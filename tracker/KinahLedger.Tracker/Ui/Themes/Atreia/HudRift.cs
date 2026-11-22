using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using KinahLedger.Parser;
using KinahLedger.Session;
using static KinahLedger.Ui.HudKit;

namespace KinahLedger.Ui;

/// <summary>
/// Rift (the Atreia theme, the second looks pack, 2026-11-20): the Abyss torn open. Every surface is deep violet-black glass
/// (a nebula and a field of stars in it, drawn once) with a thin tear of light along its top edge, violet blooming to a
/// near-white cyan core. The meter's bars rise from the Abyss's violet into each member's class colour, and each bar's
/// leading edge is a fissure of rift light (a white core down a jagged line, a cyan rim, a violet bloom), the leader's the
/// brightest; over the leader's emblem floats a crown of three crystal shards. Figures are starlight white with a violet glow.
/// The Buddy is the same glass: the run's ring as a rift portal (violet, a point of light at the arc's head), the hour big in
/// starlight, the pickups as tiles rimmed in violet, the timers as violet glass chips.
/// The moments are rift light, never motion of a figure: a pickup sends a pulse along the tear; a rare drop's own panel
/// tears open in its grade's colour; your best passed runs the portal round to full; an entry opening lights its chip; a
/// fight settles in, the crown lands on its leader in a flash, and a pulse runs the seams (the tear, then each bar's edge
/// in rank order), as it does on a new leader and on the fight's end, under a plate saying how it ended.
/// </summary>
sealed partial class HudRift : ModernLook
{
    const double Radius = 12, Pad = 11;
    static readonly SolidColorBrush White = Frozen(StarC), Soft = Frozen(Mod.A(Color.FromRgb(0xEE, 0xE8, 0xFF), .86)), Quiet = Frozen(Mod.A(Color.FromRgb(0xD8, 0xCC, 0xFF), .62)),
        Lilac = Frozen(LilacC), Mint2 = Frozen(Color.FromRgb(0x8C, 0xFF, 0xD2)), Coral = Frozen(CoralC), Gold2 = Frozen(GoldC2), Cyan = Frozen(CyanC), Ink = Frozen(Color.FromRgb(0x16, 0x08, 0x30));
    /// <summary>The pickup strip on the glass: 26 px tiles beside the hour, a violet rule, the gold star; the tiles rimmed in violet (<see cref="VioletRims"/>).</summary>
    static readonly PickStrip.Look Strip = new(26, 4, true, Mod.Br(LilacC, .38), Frozen(GoldC2));

    // the room round the panels for the drop under them (drawn inside the window: nothing past its edge)
    readonly Grid _root = new() { Margin = new Thickness(10, 8, 98, 16), HorizontalAlignment = HorizontalAlignment.Left };
    readonly StackPanel _body = new() { HorizontalAlignment = HorizontalAlignment.Left };
    double _opacity = 0.9;

    // the Buddy: its glass, the run's head over its words, the ring, the hour, the session and the timer chips
    readonly Grid _line = new() { HorizontalAlignment = HorizontalAlignment.Left };
    readonly TacSketch _lineArt;
    readonly StackPanel _lineStack = new();
    readonly StackPanel _lineBody = new() { Orientation = Orientation.Horizontal, Margin = new Thickness(8, 18, 14, 8) };
    readonly StackPanel _timers = new() { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
    readonly Border _timersInLine = new() { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 0) };
    /// <summary>The timer chips over the line, when the pickup strip takes their room on it.</summary>
    readonly Border _timersUp = new() { HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(2, 0, 0, 6), Visibility = Visibility.Collapsed };
    bool _dim;
    TacSketch? _ringArt;
    Grid? _ringHost;
    double _share = -1;
    bool _beat, _ringEmpty;
    /// <summary>The pulses of light along the Buddy's and the meter's tears (layers over the glass: the glass is never drawn again for them).</summary>
    readonly RiftPulse _linePulse = new(Radius), _fightPulse = new(Radius);
    /// <summary>The wound flaring: its frozen drawing again over the glass, only faded in and out.</summary>
    readonly TacSketch _lineFlare = new((dc, s) => dc.DrawDrawing(WoundArt(Math.Round(s.Width), CyanC))) { Opacity = 0, IsHitTestVisible = false },
        _fightFlare = new((dc, s) => dc.DrawDrawing(WoundArt(Math.Round(s.Width), CyanC))) { Opacity = 0, IsHitTestVisible = false };
    /// <summary>The Buddy's moments' layer, clipped to its glass (a best's burst never leaves it).</summary>
    readonly Grid _lineFx = new() { IsHitTestVisible = false };
    Border? _firstChip;

