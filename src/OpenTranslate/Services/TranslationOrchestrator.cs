using OpenTranslate.Models;

namespace OpenTranslate.Services;

public sealed class TranslationOrchestrator
{
    private const int ClipboardDelayMs = 120;
    private const int PasteDelayMs = 200;

    private readonly SecureSettingsStore _settingsStore;
    private readonly TranslationClient _translationClient;
    private readonly ClipboardService _clipboardService;
    private readonly KeyboardHookService _keyboardHookService;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public event EventHandler<string>? StatusChanged;
    public event EventHandler<string>? TranslationFailed;

    public TranslationOrchestrator(
        SecureSettingsStore settingsStore,
        TranslationClient translationClient,
        ClipboardService clipboardService,
        KeyboardHookService keyboardHookService)
    {
        _settingsStore = settingsStore;
        _translationClient = translationClient;
        _clipboardService = clipboardService;
        _keyboardHookService = keyboardHookService;
    }

    public Task TranslateClipboardAsync(
        bool fromShortcut = false,
        nint targetWindow = 0,
        nint targetControl = 0,
        bool clipboardAlreadyUpdated = false) =>
        TranslateClipboardCoreAsync(fromShortcut, targetWindow, targetControl, clipboardAlreadyUpdated);

    private async Task TranslateClipboardCoreAsync(
        bool fromShortcut,
        nint targetWindow,
        nint targetControl,
        bool clipboardAlreadyUpdated)
    {
        if (!await _gate.WaitAsync(0).ConfigureAwait(false))
            return;

        _keyboardHookService.Pause();

        try
        {
            var settings = _settingsStore.Load();

            if (settings.PlaySoundOnTranslationStart)
                TranslationSoundService.PlayTranslationStarted();

            SetStatus("Translating…");

            if (string.IsNullOrWhiteSpace(settings.GetActiveApiKey()))
            {
                Fail(TranslationProviders.GetApiKeyMissingMessage(settings.Provider));
                return;
            }

            if (fromShortcut)
            {
                await TryShowTooltipPendingAsync(targetWindow, targetControl, settings.TooltipFontSize)
                    .ConfigureAwait(false);
            }

            var source = await ResolveSourceTextAsync(fromShortcut, targetWindow, targetControl, clipboardAlreadyUpdated)
                .ConfigureAwait(false);

            if (source.Text is null)
            {
                Fail("No selected text or clipboard content to translate.");
                return;
            }

            if (source.IsEditable)
            {
                if (CanUseInlineSpinner(source.Window, source.Control)
                    && !EditableFieldSpinnerService.IsActive)
                {
                    await StartInlineSpinnerAsync(source).ConfigureAwait(false);
                }
            }
            else
            {
                await ShowTooltipPendingAsync(settings.TooltipFontSize).ConfigureAwait(false);
            }

            var translated = await _translationClient
                .TranslateAsync(source.Text, settings)
                .ConfigureAwait(false);

            await ApplyTranslationAsync(
                translated,
                source.Window,
                source.Control,
                source.ReplaceAll,
                source.PreferUiAutomationPaste,
                source.IsEditable,
                fromShortcut,
                settings.TooltipFontSize).ConfigureAwait(false);

            SetStatus("Done");
        }
        catch (TranslationApiException ex)
        {
            Fail(ex.Message);
        }
        catch (TaskCanceledException)
        {
            Fail("Translation timed out and was cancelled.");
        }
        catch (Exception ex)
        {
            Fail($"Unexpected error: {ex.Message}");
        }
        finally
        {
            _keyboardHookService.Resume();
            _gate.Release();
        }
    }

