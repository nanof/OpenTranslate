using OpenTranslate.Models;
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

    private static string _variantSourceText = "";
    private static AppSettings? _variantSettings;
    private static TranslationClient? _variantClient;
    private static UsageTrackingService? _usageTracking;
    private static TextImprovementMode _activeVariantMode = TextImprovementMode.None;
    private static readonly Dictionary<TextImprovementMode, string> _variantCache = [];

    public static void SetUsageTracking(UsageTrackingService usageTracking) =>
        _usageTracking = usageTracking;

    public static bool VariantsAvailable { get; private set; }

    public static TextImprovementMode ActiveVariantMode => _activeVariantMode;

    public static IReadOnlyList<TextImprovementOption> GetTooltipVariantOptions() =>
        _variantSettings is null
            ? TextImprovementModes.SettingsOptions
            : TextImprovementModes.GetTooltipOptions(_variantSettings);

    public static void ShowPending(double fontSize, bool spinnerOnly = false)
    {
        var dispatcher = WpfApplication.Current.Dispatcher;
        if (!dispatcher.CheckAccess())
        {
            dispatcher.Invoke(() => ShowPending(fontSize, spinnerOnly));
            return;
        }

        ClearVariantContext();

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
        bool replaceAll = false,
        bool canReplace = false)
    {
        var dispatcher = WpfApplication.Current.Dispatcher;
        if (!dispatcher.CheckAccess())
        {
            dispatcher.Invoke(() => Update(translation, fontSize, targetWindow, targetControl, replaceAll, canReplace));
            return;
        }

        StorePasteContext(translation, targetWindow, targetControl, replaceAll);

        if (_current is { IsVisible: true })
        {
            _current.SetTranslation(translation, canReplace, VariantsAvailable, _activeVariantMode);
            // The pending spinner was shown without focus; grab focus now so the user can
            // dismiss the result with ESC.
            _current.FocusForInteraction();
            return;
        }

        OpenTranslation(translation, fontSize, canReplace);
    }

    // Registers everything needed to regenerate the same source text under a different
    // improvement mode, so the tooltip can offer a "Modes" preview. The cache is seeded
    // with the translation already shown for the currently active mode.
    public static void SetVariantContext(
        string sourceText,
        AppSettings settings,
        TranslationClient client,
        TextImprovementMode activeMode,
        string activeTranslation)
    {
        _variantSourceText = sourceText;
        _variantSettings = settings;
        _variantClient = client;
        _activeVariantMode = activeMode;
        _variantCache.Clear();
        _variantCache[activeMode] = activeTranslation;
        VariantsAvailable = TranslationProviders.SupportsModelSelection(settings.Provider)
            && !string.IsNullOrWhiteSpace(sourceText);
    }

    public static void ClearVariantContext()
    {
        _variantSourceText = "";
        _variantSettings = null;
        _variantClient = null;
        _activeVariantMode = TextImprovementMode.None;
        _variantCache.Clear();
        VariantsAvailable = false;
    }

    public static async Task<string> GenerateVariantAsync(
        TextImprovementMode mode,
        CancellationToken cancellationToken)
    {
        if (_variantCache.TryGetValue(mode, out var cached))
        {
            _activeVariantMode = mode;
            _translation = cached;
            return cached;
        }

        if (_variantClient is null || _variantSettings is null)
            return _translation;

        var variantSettings = _variantSettings.WithImprovementMode(mode);
        var raw = await _variantClient
            .TranslateAsync(_variantSourceText, variantSettings, cancellationToken)
            .ConfigureAwait(true);

        var normalized = TextFormattingHelper.NormalizeForTranslation(raw);
        _variantCache[mode] = normalized;
        _activeVariantMode = mode;
        _translation = normalized;
        _usageTracking?.RecordTranslation(_variantSourceText, normalized);
        return normalized;
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
        ClearVariantContext();
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

    private static void OpenTranslation(string translation, double fontSize, bool canReplace) =>
        Open(() => new TranslationTooltipWindow(
            translation,
            fontSize,
            canReplace: canReplace,
            canShowModes: VariantsAvailable,
            activeMode: _activeVariantMode));

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
