using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace AvaloniauiForEasytier.Services;

/// <summary>
/// 管理同一桌面进程中的多个 EasyTier FFI 网络实例。
/// </summary>
public sealed class NetworkRuntimeManager : IAsyncDisposable
{
    private readonly object _syncRoot = new();
    private readonly SemaphoreSlim _operationGate = new(1, 1);
    private readonly Dictionary<string, EasyTierFfiRuntime> _runtimes = new(StringComparer.Ordinal);
    private bool _isDisposed; // 标记多实例管理器是否已经释放。

    /// <summary>某个网络实例的运行状态发生变化时触发。</summary>
    public event Action<string, CoreStatusChangedEventArgs>? StatusChanged;

    /// <summary>某个网络实例产生运行输出时触发。</summary>
    public event Action<string, CoreOutputEventArgs>? OutputReceived;

    /// <summary>获取 EasyTier FFI 原生库路径。</summary>
    public string NativeLibraryPath => Path.Combine(
        AppContext.BaseDirectory,
        OperatingSystem.IsWindows() ? "easytier_ffi.dll" : "libeasytier_ffi.so");

    /// <summary>获取当前正在运行的网络数量。</summary>
    public int RunningCount
    {
        get
        {
            lock (_syncRoot)
            {
                return _runtimes.Values.Count(runtime => runtime.IsRunning);
            }
        }
    }

    /// <summary>获取当前是否至少有一个网络正在运行。</summary>
    public bool IsAnyRunning => RunningCount > 0;

    /// <summary>
    /// 启动指定网络配置；其他已运行网络不会受到影响。
    /// </summary>
    /// <param name="profile">需要启动的网络配置，类型为 NetworkProfile，不可为空，必填。</param>
    /// <param name="cancellationToken">取消启动等待的令牌，类型为 CancellationToken，取值为可取消或不可取消令牌，非必填。</param>
    /// <returns>启动结果，类型为 Task&lt;bool&gt;；成功返回 true，失败返回 false。</returns>
    public async Task<bool> StartAsync(NetworkProfile profile, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ThrowIfDisposed();
        await _operationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            EasyTierFfiRuntime? previousRuntime;
            lock (_syncRoot)
            {
                _runtimes.TryGetValue(profile.InstanceName, out previousRuntime);
            }

            // 已运行的同名实例保持现状，防止重复调用 FFI。
            if (previousRuntime?.IsRunning == true)
            {
                return true;
            }

            // 失败或已停止的旧运行时不再复用，确保重新启动使用最新配置。
            if (previousRuntime is not null)
            {
                await previousRuntime.DisposeAsync().ConfigureAwait(false);
                lock (_syncRoot)
                {
                    _runtimes.Remove(profile.InstanceName);
                }
            }

            var instanceName = profile.InstanceName;
            var configuration = profile.BuildTomlConfiguration();
            var runtime = new EasyTierFfiRuntime(() => configuration, () => instanceName);
            runtime.StatusChanged += (_, eventArgs) => StatusChanged?.Invoke(instanceName, eventArgs);
            runtime.OutputReceived += (_, eventArgs) => OutputReceived?.Invoke(instanceName, eventArgs);

            lock (_syncRoot)
            {
                _runtimes[instanceName] = runtime;
            }

            return await runtime.StartAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _operationGate.Release();
        }
    }

    /// <summary>
    /// 停止指定名称的网络实例；其他网络继续运行。
    /// </summary>
    /// <param name="instanceName">实例名称，类型为字符串，取值为非空已保存名称，必填。</param>
    /// <param name="cancellationToken">取消停止等待的令牌，类型为 CancellationToken，取值为可取消或不可取消令牌，非必填。</param>
    /// <returns>异步停止任务，类型为 Task；实例不存在时正常完成。</returns>
    public async Task StopAsync(string instanceName, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        await _operationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            EasyTierFfiRuntime? runtime;
            lock (_syncRoot)
            {
                _runtimes.TryGetValue(instanceName, out runtime);
            }

            // 未由当前应用启动的实例不执行停止，避免误伤其他实例。
            if (runtime is null)
            {
                return;
            }

            await runtime.StopAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _operationGate.Release();
        }
    }

    /// <summary>
    /// 依次启动给定的全部网络配置。
    /// </summary>
    /// <param name="profiles">网络配置集合，类型为 IEnumerable&lt;NetworkProfile&gt;，不可为空，必填。</param>
    /// <param name="cancellationToken">取消启动等待的令牌，类型为 CancellationToken，取值为可取消或不可取消令牌，非必填。</param>
    /// <returns>异步启动任务，类型为 Task。</returns>
    public async Task StartAllAsync(IEnumerable<NetworkProfile> profiles, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(profiles);
        foreach (var profile in profiles)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await StartAsync(profile, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// 停止当前应用管理的全部网络实例。
    /// </summary>
    /// <param name="cancellationToken">取消停止等待的令牌，类型为 CancellationToken，取值为可取消或不可取消令牌，非必填。</param>
    /// <returns>异步停止任务，类型为 Task。</returns>
    public async Task StopAllAsync(CancellationToken cancellationToken = default)
    {
        string[] instanceNames;
        lock (_syncRoot)
        {
            instanceNames = _runtimes.Keys.ToArray();
        }

        foreach (var instanceName in instanceNames)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await StopAsync(instanceName, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// 获取指定实例的当前状态。
    /// </summary>
    /// <param name="instanceName">实例名称，类型为字符串，取值为非空名称，必填。</param>
    /// <returns>运行状态，类型为 CoreProcessStatus；未创建运行时时返回 Stopped。</returns>
    public CoreProcessStatus GetStatus(string instanceName)
    {
        lock (_syncRoot)
        {
            return _runtimes.TryGetValue(instanceName, out var runtime)
                ? runtime.Status
                : CoreProcessStatus.Stopped;
        }
    }

    /// <summary>
    /// 获取指定实例最近一次状态说明。
    /// </summary>
    /// <param name="instanceName">实例名称，类型为字符串，取值为非空名称，必填。</param>
    /// <returns>状态说明，类型为字符串；没有说明时返回 null。</returns>
    public string? GetLastMessage(string instanceName)
    {
        lock (_syncRoot)
        {
            return _runtimes.TryGetValue(instanceName, out var runtime)
                ? runtime.LastMessage
                : null;
        }
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        bool shouldDispose; // 标记本次调用是否需要执行释放流程。
        lock (_syncRoot)
        {
            shouldDispose = !_isDisposed;
        }

        // 重复释放时直接返回，避免访问已经释放的信号量。
        if (!shouldDispose)
        {
            return;
        }

        await StopAllAsync().ConfigureAwait(false);

        EasyTierFfiRuntime[] runtimes;
        lock (_syncRoot)
        {
            _isDisposed = true;
            runtimes = _runtimes.Values.ToArray();
            _runtimes.Clear();
        }

        foreach (var runtime in runtimes)
        {
            await runtime.DisposeAsync().ConfigureAwait(false);
        }

        _operationGate.Dispose();
    }

    /// <summary>
    /// 检查多实例管理器是否已经释放。
    /// </summary>
    private void ThrowIfDisposed()
    {
        lock (_syncRoot)
        {
            // 释放后禁止再次调用 FFI 或同步资源。
            if (_isDisposed)
            {
                throw new ObjectDisposedException(nameof(NetworkRuntimeManager));
            }
        }
    }
}
