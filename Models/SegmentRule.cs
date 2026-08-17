namespace DnsRouteUI.Models;

using System.Text.Json.Serialization;

/// <summary>
/// 客户端网段策略（规格第 5.3 节）。
/// 一条网段规则会创建：一条缓存策略 + 一条递归策略（规格第 6 节）。
/// </summary>
public sealed class SegmentRule
{
    /// <summary>规则唯一 ID。</summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>规则名称，不可重复。</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>客户端 IPv4 网段（CIDR），例如 192.168.1.0/24。</summary>
    public string ClientSubnet { get; set; } = string.Empty;

    /// <summary>引用的上游解析配置档 ID。</summary>
    public string ResolverProfileId { get; set; } = string.Empty;

    /// <summary>优先级，数值越小越优先。默认策略固定为最低优先级。</summary>
    public int Priority { get; set; } = 1;

    /// <summary>启用状态。禁用后该网段请求回落至后续规则或默认策略。</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>备注，可选。</summary>
    public string Note { get; set; } = string.Empty;

    /// <summary>
    /// 引用的配置档名称（仅用于表格展示，由 ViewModel.Reload 时填充，不持久化）。
    /// </summary>
    [JsonIgnore]
    public string ResolverProfileName { get; set; } = string.Empty;

    /// <summary>Client Subnet 对象名称（DnsRouteUI_Subnet_{Id}）。</summary>
    public static string BuildClientSubnetName(string prefix, string id)
    {
        var safe = string.IsNullOrWhiteSpace(id) ? "rule" : id;
        return $"{prefix}Subnet_{safe}";
    }

    /// <summary>缓存策略名称（..cache 的 Query Resolution Policy）。</summary>
    public static string BuildCachePolicyName(string prefix, string id)
    {
        var safe = string.IsNullOrWhiteSpace(id) ? "rule" : id;
        return $"{prefix}CachePolicy_{safe}";
    }

    /// <summary>递归策略名称（Recursion Query Resolution Policy）。</summary>
    public static string BuildRecursionPolicyName(string prefix, string id)
    {
        var safe = string.IsNullOrWhiteSpace(id) ? "rule" : id;
        return $"{prefix}RecursionPolicy_{safe}";
    }
}
