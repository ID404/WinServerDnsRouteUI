using CommunityToolkit.Mvvm.ComponentModel;

namespace DnsRouteUI.Mvvm;

/// <summary>
/// 所有视图模型基类。基于 CommunityToolkit.Mvvm 的 ObservableObject，
/// 提供导航标题与 IsActive 生命周期钩子。
/// </summary>
public abstract class ViewModelBase : ObservableObject
{
    private string _displayName = string.Empty;

    /// <summary>导航/Tab 显示名称。</summary>
    public string DisplayName
    {
        get => _displayName;
        set => SetProperty(ref _displayName, value);
    }

    private bool _isActive;

    /// <summary>当前视图是否处于激活状态（被选中）。</summary>
    public bool IsActive
    {
        get => _isActive;
        set
        {
            if (SetProperty(ref _isActive, value))
            {
                if (value) OnActivated();
                else OnDeactivated();
            }
        }
    }

    /// <summary>视图激活时调用，子类可重写以加载数据。</summary>
    protected virtual void OnActivated()
    {
    }

    /// <summary>视图失活时调用，子类可重写以释放资源。</summary>
    protected virtual void OnDeactivated()
    {
    }
}
