using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using AvaloniauiForEasytier.Services;
using System;
using System.Collections.Generic;
using System.Linq;

namespace AvaloniauiForEasytier.Views;

/// <summary>
/// 管理多个 EasyTier 网络配置及其独立运行状态。
/// </summary>
public partial class NetworkView : UserControl
{
    private readonly NetworkProfileRepository? _repository;
    private readonly NetworkRuntimeManager? _runtimeManager;
    private NetworkProfile? _selectedProfile;

    /// <summary>初始化设计器使用的网络配置视图。</summary>
    public NetworkView() { InitializeComponent(); }

    /// <summary>
    /// 初始化多网络配置视图。
    /// </summary>
    /// <param name="repository">网络配置仓储，类型为 NetworkProfileRepository，不可为空，必填。</param>
    /// <param name="runtimeManager">多实例运行时管理器，类型为 NetworkRuntimeManager，不可为空，必填。</param>
    public NetworkView(NetworkProfileRepository repository, NetworkRuntimeManager runtimeManager)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _runtimeManager = runtimeManager ?? throw new ArgumentNullException(nameof(runtimeManager));
        InitializeComponent();
        NewProfileButton.Click += NewProfileButton_Click;
        SaveProfileButton.Click += SaveProfileButton_Click;
        DeleteProfileButton.Click += DeleteProfileButton_Click;
        StartProfileButton.Click += StartProfileButton_Click;
        StopProfileButton.Click += StopProfileButton_Click;
        _runtimeManager.StatusChanged += RuntimeManager_StatusChanged;
        LoadProfiles(null);
    }

    /// <summary>
    /// 响应运行时状态变化并刷新配置列表。
    /// </summary>
    /// <param name="instanceName">发生变化的实例名称，类型为字符串，取值为非空名称，必填。</param>
    /// <param name="eventArgs">状态变化参数，类型为 CoreStatusChangedEventArgs，不可为空，必填。</param>
    private void RuntimeManager_StatusChanged(string instanceName, CoreStatusChangedEventArgs eventArgs)
    {
        Dispatcher.UIThread.Post(() => { RefreshProfileListStatus(); UpdateEditorStatus(); });
    }

    /// <summary>
    /// 只刷新左侧配置列表中的运行状态，不覆盖右侧未保存编辑内容。
    /// </summary>
    private void RefreshProfileListStatus()
    {
        if (_repository is null) return;
        var profiles = _repository.GetAll();
        foreach (var profile in profiles)
        {
            if (ProfileListPanel.Children.FirstOrDefault(control => control is Button button && button.Tag is long id && id == profile.Id) is Button button)
            {
                button.Content = $"{profile.ProfileName}\n{GetStatusText(profile.InstanceName)}";
            }
        }
    }

    /// <summary>
    /// 读取数据库中的配置并重建左侧列表。
    /// </summary>
    /// <param name="selectedId">需要重新选中的配置主键，类型为 long 可空值，取值为已存在主键或空，非必填。</param>
    private void LoadProfiles(long? selectedId)
    {
        if (_repository is null) return;
        ProfileListPanel.Children.Clear();
        var profiles = _repository.GetAll();
        foreach (var profile in profiles)
        {
            var button = new Button
            {
                Content = $"{profile.ProfileName}\n{GetStatusText(profile.InstanceName)}",
                Tag = profile.Id,
                HorizontalContentAlignment = Avalonia.Layout.HorizontalAlignment.Left,
                HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch,
                Margin = new Avalonia.Thickness(0, 0, 0, 5),
                Padding = new Avalonia.Thickness(9, 7)
            };
            button.Click += ProfileButton_Click;
            ProfileListPanel.Children.Add(button);
        }
        var idToSelect = selectedId ?? profiles.FirstOrDefault()?.Id;
        if (idToSelect.HasValue) SelectProfile(idToSelect.Value); else ClearEditor();
    }

    /// <summary>
    /// 响应左侧配置选择。
    /// </summary>
    /// <param name="sender">触发事件的配置按钮，类型为对象，可为空，非必填。</param>
    /// <param name="e">路由事件参数，类型为 RoutedEventArgs，不可为空，必填。</param>
    private void ProfileButton_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.Tag is long id) SelectProfile(id);
    }

    /// <summary>
    /// 选择一个配置并回填编辑器。
    /// </summary>
    /// <param name="id">配置主键，类型为 long，取值为正数数据库主键，必填。</param>
    private void SelectProfile(long id)
    {
        _selectedProfile = _repository?.GetById(id);
        if (_selectedProfile is null) return;
        InstanceNameTextBox.Text = _selectedProfile.InstanceName;
        ProfileNameTextBox.Text = _selectedProfile.ProfileName;
        NetworkNameTextBox.Text = _selectedProfile.NetworkName;
        NetworkSecretTextBox.Text = _selectedProfile.NetworkSecret;
        Ipv4TextBox.Text = _selectedProfile.Ipv4;
        HostnameTextBox.Text = _selectedProfile.Hostname;
        PeerTextBox.Text = _selectedProfile.PeerUris;
        AutoStartToggle.IsChecked = _selectedProfile.AutoStart;
        UpdateEditorStatus();
    }

    /// <summary>
    /// 新建一个未保存网络配置。
    /// </summary>
    /// <param name="sender">触发事件的按钮，类型为对象，可为空，非必填。</param>
    /// <param name="e">路由事件参数，类型为 RoutedEventArgs，不可为空，必填。</param>
    private void NewProfileButton_Click(object? sender, RoutedEventArgs e)
    {
        _selectedProfile = new NetworkProfile { ProfileName = "新网络", InstanceName = $"easytier-{DateTime.Now:HHmmss}", NetworkName = "easytier" };
        InstanceNameTextBox.Text = _selectedProfile.InstanceName;
        ProfileNameTextBox.Text = _selectedProfile.ProfileName;
        NetworkNameTextBox.Text = _selectedProfile.NetworkName;
        NetworkSecretTextBox.Text = Ipv4TextBox.Text = HostnameTextBox.Text = PeerTextBox.Text = string.Empty;
        AutoStartToggle.IsChecked = false;
        UpdateEditorStatus();
    }

    /// <summary>
    /// 保存当前编辑器中的网络配置。
    /// </summary>
    /// <param name="sender">触发事件的按钮，类型为对象，可为空，非必填。</param>
    /// <param name="e">路由事件参数，类型为 RoutedEventArgs，不可为空，必填。</param>
    private void SaveProfileButton_Click(object? sender, RoutedEventArgs e)
    {
        if (_repository is null || _selectedProfile is null) return;
        var profile = _selectedProfile;
        var originalInstanceName = profile.InstanceName;
        // 运行中的实例配置不可直接改名或改参数，避免数据库配置与已启动实例脱节。
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
        if (_repository.IsInstanceNameUsed(profile.InstanceName, profile.Id)) { ProfileStatusText.Text = "实例名称已被其他配置使用"; return; }
        _repository.Save(profile);
        _selectedProfile = profile;
        LoadProfiles(profile.Id);
        ProfileStatusText.Text = "配置已保存";
    }

    /// <summary>
    /// 删除当前网络配置；运行中的配置需要先停止。
    /// </summary>
    /// <param name="sender">触发事件的按钮，类型为对象，可为空，非必填。</param>
    /// <param name="e">路由事件参数，类型为 RoutedEventArgs，不可为空，必填。</param>
    private void DeleteProfileButton_Click(object? sender, RoutedEventArgs e)
    {
        if (_repository is null || _selectedProfile is null || _selectedProfile.Id == 0) return;
        if (_runtimeManager?.GetStatus(_selectedProfile.InstanceName) == CoreProcessStatus.Running) { ProfileStatusText.Text = "请先停止运行中的网络"; return; }
        _repository.Delete(_selectedProfile.Id);
        _selectedProfile = null;
        LoadProfiles(null);
    }

    /// <summary>
    /// 启动当前配置对应的网络实例。
    /// </summary>
    /// <param name="sender">触发事件的按钮，类型为对象，可为空，非必填。</param>
    /// <param name="e">路由事件参数，类型为 RoutedEventArgs，不可为空，必填。</param>
    private async void StartProfileButton_Click(object? sender, RoutedEventArgs e)
    {
        if (_selectedProfile is not null && _runtimeManager is not null) await _runtimeManager.StartAsync(_selectedProfile);
    }

    /// <summary>
    /// 停止当前配置对应的网络实例。
    /// </summary>
    /// <param name="sender">触发事件的按钮，类型为对象，可为空，非必填。</param>
    /// <param name="e">路由事件参数，类型为 RoutedEventArgs，不可为空，必填。</param>
    private async void StopProfileButton_Click(object? sender, RoutedEventArgs e)
    {
        if (_selectedProfile is not null && _runtimeManager is not null) await _runtimeManager.StopAsync(_selectedProfile.InstanceName);
    }

    /// <summary>更新编辑器中的实例运行状态和可用操作。</summary>
    private void UpdateEditorStatus()
    {
        if (_selectedProfile is null || _runtimeManager is null) { ProfileStatusText.Text = "未选择配置"; return; }
        var status = _runtimeManager.GetStatus(_selectedProfile.InstanceName);
        ProfileStatusText.Text = GetStatusText(status);
        StartProfileButton.IsEnabled = status is CoreProcessStatus.Stopped or CoreProcessStatus.Failed;
        StopProfileButton.IsEnabled = status is CoreProcessStatus.Running or CoreProcessStatus.Starting;
    }

    /// <summary>清空编辑器中的字段。</summary>
    private void ClearEditor()
    {
        _selectedProfile = null;
        InstanceNameTextBox.Text = ProfileNameTextBox.Text = NetworkNameTextBox.Text = NetworkSecretTextBox.Text = Ipv4TextBox.Text = HostnameTextBox.Text = PeerTextBox.Text = string.Empty;
        AutoStartToggle.IsChecked = false;
        UpdateEditorStatus();
    }

    /// <summary>
    /// 获取配置对应的可读状态文本。
    /// </summary>
    /// <param name="instanceName">实例名称，类型为字符串，取值为非空名称，必填。</param>
    /// <returns>状态文本，类型为字符串。</returns>
    private string GetStatusText(string instanceName) => GetStatusText(_runtimeManager?.GetStatus(instanceName) ?? CoreProcessStatus.Stopped);

    /// <summary>
    /// 将运行状态转换为中文文本。
    /// </summary>
    /// <param name="status">运行状态，类型为 CoreProcessStatus，取值为枚举定义的状态，必填。</param>
    /// <returns>状态文本，类型为字符串。</returns>
    private static string GetStatusText(CoreProcessStatus status) => status switch { CoreProcessStatus.Running => "运行中", CoreProcessStatus.Starting => "启动中", CoreProcessStatus.Stopping => "停止中", CoreProcessStatus.Failed => "异常", _ => "已停止" };

    /// <summary>
    /// 将空白文本转换为可空字段。
    /// </summary>
    /// <param name="value">待转换文本，类型为字符串，可为空，非必填。</param>
    /// <returns>去除首尾空白后的文本，类型为字符串可空值。</returns>
    private static string? EmptyToNull(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
