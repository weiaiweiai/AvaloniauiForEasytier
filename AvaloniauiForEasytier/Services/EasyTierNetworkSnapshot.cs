using System;
using System.Collections.Generic;
using System.Text.Json;

namespace AvaloniauiForEasytier.Services;

/// <summary>
/// 表示一次 FFI 网络运行信息采集得到的节点和路由快照。
/// </summary>
public sealed class EasyTierNetworkSnapshot
{
    /// <summary>快照对应的实例名称。</summary>
    public string InstanceName { get; set; } = string.Empty;

    /// <summary>实例当前是否处于运行状态。</summary>
    public bool IsRunning { get; set; }

    /// <summary>实例最近一次错误说明；没有错误时为空。</summary>
    public string? ErrorMessage { get; set; }

    /// <summary>虚拟网卡名称；未上报时为空。</summary>
    public string? DeviceName { get; set; }

    /// <summary>本机节点在虚拟网络中的节点编号。</summary>
    public uint MyPeerId { get; set; }

    /// <summary>本机虚拟 IPv4 地址和网段；未分配时为空。</summary>
    public string? VirtualIpv4 { get; set; }

    /// <summary>本机节点名称；未上报时为空。</summary>
    public string? Hostname { get; set; }

    /// <summary>本机 EasyTier 核心版本；未上报时为空。</summary>
    public string? Version { get; set; }

    /// <summary>虚拟网络中的节点列表；来自节点与路由配对数据。</summary>
    public List<EasyTierNodeInfo> Nodes { get; } = new();

    /// <summary>虚拟网络中的路由列表。</summary>
    public List<EasyTierRouteInfo> Routes { get; } = new();
}

/// <summary>
/// 表示虚拟网络中一个节点的展示信息。
/// </summary>
public sealed class EasyTierNodeInfo
{
    /// <summary>节点在虚拟网络中的编号。</summary>
    public uint PeerId { get; set; }

    /// <summary>节点主机名称；未上报时为空。</summary>
    public string? Hostname { get; set; }

    /// <summary>节点虚拟 IPv4 地址和网段；未分配时为空。</summary>
    public string? VirtualIpv4 { get; set; }

    /// <summary>到该节点的路径延迟（毫秒）；未上报时为零。</summary>
    public int PathLatencyMs { get; set; }

    /// <summary>连接丢包率，取值零到一；未上报时为空。</summary>
    public double? LossRate { get; set; }

    /// <summary>连接使用的隧道类型，例如 tcp、udp；未连接时为空。</summary>
    public string? TunnelType { get; set; }

    /// <summary>连接累计接收字节数。</summary>
    public ulong RxBytes { get; set; }

    /// <summary>连接累计发送字节数。</summary>
    public ulong TxBytes { get; set; }

    /// <summary>节点 EasyTier 版本；未上报时为空。</summary>
    public string? Version { get; set; }
}

/// <summary>
/// 表示虚拟网络中一条路由的展示信息。
/// </summary>
public sealed class EasyTierRouteInfo
{
    /// <summary>目标节点编号。</summary>
    public uint PeerId { get; set; }

    /// <summary>目标节点主机名称；未上报时为空。</summary>
    public string? Hostname { get; set; }

    /// <summary>目标节点虚拟 IPv4 地址和网段；未分配时为空。</summary>
    public string? VirtualIpv4 { get; set; }

    /// <summary>下一跳节点编号；等于目标节点时为直连。</summary>
    public uint NextHopPeerId { get; set; }

    /// <summary>路由开销，数值越大距离越远。</summary>
    public int Cost { get; set; }

    /// <summary>路径延迟（毫秒）；未上报时为零。</summary>
    public int PathLatencyMs { get; set; }

    /// <summary>目标节点 EasyTier 版本；未上报时为空。</summary>
    public string? Version { get; set; }
}

/// <summary>
/// 把 FFI 返回的网络运行信息 JSON 解析为界面可用的快照模型。
/// </summary>
public static class EasyTierNetworkSnapshotParser
{
    private static readonly NLog.Logger Logger = ApplicationLogging.GetLogger(nameof(EasyTierNetworkSnapshotParser));

