using System.IO;
using System.Text.Json;
using DnsRouteUI.Models;

namespace DnsRouteUI.Services;

/// <summary>
/// 日志服务接口（规格第 8 节：结构化 JSON 日志与可读文本日志）。
/// </summary>
public interface ILogger
{
    void Info(string message, string? context = null);

    void Warning(string message, string? context = null);

    void Error(string message, string? context = null, Exception? ex = null);

    void Debug(string message, string? context = null);
}

/// <summary>
/// 文件日志服务：同时写入结构化 JSONL 日志与可读文本日志。
/// 日志目录：C:\ProgramData\DnsRouteUI\logs\。
/// </summary>
public sealed class FileLogger : ILogger
{
    private readonly AppConfigOptions _options;
    private readonly object _sync = new();

    public FileLogger(AppConfigOptions options)
    {
        _options = options;
        Directory.CreateDirectory(_options.LogDirectory);
    }

    public void Info(string message, string? context = null) => Write("INFO", message, context, null);
    public void Warning(string message, string? context = null) => Write("WARN", message, context, null);
    public void Error(string message, string? context = null, Exception? ex = null) => Write("ERROR", message, context, ex);
    public void Debug(string message, string? context = null) => Write("DEBUG", message, context, null);

    private void Write(string level, string message, string? context, Exception? ex)
    {
        var now = DateTime.Now;
        var stamp = now.ToString("yyyy-MM-dd");
        var textPath = Path.Combine(_options.LogDirectory, $"DnsRouteUI_{stamp}.log");
        var jsonPath = Path.Combine(_options.LogDirectory, $"DnsRouteUI_{stamp}.jsonl");

        var entry = new
        {
            ts = now.ToString("o"),
            level,
            context = context ?? string.Empty,
            message,
            error = ex?.ToString()
        };

        lock (_sync)
        {
            // 可读文本日志
            var contextTag = string.IsNullOrEmpty(context) ? "" : $" [{context}]";
            var errorTag = ex is null ? "" : $"\n  Exception: {ex}";
            File.AppendAllText(textPath, $"{now:HH:mm:ss.fff} {level,-5}{contextTag} {message}{errorTag}\n");

            // 结构化 JSONL 日志
            var json = JsonSerializer.Serialize(entry);
            File.AppendAllText(jsonPath, json + "\n");
        }
    }
}
