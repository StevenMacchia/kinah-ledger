using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using KinahLedger.Parser;
using KinahLedger.Session;
using static KinahLedger.Ui.HudKit;

namespace KinahLedger.Ui;

/// <summary>
/// Storm (the Elements theme: Frost, Storm, Magma, Tide; pack 3, 2026-11-20): lightning. Every surface is storm-cloud glass, a
/// cut-cornered panel of dark slate-indigo with cloud mottling in it, a breath of violet low at its right and a charged rim
/// (blue along the top, a white charge on its cut corner). The meter's rows are charged conduits: a slot of night glass filled
/// in the member's class colour, charging up to a white-hot leading edge, an electric core along its foot and a static spark
/// crown where it ends (seeded bolts, the same in every draw; the leader's crackles brightest), the lead's bolt glyph by their
/// rank. The Buddy: the run's ring as a charged coil, the hour big in electric white, the pickups in tiles with a blue arc rim.
/// The moments are lightning, never motion of a figure: a fight opens with a thunderclap (a bolt strikes the leader's rank
/// from the panel's top, the clouds light up, the row flashes white and the glyph lands) and arcs jumping bar to bar down the
/// rows; a new leader gets the same; a kill sends the arcs down again under an outcome plate; a pickup crackles along the
/// Buddy's top edge as its worth leaps out beside it on a spark; a rare drop's strip blooms in its grade's colour with sparks
/// bursting off its tile; your best passed charges the coil round to full.
/// </summary>
sealed partial class HudStorm : ModernLook, IThemeCard
{
    static readonly Color MintC2 = Color.FromRgb(0x5D, 0xF2, 0xA6), MarkMintC = Color.FromRgb(0x7D, 0xFF, 0xC0),
        CoralC = Color.FromRgb(0xFF, 0x86, 0x86), GreyC = Color.FromRgb(0x8A, 0x90, 0xA0), WarnC2 = Color.FromRgb(0xFF, 0xB0, 0x86), GoldC2 = Color.FromRgb(0xFF, 0xCF, 0x6A);
    static readonly SolidColorBrush White = Frozen(Colors.White), Soft = Frozen(Mod.A(Colors.White, .84)), Quiet = Frozen(Mod.A(Colors.White, .6)),
        Mint2 = Frozen(Color.FromRgb(0x8C, 0xFF, 0xC6)), MarkMint = Frozen(MarkMintC), Coral = Frozen(CoralC);
    const double Pad = 10;
    /// <summary>How tall the storm cloud's crest rises over the meter's glass, and over the Buddy's.</summary>
    const double FightCrest = 26, LineCrest = 14;

    // the room round the look: the panels' drop inside the window; 98 at the right for the pickup's spark and chip beside the Buddy
    readonly Grid _root = new() { Margin = new Thickness(8, 8, 98, 12), HorizontalAlignment = HorizontalAlignment.Left };
    readonly StackPanel _body = new() { HorizontalAlignment = HorizontalAlignment.Left };
    double _opacity = 0.9;

    // the Buddy: its storm glass, the coil, the hour, the session and the timer chips
    readonly Grid _line = new() { HorizontalAlignment = HorizontalAlignment.Left };
    readonly StormGlass _linePanel;
    /// <summary>The pickup's crackle: a bolt drawn once, run along the Buddy's top edge by its transform.</summary>
    readonly Canvas _zapHost = new() { IsHitTestVisible = false, ClipToBounds = true };
    readonly TacSketch _zapEl;
    readonly TranslateTransform _zapAt = new();
    readonly StackPanel _lineBody = new() { Orientation = Orientation.Horizontal, Margin = new Thickness(8, 7 + LineCrest, 14, 7) };
    readonly Canvas _lineAnchor = Tac.Anchor(HorizontalAlignment.Right, VerticalAlignment.Center);
    readonly StackPanel _timers = new() { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
    readonly Border _timersInLine = new() { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 0) };
    /// <summary>The timer chips over the line, when the pickup strip takes their room on it.</summary>
    readonly Border _timersUp = new() { HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(2, 0, 0, 6), Visibility = Visibility.Collapsed };
    /// <summary>The pickup strip in Storm: 24 px tiles beside the hour (each in a blue arc rim), a blue rule, a gold star.</summary>
    static readonly PickStrip.Look Strip = new(24, 5, true, Frozen(Mod.A(VoltC, .4)), Frozen(Color.FromRgb(0xFF, 0xCF, 0x6A)));
    bool _dim;
    TacSketch? _coil;
    double _share = -1, _complete, _head = -1;
    bool _beat;
    Border? _firstChip;

