using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using static KinahLedger.Ui.HudKit;

namespace KinahLedger.Ui;

/// <summary>
/// The Buddy in a dungeon run (the "run*" values, MainWindow.HudRun.cs; Steven, 2026-11-18): a speedrun timer in the card's
/// own rows. The head names the dungeon and its mode, with "Run 3 today · 38 clears" at its end; the figures give the run
/// clock big (green ahead of your fastest run's pace, red behind), the pace and your best, and under them the last bosses'
/// splits against your best split for each. For a few seconds after the final boss: the clear and how it ranks ("NEW BEST"),
/// then the run's worth against your average run here. Between runs in the dungeon: today here and your clears and fastest.
/// The pickup tiles stay at the right.
/// </summary>
public sealed partial class HudCard
{
    readonly TextBlock _runHead = T("", 12, Ink, weight: FontWeights.SemiBold), _runCount = T("", 11.5, Muted, weight: FontWeights.SemiBold);
    readonly TextBlock _runBig = new() { LineHeight = 30, LineStackingStrategy = LineStackingStrategy.BlockLineHeight, VerticalAlignment = VerticalAlignment.Center };
    readonly TextBlock _runSmall = new() { LineHeight = 17, LineStackingStrategy = LineStackingStrategy.BlockLineHeight };
    StackPanel _runFigs = null!;
    FrameworkElement _figsHost = null!;

