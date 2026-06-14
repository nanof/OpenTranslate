using System.Windows;
using System.Windows.Interop;

namespace OpenTranslate.Services;

internal static class ForegroundMessageBox
{
    public static bool Confirm(string message, string title, nint foregroundWindow = 0)
    {
        var owner = new Window
        {
            Width = 0,
            Height = 0,
            WindowStyle = WindowStyle.None,
            ShowInTaskbar = false,
            Topmost = true,
            ShowActivated = true
        };

        if (foregroundWindow != 0)
            new WindowInteropHelper(owner) { Owner = foregroundWindow };

        owner.Show();
        owner.Activate();

        try
        {
            return System.Windows.MessageBox.Show(
                       owner,
                       message,
                       title,
                       System.Windows.MessageBoxButton.YesNo,
                       System.Windows.MessageBoxImage.Warning)
                   == System.Windows.MessageBoxResult.Yes;
        }
        finally
        {
            owner.Close();
        }
    }
}
