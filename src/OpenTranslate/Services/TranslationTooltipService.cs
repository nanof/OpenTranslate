using OpenTranslate.Views;
using WpfApplication = System.Windows.Application;
using WpfSize = System.Windows.Size;
using WpfWindow = System.Windows.Window;

namespace OpenTranslate.Services;

public static class TranslationTooltipService
{
    private const int PasteDelayMs = 200;

    private static TranslationTooltipWindow? _current;
    private static string _translation = "";
    private static nint _targetWindow;
    private static nint _targetControl;
    private static bool _replaceAll;

    public static void ShowPending(double fontSize, bool spinnerOnly = false)
    {
        var dispatcher = WpfApplication.Current.Dispatcher;
        if (!dispatcher.CheckAccess())
        {
            dispatcher.Invoke(() => ShowPending(fontSize, spinnerOnly));
            return;
        }

        if (_current is { IsVisible: true })
        {
            _current.SetPending(spinnerOnly);
            return;
        }

        OpenPending(fontSize, spinnerOnly);
    }

    public static void Update(
        string translation,
        double fontSize,
        nint targetWindow = 0,
        nint targetControl = 0,
        bool replaceAll = false)
    {
        var dispatcher = WpfApplication.Current.Dispatcher;
        if (!dispatcher.CheckAccess())
        {
            dispatcher.Invoke(() => Update(translation, fontSize, targetWindow, targetControl, replaceAll));
            return;
        }

        StorePasteContext(translation, targetWindow, targetControl, replaceAll);

        if (_current is { IsVisible: true })
        {
            _current.SetTranslation(translation);
            return;
        }

        OpenTranslation(translation, fontSize);
    }

    public static async Task<bool> ApplyReplaceAsync()
    {
        var dispatcher = WpfApplication.Current.Dispatcher;
        if (!dispatcher.CheckAccess())
            return await dispatcher.InvokeAsync(ApplyReplaceAsync).Task.Unwrap();

        if (string.IsNullOrWhiteSpace(_translation))
            return false;

        var translation = _translation;
        var targetWindow = _targetWindow != 0
            ? _targetWindow
            : InputSimulationService.GetForegroundWindow();
        var targetControl = _targetControl != 0
            ? _targetControl
            : InputSimulationService.GetFocusedControl(targetWindow);

        var clipboard = new ClipboardService();
        if (!clipboard.TrySetText(translation))
            return false;

        CloseIfOpen();

        await Task.Delay(PasteDelayMs).ConfigureAwait(true);

        InputSimulationService.PasteIntoWindow(targetWindow, targetControl, translation, _replaceAll);
        return true;
    }

    public static void CloseIfOpen()
    {
        var dispatcher = WpfApplication.Current.Dispatcher;
        if (!dispatcher.CheckAccess())
        {
            dispatcher.Invoke(CloseIfOpen);
            return;
        }

        _current?.CloseSafely();
    }

    private static void StorePasteContext(string translation, nint targetWindow, nint targetControl, bool replaceAll)
    {
        _translation = translation;
        _targetWindow = targetWindow;
        _targetControl = targetControl;
        _replaceAll = replaceAll;
    }

    private static void OpenPending(double fontSize, bool spinnerOnly) =>
        Open(
            () => new TranslationTooltipWindow(string.Empty, fontSize, isPending: true, spinnerOnly),
            activate: !spinnerOnly);

    private static void OpenTranslation(string translation, double fontSize) =>
        Open(() => new TranslationTooltipWindow(translation, fontSize));

    private static void Open(Func<TranslationTooltipWindow> createWindow, bool activate = true)
    {
        if (_current is { IsVisible: true })
            _current.CloseSafely();

        var window = createWindow();
        window.Closed += (_, _) =>
        {
            if (ReferenceEquals(_current, window))
                _current = null;
        };

        _current = window;
        PositionNearCursor(window);
        window.ShowActivated = activate;
        window.Show();

        if (activate)
            window.Activate();
    }

    private static void PositionNearCursor(WpfWindow window)
    {
        const int offset = 14;
        var cursor = System.Windows.Forms.Cursor.Position;
        var workArea = System.Windows.Forms.Screen.FromPoint(cursor).WorkingArea;

        window.Measure(new WpfSize(double.PositiveInfinity, double.PositiveInfinity));
        var width = window.DesiredSize.Width;
        var height = window.DesiredSize.Height;

        var left = (double)cursor.X + offset;
        var top = (double)cursor.Y + offset;

        if (left + width > workArea.Right)
            left = Math.Max(workArea.Left, cursor.X - width - offset);

        if (top + height > workArea.Bottom)
            top = Math.Max(workArea.Top, cursor.Y - height - offset);

        window.Left = left;
        window.Top = top;
    }
}
