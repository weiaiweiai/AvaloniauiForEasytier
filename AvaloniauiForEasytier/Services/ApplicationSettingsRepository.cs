using FreeSql;
using System;

namespace AvaloniauiForEasytier.Services;

/// <summary>
/// 通过应用通用数据库读写全局唯一的应用设置行。
/// </summary>
public sealed class ApplicationSettingsRepository
{
    private const long SingletonId = 1; // 全局唯一设置行的主键值。
    private readonly IFreeSql _database;

    /// <summary>
    /// 初始化应用设置仓储。
    /// </summary>
    /// <param name="database">应用数据库连接，类型为 IFreeSql，不可为空，必填。</param>
    public ApplicationSettingsRepository(IFreeSql database)
    {
        _database = database ?? throw new ArgumentNullException(nameof(database));
    }

    /// <summary>
    /// 读取全局唯一的应用设置行；首次运行时写入默认设置并返回。
    /// </summary>
    /// <returns>应用设置，类型为 ApplicationSettings；调用方拿到的一定是已入库的数据。</returns>
    public ApplicationSettings GetOrCreate()
    {
        var settings = _database.Select<ApplicationSettings>()
            .Where(item => item.Id == SingletonId)
            .First();
        if (settings is not null)
        {
            return settings;
        }

        // 首次运行没有设置行时写入默认值，保证后续保存走更新路径。
        settings = new ApplicationSettings { UpdatedAt = DateTime.Now };
        return InsertIfMissing(settings);
    }

    /// <summary>
    /// 保存全局唯一的应用设置行；行不存在时自动补插。
    /// </summary>
    /// <param name="settings">待保存设置，类型为 ApplicationSettings，不可为空，必填。</param>
    /// <returns>保存后的设置，类型为 ApplicationSettings。</returns>
    public ApplicationSettings Save(ApplicationSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        settings.Id = SingletonId;
        settings.UpdatedAt = DateTime.Now;

        // 更新影响行数为零说明设置行缺失，转为插入默认行。
        var affected = _database.Update<ApplicationSettings>()
            .SetSource(settings)
            .ExecuteAffrows();
        if (affected == 0)
        {
            return InsertIfMissing(settings);
        }

        return settings;
    }

    /// <summary>
    /// 在并发或数据缺失场景下安全插入设置行。
    /// </summary>
    /// <param name="settings">待插入设置，类型为 ApplicationSettings，不可为空，必填。</param>
    /// <returns>数据库中生效的设置，类型为 ApplicationSettings。</returns>
    private ApplicationSettings InsertIfMissing(ApplicationSettings settings)
    {
        try
        {
            _database.Insert(settings).ExecuteAffrows();
            return settings;
        }
        catch (Exception)
        {
            // 主键冲突说明并发写入已经创建了设置行，读取既有行作为结果。
            var existing = _database.Select<ApplicationSettings>()
                .Where(item => item.Id == SingletonId)
                .First();
            return existing ?? settings;
        }
    }
}
