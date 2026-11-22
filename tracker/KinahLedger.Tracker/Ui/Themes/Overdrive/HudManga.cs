using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using KinahLedger.Parser;
using KinahLedger.Session;
using static KinahLedger.Ui.HudKit;

namespace KinahLedger.Ui;

/// <summary>
/// Manga (the Overdrive theme, 2026-11-20; the settings' hint: "A manga finisher: speed-line bursts, screentone, giant sound
/// effects and a cut-in on every lead change"): the meter is a page from a battle manga at its climax. The fight is a sheet
/// of paper with a brushed ink edge, its panels stark black ink: the head (the boss, the outcome, the clock in Impact), a
/// gutter of paper with the boss's HP as a red brush stroke and, over the leader, a hand-lettered sound effect ("DOOM")
/// breaking out of its panel, the rows' panel, the foot in ink on the paper. Screentone shades two corners, ink is flung
/// off the sheet. Every row sits on solid ink (HudManga.Rows.cs): the leader's row is the climax panel, white with
/// concentration lines driving in from its edges and spilling out round the panel; each bar a brush stroke in the class's
/// colour, the only colour on the page but one hot red. The Buddy is a speech bubble (a shout when your best is beaten):
/// the hour huge in ink, your best's ring a circle of concentration lines, the pickups small panels, the timers and the
/// notice black narration boxes.
/// The moments are a manga's: a fight starts with the page wiped in by a stroke of ink and the sound effect slammed on;
/// a lead change cuts in a diagonal panel with the new leader's class art and speed lines; a kill flashes an inverted
/// impact frame under a giant sound effect; your best passed bursts a "!!" balloon; a rare drop throws shoujo sparkles.
/// </summary>
sealed partial class HudManga : ModernLook
{
    // the room round the look: 98 at the right for the pickup's panel beside the Buddy
    readonly Grid _root = new() { Margin = new Thickness(8, 8, 98, 10), HorizontalAlignment = HorizontalAlignment.Left };
    readonly StackPanel _body = new() { HorizontalAlignment = HorizontalAlignment.Left };
    double _opacity = 0.9;

    // the Buddy: the speech bubble, the ring, the hour, the session and the timer captions
    readonly Grid _line = new() { HorizontalAlignment = HorizontalAlignment.Left };
    readonly TacSketch _bubble;
    const double TailH = 7;
    readonly StackPanel _lineBody = new() { Orientation = Orientation.Horizontal, Margin = new Thickness(9, 5, 15, 5 + TailH) };
    readonly Canvas _lineAnchor = Tac.Anchor(HorizontalAlignment.Right, VerticalAlignment.Center);
    readonly StackPanel _timers = new() { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
    readonly Border _timersInLine = new() { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 0, 0) };
    /// <summary>The timer captions over the bubble, when the pickup strip takes their room in it.</summary>
    readonly Border _timersUp = new() { HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(4, 0, 0, 4), Visibility = Visibility.Collapsed };
    /// <summary>The pickup strip in Manga: 24 px tiles beside the hour, an ink rule, a red star.</summary>
    static readonly PickStrip.Look Strip = new(24, 4, true, Frozen(Mod.A(Color.FromRgb(0x0A, 0x0A, 0x0D), .34)), Frozen(Color.FromRgb(0xE8, 0x1C, 0x2B)));
    bool _dim, _shout;
    TacSketch? _ringArt;
    Grid? _ringHost;
    double _share = -1, _complete;
    bool _beat;
    Border? _firstChip;

