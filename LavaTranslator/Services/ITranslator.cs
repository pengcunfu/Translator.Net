using LavaTranslator.Models;

namespace LavaTranslator.Services;

public interface ITranslator
{
    string Name { get; }
    Task<string> TranslateAsync(string text, TranslationOptions options, CancellationToken cancellationToken = default);
}
