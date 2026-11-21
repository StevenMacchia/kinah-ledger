using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using KinahLedger.Parser;
using KinahLedger.Session;
using static KinahLedger.Ui.HudKit;

namespace KinahLedger.Ui;

/// <summary>
/// Asmodian (the Atreia theme, the second looks pack, 2026-11-20): the shadow faction's war-gear. Every surface is a plate of
/// black iron (HudAsmodian.Iron.cs) with notched corners, brushed grain, a crimson edge glowing just past it, a smoulder
/// rising from its foot and a thorn off each deep cut. Titles in a heavy condensed face, figures crisp white.
/// The Buddy is one plate: the run's ring as a crimson iron ring with studs, the hour big in white over a red underglow, the
/// pickups as iron-framed tiles, the session under it, the timers as iron chips. The meter is one plate: the boss in the
/// heavy face, the clock, a forged HP bar with spikes at the thirds, then each member as an iron slab with their bar forged
/// in their class's colour, tempered dark, ending in a blade edge that burns crimson to ember; a blood-red horned crown on
/// the top row. The moments: a pickup's ember light crosses the plate, a rare drop's plate flares in its grade, your best
/// passed burns the ring round in ember gold with sparks; a fight rises in, embers lift off the bars and a heat glint runs
/// along them (on the start, a kill and a new leader, when the crown slams down on the new top row), and its end stamps an
/// iron banner over the head.
/// </summary>
sealed partial class HudAsmodian : ModernLook
{
    static readonly Color MintC2 = Color.FromRgb(0x5D, 0xF2, 0xA6), MarkMintC = Color.FromRgb(0x7D, 0xFF, 0xC0), CoralC = Color.FromRgb(0xFF, 0x8C, 0x8C),
        GoldC2 = Color.FromRgb(0xFF, 0xC4, 0x6A), WarnC2 = Color.FromRgb(0xFF, 0xB0, 0x86), HpLowC = Color.FromRgb(0xFF, 0x9A, 0x8A);
    static readonly SolidColorBrush White = Frozen(Colors.White), Soft = Frozen(Color.FromArgb(0xDC, 0xF6, 0xEE, 0xEE)), Quiet = Frozen(Color.FromArgb(0xC4, 0xF0, 0xDD, 0xDE)),
        Mint2 = Frozen(Color.FromRgb(0x8C, 0xFF, 0xC6)), MarkMint = Frozen(MarkMintC), Coral = Frozen(CoralC), Gold2 = Frozen(GoldC2);

    // the room round the look: the plates' glow lies inside their own room; 98 at the right for the pickup's chip beside the Buddy
    readonly Grid _root = new() { Margin = new Thickness(6, 6, 98, 8), HorizontalAlignment = HorizontalAlignment.Left };
    readonly StackPanel _body = new() { HorizontalAlignment = HorizontalAlignment.Left };
    double _opacity = 0.9;

    // the Buddy: its plate, the ring, the hour, the session and the timer chips
    readonly Grid _line = new() { HorizontalAlignment = HorizontalAlignment.Left };
    readonly TacSketch _linePlate, _lineFx;
    readonly StackPanel _lineBody = new() { Orientation = Orientation.Horizontal, Margin = new Thickness(G + 8, G + 6, G + 13, G + 6) };
    readonly Canvas _lineAnchor = Tac.Anchor(HorizontalAlignment.Right, VerticalAlignment.Center);
    readonly StackPanel _timers = new() { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
    readonly Border _timersInLine = new() { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 0) };
    /// <summary>The timer chips over the line, when the pickup strip takes their room on it.</summary>
    readonly Border _timersUp = new() { HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(G, 0, 0, 3), Visibility = Visibility.Collapsed };
    /// <summary>The pickup strip: 24 px tiles in iron frames beside the hour, a crimson rule, an ember-gold star.</summary>
    static readonly PickStrip.Look Strip = new(24, 5, true, Frozen(Color.FromArgb(0x90, 0xC2, 0x1F, 0x2E)), Frozen(Color.FromRgb(0xFF, 0xC4, 0x6A)));
    bool _dim;
    Grid? _ringHost;
    TacSketch? _ringArt;
    double _share = -1, _complete, _sheen = -1, _lineHeat, _bestSparks = -1;
    bool _beat;
    Grid? _firstChip;

