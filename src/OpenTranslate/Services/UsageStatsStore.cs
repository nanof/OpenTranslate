using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using OpenTranslate.Models;

namespace OpenTranslate.Services;

public sealed class UsageStatsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = false
    };

    private readonly string _statsPath;

    public UsageStatsStore()
    {
        var folder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "OpenTranslate");
        Directory.CreateDirectory(folder);
        _statsPath = Path.Combine(folder, "usage.dat");
    }

    public UsageStats Load()
    {
        if (!File.Exists(_statsPath))
            return new UsageStats();

        try
        {
            var encrypted = File.ReadAllBytes(_statsPath);
            var plain = ProtectedData.Unprotect(encrypted, null, DataProtectionScope.CurrentUser);
            var json = Encoding.UTF8.GetString(plain);
            return JsonSerializer.Deserialize<UsageStats>(json, JsonOptions) ?? new UsageStats();
        }
        catch
        {
            return new UsageStats();
        }
    }

    public void Save(UsageStats stats)
    {
        var json = JsonSerializer.Serialize(stats, JsonOptions);
        var plain = Encoding.UTF8.GetBytes(json);
        var encrypted = ProtectedData.Protect(plain, null, DataProtectionScope.CurrentUser);
        File.WriteAllBytes(_statsPath, encrypted);
    }
}
