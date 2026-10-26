using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using static KinahLedger.Ui.HudKit;

namespace KinahLedger.Ui;

/// <summary>
/// The Ticker look (option C, 2026-10-25), 270 px wide: a small pill with the run dot, "RUN 42:17", the hour and the
/// session, and under it up to four toasts, newest on top, the way the game shows loot. Each pickup slides in and fades
/// out over 6 s; a notice (the reader paused, an update waiting) is an orange toast at the top; a rare drop a taller toast
/// framed in its grade's colour while the window holds it. It sits left of the target bar by default (HudLooks.TickerLeftOfCentre).
/// </summary>
public sealed class HudTicker : IHudLook
{
    /// <summary>For layout renders and the Options preview: every toast at its place's strength, nothing animates or leaves.</summary>
    public bool Still { get; init; }

    public FrameworkElement Root { get; }
    public Thickness Inset { get; } = new(10);
    public double Corner => 14;

    /// <summary>How long a pickup stays (sliding in, holding, fading out), and when its fade starts.</summary>
    const double Life = 6, FadeFrom = 4;
    /// <summary>The toasts below the first, each a little fainter (the mock's ages 1 to 3).</summary>
    static readonly double[] PlaceOpacity = { 1, .92, .75, .5 };

    readonly Border _pill;
    readonly Ellipse _dot;
    readonly TextBlock _run, _time, _rate, _unit, _total;
    readonly FrameworkElement _sep2;
    readonly StackPanel _stack = new() { Width = 270, HorizontalAlignment = HorizontalAlignment.Left };
    readonly Dictionary<string, Toast> _toasts = new();
    readonly Dictionary<string, string> _drawn = new();
    string? _acc; bool _accCheck;
    double _alpha = 1;

    sealed class Toast
    {
        public required Border Place { get; init; }   // holds the place's opacity
        public required Border Body { get; init; }    // the toast itself: slides and fades
        public required string Kind { get; init; }    // "pick", "rare", "notice"
        public bool Gone;
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
        _sep2 = Rule(14, 9);
        _pill = new Border
        {
            Height = 28, Padding = new Thickness(10, 0, 13, 0), CornerRadius = new CornerRadius(14), BorderThickness = new Thickness(1), BorderBrush = FrameLine,
            HorizontalAlignment = HorizontalAlignment.Left,
            Child = new StackPanel { Orientation = Orientation.Horizontal, Children = { _dot, _run, _time, Rule(14, 9), _rate, _unit, _sep2, _total } },
        };
        Paint();
        Root = new StackPanel { Margin = Inset, Width = 270, Children = { _pill, _stack } };
    }

    void Paint() => _pill.Background = B(PanelC, .94 * _alpha);

