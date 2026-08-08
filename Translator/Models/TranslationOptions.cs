namespace LavaTranslator.Models;

public sealed class TranslationOptions
{
    public string FromCode { get; init; } = "auto";
    public string ToCode { get; init; } = "en";

    public string ResolveFromCode(string text) =>
        FromCode.Equals("auto", StringComparison.OrdinalIgnoreCase)
            ? LanguageCatalog.DetectFromText(text)
            : FromCode;

    public void Validate(string text)
    {
        if (string.IsNullOrWhiteSpace(ToCode) || ToCode.Equals("auto", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("请选择目标语言");

        var from = ResolveFromCode(text);
        if (from.Equals(ToCode, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("原文语言与目标语言不能相同");
    }
}
