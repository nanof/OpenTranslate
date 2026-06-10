using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;

namespace OpenTranslate.Services;

public static class InputSimulationService
{
    private const int SwRestore = 9;
    private const int AsfwAny = -1;
    private const int WmPaste = 0x0318;
    private const int EmReplaceSel = 0x040E;
    private const int EmReplaceSelRich = 0x0432;

    [DllImport("user32.dll")]
    public static extern nint GetForegroundWindow();

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

        if (targetControl != 0 && IsWindow(targetControl))
        {
            if (replaceAll)
                TextControlService.SelectAll(targetControl);

            if (TryReplaceSelection(targetControl, translatedText))
                return;

            SendMessage(targetControl, WmPaste, 0, 0);
            Thread.Sleep(80);
        }

        PasteIntoWindow(targetWindow, translatedText);
    }

    public static void PasteIntoWindow(nint targetWindow, string translatedText)
    {
        if (targetWindow != 0 && IsWindow(targetWindow))
        {
            var focusedControl = GetFocusedControl(targetWindow);
            if (focusedControl != 0)
            {
                if (TryReplaceSelection(focusedControl, translatedText))
                    return;

                SendMessage(focusedControl, WmPaste, 0, 0);
                Thread.Sleep(80);

                if (GetForegroundWindow() == targetWindow)
                {
                    SendCtrlVRaw();
                    return;
                }
            }
        }

        if (targetWindow != 0 && IsWindow(targetWindow))
        {
            for (var attempt = 0; attempt < 5; attempt++)
            {
                if (TryFocusAndPaste(targetWindow))
                    return;

                Thread.Sleep(80 + attempt * 40);
            }
        }

        SendKeys.SendWait("^v");
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

    private static bool TryReplaceSelection(nint control, string text)
    {
        var textPtr = Marshal.StringToHGlobalUni(text);
        try
        {
            var result = SendMessage(control, EmReplaceSel, 1, textPtr);
            if (result != 0)
                return true;

            var className = GetWindowClassName(control);
            if (className.Contains("RichEdit", StringComparison.OrdinalIgnoreCase))
            {
                result = SendMessage(control, EmReplaceSelRich, 1, textPtr);
                return result != 0;
            }

            return false;
        }
        finally
        {
            Marshal.FreeHGlobal(textPtr);
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
