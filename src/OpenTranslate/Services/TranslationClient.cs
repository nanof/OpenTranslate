using System.Globalization;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
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
        var protection = TextFormattingHelper.ProtectForTranslation(text, settings.PreserveFormatAndCode);
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
                    Content = protection.Text
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
        translated = TextFormattingHelper.RestoreFromTranslation(translated ?? "", protection);

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
        var protection = TextFormattingHelper.ProtectForTranslation(text, settings.PreserveFormatAndCode);
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
                    Parts = [new GeminiPart { Text = protection.Text }]
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

        translated = TextFormattingHelper.RestoreFromTranslation(translated ?? "", protection);

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
        var autoDetect = settings.AutoDetectLanguage;

        if (MyMemoryLanguagesMatch(source, target))
            throw new TranslationApiException(0, "Source and target language must be different.");

        // MyMemory has no LLM-style instruction, so translate line by line to preserve the
        // original layout (and keep blank lines untouched) and chunk long lines to fit the
        // free endpoint's per-query length limit.
        var protection = TextFormattingHelper.ProtectForTranslation(text, settings.PreserveFormatAndCode);
        var lines = protection.Text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
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
                builder.Append(await TranslateMyMemorySegmentAsync(
                        segment,
                        source,
                        target,
                        autoDetect,
                        cancellationToken)
                    .ConfigureAwait(false));
            }

            translatedLines[i] = builder.ToString();
        }

        var translated = TextFormattingHelper.RestoreFromTranslation(string.Join("\n", translatedLines), protection);

        if (string.IsNullOrWhiteSpace(translated))
            throw new TranslationApiException(0, TranslationProviders.GetEmptyResponseMessage(provider));

        return translated;
    }

    private async Task<string> TranslateMyMemorySegmentAsync(
        string segment,
        string source,
        string target,
        bool autoDetect,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(segment))
            return segment;

        if (TextFormattingHelper.PreservationMarkerRegex.IsMatch(segment)
            && TextFormattingHelper.PreservationMarkerRegex.Replace(segment, "").Length == 0)
            return segment;

        var builder = new StringBuilder();
        var parts = PreservationMarkerSplitRegex.Split(segment);

        foreach (var part in parts)
        {
            if (string.IsNullOrEmpty(part))
                continue;

            if (TextFormattingHelper.PreservationMarkerRegex.IsMatch(part))
            {
                builder.Append(part);
                continue;
            }

            builder.Append(await TranslateMyMemoryPlainSegmentAsync(
                    part,
                    source,
                    target,
                    autoDetect,
                    cancellationToken)
                .ConfigureAwait(false));
        }

        return builder.ToString();
    }

    private static readonly Regex PreservationMarkerSplitRegex = new(
        @"(⟦OT:\d+⟧)",
        RegexOptions.Compiled);

    private async Task<string> TranslateMyMemoryPlainSegmentAsync(
        string segment,
        string source,
        string target,
        bool autoDetect,
        CancellationToken cancellationToken)
    {
        if (!autoDetect)
            return RequireMyMemorySuccess(
                    await QueryMyMemoryAsync(segment, source, target, cancellationToken).ConfigureAwait(false))
                .TranslatedText;

        // autodetect|target detects the source language; if it matches target (MyMemory returns
        // 403), the text is already in the target language — translate back to source instead.
        var detected = await QueryMyMemoryAsync(segment, "autodetect", target, cancellationToken)
            .ConfigureAwait(false);

        if (detected.IsSuccess && !MyMemoryLanguagesMatch(detected.DetectedLanguage, target))
            return detected.TranslatedText;

        return RequireMyMemorySuccess(
                await QueryMyMemoryAsync(segment, target, source, cancellationToken).ConfigureAwait(false))
            .TranslatedText;
    }

    private async Task<MyMemoryQueryResult> QueryMyMemoryAsync(
        string segment,
        string sourceLang,
        string targetLang,
        CancellationToken cancellationToken)
    {
        var query = Uri.EscapeDataString(segment);
        var langPair = Uri.EscapeDataString($"{sourceLang}|{targetLang}");

        using var response = await _myMemoryClient
            .GetAsync($"get?q={query}&langpair={langPair}", cancellationToken)
            .ConfigureAwait(false);

        await EnsureSuccessAsync(response, TranslationProvider.MyMemory).ConfigureAwait(false);

        var payload = await response.Content
            .ReadFromJsonAsync<MyMemoryResponse>(JsonOptions, cancellationToken)
            .ConfigureAwait(false);

        if (!IsMyMemorySuccess(payload))
        {
            return new MyMemoryQueryResult(
                "",
                payload?.ResponseData?.DetectedLanguage,
                false,
                payload?.ResponseDetails);
        }

        var translated = payload?.ResponseData?.TranslatedText;
        if (string.IsNullOrWhiteSpace(translated))
        {
            return new MyMemoryQueryResult(
                "",
                payload?.ResponseData?.DetectedLanguage,
                false,
                payload?.ResponseDetails);
        }

        return new MyMemoryQueryResult(
            System.Net.WebUtility.HtmlDecode(translated),
            payload?.ResponseData?.DetectedLanguage,
            true,
            null);
    }

    private static MyMemoryQueryResult RequireMyMemorySuccess(MyMemoryQueryResult result)
    {
        if (result.IsSuccess)
            return result;

        var details = string.IsNullOrWhiteSpace(result.ErrorDetails)
            ? TranslationProviders.GetEmptyResponseMessage(TranslationProvider.MyMemory)
            : result.ErrorDetails!;
        throw new TranslationApiException(0, details);
    }

    private static bool IsMyMemorySuccess(MyMemoryResponse? payload) =>
        int.TryParse(payload?.ResponseStatus, out var status) && status == 200;

    private static bool MyMemoryLanguagesMatch(string? a, string? b) =>
        string.Equals(
            GetMyMemoryLanguagePrimaryCode(a),
            GetMyMemoryLanguagePrimaryCode(b),
            StringComparison.OrdinalIgnoreCase);

    private static string GetMyMemoryLanguagePrimaryCode(string? code)
    {
        var normalized = NormalizeMyMemoryLanguage(code, "");
        if (string.IsNullOrEmpty(normalized))
            return "";

        var dash = normalized.IndexOf('-');
        return dash > 0 ? normalized[..dash] : normalized;
    }

    private sealed record MyMemoryQueryResult(
        string TranslatedText,
        string? DetectedLanguage,
        bool IsSuccess,
        string? ErrorDetails);

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
        var preservationRule = TextFormattingHelper.GetPreservationPromptRule(settings.PreserveFormatAndCode);

        if (settings.ImprovementMode == TextImprovementMode.ImproveOnly)
        {
            return "Improve the following text without translating it; keep it in its original language. " +
                   "Correct spelling, grammar, and punctuation, and make it read clearly and naturally. " +
                   $"Return only the improved text, with no explanations or quotes. Preserve line breaks. {blankLineRule}{preservationRule}";
        }

        if (settings.ImprovementMode == TextImprovementMode.Summarize)
        {
            return $"Summarize the following text concisely in {target}. Capture the key points. " +
                   $"Return only the summary, with no explanations or quotes. {blankLineRule}{preservationRule}";
        }

        if (settings.ImprovementMode == TextImprovementMode.ExplainInTarget)
        {
            return $"Explain the following text clearly in {target}, as if helping someone understand it. " +
                   $"Return only the explanation, with no preamble or quotes. {blankLineRule}{preservationRule}";
        }

        if (settings.ImprovementMode == TextImprovementMode.ExplainInSource)
        {
            return $"Explain the following text clearly in {source}, as if helping someone understand it. " +
                   $"Return only the explanation, with no preamble or quotes. {blankLineRule}{preservationRule}";
        }

        var task = settings.AutoDetectLanguage
            ? $"Detect whether the text is in {source} or {target}. " +
              $"If it is in {source}, translate it to {target}. " +
              $"If it is in {target}, translate it to {source}."
            : $"Translate from {source} to {target}.";

        var improvement = TextImprovementModes.GetTranslationClause(settings.ImprovementMode);

        return $"{task}{improvement} Return only the translated text, with no explanations or quotes. Preserve line breaks. {blankLineRule}{preservationRule}";
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

        [JsonConverter(typeof(FlexibleStringJsonConverter))]
        public string? ResponseStatus { get; set; }
    }

    private sealed class MyMemoryResponseData
    {
        public string? TranslatedText { get; set; }
        public string? DetectedLanguage { get; set; }
    }

    // MyMemory returns responseStatus as a number on success (200) and as a string on errors ("403").
    private sealed class FlexibleStringJsonConverter : JsonConverter<string?>
    {
        public override string? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
            reader.TokenType switch
            {
                JsonTokenType.String => reader.GetString(),
                JsonTokenType.Number => reader.GetInt64().ToString(CultureInfo.InvariantCulture),
                JsonTokenType.Null => null,
                _ => throw new JsonException($"Unexpected token {reader.TokenType} when parsing a string.")
            };

        public override void Write(Utf8JsonWriter writer, string? value, JsonSerializerOptions options) =>
            writer.WriteStringValue(value);
    }
}
