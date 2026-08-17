namespace DnsRouteUI.Models;

/// <summary>
/// 应用配置选项（对应 appsettings.json 的 AppConfig 段）。
/// 集中管理路径、前缀、PowerShell 可执行文件等可调参数。
/// </summary>
public sealed class AppConfigOptions
{
    public string ConfigDirectory { get; set; } = @"C:\ProgramData\DnsRouteUI";

    public string ConfigFileName { get; set; } = "config.json";

    public string BackupDirectoryName { get; set; } = "backups";

    public string LogDirectoryName { get; set; } = "logs";

    public string ObjectPrefix { get; set; } = "DnsRouteUI_";

    public string PowerShellExe { get; set; } = "powershell.exe";

    public string DefaultCacheZone { get; set; } = "..cache";

    public string DefaultRecursionScope { get; set; } = ".";

    public string ConfigFilePath => System.IO.Path.Combine(ConfigDirectory, ConfigFileName);

    public string BackupDirectory => System.IO.Path.Combine(ConfigDirectory, BackupDirectoryName);

    /// <summary>程序配置目录下的日志目录（结构化日志+文本日志，长期持久化）。</summary>
    public string LogDirectory => System.IO.Path.Combine(ConfigDirectory, LogDirectoryName);

    /// <summary>
    /// 软件目录（EXE 所在目录）下的 logs 文件夹，用于诊断信息导出等运行时产物。
    /// 单文件自包含发布时，AppContext.BaseDirectory 指向 EXE 实际路径（非临时解压目录）。
    /// </summary>
    public string AppLogDirectory => System.IO.Path.Combine(AppContext.BaseDirectory, LogDirectoryName);
}
