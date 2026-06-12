namespace OpenTranslate.Models;

public sealed class ModelOption
{
    public string Id { get; init; } = "";
    public string? Description { get; init; }

    public bool MatchesFilter(string filter)
    {
        if (string.IsNullOrWhiteSpace(filter))
            return true;

        return Id.Contains(filter, StringComparison.OrdinalIgnoreCase)
            || (Description?.Contains(filter, StringComparison.OrdinalIgnoreCase) ?? false);
    }

    public override string ToString() => Id;
}
