using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;

namespace OpenTranslate.Services;

public static class InputSimulationService
{
    private const int SwRestore = 9;
    private const int AsfwAny = -1;
    private const int WmPaste = 0x0318;

    private const uint InputKeyboard = 1;
    private const uint KeyeventfKeyup = 0x0002;
    private const uint KeyeventfUnicode = 0x0004;
    private const ushort VkControl = 0x11;
    private const ushort VkA = 0x41;

    [DllImport("user32.dll")]
    public static extern nint GetForegroundWindow();

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

    [DllImport("user32.dll")]
    private static extern bool IsWindow(nint hWnd);

    [DllImport("user32.dll")]
    private static extern bool IsIconic(nint hWnd);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetForegroundWindow(nint hWnd);

    [DllImport("user32.dll")]
    private static extern bool BringWindowToTop(nint hWnd);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(nint hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(nint hWnd, out uint processId);

    [DllImport("user32.dll")]
    private static extern bool AttachThreadInput(uint attachTo, uint attachFrom, bool attach);

    [DllImport("user32.dll")]
    private static extern bool GetGUIThreadInfo(uint idThread, ref GuiThreadInfo info);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern nint SendMessage(nint hWnd, int msg, nint wParam, nint lParam);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern int GetClassName(nint hWnd, StringBuilder className, int maxCount);

    [DllImport("user32.dll")]
    private static extern bool AllowSetForegroundWindow(int processId);

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();

    [DllImport("user32.dll")]
    private static extern void SwitchToThisWindow(nint hWnd, bool turnOn);

    public static nint GetFocusedControl(nint targetWindow)
    {
        if (targetWindow == 0 || !IsWindow(targetWindow))
            return 0;

        var threadId = GetWindowThreadProcessId(targetWindow, out _);
        var info = new GuiThreadInfo { CbSize = (uint)Marshal.SizeOf<GuiThreadInfo>() };
        return GetGUIThreadInfo(threadId, ref info) ? info.HwndFocus : 0;
    }

    public static void PasteIntoWindow(nint targetWindow, nint targetControl, string translatedText, bool replaceAll = false)
    {
        if (replaceAll && targetWindow != 0 && IsWindow(targetWindow))
        {
            RestoreWindowFocus(targetWindow);
            Thread.Sleep(80);
            SendCtrlA();
            Thread.Sleep(50);
        }

        var control = targetControl != 0 && IsWindow(targetControl)
            ? targetControl
            : targetWindow != 0
                ? GetFocusedControl(targetWindow)
                : 0;

        if (control != 0 && TextControlService.IsTextInputControl(control))
        {
            if (replaceAll)
                TextControlService.SelectAll(control);

            if (TextControlService.TryReplaceSelection(control, translatedText))
                return;

            SendMessage(control, WmPaste, 0, 0);
            Thread.Sleep(80);
            return;
        }

        PasteWithKeyboard(targetWindow);
    }

    public static void PasteIntoWindow(nint targetWindow, string translatedText)
    {
        _ = translatedText;
        PasteWithKeyboard(targetWindow);
    }

    /// <summary>
    /// Types <paramref name="text"/> into the target window character by character (a fast
    /// typewriter effect) using synthesized Unicode key events. Returns false if the target
    /// window could not be focused, so the caller can fall back to a regular paste.
    /// </summary>
    public static bool TypeIntoWindow(nint targetWindow, nint targetControl, string text, bool replaceAll, int perCharDelayMicroseconds)
    {
        _ = targetControl;

        if (string.IsNullOrEmpty(text) || targetWindow == 0 || !IsWindow(targetWindow))
            return false;

        RestoreWindowFocus(targetWindow);
        Thread.Sleep(120);

        if (GetForegroundWindow() != targetWindow)
            return false;

        if (replaceAll)
        {
            SendKeyChord(VkControl, VkA);
            Thread.Sleep(40);
        }

        foreach (var ch in text)
        {
            SendUnicodeChar(ch);
            if (perCharDelayMicroseconds > 0)
                SpinWaitMicroseconds(perCharDelayMicroseconds);
        }

        return true;
    }

    private static void SpinWaitMicroseconds(int microseconds)
    {
        var ticks = (long)(microseconds * (Stopwatch.Frequency / 1_000_000.0));
        var start = Stopwatch.GetTimestamp();
        while (Stopwatch.GetTimestamp() - start < ticks)
            Thread.SpinWait(1);
    }

    private static void SendUnicodeChar(char ch)
    {
        var inputs = new[]
        {
            KeyboardInput(0, ch, KeyeventfUnicode),
            KeyboardInput(0, ch, KeyeventfUnicode | KeyeventfKeyup)
        };

        SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<INPUT>());
    }

