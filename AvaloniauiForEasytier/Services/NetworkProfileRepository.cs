using FreeSql;
using System;
using System.Collections.Generic;
using System.Linq;

namespace AvaloniauiForEasytier.Services;

/// <summary>
/// 通过应用通用数据库读写 EasyTier 网络配置。
/// </summary>
public sealed class NetworkProfileRepository
{
    private readonly IFreeSql _database;

    /// <summary>
    /// 初始化网络配置仓储。
    /// </summary>
    /// <param name="database">应用数据库连接，类型为 IFreeSql，不可为空，必填。</param>
    public NetworkProfileRepository(IFreeSql database)
    {
        _database = database ?? throw new ArgumentNullException(nameof(database));
    }

    /// <summary>
    /// 按显示名称读取全部网络配置。
    /// </summary>
    /// <returns>网络配置只读列表，类型为 IReadOnlyList&lt;NetworkProfile&gt;。</returns>
    public IReadOnlyList<NetworkProfile> GetAll()
    {
        return _database.Select<NetworkProfile>()
            .OrderBy(profile => profile.ProfileName)
            .ToList();
    }

    /// <summary>
    /// 按主键读取一个网络配置。
    /// </summary>
    /// <param name="id">配置主键，类型为 long，取值为大于零的数据库主键，必填。</param>
    /// <returns>匹配的网络配置，类型为 NetworkProfile；不存在时返回 null。</returns>
    public NetworkProfile? GetById(long id)
    {
        return _database.Select<NetworkProfile>()
            .Where(profile => profile.Id == id)
            .First();
    }

    /// <summary>
    /// 新增或更新一个网络配置。
    /// </summary>
    /// <param name="profile">待保存配置，类型为 NetworkProfile，不可为空，必填。</param>
    /// <returns>保存后的配置，类型为 NetworkProfile，包含数据库主键和时间。</returns>
    public NetworkProfile Save(NetworkProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        var now = DateTime.Now;

        // 新配置写入创建时间并取得数据库生成的主键。
        if (profile.Id == 0)
        {
            profile.CreatedAt = now; // 配置创建时间。
            profile.UpdatedAt = now; // 配置最后修改时间。
            profile.Id = _database.Insert(profile).ExecuteIdentity(); // 配置数据库主键。
            return profile;
        }

        profile.UpdatedAt = now; // 配置最后修改时间。
        _database.Update<NetworkProfile>()
            .SetSource(profile)
            .ExecuteAffrows();
        return profile;
    }

    /// <summary>
    /// 判断另一个配置是否已经使用指定实例名称。
    /// </summary>
    /// <param name="instanceName">实例名称，类型为字符串，取值为非空名称，必填。</param>
    /// <param name="excludedId">需要排除的当前配置主键，类型为 long，取值为零或正数，必填。</param>
    /// <returns>是否存在重名配置，类型为 bool；存在时返回 true。</returns>
    public bool IsInstanceNameUsed(string instanceName, long excludedId)
    {
        return _database.Select<NetworkProfile>()
            .Where(profile => profile.InstanceName == instanceName && profile.Id != excludedId)
            .Any();
    }

    /// <summary>
    /// 删除指定主键的网络配置。
    /// </summary>
    /// <param name="id">配置主键，类型为 long，取值为大于零的数据库主键，必填。</param>
    /// <returns>受影响行数，类型为 int，删除成功通常为一。</returns>
    public int Delete(long id)
    {
        return _database.Delete<NetworkProfile>()
            .Where(profile => profile.Id == id)
            .ExecuteAffrows();
    }
}
