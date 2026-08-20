namespace LavaTranslator.Models;

public sealed class LanguageOption
{
    public required string Code { get; init; }
    public required string DisplayName { get; init; }
    public bool AllowAsSource { get; init; } = true;
    public bool AllowAsTarget { get; init; } = true;
}

public static class LanguageCatalog
{
    public static readonly LanguageOption Auto = new() { Code = "auto", DisplayName = "自动检测", AllowAsTarget = false };

    public static IReadOnlyList<LanguageOption> All { get; } =
    [
        Auto,
        new() { Code = "zh", DisplayName = "中文" },
        new() { Code = "en", DisplayName = "英语" },
        new() { Code = "jp", DisplayName = "日语" },
        new() { Code = "kor", DisplayName = "韩语" },
        new() { Code = "fra", DisplayName = "法语" },
        new() { Code = "de", DisplayName = "德语" },
        new() { Code = "spa", DisplayName = "西班牙语" },
        new() { Code = "ru", DisplayName = "俄语" },
        new() { Code = "pt", DisplayName = "葡萄牙语" },
        new() { Code = "it", DisplayName = "意大利语" },
        new() { Code = "th", DisplayName = "泰语" },
        new() { Code = "ara", DisplayName = "阿拉伯语" },
        new() { Code = "vie", DisplayName = "越南语" },
        new() { Code = "cht", DisplayName = "繁体中文" },
    ];

    public static IReadOnlyList<LanguageOption> SourceLanguages =>
        All.Where(l => l.AllowAsSource).ToList();

    public static IReadOnlyList<LanguageOption> TargetLanguages =>
        All.Where(l => l.AllowAsTarget).ToList();

    public static LanguageOption? FindByCode(string? code) =>
        All.FirstOrDefault(l => l.Code.Equals(code, StringComparison.OrdinalIgnoreCase));

    public static string GetDisplayName(string code) =>
        FindByCode(code)?.DisplayName ?? code;

    public static string DetectFromText(string text)
    {
        foreach (var c in text)
        {
            if (c is >= '\u4e00' and <= '\u9fff')
                return "zh";
        }
        return "en";
    }
}
