using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using DnsRouteUI.Mvvm;
using DnsRouteUI.Services;

namespace DnsRouteUI.ViewModels;

/// <summary>请求主导航切换到指定模块的消息（如：勾选条件转发开关后自动跳转到应用页）。</summary>
public sealed record NavigateToMessage(Type ModuleType);

/// <summary>
/// 主视图模型：管理左侧导航与当前激活的功能模块。
/// 模块对应规格第 5 节：环境状态、配置档、网段策略（含默认策略）、预览应用、测试日志。
/// </summary>
public partial class MainViewModel : ViewModelBase
{
    private readonly IConfigService _config;
    private readonly ILogger _logger;

    public MainViewModel(
        IConfigService config,
        ILogger logger,
        EnvironmentViewModel environment,
        ResolverProfilesViewModel profiles,
        SegmentRulesViewModel rules,
        PreviewApplyViewModel preview,
        TestLogViewModel testLog)
    {
        _config = config;
        _logger = logger;

        DisplayName = "DnsRouteUI";

        Modules = new ObservableCollection<ViewModelBase>
        {
            environment,
            profiles,
            rules,
            preview,
            testLog
        };

        foreach (var m in Modules)
        {
            m.DisplayName = m switch
            {
                EnvironmentViewModel => "环境与服务状态",
                ResolverProfilesViewModel => "上游解析配置档",
                SegmentRulesViewModel => "网段策略",
                PreviewApplyViewModel => "变更预览与应用",
                TestLogViewModel => "测试与日志",
                _ => m.DisplayName
            };
        }

        // 首次启动加载配置
        _config.Load();
        _logger.Info("DnsRouteUI 启动。", nameof(MainViewModel));

        // 接收跨模块导航请求（如：环境页勾选条件转发后跳转到应用页）
        WeakReferenceMessenger.Default.Register<NavigateToMessage>(this, (recipient, message) =>
        {
            var target = Modules.FirstOrDefault(m => m.GetType() == message.ModuleType);
            if (target is not null)
            {
                SelectedModule = target;
            }
        });

        SelectedModule = Modules[0];
    }

    /// <summary>导航模块列表。</summary>
    public ObservableCollection<ViewModelBase> Modules { get; }

    [ObservableProperty]
    private ViewModelBase? _selectedModule;

    partial void OnSelectedModuleChanged(ViewModelBase? value)
    {
        foreach (var m in Modules) m.IsActive = ReferenceEquals(m, value);
    }

    [RelayCommand]
    private void Refresh()
    {
        _config.Load();
    }
}
