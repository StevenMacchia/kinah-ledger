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
    readonly Grid _root = new() { Margin = new Thickness(10, 8, 10, 16), HorizontalAlignment = HorizontalAlignment.Left };
    readonly StackPanel _body = new() { HorizontalAlignment = HorizontalAlignment.Left };
    double _opacity = 0.9;

    // the Buddy: its glass, the run's head over its words, the ring, the hour, the session and the timer chips
    readonly Grid _line = new() { HorizontalAlignment = HorizontalAlignment.Left };
    readonly TacSketch _lineArt;
    readonly StackPanel _lineStack = new();
    readonly StackPanel _lineBody = new() { Orientation = Orientation.Horizontal, Margin = new Thickness(8, 8, 14, 8) };
    readonly StackPanel _timers = new() { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
    readonly Border _timersInLine = new() { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 0) };
    /// <summary>The timer chips over the line, when the pickup strip takes their room on it.</summary>
    readonly Border _timersUp = new() { HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(2, 0, 0, 6), Visibility = Visibility.Collapsed };
    bool _dim;
    TacSketch? _ringArt;
    double _share = -1, _complete, _glint = -1, _linePulse = -1, _burst = -1;
    bool _beat, _ringEmpty;
    Color? _pulseC;
    Border? _firstChip;
    double _chipLight;

    // the rare drop under the Buddy (its own glass), and the notice line
    readonly Grid _rare = new() { HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 6, 0, 0), Visibility = Visibility.Collapsed };
    readonly Grid _note = new() { HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 6, 0, 0), Visibility = Visibility.Collapsed };

    // the fight: its glass, its head, HP, rows and foot
    readonly Grid _fight = new() { HorizontalAlignment = HorizontalAlignment.Left, Visibility = Visibility.Collapsed, Margin = new Thickness(0, 0, 0, 8) };
    readonly TacSketch _fightArt;
    readonly StackPanel _fightBody = new() { Margin = new Thickness(Pad, 10, Pad, 9) };
    readonly Border _timersInFight = new() { Margin = new Thickness(0, 0, 0, 8), HorizontalAlignment = HorizontalAlignment.Left };
    readonly Grid _fightFx = new() { IsHitTestVisible = false };
    readonly TranslateTransform _settle = new();
    FrameworkElement? _head;

    public HudRift() : base(bare: true)
    {
        // the look's own rows (Ui/Themes/ThemeRows.cs), a fit shared by the rows so the share column holds or goes for all
        var fit = new RiftFit();
        UseRows(RiftRow.Pitch, m => new RiftRow(this, fit), 15, GapLine);
        _lineArt = new TacSketch((dc, s) => RiftPanel(dc, s, Radius, _opacity, _linePulse, _pulseC, _linePulse is > 0 and < 1 ? .9 * Math.Sin(Math.PI * _linePulse) : 0));
        _lineStack.Children.Add(_runHead); _lineStack.Children.Add(_lineBody);
        _line.Children.Add(_lineArt); _line.Children.Add(_lineStack);
        _fightArt = new TacSketch((dc, s) => RiftPanel(dc, s, Radius, _opacity, _seam is > 0 and < .4 ? _seam / .4 : -1, _seamC, _seam is > 0 and < .4 ? .7 * Math.Sin(Math.PI * _seam / .4) : 0));
        _fight.Children.Add(_fightArt); _fight.Children.Add(_fightBody); _fight.Children.Add(_fightFx);
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

    // ================================================================ small parts

    /// <summary>The violet glow round a big figure in starlight (the hour, the clock): one effect on a few texts.</summary>
    static DropShadowEffect Glow(double blur = 10, double a = .8) => new() { Color = VioletC, ShadowDepth = 0, BlurRadius = blur, Opacity = a };

    /// <summary>The tight dark shade under the meter rows' words, violet-black (one effect for each row's words).</summary>
    static DropShadowEffect Shade() => new() { Color = Color.FromRgb(0x0A, 0x02, 0x18), Direction = 270, ShadowDepth = 1, BlurRadius = 3, Opacity = .95 };

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
        _lineBody.Margin = new Thickness(8, 8, 14, 8);
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
            var w = Tt(Waiting(f.Abyss), 13, Soft, FontWeights.SemiBold); w.Margin = new Thickness(mark ? 0 : 2, 0, 0, 0);
            _lineBody.Children.Add(w);
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
        _share = share; _beat = beat; _complete = 0; _ringEmpty = empty;
        var ring = new Grid { Width = 44, Height = 44, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 11, 0) };
        _ringArt = new TacSketch(DrawRing, 44, 44);
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
    double _rareOpen = -1;
    TacSketch? _rareArt;
    FrameworkElement? _rareTile;

    /// <summary>The rare drop the window holds, on its own glass under the Buddy: its tile, RARE DROP, its name in its grade's colour; the tear in that colour.</summary>
    void DrawRare()
    {
        _rare.Children.Clear();
        if (!KinahHidden && Rare(Values) is { } r)
        {
            var c = ColorOf(r.Color, r.Grade);
            _rareC = c;
            var art = new TacSketch((dc, s) =>
            {
                double open = _rareOpen;
                RiftPanel(dc, s, s.Height / 2, _opacity, open is > 0 and < 1 ? Seg(open, .1, .7) : -1, c, open is > 0 and < 1 ? 1.2 * Math.Sin(Math.PI * Seg(open, 0, .8)) : .25, shadow: false, tear: 1);
                // the grade's light breathing in the glass behind the tile
                double k = open is > 0 and < 1 ? Math.Sin(Math.PI * Seg(open, 0, .9)) : 0;
                dc.PushOpacity(.35 + .65 * k);
                dc.DrawRectangle(Radial(new Point(.5, .5), .5, .5, (Mod.A(c, .6), 0), (Mod.A(c, 0), 1)), null, new Rect(0, -4, 50 + 16 * k, s.Height + 8));
                dc.Pop();
                if (k > 0)
                {
                    // the glass torn open: a fissure of the grade's light down beside the tile, and a rift of it running out
                    // along the pill's top and foot from there, then closing (the words between them kept clear)
                    double run = Out(Seg(open, .05, .45)), x1 = 40 + (s.Width - 58) * run, th = 3.4 * k;
                    var lc = Mod.Mix(c, Colors.White, .35);
                    dc.PushOpacity(Math.Min(1, 1.3 * k));
                    Fissure(dc, 41, 2, s.Height - 4, c, 1 + .9 * k, 2);
                    foreach (var y in new[] { 2.2, s.Height - 2.2 })
                    {
                        dc.DrawGeometry(Mod.Grad(false, (Mod.A(lc, .95), 0), (Mod.A(lc, .8), .7), (Mod.A(c, 0), 1)), null, TearAcross(36, x1 + 6, y, th, y > 3 ? 7 : 4));
                        dc.DrawGeometry(Mod.Grad(false, (Mod.A(Colors.White, 1), 0), (Mod.A(Colors.White, .7), .6), (Mod.A(Colors.White, 0), 1)), null, TearAcross(36, x1, y, th * .4, 5));
                    }
                    dc.Pop();
                }
            });
            _rareArt = art;
            _rare.Children.Add(art);
            var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(9, 6, 16, 8) };
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
        else { _rare.Visibility = Visibility.Collapsed; _rareArt = null; _rareTile = null; }
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
        if (_ringEmpty) { dc.DrawEllipse(null, Mod.P(Mod.Br(LilacC, .4), 1), center, r + 1.7, r + 1.7); return; }
        double share = Math.Clamp(_beat ? 1 : _share, 0, 1);
        double shown = share + (1 - share) * Math.Clamp(_complete, 0, 1);
        bool full = shown >= .999;
        var arc = Mod.Grad(true, (LilacC, 0), (VioletC, .55), (CyanC, 1));
        if (_burst is > 0 and < 1)
        {
            // the portal opening: a ring of rift light spreading out from it and fading
            double rr = r + 5 * Out(_burst);
            dc.DrawEllipse(null, Mod.P(Mod.Br(LightC, .9 * (1 - _burst)), 2.2 * (1 - _burst) + .6), center, rr, rr);
        }
        if (full)
        {
            dc.DrawEllipse(null, Mod.P(Mod.Br(CyanC, .3 + (_burst is > 0 and < 1 ? .35 * (1 - _burst) : 0)), 7.5), center, r, r);
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
        double g = _glint is > 0 and < 1 ? 1 : 0;
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
            if (PartyBeforeHit(e)) { _fightBody.Children.Add(new Border { Height = 8 }); PutRows(_fightBody, e, now, inner); }
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
                _fightBody.Children.Add(new Border { Height = 8 });
            }
            else _fightBody.Children.Add(new Border { Height = 8 });
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

    /// <summary>The seams' pulse, 0 to 1 (-1: none): along the panel's tear first, then down each bar's edge in rank order.</summary>
    internal double _seam = -1;
    internal Color? _seamC;
    long _leadId;
    string _leadFight = "";

    /// <summary>A pulse of rift light runs the seams: the meter's tear left to right, then each bar's edge in rank order (a light sweeping up the bar into its fissure, which flares).</summary>
    void Seams(Color? c)
    {
        Motion.Play(_fightArt, 1500, t =>
        {
            _seam = t; _seamC = c;
            _fightArt.InvalidateVisual();
            RedrawRows();
        }, () => { _seam = -1; _fightArt.InvalidateVisual(); RedrawRows(); });
    }

    void RedrawRows()
    {
        if (Meter is not { } m) return;
        foreach (var x in m.Members) if (m.RowOf(x.Id) is RiftRow r) r.Redraw();
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

    /// <summary>A pickup: a pulse of rift light runs along the Buddy's tear.</summary>
    protected override void Pickup(Pick p, bool held)
    {
        if (KinahHidden || _line.Visibility != Visibility.Visible) return;
        _pulseC = null;
        Motion.Play(_lineArt, 1100, t => { _linePulse = t; _lineArt.InvalidateVisual(); }, () => { _linePulse = -1; _lineArt.InvalidateVisual(); });
    }

    /// <summary>A rare drop: its glass tears open in its grade's colour (a fissure flaring behind the tile, the tear lit in the colour), the tile popping in.</summary>
    protected override void RareDrop(Pick p, bool held)
    {
        if (_rareArt is not { } art) return;
        var tile = _rareTile;
        var sc = new ScaleTransform(1, 1);
        if (tile != null) { tile.RenderTransformOrigin = new Point(.5, .5); tile.RenderTransform = sc; }
        Motion.Play(art, 1600, t =>
        {
            _rareOpen = t; art.InvalidateVisual();
            sc.ScaleX = sc.ScaleY = .6 + .4 * Back(Seg(t, .05, .4), 2.2);
        }, () => { _rareOpen = -1; art.InvalidateVisual(); sc.ScaleX = sc.ScaleY = 1; });
    }

    /// <summary>Your best passed: the portal's arc runs on round to full, its point of light bright at the head, then the whole ring lights; a pulse crosses the tear.</summary>
    protected override void BestPassed(bool held)
    {
        if (_ringArt is not { } art) return;
        double was = _share is > 0 and < 1 ? _share : .84;
        bool beat = _beat;
        _pulseC = MintC2;
        Motion.Play(art, 1800, t =>
        {
            _beat = false; _share = was;
            _complete = Out(Seg(t, 0, .55));
            _glint = t < .55 ? Seg(t, 0, .55) : -1;
            if (t >= .55) { _beat = beat; _complete = 1; }
            _burst = t is > .52 and < .95 ? Seg(t, .52, .95) : -1;
            art.InvalidateVisual();
            _linePulse = t is > .45 and < .95 ? Seg(t, .45, .95) : -1; _lineArt.InvalidateVisual();
        }, () => { _complete = 0; _beat = beat; _glint = -1; _burst = -1; _linePulse = -1; _pulseC = null; art.InvalidateVisual(); _lineArt.InvalidateVisual(); });
    }

    /// <summary>An entry opened: the first timer's chip lights in mint round its edge and settles.</summary>
    protected override void EntryOpened(TimerBoard.Item i, bool held)
    {
        if (_firstChip is not { } chip) return;
        var glow = new DropShadowEffect { Color = MintC2, ShadowDepth = 0, BlurRadius = 10, Opacity = 0 };
        chip.Effect = glow;
        var edge = chip.BorderBrush;
        Motion.Play(chip, 1300, t =>
        {
            _chipLight = t < .25 ? Seg(t, 0, .25) : 1 - Seg(t, .25, 1);
            glow.Opacity = .9 * _chipLight;
            chip.BorderBrush = Frozen(Mod.A(Mod.Mix(MintC2, Colors.White, .3 * _chipLight), .6 + .4 * _chipLight));
        }, () => { chip.Effect = null; chip.BorderBrush = edge; _chipLight = 0; });
    }

    /// <summary>A fight started: its glass settles in (rising a few pixels as it fades up), the crown lands on the leader, a pulse runs the seams.</summary>
    protected override void FightStarted(Encounter e, bool held)
    {
        Motion.Play(_fight, 460, t => { double k = Out(t); _settle.Y = 6 * (1 - k); _fight.Opacity = .2 + .8 * k; }, () => { _settle.Y = 0; _fight.Opacity = 1; });
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
        double lit = 0;
        var art = new TacSketch((dc, s) =>
        {
            if (s.Width < 6 || s.Height < 6) return;
            var r = new Rect(.5, .5, s.Width - 1, s.Height - 1);
            dc.DrawGeometry(Mod.Br(VoidC, .96), null, Mod.Pill(r, r.Height / 2));
            RiftPanel(dc, s, s.Height / 2, 1, lit is > 0 and < 1 ? lit : -1, c, .6, shadow: false);
            dc.DrawGeometry(Mod.Grad(false, (Mod.A(c, 0), 0), (Mod.A(c, .18), .5), (Mod.A(c, 0), 1)), null, Mod.Pill(r, r.Height / 2));
            dc.DrawGeometry(null, Mod.P(Mod.Br(c, .6), 1.2), Mod.Pill(r, r.Height / 2));
        });
        plate.Children.Add(art);
        var words = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        words.Children.Add(Tt(OutcomeText(e), 16, Frozen(Mod.Mix(c, Colors.White, .3)), FontWeights.Bold));
        var ck = Tt("  " + Clock(e.Duration(e.End ?? DateTime.UtcNow)), 15, White, FontWeights.SemiBold);
        words.Children.Add(ck);
        plate.Children.Add(words);
        var sc = new ScaleTransform(1, 1);
        plate.RenderTransformOrigin = new Point(.5, .5); plate.RenderTransform = sc;
        _fightFx.Children.Clear(); _fightFx.Children.Add(plate);
        // over the head as laid out now (the timers can sit above it in a dungeon), a few px proud of it each way
        void Seat()
        {
            double y = _fightBody.Margin.Top, h = 22;
            if (_head is { } hd && hd.ActualHeight > 0 && hd.IsDescendantOf(_fight)) { y = hd.TranslatePoint(new Point(0, 0), _fight).Y; h = hd.ActualHeight; }
            plate.Margin = new Thickness(Pad - 4, Math.Max(2, y - 4), Pad - 4, 0);
            plate.Height = h + 8;
        }
        Seat();
        Seams(wipe ? CoralC : null);
        Motion.Play(_fightFx, 2400, t =>
        {
            Seat();
            double i = Out(Seg(t, 0, .15)), o = Seg(t, .8, 1);
            plate.Opacity = i * (1 - o);
            sc.ScaleX = sc.ScaleY = .96 + .04 * i;
            lit = Seg(t, .05, .45); art.InvalidateVisual();
        }, () => _fightFx.Children.Clear());
    }
}
