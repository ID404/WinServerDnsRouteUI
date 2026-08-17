using System.IO;
using System.Text;
using DnsRouteUI.Models;

namespace DnsRouteUI.Services;

/// <summary>
/// PowerShell 脚本导出服务（规格第 5.5 节、第 6 节对象映射）。
/// 根据配置生成可独立执行的 DnsServer 模块 PowerShell 脚本，
/// 支持“仅生成脚本，不执行”模式（规格第 9 节）。
/// </summary>
public interface IScriptExportService
{
    /// <summary>生成应用脚本（创建/更新所有 DNS 对象）。</summary>
    string GenerateApplyScript(DnsRouteConfig config);

    /// <summary>生成回滚脚本（移除所有带前缀的托管对象）。</summary>
    string GenerateRollbackScript();

    /// <summary>将脚本写入文件。</summary>
    string SaveScript(string script, string fileName);
}

public sealed class ScriptExportService : IScriptExportService
{
    private readonly AppConfigOptions _options;
    private readonly ILogger _logger;

    public ScriptExportService(AppConfigOptions options, ILogger logger)
    {
        _options = options;
        _logger = logger;
    }

    public string GenerateApplyScript(DnsRouteConfig config)
    {
        var prefix = _options.ObjectPrefix;
        var cacheZone = _options.DefaultCacheZone;
        var sb = new StringBuilder();

        sb.AppendLine("# ============================================================");
        sb.AppendLine("# DnsRouteUI 应用脚本");
        sb.AppendLine($"# 生成时间：{DateTime.Now:O}");
        sb.AppendLine("# 目标：Windows Server 2019 DNS Server");
        sb.AppendLine("# 所有对象统一使用 DnsRouteUI_ 前缀，避免误操作手工配置。");
        sb.AppendLine($"# 条件转发（DNS 分流）：{(config.ConditionalForwardingEnabled ? "启用" : "禁用")}");
        sb.AppendLine("# ============================================================");
        sb.AppendLine();
        sb.AppendLine("# ===== 错误处理（关键）：所有变更命令失败必须立即终止 =====");
        sb.AppendLine("$ErrorActionPreference = 'Stop'");
        sb.AppendLine("Import-Module DnsServer -ErrorAction Stop");
        sb.AppendLine();
        sb.AppendLine("# 公共辅助函数：可靠地判断对象是否存在（不用 Get-xxx -Name + -ErrorAction SilentlyContinue 模式，");
        sb.AppendLine("# 该模式在 DnsServer 模块 + $ErrorActionPreference=Stop 下经常异常中断）");
        sb.AppendLine("function Test-ItemByList($getAllCmd, $nameProperty, $targetName) {");
        sb.AppendLine("    $items = & $getAllCmd -ErrorAction SilentlyContinue");
        sb.AppendLine("    return [bool]($items | Where-Object { $_.$nameProperty -eq $targetName })");
        sb.AppendLine("}");
        sb.AppendLine("function Remove-IfExists($getAllCmd, $nameProperty, $targetName, $removeCmd) {");
        sb.AppendLine("    $items = & $getAllCmd -ErrorAction SilentlyContinue");
        sb.AppendLine("    $match = $items | Where-Object { $_.$nameProperty -eq $targetName }");
        sb.AppendLine("    if ($match) {");
        sb.AppendLine("        Write-Host \"  [清理] 移除已存在: $targetName\" -ForegroundColor DarkGray");
        sb.AppendLine("        & $removeCmd -ErrorAction Stop");
        sb.AppendLine("    }");
        sb.AppendLine("}");
        sb.AppendLine();

        // 条件转发关闭时：仅清理已应用的递归策略与递归范围，不创建新的分流策略
        if (!config.ConditionalForwardingEnabled)
        {
            sb.AppendLine("# --- 条件转发已关闭：清理已应用的递归策略与递归范围（不影响缓存配置） ---");
            sb.AppendLine("Write-Host '条件转发开关=关闭，正在清理已应用的递归策略与递归范围...' -ForegroundColor Yellow");
            sb.AppendLine($"Get-DnsServerQueryResolutionPolicy -ErrorAction SilentlyContinue | Where-Object {{ $_.Name -like '{prefix}*' }} | ForEach-Object {{ Write-Host \"  [清理] RecursionPolicy: $($_.Name)\" -ForegroundColor DarkGray; Remove-DnsServerQueryResolutionPolicy -Name $_.Name -Force -ErrorAction Stop }}");
            sb.AppendLine($"Get-DnsServerRecursionScope -ErrorAction SilentlyContinue | Where-Object {{ $_.Name -like '{prefix}*' }} | ForEach-Object {{ Write-Host \"  [清理] RecursionScope: $($_.Name)\" -ForegroundColor DarkGray; Remove-DnsServerRecursionScope -Name $_.Name -Force -ErrorAction Stop }}");
            sb.AppendLine();
            sb.AppendLine("# 清理缓存");
            sb.AppendLine("Clear-DnsServerCache -Force -ErrorAction Stop");
            sb.AppendLine("Write-Host '条件转发已关闭：递归策略与递归范围已清理，DNS Server 回退默认递归行为。' -ForegroundColor Green");
            sb.AppendLine();
            sb.AppendLine("# 条件转发关闭模式——结束，不再创建分流对象");
            return sb.ToString();
        }

        // ============================================================
        // 前置：按依赖顺序全量清理上次应用的 DnsRouteUI_ 前缀对象
        // 依赖关系：策略 → 子网/范围/缓存范围，必须先删引用方再删被引用方
        // 否则 Remove-DnsServerRecursionScope 会报 WIN32 9988（对象被引用）
        // ============================================================
        sb.AppendLine("# =============================================================");
        sb.AppendLine("# 前置：清理上次应用的托管对象（按依赖顺序，避免删除被引用的对象时失败）");
        sb.AppendLine("# =============================================================");
        sb.AppendLine("Write-Host '正在清理上次应用的托管对象（如有）...' -ForegroundColor Yellow");
        // 1) 服务器级 QueryResolutionPolicy（引用 RecursionScope + ClientSubnet，必须最先删）
        sb.AppendLine($"Get-DnsServerQueryResolutionPolicy -ErrorAction SilentlyContinue | Where-Object {{ $_.Name -like '{prefix}*' }} | ForEach-Object {{ Write-Host \"  [清理] ServerPolicy: $($_.Name)\" -ForegroundColor DarkGray; Remove-DnsServerQueryResolutionPolicy -Name $_.Name -Force -ErrorAction Stop }}");
        // 2) ..cache 分区级 QueryResolutionPolicy（引用 ClientSubnet + CacheZoneScope）
        sb.AppendLine($"Get-DnsServerQueryResolutionPolicy -ZoneName '{cacheZone}' -ErrorAction SilentlyContinue | Where-Object {{ $_.Name -like '{prefix}*' }} | ForEach-Object {{ Write-Host \"  [清理] CachePolicy: $($_.Name)\" -ForegroundColor DarkGray; Remove-DnsServerQueryResolutionPolicy -ZoneName '{cacheZone}' -Name $_.Name -Force -ErrorAction Stop }}");
        // 3) Client Subnet（此时已无策略引用）
        sb.AppendLine($"Get-DnsServerClientSubnet -ErrorAction SilentlyContinue | Where-Object {{ $_.Name -like '{prefix}*' }} | ForEach-Object {{ Write-Host \"  [清理] ClientSubnet: $($_.Name)\" -ForegroundColor DarkGray; Remove-DnsServerClientSubnet -Name $_.Name -Force -ErrorAction Stop }}");
        // 4) Recursion Scope（此时已无策略引用，可安全删除）
        sb.AppendLine($"Get-DnsServerRecursionScope -ErrorAction SilentlyContinue | Where-Object {{ $_.Name -like '{prefix}*' }} | ForEach-Object {{ Write-Host \"  [清理] RecursionScope: $($_.Name)\" -ForegroundColor DarkGray; Remove-DnsServerRecursionScope -Name $_.Name -Force -ErrorAction Stop }}");
        // 5) Cache Zone Scope
        sb.AppendLine($"Get-DnsServerZoneScope -ZoneName '{cacheZone}' -ErrorAction SilentlyContinue | Where-Object {{ $_.ZoneScope -like '{prefix}*' }} | ForEach-Object {{ Write-Host \"  [清理] CacheZoneScope: $($_.ZoneScope)\" -ForegroundColor DarkGray; Remove-DnsServerZoneScope -ZoneName '{cacheZone}' -Name $_.ZoneScope -Force -ErrorAction Stop }}");
        sb.AppendLine("Write-Host '清理完成。' -ForegroundColor Green");
        sb.AppendLine();

        // ============================================================
        // 1. 配置档：Recursion Scope + Cache Zone Scope
        // ============================================================
        sb.AppendLine("# =============================================================");
        sb.AppendLine("# 第 1 步 / 共 3 步：上游解析配置档 → Recursion Scope + Cache Zone Scope");
        sb.AppendLine("# =============================================================");
        var pIdx = 0;
        foreach (var profile in config.ResolverProfiles)
        {
            pIdx++;
            var recursionScope = ResolverProfile.BuildRecursionScopeName(prefix, profile.Id);
            var cacheScope = string.IsNullOrEmpty(profile.CacheScopeName)
                ? ResolverProfile.BuildDefaultCacheScopeName(prefix, profile.Id)
                : profile.CacheScopeName;
            // 数组参数安全写法：@('ip1','ip2')，避免逗号分隔字符串在某些环境下被解析失败
            var fwdArray = "@(" + string.Join(", ", profile.Forwarders.Select(f => $"'{f.Trim()}'")) + ")";
            var enableRec = profile.EnableRecursion ? "$true" : "$false";

            sb.AppendLine($"Write-Host '[{pIdx}/{config.ResolverProfiles.Count}] 配置档: {profile.Name}' -ForegroundColor Cyan");

            // 1a) Recursion Scope（幂等：先移除再创建）
            sb.AppendLine($"Remove-IfExists {{ Get-DnsServerRecursionScope -ErrorAction SilentlyContinue }} 'Name' '{recursionScope}' {{ Remove-DnsServerRecursionScope -Name '{recursionScope}' -Force }}");
            sb.AppendLine($"Write-Host \"  [创建] RecursionScope: {recursionScope}  转发器={fwdArray}  递归={enableRec}\" -ForegroundColor Green");
            sb.AppendLine($"Add-DnsServerRecursionScope -Name '{recursionScope}' -Forwarder {fwdArray} -EnableRecursion {enableRec} -ErrorAction Stop");

            // 1b) Cache Zone Scope（仅当配置档启用缓存隔离时创建独立 Zone Scope）
            if (profile.CacheIsolationEnabled)
            {
                // 注意：Get-DnsServerZoneScope 的名字属性叫 ZoneScope，不是 Name
                sb.AppendLine($"$cacheZoneScopes = Get-DnsServerZoneScope -ZoneName '{cacheZone}' -ErrorAction SilentlyContinue | Where-Object {{ $_.ZoneScope -eq '{cacheScope}' }}");
                sb.AppendLine($"if (-not $cacheZoneScopes) {{");
                sb.AppendLine($"    Write-Host \"  [创建] CacheZoneScope: {cacheScope}\" -ForegroundColor Green");
                sb.AppendLine($"    Add-DnsServerZoneScope -ZoneName '{cacheZone}' -Name '{cacheScope}' -ErrorAction Stop");
                sb.AppendLine($"}} else {{ Write-Host \"  [跳过] CacheZoneScope: {cacheScope} 已存在\" -ForegroundColor DarkGray }}");
            }
            sb.AppendLine();
        }

        // ============================================================
        // 2. 网段规则：Client Subnet + Cache Policy + Recursion Policy
        // ============================================================
        sb.AppendLine("# =============================================================");
        sb.AppendLine("# 第 2 步 / 共 3 步：网段规则 → Client Subnet + Cache Policy + Recursion Policy");
        sb.AppendLine("# =============================================================");
        var orderedRules = config.Rules.Where(r => r.Enabled).OrderBy(r => r.Priority).ToList();
        var rIdx = 0;
        foreach (var rule in orderedRules)
        {
            rIdx++;
            var profile = config.FindProfile(rule.ResolverProfileId);
            if (profile is null)
            {
                sb.AppendLine($"Write-Host '[{rIdx}/{orderedRules.Count}] 规则: {rule.Name} → 引用配置档不存在，跳过' -ForegroundColor Yellow");
                continue;
            }

            var subnetName = SegmentRule.BuildClientSubnetName(prefix, rule.Id);
            var cachePolicy = SegmentRule.BuildCachePolicyName(prefix, rule.Id);
            var recursionPolicy = SegmentRule.BuildRecursionPolicyName(prefix, rule.Id);
            var cacheScope = profile.CacheIsolationEnabled
                ? (string.IsNullOrEmpty(profile.CacheScopeName) ? ResolverProfile.BuildDefaultCacheScopeName(prefix, profile.Id) : profile.CacheScopeName)
                : DefaultPolicy.DefaultCacheZone;
            var recursionScope = ResolverProfile.BuildRecursionScopeName(prefix, profile.Id);

            sb.AppendLine($"Write-Host '[{rIdx}/{orderedRules.Count}] 规则: {rule.Name}  网段={rule.ClientSubnet}  →  配置档={profile.Name}' -ForegroundColor Cyan");

            // 2a) Client Subnet（幂等：先移除再创建）
            sb.AppendLine($"Remove-IfExists {{ Get-DnsServerClientSubnet -ErrorAction SilentlyContinue }} 'Name' '{subnetName}' {{ Remove-DnsServerClientSubnet -Name '{subnetName}' -Force }}");
            sb.AppendLine($"Write-Host \"  [创建] ClientSubnet: {subnetName} = {rule.ClientSubnet}\" -ForegroundColor Green");
            sb.AppendLine($"Add-DnsServerClientSubnet -Name '{subnetName}' -IPv4Subnet '{rule.ClientSubnet}' -ErrorAction Stop");

            // 2b) Cache Policy（..cache 分区下 Query Resolution Policy：客户端网段→缓存范围）
            sb.AppendLine("$existingCachePol = Get-DnsServerQueryResolutionPolicy -ZoneName '" + cacheZone + "' -ErrorAction SilentlyContinue | Where-Object { $_.Name -eq '" + cachePolicy + "' }");
            sb.AppendLine($"if ($existingCachePol) {{");
            sb.AppendLine($"    Write-Host \"  [清理] 移除已存在 CachePolicy: {cachePolicy}\" -ForegroundColor DarkGray");
            sb.AppendLine($"    Remove-DnsServerQueryResolutionPolicy -ZoneName '{cacheZone}' -Name '{cachePolicy}' -Force -ErrorAction Stop");
            sb.AppendLine("}");
            sb.AppendLine($"Write-Host \"  [创建] CachePolicy: {cachePolicy}  子网={subnetName} → 缓存范围={cacheScope}\" -ForegroundColor Green");
            sb.AppendLine($"Add-DnsServerQueryResolutionPolicy -ZoneName '{cacheZone}' -Name '{cachePolicy}' -Action ALLOW -ClientSubnet 'EQ,{subnetName}' -ZoneScope '{cacheScope},1' -ErrorAction Stop");

            // 2c) Recursion Policy（服务器级 Query Resolution Policy：缓存未命中时→对应递归范围）
            //     注意：DnsServer 模块没有 *-DnsServerRecursionPolicy cmdlet！
            //     服务器级递归策略 = Add-DnsServerQueryResolutionPolicy 不带 -ZoneName、带 -ApplyOnRecursion
            sb.AppendLine($"Remove-IfExists {{ Get-DnsServerQueryResolutionPolicy -ErrorAction SilentlyContinue }} 'Name' '{recursionPolicy}' {{ Remove-DnsServerQueryResolutionPolicy -Name '{recursionPolicy}' -Force }}");
            sb.AppendLine($"Write-Host \"  [创建] RecursionPolicy(服务器级): {recursionPolicy}  子网={subnetName} → 递归范围={recursionScope}\" -ForegroundColor Green");
            sb.AppendLine($"Add-DnsServerQueryResolutionPolicy -Name '{recursionPolicy}' -Action ALLOW -ApplyOnRecursion -ClientSubnet 'EQ,{subnetName}' -RecursionScope '{recursionScope}' -ErrorAction Stop");
            sb.AppendLine();
        }

        // ============================================================
        // 3. 默认策略：更新默认递归范围 "."
        // ============================================================
        var defaultProfile = config.FindProfile(config.DefaultPolicy.ResolverProfileId);
        if (defaultProfile is not null && config.DefaultPolicy.Enabled)
        {
            var fwdArray = "@(" + string.Join(", ", defaultProfile.Forwarders.Select(f => $"'{f.Trim()}'")) + ")";
            var enableRec = defaultProfile.EnableRecursion ? "$true" : "$false";
            var fwdJoin = string.Join(",", defaultProfile.Forwarders.Select(f => f.Trim()));
            sb.AppendLine("# =============================================================");
            sb.AppendLine("# 第 3 步 / 共 3 步：默认策略 → 更新默认递归范围 \".\"（规格第 9 节：需明确确认）");
            sb.AppendLine("# =============================================================");
            // 先读取当前默认范围的转发器，仅在实际需要修改时才执行 Set（避免无谓变更与重复警告）
            sb.AppendLine($"$currentDefault = Get-DnsServerRecursionScope -Name '.' -ErrorAction SilentlyContinue");
            sb.AppendLine($"$desiredFwd = '{fwdJoin}'.Split(',') | ForEach-Object {{ $_.Trim() }}");
            sb.AppendLine($"$currentFwd = @()");
            sb.AppendLine($"if ($currentDefault.Forwarder) {{ $currentFwd = @($currentDefault.Forwarder) }}");
            sb.AppendLine($"$needUpdate = $false");
            sb.AppendLine($"if ($currentFwd.Count -ne $desiredFwd.Count) {{ $needUpdate = $true }}");
            sb.AppendLine($"else {{ for ($i = 0; $i -lt $desiredFwd.Count; $i++) {{ if ($currentFwd[$i] -ne $desiredFwd[$i]) {{ $needUpdate = $true; break }} }} }}");
            sb.AppendLine($"if ($currentDefault.EnableRecursion -ne {enableRec}) {{ $needUpdate = $true }}");
            sb.AppendLine($"if ($needUpdate) {{");
            sb.AppendLine("    Write-Warning '即将修改默认递归范围 \".\"，这将影响所有未命中手工规则的客户端！'");
            sb.AppendLine($"    Write-Host '  默认策略: {defaultProfile.Name}  转发器={fwdArray}  递归={enableRec}' -ForegroundColor Cyan");
            sb.AppendLine($"    Set-DnsServerRecursionScope -Name '.' -Forwarder {fwdArray} -EnableRecursion {enableRec} -ErrorAction Stop");
            sb.AppendLine("    Write-Host '  [完成] 默认递归范围已更新' -ForegroundColor Green");
            sb.AppendLine("} else {");
            sb.AppendLine("    Write-Host '  [跳过] 默认递归范围 \".\" 配置与目标一致，无需修改' -ForegroundColor DarkGray");
            sb.AppendLine("}");
            sb.AppendLine();
        }
        else
        {
            sb.AppendLine("# =============================================================");
            sb.AppendLine("# 第 3 步（跳过）：默认策略未启用或未选择配置档");
            sb.AppendLine("# =============================================================");
            sb.AppendLine();
        }

        sb.AppendLine("# =============================================================");
        sb.AppendLine("# 收尾：清理缓存 + 立即验证已创建对象数量");
        sb.AppendLine("# =============================================================");
        sb.AppendLine("Write-Host '正在清理 DNS 缓存...' -ForegroundColor Gray");
        sb.AppendLine("Clear-DnsServerCache -Force -ErrorAction Stop");
        sb.AppendLine();
        sb.AppendLine("Write-Host ''");
        sb.AppendLine("Write-Host '========== 应用完成！即时验证 ==========' -ForegroundColor Green");
        sb.AppendLine("$prefix = '" + prefix + "'");
        sb.AppendLine("$cacheZone = '" + cacheZone + "'");
        sb.AppendLine("$cnt_rs = @(Get-DnsServerRecursionScope -ErrorAction SilentlyContinue | Where-Object { $_.Name -like \"$prefix*\" }).Count");
        sb.AppendLine("$cnt_cs = @(Get-DnsServerClientSubnet -ErrorAction SilentlyContinue | Where-Object { $_.Name -like \"$prefix*\" }).Count");
        sb.AppendLine("$cnt_zs = @(Get-DnsServerZoneScope -ZoneName $cacheZone -ErrorAction SilentlyContinue | Where-Object { $_.ZoneScope -like \"$prefix*\" }).Count");
        sb.AppendLine("$cnt_cp = @(Get-DnsServerQueryResolutionPolicy -ZoneName $cacheZone -ErrorAction SilentlyContinue | Where-Object { $_.Name -like \"$prefix*\" }).Count");
        sb.AppendLine("$cnt_rp = @(Get-DnsServerQueryResolutionPolicy -ErrorAction SilentlyContinue | Where-Object { $_.Name -like \"$prefix*\" }).Count");
        sb.AppendLine("Write-Host ('  RecursionScope (上游DNS配置档): ' + $cnt_rs) -ForegroundColor Cyan");
        sb.AppendLine("Write-Host ('  ClientSubnet (客户端子网):     ' + $cnt_cs) -ForegroundColor Cyan");
        sb.AppendLine("Write-Host ('  CacheZoneScope (缓存范围):    ' + $cnt_zs) -ForegroundColor Cyan");
        sb.AppendLine("Write-Host ('  CachePolicy (缓存分流策略):   ' + $cnt_cp) -ForegroundColor Cyan");
        sb.AppendLine("Write-Host ('  RecursionPolicy (递归分流策略):' + $cnt_rp) -ForegroundColor Cyan");
        sb.AppendLine("$total = $cnt_rs + $cnt_cs + $cnt_zs + $cnt_cp + $cnt_rp");
        sb.AppendLine("if ($total -eq 0) { Write-Warning '注意：5 类托管对象数量均为 0！可能未创建成功，请查看上方错误信息。' } else { Write-Host \"DnsRouteUI 配置应用成功。共创建/保留 $total 个托管对象。\" -ForegroundColor Green }");

        return sb.ToString();
    }

