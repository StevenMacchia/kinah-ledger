using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using KinahLedger.Parser;
using KinahLedger.Session;
using static KinahLedger.Ui.HudKit;

namespace KinahLedger.Ui;

/// <summary>
/// Frost (the Elements theme, 2026-11-20): the meter made of ice. Every surface is a slab of glacial ice, deep navy-teal
/// (#0D2A3A to #071621), cut at two opposite corners like a crystal's facet, with facet planes catching a cold light from its
/// top-left, hairline cracks running in from its edges, a lit rim and fern-like rime branching in from its corners
/// (HudFrost.Ice.cs). Each member's bar is translucent crystal in a cool tint of their class's colour, rime along its top
/// edge, an icy-white leading edge cut on a slant; the leader's the clearest and brightest, crowned with a snowflake that
/// crystallises when the lead changes (HudFrost.Rows.cs). Figures crisp white with a faint cyan halo, names white with a
/// tight dark shade. The Buddy is one ice slab: the frozen ring with frost growing round your best's share, the hour big in
/// ice-white, the best drop and the last pickups as ice-framed tiles, the session under it, the next timer as an ice chip.
/// The moments are frost and cold light, never motion of a figure: frost creeps along the bars (the rime regrowing left to
/// right) and a cold glint follows, on a fight's start, a kill and a new leader; a pickup's glint crosses the Buddy and its
/// worth comes in on an ice chip; a rare drop's plate freezes out in its grade's colour; your best passed grows the frost on
/// round the ring; an entry opening flashes its chip; a fight's end freezes a plate over its head.
/// </summary>
sealed partial class HudFrost : ModernLook
{
    const double Pad = 10;

    // the room round the look: the panels' drop stays inside the window; 98 at the right for the pickup's chip beside the Buddy
    readonly Grid _root = new() { Margin = new Thickness(26, 48, 98, 24), HorizontalAlignment = HorizontalAlignment.Left };
    readonly StackPanel _body = new() { HorizontalAlignment = HorizontalAlignment.Left };
    double _opacity = 0.9;

    // the Buddy: its slab, the ring, the hour, the session and the timer chips
    readonly Grid _line = new() { HorizontalAlignment = HorizontalAlignment.Left };
    readonly IceArt _lineArt = new(11, 7, 3, crest: .5, icicles: .8);
    readonly TacSketch _lineIce;
    readonly StackPanel _lineBody = new() { Orientation = Orientation.Horizontal, Margin = new Thickness(7, 7, 14, 7) };
    readonly Canvas _lineAnchor = Tac.Anchor(HorizontalAlignment.Right, VerticalAlignment.Center);
    readonly StackPanel _timers = new() { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
    readonly Border _timersInLine = new() { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 0, 0) };
    /// <summary>The timer chips over the line, when the pickup strip takes their room on it.</summary>
    readonly Border _timersUp = new() { HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(2, 0, 0, 26), Visibility = Visibility.Collapsed };
    /// <summary>The pickup strip in Frost: 24 px tiles in ice frames beside the hour, a frost rule, an ice-white star.</summary>
    static readonly PickStrip.Look Strip = new(24, 6, true, Frozen(Mod.A(RimC, .42)), Frozen(Color.FromRgb(0xD8, 0xF6, 0xFF)));
    bool _dim;
    TacSketch? _ringArt;
    double _share = -1;
    bool _beat;
    // the Buddy's moments, painted once and moved: a glint crossing its ice, a frost breath over it, the ring's burst and orbiting glint
    readonly Canvas _lineGlintHost = new() { IsHitTestVisible = false };
    readonly System.Windows.Shapes.Path _lineGlint = Glint(BandGeo, BandBrush);
    readonly TacSketch _lineBreath;
    double _lineGlintT = -1;
    Grid? _ringBox;
    Border? _ringBurst, _ringSpark, _ringStar;
    RotateTransform? _ringOrbit;
    Grid? _firstChip;
    TacSketch? _firstChipGlow;

