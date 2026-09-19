using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using AvaloniauiForEasytier.Services;
using System;
using System.Collections.Generic;
using System.Linq;

namespace AvaloniauiForEasytier.Views;

/// <summary>
/// 显示网络运行总览、网络快照和最近活动的主页。
/// </summary>
public partial class HomeView : UserControl
{
    /// <summary>最近活动区域最多保留的输出条数。</summary>
    private const int MaxRecentActivityCount = 6;

    private static readonly IBrush RunningBrush = new SolidColorBrush(Color.Parse("#12B76A"));
    private static readonly IBrush TransitionBrush = new SolidColorBrush(Color.Parse("#D97706"));
    private static readonly IBrush FailedBrush = new SolidColorBrush(Color.Parse("#F04438"));
    private static readonly IBrush StoppedBrush = new SolidColorBrush(Color.Parse("#98A2B3"));
    private static readonly IBrush MessageBrush = new SolidColorBrush(Color.Parse("#475467"));

    private readonly NetworkProfileRepository? _repository;
    private readonly NetworkRuntimeManager? _runtimeManager;
    private readonly Action<long>? _openNetworkDetail;
    private List<NetworkProfile> _profiles = new();
    private bool _isBatchOperating; // 标记批量启停操作是否正在执行，避免期间重复提交。

    /// <summary>初始化设计器使用的主页视图。</summary>
    public HomeView() { InitializeComponent(); }

