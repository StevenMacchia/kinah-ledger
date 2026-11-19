using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using static KinahLedger.Ui.GlitchKit;

namespace KinahLedger.Ui;

/// <summary>
/// Corrupted in a dungeon run (the "run*" values in RunBuddy's words, Ui/HudRunBuddy.cs; Steven, 2026-11-18: "Yes, all
/// looks"): the broken stream carries the run. The meta line names the dungeon and its mode, "Run 3 today · 38 clears" at its
/// right; the run clock big where the hour is, split magenta and cyan behind it (green ahead of your fastest run's pace, red
/// behind, white even), the pace beside it and your best in the white block; under it the last bosses' splits where the
/// session is, cut by the stream's "//", the timer's block kept at its end. The clear: the clock in gold and a gold
/// "★ NEW BEST" block, or "Cleared in 4:17" and a "2nd fastest" block; then the run's worth against your average run here.
/// Between runs: today here, its runs and average clear. The pickup strip stays between the figures, the last three before
/// any figure goes.
/// </summary>
sealed partial class HudCorrupt
{
    readonly TextBlock _runCount = Txt("", 10, LilacDim, Face);
    FrameworkElement _metaLead = null!;
    bool _runOn;
    static readonly Brush Green = Br(GreenC), Red = Br(RedC2), Snow = Br(0xEDE6FF);

    /// <summary>The run's count on the meta line's right (the pickup's block flickers up over it); hidden until a run.</summary>
    void BuildRun(Grid meta)
    {
        _metaLead = (FrameworkElement)meta.Children[0];
        _runCount.HorizontalAlignment = HorizontalAlignment.Right; _runCount.Margin = new Thickness(14, 0, 0, 0); _runCount.Visibility = Visibility.Collapsed;
        Grid.SetColumn(_runCount, 1); meta.Children.Add(_runCount);
    }

    void ShowRun(bool on)
    {
        _runOn = on;
        _runCount.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>A pickup's block on the meta line: the count steps aside for it.</summary>
    void RunCountAside(double ms)
    {
        if (!_runOn) return;
        _runCount.Visibility = Visibility.Hidden;
        Motion.Play(_runCount, ms, _ => { }, () => _runCount.Visibility = _runOn ? Visibility.Visible : Visibility.Collapsed);
    }

    /// <summary>The meta line in a run: the dungeon and its mode, the count at its right; each the fullest form that fits.</summary>
    void RunMeta(RunBuddy.State s)
    {
        double room = FightWidth - _deck.Body.Margin.Left - _deck.Body.Margin.Right - 1;
        var counts = CountForms(s);
        foreach (var head in new[] { s.Head, s.Place })
            foreach (var count in counts)
            {
                _meta.Text = head; _runCount.Text = count;
                if (Wide(_metaLead) + (count.Length > 0 ? Wide(_runCount) : 0) <= room + .5) return;
            }
    }

    /// <summary>The run's figures and the line under them.</summary>
    void DrawRun(RunBuddy.State s)
    {
        _figRow.Children.Clear(); _figRow.ColumnDefinitions.Clear(); _figRow.RowDefinitions.Clear(); _bestBlock = null;
        _figRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); _figRow.ColumnDefinitions.Add(new ColumnDefinition()); _figRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        double cap = FightWidth - _deck.Body.Margin.Left - _deck.Body.Margin.Right - 1;   // a pixel kept for rounding
        var (forms, whole) = FigForms(s);
        // the best drop and the last three in the room the figures leave, else on their own line under them (always whole)
        var (left, right, strip) = FigForm(Values, Strip, cap, forms, whole);
        _figRow.Children.Add(left);
        if (right != null) { Grid.SetColumn(right, 2); _figRow.Children.Add(right); }
        if (strip != null) StripBeside(_figRow, strip);
        else if (PickStrip.Of(Values, Strip) is { } under) StripUnder(_figRow, under);
        _stats.Inlines.Clear(); _stats.Visibility = Visibility.Collapsed;
        _rare.Visibility = Visibility.Collapsed;
        RunSub(s);
    }

