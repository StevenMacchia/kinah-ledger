using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using KinahLedger.Parser;
using KinahLedger.Session;
using static KinahLedger.Ui.HudKit;

namespace KinahLedger.Ui;

/// <summary>
/// Arena and Studio (the Tactical theme's broadcast looks; Steven picked both from the Scorebug's concepts, 2026-11-17). Their
/// Kinah side is a tournament broadcast's bug: solid blocks slanted ten degrees, condensed bold type, a red block with your
/// best's share, a near-black one with the hour huge in white, a light one with the session, a green one for the next timer.
/// A fight: Arena draws the stadium's video board (a red title block, the clock in amber digits, one black tower with LED
/// segment bars glowing in the class's colour); Studio a daytime studio package (white glossy cards on soft shadows, a thick
/// glossy band as each member's bar, Ui/Themes/Tactical/HudScorebug.Studio.cs). Arena keeps the Scorebug's key (scorebug).
/// The moments: a pickup wipes a yellow ticker in under the bug; a rare drop runs a lower third with its tile; your best
/// passed runs a NEW PERSONAL BEST lower third with a light sweep; an entry opening pulses the green block's edge; a
/// fight's start slides its parts in one after another; its end puts up a VICTORY or DEFEAT plate.
/// </summary>
sealed partial class HudScorebug : TacticalLook
{
    static readonly Color RedC = Color.FromRgb(0xE8, 0x26, 0x3A), InkC0 = Color.FromRgb(0x0E, 0x10, 0x16), LightC = Color.FromRgb(0xF2, 0xF3, 0xF7),
        GreenC = Color.FromRgb(0x2B, 0xD4, 0x7A), YellowC = Color.FromRgb(0xFF, 0xD2, 0x3F), OrangeC = Color.FromRgb(0xFF, 0x9F, 0x1C), SlateC = Color.FromRgb(0x2A, 0x2F, 0x3B),
        GoldTab = Color.FromRgb(0xF5, 0xC5, 0x42), SilverTab = Color.FromRgb(0xC9, 0xD1, 0xDC), BronzeTab = Color.FromRgb(0xD0, 0x86, 0x4A), SubC = Color.FromRgb(0x5A, 0x60, 0x70);
    static readonly SolidColorBrush White = Frozen(Colors.White), Light = Frozen(LightC), InkB = Frozen(InkC0), Sub = Frozen(SubC), Yellow = Frozen(YellowC),
        Grey = Frozen(Color.FromRgb(0xA9, 0xAF, 0xBE)), Red = Frozen(RedC), Green = Frozen(GreenC);
    static readonly FontFamily Face = HudThemes.Bahn;
    static readonly FontStretch Tight = FontStretches.Condensed;
    const double BugH = 42, Lean = 7.4;   // tan(10°) of the bug's height

    // 35 under the bug: the NEW PERSONAL BEST lower third (3 down, 30 tall, its hairline) hangs there, never cut
    readonly Grid _root = new() { Margin = new Thickness(4, 4, 12, 35), HorizontalAlignment = HorizontalAlignment.Left };
    readonly StackPanel _body = new() { HorizontalAlignment = HorizontalAlignment.Left };
    double _opacity = 0.9;

    // the bug
    readonly Grid _bugHost = new() { HorizontalAlignment = HorizontalAlignment.Left };
    readonly StackPanel _bug = new() { Orientation = Orientation.Horizontal };
    readonly Canvas _bugAnchor = Tac.Anchor(HorizontalAlignment.Left, VerticalAlignment.Bottom);
    readonly StackPanel _under = new() { HorizontalAlignment = HorizontalAlignment.Left };
    FrameworkElement? _bestBlock, _timerBlock, _rareBanner;
    double _timerPulse = -1;
    readonly TacSketch _timerEdge;
    (string Text, TimerBadge.Tone Tone)? _timer;
    string _timerMore = "";

    // the leaderboard
    readonly StackPanel _fight = new() { HorizontalAlignment = HorizontalAlignment.Left, Visibility = Visibility.Collapsed, Margin = new Thickness(0, 0, 0, 6) };
    readonly Grid _fightGrid = new() { HorizontalAlignment = HorizontalAlignment.Left };
    readonly Border _titleHost = new(), _hpHost = new(), _rowsHost = new(), _footHost = new();
    readonly Grid _fightFx = new() { IsHitTestVisible = false };
    double _enter = 1;

    /// <summary>Studio's fight (white cards) rather than Arena's (the video board).</summary>
    readonly bool _studio;

    public HudScorebug(bool studio = false) : base(bare: false, Face)
    {
        _studio = studio;
        // the leaderboard's own rows (Ui/Themes/ThemeRows.cs)
        if (studio) UseRows(StudioRow.Pitch, m => new StudioRow(), 16, StudioGap);
        else UseRows(ArenaRow.Pitch, m => new ArenaRow(), 18, ArenaGap);
        _timerEdge = new TacSketch(DrawTimerEdge);
        _bugHost.Children.Add(_bug); _bugHost.Children.Add(_bugAnchor);
        var parts = new StackPanel();
        parts.Children.Add(_titleHost);
        if (studio) { parts.Children.Add(_hpHost); parts.Children.Add(_rowsHost); parts.Children.Add(_footHost); }
        else
        {
            // Arena's tower: one black board under the title, the HP, the rows and the foot on it
            var board = new Grid();
            board.Children.Add(new TacSketch(DrawBoard));
            var on = new StackPanel();
            on.Children.Add(_hpHost); on.Children.Add(_rowsHost); on.Children.Add(_footHost);
            board.Children.Add(on);
            parts.Children.Add(board);
            parts.Effect = Glow(14, 3, .6);
        }
        _fightGrid.Children.Add(parts); _fightGrid.Children.Add(_fightFx);
        _fight.Children.Add(_fightGrid);
        _body.Children.Add(_fight); _body.Children.Add(_runTab); _body.Children.Add(_bugHost); _body.Children.Add(_under);
        _root.Children.Add(_body);
        System.Windows.Documents.TextElement.SetFontFamily(_root, Face);
    }

