using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using DnsRouteUI.Models;

namespace DnsRouteUI.Services;

/// <summary>
/// 配置持久化服务（规格第 8 节：配置持久化 JSON，本地配置目录 C:\ProgramData\DnsRouteUI\）。
/// 负责 DnsRouteConfig 的加载、保存与默认初始化。
/// </summary>
public interface IConfigService
{
    /// <summary>当前内存中的配置（单例）。</summary>
    DnsRouteConfig Current { get; }

    /// <summary>配置文件是否存在。</summary>
    bool Exists { get; }

    /// <summary>从磁盘加载配置；不存在则返回默认配置。</summary>
    DnsRouteConfig Load();

    /// <summary>保存当前配置到磁盘。</summary>
    void Save();

    /// <summary>保存指定配置到磁盘（不影响 Current）。</summary>
    void Save(DnsRouteConfig config);

    /// <summary>记录最近一次应用操作结果并持久化。</summary>
    void RecordApplyResult(ApplicationResult result);

    /// <summary>用示例数据初始化一份默认配置（仅首次启动或手动重置时使用）。</summary>
    DnsRouteConfig CreateDefault();
}

public sealed class ConfigService : IConfigService
{
    private readonly AppConfigOptions _options;
    private readonly ILogger _logger;
    private readonly object _saveSync = new();
    private DnsRouteConfig _current;

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public ConfigService(AppConfigOptions options, ILogger logger)
    {
        _options = options;
        _logger = logger;
        _current = CreateDefault();
    }

    public DnsRouteConfig Current => _current;

    public bool Exists => File.Exists(_options.ConfigFilePath);

    public DnsRouteConfig Load()
    {
        try
        {
            if (!Exists)
            {
                _logger.Info("配置文件不存在，创建默认配置。", nameof(ConfigService));
                _current = CreateDefault();
                EnsureDirectory();
                Save();
                return _current;
            }

            var json = File.ReadAllText(_options.ConfigFilePath);
            _current = JsonSerializer.Deserialize<DnsRouteConfig>(json, JsonOpts) ?? CreateDefault();
            _logger.Info($"已加载配置：{_current.ResolverProfiles.Count} 个配置档，{_current.Rules.Count} 条规则。", nameof(ConfigService));
            return _current;
        }
        catch (Exception ex)
        {
            _logger.Error("加载配置失败，回退默认配置。", nameof(ConfigService), ex);
            _current = CreateDefault();
            return _current;
        }
    }

    public void Save() => Save(_current);

    public void Save(DnsRouteConfig config)
    {
        lock (_saveSync)
        {
            EnsureDirectory();
            var json = JsonSerializer.Serialize(config, JsonOpts);
            File.WriteAllText(_options.ConfigFilePath, json);
            _logger.Info("配置已保存。", nameof(ConfigService));
        }
    }

    public void RecordApplyResult(ApplicationResult result)
    {
        _current.LastAppliedAt = result.FinishedAt;
        _current.LastApplyResult = result.Summary;
        Save();
    }

    public DnsRouteConfig CreateDefault()
    {
        var prefix = _options.ObjectPrefix;

        // 默认公共 DNS 配置档
        var defaultProfile = new ResolverProfile
        {
            Id = "default-public",
            Name = "默认公共 DNS",
            Forwarders = new List<string> { "1.1.1.1", "8.8.8.8" },
            EnableRecursion = true,
            CacheIsolationEnabled = true,
            CacheScopeName = $"{prefix}Cache_DefaultPublic",
            Note = "默认策略使用的公共 DNS"
        };

        return new DnsRouteConfig
        {
            Version = 1,
            CacheIsolation = CacheIsolationMode.ByResolverProfile,
            ResolverProfiles = new List<ResolverProfile> { defaultProfile },
            Rules = new List<SegmentRule>(),
            DefaultPolicy = new DefaultPolicy
            {
                ResolverProfileId = defaultProfile.Id,
                Enabled = true,
                Note = "未匹配网段的默认出口"
            }
        };
    }

    private void EnsureDirectory()
    {
        var dir = Path.GetDirectoryName(_options.ConfigFilePath);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
    }
}
