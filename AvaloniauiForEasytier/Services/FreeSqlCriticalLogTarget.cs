using System;
using System.Diagnostics;
using FreeSql;
using NLog;
using NLog.Targets;

namespace AvaloniauiForEasytier.Services;

/// <summary>
/// 将 NLog 的关键级别日志写入 FreeSql 管理的 SQLite 数据库。
/// </summary>
public sealed class FreeSqlCriticalLogTarget : Target
{
    private readonly IFreeSql _freeSql;

    /// <summary>
    /// 初始化 SQLite 关键日志目标。
    /// </summary>
    /// <param name="freeSql">FreeSql 单例，类型为 IFreeSql，必须已连接到 SQLite，必填。</param>
    public FreeSqlCriticalLogTarget(IFreeSql freeSql)
    {
        _freeSql = freeSql ?? throw new ArgumentNullException(nameof(freeSql));
    }

    /// <summary>
    /// 将一条关键日志写入数据库。
    /// </summary>
    /// <param name="logEvent">NLog 日志事件，类型为 LogEventInfo，包含时间、级别、来源和异常，必填。</param>
    protected override void Write(LogEventInfo logEvent)
    {
        try
        {
            var record = new CriticalLogRecord
            {
                OccurredAt = logEvent.TimeStamp, // 日志发生时间。
                Level = logEvent.Level.Name, // 日志级别。
                Logger = logEvent.LoggerName ?? "未知来源", // 日志来源。
                Message = logEvent.FormattedMessage, // 日志正文。
                Exception = logEvent.Exception?.ToString() // 异常详情。
            };

            _freeSql.Insert(record).ExecuteAffrows();
        }
        catch (Exception exception)
        {
            // 数据库写入失败不能再次调用 NLog，避免关键日志目标递归记录自身错误。
            Trace.WriteLine($"写入 SQLite 关键日志失败：{exception}");
        }
    }
}
