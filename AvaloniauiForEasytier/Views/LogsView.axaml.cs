using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using AvaloniauiForEasytier.Services;
using System;

namespace AvaloniauiForEasytier.Views;

/// <summary>
/// 显示所有 EasyTier 网络实例的实时运行输出。
/// </summary>
public partial class LogsView : UserControl
{
    private readonly NetworkRuntimeManager? _runtimeManager;

    /// <summary>初始化设计器使用的日志视图。</summary>
    public LogsView() { InitializeComponent(); }

    /// <summary>
    /// 初始化多网络日志视图并订阅运行时输出。
    /// </summary>
    /// <param name="runtimeManager">多实例运行时管理器，类型为 NetworkRuntimeManager，不可为空，必填。</param>
    public LogsView(NetworkRuntimeManager runtimeManager)
    {
        _runtimeManager = runtimeManager ?? throw new ArgumentNullException(nameof(runtimeManager));
        InitializeComponent();
        ClearLogsButton.Click += ClearLogsButton_Click;
        _runtimeManager.OutputReceived += RuntimeManager_OutputReceived;
        _runtimeManager.StatusChanged += RuntimeManager_StatusChanged;
    }

    /// <summary>
    /// 接收指定网络输出并切换到 Avalonia UI 线程追加日志行。
    /// </summary>
    /// <param name="instanceName">输出所属实例名称，类型为字符串，取值为非空名称，必填。</param>
    /// <param name="eventArgs">输出事件参数，类型为 CoreOutputEventArgs，不可为空，必填。</param>
    private void RuntimeManager_OutputReceived(string instanceName, CoreOutputEventArgs eventArgs)
    {
        Dispatcher.UIThread.Post(() => AppendLogLine(instanceName, eventArgs));
    }

    /// <summary>
    /// 响应指定网络状态变化并刷新日志页状态提示。
    /// </summary>
    /// <param name="instanceName">发生变化的实例名称，类型为字符串，取值为非空名称，必填。</param>
    /// <param name="eventArgs">状态变化参数，类型为 CoreStatusChangedEventArgs，不可为空，必填。</param>
    private void RuntimeManager_StatusChanged(string instanceName, CoreStatusChangedEventArgs eventArgs)
    {
        Dispatcher.UIThread.Post(() => LogServiceStatusText.Text = $"{instanceName}：{GetStatusText(eventArgs.Status)}");
    }

    /// <summary>
    /// 清空当前日志输出窗口。
    /// </summary>
    /// <param name="sender">触发事件的清空按钮，类型为对象，可为空，非必填。</param>
    /// <param name="e">路由事件参数，类型为 Avalonia.Interactivity.RoutedEventArgs，不可为空，必填。</param>
    private void ClearLogsButton_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => LogOutputPanel.Children.Clear();

    /// <summary>
    /// 向日志输出窗口追加一行带实例名称的文本。
    /// </summary>
    /// <param name="instanceName">输出所属实例名称，类型为字符串，取值为非空名称，必填。</param>
    /// <param name="eventArgs">输出事件参数，类型为 CoreOutputEventArgs，不可为空，必填。</param>
    private void AppendLogLine(string instanceName, CoreOutputEventArgs eventArgs)
    {
        var isError = eventArgs.Kind == CoreOutputKind.StandardError; // 标记当前输出是否为错误流。
        var levelText = eventArgs.Kind switch { CoreOutputKind.StandardError => "ERROR", CoreOutputKind.System => "SYSTEM", _ => "INFO" };
        LogOutputPanel.Children.Add(new TextBlock
        {
            FontFamily = new FontFamily("Consolas"),
            FontSize = 11,
            Foreground = new SolidColorBrush(isError ? Color.Parse("#D92D20") : Color.Parse("#667085")),
            Text = $"[{eventArgs.Timestamp:HH:mm:ss.fff}] {levelText,-6} [{instanceName}] {eventArgs.Message}"
        });

        // 限制日志控件数量，避免多个长时间运行网络造成界面无限增长。
        const int maximumLines = 1000;
        if (LogOutputPanel.Children.Count > maximumLines) LogOutputPanel.Children.RemoveAt(0);
    }

    /// <summary>
    /// 将运行状态转换为中文文本。
    /// </summary>
    /// <param name="status">运行状态，类型为 CoreProcessStatus，取值为枚举定义的状态，必填。</param>
    /// <returns>状态文本，类型为字符串。</returns>
    private static string GetStatusText(CoreProcessStatus status) => status switch { CoreProcessStatus.Running => "运行中", CoreProcessStatus.Starting => "启动中", CoreProcessStatus.Stopping => "停止中", CoreProcessStatus.Failed => "异常", _ => "已停止" };
}
