using System.Text.RegularExpressions;

namespace OpenTranslate.Services;

public static class TextFormattingHelper
{
    internal const string BlankLineMarker = "⟦BLANK⟧";

    public static string NormalizeLineEndings(string text) =>
        text
            .Replace("\r\n", "\n")
            .Replace("\r", "\n")
            .Replace("\u2029", "\n\n")
            .Replace("\u2028", "\n");

    public static string NormalizeForTranslation(string text)
    {
        if (string.IsNullOrEmpty(text))
            return text;

        return TrimTrailingSpacesOnly(NormalizeLineEndings(text));
    }

    public static string ProtectBlankLines(string text)
    {
        var normalized = NormalizeForTranslation(text);
        return Regex.Replace(normalized, @"\n[ \t]*\n", $"\n{BlankLineMarker}\n");
    }

    public static string RestoreBlankLines(string text)
    {
        var normalized = NormalizeForTranslation(text);
        return Regex.Replace(normalized, $@"\n\s*{Regex.Escape(BlankLineMarker)}\s*\n", "\n\n");
    }

    private static string TrimTrailingSpacesOnly(string text)
    {
        var end = text.Length;
        while (end > 0 && text[end - 1] is ' ' or '\t')
            end--;

        return end == text.Length ? text : text[..end];
    }
}
