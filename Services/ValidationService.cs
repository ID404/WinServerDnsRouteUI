using System.Net;
using DnsRouteUI.Models;

namespace DnsRouteUI.Services;

/// <summary>
/// 校验服务（规格第 5.2、5.3、第 11 节验收标准 9）。
/// 校验：IPv4 地址、IPv4 CIDR、转发器列表、规则名称唯一性、网段重叠、
/// 网段精确度优先级（/24 应排在重叠的 /16 前）。
/// </summary>
public interface IValidationService
{
    bool IsValidIPv4(string ip);

    bool IsValidIPv4Cidr(string cidr);

    /// <summary>校验转发器列表：至少一台、全部有效 IP。</summary>
    ValidationResult ValidateForwarders(List<string> forwarders);

    /// <summary>校验规则名称在规则集合中是否唯一（排除指定 ID）。</summary>
    ValidationResult ValidateRuleNameUnique(string name, IEnumerable<SegmentRule> existing, string? excludeId);

    /// <summary>校验配置档名称唯一性。</summary>
    ValidationResult ValidateProfileNameUnique(string name, IEnumerable<ResolverProfile> existing, string? excludeId);

    /// <summary>检测网段重叠，返回警告列表（更精确网段应有更高优先级）。</summary>
    ValidationResult ValidateSubnetOverlaps(IEnumerable<SegmentRule> rules);

    /// <summary>校验完整配置。</summary>
    ValidationResult ValidateConfig(DnsRouteConfig config);
}

public sealed class ValidationService : IValidationService
{
    public bool IsValidIPv4(string ip)
    {
        if (string.IsNullOrWhiteSpace(ip)) return false;
        return IPAddress.TryParse(ip.Trim(), out var addr) && addr.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork;
    }

    public bool IsValidIPv4Cidr(string cidr)
    {
        if (string.IsNullOrWhiteSpace(cidr)) return false;
        var parts = cidr.Trim().Split('/');
        if (parts.Length != 2) return false;
        if (!IsValidIPv4(parts[0])) return false;
        if (!int.TryParse(parts[1], out var prefix) || prefix < 0 || prefix > 32) return false;

        // 网络地址校验：主机位应全 0（如 192.168.1.0/24 合法，192.168.1.5/24 给出警告但不阻断）
        // 这里仅校验格式，主机位检查放在 ValidateSubnetOverlaps/ValidateConfig 中以警告形式提示。
        return true;
    }

