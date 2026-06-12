namespace OpenTranslate.Services;

public static class KeyboardTextCaptureService
{
    private const int FocusDelayMs = 120;
    private const int CopyDelayMs = 150;

    public static TextCaptureResult? TryCaptureSelectionStrict(nint window, ClipboardService clipboard)
    {
        if (window == 0)
            return null;

        try
        {
            var sequenceBefore = ClipboardService.GetSequenceNumber();

            InputSimulationService.RestoreFocusForCapture(window);
            Thread.Sleep(FocusDelayMs);

            InputSimulationService.SendCtrlC();

            // The clipboard sequence number only increments when the app actually writes to
            // the clipboard, i.e. when there is a real selection. If it stays unchanged the
            // Ctrl+C was a no-op (nothing selected), so we report no selection.
            var deadline = Environment.TickCount64 + 700;
            while (Environment.TickCount64 < deadline)
            {
                if (ClipboardService.GetSequenceNumber() != sequenceBefore)
                {
                    var copied = clipboard.TryGetText();
                    if (!string.IsNullOrWhiteSpace(copied))
                    {
                        return new TextCaptureResult
                        {
                            Text = TextFormattingHelper.NormalizeForTranslation(copied),
                            Control = 0,
                            ReplaceAll = false
                        };
                    }
                }

                Thread.Sleep(40);
            }

            return null;
        }
        catch
        {
            return null;
        }
    }

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