    public override FrameworkElement Root => _root;
    public override Thickness Inset => _root.Margin;
    public override double Corner => 0;
    public override FrameworkElement? FightFrame => _fight.Visibility == Visibility.Visible ? _fightGrid : null;

    double InkA => 0.6 + 0.4 * _opacity;
    Color Ink(double k = 1) => Tac.A(InkC0, InkA * k);

    public override void SetOpacity(double opacity)
    {
        _opacity = Math.Clamp(opacity, 0.3, 1);
        // the dark blocks take the setting: everything drawn again (it changes seldom)
        if (Figs is { } f) DrawLine(f);
        if (_lastFight != null) Redraw();
    }
    void Redraw() => DrawFight(_lastFight, _lastNow);

    protected override string LineSig(Figures f) => base.LineSig(f) + "|" + (Rare(Values)?.Id ?? "") + "|" + StripSig(StripLook) + "|" + RunSig;

    /// <summary>The pickup strip as the bug wears it: Arena's on an ink block, Studio's on a light card (HudScorebug.Studio.cs); the star yellow.</summary>
    PickStrip.Look StripLook => _studio ? StudioStrip : ArenaStrip;
    static readonly PickStrip.Look ArenaStrip = new(Size: 24, Gap: 4, Rule: Frozen(Color.FromArgb(0x55, 0xFF, 0xFF, 0xFF)), Star: Frozen(YellowC));

    FrameworkElement? _lootRow;

    /// <summary>The pickups' row gives way while a ticker or lower third (<paramref name="covered"/>, 0 to 1) is over it, so no tile shows cut under its edge.</summary>
    void LootUnder(double covered) { if (_lootRow != null) _lootRow.Opacity = 1 - Math.Clamp(covered, 0, 1); }

