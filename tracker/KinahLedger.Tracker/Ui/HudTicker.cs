using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using static KinahLedger.Ui.HudKit;

namespace KinahLedger.Ui;

/// <summary>
/// The Ticker look (option C, 2026-10-25), 270 px wide: a small pill with the run dot, "RUN 42:17", the hour and the
/// session, and under it four toasts: the three newest pickups, newest on top, and the session's top drop pinned under
/// them (Steven, 2026-10-26: "the 3 most recent and the Top Drop"). Pickups stay until a newer one pushes the oldest out,
/// which then fades; they no longer fade out after 6 s. A notice (the reader paused, an update waiting) is a toast at the
/// top and takes a pickup's place; a rare drop a taller toast framed in its grade's colour while the window holds it.
/// Until something is counted the pill says so instead of "0:00 | 0". It sits left of the target bar by default
/// (HudLooks.TickerLeftOfCentre).
/// </summary>
public sealed partial class HudTicker : IHudLook
{
    /// <summary>For layout renders and the Options preview: every toast at its place's strength, nothing animates or leaves.</summary>
    public bool Still { get; init; }

    public FrameworkElement Root { get; }
    public Thickness Inset { get; } = new(10);
    public double Corner => 14;

    /// <summary>How long a pickup pushed out of the stack takes to fade.</summary>
    static readonly TimeSpan LeaveTime = TimeSpan.FromMilliseconds(320);
    /// <summary>The toasts below the first, each a little fainter (the mock's ages 1 to 3).</summary>
    static readonly double[] PlaceOpacity = { 1, .92, .75, .5 };

    readonly Border _pill;
    readonly Ellipse _dot;
    readonly TextBlock _run, _time, _rate, _unit, _total, _idle;
    readonly FrameworkElement _sep1, _sep2;
    readonly StackPanel _stack = new() { Width = 270, HorizontalAlignment = HorizontalAlignment.Left };
    readonly Dictionary<string, Toast> _toasts = new();
    /// <summary>Toasts pushed out, fading under the stack until they go.</summary>
    readonly List<Toast> _leaving = new();
    string _topName = "";
    readonly Dictionary<string, string> _drawn = new();
    string? _acc; bool _accCheck;
    double _alpha = 1;

    sealed class Toast
    {
        public required Border Place { get; init; }   // holds the place's opacity
        public required Border Body { get; init; }    // the toast itself: slides and fades
        public required string Kind { get; init; }    // "pick", "top", "rare", "notice"
    }

    bool Changed(string part, string sig)
    {
        if (_drawn.TryGetValue(part, out var was) && was == sig) return false;
        _drawn[part] = sig;
        return true;
    }

    public HudTicker()
    {
        _dot = Dot(8); _dot.Margin = new Thickness(0, 0, 7, 0);
        _run = T("RUN", 11, Faint, weight: FontWeights.SemiBold); _run.Margin = new Thickness(0, 0, 7, 0);
        _time = T("", 13, Ink, mono: true);
        _rate = T("", 15, Gold, mono: true);
        _unit = T("/h", 12, Faint); _unit.Margin = new Thickness(7, 0, 0, 0);
        _total = T("", 13, Muted, mono: true);
        _sep1 = Rule(14, 9); _sep2 = Rule(14, 9);
        _idle = T("Waiting for the first count", 12, Faint); _idle.Visibility = Visibility.Collapsed;
        _pill = new Border
        {
            Height = 28, Padding = new Thickness(10, 0, 13, 0), CornerRadius = new CornerRadius(14), BorderThickness = new Thickness(1), BorderBrush = FrameLine,
            HorizontalAlignment = HorizontalAlignment.Left,
            Child = new StackPanel { Orientation = Orientation.Horizontal, Children = { _dot, _run, _time, _sep1, _rate, _unit, _sep2, _total, _idle } },
        };
        BuildCraft();   // the crafting pill (Ui/HudCraftLooks.cs)
        Paint();
        Root = new StackPanel { Margin = Inset, Width = 270, Children = { _pill, _stack } };
    }

    void Paint() => _pill.Background = B(PanelC, .94 * _alpha);

    public void SetOpacity(double opacity)
    {
        _alpha = Math.Clamp(opacity, 0.3, 1) / .94;
        Paint();
        foreach (var t in _toasts.Values.Where(t => t.Kind != "rare")) t.Body.Background = t.Kind == "top" ? TopWash() : Wash(t.Kind, t.Kind == "pick" && _stack.Children.IndexOf(t.Place) == 0);
    }

    public void SetAccuracy(string? text, bool toCheck)
    {
        if (_acc == text && _accCheck == toCheck) return;
        _acc = string.IsNullOrEmpty(text) ? null : text; _accCheck = toCheck;
        _drawn.Remove("stack");   // the next Update lays the stack out again
    }

