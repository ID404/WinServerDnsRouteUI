namespace DnsRouteUI.Models;

/// <summary>
/// 应用操作结果（规格第 5.1、5.5 节）。
/// 记录每次“创建备份并应用”的执行情况，用于状态栏展示与日志记录。
/// </summary>
public sealed class ApplicationResult
{
    /// <summary>是否成功。</summary>
    public bool Success { get; set; }

    /// <summary>开始时间（UTC ISO 8601）。</summary>
    public string StartedAt { get; set; } = string.Empty;

    /// <summary>结束时间（UTC ISO 8601）。</summary>
    public string FinishedAt { get; set; } = string.Empty;

    /// <summary>生成的 PowerShell 脚本路径（备份）。</summary>
    public string? ScriptPath { get; set; }

    /// <summary>DNS 对象快照备份路径。</summary>
    public string? SnapshotPath { get; set; }

    /// <summary>程序配置快照备份路径。</summary>
    public string? ConfigBackupPath { get; set; }

    /// <summary>执行的变更项数。</summary>
    public int ChangeCount { get; set; }

    /// <summary>错误详情（若有）。</summary>
    public string? Error { get; set; }

    /// <summary>摘要文本。</summary>
    public string Summary => Success
        ? $"成功应用 {ChangeCount} 项变更（{StartedAt}）"
        : $"应用失败：{Error}";
}
