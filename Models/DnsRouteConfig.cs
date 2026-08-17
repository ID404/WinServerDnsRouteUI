namespace DnsRouteUI.Models;

/// <summary>
/// 程序配置根（规格第 7 节数据模型）。
/// 持久化为 JSON 到 C:\ProgramData\DnsRouteUI\config.json。
/// </summary>
public sealed class DnsRouteConfig
{
    /// <summary>配置版本号，用于未来迁移。</summary>
    public int Version { get; set; } = 1;

    /// <summary>缓存隔离模式（规格第 10 节），默认按配置档隔离。</summary>
    public CacheIsolationMode CacheIsolation { get; set; } = CacheIsolationMode.ByResolverProfile;

    /// <summary>
    /// 是否启用按条件转发（DNS 分流）。
    /// 开启时：应用网段递归策略与缓存策略，按客户端网段分流到不同上游 DNS。
    /// 关闭时：不生成/不应用递归策略，DNS Server 回退到默认递归行为。
    /// 首次运行时由环境检测自动判断当前 DNS Server 是否已存在本程序管理的策略。
    /// </summary>
    public bool ConditionalForwardingEnabled { get; set; } = true;

    /// <summary>上游解析配置档列表。</summary>
    public List<ResolverProfile> ResolverProfiles { get; set; } = new();

    /// <summary>网段策略列表（不含默认策略）。</summary>
    public List<SegmentRule> Rules { get; set; } = new();

    /// <summary>默认策略。</summary>
    public DefaultPolicy DefaultPolicy { get; set; } = new();

    /// <summary>最近一次应用操作的时间（UTC ISO 8601）。</summary>
    public string? LastAppliedAt { get; set; }

    /// <summary>最近一次应用操作的结果摘要。</summary>
    public string? LastApplyResult { get; set; }

    /// <summary>根据 ID 查找配置档。</summary>
    public ResolverProfile? FindProfile(string id) =>
        ResolverProfiles.FirstOrDefault(p => p.Id == id);

    /// <summary>根据 ID 查找规则。</summary>
    public SegmentRule? FindRule(string id) =>
        Rules.FirstOrDefault(r => r.Id == id);

    /// <summary>统计被指定配置档引用的规则数（含默认策略）。</summary>
    public int CountProfileReferences(string profileId)
    {
        var ruleCount = Rules.Count(r => r.ResolverProfileId == profileId);
        var defaultCount = DefaultPolicy.ResolverProfileId == profileId ? 1 : 0;
        return ruleCount + defaultCount;
    }
}
