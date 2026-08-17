using System.Collections.ObjectModel;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DnsRouteUI.Models;
using DnsRouteUI.Mvvm;
using DnsRouteUI.Services;

namespace DnsRouteUI.ViewModels;

/// <summary>
/// 变更预览与应用视图模型（规格第 5.5 节、第 9 节）。
/// 应用前展示差异、导出 PowerShell 脚本、创建备份并应用。
/// </summary>
public partial class PreviewApplyViewModel : ViewModelBase
{
    private readonly IConfigService _config;
    private readonly IDnsServerService _dns;
    private readonly IChangePreviewService _preview;
    private readonly IScriptExportService _script;
    private readonly IBackupService _backup;
    private readonly IValidationService _validation;
    private readonly AppConfigOptions _options;
    private readonly ILogger _logger;

    public PreviewApplyViewModel(
        IConfigService config,
        IDnsServerService dns,
        IChangePreviewService preview,
        IScriptExportService script,
        IBackupService backup,
        IValidationService validation,
        AppConfigOptions options,
        ILogger logger)
    {
        _config = config;
        _dns = dns;
        _preview = preview;
        _script = script;
        _backup = backup;
        _validation = validation;
        _options = options;
        _logger = logger;
    }

    public ObservableCollection<ChangePreviewItem> PreviewItems { get; } = new();

    [ObservableProperty]
    private string _previewText = string.Empty;

    [ObservableProperty]
    private string _generatedScript = string.Empty;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private string _statusMessage = "尚未生成预览。";

    [ObservableProperty]
    private ApplicationResult? _lastResult;

    /// <summary>
    /// 最近一次 PowerShell 执行的合并输出（stdout+stderr），便于用户在 UI 上直接诊断。
    /// </summary>
    [ObservableProperty]
    private string _lastExecutionOutput = string.Empty;

    /// <summary>
    /// 当前 DNS Server 上本程序托管对象的查询结果文本。
    /// 用户可通过"查询当前托管对象"按钮刷新此内容。
    /// </summary>
    [ObservableProperty]
    private string _currentManagedObjectsText = "尚未查询。";

    /// <summary>
    /// 用户可直接复制到 PowerShell 执行的诊断命令参考文本。
    /// </summary>
    public string DiagnosticCommands { get; } = @"# ===== DNS Server 诊断命令参考 =====
# 1. 查询所有递归范围（含本程序创建的 DnsRouteUI_ 前缀对象）
Get-DnsServerRecursionScope | Format-Table Name, Forwarder, EnableRecursion -AutoSize

# 2. 查询所有服务器级分流策略（网段→上游 DNS 的递归分流规则；服务器级策略不带 -ZoneName）
Get-DnsServerQueryResolutionPolicy | Format-List Name, Action, ClientSubnet, RecursionScope, ProcessingOrder

# 3. 查询 ..cache 分区下的缓存分流策略（客户端网段→缓存范围）
Get-DnsServerQueryResolutionPolicy -ZoneName '..cache' | Format-Table Name, Action, ClientSubnet, ZoneScope -AutoSize

# 4. 查询所有客户端子网对象
Get-DnsServerClientSubnet | Format-Table Name, IPv4Subnet -AutoSize

# 5. 查询 ..cache 分区下的缓存范围
Get-DnsServerZoneScope -ZoneName '..cache' | Format-Table ZoneScope -AutoSize

# 6. 综合查询（仅看 DnsRouteUI_ 前缀的托管对象）
$prefix = 'DnsRouteUI_'
Write-Host '--- Recursion Scopes ---'
Get-DnsServerRecursionScope | Where-Object { $_.Name -like ""$prefix*"" } | Format-Table Name, Forwarder, EnableRecursion -AutoSize
Write-Host '--- Server Policies (递归分流策略) ---'
Get-DnsServerQueryResolutionPolicy | Where-Object { $_.Name -like ""$prefix*"" } | Format-List Name, Action, ClientSubnet, RecursionScope, ProcessingOrder
Write-Host '--- Cache Policies (..cache) ---'
Get-DnsServerQueryResolutionPolicy -ZoneName '..cache' | Where-Object { $_.Name -like ""$prefix*"" } | Format-Table Name, Action, ClientSubnet, ZoneScope -AutoSize
Write-Host '--- Client Subnets ---'
Get-DnsServerClientSubnet | Where-Object { $_.Name -like ""$prefix*"" } | Format-Table Name, IPv4Subnet -AutoSize
";

    protected override void OnActivated()
    {
        // 每次进入应用页都重新生成预览，确保最新配置（新增/修改的规则）被纳入脚本
        GeneratePreviewCommand.Execute(null);
    }

