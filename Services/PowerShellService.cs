using System.Diagnostics;
using System.IO;
using DnsRouteUI.Models;

namespace DnsRouteUI.Services;

/// <summary>
/// PowerShell 执行结果。
/// </summary>
public sealed class PowerShellResult
{
    public int ExitCode { get; set; }

    public string StandardOutput { get; set; } = string.Empty;

    public string StandardError { get; set; } = string.Empty;

    public bool Success => ExitCode == 0;

    /// <summary>合并的标准输出 + 错误输出，便于诊断。</summary>
    public string CombinedOutput
    {
        get
        {
            var sb = new System.Text.StringBuilder();
            if (!string.IsNullOrEmpty(StandardOutput)) sb.Append(StandardOutput);
            if (!string.IsNullOrEmpty(StandardError))
            {
                if (sb.Length > 0) sb.AppendLine();
                sb.Append("[STDERR] ").Append(StandardError);
            }
            return sb.ToString();
        }
    }
}

/// <summary>
/// PowerShell 调用服务（规格第 8 节：调用 Windows DnsServer PowerShell 模块）。
/// 采用进程方式调用 Windows PowerShell（powershell.exe），以兼容仅 Windows PowerShell
/// 可用的 DnsServer 模块，并避免 SDK 依赖冲突。规格第 9 节：所有 PowerShell 执行错误
/// 必须显示详细信息并写入日志。
/// </summary>
public interface IPowerShellService
{
    /// <summary>执行单段 PowerShell 脚本，返回完整结果。</summary>
    PowerShellResult Execute(string script, int timeoutSeconds = 60);

    /// <summary>执行脚本并尝试解析为对象列表（基于 ConvertTo-Json 输出）。</summary>
    Task<PowerShellResult> ExecuteAsync(string script, int timeoutSeconds = 60);
}

public sealed class PowerShellService : IPowerShellService
{
    private readonly AppConfigOptions _options;
    private readonly ILogger _logger;

    public PowerShellService(AppConfigOptions options, ILogger logger)
    {
        _options = options;
        _logger = logger;
    }

    public PowerShellResult Execute(string script, int timeoutSeconds = 60)
    {
        // 写入临时 .ps1 文件再用 -File 执行，避免命令行长度限制、引号转义、换行丢失等问题
        // 这些问题在内联 -Command 模式下极易导致复杂脚本静默失败或被截断
        string? tempScriptPath = null;
        try
        {
            tempScriptPath = Path.Combine(Path.GetTempPath(), $"DnsRouteUI_{Guid.NewGuid():N}.ps1");
            // 编码注意（中文乱码的两个根源）：
            // 1) Windows PowerShell 5.1 将"无 BOM 的 UTF-8"脚本按 ANSI(中文系统=GBK) 解析 → 必须写入带 BOM 的 UTF-8；
            // 2) powershell.exe 重定向输出默认使用 OEM 代码页 → 在脚本开头强制 [Console]::OutputEncoding=UTF8，
            //    与本进程 StandardOutputEncoding/ErrorEncoding 的 UTF-8 解码保持一致。
            var encodingPreamble =
                "try { [Console]::OutputEncoding = [System.Text.Encoding]::UTF8 } catch { }" + Environment.NewLine;
            File.WriteAllText(tempScriptPath, encodingPreamble + script, new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: true));

            var psi = new ProcessStartInfo
            {
                FileName = _options.PowerShellExe,
                // -NoProfile 避免加载用户配置文件拖慢启动；
                // -NonInteractive 防止挂起等待输入；
                // -ExecutionPolicy Bypass 允许本次脚本执行；
                // -File 执行临时脚本文件（比内联 -Command 可靠得多）
                Arguments = $"-NoProfile -NonInteractive -ExecutionPolicy Bypass -File \"{tempScriptPath}\"",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                StandardOutputEncoding = System.Text.Encoding.UTF8,
                StandardErrorEncoding = System.Text.Encoding.UTF8
            };

            using var p = Process.Start(psi);
            if (p is null)
            {
                _logger.Error("无法启动 PowerShell 进程。", nameof(PowerShellService));
                return new PowerShellResult { ExitCode = -1, StandardError = "无法启动 PowerShell 进程。" };
            }

            var stdoutTask = p.StandardOutput.ReadToEndAsync();
            var stderrTask = p.StandardError.ReadToEndAsync();
            var exited = p.WaitForExit(timeoutSeconds * 1000);

            if (!exited)
            {
                try { p.Kill(true); } catch { /* 忽略 */ }
                _logger.Error($"PowerShell 执行超时（{timeoutSeconds}s），已终止。脚本文件：{tempScriptPath}", nameof(PowerShellService));
                return new PowerShellResult { ExitCode = -2, StandardError = $"执行超时（{timeoutSeconds}秒）。脚本已保留：{tempScriptPath}" };
            }

            var result = new PowerShellResult
            {
                ExitCode = p.ExitCode,
                StandardOutput = stdoutTask.Result,
                StandardError = stderrTask.Result
            };

            if (!result.Success)
            {
                _logger.Error($"PowerShell 退出码 {result.ExitCode}。脚本文件：{tempScriptPath}\n输出：{result.CombinedOutput}", nameof(PowerShellService));
                // 失败时保留脚本文件便于排查
                tempScriptPath = null;
            }
            else
            {
                _logger.Info($"PowerShell 执行成功（exit 0），脚本长度 {script.Length} 字符。", nameof(PowerShellService));
            }

            return result;
        }
        catch (Exception ex)
        {
            _logger.Error($"PowerShell 调用异常。脚本文件：{tempScriptPath}", nameof(PowerShellService), ex);
            return new PowerShellResult { ExitCode = -3, StandardError = ex.ToString() };
        }
        finally
        {
            // 成功时清理临时文件；失败时保留以辅助诊断
            if (tempScriptPath is not null && File.Exists(tempScriptPath))
            {
                try { File.Delete(tempScriptPath); } catch { /* 忽略 */ }
            }
        }
    }

    public async Task<PowerShellResult> ExecuteAsync(string script, int timeoutSeconds = 60)
    {
        return await Task.Run(() => Execute(script, timeoutSeconds));
    }
}
