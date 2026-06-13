using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using OpenTranslate.Models;

namespace OpenTranslate.Services;

public sealed class SecureSettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = false,
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly string _settingsPath;

    public SecureSettingsStore()
    {
        var folder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "OpenTranslate");
        Directory.CreateDirectory(folder);
        _settingsPath = Path.Combine(folder, "settings.dat");
    }

    public AppSettings Load()
    {
        if (!File.Exists(_settingsPath))
            return new AppSettings();

        try
        {
            var encrypted = File.ReadAllBytes(_settingsPath);
            var plain = ProtectedData.Unprotect(encrypted, null, DataProtectionScope.CurrentUser);
            var json = Encoding.UTF8.GetString(plain);
            var settings = JsonSerializer.Deserialize<AppSettings>(json, JsonOptions) ?? new AppSettings();

            if (!json.Contains("ActivationShortcut", StringComparison.Ordinal)
                && json.Contains("DoubleCopyWindowMs", StringComparison.Ordinal))
            {
                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.TryGetProperty("DoubleCopyWindowMs", out var windowMs))
                    settings.ActivationShortcut.DoublePressWindowMs = windowMs.GetInt32();
            }

            if (settings.TooltipFontSize <= 0)
                settings.TooltipFontSize = AppSettings.DefaultTooltipFontSize;

            if (settings.TooltipWidth > 0)
                settings.TooltipWidth = Math.Clamp(
                    settings.TooltipWidth,
                    AppSettings.MinTooltipWidth,
                    AppSettings.MaxTooltipWidth);

            if (settings.TooltipHeight > 0)
                settings.TooltipHeight = Math.Clamp(
                    settings.TooltipHeight,
                    AppSettings.MinTooltipHeight,
                    AppSettings.MaxTooltipHeight);

            if (!json.Contains("ApiKeys", StringComparison.Ordinal)
                && json.Contains("\"ApiKey\"", StringComparison.Ordinal))
            {
                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.TryGetProperty("ApiKey", out var legacyApiKey))
                {
                    var key = legacyApiKey.GetString();
                    if (!string.IsNullOrWhiteSpace(key))
                        settings.SetApiKey(settings.Provider, key);
                }
            }

            return settings;
        }
        catch
        {
            return new AppSettings();
        }
    }

    public void Save(AppSettings settings)
    {
        var json = JsonSerializer.Serialize(settings, JsonOptions);
        var plain = Encoding.UTF8.GetBytes(json);
        var encrypted = ProtectedData.Protect(plain, null, DataProtectionScope.CurrentUser);
        File.WriteAllBytes(_settingsPath, encrypted);
    }
}
