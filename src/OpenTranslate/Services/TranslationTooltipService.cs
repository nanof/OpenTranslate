using OpenTranslate.Views;
using WpfApplication = System.Windows.Application;
using WpfSize = System.Windows.Size;
using WpfWindow = System.Windows.Window;

namespace OpenTranslate.Services;

public static class TranslationTooltipService
{
    private static TranslationTooltipWindow? _current;

    public static void ShowPending(double fontSize)
    {
        var dispatcher = WpfApplication.Current.Dispatcher;
        if (!dispatcher.CheckAccess())
        {
            dispatcher.Invoke(() => ShowPending(fontSize));
            return;
        }

        if (_current is { IsVisible: true })
        {
            _current.SetPending();
            return;
        }

        OpenPending(fontSize);
    }

    public static void Update(string translation, double fontSize)
    {
        var dispatcher = WpfApplication.Current.Dispatcher;
        if (!dispatcher.CheckAccess())
        {
            dispatcher.Invoke(() => Update(translation, fontSize));
            return;
        }

        if (_current is { IsVisible: true })
        {
            _current.SetTranslation(translation);
            return;
        }

        OpenTranslation(translation, fontSize);
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

    private static void OpenPending(double fontSize) =>
        Open(() => new TranslationTooltipWindow(string.Empty, fontSize, isPending: true));

    private static void OpenTranslation(string translation, double fontSize) =>
        Open(() => new TranslationTooltipWindow(translation, fontSize));

    private static void Open(Func<TranslationTooltipWindow> createWindow)
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
        window.Show();
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
