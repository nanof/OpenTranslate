using System.IO;
using System.Text.Json;
using OpenTranslate.Models;

namespace OpenTranslate.Services;

public sealed class UpdateCheckStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = false
    };

    private readonly string _statePath;

    public UpdateCheckStore()
    {
        var folder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "OpenTranslate");
        Directory.CreateDirectory(folder);
        _statePath = Path.Combine(folder, "update-check.json");
    }

    public UpdateCheckState Load()
    {
        if (!File.Exists(_statePath))
            return new UpdateCheckState();

        try
        {
            var json = File.ReadAllText(_statePath);
            return JsonSerializer.Deserialize<UpdateCheckState>(json, JsonOptions) ?? new UpdateCheckState();
        }
        catch
        {
            return new UpdateCheckState();
        }
    }

    public void Save(UpdateCheckState state)
    {
        var json = JsonSerializer.Serialize(state, JsonOptions);
        File.WriteAllText(_statePath, json);
    }
}
