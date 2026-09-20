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
using System.Threading.Tasks;

namespace AvaloniauiForEasytier.Views;

/// <summary>
/// 以网络为主体：二级边栏选择网络，右侧详情页集中启停、配置以及节点和路由查看。
/// </summary>
public partial class NetworkView : UserControl
{
    private static readonly IBrush StatusRunningBrush = new SolidColorBrush(Color.Parse("#12B76A"));
    private static readonly IBrush StatusTransitionBrush = new SolidColorBrush(Color.Parse("#D97706"));
    private static readonly IBrush StatusFailedBrush = new SolidColorBrush(Color.Parse("#F04438"));
    private static readonly IBrush StatusStoppedBrush = new SolidColorBrush(Color.Parse("#98A2B3"));

    private readonly NetworkProfileRepository? _repository;
    private readonly ServerEndpointRepository? _serverRepository;
    private readonly NetworkRuntimeManager? _runtimeManager;
    private List<NetworkProfile> _sidebarProfiles = new();
    private NetworkProfile? _selectedProfile;
    private readonly DispatcherTimer? _detailDataTimer;
    private bool _isLoadingDetailData; // 标记节点路由快照是否正在采集，防止并发调用 FFI。

    /// <summary>初始化设计器使用的网络视图。</summary>
    public NetworkView() { InitializeComponent(); }

    /// <summary>
    /// 初始化以网络为主体的网络视图。
    /// </summary>
    /// <param name="repository">网络配置仓储，类型为 NetworkProfileRepository，不可为空，必填。</param>
    /// <param name="serverRepository">服务器地址仓储，类型为 ServerEndpointRepository，不可为空，必填。</param>
    /// <param name="runtimeManager">多实例运行时管理器，类型为 NetworkRuntimeManager，不可为空，必填。</param>
    public NetworkView(NetworkProfileRepository repository, ServerEndpointRepository serverRepository, NetworkRuntimeManager runtimeManager)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _serverRepository = serverRepository ?? throw new ArgumentNullException(nameof(serverRepository));
        _runtimeManager = runtimeManager ?? throw new ArgumentNullException(nameof(runtimeManager));
        InitializeComponent();
        NewNetworkButton.Click += NewNetworkButton_Click;
        StartNetworkButton.Click += StartNetworkButton_Click;
        DeleteNetworkButton.Click += DeleteNetworkButton_Click;
        SaveProfileButton.Click += SaveProfileButton_Click;
        RefreshPeersButton.Click += RefreshDataButton_Click;
        RefreshRoutesButton.Click += RefreshDataButton_Click;
        DetailTabControl.SelectionChanged += DetailTabControl_SelectionChanged;
        _runtimeManager.StatusChanged += RuntimeManager_StatusChanged;

