namespace LavaTranslator.Models;

public sealed class AppConfig
{
    public BaiduConfig Baidu { get; set; } = new();
    public GeneralConfig General { get; set; } = new();
}

public sealed class BaiduConfig
{
    public string AppId { get; set; } = "";
    public string SecretKey { get; set; } = "";
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
