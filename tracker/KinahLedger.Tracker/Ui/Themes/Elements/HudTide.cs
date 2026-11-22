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
    /// <summary>Under the fight's words, over its glass (clipped to it): the wall of water a big moment sweeps across.</summary>
    readonly Canvas _fightUnder = new() { IsHitTestVisible = false };
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
    /// <summary>Over everything, unclipped: the moments' short-lived bits (ElementsFx.cs), each moment on a layer of its own.</summary>
    readonly Canvas _sky = new() { IsHitTestVisible = false };
    /// <summary>A wipe's draining: the deep's dark rising from the panel's foot over the rows (its height by its scale, shown by its opacity).</summary>
    readonly System.Windows.Shapes.Rectangle _fightDeep = new()
    {
        IsHitTestVisible = false, Opacity = 0, Visibility = Visibility.Collapsed, RenderTransformOrigin = new Point(.5, 1), RenderTransform = new ScaleTransform(1, 0),
        Fill = Mod.Grad(true, (Mod.A(AbyssC, 0), 0), (Mod.A(AbyssC, .4), .45), (Mod.A(Color.FromRgb(0x02, 0x08, 0x0C), .62), 1)),
    };
    /// <summary>The Buddy's surge on your best passed: the breaking wave sized to the Buddy, run across its glass.</summary>
    readonly TacSketch _lineWave = new((dc, s) => DrawBigWave(dc, s.Height)) { Width = BigWaveW, Visibility = Visibility.Collapsed };
    readonly TranslateTransform _lineWaveT = new();

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
        _fightUnder.Children.Add(_bigWave); _fightOver.Children.Add(_fightBub.Layer); _fightOver.Children.Add(_fightSplash.Layer);
        _lineFx.Children.Add(_lineSplash.Layer);
        _rare.SizeChanged += (_, e) => ClipRare(e.NewSize);
        _fight.SizeChanged += (_, e) => { _fightOver.Clip = _fightUnder.Clip = PanelShape(e.NewSize, Radius, true); if (Math.Abs(_bigWave.Height - e.NewSize.Height) > .5) _bigWave.Height = e.NewSize.Height; };
        _fight.Children.Add(_fightArt); _fight.Children.Add(_fightUnder); _fight.Children.Add(_fightBody); _fight.Children.Add(_fightDeep); _fight.Children.Add(_fightOver); _fight.Children.Add(_fightFx);
        _fight.SizeChanged += (_, e) => _fightDeep.Clip = PanelShape(e.NewSize, Radius, true);
        _lineWave.RenderTransform = _lineWaveT;
        _lineFx.Children.Insert(0, _lineWave);
        _fight.RenderTransform = _settle;
        _body.Children.Add(_fight); _body.Children.Add(_timersUp); _body.Children.Add(_line); _body.Children.Add(_rare); _body.Children.Add(_note);
        _root.Children.Add(_body); _root.Children.Add(_sky);
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
            // the Abyss's stats so far under the waiting words ("2 kills · 4.4K Kinah": information, never empty glass)
            var ws = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 2, 0) };
            ws.Children.Add(Tt(Waiting(f.Abyss), 13, Soft, FontWeights.SemiBold));
            var wsub = Sub(f, false);
            if (wsub.Inlines.Count > 0) ws.Children.Add(wsub);
            _lineBody.Children.Add(Lit(ws));
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
        // its worth floats up out of the glass on a small capsule of sea glass, then fades (the small one: a rare drop is the big one)
        var worth = p.Worth.Length > 0 ? "+" + p.Worth.TrimStart('+') : p.Count;
        var sp = new StackPanel { Orientation = Orientation.Horizontal };
        sp.Children.Add(Tt(worth, 12.5, p.Worth.Length > 0 ? Frozen(Mod.Mix(MintC2, Colors.White, .3)) : White, FontWeights.Bold));
        var nm = Tt(" " + (p.Name.Length > 14 ? p.Name[..13].TrimEnd() + "…" : p.Name), 9.5, Soft, FontWeights.SemiBold); nm.VerticalAlignment = VerticalAlignment.Center; nm.Margin = new Thickness(2, 1, 0, 0);
        sp.Children.Add(nm);
        var cap = new Border
        {
            Child = Lit(sp), CornerRadius = new CornerRadius(99), Padding = new Thickness(8, 1, 9, 2), IsHitTestVisible = false,
            Background = Mod.Grad(true, (Mod.A(SeaC, .92), 0), (Mod.A(AbyssC, .94), 1)), BorderThickness = new Thickness(1), BorderBrush = Frozen(Mod.A(AquaC, .6)),
        };
        var rise = new TranslateTransform(); var pop = new ScaleTransform(1, 1);
        cap.RenderTransformOrigin = new Point(.5, .5); cap.RenderTransform = new TransformGroup { Children = { pop, rise } };
        cap.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        // over the glass's top edge, left of the breaking wave (not in a run: its head sits there)
        Canvas.SetLeft(cap, Math.Max(4, w * .62 - cap.DesiredSize.Width / 2)); Canvas.SetTop(cap, -cap.DesiredSize.Height / 2);
        if (_runHead.Visibility != Visibility.Visible) _lineTop.Children.Add(cap);
        Motion.Play(_lineFx, 1700, t =>
        {
            Stir(Seg(t, 0, .88), 1.6);
            rise.Y = 4 - 12 * Out(Seg(t, .06, .5)) - 4 * Seg(t, .5, 1);
            pop.ScaleX = pop.ScaleY = ElemFx.Punch(t, .06, .3, .8, 2);
            cap.Opacity = t < .8 ? Seg(t, .06, .14) : 1 - Seg(t, .8, 1);
        }, () => { Stir(-1, 0); _lineTop.Children.Remove(cap); });
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

    /// <summary>A drop of water thrown up (made once; moved by its transform): the sea's bead, or a teardrop in <paramref name="tint"/>.</summary>
    static FrameworkElement Droplet(double d, Color? tint = null) => tint is { } c
        ? ElemFx.Shape(ElemFx.Drop, Mod.Grad(true, (Mod.Mix(c, Colors.White, .8), 0), (Mod.Mix(c, Colors.White, .3), .6), (Mod.A(c, .85), 1)), d * .66, d)
        : new System.Windows.Shapes.Rectangle { Width = d, Height = d, Fill = DropBrush, IsHitTestVisible = false };

    /// <summary>
    /// A rare drop, the big one: a spout of the grade's colour bursts up out of the tile, its glass blooms in the colour and a
    /// whirlpool turns behind the tile in rings of it, drops of it are thrown up and fall back, and the tile lands big and
    /// settles (a pickup is the quiet stir of light and bubbles in the Buddy's glass).
    /// </summary>
    protected override void RareDrop(Pick p, bool held)
    {
        if (_rareArt is not { } art || _rareGlow is not { } glow || _rareRipples is not { } ripples) return;
        var tile = _rareTile;
        var sc = new ScaleTransform(1, 1);
        if (tile != null) { tile.RenderTransformOrigin = new Point(.5, .5); tile.RenderTransform = sc; }
        var gs = (ScaleTransform)glow.RenderTransform;
        var c = _rareC;
        var bits = new ElemFx.Bits();
        // the spout: a column of the colour standing up out of the tile, a burst of white where it breaks
        bits.Add(ElemFx.Glow(c, 30, 104, .9, .7), new Point(0, -28), new Vector(0, 0), delay: 0, life: .5, s0: .2, s1: 1.05);
        bits.Add(ElemFx.Glow(Colors.White, 48, 44, .95, 1), new Point(0, -2), new Vector(0, 0), delay: .04, life: .26, s0: .4, s1: 1.15);
        double[] angs = { -146, -122, -104, -88, -72, -56, -36, -14 }, dist = { 18, 32, 46, 52, 52, 46, 40, 34 };
        for (int i = 0; i < angs.Length; i++)
        {
            double a = angs[i] * Math.PI / 180;
            bits.Add(Droplet(i % 3 == 0 ? 9 : 7, i % 2 == 0 ? c : null), new Point(Math.Cos(a) * 6, -4 + Math.Sin(a) * 4), new Vector(Math.Cos(a) * dist[i], Math.Sin(a) * dist[i] * 1.15),
                g: 40, delay: .06 + .014 * i, life: .55, s0: 1, s1: .7);
        }
        // the whole glass takes it: a flash of the grade's colour over it, and it lands past its size and settles
        double rw = _rare.ActualWidth > 0 ? _rare.ActualWidth : 240;
        bits.Add(ElemFx.Glow(c, rw * 1.05, 60, .6, .3), new Point(rw / 2 - 23, 0), new Vector(0, 0), delay: .03, life: .4, s0: .7, s1: 1);
        var cardK = new ScaleTransform(1, 1);
        _rare.RenderTransformOrigin = new Point(0, .5); _rare.RenderTransform = cardK;
        ElemFx.Play(_sky, bits, (FrameworkElement?)tile ?? art, .5, .5, 1700, t =>
        {
            cardK.ScaleX = cardK.ScaleY = t < .03 ? .9 : ElemFx.Punch(t, .03, .32, .9, 2.6);
            double b = Math.Sin(Math.PI * Math.Min(1, t * 1.6));
            glow.Opacity = Math.Max(b, t < .06 ? t / .06 : 0); gs.ScaleX = gs.ScaleY = .5 + .5 * Out(Seg(t, 0, .35));
            ripples.Step(Seg(t, .02, .9));
            if (_whirl is { } wh && _whirlR is { } wr) { wh.Opacity = t < .08 ? t / .08 : 1 - Seg(t, .7, 1); wr.Angle = -360 * Out(t); }
            sc.ScaleX = sc.ScaleY = t < .04 ? .3 : ElemFx.Punch(t, .04, .38, .3, 3);
        }, () => { glow.Opacity = 0; ripples.Step(-1); if (_whirl != null) _whirl.Opacity = 0; sc.ScaleX = sc.ScaleY = 1; cardK.ScaleX = cardK.ScaleY = 1; });
    }

    /// <summary>
    /// Your best passed: the well surges to the brim (its water rising past it) and turns aqua-mint, it swells and bursts with
    /// a splash, rings of ripples spreading round it; then a wave surges across the Buddy's glass, drops are thrown up off the
    /// well and fall back, and "Best here" rises out of it.
    /// </summary>
    protected override void BestPassed(bool held)
    {
        if (_ringArt is not { } rim || _wellWater is not { } water) return;
        double was = _share is > 0 and < 1 ? _share : .84;
        bool beat = _beat;
        var ripples = _wellRipples;
        void Full(bool full) { if (_wellFull == full) return; _wellFull = full; water.InvalidateVisual(); rim.InvalidateVisual(); }
        Full(false);
        var well = rim.Parent as FrameworkElement;
        var swell = new ScaleTransform(1, 1);
        if (well != null) { well.RenderTransformOrigin = new Point(.5, .5); well.RenderTransform = swell; }
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
        // the wave across the glass, sized to it
        double lw = _line.ActualWidth > 0 ? _line.ActualWidth : 300, lh = _line.ActualHeight > 0 ? _line.ActualHeight : 60;
        if (Math.Abs(_lineWave.Height - lh) > .5 || double.IsNaN(_lineWave.Height)) _lineWave.Height = lh;
        // drops thrown up off the well, out to the right and up (the well sits at the window's left)
        var bits = new ElemFx.Bits();
        bits.Add(ElemFx.Glow(Colors.White, 64, 64, .85, 1), new Point(0, 0), new Vector(0, 0), delay: .14, life: .26, s0: .4, s1: 1.1);
        bits.Add(ElemFx.Ring(FoamC, 44, 44, 2), new Point(0, 0), new Vector(0, 0), delay: .16, life: .4, s0: .9, s1: 1.6);
        double[] angs = { -140, -116, -96, -78, -60, -40, -20 }, dist = { 22, 34, 40, 44, 46, 46, 42 };
        for (int i = 0; i < angs.Length; i++)
        {
            double a = angs[i] * Math.PI / 180;
            bits.Add(Droplet(i % 3 == 0 ? 8 : 6), new Point(Math.Cos(a) * 14, Math.Sin(a) * 14), new Vector(Math.Cos(a) * dist[i], Math.Sin(a) * dist[i]),
                g: 36, delay: .16 + .012 * i, life: .5, s0: 1, s1: .7);
        }
        ElemFx.Play(_sky, bits, well ?? rim, .5, .5, 1800);
        Motion.Play(rim, 1800, t =>
        {
            word.Opacity = Seg(t, .2, .3) * (1 - Seg(t, .88, 1));
            wordT.Y = 8 * (1 - ElemFx.Back(Seg(t, .2, .42), 2));
            _lineSplash.Step(Seg(t, .14, .7));
            _wellWaterT.Y = WellLevel(was + (1 - was) * ElemFx.Back(Seg(t, 0, .18), 1.4));
            Full(t >= .16);
            // a breath in as it fills, then it bursts out and settles
            swell.ScaleX = swell.ScaleY = t < .14 ? 1 - .08 * Out(Seg(t, 0, .14)) : ElemFx.Punch(t, .14, .46, .92, 3.4);
            ripples?.Step(Seg(t, .12, .85));
            Stir(Seg(t, .1, .9), 1.1, bubbles: false);
            // the surge: the wave runs across the glass behind the words
            double k = Seg(t, .24, .7), e = k * k * (3 - 2 * k);
            bool on = k is > 0 and < 1;
            _lineWave.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
            if (on) { _lineWaveT.X = -BigWaveW + (lw + BigWaveW + 12) * e; _lineWave.Opacity = .8 * Seg(k, 0, .08) * (1 - Seg(k, .85, 1)); }
        }, () =>
        {
            Full(beat); _wellWaterT.Y = WellLevel(beat ? 1 : _share); ripples?.Step(-1); Stir(-1, 0); _lineSplash.Step(-1); _lineTop.Children.Clear();
            swell.ScaleX = swell.ScaleY = 1; _lineWave.Visibility = Visibility.Collapsed;
        });
    }

    /// <summary>
    /// An entry opened: the first timer's capsule rides in on a swell. It dips and rises up past its place on the water, a
    /// mint glow and a ripple ring spreading under it, a few drops thrown up off it falling back; then it rests.
    /// </summary>
    protected override void EntryOpened(TimerBoard.Item i, bool held)
    {
        if (_firstChip is not { } chip) return;
        var edge = chip.BorderBrush;
        chip.BorderBrush = Frozen(Mod.Mix(MintC2, Colors.White, .25));
        var sc = new ScaleTransform(1, 1); var lift = new TranslateTransform();
        chip.RenderTransformOrigin = new Point(.5, .5); chip.RenderTransform = new TransformGroup { Children = { sc, lift } };
        double hw = chip.ActualWidth > 0 ? chip.ActualWidth / 2 : 46, hh = chip.ActualHeight > 0 ? chip.ActualHeight / 2 : 12;
        var bits = new ElemFx.Bits();
        bits.Add(ElemFx.Glow(MintC2, 2 * hw + 20, 2 * hh + 24, .7, .5), new Point(0, 0), new Vector(0, 0), delay: .06, life: .5, s0: .7, s1: 1.05);
        bits.Add(ElemFx.Glow(FoamC, 2 * hw + 10, 14, .8, 1), new Point(0, -hh + 1), new Vector(0, 0), delay: .08, life: .22, s0: .5, s1: 1.05);
        double[] xs = { -.62, -.34, -.08, .18, .42, .66 };
        for (int k = 0; k < xs.Length; k++)
            bits.Add(Droplet(k % 2 == 0 ? 7 : 5), new Point(hw * xs[k], -hh + 1), new Vector(xs[k] * 12, -(15 + 5 * (k % 3))), g: 40, delay: .08 + .012 * k, life: .46, s0: 1, s1: .7);
        ElemFx.Play(_sky, bits, chip, .5, .5, 1300, t =>
        {
            // it surfaces: up out of the water from under its place, breaking the surface in a crown of drops, bobbing to rest
            chip.Opacity = Seg(t, 0, .08);
            lift.Y = t < .01 ? 10 : 10 - 10 * ElemFx.Back(Seg(t, .01, .3), 2.2);
            sc.ScaleX = sc.ScaleY = ElemFx.Punch(t, .01, .3, .88, 2);
        }, () => { chip.BorderBrush = edge; chip.Opacity = 1; sc.ScaleX = sc.ScaleY = 1; lift.Y = 0; });
    }

    /// <summary>A fight started: its glass rises into place on a swell (past it and back), the pearl surfaces on the leader, a wall of water sweeps the bars.</summary>
    protected override void FightStarted(Encounter e, bool held)
    {
        Motion.Play(_fight, 700, t => { _settle.Y = 8 - 8 * ElemFx.Back(t, 2); _fight.Opacity = .75 + .25 * Out(Seg(t, 0, .3)); }, () => { _settle.Y = 0; _fight.Opacity = 1; });
        LeaderRow()?.Surface();
        Swell();
    }

    /// <summary>
    /// A fight ended. A kill is a wave breaking: a plate of the glass saying how ("Killed 12:47") washes in over the head
    /// behind a curling crest in mint, the panel surges up on it and settles, the words land big as the crest breaks into a
    /// splash and a flash of foam, a ripple runs out along the plate, and the wall of water sweeps the bars. A wipe is the tide
    /// going out: the plate sinks in slowly in coral, the deep's dark rises from the panel's foot over the rows as it sinks,
    /// a few bubbles drift up; then the water comes back. Either ebbs after a moment.
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
        var lit = Lit(words);
        var wordsK = new ScaleTransform(1, 1); var wordsY = new TranslateTransform();
        lit.RenderTransformOrigin = new Point(.5, .5); lit.RenderTransform = new TransformGroup { Children = { wordsK, wordsY } };
        plate.Children.Add(lit);
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
        var plateY = new ScaleTransform(1, 1);
        plate.RenderTransformOrigin = new Point(.5, .5); plate.RenderTransform = plateY;
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
        bool seated = false;
        var head = _head;
        void SplashAt() => _fightSplash.At(host.Margin.Left + pw - 6, host.Margin.Top + (host.Height is var hh && double.IsFinite(hh) ? hh * .7 : 20));
        SplashAt();
        // a kill's foam flash where the crest breaks, and the ripple running out along the plate; a wipe's bubbles drifting up
        var bits = new ElemFx.Bits();
        if (!wipe)
        {
            // the wave crashing down over the meter: a wall of foam falling from the plate to the foot, a burst of white where it hit
            double fh = _fight.ActualHeight > 0 ? _fight.ActualHeight : 200;
            var front = new System.Windows.Shapes.Rectangle { Width = pw * .96, Height = 5, RadiusX = 2.5, RadiusY = 2.5, Fill = Mod.Grad(false, (Mod.A(FoamC, 0), 0), (Mod.A(Colors.White, .95), .3), (Mod.A(Colors.White, .95), .7), (Mod.A(FoamC, 0), 1)) };
            bits.Add(front, new Point(pw / 2, 10), new Vector(0, fh - 64), delay: .1, life: .3, s0: 1, s1: 1, peak: .95);
            bits.Add(ElemFx.Glow(AquaC, pw * .95, 34, .5, .3), new Point(pw / 2, 4), new Vector(0, fh - 64), delay: .11, life: .32, s0: 1, s1: 1, peak: .8);
            bits.Add(ElemFx.Glow(Colors.White, 90, 30, 1, 1), new Point(60, 0), new Vector(0, 0), delay: .07, life: .12, s0: .6, s1: 1.1);
            bits.Add(ElemFx.Glow(FoamC, 110, 48, .9, 1), new Point(pw - 74, 0), new Vector(0, 0), delay: .16, life: .26, s0: .4, s1: 1.1);
        }
        else
            for (int i = 0; i < 6; i++)
                bits.Add(new System.Windows.Shapes.Rectangle { Width = 7 + 3 * (i % 2), Height = 7 + 3 * (i % 2), Fill = BubbleBrush }, new Point(pw * (.1 + .16 * i), 150 - 14 * (i % 3)), new Vector(4 * Math.Sin(i * 1.7), -70 - 10 * (i % 2)),
                    delay: .16 + .06 * i, life: .6, s0: .8, s1: 1, peak: .85);
        bits.Shift = new Vector(-4, 0);
        ElemFx.Play(_sky, bits, (FrameworkElement?)head ?? _fight, 0, .5, 2500);
        var deepK = (ScaleTransform)_fightDeep.RenderTransform;
        Motion.Play(_fightFx, 2500, t =>
        {
            if (!seated && t > 0) { Seat(); SplashAt(); seated = true; }
            if (!wipe)
            {
                // the crest races over the head and breaks: the words land big, the wave crashes down the meter, the panel takes it
                _fightSplash.Step(Seg(t, .14, .6));
                double front = (pw + 30) * Out(Seg(t, 0, .16));
                clipT.X = front - pw - 4 - 18;
                crestT.X = front - 40;
                crest.Opacity = 1 - Seg(t, .14, .22);
                _settle.Y = 5 * ElemFx.Hit(t, .1, .16, .45);
                wordsK.ScaleX = wordsK.ScaleY = ElemFx.Punch(t, .08, .3, 1.3, 2);
            }
            else
            {
                // the tide going out: the plate sinks in slowly with no crest, the deep's dark rises over the rows, the panel sinks
                clipT.X = 0; crest.Opacity = 0;
                wordsY.Y = -8 * (1 - Out(Seg(t, .04, .4)));
                plateY.ScaleY = .78 + .22 * Out(Seg(t, .02, .32));
                double d = Seg(t, .04, .42) * (1 - Seg(t, .7, .94));
                deepK.ScaleY = Out(Seg(t, .04, .5));
                _fightDeep.Opacity = d;
                _fightDeep.Visibility = d > .01 ? Visibility.Visible : Visibility.Collapsed;
                _settle.Y = 4 * Math.Sin(Math.PI * Seg(t, .04, .92));
            }
            words.Opacity = t < .74 ? (wipe ? Seg(t, .04, .2) : Seg(t, .06, .1)) : 1 - Seg(t, .74, .82);
            plate.Opacity = t < .74 ? (wipe ? Seg(t, .03, .2) : 1) : 1 - Seg(t, .74, .84);
            if (head != null) head.Opacity = t < .5 ? 1 - Seg(t, 0, .03) : Out(Seg(t, .82, 1));
        }, () =>
        {
            _fightFx.Children.Clear(); _fightSplash.Step(-1); if (head != null) head.Opacity = 1;
            _fightDeep.Opacity = 0; _fightDeep.Visibility = Visibility.Collapsed; deepK.ScaleY = 0; _settle.Y = 0;
        });
    }
}
