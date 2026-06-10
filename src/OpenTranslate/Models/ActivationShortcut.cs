using System.Windows.Forms;

namespace OpenTranslate.Models;

public sealed class ActivationShortcut
{
    public int KeyCode { get; set; } = (int)Keys.T;
    public bool Control { get; set; } = true;
    public bool Alt { get; set; }
    public bool Shift { get; set; } = true;
    public bool DoublePress { get; set; }
    public int DoublePressWindowMs { get; set; } = 500;

    public static ActivationShortcut Default => new();

    public bool IsValid =>
        KeyCode != 0 && !IsModifierKey((Keys)KeyCode);

    public static bool IsModifierKey(Keys key) =>
        key is Keys.ControlKey or Keys.LControlKey or Keys.RControlKey
            or Keys.Menu or Keys.LMenu or Keys.RMenu
            or Keys.ShiftKey or Keys.LShiftKey or Keys.RShiftKey
            or Keys.LWin or Keys.RWin;
}
