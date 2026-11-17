using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using KinahLedger.Parser;
using KinahLedger.Session;
using static KinahLedger.Ui.HudKit;

namespace KinahLedger.Ui;

/// <summary>
/// Holo (the Tactical theme): projected cyan glass. Panels with their corners cut at 45°, a faint grid in the dark teal
/// ground, a cyan edge lit at the cuts; a dashed ring with your best's share as a cyan arc, the hour in white, units and
/// labels in light cyan; the next timer in a green outlined chip. A fight is the same glass: "◇ Kashapa", a thin glowing
/// coral HP line with ticks at the thirds, the rows on the dark glass, the foot after a cyan hairline.
/// The moments: a pickup sweeps a scan line across the panel and its worth materialises beside it (revealed through scan
/// lines, then still); a rare drop unfolds a hex frame round its tile; your best passed completes the ring's arc with a
/// light trail and RECORD; an entry opening traces the chip's outline in; a fight's start projects its panel out of a line;
/// its end says TARGET ELIMINATED or SIGNAL LOST.
/// </summary>
sealed partial class HudHolo : TacticalLook
{
    static readonly Color CyanC = Color.FromRgb(0x00, 0xE5, 0xFF), LiteC = Color.FromRgb(0x7F, 0xE9, 0xFF), PaleC = Color.FromRgb(0xBF, 0xF6, 0xFF),
        GroundC = Color.FromRgb(0x06, 0x20, 0x2A), Ground2C = Color.FromRgb(0x0A, 0x30, 0x3E), GreenC = Color.FromRgb(0x2B, 0xFF, 0x9A), AmberC = Color.FromRgb(0xFF, 0xC8, 0x4A),
        CoralC = Color.FromRgb(0xFF, 0x6B, 0x6B), DeepC = Color.FromRgb(0x00, 0x08, 0x0C);
    static readonly SolidColorBrush Cyan = Frozen(CyanC), Lite = Frozen(LiteC), Pale = Frozen(PaleC), White = Frozen(Colors.White), Green = Frozen(GreenC),
        Amber = Frozen(AmberC), Coral = Frozen(CoralC), LiteDim = Frozen(Tac.A(LiteC, .72));
    static readonly FontFamily Face = HudThemes.Bahn, Small = HudThemes.Mono;
    const double Cut = 11;

    readonly Grid _root = new() { Margin = new Thickness(6, 6, 84, 8), HorizontalAlignment = HorizontalAlignment.Left };
    readonly StackPanel _body = new() { HorizontalAlignment = HorizontalAlignment.Left };
    double _opacity = 0.9;

    // the timers' chips
    readonly Border _timersTop = new() { Margin = new Thickness(0, 0, 0, 5), HorizontalAlignment = HorizontalAlignment.Left };
    readonly StackPanel _timers = new() { Orientation = Orientation.Horizontal };
    double _trace = 1;
    TacSketch? _chipArt;

