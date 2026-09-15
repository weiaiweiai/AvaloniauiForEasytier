using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NLog;

namespace AvaloniauiForEasytier.Services;

/// <summary>
/// EasyTier Core 进程状态。
/// </summary>
public enum CoreProcessStatus
{
    /// <summary>进程未启动。</summary>
    Stopped,

    /// <summary>进程正在启动。</summary>
    Starting,

    /// <summary>进程正在运行。</summary>
    Running,

    /// <summary>进程正在停止。</summary>
    Stopping,

    /// <summary>进程启动或运行失败。</summary>
    Failed
}

/// <summary>
/// EasyTier Core 输出来源。
/// </summary>
public enum CoreOutputKind
{
    /// <summary>标准输出。</summary>
    StandardOutput,

    /// <summary>错误输出。</summary>
    StandardError,

    /// <summary>控制台生成的系统信息。</summary>
    System
}

/// <summary>
/// 表示 Core 输出的一行文本。
/// </summary>
public sealed class CoreOutputEventArgs : EventArgs
{
    /// <summary>
    /// 初始化 Core 输出事件参数。
    /// </summary>
    /// <param name="kind">输出来源，类型为 CoreOutputKind，取值为 StandardOutput、StandardError 或 System，必填。</param>
    /// <param name="message">输出内容，类型为字符串，取值为任意非空文本，必填。</param>
    public CoreOutputEventArgs(CoreOutputKind kind, string message)
    {
        Kind = kind;
        Message = message;
        Timestamp = DateTimeOffset.Now;
    }

    /// <summary>获取输出来源。</summary>
    public CoreOutputKind Kind { get; }

    /// <summary>获取输出文本。</summary>
    public string Message { get; }

    /// <summary>获取输出产生的本地时间。</summary>
    public DateTimeOffset Timestamp { get; }
}

/// <summary>
/// 表示 Core 进程状态变化。
/// </summary>
public sealed class CoreStatusChangedEventArgs : EventArgs
{
    /// <summary>
    /// 初始化 Core 状态变化事件参数。
    /// </summary>
    /// <param name="status">新的进程状态，类型为 CoreProcessStatus，取值为枚举定义的状态，必填。</param>
    /// <param name="executablePath">当前使用的可执行文件路径，类型为字符串，可为空，非必填。</param>
    /// <param name="message">状态说明，类型为字符串，可为空，非必填。</param>
    public CoreStatusChangedEventArgs(CoreProcessStatus status, string? executablePath, string? message)
    {
        Status = status;
        ExecutablePath = executablePath;
        Message = message;
    }

    /// <summary>获取新的进程状态。</summary>
    public CoreProcessStatus Status { get; }

    /// <summary>获取当前使用的可执行文件路径。</summary>
    public string? ExecutablePath { get; }

    /// <summary>获取状态说明。</summary>
    public string? Message { get; }
}

/// <summary>
/// 管理 EasyTier Core 的跨平台进程生命周期和输出流。
/// </summary>
public sealed class CoreProcessManager : IEasyTierRuntime
{
    private static readonly Logger Logger = ApplicationLogging.GetLogger(nameof(CoreProcessManager));
    private readonly object _syncRoot = new();
    private Process? _process;
    private CancellationTokenSource? _lifetimeCancellation;
    private CoreProcessStatus _status = CoreProcessStatus.Stopped;
    private string? _executablePath;
    private string? _lastMessage;
    private bool _isDisposed; // 标记管理器是否已经释放，避免释放后再次启动进程。

    /// <summary>
    /// Core 状态变化事件。
    /// </summary>
    public event EventHandler<CoreStatusChangedEventArgs>? StatusChanged;

    /// <summary>
    /// Core 输出事件。
    /// </summary>
    public event EventHandler<CoreOutputEventArgs>? OutputReceived;

    /// <summary>
    /// 获取当前 Core 进程状态。
    /// </summary>
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

