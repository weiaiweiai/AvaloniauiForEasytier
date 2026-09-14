using System;
using System.IO;
using System.Text;
using FreeSql;
using NLog;
using NLog.Config;
using NLog.Targets;
using NLog.Targets.Wrappers;

namespace AvaloniauiForEasytier.Services;

/// <summary>
/// 管理应用级 NLog 和关键日志 SQLite 存储。
/// </summary>
public static class ApplicationLogging
{
    private static readonly object SyncRoot = new();
    private static IFreeSql? _freeSql;
    private static bool _initialized; // 标记日志模块是否已经完成初始化。
    private static bool _criticalDatabaseAvailable; // 标记关键日志数据库当前是否可用。
    private static string? _criticalDatabasePath;

    /// <summary>
    /// 获取关键日志 SQLite 当前是否可用。
    /// </summary>
    public static bool IsCriticalDatabaseAvailable
    {
        get
        {
            lock (SyncRoot)
            {
                return _criticalDatabaseAvailable;
            }
        }
    }

    /// <summary>
    /// 获取关键日志 SQLite 文件路径。
    /// </summary>
    public static string? CriticalDatabasePath
    {
        get
        {
            lock (SyncRoot)
            {
                return _criticalDatabasePath;
            }
        }
    }

    /// <summary>
    /// 初始化 NLog 文件、控制台和关键日志数据库目标。
    /// </summary>
    public static void Initialize()
    {
        lock (SyncRoot)
        {
            if (_initialized)
            {
                return;
            }

            var dataDirectory = GetApplicationDataDirectory();
            var logDirectory = Path.Combine(dataDirectory, "logs");
            Directory.CreateDirectory(logDirectory);

            var configuration = new LoggingConfiguration();
            var layout = "${longdate}|${level:uppercase=true}|${logger}|${message} ${exception:format=tostring}";
            var consoleTarget = new ConsoleTarget("console")
            {
                Layout = layout
            };
            var fileTarget = new FileTarget("file")
            {
                FileName = Path.Combine(logDirectory, "application-${shortdate}.log"),
                Layout = layout,
                Encoding = Encoding.UTF8,
                KeepFileOpen = false,
                ConcurrentWrites = true
            };

            configuration.AddTarget(consoleTarget);
            configuration.AddTarget(fileTarget);
            configuration.AddRule(LogLevel.Trace, LogLevel.Fatal, consoleTarget);
            configuration.AddRule(LogLevel.Trace, LogLevel.Fatal, fileTarget);

            Exception? databaseException = null;
            try
            {
                _criticalDatabasePath = Path.Combine(dataDirectory, "critical-logs.db");
                _freeSql = new FreeSqlBuilder()
                    .UseConnectionString(DataType.Sqlite, $"Data Source={_criticalDatabasePath}")
                    .UseAutoSyncStructure(false)
                    .Build();
                _freeSql.CodeFirst.SyncStructure<CriticalLogRecord>();

                var databaseTarget = new FreeSqlCriticalLogTarget(_freeSql)
                {
                    Name = "criticalDatabase"
                };
                var asyncDatabaseTarget = new AsyncTargetWrapper(databaseTarget)
                {
                    Name = "criticalDatabaseAsync"
                };
                configuration.AddTarget(databaseTarget);
                configuration.AddTarget(asyncDatabaseTarget);
                // 只有 Error 和 Fatal 级别进入 SQLite，普通运行日志只保留在文件和控制台。
                configuration.AddRule(LogLevel.Error, LogLevel.Fatal, asyncDatabaseTarget);
                _criticalDatabaseAvailable = true;
            }
            catch (Exception exception)
            {
                databaseException = exception;
                _criticalDatabaseAvailable = false;
                _freeSql?.Dispose();
                _freeSql = null;
            }

            LogManager.Configuration = configuration;
            _initialized = true;

            var logger = LogManager.GetCurrentClassLogger();
            logger.Info("日志模块已初始化，普通日志写入文件和控制台");
            if (databaseException is null)
            {
                logger.Info("关键日志将写入 SQLite：{0}", _criticalDatabasePath);
            }
            else
            {
                // SQLite 不可用时保留 NLog 文件和控制台能力，避免日志初始化阻断应用启动。
                logger.Warn(databaseException, "关键日志 SQLite 初始化失败，将仅写入文件和控制台");
            }
        }
    }

    /// <summary>
    /// 获取指定名称的 NLog 记录器。
    /// </summary>
    /// <param name="loggerName">记录器名称，类型为字符串，取值为非空类型或模块名称，必填。</param>
    /// <returns>NLog 记录器，类型为 Logger。</returns>
    public static Logger GetLogger(string loggerName)
    {
        if (string.IsNullOrWhiteSpace(loggerName))
        {
            throw new ArgumentException("记录器名称不能为空。", nameof(loggerName));
        }

        return LogManager.GetLogger(loggerName);
    }

    /// <summary>
    /// 关闭 NLog 目标并释放 FreeSql 连接。
    /// </summary>
    public static void Shutdown()
    {
        IFreeSql? freeSql;
        lock (SyncRoot)
        {
            if (!_initialized)
            {
                return;
            }

            _initialized = false;
            _criticalDatabaseAvailable = false;
            freeSql = _freeSql;
            _freeSql = null;
        }

        // 先等待异步 SQLite 目标刷新，再释放数据库连接。
        LogManager.Shutdown();
        freeSql?.Dispose();
    }

    /// <summary>
    /// 获取跨平台的应用数据目录。
    /// </summary>
    /// <returns>应用数据目录，类型为字符串，返回绝对路径。</returns>
    private static string GetApplicationDataDirectory()
    {
        var localApplicationData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return string.IsNullOrWhiteSpace(localApplicationData)
            ? Path.Combine(AppContext.BaseDirectory, "data")
            : Path.Combine(localApplicationData, "AvaloniauiForEasytier");
    }
}
