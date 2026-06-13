namespace OpenTranslate.Models;

public sealed class AppSettings
{
    public const string DefaultOpenRouterModel = "google/gemini-3.1-flash-lite";
    public const string DefaultOpenAiModel = "gpt-4o-mini";
    public const string DefaultGeminiModel = "gemini-3.1-flash-lite";
    public const double DefaultTooltipFontSize = 13;

    public TranslationProvider Provider { get; set; } = TranslationProvider.MyMemory;
    public Dictionary<TranslationProvider, string> ApiKeys { get; set; } = [];
    public string Model { get; set; } = DefaultOpenRouterModel;
    public string SourceLanguage { get; set; } = "es";
    public string TargetLanguage { get; set; } = "en";
    public bool AutoDetectLanguage { get; set; }
    public TextImprovementMode ImprovementMode { get; set; } = TextImprovementMode.None;
    public bool StartWithWindows { get; set; }
    public bool PlaySoundOnTranslationStart { get; set; }
    public bool TypewriterPaste { get; set; } = true;
    public bool PreserveFormatAndCode { get; set; } = true;
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

    // Produces a copy with a different improvement mode, used to preview how the same
    // source text reads under each mode without mutating the persisted settings.
    public AppSettings WithImprovementMode(TextImprovementMode mode) => new()
    {
        Provider = Provider,
        ApiKeys = new Dictionary<TranslationProvider, string>(ApiKeys),
        Model = Model,
        SourceLanguage = SourceLanguage,
        TargetLanguage = TargetLanguage,
        AutoDetectLanguage = AutoDetectLanguage,
        ImprovementMode = mode,
        StartWithWindows = StartWithWindows,
        PlaySoundOnTranslationStart = PlaySoundOnTranslationStart,
        TypewriterPaste = TypewriterPaste,
        PreserveFormatAndCode = PreserveFormatAndCode,
        TooltipFontSize = TooltipFontSize,
        ActivationShortcut = ActivationShortcut
    };
}
