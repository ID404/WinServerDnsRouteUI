namespace DnsRouteUI.Models;

/// <summary>
/// 缓存隔离策略模式（规格第 10 节）。
/// </summary>
public enum CacheIsolationMode
{
    /// <summary>共享默认缓存：所有策略共用默认缓存，追求最高缓存命中率。</summary>
    SharedDefault = 0,

    /// <summary>按配置档隔离缓存：同一上游解析配置档共享缓存。推荐默认模式。</summary>
    ByResolverProfile = 1,

    /// <summary>按策略隔离缓存：每条网段规则单独缓存，解析结果完全隔离。</summary>
    ByRule = 2
}
