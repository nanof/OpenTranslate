namespace OpenTranslate.Services;

public static class KeyboardTextCaptureService
{
    private const int FocusDelayMs = 120;
    private const int CopyDelayMs = 150;

    public static TextCaptureResult? TryCaptureSelection(nint window, ClipboardService clipboard)
    {
        if (window == 0)
            return null;

        try
        {
            var clipboardBefore = clipboard.TryGetText() ?? "";

            InputSimulationService.RestoreFocusForCapture(window);
            Thread.Sleep(FocusDelayMs);

            InputSimulationService.SendCtrlC();
            Thread.Sleep(CopyDelayMs);

            var copied = clipboard.WaitForTextChange(clipboardBefore, timeoutMs: 1200);
            if (string.IsNullOrWhiteSpace(copied))
                return null;

            return new TextCaptureResult
            {
                Text = TextFormattingHelper.NormalizeForTranslation(copied),
                Control = 0,
                ReplaceAll = false
            };
        }
        catch
        {
            return null;
        }
    }

    public static TextCaptureResult? TryCaptureAll(nint window, ClipboardService clipboard)
    {
        if (window == 0)
            return null;

        try
        {
            var clipboardBefore = clipboard.TryGetText() ?? "";

            InputSimulationService.RestoreFocusForCapture(window);
            Thread.Sleep(FocusDelayMs);

            InputSimulationService.SendCtrlA();
            Thread.Sleep(CopyDelayMs);
            InputSimulationService.SendCtrlC();
            Thread.Sleep(CopyDelayMs);

            var copiedAll = clipboard.WaitForTextChange(clipboardBefore, timeoutMs: 1200);
            if (string.IsNullOrWhiteSpace(copiedAll))
                copiedAll = clipboard.TryGetText();

            if (string.IsNullOrWhiteSpace(copiedAll))
                return null;

            return new TextCaptureResult
            {
                Text = TextFormattingHelper.NormalizeForTranslation(copiedAll),
                Control = 0,
                ReplaceAll = true
            };
        }
        catch
        {
            return null;
        }
    }

    public static TextCaptureResult? TryCapture(nint window, ClipboardService clipboard)
    {
        var selection = TryCaptureSelection(window, clipboard);
        if (selection is not null)
            return selection;

        return TryCaptureAll(window, clipboard);
    }
}