    /// <summary>
    /// 解析一个实例的网络运行信息 JSON。
    /// </summary>
    /// <param name="instanceName">实例名称，类型为字符串，取值为非空 FFI 实例名，必填。</param>
    /// <param name="json">运行信息 JSON，类型为字符串，取值为 FFI 序列化的完整文本，必填。</param>
    /// <returns>网络运行快照，类型为 EasyTierNetworkSnapshot；解析失败时返回 null。</returns>
    public static EasyTierNetworkSnapshot? Parse(string instanceName, string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            return ParseRoot(instanceName, document.RootElement);
        }
        catch (Exception exception)
        {
            Logger.Warn(exception, "解析 EasyTier 网络快照 JSON 失败：{0}", instanceName);
            return null;
        }
    }

    /// <summary>
    /// 从 JSON 根元素解析快照内容。
    /// </summary>
    /// <param name="instanceName">实例名称，类型为字符串，取值为非空 FFI 实例名，必填。</param>
    /// <param name="root">JSON 根元素，类型为 JsonElement，取值为 NetworkInstanceRunningInfo 对象，必填。</param>
    /// <returns>网络运行快照，类型为 EasyTierNetworkSnapshot。</returns>
    private static EasyTierNetworkSnapshot ParseRoot(string instanceName, JsonElement root)
    {
        var snapshot = new EasyTierNetworkSnapshot { InstanceName = instanceName };

        // pbjson 在字段为默认值时会省略键名，缺失的 running 视为运行中，缺失的错误文本视为无错误。
        if (TryGetProperty(root, "running", out var runningElement) && runningElement.ValueKind == JsonValueKind.False)
        {
            snapshot.IsRunning = false;
        }
        else
        {
            snapshot.IsRunning = true;
        }

        if (TryGetProperty(root, "error_msg", out var errorElement) && errorElement.ValueKind == JsonValueKind.String)
        {
            snapshot.ErrorMessage = errorElement.GetString();
        }

        if (TryGetProperty(root, "dev_name", out var deviceElement) && deviceElement.ValueKind == JsonValueKind.String)
        {
            snapshot.DeviceName = deviceElement.GetString();
        }

        if (TryGetProperty(root, "my_node_info", out var myNodeElement) && myNodeElement.ValueKind == JsonValueKind.Object)
        {
            if (TryGetProperty(myNodeElement, "peer_id", out var myPeerElement))
            {
                snapshot.MyPeerId = ReadUInt32(myPeerElement);
            }

            if (TryGetProperty(myNodeElement, "virtual_ipv4", out var myIpElement))
            {
                snapshot.VirtualIpv4 = ReadInetText(myIpElement);
            }

            snapshot.Hostname = TryReadString(myNodeElement, "hostname");
            snapshot.Version = TryReadString(myNodeElement, "version");
        }

        // 节点列表来自路由与连接信息的配对结果，已按公共服务器优先排序。
        if (TryGetProperty(root, "peer_route_pairs", out var pairsElement) && pairsElement.ValueKind == JsonValueKind.Array)
        {
            foreach (var pairElement in pairsElement.EnumerateArray())
            {
                // 缺少路由部分的配对无法确定虚拟地址，跳过该条目。
                if (!TryGetProperty(pairElement, "route", out var routeElement) || routeElement.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                var node = new EasyTierNodeInfo();
                if (TryGetProperty(routeElement, "peer_id", out var peerIdElement))
                {
                    node.PeerId = ReadUInt32(peerIdElement);
                }

                node.Hostname = TryReadString(routeElement, "hostname");
                node.Version = TryReadString(routeElement, "version");

                if (TryGetProperty(routeElement, "ipv4_addr", out var ipv4Element))
                {
                    node.VirtualIpv4 = ReadInetText(ipv4Element);
                }

                if (TryGetProperty(routeElement, "path_latency", out var latencyElement))
                {
                    node.PathLatencyMs = ReadInt32(latencyElement);
                }

                // 连接明细取第一个未关闭连接，没有连接时流量和隧道字段保持为空。
                if (TryGetProperty(pairElement, "peer", out var peerElement) && peerElement.ValueKind == JsonValueKind.Object)
                {
                    var connection = SelectActiveConnection(peerElement);
                    if (connection is not null)
                    {
                        if (TryGetProperty(connection.Value, "loss_rate", out var lossElement) && lossElement.ValueKind == JsonValueKind.Number)
                        {
                            node.LossRate = lossElement.GetDouble();
                        }

                        if (TryGetProperty(connection.Value, "tunnel", out var tunnelElement) && tunnelElement.ValueKind == JsonValueKind.Object)
                        {
                            node.TunnelType = TryReadString(tunnelElement, "tunnel_type");
                        }

                        if (TryGetProperty(connection.Value, "stats", out var statsElement) && statsElement.ValueKind == JsonValueKind.Object)
                        {
                            if (TryGetProperty(statsElement, "rx_bytes", out var rxElement))
                            {
                                node.RxBytes = ReadUInt64(rxElement);
                            }

                            if (TryGetProperty(statsElement, "tx_bytes", out var txElement))
                            {
                                node.TxBytes = ReadUInt64(txElement);
                            }
                        }
                    }
                }

                snapshot.Nodes.Add(node);
            }
        }

        // 路由列表直接来自运行信息的路由数组。
        if (TryGetProperty(root, "routes", out var routesElement) && routesElement.ValueKind == JsonValueKind.Array)
        {
            foreach (var routeElement in routesElement.EnumerateArray())
            {
                var route = new EasyTierRouteInfo();
                if (TryGetProperty(routeElement, "peer_id", out var routePeerElement))
                {
                    route.PeerId = ReadUInt32(routePeerElement);
                }

                route.Hostname = TryReadString(routeElement, "hostname");
                route.Version = TryReadString(routeElement, "version");

                if (TryGetProperty(routeElement, "ipv4_addr", out var routeIpElement))
                {
                    route.VirtualIpv4 = ReadInetText(routeIpElement);
                }

                if (TryGetProperty(routeElement, "next_hop_peer_id", out var nextHopElement))
                {
                    route.NextHopPeerId = ReadUInt32(nextHopElement);
                }

                if (TryGetProperty(routeElement, "cost", out var costElement))
                {
                    route.Cost = ReadInt32(costElement);
                }

                if (TryGetProperty(routeElement, "path_latency", out var routeLatencyElement))
                {
                    route.PathLatencyMs = ReadInt32(routeLatencyElement);
                }

                snapshot.Routes.Add(route);
            }
        }

        return snapshot;
    }

    /// <summary>
    /// 按名称读取对象属性；EasyTier 的 pbjson 配置了 preserve_proto_field_names，输出 snake_case。
    /// </summary>
    /// <param name="element">JSON 对象元素，类型为 JsonElement，取值为任意对象，必填。</param>
    /// <param name="propertyName">snake_case 属性名，类型为字符串，取值为非空名称，必填。</param>
    /// <param name="value">命中的属性元素，类型为 JsonElement，未命中时为默认值。</param>
    /// <returns>是否命中属性，类型为 bool；命中时返回 true。</returns>
    private static bool TryGetProperty(JsonElement element, string propertyName, out JsonElement value)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            value = default;
            return false;
        }

        if (element.TryGetProperty(propertyName, out value))
        {
            return true;
        }

        // 去掉下划线后忽略大小写比较，兼容序列化器切换命名风格。
        foreach (var property in element.EnumerateObject())
        {
            if (string.Equals(property.Name.Replace("_", string.Empty, StringComparison.Ordinal), propertyName.Replace("_", string.Empty, StringComparison.Ordinal), StringComparison.OrdinalIgnoreCase))
            {
                value = property.Value;
                return true;
            }
        }

        value = default;
        return false;
    }

    /// <summary>
    /// 读取一个字符串属性；属性缺失或类型不符时返回空。
    /// </summary>
    /// <param name="element">JSON 对象元素，类型为 JsonElement，取值为任意对象，必填。</param>
    /// <param name="propertyName">snake_case 属性名，类型为字符串，取值为非空名称，必填。</param>
    /// <returns>属性文本，类型为字符串可空值。</returns>
    private static string? TryReadString(JsonElement element, string propertyName)
    {
        return TryGetProperty(element, propertyName, out var propertyElement) && propertyElement.ValueKind == JsonValueKind.String
            ? propertyElement.GetString()
            : null;
    }

    /// <summary>
    /// 读取一个无符号 32 位整数属性；兼容数字与字符串两种编码。
    /// </summary>
    /// <param name="element">JSON 数值元素，类型为 JsonElement，取值为整数或整数字符串，必填。</param>
    /// <returns>无符号整数，类型为 uint；解析失败时返回零。</returns>
    private static uint ReadUInt32(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Number)
        {
            return element.TryGetUInt32(out var value) ? value : (uint)element.GetInt64();
        }

        return element.ValueKind == JsonValueKind.String && uint.TryParse(element.GetString(), out var parsed) ? parsed : 0u;
    }

    /// <summary>
    /// 读取一个 32 位整数属性；兼容数字与字符串两种编码。
    /// </summary>
    /// <param name="element">JSON 数值元素，类型为 JsonElement，取值为整数或整数字符串，必填。</param>
    /// <returns>整数，类型为 int；解析失败时返回零。</returns>
    private static int ReadInt32(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Number)
        {
            return element.TryGetInt32(out var value) ? value : (int)element.GetInt64();
        }

        return element.ValueKind == JsonValueKind.String && int.TryParse(element.GetString(), out var parsed) ? parsed : 0;
    }

    /// <summary>
    /// 读取一个无符号 64 位整数属性；protobuf JSON 会把 64 位整数编码为字符串。
    /// </summary>
    /// <param name="element">JSON 数值元素，类型为 JsonElement，取值为整数或整数字符串，必填。</param>
    /// <returns>无符号长整数，类型为 ulong；解析失败时返回零。</returns>
    private static ulong ReadUInt64(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Number)
        {
            return element.TryGetUInt64(out var value) ? value : (ulong)element.GetInt64();
        }

        return element.ValueKind == JsonValueKind.String && ulong.TryParse(element.GetString(), out var parsed) ? parsed : 0ul;
    }

    /// <summary>
    /// 把 Ipv4Inet 对象转换为“地址/前缀长度”文本。
    /// </summary>
    /// <param name="element">JSON 对象元素，类型为 JsonElement，取值为 Ipv4Inet 结构，必填。</param>
    /// <returns>地址文本，类型为字符串可空值；结构不完整时返回 null。</returns>
    private static string? ReadInetText(JsonElement element)
    {
        // 地址与前缀任一缺失时按未分配处理。
        if (!TryGetProperty(element, "address", out var addressElement)
            || addressElement.ValueKind != JsonValueKind.Object
            || !TryGetProperty(addressElement, "addr", out var packedElement)
            || !TryGetProperty(element, "network_length", out var prefixElement))
        {
            return null;
        }

        var packed = ReadUInt32(packedElement);
        var prefix = ReadUInt32(prefixElement);
        return $"{(packed >> 24) & 0xFF}.{(packed >> 16) & 0xFF}.{(packed >> 8) & 0xFF}.{packed & 0xFF}/{prefix}";
    }

    /// <summary>
    /// 从节点信息中选择第一个未关闭的连接。
    /// </summary>
    /// <param name="peerElement">节点 JSON 元素，类型为 JsonElement，取值为 PeerInfo 对象，必填。</param>
    /// <returns>连接 JSON 元素，类型为 JsonElement 可空值；没有可用连接时返回 null。</returns>
    private static JsonElement? SelectActiveConnection(JsonElement peerElement)
    {
        // 连接列表缺失或为空时没有可展示的连接明细。
        if (!TryGetProperty(peerElement, "conns", out var connsElement) || connsElement.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        foreach (var connection in connsElement.EnumerateArray())
        {
            // 已关闭连接不作为展示对象。
            var isClosed = TryGetProperty(connection, "is_closed", out var closedElement) && closedElement.ValueKind == JsonValueKind.True;
            if (!isClosed)
            {
                return connection;
            }
        }

        return null;
    }
}