    public void Update(IReadOnlyDictionary<string, string> values)
    {
        string V(string k) => values.TryGetValue(k, out var v) ? v : "";
        var (run, clock) = Time(V("time"));
        var paused = V("paused");
        bool live = V("run") == "1" && paused.Length == 0 && V("show") != "dim";
        bool warm = V("warm") == "1";
        // nothing counted yet (no run, no pickup, a total of 0): "Waiting for the first count" rather than "0:00 | 0" (2026-10-26)
        bool counted = run || Picks(values).Count > 0 || V("total") is not ("" or "0");
        if (Changed("pill", $"{live}|{run}|{clock}|{V("rate")}|{warm}|{V("total")}|{paused.Length > 0}|{counted}"))
        {
            SetDot(_dot, live);
            _run.Visibility = run ? Visibility.Visible : Visibility.Collapsed;
            _time.Text = clock; _time.Foreground = live || (!run && paused.Length == 0) ? Ink : Faint;
            _rate.Text = V("rate"); _rate.FontSize = warm ? 13 : 15; _rate.Opacity = warm ? .55 : 1;
            _unit.Visibility = V("rate").Length > 0 && !warm ? Visibility.Visible : Visibility.Collapsed;
            _total.Text = V("total");
            _sep2.Visibility = _total.Visibility = V("total").Length > 0 ? Visibility.Visible : Visibility.Collapsed;
            _rate.Visibility = Visibility.Visible;
            _idle.Visibility = counted ? Visibility.Collapsed : Visibility.Visible;
            _time.Visibility = _sep1.Visibility = counted ? Visibility.Visible : Visibility.Collapsed;
            if (!counted) _rate.Visibility = _unit.Visibility = _sep2.Visibility = _total.Visibility = Visibility.Collapsed;
        }
        bool crafting = CraftPill(values);   // while the crafting window is open: the crafts instead of the pickups

        // what the stack shows, top down: one notice, the accuracy line, the rare drop, then the newest pickups (crafts while
        // crafting), and the session's top drop pinned last; four at most, so a notice takes the oldest pickup's place
        var want = new List<(string Key, string Kind, Func<Border> Make)>();
        string note, clockV = ""; Brush tone; bool sign = false;
        if (paused.Length > 0) { note = paused; tone = Warn; sign = true; }
        else if (V("flash").Length > 0) { note = V("flash"); tone = Ink; }
        else if (V("notice").Length > 0) { note = V("notice"); tone = Gold; clockV = V("noticeClock"); }
        else if (V("flag").Length > 0) { note = V("flag") + " · fix in tracker"; tone = Gold; }
        else { note = ""; tone = Warn; }
        if (note.Length > 0) { var (n, t, s, cv) = (note, tone, sign, clockV); want.Add(("notice|" + n + cv, "notice", () => NoticeToast(n, t, s, HudClockNotice.Parse(cv)))); }
        if (_acc is { } acc) { var check = _accCheck; want.Add(("acc|" + acc + check, "notice", () => NoticeToast(acc, check ? Warn : B(0x62D98B), check))); }
        var rare = Rare(values);
        if (HudRare.Of(values) is { } drop) want.Add((drop.Pick.Id, "rare", () => HudRare.TickerToast(drop, _alpha)));   // 66 px since 2026-10-26
        var top = crafting ? null : Top(values);
        if (top != null && rare != null && string.Equals(top.Name, rare.Name, StringComparison.OrdinalIgnoreCase)) top = null;   // the drop itself is on show above
        int room = 4 - want.Count - (top != null ? 1 : 0);
        bool skipped = false, skippedTop = false;
        var now = DateTime.UtcNow;
        foreach (var p in crafting ? CraftKit.Crafts(values) : Picks(values))
        {
            if (room <= 0) break;
            if (rare != null && !skipped && string.Equals(p.Name, rare.Name, StringComparison.OrdinalIgnoreCase)) { skipped = true; continue; }
            // the top drop picked up once is the pinned toast, not twice in the stack
            if (top != null && !skippedTop && top.Count == "×1" && string.Equals(p.Name, top.Name, StringComparison.OrdinalIgnoreCase)) { skippedTop = true; continue; }
            double age = p.At == DateTime.MinValue ? 0 : (now - p.At).TotalSeconds;
            var pick = p;
            want.Add((p.Id, "pick", () => crafting ? CraftToast(pick) : PickToast(pick, age)));
            room--;
        }
        bool newTop = false;
        if (top != null)
        {
            var t0 = top;
            newTop = !string.Equals(_topName, top.Name, StringComparison.OrdinalIgnoreCase);
            want.Add((top.Id, "top", () => TopToast(t0)));
        }
        _topName = top?.Name ?? "";
        // two feed lines read in the same instant with the same name and amount share a key (2026-10-26 10:05, "Specified
        // Visual is already a child of another Visual"): one toast per key, the first one
        want = want.GroupBy(w => w.Key).Select(g => g.First()).Take(4).ToList();
        if (!Changed("stack", string.Join("\n", want.Select(w => w.Key)))) return;

        // toasts no longer wanted leave (a pickup pushed out fades under the stack); new ones are made once; the rest keep
        // their animations and move to their place
        var keys = want.Select(w => w.Key).ToHashSet();
        foreach (var k in _toasts.Keys.Where(x => !keys.Contains(x)).ToList())
        {
            var gone = _toasts[k];
            _toasts.Remove(k);
            if (!Still && gone.Kind == "pick") Leave(gone);
        }
        _stack.Children.Clear();
        for (int i = 0; i < want.Count; i++)
        {
            var (key, kind, make) = want[i];
            if (!_toasts.TryGetValue(key, out var t))
            {
                var body = make();
                t = new Toast { Place = new Border { Child = body }, Body = body, Kind = kind };
                _toasts[key] = t;
                // a top drop slides in when it is a new item, not when its count or worth moves on
                if (!Still) Arrive(t, kind == "pick" ? Age(values, key) : kind == "top" && !newTop ? 99 : -1);
            }
            t.Place.Margin = new Thickness(0, i == 0 ? 6 : 4, 0, 0);
            t.Place.Opacity = kind == "pick" ? PlaceOpacity[i] : 1;
            if (kind == "pick") t.Body.Background = Wash("pick", i == 0);
            if (t.Place.Parent is Panel owner) owner.Children.Remove(t.Place);
            _stack.Children.Add(t.Place);
        }
        foreach (var l in _leaving) if (l.Place.Parent == null) _stack.Children.Add(l.Place);
    }

