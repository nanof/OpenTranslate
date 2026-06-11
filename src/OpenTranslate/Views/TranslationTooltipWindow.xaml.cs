using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;

namespace OpenTranslate.Views;

public partial class TranslationTooltipWindow : Window
{
    private bool _isClosing;
    private bool _closeOnDeactivate = true;

    public TranslationTooltipWindow(
        string translation,
        double fontSize,
        bool isPending = false,
        bool spinnerOnly = false)
    {
        InitializeComponent();
        TranslationText.FontSize = fontSize;

        if (isPending)
            SetPending(spinnerOnly);
        else
            SetTranslation(translation);
    }

    public void SetPending(bool spinnerOnly = false)
    {
        _closeOnDeactivate = !spinnerOnly;
        PendingPanel.Visibility = Visibility.Visible;
        PendingText.Visibility = spinnerOnly ? Visibility.Collapsed : Visibility.Visible;
        SpinnerHost.Margin = spinnerOnly ? new Thickness(0) : new Thickness(0, 0, 8, 0);
        TranslationText.Visibility = Visibility.Collapsed;
        CopyButton.Visibility = Visibility.Collapsed;
    }

    public void SetTranslation(string text)
    {
        _closeOnDeactivate = true;
        PendingPanel.Visibility = Visibility.Collapsed;
        TranslationText.Visibility = Visibility.Visible;
        TranslationText.Text = text;
        CopyButton.Visibility = Visibility.Visible;
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        _isClosing = true;
        base.OnClosing(e);
    }

    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
            CloseSafely();
    }

    private void OnDeactivated(object? sender, EventArgs e)
    {
        if (_isClosing || !_closeOnDeactivate)
            return;

        Dispatcher.BeginInvoke(CloseSafely);
    }

    public void CloseSafely()
    {
        if (_isClosing)
            return;

        _isClosing = true;
        Close();
    }

    private void OnCopy(object sender, RoutedEventArgs e)
    {
        TranslationText.SelectAll();
        TranslationText.Copy();
        TranslationText.SelectionLength = 0;
    }
}
