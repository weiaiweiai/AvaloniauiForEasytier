using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using AvaloniauiForEasytier.Services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace AvaloniauiForEasytier.Views;

/// <summary>
/// 显示所有 EasyTier 网络实例的实时运行输出，并提供级别筛选、文本筛选、暂停滚动和导出。
/// </summary>
public partial class LogsView : UserControl
{
    /// <summary>输出缓冲区最多保留的日志条数。</summary>
    private const int MaxBufferedEntries = 1000;

    private static readonly IBrush InfoBrush = new SolidColorBrush(Color.Parse("#667085"));
    private static readonly IBrush SystemBrush = new SolidColorBrush(Color.Parse("#0F766E"));
    private static readonly IBrush ErrorBrush = new SolidColorBrush(Color.Parse("#D92D20"));
    private static readonly IBrush RunningBrush = new SolidColorBrush(Color.Parse("#12B76A"));
    private static readonly IBrush TransitionBrush = new SolidColorBrush(Color.Parse("#D97706"));
    private static readonly IBrush FailedBrush = new SolidColorBrush(Color.Parse("#F04438"));
    private static readonly IBrush StoppedBrush = new SolidColorBrush(Color.Parse("#98A2B3"));

    private readonly NetworkRuntimeManager? _runtimeManager;

    /// <summary>已接收的输出缓冲区，保存实例名称和原始输出参数。</summary>
    private readonly List<(string InstanceName, CoreOutputEventArgs Output)> _entries = new();