    /// <summary>
    /// 初始化网络总览主页。
    /// </summary>
    /// <param name="repository">网络配置仓储，类型为 NetworkProfileRepository，不可为空，必填。</param>
    /// <param name="runtimeManager">多实例运行时管理器，类型为 NetworkRuntimeManager，不可为空，必填。</param>
    /// <param name="openNetworkDetail">打开网络详情的回调，类型为 Action&lt;long&gt;；参数为目标网络主键，可空，非必填。</param>
    public HomeView(NetworkProfileRepository repository, NetworkRuntimeManager runtimeManager, Action<long>? openNetworkDetail = null)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _runtimeManager = runtimeManager ?? throw new ArgumentNullException(nameof(runtimeManager));
        _openNetworkDetail = openNetworkDetail;
        InitializeComponent();
        StartAllButton.Click += StartAllButton_Click;
        StopAllButton.Click += StopAllButton_Click;
        _runtimeManager.StatusChanged += RuntimeManager_StatusChanged;
        _runtimeManager.OutputReceived += RuntimeManager_OutputReceived;
        RenderOverview();
    }

    /// <summary>
    /// 响应任一网络状态变化并刷新总览。
    /// </summary>
    /// <param name="instanceName">发生变化的实例名称，类型为字符串，取值为非空名称，必填。</param>
    /// <param name="eventArgs">状态变化参数，类型为 CoreStatusChangedEventArgs，不可为空，必填。</param>
    private void RuntimeManager_StatusChanged(string instanceName, CoreStatusChangedEventArgs eventArgs)
    {
        Dispatcher.UIThread.Post(RenderOverview);
    }

    /// <summary>
    /// 响应网络运行输出并追加到最近活动区域。
    /// </summary>
    /// <param name="instanceName">产生输出的实例名称，类型为字符串，取值为非空名称，必填。</param>
    /// <param name="eventArgs">输出参数，类型为 CoreOutputEventArgs，不可为空，必填。</param>
    private void RuntimeManager_OutputReceived(string instanceName, CoreOutputEventArgs eventArgs)
    {
        Dispatcher.UIThread.Post(() => AppendRecentActivity(instanceName, eventArgs));
    }

    /// <summary>
    /// 批量启动数据库中的全部网络。
    /// </summary>
    /// <param name="sender">触发事件的按钮，类型为对象，可为空，非必填。</param>
    /// <param name="e">路由事件参数，类型为 RoutedEventArgs，不可为空，必填。</param>
    private async void StartAllButton_Click(object? sender, RoutedEventArgs e)
    {
        if (_repository is null || _runtimeManager is null) return;
        _isBatchOperating = true;
        UpdateBatchButtons();
        try
        {
            await _runtimeManager.StartAllAsync(_repository.GetAll());
        }
        catch (Exception exception)
        {
            AppendRecentActivity("系统", new CoreOutputEventArgs(CoreOutputKind.StandardError, $"批量启动失败：{exception.Message}"));
        }
        finally
        {
            _isBatchOperating = false;
            RenderOverview();
        }
    }

    /// <summary>
    /// 批量停止当前应用管理的全部网络。
    /// </summary>
    /// <param name="sender">触发事件的按钮，类型为对象，可为空，非必填。</param>
    /// <param name="e">路由事件参数，类型为 RoutedEventArgs，不可为空，必填。</param>
    private async void StopAllButton_Click(object? sender, RoutedEventArgs e)
    {
        if (_repository is null || _runtimeManager is null) return;
        _isBatchOperating = true;
        UpdateBatchButtons();
        try
        {
            await _runtimeManager.StopAllAsync();
        }
        catch (Exception exception)
        {
            AppendRecentActivity("系统", new CoreOutputEventArgs(CoreOutputKind.StandardError, $"批量停止失败：{exception.Message}"));
        }
        finally
        {
            _isBatchOperating = false;
            RenderOverview();
        }
    }

    /// <summary>
    /// 响应网络快照行点击并打开对应网络详情。
    /// </summary>
    /// <param name="sender">触发事件的快照行按钮，类型为对象，可为空，非必填。</param>
    /// <param name="e">路由事件参数，类型为 RoutedEventArgs，不可为空，必填。</param>
    private void SnapshotRowButton_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: long profileId })
        {
            _openNetworkDetail?.Invoke(profileId);
        }
    }

    /// <summary>刷新汇总卡片、网络快照和批量按钮。</summary>
    private void RenderOverview()
    {
        if (_repository is null || _runtimeManager is null) return;
        _profiles = _repository.GetAll().ToList();
        var runningCount = _runtimeManager.RunningCount;
        var autoStartCount = _profiles.Count(profile => profile.AutoStart);

        // 服务状态卡片显示整体运行情况；没有网络时视为未运行。
        ServiceStatusValueText.Text = runningCount > 0 ? "运行中" : "未运行";
        ServiceStatusDescriptionText.Text = $"运行中 {runningCount} / {_profiles.Count} 个网络";
        SummaryStatusIndicator.Fill = runningCount > 0 ? RunningBrush : StoppedBrush;
        NetworkTotalValueText.Text = _profiles.Count.ToString();
        AutoStartValueText.Text = autoStartCount.ToString();
        UpdateBatchButtons();

        NetworkSnapshotPanel.Children.Clear();
        if (_profiles.Count == 0)
        {
            var hint = new TextBlock
            {
                Classes = { "muted" },
                Text = "暂无网络，请在“网络”页新建网络",
                FontSize = 12,
                Margin = new Thickness(4, 8)
            };
            NetworkSnapshotPanel.Children.Add(hint);
            return;
        }

        foreach (var profile in _profiles)
        {
            NetworkSnapshotPanel.Children.Add(CreateSnapshotRow(profile, _runtimeManager.GetStatus(profile.InstanceName)));
        }
    }

    /// <summary>刷新批量启停按钮的可用状态。</summary>
    private void UpdateBatchButtons()
    {
        // 批量操作执行中或没有已保存网络时禁用，防止重复提交。
        var enabled = !_isBatchOperating && _profiles.Count > 0;
        StartAllButton.IsEnabled = enabled;
        StopAllButton.IsEnabled = enabled;
    }

    /// <summary>
    /// 创建一行可点击的网络快照。
    /// </summary>
    /// <param name="profile">网络配置，类型为 NetworkProfile，不可为空，必填。</param>
    /// <param name="status">该网络当前运行状态，类型为 CoreProcessStatus，取值为枚举定义的状态，必填。</param>
    /// <returns>快照行控件，类型为 Button。</returns>
    private Button CreateSnapshotRow(NetworkProfile profile, CoreProcessStatus status)
    {
        var rowButton = new Button
        {
            Classes = { "snapshot-row" },
            Tag = profile.Id,
            Margin = new Thickness(0, 0, 0, 4)
        };
        rowButton.Click += SnapshotRowButton_Click;

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(1.6, GridUnitType.Star)));
        grid.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(1, GridUnitType.Star)));
        grid.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(140, GridUnitType.Pixel)));
        grid.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(28, GridUnitType.Pixel)));

        var nameText = new TextBlock
        {
            Text = profile.ProfileName,
            FontSize = 12,
            FontWeight = FontWeight.Medium,
            Foreground = new SolidColorBrush(Color.Parse("#344054")),
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis
        };
        grid.Children.Add(nameText);

        // 未填写虚拟网段时显示占位说明，表示由 EasyTier 自动分配地址。
        var subnetConfigured = !string.IsNullOrWhiteSpace(profile.Ipv4);
        var subnetText = new TextBlock
        {
            Text = subnetConfigured ? profile.Ipv4!.Trim() : "自动分配",
            FontSize = 12,
            Foreground = new SolidColorBrush(Color.Parse(subnetConfigured ? "#475467" : "#98A2B3")),
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis
        };
        Grid.SetColumn(subnetText, 1);
        grid.Children.Add(subnetText);

        var statusBrush = GetStatusBrush(status);
        var statusPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 7,
            VerticalAlignment = VerticalAlignment.Center
        };
        statusPanel.Children.Add(new Ellipse
        {
            Width = 8,
            Height = 8,
            VerticalAlignment = VerticalAlignment.Center,
            Fill = statusBrush
        });
        statusPanel.Children.Add(new TextBlock
        {
            Text = GetStatusText(status),
            FontSize = 12,
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = statusBrush
        });
        Grid.SetColumn(statusPanel, 2);
        grid.Children.Add(statusPanel);

        // 行尾箭头提示该行可以进入网络详情。
        var chevron = new TextBlock
        {
            Text = "›",
            FontSize = 16,
            Foreground = new SolidColorBrush(Color.Parse("#98A2B3")),
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(chevron, 3);
        grid.Children.Add(chevron);

        rowButton.Content = grid;
        return rowButton;
    }

    /// <summary>
    /// 追加一条最近活动输出。
    /// </summary>
    /// <param name="instanceName">产生输出的实例名称，类型为字符串，取值为非空名称，必填。</param>
    /// <param name="eventArgs">输出参数，类型为 CoreOutputEventArgs，不可为空，必填。</param>
    private void AppendRecentActivity(string instanceName, CoreOutputEventArgs eventArgs)
    {
        // 多行输出压缩为单行，保持最近活动区域整齐。
        var message = (eventArgs.Message ?? string.Empty).Replace("\r", " ").Replace("\n", " ").Trim();
        if (message.Length == 0) return;

        // 第一条真实输出到达后移除空提示。
        if (RecentActivityPanel.Children.Contains(RecentActivityEmptyText))
        {
            RecentActivityPanel.Children.Remove(RecentActivityEmptyText);
        }

        var row = new Grid();
        row.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(62, GridUnitType.Pixel)));
        row.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(1, GridUnitType.Star)));
        var timeText = new TextBlock
        {
            Text = eventArgs.Timestamp.LocalDateTime.ToString("HH:mm:ss"),
            FontSize = 11,
            Foreground = StoppedBrush,
            VerticalAlignment = VerticalAlignment.Center
        };
        var messageText = new TextBlock
        {
            Text = $"{instanceName}：{message}",
            FontSize = 11,
            Foreground = eventArgs.Kind == CoreOutputKind.StandardError ? FailedBrush : MessageBrush,
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis
        };
        Grid.SetColumn(messageText, 1);
        row.Children.Add(timeText);
        row.Children.Add(messageText);
        RecentActivityPanel.Children.Add(row);

        // 超出保留上限时移除最早一条。
        while (RecentActivityPanel.Children.Count > MaxRecentActivityCount)
        {
            RecentActivityPanel.Children.RemoveAt(0);
        }
    }

    /// <summary>
    /// 获取运行状态对应的显示颜色。
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

    /// <summary>
    /// 获取运行状态的中文文本。
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
}
