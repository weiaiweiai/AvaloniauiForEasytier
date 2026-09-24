using Avalonia.Controls;
using Avalonia.Interactivity;
using AvaloniauiForEasytier.Services;
using System;

namespace AvaloniauiForEasytier.Views;

/// <summary>
/// 应用设置视图；常规设置表单读写数据库中的全局唯一设置行。
/// </summary>
public partial class SettingsView : UserControl
{
    private readonly ApplicationSettingsRepository? _repository;
    private ApplicationSettings? _settings; // 当前编辑中的全局设置行；null 表示仓储未注入。

    /// <summary>初始化设计器使用的应用设置视图。</summary>
    public SettingsView() { InitializeComponent(); }

    /// <summary>
    /// 初始化应用设置视图并回填数据库中的设置。
    /// </summary>
    /// <param name="repository">应用设置仓储，类型为 ApplicationSettingsRepository，不可为空，必填。</param>
    public SettingsView(ApplicationSettingsRepository repository)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        InitializeComponent();
        SaveSettingsButton.Click += SaveSettingsButton_Click;
        GeneralCategoryButton.Click += CategoryButton_Click;
        AppearanceCategoryButton.Click += CategoryButton_Click;
        NotificationCategoryButton.Click += CategoryButton_Click;
        ShowCategory("general");
        LoadSettings();
    }

    /// <summary>
    /// 响应左侧设置类别点击并切换右侧显示的设置分区。
    /// </summary>
    /// <param name="sender">触发事件的类别按钮，类型为对象，可为空，非必填。</param>
    /// <param name="e">路由事件参数，类型为 RoutedEventArgs，不可为空，必填。</param>
    private void CategoryButton_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string categoryKey })
        {
            ShowCategory(categoryKey);
        }
    }

    /// <summary>
    /// 按类别标识切换设置分区的可见性、标题和导航选中状态。
    /// </summary>
    /// <param name="categoryKey">类别标识，类型为字符串，取值为 general、appearance 或 notification，必填。</param>
    private void ShowCategory(string categoryKey)
    {
        // 每个类别只显示自己的设置分区，避免用户在长列表中查找。
        GeneralSection.IsVisible = categoryKey == "general";
        AppearanceSection.IsVisible = categoryKey == "appearance";
        NotificationSection.IsVisible = categoryKey == "notification";

        (CategoryTitleText.Text, CategoryCaptionText.Text) = categoryKey switch
        {
            "appearance" => ("外观设置", "调整主题配色与界面密度。"),
            "notification" => ("通知设置", "决定网络状态变化时是否提醒以及提醒范围。"),
            _ => ("常规设置", "控制应用启动与后台驻留行为。")
        };

        // 尚未生效的说明只在涉及未实现能力的类别下显示。
        PendingFeatureText.IsVisible = categoryKey != "notification";
        PendingFeatureText.Text = categoryKey == "appearance"
            ? "主题和界面密度目前仅保存设置值，动态切换尚未接入。"
            : "开机启动、托盘驻留和更新检查目前仅保存设置值，实际行为尚未接入。";

        foreach (var button in GetCategoryButtons())
        {
            button.Classes.Remove("selected");
        }

        var selectedButton = categoryKey switch
        {
            "appearance" => AppearanceCategoryButton,
            "notification" => NotificationCategoryButton,
            _ => GeneralCategoryButton
        };
        selectedButton.Classes.Add("selected");
    }

    /// <summary>
    /// 获取全部设置类别按钮。
    /// </summary>
    /// <returns>类别按钮数组，类型为 Button 数组。</returns>
    private Button[] GetCategoryButtons()
    {
        return new[] { GeneralCategoryButton, AppearanceCategoryButton, NotificationCategoryButton };
    }

    /// <summary>
    /// 从数据库读取全局设置并回填界面控件。
    /// </summary>
    private void LoadSettings()
    {
        if (_repository is null) return;
        try
        {
            _settings = _repository.GetOrCreate();
        }
        catch (Exception exception)
        {
            // 设置读取失败不阻断界面显示，仅提示用户保存时重试。
            _settings = null;
            SettingsStatusText.Text = "设置读取失败，保存时重试";
            ApplicationLogging.GetLogger(nameof(SettingsView)).Error(exception, "读取应用设置失败");
            return;
        }

        LaunchAtLoginToggle.IsChecked = _settings.LaunchAtLogin;
        MinimizeToTrayToggle.IsChecked = _settings.MinimizeToTray;
        CheckUpdatesToggle.IsChecked = _settings.CheckForUpdates;
        ThemeModeComboBox.SelectedIndex = GetThemeIndex(_settings.ThemeMode);
        UiDensityComboBox.SelectedIndex = GetDensityIndex(_settings.UiDensity);
        StatusNotificationToggle.IsChecked = _settings.StatusNotification;
        NotificationLevelComboBox.SelectedIndex = GetNotifyLevelIndex(_settings.NotificationLevel);
        SettingsStatusText.Text = $"已加载设置，最后修改于 {_settings.UpdatedAt:yyyy-MM-dd HH:mm}";
    }

    /// <summary>
    /// 响应保存按钮，把界面控件值写入数据库。
    /// </summary>
    /// <param name="sender">触发事件的保存按钮，类型为对象，可为空，非必填。</param>
    /// <param name="e">路由事件参数，类型为 RoutedEventArgs，不可为空，必填。</param>
    private void SaveSettingsButton_Click(object? sender, RoutedEventArgs e)
    {
        if (_repository is null) return;

        // 读取失败或首次写入前没有内存副本时，先向仓储索取全局设置行。
        var settings = _settings ?? _repository.GetOrCreate();

        settings.LaunchAtLogin = LaunchAtLoginToggle.IsChecked == true;
        settings.MinimizeToTray = MinimizeToTrayToggle.IsChecked == true;
        settings.CheckForUpdates = CheckUpdatesToggle.IsChecked == true;
        settings.ThemeMode = GetThemeValue(ThemeModeComboBox.SelectedIndex);
        settings.UiDensity = GetDensityValue(UiDensityComboBox.SelectedIndex);
        settings.StatusNotification = StatusNotificationToggle.IsChecked == true;
        settings.NotificationLevel = GetNotifyLevelValue(NotificationLevelComboBox.SelectedIndex);

        try
        {
            _settings = _repository.Save(settings);
            SettingsStatusText.Text = $"设置已保存，时间 {DateTime.Now:HH:mm:ss}";
        }
        catch (Exception exception)
        {
            SettingsStatusText.Text = "设置保存失败，请查看日志";
            ApplicationLogging.GetLogger(nameof(SettingsView)).Error(exception, "保存应用设置失败");
        }
    }

    /// <summary>
    /// 把主题存储值转换为下拉框序号。
    /// </summary>
    /// <param name="value">主题存储值，类型为字符串，取值为 FollowSystem、Light 或 Dark，必填。</param>
    /// <returns>下拉框序号，类型为 int；未知值回退为零。</returns>
    private static int GetThemeIndex(string? value) => value switch
    {
        ApplicationSettings.ThemeLight => 1,
        ApplicationSettings.ThemeDark => 2,
        _ => 0
    };

    /// <summary>
    /// 把主题下拉框序号转换为存储值。
    /// </summary>
    /// <param name="index">下拉框序号，类型为 int，取值为零到二，必填。</param>
    /// <returns>主题存储值，类型为字符串。</returns>
    private static string GetThemeValue(int index) => index switch
    {
        1 => ApplicationSettings.ThemeLight,
        2 => ApplicationSettings.ThemeDark,
        _ => ApplicationSettings.ThemeFollowSystem
    };

    /// <summary>
    /// 把界面密度存储值转换为下拉框序号。
    /// </summary>
    /// <param name="value">密度存储值，类型为字符串，取值为 Comfortable 或 Compact，必填。</param>
    /// <returns>下拉框序号，类型为 int；未知值回退为零。</returns>
    private static int GetDensityIndex(string? value) => value == ApplicationSettings.DensityCompact ? 1 : 0;

    /// <summary>
    /// 把界面密度下拉框序号转换为存储值。
    /// </summary>
    /// <param name="index">下拉框序号，类型为 int，取值为零或一，必填。</param>
    /// <returns>密度存储值，类型为字符串。</returns>
    private static string GetDensityValue(int index) => index == 1 ? ApplicationSettings.DensityCompact : ApplicationSettings.DensityComfortable;

    /// <summary>
    /// 把通知级别存储值转换为下拉框序号。
    /// </summary>
    /// <param name="value">通知级别存储值，类型为字符串，取值为 ErrorsOnly、WarningsAndErrors 或 All，必填。</param>
    /// <returns>下拉框序号，类型为 int；未知值回退为零。</returns>
    private static int GetNotifyLevelIndex(string? value) => value switch
    {
        ApplicationSettings.NotifyWarningsAndErrors => 1,
        ApplicationSettings.NotifyAll => 2,
        _ => 0
    };

    /// <summary>
    /// 把通知级别下拉框序号转换为存储值。
    /// </summary>
    /// <param name="index">下拉框序号，类型为 int，取值为零到二，必填。</param>
    /// <returns>通知级别存储值，类型为字符串。</returns>
    private static string GetNotifyLevelValue(int index) => index switch
    {
        1 => ApplicationSettings.NotifyWarningsAndErrors,
        2 => ApplicationSettings.NotifyAll,
        _ => ApplicationSettings.NotifyErrorsOnly
    };
}
