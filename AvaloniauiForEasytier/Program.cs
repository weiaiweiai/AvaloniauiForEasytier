using Avalonia;
using System;
using AvaloniauiForEasytier.Services;

namespace AvaloniauiForEasytier;

class Program
{
    /// <summary>
    /// 初始化日志模块并启动 Avalonia 桌面生命周期。
    /// </summary>
    /// <param name="args">进程启动参数，类型为字符串数组，取值为命令行参数集合，可为空，非必填。</param>
    [STAThread]
    public static void Main(string[] args)
    {
        ApplicationLogging.Initialize();
        try
        {
            BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        }
        finally
        {
            ApplicationLogging.Shutdown();
        }
    }

    /// <summary>
    /// 创建 Avalonia 应用构建器，供桌面启动和可视化设计器使用。
    /// </summary>
    /// <returns>应用构建器，类型为 AppBuilder，包含平台、字体和诊断配置。</returns>
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
#if DEBUG
            .WithDeveloperTools()
#endif
            .WithInterFont()
            .LogToTrace();
}
