namespace OpenTranslate.Models;

public static class TextImprovementModes
{
    public static readonly IReadOnlyList<TextImprovementOption> SettingsOptions =
    [
        new() { Mode = TextImprovementMode.None, DisplayName = "None (translate as-is)" },
        new() { Mode = TextImprovementMode.Fix, DisplayName = "Fix spelling & grammar" },
        new() { Mode = TextImprovementMode.Natural, DisplayName = "Make it sound natural" },
        new() { Mode = TextImprovementMode.Concise, DisplayName = "Make it concise" },
        new() { Mode = TextImprovementMode.Formal, DisplayName = "Formal tone" },
        new() { Mode = TextImprovementMode.Informal, DisplayName = "Casual tone" },
        new() { Mode = TextImprovementMode.ImproveOnly, DisplayName = "Improve only (don't translate)" }
    ];

    // Backward-compatible alias used by settings.
    public static readonly IReadOnlyList<TextImprovementOption> Options = SettingsOptions;

    public static IReadOnlyList<TextImprovementOption> GetTooltipOptions(AppSettings settings)
    {
        var source = TranslationLanguages.ResolveName(settings.SourceLanguage);
        var target = TranslationLanguages.ResolveName(settings.TargetLanguage);

        var options = new List<TextImprovementOption>(SettingsOptions)
        {
            new() { Mode = TextImprovementMode.Summarize, DisplayName = "Summarize" },
            new() { Mode = TextImprovementMode.ExplainInTarget, DisplayName = $"Explain in {target}" },
            new() { Mode = TextImprovementMode.ExplainInSource, DisplayName = $"Explain in {source}" }
        };

        return options;
    }

    public static TextImprovementOption FromMode(TextImprovementMode mode) =>
        SettingsOptions.FirstOrDefault(option => option.Mode == mode)
        ?? GetTooltipOptions(new AppSettings()).FirstOrDefault(option => option.Mode == mode)
        ?? SettingsOptions[0];

    public static bool IsStandaloneMode(TextImprovementMode mode) =>
        mode is TextImprovementMode.ImproveOnly
            or TextImprovementMode.Summarize
            or TextImprovementMode.ExplainInTarget
            or TextImprovementMode.ExplainInSource;

    // The clause appended to a translation instruction. Returns an empty string for
    // standalone modes (ImproveOnly and tooltip-only variants).
    public static string GetTranslationClause(TextImprovementMode mode) =>
        mode switch
        {
            TextImprovementMode.Fix =>
                " Also correct any spelling, grammar, and punctuation mistakes.",
            TextImprovementMode.Natural =>
                " Make the result sound natural and fluent, the way a native speaker would write it, rather than a literal translation.",
            TextImprovementMode.Concise =>
                " Make the result concise: remove redundancy and wordiness while preserving the meaning.",
            TextImprovementMode.Formal =>
                " Use a formal, professional tone.",
            TextImprovementMode.Informal =>
                " Use a casual, friendly, informal tone.",
            _ => ""
        };
}
