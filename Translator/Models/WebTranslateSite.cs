namespace LavaTranslator.Models;

public sealed class WebTranslateSite
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required Uri HomeUrl { get; init; }

    /// <summary>若支持，用原文构造带查询参数的地址；否则返回首页。</summary>
    public Func<string, Uri>? BuildUrlWithText { get; init; }

    public Uri ResolveUrl(string? text)
    {
        if (string.IsNullOrWhiteSpace(text) || BuildUrlWithText is null)
            return HomeUrl;
        return BuildUrlWithText(text.Trim());
    }
}

public static class WebTranslateCatalog
{
    public static IReadOnlyList<WebTranslateSite> All { get; } =
    [
        new()
        {
            Id = "youdao",
            Name = "有道翻译",
            HomeUrl = new Uri("https://fanyi.youdao.com/index.html#/")
        },
        new()
        {
            Id = "sogou",
            Name = "搜狗翻译",
            HomeUrl = new Uri("https://fanyi.sogou.com/text")
        },
        new()
        {
            Id = "baidu",
            Name = "百度翻译",
            HomeUrl = new Uri("https://fanyi.baidu.com/")
        },
        new()
        {
            Id = "bing",
            Name = "必应翻译",
            HomeUrl = new Uri("https://www.bing.com/translator"),
            BuildUrlWithText = text => new Uri(
                "https://www.bing.com/translator?text=" + Uri.EscapeDataString(text))
        },
        new()
        {
            Id = "google",
            Name = "谷歌翻译",
            HomeUrl = new Uri("https://translate.google.com/"),
            BuildUrlWithText = text => new Uri(
                "https://translate.google.com/?sl=auto&tl=zh-CN&text="
                + Uri.EscapeDataString(text)
                + "&op=translate")
        },
        new()
        {
            Id = "deepl",
            Name = "DeepL",
            HomeUrl = new Uri("https://www.deepl.com/translator")
        },
        new()
        {
            Id = "tencent",
            Name = "腾讯翻译",
            HomeUrl = new Uri("https://fanyi.qq.com/")
        },
    ];

    public static WebTranslateSite? FindById(string? id) =>
        All.FirstOrDefault(s => s.Id.Equals(id, StringComparison.OrdinalIgnoreCase));
}
