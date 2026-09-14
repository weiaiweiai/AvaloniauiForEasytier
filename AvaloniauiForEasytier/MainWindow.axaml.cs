using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using AvaloniauiForEasytier.Services;
using AvaloniauiForEasytier.Views;
using SukiUI.Controls;
using System;
using System.IO;
using System.Threading.Tasks;

namespace AvaloniauiForEasytier;

public partial class MainWindow : SukiWindow
{
    private readonly IEasyTierRuntime _runtime;
    private readonly HomeView _homeView;
    private readonly NetworkView _networkView;
    private readonly NodesView _nodesView;
    private readonly RoutesView _routesView;
    private readonly LogsView _logsView;
    private readonly SettingsView _settingsView;
    private readonly AboutView _aboutView;

    /// <summary>
    /// 初始化桌面控制台窗口及默认主页。
    /// </summary>
    public MainWindow()
    {
        _networkView = new NetworkView();
        _runtime = new EasyTierFfiRuntime(_networkView.GetConfiguration, _networkView.GetInstanceName);
        _homeView = new HomeView(_runtime);
        _nodesView = new NodesView();
        _routesView = new RoutesView();
        _logsView = new LogsView(_runtime);
        _settingsView = new SettingsView();
        _aboutView = new AboutView();
        InitializeComponent();
        RegisterUiEvents();
        _runtime.StatusChanged += CoreProcessManager_StatusChanged;
        UpdateFooter(new CoreStatusChangedEventArgs(
            _runtime.Status,
            _runtime.ExecutablePath,
            _runtime.LastMessage));
        PageHost.Content = _homeView;
    }

    /// <summary>
    /// 注册侧边导航和窗口拖动的强类型事件。
    /// </summary>
    private void RegisterUiEvents()
    {
        HomeNavigationButton.Click += Navigate_Click;
        NetworkNavigationButton.Click += Navigate_Click;
        NodesNavigationButton.Click += Navigate_Click;
        RoutesNavigationButton.Click += Navigate_Click;
        LogsNavigationButton.Click += Navigate_Click;
        SettingsNavigationButton.Click += Navigate_Click;
        AboutNavigationButton.Click += Navigate_Click;
        Closed += MainWindow_Closed;
    }

    /// <summary>
    /// 响应 Core 状态变化并刷新窗口底部状态栏。
    /// </summary>
    /// <param name="sender">触发事件的 Core 管理器，类型为对象，可为空，非必填。</param>
    /// <param name="e">状态变化参数，类型为 CoreStatusChangedEventArgs，不可为空，必填。</param>
    private void CoreProcessManager_StatusChanged(object? sender, CoreStatusChangedEventArgs e)
    {
        Dispatcher.UIThread.Post(() => UpdateFooter(e));
    }

    /// <summary>
    /// 在窗口关闭后释放 Core 进程管理器。
    /// </summary>
    /// <param name="sender">触发关闭事件的窗口，类型为对象，可为空，非必填。</param>
    /// <param name="e">关闭事件参数，类型为 EventArgs，不可为空，必填。</param>
    private async void MainWindow_Closed(object? sender, EventArgs e)
    {
        await _runtime.DisposeAsync();
    }

    /// <summary>
    /// 根据 Core 状态更新窗口底部状态栏。
    /// </summary>
    /// <param name="eventArgs">状态变化参数，类型为 CoreStatusChangedEventArgs，不可为空，必填。</param>
    private void UpdateFooter(CoreStatusChangedEventArgs eventArgs)
    {
        FooterStatusText.Text = eventArgs.Status switch
        {
            CoreProcessStatus.Starting => "服务启动中",
            CoreProcessStatus.Running => "服务运行中",
            CoreProcessStatus.Stopping => "服务停止中",
            CoreProcessStatus.Failed => "服务异常",
            _ => "服务未运行"
        };
        FooterStatusIndicator.Fill = new SolidColorBrush(eventArgs.Status switch
        {
            CoreProcessStatus.Running => Color.Parse("#12B76A"),
            CoreProcessStatus.Failed => Color.Parse("#D92D20"),
            CoreProcessStatus.Starting or CoreProcessStatus.Stopping => Color.Parse("#D97706"),
            _ => Color.Parse("#98A2B3")
        });
        CoreLocationText.Text = eventArgs.ExecutablePath is null
            ? "路径未检测到"
            : Path.GetFileName(eventArgs.ExecutablePath);
    }

    /// <summary>
    /// 根据左侧导航标识切换当前工作区页面。
    /// </summary>
    /// <param name="sender">触发事件的导航按钮，类型为对象，可为空，非必填。</param>
    /// <param name="e">路由事件参数，类型为 RoutedEventArgs，表示导航点击事件，不可为空，必填。</param>
    public void Navigate_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button button || button.Tag is not string pageKey)
        {
            return;
        }

        // 根据导航标识选择页面，保持页面实例以保留用户已填写的界面内容。
        PageHost.Content = pageKey switch
        {
            "home" => _homeView,
            "network" => _networkView,
            "nodes" => _nodesView,
            "routes" => _routesView,
            "logs" => _logsView,
            "settings" => _settingsView,
            "about" => _aboutView,
            _ => _homeView
        };
        SetSelectedNavigation(pageKey);
    }

    /// <summary>
    /// 更新左侧导航按钮的选中视觉状态。
    /// </summary>
    /// <param name="pageKey">页面标识，类型为字符串，取值为 home、network、nodes、routes、logs 或 settings，必填。</param>
    private void SetSelectedNavigation(string pageKey)
    {
        var navigationButtons = new[]
        {
            HomeNavigationButton,
            NetworkNavigationButton,
            NodesNavigationButton,
            RoutesNavigationButton,
            LogsNavigationButton,
            SettingsNavigationButton,
            AboutNavigationButton
        };

        foreach (var button in navigationButtons)
        {
            button.Classes.Remove("selected");
        }

        // 只有匹配当前页面标识的按钮显示选中状态。
        var selectedButton = pageKey switch
        {
            "home" => HomeNavigationButton,
            "network" => NetworkNavigationButton,
            "nodes" => NodesNavigationButton,
            "routes" => RoutesNavigationButton,
            "logs" => LogsNavigationButton,
            "settings" => SettingsNavigationButton,
            "about" => AboutNavigationButton,
            _ => HomeNavigationButton
        };
        selectedButton.Classes.Add("selected");
    }
}
