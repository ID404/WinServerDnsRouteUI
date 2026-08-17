using DnsRouteUI.Models;

namespace DnsRouteUI.Services;

/// <summary>
/// 变更预览服务（规格第 5.5 节、第 6 节对象映射）。
/// 比对当前程序配置与已应用的 DNS Server 快照，生成变更预览项列表。
/// </summary>
public interface IChangePreviewService
{
    /// <summary>生成变更预览。</summary>
    /// <param name="config">目标配置。</param>
    /// <param name="current">当前已应用的 DNS 对象快照（可为 null 表示全量创建）。</param>
    List<ChangePreviewItem> BuildPreview(DnsRouteConfig config, DnsServerSnapshot? current);

    /// <summary>格式化预览为可读文本（用于导出/展示）。</summary>
    string FormatPreviewText(List<ChangePreviewItem> items);
}

public sealed class ChangePreviewService : IChangePreviewService
{
    private readonly AppConfigOptions _options;

    public ChangePreviewService(AppConfigOptions options)
    {
        _options = options;
    }

    public List<ChangePreviewItem> BuildPreview(DnsRouteConfig config, DnsServerSnapshot? current)
    {
        var items = new List<ChangePreviewItem>();
        var prefix = _options.ObjectPrefix;

        var existingSubnets = current?.ClientSubnets ?? new List<string>();
        var existingCacheScopes = current?.CacheZoneScopes ?? new List<string>();
        var existingRecursionScopes = current?.RecursionScopes ?? new List<string>();
        var existingCachePolicies = current?.CachePolicies ?? new List<string>();
        var existingRecursionPolicies = current?.RecursionPolicies ?? new List<string>();

        // 条件转发关闭时：仅生成清理递归策略/范围的预览项，不创建新分流策略
        if (!config.ConditionalForwardingEnabled)
        {
            foreach (var scopeName in existingRecursionScopes)
            {
                items.Add(new ChangePreviewItem
                {
                    Action = ChangePreviewItem.ChangeAction.Delete,
                    Type = ChangePreviewItem.ObjectType.RecursionScope,
                    Name = scopeName,
                    Detail = "条件转发已关闭，移除已应用的递归范围"
                });
            }
            foreach (var policyName in existingRecursionPolicies)
            {
                items.Add(new ChangePreviewItem
                {
                    Action = ChangePreviewItem.ChangeAction.Delete,
                    Type = ChangePreviewItem.ObjectType.RecursionPolicy,
                    Name = policyName,
                    Detail = "条件转发已关闭，移除已应用的递归策略"
                });
            }
            if (items.Count == 0)
            {
                items.Add(new ChangePreviewItem
                {
                    Action = ChangePreviewItem.ChangeAction.Update,
                    Type = ChangePreviewItem.ObjectType.DefaultRecursionScope,
                    Name = _options.DefaultRecursionScope,
                    Detail = "条件转发已关闭，DNS Server 回退默认递归行为（无需变更）"
                });
            }
            return items;
        }

        // 1. 配置档：Recursion Scope + Cache Zone Scope
        foreach (var profile in config.ResolverProfiles)
        {
            // 与 ScriptExportService 保持一致：递归范围名称始终由 BuildRecursionScopeName 生成
            var recursionScope = ResolverProfile.BuildRecursionScopeName(prefix, profile.Id);
            var cacheScope = string.IsNullOrEmpty(profile.CacheScopeName)
                ? ResolverProfile.BuildDefaultCacheScopeName(prefix, profile.Id)
                : profile.CacheScopeName;

            var fwd = string.Join(", ", profile.Forwarders);

            items.Add(new ChangePreviewItem
            {
                Action = existingRecursionScopes.Contains(recursionScope) ? ChangePreviewItem.ChangeAction.Update : ChangePreviewItem.ChangeAction.Create,
                Type = ChangePreviewItem.ObjectType.RecursionScope,
                Name = recursionScope,
                Detail = $"转发器：{fwd}；递归={(profile.EnableRecursion ? "启用" : "禁用")}",
                SourceId = profile.Id
            });

            // 缓存范围（仅当配置档启用缓存隔离时创建独立 Zone Scope）
            if (profile.CacheIsolationEnabled)
            {
                items.Add(new ChangePreviewItem
                {
                    Action = existingCacheScopes.Contains(cacheScope) ? ChangePreviewItem.ChangeAction.Update : ChangePreviewItem.ChangeAction.Create,
                    Type = ChangePreviewItem.ObjectType.CacheZoneScope,
                    Name = $"{_options.DefaultCacheZone} / {cacheScope}",
                    Detail = $"配置档：{profile.Name}",
                    SourceId = profile.Id
                });
            }
        }

        // 2. 网段规则：Client Subnet + Cache Policy + Recursion Policy
        var orderedRules = config.Rules.Where(r => r.Enabled).OrderBy(r => r.Priority).ToList();
        foreach (var rule in orderedRules)
        {
            var profile = config.FindProfile(rule.ResolverProfileId);
            if (profile is null) continue;

            var subnetName = SegmentRule.BuildClientSubnetName(prefix, rule.Id);
            var cachePolicy = SegmentRule.BuildCachePolicyName(prefix, rule.Id);
            var recursionPolicy = SegmentRule.BuildRecursionPolicyName(prefix, rule.Id);
            var cacheScope = profile.CacheIsolationEnabled
                ? (string.IsNullOrEmpty(profile.CacheScopeName) ? ResolverProfile.BuildDefaultCacheScopeName(prefix, profile.Id) : profile.CacheScopeName)
                : DefaultPolicy.DefaultCacheZone;
            // 与 ScriptExportService 保持一致：递归范围名称始终由 BuildRecursionScopeName 生成
            var recursionScope = ResolverProfile.BuildRecursionScopeName(prefix, profile.Id);

            items.Add(new ChangePreviewItem
            {
                Action = existingSubnets.Contains(subnetName) ? ChangePreviewItem.ChangeAction.Update : ChangePreviewItem.ChangeAction.Create,
                Type = ChangePreviewItem.ObjectType.ClientSubnet,
                Name = subnetName,
                Detail = $"客户端：{rule.ClientSubnet}",
                SourceId = rule.Id
            });

            items.Add(new ChangePreviewItem
            {
                Action = existingCachePolicies.Contains(cachePolicy) ? ChangePreviewItem.ChangeAction.Update : ChangePreviewItem.ChangeAction.Create,
                Type = ChangePreviewItem.ObjectType.CachePolicy,
                Name = cachePolicy,
                Detail = $"客户端：{rule.ClientSubnet}；缓存范围：{cacheScope}",
                SourceId = rule.Id
            });

            items.Add(new ChangePreviewItem
            {
                Action = existingRecursionPolicies.Contains(recursionPolicy) ? ChangePreviewItem.ChangeAction.Update : ChangePreviewItem.ChangeAction.Create,
                Type = ChangePreviewItem.ObjectType.RecursionPolicy,
                Name = recursionPolicy,
                Detail = $"客户端：{rule.ClientSubnet}；递归范围：{recursionScope}",
                SourceId = rule.Id
            });
        }

        // 3. 默认策略：更新默认递归范围 "."
        var defaultProfile = config.FindProfile(config.DefaultPolicy.ResolverProfileId);
        if (defaultProfile is not null && config.DefaultPolicy.Enabled)
        {
            var fwd = string.Join(", ", defaultProfile.Forwarders);
            items.Add(new ChangePreviewItem
            {
                Action = ChangePreviewItem.ChangeAction.Update,
                Type = ChangePreviewItem.ObjectType.DefaultRecursionScope,
                Name = _options.DefaultRecursionScope,
                Detail = $"默认递归范围 → {fwd}",
                SourceId = config.DefaultPolicy.ResolverProfileId
            });
        }

        return items;
    }

