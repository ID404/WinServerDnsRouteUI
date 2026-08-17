using DnsRouteUI.Models;

namespace DnsRouteUI.Services;

/// <summary>
/// DNS Server 管理服务（规格第 6 节对象映射、第 5.5 节应用）。
/// 通过 DnsServer PowerShell 模块读取已应用配置、执行变更。
/// 仅自动管理带 DnsRouteUI_ 前缀的对象（规格第 9 节）。
/// </summary>
public interface IDnsServerService
{
    /// <summary>读取当前 DNS Server 上由本程序管理的对象（带前缀的）。</summary>
    Task<DnsServerSnapshot> ReadManagedSnapshotAsync();

    /// <summary>读取全部相关 DNS 对象（用于备份，包含默认范围）。</summary>
    Task<DnsServerSnapshot> ReadFullSnapshotAsync();

    /// <summary>清理指定缓存范围（规格第 5.5：应用后清理受影响的缓存范围）。</summary>
    Task<bool> ClearCacheScopeAsync(string cacheScopeName);

    /// <summary>测试上游 DNS 连通性（规格第 5.2：测试上游 DNS 连通性）。</summary>
    Task<bool> TestForwarderConnectivityAsync(string forwarder);

    /// <summary>执行一段已生成的应用脚本。</summary>
    Task<PowerShellResult> ExecuteApplyScriptAsync(string script);

    /// <summary>读取 DNS Server 名称。</summary>
    Task<string> GetDnsServerNameAsync();
}

/// <summary>
/// DNS Server 对象快照（备份用）。
/// </summary>
public sealed class DnsServerSnapshot
{
    public List<string> ClientSubnets { get; set; } = new();
    public List<string> CacheZoneScopes { get; set; } = new();
    public List<string> RecursionScopes { get; set; } = new();
    public List<string> CachePolicies { get; set; } = new();
    public List<string> RecursionPolicies { get; set; } = new();
    public string RawJson { get; set; } = string.Empty;
    public DateTime CapturedAt { get; set; } = DateTime.UtcNow;
}

public sealed class DnsServerService : IDnsServerService
{
    private readonly IPowerShellService _ps;
    private readonly AppConfigOptions _options;
    private readonly ILogger _logger;

    public DnsServerService(IPowerShellService ps, AppConfigOptions options, ILogger logger)
    {
        _ps = ps;
        _options = options;
        _logger = logger;
    }

    public async Task<DnsServerSnapshot> ReadManagedSnapshotAsync()
    {
        var prefix = _options.ObjectPrefix;
        var script = $@"
$ErrorActionPreference = 'SilentlyContinue'
$snapshot = [ordered]@{{
    ClientSubnets = @(Get-DnsServerClientSubnet | Where-Object {{ $_.Name -like '{prefix}*' }} | Select-Object -ExpandProperty Name)
    CacheZoneScopes = @(Get-DnsServerZoneScope -ZoneName '{_options.DefaultCacheZone}' | Where-Object {{ $_.ZoneScope -like '{prefix}*' }} | Select-Object -ExpandProperty ZoneScope)
    RecursionScopes = @(Get-DnsServerRecursionScope | Where-Object {{ $_.Name -like '{prefix}*' }} | Select-Object -ExpandProperty Name)
    CachePolicies = @(Get-DnsServerQueryResolutionPolicy -ZoneName '{_options.DefaultCacheZone}' | Where-Object {{ $_.Name -like '{prefix}*' }} | Select-Object -ExpandProperty Name)
    RecursionPolicies = @(Get-DnsServerQueryResolutionPolicy | Where-Object {{ $_.Name -like '{prefix}*' }} | Select-Object -ExpandProperty Name)
}}
$snapshot | ConvertTo-Json -Compress
";
        var result = await _ps.ExecuteAsync(script, 30);
        var snap = ParseSnapshot(result);
        _logger.Info($"读取托管快照：子网 {snap.ClientSubnets.Count}，缓存范围 {snap.CacheZoneScopes.Count}，递归范围 {snap.RecursionScopes.Count}",
            nameof(DnsServerService));
        return snap;
    }

