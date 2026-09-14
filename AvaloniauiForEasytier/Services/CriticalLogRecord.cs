using System;
using FreeSql.DataAnnotations;

namespace AvaloniauiForEasytier.Services;

/// <summary>
/// 表示需要持久化到本地 SQLite 的关键日志记录。
/// </summary>
[Table(Name = "CriticalLogs")]
public sealed class CriticalLogRecord
{
    /// <summary>日志记录的自增主键。</summary>
    [Column(IsPrimary = true, IsIdentity = true)]
    public long Id { get; set; }

    /// <summary>日志发生时间。</summary>
    [Column(IsNullable = false)]
    public DateTime OccurredAt { get; set; }

    /// <summary>日志级别，例如 Error 或 Fatal。</summary>
    [Column(DbType = "TEXT", IsNullable = false)]
    public string Level { get; set; } = string.Empty;

    /// <summary>日志来源记录器名称。</summary>
    [Column(DbType = "TEXT", IsNullable = false)]
    public string Logger { get; set; } = string.Empty;

    /// <summary>日志正文。</summary>
    [Column(DbType = "TEXT", IsNullable = false)]
    public string Message { get; set; } = string.Empty;

    /// <summary>异常详情，没有异常时为空。</summary>
    [Column(DbType = "TEXT", IsNullable = true)]
    public string? Exception { get; set; }
}
