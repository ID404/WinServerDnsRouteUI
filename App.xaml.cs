using System.Windows;
using System.Windows.Threading;
using DnsRouteUI.Mvvm;
using DnsRouteUI.ViewModels;

namespace DnsRouteUI;

/// <summary>
/// 应用入口。初始化依赖注入容器，显示主窗口。
/// </summary>
public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // 注册全局未处理异常处理器，避免触发系统崩溃报告（CrashSender.exe）
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnDomainUnhandledException;
        System.Threading.Tasks.TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;

        // 初始化服务容器（含 appsettings.json 读取、服务注册、配置加载）
        ServiceLocator.Initialize();

        // 注册全局对话框服务
        DialogServiceRegistry.DialogService = new DialogService();

        var mainVm = ServiceLocator.GetService<MainViewModel>();
        var window = new MainWindow { DataContext = mainVm };
        window.Show();
    }

    /// <summary>
    /// UI 线程未处理异常：捕获后弹窗提示，阻止崩溃报告程序启动。
    /// </summary>
    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        try
        {
            ServiceLocator.GetService<Services.ILogger>()?.Error("UI 线程未处理异常。", "App", e.Exception);
            MessageBox.Show($"发生未处理异常：\n{e.Exception.Message}\n\n详情已写入日志。", "错误",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
        catch
        {
            // 日志服务也可能异常，兜底用 MessageBox
            MessageBox.Show($"发生未处理异常：\n{e.Exception?.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        e.Handled = true; // 标记已处理，阻止进程崩溃
    }

    /// <summary>非 UI 线程 / 域级未处理异常：记录日志后无法阻止进程退出。</summary>
    private void OnDomainUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        try
        {
            var ex = e.ExceptionObject as Exception;
            ServiceLocator.GetService<Services.ILogger>()?.Error("AppDomain 未处理异常。", "App", ex);
        }
        catch { /* 忽略 */ }
    }

    /// <summary>未观察的 Task 异常：记录日志。</summary>
    private void OnUnobservedTaskException(object? sender, System.Threading.Tasks.UnobservedTaskExceptionEventArgs e)
    {
        try
        {
            ServiceLocator.GetService<Services.ILogger>()?.Error("未观察的 Task 异常。", "App", e.Exception);
        }
        catch { /* 忽略 */ }
        e.SetObserved();
    }
}

/// <summary>全局对话框服务注册表（供 ViewModel 在非 DI 场景使用）。</summary>
internal static class DialogServiceRegistry
{
    public static IDialogService? DialogService { get; set; }
}
