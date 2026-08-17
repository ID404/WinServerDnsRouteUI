using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using DnsRouteUI.Models;
using DnsRouteUI.Mvvm;
using DnsRouteUI.Services;

namespace DnsRouteUI.ViewModels;

/// <summary>
/// 环境与服务状态视图模型（规格第 5.1 节）。
/// </summary>
public partial class EnvironmentViewModel : ViewModelBase
{
    private readonly IEnvironmentService _env;
    private readonly IConfigService _config;
    private readonly AppConfigOptions _options;
    private readonly ILogger _logger;

    public EnvironmentViewModel(IEnvironmentService env, IConfigService config, AppConfigOptions options, ILogger logger)
    {
        _env = env;
        _config = config;
        _options = options;
        _logger = logger;
    }

    [ObservableProperty]
    private EnvironmentStatus _status = new();

    [ObservableProperty]
    private bool _isChecking;

    [ObservableProperty]
    private string _diagnosticsPath = string.Empty;

    /// <summary>
    /// 条件转发开关（双向绑定到配置）。
    /// 开启时应用递归分流策略；关闭时 DNS Server 回退默认递归行为。
    /// </summary>
    [ObservableProperty]
    private bool _conditionalForwardingEnabled = true;

    public ObservableCollection<string> LogLines { get; } = new();

    protected override void OnActivated()
    {
        if (string.IsNullOrEmpty(Status.DnsServerName)) CheckCommand.Execute(null);
    }

    [RelayCommand]
    private async Task CheckAsync()
    {
        IsChecking = true;
        try
        {
            var status = await Task.Run(() => _env.Check());
            Status = status;
            ConditionalForwardingEnabled = status.ConditionalForwardingEnabled;
            var cfState = status.ConditionalForwardingEnabled ? "开启" : "关闭";
            var serverState = status.ConditionalForwardingActiveOnServer ? "已激活" : "未激活";
            LogLines.Insert(0, $"[{DateTime.Now:HH:mm:ss}] 环境检测完成：就绪={status.IsReady}，条件转发={cfState}(Server:{serverState})");
        }
        catch (Exception ex)
        {
            _logger.Error("环境检测失败。", nameof(EnvironmentViewModel), ex);
            LogLines.Insert(0, $"[{DateTime.Now:HH:mm:ss}] 检测失败：{ex.Message}");
        }
        finally
        {
            IsChecking = false;
        }
    }

    /// <summary>
    /// 切换条件转发开关。保存到配置文件。
    /// 注意：仅修改配置，不会立即生效到 DNS Server，需在"变更预览与应用"中应用。
    /// </summary>
    [RelayCommand]
    private void ToggleConditionalForwarding()
    {
        var cfg = _config.Current;
        var newState = ConditionalForwardingEnabled;
        if (cfg.ConditionalForwardingEnabled == newState)
        {
            // 开关状态已是目标值（可能是 UI 绑定自动触发），无需重复保存
            return;
        }

        cfg.ConditionalForwardingEnabled = newState;
        _config.Save();
        Status.ConditionalForwardingEnabled = newState;

        var stateText = newState ? "开启" : "关闭";
        var hint = newState
            ? "已开启条件转发。请在\"变更预览与应用\"中应用配置以使其生效。"
            : "已关闭条件转发。下次应用时将不生成递归策略；如需彻底清理已应用的策略，请导出回滚脚本执行。";
        LogLines.Insert(0, $"[{DateTime.Now:HH:mm:ss}] 条件转发开关：{stateText}。{hint}");
        _logger.Info($"条件转发开关切换为：{stateText}", nameof(EnvironmentViewModel));

        // 开关仅保存配置，不会自动创建 DNS 策略对象。
        // 为避免用户误以为勾选即生效，此处弹窗说明并自动跳转到"变更预览与应用"页引导完成应用。
        MessageBox.Show(
            $"条件转发开关已{stateText}。\n\n" +
            "注意：开关只保存配置，不会立即修改 DNS Server。\n" +
            "必须在\"变更预览与应用\"页面点击【应用】按钮，才会真正创建递归范围、客户端子网和分流策略等 DNS 对象\n" +
            "（之后 Get-DnsServerRecursionScope / Get-DnsServerQueryResolutionPolicy 等命令才能查询到）。\n\n" +
            "点击\"确定\"后将自动跳转到\"变更预览与应用\"页面。",
            "条件转发开关", MessageBoxButton.OK, MessageBoxImage.Information);

        WeakReferenceMessenger.Default.Send(new NavigateToMessage(typeof(PreviewApplyViewModel)));
    }

    partial void OnConditionalForwardingEnabledChanged(bool value)
    {
        // UI CheckBox 变化时触发保存（仅在已初始化后）
        if (!string.IsNullOrEmpty(Status.DnsServerName))
        {
            ToggleConditionalForwardingCommand.Execute(null);
        }
    }

    [RelayCommand]
    private async Task ExportDiagnosticsAsync()
    {
        try
        {
            // 导出到软件目录下的 logs 文件夹，便于随软件一起分发和排查
            var path = await Task.Run(() => _env.ExportDiagnostics(_options.AppLogDirectory));
            DiagnosticsPath = path;
            LogLines.Insert(0, $"[{DateTime.Now:HH:mm:ss}] 诊断信息已导出：{path}");
            MessageBox.Show($"诊断信息已导出：\n{path}", "导出完成", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            _logger.Error("导出诊断信息失败。", nameof(EnvironmentViewModel), ex);
            MessageBox.Show($"导出诊断信息失败：{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    [RelayCommand]
    private void ReadConfig()
    {
        _config.Load();
        var cfg = _config.Current;
        Status.RuleCount = cfg.Rules.Count;
        Status.ProfileCount = cfg.ResolverProfiles.Count;
        Status.DefaultPolicyConfigured = !string.IsNullOrEmpty(cfg.DefaultPolicy.ResolverProfileId);
        Status.LastAppliedAt = cfg.LastAppliedAt;
        Status.LastApplyResult = cfg.LastApplyResult;
        Status.ConditionalForwardingEnabled = cfg.ConditionalForwardingEnabled;
        ConditionalForwardingEnabled = cfg.ConditionalForwardingEnabled;
        LogLines.Insert(0, $"[{DateTime.Now:HH:mm:ss}] 已读取配置：{cfg.ResolverProfiles.Count} 配置档 / {cfg.Rules.Count} 规则 / 条件转发={(cfg.ConditionalForwardingEnabled ? "开" : "关")}");
    }
}
