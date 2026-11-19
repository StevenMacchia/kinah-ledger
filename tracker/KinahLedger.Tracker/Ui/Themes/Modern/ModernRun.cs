using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;

namespace KinahLedger.Ui;

/// <summary>
/// A Modern look's inks for the run's words: its main white, its softer and quieter whites, its separators' tone, ahead
/// (green), behind (red), a new best split or a ranked clear (gold), the run's worth, and the sizes of its big figure and
/// the line under it.
/// </summary>
sealed record RunInk(Brush Hero, Brush Soft, Brush Dim, Brush Faint, Brush Green, Brush Red, Brush Gold, Brush Worth, double Clock = 24, double Sub = 11.5);

/// <summary>
/// The Buddy's dungeon run in the Modern looks Mono, Line and Fade (Steven, 2026-11-18: "Yes, all looks"): the same words
/// and the same falls as Glass's (HudModernGlass.Run.cs), in each look's own inks and sizes. A speedrun timer: the run clock
/// big, green ahead of your fastest run's pace and red behind, "best 4:12" beside it, the last bosses' splits under it;
/// after the final boss NEW BEST or "Cleared in 4:17 · 2nd fastest" and the run's worth against your average; between runs
/// today here. Each line takes the fullest form that fits its room: a part goes before a figure is cut.
/// </summary>
static class ModernRun
{
    /// <summary>The run's big line and the line under it, each the fullest form that fits <paramref name="room"/> (infinite: the fullest).</summary>
    public static void Fill(TextBlock big, TextBlock small, RunBuddy.State s, double room, RunInk k)
    {
        double C(double glass) => glass * k.Clock / 24;   // Glass's sizes, at the look's own figure size
        double S(double glass) => glass * k.Sub / 11.5;
        if (s.Run)
        {
            var tone = s.PaceSign < 0 ? k.Green : s.PaceSign > 0 ? k.Red : k.Hero;
            Run[] clock = { Ru(s.Clock, tone, k.Clock) };
            var pace = s.Pace.Length > 0 ? new[] { Ru("  " + s.Pace, tone, C(14)) } : Array.Empty<Run>();
            var best = s.Best.Length > 0 ? new[] { Ru("   " + L.T("best") + " ", k.Faint, C(12), FontWeights.SemiBold), Ru(s.Best, k.Soft, C(12.5)) }
                : s.Verdict == "first" ? new[] { Ru("   " + L.T("First clear here"), k.Dim, C(12), FontWeights.SemiBold) } : Array.Empty<Run>();
            Fit(big, room, Join(clock, pace, best), Join(clock, pace), clock);
            Splits(small, s, room, k);
        }
        else if (s.Clear)
        {
            Run[] clock = { Ru(s.Clock, k.Hero, k.Clock) };
            if (s.Verdict == "best")
            {
                // "NEW BEST 4:01 −0:11" in green, else the clock first with the words smaller after it
                Run[] nb = { Ru(L.T("NEW BEST") + "  ", k.Green, C(20)) };
                var pace = s.Pace.Length > 0 ? new[] { Ru("  " + s.Pace, k.Green, C(14)) } : Array.Empty<Run>();
                Run[] after = { Ru("  " + L.T("NEW BEST"), k.Green, C(14)) };
                Fit(big, room, Join(nb, clock, pace), Join(nb, clock), Join(clock, after), clock);
            }
            else
            {
                var verdict = RunBuddy.Verdict(s);
                var vb = verdict.Length > 0 ? new[] { Ru("   " + verdict, s.Verdict is "rank" or "first" ? k.Gold : k.Soft, C(13.5)) } : Array.Empty<Run>();
                // "Cleared in 4:17": the clock big wherever the language puts it
                var fmt = L.T("Cleared in {0}");
                int at = fmt.IndexOf("{0}", StringComparison.Ordinal);
                var cleared = new List<Run>();
                if (at > 0) cleared.Add(Ru(fmt[..at], k.Dim, C(12.5), FontWeights.SemiBold));
                cleared.AddRange(clock);
                if (at >= 0 && at + 3 < fmt.Length) cleared.Add(Ru(fmt[(at + 3)..], k.Dim, C(12.5), FontWeights.SemiBold));
                Fit(big, room, Join(cleared.ToArray(), vb), Join(clock, vb), clock);
            }
            // the run's worth against your average run here, once its rewards were read; until then its splits
            if (s.Kinah.Length > 0)
            {
                Run[] kin = { Ru(s.Kinah, k.Worth, S(12.5)) };
                Fit(small, room, s.Avg.Length > 0 ? Join(kin, new[] { Ru("  ·  ", k.Faint, S(11.5), FontWeights.Medium), Ru(L.T("avg") + " ", k.Dim, S(11.5), FontWeights.SemiBold), Ru(s.Avg, k.Soft, S(12)) }) : kin, kin);
            }
            else Splits(small, s, room, k);
        }
        else if (s.TodayRuns.Length > 0)
        {
            // today here big ("335K today here"), its runs and average clear under it; your clears and fastest are in the head
            string runs = s.TodayRuns == "1" ? L.T("1 run") : L.F("{0} runs", s.TodayRuns);
            var hero = s.TodayKinah.Length > 0 ? Ru(s.TodayKinah, k.Hero, k.Clock) : Ru(runs, k.Hero, C(20));
            Run[] word = { Ru("  " + L.T("today here"), k.Faint, C(12), FontWeights.SemiBold) };
            Fit(big, room, Join(new[] { hero }, word), new[] { hero });
            var more = new List<Run>();
            if (s.TodayKinah.Length > 0) more.Add(Ru(runs, k.Soft, S(11.5), FontWeights.SemiBold));
            if (s.TodayAvg.Length > 0) { if (more.Count > 0) more.Add(Ru("  ·  ", k.Faint, S(11.5), FontWeights.Medium)); more.Add(Ru(L.T("avg") + " ", k.Dim, S(11.5), FontWeights.SemiBold)); more.Add(Ru(s.TodayAvg, k.Hero, S(12))); }
            Fit(small, room, more.ToArray(), more.Take(1).ToArray());
        }
        else if (s.Best.Length > 0)
        {
            Fit(big, room, new[] { Ru(s.Best, k.Hero, k.Clock), Ru("  " + L.T("fastest"), k.Faint, C(12), FontWeights.SemiBold) });
            Fit(small, room, Array.Empty<Run>());
        }
        else
        {
            Fit(big, room, new[] { Ru(L.T("First clear here"), k.Soft, C(14), FontWeights.SemiBold) });
            Fit(small, room, Array.Empty<Run>());
        }
    }

