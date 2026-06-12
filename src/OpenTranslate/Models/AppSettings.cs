namespace OpenTranslate.Models;

public sealed class AppSettings
{
    public const string DefaultOpenRouterModel = "google/gemini-2.0-flash-001";
    public const string DefaultOpenAiModel = "gpt-4o-mini";
    public const string DefaultGeminiModel = "gemini-2.0-flash";
    public const double DefaultTooltipFontSize = 13;

    public TranslationProvider Provider { get; set; } = TranslationProvider.OpenRouter;
    public Dictionary<TranslationProvider, string> ApiKeys { get; set; } = [];
    public string Model { get; set; } = DefaultOpenRouterModel;
    public string SourceLanguage { get; set; } = "es";
    public string TargetLanguage { get; set; } = "en";
    public bool AutoDetectLanguage { get; set; }
    public bool StartWithWindows { get; set; }
    public bool PlaySoundOnTranslationStart { get; set; }
    public double TooltipFontSize { get; set; } = DefaultTooltipFontSize;
    public ActivationShortcut ActivationShortcut { get; set; } = ActivationShortcut.Default;

    public string GetApiKey(TranslationProvider provider) =>
        ApiKeys.TryGetValue(provider, out var key) ? key.Trim() : "";

    public string GetActiveApiKey() => GetApiKey(Provider);

    public void SetApiKey(TranslationProvider provider, string apiKey) =>
        ApiKeys[provider] = apiKey.Trim();

    public string GetEffectiveModel() =>
        string.IsNullOrWhiteSpace(Model)
            ? TranslationProviders.GetDefaultModel(Provider)
            : Model.Trim();
}