    private Task<(string? Text, nint Window, nint Control, bool ReplaceAll, bool PreferUiAutomationPaste, bool IsEditable)> ResolveSourceTextAsync(
        bool fromShortcut,
        nint targetWindow,
        nint targetControl,
        bool clipboardAlreadyUpdated)
    {
        return RunOnUiThreadAsync(async () =>
        {
            var window = targetWindow != 0
                ? targetWindow
                : InputSimulationService.GetForegroundWindow();

            var control = targetControl != 0
                ? targetControl
                : InputSimulationService.GetFocusedControl(window);

            if (fromShortcut)
                TryStartInlineSpinnerEarly(window, control, clipboardAlreadyUpdated);

            if (EditableFieldSpinnerService.IsActive)
            {
                var early = EditableFieldSpinnerService.GetCapturedSource();
                if (!string.IsNullOrWhiteSpace(early.Text))
                {
                    return (
                        early.Text,
                        early.Window,
                        early.Control,
                        early.ReplaceAll,
                        false,
                        true);
                }
            }

            if (fromShortcut)
                await Task.Delay(ClipboardDelayMs).ConfigureAwait(true);

            var preferFieldContent = ShouldPreferFieldContent(window, control);

            if (fromShortcut && clipboardAlreadyUpdated)
            {
                // The activating Ctrl+C may have left stale clipboard content if nothing was
                // selected. Re-copy and use the clipboard sequence number to confirm there is a
                // real selection before trusting it; otherwise fall through to whole-field capture.
                var verifiedSelection = KeyboardTextCaptureService.TryCaptureSelectionStrict(window, _clipboardService);
                if (verifiedSelection is not null && !string.IsNullOrWhiteSpace(verifiedSelection.Text))
                {
                    return (
                        verifiedSelection.Text,
                        window,
                        control,
                        false,
                        false,
                        preferFieldContent);
                }
            }
            else
            {
                var selectionCapture = TryCaptureEditableSelection(window, control, fromShortcut);
                if (selectionCapture is not null && !string.IsNullOrWhiteSpace(selectionCapture.Text))
                    return FromFieldCapture(selectionCapture, window, control, preferFieldContent);
            }

            if (preferFieldContent)
            {
                if (fromShortcut)
                {
                    var fullFieldCapture = TryCaptureEditableFieldContent(window, control);
                    if (fullFieldCapture is not null && !string.IsNullOrWhiteSpace(fullFieldCapture.Text))
                        return FromFieldCapture(fullFieldCapture, window, control, true);

                    var allCapture = KeyboardTextCaptureService.TryCaptureAll(window, _clipboardService);
                    if (allCapture is not null && !string.IsNullOrWhiteSpace(allCapture.Text))
                    {
                        return (
                            (string?)allCapture.Text,
                            window,
                            control,
                            true,
                            false,
                            true);
                    }
                }

                return (null, window, control, false, false, true);
            }

            var automationCapture = UiAutomationTextService.TryCapture(window, control);
            if (automationCapture is not null && !string.IsNullOrWhiteSpace(automationCapture.Text))
            {
                return (
                    (string?)automationCapture.Text,
                    window,
                    control,
                    automationCapture.ReplaceAll,
                    automationCapture.ReplaceAll,
                    false);
            }

            if (fromShortcut)
            {
                var allCapture = KeyboardTextCaptureService.TryCaptureAll(window, _clipboardService);
                if (allCapture is not null && !string.IsNullOrWhiteSpace(allCapture.Text))
                {
                    return (
                        (string?)allCapture.Text,
                        window,
                        control,
                        true,
                        false,
                        false);
                }
            }
            else
            {
                var clipboardText = _clipboardService.TryGetText();
                if (clipboardText is not null)
                {
                    return (
                        clipboardText,
                        window,
                        control,
                        false,
                        false,
                        false);
                }

                var keyboardCapture = KeyboardTextCaptureService.TryCapture(window, _clipboardService);
                if (keyboardCapture is not null && !string.IsNullOrWhiteSpace(keyboardCapture.Text))
                {
                    return (
                        (string?)keyboardCapture.Text,
                        window,
                        control,
                        keyboardCapture.ReplaceAll,
                        false,
                        false);
                }
            }

            return (null, window, control, false, false, true);
        });
    }

