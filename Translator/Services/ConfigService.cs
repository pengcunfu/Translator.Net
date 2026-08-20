using System.Text.Json;
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

    public void UpdateGeneral(GeneralConfig general)
    {
        _config.General = general;
        Save();
    }

    public string ConfigFilePath => _configPath;

    private static AppConfig CreateDefault() => new()
    {
        General = new GeneralConfig()
    };

    private static AppConfig MergeWithDefaults(AppConfig loaded)
    {
        var defaults = CreateDefault();
        loaded.General ??= defaults.General;
        return loaded;
    }
}
