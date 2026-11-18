using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using static KinahLedger.Ui.HudKit;

namespace KinahLedger.Ui;

/// <summary>
/// Arena and Studio in a dungeon run (the "run*" values, Ui/HudRunBuddy.cs; Steven, 2026-11-18: "Yes, all looks"): the bug
/// as a broadcast's speedrun clock. A tab over it names the dungeon and its mode on the red key block, "Run 3 today · 38
/// clears" beside it; the bug's ink block holds the run clock huge (white, green ahead of your fastest run's pace, red behind),
/// a block after it the pace, solid green ahead and red behind, then the light block with your best. Under the bug the last
/// bosses' splits on blocks of their own (the delta in the gold for a new best split), then the pickups. After the final
/// boss, for a few seconds: a gold ★ NEW BEST block before the clock, or "Cleared in 4:17" with a gold "2nd fastest" block,
/// then the run's worth on a gold block against your average run here. Between runs in the dungeon: today here in the ink
/// block, your fastest in the light one, today's runs and average clear under it. Arena's blocks under the bug are ink, Studio's
/// light (as their pickups' row).
/// </summary>
sealed partial class HudScorebug
{
    /// <summary>The run's tab over the bug (the dungeon and its mode, the count); empty and collapsed out of a run.</summary>
    readonly StackPanel _runTab = new() { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 0, 0, 3), Visibility = Visibility.Collapsed };

    // (the colours as figures, not the other parts' fields: a partial's statics start in no set order)
    static readonly SolidColorBrush CoralB = Frozen(Color.FromRgb(0xFF, 0x6B, 0x78)), PaperGreenB = Frozen(Color.FromRgb(0x14, 0x93, 0x5A)), PaperRedB = Frozen(Color.FromRgb(0xC8, 0x1E, 0x30)),
        PaperGold = Frozen(Color.FromRgb(0xA8, 0x6A, 0x00));

    void DrawRun(RunBuddy.State s, Figures f)
    {
        double cap = FightWidth - _root.Margin.Left - _root.Margin.Right;
        var tone = s.PaceSign < 0 ? Green : s.PaceSign > 0 ? CoralB : White;

        // the bug: its fullest form that fits with the timer's block at its end, else the next
        var forms = new List<Func<Block>[]>();
        int fastestUpTo = -1;   // the forms that show your fastest (between runs: the tab then leaves it out)
        if (s.Run)
        {
            var clock = new Func<Block>[] { () => ClockBlock(s.Clock, tone) };
            var pace = s.Pace.Length > 0 ? new Func<Block>[] { () => PaceBlock(s.Pace, s.PaceSign) } : Array.Empty<Func<Block>>();
            var best = s.Best.Length > 0 ? new Func<Block>[] { () => LightFig(L.T("Best"), s.Best) }
                : s.Verdict == "first" ? new Func<Block>[] { () => LightWords(L.T("First clear here")) } : Array.Empty<Func<Block>>();
            forms.Add(Cat(clock, pace, best)); forms.Add(Cat(clock, pace)); forms.Add(clock);
        }
        else if (s.Clear)
        {
            if (s.Verdict == "best")
            {
                Func<Block>[] nb = { () => new Block(YellowC, T("★ " + L.T("NEW BEST"), 18, InkB), BugH, new Thickness(12, 0, 12, 0), leanLeft: false) };
                Func<Block>[] clock = { () => ClockBlock(s.Clock, White) };
                var pace = s.Pace.Length > 0 ? new Func<Block>[] { () => PaceBlock(s.Pace, -1) } : Array.Empty<Func<Block>>();
                forms.Add(Cat(nb, clock, pace)); forms.Add(Cat(nb, clock)); forms.Add(clock);
            }
            else
            {
                // "Cleared in 4:17" in the ink block (the clock huge wherever the language puts it), the verdict on gold after it, or your best
                var (before, after) = ClearedWords();
                var verdict = RunBuddy.Verdict(s);
                var vb = s.Verdict is "rank" or "first" ? new Func<Block>[] { () => new Block(YellowC, T(verdict, 15, InkB), BugH, new Thickness(13, 0, 13, 0)) }
                    : s.Best.Length > 0 ? new Func<Block>[] { () => LightFig(L.T("Best"), s.Best) } : Array.Empty<Func<Block>>();
                Func<Block>[] cleared = { () => ClockBlock(s.Clock, White, before, after) };
                Func<Block>[] clock = { () => ClockBlock(s.Clock, White) };
                forms.Add(Cat(cleared, vb)); forms.Add(Cat(clock, vb)); forms.Add(clock);
            }
        }
        else if (s.TodayRuns.Length > 0)
        {
            // today here in the ink block ("335K today here"), your fastest in the light one
            string hero = s.TodayKinah.Length > 0 ? s.TodayKinah : RunsWord(s.TodayRuns);
            double size = s.TodayKinah.Length > 0 ? 32 : 22;
            var fastest = s.Best.Length > 0 ? new Func<Block>[] { () => LightFig(L.T("fastest"), s.Best) } : Array.Empty<Func<Block>>();
            Func<Block>[] full = { () => HeroBlock(hero, size, L.T("today here")) }, bare = { () => HeroBlock(hero, size, "") };
            forms.Add(Cat(full, fastest)); forms.Add(Cat(bare, fastest)); forms.Add(full); forms.Add(bare);
            if (fastest.Length > 0) fastestUpTo = 1;
        }
        else if (s.Best.Length > 0) { forms.Add(new Func<Block>[] { () => HeroBlock(s.Best, 32, L.T("fastest")) }); forms.Add(new Func<Block>[] { () => HeroBlock(s.Best, 32, "") }); }
        else forms.Add(new Func<Block>[] { () => Ink(T(L.T("First clear here"), 14, Grey, FontWeights.SemiBold), BugH, new Thickness(14, 0, 16, 0), l: false) });
        int at = 0;
        for (; at < forms.Count; at++)
        {
            _bug.Children.Clear(); _timerBlock = null;
            foreach (var make in forms[at]) Row(_bug, make());
            DrawTimerBlock();
            if (Wants(_bug) <= cap + .5) break;
        }

        // the tab: the dungeon and its mode on the red key block, the count beside it (the parts give way in turn)
        var count = RunCount(s, at <= fastestUpTo);
        var today = s.Today.Length > 0 && !s.Between ? L.F("Run {0} today", s.Today) : "";
        var clears = ClearsWord(s.Clears);
        foreach (var (head, c) in new[] { (s.Head, count), (s.Head, today.Length > 0 ? today : clears), (s.Head, clears), (s.Head, ""), (s.Place, "") })
        {
            _runTab.Children.Clear();
            Row(_runTab, new Block(RedC, T(head.ToUpperInvariant(), 12.5, White), 22, new Thickness(10, 0, 12, 0), leanLeft: false));
            if (c.Length > 0)
                Row(_runTab, _studio ? new Block(LightC, T(c, 12, Sub, FontWeights.SemiBold), 22, new Thickness(11, 0, 12, 0)) : Ink(T(c, 12, Grey, FontWeights.SemiBold), 22, new Thickness(11, 0, 12, 0)));
            if (Wants(_runTab) <= cap + .5) break;
        }
        _runTab.Visibility = Visibility.Visible;

        // under the bug: the splits, or once the clear's rewards were read its worth against your average run; between runs, today's runs
        _under.Children.Clear();
        _rareBanner = null;
        double room = FightWidth - 40;
        StackPanel? sub = null;
        if (s.Clear && s.Kinah.Length > 0)
        {
            var kin = new Block(YellowC, T(s.Kinah, 15, InkB), 24, new Thickness(11, 0, 11, 0), leanLeft: false);
            sub = UnderRow(room, new[] { kin, s.Avg.Length > 0 ? UnderBlock(Words((L.T("avg") + " ", 12, false), (s.Avg, 14, true))) : null }, new[] { kin });
        }
        else if ((s.Run || s.Clear) && s.Splits.Count > 0)
        {
            Block One(RunBuddy.Split x)
            {
                var parts = new List<(string, double, bool)> { (x.Boss + " ", 12, false), (x.Clock, 14, true) };
                var b = UnderBlock(Words(parts.ToArray()));
                if (x.Delta.Length > 0 && b.Root.Children[1] is Border { Child: StackPanel sp })
                {
                    var d = T(" " + x.Delta, 14, x.Best ? (_studio ? PaperGold : Yellow) : x.Sign < 0 ? (_studio ? PaperGreenB : Green) : x.Sign > 0 ? (_studio ? PaperRedB : CoralB) : (_studio ? Sub : Grey));
                    d.VerticalAlignment = VerticalAlignment.Center; sp.Children.Add(d);
                }
                return b;
            }
            sub = s.Splits.Count >= 2 ? UnderRow(room, new[] { One(s.Splits[^2]), One(s.Splits[^1]) }, new[] { One(s.Splits[^1]) }) : UnderRow(room, new[] { One(s.Splits[^1]) });
        }
        else if (s.Between && s.TodayRuns.Length > 0)
        {
            var runs = s.TodayKinah.Length > 0 ? UnderBlock(Words((RunsWord(s.TodayRuns), 14, true))) : null;
            var avg = s.TodayAvg.Length > 0 ? UnderBlock(Words((L.T("avg") + " ", 12, false), (s.TodayAvg, 14, true))) : null;
            if (runs != null || avg != null) sub = UnderRow(room, new[] { runs, avg }, new[] { runs ?? avg });
        }
        if (sub != null) _under.Children.Add(sub);
        // the pickups (the best drop and the last three, else the three: they stay before any figure goes)
        var strips = RunStrips(StripLook);
        var strip = strips.FirstOrDefault(x => Wants(x) <= room + .5) ?? strips.LastOrDefault();
        if (strip != null) _under.Children.Add(LootRow(strip));
        // the row right under the bug gives way while the pickup's ticker is over it; the ticker hangs over it, not under it
        _lootRow = _under.Children.Count > 0 ? (FrameworkElement)_under.Children[0] : null;
        Panel.SetZIndex(_bugHost, _lootRow != null ? 1 : 0);
        if (NoteRow(f) is { } nr) _under.Children.Add(nr);
    }

    static Func<Block>[] Cat(params Func<Block>[][] parts) => parts.SelectMany(p => p).ToArray();

    /// <summary>The ink block with a big figure (the clock), and the clear's words round it in grey (<paramref name="before"/>, <paramref name="after"/>).</summary>
    Block ClockBlock(string clock, Brush fg, string before = "", string after = "", bool leanLeft = false)
    {
        var p = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        if (before.Length > 0) { var b = T(before, 14, Grey, FontWeights.SemiBold); b.VerticalAlignment = VerticalAlignment.Bottom; b.Margin = new Thickness(0, 0, 6, 7); p.Children.Add(b); }
        var c = T(clock, 32, fg); c.Margin = new Thickness(0, -2, 0, 0); p.Children.Add(c);
        if (after.Length > 0) { var a = T(after, 14, Grey, FontWeights.SemiBold); a.VerticalAlignment = VerticalAlignment.Bottom; a.Margin = new Thickness(6, 0, 0, 7); p.Children.Add(a); }
        return Ink(p, BugH, new Thickness(14, 0, 14, 0), l: leanLeft);
    }

    /// <summary>The ink block with a big figure and its unit after it, as the hour's ("335K today here").</summary>
    Block HeroBlock(string fig, double size, string unit)
    {
        var p = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        var t = T(fig, size, White); t.Margin = new Thickness(0, -2, 0, 0); p.Children.Add(t);
        if (unit.Length > 0) { var u = T(unit, 14, Grey, FontWeights.SemiBold); u.VerticalAlignment = VerticalAlignment.Bottom; u.Margin = new Thickness(5, 0, 0, size > 30 ? 7 : 4); p.Children.Add(u); }
        return Ink(p, BugH, new Thickness(14, 0, 14, 0), l: false);
    }

    /// <summary>The pace on a solid block: green ahead of your fastest run (dark figures), red behind, slate level.</summary>
    static Block PaceBlock(string pace, int sign) =>
        new(sign < 0 ? GreenC : sign > 0 ? RedC : SlateC, T(pace, 21, sign < 0 ? InkB : White), BugH, new Thickness(12, 0, 12, 0));

    /// <summary>The light block with a caption over a figure, as the session's ("BEST" over 4:12).</summary>
    static Block LightFig(string caption, string fig)
    {
        var p = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        p.Children.Add(Cap(caption, Sub));
        var t = T(fig, 18, InkB); t.Margin = new Thickness(0, -3, 0, 0);
        p.Children.Add(t);
        return new Block(LightC, p, BugH, new Thickness(13, 0, 13, 0));
    }

    static Block LightWords(string words) => new(LightC, T(words, 13.5, InkB, FontWeights.SemiBold), BugH, new Thickness(13, 0, 13, 0));

    /// <summary>Words and figures in one line for a block under the bug: each (text, size, a figure): a figure white (Studio ink), a word grey.</summary>
    StackPanel Words(params (string Text, double Size, bool Fig)[] parts)
    {
        var p = new StackPanel { Orientation = Orientation.Horizontal };
        foreach (var (text, size, fig) in parts)
        {
            var t = T(text, size, fig ? (_studio ? InkB : White) : (_studio ? Sub : Grey), fig ? FontWeights.Bold : FontWeights.SemiBold);
            t.VerticalAlignment = VerticalAlignment.Center;
            p.Children.Add(t);
        }
        return p;
    }

    /// <summary>A block under the bug: Arena's ink, Studio's light (as their pickups' row).</summary>
    Block UnderBlock(UIElement child) => _studio ? new Block(LightC, child, 24, new Thickness(11, 0, 11, 0)) : Ink(child, 24, new Thickness(11, 0, 11, 0));

    /// <summary>The first of the blocks' rows that fits <paramref name="room"/> (the last when none does), hung under the bug as the stats strip is.</summary>
    static StackPanel UnderRow(double room, params Block?[][] forms)
    {
        StackPanel row = null!;
        foreach (var form in forms)
        {
            row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(Lean, 3, 0, 0) };
            foreach (var b in form.Where(b => b != null))
            {
                if (b!.Root.Parent is Panel old) old.Children.Remove(b.Root);
                b.Root.Margin = new Thickness(0);
                Row(row, b);
            }
            if (Wants(row) <= room + .5) break;
        }
        return row;
    }
}
