using System.Diagnostics;
using System.IO;
using DnsRouteUI.Models;

namespace DnsRouteUI.Services;

/// <summary>
/// 环境与服务状态检测服务（规格第 5.1 节、第 9 节）。
/// 启动时检查：管理员权限、DNS Server 服务状态、DnsServer PowerShell 模块可用性。
/// </summary>
public interface IEnvironmentService
{
    /// <summary>检测当前环境状态。</summary>
    EnvironmentStatus Check();

    /// <summary>仅刷新 DNS Server 服务状态。</summary>
    bool IsDnsServiceRunning();

    /// <summary>
    /// 检测 DNS Server 当前是否已存在本程序管理的递归策略（带 DnsRouteUI_ 前缀）。
    /// 用于首次运行时判断是否已开启按条件转发。
    /// </summary>
    bool IsConditionalForwardingActiveOnServer();

    /// <summary>导出诊断信息到指定路径。</summary>
    string ExportDiagnostics(string targetDirectory);
}

public sealed class EnvironmentService : IEnvironmentService
{
    private readonly IPowerShellService _ps;
    private readonly IConfigService _config;
    private readonly AppConfigOptions _options;
    private readonly ILogger _logger;

    public EnvironmentService(IPowerShellService ps, IConfigService config, AppConfigOptions options, ILogger logger)
    {
        _ps = ps;
        _config = config;
        _options = options;
        _logger = logger;
    }

    public EnvironmentStatus Check()
    {
        var status = new EnvironmentStatus();
        try
        {
            status.IsAdministrator = IsAdministrator();
            status.DnsServerName = Environment.MachineName;
            status.DnsServiceRunning = IsDnsServiceRunning();
            status.DnsServerModuleAvailable = IsDnsServerModuleAvailable();

            var cfg = _config.Current;
            status.RuleCount = cfg.Rules.Count;
            status.ProfileCount = cfg.ResolverProfiles.Count;
            status.DefaultPolicyConfigured = !string.IsNullOrEmpty(cfg.DefaultPolicy.ResolverProfileId)
                                             && cfg.FindProfile(cfg.DefaultPolicy.ResolverProfileId) is not null;
            status.LastAppliedAt = cfg.LastAppliedAt;
            status.LastApplyResult = cfg.LastApplyResult;

            // 检测 DNS Server 当前是否已存在本程序管理的递归策略
            status.ConditionalForwardingActiveOnServer = IsConditionalForwardingActiveOnServer();
            status.ConditionalForwardingEnabled = cfg.ConditionalForwardingEnabled;

            // 首次运行同步：若程序配置中未明确记录过应用状态，且 DNS Server 上已有策略，
            // 则将配置开关同步为开启；反之若从未应用过（无策略且无 LastAppliedAt），保持默认开启。
            if (string.IsNullOrEmpty(cfg.LastAppliedAt) && status.ConditionalForwardingActiveOnServer && !cfg.ConditionalForwardingEnabled)
            {
                cfg.ConditionalForwardingEnabled = true;
                _config.Save();
                status.ConditionalForwardingEnabled = true;
                _logger.Info("首次运行检测到 DNS Server 已存在本程序管理的策略，已自动同步条件转发开关为开启。", nameof(EnvironmentService));
            }

            _logger.Info($"环境检测完成：管理员={status.IsAdministrator}, DNS服务={status.DnsServiceRunning}, 模块={status.DnsServerModuleAvailable}, 条件转发={status.ConditionalForwardingEnabled}(Server:{status.ConditionalForwardingActiveOnServer})",
                nameof(EnvironmentService));
        }
        catch (Exception ex)
        {
            status.ErrorMessage = ex.Message;
            _logger.Error("环境检测异常。", nameof(EnvironmentService), ex);
        }
        return status;
    }

    public bool IsDnsServiceRunning()
    {
        // 通过 PowerShell Get-Service 检查 DNS 服务状态（避免引入 ServiceController NuGet 依赖）
        var result = _ps.Execute("Get-Service -Name DNS -ErrorAction SilentlyContinue | Select-Object -ExpandProperty Status");
        return result.Success && result.StandardOutput.Trim().Equals("Running", StringComparison.OrdinalIgnoreCase);
    }

