using System.Runtime.InteropServices;
using System.Text;

namespace OpenTranslate.Services;

public sealed class TextCaptureResult
{
    public required string Text { get; init; }
    public required nint Control { get; init; }
    public bool ReplaceAll { get; init; }
}

public static class TextControlService
{
    private const int WmGetText = 0x000D;
    private const int WmGetTextLength = 0x000E;
    private const int GwlStyle = -16;
    private const int EsReadonly = 0x08000000;
    private const int EmGetSel = 0x00B0;
    private const int EmSetSel = 0x00B1;
    private const int EmGetReadonly = 0x00CF;

    [DllImport("user32.dll")]
    private static extern bool IsWindow(nint hWnd);

    [DllImport("user32.dll", EntryPoint = "GetWindowLong")]
    private static extern int GetWindowLong32(nint hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtr")]
    private static extern nint GetWindowLongPtr64(nint hWnd, int nIndex);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern nint SendMessage(nint hWnd, int msg, nint wParam, nint lParam);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern int GetClassName(nint hWnd, StringBuilder className, int maxCount);

    public static bool IsTextInputControl(nint control)
    {
        if (control == 0 || !IsWindow(control))
            return false;

        var className = GetWindowClassName(control);
        return className.Contains("Edit", StringComparison.OrdinalIgnoreCase)
               || className.Contains("RichEdit", StringComparison.OrdinalIgnoreCase)
               || className.Equals("Scintilla", StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsEditable(nint control)
    {
        if (!IsTextInputControl(control))
            return false;

        if (SendMessage(control, EmGetReadonly, 0, 0) != 0)
            return false;

        var style = IntPtr.Size == 8
            ? (int)(GetWindowLongPtr64(control, GwlStyle) & 0xFFFFFFFF)
            : GetWindowLong32(control, GwlStyle);

        return (style & EsReadonly) == 0;
    }

    public static TextCaptureResult? TryCapture(nint control)
    {
        if (!IsTextInputControl(control))
            return null;

        var allText = GetText(control);
        if (string.IsNullOrWhiteSpace(allText))
            return null;

        var (start, end) = GetSelectionRange(control);

        if (start != end && start >= 0 && end <= allText.Length && end > start)
        {
            return new TextCaptureResult
            {
                Text = TextFormattingHelper.NormalizeForTranslation(allText[start..end]),
                Control = control,
                ReplaceAll = false
            };
        }

        SelectAll(control);
        return new TextCaptureResult
        {
            Text = TextFormattingHelper.NormalizeForTranslation(allText),
            Control = control,
            ReplaceAll = true
        };
    }

    public static void SelectAll(nint control) =>
        SendMessage(control, EmSetSel, 0, -1);

    private static (int Start, int End) GetSelectionRange(nint control)
    {
        var result = SendMessage(control, EmGetSel, 0, 0);
        var value = result.ToInt64();
        return ((int)(value & 0xFFFF), (int)((value >> 16) & 0xFFFF));
    }

    private static string GetText(nint control)
    {
        var length = SendMessage(control, WmGetTextLength, 0, 0).ToInt32();
        if (length <= 0)
            return "";

        var ptr = Marshal.AllocHGlobal((length + 1) * 2);
        try
        {
            SendMessage(control, WmGetText, length + 1, ptr);
            return Marshal.PtrToStringUni(ptr) ?? "";
        }
        finally
        {
            Marshal.FreeHGlobal(ptr);
        }
    }

    private static string GetWindowClassName(nint hWnd)
    {
        var buffer = new StringBuilder(256);
        _ = GetClassName(hWnd, buffer, buffer.Capacity);
        return buffer.ToString();
    }
}
