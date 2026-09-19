using FreeSql;
using System;
using System.Collections.Generic;

namespace AvaloniauiForEasytier.Services;

/// <summary>
/// 通过应用通用数据库读写入口节点服务器地址。
/// </summary>
public sealed class ServerEndpointRepository
{
    private readonly IFreeSql _database;

    /// <summary>
    /// 初始化服务器地址仓储。
    /// </summary>
    /// <param name="database">应用数据库连接，类型为 IFreeSql，不可为空，必填。</param>
    public ServerEndpointRepository(IFreeSql database)
    {
        _database = database ?? throw new ArgumentNullException(nameof(database));
    }

    /// <summary>
    /// 按显示名称读取全部服务器地址。
    /// </summary>
    /// <returns>服务器地址只读列表，类型为 IReadOnlyList&lt;ServerEndpoint&gt;。</returns>
    public IReadOnlyList<ServerEndpoint> GetAll()
    {
        return _database.Select<ServerEndpoint>()
            .OrderBy(endpoint => endpoint.Name)
            .ToList();
    }

    /// <summary>
    /// 按主键读取一个服务器地址。
    /// </summary>
    /// <param name="id">服务器主键，类型为 long，取值为大于零的数据库主键，必填。</param>
    /// <returns>匹配的服务器地址，类型为 ServerEndpoint；不存在时返回 null。</returns>
    public ServerEndpoint? GetById(long id)
    {
        return _database.Select<ServerEndpoint>()
            .Where(endpoint => endpoint.Id == id)
            .First();
    }

    /// <summary>
    /// 新增或更新一个服务器地址。
    /// </summary>
    /// <param name="endpoint">待保存服务器地址，类型为 ServerEndpoint，不可为空，必填。</param>
    /// <returns>保存后的服务器地址，类型为 ServerEndpoint，包含数据库主键和时间。</returns>
    public ServerEndpoint Save(ServerEndpoint endpoint)
    {
        ArgumentNullException.ThrowIfNull(endpoint);
        var now = DateTime.Now;

        // 新地址写入创建时间并取得数据库生成的主键。
        if (endpoint.Id == 0)
        {
            endpoint.CreatedAt = now; // 服务器记录创建时间。
            endpoint.UpdatedAt = now; // 服务器记录最后修改时间。
            endpoint.Id = _database.Insert(endpoint).ExecuteIdentity(); // 服务器数据库主键。
            return endpoint;
        }

        endpoint.UpdatedAt = now; // 服务器记录最后修改时间。
        _database.Update<ServerEndpoint>()
            .SetSource(endpoint)
            .ExecuteAffrows();
        return endpoint;
    }

    /// <summary>
    /// 删除指定主键的服务器地址。
    /// </summary>
    /// <param name="id">服务器主键，类型为 long，取值为大于零的数据库主键，必填。</param>
    /// <returns>受影响行数，类型为 int，删除成功通常为一。</returns>
    public int Delete(long id)
    {
        return _database.Delete<ServerEndpoint>()
            .Where(endpoint => endpoint.Id == id)
            .ExecuteAffrows();
    }
}
