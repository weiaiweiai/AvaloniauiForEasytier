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
/// 按网络罗列运行状态并提供单个网络与批量启停操作的主页。
/// </summary>
public partial class HomeView : UserControl
{
    /// <summary>最近活动区域最多保留的输出条数。</summary>
    private const int MaxRecentActivityCount = 6;

    private static readonly IBrush RunningBrush = new SolidColorBrush(Color.Parse("#12B76A"));
    private static readonly IBrush TransitionBrush = new SolidColorBrush(Color.Parse("#D97706"));
    private static readonly IBrush FailedBrush = new SolidColorBrush(Color.Parse("#F04438"));
    private static readonly IBrush StoppedBrush = new SolidColorBrush(Color.Parse("#98A2B3"));
    private static readonly IBrush AutoStartBrush = new SolidColorBrush(Color.Parse("#0F9F8F"));
    private static readonly IBrush MessageBrush = new SolidColorBrush(Color.Parse("#475467"));

    private readonly NetworkProfileRepository? _repository;
    private readonly NetworkRuntimeManager? _runtimeManager;
    private List<NetworkProfile> _profiles = new();
    private bool _isBatchOperating; // 标记批量启停操作是否正在执行，避免期间重复提交。

    /// <summary>初始化设计器使用的主页视图。</summary>
    public HomeView() { InitializeComponent(); }

