using System.Text;
using System.Windows.Forms;
using OpenTranslate.Models;

namespace OpenTranslate.Services;

public static class ShortcutFormatter
{
    public static string Format(ActivationShortcut shortcut)
    {
        var parts = new List<string>();

        if (shortcut.Control)
            parts.Add("Ctrl");
        if (shortcut.Alt)
            parts.Add("Alt");
        if (shortcut.Shift)
            parts.Add("Shift");

        parts.Add(FormatKey((Keys)shortcut.KeyCode));

        var combo = string.Join("+", parts);
        return shortcut.DoublePress ? $"{combo} (twice)" : combo;
    }

    private static string FormatKey(Keys key) => key switch
    {
        >= Keys.A and <= Keys.Z => key.ToString(),
        >= Keys.D0 and <= Keys.D9 => key.ToString()[1..],
        >= Keys.NumPad0 and <= Keys.NumPad9 => "Num" + (key - Keys.NumPad0),
        Keys.Space => "Space",
        Keys.Enter => "Enter",
        Keys.Escape => "Esc",
        Keys.Tab => "Tab",
        Keys.Back => "Backspace",
        Keys.Delete => "Delete",
        Keys.Insert => "Insert",
        Keys.Home => "Home",
        Keys.End => "End",
        Keys.PageUp => "Page Up",
        Keys.PageDown => "Page Down",
        Keys.Up => "↑",
        Keys.Down => "↓",
        Keys.Left => "←",
        Keys.Right => "→",
        Keys.F1 => "F1",
        Keys.F2 => "F2",
        Keys.F3 => "F3",
        Keys.F4 => "F4",
        Keys.F5 => "F5",
        Keys.F6 => "F6",
        Keys.F7 => "F7",
        Keys.F8 => "F8",
        Keys.F9 => "F9",
        Keys.F10 => "F10",
        Keys.F11 => "F11",
        Keys.F12 => "F12",
        _ => key.ToString()
    };
}
