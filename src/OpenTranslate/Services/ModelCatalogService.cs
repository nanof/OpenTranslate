using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using OpenTranslate.Models;

namespace OpenTranslate.Services;

public sealed class ModelCatalogService : IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly HttpClient _httpClient;

    public ModelCatalogService()
    {
        _httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(30)
        };
        _httpClient.DefaultRequestHeaders.Accept.Add(
            new MediaTypeWithQualityHeaderValue("application/json"));
    }

    public async Task<IReadOnlyList<ModelOption>> GetModelsAsync(
        TranslationProvider provider,
        string? apiKey,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var models = provider switch
            {
                TranslationProvider.OpenRouter => await FetchOpenRouterModelsAsync(cancellationToken),
                TranslationProvider.OpenAi => await FetchOpenAiModelsAsync(apiKey, cancellationToken),
                TranslationProvider.Gemini => await FetchGeminiModelsAsync(apiKey, cancellationToken),
                _ => GetFallbackModels(provider)
            };

            return SortModels(models, TranslationProviders.GetDefaultModel(provider));
        }
        catch
        {
            return GetFallbackModels(provider);
        }
    }

    private async Task<IReadOnlyList<ModelOption>> FetchOpenRouterModelsAsync(CancellationToken cancellationToken)
    {
        using var response = await _httpClient.GetAsync(
            "https://openrouter.ai/api/v1/models",
            cancellationToken);

        if (!response.IsSuccessStatusCode)
            return GetFallbackModels(TranslationProvider.OpenRouter);

        var payload = await response.Content.ReadFromJsonAsync<OpenRouterModelsResponse>(JsonOptions, cancellationToken);
        var models = payload?.Data?
            .Where(model => !string.IsNullOrWhiteSpace(model.Id))
            .Select(model => new ModelOption
            {
                Id = model.Id!.Trim(),
                Description = string.IsNullOrWhiteSpace(model.Name) ? null : model.Name.Trim()
            })
            .ToList();

        return models is { Count: > 0 }
            ? models
            : GetFallbackModels(TranslationProvider.OpenRouter);
    }

    private async Task<IReadOnlyList<ModelOption>> FetchOpenAiModelsAsync(
        string? apiKey,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(apiKey))
            return GetFallbackModels(TranslationProvider.OpenAi);

        using var request = new HttpRequestMessage(HttpMethod.Get, "https://api.openai.com/v1/models");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey.Trim());

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
            return GetFallbackModels(TranslationProvider.OpenAi);

        var payload = await response.Content.ReadFromJsonAsync<OpenAiModelsResponse>(JsonOptions, cancellationToken);
        var models = payload?.Data?
            .Where(model => IsOpenAiChatModel(model.Id))
            .Select(model => new ModelOption { Id = model.Id!.Trim() })
            .ToList();

        return models is { Count: > 0 }
            ? models
            : GetFallbackModels(TranslationProvider.OpenAi);
    }

    private async Task<IReadOnlyList<ModelOption>> FetchGeminiModelsAsync(
        string? apiKey,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(apiKey))
            return GetFallbackModels(TranslationProvider.Gemini);

        var escapedKey = Uri.EscapeDataString(apiKey.Trim());
        using var response = await _httpClient.GetAsync(
            $"https://generativelanguage.googleapis.com/v1beta/models?key={escapedKey}",
            cancellationToken);

        if (!response.IsSuccessStatusCode)
            return GetFallbackModels(TranslationProvider.Gemini);

        var payload = await response.Content.ReadFromJsonAsync<GeminiModelsResponse>(JsonOptions, cancellationToken);
        var models = payload?.Models?
            .Where(model =>
                model.SupportedGenerationMethods?.Contains("generateContent", StringComparer.OrdinalIgnoreCase) == true
                && !string.IsNullOrWhiteSpace(model.Name))
            .Select(model => new ModelOption
            {
                Id = NormalizeGeminiModelId(model.Name!),
                Description = string.IsNullOrWhiteSpace(model.DisplayName) ? null : model.DisplayName.Trim()
            })
            .Where(model => !string.IsNullOrWhiteSpace(model.Id))
            .GroupBy(model => model.Id, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToList();

        return models is { Count: > 0 }
            ? models
            : GetFallbackModels(TranslationProvider.Gemini);
    }

    private static bool IsOpenAiChatModel(string? id)
    {
        if (string.IsNullOrWhiteSpace(id))
            return false;

        if (id.Contains("realtime", StringComparison.OrdinalIgnoreCase)
            || id.Contains("audio", StringComparison.OrdinalIgnoreCase)
            || id.Contains("transcribe", StringComparison.OrdinalIgnoreCase)
            || id.Contains("search", StringComparison.OrdinalIgnoreCase)
            || id.Contains("computer", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return id.StartsWith("gpt-", StringComparison.OrdinalIgnoreCase)
            || id.StartsWith("chatgpt-", StringComparison.OrdinalIgnoreCase)
            || id.StartsWith("o1", StringComparison.OrdinalIgnoreCase)
            || id.StartsWith("o3", StringComparison.OrdinalIgnoreCase)
            || id.StartsWith("o4", StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeGeminiModelId(string name)
    {
        const string prefix = "models/";
        return name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
            ? name[prefix.Length..]
            : name;
    }

    private static IReadOnlyList<ModelOption> GetFallbackModels(TranslationProvider provider) =>
        provider switch
        {
            TranslationProvider.OpenAi =>
            [
                new() { Id = "gpt-4o-mini", Description = "Fast and affordable" },
                new() { Id = "gpt-4o", Description = "High quality" },
                new() { Id = "gpt-4.1-mini", Description = "Latest mini model" },
                new() { Id = "gpt-4.1", Description = "Latest flagship" },
                new() { Id = "o3-mini", Description = "Reasoning model" }
            ],
            TranslationProvider.Gemini =>
            [
                new() { Id = "gemini-3.1-flash-lite", Description = "Fast default" },
                new() { Id = "gemini-3.5-flash", Description = "Higher quality" }
            ],
            _ =>
            [
                new() { Id = "google/gemini-3.1-flash-lite", Description = "Google Gemini 3.1 Flash-Lite" },
                new() { Id = "google/gemini-3.5-flash", Description = "Google Gemini 3.5 Flash" },
                new() { Id = "openai/gpt-4o-mini", Description = "OpenAI GPT-4o mini" },
                new() { Id = "anthropic/claude-haiku-4.5", Description = "Anthropic Claude Haiku 4.5" },
                new() { Id = "meta-llama/llama-3.3-70b-instruct", Description = "Meta Llama 3.3 70B" }
            ]
        };

    private static IReadOnlyList<ModelOption> SortModels(IReadOnlyList<ModelOption> models, string defaultModelId)
    {
        return models
            .OrderBy(model => string.Equals(model.Id, defaultModelId, StringComparison.OrdinalIgnoreCase) ? 0 : 1)
            .ThenBy(model => model.Id, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public void Dispose() => _httpClient.Dispose();

    private sealed class OpenRouterModelsResponse
    {
        public List<OpenRouterModel>? Data { get; set; }
    }

    private sealed class OpenRouterModel
    {
        public string? Id { get; set; }
        public string? Name { get; set; }
    }

    private sealed class OpenAiModelsResponse
    {
        public List<OpenAiModel>? Data { get; set; }
    }

    private sealed class OpenAiModel
    {
        public string? Id { get; set; }
    }

    private sealed class GeminiModelsResponse
    {
        public List<GeminiModel>? Models { get; set; }
    }

    private sealed class GeminiModel
    {
        public string? Name { get; set; }
        public string? DisplayName { get; set; }
        public List<string>? SupportedGenerationMethods { get; set; }
    }
}