    // the rare drop under the Buddy, and the notice line
    readonly Grid _rare = new() { HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 2, 0, 0), Visibility = Visibility.Collapsed };
    readonly Grid _note = new() { HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(6, 1, 0, 0), Visibility = Visibility.Collapsed };
    readonly StackPanel _noteBody = new() { Orientation = Orientation.Horizontal, Margin = new Thickness(G + 8, G + 3, G + 10, G + 4) };

    // the fight: one iron plate, its head, HP, rows and foot
    readonly Grid _fight = new() { HorizontalAlignment = HorizontalAlignment.Left, Visibility = Visibility.Collapsed, Margin = new Thickness(0, 0, 0, 4) };
    readonly TacSketch _fightPlate;
    const double FightPad = 10;
    readonly StackPanel _fightBody = new() { Margin = new Thickness(G + FightPad, G + 7, G + FightPad, G + 8) };
    readonly Border _timersInFight = new() { Margin = new Thickness(0, 0, 0, 6), HorizontalAlignment = HorizontalAlignment.Left };
    readonly Grid _fightFx = new() { IsHitTestVisible = false, ClipToBounds = true };
    readonly TacSketch _emberArt;
    readonly Grid _plateHost = new() { IsHitTestVisible = false, VerticalAlignment = VerticalAlignment.Top };
    readonly TranslateTransform _fightShift = new();
    double _fightHeat, _emberT = -1;
    DateTime _lastLead;
    FrameworkElement? _head;

    public HudAsmodian() : base(bare: true)
    {
        // the look's own rows (Ui/Themes/ThemeRows.cs): each an iron slab with its forged bar
        UseRows(IronRow.Pitch, m => new IronRow(this), 14, GapLine);
        _linePlate = new TacSketch((dc, s) => Iron(dc, s, _opacity, _lineHeat, _dim ? AshC : null, sheen: _sheen));
        _lineFx = new TacSketch(DrawLineFx) { ClipToBounds = true };
        _line.Children.Add(_linePlate); _line.Children.Add(_lineBody); _line.Children.Add(_lineFx); _line.Children.Add(_lineAnchor);
        _fightPlate = new TacSketch((dc, s) => Iron(dc, s, _opacity, _fightHeat, big: 11));
        _emberArt = new TacSketch(DrawEmbers);
        _fightFx.Children.Add(_emberArt); _fightFx.Children.Add(_plateHost);
        _fight.Children.Add(_fightPlate); _fight.Children.Add(_fightBody); _fight.Children.Add(_fightFx);
        _fight.RenderTransform = _fightShift;
        _note.Children.Add(new TacSketch((dc, s) => Iron(dc, s, _opacity, big: 6, small: 2, thorns: false)));
        _note.Children.Add(_noteBody);
        _runHeadHost = RunHeadPlate(_runHead); _runHeadHost.Visibility = Visibility.Collapsed;
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
        _linePlate.InvalidateVisual(); _fightPlate.InvalidateVisual();
        foreach (var s in _note.Children.OfType<TacSketch>()) s.InvalidateVisual();
        foreach (var s in _rare.Children.OfType<TacSketch>()) s.InvalidateVisual();
    }

    protected override string LineSig(Figures f) => base.LineSig(f) + "|" + (Rare(Values)?.Id ?? "") + "|" + (Picks(Values).FirstOrDefault()?.Id ?? "") + "|" + Dimmed(Values) + "|" + PickStrip.Sig(Values, Strip) + "|" + RunBuddy.Sig(Values);

    static bool Dimmed(IReadOnlyDictionary<string, string> v) => (v.TryGetValue("show", out var s) && s == "dim") || (v.TryGetValue("paused", out var p) && p.Length > 0);

    /// <summary>The tight shade under words straight on the game (the run's head over its tab is the only such text).</summary>
    static DropShadowEffect Shade() => new() { Color = Colors.Black, Direction = 270, ShadowDepth = 1, BlurRadius = 3, Opacity = .95 };

    /// <summary>The red underglow under the hour (one effect, on one text).</summary>
    static DropShadowEffect Underglow(double opacity = 1) => new() { Color = Color.FromRgb(0xFF, 0x1E, 0x1E), Direction = 270, ShadowDepth = 2.5, BlurRadius = 14, Opacity = opacity };

    /// <summary>The pickup strip's tiles each set in an iron frame: a notched dark rim and crimson corner brackets.</summary>
    static FrameworkElement? Forge(FrameworkElement? strip)
    {
        if (strip is not Panel row) return strip;
        foreach (var cell in row.Children.OfType<Grid>())
        {
            if (cell.Tag is not ("pick-tile" or "pick-best")) continue;
            cell.Children.Insert(1, new TacSketch((dc, s) =>
            {
                var r = new Rect(-1.5, -1.5, s.Width + 3, s.Height + 3);
                dc.DrawGeometry(null, Mod.P(Mod.Br(IronC, .9), 1.4), Plate(r, 4, 0));
                var pen = Mod.P(Mod.Br(HotC, .95), 1.3);
                double k = 5;
                dc.DrawLine(pen, new Point(r.Right - k, r.Top), new Point(r.Right, r.Top)); dc.DrawLine(pen, new Point(r.Right, r.Top), new Point(r.Right, r.Top + k));
                dc.DrawLine(pen, new Point(r.Left, r.Bottom - k), new Point(r.Left, r.Bottom)); dc.DrawLine(pen, new Point(r.Left, r.Bottom), new Point(r.Left + k, r.Bottom));
            }));
        }
        return strip;
    }

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
        // in a dungeon run: the run's clock, splits and verdict in place of the hour (HudAsmodian.Run.cs)
        var rs = RunNow();
        _runHeadHost.Visibility = Visibility.Collapsed;
        if (rs != null) DrawRun(rs);
        else if (!HasHour(f))
        {
            var w = Head(Waiting(f.Abyss), 14, Soft); w.Margin = new Thickness(2, 5, 0, 5);
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
                var pc = Big(f.Beat ? "★" : Pct(f.Share), f.Beat ? 15 : 14, f.Beat ? Frozen(PaleC) : White, FontWeights.Bold); pc.HorizontalAlignment = HorizontalAlignment.Center;
                ring.Children.Add(pc);
                _ringHost = ring;
                _lineBody.Children.Add(ring);
            }
            // the hour big over its red underglow, its unit small; the session and the latest pickup under it
            var words = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 0, 1) };
            var top = new StackPanel { Orientation = Orientation.Horizontal };
            var rate = Big(f.Rate, f.Warm ? 16 : 25, f.Warm ? Soft : White, f.Warm ? FontWeights.SemiBold : FontWeights.Bold);
            rate.Tag = "buddy-rate";
            if (f.Warm) rate.FontStretch = FontStretches.Condensed;
            top.Children.Add(new Border { Child = rate, Effect = f.Warm ? null : Underglow() });
            if (f.Unit.Length > 0) { var u = Head(f.Unit, 12, Quiet); u.VerticalAlignment = VerticalAlignment.Bottom; u.Margin = new Thickness(2, 0, 0, f.Warm ? 1 : 4); top.Children.Add(u); }
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
                if (strip != null) { sub = bare; strip.Margin = new Thickness(14, 0, 0, 0); Forge(strip); }
                // no room beside the hour: under the session, never dropped (Steven, 2026-11-19)
                else if ((under = StripUnder(Strip, false)) != null) { sub = bare; under.Margin = new Thickness(2, 7, 0, 2); Forge(under); }
                _stripTimerW = timerW;
            }
            if (sub.Inlines.Count > 0) words.Children.Add(sub);
            if (under != null) words.Children.Add(under);
            _lineBody.Children.Add(words);
            if (strip != null) _lineBody.Children.Add(strip);
        }
        _lineBody.Children.Add(_timersInLine);
        PlaceTimers();
        _linePlate.InvalidateVisual();

        // the rare drop the window holds: its own plate edged in its grade's colour, its tile, RARE DROP and its name
        _rare.Children.Clear();
        if (!KinahHidden && Rare(Values) is { } r)
        {
            var c = ColorOf(r.Color, r.Grade);
            _rareC = c; _rareBloom = 1; _rareFlash = 0;
            var plate = new TacSketch((dc, s) =>
            {
                double bw = s.Width * Math.Clamp(_rareBloom, 0, 1);
                if (bw < 16) return;
                dc.PushClip(new RectangleGeometry(new Rect(0, 0, bw, s.Height)));
                Iron(dc, new Size(s.Width, s.Height), _opacity, .35 + .65 * _rareFlash, c, big: 8);
                dc.DrawRectangle(Mod.Grad(false, (Mod.A(c, .3 + .4 * _rareFlash), 0), (Mod.A(c, 0), .6)), null, new Rect(G + 1, G + 1, Math.Max(0, s.Width - 2 * G - 2), s.Height - 2 * G - 2));
                dc.Pop();
                if (_rareSparks is > 0 and < 1 && _rareTile is { } tl && tl.IsDescendantOf(_rare))
                {
                    var p = tl.TranslatePoint(new Point(tl.ActualWidth / 2, 4), _rare);
                    for (int i = 0; i < 7; i++) Spark(dc, new Point(p.X - 10 + 3.4 * i, p.Y), 40 + i, _rareSparks, 20);
                }
            });
            _rareArt = plate;
            _rare.Children.Add(plate);
            var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(G + 7, G + 5, G + 14, G + 5) };
            var tile = Tile(r, 26); tile.Margin = new Thickness(0, 0, 9, 0);
            _rareTile = tile;
            row.Children.Add(tile);
            var mk = Chip(L.T("RARE DROP"), Frozen(Mod.Mix(c, Colors.White, .55)), c, size: 8); mk.Margin = new Thickness(0, 0, 8, 0);
            row.Children.Add(mk);
            var nm = Head(r.Name, 15, Frozen(Mod.Mix(c, Colors.White, .4)), FontWeights.Bold);
            row.Children.Add(nm);
            if (r.Count.Length > 0) { var cn = Tt(" " + r.Count, 11, Soft, FontWeights.SemiBold); cn.Margin = new Thickness(3, 1, 0, 0); row.Children.Add(cn); }
            _rare.Children.Add(row);
            _rare.Visibility = Visibility.Visible;
        }
        else { _rare.Visibility = Visibility.Collapsed; _rareArt = null; _rareTile = null; }

        // the notice: its words in their tone on a slim iron plate under the Buddy, the warning sign before a pause
        _noteBody.Children.Clear();
        if (f.Note.Length > 0 && !KinahHidden)
        {
            bool warn = ReferenceEquals(f.NoteTone, Warn);
            if (warn) { var wi = WarnIcon(); wi.Width = wi.Height = 13; wi.Margin = new Thickness(0, 0, 6, 0); _noteBody.Children.Add(wi); }
            var tone = warn ? WarnC2 : ReferenceEquals(f.NoteTone, Gold) ? GoldC2 : ReferenceEquals(f.NoteTone, Mint) ? MintC2 : BoneC;
            var t = Tt(f.Note, 11.5, Frozen(tone), FontWeights.SemiBold); t.TextTrimming = TextTrimming.CharacterEllipsis; t.MaxWidth = 380;
            _noteBody.Children.Add(t);
            _note.Visibility = Visibility.Visible;
        }
        else _note.Visibility = Visibility.Collapsed;
    }

    /// <summary>The line under the hour: the session, the latest pickup and its worth (<paramref name="pick"/>); in the Abyss its stats.</summary>
    TextBlock Sub(Figures f, bool pick)
    {
        var sub = new TextBlock { FontFamily = Mod.Face, FontSize = 10.5, FontWeight = FontWeights.Medium, Foreground = Soft, Margin = new Thickness(1, 0, 0, 0) };
        System.Windows.Documents.Typography.SetNumeralAlignment(sub, FontNumeralAlignment.Tabular);
        void Dot() { if (sub.Inlines.Count > 0) sub.Inlines.Add(new System.Windows.Documents.Run("  ◆  ") { Foreground = Frozen(Mod.A(CrimC, .9)), FontSize = 6.5, BaselineAlignment = BaselineAlignment.Center }); }
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

    /// <summary>The timer chips' room at the line's end, 0 with none.</summary>
    double LineTimerW() => _timers.Children.Count > 0 && !KinahHidden ? 12 + Mod.W(_timers) : 0;

    /// <summary>The timers' room the line's strip was fitted to (-1: no strip time); chips that change it draw the line again.</summary>
    double _stripTimerW = -1;

    /// <summary>The timer chips sit over the line, to leave its room to the pickup strip.</summary>
    bool _timerUp;

    Color _rareC;
    double _rareBloom = 1, _rareFlash, _rareSparks = -1;
    TacSketch? _rareArt;
    FrameworkElement? _rareTile;

    /// <summary>
    /// The run's ring as a ring of crimson iron: a dark well, a heavy iron track with four studs, your best's share as a
    /// crimson arc burning hotter at its head (ember gold once beaten), flat-cut ends.
    /// </summary>
    void DrawRing(DrawingContext dc, Size s)
    {
        double c = s.Width / 2, r = c - 4;
        var center = new Point(c, c);
        dc.DrawEllipse(Mod.Br(IronC, .9), Mod.P(Mod.Br(CrimC, .5), 1), center, c - .5, c - .5);
        dc.DrawEllipse(null, Mod.P(Mod.Br(RimC), 5), center, r, r);
        double share = Math.Clamp(_beat ? 1 : _share, 0, 1);
        double shown = share + (1 - share) * Math.Clamp(_complete, 0, 1);
        bool won = _beat || _complete >= 1;
        Brush tone = won ? Mod.Grad(true, (PaleC, 0), (EmberC, 1)) : Mod.Grad(false, (CrimC, 0), (HotC, 1));
        if (shown >= .999) dc.DrawEllipse(null, Mod.P(tone, 4.2), center, r, r);
        else if (shown > .004)
        {
            double a = 2 * Math.PI * shown;
            var fig = new PathFigure { StartPoint = new Point(c, c - r) };
            fig.Segments.Add(new ArcSegment(new Point(c + r * Math.Sin(a), c - r * Math.Cos(a)), new Size(r, r), 0, a > Math.PI, SweepDirection.Clockwise, true));
            var pg = new PathGeometry(); pg.Figures.Add(fig); pg.Freeze();
            dc.DrawGeometry(null, Mod.P(tone, 4.2), pg);
            // the arc's head: a hot point
            var head = new Point(c + r * Math.Sin(a), c - r * Math.Cos(a));
            dc.DrawEllipse(Mod.Br(PaleC, .9), null, head, 1.6, 1.6);
        }
        // the studs: four dark notches across the ring
        var stud = Mod.P(Mod.Br(IronC), 1.4);
        for (int k = 0; k < 4; k++)
        {
            double ang = Math.PI / 2 * k + Math.PI / 4;
            dc.DrawLine(stud, new Point(c + (r - 3) * Math.Sin(ang), c - (r - 3) * Math.Cos(ang)), new Point(c + (r + 3) * Math.Sin(ang), c - (r + 3) * Math.Cos(ang)));
        }
        if (_complete is > 0 and < 1)
            dc.DrawEllipse(null, Mod.P(Mod.Br(PaleC, .5 * (1 - _complete)), 2), center, r + 2 + 2 * _complete, r + 2 + 2 * _complete);
    }

    /// <summary>The Buddy's sparks: your best passed, a short burst rising off the ring.</summary>
    void DrawLineFx(DrawingContext dc, Size s)
    {
        if (_bestSparks is not (> 0 and < 1) || _ringHost is not { } ring || !ring.IsDescendantOf(_line)) return;
        var p = ring.TranslatePoint(new Point(21, 8), _line);
        for (int i = 0; i < 8; i++)
        {
            double ang = Math.PI * (.15 + .7 * i / 7.0);
            Spark(dc, new Point(p.X - 15 * Math.Cos(ang), p.Y + 6 - 12 * Math.Sin(ang)), 70 + i, _bestSparks, 18);
        }
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
            var c = tone == TimerBadge.Tone.On ? MintC2 : tone == TimerBadge.Tone.Soon ? GoldC2 : Mod.Mix(CrimC, BoneC, .25);
            var sp = new StackPanel { Orientation = Orientation.Horizontal, Margin = first ? new Thickness(8, 3, 10, 4) : new Thickness(7, 2, 9, 3) };
            // a small diamond in the tone before the words
            double d = first ? 6.5 : 5.5;
            sp.Children.Add(new System.Windows.Shapes.Rectangle { Width = d, Height = d, Fill = Frozen(c), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 7, 0), RenderTransformOrigin = new Point(.5, .5), RenderTransform = new RotateTransform(45) });
            var t = Tt(text, first ? 11.5 : 10, first ? White : Soft, first ? FontWeights.Bold : FontWeights.SemiBold);
            if (tone == TimerBadge.Tone.On) t.Foreground = Frozen(Mod.Mix(MintC2, Colors.White, .45));
            else if (tone == TimerBadge.Tone.Soon) t.Foreground = Frozen(Mod.Mix(GoldC2, Colors.White, .4));
            sp.Children.Add(t);
            var edge = tone == TimerBadge.Tone.Quiet ? CrimC : c;
            var chip = new Grid { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(first ? 0 : 5, 0, 0, 0), Tag = first ? "timer-chip" : null };
            chip.Children.Add(new TacSketch((dc, s) =>
            {
                if (s.Width < 4 || s.Height < 4) return;
                var shape = Plate(new Rect(.5, .5, s.Width - 1, s.Height - 1), 4, 1);
                dc.DrawGeometry(Mod.Grad(true, (Mod.A(Iron2C, .93), 0), (Mod.A(IronC, .95), 1)), Mod.P(Mod.Br(edge, tone == TimerBadge.Tone.Quiet ? .75 : .85), 1), shape);
                dc.DrawRectangle(Mod.Br(edge, .12), null, new Rect(1, 1, s.Width - 2, s.Height - 2));
            }));
            chip.Children.Add(sp);
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
        if (e == null) { _fight.Visibility = Visibility.Collapsed; _plateHost.Children.Clear(); PlaceTimers(); return; }
        double w = FightWidth - 10;
        _fight.Width = w;
        double inner = w - 2 * (G + FightPad);
        _fightBody.Children.Add(_timersInFight);
        // the head: the boss in the heavy face, the outcome once it's over, the clock
        var head = new Grid();
        head.ColumnDefinitions.Add(new ColumnDefinition()); head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var title = Head(e.Title, 17, White); title.TextTrimming = TextTrimming.CharacterEllipsis;
        head.Children.Add(title);
        var right = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(8, 0, 0, 0) };
        if (e.Waiting) right.Children.Add(Chip(L.T("READY"), MarkMint, MintC2, size: 9.5, pad: new Thickness(7, 1, 7, 2)));
        else
        {
            var outcome = OutcomeText(e);
            if (outcome.Length > 0)
            {
                bool wipe = e.Outcome == "wipe";
                var oc = Chip(outcome, wipe ? Frozen(Mod.Mix(AshC, Colors.White, .35)) : Gold2, wipe ? AshC : GoldC2, size: 10, face: Bahn, pad: new Thickness(7, 1, 7, 2));
                oc.Margin = new Thickness(0, 1, 9, 0); oc.Tag = "outcome";
                right.Children.Add(oc);
            }
            var clock = Big(Clock(e, now), 19, White); clock.Tag = "fight-clock";
            right.Children.Add(clock);
        }
        Grid.SetColumn(right, 1); head.Children.Add(right);
        _head = head;
        _fightBody.Children.Add(head);
        if (e.Waiting)
        {
            var wt = Tt(e.ReadyText, 12.5, Soft, FontWeights.SemiBold); wt.Margin = new Thickness(0, 5, 0, 1); wt.TextTrimming = TextTrimming.CharacterEllipsis;
            _fightBody.Children.Add(wt);
            if (PartyBeforeHit(e)) { _fightBody.Children.Add(Rule(new Thickness(0, 4, 0, 4), false)); PutRows(_fightBody, e, now, inner); }
        }
        else
        {
            if (HpOf(e) is { } hp)
            {
                // the boss's HP: a forged bar with spikes at the thirds, "47%  ·  1.84M left" at its right
                var hpLine = new Grid { Margin = new Thickness(0, 5, 0, 0) };
                hpLine.ColumnDefinitions.Add(new ColumnDefinition()); hpLine.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                var track = new Grid { Height = 7, VerticalAlignment = VerticalAlignment.Center };
                track.Children.Add(new TacSketch((dc, s) =>
                {
                    var r = new Rect(0, 0, s.Width, s.Height);
                    var shape = Plate(r, 3, 0);
                    dc.DrawGeometry(Mod.Br(Color.FromRgb(0x22, 0x0E, 0x10)), Mod.P(Mod.Br(Colors.Black, .8), 1), shape);
                    double x1 = s.Width * Math.Clamp(hp, 0, 1);
                    dc.PushClip(shape);
                    if (x1 > .5)
                    {
                        dc.DrawRectangle(Mod.Grad(false, (Color.FromRgb(0x6E, 0x0B, 0x16), 0), (Color.FromRgb(0xE0, 0x28, 0x3A), 1)), null, new Rect(0, 0, x1, s.Height));
                        dc.DrawRectangle(Mod.Br(Colors.White, .22), null, new Rect(0, 0, x1, 1));
                        dc.DrawRectangle(Mod.Br(Colors.Black, .3), null, new Rect(0, s.Height - 2, x1, 2));
                        dc.DrawRectangle(Mod.Br(Color.FromRgb(0xFF, 0x9A, 0x70)), null, new Rect(Math.Max(0, x1 - 1.5), 0, 1.5, s.Height));
                    }
                    dc.Pop();
                    // the spikes at the thirds: dark wedges biting down from the top
                    for (int k = 1; k <= 2; k++)
                    {
                        double x = Math.Round(s.Width * k / 3);
                        var sp = new StreamGeometry();
                        using (var q = sp.Open()) { q.BeginFigure(new Point(x - 2.5, -1), true, true); q.LineTo(new Point(x + 2.5, -1), true, false); q.LineTo(new Point(x, s.Height - 1), true, false); }
                        sp.Freeze();
                        dc.DrawGeometry(Mod.Br(IronC), null, sp);
                    }
                }));
                HpGhost(track, e, hp, now, 0);
                hpLine.Children.Add(track);
                var hl = new TextBlock { FontFamily = Mod.Face, FontSize = 10.5, Foreground = Soft, Margin = new Thickness(10, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
                System.Windows.Documents.Typography.SetNumeralAlignment(hl, FontNumeralAlignment.Tabular);
                hl.Inlines.Add(new System.Windows.Documents.Run($"{Math.Round(hp * 100):0}%") { FontWeight = FontWeights.Bold, FontSize = 11.5, Foreground = hp < .34 ? Frozen(HpLowC) : White });
                var left = HpLeftText(e);
                if (left.Length > 0) hl.Inlines.Add(new System.Windows.Documents.Run("  ·  " + left) { FontWeight = FontWeights.Medium });
                hl.Tag = "hp-text";
                Grid.SetColumn(hl, 1); hpLine.Children.Add(hl);
                _fightBody.Children.Add(hpLine);
                _fightBody.Children.Add(new Border { Height = 7 });
            }
            else _fightBody.Children.Add(Rule(new Thickness(0, 3, 0, 4), false));
            PutRows(_fightBody, e, now, inner);
            if (e.HasDamage)
            {
                _fightBody.Children.Add(Rule(new Thickness(0, 3, 0, 3)));
                _fightBody.Children.Add(Foot(e, now));
            }
        }
        _fight.Visibility = Visibility.Visible;
        PlaceTimers();
    }

    // ================================================================ the embers

    /// <summary>A new leader took the top row: the embers and the heat glint (at most once in a while, however often the lead swaps).</summary>
    internal void Lead()
    {
        if (!Moves || (DateTime.UtcNow - _lastLead).TotalMilliseconds < 1500) return;
        _lastLead = DateTime.UtcNow;
        Embers();
    }

    /// <summary>
    /// The meter's burst: a few embers (nine at most) lift off the bars' burning ends and a heat glint runs along each bar,
    /// one row after another, the plate's smoulder flaring and cooling with them. One course, then gone.
    /// </summary>
    void Embers()
    {
        var rows = Meter?.Members.Select(m => Meter.RowOf(m.Id)).OfType<IronRow>().ToList() ?? new();
        Motion.Play(_emberArt, 1150, t =>
        {
            _emberT = t; _emberArt.InvalidateVisual();
            for (int i = 0; i < rows.Count; i++) rows[i].SetGlint(Seg(t, .05 * i, .5 + .05 * i));
            _fightHeat = t < .2 ? Seg(t, 0, .2) : 1 - Seg(t, .2, 1); _fightPlate.InvalidateVisual();
        }, () =>
        {
            _emberT = -1; _emberArt.InvalidateVisual(); _fightHeat = 0; _fightPlate.InvalidateVisual();
            foreach (var r in rows) r.SetGlint(-1);
        });
    }

    void DrawEmbers(DrawingContext dc, Size s)
    {
        if (_emberT is not (> 0 and < 1) || Meter is not { } m) return;
        int n = 0;
        for (int i = 0; i < m.Members.Count && n < 9; i++)
        {
            if (m.RowOf(m.Members[i].Id) is not IronRow row || !row.Root.IsDescendantOf(_fight) || row.Root.ActualWidth <= 0) continue;
            var p = row.Root.TranslatePoint(new Point(row.TipX, IronRow.H / 2), _emberArt);
            int count = i == 0 ? 3 : i < 3 ? 2 : 1;
            for (int k = 0; k < count && n < 9; k++, n++) Spark(dc, new Point(Math.Max(p.X - 6 - 11 * k, 8), p.Y + 3), n * 7 + i, _emberT, 22 + 4 * (count - k));
        }
    }

    // ================================================================ the moments

    /// <summary>A pickup: a band of ember light crosses the Buddy's plate and the worth comes in on an iron chip beside it, then goes.</summary>
    protected override void Pickup(Pick p, bool held)
    {
        if (KinahHidden || _line.Visibility != Visibility.Visible) return;
        var worth = p.Worth.Length > 0 ? "+" + p.Worth.TrimStart('+') : p.Count;
        var words = new StackPanel { Margin = new Thickness(9, 3, 11, 4) };
        words.Children.Add(Big(worth, 15, p.Worth.Length > 0 ? Frozen(Mod.Mix(MintC2, Colors.White, .25)) : White, FontWeights.Bold));
        var nm = Tt(p.Name.Length > 13 ? p.Name[..12].TrimEnd() + "…" : p.Name, 9.5, Soft, FontWeights.SemiBold); nm.Margin = new Thickness(0, -1, 0, 0);
        words.Children.Add(nm);
        var chip = new Grid { MaxWidth = 88 };
        chip.Children.Add(new TacSketch((dc, s) => Iron(dc, s, 1, .4, big: 6, small: 2, thorns: false, g: 2)));
        chip.Children.Add(words);
        var shift = new TranslateTransform();
        chip.RenderTransform = shift;
        chip.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        Canvas.SetLeft(chip, 4); Canvas.SetTop(chip, -chip.DesiredSize.Height / 2);
        _lineAnchor.Children.Clear(); _lineAnchor.Children.Add(chip);
        Motion.Play(chip, 1900, t =>
        {
            _sheen = t < .5 ? Seg(t, 0, .5) : -1; _lineHeat = t < .5 ? .5 * Math.Sin(Math.PI * Seg(t, 0, .5)) : 0; _linePlate.InvalidateVisual();
            double k = Out(Seg(t, .1, .3));
            shift.X = -10 * (1 - k);
            chip.Opacity = t < .82 ? k : 1 - Seg(t, .82, 1);
        }, () => { _sheen = -1; _lineHeat = 0; _linePlate.InvalidateVisual(); _lineAnchor.Children.Remove(chip); });
    }

    /// <summary>A rare drop: its plate forges out to the right, flaring in its grade's colour, the tile stamping in, sparks off it.</summary>
    protected override void RareDrop(Pick p, bool held)
    {
        if (_rareArt is not { } art) return;
        var tile = _rareTile;
        var sc = new ScaleTransform(1, 1);
        if (tile != null) { tile.RenderTransformOrigin = new Point(.5, .5); tile.RenderTransform = sc; }
        Motion.Play(art, 1500, t =>
        {
            _rareBloom = Out(Seg(t, 0, .4));
            _rareFlash = t < .2 ? Seg(t, 0, .2) : 1 - Seg(t, .2, .85);
            _rareSparks = t is > .12 and < .95 ? Seg(t, .12, .95) : -1;
            art.InvalidateVisual();
            sc.ScaleX = sc.ScaleY = t < .1 ? 1.5 : 1.5 - .5 * Back(Seg(t, .1, .4), 2.4);
        }, () => { _rareBloom = 1; _rareFlash = 0; _rareSparks = -1; art.InvalidateVisual(); sc.ScaleX = sc.ScaleY = 1; });
    }

    /// <summary>Your best passed: the ring burns on round to full in ember gold, sparks rise off it and the plate's smoulder flares.</summary>
    protected override void BestPassed(bool held)
    {
        if (_ringArt is not { } art) return;
        double was = _share is > 0 and < 1 ? _share : .86;
        bool beat = _beat;
        Motion.Play(art, 1800, t =>
        {
            _beat = false; _share = was;
            _complete = Out(Seg(t, 0, .45));
            if (t >= .45) { _beat = beat; _complete = 1; }
            art.InvalidateVisual();
            _bestSparks = t is > .35 and < 1 ? Seg(t, .35, 1) : -1; _lineFx.InvalidateVisual();
            _lineHeat = t < .45 ? Seg(t, 0, .45) : 1 - Seg(t, .45, 1); _linePlate.InvalidateVisual();
        }, () => { _complete = 0; _beat = beat; _bestSparks = -1; _lineHeat = 0; art.InvalidateVisual(); _lineFx.InvalidateVisual(); _linePlate.InvalidateVisual(); });
    }

    /// <summary>An entry opened: the first timer's chip flares mint round its edge and settles.</summary>
    protected override void EntryOpened(TimerBoard.Item i, bool held)
    {
        if (_firstChip is not { } chip) return;
        var glow = new DropShadowEffect { Color = MintC2, ShadowDepth = 0, BlurRadius = 10, Opacity = 0 };
        chip.Effect = glow;
        var sc = new ScaleTransform(1, 1);
        chip.RenderTransformOrigin = new Point(.5, .5); chip.RenderTransform = sc;
        Motion.Play(chip, 1200, t =>
        {
            double k = t < .2 ? Seg(t, 0, .2) : 1 - Seg(t, .2, 1);
            glow.Opacity = .9 * k;
            sc.ScaleX = sc.ScaleY = 1 + .05 * k;
        }, () => { chip.Effect = null; sc.ScaleX = sc.ScaleY = 1; });
    }

    /// <summary>A fight started: the plate rises in as it fades up, the crown slams onto the top row, embers lift off the bars.</summary>
    protected override void FightStarted(Encounter e, bool held)
    {
        Motion.Play(_fight, 480, t =>
        {
            double k = Out(t);
            _fightShift.Y = 6 * (1 - k);
            _fightBody.Opacity = k;
        }, () => { _fightShift.Y = 0; _fightBody.Opacity = 1; });
        if (Meter is { Members.Count: > 0 } m && m.RowOf(m.Members[0].Id) is IronRow top) top.Slam();
        Embers();
    }

    /// <summary>
    /// A fight ended: an iron banner stamps down over the fight's head with how it went and the clock (ember gold for a kill,
    /// ash for a wipe), its edge flaring; a kill sends the embers up the bars. It lifts away after a moment.
    /// </summary>
    protected override void FightEnded(Encounter e, bool held)
    {
        if (e.Outcome is not ("kill" or "wipe" or "cleared")) return;
        bool wipe = e.Outcome == "wipe";
        var c = wipe ? AshC : GoldC2;
        double flare = 0;
        var band = new Grid { VerticalAlignment = VerticalAlignment.Top };
        band.Children.Add(new TacSketch((dc, s) => Iron(dc, s, 1, wipe ? 0 : flare, wipe ? AshC : null, big: 9)));
        var words = new Grid { Margin = new Thickness(G + 10, G + 2, G + 10, G + 2), VerticalAlignment = VerticalAlignment.Center };
        words.ColumnDefinitions.Add(new ColumnDefinition()); words.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var ot = Head(OutcomeText(e), 21, Frozen(Mod.Mix(c, Colors.White, .3)), FontWeights.Bold);
        if (!wipe) ot.Effect = Underglow(.6);
        words.Children.Add(ot);
        var ck = Big(Clock(e.Duration(e.End ?? DateTime.UtcNow)), 19, White);
        Grid.SetColumn(ck, 1); words.Children.Add(ck);
        band.Children.Add(words);
        var sc = new ScaleTransform(1, 1);
        band.RenderTransformOrigin = new Point(.5, .5); band.RenderTransform = sc;
        _plateHost.Children.Clear(); _plateHost.Children.Add(band);
        void Seat()
        {
            double y = _fightBody.Margin.Top, h = 22;
            if (_head is { } hd && hd.ActualHeight > 0 && hd.IsDescendantOf(_fight)) { y = hd.TranslatePoint(new Point(0, 0), _fight).Y; h = hd.ActualHeight; }
            band.Margin = new Thickness(G + 2, Math.Max(0, y - G - 5), G + 2, 0);
            band.Height = h + 2 * G + 10;
        }
        Seat();
        var head = _head;
        Motion.Play(_plateHost, 2400, t =>
        {
            Seat();
            double i = Seg(t, 0, .14), o = Seg(t, .84, 1);
            band.Opacity = (i > 0 ? Math.Min(1, i * 2.5) : 0) * (1 - o);
            sc.ScaleX = sc.ScaleY = 1 + .14 * (1 - Out(i));
            flare = t < .14 ? 0 : 1 - Seg(t, .14, .6);
            ((TacSketch)band.Children[0]).InvalidateVisual();
            if (head != null) head.Opacity = t < .84 ? 1 - Seg(t, 0, .1) : Seg(t, .84, 1);
        }, () => { _plateHost.Children.Clear(); if (head != null) head.Opacity = 1; });
        if (!wipe) Embers();
    }
}
