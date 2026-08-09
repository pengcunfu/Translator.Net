namespace LavaTranslator.Models;

public sealed class AppConfig
{
    public BaiduConfig Baidu { get; set; } = new();
    public List<OpenAiProviderConfig> OpenAiProviders { get; set; } = [];
    public GeneralConfig General { get; set; } = new();
}

public sealed class BaiduConfig
{
    public string AppId { get; set; } = "";
    public string SecretKey { get; set; } = "";
}

public sealed class OpenAiProviderConfig
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "AI翻译";
    public string ApiKey { get; set; } = "";
    public string BaseUrl { get; set; } = "https://api.openai.com/v1/";
    public string Model { get; set; } = "gpt-4o-mini";
    public double Temperature { get; set; } = 0.3;
    public int MaxTokens { get; set; } = 2000;
    public bool Enabled { get; set; } = true;
}

public sealed class GeneralConfig
{
    public bool RunAtStartup { get; set; }
    public bool RememberLastTranslator { get; set; } = true;
    public string LastTranslator { get; set; } = "百度翻译";
    public string Hotkey { get; set; } = "Alt+Space";
    public string SourceLanguage { get; set; } = "auto";
    public string TargetLanguage { get; set; } = "en";
    public string LastWebSiteId { get; set; } = "youdao";
}