    public bool IsConditionalForwardingActiveOnServer()
    {
        // 检测 DNS Server 是否已存在带 DnsRouteUI_ 前缀的递归范围。
        // 递归范围是条件转发的核心对象：只要存在 DnsRouteUI_Resolver_* 即认为已激活。
        // 不再依赖 Get-DnsServerQueryResolutionPolicy（服务器级策略在某些版本下查询行为不稳定）。
        var prefix = _options.ObjectPrefix;
        var script = $@"
$ErrorActionPreference = 'SilentlyContinue'
$scopes = @(Get-DnsServerRecursionScope | Where-Object {{ $_.Name -like '{prefix}*' }})
if ($scopes.Count -gt 0) {{ 'ACTIVE' }} else {{ 'INACTIVE' }}
";
        var result = _ps.Execute(script, 20);
        var active = result.Success && result.StandardOutput.Trim().Equals("ACTIVE", StringComparison.OrdinalIgnoreCase);
        _logger.Info($"条件转发激活检测：{(active ? "已激活" : "未激活")}（exit={result.ExitCode}）", nameof(EnvironmentService));
        return active;
    }

    /// <summary>当前账户是否在管理员组中（通过 token elevation 检测）。</summary>
    private static bool IsAdministrator()
    {
        try
        {
            using var identity = System.Security.Principal.WindowsIdentity.GetCurrent();
            var principal = new System.Security.Principal.WindowsPrincipal(identity);
            return principal.IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
        }
        catch
        {
            return false;
        }
    }

    private bool IsDnsServerModuleAvailable()
    {
        // DnsServer 模块仅在 Windows Server 上可用，需 RSAT 或 DNS 角色安装。
        var result = _ps.Execute("Get-Module -ListAvailable -Name DnsServer | Select-Object -First 1 -ExpandProperty Name");
        return result.Success && result.StandardOutput.Trim().Equals("DnsServer", StringComparison.OrdinalIgnoreCase);
    }

    public string ExportDiagnostics(string targetDirectory)
    {
        Directory.CreateDirectory(targetDirectory);
        var stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
        var path = Path.Combine(targetDirectory, $"DnsRouteUI_Diagnostics_{stamp}.txt");

        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"DnsRouteUI 诊断信息导出 - {DateTime.Now:O}");
        sb.AppendLine(new string('=', 60));
        sb.AppendLine();

        // ===== 1. 环境与配置摘要 =====
        sb.AppendLine("[1] 环境与配置摘要");
        sb.AppendLine(new string('-', 60));
        var status = Check();
        sb.AppendLine($"计算机名: {status.DnsServerName}");
        sb.AppendLine($"管理员权限: {status.IsAdministrator}");
        sb.AppendLine($"DNS 服务运行: {status.DnsServiceRunning}");
        sb.AppendLine($"DnsServer 模块可用: {status.DnsServerModuleAvailable}");
        sb.AppendLine($"环境就绪: {status.IsReady}");
        sb.AppendLine($"配置档数: {status.ProfileCount}");
        sb.AppendLine($"规则数: {status.RuleCount}");
        sb.AppendLine($"默认策略已配置: {status.DefaultPolicyConfigured}");
        sb.AppendLine($"条件转发开关(配置): {status.ConditionalForwardingEnabled}");
        sb.AppendLine($"条件转发已激活(Server): {status.ConditionalForwardingActiveOnServer}");
        sb.AppendLine($"最近应用时间: {status.LastAppliedAt ?? "无"}");
        sb.AppendLine($"最近应用结果: {status.LastApplyResult ?? "无"}");
        if (!string.IsNullOrEmpty(status.ErrorMessage))
        {
            sb.AppendLine($"检测错误: {status.ErrorMessage}");
        }
        sb.AppendLine();

