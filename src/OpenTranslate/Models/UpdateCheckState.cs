namespace OpenTranslate.Models;

public sealed class UpdateCheckState
{
    public DateTime? LastCheckUtc { get; set; }
    public string? LastNotifiedVersion { get; set; }
}
