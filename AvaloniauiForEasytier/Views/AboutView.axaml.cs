using Avalonia.Controls;
using AvaloniauiForEasytier.Services;
using System;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;

namespace AvaloniauiForEasytier.Views;

/// <summary>
/// 展示产品信息以及从程序集和运行环境读取的版本详情。
/// </summary>
public partial class AboutView : UserControl
{
    private readonly NetworkRuntimeManager? _runtimeManager;

    /// <summary>初始化设计器使用的关于视图。</summary>
    public AboutView()
    {
        InitializeComponent();
        RenderEnvironment();
    }

    /// <summary>
    /// 初始化关于视图并接入原生库状态检测。
    /// </summary>
    /// <param name="runtimeManager">多实例运行时管理器，类型为 NetworkRuntimeManager，不可为空，必填。</param>
    public AboutView(NetworkRuntimeManager runtimeManager)
    {
        _runtimeManager = runtimeManager ?? throw new ArgumentNullException(nameof(runtimeManager));
        InitializeComponent();
        RenderEnvironment();
    }

    /// <summary>从程序集和运行环境读取版本信息并填充界面。</summary>
    private void RenderEnvironment()
    {
        var appVersion = GetAssemblyVersion(typeof(AboutView).Assembly);
        ProductVersionText.Text = $"版本 {appVersion}";
        AppVersionValueText.Text = appVersion;
        FrameworkValueText.Text = RuntimeInformation.FrameworkDescription;
        UiFrameworkValueText.Text = $"Avalonia UI {GetAssemblyVersion(typeof(Avalonia.Application).Assembly)}";
        ThemeValueText.Text = $"SukiUI {GetAssemblyVersion(typeof(SukiUI.SukiTheme).Assembly)}";
        OperatingSystemValueText.Text = RuntimeInformation.OSDescription;
        ArchitectureValueText.Text = RuntimeInformation.ProcessArchitecture.ToString();
        RenderNativeLibraryState();
    }

    /// <summary>检测 EasyTier 原生库是否存在并显示文件名与大小。</summary>
    private void RenderNativeLibraryState()
    {
        // 设计器构造函数没有运行时管理器，此时不展示原生库信息。
        if (_runtimeManager is null)
        {
            NativeLibraryStatusText.Text = "未检测";
            NativeLibraryPathText.IsVisible = false;
            return;
        }

        var path = _runtimeManager.NativeLibraryPath;
        NativeLibraryPathText.Text = Path.GetFileName(path);
        NativeLibraryPathText.IsVisible = true;

        // 原生库缺失时所有网络都无法启动，这里直接给出结论而不是只显示路径。
        if (!File.Exists(path))
        {
            NativeLibraryStatusText.Text = "未找到，网络无法启动";
            return;
        }

        var sizeInMegabytes = new FileInfo(path).Length / 1024d / 1024d;
        NativeLibraryStatusText.Text = $"已就绪（{sizeInMegabytes:0.0} MB）";
    }

    /// <summary>
    /// 读取程序集的版本号。
    /// </summary>
    /// <param name="assembly">目标程序集，类型为 Assembly，不可为空，必填。</param>
    /// <returns>版本号文本，类型为字符串；读取失败时返回占位符。</returns>
    private static string GetAssemblyVersion(Assembly assembly)
    {
        var version = assembly.GetName().Version;
        return version is null ? "--" : $"{version.Major}.{version.Minor}.{version.Build}";
    }
}