    public void SetOpacity(double opacity)
    {
        _alpha = Math.Clamp(opacity, 0.3, 1) / .94;
        Paint();
        foreach (var t in _toasts.Values.Where(t => t.Kind != "rare")) t.Body.Background = Wash(t.Kind, t.Kind == "pick" && _stack.Children.IndexOf(t.Place) == 0);
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
        if (Changed("pill", $"{live}|{run}|{clock}|{V("rate")}|{warm}|{V("total")}|{paused.Length > 0}"))
        {
            SetDot(_dot, live);
            _run.Visibility = run ? Visibility.Visible : Visibility.Collapsed;
            _time.Text = clock; _time.Foreground = live || (!run && paused.Length == 0) ? Ink : Faint;
            _rate.Text = V("rate"); _rate.FontSize = warm ? 13 : 15; _rate.Opacity = warm ? .55 : 1;
            _unit.Visibility = V("rate").Length > 0 && !warm ? Visibility.Visible : Visibility.Collapsed;
            _total.Text = V("total");
            _sep2.Visibility = _total.Visibility = V("total").Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        // what the stack shows, top down: one notice, the accuracy line, the rare drop, then pickups under 6 s old; four at most
        var want = new List<(string Key, string Kind, Func<Border> Make)>();
        string note; Brush tone; bool sign = false;
        if (paused.Length > 0) { note = paused; tone = Warn; sign = true; }
        else if (V("flash").Length > 0) { note = V("flash"); tone = Ink; }
        else if (V("notice").Length > 0) { note = V("notice"); tone = Gold; }
        else if (V("flag").Length > 0) { note = V("flag") + " · fix in tracker"; tone = Gold; }
        else { note = ""; tone = Warn; }
        if (note.Length > 0) { var (n, t, s) = (note, tone, sign); want.Add(("notice|" + n, "notice", () => NoticeToast(n, t, s))); }
        if (_acc is { } acc) { var check = _accCheck; want.Add(("acc|" + acc + check, "notice", () => NoticeToast(acc, check ? Warn : B(0x62D98B), check))); }
        var rare = Rare(values);
        if (HudRare.Of(values) is { } drop) want.Add((drop.Pick.Id, "rare", () => HudRare.TickerToast(drop, _alpha)));   // 66 px since 2026-10-26
        bool skipped = false;
        var now = DateTime.UtcNow;
        foreach (var p in Picks(values))
        {
            if (rare != null && !skipped && string.Equals(p.Name, rare.Name, StringComparison.OrdinalIgnoreCase)) { skipped = true; continue; }
            double age = p.At == DateTime.MinValue ? 0 : (now - p.At).TotalSeconds;
            if (!Still && age >= Life) continue;
            if (_toasts.TryGetValue(p.Id, out var had) && had.Gone) continue;
            var pick = p;
            want.Add((p.Id, "pick", () => PickToast(pick, age)));
        }
        want = want.Take(4).ToList();
        if (!Changed("stack", string.Join("\n", want.Select(w => w.Key)))) return;

        // toasts no longer wanted leave; new ones are made once; the rest keep their animations and move to their place
        var keys = want.Select(w => w.Key).ToHashSet();
        foreach (var k in _toasts.Keys.Where(x => !keys.Contains(x)).ToList()) { _stack.Children.Remove(_toasts[k].Place); _toasts.Remove(k); }
        _stack.Children.Clear();
        for (int i = 0; i < want.Count; i++)
        {
            var (key, kind, make) = want[i];
            if (!_toasts.TryGetValue(key, out var t))
            {
                var body = make();
                t = new Toast { Place = new Border { Child = body }, Body = body, Kind = kind };
                _toasts[key] = t;
                if (!Still) Arrive(t, kind == "pick" ? Age(values, key) : -1);
            }
            t.Place.Margin = new Thickness(0, i == 0 ? 6 : 4, 0, 0);
            t.Place.Opacity = kind == "pick" ? PlaceOpacity[i] : 1;
            if (kind == "pick") t.Body.Background = Wash("pick", i == 0);
            _stack.Children.Add(t.Place);
        }
    }

    static double Age(IReadOnlyDictionary<string, string> values, string id)
    {
        var p = Picks(values).FirstOrDefault(x => x.Id == id);
        return p == null || p.At == DateTime.MinValue ? 0 : (DateTime.UtcNow - p.At).TotalSeconds;
    }

    /// <summary>
    /// A new toast slides in from the left (220 ms); a pickup's then holds and fades out by 6 s from when it was picked up,
    /// and goes. One DoubleAnimation each, started once: no timer touches the stack.
    /// </summary>
    void Arrive(Toast t, double age)
    {
        var body = t.Body;
        if (age < 0.5)
        {
            var slide = new TranslateTransform(-18, 0);
            body.RenderTransform = slide;
            slide.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(0, TimeSpan.FromMilliseconds(220)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
        }
        if (t.Kind != "pick")
        {
            if (age < 0.5) body.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(220)));
            return;
        }
        double from = age <= FadeFrom ? 1 : Math.Max(0, (Life - age) / (Life - FadeFrom));
        var fade = new DoubleAnimation(from, 0, TimeSpan.FromSeconds(Math.Max(0.1, Math.Min(Life - FadeFrom, Life - age))))
        {
            BeginTime = TimeSpan.FromSeconds(Math.Max(0, FadeFrom - age)),
        };
        fade.Completed += (_, _) => { t.Gone = true; t.Place.Visibility = Visibility.Collapsed; };
        if (age < 0.5)
        {
            var appear = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(220));
            appear.Completed += (_, _) => { if (!t.Gone) body.BeginAnimation(UIElement.OpacityProperty, fade); };
            body.BeginAnimation(UIElement.OpacityProperty, appear);
        }
        else body.BeginAnimation(UIElement.OpacityProperty, fade);
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

    /// <summary>A notice: the reader paused (orange, with the sign), a flash, an update waiting; or the accuracy line.</summary>
    Border NoticeToast(string text, Brush tone, bool sign)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        if (sign) { var i = WarnIcon(); i.Margin = new Thickness(0, 0, 8, 0); row.Children.Add(i); }
        var t = T(text, 13, tone, weight: FontWeights.SemiBold); t.MaxWidth = sign ? 230 : 250;
        row.Children.Add(t);
        return new Border
        {
            Width = 270, Height = 34, Padding = new Thickness(9, 0, 10, 0), CornerRadius = new CornerRadius(6), BorderThickness = new Thickness(2, 0, 0, 0),
            BorderBrush = sign ? Warn : tone, Background = Wash("notice", false), Child = row,
        };
    }
}