    /// <summary>A pickup pushed out by a newer one: it fades where it is, under the stack, and goes.</summary>
    void Leave(Toast t)
    {
        _leaving.Add(t);
        var fade = new DoubleAnimation(0, LeaveTime);
        fade.Completed += (_, _) => { _leaving.Remove(t); _stack.Children.Remove(t.Place); };
        t.Body.BeginAnimation(UIElement.OpacityProperty, fade);
    }

    static double Age(IReadOnlyDictionary<string, string> values, string id)
    {
        var p = Picks(values).Concat(CraftKit.Crafts(values)).FirstOrDefault(x => x.Id == id);
        return p == null || p.At == DateTime.MinValue ? 0 : (DateTime.UtcNow - p.At).TotalSeconds;
    }

    /// <summary>
    /// A new toast (under half a second old) slides in from the left and fades in (220 ms), then stays until a newer one
    /// pushes it out (<see cref="Leave"/>). One DoubleAnimation each, started once: no timer touches the stack.
    /// </summary>
    static void Arrive(Toast t, double age)
    {
        if (age >= 0.5) return;
        var body = t.Body;
        var slide = new TranslateTransform(-18, 0);
        body.RenderTransform = slide;
        slide.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(0, TimeSpan.FromMilliseconds(220)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
        body.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(220)));
    }

    /// <summary>A toast's wash: dark navy fading lighter to the right; the newest pickup's warm at its start, a notice's amber.</summary>
    Brush Wash(string kind, bool newest)
    {
        var (a, b, c) = kind == "notice" ? ((0x3A220E, .95), (0x1E1410, .92), (0x141014, .78))
            : newest ? ((0x28241E, .95), (0x12162D, .92), (0x0E1124, .75))
            : ((0x0A0C18, .94), (0x0E1124, .90), (0x0E1124, .72));
        var g = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 0) };
        g.GradientStops.Add(new GradientStop(Rgb((uint)a.Item1, a.Item2 * _alpha), 0));
        g.GradientStops.Add(new GradientStop(Rgb((uint)b.Item1, b.Item2 * _alpha), newest && kind == "pick" ? .6 : .65));
        g.GradientStops.Add(new GradientStop(Rgb((uint)c.Item1, c.Item2 * _alpha), 1));
        g.Freeze();
        return g;
    }

    /// <summary>One pickup: its tile with the count badge, its name (and "gathered"), and "+7.5K" in gold on the right.</summary>
    Border PickToast(Pick p, double age)
    {
        var g = new Grid();
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        g.ColumnDefinitions.Add(new ColumnDefinition());
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var tile = Tile(p, 24);
        g.Children.Add(tile);
        var name = new TextBlock { VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(9, 0, 0, 0) };
        name.Inlines.Add(R(p.Name, 13, Ink));
        if (p.Note.Length > 0) name.Inlines.Add(R(" " + p.Note, 12, Faint));
        Grid.SetColumn(name, 1); g.Children.Add(name);
        var worth = T(p.Worth, 13, Gold, mono: true); worth.Margin = new Thickness(9, 0, 0, 0);
        Grid.SetColumn(worth, 2); g.Children.Add(worth);
        return new Border
        {
            Width = 270, Height = 34, Padding = new Thickness(6, 0, 10, 0), CornerRadius = new CornerRadius(6), BorderThickness = new Thickness(2, 0, 0, 0),
            BorderBrush = p.Kinah ? B(GoldC, .7) : B(CommonC, .55), Background = Wash("pick", false), Child = g,
        };
    }

    /// <summary>The top drop's wash: a faint gold start, for a toast that is kept rather than passing.</summary>
    Brush TopWash()
    {
        var g = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 0) };
        g.GradientStops.Add(new GradientStop(Rgb(0x2A2414, .95 * _alpha), 0));
        g.GradientStops.Add(new GradientStop(Rgb(0x12162D, .92 * _alpha), .55));
        g.GradientStops.Add(new GradientStop(Rgb(0x0E1124, .78 * _alpha), 1));
        g.Freeze();
        return g;
    }

    /// <summary>
    /// The session's top drop, pinned under the newest pickups: "TOP DROP" in small gold letters over the name (in its
    /// grade's colour above Common), the tile with the session's count, and their worth in gold, on a gold edge.
    /// </summary>
    Border TopToast(Pick p)
    {
        var g = new Grid();
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        g.ColumnDefinitions.Add(new ColumnDefinition());
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        g.Children.Add(Tile(p, 24));
        var label = T("TOP DROP", 8.5, Gold, weight: FontWeights.SemiBold);
        var name = new TextBlock { TextTrimming = TextTrimming.CharacterEllipsis };
        name.Inlines.Add(R(p.Name, 12.5, p.Grade is "" or "Common" ? Ink : Frozen(GradeColor(p.Grade))));
        var words = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(9, 0, 0, 0), Children = { label, name } };
        Grid.SetColumn(words, 1); g.Children.Add(words);
        var worth = T(p.Worth, 13, Gold, mono: true); worth.Margin = new Thickness(9, 0, 0, 0);
        Grid.SetColumn(worth, 2); g.Children.Add(worth);
        return new Border
        {
            Width = 270, Height = 34, Padding = new Thickness(6, 0, 10, 0), CornerRadius = new CornerRadius(6), BorderThickness = new Thickness(2, 0, 0, 0),
            BorderBrush = B(GoldC, .85), Background = TopWash(), Child = g,
        };
    }

    /// <summary>A notice: the reader paused (orange, with the sign), a flash, an update waiting; or the accuracy line.</summary>
    Border NoticeToast(string text, Brush tone, bool sign, (double Left, double Seconds)? clock = null)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        if (sign) { var i = WarnIcon(); i.Margin = new Thickness(0, 0, 8, 0); row.Children.Add(i); }
        var t = T(text, 13, tone, weight: FontWeights.SemiBold); t.MaxWidth = sign ? 230 : 250;
        if (clock is { } c)
        {
            // the That was wrong key's answer: a gold clock, the words in two tones, a gold wash and the line running down its 3 s
            // 11.75 px so the whole line fits the toast's 270 px (at 12.5 "Report in tracker" lost its end)
            var i = HudClockNotice.ClockIcon(); i.Margin = new Thickness(0, 0, 6, 0); row.Children.Add(i);
            HudClockNotice.Words(t, text, 11.75); t.MaxWidth = 235;
            row.Children.Add(t);
            var drain = HudClockNotice.Drain(); drain.Margin = new Thickness(-8, 0, -4, 0);
            HudClockNotice.Run(drain, c, Still);
            var wash = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 0) };
            wash.GradientStops.Add(new GradientStop(Rgb(0x302816, .96 * _alpha), 0));
            wash.GradientStops.Add(new GradientStop(Rgb(0x18161E, .92 * _alpha), .65));
            wash.GradientStops.Add(new GradientStop(Rgb(0x12121E, .80 * _alpha), 1));
            wash.Freeze();
            return new Border
            {
                Width = 270, Height = 34, Padding = new Thickness(8, 0, 4, 0), CornerRadius = new CornerRadius(6), BorderThickness = new Thickness(2, 0, 0, 0),
                BorderBrush = Gold, Background = wash, Child = new Grid { Children = { row, drain } },
            };
        }
        row.Children.Add(t);
        return new Border
        {
            Width = 270, Height = 34, Padding = new Thickness(9, 0, 10, 0), CornerRadius = new CornerRadius(6), BorderThickness = new Thickness(2, 0, 0, 0),
            BorderBrush = sign ? Warn : tone, Background = Wash("notice", false), Child = row,
        };
    }
}
