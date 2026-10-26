using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using static KinahLedger.Ui.HudKit;

namespace KinahLedger.Ui;

/// <summary>
/// The Card look (option B, 2026-10-25): a game tooltip, 360 px wide. The run dot, "RUN 42:17", the zone and the newest
/// pickup with its worth; a thin gold rule; the hour big in gold with the session under it, and the last three pickups as
/// tiles, the newest lit for a few seconds. A notice (the reader paused, an update waiting) adds a slim row; a rare drop
/// lights the card in its grade's colour while the window holds it (the "rare" values, about 5 s).
/// </summary>
public sealed class HudCard : IHudLook
{
    /// <summary>For layout renders and the Options preview: everything at full, nothing animates.</summary>
    public bool Still { get; init; }

    public FrameworkElement Root { get; }
    public Thickness Inset { get; } = new(10);
    public double Corner => 10;

    readonly Border _card, _ring, _notice, _acc;
    readonly Ellipse _dot;
    readonly TextBlock _run, _time, _zone, _newest, _rate, _total, _noticeText, _accText;
    readonly Image _noticeIcon, _noticeClock;
    /// <summary>The That was wrong notice's gold line, running down its 3 s (HudClockNotice).</summary>
    readonly Border _drain;
    readonly StackPanel _tiles = new() { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 2, 0) };
    readonly Dictionary<string, string> _drawn = new();
    double _alpha = 1;
    string _newestId = "";

    /// <summary>The part's words changed since it was last drawn (MainWindow.Changed's pattern): a tick that changes nothing redraws nothing.</summary>
    bool Changed(string part, string sig)
    {
        if (_drawn.TryGetValue(part, out var was) && was == sig) return false;
        _drawn[part] = sig;
        return true;
    }

    public HudCard()
    {
        _dot = Dot(7);
        _run = T("RUN", 11, Faint, weight: FontWeights.SemiBold); _run.Margin = new Thickness(6, 0, 0, 0);
        _time = T("", 13, Ink, mono: true); _time.Margin = new Thickness(6, 0, 0, 0);
        _zone = T("", 12, Faint); _zone.Margin = new Thickness(10, 0, 0, 0);
        _newest = T("", 13, Ink); _newest.Margin = new Thickness(6, 0, 0, 0); _newest.TextTrimming = TextTrimming.None;
        var head = new Grid { Height = 18 };
        foreach (var w in new[] { GridLength.Auto, GridLength.Auto, GridLength.Auto, new GridLength(1, GridUnitType.Star), GridLength.Auto }) head.ColumnDefinitions.Add(new ColumnDefinition { Width = w });
        int col = 0;
        foreach (var e in new FrameworkElement[] { _dot, _run, _time, _zone, _newest }) { Grid.SetColumn(e, col++); head.Children.Add(e); }

        // the gold rule under the head, fading out to the right
        var rule = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 0) };
        rule.GradientStops.Add(new GradientStop(Rgb(GoldC, .65), 0));
        rule.GradientStops.Add(new GradientStop(Rgb(SecGoldC, .25), .6));
        rule.GradientStops.Add(new GradientStop(Rgb(SecGoldC, 0), 1));
        rule.Freeze();

        // the figures: the hour big in gold, the session under it
        _rate = new TextBlock { LineHeight = 26, LineStackingStrategy = LineStackingStrategy.BlockLineHeight, TextTrimming = TextTrimming.CharacterEllipsis };
        _total = new TextBlock { LineHeight = 18, LineStackingStrategy = LineStackingStrategy.BlockLineHeight, TextTrimming = TextTrimming.CharacterEllipsis };
        var figs = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Children = { _rate, _total } };
        var body = new Grid();
        body.ColumnDefinitions.Add(new ColumnDefinition());
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        body.Children.Add(figs);
        Grid.SetColumn(_tiles, 1); body.Children.Add(_tiles);

        _noticeIcon = WarnIcon(); _noticeIcon.Margin = new Thickness(0, 0, 7, 0);
        _noticeText = T("", 13, Warn, weight: FontWeights.SemiBold);
        _noticeClock = HudClockNotice.ClockIcon(); _noticeClock.Margin = new Thickness(0, 0, 7, 0); _noticeClock.Visibility = Visibility.Collapsed;
        _drain = HudClockNotice.Drain(); _drain.Margin = new Thickness(0, 0, 0, -5); _drain.Visibility = Visibility.Collapsed;
        _notice = new Border
        {
            Margin = new Thickness(0, 8, 0, 0), Padding = new Thickness(0, 6, 0, 0), BorderThickness = new Thickness(0, 1, 0, 0), BorderBrush = B(WarnC, .28), Visibility = Visibility.Collapsed,
            Child = new Grid { Children = { new StackPanel { Orientation = Orientation.Horizontal, Height = 17, Children = { _noticeIcon, _noticeClock, _noticeText } }, _drain } },
        };
        _accText = T("", 12.5, Warn, weight: FontWeights.SemiBold);
        _acc = new Border { Margin = new Thickness(0, 6, 0, 0), Visibility = Visibility.Collapsed, Child = _accText };

        _card = new Border
        {
            Width = 360, CornerRadius = new CornerRadius(10), BorderThickness = new Thickness(1), BorderBrush = FrameLine, Padding = new Thickness(12, 8, 12, 9),
            Child = new StackPanel { Children = { head, new Border { Height = 1, Margin = new Thickness(0, 6, 0, 7), Background = rule }, body, _notice, _acc } },
        };
        Paint();
        _ring = new Border { Visibility = Visibility.Collapsed, IsHitTestVisible = false };
        Root = new Grid { Margin = Inset, Children = { _ring, _card } };
    }

    /// <summary>The card's background (the mock's tooltip wash: warm in the top-right corner to the frame's navy), at the chosen opacity.</summary>
    void Paint()
    {
        var b = new RadialGradientBrush { Center = new Point(1, 0), GradientOrigin = new Point(1, 0), RadiusX = 1.2, RadiusY = 1.4 };
        b.GradientStops.Add(new GradientStop(Rgb(0x3A3323, .92 * _alpha), 0));
        b.GradientStops.Add(new GradientStop(Rgb(0x171C38, .94 * _alpha), .55));
        b.GradientStops.Add(new GradientStop(Rgb(PanelC, .95 * _alpha), 1));
        b.Freeze();
        _card.Background = b;
    }

    public void SetOpacity(double opacity)
    {
        // the mock's 94% is the slider's 94%: the strip's frame and the card's wash move together
        _alpha = Math.Clamp(opacity, 0.3, 1) / .94;
        Paint();
    }

    public void SetAccuracy(string? text, bool toCheck)
    {
        _accText.Text = text ?? "";
        _accText.Foreground = toCheck ? Warn : B(0x62D98B);
        _acc.Visibility = string.IsNullOrEmpty(text) ? Visibility.Collapsed : Visibility.Visible;
    }

    public void Update(IReadOnlyDictionary<string, string> values)
    {
        string V(string k) => values.TryGetValue(k, out var v) ? v : "";
        var (run, clock) = Time(V("time"));
        var paused = V("paused");
        bool live = V("run") == "1" && paused.Length == 0 && V("show") != "dim";
        if (Changed("head", $"{live}|{run}|{clock}|{V("zone")}|{paused.Length > 0}"))
        {
            SetDot(_dot, live);
            _run.Visibility = run ? Visibility.Visible : Visibility.Collapsed;
            _time.Text = clock; _time.Foreground = live || (!run && paused.Length == 0) ? Ink : Faint;
            _time.Margin = new Thickness(run ? 6 : 8, 0, 0, 0);
            _zone.Text = V("zone");
        }

        // the hour and the session: "1.27M an hour", "894K session, after the Market's cut"; warming up says so, dimmed
        bool warm = V("warm") == "1";
        if (Changed("figs", $"{V("rate")}|{warm}|{V("total")}"))
        {
            _rate.Inlines.Clear();
            if (V("rate").Length == 0) _rate.Inlines.Add(R("Waiting for the first count", 13, Faint));
            else if (warm) _rate.Inlines.Add(R(V("rate"), 16, Gold, mono: true));
            else { _rate.Inlines.Add(R(V("rate"), 20, Gold, mono: true)); _rate.Inlines.Add(R(" an hour", 12, Faint)); }
            _rate.Opacity = warm ? .55 : 1;
            _total.Inlines.Clear();
            if (V("total").Length > 0) { _total.Inlines.Add(R(V("total"), 13, Muted, mono: true)); _total.Inlines.Add(R(" session, after the Market's cut", 12, Faint)); }
        }

        // the notice row: the reader paused (orange, with the sign), else a start or stop's flash, an update waiting, a flagged run
        string note, clockV = ""; Brush tone; bool sign = false;
        if (paused.Length > 0) { note = paused; tone = Warn; sign = true; }
        else if (V("flash").Length > 0) { note = V("flash"); tone = Ink; }
        else if (V("notice").Length > 0) { note = V("notice"); tone = Gold; clockV = V("noticeClock"); }
        else if (V("flag").Length > 0) { note = V("flag") + " · fix in tracker"; tone = Gold; }
        else { note = ""; tone = Warn; }
        if (Changed("notice", $"{note}|{sign}|{clockV}"))
        {
            _noticeText.Text = note; _noticeText.Foreground = tone;
            _noticeIcon.Visibility = sign ? Visibility.Visible : Visibility.Collapsed;
            _notice.BorderBrush = sign ? B(WarnC, .28) : B(LineC);
            _notice.Visibility = note.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
            // the That was wrong key's answer: a gold clock, the words in two tones, and the gold line running down its 3 s
            var ck = HudClockNotice.Parse(clockV);
            _noticeClock.Visibility = _drain.Visibility = ck != null ? Visibility.Visible : Visibility.Collapsed;
            if (ck is { } left) { HudClockNotice.Words(_noticeText, note, 13); _notice.BorderBrush = B(GoldC, .26); HudClockNotice.Run(_drain, left, Still); }
        }

        // the newest pickup in the head and the last three as tiles; a rare drop goes first, in its colour
        var picks = Picks(values);
        var rare = Rare(values);
        var tiles = new List<Pick>();
        if (rare != null) tiles.Add(rare);
        bool skipped = false;   // the drop itself is among the pickups too: shown once, as the rare tile
        foreach (var p in picks)
        {
            if (rare != null && !skipped && string.Equals(p.Name, rare.Name, StringComparison.OrdinalIgnoreCase)) { skipped = true; continue; }
            tiles.Add(p);
        }
        tiles = tiles.Take(3).ToList();
        if (Changed("tiles", string.Join("|", tiles.Select(t => $"{t.Id}:{t.Count}:{t.Icon}:{t.Grade}"))))
        {
            _tiles.Children.Clear();
            for (int i = 0; i < tiles.Count; i++)
            {
                var t = Tile(tiles[i], 28);
                t.Margin = new Thickness(i == 0 ? 0 : 6, 0, 0, 0);
                t.Opacity = i switch { 0 => 1, 1 => .9, _ => .75 };
                if (i == 0 && t is Grid g) g.Children.Add(Light(tiles[0]));
                _tiles.Children.Add(t);
            }
        }
        var first = tiles.FirstOrDefault();
        if (Changed("newest", first == null ? "" : $"{first.Id}|{first.Name}|{first.Worth}|{first.Count}"))
        {
            _newest.Inlines.Clear();
            if (first?.Rare == true)
            {
                var c = Frozen(ColorOf(first.Color, first.Grade));
                _newest.Inlines.Add(R(first.Name, 13, c, weight: FontWeights.SemiBold));
                if (first.Count.Length > 0) _newest.Inlines.Add(R(" " + first.Count, 12, Muted, mono: true));
            }
            else if (first != null)
            {
                _newest.Inlines.Add(R(first.Name, 13, Ink));
                if (first.Worth.Length > 0) { _newest.Inlines.Add(R("  ", 13, Ink)); _newest.Inlines.Add(R(first.Worth, 13, Gold, mono: true)); }
            }
            // a long name gives way to the zone, never the other way: the zone trims first
            _newest.MaxWidth = 200;
        }

        // the rare drop: the card's border in the grade's colour, 2 px, with a glow
        if (Changed("rare", rare == null ? "" : rare.Color + rare.Grade))
        {
            if (rare != null)
            {
                var c = ColorOf(rare.Color, rare.Grade);
                _card.BorderBrush = Frozen(c); _card.BorderThickness = new Thickness(2); _card.Padding = new Thickness(11, 7, 11, 8);
                var glow = Glow(c, 10, 18, .55, 2);
                _ring.BorderBrush = glow.BorderBrush; _ring.BorderThickness = glow.BorderThickness; _ring.CornerRadius = glow.CornerRadius; _ring.Effect = glow.Effect;
                _ring.Visibility = Visibility.Visible;
            }
            else
            {
                _card.BorderBrush = FrameLine; _card.BorderThickness = new Thickness(1); _card.Padding = new Thickness(12, 8, 12, 9);
                _ring.Visibility = Visibility.Collapsed;
            }
        }
    }

    /// <summary>
    /// The newest tile's light: a gold frame with a faint ring and a soft glow that fades over 5 s from the pickup (a
    /// DoubleAnimation, started once per new pickup); a rare drop's in its colour, held while the window holds the drop.
    /// </summary>
    FrameworkElement Light(Pick p)
    {
        var c = p.Rare ? ColorOf(p.Color, p.Grade) : Rgb(GoldC);
        var light = new Grid { IsHitTestVisible = false };
        light.Children.Add(new Border { Margin = new Thickness(-1), CornerRadius = new CornerRadius(6), BorderThickness = new Thickness(1), BorderBrush = Frozen(Color.FromArgb(p.Rare ? (byte)0x4D : (byte)0x40, c.R, c.G, c.B)) });
        var glow = Glow(c, 5, p.Rare ? 12 : 10, p.Rare ? .6 : .35);
        glow.BorderBrush = Frozen(Color.FromArgb(p.Rare ? (byte)0xFF : (byte)0xCC, c.R, c.G, c.B));
        light.Children.Add(glow);
        bool fresh = p.Id != _newestId;
        _newestId = p.Id;
        if (Still || p.Rare) return light;
        var age = p.At == DateTime.MinValue ? 0 : (DateTime.UtcNow - p.At).TotalSeconds;
        if (age >= 5 || !fresh) { light.Opacity = 0; return light; }
        light.Opacity = 1 - age / 5;
        var fade = new DoubleAnimation(0, TimeSpan.FromSeconds(5 - age)) { EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseIn } };
        light.BeginAnimation(UIElement.OpacityProperty, fade);
        return light;
    }
}
