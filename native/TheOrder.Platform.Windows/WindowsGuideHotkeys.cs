using System.Runtime.InteropServices;

namespace TheOrder.Platform.Windows;

public sealed class GuideHotkeyUnavailableException(string message) : InvalidOperationException(message);

public static class WindowsGuideHotkeys
{
    public const int DefaultKey = 0x4a;
    // Low byte: Windows virtual key. High word: MOD_ALT / CONTROL / SHIFT.
    // Plain J keeps its original value, so the session header remains compatible.
    public static int Key(int shortcut) => shortcut & 0xff;
    public static int Modifiers(int shortcut) => (shortcut >> 16) & 7;
    public static int FromLegacy(int shortcut) => shortcut is 0xbf or 0x77 or 0x78 or 0x79 or 0x7a ? DefaultKey : shortcut;
    public static bool IsSupported(int shortcut)
    {
        if ((shortcut & ~0x700ff) != 0) return false;
        int key = Key(shortcut);
        // Navigation, modifier-only and system-reserved keys stay with Windows
        // and the reader. Letters, digits, F1-F11 and punctuation are recordable.
        if (!(key is >= 0x30 and <= 0x39 or >= 0x41 and <= 0x5a or >= 0x60 and <= 0x6b
            or >= 0x6d and <= 0x7a or >= 0xba and <= 0xc0 or >= 0xdb and <= 0xde)) return false;
        return !(Key(shortcut) == 0x73 && (Modifiers(shortcut) & 1) != 0); // Alt+F4
    }
    public static string Label(int shortcut)
    {
        int key = Key(shortcut), modifiers = Modifiers(shortcut);
        string name = key switch
        {
            >= 0x30 and <= 0x39 or >= 0x41 and <= 0x5a => ((char)key).ToString(),
            >= 0x70 and <= 0x7a => "F" + (key - 0x6f),
            >= 0x60 and <= 0x69 => "数字键盘 " + (key - 0x60),
            0x6a => "数字键盘 *", 0x6b => "数字键盘 +", 0x6d => "数字键盘 -", 0x6e => "数字键盘 .", 0x6f => "数字键盘 /",
            0xba => ";", 0xbb => "=", 0xbc => ",", 0xbd => "-", 0xbe => ".", 0xbf => "/", 0xc0 => "`",
            0xdb => "[", 0xdc => "\\", 0xdd => "]", 0xde => "'", _ => "未设置"
        };
        return ((modifiers & 2) != 0 ? "Ctrl+" : "") + ((modifiers & 4) != 0 ? "Shift+" : "")
            + ((modifiers & 1) != 0 ? "Alt+" : "") + name;
    }
    // Probe and release on the same thread. A successful probe describes the current instant only.
    public static (bool Available, string Message) Check(int key)
    {
        if (!IsSupported(key)) throw new ArgumentOutOfRangeException(nameof(key));
        const int id = 0x6173;
        bool held = RegisterHotKey(IntPtr.Zero, id, 0x4000 | (uint)Modifiers(key), (uint)Key(key));
        int error = held ? 0 : Marshal.GetLastWin32Error();
        try
        {
            return held ? (true, Label(key) + " 当前可用；游戏运行时仍会检查占用。")
                : (false, error == 1409 ? Label(key) + " 已被其他程序占用，请选择另一个快捷键。"
                    : Label(key) + " 暂时无法注册，请选择另一个快捷键。");
        }
        finally { if (held) UnregisterHotKey(IntPtr.Zero, id); }
    }
    public static void RequireAvailable(int key)
    { var result = Check(key); if (!result.Available) throw new GuideHotkeyUnavailableException(result.Message); }
    [DllImport("user32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RegisterHotKey(IntPtr window, int id, uint modifiers, uint key);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnregisterHotKey(IntPtr window, int id);
}
