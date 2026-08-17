using System.Collections.ObjectModel;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DnsRouteUI.Models;
using DnsRouteUI.Mvvm;
using DnsRouteUI.Services;

namespace DnsRouteUI.ViewModels;

/// <summary>
/// 上游解析配置档视图模型（规格第 5.2 节）。
/// </summary>
public partial class ResolverProfilesViewModel : ViewModelBase
{
    private readonly IConfigService _config;
    private readonly IValidationService _validation;
    private readonly IDnsServerService _dns;
    private readonly AppConfigOptions _options;
    private readonly ILogger _logger;

    public ResolverProfilesViewModel(IConfigService config, IValidationService validation, IDnsServerService dns, AppConfigOptions options, ILogger logger)
    {
        _config = config;
        _validation = validation;
        _dns = dns;
        _options = options;
        _logger = logger;
    }

    public ObservableCollection<ResolverProfile> Profiles { get; } = new();

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    [NotifyCanExecuteChangedFor(nameof(DeleteCommand))]
    [NotifyCanExecuteChangedFor(nameof(TestConnectivityCommand))]
    private ResolverProfile? _selectedProfile;

    [ObservableProperty]
    private string _forwardersText = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    [NotifyCanExecuteChangedFor(nameof(DeleteCommand))]
    [NotifyCanExecuteChangedFor(nameof(EditCommand))]
    private bool _isEditing;

    [ObservableProperty]
    private bool _isNew;

    [ObservableProperty]
    private string _referenceCount = string.Empty;

    private ResolverProfile? _originalForCancel;

    protected override void OnActivated()
    {
        Reload();
    }

    public void Reload()
    {
        Profiles.Clear();
        foreach (var p in _config.Current.ResolverProfiles) Profiles.Add(p);
    }

    [RelayCommand]
    private void New()
    {
        var id = Guid.NewGuid().ToString("N").Substring(0, 8);
        SelectedProfile = new ResolverProfile
        {
            Id = id,
            Name = "新建配置档",
            Forwarders = new List<string>(),
            EnableRecursion = true,
            CacheIsolationEnabled = true,
            CacheScopeName = ResolverProfile.BuildDefaultCacheScopeName(_options.ObjectPrefix, id),
            Note = ""
        };
        ForwardersText = string.Empty;
        IsNew = true;
        IsEditing = true;
        _originalForCancel = null;
    }

    [RelayCommand]
    private void Edit()
    {
        if (SelectedProfile is null) return;
        // 工作副本，取消时还原
        _originalForCancel = SelectedProfile;
        var copy = Clone(SelectedProfile);
        SelectedProfile = copy;
        ForwardersText = string.Join(Environment.NewLine, copy.Forwarders);
        IsNew = false;
        IsEditing = true;
        UpdateReferenceCount(copy.Id);
    }

    [RelayCommand]
    private void Cancel()
    {
        IsEditing = false;
        IsNew = false;
        ForwardersText = string.Empty;
        if (_originalForCancel is not null)
        {
            SelectedProfile = _originalForCancel;
            _originalForCancel = null;
        }
    }

    [RelayCommand(CanExecute = nameof(CanSave))]
    private void Save()
    {
        if (SelectedProfile is null) return;

        // 解析转发器文本
        SelectedProfile.Forwarders = ForwardersText
            .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(s => s.Trim())
            .Where(s => !string.IsNullOrEmpty(s))
            .ToList();

        // 校验
        var nameCheck = _validation.ValidateProfileNameUnique(SelectedProfile.Name, _config.Current.ResolverProfiles, IsNew ? null : SelectedProfile.Id);
        var fwdCheck = _validation.ValidateForwarders(SelectedProfile.Forwarders);
        var errors = nameCheck.Errors.Concat(fwdCheck.Errors).ToList();
        if (errors.Count > 0)
        {
            MessageBox.Show(string.Join("\n", errors), "校验失败", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (string.IsNullOrWhiteSpace(SelectedProfile.CacheScopeName))
        {
            SelectedProfile.CacheScopeName = ResolverProfile.BuildDefaultCacheScopeName(_options.ObjectPrefix, SelectedProfile.Id);
        }

        // 保存日志所需信息（Reload 会清空集合，导致 SelectedProfile 被绑定置为 null）
        var savedId = SelectedProfile.Id;
        var savedName = SelectedProfile.Name;

        var config = _config.Current;
        if (IsNew)
        {
            config.ResolverProfiles.Add(SelectedProfile);
        }
        else if (_originalForCancel is not null)
        {
            var idx = config.ResolverProfiles.IndexOf(_originalForCancel);
            if (idx >= 0) config.ResolverProfiles[idx] = SelectedProfile;
        }

        _config.Save();

        // 先退出编辑模式，再 Reload，避免集合清空时 ListBox 绑定副作用
        IsEditing = false;
        IsNew = false;
        _originalForCancel = null;
        Reload();

        // 重新选中刚保存的项
        SelectedProfile = Profiles.FirstOrDefault(p => p.Id == savedId);
        _logger.Info($"保存配置档：{savedName}", nameof(ResolverProfilesViewModel));
    }

    private bool CanSave() => SelectedProfile is not null && IsEditing;

    [RelayCommand(CanExecute = nameof(CanDelete))]
    private void Delete()
    {
        if (SelectedProfile is null) return;
        var refs = _config.Current.CountProfileReferences(SelectedProfile.Id);
        if (refs > 0)
        {
            MessageBox.Show($"该配置档被 {refs} 条策略引用，无法删除。\n请先移除或更换引用该配置档的策略。",
                "不可删除", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        if (MessageBox.Show($"确认删除配置档“{SelectedProfile.Name}”？", "确认", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
            return;

        var deletedName = SelectedProfile.Name;
        _config.Current.ResolverProfiles.Remove(SelectedProfile);
        _config.Save();
        Reload();
        SelectedProfile = null;
        _logger.Info($"删除配置档：{deletedName}", nameof(ResolverProfilesViewModel));
    }

    private bool CanDelete() => SelectedProfile is not null && !IsEditing;

    [RelayCommand(CanExecute = nameof(CanTest))]
    private async Task TestConnectivityAsync()
    {
        if (SelectedProfile is null) return;
        foreach (var fwd in SelectedProfile.Forwarders)
        {
            var ok = await _dns.TestForwarderConnectivityAsync(fwd);
            _logger.Info($"连通性测试 {fwd}：{(ok ? "成功" : "失败")}", nameof(ResolverProfilesViewModel));
            MessageBox.Show($"{fwd}：{(ok ? "可达" : "不可达")}", "连通性测试", MessageBoxButton.OK,
                ok ? MessageBoxImage.Information : MessageBoxImage.Warning);
        }
    }

    private bool CanTest() => SelectedProfile is not null && SelectedProfile.Forwarders.Count > 0;

    partial void OnSelectedProfileChanged(ResolverProfile? value)
    {
        if (value is not null && !IsEditing) UpdateReferenceCount(value.Id);
    }

    private void UpdateReferenceCount(string id)
    {
        var n = _config.Current.CountProfileReferences(id);
        ReferenceCount = $"被 {n} 条策略引用";
    }

    private static ResolverProfile Clone(ResolverProfile p) => new()
    {
        Id = p.Id,
        Name = p.Name,
        Forwarders = new List<string>(p.Forwarders),
        EnableRecursion = p.EnableRecursion,
        CacheIsolationEnabled = p.CacheIsolationEnabled,
        CacheScopeName = p.CacheScopeName,
        Note = p.Note
    };
}
