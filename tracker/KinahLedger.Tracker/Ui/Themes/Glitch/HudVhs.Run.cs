using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using static KinahLedger.Ui.GlitchKit;

namespace KinahLedger.Ui;

/// <summary>
/// The tapes in a dungeon run (the "run*" values in RunBuddy's words, Ui/HudRunBuddy.cs; Steven, 2026-11-18: "Yes, all
/// looks"): the tape records the run. The tape row: "● REC" while the run goes (the dot blinks with the run clock), "■ STOP"
/// once it is cleared, "‖ STBY" between runs, then the dungeon and its mode, and "Run 3 today · 38 clears" where the counter
/// stands. The figures: the run clock big where the hour is (green ahead of your fastest run's pace, red behind, Deck's display
/// cyan or Viewfinder's white even), the pace beside it, your best where the battery is; under them the last bosses' splits
/// where the session is, the timer kept at its end. The clear: the clock in the tape's yellow and a full yellow battery,
/// "★ NEW BEST", or "Cleared in 4:17" and "2nd fastest"; then the run's worth against your average run here. Between runs:
/// today here, its runs and average clear. The pickup strip stays between the figures, the last three before any figure goes.
/// </summary>
sealed partial class HudVhs
{
    readonly TextBlock _runHead = Txt("", 11, Br(0xFFFFFF, .82), VhsRows.Sans, FontWeights.SemiBold), _runCount = Txt("", 10.5, Br(0xFFFFFF, .6), VhsRows.Sans, FontWeights.SemiBold);
    bool _runOn;

    /// <summary>The run's head and count in the tape row: the head after REC, the count where the counter stands; hidden until a run.</summary>
    void BuildRun(Grid tape)
    {
        _runHead.Margin = new Thickness(9, 0, 0, 0); _runHead.HorizontalAlignment = HorizontalAlignment.Left; _runHead.Visibility = Visibility.Collapsed;
        if (Deck) { _runHead.Foreground = Br(0xE6FDFF, .85); _runCount.Foreground = Br(VfdC, .66); }
        Grid.SetColumn(_runHead, 1); tape.Children.Insert(tape.Children.IndexOf(_ff), _runHead);
        _runCount.Margin = new Thickness(14, 0, 0, 0); _runCount.HorizontalAlignment = HorizontalAlignment.Right; _runCount.Visibility = Visibility.Collapsed;
        Grid.SetColumn(_runCount, 2); tape.Children.Add(_runCount);
    }

    /// <summary>The run's head and count in the tape row in place of the counter, or the counter back.</summary>
    void ShowRun(bool on)
    {
        _runOn = on;
        _runHead.Visibility = _runCount.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
        _tc.Visibility = on ? Visibility.Collapsed : Visibility.Visible;
    }

    /// <summary>A pickup's mark in the tape row: the dungeon's name steps aside for it.</summary>
    void RunHeadAside(double ms)
    {
        if (!_runOn) return;
        _runHead.Visibility = Visibility.Hidden;
        Motion.Play(_runHead, ms, _ => { }, () => _runHead.Visibility = _runOn ? Visibility.Visible : Visibility.Collapsed);
    }

    /// <summary>The tape row in a run: the tape's state, the dungeon and its mode, the count; each the fullest form that fits.</summary>
    void RunTape(RunBuddy.State s)
    {
        _recWord.Text = s.Run ? L.T("REC") : s.Clear ? "■ " + L.T("STOP") : "‖ " + L.T("STBY");
        _recWord.Foreground = s.Run ? Red : s.Clear ? _text : _label;
        _dot.Visibility = s.Run ? Visibility.Visible : Visibility.Collapsed;
        _dot.Opacity = s.Clock.Length > 0 && char.IsDigit(s.Clock[^1]) && (s.Clock[^1] - '0') % 2 == 1 ? 1 : .3;
        double room = FightWidth - _deck.Body.Margin.Left - _deck.Body.Margin.Right - Wide(_rec) - _runHead.Margin.Left - 1;
        var counts = CountForms(s);
        foreach (var head in new[] { s.Head, s.Place })
            foreach (var count in counts)
            {
                _runHead.Text = head; _runCount.Text = count;
                if (Wide(_runHead) - _runHead.Margin.Left + (count.Length > 0 ? Wide(_runCount) : 0) <= room + .5) return;
            }
    }

