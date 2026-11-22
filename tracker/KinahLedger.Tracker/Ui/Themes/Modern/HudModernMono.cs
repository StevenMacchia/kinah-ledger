using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using KinahLedger.Parser;
using KinahLedger.Session;
using static KinahLedger.Ui.HudKit;

namespace KinahLedger.Ui;

/// <summary>
/// Mono (the Modern theme; Steven, 2026-11-17, picked from the HTML mock-ups): Swiss minimal. A near-black panel with a
/// hairline edge, off-white Inter with tabular figures, one orange accent (you, the boss's HP, the ring); flat bars on an exact
/// 4 px grid, hairline rules, 2 px corners, nothing extra. The Buddy is one panel: the ring of your best, the hour big with the best drop
/// and the last pickups as tiles beside it, the session under it, the next timer in an outlined box at its right. A fight is the same panel: the
/// boss and its clock, a 4 px HP bar cut at its thirds, the rows each over a flat 4 px bar, the foot after a hairline.
/// The moments stay flat: a pickup runs an accent line along the panel's foot; a rare drop draws a rule in its grade's colour;
/// your best passed fills the ring; an entry opening inverts the timer's box once; a fight wipes in from the top and its end
/// lays a band with the outcome across the rows.
/// </summary>
sealed partial class HudModernMono : ModernLook
{
    /// <summary>The note's pop-out moves this (HudStackLook.PopNote).</summary>
    protected override UIElement? NoteHost => _noteHost;

    static readonly Color BgC = Color.FromRgb(0x0B, 0x0C, 0x0E), EdgeC = Color.FromRgb(0x23, 0x24, 0x28), PaperC = Color.FromRgb(0xF2, 0xF1, 0xEC),
        GreyC = Color.FromRgb(0x8B, 0x8A, 0x85), RuleC = Color.FromRgb(0x26, 0x27, 0x2B), TrackC = Color.FromRgb(0x1C, 0x1D, 0x21),
        AccentC = Color.FromRgb(0xFF, 0x4D, 0x2E), GreenC = Color.FromRgb(0x5D, 0xF2, 0xA6), RedC = Color.FromRgb(0xFF, 0x6B, 0x6B);
    static readonly SolidColorBrush Paper = Frozen(PaperC), Grey = Frozen(GreyC), Accent = Frozen(AccentC), Green = Frozen(GreenC), Red = Frozen(RedC),
        Edge = Frozen(EdgeC), RuleB = Frozen(RuleC), TrackB = Frozen(TrackC), Dark = Frozen(BgC), Soft = Frozen(Mod.A(PaperC, .8));
    const double Radius = 4, Corner2 = 2;
    /// <summary>The pickup strip in Mono: 26 px tiles beside the hour, a faint off-white rule, the orange accent's star.</summary>
    static readonly PickStrip.Look Strip = new(26, 4, true, Frozen(Mod.A(PaperC, .22)), Accent);

    // every part inside the window's edge: the moments draw inside the panels (the spill check stays empty)
    readonly Grid _root = new() { Margin = new Thickness(4), HorizontalAlignment = HorizontalAlignment.Left };
    readonly StackPanel _body = new() { HorizontalAlignment = HorizontalAlignment.Left };
    double _opacity = 0.9;