    /// <summary>
    /// 获取当前是否存在运行中的 Core 进程。
    /// </summary>
    public bool IsRunning
    {
        get
        {
            lock (_syncRoot)
            {
                return _process is not null && !_process.HasExited;
            }
        }
    }

    /// <summary>
    /// 获取当前 Core 可执行文件路径。
    /// </summary>
    public string? ExecutablePath
    {
        get
        {
            lock (_syncRoot)
            {
                return _executablePath;
            }
        }
    }

    /// <summary>
    /// 获取最近一次状态说明。
    /// </summary>
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

    /// <summary>
    /// 启动 EasyTier Core。
    /// </summary>
    /// <param name="executablePath">Core 可执行文件路径，类型为字符串，可为空；为空时按应用目录、当前目录和系统 PATH 查找，非必填。</param>
    /// <param name="arguments">Core 启动参数，类型为只读字符串列表，可为空；当前未配置时传入空集合，非必填。</param>
    /// <param name="cancellationToken">取消启动等待的令牌，类型为 CancellationToken，取值为可取消或不可取消令牌，非必填。</param>
    /// <returns>启动结果，类型为 Task&lt;bool&gt;；启动成功返回 true，找不到文件或启动异常返回 false。</returns>
    public async Task<bool> StartAsync(
        string? executablePath = null,
        IReadOnlyList<string>? arguments = null,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        lock (_syncRoot)
        {
            // 已经处于启动或运行状态时不重复创建 Core 进程。
            if (_status is CoreProcessStatus.Starting or CoreProcessStatus.Running)
            {
                return true;
            }
        }

        SetStatus(CoreProcessStatus.Starting, null, "正在启动 EasyTier Core");
        cancellationToken.ThrowIfCancellationRequested();

        var resolvedPath = ResolveExecutablePath(executablePath);
        if (resolvedPath is null)
        {
            const string message = "未找到 EasyTier Core，请将 easytier-core 放入应用目录或配置到系统 PATH。";
            PublishOutput(CoreOutputKind.System, message);
            SetStatus(CoreProcessStatus.Failed, null, message);
            return false;
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = resolvedPath,
            WorkingDirectory = Path.GetDirectoryName(resolvedPath) ?? AppContext.BaseDirectory,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        foreach (var argument in arguments ?? Array.Empty<string>())
        {
            startInfo.ArgumentList.Add(argument);
        }

        var process = new Process
        {
            StartInfo = startInfo,
            EnableRaisingEvents = true
        };
        process.Exited += Process_Exited;

        lock (_syncRoot)
        {
            _process = process;
            _executablePath = resolvedPath;
            _lifetimeCancellation = new CancellationTokenSource();
        }

        try
        {
            // 启动失败时统一进入 Failed 状态并释放当前 Process 对象。
            if (!process.Start())
            {
                const string message = "EasyTier Core 启动失败，操作系统未创建进程。";
                PublishOutput(CoreOutputKind.System, message);
                SetStatus(CoreProcessStatus.Failed, resolvedPath, message);
                await CleanupProcessAsync(process).ConfigureAwait(false);
                return false;
            }
        }
        catch (Exception exception) when (exception is InvalidOperationException or System.ComponentModel.Win32Exception or UnauthorizedAccessException)
        {
            var message = $"EasyTier Core 启动失败：{exception.Message}";
            PublishOutput(CoreOutputKind.System, message);
            SetStatus(CoreProcessStatus.Failed, resolvedPath, message);
            await CleanupProcessAsync(process).ConfigureAwait(false);
            return false;
        }

        SetStatus(CoreProcessStatus.Running, resolvedPath, "EasyTier Core 已启动");
        PublishOutput(CoreOutputKind.System, $"已启动 EasyTier Core：{resolvedPath}");

        var lifetimeToken = _lifetimeCancellation?.Token ?? CancellationToken.None;
        _ = ReadOutputAsync(process, process.StandardOutput, CoreOutputKind.StandardOutput, lifetimeToken);
        _ = ReadOutputAsync(process, process.StandardError, CoreOutputKind.StandardError, lifetimeToken);
        return true;
    }

    /// <summary>
    /// 以统一运行时接口启动 EasyTier Core。
    /// </summary>
    /// <param name="cancellationToken">取消启动等待的令牌，类型为 CancellationToken，取值为可取消或不可取消令牌，非必填。</param>
    /// <returns>启动结果，类型为 Task&lt;bool&gt;；启动成功返回 true，失败返回 false。</returns>
    Task<bool> IEasyTierRuntime.StartAsync(CancellationToken cancellationToken)
    {
        return StartAsync(cancellationToken: cancellationToken);
    }

    /// <summary>
    /// 停止当前 EasyTier Core 进程。
    /// </summary>
    /// <param name="cancellationToken">取消停止等待的令牌，类型为 CancellationToken，取值为可取消或不可取消令牌，非必填。</param>
    /// <returns>异步停止任务，类型为 Task；进程不存在时也会正常完成。</returns>
    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        Process? process;
        lock (_syncRoot)
        {
            process = _process;
            // 没有活动进程时只同步状态，不执行停止操作。
            if (process is not null)
            {
                _status = CoreProcessStatus.Stopping;
                _lastMessage = "正在停止 EasyTier Core";
            }
        }

        if (process is null)
        {
            SetStatus(CoreProcessStatus.Stopped, _executablePath, "EasyTier Core 未运行");
            return;
        }

        PublishStatusSnapshot();

        try
        {
            // 使用整个进程树终止，避免 Core 派生的工作进程残留。
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }

            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
            PublishOutput(CoreOutputKind.System, "EasyTier Core 已停止");
        }
        catch (InvalidOperationException)
        {
            // 进程已在停止过程中退出时，按已停止处理。
            PublishOutput(CoreOutputKind.System, "EasyTier Core 已退出");
        }
        finally
        {
            await CleanupProcessAsync(process).ConfigureAwait(false);
            SetStatus(CoreProcessStatus.Stopped, _executablePath, "EasyTier Core 未运行");
        }
    }

    /// <summary>
    /// 重启 EasyTier Core 进程。
    /// </summary>
    /// <param name="executablePath">Core 可执行文件路径，类型为字符串，可为空；为空时自动查找，非必填。</param>
    /// <param name="arguments">Core 启动参数，类型为只读字符串列表，可为空；没有参数时传入空集合，非必填。</param>
    /// <param name="cancellationToken">取消重启等待的令牌，类型为 CancellationToken，取值为可取消或不可取消令牌，非必填。</param>
    /// <returns>重启结果，类型为 Task&lt;bool&gt;；启动成功返回 true，否则返回 false。</returns>
    public async Task<bool> RestartAsync(
        string? executablePath = null,
        IReadOnlyList<string>? arguments = null,
        CancellationToken cancellationToken = default)
    {
        await StopAsync(cancellationToken).ConfigureAwait(false);
        return await StartAsync(executablePath, arguments, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// 释放管理器并停止仍在运行的 Core 进程。
    /// </summary>
    /// <returns>异步释放任务，类型为 ValueTask。</returns>
    public async ValueTask DisposeAsync()
    {
        bool shouldStop; // 标记是否存在需要停止的 Core 进程。
        lock (_syncRoot)
        {
            shouldStop = !_isDisposed && _process is not null;
            _isDisposed = true;
        }

        if (shouldStop)
        {
            await StopAsync().ConfigureAwait(false);
        }

        lock (_syncRoot)
        {
            _lifetimeCancellation?.Dispose();
            _lifetimeCancellation = null;
        }
    }

    /// <summary>
    /// 读取 Core 的一个重定向输出流。
    /// </summary>
    /// <param name="process">正在运行的进程，类型为 Process，不可为空，必填。</param>
    /// <param name="reader">进程输出读取器，类型为 StreamReader，不可为空，必填。</param>
    /// <param name="kind">输出来源，类型为 CoreOutputKind，取值为 StandardOutput 或 StandardError，必填。</param>
    /// <param name="cancellationToken">进程生命周期取消令牌，类型为 CancellationToken，取值为可取消令牌，必填。</param>
    /// <returns>异步读取任务，类型为 Task。</returns>
    private async Task ReadOutputAsync(Process process, StreamReader reader, CoreOutputKind kind, CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var line = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);
                if (line is null)
                {
                    break;
                }

                if (line.Length > 0)
                {
                    PublishOutput(kind, line);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // 停止进程时取消读取属于正常流程，不额外记录错误。
        }
        catch (ObjectDisposedException)
        {
            // 进程清理后读取器被释放属于正常流程。
        }
    }

    /// <summary>
    /// 响应 Core 进程退出事件。
    /// </summary>
    /// <param name="sender">触发事件的进程对象，类型为对象，可为空，非必填。</param>
    /// <param name="e">进程事件参数，类型为 EventArgs，不可为空，必填。</param>
    private void Process_Exited(object? sender, EventArgs e)
    {
        if (sender is Process process)
        {
            _ = HandleProcessExitedAsync(process);
        }
    }

    /// <summary>
    /// 处理 Core 进程自然退出或异常退出。
    /// </summary>
    /// <param name="process">已退出的进程，类型为 Process，不可为空，必填。</param>
    /// <returns>异步退出处理任务，类型为 Task。</returns>
    private async Task HandleProcessExitedAsync(Process process)
    {
        try
        {
            await process.WaitForExitAsync().ConfigureAwait(false);
        }
        catch (InvalidOperationException)
        {
            return;
        }

        lock (_syncRoot)
        {
            // 忽略旧进程的退出回调，避免覆盖新启动进程的状态。
            if (!ReferenceEquals(_process, process))
            {
                return;
            }

            _process = null;
            _lifetimeCancellation?.Cancel();
        }

        var exitCode = process.ExitCode;
        var message = exitCode == 0
            ? "EasyTier Core 已退出"
            : $"EasyTier Core 异常退出，退出代码：{exitCode}";
        PublishOutput(CoreOutputKind.System, message);
        SetStatus(exitCode == 0 ? CoreProcessStatus.Stopped : CoreProcessStatus.Failed, _executablePath, message);
        process.Dispose();
    }

    /// <summary>
    /// 清理指定进程和对应的读取取消令牌。
    /// </summary>
    /// <param name="process">需要清理的进程，类型为 Process，可为空，非必填。</param>
    /// <returns>异步清理任务，类型为 Task。</returns>
    private async Task CleanupProcessAsync(Process? process)
    {
        CancellationTokenSource? cancellation;
        lock (_syncRoot)
        {
            if (ReferenceEquals(_process, process))
            {
                _process = null;
            }

            cancellation = _lifetimeCancellation;
            _lifetimeCancellation = null;
        }

        cancellation?.Cancel();
        cancellation?.Dispose();
        if (process is not null)
        {
            process.Exited -= Process_Exited;
            process.Dispose();
        }

        await Task.CompletedTask.ConfigureAwait(false);
    }

    /// <summary>
    /// 在应用目录、当前目录和系统 PATH 中查找 Core 可执行文件。
    /// </summary>
    /// <param name="requestedPath">用户指定的路径，类型为字符串，可为空；为空时执行默认查找，非必填。</param>
    /// <returns>可执行文件完整路径，类型为字符串；找不到时返回 null。</returns>
    private static string? ResolveExecutablePath(string? requestedPath)
    {
        if (!string.IsNullOrWhiteSpace(requestedPath))
        {
            var explicitPath = ToFullPath(requestedPath);
            if (explicitPath is not null && File.Exists(explicitPath))
            {
                return explicitPath;
            }
        }

        var executableNames = OperatingSystem.IsWindows()
            ? new[] { "easytier-core.exe", "easytier-core" }
            : new[] { "easytier-core", "easytier-core.exe" };
        var searchDirectories = new[]
        {
            AppContext.BaseDirectory,
            Environment.CurrentDirectory
        };

        foreach (var directory in searchDirectories)
        {
            foreach (var executableName in executableNames)
            {
                var candidate = Path.Combine(directory, executableName);
                if (File.Exists(candidate))
                {
                    return Path.GetFullPath(candidate);
                }
            }
        }

        var pathVariable = Environment.GetEnvironmentVariable("PATH");
        if (!string.IsNullOrWhiteSpace(pathVariable))
        {
            foreach (var directory in pathVariable.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries).Distinct(StringComparer.OrdinalIgnoreCase))
            {
                foreach (var executableName in executableNames)
                {
                    var candidate = Path.Combine(directory, executableName);
                    if (File.Exists(candidate))
                    {
                        return Path.GetFullPath(candidate);
                    }
                }
            }
        }

        return null;
    }

    /// <summary>
    /// 将可能为相对路径的文本转换为完整路径。
    /// </summary>
    /// <param name="path">待转换路径，类型为字符串，不可为空，必填。</param>
    /// <returns>完整路径，类型为字符串；路径格式无效时返回 null。</returns>
    private static string? ToFullPath(string path)
    {
        try
        {
            return Path.GetFullPath(path);
        }
        catch (ArgumentException)
        {
            return null;
        }
        catch (NotSupportedException)
        {
            return null;
        }
    }

    /// <summary>
    /// 发布进程状态事件的当前快照。
    /// </summary>
    private void PublishStatusSnapshot()
    {
        CoreProcessStatus status;
        string? path;
        string? message;
        lock (_syncRoot)
        {
            status = _status;
            path = _executablePath;
            message = _lastMessage;
        }

        StatusChanged?.Invoke(this, new CoreStatusChangedEventArgs(status, path, message));
    }

    /// <summary>
    /// 更新并发布进程状态。
    /// </summary>
    /// <param name="status">新的进程状态，类型为 CoreProcessStatus，取值为枚举定义的状态，必填。</param>
    /// <param name="path">Core 可执行文件路径，类型为字符串，可为空，非必填。</param>
    /// <param name="message">状态说明，类型为字符串，可为空，非必填。</param>
    private void SetStatus(CoreProcessStatus status, string? path, string? message)
    {
        lock (_syncRoot)
        {
            _status = status;
            _executablePath = path ?? _executablePath;
            _lastMessage = message;
        }

        StatusChanged?.Invoke(this, new CoreStatusChangedEventArgs(status, ExecutablePath, message));
    }

    /// <summary>
    /// 发布一行 Core 输出。
    /// </summary>
    /// <param name="kind">输出来源，类型为 CoreOutputKind，取值为 StandardOutput、StandardError 或 System，必填。</param>
    /// <param name="message">输出文本，类型为字符串，取值为任意非空文本，必填。</param>
    private void PublishOutput(CoreOutputKind kind, string message)
    {
        // 运行时输出同时写入统一日志文件，确保界面日志与持久化日志来源一致。
        if (kind == CoreOutputKind.StandardError)
        {
            Logger.Error("EasyTier Core：{0}", message);
        }
        else
        {
            Logger.Info("EasyTier Core：{0}", message);
        }

        OutputReceived?.Invoke(this, new CoreOutputEventArgs(kind, message));
    }

    /// <summary>
    /// 检查管理器是否已经释放。
    /// </summary>
    private void ThrowIfDisposed()
    {
        lock (_syncRoot)
        {
            // 释放后禁止再次启动进程，避免使用已失效的取消令牌。
            if (_isDisposed)
            {
                throw new ObjectDisposedException(nameof(CoreProcessManager));
            }
        }
    }
}