    private bool _isScrollPaused; // 标记是否暂停自动滚动到最新一行。
    private Border? _selectedRow; // 当前在事件详情中展示的日志行容器。

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
        PauseScrollButton.Click += PauseScrollButton_Click;
        ExportLogsButton.Click += ExportLogsButton_Click;
        LogLevelComboBox.SelectionChanged += LogFilterChanged;
        LogFilterTextBox.TextChanged += LogFilterChanged;
        _runtimeManager.OutputReceived += RuntimeManager_OutputReceived;
        _runtimeManager.StatusChanged += RuntimeManager_StatusChanged;
        UpdateCounters();
    }
    /// <summary>
    /// 接收指定网络输出并切换到 Avalonia UI 线程追加日志行。
    /// </summary>
    /// <param name="instanceName">输出所属实例名称，类型为字符串，取值为非空名称，必填。</param>
    /// <param name="eventArgs">输出事件参数，类型为 CoreOutputEventArgs，不可为空，必填。</param>
    private void RuntimeManager_OutputReceived(string instanceName, CoreOutputEventArgs eventArgs)
    {
        Dispatcher.UIThread.Post(() => AppendEntry(instanceName, eventArgs));
    }

    /// <summary>
    /// 响应指定网络状态变化并刷新日志页状态提示。
    /// </summary>
    /// <param name="instanceName">发生变化的实例名称，类型为字符串，取值为非空名称，必填。</param>
    /// <param name="eventArgs">状态变化参数，类型为 CoreStatusChangedEventArgs，不可为空，必填。</param>
    private void RuntimeManager_StatusChanged(string instanceName, CoreStatusChangedEventArgs eventArgs)
    {
        Dispatcher.UIThread.Post(() =>
        {
            LogServiceStatusText.Text = $"{instanceName}：{GetStatusText(eventArgs.Status)}";
            LogServiceStatusIndicator.Fill = GetStatusBrush(eventArgs.Status);
        });
    }

    /// <summary>
    /// 清空输出缓冲区、日志行和事件详情。
    /// </summary>
    /// <param name="sender">触发事件的清空按钮，类型为对象，可为空，非必填。</param>
    /// <param name="e">路由事件参数，类型为 RoutedEventArgs，不可为空，必填。</param>
    private void ClearLogsButton_Click(object? sender, RoutedEventArgs e)
    {
        _entries.Clear();
        _selectedRow = null;
        LogOutputPanel.Children.Clear();
        ClearDetail();
        UpdateCounters();
    }

    /// <summary>
    /// 切换自动滚动的暂停状态。
    /// </summary>
    /// <param name="sender">触发事件的暂停按钮，类型为对象，可为空，非必填。</param>
    /// <param name="e">路由事件参数，类型为 RoutedEventArgs，不可为空，必填。</param>
    private void PauseScrollButton_Click(object? sender, RoutedEventArgs e)
    {
        _isScrollPaused = !_isScrollPaused;
        PauseScrollButton.Content = _isScrollPaused ? "继续滚动" : "暂停滚动";
        AutoScrollStateText.Text = _isScrollPaused ? "自动滚动：暂停" : "自动滚动：开启";

        // 恢复滚动时立即跳到末尾，避免用户还要手动下拉才能看到最新输出。
        if (!_isScrollPaused)
        {
            ScrollToEnd();
        }
    }

    /// <summary>
    /// 把当前筛选结果导出为文本文件。
    /// </summary>
    /// <param name="sender">触发事件的导出按钮，类型为对象，可为空，非必填。</param>
    /// <param name="e">路由事件参数，类型为 RoutedEventArgs，不可为空，必填。</param>
    private async void ExportLogsButton_Click(object? sender, RoutedEventArgs e)
    {
        var storageProvider = TopLevel.GetTopLevel(this)?.StorageProvider;

        // 缺少顶层窗口或存储提供程序时无法弹出保存对话框。
        if (storageProvider is null)
        {
            LogFilterSummaryText.Text = "当前环境不支持导出";
            return;
        }

        var visibleEntries = _entries.Where(IsEntryVisible).ToList();

        // 没有符合筛选条件的日志时不创建空文件。
        if (visibleEntries.Count == 0)
        {
            LogFilterSummaryText.Text = "没有可导出的日志";
            return;
        }

        try
        {
            var file = await storageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = "导出日志",
                SuggestedFileName = $"easytier-log-{DateTime.Now:yyyyMMdd-HHmmss}.log",
                DefaultExtension = "log"
            });

            // 用户取消保存对话框时不做任何处理。
            if (file is null)
            {
                return;
            }

            var builder = new StringBuilder();
            foreach (var entry in visibleEntries)
            {
                builder.AppendLine(FormatEntry(entry.InstanceName, entry.Output));
            }

            await using var stream = await file.OpenWriteAsync();
            await using var writer = new System.IO.StreamWriter(stream, new UTF8Encoding(false));
            await writer.WriteAsync(builder.ToString());
            LogFilterSummaryText.Text = $"已导出 {visibleEntries.Count} 行日志";
        }
        catch (Exception exception)
        {
            LogFilterSummaryText.Text = "导出日志失败，请查看应用日志";
            ApplicationLogging.GetLogger(nameof(LogsView)).Error(exception, "导出日志失败");
        }
    }

    /// <summary>
    /// 响应级别或文本筛选条件变化并重建日志行。
    /// </summary>
    /// <param name="sender">触发事件的筛选控件，类型为对象，可为空，非必填。</param>
    /// <param name="e">事件参数，类型为 EventArgs，不可为空，必填。</param>
    private void LogFilterChanged(object? sender, EventArgs e) => RebuildRows();

    /// <summary>
    /// 把一条输出加入缓冲区，并在符合筛选条件时追加日志行。
    /// </summary>
    /// <param name="instanceName">输出所属实例名称，类型为字符串，取值为非空名称，必填。</param>
    /// <param name="eventArgs">输出事件参数，类型为 CoreOutputEventArgs，不可为空，必填。</param>
    private void AppendEntry(string instanceName, CoreOutputEventArgs eventArgs)
    {
        _entries.Add((instanceName, eventArgs));

        // 缓冲区超出上限时丢弃最早一条，并同步移除对应日志行，避免界面无限增长。
        if (_entries.Count > MaxBufferedEntries)
        {
            _entries.RemoveAt(0);
            if (LogOutputPanel.Children.Count > 0)
            {
                LogOutputPanel.Children.RemoveAt(0);
            }
        }

        if (IsEntryVisible((instanceName, eventArgs)))
        {
            LogOutputPanel.Children.Add(BuildRow(instanceName, eventArgs));
            ScrollToEnd();
        }

        UpdateCounters();
    }

    /// <summary>按当前筛选条件重建全部日志行。</summary>
    private void RebuildRows()
    {
        _selectedRow = null;
        LogOutputPanel.Children.Clear();
        foreach (var entry in _entries.Where(IsEntryVisible))
        {
            LogOutputPanel.Children.Add(BuildRow(entry.InstanceName, entry.Output));
        }

        UpdateCounters();
        ScrollToEnd();
    }

    /// <summary>
    /// 判断一条输出是否符合当前级别和文本筛选条件。
    /// </summary>
    /// <param name="entry">输出条目，类型为实例名称与输出参数的元组，不可为空，必填。</param>
    /// <returns>是否显示该条目，类型为 bool。</returns>
    private bool IsEntryVisible((string InstanceName, CoreOutputEventArgs Output) entry)
    {
        // 级别下拉框依次为全部、信息、系统和错误；选择全部时不按级别过滤。
        var levelMatched = LogLevelComboBox.SelectedIndex switch
        {
            1 => entry.Output.Kind == CoreOutputKind.StandardOutput,
            2 => entry.Output.Kind == CoreOutputKind.System,
            3 => entry.Output.Kind == CoreOutputKind.StandardError,
            _ => true
        };
        if (!levelMatched)
        {
            return false;
        }

        // 文本筛选为空时不做内容匹配；非空时按实例名称和消息做忽略大小写包含匹配。
        var keyword = LogFilterTextBox.Text?.Trim();
        if (string.IsNullOrEmpty(keyword))
        {
            return true;
        }

        return entry.InstanceName.Contains(keyword, StringComparison.OrdinalIgnoreCase)
            || (entry.Output.Message ?? string.Empty).Contains(keyword, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// 构建一行可选中的日志行。
    /// </summary>
    /// <param name="instanceName">输出所属实例名称，类型为字符串，取值为非空名称，必填。</param>
    /// <param name="eventArgs">输出事件参数，类型为 CoreOutputEventArgs，不可为空，必填。</param>
    /// <returns>日志行控件，类型为 Border。</returns>
    private Border BuildRow(string instanceName, CoreOutputEventArgs eventArgs)
    {
        // 日志行使用 Border 承载文本：Button 模板有最小高度和内边距，会让密集日志行间距过大。
        var row = new Border
        {
            Classes = { "log-row" },
            Tag = (instanceName, eventArgs),
            Child = new TextBlock
            {
                Classes = { "mono" },
                FontSize = 11,
                Foreground = GetOutputBrush(eventArgs.Kind),
                Text = FormatEntry(instanceName, eventArgs)
            }
        };
        row.PointerPressed += LogRow_PointerPressed;
        return row;
    }

    /// <summary>
    /// 响应日志行点击并在事件详情中展示该行内容。
    /// </summary>
    /// <param name="sender">触发事件的日志行容器，类型为对象，可为空，非必填。</param>
    /// <param name="e">指针按下事件参数，类型为 PointerPressedEventArgs，不可为空，必填。</param>
    private void LogRow_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        // 只响应鼠标左键，避免右键或中键误改事件详情。
        if (sender is not Border { Tag: ValueTuple<string, CoreOutputEventArgs> entry } row
            || !e.GetCurrentPoint(null).Properties.IsLeftButtonPressed)
        {
            return;
        }

        // 切换选中行时移除上一行的高亮状态。
        _selectedRow?.Classes.Remove("selected");
        row.Classes.Add("selected");
        _selectedRow = row;

        DetailMessageText.Text = string.IsNullOrWhiteSpace(entry.Item2.Message) ? "（空行）" : entry.Item2.Message;
        DetailLevelText.Text = GetLevelText(entry.Item2.Kind);
        DetailTimeText.Text = entry.Item2.Timestamp.LocalDateTime.ToString("yyyy-MM-dd HH:mm:ss.fff");
        DetailSourceText.Text = entry.Item1;
    }

    /// <summary>把事件详情恢复为未选择状态。</summary>
    private void ClearDetail()
    {
        DetailMessageText.Text = "未选择日志行";
        DetailLevelText.Text = "--";
        DetailTimeText.Text = "--";
        DetailSourceText.Text = "--";
    }

    /// <summary>刷新底部的级别计数和筛选提示。</summary>
    private void UpdateCounters()
    {
        var infoCount = _entries.Count(entry => entry.Output.Kind == CoreOutputKind.StandardOutput);
        var systemCount = _entries.Count(entry => entry.Output.Kind == CoreOutputKind.System);
        var errorCount = _entries.Count(entry => entry.Output.Kind == CoreOutputKind.StandardError);
        LogCountsText.Text = $"信息 {infoCount}   系统 {systemCount}   错误 {errorCount}";

        // 存在筛选条件时提示当前显示条数与总条数的关系。
        var isFiltered = LogLevelComboBox.SelectedIndex > 0 || !string.IsNullOrWhiteSpace(LogFilterTextBox.Text);
        LogFilterSummaryText.Text = isFiltered
            ? $"筛选后显示 {LogOutputPanel.Children.Count} / {_entries.Count} 行"
            : string.Empty;
    }

    /// <summary>未暂停时把输出窗口滚动到最新一行。</summary>
    private void ScrollToEnd()
    {
        // 暂停期间保持当前滚动位置，便于用户查看历史输出。
        if (_isScrollPaused)
        {
            return;
        }

        Dispatcher.UIThread.Post(() => LogScrollViewer.ScrollToEnd(), DispatcherPriority.Background);
    }

    /// <summary>
    /// 把一条输出格式化为单行文本。
    /// </summary>
    /// <param name="instanceName">输出所属实例名称，类型为字符串，取值为非空名称，必填。</param>
    /// <param name="eventArgs">输出事件参数，类型为 CoreOutputEventArgs，不可为空，必填。</param>
    /// <returns>格式化后的日志文本，类型为字符串。</returns>
    private static string FormatEntry(string instanceName, CoreOutputEventArgs eventArgs)
    {
        return $"[{eventArgs.Timestamp.LocalDateTime:HH:mm:ss.fff}] {GetLevelTag(eventArgs.Kind),-6} [{instanceName}] {eventArgs.Message}";
    }

    /// <summary>
    /// 获取输出级别在日志行中使用的英文标签。
    /// </summary>
    /// <param name="kind">输出类型，类型为 CoreOutputKind，取值为枚举定义的类型，必填。</param>
    /// <returns>级别标签，类型为字符串。</returns>
    private static string GetLevelTag(CoreOutputKind kind) => kind switch
    {
        CoreOutputKind.StandardError => "ERROR",
        CoreOutputKind.System => "SYSTEM",
        _ => "INFO"
    };

    /// <summary>
    /// 获取输出级别的中文文本。
    /// </summary>
    /// <param name="kind">输出类型，类型为 CoreOutputKind，取值为枚举定义的类型，必填。</param>
    /// <returns>级别文本，类型为字符串。</returns>
    private static string GetLevelText(CoreOutputKind kind) => kind switch
    {
        CoreOutputKind.StandardError => "错误",
        CoreOutputKind.System => "系统",
        _ => "信息"
    };

    /// <summary>
    /// 获取输出级别对应的文字颜色。
    /// </summary>
    /// <param name="kind">输出类型，类型为 CoreOutputKind，取值为枚举定义的类型，必填。</param>
    /// <returns>文字颜色画刷，类型为 IBrush。</returns>
    private static IBrush GetOutputBrush(CoreOutputKind kind) => kind switch
    {
        CoreOutputKind.StandardError => ErrorBrush,
        CoreOutputKind.System => SystemBrush,
        _ => InfoBrush
    };

    /// <summary>
    /// 将运行状态转换为中文文本。
    /// </summary>
    /// <param name="status">运行状态，类型为 CoreProcessStatus，取值为枚举定义的状态，必填。</param>
    /// <returns>状态文本，类型为字符串。</returns>
    private static string GetStatusText(CoreProcessStatus status) => status switch
    {
        CoreProcessStatus.Running => "运行中",
        CoreProcessStatus.Starting => "启动中",
        CoreProcessStatus.Stopping => "停止中",
        CoreProcessStatus.Failed => "异常",
        _ => "已停止"
    };

    /// <summary>
    /// 获取运行状态对应的指示灯颜色。
    /// </summary>
    /// <param name="status">运行状态，类型为 CoreProcessStatus，取值为枚举定义的状态，必填。</param>
    /// <returns>状态颜色画刷，类型为 IBrush。</returns>
    private static IBrush GetStatusBrush(CoreProcessStatus status) => status switch
    {
        CoreProcessStatus.Running => RunningBrush,
        CoreProcessStatus.Starting or CoreProcessStatus.Stopping => TransitionBrush,
        CoreProcessStatus.Failed => FailedBrush,
        _ => StoppedBrush
    };
}
