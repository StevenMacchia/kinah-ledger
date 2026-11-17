using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using KinahLedger.Parser;
using KinahLedger.Session;
using static KinahLedger.Ui.HudKit;

namespace KinahLedger.Ui;

/// <summary>
/// Scorebug (the Tactical theme): a tournament broadcast's bug. Solid blocks slanted ten degrees, condensed bold type: a red
/// block with your best's share, a near-black one with the hour huge in white, a light one with the session, a green one
/// for the next timer. A fight is a broadcast leaderboard: the boss on a red title block, its clock in yellow on dark, the
/// HP on a notched bar, the rows on a dark ground with gold, silver and bronze tabs beside the top three, a dark total strip.
/// The moments: a pickup wipes a yellow ticker in under the bug; a rare drop runs a lower third with its tile; your best
/// passed runs a NEW PERSONAL BEST lower third with a light sweep; an entry opening pulses the green block's edge; a
/// fight's start slides its blocks in one after another; its end puts up a VICTORY or DEFEAT plate.
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

    public HudScorebug() : base(bare: false, Face)
    {
        // the leaderboard's own rows (Ui/Themes/ThemeRows.cs): rank tab, team stripe, name plate, stat cells
        UseRows(ScoreRow.Pitch, m => new ScoreRow(), 22, left => GapLine(left));
        _timerEdge = new TacSketch(DrawTimerEdge);
        _bugHost.Children.Add(_bug); _bugHost.Children.Add(_bugAnchor);
        var parts = new StackPanel();
        parts.Children.Add(_titleHost); parts.Children.Add(_hpHost); parts.Children.Add(_rowsHost); parts.Children.Add(_footHost);
        _fightGrid.Children.Add(parts); _fightGrid.Children.Add(_fightFx);
        _fight.Children.Add(_fightGrid);
        _body.Children.Add(_fight); _body.Children.Add(_bugHost); _body.Children.Add(_under);
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

    protected override string LineSig(Figures f) => base.LineSig(f) + "|" + (Rare(Values)?.Id ?? "");

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
        if (f.Note.Length > 0)
        {
            bool warn = ReferenceEquals(f.NoteTone, Warn) || ReferenceEquals(f.NoteTone, Gold);
            var nt = T(f.Note, 12.5, InkB, FontWeights.SemiBold);
            var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(Lean, 3, 0, 0) };
            Row(row, new Block(warn ? YellowC : GreenC, new Border { Width = 4 }, 22, new Thickness(4, 0, 4, 0)));
            Row(row, new Block(LightC, nt, 22, new Thickness(11, 0, 12, 0)));
            _under.Children.Add(row);
        }
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
        DrawTimerBlock();
        if (_lastFight != null && KinahHidden) Redraw();
    }

    // ================================================================ the leaderboard

    protected override void DrawFight(Encounter? e, DateTime now)
    {
        _titleHost.Child = _hpHost.Child = _rowsHost.Child = _footHost.Child = null;
        if (e == null) { _fight.Visibility = Visibility.Collapsed; _fightFx.Children.Clear(); return; }
        double w = FightWidth - 10;
        _fightGrid.Width = w;
        // the title bar: the boss on red, its clock in yellow on dark (in a dungeon the next timer at its end)
        var title = new Grid { Height = 32 };
        title.ColumnDefinitions.Add(new ColumnDefinition()); title.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); title.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var name = T(e.Title, 17, White); name.TextTrimming = TextTrimming.CharacterEllipsis;
        var nb = new Block(RedC, name, 32, new Thickness(12, 0, 14, 0), leanLeft: false);
        title.Children.Add(nb.Root);
        FrameworkElement clockChild;
        if (e.Waiting) clockChild = T(L.T("Ready"), 14, Grey, FontWeights.SemiBold);
        else
        {
            var outcome = OutcomeText(e);
            var cs = new StackPanel { Orientation = Orientation.Horizontal };
            if (outcome.Length > 0) { var ot = Cap(outcome, e.Outcome == "wipe" ? Frozen(Color.FromRgb(0xFF, 0x6B, 0x78)) : Green, 11); ot.FontWeight = FontWeights.Bold; ot.Margin = new Thickness(0, 1, 8, 0); cs.Children.Add(ot); }
            cs.Children.Add(T(Clock(e.Duration(now)), 18, e.Live ? Yellow : White));
            clockChild = cs;
        }
        var cb = Ink(clockChild, 32, new Thickness(13, 0, 13, 0));
        cb.Root.Margin = new Thickness(-Lean + 2.5, 0, 0, 0);
        Grid.SetColumn(cb.Root, 1); title.Children.Add(cb.Root);
        if (KinahHidden && _timer is { } t)
        {
            var tb = TimerPlate(t.Text, t.Tone, 32, "");
            tb.Root.Margin = new Thickness(-Lean + 2.5, 0, 0, 0);
            Grid.SetColumn(tb.Root, 2); title.Children.Add(tb.Root);
        }
        _titleHost.Child = title;
        if (e.Waiting)
        {
            var wt = T(e.ReadyText, 14, Grey, FontWeights.SemiBold);
            var wb = Ink(wt, 34, new Thickness(14, 0, 14, 0), l: false);
            wb.Root.Margin = new Thickness(0, 2, 0, 0);
            if (PartyBeforeHit(e))
            {
                // the party under the ready plate, on the leaderboard's dark ground
                var party = new StackPanel();
                party.Children.Add(wb.Root);
                var rowsGrid = new Grid { Margin = new Thickness(0, 2, 0, 0) };
                rowsGrid.Children.Add(new Border { Background = Frozen(Ink(.96)) });
                var rowsIn = new Grid { Margin = new Thickness(4, 5, 4, 3) };
                rowsGrid.Children.Add(rowsIn);
                PutRows(rowsIn, e, now, w - 8);
                party.Children.Add(rowsGrid);
                _rowsHost.Child = party;
            }
            else _rowsHost.Child = wb.Root;
        }
        else
        {
            // the HP: a red bar with notches at the thirds on dark, "47% · 1.82M left" at its end
            if (HpOf(e) is { } hp)
            {
                var g = new Grid { Margin = new Thickness(0, 0, 0, 0) };
                g.ColumnDefinitions.Add(new ColumnDefinition()); g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                var track = new Grid { Height = 8, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0) };
                track.Children.Add(new TacSketch((dc, s) =>
                {
                    dc.DrawRectangle(Frozen(Color.FromRgb(0x2A, 0x2D, 0x36)), null, new Rect(0, 0, s.Width, s.Height));
                    dc.DrawRectangle(Tac.Grad(false, (Color.FromRgb(0xB8, 0x14, 0x28), 0), (RedC, 1)), null, new Rect(0, 0, s.Width * hp, s.Height));
                    dc.DrawRectangle(Frozen(Color.FromArgb(0x50, 0xFF, 0xFF, 0xFF)), null, new Rect(0, 0, s.Width * hp, 2));
                    // the notches: a third and two thirds, cut through the bar, a tick above and below
                    for (int k = 1; k <= 2; k++)
                    {
                        double x = Math.Round(s.Width * k / 3);
                        dc.DrawRectangle(Frozen(InkC0), null, new Rect(x - 1.5, -3, 3, s.Height + 6));
                        dc.DrawRectangle(Frozen(Color.FromArgb(0xB0, 0xFF, 0xFF, 0xFF)), null, new Rect(x - .5, -3, 1, 2.5));
                    }
                }));
                HpGhost(track, e, hp, now, 0);
                g.Children.Add(track);
                var words = new StackPanel { Orientation = Orientation.Horizontal };
                words.Children.Add(T($"{Math.Round(hp * 100):0}%", 15, hp < .34 ? Frozen(Color.FromRgb(0xFF, 0x6B, 0x78)) : White));
                var left = HpLeftText(e);
                if (left.Length > 0) { var lt = T("  " + left, 13, Grey, FontWeights.SemiBold); lt.Margin = new Thickness(0, 1, 0, 0); words.Children.Add(lt); }
                Grid.SetColumn(words, 1); g.Children.Add(words);
                var hb = Ink(g, 26, new Thickness(12, 0, 12, 0), l: false, r: false);
                hb.Root.Margin = new Thickness(0, 2, 0, 0);
                _hpHost.Child = hb.Root;
            }
            // the leaderboard: each member a broadcast row (rank tab, team stripe, name plate, stat cells) on a dark ground
            var rowsGrid = new Grid { Margin = new Thickness(0, 2, 0, 0) };
            rowsGrid.Children.Add(new Border { Background = Frozen(Ink(.96)) });
            var rowsIn = new Grid { Margin = new Thickness(4, 5, 4, 3) };
            rowsGrid.Children.Add(rowsIn);
            PutRows(rowsIn, e, now, w - 8);
            _rowsHost.Child = rowsGrid;
            if (e.HasDamage) _footHost.Child = Foot(e, now, w);
        }
        _fight.Visibility = Visibility.Visible;
        ApplyEnter();
    }

    /// <summary>
    /// The total strip: TOTAL on red and the fight's figure, a dark run, then the group's word on slate and its figure (the
    /// held fight's average with a green AVG. DPS tab), each a slanted cell as the bug's.
    /// </summary>
    FrameworkElement Foot(Encounter e, DateTime now, double w)
    {
        var (total, word, group, held) = FootOf(e, now);
        var g = new Grid { Margin = new Thickness(0, 2, 0, 0), Height = 28 };
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); g.ColumnDefinitions.Add(new ColumnDefinition()); g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        // TOTAL on red, its figure's cell running on to the group's cells (no dead cell between)
        var tl = new Block(RedC, Cap(L.T("Total"), White, 10.5), 28, new Thickness(12, 0, 11, 0), leanLeft: false);
        g.Children.Add(tl.Root);
        var tv = Ink(T(total, 17, White), 28, new Thickness(11, 0, 12, 0));
        tv.Root.Margin = new Thickness(-Lean + 2.5, 0, 0, 0);
        Grid.SetColumn(tv.Root, 1); g.Children.Add(tv.Root);
        var right = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(-Lean + 2.5, 0, 0, 0) };
        Row(right, new Block(SlateC, Cap(word, Frozen(Color.FromRgb(0xC9, 0xD1, 0xDC)), 10.5), 28, new Thickness(11, 0, 11, 0)));
        Row(right, Ink(T(group, 17, Yellow), 28, new Thickness(11, 0, held ? 11 : 12, 0), r: held));
        if (held)
        {
            var badge = new Block(GreenC, Cap(L.T(Encounter.AvgBadge), InkB, 10), 28, new Thickness(10, 0, 10, 0), leanRight: false);
            badge.Root.Tag = "avg-badge";   // the held average's badge (the Parser's AVG. DPS)
            Row(right, badge);
        }
        Grid.SetColumn(right, 2); g.Children.Add(right);
        return AsFoot(g, e, now);
    }

    /// <summary>The line between the party and you pinned below it: "▼ 2 more" on a fine rule.</summary>
    static FrameworkElement GapLine(int left)
    {
        var g = new Grid { VerticalAlignment = VerticalAlignment.Center };
        g.Children.Add(new Border { Height = 1, Background = Frozen(Color.FromArgb(0x30, 0xFF, 0xFF, 0xFF)), VerticalAlignment = VerticalAlignment.Center });
        var t = Tt(left > 0 ? "▼  " + L.F("{0} more", left) : "▼", 10.5, Frozen(Color.FromRgb(0xA9, 0xAF, 0xBE)), Face, FontWeights.SemiBold, FontStretches.Normal);
        g.Children.Add(new Border { Background = Frozen(InkC0), Padding = new Thickness(8, 0, 8, 0), HorizontalAlignment = HorizontalAlignment.Center, Child = t });
        return g;
    }

    /// <summary>The fight's blocks as far into their broadcast entrance as <see cref="_enter"/> says (1: all in).</summary>
    void ApplyEnter()
    {
        // the title and the HP wipe in behind a slanted edge (short words: fast), the rows and the foot fade up after them,
        // so no figure is ever shown cut by the wipe
        double tw = Out(Seg(_enter, 0, .16)), hw = Out(Seg(_enter, .06, .24));
        void W(Border b, double p) { if (p >= 1) b.ClearValue(UIElement.ClipProperty); else b.Clip = Wipe(0, (FightWidth + 30) * p, 60); }
        W(_titleHost, tw); W(_hpHost, hw);
        // the board's ground fades up, then each row wipes in after the one above it (its words on once its plates are in)
        _rowsHost.Opacity = Seg(_enter, .2, .3);
        if (Meter is { } m)
            for (int i = 0; i < m.Members.Count; i++)
                if (m.RowOf(m.Members[i].Id) is ScoreRow r) { r.Enter = Out(Seg(_enter, .2 + i * .08, .44 + i * .08)); r.Root.InvalidateVisual(); foreach (var ch in ((Grid)r.Root).Children) if (ch is UIElement u) u.InvalidateVisual(); }
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
            yb.Sweep = Seg(t, .22, .62); yb.Art.InvalidateVisual();
        }, () => _bugAnchor.Children.Remove(row));
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
            words.Sweep = Seg(t, .16, .5); words.Art.InvalidateVisual();
            star.Sweep = Seg(t, .1, .36); star.Art.InvalidateVisual();
        }, () => _bugAnchor.Children.Remove(row));
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
        var dim = new Border { Background = Frozen(Tac.A(InkC0, .78)), Opacity = 0, Margin = new Thickness(0, 72, 0, 0) };
        var band = new Border { Height = 64, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 0, 38), Background = Frozen(Tac.A(InkC0, .9)), BorderBrush = wipe ? Red : Yellow, BorderThickness = new Thickness(0, 2, 0, 2), Opacity = 0 };
        var plate = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 0, 46) };
        var winner = !wipe && Meter is { Members.Count: > 0 } mm ? mm.RowOf(mm.Members[0].Id) as ScoreRow : null;
        var head = new Block(wipe ? SlateC : YellowC, T(wipe ? "✖" : "★", 22, wipe ? Red : InkB), 48, new Thickness(14, 0, 12, 0), leanLeft: true);
        var main = new Block(wipe ? RedC : InkC0, T(L.T(wipe ? "DEFEAT" : "VICTORY"), 34, wipe ? White : Yellow), 48, new Thickness(18, 0, 22, 0));
        Row(plate, head); Row(plate, main);
        plate.Clip = Geometry.Empty;
        _fightFx.Children.Clear(); _fightFx.Children.Add(dim); _fightFx.Children.Add(band); _fightFx.Children.Add(plate);
        plate.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        double w = plate.DesiredSize.Width + 16;
        Motion.Play(_fightFx, 2600, t =>
        {
            double i = Out(Seg(t, .06, .22)), o = Seg(t, .84, 1); o = o * o;
            plate.Clip = Wipe(w * o, w * i, 52);
            dim.Opacity = band.Opacity = t < .84 ? Out(Seg(t, 0, .1)) : 1 - Seg(t, .84, 1);
            // a win glints; a loss slams in with a red flash and no shine
            if (!wipe) { main.Sweep = Seg(t, .24, .6); main.Art.InvalidateVisual(); }
            else
            {
                plate.RenderTransform = new TranslateTransform(6 * (1 - Out(Seg(t, .06, .2))), 0);
                band.Background = Frozen(Tac.A(Tac.Mix(InkC0, RedC, t > .08 && t < .2 ? .6 : 0), .9));
            }
            if (winner != null) { winner.Sweep = Seg(t, .04, .4); foreach (var ch in ((Grid)winner.Root).Children) if (ch is UIElement u) u.InvalidateVisual(); }
        }, () => { _fightFx.Children.Clear(); if (winner != null) { winner.Sweep = -1; foreach (var ch in ((Grid)winner.Root).Children) if (ch is UIElement u) u.InvalidateVisual(); } });
    }
}
