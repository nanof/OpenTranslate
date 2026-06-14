namespace OpenTranslate.Models;

public sealed class TooltipPlacementOption
{
    public required TooltipPlacement Placement { get; init; }
    public required string DisplayName { get; init; }
}

public static class TooltipPlacementOptions
{
    public static readonly IReadOnlyList<TooltipPlacementOption> All =
    [
        new() { Placement = TooltipPlacement.Floating, DisplayName = "Floating (follow cursor)" },
        new() { Placement = TooltipPlacement.BottomRight, DisplayName = "Bottom-right corner (notifications)" }
    ];

    public static TooltipPlacementOption FromPlacement(TooltipPlacement placement) =>
        All.FirstOrDefault(option => option.Placement == placement) ?? All[0];
}
