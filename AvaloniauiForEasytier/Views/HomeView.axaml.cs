using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using AvaloniauiForEasytier.Services;
using System;
using System.Threading.Tasks;

namespace AvaloniauiForEasytier.Views;

public partial class HomeView : UserControl
{
    private readonly IEasyTierRuntime _runtime;

    /// <summary>
    /// 初始化设计器使用的主页视图。
    /// </summary>
    public HomeView()
        : this(new CoreProcessManager())
    {
    }

    /// <summary>
    /// 初始化主页视图和 Core 状态订阅。
    /// </summary>
    /// <param name="runtime">EasyTier 运行时，类型为 IEasyTierRuntime，不可为空，必填。</param>
    public HomeView(IEasyTierRuntime runtime)
    {
        _runtime = runtime;
        InitializeComponent();
        ServiceActionButton.Click += ServiceActionButton_Click;
        _runtime.StatusChanged += CoreProcessManager_StatusChanged;
        ApplyStatus(new CoreStatusChangedEventArgs(
            _runtime.Status,
            _runtime.ExecutablePath,
            _runtime.LastMessage));
    }

    /// <summary>
    /// 响应 Core 状态变化并切换到 Avalonia UI 线程更新界面。
    /// </summary>
    /// <param name="sender">触发事件的 Core 管理器，类型为对象，可为空，非必填。</param>
    /// <param name="e">状态变化参数，类型为 CoreStatusChangedEventArgs，不可为空，必填。</param>
    private void CoreProcessManager_StatusChanged(object? sender, CoreStatusChangedEventArgs e)
    {
        Dispatcher.UIThread.Post(() => ApplyStatus(e));
    }

    /// <summary>
    /// 处理主页上的 Core 启动或停止按钮。
    /// </summary>
    /// <param name="sender">触发事件的按钮，类型为对象，可为空，非必填。</param>
    /// <param name="e">路由事件参数，类型为 RoutedEventArgs，不可为空，必填。</param>
    private async void ServiceActionButton_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        // 启动或停止过程中不允许重复提交操作。
        if (_runtime.Status is CoreProcessStatus.Starting or CoreProcessStatus.Stopping)
        {
            return;
        }

        ServiceActionButton.IsEnabled = false;
        bool operationSucceeded; // 标记本次启动或停止操作是否成功。
        try
        {
            if (_runtime.IsRunning)
            {
                await _runtime.StopAsync();
                operationSucceeded = true;
            }
            else
            {
                operationSucceeded = await _runtime.StartAsync();
            }
        }
        catch (Exception exception)
        {
            operationSucceeded = false;
            ApplyStatus(new CoreStatusChangedEventArgs(
                CoreProcessStatus.Failed,
                _runtime.ExecutablePath,
                $"操作失败：{exception.Message}"));
        }
        finally
        {
            ServiceActionButton.IsEnabled = true;
        }

        if (!operationSucceeded)
        {
            ApplyStatus(new CoreStatusChangedEventArgs(
                _runtime.Status,
                _runtime.ExecutablePath,
                _runtime.LastMessage));
        }
    }

    /// <summary>
    /// 根据 Core 状态刷新主页上的状态文本和操作按钮。
    /// </summary>
    /// <param name="eventArgs">状态变化参数，类型为 CoreStatusChangedEventArgs，不可为空，必填。</param>
    private void ApplyStatus(CoreStatusChangedEventArgs eventArgs)
    {
        switch (eventArgs.Status)
        {
            case CoreProcessStatus.Starting:
                SetStatusVisual("启动中", "正在启动 EasyTier Core", "正在启动", "请稍候，正在创建 Core 进程", "#D97706", "停止");
                break;
            case CoreProcessStatus.Running:
                SetStatusVisual("运行中", "EasyTier Core 正在运行", "运行中", "网络服务已启动，可以查看运行日志", "#12B76A", "停止服务");
                break;
            case CoreProcessStatus.Stopping:
                SetStatusVisual("停止中", "正在关闭 EasyTier Core", "停止中", "正在等待 Core 进程退出", "#D97706", "停止");
                break;
            case CoreProcessStatus.Failed:
                var errorMessage = string.IsNullOrWhiteSpace(eventArgs.Message)
                    ? "EasyTier Core 启动失败"
                    : eventArgs.Message;
                SetStatusVisual("启动失败", errorMessage, "启动失败", errorMessage, "#D92D20", "启动服务");
                break;
            default:
                SetStatusVisual("未运行", "EasyTier Core 等待启动", "未运行", "请选择配置后启动网络服务", "#D97706", "启动服务");
                break;
        }
    }

    /// <summary>
    /// 设置主页服务控制区域的统一视觉状态。
    /// </summary>
    /// <param name="summary">概览状态文本，类型为字符串，取值为非空短文本，必填。</param>
    /// <param name="summaryDescription">概览说明文本，类型为字符串，取值为非空文本，必填。</param>
    /// <param name="controlStatus">控制区域状态文本，类型为字符串，取值为非空短文本，必填。</param>
    /// <param name="controlDescription">控制区域主说明，类型为字符串，取值为非空文本，必填。</param>
    /// <param name="indicatorColor">状态指示灯颜色，类型为十六进制颜色字符串，取值为有效颜色值，必填。</param>
    /// <param name="actionText">操作按钮文本，类型为字符串，取值为非空命令文本，必填。</param>
    private void SetStatusVisual(
        string summary,
        string summaryDescription,
        string controlStatus,
        string controlDescription,
        string indicatorColor,
        string actionText)
    {
        ServiceStatusText.Text = summary;
        ServiceStatusDescriptionText.Text = summaryDescription;
        ServiceControlStatusText.Text = controlStatus;
        ServiceControlDescriptionText.Text = $"EasyTier Core {controlStatus}";
        ServiceControlHintText.Text = controlDescription;
        ServiceActionButton.Content = actionText;
        ServiceStatusIndicator.Fill = new SolidColorBrush(Color.Parse(indicatorColor));
    }
}
