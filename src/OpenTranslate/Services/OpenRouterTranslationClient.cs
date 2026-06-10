using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using OpenTranslate.Models;

namespace OpenTranslate.Services;

public sealed class OpenRouterTranslationClient : IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly HttpClient _httpClient;

    public OpenRouterTranslationClient()
    {
        _httpClient = new HttpClient
        {
            BaseAddress = new Uri("https://openrouter.ai/api/v1/"),
            Timeout = TimeSpan.FromSeconds(60)
        };
        _httpClient.DefaultRequestHeaders.Accept.Add(
            new MediaTypeWithQualityHeaderValue("application/json"));
        _httpClient.DefaultRequestHeaders.Add("HTTP-Referer", "https://github.com/OpenTranslate");
        _httpClient.DefaultRequestHeaders.Add("X-Title", "OpenTranslate");
    }

    public async Task<string> TranslateAsync(
        string text,
        AppSettings settings,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(settings.ApiKey))
            throw new OpenRouterApiException(0, "Configure your OpenRouter API key in Settings.");

        var model = string.IsNullOrWhiteSpace(settings.Model)
            ? AppSettings.DefaultModel
            : settings.Model.Trim();

        var protectedText = TextFormattingHelper.ProtectBlankLines(text);
        var systemPrompt = BuildSystemPrompt(settings);

        var requestBody = new ChatCompletionRequest
        {
            Model = model,
            Messages =
            [
                new ChatMessage
                {
                    Role = "system",
                    Content = systemPrompt
                },
                new ChatMessage
                {
                    Role = "user",
                    Content = protectedText
                }
            ]
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, "chat/completions");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", settings.ApiKey.Trim());
        request.Content = JsonContent.Create(requestBody, options: JsonOptions);

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        await EnsureSuccessAsync(response);

        var payload = await response.Content.ReadFromJsonAsync<ChatCompletionResponse>(JsonOptions, cancellationToken);
        var translated = payload?.Choices?.FirstOrDefault()?.Message?.Content;
        translated = TextFormattingHelper.RestoreBlankLines(translated ?? "");

        if (string.IsNullOrWhiteSpace(translated))
            throw new OpenRouterApiException(0, "OpenRouter returned an empty response.");

        return translated;
    }

    private static string BuildSystemPrompt(AppSettings settings)
    {
        var sourceCode = string.IsNullOrWhiteSpace(settings.SourceLanguage)
            ? "es"
            : settings.SourceLanguage.Trim();
        var targetCode = string.IsNullOrWhiteSpace(settings.TargetLanguage)
            ? "en"
            : settings.TargetLanguage.Trim();
        var source = TranslationLanguages.ResolveName(sourceCode);
        var target = TranslationLanguages.ResolveName(targetCode);
        var blankLineRule =
            $"The marker {TextFormattingHelper.BlankLineMarker} represents a blank line between paragraphs: do not translate it, remove it, or move it.";

        if (settings.AutoDetectLanguage)
        {
            return $"Detect whether the text is in {source} or {target}. " +
                   $"If it is in {source}, translate it to {target}. " +
                   $"If it is in {target}, translate it to {source}. " +
                   $"Return only the translated text, with no explanations or quotes. Preserve line breaks. {blankLineRule}";
        }

        return $"Translate from {source} to {target}. Return only the translated text, with no explanations or quotes. Preserve line breaks. {blankLineRule}";
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage response)
    {
        if (response.IsSuccessStatusCode)
            return;

        var body = await response.Content.ReadAsStringAsync();
        var message = response.StatusCode switch
        {
            System.Net.HttpStatusCode.Unauthorized =>
                "The OpenRouter API key is invalid or has expired.",
            System.Net.HttpStatusCode.TooManyRequests =>
                "OpenRouter rate-limited the request. Try again in a few seconds.",
            _ => $"OpenRouter error ({(int)response.StatusCode}): {Truncate(body, 120)}"
        };

        throw new OpenRouterApiException((int)response.StatusCode, message);
    }

    private static string Truncate(string value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "no details";

        var trimmed = value.Trim();
        return trimmed.Length <= maxLength ? trimmed : trimmed[..maxLength] + "…";
    }

    public void Dispose() => _httpClient.Dispose();

    private sealed class ChatCompletionRequest
    {
        public string Model { get; set; } = "";
        public List<ChatMessage> Messages { get; set; } = [];
    }

    private sealed class ChatMessage
    {
        public string Role { get; set; } = "";
        public string Content { get; set; } = "";
    }

    private sealed class ChatCompletionResponse
    {
        public List<ChatChoice>? Choices { get; set; }
    }

    private sealed class ChatChoice
    {
        public ChatMessage? Message { get; set; }
    }
}
