using FreeSql.DataAnnotations;
using System;

namespace AvaloniauiForEasytier.Services;

/// <summary>
/// 表示应用全局设置；全库仅一行，主键固定为一。
/// </summary>
[Table(Name = "ApplicationSettings")]
public sealed class ApplicationSettings
{
    /// <summary>主题取值：跟随系统。</summary>
    public const string ThemeFollowSystem = "FollowSystem";

    /// <summary>主题取值：浅色。</summary>
    public const string ThemeLight = "Light";

    /// <summary>主题取值：深色。</summary>
    public const string ThemeDark = "Dark";

    /// <summary>界面密度取值：舒适。</summary>
    public const string DensityComfortable = "Comfortable";

    /// <summary>界面密度取值：紧凑。</summary>
    public const string DensityCompact = "Compact";

    /// <summary>通知级别取值：仅错误。</summary>
    public const string NotifyErrorsOnly = "ErrorsOnly";

    /// <summary>通知级别取值：警告及错误。</summary>
    public const string NotifyWarningsAndErrors = "WarningsAndErrors";

    /// <summary>通知级别取值：全部事件。</summary>
    public const string NotifyAll = "All";

    /// <summary>全局唯一设置行的主键；固定为一，不使用自增。</summary>
    [Column(IsPrimary = true)]
    public long Id { get; set; } = 1;

    /// <summary>登录系统后是否自动启动控制台。</summary>
    [Column(IsNullable = false)]
    public bool LaunchAtLogin { get; set; }

    /// <summary>关闭窗口时是否最小化到托盘并继续后台运行。</summary>
    [Column(IsNullable = false)]
    public bool MinimizeToTray { get; set; } = true;

    /// <summary>应用启动后是否自动检查更新；仅检查版本，不自动安装。</summary>
    [Column(IsNullable = false)]
    public bool CheckForUpdates { get; set; } = true;

    /// <summary>界面主题模式；取值为 FollowSystem、Light 或 Dark，默认跟随系统。</summary>
    [Column(DbType = "TEXT", IsNullable = false)]
    public string ThemeMode { get; set; } = ThemeFollowSystem;

    /// <summary>界面密度；取值为 Comfortable 或 Compact，默认舒适。</summary>
    [Column(DbType = "TEXT", IsNullable = false)]
    public string UiDensity { get; set; } = DensityComfortable;

    /// <summary>网络运行状态变化时是否弹出系统通知。</summary>
    [Column(IsNullable = false)]
    public bool StatusNotification { get; set; } = true;

    /// <summary>系统通知级别；取值为 ErrorsOnly、WarningsAndErrors 或 All，默认仅错误。</summary>
    [Column(DbType = "TEXT", IsNullable = false)]
    public string NotificationLevel { get; set; } = NotifyErrorsOnly;

    /// <summary>设置最后修改时间。</summary>
    [Column(IsNullable = false)]
    public DateTime UpdatedAt { get; set; }
}
