namespace LavaTranslator.Models;

public sealed class AppConfig
{
    public GeneralConfig General { get; set; } = new();
}

public sealed class GeneralConfig
{
    public bool RunAtStartup { get; set; }
    public string Hotkey { get; set; } = "Alt+Space";
    public string LastWebSiteId { get; set; } = "youdao";
}
