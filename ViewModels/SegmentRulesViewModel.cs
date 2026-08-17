using System.Collections.ObjectModel;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DnsRouteUI.Models;
using DnsRouteUI.Mvvm;
using DnsRouteUI.Services;

namespace DnsRouteUI.ViewModels;

/// <summary>
/// 网段策略视图模型（规格第 5.3、5.4 节）。
/// 默认策略始终显示在规则列表最后一行，不可删除、不可移动。
/// </summary>
public partial class SegmentRulesViewModel : ViewModelBase
{
    private readonly IConfigService _config;
    private readonly IValidationService _validation;
    private readonly AppConfigOptions _options;
    private readonly ILogger _logger;

    public SegmentRulesViewModel(IConfigService config, IValidationService validation, AppConfigOptions options, ILogger logger)
    {
        _config = config;
        _validation = validation;
        _options = options;
        _logger = logger;
    }

    /// <summary>规则列表（不含默认策略，默认策略单独绑定）。</summary>
    public ObservableCollection<SegmentRule> Rules { get; } = new();

    /// <summary>配置档下拉选项。</summary>
    public ObservableCollection<ResolverProfile> AvailableProfiles { get; } = new();

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveRuleCommand))]
    [NotifyCanExecuteChangedFor(nameof(DeleteRuleCommand))]
    [NotifyCanExecuteChangedFor(nameof(MoveUpCommand))]
    [NotifyCanExecuteChangedFor(nameof(MoveDownCommand))]
    [NotifyCanExecuteChangedFor(nameof(EditRuleCommand))]
    [NotifyCanExecuteChangedFor(nameof(NewRuleCommand))]
    private SegmentRule? _selectedRule;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveRuleCommand))]
    [NotifyCanExecuteChangedFor(nameof(DeleteRuleCommand))]
    [NotifyCanExecuteChangedFor(nameof(MoveUpCommand))]
    [NotifyCanExecuteChangedFor(nameof(MoveDownCommand))]
    [NotifyCanExecuteChangedFor(nameof(EditRuleCommand))]
    [NotifyCanExecuteChangedFor(nameof(NewRuleCommand))]
    private bool _isEditing;

    [ObservableProperty]
    private bool _isNew;

    [ObservableProperty]
    private string _validationMessages = string.Empty;

    // 默认策略编辑
    [ObservableProperty]
    private DefaultPolicy _defaultPolicy = new();

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveDefaultCommand))]
    private ResolverProfile? _defaultSelectedProfile;

    private SegmentRule? _originalForCancel;

    protected override void OnActivated()
    {
        Reload();
    }

    public void Reload()
    {
        Rules.Clear();
        AvailableProfiles.Clear();
        foreach (var r in _config.Current.Rules.OrderBy(r => r.Priority))
        {
            // 填充配置档名称用于表格展示（避免用户需点击详细查看）
            var profile = _config.Current.FindProfile(r.ResolverProfileId);
            r.ResolverProfileName = profile?.Name ?? "(未知配置档)";
            Rules.Add(r);
        }
        foreach (var p in _config.Current.ResolverProfiles) AvailableProfiles.Add(p);

        DefaultPolicy = _config.Current.DefaultPolicy;
        DefaultSelectedProfile = _config.Current.FindProfile(DefaultPolicy.ResolverProfileId);
        ValidationMessages = string.Empty;
    }

    [RelayCommand]
    private void NewRule()
    {
        var id = Guid.NewGuid().ToString("N").Substring(0, 8);
        var nextPriority = (Rules.Count == 0 ? 1 : Rules.Max(r => r.Priority) + 1);
        SelectedRule = new SegmentRule
        {
            Id = id,
            Name = "新建规则",
            ClientSubnet = "192.168.0.0/24",
            ResolverProfileId = AvailableProfiles.FirstOrDefault()?.Id ?? string.Empty,
            Priority = nextPriority,
            Enabled = true,
            Note = ""
        };
        IsNew = true;
        IsEditing = true;
        _originalForCancel = null;
    }

    [RelayCommand]
    private void EditRule()
    {
        if (SelectedRule is null) return;
        _originalForCancel = SelectedRule;
        SelectedRule = Clone(SelectedRule);
        IsNew = false;
        IsEditing = true;
    }

    [RelayCommand]
    private void CancelEdit()
    {
        IsEditing = false;
        IsNew = false;
        if (_originalForCancel is not null)
        {
            SelectedRule = _originalForCancel;
            _originalForCancel = null;
        }
    }

    [RelayCommand(CanExecute = nameof(CanSaveRule))]
    private void SaveRule()
    {
        if (SelectedRule is null) return;

        ValidationMessages = string.Empty;

        // 校验 CIDR
        if (!_validation.IsValidIPv4Cidr(SelectedRule.ClientSubnet))
        {
            ValidationMessages = $"网段“{SelectedRule.ClientSubnet}”不是有效的 IPv4 CIDR。";
            return;
        }

        // 校验名称唯一
        var nameCheck = _validation.ValidateRuleNameUnique(SelectedRule.Name, _config.Current.Rules, IsNew ? null : SelectedRule.Id);
        if (!nameCheck.IsValid)
        {
            ValidationMessages = string.Join("\n", nameCheck.Errors);
            return;
        }

        // 校验配置档存在
        if (_config.Current.FindProfile(SelectedRule.ResolverProfileId) is null)
        {
            ValidationMessages = "请选择有效的上游解析配置档。";
            return;
        }

        var config = _config.Current;
        if (IsNew)
        {
            config.Rules.Add(SelectedRule);
        }
        else if (_originalForCancel is not null)
        {
            var idx = config.Rules.IndexOf(_originalForCancel);
            if (idx >= 0) config.Rules[idx] = SelectedRule;
        }

        // 保存日志所需信息（Reload 会清空集合，导致 SelectedRule 被绑定置为 null）
        var savedId = SelectedRule.Id;
        var savedName = SelectedRule.Name;

        // 重新编号优先级（按当前列表顺序）
        ReindexPriorities(config);
        _config.Save();

        // 先退出编辑模式，再 Reload，避免集合清空时 DataGrid 绑定副作用
        IsEditing = false;
        IsNew = false;
        _originalForCancel = null;
        Reload();

        // 重新选中刚保存的项
        SelectedRule = Rules.FirstOrDefault(r => r.Id == savedId);

        // 网段重叠警告
        var overlap = _validation.ValidateSubnetOverlaps(config.Rules);
        if (overlap.Warnings.Count > 0)
        {
            ValidationMessages = "保存成功，但存在以下警告：\n" + string.Join("\n", overlap.Warnings);
        }
        _logger.Info($"保存规则：{savedName}", nameof(SegmentRulesViewModel));
    }

    private bool CanSaveRule() => SelectedRule is not null && IsEditing;

    [RelayCommand(CanExecute = nameof(CanDeleteRule))]
    private void DeleteRule()
    {
        if (SelectedRule is null) return;
        if (MessageBox.Show($"确认删除规则“{SelectedRule.Name}”？", "确认", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
            return;

        var name = SelectedRule.Name;
        _config.Current.Rules.Remove(SelectedRule);
        ReindexPriorities(_config.Current);
        _config.Save();
        Reload();
        SelectedRule = null;
        _logger.Info($"删除规则：{name}", nameof(SegmentRulesViewModel));
    }

    private bool CanDeleteRule() => SelectedRule is not null && !IsEditing;

    [RelayCommand(CanExecute = nameof(CanMove))]
    private void MoveUp()
    {
        if (SelectedRule is null) return;
        var ruleId = SelectedRule.Id; // Reload 会清空集合导致 SelectedRule 变 null，必须先缓存
        var list = _config.Current.Rules.OrderBy(r => r.Priority).ToList();
        // 用 ID 查找代替引用比较，避免 _config.Current 被重新 Load 后引用不一致导致 IndexOf 返回 -1
        var idx = list.FindIndex(r => r.Id == ruleId);
        if (idx <= 0) return;
        (list[idx].Priority, list[idx - 1].Priority) = (list[idx - 1].Priority, list[idx].Priority);
        ReindexPriorities(_config.Current); // 确保 Priority 连续（1,2,3...）
        _config.Save();
        Reload();
        SelectedRule = Rules.FirstOrDefault(r => r.Id == ruleId);
    }

    [RelayCommand(CanExecute = nameof(CanMove))]
    private void MoveDown()
    {
        if (SelectedRule is null) return;
        var ruleId = SelectedRule.Id;
        var list = _config.Current.Rules.OrderBy(r => r.Priority).ToList();
        var idx = list.FindIndex(r => r.Id == ruleId);
        if (idx < 0 || idx >= list.Count - 1) return;
        (list[idx].Priority, list[idx + 1].Priority) = (list[idx + 1].Priority, list[idx].Priority);
        ReindexPriorities(_config.Current);
        _config.Save();
        Reload();
        SelectedRule = Rules.FirstOrDefault(r => r.Id == ruleId);
    }

    private bool CanMove() => SelectedRule is not null && !IsEditing;

    [RelayCommand]
    private void ToggleEnabled()
    {
        if (SelectedRule is null) return;
        // Reload 会清空集合导致 SelectedRule 变 null，必须先缓存 ID 与名称
        var ruleId = SelectedRule.Id;
        var ruleName = SelectedRule.Name;
        var newState = !SelectedRule.Enabled;
        SelectedRule.Enabled = newState;
        _config.Save();
        Reload();
        // 用缓存的 ID 重新选中该规则，避免 SelectedRule 为 null
        SelectedRule = Rules.FirstOrDefault(r => r.Id == ruleId);
        _logger.Info($"规则“{ruleName}”已{(newState ? "启用" : "禁用")}", nameof(SegmentRulesViewModel));
    }

    [RelayCommand(CanExecute = nameof(CanSaveDefault))]
    private void SaveDefault()
    {
        if (DefaultSelectedProfile is null)
        {
            MessageBox.Show("默认策略必须选择一个有效配置档（不允许空的默认策略）。", "错误", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        _config.Current.DefaultPolicy.ResolverProfileId = DefaultSelectedProfile.Id;
        _config.Save();
        DefaultPolicy = _config.Current.DefaultPolicy;
        _logger.Info($"默认策略已更新为配置档：{DefaultSelectedProfile.Name}", nameof(SegmentRulesViewModel));
        MessageBox.Show("默认策略已保存。", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private bool CanSaveDefault() => DefaultSelectedProfile is not null;

    private static void ReindexPriorities(DnsRouteConfig config)
    {
        var ordered = config.Rules.OrderBy(r => r.Priority).ToList();
        for (var i = 0; i < ordered.Count; i++) ordered[i].Priority = i + 1;
    }

    private static SegmentRule Clone(SegmentRule r) => new()
    {
        Id = r.Id,
        Name = r.Name,
        ClientSubnet = r.ClientSubnet,
        ResolverProfileId = r.ResolverProfileId,
        Priority = r.Priority,
        Enabled = r.Enabled,
        Note = r.Note
    };
}
