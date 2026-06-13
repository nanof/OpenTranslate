using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using OpenTranslate.Models;

namespace OpenTranslate.Services;

public sealed class TranslationClient : IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    // MyMemory's free anonymous endpoint caps each query at 500 bytes; stay safely below it.
    private const int MyMemoryMaxSegmentLength = 450;

    private readonly HttpClient _openRouterClient;
    private readonly HttpClient _openAiClient;
    private readonly HttpClient _geminiClient;
    private readonly HttpClient _myMemoryClient;

    public TranslationClient()
    {
        _openRouterClient = CreateClient(
            "https://openrouter.ai/api/v1/",
            configureHeaders: client =>
            {
                client.DefaultRequestHeaders.Add("HTTP-Referer", "https://github.com/nanof/OpenTranslate");
                client.DefaultRequestHeaders.Add("X-Title", "OpenTranslate");
            });

        _openAiClient = CreateClient("https://api.openai.com/v1/");
        _geminiClient = CreateClient("https://generativelanguage.googleapis.com/v1beta/");
        _myMemoryClient = CreateClient("https://api.mymemory.translated.net/");
    }

    public Task<string> TranslateAsync(
        string text,
        AppSettings settings,
        CancellationToken cancellationToken = default) =>
        settings.Provider switch
        {
            TranslationProvider.MyMemory => TranslateWithMyMemoryAsync(text, settings, cancellationToken),
            TranslationProvider.Gemini => TranslateWithGeminiAsync(text, settings, cancellationToken),
            _ => TranslateWithChatCompletionsAsync(text, settings, cancellationToken)
        };

    private async Task<string> TranslateWithChatCompletionsAsync(
        string text,
        AppSettings settings,
        CancellationToken cancellationToken)
    {
        var provider = settings.Provider;

        var apiKey = settings.GetActiveApiKey();
        if (string.IsNullOrWhiteSpace(apiKey))
            throw new TranslationApiException(0, TranslationProviders.GetApiKeyMissingMessage(provider));

        var model = settings.GetEffectiveModel();
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

        var httpClient = provider == TranslationProvider.OpenAi ? _openAiClient : _openRouterClient;

        using var request = new HttpRequestMessage(HttpMethod.Post, "chat/completions");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        request.Content = JsonContent.Create(requestBody, options: JsonOptions);

        using var response = await httpClient.SendAsync(request, cancellationToken);
        await EnsureSuccessAsync(response, provider);

        var payload = await response.Content.ReadFromJsonAsync<ChatCompletionResponse>(JsonOptions, cancellationToken);
        var translated = payload?.Choices?.FirstOrDefault()?.Message?.Content;
        translated = TextFormattingHelper.RestoreBlankLines(translated ?? "");

        if (string.IsNullOrWhiteSpace(translated))
            throw new TranslationApiException(0, TranslationProviders.GetEmptyResponseMessage(provider));

        return translated;
    }

    private async Task<string> TranslateWithGeminiAsync(
        string text,
        AppSettings settings,
        CancellationToken cancellationToken)
    {
        const TranslationProvider provider = TranslationProvider.Gemini;

        var apiKey = settings.GetActiveApiKey();
        if (string.IsNullOrWhiteSpace(apiKey))
            throw new TranslationApiException(0, TranslationProviders.GetApiKeyMissingMessage(provider));

        var model = NormalizeGeminiModel(settings.GetEffectiveModel());
        var protectedText = TextFormattingHelper.ProtectBlankLines(text);
        var systemPrompt = BuildSystemPrompt(settings);

        var requestBody = new GeminiGenerateContentRequest
        {
            SystemInstruction = new GeminiContent
            {
                Parts = [new GeminiPart { Text = systemPrompt }]
            },
            Contents =
            [
                new GeminiContent
                {
                    Role = "user",
                    Parts = [new GeminiPart { Text = protectedText }]
                }
            ]
        };

        var escapedApiKey = Uri.EscapeDataString(apiKey);
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"models/{model}:generateContent?key={escapedApiKey}");
        request.Content = JsonContent.Create(requestBody, options: JsonOptions);

        using var response = await _geminiClient.SendAsync(request, cancellationToken);
        await EnsureSuccessAsync(response, provider);

        var payload = await response.Content.ReadFromJsonAsync<GeminiGenerateContentResponse>(JsonOptions, cancellationToken);
        var translated = payload?.Candidates?
            .FirstOrDefault()?
            .Content?
            .Parts?
            .FirstOrDefault()?
            .Text;

        translated = TextFormattingHelper.RestoreBlankLines(translated ?? "");

        if (string.IsNullOrWhiteSpace(translated))
            throw new TranslationApiException(0, TranslationProviders.GetEmptyResponseMessage(provider));

        return translated;
    }

    private async Task<string> TranslateWithMyMemoryAsync(
        string text,
        AppSettings settings,
        CancellationToken cancellationToken)
    {
        const TranslationProvider provider = TranslationProvider.MyMemory;

        var source = NormalizeMyMemoryLanguage(settings.SourceLanguage, "es");
        var target = NormalizeMyMemoryLanguage(settings.TargetLanguage, "en");

        if (string.Equals(source, target, StringComparison.OrdinalIgnoreCase))
            throw new TranslationApiException(0, "Source and target language must be different.");

        // MyMemory has no LLM-style instruction, so translate line by line to preserve the
        // original layout (and keep blank lines untouched) and chunk long lines to fit the
        // free endpoint's per-query length limit.
        var lines = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        var translatedLines = new string[lines.Length];

        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            if (string.IsNullOrWhiteSpace(line))
            {
                translatedLines[i] = line;
                continue;
            }

            var builder = new StringBuilder();
            foreach (var segment in SplitForMyMemory(line, MyMemoryMaxSegmentLength))
            {
                builder.Append(await TranslateMyMemorySegmentAsync(segment, source, target, cancellationToken)
                    .ConfigureAwait(false));
            }

            translatedLines[i] = builder.ToString();
        }

        var translated = string.Join("\n", translatedLines);

        if (string.IsNullOrWhiteSpace(translated))
            throw new TranslationApiException(0, TranslationProviders.GetEmptyResponseMessage(provider));

        return translated;
    }

    private async Task<string> TranslateMyMemorySegmentAsync(
        string segment,
        string source,
        string target,
        CancellationToken cancellationToken)
    {
        var query = Uri.EscapeDataString(segment);
        var langPair = Uri.EscapeDataString($"{source}|{target}");

        using var response = await _myMemoryClient
            .GetAsync($"get?q={query}&langpair={langPair}", cancellationToken)
            .ConfigureAwait(false);

        await EnsureSuccessAsync(response, TranslationProvider.MyMemory).ConfigureAwait(false);

        var payload = await response.Content
            .ReadFromJsonAsync<MyMemoryResponse>(JsonOptions, cancellationToken)
            .ConfigureAwait(false);

        var translated = payload?.ResponseData?.TranslatedText;
        if (string.IsNullOrWhiteSpace(translated))
        {
            var details = string.IsNullOrWhiteSpace(payload?.ResponseDetails)
                ? TranslationProviders.GetEmptyResponseMessage(TranslationProvider.MyMemory)
                : payload!.ResponseDetails!;
            throw new TranslationApiException(0, details);
        }

        return System.Net.WebUtility.HtmlDecode(translated);
    }

    private static IReadOnlyList<string> SplitForMyMemory(string text, int maxLength)
    {
        if (text.Length <= maxLength)
            return [text];

        var parts = new List<string>();
        var start = 0;

        while (start < text.Length)
        {
            if (text.Length - start <= maxLength)
            {
                parts.Add(text[start..]);
                break;
            }

            var end = start + maxLength;
            var breakPos = text.LastIndexOf(' ', end - 1, end - start);

            if (breakPos <= start)
            {
                parts.Add(text[start..end]);
                start = end;
            }
            else
            {
                parts.Add(text[start..(breakPos + 1)]);
                start = breakPos + 1;
            }
        }

        return parts;
    }

    private static string NormalizeMyMemoryLanguage(string? code, string fallback)
    {
        var trimmed = code?.Trim();
        return string.IsNullOrWhiteSpace(trimmed) ? fallback : trimmed.ToLowerInvariant();
    }

    private static string NormalizeGeminiModel(string model)
    {
        var trimmed = model.Trim();
        const string prefix = "models/";
        return trimmed.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
            ? trimmed[prefix.Length..]
            : trimmed;
    }

    private static HttpClient CreateClient(string baseAddress, Action<HttpClient>? configureHeaders = null)
    {
        var client = new HttpClient
        {
            BaseAddress = new Uri(baseAddress),
            Timeout = TimeSpan.FromSeconds(60)
        };
        client.DefaultRequestHeaders.Accept.Add(
            new MediaTypeWithQualityHeaderValue("application/json"));
        configureHeaders?.Invoke(client);
        return client;
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

        if (settings.ImprovementMode == TextImprovementMode.ImproveOnly)
        {
            return "Improve the following text without translating it; keep it in its original language. " +
                   "Correct spelling, grammar, and punctuation, and make it read clearly and naturally. " +
                   $"Return only the improved text, with no explanations or quotes. Preserve line breaks. {blankLineRule}";
        }

        var task = settings.AutoDetectLanguage
            ? $"Detect whether the text is in {source} or {target}. " +
              $"If it is in {source}, translate it to {target}. " +
              $"If it is in {target}, translate it to {source}."
            : $"Translate from {source} to {target}.";

        var improvement = TextImprovementModes.GetTranslationClause(settings.ImprovementMode);

        return $"{task}{improvement} Return only the translated text, with no explanations or quotes. Preserve line breaks. {blankLineRule}";
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage response, TranslationProvider provider)
    {
        if (response.IsSuccessStatusCode)
            return;

        var body = await response.Content.ReadAsStringAsync();
        var message = response.StatusCode switch
        {
            System.Net.HttpStatusCode.Unauthorized or System.Net.HttpStatusCode.Forbidden =>
                TranslationProviders.GetInvalidApiKeyMessage(provider),
            System.Net.HttpStatusCode.TooManyRequests =>
                TranslationProviders.GetRateLimitMessage(provider),
            System.Net.HttpStatusCode.BadRequest when provider == TranslationProvider.Gemini
                && body.Contains("API key", StringComparison.OrdinalIgnoreCase) =>
                TranslationProviders.GetInvalidApiKeyMessage(provider),
            _ => TranslationProviders.GetErrorMessage(provider, (int)response.StatusCode, Truncate(body, 120))
        };

        throw new TranslationApiException((int)response.StatusCode, message);
    }

    private static string Truncate(string value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "no details";

        var trimmed = value.Trim();
        return trimmed.Length <= maxLength ? trimmed : trimmed[..maxLength] + "…";
    }

    public void Dispose()
    {
        _openRouterClient.Dispose();
        _openAiClient.Dispose();
        _geminiClient.Dispose();
        _myMemoryClient.Dispose();
    }

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

    private sealed class GeminiGenerateContentRequest
    {
        public GeminiContent? SystemInstruction { get; set; }
        public List<GeminiContent> Contents { get; set; } = [];
    }

    private sealed class GeminiGenerateContentResponse
    {
        public List<GeminiCandidate>? Candidates { get; set; }
    }

    private sealed class GeminiCandidate
    {
        public GeminiContent? Content { get; set; }
    }

    private sealed class GeminiContent
    {
        public string? Role { get; set; }
        public List<GeminiPart>? Parts { get; set; }
    }

    private sealed class GeminiPart
    {
        public string? Text { get; set; }
    }

    private sealed class MyMemoryResponse
    {
        public MyMemoryResponseData? ResponseData { get; set; }
        public string? ResponseDetails { get; set; }
    }

    private sealed class MyMemoryResponseData
    {
        public string? TranslatedText { get; set; }
    }
}
