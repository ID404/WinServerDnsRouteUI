using System.Text.Json.Serialization;

namespace DnsRouteUI.Models;

/// <summary>
/// 上游解析配置档（规格第 5.2 节）。
/// 代表一套可共享的 DNS 解析上下文：转发器列表、递归开关、缓存范围等。
/// 相同配置档的不同网段策略共享同一缓存范围。
/// </summary>
public sealed class ResolverProfile
{
    /// <summary>程序内部唯一 ID。</summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>配置档名称，例如“阿里 DNS”。</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>转发器列表，一行一个 IPv4 地址。</summary>
    public List<string> Forwarders { get; set; } = new();

    /// <summary>是否启用递归，默认启用。</summary>
    public bool EnableRecursion { get; set; } = true;

    /// <summary>是否启用缓存隔离，默认启用。</summary>
    public bool CacheIsolationEnabled { get; set; } = true;

    /// <summary>缓存范围名称，自动生成，允许高级修改。</summary>
    public string CacheScopeName { get; set; } = string.Empty;

    /// <summary>备注，可选。</summary>
    public string Note { get; set; } = string.Empty;

    /// <summary>
    /// 生成默认缓存范围名称。规则：DnsRouteUI_Cache_{Id 首字母大写}。
    /// 由配置服务在创建/重命名时统一维护，避免与运行时前缀不一致。
    /// </summary>
    public static string BuildDefaultCacheScopeName(string prefix, string id)
    {
        var safe = string.IsNullOrWhiteSpace(id) ? "profile" : id;
        return $"{prefix}Cache_{safe}";
    }

    /// <summary>
    /// 生成对应的 Recursion Scope 名称。
    /// </summary>
    public static string BuildRecursionScopeName(string prefix, string id)
    {
        var safe = string.IsNullOrWhiteSpace(id) ? "profile" : id;
        return $"{prefix}Resolver_{safe}";
    }
}