    private static void SendKeyChord(ushort modifier, ushort key)
    {
        var inputs = new[]
        {
            KeyboardInput(modifier, 0, 0),
            KeyboardInput(key, 0, 0),
            KeyboardInput(key, 0, KeyeventfKeyup),
            KeyboardInput(modifier, 0, KeyeventfKeyup)
        };

        SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<INPUT>());
    }

    private static INPUT KeyboardInput(ushort virtualKey, ushort scanCode, uint flags) => new()
    {
        Type = InputKeyboard,
        U = new InputUnion
        {
            Keyboard = new KEYBDINPUT
            {
                Vk = virtualKey,
                Scan = scanCode,
                Flags = flags,
                Time = 0,
                DwExtraInfo = nint.Zero
            }
        }
    };

    private static void PasteWithKeyboard(nint targetWindow)
    {
        if (targetWindow != 0 && IsWindow(targetWindow))
        {
            for (var attempt = 0; attempt < 5; attempt++)
            {
                if (TryFocusAndPaste(targetWindow))
                    return;

                Thread.Sleep(80 + attempt * 40);
            }
        }

        SendCtrlVRaw();
    }

    public static void RestoreFocusForCapture(nint targetWindow) =>
        RestoreWindowFocus(targetWindow);

    public static void SendCtrlA() => SendChord("^a");

    public static void SendCtrlC() => SendChord("^c");

    private static void SendChord(string keys)
    {
        SendKeys.SendWait(keys);
    }

    private static void RestoreWindowFocus(nint targetWindow)
    {
        if (targetWindow == 0 || !IsWindow(targetWindow))
            return;

        var foreground = GetForegroundWindow();
        var foregroundThread = foreground != 0 ? GetWindowThreadProcessId(foreground, out _) : 0;
        var targetThread = GetWindowThreadProcessId(targetWindow, out _);
        var currentThread = GetCurrentThreadId();

        GetWindowThreadProcessId(targetWindow, out var targetProcessId);
        AllowSetForegroundWindow(AsfwAny);
        AllowSetForegroundWindow((int)targetProcessId);

        var attachedToForeground = false;
        var attachedToTarget = false;

        try
        {
            if (foregroundThread != 0 && foregroundThread != currentThread)
                attachedToForeground = AttachThreadInput(currentThread, foregroundThread, true);

            if (targetThread != currentThread && targetThread != foregroundThread)
                attachedToTarget = AttachThreadInput(currentThread, targetThread, true);

            // SW_RESTORE on a maximized window un-maximizes it; only use it when minimized.
            if (IsIconic(targetWindow))
                ShowWindow(targetWindow, SwRestore);

            SwitchToThisWindow(targetWindow, true);
            BringWindowToTop(targetWindow);
            SetForegroundWindow(targetWindow);
        }
        finally
        {
            if (attachedToTarget)
                AttachThreadInput(currentThread, targetThread, false);

            if (attachedToForeground)
                AttachThreadInput(currentThread, foregroundThread, false);
        }
    }

    private static string GetWindowClassName(nint hWnd)
    {
        var buffer = new StringBuilder(256);
        _ = GetClassName(hWnd, buffer, buffer.Capacity);
        return buffer.ToString();
    }

    private static bool TryFocusAndPaste(nint targetWindow)
    {
        RestoreWindowFocus(targetWindow);
        Thread.Sleep(150);

        if (GetForegroundWindow() != targetWindow)
            return false;

        SendCtrlVRaw();
        return true;
    }

    private static void SendCtrlVRaw() => SendChord("^v");

    [StructLayout(LayoutKind.Sequential)]
    private struct INPUT
    {
        public uint Type;
        public InputUnion U;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct InputUnion
    {
        [FieldOffset(0)] public MOUSEINPUT Mouse;
        [FieldOffset(0)] public KEYBDINPUT Keyboard;
        [FieldOffset(0)] public HARDWAREINPUT Hardware;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KEYBDINPUT
    {
        public ushort Vk;
        public ushort Scan;
        public uint Flags;
        public uint Time;
        public nint DwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MOUSEINPUT
    {
        public int Dx;
        public int Dy;
        public uint MouseData;
        public uint Flags;
        public uint Time;
        public nint DwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct HARDWAREINPUT
    {
        public uint Msg;
        public ushort ParamL;
        public ushort ParamH;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct GuiThreadInfo
    {
        public uint CbSize;
        public uint Flags;
        public nint HwndActive;
        public nint HwndFocus;
        public nint HwndCapture;
        public nint HwndMenuOwner;
        public nint HwndMoveSize;
        public nint HwndCaret;
        public RECT RcCaret;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }
}
