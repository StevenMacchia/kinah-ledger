using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using KinahLedger.Parser;
using KinahLedger.Session;
using static KinahLedger.Ui.HudKit;

namespace KinahLedger.Ui;

/// <summary>
/// Versus (the Overdrive theme, 2026-11-20; Steven: "at least 1 theme should be WILD"): the meter IS a versus fighting game's
/// HUD (an original one: no game's art or names). The fight sits on a dark slab with a cut corner and a hot slash. Its head
/// is the round timer (the fight clock in a chamfered box) beside the boss's name on a slanted banner, and the boss's HP as
/// the opponent's gauge, mirrored, draining away from the timer. Each member is a huge slanted life gauge (HudVersus.Rows.cs):
/// rank in an angled numeral block, the class's art in a portrait chip, the gauge filled in their class colour (the leader's
/// hot red to yellow, a combo counter's hit spark behind their "1"), the DPS on a black score plate. The Buddy is the score:
/// the hour giant in Impact on a slanted plate, a segmented super meter glowing cyan for the share of your best.
/// The moments are events, each a slam over in about a second: FIGHT! at the start (a scale-in, a flash frame, streaks);
/// a new leader's spark slamming onto their row with a burst and a ring; K.O. on a kill (giant, angled, an impact ring and a
/// white flash); DEFEAT on a wipe; NEW RECORD as a super move cut-in when your best is passed; SPECIAL! on a rare drop; a
/// pickup's chip punched in from the side.
/// </summary>
sealed partial class HudVersus : ModernLook
{
    // the room round the look: 98 at the right for the pickup's chip beside the Buddy
    readonly Grid _root = new() { Margin = new Thickness(8, 8, 98, 10), HorizontalAlignment = HorizontalAlignment.Left };
    readonly StackPanel _body = new() { HorizontalAlignment = HorizontalAlignment.Left };
    double _opacity = 0.9;

    // the Buddy: the score plate, the hour, the session, the super meter and the timer chips
    readonly Grid _line = new() { HorizontalAlignment = HorizontalAlignment.Left };
    readonly TacSketch _linePlate;
    readonly StackPanel _lineStack = new();
    readonly StackPanel _lineBody = new() { Orientation = Orientation.Horizontal, Margin = new Thickness(17, 6, 18, 5) };
    readonly Grid _lineFx = new() { IsHitTestVisible = false, ClipToBounds = true };
    readonly Canvas _lineAnchor = Tac.Anchor(HorizontalAlignment.Right, VerticalAlignment.Center);
    readonly StackPanel _timers = new() { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
    readonly Border _timersInLine = new() { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 0, 0) };
    /// <summary>The timer chips over the line, when the pickup strip takes their room on it.</summary>
    readonly Border _timersUp = new() { HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(4, 0, 0, 5), Visibility = Visibility.Collapsed };
    /// <summary>The pickup strip in Versus: 24 px tiles beside the hour, a chrome rule, a gold star.</summary>
    static readonly PickStrip.Look Strip = new(24, 4, true, Frozen(Mod.A(Color.FromRgb(0xD8, 0xE0, 0xF0), .4)), Frozen(Color.FromRgb(0xFF, 0xD8, 0x1F)));
    // the super meter under the hour: the share of your best in ten glowing segments
    readonly Grid _super = new() { Height = 12, Margin = new Thickness(15, 0, 20, 7), Visibility = Visibility.Collapsed };
    TacSketch? _superArt;
    bool _dim;
    double _share = -1, _complete, _sheen = -1;
    bool _beat;
    Grid? _firstChip;

