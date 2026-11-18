using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using static KinahLedger.Ui.HudKit;

namespace KinahLedger.Ui;

/// <summary>
/// Glass's Buddy in a dungeon run (the "run*" values, MainWindow.HudRun.cs; Steven, 2026-11-18): a speedrun timer on the same
/// pill. Over it, small, the dungeon and its mode with "Run 3 today · 38 clears"; the run clock big in white, green ahead of
/// your fastest run's pace and red behind, the pace and your best beside it; under it the last bosses' splits against your
/// best split for each. After the final boss, for a few seconds: the clear and how it ranks (a green "NEW BEST"), then the
/// run's worth against your average run here. Between runs in the dungeon: today here big, its runs and average clear under
/// it, your clears and fastest in the head. No ring: the pill keeps one shape through the run, the pickup strip beside it.
/// </summary>
sealed partial class HudModernGlass
{
    /// <summary>The run's line: the head over the pill's width, then the figures, the strip and the timer chip.</summary>
    void DrawRun(RunBuddy.State s, bool dim)
    {
        var row = new Grid();
        for (int i = 0; i < 4; i++) row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });   // (no lead), words, strip, timer
        const double Edge = 14;
        double cap = FightWidth - 8 - 2 * Edge;
        var tone = s.PaceSign < 0 ? Green : s.PaceSign > 0 ? Red : White;

        var words = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        var big = Tt("", 12, White, FontWeights.Bold);
        var small = Tt("", 11.5, Soft, FontWeights.Medium);
        small.Margin = new Thickness(0, 2, 0, 0);
        words.Children.Add(big); words.Children.Add(small);

        // the words' fullest forms first, the strip kept (the best drop and the last three, else the last three), the timer
        // chip on the line; then each given up in turn, the timer chip going up over the Buddy. Then the same for the words'
        // essentials (the big line whole, the newest split alone under it). With none of those, the last three stay and the
        // words take a shorter form.
        Fill(double.PositiveInfinity);
        double fullW = Mod.W(words), timerW = LineTimerW();
        double bigW = Mod.W(big);
        Fill(0);
        double needW = Math.Max(bigW, small.Inlines.Count > 0 ? Mod.W(small) : 0);
        var strips = PickStrip.Shown && Picks(Values).Any() ? StripFall(Strip).Select(l => PickStrip.Of(Values, l)).Where(x => x != null).Select(x => x!).ToList() : new List<FrameworkElement>();
        FrameworkElement? strip = strips.LastOrDefault();
        bool up = timerW > 0, found = false;
        foreach (var want in new[] { fullW, needW })
            foreach (var st in strips.Count > 0 ? strips.Cast<FrameworkElement?>() : new FrameworkElement?[] { null })
                foreach (var u in timerW > 0 ? new[] { false, true } : new[] { false })
                {
                    if (found) break;
                    double room = cap - (st != null ? Mod.W(st) + 14 : 0) - (u ? 0 : timerW);
                    if (want <= room + .5) { found = true; strip = st; up = u; }
                }
        _timerUp = up;
        _stripTimerW = strips.Count > 0 ? timerW : -1;
        _lineBody.Margin = new Thickness(Edge, 8, Edge, 9);
        Fill(cap - (strip != null ? Mod.W(strip) + 14 : 0) - (up ? 0 : timerW));
        if (strip != null)
        {
            strip.Margin = new Thickness(14, 0, 0, 0); strip.VerticalAlignment = VerticalAlignment.Center;
            if (dim) strip.Opacity = .6;
            Grid.SetColumn(strip, 2); row.Children.Add(strip);
        }
        Grid.SetColumn(words, 1); row.Children.Add(words);
        Grid.SetColumn(_lineTimer, 3);
        if (_lineTimer.Parent is Panel old) old.Children.Remove(_lineTimer);
        row.Children.Add(_lineTimer);

        // the head: the dungeon and its mode, the run's count today and your clears, over the whole pill
        var head = Tt("", 10.5, Dim, FontWeights.SemiBold);
        head.Margin = new Thickness(0, 0, 0, 3);
        var count = RunBuddy.Count(s);
        Fit(head, Math.Max(cap, Mod.W(row)),
            count.Length > 0 ? new[] { Ru(s.Head, Soft, 10.5), Ru("  ·  " + count, Dim, 10.5, FontWeights.SemiBold) } : new[] { Ru(s.Head, Soft, 10.5) },
            new[] { Ru(s.Head, Soft, 10.5) }, new[] { Ru(s.Place, Soft, 10.5) });
        if (dim) words.Opacity = head.Opacity = .6;
        _lineBody.Children.Add(head);
        _lineBody.Children.Add(row);
        small.Visibility = small.Inlines.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

        // the lines drawn in the room they have, each the fullest form that fits it (infinite room: the fullest)
        void Fill(double room)
        {
            if (s.Run)
            {
                Run[] clock = { Ru(s.Clock, tone, 24) };
                var pace = s.Pace.Length > 0 ? new[] { Ru("  " + s.Pace, tone, 14) } : Array.Empty<Run>();
                var best = s.Best.Length > 0 ? new[] { Ru("   " + L.T("best") + " ", Faint, 12, FontWeights.SemiBold), Ru(s.Best, Soft, 12.5) }
                    : s.Verdict == "first" ? new[] { Ru("   " + L.T("First clear here"), Dim, 12, FontWeights.SemiBold) } : Array.Empty<Run>();
                Fit(big, room, Join(clock, pace, best), Join(clock, pace), clock);
                SplitsIn(small, room);
            }
            else if (s.Clear)
            {
                Run[] clock = { Ru(s.Clock, White, 24) };
                if (s.Verdict == "best")
                {
                    // "NEW BEST 4:01 −0:11" in green, else the clock first with the words smaller after it
                    Run[] nb = { Ru(L.T("NEW BEST") + "  ", Green, 20) };
                    var pace = s.Pace.Length > 0 ? new[] { Ru("  " + s.Pace, Green, 14) } : Array.Empty<Run>();
                    Run[] after = { Ru("  " + L.T("NEW BEST"), Green, 14) };
                    Fit(big, room, Join(nb, clock, pace), Join(nb, clock), Join(clock, after), clock);
                }
                else
                {
                    var verdict = RunBuddy.Verdict(s);
                    var vb = verdict.Length > 0 ? new[] { Ru("   " + verdict, s.Verdict is "rank" or "first" ? Amber : Soft, 13.5) } : Array.Empty<Run>();
                    // "Cleared in 4:17": the clock big wherever the language puts it
                    var fmt = L.T("Cleared in {0}");
                    int at = fmt.IndexOf("{0}", StringComparison.Ordinal);
                    var cleared = new List<Run>();
                    if (at > 0) cleared.Add(Ru(fmt[..at], Dim, 12.5, FontWeights.SemiBold));
                    cleared.AddRange(clock);
                    if (at >= 0 && at + 3 < fmt.Length) cleared.Add(Ru(fmt[(at + 3)..], Dim, 12.5, FontWeights.SemiBold));
                    Fit(big, room, Join(cleared.ToArray(), vb), Join(clock, vb), clock);
                }
                // the run's worth against your average run here, once its rewards were read; until then its splits
                if (s.Kinah.Length > 0)
                {
                    Run[] kin = { Ru(s.Kinah, Amber, 12.5) };
                    Fit(small, room, s.Avg.Length > 0 ? Join(kin, new[] { Ru("  ·  ", Faint, 11.5, FontWeights.Medium), Ru(L.T("avg") + " ", Dim, 11.5, FontWeights.SemiBold), Ru(s.Avg, Soft, 12) }) : kin, kin);
                }
                else SplitsIn(small, room);
            }
            else
            {
                // today here big ("335K today here"), its runs and average clear under it; your clears and fastest are in the head
                if (s.TodayRuns.Length > 0)
                {
                    string runs = s.TodayRuns == "1" ? L.T("1 run") : L.F("{0} runs", s.TodayRuns);
                    var hero = s.TodayKinah.Length > 0 ? Ru(s.TodayKinah, White, 24) : Ru(runs, White, 20);
                    Run[] word = { Ru("  " + L.T("today here"), Faint, 12, FontWeights.SemiBold) };
                    Fit(big, room, Join(new[] { hero }, word), new[] { hero });
                    var more = new List<Run>();
                    if (s.TodayKinah.Length > 0) more.Add(Ru(runs, Soft, 11.5, FontWeights.SemiBold));
                    if (s.TodayAvg.Length > 0) { if (more.Count > 0) more.Add(Ru("  ·  ", Faint, 11.5, FontWeights.Medium)); more.Add(Ru(L.T("avg") + " ", Dim, 11.5, FontWeights.SemiBold)); more.Add(Ru(s.TodayAvg, White, 12)); }
                    Fit(small, room, more.ToArray(), more.Take(1).ToArray());
                }
                else if (s.Best.Length > 0)
                {
                    Fit(big, room, new[] { Ru(s.Best, White, 24), Ru("  " + L.T("fastest"), Faint, 12, FontWeights.SemiBold) });
                    Fit(small, room, Array.Empty<Run>());
                }
                else
                {
                    Fit(big, room, new[] { Ru(L.T("First clear here"), Soft, 14, FontWeights.SemiBold) });
                    Fit(small, room, Array.Empty<Run>());
                }
            }
        }

        // the last bosses' splits ("Kwapo 1:02 −0:05 · Duanka 2:44 +0:03"), the newest kept when both don't fit
        void SplitsIn(TextBlock t, double room)
        {
            Run[] One(RunBuddy.Split x)
            {
                var parts = new List<Run> { Ru(x.Boss + " ", Soft, 11.5, FontWeights.SemiBold), Ru(x.Clock, White, 12) };
                if (x.Delta.Length > 0) parts.Add(Ru(" " + x.Delta, x.Best ? Amber : x.Sign < 0 ? Green : x.Sign > 0 ? Red : Dim, 12));
                return parts.ToArray();
            }
            var forms = new List<Run[]>();
            if (s.Splits.Count >= 2) forms.Add(Join(One(s.Splits[^2]), new[] { Ru("  ·  ", Faint, 11.5, FontWeights.Medium) }, One(s.Splits[^1])));
            if (s.Splits.Count >= 1) forms.Add(One(s.Splits[^1]));
            Fit(t, room, forms.Count > 0 ? forms.ToArray() : new[] { Array.Empty<Run>() });
        }
    }

    /// <summary>A run of the line in Inter, bold unless said.</summary>
    static Run Ru(string text, Brush fg, double size = 11.5, FontWeight? weight = null) => new(text) { Foreground = fg, FontSize = size, FontWeight = weight ?? FontWeights.Bold };

    static Run[] Join(params Run[][] parts) => parts.SelectMany(p => p).ToArray();

    /// <summary><paramref name="t"/> as the first of <paramref name="forms"/> that fits <paramref name="room"/> (the last when none does): a part goes before a figure is cut.</summary>
    static void Fit(TextBlock t, double room, params Run[][] forms)
    {
        foreach (var form in forms)
        {
            t.Inlines.Clear();
            foreach (var r in form) t.Inlines.Add(new Run(r.Text) { Foreground = r.Foreground, FontSize = r.FontSize, FontWeight = r.FontWeight });
            if (Mod.W(t) <= room + .5) return;
        }
    }
}
