using System.Runtime.InteropServices;
using System.Windows.Threading;

namespace OpenTranslate.Services;

public sealed class ClipboardService
{
    private const int HResultClipboardBusy = unchecked((int)0x800401D0);

    public string? TryGetText() => RunOnUiThread(TryGetTextCore);

    public bool TrySetText(string text) => RunOnUiThread(() => TrySetTextCore(text));

    public void SetText(string text)
    {
        if (!TrySetText(text))
            throw new InvalidOperationException("Could not write to the clipboard. Close other apps using it and try again.");
    }

    public string? WaitForNonEmptyText(int timeoutMs = 1000) =>
        RunOnUiThread(() => WaitForNonEmptyTextCore(timeoutMs));

    public string? WaitForTextChange(string? previousText, int timeoutMs = 1000) =>
        RunOnUiThread(() => WaitForTextChangeCore(previousText, timeoutMs));

    public async Task<string?> GetTextAfterDelayAsync(int delayMs, CancellationToken cancellationToken = default)
    {
        if (delayMs > 0)
            await Task.Delay(delayMs, cancellationToken).ConfigureAwait(true);

        return TryGetText();
    }

    private static string? TryGetTextCore()
    {
        for (var attempt = 0; attempt < 20; attempt++)
        {
            try
            {
                var text = ReadClipboardText();
                if (!string.IsNullOrWhiteSpace(text))
                    return TextFormattingHelper.NormalizeForTranslation(text);
            }
            catch (Exception ex) when (IsClipboardBusy(ex) && attempt < 19)
            {
                Thread.Sleep(50 + attempt * 40);
            }
        }

        return null;
    }

    private static string? ReadClipboardText()
    {
        if (System.Windows.Clipboard.ContainsText())
        {
            var text = System.Windows.Clipboard.GetText();
            if (!string.IsNullOrWhiteSpace(text))
                return text;
        }

        var data = System.Windows.Clipboard.GetDataObject();
        if (data is null)
            return null;

        foreach (var format in new[]
                 {
                     System.Windows.DataFormats.UnicodeText,
                     System.Windows.DataFormats.Text,
                     "text/plain",
                     "Text"
                 })
        {
            if (!data.GetDataPresent(format))
                continue;

            if (data.GetData(format) is string value && !string.IsNullOrWhiteSpace(value))
                return value;
        }

        return null;
    }

    private static bool TrySetTextCore(string text)
    {
        for (var attempt = 0; attempt < 20; attempt++)
        {
            try
            {
                System.Windows.Clipboard.SetText(text, System.Windows.TextDataFormat.UnicodeText);
                return true;
            }
            catch (Exception ex) when (IsClipboardBusy(ex) && attempt < 19)
            {
                Thread.Sleep(50 + attempt * 50);
            }
        }

        return false;
    }

    private static string? WaitForNonEmptyTextCore(int timeoutMs)
    {
        var deadline = Environment.TickCount64 + timeoutMs;

        while (Environment.TickCount64 < deadline)
        {
            var current = TryGetTextCore();
            if (!string.IsNullOrWhiteSpace(current))
                return current;

            Thread.Sleep(60);
        }

        return null;
    }

    private static string? WaitForTextChangeCore(string? previousText, int timeoutMs)
    {
        var deadline = Environment.TickCount64 + timeoutMs;

        while (Environment.TickCount64 < deadline)
        {
            var current = TryGetTextCore();
            if (!string.IsNullOrWhiteSpace(current)
                && !string.Equals(current, previousText, StringComparison.Ordinal))
            {
                return current;
            }

            Thread.Sleep(60);
        }

        return null;
    }

    private static T RunOnUiThread<T>(Func<T> action)
    {
        var dispatcher = System.Windows.Application.Current.Dispatcher;
        if (dispatcher.CheckAccess())
            return action();

        return dispatcher.Invoke(action, DispatcherPriority.Normal);
    }

    private static bool IsClipboardBusy(Exception ex) =>
        ex.HResult == HResultClipboardBusy
        || ex.Message.Contains("OpenClipboard", StringComparison.OrdinalIgnoreCase)
        || ex.Message.Contains("CLIPBRD_E_CANT_OPEN", StringComparison.OrdinalIgnoreCase);
}
