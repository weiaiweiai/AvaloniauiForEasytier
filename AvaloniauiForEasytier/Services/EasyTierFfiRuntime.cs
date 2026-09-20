using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using NLog;

namespace AvaloniauiForEasytier.Services;

/// <summary>
/// 通过 EasyTier FFI 管理本机网络实例。
/// </summary>
public sealed class EasyTierFfiRuntime : IEasyTierRuntime
{
    private static readonly Logger Logger = ApplicationLogging.GetLogger(nameof(EasyTierFfiRuntime));
    private readonly object _syncRoot = new();
    private readonly SemaphoreSlim _operationGate = new(1, 1);
    private readonly Func<string> _configurationProvider;
    private readonly Func<string> _instanceNameProvider;
    private readonly string _nativeLibraryPath;
    private CoreProcessStatus _status = CoreProcessStatus.Stopped;
    private string? _lastMessage;
    private string? _activeInstanceName;
    private bool _isDisposed; // 标记运行时是否已经释放，避免释放后继续调用 FFI。

    /// <summary>
    /// 初始化 FFI 运行时。
    /// </summary>
    /// <param name="configurationProvider">配置文本提供函数，类型为 Func&lt;string&gt;，返回完整 TOML 配置，必填。</param>
    /// <param name="instanceNameProvider">实例名称提供函数，类型为 Func&lt;string&gt;，返回用于停止实例的名称，必填。</param>
    public EasyTierFfiRuntime(Func<string> configurationProvider, Func<string> instanceNameProvider)
    {
        _configurationProvider = configurationProvider ?? throw new ArgumentNullException(nameof(configurationProvider));
        _instanceNameProvider = instanceNameProvider ?? throw new ArgumentNullException(nameof(instanceNameProvider));
        _nativeLibraryPath = Path.Combine(
            AppContext.BaseDirectory,
            OperatingSystem.IsWindows() ? "easytier_ffi.dll" : "libeasytier_ffi.so");
    }

    /// <inheritdoc />
    public event EventHandler<CoreStatusChangedEventArgs>? StatusChanged;

    /// <inheritdoc />
    public event EventHandler<CoreOutputEventArgs>? OutputReceived;

    /// <inheritdoc />
    public CoreProcessStatus Status
    {
        get
        {
            lock (_syncRoot)
            {
                return _status;
            }
        }
    }

    /// <inheritdoc />
    public bool IsRunning => Status == CoreProcessStatus.Running;

    /// <inheritdoc />
    public string? ExecutablePath => _nativeLibraryPath;

    /// <inheritdoc />
    public string? LastMessage
    {
        get
        {
            lock (_syncRoot)
            {
                return _lastMessage;
            }
        }
    }

    /// <inheritdoc />
    public async Task<bool> StartAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        await _operationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (IsRunning)
            {
                Logger.Debug("EasyTier FFI 已经处于运行状态，跳过重复启动");
                return true;
            }

            SetStatus(CoreProcessStatus.Starting, "正在启动 EasyTier FFI 实例");
            cancellationToken.ThrowIfCancellationRequested();

            string config;
            string instanceName;
            try
            {
                config = _configurationProvider();
                instanceName = _instanceNameProvider();
            }
            catch (Exception exception)
            {
                return FailStart($"读取网络配置失败：{exception.Message}", exception);
            }

            if (string.IsNullOrWhiteSpace(config) || string.IsNullOrWhiteSpace(instanceName))
            {
                return FailStart("网络配置或实例名称为空，无法启动 EasyTier。");
            }

            // 在同一线程中完成解析、启动和错误读取，保证 FFI 的线程本地错误信息有效。
            var result = await Task.Run(() => ParseAndRun(config), cancellationToken).ConfigureAwait(false);
            if (!result.Success)
            {
                return FailStart(result.ErrorMessage);
            }

            lock (_syncRoot)
            {
                _activeInstanceName = instanceName;
            }