    /// <summary>The run's parts, put in the head (after the dot) and the body (where the figures are); hidden until a run.</summary>
    void BuildRun(Grid head, Grid body, FrameworkElement figs)
    {
        _figsHost = figs;
        _runHead.Margin = new Thickness(8, 0, 0, 0);
        _runCount.Margin = new Thickness(10, 0, 0, 0); _runCount.TextTrimming = TextTrimming.None; _runCount.HorizontalAlignment = HorizontalAlignment.Right;
        Grid.SetColumn(_runHead, 1); Grid.SetColumnSpan(_runHead, 3); head.Children.Add(_runHead);
        Grid.SetColumn(_runCount, 4); head.Children.Add(_runCount);
        _runFigs = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Children = { _runBig, _runSmall } };
        Grid.SetColumn(_runFigs, 1); body.Children.Add(_runFigs);
        ShowRun(false);
    }

    /// <summary>The run's parts in place of the farming ones, or back.</summary>
    void ShowRun(bool on)
    {
        var run = on ? Visibility.Visible : Visibility.Collapsed;
        var farm = on ? Visibility.Collapsed : Visibility.Visible;
        _runHead.Visibility = _runCount.Visibility = _runFigs.Visibility = run;
        _zone.Visibility = _newest.Visibility = _figsHost.Visibility = farm;
        if (on) { _run.Visibility = _time.Visibility = Visibility.Collapsed; _ringHost.Visibility = Visibility.Collapsed; }
    }

    /// <summary>The run drawn: the head's words and the two lines, each the fullest form that fits beside the tiles.</summary>
    void DrawRun(RunBuddy.State s, bool live)
    {
        SetDot(_dot, live && !s.Between);
        _runHead.Text = s.Head;
        _runCount.Text = RunBuddy.Count(s);
        // the room the figures have: the card's inside less the tiles beside them
        _tiles.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        double room = 360 - 24 - (_tiles.Visibility == Visibility.Visible && _tiles.Children.Count > 0 ? _tiles.DesiredSize.Width + _tiles.Margin.Left + _tiles.Margin.Right : 0) - 4;
        var tone = s.PaceSign < 0 ? Mint : s.PaceSign > 0 ? Rec : Ink;
        if (s.Run)
        {
            var best = s.Best.Length > 0 ? new[] { Sep(12), R(L.T("best") + " " + s.Best, 12.5, Muted, weight: FontWeights.SemiBold) } : s.Verdict == "first" ? new[] { Sep(12), R(L.T("First clear here"), 12.5, Muted, weight: FontWeights.SemiBold) } : Array.Empty<Run>();
            var pace = s.Pace.Length > 0 ? new[] { R("  " + s.Pace, 14, tone, weight: FontWeights.Bold) } : Array.Empty<Run>();
            Fit(_runBig, room, Join(new[] { R(s.Clock, 26, tone, weight: FontWeights.Bold) }, pace, best), Join(new[] { R(s.Clock, 26, tone, weight: FontWeights.Bold) }, pace), new[] { R(s.Clock, 26, tone, weight: FontWeights.Bold) });
            Splits(s, room);
        }
        else if (s.Clear)
        {
            var big = new List<Run[]>();
            if (s.Verdict == "best")
            {
                var head = new[] { R("★ " + L.T("NEW BEST"), 20, Gold, weight: FontWeights.Bold), R("  " + s.Clock, 24, Ink, weight: FontWeights.Bold) };
                big.Add(Join(head, s.Pace.Length > 0 ? new[] { R("  " + s.Pace, 14, Mint, weight: FontWeights.Bold) } : Array.Empty<Run>()));
                big.Add(head);
            }
            else
            {
                // "Cleared in 4:17", the clock big wherever the language puts it, then the verdict ("2nd fastest")
                var fmt = L.T("Cleared in {0}");
                int at = fmt.IndexOf("{0}", StringComparison.Ordinal);
                var cleared = new List<Run>();
                if (at > 0) cleared.Add(R(fmt[..at], 13, Muted, weight: FontWeights.SemiBold));
                cleared.Add(R(s.Clock, 24, Ink, weight: FontWeights.Bold));
                if (at >= 0 && at + 3 < fmt.Length) cleared.Add(R(fmt[(at + 3)..], 13, Muted, weight: FontWeights.SemiBold));
                var verdict = RunBuddy.Verdict(s);
                var vTone = s.Verdict is "rank" or "first" ? Gold : Muted;
                if (verdict.Length > 0) big.Add(Join(cleared.ToArray(), new[] { Sep(13), R(verdict, 13.5, vTone, weight: FontWeights.Bold) }));
                big.Add(new[] { R(s.Clock, 24, Ink, weight: FontWeights.Bold), Sep(13), R(verdict, 13.5, vTone, weight: FontWeights.Bold) });
                big.Add(new[] { R(s.Clock, 24, Ink, weight: FontWeights.Bold) });
            }
            Fit(_runBig, room, big.ToArray());
            // the run's worth against your average run here, once its rewards were read; until then its splits
            if (s.Kinah.Length > 0)
            {
                var kin = new[] { R(s.Kinah, 12.5, Gold, weight: FontWeights.Bold) };
                Fit(_runSmall, room, s.Avg.Length > 0 ? Join(kin, new[] { Sep(11.5), R(L.T("avg") + " " + s.Avg, 12, Muted, weight: FontWeights.SemiBold) }) : kin, kin);
            }
            else Splits(s, room);
        }
        else
        {
            // between runs: today here big ("335K today here"), its runs and average clear under it; your clears and fastest
            // are in the head
            if (s.TodayRuns.Length > 0)
            {
                string runs = s.TodayRuns == "1" ? L.T("1 run") : L.F("{0} runs", s.TodayRuns);
                var hero = s.TodayKinah.Length > 0 ? R(s.TodayKinah, 22, Gold, weight: FontWeights.Bold) : R(runs, 18, Ink, weight: FontWeights.Bold);
                var word = R("  " + L.T("today here"), 12.5, Muted, weight: FontWeights.SemiBold);
                Fit(_runBig, room, new[] { hero, word }, new[] { hero });
                var more = new List<Run>();
                if (s.TodayKinah.Length > 0) more.Add(R(runs, 12, Ink, weight: FontWeights.SemiBold));
                if (s.TodayAvg.Length > 0) { if (more.Count > 0) more.Add(Sep(11.5)); more.Add(R(L.T("avg") + " ", 12, Muted, weight: FontWeights.SemiBold)); more.Add(R(s.TodayAvg, 12, Ink, weight: FontWeights.SemiBold)); }
                Fit(_runSmall, room, more.ToArray(), more.Take(1).ToArray());
            }
            else if (s.Best.Length > 0)
            {
                Fit(_runBig, room, new[] { R(s.Best, 22, Ink, weight: FontWeights.Bold), R("  " + L.T("fastest"), 12.5, Muted, weight: FontWeights.SemiBold) });
                Fit(_runSmall, room, Array.Empty<Run>());
            }
            else
            {
                Fit(_runBig, room, new[] { R(L.T("First clear here"), 13, Muted, weight: FontWeights.SemiBold) });
                Fit(_runSmall, room, Array.Empty<Run>());
            }
        }
        _runSmall.Visibility = _runSmall.Inlines.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>The last bosses' splits ("Kwapo 1:02 −0:05 · Duanka 2:44 +0:03"), the newest kept when both don't fit.</summary>
    void Splits(RunBuddy.State s, double room)
    {
        Run[] One(RunBuddy.Split x)
        {
            var parts = new List<Run> { R(x.Boss, 12, Ink, weight: FontWeights.SemiBold), R(" " + x.Clock, 12, Muted, weight: FontWeights.SemiBold) };
            if (x.Delta.Length > 0) parts.Add(R(" " + x.Delta, 12, x.Best ? Gold : x.Sign < 0 ? Mint : x.Sign > 0 ? Rec : Muted, weight: FontWeights.Bold));
            return parts.ToArray();
        }
        var forms = new List<Run[]>();
        if (s.Splits.Count >= 2) forms.Add(Join(One(s.Splits[^2]), new[] { Sep(11.5) }, One(s.Splits[^1])));
        if (s.Splits.Count >= 1) forms.Add(One(s.Splits[^1]));
        Fit(_runSmall, room, forms.Count > 0 ? forms.ToArray() : new[] { Array.Empty<Run>() });
    }

    static Run Sep(double size) => R("  ·  ", size, Faint);

    static Run[] Join(params Run[][] parts) => parts.SelectMany(p => p).ToArray();

    /// <summary>
    /// <paramref name="t"/> as the first of <paramref name="forms"/> that fits <paramref name="room"/> (the last when none
    /// does): a part is left out before any figure is cut.
    /// </summary>
    static void Fit(TextBlock t, double room, params Run[][] forms)
    {
        foreach (var form in forms)
        {
            t.Inlines.Clear();
            foreach (var r in form) t.Inlines.Add(new Run(r.Text) { FontSize = r.FontSize, Foreground = r.Foreground, FontFamily = r.FontFamily, FontWeight = r.FontWeight });
            t.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            if (t.DesiredSize.Width <= room) return;
        }
    }
}
