namespace OpenTranslate.Models;

public static class TextImprovementModes
{
    public static readonly IReadOnlyList<TextImprovementOption> Options =
    [
        new() { Mode = TextImprovementMode.None, DisplayName = "None (translate as-is)" },
        new() { Mode = TextImprovementMode.Fix, DisplayName = "Fix spelling & grammar" },
        new() { Mode = TextImprovementMode.Natural, DisplayName = "Make it sound natural" },
        new() { Mode = TextImprovementMode.Concise, DisplayName = "Make it concise" },
        new() { Mode = TextImprovementMode.Formal, DisplayName = "Formal tone" },
        new() { Mode = TextImprovementMode.Informal, DisplayName = "Casual tone" },
        new() { Mode = TextImprovementMode.ImproveOnly, DisplayName = "Improve only (don't translate)" }
    ];

    public static TextImprovementOption FromMode(TextImprovementMode mode) =>
        Options.FirstOrDefault(option => option.Mode == mode) ?? Options[0];

    // The clause appended to a translation instruction. Returns an empty string for
    // None and ImproveOnly (the latter is handled as a standalone instruction).
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