        // ===== 2. 软件运行环境信息 =====
        sb.AppendLine("[2] 软件运行环境");
        sb.AppendLine(new string('-', 60));
        sb.AppendLine($"软件目录: {AppContext.BaseDirectory}");
        sb.AppendLine($"配置文件路径: {_options.ConfigFilePath}");
        sb.AppendLine($"日志目录: {_options.LogDirectory}");
        sb.AppendLine($"诊断导出目录: {targetDirectory}");
        sb.AppendLine($"对象前缀: {_options.ObjectPrefix}");
        sb.AppendLine($"默认缓存分区: {_options.DefaultCacheZone}");
        sb.AppendLine($".NET 运行时: {Environment.Version}");
        sb.AppendLine($"操作系统: {Environment.OSVersion}");
        sb.AppendLine($"64 位进程: {Environment.Is64BitProcess}");
        sb.AppendLine();

        // ===== 3. 当前配置内容（JSON）=====
        sb.AppendLine("[3] 当前配置内容（config.json）");
        sb.AppendLine(new string('-', 60));
        try
        {
            var cfg = _config.Current;
            sb.AppendLine($"配置版本: {cfg.Version}");
            sb.AppendLine($"缓存隔离模式: {cfg.CacheIsolation}");
            sb.AppendLine($"条件转发开关: {cfg.ConditionalForwardingEnabled}");
            sb.AppendLine($"最近应用时间: {cfg.LastAppliedAt ?? "(无)"}");
            sb.AppendLine($"最近应用结果: {cfg.LastApplyResult ?? "(无)"}");
            sb.AppendLine();
            sb.AppendLine($"配置档列表（{cfg.ResolverProfiles.Count} 个）:");
            foreach (var p in cfg.ResolverProfiles)
            {
                sb.AppendLine($"  - [{p.Id}] {p.Name}");
                sb.AppendLine($"      转发器: {string.Join(", ", p.Forwarders)}");
                sb.AppendLine($"      递归: {p.EnableRecursion}, 缓存隔离: {p.CacheIsolationEnabled}, 缓存范围: {p.CacheScopeName}");
            }
            sb.AppendLine();
            sb.AppendLine($"规则列表（{cfg.Rules.Count} 条）:");
            foreach (var r in cfg.Rules.OrderBy(x => x.Priority))
            {
                var profile = cfg.FindProfile(r.ResolverProfileId);
                sb.AppendLine($"  - [{r.Priority}] {r.Name} (ID={r.Id}) 网段={r.ClientSubnet} → {profile?.Name ?? "(未知)"} 启用={r.Enabled}");
            }
            sb.AppendLine();
            sb.AppendLine($"默认策略: 配置档={cfg.DefaultPolicy.ResolverProfileId}, 启用={cfg.DefaultPolicy.Enabled}");
        }
        catch (Exception ex)
        {
            sb.AppendLine($"读取配置失败: {ex.Message}");
        }
        sb.AppendLine();

        // ===== 4. DNS Server 概况 =====
        sb.AppendLine("[4] DNS Server 概况");
        sb.AppendLine(new string('-', 60));
        var dnsInfo = _ps.Execute("Get-DnsServerSetting | Select-Object -Property ComputerName,EnableDnsSec,ListenAddresses | ConvertTo-Json");
        sb.AppendLine(dnsInfo.CombinedOutput);
        sb.AppendLine();

