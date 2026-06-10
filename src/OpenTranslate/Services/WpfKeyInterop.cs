using System.Windows.Interop;
using System.Windows.Input;
using OpenTranslate.Models;

namespace OpenTranslate.Services;

public static class WpfKeyInterop
{
    public static bool IsModifierKey(Key key) =>
        key is Key.LeftCtrl or Key.RightCtrl
            or Key.LeftAlt or Key.RightAlt
            or Key.LeftShift or Key.RightShift
            or Key.LWin or Key.RWin;

    public static ActivationShortcut FromKeyEvent(Key key, Key systemKey, ModifierKeys modifiers)
    {
        var resolved = key == Key.System ? systemKey : key;

        return new ActivationShortcut
        {
            KeyCode = KeyInterop.VirtualKeyFromKey(resolved),
            Control = modifiers.HasFlag(ModifierKeys.Control),
            Alt = modifiers.HasFlag(ModifierKeys.Alt),
            Shift = modifiers.HasFlag(ModifierKeys.Shift)
        };
    }
}
