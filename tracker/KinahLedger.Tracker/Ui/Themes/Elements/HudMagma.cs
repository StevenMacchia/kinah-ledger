using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using KinahLedger.Parser;
using KinahLedger.Session;
using static KinahLedger.Ui.HudKit;

namespace KinahLedger.Ui;

/// <summary>
/// Magma (the Elements theme, the third looks pack, 2026-11-20): cooled basalt split by glowing lava, each bar a molten flow.
/// Every surface is a slab of charcoal basalt (HudMagma.Rock.cs: a grain drawn once, a chipped outline, the lava's heat glowing
/// up from its foot) split along its edges by lava cracks, orange with a white-hot core. The Buddy is one slab: the run's
/// ring as a lava ring (a molten arc with a white-hot tip round a basalt track), the hour big in white-hot over a warm
/// underglow, the pickups set in basalt with a crack glowing across each corner, the timers as basalt chips. The meter is one
/// slab: the boss in a heavy face, the clock, the HP as a red seam in a stone channel, then each member as a channel cut in
/// the stone with their molten flow in it, dark red at its root under cooled crust plates (the fire glowing in the seams)
/// through orange to a yellow-white front, the leader's hottest; the class's colour a stripe down the channel's edge; a
/// molten crown on the top row. The moments: a pickup sends a wave of heat across the slab, a rare drop's cracks burn in its
/// grade's colour, your best passed fills the lava ring white-hot and throws sparks; a fight rises in, the heat pulse rolls
/// down every flow with a heat haze over it (on the start, a kill and a new leader, when the crown rises out of its pool), and
/// its end lays a basalt banner over the head, its cracks flaring (cooled to ash for a wipe).
/// </summary>
sealed partial class HudMagma : ModernLook
{
    static readonly Color MintC2 = Color.FromRgb(0x5D, 0xF2, 0xA6), MarkMintC = Color.FromRgb(0x7D, 0xFF, 0xC0), CoralC = Color.FromRgb(0xFF, 0x8C, 0x7C),
        GoldC2 = Color.FromRgb(0xFF, 0xC8, 0x5A), WarnC2 = Color.FromRgb(0xFF, 0xB0, 0x86), HpC = Color.FromRgb(0xFF, 0x3E, 0x22);
    static readonly SolidColorBrush White = Frozen(Colors.White), Soft = Frozen(Color.FromArgb(0xE0, 0xFF, 0xF0, 0xE4)), Quiet = Frozen(Color.FromArgb(0xA8, 0xFF, 0xE6, 0xD4)),
        Mint2 = Frozen(Color.FromRgb(0x8C, 0xFF, 0xC6)), MarkMint = Frozen(MarkMintC), Coral = Frozen(CoralC);
    static readonly FontFamily Heavy = HudThemes.Heavy;

    // the room round the look: the slabs keep their glow inside; 98 at the right for the pickup's chip beside the Buddy
    readonly Grid _root = new() { Margin = new Thickness(6, 6, 98, 12), HorizontalAlignment = HorizontalAlignment.Left };   // 12 under: the drips
    readonly StackPanel _body = new() { HorizontalAlignment = HorizontalAlignment.Left };
    double _opacity = 0.9;

    // the Buddy: its slab, the ring, the hour, the session and the timer chips
    readonly Grid _line = new() { HorizontalAlignment = HorizontalAlignment.Left };
    readonly TacSketch _lineSlab, _lineFlare;
    /// <summary>The Buddy's moment layer: a pickup's band of heat crossing the slab (drawn once, moved along).</summary>
    readonly Canvas _lineFx = new() { IsHitTestVisible = false, ClipToBounds = true, Visibility = Visibility.Collapsed };
    readonly TacSketch _sheenBand = new((dc, s) => dc.DrawRectangle(Mod.Grad(false, (Mod.A(LavaC, 0), 0), (Mod.A(LavaC, .24), .55), (Mod.A(LavaYelC, .34), .88), (Mod.A(LavaYelC, 0), 1)), null, new Rect(0, 0, s.Width, s.Height)), 64, double.NaN);
    readonly TranslateTransform _sheenX = new();
    /// <summary>Three small jets off the Buddy's slab (best passed), standing on its river; drawn once.</summary>
    readonly Grid _lineJets = new() { IsHitTestVisible = false, VerticalAlignment = VerticalAlignment.Bottom, HorizontalAlignment = HorizontalAlignment.Left, Width = 150, Margin = new Thickness(40, 0, 0, RiverH - 1), Visibility = Visibility.Collapsed };
    readonly List<(UIElement Jet, ScaleTransform S)> _lineJetParts = new();
    readonly StackPanel _lineBody = new() { Orientation = Orientation.Horizontal, Margin = new Thickness(9, 7, 14, 7) };
    readonly Canvas _lineAnchor = Tac.Anchor(HorizontalAlignment.Right, VerticalAlignment.Center);
    readonly StackPanel _timers = new() { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
    readonly Border _timersInLine = new() { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 0) };
    /// <summary>The timer chips over the line, when the pickup strip takes their room on it.</summary>
    readonly Border _timersUp = new() { HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(2, 0, 0, 4), Visibility = Visibility.Collapsed };
    /// <summary>The pickup strip: 24 px tiles set in basalt beside the hour, a lava rule, a yellow-hot star.</summary>
    static readonly PickStrip.Look Strip = new(24, 6, true, Frozen(Color.FromArgb(0xB0, 0xEE, 0x57, 0x12)), Frozen(Color.FromRgb(0xFF, 0xD2, 0x5E)));
    bool _dim;
    TacSketch? _ringArt;
    Grid? _ringHost;
    double _share = -1, _complete;
    bool _beat;
    Border? _firstChip;