    // the timers: in the Buddy's panel at its right, inside the fight's panel in a dungeon, or a small panel of their own
    readonly StackPanel _timers = new() { VerticalAlignment = VerticalAlignment.Center };
    readonly Border _timersBuddy = new() { Margin = new Thickness(14, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
    readonly Border _timersIn = new() { Margin = new Thickness(0, 0, 0, 10), HorizontalAlignment = HorizontalAlignment.Left };
    readonly Border _timersTop;
    readonly Border _timersTopHost = new() { HorizontalAlignment = HorizontalAlignment.Left };
    Border? _chip;
    TextBlock? _chipText;
    Border? _chipDot;
    TimerBadge.Tone _chipTone;

    // the Buddy
    readonly Border _line;
    readonly Grid _lineGrid = new();
    readonly StackPanel _lineBody = new();
    readonly Grid _lineFx = new() { IsHitTestVisible = false, ClipToBounds = true };
    TacSketch? _ringArt;
    double _share = -1, _fill = -1;
    bool _beat;
    FrameworkElement? _rareRow, _rareLine, _rareTileEl, _ringEl;
    TextBlock? _rateText;
    TacSketch? _rareRule;
    double _rareDraw = 1;
    Color _rareC;
    readonly Border _noteHost;
    readonly TextBlock _note = Mod.Text("", 11.5, Paper, FontWeights.Medium);
    readonly Border _noteDot = new() { Width = 6, Height = 6, Margin = new Thickness(0, 1, 8, 0), VerticalAlignment = VerticalAlignment.Center };

    // the fight
    readonly Border _fight;
    readonly Grid _fightGrid = new();
    readonly StackPanel _fightBody = new() { Margin = new Thickness(12, 9, 12, 8) };
    readonly Grid _fightFx = new() { IsHitTestVisible = false, ClipToBounds = true };
    Grid? _head;

    public HudModernMono() : base(bare: false)
    {
        // the meter: the look's own rows (Ui/Themes/ThemeRows.cs) on the 4 px grid, a muted "2 MORE" between the party and you
        UseRows(MonoRow.Pitch, m => new MonoRow(), 14, GapLine);
        _line = Panel(_lineGrid);
        _lineGrid.Children.Add(_lineBody); _lineGrid.Children.Add(_lineFx);
        _lineBody.Margin = new Thickness(10, 10, 14, 10);
        _fight = Panel(_fightGrid);
        _fight.Visibility = Visibility.Collapsed; _fight.Margin = new Thickness(0, 0, 0, 6); _fight.HorizontalAlignment = HorizontalAlignment.Left;
        _fightGrid.Children.Add(_fightBody); _fightGrid.Children.Add(_fightFx);
        _timersTop = Panel(_timersTopHost);
        _timersTop.HorizontalAlignment = HorizontalAlignment.Left; _timersTop.Margin = new Thickness(0, 0, 0, 6); _timersTopHost.Padding = new Thickness(8, 7, 8, 7);
        _noteHost = Panel(new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(10, 5, 12, 6), Children = { _noteDot, _note } });
        _noteHost.HorizontalAlignment = HorizontalAlignment.Left; _noteHost.Margin = new Thickness(0, 4, 0, 0); _noteHost.Visibility = Visibility.Collapsed;
        _body.Children.Add(_timersTop); _body.Children.Add(_fight); _body.Children.Add(_line); _body.Children.Add(_noteHost);
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
        var bg = Ground();
        foreach (var p in new[] { _line, _fight, _timersTop, _noteHost }) p.Background = bg;
    }

    /// <summary>The panel's near-black at the overlay's opacity (never so thin the off-white loses its ground).</summary>
    SolidColorBrush Ground() => Frozen(Mod.A(BgC, .72 + .28 * _opacity));

    /// <summary>A panel: near-black, a hairline edge, 4 px corners.</summary>
    Border Panel(UIElement child) => new() { Child = child, Background = Frozen(Mod.A(BgC, .97)), BorderBrush = Edge, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(Radius) };

    protected override string LineSig(Figures f) => base.LineSig(f) + "|" + (Rare(Values)?.Id ?? "") + "|" + (Picks(Values).FirstOrDefault()?.Id ?? "") + "|" + Dim + "|" + PickStrip.Sig(Values, Strip) + "|" + RunBuddy.Sig(Values);

    /// <summary>The reader is paused or the window dims (the "show" value): the Buddy's figures go quiet, the note stays bright.</summary>
    bool Dim => Values.TryGetValue("show", out var s) && s == "dim" || Values.TryGetValue("paused", out var p) && p.Length > 0;

    // ================================================================ the Buddy

    protected override void DrawLine(Figures f)
    {
        _lineBody.Children.Clear();
        _ringArt = null; _rareRow = null; _rareRule = null; _rareLine = null; _rareTileEl = null; _ringEl = null; _rateText = null; _timerUp = false;
        // in a dungeon run: the run's clock, splits and verdict in place of the hour (HudModernMono.Run.cs)
        if (RunNow() is { } rs)
        {
            DrawRun(rs, Dim);
            DrawNote(f);
            PlaceTimers();
            return;
        }
        var row = new Grid();
        for (int i = 0; i < 4; i++) row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });   // ring, hour, strip, timers
        bool dim = Dim;
        var ink = dim ? Grey : Paper;
        if (f.Abyss && f.Counted && f.Icon.Length > 0)
        {
            var m = AbyssMark(f.Icon, 40); m.Margin = new Thickness(0, 0, 12, 0); m.VerticalAlignment = VerticalAlignment.Center;
            row.Children.Add(m);
        }
        else if (HasRing(f))
        {
            _share = f.Share; _beat = f.Beat; _fill = -1;
            var ring = new Grid { Width = 44, Height = 44, Margin = new Thickness(0, 0, 12, 0), VerticalAlignment = VerticalAlignment.Center };
            var art = new TacSketch((dc, s) => Ring(dc, s, dim), 44, 44);
            _ringArt = art; _ringEl = ring;
            ring.Children.Add(art);
            var pc = Tt(f.Beat ? "★" : Pct(f.Share), 14, ink, FontWeights.Bold);
            pc.HorizontalAlignment = HorizontalAlignment.Center;
            ring.Children.Add(pc);
            row.Children.Add(ring);
        }
        else if (HasHour(f))
        {
            // no best here yet: the accent square, as the timer's dot, keeps the panel's left edge
            row.Children.Add(new Border { Width = 8, Height = 8, Background = dim ? Grey : Accent, Margin = new Thickness(4, 0, 12, 0), VerticalAlignment = VerticalAlignment.Center });
        }
        var kv = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        if (!HasHour(f))
        {
            var w = Tt(Waiting(f.Abyss), 13, Grey, FontWeights.Medium);
            w.Margin = new Thickness(2, 6, 0, 6);
            kv.Children.Add(w);
            // in the Abyss, what's already counted ("2 kills · 266.5K Kinah") under it: information, not emptiness (Steven, 2026-11-21)
            if (f.Abyss && f.Stats.Count > 0) { w.Margin = new Thickness(2, 2, 0, 0); var st = Sub(f, false, dim, ink); st.Margin = new Thickness(2, 2, 0, 2); kv.Children.Add(st); }
        }
        else
        {
            var top = new StackPanel { Orientation = Orientation.Horizontal };
            var rate = Tt(f.Rate, f.Warm ? 15 : 24, f.Warm ? Grey : ink, f.Warm ? FontWeights.SemiBold : FontWeights.Bold);
            if (!f.Warm) rate.Margin = new Thickness(0, -2, 0, -1);
            _rateText = rate;
            top.Children.Add(rate);
            if (f.Unit.Length > 0) { var u = Tt(f.Unit, 12, Grey, FontWeights.Medium); u.VerticalAlignment = VerticalAlignment.Bottom; u.Margin = new Thickness(3, 0, 0, 3); top.Children.Add(u); }
            kv.Children.Add(top);
            // the session, then the latest pickup and its worth in green. Farming, the pickups as the strip beside the hour
            // where it fits (the best drop and the last three), the session alone under it; with the timers at the panel's
            // right and no room left beside them, the timers go up to a small panel of their own over the Buddy
            var sub = Sub(f, true, dim, ink);
            FrameworkElement? under = null;
            if (StripTime(f))
            {
                var bare = Sub(f, false, dim, ink);
                double baseW = 2 + 10 + 14 + (row.Children.Count > 0 ? Mod.W((FrameworkElement)row.Children[0]) : 0), timerW = LineTimerW();   // the edges, the body's margins, the ring
                double wordsW = Math.Max(Mod.W(top), Mod.W(bare)), oldW = Math.Max(Mod.W(top), Mod.W(sub));
                FrameworkElement? strip = null;
                foreach (var look in StripFall(Strip))
                {
                    if ((strip = StripIn(StripRoom(FightWidth - 8, baseW + timerW, wordsW, oldW, 14), look)) != null) break;
                    if (timerW > 0 && (strip = StripIn(StripRoom(FightWidth - 8, baseW, wordsW, oldW + timerW, 14), look)) != null) { _timerUp = true; break; }
                }
                if (strip != null)
                {
                    sub = bare;
                    strip.Margin = new Thickness(14, 0, 0, 0);
                    if (dim) strip.Opacity = .5;
                    Grid.SetColumn(strip, 2); row.Children.Add(strip);
                }
                // no room beside the hour: under the session, never dropped (Steven, 2026-11-19)
                else if ((under = StripUnder(Strip, dim, .5)) != null) sub = bare;
            }
            if (sub.Inlines.Count > 0) kv.Children.Add(sub);
            if (under != null) kv.Children.Add(under);
        }
        _stripTimerW = StripTime(f) ? LineTimerW() : -1;
        Grid.SetColumn(kv, 1); row.Children.Add(kv);
        Grid.SetColumn(_timersBuddy, 3);
        if (_timersBuddy.Parent is Panel old) old.Children.Remove(_timersBuddy);
        row.Children.Add(_timersBuddy);
        _lineBody.Children.Add(row);

