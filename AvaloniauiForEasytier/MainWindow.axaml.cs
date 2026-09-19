using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using AvaloniauiForEasytier.Services;
using AvaloniauiForEasytier.Views;
using SukiUI.Controls;
using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace AvaloniauiForEasytier;

public partial class MainWindow : SukiWindow
{
    private readonly NetworkRuntimeManager _runtimeManager;
    private readonly NetworkProfileRepository _profileRepository;
    private readonly ServerEndpointRepository _serverRepository;
    private readonly ApplicationSettingsRepository _settingsRepository;
    private readonly HomeView _homeView;
    private readonly NetworkView _networkView;
    private readonly ServersView _serversView;
    private readonly LogsView _logsView;
    private readonly SettingsView _settingsView;
    private readonly AboutView _aboutView;
    private bool _isSidebarCollapsed; // 标记主侧边栏是否处于仅图标的收起状态。

    /// <summary>
    /// 初始化桌面控制台窗口及默认主页。
    /// </summary>
    public MainWindow()
    {
        _profileRepository = new NetworkProfileRepository(ApplicationLogging.GetRequiredDatabase());
        _serverRepository = new ServerEndpointRepository(ApplicationLogging.GetRequiredDatabase());
        _settingsRepository = new ApplicationSettingsRepository(ApplicationLogging.GetRequiredDatabase());
        EnsureDefaultProfile();
        _runtimeManager = new NetworkRuntimeManager();
        _networkView = new NetworkView(_profileRepository, _serverRepository, _runtimeManager);
        _homeView = new HomeView(_profileRepository, _runtimeManager, OpenNetworkFromHome);
        _serversView = new ServersView(_serverRepository);
        _logsView = new LogsView(_runtimeManager);
        _settingsView = new SettingsView(_settingsRepository);
        _aboutView = new AboutView();
        InitializeComponent();
        RegisterUiEvents();
        _runtimeManager.StatusChanged += RuntimeManager_StatusChanged;
        UpdateFooter();
        PageHost.Content = _homeView;
        _ = StartAutoStartNetworksAsync();
    }

    /// <summary>
    /// 注册侧边导航和窗口拖动的强类型事件。
    /// </summary>
    private void RegisterUiEvents()
    {
        HomeNavigationButton.Click += Navigate_Click;
        NetworkNavigationButton.Click += Navigate_Click;
        ServersNavigationButton.Click += Navigate_Click;
        LogsNavigationButton.Click += Navigate_Click;
        SettingsNavigationButton.Click += Navigate_Click;
        AboutNavigationButton.Click += Navigate_Click;
        SidebarToggleButton.Click += SidebarToggleButton_Click;
        Closed += MainWindow_Closed;
    }

    /// <summary>
    /// 切换主侧边栏的展开和收起状态。
    /// </summary>
    /// <param name="sender">触发事件的收起按钮，类型为对象，可为空，非必填。</param>
    /// <param name="e">路由事件参数，类型为 RoutedEventArgs，不可为空，必填。</param>
    private void SidebarToggleButton_Click(object? sender, RoutedEventArgs e)
    {
        _isSidebarCollapsed = !_isSidebarCollapsed;
        ApplySidebarMode();
    }

    /// <summary>按当前收起状态调整主侧边栏的宽度、文字和分隔条。</summary>
    private void ApplySidebarMode()
    {
        const double expandedWidth = 184; // 展开状态的侧边栏宽度（像素）。
        const double collapsedWidth = 84; // 收起状态的侧边栏宽度（像素），仅容纳图标。
        MainGrid.ColumnDefinitions[0].Width = new GridLength(_isSidebarCollapsed ? collapsedWidth : expandedWidth, GridUnitType.Pixel);

        // 收起时隐藏标题文字、分组标题和底部 Core 卡片，仅保留图标和收起按钮。
        SidebarTitlePanel.IsVisible = !_isSidebarCollapsed;
        WorkspaceGroupHeader.IsVisible = !_isSidebarCollapsed;
        ApplicationGroupHeader.IsVisible = !_isSidebarCollapsed;
        CoreInfoCard.IsVisible = !_isSidebarCollapsed;

        // 收起时隐藏分隔条，避免用户把仅图标侧边栏拖回宽布局。
        MainGrid.ColumnDefinitions[1].Width = new GridLength(_isSidebarCollapsed ? 0 : 5, GridUnitType.Pixel);
        SidebarSplitter.IsVisible = !_isSidebarCollapsed;

        // 收起时头部改为纵向排列：logo 居中在上，收起按钮居中在下；展开时恢复水平布局。
        if (_isSidebarCollapsed)
        {
            Grid.SetColumnSpan(SidebarLogo, 3);
            SidebarLogo.HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center;
            Grid.SetRow(SidebarToggleButton, 1);
            Grid.SetColumn(SidebarToggleButton, 0);
            Grid.SetColumnSpan(SidebarToggleButton, 3);
            SidebarToggleButton.HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center;
            SidebarToggleButton.Margin = new Thickness(0, 10, 0, 0);
        }
        else
        {
            Grid.SetColumnSpan(SidebarLogo, 1);
            SidebarLogo.HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left;
            Grid.SetRow(SidebarToggleButton, 0);
            Grid.SetColumn(SidebarToggleButton, 2);
            Grid.SetColumnSpan(SidebarToggleButton, 1);
            SidebarToggleButton.HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center;
            SidebarToggleButton.Margin = new Thickness(0);
        }

        // 收起时导航按钮只显示居中图标，展开时恢复左对齐文字；两种模式都不绘制按钮描边。
        var contentAlignment = _isSidebarCollapsed ? Avalonia.Layout.HorizontalAlignment.Center : Avalonia.Layout.HorizontalAlignment.Left;
        foreach (var button in GetNavigationButtons())
        {
            button.HorizontalContentAlignment = contentAlignment;
            button.Padding = _isSidebarCollapsed ? new Thickness(0, 7) : new Thickness(12, 7);
            if (button.Content is StackPanel panel)
            {
                foreach (var text in panel.Children.OfType<TextBlock>())
                {
                    text.IsVisible = !_isSidebarCollapsed;
                }
            }
        }

        ToolTip.SetTip(SidebarToggleButton, _isSidebarCollapsed ? "展开侧边栏" : "收起侧边栏");
    }