    /// <summary>
    /// The figure row's forms, fullest first: the big figure (and its words) at the left, a block at the right; and how many
    /// of the first keep every figure (<see cref="GlitchKit.FigForm"/>).
    /// </summary>
    (List<(FrameworkElement L, FrameworkElement? R)> Forms, int Whole) FigForms(RunBuddy.State s)
    {
        var forms = new List<(FrameworkElement L, FrameworkElement? R)>();
        int whole = 1;
        if (s.Run)
        {
            var tone = PaceTone(s.PaceSign, Green, Red, White);
            FrameworkElement Clock(bool pace) => Big(s.Clock, tone, pace && s.Pace.Length > 0 ? Beside(s.Pace, 14, tone) : null);
            FrameworkElement? Best() => s.Best.Length > 0 ? Block($"{L.T("BEST")} {s.Best}", Br(0xE8E2F5))
                : s.Verdict == "first" ? Block(L.T("First clear here"), Br(0xE8E2F5)) : null;
            forms.Add((Clock(true), Best())); forms.Add((Clock(true), null)); forms.Add((Clock(false), null));
        }
        else if (s.Clear && s.Verdict == "best")
        {
            // a new best: the clock in gold, a gold block
            FrameworkElement Clock(bool pace) => Big(s.Clock, Gold2, pace && s.Pace.Length > 0 ? Beside(s.Pace, 14, Green) : null);
            forms.Add((Clock(true), NewBest())); forms.Add((Clock(false), NewBest())); forms.Add((Clock(false), null));
        }
        else if (s.Clear)
        {
            // "Cleared in 4:17", the clock big wherever the language puts it, then the verdict's block ("2nd fastest")
            var (before, after) = ClearedIn();
            var verdict = RunBuddy.Verdict(s);
            FrameworkElement? Verdict() => verdict.Length > 0 ? Block(verdict, s.Verdict is "rank" or "first" ? Gold2 : Br(0xE8E2F5)) : null;
            var cleared = new StackPanel { Orientation = Orientation.Horizontal };
            if (before.Trim().Length > 0) cleared.Children.Add(Unit(before.TrimEnd(), new Thickness(0, 0, 6, 5)));
            cleared.Children.Add(Split(s.Clock, 27, Face, FontWeights.Bold, White, SplitL, SplitR, 2.2));
            if (after.Trim().Length > 0) cleared.Children.Add(Unit(after.TrimStart(), new Thickness(6, 0, 0, 5)));
            forms.Add((cleared, Verdict())); forms.Add((Big(s.Clock, White, null), Verdict())); forms.Add((Big(s.Clock, White, null), null));
            whole = 2;
        }
        else if (s.TodayRuns.Length > 0)
        {
            // between runs: today here big ("335K today here"); its runs and average clear under it
            string runs = s.TodayRuns == "1" ? L.T("1 run") : L.F("{0} runs", s.TodayRuns);
            FrameworkElement Hero(bool word)
            {
                var sp = new StackPanel { Orientation = Orientation.Horizontal };
                sp.Children.Add(s.TodayKinah.Length > 0 ? Split(s.TodayKinah, 27, Face, FontWeights.Bold, White, SplitL, SplitR, 2.2) : Txt(runs, 19, White, Face));
                if (word) sp.Children.Add(Unit(L.T("today here"), new Thickness(7, 0, 0, 4)));
                return sp;
            }
            forms.Add((Hero(true), null)); forms.Add((Hero(false), null));
            whole = 2;
        }
        else if (s.Best.Length > 0)
        {
            var sp = new StackPanel { Orientation = Orientation.Horizontal };
            sp.Children.Add(Split(s.Best, 27, Face, FontWeights.Bold, White, SplitL, SplitR, 2.2));
            sp.Children.Add(Unit(L.T("fastest"), new Thickness(7, 0, 0, 4)));
            forms.Add((sp, null)); forms.Add((Big(s.Best, White, null), null));
            whole = 2;
        }
        else forms.Add((Txt(L.T("First clear here"), 13, LilacDim, Face), null));
        return (forms, whole);
    }

