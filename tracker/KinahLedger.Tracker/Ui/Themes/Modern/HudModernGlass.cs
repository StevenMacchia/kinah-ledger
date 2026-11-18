using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using KinahLedger.Parser;
using KinahLedger.Session;
using static KinahLedger.Ui.HudKit;

namespace KinahLedger.Ui;

/// <summary>
/// Glass (the Modern theme; Steven, 2026-11-17, picked from the mock-ups): frosted acrylic over the game. A panel of dark,
/// cool glass with a fine grain in it (WPF has no backdrop blur: the scene is darkened and quietened instead), a calm
/// specular line along its top edge, a hairline rim and a soft drop under it. The Buddy is one pill: the ring of your best
/// in white, the hour big in white Inter with the best drop and the last pickups as tiles beside it, the session under it, the next timer as a green glass chip.
/// The meter is the same glass: the boss and the clock, a slim HP bar with ticks at the thirds, then each member as a lit
/// glass pill filled to their share in their class's colour with a bright leading edge; the foot after a hairline.
/// The moments are light, never motion of a figure: a sheen sweeps the glass on a pickup, a rare drop's tile rings out in
/// its colour, your best passed runs the ring round to full, an entry opening breathes on the timer chip, a fight's panel
/// settles in, and its end shows a frosted "Killed" plate over it for a moment.
/// </summary>
sealed partial class HudModernGlass : ModernLook, IThemeCard
{
    // the palette of the mock-up (mockups/modern.css, .glass)
    static readonly Color TintC = Color.FromRgb(0x12, 0x16, 0x1E), DeepC = Color.FromRgb(0x06, 0x08, 0x0C), GreenC = Color.FromRgb(0x5D, 0xF2, 0xA6),
        MintC = Color.FromRgb(0x7D, 0xFF, 0xC0), PaleGreenC = Color.FromRgb(0xC6, 0xFF, 0xE2), HealInkC = Color.FromRgb(0xB9, 0xFF, 0xDA), RedC = Color.FromRgb(0xFF, 0x6B, 0x6B),
        HpAC = Color.FromRgb(0xFF, 0x5A, 0x6E), HpBC = Color.FromRgb(0xFF, 0x8A, 0x5C), AmberC = Color.FromRgb(0xFF, 0xC8, 0x5A), YouInkC = Color.FromRgb(0x0B, 0x12, 0x20);
    static readonly SolidColorBrush White = Frozen(Colors.White), Soft = Mod.Br(Colors.White, .85), Dim = Mod.Br(Colors.White, .62), Faint = Mod.Br(Colors.White, .45),
        Green = Frozen(GreenC), Mint = Frozen(MintC), Red = Frozen(RedC), Amber = Frozen(AmberC);
    const double Radius = 18, Pad = 14;
    /// <summary>The pickup strip on the glass: the Buddy's 28 px tiles beside the hour, a faint white rule, the amber star.</summary>
    static readonly PickStrip.Look Strip = new(28, 4, true, Mod.Br(Colors.White, .22), Amber);

    // room round the panels for the drop under them (drawn inside the window: nothing past its edge)
    readonly Grid _root = new() { Margin = new Thickness(10, 8, 10, 16), HorizontalAlignment = HorizontalAlignment.Left };
    readonly StackPanel _body = new() { HorizontalAlignment = HorizontalAlignment.Left };
    double _opacity = 0.9;

