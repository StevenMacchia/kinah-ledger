using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using KinahLedger.Parser;
using KinahLedger.Session;
using static KinahLedger.Ui.HudKit;

namespace KinahLedger.Ui;

/// <summary>
/// Telestrator (the Esports theme; 2026-11-20): an analyst desk breaking down the replay. Everything sits on the analysis
/// board, charcoal glass with a tactics grid ruled into it and a replay frame's corner brackets (HudTelestrator.Ink.cs);
/// the figures are crisp white Inter, the bars flat lanes in each class's colour with a bright playhead at their end. Over
/// it the caster's pen, bright marker strokes with a hand's wobble that never cross a figure: the leader's rank circled in
/// yellow, a pink underline under your name, a scribbled plus before a healer's figure, an arrow drawn up beside a row that
/// climbed. The Buddy is the same board: the ring of your best a hand-drawn yellow loop, the hour big, a scribbled arrow
/// from it to the best drop in the strip, the session under it, the timers as small board tags.
/// The moments are the pen at work: the leader's circle drawn on when the lead changes (and as a fight starts); a
/// highlighter swipe along the bars on the first hit, a kill and a new leader; a pickup's swipe across the Buddy with its
/// worth pinned beside it; a rare drop circled in its grade's colour; your best passed closes the loop; an opened entry
/// circled in mint; a fight's end written over its head by hand ("Killed", underlined).
/// </summary>
sealed partial class HudTelestrator : ModernLook
{
    static readonly Color MintC2 = Color.FromRgb(0x5D, 0xF2, 0xA6), CoralC = Color.FromRgb(0xFF, 0x86, 0x86), HpC = Color.FromRgb(0xFF, 0x5C, 0x70),
        GreyC = Color.FromRgb(0x8A, 0x90, 0xA0), WarnC2 = Color.FromRgb(0xFF, 0xB0, 0x86), GoldC2 = Color.FromRgb(0xFF, 0xCF, 0x6A), InkDarkC = Color.FromRgb(0x14, 0x12, 0x0A);
    static readonly SolidColorBrush White = Frozen(Colors.White), Soft = Frozen(Mod.A(Colors.White, .84)), Quiet = Frozen(Mod.A(Colors.White, .6)),
        Mint2 = Frozen(Color.FromRgb(0x8C, 0xFF, 0xC6)), Coral = Frozen(CoralC);
    const double Radius = 5, Pad = 10;

    // the room round the look: the board's drop inside the window; 98 at the right for the pickup's note beside the Buddy
    readonly Grid _root = new() { Margin = new Thickness(8, 6, 98, 12), HorizontalAlignment = HorizontalAlignment.Left };
    readonly StackPanel _body = new() { HorizontalAlignment = HorizontalAlignment.Left };
    double _opacity = 0.9;

    // the Buddy: its board, the ring, the hour, the session and the timer tags
    readonly Grid _line = new() { HorizontalAlignment = HorizontalAlignment.Left };
    readonly TacSketch _lineBoard, _lineInk, _lineSwipe;
    readonly ScaleTransform _lineSwipeScale = new(0, 1);
    readonly StackPanel _lineCol = new();
    readonly StackPanel _lineBody = new() { Orientation = Orientation.Horizontal, Margin = new Thickness(7, 6, 14, 6) };
    readonly Canvas _lineAnchor = Tac.Anchor(HorizontalAlignment.Right, VerticalAlignment.Center);
    readonly StackPanel _timers = new() { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
    readonly Border _timersInLine = new() { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 0, 0) };
    /// <summary>The timer tags over the line, when the pickup strip takes their room on it.</summary>
    readonly Border _timersUp = new() { HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(4, 0, 0, 6), Visibility = Visibility.Collapsed };
    /// <summary>The pickup strip on the board: 24 px tiles beside the hour, a faint chalk rule, the best drop's star in marker yellow.</summary>
    static readonly PickStrip.Look Strip = new(24, 4, true, Frozen(Mod.A(Colors.White, .26)), Frozen(PenC));
    bool _dim;
    TacSketch? _ringArt;
    double _share = -1, _complete, _arrowDrawn = 1;
    bool _beat;
    Color _sheenC = PenC;
    FrameworkElement? _hour, _best;
    Grid? _firstChip;
    TacSketch? _firstChipInk;
    double _chipLoop = -1;

