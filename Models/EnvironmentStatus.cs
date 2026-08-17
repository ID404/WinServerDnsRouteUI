namespace DnsRouteUI.Models;

/// <summary>
/// 环境与服务状态（规格第 5.1 节）。
/// 启动时检查管理员权限、DNS 服务状态、DnsServer PowerShell 模块可用性。
/// </summary>
public sealed class EnvironmentStatus
{
    /// <summary>当前 DNS Server 名称（计算机名）。</summary>
    public string DnsServerName { get; set; } = string.Empty;

    /// <summary>DNS Server 服务是否正在运行。</summary>
    public bool DnsServiceRunning { get; set; }

    /// <summary>当前运行账户是否具有管理员权限。</summary>
    public bool IsAdministrator { get; set; }

    /// <summary>DnsServer PowerShell 模块是否可用。</summary>
    public bool DnsServerModuleAvailable { get; set; }

    /// <summary>当前手工规则数。</summary>
    public int RuleCount { get; set; }

    /// <summary>当前配置档数。</summary>
    public int ProfileCount { get; set; }

    /// <summary>默认策略是否已配置有效配置档。</summary>
    public bool DefaultPolicyConfigured { get; set; }

    /// <summary>
    /// DNS Server 当前是否已存在本程序管理的递归策略（带 DnsRouteUI_ 前缀）。
    /// 用于首次运行时判断是否已开启按条件转发。
    /// </summary>
    public bool ConditionalForwardingActiveOnServer { get; set; }

    /// <summary>程序配置中的条件转发开关状态。</summary>
    public bool ConditionalForwardingEnabled { get; set; }

    /// <summary>最近一次应用操作时间。</summary>
    public string? LastAppliedAt { get; set; }

    /// <summary>最近一次应用操作结果。</summary>
    public string? LastApplyResult { get; set; }

    /// <summary>检测过程中产生的错误信息。</summary>
    public string? ErrorMessage { get; set; }

    /// <summary>整体环境是否就绪（管理员 + 服务运行 + 模块可用）。</summary>
    public bool IsReady => IsAdministrator && DnsServiceRunning && DnsServerModuleAvailable;
}
