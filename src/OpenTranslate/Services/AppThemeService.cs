using System.Windows;
using System.Windows.Media;
using Microsoft.Win32;
using OpenTranslate.Models;

namespace OpenTranslate.Services;

public sealed class AppThemeService : IDisposable
{
    public static AppThemeService Instance { get; } = new();

    public event EventHandler? ThemeChanged;

    public bool IsDarkEffective { get; private set; } = true;

    private AppThemePreference _preference = AppThemePreference.Dark;
    private bool _systemEventsHooked;

    private AppThemeService()
    {
    }

    public void Initialize(AppThemePreference preference)
    {
        _preference = preference;
        Apply(preference);
        UpdateSystemEventsHook();
    }

    public void Apply(AppThemePreference preference)
    {
        _preference = preference;
        ApplyPalette(ResolveIsDark(preference));
        UpdateSystemEventsHook();
    }

    public static System.Windows.Media.Brush GetBrush(string resourceKey)
    {
        if (System.Windows.Application.Current?.TryFindResource(resourceKey) is System.Windows.Media.Brush brush)
            return brush;

        return System.Windows.Media.Brushes.Gray;
    }

    private bool ResolveIsDark(AppThemePreference preference) =>
        preference switch
        {
            AppThemePreference.Light => false,
            AppThemePreference.Dark => true,
            _ => !IsWindowsLightTheme()
        };

    private static bool IsWindowsLightTheme()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            var value = key?.GetValue("AppsUseLightTheme");
            return value is int light && light == 1;
        }
        catch
        {
            return true;
        }
    }

    private void ApplyPalette(bool isDark)
    {
        IsDarkEffective = isDark;

        var app = System.Windows.Application.Current
            ?? throw new InvalidOperationException("Application is not initialized.");

        var palette = isDark ? DarkPalette : LightPalette;
        foreach (var (key, color) in palette)
            app.Resources[key] = CreateBrush(color);

        ThemeChanged?.Invoke(this, EventArgs.Empty);
    }

    private static SolidColorBrush CreateBrush(string hex)
    {
        var brush = new SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(hex)!);
        if (brush.CanFreeze)
            brush.Freeze();

        return brush;
    }

    private void UpdateSystemEventsHook()
    {
        if (_preference == AppThemePreference.System)
        {
            if (_systemEventsHooked)
                return;

            SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
            _systemEventsHooked = true;
            return;
        }

        if (!_systemEventsHooked)
            return;

        SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
        _systemEventsHooked = false;
    }

    private void OnUserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
    {
        if (_preference != AppThemePreference.System)
            return;

        if (e.Category != UserPreferenceCategory.General)
            return;

        System.Windows.Application.Current?.Dispatcher.Invoke(() =>
            ApplyPalette(ResolveIsDark(_preference)));
    }

    public void Dispose()
    {
        if (!_systemEventsHooked)
            return;

        SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
        _systemEventsHooked = false;
    }

    private static readonly (string Key, string Color)[] DarkPalette =
    [
        ("ThemeWindowBg", "#1E1E2E"),
        ("ThemeSurface", "#313244"),
        ("ThemeSurfaceHover", "#45475A"),
        ("ThemeSurfaceMuted", "#585B70"),
        ("ThemeForeground", "#CDD6F4"),
        ("ThemeForegroundMuted", "#A6ADC8"),
        ("ThemeForegroundSubtle", "#BAC2DE"),
        ("ThemeForegroundDim", "#6C7086"),
        ("ThemeBorder", "#45475A"),
        ("ThemeAccent", "#89B4FA"),
        ("ThemeAccentHoverAlt", "#B4BEFE"),
        ("ThemeAccentPressedAlt", "#74C7EC"),
        ("ThemeAccentForeground", "#1E1E2E"),
        ("ThemeSuccess", "#A6E3A1"),
        ("ThemeError", "#F38BA8"),
        ("ThemeModesPanel", "#272736"),
        ("ThemeSpinner", "#00FF41"),
        ("ThemeDefaultButtonRing", "#F5E0DC"),
        ("ThemeModeHover", "#45475A")
    ];

    private static readonly (string Key, string Color)[] LightPalette =
    [
        ("ThemeWindowBg", "#EFF1F5"),
        ("ThemeSurface", "#FFFFFF"),
        ("ThemeSurfaceHover", "#DCE0E8"),
        ("ThemeSurfaceMuted", "#CCD0DA"),
        ("ThemeForeground", "#4C4F69"),
        ("ThemeForegroundMuted", "#6C6F85"),
        ("ThemeForegroundSubtle", "#5C5F77"),
        ("ThemeForegroundDim", "#9CA0B0"),
        ("ThemeBorder", "#CCD0DA"),
        ("ThemeAccent", "#1E66F5"),
        ("ThemeAccentHoverAlt", "#3584E4"),
        ("ThemeAccentPressedAlt", "#104CE6"),
        ("ThemeAccentForeground", "#FFFFFF"),
        ("ThemeSuccess", "#40A02B"),
        ("ThemeError", "#D20F39"),
        ("ThemeModesPanel", "#E6E9EF"),
        ("ThemeSpinner", "#15803D"),
        ("ThemeDefaultButtonRing", "#1E66F5"),
        ("ThemeModeHover", "#DCE0E8")
    ];
}
