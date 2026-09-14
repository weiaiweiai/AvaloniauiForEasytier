using System;
using System.Threading;
using System.Threading.Tasks;

namespace AvaloniauiForEasytier.Services;

/// <summary>
/// 定义 EasyTier 运行时的统一控制接口。
/// </summary>
public interface IEasyTierRuntime : IAsyncDisposable
{
    /// <summary>运行状态变化事件。</summary>
    event EventHandler<CoreStatusChangedEventArgs>? StatusChanged;

    /// <summary>运行时输出事件。</summary>
    event EventHandler<CoreOutputEventArgs>? OutputReceived;

    /// <summary>获取当前运行状态。</summary>
    CoreProcessStatus Status { get; }

    /// <summary>获取当前是否正在运行。</summary>
    bool IsRunning { get; }

    /// <summary>获取当前原生库或 Core 可执行文件路径。</summary>
    string? ExecutablePath { get; }

    /// <summary>获取最近一次状态说明。</summary>
    string? LastMessage { get; }

    /// <summary>
    /// 启动 EasyTier 运行时。
    /// </summary>
    /// <param name="cancellationToken">取消启动等待的令牌，类型为 CancellationToken，取值为可取消或不可取消令牌，非必填。</param>
    /// <returns>启动结果，类型为 Task&lt;bool&gt;；成功返回 true，失败返回 false。</returns>
    Task<bool> StartAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 停止 EasyTier 运行时。
    /// </summary>
    /// <param name="cancellationToken">取消停止等待的令牌，类型为 CancellationToken，取值为可取消或不可取消令牌，非必填。</param>
    /// <returns>异步停止任务，类型为 Task；运行时未启动时也会正常完成。</returns>
    Task StopAsync(CancellationToken cancellationToken = default);
}
