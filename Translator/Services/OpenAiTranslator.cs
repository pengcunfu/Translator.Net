using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using LavaTranslator.Models;

namespace LavaTranslator.Services;

public sealed class OpenAiTranslator : ITranslator
{
    private readonly OpenAiProviderConfig _config;
    private readonly HttpClient _httpClient;

    public OpenAiTranslator(OpenAiProviderConfig config, HttpClient? httpClient = null)
    {
        _config = config;
        _httpClient = httpClient ?? new HttpClient { Timeout = TimeSpan.FromMinutes(2) };
        Name = string.IsNullOrWhiteSpace(config.Name) ? "AI翻译" : config.Name;
    }

    public string Name { get; }

    public async Task<string> TranslateAsync(string text, TranslationOptions options, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(_config.ApiKey))
            throw new InvalidOperationException($"请先在设置中配置「{Name}」的 API Key");

        options.Validate(text);

        var resolvedFrom = options.ResolveFromCode(text);
        var sourceLang = LanguageCatalog.GetAiLanguageName(
            options.FromCode.Equals("auto", StringComparison.OrdinalIgnoreCase) ? resolvedFrom : options.FromCode);
        var targetLang = LanguageCatalog.GetAiLanguageName(options.ToCode);

        var baseUrl = NormalizeBaseUrl(_config.BaseUrl);
        var requestUri = new Uri(new Uri(baseUrl), "chat/completions");

        var payload = new
        {
            model = _config.Model,
            messages = new object[]
            {
                new
                {
                    role = "system",
                    content = $"你是一个专业的翻译助手。请将用户输入的{sourceLang}翻译成{targetLang}。只返回翻译结果，不要添加任何解释或额外内容。"
                },
                new { role = "user", content = text }
            },
            temperature = _config.Temperature,
            max_tokens = _config.MaxTokens
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, requestUri);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _config.ApiKey);
        request.Content = new StringContent(
            JsonSerializer.Serialize(payload),
            Encoding.UTF8,
            "application/json");

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"{Name} 请求失败 ({(int)response.StatusCode}): {Truncate(body, 300)}");

        var chatResponse = JsonSerializer.Deserialize<ChatCompletionResponse>(body);
        var content = chatResponse?.Choices?.FirstOrDefault()?.Message?.Content?.Trim();

        if (string.IsNullOrWhiteSpace(content))
            throw new InvalidOperationException($"{Name} 返回空结果");

        return content;
    }

    private static string NormalizeBaseUrl(string baseUrl)
    {
        if (string.IsNullOrWhiteSpace(baseUrl))
            return "https://api.openai.com/v1/";

        baseUrl = baseUrl.Trim();
        if (!baseUrl.EndsWith('/'))
            baseUrl += '/';

        // 已包含版本路径（/v1、/v4、/paas/v4 等）时不再追加 /v1/
        if (System.Text.RegularExpressions.Regex.IsMatch(
                baseUrl, @"/v\d+/?$", System.Text.RegularExpressions.RegexOptions.IgnoreCase))
            return baseUrl;

        try
        {
            var uri = new Uri(baseUrl);
            var path = uri.AbsolutePath.Trim('/');
            // 仅有域名或根路径时补全 /v1/
            if (string.IsNullOrEmpty(path))
                return $"{baseUrl.TrimEnd('/')}/v1/";
        }
        catch
        {
            // 非法 URL 原样返回，请求时会报错
        }

        return baseUrl;
    }

    private static string Truncate(string value, int max) =>
        value.Length <= max ? value : value[..max] + "...";

    private sealed class ChatCompletionResponse
    {
        [JsonPropertyName("choices")]
        public List<ChatChoice>? Choices { get; set; }
    }

    private sealed class ChatChoice
    {
        [JsonPropertyName("message")]
        public ChatMessage? Message { get; set; }
    }

    private sealed class ChatMessage
    {
        [JsonPropertyName("content")]
        public string? Content { get; set; }
    }
}
