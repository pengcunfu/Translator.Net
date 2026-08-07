using System.Net.Http;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization;
using LavaTranslator.Models;

namespace LavaTranslator.Services;

public sealed class BaiduTranslator : ITranslator
{
    private const string ApiUrl = "https://fanyi-api.baidu.com/api/trans/vip/translate";
    private readonly HttpClient _httpClient;
    private readonly BaiduConfig _config;

    public BaiduTranslator(BaiduConfig config, HttpClient? httpClient = null)
    {
        _config = config;
        _httpClient = httpClient ?? new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
    }

    public string Name => "百度翻译";

    public async Task<string> TranslateAsync(string text, TranslationOptions options, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(_config.AppId) || string.IsNullOrWhiteSpace(_config.SecretKey))
            throw new InvalidOperationException("请先在设置中配置百度翻译的 App ID 和密钥");

        options.Validate(text);

        var salt = Random.Shared.Next(100000, 999999).ToString();
        var sign = ComputeSign(_config.AppId, text, salt, _config.SecretKey);
        var from = options.FromCode.Equals("auto", StringComparison.OrdinalIgnoreCase)
            ? "auto"
            : options.FromCode;
        var to = options.ToCode;

        var query = new Dictionary<string, string>
        {
            ["q"] = text,
            ["from"] = from,
            ["to"] = to,
            ["appid"] = _config.AppId,
            ["salt"] = salt,
            ["sign"] = sign
        };

        using var content = new FormUrlEncodedContent(query);
        using var response = await _httpClient.PostAsync(ApiUrl, content, cancellationToken);
        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<BaiduApiResponse>(cancellationToken);
        if (result is null)
            throw new InvalidOperationException("百度翻译返回空响应");

        if (!string.IsNullOrEmpty(result.ErrorCode))
            throw new InvalidOperationException($"百度翻译错误 [{result.ErrorCode}]: {GetBaiduErrorMessage(result.ErrorCode)}");

        var translated = result.TransResult?.FirstOrDefault()?.Dst;
        if (string.IsNullOrWhiteSpace(translated))
            throw new InvalidOperationException("百度翻译结果为空");

        return translated;
    }

    private static string ComputeSign(string appId, string query, string salt, string secretKey)
    {
        var input = appId + query + salt + secretKey;
        var hash = MD5.HashData(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static string GetBaiduErrorMessage(string code) => code switch
    {
        "52001" => "请求超时，请重试",
        "52002" => "系统错误，请重试",
        "52003" => "未授权用户，请检查 App ID 和密钥",
        "54000" => "必填参数为空",
        "54001" => "签名错误，请检查密钥",
        "54003" => "访问频率受限",
        "54004" => "账户余额不足",
        "54005" => "长query请求频繁",
        "58000" => "客户端 IP 非法",
        "58001" => "译文语言方向不支持",
        "58002" => "服务当前已关闭",
        _ => "未知错误"
    };

    private sealed class BaiduApiResponse
    {
        [JsonPropertyName("error_code")]
        public string? ErrorCode { get; set; }

        [JsonPropertyName("trans_result")]
        public List<BaiduTransItem>? TransResult { get; set; }
    }

    private sealed class BaiduTransItem
    {
        [JsonPropertyName("dst")]
        public string? Dst { get; set; }
    }
}