    /// <summary>The pickups under the bug, a slanted block of the bug's own, as the Abyss's stats strip hangs there.</summary>
    FrameworkElement LootRow(FrameworkElement strip)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(Lean, 3, 0, 0) };
        if (_studio) Row(row, new Block(LightC, strip, 34, new Thickness(13, 0, 14, 0)));
        else Row(row, Ink(strip, 34, new Thickness(13, 0, 14, 0)));
        return row;
    }

    // ================================================================ blocks

    /// <summary>A slanted block: its shape drawn in its colour, its content on it; <see cref="Fill"/> can change (the ink blocks follow the opacity).</summary>
    sealed class Block
    {
        public readonly Grid Root = new() { VerticalAlignment = VerticalAlignment.Top };
        public readonly TacSketch Art;
        public Color Fill;
        public double Sweep = -1;
        public Block(Color fill, UIElement child, double height, Thickness pad, bool leanLeft = true, bool leanRight = true)
        {
            Fill = fill;
            Root.Tag = this;
            Art = new TacSketch((dc, s) =>
            {
                var r = new Rect(0, 0, s.Width, s.Height);
                var g = Shape(r, leanLeft, leanRight);
                // a dark hairline round every block: a light one keeps its edge on a pale sky
                dc.DrawGeometry(Frozen(Fill), Tac.P(Frozen(Color.FromArgb(0x99, 0x0B, 0x0D, 0x12)), 1), g);
                // a fine light along the top edge: the block reads as a solid plate, not a flat fill
                dc.PushClip(g);
                dc.DrawRectangle(Tac.Grad(true, (Color.FromArgb(0x30, 0xFF, 0xFF, 0xFF), 0), (Color.FromArgb(0, 0xFF, 0xFF, 0xFF), .45)), null, r);
                if (Sweep is > 0 and < 1)
                {
                    // the light sweep: a slanted bright band crossing the block
                    double x = -40 + (s.Width + 80) * Sweep;
                    var band = Tac.Lean(new Rect(x - 18, 0, 36, s.Height), 12);
                    dc.DrawGeometry(Tac.Grad(false, (Color.FromArgb(0, 0xFF, 0xFF, 0xFF), 0), (Color.FromArgb(0xC8, 0xFF, 0xFF, 0xFF), .5), (Color.FromArgb(0, 0xFF, 0xFF, 0xFF), 1)), null, band);
                }
                dc.Pop();
            });
            Root.Height = height;
            Root.Children.Add(Art);
            var holder = new Border { Padding = pad, Child = child, VerticalAlignment = VerticalAlignment.Center };
            Root.Children.Add(holder);
        }
        static Geometry Shape(Rect r, bool l, bool rt)
        {
            var g = new StreamGeometry();
            using (var x = g.Open())
            {
                x.BeginFigure(new Point(r.Left + (l ? Lean : 0), r.Top), true, true);
                x.LineTo(new Point(r.Right, r.Top), true, false);
                x.LineTo(new Point(r.Right - (rt ? Lean : 0), r.Bottom), true, false);
                x.LineTo(new Point(r.Left, r.Bottom), true, false);
            }
            g.Freeze();
            return g;
        }
    }

    Block Ink(UIElement child, double h, Thickness pad, bool l = true, bool r = true) => new(Ink(), child, h, pad, l, r);

    /// <summary>Blocks in a row, a fine seam between them (each one's slant meets the next).</summary>
    static void Row(Panel row, Block b) { if (row.Children.Count > 0) b.Root.Margin = new Thickness(-Lean + 2.5, 0, 0, 0); row.Children.Add(b.Root); }

    static TextBlock T(string s, double size, Brush fg, FontWeight? w = null) => Tt(s, size, fg, Face, w ?? FontWeights.Bold, Tight);

    /// <summary>A small label over a figure (the bug's captions): caps, spaced out a little by the face's own width.</summary>
    static TextBlock Cap(string s, Brush fg, double size = 9.5) { var t = Tt(s.ToUpperInvariant(), size, fg, Face, FontWeights.SemiBold, FontStretches.Normal); return t; }

    // ================================================================ the bug

    protected override void DrawLine(Figures f)
    {
        _bug.Children.Clear();
        _bestBlock = _timerBlock = null;
        // in a dungeon run: the bug as the run's clock, its tab and splits (Ui/Themes/Tactical/HudScorebug.Run.cs)
        _runTab.Children.Clear(); _runTab.Visibility = Visibility.Collapsed;
        if (RunNow is { } rs) { DrawRun(rs, f); return; }
        bool mark = f.Abyss && f.Counted && f.Icon.Length > 0;
        // the red block: the share of your best (or the Abyss's mark)
        if (mark)
        {
            var m = AbyssMark(f.Icon, 30);
            var rb = new Block(RedC, m, BugH, new Thickness(12, 0, 10, 0), leanLeft: false);
            Row(_bug, rb);
        }
        else if (HasRing(f))
        {
            var sp = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            var pct = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };
            pct.Children.Add(T(f.Beat ? "★" : Pct(f.Share), 21, White));
            if (!f.Beat) { var pc = T("%", 12, White); pc.VerticalAlignment = VerticalAlignment.Bottom; pc.Margin = new Thickness(1, 0, 0, 3); pct.Children.Add(pc); }
            sp.Children.Add(pct);
            var gap = Gap(f);
            var cap = Cap(gap.Length > 0 ? (f.Beat ? L.T("New best") : gap.TrimStart('−') + " " + L.T("to best")) : L.T("Best"), Frozen(Color.FromArgb(0xF2, 0xFF, 0xFF, 0xFF)), 10.5);
            cap.HorizontalAlignment = HorizontalAlignment.Center; cap.Margin = new Thickness(0, -3, 0, 0);
            sp.Children.Add(cap);
            var rb = new Block(RedC, sp, BugH, new Thickness(12, 0, 12, 0), leanLeft: false);
            Row(_bug, rb);
            _bestBlock = rb.Root;
        }
        else
        {
            // no best here: a narrow red tab so the bug keeps its colour key
            var idle = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            var dash = T("—%", 19, Frozen(Color.FromArgb(0x80, 0xFF, 0xFF, 0xFF))); dash.HorizontalAlignment = HorizontalAlignment.Center;
            var bc = Cap(L.T("Best"), Frozen(Color.FromArgb(0xB0, 0xFF, 0xFF, 0xFF)), 9.5); bc.HorizontalAlignment = HorizontalAlignment.Center; bc.Margin = new Thickness(0, -3, 0, 0);
            idle.Children.Add(dash); idle.Children.Add(bc);
            var rb = new Block(Color.FromRgb(0xB8, 0x1E, 0x2E), idle, BugH, new Thickness(12, 0, 12, 0), leanLeft: false);
            Row(_bug, rb);
        }
        // the ink block: the hour, huge, in white
        if (!HasHour(f))
        {
            var w = T(Waiting(f.Abyss), 14, Grey, FontWeights.SemiBold);
            Row(_bug, Ink(w, BugH, new Thickness(14, 0, 16, 0)));
        }
        else
        {
            var hr = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            var rate = T(f.Rate, f.Warm ? 17 : 32, f.Warm ? Grey : White, f.Warm ? FontWeights.SemiBold : FontWeights.Bold);
            if (!f.Warm) rate.Margin = new Thickness(0, -2, 0, 0);
            hr.Children.Add(rate);
            if (f.Unit.Length > 0) { var u = T(f.Unit, 14, Grey, FontWeights.SemiBold); u.VerticalAlignment = VerticalAlignment.Bottom; u.Margin = new Thickness(3, 0, 0, 7); hr.Children.Add(u); }
            Row(_bug, Ink(hr, BugH, new Thickness(14, 0, 14, 0)));
            // the light block: the session
            if (f.Total.Length > 0)
            {
                var ses = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
                ses.Children.Add(Cap(f.Abyss ? f.TotalWord : L.T("Session"), Sub));
                var tv = T(f.Total, 18, InkB); tv.Margin = new Thickness(0, -3, 0, 0);
                ses.Children.Add(tv);
                Row(_bug, new Block(LightC, ses, BugH, new Thickness(13, 0, 13, 0)));
            }
        }
        DrawTimerBlock();
        DrawUnder(f);
    }

    /// <summary>The next timer as the bug's last block: green while its entry is open, orange when soon, slate while it waits.</summary>
    void DrawTimerBlock()
    {
        if (_timerBlock != null) { _bug.Children.Remove(_timerBlock); _timerBlock = null; }
        if (KinahHidden || _timer is not { } t) return;
        var host = TimerPlate(t.Text, t.Tone, BugH, _timerMore);
        Row(_bug, host);
        _timerBlock = host.Root;
    }

    Block TimerPlate(string text, TimerBadge.Tone tone, double h, string more)
    {
        var (bg, fg) = tone switch { TimerBadge.Tone.On => (GreenC, InkC0), TimerBadge.Tone.Soon => (OrangeC, InkC0), _ => (SlateC, Colors.White) };
        var sp = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        sp.Children.Add(T(text, h > 34 ? 15 : 13, Frozen(fg)));
        if (more.Length > 0 && h > 34) { var m = Tt(more, 10, Frozen(Tac.A(fg, .78)), Face, FontWeights.SemiBold, FontStretches.Normal); m.Margin = new Thickness(0, -2, 0, 0); sp.Children.Add(m); }
        var b = new Block(bg, sp, h, new Thickness(13, 0, 13, 0), leanRight: true);
        if (_timerEdge.Parent is Panel old) old.Children.Remove(_timerEdge);
        b.Root.Children.Add(_timerEdge);
        return b;
    }

    void DrawTimerEdge(DrawingContext dc, Size s)
    {
        if (_timerPulse is < 0 or >= 1) return;
        // two pulses: the block's edge lights and rings outward, each fading as it grows
        for (int k = 0; k < 2; k++)
        {
            double p = Math.Clamp(_timerPulse * 2 - k * .55, 0, 1);
            if (p <= 0 || p >= 1) continue;
            // wide to the side, shallow up and down: the bug sits 4 px under the window's top, the ring stays inside it
            double g = 9 * Out(p);
            var r = new Rect(-g, -g * .25, s.Width + 2 * g, s.Height + .5 * g);
            var shape = Tac.Lean(r, Lean + g * .2);
            dc.DrawGeometry(null, Tac.P(Frozen(Tac.A(GreenC, (1 - p) * .95)), 2.5 * (1 - p) + 1), shape);
        }
    }

    /// <summary>Under the bug: the rare drop's lower third, the Abyss's stats strip, the note.</summary>
    void DrawUnder(Figures f)
    {
        _under.Children.Clear();
        _rareBanner = null;
        if (KinahHidden) return;
        if (Rare(Values) is { } r) { _rareBanner = RareThird(r); _rareBanner.Margin = new Thickness(0, 4, 0, 0); _under.Children.Add(_rareBanner); }
        var loot = FarmStrip(f, StripLook);
        _lootRow = loot != null ? LootRow(loot) : null;
        if (_lootRow != null) _under.Children.Add(_lootRow);
        // the pickup's ticker and the NEW PERSONAL BEST lower third hang from the bug over the pickups' row, not under it
        Panel.SetZIndex(_bugHost, loot != null ? 1 : 0);
        if (f.Stats.Count > 0)
        {
            var strip = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(Lean, 3, 0, 0) };
            foreach (var (fig, word, tone) in f.Stats)
            {
                var sp = new StackPanel { Orientation = Orientation.Horizontal };
                sp.Children.Add(T(fig, 14, ReferenceEquals(tone, Rec) ? Frozen(Color.FromRgb(0xFF, 0x6B, 0x78)) : ReferenceEquals(tone, Gold) ? Yellow : White));
                var w = T(word, 12, Grey, FontWeights.SemiBold); w.Margin = new Thickness(4, 1, 0, 0); sp.Children.Add(w);
                Row(strip, Ink(sp, 24, new Thickness(11, 0, 11, 0)));
            }
            _under.Children.Add(strip);
        }
        if (NoteRow(f) is { } nr) _under.Children.Add(nr);
    }

    /// <summary>The note under the bug: a bar in its tone, the words on a light block; null with no note.</summary>
    StackPanel? NoteRow(Figures f)
    {
        if (f.Note.Length == 0) return null;
        bool warn = ReferenceEquals(f.NoteTone, Warn) || ReferenceEquals(f.NoteTone, Gold);
        var nt = T(f.Note, 12.5, InkB, FontWeights.SemiBold);
        var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(Lean, 3, 0, 0) };
        Row(row, new Block(warn ? YellowC : GreenC, new Border { Width = 4 }, 22, new Thickness(4, 0, 4, 0)));
        Row(row, new Block(LightC, nt, 22, new Thickness(11, 0, 12, 0)));
        return row;
    }

    /// <summary>The rare drop's lower third: the tile on a block in its grade's colour, RARE DROP and the name on dark.</summary>
    FrameworkElement RareThird(Pick r)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        var c = ColorOf(r.Color, r.Grade);
        var tile = Tile(r, 30);
        Row(row, new Block(c, tile, 40, new Thickness(12, 0, 12, 0), leanLeft: false));
        var words = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        words.Children.Add(Cap(L.T("Rare drop"), Yellow, 10));
        var nm = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, -2, 0, 0) };
        nm.Children.Add(T(r.Name, 17, White));
        if (r.Count.Length > 0) { var cn = T(r.Count, 13, Grey, FontWeights.SemiBold); cn.Margin = new Thickness(6, 2, 0, 0); nm.Children.Add(cn); }
        words.Children.Add(nm);
        Row(row, Ink(words, 40, new Thickness(14, 0, 18, 0)));
        return row;
    }

    protected override void KinahShown(bool shown)
    {
        _bugHost.Visibility = shown ? Visibility.Visible : Visibility.Collapsed;
        _under.Visibility = shown ? Visibility.Visible : Visibility.Collapsed;
        _fight.Margin = new Thickness(0, 0, 0, shown ? 6 : 0);
        _root.Margin = new Thickness(4, 4, 12, shown ? 35 : 4);
    }

    // ================================================================ the timers

    protected override void DrawTimers(IReadOnlyList<TimerBoard.Item> due, DateTime now)
    {
        _timer = due.FirstOrDefault() is { } i ? TimerWords(i, now) : null;
        // the ones after it, small under it: "Invasion 4:39 · Kashapa 11:59"
        _timerMore = string.Join("  ·  ", due.Skip(1).Take(2).Select(x => TimerWords(x, now).Text));
        // in a run the bug is fitted again with the timer's block in it (its parts give way in turn)
        if (RunNow != null && Figs is { } f) DrawLine(f);
        else DrawTimerBlock();
        if (_lastFight != null && KinahHidden) Redraw();
    }

    // ================================================================ the leaderboard (Arena's video board, or Studio's cards)

    protected override void DrawFight(Encounter? e, DateTime now)
    {
        StripFollowsFight(e);
        _titleHost.Child = _hpHost.Child = _rowsHost.Child = _footHost.Child = null;
        if (e == null) { _fight.Visibility = Visibility.Collapsed; _fightFx.Children.Clear(); return; }
        double w = FightWidth - 10;
        _fightGrid.Width = w;
        _titleHost.Child = _studio ? StudioTitle(e, now) : ArenaTitle(e, now);
        if (!e.Waiting && HpOf(e) is { } hp) _hpHost.Child = _studio ? StudioHp(e, hp, now) : ArenaHp(e, hp, now);
        var rows = new Grid { Margin = _studio ? new Thickness(0, 5, 0, 0) : new Thickness(6, 6, 6, 1) };
        if (e.Waiting)
        {
            var panel = new StackPanel();
            panel.Children.Add(_studio ? StudioReady(e) : ArenaReady(e, PartyBeforeHit(e)));
            if (PartyBeforeHit(e)) { panel.Children.Add(rows); PutRows(rows, e, now, w - rows.Margin.Left - rows.Margin.Right); }
            _rowsHost.Child = panel;
        }
        else
        {
            PutRows(rows, e, now, w - rows.Margin.Left - rows.Margin.Right);
            _rowsHost.Child = rows;
            if (e.HasDamage) _footHost.Child = AsFoot(_studio ? StudioFoot(e, now) : ArenaFoot(e, now), e, now);
        }
        _fight.Visibility = Visibility.Visible;
        ApplyEnter();
    }

    /// <summary>Arena's board: near-black, a faint LED dot grid, a soft light from above, a fine rim.</summary>
    static void DrawBoard(DrawingContext dc, Size s)
    {
        if (s.Width < 1 || s.Height < 1) return;
        dc.DrawRectangle(Tac.Grad(true, (Tac.A(Color.FromRgb(0x0D, 0x0F, 0x14), .96), 0), (Tac.A(Color.FromRgb(0x05, 0x06, 0x08), .96), 1)), null, new Rect(s));
        var dot = Frozen(Tac.A(Colors.White, .035));
        for (double y = 2; y < s.Height; y += 4) for (double x = 2; x < s.Width; x += 4) dc.DrawRectangle(dot, null, new Rect(x, y, 1, 1));
        dc.DrawRectangle(Tac.Grad(true, (Tac.A(Colors.White, .06), 0), (Tac.A(Colors.White, 0), 1)), null, new Rect(0, 0, s.Width, Math.Min(60, s.Height)));
        dc.DrawRectangle(null, Tac.P(Frozen(BoardLine), 1), new Rect(.5, -.5, s.Width - 1, s.Height));
    }

    /// <summary>Arena's top: the boss in capitals on a red slanted block, the clock in glowing amber digits on black (the outcome glowing before it; in a dungeon the next timer after it).</summary>
    FrameworkElement ArenaTitle(Encounter e, DateTime now)
    {
        var head = new Grid { Height = 34 };
        head.ColumnDefinitions.Add(new ColumnDefinition()); head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var title = C(Up(e.Title), 15, Colors.White, FontWeights.Bold); title.TextTrimming = TextTrimming.CharacterEllipsis; title.Effect = Glow(3, 1, .6);
        head.Children.Add(GBlock(RedC, title, 34, new Thickness(12, 0, 14, 1), 0, 10, .22));
        var cl = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        if (OutcomeText(e) is { Length: > 0 } ow)
        {
            var oc = e.Outcome == "wipe" ? Coral : GreenC;
            var o = C(Up(ow), 11.5, oc, FontWeights.Bold, Semi); o.Margin = new Thickness(0, 1, 9, 0); o.Effect = Glow(8, 0, .8, oc); cl.Children.Add(o);
        }
        var ct = e.Waiting ? C(L.T("Ready"), 14, GreyOf, FontWeights.SemiBold) : C(Clock(e, now), 21, Amber, FontWeights.Bold);
        if (!e.Waiting) ct.Effect = Glow(10, 0, .75, Amber);
        cl.Children.Add(ct);
        bool timer = KinahHidden && _timer is not null;
        var cc = GCell((dc, s) => Gloss(dc, Slant(new Rect(s), 10, timer ? 7 : 0), Color.FromRgb(0x16, 0x18, 0x1E), Color.FromRgb(0x05, 0x06, 0x08), .1, .2), cl, 34, new Thickness(18, 0, timer ? 16 : 12, 0));
        cc.Margin = new Thickness(-8, 0, 0, 0);
        if (ct.Text.Length > 0 || cl.Children.Count > 1) { Grid.SetColumn(cc, 1); head.Children.Add(cc); }   // the PvP panel has no clock
        if (KinahHidden && _timer is { } t)
        {
            var tb = TimerPlate(t.Text, t.Tone, 34, "");
            tb.Root.Margin = new Thickness(-Lean + 2.5, 0, 0, 0);
            Grid.SetColumn(tb.Root, 2); head.Children.Add(tb.Root);
        }
        return head;
    }

    /// <summary>Arena's HP: red LED segments glowing on the board, ticks at its thirds, "47%" and what's left.</summary>
    FrameworkElement ArenaHp(Encounter e, double hp, DateTime now)
    {
        var g = new Grid { Height = 24, Margin = new Thickness(10, 6, 10, 0) };
        g.ColumnDefinitions.Add(new ColumnDefinition()); g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var track = new Grid { Height = 8, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0) };
        track.Children.Add(new TacSketch((dc, s) => Leds(dc, new Rect(0, 0, s.Width, s.Height), hp, RedC, 3, 1.4)) { Effect = Glow(8, 0, .7, RedC) });
        // the thirds: a white tick above and below the segments
        track.Children.Add(new TacSketch((dc, s) =>
        {
            for (int k = 1; k <= 2; k++)
            {
                double x = Math.Round(s.Width * k / 3);
                dc.DrawRectangle(Frozen(Tac.A(Colors.White, .85)), null, new Rect(x - .75, -4, 1.5, 3));
                dc.DrawRectangle(Frozen(Tac.A(Colors.White, .85)), null, new Rect(x - .75, s.Height + 1, 1.5, 3));
            }
        }) { Tag = "hp-ticks" });
        HpGhost(track, e, hp, now, 0);
        g.Children.Add(track);
        var words = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        words.Children.Add(C($"{Math.Round(hp * 100):0}%", 15, hp < .34 ? Coral : Colors.White, FontWeights.Bold));
        if (HpLeftText(e) is { Length: > 0 } left) words.Children.Add(C("  " + left, 12.5, GreyOf, FontWeights.SemiBold, Semi));
        Grid.SetColumn(words, 1); g.Children.Add(words);
        return g;
    }

    /// <summary>Arena before the first pull: the ready line in grey capitals on the board.</summary>
    static FrameworkElement ArenaReady(Encounter e, bool party)
    {
        var wt = C(Up(e.ReadyText), 13, GreyOf, FontWeights.SemiBold, Semi);
        wt.Margin = new Thickness(12, 8, 12, party ? 2 : 10); wt.TextTrimming = TextTrimming.CharacterEllipsis;
        return wt;
    }

    /// <summary>Arena's foot on the board: TOTAL and the fight's figure, the group's word and its figure in glowing amber, the AVG. DPS badge.</summary>
    static FrameworkElement ArenaFoot(Encounter e, DateTime now)
    {
        var (total, word, group, held) = FootOf(e, now);
        var fg = new Grid { Height = 30 };
        fg.ColumnDefinitions.Add(new ColumnDefinition()); fg.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var bg = new TacSketch((dc, s) =>
        {
            dc.DrawRectangle(Tac.Grad(true, (Color.FromRgb(0x15, 0x17, 0x1D), 0), (Color.FromRgb(0x0B, 0x0C, 0x10), 1)), null, new Rect(s));
            dc.DrawRectangle(Frozen(BoardLine), null, new Rect(0, 0, s.Width, 1));
        });
        Grid.SetColumnSpan(bg, 2); fg.Children.Add(bg);
        var tl = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 0) };
        tl.Children.Add(C(Up(L.T("Total")), 10.5, GreyOf, FontWeights.Bold, Semi));
        var tv = C(total, 17, Colors.White, FontWeights.Bold); tv.Margin = new Thickness(8, 0, 0, 0); tl.Children.Add(tv);
        EnergyMark.After(tl, 12.5, Frozen(GreyOf));   // Odyle Energy (Ui/EnergyMark.cs)
        fg.Children.Add(tl);
        var rt = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 12, 0) };
        if (word.Length > 0) { rt.Children.Add(C(Up(word), 10.5, GreyOf, FontWeights.Bold, Semi)); var gv = C(group, 17, Amber, FontWeights.Bold); gv.Margin = new Thickness(8, 0, 0, 0); gv.Effect = Glow(8, 0, .6, Amber); rt.Children.Add(gv); }
        if (held)
            rt.Children.Add(new Border { Background = Frozen(GreenC), Padding = new Thickness(5, 1, 5, 2), Margin = new Thickness(9, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center,
                Tag = "avg-badge", Child = C(Up(L.T(Encounter.AvgBadge)), 10, InkC0, FontWeights.Bold, Semi) });
        Grid.SetColumn(rt, 1); fg.Children.Add(rt);
        return fg;
    }

    /// <summary>Arena's line between the party and you pinned below it: "▼ 1 more" on a fine rule.</summary>
    static FrameworkElement ArenaGap(int left)
    {
        var g = new Grid { VerticalAlignment = VerticalAlignment.Center };
        g.Children.Add(new Border { Height = 1, Background = Frozen(BoardLine), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(30, 0, 30, 0) });
        var t = C(left > 0 ? "▼  " + L.F("{0} more", left) : "▼", 10.5, GreyOf, FontWeights.SemiBold, Semi);
        g.Children.Add(new Border { Background = Frozen(Color.FromRgb(0x09, 0x0A, 0x0E)), Padding = new Thickness(10, 0, 10, 1), HorizontalAlignment = HorizontalAlignment.Center, Child = t });
        return g;
    }

    /// <summary>The fight's parts as far into their broadcast entrance as <see cref="_enter"/> says (1: all in).</summary>
    void ApplyEnter()
    {
        // the title and the HP wipe in behind a slanted edge (short words: fast), the rows and the foot fade up after them,
        // so no figure is ever shown cut by the wipe
        double tw = Out(Seg(_enter, 0, .16)), hw = Out(Seg(_enter, .06, .24));
        void W(Border b, double p) { if (p >= 1) b.ClearValue(UIElement.ClipProperty); else b.Clip = Wipe(0, (FightWidth + 30) * p, 60); }
        W(_titleHost, tw); W(_hpHost, hw);
        // the board's ground fades up, then each row wipes in after the one above it (its words on once it is in)
        _rowsHost.Opacity = Seg(_enter, .2, .3);
        if (Meter is { } m)
            for (int i = 0; i < m.Members.Count; i++)
                if (m.RowOf(m.Members[i].Id) is BoardRow r) { r.Enter = Out(Seg(_enter, .2 + i * .08, .44 + i * .08)); r.Apply(); }
        _footHost.Clip = _enter >= 1 ? null : Wipe(0, (FightWidth + 30) * Out(Seg(_enter, .55, .8)), 60);
    }

    // ================================================================ the moments

    /// <summary>A pickup: a yellow ticker wipes in under the bug from the left, holds, and wipes out to the right.</summary>
    protected override void Pickup(Pick p, bool held)
    {
        if (KinahHidden || _bugHost.Visibility != Visibility.Visible) return;
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        var worth = p.Worth.Length > 0 ? p.Worth : p.Count;
        var yb = new Block(YellowC, T(worth, 17, InkB), 26, new Thickness(12, 0, 12, 0), leanLeft: false);
        Row(row, yb);
        Row(row, Ink(T(p.Name, 13.5, White, FontWeights.SemiBold), 26, new Thickness(11, 0, 13, 0)));
        Canvas.SetLeft(row, 0); Canvas.SetTop(row, 3);
        row.Clip = Geometry.Empty;
        _bugAnchor.Children.Clear(); _bugAnchor.Children.Add(row);
        row.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        double w = row.DesiredSize.Width + 12;
        Motion.Play(row, 2000, t =>
        {
            double a = Out(Seg(t, 0, .26)), b = Seg(t, .84, 1); b = b * b;
            row.Clip = Wipe(w * b, w * a, 30);
            LootUnder(Seg(t, 0, .12) * (1 - Seg(t, .9, 1)));
            yb.Sweep = Seg(t, .22, .62); yb.Art.InvalidateVisual();
        }, () => { _bugAnchor.Children.Remove(row); LootUnder(0); });
    }

    /// <summary>A rare drop: its lower third's two blocks wipe in one after the other and a light sweeps across the tile's block.</summary>
    protected override void RareDrop(Pick p, bool held)
    {
        if (_rareBanner is not Panel row || row.Children.Count < 2) return;
        var first = (Grid)row.Children[0]; var second = (Grid)row.Children[1];
        Motion.Play(row, 1600, t =>
        {
            double w1 = first.ActualWidth > 0 ? first.ActualWidth : 60, w2 = second.ActualWidth > 0 ? second.ActualWidth + Lean : 260;
            first.Clip = Wipe(-20, (w1 + 20) * Out(Seg(t, 0, .22)), 44);
            second.Clip = Wipe(-20, (w2 + 20) * Out(Seg(t, .12, .42)), 44);
            // the words come on once their plate is in (never cut by the wipe)
            if (second.Children.Count > 1) second.Children[1].Opacity = t < .4 ? 0 : 1;
            SweepOf(first, Seg(t, .3, .65)); SweepOf(second, Seg(t, .38, .78));
        }, () => { first.ClearValue(UIElement.ClipProperty); second.ClearValue(UIElement.ClipProperty); if (second.Children.Count > 1) second.Children[1].Opacity = 1; SweepOf(first, -1); SweepOf(second, -1); });
    }

    /// <summary>A wipe's window from <paramref name="x0"/> to <paramref name="x1"/>, its edges slanted as the blocks are (never a flat cut).</summary>
    static Geometry Wipe(double x0, double x1, double h)
    {
        double s = (h + 4) * .1763;
        if (x1 <= x0) return Geometry.Empty;
        return Tac.Lean(new Rect(x0 - s, -2, x1 - x0 + s, h + 4), s);
    }

    /// <summary>The light sweep across a block (its root's Block), at 0..1; -1 none.</summary>
    static void SweepOf(Grid root, double at)
    {
        if (root.Tag is not Block b) return;
        b.Sweep = at; b.Art.InvalidateVisual();
    }

    /// <summary>Your best passed: a NEW PERSONAL BEST lower third wipes in under the bug, a light sweeping it; the red block flashes.</summary>
    protected override void BestPassed(bool held)
    {
        if (KinahHidden || _bugHost.Visibility != Visibility.Visible) return;
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        var star = new Block(YellowC, T("★", 16, InkB), 30, new Thickness(10, 0, 10, 0), leanLeft: false);
        var words = new Block(RedC, T(L.T("NEW PERSONAL BEST"), 18, White), 30, new Thickness(12, 0, 16, 0));
        Row(row, star); Row(row, words);
        Canvas.SetLeft(row, 0); Canvas.SetTop(row, 3);
        row.Clip = Geometry.Empty;
        _bugAnchor.Children.Clear(); _bugAnchor.Children.Add(row);
        row.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        double w = row.DesiredSize.Width + 12;
        Motion.Play(row, 2600, t =>
        {
            double i = Out(Seg(t, 0, .2)), o = Seg(t, .86, 1); o = o * o;
            row.Clip = Wipe(w * o, w * i, 34);
            LootUnder(Seg(t, 0, .1) * (1 - Seg(t, .92, 1)));
            words.Sweep = Seg(t, .16, .5); words.Art.InvalidateVisual();
            star.Sweep = Seg(t, .1, .36); star.Art.InvalidateVisual();
        }, () => { _bugAnchor.Children.Remove(row); LootUnder(0); });
        if (_bestBlock is Grid bg)
        {
            // the red block's own sweep, a beat after the lower third's
            Motion.Play(bg, 1400, t => SweepOf(bg, Seg(t, .2, .8)), () => SweepOf(bg, -1));
        }
    }

    /// <summary>An entry opened: the green block's edge pulses outward twice.</summary>
    protected override void EntryOpened(TimerBoard.Item i, bool held)
    {
        Motion.Play(_timerEdge, 1500, t => { _timerPulse = t; _timerEdge.InvalidateVisual(); }, () => { _timerPulse = -1; _timerEdge.InvalidateVisual(); });
    }

    /// <summary>A fight started: the leaderboard's blocks slide in one after another, as a broadcast's transition.</summary>
    protected override void FightStarted(Encounter e, bool held)
    {
        Motion.Play(_fightGrid, 1100, t => { _enter = t; ApplyEnter(); }, () => { _enter = 1; ApplyEnter(); });
    }

    /// <summary>A fight ended: a VICTORY plate (DEFEAT for a wipe) wipes in over the leaderboard, a light crosses it, it goes.</summary>
    protected override void FightEnded(Encounter e, bool held)
    {
        if (e.Outcome is not ("kill" or "wipe" or "cleared")) return;
        bool wipe = e.Outcome == "wipe";
        // the leaderboard dims under a full-width band, and the plate wipes in across it
        // the winner's row stays in sight above the band, a gold glint crossing it; the band sits over the rest of the board
        // (the winner's row: under the title and the HP, the first row's pitch; the band above the foot)
        double keepTop = _titleHost.ActualHeight + _hpHost.ActualHeight + (_studio ? 5 : 7) + (_studio ? StudioRow.Pitch : ArenaRow.Pitch), footH = _footHost.ActualHeight + 8;
        var dim = new Border { Background = Frozen(Tac.A(InkC0, .78)), Opacity = 0, Margin = new Thickness(0, keepTop, 0, 0) };
        var band = new Border { Height = 64, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 0, footH), Background = Frozen(Tac.A(InkC0, .9)), BorderBrush = wipe ? Red : Yellow, BorderThickness = new Thickness(0, 2, 0, 2), Opacity = 0 };
        var plate = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 0, footH + 8) };
        double plateH = 48;
        if (_studio)
        {
            // Studio (art review, 2026-11-20): the band and the plate seated on the second row's card (the first, alone), a 2 px
            // bleed each side and the band opaque, so nothing of that row shows; nothing else dims, the winner, the rows after
            // it and the foot read at full
            plateH = StudioRow.H + 4;
            double rowTop = keepTop;
            if (Meter is { Members.Count: > 0 } ms && ms.RowOf(ms.Members[Math.Min(1, ms.Members.Count - 1)].Id)?.Root is FrameworkElement r2 && r2.IsDescendantOf(_fightGrid))
                rowTop = r2.TranslatePoint(new Point(0, 0), _fightGrid).Y;
            double bandH = StudioRow.H + 4, mid = rowTop - 2 + bandH / 2;
            dim.Visibility = Visibility.Collapsed;
            band.Height = bandH; band.VerticalAlignment = VerticalAlignment.Top; band.Margin = new Thickness(0, rowTop - 2, 0, 0); band.Background = Frozen(InkC0);
            plate.VerticalAlignment = VerticalAlignment.Top; plate.Margin = new Thickness(0, mid - plateH / 2, 0, 0);
        }
        var winner = !wipe && Meter is { Members.Count: > 0 } mm ? mm.RowOf(mm.Members[0].Id) as BoardRow : null;
        var head = new Block(wipe ? SlateC : YellowC, T(wipe ? "✖" : "★", _studio ? 16 : 22, wipe ? Red : InkB), plateH, _studio ? new Thickness(11, 0, 9, 0) : new Thickness(14, 0, 12, 0), leanLeft: true);
        var main = new Block(wipe ? RedC : InkC0, T(L.T(wipe ? "DEFEAT" : "VICTORY"), _studio ? 21 : 34, wipe ? White : Yellow), plateH, _studio ? new Thickness(14, 0, 17, 0) : new Thickness(18, 0, 22, 0));
        Row(plate, head); Row(plate, main);
        plate.Clip = Geometry.Empty;
        _fightFx.Children.Clear(); _fightFx.Children.Add(dim); _fightFx.Children.Add(band); _fightFx.Children.Add(plate);
        plate.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        double w = plate.DesiredSize.Width + 16;
        Motion.Play(_fightFx, 2600, t =>
        {
            double i = Out(Seg(t, .06, .22)), o = Seg(t, .84, 1); o = o * o;
            plate.Clip = Wipe(w * o, w * i, plateH + 4);
            dim.Opacity = band.Opacity = t < .84 ? Out(Seg(t, 0, .1)) : 1 - Seg(t, .84, 1);
            // a win glints; a loss slams in with a red flash and no shine
            if (!wipe) { main.Sweep = Seg(t, .24, .6); main.Art.InvalidateVisual(); }
            else
            {
                plate.RenderTransform = new TranslateTransform(6 * (1 - Out(Seg(t, .06, .2))), 0);
                band.Background = Frozen(Tac.A(Tac.Mix(InkC0, RedC, t > .08 && t < .2 ? .6 : 0), _studio ? 1 : .9));
            }
            if (winner != null) { winner.Sweep = Seg(t, .04, .4); winner.Apply(); }
        }, () => { _fightFx.Children.Clear(); if (winner != null) { winner.Sweep = -1; winner.Apply(); } });
    }
}
