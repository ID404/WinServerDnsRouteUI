namespace DnsRouteUI.Models;

/// <summary>
/// 默认策略（规格第 5.4 节）。
/// 始终位于规则列表最下方，不可删除、不可移动，但可修改引用的配置档。
/// 底层使用 Windows DNS 的默认递归范围 "." 和默认缓存 "..cache"。
/// </summary>
public sealed class DefaultPolicy
{
    /// <summary>引用的上游解析配置档 ID。</summary>
    public string ResolverProfileId { get; set; } = string.Empty;

    /// <summary>启用状态。</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>备注。</summary>
    public string Note { get; set; } = "未匹配网段的默认出口";

    /// <summary>默认递归范围名称固定为 "."。</summary>
    public const string DefaultRecursionScope = ".";

    /// <summary>默认缓存 Zone 名称固定为 "..cache"。</summary>
    public const string DefaultCacheZone = "..cache";
}
