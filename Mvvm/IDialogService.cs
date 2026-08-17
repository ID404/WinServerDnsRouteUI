using System.Windows;

namespace DnsRouteUI.Mvvm;

/// <summary>
/// 对话框服务接口，让 ViewModel 可弹出消息框而不直接依赖 UI 框架。
/// </summary>
public interface IDialogService
{
    void ShowInfo(string message, string title = "提示");

    void ShowWarning(string message, string title = "警告");

    void ShowError(string message, string title = "错误");

    bool Confirm(string message, string title = "确认");

    bool ConfirmDanger(string message, string title = "危险操作");
}

/// <summary>基于 WPF MessageBox 的默认实现。</summary>
public sealed class DialogService : IDialogService
{
    public void ShowInfo(string message, string title = "提示") =>
        MessageBox.Show(message, title, MessageBoxButton.OK, MessageBoxImage.Information);

    public void ShowWarning(string message, string title = "警告") =>
        MessageBox.Show(message, title, MessageBoxButton.OK, MessageBoxImage.Warning);

    public void ShowError(string message, string title = "错误") =>
        MessageBox.Show(message, title, MessageBoxButton.OK, MessageBoxImage.Error);

    public bool Confirm(string message, string title = "确认") =>
        MessageBox.Show(message, title, MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes;

    public bool ConfirmDanger(string message, string title = "危险操作") =>
        MessageBox.Show(message, title, MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes;
}
