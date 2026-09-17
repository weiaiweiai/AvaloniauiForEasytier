using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using AvaloniauiForEasytier.Services;
using System;

namespace AvaloniauiForEasytier.Views;

/// <summary>
/// 显示多个 EasyTier 网络的汇总状态和批量控制操作。
/// </summary>
public partial class HomeView : UserControl
{
    private readonly NetworkProfileRepository? _repository;
    private readonly NetworkRuntimeManager? _runtimeManager;

    /// <summary>初始化设计器使用的主页视图。</summary>
    public HomeView() { InitializeComponent(); }

    /// <summary>
    /// 初始化多网络主页。
    /// </summary>
    /// <param name="repository">网络配置仓储，类型为 NetworkProfileRepository，不可为空，必填。</param>
    /// <param name="runtimeManager">多实例运行时管理器，类型为 NetworkRuntimeManager，不可为空，必填。</param>
    public HomeView(NetworkProfileRepository repository, NetworkRuntimeManager runtimeManager)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _runtimeManager = runtimeManager ?? throw new ArgumentNullException(nameof(runtimeManager));
        InitializeComponent();
        ServiceActionButton.Click += ServiceActionButton_Click;
        _runtimeManager.StatusChanged += RuntimeManager_StatusChanged;
        ApplySummary();
    }

    /// <summary>
    /// 响应任一网络状态变化并刷新主页汇总。
    /// </summary>
    /// <param name="instanceName">发生变化的实例名称，类型为字符串，取值为非空名称，必填。</param>
    /// <param name="eventArgs">状态变化参数，类型为 CoreStatusChangedEventArgs，不可为空，必填。</param>
    private void RuntimeManager_StatusChanged(string instanceName, CoreStatusChangedEventArgs eventArgs)
    {
        Dispatcher.UIThread.Post(ApplySummary);
    }

    /// <summary>
    /// 启动或停止全部已保存网络。
    /// </summary>
    /// <param name="sender">触发事件的按钮，类型为对象，可为空，非必填。</param>
    /// <param name="e">路由事件参数，类型为 Avalonia.Interactivity.RoutedEventArgs，不可为空，必填。</param>
    private async void ServiceActionButton_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (_repository is null || _runtimeManager is null) return;
        ServiceActionButton.IsEnabled = false;
        try
        {
            if (_runtimeManager.IsAnyRunning) await _runtimeManager.StopAllAsync();
            else await _runtimeManager.StartAllAsync(_repository.GetAll());
        }
        catch (Exception exception)
        {
            ServiceControlHintText.Text = $"批量操作失败：{exception.Message}";
        }
        finally
        {
            ServiceActionButton.IsEnabled = true;
            ApplySummary();
        }
    }

    /// <summary>刷新主页上的运行数量和批量控制状态。</summary>
    private void ApplySummary()
    {
        if (_runtimeManager is null || _repository is null) return;
        var profiles = _repository.GetAll();
        var runningCount = _runtimeManager.RunningCount;
        var totalCount = profiles.Count;
        ServiceStatusText.Text = runningCount > 0 ? "运行中" : "未运行";
        ServiceStatusDescriptionText.Text = $"{runningCount} / {totalCount} 个网络正在运行";
        CurrentNetworkText.Text = runningCount > 0 ? $"{runningCount} 个网络" : "未选择";
        CurrentNetworkDescriptionText.Text = totalCount == 0 ? "没有网络配置" : $"已保存 {totalCount} 个网络配置";
        ServiceControlStatusText.Text = runningCount > 0 ? $"{runningCount} 个网络运行中" : "全部网络已停止";
        ServiceControlDescriptionText.Text = runningCount > 0 ? "EasyTier 多网络运行时" : "EasyTier 多网络运行时未运行";
        ServiceControlHintText.Text = runningCount > 0 ? "再次操作将停止全部网络" : "启动后可在网络配置页单独管理实例";
        ServiceActionButton.Content = runningCount > 0 ? "停止全部" : "启动全部";
        ServiceStatusIndicator.Fill = new SolidColorBrush(runningCount > 0 ? Color.Parse("#12B76A") : Color.Parse("#98A2B3"));
        NetworkCountText.Text = totalCount.ToString();
        RunningCountText.Text = $"运行中 {runningCount}";
    }
}
