using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using AvaloniauiForEasytier.Services;
using System;

namespace AvaloniauiForEasytier.Views;

public partial class LogsView : UserControl
{
    private readonly IEasyTierRuntime _runtime;

    /// <summary>
    /// 初始化设计器使用的运行日志视图。
    /// </summary>
    public LogsView()
        : this(new CoreProcessManager())
    {
    }

    /// <summary>
    /// 初始化运行日志视图并订阅 Core 输出。
    /// </summary>
    /// <param name="runtime">EasyTier 运行时，类型为 IEasyTierRuntime，不可为空，必填。</param>
    public LogsView(IEasyTierRuntime runtime)
    {
        _runtime = runtime;
        InitializeComponent();
        ClearLogsButton.Click += ClearLogsButton_Click;
        _runtime.OutputReceived += CoreProcessManager_OutputReceived;
        _runtime.StatusChanged += CoreProcessManager_StatusChanged;
        UpdateServiceStatus(_runtime.Status);
    }

    /// <summary>
    /// 接收 Core 输出并切换到 Avalonia UI 线程追加日志行。
    /// </summary>
    /// <param name="sender">触发事件的 Core 管理器，类型为对象，可为空，非必填。</param>
    /// <param name="e">输出事件参数，类型为 CoreOutputEventArgs，不可为空，必填。</param>
    private void CoreProcessManager_OutputReceived(object? sender, CoreOutputEventArgs e)
    {
        Dispatcher.UIThread.Post(() => AppendLogLine(e));
    }

    /// <summary>
    /// 响应 Core 状态变化并刷新日志页状态提示。
    /// </summary>
    /// <param name="sender">触发事件的 Core 管理器，类型为对象，可为空，非必填。</param>
    /// <param name="e">状态变化参数，类型为 CoreStatusChangedEventArgs，不可为空，必填。</param>
    private void CoreProcessManager_StatusChanged(object? sender, CoreStatusChangedEventArgs e)
    {
        Dispatcher.UIThread.Post(() => UpdateServiceStatus(e.Status));
    }

    /// <summary>
    /// 清空当前日志输出窗口。
    /// </summary>
    /// <param name="sender">触发事件的清空按钮，类型为对象，可为空，非必填。</param>
    /// <param name="e">路由事件参数，类型为 RoutedEventArgs，不可为空，必填。</param>
    private void ClearLogsButton_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        LogOutputPanel.Children.Clear();
    }

    /// <summary>
    /// 向日志输出窗口追加一行文本。
    /// </summary>
    /// <param name="eventArgs">输出事件参数，类型为 CoreOutputEventArgs，不可为空，必填。</param>
    private void AppendLogLine(CoreOutputEventArgs eventArgs)
    {
        var levelText = eventArgs.Kind switch
        {
            CoreOutputKind.StandardError => "ERROR",
            CoreOutputKind.System => "SYSTEM",
            _ => "INFO"
        };
        var line = new TextBlock
        {
            FontFamily = new FontFamily("Consolas"),
            FontSize = 11,
            Foreground = new SolidColorBrush(eventArgs.Kind == CoreOutputKind.StandardError
                ? Color.Parse("#D92D20")
                : Color.Parse("#667085")),
            Text = $"[{eventArgs.Timestamp:HH:mm:ss.fff}] {levelText,-6} {eventArgs.Message}"
        };
        LogOutputPanel.Children.Add(line);

        // 限制日志控件数量，避免长时间运行造成界面控件无限增长。
        const int maximumLines = 1000;
        if (LogOutputPanel.Children.Count > maximumLines)
        {
            LogOutputPanel.Children.RemoveAt(0);
        }
    }

    /// <summary>
    /// 更新日志页顶部的服务状态文本。
    /// </summary>
    /// <param name="status">Core 进程状态，类型为 CoreProcessStatus，取值为枚举定义的状态，必填。</param>
    private void UpdateServiceStatus(CoreProcessStatus status)
    {
        LogServiceStatusText.Text = status switch
        {
            CoreProcessStatus.Starting => "服务启动中",
            CoreProcessStatus.Running => "服务运行中",
            CoreProcessStatus.Stopping => "服务停止中",
            CoreProcessStatus.Failed => "服务异常",
            _ => "服务未运行"
        };
    }
}