    public string GenerateRollbackScript()
    {
        var prefix = _options.ObjectPrefix;
        var cacheZone = _options.DefaultCacheZone;
        var sb = new StringBuilder();

        sb.AppendLine("# ============================================================");
        sb.AppendLine("# DnsRouteUI 回滚脚本：移除所有带前缀的托管对象");
        sb.AppendLine($"# 生成时间：{DateTime.Now:O}");
        sb.AppendLine("# 仅移除 DnsRouteUI_ 前缀对象，不影响手工配置（规格第 9 节）");
        sb.AppendLine("# ============================================================");
        sb.AppendLine();
        sb.AppendLine("$ErrorActionPreference = 'Stop'");
        sb.AppendLine("Import-Module DnsServer -ErrorAction Stop");
        sb.AppendLine();

        sb.AppendLine("# 移除递归策略");
        sb.AppendLine($"Get-DnsServerQueryResolutionPolicy -ErrorAction SilentlyContinue | Where-Object {{ $_.Name -like '{prefix}*' }} | ForEach-Object {{ Write-Host \"[移除] RecursionPolicy: $($_.Name)\" -ForegroundColor DarkGray; Remove-DnsServerQueryResolutionPolicy -Name $_.Name -Force -ErrorAction Stop }}");
        sb.AppendLine();
        sb.AppendLine("# 移除缓存策略");
        sb.AppendLine($"Get-DnsServerQueryResolutionPolicy -ZoneName '{cacheZone}' -ErrorAction SilentlyContinue | Where-Object {{ $_.Name -like '{prefix}*' }} | ForEach-Object {{ Write-Host \"[移除] CachePolicy: $($_.Name)\" -ForegroundColor DarkGray; Remove-DnsServerQueryResolutionPolicy -ZoneName '{cacheZone}' -Name $_.Name -Force -ErrorAction Stop }}");
        sb.AppendLine();
        sb.AppendLine("# 移除 Recursion Scope");
        sb.AppendLine($"Get-DnsServerRecursionScope -ErrorAction SilentlyContinue | Where-Object {{ $_.Name -like '{prefix}*' }} | ForEach-Object {{ Write-Host \"[移除] RecursionScope: $($_.Name)\" -ForegroundColor DarkGray; Remove-DnsServerRecursionScope -Name $_.Name -Force -ErrorAction Stop }}");
        sb.AppendLine();
        sb.AppendLine("# 移除 Cache Zone Scope");
        sb.AppendLine($"Get-DnsServerZoneScope -ZoneName '{cacheZone}' -ErrorAction SilentlyContinue | Where-Object {{ $_.ZoneScope -like '{prefix}*' }} | ForEach-Object {{ Write-Host \"[移除] CacheZoneScope: $($_.ZoneScope)\" -ForegroundColor DarkGray; Remove-DnsServerZoneScope -ZoneName '{cacheZone}' -Name $_.ZoneScope -Force -ErrorAction Stop }}");
        sb.AppendLine();
        sb.AppendLine("# 移除 Client Subnet");
        sb.AppendLine($"Get-DnsServerClientSubnet -ErrorAction SilentlyContinue | Where-Object {{ $_.Name -like '{prefix}*' }} | ForEach-Object {{ Write-Host \"[移除] ClientSubnet: $($_.Name)\" -ForegroundColor DarkGray; Remove-DnsServerClientSubnet -Name $_.Name -Force -ErrorAction Stop }}");
        sb.AppendLine();
        sb.AppendLine("Clear-DnsServerCache -Force -ErrorAction Stop");
        sb.AppendLine("Write-Host 'DnsRouteUI 托管对象已全部移除并清理缓存。' -ForegroundColor Green");

        return sb.ToString();
    }

    public string SaveScript(string script, string fileName)
    {
        Directory.CreateDirectory(_options.BackupDirectory);
        var path = Path.Combine(_options.BackupDirectory, fileName);
        File.WriteAllText(path, script);
        _logger.Info($"脚本已保存：{path}", nameof(ScriptExportService));
        return path;
    }

    private static (string ip, int bits) ParseCidr(string cidr)
    {
        var parts = cidr.Trim().Split('/');
        return (parts[0], int.Parse(parts[1]));
    }
}
