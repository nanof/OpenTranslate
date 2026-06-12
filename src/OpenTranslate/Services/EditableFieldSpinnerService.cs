using System.Windows.Threading;

namespace OpenTranslate.Services;

public static class EditableFieldSpinnerService
{
    private static readonly string[] Frames =
    [
        "⠋", "⠙", "⠹", "⠸", "⠼", "⠴", "⠦", "⠧", "⠇", "⠏"
    ];

    private static DispatcherTimer? _timer;
    private static int _frameIndex;
    private static SpinnerAnchor? _anchor;

    public static bool IsActive => _anchor is not null;

    public static (string? Text, nint Window, nint Control, bool ReplaceAll) GetCapturedSource()
    {
        if (_anchor is null)
            return (null, 0, 0, false);

        return (_anchor.OriginalText, _anchor.Window, _anchor.Control, _anchor.ReplaceAll);
    }

    public static bool TryStart(nint window, nint control, string originalText, bool replaceAll)
    {
        Stop();
        _anchor = null;

        if (!TryBeginSpinner(window, control, originalText, replaceAll, out var anchor))
            return false;

        _anchor = anchor;
        _frameIndex = 0;
        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(90) };
        _timer.Tick += OnTick;
        _timer.Start();
        return true;
    }

    public static void Complete(string text)
    {
        if (_anchor is null)
            return;

        Stop();
        ApplyAtAnchor(_anchor, text);
        _anchor = null;
    }

    public static void Restore()
    {
        if (_anchor is null)
            return;

        Stop();
        ApplyAtAnchor(_anchor, _anchor.OriginalText);
        _anchor = null;
    }

    public static void Stop()
    {
        if (_timer is not null)
        {
            _timer.Tick -= OnTick;
            _timer.Stop();
            _timer = null;
        }
    }

    private static void OnTick(object? sender, EventArgs e)
    {
        if (_anchor is null)
            return;

        _frameIndex = (_frameIndex + 1) % Frames.Length;
        ApplyAtAnchor(_anchor, Frames[_frameIndex]);
    }

    private static bool TryBeginSpinner(
        nint window,
        nint control,
        string originalText,
        bool replaceAll,
        out SpinnerAnchor anchor)
    {
        anchor = default!;
        var frame = Frames[0];
        control = ResolveControl(window, control);

        if (control != 0
            && TextControlService.IsTextInputControl(control)
            && TextControlService.IsEditable(control)
            && TryInsertAtKnownRange(control, frame, originalText, replaceAll, out var win32Start, out var win32Length))
        {
            anchor = new SpinnerAnchor(
                window,
                control,
                originalText,
                replaceAll,
                SpinnerApplyMode.Win32,
                win32Start,
                win32Length);
            return true;
        }

        if (replaceAll && UiAutomationTextService.TrySetValue(window, control, frame))
        {
            anchor = new SpinnerAnchor(
                window,
                control,
                originalText,
                true,
                SpinnerApplyMode.UiValue,
                0,
                frame.Length);
            return true;
        }

        return false;
    }

    private static bool TryInsertAtKnownRange(
        nint control,
        string frame,
        string originalText,
        bool replaceAll,
        out int start,
        out int length)
    {
        start = 0;
        length = frame.Length;

        if (replaceAll)
        {
            TextControlService.SelectAll(control);
            if (!TextControlService.TryReplaceSelection(control, frame))
                return false;
        }
        else if (TryFindOriginalText(control, originalText, out start))
        {
            if (!TextControlService.TryReplaceRange(control, start, start + originalText.Length, frame))
                return false;
        }
        else
        {
            var (selectionStart, selectionEnd) = TextControlService.GetSelectionRange(control);
            if (selectionStart == selectionEnd)
                return false;

            if (!TextControlService.TryReplaceRange(control, selectionStart, selectionEnd, frame))
                return false;
        }

        return TryLocateText(control, frame, replaceAll, out start, out length);
    }

    private static bool TryFindOriginalText(nint control, string originalText, out int start)
    {
        start = 0;
        if (string.IsNullOrEmpty(originalText))
            return false;

        var allText = TextControlService.GetText(control);
        var index = allText.IndexOf(originalText, StringComparison.Ordinal);
        if (index < 0)
            return false;

        start = index;
        return true;
    }

    private static bool TryLocateText(nint control, string text, bool replaceAll, out int start, out int length)
    {
        start = 0;
        length = text.Length;

        var allText = TextControlService.GetText(control);
        if (replaceAll)
        {
            if (!allText.StartsWith(text, StringComparison.Ordinal))
                return false;

            return true;
        }

        var index = allText.IndexOf(text, StringComparison.Ordinal);
        if (index < 0)
            return false;

        start = index;
        return true;
    }

    private static void ApplyAtAnchor(SpinnerAnchor anchor, string text)
    {
        switch (anchor.Mode)
        {
            case SpinnerApplyMode.Win32 when anchor.Control != 0:
                if (!TextControlService.TryReplaceRange(
                        anchor.Control,
                        anchor.RangeStart,
                        anchor.RangeStart + anchor.RangeLength,
                        text))
                {
                    InputSimulationService.PasteIntoWindow(
                        anchor.Window,
                        anchor.Control,
                        text,
                        anchor.ReplaceAll);
                }

                anchor.RangeLength = text.Length;
                break;

            case SpinnerApplyMode.UiValue:
                UiAutomationTextService.TrySetValue(anchor.Window, anchor.Control, text);
                anchor.RangeLength = text.Length;
                break;
        }
    }

    private static nint ResolveControl(nint window, nint control)
    {
        if (control != 0)
            return control;

        return window != 0
            ? InputSimulationService.GetFocusedControl(window)
            : 0;
    }

    private enum SpinnerApplyMode
    {
        Win32,
        UiValue
    }

    private sealed class SpinnerAnchor(
        nint window,
        nint control,
        string originalText,
        bool replaceAll,
        SpinnerApplyMode mode,
        int rangeStart,
        int rangeLength)
    {
        public nint Window { get; } = window;
        public nint Control { get; } = control;
        public string OriginalText { get; } = originalText;
        public bool ReplaceAll { get; } = replaceAll;
        public SpinnerApplyMode Mode { get; } = mode;
        public int RangeStart { get; } = rangeStart;
        public int RangeLength { get; set; } = rangeLength;
    }
}
