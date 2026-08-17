namespace DnsRouteUI.Models;

/// <summary>
/// 变更预览项（规格第 5.5 节）。
/// 应用前展示将要创建/更新/删除的 DNS 对象。
/// </summary>
public sealed class ChangePreviewItem
{
    public enum ChangeAction
    {
        Create,
        Update,
        Delete
    }

    public enum ObjectType
    {
        ClientSubnet,
        CacheZoneScope,
        RecursionScope,
        CachePolicy,
        RecursionPolicy,
        DefaultRecursionScope
    }

    /// <summary>操作类型。</summary>
    public ChangeAction Action { get; set; }

    /// <summary>对象类型。</summary>
    public ObjectType Type { get; set; }

    /// <summary>对象名称。</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>对象详情（如转发器列表、CIDR、缓存范围等）。</summary>
    public string Detail { get; set; } = string.Empty;

    /// <summary>关联的规则或配置档 ID，便于定位来源。</summary>
    public string? SourceId { get; set; }

    /// <summary>是否为修改默认递归范围 "."（需要明确确认，规格第 9 节）。</summary>
    public bool IsDefaultScopeChange => Type == ObjectType.DefaultRecursionScope;

    /// <summary>
    /// 是否需要用户明确确认（仅当默认范围的转发器配置实际需要变更时为 true）。
    /// 由 ChangePreviewService 根据 DNS Server 快照对比设置。
    /// </summary>
    public bool NeedsConfirmation { get; set; }
}