    /// <summary>A big figure split magenta and cyan behind it, with its words beside it.</summary>
    static FrameworkElement Big(string fig, Brush fg, FrameworkElement? beside)
    {
        var sp = new StackPanel { Orientation = Orientation.Horizontal };
        sp.Children.Add(Split(fig, 27, Face, FontWeights.Bold, fg, SplitL, SplitR, 2.2));
        if (beside != null) sp.Children.Add(beside);
        return sp;
    }

    /// <summary>Words beside a big figure, on its baseline (the pace's "−0:11").</summary>
    static TextBlock Beside(string s, double size, Brush fg)
    {
        var t = Txt(s, size, fg, Face); t.VerticalAlignment = VerticalAlignment.Bottom; t.Margin = new Thickness(7, 0, 0, 4);
        return t;
    }

    /// <summary>A quiet word beside a big figure, in lilac as the hour's "/h" is.</summary>
    static TextBlock Unit(string s, Thickness margin)
    {
        var t = Txt(s, 14, Lilac, Face); t.VerticalAlignment = VerticalAlignment.Bottom; t.Margin = margin;
        return t;
    }

    /// <summary>A block at the row's right, where your best's stands while farming.</summary>
    static Grid Block(string text, Brush bg)
    {
        var b = GlitchBlock(text, bg, Black, 10.5);
        b.Margin = new Thickness(16, 0, 2, 0); b.VerticalAlignment = VerticalAlignment.Center;
        return b;
    }

    static Grid NewBest() => Block("★ " + L.T("NEW BEST"), Gold2);

    /// <summary>
    /// The line under the figures, where the session is, in the room the timer's block leaves it: the last bosses' splits (the
    /// newest kept when both don't fit); after a clear the run's worth against your average run here; between runs today's runs
    /// and average clear.
    /// </summary>
    void RunSub(RunBuddy.State s)
    {
        double room = FightWidth - _deck.Body.Margin.Left - _deck.Body.Margin.Right - 1 - (_timerBlock.Visibility == Visibility.Visible ? Wide(_timerCell) : 0);
        var forms = new List<Run[]>();
        var sep = Rn("  //  ", Br(MagC, .7), 12);
        Run[] Avg(string fig) => new[] { sep, Rn(L.T("avg") + " ", LilacDim, 12), Rn(fig, Snow, 13.5) };
        if (s.Clear && s.Kinah.Length > 0)
        {
            Run[] kin = { Rn(s.Kinah, Gold2, 14) };
            if (s.Avg.Length > 0) forms.Add(Join(kin, Avg(s.Avg)));
            forms.Add(kin);
        }
        else if (s.Run || s.Clear)
        {
            Run[] One(RunBuddy.Split x)
            {
                var parts = new List<Run> { Rn(x.Boss + " ", Br(LilacC, .82), 12), Rn(x.Clock, Snow, 13.5) };
                if (x.Delta.Length > 0) parts.Add(Rn(" " + x.Delta, x.Best ? Gold2 : PaceTone(x.Sign, Green, Red, LilacDim), 13.5));
                return parts.ToArray();
            }
            if (s.Splits.Count >= 2) forms.Add(Join(One(s.Splits[^2]), new[] { sep }, One(s.Splits[^1])));
            if (s.Splits.Count >= 1) forms.Add(One(s.Splits[^1]));
        }
        else if (s.TodayRuns.Length > 0)
        {
            string runs = s.TodayRuns == "1" ? L.T("1 run") : L.F("{0} runs", s.TodayRuns);
            var more = new List<Run>();
            if (s.TodayKinah.Length > 0) more.Add(Rn(runs, Snow, 14));
            if (s.TodayAvg.Length > 0) { var a = Avg(s.TodayAvg); more.AddRange(more.Count > 0 ? a : a.Skip(1)); }
            if (more.Count > 0) forms.Add(more.ToArray());
            if (s.TodayKinah.Length > 0) forms.Add(more.Take(1).ToArray());
        }
        if (forms.Count > 0) FitRuns(_sess, room, forms.ToArray()); else _sess.Inlines.Clear();
        _sessRow.Visibility = _sess.Inlines.Count > 0 || _timerBlock.Visibility == Visibility.Visible ? Visibility.Visible : Visibility.Collapsed;
    }
}