    /// <summary>The run's figures and the line under them.</summary>
    void DrawRun(RunBuddy.State s)
    {
        _figRow.Children.Clear(); _figRow.ColumnDefinitions.Clear();
        _figRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); _figRow.ColumnDefinitions.Add(new ColumnDefinition()); _figRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        double cap = FightWidth - _deck.Body.Margin.Left - _deck.Body.Margin.Right - 1;   // a pixel kept for the frame's rim and rounding
        var (forms, whole) = FigForms(s);
        // the best drop and the last three in the room the figures leave, the last three alone before any figure goes
        var (left, right, strip) = FigForm(Values, Strip, cap, forms, whole);
        _figRow.Children.Add(left);
        if (right != null) { Grid.SetColumn(right, 2); _figRow.Children.Add(right); }
        if (strip != null)
        {
            strip.HorizontalAlignment = HorizontalAlignment.Right; strip.Margin = new Thickness(14, 0, 0, 0);
            Grid.SetColumn(strip, 1); _figRow.Children.Add(strip);
        }
        _stats.Inlines.Clear(); _stats.Visibility = Visibility.Collapsed;
        _rare.Visibility = Visibility.Collapsed;
        RunSub(s);
    }

    /// <summary>
    /// The figure row's forms, fullest first: the big figure (and its words) at the left, your best or the verdict at the
    /// right; and how many of the first keep every figure (<see cref="GlitchKit.FigForm"/>).
    /// </summary>
    (List<(FrameworkElement L, FrameworkElement? R)> Forms, int Whole) FigForms(RunBuddy.State s)
    {
        var forms = new List<(FrameworkElement L, FrameworkElement? R)>();
        int whole = 1;
        if (s.Run)
        {
            var tone = PaceTone(s.PaceSign, Green, Red, _ink);
            FrameworkElement Clock(bool pace) => Big(s.Clock, tone, pace && s.Pace.Length > 0 ? Beside(s.Pace, 14, tone, FontWeights.Bold) : null);
            FrameworkElement? best = s.Best.Length > 0 ? Right(Rn(L.T("BEST") + "  ", _label, 9.5, FontWeights.SemiBold), Rn(s.Best, _text, 13.5))
                : s.Verdict == "first" ? Right(Rn(L.T("First clear here"), _label, 11.5, FontWeights.SemiBold)) : null;
            forms.Add((Clock(true), best)); forms.Add((Clock(true), null)); forms.Add((Clock(false), null));
        }
        else if (s.Clear && s.Verdict == "best")
        {
            // a new best: the clock in the tape's yellow, the battery full and yellow under "★ NEW BEST"
            FrameworkElement Clock(bool pace) => Big(s.Clock, Yellow, pace && s.Pace.Length > 0 ? Beside(s.Pace, 14, Green, FontWeights.Bold) : null);
            forms.Add((Clock(true), NewBest())); forms.Add((Clock(false), NewBest())); forms.Add((Clock(false), null));
        }
        else if (s.Clear)
        {
            // "Cleared in 4:17", the clock big wherever the language puts it, then the verdict ("2nd fastest") where the battery is
            var (before, after) = ClearedIn();
            var verdict = RunBuddy.Verdict(s);
            FrameworkElement? Verdict() => verdict.Length > 0 ? Right(Rn(verdict, s.Verdict is "rank" or "first" ? Yellow : _text, 13.5)) : null;
            var cleared = new StackPanel { Orientation = Orientation.Horizontal };
            if (before.Trim().Length > 0) cleared.Children.Add(Unit(before.TrimEnd(), 13, new Thickness(0, 0, 6, 5)));
            cleared.Children.Add(Txt(s.Clock, 28, _ink, VhsRows.Sans));
            if (after.Trim().Length > 0) cleared.Children.Add(Unit(after.TrimStart(), 13, new Thickness(6, 0, 0, 5)));
            forms.Add((cleared, Verdict())); forms.Add((Big(s.Clock, _ink, null), Verdict())); forms.Add((Big(s.Clock, _ink, null), null));
            whole = 2;
        }
        else if (s.TodayRuns.Length > 0)
        {
            // between runs: today here big ("335K today here"); its runs and average clear under it
            string runs = s.TodayRuns == "1" ? L.T("1 run") : L.F("{0} runs", s.TodayRuns);
            FrameworkElement Hero(bool word)
            {
                var sp = new StackPanel { Orientation = Orientation.Horizontal };
                sp.Children.Add(s.TodayKinah.Length > 0 ? Txt(s.TodayKinah, 28, _ink, VhsRows.Sans) : Txt(runs, 20, _ink, VhsRows.Sans));
                if (word) sp.Children.Add(Unit(L.T("today here"), 13, new Thickness(6, 0, 0, 5)));
                return sp;
            }
            forms.Add((Hero(true), null)); forms.Add((Hero(false), null));
            whole = 2;
        }
        else if (s.Best.Length > 0)
        {
            var sp = new StackPanel { Orientation = Orientation.Horizontal };
            sp.Children.Add(Txt(s.Best, 28, _ink, VhsRows.Sans));
            sp.Children.Add(Unit(L.T("fastest"), 13, new Thickness(6, 0, 0, 5)));
            forms.Add((sp, null)); forms.Add((Big(s.Best, _ink, null), null));
            whole = 2;
        }
        else forms.Add((Txt(L.T("First clear here"), 13, _label, VhsRows.Sans, FontWeights.SemiBold), null));
        return (forms, whole);
    }

    /// <summary>A big figure, crisp in Inter, with its words beside it.</summary>
    static FrameworkElement Big(string fig, Brush fg, FrameworkElement? beside)
    {
        var sp = new StackPanel { Orientation = Orientation.Horizontal };
        sp.Children.Add(Txt(fig, 28, fg, VhsRows.Sans));
        if (beside != null) sp.Children.Add(beside);
        return sp;
    }

    /// <summary>Words beside a big figure, on its baseline (the pace's "−0:11").</summary>
    static TextBlock Beside(string s, double size, Brush fg, FontWeight w)
    {
        var t = Txt(s, size, fg, VhsRows.Sans, w); t.VerticalAlignment = VerticalAlignment.Bottom; t.Margin = new Thickness(7, 0, 0, 5);
        return t;
    }

    /// <summary>A quiet word beside a big figure, as the hour's "/h" is.</summary>
    TextBlock Unit(string s, double size, Thickness margin)
    {
        var t = Txt(s, size, _label, VhsRows.Sans, FontWeights.SemiBold); t.VerticalAlignment = VerticalAlignment.Bottom; t.Margin = margin;
        return t;
    }

    /// <summary>The words where the battery stands: right-aligned, 16 px off the figures.</summary>
    TextBlock Right(params Run[] runs)
    {
        var t = Txt("", 13.5, _text, VhsRows.Sans); t.HorizontalAlignment = HorizontalAlignment.Right; t.Margin = new Thickness(16, 0, 0, 0);
        foreach (var r in runs) t.Inlines.Add(r);
        return t;
    }

    /// <summary>A new best: "★ NEW BEST" over the battery's ten cells, every one lit yellow (the farming best's battery).</summary>
    StackPanel NewBest()
    {
        var sp = new StackPanel { VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(16, 0, 0, 0) };
        var t = Txt("★ " + L.T("NEW BEST"), 11.5, Yellow, VhsRows.Sans); t.HorizontalAlignment = HorizontalAlignment.Right;
        sp.Children.Add(t);
        var cells = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 4, 0, 0) };
        for (int i = 0; i < 10; i++) cells.Children.Add(new Border { Width = 5, Height = 8, Margin = new Thickness(0, 0, 1.5, 0), Background = Yellow });
        cells.Children.Add(new Border { Width = 2, Height = 4, Background = Br(0xFFFFFF, .45), VerticalAlignment = VerticalAlignment.Center });
        sp.Children.Add(cells);
        return sp;
    }

    /// <summary>
    /// The line under the figures, where the session is, in the room the timer leaves it: the last bosses' splits (the newest
    /// kept when both don't fit); after a clear the run's worth against your average run here; between runs today's runs and
    /// average clear.
    /// </summary>
    void RunSub(RunBuddy.State s)
    {
        double room = FightWidth - _deck.Body.Margin.Left - _deck.Body.Margin.Right - 1 - (_timer.Text.Length > 0 ? Wide(_timer) + _timerCell.Margin.Left : 0);
        var forms = new List<Run[]>();
        var sep = Rn("  ·  ", Faded, 11.5, FontWeights.SemiBold);
        Run[] Avg(string fig) => new[] { sep, Rn(L.T("avg").ToUpper(System.Globalization.CultureInfo.CurrentCulture) + "  ", _label, 10, FontWeights.SemiBold), Rn(fig, _text, 13, FontWeights.Bold) };
        if (s.Clear && s.Kinah.Length > 0)
        {
            Run[] kin = { Rn(s.Kinah, Yellow, 14, FontWeights.Bold) };
            if (s.Avg.Length > 0) forms.Add(Join(kin, Avg(s.Avg)));
            forms.Add(kin);
        }
        else if (s.Run || s.Clear)
        {
            Run[] One(RunBuddy.Split x)
            {
                var parts = new List<Run> { Rn(x.Boss + " ", _label, 11.5, FontWeights.SemiBold), Rn(x.Clock, _text, 13, FontWeights.Bold) };
                if (x.Delta.Length > 0) parts.Add(Rn(" " + x.Delta, x.Best ? Yellow : PaceTone(x.Sign, Green, Red, _label), 13, FontWeights.Bold));
                return parts.ToArray();
            }
            if (s.Splits.Count >= 2) forms.Add(Join(One(s.Splits[^2]), new[] { sep }, One(s.Splits[^1])));
            if (s.Splits.Count >= 1) forms.Add(One(s.Splits[^1]));
        }
        else if (s.TodayRuns.Length > 0)
        {
            string runs = s.TodayRuns == "1" ? L.T("1 run") : L.F("{0} runs", s.TodayRuns);
            var more = new List<Run>();
            if (s.TodayKinah.Length > 0) more.Add(Rn(runs, _text, 13, FontWeights.Bold));
            if (s.TodayAvg.Length > 0) { var a = Avg(s.TodayAvg); more.AddRange(more.Count > 0 ? a : a.Skip(1)); }
            if (more.Count > 0) forms.Add(more.ToArray());
            if (s.TodayKinah.Length > 0) forms.Add(more.Take(1).ToArray());
        }
        if (forms.Count > 0) FitRuns(_sess, room, forms.ToArray()); else _sess.Inlines.Clear();
        _sessRow.Visibility = _sess.Inlines.Count > 0 || _timer.Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
    }
}
