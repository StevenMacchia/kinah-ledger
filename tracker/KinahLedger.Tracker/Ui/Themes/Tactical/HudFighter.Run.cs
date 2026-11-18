using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using static KinahLedger.Ui.HudKit;

namespace KinahLedger.Ui;

/// <summary>
/// Fighter in a dungeon run (the "run*" values, Ui/HudRunBuddy.cs; Steven, 2026-11-18: "Yes, all looks"): the HUD's mission
/// clock in the brackets. Over it, small and dim, the dungeon and its mode with "RUN 3 TODAY · 38 CLEARS"; the run clock big,
/// phosphor green ahead of your fastest run's pace, red behind (pale with no pace yet), the pace beside it and BEST 4:12; under
/// it the last bosses' splits against your best split for each (amber for a new best split). After the final boss, for a few
/// seconds: ★ NEW BEST in amber with the clock, or "Cleared in 4:17 · 2ND FASTEST", then the run's worth against your average
/// run here. Between runs in the dungeon: today here big, its runs and average clear under it, your clears and fastest in the
/// head. No reticle: the clock is the figure. The pickup strip stays beside the big line (the last three before a figure goes).
/// </summary>
sealed partial class HudFighter
{
    /// <summary>The run's lines in the brackets: the head, the big line with the strip at its right, and the splits or the run's worth.</summary>
    void DrawRun(RunBuddy.State s)
    {
        _figs.Margin = new Thickness(0);
        double cap = FightWidth - 20 - _lineRow.Margin.Left - _lineRow.Margin.Right;
        var tone = s.PaceSign < 0 ? Phos : s.PaceSign > 0 ? Red : Pale;

        // the head: the dungeon and its mode, then the count ("RUN 3 TODAY · 38 CLEARS"), dim, in the HUD's capitals
        // (the parts give way in turn: this run's number today, kept over the clears in a run, then the clears)
        var count = RunBuddy.Count(s);
        var today = s.Today.Length > 0 && !s.Between ? L.F("Run {0} today", s.Today) : "";
        var clears = ClearsWord(s.Clears);
        var head = FitForm(cap,
            () => HeadRow(s.Head, count), () => HeadRow(s.Head, today.Length > 0 ? today : clears), () => HeadRow(s.Head, clears), () => HeadRow(s.Head, ""), () => HeadRow(s.Place, ""));
        head.Margin = new Thickness(1, 0, 0, 1);
        _figs.Children.Add(head);

        // the big line: the fullest form beside the strip (the best drop and the three, else the three), then shorter ones
        Func<FrameworkElement>[] forms;
        if (s.Run)
        {
            var best = s.Best.Length > 0 ? new Func<FrameworkElement>[] { Dot, () => Label(L.T("BEST")), () => Fig(s.Best, Pale, 14) }
                : s.Verdict == "first" ? new Func<FrameworkElement>[] { Dot, () => Label(L.T("First clear here").ToUpperInvariant()) } : Array.Empty<Func<FrameworkElement>>();
            var pace = s.Pace.Length > 0 ? new Func<FrameworkElement>[] { () => Small(s.Pace, tone, 14) } : Array.Empty<Func<FrameworkElement>>();
            forms = new Func<FrameworkElement>[]
            {
                () => Line(Big(s.Clock, tone), pace, best), () => Line(Big(s.Clock, tone), pace), () => Line(Big(s.Clock, tone)),
            };
        }
        else if (s.Clear)
        {
            if (s.Verdict == "best")
            {
                // ★ NEW BEST 4:01 −0:11, the words amber; else the clock first with the words smaller after it
                var pace = s.Pace.Length > 0 ? new Func<FrameworkElement>[] { () => Small(s.Pace, Phos, 14) } : Array.Empty<Func<FrameworkElement>>();
                FrameworkElement Nb()
                {
                    var t = Halo("★ " + L.T("NEW BEST"), 19, Amber, FontWeights.Bold); t.Margin = new Thickness(0, 0, 10, 3); t.VerticalAlignment = VerticalAlignment.Bottom;
                    return new StackPanel { Orientation = Orientation.Horizontal, Children = { t, Big(s.Clock, Pale) } };
                }
                forms = new Func<FrameworkElement>[]
                {
                    () => Line(Nb(), pace), () => Line(Nb()),
                    () => Line(Big(s.Clock, Pale), new Func<FrameworkElement>[] { () => Small(L.T("NEW BEST"), Amber, 13) }), () => Line(Big(s.Clock, Pale)),
                };
            }
            else
            {
                // "CLEARED IN 4:17 · 2ND FASTEST": the clock big wherever the language puts it
                var (before, after) = ClearedWords();
                var verdict = RunBuddy.Verdict(s);
                var vTone = s.Verdict is "rank" or "first" ? Amber : PhosDim;
                var vb = verdict.Length > 0 ? new Func<FrameworkElement>[] { Dot, () => Small(verdict.ToUpperInvariant(), vTone, 13, 0) } : Array.Empty<Func<FrameworkElement>>();
                FrameworkElement Cleared()
                {
                    var p = new StackPanel { Orientation = Orientation.Horizontal };
                    if (before.Length > 0) { var b = Label(before.ToUpperInvariant()); b.VerticalAlignment = VerticalAlignment.Bottom; b.Margin = new Thickness(0, 0, 6, 7); p.Children.Add(b); }
                    p.Children.Add(Big(s.Clock, Pale));
                    if (after.Length > 0) { var a = Label(after.ToUpperInvariant()); a.VerticalAlignment = VerticalAlignment.Bottom; a.Margin = new Thickness(6, 0, 0, 7); p.Children.Add(a); }
                    return p;
                }
                forms = new Func<FrameworkElement>[] { () => Line(Cleared(), vb), () => Line(Big(s.Clock, Pale), vb), () => Line(Big(s.Clock, Pale)) };
            }
        }
        else if (s.TodayRuns.Length > 0)
        {
            // between runs: today here big ("335K TODAY HERE"); its runs and average clear under it
            FrameworkElement Hero() => s.TodayKinah.Length > 0 ? Big(s.TodayKinah, Phos) : Big(RunsWord(s.TodayRuns).ToUpperInvariant(), Phos, 20);
            forms = new Func<FrameworkElement>[] { () => Line(Hero(), new Func<FrameworkElement>[] { () => Unit(L.T("today here")) }), () => Line(Hero()) };
        }
        else if (s.Best.Length > 0) forms = new Func<FrameworkElement>[] { () => Line(Big(s.Best, Pale), new Func<FrameworkElement>[] { () => Unit(L.T("fastest")) }), () => Line(Big(s.Best, Pale)) };
        else forms = new Func<FrameworkElement>[] { () => Halo(L.T("First clear here").ToUpperInvariant(), 13, PhosDim, FontWeights.Bold) };

        const double air = 12;
        var (big, strip) = Beside(cap, air, RunStrips(StripLook), forms);
        if (strip != null)
        {
            var g = new Grid();
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MinWidth = air });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            g.Children.Add(big);
            strip.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(strip, 2); g.Children.Add(strip);
            _figs.Children.Add(g);
        }
        else _figs.Children.Add(big);

        // under it: the splits, or once the clear's rewards were read its worth against your average run; between runs, today's runs
        FrameworkElement? sub = null;
        if (s.Clear && s.Kinah.Length > 0)
        {
            sub = FitForm(cap,
                () => Line(Small(s.Kinah, Amber, 14, 0), s.Avg.Length > 0 ? new Func<FrameworkElement>[] { Dot, () => Label(L.T("avg").ToUpperInvariant()), () => Fig(s.Avg, Pale, 13) } : Array.Empty<Func<FrameworkElement>>()),
                () => Line(Small(s.Kinah, Amber, 14, 0)));
        }
        else if (s.Run || s.Clear) sub = SplitsRow(s, cap);
        else if (s.TodayRuns.Length > 0)
        {
            var runs = s.TodayKinah.Length > 0 ? new Func<FrameworkElement>[] { () => Small(RunsWord(s.TodayRuns).ToUpperInvariant(), Pale, 13, 0) } : Array.Empty<Func<FrameworkElement>>();
            var avg = s.TodayAvg.Length > 0 ? new Func<FrameworkElement>[] { () => Label(L.T("avg").ToUpperInvariant()), () => Fig(s.TodayAvg, Pale, 13) } : Array.Empty<Func<FrameworkElement>>();
            if (runs.Length + avg.Length > 0)
                sub = FitForm(cap, () => Line(null, runs, runs.Length > 0 && avg.Length > 0 ? new Func<FrameworkElement>[] { Dot } : Array.Empty<Func<FrameworkElement>>(), avg),
                    () => Line(null, runs.Length > 0 ? runs : avg));
        }
        if (sub != null) { sub.Margin = new Thickness(1, 0, 0, 0); _figs.Children.Add(sub); }
    }

    /// <summary>The last bosses' splits ("KWAPO 1:02 −0:05 · DUANKA 2:30 −0:11"), the newest kept when both don't fit; null before the first.</summary>
    FrameworkElement? SplitsRow(RunBuddy.State s, double cap)
    {
        if (s.Splits.Count == 0) return null;
        Func<FrameworkElement>[] One(RunBuddy.Split x)
        {
            var parts = new List<Func<FrameworkElement>> { () => Label(x.Boss.ToUpperInvariant()), () => Fig(x.Clock, Pale, 12.5) };
            if (x.Delta.Length > 0) parts.Add(() => Fig(x.Delta, x.Best ? Amber : x.Sign < 0 ? Phos : x.Sign > 0 ? Red : PhosDim, 12.5));
            return parts.ToArray();
        }
        var forms = new List<Func<FrameworkElement>>();
        if (s.Splits.Count >= 2) forms.Add(() => Line(null, One(s.Splits[^2]), new Func<FrameworkElement>[] { Dot }, One(s.Splits[^1])));
        forms.Add(() => Line(null, One(s.Splits[^1])));
        return FitForm(cap, forms.ToArray());
    }

    /// <summary>The head: the dungeon and its mode bright, the count after it dim, in capitals.</summary>
    static FrameworkElement HeadRow(string head, string count)
    {
        var p = new StackPanel { Orientation = Orientation.Horizontal };
        p.Children.Add(Halo(head.ToUpperInvariant(), 10.5, Phos, FontWeights.Bold));
        if (count.Length > 0) { var d = Halo("·", 10.5, PhosDim, FontWeights.Bold); d.Margin = new Thickness(6, 0, 6, 0); p.Children.Add(d); p.Children.Add(Halo(count.ToUpperInvariant(), 10.5, PhosDim, FontWeights.Bold)); }
        return p;
    }

    /// <summary>The big figure (the clock), as the hour is drawn.</summary>
    static FrameworkElement Big(string s, Brush fg, double size = 27) => Halo(s, size, fg, FontWeights.Bold);

    /// <summary>A smaller figure on the big line's foot (the pace), or a line's own figure with <paramref name="left"/> 0.</summary>
    static FrameworkElement Small(string s, Brush fg, double size, double left = 7)
    {
        var h = Halo(s, size, fg, FontWeights.Bold);
        h.VerticalAlignment = left > 0 ? VerticalAlignment.Bottom : VerticalAlignment.Center;
        h.Margin = new Thickness(left, 0, 0, left > 0 ? 5 : 0);
        return h;
    }

    /// <summary>A unit after the big figure ("TODAY HERE"), as the hour's "/h".</summary>
    static FrameworkElement Unit(string s) { var u = Halo(s.ToUpperInvariant(), 12, PhosDim, FontWeights.Bold); u.VerticalAlignment = VerticalAlignment.Bottom; u.Margin = new Thickness(6, 0, 0, 6); return u; }

    /// <summary>
    /// A row of parts: <paramref name="first"/> (when given, the big figure) and the parts after it, made fresh. After a big
    /// figure the parts sit together on its foot, as the hour's unit does.
    /// </summary>
    static FrameworkElement Line(FrameworkElement? first, params Func<FrameworkElement>[][] parts)
    {
        var p = new StackPanel { Orientation = Orientation.Horizontal };
        var tail = p;
        if (first != null)
        {
            p.Children.Add(first);
            if (parts.Any(x => x.Length > 0))
            {
                tail = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 0, 5) };
                p.Children.Add(tail);
            }
        }
        foreach (var make in parts.SelectMany(x => x))
        {
            var e = make();
            if (tail == p && p.Children.Count == 0) e.Margin = new Thickness(0, e.Margin.Top, e.Margin.Right, e.Margin.Bottom);   // a line's first part starts at its edge
            if (tail != p) { e.VerticalAlignment = VerticalAlignment.Center; e.Margin = new Thickness(e.Margin.Left, 0, e.Margin.Right, 0); }
            else if (e.VerticalAlignment == VerticalAlignment.Stretch) e.VerticalAlignment = VerticalAlignment.Center;
            tail.Children.Add(e);
        }
        return p;
    }

    /// <summary>
    /// The big line and the strip beside it in <paramref name="cap"/>: the fullest form with the best drop and the three, else
    /// with the three alone; then the shorter forms beside the three (they stay before a figure goes).
    /// </summary>
    static (FrameworkElement Big, FrameworkElement? Strip) Beside(double cap, double air, List<FrameworkElement> strips, Func<FrameworkElement>[] forms)
    {
        if (strips.Count == 0) return (FitForm(cap, forms), null);
        var first = forms[0]();
        double fw = Wants(first);
        foreach (var st in strips) if (fw + air + Wants(st) <= cap + .5) return (first, st);
        var three = strips[^1];
        return (forms.Length > 1 ? FitForm(cap - air - Wants(three), forms[1..]) : first, three);
    }
}
