namespace OpenTranslate.Models;

public sealed class UpdateInfo
{
    public required Version Version { get; init; }
    public required string DownloadUrl { get; init; }
    public required string ReleasePageUrl { get; init; }
}

public sealed class UpdateCheckResult
{
    public UpdateInfo? AvailableUpdate { get; init; }
    public bool IsUpdateAvailable => AvailableUpdate is not null;
}
