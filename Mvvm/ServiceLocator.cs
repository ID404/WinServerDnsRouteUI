using System.IO;
using System.Text.Json;
using DnsRouteUI.Models;
using Microsoft.Extensions.DependencyInjection;

namespace DnsRouteUI.Mvvm;

/// <summary>
/// 服务定位器 / 依赖注入容器。
/// 在 App 启动时注册所有服务与视图模型，全局通过 <see cref="GetService"/> 解析。
/// </summary>
public static class ServiceLocator
{
    private static IServiceProvider? _provider;

    public static IServiceProvider Provider =>
        _provider ?? throw new InvalidOperationException("ServiceLocator 尚未初始化，请先调用 Initialize。");

    public static bool IsInitialized => _provider is not null;

    /// <summary>初始化并构建服务容器。</summary>
    public static IServiceProvider Initialize()
    {
        if (_provider is not null) return _provider;

        var services = new ServiceCollection();

        // 应用配置：从 appsettings.json 加载
        var options = LoadAppConfig();
        services.AddSingleton(options);

        // 服务层（接口 + 实现）
        services.AddSingleton<Services.ILogger, Services.FileLogger>();
        services.AddSingleton<Services.IConfigService, Services.ConfigService>();
        services.AddSingleton<Services.IEnvironmentService, Services.EnvironmentService>();
        services.AddSingleton<Services.IPowerShellService, Services.PowerShellService>();
        services.AddSingleton<Services.IDnsServerService, Services.DnsServerService>();
        services.AddSingleton<Services.IValidationService, Services.ValidationService>();
        services.AddSingleton<Services.IChangePreviewService, Services.ChangePreviewService>();
        services.AddSingleton<Services.IScriptExportService, Services.ScriptExportService>();
        services.AddSingleton<Services.IBackupService, Services.BackupService>();

        // 视图模型
        services.AddSingleton<ViewModels.MainViewModel>();
        services.AddTransient<ViewModels.EnvironmentViewModel>();
        services.AddTransient<ViewModels.ResolverProfilesViewModel>();
        services.AddTransient<ViewModels.SegmentRulesViewModel>();
        services.AddTransient<ViewModels.PreviewApplyViewModel>();
        services.AddTransient<ViewModels.TestLogViewModel>();

        _provider = services.BuildServiceProvider();
        return _provider;
    }

    public static T GetService<T>() where T : notnull => Provider.GetRequiredService<T>();

    /// <summary>从 appsettings.json 读取 AppConfig 段。</summary>
    private static AppConfigOptions LoadAppConfig()
    {
        var basePath = AppContext.BaseDirectory;
        var settingsPath = Path.Combine(basePath, "appsettings.json");
        if (!File.Exists(settingsPath))
            return new AppConfigOptions();

        try
        {
            var json = File.ReadAllText(settingsPath);
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("AppConfig", out var section))
            {
                return JsonSerializer.Deserialize<AppConfigOptions>(section.GetRawText())
                       ?? new AppConfigOptions();
            }
        }
        catch
        {
            // 配置读取失败时回退默认值，不阻断启动。
        }

        return new AppConfigOptions();
    }
}