        // 节点与路由数据按固定周期自动刷新，仅在详情可见且网络运行中时生效。
        _detailDataTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
        _detailDataTimer.Tick += DetailDataTimer_Tick;
        _detailDataTimer.Start();
        ClearSelection();
    }

    /// <summary>
    /// 选中指定网络并展示详情；配置不存在时清除选择。
    /// </summary>
    /// <param name="profileId">网络主键，类型为 long，取值为大于零的数据库主键，必填。</param>
    public void OpenNetworkDetail(long profileId)
    {
        if (_repository is null) return;
        var profile = _repository.GetById(profileId);
        if (profile is null)
        {
            ClearSelection();
            return;
        }

        _selectedProfile = profile;
        FillEditor(profile);
        UpdateDetailHeader();
        ShowDetail();
        RenderSidebar();
    }

    /// <summary>
    /// 响应任一网络状态变化并刷新侧边栏状态、详情头部和数据标签页。
    /// </summary>
    /// <param name="instanceName">发生变化的实例名称，类型为字符串，取值为非空名称，必填。</param>
    /// <param name="eventArgs">状态变化参数，类型为 CoreStatusChangedEventArgs，不可为空，必填。</param>
    private void RuntimeManager_StatusChanged(string instanceName, CoreStatusChangedEventArgs eventArgs)
    {
        Dispatcher.UIThread.Post(() =>
        {
            // 侧边栏状态点始终刷新；选中网络匹配变化实例时同步详情头部。
            RenderSidebar();
            if (DetailRoot.IsVisible) UpdateDetailHeader();

            // 状态变化可能意味着实例刚启动或已停止，立即刷新数据标签页内容。
            _ = RefreshDetailDataAsync();
        });
    }

    /// <summary>重建二级边栏网络列表。</summary>
    private void RenderSidebar()
    {
        if (_repository is null || _runtimeManager is null) return;
        _sidebarProfiles = _repository.GetAll().ToList();
        var selectedId = _selectedProfile?.Id ?? 0;

        NetworkSidebarPanel.Children.Clear();

        // 正在编辑未保存的新网络时，列表顶部显示一个不可点击的临时条目。
        if (selectedId == 0 && _selectedProfile is not null)
        {
            NetworkSidebarPanel.Children.Add(BuildUnsavedSidebarItem(_selectedProfile.ProfileName));
        }

        // 没有已保存网络且不在编辑新网络时显示引导文本。
        if (_sidebarProfiles.Count == 0)
        {
            if (_selectedProfile is null)
            {
                var hint = new TextBlock
                {
                    Classes = { "muted" },
                    Text = "暂无网络，点击上方“新建网络”创建",
                    FontSize = 12,
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(2, 8)
                };
                NetworkSidebarPanel.Children.Add(hint);
            }
            return;
        }

        foreach (var profile in _sidebarProfiles)
        {
            NetworkSidebarPanel.Children.Add(BuildSidebarItem(profile, selectedId));
        }
    }

    /// <summary>
    /// 构造一个网络条目：外层容器内并列放置选中按钮和快捷启停按钮。
    /// </summary>
    /// <param name="profile">网络配置，类型为 NetworkProfile，不可为空，必填。</param>
    /// <param name="selectedId">当前选中网络主键，类型为 long，取值为零或正数，必填。</param>
    /// <returns>条目容器控件，类型为 Border。</returns>
    private Border BuildSidebarItem(NetworkProfile profile, long selectedId)
    {
        var status = _runtimeManager!.GetStatus(profile.InstanceName);

        // 容器负责选中高亮样式；内部两个按钮并列，互不嵌套，保证快捷启停可点击。
        var container = new Border { Classes = { "sidebar-item" } };
        if (profile.Id == selectedId)
        {
            container.Classes.Add("selected");
        }

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(1, GridUnitType.Star)));
        grid.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(32, GridUnitType.Pixel)));

        var selectButton = new Button
        {
            Classes = { "item-select" },
            Content = BuildSidebarItemContent(profile, status),
            Tag = profile.Id
        };
        selectButton.Click += SidebarItemButton_Click;
        grid.Children.Add(selectButton);

        var quickButton = CreateQuickToggleButton(profile.Id, status);
        quickButton.Margin = new Thickness(0, 0, 4, 0);
        Grid.SetColumn(quickButton, 1);
        grid.Children.Add(quickButton);

        container.Child = grid;
        return container;
    }

    /// <summary>
    /// 创建条目右侧的快捷启停图标按钮。
    /// </summary>
    /// <param name="profileId">网络主键，类型为 long，取值为大于零的数据库主键，必填。</param>
    /// <param name="status">该网络当前运行状态，类型为 CoreProcessStatus，取值为枚举定义的状态，必填。</param>
    /// <returns>快捷启停按钮，类型为 Button。</returns>
    private Button CreateQuickToggleButton(long profileId, CoreProcessStatus status)
    {
        // 运行或切换中的网络显示停止图标，其余显示启动图标；切换期间禁用。
        var isStopAction = status is CoreProcessStatus.Running or CoreProcessStatus.Stopping;
        var quickButton = new Button
        {
            Classes = { "quick-toggle" },
            Tag = profileId,
            IsEnabled = status is CoreProcessStatus.Running or CoreProcessStatus.Stopped or CoreProcessStatus.Failed,
            VerticalAlignment = VerticalAlignment.Center,
            Content = new PathIcon
            {
                Width = 12,
                Height = 12,
                Foreground = new SolidColorBrush(Color.Parse(isStopAction ? "#98A2B3" : "#0F9F8F")),
                Data = Geometry.Parse(isStopAction ? "M6,6H18V18H6V6Z" : "M8,5V19L19,12L8,5Z")
            }
        };
        ToolTip.SetTip(quickButton, isStopAction ? "停止网络" : "启动网络");
        quickButton.Click += SidebarQuickToggleButton_Click;
        return quickButton;
    }

    /// <summary>
    /// 响应条目快捷启停按钮，启动或停止对应网络。
    /// </summary>
    /// <param name="sender">触发事件的快捷按钮，类型为对象，可为空，非必填。</param>
    /// <param name="e">路由事件参数，类型为 RoutedEventArgs，不可为空，必填。</param>
    private async void SidebarQuickToggleButton_Click(object? sender, RoutedEventArgs e)
    {
        // 快捷按钮与选中按钮并列，只启停对应网络，不改变当前选中网络。
        if (_runtimeManager is null || sender is not Button { Tag: long profileId }) return;
        var profile = _sidebarProfiles.FirstOrDefault(item => item.Id == profileId);
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

    /// <summary>
    /// 构造侧边栏网络条目的文字内容。
    /// </summary>
    /// <param name="profile">网络配置，类型为 NetworkProfile，不可为空，必填。</param>
    /// <param name="status">该网络当前运行状态，类型为 CoreProcessStatus，取值为枚举定义的状态，必填。</param>
    /// <returns>条目内容控件，类型为 Control。</returns>
    private static Control BuildSidebarItemContent(NetworkProfile profile, CoreProcessStatus status)
    {
        var content = new StackPanel { Spacing = 3 };
        content.Children.Add(new TextBlock
        {
            Text = profile.ProfileName,
            FontSize = 12,
            FontWeight = FontWeight.SemiBold,
            Foreground = new SolidColorBrush(Color.Parse("#344054")),
            TextTrimming = TextTrimming.CharacterEllipsis
        });

        // 副行显示状态圆点和网段摘要，体现每个网络的独立运行状态。
        var subnet = string.IsNullOrWhiteSpace(profile.Ipv4) ? null : profile.Ipv4.Trim();
        var detailPanel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        detailPanel.Children.Add(new Ellipse
        {
            Width = 7,
            Height = 7,
            VerticalAlignment = VerticalAlignment.Center,
            Fill = GetStatusBrush(status)
        });
        detailPanel.Children.Add(new TextBlock
        {
            Text = subnet is null ? GetStatusText(status) : $"{GetStatusText(status)} · {subnet}",
            FontSize = 10,
            Foreground = new SolidColorBrush(Color.Parse("#667085")),
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis
        });
        content.Children.Add(detailPanel);
        return content;
    }

    /// <summary>
    /// 构造未保存新网络的临时条目。
    /// </summary>
    /// <param name="profileName">网络显示名称，类型为字符串，取值为任意文本，必填。</param>
    /// <returns>临时条目控件，类型为 Border。</returns>
    private static Border BuildUnsavedSidebarItem(string profileName)
    {
        var content = new StackPanel { Spacing = 3 };
        content.Children.Add(new TextBlock
        {
            Text = profileName,
            FontSize = 12,
            FontWeight = FontWeight.SemiBold,
            Foreground = new SolidColorBrush(Color.Parse("#98A2B3")),
            TextTrimming = TextTrimming.CharacterEllipsis
        });
        content.Children.Add(new TextBlock
        {
            Text = "未保存",
            FontSize = 10,
            Foreground = new SolidColorBrush(Color.Parse("#98A2B3"))
        });
        return new Border
        {
            Background = new SolidColorBrush(Color.Parse("#F7F8FA")),
            BorderBrush = new SolidColorBrush(Color.Parse("#D9DEE6")),
            BorderThickness = new Thickness(1),
            CornerRadius = new Avalonia.CornerRadius(5),
            Padding = new Thickness(9, 8),
            Margin = new Thickness(0, 0, 0, 5),
            Child = content
        };
    }

    /// <summary>
    /// 响应侧边栏网络条目点击并选中该网络。
    /// </summary>
    /// <param name="sender">触发事件的条目按钮，类型为对象，可为空，非必填。</param>
    /// <param name="e">路由事件参数，类型为 RoutedEventArgs，不可为空，必填。</param>
    private void SidebarItemButton_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: long profileId }) OpenNetworkDetail(profileId);
    }

    /// <summary>清除选择并显示空状态。</summary>
    private void ClearSelection()
    {
        _selectedProfile = null;
        DetailRoot.IsVisible = false;
        DetailEmptyRoot.IsVisible = true;
        RenderSidebar();
    }

    /// <summary>显示选中网络的详情并默认打开配置标签。</summary>
    private void ShowDetail()
    {
        DetailEmptyRoot.IsVisible = false;
        DetailRoot.IsVisible = true;
        DetailTabControl.SelectedIndex = 0;
    }

    /// <summary>
    /// 新建一个未保存的网络并进入详情编辑。
    /// </summary>
    /// <param name="sender">触发事件的新建按钮，类型为对象，可为空，非必填。</param>
    /// <param name="e">路由事件参数，类型为 RoutedEventArgs，不可为空，必填。</param>
    private void NewNetworkButton_Click(object? sender, RoutedEventArgs e)
    {
        _selectedProfile = new NetworkProfile { ProfileName = "新网络", InstanceName = $"easytier-{DateTime.Now:HHmmss}", NetworkName = "easytier" };
        FillEditor(_selectedProfile);
        UpdateDetailHeader();
        ShowDetail();
        RenderSidebar();
    }

    /// <summary>
    /// 启动或停止详情页当前网络。
    /// </summary>
    /// <param name="sender">触发事件的启停按钮，类型为对象，可为空，非必填。</param>
    /// <param name="e">路由事件参数，类型为 RoutedEventArgs，不可为空，必填。</param>
    private async void StartNetworkButton_Click(object? sender, RoutedEventArgs e)
    {
        if (_selectedProfile is null || _runtimeManager is null) return;
        var status = _runtimeManager.GetStatus(_selectedProfile.InstanceName);

        // 运行中或状态切换中的网络执行停止，其余状态执行启动。
        if (status is CoreProcessStatus.Running or CoreProcessStatus.Starting or CoreProcessStatus.Stopping)
        {
            await _runtimeManager.StopAsync(_selectedProfile.InstanceName);
        }
        else
        {
            await _runtimeManager.StartAsync(_selectedProfile);
        }
    }

    /// <summary>
    /// 删除详情页当前网络；未保存的新网络视为放弃编辑。
    /// </summary>
    /// <param name="sender">触发事件的删除按钮，类型为对象，可为空，非必填。</param>
    /// <param name="e">路由事件参数，类型为 RoutedEventArgs，不可为空，必填。</param>
    private void DeleteNetworkButton_Click(object? sender, RoutedEventArgs e)
    {
        if (_repository is null || _selectedProfile is null) return;

        // 未保存的新网络没有数据库记录，直接清除选择。
        if (_selectedProfile.Id == 0)
        {
            ClearSelection();
            return;
        }

        if (_runtimeManager?.GetStatus(_selectedProfile.InstanceName) is CoreProcessStatus.Running or CoreProcessStatus.Starting or CoreProcessStatus.Stopping)
        {
            ProfileStatusText.Text = "请先停止运行中的网络";
            return;
        }

        _repository.Delete(_selectedProfile.Id);
        ClearSelection();
    }

    /// <summary>
    /// 保存详情页编辑器中的网络参数。
    /// </summary>
    /// <param name="sender">触发事件的保存按钮，类型为对象，可为空，非必填。</param>
    /// <param name="e">路由事件参数，类型为 RoutedEventArgs，不可为空，必填。</param>
    private void SaveProfileButton_Click(object? sender, RoutedEventArgs e)
    {
        if (_repository is null || _selectedProfile is null) return;
        var profile = _selectedProfile;
        var originalInstanceName = profile.InstanceName;

        // 运行中的实例参数不可直接修改，避免数据库配置与已启动实例脱节。
        if (_runtimeManager?.GetStatus(originalInstanceName) is CoreProcessStatus.Running or CoreProcessStatus.Starting or CoreProcessStatus.Stopping)
        {
            ProfileStatusText.Text = "请先停止网络再保存修改";
            return;
        }

        profile.ProfileName = string.IsNullOrWhiteSpace(ProfileNameTextBox.Text) ? "未命名网络" : ProfileNameTextBox.Text.Trim();
        profile.InstanceName = string.IsNullOrWhiteSpace(InstanceNameTextBox.Text) ? "easytier-desktop" : InstanceNameTextBox.Text.Trim();
        profile.NetworkName = string.IsNullOrWhiteSpace(NetworkNameTextBox.Text) ? "easytier" : NetworkNameTextBox.Text.Trim();
        profile.NetworkSecret = EmptyToNull(NetworkSecretTextBox.Text);
        profile.Ipv4 = EmptyToNull(Ipv4TextBox.Text);
        profile.Hostname = EmptyToNull(HostnameTextBox.Text);
        profile.PeerUris = CollectSelectedPeerAddresses(); // 按勾选列表收集信令服务器地址。
        profile.AutoStart = AutoStartToggle.IsChecked == true;
        if (_repository.IsInstanceNameUsed(profile.InstanceName, profile.Id)) { ProfileStatusText.Text = "实例名称已被其他网络使用"; return; }
        _repository.Save(profile);
        _selectedProfile = profile;
        UpdateDetailHeader();
        RenderSidebar();
        ProfileStatusText.Text = "网络已保存";
    }

    /// <summary>
    /// 按服务器地址簿和当前配置重建信令服务器地址勾选列表。
    /// </summary>
    /// <param name="profile">网络配置，类型为 NetworkProfile，不可为空，必填。</param>
    private void RenderServerAddressList(NetworkProfile profile)
    {
        if (_serverRepository is null) return;
        ServerListPanel.Children.Clear();
        var selectedLines = ParseStoredPeerLines(profile.PeerUris);
        var servers = _serverRepository.GetAll();

        // 地址簿为空且配置中没有已保存地址时，提示先维护服务器列表。
        if (servers.Count == 0 && selectedLines.Count == 0)
        {
            ServerListPanel.Children.Add(new TextBlock
            {
                Classes = { "muted" },
                FontSize = 11,
                Text = "服务器列表为空，请先在“服务器列表”页添加"
            });
            return;
        }

        var usedAddresses = new List<string>(); // 地址簿中已展示的地址，用于识别配置里的自定义地址。
        foreach (var server in servers)
        {
            var address = server.Address.Trim();
            usedAddresses.Add(address);
            ServerListPanel.Children.Add(CreatePeerCheckBox($"{server.Name}（{address}）", address, selectedLines.Contains(address, StringComparer.Ordinal)));
        }

        // 配置中已保存但不在地址簿中的地址以自定义条目保留，勾选状态保持原样，避免保存时丢失。
        foreach (var line in selectedLines)
        {
            if (!usedAddresses.Contains(line, StringComparer.Ordinal))
            {
                ServerListPanel.Children.Add(CreatePeerCheckBox($"{line}（自定义地址）", line, true));
            }
        }
    }

    /// <summary>
    /// 创建一个信令服务器地址勾选行。
    /// </summary>
    /// <param name="displayText">条目显示文本，类型为字符串，取值为名称加地址或自定义标注，必填。</param>
    /// <param name="address">该条目对应的地址，类型为字符串，取值去除首尾空白的节点地址，必填。</param>
    /// <param name="isChecked">是否勾选，类型为 bool；地址已在当前配置中时为真。</param>
    /// <returns>勾选行控件，类型为 CheckBox。</returns>
    private static CheckBox CreatePeerCheckBox(string displayText, string address, bool isChecked)
    {
        var checkBox = new CheckBox
        {
            Content = new TextBlock
            {
                Text = displayText,
                FontSize = 12,
                TextTrimming = TextTrimming.CharacterEllipsis
            },
            Tag = address, // Tag 保存提交时使用的地址文本。
            IsChecked = isChecked
        };
        return checkBox;
    }

    /// <summary>
    /// 收集勾选中的信令服务器地址并按行合并。
    /// </summary>
    /// <returns>按行合并的地址文本，类型为字符串可空值；没有勾选时返回 null。</returns>
    private string? CollectSelectedPeerAddresses()
    {
        var addresses = ServerListPanel.Children.OfType<CheckBox>()
            .Where(box => box.IsChecked == true && box.Tag is string)
            .Select(box => ((string)box.Tag!).Trim())
            .Where(address => address.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .ToList();
        return addresses.Count == 0 ? null : string.Join(Environment.NewLine, addresses);
    }

    /// <summary>
    /// 解析配置中按行保存的信令服务器地址。
    /// </summary>
    /// <param name="peerUris">按行保存的地址文本，类型为字符串，可为空，非必填。</param>
    /// <returns>去重后的地址行列表，类型为 List&lt;string&gt;。</returns>
    private static List<string> ParseStoredPeerLines(string? peerUris)
    {
        return (peerUris ?? string.Empty)
            .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(line => !string.IsNullOrWhiteSpace(line))
            .Distinct(StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>
    /// 用网络参数回填编辑器控件。
    /// </summary>
    /// <param name="profile">网络配置，类型为 NetworkProfile，不可为空，必填。</param>
    private void FillEditor(NetworkProfile profile)
    {
        InstanceNameTextBox.Text = profile.InstanceName;
        ProfileNameTextBox.Text = profile.ProfileName;
        NetworkNameTextBox.Text = profile.NetworkName;
        NetworkSecretTextBox.Text = profile.NetworkSecret;
        Ipv4TextBox.Text = profile.Ipv4;
        HostnameTextBox.Text = profile.Hostname;
        RenderServerAddressList(profile); // 按地址簿和已保存地址重建信令服务器勾选列表。
        AutoStartToggle.IsChecked = profile.AutoStart;

        // 提示当前编辑来源：已保存网络已加载数据库参数，新网络尚未保存。
        ProfileStatusText.Text = profile.Id == 0 ? "新网络尚未保存" : "已加载数据库中的网络参数";
    }

    /// <summary>
    /// 响应刷新按钮，立即采集节点和路由数据。
    /// </summary>
    /// <param name="sender">触发事件的刷新按钮，类型为对象，可为空，非必填。</param>
    /// <param name="e">路由事件参数，类型为 RoutedEventArgs，不可为空，必填。</param>
    private void RefreshDataButton_Click(object? sender, RoutedEventArgs e)
    {
        _ = RefreshDetailDataAsync();
    }

    /// <summary>
    /// 响应标签页切换，进入节点或路由页时立即刷新数据。
    /// </summary>
    /// <param name="sender">触发事件的标签控件，类型为对象，可为空，非必填。</param>
    /// <param name="e">选择变化参数，类型为 SelectionChangedEventArgs，不可为空，必填。</param>
    private void DetailTabControl_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        // 配置页不消耗采集开销，只有节点和路由页需要立即刷新。
        if (DetailTabControl.SelectedIndex is 1 or 2)
        {
            _ = RefreshDetailDataAsync();
        }
    }

    /// <summary>
    /// 响应定时器心跳，详情可见时自动刷新节点和路由数据。
    /// </summary>
    /// <param name="sender">触发事件的定时器，类型为对象，可为空，非必填。</param>
    /// <param name="e">事件参数，类型为 EventArgs，不可为空，必填。</param>
    private void DetailDataTimer_Tick(object? sender, EventArgs e)
    {
        // 定时刷新只在详情可见且已选中网络时执行，避免后台空转。
        if (DetailRoot.IsVisible && _selectedProfile is not null)
        {
            _ = RefreshDetailDataAsync();
        }
    }

    /// <summary>
    /// 采集并渲染当前网络的节点和路由数据。
    /// </summary>
    /// <returns>异步刷新任务，类型为 Task。</returns>
    private async Task RefreshDetailDataAsync()
    {
        if (_runtimeManager is null)
        {
            return;
        }

        // 未选中网络或网络尚未保存时没有可采集的实例。
        if (_selectedProfile is null || _selectedProfile.Id == 0)
        {
            RenderEmptyDataTabs(_selectedProfile is null ? "未选择网络" : "网络尚未保存，保存并启动后可查看数据");
            return;
        }

        var instanceName = _selectedProfile.InstanceName;

        // 只有运行中的实例才能通过 FFI 采集到节点和路由数据。
        if (_runtimeManager.GetStatus(instanceName) != CoreProcessStatus.Running)
        {
            RenderEmptyDataTabs("网络未运行，启动后可查看节点和路由数据");
            return;
        }

        // 上一次采集尚未完成时跳过本次刷新，避免并发调用 FFI。
        if (_isLoadingDetailData)
        {
            return;
        }

        _isLoadingDetailData = true;
        try
        {
            var snapshot = await Task.Run(() => _runtimeManager.GetNetworkSnapshot(instanceName));

            // 采集期间用户可能切换到其他网络，实例名不匹配时放弃渲染。
            if (_selectedProfile?.InstanceName != instanceName)
            {
                return;
            }

            RenderDetailData(snapshot);
        }
        catch (Exception exception)
        {
            ApplicationLogging.GetLogger(nameof(NetworkView)).Error(exception, "刷新网络详情数据失败");
            RenderEmptyDataTabs("读取节点和路由数据失败，请查看日志");
        }
        finally
        {
            _isLoadingDetailData = false;
        }
    }

    /// <summary>
    /// 渲染一次快照到节点和路由表格。
    /// </summary>
    /// <param name="snapshot">网络运行快照，类型为 EasyTierNetworkSnapshot，可为空，非必填。</param>
    private void RenderDetailData(EasyTierNetworkSnapshot? snapshot)
    {
        // 快照缺失说明 FFI 没有上报该实例数据，通常为实例仍在初始化。
        if (snapshot is null)
        {
            RenderEmptyDataTabs("暂无运行数据，实例可能仍在初始化");
            return;
        }

        RenderPeerRows(snapshot);
        RenderRouteRows(snapshot);
        var updatedText = $"更新于 {DateTime.Now:HH:mm:ss}"; // 数据刷新时间标注。
        PeersUpdatedText.Text = updatedText;
        RoutesUpdatedText.Text = updatedText;
    }

    /// <summary>
    /// 在节点和路由表格中显示占位说明。
    /// </summary>
    /// <param name="reason">占位说明文本，类型为字符串，取值为非空原因描述，必填。</param>
    private void RenderEmptyDataTabs(string reason)
    {
        RenderHintPanel(PeersRowsPanel, reason);
        RenderHintPanel(RoutesRowsPanel, reason);
        PeersUpdatedText.Text = string.Empty;
        RoutesUpdatedText.Text = string.Empty;
    }

    /// <summary>
    /// 在指定表格容器中写入占位说明。
    /// </summary>
    /// <param name="panel">表格行容器，类型为 StackPanel，不可为空，必填。</param>
    /// <param name="reason">占位说明文本，类型为字符串，取值为非空原因描述，必填。</param>
    private static void RenderHintPanel(StackPanel panel, string reason)
    {
        panel.Children.Clear();
        panel.Children.Add(new TextBlock
        {
            Classes = { "muted" },
            Text = reason,
            FontSize = 12,
            Margin = new Thickness(0, 12),
            HorizontalAlignment = HorizontalAlignment.Center
        });
    }

    /// <summary>
    /// 渲染节点表格的表头和数据行。
    /// </summary>
    /// <param name="snapshot">网络运行快照，类型为 EasyTierNetworkSnapshot，不可为空，必填。</param>
    private void RenderPeerRows(EasyTierNetworkSnapshot snapshot)
    {
        PeersRowsPanel.Children.Clear();
        PeersRowsPanel.Children.Add(BuildPeerHeaderRow());

        // 实例已运行但还没有节点上报时显示空态提示。
        if (snapshot.Nodes.Count == 0)
        {
            RenderHintPanel(PeersRowsPanel, "暂无节点数据");
            return;
        }

        foreach (var node in snapshot.Nodes)
        {
            PeersRowsPanel.Children.Add(BuildPeerRow(node, snapshot.MyPeerId));
        }
    }

    /// <summary>
    /// 渲染路由表格的表头和数据行。
    /// </summary>
    /// <param name="snapshot">网络运行快照，类型为 EasyTierNetworkSnapshot，不可为空，必填。</param>
    private void RenderRouteRows(EasyTierNetworkSnapshot snapshot)
    {
        RoutesRowsPanel.Children.Clear();
        RoutesRowsPanel.Children.Add(BuildRouteHeaderRow());

        // 实例已运行但还没有路由上报时显示空态提示。
        if (snapshot.Routes.Count == 0)
        {
            RenderHintPanel(RoutesRowsPanel, "暂无路由数据");
            return;
        }

        foreach (var route in snapshot.Routes)
        {
            RoutesRowsPanel.Children.Add(BuildRouteRow(route, snapshot.MyPeerId));
        }
    }

    /// <summary>
    /// 创建节点表格使用的统一列定义。
    /// </summary>
    /// <returns>行网格，类型为 Grid。</returns>
    private static Grid CreatePeerRowGrid()
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(52, GridUnitType.Pixel)));
        grid.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(1.4, GridUnitType.Star)));
        grid.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(1.2, GridUnitType.Star)));
        grid.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(56, GridUnitType.Pixel)));
        grid.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(56, GridUnitType.Pixel)));
        grid.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(56, GridUnitType.Pixel)));
        grid.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(72, GridUnitType.Pixel)));
        grid.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(72, GridUnitType.Pixel)));
        return grid;
    }

    /// <summary>
    /// 创建路由表格使用的统一列定义。
    /// </summary>
    /// <returns>行网格，类型为 Grid。</returns>
    private static Grid CreateRouteRowGrid()
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(52, GridUnitType.Pixel)));
        grid.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(1.4, GridUnitType.Star)));
        grid.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(1.2, GridUnitType.Star)));
        grid.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(80, GridUnitType.Pixel)));
        grid.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(48, GridUnitType.Pixel)));
        grid.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(64, GridUnitType.Pixel)));
        grid.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(76, GridUnitType.Pixel)));
        return grid;
    }

    /// <summary>
    /// 构建节点表格表头行。
    /// </summary>
    /// <returns>表头控件，类型为 Border。</returns>
    private static Border BuildPeerHeaderRow()
    {
        var grid = CreatePeerRowGrid();
        AddCell(grid, 0, "节点ID", "table-header");
        AddCell(grid, 1, "主机名称", "table-header");
        AddCell(grid, 2, "虚拟地址", "table-header");
        AddCell(grid, 3, "延迟", "table-header");
        AddCell(grid, 4, "丢包率", "table-header");
        AddCell(grid, 5, "隧道", "table-header");
        AddCell(grid, 6, "上行", "table-header");
        AddCell(grid, 7, "下行", "table-header");
        return BuildHeaderContainer(grid);
    }

    /// <summary>
    /// 构建一个节点的数据行。
    /// </summary>
    /// <param name="node">节点信息，类型为 EasyTierNodeInfo，不可为空，必填。</param>
    /// <param name="myPeerId">本机节点编号，类型为 uint，取值为零或正数，必填。</param>
    /// <returns>数据行控件，类型为 Border。</returns>
    private static Border BuildPeerRow(EasyTierNodeInfo node, uint myPeerId)
    {
        var grid = CreatePeerRowGrid();
        AddCell(grid, 0, node.PeerId.ToString(), "table-cell-muted");
        var hostname = string.IsNullOrWhiteSpace(node.Hostname) ? $"节点 {node.PeerId}" : node.Hostname!.Trim();

        // 本机节点在主机名后附加标注，方便与远端节点区分。
        if (node.PeerId == myPeerId)
        {
            hostname += "（本机）";
        }

        AddCell(grid, 1, hostname, "table-cell");
        AddCell(grid, 2, node.VirtualIpv4 ?? "--", "table-cell-muted");
        AddCell(grid, 3, node.PathLatencyMs > 0 ? $"{node.PathLatencyMs} ms" : "--", "table-cell-muted");
        AddCell(grid, 4, FormatLossRate(node.LossRate), "table-cell-muted");
        AddCell(grid, 5, string.IsNullOrWhiteSpace(node.TunnelType) ? "--" : node.TunnelType!.ToUpperInvariant(), "table-cell-muted");
        AddCell(grid, 6, node.TxBytes > 0 ? FormatBytes(node.TxBytes) : "--", "table-cell-muted");
        AddCell(grid, 7, node.RxBytes > 0 ? FormatBytes(node.RxBytes) : "--", "table-cell-muted");
        return BuildDataRowContainer(grid);
    }

    /// <summary>
    /// 构建路由表格表头行。
    /// </summary>
    /// <returns>表头控件，类型为 Border。</returns>
    private static Border BuildRouteHeaderRow()
    {
        var grid = CreateRouteRowGrid();
        AddCell(grid, 0, "节点ID", "table-header");
        AddCell(grid, 1, "主机名称", "table-header");
        AddCell(grid, 2, "虚拟地址", "table-header");
        AddCell(grid, 3, "下一跳", "table-header");
        AddCell(grid, 4, "跳数", "table-header");
        AddCell(grid, 5, "路径延迟", "table-header");
        AddCell(grid, 6, "版本", "table-header");
        return BuildHeaderContainer(grid);
    }

    /// <summary>
    /// 构建一条路由的数据行。
    /// </summary>
    /// <param name="route">路由信息，类型为 EasyTierRouteInfo，不可为空，必填。</param>
    /// <param name="myPeerId">本机节点编号，类型为 uint，取值为零或正数，必填。</param>
    /// <returns>数据行控件，类型为 Border。</returns>
    private static Border BuildRouteRow(EasyTierRouteInfo route, uint myPeerId)
    {
        var grid = CreateRouteRowGrid();
        AddCell(grid, 0, route.PeerId.ToString(), "table-cell-muted");
        var hostname = string.IsNullOrWhiteSpace(route.Hostname) ? $"节点 {route.PeerId}" : route.Hostname!.Trim();

        // 本机节点在主机名后附加标注，方便与远端节点区分。
        if (route.PeerId == myPeerId)
        {
            hostname += "（本机）";
        }

        AddCell(grid, 1, hostname, "table-cell");
        AddCell(grid, 2, route.VirtualIpv4 ?? "--", "table-cell-muted");

        // 下一跳等于目标节点本身时表示直连，否则显示中转节点编号。
        var nextHopText = route.NextHopPeerId == route.PeerId ? "直连" : $"节点 {route.NextHopPeerId}";
        AddCell(grid, 3, nextHopText, "table-cell-muted");
        AddCell(grid, 4, route.Cost.ToString(), "table-cell-muted");
        AddCell(grid, 5, route.PathLatencyMs > 0 ? $"{route.PathLatencyMs} ms" : "--", "table-cell-muted");
        AddCell(grid, 6, string.IsNullOrWhiteSpace(route.Version) ? "--" : route.Version!, "table-cell-muted");
        return BuildDataRowContainer(grid);
    }

    /// <summary>
    /// 构建表格表头容器。
    /// </summary>
    /// <param name="grid">表头网格，类型为 Grid，不可为空，必填。</param>
    /// <returns>表头容器，类型为 Border。</returns>
    private static Border BuildHeaderContainer(Grid grid)
    {
        return new Border
        {
            Background = new SolidColorBrush(Color.Parse("#F7F8FA")),
            CornerRadius = new Avalonia.CornerRadius(5),
            Padding = new Thickness(10, 7),
            Child = grid
        };
    }

    /// <summary>
    /// 构建表格数据行容器。
    /// </summary>
    /// <param name="grid">数据行网格，类型为 Grid，不可为空，必填。</param>
    /// <returns>数据行容器，类型为 Border。</returns>
    private static Border BuildDataRowContainer(Grid grid)
    {
        return new Border
        {
            BorderBrush = new SolidColorBrush(Color.Parse("#EEF1F5")),
            BorderThickness = new Thickness(0, 0, 0, 1),
            Padding = new Thickness(10, 8),
            Child = grid
        };
    }

    /// <summary>
    /// 向行网格添加一个单元格文本。
    /// </summary>
    /// <param name="grid">目标行网格，类型为 Grid，不可为空，必填。</param>
    /// <param name="column">单元格列序号，类型为 int，取值为从零开始的列号，必填。</param>
    /// <param name="text">单元格文本，类型为字符串，取值为任意文本，必填。</param>
    /// <param name="styleClass">文本样式类名，类型为字符串，取值为 table-header、table-cell 或 table-cell-muted，必填。</param>
    private static void AddCell(Grid grid, int column, string text, string styleClass)
    {
        var textBlock = new TextBlock { Classes = { styleClass }, Text = text };
        Grid.SetColumn(textBlock, column);
        grid.Children.Add(textBlock);
    }

    /// <summary>
    /// 把丢包率格式化为百分比文本。
    /// </summary>
    /// <param name="lossRate">丢包率，类型为 double 可空值，取值为零到一，可为空。</param>
    /// <returns>丢包率文本，类型为字符串；未上报时返回占位符。</returns>
    private static string FormatLossRate(double? lossRate) => lossRate is null ? "--" : $"{lossRate.Value * 100:0.#}%";

    /// <summary>
    /// 把字节数格式化为可读的流量文本。
    /// </summary>
    /// <param name="bytes">字节数，类型为 ulong，取值为零或正数，必填。</param>
    /// <returns>流量文本，类型为字符串。</returns>
    private static string FormatBytes(ulong bytes) => bytes switch
    {
        >= 1024UL * 1024 * 1024 => $"{bytes / 1024.0 / 1024 / 1024:0.0} GB",
        >= 1024UL * 1024 => $"{bytes / 1024.0 / 1024:0.0} MB",
        >= 1024UL => $"{bytes / 1024.0:0.0} KB",
        _ => $"{bytes} B"
    };

    /// <summary>刷新详情页头部的标题、状态徽标和按钮状态。</summary>
    private void UpdateDetailHeader()
    {
        if (_selectedProfile is null)
        {
            DetailTitleText.Text = "未选择网络";
            DetailInstanceText.Text = "实例：--";
            DetailStatusText.Text = "已停止";
            return;
        }

        var profile = _selectedProfile;
        DetailTitleText.Text = profile.Id == 0 ? $"{profile.ProfileName}（未保存）" : profile.ProfileName;
        DetailInstanceText.Text = $"实例：{profile.InstanceName}";
        var status = _runtimeManager?.GetStatus(profile.InstanceName) ?? CoreProcessStatus.Stopped;
        DetailStatusText.Text = GetStatusText(status);

        // 头部按钮随状态切换：运行或切换中显示停止并禁用删除，其余显示启动。
        var isStopAction = status is CoreProcessStatus.Running or CoreProcessStatus.Starting or CoreProcessStatus.Stopping;
        StartNetworkButton.Content = isStopAction ? "停止网络" : "启动网络";
        DeleteNetworkButton.IsEnabled = !isStopAction;
    }

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

    /// <summary>
    /// 获取运行状态对应的显示颜色。
    /// </summary>
    /// <param name="status">运行状态，类型为 CoreProcessStatus，取值为枚举定义的状态，必填。</param>
    /// <returns>状态颜色画刷，类型为 IBrush。</returns>
    private static IBrush GetStatusBrush(CoreProcessStatus status) => status switch
    {
        CoreProcessStatus.Running => StatusRunningBrush,
        CoreProcessStatus.Starting or CoreProcessStatus.Stopping => StatusTransitionBrush,
        CoreProcessStatus.Failed => StatusFailedBrush,
        _ => StatusStoppedBrush
    };

    /// <summary>
    /// 将空白文本转换为可空字段。
    /// </summary>
    /// <param name="value">待转换文本，类型为字符串，可为空，非必填。</param>
    /// <returns>去除首尾空白后的文本，类型为字符串可空值。</returns>
    private static string? EmptyToNull(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
