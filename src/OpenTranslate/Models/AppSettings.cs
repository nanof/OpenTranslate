namespace OpenTranslate.Models;

public sealed class AppSettings
{
    public const string DefaultModel = "google/gemini-2.0-flash-001";
    public const double DefaultTooltipFontSize = 13;

    public string ApiKey { get; set; } = "";
    public string Model { get; set; } = DefaultModel;
    public string SourceLanguage { get; set; } = "es";
    public string TargetLanguage { get; set; } = "en";
    public bool AutoDetectLanguage { get; set; }
    public bool StartWithWindows { get; set; }
    public bool PlaySoundOnTranslationStart { get; set; }
    public double TooltipFontSize { get; set; } = DefaultTooltipFontSize;
    public ActivationShortcut ActivationShortcut { get; set; } = ActivationShortcut.Default;
}