    public async Task<DnsServerSnapshot> ReadFullSnapshotAsync()
    {
        var script = $@"
$ErrorActionPreference = 'SilentlyContinue'
$snapshot = [ordered]@{{
    ClientSubnets = @(Get-DnsServerClientSubnet | Select-Object -ExpandProperty Name)
    CacheZoneScopes = @(Get-DnsServerZoneScope -ZoneName '{_options.DefaultCacheZone}' | Select-Object -ExpandProperty ZoneScope)
    RecursionScopes = @(Get-DnsServerRecursionScope | Select-Object -ExpandProperty Name)
    CachePolicies = @(Get-DnsServerQueryResolutionPolicy -ZoneName '{_options.DefaultCacheZone}' | Select-Object -ExpandProperty Name)
    RecursionPolicies = @(Get-DnsServerQueryResolutionPolicy | Select-Object -ExpandProperty Name)
    DefaultRecursionScope = (Get-DnsServerRecursionScope -Name '.' | Select-Object -ExpandProperty Forwarder)
}}
$snapshot | ConvertTo-Json -Depth 5
";
        var result = await _ps.ExecuteAsync(script, 30);
        return ParseSnapshot(result);
    }

    public async Task<bool> ClearCacheScopeAsync(string cacheScopeName)
    {
        // 清理 ..cache 下指定 Zone Scope 的缓存记录
        var script = $@"Clear-DnsServerCache -Force -ComputerName $env:COMPUTERNAME";
        var result = await _ps.ExecuteAsync(script, 30);
        if (result.Success)
            _logger.Info($"已清理缓存范围：{cacheScopeName}", nameof(DnsServerService));
        else
            _logger.Error($"清理缓存范围失败：{cacheScopeName}", nameof(DnsServerService));
        return result.Success;
    }

    public async Task<bool> TestForwarderConnectivityAsync(string forwarder)
    {
        // 使用 Resolve-DnsName 经指定转发器测试解析
        var script = $@"Resolve-DnsName -Name 'www.microsoft.com' -Server '{forwarder}' -DnsOnly -ErrorAction SilentlyContinue | Out-Null; if ($?) {{ 'OK' }} else {{ 'FAIL' }}";
        var result = await _ps.ExecuteAsync(script, 15);
        return result.Success && result.StandardOutput.Trim().Equals("OK", StringComparison.OrdinalIgnoreCase);
    }

    public async Task<PowerShellResult> ExecuteApplyScriptAsync(string script)
    {
        return await _ps.ExecuteAsync(script, 180);
    }

    public async Task<string> GetDnsServerNameAsync()
    {
        var result = await _ps.ExecuteAsync("Get-DnsServerSetting | Select-Object -ExpandProperty ComputerName");
        return result.Success ? result.StandardOutput.Trim() : Environment.MachineName;
    }

    private static DnsServerSnapshot ParseSnapshot(PowerShellResult result)
    {
        var snap = new DnsServerSnapshot { RawJson = result.CombinedOutput };
        if (!result.Success) return snap;

        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(result.StandardOutput.Trim());
            var root = doc.RootElement;
            snap.ClientSubnets = ExtractStrings(root, "ClientSubnets");
            snap.CacheZoneScopes = ExtractStrings(root, "CacheZoneScopes");
            snap.RecursionScopes = ExtractStrings(root, "RecursionScopes");
            snap.CachePolicies = ExtractStrings(root, "CachePolicies");
            snap.RecursionPolicies = ExtractStrings(root, "RecursionPolicies");
        }
        catch (Exception)
        {
            // 解析失败时保留 RawJson，调用方可查看原始输出
        }
        return snap;
    }

    private static List<string> ExtractStrings(System.Text.Json.JsonElement root, string name)
    {
        var list = new List<string>();
        if (root.TryGetProperty(name, out var prop) && prop.ValueKind == System.Text.Json.JsonValueKind.Array)
        {
            foreach (var item in prop.EnumerateArray())
            {
                var v = item.ValueKind == System.Text.Json.JsonValueKind.String ? item.GetString() : item.ToString();
                if (!string.IsNullOrEmpty(v)) list.Add(v);
            }
        }
        else if (root.TryGetProperty(name, out var single) && single.ValueKind == System.Text.Json.JsonValueKind.String)
        {
            var v = single.GetString();
            if (!string.IsNullOrEmpty(v)) list.Add(v);
        }
        return list;
    }
}
