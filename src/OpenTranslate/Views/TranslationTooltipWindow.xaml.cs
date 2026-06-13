using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
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
    private const double DefaultContentWidth = AppSettings.DefaultTooltipWidth;
    private const double DefaultContentHeight = AppSettings.DefaultTooltipHeight;
    private const double MaxInitialContentWidth = 420;
    private const double MaxInitialContentHeight = 320;
    private const int ResizeBorderPixels = 6;
    private const double TranslationMinWidth = AppSettings.MinTooltipWidth;
    private const double TranslationMinHeight = AppSettings.MinTooltipHeight;

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
    private bool _isSpinnerCompact;
    private bool _applyingInitialSize;
    private System.Windows.Size? _lastUserSize;

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
        SizeChanged += OnSizeChanged;
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        var source = PresentationSource.FromVisual(this) as HwndSource;
        source?.AddHook(WndProc);
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (TranslationText.Visibility == Visibility.Visible)
            EnableResizingWithInitialSize();

        PlayEntranceAnimation();
    }

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
        _isSpinnerCompact = spinnerOnly;
        DisableResizing();

        if (spinnerOnly)
        {
            // Compact, circular badge that hugs the spinner instead of a large boxy tooltip.
            RootBorder.Padding = new Thickness(8);
            RootBorder.CornerRadius = new CornerRadius(999);
            RootBorder.HorizontalAlignment = System.Windows.HorizontalAlignment.Center;
            RootBorder.VerticalAlignment = VerticalAlignment.Center;
            SpinnerHost.Margin = new Thickness(0);
            ResizeGripHint.Visibility = Visibility.Collapsed;
            TextRow.Height = new GridLength(0);
            TextRow.MinHeight = 0;
            MinWidth = 0;
            MinHeight = 0;
            // Let the window hug the spinner. We can't fix Width/Height to ActualWidth here
            // because this runs during construction (before the window is shown), so the
            // measured size would be 0 and the badge would never appear.
            Width = double.NaN;
            Height = double.NaN;
            SizeToContent = SizeToContent.WidthAndHeight;
        }
        else
        {
            RootBorder.Padding = new Thickness(10);
            RootBorder.CornerRadius = new CornerRadius(8);
            RootBorder.HorizontalAlignment = System.Windows.HorizontalAlignment.Stretch;
            RootBorder.VerticalAlignment = VerticalAlignment.Stretch;
            SpinnerHost.Margin = new Thickness(0, 0, 8, 0);
            ResizeGripHint.Visibility = Visibility.Collapsed;
            RestoreTranslationLayout();
            ApplyCompactPendingSize();
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
        var willReplayEntrance = wasPending && IsLoaded;

        // Snap to the hidden pre-entrance state (opacity 0) BEFORE any resizing or content
        // swap. Otherwise the window — still fully opaque from the spinner's finished
        // entrance — would resize to the full translation size for one visible frame
        // before the animation resets it, producing a "full-size flash" glitch.
        if (willReplayEntrance)
            ResetToEntranceStart();

        _closeOnDeactivate = true;
        _isSpinnerCompact = false;
        StopGlyphSpinner();
        RootBorder.Padding = new Thickness(10);
        RootBorder.CornerRadius = new CornerRadius(8);
        RootBorder.HorizontalAlignment = System.Windows.HorizontalAlignment.Stretch;
        RootBorder.VerticalAlignment = VerticalAlignment.Stretch;
        RestoreTranslationLayout();
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
        ResizeGripHint.Visibility = Visibility.Visible;

        EnableResizingWithInitialSize();

        // When swapping the spinner for the actual result, replay the full entrance
        // "pop" so the translation animates in (not just the tiny spinner badge that
        // was shown while the request was pending). The window already resized while
        // hidden (opacity 0), so defer the animation until layout settles to scale/slide
        // against the final size without any glitch.
        if (willReplayEntrance)
            Dispatcher.BeginInvoke(PlayEntranceAnimation, DispatcherPriority.Loaded);
    }

    private void DisableResizing()
    {
        ResizeMode = ResizeMode.NoResize;
        SizeToContent = SizeToContent.Manual;
        ResizeGripHint.Visibility = Visibility.Collapsed;
    }

    private void ApplyCompactPendingSize()
    {
        MinWidth = 0;
        MinHeight = 0;
        Width = double.NaN;
        Height = double.NaN;
        SizeToContent = SizeToContent.WidthAndHeight;
    }

    private void RestoreTranslationLayout()
    {
        MinWidth = TranslationMinWidth;
        MinHeight = TranslationMinHeight;
        TextRow.Height = new GridLength(1, GridUnitType.Star);
        TextRow.MinHeight = 40;
    }

    private void EnableResizingWithInitialSize()
    {
        _applyingInitialSize = true;
        try
        {
            RestoreTranslationLayout();
            ResizeMode = ResizeMode.CanResize;

            var (savedWidth, savedHeight) = TranslationTooltipService.GetSavedSize();
            if (savedWidth > 0 && savedHeight > 0)
            {
                // Switch to manual sizing FIRST; otherwise (e.g. coming from the spinner,
                // which uses SizeToContent=WidthAndHeight) the Width/Height assignments are
                // ignored and the saved size never gets applied.
                SizeToContent = SizeToContent.Manual;
                Width = savedWidth;
                Height = savedHeight;
                ResizeGripHint.Visibility = Visibility.Visible;
                return;
            }

            SizeToContent = SizeToContent.WidthAndHeight;
            Width = double.NaN;
            Height = double.NaN;
            UpdateLayout();

            var initialWidth = Math.Clamp(
                ActualWidth > 0 ? ActualWidth : DefaultContentWidth,
                TranslationMinWidth,
                MaxInitialContentWidth);
            var initialHeight = Math.Clamp(
                ActualHeight > 0 ? ActualHeight : DefaultContentHeight,
                TranslationMinHeight,
                MaxInitialContentHeight);

            SizeToContent = SizeToContent.Manual;
            Width = initialWidth;
            Height = initialHeight;
            ResizeGripHint.Visibility = Visibility.Visible;
        }
        finally
        {
            _applyingInitialSize = false;
        }
    }

    // Captures every size change the user makes (ignoring the programmatic initial sizing),
    // so the latest dimensions can be persisted reliably when the tooltip closes.
    private void OnSizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (_applyingInitialSize || _isSpinnerCompact)
            return;

        if (ResizeMode != ResizeMode.CanResize || SizeToContent != SizeToContent.Manual)
            return;

        if (ActualWidth >= TranslationMinWidth && ActualHeight >= TranslationMinHeight)
            _lastUserSize = new System.Windows.Size(ActualWidth, ActualHeight);
    }

    private void PersistTooltipSize()
    {
        if (_isSpinnerCompact)
            return;

        var width = _lastUserSize?.Width ?? ActualWidth;
        var height = _lastUserSize?.Height ?? ActualHeight;

        if (width >= TranslationMinWidth && height >= TranslationMinHeight)
            TranslationTooltipService.SaveTooltipSize(width, height);
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        const int wmNcHitTest = 0x0084;
        const int htLeft = 10;
        const int htRight = 11;
        const int htTop = 12;
        const int htTopLeft = 13;
        const int htTopRight = 14;
        const int htBottom = 15;
        const int htBottomLeft = 16;
        const int htBottomRight = 17;

        if (msg != wmNcHitTest || ResizeMode != ResizeMode.CanResize)
            return IntPtr.Zero;

        var screenPoint = new System.Windows.Point(
            (short)(lParam.ToInt64() & 0xFFFF),
            (short)((lParam.ToInt64() >> 16) & 0xFFFF));
        var windowPoint = PointFromScreen(screenPoint);

        var left = windowPoint.X <= ResizeBorderPixels;
        var right = windowPoint.X >= ActualWidth - ResizeBorderPixels;
        var top = windowPoint.Y <= ResizeBorderPixels;
        var bottom = windowPoint.Y >= ActualHeight - ResizeBorderPixels;

        if (left && top)
        {
            handled = true;
            return (IntPtr)htTopLeft;
        }

        if (right && top)
        {
            handled = true;
            return (IntPtr)htTopRight;
        }

        if (left && bottom)
        {
            handled = true;
            return (IntPtr)htBottomLeft;
        }

        if (right && bottom)
        {
            handled = true;
            return (IntPtr)htBottomRight;
        }

        if (left)
        {
            handled = true;
            return (IntPtr)htLeft;
        }

        if (right)
        {
            handled = true;
            return (IntPtr)htRight;
        }

        if (top)
        {
            handled = true;
            return (IntPtr)htTop;
        }

        if (bottom)
        {
            handled = true;
            return (IntPtr)htBottom;
        }

        return IntPtr.Zero;
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
        PersistTooltipSize();
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

        foreach (var option in TranslationTooltipService.GetTooltipVariantOptions())
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
