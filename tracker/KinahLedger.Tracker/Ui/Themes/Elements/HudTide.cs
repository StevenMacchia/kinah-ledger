using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using KinahLedger.Parser;
using KinahLedger.Session;
using static KinahLedger.Ui.HudKit;

namespace KinahLedger.Ui;

/// <summary>
/// Tide (the Elements theme): the deep, seen from under the surface. Every panel is sea glass whose top edge is the sea's
/// surface: a slow swell that rises at the right into a breaking wave standing out of the panel (its lip curling over a dark
/// tube, foam heaped on it, spray thrown off it); light shafts and a net of caustic light fall in from the top, and along the
/// meter's foot lies the seabed (sand, pebbles, kelp and a coral fan, a starfish). Each meter row is a dark channel the
/// member's water fills to their share, in the sea's tint of their class colour, lit under its surface, its leading edge a
/// crest curling forward with white foam on its lip; the leader's water carries the caustic light and an open clam with a
/// pearl stands in rank 1's place. Figures are white (an aqua halo behind the DPS, the water ebbing dark under the share),
/// names white with a tight dark shade. The Buddy is the same glass with a smaller wave: the ring a well filled with water
/// to your best's share, the hour big in white, the pickups in tiles rimmed in sea glass, the timers as sea-glass capsules.
/// The moments are the sea's, each made of parts drawn once that only move and fade: a wall of water sweeps the meter on a
/// fight's start, a kill and a new leader, light rolling along the bars behind it and bubbles rising, the pearl surfacing in
/// rings of ripples; a kill's plate washes in behind a crest that throws up a splash; a pickup stirs the caustics and sends
/// bubbles up; a rare drop turns a whirlpool behind its tile in rings of its grade's colour; your best passed surges the
/// well to the brim with a splash and "Best here".
/// </summary>
sealed partial class HudTide : ModernLook
{
    static readonly Color MintC2 = Color.FromRgb(0x5D, 0xF2, 0xA6), MarkMintC = Color.FromRgb(0x7D, 0xFF, 0xC0),
        CoralC = Color.FromRgb(0xFF, 0x86, 0x86), HpC = Color.FromRgb(0xFF, 0x6B, 0x7D), HpBC = Color.FromRgb(0xFF, 0x9A, 0x7A), GreyC = Color.FromRgb(0x8A, 0x90, 0xA0),
        WarnC2 = Color.FromRgb(0xFF, 0xB0, 0x86), GoldC2 = Color.FromRgb(0xFF, 0xCF, 0x6A);
    static readonly SolidColorBrush White = Frozen(Colors.White), Soft = Frozen(Mod.A(Color.FromRgb(0xE8, 0xFC, 0xFC), .86)), Quiet = Frozen(Mod.A(Color.FromRgb(0xCF, 0xF2, 0xF2), .64)),
        Mint2 = Frozen(Color.FromRgb(0x8C, 0xFF, 0xC6)), MarkMint = Frozen(MarkMintC), Coral = Frozen(CoralC), Aqua = Frozen(Color.FromRgb(0x9F, 0xF2, 0xF2));
    const double Radius = 13, Pad = 11;
    /// <summary>The Buddy's breaking wave, smaller than the meter's.</summary>
    const double LineCrest = .6;
    /// <summary>The pickup strip on the glass: 26 px tiles beside the hour, an aqua rule, the gold star; plain tiles rimmed in sea glass (<see cref="SeaRims"/>).</summary>
    static readonly PickStrip.Look Strip = new(26, 4, true, Frozen(Mod.A(Color.FromRgb(0x6F, 0xE6, 0xEA), .4)), Frozen(Color.FromRgb(0xFF, 0xCF, 0x6A)));

    // the room round the panels for the drop under them (drawn inside the window: nothing past its edge)
    readonly Grid _root = new() { Margin = new Thickness(10, 33, 10, 16), HorizontalAlignment = HorizontalAlignment.Left };   // over it, the room for the breaking wave (CrestRoom(1))
    readonly StackPanel _body = new() { HorizontalAlignment = HorizontalAlignment.Left };
    double _opacity = 0.9;

    // the Buddy: its glass, the run's head over its words, the well, the hour, the session and the timer chips
    readonly Grid _line = new() { HorizontalAlignment = HorizontalAlignment.Left };
    readonly TacSketch _lineArt;
    readonly StackPanel _lineStack = new();
    readonly StackPanel _lineBody = new() { Orientation = Orientation.Horizontal, Margin = new Thickness(8, 10, 14, 8) };
    readonly StackPanel _timers = new() { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
    readonly Border _timersInLine = new() { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 0) };
    /// <summary>The timer chips over the line, when the pickup strip takes their room on it.</summary>
    readonly Border _timersUp = new() { HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(2, 0, 0, 12), Visibility = Visibility.Collapsed };
    bool _dim;
    TacSketch? _ringArt;
    double _share = -1;
    bool _beat, _ringEmpty;
    /// <summary>
    /// The Buddy's glass stirred by a pickup: a second net of caustic light over it that fades in and drifts, and bubbles
    /// rising (both clipped to the glass, hidden at rest; only their transforms and opacity move).
    /// </summary>
    readonly Canvas _lineFx = new() { IsHitTestVisible = false };
    readonly System.Windows.Shapes.Rectangle _stir = new() { Height = CauH, Fill = Caustic, Opacity = 0, Visibility = Visibility.Collapsed };
    readonly TranslateTransform _stirT = new();
    readonly BubbleSet _lineBubbles = new(6);
    /// <summary>Over the Buddy, unclipped: the word a moment raises ("Best here").</summary>
    readonly Canvas _lineTop = new() { IsHitTestVisible = false };
    Border? _firstChip;