    // the rare drop under the Buddy, and the notice line
    readonly Grid _rare = new() { HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 3, 0, 0), Visibility = Visibility.Collapsed };
    readonly Grid _note = new() { HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(4, 3, 0, 0), Visibility = Visibility.Collapsed };
    readonly StackPanel _noteBody = new() { Orientation = Orientation.Horizontal, Margin = new Thickness(9, 4, 11, 5) };

    // the fight: one basalt slab, its head, HP, rows and foot
    readonly Grid _fight = new() { HorizontalAlignment = HorizontalAlignment.Left, Visibility = Visibility.Collapsed, Margin = new Thickness(0, 0, 0, 4) };
    readonly TacSketch _fightSlab, _fightFlare, _fightErupt;
    /// <summary>The eruption's lava jets, standing on the river along the slab's foot (drawn once; each scaled up out of the river and back).</summary>
    readonly Grid _jets = new() { IsHitTestVisible = false, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(4, 0, 4, RiverH - 1) };
    readonly List<(UIElement Jet, ScaleTransform S, UIElement Bead, TranslateTransform B, double H, double Delay, double Peak)> _jetParts = new();
    /// <summary>The jets: their columns' widths (uneven), heights (three tiers: the tall ones rise up into the rows behind the words) and starts.</summary>
    static readonly (double Col, double H, double W, double Delay)[] JetPlan =
    {
        (1.0, 30, 15, .05), (1.5, 58, 20, 0), (.8, 18, 12, .1), (1.3, 44, 17, .03), (1.7, 22, 13, .12), (.9, 62, 21, .02), (1.2, 32, 15, .08),
    };
    /// <summary>The fissures splitting the meter's slab: their flares, flashed by a moment.</summary>
    readonly List<UIElement> _splitFlares = new();
    /// <summary>The dark plates under the foot's words, shown only while an eruption's jets play behind them (Foot).</summary>
    readonly SolidColorBrush _footPlate = new(Color.FromArgb(0xC8, 0x0A, 0x06, 0x04)) { Opacity = 0 };
    const double FightPad = 9;
    readonly StackPanel _fightBody = new() { Margin = new Thickness(FightPad, 8, FightPad, 8) };
    readonly Border _timersInFight = new() { Margin = new Thickness(0, 0, 0, 6), HorizontalAlignment = HorizontalAlignment.Left };
    readonly Grid _fightFx = new() { IsHitTestVisible = false, ClipToBounds = true };
    readonly TranslateTransform _fightShift = new();
    DateTime _lastLead;
    FrameworkElement? _head;

    public HudMagma() : base(bare: true)
    {
        // the look's own rows (Ui/Themes/ThemeRows.cs): each a channel in the stone with its molten flow
        UseRows(MagmaRow.Pitch, m => new MagmaRow(this), 14, GapLine);
        _lineSlab = new TacSketch((dc, s) => Slab(dc, s, SlabA, 11, ash: _dim, river: true));
        _lineFlare = new TacSketch((dc, s) => Flare(dc, s, 11)) { Opacity = 0 };
        _sheenBand.RenderTransform = _sheenX;
        _lineFx.Children.Add(_sheenBand);
        foreach (var (i, jh, jw) in new[] { (0, 24.0, 12.0), (1, 34.0, 15.0), (2, 20.0, 11.0) })
        {
            int seed = 301 + i * 7;
            _lineJets.ColumnDefinitions.Add(new ColumnDefinition());
            var jet = new TacSketch((dc, s) => Jet(dc, s, seed), jw, jh) { VerticalAlignment = VerticalAlignment.Bottom, HorizontalAlignment = HorizontalAlignment.Center, Opacity = 0 };
            var sc = new ScaleTransform(1, 0);
            jet.RenderTransformOrigin = new Point(.5, 1); jet.RenderTransform = sc;
            Grid.SetColumn(jet, i); _lineJets.Children.Add(jet); _lineJetParts.Add((jet, sc));
        }
        _line.Children.Add(_lineSlab); _line.Children.Add(_lineFlare); _line.Children.Add(_lineFx); _line.Children.Add(_lineJets); _line.Children.Add(_lineBody); _line.Children.Add(_lineAnchor);
        _fightSlab = new TacSketch((dc, s) => Slab(dc, s, SlabA, 23, river: true));
        _fightFlare = new TacSketch((dc, s) => Flare(dc, s, 23)) { Opacity = 0 };
        _fightErupt = new TacSketch((dc, s) => Eruption(dc, s, 23)) { Opacity = 0 };
        for (int i = 0; i < JetPlan.Length; i++)
        {
            int seed = 71 + i * 13;
            var (col, jh, jw, delay) = JetPlan[i];
            _jets.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(col, GridUnitType.Star) });
            var jet = new TacSketch((dc, s) => Jet(dc, s, seed), jw, jh) { VerticalAlignment = VerticalAlignment.Bottom, HorizontalAlignment = HorizontalAlignment.Center, Opacity = 0 };
            var sc = new ScaleTransform(1, 0);
            jet.RenderTransformOrigin = new Point(.5, 1); jet.RenderTransform = sc;
            var bg = new RadialGradientBrush(WhiteHotC, Mod.A(LavaHotC, 0)) { GradientOrigin = new Point(.5, .4) }; bg.Freeze();
            var bt = new TranslateTransform();
            var bead = new System.Windows.Shapes.Ellipse { Width = 5, Height = 6, Fill = bg, VerticalAlignment = VerticalAlignment.Bottom, HorizontalAlignment = HorizontalAlignment.Center, Opacity = 0, RenderTransform = bt };
            Grid.SetColumn(jet, i); Grid.SetColumn(bead, i);
            _jets.Children.Add(jet); _jets.Children.Add(bead);
            _jetParts.Add((jet, sc, bead, bt, jh, delay, jh > 40 ? .75 : 1));
        }
        _jets.Visibility = Visibility.Collapsed;
        _fight.Children.Add(_fightSlab); _fight.Children.Add(_fightFlare); _fight.Children.Add(_fightErupt); _fight.Children.Add(_jets); _fight.Children.Add(_fightBody); _fight.Children.Add(_fightFx);
        _fight.RenderTransform = _fightShift;
        _note.Children.Add(new TacSketch((dc, s) => Slab(dc, s, SlabA, 5, cracks: false)));
        _note.Children.Add(_noteBody);
        _runHeadHost = RunHeadSlab(_runHead); _runHeadHost.Visibility = Visibility.Collapsed;
        _body.Children.Add(_fight); _body.Children.Add(_timersUp); _body.Children.Add(_runHeadHost); _body.Children.Add(_line); _body.Children.Add(_rare); _body.Children.Add(_note);
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
        _lineSlab.InvalidateVisual(); _fightSlab.InvalidateVisual();
        foreach (var s in _note.Children.OfType<TacSketch>()) s.InvalidateVisual();
        foreach (var s in _rare.Children.OfType<TacSketch>()) s.InvalidateVisual();
    }

    /// <summary>The stone's strength from the opacity setting: near solid at the usual 90% (the rows' channels stay dark at any setting).</summary>
    double SlabA => Math.Clamp(.5 + .5 * _opacity, .6, 1);

    protected override string LineSig(Figures f) => base.LineSig(f) + "|" + (Rare(Values)?.Id ?? "") + "|" + (Picks(Values).FirstOrDefault()?.Id ?? "") + "|" + Dimmed(Values) + "|" + PickStrip.Sig(Values, Strip) + "|" + RunBuddy.Sig(Values);

    static bool Dimmed(IReadOnlyDictionary<string, string> v) => (v.TryGetValue("show", out var s) && s == "dim") || (v.TryGetValue("paused", out var p) && p.Length > 0);

    // ================================================================ the Buddy

    protected override void DrawLine(Figures f)
    {
        _lineBody.Children.Clear();
        _ringArt = null; _ringHost = null; _timerUp = false; _stripTimerW = -1;
        if (_timersInLine.Child != null) _timersInLine.Child = null;
        _dim = Dimmed(Values);
        _lineBody.Opacity = _dim ? .62 : 1;
        bool mark = f.Abyss && f.Counted && f.Icon.Length > 0;
        if (mark) { var m = AbyssMark(f.Icon, 36); m.Margin = new Thickness(0, 0, 10, 0); _lineBody.Children.Add(m); }
        // in a dungeon run: the run's clock, splits and verdict in place of the hour (HudMagma.Run.cs)
        var rs = RunNow();
        _runHeadHost.Visibility = Visibility.Collapsed;
        if (rs != null) DrawRun(rs);
        else if (!HasHour(f))
        {
            var w = Tt(Waiting(f.Abyss), 12.5, Soft, FontWeights.SemiBold); w.Margin = new Thickness(2, 4, 0, 4);
            _lineBody.Children.Add(w);
        }
        else
        {
            if (HasRing(f))
            {
                _share = f.Share; _beat = f.Beat; _complete = 0;
                var ring = new Grid { Width = 42, Height = 42, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0) };
                _ringArt = new TacSketch(DrawRing, 42, 42);
                ring.Children.Add(_ringArt);
                var pc = Tt(f.Beat ? "★" : Pct(f.Share), f.Beat ? 15 : 13, f.Beat ? Frozen(LavaYelC) : WhiteHot, FontWeights.Bold); pc.HorizontalAlignment = HorizontalAlignment.Center;
                ring.Children.Add(pc);
                _ringHost = ring;
                _lineBody.Children.Add(ring);
            }
            else if (!mark) _lineBody.Children.Add(new Border { Width = 2 });
            // the hour big in white-hot over its warm underglow, its unit small; the session and the latest pickup under it
            var words = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 0, 1) };
            var top = new StackPanel { Orientation = Orientation.Horizontal };
            var rate = Tt(f.Rate, f.Warm ? 15 : 23, f.Warm ? Soft : WhiteHot, f.Warm ? FontWeights.SemiBold : FontWeights.Bold);
            rate.Tag = "buddy-rate";
            top.Children.Add(new Border { Child = rate, Effect = f.Warm ? null : Underglow() });
            if (f.Unit.Length > 0) { var u = Tt(f.Unit, 11, Quiet, FontWeights.SemiBold); u.VerticalAlignment = VerticalAlignment.Bottom; u.Margin = new Thickness(2, 0, 0, f.Warm ? 1 : 4); top.Children.Add(u); }
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
                if (strip != null) { sub = bare; strip.Margin = new Thickness(14, 0, 0, 0); Set(strip); }
                // no room beside the hour: under the session, never dropped (Steven, 2026-11-19)
                else if ((under = StripUnder(Strip, false)) != null) { sub = bare; under.Margin = new Thickness(2, 7, 0, 2); Set(under); }
                _stripTimerW = timerW;
            }
            if (sub.Inlines.Count > 0) words.Children.Add(sub);
            if (under != null) words.Children.Add(under);
            _lineBody.Children.Add(words);
            if (strip != null) _lineBody.Children.Add(strip);
        }
        _lineBody.Children.Add(_timersInLine);
        PlaceTimers();
        _lineSlab.InvalidateVisual();

        // the rare drop the window holds: a slab whose cracks burn in its grade's colour, its tile, RARE DROP and its name
        _rare.Children.Clear();
        if (!KinahHidden && Rare(Values) is { } r)
        {
            var c = ColorOf(r.Color, r.Grade);
            var slab = new Grid();
            slab.Children.Add(new TacSketch((dc, s) =>
            {
                Slab(dc, s, SlabA, 7, .35, river: true);
                // the grade's glow from the tile's side
                dc.DrawRectangle(Mod.Grad(false, (Mod.A(c, .28), 0), (Mod.A(c, 0), .55)), null, new Rect(2, 2, s.Width - 4, s.Height - 4));
            }));
            _rareFlare = new TacSketch((dc, s) => Flare(dc, s, 7)) { Opacity = 0 };
            slab.Children.Add(_rareFlare);
            _rareArt = slab;
            _rare.Children.Add(slab);
            var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(8, 6, 14, 6) };
            var tile = Tile(r, 26); tile.Margin = new Thickness(0, 0, 9, 0);
            _rareTile = tile;
            row.Children.Add(tile);
            var mk = Chip(L.T("RARE DROP"), Frozen(Mod.Mix(c, Colors.White, .55)), c, size: 8); mk.Margin = new Thickness(0, 0, 7, 0);
            row.Children.Add(mk);
            var nm = Tt(r.Name, 13, Frozen(Mod.Mix(c, Colors.White, .5)), FontWeights.Bold);
            row.Children.Add(nm);
            if (r.Count.Length > 0) { var cn = Tt(" " + r.Count, 11, Soft, FontWeights.SemiBold); cn.Margin = new Thickness(2, 1, 0, 0); row.Children.Add(cn); }
            _rare.Children.Add(row);
            _rare.Visibility = Visibility.Visible;
        }
        else { _rare.Visibility = Visibility.Collapsed; _rareArt = null; _rareTile = null; _rareFlare = null; }

        // the notice: its words in their tone on a small slab under the Buddy, the warning sign before a pause
        _noteBody.Children.Clear();
        if (f.Note.Length > 0 && !KinahHidden)
        {
            bool warn = ReferenceEquals(f.NoteTone, Warn);
            if (warn) { var wi = WarnIcon(); wi.Width = wi.Height = 13; wi.Margin = new Thickness(0, 0, 5, 0); _noteBody.Children.Add(wi); }
            var tone = warn ? WarnC2 : ReferenceEquals(f.NoteTone, Gold) ? GoldC2 : ReferenceEquals(f.NoteTone, Mint) ? MintC2 : Color.FromRgb(0xFF, 0xF0, 0xE4);
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
        void Dot() { if (sub.Inlines.Count > 0) sub.Inlines.Add(new System.Windows.Documents.Run("  ·  ") { Foreground = Frozen(Mod.A(LavaHotC, .8)) }); }
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

    Grid? _rareArt;
    TacSketch? _rareFlare;
    FrameworkElement? _rareTile;

    /// <summary>The run's ring as a lava ring: a basalt track, your best's share as a molten arc with a white-hot tip (all white-hot once beaten).</summary>
    void DrawRing(DrawingContext dc, Size s)
    {
        double c = s.Width / 2, r = c - 4;
        var center = new Point(c, c);
        dc.DrawEllipse(Frozen(Color.FromArgb(0xE6, 0x0C, 0x08, 0x07)), null, center, r + 3, r + 3);
        dc.DrawEllipse(null, Mod.P(Frozen(Color.FromRgb(0x2A, 0x1D, 0x17)), 4.2), center, r, r);
        dc.DrawEllipse(null, Mod.P(Frozen(Mod.A(BevelC, .6)), .8), center, r + 2.3, r + 2.3);
        double share = Math.Clamp(_beat ? 1 : _share, 0, 1);
        double shown = share + (1 - share) * Math.Clamp(_complete, 0, 1);
        bool full = _beat || _complete >= 1;
        if (shown >= .999)
        {
            dc.DrawEllipse(null, Mod.P(Mod.Br(LavaHotC, .35), 7.5), center, r, r);
            dc.DrawEllipse(null, Mod.P(Frozen(full ? LavaYelC : LavaHotC), 3.4), center, r, r);
            dc.DrawEllipse(null, Mod.P(Mod.Br(WhiteHotC, .85), 1.2), center, r, r);
            return;
        }
        if (shown < .004) return;
        double a = 2 * Math.PI * shown;
        var tip = new Point(c + r * Math.Sin(a), c - r * Math.Cos(a));
        var fig = new PathFigure { StartPoint = new Point(c, c - r) };
        fig.Segments.Add(new ArcSegment(tip, new Size(r, r), 0, a > Math.PI, SweepDirection.Clockwise, true));
        var pg = new PathGeometry(); pg.Figures.Add(fig); pg.Freeze();
        dc.DrawGeometry(null, Mod.P(Mod.Br(LavaC, .35), 7.5, round: true), pg);
        dc.DrawGeometry(null, Mod.P(Mod.Grad(true, (LavaHotC, 0), (LavaC, 1)), 3.6, round: true), pg);
        dc.DrawGeometry(null, Mod.P(Mod.Br(LavaYelC, .7), 1, round: true), pg);
        var g = new RadialGradientBrush(Mod.A(WhiteHotC, 1), Mod.A(LavaHotC, 0)); g.Freeze();
        dc.DrawEllipse(g, null, tip, 5, 5);
        dc.DrawEllipse(WhiteHot, null, tip, 1.7, 1.7);
    }

    protected override void KinahShown(bool shown)
    {
        _line.Visibility = shown ? Visibility.Visible : Visibility.Collapsed;
        if (!shown) { _note.Visibility = Visibility.Collapsed; _rare.Visibility = Visibility.Collapsed; }
        _fight.Margin = new Thickness(0, 0, 0, shown ? 4 : 0);
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
            var c = tone == TimerBadge.Tone.On ? MintC2 : tone == TimerBadge.Tone.Soon ? LavaHotC : Color.FromRgb(0xC8, 0xB4, 0xA8);
            var sp = new StackPanel { Orientation = Orientation.Horizontal };
            double d = first ? 7 : 5.5;
            sp.Children.Add(new System.Windows.Shapes.Ellipse
            {
                Width = d, Height = d, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0),
                Fill = new RadialGradientBrush(Mod.Mix(c, Colors.White, .55), c),
            });
            var t = Tt(text, first ? 11.5 : 10, first ? White : Soft, first ? FontWeights.Bold : FontWeights.SemiBold);
            if (tone == TimerBadge.Tone.On) t.Foreground = Frozen(Mod.Mix(MintC2, Colors.White, .45));
            sp.Children.Add(t);
            var chip = new Border
            {
                Child = sp, CornerRadius = new CornerRadius(4), Padding = first ? new Thickness(7, 3, 9, 4) : new Thickness(6, 2, 8, 3), VerticalAlignment = VerticalAlignment.Center,
                Background = Mod.Grad(true, (Color.FromArgb(0xF2, 0x22, 0x18, 0x13), 0), (Color.FromArgb(0xF2, 0x10, 0x0B, 0x09), 1)), BorderThickness = new Thickness(1),
                BorderBrush = Frozen(Mod.A(tone == TimerBadge.Tone.On ? MintC2 : tone == TimerBadge.Tone.Soon ? LavaHotC : BevelC, tone == TimerBadge.Tone.Quiet ? 1 : .7)),
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
        _splitFlares.Clear();
        if (e == null) { _fight.Visibility = Visibility.Collapsed; _fightFx.Children.Clear(); PlaceTimers(); return; }
        double w = FightWidth - 10, inner = w - 2 * FightPad;
        _fight.Width = w;
        _fightBody.Children.Add(_timersInFight);
        // the head: the boss in the heavy face, the outcome once it's over, the clock over its warm underglow
        var head = new Grid();
        head.ColumnDefinitions.Add(new ColumnDefinition()); head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var title = Tt(e.Title, 13.5, White, FontWeights.Black, Heavy); title.TextTrimming = TextTrimming.CharacterEllipsis;
        head.Children.Add(title);
        var right = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(8, 0, 0, 0) };
        if (e.Waiting) right.Children.Add(Chip(L.T("READY"), MarkMint, MintC2, size: 9.5));
        else
        {
            var outcome = OutcomeText(e);
            if (outcome.Length > 0)
            {
                bool wipe = e.Outcome == "wipe";
                var st = Chip(outcome, wipe ? Coral : LavaYel, wipe ? CoralC : LavaHotC, size: 9.5);
                st.Margin = new Thickness(0, 1, 8, 0); st.Tag = "outcome";
                right.Children.Add(st);
            }
            var clock = Tt(Clock(e, now), 18, WhiteHot, FontWeights.Bold); clock.Tag = "fight-clock";
            right.Children.Add(new Border { Child = clock, Effect = Underglow(.75) });
        }
        Grid.SetColumn(right, 1); head.Children.Add(right);
        _head = head;
        _fightBody.Children.Add(head);
        if (e.Waiting)
        {
            var wt = Tt(e.ReadyText, 12.5, Soft, FontWeights.SemiBold); wt.Margin = new Thickness(0, 5, 0, 1); wt.TextTrimming = TextTrimming.CharacterEllipsis;
            _fightBody.Children.Add(wt);
            if (PartyBeforeHit(e)) { _fightBody.Children.Add(Seam(new Thickness(0, 3, 0, 3), .6)); PutRows(_fightBody, e, now, inner); }
        }
        else
        {
            if (HpOf(e) is { } hp)
            {
                // the boss's HP: a red seam in a stone channel, cracks at the thirds, the figure and what's left under its right end
                var track = new Grid { Height = 7, Margin = new Thickness(0, 6, 0, 0) };
                track.Children.Add(new TacSketch((dc, s) =>
                {
                    // a trough cut in the basalt, the boss's life a crimson seam in it with a hot pale core, cracks at the thirds
                    var r = new Rect(0, 0, s.Width, s.Height);
                    dc.DrawRoundedRectangle(Frozen(Color.FromRgb(0x07, 0x04, 0x03)), Mod.P(Mod.Grad(true, (RimC, 0), (BevelC, 1)), 1), r, 2.5, 2.5);
                    double x1 = (s.Width - 3) * Math.Clamp(hp, 0, 1);
                    if (x1 > .5)
                    {
                        var f = new Rect(1.5, 1.5, x1, s.Height - 3);
                        dc.DrawRoundedRectangle(Mod.Grad(true, (Color.FromRgb(0xFF, 0x4A, 0x5A), 0), (Color.FromRgb(0xD0, 0x10, 0x2A), .55), (Color.FromRgb(0x6A, 0x04, 0x14), 1)), null, f, 1.5, 1.5);
                        dc.DrawRectangle(Mod.Grad(false, (Mod.A(Color.FromRgb(0xFF, 0xC8, 0xD0), .35), 0), (Color.FromRgb(0xFF, 0xE4, 0xE8), 1)), null, new Rect(1.5, s.Height / 2 - .9, x1, 1.4));
                    }
                    for (int k = 1; k <= 2; k++)
                    {
                        double x = Math.Round(s.Width * k / 3);
                        dc.DrawLine(Mod.P(Frozen(RimC), 2.2), new Point(x, -.5), new Point(x - 1, s.Height + .5));
                    }
                }));
                HpGhost(track, e, hp, now, 1.5);
                _fightBody.Children.Add(track);
                var hl = new TextBlock { FontFamily = Mod.Face, FontSize = 10.5, Foreground = Soft, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 2, 0, 0) };
                System.Windows.Documents.Typography.SetNumeralAlignment(hl, FontNumeralAlignment.Tabular);
                hl.Inlines.Add(new System.Windows.Documents.Run($"{Math.Round(hp * 100):0}%") { FontWeight = FontWeights.Bold, Foreground = hp < .34 ? Frozen(Color.FromRgb(0xFF, 0x9A, 0x8A)) : White });
                var left = HpLeftText(e);
                if (left.Length > 0) hl.Inlines.Add(new System.Windows.Documents.Run("  ·  " + left) { FontWeight = FontWeights.Medium });
                hl.Tag = "hp-text";
                _fightBody.Children.Add(hl);
            }
            // the slab split under its head: a lava fissure edge to edge
            _fightBody.Children.Add(Split(new Thickness(-FightPad, HpOf(e) != null ? 1 : 4, -FightPad, 2), 1, _splitFlares));
            PutRows(_fightBody, e, now, inner);
            if (e.HasDamage)
            {
                _fightBody.Children.Add(Split(new Thickness(-FightPad, 2, -FightPad, 1), 2, _splitFlares));
                var foot = Foot(e, now);
                _fightBody.Children.Add(foot);
                // the jets stand between the foot's words (its total at the left, the group's figure at the right)
                if (foot is Panel fp && fp.Children.Count >= 2)
                    _jets.Margin = new Thickness(FightPad + Mod.W((FrameworkElement)fp.Children[0]) + 10, 0, FightPad + Mod.W((FrameworkElement)fp.Children[1]) + 10, RiverH - 1);
            }
        }
        _fight.Visibility = Visibility.Visible;
        PlaceTimers();
    }

    // ================================================================ the heat pulse

    /// <summary>A new leader took the top row: the heat pulse (at most once in a while, however often the lead swaps).</summary>
    internal void Lead()
    {
        if (!Moves || (DateTime.UtcNow - _lastLead).TotalMilliseconds < 1500) return;
        _lastLead = DateTime.UtcNow;
        Erupt(700);
        Heat();
    }

    /// <summary>
    /// The meter's heat pulse: a bright band rolls down each flow from root to front (row after row), shining through the
    /// crust's seams, the front flares as it arrives, a heat haze ripples over the molten part, and the slab's cracks flare
    /// and cool with it. One course, then gone.
    /// </summary>
    void Heat()
    {
        var rows = Meter?.Members.Select(m => Meter.RowOf(m.Id)).OfType<MagmaRow>().ToList() ?? new();
        Motion.Play(_fightSlab, 1300, t =>
        {
            for (int i = 0; i < rows.Count; i++) rows[i].SetPulse(Seg(t, .06 * i, .72 + .06 * i));
            _fightFlare.Opacity = t < .25 ? Seg(t, 0, .25) : 1 - Seg(t, .25, 1);
        }, () =>
        {
            _fightFlare.Opacity = 0;
            foreach (var r in rows) r.SetPulse(-1);
        });
    }

    // ================================================================ the moments

    /// <summary>A pickup: a wave of heat crosses the Buddy's slab and the worth comes in on a basalt chip beside it, then goes.</summary>
    protected override void Pickup(Pick p, bool held)
    {
        if (KinahHidden || _line.Visibility != Visibility.Visible) return;
        var worth = p.Worth.Length > 0 ? "+" + p.Worth.TrimStart('+') : p.Count;
        var words = new StackPanel { Margin = new Thickness(9, 3, 11, 4) };
        words.Children.Add(Tt(worth, 14.5, p.Worth.Length > 0 ? Frozen(Mod.Mix(MintC2, Colors.White, .25)) : White, FontWeights.Bold));
        var nm = Tt(p.Name.Length > 13 ? p.Name[..12].TrimEnd() + "…" : p.Name, 9.5, Soft, FontWeights.SemiBold); nm.Margin = new Thickness(0, -1, 0, 0);
        words.Children.Add(nm);
        var chip = new Grid { MaxWidth = 88 };
        chip.Children.Add(new TacSketch((dc, s) => Slab(dc, s, 1, 3, .5)));
        chip.Children.Add(words);
        var shift = new TranslateTransform();
        chip.RenderTransform = shift;
        chip.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        Canvas.SetLeft(chip, 4); Canvas.SetTop(chip, -chip.DesiredSize.Height / 2);
        _lineAnchor.Children.Clear(); _lineAnchor.Children.Add(chip);
        _lineFx.Visibility = Visibility.Visible;
        _sheenBand.Height = Math.Max(1, _line.ActualHeight);
        Motion.Play(chip, 1900, t =>
        {
            double k0 = Seg(t, 0, .5), w = Math.Max(80, _line.ActualWidth);
            _sheenX.X = -64 + (w + 64) * k0;
            _sheenBand.Opacity = t < .5 ? Math.Sin(Math.PI * k0) : 0;
            _lineFlare.Opacity = t < .5 ? .6 * Math.Sin(Math.PI * k0) : 0;
            double k = Out(Seg(t, .1, .3));
            shift.X = -10 * (1 - k);
            chip.Opacity = t < .82 ? k : 1 - Seg(t, .82, 1);
        }, () => { _lineFx.Visibility = Visibility.Collapsed; _lineFlare.Opacity = 0; _lineAnchor.Children.Remove(chip); });
    }

    /// <summary>A rare drop: its slab breaks open to the right, its cracks burning in the grade's colour, the tile popping in.</summary>
    protected override void RareDrop(Pick p, bool held)
    {
        if (_rareArt is not { } art || _rareFlare is not { } flare) return;
        var tile = _rareTile;
        var sc = new ScaleTransform(1, 1);
        if (tile != null) { tile.RenderTransformOrigin = new Point(.5, .5); tile.RenderTransform = sc; }
        var open = new ScaleTransform(1, 1);
        art.RenderTransformOrigin = new Point(0, .5); art.RenderTransform = open;
        Motion.Play(art, 1500, t =>
        {
            open.ScaleX = .25 + .75 * Out(Seg(t, 0, .4));
            flare.Opacity = t < .2 ? Seg(t, 0, .2) : 1 - Seg(t, .2, .85);
            sc.ScaleX = sc.ScaleY = .65 + .35 * Back(Seg(t, .05, .4), 2.2);
        }, () => { open.ScaleX = 1; flare.Opacity = 0; sc.ScaleX = sc.ScaleY = 1; });
    }

    /// <summary>Your best passed: the lava ring runs on round to full and goes white-hot, sparks spray off it and the slab's cracks flare.</summary>
    protected override void BestPassed(bool held)
    {
        if (_ringArt is not { } art) return;
        double was = _share is > 0 and < 1 ? _share : .86;
        bool beat = _beat;
        // the sparks: seven hot drops thrown up and out off the ring, each a small dot moved by its transform
        var sparks = new Canvas { Width = 42, Height = 42, IsHitTestVisible = false };
        var dots = new List<(UIElement Dot, TranslateTransform T, double Ang)>();
        for (int i = 0; i < 7; i++)
        {
            var g = new RadialGradientBrush(Mod.A(i % 2 == 0 ? LavaYelC : WhiteHotC, 1), Mod.A(LavaC, 0)); g.Freeze();
            var d = new System.Windows.Shapes.Ellipse { Width = 7, Height = 7, Fill = g, Opacity = 0 };
            var tt = new TranslateTransform(); d.RenderTransform = tt;
            Canvas.SetLeft(d, 17.5); Canvas.SetTop(d, 17.5);
            sparks.Children.Add(d);
            dots.Add((d, tt, -Math.PI / 2 + (i - 3) * .38));
        }
        _ringHost?.Children.Add(sparks);
        // the ring's flare: it swells a quarter and settles, a halo of heat ringing out off it
        var halo = new TacSketch((dc, s) =>
        {
            var c = new Point(21, 21);
            dc.DrawEllipse(null, Mod.P(Mod.Br(LavaC, .5), 7), c, 19, 19);
            dc.DrawEllipse(null, Mod.P(Mod.Br(LavaYelC, 1), 2), c, 19, 19);
        }, 42, 42) { Opacity = 0 };
        var haloS = new ScaleTransform(1, 1);
        halo.RenderTransformOrigin = new Point(.5, .5); halo.RenderTransform = haloS;
        _ringHost?.Children.Insert(0, halo);
        var swell = new ScaleTransform(1, 1);
        if (_ringHost != null) { _ringHost.RenderTransformOrigin = new Point(.5, .5); _ringHost.RenderTransform = swell; }
        _lineJets.Visibility = Visibility.Visible;
        Motion.Play(art, 1800, t =>
        {
            double fl = Seg(t, .4, .75);
            swell.ScaleX = swell.ScaleY = 1 + .22 * Math.Sin(Math.PI * fl);
            haloS.ScaleX = haloS.ScaleY = 1 + .55 * Out(fl);
            halo.Opacity = fl is > 0 and < 1 ? 1 - fl : 0;
            for (int i = 0; i < _lineJetParts.Count; i++)
            {
                var (jet, sc) = _lineJetParts[i];
                double k = Seg(t, .4 + i * .04, .78 + i * .04);
                sc.ScaleY = k < .4 ? 1.1 * Out(Seg(k, 0, .4)) : 1.1 * (1 - Seg(k, .4, 1) * Seg(k, .4, 1));
                jet.Opacity = k is > 0 and < 1 ? .9 : 0;
            }
            _beat = false; _share = was;
            _complete = Out(Seg(t, 0, .45));
            if (t >= .45) { _beat = beat; _complete = 1; }
            art.InvalidateVisual();
            double sp = Seg(t, .4, 1);
            for (int i = 0; i < dots.Count; i++)
            {
                var (d, tt, ang) = dots[i];
                double k = Out(Seg(sp, i * .04, .7 + i * .04)), fall = 8 * Seg(sp, .45, 1) * Seg(sp, .45, 1), dist = 17 + 9 * k;
                tt.X = Math.Cos(ang) * dist; tt.Y = Math.Sin(ang) * dist + fall;
                d.Opacity = t > .4 ? 1 - sp : 0;
            }
            _lineFlare.Opacity = t < .45 ? Seg(t, 0, .45) : 1 - Seg(t, .45, 1);
        }, () =>
        {
            _complete = 0; _beat = beat; art.InvalidateVisual(); _lineFlare.Opacity = 0; (sparks.Parent as Panel)?.Children.Remove(sparks); (halo.Parent as Panel)?.Children.Remove(halo);
            swell.ScaleX = swell.ScaleY = 1; _lineJets.Visibility = Visibility.Collapsed;
            foreach (var (jet, sc) in _lineJetParts) { jet.Opacity = 0; sc.ScaleY = 0; }
        });
    }

    /// <summary>An entry opened: the first timer's chip flares mint round its edge and settles.</summary>
    protected override void EntryOpened(TimerBoard.Item i, bool held)
    {
        if (_firstChip is not { } chip) return;
        var glow = new DropShadowEffect { Color = MintC2, ShadowDepth = 0, BlurRadius = 10, Opacity = 0 };
        chip.Effect = glow;
        var edge = chip.BorderBrush;
        chip.BorderBrush = Frozen(MintC2);
        Motion.Play(chip, 1200, t =>
        {
            double k = t < .25 ? Seg(t, 0, .25) : 1 - Seg(t, .25, 1);
            glow.Opacity = .9 * k;
        }, () => { chip.Effect = null; chip.BorderBrush = edge; });
    }

    /// <summary>A fight started: the slab rises in as it fades up, the crown rises out of its pool, the heat pulse rolls down the flows.</summary>
    protected override void FightStarted(Encounter e, bool held)
    {
        Motion.Play(_fight, 420, t => _fightShift.Y = 2.5 * (1 - Out(t)), () => _fightShift.Y = 0);   // a short rise; the words never dim
        if (Meter is { Members.Count: > 0 } m && m.RowOf(m.Members[0].Id) is MagmaRow top) top.Rise();
        Erupt(900);
        Heat();
    }

    /// <summary>
    /// The eruption: lava jets shoot up out of the river along the slab's foot and fall back, the river boils white-hot, the
    /// fissures splitting the slab flash, the cracks flare; under a second. Every part drawn once; transforms and opacity move.
    /// </summary>
    void Erupt(double ms)
    {
        _jets.Visibility = Visibility.Visible;

        var flares = _splitFlares.ToList();
        Motion.Play(_fightErupt, ms, t =>
        {
            _fightErupt.Opacity = t < .08 ? Seg(t, 0, .08) : 1 - Out(Seg(t, .08, .7));
            _fightFlare.Opacity = Math.Max(_fightFlare.Opacity, t < .1 ? Seg(t, 0, .1) : 1 - Seg(t, .1, .8));
            // the fissures flash white-hot for a beat
            double f = t < .05 ? Seg(t, 0, .05) : 1 - Seg(t, .05, .35);
            foreach (var fl in flares) fl.Opacity = f;
            _footPlate.Opacity = t < .06 ? Seg(t, 0, .06) : 1 - Seg(t, .6, 1);
            // the jets: each shoots up out of the river on its own beat, overshoots and falls back; the tall ones, up among the
            // rows, stay translucent behind the words; a bead of lava flies off each tip and fades
            foreach (var (jet, sc, bead, bt, jh, delay, peak) in _jetParts)
            {
                double k = Seg(t, delay, .5 + delay);
                sc.ScaleY = k < .4 ? 1.15 * Out(Seg(k, 0, .4)) : 1.15 * (1 - Seg(k, .4, 1) * Seg(k, .4, 1));
                jet.Opacity = k is > 0 and < 1 ? peak * (1 - .6 * Seg(k, .6, 1)) : 0;
                double b = Seg(k, .3, 1);
                bt.Y = -jh * 1.1 - 16 * Out(b) + 10 * b * b;
                bead.Opacity = b is > 0 and < 1 ? 1 - b : 0;
            }
        }, () =>
        {
            _fightErupt.Opacity = 0; _jets.Visibility = Visibility.Collapsed;
            _footPlate.Opacity = 0;
            foreach (var fl in flares) fl.Opacity = 0;
            foreach (var (jet, sc, bead, _, _, _, _) in _jetParts) { jet.Opacity = 0; sc.ScaleY = 0; bead.Opacity = 0; }
        });
    }

    /// <summary>
    /// A fight ended: a basalt banner lands over the fight's head with how it went in white-hot and the clock, its cracks
    /// flaring (cooled to ash and a dull seam for a wipe); a kill sends the heat pulse down the flows. It lifts after a moment.
    /// </summary>
    protected override void FightEnded(Encounter e, bool held)
    {
        if (e.Outcome is not ("kill" or "wipe" or "cleared")) return;
        bool wipe = e.Outcome == "wipe";
        var band = new Grid { VerticalAlignment = VerticalAlignment.Top };
        band.Children.Add(new TacSketch((dc, s) => Slab(dc, s, 1, 31, ash: wipe)));
        var flare = new TacSketch((dc, s) => Flare(dc, s, 31)) { Opacity = 0, Visibility = wipe ? Visibility.Collapsed : Visibility.Visible };
        band.Children.Add(flare);
        var words = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(12, 2, 10, 2), VerticalAlignment = VerticalAlignment.Center };
        var ot = Tt(OutcomeText(e), 22, wipe ? Frozen(Color.FromRgb(0xE8, 0xDE, 0xD8)) : WhiteHot, FontWeights.Black, Heavy);
        var otHost = new Border { Child = ot, Effect = wipe ? null : Underglow() };
        words.Children.Add(otHost);
        var ck = Tt(Clock(e.Duration(e.End ?? DateTime.UtcNow)), 14, wipe ? Soft : Frozen(LavaYelC), FontWeights.Bold); ck.Margin = new Thickness(10, 3, 0, 0);
        words.Children.Add(ck);
        // the boss's name kept on the banner, after the outcome and the clock
        var who = Tt(e.Title, 11.5, Soft, FontWeights.SemiBold); who.Margin = new Thickness(10, 3, 0, 0); who.TextTrimming = TextTrimming.CharacterEllipsis;
        who.MaxWidth = Math.Max(40, FightWidth - 10 - 40 - Mod.W(otHost) - Mod.W(ck) - 50);
        words.Children.Add(who);
        band.Children.Add(words);
        var slam = new ScaleTransform(1, 1);
        band.RenderTransformOrigin = new Point(.5, .5); band.RenderTransform = slam;
        // the shock ring off the slam: one ellipse's outline, drawn once, scaled out and faded
        var ring = new TacSketch((dc, s) =>
        {
            var c = new Point(s.Width / 2, s.Height / 2);
            dc.DrawEllipse(null, Mod.P(Mod.Br(wipe ? AshC : LavaC, .4), 8), c, s.Width / 2 - 5, s.Height / 2 - 5);
            dc.DrawEllipse(null, Mod.P(Mod.Br(wipe ? AshC : LavaHotC, 1), 3), c, s.Width / 2 - 5, s.Height / 2 - 5);
            dc.DrawEllipse(null, Mod.P(Mod.Br(wipe ? Colors.White : WhiteHotC, 1), 1), c, s.Width / 2 - 6.5, s.Height / 2 - 6.5);
        }) { Width = Math.Max(120, FightWidth - 16), Height = 44, Opacity = 0, VerticalAlignment = VerticalAlignment.Top, HorizontalAlignment = HorizontalAlignment.Center };
        var ringS = new ScaleTransform(.3, .3);
        ring.RenderTransformOrigin = new Point(.5, .5); ring.RenderTransform = ringS;
        var drop = new TranslateTransform();
        var tg = new TransformGroup(); tg.Children.Add(slam); tg.Children.Add(drop);
        band.RenderTransform = tg;
        _fightFx.Children.Clear(); _fightFx.Children.Add(band); _fightFx.Children.Add(ring);
        void Seat()
        {
            double y = _fightBody.Margin.Top, h = 22;
            if (_head is { } hd && hd.ActualHeight > 0 && hd.IsDescendantOf(_fight)) { y = hd.TranslatePoint(new Point(0, 0), _fight).Y; h = hd.ActualHeight; }
            band.Margin = new Thickness(3, Math.Max(2, y - 5), 3, 0);
            band.Height = h + 12;
            ring.Margin = new Thickness(0, Math.Max(0, y - 5) + (h + 12) / 2 - 22, 0, 0);
        }
        Seat();
        bool seated = _head is { ActualHeight: > 0 };
        var head = _head;
        Motion.Play(_fightFx, 2400, t =>
        {
            if (!seated && _head is { ActualHeight: > 0 }) { Seat(); seated = true; }   // seated once, after the head's first layout
            double i = Seg(t, 0, .1), o = Seg(t, .84, 1);
            band.Opacity = Math.Min(1, i * 3) * (1 - o);
            // the slam: dropped in from above, a touch large, landing past its size and settling
            drop.Y = -24 * (1 - i * i);
            slam.ScaleX = slam.ScaleY = t < .1 ? 1.15 : 1 - .04 * Math.Sin(Math.PI * Seg(t, .1, .22));
            // the shock ring off the landing, and the fissures flaring thick with it
            double r = Seg(t, .1, .5);
            ringS.ScaleX = .3 + 1.1 * Out(r); ringS.ScaleY = .3 + .7 * Out(r);
            ring.Opacity = t is >= .1 and < .5 ? 1 - r * r : 0;
            flare.Opacity = t < .1 ? Seg(t, 0, .1) : 1 - Seg(t, .1, .7);
            if (head != null) head.Opacity = t < .84 ? 1 - Seg(t, .06, .1) : Seg(t, .84, 1);
        }, () => { _fightFx.Children.Clear(); if (head != null) head.Opacity = 1; });
        if (!wipe) { Erupt(1000); Heat(); }
    }
}
