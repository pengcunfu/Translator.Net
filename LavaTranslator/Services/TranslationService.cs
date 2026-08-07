using LavaTranslator.Models;

namespace LavaTranslator.Services;

public sealed class TranslationService
{
    private readonly ConfigService _configService;
    private readonly Dictionary<string, ITranslator> _translators = new(StringComparer.Ordinal);

    public TranslationService(ConfigService configService)
    {
        _configService = configService;
        _configService.ConfigChanged += (_, _) => RebuildTranslators();
        RebuildTranslators();
    }

    public IReadOnlyList<string> AvailableTranslators =>
        _translators.Keys.OrderBy(k => k == "百度翻译" ? 0 : 1).ThenBy(k => k).ToList();

    public async Task<TranslationResult> TranslateAsync(
        string text,
        string translatorName,
        TranslationOptions options,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(text))
            return new TranslationResult
            {
                OriginalText = text,
                TranslatedText = "",
                TranslatorName = translatorName,
                Success = false,
                ErrorMessage = "请输入要翻译的文本"
            };

        if (!_translators.TryGetValue(translatorName, out var translator))
            return new TranslationResult
            {
                OriginalText = text,
                TranslatedText = "",
                TranslatorName = translatorName,
                Success = false,
                ErrorMessage = $"未知的翻译引擎: {translatorName}"
            };

        try
        {
            var translated = await translator.TranslateAsync(text, options, cancellationToken);
            return new TranslationResult
            {
                OriginalText = text,
                TranslatedText = translated,
                TranslatorName = translatorName,
                SourceLanguage = options.FromCode,
                TargetLanguage = options.ToCode,
                Success = true
            };
        }
        catch (Exception ex)
        {
            return new TranslationResult
            {
                OriginalText = text,
                TranslatedText = "",
                TranslatorName = translatorName,
                Success = false,
                ErrorMessage = ex.Message
            };
        }
    }

    private void RebuildTranslators()
    {
        _translators.Clear();
        var config = _configService.Current;

        _translators["百度翻译"] = new BaiduTranslator(config.Baidu);

        foreach (var provider in config.OpenAiProviders.Where(p =>
            p.Enabled && !string.IsNullOrWhiteSpace(p.Name) && !string.IsNullOrWhiteSpace(p.ApiKey)))
        {
            var name = provider.Name.Trim();
            if (_translators.ContainsKey(name))
                name = $"{name} ({provider.Id[..6]})";

            _translators[name] = new OpenAiTranslator(provider);
        }
    }
}
