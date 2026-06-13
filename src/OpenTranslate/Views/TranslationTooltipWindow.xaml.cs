using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using OpenTranslate.Services;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;

namespace OpenTranslate.Views;

public partial class TranslationTooltipWindow : Window
{
    private const double EntranceInitialScale = 0.35;
    private const double EntranceInitialSlide = 10;
    private const double EntranceBackAmplitude = 0.35;
    private const double EntranceDurationMs = 111;
    private const double EntranceFadeMs = 102;

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

        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e) => PlayEntranceAnimation();

    // macOS / iOS style entrance: a gentle fade combined with a springy "pop" scale
    // and a subtle upward slide, so the tooltip feels like it grows out near the cursor.
    private void PlayEntranceAnimation()
    {
        var ease = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = EntranceBackAmplitude };
        var duration = TimeSpan.FromMilliseconds(EntranceDurationMs);

        var fade = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(EntranceFadeMs))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };

        var scale = new DoubleAnimation(EntranceInitialScale, 1, duration) { EasingFunction = ease };
        var slide = new DoubleAnimation(EntranceInitialSlide, 0, duration) { EasingFunction = ease };

        RootBorder.BeginAnimation(OpacityProperty, fade);
        RootScale.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleXProperty, scale);
        RootScale.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleYProperty, scale);
        RootTranslate.BeginAnimation(System.Windows.Media.TranslateTransform.YProperty, slide);
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
        var wasPending = PendingPanel.Visibility == Visibility.Visible;

        _closeOnDeactivate = true;
        StopGlyphSpinner();
        RootBorder.Padding = new Thickness(10);
        RootBorder.CornerRadius = new CornerRadius(8);
        PendingPanel.Visibility = Visibility.Collapsed;
        TranslationText.Visibility = Visibility.Visible;
        TranslationText.Text = text;
        ActionPanel.Visibility = Visibility.Visible;
        ReplaceButton.Visibility = canReplace ? Visibility.Visible : Visibility.Collapsed;

        // When swapping the spinner for the actual result, replay the full entrance
        // "pop" so the translation animates in (not just the tiny spinner badge that
        // was shown while the request was pending).
        if (wasPending && IsLoaded)
        {
            // Snap back to the hidden pre-entrance state synchronously. The spinner's
            // finished animation otherwise holds the final look (opacity 1, scale 1),
            // which would flash on screen for a frame before the replay resets it.
            ResetToEntranceStart();

            // The window resizes to fit the new content; defer the animation until layout
            // settles so the scale/slide animate against the final size. It stays hidden
            // (opacity 0) during that gap, so there's no glitch.
            Dispatcher.BeginInvoke(PlayEntranceAnimation, DispatcherPriority.Loaded);
        }
    }

    private void ResetToEntranceStart()
    {
        RootBorder.BeginAnimation(OpacityProperty, null);
        RootScale.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleXProperty, null);
        RootScale.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleYProperty, null);
        RootTranslate.BeginAnimation(System.Windows.Media.TranslateTransform.YProperty, null);

        RootBorder.Opacity = 0;
        RootScale.ScaleX = EntranceInitialScale;
        RootScale.ScaleY = EntranceInitialScale;
        RootTranslate.Y = EntranceInitialSlide;
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
