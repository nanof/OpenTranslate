namespace OpenTranslate.Models;

public sealed class TextImprovementOption
{
    public TextImprovementMode Mode { get; init; }
    public string DisplayName { get; init; } = "";

    public override string ToString() => DisplayName;
}
