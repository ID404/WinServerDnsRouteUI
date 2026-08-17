namespace DnsRouteUI.Models;

/// <summary>
/// DNS 查询测试结果（规格第 5.6 节）。
/// </summary>
public sealed class DnsTestResult
{
    /// <summary>测试客户端 IP。</summary>
    public string ClientIp { get; set; } = string.Empty;

    /// <summary>待解析域名。</summary>
    public string Domain { get; set; } = string.Empty;

    /// <summary>记录类型，默认 A。</summary>
    public string RecordType { get; set; } = "A";

    /// <summary>命中的规则或默认策略名称。</summary>
    public string? MatchedRule { get; set; }

    /// <summary>是否命中默认策略。</summary>
    public bool IsDefaultPolicy { get; set; }

    /// <summary>使用的上游解析配置档名称。</summary>
    public string? ResolverProfile { get; set; }

    /// <summary>预计使用的缓存范围。</summary>
    public string? CacheScope { get; set; }

    /// <summary>预计使用的上游 DNS（逗号分隔）。</summary>
    public string? UpstreamDns { get; set; }

    /// <summary>DNS 查询结果。</summary>
    public string? QueryResult { get; set; }

    /// <summary>缓存命中或未命中。</summary>
    public string? CacheStatus { get; set; }

    /// <summary>是否成功。</summary>
    public bool Success { get; set; }

    /// <summary>错误详情。</summary>
    public string? Error { get; set; }
}
