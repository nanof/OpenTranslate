using System.Runtime.InteropServices;
using Gma.System.MouseKeyHook;
using OpenTranslate.Models;
using System.Windows.Forms;

namespace OpenTranslate.Services;

public sealed class KeyboardHookService : IDisposable
{
    private const int VkControl = 0x11;
    private const int VkMenu = 0x12;
    private const int VkShift = 0x10;
    private const int CopyActivationDelayMs = 120;

    private readonly IKeyboardMouseEvents _hook;
    private ActivationShortcut _shortcut = ActivationShortcut.Default;
    private DateTime _lastPressTime = DateTime.MinValue;
    private CancellationTokenSource? _copyActivationDelayCts;
    private bool _paused;

    public event EventHandler<ShortcutActivatedEventArgs>? ShortcutActivated;

    public KeyboardHookService()
    {
        _hook = Hook.GlobalEvents();
        _hook.KeyDown += OnKeyDown;
    }

    public void UpdateShortcut(ActivationShortcut shortcut)
    {
        _shortcut = shortcut.IsValid ? shortcut : ActivationShortcut.Default;
        ResetDoublePressState();
    }

    public void Pause() => _paused = true;

    public void Resume() => _paused = false;

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (_paused || !MatchesShortcut(e, _shortcut))
            return;

        if (_shortcut.DoublePress)
        {
            var now = DateTime.UtcNow;
            var windowMs = Math.Clamp(_shortcut.DoublePressWindowMs, 200, 2000);

            if (_lastPressTime != DateTime.MinValue
                && (now - _lastPressTime).TotalMilliseconds <= windowMs)
            {
                _lastPressTime = DateTime.MinValue;

                if (IsCopyShortcut(_shortcut))
                {
                    ScheduleCopyShortcutActivation();
                    return;
                }

                RaiseShortcutActivated(false, 0, 0);
            }
            else
            {
                _lastPressTime = now;
                CancelScheduledCopyActivation();
            }

            return;
        }

        RaiseShortcutActivated(false, 0, 0);
    }

    private void ScheduleCopyShortcutActivation()
    {
        var targetWindow = InputSimulationService.GetForegroundWindow();
        var targetControl = InputSimulationService.GetFocusedControl(targetWindow);

        CancelScheduledCopyActivation();
        _copyActivationDelayCts = new CancellationTokenSource();
        var token = _copyActivationDelayCts.Token;

        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(CopyActivationDelayMs, token).ConfigureAwait(false);
                if (token.IsCancellationRequested || _paused)
                    return;

                RaiseShortcutActivated(clipboardAlreadyUpdated: true, targetWindow, targetControl);
            }
            catch (OperationCanceledException)
            {
                // Another key press cancelled this activation.
            }
        });
    }

    private void CancelScheduledCopyActivation()
    {
        _copyActivationDelayCts?.Cancel();
        _copyActivationDelayCts?.Dispose();
        _copyActivationDelayCts = null;
    }

    private void RaiseShortcutActivated(bool clipboardAlreadyUpdated, nint targetWindow, nint targetControl)
    {
        if (targetWindow == 0)
            targetWindow = InputSimulationService.GetForegroundWindow();

        if (targetControl == 0)
            targetControl = InputSimulationService.GetFocusedControl(targetWindow);

        ShortcutActivated?.Invoke(this, new ShortcutActivatedEventArgs
        {
            ClipboardAlreadyUpdated = clipboardAlreadyUpdated,
            TargetWindow = targetWindow,
            TargetControl = targetControl
        });
    }

    private static bool IsCopyShortcut(ActivationShortcut shortcut) =>
        shortcut.Control
        && !shortcut.Alt
        && !shortcut.Shift
        && shortcut.KeyCode == (int)Keys.C;

    private static bool MatchesShortcut(KeyEventArgs e, ActivationShortcut shortcut)
    {
        if (ActivationShortcut.IsModifierKey(e.KeyCode))
            return false;

        var controlPressed = e.Control || IsKeyPressed(VkControl);
        var altPressed = e.Alt || IsKeyPressed(VkMenu);
        var shiftPressed = e.Shift || IsKeyPressed(VkShift);

        return e.KeyCode == (Keys)shortcut.KeyCode
               && controlPressed == shortcut.Control
               && altPressed == shortcut.Alt
               && shiftPressed == shortcut.Shift;
    }

    private static bool IsKeyPressed(int virtualKey) =>
        (GetAsyncKeyState(virtualKey) & 0x8000) != 0;

    private void ResetDoublePressState()
    {
        _lastPressTime = DateTime.MinValue;
        CancelScheduledCopyActivation();
    }

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int virtualKey);

    public void Dispose()
    {
        _hook.KeyDown -= OnKeyDown;
        ResetDoublePressState();
        _hook.Dispose();
    }
}
