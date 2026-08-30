using System.Runtime.InteropServices;
using BepInEx.Configuration;
using UnityEngine;

namespace UseItemsAnywhere.QuickUseWheel;

internal static class QuickUseWheelShortcut
{
    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int virtualKey);

    internal static bool IsMainKeyPressed(KeyboardShortcut shortcut)
    {
        if (shortcut.IsPressed())
        {
            return true;
        }

        var virtualKey = GetVirtualKey(shortcut.MainKey);
        return virtualKey != 0 && (GetAsyncKeyState(virtualKey) & 0x8000) != 0;
    }

    private static int GetVirtualKey(KeyCode key)
    {
        var value = (int)key;
        if (value >= (int)KeyCode.A && value <= (int)KeyCode.Z)
        {
            return value - 32;
        }
        if (value >= (int)KeyCode.Alpha0 && value <= (int)KeyCode.Alpha9)
        {
            return value;
        }
        if (value >= (int)KeyCode.Keypad0 && value <= (int)KeyCode.Keypad9)
        {
            return 0x60 + value - (int)KeyCode.Keypad0;
        }
        if (value >= (int)KeyCode.F1 && value <= (int)KeyCode.F15)
        {
            return 0x70 + value - (int)KeyCode.F1;
        }

        return key switch
        {
            KeyCode.Backspace => 0x08,
            KeyCode.Tab => 0x09,
            KeyCode.Return or KeyCode.KeypadEnter => 0x0D,
            KeyCode.Pause => 0x13,
            KeyCode.CapsLock => 0x14,
            KeyCode.Escape => 0x1B,
            KeyCode.Space => 0x20,
            KeyCode.PageUp => 0x21,
            KeyCode.PageDown => 0x22,
            KeyCode.End => 0x23,
            KeyCode.Home => 0x24,
            KeyCode.LeftArrow => 0x25,
            KeyCode.UpArrow => 0x26,
            KeyCode.RightArrow => 0x27,
            KeyCode.DownArrow => 0x28,
            KeyCode.Insert => 0x2D,
            KeyCode.Delete => 0x2E,
            KeyCode.KeypadMultiply => 0x6A,
            KeyCode.KeypadPlus => 0x6B,
            KeyCode.KeypadMinus => 0x6D,
            KeyCode.KeypadPeriod => 0x6E,
            KeyCode.KeypadDivide => 0x6F,
            KeyCode.Numlock => 0x90,
            KeyCode.ScrollLock => 0x91,
            KeyCode.LeftShift => 0xA0,
            KeyCode.RightShift => 0xA1,
            KeyCode.LeftControl => 0xA2,
            KeyCode.RightControl => 0xA3,
            KeyCode.LeftAlt => 0xA4,
            KeyCode.RightAlt => 0xA5,
            KeyCode.Mouse0 => 0x01,
            KeyCode.Mouse1 => 0x02,
            KeyCode.Mouse2 => 0x04,
            _ => 0,
        };
    }
}
