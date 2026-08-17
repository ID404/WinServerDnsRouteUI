using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DnsRouteUI.Models;
using DnsRouteUI.Mvvm;
using DnsRouteUI.Services;

namespace DnsRouteUI.ViewModels;

/// <summary>
/// 测试与日志视图模型（规格第 5.6 节）。
/// 输入测试客户端 IP 与域名，输出命中的规则、配置档、缓存范围、上游 DNS、查询结果。
/// </summary>
public partial class TestLogViewModel : ViewModelBase
{
    private readonly IConfigService _config;
    private readonly IPowerShellService _ps;
    private readonly ILogger _logger;

    public TestLogViewModel(IConfigService config, IPowerShellService ps, ILogger logger)
    {
        _config = config;
        _ps = ps;
        _logger = logger;
    }

    [ObservableProperty]
    private string _clientIp = "192.168.1.10";

    [ObservableProperty]
    private string _domain = "www.example.com";

    [ObservableProperty]
    private string _recordType = "A";

    [ObservableProperty]
    private DnsTestResult? _result;

    [ObservableProperty]
    private bool _isTesting;

    public ObservableCollection<string> LogLines { get; } = new();

    [RelayCommand]
    private async Task RunTestAsync()
    {
        if (string.IsNullOrWhiteSpace(ClientIp) || string.IsNullOrWhiteSpace(Domain))
        {
            LogLines.Insert(0, $"[{DateTime.Now:HH:mm:ss}] 请输入客户端 IP 和域名。");
            return;
        }

        IsTesting = true;
        var result = new DnsTestResult
        {
            ClientIp = ClientIp,
            Domain = Domain,
            RecordType = string.IsNullOrWhiteSpace(RecordType) ? "A" : RecordType
        };

        try
        {
            // 1. 本地匹配规则（模拟 DNS Server 策略匹配）
            var match = MatchRule(ClientIp, _config.Current);
            if (match.matched is not null)
            {
                result.MatchedRule = match.matched.Name;
                result.IsDefaultPolicy = false;
            }
            else
            {
                result.MatchedRule = "默认策略";
                result.IsDefaultPolicy = true;
            }

            var profileId = match.profileId ?? _config.Current.DefaultPolicy.ResolverProfileId;
            var profile = _config.Current.FindProfile(profileId);
            if (profile is not null)
            {
                result.ResolverProfile = profile.Name;
                result.CacheScope = profile.CacheIsolationEnabled ? profile.CacheScopeName : DefaultPolicy.DefaultCacheZone;
                result.UpstreamDns = string.Join(", ", profile.Forwarders);
            }

            // 2. 执行 DNS 查询（经指定上游 DNS）
            var primary = profile?.Forwarders.FirstOrDefault();
            if (!string.IsNullOrEmpty(primary))
            {
                var script = $"Resolve-DnsName -Name '{Domain}' -Type {result.RecordType} -Server '{primary}' -DnsOnly -ErrorAction SilentlyContinue | ConvertTo-Json -Depth 3";
                var ps = await _ps.ExecuteAsync(script, 15);
                if (ps.Success)
                {
                    result.QueryResult = string.IsNullOrWhiteSpace(ps.StandardOutput) ? "（无记录）" : ps.StandardOutput.Trim();
                    result.Success = true;
                }
                else
                {
                    result.Success = false;
                    result.Error = ps.CombinedOutput;
                }
            }
            else
            {
                result.Success = false;
                result.Error = "未配置上游 DNS。";
            }

            result.CacheStatus = "未命中（首次查询）";
        }
        catch (Exception ex)
        {
            result.Success = false;
            result.Error = ex.Message;
            _logger.Error("测试执行异常。", nameof(TestLogViewModel), ex);
        }
        finally
        {
            Result = result;
            var status = result.Success ? "成功" : "失败";
            LogLines.Insert(0, $"[{DateTime.Now:HH:mm:ss}] 测试 {Domain}@{result.RecordType} from {ClientIp} → {result.MatchedRule} [{status}]");
            IsTesting = false;
        }
    }

    /// <summary>按 CIDR 匹配网段规则（更精确的网段优先）。</summary>
    private static (SegmentRule? matched, string? profileId) MatchRule(string clientIp, DnsRouteConfig config)
    {
        if (!System.Net.IPAddress.TryParse(clientIp, out var ip)) return (null, null);
        if (ip.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork) return (null, null);

        var ipBytes = ip.GetAddressBytes();
        var ipUint = System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(ipBytes);

        SegmentRule? best = null;
        int bestPrefix = -1;
        foreach (var rule in config.Rules.Where(r => r.Enabled))
        {
            var parts = rule.ClientSubnet.Split('/');
            if (parts.Length != 2) continue;
            if (!System.Net.IPAddress.TryParse(parts[0], out var net)) continue;
            if (!int.TryParse(parts[1], out var prefix)) continue;

            var netBytes = net.GetAddressBytes();
            var netUint = System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(netBytes);
            var mask = prefix == 0 ? 0u : unchecked((uint)~(0xFFFFFFFFu >> prefix));

            if ((ipUint & mask) == (netUint & mask))
            {
                // 更精确的网段（prefix 更大）优先
                if (prefix > bestPrefix)
                {
                    bestPrefix = prefix;
                    best = rule;
                }
            }
        }

        return best is not null ? (best, best.ResolverProfileId) : (null, null);
    }
}
