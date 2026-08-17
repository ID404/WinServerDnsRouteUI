using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using DnsRouteUI.Models;

namespace DnsRouteUI.Services;

/// <summary>
/// 备份服务（规格第 5.5 节、第 8 节）。
/// 应用前导出 DNS 对象快照和程序配置快照。
/// 备份目录：C:\ProgramData\DnsRouteUI\backups\。
/// </summary>
public interface IBackupService
{
    /// <summary>创建应用前备份（DNS 快照 + 程序配置快照 + 应用脚本）。</summary>
    Task<BackupBundle> CreatePreApplyBackupAsync(DnsRouteConfig config, string applyScript);

    /// <summary>列出所有历史备份。</summary>
    IReadOnlyList<string> ListBackups();
}

public sealed class BackupBundle
{
    public string DirectoryPath { get; set; } = string.Empty;
    public string SnapshotPath { get; set; } = string.Empty;
    public string ConfigBackupPath { get; set; } = string.Empty;
    public string ScriptPath { get; set; } = string.Empty;
    public string CreatedAt { get; set; } = string.Empty;
}

public sealed class BackupService : IBackupService
{
    private readonly IDnsServerService _dns;
    private readonly IConfigService _config;
    private readonly AppConfigOptions _options;
    private readonly ILogger _logger;

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public BackupService(IDnsServerService dns, IConfigService config, AppConfigOptions options, ILogger logger)
    {
        _dns = dns;
        _config = config;
        _options = options;
        _logger = logger;
    }

    public async Task<BackupBundle> CreatePreApplyBackupAsync(DnsRouteConfig config, string applyScript)
    {
        var stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
        var backupDir = Path.Combine(_options.BackupDirectory, $"apply_{stamp}");
        Directory.CreateDirectory(backupDir);

        var bundle = new BackupBundle
        {
            DirectoryPath = backupDir,
            CreatedAt = DateTime.UtcNow.ToString("o")
        };

        try
        {
            // 1. DNS 对象快照
            var snapshot = await _dns.ReadFullSnapshotAsync();
            var snapshotPath = Path.Combine(backupDir, "dns_snapshot.json");
            await File.WriteAllTextAsync(snapshotPath, JsonSerializer.Serialize(snapshot, JsonOpts));
            bundle.SnapshotPath = snapshotPath;

            // 2. 程序配置快照
            var configPath = Path.Combine(backupDir, "config_backup.json");
            await File.WriteAllTextAsync(configPath, JsonSerializer.Serialize(config, JsonOpts));
            bundle.ConfigBackupPath = configPath;

            // 3. 应用脚本副本
            var scriptPath = Path.Combine(backupDir, "apply.ps1");
            await File.WriteAllTextAsync(scriptPath, applyScript);
            bundle.ScriptPath = scriptPath;

            _logger.Info($"已创建应用前备份：{backupDir}", nameof(BackupService));
        }
        catch (Exception ex)
        {
            _logger.Error("创建备份失败。", nameof(BackupService), ex);
            throw;
        }

        return bundle;
    }

    public IReadOnlyList<string> ListBackups()
    {
        if (!Directory.Exists(_options.BackupDirectory)) return Array.Empty<string>();
        return Directory.GetDirectories(_options.BackupDirectory, "apply_*")
            .OrderByDescending(d => d)
            .ToList();
    }
}