    // the rare drop under the Buddy (its own glass), and the notice line
    readonly Grid _rare = new() { HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 6, 0, 0), Visibility = Visibility.Collapsed };
    readonly Grid _note = new() { HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 6, 0, 0), Visibility = Visibility.Collapsed };

    // the fight: its glass, its head, HP, rows and foot
    readonly Grid _fight = new() { HorizontalAlignment = HorizontalAlignment.Left, Visibility = Visibility.Collapsed, Margin = new Thickness(0, 0, 0, 8) };
    readonly TacSketch _fightArt;
    readonly StackPanel _fightBody = new() { Margin = new Thickness(Pad, 19, Pad, 9) };
    readonly Border _timersInFight = new() { Margin = new Thickness(0, 0, 0, 8), HorizontalAlignment = HorizontalAlignment.Left };
    readonly Grid _fightFx = new() { IsHitTestVisible = false };
    readonly TranslateTransform _settle = new();
    /// <summary>The meter glass's punch (a kill's hit), under its settle.</summary>
    readonly ScaleTransform _fightPunch = new();
    /// <summary>The moments' layer over the whole look (AtreiaFx): each moment a stage of its own on it, clipped to the window.</summary>
    readonly Canvas _fx = new() { IsHitTestVisible = false };
    /// <summary>Where a pickup's worth comes in, beside the Buddy (in the room at the root's right).</summary>
    readonly Canvas _lineAnchor = Tac.Anchor(HorizontalAlignment.Right, VerticalAlignment.Center);
    FrameworkElement? _head;

    public HudRift() : base(bare: true)
    {
        // the look's own rows (Ui/Themes/ThemeRows.cs), a fit shared by the rows so the share column holds or goes for all
        var fit = new RiftFit();
        UseRows(RiftRow.Pitch, m => new RiftRow(this, fit), 15, GapLine);
        _lineArt = new TacSketch((dc, s) => RiftPanel(dc, s, Radius, _opacity, wound: true));
        _lineStack.Children.Add(_runHead); _lineStack.Children.Add(_lineBody);
        _line.Children.Add(_lineArt); _line.Children.Add(_lineFlare); _line.Children.Add(_linePulse); _line.Children.Add(_lineStack); _line.Children.Add(_lineFx); _line.Children.Add(_lineAnchor);
        _lineFx.SizeChanged += (_, _) => { var g = new RectangleGeometry(new Rect(0, 0, _lineFx.ActualWidth, _lineFx.ActualHeight), Radius, Radius); g.Freeze(); _lineFx.Clip = g; };
        _fightArt = new TacSketch((dc, s) => RiftPanel(dc, s, Radius, _opacity, wound: true));
        _deepHost.Children.Add(_deep);
        _deepHost.SizeChanged += (_, _) => ClipDeep();
        _fight.Children.Add(_fightArt); _fight.Children.Add(_deepHost); _fight.Children.Add(_fightFlare); _fight.Children.Add(_fightPulse); _fight.Children.Add(_fightBody); _fight.Children.Add(_fightFx);
        _fight.RenderTransform = new TransformGroup { Children = { _fightPunch, _settle } };
        _body.Children.Add(_fight); _body.Children.Add(_timersUp); _body.Children.Add(_line); _body.Children.Add(_rare); _body.Children.Add(_note);
        _root.Children.Add(_body); _root.Children.Add(_fx);
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

    // ================================================================ small parts

    /// <summary>The violet glow round a big figure in starlight (the hour, the clock): one effect on a few texts.</summary>
    static DropShadowEffect Glow(double blur = 10, double a = .8) => new() { Color = VioletC, ShadowDepth = 0, BlurRadius = blur, Opacity = a };

    /// <summary>A small violet glass chip with a lilac hairline (★ DPS, the heal mark, AVG, RARE DROP); <paramref name="filled"/>: starlight with dark violet words (YOU).</summary>
    static Border Mark(string s, Brush? fg = null, Color? edge = null, bool filled = false, double size = 8.5)
    {
        var t = Tt(s, size, filled ? Ink : fg ?? White, FontWeights.Bold);
        return new Border
        {
            Child = t, CornerRadius = new CornerRadius(4), Padding = new Thickness(4, 0, 4, 1), VerticalAlignment = VerticalAlignment.Center,
            Background = filled ? White : Frozen(Mod.A(Mod.Mix(DeepVioletC, edge ?? VioletC, .2), .78)), BorderThickness = new Thickness(1),
            BorderBrush = filled ? Frozen(LightC) : Frozen(Mod.A(edge ?? LilacC, edge == null ? .5 : .7)),
        };
    }

    /// <summary>A seam across the glass (the foot's, the card's): violet at its ends, a breath of rift light in its middle.</summary>
    static FrameworkElement Seam(Thickness m, double a = 1) => new TacSketch((dc, s) =>
        dc.DrawRectangle(Mod.Grad(false, (Mod.A(VioletC, .06 * a), 0), (Mod.A(LilacC, .3 * a), .5), (Mod.A(VioletC, .06 * a), 1)), null, new Rect(0, 0, s.Width, 1))) { Height = 1, Margin = m };

    /// <summary>The pickup strip's plain tiles rimmed in the rift's violet (a grade's own colour kept); the best drop's star stays gold.</summary>
    static FrameworkElement VioletRims(FrameworkElement strip)
    {
        void Walk(DependencyObject d)
        {
            if (d is Border { CornerRadius.TopLeft: 5, BorderBrush: SolidColorBrush b } box && Math.Abs(b.Color.R - b.Color.G) < 24 && Math.Abs(b.Color.G - b.Color.B) < 24)
            {
                box.BorderBrush = Frozen(Mod.A(LilacC, .62));
                return;
            }
            int n = VisualTreeHelper.GetChildrenCount(d);
            if (n == 0 && d is Panel p) foreach (var c in p.Children.OfType<DependencyObject>()) Walk(c);
            else if (n == 0 && d is Decorator dec && dec.Child != null) Walk(dec.Child);
            else for (int i = 0; i < n; i++) Walk(VisualTreeHelper.GetChild(d, i));
        }
        Walk(strip);
        return strip;
    }

    // ================================================================ the Buddy

    protected override void DrawLine(Figures f)
    {
        _lineBody.Children.Clear();
        _ringArt = null; _timerUp = false; _stripTimerW = -1;
        if (_timersInLine.Child != null) _timersInLine.Child = null;
        _dim = Dimmed(Values);
        _lineStack.Opacity = _dim ? .62 : 1;
        _lineBody.Margin = new Thickness(8, 18, 14, 8);
        bool mark = f.Abyss && f.Counted && f.Icon.Length > 0;
        if (mark) { var m = AbyssMark(f.Icon, 40); m.Margin = new Thickness(0, 0, 10, 0); _lineBody.Children.Add(m); }
        // in a dungeon run: the run's clock, splits and verdict in place of the hour (HudRift.Run.cs)
        var rs = RunNow();
        _runHead.Visibility = Visibility.Collapsed;
        if (rs != null) DrawRun(rs);
        else if (!HasHour(f))
        {
            // no hour yet: the portal closed (an empty ring), the waiting words beside it
            if (!mark) _lineBody.Children.Add(Ring(-1, false, true));
            // and the figures there are under the words (in the Abyss: "2 kills · Kinah"): information, never an empty glass
            var w = Tt(Waiting(f.Abyss), 13, Soft, FontWeights.SemiBold);
            var sub = Sub(f, false);
            var words = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(mark ? 0 : 2, 0, 0, 0) };
            words.Children.Add(w);
            if (sub.Inlines.Count > 0) words.Children.Add(sub);
            _lineBody.Children.Add(words);
        }
        else
        {
            if (HasRing(f)) _lineBody.Children.Add(Ring(f.Share, f.Beat, false));
            else if (!mark) _lineBody.Children.Add(new Border { Width = 6 });
            // the hour big in starlight, its unit small; the session and the latest pickup under it
            var words = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 0, 1) };
            var top = new StackPanel { Orientation = Orientation.Horizontal };
            var rate = Tt(f.Rate, f.Warm ? 15 : 23, f.Warm ? Soft : White, f.Warm ? FontWeights.SemiBold : FontWeights.Bold);
            rate.Tag = "buddy-rate";
            if (!f.Warm) rate.Effect = Glow();
            top.Children.Add(rate);
            if (f.Unit.Length > 0) { var u = Tt(f.Unit, 11.5, Lilac, FontWeights.SemiBold); u.VerticalAlignment = VerticalAlignment.Bottom; u.Margin = new Thickness(2, 0, 0, 4); top.Children.Add(u); }
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
                if (strip != null) { sub = bare; strip.Margin = new Thickness(14, 0, 0, 0); VioletRims(strip); }
                // no room beside the hour: under the session, never dropped (Steven, 2026-11-19)
                else if ((under = StripUnder(Strip, false)) != null) { sub = bare; VioletRims(under); }
                _stripTimerW = timerW;
            }
            if (sub.Inlines.Count > 0) words.Children.Add(sub);
            if (under != null) words.Children.Add(under);
            _lineBody.Children.Add(words);
            if (strip != null) _lineBody.Children.Add(strip);
        }
        _lineBody.Children.Add(_timersInLine);
        PlaceTimers();
        _lineArt.InvalidateVisual();
        DrawRare();
        DrawNote(f);
    }

    /// <summary>The run's ring as a rift portal: <paramref name="share"/> of your best round it (<paramref name="empty"/>: closed, no hour yet).</summary>
    FrameworkElement Ring(double share, bool beat, bool empty)
    {
        _share = share; _beat = beat; _ringEmpty = empty;
        var ring = new Grid { Width = 44, Height = 44, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 11, 0) };
        _ringArt = new TacSketch(DrawRing, 44, 44);
        _ringHost = ring;
        ring.Children.Add(_ringArt);
        if (!empty)
        {
            var pc = Tt(beat ? "★" : Pct(share), beat ? 15 : 13.5, White, FontWeights.Bold); pc.HorizontalAlignment = HorizontalAlignment.Center;
            ring.Children.Add(pc);
        }
        return ring;
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
            var c = ReferenceEquals(tone, Rec) ? CoralC : ReferenceEquals(tone, Gold) ? GoldC2 : StarC;
            sub.Inlines.Add(new System.Windows.Documents.Run(fig) { FontWeight = FontWeights.Bold, Foreground = Frozen(c) });
            sub.Inlines.Add(new System.Windows.Documents.Run(" " + word));
        }
        if (pick && !f.Abyss && Picks(Values).FirstOrDefault() is { } p)
        {
            Dot();
            sub.Inlines.Add(new System.Windows.Documents.Run(p.Name.Length > 22 ? p.Name[..21].TrimEnd() + "…" : p.Name));
            var worth = p.Worth.Length > 0 ? "+" + p.Worth.TrimStart('+') : p.Count;
            if (worth.Length > 0) sub.Inlines.Add(new System.Windows.Documents.Run(" " + worth) { FontWeight = FontWeights.Bold, Foreground = p.Worth.Length > 0 ? (p.Kinah ? Gold2 : Mint2) : Soft });
        }
        return sub;
    }

    /// <summary>The timer chips' room at the line's end, 0 with none.</summary>
    double LineTimerW() => _timers.Children.Count > 0 && !KinahHidden ? 12 + Mod.W(_timers) : 0;

    /// <summary>The timers' room the line's strip was fitted to (-1: no strip time); chips that change it draw the line again.</summary>
    double _stripTimerW = -1;

    /// <summary>The timer chips sit over the line, to leave its room to the pickup strip.</summary>
    bool _timerUp;

    Color _rareC;
    FrameworkElement? _rareTile;
    /// <summary>The rare drop's moment, drawn once and only faded and stretched while it plays: the grade's light behind the tile, the fissure beside it, the rifts along the pill's top and foot, the pulse.</summary>
    FrameworkElement? _rareGlow, _rareFissure, _rareTears;
    readonly ScaleTransform _rareRun = new(1, 1, 40, 0);
    RiftPulse? _rarePulse;

    /// <summary>The rare drop the window holds, on its own glass under the Buddy: its tile, RARE DROP, its name in its grade's colour; the tear in that colour.</summary>
    void DrawRare()
    {
        _rare.Children.Clear();
        if (!KinahHidden && Rare(Values) is { } r)
        {
            var c = ColorOf(r.Color, r.Grade);
            _rareC = c;
            _rare.Children.Add(new TacSketch((dc, s) =>
            {
                RiftPanel(dc, s, s.Height / 2, _opacity, tint: c, flare: .25, shadow: false);
                TornEnds(dc, s, c);
                // the grade's light breathing in the glass behind the tile
                dc.PushOpacity(.35);
                dc.DrawRectangle(Radial(new Point(.5, .5), .5, .5, (Mod.A(c, .6), 0), (Mod.A(c, 0), 1)), null, new Rect(0, -4, 50, s.Height + 8));
                dc.Pop();
            }));
            // the moment's layer (clipped to the pill): the glass torn open, a fissure of the grade's light beside the tile and a
            // rift of it running out along the pill's top and foot (the words between them kept clear), then closing
            var lc = Mod.Mix(c, Colors.White, .35);
            var fx = new Grid { IsHitTestVisible = false, ClipToBounds = true };
            fx.SizeChanged += (_, _) => { var g = new RectangleGeometry(new Rect(0, 0, fx.ActualWidth, fx.ActualHeight), fx.ActualHeight / 2, fx.ActualHeight / 2); g.Freeze(); fx.Clip = g; };
            _rareGlow = new TacSketch((dc, s) => dc.DrawRectangle(Radial(new Point(.5, .5), .5, .5, (Mod.A(c, .7), 0), (Mod.A(c, 0), 1)), null, new Rect(0, -6, 150, s.Height + 12))) { Opacity = 0 };
            _rareFissure = new TacSketch((dc, s) => Fissure(dc, 45, 2, s.Height - 4, c, 1.9, 2)) { Opacity = 0 };
            _rareTears = new TacSketch((dc, s) =>
            {
                double x1 = s.Width - s.Height / 2;
                foreach (var y in new[] { 2.2, s.Height - 2.2 })
                {
                    dc.DrawGeometry(Mod.Grad(false, (Mod.A(lc, .95), 0), (Mod.A(lc, .8), .7), (Mod.A(c, 0), 1)), null, TearAcross(40, x1 + 6, y, 5, y > 3 ? 7 : 4));
                    dc.DrawGeometry(Mod.Grad(false, (Mod.A(Colors.White, 1), 0), (Mod.A(Colors.White, .7), .6), (Mod.A(Colors.White, 0), 1)), null, TearAcross(40, x1, y, 1.4, 5));
                }
            }) { Opacity = 0, RenderTransform = _rareRun };
            _rarePulse = new RiftPulse(99);
            _rarePulse.Tint(c);
            foreach (var el in new UIElement[] { _rareGlow, _rareFissure, _rareTears, _rarePulse }) fx.Children.Add(el);
            _rare.Children.Add(fx);
            var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(13, 6, 26, 8) };   // clear of the torn ends
            var tile = Tile(r, 26); tile.Margin = new Thickness(0, 0, 9, 0);
            _rareTile = tile;
            row.Children.Add(tile);
            var mk = Mark(L.T("RARE DROP"), size: 8, edge: Mod.Mix(c, Colors.White, .2)); mk.Margin = new Thickness(0, 0, 8, 0);
            row.Children.Add(mk);
            var nm = Tt(r.Name, 13.5, Frozen(Mod.Mix(c, Colors.White, .4)), FontWeights.Bold);
            row.Children.Add(nm);
            if (r.Count.Length > 0) { var cn = Tt(" " + r.Count, 11, Soft, FontWeights.SemiBold); cn.Margin = new Thickness(2, 1, 0, 0); row.Children.Add(cn); }
            _rare.Children.Add(row);
            _rare.Visibility = Visibility.Visible;
        }
        else { _rare.Visibility = Visibility.Collapsed; _rareGlow = _rareFissure = _rareTears = null; _rarePulse = null; _rareTile = null; }
    }

    /// <summary>The notice: its words in their tone on a slim pill of the glass, the warning sign before a pause.</summary>
    void DrawNote(Figures f)
    {
        _note.Children.Clear();
        if (f.Note.Length > 0 && !KinahHidden)
        {
            _note.Children.Add(new TacSketch((dc, s) => RiftPanel(dc, s, s.Height / 2, _opacity, shadow: false, tear: .55)));
            var sp = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(12, 5, 13, 6) };
            bool warn = ReferenceEquals(f.NoteTone, Warn);
            if (warn) { var wi = WarnIcon(); wi.Width = wi.Height = 13; wi.Margin = new Thickness(0, 0, 6, 0); sp.Children.Add(wi); }
            var tone = warn ? WarnC2 : ReferenceEquals(f.NoteTone, Gold) ? GoldC2 : ReferenceEquals(f.NoteTone, HudKit.Mint) ? MintC2 : Color.FromRgb(0xF0, 0xEC, 0xFF);
            var t = Tt(f.Note, 11.5, Frozen(tone), FontWeights.SemiBold); t.TextTrimming = TextTrimming.CharacterEllipsis; t.MaxWidth = Math.Max(200, FightWidth - 40);
            sp.Children.Add(t);
            _note.Children.Add(sp);
            _note.Visibility = Visibility.Visible;
        }
        else _note.Visibility = Visibility.Collapsed;
    }

    /// <summary>
    /// The rift portal: a dark violet well, a violet bloom round its rim, the track in deep violet; your best's share as an arc
    /// rising from violet to the rift's cyan with a point of light at its head (the light through the gap). Beaten: a whole
    /// ring of rift light. Closed (no hour yet): the track alone.
    /// </summary>
    void DrawRing(DrawingContext dc, Size s)
    {
        double c = s.Width / 2, r = c - 5;
        var center = new Point(c, c);
        dc.DrawEllipse(Radial(new Point(.5, .5), .5, .5, (Mod.A(VoidC, .85), 0), (Mod.A(DeepVioletC, .7), .8), (Mod.A(VioletC, .25), 1)), null, center, r + 1, r + 1);
        dc.DrawEllipse(null, Mod.P(Mod.Br(VioletC, .2), 7.5), center, r, r);
        dc.DrawEllipse(null, Mod.P(Mod.Br(Color.FromRgb(0x3A, 0x1E, 0x70), 1), 3.4), center, r, r);
        if (_ringEmpty)
        {
            dc.DrawEllipse(null, Mod.P(Mod.Br(LilacC, .55), 1.2), center, r + 1.7, r + 1.7);
            dc.DrawEllipse(null, Mod.P(Mod.Br(VioletC, .6), 3.4), center, r, r);
            dc.PushOpacity(.6); dc.DrawRectangle(Spark, null, new Rect(c - 6, c - r - 6, 12, 12)); dc.Pop();
            return;
        }
        double share = Math.Clamp(_beat ? 1 : _share, 0, 1);
        double shown = share;
        bool full = shown >= .999;
        var arc = Mod.Grad(true, (LilacC, 0), (VioletC, .55), (CyanC, 1));
        if (full)
        {
            dc.DrawEllipse(null, Mod.P(Mod.Br(CyanC, .3), 7.5), center, r, r);
            dc.DrawEllipse(null, Mod.P(Mod.Grad(true, (LightC, 0), (CyanC, .6), (LilacC, 1)), 3.4), center, r, r);
            return;
        }
        if (shown < .004) return;
        double a = 2 * Math.PI * shown;
        var fig = new PathFigure { StartPoint = new Point(c, c - r) };
        fig.Segments.Add(new ArcSegment(new Point(c + r * Math.Sin(a), c - r * Math.Cos(a)), new Size(r, r), 0, a > Math.PI, SweepDirection.Clockwise, true));
        var pg = new PathGeometry(); pg.Figures.Add(fig); pg.Freeze();
        dc.DrawGeometry(null, Mod.P(arc, 3.4, round: true), pg);
        // the light at the arc's head: the gap the rift's light comes through (brighter as it runs round)
        var p = new Point(c + r * Math.Sin(a), c - r * Math.Cos(a));
        const double g = 0;
        dc.DrawRectangle(Spark, null, new Rect(p.X - 6 - 3 * g, p.Y - 6 - 3 * g, 12 + 6 * g, 12 + 6 * g));
        dc.DrawEllipse(Frozen(Colors.White), null, p, 1.7 + .5 * g, 1.7 + .5 * g);
    }

    protected override void KinahShown(bool shown)
    {
        _line.Visibility = shown ? Visibility.Visible : Visibility.Collapsed;
        if (!shown) { _note.Visibility = Visibility.Collapsed; _rare.Visibility = Visibility.Collapsed; }
        _fight.Margin = new Thickness(0, 0, 0, shown ? 8 : 0);
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
            bool first = _timers.Children.Count == 0, on = tone == TimerBadge.Tone.On;
            var c = on ? MintC2 : tone == TimerBadge.Tone.Soon ? GoldC2 : LilacC;
            var sp = new StackPanel { Orientation = Orientation.Horizontal };
            if (tone != TimerBadge.Tone.Quiet)
                sp.Children.Add(new System.Windows.Shapes.Ellipse { Width = first ? 6.5 : 5, Height = first ? 6.5 : 5, Fill = Frozen(c), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) });
            var t = Tt(text, first ? 11.5 : 10.5, first ? White : Soft, first ? FontWeights.Bold : FontWeights.SemiBold);
            if (on) t.Foreground = Frozen(Mod.Mix(MintC2, Colors.White, .45));
            else if (tone == TimerBadge.Tone.Soon) t.Foreground = Frozen(Mod.Mix(GoldC2, Colors.White, .35));
            sp.Children.Add(t);
            var chip = new Border
            {
                Child = sp, CornerRadius = new CornerRadius(99), Padding = first ? new Thickness(9, 4, 11, 5) : new Thickness(8, 3, 10, 4), VerticalAlignment = VerticalAlignment.Center,
                Background = Mod.Grad(true, (Mod.A(DuskC, .9), 0), (Mod.A(VoidC, .94), 1)), BorderThickness = new Thickness(1),
                BorderBrush = Frozen(Mod.A(on ? MintC2 : tone == TimerBadge.Tone.Soon ? GoldC2 : LilacC, on ? .6 : .38)),
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

    protected override void DrawFight(Encounter? e, DateTime now)
    {
        _fightBody.Children.Clear();
        if (e == null) { _fight.Visibility = Visibility.Collapsed; _fightFx.Children.Clear(); PlaceTimers(); return; }
        double w = FightWidth - 8;
        _fight.Width = w;
        double inner = w - 2 * Pad;
        _fightBody.Children.Add(_timersInFight);
        // the head: the boss, the outcome once it's over, the clock in starlight
        var head = new Grid();
        head.ColumnDefinitions.Add(new ColumnDefinition()); head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var title = Tt(e.Title, 14.5, White, FontWeights.SemiBold); title.TextTrimming = TextTrimming.CharacterEllipsis;
        head.Children.Add(title);
        var right = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(8, 0, 0, 0) };
        if (e.Waiting) right.Children.Add(Mark(L.T("READY"), Mint2, MintC2, size: 9.5));
        else
        {
            var outcome = OutcomeText(e);
            if (outcome.Length > 0)
            {
                bool wipe = e.Outcome == "wipe";
                var oc = Mark(outcome, wipe ? Frozen(Mod.Mix(CoralC, Colors.White, .3)) : Mint2, wipe ? CoralC : MintC2, size: 10);
                oc.CornerRadius = new CornerRadius(99); oc.Padding = new Thickness(8, 1, 8, 2); oc.Margin = new Thickness(0, 1, 8, 0); oc.Tag = "outcome";
                right.Children.Add(oc);
            }
            var clock = Tt(Clock(e, now), 18, White, FontWeights.Bold); clock.Tag = "fight-clock"; clock.Effect = Glow(9, .75);
            right.Children.Add(clock);
        }
        Grid.SetColumn(right, 1); head.Children.Add(right);
        _head = head;
        _fightBody.Children.Add(head);
        if (e.Waiting)
        {
            var wt = Tt(e.ReadyText, 12.5, Soft, FontWeights.Medium); wt.Margin = new Thickness(0, 6, 0, 0); wt.TextWrapping = TextWrapping.Wrap;
            _fightBody.Children.Add(wt);
            if (PartyBeforeHit(e)) { _fightBody.Children.Add(new Border { Height = 8 }); PutRows(_fightBody, e, now, inner); }   // (no crown before damage)
        }
        else
        {
            if (HpOf(e) is { } hp)
            {
                // the boss's HP and its words on one line: a slim rose-to-violet bar torn at its end, "47%  ·  1.84M left" at its right
                var hpLine = new Grid { Margin = new Thickness(0, 7, 0, 0) };
                hpLine.ColumnDefinitions.Add(new ColumnDefinition()); hpLine.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                var track = new Grid { Height = 6, VerticalAlignment = VerticalAlignment.Center };
                track.Children.Add(new TacSketch((dc, s) =>
                {
                    var r = new Rect(0, 0, s.Width, s.Height);
                    var shape = Mod.Pill(r, 3);
                    dc.DrawGeometry(Mod.Br(VoidC, .85), Mod.P(Mod.Br(LilacC, .16), 1), shape);
                    dc.PushClip(shape);
                    double x1 = s.Width * Math.Clamp(hp, 0, 1);
                    if (x1 > .5)
                    {
                        dc.DrawRectangle(Mod.Grad(false, (HpAC, 0), (HpBC, 1)), null, new Rect(0, 0, x1, s.Height));
                        dc.DrawRectangle(Mod.Br(Colors.White, .28), null, new Rect(0, 0, x1, 1));
                    }
                    for (int k = 1; k <= 2; k++) { double x = Math.Round(s.Width * k / 3); dc.DrawRectangle(Mod.Br(VoidC, .7), null, new Rect(x - 1, 0, 2, s.Height)); }
                    dc.Pop();
                    if (x1 > 2) Fissure(dc, x1, 0, s.Height, HpBC, .7, 5);
                }));
                HpGhost(track, e, hp, now, 3);
                hpLine.Children.Add(track);
                var hl = Tt("", 11, Soft, FontWeights.Medium);
                hl.Margin = new Thickness(10, 0, 0, 0);
                hl.Inlines.Add(new System.Windows.Documents.Run($"{Math.Round(hp * 100):0}%") { FontWeight = FontWeights.Bold, Foreground = hp < .34 ? Frozen(Color.FromRgb(0xFF, 0x9A, 0xB8)) : White });
                var left = HpLeftText(e);
                if (left.Length > 0) hl.Inlines.Add(new System.Windows.Documents.Run("  ·  " + left));
                hl.Tag = "hp-text";
                Grid.SetColumn(hl, 1); hpLine.Children.Add(hl);
                _fightBody.Children.Add(hpLine);
                _fightBody.Children.Add(new Border { Height = 18 });   // the room the leader's crown floats in
            }
            else _fightBody.Children.Add(new Border { Height = 18 });
            PutRows(_fightBody, e, now, inner);
            if (e.HasDamage)
            {
                _fightBody.Children.Add(Seam(new Thickness(0, 6, 0, 5)));
                _fightBody.Children.Add(Foot(e, now));
            }
        }
        _fight.Visibility = Visibility.Visible;
        PlaceTimers();
    }

    // ================================================================ the seams and the crown

    long _leadId;
    string _leadFight = "";

    /// <summary>
    /// A pulse of rift light runs the seams: along the meter's tear left to right, then each bar's edge in rank order (a band of
    /// light sweeping up the bar into its fissure, which flares). Transforms and opacity only: nothing is drawn again.
    /// </summary>
    void Seams(Color? c)
    {
        _fightPulse.Tint(c);
        Motion.Play(_fightPulse, 1500, t =>
        {
            _fightPulse.At(t < .4 ? t / .4 : -1);
            _fightFlare.Opacity = t < .08 ? Seg(t, 0, .08) : 1 - Seg(t, .08, .45);
            RowsSeam(t, c);
        }, () => { _fightPulse.At(-1); _fightFlare.Opacity = 0; RowsSeam(-1, c); });
    }

    void RowsSeam(double t, Color? c)
    {
        if (Meter is not { } m) return;
        foreach (var x in m.Members) if (m.RowOf(x.Id) is RiftRow r) r.Seam(t, c);
    }

    /// <summary>The leader's row as drawn: a new leader in the fight seen (with effects playing) lands the crown on them and runs the seams.</summary>
    internal void Lead(RiftRow row, MeterMember m, MeterContext c)
    {
        bool changed = _leadFight == c.E.Id && _leadId != 0 && _leadId != m.Id;
        _leadFight = c.E.Id; _leadId = m.Id;
        if (changed && c.Moves) { row.Land(); Seams(null); }
    }

    RiftRow? LeaderRow() => Meter is { } m && m.Members.FirstOrDefault(x => x.Rank == 1 && !x.CpOnly) is { } top ? m.RowOf(top.Id) as RiftRow : null;

    // ================================================================ the moments

    /// <summary>
    /// A pickup (the small one): a pulse of rift light runs along the Buddy's tear and the worth comes in beside it on a
    /// small violet glass chip, then goes. A rare drop is the big one (<see cref="RareDrop"/>).
    /// </summary>
    protected override void Pickup(Pick p, bool held)
    {
        if (KinahHidden || _line.Visibility != Visibility.Visible) return;
        _linePulse.Tint(null);
        var worth = p.Worth.Length > 0 ? "+" + p.Worth.TrimStart('+') : p.Count;
        var words = new StackPanel { Margin = new Thickness(10, 3, 10, 4) };
        words.Children.Add(Tt(worth, 14, p.Worth.Length > 0 ? Mint2 : White, FontWeights.Bold));
        var nm = Tt(p.Name.Length > 13 ? p.Name[..12].TrimEnd() + "…" : p.Name, 9.5, Soft, FontWeights.SemiBold); nm.Margin = new Thickness(0, -2, 0, 0);
        words.Children.Add(nm);
        var chip = new Border
        {
            Child = words, MaxWidth = 88, CornerRadius = new CornerRadius(7), BorderThickness = new Thickness(1),
            Background = Mod.Grad(true, (Mod.A(DuskC, .95), 0), (Mod.A(VoidC, .96), 1)), BorderBrush = Mod.Grad(true, (Mod.A(CyanC, .85), 0), (Mod.A(VioletC, .5), 1)),
        };
        var shift = new TranslateTransform(); var pop = new ScaleTransform(1, 1);
        chip.RenderTransform = new TransformGroup { Children = { pop, shift } };
        chip.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        Canvas.SetLeft(chip, 6); Canvas.SetTop(chip, -chip.DesiredSize.Height / 2 + 4);
        _lineAnchor.Children.Clear(); _lineAnchor.Children.Add(chip);
        // the newest tile pops (1.15 and back in 0.22 s) under a 0.15 s glint of the tear's cyan (on the look's stage)
        var tile = AtreiaFx.NewestTile(_line);
        var st = AtreiaFx.Stage(_fx, _root);
        var parts = new List<Action<double>> { AtreiaFx.Pop(tile, 0, .116, .15, middle: true) };
        if (tile != null && AtreiaFx.At(tile, _fx, new Point(tile.ActualWidth / 2, tile.ActualHeight / 2)) is { } tc)
            parts.Add(AtreiaFx.Streak(st, tc, 46, 5, CyanC, 0, .03, .08));
        AtreiaFx.Play(_fx, st, 1900, t => AtreiaFx.All(parts, t));
        Motion.Play(_linePulse, 1900, t =>
        {
            _linePulse.At(t < .58 ? Seg(t, 0, .58) : -1);
            _lineFlare.Opacity = t < .05 ? Seg(t, 0, .05) : 1 - Seg(t, .05, .32);
            double k = Back(Seg(t, .06, .24), 3);
            shift.X = -14 * (1 - k);
            pop.ScaleX = pop.ScaleY = AtreiaFx.Punch(t, .06, .4, 0, .05);
            chip.Opacity = t < .82 ? Math.Min(1, Seg(t, .06, .16)) : 1 - Seg(t, .82, 1);
        }, () => { _linePulse.At(-1); _lineFlare.Opacity = 0; _lineAnchor.Children.Remove(chip); });
    }

    /// <summary>A rare drop: its glass tears open in its grade's colour (a fissure flaring behind the tile, the tear lit in the colour), the tile popping in.</summary>
    protected override void RareDrop(Pick p, bool held)
    {
        if (_rareGlow is not { } glow || _rareFissure is not { } fis || _rareTears is not { } tears || _rarePulse is not { } pulse) return;
        var tile = _rareTile;
        var sc = new ScaleTransform(1, 1);
        if (tile != null) { tile.RenderTransformOrigin = new Point(.5, .5); tile.RenderTransform = sc; }
        // the drop comes through the rift: a column of the grade's light pours down onto the tile, it ducks and punches up,
        // a ring of that light and a burst of rays go out, shards of rift glass in its colour fly off and fall
        var c = ColorOf(p.Color, p.Grade);
        var st = AtreiaFx.Stage(_fx, _root);
        var parts = new List<Action<double>>();
        if (AtreiaFx.At(tile, _fx, new Point(13, 13)) is { } at)
        {
            parts.Add(AtreiaFx.Pillar(st, at + new Vector(0, 6), 36, 86, c, 0, .15, .56));
            parts.Add(AtreiaFx.Bloom(st, at, 100, 60, AtreiaFx.Glow(c, .9, Colors.White), .12, .18, .58));
            parts.Add(AtreiaFx.Rays(st, at, 12, 11, 44, 3.4, i => i % 3 == 0 ? LightC : i % 3 == 1 ? Mod.Mix(c, Colors.White, .3) : VioletC, .13, .21, .7, turn: -26, max: .9));
            // the drop tears its own glass open: a line of its colour snapping out along the row from the tile
            parts.Add(AtreiaFx.Streak(st, new Point(at.X + 90, at.Y), 200, 6, c, .14, .22, .6));
            var bits = new AtreiaFx.Bits(st);
            bits.Burst(10, AtreiaFx.Shard, i => AtreiaFx.Br(i % 3 == 0 ? LightC : i % 3 == 1 ? c : LilacC), at, 38, 6, .15, .7, fall: 26, dir: -30, spread: 120, r0: 8, spin: 260, seed: 29);
            parts.Add(bits.At);
        }
        AtreiaFx.Play(_fx, st, 1700, t =>
        {
            AtreiaFx.All(parts, t);
            double k = Math.Sin(Math.PI * Seg(t, .12, .95));
            glow.Opacity = k;
            fis.Opacity = tears.Opacity = Math.Min(1, 1.3 * k);
            _rareRun.ScaleX = Math.Max(.02, Out(Seg(t, .15, .5)));
            pulse.At(Seg(t, .2, .75) is var u && u > 0 && u < 1 ? u : -1);
            sc.ScaleX = sc.ScaleY = AtreiaFx.Punch(t, .05, .62, .2, .25);
        }, () => { glow.Opacity = fis.Opacity = tears.Opacity = 0; pulse.At(-1); _rareRun.ScaleX = 1; sc.ScaleX = sc.ScaleY = 1; });
    }

    /// <summary>Your best passed: the portal's arc runs on round to full, its point of light bright at the head, then the whole ring lights; a pulse crosses the tear.</summary>
    protected override void BestPassed(bool held)
    {
        if (_ringArt is not { } art || _ringHost is not { } host) return;
        double was = _share is > 0 and < 1 ? _share : .84;
        bool beat = _beat;
        // the ring as it was (drawn once), then three parts drawn once over it and only turned, faded and scaled: a point of
        // light running round from the arc's head to the top, the whole ring of rift light, a ring of light bursting out
        _beat = false; _share = was; art.InvalidateVisual();
        const double c = 22, r = 17;
        var turn = new RotateTransform(0, c, c);
        var orbit = new TacSketch((dc, _) =>
        {
            dc.DrawRectangle(Spark, null, new Rect(c - 9, c - r - 9, 18, 18));
            dc.DrawEllipse(Brushes.White, null, new Point(c, c - r), 2.2, 2.2);
        }, 44, 44) { RenderTransform = turn };
        var lit = new TacSketch((dc, _) =>
        {
            dc.DrawEllipse(null, Mod.P(Mod.Br(CyanC, .55), 7.5), new Point(c, c), r, r);
            dc.DrawEllipse(null, Mod.P(Mod.Grad(true, (LightC, 0), (CyanC, .6), (LilacC, 1)), 3.4), new Point(c, c), r, r);
        }, 44, 44) { Opacity = 0 };
        var pop = new ScaleTransform(1, 1, c, c);
        var burst = new System.Windows.Shapes.Ellipse { Width = 2 * r, Height = 2 * r, Margin = new Thickness(c - r), Stroke = Mod.Br(LightC, .9), StrokeThickness = 1.8, Opacity = 0, RenderTransform = pop, IsHitTestVisible = false };
        var parts = new UIElement[] { lit, burst, orbit };
        foreach (var el in parts) host.Children.Insert(1, el);
        // the burst in the Buddy's clipped layer: a shock ring two and a half times the portal, shards, NEW BEST over the wound
        var at0 = host.IsDescendantOf(_line) && host.ActualWidth > 0 ? host.TranslatePoint(new Point(c, c), _line) : new Point(8 + c, 18 + c);
        var shock = new System.Windows.Shapes.Ellipse { Width = 76, Height = 76, Stroke = Mod.Br(LightC, .9), StrokeThickness = 2.4, Opacity = 0, IsHitTestVisible = false, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(at0.X - 38, at0.Y - 38, 0, 0) };
        var shockScale = new ScaleTransform(.3, .3, 38, 38); shock.RenderTransform = shockScale;
        var flying = new Canvas { IsHitTestVisible = false };
        var bits = new List<(UIElement El, TranslateTransform At, double Dx, double Dy)>();
        for (int i = 0; i < 8; i++)
        {
            double a = i * Math.PI / 4 + .3, d = (22 + 6 * (i % 3)) * (Math.Cos(i * Math.PI / 4 + .3) < 0 ? .7 : 1.15);
            var sh = ShardOf(9, 2.6);
            var mv = new TranslateTransform();
            sh.RenderTransform = new TransformGroup { Children = { new RotateTransform(a * 180 / Math.PI + 90), mv } };
            sh.Opacity = 0;
            Canvas.SetLeft(sh, at0.X); Canvas.SetTop(sh, at0.Y);
            flying.Children.Add(sh);
            bits.Add((sh, mv, Math.Cos(a) * d, Math.Sin(a) * d));
        }
        var tag = new Border
        {
            Child = Tt(L.T("NEW BEST"), 10.5, Frozen(LightC), FontWeights.Bold), Padding = new Thickness(8, 0, 8, 1), CornerRadius = new CornerRadius(3),
            Background = Frozen(Mod.A(Color.FromRgb(0x2A, 0x0F, 0x5C), .95)), BorderBrush = Frozen(Mod.A(LightC, .9)), BorderThickness = new Thickness(1),
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 1, 0, 0), Opacity = 0, Tag = "rift-new-best",
        };
        var tagScale = new ScaleTransform(1.5, 1.5); tag.RenderTransformOrigin = new Point(.5, .5); tag.RenderTransform = tagScale;
        var burstParts = new UIElement[] { shock, flying, tag };
        foreach (var el in burstParts) _lineFx.Children.Add(el);
        _linePulse.Tint(MintC2);
        // the portal punching as it closes, a crown of rift light turning out of it (on the look's stage, the same 1.8 s)
        var st = AtreiaFx.Stage(_fx, _root);
        var extra = new List<Action<double>>();
        if (AtreiaFx.At(host, _fx, new Point(c, c)) is { } rc)
        {
            var portal = AtreiaFx.ScaleOn(host, new Point(c, c));
            extra.Add(t => portal.ScaleX = portal.ScaleY = AtreiaFx.Punch(t, 0, .92, .08, .22));
            extra.Add(AtreiaFx.Rays(st, rc, 12, 22, 34, 4.2, i => i % 2 == 0 ? CyanC : VioletC, .3, .38, .8, turn: -34, max: .95));
            extra.Add(AtreiaFx.Bloom(st, rc, 70, 70, AtreiaFx.Glow(CyanC, .9, Colors.White), .3, .35, .66));
            extra.Add(AtreiaFx.Pop(AtreiaFx.Hour(_lineBody), .3, .46, .08));
        }
        AtreiaFx.Play(_fx, st, 1800, t => AtreiaFx.All(extra, t));
        Motion.Play(art, 1800, t0 =>
        {
            // the run round to the top in the first 0.55 s, the burst after it (its parts' times are of the course as it was)
            double t = t0 < .3 ? t0 * .55 / .3 : .55 + (t0 - .3) * .45 / .7;
            double run = Out(Seg(t, 0, .55));
            turn.Angle = 360 * (was + (1 - was) * run);
            orbit.Opacity = t < .55 ? 1 : 1 - Seg(t, .55, .7);
            lit.Opacity = Seg(t, .5, .6);
            double b = Seg(t, .52, .95);
            pop.ScaleX = pop.ScaleY = 1 + .28 * Out(b);
            burst.Opacity = t < .52 ? 0 : .9 * (1 - b);
            _linePulse.At(t is > .45 and < .95 ? Seg(t, .45, .95) : -1);
            double sb = Seg(t, .5, .9);
            shockScale.ScaleX = shockScale.ScaleY = .3 + .7 * Out(sb);
            shock.Opacity = t < .5 ? 0 : .9 * (1 - sb);
            double fly = Out(Seg(t, .5, .95));
            foreach (var bt in bits) { bt.At.X = bt.Dx * fly; bt.At.Y = bt.Dy * fly; bt.El.Opacity = t < .5 ? 0 : 1 - Seg(t, .7, .95); }
            double tg = Seg(t, .5, .62);
            tagScale.ScaleX = tagScale.ScaleY = 1.5 - .5 * Back(tg, 2);
            tag.Opacity = t < .5 ? 0 : tg < 1 ? tg : 1 - Seg(t, .92, 1);
            _lineFlare.Opacity = t < .5 ? 0 : 1 - Seg(t, .5, .9);
        }, () =>
        {
            foreach (var el in burstParts) _lineFx.Children.Remove(el);
            _lineFlare.Opacity = 0;
            foreach (var el in parts) host.Children.Remove(el);
            _beat = beat; _share = was; art.InvalidateVisual();
            _linePulse.At(-1);
        });
    }

    /// <summary>
    /// An entry opened: a rift tears open across the chip (a jagged line of light snapping out sideways from its middle),
    /// the chips duck and punch out, rift light flashes through it, a ring ripples off its edge and shards of the rift's
    /// glass spit out of the tear; then the tear closes and the chip rests.
    /// </summary>
    protected override void EntryOpened(TimerBoard.Item i, bool held)
    {
        if (_firstChip is not { } chip || AtreiaFx.Box(chip, _fx) is not { } r) return;
        var st = AtreiaFx.Stage(_fx, _root);
        var pop = AtreiaFx.ScaleOn(_timers, chip.TranslatePoint(new Point(chip.ActualWidth / 2, chip.ActualHeight / 2), _timers));
        var c = new Point(r.X + r.Width / 2, r.Y + r.Height / 2);
        var parts = new List<Action<double>>
        {
            AtreiaFx.Bloom(st, c, r.Width + 40, r.Height + 30, AtreiaFx.Glow(CyanC, .85, Colors.White), .1, .17, .55, s0: .5),
            AtreiaFx.Streak(st, c, r.Width + 34, 6, VioletC, .08, .16, .5),
            AtreiaFx.Ripple(st, r, r.Height / 2, AtreiaFx.Br(MintC2, .95), 1.8, 6, .14, .45),
        };
        var bits = new AtreiaFx.Bits(st);
        bits.Burst(8, AtreiaFx.Shard, k => AtreiaFx.Br(k % 3 == 0 ? LightC : k % 3 == 1 ? CyanC : LilacC), c, 18, 5.5, .12, .62, fall: 8, r0: r.Width / 2, spin: 200, seed: 13, squash: .42);
        parts.Add(bits.At);
        AtreiaFx.Play(_fx, st, 1150, t =>
        {
            pop.ScaleX = pop.ScaleY = AtreiaFx.Punch(t, 0, .72, .06, .08);
            AtreiaFx.All(parts, t);
        }, () => pop.ScaleX = pop.ScaleY = 1);
    }

    // ---- the Abyss tearing open (a fight's start): parts drawn once, then only clipped, moved, turned and faded

    /// <summary>The deep field of stars behind the meter's glass (a layer of its own, clipped to the glass): still, but for a drift of parallax during a moment.</summary>
    readonly Grid _deepHost = new() { IsHitTestVisible = false };
    readonly TacSketch _deep = new((dc, s) => { dc.PushOpacity(.75); dc.DrawRectangle(DeepStars, null, new Rect(-40, -40, s.Width + 80, s.Height + 80)); dc.Pop(); }) { IsHitTestVisible = false };
    readonly TranslateTransform _deepAt = new();
    Grid? _openFx;
    TacSketch? _flash, _ripFront, _ripFront2;
    readonly TranslateTransform _ripAt2 = new();
    /// <summary>The words' reveal as the rip opens: a mask opaque only inside the slit (its stops moved, nothing laid out).</summary>
    readonly GradientStop _revealA = new(Colors.Transparent, 0), _revealB = new(Colors.Black, 0), _revealC = new(Colors.Black, 1), _revealD = new(Colors.Transparent, 1);
    LinearGradientBrush? _revealBrush;
    LinearGradientBrush _reveal => _revealBrush ??= new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 0), GradientStops = { _revealA, _revealB, _revealC, _revealD } };
    readonly ScaleTransform _flashScale = new();
    readonly TranslateTransform _ripAt = new();
    readonly RectangleGeometry _ripClip = new();
    readonly List<(UIElement El, TranslateTransform At, RotateTransform Turn, double Dx, double Dy, double Spin)> _shards = new();

    void ClipDeep()
    {
        var g = new RectangleGeometry(new Rect(.5, .5, Math.Max(0, _deepHost.ActualWidth - 1), Math.Max(0, _deepHost.ActualHeight - 1)), Radius, Radius); g.Freeze();
        _deepHost.Clip = g;
    }

    void OpenFx()
    {
        _deep.RenderTransform ??= _deepAt;
        if (_openFx != null) return;
        // the layer over the glass, clipped to it, so the flung shards never leave it
        _openFx = new Grid { IsHitTestVisible = false, ClipToBounds = true };
        // the flash: a blinding ellipse of rift light out of the wound's widest point
        _flash = new TacSketch((dc, s) =>
        {
            dc.DrawRectangle(Radial(new Point(WoundPeak, .12), .6, .8, (Mod.A(Colors.White, 1), 0), (Mod.A(LightC, .8), .18), (Mod.A(CyanC, .45), .4), (Mod.A(VioletC, .2), .7), (Mod.A(VioletC, 0), 1)), null, new Rect(0, -14, s.Width, 120));
            dc.DrawRectangle(Mod.Grad(false, (Mod.A(Colors.White, 0), 0), (Mod.A(Colors.White, 1), WoundPeak), (Mod.A(Colors.White, 0), 1)), null, new Rect(0, 1, s.Width, 7));
        })
        { Opacity = 0, RenderTransform = _flashScale, VerticalAlignment = VerticalAlignment.Top, Height = 100 };
        // the rip's fronts: two jagged lines of light running out sideways from the wound as the slit opens
        _ripFront = new TacSketch((dc, s) =>
        {
            dc.DrawRectangle(Mod.Grad(false, (Mod.A(VioletC, 0), 0), (Mod.A(Mod.Mix(VioletC, CyanC, .4), .7), .5), (Mod.A(VioletC, 0), 1)), null, new Rect(0, 0, 24, s.Height));
            dc.DrawGeometry(Mod.Br(Color.FromRgb(0xFF, 0x2E, 0x9A), .7), null, TearDown(12, 0, s.Height, 4.5, 9));
            dc.DrawGeometry(Mod.Br(Colors.White, 1), null, TearDown(12, 2, s.Height - 4, 2, 9));
        }) { Opacity = 0, Width = 24, HorizontalAlignment = HorizontalAlignment.Left, RenderTransform = _ripAt };
        _ripFront2 = new TacSketch((dc, s) =>
        {
            dc.DrawRectangle(Mod.Grad(false, (Mod.A(VioletC, 0), 0), (Mod.A(Mod.Mix(VioletC, CyanC, .4), .7), .5), (Mod.A(VioletC, 0), 1)), null, new Rect(0, 0, 24, s.Height));
            dc.DrawGeometry(Mod.Br(Color.FromRgb(0xFF, 0x2E, 0x9A), .7), null, TearDown(12, 0, s.Height, 4.5, 4));
            dc.DrawGeometry(Mod.Br(Colors.White, 1), null, TearDown(12, 2, s.Height - 4, 2, 4));
        }) { Opacity = 0, Width = 24, HorizontalAlignment = HorizontalAlignment.Left, RenderTransform = _ripAt2 };
        var shards = new Canvas { IsHitTestVisible = false };
        var rnd = new Random(919);
        for (int i = 0; i < 13; i++)
        {
            double dx = (rnd.NextDouble() < .5 ? -1 : 1) * (25 + 120 * rnd.NextDouble()), dy = 18 + 70 * rnd.NextDouble();
            var sh = ShardOf(8 + 7 * rnd.NextDouble(), 2.4 + 1.6 * rnd.NextDouble());
            var turn = new RotateTransform(Math.Atan2(dx, -dy) * 180 / Math.PI);
            var at = new TranslateTransform();
            sh.RenderTransform = new TransformGroup { Children = { turn, at } };
            sh.Opacity = 0;
            shards.Children.Add(sh);
            _shards.Add((sh, at, turn, dx, dy, (rnd.NextDouble() - .5) * 240));
        }
        _openFx.Children.Add(_flash); _openFx.Children.Add(_ripFront); _openFx.Children.Add(_ripFront2); _openFx.Children.Add(shards);
        _fight.Children.Add(_openFx);
    }

    /// <summary>
    /// A fight started: the Abyss tears open. The meter's glass rips open down from the wound (a clip running down it, a jagged
    /// line of light at its front), a flash bursts out of the wound's widest point, shards of light fly out of it, the deep
    /// stars drift in behind; the crown lands on the leader in a burst; a pulse runs the seams.
    /// </summary>
    protected override void FightStarted(Encounter e, bool held)
    {
        OpenFx();
        foreach (var s in _shards) { Canvas.SetLeft(s.El, Math.Max(20, FightWidth - 8) * WoundPeak); Canvas.SetTop(s.El, WoundY); }
        // the glass and the deep stars tear open sideways from the wound's widest point (a clip on the art alone: no word is ever
        // cut, and the rows stay solid), a flash over the slit, shards flying out of it
        _fightArt.Clip = _ripClip;
        Motion.Play(_fight, 950, t =>
        {
            double w = _fight.ActualWidth > 0 ? _fight.ActualWidth : FightWidth - 8, h = _fight.ActualHeight > 0 ? _fight.ActualHeight : 200;
            double u = Seg(t, .02, .3), open = u * u * (3 - 2 * u), px = w * WoundPeak, half = 1 + (Math.Max(px, w - px) + 20) * open;
            _ripClip.Rect = new Rect(px - half, -12, 2 * half, h + 40);
            _deepHost.Opacity = open;
            if (open < 1)
            {
                double a = Math.Clamp((px - half) / w, 0, 1), b = Math.Clamp((px + half) / w, 0, 1);
                _revealA.Offset = a; _revealB.Offset = Math.Min(1, a + .015); _revealC.Offset = Math.Max(0, b - .015); _revealD.Offset = b;
                _fightBody.OpacityMask ??= _reveal;
            }
            else _fightBody.OpacityMask = null;
            _fightBody.Opacity = open < .02 ? 0 : 1;   // nothing before the tear opens (the slit's mask is not yet a slit)
            _ripAt.X = Math.Max(-12, px - half - 12); _ripAt2.X = Math.Min(w - 12, px + half - 12);
            _ripFront!.Opacity = _ripFront2!.Opacity = t < .02 ? Seg(t, 0, .02) : open < 1 ? 1 : 1 - Seg(t, .3, .4);
            _flashScale.CenterX = px; _flashScale.CenterY = 6;
            _flashScale.ScaleX = Math.Max(.05, Math.Min(1, 2 * half / w)); _flashScale.ScaleY = .7 + .3 * open;
            _flash!.Opacity = t < .05 ? Seg(t, 0, .05) : 1 - Seg(t, .14, .4);
            _fightFlare.Opacity = t < .3 ? open : 1 - Seg(t, .3, .6);
            double fly = Seg(t, .03, .8), k = Out(fly);
            foreach (var s in _shards)
            {
                s.At.X = s.Dx * k; s.At.Y = s.Dy * k + 26 * fly * fly;
                s.Turn.Angle = Math.Atan2(s.Dx, -s.Dy) * 180 / Math.PI + s.Spin * k * .3;
                s.El.Opacity = t < .05 ? Seg(t, 0, .05) : 1 - Seg(t, .45, .8);
            }
            _deepAt.Y = 16 * (1 - Out(Seg(t, 0, .9))); _deepAt.X = -8 * (1 - Out(Seg(t, 0, .9)));
        }, () =>
        {
            _fightArt.Clip = null; _fightBody.OpacityMask = null; _fightBody.Opacity = 1; _deepHost.Opacity = 1; _ripFront!.Opacity = _ripFront2!.Opacity = 0; _flash!.Opacity = 0; _fightFlare.Opacity = 0; _deepAt.X = _deepAt.Y = 0;
            foreach (var s in _shards) s.El.Opacity = 0;
        });
        LeaderRow()?.Land();
        Seams(null);
    }

    /// <summary>
    /// A fight ended: a plate of the glass saying how ("Killed 12:47", "Wiped") settles over the head for a moment, its tear lit
    /// in the outcome's colour, as a pulse runs the seams in that colour; then it fades. It sits on the boss and the clock
    /// (which say the same) and never over the rows.
    /// </summary>
    protected override void FightEnded(Encounter e, bool held)
    {
        if (e.Outcome is not ("kill" or "wipe" or "cleared")) return;
        bool wipe = e.Outcome == "wipe";
        var c = wipe ? CoralC : MintC2;
        var plate = new Grid { VerticalAlignment = VerticalAlignment.Top };
        var art = new TacSketch((dc, s) =>
        {
            if (s.Width < 6 || s.Height < 6) return;
            var r = new Rect(.5, .5, s.Width - 1, s.Height - 1);
            dc.DrawGeometry(Mod.Br(VoidC, .96), null, Mod.Pill(r, r.Height / 2));
            RiftPanel(dc, s, s.Height / 2, 1, tint: c, flare: .6, shadow: false);
            TornEnds(dc, s, c);
            dc.DrawGeometry(Mod.Grad(false, (Mod.A(c, 0), 0), (Mod.A(c, .18), .5), (Mod.A(c, 0), 1)), null, Mod.Pill(r, r.Height / 2));
            dc.DrawGeometry(null, Mod.P(Mod.Br(c, .6), 1.2), Mod.Pill(r, r.Height / 2));
            dc.DrawGeometry(Mod.Grad(false, (Mod.A(VioletC, 0), 0), (Mod.A(Color.FromRgb(0xC7, 0x7D, 0xFF), .95), .5), (Mod.A(VioletC, 0), 1)), null, TearAcross(s.Height / 2, s.Width - s.Height / 2, s.Height - 2.5, 3.2, 8));
        });
        plate.Children.Add(art);
        var lit = new RiftPulse(99);
        lit.Tint(c);
        plate.Children.Add(lit);
        var words = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        words.Children.Add(Tt(OutcomeText(e), 16, Frozen(Mod.Mix(c, Colors.White, .3)), FontWeights.Bold));
        var ck = Tt("  " + Clock(e.Duration(e.End ?? DateTime.UtcNow)), 15, White, FontWeights.SemiBold);
        words.Children.Add(ck);
        plate.Children.Add(words);
        var sc = new ScaleTransform(1, 1);
        plate.RenderTransformOrigin = new Point(.5, .5); plate.RenderTransform = sc;
        _fightFx.Children.Clear(); _fightFx.Children.Add(plate);
        // over the head as laid out now (the timers can sit above it in a dungeon), a few px proud of it each way
        bool Seat()
        {
            double y = _fightBody.Margin.Top, h = 22;
            bool laid = _head is { } hd && hd.ActualHeight > 0 && hd.IsDescendantOf(_fight);
            if (laid) { y = _head!.TranslatePoint(new Point(0, 0), _fight).Y; h = _head.ActualHeight; }
            plate.Margin = new Thickness(Pad - 4, Math.Max(2, y - 4), Pad - 4, 0);
            plate.Height = h + 8;
            return laid;
        }
        bool seated = Seat();
        Seams(wipe ? CoralC : LilacC);
        // on the look's stage: a kill bursts out of the wound (a blinding flare along the tear, a crown of rift light, a
        // shock ring, shards of the glass flung out and falling) and the glass punches; a wipe is the rift sealing: its
        // edges go dark, the glass shakes, a coral ring and the shards are pulled back into the wound and gone
        var st = AtreiaFx.Stage(_fx, _root);
        var parts = new List<Action<double>>();
        double fw = _fight.ActualWidth > 0 ? _fight.ActualWidth : FightWidth - 8;
        var wp = new Point(fw * WoundPeak, WoundY + 2);
        if (AtreiaFx.Box(_fight, _fx) is { } b)
        {
            var hit = new Point(b.X + wp.X, b.Y + wp.Y);
            var bits = new AtreiaFx.Bits(st);
            if (!wipe)
            {
                parts.Add(AtreiaFx.Bloom(st, hit, 170, 90, AtreiaFx.Glow(CyanC, 1, Colors.White), 0, .05, .34, s0: .3));
                parts.Add(AtreiaFx.Streak(st, new Point(b.X + b.Width / 2, hit.Y), b.Width * 1.05, 8, CyanC, 0, .07, .42));
                parts.Add(AtreiaFx.Rays(st, hit, 16, 8, b.Width * .5, 4.5, i => i % 3 == 0 ? LightC : i % 3 == 1 ? VioletC : Color.FromRgb(0xFF, 0x2E, 0x9A), .02, .08, .5, turn: -16, max: .6));
                bits.Burst(12, AtreiaFx.Shard, i => AtreiaFx.Br(i % 3 == 0 ? LightC : i % 3 == 1 ? CyanC : LilacC), hit, b.Width * .32, 8, .04, .55, fall: 40, dir: 90, spread: 300, r0: 8, spin: 300, seed: 31, squash: .6);
            }
            else
            {
                parts.Add(AtreiaFx.Veil(st, new Rect(b.X + 1, b.Y + 1, b.Width - 2, b.Height - 2), Color.FromRgb(0x06, 0x02, 0x10), .8, .02, .16, .66));
                // a dark band along the top, the tear's light drawn in to its middle over it in 0.25 s, ease-in, and gone
                parts.Add(AtreiaFx.Veil(st, new Rect(b.X + 6, b.Y, b.Width - 12, 18), Color.FromRgb(0x05, 0x02, 0x0C), 1, .02, .06, .34));
                parts.Add(AtreiaFx.Seal(st, new Point(b.X + b.Width / 2, hit.Y + 1), b.Width - 16, 10, CoralC, .03, .2));
                parts.Add(AtreiaFx.Bloom(st, new Point(b.X + b.Width / 2, hit.Y), 90, 40, AtreiaFx.Glow(CoralC, .9), .24, .3, .5, s0: 1));
                var rnd = new Random(37);
                for (int k = 0; k < 11; k++)
                {
                    double a = 2 * Math.PI * k / 11 + .3 * rnd.NextDouble(), d = 50 + 60 * rnd.NextDouble();
                    var u = new Vector(Math.Cos(a), Math.Abs(Math.Sin(a)) * .7);
                    var from = hit + u * d;
                    bits.Add(AtreiaFx.Shard, AtreiaFx.Br(k % 3 == 0 ? CoralC : LilacC, .85), from, -u * d, rot0: a * 180 / Math.PI + 180, spin: 120 * (rnd.NextDouble() - .5),
                        s0: 7, s1: 1.5, delay: .02 + .06 * rnd.NextDouble(), life: .45);
                }
            }
            parts.Add(bits.At);
        }
        _fightPunch.CenterX = wp.X; _fightPunch.CenterY = wp.Y;
        // a wipe glitches the head sideways 2 px for three frames as the tear seals
        var jit = new TranslateTransform();
        if (wipe && _head != null) _head.RenderTransform = jit;
        AtreiaFx.Play(_fx, st, 2400, t =>
        {
            double k = Math.Min(1, t * 2400 / 1500);
            AtreiaFx.All(parts, k);
            if (wipe) { _settle.X = AtreiaFx.Shake(k, .02, .46, 5, 3.5); jit.X = k is > .03 and < .09 ? ((int)(k * 50) % 2 == 0 ? 2 : -2) : 0; }
            else _fightPunch.ScaleX = _fightPunch.ScaleY = AtreiaFx.Punch(k, 0, .5, .012, .026);
            if (!seated) seated = Seat();   // once the head is laid out (a fight just drawn), then never again
            double i = Out(Seg(t, 0, .15)), o = Seg(t, .8, .9);
            plate.Opacity = i * (1 - o);
            // the head under the plate steps back while it shows (their words never sit over each other half seen)
            if (_head is { } hd0) hd0.Opacity = t < .8 ? 1 - Seg(t, 0, .06) : Seg(t, .88, 1);
            sc.ScaleX = sc.ScaleY = .96 + .04 * i;
            lit.At(Seg(t, .05, .45) is var u && u > 0 && u < 1 ? u : -1);
        }, () => { _fightFx.Children.Clear(); _settle.X = 0; _fightPunch.ScaleX = _fightPunch.ScaleY = 1; if (_head != null) _head.Opacity = 1; jit.X = 0; });
    }
}
