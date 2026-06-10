namespace OpenTranslate.Services;

public sealed class ShortcutActivatedEventArgs : EventArgs
{
    public bool ClipboardAlreadyUpdated { get; init; }
    public nint TargetWindow { get; init; }
    public nint TargetControl { get; init; }
}
