using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using KinahLedger.Parser;
using KinahLedger.Session;
using static KinahLedger.Ui.HudKit;

namespace KinahLedger.Ui;

/// <summary>
/// Finals (the Esports theme, 2026-11-20; the settings' hint: "The championship's black and gold: a trophy over the leader,
/// confetti on the kill"): a world championship's grand final on the stream. Every panel is a plate of matte black under a
/// stage light falling from its top, rimmed in brushed gold with a thin gold line inside (HudFinals.Kit.cs). Titles and
/// labels in tall condensed caps (Bahnschrift), figures white and tabular.
/// The meter: the boss in condensed type and the clock over a gold rule, the boss's HP a slim crimson bar, then each member
/// on a graphite bar lit from inside in their class's colour; the leader's bar brushed gold, a gold trophy cup over their
/// rank (HudFinals.Rows.cs). The Buddy: the hour as a gold headline beside a gold medal (your best's ring, ribbon tails),
/// the session under it, the pickups in black-and-gold tiles, the next timer as a black chip rimmed in gold.
/// The moments: a band of gold light sweeps the bars as a fight starts and when the lead changes, the trophy rising onto the
/// new leader with a gleam; a kill throws gold and white confetti through the panel under a "Killed" plate; a pickup sweeps
/// the Buddy's plate, a rare drop rings out from its tile, your best passed runs the medal round to full.
/// </summary>
sealed partial class HudFinals : ModernLook
{
    /// <summary>The note's pop-out moves this (HudStackLook.PopNote).</summary>
    protected override UIElement? NoteHost => _note;

    const double Radius = 4, Pad = 10;
    /// <summary>The pickup strip on the plate: 26 px tiles in black and gold beside the hour, a gold rule, the gold star.</summary>
    static readonly PickStrip.Look Strip = new(26, 4, true, Mod.Br(AuC, .45), Frozen(AuLightC));

    // the room round the plates for the drop under them (drawn inside the window: nothing past its edge)
    readonly Grid _root = new() { Margin = new Thickness(10, 8, 10, 16), HorizontalAlignment = HorizontalAlignment.Left };
    readonly StackPanel _body = new() { HorizontalAlignment = HorizontalAlignment.Left };
    double _opacity = 0.9;