    // the rare drop under the Buddy (its own glass), and the notice line
    readonly Grid _rare = new() { HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 6, 0, 0), Visibility = Visibility.Collapsed };
    readonly Grid _note = new() { HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 6, 0, 0), Visibility = Visibility.Collapsed };

    // the fight: its glass, its head, HP, rows and foot; over it the bubbles, and the plate at its end
    readonly Grid _fight = new() { HorizontalAlignment = HorizontalAlignment.Left, Visibility = Visibility.Collapsed, Margin = new Thickness(0, 0, 0, 23) };
    readonly TacSketch _fightArt;
    /// <summary>Over the fight's glass (clipped to it): the bubbles a swell sends up.</summary>
    readonly Canvas _fightOver = new() { IsHitTestVisible = false };
    readonly BubbleSet _fightBub = new(9);
    /// <summary>The wave that sweeps the meter on its big moments (drawn once per height, only moved), and the splash a kill throws up.</summary>
    readonly TacSketch _bigWave = new((dc, s) => DrawBigWave(dc, s.Height)) { Width = BigWaveW, Height = 200, Visibility = Visibility.Collapsed };
    readonly TranslateTransform _bigWaveT = new();
    readonly SplashSet _fightSplash = new(12, .8), _lineSplash = new(10, .55);
    readonly StackPanel _fightBody = new() { Margin = new Thickness(Pad, 12, Pad, 12) };
    readonly Border _timersInFight = new() { Margin = new Thickness(0, 0, 0, 8), HorizontalAlignment = HorizontalAlignment.Left };
    readonly Grid _fightFx = new() { IsHitTestVisible = false };
    readonly TranslateTransform _settle = new();
    FrameworkElement? _head;

    public HudTide() : base(bare: true)
    {
        // the look's own rows (Ui/Themes/ThemeRows.cs): each a channel the member's water fills
        UseRows(TideRow.Pitch, m => new TideRow(this), 15, GapLine);
        _lineArt = new TacSketch(DrawLineGlass);
        _lineStack.Children.Add(_runHead); _lineStack.Children.Add(_lineBody);
        _stir.RenderTransform = _stirT;
        _lineFx.Children.Add(_stir); _lineFx.Children.Add(_lineBubbles.Layer);
        _line.Children.Add(_lineArt); _line.Children.Add(_lineFx); _line.Children.Add(_lineStack); _line.Children.Add(_lineTop);
        _line.SizeChanged += (_, e) => { _lineFx.Clip = PanelShape(e.NewSize, Radius, true, LineCrest); _stir.Width = e.NewSize.Width + CauW; };
        _fightArt = new TacSketch((dc, s) => SeaPanel(dc, s, Radius, _opacity, surface: true, seabed: _seabed));
        _fightBub.Layer.IsHitTestVisible = false;
        _bigWave.RenderTransform = _bigWaveT;
        _fightOver.Children.Add(_bigWave); _fightOver.Children.Add(_fightBub.Layer); _fightOver.Children.Add(_fightSplash.Layer);
        _lineFx.Children.Add(_lineSplash.Layer);
        _rare.SizeChanged += (_, e) => ClipRare(e.NewSize);
        _fight.SizeChanged += (_, e) => { _fightOver.Clip = PanelShape(e.NewSize, Radius, true); if (Math.Abs(_bigWave.Height - e.NewSize.Height) > .5) _bigWave.Height = e.NewSize.Height; };
        _fight.Children.Add(_fightArt); _fight.Children.Add(_fightBody); _fight.Children.Add(_fightOver); _fight.Children.Add(_fightFx);
        _fight.RenderTransform = _settle;
        _body.Children.Add(_fight); _body.Children.Add(_timersUp); _body.Children.Add(_line); _body.Children.Add(_rare); _body.Children.Add(_note);
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
        _lineArt.InvalidateVisual(); _fightArt.InvalidateVisual();
        foreach (var g in new[] { _rare, _note }) foreach (var a in g.Children.OfType<TacSketch>()) a.InvalidateVisual();
    }

    protected override string LineSig(Figures f) => base.LineSig(f) + "|" + (Rare(Values)?.Id ?? "") + "|" + (Picks(Values).FirstOrDefault()?.Id ?? "") + "|" + Dimmed(Values) + "|" + PickStrip.Sig(Values, Strip) + "|" + RunBuddy.Sig(Values);

    static bool Dimmed(IReadOnlyDictionary<string, string> v) => (v.TryGetValue("show", out var s) && s == "dim") || (v.TryGetValue("paused", out var p) && p.Length > 0);

    // ================================================================ the Buddy

    /// <summary>The Buddy's glass (greyed a little while paused).</summary>
    void DrawLineGlass(DrawingContext dc, Size s) =>
        SeaPanel(dc, s, Radius, _opacity * (_dim ? .85 : 1), _dim ? .5 : 1, 0, tint: _dim ? GreyC : null, tintK: .35, surface: true, crest: LineCrest);

    protected override void DrawLine(Figures f)
    {
        _lineBody.Children.Clear();
        _ringArt = null; _timerUp = false; _stripTimerW = -1;
        if (_timersInLine.Child != null) _timersInLine.Child = null;
        _dim = Dimmed(Values);
        _lineBody.Opacity = _dim ? .62 : 1;
        bool mark = f.Abyss && f.Counted && f.Icon.Length > 0;
        if (mark) { var m = AbyssMark(f.Icon, 38); m.Margin = new Thickness(0, 0, 10, 0); m.VerticalAlignment = VerticalAlignment.Center; _lineBody.Children.Add(m); }
        // in a dungeon run: the run's clock, splits and verdict in place of the hour (HudTide.Run.cs)
        var rs = RunNow();
        _runHead.Visibility = Visibility.Collapsed;
        if (rs != null) DrawRun(rs);
        else if (!HasHour(f))
        {
            // no hour yet: the well empty, the waiting words beside it
            if (!mark) _lineBody.Children.Add(Well(-1, false, ""));
            var w = Lit(Tt(Waiting(f.Abyss), 13, Soft, FontWeights.SemiBold)); w.Margin = new Thickness(0, 0, 2, 0);
            _lineBody.Children.Add(w);
        }
        else
        {
            if (HasRing(f)) _lineBody.Children.Add(Well(f.Share, f.Beat, f.Beat ? "★" : Pct(f.Share)));
            // the hour big with its aqua halo, its unit small; the session and the latest pickup under it
            var words = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            var top = new StackPanel { Orientation = Orientation.Horizontal };
            var rate = Tt(f.Rate, f.Warm ? 15 : 23, f.Warm ? Soft : White, FontWeights.Bold);
            rate.Tag = "buddy-rate";
            if (!f.Warm) rate.Effect = Halo(10, .6);
            top.Children.Add(rate);
            if (f.Unit.Length > 0) { var u = Tt(f.Unit, 11.5, Quiet, FontWeights.SemiBold); u.VerticalAlignment = VerticalAlignment.Bottom; u.Margin = new Thickness(2, 0, 0, 4); top.Children.Add(u); }
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
                    if ((strip = StripIn(StripRoom(FightWidth - 8, baseW + timerW, wordsW, oldW, 14), look)) != null) break;
                    if (timerW > 0 && (strip = StripIn(StripRoom(FightWidth - 8, baseW, wordsW, oldW + timerW, 14), look)) != null) { _timerUp = true; break; }
                }
                if (strip != null) { sub = bare; strip.Margin = new Thickness(14, 0, 0, 0); SeaRims(strip); }
                // no room beside the hour: under the session, never dropped (Steven, 2026-11-19)
                else if ((under = StripUnder(Strip, false)) != null) { sub = bare; SeaRims(under); }
                _stripTimerW = timerW;
            }
            if (sub.Inlines.Count > 0) words.Children.Add(sub);
            if (under != null) words.Children.Add(under);
            _lineBody.Children.Add(Lit(words));
            if (strip != null) _lineBody.Children.Add(strip);
        }
        _lineBody.Children.Add(_timersInLine);
        PlaceTimers();
        _lineArt.InvalidateVisual();

        // the rare drop the window holds: its own glass blooming in its grade's colour, the tile, RARE DROP and its name
        _rare.Children.Clear();
        _rareArt = null; _rareTile = null;
        if (!KinahHidden && Rare(Values) is { } r)
        {
            var c = ColorOf(r.Color, r.Grade);
            _rareC = c;
            var art = new TacSketch((dc, s) => DrawRareGlass(dc, s, c));
            _rareArt = art;
            _rare.Children.Add(art);
            // the bloom's parts (hidden at rest): a glow of the grade's colour behind the tile and rings spreading from it
            var fx = new Canvas { IsHitTestVisible = false };
            _rareGlow = new System.Windows.Shapes.Ellipse { Width = 120, Height = 64, Fill = GlowBrush(c, .9), Opacity = 0, RenderTransformOrigin = new Point(.5, .5), RenderTransform = new ScaleTransform(.4, .4) };
            Canvas.SetLeft(_rareGlow, 23 - 60); Canvas.SetTop(_rareGlow, 21 - 32);
            _rareRipples = new RippleSet(110, .42, Mod.Mix(c, Colors.White, .5), 3, 1, 1.6);
            _rareRipples.At(23, 21);
            _whirl = new System.Windows.Shapes.Rectangle { Width = 50, Height = 50, Fill = WhirlBrush, Opacity = 0, IsHitTestVisible = false };
            _whirlR = new RotateTransform(0, 25, 25);
            _whirl.RenderTransform = new TransformGroup { Children = { _whirlR, new ScaleTransform(1, .62, 25, 25) } };
            Canvas.SetLeft(_whirl, 23 - 25); Canvas.SetTop(_whirl, 21 - 25);
            fx.Children.Add(_rareGlow); fx.Children.Add(_whirl); fx.Children.Add(_rareRipples.Layer);
            _rareFx = fx;
            ClipRare(new Size(_rare.ActualWidth, _rare.ActualHeight));
            _rare.Children.Add(fx);
            var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(9, 7, 16, 7) };
            var tile = Tile(r, 28); tile.Margin = new Thickness(0, 0, 10, 0);
            _rareTile = tile;
            row.Children.Add(tile);
            var words = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            var mk = Mark(L.T("RARE DROP"), Frozen(Mod.Mix(c, Colors.White, .55)), c, size: 8); mk.HorizontalAlignment = HorizontalAlignment.Left; mk.Margin = new Thickness(0, 0, 0, 2);
            words.Children.Add(mk);
            var nm = Tt(r.Name, 14, Frozen(Mod.Mix(c, Colors.White, .45)), FontWeights.Bold);
            if (r.Count.Length > 0) nm.Inlines.Add(new System.Windows.Documents.Run("  " + r.Count) { FontSize = 11.5, FontWeight = FontWeights.SemiBold, Foreground = Soft });
            words.Children.Add(nm);
            row.Children.Add(Lit(words));
            _rare.Children.Add(row);
            _rare.Visibility = Visibility.Visible;
        }
        else _rare.Visibility = Visibility.Collapsed;

        // the notice: its words in their tone on a capsule of the glass, the warning sign before a pause
        _note.Children.Clear();
        if (f.Note.Length > 0 && !KinahHidden)
        {
            bool warn = ReferenceEquals(f.NoteTone, Warn);
            _note.Children.Add(new TacSketch((dc, s) => SeaPanel(dc, s, s.Height / 2, _opacity, .6, 90, shadow: false)));
            var sp = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(12, 4, 13, 5) };
            if (warn) { var wi = WarnIcon(); wi.Width = wi.Height = 13; wi.Margin = new Thickness(0, 0, 6, 0); sp.Children.Add(wi); }
            var tone = warn ? WarnC2 : ReferenceEquals(f.NoteTone, Gold) ? GoldC2 : ReferenceEquals(f.NoteTone, Mint) ? MintC2 : Color.FromRgb(0xEC, 0xFA, 0xFA);
            var t = Tt(f.Note, 11.5, Frozen(tone), FontWeights.SemiBold); t.TextTrimming = TextTrimming.CharacterEllipsis; t.MaxWidth = 380;
            sp.Children.Add(t);
            _note.Children.Add(Lit(sp));
            _note.Visibility = Visibility.Visible;
        }
        else _note.Visibility = Visibility.Collapsed;
    }

    /// <summary>The line under the hour: the session, the latest pickup and its worth (<paramref name="pick"/>); in the Abyss its stats.</summary>
    TextBlock Sub(Figures f, bool pick)
    {
        var sub = new TextBlock { FontFamily = Mod.Face, FontSize = 11, FontWeight = FontWeights.Medium, Foreground = Soft, Margin = new Thickness(1, 2, 0, 0) };
        System.Windows.Documents.Typography.SetNumeralAlignment(sub, FontNumeralAlignment.Tabular);
        void Dot() { if (sub.Inlines.Count > 0) sub.Inlines.Add(new System.Windows.Documents.Run("  ·  ") { Foreground = Quiet }); }
        if (f.Total.Length > 0) { sub.Inlines.Add(new System.Windows.Documents.Run(f.Total) { FontWeight = FontWeights.Bold, Foreground = White }); sub.Inlines.Add(new System.Windows.Documents.Run(" " + f.TotalWord)); }
        foreach (var (fig, word, tone) in f.Stats)
        {
            Dot();
            var c = ReferenceEquals(tone, Rec) ? CoralC : ReferenceEquals(tone, Gold) ? GoldC2 : Colors.White;
            sub.Inlines.Add(new System.Windows.Documents.Run(fig) { FontWeight = FontWeights.Bold, Foreground = Frozen(c) });
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

    /// <summary>
    /// The well: your best's share as water filling a round well (<paramref name="share"/> -1: empty, waiting), its figure in
    /// white over it. Its parts: the deep (drawn once), the water (drawn once, set at its level by a translation, so filling
    /// it to the brim moves only a transform), the rim and the glint over it, the ripples a full well sends out.
    /// </summary>
    Grid Well(double share, bool beat, string text)
    {
        _share = share; _beat = beat; _ringEmpty = share < 0; _wellFull = beat;
        var ring = new Grid { Width = 44, Height = 44, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 12, 0) };
        ring.Children.Add(new TacSketch(DrawWellDeep, 44, 44));
        _wellWater = null; _wellRipples = null;
        if (!_ringEmpty)
        {
            var host = new Canvas { Width = 44, Height = 44, IsHitTestVisible = false, Clip = WellDisk };
            _wellWaterT.Y = WellLevel(beat ? 1 : share);
            _wellWater = new TacSketch(DrawWellWater, 44, 48) { RenderTransform = _wellWaterT };
            host.Children.Add(_wellWater);
            ring.Children.Add(host);
        }
        _ringArt = new TacSketch(DrawWellRim, 44, 44);
        ring.Children.Add(_ringArt);
        if (!_ringEmpty)
        {
            var fx = new Canvas { IsHitTestVisible = false };
            _wellRipples = new RippleSet(30, 1, AquaC, 3, .75, 1.4);
            _wellRipples.At(22, 22);
            fx.Children.Add(_wellRipples.Layer);
            ring.Children.Add(fx);
        }
        if (text.Length > 0)
        {
            var pc = Lit(Tt(text, beat ? 15 : 13.5, White, FontWeights.Bold)); pc.HorizontalAlignment = HorizontalAlignment.Center;
            ring.Children.Add(pc);
        }
        return ring;
    }

    static readonly EllipseGeometry WellDisk = MakeDisk();
    static EllipseGeometry MakeDisk() { var g = new EllipseGeometry(new Point(22, 22), 17, 17); g.Freeze(); return g; }

    /// <summary>The water's top in the well for a share (a full well brims a little higher).</summary>
    static double WellLevel(double shown) => 22 + 17 - 34 * Math.Clamp(shown, 0, 1) - (shown >= .999 ? 3 : 0) - 1.4;

    TacSketch? _wellWater;
    readonly TranslateTransform _wellWaterT = new();
    RippleSet? _wellRipples;
    bool _wellFull;

    /// <summary>The well's deep: a dark round with a faint inner ring while it waits empty.</summary>
    void DrawWellDeep(DrawingContext dc, Size s)
    {
        dc.DrawEllipse(Mod.Br(AbyssC, .85), null, new Point(22, 22), 20, 20);
        if (_ringEmpty) { dc.DrawEllipse(null, Mod.P(Mod.Br(AquaC, .26), 1), new Point(22, 22), 13, 13); dc.DrawEllipse(null, Mod.P(Mod.Br(AquaC, .14), 1), new Point(22, 22), 7, 7); }
    }

    /// <summary>The well's water, its surface at the top of the part: a gently waving top with a line of light, the caustic net in it; aqua-mint once beaten.</summary>
    void DrawWellWater(DrawingContext dc, Size s)
    {
        var tint = _wellFull ? Mod.Mix(MintC2, SeaTintC, .45) : SeaTintC;
        double Y(double x) => 1.4 + 1.1 * Math.Sin((x - 22) / 5.5 + .8);
        var g = new StreamGeometry();
        using (var gc = g.Open())
        {
            gc.BeginFigure(new Point(0, s.Height), true, true);
            for (double x = 0; x <= 44; x += 2) gc.LineTo(new Point(x, Y(x)), true, true);
            gc.LineTo(new Point(44, s.Height), true, true);
        }
        g.Freeze();
        dc.DrawGeometry(Mod.Grad(true, (Mod.A(Mod.Mix(tint, FoamC, .12), .95), 0), (Mod.A(Mod.Mix(tint, AbyssC, .55), .95), 1)), null, g);
        dc.PushClip(g);
        Caustics(dc, new Rect(0, 0, 44, s.Height), .55, 60);
        dc.Pop();
        var sl = new StreamGeometry();
        using (var sc = sl.Open())
        {
            sc.BeginFigure(new Point(0, Y(0)), false, false);
            for (double x = 2; x <= 44; x += 2) sc.LineTo(new Point(x, Y(x)), true, true);
        }
        sl.Freeze();
        dc.DrawGeometry(null, Mod.P(Mod.Br(FoamC, .85), 1.1), sl);
    }

    /// <summary>The well's rim (mint once beaten) and a glint on its glass.</summary>
    void DrawWellRim(DrawingContext dc, Size s)
    {
        var center = new Point(22, 22);
        dc.DrawEllipse(null, Mod.P(Mod.Br(_wellFull ? MintC2 : AquaC, _wellFull ? .85 : _ringEmpty ? .62 : .5), 1.4), center, 20, 20);
        dc.PushClip(WellDisk);
        dc.DrawEllipse(Mod.Br(Colors.White, .08), null, new Point(16, 13), 9, 5);
        dc.Pop();
    }

    /// <summary>The timer chips' room at the line's end, 0 with none.</summary>
    double LineTimerW() => _timers.Children.Count > 0 && !KinahHidden ? 12 + Mod.W(_timers) : 0;

    /// <summary>The timers' room the line's strip was fitted to (-1: no strip time); chips that change it draw the line again.</summary>
    double _stripTimerW = -1;

    /// <summary>The timer chips sit over the line, to leave its room to the pickup strip.</summary>
    bool _timerUp;

    Color _rareC;
    TacSketch? _rareArt;
    FrameworkElement? _rareTile;
    Canvas? _rareFx;
    System.Windows.Shapes.Ellipse? _rareGlow;
    RippleSet? _rareRipples;
    System.Windows.Shapes.Rectangle? _whirl;
    RotateTransform? _whirlR;

    void ClipRare(Size s)
    {
        if (_rareFx != null && s.Width > 2) _rareFx.Clip = Mod.Pill(new Rect(1, 1, s.Width - 2, Math.Max(0, s.Height - 2)), Radius);
    }

    /// <summary>The rare drop's glass: the sea in a breath of its grade's colour, the grade's glow held behind the tile.</summary>
    void DrawRareGlass(DrawingContext dc, Size s, Color c)
    {
        SeaPanel(dc, s, Radius, _opacity, .7, 140, tint: c, tintK: .32);
        dc.PushClip(Mod.Pill(new Rect(1, 1, s.Width - 2, s.Height - 2), Radius));
        var glow = new RadialGradientBrush();
        glow.GradientStops.Add(new GradientStop(Mod.A(c, .34), 0));
        glow.GradientStops.Add(new GradientStop(Mod.A(c, 0), 1));
        glow.Freeze();
        dc.DrawEllipse(glow, null, new Point(23, s.Height / 2), 34, 26);
        dc.Pop();
    }

    protected override void KinahShown(bool shown)
    {
        _line.Visibility = shown ? Visibility.Visible : Visibility.Collapsed;
        if (!shown) { _note.Visibility = Visibility.Collapsed; _rare.Visibility = Visibility.Collapsed; }
        _fight.Margin = new Thickness(0, 0, 0, shown ? 23 : 0);   // room for the Buddy's wave under it (CrestRoom(LineCrest))
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
            var c = tone == TimerBadge.Tone.On ? MintC2 : tone == TimerBadge.Tone.Soon ? GoldC2 : AquaC;
            var sp = new StackPanel { Orientation = Orientation.Horizontal };
            // the dot: a small pearl of light in the tone
            sp.Children.Add(new System.Windows.Shapes.Ellipse { Width = first ? 7 : 5.5, Height = first ? 7 : 5.5, Fill = Frozen(Mod.Mix(c, Colors.White, .15)), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) });
            var t = Tt(text, first ? 11.5 : 10.5, first ? White : Soft, first ? FontWeights.Bold : FontWeights.SemiBold);
            if (tone == TimerBadge.Tone.On) t.Foreground = Frozen(Mod.Mix(MintC2, Colors.White, .45));
            else if (tone == TimerBadge.Tone.Soon) t.Foreground = Frozen(Mod.Mix(GoldC2, Colors.White, .4));
            sp.Children.Add(t);
            var chip = new Border
            {
                Child = Lit(sp), CornerRadius = new CornerRadius(99), Padding = first ? new Thickness(9, 4, 11, 5) : new Thickness(8, 3, 9, 4), VerticalAlignment = VerticalAlignment.Center,
                Background = Mod.Grad(true, (Mod.A(Mod.Mix(SeaC, c, .12), .92), 0), (Mod.A(AbyssC, .94), 1)), BorderThickness = new Thickness(1),
                BorderBrush = Frozen(Mod.A(c, tone == TimerBadge.Tone.Quiet ? .36 : .62)),
                Margin = new Thickness(first ? 0 : 6, 0, 0, 0), Tag = first ? "timer-chip" : null,
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

    /// <summary>The seabed along the meter's foot: only under a foot (the words of a waiting card keep their room).</summary>
    bool _seabed;

    protected override void DrawFight(Encounter? e, DateTime now)
    {
        _fightBody.Children.Clear();
        bool bed = e is { Waiting: false, HasDamage: true };
        if (bed != _seabed) { _seabed = bed; _fightArt.InvalidateVisual(); }
        if (e == null) { _fight.Visibility = Visibility.Collapsed; _fightFx.Children.Clear(); PlaceTimers(); return; }
        double w = FightWidth - 8;
        _fight.Width = w;
        double inner = w - 2 * Pad;
        _fightBody.Children.Add(_timersInFight);
        // the head: the boss, the outcome once it's over, the clock with its aqua halo
        var head = new Grid();
        head.ColumnDefinitions.Add(new ColumnDefinition()); head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var title = Tt(e.Title, 14.5, White, FontWeights.Bold); title.TextTrimming = TextTrimming.CharacterEllipsis;
        head.Children.Add(Lit(title));
        var right = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(8, 0, 0, 0) };
        if (e.Waiting) right.Children.Add(Mark(L.T("READY"), MarkMint, MarkMintC, size: 9.5));
        else
        {
            var outcome = OutcomeText(e);
            if (outcome.Length > 0)
            {
                bool wipe = e.Outcome == "wipe";
                var oc = Mark(outcome, wipe ? Frozen(Color.FromRgb(0xFF, 0xA4, 0xA4)) : MarkMint, wipe ? CoralC : MarkMintC, size: 10);
                oc.CornerRadius = new CornerRadius(99); oc.Padding = new Thickness(8, 1, 8, 2); oc.Margin = new Thickness(0, 1, 8, 0); oc.Tag = "outcome";
                right.Children.Add(oc);
            }
            var clock = Tt(Clock(e, now), 18, White, FontWeights.Bold); clock.Tag = "fight-clock";
            clock.Effect = Halo(9, .65);
            right.Children.Add(clock);
        }
        Grid.SetColumn(right, 1); head.Children.Add(right);
        _head = head;
        _fightBody.Children.Add(head);
        if (e.Waiting)
        {
            var wt = Lit(Tt(e.ReadyText, 12.5, Soft, FontWeights.SemiBold)); wt.Margin = new Thickness(0, 6, 0, 2); wt.TextWrapping = TextWrapping.Wrap;
            _fightBody.Children.Add(wt);
            if (PartyBeforeHit(e)) { _fightBody.Children.Add(new Border { Height = 7 }); PutRows(_fightBody, e, now, inner); }
        }
        else
        {
            if (HpOf(e) is { } hp)
            {
                // the boss's HP: a slim coral line in a dark channel, ticks at the thirds, "47% · 1.84M left" at its right
                var hpLine = new Grid { Margin = new Thickness(0, 7, 0, 0) };
                hpLine.ColumnDefinitions.Add(new ColumnDefinition()); hpLine.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                var track = new Grid { Height = 6, VerticalAlignment = VerticalAlignment.Center };
                track.Children.Add(new TacSketch((dc, s) =>
                {
                    var r = new Rect(0, 0, s.Width, s.Height);
                    var shape = Mod.Pill(r, 3);
                    dc.DrawGeometry(Mod.Br(AbyssC, .9), Mod.P(Mod.Br(AquaC, .16), 1), shape);
                    dc.PushClip(shape);
                    double x1 = s.Width * Math.Clamp(hp, 0, 1);
                    if (x1 > .5) dc.DrawRectangle(Mod.Grad(false, (HpC, 0), (HpBC, 1)), null, new Rect(0, 0, x1, s.Height));
                    dc.DrawRectangle(Mod.Br(Colors.White, .3), null, new Rect(0, 0, x1, 1));
                    for (int k = 1; k <= 2; k++) { double x = Math.Round(s.Width * k / 3); dc.DrawRectangle(Mod.Br(DarkC, .7), null, new Rect(x - 1, 0, 2, s.Height)); }
                    dc.Pop();
                }));
                HpGhost(track, e, hp, now, 3);
                hpLine.Children.Add(track);
                var hl = Tt("", 11, Soft, FontWeights.Medium); hl.Margin = new Thickness(10, 0, 0, 0);
                hl.Inlines.Add(new System.Windows.Documents.Run($"{Math.Round(hp * 100):0}%") { FontWeight = FontWeights.Bold, Foreground = hp < .34 ? Frozen(Color.FromRgb(0xFF, 0x9A, 0xA4)) : White });
                var left = HpLeftText(e);
                if (left.Length > 0) hl.Inlines.Add(new System.Windows.Documents.Run("  ·  " + left));
                hl.Tag = "hp-text";
                Grid.SetColumn(hl, 1); hpLine.Children.Add(Lit(hl));
                _fightBody.Children.Add(hpLine);
                _fightBody.Children.Add(new Border { Height = 8 });
            }
            else _fightBody.Children.Add(new Border { Height = 8 });
            PutRows(_fightBody, e, now, inner);
            if (e.HasDamage)
            {
                _fightBody.Children.Add(SeaLine(new Thickness(0, 5, 0, 4)));
                _fightBody.Children.Add(Foot(e, now));
            }
        }
        _fight.Visibility = Visibility.Visible;
        PlaceTimers();
    }

    // ================================================================ the swell, the bubbles and the pearl

    long _leadId;
    string _leadFight = "";

    /// <summary>
    /// A swell rolls along the bars left to right (each row a beat after the one above), its crest lighting, and a few bubbles
    /// rise off them. Every part is made already and only moved and faded (the rows' bands of light, the bubbles).
    /// </summary>
    void Swell()
    {
        // the bubbles start under the rows, spread across them
        double w = _fight.ActualWidth > 0 ? _fight.ActualWidth : FightWidth - 8, top = 40, bottom = _fight.ActualHeight > 0 ? _fight.ActualHeight - 30 : 180;
        if (Meter is { } m && m.IsDescendantOf(_fight) && m.ActualHeight > 0)
        {
            var at = m.TranslatePoint(new Point(0, 0), _fight);
            top = at.Y; bottom = at.Y + m.ActualHeight + 6;
        }
        _fightBub.Place(new Rect(Pad, top, w - 2 * Pad, Math.Max(20, bottom - top)), Math.Min(90, Math.Max(40, bottom - top)));
        Motion.Play(_fightOver, 1700, t =>
        {
            double k = Seg(t, 0, .72), e = k * k * (3 - 2 * k);
            bool on = k is > 0 and < 1;
            _bigWave.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
            if (on) { _bigWaveT.X = -BigWaveW + (w + BigWaveW + 12) * e; _bigWave.Opacity = Seg(k, 0, .06) * (1 - Seg(k, .9, 1)); }
            _fightBub.Step(Seg(t, .12, 1));
            StepRows(Seg(t, .05, .95));
        }, () => { _bigWave.Visibility = Visibility.Collapsed; _fightBub.Step(-1); StepRows(-1); });
    }

    void StepRows(double sw)
    {
        if (Meter is not { } m) return;
        foreach (var x in m.Members) if (m.RowOf(x.Id) is TideRow r) r.SwellStep(sw);
    }

    /// <summary>The leader's row as drawn: a new leader in the fight seen (with effects playing) surfaces the pearl on them and rolls a swell.</summary>
    internal void Lead(TideRow row, MeterMember m, MeterContext c)
    {
        bool changed = _leadFight == c.E.Id && _leadId != 0 && _leadId != m.Id;
        _leadFight = c.E.Id; _leadId = m.Id;
        if (changed && c.Moves) { row.Surface(); Swell(); }
    }

    TideRow? LeaderRow() => Meter is { } m && m.Members.FirstOrDefault(x => x.Rank == 1 && !x.CpOnly && !x.Pvp) is { } top ? m.RowOf(top.Id) as TideRow : null;

    // ================================================================ the moments

    /// <summary>A pickup: a second net of caustic light fades in over the Buddy's glass and drifts, and a few bubbles rise through it.</summary>
    protected override void Pickup(Pick p, bool held)
    {
        if (KinahHidden || _line.Visibility != Visibility.Visible) return;
        double w = _line.ActualWidth > 0 ? _line.ActualWidth : 300, h = _line.ActualHeight > 0 ? _line.ActualHeight : 58;
        _lineBubbles.Place(new Rect(w * .3, 2, w * .66, h - 2), h * .7);
        Motion.Play(_lineFx, 1500, t => Stir(t, 1.6), () => Stir(-1, 0));
    }

    /// <summary>The Buddy's glass stirred at a course's point (outside it: still): the second net's strength rises and falls as it drifts; the bubbles rise.</summary>
    void Stir(double t, double strength, bool bubbles = true)
    {
        bool on = t is > 0 and < 1;
        _stir.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
        if (bubbles || !on) _lineBubbles.Step(on ? t : -1);
        if (!on) return;
        double k = t < .2 ? Out(t / .2) : 1 - Seg(t, .45, 1);
        _stir.Opacity = .55 * strength * k;
        _stirT.X = -CauW + 40 * Out(t);
    }

    /// <summary>A rare drop: its glass blooms in the grade's colour behind the tile, rings spreading out from it, the tile popping in.</summary>
    protected override void RareDrop(Pick p, bool held)
    {
        if (_rareArt is not { } art || _rareGlow is not { } glow || _rareRipples is not { } ripples) return;
        var tile = _rareTile;
        var sc = new ScaleTransform(1, 1);
        if (tile != null) { tile.RenderTransformOrigin = new Point(.5, .5); tile.RenderTransform = sc; }
        var gs = (ScaleTransform)glow.RenderTransform;
        Motion.Play(art, 1600, t =>
        {
            double b = Math.Sin(Math.PI * Math.Min(1, t * 1.6));
            glow.Opacity = Math.Max(b, t < .06 ? t / .06 : 0); gs.ScaleX = gs.ScaleY = .5 + .5 * Out(Seg(t, 0, .35));
            ripples.Step(Seg(t, .02, .9));
            if (_whirl is { } wh && _whirlR is { } wr) { wh.Opacity = t < .08 ? t / .08 : 1 - Seg(t, .7, 1); wr.Angle = -360 * Out(t); }
            sc.ScaleX = sc.ScaleY = .6 + .4 * Back(Seg(t, .05, .4), 2.2);
        }, () => { glow.Opacity = 0; ripples.Step(-1); if (_whirl != null) _whirl.Opacity = 0; sc.ScaleX = sc.ScaleY = 1; });
    }

    /// <summary>Your best passed: the well fills to the brim (its water rising) and turns aqua-mint, rings of ripples spreading round it; the glass's caustics stir.</summary>
    protected override void BestPassed(bool held)
    {
        if (_ringArt is not { } rim || _wellWater is not { } water) return;
        double was = _share is > 0 and < 1 ? _share : .84;
        bool beat = _beat;
        var ripples = _wellRipples;
        void Full(bool full) { if (_wellFull == full) return; _wellFull = full; water.InvalidateVisual(); rim.InvalidateVisual(); }
        Full(false);
        // the splash off the brim: from the well's top, where it sits on the glass now
        var at = rim.IsDescendantOf(_line) && rim.ActualWidth > 0 ? rim.TranslatePoint(new Point(22, 16), _line) : new Point(_lineBody.Margin.Left + 22, _lineBody.Margin.Top + 16);
        _lineSplash.At(at.X, at.Y);
        // "Best here" rises out of the well as it brims
        var word = Mark(L.T("Best here"), MarkMint, MarkMintC, size: 10);
        word.Padding = new Thickness(7, 1, 7, 2); word.CornerRadius = new CornerRadius(99); word.Effect = Halo(8, .5);
        var wordT = new TranslateTransform();
        word.RenderTransform = wordT; word.Opacity = 0;
        Canvas.SetLeft(word, Math.Max(2, at.X - 30)); Canvas.SetTop(word, at.Y - 24);
        _lineTop.Children.Clear(); _lineTop.Children.Add(word);
        Motion.Play(rim, 1800, t =>
        {
            word.Opacity = Seg(t, .03, .12) * (1 - Seg(t, .88, 1));
            wordT.Y = 8 * (1 - Back(Seg(t, .03, .25), 2));
            _lineSplash.Step(Seg(t, .14, .7));
            _wellWaterT.Y = WellLevel(was + (1 - was) * Back(Seg(t, 0, .22), 1.4));
            Full(t >= .18);
            ripples?.Step(Seg(t, .12, .85));
            Stir(Seg(t, .1, .9), 1.1, bubbles: false);
        }, () => { Full(beat); _wellWaterT.Y = WellLevel(beat ? 1 : _share); ripples?.Step(-1); Stir(-1, 0); _lineSplash.Step(-1); _lineTop.Children.Clear(); });
    }

    /// <summary>An entry opened: the first timer's capsule lights mint round its rim and breathes once.</summary>
    protected override void EntryOpened(TimerBoard.Item i, bool held)
    {
        if (_firstChip is not { } chip) return;
        var glow = new DropShadowEffect { Color = MintC2, ShadowDepth = 0, BlurRadius = 10, Opacity = 0 };
        chip.Effect = glow;
        var edge = chip.BorderBrush;
        chip.BorderBrush = Frozen(Mod.Mix(MintC2, Colors.White, .25));
        var sc = new ScaleTransform(1, 1);
        chip.RenderTransformOrigin = new Point(.5, .5); chip.RenderTransform = sc;
        Motion.Play(chip, 1300, t =>
        {
            glow.Opacity = .9 * (t < .25 ? Seg(t, 0, .25) : 1 - Seg(t, .25, 1));
            sc.ScaleX = sc.ScaleY = 1 + .05 * Math.Sin(Math.PI * Seg(t, 0, .5));
        }, () => { chip.Effect = null; chip.BorderBrush = edge; chip.RenderTransform = null; });
    }

    /// <summary>A fight started: its glass rises into place as it fades up, the pearl surfaces on the leader, a swell rolls along the bars.</summary>
    protected override void FightStarted(Encounter e, bool held)
    {
        Motion.Play(_fight, 480, t => { double k = Out(t); _settle.Y = 8 * (1 - k); _fight.Opacity = .75 + .25 * Out(Seg(t, 0, .4)); }, () => { _settle.Y = 0; _fight.Opacity = 1; });
        LeaderRow()?.Surface();
        Swell();
    }

    /// <summary>
    /// A fight ended: a plate of the glass saying how ("Killed 12:47", "Wiped") washes in over the head behind a curling crest,
    /// in mint (coral for a wipe), as a swell rolls along the bars and bubbles rise; then it ebbs. It sits on the boss and the
    /// clock (which say the same) and never over the rows. The plate and the crest are drawn once; the wash is a clip and the
    /// crest sliding across, then the plate's opacity.
    /// </summary>
    protected override void FightEnded(Encounter e, bool held)
    {
        if (e.Outcome is not ("kill" or "wipe" or "cleared")) return;
        bool wipe = e.Outcome == "wipe";
        var c = wipe ? CoralC : MintC2;
        double pw = Math.Max(40, (_fight.Width is var fw && double.IsFinite(fw) ? fw : FightWidth - 8) - 2 * (Pad - 4));
        var plate = new Grid { Width = pw, HorizontalAlignment = HorizontalAlignment.Left };
        plate.Children.Add(new TacSketch((dc, s) =>
        {
            if (s.Width < 6 || s.Height < 6) return;
            var r = new Rect(.5, .5, s.Width - 1, s.Height - 1);
            var shape = Mod.Pill(r, r.Height / 2);
            dc.DrawGeometry(Mod.Br(AbyssC, .97), null, shape);
            SeaPanel(dc, s, s.Height / 2, 1, 1.2, 70, shadow: false, tint: c, tintK: .22);
            dc.DrawGeometry(null, Mod.P(Mod.Br(c, .6), 1.2), shape);
        }));
        var words = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        words.Children.Add(Tt(OutcomeText(e), 16, Frozen(Mod.Mix(c, Colors.White, .3)), FontWeights.Bold));
        words.Children.Add(Tt("  " + Clock(e.Duration(e.End ?? DateTime.UtcNow)), 15, White, FontWeights.SemiBold));
        plate.Children.Add(Lit(words));
        // the wash: the plate shown up to its front (a clip slid across), the crest riding the front
        var clipT = new TranslateTransform(-pw - 4, 0);
        plate.Clip = new RectangleGeometry(new Rect(-40, -4, pw + 44, 60)) { Transform = clipT };
        var crestT = new TranslateTransform(-40, 0);
        var crest = new TacSketch((dc, s) =>
        {
            dc.PushClip(Mod.Pill(new Rect(0, 0, s.Width, s.Height), s.Height / 2));
            WaterBar(dc, new Rect(0, 0, 34, s.Height), 30, c, boost: .6, alpha: .9);
            dc.Pop();
        }) { Width = 40, HorizontalAlignment = HorizontalAlignment.Left, RenderTransform = crestT };
        var host = new Grid { VerticalAlignment = VerticalAlignment.Top, HorizontalAlignment = HorizontalAlignment.Left, ClipToBounds = true };
        host.Children.Add(plate); host.Children.Add(crest);
        _fightFx.Children.Clear(); _fightFx.Children.Add(host);
        // over the head as laid out now (the timers can sit above it in a dungeon), a few px proud of it each way
        void Seat()
        {
            double y = _fightBody.Margin.Top, h = 22;
            if (_head is { } hd && hd.ActualHeight > 0 && hd.IsDescendantOf(_fight)) { y = hd.TranslatePoint(new Point(0, 0), _fight).Y; h = hd.ActualHeight; }
            var m = new Thickness(Pad - 4, Math.Max(2, y - 4), 0, 0);
            if (host.Margin != m) host.Margin = m;
            if (host.Height != h + 8) host.Height = h + 8;
        }
        Seat();
        if (!wipe) Swell();
        bool seated = false;
        var head = _head;
        void SplashAt() => _fightSplash.At(host.Margin.Left + pw - 6, host.Margin.Top + (host.Height is var hh && double.IsFinite(hh) ? hh * .7 : 20));
        SplashAt();
        Motion.Play(_fightFx, 2500, t =>
        {
            if (!seated && t > 0) { Seat(); SplashAt(); seated = true; }
            if (!wipe) _fightSplash.Step(Seg(t, .24, .7));
            double front = (pw + 30) * Out(Seg(t, 0, .3));
            clipT.X = front - pw - 4 - 18;
            crestT.X = front - 40;
            crest.Opacity = 1 - Seg(t, .26, .34);
            words.Opacity = 1 - Seg(t, .82, 1);
            plate.Opacity = 1 - Seg(t, .84, 1);
            if (head != null) head.Opacity = t < .84 ? 1 - Seg(t, 0, .08) : Seg(t, .84, 1);
        }, () => { _fightFx.Children.Clear(); _fightSplash.Step(-1); if (head != null) head.Opacity = 1; });
    }
}