    // the rare drop under the Buddy, and the notice line
    readonly Grid _rare = new() { HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 20, 0, 0), Visibility = Visibility.Collapsed };
    readonly IceArt _rareArt = new(7, 3, 11, icicles: .6);
    readonly Grid _note = new() { HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 20, 0, 0), Visibility = Visibility.Collapsed };
    readonly IceArt _noteArt = new(5, 2, 13);
    readonly StackPanel _noteBody = new() { Orientation = Orientation.Horizontal, Margin = new Thickness(10, 4, 12, 5) };

    // the fight: one slab of ice, its head, HP, rows and foot on it
    readonly Grid _fight = new() { HorizontalAlignment = HorizontalAlignment.Left, Visibility = Visibility.Collapsed, Margin = new Thickness(0, 0, 0, 24) };
    readonly IceArt _fightArt = new(12, 8, 1, crest: 1, icicles: .9) { IcicleFrom = .22 };
    readonly TacSketch _fightIce, _fightBreath;
    readonly StackPanel _fightBody = new() { Margin = new Thickness(Pad, 9, Pad, 9) };
    readonly Border _timersInFight = new() { Margin = new Thickness(0, 0, 0, 7), HorizontalAlignment = HorizontalAlignment.Left };
    readonly Grid _fightFx = new() { IsHitTestVisible = false };
    readonly TranslateTransform _fightShift = new();
    FrameworkElement? _head;
    Canvas? _shards;

    public HudFrost() : base(bare: true)
    {
        // the look's own rows (Ui/Themes/ThemeRows.cs): each a bar of ice on a slab of dark ice
        UseRows(FrostRow.Pitch, m => new FrostRow(this), 15, GapLine);
        _lineIce = new TacSketch((dc, s) => IcePanel(dc, s, _lineArt, _opacity * (_dim ? .7 : 1), rimeK: _dim ? .5 : 1));
        _lineBreath = FrostBreath(_lineArt);
        _lineGlintHost.Children.Add(_lineGlint);
        _line.Children.Add(_lineIce); _line.Children.Add(_lineBreath); _line.Children.Add(_lineGlintHost); _line.Children.Add(_lineBody); _line.Children.Add(_lineAnchor);
        _line.SizeChanged += (_, e) => { _lineArt.Fit(e.NewSize); _lineGlintHost.Clip = _lineArt.Shape; PlaceLineGlint(); };
        _fightIce = new TacSketch((dc, s) => IcePanel(dc, s, _fightArt, _opacity));
        _fightBreath = FrostBreath(_fightArt);
        _fight.Children.Add(_fightIce); _fight.Children.Add(_fightBreath); _fight.Children.Add(_fightBody); _fight.Children.Add(_fightFx); _fight.Children.Add(_frostRun);
        _fight.RenderTransform = _fightShift;
        _note.Children.Add(new TacSketch((dc, s) => IcePanel(dc, s, _noteArt, _opacity, shadow: false, rimeK: .6)));
        _note.Children.Add(_noteBody);
        _runHeadLit = Lit(_runHead); _runHeadLit.Visibility = Visibility.Collapsed;
        _body.Children.Add(_fight); _body.Children.Add(_timersUp); _body.Children.Add(_runHeadLit); _body.Children.Add(_line); _body.Children.Add(_rare); _body.Children.Add(_note);
        _root.Children.Add(_body);
        System.Windows.Documents.TextElement.SetFontFamily(_root, Mod.Face);
        TextOptions.SetTextFormattingMode(_root, TextFormattingMode.Display);
    }

    public override FrameworkElement Root => _root;
    public override Thickness Inset => _root.Margin;
    public override double Corner => 4;
    public override FrameworkElement? FightFrame => _fight.Visibility == Visibility.Visible ? _fight : null;

    public override void SetOpacity(double opacity)
    {
        // the ice takes the setting (never so clear the words lose their dark ground)
        _opacity = Math.Clamp(opacity, 0.3, 1);
        _lineIce.InvalidateVisual(); _fightIce.InvalidateVisual();
        foreach (var c in _note.Children.OfType<TacSketch>()) c.InvalidateVisual();
    }

    protected override string LineSig(Figures f) => base.LineSig(f) + "|" + (Rare(Values)?.Id ?? "") + "|" + (Picks(Values).FirstOrDefault()?.Id ?? "") + "|" + Dimmed(Values) + "|" + PickStrip.Sig(Values, Strip) + "|" + RunBuddy.Sig(Values);

    static bool Dimmed(IReadOnlyDictionary<string, string> v) => (v.TryGetValue("show", out var s) && s == "dim") || (v.TryGetValue("paused", out var p) && p.Length > 0);

    // ================================================================ the Buddy

    protected override void DrawLine(Figures f)
    {
        _lineBody.Children.Clear();
        _ringArt = null; _timerUp = false; _stripTimerW = -1;
        if (_timersInLine.Child != null) _timersInLine.Child = null;
        _dim = Dimmed(Values);
        _lineBody.Opacity = _dim ? .66 : 1;
        bool mark = f.Abyss && f.Counted && f.Icon.Length > 0;
        if (mark) { var m = AbyssMark(f.Icon, 36); m.Margin = new Thickness(2, 0, 9, 0); _lineBody.Children.Add(m); }
        // in a dungeon run: the run's clock, splits and verdict in place of the hour (HudFrost.Run.cs)
        var rs = RunNow();
        _runHeadLit.Visibility = Visibility.Collapsed;
        if (rs != null) DrawRun(rs);
        else if (!HasHour(f))
        {
            var w = Tt(Waiting(f.Abyss), 12.5, Soft, FontWeights.SemiBold); w.Margin = new Thickness(mark ? 0 : 7, 6, 0, 6);
            _lineBody.Children.Add(w);
        }
        else
        {
            if (HasRing(f))
            {
                _share = f.Share; _beat = f.Beat;
                var ring = new Grid { Width = 44, Height = 44, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0), RenderTransformOrigin = new Point(.5, .5) };
                _ringArt = new TacSketch(DrawRing, 44, 44);
                ring.Children.Add(_ringArt);
                var pc = Tt(f.Beat ? "★" : Pct(f.Share), 13, White, FontWeights.Bold); pc.HorizontalAlignment = HorizontalAlignment.Center;
                ring.Children.Add(pc);
                // your best passed: a ring of frost flung out and a glint running once round (hidden until then)
                _ringBurst = Piece(FrostRing, 56, 56, 0); _ringBurst.Margin = new Thickness(-6); _ringBurst.Visibility = Visibility.Hidden; _ringBurst.RenderTransform = new ScaleTransform(1, 1);
                _ringSpark = Piece(SparkleBrush, 13, 13, 0); _ringSpark.VerticalAlignment = VerticalAlignment.Top; _ringSpark.Margin = new Thickness(0, -1, 0, 0); _ringSpark.Visibility = Visibility.Hidden;
                _ringOrbit = new RotateTransform();
                ring.Children.Add(_ringBurst);
                // and a starburst of ice crystals flung out behind it
                _ringStar = Piece(Starburst, 96, 96, 0); _ringStar.Margin = new Thickness(-26); _ringStar.Visibility = Visibility.Hidden; _ringStar.RenderTransform = new ScaleTransform(1, 1);
                ring.Children.Insert(0, _ringStar);
                ring.Children.Add(new Grid { IsHitTestVisible = false, RenderTransform = _ringOrbit, RenderTransformOrigin = new Point(.5, .5), Children = { _ringSpark } });
                _ringBox = ring;
                _lineBody.Children.Add(ring);
            }
            else if (!mark) _lineBody.Children.Add(new Border { Width = 6 });
            // the hour big in ice-white, its unit small; the session and the latest pickup under it
            var words = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 1, 0, 1) };
            var top = new StackPanel { Orientation = Orientation.Horizontal };
            var rate = Tt(f.Rate, f.Warm ? 15 : 22, f.Warm ? Soft : White, f.Warm ? FontWeights.SemiBold : FontWeights.Bold);
            rate.Tag = "buddy-rate";
            if (!f.Warm) rate.Effect = ColdGlow(.6);
            top.Children.Add(rate);
            if (f.Unit.Length > 0) { var u = Tt(f.Unit, 11, IceInk, FontWeights.SemiBold); u.VerticalAlignment = VerticalAlignment.Bottom; u.Margin = new Thickness(2, 0, 0, 4); top.Children.Add(u); }
            words.Children.Add(top);
            // farming, the pickups as the strip beside the hour where it fits (the best drop and the last three), the session
            // alone under it; with the timer chips at the line's end and no room left beside them, the chips go over the line
            var sub = Sub(f, true);
            FrameworkElement? strip = null, under = null;
            if (StripTime(f))
            {
                var bare = Sub(f, false);
                double baseW = _lineBody.Margin.Left + _lineBody.Margin.Right + _lineBody.Children.OfType<FrameworkElement>().Sum(Mod.W), timerW = LineTimerW();
                double wordsW = Math.Max(Mod.W(top), Mod.W(bare)), oldW = Math.Max(Mod.W(top), Mod.W(sub));
                foreach (var look in StripFall(Strip))
                {
                    if ((strip = StripIn(StripRoom(FightWidth - 10, baseW + timerW, wordsW, oldW, 12), look)) != null) break;
                    if (timerW > 0 && (strip = StripIn(StripRoom(FightWidth - 10, baseW, wordsW, oldW + timerW, 12), look)) != null) { _timerUp = true; break; }
                }
                if (strip != null) { sub = bare; strip.Margin = new Thickness(12, 0, 0, 0); IceFramed(strip, Strip.Size); }
                // no room beside the hour: under the session, never dropped (Steven, 2026-11-19)
                else if ((under = StripUnder(Strip, false)) != null) { sub = bare; IceFramed(under, Strip.Size); }
                _stripTimerW = timerW;
            }
            if (sub.Inlines.Count > 0) words.Children.Add(sub);
            if (under != null) words.Children.Add(under);
            _lineBody.Children.Add(words);
            if (strip != null) _lineBody.Children.Add(strip);
        }
        _lineBody.Children.Add(_timersInLine);
        PlaceTimers();
        _lineIce.InvalidateVisual();

        // the rare drop the window holds: a plate of ice lit in its grade's colour, its tile in an ice frame, RARE DROP and its name
        _rare.Children.Clear();
        if (!KinahHidden && Rare(Values) is { } r)
        {
            var c = ColorOf(r.Color, r.Grade);
            var plate = new TacSketch((dc, s) => IcePanel(dc, s, _rareArt, _opacity, tint: c));
            _rareWash = plate;
            _rare.Children.Add(plate);
            _rareFlashEl = FrostBreath(_rareArt, c);
            _rare.Children.Add(_rareFlashEl);
            // its moment's cold glint, clipped to the plate's shape once it has its size
            var glintHost = new Canvas { IsHitTestVisible = false };
            _rareGlint = Glint(BandGeo, BandBrush);
            glintHost.Children.Add(_rareGlint);
            _rare.Children.Add(glintHost);
            if (_rareSized != null) _rare.SizeChanged -= _rareSized;
            _rareSized = (_, e) => { _rareArt.Fit(e.NewSize); glintHost.Clip = _rareArt.Shape; PlaceRareGlint(); };
            _rare.SizeChanged += _rareSized;
            if (_rare.ActualWidth > 0) { _rareArt.Fit(_rare.RenderSize); glintHost.Clip = _rareArt.Shape; }
            var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(7, 5, 14, 5) };
            var tile = IceFramed(new Grid { Children = { Tile(r, 26) } }, 26); tile.Margin = new Thickness(0, 0, 9, 0);
            _rareTile = tile;
            row.Children.Add(tile);
            var mk = Chip(L.T("RARE DROP"), size: 8, edge: Mod.Mix(c, Colors.White, .4)); mk.Margin = new Thickness(0, 0, 7, 0);
            row.Children.Add(mk);
            var nm = Tt(r.Name, 13, Frozen(Mod.Mix(c, Colors.White, .5)), FontWeights.Bold);
            row.Children.Add(nm);
            if (r.Count.Length > 0) { var cn = Tt(" " + r.Count, 11, Soft, FontWeights.SemiBold); cn.Margin = new Thickness(2, 1, 0, 0); row.Children.Add(cn); }
            _rare.Children.Add(row);
            _rareRow = row;
            _rare.Visibility = Visibility.Visible;
        }
        else { _rare.Visibility = Visibility.Collapsed; _rareWash = null; _rareTile = null; }

        // the notice: its words in their tone on a strip of ice under the Buddy, the warning sign before a pause
        _noteBody.Children.Clear();
        if (f.Note.Length > 0 && !KinahHidden)
        {
            bool warn = ReferenceEquals(f.NoteTone, Warn);
            if (warn) { var wi = WarnIcon(); wi.Width = wi.Height = 13; wi.Margin = new Thickness(0, 0, 5, 0); _noteBody.Children.Add(wi); }
            var tone = warn ? WarnC2 : ReferenceEquals(f.NoteTone, Gold) ? GoldC2 : ReferenceEquals(f.NoteTone, Mint) ? MintC2 : Color.FromRgb(0xEE, 0xF8, 0xFF);
            var t = Tt(f.Note, 11.5, Frozen(tone), FontWeights.SemiBold); t.TextTrimming = TextTrimming.CharacterEllipsis; t.MaxWidth = 380;
            _noteBody.Children.Add(t);
            _note.Visibility = Visibility.Visible;
        }
        else _note.Visibility = Visibility.Collapsed;
    }

    /// <summary>The line under the hour: the session, the latest pickup and its worth (<paramref name="pick"/>); in the Abyss its stats.</summary>
    TextBlock Sub(Figures f, bool pick)
    {
        var sub = new TextBlock { FontFamily = Mod.Face, FontSize = 10.5, FontWeight = FontWeights.Medium, Foreground = Soft, Margin = new Thickness(1, 1, 0, 0) };
        System.Windows.Documents.Typography.SetNumeralAlignment(sub, FontNumeralAlignment.Tabular);
        void Dot() { if (sub.Inlines.Count > 0) sub.Inlines.Add(new System.Windows.Documents.Run("  ·  ") { Foreground = Quiet }); }
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
    double LineTimerW() => _timers.Children.Count > 0 && !KinahHidden ? 10 + Mod.W(_timers) : 0;

    /// <summary>The timers' room the line's strip was fitted to (-1: no strip time); chips that change it draw the line again.</summary>
    double _stripTimerW = -1;

    /// <summary>The timer chips sit over the line, to leave its room to the pickup strip.</summary>
    bool _timerUp;

    TacSketch? _rareWash, _rareFlashEl;
    System.Windows.Shapes.Path? _rareGlint;
    FrameworkElement? _rareRow;
    SizeChangedEventHandler? _rareSized;
    double _rareGlintT = -1;

    void PlaceRareGlint()
    {
        if (_rareGlint is not { } g) return;
        double t = _rareGlintT, w = _rare.ActualWidth;
        if (t is < 0 or > 1 || w <= 0) { Show(g, 0); return; }
        Show(g, Math.Sin(Math.PI * t));
        var tt = (TranslateTransform)g.RenderTransform; tt.X = -70 + (w + 140) * t; tt.Y = -30;
    }
    FrameworkElement? _rareTile;

    /// <summary>
    /// The frozen ring: a disc of dark ice in a ring of frost, your best's share as an arc of ice-white light with frost
    /// crystals growing outward along it and a glint at its end (mint ice once beaten).
    /// </summary>
    void DrawRing(DrawingContext dc, Size s)
    {
        double c = s.Width / 2, r = c - 5.5;
        var center = new Point(c, c);
        var disc = new RadialGradientBrush(Mod.A(TopC, .95), Mod.A(DeepC, .95)) { Center = new Point(.4, .3), GradientOrigin = new Point(.4, .3) };
        disc.Freeze();
        dc.DrawEllipse(disc, Mod.P(Mod.Br(Colors.Black, .45), 1), center, r + 2.6, r + 2.6);
        dc.DrawEllipse(null, Mod.P(Mod.Br(IceC, .2), 3.2), center, r, r);
        double share = Math.Clamp(_beat ? 1 : _share, 0, 1);
        double shown = share;
        var tone = _beat ? MintC2 : FrostC;
        if (shown < .004) return;
        double end = 2 * Math.PI * Math.Min(shown, .9999);
        Geometry arcG;
        if (shown >= .999) arcG = new EllipseGeometry(center, r, r);
        else
        {
            var fig = new PathFigure { StartPoint = new Point(c, c - r) };
            fig.Segments.Add(new ArcSegment(new Point(c + r * Math.Sin(end), c - r * Math.Cos(end)), new Size(r, r), 0, end > Math.PI, SweepDirection.Clockwise, true));
            var pg = new PathGeometry(); pg.Figures.Add(fig); arcG = pg;
        }
        arcG.Freeze();
        dc.DrawGeometry(null, Mod.P(Mod.Br(CyanC, .32), 6, round: true), arcG);
        dc.DrawGeometry(null, Mod.P(Frozen(tone), 3.2, round: true), arcG);
        // frost growing round the share: crystals at an uneven spacing along the arc, pointing out, each with a pair of twigs
        var spike = Mod.P(Mod.Br(tone, .85), .75, round: true);
        int k = 0;
        for (double a = .2; a < end - .08; k++)
        {
            double l = k % 3 == 1 ? 4.6 : k % 3 == 2 ? 2.6 : 3.6;
            Point At(double ang, double rr) => new(c + Math.Sin(ang) * rr, c - Math.Cos(ang) * rr);
            dc.DrawLine(spike, At(a, r + 1.6), At(a, r + 1.6 + l));
            if (l > 3) { dc.DrawLine(spike, At(a, r + 1.6 + l * .5), At(a + .1, r + 1.6 + l * .85)); dc.DrawLine(spike, At(a, r + 1.6 + l * .5), At(a - .1, r + 1.6 + l * .85)); }
            a += k % 2 == 0 ? .42 : .55;
        }
        if (shown < .999) Sparkle(dc, new Point(c + r * Math.Sin(end), c - r * Math.Cos(end)), 4, .95);
    }

    protected override void KinahShown(bool shown)
    {
        _line.Visibility = shown ? Visibility.Visible : Visibility.Collapsed;
        if (!shown) { _note.Visibility = Visibility.Collapsed; _rare.Visibility = Visibility.Collapsed; }
        _fight.Margin = new Thickness(0, 0, 0, shown ? 24 : 0);
        PlaceTimers();
    }

    // ================================================================ the timers

    protected override void DrawTimers(IReadOnlyList<TimerBoard.Item> due, DateTime now)
    {
        _timers.Children.Clear();
        _firstChip = null; _firstChipGlow = null;
        foreach (var i in due.Take(2))
        {
            var (text, tone) = TimerWords(i, now);
            bool first = _timers.Children.Count == 0;
            var c = tone == TimerBadge.Tone.On ? MintC2 : tone == TimerBadge.Tone.Soon ? GoldC2 : IceC;
            var sp = new StackPanel { Orientation = Orientation.Horizontal, Margin = first ? new Thickness(8, 3, 10, 4) : new Thickness(7, 2, 9, 3) };
            double d = first ? 7 : 5.5;
            // a small crystal of the tone's colour: a diamond
            sp.Children.Add(new TacSketch((dc, s) =>
            {
                var g = new StreamGeometry();
                using (var k = g.Open()) { k.BeginFigure(new Point(d / 2, 0), true, true); k.LineTo(new Point(d, d / 2), true, false); k.LineTo(new Point(d / 2, d), true, false); k.LineTo(new Point(0, d / 2), true, false); }
                g.Freeze();
                dc.DrawGeometry(Mod.Grad(true, (Mod.Mix(c, Colors.White, .5), 0), (c, 1)), Mod.P(Mod.Br(DeepC, .6), .6), g);
            }, d, d) { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) });
            var t = Tt(text, first ? 11.5 : 10, first ? White : Soft, first ? FontWeights.Bold : FontWeights.SemiBold);
            if (tone == TimerBadge.Tone.On) t.Foreground = Frozen(Mod.Mix(MintC2, Colors.White, .45));
            sp.Children.Add(t);
            bool on = tone == TimerBadge.Tone.On;
            var chip = new Grid { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(first ? 0 : 5, 0, 0, 0), Tag = first ? "timer-chip" : null };
            var art = new TacSketch((dc, s) =>
            {
                if (s.Width < 6) return;
                var r = new Rect(.5, .5, s.Width - 1, s.Height - 1);
                var sh = Shard(r, 5, 1.5);
                dc.DrawGeometry(Mod.Grad(true, (Mod.A(TopC, .93), 0), (Mod.A(BotC, .95), 1)), null, sh);
                dc.PushClip(sh);
                dc.DrawRectangle(Mod.Grad(false, (Mod.A(Colors.White, .09), 0), (Mod.A(Colors.White, 0), .5)), null, r);
                dc.Pop();
                var e = on ? MintC2 : RimC;
                dc.DrawGeometry(null, Mod.P(Mod.Grad(true, (Mod.A(e, on ? .9 : .65), 0), (Mod.A(e, on ? .5 : .25), 1)), 1), sh);
            });
            chip.Children.Add(art);
            if (first)
            {
                // an entry opening: the chip's mint flash, drawn once and hidden until then
                var glow = new TacSketch((dc, s) =>
                {
                    if (s.Width < 6) return;
                    var sh = Shard(new Rect(.5, .5, s.Width - 1, s.Height - 1), 5, 1.5);
                    dc.DrawGeometry(null, Mod.P(Mod.Br(MintC2, .3), 5), sh);
                    dc.DrawGeometry(Mod.Br(MintC2, .2), Mod.P(Mod.Br(MintC2), 2), sh);
                }) { Opacity = 0, Visibility = Visibility.Hidden };
                chip.Children.Add(glow);
                _firstChip = chip; _firstChipGlow = glow;
            }
            chip.Children.Add(sp);
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
        if (e == null) { _fight.Visibility = Visibility.Collapsed; _fightFx.Children.Clear(); PlaceTimers(); return; }
        double w = FightWidth - 10, inner = w - 2 * Pad;
        _fight.Width = w;
        _fightBody.Children.Add(_timersInFight);
        // the head: the boss, the outcome once it's over, the clock in ice-white
        var head = new Grid();
        head.ColumnDefinitions.Add(new ColumnDefinition()); head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var title = Tt(e.Title, 14, White, FontWeights.SemiBold); title.TextTrimming = TextTrimming.CharacterEllipsis;
        head.Children.Add(title);
        var right = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(8, 0, 0, 0) };
        if (e.Waiting) right.Children.Add(Chip(L.T("READY"), MarkMint, MintC2, size: 9.5));
        else
        {
            var outcome = OutcomeText(e);
            if (outcome.Length > 0)
            {
                bool wipe = e.Outcome == "wipe";
                var oc = Chip(outcome, wipe ? Frozen(Color.FromRgb(0xFF, 0xA8, 0xB4)) : MarkMint, wipe ? CoralC : MintC2, size: 10);
                oc.Margin = new Thickness(0, 1, 8, 0); oc.Tag = "outcome";
                right.Children.Add(oc);
            }
            var clock = Tt(Clock(e, now), 18, White, FontWeights.Bold); clock.Tag = "fight-clock";
            clock.Effect = ColdGlow(.55);
            right.Children.Add(clock);
        }
        Grid.SetColumn(right, 1); head.Children.Add(right);
        _head = head;
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
                // the boss's HP: a sliver of red ice in a frosted groove, notches of ice at the thirds; the figure and what's left under its right end
                var track = new Grid { Height = 6, Margin = new Thickness(0, 7, 0, 0) };
                track.Children.Add(new TacSketch((dc, s) =>
                {
                    var r = new Rect(0, .5, s.Width, 5);
                    var groove = Shard(r, 2.5, 0);
                    dc.DrawGeometry(Mod.Br(DeepC, .9), Mod.P(Mod.Br(RimC, .25), .8), groove);
                    double x1 = s.Width * Math.Clamp(hp, 0, 1);
                    if (x1 > .5)
                    {
                        dc.PushClip(groove);
                        dc.DrawRectangle(Mod.Grad(true, (Mod.Mix(HpC, Colors.White, .45), 0), (HpC, .5), (Mod.Mix(HpC, DeepC, .35), 1)), null, new Rect(0, r.Top, x1, r.Height));
                        dc.DrawRectangle(Mod.Br(Colors.White, .45), null, new Rect(0, r.Top + .8, x1, .8));
                        dc.Pop();
                        dc.DrawLine(Mod.P(Mod.Br(FrostC), 1.2), new Point(x1, r.Top), new Point(x1, r.Bottom));
                    }
                    for (int k = 1; k <= 2; k++)
                    {
                        double x = Math.Round(s.Width * k / 3);
                        var n = new StreamGeometry();
                        using (var c = n.Open()) { c.BeginFigure(new Point(x - 2, r.Top - .5), true, true); c.LineTo(new Point(x + 2, r.Top - .5), true, false); c.LineTo(new Point(x, r.Bottom + .5), true, false); }
                        n.Freeze();
                        dc.DrawGeometry(Mod.Br(DeepC, .95), Mod.P(Mod.Br(RimC, .55), .6), n);
                    }
                }));
                HpGhost(track, e, hp, now, 1.5);
                _fightBody.Children.Add(track);
                var hl = new TextBlock { FontFamily = Mod.Face, FontSize = 10.5, Foreground = Soft, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 2, 0, 0) };
                System.Windows.Documents.Typography.SetNumeralAlignment(hl, FontNumeralAlignment.Tabular);
                hl.Inlines.Add(new System.Windows.Documents.Run($"{Math.Round(hp * 100):0}%") { FontWeight = FontWeights.Bold, Foreground = hp < .34 ? Frozen(Color.FromRgb(0xFF, 0xA0, 0xB0)) : White });
                var left = HpLeftText(e);
                if (left.Length > 0) hl.Inlines.Add(new System.Windows.Documents.Run("  ·  " + left) { FontWeight = FontWeights.Medium });
                hl.Tag = "hp-text";
                        _fightBody.Children.Add(hl);
                _fightBody.Children.Add(new Border { Height = 3 });
            }
            else _fightBody.Children.Add(new Border { Height = 7 });
            PutRows(_fightBody, e, now, inner);
            if (e.HasDamage)
            {
                _fightBody.Children.Add(Hairline(new Thickness(0, 6, 0, 5)));
                _fightBody.Children.Add(Foot(e, now));
            }
        }
        _fight.Visibility = Visibility.Visible;
        _fightIce.InvalidateVisual();
        PlaceTimers();
    }

    // ================================================================ the moments
    // Every moment moves parts painted once (HudFrost.Ice.cs): transforms and opacities, never a redraw or a layout per frame.

    /// <summary>The Buddy's cold glint at <paramref name="t"/> of its crossing (-1: none), placed once the line has its size.</summary>
    void LineGlint(double t) { _lineGlintT = t; PlaceLineGlint(); }

    void PlaceLineGlint()
    {
        double t = _lineGlintT, w = _line.ActualWidth;
        if (t is < 0 or > 1 || w <= 0) { Show(_lineGlint, 0); return; }
        Show(_lineGlint, Math.Sin(Math.PI * t));
        var tt = (TranslateTransform)_lineGlint.RenderTransform;
        tt.X = -70 + (w + 140) * t; tt.Y = -30;
    }

    /// <summary>
    /// A clip that freezes an element out from its left as <paramref name="k"/>'s scale grows 0 to 1: its own size (and a
    /// little room round it), kept to it once it is laid out; only the scale moves.
    /// </summary>
    static RectangleGeometry Reveal(FrameworkElement el, ScaleTransform k)
    {
        var clip = new RectangleGeometry { Transform = k };
        void Fit(Size s) => clip.Rect = new Rect(-12, -12, s.Width + 24, s.Height + 24);
        Fit(el.RenderSize);
        el.SizeChanged += (_, e) => Fit(e.NewSize);
        return clip;
    }

    /// <summary>A pickup: a cold glint crosses the Buddy's ice and the worth comes in on a chip of ice beside it, then goes.</summary>
    protected override void Pickup(Pick p, bool held)
    {
        if (KinahHidden || _line.Visibility != Visibility.Visible) return;
        var worth = p.Worth.Length > 0 ? "+" + p.Worth.TrimStart('+') : p.Count;
        var words = new StackPanel { Margin = new Thickness(9, 3, 11, 4) };
        words.Children.Add(Tt(worth, 14, p.Worth.Length > 0 ? Frozen(Mod.Mix(MintC2, Colors.White, .3)) : White, FontWeights.Bold));
        var nm = Tt(p.Name.Length > 13 ? p.Name[..12].TrimEnd() + "…" : p.Name, 9.5, Soft, FontWeights.SemiBold); nm.Margin = new Thickness(0, -2, 0, 0);
        words.Children.Add(nm);
        var art = new IceArt(6, 2, 17);
        var chip = new Grid { MaxWidth = 88 };
        chip.Children.Add(new TacSketch((dc, s) => IcePanel(dc, s, art, 1, shadow: false, rimeK: .7)));
        chip.Children.Add(words);
        var shift = new TranslateTransform();
        chip.RenderTransform = shift;
        chip.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        Canvas.SetLeft(chip, 6); Canvas.SetTop(chip, -chip.DesiredSize.Height / 2);
        _lineAnchor.Children.Clear(); _lineAnchor.Children.Add(chip);
        Motion.Play(chip, 1900, t =>
        {
            LineGlint(t < .5 ? Seg(t, 0, .5) : -1);
            double k = Out(Seg(t, .1, .3));
            shift.X = -10 * (1 - k);
            chip.Opacity = t < .82 ? k : 1 - Seg(t, .82, 1);
        }, () => { LineGlint(-1); _lineAnchor.Children.Remove(chip); });
    }

    /// <summary>A rare drop: its plate freezes out to the right in the grade's colour (its clip's scale), a flash of frost over it, the tile popping in.</summary>
    protected override void RareDrop(Pick p, bool held)
    {
        if (_rareWash is not { } plate || _rareFlashEl is not { } flash) return;
        var tile = _rareTile;
        var sc = new ScaleTransform(1, 1);
        if (tile != null) { tile.RenderTransformOrigin = new Point(.5, .5); tile.RenderTransform = sc; }
        var reveal = new ScaleTransform(0, 1);
        var clip = Reveal(plate, reveal);
        plate.Clip = clip; flash.Clip = clip;
        var words = _rareRow;
        Motion.Play(plate, 1500, t =>
        {
            reveal.ScaleX = Out(Seg(t, 0, .42));
            if (words != null) words.Opacity = Seg(t, .3, .44);
            _rareGlintT = t is > .3 and < .8 ? Seg(t, .3, .8) : -1; PlaceRareGlint();
            Show(flash, t < .22 ? Seg(t, 0, .22) : 1 - Seg(t, .22, .85));
            sc.ScaleX = sc.ScaleY = .6 + .4 * Back(Seg(t, .08, .42), 2.2);
        }, () => { plate.Clip = null; flash.Clip = null; if (words != null) words.Opacity = 1; Show(flash, 0); sc.ScaleX = sc.ScaleY = 1; _rareGlintT = -1; PlaceRareGlint(); });
    }

    /// <summary>
    /// Your best passed: the frozen ring swells and bursts, a starburst of ice crystals and a ring of frost flung out round
    /// it, a glint running once round it, a breath of frost and a glint crossing the Buddy.
    /// </summary>
    protected override void BestPassed(bool held)
    {
        if (_ringBox is not { } ring || _ringBurst is not { } burst || _ringSpark is not { } spark || _ringOrbit is not { } orbit || _ringStar is not { } star) return;
        var swell = new ScaleTransform(1, 1);
        ring.RenderTransform = swell;
        var bk = (ScaleTransform)burst.RenderTransform;
        var sk = (ScaleTransform)star.RenderTransform;
        Motion.Play(ring, 1800, t =>
        {
            double s0 = Seg(t, .04, .5);
            Show(star, s0 is > 0 and < 1 ? (s0 < .2 ? s0 / .2 : 1 - Seg(s0, .2, 1)) : 0);
            sk.ScaleX = sk.ScaleY = .5 + .52 * Out(s0);
            double up = t < .15 ? Out(Seg(t, 0, .15)) : 1 - Out(Seg(t, .15, .45));
            swell.ScaleX = swell.ScaleY = 1 + .1 * up;
            double k = Seg(t, .1, .6);
            Show(burst, k is > 0 and < 1 ? .9 * (1 - k) : 0);
            bk.ScaleX = bk.ScaleY = .7 + .5 * Out(k);
            double o = Seg(t, .05, .75);
            orbit.Angle = 360 * Out(o);
            Show(spark, o is > 0 and < 1 ? Math.Sin(Math.PI * o) : 0);
            Show(_lineBreath, t < .3 ? .6 * Seg(t, .1, .3) : .6 * (1 - Seg(t, .3, .9)));
            LineGlint(t is > .4 and < .9 ? Seg(t, .4, .9) : -1);
        }, () => { swell.ScaleX = swell.ScaleY = 1; Show(burst, 0); Show(star, 0); Show(spark, 0); orbit.Angle = 0; Show(_lineBreath, 0); LineGlint(-1); });
    }

    /// <summary>An entry opened: the first timer's chip flashes mint through its ice, grows a touch and settles.</summary>
    protected override void EntryOpened(TimerBoard.Item i, bool held)
    {
        if (_firstChip is not { } chip || _firstChipGlow is not { } glow) return;
        var sc = new ScaleTransform(1, 1);
        chip.RenderTransformOrigin = new Point(.5, .5); chip.RenderTransform = sc;
        Motion.Play(chip, 1200, t =>
        {
            double k = t < .2 ? Seg(t, 0, .2) : 1 - Seg(t, .2, 1);
            Show(glow, k);
            sc.ScaleX = sc.ScaleY = 1 + .05 * k;
        }, () => { Show(glow, 0); sc.ScaleX = sc.ScaleY = 1; });
    }

    /// <summary>A fight started: the slab settles in as it fades up, the crown crystallises on the top row, the frost creeps along the bars.</summary>
    protected override void FightStarted(Encounter e, bool held)
    {
        Motion.Play(_fight, 480, t =>
        {
            double k = Out(t);
            _fightShift.Y = 5 * (1 - k);
        }, () => { _fightShift.Y = 0; });
        if (RowsNow().FirstOrDefault() is { } top) top.Crystallise();
        _lastLead = DateTime.UtcNow;
        Frost();
    }

    /// <summary>
    /// A fight ended: a plate of ice freezes out over the fight's head from the left (its clip's scale), with how it went and
    /// the clock (cold white and mint for a kill, its rim coral for a wipe), a flash of frost and a glint crossing it. A kill
    /// shatters: a dozen splinters of ice burst off the plate across the meter, spinning and falling as they fade, and the frost
    /// creeps along the bars. It lifts away after a moment.
    /// </summary>
    protected override void FightEnded(Encounter e, bool held)
    {
        if (e.Outcome is not ("kill" or "wipe" or "cleared")) return;
        bool wipe = e.Outcome == "wipe";
        var c = wipe ? CoralC : MintC2;
        var art = new IceArt(8, 3, 29);
        var band = new Grid { VerticalAlignment = VerticalAlignment.Top };
        band.Children.Add(new TacSketch((dc, s) => IcePanel(dc, s, art, 1, tint: wipe ? CoralC : null)));
        var flash = FrostBreath(art, wipe ? CoralC : null, fill: false);
        var slam = new TranslateTransform();
        band.RenderTransform = slam;
        band.Children.Add(flash);
        var glintHost = new Canvas { IsHitTestVisible = false };
        var glint = Glint(BandGeo, BandBrush);
        glintHost.Children.Add(glint);
        band.Children.Add(glintHost);
        var words = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(12, 0, 12, 0), VerticalAlignment = VerticalAlignment.Center };
        var ot = Tt(OutcomeText(e), 18, White, FontWeights.Bold);
        if (!wipe) ot.Effect = ColdGlow(.7);
        words.Children.Add(ot);
        var ck = Tt("  " + Clock(e.Duration(e.End ?? DateTime.UtcNow)), 12, Frozen(Mod.Mix(c, Colors.White, .45)), FontWeights.SemiBold); ck.Margin = new Thickness(4, 3, 0, 0);
        words.Children.Add(ck);
        band.Children.Add(words);
        var reveal = new ScaleTransform(0, 1);
        band.Clip = Reveal(band, reveal);
        _fightFx.Children.Clear(); _fightFx.Children.Add(band);
        // the shatter: splinters of ice painted once, each only moved, turned and faded
        var shards = new Canvas { IsHitTestVisible = false };
        var pieces = new List<(Border El, TranslateTransform At, RotateTransform Turn, ScaleTransform K, double Ang, double Dist, double Spin)>();
        if (!wipe)
        {
            double[] angs = { -3.35, -3.1, -2.85, -2.6, -2.35, -2.1, -1.9, -1.7, -1.5, -1.3, -1.1, -.9, -.7, -.5, -.3, -.12, .06, .2 };
            for (int i = 0; i < angs.Length; i++)
            {
                double size = i % 3 == 0 ? 1.4 : i % 3 == 1 ? 1 : .7;
                var el = new Border { Width = 20 * size, Height = 8 * size, Background = Splinter, IsHitTestVisible = false, Visibility = Visibility.Hidden, RenderTransformOrigin = new Point(.5, .5) };
                var at = new TranslateTransform(); var turn = new RotateTransform(); var k = new ScaleTransform(1, 1);
                el.RenderTransform = new TransformGroup { Children = { k, turn, at } };
                shards.Children.Add(el);
                pieces.Add((el, at, turn, k, angs[i], 70 + 110 * ((i * 7) % 5) / 4.0, (i % 2 == 0 ? 1 : -1) * (200 + 60 * (i % 3))));
            }
            // under the meter's words (behind the rows and over the slab), and mostly up and out into the window's room
            if (_shards != null) _fight.Children.Remove(_shards);
            _shards = shards;
            _fight.Children.Insert(_fight.Children.IndexOf(_fightBody), shards);
        }
        double shatterT = -1;
        void PlaceShards()
        {
            if (pieces.Count == 0) return;
            double w = _fight.ActualWidth > 0 ? _fight.ActualWidth : FightWidth - 10, h = _fight.ActualHeight > 0 ? _fight.ActualHeight : 160;
            double x0 = Pad + 34, y0 = band.Margin.Top + (double.IsNaN(band.Height) ? 16 : band.Height / 2);
            double l = Seg(shatterT, 0, 1);
            foreach (var (el, at, turn, k, ang, dist, spin) in pieces)
            {
                if (shatterT is < 0 or > 1) { Show(el, 0); continue; }
                // each flies out on its line (kept inside the meter: never past the window), falls a little, spins and fades
                // flung out past the slab's edges, into the window's own room round it (never past the window)
                double ex = Math.Clamp(x0 + Math.Cos(ang) * dist, -18, w + 80), ey = Math.Clamp(y0 + Math.Sin(ang) * dist * .8, -40, h + 4);
                double q = Out(l);
                at.X = x0 + (ex - x0) * q - el.Width / 2; at.Y = y0 + (ey - y0) * q + 12 * l * l - el.Height / 2;
                at.Y = Math.Min(at.Y, h + 4);
                turn.Angle = ang * 57.3 + spin * l;
                k.ScaleX = k.ScaleY = 1.25 - .5 * l;
                Show(el, l < .08 ? l / .08 : 1 - Seg(l, .55, 1));
            }
        }
        double glintT = -1;
        void PlaceGlint()
        {
            double w = band.ActualWidth;
            if (glintT is < 0 or > 1 || w <= 0) { Show(glint, 0); return; }
            Show(glint, Math.Sin(Math.PI * glintT));
            var tt = (TranslateTransform)glint.RenderTransform; tt.X = -70 + (w + 140) * glintT; tt.Y = -40;
        }
        // seated over the head once laid out (never moved per frame)
        void Seat()
        {
            double y = _fightBody.Margin.Top, h = 22;
            if (_head is { } hd && hd.ActualHeight > 0 && hd.IsDescendantOf(_fight)) { y = hd.TranslatePoint(new Point(0, 0), _fight).Y; h = hd.ActualHeight; }
            var m = new Thickness(Pad - 4, Math.Max(2, y - 5), Pad - 4, 0);
            if (band.Margin != m) band.Margin = m;
            if (band.Height != h + 10) band.Height = h + 10;
        }
        Seat();
        if (_head is { } h0) h0.SizeChanged += (_, _) => { Seat(); PlaceShards(); };
        band.SizeChanged += (_, ev) => { art.Fit(ev.NewSize); glintHost.Clip = art.Shape; PlaceGlint(); };
        var head = _head;
        var words0 = words;
        Motion.Play(_fightFx, 2400, t =>
        {
            reveal.ScaleX = Out(Seg(t, 0, .2));
            Show(flash, t < .14 ? .55 * Seg(t, .06, .14) : .55 * (1 - Seg(t, .14, .32)));
            slam.Y = t < .1 ? -12 * (1 - Out(Seg(t, 0, .1))) : 2.5 * Math.Sin(Math.PI * Seg(t, .1, .2)) * (1 - Seg(t, .1, .2));
            glintT = t is > .2 and < .6 ? Seg(t, .2, .6) : -1; PlaceGlint();
            shatterT = t is > .16 and < .62 ? Seg(t, .16, .62) : -1; PlaceShards();
            words0.Opacity = t < .84 ? Seg(t, .02, .07) : 1 - Seg(t, .84, 1);
            band.Opacity = t < .84 ? 1 : 1 - Seg(t, .84, 1);
            if (head != null) head.Opacity = t < .84 ? 1 - Seg(t, 0, .12) : Seg(t, .84, 1);
        }, () => { _fightFx.Children.Clear(); if (head != null) head.Opacity = 1; if (_shards != null) { _fight.Children.Remove(_shards); _shards = null; } });
        if (!wipe) Frost();
    }
}