    [RelayCommand]
    private async Task GeneratePreviewAsync()
    {
        IsBusy = true;
        StatusMessage = "正在读取 DNS Server 当前对象...";
        try
        {
            var snapshot = await _dns.ReadManagedSnapshotAsync();
            var items = _preview.BuildPreview(_config.Current, snapshot);
            PreviewItems.Clear();
            foreach (var i in items) PreviewItems.Add(i);
            PreviewText = _preview.FormatPreviewText(items);
            GeneratedScript = _script.GenerateApplyScript(_config.Current);
            StatusMessage = $"已生成预览：{items.Count} 项变更（{items.Count(i => i.IsDefaultScopeChange)} 项涉及默认范围）。";
        }
        catch (Exception ex)
        {
            _logger.Error("生成预览失败。", nameof(PreviewApplyViewModel), ex);
            StatusMessage = $"生成预览失败：{ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private void ExportScript()
    {
        if (string.IsNullOrEmpty(GeneratedScript))
        {
            GeneratedScript = _script.GenerateApplyScript(_config.Current);
        }
        var stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
        var path = _script.SaveScript(GeneratedScript, $"DnsRouteUI_Apply_{stamp}.ps1");
        StatusMessage = $"脚本已导出：{path}";
        _logger.Info($"导出应用脚本：{path}", nameof(PreviewApplyViewModel));
        MessageBox.Show($"脚本已导出：\n{path}\n\n可用于生产环境审批后手动执行。", "导出完成", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    [RelayCommand]
    private void ExportRollbackScript()
    {
        var script = _script.GenerateRollbackScript();
        var stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
        var path = _script.SaveScript(script, $"DnsRouteUI_Rollback_{stamp}.ps1");
        StatusMessage = $"回滚脚本已导出：{path}";
        MessageBox.Show($"回滚脚本已导出：\n{path}", "导出完成", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    [RelayCommand]
    private async Task ApplyAsync()
    {
        // 关键：每次应用前必须从当前配置重新生成预览和脚本，
        // 避免使用缓存的旧脚本（其中可能不包含新增/修改的规则）
        await GeneratePreviewAsync();
        if (PreviewItems.Count == 0)
        {
            MessageBox.Show("无法生成变更预览（可能无法读取 DNS Server 状态），请先在\"环境与服务状态\"页检查环境。", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        // 确保脚本从当前配置生成（GeneratePreviewAsync 已生成，但这里二次确认）
        GeneratedScript = _script.GenerateApplyScript(_config.Current);

        // 校验配置
        var check = _validation.ValidateConfig(_config.Current);
        if (!check.IsValid)
        {
            MessageBox.Show("配置存在错误，无法应用：\n" + string.Join("\n", check.Errors), "校验失败", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        // 确认应用：脚本内部已对默认范围做条件判断（值匹配时自动跳过），此处仅做通用确认
        var hasDefaultScope = PreviewItems.Any(i => i.IsDefaultScopeChange);
        var confirmMsg = hasDefaultScope
            ? $"确认应用 {PreviewItems.Count} 项变更？\n\n（包含默认递归范围配置；脚本会自动检测，仅在配置不同时才修改默认范围 \".\"）\n应用前将自动创建备份。"
            : $"确认应用 {PreviewItems.Count} 项变更？\n应用前将自动创建备份。";
        if (MessageBox.Show(confirmMsg, "应用确认", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
            return;

        IsBusy = true;
        StatusMessage = "正在创建应用前备份...";
        var result = new ApplicationResult
        {
            StartedAt = DateTime.UtcNow.ToString("o"),
            ChangeCount = PreviewItems.Count
        };

        try
        {
            // 1. 备份
            var bundle = await _backup.CreatePreApplyBackupAsync(_config.Current, GeneratedScript);
            result.SnapshotPath = bundle.SnapshotPath;
            result.ConfigBackupPath = bundle.ConfigBackupPath;
            result.ScriptPath = bundle.ScriptPath;

            // 2. 执行脚本
            StatusMessage = "正在执行应用脚本...";
            var psResult = await _dns.ExecuteApplyScriptAsync(GeneratedScript);
            LastExecutionOutput = psResult.CombinedOutput;
            if (!psResult.Success)
            {
                result.Success = false;
                result.Error = psResult.CombinedOutput;
                StatusMessage = $"应用失败：{psResult.StandardError}";
                MessageBox.Show($"应用失败（exit {psResult.ExitCode}）：\n{psResult.CombinedOutput}\n\n详细信息已写入\"执行输出\"面板。",
                    "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            else
            {
                result.Success = true;
                StatusMessage = "应用成功。正在清理缓存并刷新当前对象...";
                // 3. 清理缓存
                await _dns.ClearCacheScopeAsync(_options.DefaultCacheZone);
                // 4. 应用后立即刷新快照展示，用户可直接看到结果
                await QueryCurrentManagedObjectsAsync(showSuccessToast: false);
            }
        }
        catch (Exception ex)
        {
            result.Success = false;
            result.Error = ex.Message;
            StatusMessage = $"应用异常：{ex.Message}";
            LastExecutionOutput = ex.ToString();
            _logger.Error("应用过程异常。", nameof(PreviewApplyViewModel), ex);
        }
        finally
        {
            result.FinishedAt = DateTime.UtcNow.ToString("o");
            LastResult = result;
            _config.RecordApplyResult(result);
            _logger.Info($"应用结果：{result.Summary}", nameof(PreviewApplyViewModel));
            IsBusy = false;
        }
    }

    /// <summary>
    /// 一键查询当前 DNS Server 上本程序管理的所有对象，并输出到文本框。
    /// 这是最直接的应用后验证方式，可替代手动逐条跑命令。
    /// </summary>
    [RelayCommand]
    private async Task QueryCurrentManagedObjectsAsync(bool showSuccessToast = true)
    {
        IsBusy = true;
        StatusMessage = "正在查询 DNS Server 当前托管对象...";
        try
        {
            var prefix = _options.ObjectPrefix;
            var cacheZone = _options.DefaultCacheZone;
            var script = $@"
$ErrorActionPreference = 'SilentlyContinue'
$prefix = '{prefix}'
$cacheZone = '{cacheZone}'

Write-Host '========== 当前 DNS Server 托管对象查询结果 ==========' -ForegroundColor Cyan
Write-Host ''

Write-Host '--- [1/5] Recursion Scope（递归范围 / 上游转发器组） ---' -ForegroundColor Yellow
$rs = @(Get-DnsServerRecursionScope | Where-Object {{ $_.Name -like ""$prefix*"" }})
if ($rs.Count -eq 0) {{ Write-Host '(空)' }} else {{ $rs | Format-Table Name, Forwarder, EnableRecursion -AutoSize }}
Write-Host ''

Write-Host '--- [2/5] Client Subnet（客户端子网） ---' -ForegroundColor Yellow
$cs = @(Get-DnsServerClientSubnet | Where-Object {{ $_.Name -like ""$prefix*"" }})
if ($cs.Count -eq 0) {{ Write-Host '(空)' }} else {{ $cs | Format-Table Name, IPv4Subnet -AutoSize }}
Write-Host ''

Write-Host '--- [3/5] Cache Zone Scope（缓存范围 / ..cache 分区） ---' -ForegroundColor Yellow
$zs = @(Get-DnsServerZoneScope -ZoneName $cacheZone | Where-Object {{ $_.ZoneScope -like ""$prefix*"" }})
if ($zs.Count -eq 0) {{ Write-Host '(空)' }} else {{ $zs | Format-Table ZoneScope -AutoSize }}
Write-Host ''

Write-Host '--- [4/5] Cache Policy（..cache 分区下的缓存分流策略） ---' -ForegroundColor Yellow
$cp = @(Get-DnsServerQueryResolutionPolicy -ZoneName $cacheZone | Where-Object {{ $_.Name -like ""$prefix*"" }})
if ($cp.Count -eq 0) {{ Write-Host '(空)' }} else {{ $cp | Format-Table Name, Action, ClientSubnet, ZoneScope -AutoSize }}
Write-Host ''

Write-Host '--- [5/5] Recursion Policy（服务器级递归分流策略） ---' -ForegroundColor Yellow
$rp = @(Get-DnsServerQueryResolutionPolicy | Where-Object {{ $_.Name -like ""$prefix*"" }})
if ($rp.Count -eq 0) {{ Write-Host '(空)' }} else {{ $rp | Format-List Name, Action, ClientSubnet, RecursionScope, ProcessingOrder }}
Write-Host ''

Write-Host '========== 完成 ==========' -ForegroundColor Cyan
Write-Host ('托管对象总数：RecursionScope=' + $rs.Count + ', ClientSubnet=' + $cs.Count + ', CacheZoneScope=' + $zs.Count + ', CachePolicy=' + $cp.Count + ', RecursionPolicy=' + $rp.Count)
";
            var r = await _dns.ExecuteApplyScriptAsync(script);
            var header = $"查询时间：{DateTime.Now:yyyy-MM-dd HH:mm:ss}  (exit {r.ExitCode})\n\n";
            CurrentManagedObjectsText = header + (string.IsNullOrEmpty(r.CombinedOutput) ? "(无输出)" : r.CombinedOutput);
            StatusMessage = "查询完成，详情见\"当前托管对象\"面板。";
            if (showSuccessToast)
            {
                _logger.Info("查询当前托管对象完成。", nameof(PreviewApplyViewModel));
            }
        }
        catch (Exception ex)
        {
            CurrentManagedObjectsText = "查询异常：\n" + ex;
            StatusMessage = $"查询异常：{ex.Message}";
            _logger.Error("查询当前托管对象异常。", nameof(PreviewApplyViewModel), ex);
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>将诊断命令参考文本复制到剪贴板。</summary>
    [RelayCommand]
    private void CopyDiagnosticCommands()
    {
        try
        {
            Clipboard.SetText(DiagnosticCommands);
            StatusMessage = "诊断命令已复制到剪贴板，可在 PowerShell (管理员) 中粘贴执行。";
        }
        catch (Exception ex)
        {
            MessageBox.Show($"复制剪贴板失败：{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
}
