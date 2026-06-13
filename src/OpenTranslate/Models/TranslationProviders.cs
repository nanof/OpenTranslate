namespace OpenTranslate.Models;

public static class TranslationProviders
{
    public static readonly IReadOnlyList<TranslationProviderOption> Options =
    [
        new() { Provider = TranslationProvider.MyMemory, DisplayName = "MyMemory (free, no API key)" },
        new() { Provider = TranslationProvider.OpenRouter, DisplayName = "OpenRouter" },
        new() { Provider = TranslationProvider.OpenAi, DisplayName = "OpenAI" },
        new() { Provider = TranslationProvider.Gemini, DisplayName = "Gemini (Google)" }
    ];

    public static string GetDefaultModel(TranslationProvider provider) =>
        provider switch
        {
            TranslationProvider.MyMemory => "",
            TranslationProvider.OpenAi => AppSettings.DefaultOpenAiModel,
            TranslationProvider.Gemini => AppSettings.DefaultGeminiModel,
            _ => AppSettings.DefaultOpenRouterModel
        };

    public static string GetDisplayName(TranslationProvider provider) =>
        provider switch
        {
            TranslationProvider.MyMemory => "MyMemory",
            TranslationProvider.OpenAi => "OpenAI",
            TranslationProvider.Gemini => "Gemini",
            _ => "OpenRouter"
        };

    public static bool RequiresApiKey(TranslationProvider provider) =>
        provider != TranslationProvider.MyMemory;

    public static bool SupportsModelSelection(TranslationProvider provider) =>
        provider != TranslationProvider.MyMemory;

    public static bool SupportsAutoDetect(TranslationProvider provider) =>
        provider != TranslationProvider.MyMemory;

    public static string GetApiKeyLabel(TranslationProvider provider) =>
        provider switch
        {
            TranslationProvider.MyMemory => "No API key required (free)",
            TranslationProvider.OpenAi => "OpenAI API key",
            TranslationProvider.Gemini => "Gemini API key",
            _ => "OpenRouter API key"
        };

    public static string GetApiKeyMissingMessage(TranslationProvider provider) =>
        $"Configure your {GetDisplayName(provider)} API key in Settings.";

    public static string GetInvalidApiKeyMessage(TranslationProvider provider) =>
        $"The {GetDisplayName(provider)} API key is invalid or has expired.";

    public static string GetRateLimitMessage(TranslationProvider provider) =>
        $"{GetDisplayName(provider)} rate-limited the request. Try again in a few seconds.";

    public static string GetErrorMessage(TranslationProvider provider, int statusCode, string details) =>
        $"{GetDisplayName(provider)} error ({statusCode}): {details}";

    public static string GetEmptyResponseMessage(TranslationProvider provider) =>
        $"{GetDisplayName(provider)} returned an empty response.";

    public static bool IsModelCompatibleWithProvider(string? model, TranslationProvider provider)
    {
        if (provider == TranslationProvider.MyMemory)
            return true;

        var trimmed = model?.Trim() ?? "";
        if (string.IsNullOrWhiteSpace(trimmed))
            return false;

        return provider switch
        {
            TranslationProvider.Gemini =>
                !trimmed.Contains('/')
                && trimmed.StartsWith("gemini", StringComparison.OrdinalIgnoreCase),
            TranslationProvider.OpenAi =>
                !trimmed.Contains('/')
                && (trimmed.StartsWith("gpt", StringComparison.OrdinalIgnoreCase)
                    || trimmed.StartsWith("chatgpt", StringComparison.OrdinalIgnoreCase)
                    || trimmed.StartsWith("o1", StringComparison.OrdinalIgnoreCase)
                    || trimmed.StartsWith("o3", StringComparison.OrdinalIgnoreCase)
                    || trimmed.StartsWith("o4", StringComparison.OrdinalIgnoreCase)),
            _ => trimmed.Contains('/')
        };
    }
}