    /// <summary>
    /// 初始化按网络管理的主页。
    /// </summary>
    /// <param name="repository">网络配置仓储，类型为 NetworkProfileRepository，不可为空，必填。</param>
    /// <param name="runtimeManager">多实例运行时管理器，类型为 NetworkRuntimeManager，不可为空，必填。</param>
    public HomeView(NetworkProfileRepository repository, NetworkRuntimeManager runtimeManager)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _runtimeManager = runtimeManager ?? throw new ArgumentNullException(nameof(runtimeManager));
        InitializeComponent();
        StartAllButton.Click += StartAllButton_Click;
        StopAllButton.Click += StopAllButton_Click;
        _runtimeManager.StatusChanged += RuntimeManager_StatusChanged;
        _runtimeManager.OutputReceived += RuntimeManager_OutputReceived;
        RenderNetworks();
    }

    /// <summary>
    /// 响应任一网络状态变化并重建网络表格。
    /// </summary>
    /// <param name="instanceName">发生变化的实例名称，类型为字符串，取值为非空名称，必填。</param>
    /// <param name="eventArgs">状态变化参数，类型为 CoreStatusChangedEventArgs，不可为空，必填。</param>
    private void RuntimeManager_StatusChanged(string instanceName, CoreStatusChangedEventArgs eventArgs)
    {
        Dispatcher.UIThread.Post(RenderNetworks);
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
            RenderNetworks();
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
            RenderNetworks();
        }
    }

    /// <summary>
    /// 启动或停止表格行对应的单个网络。
    /// </summary>
    /// <param name="sender">触发事件的行内操作按钮，类型为对象，可为空，非必填。</param>
    /// <param name="e">路由事件参数，类型为 RoutedEventArgs，不可为空，必填。</param>
    private async void NetworkRowActionButton_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: long profileId } || _runtimeManager is null) return;
        var profile = _profiles.FirstOrDefault(item => item.Id == profileId);
        if (profile is null) return;

        var status = _runtimeManager.GetStatus(profile.InstanceName);
        // 运行中或状态切换中的网络执行停止，其余状态执行启动。
        if (status is CoreProcessStatus.Running or CoreProcessStatus.Starting or CoreProcessStatus.Stopping)
        {
            await _runtimeManager.StopAsync(profile.InstanceName);
        }
        else
        {
            await _runtimeManager.StartAsync(profile);
        }
    }

    /// <summary>重建网络表格并刷新汇总状态和批量按钮。</summary>
    private void RenderNetworks()
    {
        if (_repository is null || _runtimeManager is null) return;
        _profiles = _repository.GetAll().ToList();
        var runningCount = _runtimeManager.RunningCount;

        // 汇总行显示运行数量，没有已保存网络时提示引导。
        if (_profiles.Count == 0)
        {
            SummaryStatusText.Text = "暂无网络";
            SummaryStatusIndicator.Fill = StoppedBrush;
        }
        else
        {
            SummaryStatusText.Text = runningCount > 0 ? $"运行中 {runningCount} / {_profiles.Count}" : "全部网络已停止";
            SummaryStatusIndicator.Fill = runningCount > 0 ? RunningBrush : StoppedBrush;
        }
        UpdateBatchButtons();

        NetworkRowsPanel.Children.Clear();
        if (_profiles.Count == 0)
        {
            var hint = new TextBlock
            {
                Classes = { "muted" },
                Text = "暂无网络，请在网络配置页新建网络",
                FontSize = 12,
                Margin = new Thickness(0, 12)
            };
            NetworkRowsPanel.Children.Add(hint);
            return;
        }

        for (var index = 0; index < _profiles.Count; index++)
        {
            var isLastRow = index == _profiles.Count - 1; // 标记是否为最后一行，用于省略底部分隔线。
            var profile = _profiles[index];
            NetworkRowsPanel.Children.Add(CreateNetworkRow(profile, _runtimeManager.GetStatus(profile.InstanceName), isLastRow));
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
    /// 创建一行网络状态表格。
    /// </summary>
    /// <param name="profile">网络配置，类型为 NetworkProfile，不可为空，必填。</param>
    /// <param name="status">该网络当前运行状态，类型为 CoreProcessStatus，取值为枚举定义的状态，必填。</param>
    /// <param name="isLastRow">是否为最后一行，类型为 bool；为真时不绘制底部分隔线。</param>
    /// <returns>表格行控件，类型为 Border。</returns>
    private Border CreateNetworkRow(NetworkProfile profile, CoreProcessStatus status, bool isLastRow)
    {
        var row = new Border
        {
            MinHeight = 44,
            BorderBrush = new SolidColorBrush(Color.Parse("#EEF1F5")),
            BorderThickness = isLastRow ? new Thickness(0) : new Thickness(0, 0, 0, 1)
        };

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(1.4, GridUnitType.Star)));
        grid.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(1, GridUnitType.Star)));
        grid.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(96, GridUnitType.Pixel)));
        grid.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(120, GridUnitType.Pixel)));
        grid.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(88, GridUnitType.Pixel)));

        var nameText = new TextBlock
        {
            Classes = { "cell" },
            Text = profile.ProfileName,
            FontWeight = FontWeight.Medium
        };
        grid.Children.Add(nameText);

        // 未填写虚拟网段时显示占位说明，表示由 EasyTier 自动分配地址。
        var subnetConfigured = !string.IsNullOrWhiteSpace(profile.Ipv4);
        var subnetText = new TextBlock
        {
            Text = subnetConfigured ? profile.Ipv4!.Trim() : "自动分配",
            FontSize = 12,
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis
        };
        subnetText.Classes.Add(subnetConfigured ? "cell" : "cell-muted");
        Grid.SetColumn(subnetText, 1);
        grid.Children.Add(subnetText);

        var autoStartText = new TextBlock
        {
            Text = profile.AutoStart ? "是" : "否",
            FontSize = 12,
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = profile.AutoStart ? AutoStartBrush : StoppedBrush
        };
        Grid.SetColumn(autoStartText, 2);
        grid.Children.Add(autoStartText);

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
        Grid.SetColumn(statusPanel, 3);
        grid.Children.Add(statusPanel);

        // 运行中和停止中的行显示停止按钮，其余状态显示启动按钮；状态切换期间禁用。
        var isStopAction = status is CoreProcessStatus.Running or CoreProcessStatus.Stopping;
        var actionButton = new Button
        {
            Content = isStopAction ? "停止" : "启动",
            IsEnabled = status is CoreProcessStatus.Running or CoreProcessStatus.Stopped or CoreProcessStatus.Failed,
            Tag = profile.Id,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Center
        };
        actionButton.Classes.Add("row-action");
        actionButton.Classes.Add(isStopAction ? "outline" : "accent");
        actionButton.Click += NetworkRowActionButton_Click;
        Grid.SetColumn(actionButton, 4);
        grid.Children.Add(actionButton);

        row.Child = grid;
        return row;
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
