using Avalonia.Controls;
using System;
using System.Text;

namespace AvaloniauiForEasytier.Views;

public partial class NetworkView : UserControl
{
    /// <summary>
    /// 初始化网络配置视图。
    /// </summary>
    public NetworkView()
    {
        InitializeComponent();
    }

    /// <summary>
    /// 获取当前配置使用的实例名称。
    /// </summary>
    /// <returns>实例名称，类型为字符串；为空时返回桌面端默认名称。</returns>
    public string GetInstanceName()
    {
        return string.IsNullOrWhiteSpace(InstanceNameTextBox.Text)
            ? "easytier-desktop"
            : InstanceNameTextBox.Text.Trim();
    }

    /// <summary>
    /// 将界面配置转换为 EasyTier FFI 所需的 TOML 文本。
    /// </summary>
    /// <returns>完整 TOML 配置，类型为字符串，取值为可供 EasyTier 解析的配置文本。</returns>
    public string GetConfiguration()
    {
        var networkName = string.IsNullOrWhiteSpace(NetworkNameTextBox.Text)
            ? "easytier"
            : NetworkNameTextBox.Text.Trim();
        var builder = new StringBuilder();
        builder.AppendLine($"inst_name = {QuoteToml(GetInstanceName())}");
        builder.AppendLine($"network_name = {QuoteToml(networkName)}");

        if (!string.IsNullOrWhiteSpace(NetworkSecretTextBox.Text))
        {
            builder.AppendLine($"network_secret = {QuoteToml(NetworkSecretTextBox.Text.Trim())}");
        }

        if (!string.IsNullOrWhiteSpace(Ipv4TextBox.Text))
        {
            builder.AppendLine($"ipv4 = {QuoteToml(Ipv4TextBox.Text.Trim())}");
        }

        if (!string.IsNullOrWhiteSpace(PeerTextBox.Text))
        {
            builder.AppendLine($"peers = [{QuoteToml(PeerTextBox.Text.Trim())}]");
        }

        // 未配置入口节点时不监听外部地址，避免首次启动产生意外网络暴露。
        builder.AppendLine("listeners = []");
        return builder.ToString();
    }

    /// <summary>
    /// 对 TOML 基本字符串进行转义和加引号。
    /// </summary>
    /// <param name="value">待转义文本，类型为字符串，取值为任意文本，必填。</param>
    /// <returns>带 TOML 双引号的文本，类型为字符串。</returns>
    private static string QuoteToml(string value)
    {
        return $"\"{value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal)}\"";
    }
}
