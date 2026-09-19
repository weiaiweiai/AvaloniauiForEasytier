using FreeSql.DataAnnotations;
using System;

namespace AvaloniauiForEasytier.Services;

/// <summary>
/// 表示一个可复用的 EasyTier 入口节点服务器地址。
/// </summary>
[Table(Name = "ServerEndpoints")]
public sealed class ServerEndpoint
{
    /// <summary>服务器记录的自增主键。</summary>
    [Column(IsPrimary = true, IsIdentity = true)]
    public long Id { get; set; }

    /// <summary>显示在桌面界面中的服务器名称。</summary>
    [Column(DbType = "TEXT", IsNullable = false)]
    public string Name { get; set; } = string.Empty;

    /// <summary>入口节点地址，例如 tcp://public.easytier.cn:11010。</summary>
    [Column(DbType = "TEXT", IsNullable = false)]
    public string Address { get; set; } = string.Empty;

    /// <summary>服务器记录创建时间。</summary>
    [Column(IsNullable = false)]
    public DateTime CreatedAt { get; set; }

    /// <summary>服务器记录最后修改时间。</summary>
    [Column(IsNullable = false)]
    public DateTime UpdatedAt { get; set; }
}