    // the Buddy: its plate, the medal, the hour, the session and the timer chips
    readonly Grid _line = new() { HorizontalAlignment = HorizontalAlignment.Left };
    readonly TacSketch _linePlate;
    readonly StackPanel _lineBody = new() { Margin = new Thickness(8, 10, 14, 7) };
    readonly StackPanel _lineRow = new() { Orientation = Orientation.Horizontal };
    readonly StackPanel _timers = new() { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
    readonly Border _timersInLine = new() { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 0) };
    /// <summary>The timer chips over the Buddy, when the pickup strip takes their room on its line.</summary>
    readonly Border _timersUp = new() { HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 0, 0, 6), Visibility = Visibility.Collapsed };
    bool _dim;
    TacSketch? _medal;
    double _share = -1, _complete, _medalGlint = -1;
    bool _beat;
    /// <summary>The light crossing the Buddy's plate (a pickup, a rare drop, your best passed): a band moved by its transform.</summary>
    readonly Band _lineGlint = new(AuPaleC, .32, 38) { Margin = new Thickness(2) };
    Border? _firstChip;

    // the rare drop on the Buddy's plate, and the notice under it
    Border? _rareRing;
    readonly Grid _note = new() { HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 5, 0, 0), Visibility = Visibility.Collapsed };

    // the meter
    readonly Grid _fight = new() { HorizontalAlignment = HorizontalAlignment.Left, Visibility = Visibility.Collapsed, Margin = new Thickness(0, 0, 0, 8) };
    readonly TacSketch _fightPlate;
    readonly StackPanel _fightBody = new() { Margin = new Thickness(Pad, 18, Pad, 9) };
    readonly Border _timersInFight = new() { Margin = new Thickness(0, 0, 0, 7), HorizontalAlignment = HorizontalAlignment.Left };
    readonly Grid _fightFx = new() { IsHitTestVisible = false };
    readonly Canvas _confetti = new() { IsHitTestVisible = false, ClipToBounds = true };
    readonly Canvas _pyro = new() { IsHitTestVisible = false, ClipToBounds = true };
    readonly Border _sweepHost = new() { IsHitTestVisible = false };
    /// <summary>The stage lights behind the head (static at rest; they swing in the moments).</summary>
    readonly StageBeams _beams = new(4);
    /// <summary>The trophy in flight to a new leader, its streak of gold and the ring it lands with (hidden at rest).</summary>
    readonly Canvas _fly = new() { IsHitTestVisible = false, ClipToBounds = true };
    readonly Trophy _flyer = new(FinalsRow.CupScale) { Opacity = 0 };
    readonly TranslateTransform _flyAt = new(), _streakAt = new(), _ringAt = new();
    readonly ScaleTransform _flyGrow = new(1, 1), _streakLen = new(1, 1), _ringGrow = new(1, 1);
    readonly System.Windows.Shapes.Rectangle _streak = new() { Width = 7, Height = 1, Opacity = 0, RadiusX = 3, RadiusY = 0, Fill = Mod.Grad(true, (Mod.A(AuPaleC, .95), 0), (Mod.A(AuC, .55), .4), (Mod.A(AuC, 0), 1)) };
    readonly System.Windows.Shapes.Ellipse _ring = new() { Width = 36, Height = 36, Opacity = 0, StrokeThickness = 2, Stroke = Mod.Br(AuLightC, .95) };
    readonly TranslateTransform _settle = new();
    FrameworkElement? _head;
    /// <summary>The fight's end plate is up over the head (the head's words hidden under it meanwhile).</summary>
    bool _plateUp;
    string _leadFight = "";
    long _leadId;

    public HudFinals()
    {
        // the look's own rows (Ui/Themes/ThemeRows.cs), a fit shared by the rows so the share column holds or goes for all
        var fit = new FinalsFit();
        UseRows(FinalsRow.Pitch, m => new FinalsRow(fit), 15, GapLine);
        _linePlate = new TacSketch((dc, s) => Plate(dc, s, Radius, _opacity, lamps: 3, beams: true));
        _lineBody.Children.Add(_lineRow);
        _line.Children.Add(_linePlate); _line.Children.Add(_lineGlint); _line.Children.Add(_lineBody);
        _fightPlate = new TacSketch((dc, s) => Plate(dc, s, Radius, _opacity, lamps: 4, rig: true));
        _fight.Children.Add(_fightPlate); _fight.Children.Add(_beams); _fight.Children.Add(_fightBody); _fight.Children.Add(_fightFx);
        _fight.Children.Add(_pyro); _fight.Children.Add(_confetti); _fight.Children.Add(_fly); _fight.Children.Add(_sweepHost);
        _flyer.RenderTransform = new TransformGroup { Children = { _flyGrow, _flyAt } };
        _flyer.RenderTransformOrigin = new Point(.5, .5);
        _streak.RenderTransform = new TransformGroup { Children = { _streakLen, _streakAt } };
        _ring.RenderTransformOrigin = new Point(.5, .5);
        _ring.RenderTransform = new TransformGroup { Children = { _ringGrow, _ringAt } };
        _fly.Children.Add(_streak); _fly.Children.Add(_ring); _fly.Children.Add(_flyer);
        _fight.RenderTransform = _settle;
        _body.Children.Add(_timersUp); _body.Children.Add(_fight); _body.Children.Add(_line); _body.Children.Add(_note);
        _root.Children.Add(_body);
        System.Windows.Documents.TextElement.SetFontFamily(_root, Bahn);
        TextOptions.SetTextFormattingMode(_root, TextFormattingMode.Display);
    }

    public override FrameworkElement Root => _root;
    public override Thickness Inset => _root.Margin;
    public override double Corner => Radius;
    public override FrameworkElement? FightFrame => _fight.Visibility == Visibility.Visible ? _fight : null;

    public override void SetOpacity(double opacity)
    {
        _opacity = Math.Clamp(opacity, 0.3, 1);
        _linePlate.InvalidateVisual(); _fightPlate.InvalidateVisual();
        foreach (var c in _note.Children.OfType<TacSketch>()) c.InvalidateVisual();
    }

    protected override string LineSig(Figures f) => base.LineSig(f) + "|" + (Rare(Values)?.Id ?? "") + "|" + (Picks(Values).FirstOrDefault()?.Id ?? "") + "|" + Dimmed(Values) + "|" + PickStrip.Sig(Values, Strip) + "|" + RunBuddy.Sig(Values);

    static bool Dimmed(IReadOnlyDictionary<string, string> v) => (v.TryGetValue("show", out var s) && s == "dim") || (v.TryGetValue("paused", out var p) && p.Length > 0);

    // ================================================================ the Buddy

    protected override void DrawLine(Figures f)
    {
        _lineRow.Children.Clear();
        while (_lineBody.Children.Count > 0) _lineBody.Children.RemoveAt(0);
        _medal = null; _rareRing = null; _rareTileEl = null; _stripEl = null; _rareRow = null; _timerUp = false; _stripTimerW = -1;
        if (_timersInLine.Child != null) _timersInLine.Child = null;
        _dim = Dimmed(Values);
        _lineBody.Opacity = _dim ? .62 : 1;
        _lineBody.Margin = new Thickness(8, 10, 14, 7);
        bool mark = f.Abyss && f.Counted && f.Icon.Length > 0;
        if (mark) { var m = AbyssMark(f.Icon, 40); m.Margin = new Thickness(2, 0, 10, 0); m.VerticalAlignment = VerticalAlignment.Center; _lineRow.Children.Add(m); }
        // in a dungeon run: the run's head, clock, splits and verdict in place of the hour (HudFinals.Run.cs)
        var rs = RunNow();
        if (rs != null) { _lineBody.Children.Add(_runHead); DrawRun(rs); }
        else if (!HasHour(f))
        {
            if (!mark) { var g = new TacSketch((dc, _) => Medal(dc, 0, false, 0, -1, empty: true), 46, 46) { Margin = new Thickness(0, 0, 10, 0), VerticalAlignment = VerticalAlignment.Center }; _lineRow.Children.Add(g); }
            var w = T(Waiting(f.Abyss), 14, Soft, FontWeights.SemiBold); w.Margin = new Thickness(0, 0, 4, 2);
            // in the Abyss before the first points: what has happened so far under it ("2 kills · Kinah"), never an empty line
            var so = f.Abyss ? Sub(f, false) : null;
            if (so != null && so.Inlines.Count > 0)
            {
                var col = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
                w.Margin = new Thickness(0, 0, 4, 1); col.Children.Add(w); col.Children.Add(so);
                _lineRow.Children.Add(col);
            }
            else _lineRow.Children.Add(w);
        }
        else
        {
            if (HasRing(f))
            {
                _share = f.Share; _beat = f.Beat; _complete = 0;
                var ring = new Grid { Width = 46, Height = 46, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 11, 0) };
                _medal = new TacSketch((dc, _) => Medal(dc, _share, _beat, _complete, _medalGlint), 46, 46);
                ring.Children.Add(_medal);
                var pc = T(f.Beat ? "★" : Pct(f.Share), f.Beat ? 17 : 14.5, f.Beat ? AuLight : White, FontWeights.SemiBold);
                pc.HorizontalAlignment = HorizontalAlignment.Center; pc.VerticalAlignment = VerticalAlignment.Top;
                pc.Margin = new Thickness(0, f.Beat ? 10 : 11, 0, 0);
                ring.Children.Add(pc);
                _lineRow.Children.Add(ring);
            }
            else if (!mark) _lineRow.Children.Add(new Border { Width = 6 });
            // the hour as a gold headline, its unit small; the session and the latest pickup under it
            var words = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            var top = new StackPanel { Orientation = Orientation.Horizontal };
            var rate = T(f.Rate, f.Warm ? 17 : 28, f.Warm ? Soft : GoldInk, FontWeights.SemiBold, cond: !f.Warm);
            rate.Tag = "buddy-rate";
            if (!f.Warm) rate.Effect = new DropShadowEffect { Color = Colors.Black, ShadowDepth = 1, Direction = 270, BlurRadius = 4, Opacity = .8 };
            top.Children.Add(rate);
            if (f.Unit.Length > 0) { var u = T(f.Unit, 12.5, AuSoft, FontWeights.SemiBold); u.VerticalAlignment = VerticalAlignment.Bottom; u.Margin = new Thickness(2, 0, 0, 4); top.Children.Add(u); }
            words.Children.Add(top);
            // farming, the pickups as the strip beside the hour where it fits (the best drop and the last three), the session
            // alone under it; with the timer chips at the line's end and no room left beside them, the chips go over the Buddy
            var sub = Sub(f, true);
            FrameworkElement? strip = null, under = null;
            if (StripTime(f))
            {
                var bare = Sub(f, false);
                double baseW = _lineBody.Margin.Left + _lineBody.Margin.Right + _lineRow.Children.OfType<FrameworkElement>().Sum(e => Mod.W(e) + e.Margin.Left + e.Margin.Right), timerW = LineTimerW();
                double wordsW = Math.Max(Mod.W(top), Mod.W(bare)), oldW = Math.Max(Mod.W(top), Mod.W(sub));
                foreach (var look in StripFall(Strip))
                {
                    if ((strip = StripIn(StripRoom(FightWidth - 8, baseW + timerW, wordsW, oldW, 14), look)) != null) break;
                    if (timerW > 0 && (strip = StripIn(StripRoom(FightWidth - 8, baseW, wordsW, oldW + timerW, 14), look)) != null) { _timerUp = true; break; }
                }
                if (strip != null) { sub = bare; strip.Margin = new Thickness(14, 0, 0, 0); GoldTiles(strip); }
                // no room beside the hour: under the session, never dropped (Steven, 2026-11-19)
                else if ((under = StripUnder(Strip, false)) != null) { sub = bare; GoldTiles(under); }
                _stripTimerW = timerW;
            }
            if (sub.Inlines.Count > 0) words.Children.Add(sub);
            if (under != null) words.Children.Add(under);
            _lineRow.Children.Add(words);
            if (strip != null) _lineRow.Children.Add(strip);
            _stripEl = strip ?? under;
        }
        _lineRow.Children.Add(_timersInLine);
        _lineBody.Children.Add(_lineRow);
        DrawRare();
        DrawNote(f);
        PlaceTimers();
    }

    /// <summary>The line under the hour: the session, the latest pickup and its worth (<paramref name="pick"/>); in the Abyss its stats.</summary>
    TextBlock Sub(Figures f, bool pick)
    {
        var sub = T("", 12, Soft, FontWeights.Normal);
        sub.Margin = new Thickness(1, 1, 0, 0);
        void Dot() { if (sub.Inlines.Count > 0) sub.Inlines.Add(new System.Windows.Documents.Run("  ·  ") { Foreground = Au }); }
        if (f.Total.Length > 0) { sub.Inlines.Add(new System.Windows.Documents.Run(f.Total) { FontWeight = FontWeights.SemiBold, Foreground = White }); sub.Inlines.Add(new System.Windows.Documents.Run(" " + f.TotalWord)); }
        foreach (var (fig, word, tone) in f.Stats)
        {
            Dot();
            var c = ReferenceEquals(tone, Rec) ? CoralC : ReferenceEquals(tone, Gold) ? AuLightC : Colors.White;
            sub.Inlines.Add(new System.Windows.Documents.Run(fig) { FontWeight = FontWeights.SemiBold, Foreground = Frozen(c) });
            sub.Inlines.Add(new System.Windows.Documents.Run(" " + word));
        }
        if (pick && !f.Abyss && Picks(Values).FirstOrDefault() is { } p)
        {
            Dot();
            sub.Inlines.Add(new System.Windows.Documents.Run(p.Name.Length > 22 ? p.Name[..21].TrimEnd() + "…" : p.Name));
            var worth = p.Worth.Length > 0 ? "+" + p.Worth.TrimStart('+') : p.Count;
            if (worth.Length > 0) sub.Inlines.Add(new System.Windows.Documents.Run(" " + worth) { FontWeight = FontWeights.SemiBold, Foreground = p.Worth.Length > 0 ? (p.Kinah ? AuLight : Mint2) : Soft });
        }
        return sub;
    }

    /// <summary>The rare drop the window holds, on the Buddy's plate under a gold rule: its tile ringed in its grade's colour, RARE DROP and its name.</summary>
    void DrawRare()
    {
        if (KinahHidden || Rare(Values) is not { } r) return;
        _lineBody.Children.Add(GoldRule(new Thickness(0, 8, 0, 7), .55));
        var rr = new StackPanel { Orientation = Orientation.Horizontal };
        var c = ColorOf(r.Color, r.Grade);
        var tile = new Grid { Width = 44, Height = 36 };
        var box = new Rect(6, 2, 32, 32);
        tile.Children.Add(new TacSketch((dc, _) =>
        {
            dc.DrawGeometry(Mod.Br(Black0, .9), Mod.P(Mod.Br(c, .9), 1.4), Mod.Pill(box, 4));
            dc.DrawGeometry(null, Mod.P(Mod.Br(AuC, .5), 1), Mod.Pill(new Rect(box.X - 2.5, box.Y - 2.5, box.Width + 5, box.Height + 5), 5));
        }, 44, 36));
        // the rare drop's ring: a frame of the grade's colour that spreads out and fades (scaled, never redrawn)
        _rareRing = new Border { Width = 37, Height = 37, CornerRadius = new CornerRadius(5), BorderThickness = new Thickness(2), BorderBrush = Mod.Br(c, .85),
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, -.5, 0, 0), Opacity = 0,
            RenderTransformOrigin = new Point(.5, .5), RenderTransform = new ScaleTransform(1, 1), IsHitTestVisible = false };
        tile.Children.Add(_rareRing);
        var t = Tile(r, 26); t.HorizontalAlignment = HorizontalAlignment.Center; t.VerticalAlignment = VerticalAlignment.Top; t.Margin = new Thickness(0, 5, 0, 0);
        tile.Children.Add(t);
        _rareTileEl = t;
        rr.Children.Add(tile);
        var w = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(9, 0, 0, 0) };
        w.Children.Add(Label(L.T("RARE DROP"), 10, AuSoft));
        var nm = T(r.Name, 15, Frozen(Mod.Mix(c, Colors.White, .3)), FontWeights.SemiBold);
        if (r.Count.Length > 0) nm.Inlines.Add(new System.Windows.Documents.Run("  " + r.Count) { FontSize = 12, Foreground = Quiet });
        w.Children.Add(nm);
        rr.Children.Add(w);
        _lineBody.Children.Add(rr);
        _rareRow = rr;
    }

    /// <summary>The notice under the Buddy: its words in their tone on a small black plate, the warning sign before a pause.</summary>
    void DrawNote(Figures f)
    {
        _note.Children.Clear();
        if (f.Note.Length == 0 || KinahHidden) { _note.Visibility = Visibility.Collapsed; return; }
        bool warn = ReferenceEquals(f.NoteTone, Warn);
        _note.Children.Add(new TacSketch((dc, s) => Plate(dc, s, 3, _opacity, shadow: false, spot: false, rim: .55)));
        var sp = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(10, 4, 11, 5) };
        if (warn) { var wi = WarnIcon(); wi.Width = wi.Height = 13; wi.Margin = new Thickness(0, 0, 6, 0); sp.Children.Add(wi); }
        var tone = warn ? WarnC2 : ReferenceEquals(f.NoteTone, Gold) ? AuLightC : ReferenceEquals(f.NoteTone, Mint) ? MintC : Color.FromRgb(0xF2, 0xF0, 0xEA);
        var t = T(f.Note, 12, Frozen(tone), FontWeights.SemiBold); t.TextTrimming = TextTrimming.CharacterEllipsis; t.MaxWidth = 380;
        sp.Children.Add(t);
        _note.Children.Add(sp);
        _note.Visibility = Visibility.Visible;
    }

    /// <summary>The timer chips' room at the line's end, 0 with none.</summary>
    double LineTimerW() => _timers.Children.Count > 0 && !KinahHidden ? 12 + Mod.W(_timers) : 0;

    /// <summary>The timers' room the line's strip was fitted to (-1: no strip time); chips that change it draw the line again.</summary>
    double _stripTimerW = -1;

    /// <summary>The timer chips sit over the Buddy, to leave its line's room to the pickup strip.</summary>
    bool _timerUp;

    protected override void KinahShown(bool shown)
    {
        _line.Visibility = shown ? Visibility.Visible : Visibility.Collapsed;
        if (!shown) _note.Visibility = Visibility.Collapsed;
        _fight.Margin = new Thickness(0, 0, 0, shown ? 8 : 0);
        PlaceTimers();
    }

    // ================================================================ the timers

    protected override void DrawTimers(IReadOnlyList<TimerBoard.Item> due, DateTime now)
    {
        _timers.Children.Clear();
        _firstChip = null; _firstSpark = null;
        foreach (var i in due.Take(2))
        {
            var (text, tone) = TimerWords(i, now);
            bool first = _timers.Children.Count == 0, on = tone == TimerBadge.Tone.On;
            var c = on ? MintC : tone == TimerBadge.Tone.Soon ? AuLightC : Color.FromRgb(0xE6, 0xE4, 0xDE);
            var sp = new StackPanel { Orientation = Orientation.Horizontal };
            if (tone != TimerBadge.Tone.Quiet)
                sp.Children.Add(new System.Windows.Shapes.Ellipse { Width = first ? 6.5 : 5.5, Height = first ? 6.5 : 5.5, Fill = Frozen(c), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) });
            var t = T(text, first ? 12.5 : 11, first ? White : Soft, FontWeights.SemiBold);
            if (on) t.Foreground = Frozen(Mod.Mix(MintC, Colors.White, .45));
            else if (tone == TimerBadge.Tone.Soon) t.Foreground = AuLight;
            sp.Children.Add(t);
            var chip = new Border
            {
                Child = sp, CornerRadius = new CornerRadius(3), Padding = first ? new Thickness(9, 3, 10, 4) : new Thickness(7, 2, 8, 3), VerticalAlignment = VerticalAlignment.Center,
                Background = Mod.Br(Black0, .88), BorderThickness = new Thickness(1),
                BorderBrush = on ? Mod.Br(MintC, .7) : tone == TimerBadge.Tone.Soon ? (Brush)GoldMetal : Mod.Br(AuC, .42),
                Margin = new Thickness(first ? 0 : 5, 0, 0, 0), Tag = first ? "timer-chip" : null,
            };
            if (first)
            {
                // the first chip with its spark (a four-point glint at its corner, hidden at rest; the entry's moment flares it)
                _firstChip = chip;
                var host = new Grid { VerticalAlignment = VerticalAlignment.Center };
                host.Children.Add(chip);
                _firstSpark = new System.Windows.Shapes.Path { Data = SparkGeo, Fill = Frozen(Color.FromRgb(0xFF, 0xF4, 0xD6)), Width = 14, Height = 14, Stretch = Stretch.Fill,
                    HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, -6, -5, 0), Opacity = 0, IsHitTestVisible = false,
                    RenderTransformOrigin = new Point(.5, .5) };
                host.Children.Add(_firstSpark);
                _timers.Children.Add(host);
            }
            else _timers.Children.Add(chip);
        }
        // the line's pickup strip was fitted beside the timer chips: chips that come, go or change their width fit it again
        if (_stripTimerW >= 0 && Figs is { } f && Math.Abs(LineTimerW() - _stripTimerW) > .5) DrawLine(f);
        PlaceTimers();
    }

    void PlaceTimers()
    {
        bool inside = KinahHidden && _fight.Visibility == Visibility.Visible, up = !inside && (_timerUp || _line.Visibility != Visibility.Visible);
        if (_timersInLine.Child == _timers) _timersInLine.Child = null;
        if (_timersUp.Child == _timers) _timersUp.Child = null;
        if (_timersInFight.Child == _timers) _timersInFight.Child = null;
        if (inside) _timersInFight.Child = _timers;
        else if (up) _timersUp.Child = _timers;
        else if (_timersInLine.Parent != null) _timersInLine.Child = _timers;
        bool any = _timers.Children.Count > 0;
        _timersInFight.Visibility = inside && any ? Visibility.Visible : Visibility.Collapsed;
        _timersUp.Visibility = up && any ? Visibility.Visible : Visibility.Collapsed;
        _timersInLine.Visibility = !inside && !up && any ? Visibility.Visible : Visibility.Collapsed;
    }

    // ================================================================ the meter

    protected override void DrawFight(Encounter? e, DateTime now)
    {
        _fightBody.Children.Clear();
        if (e == null) { _fight.Visibility = Visibility.Collapsed; _fightFx.Children.Clear(); _confetti.Children.Clear(); _pyro.Children.Clear(); _plateUp = false; _leadFight = ""; _leadId = 0; PlaceTimers(); return; }
        double w = FightWidth - 8;
        _fight.Width = w;
        _beams.Fit(w);
        var before = Meter?.Members.ToDictionary(m => m.Id, m => m.Rank) ?? new Dictionary<long, int>();
        double inner = w - 2 * Pad;
        _fightBody.Children.Add(_timersInFight);
        // the head: the boss in tall condensed type (as the game names it), the outcome chip and the clock at the right
        var head = new Grid();
        head.ColumnDefinitions.Add(new ColumnDefinition()); head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var title = T(e.Title, 18, White, FontWeights.SemiBold, cond: true); title.TextTrimming = TextTrimming.CharacterEllipsis;
        head.Children.Add(title);
        var right = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(8, 0, 0, 0) };
        if (e.Waiting) right.Children.Add(Chip(Caps(L.T("READY")), AuLight, size: 11));
        else
        {
            var outcome = OutcomeText(e);
            if (outcome.Length > 0)
            {
                bool wipe = e.Outcome == "wipe";
                var oc = wipe ? Chip(Caps(outcome), Frozen(Mod.Mix(CoralC, Colors.White, .3)), CoralC, size: 11) : Chip(Caps(outcome), AuLight, size: 11);
                oc.Margin = new Thickness(0, 1, 8, 0); oc.Tag = "outcome";
                right.Children.Add(oc);
            }
            var clock = T(Clock(e, now), 20, White, FontWeights.SemiBold); clock.Tag = "fight-clock";
            right.Children.Add(clock);
        }
        Grid.SetColumn(right, 1); head.Children.Add(right);
        _head = head;
        if (_plateUp) head.Visibility = Visibility.Hidden;   // the end's plate is over it
        _fightBody.Children.Add(head);
        _fightBody.Children.Add(GoldRule(new Thickness(-2, 5, -2, 0)));
        if (e.Waiting)
        {
            var wt = T(e.ReadyText, 13, Soft, FontWeights.Normal); wt.Margin = new Thickness(0, 7, 0, 1); wt.TextWrapping = TextWrapping.Wrap;
            _fightBody.Children.Add(wt);
            if (PartyBeforeHit(e)) { _fightBody.Children.Add(new Border { Height = 7 }); PutRows(_fightBody, e, now, inner); }
        }
        else
        {
            if (HpOf(e) is { } hp)
            {
                // the boss's HP and its words on one line: a slim crimson bar with ticks at the thirds, "47%  ·  1.84M left"
                var hpLine = new Grid { Margin = new Thickness(0, 7, 0, 0) };
                hpLine.ColumnDefinitions.Add(new ColumnDefinition()); hpLine.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                var track = new Grid { Height = 6, VerticalAlignment = VerticalAlignment.Center };
                track.Children.Add(new TacSketch((dc, s) =>
                {
                    var r = new Rect(0, 0, s.Width, s.Height);
                    var shape = Mod.Pill(r, 1.5);
                    dc.DrawGeometry(Frozen(Color.FromRgb(0x24, 0x22, 0x22)), Mod.P(Mod.Br(AuC, .22), .8), shape);
                    dc.PushClip(shape);
                    double x1 = s.Width * Math.Clamp(hp, 0, 1);
                    if (x1 > .5)
                    {
                        dc.DrawRectangle(Mod.Grad(false, (HpAC, 0), (HpBC, 1)), null, new Rect(0, 0, x1, s.Height));
                        dc.DrawRectangle(Mod.Br(Colors.White, .32), null, new Rect(0, 0, x1, 1.2));
                    }
                    for (int k = 1; k <= 2; k++) { double x = Math.Round(s.Width * k / 3); dc.DrawRectangle(Mod.Br(Colors.Black, .7), null, new Rect(x - 1, 0, 2, s.Height)); }
                    dc.Pop();
                }));
                HpGhost(track, e, hp, now, 1.5);
                hpLine.Children.Add(track);
                var words = T("", 12, Soft, FontWeights.Normal);
                words.Margin = new Thickness(10, 0, 0, 0);
                words.Inlines.Add(new System.Windows.Documents.Run($"{Math.Round(hp * 100):0}%") { FontWeight = FontWeights.SemiBold, Foreground = hp < .34 ? Frozen(HpBC) : White });
                var left = HpLeftText(e);
                if (left.Length > 0) words.Inlines.Add(new System.Windows.Documents.Run("  ·  " + left));
                words.Tag = "hp-text";
                Grid.SetColumn(words, 1); hpLine.Children.Add(words);
                _fightBody.Children.Add(hpLine);
                _fightBody.Children.Add(new Border { Height = 12 });
            }
            else _fightBody.Children.Add(new Border { Height = 12 });
            PutRows(_fightBody, e, now, inner);
            if (e.HasDamage)
            {
                _fightBody.Children.Add(GoldRule(new Thickness(0, 6, 0, 5), .4));
                _fightBody.Children.Add(Foot(e, now));
            }
        }
        _fight.Visibility = Visibility.Visible;
        PlaceTimers();
        // the lead changing hands while the fight runs: the trophy rises onto the new leader and a glint sweeps the bars
        var lead = Meter?.Members.FirstOrDefault(m => m.Rank == 1 && !m.CpOnly && !m.Pvp);
        long id = lead?.Id ?? 0;
        if (e.Id == _leadFight && id != 0 && _leadId != 0 && id != _leadId && e.Live && !e.Waiting && Moves) LeadChanged(id, before.TryGetValue(id, out var was) ? was : Meter?.Members.Count ?? 1);
        _leadFight = e.Id; _leadId = id;
    }

    /// <summary>The rows shown now, top first, as the look built them.</summary>
    IEnumerable<FinalsRow> ShownRows() => Meter is { } m ? m.Members.Select(x => m.RowOf(x.Id)).OfType<FinalsRow>() : Enumerable.Empty<FinalsRow>();

    /// <summary>A band of gold light sweeping the bars from left to right, each row a beat after the one above: one course.</summary>
    void Sweep(double ms, double delay = 0)
    {
        var rows = ShownRows().ToList();
        if (rows.Count == 0) return;
        Motion.Play(_sweepHost, ms, t =>
        {
            for (int i = 0; i < rows.Count; i++)
            {
                double a = delay + i * .07, b = a + .55;
                rows[i].Glint(t <= a || t >= b ? -1 : Seg(t, a, b));
            }
        }, () => { foreach (var r in rows) r.Glint(-1); });
    }

    /// <summary>
    /// The lead changing hands: the trophy leaves its stand, picks up the new leader where they climbed from and rides up with
    /// them to the top on a streak of gold, then lands with a ring of gold and its gleam; the stage lights swing and a glint
    /// sweeps the bars. Transforms and opacity only.
    /// </summary>
    void LeadChanged(long id, int fromRank)
    {
        Sweep(700);
        _beams.Play(900, 11);
        if (Meter is not { } meter || meter.RowOf(id) is not FinalsRow row) return;
        Point m0;
        try { m0 = meter.TranslatePoint(new Point(0, 0), _fight); } catch (InvalidOperationException) { row.Crown(900); return; }
        double cw = 14 * FinalsRow.CupScale, ch = 16 * FinalsRow.CupScale;
        double x = m0.X + FinalsRow.CupX - cw / 2, yTop = m0.Y + FinalsRow.CupTop;
        int from = Math.Clamp(fromRank - 1, 0, Math.Max(0, meter.Members.Count - 1));
        double yFrom = m0.Y + from * FinalsRow.Pitch + FinalsRow.CupTop;
        if (from == 0) { row.Crown(900); return; }
        row.CrownAway();
        bool landed = false;
        _flyAt.X = x; _flyAt.Y = yFrom;
        _streakAt.X = x + cw / 2 - 3.5;
        _ringAt.X = x + cw / 2 - 18; _ringAt.Y = yTop + ch * .55 - 18;
        Motion.Play(_fly, 950, t =>
        {
            double f = Seg(t, 0, .5), k = Out(f);
            double y = yFrom + (yTop - yFrom) * k - 8 * Math.Sin(Math.PI * f);
            _flyAt.Y = y;
            _flyGrow.ScaleX = _flyGrow.ScaleY = 1 + .3 * Math.Sin(Math.PI * f);
            _flyer.Opacity = t < .05 ? t / .05 : t < .5 ? 1 : 0;
            // the streak: from the cup back down the way it came, shrinking away once it lands
            double len = Math.Max(0, yFrom - y) * (1 - Seg(t, .45, .75));
            _streakAt.Y = y + ch * .45; _streakLen.ScaleY = Math.Max(.01, len);
            _streak.Opacity = len > 1 ? .95 : 0;
            double r = Seg(t, .5, .92);
            _ringGrow.ScaleX = _ringGrow.ScaleY = .4 + 1.8 * Out(r);
            _ring.Opacity = r is > 0 and < 1 ? 1 - r : 0;
            if (!landed && t >= .5) { landed = true; row.Crown(650); }
        }, () =>
        {
            _flyer.Opacity = 0; _streak.Opacity = 0; _ring.Opacity = 0;
            if (!landed) row.Crown(1);
        });
    }

    // ================================================================ the moments

    /// <summary>The four-point glint (a trophy's shine), drawn once, scaled and turned by the moments.</summary>
    static readonly Geometry SparkGeo = Geometry.Parse("M 7,0 C 7.6,5 9,6.4 14,7 C 9,7.6 7.6,9 7,14 C 6.4,9 5,7.6 0,7 C 5,6.4 6.4,5 7,0 Z");
    System.Windows.Shapes.Path? _firstSpark;
    FrameworkElement? _rareTileEl, _stripEl, _rareRow;

    /// <summary>
    /// A pickup: a band of gold light sweeps the Buddy's plate and the worth comes in on a small black-and-gold ticket at the
    /// plate's right end, over the strip (never over the hour), held a beat and gone.
    /// </summary>
    protected override void Pickup(Pick p, bool held)
    {
        if (KinahHidden || _line.Visibility != Visibility.Visible) return;
        _lineGlint.Tint(AuPaleC, .32);
        Motion.Play(_lineGlint, 1100, t => _lineGlint.At(t), () => _lineGlint.At(-1));
        var worth = p.Worth.Length > 0 ? "+" + p.Worth.TrimStart('+') : p.Count;
        // one line on a ticket hung on the plate's top rim at its right end, over the lamps: the strip under it stays in view
        var line = T(worth, 13.5, p.Worth.Length > 0 ? GoldInk : White, FontWeights.SemiBold, cond: true);
        line.Inlines.Add(new System.Windows.Documents.Run("  " + (p.Name.Length > 13 ? p.Name[..12].TrimEnd() + "…" : p.Name)) { FontSize = 10.5, Foreground = Soft });
        line.TextTrimming = TextTrimming.CharacterEllipsis;
        var ticket = new Border
        {
            Child = line, Background = Mod.Br(Black0, .96), BorderBrush = GoldMetal, BorderThickness = new Thickness(1.2), CornerRadius = new CornerRadius(3),
            Padding = new Thickness(7, 0, 8, 1), MaxWidth = 150, IsHitTestVisible = false,
        };
        var anchor = new Canvas { HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top, IsHitTestVisible = false };
        ticket.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        Canvas.SetLeft(ticket, -ticket.DesiredSize.Width - 14); Canvas.SetTop(ticket, -5);
        var sh = new TranslateTransform();
        var sc = new ScaleTransform(1, 1);
        ticket.RenderTransformOrigin = new Point(1, 0);
        ticket.RenderTransform = new TransformGroup { Children = { sc, sh } };
        anchor.Children.Add(ticket);
        _line.Children.Add(anchor);
        Motion.Play(ticket, 1800, t =>
        {
            // slid out from the plate's right end, a touch big and settling, held, then tucked back in
            double k = Seg(t, .1, .28), o = Seg(t, .82, 1);
            sh.X = 16 * (1 - Out(k)) + 16 * o * o;
            sc.ScaleX = sc.ScaleY = EsportsFx.Punch(k, .75, 2.4);
            ticket.Opacity = t < .82 ? Out(k) : 1 - o;
        }, () => _line.Children.Remove(anchor));
    }

    /// <summary>
    /// A rare drop, the big one: the tile slams in from large, a burst of sparks in the grade's colour flies off it, a ring of
    /// that colour spreads from it and a light of that colour crosses the plate.
    /// </summary>
    protected override void RareDrop(Pick p, bool held)
    {
        if (_rareRing is not { } ring || ring.RenderTransform is not ScaleTransform sc) return;
        var c = ColorOf(p.Color, p.Grade);
        _lineGlint.Tint(Mod.Mix(c, Colors.White, .35), .3);
        var tile = _rareTileEl;
        var tsc = new ScaleTransform(1, 1);
        if (tile != null) { tile.RenderTransformOrigin = new Point(.5, .5); tile.RenderTransform = tsc; }
        var row = _rareRow;
        var rise = new TranslateTransform();
        if (row != null) row.RenderTransform = rise;
        if (ring.Parent is Grid host)
            EsportsFx.Spray(host, new Point(host.Width / 2, 18), 10, 30, -200, 20, new[] { Mod.Mix(c, Colors.White, .25), Colors.White, c }, 1100, fall: 10, delay: .12, sparks: true);
        Motion.Play(ring, 1500, t =>
        {
            double k = Seg(t, .1, .75);
            sc.ScaleX = 1 + .38 * Out(k); sc.ScaleY = 1 + .26 * Out(k);
            ring.Opacity = k is > 0 and < 1 ? 1 - k : 0;
            tsc.ScaleX = tsc.ScaleY = t < .02 ? 1.8 : EsportsFx.Punch(Seg(t, .02, .3), 1.8, 2.6);
            if (tile != null) tile.Opacity = Math.Min(1, Seg(t, .04, .1) * 1.6);
            // the whole line rises into place first (no empty slot showing before it lands)
            if (row != null) { row.Opacity = Seg(t, 0, .06); rise.Y = 6 * (1 - Out(Seg(t, 0, .16))); }
            _lineGlint.At(Seg(t, .1, .9));
        }, () => { ring.Opacity = 0; sc.ScaleX = sc.ScaleY = 1; tsc.ScaleX = tsc.ScaleY = 1; if (tile != null) tile.Opacity = 1; if (row != null) { row.Opacity = 1; rise.Y = 0; } _lineGlint.At(-1); });
    }

    /// <summary>
    /// Your best passed, the trophy shine: the medal's gold arc runs on round to full with a point of light at its head, then
    /// the medal punches out, a burst of gold sparks flies off it, a four-point glint flares on its rim and a light crosses
    /// the plate.
    /// </summary>
    protected override void BestPassed(bool held)
    {
        if (_medal is not { } art) return;
        double was = _share is > 0 and < 1 ? _share : .82;
        bool beat = _beat;
        _lineGlint.Tint(AuPaleC, .32);
        var medal = art.Parent as Grid;
        var msc = new ScaleTransform(1, 1);
        System.Windows.Shapes.Path? glint = null;
        var gsc = new ScaleTransform(0, 0);
        var gturn = new RotateTransform(0);
        if (medal != null)
        {
            medal.RenderTransformOrigin = new Point(.5, .46);
            medal.RenderTransform = msc;
            glint = new System.Windows.Shapes.Path { Data = SparkGeo, Fill = Frozen(Color.FromRgb(0xFF, 0xF8, 0xE4)), Width = 18, Height = 18, Stretch = Stretch.Fill, IsHitTestVisible = false,
                HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, -3, -3, 0), RenderTransformOrigin = new Point(.5, .5) };
            glint.RenderTransform = new TransformGroup { Children = { gsc, gturn } };
            medal.Children.Add(glint);
            // gold flecks falling from the plate's lamps over the medal and the hour
            EsportsFx.Spray(_line, new Point(40, 2), 5, 14, 70, 110, new[] { AuLightC, Colors.White, AuC }, 1500, fall: 46, delay: .24);
            EsportsFx.Spray(_line, new Point(_line.ActualWidth > 0 ? _line.ActualWidth * .42 : 130, 2), 5, 14, 70, 110, new[] { AuPaleC, AuLightC, Colors.White }, 1500, fall: 46, delay: .28);
        }
        Motion.Play(art, 1900, t =>
        {
            // the arc's one small drawing (46 px) is drawn again while it runs round; the light is a band on its transform
            _beat = false; _share = was;
            _complete = Out(Seg(t, 0, .22));
            _medalGlint = t < .26 ? Seg(t, 0, .26) : -1;
            if (t >= .26) { _beat = beat; _complete = 1; }
            if (t <= .28) art.InvalidateVisual();
            msc.ScaleX = msc.ScaleY = t < .22 ? 1 - .05 * Seg(t, .1, .22) : EsportsFx.Punch(Seg(t, .22, .42), 1.3, 2.4);
            double g = Seg(t, .24, .6);
            gsc.ScaleX = gsc.ScaleY = g is > 0 and < 1 ? Math.Sin(Math.PI * g) * 1.1 : 0;
            gturn.Angle = 90 * g;
            _lineGlint.At(Seg(t, .3, .85));
        }, () => { _complete = 0; _medalGlint = -1; _beat = beat; art.InvalidateVisual(); _lineGlint.At(-1); msc.ScaleX = msc.ScaleY = 1; if (glint != null) medal?.Children.Remove(glint); });
    }

    /// <summary>An entry opened: the first timer's chip ducks, pops up a touch big as its edge lights mint, a gold glint flares at its corner, and it settles.</summary>
    protected override void EntryOpened(TimerBoard.Item i, bool held)
    {
        if (_firstChip is not { } chip) return;
        var edge = chip.BorderBrush;
        var sc = new ScaleTransform(1, 1);
        chip.RenderTransformOrigin = new Point(0, .5);
        chip.RenderTransform = sc;
        var spark = _firstSpark;
        var ssc = new ScaleTransform(0, 0);
        var sturn = new RotateTransform(0);
        if (spark != null) spark.RenderTransform = new TransformGroup { Children = { ssc, sturn } };
        var lit = Mod.Br(MintC, 1);
        Motion.Play(chip, 1200, t =>
        {
            sc.ScaleX = sc.ScaleY = t < .1 ? 1 - .06 * Out(Seg(t, 0, .1)) : t < .24 ? .94 + .14 * Out(Seg(t, .1, .24)) : EsportsFx.Punch(Seg(t, .24, .55), 1.08, 2.2);
            chip.BorderBrush = t is > .12 and < .8 ? lit : edge;
            double g = Seg(t, .2, .62);
            ssc.ScaleX = ssc.ScaleY = g is > 0 and < 1 ? Math.Sin(Math.PI * g) * 1.15 : 0;
            sturn.Angle = 120 * g;
            if (spark != null) spark.Opacity = g is > 0 and < 1 ? 1 : 0;
        }, () => { chip.BorderBrush = edge; sc.ScaleX = sc.ScaleY = 1; if (spark != null) spark.Opacity = 0; });
    }

    /// <summary>A fight started: its plate rises in, the stage lights swing on, a band of gold sweeps the bars, the trophy lands on the leader.</summary>
    protected override void FightStarted(Encounter e, bool held)
    {
        // a new fight under the last one's plate: the plate goes, the head comes back
        if (_plateUp) { Motion.Halt(_fightFx); _fightFx.Children.Clear(); _plateUp = false; if (_head != null) _head.Visibility = Visibility.Visible; }
        Motion.Play(_fight, 420, t => { double k = Out(t); _settle.Y = 6 * (1 - k); _fight.Opacity = .2 + .8 * k; }, () => { _settle.Y = 0; _fight.Opacity = 1; });
        _beams.Play(1200, 16);
        Sweep(1000, .12);
        if (ShownRows().FirstOrDefault(r => r.Leads) is { } lead) lead.Crown(1100);
    }

    /// <summary>
    /// A fight ended. A kill or a clear is the grand final's last second: gold pyro bursts from both top corners, the stage
    /// lights swing wide, the CHAMPION plate drops over the head with the top damage dealer's name, their DPS and the time,
    /// a confetti cannon fires from both bottom corners and a band of gold sweeps the bars. A wipe: a plate in coral over the
    /// head saying so, nothing more. All of it one course each, transforms and opacity only, clipped to the panel.
    /// </summary>
    protected override void FightEnded(Encounter e, bool held)
    {
        if (e.Outcome is not ("kill" or "wipe" or "cleared")) return;
        bool wipe = e.Outcome == "wipe";
        double headY = _fightBody.Margin.Top, headH = 24;
        if (_head is { } hd && hd.ActualHeight > 0 && hd.IsDescendantOf(_fight)) { headY = hd.TranslatePoint(new Point(0, 0), _fight).Y; headH = hd.ActualHeight; }
        var champ = wipe ? null : Meter?.Members.FirstOrDefault(m => m.TopDps && !m.Pvp) ?? Meter?.Members.FirstOrDefault(m => m.Rank == 1 && !m.Pvp);
        var plate = new Grid { VerticalAlignment = VerticalAlignment.Top };
        plate.Children.Add(new TacSketch((dc, s) =>
        {
            if (s.Width < 6 || s.Height < 6) return;
            dc.DrawGeometry(Frozen(Black0), null, Mod.Pill(new Rect(.75, .75, s.Width - 1.5, s.Height - 1.5), 3));   // nothing of the head shows through
            Plate(dc, s, 3, 1, shadow: false, rim: wipe ? 0 : 1);
            if (champ != null) Sunburst(dc, s);
            if (wipe) dc.DrawGeometry(null, Mod.P(Mod.Br(CoralC, .75), 1.4), Mod.Pill(new Rect(.75, .75, s.Width - 1.5, s.Height - 1.5), 3));
        }));
        var clock = Clock(e.Duration(e.End ?? DateTime.UtcNow));
        Band? light = null;
        FrameworkElement? champLines = null;
        if (champ != null)
        {
            // CHAMPION between two trophies; under it the champion, their DPS, how the fight ended and its time
            var lines = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 8, 1) };
            var top = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };
            var word = T(Caps(L.T("CHAMPION")), 25, GoldInk, FontWeights.SemiBold, cond: true);
            word.Margin = new Thickness(7, -2, 7, -3);
            word.Effect = new DropShadowEffect { Color = AuC, ShadowDepth = 0, BlurRadius = 8, Opacity = .55 };
            top.Children.Add(new Trophy(1.25) { VerticalAlignment = VerticalAlignment.Center });
            top.Children.Add(word);
            top.Children.Add(new Trophy(1.25) { VerticalAlignment = VerticalAlignment.Center });
            lines.Children.Add(top);
            var sub = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };
            var nm = T(champ.Name, 13.5, White, FontWeights.SemiBold); nm.TextTrimming = TextTrimming.CharacterEllipsis; nm.MaxWidth = Math.Max(80, FightWidth - 8 - 2 * Pad - 190);
            sub.Children.Add(nm);
            var rest = T("", 13, Soft, FontWeights.Normal);
            if (!champ.CpOnly && champ.Figure.Length > 0)
            {
                rest.Inlines.Add(new System.Windows.Documents.Run("   " + champ.Figure) { Foreground = AuPale, FontWeight = FontWeights.SemiBold });
                rest.Inlines.Add(new System.Windows.Documents.Run(" " + L.T("DPS")) { FontSize = 10.5, FontStretch = FontStretches.Condensed, Foreground = AuSoft });
            }
            rest.Inlines.Add(new System.Windows.Documents.Run("   ·   ") { Foreground = Au });
            rest.Inlines.Add(new System.Windows.Documents.Run(Caps(OutcomeText(e)) + " ") { FontSize = 11, FontStretch = FontStretches.Condensed, Foreground = AuSoft });
            rest.Inlines.Add(new System.Windows.Documents.Run(clock) { Foreground = White, FontWeight = FontWeights.SemiBold });
            sub.Children.Add(rest);
            lines.Children.Add(sub);
            light = new Band(AuPaleC, .4, 54) { Margin = new Thickness(2) };
            plate.Children.Add(light);
            plate.Children.Add(lines);
            champLines = lines;
            plate.Height = 56;
        }
        else
        {
            var words = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            words.Children.Add(T(Caps(OutcomeText(e)), 19, wipe ? Frozen(Mod.Mix(CoralC, Colors.White, .25)) : GoldInk, FontWeights.SemiBold, cond: true));
            var ck = T("   " + clock, 16, White, FontWeights.SemiBold); ck.Margin = new Thickness(0, 1, 0, 0);
            words.Children.Add(ck);
            plate.Children.Add(words);
            plate.Height = headH + 8;
        }
        var flash = new Border { Background = Mod.Br(AuPaleC, .9), CornerRadius = new CornerRadius(3), Opacity = 0, IsHitTestVisible = false };
        if (!wipe) plate.Children.Add(flash);
        var sc = new ScaleTransform(1, 1);
        var drop = new TranslateTransform();
        plate.RenderTransformOrigin = new Point(.5, .5); plate.RenderTransform = new TransformGroup { Children = { sc, drop } };
        _fightFx.Children.Clear(); _fightFx.Children.Add(plate);
        _plateUp = true;
        if (_head != null) _head.Visibility = Visibility.Hidden;
        // seated over the head as laid out now, once (nothing is laid out again while it plays)
        // the champion's plate drops over the whole stage head (the rig too); a wipe's over the head only
        plate.Margin = new Thickness(Pad - 4, champ != null ? 4 : Math.Max(3, headY - 4), Pad - 4, 0);
        double rowsTop = headY + headH + 17;
        if (Meter is { } mt && mt.ActualHeight > 0 && mt.IsDescendantOf(_fight)) rowsTop = mt.TranslatePoint(new Point(0, 0), _fight).Y;
        // never over the leader's trophy: the plate ends just over it
        if (champ != null) plate.Height = Math.Clamp(rowsTop + FinalsRow.CupTop - 3 - plate.Margin.Top, 42, 60);
        double plateFoot = plate.Margin.Top + plate.Height;
        // a wipe: the house lights go down. The stage dims under a dark wash, the lights sag, and the plate falls in heavy and
        // bounces once on the head; no gold, no flash, no confetti (the champion's moment is the opposite)
        Border? dark = null;
        var wrows = wipe ? ShownRows().ToList() : new List<FinalsRow>();
        if (wipe)
        {
            dark = new Border { Background = Mod.Br(Black0, .22), BorderBrush = Mod.Br(CoralC, .8), BorderThickness = new Thickness(1.5), CornerRadius = new CornerRadius(Radius), IsHitTestVisible = false, Opacity = 0 };
            _fight.Children.Insert(_fight.Children.IndexOf(_fightFx), dark);
            _lineGlint.Tint(CoralC, .22);
        }
        Motion.Play(_fightFx, wipe ? 2400 : 3400, t =>
        {
            double i = Seg(t, 0, wipe ? .14 : .1), o = Seg(t, wipe ? .82 : .86, 1);
            plate.Opacity = Out(i) * (1 - o);
            if (wipe)
            {
                double fl = Seg(t, 0, .13);
                drop.Y = t < .13 ? -12 * (1 - fl * fl) : -5 * Math.Abs(Math.Sin(Math.PI * Seg(t, .13, .25))) * (1 - Seg(t, .13, .25));
                sc.ScaleX = 1 + (t is > .12 and < .17 ? .04 : 0); sc.ScaleY = 1 - (t is > .12 and < .17 ? .08 : 0);
                if (dark != null) dark.Opacity = t < .1 ? Seg(t, 0, .1) : t < .2 ? 1 : t < .26 ? 1 - .5 * Seg(t, .2, .23) + .5 * Seg(t, .23, .26) : 1 - Out(Seg(t, .3, .45));
                // the lights flicker off (on, half, gone) and come back slowly once the plate has landed
                _beams.Opacity = t < .03 ? 1 : t < .06 ? .3 : t < .09 ? .85 : t < .12 ? .25 : t < .5 ? .08 : .08 + .92 * Out(Seg(t, .5, .85));
                for (int r = 0; r < wrows.Count; r++) wrows[r].Root.Opacity = t < .14 ? 1 - .3 * Seg(t, .06, .14) : .7 + .3 * Out(Seg(t, .18 + (wrows.Count - 1 - r) * .03, .32 + (wrows.Count - 1 - r) * .03));
                _settle.Y = EsportsFx.Shake(t, .13, .3, 2.5, 2);
                return;
            }
            // the plate slams down from above, overshooting, a white-gold flash on impact, then a light runs across it
            drop.Y = -16 * (1 - Math.Min(1.06, Back(i, 2.4))) - 6 * o;
            sc.ScaleX = sc.ScaleY = 1.14 - .14 * Out(i);
            double f = Seg(t, .07, .2);
            flash.Opacity = f is > 0 and < 1 ? .85 * (1 - f) : 0;
            light?.At(Seg(t, .14, .42));
        }, () =>
        {
            _fightFx.Children.Clear(); _plateUp = false; if (_head != null) _head.Visibility = Visibility.Visible;
            if (dark != null) { _fight.Children.Remove(dark); _beams.Opacity = 1; _settle.Y = 0; foreach (var r in wrows) r.Root.Opacity = 1; }
        });
        if (wipe) return;
        // the panel's size as laid out (or, not laid out yet in a render's held frame, as it measures)
        double fw = _fight.ActualWidth > 0 ? _fight.ActualWidth : _fight.Width, fh = _fight.ActualHeight;
        if (fh <= 0 || double.IsNaN(fw)) { _fight.Measure(new Size(double.IsNaN(fw) ? double.PositiveInfinity : fw, double.PositiveInfinity)); fh = _fight.DesiredSize.Height - _fight.Margin.Bottom; if (double.IsNaN(fw)) fw = _fight.DesiredSize.Width; }
        // the pyro and the confetti keep to the banner band over the rows (the stage's floor), out of the plate's words: no
        // figure, name or mark of a row is ever covered
        var band = new RectangleGeometry(new Rect(0, 0, fw, Math.Max(20, rowsTop - 1)));
        Geometry open = band;
        if (champLines != null)
        {
            champLines.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            var ds = champLines.DesiredSize;
            var words = new Rect(fw / 2 - ds.Width / 2 - 4, plate.Margin.Top + (plate.Height - ds.Height) / 2 - 4, ds.Width + 8, ds.Height + 8);
            open = new CombinedGeometry(GeometryCombineMode.Exclude, band, new RectangleGeometry(words));
        }
        open.Freeze();
        _pyro.Clip = open; _confetti.Clip = open;
        Pyro(_pyro, fw, fh);
        Confetti(_confetti, fw, rowsTop);
        _beams.Play(1800, 24);
        Sweep(1100, .12);
    }

    /// <summary>The champion's plate's backdrop: rays of gold fanning out from behind CHAMPION, fading toward the plate's edges (drawn once).</summary>
    static void Sunburst(DrawingContext dc, Size s)
    {
        var c = new Point(s.Width / 2, s.Height * .38);
        var rays = new StreamGeometry();
        using (var g = rays.Open())
        {
            const int n = 28;
            double far = s.Width;
            for (int i = 0; i < n; i += 2)
            {
                double a0 = 2 * Math.PI * i / n, a1 = 2 * Math.PI * (i + 1) / n;
                g.BeginFigure(c, true, true);
                g.LineTo(new Point(c.X + far * Math.Cos(a0), c.Y + far * Math.Sin(a0)), true, false);
                g.LineTo(new Point(c.X + far * Math.Cos(a1), c.Y + far * Math.Sin(a1)), true, false);
            }
        }
        rays.Freeze();
        // the light: gold at the centre, gone by the plate's ends (placed on the plate, not on the rays' far ends)
        var fill = new RadialGradientBrush { MappingMode = BrushMappingMode.Absolute, Center = c, GradientOrigin = c, RadiusX = s.Width * .55, RadiusY = s.Height * 1.1 };
        fill.GradientStops.Add(new GradientStop(Mod.A(AuC, .38), 0));
        fill.GradientStops.Add(new GradientStop(Mod.A(AuC, .16), .6));
        fill.GradientStops.Add(new GradientStop(Mod.A(AuC, 0), 1));
        fill.Freeze();
        dc.PushClip(Mod.Pill(new Rect(1.5, 1.5, s.Width - 3, s.Height - 3), 3));
        dc.DrawGeometry(fill, null, rays);
        dc.Pop();
    }
}
