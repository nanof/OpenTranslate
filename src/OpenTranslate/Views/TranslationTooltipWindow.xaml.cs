using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using OpenTranslate.Services;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;

namespace OpenTranslate.Views;

public partial class TranslationTooltipWindow : Window
{
    // A Matrix-style spinner: a single glyph that flickers through characters from
    // Japanese (katakana + hiragana), Greek and Latin scripts.
    private static readonly string[] SpinnerGlyphs = BuildGlyphs(
        "アイウエオカキクケコサシスセソタチツテトナニヌネノハヒフヘホマミムメモヤユヨラリルレロワヲン" +
        "あいうえおかきくけこさしすせそたちつてとなにぬねのはひふへほまみむめもやゆよらりるれろわをん" +
        "αβγδεζηθικλμνξοπρστυφχψωΓΔΘΛΞΠΣΦΨΩ" +
        "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789");

    private readonly Random _random = new();
    private DispatcherTimer? _glyphTimer;

    private bool _isClosing;
    private bool _closeOnDeactivate = true;

    public TranslationTooltipWindow(
        string translation,
        double fontSize,
        bool isPending = false,
        bool spinnerOnly = false,
        bool canReplace = false)
    {
        InitializeComponent();
        TranslationText.FontSize = fontSize;

        if (isPending)
            SetPending(spinnerOnly);
        else
            SetTranslation(translation, canReplace);
    }

    public void SetPending(bool spinnerOnly = false)
    {
        _closeOnDeactivate = !spinnerOnly;

        if (spinnerOnly)
        {
            // Compact, circular badge that hugs the spinner instead of a large boxy tooltip.
            RootBorder.Padding = new Thickness(8);
            RootBorder.CornerRadius = new CornerRadius(999);
            SpinnerHost.Margin = new Thickness(0);
        }
        else
        {
            RootBorder.Padding = new Thickness(10);
            RootBorder.CornerRadius = new CornerRadius(8);
            SpinnerHost.Margin = new Thickness(0, 0, 8, 0);
        }

        PendingPanel.Visibility = Visibility.Visible;
        PendingText.Visibility = spinnerOnly ? Visibility.Collapsed : Visibility.Visible;
        TranslationText.Visibility = Visibility.Collapsed;
        ActionPanel.Visibility = Visibility.Collapsed;

        StartGlyphSpinner();
    }

    public void SetTranslation(string text, bool canReplace = false)
    {
        _closeOnDeactivate = true;
        StopGlyphSpinner();
        RootBorder.Padding = new Thickness(10);
        RootBorder.CornerRadius = new CornerRadius(8);
        PendingPanel.Visibility = Visibility.Collapsed;
        TranslationText.Visibility = Visibility.Visible;
        TranslationText.Text = text;
        ActionPanel.Visibility = Visibility.Visible;
        ReplaceButton.Visibility = canReplace ? Visibility.Visible : Visibility.Collapsed;
    }

    public void FocusForInteraction()
    {
        Activate();
        Focus();
    }

    private void StartGlyphSpinner()
    {
        if (_glyphTimer is not null)
            return;

        _glyphTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(90) };
        _glyphTimer.Tick += OnGlyphTick;
        _glyphTimer.Start();
        OnGlyphTick(this, EventArgs.Empty);
    }

    private void StopGlyphSpinner()
    {
        if (_glyphTimer is null)
            return;

        _glyphTimer.Stop();
        _glyphTimer.Tick -= OnGlyphTick;
        _glyphTimer = null;
    }

    private void OnGlyphTick(object? sender, EventArgs e) =>
        SpinnerGlyph.Text = SpinnerGlyphs[_random.Next(SpinnerGlyphs.Length)];

    private static string[] BuildGlyphs(string source)
    {
        var glyphs = new List<string>(source.Length);
        foreach (var ch in source)
        {
            if (!char.IsWhiteSpace(ch))
                glyphs.Add(ch.ToString());
        }

        return [.. glyphs];
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        _isClosing = true;
        StopGlyphSpinner();
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

    private async void OnReplace(object sender, RoutedEventArgs e)
    {
        _closeOnDeactivate = false;
        ReplaceButton.IsEnabled = false;
        CopyButton.IsEnabled = false;

        try
        {
            await TranslationTooltipService.ApplyReplaceAsync();
        }
        finally
        {
            if (IsVisible)
            {
                ReplaceButton.IsEnabled = true;
                CopyButton.IsEnabled = true;
                _closeOnDeactivate = true;
            }
        }
    }
}
