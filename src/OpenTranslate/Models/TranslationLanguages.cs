namespace OpenTranslate.Models;

public static class TranslationLanguages
{
    private static readonly TranslationLanguage[] All =
    [
        new("es", "Spanish"),
        new("en", "English"),
        new("fr", "French"),
        new("de", "German"),
        new("pt", "Portuguese"),
        new("it", "Italian"),
        new("ca", "Catalan"),
        new("gl", "Galician"),
        new("nl", "Dutch"),
        new("pl", "Polish"),
        new("ru", "Russian"),
        new("uk", "Ukrainian"),
        new("zh", "Chinese"),
        new("ja", "Japanese"),
        new("ko", "Korean"),
        new("ar", "Arabic"),
        new("he", "Hebrew"),
        new("hi", "Hindi"),
        new("tr", "Turkish"),
        new("sv", "Swedish"),
        new("da", "Danish"),
        new("no", "Norwegian"),
        new("fi", "Finnish"),
        new("cs", "Czech"),
        new("ro", "Romanian"),
        new("hu", "Hungarian"),
        new("el", "Greek"),
        new("id", "Indonesian"),
        new("vi", "Vietnamese"),
        new("th", "Thai")
    ];

    public static IReadOnlyList<TranslationLanguage> Supported { get; } = All;

    public static string ResolveName(string? code)
    {
        if (string.IsNullOrWhiteSpace(code))
            return code ?? "";

        var normalized = code.Trim().ToLowerInvariant();
        return All.FirstOrDefault(language =>
            string.Equals(language.Code, normalized, StringComparison.OrdinalIgnoreCase))?.Name
            ?? normalized;
    }

    public static IReadOnlyList<TranslationLanguage> BuildOptions(string? sourceCode, string? targetCode)
    {
        var options = new List<TranslationLanguage>(All);
        AddCustomLanguage(options, sourceCode);
        AddCustomLanguage(options, targetCode);
        return options
            .OrderBy(language => language.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    private static void AddCustomLanguage(List<TranslationLanguage> options, string? code)
    {
        if (string.IsNullOrWhiteSpace(code))
            return;

        var normalized = code.Trim().ToLowerInvariant();
        if (options.Any(language =>
                string.Equals(language.Code, normalized, StringComparison.OrdinalIgnoreCase)))
            return;

        options.Add(new TranslationLanguage(normalized, normalized.ToUpperInvariant()));
    }
}