        // ===== 5. DNS Server 托管对象实际查询（关键诊断）=====
        // 这是判断"应用是否真正生效"的最直接证据
        sb.AppendLine("[5] DNS Server 托管对象实际查询（DnsRouteUI_ 前缀）");
        sb.AppendLine(new string('-', 60));
        var prefix = _options.ObjectPrefix;
        var cacheZone = _options.DefaultCacheZone;
        var queryScript = $@"
$ErrorActionPreference = 'SilentlyContinue'
$prefix = '{prefix}'
$cacheZone = '{cacheZone}'

Write-Host '--- Recursion Scope（递归范围 / 上游转发器组） ---'
$rs = @(Get-DnsServerRecursionScope | Where-Object {{ $_.Name -like ""$prefix*"" }})
Write-Host ('数量: ' + $rs.Count)
if ($rs.Count -gt 0) {{ $rs | Format-Table Name, Forwarder, EnableRecursion -AutoSize | Out-String }}
Write-Host ''

Write-Host '--- Client Subnet（客户端子网） ---'
$cs = @(Get-DnsServerClientSubnet | Where-Object {{ $_.Name -like ""$prefix*"" }})
Write-Host ('数量: ' + $cs.Count)
if ($cs.Count -gt 0) {{ $cs | Format-Table Name, IPv4Subnet -AutoSize | Out-String }}
Write-Host ''

Write-Host '--- Cache Zone Scope（缓存范围 / ..cache 分区） ---'
$zs = @(Get-DnsServerZoneScope -ZoneName $cacheZone | Where-Object {{ $_.ZoneScope -like ""$prefix*"" }})
Write-Host ('数量: ' + $zs.Count)
if ($zs.Count -gt 0) {{ $zs | Format-Table ZoneScope -AutoSize | Out-String }}
Write-Host ''

Write-Host '--- 服务器级 Query Resolution Policy（递归分流策略） ---'
$rp = @(Get-DnsServerQueryResolutionPolicy | Where-Object {{ $_.Name -like ""$prefix*"" }})
Write-Host ('数量: ' + $rp.Count)
if ($rp.Count -gt 0) {{ $rp | Format-List Name, Action, ClientSubnet, RecursionScope | Out-String }}
Write-Host ''

Write-Host '--- ..cache 分区级 Query Resolution Policy（缓存分流策略） ---'
$cp = @(Get-DnsServerQueryResolutionPolicy -ZoneName $cacheZone | Where-Object {{ $_.Name -like ""$prefix*"" }})
Write-Host ('数量: ' + $cp.Count)
if ($cp.Count -gt 0) {{ $cp | Format-Table Name, Action, ClientSubnet, ZoneScope -AutoSize | Out-String }}
";
        var queryResult = _ps.Execute(queryScript, 30);
        sb.AppendLine(queryResult.CombinedOutput);
        sb.AppendLine();

        // ===== 6. 默认递归范围 "." 状态 =====
        sb.AppendLine("[6] 默认递归范围 \".\" 状态");
        sb.AppendLine(new string('-', 60));
        var defaultScope = _ps.Execute("Get-DnsServerRecursionScope -Name '.' | Select-Object Name, Forwarder, EnableRecursion | ConvertTo-Json");
        sb.AppendLine(defaultScope.CombinedOutput);
        sb.AppendLine();

        // ===== 7. 最近日志（尾部 50 行）=====
        sb.AppendLine("[7] 最近运行日志（尾部）");
        sb.AppendLine(new string('-', 60));
        try
        {
            var today = DateTime.Now.ToString("yyyy-MM-dd");
            var logPath = Path.Combine(_options.LogDirectory, $"DnsRouteUI_{today}.log");
            if (File.Exists(logPath))
            {
                var lines = File.ReadAllLines(logPath);
                var tail = lines.Length > 50 ? lines.Skip(lines.Length - 50).ToArray() : lines;
                sb.AppendLine($"日志文件: {logPath}");
                sb.AppendLine($"总行数: {lines.Length}，显示最后 {tail.Length} 行:");
                sb.AppendLine();
                foreach (var line in tail) sb.AppendLine(line);
            }
            else
            {
                sb.AppendLine($"今日日志文件不存在: {logPath}");
            }
        }
        catch (Exception ex)
        {
            sb.AppendLine($"读取日志失败: {ex.Message}");
        }
        sb.AppendLine();

        sb.AppendLine(new string('=', 60));
        sb.AppendLine($"诊断信息导出完成 - {DateTime.Now:O}");

        File.WriteAllText(path, sb.ToString(), System.Text.Encoding.UTF8);
        _logger.Info($"诊断信息已导出：{path}", nameof(EnvironmentService));
        return path;
    }
}
