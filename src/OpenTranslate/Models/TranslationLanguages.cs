namespace OpenTranslate.Models;

public static class TranslationLanguages
{
    private static readonly TranslationLanguage[] All =
    [
        new("es", "Español"),
        new("en", "English"),
        new("fr", "Français"),
        new("de", "Deutsch"),
        new("pt", "Português"),
        new("it", "Italiano"),
        new("ca", "Català"),
        new("gl", "Galego"),
        new("nl", "Nederlands"),
        new("pl", "Polski"),
        new("ru", "Русский"),
        new("uk", "Українська"),
        new("zh", "中文"),
        new("ja", "日本語"),
        new("ko", "한국어"),
        new("ar", "العربية"),
        new("he", "עברית"),
        new("hi", "हिन्दी"),
        new("tr", "Türkçe"),
        new("sv", "Svenska"),
        new("da", "Dansk"),
        new("no", "Norsk"),
        new("fi", "Suomi"),
        new("cs", "Čeština"),
        new("ro", "Română"),
        new("hu", "Magyar"),
        new("el", "Ελληνικά"),
        new("id", "Bahasa Indonesia"),
        new("vi", "Tiếng Việt"),
        new("th", "ไทย")
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
