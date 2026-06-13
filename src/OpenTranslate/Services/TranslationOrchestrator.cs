using OpenTranslate.Models;

namespace OpenTranslate.Services;

public sealed class TranslationOrchestrator
{
    private const int ClipboardDelayMs = 120;
    private const int PasteDelayMs = 200;
    private const int OperationTimeoutMs = 20000;

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

        var released = 0;

        void ReleaseOnce()
        {
            if (Interlocked.Exchange(ref released, 1) != 0)
                return;

            _keyboardHookService.Resume();
            _gate.Release();
        }

        using var watchdogCts = new CancellationTokenSource();

        // Safety net: even if a capture/paste step hangs (e.g. an unresponsive app's UI
        // Automation provider), guarantee the hook and gate are released so the app keeps
        // working everywhere else.
        _ = Task.Delay(OperationTimeoutMs, watchdogCts.Token)
            .ContinueWith(
                _ => ReleaseOnce(),
                CancellationToken.None,
                TaskContinuationOptions.OnlyOnRanToCompletion,
                TaskScheduler.Default);

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

            // Show feedback immediately, before any (potentially slow) text capture or UI
            // Automation work, so the spinner appears right after the activation sound.
            if (fromShortcut)
                await ShowSpinnerOnlyAsync(settings.TooltipFontSize).ConfigureAwait(false);

            var source = await ResolveSourceTextAsync(fromShortcut, targetWindow, targetControl, clipboardAlreadyUpdated)
                .ConfigureAwait(false);

            if (source.Text is null)
            {
                Fail("No selected text or clipboard content to translate.");
                return;
            }

            // For shortcut activations the floating spinner was already shown above. For other
            // activations (e.g. tray menu) show the appropriate feedback now.
            if (!fromShortcut)
            {
                if (source.IsEditable)
                    await ShowSpinnerOnlyAsync(settings.TooltipFontSize).ConfigureAwait(false);
                else
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
            watchdogCts.Cancel();
            ReleaseOnce();
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

            // Computed once and reused to avoid repeating the (potentially slow) editability
            // probe in non-Win32 apps.
            var preferFieldContent = ShouldPreferFieldContent(window, control);

            if (fromShortcut)
                await Task.Delay(ClipboardDelayMs).ConfigureAwait(true);

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
                    // Native Win32 controls expose their text instantly via window messages.
                    var win32Capture = TryCaptureWin32FieldContent(window, control);
                    if (win32Capture is not null && !string.IsNullOrWhiteSpace(win32Capture.Text))
                        return FromFieldCapture(win32Capture, window, control, true);

                    // For non-Win32 (Electron) fields, a keyboard select-all+copy is far faster and
                    // more reliable than a full UI Automation tree walk, so try it before UI Automation.
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

                    var fieldCapture = TryCaptureEditableFieldContent(window, control);
                    if (fieldCapture is not null && !string.IsNullOrWhiteSpace(fieldCapture.Text))
                        return FromFieldCapture(fieldCapture, window, control, true);
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

    private static TextCaptureResult? TryCaptureWin32FieldContent(nint window, nint control)
    {
        var resolvedControl = ResolveFocusedControl(window, control);
        if (resolvedControl == 0)
            return null;

        return TextControlService.TryCapture(resolvedControl);
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

                // The tooltip is only shown for non-editable targets, so a paste-back would not
                // work; hide the Replace action in that case.
                var canReplace = ResolveEditability(tooltipWindow, tooltipControl);

                TranslationTooltipService.Update(
                    TextFormattingHelper.NormalizeForTranslation(translated),
                    tooltipFontSize,
                    tooltipWindow,
                    tooltipControl,
                    replaceAll,
                    canReplace);
                return;
            }

            var textToPaste = TextFormattingHelper.NormalizeForTranslation(translated);

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

    private static Task ShowTooltipPendingAsync(double tooltipFontSize) =>
        RunOnUiThreadAsync(() =>
        {
            TranslationTooltipService.ShowPending(tooltipFontSize);
            return Task.CompletedTask;
        });

    private static Task ShowSpinnerOnlyAsync(double tooltipFontSize) =>
        RunOnUiThreadAsync(() =>
        {
            TranslationTooltipService.ShowPending(tooltipFontSize, spinnerOnly: true);
            return Task.CompletedTask;
        });

    private void Fail(string message)
    {
        TranslationTooltipService.CloseIfOpen();
        SetStatus("Error");
        TranslationFailed?.Invoke(this, message);
    }
}