    // the line's glass
    readonly Grid _line = new() { HorizontalAlignment = HorizontalAlignment.Left };
    readonly TacSketch _lineGlass;
    readonly StackPanel _lineBody = new() { Margin = new Thickness(12, 9, 16, 9) };
    readonly Canvas _lineAnchor = Tac.Anchor(HorizontalAlignment.Right, VerticalAlignment.Center);
    readonly Grid _lineFx = new() { IsHitTestVisible = false, ClipToBounds = true };
    Grid? _ring;
    TacSketch? _ringArt;
    double _share = -1, _complete = 0, _trail = -1;
    bool _beat;
    FrameworkElement? _hexHost;
    double _hex = 1;
    readonly TextBlock _note = new() { FontFamily = Face, FontSize = 12, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center };
    readonly Grid _noteHost = new() { HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 4, 0, 0), Visibility = Visibility.Collapsed };

    // the fight's glass
    readonly Grid _fight = new() { HorizontalAlignment = HorizontalAlignment.Left, Visibility = Visibility.Collapsed, Margin = new Thickness(0, 0, 0, 6) };
    readonly TacSketch _fightGlass;
    readonly StackPanel _fightBody = new() { Margin = new Thickness(12, 9, 12, 9) };
    readonly Border _timersIn = new() { Margin = new Thickness(0, 0, 0, 7), HorizontalAlignment = HorizontalAlignment.Left };
    readonly Grid _fightFx = new() { IsHitTestVisible = false };
    readonly ScaleTransform _project = new(1, 1);
    double _projectLine = 0;

    public HudHolo() : base(bare: false, Face)
    {
        // the holographic list: the look's own rows (Ui/Themes/ThemeRows.cs)
        UseRows(HoloRow.Pitch, m => new HoloRow(), 20, GapLine);
        _lineGlass = new TacSketch((dc, s) => Glass(dc, s, 1));
        _line.Children.Add(_lineGlass); _line.Children.Add(_lineBody); _line.Children.Add(_lineFx); _line.Children.Add(_lineAnchor);
        _fightGlass = new TacSketch((dc, s) => { Glass(dc, s, 1.06); if (_projectLine > 0) ProjectLine(dc, s); });
        _fight.Children.Add(_fightGlass); _fight.Children.Add(_fightBody); _fight.Children.Add(_fightFx);
        _fight.RenderTransformOrigin = new Point(.5, .5); _fight.RenderTransform = _project;
        _noteHost.Children.Add(new TacSketch((dc, s) =>
        {
            var g = Tac.Cut(new Rect(.5, .5, s.Width - 1, s.Height - 1), 6);
            dc.DrawGeometry(Frozen(Tac.A(GroundC, .55 + .4 * _opacity)), Tac.P(Frozen(Tac.A(CyanC, .45)), 1), g);
        }));
        _note.Margin = new Thickness(10, 3, 12, 4);
        _noteHost.Children.Add(_note);
        _timersTop.Child = _timers;
        _body.Children.Add(_timersTop); _body.Children.Add(_fight); _body.Children.Add(_line); _body.Children.Add(_noteHost);
        _root.Children.Add(_body);
        System.Windows.Documents.TextElement.SetFontFamily(_root, Face);
    }

    public override FrameworkElement Root => _root;
    public override Thickness Inset => _root.Margin;
    public override double Corner => 0;
    public override FrameworkElement? FightFrame => _fight.Visibility == Visibility.Visible ? _fight : null;

    public override void SetOpacity(double opacity)
    {
        _opacity = Math.Clamp(opacity, 0.3, 1);
        _lineGlass.InvalidateVisual(); _fightGlass.InvalidateVisual();
    }

    protected override string LineSig(Figures f) => base.LineSig(f) + "|" + (Rare(Values)?.Id ?? "");

    // ================================================================ the glass

    /// <summary>
    /// The projected glass: a dark teal ground (a hair darker at the foot), a faint grid in it, a light band along the top,
    /// a dark hairline outside (it keeps its edge on snow) and the cyan edge, lit brighter along the two cut corners.
    /// </summary>
    void Glass(DrawingContext dc, Size s, double solid)
    {
        if (s.Width < 4 || s.Height < 4) return;
        var r = new Rect(.5, .5, s.Width - 1, s.Height - 1);
        var g = Tac.Cut(r, Cut);
        double a = Math.Min(.97, (.6 + .3 * _opacity) * solid);
        dc.DrawGeometry(null, Tac.P(Frozen(Tac.A(DeepC, .55)), 3), g);
        dc.DrawGeometry(Tac.Grad(true, (Tac.A(Ground2C, a), 0), (Tac.A(GroundC, a), .5), (Tac.A(DeepC, a), 1)), null, g);
        dc.PushClip(g);
        var grid = Frozen(Tac.A(CyanC, .055));
        for (double x = 8; x < s.Width; x += 12) dc.DrawRectangle(grid, null, new Rect(x, 0, 1, s.Height));
        for (double y = 8; y < s.Height; y += 12) dc.DrawRectangle(grid, null, new Rect(0, y, s.Width, 1));
        dc.DrawRectangle(Tac.Grad(true, (Tac.A(CyanC, .14), 0), (Tac.A(CyanC, 0), 1)), null, new Rect(0, 0, s.Width, Math.Min(18, s.Height)));
        dc.Pop();
        dc.DrawGeometry(null, Tac.P(Frozen(Tac.A(CyanC, .55)), 1), g);
        // the lit cuts: the top-left and bottom-right corners, a brighter stroke along the cut and a little of each side
        var lit = new StreamGeometry();
        using (var x = lit.Open())
        {
            x.BeginFigure(new Point(r.Left, r.Top + Cut + 10), false, false); x.LineTo(new Point(r.Left, r.Top + Cut), true, false);
            x.LineTo(new Point(r.Left + Cut, r.Top), true, false); x.LineTo(new Point(r.Left + Cut + 18, r.Top), true, false);
            x.BeginFigure(new Point(r.Right, r.Bottom - Cut - 10), false, false); x.LineTo(new Point(r.Right, r.Bottom - Cut), true, false);
            x.LineTo(new Point(r.Right - Cut, r.Bottom), true, false); x.LineTo(new Point(r.Right - Cut - 18, r.Bottom), true, false);
        }
        lit.Freeze();
        dc.DrawGeometry(null, Tac.P(Frozen(Tac.A(CyanC, .35)), 4), lit);
        dc.DrawGeometry(null, Tac.P(Cyan, 1.6), lit);
    }

    // ================================================================ the line

    protected override void DrawLine(Figures f)
    {
        _lineBody.Children.Clear();
        _ring = null; _ringArt = null; _hexHost = null;
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        bool mark = f.Abyss && f.Counted && f.Icon.Length > 0;
        if (mark) { var m = AbyssMark(f.Icon, 38); m.Margin = new Thickness(0, 0, 10, 0); row.Children.Add(m); }
        if (!HasHour(f))
        {
            var w = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            w.Children.Add(Label(L.T("STANDBY")));
            w.Children.Add(Tt(Waiting(f.Abyss), 14, Pale, Face, FontWeights.SemiBold));
            row.Children.Add(w);
        }
        else
        {
            if (HasRing(f))
            {
                _share = f.Share; _beat = f.Beat;
                _ring = new Grid { Width = 50, Height = 50, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 11, 0) };
                _ringArt = new TacSketch(DrawRing, 50, 50);
                _ring.Children.Add(_ringArt);
                var pc = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
                pc.Children.Add(Tt(f.Beat ? "★" : Pct(f.Share), 15, White, Face, FontWeights.Bold));
                if (!f.Beat) { var u = Tt("%", 9, Lite, Face, FontWeights.SemiBold); u.VerticalAlignment = VerticalAlignment.Bottom; u.Margin = new Thickness(0, 0, 0, 2); pc.Children.Add(u); }
                _ring.Children.Add(pc);
                row.Children.Add(_ring);
            }
            // the hour: the figure white, its unit light cyan; the way to your best under it
            var hour = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            var top = new StackPanel { Orientation = Orientation.Horizontal };
            var rate = Tt(f.Rate, f.Warm ? 18 : 29, f.Warm ? LiteDim : White, Face, f.Warm ? FontWeights.SemiBold : FontWeights.Bold);
            top.Children.Add(rate);
            if (f.Unit.Length > 0) { var u = Tt(f.Unit, 14, Lite, Face, FontWeights.SemiBold); u.VerticalAlignment = VerticalAlignment.Bottom; u.Margin = new Thickness(4, 0, 0, 5); top.Children.Add(u); }
            hour.Children.Add(top);
            var gap = Gap(f);
            if (gap.Length > 0 && !f.Abyss)
            {
                var gl = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(1, -3, 0, 0) };
                gl.Children.Add(Tt("◇", 10, Cyan, Face, FontWeights.Bold));
                gl.Children.Add(Tt(" " + gap.TrimStart('−'), 12.5, f.Beat ? Green : Pale, Face, FontWeights.SemiBold));
                var tb = Label(f.Beat ? L.T(" BEST") : L.T(" TO BEST")); tb.Margin = new Thickness(3, 1, 0, 0); gl.Children.Add(tb);
                hour.Children.Add(gl);
            }
            row.Children.Add(hour);
            if (f.Total.Length > 0)
            {
                row.Children.Add(new TacSketch((dc, s) =>
                {
                    dc.DrawRectangle(Tac.Grad(true, (Tac.A(CyanC, 0), 0), (Tac.A(CyanC, .55), .5), (Tac.A(CyanC, 0), 1)), null, new Rect(0, 0, 1, s.Height));
                }) { Width = 1, Height = 36, Margin = new Thickness(14, 0, 13, 0), VerticalAlignment = VerticalAlignment.Center });
                var ses = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
                ses.Children.Add(Label(f.Abyss ? f.TotalWord : L.T("SESSION")));
                var tv = Tt(f.Total, 19, White, Face, FontWeights.SemiBold); tv.Margin = new Thickness(0, -1, 0, 0);
                ses.Children.Add(tv);
                row.Children.Add(ses);
            }
        }
        _lineBody.Children.Add(row);
        // the Abyss's kills, Rank Points, deaths and Kinah, under a hairline
        if (f.Stats.Count > 0)
        {
            _lineBody.Children.Add(Hairline(new Thickness(0, 7, 0, 5)));
            var st = new StackPanel { Orientation = Orientation.Horizontal };
            foreach (var (fig, word, tone) in f.Stats)
            {
                if (st.Children.Count > 0) st.Children.Add(Tt("  ◇  ", 8, Frozen(Tac.A(CyanC, .6)), Face, FontWeights.Bold));
                st.Children.Add(Tt(fig, 13.5, ReferenceEquals(tone, Rec) ? Coral : ReferenceEquals(tone, Gold) ? Amber : White, Face, FontWeights.Bold));
                var w = Tt(" " + word, 12, Lite, Face, FontWeights.SemiBold); st.Children.Add(w);
            }
            _lineBody.Children.Add(st);
        }
        // the rare drop the window holds: its tile in a hex frame, RARE DROP and its name
        if (!KinahHidden && Rare(Values) is { } r)
        {
            _lineBody.Children.Add(Hairline(new Thickness(0, 8, 0, 6)));
            var rr = new StackPanel { Orientation = Orientation.Horizontal };
            var hex = new Grid { Width = 44, Height = 44 };
            var c = ColorOf(r.Color, r.Grade);
            hex.Children.Add(new TacSketch((dc, s) => Hex(dc, s, c), 44, 44));
            var tile = Tile(r, 26); tile.HorizontalAlignment = HorizontalAlignment.Center; tile.VerticalAlignment = VerticalAlignment.Center;
            hex.Children.Add(tile);
            _hexHost = hex;
            rr.Children.Add(hex);
            var words = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(9, 0, 0, 0) };
            _rareLabel = Label(L.T("RARE DROP"));
            words.Children.Add(_rareLabel);
            var nm = new StackPanel { Orientation = Orientation.Horizontal };
            nm.Children.Add(Tt(r.Name, 15, Frozen(c), Face, FontWeights.Bold));
            if (r.Count.Length > 0) { var cn = Tt(" " + r.Count, 12, Lite, Face, FontWeights.SemiBold); cn.Margin = new Thickness(3, 1, 0, 0); nm.Children.Add(cn); }
            words.Children.Add(nm);
            rr.Children.Add(words);
            _lineBody.Children.Add(rr);
        }
        _note.Text = f.Note;
        _note.Foreground = ReferenceEquals(f.NoteTone, Warn) || ReferenceEquals(f.NoteTone, Gold) ? Amber : ReferenceEquals(f.NoteTone, Mint) ? Green : Pale;
        _noteHost.Visibility = f.Note.Length > 0 && !KinahHidden ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>The ring: a dashed track, the share as a cyan arc with a soft glow; on your best passed the arc runs on round (with a trail).</summary>
    void DrawRing(DrawingContext dc, Size s)
    {
        double c = s.Width / 2, r = 21;
        var center = new Point(c, c);
        dc.DrawEllipse(Frozen(Tac.A(DeepC, .35)), null, center, r - 2, r - 2);
        dc.DrawEllipse(null, Tac.P(Frozen(Tac.A(LiteC, .45)), 1.2, dash: new DashStyle(new double[] { 1.5, 2.2 }, 0)), center, r, r);
        double share = Math.Clamp(_beat ? 1 : _share, 0, 1);
        double shown = share + (1 - share) * Math.Clamp(_complete, 0, 1);
        void Arc(double from, double to, Brush b, double w)
        {
            if (to - from < 0.003) return;
            if (to - from >= 0.999) { dc.DrawEllipse(null, Tac.P(b, w), center, r, r); return; }
            double a0 = 2 * Math.PI * from, a1 = 2 * Math.PI * to;
            var fig = new PathFigure { StartPoint = new Point(c + r * Math.Sin(a0), c - r * Math.Cos(a0)) };
            fig.Segments.Add(new ArcSegment(new Point(c + r * Math.Sin(a1), c - r * Math.Cos(a1)), new Size(r, r), 0, a1 - a0 > Math.PI, SweepDirection.Clockwise, true));
            var pg = new PathGeometry(); pg.Figures.Add(fig);
            dc.DrawGeometry(null, Tac.P(b, w, round: true), pg);
        }
        var tone = _beat || _complete >= 1 ? GreenC : CyanC;
        Arc(0, shown, Frozen(Tac.A(tone, .25)), 7);
        Arc(0, shown, Frozen(tone), 3);
        if (_trail is > 0 and < 1)
        {
            // the light trail: a bright head running round, the stretch behind it fading
            double head = shown, tail = Math.Max(0, head - .22);
            for (int k = 0; k < 6; k++) Arc(tail + (head - tail) * k / 6, tail + (head - tail) * (k + 1) / 6, Frozen(Tac.A(Colors.White, .15 + .14 * k)), 3);
            double ah = 2 * Math.PI * head;
            dc.DrawEllipse(Frozen(Tac.A(Colors.White, .95)), null, new Point(c + r * Math.Sin(ah), c - r * Math.Cos(ah)), 2.6, 2.6);
        }
        // the four ticks at the quarters, outside the ring
        for (int i = 0; i < 4; i++)
        {
            double a = i * Math.PI / 2;
            dc.DrawLine(Tac.P(Lite, 1.2), new Point(c + (r + 2.5) * Math.Sin(a), c - (r + 2.5) * Math.Cos(a)), new Point(c + (r + 5) * Math.Sin(a), c - (r + 5) * Math.Cos(a)));
        }
    }

    /// <summary>The hex frame round a rare drop's tile, unfolded as far as <see cref="_hex"/> (its six sides drawn in turn).</summary>
    void Hex(DrawingContext dc, Size s, Color c)
    {
        double cx = s.Width / 2, cy = s.Height / 2, r = s.Width / 2 - 2;
        var pts = Enumerable.Range(0, 6).Select(i => { double a = Math.PI / 3 * i - Math.PI / 2; return new Point(cx + r * Math.Cos(a), cy + r * Math.Sin(a)); }).ToArray();
        if (_hex >= 1) dc.DrawGeometry(Frozen(Tac.A(c, .14)), null, new PathGeometry(new[] { new PathFigure(pts[0], pts.Skip(1).Select(p => (PathSegment)new LineSegment(p, true)), true) }));
        var g = Tac.Trace(pts, _hex, out var head);
        dc.DrawGeometry(null, Tac.P(Frozen(Tac.A(c, .35)), 4.5, round: true), g);
        dc.DrawGeometry(null, Tac.P(Frozen(c), 1.6, round: true), g);
        if (_hex < 1) { var spark = Tac.Trace(pts, _hex, out _, Math.Max(0, _hex - .07)); dc.DrawGeometry(null, Tac.P(Frozen(Tac.A(Colors.White, .45)), 4.5, round: true), spark); dc.DrawGeometry(null, Tac.P(White, 2, round: true), spark); }
    }

    static TextBlock Label(string s) { var t = Tt(s, 9.5, Lite, Small, FontWeights.SemiBold); t.Opacity = .9; return t; }

    static FrameworkElement Hairline(Thickness m) => new TacSketch((dc, s) =>
        dc.DrawRectangle(Tac.Grad(false, (Tac.A(CyanC, .6), 0), (Tac.A(CyanC, .25), .7), (Tac.A(CyanC, 0), 1)), null, new Rect(0, 0, s.Width, 1))) { Height = 1, Margin = m };

    protected override void KinahShown(bool shown)
    {
        _line.Visibility = shown ? Visibility.Visible : Visibility.Collapsed;
        if (!shown) _noteHost.Visibility = Visibility.Collapsed;
        _fight.Margin = new Thickness(0, 0, 0, shown ? 6 : 0);
        PlaceTimers();
    }

    // ================================================================ the timers

    protected override void DrawTimers(IReadOnlyList<TimerBoard.Item> due, DateTime now)
    {
        _timers.Children.Clear();
        _chipArt = null;
        foreach (var i in due.Take(KinahHidden ? 2 : HudWindow.TimerLineMax))
        {
            var (text, tone) = TimerWords(i, now);
            bool first = _timers.Children.Count == 0;
            var c = tone == TimerBadge.Tone.On ? GreenC : tone == TimerBadge.Tone.Soon ? AmberC : LiteC;
            var chip = new Grid { Margin = new Thickness(first ? 0 : 5, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
            var art = new TacSketch((dc, s) =>
            {
                var r = new Rect(.5, .5, s.Width - 1, s.Height - 1);
                var pts = Tac.CutPoints(r, 6);
                dc.DrawGeometry(Frozen(Tac.A(GroundC, .6 + .35 * _opacity)), null, Tac.Cut(r, 6));
                double f = first ? _trace : 1;
                // the chip lights in its colour as its outline draws (the entry's green), then settles to a tint
                if (first && tone == TimerBadge.Tone.On) dc.DrawGeometry(Frozen(Tac.A(c, f < 1 ? .1 + .25 * (1 - f) : .1)), null, Tac.Cut(r, 6));
                var g = Tac.Trace(pts, f, out var head);
                dc.DrawGeometry(null, Tac.P(Frozen(Tac.A(c, first && tone == TimerBadge.Tone.On ? .4 : .2)), 4, round: true), g);
                dc.DrawGeometry(null, Tac.P(Frozen(c), first ? 1.5 : 1, round: true), g);
                if (f < 1) { var spark = Tac.Trace(pts, f, out _, Math.Max(0, f - .06)); dc.DrawGeometry(null, Tac.P(Frozen(Tac.A(Colors.White, .45)), 4, round: true), spark); dc.DrawGeometry(null, Tac.P(White, 1.8, round: true), spark); }
            });
            chip.Children.Add(art);
            var t = Tt(text, first ? 13 : 11.5, Frozen(first ? c : Tac.A(c, .9)), Face, first ? FontWeights.Bold : FontWeights.SemiBold);
            t.Margin = first ? new Thickness(10, 3, 11, 4) : new Thickness(8, 2, 9, 3);
            chip.Children.Add(t);
            if (first) _chipArt = art;
            _timers.Children.Add(chip);
        }
        PlaceTimers();
    }

    void PlaceTimers()
    {
        bool inside = KinahHidden && _fight.Visibility == Visibility.Visible;
        var host = inside ? _timersIn : _timersTop;
        var other = inside ? _timersTop : _timersIn;
        if (other.Child == _timers) other.Child = null;
        if (host.Child != _timers) host.Child = _timers;
        _timersTop.Visibility = !inside && _timers.Children.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        _timersIn.Visibility = inside && _timers.Children.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    // ================================================================ the fight

    protected override void DrawFight(Encounter? e, DateTime now)
    {
        _fightBody.Children.Clear();
        if (e == null) { _fight.Visibility = Visibility.Collapsed; _fightFx.Children.Clear(); PlaceTimers(); return; }
        double w = FightWidth - 10;
        _fight.Width = w;
        _fightBody.Children.Add(_timersIn);
        var head = new Grid();
        head.ColumnDefinitions.Add(new ColumnDefinition()); head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var title = new StackPanel { Orientation = Orientation.Horizontal };
        title.Children.Add(Tt("◇ ", 13, Cyan, Face, FontWeights.Bold));
        var tt = Tt(e.Title, 16, White, Face, FontWeights.SemiBold); tt.TextTrimming = TextTrimming.CharacterEllipsis; title.Children.Add(tt);
        head.Children.Add(title);
        var right = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(8, 0, 0, 0) };
        if (e.Waiting) right.Children.Add(Label(L.T("STANDBY")));
        else
        {
            var outcome = OutcomeText(e);
            if (outcome.Length > 0) { var ot = Tt(outcome.ToUpperInvariant(), 10.5, e.Outcome == "wipe" ? Coral : Green, Small, FontWeights.Bold); ot.Margin = new Thickness(0, 1, 8, 0); right.Children.Add(ot); }
            right.Children.Add(Tt(Clock(e.Duration(now)), 17, e.Live ? Amber : Pale, Face, FontWeights.Bold));
        }
        Grid.SetColumn(right, 1); head.Children.Add(right);
        _fightBody.Children.Add(head);
        if (e.Waiting)
        {
            var wt = Tt(e.ReadyText, 13.5, Pale, Face, FontWeights.SemiBold); wt.Margin = new Thickness(0, 7, 0, 1);
            _fightBody.Children.Add(wt);
        }
        else
        {
            if (HpOf(e) is { } hp)
            {
                var hpRow = new Grid { Margin = new Thickness(0, 6, 0, 7) };
                hpRow.ColumnDefinitions.Add(new ColumnDefinition()); hpRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                var track = new Grid { Height = 10, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0) };
                track.Children.Add(new TacSketch((dc, s) =>
                {
                    double y = s.Height / 2, x1 = s.Width * hp;
                    dc.DrawRectangle(Frozen(Tac.A(Color.FromRgb(0xFF, 0x8A, 0x7A), .18)), null, new Rect(0, y - 2.5, s.Width, 5));
                    // the coral line glowing: a wide faint band under a bright core
                    dc.DrawRectangle(Frozen(Tac.A(CoralC, .22)), null, new Rect(0, y - 4.5, x1, 9));
                    dc.DrawRectangle(Frozen(Tac.A(CoralC, .85)), null, new Rect(0, y - 2.5, x1, 5));
                    dc.DrawRectangle(Frozen(Color.FromRgb(0xFF, 0xB4, 0xB0)), null, new Rect(0, y - 1, x1, 1.5));
                    dc.DrawRectangle(White, null, new Rect(Math.Max(0, x1 - 1.5), y - 3.5, 1.5, 7));
                    for (int k = 1; k <= 2; k++) { double x = Math.Round(s.Width * k / 3); dc.DrawRectangle(Lite, null, new Rect(x - .5, 0, 1, s.Height)); }
                }));
                HpGhost(track, e, hp, now, 0);
                hpRow.Children.Add(track);
                var words = new StackPanel { Orientation = Orientation.Horizontal };
                words.Children.Add(Tt($"{Math.Round(hp * 100):0}%", 14, hp < .34 ? Coral : White, Face, FontWeights.Bold));
                var left = HpLeftText(e);
                if (left.Length > 0) { var lt = Tt("  " + left, 12, Lite, Face, FontWeights.SemiBold); lt.Margin = new Thickness(0, 1, 0, 0); words.Children.Add(lt); }
                Grid.SetColumn(words, 1); hpRow.Children.Add(words);
                _fightBody.Children.Add(hpRow);
            }
            else _fightBody.Children.Add(new Border { Height = 6 });
            PutRows(_fightBody, e, now, w - 24);
            if (e.HasDamage)
            {
                _fightBody.Children.Add(Hairline(new Thickness(0, 6, 0, 5)));
                _fightBody.Children.Add(Foot(e, now));
            }
        }
        _fight.Visibility = Visibility.Visible;
        PlaceTimers();
    }

    void ProjectLine(DrawingContext dc, Size s)
    {
        // the projector's line: a bright cyan line across the middle as the panel opens out of it
        double y = s.Height / 2;
        dc.DrawRectangle(Frozen(Tac.A(CyanC, .35 * _projectLine)), null, new Rect(-10, y - 3 / Math.Max(.05, _project.ScaleY), s.Width + 20, 6 / Math.Max(.05, _project.ScaleY)));
    }

    // ================================================================ the moments

    /// <summary>A pickup: a scan line sweeps across the panel and the worth materialises beside it out of scan lines, then sits still.</summary>
    protected override void Pickup(Pick p, bool held)
    {
        if (KinahHidden || _line.Visibility != Visibility.Visible) return;
        double sweep = -1;
        var scan = new TacSketch((dc, s) =>
        {
            if (sweep is < 0 or > 1) return;
            double x = -30 + (s.Width + 30) * sweep;
            // the scan: a cyan trail behind it, a soft glow round a bright core
            dc.DrawRectangle(Tac.Grad(false, (Tac.A(CyanC, 0), 0), (Tac.A(CyanC, .28), 1)), null, new Rect(x - 46, 0, 46, s.Height));
            dc.DrawRectangle(Tac.Grad(false, (Tac.A(CyanC, 0), 0), (Tac.A(CyanC, .45), .5), (Tac.A(CyanC, 0), 1)), null, new Rect(x - 5, 0, 12, s.Height));
            dc.DrawRectangle(Tac.Grad(false, (Tac.A(PaleC, .2), 0), (Tac.A(PaleC, .8), 1)), null, new Rect(x - 3, 0, 4.5, s.Height));
        });
        _lineFx.Children.Clear(); _lineFx.Children.Add(scan);
        // the chip beside the panel: the worth in white, the name small; revealed through scan lines, then still
        var chip = new Grid();
        chip.Children.Add(new TacSketch((dc, s) =>
        {
            var r = new Rect(.5, .5, s.Width - 1, s.Height - 1);
            dc.DrawGeometry(Frozen(Tac.A(GroundC, .92)), Tac.P(Cyan, 1.2), Tac.Cut(r, 7));
        }));
        var words = new StackPanel { Margin = new Thickness(9, 3, 10, 4) };
        var worth = p.Worth.Length > 0 ? p.Worth : p.Count;
        words.Children.Add(Tt(worth, 17, White, Face, FontWeights.Bold));
        var nm = Tt(p.Name.Length > 12 ? p.Name[..11].TrimEnd() + "…" : p.Name, 9.5, Lite, Small, FontWeights.SemiBold); nm.Margin = new Thickness(0, -2, 0, 0);
        words.Children.Add(nm);
        chip.Children.Add(words);
        double lines = 1;
        var mask = new TacSketch((dc, s) =>
        {
            if (lines <= 0) return;
            // the scan lines over the chip: dark rows, fewer and fainter as it resolves
            for (double y = 0; y < s.Height; y += 3) dc.DrawRectangle(Frozen(Tac.A(DeepC, .85 * lines)), null, new Rect(0, y, s.Width, 1.6));
        });
        chip.Children.Add(mask);
        var reveal = new RectangleGeometry(new Rect(0, 0, 200, 0));
        chip.Clip = reveal;
        Canvas.SetLeft(chip, 8);
        chip.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        Canvas.SetTop(chip, -chip.DesiredSize.Height / 2);
        double ch = chip.DesiredSize.Height;
        _lineAnchor.Children.Clear(); _lineAnchor.Children.Add(chip);
        Motion.Play(chip, 2000, t =>
        {
            sweep = Seg(t, 0, .4); scan.InvalidateVisual();
            // top to bottom, as the scan crosses the panel's edge; then the scan lines thin out
            reveal.Rect = new Rect(-2, -2, 220, (ch + 4) * Out(Seg(t, .2, .36)));
            lines = 1 - Seg(t, .28, .5); mask.InvalidateVisual();
            chip.Opacity = t < .86 ? Math.Max(.5, Seg(t, .2, .32)) : 1 - Seg(t, .86, 1);
        }, () => { _lineAnchor.Children.Remove(chip); _lineFx.Children.Remove(scan); });
    }

    /// <summary>A rare drop: the hex frame unfolds round its tile, side by side, a light at its head.</summary>
    protected override void RareDrop(Pick p, bool held)
    {
        if (_hexHost is not Grid hex || hex.Children[0] is not TacSketch art) return;
        // the frame unfolds and the tile blooms in (from 0.6, past 1.15, settling); RARE DROP lights amber while it plays
        var sc = new ScaleTransform(1, 1);
        hex.RenderTransformOrigin = new Point(.5, .5); hex.RenderTransform = sc;
        var label = _rareLabel;
        Motion.Play(hex, 1600, t =>
        {
            _hex = Out(Seg(t, 0, .6)); art.InvalidateVisual();
            double k = Seg(t, 0, .4);
            sc.ScaleX = sc.ScaleY = .6 + .4 * Back(k, 2.4);
            if (label != null) { label.Foreground = t < .9 ? Amber : Lite; label.FontSize = t < .9 ? 10.5 : 9.5; }
        }, () => { _hex = 1; art.InvalidateVisual(); sc.ScaleX = sc.ScaleY = 1; if (label != null) { label.Foreground = Lite; label.FontSize = 9.5; } });
    }
    TextBlock? _rareLabel;

    /// <summary>Your best passed: the ring's arc runs on round to full with a light trail and RECORD lights under it.</summary>
    protected override void BestPassed(bool held)
    {
        if (_ring == null || _ringArt == null) return;
        var ring = _ring; var art = _ringArt;
        // the arc runs from the share before (the beat ring is full: from 0.85 of it)
        double was = _share is > 0 and < 1 ? _share : .82;
        bool beat = _beat;
        var tag = new Grid { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(-30, 0, -30, -5) };
        tag.Children.Add(new TacSketch((dc, s) => dc.DrawGeometry(Frozen(Tac.A(DeepC, .95)), Tac.P(Green, 1.2), Tac.Cut(new Rect(.5, .5, s.Width - 1, s.Height - 1), 4, all: true))));
        var tt = Tt(L.T("RECORD"), 10.5, Green, Small, FontWeights.Bold); tt.Margin = new Thickness(7, 1, 7, 1);
        tag.Children.Add(tt);
        tag.Opacity = 0;
        ring.Children.Add(tag);
        Motion.Play(art, 2000, t =>
        {
            _beat = false; _share = was;
            _complete = Out(Seg(t, 0, .45));
            _trail = t < .62 ? Seg(t, 0, .62) : -1;
            if (t >= .62) { _beat = beat; _complete = 1; }
            art.InvalidateVisual();
            tag.Opacity = t < .4 ? 0 : t < .44 ? .4 : 1;
        }, () => { _complete = 0; _trail = -1; _beat = beat; art.InvalidateVisual(); ring.Children.Remove(tag); });
    }

    /// <summary>An entry opened: the green chip's outline traces itself in, a light at its head.</summary>
    protected override void EntryOpened(TimerBoard.Item i, bool held)
    {
        Motion.Play(_timers, 1100, t => { _trace = Out(t); _chipArt?.InvalidateVisual(); }, () => { _trace = 1; _chipArt?.InvalidateVisual(); });
    }

    /// <summary>A fight started: its panel projects in, out of a bright line across its middle.</summary>
    protected override void FightStarted(Encounter e, bool held)
    {
        Motion.Play(_fight, 900, t =>
        {
            // a line draws across, the glass opens out of it (the header and HP on first), the line fades off
            double x = Out(Seg(t, 0, .16)), y = Out(Seg(t, .1, .36));
            _project.ScaleX = .3 + .7 * x;
            _project.ScaleY = Math.Max(.02, y);
            _projectLine = 1 - Seg(t, .3, .55);
            _fightBody.Opacity = Seg(t, .18, .34);
            _fightGlass.InvalidateVisual();
        }, () => { _project.ScaleX = _project.ScaleY = 1; _projectLine = 0; _fightBody.Opacity = 1; _fightGlass.InvalidateVisual(); });
    }

    /// <summary>A fight ended: TARGET ELIMINATED (cyan) or SIGNAL LOST (coral) opens across the panel out of a line.</summary>
    protected override void FightEnded(Encounter e, bool held)
    {
        if (e.Outcome is not ("kill" or "wipe" or "cleared")) return;
        bool wipe = e.Outcome == "wipe";
        var c = wipe ? CoralC : CyanC;
        var band = new Grid { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 6, 0) };
        var scale = new ScaleTransform(1, 0);
        band.RenderTransformOrigin = new Point(.5, .5); band.RenderTransform = scale;
        band.Children.Add(new TacSketch((dc, s) =>
        {
            var r = new Rect(.5, .5, s.Width - 1, s.Height - 1);
            dc.DrawGeometry(Frozen(Tac.A(DeepC, .94)), Tac.P(Frozen(c), 1.4), Tac.Cut(r, 9));
            for (double y = 3; y < s.Height; y += 3) dc.DrawRectangle(Frozen(Tac.A(c, .06)), null, new Rect(0, y, s.Width, 1));
        }));
        var words = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 8, 0, 9) };
        var big = Tt(L.T(wipe ? "SIGNAL LOST" : "TARGET ELIMINATED"), 21, Frozen(wipe ? CoralC : PaleC), Face, FontWeights.Bold); big.HorizontalAlignment = HorizontalAlignment.Center;
        var small = Tt(wipe ? L.T("RECONNECTING") : "◇ " + Clock(e.Duration(e.End ?? DateTime.UtcNow)), 10, Frozen(Tac.A(c, .85)), Small, FontWeights.SemiBold); small.HorizontalAlignment = HorizontalAlignment.Center;
        words.Children.Add(big); words.Children.Add(small);
        band.Children.Add(words);
        var dim = new Border { Background = Frozen(Tac.A(DeepC, .78)), Opacity = 0 };
        _fightFx.Children.Clear(); _fightFx.Children.Add(dim); _fightFx.Children.Add(band);
        Motion.Play(_fightFx, 2600, t =>
        {
            scale.ScaleY = Math.Max(.03, Out(Seg(t, .04, .2)));
            scale.ScaleX = Out(Seg(t, 0, .1)) * .9 + .1;
            words.Opacity = t < .16 ? 0 : t < .19 ? .4 : t < .84 ? 1 : 1 - Seg(t, .84, 1);
            band.Opacity = t < .84 ? 1 : 1 - Seg(t, .84, 1);
            dim.Opacity = t < .84 ? Out(Seg(t, 0, .14)) : 1 - Seg(t, .84, 1);
        }, () => _fightFx.Children.Clear());
    }
}