    // the rare drop under the Buddy, and the notice line
    readonly Grid _rare = new() { HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 5, 0, 0), Visibility = Visibility.Collapsed };
    readonly Border _note = new()
    {
        HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 5, 0, 0), Visibility = Visibility.Collapsed, CornerRadius = new CornerRadius(3),
        Background = Frozen(Mod.A(Deep0C, .88)), BorderBrush = Frozen(Mod.A(VoltC, .22)), BorderThickness = new Thickness(1), Padding = new Thickness(9, 3, 10, 4),
    };
    readonly StackPanel _noteBody = new() { Orientation = Orientation.Horizontal };

    // the fight: its storm glass, its head, HP, rows and foot; the lightning over it
    readonly Grid _fight = new() { HorizontalAlignment = HorizontalAlignment.Left, Visibility = Visibility.Collapsed, Margin = new Thickness(0, 0, 0, 10) };
    readonly StormGlass _fightPanel;
    readonly TacSketch _bolts;
    readonly System.Windows.Shapes.Rectangle _fightFlash;
    readonly StackPanel _fightBody = new() { Margin = new Thickness(Pad, 9 + FightCrest, Pad, 8) };
    readonly Border _timersInFight = new() { Margin = new Thickness(0, 0, 0, 8), HorizontalAlignment = HorizontalAlignment.Left };
    readonly Grid _band = new() { IsHitTestVisible = false, VerticalAlignment = VerticalAlignment.Top };
    readonly TranslateTransform _fightShift = new();
    FrameworkElement? _headEl;
    double _clapT = -1, _shimT = -1;
    string _leadFight = "";
    long _leadId;

    public HudStorm() : base(bare: true)
    {
        // the look's own rows (Ui/Themes/ThemeRows.cs): each a charged conduit
        UseRows(StormRow.Pitch, m => new StormRow(), 14, GapLine);
        _linePanel = new StormGlass(() => _dim ? _opacity * .7 : _opacity, 10, 3, crest: LineCrest);
        _zapEl = new TacSketch(DrawZap, 56, 7) { RenderTransform = _zapAt, Visibility = Visibility.Collapsed };
        Canvas.SetTop(_zapEl, LineCrest - 1);
        _zapHost.Children.Add(_zapEl);
        _line.Children.Add(_linePanel); _line.Children.Add(_zapHost); _line.Children.Add(_lineBody); _line.Children.Add(_lineAnchor);
        _fightPanel = new StormGlass(() => _opacity, 11, 3, crest: FightCrest);
        _fightFlash = Flash(_fight, 11, 3, FightCrest);
        _bolts = new TacSketch(DrawBolts) { ClipToBounds = true };
        _fight.Children.Add(_fightPanel); _fight.Children.Add(_fightFlash); _fight.Children.Add(_fightBody); _fight.Children.Add(_band); _fight.Children.Add(_bolts);
        _fight.RenderTransform = _fightShift;
        _note.Child = _noteBody;
        _runHeadLit = Lit(_runHead); _runHeadLit.Visibility = Visibility.Collapsed;
        _body.Children.Add(_fight); _body.Children.Add(_timersUp); _body.Children.Add(_runHeadLit); _body.Children.Add(_line); _body.Children.Add(_rare); _body.Children.Add(_note);
        _root.Children.Add(_body);
        System.Windows.Documents.TextElement.SetFontFamily(_root, Mod.Face);
        TextOptions.SetTextFormattingMode(_root, TextFormattingMode.Display);
    }

    public override FrameworkElement Root => _root;
    public override Thickness Inset => _root.Margin;
    public override double Corner => 0;
    public override FrameworkElement? FightFrame => _fight.Visibility == Visibility.Visible ? _fight : null;

    public override void SetOpacity(double opacity)
    {
        _opacity = Math.Clamp(opacity, 0.3, 1);
        _linePanel.InvalidateVisual(); _fightPanel.InvalidateVisual();
        if (_rareGlass != null) _rareGlass.InvalidateVisual();
    }

    protected override string LineSig(Figures f) => base.LineSig(f) + "|" + (Rare(Values)?.Id ?? "") + "|" + (Picks(Values).FirstOrDefault()?.Id ?? "") + "|" + Dimmed(Values) + "|" + PickStrip.Sig(Values, Strip) + "|" + RunBuddy.Sig(Values);

    static bool Dimmed(IReadOnlyDictionary<string, string> v) => (v.TryGetValue("show", out var s) && s == "dim") || (v.TryGetValue("paused", out var p) && p.Length > 0);

    // ================================================================ the shade and the marks

    /// <summary>The tight shade under words: 1 px down, soft, near black.</summary>
    static DropShadowEffect Shade() => new() { Color = Colors.Black, Direction = 270, ShadowDepth = 1, BlurRadius = 3, Opacity = .9 };

    /// <summary>Words straight on the game (the run's head over the Buddy): the tight shade and a wide soft halo round them.</summary>
    static Border Lit(FrameworkElement el)
    {
        el.Effect = Shade();
        return new Border { Child = el, Effect = new DropShadowEffect { Color = Colors.Black, ShadowDepth = 0, BlurRadius = 10, Opacity = .65 }, HorizontalAlignment = el.HorizontalAlignment, VerticalAlignment = el.VerticalAlignment };
    }

    /// <summary>A small night-glass mark with a charged hairline (TOP DPS, the heal mark, AVG): <paramref name="filled"/> electric white with dark words (YOU).</summary>
    static Border Mark(string s, Brush? fg = null, Color? edge = null, bool filled = false, double size = 8.5)
    {
        var t = Tt(s, size, filled ? Frozen(Deep0C) : fg ?? Arc, FontWeights.Bold);
        return new Border
        {
            Child = t, CornerRadius = new CornerRadius(2), Padding = new Thickness(4, 0, 4, 1), VerticalAlignment = VerticalAlignment.Center,
            Background = filled ? Arc : Frozen(Mod.A(Deep0C, .78)), BorderThickness = new Thickness(1),
            BorderBrush = filled ? Frozen(VoltC) : Frozen(Mod.A(edge ?? VoltC, edge == null ? .45 : .6)),
        };
    }

    /// <summary>A charged hairline across (the foot's, the card's): blue, brightest a third of the way in.</summary>
    static FrameworkElement Hairline(Thickness m, double a = .5) => new TacSketch((dc, s) =>
        dc.DrawRectangle(Mod.Grad(false, (Mod.A(VoltC, a * .2), 0), (Mod.A(VoltC, a), .3), (Mod.A(VioletC, a * .5), .75), (Mod.A(VioletC, 0), 1)), null, new Rect(0, 0, s.Width, 1)))
        { Height = 1, Margin = m };

    // ================================================================ the Buddy

    protected override void DrawLine(Figures f)
    {
        _lineBody.Children.Clear();
        _coil = null; _timerUp = false; _stripTimerW = -1;
        if (_timersInLine.Child != null) _timersInLine.Child = null;
        _dim = Dimmed(Values);
        _line.Opacity = _dim ? .62 : 1;
        bool mark = f.Abyss && f.Counted && f.Icon.Length > 0;
        if (mark) { var m = AbyssMark(f.Icon, 36); m.Margin = new Thickness(0, 0, 10, 0); _lineBody.Children.Add(m); }
        // in a dungeon run: the run's clock, splits and verdict in place of the hour (HudStorm.Run.cs)
        var rs = RunNow();
        _runHeadLit.Visibility = Visibility.Collapsed;
        if (rs != null) DrawRun(rs);
        else if (!HasHour(f))
        {
            var w = Tt(Waiting(f.Abyss), 12.5, Soft, FontWeights.SemiBold); w.Margin = new Thickness(mark ? 0 : 4, 5, 0, 5);
            _lineBody.Children.Add(w);
        }
        else
        {
            if (HasRing(f))
            {
                _share = f.Share; _beat = f.Beat; _complete = 0;
                var ring = new Grid { Width = 44, Height = 44, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0) };
                _coil = new TacSketch(DrawCoil, 44, 44);
                ring.Children.Add(_coil);
                var pc = Tt(f.Beat ? "★" : Pct(f.Share), 13, Arc, FontWeights.Bold); pc.HorizontalAlignment = HorizontalAlignment.Center;
                ring.Children.Add(pc);
                _lineBody.Children.Add(ring);
            }
            else if (!mark) _lineBody.Children.Add(new Border { Width = 4 });
            // the hour big in electric white (a blue glow round it), its unit small; the session and the latest pickup under it
            var words = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 0, 1) };
            var top = new StackPanel { Orientation = Orientation.Horizontal };
            var rate = Tt(f.Rate, f.Warm ? 15 : 22, f.Warm ? Soft : Arc, f.Warm ? FontWeights.SemiBold : FontWeights.Bold);
            if (!f.Warm) rate.Effect = new DropShadowEffect { Color = VoltC, ShadowDepth = 0, BlurRadius = 9, Opacity = .55 };
            rate.Tag = "buddy-rate";
            top.Children.Add(rate);
            if (f.Unit.Length > 0) { var u = Tt(f.Unit, 11, Frozen(Mod.A(VoltC, .85)), FontWeights.SemiBold); u.VerticalAlignment = VerticalAlignment.Bottom; u.Margin = new Thickness(2, 0, 0, 4); top.Children.Add(u); }
            words.Children.Add(top);
            // farming, the pickups as the strip beside the hour where it fits (the best drop and the last three), the session
            // alone under it; with the timer chips at the line's end and no room left beside them, the chips go over the line
            var sub = Sub(f, true);
            FrameworkElement? strip = null, under = null;
            if (StripTime(f))
            {
                var bare = Sub(f, false);
                double baseW = _lineBody.Margin.Left + _lineBody.Margin.Right + _lineBody.Children.OfType<FrameworkElement>().Sum(e => Mod.W(e) + e.Margin.Left + e.Margin.Right), timerW = LineTimerW();
                double wordsW = Math.Max(Mod.W(top), Mod.W(bare)), oldW = Math.Max(Mod.W(top), Mod.W(sub));
                foreach (var look in StripFall(Strip))
                {
                    if ((strip = StripIn(StripRoom(FightWidth - 10, baseW + timerW, wordsW, oldW, 14), look)) != null) break;
                    if (timerW > 0 && (strip = StripIn(StripRoom(FightWidth - 10, baseW, wordsW, oldW + timerW, 14), look)) != null) { _timerUp = true; break; }
                }
                if (strip != null) { sub = bare; strip.Margin = new Thickness(14, 0, 0, 0); ArcRim(strip); }
                // no room beside the hour: under the session, never dropped (Steven, 2026-11-19)
                else if ((under = StripUnder(Strip, false)) != null) { sub = bare; ArcRim(under); }
                _stripTimerW = timerW;
            }
            if (sub.Inlines.Count > 0) words.Children.Add(sub);
            if (under != null) words.Children.Add(under);
            _lineBody.Children.Add(words);
            if (strip != null) _lineBody.Children.Add(strip);
        }
        _lineBody.Children.Add(_timersInLine);
        PlaceTimers();
        _linePanel.InvalidateVisual();

        DrawRare();

        // the notice: its words in their tone on a night-glass chip under the Buddy, the warning sign before a pause
        _noteBody.Children.Clear();
        if (f.Note.Length > 0 && !KinahHidden)
        {
            bool warn = ReferenceEquals(f.NoteTone, Warn);
            if (warn) { var wi = WarnIcon(); wi.Width = wi.Height = 13; wi.Margin = new Thickness(0, 0, 6, 0); _noteBody.Children.Add(wi); }
            var tone = warn ? WarnC2 : ReferenceEquals(f.NoteTone, Gold) ? GoldC2 : ReferenceEquals(f.NoteTone, Mint) ? MintC2 : ArcC;
            var t = Tt(f.Note, 11.5, Frozen(tone), FontWeights.SemiBold); t.TextTrimming = TextTrimming.CharacterEllipsis; t.MaxWidth = 380;
            _noteBody.Children.Add(t);
            _note.Visibility = Visibility.Visible;
        }
        else _note.Visibility = Visibility.Collapsed;
    }

    /// <summary>The line under the hour: the session, the latest pickup and its worth (<paramref name="pick"/>); in the Abyss its stats.</summary>
    TextBlock Sub(Figures f, bool pick)
    {
        var sub = new TextBlock { FontFamily = Mod.Face, FontSize = 10.5, FontWeight = FontWeights.Medium, Foreground = Frozen(Mod.A(Colors.White, .8)), Margin = new Thickness(1, 1, 0, 0) };
        System.Windows.Documents.Typography.SetNumeralAlignment(sub, FontNumeralAlignment.Tabular);
        void Dot() { if (sub.Inlines.Count > 0) sub.Inlines.Add(new System.Windows.Documents.Run("  ·  ") { Foreground = Frozen(Mod.A(VoltC, .6)) }); }
        if (f.Total.Length > 0) { sub.Inlines.Add(new System.Windows.Documents.Run(f.Total) { FontWeight = FontWeights.SemiBold, Foreground = White }); sub.Inlines.Add(new System.Windows.Documents.Run(" " + f.TotalWord)); }
        foreach (var (fig, word, tone) in f.Stats)
        {
            Dot();
            var c = ReferenceEquals(tone, Rec) ? CoralC : ReferenceEquals(tone, Gold) ? GoldC2 : Colors.White;
            sub.Inlines.Add(new System.Windows.Documents.Run(fig) { FontWeight = FontWeights.SemiBold, Foreground = Frozen(c) });
            sub.Inlines.Add(new System.Windows.Documents.Run(" " + word));
        }
        if (pick && !f.Abyss && Picks(Values).FirstOrDefault() is { } p)
        {
            Dot();
            sub.Inlines.Add(new System.Windows.Documents.Run(p.Name.Length > 22 ? p.Name[..21].TrimEnd() + "…" : p.Name));
            var worth = p.Worth.Length > 0 ? "+" + p.Worth.TrimStart('+') : p.Count;
            if (worth.Length > 0) sub.Inlines.Add(new System.Windows.Documents.Run(" " + worth) { FontWeight = FontWeights.Bold, Foreground = p.Worth.Length > 0 ? Mint2 : Soft });
        }
        return sub;
    }

    /// <summary>The timer chips' room at the line's end, 0 with none.</summary>
    double LineTimerW() => _timers.Children.Count > 0 && !KinahHidden ? 12 + Mod.W(_timers) : 0;

    /// <summary>The timers' room the line's strip was fitted to (-1: no strip time); chips that change it draw the line again.</summary>
    double _stripTimerW = -1;

    /// <summary>The timer chips sit over the line, to leave its room to the pickup strip.</summary>
    bool _timerUp;

    /// <summary>The pickup's crackle, drawn once: a short live bolt along the top edge, brightest at its front.</summary>
    static void DrawZap(DrawingContext dc, Size s)
    {
        var pts = BoltPts(new Point(2, 3.5), new Point(s.Width - 2, 3.5), 41, 2.4, 4);
        DrawBolt(dc, pts, .6, .9);
        DrawBolt(dc, pts.Skip(pts.Count / 2).ToList(), 1, 1.15);
        var g = new RadialGradientBrush(Mod.A(ArcC, .9), Mod.A(VoltC, 0)); g.Freeze();
        dc.DrawEllipse(g, null, new Point(s.Width - 3, 3.5), 6, 3.5);
    }

    /// <summary>The crackle at <paramref name="t"/> (0 to 1, -1 off) along the Buddy's top edge, in <paramref name="tint"/> opacity.</summary>
    void Zap(double t, double alpha = 1)
    {
        bool on = t is > 0 and < 1 && _line.ActualWidth > 40;
        _zapEl.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
        if (!on) return;
        _zapAt.X = -56 + (_line.ActualWidth + 50) * t;
        _zapEl.Opacity = alpha * (1 - t * .5);
    }

    /// <summary>
    /// The run's ring as a charged capacitor: six cut cells round a dark core, charged blue-white up to your best's share (the
    /// last one part-charged), a hot spark at the charge's front; past your best, every cell white-hot and a blue halo round
    /// it. <see cref="_head"/>: the spark running round ahead of the charge (your best passed).
    /// </summary>
    void DrawCoil(DrawingContext dc, Size s)
    {
        double c = s.Width / 2, r = c - 5;
        var center = new Point(c, c);
        dc.DrawEllipse(Frozen(Mod.A(Deep0C, .78)), Mod.P(Mod.Br(VoltC, .16), 1), center, r - 4, r - 4);
        double share = Math.Clamp(_beat ? 1 : _share, 0, 1);
        double shown = share + (1 - share) * Math.Clamp(_complete, 0, 1);
        bool full = _beat || _complete >= 1 || shown >= .999;
        if (full)
        {
            var halo = new RadialGradientBrush { GradientOrigin = new Point(.5, .5), Center = new Point(.5, .5), RadiusX = .5, RadiusY = .5 };
            halo.GradientStops.Add(new GradientStop(Mod.A(VoltC, 0), .6)); halo.GradientStops.Add(new GradientStop(Mod.A(VoltC, .55), .8)); halo.GradientStops.Add(new GradientStop(Mod.A(VoltC, 0), 1));
            halo.Freeze();
            dc.DrawEllipse(halo, null, center, c, c);
        }
        PathGeometry Arc(double from, double to)
        {
            double a0 = 2 * Math.PI * from, a1 = 2 * Math.PI * to;
            var fig = new PathFigure { StartPoint = new Point(c + r * Math.Sin(a0), c - r * Math.Cos(a0)) };
            fig.Segments.Add(new ArcSegment(new Point(c + r * Math.Sin(a1), c - r * Math.Cos(a1)), new Size(r, r), 0, a1 - a0 > Math.PI, SweepDirection.Clockwise, true));
            var pg = new PathGeometry(); pg.Figures.Add(fig); pg.Freeze();
            return pg;
        }
        // the six cells: a gap between each, empty ones a dim track, charged ones glowing with a white-hot core
        const int n = 6;
        const double gap = .035;
        var track = new Pen(Mod.Br(VoltC, .22), 4.5) { StartLineCap = PenLineCap.Flat, EndLineCap = PenLineCap.Flat }; track.Freeze();
        var glow = new Pen(Mod.Br(VoltC, full ? .6 : .45), 6.5) { StartLineCap = PenLineCap.Flat, EndLineCap = PenLineCap.Flat }; glow.Freeze();
        var core = new Pen(full ? Arc0 : Mod.Br(Mod.Mix(VoltC, Colors.White, .55), 1), 3) { StartLineCap = PenLineCap.Flat, EndLineCap = PenLineCap.Flat }; core.Freeze();
        for (int i = 0; i < n; i++)
        {
            double f0 = (double)i / n + gap / 2, f1 = (double)(i + 1) / n - gap / 2;
            dc.DrawGeometry(null, track, Arc(f0, f1));
            double lit = Math.Min(f1, shown);
            if (lit > f0 + .004) { var g = Arc(f0, lit); dc.DrawGeometry(null, glow, g); dc.DrawGeometry(null, core, g); }
        }
        if (shown is > .01 and < .999)
        {
            // the charge's front: a hot spark (brighter while the charge runs round)
            double ah = 2 * Math.PI * shown;
            var p = new Point(c + r * Math.Sin(ah), c - r * Math.Cos(ah));
            var g = new RadialGradientBrush(Mod.A(ArcC, _head is > 0 and < 1 ? 1 : .75), Mod.A(VoltC, 0)); g.Freeze();
            dc.DrawEllipse(g, null, p, _head is > 0 and < 1 ? 7 : 5, _head is > 0 and < 1 ? 7 : 5);
            dc.DrawEllipse(Arc0, null, p, 1.6, 1.6);
        }
    }

    static readonly SolidColorBrush Arc0 = Frozen(ArcC);

    // ---- the rare drop

    double _rareBurst = -1;
    StormGlass? _rareGlass;
    TacSketch? _rareSparks;
    System.Windows.Shapes.Rectangle? _rareFlash;
    /// <summary>The rare strip's bloom: its glass and charge grow out from the left by their scale (the words fade in over them).</summary>
    readonly ScaleTransform _rareBloom = new(1, 1);
    FrameworkElement? _rareRow;
    FrameworkElement? _rareTile;

    /// <summary>The rare drop the window holds: a strip of storm glass charged in its grade's colour, its tile, RARE DROP and its name.</summary>
    void DrawRare()
    {
        _rare.Children.Clear();
        _rareBloom.ScaleX = 1;
        if (!KinahHidden && Rare(Values) is { } r)
        {
            var c = ColorOf(r.Color, r.Grade);
            _rareGlass = new StormGlass(() => _opacity, 8, 2) { RenderTransform = _rareBloom };
            _rare.Children.Add(_rareGlass);
            // the grade's charge: its colour from the left, a hot line down the left edge (drawn once)
            _rare.Children.Add(new TacSketch((dc, s) =>
            {
                var shape = Cut(new Rect(.5, .5, Math.Max(1, s.Width - 1), Math.Max(1, s.Height - 1)), 8, 2);
                dc.PushClip(shape);
                dc.DrawRectangle(Mod.Grad(false, (Mod.A(c, .42), 0), (Mod.A(c, .12), .55), (Mod.A(c, 0), 1)), null, new Rect(0, 0, s.Width, s.Height));
                dc.DrawRectangle(Mod.Br(Mod.Mix(c, Colors.White, .5), 1), null, new Rect(0, 0, 2.5, s.Height));
                dc.Pop();
                dc.DrawGeometry(null, Mod.P(Mod.Br(Mod.Mix(c, VioletC, .45), .85), 1.4), shape);
            }) { RenderTransform = _rareBloom });
            _rareFlash = Flash(_rare, 8, 2);
            _rareFlash.Fill = Mod.Grad(false, (Mod.A(Mod.Mix(c, Colors.White, .4), .5), 0), (Mod.A(c, .12), 1));
            _rare.Children.Add(_rareFlash);
            // the sparks bursting off the tile (only while the moment plays)
            _rareSparks = new TacSketch((dc, s) =>
            {
                if (_rareBurst is <= 0 or >= 1 || _rareTile is not { } tile || !tile.IsDescendantOf(_rare)) return;
                var tl = tile.TranslatePoint(new Point(0, 0), _rare);
                var o = new Point(tl.X + tile.ActualWidth / 2, tl.Y + tile.ActualHeight / 2);
                double k = Out(_rareBurst), a = 1 - _rareBurst;
                for (int i = 0; i < 7; i++)
                {
                    double ang = -Math.PI / 2 + i * 2 * Math.PI / 7 + .3;
                    double r0 = 12, r1 = 12 + 22 * k;
                    var p0 = new Point(o.X + r0 * Math.Cos(ang), Math.Clamp(o.Y + r0 * Math.Sin(ang), 1, s.Height - 1));
                    var p1 = new Point(Math.Max(1, o.X + r1 * Math.Cos(ang)), Math.Clamp(o.Y + r1 * Math.Sin(ang) * .8, 1, s.Height - 1));
                    DrawBolt(dc, BoltPts(p0, p1, 71 + i * 13, 2.6, 3), a, .85, Mod.Mix(c, VoltC, .4));
                }
            });
            var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(9, 5, 22, 5) };
            var tile = Tile(r, 26); tile.Margin = new Thickness(0, 0, 9, 0);
            _rareTile = tile;
            row.Children.Add(tile);
            var mk = Mark(L.T("RARE DROP"), Frozen(Mod.Mix(GoldC2, Colors.White, .25)), GoldC2, size: 9); mk.Margin = new Thickness(0, 0, 8, 0);
            row.Children.Add(mk);
            var nm = Tt(r.Name, 13, Frozen(Mod.Mix(c, Colors.White, .5)), FontWeights.Bold);
            nm.Effect = Shade();
            row.Children.Add(nm);
            if (r.Count.Length > 0) { var cn = Tt(" " + r.Count, 11, Soft, FontWeights.SemiBold); cn.Margin = new Thickness(2, 1, 0, 0); row.Children.Add(cn); }
            _rareRow = row;
            _rare.Children.Add(row);
            _rare.Children.Add(_rareSparks);
            _rare.Visibility = Visibility.Visible;
        }
        else { _rare.Visibility = Visibility.Collapsed; _rareGlass = null; _rareSparks = null; _rareFlash = null; _rareTile = null; }
    }

    protected override void KinahShown(bool shown)
    {
        _line.Visibility = shown ? Visibility.Visible : Visibility.Collapsed;
        if (!shown) { _note.Visibility = Visibility.Collapsed; _rare.Visibility = Visibility.Collapsed; }
        _fight.Margin = new Thickness(0, 0, 0, shown ? 10 : 0);
        PlaceTimers();
    }

    // ================================================================ the timers

    protected override void DrawTimers(IReadOnlyList<TimerBoard.Item> due, DateTime now)
    {
        _timers.Children.Clear();
        _firstChip = null;
        foreach (var i in due.Take(2))
        {
            var (text, tone) = TimerWords(i, now);
            bool first = _timers.Children.Count == 0;
            var c = tone == TimerBadge.Tone.On ? MintC2 : tone == TimerBadge.Tone.Soon ? GoldC2 : VoltC;
            var sp = new StackPanel { Orientation = Orientation.Horizontal };
            sp.Children.Add(new System.Windows.Shapes.Ellipse { Width = first ? 6.5 : 5, Height = first ? 6.5 : 5, Fill = Frozen(c), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) });
            var t = Tt(text, first ? 11.5 : 10, first ? Arc : Soft, first ? FontWeights.Bold : FontWeights.SemiBold);
            if (tone == TimerBadge.Tone.On) t.Foreground = Frozen(Mod.Mix(MintC2, Colors.White, .45));
            else if (tone == TimerBadge.Tone.Soon) t.Foreground = Frozen(Mod.Mix(GoldC2, Colors.White, .35));
            sp.Children.Add(t);
            var chip = new Border
            {
                Child = sp, CornerRadius = new CornerRadius(3), Padding = first ? new Thickness(8, 3, 10, 4) : new Thickness(7, 2, 9, 3), VerticalAlignment = VerticalAlignment.Center,
                Background = Frozen(Mod.A(Deep0C, .9)), BorderThickness = new Thickness(1), BorderBrush = Frozen(Mod.A(c, tone == TimerBadge.Tone.Quiet ? .3 : .55)),
                Margin = new Thickness(first ? 0 : 5, 0, 0, 0), Tag = first ? "timer-chip" : null,
            };
            if (first) _firstChip = chip;
            _timers.Children.Add(chip);
        }
        // the line's pickup strip was fitted beside the timer chips: chips that come, go or change their width fit it again
        if (_stripTimerW >= 0 && Figs is { } f && Math.Abs(LineTimerW() - _stripTimerW) > .5) DrawLine(f);
        PlaceTimers();
    }

    void PlaceTimers()
    {
        bool inside = KinahHidden && _fight.Visibility == Visibility.Visible, up = !inside && _timerUp;
        if (inside)
        {
            if (_timersInLine.Child == _timers) _timersInLine.Child = null;
            if (_timersUp.Child == _timers) _timersUp.Child = null;
            if (_timersInFight.Child != _timers) _timersInFight.Child = _timers;
        }
        else if (up)
        {
            if (_timersInLine.Child == _timers) _timersInLine.Child = null;
            if (_timersInFight.Child == _timers) _timersInFight.Child = null;
            if (_timersUp.Child != _timers) _timersUp.Child = _timers;
        }
        else
        {
            if (_timersInFight.Child == _timers) _timersInFight.Child = null;
            if (_timersUp.Child == _timers) _timersUp.Child = null;
            if (_timersInLine.Child != _timers && _timersInLine.Parent != null) _timersInLine.Child = _timers;
        }
        bool any = _timers.Children.Count > 0;
        _timersInFight.Visibility = inside && any ? Visibility.Visible : Visibility.Collapsed;
        _timersUp.Visibility = up && any ? Visibility.Visible : Visibility.Collapsed;
        _timersInLine.Visibility = !inside && !up && any ? Visibility.Visible : Visibility.Collapsed;
    }

    // ================================================================ the fight

    protected override void DrawFight(Encounter? e, DateTime now)
    {
        _fightBody.Children.Clear();
        if (e == null) { _fight.Visibility = Visibility.Collapsed; _band.Children.Clear(); PlaceTimers(); return; }
        double w = FightWidth - 10, inner = w - 2 * Pad;
        _fight.Width = w;
        _fightBody.Children.Add(_timersInFight);
        // the head: the boss, the outcome once it's over, the clock
        var head = new Grid();
        head.ColumnDefinitions.Add(new ColumnDefinition()); head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var title = Tt(e.Title, 14, White, FontWeights.SemiBold); title.TextTrimming = TextTrimming.CharacterEllipsis;
        head.Children.Add(title);
        var right = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(8, 0, 0, 0) };
        if (e.Waiting) right.Children.Add(Mark(L.T("READY"), MarkMint, MarkMintC, size: 9.5));
        else
        {
            var outcome = OutcomeText(e);
            if (outcome.Length > 0)
            {
                bool wipe = e.Outcome == "wipe";
                var oc = Mark(outcome, wipe ? Frozen(Color.FromRgb(0xFF, 0x9A, 0x9A)) : MarkMint, wipe ? CoralC : MarkMintC, size: 9.5);
                oc.Margin = new Thickness(0, 1, 8, 0); oc.Tag = "outcome";
                right.Children.Add(oc);
            }
            var clock = Tt(Clock(e, now), 17, Arc, FontWeights.Bold); clock.Tag = "fight-clock";
            right.Children.Add(clock);
        }
        Grid.SetColumn(right, 1); head.Children.Add(right);
        head.Effect = Shade();
        _headEl = head;
        _fightBody.Children.Add(head);
        if (e.Waiting)
        {
            var wt = Tt(e.ReadyText, 12.5, Soft, FontWeights.SemiBold); wt.Margin = new Thickness(0, 5, 0, 1); wt.TextTrimming = TextTrimming.CharacterEllipsis;
            _fightBody.Children.Add(wt);
            if (PartyBeforeHit(e)) { _fightBody.Children.Add(new Border { Height = 7 }); PutRows(_fightBody, e, now, inner); }
        }
        else
        {
            if (HpOf(e) is { } hp)
            {
                // the boss's HP and its words on one line: a charged bar with ticks at the thirds, "47% · 1.84M left" at its right
                var hpLine = new Grid { Margin = new Thickness(0, 7, 0, 0) };
                hpLine.ColumnDefinitions.Add(new ColumnDefinition()); hpLine.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                var track = new Grid { Height = 6, VerticalAlignment = VerticalAlignment.Center };
                track.Children.Add(new TacSketch((dc, s) =>
                {
                    var r = new Rect(0, 0, s.Width, s.Height);
                    var shape = Cut(r, 3, 1);
                    dc.DrawGeometry(Mod.Br(Deep0C, .85), Mod.P(Mod.Br(VoltC, .18), 1), shape);
                    dc.PushClip(shape);
                    double x1 = s.Width * Math.Clamp(hp, 0, 1);
                    if (x1 > .5)
                    {
                        dc.DrawRectangle(Mod.Grad(false, (HpAC, 0), (HpBC, 1)), null, new Rect(0, 0, x1, s.Height));
                        dc.DrawRectangle(Mod.Br(Colors.White, .45), null, new Rect(0, 0, x1, 1));
                        dc.DrawRectangle(Mod.Br(Mod.Mix(HpBC, Colors.White, .6), 1), null, new Rect(Math.Max(0, x1 - 1.5), 0, Math.Min(1.5, x1), s.Height));
                    }
                    for (int k = 1; k <= 2; k++) { double x = Math.Round(s.Width * k / 3); dc.DrawRectangle(Mod.Br(Deep0C, .8), null, new Rect(x - 1, 0, 2, s.Height)); }
                    dc.Pop();
                }));
                HpGhost(track, e, hp, now, 1.5);
                hpLine.Children.Add(track);
                var hl = new TextBlock { FontFamily = Mod.Face, FontSize = 10.5, Foreground = Soft, Margin = new Thickness(10, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
                System.Windows.Documents.Typography.SetNumeralAlignment(hl, FontNumeralAlignment.Tabular);
                hl.Inlines.Add(new System.Windows.Documents.Run($"{Math.Round(hp * 100):0}%") { FontWeight = FontWeights.Bold, Foreground = hp < .34 ? Frozen(Color.FromRgb(0xFF, 0x9A, 0xB4)) : Arc });
                var left = HpLeftText(e);
                if (left.Length > 0) hl.Inlines.Add(new System.Windows.Documents.Run("  ·  " + left) { FontWeight = FontWeights.Medium });
                hl.Tag = "hp-text";
                Grid.SetColumn(hl, 1); hpLine.Children.Add(hl);
                _fightBody.Children.Add(hpLine);
                _fightBody.Children.Add(new Border { Height = 8 });
            }
            else _fightBody.Children.Add(new Border { Height = 8 });
            PutRows(_fightBody, e, now, inner);
            if (e.HasDamage)
            {
                _fightBody.Children.Add(Hairline(new Thickness(0, 7, 0, 5)));
                _fightBody.Children.Add(Foot(e, now));
            }
        }
        _fight.Visibility = Visibility.Visible;
        PlaceTimers();
        // a new leader in a fight under way: a thunderclap on their rank and arcs down the bars
        long lead = e.Live && e.HasDamage && Meter?.Members.FirstOrDefault(m => m.Rank == 1 && !m.CpOnly) is { } top ? top.Id : 0;
        if (lead != 0 && e.Id == _leadFight && _leadId != 0 && lead != _leadId && Moves) Storm(true, true, 1500);
        if (e.Id != _leadFight || lead != 0) { _leadFight = e.Id; _leadId = lead; }
    }

    // ================================================================ the lightning over the fight

    /// <summary>The rows as drawn now, top first, with their place in the fight's panel.</summary>
    List<(StormRow Row, Rect At)> RowsAt()
    {
        var list = new List<(StormRow, Rect)>();
        if (Meter is not { } meter || !meter.IsDescendantOf(_fight)) return list;
        foreach (var m in meter.Members)
            if (meter.RowOf(m.Id) is StormRow r && r.Root.ActualWidth > 0 && r.Root.Visibility == Visibility.Visible)
            {
                var tl = r.Root.TranslatePoint(new Point(0, 0), _fight);
                list.Add((r, new Rect(tl, new Size(r.Root.ActualWidth, r.Root.ActualHeight))));
            }
        list.Sort((a, b) => a.Item2.Y.CompareTo(b.Item2.Y));
        return list;
    }

    /// <summary>The shimmer's beat down the rows: each row's charge starts this much after the one above.</summary>
    static double ShimStep(int n) => Math.Min(.16, .62 / Math.Max(1, n));

    /// <summary>
    /// The lightning over the fight: the thunderclap (<see cref="_clapT"/>: a bolt forking down from the panel's top edge onto the
    /// leader's rank, struck twice, a white glow where it lands) and the shimmer's arcs (<see cref="_shimT"/>: when a bar's charge
    /// reaches its edge, a spark leaps from that edge to the next bar's, down the rows, in the gaps between them).
    /// </summary>
    void DrawBolts(DrawingContext dc, Size s)
    {
        bool clap = _clapT is >= 0 and < 1, shim = _shimT is >= 0 and < 1;
        if (!clap && !shim) return;
        var rows = RowsAt();
        if (rows.Count == 0) return;
        var lead = rows.FirstOrDefault(x => x.Row.Member is { Rank: 1 });
        if (clap && lead.Row != null)
        {
            double t = _clapT, reach = Out(Seg(t, 0, .06));
            double a = ClapLight(t);
            var land = new Point(lead.At.X + 2, lead.At.Y + lead.At.Height / 2);   // at the slot's edge, short of the rank's digit
            if (a > .01)
            {
                // the strike: out of the crest's first head, across to the glass's left gutter, down it (where no word sits), into the leader's rank
                double gx = 5;
                var head = new Point(Pad + 22, FightCrest * .55);
                var down = BoltPts(head, new Point(gx, FightCrest + 10), 907, 4, 3);
                down.AddRange(BoltPts(new Point(gx, FightCrest + 10), new Point(gx, land.Y), 911, 3, 5).Skip(1).Select(q => new Point(Math.Clamp(q.X, 2, 8), q.Y)));
                down.AddRange(BoltPts(new Point(gx, land.Y), land, 913, 1.2, 2).Skip(1));
                // a wide halo of the strike's light first, then the bolt itself
                dc.DrawGeometry(null, Mod.P(Mod.Br(VoltC, .16 * a), 20, round: true), BoltLine(down, Math.Max(2, (int)Math.Ceiling(down.Count * reach))));
                DrawBolt(dc, down, a, 2.8, VoltC, reach);
                if (reach >= 1)
                {
                    // forks off the stroke, a third and two thirds down, and one running back up into the cloud
                    // forks: one back up into the cloud, one along the crest's base, one off the gutter
                    DrawBolt(dc, BoltPts(head, new Point(Pad + 80, 5), 929, 3, 4), a * .8, .9);
                    DrawBolt(dc, BoltPts(head, new Point(Pad + 60, FightCrest + 3), 931, 2.5, 3), a * .6, .7);
                    var f1 = down[down.Count * 2 / 3];
                    DrawBolt(dc, BoltPts(f1, new Point(2.5, Math.Min(land.Y - 3, f1.Y + 10)), 923, 1.5, 3), a * .6, .7);
                    var g = new RadialGradientBrush(Mod.A(ArcC, a), Mod.A(VoltC, 0)); g.Freeze();
                    dc.DrawEllipse(g, null, land, 34, 17);
                }
            }
            // the shockwave: a ring of light thrown out from where it struck, widening and fading
            double k = Seg(t, .06, .5);
            if (k is > 0 and < 1)
            {
                // centred on the lead's glyph, kept small so it never sits on the rank's digit
                var at = new Point(lead.At.X + 20, lead.At.Y + lead.At.Height / 2);
                double rx = 4 + 8 * Out(k), ry = 3 + 6 * Out(k);
                dc.DrawEllipse(null, Mod.P(Mod.Br(VoltC, .5 * (1 - k)), 5 * (1 - k) + 1), at, rx, ry);
                dc.DrawEllipse(null, Mod.P(Mod.Br(ArcC, .9 * (1 - k)), 1.2), at, rx, ry);
            }
        }
        if (shim)
        {
            double step = ShimStep(rows.Count);
            for (int i = 0; i + 1 < rows.Count; i++)
            {
                var (ra, at) = rows[i];
                var (rb, bt) = rows[i + 1];
                if (bt.Y - at.Bottom > 6) continue;   // you pinned below the gap: no arc across it
                double k = Seg(_shimT, .06 + step * i + .17, .06 + step * i + .32);
                if (k is <= 0 or >= 1) continue;
                double gy = (at.Bottom + bt.Y) / 2, alpha = Math.Sin(Math.PI * k);
                static double gyOf(Rect a, Rect b) => (a.Bottom + b.Y) / 2;
                // from edge to edge, but never past the figure column (the arc stays clear of every figure)
                bool qa = ra.EdgeQuiet, qb = rb.EdgeQuiet;
                var p0 = new Point(at.X + Math.Clamp(ra.EdgeX - 1, 4, ra.FigureX), qa ? gyOf(at, bt) : at.Bottom - 2);
                var p1 = new Point(bt.X + Math.Clamp(rb.EdgeX - 1, 4, rb.FigureX), qb ? gyOf(at, bt) : bt.Y + 2);
                var pts = new List<Point> { p0 };
                pts.AddRange(BoltPts(new Point(p0.X, gy), new Point(Math.Abs(p1.X - p0.X) < 3 ? p0.X + 6 : p1.X, gy), 501 + i * 37, 1.8, 4));
                pts.Add(p1);
                DrawBolt(dc, pts, alpha, 1.35);
                if (!qb) { var g = new RadialGradientBrush(Mod.A(ArcC, .8 * alpha), Mod.A(VoltC, 0)); g.Freeze(); dc.DrawEllipse(g, null, p1, 9, 4); }
            }
        }
    }

    /// <summary>The thunderclap's light over its course: struck and held, a flicker, struck again, then gone.</summary>
    static double ClapLight(double t) => t < .06 ? Seg(t, 0, .06) : t < .2 ? 1 : t < .24 ? 1 - .75 * Seg(t, .2, .24) : t < .28 ? .25 + .75 * Seg(t, .24, .28) : 1 - Seg(t, .3, .55);

    /// <summary>
    /// One storm course on the fight: the thunderclap on the leader (<paramref name="clap"/>: the strike, the clouds and the
    /// glass lit white, the row's flash, the glyph landing, the shockwave), the charges and arcs down the bars
    /// (<paramref name="shimmer"/>), and for a kill the storm breaking over the panel (<paramref name="flicker"/>: its light twice).
    /// </summary>
    void Storm(bool clap, bool shimmer, double ms, bool flicker = false)
    {
        if (_fight.Visibility != Visibility.Visible) return;
        Motion.Play(_bolts, ms, t =>
        {
            _clapT = clap ? t : -1; _shimT = shimmer ? t : -1;
            var rows = RowsAt();
            double step = ShimStep(rows.Count);
            for (int i = 0; i < rows.Count; i++)
            {
                var r = rows[i].Row;
                if (shimmer) { double k = Seg(t, .06 + step * i, .06 + step * i + .24); r.Sweep(k is > 0 and < 1 ? k : -1); }
                if (clap && r.Member is { Rank: 1 })
                {
                    double flash = t < .05 ? 0 : t < .09 ? Seg(t, .05, .09) : 1 - Seg(t, .2, .45);
                    double glyph = t < .1 ? 0 : Back(Seg(t, .1, .3), 2.6);
                    r.Strike(flash, glyph);
                }
            }
            if (clap) Lite(_fightFlash, .5 * ClapLight(t));   // the glass lit, never washed out
            else if (flicker) Lite(_fightFlash, .75 * (t < .05 ? Seg(t, 0, .05) : t < .12 ? 1 - Seg(t, .05, .12) : t < .16 ? Seg(t, .12, .16) : 1 - Seg(t, .16, .4)));
            _bolts.InvalidateVisual();
        }, () =>
        {
            _clapT = _shimT = -1; Lite(_fightFlash, 0);
            foreach (var (r, _) in RowsAt()) { r.Sweep(-1); r.Strike(0, 1); }
            _bolts.InvalidateVisual();
        });
    }

    // ================================================================ the moments

    /// <summary>A pickup: a crackle runs along the Buddy's top edge, and its worth leaps out beside it on a spark, then fades.</summary>
    protected override void Pickup(Pick p, bool held)
    {
        if (KinahHidden || _line.Visibility != Visibility.Visible) return;
        var worth = p.Worth.Length > 0 ? "+" + p.Worth.TrimStart('+') : p.Count;
        var words = new StackPanel { Margin = new Thickness(9, 2, 11, 3) };
        words.Children.Add(Tt(worth, 14, p.Worth.Length > 0 ? Frozen(Mod.Mix(MintC2, Colors.White, .3)) : Arc, FontWeights.Bold));
        var nm = Tt(p.Name.Length > 13 ? p.Name[..12].TrimEnd() + "…" : p.Name, 9.5, Soft, FontWeights.SemiBold); nm.Margin = new Thickness(0, -2, 0, 0);
        words.Children.Add(nm);
        var chip = new Border
        {
            Child = words, MaxWidth = 84, CornerRadius = new CornerRadius(3), Background = Frozen(Mod.A(Deep0C, .92)), BorderThickness = new Thickness(1),
            BorderBrush = Frozen(Mod.A(VoltC, .6)),
        };
        var shift = new TranslateTransform();
        chip.RenderTransform = shift;
        chip.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        Canvas.SetLeft(chip, 3); Canvas.SetTop(chip, -chip.DesiredSize.Height / 2 + LineCrest / 2);
        // the spark it leaps out on: drawn once, shown by its opacity
        var arc = new TacSketch((dc, _) => DrawBolt(dc, BoltPts(new Point(-8, LineCrest / 2), new Point(6, LineCrest / 2), 77, 2.2, 3), 1, .8)) { Opacity = 0 };
        _lineAnchor.Children.Clear(); _lineAnchor.Children.Add(arc); _lineAnchor.Children.Add(chip);
        Motion.Play(chip, 1900, t =>
        {
            Zap(t < .45 ? Seg(t, 0, .45) : -1);
            arc.Opacity = t < .12 ? 0 : t < .18 ? 1 : 1 - Seg(t, .18, .4);
            double k = Out(Seg(t, .14, .3));
            shift.X = -8 * (1 - k);
            chip.Opacity = t < .82 ? k : 1 - Seg(t, .82, 1);
        }, () => { Zap(-1); _lineAnchor.Children.Clear(); });
    }

    /// <summary>A rare drop: its strip blooms out from the left (a clip widening), the grade's light flashes, the tile pops in with sparks bursting off it.</summary>
    protected override void RareDrop(Pick p, bool held)
    {
        if (_rareSparks is not { } sparks) return;
        var tile = _rareTile;
        var sc = new ScaleTransform(1, 1);
        if (tile != null) { tile.RenderTransformOrigin = new Point(.5, .5); tile.RenderTransform = sc; }
        var row = _rareRow;
        Motion.Play(sparks, 1500, t =>
        {
            _rareBloom.ScaleX = Math.Max(.02, Out(Seg(t, 0, .4)));
            if (row != null) row.Opacity = Seg(t, 0, .12);
            if (_rareFlash != null) Lite(_rareFlash, t < .15 ? Seg(t, 0, .15) : 1 - Seg(t, .15, .7));
            _rareBurst = t is > .12 and < .7 ? Seg(t, .12, .7) : -1;
            sparks.InvalidateVisual();
            sc.ScaleX = sc.ScaleY = .6 + .4 * Back(Seg(t, .08, .38), 2.4);
        }, () => { _rareBloom.ScaleX = 1; if (row != null) row.Opacity = 1; if (_rareFlash != null) Lite(_rareFlash, 0); _rareBurst = -1; sparks.InvalidateVisual(); sc.ScaleX = sc.ScaleY = 1; });
    }

    /// <summary>Your best passed: the coil charges round to full, a spark at the head of the charge, and a crackle runs the Buddy's edge.</summary>
    protected override void BestPassed(bool held)
    {
        if (_coil is not { } art) return;
        double was = _share is > 0 and < 1 ? _share : .86;
        bool beat = _beat;
        Motion.Play(art, 1800, t =>
        {
            _beat = false; _share = was;
            _complete = Out(Seg(t, 0, .5));
            _head = t < .5 ? Seg(t, 0, .5) : -1;
            if (t >= .5) { _beat = beat; _complete = 1; }
            art.InvalidateVisual();
            Zap(t is > .45 and < .9 ? Seg(t, .45, .9) : -1);
        }, () => { _complete = 0; _head = -1; _beat = beat; Zap(-1); art.InvalidateVisual(); });
    }

    /// <summary>An entry opened: the first timer's chip charges mint round its edge and settles.</summary>
    protected override void EntryOpened(TimerBoard.Item i, bool held)
    {
        if (_firstChip is not { } chip) return;
        var glow = new DropShadowEffect { Color = MintC2, ShadowDepth = 0, BlurRadius = 10, Opacity = 0 };
        chip.Effect = glow;
        var edge = chip.BorderBrush;
        chip.BorderBrush = Frozen(Mod.Mix(MintC2, Colors.White, .3));
        Motion.Play(chip, 1200, t => glow.Opacity = .9 * (t < .2 ? Seg(t, 0, .2) : 1 - Seg(t, .2, 1)),
            () => { chip.Effect = null; chip.BorderBrush = edge; });
    }

    /// <summary>A fight started: the panel settles in, then the thunderclap on the leader and the arcs down the bars.</summary>
    protected override void FightStarted(Encounter e, bool held)
    {
        Motion.Play(_fight, 420, t =>
        {
            double k = Out(t);
            _fightShift.Y = 6 * (1 - k);
            _fight.Opacity = .85 + .15 * k;
        }, () => { _fightShift.Y = 0; _fight.Opacity = 1; });
        _leadFight = e.Id; _leadId = Meter?.Members.FirstOrDefault(m => m.Rank == 1)?.Id ?? 0;
        Storm(true, true, 1700);
    }

    /// <summary>A fight ended: a night-glass plate charged in mint (coral for a wipe) lays over the fight's head with the outcome and the clock; a kill sends the arcs down the bars.</summary>
    protected override void FightEnded(Encounter e, bool held)
    {
        if (e.Outcome is not ("kill" or "wipe" or "cleared")) return;
        bool wipe = e.Outcome == "wipe";
        var c = wipe ? CoralC : MintC2;
        var plate = new Grid { Height = 32, Margin = new Thickness(Pad - 4, 0, Pad - 4, 0), VerticalAlignment = VerticalAlignment.Top };
        var bloomAt = new ScaleTransform(1, 1);
        plate.Children.Add(new TacSketch((dc, s) =>
        {
            double w = s.Width;
            if (w < 10) return;
            var r = new Rect(.5, .5, w - 1, s.Height - 1);
            var shape = Cut(r, 8, 2);
            dc.DrawGeometry(Mod.Br(Deep0C, .96), null, shape);
            dc.PushClip(shape);
            dc.DrawRectangle(Mod.Grad(false, (Mod.A(c, .26), 0), (Mod.A(c, .08), .6), (Mod.A(c, .02), 1)), null, new Rect(0, 0, w, s.Height));
            dc.DrawRectangle(Mod.Br(Mod.Mix(c, Colors.White, .5), 1), null, new Rect(0, 0, 2.5, s.Height));
            dc.Pop();
            dc.DrawGeometry(null, Mod.P(Mod.Br(c, .7), 1.2), shape);
        }) { RenderTransform = bloomAt });
        var words = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(14, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        words.Children.Add(Tt(OutcomeText(e), 17, White, FontWeights.Bold));
        var ck = Tt("  " + Clock(e.Duration(e.End ?? DateTime.UtcNow)), 12, Arc, FontWeights.SemiBold); ck.Margin = new Thickness(4, 3, 0, 0);
        words.Children.Add(ck);
        words.Effect = Shade();
        plate.Children.Add(words);
        _band.Children.Clear(); _band.Children.Add(plate);
        // over the head as laid out now (the timers can sit above it in a dungeon), a few px proud of it each way
        void Seat()
        {
            double y = _fightBody.Margin.Top, h = 22;
            if (_headEl is { } hd && hd.ActualHeight > 0 && hd.IsDescendantOf(_fight)) { y = hd.TranslatePoint(new Point(0, 0), _fight).Y; h = hd.ActualHeight; }
            plate.Margin = new Thickness(Pad - 4, Math.Max(2, y - 5), Pad - 4, 0);
            plate.Height = h + 10;
        }
        Seat();
        var head = _headEl;
        bool seated = false;
        Motion.Play(_band, 2400, t =>
        {
            if (!seated) { Seat(); seated = true; }   // once, on the first frame after layout: never a layout per frame
            bloomAt.ScaleX = Math.Max(.01, Out(Seg(t, 0, .2)));
            words.Opacity = t < .84 ? Seg(t, .06, .2) : 1 - Seg(t, .84, 1);
            plate.Opacity = t < .84 ? 1 : 1 - Seg(t, .84, 1);
            if (head != null) head.Opacity = t < .84 ? 1 - Seg(t, 0, .1) : Seg(t, .84, 1);
        }, () => { _band.Children.Clear(); if (head != null) head.Opacity = 1; });
        if (!wipe) Storm(false, true, 1500, flicker: true);
    }
}
