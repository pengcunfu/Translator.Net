using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using LavaTranslator.Models;

namespace LavaTranslator.Services;

public sealed class ConfigService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly string _configPath;
    private AppConfig _config;

    public ConfigService()
    {
        var configDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".lava_translator");
        Directory.CreateDirectory(configDir);
        _configPath = Path.Combine(configDir, "config.json");
        _config = Load();
    }

    public AppConfig Current => _config;

    public event EventHandler? ConfigChanged;

    public AppConfig Load()
    {
        if (!File.Exists(_configPath))
        {
            _config = CreateDefault();
            Save(_config);
            return _config;
        }

        try
        {
            var json = File.ReadAllText(_configPath);
            var loaded = JsonSerializer.Deserialize<AppConfig>(json, JsonOptions);
            _config = MergeWithDefaults(loaded ?? CreateDefault());
            MigrateLegacyGlmConfig(json);
            return _config;
        }
        catch
        {
            _config = CreateDefault();
            return _config;
        }
    }

    public bool Save(AppConfig? config = null)
    {
        try
        {
            _config = config ?? _config;
            var json = JsonSerializer.Serialize(_config, JsonOptions);
            File.WriteAllText(_configPath, json);
            ConfigChanged?.Invoke(this, EventArgs.Empty);
            return true;
        }
        catch
        {
            return false;
        }
    }

    public void UpdateBaidu(BaiduConfig baidu)
    {
        _config.Baidu = baidu;
        Save();
    }

    public void UpdateOpenAiProviders(List<OpenAiProviderConfig> providers)
    {
        _config.OpenAiProviders = providers;
        Save();
    }

    public void UpdateGeneral(GeneralConfig general)
    {
        _config.General = general;
        Save();
    }

    public string ConfigFilePath => _configPath;

    private static AppConfig CreateDefault() => new()
    {
        Baidu = new BaiduConfig(),
        OpenAiProviders =
        [
            new OpenAiProviderConfig
            {
                Name = "示例 AI",
                BaseUrl = "https://api.openai.com/v1/",
                Model = "gpt-4o-mini",
                Enabled = false
            }
        ],
        General = new GeneralConfig()
    };

    private void MigrateLegacyGlmConfig(string json)
    {
        if (_config.OpenAiProviders.Count > 0 && _config.OpenAiProviders.Any(p => p.Enabled && !string.IsNullOrWhiteSpace(p.ApiKey)))
            return;

        try
        {
            var node = JsonNode.Parse(json);
            var glm = node?["glm"];
            if (glm is null)
                return;

            var provider = new OpenAiProviderConfig
            {
                Name = "GLM翻译",
                ApiKey = glm["api_key"]?.GetValue<string>() ?? "",
                BaseUrl = glm["base_url"]?.GetValue<string>() ?? "https://open.bigmodel.cn/api/paas/v4/",
                Model = glm["model"]?.GetValue<string>() ?? "glm-4-flash",
                Temperature = glm["temperature"]?.GetValue<double>() ?? 0.3,
                MaxTokens = glm["max_tokens"]?.GetValue<int>() ?? 2000,
                Enabled = !string.IsNullOrWhiteSpace(glm["api_key"]?.GetValue<string>())
            };

            if (string.IsNullOrWhiteSpace(provider.ApiKey))
                return;

            _config.OpenAiProviders = [provider];
            Save();
        }
        catch
        {
            // ignore migration errors
        }
    }

    private static AppConfig MergeWithDefaults(AppConfig loaded)
    {
        var defaults = CreateDefault();
        if (string.IsNullOrWhiteSpace(loaded.Baidu.AppId))
            loaded.Baidu ??= defaults.Baidu;
        loaded.OpenAiProviders ??= [];
        loaded.General ??= defaults.General;
        return loaded;
    }
}