        // the rare drop the window holds: a rule in its grade's colour, its tile, RARE DROP and its name
        if (!KinahHidden && Rare(Values) is { } r)
        {
            _rareC = ColorOf(r.Color, r.Grade);
            var box = new StackPanel { Margin = new Thickness(0, 10, 0, 0) };
            _rareRule = new TacSketch((dc, s) =>
            {
                dc.DrawRectangle(RuleB, null, new Rect(0, 0, s.Width, 1));
                dc.DrawRectangle(Frozen(_rareC), null, new Rect(0, 0, s.Width * Math.Clamp(_rareDraw, 0, 1), 1));
            }) { Height = 1, Margin = new Thickness(0, 0, 0, 8) };
            box.Children.Add(_rareRule);
            var rr = new StackPanel { Orientation = Orientation.Horizontal };
            var tileEl = FlatTile(r, 26, _rareC);
            _rareTileEl = tileEl; _rareLine = rr;
            rr.Children.Add(tileEl);
            var words = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 0, 0) };
            words.Children.Add(Label(L.T("RARE DROP")));
            var nm = new StackPanel { Orientation = Orientation.Horizontal };
            nm.Children.Add(Tt(r.Name, 13.5, Frozen(_rareC), FontWeights.SemiBold));
            if (r.Count.Length > 0) { var cn = Tt(" " + r.Count, 11.5, Grey, FontWeights.Medium); nm.Children.Add(cn); }
            words.Children.Add(nm);
            rr.Children.Add(words);
            box.Children.Add(rr);
            _rareRow = box;
            _lineBody.Children.Add(box);
        }
        DrawNote(f);
        PlaceTimers();
    }

    /// <summary>The note under the panel: a small square in its tone, the words in off-white.</summary>
    void DrawNote(Figures f)
    {
        _note.Text = f.Note;
        var noteTone = ReferenceEquals(f.NoteTone, Warn) ? Accent : ReferenceEquals(f.NoteTone, Mint) ? Green : ReferenceEquals(f.NoteTone, Gold) ? Paper : Grey;
        _noteDot.Background = noteTone;
        _noteHost.Visibility = f.Note.Length > 0 && !KinahHidden ? Visibility.Visible : Visibility.Collapsed;
        _lineBody.Opacity = 1;
    }

    /// <summary>The line under the hour: the session, the latest pickup and its worth in green (<paramref name="pick"/>); in the Abyss its stats.</summary>
    TextBlock Sub(Figures f, bool pick, bool dim, Brush ink)
    {
        var sub = new TextBlock { FontFamily = Mod.Face, FontSize = 11.5, FontWeight = FontWeights.Medium, Foreground = dim ? Grey : Soft, Margin = new Thickness(0, 2, 0, 0) };
        System.Windows.Documents.Typography.SetNumeralAlignment(sub, FontNumeralAlignment.Tabular);
        void Sep() { if (sub.Inlines.Count > 0) sub.Inlines.Add(new System.Windows.Documents.Run("  ·  ") { Foreground = Grey }); }
        if (f.Total.Length > 0) sub.Inlines.Add(new System.Windows.Documents.Run($"{f.Total} {f.TotalWord}"));
        if (f.Abyss)
            foreach (var (fig, word, tone) in f.Stats)
            {
                Sep();
                sub.Inlines.Add(new System.Windows.Documents.Run(fig) { Foreground = ReferenceEquals(tone, Rec) ? Red : ink, FontWeight = FontWeights.SemiBold });
                sub.Inlines.Add(new System.Windows.Documents.Run(" " + word));
            }
        else if (pick && !KinahHidden && Picks(Values).FirstOrDefault() is { } p)
        {
            Sep();
            sub.Inlines.Add(new System.Windows.Documents.Run(p.Name.Length > 18 ? p.Name[..17].TrimEnd() + "…" : p.Name));
            var worth = p.Worth.Length > 0 ? p.Worth : p.Count;
            if (worth.Length > 0) sub.Inlines.Add(new System.Windows.Documents.Run(" " + (p.Worth.Length > 0 && !worth.StartsWith('+') ? "+" : "") + worth) { Foreground = dim ? Grey : Green, FontWeight = FontWeights.SemiBold });
        }
        return sub;
    }

    /// <summary>The timers' room at the Buddy's right (the next in its box, the rest under it), 0 with none.</summary>
    double LineTimerW() => _timers.Children.Count > 0 && !KinahHidden ? 14 + Mod.W(_timers) : 0;

    /// <summary>The timers' room the line's strip was fitted to (-1: no strip time); timers that change it draw the line again.</summary>
    double _stripTimerW = -1;

    /// <summary>The timers sit in their own small panel over the Buddy, to leave the line's room to the pickup strip.</summary>
    bool _timerUp;

    /// <summary>The ring of your best: a 4 px track, the share as a flat accent arc (butt ends) from the top; full once beaten.</summary>
    void Ring(DrawingContext dc, Size s, bool dim)
    {
        double c = s.Width / 2, r = c - 2;
        var center = new Point(c, c);
        dc.DrawEllipse(null, Mod.P(RuleB, 4), center, r, r);
        double share = _fill >= 0 ? _fill : Math.Clamp(_beat ? 1 : _share, 0, 1);
        var tone = dim ? Grey : Accent;
        if (share >= 0.999) { dc.DrawEllipse(null, Mod.P(tone, 4), center, r, r); return; }
        if (share < 0.004) return;
        double a = 2 * Math.PI * share;
        var fig = new PathFigure { StartPoint = new Point(c, c - r) };
        fig.Segments.Add(new ArcSegment(new Point(c + r * Math.Sin(a), c - r * Math.Cos(a)), new Size(r, r), 0, a > Math.PI, SweepDirection.Clockwise, true));
        var pg = new PathGeometry(); pg.Figures.Add(fig); pg.Freeze();
        dc.DrawGeometry(null, Mod.P(tone, 4), pg);
    }

    /// <summary>An item's tile, flat: its icon in a 2 px-cornered square framed by a hairline in <paramref name="c"/>.</summary>
    static FrameworkElement FlatTile(Pick p, double size, Color c)
    {
        var box = new Border { Width = size, Height = size, CornerRadius = new CornerRadius(Corner2), BorderThickness = new Thickness(1), BorderBrush = Frozen(c), Background = TrackB, VerticalAlignment = VerticalAlignment.Center };
        var slot = IconCache.Get(p.Icon);
        var pic = new Image { Source = slot.Image, Stretch = Stretch.UniformToFill, Margin = new Thickness(1) };
        RenderOptions.SetBitmapScalingMode(pic, BitmapScalingMode.HighQuality);
        if (slot.Image == null && !ReferenceEquals(slot, IconCache.None))
        {
            System.ComponentModel.PropertyChangedEventHandler? on = null;
            on = (_, _) => { if (slot.Image == null) return; pic.Source = slot.Image; slot.PropertyChanged -= on; };
            slot.PropertyChanged += on;
        }
        box.Child = pic;
        return box;
    }

    /// <summary>A small uppercase label in grey.</summary>
    static TextBlock Label(string s) => Mod.Text(s, 9, Grey, FontWeights.SemiBold);

    static FrameworkElement Hairline(Thickness m) => new Border { Height = 1, Background = RuleB, Margin = m, SnapsToDevicePixels = true };

    protected override void KinahShown(bool shown)
    {
        _line.Visibility = shown ? Visibility.Visible : Visibility.Collapsed;
        if (!shown) _noteHost.Visibility = Visibility.Collapsed;
        _fight.Margin = new Thickness(0, 0, 0, shown ? 6 : 0);
        PlaceTimers();
    }

    // ================================================================ the timers

    protected override void DrawTimers(IReadOnlyList<TimerBoard.Item> due, DateTime now)
    {
        _timers.Children.Clear();
        _chip = null; _chipText = null; _chipDot = null;
        var shown = due.Take(KinahHidden ? 2 : HudWindow.TimerLineMax).ToList();
        if (shown.Count > 0)
        {
            // the next: an outlined box, a square dot in its tone (green while the entry is open), the words in off-white
            var (text, tone) = TimerWords(shown[0], now);
            _chipTone = tone;
            var c = tone == TimerBadge.Tone.On ? Green : tone == TimerBadge.Tone.Soon ? Accent : Grey;
            var dot = new Border { Width = 7, Height = 7, Background = c, Margin = new Thickness(0, 1, 7, 0), VerticalAlignment = VerticalAlignment.Center };
            var t = Tt(text, 12, Paper, FontWeights.SemiBold);
            _chip = new Border
            {
                BorderBrush = tone == TimerBadge.Tone.On ? Green : Paper, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(Corner2), Padding = new Thickness(9, 4, 10, 5),
                HorizontalAlignment = HorizontalAlignment.Left, Child = new StackPanel { Orientation = Orientation.Horizontal, Children = { dot, t } },
            };
            _chipText = t; _chipDot = dot;
            _timers.Children.Add(_chip);
            // the rest: one quiet line under it
            if (shown.Count > 1)
            {
                var rest = Tt(string.Join("  ·  ", shown.Skip(1).Select(i => TimerWords(i, now).Text)), 10.5, Grey, FontWeights.Medium);
                rest.Margin = new Thickness(1, 5, 0, 0);
                _timers.Children.Add(rest);
            }
        }
        // the line's pickup strip was fitted beside the timers: timers that come, go or change their width fit it again
        if (_stripTimerW >= 0 && Figs is { } f && Math.Abs(LineTimerW() - _stripTimerW) > .5) DrawLine(f);
        PlaceTimers();
    }

    void PlaceTimers()
    {
        bool fight = _fight.Visibility == Visibility.Visible;
        bool buddy = !KinahHidden && _line.Visibility == Visibility.Visible && !_timerUp;
        // the Buddy holds them while it shows (unless its pickup strip took their room); in a dungeon (no Buddy) the fight's
        // panel; else a small panel of their own (an open-world fight's panel keeps to the fight)
        var host = buddy ? _timersBuddy : fight && !_timerUp ? _timersIn : _timersTopHost;
        foreach (var b in new[] { _timersBuddy, _timersIn, _timersTopHost }) if (b != host && b.Child == _timers) b.Child = null;
        if (host.Child != _timers) host.Child = _timers;
        bool any = _timers.Children.Count > 0;
        _timersBuddy.Visibility = host == _timersBuddy && any ? Visibility.Visible : Visibility.Collapsed;
        _timersIn.Visibility = host == _timersIn && any ? Visibility.Visible : Visibility.Collapsed;
        _timersTop.Visibility = host == _timersTopHost && any ? Visibility.Visible : Visibility.Collapsed;
    }

    // ================================================================ the fight

    protected override void DrawFight(Encounter? e, DateTime now)
    {
        _fightBody.Children.Clear();
        if (e == null) { _fight.Visibility = Visibility.Collapsed; _fightFx.Children.Clear(); PlaceTimers(); return; }
        double w = FightWidth - 8;
        _fight.Width = w;
        double inner = w - 2 - _fightBody.Margin.Left - _fightBody.Margin.Right;
        if (_timersIn.Parent is Panel old) old.Children.Remove(_timersIn);
        _fightBody.Children.Add(_timersIn);

        // Ferocious Horn Nuakum                [KILLED] 12:47
        var head = new Grid();
        _head = head;
        head.ColumnDefinitions.Add(new ColumnDefinition()); head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var title = Tt(e.Title, 15, Paper, FontWeights.SemiBold); title.TextTrimming = TextTrimming.CharacterEllipsis;
        head.Children.Add(title);
        var right = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(10, 0, 0, 0) };
        if (e.Waiting) right.Children.Add(Mark(L.T("READY"), Paper, filled: false));
        else
        {
            var outcome = OutcomeText(e);
            if (outcome.Length > 0)
            {
                var ot = Mark(outcome.ToUpperInvariant(), e.Outcome == "wipe" ? Accent : Paper, filled: true);
                ot.Margin = new Thickness(0, 1, 10, 0);
                right.Children.Add(ot);
            }
            right.Children.Add(Tt(Clock(e, now), 18, Paper, FontWeights.Bold));
        }
        Grid.SetColumn(right, 1); head.Children.Add(right);
        _fightBody.Children.Add(head);

        if (e.Waiting)
        {
            var wt = Tt(e.ReadyText, 12.5, Grey, FontWeights.Medium); wt.Margin = new Thickness(0, 8, 0, 0); wt.TextWrapping = TextWrapping.Wrap;
            _fightBody.Children.Add(wt);
            if (PartyBeforeHit(e)) { _fightBody.Children.Add(Hairline(new Thickness(0, 8, 0, 6))); PutRows(_fightBody, e, now, inner); }
        }
        else
        {
            if (HpOf(e) is { } hp)
            {
                // a 4 px bar in the accent, cut at its thirds by the panel's own near-black; the figure and what's left at its right
                var hpLine = new Grid { Margin = new Thickness(0, 7, 0, 0) };
                hpLine.ColumnDefinitions.Add(new ColumnDefinition()); hpLine.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                var track = new Grid { Height = 4, VerticalAlignment = VerticalAlignment.Center };
                track.Children.Add(new TacSketch((dc, s) =>
                {
                    dc.DrawRectangle(RuleB, null, new Rect(0, 0, s.Width, s.Height));
                    dc.DrawRectangle(Accent, null, new Rect(0, 0, Math.Round(s.Width * Math.Clamp(hp, 0, 1)), s.Height));
                }));
                HpGhost(track, e, hp, now, 0);
                track.Children.Add(new TacSketch((dc, s) =>
                {
                    for (int k = 1; k <= 2; k++) dc.DrawRectangle(Dark, null, new Rect(Math.Round(s.Width * k / 3) - 1, 0, 2, s.Height));
                }));
                hpLine.Children.Add(track);
                var hpl = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(10, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
                hpl.Children.Add(Tt($"{Math.Round(hp * 100):0}%", 10.5, Paper, FontWeights.SemiBold));
                var left = HpLeftText(e);
                if (left.Length > 0) hpl.Children.Add(Tt("  ·  " + left, 10.5, Grey, FontWeights.Medium));
                Grid.SetColumn(hpl, 1); hpLine.Children.Add(hpl);
                _fightBody.Children.Add(hpLine);
                _fightBody.Children.Add(Hairline(new Thickness(0, 7, 0, 6)));
            }
            else _fightBody.Children.Add(Hairline(new Thickness(0, 8, 0, 6)));
            PutRows(_fightBody, e, now, inner);
            if (e.HasDamage)
            {
                _fightBody.Children.Add(Hairline(new Thickness(0, 2, 0, 6)));
                _fightBody.Children.Add(Foot(e, now));
            }
        }
        _fight.Visibility = Visibility.Visible;
        PlaceTimers();
    }

    // ================================================================ the moments
    //
    // Mono's moments are Swiss and flat (2026-11-21, "cinematic ... keeping it light"): hard-edged blocks that wipe in and
    // out on an exponential snap, rules that draw, registration marks at a panel's corners, a stutter in place of a shake.
    // No glow, no blur, no curve. Each part is drawn once and moved by its transforms (Ui/Themes/Modern/ModernFx.cs).

    /// <summary>
    /// A block's visible run along its length, from <paramref name="a"/> to <paramref name="b"/> (0 to 1 of <paramref name="len"/>):
    /// its scale from its start edge and its shift (the block's origin at its left or top).
    /// </summary>
    static void Run(ModFx.Xf x, double a, double b, double len, bool down = false)
    {
        double s = Math.Max(0, b - a);
        if (down) { x.S.ScaleY = s; x.T.Y = a * len; } else { x.S.ScaleX = s; x.T.X = a * len; }
    }

    /// <summary>A block's wipe: in from its start (<paramref name="i0"/>..<paramref name="i1"/>), held, out at its far end (<paramref name="o0"/>..<paramref name="o1"/>).</summary>
    static void Wipe(ModFx.Xf x, double t, double i0, double i1, double o0, double o1, double len, bool down = false) =>
        Run(x, ModFx.Expo(Seg(t, o0, o1)), ModFx.Expo(Seg(t, i0, i1)), len, down);

    /// <summary>Registration marks: an L at each corner of <paramref name="r"/>, 9 px arms, 2 px thick, 3 px inside its edge (each arm grows from the corner).</summary>
    static List<(FrameworkElement El, ModFx.Xf X)> Marks(Canvas layer, Rect r, Brush ink)
    {
        const double arm = 9, th = 2, ins = 3;
        var marks = new List<(FrameworkElement El, ModFx.Xf X)>();
        foreach (var (cx, cy, sxs, sys) in new[] { (r.Left + ins, r.Top + ins, 1, 1), (r.Right - ins, r.Top + ins, -1, 1), (r.Left + ins, r.Bottom - ins, 1, -1), (r.Right - ins, r.Bottom - ins, -1, -1) })
        {
            var h = ModFx.Put(layer, ModFx.Block(arm, th, ink), sxs > 0 ? cx : cx - arm, sys > 0 ? cy : cy - th);
            var v = ModFx.Put(layer, ModFx.Block(th, arm, ink), sxs > 0 ? cx : cx - th, sys > 0 ? cy : cy - arm);
            marks.Add((h, new ModFx.Xf(h, new Point(sxs > 0 ? 0 : 1, .5))));
            marks.Add((v, new ModFx.Xf(v, new Point(.5, sys > 0 ? 0 : 1))));
        }
        return marks;
    }

    /// <summary>A pickup: an accent block wipes over the new slot and off, and an accent rule runs along the Buddy's foot.</summary>
    protected override void Pickup(Pick p, bool held)
    {
        if (KinahHidden || _line.Visibility != Visibility.Visible) return;
        var lb = ModFx.In(_line, _root);
        if (lb.IsEmpty) return;
        var layer = FxLayer("pickup", _root);
        double w = lb.Width - 2;
        var rule = ModFx.Put(layer, ModFx.Block(w, 2, Accent), lb.Left + 1, lb.Bottom - 3);
        var x = new ModFx.Xf(rule, new Point(0, .5));
        // the new slot: a hard accent block wipes over it and off, the tile under it as it goes
        var tile = NewestTile(_lineBody);
        var tb = tile != null ? ModFx.In(tile, _root) : Rect.Empty;
        FrameworkElement? slot = null; ModFx.Xf? sx = null;
        if (!tb.IsEmpty) { slot = ModFx.Put(layer, ModFx.Block(tb.Width + 2, tb.Height + 2, Accent, Corner2), tb.Left - 1, tb.Top - 1); sx = new ModFx.Xf(slot, new Point(0, .5)); }
        Motion.Play(layer, 800, t =>
        {
            Wipe(x, t, 0, .5, .38, .92, w);
            if (sx != null) { Wipe(sx, t, 0, .1, .14, .24, tb.Width + 2); tile!.Opacity = t < .1 ? 0 : 1; }
        }, () => { if (tile != null) tile.Opacity = 1; FxDone("pickup", layer); });
    }

    /// <summary>
    /// A rare drop, the big one: a block in its grade's colour wipes across its line and off again, the line under it; the
    /// rule over it draws in the colour, the tile punches, and a bar of the colour grows down the panel's left edge.
    /// </summary>
    protected override void RareDrop(Pick p, bool held)
    {
        if (_rareLine is not { } line || _rareRule is not { } rule) return;
        var rb = ModFx.In(line, _root); var lb = ModFx.In(_line, _root);
        if (rb.IsEmpty || lb.IsEmpty) return;
        var c = Frozen(_rareC);
        var layer = FxLayer("rare", _root);
        var box = new Rect(lb.Left + 1, rb.Top - 3, lb.Width - 2, rb.Height + 6);
        var block = ModFx.Put(layer, ModFx.Block(box.Width, box.Height, c), box.Left, box.Top);
        var bx = new ModFx.Xf(block, new Point(0, .5));
        var edge = ModFx.Put(layer, ModFx.Block(3, lb.Height - 2, c), lb.Left + 1, lb.Top + 1);
        var ex = new ModFx.Xf(edge, new Point(.5, 0));
        var rx = new ModFx.Xf(rule, new Point(0, .5));
        _rareDraw = 1; rule.InvalidateVisual();
        ModFx.Xf? tx = _rareTileEl != null ? new ModFx.Xf(_rareTileEl) : null;
        var marks = Marks(layer, box, c);
        Motion.Play(layer, 1400, t =>
        {
            for (int i = 0; i < marks.Count; i++)
            {
                double k = ModFx.Expo(Seg(t, .3, .4));
                if (i % 2 == 0) marks[i].X.S.ScaleX = k; else marks[i].X.S.ScaleY = k;
                marks[i].El.Opacity = t < .58 ? 1 : 0;
            }
            Wipe(bx, t, 0, .16, .26, .42, box.Width);
            line.Opacity = t < .14 ? 0 : 1;
            rx.S.ScaleX = ModFx.Expo(Seg(t, .3, .62));
            Run(ex, 0, ModFx.Expo(Seg(t, .12, .4)), lb.Height - 2, down: true);
            edge.Opacity = 1 - Seg(t, .82, 1);
            if (tx != null) tx.Scale = t < .3 ? 1 : 1 + .16 * (1 - ModFx.Expo(Seg(t, .3, .55)));
        }, () => { line.Opacity = 1; rx.S.ScaleX = 1; if (tx != null) tx.Scale = 1; FxDone("rare", layer); });
    }

    /// <summary>
    /// Your best passed: the ring fills; a solid accent square stamps over it with the star cut out in ink and lifts off the
    /// full ring; registration marks snap onto the Buddy's four corners and an accent rule draws under the hour.
    /// </summary>
    protected override void BestPassed(bool held)
    {
        if (_ringArt is not { } art) return;
        double was = _share is > 0 and < 1 ? _share : .8;
        var lb = ModFx.In(_line, _root);
        var rb = _ringEl != null ? ModFx.In(_ringEl, _root) : Rect.Empty;
        var hb = _rateText != null ? ModFx.In(_rateText, _root) : Rect.Empty;
        Canvas? layer = null;
        var marks = new List<(FrameworkElement El, ModFx.Xf X)>();
        FrameworkElement? stamp = null, under = null; ModFx.Xf? sx = null, ux = null;
        if (!lb.IsEmpty)
        {
            layer = FxLayer("best", _root);
            if (!rb.IsEmpty)
            {
                var st = new Grid { Width = rb.Width, Height = rb.Height, IsHitTestVisible = false };
                st.Children.Add(ModFx.Block(rb.Width, rb.Height, Accent, Corner2));
                var star = Tt("★", 17, Dark, FontWeights.Bold); star.HorizontalAlignment = HorizontalAlignment.Center;
                st.Children.Add(star);
                stamp = ModFx.Put(layer, st, rb.Left, rb.Top); sx = new ModFx.Xf(st);
            }
            if (!hb.IsEmpty) { under = ModFx.Put(layer, ModFx.Block(hb.Width, 2, Accent), hb.Left, hb.Bottom + 1); ux = new ModFx.Xf(under, new Point(0, .5)); }
            marks.AddRange(Marks(layer, lb, Accent));
        }
        Motion.Play(art, 1500, t =>
        {
            _fill = was + (1 - was) * ModFx.Expo(Seg(t, 0, .4)); art.InvalidateVisual();
            if (layer == null) return;
            if (sx != null) { sx.Scale = t < .66 ? ModFx.Expo(Seg(t, .38, .48)) : 1 - ModFx.In(Seg(t, .66, .78)); stamp!.Opacity = t < .38 || t > .78 ? 0 : 1; }
            if (ux != null) Wipe(ux, t, .48, .62, .84, .96, hb.Width);
            for (int i = 0; i < marks.Count; i++)
            {
                double k = ModFx.Expo(Seg(t, .46 + .015 * (i / 2), .58 + .015 * (i / 2)));
                if (i % 2 == 0) marks[i].X.S.ScaleX = k; else marks[i].X.S.ScaleY = k;
                marks[i].El.Opacity = 1 - Seg(t, .86, 1);
            }
        }, () => { _fill = -1; art.InvalidateVisual(); if (layer != null) FxDone("best", layer); });
    }

    /// <summary>An entry opened: a solid green block wipes across the timer's box and off again, revealing it lit; its dot blinks twice.</summary>
    protected override void EntryOpened(TimerBoard.Item i, bool held)
    {
        if (_chip is not { } chip) return;
        var cb = ModFx.In(chip, _root);
        if (cb.IsEmpty) return;
        var layer = FxLayer("entry", _root);
        var tone = _chipTone == TimerBadge.Tone.On ? Green : Paper;
        var block = ModFx.Put(layer, ModFx.Block(cb.Width, cb.Height, tone, Corner2), cb.Left, cb.Top);
        var bx = new ModFx.Xf(block, new Point(0, .5));
        var dot = _chipDot;
        Motion.Play(layer, 1000, t =>
        {
            Wipe(bx, t, 0, .14, .3, .46, cb.Width);
            chip.Opacity = t < .13 ? 0 : 1;
            // the blink: off, on, off, on
            if (dot != null) dot.Opacity = t is (>= .56 and < .63) or (>= .7 and < .77) ? 0 : 1;
        }, () => { chip.Opacity = 1; if (dot != null) dot.Opacity = 1; FxDone("entry", layer); });
    }

    /// <summary>
    /// A fight started: its panel wipes in from the top behind an accent scan rule, its body after. The wipe is a mask with
    /// a short soft edge running down (not a clip), so nothing in the panel is ever cut, only not yet lit.
    /// </summary>
    protected override void FightStarted(Encounter e, bool held)
    {
        var mask = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(0, 1) };
        var lit = new GradientStop(Colors.Black, 0); var dark = new GradientStop(Colors.Transparent, 0);
        mask.GradientStops.Add(lit); mask.GradientStops.Add(dark);
        var fb = ModFx.In(_fight, _root);
        Canvas? layer = null; FrameworkElement? scan = null; ModFx.Xf? sx = null;
        if (!fb.IsEmpty)
        {
            layer = FxLayer("start", _root);
            scan = ModFx.Put(layer, ModFx.Block(fb.Width - 2, 2, Accent), fb.Left + 1, fb.Top);
            sx = new ModFx.Xf(scan, new Point(0, .5));
        }
        Motion.Play(_fight, 700, t =>
        {
            // the lit part's foot, from the top to past the bottom, with a soft edge of about a tenth of the panel under it
            double k = ModFx.Expo(Seg(t, 0, .85)) * 1.1;
            lit.Offset = Math.Max(0, k - .1); dark.Offset = Math.Max(.001, k);
            _fight.OpacityMask = mask;
            if (sx != null)
            {
                sx.T.Y = Math.Min(1, k) * (fb.Height - 2);
                sx.S.ScaleX = ModFx.Expo(Seg(t, 0, .12));
                scan!.Opacity = 1 - Seg(t, .55, .85);
            }
        }, () => { _fight.ClearValue(UIElement.OpacityMaskProperty); if (layer != null) FxDone("start", layer); });
    }

    /// <summary>
    /// A fight ended, as a broadcast graphic over the header (the boss and the clock say the same; never over the rows). A
    /// kill: an off-white block wipes in from the left with KILLED and the time in ink, an accent rule draws under it, and it
    /// wipes off to the right. A wipe: an accent block drops down from the top with WIPED, the panel stutters, the rows go
    /// dim, and it folds back up.
    /// </summary>
    protected override void FightEnded(Encounter e, bool held)
    {
        if (e.Outcome is not ("kill" or "wipe" or "cleared")) return;
        bool wipe = e.Outcome == "wipe";
        // the band: its block wipes (scaled), its words only fade and slide (never squashed)
        var band = new Grid { VerticalAlignment = VerticalAlignment.Top };
        var block = new Border { Background = wipe ? Accent : Paper };
        band.Children.Add(block);
        var words = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        words.Children.Add(Tt(OutcomeText(e).ToUpperInvariant(), 16, Dark, FontWeights.Bold));
        var tm = Tt(Clock(e.Duration(e.End ?? DateTime.UtcNow)), 16, Frozen(Mod.A(BgC, .62)), FontWeights.SemiBold); tm.Margin = new Thickness(10, 0, 0, 0);
        words.Children.Add(tm);
        band.Children.Add(words);
        var bx = new ModFx.Xf(block, wipe ? new Point(.5, 0) : new Point(0, .5));
        var wx = new ModFx.Xf(words);
        _fightFx.Children.Clear(); _fightFx.Children.Add(band);
        // over the header as laid out now (the timers can sit above it in a dungeon), a few px proud of it each way
        void Seat()
        {
            double y = _fightBody.Margin.Top, h = 22;
            if (_head is { } hd && hd.ActualHeight > 0 && hd.IsDescendantOf(_fightGrid))
            {
                y = hd.TranslatePoint(new Point(0, 0), _fightGrid).Y; h = hd.ActualHeight;
            }
            band.Margin = new Thickness(0, Math.Max(0, y - 5), 0, 0);
            band.Height = h + 9;
        }
        Seat();
        double w = _fightGrid.ActualWidth > 0 ? _fightGrid.ActualWidth : FightWidth - 10;
        var fb = ModFx.In(_fight, _root);
        var hb = _head != null ? ModFx.In(_head, _root) : Rect.Empty;
        var rows = RowRoots();
        Canvas? layer = null; FrameworkElement? rule = null; ModFx.Xf? rx = null;
        if (!wipe && !fb.IsEmpty && !hb.IsEmpty)
        {
            layer = FxLayer("end", _root);
            rule = ModFx.Put(layer, ModFx.Block(fb.Width - 2, 2, Accent), fb.Left + 1, hb.Bottom + 4);
            rx = new ModFx.Xf(rule, new Point(0, .5));
        }
        var shift = new TranslateTransform();
        if (wipe) _fight.RenderTransform = shift;
        Motion.Play(_fightFx, 1500, t =>
        {
            Seat();
            if (!wipe)
            {
                // a stepped entrance (two hard steps, then whole), a short hold, a hard wipe off to the right
                double inn = t < .03 ? .4 : t < .06 ? .75 : 1;
                Run(bx, ModFx.Expo(Seg(t, .58, .68)), inn, w);
                wx.T.X = -12 * (1 - ModFx.Expo(Seg(t, .06, .2))) + 10 * ModFx.In(Seg(t, .58, .62));
                words.Opacity = Seg(t, .06, .09) * (1 - Seg(t, .58, .61));
                if (rx != null) Wipe(rx, t, .08, .2, .56, .66, fb.Width - 2);
            }
            else
            {
                // the block falls from the top (heavy, no ease-out), one frame's jolt as it lands, then folds back up
                double h = band.Height;
                bx.S.ScaleY = t < .6 ? ModFx.In(Seg(t, 0, .08)) : 1 - ModFx.Expo(Seg(t, .6, .7));
                bx.T.Y = t is >= .08 and < .11 ? 2 : 0;
                words.Opacity = Seg(t, .07, .1) * (1 - Seg(t, .58, .61));
                wx.T.Y = -6 * (1 - ModFx.Expo(Seg(t, .07, .16)));
                // the stutter: hard 2 px steps, no easing
                shift.X = t switch { >= .08 and < .11 => 2, >= .11 and < .14 => -2, >= .14 and < .17 => 1, _ => 0 };
                foreach (var r in rows) r.Opacity = 1 - .55 * ModFx.Life(t, .08, .11, .24, .3);
            }
        }, () => { _fightFx.Children.Clear(); if (wipe) _fight.RenderTransform = null; foreach (var r in rows) r.Opacity = 1; if (layer != null) FxDone("end", layer); });
    }
}