    // the rare drop under the Buddy, and the notice
    readonly Grid _rare = new() { HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 3, 0, 0), Visibility = Visibility.Collapsed };
    readonly StackPanel _note = new() { Orientation = Orientation.Horizontal, Margin = new Thickness(4, 4, 0, 0), Visibility = Visibility.Collapsed };

    // the fight: the page and its panels
    readonly StackPanel _fightOuter = new() { HorizontalAlignment = HorizontalAlignment.Left, Visibility = Visibility.Collapsed, Margin = new Thickness(0, 0, 0, 8) };
    readonly Grid _fight = new() { HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top };
    readonly InkArt _page = new(), _pageInv = new() { Opacity = 0 };
    readonly StackPanel _fightBody = new() { VerticalAlignment = VerticalAlignment.Top };
    readonly Canvas _sfxLayer = new() { IsHitTestVisible = false, VerticalAlignment = VerticalAlignment.Top, HorizontalAlignment = HorizontalAlignment.Left };
    readonly Grid _fightFx = new() { IsHitTestVisible = false, ClipToBounds = true };
    readonly Border _timersInFight = new() { Margin = new Thickness(2, 0, 0, 5), HorizontalAlignment = HorizontalAlignment.Left };
    readonly TranslateTransform _fightShift = new();
    InkArt? _sfx;
    ScaleTransform? _sfxScale;
    double _pageW, _pageH, _rowsTop, _rowsH;
    string _pageSig = "";
    bool _bandHp;
    readonly List<double> _gutters = new();

    public HudManga() : base(bare: true)
    {
        UseRows(MangaRow.Pitch, m => new MangaRow(), 16, GapLine);
        _bubble = new TacSketch(DrawBubble);
        _line.Children.Add(_bubble); _line.Children.Add(_lineBody); _line.Children.Add(_lineAnchor);
        _fight.Children.Add(_page); _fight.Children.Add(_pageInv); _fight.Children.Add(_fightBody); _fight.Children.Add(_sfxLayer); _fight.Children.Add(_fightFx);
        _page.VerticalAlignment = _pageInv.VerticalAlignment = VerticalAlignment.Top;
        _page.HorizontalAlignment = _pageInv.HorizontalAlignment = HorizontalAlignment.Left;
        _fight.RenderTransform = _fightShift;
        _fightOuter.Children.Add(_timersInFight); _fightOuter.Children.Add(_fight);
        _runCaption = Caption(_runHead, new Thickness(7, 2, 8, 3)); _runCaption.HorizontalAlignment = HorizontalAlignment.Left; _runCaption.Margin = new Thickness(4, 0, 0, -2);
        _runCaption.Visibility = Visibility.Collapsed;
        _body.Children.Add(_fightOuter); _body.Children.Add(_timersUp); _body.Children.Add(_runCaption); _body.Children.Add(_line); _body.Children.Add(_rare); _body.Children.Add(_note);
        _root.Children.Add(_body);
        System.Windows.Documents.TextElement.SetFontFamily(_root, Mod.Face);
        TextOptions.SetTextFormattingMode(_root, TextFormattingMode.Display);
    }

    public override FrameworkElement Root => _root;
    public override Thickness Inset => _root.Margin;
    public override double Corner => 0;
    public override FrameworkElement? FightFrame => _fightOuter.Visibility == Visibility.Visible ? _fight : null;

    public override void SetOpacity(double opacity)
    {
        // the paper and the bubble take the setting, never so faint the ink is lost
        _opacity = Math.Clamp(opacity, 0.3, 1);
        _page.Opacity = ArtK;
        _bubble.Opacity = ArtK;
    }

    double ArtK => .72 + .28 * _opacity;

    protected override string LineSig(Figures f) => base.LineSig(f) + "|" + (Rare(Values)?.Id ?? "") + "|" + (Picks(Values).FirstOrDefault()?.Id ?? "") + "|" + Dimmed(Values) + "|" + PickStrip.Sig(Values, Strip) + "|" + RunBuddy.Sig(Values);

    static bool Dimmed(IReadOnlyDictionary<string, string> v) => (v.TryGetValue("show", out var s) && s == "dim") || (v.TryGetValue("paused", out var p) && p.Length > 0);

    /// <summary>A small ink box (TOP DPS, AVG, the heal mark); <paramref name="red"/> the hot red one (YOU); <paramref name="inverse"/> white on a black row.</summary>
    static Border Mark(string s, bool red = false, bool inverse = false, double size = 8.5)
    {
        var fg = red ? White : inverse ? InkB : White;
        var t = Tt(s, size, fg, FontWeights.Black);
        return new Border
        {
            Child = t, Padding = new Thickness(4, 0, 4, 1), VerticalAlignment = VerticalAlignment.Center,
            Background = red ? RedB : inverse ? White : InkB, BorderThickness = new Thickness(red || !inverse ? 1 : 0), BorderBrush = red ? InkB : White,
        };
    }

    // ================================================================ the Buddy

    /// <summary>The hour with its unit on the bubble (a best passed punches it).</summary>
    FrameworkElement? _hourTop;

    protected override void DrawLine(Figures f)
    {
        _lineBody.Children.Clear();
        _hourTop = null;
        _ringArt = null; _ringHost = null; _timerUp = false; _stripTimerW = -1;
        if (_timersInLine.Child != null) _timersInLine.Child = null;
        _dim = Dimmed(Values);
        _line.Opacity = _dim ? .66 : 1;
        _shout = false;
        bool mark = f.Abyss && f.Counted && f.Icon.Length > 0;
        if (mark) { var m = AbyssMark(f.Icon, 36); m.Margin = new Thickness(0, 0, 9, 0); _lineBody.Children.Add(m); }
        var rs = RunNow();
        _runCaption.Visibility = Visibility.Collapsed;
        if (rs != null) DrawRun(rs);
        else if (!HasHour(f))
        {
            var w = Tt(Waiting(f.Abyss), 12.5, InkB, FontWeights.Bold); w.Margin = new Thickness(mark ? 0 : 4, 6, 0, 6);
            // the kills and the Kinah so far under it (Steven, 2026-11-21: information, not emptiness)
            var stats = Sub(f, false);
            if (stats.Inlines.Count > 0)
            {
                w.Margin = new Thickness(mark ? 0 : 4, 3, 0, 0);
                stats.Margin = new Thickness(mark ? 1 : 5, 1, 0, 3);
                _lineBody.Children.Add(new StackPanel { VerticalAlignment = VerticalAlignment.Center, Children = { w, stats } });
            }
            else _lineBody.Children.Add(w);
        }
        else
        {
            if (HasRing(f))
            {
                _share = f.Share; _beat = f.Beat; _complete = 0; _shout = f.Beat;
                var ring = new Grid { Width = 44, Height = 44, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 9, 0) };
                _ringArt = new TacSketch(DrawRing, 44, 44);
                ring.Children.Add(_ringArt);
                var pc = f.Beat ? Tt("★", 15, RedB, FontWeights.Bold) : Fig(Pct(f.Share), 15, InkB);
                pc.HorizontalAlignment = HorizontalAlignment.Center;
                ring.Children.Add(pc);
                _ringHost = ring;
                _lineBody.Children.Add(ring);
            }
            else if (!mark) _lineBody.Children.Add(new Border { Width = 3 });
            // the hour huge in ink, its unit small; the session and the latest pickup under it
            var words = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 0, 0) };
            var top = new StackPanel { Orientation = Orientation.Horizontal };
            var rate = f.Warm ? Tt(f.Rate, 15, OnPaperSoft, FontWeights.Bold) : Fig(f.Rate, 27, InkB);
            rate.Tag = "buddy-rate";
            if (!f.Warm) rate.Margin = new Thickness(0, -3, 0, -3);
            top.Children.Add(rate);
            _hourTop = f.Warm ? null : top;
            if (f.Unit.Length > 0) { var u = Tt(f.Unit, 11, OnPaperSoft, FontWeights.Black); u.VerticalAlignment = VerticalAlignment.Bottom; u.Margin = new Thickness(3, 0, 0, 3); top.Children.Add(u); }
            words.Children.Add(top);
            var sub = Sub(f, true);
            FrameworkElement? strip = null, under = null;
            if (StripTime(f))
            {
                var bare = Sub(f, false);
                double baseW = 9 + 15 + 14 + _lineBody.Children.OfType<FrameworkElement>().Sum(Mod.W), timerW = LineTimerW();
                double wordsW = Math.Max(Mod.W(top), Mod.W(bare)), oldW = Math.Max(Mod.W(top), Mod.W(sub));
                foreach (var look in StripFall(Strip))
                {
                    if ((strip = StripIn(StripRoom(FightWidth - 10, baseW + timerW, wordsW, oldW, 12), look)) != null) break;
                    if (timerW > 0 && (strip = StripIn(StripRoom(FightWidth - 10, baseW, wordsW, oldW + timerW, 12), look)) != null) { _timerUp = true; break; }
                }
                if (strip != null) { sub = bare; strip.Margin = new Thickness(12, 0, 0, 0); }
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
        _bubble.InvalidateVisual();

        // the rare drop the window holds: a black ink panel, its tile, RARE DROP in red and its name
        _rare.Children.Clear();
        if (!KinahHidden && Rare(Values) is { } r)
        {
            _rare.Children.Add(new TacSketch(DrawRarePanel));
            var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(8, 6, 30, 6) };
            var tileHost = new Grid { Width = 34, Height = 34, Margin = new Thickness(0, 0, 9, 0), VerticalAlignment = VerticalAlignment.Center };
            var fx = new Canvas { Width = 0, Height = 0, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, IsHitTestVisible = false };
            tileHost.Children.Add(fx);
            var tile = Tile(r, 34);
            tileHost.Children.Add(tile);
            _rareTile = tile; _rareFx = fx;
            row.Children.Add(tileHost);
            var mk = Mark(L.T("RARE DROP"), red: true, size: 8); mk.Margin = new Thickness(0, 0, 7, 0);
            row.Children.Add(mk);
            row.Children.Add(Tt(r.Name, 13, InkB, FontWeights.Black));
            if (r.Count.Length > 0) { var cn = Tt(" " + r.Count, 11, OnPaperSoft, FontWeights.Bold); cn.Margin = new Thickness(2, 1, 0, 0); row.Children.Add(cn); }
            _rare.Children.Add(row);
            _rare.Visibility = Visibility.Visible;
        }
        else { _rare.Visibility = Visibility.Collapsed; _rareTile = null; _rareFx = null; }

        // the notice: a narration box under the Buddy, the warning sign before a pause
        _note.Children.Clear();
        if (f.Note.Length > 0 && !KinahHidden)
        {
            bool warn = ReferenceEquals(f.NoteTone, Warn);
            var sp = new StackPanel { Orientation = Orientation.Horizontal };
            if (warn) { var wi = WarnIcon(); wi.Width = wi.Height = 13; wi.Margin = new Thickness(0, 0, 5, 0); sp.Children.Add(wi); }
            var t = Tt(f.Note, 11.5, White, FontWeights.Bold); t.TextTrimming = TextTrimming.CharacterEllipsis; t.MaxWidth = 380;
            sp.Children.Add(t);
            _note.Children.Add(Caption(sp, new Thickness(7, 2, 9, 3), red: warn));
            _note.Visibility = Visibility.Visible;
        }
        else _note.Visibility = Visibility.Collapsed;
    }

    /// <summary>The line under the hour: the session, the latest pickup and its worth (<paramref name="pick"/>); in the Abyss its stats. Ink on the bubble.</summary>
    TextBlock Sub(Figures f, bool pick)
    {
        var sub = new TextBlock { FontFamily = Mod.Face, FontSize = 10.5, FontWeight = FontWeights.SemiBold, Foreground = OnPaperSoft, Margin = new Thickness(1, 1, 0, 0) };
        System.Windows.Documents.Typography.SetNumeralAlignment(sub, FontNumeralAlignment.Tabular);
        void Dot() { if (sub.Inlines.Count > 0) sub.Inlines.Add(new System.Windows.Documents.Run("  ·  ") { Foreground = OnPaperQuiet }); }
        if (f.Total.Length > 0) { sub.Inlines.Add(new System.Windows.Documents.Run(f.Total) { FontWeight = FontWeights.Black, Foreground = InkB }); sub.Inlines.Add(new System.Windows.Documents.Run(" " + f.TotalWord)); }
        foreach (var (fig, word, tone) in f.Stats)
        {
            Dot();
            sub.Inlines.Add(new System.Windows.Documents.Run(fig) { FontWeight = FontWeights.Black, Foreground = ReferenceEquals(tone, Rec) ? RedB : InkB });
            sub.Inlines.Add(new System.Windows.Documents.Run(" " + word));
        }
        if (pick && !f.Abyss && Picks(Values).FirstOrDefault() is { } p)
        {
            Dot();
            sub.Inlines.Add(new System.Windows.Documents.Run(p.Name.Length > 22 ? p.Name[..21].TrimEnd() + "…" : p.Name));
            var worth = p.Worth.Length > 0 ? "+" + p.Worth.TrimStart('+') : p.Count;
            if (worth.Length > 0) sub.Inlines.Add(new System.Windows.Documents.Run(" " + worth) { FontWeight = FontWeights.Black, Foreground = p.Worth.Length > 0 ? RedB : InkB });
        }
        return sub;
    }

    /// <summary>The timer captions' room at the bubble's end, 0 with none.</summary>
    double LineTimerW() => _timers.Children.Count > 0 && !KinahHidden ? 10 + Mod.W(_timers) : 0;

    /// <summary>The timers' room the bubble's strip was fitted to (-1: no strip time); captions that change it draw the line again.</summary>
    double _stripTimerW = -1;
    bool _timerUp;

    FrameworkElement? _rareTile;
    Canvas? _rareFx;

    /// <summary>
    /// The speech bubble: paper inside a thick ink line, a tail at its lower left; with your best beaten a shout (its edge
    /// burst into spikes). The page's paper under the words, so the hour reads in ink over any scene.
    /// </summary>
    void DrawBubble(DrawingContext dc, Size s)
    {
        if (s.Width < 20 || s.Height < 20) return;
        double h = s.Height - TailH, w = s.Width;
        var body = new Rect(1.5, 1.5, w - 3, h - 3);
        Geometry shape;
        if (_shout)
        {
            // the shout: spikes all round, a little longer at the corners
            var pts = new List<Point>();
            double per = 2 * (body.Width + body.Height), step = 9;
            int n = (int)(per / step);
            for (int i = 0; i < n; i++)
            {
                double d = per * i / n, x, y; Vector o;
                if (d < body.Width) { x = body.Left + d; y = body.Top; o = new Vector(0, -1); }
                else if (d < body.Width + body.Height) { x = body.Right; y = body.Top + d - body.Width; o = new Vector(1, 0); }
                else if (d < 2 * body.Width + body.Height) { x = body.Right - (d - body.Width - body.Height); y = body.Bottom; o = new Vector(0, 1); }
                else { x = body.Left; y = body.Bottom - (d - 2 * body.Width - body.Height); o = new Vector(-1, 0); }
                double k = i % 2 == 0 ? 0 : 2.6 + 1.2 * J(7, i);
                // pulled in a touch so the spikes stay inside the part's room
                pts.Add(new Point(x - o.X * (2.8 - k), y - o.Y * (2.8 - k)));
            }
            shape = Brushed(pts, 3, 0, 99);
        }
        else shape = new RectangleGeometry(body, Math.Min(15, h / 2.4), Math.Min(15, h / 2.4));
        // the tail: a wedge from the lower left, ending in a point under the bubble
        var tail = new StreamGeometry();
        using (var c = tail.Open())
        {
            c.BeginFigure(P(18, h - 4), true, true);
            c.LineTo(P(9, h - 1 + TailH - .5), true, true);
            c.LineTo(P(33, h - 4), true, true);
        }
        var all = Geometry.Combine(shape, tail, GeometryCombineMode.Union, null);
        var paper = _dim ? Frozen(Color.FromRgb(0xD8, 0xD8, 0xD4)) : PaperB;
        dc.DrawGeometry(paper, Pn(InkB, 2.4), all);
        // screentone in the bubble's lower right, where no word sits
        Tone(dc, new RectangleGeometry(new Rect(w - 26, h - 16, 24, 14)), P(w, h), 26, 3.6, 1.2, Frozen(Mod.A(InkC, .5)));
        dc.DrawGeometry(null, Pn(InkB, 2.4), all);
    }

    /// <summary>The ring of your best: a circle of concentration lines, ink for your part of it, faint for the rest, all red once beaten.</summary>
    void DrawRing(DrawingContext dc, Size s)
    {
        double c = s.Width / 2;
        const int N = 36;
        double share = Math.Clamp(_beat ? 1 : _share, 0, 1);
        double shown = share + (1 - share) * Math.Clamp(_complete, 0, 1);
        bool red = _beat || _complete >= 1;
        var on = Pn(red ? RedB : InkB, 2.3, false);
        var off = Pn(Frozen(Mod.A(InkC, .22)), 1.2, false);
        int lit = (int)Math.Round(shown * N);
        for (int i = 0; i < N; i++)
        {
            double a = 2 * Math.PI * i / N - Math.PI / 2, r0 = 13.5 + (i % 3 == 0 ? -1.5 : 0), r1 = c - 1.5 - (i % 2) * 2;
            var p0 = P(c + r0 * Math.Cos(a), c + r0 * Math.Sin(a)); var p1 = P(c + r1 * Math.Cos(a), c + r1 * Math.Sin(a));
            dc.DrawLine(i < lit ? on : off, p0, p1);
        }
    }

    /// <summary>The rare drop's panel: black ink with a brushed edge and a white hairline inside.</summary>
    void DrawRarePanel(DrawingContext dc, Size s)
    {
        if (s.Width < 10) return;
        var g = Brushed(new[] { P(1.5, 2), P(s.Width - 1.5, 1), P(s.Width - 3, s.Height - 1.5), P(1, s.Height - 1) }, 21, .7, 14);
        dc.DrawGeometry(PaperB, Pn(InkB, 2.4), g);
        Tone(dc, new RectangleGeometry(new Rect(s.Width - 30, 2, 27, s.Height - 4)), P(s.Width, s.Height / 2), 30, 3.6, 1.3, Frozen(Mod.A(InkC, .55)));
    }

    protected override void KinahShown(bool shown)
    {
        _line.Visibility = shown ? Visibility.Visible : Visibility.Collapsed;
        if (!shown) { _note.Visibility = Visibility.Collapsed; _rare.Visibility = Visibility.Collapsed; _runCaption.Visibility = Visibility.Collapsed; }
        _fightOuter.Margin = new Thickness(0, 0, 0, shown ? 8 : 0);
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
            var sp = new StackPanel { Orientation = Orientation.Horizontal };
            var dot = on ? White : tone == TimerBadge.Tone.Soon ? RedB : Frozen(Mod.A(Colors.White, .7));
            sp.Children.Add(new System.Windows.Shapes.Rectangle { Width = first ? 6 : 5, Height = first ? 6 : 5, Fill = dot, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0), RenderTransformOrigin = new Point(.5, .5), RenderTransform = new RotateTransform(45) });
            sp.Children.Add(Tt(text, first ? 11.5 : 10, first ? White : OnInkSoft, first ? FontWeights.Black : FontWeights.Bold));
            var chip = Caption(sp, first ? new Thickness(7, 2, 9, 3) : new Thickness(6, 1, 8, 2), red: on);
            chip.Margin = new Thickness(first ? 0 : 5, 0, 0, 0); chip.Tag = first ? "timer-chip" : null;
            if (first) _firstChip = chip;
            _timers.Children.Add(chip);
        }
        if (_stripTimerW >= 0 && Figs is { } f && Math.Abs(LineTimerW() - _stripTimerW) > .5) DrawLine(f);
        PlaceTimers();
    }

    void PlaceTimers()
    {
        bool inside = KinahHidden && _fightOuter.Visibility == Visibility.Visible, up = !inside && _timerUp;
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

    const double Mg = 6, HeadH = 34, FootH = 22, RowsPad = 4;

    string _leadFight = "";
    long _leadId;

    protected override void DrawFight(Encounter? e, DateTime now)
    {
        _fightBody.Children.Clear();
        if (e == null) { _fightOuter.Visibility = Visibility.Collapsed; _fightFx.Children.Clear(); _leadFight = ""; _leadId = 0; PlaceTimers(); return; }
        double w = FightWidth - 10;
        _fight.Width = w;
        double inner = w - 2 * Mg;
        double y = Mg;
        var panels = new List<(double Top, double H, int Kind)>();   // 0 the head, 1 the rows

        // the head panel: the boss, the outcome once it's over, the clock
        var head = new Grid { Height = HeadH, Margin = new Thickness(Mg, Mg, Mg, 0) };
        head.ColumnDefinitions.Add(new ColumnDefinition()); head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var title = Tt(e.Title, 14, White, FontWeights.Black, NameFace); title.TextTrimming = TextTrimming.CharacterEllipsis; title.Margin = new Thickness(13, 0, 0, 8);
        head.Children.Add(title);
        var right = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(8, 0, 11, 2), VerticalAlignment = VerticalAlignment.Center };
        if (e.Waiting) right.Children.Add(Mark(L.T("READY"), inverse: true, size: 9.5));
        else
        {
            var outcome = OutcomeText(e);
            if (outcome.Length > 0)
            {
                var st = Tt(outcome, 10, White, FontWeights.Black);
                var ob = new Border { Child = st, Padding = new Thickness(6, 1, 6, 2), Background = e.Outcome == "wipe" ? InkB : RedB, BorderBrush = White, BorderThickness = new Thickness(1.2), Margin = new Thickness(0, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center, Tag = "outcome" };
                right.Children.Add(ob);
            }
            var clock = Fig(Clock(e, now), 22, White); clock.Tag = "fight-clock";
            right.Children.Add(clock);
        }
        Grid.SetColumn(right, 1); head.Children.Add(right);
        _fightBody.Children.Add(head);
        panels.Add((y, HeadH, 0));
        y += HeadH;

        bool rowsShown = false, sfx = false;
        _bandHp = false;
        double bandTop = y, bandH = 0;
        if (e.Waiting)
        {
            var wt = Tt(e.ReadyText, 12.5, InkB, FontWeights.Bold); wt.Margin = new Thickness(Mg + 4, 5, Mg, 4); wt.TextTrimming = TextTrimming.CharacterEllipsis; wt.Height = 17;
            _fightBody.Children.Add(wt);
            y += 26;
            if (PartyBeforeHit(e)) { rowsShown = true; }
            else y += 2;
        }
        Grid? band = null;
        if (!e.Waiting) { rowsShown = true; band = new Grid { Margin = new Thickness(Mg, 0, Mg, 0) }; _fightBody.Children.Add(band); }
        var rowsHost = rowsShown ? new Grid { Margin = new Thickness(Mg + RowsPad, RowsPad, Mg + RowsPad, RowsPad) } : null;
        if (rowsHost != null) { PutRows(rowsHost, e, now, inner - 2 * RowsPad); _fightBody.Children.Add(rowsHost); }
        if (band != null)
        {
            // the sound effect only while the fight runs: once it's over the head says how it ended and the band folds away
            sfx = e.Live && e.HasDamage && Meter?.Members.FirstOrDefault() is { Rank: 1, CpOnly: false, Pvp: false };
            var hp = HpOf(e);
            bandH = hp != null || sfx ? 24 : 5;
            _bandHp = hp != null;
            band.Height = bandH;
            if (hp is { } h)
            {
                // the boss's HP: a red brush stroke in an ink track, ticks at the thirds, the figure and what's left at its right
                band.ColumnDefinitions.Add(new ColumnDefinition()); band.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                var track = new Grid { Height = 9, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(sfx ? SfxRoom() : 2, 1, 8, 0) };
                track.Children.Add(new TacSketch((dc, s) => DrawHp(dc, s, h)));
                HpGhost(track, e, h, now, 0);
                band.Children.Add(track);
                var hl = new TextBlock { FontFamily = Mod.Face, FontSize = 10.5, Foreground = OnPaperSoft, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 1, 2, 0) };
                System.Windows.Documents.Typography.SetNumeralAlignment(hl, FontNumeralAlignment.Tabular);
                hl.Inlines.Add(new System.Windows.Documents.Run($"{Math.Round(h * 100):0}%") { FontFamily = FigFace, FontSize = 14, Foreground = h < .34 ? RedB : InkB });
                var left = HpLeftText(e);
                if (left.Length > 0) hl.Inlines.Add(new System.Windows.Documents.Run("  " + left) { FontWeight = FontWeights.Bold });
                hl.Tag = "hp-text";
                Grid.SetColumn(hl, 1); band.Children.Add(hl);
            }
            y += bandH;
        }
        if (rowsShown)
        {
            double rh = Meter?.Height is double mh && !double.IsNaN(mh) ? mh : 0;
            _rowsTop = y; _rowsH = rh + 2 * RowsPad;
            panels.Add((y, _rowsH, 1));
            y += _rowsH;
        }
        if (!e.Waiting && e.HasDamage)
        {
            var foot = Foot(e, now);
            foot.Height = FootH; foot.Margin = new Thickness(Mg + 5, 1, Mg + 4, 0);
            _fightBody.Children.Add(foot);
            y += FootH + 1;
        }
        y += Mg - 1;
        _pageW = w; _pageH = Math.Ceiling(y);

        // the page's art, drawn again only when its shape changes
        bool leaderRow = rowsShown && Meter is { } mt && mt.Members.FirstOrDefault() is { Rank: 1, CpOnly: false, Pvp: false };
        // the paper gutters between the rows: each row its own panel (none across the "2 more" line before you pinned below)
        _gutters.Clear();
        if (rowsShown && Meter is { } mr)
            for (int i = 1; i < mr.Members.Count; i++)
                if (!mr.Members[i].Pinned) _gutters.Add(_rowsTop + RowsPad + i * MangaRow.Pitch - (MangaRow.Pitch - MangaRow.H) / 2);
        if (!_bandHp && bandH > 0) _gutters.Add(-1);   // (the sign of the band's tone in the page's key)
        var sig = $"{w}|{_pageH}|{string.Join(",", panels.Select(p => $"{p.Top:0.#}:{p.H:0.#}:{p.Kind}"))}|{bandTop}|{bandH}|{leaderRow}|{string.Join(",", _gutters)}";
        if (sig != _pageSig)
        {
            _pageSig = sig;
            _page.Art = Page(w, _pageH, panels, bandTop, bandH, leaderRow, _gutters, _bandHp ? 0 : SfxRoom());
            _page.Width = _pageInv.Width = w; _page.Height = _pageInv.Height = _pageH;
            _pageInv.Art = null;   // the impact frame, made when a kill wants it
        }
        _page.Opacity = ArtK;
        _fight.Height = _pageH;

        // the sound effect over the leader, breaking out of the band into the head panel
        PlaceSfx(sfx, bandTop, bandH);

        // the lead changing hands while the fight runs: the cut-in
        var leadM = Meter?.Members.FirstOrDefault(m => m.Rank == 1 && !m.CpOnly && !m.Pvp);
        long id = leadM?.Id ?? 0;
        if (e.Id == _leadFight && id != 0 && _leadId != 0 && id != _leadId && e.Live && !e.Waiting && Moves) LeadChanged(leadM!, false);
        _leadFight = e.Id; _leadId = id;

        _fightOuter.Visibility = Visibility.Visible;
        PlaceTimers();
    }

    /// <summary>The HP stroke: an ink track on the paper, the red stroke to what's left, the thirds as ink notches.</summary>
    static void DrawHp(DrawingContext dc, Size s, double hp)
    {
        if (s.Width < 8) return;
        double x1 = s.Width * Math.Clamp(hp, 0, 1);
        var track = new Rect(0, 0, s.Width, s.Height);
        dc.DrawRectangle(PaperB, Pn(InkB, 1.6, false), track);
        if (x1 > 1)
        {
            // the red stroke: a hard brush end, a dry streak along it
            var g = Brushed(new[] { P(1.5, 1.5), P(Math.Max(2, x1 - 2), 1.5), P(x1 + 1.5, s.Height / 2), P(Math.Max(2, x1 - 3), s.Height - 1.5), P(1.5, s.Height - 1.5) }, 31, .35, 10);
            dc.PushClip(new RectangleGeometry(track));
            dc.DrawGeometry(RedB, null, g);
            dc.DrawRectangle(Frozen(Mod.A(Colors.White, .32)), null, new Rect(3, 2.5, Math.Max(0, x1 - 9), 1.2));
            dc.Pop();
        }
        for (int k = 1; k <= 2; k++) { double x = Math.Round(s.Width * k / 3); dc.DrawRectangle(InkB, null, new Rect(x - 1, 0, 2, s.Height)); }
    }

    /// <summary>The room the sound effect takes at the band's left.</summary>
    double SfxRoom() => SfxArt().Size.Width + 4;

    (Drawing Art, Size Size) SfxArt()
    {
        var word = L.T("DOOM");
        if (_sfxWord?.Word == word) return (_sfxWord.Value.Art, _sfxWord.Value.Size);
        var art = Lettered(word, 25, Colors.White, InkC, RedC, 4.6, -9, out var size);
        _sfxWord = (word, art, size);
        return (art, size);
    }

    (string Word, Drawing Art, Size Size)? _sfxWord;

    void PlaceSfx(bool on, double bandTop, double bandH)
    {
        if (!on) { _sfxLayer.Children.Clear(); _sfx = null; return; }
        var (art, size) = SfxArt();
        if (_sfx == null)
        {
            _sfxScale = new ScaleTransform(1, 1);
            _sfx = new InkArt(art, size.Width, size.Height) { RenderTransformOrigin = new Point(.5, .5), RenderTransform = _sfxScale, Tag = "manga-sfx" };
            _sfxLayer.Children.Clear(); _sfxLayer.Children.Add(_sfx);
        }
        else { _sfx.Art = art; _sfx.Width = size.Width; _sfx.Height = size.Height; }
        // its foot sits on the rows' panel, its head in the head panel's lower left (under the title's line)
        Canvas.SetLeft(_sfx, Mg + 3);
        Canvas.SetTop(_sfx, bandTop + bandH - size.Height + 1);
    }

    /// <summary>
    /// The page: a sheet of paper with a brushed ink edge, tilted a hair; the head and the rows as black ink panels on it
    /// (the head's top left corner cut away, the rows' lower right), screentone in those two cut corners, ink flung off the
    /// sheet's right edge, and round the leader's row concentration lines spilling out of the panel into the gutters.
    /// The rows' panel is split by paper <paramref name="gutters"/>; without the boss's HP the band carries tone from <paramref name="toneFrom"/>.
    /// </summary>
    static DrawingGroup Page(double w, double h, List<(double Top, double H, int Kind)> panels, double bandTop, double bandH, bool leaderRow, IReadOnlyList<double> gutters, double toneFrom)
    {
        gutters = gutters.Where(g => g >= 0).ToList();
        var ink = InkB;
        var paper = PaperB;
        return Drawn(dc =>
        {
            // the sheet's ink edge, then its paper
            var edge = Brushed(new[] { P(-.5, 1), P(w + .5, -.5), P(w + .5, h + .5), P(-.5, h + .5) }, 11, .9, 13);
            dc.DrawGeometry(ink, null, edge);
            var sheet = Brushed(new[] { P(2, 3), P(w - 2, 1.8), P(w - 2, h - 2), P(2, h - 2.5) }, 12, .6, 15);
            dc.DrawGeometry(paper, null, sheet);
            double x0 = Mg, x1 = w - Mg;
            // the band without the boss's HP: screentone after the sound effect, swelling to the right
            if (toneFrom > 0 && bandH > 8)
                Tone(dc, new RectangleGeometry(new Rect(x0 + toneFrom + 6, bandTop, x1 - x0 - toneFrom - 6, bandH)), P(x1 + 30, bandTop + bandH / 2), Math.Max(60, (x1 - x0 - toneFrom) * .8), 4, 1.7, ink);
            // the leader's concentration lines, out of the row into the gutters round it
            var rows = panels.FirstOrDefault(p => p.Kind == 1);
            if (leaderRow && rows.H > 0)
            {
                double cy = rows.Top + RowsPad + MangaRow.H / 2;
                dc.PushClip(Geometry.Combine(sheet, new RectangleGeometry(new Rect(0, Math.Max(bandTop + bandH - (bandH >= 24 ? 5 : 9), rows.Top - 9), w, MangaRow.H + 2 * RowsPad + 12)), GeometryCombineMode.Intersect, null));
                dc.DrawGeometry(ink, null, Burst(P(w / 2, cy), .62, 1.8, 96, 5, 1.7, w / 2, MangaRow.H / 2 + 10));
                dc.Pop();
            }
            foreach (var (top, ph, kind) in panels)
            {
                Geometry g;
                if (kind == 0)
                {
                    // the head: its top left corner cut away for the tone, its foot slanting up to the right
                    double cut = 15, b = top + ph;
                    Tone(dc, new RectangleGeometry(new Rect(0, 0, x0 + cut + 10, top + cut + 10)), P(0, 0), 42, 4, 1.9, ink);
                    g = Brushed(new[] { P(x0 + cut, top), P(x1, top - .5), P(x1, b - 3), P(x0, b), P(x0, top + cut) }, 13, .55, 12);
                }
                else
                {
                    double cut = 13, b = top + ph;
                    Tone(dc, new RectangleGeometry(new Rect(x1 - cut - 10, b - cut - 10, w - x1 + cut + 10, cut + 12)), P(w, b + 2), 30, 4, 1.8, ink);
                    g = Brushed(new[] { P(x0, top + 1), P(x1, top - .5), P(x1, b - cut), P(x1 - cut, b), P(x0 + .5, b) }, 14, .55, 12);
                }
                dc.DrawGeometry(ink, null, g);
            }
            // the paper gutters between the rows, each row its own panel (a hair off level, as cut by hand)
            int gi = 0;
            foreach (var gy in gutters)
            {
                double tilt = 1.1 * J(15, gi++);
                dc.DrawGeometry(paper, null, new PolygonG(new[] { P(x0 - 1, gy - 1.1 + tilt), P(x1 + 1, gy - 1.1 - tilt), P(x1 + 1, gy + 1.1 - tilt), P(x0 - 1, gy + 1.1 + tilt) }).Geo);
            }
            // ink flung off the sheet
            Splat(dc, P(w - 1, 9), new Vector(1, .45), 41, 2.6, ink);
            Splat(dc, P(w, h - 16), new Vector(1, -.2), 42, 2.1, ink);
            Splat(dc, P(3, h - 3), new Vector(1, .1), 43, 1.6, ink);
        });
    }

    /// <summary>
    /// The impact frame: the page's paper turned to ink (the sheet one black slab, a white line round it), white impact
    /// lines out of the head's middle behind every word, the foot's strip hot red so its ink words still read. The words
    /// themselves are not touched: white on the ink, ink on the red.
    /// </summary>
    static DrawingGroup ImpactArt(double w, double h, double footTop) => Drawn(dc =>
    {
        var sheet = Brushed(new[] { P(-.5, 1), P(w + .5, -.5), P(w + .5, h + .5), P(-.5, h + .5) }, 11, .9, 13);
        dc.DrawGeometry(InkB, Pn(White, 2), sheet);
        dc.PushClip(sheet);
        dc.DrawGeometry(Frozen(Mod.A(Colors.White, .42)), null, Burst(P(w / 2, HeadH / 2 + Mg), 26, Math.Max(w, h), 80, 9, 2.6));
        if (footTop > 0 && footTop < h - 6) dc.DrawRectangle(RedB, null, new Rect(Mg - 2, footTop + 1, w - 2 * Mg + 4, h - footTop - Mg + 2));
        dc.Pop();
    });

    // ================================================================ the moments

    /// <summary>A pickup: a small panel with its worth slides in beside the bubble behind speed lines, then fades.</summary>
    protected override void Pickup(Pick p, bool held)
    {
        if (KinahHidden || _line.Visibility != Visibility.Visible) return;
        var worth = p.Worth.Length > 0 ? "+" + p.Worth.TrimStart('+') : p.Count;
        var words = new StackPanel { Margin = new Thickness(7, 1, 8, 2) };
        words.Children.Add(Fig(worth, 16, p.Worth.Length > 0 ? RedB : InkB));
        var nm = Tt(p.Name.Length > 13 ? p.Name[..12].TrimEnd() + "…" : p.Name, 9.5, OnPaperSoft, FontWeights.Bold); nm.Margin = new Thickness(0, -2, 0, 0);
        words.Children.Add(nm);
        var chip = new Border { Child = words, MaxWidth = 84, Background = PaperB, BorderBrush = InkB, BorderThickness = new Thickness(2), RenderTransformOrigin = new Point(.5, .5) };
        var host = new Grid();
        host.Children.Add(chip);
        var shift = new TranslateTransform();
        var tilt = new RotateTransform(-4);
        var tg = new TransformGroup(); tg.Children.Add(tilt); tg.Children.Add(shift);
        host.RenderTransform = tg;
        host.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        Canvas.SetLeft(host, 8); Canvas.SetTop(host, -host.DesiredSize.Height / 2 - 1);
        _lineAnchor.Children.Clear(); _lineAnchor.Children.Add(host);
        Motion.Play(host, 1100, t =>
        {
            double k = Back(Seg(t, 0, .22), 1.6);
            shift.X = 22 * (1 - k);
            host.Opacity = t < .8 ? Seg(t, 0, .1) : 1 - Seg(t, .8, 1);
        }, () => _lineAnchor.Children.Remove(host));
    }

    /// <summary>A rare drop: shoujo sparkles burst out behind the tile as it pops in.</summary>
    protected override void RareDrop(Pick p, bool held)
    {
        if (_rareFx is not { } fx) return;
        var tile = _rareTile;
        var sparkle = new InkArt(SparkleArt, 0, 0);
        var sc = new ScaleTransform(.3, .3); var rot = new RotateTransform(-20);
        var tg = new TransformGroup(); tg.Children.Add(sc); tg.Children.Add(rot);
        sparkle.RenderTransform = tg;
        // the big one in its grade's colour (2026-11-21 review: no bigger than the pickup's card): a balloon of the grade's
        // colour bursts behind the tile and the whole panel squashes and springs as it lands
        var gc = Rare(Values) is { } rp ? ColorOf(rp.Color, rp.Grade) : RedC;
        var balloon = new InkArt(Drawn(dc => dc.DrawGeometry(Frozen(gc), Pn(InkB, 2.2), Balloon(P(0, 0), 25, 17, 12, 97, .85))), 0, 0);
        var bsc = new ScaleTransform(0, 0); balloon.RenderTransform = bsc;
        fx.Children.Clear(); fx.Children.Add(balloon); fx.Children.Add(sparkle);
        var tsc = new ScaleTransform(1, 1);
        if (tile != null) { tile.RenderTransformOrigin = new Point(.5, .5); tile.RenderTransform = tsc; }
        var squash = new ScaleTransform(1, 1);
        _rare.RenderTransformOrigin = new Point(0, .5); _rare.RenderTransform = squash;
        Motion.Play(fx, 1000, t =>
        {
            bsc.ScaleX = bsc.ScaleY = Back(Seg(t, .04, .22), 2.6) * (1 - Ez.In(Seg(t, .6, .85)));
            squash.ScaleX = t < .06 ? 1 - .06 * Seg(t, 0, .06) : 1 + .08 * Ez.Wob(Seg(t, .06, .5), 1.5, 4);
            squash.ScaleY = t < .06 ? 1 + .08 * Seg(t, 0, .06) : 1 - .06 * Ez.Wob(Seg(t, .06, .5), 1.5, 4);
            double k = Out(Seg(t, 0, .45));
            sc.ScaleX = sc.ScaleY = .3 + .75 * k;
            rot.Angle = -20 + 26 * Out(t);
            sparkle.Opacity = t < .6 ? 1 : 1 - Seg(t, .6, 1);
            tsc.ScaleX = tsc.ScaleY = .6 + .4 * Back(Seg(t, .04, .36), 2.4);
        }, () => { fx.Children.Clear(); tsc.ScaleX = tsc.ScaleY = 1; _rare.RenderTransform = null; });
    }

    /// <summary>The sparkles: big and small four-pointed stars with ink edges and bubbles, round the drawing's origin.</summary>
    static Drawing? _sparkleArt, _bangArt;
    static Drawing SparkleArt => _sparkleArt ??= Drawn(dc =>
    {
        // a bloom of screentone, then a field of sparkles in four sizes and bubbles, flung wider to the right (the tile sits near the window's left)
        // the bloom: a ring of tone round the tile, densest at the tile's edge
        var bloom = Geometry.Combine(new EllipseGeometry(P(16, 0), 38, 30), new EllipseGeometry(P(0, 0), 16, 16), GeometryCombineMode.Exclude, null);
        Tone(dc, bloom, P(0, 0), 46, 3.2, 1.9, Frozen(Mod.A(InkC, .9)));
        var edge = Pn(InkB, 1.6);
        double[] sizes = { 13, 9, 6.5, 4.2 };
        for (int i = 0; i < 14; i++)
        {
            // round the tile, outside it: wide to the right, close at the left (the window's edge), shallow above and below
            double a = 2 * Math.PI * i / 14 + .25 * J(51, i), r = 25 + 6 * (J(52, i) + 1) / 2, s = sizes[i % 4];
            double x = r * Math.Cos(a) * (Math.Cos(a) > 0 ? 1.75 : .95), y = r * Math.Sin(a) * .95;
            y = Math.Clamp(y, -34 + s, 28 - s); x = Math.Max(x, -24 + s);
            dc.DrawGeometry(White, edge, Sparkle(P(x, y), s));
        }
        for (int i = 0; i < 9; i++)
        {
            double a = 2 * Math.PI * (i + .5) / 9, r = 15 + 6 * J(53, i);
            double x = r * Math.Cos(a) * (Math.Cos(a) > 0 ? 1.6 : .6), y = r * Math.Sin(a) * .7;
            dc.DrawEllipse(White, Pn(InkB, .9), P(x, y), 1.8 + J(54, i) * .6, 1.8 + J(54, i) * .6);
        }
    });

    /// <summary>Your best passed: the ring's lines fill round in red and a "!!" balloon bursts beside it.</summary>
    protected override void BestPassed(bool held)
    {
        if (_ringArt is not { } art || _ringHost is not { } host) return;
        double was = _share is > 0 and < 1 ? _share : .86;
        bool beat = _beat;
        // the balloon breaks out over the bubble's top left, clear of the hour
        var balloon = new InkArt(BangArt, 0, 0) { HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(9, 13, 0, 0) };
        var sc = new ScaleTransform(0, 0);
        balloon.RenderTransform = sc;
        host.Children.Add(balloon);
        // the hour takes the hit with the balloon: crouched, punched past its size, a shudder
        var hour = _hourTop;
        var hsc = new ScaleTransform(1, 1); var hj = new TranslateTransform();
        if (hour != null) { hour.RenderTransformOrigin = new Point(0, .6); hour.RenderTransform = new TransformGroup { Children = { hsc, hj } }; }
        Motion.Play(art, 1100, t =>
        {
            hsc.ScaleX = hsc.ScaleY = t < .3 ? 1 - .06 * Ez.InOut(Seg(t, .22, .3)) : .94 + .06 * Out(Seg(t, .3, .34)) + .12 * Ez.Wob(Seg(t, .3, .72), 1.5, 4);
            hj.X = Ez.Jolt(Seg(t, .3, .5), 2, 6);
            _beat = false; _share = was;
            _complete = Out(Seg(t, 0, .4));
            if (t >= .4) { _beat = beat; _complete = 1; }
            art.InvalidateVisual();
            double k = Back(Seg(t, .3, .55), 2.6);
            sc.ScaleX = sc.ScaleY = k;
            balloon.Opacity = t < .8 ? 1 : 1 - Seg(t, .8, 1);
        }, () => { _complete = 0; _beat = beat; art.InvalidateVisual(); host.Children.Remove(balloon); if (hour != null) hour.RenderTransform = null; });
    }

    /// <summary>The "!!" balloon: a red explosion with an ink edge, "!!" lettered in white, round its origin.</summary>
    static Drawing BangArt => _bangArt ??= Drawn(dc =>
    {
        dc.DrawGeometry(RedB, Pn(InkB, 2.2), Balloon(P(0, 0), 22, 13.5, 12, 61));
        var bang = Lettered("!!", 24, Colors.White, InkC, null, 3.6, 6, out var s);
        dc.PushTransform(new TranslateTransform(-s.Width / 2, -s.Height / 2));
        dc.DrawDrawing(bang);
        dc.Pop();
    });

    /// <summary>
    /// An entry opened, a panel's "!" beat: the first caption crouches, then punches out tilted as a red explosion balloon
    /// bursts behind its words and ink focus lines shoot out round it; the balloon shrinks back into the caption, which
    /// shudders and settles red, square to the page.
    /// </summary>
    protected override void EntryOpened(TimerBoard.Item i, bool held)
    {
        if (_firstChip is not { } chip || chip.Child is not FrameworkElement words) return;
        chip.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        double cw = Math.Max(40, chip.DesiredSize.Width), ch = Math.Max(14, chip.DesiredSize.Height);
        // the balloon and the focus lines, drawn once round the caption's middle
        var balloon = new InkArt(Drawn(dc => dc.DrawGeometry(RedB, Pn(InkB, 2), Balloon(P(0, 0), cw * .62, cw * .46, 11, 83, ch * 1.05 / cw))), 0, 0);
        var bsc = new ScaleTransform(0, 0); balloon.RenderTransform = bsc;
        // (flat: the bubble often sits at the window's top, with a few pixels above it)
        var lines = new InkArt(Drawn(dc => dc.DrawGeometry(InkB, null, Burst(P(0, 0), cw * .64, cw * .96, 26, 85, 1.5, 1, ch * .95 / cw))), 0, 0);
        var lsc = new ScaleTransform(1, 1); lines.RenderTransform = lsc;
        var stage = new Canvas { Width = 0, Height = 0, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, IsHitTestVisible = false };
        stage.Children.Add(lines); stage.Children.Add(balloon);
        chip.Child = null;
        var g = new Grid { Margin = new Thickness(-chip.Padding.Left, -chip.Padding.Top, -chip.Padding.Right, -chip.Padding.Bottom) };
        var inner = new Border { Padding = chip.Padding, Child = words };
        g.Children.Add(stage); g.Children.Add(inner);
        chip.Child = g;
        var sc = new ScaleTransform(1, 1); var rot = new RotateTransform(0); var at = new TranslateTransform();
        chip.RenderTransformOrigin = new Point(.5, .5);
        chip.RenderTransform = new TransformGroup { Children = { sc, rot, at } };
        Motion.Play(chip, 950, t =>
        {
            double wind = Seg(t, 0, .12), pop = Ez.Wob(Seg(t, .12, .62), 1.5, 4), rise = Out(Seg(t, .12, .18));
            sc.ScaleX = sc.ScaleY = t < .12 ? 1 - .14 * Ez.InOut(wind) : .86 + .14 * rise + .2 * pop;
            rot.Angle = t < .12 ? 3 * wind : -7 * (1 - Out(Seg(t, .12, .5)));
            at.X = Ez.Jolt(Seg(t, .3, .55), 2, 5);
            double b = Seg(t, .12, .24), shrink = Seg(t, .42, .62);
            bsc.ScaleX = bsc.ScaleY = t < .12 ? 0 : 1.1 * Back(b, 2.2) * (1 - Ez.In(shrink));
            double l = Seg(t, .14, .62);
            lsc.ScaleX = lsc.ScaleY = .8 + .45 * Out(l);
            lines.Opacity = t < .14 ? 0 : 1 - Seg(l, .4, 1);
        }, () =>
        {
            chip.RenderTransform = null;
            inner.Child = null; chip.Child = words;
        });
    }

    /// <summary>
    /// A fight started: the page turns in, wiped on behind a stroke of ink that crosses it left to right, and the sound
    /// effect is slammed onto the leader.
    /// </summary>
    protected override void FightStarted(Encounter e, bool held)
    {
        if (held && Environment.GetEnvironmentVariable("KL_MANGA_CUTIN") == "1")
        {
            // a render of the cut-in (a lead change has no frame of its own in the theme renders)
            if (Meter?.Members.FirstOrDefault(m => m.Rank == 1 && !m.CpOnly && !m.Pvp) is { } lm) LeadChanged(lm, true);
            return;
        }
        double w = _pageW > 0 ? _pageW : FightWidth - 10, h = _pageH > 0 ? _pageH : 200;
        var clipShift = new TranslateTransform(-(w + 60), 0);
        var clip = new StreamGeometry();
        using (var c = clip.Open()) { c.BeginFigure(P(-120, -20), true, true); c.LineTo(P(w + 40, -20), true, false); c.LineTo(P(w + 10, h + 20), true, false); c.LineTo(P(-120, h + 20), true, false); }
        clip.Transform = clipShift;
        // the art is wiped on; the words come up behind the stroke (never cut by it)
        _page.Clip = clip; _sfxLayer.Clip = clip;
        var stroke = new InkArt(Drawn(dc =>
        {
            dc.DrawGeometry(InkB, null, Brushed(new[] { P(30, -4), P(48, -4), P(18, h + 4), P(0, h + 4) }, 71, 1.6, 9));
            Splat(dc, P(16, h * .3), new Vector(-1, .2), 72, 2.4, InkB);
            Splat(dc, P(8, h * .75), new Vector(-1, -.3), 73, 2, InkB);
        }), 48, h) { HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top };
        var sx = new TranslateTransform();
        stroke.RenderTransform = sx;
        _fightFx.Children.Clear(); _fightFx.Children.Add(stroke);
        var sfx = _sfx; var sfs = _sfxScale;
        Motion.Play(_fightFx, 820, t =>
        {
            // (the wipe in .3 of the course, the words whole by then: the page never hides the figures for long)
            double k = Out(Seg(t, 0, .3));
            clipShift.X = -(w + 60) * (1 - k);
            sx.X = (w + 40) * k - 44;
            stroke.Opacity = t < .26 ? 1 : 1 - Seg(t, .26, .36);
            _fightShift.X = -6 * (1 - k);
            // the words come up as the stroke passes, whole once the page is on (never over the bare game)
            _fightBody.Opacity = Seg(t, .18, .3);
            if (sfx != null && sfs != null)
            {
                double s = Seg(t, .58, .82);
                sfx.Opacity = t < .58 ? 0 : 1;
                sfs.ScaleX = sfs.ScaleY = 1.55 - .55 * Back(s, 2.2);   // (from 1.55: from 1.9 it ran 11 px past the window's left)
            }
        }, () => { _page.Clip = null; _sfxLayer.Clip = null; _fightBody.Opacity = 1; _fightFx.Children.Remove(stroke); _fightShift.X = 0; if (sfx != null) sfx.Opacity = 1; if (sfs != null) sfs.ScaleX = sfs.ScaleY = 1; });
    }

    /// <summary>
    /// A fight ended: the impact frame. The page's art flashes inverted twice (ink to paper, paper to ink, with impact lines
    /// out of the head; the words stay as they are), and a giant sound effect slams over the head and holds, then goes.
    /// </summary>
    protected override void FightEnded(Encounter e, bool held)
    {
        if (e.Outcome is not ("kill" or "wipe" or "cleared")) return;
        bool wipe = e.Outcome == "wipe";
        if (_pageInv.Art == null && _pageSig.Length > 0) _pageInv.Art = ImpactArt(_pageW, _pageH, _rowsH > 0 ? _rowsTop + _rowsH : 0);
        double w = _pageW > 0 ? _pageW : FightWidth - 10;
        var word = wipe ? L.T("CRASH") : L.T("KABOOM!");
        var art = Lettered(word, 34, wipe ? Color.FromRgb(0xD8, 0xD8, 0xDC) : Colors.White, InkC, RedC, 6, wipe ? 5 : -8, out var size);
        var big = new InkArt(art, size.Width, size.Height) { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Top, RenderTransformOrigin = new Point(.5, .5) };
        double fit = Math.Min(1, (w - 30) / Math.Max(1, size.Width));
        big.Margin = new Thickness(0, Math.Max(0, Mg + HeadH / 2 - size.Height * fit / 2 + 4), 0, 0);
        var sc = new ScaleTransform(fit, fit);
        var drop = new TranslateTransform(); var tip = new RotateTransform(0);
        big.RenderTransform = new TransformGroup { Children = { sc, tip, drop } };
        var lines = new InkArt(Drawn(dc => dc.DrawGeometry(InkB, null, Burst(P(0, 0), size.Width * fit * .55, size.Width * fit * .95, 40, 19, 1.6, 1, .55))), 0, 0)
        { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, Mg + HeadH / 2 + 4, 0, 0) };
        var lsc = new ScaleTransform(1, 1); lines.RenderTransform = lsc;
        _fightFx.Children.Clear(); _fightFx.Children.Add(lines); _fightFx.Children.Add(big);
        double rise = Mg + HeadH / 2 + 4 + size.Height * fit;
        Motion.Play(_fightFx, 1000, t =>
        {
            if (!wipe)
            {
                // a kill: two flashes of the inverted frame on the art (the page's own art under it), the word slammed from
                // big, the page jolted sideways by the hit
                _pageInv.Opacity = (t is > .0 and < .07) || (t is > .12 and < .19) ? ArtK : 0;
                double k = Seg(t, .03, .22);
                sc.ScaleX = sc.ScaleY = fit * (2.1 - 1.1 * Back(k, 2));
                _fightShift.X = Ez.Jolt(Seg(t, .12, .36), 5, 5); _fightShift.Y = Ez.Jolt(Seg(t, .12, .36), 2, 4);
            }
            else
            {
                // a wipe: no flash; the word falls from above the page, lands heavy (a dead thud down the page), tips and sags
                // (from inside the window: a short fall from a little above, big, onto the page)
                double f = Seg(t, 0, .14);
                drop.Y = -Math.Min(rise, 16) * (1 - f * f);
                sc.ScaleX = sc.ScaleY = fit * (1.3 - .3 * f * f);
                tip.Angle = 7 * Out(Seg(t, .14, .7));
                drop.Y += 4 * Out(Seg(t, .14, .7));
                _fightShift.Y = Math.Abs(Ez.Jolt(Seg(t, .14, .36), 4, 4));
                // the page goes grey under it: the rows sag to half ink and come back as the word goes
                _fightBody.Opacity = t < .14 ? 1 : t < .62 ? 1 - .45 * Out(Seg(t, .14, .22)) : .55 + .45 * Seg(t, .62, .8);
            }
            big.Opacity = t < .03 ? 0 : t < .8 ? 1 : 1 - Seg(t, .8, 1);
            lsc.ScaleX = lsc.ScaleY = .7 + .5 * Out(Seg(t, .05, .5));
            lines.Opacity = wipe || t < .05 ? 0 : 1 - Seg(t, .25, .6);
        }, () => { _fightFx.Children.Clear(); _pageInv.Opacity = 0; _fightShift.X = _fightShift.Y = 0; _fightBody.Opacity = 1; });
    }

    /// <summary>
    /// The lead changed hands: a diagonal panel slashes in across the rows with the new leader's class art big and their
    /// name over speed lines, holds a beat and is gone in under a second; as it leaves, the sound effect is slammed onto
    /// the new leader, their crown lands and a glint runs along the bars.
    /// </summary>
    void LeadChanged(MeterMember lead, bool held)
    {
        double w = _pageW > 0 ? _pageW : FightWidth - 10;
        double bh = 62, top = Math.Max(4, _rowsTop + RowsPad + MangaRow.H / 2 - bh / 2 + 4);
        var band = new Grid { Width = w, Height = bh, VerticalAlignment = VerticalAlignment.Top, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, top, 0, 0), RenderTransformOrigin = new Point(.5, .5) };
        band.Children.Add(new InkArt(CutInArt(w, bh, lead.ClassColor), w, bh) { HorizontalAlignment = HorizontalAlignment.Left });
        // the class's art big, breaking out over the panel's top edge; the name on a narration box beside it
        var who = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(30, -10, 0, 0) };
        var icon = ThemeText.Icon(lead.Class, 58, lead.Name);
        if (icon is Border ib) { ib.BorderBrush = InkB; ib.BorderThickness = new Thickness(3.5); }
        icon.VerticalAlignment = VerticalAlignment.Top;
        who.Children.Add(icon);
        var nm = Tt(lead.Name, 19, White, FontWeights.Black, NameFace); nm.MaxWidth = Math.Max(80, w - 190);
        nm.TextTrimming = TextTrimming.CharacterEllipsis;
        var cap = Caption(nm, new Thickness(8, 1, 9, 3)); cap.VerticalAlignment = VerticalAlignment.Top; cap.Margin = new Thickness(6, 24, 0, 0);
        who.Children.Add(cap);
        band.Children.Add(who);
        var zb = Lettered(L.T("ZBAM!"), 24, Colors.White, InkC, RedC, 4.4, -10, out var zs);
        band.Children.Add(new InkArt(zb, zs.Width, zs.Height) { HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, -6, 14, 0) });
        var shift = new TranslateTransform(w, 0);
        var punch = new ScaleTransform(1.2, 1.2);
        var tg = new TransformGroup(); tg.Children.Add(punch); tg.Children.Add(new RotateTransform(-6)); tg.Children.Add(shift);
        band.RenderTransform = tg;
        // the exit: one hard wipe, the panel cut away left to right (no fade)
        var wipe = new TranslateTransform();
        band.Clip = new RectangleGeometry(new Rect(-40, -40, w + 80, bh + 80)) { Transform = wipe };
        _fightFx.Children.Clear(); _fightFx.Children.Add(band);
        var sfx = _sfx; var sfs = _sfxScale;
        bool crowned = false;
        Motion.Play(band, 840, t =>
        {
            double k = Back(Seg(t, 0, .32), 1.3);
            shift.X = w * 1.05 * (1 - k);
            punch.ScaleX = punch.ScaleY = 1.2 - .2 * Out(Seg(t, 0, .4));
            wipe.X = (w + 80) * Out(Seg(t, .66, .84));
            if (sfx != null && sfs != null)
            {
                double s = Seg(t, .62, .86);
                sfx.Opacity = t < .62 && t > .02 ? 0 : 1;
                sfs.ScaleX = sfs.ScaleY = t < .62 ? 1 : 1.8 - .8 * Back(s, 2.2);
            }
            if (!crowned && t >= .62) { crowned = true; Crown(lead.Id, held); }
        }, () => { _fightFx.Children.Remove(band); if (sfx != null) sfx.Opacity = 1; if (sfs != null) sfs.ScaleX = sfs.ScaleY = 1; });
    }

    /// <summary>The new leader's crown lands, and a glint runs down the bars one after another.</summary>
    void Crown(long id, bool held)
    {
        if (Meter is not { } m) return;
        if (m.RowOf(id) is MangaRow lead) lead.Crown();
        int i = 0;
        foreach (var mm in m.Members) if (m.RowOf(mm.Id) is MangaRow r) r.Glint(i++ * 70);
    }

    /// <summary>
    /// The cut-in's panel: a slashed parallelogram in the leader's class colour, black speed lines streaming across it, a
    /// red stripe at its foot, a thick ink border and a white one outside it (so it reads on the rows' ink).
    /// </summary>
    static Drawing CutInArt(double w, double h, Color cls) => Drawn(dc =>
    {
        double s = 30;
        var shape = new[] { P(s, 0), P(w - 2, 0), P(w - 2 - s, h), P(2, h) };
        var g = Brushed(shape, 85, .6, 16);
        dc.DrawGeometry(White, Pn(White, 6), g);
        dc.DrawGeometry(Frozen(cls), Pn(InkB, 3.2), g);
        dc.PushClip(g);
        // a darker band toward the foot, the red stripe on it
        dc.DrawRectangle(Frozen(Mod.A(Mod.Mix(cls, Colors.Black, .35), .9)), null, new Rect(0, h * .62, w, h));
        dc.DrawRectangle(RedB, null, new Rect(0, h - 7, w, 4));
        for (int i = 0; i < 26; i++)
        {
            double y = 3 + (h - 6) * (i + .5 * J(81, i)) / 26, x0 = w * (.3 + .6 * (J(82, i) + 1) / 2);
            dc.DrawGeometry(Frozen(Mod.A(InkC, .45 + .35 * J(83, i))), null, Streak(P(x0 - w * (.25 + .25 * (J(84, i) + 1)), y), P(x0, y), .7 + .7 * (J(85, i) + 1)));
        }
        dc.Pop();
        dc.DrawGeometry(null, Pn(InkB, 3.2), g);
    });

    /// <summary>A closed polygon's geometry, frozen.</summary>
    readonly struct PolygonG
    {
        public readonly Geometry Geo;
        public PolygonG(IReadOnlyList<Point> pts)
        {
            var g = new StreamGeometry();
            using (var c = g.Open()) { c.BeginFigure(pts[0], true, true); c.PolyLineTo(pts.Skip(1).ToList(), true, true); }
            g.Freeze();
            Geo = g;
        }
    }
}