    public string FormatPreviewText(List<ChangePreviewItem> items)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("变更预览（规格第 5.5 节）");
        sb.AppendLine(new string('=', 50));

        var grouped = items.GroupBy(i => i.Type);
        foreach (var g in grouped)
        {
            sb.AppendLine();
            sb.AppendLine($"[{GetTypeLabel(g.Key)}]");
            foreach (var item in g)
            {
                var actionTag = item.Action switch
                {
                    ChangePreviewItem.ChangeAction.Create => "创建",
                    ChangePreviewItem.ChangeAction.Update => "更新",
                    ChangePreviewItem.ChangeAction.Delete => "删除",
                    _ => item.Action.ToString()
                };
                var dangerTag = item.IsDefaultScopeChange ? " ⚠ 需明确确认" : "";
                sb.AppendLine($"  {actionTag} {item.Name}{dangerTag}");
                sb.AppendLine($"    {item.Detail}");
            }
        }

        var hasDanger = items.Any(i => i.IsDefaultScopeChange);
        if (hasDanger)
        {
            sb.AppendLine();
            sb.AppendLine("注意：本次变更涉及默认递归范围 \".\"，需明确确认（规格第 9 节）。");
        }

        return sb.ToString();
    }

    private static string GetTypeLabel(ChangePreviewItem.ObjectType type) => type switch
    {
        ChangePreviewItem.ObjectType.ClientSubnet => "Client Subnet",
        ChangePreviewItem.ObjectType.CacheZoneScope => "Cache Zone Scope",
        ChangePreviewItem.ObjectType.RecursionScope => "Recursion Scope",
        ChangePreviewItem.ObjectType.CachePolicy => "Cache Policy",
        ChangePreviewItem.ObjectType.RecursionPolicy => "Recursion Policy",
        ChangePreviewItem.ObjectType.DefaultRecursionScope => "默认递归范围",
        _ => type.ToString()
    };
}
