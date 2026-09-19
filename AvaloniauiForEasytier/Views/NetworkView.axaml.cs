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
        PickServersButton.Click += PickServersButton_Click;
        _runtimeManager.StatusChanged += RuntimeManager_StatusChanged;
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
    /// 响应任一网络状态变化并刷新侧边栏状态和详情头部。
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
            var status = _runtimeManager.GetStatus(profile.InstanceName);
            var button = new Button
            {
                Content = BuildSidebarItemContent(profile, status, CreateQuickToggleButton(profile.Id, status)),
                Tag = profile.Id,
                Margin = new Thickness(0, 0, 0, 5)
            };
            button.Classes.Add("profile-item");

            // 只有主键匹配选中网络的条目显示选中状态。
            if (profile.Id == selectedId)
            {
                button.Classes.Add("selected");
            }

            button.Click += SidebarItemButton_Click;
            NetworkSidebarPanel.Children.Add(button);
        }
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
        // 阻止事件冒泡到条目按钮，避免快捷启停时切换正在编辑的网络。
        e.Handled = true;
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
    /// 构造侧边栏网络条目的显示内容。
    /// </summary>
    /// <param name="profile">网络配置，类型为 NetworkProfile，不可为空，必填。</param>
    /// <param name="status">该网络当前运行状态，类型为 CoreProcessStatus，取值为枚举定义的状态，必填。</param>
    /// <param name="quickToggleButton">条目右侧的快捷启停按钮，类型为 Control，不可为空，必填。</param>
    /// <returns>条目内容控件，类型为 Control。</returns>
    private static Control BuildSidebarItemContent(NetworkProfile profile, CoreProcessStatus status, Control quickToggleButton)
    {
        var root = new Grid();
        root.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(1, GridUnitType.Star)));
        root.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(30, GridUnitType.Pixel)));

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

        root.Children.Add(content);
        Grid.SetColumn(quickToggleButton, 1);
        root.Children.Add(quickToggleButton);
        return root;
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
        profile.PeerUris = EmptyToNull(PeerTextBox.Text);
        profile.AutoStart = AutoStartToggle.IsChecked == true;
        if (_repository.IsInstanceNameUsed(profile.InstanceName, profile.Id)) { ProfileStatusText.Text = "实例名称已被其他网络使用"; return; }
        _repository.Save(profile);
        _selectedProfile = profile;
        UpdateDetailHeader();
        RenderSidebar();
        ProfileStatusText.Text = "网络已保存";
    }

    /// <summary>
    /// 打开服务器地址勾选列表。
    /// </summary>
    /// <param name="sender">触发事件的选择按钮，类型为对象，可为空，非必填。</param>
    /// <param name="e">路由事件参数，类型为 RoutedEventArgs，不可为空，必填。</param>
    private void PickServersButton_Click(object? sender, RoutedEventArgs e)
    {
        RebuildServerPicker();
        ServerPickerPopup.IsOpen = true;
    }

    /// <summary>按数据库重建服务器地址勾选列表。</summary>
    private void RebuildServerPicker()
    {
        if (_serverRepository is null) return;
        ServerPickerPanel.Children.Clear();
        var servers = _serverRepository.GetAll();
        if (servers.Count == 0)
        {
            ServerPickerPanel.Children.Add(new TextBlock
            {
                Classes = { "muted" },
                FontSize = 11,
                Text = "服务器列表为空，请先在“服务器列表”页添加"
            });
            return;
        }

        var currentLines = ParsePeerLines();
        foreach (var server in servers)
        {
            var checkBox = new CheckBox
            {
                Content = new TextBlock
                {
                    Text = $"{server.Name}（{server.Address}）",
                    FontSize = 12,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    MaxWidth = 360
                },
                Tag = server,
                IsChecked = currentLines.Contains(server.Address.Trim(), StringComparer.Ordinal)
            };
            checkBox.Click += ServerPickerCheckBox_Click;
            ServerPickerPanel.Children.Add(checkBox);
        }
    }

    /// <summary>
    /// 响应服务器地址勾选变化，同步入口节点地址文本。
    /// </summary>
    /// <param name="sender">触发事件的复选框，类型为对象，可为空，非必填。</param>
    /// <param name="e">路由事件参数，类型为 RoutedEventArgs，不可为空，必填。</param>
    private void ServerPickerCheckBox_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is not CheckBox { Tag: ServerEndpoint server } checkBox) return;
        var lines = ParsePeerLines();
        var address = server.Address.Trim();

        // 勾选时追加地址行，取消勾选时移除对应行。
        var contains = lines.Contains(address, StringComparer.Ordinal);
        if (checkBox.IsChecked == true && !contains)
        {
            lines.Add(address);
        }
        else if (checkBox.IsChecked != true && contains)
        {
            lines.RemoveAll(line => string.Equals(line, address, StringComparison.Ordinal));
        }

        PeerTextBox.Text = string.Join(Environment.NewLine, lines);
    }

    /// <summary>
    /// 解析入口节点地址文本框中的地址行。
    /// </summary>
    /// <returns>去重后的地址行列表，类型为 List&lt;string&gt;。</returns>
    private List<string> ParsePeerLines()
    {
        return (PeerTextBox.Text ?? string.Empty)
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
        PeerTextBox.Text = profile.PeerUris;
        AutoStartToggle.IsChecked = profile.AutoStart;

        // 提示当前编辑来源：已保存网络已加载数据库参数，新网络尚未保存。
        ProfileStatusText.Text = profile.Id == 0 ? "新网络尚未保存" : "已加载数据库中的网络参数";
    }

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