    private TextCaptureResult? TryCaptureEditableSelection(nint window, nint control, bool allowKeyboardCapture)
    {
        var resolvedControl = ResolveFocusedControl(window, control);

        if (resolvedControl != 0)
        {
            var peeked = TextControlService.TryPeekSelection(resolvedControl);
            if (peeked is not null)
                return peeked;
        }

        var automationSelection = UiAutomationTextService.TryCaptureSelection(window, resolvedControl);
        if (automationSelection is not null)
            return automationSelection;

        if (!allowKeyboardCapture || window == 0)
            return null;

        var keyboardSelection = KeyboardTextCaptureService.TryCaptureSelection(window, _clipboardService);
        if (keyboardSelection is null)
            return null;

        return new TextCaptureResult
        {
            Text = keyboardSelection.Text,
            Control = resolvedControl,
            ReplaceAll = false
        };
    }

    private static TextCaptureResult? TryCaptureEditableFieldContent(nint window, nint control)
    {
        var resolvedControl = ResolveFocusedControl(window, control);

        if (resolvedControl != 0)
        {
            var captured = TextControlService.TryCapture(resolvedControl);
            if (captured is not null)
                return captured;
        }

        return UiAutomationTextService.TryCapture(window, resolvedControl);
    }

    private static nint ResolveFocusedControl(nint window, nint control) =>
        control != 0
            ? control
            : window != 0
                ? InputSimulationService.GetFocusedControl(window)
                : 0;

    private static (string? Text, nint Window, nint Control, bool ReplaceAll, bool PreferUiAutomationPaste, bool IsEditable)
        FromFieldCapture(TextCaptureResult capture, nint window, nint control, bool isEditable) =>
        (
            capture.Text,
            window,
            capture.Control != 0 ? capture.Control : control,
            capture.ReplaceAll,
            false,
            isEditable);

    private void TryStartInlineSpinnerEarly(nint window, nint control, bool clipboardAlreadyUpdated)
    {
        if (!ShouldPreferFieldContent(window, control) || !CanUseInlineSpinner(window, control))
            return;

        TextCaptureResult? fieldCapture = null;

        if (clipboardAlreadyUpdated)
        {
            var clipboardText = _clipboardService.TryGetText();
            if (!string.IsNullOrWhiteSpace(clipboardText))
            {
                fieldCapture = new TextCaptureResult
                {
                    Text = TextFormattingHelper.NormalizeForTranslation(clipboardText),
                    Control = ResolveFocusedControl(window, control),
                    ReplaceAll = false
                };
            }
        }

        fieldCapture ??= TryCaptureEditableSelection(window, control, allowKeyboardCapture: false);

        if (fieldCapture is null || string.IsNullOrWhiteSpace(fieldCapture.Text))
            return;

        EditableFieldSpinnerService.TryStart(
            window,
            fieldCapture.Control != 0 ? fieldCapture.Control : control,
            fieldCapture.Text,
            fieldCapture.ReplaceAll);
    }

    private static bool CanUseInlineSpinner(nint window, nint control)
    {
        var resolvedControl = ResolveFocusedControl(window, control);
        return resolvedControl != 0 && TextControlService.IsTextInputControl(resolvedControl);
    }

    private static bool ShouldPreferFieldContent(nint window, nint control) =>
        ResolveEditability(window, control);

    private static bool ResolveEditability(nint window, nint control)
    {
        if (control != 0 && TextControlService.IsTextInputControl(control))
            return TextControlService.IsEditable(control);

        return UiAutomationTextService.TryGetEditability(window, control) ?? true;
    }

