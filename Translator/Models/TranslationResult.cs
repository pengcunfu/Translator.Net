namespace LavaTranslator.Models;

public sealed class TranslationResult
{
    public required string OriginalText { get; init; }
    public required string TranslatedText { get; init; }
    public required string TranslatorName { get; init; }
    public string? SourceLanguage { get; init; }
    public string? TargetLanguage { get; init; }
    public bool Success { get; init; }
    public string? ErrorMessage { get; init; }
}