    /// <summary>The head: the dungeon and its mode, then "Run 3 today · 38 clears" (between runs your clears and fastest), the fullest that fits.</summary>
    public static void Head(TextBlock head, RunBuddy.State s, double room, RunInk k, double size = 10.5)
    {
        var count = RunBuddy.Count(s);
        Fit(head, room,
            count.Length > 0 ? new[] { Ru(s.Head, k.Soft, size), Ru("  ·  " + count, k.Dim, size, FontWeights.SemiBold) } : new[] { Ru(s.Head, k.Soft, size) },
            new[] { Ru(s.Head, k.Soft, size) }, new[] { Ru(s.Place, k.Soft, size) });
    }

    /// <summary>
    /// The words' widths for <see cref="Place"/>: their fullest forms, and their essentials (the big line in its shortest
    /// form, the newest split alone).
    /// </summary>
    public static (double Full, double Need) Widths(TextBlock big, TextBlock small, RunBuddy.State s, RunInk k)
    {
        double W() => Math.Max(Mod.W(big), small.Inlines.Count > 0 ? Mod.W(small) : 0);
        Fill(big, small, s, double.PositiveInfinity, k);
        double full = W();
        Fill(big, small, s, 0, k);
        return (full, W());
    }

    /// <summary>
    /// What the line keeps beside the words in <paramref name="cap"/> px: the words' fullest forms first with the strip (the
    /// best drop and the last three, whole: <see cref="ModernLook.RunStrips"/>) and the timer on the line; then the timer going
    /// up over the Buddy. Then the same for the words' essentials. With none of those the strip stays and the words take a
    /// shorter form. Returns the strip kept, whether the timer goes up, and the words' room.
    /// </summary>
    public static (FrameworkElement? Strip, bool Up, double Room) Place(double cap, double fullW, double needW, double timerW, IReadOnlyList<FrameworkElement> strips, double air)
    {
        FrameworkElement? strip = strips.LastOrDefault();
        bool up = timerW > 0, found = false;
        foreach (var want in new[] { fullW, needW })
            foreach (var st in strips.Count > 0 ? strips.Cast<FrameworkElement?>() : new FrameworkElement?[] { null })
                foreach (var u in timerW > 0 ? new[] { false, true } : new[] { false })
                {
                    if (found) break;
                    double room = cap - (st != null ? Mod.W(st) + air : 0) - (u ? 0 : timerW);
                    if (want <= room + .5) { found = true; strip = st; up = u; }
                }
        return (strip, up, cap - (strip != null ? Mod.W(strip) + air : 0) - (up ? 0 : timerW));
    }

    /// <summary>The last bosses' splits ("Kwapo 1:02 −0:05 · Duanka 2:44 +0:03"), the newest kept when both don't fit.</summary>
    static void Splits(TextBlock t, RunBuddy.State s, double room, RunInk k)
    {
        double S(double glass) => glass * k.Sub / 11.5;
        Run[] One(RunBuddy.Split x)
        {
            var parts = new List<Run> { Ru(x.Boss + " ", k.Soft, S(11.5), FontWeights.SemiBold), Ru(x.Clock, k.Hero, S(12)) };
            if (x.Delta.Length > 0) parts.Add(Ru(" " + x.Delta, x.Best ? k.Gold : x.Sign < 0 ? k.Green : x.Sign > 0 ? k.Red : k.Dim, S(12)));
            return parts.ToArray();
        }
        var forms = new List<Run[]>();
        if (s.Splits.Count >= 2) forms.Add(Join(One(s.Splits[^2]), new[] { Ru("  ·  ", k.Faint, S(11.5), FontWeights.Medium) }, One(s.Splits[^1])));
        if (s.Splits.Count >= 1) forms.Add(One(s.Splits[^1]));
        Fit(t, room, forms.Count > 0 ? forms.ToArray() : new[] { Array.Empty<Run>() });
    }

    /// <summary>A run of the line in the look's face, bold unless said.</summary>
    static Run Ru(string text, Brush fg, double size, FontWeight? weight = null) => new(text) { Foreground = fg, FontSize = size, FontWeight = weight ?? FontWeights.Bold };

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