    private Task ApplyTranslationAsync(
        string translated,
        nint targetWindow,
        nint targetControl,
        bool replaceAll,
        bool preferUiAutomationPaste,
        bool isEditable,
        bool fromShortcut,
        double tooltipFontSize) =>
        RunOnUiThreadAsync(async () =>
        {
            if (!isEditable)
            {
                var tooltipWindow = fromShortcut && targetWindow != 0
                    ? targetWindow
                    : InputSimulationService.GetForegroundWindow();

                var tooltipControl = fromShortcut && targetControl != 0
                    ? targetControl
                    : InputSimulationService.GetFocusedControl(tooltipWindow);

                TranslationTooltipService.Update(
                    TextFormattingHelper.NormalizeForTranslation(translated),
                    tooltipFontSize,
                    tooltipWindow,
                    tooltipControl,
                    replaceAll);
                return;
            }

            var textToPaste = TextFormattingHelper.NormalizeForTranslation(translated);

            if (EditableFieldSpinnerService.IsActive)
            {
                EditableFieldSpinnerService.Complete(textToPaste);
                return;
            }

            TranslationTooltipService.CloseIfOpen();

            var pasteWindow = fromShortcut && targetWindow != 0
                ? targetWindow
                : InputSimulationService.GetForegroundWindow();

            var pasteControl = fromShortcut && targetControl != 0
                ? targetControl
                : InputSimulationService.GetFocusedControl(pasteWindow);

            if (preferUiAutomationPaste
                && UiAutomationTextService.TryApplyTranslation(pasteWindow, pasteControl, translated, replaceAll))
            {
                return;
            }

            if (!_clipboardService.TrySetText(textToPaste))
            {
                throw new InvalidOperationException(
                    "Could not write to the clipboard. Slack or another app may be blocking it; try again.");
            }

            await Task.Delay(PasteDelayMs).ConfigureAwait(true);

            InputSimulationService.PasteIntoWindow(pasteWindow, pasteControl, textToPaste, replaceAll);
        });

    private static Task RunOnUiThreadAsync(Func<Task> action)
    {
        var dispatcher = System.Windows.Application.Current.Dispatcher;
        if (dispatcher.CheckAccess())
            return action();

        return dispatcher.InvokeAsync(action).Task.Unwrap();
    }

    private static Task<T> RunOnUiThreadAsync<T>(Func<Task<T>> action)
    {
        var dispatcher = System.Windows.Application.Current.Dispatcher;
        if (dispatcher.CheckAccess())
            return action();

        return dispatcher.InvokeAsync(action).Task.Unwrap();
    }

    private void SetStatus(string message) => StatusChanged?.Invoke(this, message);

    private async Task TryShowTooltipPendingAsync(
        nint targetWindow,
        nint targetControl,
        double tooltipFontSize)
    {
        await RunOnUiThreadAsync(() =>
        {
            var window = targetWindow != 0
                ? targetWindow
                : InputSimulationService.GetForegroundWindow();

            var control = targetControl != 0
                ? targetControl
                : InputSimulationService.GetFocusedControl(window);

            if (!ShouldPreferFieldContent(window, control))
                TranslationTooltipService.ShowPending(tooltipFontSize);

            return Task.CompletedTask;
        }).ConfigureAwait(false);
    }

    private static Task ShowTooltipPendingAsync(double tooltipFontSize) =>
        RunOnUiThreadAsync(() =>
        {
            TranslationTooltipService.ShowPending(tooltipFontSize);
            return Task.CompletedTask;
        });

    private static Task StartInlineSpinnerAsync(
        (string? Text, nint Window, nint Control, bool ReplaceAll, bool PreferUiAutomationPaste, bool IsEditable) source) =>
        RunOnUiThreadAsync(() =>
        {
            TranslationTooltipService.CloseIfOpen();
            EditableFieldSpinnerService.TryStart(
                source.Window,
                source.Control,
                source.Text!,
                source.ReplaceAll);
            return Task.CompletedTask;
        });

    private void Fail(string message)
    {
        EditableFieldSpinnerService.Restore();
        TranslationTooltipService.CloseIfOpen();
        SetStatus("Error");
        TranslationFailed?.Invoke(this, message);
    }
}