    // the timers: the next one inside the Buddy, the rest above it (in a dungeon, inside the meter's panel)
    readonly WrapPanel _timersTop = new() { Margin = new Thickness(0, 0, 0, 6), HorizontalAlignment = HorizontalAlignment.Left, Visibility = Visibility.Collapsed };
    readonly WrapPanel _timersIn = new() { Margin = new Thickness(0, 0, 0, 8), Visibility = Visibility.Collapsed };
    readonly Border _lineTimer = new() { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 0) };
    List<FrameworkElement> _chips = new();
    double _breath = 0;

    // the Buddy
    readonly Grid _line = new() { HorizontalAlignment = HorizontalAlignment.Left };
    readonly TacSketch _lineGlass;
    readonly StackPanel _lineBody = new() { Margin = new Thickness(8, 8, 14, 8) };
    readonly Grid _lineFx = new() { IsHitTestVisible = false };
    Grid? _ring;
    TacSketch? _ringArt;
    double _share = -1, _complete = 0, _glint = -1, _sheen = -1;
    bool _beat;
    Grid? _rareTile;
    TacSketch? _rareRing;
    double _rarePulse = -1;
    readonly TextBlock _note = new() { FontSize = 11.5, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 4, 12, 5) };
    readonly Grid _noteHost = new() { HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 6, 0, 0), Visibility = Visibility.Collapsed };

    // the meter
    readonly Grid _fight = new() { HorizontalAlignment = HorizontalAlignment.Left, Visibility = Visibility.Collapsed, Margin = new Thickness(0, 0, 0, 8) };
    readonly TacSketch _fightGlass;
    readonly StackPanel _fightBody = new() { Margin = new Thickness(Pad, 12, Pad, 11) };
    readonly Grid _fightFx = new() { IsHitTestVisible = false };
    readonly TranslateTransform _settle = new();

    public HudModernGlass()
    {
        // the meter's own rows (Ui/Themes/ThemeRows.cs), a fit shared by the rows so the share column holds or goes for all
        var fit = new GlassFit();
        UseRows(GlassRow.Pitch, m => new GlassRow(fit), 22, GapLine);
        _lineGlass = new TacSketch((dc, s) => GlassPanel(dc, s, Radius, _opacity, _sheen));
        _line.Children.Add(_lineGlass); _line.Children.Add(_lineBody); _line.Children.Add(_lineFx);
        _fightGlass = new TacSketch((dc, s) => GlassPanel(dc, s, Radius, _opacity, -1));
        _fight.Children.Add(_fightGlass); _fight.Children.Add(_fightBody); _fight.Children.Add(_fightFx);
        _fight.RenderTransform = _settle;
        _noteHost.Children.Add(new TacSketch((dc, s) => GlassPanel(dc, s, s.Height / 2, _opacity, -1, shadow: false)));
        _noteHost.Children.Add(_note);
        _body.Children.Add(_timersTop); _body.Children.Add(_fight); _body.Children.Add(_line); _body.Children.Add(_noteHost);
        _root.Children.Add(_body);
        System.Windows.Documents.TextElement.SetFontFamily(_root, Mod.Face);
        TextOptions.SetTextFormattingMode(_root, TextFormattingMode.Display);
    }

    public override FrameworkElement Root => _root;
    public override Thickness Inset => _root.Margin;
    public override double Corner => Radius;
    public override FrameworkElement? FightFrame => _fight.Visibility == Visibility.Visible ? _fight : null;

    public override void SetOpacity(double opacity)
    {
        _opacity = Math.Clamp(opacity, 0.3, 1);
        _lineGlass.InvalidateVisual(); _fightGlass.InvalidateVisual();
        foreach (var c in _noteHost.Children.OfType<TacSketch>()) c.InvalidateVisual();
    }

    protected override string LineSig(Figures f) => base.LineSig(f) + "|" + (Rare(Values)?.Id ?? "") + "|" + (Picks(Values).FirstOrDefault()?.Id ?? "") + "|" + Values.GetValueOrDefault("show", "") + "|" + PickStrip.Sig(Values, Strip);

    // ================================================================ the glass

    /// <summary>The grain in the glass: a tile of faint light and dark specks, made once.</summary>
    static readonly ImageBrush Grain = MakeGrain();

    static ImageBrush MakeGrain()
    {
        const int n = 96;
        var px = new byte[n * n * 4];
        var rnd = new Random(1117);
        for (int i = 0; i < n * n; i++)
        {
            bool light = rnd.Next(2) == 0;
            byte v = light ? (byte)255 : (byte)0;
            byte a = (byte)rnd.Next(0, light ? 13 : 22);
            // premultiplied BGRA
            px[i * 4] = px[i * 4 + 1] = px[i * 4 + 2] = (byte)(v * a / 255); px[i * 4 + 3] = a;
        }
        var bmp = System.Windows.Media.Imaging.BitmapSource.Create(n, n, 96, 96, PixelFormats.Pbgra32, null, px, n * 4);
        bmp.Freeze();
        var b = new ImageBrush(bmp) { TileMode = TileMode.Tile, Viewport = new Rect(0, 0, n, n), ViewportUnits = BrushMappingMode.Absolute, Stretch = Stretch.None };
        b.Freeze();
        return b;
    }

    /// <summary>
    /// The frosted glass: a soft drop under it (outside its shape only), the dark cool tint (the scene darkened and quietened
    /// as a blur would), the grain, a faint light falling from the top, the hairline rim and the specular line along the top.
    /// <paramref name="sheen"/> 0 to 1: a pickup's band of light crossing it.
    /// </summary>
    internal static void GlassPanel(DrawingContext dc, Size s, double radius, double opacity, double sheen, bool shadow = true)
    {
        if (s.Width < 4 || s.Height < 4) return;
        var r = new Rect(.5, .5, s.Width - 1, s.Height - 1);
        var shape = Mod.Pill(r, radius);
        if (shadow)
        {
            // the drop: rings outside the shape, deeper below it (a 40 px blur's look, kept inside the window's room)
            for (int k = 1; k <= 7; k++)
            {
                var o = new Rect(r.X - k * 1.1, r.Y - k * .6 + 3, r.Width + k * 2.2, r.Height + k * 1.6 + 2);
                var ring = new CombinedGeometry(GeometryCombineMode.Exclude, Mod.Pill(o, radius + k), shape);
                ring.Freeze();
                dc.DrawGeometry(Mod.Br(Colors.Black, .055 - k * .005), null, ring);
            }
        }
        // the scene darkened and quietened (a blur's look without one), with a faint frost of light over it
        double a = Math.Clamp(.48 + .3 * opacity, .5, .86);
        dc.DrawGeometry(Mod.Grad(true, (Mod.A(TintC, a), 0), (Mod.A(DeepC, Math.Min(.92, a + .07)), 1)), null, shape);
        dc.DrawGeometry(Mod.Br(Color.FromRgb(0xB8, 0xC4, 0xD8), .035), null, shape);
        dc.PushClip(shape);
        dc.PushOpacity(.9);
        dc.DrawRectangle(Grain, null, new Rect(0, 0, s.Width, s.Height));
        dc.Pop();
        dc.DrawRectangle(Mod.Grad(true, (Mod.A(Colors.White, .085), 0), (Mod.A(Colors.White, .02), .45), (Mod.A(Colors.White, 0), 1)), null, new Rect(0, 0, s.Width, s.Height));
        if (sheen is > 0 and < 1)
        {
            // a soft diagonal band of light crossing left to right
            double x = -80 + (s.Width + 160) * sheen;
            var band = new StreamGeometry();
            using (var g = band.Open()) { g.BeginFigure(new Point(x, 0), true, true); g.LineTo(new Point(x + 46, 0), true, false); g.LineTo(new Point(x + 16, s.Height), true, false); g.LineTo(new Point(x - 30, s.Height), true, false); }
            band.Freeze();
            dc.DrawGeometry(Mod.Grad(false, (Mod.A(Colors.White, 0), 0), (Mod.A(Colors.White, .16), .5), (Mod.A(Colors.White, 0), 1)), null, band);
        }
        // the specular line: one pixel of light along the top, fading at the corners
        dc.DrawRectangle(Mod.Grad(false, (Mod.A(Colors.White, 0), 0), (Mod.A(Colors.White, .3), .2), (Mod.A(Colors.White, .3), .8), (Mod.A(Colors.White, 0), 1)), null, new Rect(radius * .5, 1.5, Math.Max(0, s.Width - radius), 1));
        dc.Pop();
        dc.DrawGeometry(null, Mod.P(Mod.Br(Colors.White, .16), 1), shape);
    }

    /// <summary>A small glass pill (a mark, a chip): its fill, an optional rim, its words.</summary>
    static Border Chip(string s, Brush fill, Brush ink, double size = 9.5, Brush? rim = null, bool caps = true, Thickness? pad = null)
    {
        var t = Tt(caps ? s.ToUpperInvariant() : s, size, ink, FontWeights.Bold);
        t.Margin = new Thickness(0, -.5, 0, 0);
        return new Border
        {
            Background = fill, BorderBrush = rim, BorderThickness = new Thickness(rim != null ? 1 : 0), CornerRadius = new CornerRadius(4),
            Padding = pad ?? new Thickness(5, 1, 5, 1), VerticalAlignment = VerticalAlignment.Center, Child = t,
        };
    }

    /// <summary>A hairline across the glass.</summary>
    static FrameworkElement Hairline(Thickness m) => new TacSketch((dc, s) =>
        dc.DrawRectangle(Mod.Grad(false, (Mod.A(Colors.White, .04), 0), (Mod.A(Colors.White, .16), .5), (Mod.A(Colors.White, .04), 1)), null, new Rect(0, 0, s.Width, 1))) { Height = 1, Margin = m };

    // ================================================================ the Buddy

    protected override void DrawLine(Figures f)
    {
        _lineBody.Children.Clear();
        _ring = null; _ringArt = null; _rareTile = null; _rareRing = null; _timerUp = false;
        bool dim = Values.GetValueOrDefault("show", "") == "dim";
        var row = new Grid();
        for (int i = 0; i < 4; i++) row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });   // lead, words, strip, timer
        FrameworkElement? lead = null;
        if (f.Abyss && f.Counted && f.Icon.Length > 0) lead = AbyssMark(f.Icon, 44);
        else if (HasRing(f))
        {
            _share = f.Share; _beat = f.Beat;
            _ring = new Grid { Width = 46, Height = 46 };
            _ringArt = new TacSketch(DrawRing, 46, 46);
            _ring.Children.Add(_ringArt);
            var pc = Tt(f.Beat ? "★" : Pct(f.Share), f.Beat ? 16 : 14, White, FontWeights.Bold);
            pc.HorizontalAlignment = HorizontalAlignment.Center;
            _ring.Children.Add(pc);
            lead = _ring;
        }
        else if (!HasHour(f))
        {
            // no hour yet: an empty ring, the waiting words beside it
            var g = new Grid { Width = 46, Height = 46 };
            g.Children.Add(new TacSketch((dc, s) => dc.DrawEllipse(null, Mod.P(Mod.Br(Colors.White, .18), 4), new Point(23, 23), 19.5, 19.5), 46, 46));
            lead = g;
        }
        if (lead != null) { lead.Margin = new Thickness(0, 0, 12, 0); lead.VerticalAlignment = VerticalAlignment.Center; row.Children.Add(lead); }
        else _lineBody.Margin = new Thickness(14, 8, 14, 8);
        if (lead != null) _lineBody.Margin = new Thickness(8, 8, 14, 8);

        var words = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        if (!HasHour(f))
        {
            words.Children.Add(Tt(Waiting(f.Abyss), 14, Soft, FontWeights.SemiBold));
        }
        else
        {
            var top = new StackPanel { Orientation = Orientation.Horizontal };
            var rate = Tt(f.Rate, f.Warm ? 16 : 24, f.Warm ? Dim : White, FontWeights.Bold);
            rate.Tag = "glass-rate";
            top.Children.Add(rate);
            if (f.Unit.Length > 0) { var u = Tt(f.Unit, 12, Faint, FontWeights.SemiBold); u.VerticalAlignment = VerticalAlignment.Bottom; u.Margin = new Thickness(2, 0, 0, 3); top.Children.Add(u); }
            words.Children.Add(top);
            // the session and the latest pickup ("894K session · Spiritstone +7.5K"); in the Abyss its stats. Farming, the
            // pickups as the strip beside the hour where it fits (the best drop and the last three), the session alone under it
            // (with a timer chip at the line's end and no room left beside it, the chip goes up to the timers over the Buddy)
            var sub = Sub(f, true);
            if (StripTime(f))
            {
                var bare = Sub(f, false);
                double baseW = (lead != null ? 8 + Mod.W(lead) : 14) + 14, timerW = LineTimerW();
                double wordsW = Math.Max(Mod.W(top), Mod.W(bare)), oldW = Math.Max(Mod.W(top), Mod.W(sub));
                FrameworkElement? strip = null;
                foreach (var look in StripFall(Strip))
                {
                    if ((strip = StripIn(StripRoom(FightWidth - 8, baseW + timerW, wordsW, oldW, 14), look)) != null) break;
                    if (timerW > 0 && (strip = StripIn(StripRoom(FightWidth - 8, baseW, wordsW, oldW + timerW, 14), look)) != null) { _timerUp = true; break; }
                }
                if (strip != null)
                {
                    sub = bare;
                    strip.Margin = new Thickness(14, 0, 0, 0);
                    if (dim) strip.Opacity = .6;
                    Grid.SetColumn(strip, 2); row.Children.Add(strip);
                }
            }
            if (sub.Inlines.Count > 0) words.Children.Add(sub);
        }
        _stripTimerW = StripTime(f) ? LineTimerW() : -1;
        Grid.SetColumn(words, 1); row.Children.Add(words);
        if (dim) { foreach (var el in new UIElement[] { words }) el.Opacity = .6; if (lead != null) lead.Opacity = .6; }
        Grid.SetColumn(_lineTimer, 3);
        if (_lineTimer.Parent is Panel old) old.Children.Remove(_lineTimer);
        row.Children.Add(_lineTimer);
        _lineBody.Children.Add(row);
        DrawRare();
        _note.Text = f.Note;
        _note.Foreground = ReferenceEquals(f.NoteTone, Warn) || ReferenceEquals(f.NoteTone, Gold) ? Amber : ReferenceEquals(f.NoteTone, HudKit.Mint) ? Green : Soft;
        _noteHost.Visibility = f.Note.Length > 0 && !KinahHidden ? Visibility.Visible : Visibility.Collapsed;
        PlaceTimers();
    }

    /// <summary>The timer chip's room on the Buddy's line (the next timer sits at its end), 0 with none.</summary>
    double LineTimerW() => _chips.Count > 0 && !KinahHidden ? 12 + Mod.W(_chips[0]) : 0;

    /// <summary>The timer's room the line's strip was fitted to (-1: no strip time); a timer that changes it draws the line again.</summary>
    double _stripTimerW = -1;

    /// <summary>The next timer's chip sits with the others over the Buddy, to leave the line's room to the pickup strip.</summary>
    bool _timerUp;

    /// <summary>The line under the hour: the session, the latest pickup (<paramref name="pick"/>), "Best here"; in the Abyss its stats.</summary>
    TextBlock Sub(Figures f, bool pick)
    {
        var sub = Tt("", 11.5, Soft, FontWeights.Medium);
        sub.Margin = new Thickness(0, 3, 0, 0);
        if (f.Abyss && f.Stats.Count > 0)
        {
            if (f.Total.Length > 0) { sub.Inlines.Add(new System.Windows.Documents.Run(f.Total) { FontWeight = FontWeights.Bold, Foreground = White }); sub.Inlines.Add(new System.Windows.Documents.Run(" " + f.TotalWord)); }
            foreach (var (fig, word, tone) in f.Stats)
            {
                if (sub.Inlines.Count > 0) sub.Inlines.Add(new System.Windows.Documents.Run("  ·  ") { Foreground = Faint });
                sub.Inlines.Add(new System.Windows.Documents.Run(fig) { FontWeight = FontWeights.Bold, Foreground = ReferenceEquals(tone, Rec) ? Red : ReferenceEquals(tone, Gold) ? Amber : White });
                sub.Inlines.Add(new System.Windows.Documents.Run(" " + word));
            }
        }
        else
        {
            if (f.Total.Length > 0) { sub.Inlines.Add(new System.Windows.Documents.Run(f.Total) { FontWeight = FontWeights.Bold, Foreground = White }); sub.Inlines.Add(new System.Windows.Documents.Run(" " + f.TotalWord)); }
            if (pick && !f.Abyss && Picks(Values).FirstOrDefault() is { } p)
            {
                if (sub.Inlines.Count > 0) sub.Inlines.Add(new System.Windows.Documents.Run("  ·  ") { Foreground = Faint });
                sub.Inlines.Add(new System.Windows.Documents.Run(p.Name.Length > 22 ? p.Name[..21].TrimEnd() + "…" : p.Name));
                if (p.Worth.Length > 0) sub.Inlines.Add(new System.Windows.Documents.Run(" " + p.Worth) { FontWeight = FontWeights.Bold, Foreground = p.Kinah ? Amber : Green });
                else if (p.Count.Length > 0) sub.Inlines.Add(new System.Windows.Documents.Run(" " + p.Count) { Foreground = Dim });
            }
            if (!f.Abyss && f.ToBest.Length > 0 && f.Beat)
            {
                if (sub.Inlines.Count > 0) sub.Inlines.Add(new System.Windows.Documents.Run("  ·  ") { Foreground = Faint });
                sub.Inlines.Add(new System.Windows.Documents.Run(L.T("Best here")) { FontWeight = FontWeights.Bold, Foreground = Green });
            }
        }
        return sub;
    }

    /// <summary>The rare drop the window holds: its tile ringed in its colour, "Rare drop" and its name.</summary>
    void DrawRare()
    {
        if (!KinahHidden && Rare(Values) is { } r)
        {
            _lineBody.Children.Add(Hairline(new Thickness(0, 8, 0, 7)));
            var rr = new StackPanel { Orientation = Orientation.Horizontal };
            var c = ColorOf(r.Color, r.Grade);
            var tile = new Grid { Width = 46, Height = 36 };
            _rareRing = new TacSketch((dc, s) =>
            {
                var box = new Rect((s.Width - 32) / 2, 2, 32, 32);
                dc.DrawGeometry(Mod.Br(c, .16), Mod.P(Mod.Br(c, .75), 1.2), Mod.Pill(box, 9));
                if (_rarePulse is > 0 and < 1)
                {
                    // a ring of the colour spreading out and fading (inside the tile's room)
                    double k = Out(_rarePulse) * 6;
                    var o = new Rect(box.X - k, box.Y - k * .4, box.Width + 2 * k, box.Height + k * .8);
                    dc.DrawGeometry(null, Mod.P(Mod.Br(c, .7 * (1 - _rarePulse)), 2), Mod.Pill(o, 9 + k));
                }
            });
            tile.Children.Add(_rareRing);
            var t = Tile(r, 24); t.HorizontalAlignment = HorizontalAlignment.Center; t.VerticalAlignment = VerticalAlignment.Top; t.Margin = new Thickness(0, 6, 0, 0);
            tile.Children.Add(t);
            _rareTile = tile;
            rr.Children.Add(tile);
            var w = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0) };
            w.Children.Add(Tt(L.T("Rare drop"), 10, Dim, FontWeights.SemiBold));
            var nm = Tt(r.Name, 14, Frozen(Mod.Mix(c, Colors.White, .25)), FontWeights.Bold);
            if (r.Count.Length > 0) nm.Inlines.Add(new System.Windows.Documents.Run("  " + r.Count) { FontSize = 11.5, FontWeight = FontWeights.SemiBold, Foreground = Dim });
            w.Children.Add(nm);
            rr.Children.Add(w);
            _lineBody.Children.Add(rr);
        }
    }

    /// <summary>The ring of your best: a faint track, the share in white with round ends; past your best, green and full.</summary>
    void DrawRing(DrawingContext dc, Size s)
    {
        double c = s.Width / 2, r = 19.5;
        var center = new Point(c, c);
        dc.DrawEllipse(null, Mod.P(Mod.Br(Colors.White, .18), 4), center, r, r);
        double share = Math.Clamp(_beat ? 1 : _share, 0, 1);
        double shown = share + (1 - share) * Math.Clamp(_complete, 0, 1);
        var tone = _beat || _complete >= 1 ? GreenC : Colors.White;
        void Arc(double from, double to, Brush b, double w)
        {
            if (to - from < 0.003) return;
            if (to - from >= 0.999) { dc.DrawEllipse(null, Mod.P(b, w), center, r, r); return; }
            double a0 = 2 * Math.PI * from, a1 = 2 * Math.PI * to;
            var fig = new PathFigure { StartPoint = new Point(c + r * Math.Sin(a0), c - r * Math.Cos(a0)) };
            fig.Segments.Add(new ArcSegment(new Point(c + r * Math.Sin(a1), c - r * Math.Cos(a1)), new Size(r, r), 0, a1 - a0 > Math.PI, SweepDirection.Clockwise, true));
            var pg = new PathGeometry(); pg.Figures.Add(fig);
            dc.DrawGeometry(null, Mod.P(b, w, round: true), pg);
        }
        Arc(0, shown, Frozen(tone), 4);
        if (_glint is > 0 and < 1)
        {
            // a bright point running round ahead of the arc
            double ah = 2 * Math.PI * shown;
            var p = new Point(c + r * Math.Sin(ah), c - r * Math.Cos(ah));
            dc.DrawEllipse(Mod.Br(Colors.White, .35), null, p, 5, 5);
            dc.DrawEllipse(White, null, p, 2.6, 2.6);
        }
    }

    protected override void KinahShown(bool shown)
    {
        _line.Visibility = shown ? Visibility.Visible : Visibility.Collapsed;
        if (!shown) _noteHost.Visibility = Visibility.Collapsed;
        _fight.Margin = new Thickness(0, 0, 0, shown ? 8 : 0);
        PlaceTimers();
    }

    // ================================================================ the timers

    protected override void DrawTimers(IReadOnlyList<TimerBoard.Item> due, DateTime now)
    {
        _chips = new();
        foreach (var i in due.Take(KinahHidden ? 2 : HudWindow.TimerLineMax))
        {
            var (text, tone) = TimerWords(i, now);
            bool first = _chips.Count == 0;
            _chips.Add(TimerChip(text, tone, first));
        }
        // the line's pickup strip was fitted beside the timer chip: a chip that comes, goes or changes its width fits it again
        if (_stripTimerW >= 0 && Figs is { } f && Math.Abs(LineTimerW() - _stripTimerW) > .5) DrawLine(f);
        PlaceTimers();
    }

    /// <summary>A timer as a glass chip: green while its entry is open (a dot before it), amber when soon, else quiet.</summary>
    FrameworkElement TimerChip(string text, TimerBadge.Tone tone, bool first)
    {
        var c = tone == TimerBadge.Tone.On ? GreenC : tone == TimerBadge.Tone.Soon ? AmberC : Colors.White;
        bool on = tone == TimerBadge.Tone.On;
        var g = new Grid { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) };
        g.Children.Add(new TacSketch((dc, s) =>
        {
            var r = new Rect(.5, .5, s.Width - 1, s.Height - 1);
            var shape = Mod.Pill(r, r.Height / 2);
            dc.DrawGeometry(Mod.Br(DeepC, .55 + .3 * _opacity), null, shape);
            double glow = on && first ? _breath : 0;
            dc.DrawGeometry(Mod.Br(c, (c == Colors.White ? .08 : .18) + .22 * glow), Mod.P(Mod.Br(c, (c == Colors.White ? .22 : .4) + .4 * glow), 1), shape);
        }));
        var sp = new StackPanel { Orientation = Orientation.Horizontal, Margin = first ? new Thickness(10, 5, 11, 5) : new Thickness(8, 3, 9, 3) };
        if (tone != TimerBadge.Tone.Quiet)
            sp.Children.Add(new System.Windows.Shapes.Ellipse { Width = 7, Height = 7, Fill = Frozen(on ? GreenC : c), Margin = new Thickness(0, 0, 6, 0), VerticalAlignment = VerticalAlignment.Center });
        var ink = on ? Frozen(PaleGreenC) : c == Colors.White ? Soft : Frozen(Mod.Mix(c, Colors.White, .35));
        sp.Children.Add(Tt(text, first ? 12 : 11, ink, FontWeights.Bold));
        g.Children.Add(sp);
        return g;
    }

    void PlaceTimers()
    {
        foreach (var host in new Panel[] { _timersTop, _timersIn }) host.Children.Clear();
        _lineTimer.Child = null;
        bool inFight = KinahHidden && _fight.Visibility == Visibility.Visible;
        bool inLine = !KinahHidden && _line.Visibility == Visibility.Visible && !_timerUp;
        for (int i = 0; i < _chips.Count; i++)
        {
            var c = _chips[i];
            if (c.Parent is Panel p) p.Children.Remove(c); else if (c.Parent is Decorator d) d.Child = null;
            if (i == 0 && inLine) _lineTimer.Child = c;
            else (inFight ? _timersIn : _timersTop).Children.Add(c);
        }
        _timersTop.Visibility = _timersTop.Children.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        _timersIn.Visibility = _timersIn.Children.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        _lineTimer.Visibility = _lineTimer.Child != null ? Visibility.Visible : Visibility.Collapsed;
    }

    // ================================================================ the meter

    protected override void DrawFight(Encounter? e, DateTime now)
    {
        _fightBody.Children.Clear();
        if (e == null) { _fight.Visibility = Visibility.Collapsed; _fightFx.Children.Clear(); PlaceTimers(); return; }
        double w = FightWidth - 8;
        _fight.Width = w;
        double inner = w - 2 * Pad;
        _fightBody.Children.Add(_timersIn);
        // the boss and the clock (the outcome as a green or red chip before it)
        var head = new Grid();
        head.ColumnDefinitions.Add(new ColumnDefinition()); head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var title = Tt(e.Title, 15, White, FontWeights.Bold);
        title.TextTrimming = TextTrimming.CharacterEllipsis;
        head.Children.Add(title);
        var right = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(8, 0, 0, 0) };
        if (e.Waiting) right.Children.Add(Chip(L.T("Ready"), Mod.Br(Colors.White, .12), Soft, 10.5, Mod.Br(Colors.White, .25), caps: false, pad: new Thickness(8, 1, 8, 2)));
        else
        {
            var outcome = OutcomeText(e);
            if (outcome.Length > 0)
            {
                bool bad = e.Outcome == "wipe";
                var oc = Chip(outcome, Mod.Br(bad ? RedC : GreenC, .18), bad ? Frozen(Mod.Mix(RedC, Colors.White, .35)) : Mint, 11, Mod.Br(bad ? RedC : MintC, .35), caps: false, pad: new Thickness(8, 1, 8, 2));
                oc.CornerRadius = new CornerRadius(99); oc.Margin = new Thickness(0, 1, 8, 0); oc.Tag = "glass-outcome";
                right.Children.Add(oc);
            }
            var clock = Tt(Clock(e.Duration(now)), 20, White, FontWeights.Bold);
            right.Children.Add(clock);
        }
        Grid.SetColumn(right, 1); head.Children.Add(right);
        _fightBody.Children.Add(head);
        if (e.Waiting)
        {
            var wt = Tt(e.ReadyText, 13, Soft, FontWeights.Medium); wt.Margin = new Thickness(0, 6, 0, 0); wt.TextWrapping = TextWrapping.Wrap;
            _fightBody.Children.Add(wt);
            if (PartyBeforeHit(e)) { _fightBody.Children.Add(new Border { Height = 10 }); PutRows(_fightBody, e, now, inner); }
        }
        else
        {
            if (HpOf(e) is { } hp)
            {
                var track = new Grid { Height = 6, Margin = new Thickness(0, 10, 0, 4) };
                track.Children.Add(new TacSketch((dc, s) =>
                {
                    var r = new Rect(0, 0, s.Width, s.Height);
                    var shape = Mod.Pill(r, 3);
                    dc.DrawGeometry(Mod.Br(Colors.White, .12), null, shape);
                    dc.PushClip(shape);
                    double x1 = s.Width * hp;
                    if (x1 > .5) dc.DrawRectangle(Mod.Grad(false, (HpAC, 0), (HpBC, 1)), null, new Rect(0, 0, x1, s.Height));
                    dc.DrawRectangle(Mod.Br(Colors.White, .3), null, new Rect(0, 0, x1, 1));
                    for (int k = 1; k <= 2; k++) { double x = Math.Round(s.Width * k / 3); dc.DrawRectangle(Mod.Br(Colors.Black, .45), null, new Rect(x - 1, 0, 2, s.Height)); }
                    dc.Pop();
                }));
                HpGhost(track, e, hp, now, 3);
                _fightBody.Children.Add(track);
                var words = Tt("", 11, Soft, FontWeights.Medium);
                words.HorizontalAlignment = HorizontalAlignment.Right;
                words.Inlines.Add(new System.Windows.Documents.Run($"{Math.Round(hp * 100):0}%") { FontWeight = FontWeights.Bold, Foreground = hp < .34 ? Frozen(HpBC) : White });
                var left = HpLeftText(e);
                if (left.Length > 0) words.Inlines.Add(new System.Windows.Documents.Run("  ·  " + left));
                _fightBody.Children.Add(words);
                _fightBody.Children.Add(new Border { Height = 8 });
            }
            else _fightBody.Children.Add(new Border { Height = 10 });
            PutRows(_fightBody, e, now, inner);
            if (e.HasDamage)
            {
                _fightBody.Children.Add(Hairline(new Thickness(0, 9, 0, 7)));
                _fightBody.Children.Add(Foot(e, now));
            }
        }
        _fight.Visibility = Visibility.Visible;
        PlaceTimers();
    }

    // ================================================================ the moments

    /// <summary>A pickup: a band of light sweeps the glass, and the pickup's worth glows on its line for a moment.</summary>
    protected override void Pickup(Pick p, bool held)
    {
        if (KinahHidden || _line.Visibility != Visibility.Visible) return;
        Motion.Play(_lineGlass, 1100, t => { _sheen = Out(t); _lineGlass.InvalidateVisual(); }, () => { _sheen = -1; _lineGlass.InvalidateVisual(); });
    }

    /// <summary>A rare drop: a ring of its colour spreads out from the tile and fades.</summary>
    protected override void RareDrop(Pick p, bool held)
    {
        if (_rareRing is not { } ring) return;
        Motion.Play(ring, 1400, t => { _rarePulse = Seg(t, 0, .8); ring.InvalidateVisual(); }, () => { _rarePulse = -1; ring.InvalidateVisual(); });
    }

    /// <summary>Your best passed: the ring's arc runs on round to full, a point of light at its head, and turns green.</summary>
    protected override void BestPassed(bool held)
    {
        if (_ring == null || _ringArt == null) return;
        var art = _ringArt;
        double was = _share is > 0 and < 1 ? _share : .82;
        bool beat = _beat;
        Motion.Play(art, 1600, t =>
        {
            _beat = false; _share = was;
            _complete = Out(Seg(t, 0, .55));
            _glint = t < .6 ? Seg(t, 0, .6) : -1;
            if (t >= .6) { _beat = beat; _complete = 1; }
            art.InvalidateVisual();
        }, () => { _complete = 0; _glint = -1; _beat = beat; art.InvalidateVisual(); });
    }

    /// <summary>An entry opened: the green chip breathes in its colour twice.</summary>
    protected override void EntryOpened(TimerBoard.Item i, bool held)
    {
        if (_chips.FirstOrDefault() is not Grid chip || chip.Children[0] is not TacSketch art) return;
        Motion.Play(chip, 1600, t => { _breath = Math.Sin(Math.PI * 2 * Math.Clamp(t, 0, 1)) is var s && s > 0 ? s : -s; art.InvalidateVisual(); }, () => { _breath = 0; art.InvalidateVisual(); });
    }

    /// <summary>A fight started: its panel settles in, rising a few pixels as it fades up.</summary>
    protected override void FightStarted(Encounter e, bool held)
    {
        Motion.Play(_fight, 420, t => { double k = Out(t); _settle.Y = 6 * (1 - k); _fight.Opacity = .2 + .8 * k; }, () => { _settle.Y = 0; _fight.Opacity = 1; });
    }

    /// <summary>A fight ended: a frosted plate saying how ("Killed 12:47", "Wiped") shows over the meter for a moment.</summary>
    protected override void FightEnded(Encounter e, bool held)
    {
        if (e.Outcome is not ("kill" or "wipe" or "cleared")) return;
        bool wipe = e.Outcome == "wipe";
        var c = wipe ? RedC : GreenC;
        var plate = new Grid { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        plate.Children.Add(new TacSketch((dc, s) =>
        {
            GlassPanel(dc, s, 16, 1, -1, shadow: false);
            dc.DrawGeometry(null, Mod.P(Mod.Br(c, .55), 1.2), Mod.Pill(new Rect(.5, .5, s.Width - 1, s.Height - 1), 16));
        }));
        var words = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(18, 9, 18, 10) };
        words.Children.Add(Tt(OutcomeText(e), 20, wipe ? Frozen(Mod.Mix(RedC, Colors.White, .3)) : Mint, FontWeights.Bold));
        words.Children.Add(Tt("  " + Clock(e.Duration(e.End ?? DateTime.UtcNow)), 16, White, FontWeights.SemiBold));
        plate.Children.Add(words);
        var dim = new Border { Background = Mod.Br(DeepC, .6), CornerRadius = new CornerRadius(Radius), Opacity = 0 };
        var sc = new ScaleTransform(1, 1);
        plate.RenderTransformOrigin = new Point(.5, .5); plate.RenderTransform = sc;
        _fightFx.Children.Clear(); _fightFx.Children.Add(dim); _fightFx.Children.Add(plate);
        Motion.Play(_fightFx, 2400, t =>
        {
            double i = Out(Seg(t, 0, .15)), o = Seg(t, .82, 1);
            dim.Opacity = i * (1 - o);
            plate.Opacity = i * (1 - o);
            sc.ScaleX = sc.ScaleY = .94 + .06 * i;
        }, () => _fightFx.Children.Clear());
    }
}
