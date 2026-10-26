using System.Windows.Input;

namespace KinahLedger.Platform;

/// <summary>
/// The start/stop shortcut as the player writes it ("Ctrl+Shift+K") and as Windows registers it. WPF's ModifierKeys
/// values (Alt 1, Control 2, Shift 4, Windows 8) are the same numbers RegisterHotKey takes.
/// </summary>
public readonly record struct Hotkey(ModifierKeys Modifiers, Key Key)
{
    public const string Default = "Ctrl+Shift+K";
    /// <summary>
    /// The ids the window registers its shortcuts under (RegisterHotKey): start/stop, and "That was wrong" (2026-10-26,
    /// off until the player picks a key), which marks the newest counted line wrong and saves the last minute for a report.
    /// </summary>
    public const int StartStopId = 1, MarkWrongId = 2;
    const uint MOD_NOREPEAT = 0x4000;

    public uint NativeModifiers => (uint)Modifiers | MOD_NOREPEAT;
    public uint VirtualKey => (uint)KeyInterop.VirtualKeyFromKey(Key);

    /// <summary>"Ctrl+Shift+K", "Alt+F9", "Ctrl+Alt+1". Null for empty, "Off", or anything without a modifier and a key.</summary>
    public static Hotkey? Parse(string? text) => Read(text) is { } h && IsUsable(h.Modifiers, h.Key) ? h : null;

    /// <summary>
    /// Why a saved shortcut can't be used (a sentence), or null when it can or there is none. A shortcut saved before the
    /// rules below (Ctrl+K, say) is turned off at start, and this says why.
    /// </summary>
    public static string? Explain(string? text) => Read(text) is { } h ? Refusal(h.Modifiers, h.Key) : null;

    /// <summary>The text as modifiers and a key, whether or not it is allowed; null for empty, "Off" or what isn't a shortcut.</summary>
    static Hotkey? Read(string? text)
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
        return KeyFrom(parts[^1]) is { } key ? new Hotkey(mods, key) : null;
    }

    static Key? KeyFrom(string s)
    {
        if (s.Length == 1 && char.IsDigit(s[0])) return Key.D0 + (s[0] - '0');
        return Enum.TryParse<Key>(s, ignoreCase: true, out var k) ? k : null;
    }

    /// <summary>True when two saved shortcuts are the same keys ("ctrl+shift+k" and "Ctrl+Shift+K"); false when either is off.</summary>
    public static bool Same(string? a, string? b) => Read(a) is { } x && Read(b) is { } y && x == y;

    /// <summary>A combination the shortcut may be: see <see cref="Refusal"/>.</summary>
    public static bool IsUsable(ModifierKeys mods, Key key) => Refusal(mods, key) == null;

    /// <summary>
    /// Why a combination can't be the shortcut, as the Options page says it; null when it can. RegisterHotKey takes a key
    /// from every app on the PC, so (2026-10-25 audit) the shortcut may not be one Windows or typing needs: no plain
    /// Ctrl or Alt with a letter or number (Ctrl+C, Ctrl+V, Ctrl+Z, Ctrl+A, Alt+F), which also need Shift; no Win key
    /// combinations; no Alt+Tab, Alt+Esc, Alt+Space, Alt+F4 or Ctrl+Esc; none of the keys that move or edit text.
    /// </summary>
    public static string? Refusal(ModifierKeys mods, Key key)
    {
        if (key is Key.None or Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin or Key.System)
            return "Hold Ctrl, Alt or Shift and press a key.";
        if (mods == ModifierKeys.None) return "Use Ctrl, Alt or Shift together with a key, so typing in the game can't trigger it.";
        if (mods.HasFlag(ModifierKeys.Windows)) return "Windows keeps the Win key combinations for itself. Use Ctrl, Alt or Shift.";
        bool ctrlOrAlt = (mods & (ModifierKeys.Control | ModifierKeys.Alt)) != 0;
        // switching windows, the Start menu, the window menu, closing; and the keys that move, select or edit text
        if (key is Key.Tab or Key.Escape or Key.Space or Key.Enter or Key.Back or Key.Delete or Key.Insert or Key.Home or Key.End
                or Key.PageUp or Key.PageDown or Key.Left or Key.Right or Key.Up or Key.Down or Key.Snapshot or Key.Apps
                or Key.CapsLock or Key.NumLock or Key.Scroll or Key.Pause or Key.Cancel or Key.Clear)
            return $"{Name(key)} belongs to Windows or to typing. Pick another key, an F key for one.";
        if (key == Key.F4 && ctrlOrAlt) return "Alt+F4 and Ctrl+F4 close windows. Pick another key.";
        // a letter, a number or a typing mark: Shift with Ctrl or Alt, so copy, paste, undo and the game's own keys keep working
        bool typing = key is >= Key.A and <= Key.Z or >= Key.D0 and <= Key.D9 or >= Key.NumPad0 and <= Key.Divide or >= Key.OemSemicolon and <= Key.OemBackslash;
        if (typing && !(ctrlOrAlt && mods.HasFlag(ModifierKeys.Shift)))
            return $"With a letter or number, hold Shift and Ctrl or Alt (like Ctrl+Shift+{(key is >= Key.A and <= Key.Z ? key.ToString() : "K")}), so copy, paste and typing keep working.";
        return null;
    }

    static string Name(Key k) => k switch { Key.Snapshot => "Print Screen", Key.Back => "Backspace", Key.Escape => "Esc", Key.Apps => "The menu key", Key.CapsLock => "Caps Lock", Key.NumLock => "Num Lock", Key.Scroll => "Scroll Lock", Key.PageUp => "Page Up", Key.PageDown => "Page Down", _ => k.ToString() };

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