    // the rare drop under the Buddy, and the notice line
    readonly Grid _rare = new() { HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 5, 0, 0), Visibility = Visibility.Collapsed };
    readonly Grid _note = new() { HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 5, 0, 0), Visibility = Visibility.Collapsed };
    readonly StackPanel _noteBody = new() { Orientation = Orientation.Horizontal, Margin = new Thickness(10, 4, 11, 5) };

    // the fight: the board, its head, HP, rows and foot
    readonly Grid _fight = new() { HorizontalAlignment = HorizontalAlignment.Left, Visibility = Visibility.Collapsed, Margin = new Thickness(0, 0, 0, 8) };
    readonly TacSketch _fightBoard;
    readonly StackPanel _fightBody = new() { Margin = new Thickness(Pad, 8, Pad, 8) };
    readonly Border _timersInFight = new() { Margin = new Thickness(0, 0, 0, 7), HorizontalAlignment = HorizontalAlignment.Left };
    readonly Grid _fightFx = new() { IsHitTestVisible = false, VerticalAlignment = VerticalAlignment.Top };
    readonly Border _swipeHost = new() { IsHitTestVisible = false };
    readonly TranslateTransform _fightShift = new();
    FrameworkElement? _head;

    public HudTelestrator() : base(bare: true)
    {
        // the look's own rows (Ui/Themes/ThemeRows.cs): lanes on the board, the pen over them
        UseRows(TeleRow.Pitch, m => new TeleRow(this), 15, GapLine);
        _lineBoard = new TacSketch(DrawLineBoard);
        _lineInk = new TacSketch(DrawLineInk);
        _lineSwipe = new TacSketch(DrawLineSwipe) { RenderTransform = _lineSwipeScale, Visibility = Visibility.Collapsed };
        _lineCol.Children.Add(_runHead); _lineCol.Children.Add(_lineBody);
        _line.Children.Add(_lineBoard); _line.Children.Add(_lineSwipe); _line.Children.Add(_lineCol); _line.Children.Add(_lineInk); _line.Children.Add(_lineAnchor);
        _lineBody.SizeChanged += (_, _) => _lineInk.InvalidateVisual();
        _fightBoard = new TacSketch((dc, s) => Board(dc, s, Radius, _opacity));
        _fight.Children.Add(_fightBoard); _fight.Children.Add(_fightBody); _fight.Children.Add(_fightFx); _fight.Children.Add(_swipeHost);
        _fight.RenderTransform = _fightShift;
        _note.Children.Add(new TacSketch((dc, s) => Board(dc, s, 4, _opacity, shadow: false, grid: false, brackets: 0)));
        _note.Children.Add(_noteBody);
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
        _lineBoard.InvalidateVisual(); _fightBoard.InvalidateVisual();
        foreach (var c in _note.Children.OfType<TacSketch>()) c.InvalidateVisual();
        foreach (var c in _rare.Children.OfType<TacSketch>()) c.InvalidateVisual();
    }

    protected override string LineSig(Figures f) => base.LineSig(f) + "|" + (Rare(Values)?.Id ?? "") + "|" + (Picks(Values).FirstOrDefault()?.Id ?? "") + "|" + Dimmed(Values) + "|" + PickStrip.Sig(Values, Strip) + "|" + RunBuddy.Sig(Values);

    static bool Dimmed(IReadOnlyDictionary<string, string> v) => (v.TryGetValue("show", out var s) && s == "dim") || (v.TryGetValue("paused", out var p) && p.Length > 0);

    // ================================================================ the parts

    /// <summary>
    /// A small label: on a dark board chip with a hairline rim (AVG), or filled in a marker's colour with dark ink (★ DPS on
    /// highlighter yellow, the heal mark in mint); <paramref name="light"/>: white ink on it (YOU on pink).
    /// </summary>
    static Border Chip(string s, Color? fill = null, bool light = false, double size = 8.5)
    {
        var t = Tt(s, size, fill == null || light ? White : Frozen(InkDarkC), FontWeights.Bold);
        return new Border
        {
            Child = t, CornerRadius = new CornerRadius(2.5), Padding = new Thickness(4, 0, 4, 1), VerticalAlignment = VerticalAlignment.Center,
            Background = fill is { } c ? Frozen(c) : Frozen(Mod.A(BoardDeepC, .8)),
            BorderThickness = new Thickness(fill == null ? 1 : 0), BorderBrush = Frozen(Mod.A(Colors.White, .3)),
        };
    }

    /// <summary>A chalk line across the board: short dashes, faint.</summary>
    static FrameworkElement Chalk(Thickness m) => new TacSketch((dc, s) => dc.DrawLine(ChalkPen, new Point(0, .5), new Point(s.Width, .5))) { Height = 1, Margin = m };

    static readonly Pen ChalkPen = MakeChalk(), RulerPen = Mod.P(Mod.Br(Colors.White, .3), 1);
    static readonly SolidColorBrush HpTrackBr = Mod.Br(Colors.White, .14), HpBr = Frozen(HpC);

    static Pen MakeChalk()
    {
        var p = new Pen(Mod.Br(Colors.White, .2), 1) { DashStyle = new DashStyle(new double[] { 3, 3 }, 0) };
        p.Freeze();
        return p;
    }

    // ================================================================ the Buddy

    protected override void DrawLine(Figures f)
    {
        _lineBody.Children.Clear();
        _ringArt = null; _timerUp = false; _stripTimerW = -1; _hour = null; _best = null;
        if (_timersInLine.Child != null) _timersInLine.Child = null;
        _dim = Dimmed(Values);
        _line.Opacity = _dim ? .62 : 1;
        bool mark = f.Abyss && f.Counted && f.Icon.Length > 0;
        if (mark) { var m = AbyssMark(f.Icon, 36); m.Margin = new Thickness(2, 0, 9, 0); _lineBody.Children.Add(m); }
        // in a dungeon run: the run's clock, splits and verdict in place of the hour (HudTelestrator.Run.cs)
        var rs = RunNow();
        _runHead.Visibility = Visibility.Collapsed;
        if (rs != null) DrawRun(rs);
        else if (!HasHour(f))
        {
            var w = Tt(Waiting(f.Abyss), 12.5, Soft, FontWeights.SemiBold); w.Margin = new Thickness(mark ? 0 : 5, 6, 0, 6);
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
                var pc = Tt(f.Beat ? "★" : Pct(f.Share), 13, f.Beat ? Frozen(PenC) : White, FontWeights.Bold); pc.HorizontalAlignment = HorizontalAlignment.Center;
                ring.Children.Add(pc);
                _lineBody.Children.Add(ring);
            }
            else if (!mark) _lineBody.Children.Add(new Border { Width = 4 });
            // the hour big, its unit small; the session and the latest pickup under it
            var words = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 1, 0, 1) };
            var top = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Left };
            var rate = Tt(f.Rate, f.Warm ? 15 : 22, f.Warm ? Soft : White, f.Warm ? FontWeights.SemiBold : FontWeights.Bold);
            rate.Tag = "buddy-rate";
            top.Children.Add(rate);
            if (f.Unit.Length > 0) { var u = Tt(f.Unit, 11, Soft, FontWeights.SemiBold); u.VerticalAlignment = VerticalAlignment.Bottom; u.Margin = new Thickness(2, 0, 0, 4); top.Children.Add(u); }
            words.Children.Add(top);
            _hour = top;
            // farming, the pickups as the strip beside the hour where it fits (the best drop and the last three), the session
            // alone under it; with the timer tags at the line's end and no room left beside them, the tags go over the line
            var sub = Sub(f, true);
            FrameworkElement? strip = null, under = null;
            if (StripTime(f))
            {
                var bare = Sub(f, false);
                double baseW = 7 + 14 + _lineBody.Children.OfType<FrameworkElement>().Sum(Mod.W), timerW = LineTimerW();
                double wordsW = Math.Max(Mod.W(top), Mod.W(bare)), oldW = Math.Max(Mod.W(top), Mod.W(sub));
                foreach (var look in StripFall(Strip))
                {
                    if ((strip = StripIn(StripRoom(FightWidth - 10, baseW + timerW, wordsW, oldW, 14), look)) != null) break;
                    if (timerW > 0 && (strip = StripIn(StripRoom(FightWidth - 10, baseW, wordsW, oldW + timerW, 14), look)) != null) { _timerUp = true; break; }
                }
                if (strip != null) { sub = bare; strip.Margin = new Thickness(14, 0, 0, 0); _best = FindTag(strip, "pick-best"); }
                // no room beside the hour: under the session, never dropped (Steven, 2026-11-19)
                else if ((under = StripUnder(Strip, false)) != null) sub = bare;
                _stripTimerW = timerW;
            }
            if (sub.Inlines.Count > 0) words.Children.Add(sub);
            if (under != null) words.Children.Add(under);
            _lineBody.Children.Add(words);
            if (strip != null) _lineBody.Children.Add(strip);
        }
        _lineBody.Children.Add(_timersInLine);
        PlaceTimers();
        _lineBoard.InvalidateVisual(); _lineInk.InvalidateVisual();

        // the rare drop the window holds: its tile circled in its grade's colour, RARE DROP and its name
        _rare.Children.Clear();
        _rareInk = null; _rareTile = null;
        if (!KinahHidden && Rare(Values) is { } r)
        {
            var c = ColorOf(r.Color, r.Grade);
            _rareC = c; _rareLoop = 1;
            _rare.Children.Add(new TacSketch((dc, s) => Board(dc, s, 4, _opacity, shadow: true, grid: false, brackets: 0)));
            var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(8, 6, 14, 6) };
            var host = new Grid { Width = 26, Height = 26, Margin = new Thickness(0, 0, 10, 0), VerticalAlignment = VerticalAlignment.Center };
            var tile = Tile(r, 26);
            _rareTile = tile;
            host.Children.Add(tile);
            var ink = new TacSketch((dc, s) => Ink(dc, _rareLoop >= 1 ? Cached($"rare|{s.Width}", () => Poly(LoopPts(new Rect(-5, -5, s.Width + 10, s.Height + 10), 41))) : Poly(LoopPts(new Rect(-5, -5, s.Width + 10, s.Height + 10), 41), _rareLoop), Mod.Mix(c, Colors.White, .2), 1.9));
            _rareInk = ink;
            host.Children.Add(ink);
            row.Children.Add(host);
            var mk = Chip(L.T("RARE DROP"), PenC, size: 8); mk.Margin = new Thickness(0, 0, 7, 0);
            row.Children.Add(mk);
            var nm = Tt(r.Name, 13, Frozen(Mod.Mix(c, Colors.White, .45)), FontWeights.Bold);
            // the name underlined by the pen in its grade's colour (made once per width)
            var nmHost = new Grid { VerticalAlignment = VerticalAlignment.Center };
            nmHost.Children.Add(nm);
            nmHost.Children.Add(new TacSketch((dc, s) => Ink(dc, Cached($"rarename|{s.Width:0.0}|{s.Height:0.0}", () => Poly(LinePts(new Point(0, s.Height + 1), new Point(s.Width + 2, s.Height - .4), -.7, 43, .3))), Mod.Mix(c, Colors.White, .2), 1.6)));
            row.Children.Add(nmHost);
            if (r.Count.Length > 0) { var cn = Tt(" " + r.Count, 11, Soft, FontWeights.SemiBold); cn.Margin = new Thickness(2, 1, 0, 0); row.Children.Add(cn); }
            _rare.Children.Add(row);
            _rare.Visibility = Visibility.Visible;
        }
        else _rare.Visibility = Visibility.Collapsed;

        // the notice: its words in their tone on a small board, the warning sign before a pause
        _noteBody.Children.Clear();
        if (f.Note.Length > 0 && !KinahHidden)
        {
            bool warn = ReferenceEquals(f.NoteTone, Warn);
            if (warn) { var wi = WarnIcon(); wi.Width = wi.Height = 13; wi.Margin = new Thickness(0, 0, 5, 0); _noteBody.Children.Add(wi); }
            var tone = warn ? WarnC2 : ReferenceEquals(f.NoteTone, Gold) ? GoldC2 : ReferenceEquals(f.NoteTone, Mint) ? MintC2 : Color.FromRgb(0xF2, 0xF4, 0xF8);
            var t = Tt(f.Note, 11.5, Frozen(tone), FontWeights.SemiBold); t.TextTrimming = TextTrimming.CharacterEllipsis; t.MaxWidth = 380;
            _noteBody.Children.Add(t);
            _note.Visibility = Visibility.Visible;
        }
        else _note.Visibility = Visibility.Collapsed;
    }

    /// <summary>The first element under <paramref name="root"/> tagged <paramref name="tag"/> (the strip's best drop).</summary>
    static FrameworkElement? FindTag(DependencyObject root, string tag)
    {
        foreach (var o in LogicalTreeHelper.GetChildren(root))
        {
            if (o is FrameworkElement fe && Equals(fe.Tag, tag)) return fe;
            if (o is DependencyObject d && FindTag(d, tag) is { } hit) return hit;
        }
        return null;
    }

    /// <summary>The line under the hour: the session, the latest pickup and its worth (<paramref name="pick"/>); in the Abyss its stats.</summary>
    TextBlock Sub(Figures f, bool pick)
    {
        var sub = new TextBlock { FontFamily = Mod.Face, FontSize = 10.5, FontWeight = FontWeights.Medium, Foreground = Frozen(Mod.A(Colors.White, .8)), Margin = new Thickness(1, 1, 0, 0) };
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

    /// <summary>The timer tags' room at the line's end, 0 with none.</summary>
    double LineTimerW() => _timers.Children.Count > 0 && !KinahHidden ? 10 + Mod.W(_timers) : 0;

    /// <summary>The timers' room the line's strip was fitted to (-1: no strip time); tags that change it draw the line again.</summary>
    double _stripTimerW = -1;

    /// <summary>The timer tags sit over the line, to leave its room to the pickup strip.</summary>
    bool _timerUp;

    Color _rareC;
    double _rareLoop = 1;
    TacSketch? _rareInk;
    FrameworkElement? _rareTile;

    /// <summary>The Buddy's board (grey-tinted while paused), drawn once per size.</summary>
    void DrawLineBoard(DrawingContext dc, Size s) => Board(dc, s, Radius, _dim ? _opacity * .7 : _opacity);

    /// <summary>A highlighter's chisel-tip band through the middle of the Buddy, drawn once; a swipe scales it out and fades it.</summary>
    void DrawLineSwipe(DrawingContext dc, Size s)
    {
        if (s.Width < 16 || s.Height < 8) return;
        double y0 = Math.Round(s.Height * .3), y1 = Math.Round(s.Height * .7), x1 = s.Width - 4;
        var band = new StreamGeometry();
        using (var g = band.Open())
        {
            g.BeginFigure(new Point(4, y0), true, true);
            g.LineTo(new Point(x1, y0), true, false);
            g.LineTo(new Point(x1 - 7, y1), true, false);
            g.LineTo(new Point(4, y1), true, false);
        }
        band.Freeze();
        dc.DrawGeometry(Mod.Br(_sheenC, .2), null, band);
    }

    /// <summary>The Buddy's swipe at its point (0 to 1; outside: put away), in <paramref name="c"/>.</summary>
    void LineSwipe(double t, Color c)
    {
        if (t is <= 0 or >= 1) { _lineSwipe.Visibility = Visibility.Collapsed; return; }
        if (_lineSwipe.Visibility != Visibility.Visible || _sheenC != c) { _sheenC = c; _lineSwipe.Visibility = Visibility.Visible; _lineSwipe.InvalidateVisual(); }
        _lineSwipe.RenderTransformOrigin = new Point(0, 0);
        _lineSwipeScale.ScaleX = Math.Max(.001, Out(Seg(t, 0, .6)));
        _lineSwipe.Opacity = 1 - Seg(t, .45, 1);
    }

    /// <summary>The pen on the Buddy: a scribbled arrow from the hour over to the best drop in the strip.</summary>
    void DrawLineInk(DrawingContext dc, Size s)
    {
        if (_hour is not { } hour || _best is not { } best || _arrowDrawn <= 0) return;
        if (!hour.IsVisible || !best.IsVisible || hour.ActualWidth <= 0 || best.ActualWidth <= 0) return;
        var h = hour.TranslatePoint(new Point(hour.ActualWidth, 0), _line);
        var b = best.TranslatePoint(new Point(best.ActualWidth / 2, 0), _line);
        var a = new Point(h.X + 2, h.Y + 3);
        var end = new Point(b.X - 3, Math.Max(3.5, b.Y - 4));
        if (end.X - a.X < 10) return;
        double bow = -Math.Min(9, Math.Max(4, a.Y - 1));
        var pts = LinePts(a, end, bow, 23, .3);
        if (_arrowDrawn < 1) { Arrow(dc, pts, PenC, _arrowDrawn, 1, 1.8, 4); return; }
        string k = $"buddy|{a.X:0.0}|{a.Y:0.0}|{end.X:0.0}|{end.Y:0.0}";
        Ink(dc, Cached(k + "|line", () => Poly(pts)), PenC, 1.8);
        Ink(dc, Cached(k + "|head", () => Head(pts, 4)), PenC, 1.8);
    }

    /// <summary>The ring of your best: a hand-drawn loop, its track faint chalk, your best's share in yellow marker (closed with its tail once beaten).</summary>
    void DrawRing(DrawingContext dc, Size s)
    {
        var r = new Rect(1, 1, s.Width - 2, s.Height - 2);
        var c = new Point(s.Width / 2, s.Height / 2);
        dc.DrawEllipse(Mod.Br(BoardDeepC, .55), null, c, s.Width / 2 - 5, s.Height / 2 - 5);
        var pts = RingPts ??= LoopPts(r, 17, 1.12);
        dc.DrawGeometry(null, PenOf(Mod.A(Colors.White, .16), 2), Cached("ring|track", () => Poly(pts, 1 / 1.12)));
        double share = Math.Clamp(_beat ? 1 : _share, 0, 1);
        double shown = share + (1 - share) * Math.Clamp(_complete, 0, 1);
        if (shown < .004) return;
        double cut = shown >= .999 ? 1 : Math.Round(shown / 1.12, 3);
        Ink(dc, Cached($"ring|{cut}", () => Poly(pts, cut)), PenC, 2.4);
    }

    /// <summary>The ring's loop (the ring is always 42 px), made once.</summary>
    static List<Point>? RingPts;

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
        _firstChip = null; _firstChipInk = null;
        foreach (var i in due.Take(2))
        {
            var (text, tone) = TimerWords(i, now);
            bool first = _timers.Children.Count == 0;
            var c = tone == TimerBadge.Tone.On ? MintC2 : tone == TimerBadge.Tone.Soon ? GoldC2 : Color.FromRgb(0xE6, 0xE9, 0xF0);
            var sp = new StackPanel { Orientation = Orientation.Horizontal };
            sp.Children.Add(new Border { Width = 3, Height = first ? 11 : 9, Background = Frozen(c), CornerRadius = new CornerRadius(1), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) });
            var t = Tt(text, first ? 11 : 10, first ? White : Soft, first ? FontWeights.Bold : FontWeights.SemiBold);
            if (tone == TimerBadge.Tone.On) t.Foreground = Frozen(Mod.Mix(MintC2, Colors.White, .45));
            sp.Children.Add(t);
            var chip = new Border
            {
                Child = sp, CornerRadius = new CornerRadius(3), Padding = first ? new Thickness(6, 3, 8, 4) : new Thickness(6, 2, 7, 3), VerticalAlignment = VerticalAlignment.Center,
                Background = Frozen(Mod.A(BoardDeepC, .86)), BorderThickness = new Thickness(1), BorderBrush = Frozen(Mod.A(tone == TimerBadge.Tone.On ? MintC2 : Colors.White, tone == TimerBadge.Tone.On ? .55 : .2)),
            };
            var host = new Grid { Margin = new Thickness(first ? 0 : 6, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, Tag = first ? "timer-chip" : null };
            host.Children.Add(chip);
            if (first)
            {
                var ink = new TacSketch((dc, s) =>
                {
                    if (_chipLoop is <= 0 or >= 1) return;
                    Ink(dc, Poly(LoopPts(new Rect(-4, -4, s.Width + 8, s.Height + 8), 53), Out(Seg(_chipLoop, 0, .4))), PenMintC, 1.8);
                });
                host.Children.Add(ink);
                _firstChip = host; _firstChipInk = ink;
            }
            _timers.Children.Add(host);
        }
        // the line's pickup strip was fitted beside the timer tags: tags that come, go or change their width fit it again
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
        double w = FightWidth - 10;
        _fight.Width = w;
        double inner = w - 2 * Pad;
        _fightBody.Children.Add(_timersInFight);
        // the head: the boss, the outcome once it's over (circled in the pen), the clock
        var head = new Grid();
        head.ColumnDefinitions.Add(new ColumnDefinition()); head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var title = Tt(e.Title, 14, White, FontWeights.SemiBold); title.TextTrimming = TextTrimming.CharacterEllipsis;
        head.Children.Add(title);
        var right = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(8, 0, 0, 0) };
        if (e.Waiting) right.Children.Add(Chip(L.T("READY"), MintC2, size: 9.5));
        else
        {
            var outcome = OutcomeText(e);
            if (outcome.Length > 0)
            {
                bool wipe = e.Outcome == "wipe";
                var c = wipe ? CoralC : MintC2;
                var st = Tt(outcome, 10.5, Frozen(Mod.Mix(c, Colors.White, .25)), FontWeights.Bold);
                st.Margin = new Thickness(5, 0, 5, 1);
                var og = new Grid { Margin = new Thickness(0, 1, 10, 0), VerticalAlignment = VerticalAlignment.Center, Tag = "outcome" };
                og.Children.Add(st);
                og.Children.Add(new TacSketch((dc, s) => Ink(dc, Cached($"outcome|{s.Width:0.0}|{s.Height:0.0}", () => Poly(LoopPts(new Rect(-3, -4, s.Width + 6, s.Height + 8), 61))), c, 1.7)));
                right.Children.Add(og);
            }
            var clock = Tt(Clock(e, now), 18, White, FontWeights.Bold); clock.Tag = "fight-clock";
            right.Children.Add(clock);
        }
        Grid.SetColumn(right, 1); head.Children.Add(right);
        _head = head;
        _fightBody.Children.Add(head);
        if (e.Waiting)
        {
            var wt = Tt(e.ReadyText, 12, Soft, FontWeights.SemiBold); wt.Margin = new Thickness(0, 5, 0, 1); wt.TextTrimming = TextTrimming.CharacterEllipsis;
            _fightBody.Children.Add(wt);
            if (PartyBeforeHit(e)) { _fightBody.Children.Add(new Border { Height = 7 }); PutRows(_fightBody, e, now, inner); }
        }
        else
        {
            if (HpOf(e) is { } hp)
            {
                // the boss's HP: a flat red bar on a ruler (a tick each tenth, longer at the half), the figure and what's left at its right
                var line = new Grid { Margin = new Thickness(0, 5, 0, 2) };
                line.ColumnDefinitions.Add(new ColumnDefinition()); line.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                var cell = new Grid { Height = 12, VerticalAlignment = VerticalAlignment.Center };
                var track = new Grid { Height = 4, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 2, 0, 0) };
                track.Children.Add(new TacSketch((dc, s) =>
                {
                    double x1 = Math.Round(s.Width * Math.Clamp(hp, 0, 1));
                    dc.DrawRectangle(HpTrackBr, null, new Rect(0, 0, s.Width, s.Height));
                    if (x1 > .5) dc.DrawRectangle(HpBr, null, new Rect(0, 0, x1, s.Height));
                }));
                HpGhost(track, e, hp, now, 0);
                cell.Children.Add(track);
                cell.Children.Add(new TacSketch((dc, s) =>
                {
                    var pen = RulerPen;
                    for (int k = 0; k <= 10; k++)
                    {
                        double x = Math.Round(Math.Min(s.Width - .5, s.Width * k / 10)) + (k == 0 ? .5 : -.5);
                        dc.DrawLine(pen, new Point(x, 7), new Point(x, k == 5 ? 12 : k % 10 == 0 ? 11 : 9));
                    }
                }));
                line.Children.Add(cell);
                var hl = new TextBlock { FontFamily = Mod.Face, FontSize = 10.5, Foreground = Soft, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(9, 0, 0, 0) };
                System.Windows.Documents.Typography.SetNumeralAlignment(hl, FontNumeralAlignment.Tabular);
                hl.Inlines.Add(new System.Windows.Documents.Run($"{Math.Round(hp * 100):0}%") { FontWeight = FontWeights.Bold, Foreground = hp < .34 ? Frozen(Color.FromRgb(0xFF, 0x9A, 0xA4)) : White });
                var left = HpLeftText(e);
                if (left.Length > 0) hl.Inlines.Add(new System.Windows.Documents.Run("  ·  " + left) { FontWeight = FontWeights.Medium });
                hl.Tag = "hp-text";
                Grid.SetColumn(hl, 1); line.Children.Add(hl);
                _fightBody.Children.Add(line);
                _fightBody.Children.Add(new Border { Height = 6 });
            }
            else _fightBody.Children.Add(new Border { Height = 8 });
            PutRows(_fightBody, e, now, inner);
            if (e.HasDamage)
            {
                _fightBody.Children.Add(Chalk(new Thickness(0, 7, 0, 5)));
                _fightBody.Children.Add(Foot(e, now));
            }
        }
        _fight.Visibility = Visibility.Visible;
        PlaceTimers();
    }

    // ================================================================ the moments

    /// <summary>A pickup: a highlighter swipe across the Buddy, the arrow to the best drop drawn again, the worth pinned beside it on a note.</summary>
    protected override void Pickup(Pick p, bool held)
    {
        if (KinahHidden || _line.Visibility != Visibility.Visible) return;
        var worth = p.Worth.Length > 0 ? "+" + p.Worth.TrimStart('+') : p.Count;
        var words = new StackPanel { Margin = new Thickness(8, 3, 10, 4) };
        words.Children.Add(Tt(worth, 14, p.Worth.Length > 0 ? Frozen(Mod.Mix(MintC2, Colors.White, .25)) : White, FontWeights.Bold));
        var nm = Tt(p.Name.Length > 13 ? p.Name[..12].TrimEnd() + "…" : p.Name, 9.5, Soft, FontWeights.SemiBold); nm.Margin = new Thickness(0, -2, 0, 0);
        words.Children.Add(nm);
        double under = 0;
        var note = new Grid { MaxWidth = 88 };
        note.Children.Add(new TacSketch((dc, s) => Board(dc, s, 4, 1, shadow: false, grid: false, brackets: 0)));
        note.Children.Add(words);
        note.Children.Add(new TacSketch((dc, s) => { if (under > 0) Ink(dc, Poly(LinePts(new Point(8, s.Height - 3), new Point(s.Width - 9, s.Height - 4), -.6, 71, .3), under), PenC, 1.6); }));
        var shift = new TranslateTransform();
        note.RenderTransform = shift;
        note.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        Canvas.SetLeft(note, 6); Canvas.SetTop(note, -note.DesiredSize.Height / 2);
        _lineAnchor.Children.Clear(); _lineAnchor.Children.Add(note);
        var underInk = (TacSketch)note.Children[2];
        Motion.Play(note, 1900, t =>
        {
            LineSwipe(t < .55 ? Seg(t, 0, .55) : -1, PenC);
            if (_best != null && t <= .47) { _arrowDrawn = Math.Max(.001, Out(Seg(t, .15, .45))); _lineInk.InvalidateVisual(); }
            double k = Out(Seg(t, .1, .3));
            shift.X = -8 * (1 - k);
            under = Out(Seg(t, .25, .45)); if (t is >= .24 and <= .47) underInk.InvalidateVisual();
            note.Opacity = t < .82 ? k : 1 - Seg(t, .82, 1);
        }, () => { LineSwipe(-1, PenC); _arrowDrawn = 1; _lineInk.InvalidateVisual(); _lineAnchor.Children.Remove(note); });
    }

    /// <summary>A rare drop: the pen circles its tile in the grade's colour as the tile pops in.</summary>
    protected override void RareDrop(Pick p, bool held)
    {
        if (_rareInk is not { } ink) return;
        var tile = _rareTile;
        var sc = new ScaleTransform(1, 1);
        if (tile != null) { tile.RenderTransformOrigin = new Point(.5, .5); tile.RenderTransform = sc; }
        Motion.Play(ink, 1300, t =>
        {
            _rareLoop = Out(Seg(t, .15, .65));
            ink.InvalidateVisual();
            sc.ScaleX = sc.ScaleY = .65 + .35 * Back(Seg(t, 0, .35), 2.2);
        }, () => { _rareLoop = 1; ink.InvalidateVisual(); sc.ScaleX = sc.ScaleY = 1; });
    }

    /// <summary>Your best passed: the pen closes the loop round to its tail, and a mint highlighter swipe crosses the Buddy.</summary>
    protected override void BestPassed(bool held)
    {
        if (_ringArt is not { } art) return;
        double was = _share is > 0 and < 1 ? _share : .86;
        bool beat = _beat;
        bool closed = false;
        Motion.Play(art, 1800, t =>
        {
            if (t < .5 || !closed)
            {
                _beat = false; _share = was;
                _complete = Out(Seg(t, 0, .5));
                if (t >= .5) { _beat = beat; _complete = 1; closed = true; }
                art.InvalidateVisual();
            }
            LineSwipe(t is > .3 and < .9 ? Seg(t, .3, .9) : -1, PenMintC);
        }, () => { _complete = 0; _beat = beat; LineSwipe(-1, PenC); art.InvalidateVisual(); });
    }

    /// <summary>An entry opened: the pen circles the first timer's tag in mint, and the circle fades.</summary>
    protected override void EntryOpened(TimerBoard.Item i, bool held)
    {
        if (_firstChipInk is not { } ink) return;
        Motion.Play(ink, 1400, t =>
        {
            if (t <= .42 || _chipLoop < .4) { _chipLoop = Math.Clamp(t, .001, .999); ink.InvalidateVisual(); }
            ink.Opacity = 1 - Seg(t, .8, 1);
        }, () => { _chipLoop = -1; ink.Opacity = 1; ink.InvalidateVisual(); });
    }

    /// <summary>A fight started: the board settles in, the pen circles the leader, a highlighter swipe runs down the bars.</summary>
    protected override void FightStarted(Encounter e, bool held)
    {
        Motion.Play(_fight, 460, t =>
        {
            double k = Out(t);
            _fightShift.Y = 8 * (1 - k);
            _fightBody.Opacity = .25 + .75 * k;
        }, () => { _fightShift.Y = 0; _fightBody.Opacity = 1; });
        LeaderRow()?.Crown();
        Swipe();
    }

    /// <summary>
    /// A fight ended: a plate of the board over its head, the outcome written there by hand in the outcome's marker (mint, coral
    /// for a wipe) with a quick underline, the time crisp beside it; on a kill a highlighter swipe runs down the bars. Then it lifts.
    /// </summary>
    protected override void FightEnded(Encounter e, bool held)
    {
        if (e.Outcome is not ("kill" or "wipe" or "cleared")) return;
        bool wipe = e.Outcome == "wipe";
        var c = wipe ? CoralC : PenMintC;
        double under = 0;
        var plate = new Grid { VerticalAlignment = VerticalAlignment.Top };
        plate.Children.Add(new TacSketch((dc, s) => Board(dc, s, 4, 1, shadow: false, grid: true, brackets: 0)));
        var words = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 0, 0) };
        var word = Tt(OutcomeText(e), 21, Frozen(c), FontWeights.Bold, HudThemes.Hand);
        word.Margin = new Thickness(0, -3, 0, 0);
        words.Children.Add(word);
        var ck = Tt(Clock(e.Duration(e.End ?? DateTime.UtcNow)), 15, White, FontWeights.Bold); ck.Margin = new Thickness(10, 1, 0, 0);
        words.Children.Add(ck);
        plate.Children.Add(words);
        var ink = new TacSketch((dc, s) =>
        {
            if (under <= 0 || word.ActualWidth <= 0) return;
            var p = word.TranslatePoint(new Point(0, 0), plate);
            double y = Math.Min(s.Height - 3, p.Y + word.ActualHeight - 2);
            Ink(dc, Poly(LinePts(new Point(p.X - 1, y + .5), new Point(p.X + word.ActualWidth + 4, y - 1.2), -1, 83, .35), under), c, 1.9);
        });
        plate.Children.Add(ink);
        var shift = new TranslateTransform();
        plate.RenderTransform = shift;
        _fightFx.Children.Clear(); _fightFx.Children.Add(plate);
        // over the head as laid out now (the timers can sit above it in a dungeon), a few px proud of it each way
        void Seat()
        {
            double y = _fightBody.Margin.Top, h = 22;
            if (_head is { } hd && hd.ActualHeight > 0 && hd.IsDescendantOf(_fight)) { y = hd.TranslatePoint(new Point(0, 0), _fight).Y; h = hd.ActualHeight; }
            plate.Margin = new Thickness(Pad - 5, Math.Max(3, y - 4), Pad - 5, 0);
            plate.Height = h + 8;
        }
        Seat();
        plate.Loaded += (_, _) => Seat();
        if (!wipe) Swipe();
        var head = _head;
        Motion.Play(_fightFx, 2400, t =>
        {
            double i = Out(Seg(t, 0, .14)), o = Seg(t, .84, 1);
            plate.Opacity = i * (1 - o);
            shift.Y = -4 * (1 - i);
            under = Out(Seg(t, .14, .34)); if (t is >= .13 and <= .36) ink.InvalidateVisual();
            if (head != null) head.Opacity = t < .84 ? 1 - Seg(t, 0, .1) : Seg(t, .84, 1);
        }, () => { _fightFx.Children.Clear(); if (head != null) head.Opacity = 1; });
    }
}
