using System.Windows;
using System.Windows.Input;
using OpenTranslate.Models;
using OpenTranslate.Services;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;

namespace OpenTranslate.Views;

public partial class HotkeyCaptureDialog : Window
{
    public ActivationShortcut? CapturedShortcut { get; private set; }

    public HotkeyCaptureDialog()
    {
        InitializeComponent();
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        e.Handled = true;

        if (e.Key == Key.Escape)
        {
            DialogResult = false;
            Close();
            return;
        }

        if (WpfKeyInterop.IsModifierKey(e.Key))
            return;

        var shortcut = WpfKeyInterop.FromKeyEvent(e.Key, e.SystemKey, Keyboard.Modifiers);
        if (!shortcut.IsValid)
            return;

        CapturedShortcut = shortcut;
        PreviewText.Text = ShortcutFormatter.Format(shortcut);
        DialogResult = true;
        Close();
    }

    private void OnCancel(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