    /// <summary>
    /// 获取全部侧边导航按钮。
    /// </summary>
    /// <returns>导航按钮数组，类型为 Button 数组。</returns>
    private Button[] GetNavigationButtons()
    {
        return new[]
        {
            HomeNavigationButton,
            NetworkNavigationButton,
            ServersNavigationButton,
            LogsNavigationButton,
            SettingsNavigationButton,
            AboutNavigationButton
        };
    }

    /// <summary>
    /// 从主页网络快照进入对应网络的详情页。
    /// </summary>
    /// <param name="profileId">目标网络主键，类型为 long，取值为大于零的数据库主键，必填。</param>
    private void OpenNetworkFromHome(long profileId)
    {
        PageHost.Content = _networkView;
        SetSelectedNavigation("network");
        _networkView.OpenNetworkDetail(profileId);
    }

    /// <summary>
    /// 响应 Core 状态变化并刷新窗口底部状态栏。
    /// </summary>
    /// <param name="instanceName">发生变化的实例名称，类型为字符串，取值为非空名称，必填。</param>
    /// <param name="eventArgs">状态变化参数，类型为 CoreStatusChangedEventArgs，不可为空，必填。</param>
    private void RuntimeManager_StatusChanged(string instanceName, CoreStatusChangedEventArgs eventArgs)
    {
        Dispatcher.UIThread.Post(UpdateFooter);
    }

    /// <summary>
    /// 在窗口关闭后释放 Core 进程管理器。
    /// </summary>
    /// <param name="sender">触发关闭事件的窗口，类型为对象，可为空，非必填。</param>
    /// <param name="e">关闭事件参数，类型为 EventArgs，不可为空，必填。</param>
    private async void MainWindow_Closed(object? sender, EventArgs e)
    {
        await _runtimeManager.DisposeAsync();
    }

    /// <summary>
    /// 根据全部网络运行数量更新窗口底部状态栏。
    /// </summary>
    private void UpdateFooter()
    {
        var runningCount = _runtimeManager.RunningCount;
        FooterStatusText.Text = runningCount > 0 ? $"{runningCount} 个网络运行中" : "网络未运行";
        FooterStatusIndicator.Fill = new SolidColorBrush(runningCount > 0 ? Color.Parse("#12B76A") : Color.Parse("#98A2B3"));
        CoreLocationText.Text = Path.GetFileName(_runtimeManager.NativeLibraryPath);
        RunningNetworkCountText.Text = $"网络：{runningCount}";
    }

    /// <summary>
    /// 首次运行时创建一个可直接编辑的默认配置。
    /// </summary>
    private void EnsureDefaultProfile()
    {
        if (_profileRepository.GetAll().Count > 0)
        {
            return;
        }

        _profileRepository.Save(new NetworkProfile
        {
            ProfileName = "默认网络", // 配置显示名称。
            InstanceName = "easytier-desktop", // EasyTier FFI 实例名称。
            NetworkName = "easytier", // 虚拟网络名称。
            AutoStart = false // 默认不在应用启动时自动连接。
        });
    }

    /// <summary>
    /// 启动数据库中标记为自动启动的全部网络。
    /// </summary>
    /// <returns>异步启动任务，类型为 Task。</returns>
    private async Task StartAutoStartNetworksAsync()
    {
        try
        {
            await _runtimeManager.StartAllAsync(_profileRepository.GetAll().Where(profile => profile.AutoStart));
        }
        catch (Exception exception)
        {
            ApplicationLogging.GetLogger(nameof(MainWindow)).Error(exception, "自动启动网络失败");
        }
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
            "servers" => _serversView,
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
    /// <param name="pageKey">页面标识，类型为字符串，取值为 home、network、servers、logs、settings 或 about，必填。</param>
    private void SetSelectedNavigation(string pageKey)
    {
        foreach (var button in GetNavigationButtons())
        {
            button.Classes.Remove("selected");
        }

        // 只有匹配当前页面标识的按钮显示选中状态。
        var selectedButton = pageKey switch
        {
            "home" => HomeNavigationButton,
            "network" => NetworkNavigationButton,
            "servers" => ServersNavigationButton,
            "logs" => LogsNavigationButton,
            "settings" => SettingsNavigationButton,
            "about" => AboutNavigationButton,
            _ => HomeNavigationButton
        };
        selectedButton.Classes.Add("selected");
    }
}
