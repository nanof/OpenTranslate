using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using OpenTranslate.Models;

namespace OpenTranslate.Services;

public sealed class UpdateCheckService : IDisposable
{
    public const string Repository = "nanof/OpenTranslate";

    private static readonly TimeSpan SilentCheckInterval = TimeSpan.FromHours(24);
    private const int StartupDelayMs = 8000;

    private readonly HttpClient _httpClient;
    private readonly UpdateCheckStore _store;
    private readonly object _sync = new();

    public UpdateInfo? PendingUpdate { get; private set; }

    public event EventHandler<UpdateInfo>? UpdateAvailable;

    public event EventHandler<UpdateInfo?>? PendingUpdateChanged;

    public UpdateCheckService(UpdateCheckStore store)
    {
        _store = store;
        _httpClient = new HttpClient
        {
            BaseAddress = new Uri("https://api.github.com/"),
            Timeout = TimeSpan.FromSeconds(15)
        };
        _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd($"OpenTranslate/{AppVersionHelper.CurrentDisplay}");
        _httpClient.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
    }

    public async Task<UpdateCheckResult> CheckAsync(
        bool force = false,
        CancellationToken cancellationToken = default)
    {
        if (!force && !ShouldCheckNow())
            return new UpdateCheckResult { AvailableUpdate = PendingUpdate };

        var release = await FetchLatestReleaseAsync(cancellationToken).ConfigureAwait(false);
        RecordCheck();

        if (release is null)
            return new UpdateCheckResult { AvailableUpdate = PendingUpdate };

        var update = ParseUpdate(release);
        lock (_sync)
            PendingUpdate = update;

        PendingUpdateChanged?.Invoke(this, update);
        return new UpdateCheckResult { AvailableUpdate = update };
    }

    public async Task CheckSilentlyOnStartupAsync(CancellationToken cancellationToken = default)
    {
        await Task.Delay(StartupDelayMs, cancellationToken).ConfigureAwait(false);

        var result = await CheckAsync(force: false, cancellationToken).ConfigureAwait(false);
        if (!result.IsUpdateAvailable || result.AvailableUpdate is null)
            return;

        if (!ShouldNotify(result.AvailableUpdate.Version))
            return;

        MarkNotified(result.AvailableUpdate.Version);
        UpdateAvailable?.Invoke(this, result.AvailableUpdate);
    }

    private bool ShouldCheckNow()
    {
        var state = _store.Load();
        if (state.LastCheckUtc is null)
            return true;

        return DateTime.UtcNow - state.LastCheckUtc.Value >= SilentCheckInterval;
    }

    private void RecordCheck()
    {
        var state = _store.Load();
        state.LastCheckUtc = DateTime.UtcNow;
        _store.Save(state);
    }

    private bool ShouldNotify(Version version)
    {
        var state = _store.Load();
        var versionText = version.ToString(3);
        return !string.Equals(state.LastNotifiedVersion, versionText, StringComparison.Ordinal);
    }

    private void MarkNotified(Version version)
    {
        var state = _store.Load();
        state.LastNotifiedVersion = version.ToString(3);
        _store.Save(state);
    }

    private async Task<GitHubRelease?> FetchLatestReleaseAsync(CancellationToken cancellationToken)
    {
        try
        {
            return await _httpClient
                .GetFromJsonAsync<GitHubRelease>($"repos/{Repository}/releases/latest", cancellationToken)
                .ConfigureAwait(false);
        }
        catch
        {
            return null;
        }
    }

    private UpdateInfo? ParseUpdate(GitHubRelease release)
    {
        if (!TryParseReleaseVersion(release.TagName, out var version))
            return null;

        if (version <= AppVersionHelper.Current)
            return null;

        var downloadUrl = ResolveDownloadUrl(release);
        var releasePageUrl = release.HtmlUrl?.Trim();
        if (string.IsNullOrWhiteSpace(downloadUrl) || string.IsNullOrWhiteSpace(releasePageUrl))
            return null;

        return new UpdateInfo
        {
            Version = version,
            DownloadUrl = downloadUrl,
            ReleasePageUrl = releasePageUrl
        };
    }

    private static string? ResolveDownloadUrl(GitHubRelease release)
    {
        var installer = release.Assets?
            .FirstOrDefault(asset =>
                asset.Name?.Contains("Setup", StringComparison.OrdinalIgnoreCase) == true
                && asset.Name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase));

        if (!string.IsNullOrWhiteSpace(installer?.BrowserDownloadUrl))
            return installer.BrowserDownloadUrl.Trim();

        return release.HtmlUrl?.Trim();
    }

    internal static bool TryParseReleaseVersion(string? tagName, out Version version)
    {
        version = new Version(0, 0);
        if (string.IsNullOrWhiteSpace(tagName))
            return false;

        var trimmed = tagName.Trim();
        if (trimmed.StartsWith('v') || trimmed.StartsWith('V'))
            trimmed = trimmed[1..];

        if (!Version.TryParse(trimmed, out var parsed))
            return false;

        version = parsed;
        return true;
    }

    public void Dispose() => _httpClient.Dispose();

    private sealed class GitHubRelease
    {
        [JsonPropertyName("tag_name")]
        public string? TagName { get; set; }

        [JsonPropertyName("html_url")]
        public string? HtmlUrl { get; set; }

        public List<GitHubReleaseAsset>? Assets { get; set; }
    }

    private sealed class GitHubReleaseAsset
    {
        public string? Name { get; set; }

        [JsonPropertyName("browser_download_url")]
        public string? BrowserDownloadUrl { get; set; }
    }
}
