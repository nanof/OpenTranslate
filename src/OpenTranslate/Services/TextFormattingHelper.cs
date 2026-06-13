using System.Text.RegularExpressions;

namespace OpenTranslate.Services;

public sealed class TextProtectionContext
{
    internal TextProtectionContext(string text, IReadOnlyList<string> preservedSegments)
    {
        Text = text;
        PreservedSegments = preservedSegments;
    }

    public string Text { get; }

    public IReadOnlyList<string> PreservedSegments { get; }
}

public static class TextFormattingHelper
{
    internal const string BlankLineMarker = "⟦BLANK⟧";
    internal const string PreservationMarkerPrefix = "⟦OT:";

    private static readonly Regex FencedCodeRegex = new(
        "(?s)```.*?```|~~~.*?~~~",
        RegexOptions.Compiled);

    private static readonly Regex InlineCodeRegex = new(
        "`[^`\n]+`",
        RegexOptions.Compiled);

    private static readonly Regex UrlRegex = new(
        @"<(?:https?://|mailto:)[^>\s]+>|(?:https?://|www\.|mailto:)[^\s<>()\[\]""']+",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex MarkdownLinkRegex = new(
        @"\[[^\]]*\]\([^)]+\)",
        RegexOptions.Compiled);

    // Slack: <https://example.com|label> or <https://example.com>
    private static readonly Regex SlackLinkRegex = new(
        @"<(https?://[^>|]+)\|([^>\n]+)>|<(https?://[^>\n]+)>",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex StrikethroughRegex = new(
        @"~([^~\n]+)~",
        RegexOptions.Compiled);

    private static readonly Regex MarkdownDoubleBoldRegex = new(
        @"\*\*([^*\n]+)\*\*",
        RegexOptions.Compiled);

    private static readonly Regex MarkdownDoubleItalicRegex = new(
        @"__([^_\n]+)__",
        RegexOptions.Compiled);

    // Slack *bold* (single asterisk; ** is handled above as Markdown bold).
    private static readonly Regex SingleAsteriskBoldRegex = new(
        @"(?<!\*)\*([^*\n]+)\*(?!\*)",
        RegexOptions.Compiled);

    // Slack _italic_
    private static readonly Regex UnderscoreItalicRegex = new(
        @"(?<![\w_])_([^_\n]+)_(?![\w_])",
        RegexOptions.Compiled);

    private static readonly Regex EmailRegex = new(
        @"\b[A-Za-z0-9._%+-]+@[A-Za-z0-9.-]+\.[A-Za-z]{2,}\b",
        RegexOptions.Compiled);

    // UNC (\\server\share\...) before drive-letter paths.
    private static readonly Regex UncPathRegex = new(
        @"\\(?:[^\s<>""'|\\]+\\)+[^\s<>""'|\\]+(?=[\s.,;:!?)>\]]|$)",
        RegexOptions.Compiled);

    private static readonly Regex WindowsPathRegex = new(
        @"[A-Za-z]:\\(?:[^\\/\s<>:""|?*\n\r]+\\)*[^\\/\s<>:""|?*\n\r]+(?=[\s.,;:!?)>\]]|$)",
        RegexOptions.Compiled);

    private static readonly Regex UnixPathRegex = new(
        @"(?<![@\w.])(?:~(?:/[^\s<>""'`]+)+|/(?:[\w.$~-]+/)+[\w.$~-]+)(?=[\s.,;:!?)>\]]|$)",
        RegexOptions.Compiled);

    private static readonly Regex RelativePathRegex = new(
        @"(?:\./|\.\./)(?:[\w.$~-]+/)*[\w.$~-]+(?=[\s.,;:!?)>\]]|$)",
        RegexOptions.Compiled);

    private static readonly Regex FileNameRegex = new(
        @"(?<![/\\@\w])(?:[\w.-]+\.)+(?:cs|json|xml|yaml|yml|md|txt|py|js|ts|tsx|jsx|html|css|sln|csproj|xaml|exe|dll|png|jpg|jpeg|gif|svg|webp|pdf|zip|tar|gz|config|props|targets|toml|ini|bat|ps1|sh|cpp|h|hpp|go|rs|rb|java|kt|swift|wasm|map|lock|env)(?=[\s.,;:!?)>\]]|$)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex MentionRegex = new(
        @"(?<![\w.@])@(?:here|channel|everyone|[A-Za-z_][\w.-]*)(?![\w.@])",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex HashtagRegex = new(
        @"(?<![\w#])#[A-Za-z_][\w.-]*|(?<![\w#])#\d+\b",
        RegexOptions.Compiled);

    private static readonly Regex UuidRegex = new(
        @"\b[0-9a-fA-F]{8}-(?:[0-9a-fA-F]{4}-){3}[0-9a-fA-F]{12}\b",
        RegexOptions.Compiled);

    private static readonly Regex PlaceholderRegex = new(
        @"\{\{[^{}]+\}\}|\$\{[^{}]+\}|\{[A-Za-z_][A-Za-z0-9_.-]*\}|\{\d+\}|%(?:\d+\$)?[sdifuxXoc%]",
        RegexOptions.Compiled);

    internal static readonly Regex PreservationMarkerRegex = new(
        @"⟦OT:\d+⟧",
        RegexOptions.Compiled);

    private static readonly Regex FuzzyPreservationMarkerRegex = new(
        @"⟦\s*OT\s*:\s*(\d+)\s*⟧",
        RegexOptions.Compiled);

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

    public static TextProtectionContext ProtectForTranslation(string text, bool preserveFormatAndCode)
    {
        var normalized = NormalizeForTranslation(text);
        var preserved = new List<string>();

        if (preserveFormatAndCode)
        {
            normalized = ProtectSegments(normalized, FencedCodeRegex, preserved);
            normalized = ProtectSegments(normalized, InlineCodeRegex, preserved);
            normalized = ProtectSlackLinks(normalized, preserved);
            normalized = ProtectDelimiterPair(normalized, StrikethroughRegex, "~", "~", preserved);
            normalized = ProtectDelimiterPair(normalized, MarkdownDoubleBoldRegex, "**", "**", preserved);
            normalized = ProtectDelimiterPair(normalized, MarkdownDoubleItalicRegex, "__", "__", preserved);
            normalized = ProtectDelimiterPair(normalized, SingleAsteriskBoldRegex, "*", "*", preserved);
            normalized = ProtectDelimiterPair(normalized, UnderscoreItalicRegex, "_", "_", preserved);
            normalized = ProtectSegments(normalized, UrlRegex, preserved);
            normalized = ProtectSegments(normalized, MarkdownLinkRegex, preserved);
            normalized = ProtectSegments(normalized, EmailRegex, preserved);
            normalized = ProtectSegments(normalized, UncPathRegex, preserved);
            normalized = ProtectSegments(normalized, WindowsPathRegex, preserved);
            normalized = ProtectSegments(normalized, UnixPathRegex, preserved);
            normalized = ProtectSegments(normalized, RelativePathRegex, preserved);
            normalized = ProtectSegments(normalized, FileNameRegex, preserved);
            normalized = ProtectSegments(normalized, MentionRegex, preserved);
            normalized = ProtectSegments(normalized, HashtagRegex, preserved);
            normalized = ProtectSegments(normalized, UuidRegex, preserved);
            normalized = ProtectSegments(normalized, PlaceholderRegex, preserved);
        }

        normalized = ProtectBlankLines(normalized);
        return new TextProtectionContext(normalized, preserved);
    }

    public static string RestoreFromTranslation(string text, TextProtectionContext protection)
    {
        var restored = RestorePreservedSegments(text, protection.PreservedSegments);
        return RestoreBlankLines(restored);
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

    public static string GetPreservationPromptRule(bool preserveFormatAndCode)
    {
        if (!preserveFormatAndCode)
            return "";

        return " Do not translate or alter code spans (backticks), rich-text markers (such as * or _ for bold/italic), " +
               "URLs, file paths, file names, email addresses, @mentions, #hashtags, UUIDs, " +
               "placeholder tokens such as {name}, ${var}, %s, or {0}, " +
               "or any " +
               $"{PreservationMarkerPrefix}n{PreservationMarkerSuffix} placeholders — copy them exactly.";
    }

    private const char PreservationMarkerSuffix = '⟧';

    private static string ProtectSegments(string text, Regex pattern, List<string> preserved) =>
        pattern.Replace(text, match =>
        {
            var index = preserved.Count;
            preserved.Add(match.Value);
            return FormatPreservationMarker(index);
        });

    // Keeps opening/closing delimiters intact while the inner text is translated.
    private static string ProtectDelimiterPair(
        string text,
        Regex pattern,
        string open,
        string close,
        List<string> preserved) =>
        pattern.Replace(text, match =>
        {
            var openMarker = FormatPreservationMarker(preserved.Count);
            preserved.Add(open);
            var closeMarker = FormatPreservationMarker(preserved.Count);
            preserved.Add(close);
            return $"{openMarker}{match.Groups[1].Value}{closeMarker}";
        });

    private static string ProtectSlackLinks(string text, List<string> preserved) =>
        SlackLinkRegex.Replace(text, match =>
        {
            if (match.Groups[2].Success)
            {
                var open = FormatPreservationMarker(preserved.Count);
                preserved.Add("<");
                var url = FormatPreservationMarker(preserved.Count);
                preserved.Add(match.Groups[1].Value);
                var pipe = FormatPreservationMarker(preserved.Count);
                preserved.Add("|");
                var close = FormatPreservationMarker(preserved.Count);
                preserved.Add(">");
                return $"{open}{url}{pipe}{match.Groups[2].Value}{close}";
            }

            var openOnly = FormatPreservationMarker(preserved.Count);
            preserved.Add("<");
            var urlOnly = FormatPreservationMarker(preserved.Count);
            preserved.Add(match.Groups[3].Value);
            var closeOnly = FormatPreservationMarker(preserved.Count);
            preserved.Add(">");
            return $"{openOnly}{urlOnly}{closeOnly}";
        });

    internal static string FormatPreservationMarker(int index) =>
        $"{PreservationMarkerPrefix}{index}{PreservationMarkerSuffix}";

    private static string RestorePreservedSegments(string text, IReadOnlyList<string> preserved)
    {
        if (preserved.Count == 0)
            return text;

        text = FuzzyPreservationMarkerRegex.Replace(
            text,
            match => FormatPreservationMarker(int.Parse(match.Groups[1].Value)));

        for (var i = preserved.Count - 1; i >= 0; i--)
        {
            text = text.Replace(
                FormatPreservationMarker(i),
                preserved[i],
                StringComparison.Ordinal);
        }

        return text;
    }

    private static string TrimTrailingSpacesOnly(string text)
    {
        var end = text.Length;
        while (end > 0 && text[end - 1] is ' ' or '\t')
            end--;

        return end == text.Length ? text : text[..end];
    }
}
