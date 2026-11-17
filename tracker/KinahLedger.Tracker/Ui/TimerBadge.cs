using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using KinahLedger.Session;

namespace KinahLedger.Ui;

/// <summary>
/// A timer as a badge on the in-game overlays (Steven, 2026-11-16: the timers were "just black boxes w/ yellow text, make
/// them like badges"): a small rounded plate in the timer's tone, its name and its clock. Green while it's on (an event's
/// entry open: "ENTER 2:41"), gold in its last minutes (Settings › Timers), else a quiet grey. Each plate is solid, so it
/// reads over the game without a box behind the line.
/// </summary>
static class TimerBadge
{
    public enum Tone { Quiet, Soon, On }

    static SolidColorBrush F(byte a, byte r, byte g, byte b) { var x = new SolidColorBrush(Color.FromArgb(a, r, g, b)); x.Freeze(); return x; }

    static readonly SolidColorBrush GoldInk = F(0xFF, 0xF5, 0xB9, 0x42), GoldFill = F(0xEE, 0x2B, 0x22, 0x10), GoldLine = F(0xB0, 0xF5, 0xB9, 0x42);
    static readonly SolidColorBrush MintInk = F(0xFF, 0x5E, 0xE3, 0x9E), MintFill = F(0xEE, 0x0E, 0x2A, 0x1C), MintLine = F(0xB0, 0x3D, 0xD6, 0x8C);
    static readonly SolidColorBrush QuietInk = F(0xFF, 0xD6, 0xDA, 0xE4), QuietFill = F(0xEE, 0x14, 0x16, 0x1C), QuietLine = F(0x40, 0xFF, 0xFF, 0xFF);
    static readonly SolidColorBrush NameInk = F(0xFF, 0xE4, 0xE7, 0xEE);

    /// <summary>The time to show and the badge's tone for a timer at <paramref name="now"/> (UTC).</summary>
    public static (TimeSpan Left, Tone Tone) Of(TimerBoard.Item i, DateTime now)
    {
        var left = i.AtPc is { } at ? at - now : TimeSpan.Zero;
        if (left < TimeSpan.Zero) left = TimeSpan.Zero;
        var tone = i.State == TimerBoard.Kind.On ? Tone.On : left <= TimeSpan.FromMinutes(TimerPrefs.GoldMinutes) ? Tone.Soon : Tone.Quiet;
        return (left, tone);
    }

    /// <summary>The words a badge says: "Shugo 4:52", "Shugo ENTER 2:41", "Kashapa UP".</summary>
    public static string Text(TimerBoard.Item i, DateTime now)
    {
        var (left, tone) = Of(i, now);
        return $"{i.Short} {Clock(i, left, tone)}";
    }

    static string Clock(TimerBoard.Item i, TimeSpan left, Tone tone) =>
        tone == Tone.On ? (i.AtPc != null ? L.F("ENTER {0}", TimerBoard.Clock(left)) : TimerSay.Text(i.Big).ToUpperInvariant()) : TimerBoard.Clock(left);

    /// <summary>One timer's badge, its type <paramref name="size"/> px.</summary>
    public static Border Make(TimerBoard.Item i, DateTime now, double size = 11.5)
    {
        var (left, tone) = Of(i, now);
        var (ink, fill, line) = tone switch { Tone.On => (MintInk, MintFill, MintLine), Tone.Soon => (GoldInk, GoldFill, GoldLine), _ => (QuietInk, QuietFill, QuietLine) };
        var name = new TextBlock { Text = i.Short, FontSize = size, FontWeight = FontWeights.SemiBold, Foreground = NameInk, VerticalAlignment = VerticalAlignment.Center };
        var clock = new TextBlock { Text = Clock(i, left, tone), FontSize = size, FontWeight = FontWeights.Bold, Foreground = ink, Margin = new Thickness(6, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        System.Windows.Documents.Typography.SetNumeralAlignment(clock, FontNumeralAlignment.Tabular);
        return new Border
        {
            Tag = "timer-badge", CornerRadius = new CornerRadius(5), Padding = new Thickness(7, 2, 7, 3), Background = fill, BorderBrush = line, BorderThickness = new Thickness(1),
            VerticalAlignment = VerticalAlignment.Center, Child = new StackPanel { Orientation = Orientation.Horizontal, Children = { name, clock } },
        };
    }

    /// <summary>The badges in a row, a small gap between (at most <paramref name="max"/>).</summary>
    public static void Fill(Panel row, IEnumerable<TimerBoard.Item> items, DateTime now, double size = 11.5, int max = int.MaxValue)
    {
        row.Children.Clear();
        foreach (var i in items.Take(max))
        {
            var b = Make(i, now, size);
            if (row.Children.Count > 0) b.Margin = new Thickness(5, 0, 0, 0);
            row.Children.Add(b);
        }
    }
}

/// <summary>
/// The timers' words in the player's language. The schedules and the countdowns (Session/TimerBoard, BossTimers, EventTimers)
/// write English: "Every hour", "Thu and Sat", "Up now", "12 min ago", "19m 59s", "at 07:30". Each is turned here, where a
/// timer is drawn (the overlay's badges, the page, the pop-out window): by the text itself, else by its pattern. Boss, event and
/// place names are the game's and stay as they are.
/// </summary>
static class TimerSay
{
    static readonly System.Text.RegularExpressions.Regex AtRe = new(@"^at (\d{1,2}:\d{2})$"), SpanRe = new(@"^(\d+) (s|min|h|days)(?: (\d+) min)?$"),
        ClockRe = new(@"^(?:\d+[dhms])(?: \d+[dhms])*$"), PrefixRe = new(@"^(began|spawned|spawn|up|seen) (.+)$"), AgoRe = new(@"^(.+) ago$"),
        ReadRe = new(@"^Read (.+) ago: open the world map to refresh$");

    /// <summary>A text of the timers: the whole text if the table has it, else its pattern, else as it is (a name, a time).</summary>
    public static string Text(string s)
    {
        if (s.Length == 0 || L.Code == "en") return s;
        if (L.CurrentTable().ContainsKey(s)) return L.T(s);
        var m = AtRe.Match(s);
        if (m.Success) return L.F("at {0}", m.Groups[1].Value);
        m = ReadRe.Match(s);
        if (m.Success) return L.F("Read {0} ago: open the world map to refresh", Span(m.Groups[1].Value));
        m = PrefixRe.Match(s);
        if (m.Success) return L.F(m.Groups[1].Value + " {0}", Text(m.Groups[2].Value));
        m = AgoRe.Match(s);
        if (m.Success) return L.F("{0} ago", Span(m.Groups[1].Value));
        if (SpanRe.IsMatch(s)) return Span(s);
        if (ClockRe.IsMatch(s)) return string.Join(" ", s.Split(' ').Select(t => L.F("{0}" + t[^1], t[..^1])));
        return s;
    }

    /// <summary>A length as BossTimers.Span writes it ("40 s", "12 min", "2 h 5 min", "3 days"), in the language in use.</summary>
    public static string Span(string s)
    {
        var m = SpanRe.Match(s);
        if (!m.Success || L.Code == "en") return s;
        string first = L.F("{0} " + m.Groups[2].Value, m.Groups[1].Value);
        return m.Groups[3].Success ? first + " " + L.F("{0} min", m.Groups[3].Value) : first;
    }
}
