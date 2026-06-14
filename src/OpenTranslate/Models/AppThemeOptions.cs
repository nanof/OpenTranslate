namespace OpenTranslate.Models;

public sealed class AppThemeOption
{
    public required AppThemePreference Preference { get; init; }
    public required string DisplayName { get; init; }
}

public static class AppThemeOptions
{
    public static readonly IReadOnlyList<AppThemeOption> All =
    [
        new() { Preference = AppThemePreference.System, DisplayName = "Use Windows setting" },
        new() { Preference = AppThemePreference.Dark, DisplayName = "Dark" },
        new() { Preference = AppThemePreference.Light, DisplayName = "Light" }
    ];

    public static AppThemeOption FromPreference(AppThemePreference preference) =>
        All.FirstOrDefault(option => option.Preference == preference) ?? All[1];
}