    // the rare drop under the Buddy, and the notice line
    readonly Grid _rare = new() { HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 4, 0, 0), Visibility = Visibility.Collapsed };
    readonly Grid _note = new() { HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(6, 4, 0, 0), Visibility = Visibility.Collapsed };

    // the fight: its slab, head, HP, rows and foot; the slams over it
    readonly Grid _fight = new() { HorizontalAlignment = HorizontalAlignment.Left, Visibility = Visibility.Collapsed, Margin = new Thickness(0, 0, 0, 8) };
    readonly TacSketch _fightGround;
    readonly StackPanel _fightBody = new() { Margin = new Thickness(10, 8, 12, 9) };
    readonly Border _timersInFight = new() { Margin = new Thickness(0, 0, 0, 7), HorizontalAlignment = HorizontalAlignment.Left };
    readonly Grid _fightFx = new() { IsHitTestVisible = false, ClipToBounds = true };
    readonly Border _shimmerHost = new() { Width = 0, Height = 0, IsHitTestVisible = false };
    readonly TranslateTransform _fightShift = new();
    FrameworkElement? _head;

    public HudVersus() : base(bare: true)
    {
        // the look's own rows (Ui/Themes/ThemeRows.cs): each a fighter's life gauge
        UseRows(VersusRow.Pitch, m => new VersusRow(this), 16, GapLine);
        _linePlate = new TacSketch(DrawLinePlate);
        _lineStack.Children.Add(_lineBody); _lineStack.Children.Add(_super);
        _line.Children.Add(_linePlate); _line.Children.Add(_lineFx); _line.Children.Add(_lineStack); _line.Children.Add(_lineAnchor);
        Lit(_lineBody);
        _fightGround = new TacSketch(DrawGround);
        _fight.Children.Add(_fightGround); _fight.Children.Add(_fightBody); _fight.Children.Add(_fightFx); _fight.Children.Add(_shimmerHost);
        _fight.RenderTransform = _fightShift;
        _runHeadLit = Lit(_runHead); _runHeadLit.Visibility = Visibility.Collapsed;
        _body.Children.Add(_fight); _body.Children.Add(_timersUp); _body.Children.Add(_runHeadLit); _body.Children.Add(_line); _body.Children.Add(_rare); _body.Children.Add(_note);
        _root.Children.Add(_body);
        System.Windows.Documents.TextElement.SetFontFamily(_root, Print);
        TextOptions.SetTextFormattingMode(_root, TextFormattingMode.Display);
    }

    public override FrameworkElement Root => _root;
    public override Thickness Inset => _root.Margin;
    public override double Corner => 0;
    public override FrameworkElement? FightFrame => _fight.Visibility == Visibility.Visible ? _fight : null;

    public override void SetOpacity(double opacity)
    {
        // the slab behind the fight takes the setting (the gauges and plates keep their dark ground: the rows always read)
        _opacity = Math.Clamp(opacity, 0.3, 1);
        _fightGround.InvalidateVisual();
    }

    protected override string LineSig(Figures f) => base.LineSig(f) + "|" + (Rare(Values)?.Id ?? "") + "|" + (Picks(Values).FirstOrDefault()?.Id ?? "") + "|" + Dimmed(Values) + "|" + PickStrip.Sig(Values, Strip) + "|" + RunBuddy.Sig(Values);

    static bool Dimmed(IReadOnlyDictionary<string, string> v) => (v.TryGetValue("show", out var s) && s == "dim") || (v.TryGetValue("paused", out var p) && p.Length > 0);

    // ================================================================ the Buddy

    protected override void DrawLine(Figures f)
    {
        _lineBody.Children.Clear();
        _timerUp = false; _stripTimerW = -1;
        _super.Children.Clear(); _super.Visibility = Visibility.Collapsed; _superArt = null;
        if (_timersInLine.Child != null) _timersInLine.Child = null;
        _dim = Dimmed(Values);
        _line.Opacity = _dim ? .66 : 1;
        bool mark = f.Abyss && f.Counted && f.Icon.Length > 0;
        if (mark) { var m = AbyssMark(f.Icon, 36); m.Margin = new Thickness(0, 0, 9, 0); _lineBody.Children.Add(m); }
        // in a dungeon run: the run's clock, splits and verdict in place of the hour (HudVersus.Run.cs)
        var rs = RunNow();
        _runHeadLit.Visibility = Visibility.Collapsed;
        if (rs != null) DrawRun(rs);
        else if (!HasHour(f))
        {
            var w = T(Waiting(f.Abyss), 12.5, Soft, FontWeights.SemiBold, italic: true); w.Margin = new Thickness(0, 6, 0, 6);
            // the kills and the Kinah so far under it (Steven, 2026-11-21: information, not emptiness)
            var stats = Sub(f, false);
            if (stats.Inlines.Count > 0)
            {
                w.Margin = new Thickness(0, 2, 0, 0);
                var sp = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 0, 2) };
                sp.Children.Add(w); sp.Children.Add(stats);
                _lineBody.Children.Add(sp);
            }
            else _lineBody.Children.Add(w);
        }
        else
        {
            if (HasRing(f))
            {
                _share = f.Share; _beat = f.Beat; _complete = 0;
                // the super meter: "91%" in a small chrome box, then ten segments glowing cyan to the share (gold once beaten)
                _super.ColumnDefinitions.Clear();
                _super.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); _super.ColumnDefinitions.Add(new ColumnDefinition());
                var pc = Big(f.Beat ? "★" : Pct(f.Share) + "%", 11.5, f.Beat ? Yellow : Cyan);
                pc.Margin = new Thickness(0, -2, 6, -2); pc.VerticalAlignment = VerticalAlignment.Center;
                _super.Children.Add(Lit(pc));
                _superArt = new TacSketch(DrawSuper);
                Grid.SetColumn(_superArt, 1); _super.Children.Add(_superArt);
                _super.Visibility = Visibility.Visible;
            }
            // the hour giant, its unit small; the session and the latest pickup under it
            var words = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            var top = new StackPanel { Orientation = Orientation.Horizontal };
            var rate = f.Warm ? T(f.Rate, 15, Soft, FontWeights.SemiBold, italic: true) : Big(f.Rate, 27, White);
            if (!f.Warm) rate.Margin = new Thickness(0, -3, 0, -3);
            rate.Tag = "buddy-rate";
            top.Children.Add(rate);
            if (f.Unit.Length > 0) { var u = T(f.Unit, 11, Yellow, FontWeights.Bold, italic: true); u.VerticalAlignment = VerticalAlignment.Bottom; u.Margin = new Thickness(3, 0, 0, 3); top.Children.Add(u); }
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
                if (strip != null) { sub = bare; strip.Margin = new Thickness(12, 0, 0, 0); }
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
        _linePlate.InvalidateVisual();

        // the rare drop the window holds: its tile on a plate in its grade's colour, RARE DROP and its name
        _rare.Children.Clear();
        if (!KinahHidden && Rare(Values) is { } r)
        {
            var c = ColorOf(r.Color, r.Grade);
            _rareBloom = 1;
            var plate = new TacSketch((dc, s) => PlateArt(dc, s, Lean, HotStripe, c, _rareBloom));
            _rarePlate = plate;
            _rare.Children.Add(plate);
            var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(14, 4, 22, 4) };
            var tile = Tile(r, 26); tile.Margin = new Thickness(0, 0, 9, 0);
            _rareTile = tile;
            row.Children.Add(tile);
            var mk = Badge(L.T("RARE DROP"), YellowC, size: 8); mk.Margin = new Thickness(0, 0, 8, 0);
            row.Children.Add(mk);
            var nm = T(r.Name, 13, Frozen(Mod.Mix(c, Colors.White, .5)), FontWeights.Bold, NameFace, italic: true);
            row.Children.Add(nm);
            if (r.Count.Length > 0) { var cn = T(" " + r.Count, 11, Soft, FontWeights.SemiBold); cn.Margin = new Thickness(2, 1, 0, 0); row.Children.Add(cn); }
            _rare.Children.Add(Lit(row));
            _rareFx = new Grid { IsHitTestVisible = false, ClipToBounds = true };
            _rare.Children.Add(_rareFx);
            _rareAnchor = Tac.Anchor(HorizontalAlignment.Right, VerticalAlignment.Center);
            _rare.Children.Add(_rareAnchor);
            _rare.Visibility = Visibility.Visible;
        }
        else { _rare.Visibility = Visibility.Collapsed; _rarePlate = null; _rareTile = null; _rareFx = null; _rareAnchor = null; }

        // the notice: its words in their tone on a small plate under the Buddy, the warning sign before a pause
        _note.Children.Clear();
        if (f.Note.Length > 0 && !KinahHidden)
        {
            bool warn = ReferenceEquals(f.NoteTone, Warn);
            var sp = new StackPanel { Orientation = Orientation.Horizontal };
            if (warn) { var wi = WarnIcon(); wi.Width = wi.Height = 13; wi.Margin = new Thickness(0, 0, 5, 0); sp.Children.Add(wi); }
            var tone = warn ? Color.FromRgb(0xFF, 0xB0, 0x86) : ReferenceEquals(f.NoteTone, Gold) ? YellowC : ReferenceEquals(f.NoteTone, HudKit.Mint) ? MintC : Color.FromRgb(0xF2, 0xF4, 0xF8);
            var t = T(f.Note, 11.5, Frozen(tone), FontWeights.SemiBold); t.TextTrimming = TextTrimming.CharacterEllipsis; t.MaxWidth = 380;
            sp.Children.Add(t);
            _note.Children.Add(Plated(Lit(sp), Lean, warn ? HotStripe : GreyStripe, new Thickness(12, 3, 14, 4)));
            _note.Visibility = Visibility.Visible;
        }
        else _note.Visibility = Visibility.Collapsed;
    }

    /// <summary>The line under the hour: the session, the latest pickup and its worth (<paramref name="pick"/>); in the Abyss its stats.</summary>
    TextBlock Sub(Figures f, bool pick)
    {
        var sub = T("", 10.5, Frozen(Mod.A(Colors.White, .86)), FontWeights.Medium);
        sub.Margin = new Thickness(1, 1, 0, 0);
        void Dot() { if (sub.Inlines.Count > 0) sub.Inlines.Add(new System.Windows.Documents.Run("  ·  ") { Foreground = Quiet }); }
        if (f.Total.Length > 0) { sub.Inlines.Add(new System.Windows.Documents.Run(f.Total) { FontWeight = FontWeights.Bold, Foreground = White }); sub.Inlines.Add(new System.Windows.Documents.Run(" " + f.TotalWord)); }
        foreach (var (fig, word, tone) in f.Stats)
        {
            Dot();
            var c = ReferenceEquals(tone, Rec) ? CoralC : ReferenceEquals(tone, Gold) ? YellowC : Colors.White;
            sub.Inlines.Add(new System.Windows.Documents.Run(fig) { FontWeight = FontWeights.Bold, Foreground = Frozen(c) });
            sub.Inlines.Add(new System.Windows.Documents.Run(" " + word));
        }
        if (pick && !f.Abyss && Picks(Values).FirstOrDefault() is { } p)
        {
            Dot();
            sub.Inlines.Add(new System.Windows.Documents.Run(p.Name.Length > 22 ? p.Name[..21].TrimEnd() + "…" : p.Name));
            var worth = p.Worth.Length > 0 ? "+" + p.Worth.TrimStart('+') : p.Count;
            if (worth.Length > 0) sub.Inlines.Add(new System.Windows.Documents.Run(" " + worth) { FontWeight = FontWeights.Bold, Foreground = p.Worth.Length > 0 ? Mint : Soft });
        }
        return sub;
    }

    /// <summary>The timer chips' room at the line's end, 0 with none.</summary>
    double LineTimerW() => _timers.Children.Count > 0 && !KinahHidden ? 10 + Mod.W(_timers) : 0;

    /// <summary>The timers' room the line's strip was fitted to (-1: no strip time); chips that change it draw the line again.</summary>
    double _stripTimerW = -1;

    /// <summary>The timer chips sit over the line, to leave its room to the pickup strip.</summary>
    bool _timerUp;

    double _rareBloom = 1;
    TacSketch? _rarePlate;
    FrameworkElement? _rareTile;
    Grid? _rareFx;
    Canvas? _rareAnchor;

    static readonly Brush SheenBand = Mod.Grad(false, (Mod.A(Colors.White, 0), 0), (Mod.A(Colors.White, .3), .5), (Mod.A(Colors.White, 0), 1));

    /// <summary>The Buddy's plate: navy-black, leaning, a hot stripe at its leading edge (chrome while paused); a pickup's light crossing it.</summary>
    void DrawLinePlate(DrawingContext dc, Size s)
    {
        if (s.Width < 6 || s.Height < 6) return;
        PlateArt(dc, s, .2, _dim ? GreyStripe : HotStripe);
        if (_sheen is >= 0 and <= 1)
        {
            double x = -50 + (s.Width + 60) * _sheen;
            dc.PushClip(Slab(new Rect(1.5, 1.5, s.Width - 3, s.Height - 3), .2));
            dc.PushTransform(new SkewTransform(-12, 0, 0, s.Height / 2));
            dc.DrawRectangle(SheenBand, null, new Rect(x, 0, 50, s.Height));
            dc.Pop(); dc.Pop();
        }
    }

    /// <summary>The super meter: ten leaning segments, lit to the share of your best in cyan with a glow under them (gold once beaten, or as it completes).</summary>
    void DrawSuper(DrawingContext dc, Size s)
    {
        if (s.Width < 30) return;
        int n = s.Width < 170 ? 5 : 10;
        double gap = 2.5, h = s.Height - 2, sw = (s.Width - gap * (n - 1) - Lean * h) / n;
        double share = Math.Clamp(_beat ? 1 : _share, 0, 1);
        double shown = share + (1 - share) * Math.Clamp(_complete, 0, 1);
        bool gold = _beat || _complete >= 1;
        var fill = gold ? GoldFill : SuperFill;
        var glow = Frozen(Mod.A(gold ? YellowC : CyanC, .3));
        double lit = shown * n;
        if (lit > .02) dc.DrawGeometry(glow, null, Slab(new Rect(-1, 0, Math.Min(s.Width + 1, (sw + gap) * lit + Lean * h + 1), s.Height), Lean));
        for (int i = 0; i < n; i++)
        {
            var r = new Rect(i * (sw + gap), 1, sw + Lean * h, h);
            var seg = Slab(r);
            dc.DrawGeometry(Track, null, seg);
            double part = Math.Clamp(lit - i, 0, 1);
            if (part > 0)
            {
                dc.PushClip(seg);
                dc.DrawGeometry(fill, null, Slab(new Rect(r.Left - 1, r.Top, (sw) * part + 1 + Lean * h, h)));
                dc.Pop();
            }
            dc.DrawGeometry(null, Mod.P(Frozen(Mod.A(Colors.Black, .9)), 1.2), seg);
        }
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
            bool first = _timers.Children.Count == 0;
            var c = tone == TimerBadge.Tone.On ? MintC : tone == TimerBadge.Tone.Soon ? YellowC : SteelC;
            var t = T(text, first ? 11.5 : 10, first ? White : Soft, first ? FontWeights.Bold : FontWeights.SemiBold, italic: true);
            if (tone == TimerBadge.Tone.On) t.Foreground = Frozen(Mod.Mix(MintC, Colors.White, .45));
            var chip = Plated(Lit(t), Lean, Frozen(c), first ? new Thickness(13, 3, 12, 4) : new Thickness(12, 2, 10, 3));
            chip.Margin = new Thickness(first ? 0 : 3, 0, 0, 0);
            chip.Tag = first ? "timer-chip" : null;
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

    static readonly Pen SlashEdge = Mod.P(Frozen(Mod.A(SteelC, .4)), 1), Streak = Mod.P(Frozen(Mod.A(Colors.White, .05)), 1);

    /// <summary>The fight's slab: a dark ground with its top-left corner cut and its right edge leaning, a hot slash along the cut, faint speed lines.</summary>
    void DrawGround(DrawingContext dc, Size s)
    {
        if (s.Width < 40 || s.Height < 30) return;
        double w = s.Width, h = s.Height, cut = 18, lean = 10;
        var g = new StreamGeometry();
        using (var c = g.Open())
        {
            c.BeginFigure(new Point(cut, 0), true, true);
            c.LineTo(new Point(w, 0), true, false);
            c.LineTo(new Point(w - lean, h), true, false);
            c.LineTo(new Point(0, h), true, false);
            c.LineTo(new Point(0, cut), true, false);
        }
        g.Freeze();
        double a = .55 + .4 * _opacity;
        dc.DrawGeometry(Mod.Grad(true, (Mod.A(Ink2C, .78 * a), 0), (Mod.A(InkC, .7 * a), 1)), null, g);
        dc.PushClip(g);
        for (int i = 0; i < 6; i++) { double x = w * .55 + i * 22; dc.DrawLine(Streak, new Point(x + 40, 0), new Point(x, h)); }
        dc.Pop();
        dc.DrawGeometry(null, SlashEdge, g);
        // the hot slash along the cut corner, and a short one at the foot's right
        var slash = new StreamGeometry();
        using (var c = slash.Open())
        {
            c.BeginFigure(new Point(0, cut + 9), true, true);
            c.LineTo(new Point(cut + 9, 0), true, false);
            c.LineTo(new Point(cut + 16, 0), true, false);
            c.LineTo(new Point(0, cut + 16), true, false);
        }
        slash.Freeze();
        dc.DrawGeometry(HotStripe, null, slash);
    }

    static readonly Brush TimerFill = Mod.Grad(true, (Ink2C, 0), (InkC, 1));
    static readonly Pen TimerSteel = Mod.P(Frozen(SteelC), 1.4), TimerOut = Mod.P(Frozen(Colors.Black), 3.4);

    /// <summary>The round timer's box: chamfered, navy-black, a black rim and a chrome one, a hot bar along its top.</summary>
    static void DrawTimerBox(DrawingContext dc, Size s)
    {
        if (s.Width < 10 || s.Height < 10) return;
        var r = new Rect(1.7, 1.7, s.Width - 3.4, s.Height - 3.4);
        var box = Chop(r, 7);
        dc.DrawGeometry(TimerFill, TimerOut, box);
        dc.PushClip(box);
        dc.DrawRectangle(HotStripe, null, new Rect(r.Left, r.Top, r.Width, 3));
        dc.DrawRectangle(Frozen(Mod.A(Colors.White, .07)), null, new Rect(r.Left, r.Top, r.Width, r.Height * .45));
        dc.Pop();
        dc.DrawGeometry(null, TimerSteel, Chop(new Rect(r.Left + 2.2, r.Top + 2.2, r.Width - 4.4, r.Height - 4.4), 5.5));
    }

    static readonly Brush HpFill = Mod.Grad(true, (PaleYC, 0), (YellowC, .5), (OrangeC, 1)), HpLow = Mod.Grad(true, (Color.FromRgb(0xFF, 0x9A, 0x8A), 0), (RedC, .5), (BloodC, 1));
    static readonly Pen ThirdPen = Mod.P(Frozen(Mod.A(Colors.Black, .7)), 2);

    protected override void DrawFight(Encounter? e, DateTime now)
    {
        _fightBody.Children.Clear();
        if (e == null) { _fight.Visibility = Visibility.Collapsed; _fightFx.Children.Clear(); PlaceTimers(); return; }
        double w = FightWidth - 10;
        _fight.Width = w;
        double inner = w - _fightBody.Margin.Left - _fightBody.Margin.Right;
        _fightBody.Children.Add(_timersInFight);
        // the head: the round timer at its left, the boss on a slanted banner, the opponent's gauge under it
        var head = new Grid();
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); head.ColumnDefinitions.Add(new ColumnDefinition());
        var right = new StackPanel();
        Grid.SetColumn(right, 1); head.Children.Add(right);
        // the banner: the boss's name exactly as given, the outcome or READY at its end
        var banner = new Grid { Height = 24 };
        banner.ColumnDefinitions.Add(new ColumnDefinition()); banner.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var bannerArt = new TacSketch((dc, s) => PlateArt(dc, s, Lean, HotStripe));
        Grid.SetColumnSpan(bannerArt, 2); banner.Children.Add(bannerArt);
        var title = T(e.Title, 14, White, FontWeights.Bold, NameFace, italic: true); title.TextTrimming = TextTrimming.CharacterEllipsis; title.Margin = new Thickness(15, 0, 6, 1);
        banner.Children.Add(Lit(title));
        FrameworkElement? endMark = null;
        if (e.Waiting) endMark = Badge(L.T("READY"), MintC, size: 9);
        else if (OutcomeText(e) is { Length: > 0 } outcome)
        {
            var b = Badge(outcome, e.Outcome == "wipe" ? CoralC : MintC, size: 9); b.Tag = "outcome";
            endMark = b;
        }
        if (endMark != null) { endMark.Margin = new Thickness(0, 0, 12, 0); Grid.SetColumn(endMark, 1); banner.Children.Add(endMark); }
        right.Children.Add(banner);
        if (!e.Waiting)
        {
            // the round timer: the fight clock big in Impact in a chamfered box
            var clock = Big(Clock(e, now), 24, Yellow); clock.Tag = "fight-clock";
            clock.HorizontalAlignment = HorizontalAlignment.Center; clock.Margin = new Thickness(0, 3, 2, 0);
            var box = new Grid { MinWidth = Math.Max(54, TextW(clock, clock.Text) + 20), Margin = new Thickness(0, 0, 5, 0) };
            box.Children.Add(new TacSketch(DrawTimerBox));
            box.Children.Add(Lit(clock));
            head.Children.Add(box);
        }
        _head = head;
        _fightBody.Children.Add(head);
        if (e.Waiting)
        {
            var wt = T(e.ReadyText, 12.5, Soft, FontWeights.SemiBold, italic: true); wt.Margin = new Thickness(4, 7, 0, 2); wt.TextTrimming = TextTrimming.CharacterEllipsis;
            _fightBody.Children.Add(Lit(wt));
            if (PartyBeforeHit(e)) { _fightBody.Children.Add(new Border { Height = 8 }); PutRows(_fightBody, e, now, inner); }
        }
        else
        {
            if (HpOf(e) is { } hp)
            {
                // the boss's HP as the opponent's gauge: mirrored, anchored at the right, draining away from the timer; ticks at the thirds
                // (the figures on a dark tag at its drained end next to the timer, never over the fill, never mirrored)
                var hpBox = new Grid { Height = 18, Margin = new Thickness(0, 4, 0, 0) };
                var track = new Grid { RenderTransformOrigin = new Point(.5, .5), RenderTransform = new ScaleTransform(-1, 1) };
                track.Children.Add(new TacSketch((dc, s) =>
                {
                    var r = new Rect(1.5, 1, s.Width - 3, s.Height - 2);
                    Gauge(dc, r, hp, hp < .34 ? HpLow : HpFill, null, ticks: false, k: .5);
                    for (int k = 1; k <= 2; k++) LeanLine(dc, ThirdPen, r.Left + r.Width * k / 3, r.Top + 1, r.Bottom - 1, .5);
                }));
                HpGhost(track, e, hp, now, 1.5);
                hpBox.Children.Add(track);
                var hl = T("", 10, Soft, FontWeights.SemiBold, italic: true); hl.Margin = new Thickness(8, 0, 12, 1);
                hl.Inlines.Add(new System.Windows.Documents.Run($"{Math.Round(hp * 100):0}%") { FontWeight = FontWeights.Bold, Foreground = hp < .34 ? Frozen(Color.FromRgb(0xFF, 0x9A, 0x9A)) : PaleY });
                var left = HpLeftText(e);
                if (left.Length > 0) hl.Inlines.Add(new System.Windows.Documents.Run("  ·  " + left));
                hl.Tag = "hp-text";
                var tag = new Grid { Height = 12, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(13, 0, 0, 0) };
                tag.Children.Add(new TacSketch((dc, s) => dc.DrawGeometry(Frozen(Mod.A(InkC, .86)), null, Slab(new Rect(0, 0, s.Width, s.Height), .5))));
                tag.Children.Add(Lit(hl));
                hpBox.Children.Add(tag);
                right.Children.Add(hpBox);
                _fightBody.Children.Add(new Border { Height = 7 });
            }
            else _fightBody.Children.Add(new Border { Height = 8 });
            PutRows(_fightBody, e, now, inner);
            if (e.HasDamage)
            {
                _fightBody.Children.Add(new Border { Height = 4 });
                _fightBody.Children.Add(Foot(e, now));
            }
        }
        _fight.Visibility = Visibility.Visible;
        PlaceTimers();
    }

    // ================================================================ the leader and the shimmer

    long _leadId;
    string _leadFight = "";

    /// <summary>The leader's row as drawn: a new leader in the fight seen (with effects playing) slams the spark onto them and runs the shimmer.</summary>
    void Lead(VersusRow row, MeterMember m, MeterContext c)
    {
        bool changed = _leadFight == c.E.Id && _leadId != 0 && _leadId != m.Id;
        _leadFight = c.E.Id; _leadId = m.Id;
        if (changed && c.Moves) { row.Land(); Shimmer(); }
    }

    VersusRow? LeaderRow() => Meter is { } m && m.Members.FirstOrDefault(x => x.Rank == 1 && !x.CpOnly) is { } top ? m.RowOf(top.Id) as VersusRow : null;

    /// <summary>The bar shimmer: a light sweeps along each gauge to its cap, the rows one after another down the meter.</summary>
    void Shimmer(double ms = 1100)
    {
        Motion.Play(_shimmerHost, ms, t => RowsShimmer(t), () => RowsShimmer(-1));
    }

    void RowsShimmer(double t)
    {
        if (Meter is not { } m) return;
        foreach (var x in m.Members) if (m.RowOf(x.Id) is VersusRow r) r.Shimmer(t);
    }

    // ================================================================ the moments

    /// <summary>A pickup: its chip punches in from the side of the Buddy (sliding out of the plate with speed lines) as a light crosses the plate, then fades.</summary>
    protected override void Pickup(Pick p, bool held)
    {
        if (KinahHidden || _line.Visibility != Visibility.Visible) return;
        var worth = p.Worth.Length > 0 ? "+" + p.Worth.TrimStart('+') : p.Count;
        var words = new StackPanel { MaxWidth = 62 };
        words.Children.Add(Big(worth, 14, p.Worth.Length > 0 ? Frozen(Mod.Mix(MintC, Colors.White, .25)) : White));
        var nm = T(p.Name.Length > 12 ? p.Name[..11].TrimEnd() + "…" : p.Name, 9.5, Soft, FontWeights.SemiBold); nm.Margin = new Thickness(0, -1, 0, 0);
        words.Children.Add(nm);
        var chip = Plated(Lit(words), Lean, HotStripe, new Thickness(12, 2, 10, 3));
        var shift = new TranslateTransform();
        chip.RenderTransform = shift;
        chip.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        Canvas.SetLeft(chip, 4); Canvas.SetTop(chip, -chip.DesiredSize.Height / 2);
        var lines = Streaks(YellowC, 5, 7); lines.Width = 26; lines.Height = chip.DesiredSize.Height;
        var lineAt = new TranslateTransform(); lines.RenderTransform = lineAt;
        Canvas.SetLeft(lines, -20); Canvas.SetTop(lines, -chip.DesiredSize.Height / 2);
        _lineAnchor.Children.Clear(); _lineAnchor.Children.Add(lines); _lineAnchor.Children.Add(chip);
        Motion.Play(chip, 1700, t =>
        {
            _sheen = t < .45 ? Seg(t, 0, .45) : -1; _linePlate.InvalidateVisual();
            double k = Seg(t, .06, .26);
            shift.X = -26 * (1 - Back(k, 2.2));
            chip.Opacity = t < .82 ? Seg(t, .06, .14) : 1 - Seg(t, .82, 1);
            lines.Opacity = t < .1 ? 0 : .9 * (1 - Seg(t, .12, .4));
            lineAt.X = -10 * Seg(t, .1, .4);
        }, () => { _sheen = -1; _linePlate.InvalidateVisual(); _lineAnchor.Children.Clear(); });
    }

    /// <summary>
    /// A rare drop: SPECIAL! slams in beside its plate (in the room the window keeps at the right, never over the name) on a hit
    /// spark, a spark bursts behind the tile as it pops in, a flash frame crosses the plate as it blooms out; then the word fades.
    /// </summary>
    protected override void RareDrop(Pick p, bool held)
    {
        if (_rarePlate is not { } plate || _rareFx is not { } fx || _rareAnchor is not { } anchor) return;
        var tile = _rareTile;
        var sc = new ScaleTransform(1, 1);
        if (tile != null) { tile.RenderTransformOrigin = new Point(.5, .5); tile.RenderTransform = sc; }
        string word = L.T("SPECIAL!");
        var spark = Spark(20, YellowC, 11); spark.HorizontalAlignment = HorizontalAlignment.Left; spark.Margin = new Thickness(2, 0, 0, 0);
        var spSc = new ScaleTransform(1, 1); spark.RenderTransform = spSc;
        var flash = new System.Windows.Shapes.Rectangle { Fill = White, Opacity = 0 };
        fx.Children.Clear(); fx.Children.Add(flash); fx.Children.Add(spark);
        var slam = Slam(word, SlamSize(word, 22, 92, 34), SlamFill, stroke: 4);
        var ss = new ScaleTransform(1, 1); slam.RenderTransform = ss;
        var burst = Spark(Math.Min(17, slam.Height * .6), RedC, 10);
        var bs = new ScaleTransform(1, 1); burst.RenderTransform = bs;
        Canvas.SetLeft(slam, 2); Canvas.SetTop(slam, -slam.Height / 2);
        Canvas.SetLeft(burst, 2 + slam.Width / 2 - burst.Width / 2); Canvas.SetTop(burst, -burst.Height / 2);
        anchor.Children.Clear(); anchor.Children.Add(burst); anchor.Children.Add(slam);
        Motion.Play(plate, 1300, t =>
        {
            _rareBloom = Out(Seg(t, 0, .3)); plate.InvalidateVisual();
            sc.ScaleX = sc.ScaleY = .6 + .4 * Back(Seg(t, .05, .35), 2.2);
            double b = Seg(t, .1, .6);
            spark.Opacity = t < .1 ? 0 : 1 - b; spSc.ScaleX = spSc.ScaleY = .4 + .8 * Out(b);
            double k = Seg(t, .16, .34);
            ss.ScaleX = ss.ScaleY = SlamScale(k, 1.6);
            slam.Opacity = t < .16 ? 0 : t < .72 ? Seg(t, .16, .2) : 1 - Seg(t, .72, .92);
            double u = Seg(t, .3, .7);
            burst.Opacity = t < .3 ? 0 : .9 * (1 - u); bs.ScaleX = bs.ScaleY = .5 + .6 * Out(u);
            flash.Opacity = .45 * Flash(t, .3, .1);
        }, () => { _rareBloom = 1; plate.InvalidateVisual(); sc.ScaleX = sc.ScaleY = 1; fx.Children.Clear(); anchor.Children.Clear(); });
    }

    /// <summary>
    /// Your best passed, a super move: the super meter fills to full and turns gold, a white flash frame hits the plate, a gold
    /// light sweeps diagonally across it and on past its end, and NEW RECORD punches in beside the score on a dark red slab
    /// (in the room the window keeps at the right, the score never covered); then it all clears.
    /// </summary>
    protected override void BestPassed(bool held)
    {
        if (_superArt is not { } art) return;
        double was = _share is > 0 and < 1 ? _share : .86;
        bool beat = _beat;
        string word = L.T("NEW RECORD");
        double lineW = Math.Max(110, _line.ActualWidth > 0 ? _line.ActualWidth : Mod.W(_line));
        var band = new TacSketch((dc, s) =>
        {
            if (s.Width < 10) return;
            dc.DrawGeometry(Mod.Grad(false, (Mod.A(YellowC, 0), 0), (Mod.A(PaleYC, .55), .7), (Mod.A(Colors.White, .7), 1)), null, Slab(new Rect(0, 0, 46, s.Height), .9));
        }) { Opacity = 0 };
        var bandAt = new TranslateTransform(); band.RenderTransform = bandAt;
        var flash = new System.Windows.Shapes.Rectangle { Fill = White, Opacity = 0 };
        _lineFx.Children.Clear();
        _lineFx.Children.Add(band); _lineFx.Children.Add(flash);
        // the word on its slab beside the plate
        var slam = Slam(word, SlamSize(word, 24, 82, 34), White, Colors.Black, 3.5);
        slam.Opacity = 1;
        var ss = new ScaleTransform(1, 1); slam.RenderTransform = ss;
        double pw = slam.Width + 12, ph = slam.Height + 4;
        var slab = new Grid { Width = pw, Height = ph };
        slab.Children.Add(new TacSketch((dc, s) =>
        {
            var g = Slab(new Rect(1.5, 1.5, s.Width - 3, s.Height - 3), .45);
            dc.DrawGeometry(Mod.Grad(true, (RedC, 0), (BloodC, .55), (Color.FromRgb(0x5A, 0x02, 0x0C), 1)), EdgeOut, g);
            dc.DrawGeometry(null, EdgeLead, g);
        }));
        slab.Children.Add(slam);
        var at = new TranslateTransform(); slab.RenderTransform = at;
        Canvas.SetLeft(slab, 4); Canvas.SetTop(slab, -ph / 2);
        var lines = Streaks(PaleYC, 6, 13); lines.Width = 24; lines.Height = ph;
        var lineAt = new TranslateTransform(); lines.RenderTransform = lineAt;
        Canvas.SetLeft(lines, -18); Canvas.SetTop(lines, -ph / 2);
        _lineAnchor.Children.Clear(); _lineAnchor.Children.Add(lines); _lineAnchor.Children.Add(slab);
        Motion.Play(art, 1600, t =>
        {
            _beat = false; _share = was;
            _complete = Out(Seg(t, 0, .3));
            if (t >= .3) { _beat = beat; _complete = 1; }
            art.InvalidateVisual();
            flash.Opacity = .45 * Flash(t, .3, .07);
            double b = Seg(t, .3, .55);
            band.Opacity = t < .3 || t > .55 ? 0 : Math.Sin(Math.PI * b);
            bandAt.X = -50 + (lineW + 60) * Out(b);
            double k = Seg(t, .32, .44);
            at.X = -22 * (1 - Back(k, 2.2));
            slab.Opacity = t < .32 ? 0 : t < .82 ? Seg(t, .32, .37) : 1 - Seg(t, .82, .97);
            ss.ScaleX = ss.ScaleY = SlamScale(Seg(t, .36, .5), 1.5);
            lines.Opacity = t < .34 ? 0 : .9 * (1 - Seg(t, .36, .7)); lineAt.X = -10 * Seg(t, .34, .7);
        }, () => { _complete = 0; _beat = beat; art.InvalidateVisual(); _lineFx.Children.Clear(); _lineAnchor.Children.Clear(); });
    }

    /// <summary>
    /// An entry opened, a challenger's entrance: the first timer's chip winds up (crouched, dimmed), punches out to past its
    /// size on speed streaks, lands with a white flash frame, a jolt and a mint hit spark at its leading edge, a mint shockwave
    /// of its outline spreads off it, and it settles where it rests.
    /// </summary>
    protected override void EntryOpened(TimerBoard.Item i, bool held)
    {
        if (_firstChip is not { } chip) return;
        var flash = new TacSketch((dc, s) => dc.DrawGeometry(Frozen(Mod.A(Colors.White, .9)), null, Slab(new Rect(1.5, 1.5, s.Width - 3, s.Height - 3)))) { Opacity = 0 };
        var wave = new TacSketch((dc, s) => dc.DrawGeometry(null, Mod.P(Frozen(MintC), 3), Slab(new Rect(1.5, 1.5, s.Width - 3, s.Height - 3))))
            { Opacity = 0, RenderTransformOrigin = new Point(.5, .5) };
        var waveSc = new ScaleTransform(1, 1); wave.RenderTransform = waveSc;
        var spark = Spark(19, MintC, 11); spark.HorizontalAlignment = HorizontalAlignment.Left; spark.Margin = new Thickness(-20, -14, 0, -14);
        var spSc = new ScaleTransform(1, 1); var spRot = new RotateTransform(0); spark.RenderTransform = new TransformGroup { Children = { spSc, spRot } };
        var lines = Streaks(MintC, 5, 21); lines.HorizontalAlignment = HorizontalAlignment.Right; lines.Width = 34; lines.Margin = new Thickness(0, 0, -36, 0);
        var linesAt = new TranslateTransform(); lines.RenderTransform = linesAt;
        chip.Children.Insert(0, lines);
        chip.Children.Add(flash); chip.Children.Add(wave); chip.Children.Add(spark);
        var sc = new ScaleTransform(1, 1); var at = new TranslateTransform();
        chip.RenderTransformOrigin = new Point(.5, .5);
        chip.RenderTransform = new TransformGroup { Children = { sc, at } };
        Motion.Play(chip, 1100, t =>
        {
            // wind-up (yanked back right, faded), the punch in (an overshoot past its place), the hit, the settle
            // (in place: a chip that travels runs into its neighbours' words)
            double wind = Seg(t, 0, .1), pop = Ez.Wob(Seg(t, .1, .6), 1.5, 4), rise = Motion.Out(Seg(t, .1, .16));
            sc.ScaleX = t < .1 ? 1 - .14 * Ez.InOut(wind) : .86 + .14 * rise + .24 * pop;
            sc.ScaleY = t < .1 ? 1 - .2 * Ez.InOut(wind) : .8 + .2 * rise + .34 * pop;
            at.X = Ez.Jolt(Seg(t, .25, .45), 2.5, 4);
            chip.Opacity = t < .1 ? 1 - .6 * wind : .4 + .6 * Seg(t, .1, .16);
            lines.Opacity = t < .1 ? 0 : .95 * (1 - Seg(t, .2, .5)); linesAt.X = 30 * Seg(t, .1, .5);
            flash.Opacity = .85 * Flash(t, .25, .1);
            double b = Seg(t, .25, .6);
            spark.Opacity = t < .25 ? 0 : 1 - b; spSc.ScaleX = spSc.ScaleY = .4 + .9 * Motion.Out(b); spRot.Angle = 40 * b;
            double w = Seg(t, .26, .75);
            wave.Opacity = t < .26 ? 0 : .95 * (1 - w); waveSc.ScaleX = 1 + .35 * Motion.Out(w); waveSc.ScaleY = 1 + .9 * Motion.Out(w);
        }, () => { chip.RenderTransform = null; chip.Opacity = 1; chip.Children.Remove(flash); chip.Children.Remove(wave); chip.Children.Remove(spark); chip.Children.Remove(lines); });
    }

    /// <summary>
    /// A fight started: FIGHT! slams across the panel (in from big, a white flash frame as it lands, speed streaks and a dark
    /// cut-in band behind it), the leader's spark lands on their row, and a light shimmers along the gauges; then it lifts.
    /// </summary>
    protected override void FightStarted(Encounter e, bool held)
    {
        Motion.Play(_fight, 420, t => { double k = Out(t); _fightShift.X = -6 * (1 - k); _fightBody.Opacity = .3 + .7 * k; }, () => { _fightShift.X = 0; _fightBody.Opacity = 1; });
        Cinema(L.T("FIGHT!"), SlamFill, null, 1000, drop: false, spark: false);
        LeaderRow()?.Land();
        Shimmer();
    }

    /// <summary>
    /// A fight ended: a kill (or a clear) slams K.O. giant and angled over the fight with a white flash frame, a hit spark and
    /// an impact ring; a wipe drops DEFEAT in steel over a dark frame. The gauges shimmer; then it fades.
    /// </summary>
    protected override void FightEnded(Encounter e, bool held)
    {
        if (e.Outcome is not ("kill" or "wipe" or "cleared")) return;
        bool wipe = e.Outcome == "wipe";
        if (wipe) Cinema(L.T("DEFEAT"), DefeatFill, BloodC, 1300, drop: true, spark: false);
        else { Cinema(L.T("K.O."), SlamFill, null, 1300, drop: false, spark: true); Shimmer(); }
    }

    /// <summary>A slam over the fight: the word, a dark band behind it, streaks, a flash frame (white, or dark for a drop), a spark and a ring for a kill.</summary>
    void Cinema(string word, Brush fill, Color? edge, double ms, bool drop, bool spark)
    {
        double w = Math.Max(120, _fight.Width is > 0 and var fw ? fw : FightWidth - 10);
        double h = _fight.ActualHeight > 0 ? _fight.ActualHeight : 70 + Math.Max(1, RowsShown) * VersusRow.Pitch;
        double size = SlamSize(word, spark ? 76 : 58, w * .9, h * .7);
        var slam = Slam(word, size, fill, edge, size > 40 ? 7 : 5);
        var ss = new ScaleTransform(1, 1); var sy = new TranslateTransform();
        slam.RenderTransform = new TransformGroup { Children = { ss, sy } };
        double bh = Math.Min(h - 8, size * 1.15);
        var band = new TacSketch((dc, s) => dc.DrawGeometry(Frozen(Mod.A(InkC, .72)), null, Slab(new Rect(-20, (s.Height - bh) / 2, s.Width + 40, bh), .35))) { Opacity = 0 };
        var bandSc = new ScaleTransform(1, 1, w / 2, h / 2); band.RenderTransform = bandSc;
        var lines = Streaks(drop ? SteelC : YellowC, 16, 5);
        lines.Height = bh; lines.VerticalAlignment = VerticalAlignment.Center;
        var lineAt = new TranslateTransform(); lines.RenderTransform = lineAt;
        var flash = new System.Windows.Shapes.Rectangle { Fill = drop ? Frozen(Colors.Black) : White, Opacity = 0 };
        var parts = new List<UIElement> { band, lines };
        TacSketch? burst = null; ScaleTransform? bs = null; RotateTransform? br = null;
        System.Windows.Shapes.Ellipse? ring = null;
        if (spark)
        {
            burst = Spark(Math.Min(h * .48, size * .95), YellowC, 13);
            bs = new ScaleTransform(1, 1); br = new RotateTransform(0);
            burst.RenderTransform = new TransformGroup { Children = { bs, br } };
            ring = Ring(Math.Min(w * .9, size * 3.2), PaleYC, 5);
            parts.Add(ring); parts.Add(burst);
        }
        parts.Add(slam); parts.Add(flash);
        _fightFx.Children.Clear();
        foreach (var el in parts) _fightFx.Children.Add(el);
        var head = _head;
        // where the word rests once it has hit: over the head (the boss's banner and the round timer), small, so every row's
        // figures read again within about 0.4 s while the word stays on screen (2026-11-21: the K.O. covered rows 2 and 3)
        double headY = h * .18;
        try { if (head != null && head.ActualHeight > 0) headY = head.TranslatePoint(new Point(0, head.ActualHeight / 2), _fightFx).Y; } catch (InvalidOperationException) { }
        double rest = Math.Clamp((head?.ActualHeight ?? 34) * 1.45 / Math.Max(1, slam.Height), .4, .7);
        double liftY = Math.Max(headY, slam.Height * rest / 2 + 2) - h / 2;
        var rot = new RotateTransform(0);
        ((TransformGroup)slam.RenderTransform).Children.Insert(1, rot);
        // start: the word flies at the camera and is gone (scale up, fade) instead of resting
        bool start = !drop && !spark;
        double hit = drop ? .17 : .15, leave = .24, gone = .34;
        Motion.Play(_fightFx, ms, t =>
        {
            // in: the band snaps open, the word slams from big (or drops from above, heavy), a flash frame as it lands
            double land = Seg(t, 0, hit + .03);
            bandSc.ScaleY = t < leave ? Out(Seg(t, 0, .08)) : 1 - Ez.InOut(Seg(t, leave, gone));
            band.Opacity = t < gone ? 1 : 0;
            double l = Seg(t, leave, gone), k = Ez.InOut(l);
            if (drop)
            {
                // a wipe: falls hard, a dead thud, then sags onto the banner, tipped like a fallen sign
                ss.ScaleX = ss.ScaleY = 1 + (rest - 1) * k;
                sy.Y = -h * .45 * (1 - Back(land, 1.2)) * (1 - k) + liftY * k;
                rot.Angle = 5 * k;
                slam.Opacity = t < .82 ? Seg(t, 0, .05) : 1 - Seg(t, .82, .97);
            }
            else if (start)
            {
                ss.ScaleX = ss.ScaleY = SlamScale(land, 2.3) * (1 + 1.4 * Ez.In(l));
                sy.Y = 0;
                slam.Opacity = t < leave ? Seg(t, 0, .05) : 1 - Out(l);
            }
            else
            {
                // a kill: slams in, holds a beat while the panel shakes, then lifts and stamps onto the head, a little turned
                double sc = SlamScale(land, 2.3);
                ss.ScaleX = ss.ScaleY = sc + (rest - sc) * k;
                sy.Y = liftY * k;
                rot.Angle = -4 * k;
                slam.Opacity = t < .84 ? Seg(t, 0, .05) : 1 - Seg(t, .84, .98);
            }
            flash.Opacity = (drop ? .45 : .55) * Flash(t, hit, .08);
            lines.Opacity = t < .06 ? 0 : .9 * (1 - Seg(t, .14, gone));
            lineAt.X = -w * .3 + w * .5 * Seg(t, .06, gone);
            // the hit shakes the whole panel: a sideways jolt for a kill, a dead vertical thud for a wipe, a light one at the start
            double s = Seg(t, hit, hit + .14);
            // (not at the start: the panel's own slide in owns its shift then)
            double jolt = Ez.Jolt(s, drop ? 5 : 6);
            if (drop) { _fightShift.X = 0; _fightShift.Y = Math.Abs(jolt); }
            else if (!start) { _fightShift.X = jolt; _fightShift.Y = jolt * .35; }
            if (burst != null)
            {
                double b = Seg(t, hit, .34);
                burst.Opacity = t < hit ? 0 : .95 * (1 - b);
                bs!.ScaleX = bs.ScaleY = .5 + .8 * Out(b); br!.Angle = 24 * b;
                double r = Seg(t, hit + .02, .38);
                ring!.Opacity = t < hit + .02 ? 0 : .9 * (1 - r);
                ((ScaleTransform)ring.RenderTransform).ScaleX = ((ScaleTransform)ring.RenderTransform).ScaleY = .2 + 1.1 * Out(r);
            }
            if (head != null) head.Opacity = start ? (t < gone ? .35 + .65 * Seg(t, leave, gone) : 1) : t < .84 ? .35 : .35 + .65 * Seg(t, .84, 1);
        }, () => { _fightFx.Children.Clear(); if (!start) _fightShift.X = _fightShift.Y = 0; if (head != null) head.Opacity = 1; });
    }
}
