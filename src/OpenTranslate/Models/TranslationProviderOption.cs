namespace OpenTranslate.Models;

public sealed class TranslationProviderOption
{
    public TranslationProvider Provider { get; init; }
    public string DisplayName { get; init; } = "";
}