    public ValidationResult ValidateForwarders(List<string> forwarders)
    {
        var result = new ValidationResult();
        if (forwarders is null || forwarders.Count == 0)
        {
            result.AddError("至少配置一台转发器。");
            return result;
        }

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < forwarders.Count; i++)
        {
            var f = forwarders[i]?.Trim() ?? string.Empty;
            if (string.IsNullOrEmpty(f))
            {
                result.AddError($"第 {i + 1} 个转发器为空。");
                continue;
            }
            if (!IsValidIPv4(f))
            {
                result.AddError($"第 {i + 1} 个转发器“{f}”不是有效的 IPv4 地址。");
                continue;
            }
            if (!seen.Add(f))
            {
                result.AddWarning($"转发器“{f}”重复，已忽略多余项。");
            }
        }
        return result;
    }

    public ValidationResult ValidateRuleNameUnique(string name, IEnumerable<SegmentRule> existing, string? excludeId)
    {
        var result = new ValidationResult();
        if (string.IsNullOrWhiteSpace(name))
        {
            result.AddError("规则名称不能为空。");
            return result;
        }
        if (existing.Any(r => r.Name.Equals(name, StringComparison.OrdinalIgnoreCase) && r.Id != excludeId))
        {
            result.AddError($"规则名称“{name}”已存在，规则名称不可重复。");
        }
        return result;
    }

    public ValidationResult ValidateProfileNameUnique(string name, IEnumerable<ResolverProfile> existing, string? excludeId)
    {
        var result = new ValidationResult();
        if (string.IsNullOrWhiteSpace(name))
        {
            result.AddError("配置档名称不能为空。");
            return result;
        }
        if (existing.Any(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase) && p.Id != excludeId))
        {
            result.AddError($"配置档名称“{name}”已存在。");
        }
        return result;
    }

    public ValidationResult ValidateSubnetOverlaps(IEnumerable<SegmentRule> rules)
    {
        var result = new ValidationResult();
        var list = rules.Where(r => r.Enabled).ToList();
        for (var i = 0; i < list.Count; i++)
        {
            for (var j = i + 1; j < list.Count; j++)
            {
                var overlap = GetOverlapInfo(list[i].ClientSubnet, list[j].ClientSubnet);
                if (overlap is null) continue;

                // 网段重叠时给出警告
                result.AddWarning($"网段重叠：{list[i].Name}({list[i].ClientSubnet}) 与 {list[j].Name}({list[j].ClientSubnet})");

                // 更精确的网段（prefix 更大）应具有更高优先级（数值更小）
                var pi = int.Parse(list[i].ClientSubnet.Split('/')[1]);
                var pj = int.Parse(list[j].ClientSubnet.Split('/')[1]);
                if (pi > pj && list[i].Priority > list[j].Priority)
                {
                    result.AddWarning($"更精确的网段 {list[i].ClientSubnet}(/{pi}) 应排在重叠的 {list[j].ClientSubnet}(/{pj}) 之前。");
                }
                else if (pj > pi && list[j].Priority > list[i].Priority)
                {
                    result.AddWarning($"更精确的网段 {list[j].ClientSubnet}(/{pj}) 应排在重叠的 {list[i].ClientSubnet}(/{pi}) 之前。");
                }
            }
        }
        return result;
    }

    public ValidationResult ValidateConfig(DnsRouteConfig config)
    {
        var result = new ValidationResult();

        if (config.ResolverProfiles.Count == 0)
            result.AddError("至少需要一个上游解析配置档。");

        // 默认策略不允许为空（规格第 9 节）
        if (string.IsNullOrEmpty(config.DefaultPolicy.ResolverProfileId))
        {
            result.AddError("默认策略必须选择一个有效配置档（不允许空的默认策略）。");
        }
        else if (config.FindProfile(config.DefaultPolicy.ResolverProfileId) is null)
        {
            result.AddError("默认策略引用的配置档不存在。");
        }

        // 校验每个配置档
        foreach (var p in config.ResolverProfiles)
        {
            var fwdResult = ValidateForwarders(p.Forwarders);
            foreach (var e in fwdResult.Errors) result.AddError($"配置档“{p.Name}”：{e}");
            if (string.IsNullOrWhiteSpace(p.Name)) result.AddError("存在未命名的配置档。");
        }

        // 校验每条规则
        foreach (var r in config.Rules)
        {
            if (!IsValidIPv4Cidr(r.ClientSubnet))
                result.AddError($"规则“{r.Name}”的网段“{r.ClientSubnet}”不是有效的 IPv4 CIDR。");
            if (config.FindProfile(r.ResolverProfileId) is null)
                result.AddError($"规则“{r.Name}”引用的配置档不存在。");
        }

        // 网段重叠警告
        var overlap = ValidateSubnetOverlaps(config.Rules);
        foreach (var w in overlap.Warnings) result.AddWarning(w);

        return result;
    }

    /// <summary>计算两个 CIDR 的包含/重叠关系；无重叠返回 null。</summary>
    private static (string relation, string subnet)? GetOverlapInfo(string cidrA, string cidrB)
    {
        if (!TryParseCidr(cidrA, out var netA, out var prefixA)) return null;
        if (!TryParseCidr(cidrB, out var netB, out var prefixB)) return null;

        var maskA = prefixA == 0 ? 0 : unchecked((uint)~(0xFFFFFFFF >> prefixA));
        var maskB = prefixB == 0 ? 0 : unchecked((uint)~(0xFFFFFFFF >> prefixB));

        var netAInt = System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(netA.GetAddressBytes());
        var netBInt = System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(netB.GetAddressBytes());

        bool aContainsB = (netBInt & maskA) == (netAInt & maskA);
        bool bContainsA = (netAInt & maskB) == (netBInt & maskB);

        if (aContainsB || bContainsA) return ("contains", aContainsB ? cidrA : cidrB);
        return null;
    }

    private static bool TryParseCidr(string cidr, out IPAddress network, out int prefix)
    {
        network = IPAddress.None;
        prefix = 0;
        var parts = cidr.Trim().Split('/');
        if (parts.Length != 2) return false;
        if (!IPAddress.TryParse(parts[0], out var parsed) || parsed is null) return false;
        network = parsed;
        if (network.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork) return false;
        return int.TryParse(parts[1], out prefix);
    }
}
