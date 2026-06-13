using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using OpenTranslate.Models;
using OpenTranslate.Services;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using Button = System.Windows.Controls.Button;
using Brush = System.Windows.Media.Brush;
using Brushes = System.Windows.Media.Brushes;
using SolidColorBrush = System.Windows.Media.SolidColorBrush;
using Color = System.Windows.Media.Color;
using ColorConverter = System.Windows.Media.ColorConverter;

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

    private static readonly Brush ModeActiveBackground = CreateBrush("#89B4FA");
    private static readonly Brush ModeActiveForeground = CreateBrush("#1E1E2E");
    private static readonly Brush ModeInactiveBackground = Brushes.Transparent;
    private static readonly Brush ModeInactiveForeground = CreateBrush("#CDD6F4");

    private readonly Random _random = new();
    private readonly Dictionary<TextImprovementMode, Button> _modeButtons = [];
    private DispatcherTimer? _glyphTimer;
    private CancellationTokenSource? _variantCts;
    private TextImprovementMode _activeMode = TextImprovementMode.None;

    private bool _isClosing;
    private bool _closeOnDeactivate = true;

    public TranslationTooltipWindow(
        string translation,
        double fontSize,
        bool isPending = false,
        bool spinnerOnly = false,
        bool canReplace = false,
        bool canShowModes = false,
        TextImprovementMode activeMode = TextImprovementMode.None)
    {
        InitializeComponent();
        TranslationText.FontSize = fontSize;

        if (isPending)
            SetPending(spinnerOnly);
        else
            SetTranslation(translation, canReplace, canShowModes, activeMode);

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

    public void SetTranslation(
        string text,
        bool canReplace = false,
        bool canShowModes = false,
        TextImprovementMode activeMode = TextImprovementMode.None)
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

        // Pressing Enter triggers the default button: "Replace" when it's available,
        // otherwise "Copy" is the fallback default.
        ReplaceButton.IsDefault = canReplace;
        CopyButton.IsDefault = !canReplace;

        _activeMode = activeMode;
        if (canShowModes)
            BuildModesPanel(activeMode);

        ModesButton.Visibility = canShowModes ? Visibility.Visible : Visibility.Collapsed;
        ModesPanelBorder.Visibility = Visibility.Collapsed;

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
        _variantCts?.Cancel();
        _variantCts?.Dispose();
        _variantCts = null;
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

    private void BuildModesPanel(TextImprovementMode activeMode)
    {
        if (_modeButtons.Count > 0)
        {
            UpdateModeHighlight(activeMode);
            return;
        }

        var optionStyle = (Style)FindResource("ModeOptionButton");

        foreach (var option in TextImprovementModes.Options)
        {
            var button = new Button
            {
                Content = option.DisplayName,
                Tag = option.Mode,
                Style = optionStyle,
                Padding = new Thickness(8, 4, 8, 4),
                Margin = new Thickness(0, 1, 0, 1),
                HorizontalContentAlignment = System.Windows.HorizontalAlignment.Left,
                BorderThickness = new Thickness(0),
                FontSize = 11,
                Cursor = System.Windows.Input.Cursors.Hand
            };
            button.Click += OnModeSelected;
            _modeButtons[option.Mode] = button;
            ModesPanel.Children.Add(button);
        }

        UpdateModeHighlight(activeMode);
    }

    private void UpdateModeHighlight(TextImprovementMode activeMode)
    {
        foreach (var (mode, button) in _modeButtons)
        {
            var isActive = mode == activeMode;
            button.Background = isActive ? ModeActiveBackground : ModeInactiveBackground;
            button.Foreground = isActive ? ModeActiveForeground : ModeInactiveForeground;
            button.FontWeight = isActive ? FontWeights.SemiBold : FontWeights.Normal;
        }
    }

    private void OnToggleModes(object sender, RoutedEventArgs e) =>
        ModesPanelBorder.Visibility = ModesPanelBorder.Visibility == Visibility.Visible
            ? Visibility.Collapsed
            : Visibility.Visible;

    private async void OnModeSelected(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: TextImprovementMode mode })
            return;

        _variantCts?.Cancel();
        _variantCts?.Dispose();
        _variantCts = new CancellationTokenSource();
        var token = _variantCts.Token;

        _closeOnDeactivate = false;
        SetInteractionEnabled(false);
        TranslationText.Opacity = 0.45;

        try
        {
            var text = await TranslationTooltipService.GenerateVariantAsync(mode, token);
            if (token.IsCancellationRequested)
                return;

            TranslationText.Text = text;
            _activeMode = mode;
            UpdateModeHighlight(mode);
            ModesPanelBorder.Visibility = Visibility.Collapsed;
        }
        catch (OperationCanceledException)
        {
        }
        catch (TranslationApiException ex)
        {
            TranslationText.Text = ex.Message;
        }
        catch (Exception ex)
        {
            TranslationText.Text = $"Could not generate this variant: {ex.Message}";
        }
        finally
        {
            if (IsVisible)
            {
                TranslationText.Opacity = 1;
                SetInteractionEnabled(true);
                _closeOnDeactivate = true;
            }
        }
    }

    private void SetInteractionEnabled(bool enabled)
    {
        ReplaceButton.IsEnabled = enabled;
        CopyButton.IsEnabled = enabled;
        ModesButton.IsEnabled = enabled;
        foreach (var button in _modeButtons.Values)
            button.IsEnabled = enabled;
    }

    private static Brush CreateBrush(string hex)
    {
        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
        brush.Freeze();
        return brush;
    }

    private void OnCopy(object sender, RoutedEventArgs e)
    {
        TranslationText.SelectAll();
        TranslationText.Copy();
        TranslationText.SelectionLength = 0;
        CloseSafely();
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
            CloseSafely();
        }
    }
}
