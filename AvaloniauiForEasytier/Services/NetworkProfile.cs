using FreeSql.DataAnnotations;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace AvaloniauiForEasytier.Services;

/// <summary>
/// 表示一个可以独立启动和停止的 EasyTier 网络配置。
/// </summary>
[Table(Name = "NetworkProfiles")]
public sealed class NetworkProfile
{
    /// <summary>配置记录的自增主键。</summary>
    [Column(IsPrimary = true, IsIdentity = true)]
    public long Id { get; set; }

    /// <summary>显示在桌面界面中的配置名称。</summary>
    [Column(DbType = "TEXT", IsNullable = false)]
    public string ProfileName { get; set; } = string.Empty;

    /// <summary>EasyTier FFI 使用的唯一实例名称。</summary>
    [Column(DbType = "TEXT", IsNullable = false)]
    public string InstanceName { get; set; } = string.Empty;

    /// <summary>需要加入的 EasyTier 虚拟网络名称。</summary>
    [Column(DbType = "TEXT", IsNullable = false)]
    public string NetworkName { get; set; } = string.Empty;

    /// <summary>虚拟网络认证密钥；未设置时为空。</summary>
    [Column(DbType = "TEXT", IsNullable = true)]
    public string? NetworkSecret { get; set; }

    /// <summary>本机虚拟 IPv4 地址和网段；未设置时为空。</summary>
    [Column(DbType = "TEXT", IsNullable = true)]
    public string? Ipv4 { get; set; }

    /// <summary>本机在虚拟网络中的节点名称；未设置时为空。</summary>
    [Column(DbType = "TEXT", IsNullable = true)]
    public string? Hostname { get; set; }

    /// <summary>按行保存的信令服务器地址（EasyTier 入口节点）；未设置时为空。</summary>
    [Column(DbType = "TEXT", IsNullable = true)]
    public string? PeerUris { get; set; }

    /// <summary>应用启动后是否自动运行该网络。</summary>
    [Column(IsNullable = false)]
    public bool AutoStart { get; set; }

    /// <summary>配置创建时间。</summary>
    [Column(IsNullable = false)]
    public DateTime CreatedAt { get; set; }

    /// <summary>配置最后修改时间。</summary>
    [Column(IsNullable = false)]
    public DateTime UpdatedAt { get; set; }

    /// <summary>
    /// 生成 EasyTier FFI 可以解析的 TOML 配置。
    /// </summary>
    /// <returns>完整 TOML 配置，类型为字符串，取值为当前配置对应的 EasyTier 参数。</returns>
    public string BuildTomlConfiguration()
    {
        var builder = new StringBuilder();

        // EasyTier 配置结构的键名为 instance_name；缺省时 FFI 会回退为 default，导致多网络实例冲突。
        builder.AppendLine($"instance_name = {QuoteToml(InstanceName)}");

        // 只有用户指定虚拟地址时才关闭 EasyTier 的默认地址选择逻辑。
        if (!string.IsNullOrWhiteSpace(Ipv4))
        {
            builder.AppendLine($"ipv4 = {QuoteToml(Ipv4.Trim())}");
        }

        // 节点名称用于在同一网络的节点列表中辨识本机。
        if (!string.IsNullOrWhiteSpace(Hostname))
        {
            builder.AppendLine($"hostname = {QuoteToml(Hostname.Trim())}");
        }

        // 未配置监听地址时不主动暴露端口，网络仍可通过入口节点建立连接。
        builder.AppendLine("listeners = []");

        // 网络标识使用内联表并保持在根表位置，避免 TOML 表头改变后续键的归属。
        var identityParts = new List<string> { $"network_name = {QuoteToml(NetworkName)}" };

        // 只有用户填写认证密钥时才写入配置，保留 EasyTier 的无密钥连接行为。
        if (!string.IsNullOrWhiteSpace(NetworkSecret))
        {
            identityParts.Add($"network_secret = {QuoteToml(NetworkSecret.Trim())}");
        }

        builder.AppendLine($"network_identity = {{ {string.Join(", ", identityParts)} }}");

        var peers = (PeerUris ?? string.Empty)
            .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(peer => !string.IsNullOrWhiteSpace(peer))
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        // 入口节点键名为 peer，每项是含 uri 字段的内联表。
        if (peers.Length > 0)
        {
            var peerEntries = string.Join(", ", peers.Select(peer => $"{{ uri = {QuoteToml(peer)} }}"));
            builder.AppendLine($"peer = [{peerEntries}]");
        }

        return builder.ToString();
    }

    /// <summary>
    /// 对 TOML 基本字符串进行转义并添加双引号。
    /// </summary>
    /// <param name="value">待转义文本，类型为字符串，取值为任意文本，必填。</param>
    /// <returns>带 TOML 双引号的文本，类型为字符串。</returns>
    private static string QuoteToml(string value)
    {
        return $"\"{value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal)}\"";
    }
}
