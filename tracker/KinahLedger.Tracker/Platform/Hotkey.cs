using System.Windows.Input;

namespace KinahLedger.Platform;

/// <summary>
/// The start/stop shortcut as the player writes it ("Ctrl+Shift+K") and as Windows registers it. WPF's ModifierKeys
/// values (Alt 1, Control 2, Shift 4, Windows 8) are the same numbers RegisterHotKey takes.
/// </summary>
public readonly record struct Hotkey(ModifierKeys Modifiers, Key Key)
{
    public const string Default = "Ctrl+Shift+K";
    const uint MOD_NOREPEAT = 0x4000;

    public uint NativeModifiers => (uint)Modifiers | MOD_NOREPEAT;
    public uint VirtualKey => (uint)KeyInterop.VirtualKeyFromKey(Key);

    /// <summary>"Ctrl+Shift+K", "Alt+F9", "Ctrl+Alt+1". Null for empty, "Off", or anything without a modifier and a key.</summary>
    public static Hotkey? Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text) || text.Trim().Equals("off", StringComparison.OrdinalIgnoreCase)) return null;
        var parts = text.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length < 2) return null;
        var mods = ModifierKeys.None;
        foreach (var p in parts[..^1])
        {
            switch (p.ToLowerInvariant())
            {
                case "ctrl": case "control": mods |= ModifierKeys.Control; break;
                case "alt": mods |= ModifierKeys.Alt; break;
                case "shift": mods |= ModifierKeys.Shift; break;
                case "win": case "windows": mods |= ModifierKeys.Windows; break;
                default: return null;
            }
        }
        var k = KeyFrom(parts[^1]);
        return k is { } key && IsUsable(mods, key) ? new Hotkey(mods, key) : null;
    }

    static Key? KeyFrom(string s)
    {
        if (s.Length == 1 && char.IsDigit(s[0])) return Key.D0 + (s[0] - '0');
        return Enum.TryParse<Key>(s, ignoreCase: true, out var k) ? k : null;
    }

    /// <summary>At least one modifier, and a key that isn't itself a modifier.</summary>
    public static bool IsUsable(ModifierKeys mods, Key key) =>
        mods != ModifierKeys.None && key is not (Key.None or Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt
            or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin or Key.System or Key.Escape or Key.Tab or Key.Back);

    public override string ToString()
    {
        var parts = new List<string>();
        if (Modifiers.HasFlag(ModifierKeys.Control)) parts.Add("Ctrl");
        if (Modifiers.HasFlag(ModifierKeys.Alt)) parts.Add("Alt");
        if (Modifiers.HasFlag(ModifierKeys.Shift)) parts.Add("Shift");
        if (Modifiers.HasFlag(ModifierKeys.Windows)) parts.Add("Win");
        parts.Add(Key is >= Key.D0 and <= Key.D9 ? ((int)(Key - Key.D0)).ToString() : Key.ToString());
        return string.Join("+", parts);
    }
}