            SetStatus(CoreProcessStatus.Running, "EasyTier FFI 实例已启动");
            PublishOutput(CoreOutputKind.System, $"已启动 EasyTier FFI 实例：{instanceName}");
            Logger.Info("EasyTier FFI 实例已启动：{0}", instanceName);
            return true;
        }
        catch (OperationCanceledException)
        {
            SetStatus(CoreProcessStatus.Stopped, "启动操作已取消");
            Logger.Warn("EasyTier FFI 启动操作已取消");
            throw;
        }
        catch (Exception exception) when (exception is DllNotFoundException or EntryPointNotFoundException or BadImageFormatException)
        {
            return FailStart($"加载 EasyTier FFI 失败：{exception.Message}", exception);
        }
        catch (Exception exception)
        {
            return FailStart($"EasyTier FFI 启动发生未预期异常：{exception.Message}", exception);
        }
        finally
        {
            _operationGate.Release();
        }
    }

    /// <inheritdoc />
    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        await _operationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!IsRunning)
            {
                SetStatus(CoreProcessStatus.Stopped, "EasyTier FFI 未运行");
                Logger.Debug("EasyTier FFI 未运行，跳过停止操作");
                return;
            }

            SetStatus(CoreProcessStatus.Stopping, "正在停止 EasyTier FFI 实例");
            var instanceName = GetActiveInstanceName();
            if (instanceName is null)
            {
                const string message = "未找到正在运行的 EasyTier 实例名称。";
                SetStatus(CoreProcessStatus.Failed, message);
                PublishOutput(CoreOutputKind.StandardError, message);
                Logger.Error(message);
                return;
            }

            var result = await Task.Run(() => DeleteInstance(instanceName), cancellationToken).ConfigureAwait(false);
            if (!result.Success)
            {
                var message = result.ErrorMessage;
                SetStatus(CoreProcessStatus.Failed, message);
                PublishOutput(CoreOutputKind.StandardError, message);
                Logger.Error(message);
                return;
            }

            lock (_syncRoot)
            {
                _activeInstanceName = null;
            }

            SetStatus(CoreProcessStatus.Stopped, "EasyTier FFI 实例已停止");
            PublishOutput(CoreOutputKind.System, $"已停止 EasyTier FFI 实例：{instanceName}");
            Logger.Info("EasyTier FFI 实例已停止：{0}", instanceName);
        }
        catch (OperationCanceledException)
        {
            Logger.Warn("EasyTier FFI 停止操作已取消");
            throw;
        }
        catch (Exception exception)
        {
            // 停止过程异常属于关键故障，记录完整堆栈并同步失败状态。
            const string message = "EasyTier FFI 停止发生未预期异常。";
            SetStatus(CoreProcessStatus.Failed, message);
            PublishOutput(CoreOutputKind.StandardError, $"{message} {exception.Message}");
            Logger.Error(exception, message);
        }
        finally
        {
            _operationGate.Release();
        }
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        bool shouldStop; // 标记释放前是否存在运行中的 FFI 实例。
        lock (_syncRoot)
        {
            shouldStop = !_isDisposed && _status == CoreProcessStatus.Running;
        }

        if (shouldStop)
        {
            await StopAsync().ConfigureAwait(false);
        }

        lock (_syncRoot)
        {
            _isDisposed = true;
        }

        _operationGate.Dispose();
    }

    /// <summary>
    /// 采集当前实例的节点和路由运行快照。
    /// </summary>
    /// <returns>网络运行快照，类型为 EasyTierNetworkSnapshot；实例未运行、未上报数据或采集失败时返回 null。</returns>
    public EasyTierNetworkSnapshot? CollectNetworkSnapshot()
    {
        ThrowIfDisposed();
        var instanceName = GetActiveInstanceName();
        if (instanceName is null)
        {
            return null;
        }

        try
        {
            var infos = CollectNetworkInfos();

            // FFI 返回全部实例的信息，仅保留当前活动实例的数据。
            return infos.TryGetValue(instanceName, out var json)
                ? EasyTierNetworkSnapshotParser.Parse(instanceName, json)
                : null;
        }
        catch (Exception exception)
        {
            Logger.Warn(exception, "采集 EasyTier 网络快照失败：{0}", instanceName);
            return null;
        }
    }

    /// <summary>
    /// 调用 FFI 采集全部实例的运行信息。
    /// </summary>
    /// <returns>实例名称到运行信息 JSON 的映射，类型为 Dictionary&lt;string, string&gt;。</returns>
    /// <exception cref="InvalidOperationException">FFI 返回错误时抛出，异常消息为 FFI 错误文本。</exception>
    private static Dictionary<string, string> CollectNetworkInfos()
    {
        const int maxInstanceCount = 64; // 单次采集的实例数量上限，桌面场景远够使用。
        var pairSize = Marshal.SizeOf<NativeKeyValuePair>();
        var buffer = Marshal.AllocHGlobal(pairSize * maxInstanceCount);
        try
        {
            var count = EasyTierNative.CollectNetworkInfos(buffer, maxInstanceCount);
            if (count < 0)
            {
                throw new InvalidOperationException(GetErrorMessage() ?? $"采集网络信息失败，返回码：{count}");
            }

            var result = new Dictionary<string, string>(count, StringComparer.Ordinal);
            for (var index = 0; index < count; index++)
            {
                var pair = Marshal.PtrToStructure<NativeKeyValuePair>(buffer + index * pairSize);
                try
                {
                    var key = Marshal.PtrToStringUTF8(pair.Key);
                    var value = Marshal.PtrToStringUTF8(pair.Value);

                    // 键值任一为空时跳过该条目，保证字典中只有完整数据。
                    if (key is not null && value is not null)
                    {
                        result[key] = value;
                    }
                }
                finally
                {
                    // FFI 通过 CString::into_raw 分配的字符串必须用 free_string 归还。
                    EasyTierNative.FreeString(pair.Key);
                    EasyTierNative.FreeString(pair.Value);
                }
            }

            return result;
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    /// <summary>
    /// 在同一工作线程中解析并启动配置。
    /// </summary>
    /// <param name="config">完整 TOML 配置，类型为字符串，取值为合法 EasyTier 配置文本，必填。</param>
    /// <returns>启动结果，类型为 FfiCallResult，包含成功标记和错误文本。</returns>
    private static FfiCallResult ParseAndRun(string config)
    {
        var parseResult = EasyTierNative.ParseConfig(config);
        if (parseResult != 0)
        {
            return new FfiCallResult(false, GetErrorMessage() ?? $"配置解析失败，返回码：{parseResult}");
        }

        var runResult = EasyTierNative.RunNetworkInstance(config);
        return runResult == 0
            ? new FfiCallResult(true, string.Empty)
            : new FfiCallResult(false, GetErrorMessage() ?? $"启动实例失败，返回码：{runResult}");
    }

    /// <summary>
    /// 删除指定名称的网络实例。
    /// </summary>
    /// <param name="instanceName">实例名称，类型为字符串，取值为非空 UTF-8 文本，必填。</param>
    /// <returns>停止结果，类型为 FfiCallResult，包含返回状态和错误文本。</returns>
    private static FfiCallResult DeleteInstance(string instanceName)
    {
        var namePointer = Marshal.StringToCoTaskMemUTF8(instanceName);
        var namesPointer = Marshal.AllocHGlobal(IntPtr.Size);
        try
        {
            Marshal.WriteIntPtr(namesPointer, namePointer);
            var result = EasyTierNative.DeleteNetworkInstance(namesPointer, 1);
            return result == 0
                ? new FfiCallResult(true, string.Empty)
                : new FfiCallResult(false, GetErrorMessage() ?? $"停止实例失败，返回码：{result}");
        }
        finally
        {
            Marshal.FreeHGlobal(namesPointer);
            Marshal.FreeCoTaskMem(namePointer);
        }
    }

    /// <summary>
    /// 读取当前线程上的 FFI 错误文本。
    /// </summary>
    /// <returns>错误文本，类型为字符串；没有错误时返回 null。</returns>
    private static string? GetErrorMessage()
    {
        EasyTierNative.GetErrorMessage(out var messagePointer);
        if (messagePointer == IntPtr.Zero)
        {
            return null;
        }

        try
        {
            return Marshal.PtrToStringUTF8(messagePointer);
        }
        finally
        {
            EasyTierNative.FreeString(messagePointer);
        }
    }

    /// <summary>
    /// 发布启动失败状态并返回失败结果。
    /// </summary>
    /// <param name="message">失败说明，类型为字符串，取值为非空文本，必填。</param>
    /// <param name="exception">导致失败的异常，类型为 Exception，可为空；提供时会保存完整堆栈，非必填。</param>
    /// <returns>启动结果，类型为 bool，固定为 false。</returns>
    private bool FailStart(string message, Exception? exception = null)
    {
        SetStatus(CoreProcessStatus.Failed, message);
        PublishOutput(CoreOutputKind.StandardError, message);
        if (exception is null)
        {
            Logger.Error("EasyTier FFI 启动失败：{0}", message);
        }
        else
        {
            Logger.Error(exception, "EasyTier FFI 启动失败：{0}", message);
        }
        return false;
    }

    /// <summary>
    /// 更新并发布运行状态。
    /// </summary>
    /// <param name="status">新的运行状态，类型为 CoreProcessStatus，取值为枚举定义的状态，必填。</param>
    /// <param name="message">状态说明，类型为字符串，取值为非空文本，必填。</param>
    private void SetStatus(CoreProcessStatus status, string message)
    {
        lock (_syncRoot)
        {
            _status = status;
            _lastMessage = message;
        }

        StatusChanged?.Invoke(this, new CoreStatusChangedEventArgs(status, _nativeLibraryPath, message));
    }

    /// <summary>
    /// 获取成功启动时锁存的实例名称。
    /// </summary>
    /// <returns>活动实例名称，类型为字符串；没有活动实例时返回 null。</returns>
    private string? GetActiveInstanceName()
    {
        lock (_syncRoot)
        {
            return _activeInstanceName;
        }
    }

    /// <summary>
    /// 发布一条运行时输出。
    /// </summary>
    /// <param name="kind">输出来源，类型为 CoreOutputKind，取值为 StandardOutput、StandardError 或 System，必填。</param>
    /// <param name="message">输出内容，类型为字符串，取值为非空文本，必填。</param>
    private void PublishOutput(CoreOutputKind kind, string message)
    {
        OutputReceived?.Invoke(this, new CoreOutputEventArgs(kind, message));
    }

    /// <summary>
    /// 检查运行时是否已经释放。
    /// </summary>
    private void ThrowIfDisposed()
    {
        lock (_syncRoot)
        {
            // 释放后禁止再次调用原生库，避免使用已关闭的同步资源。
            if (_isDisposed)
            {
                throw new ObjectDisposedException(nameof(EasyTierFfiRuntime));
            }
        }
    }

    private readonly record struct FfiCallResult(bool Success, string ErrorMessage);
}
