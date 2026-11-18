using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using static KinahLedger.Ui.HudKit;

namespace KinahLedger.Ui;

/// <summary>
/// Holo in a dungeon run (the "run*" values, Ui/HudRunBuddy.cs; Steven, 2026-11-18: "Yes, all looks"): a projected run timer
/// on the same glass. Over it, in the small mono labels, the dungeon and its mode with "RUN 3 TODAY ◇ 38 CLEARS"; the run
/// clock big, white, green ahead of your fastest run's pace and coral behind, the pace on its foot, then after the cyan rule
/// BEST over your fastest (as SESSION sits over the session). Under a hairline the last bosses' splits against your best split
/// for each (amber for a new best split), then the pickups as the farming line has them. After the final boss, for a few
/// seconds: ★ NEW BEST in amber with the clock, or "Cleared in 4:17 | 2nd fastest", then the run's worth against your average
/// run here. Between runs in the dungeon: today here big with FASTEST beside it, its runs and average clear under it. No ring:
/// the clock is the figure.
/// </summary>
sealed partial class HudHolo
{
    void DrawRun(RunBuddy.State s)
    {
        double cap = FightWidth - 10 - _lineBody.Margin.Left - _lineBody.Margin.Right;
        var tone = s.PaceSign < 0 ? Green : s.PaceSign > 0 ? Coral : White;

        // the big row: the fullest form that fits
        Func<FrameworkElement>[] forms;
        int fastestUpTo = -1;   // the forms that show your fastest (between runs: the head then leaves it out)
        if (s.Run)
        {
            var pace = s.Pace.Length > 0 ? new Func<FrameworkElement>[] { () => Foot(s.Pace, tone, 15, 6) } : Array.Empty<Func<FrameworkElement>>();
            var best = s.Best.Length > 0 ? new Func<FrameworkElement>[] { RunRule, () => Col(L.T("BEST"), s.Best) }
                : s.Verdict == "first" ? new Func<FrameworkElement>[] { RunRule, () => Words(L.T("First clear here"), Pale, 13) } : Array.Empty<Func<FrameworkElement>>();
            forms = new[] { Spread(Row(() => Big(s.Clock, tone), pace), best), Row(() => Big(s.Clock, tone), pace), Row(() => Big(s.Clock, tone)) };
        }
        else if (s.Clear)
        {
            if (s.Verdict == "best")
            {
                // ★ NEW BEST 4:01 −0:11, the words amber; else the clock first with the words smaller after it
                var pace = s.Pace.Length > 0 ? new Func<FrameworkElement>[] { () => Foot(s.Pace, Green, 15, 6) } : Array.Empty<Func<FrameworkElement>>();
                Func<FrameworkElement>[] nb = { () => Foot("★ " + L.T("NEW BEST"), Amber, 19, 0, right: 10, bottom: 3) };
                Func<FrameworkElement>[] clock = { () => Big(s.Clock, White) };
                forms = new[] { Row(null, nb, clock, pace), Row(null, nb, clock), Row(() => Big(s.Clock, White), new Func<FrameworkElement>[] { () => Foot(L.T("NEW BEST"), Amber, 14, 7) }), Row(() => Big(s.Clock, White)) };
            }
            else
            {
                // "Cleared in 4:17", the clock big wherever the language puts it, then after the rule the verdict ("2nd fastest") or your best
                var (before, after) = ClearedWords();
                var verdict = RunBuddy.Verdict(s);
                var vb = s.Verdict is "rank" or "first" ? new Func<FrameworkElement>[] { RunRule, () => Words(verdict, Amber, 15, FontWeights.Bold) }
                    : s.Best.Length > 0 ? new Func<FrameworkElement>[] { RunRule, () => Col(L.T("BEST"), s.Best) } : Array.Empty<Func<FrameworkElement>>();
                var cleared = new List<Func<FrameworkElement>>();
                if (before.Length > 0) cleared.Add(() => Foot(before, Lite, 13, 0, right: 6, bottom: 6, weight: FontWeights.SemiBold));
                cleared.Add(() => Big(s.Clock, White));
                if (after.Length > 0) cleared.Add(() => Foot(after, Lite, 13, 6, bottom: 6, weight: FontWeights.SemiBold));
                forms = new[] { Spread(Row(null, cleared.ToArray()), vb), Spread(Row(() => Big(s.Clock, White)), vb), Row(() => Big(s.Clock, White)) };
            }
        }
        else if (s.TodayRuns.Length > 0)
        {
            // today here big ("335K today here"), your fastest after the rule as the session sits; its runs and average under it
            Func<FrameworkElement> hero = s.TodayKinah.Length > 0 ? () => Big(s.TodayKinah, White) : () => Big(RunsWord(s.TodayRuns), White, 21);
            var unit = new Func<FrameworkElement>[] { () => Foot(L.T("today here"), Lite, 14, 5, weight: FontWeights.SemiBold) };
            var fastest = s.Best.Length > 0 ? new Func<FrameworkElement>[] { RunRule, () => Col(L.T("fastest").ToUpperInvariant(), s.Best) } : Array.Empty<Func<FrameworkElement>>();
            forms = new[] { Spread(Row(hero, unit), fastest), Spread(Row(hero), fastest), Row(hero, unit), Row(hero) };
            if (fastest.Length > 0) fastestUpTo = 1;
        }
        else if (s.Best.Length > 0) forms = new[] { Row(() => Big(s.Best, White), new Func<FrameworkElement>[] { () => Foot(L.T("fastest"), Lite, 14, 5, weight: FontWeights.SemiBold) }), Row(() => Big(s.Best, White)) };
        else forms = new[] { Row(() => Words(L.T("First clear here"), Pale, 14)) };
        var row = FitForm(cap, out int at, forms);

        // the head: the dungeon and its mode, then the count, in the small mono labels (the parts give way in turn)
        var count = RunCount(s, at <= fastestUpTo);
        var today = s.Today.Length > 0 && !s.Between ? L.F("Run {0} today", s.Today) : "";
        var clears = ClearsWord(s.Clears);
        var head = FitForm(cap, () => Head(s.Head, count), () => Head(s.Head, today.Length > 0 ? today : clears), () => Head(s.Head, clears), () => Head(s.Head, ""), () => Head(s.Place, ""));
        head.Margin = new Thickness(0, 0, 0, 2);
        _lineBody.Children.Add(head);
        _lineBody.Children.Add(row);

        // under a hairline: the splits, or once the clear's rewards were read its worth against your average run; between runs, today's runs
        FrameworkElement? sub = null;
        if (s.Clear && s.Kinah.Length > 0)
        {
            var avg = s.Avg.Length > 0 ? new Func<FrameworkElement>[] { Sep, () => Words(L.T("avg") + " ", Lite, 12, FontWeights.SemiBold), () => Words(s.Avg, White, 13.5, FontWeights.Bold) } : Array.Empty<Func<FrameworkElement>>();
            sub = FitForm(cap, Row(() => Words(s.Kinah, Amber, 13.5, FontWeights.Bold), avg), Row(() => Words(s.Kinah, Amber, 13.5, FontWeights.Bold)));
        }
        else if ((s.Run || s.Clear) && s.Splits.Count > 0)
        {
            Func<FrameworkElement>[] One(RunBuddy.Split x)
            {
                var parts = new List<Func<FrameworkElement>> { () => Words(x.Boss + " ", Lite, 12, FontWeights.SemiBold), () => Words(x.Clock, White, 12.5, FontWeights.Bold) };
                if (x.Delta.Length > 0) parts.Add(() => Words(" " + x.Delta, x.Best ? Amber : x.Sign < 0 ? Green : x.Sign > 0 ? Coral : LiteDim, 12.5, FontWeights.Bold));
                return parts.ToArray();
            }
            var splitForms = new List<Func<FrameworkElement>>();
            if (s.Splits.Count >= 2) splitForms.Add(Row(null, One(s.Splits[^2]), new Func<FrameworkElement>[] { Sep }, One(s.Splits[^1])));
            splitForms.Add(Row(null, One(s.Splits[^1])));
            sub = FitForm(cap, splitForms.ToArray());
        }
        else if (s.Between && s.TodayRuns.Length > 0)
        {
            var runs = s.TodayKinah.Length > 0 ? new Func<FrameworkElement>[] { () => Words(RunsWord(s.TodayRuns), White, 13, FontWeights.SemiBold) } : Array.Empty<Func<FrameworkElement>>();
            var avg = s.TodayAvg.Length > 0 ? new Func<FrameworkElement>[] { () => Words(L.T("avg") + " ", Lite, 12, FontWeights.SemiBold), () => Words(s.TodayAvg, White, 13.5, FontWeights.Bold) } : Array.Empty<Func<FrameworkElement>>();
            if (runs.Length + avg.Length > 0)
                sub = FitForm(cap, Row(null, runs, runs.Length > 0 && avg.Length > 0 ? new Func<FrameworkElement>[] { Sep } : Array.Empty<Func<FrameworkElement>>(), avg), Row(null, runs.Length > 0 ? runs : avg));
        }
        // the pickups (the best drop and the last three, else the three: they stay before any figure goes)
        var strips = RunStrips(StripLook);
        var strip = strips.FirstOrDefault(x => Wants(x) <= cap + .5) ?? strips.LastOrDefault();
        if (sub != null || strip != null) _lineBody.Children.Add(Hairline(new Thickness(0, 7, 0, 5)));
        if (sub != null) _lineBody.Children.Add(sub);
        if (strip != null)
        {
            var loot = Loot(strip, Math.Max(Wants(row), Math.Max(Wants(head), sub != null ? Wants(sub) : 0)));
            if (sub != null) loot.Margin = new Thickness(0, 6, 0, 0);
            _lineBody.Children.Add(loot);
        }
    }

    /// <summary>The head: the dungeon and its mode, the count after a ◇, in the small mono capitals.</summary>
    static FrameworkElement Head(string head, string count)
    {
        var p = new StackPanel { Orientation = Orientation.Horizontal };
        p.Children.Add(Label(head.ToUpperInvariant()));
        if (count.Length > 0) { p.Children.Add(Tt("  ◇  ", 7.5, Frozen(Tac.A(CyanC, .6)), Face, FontWeights.Bold)); var c = Label(count.ToUpperInvariant()); c.Opacity = .62; p.Children.Add(c); }
        return p;
    }

    /// <summary>The big figure (the clock), as the hour is drawn.</summary>
    static FrameworkElement Big(string s, Brush fg, double size = 29) => Tt(s, size, fg, Face, FontWeights.Bold);

    /// <summary>A smaller part on the big figure's foot (the pace, a unit, the clear's words).</summary>
    static FrameworkElement Foot(string s, Brush fg, double size, double left, double right = 0, double bottom = 5, FontWeight? weight = null)
    {
        var t = Tt(s, size, fg, Face, weight ?? FontWeights.Bold);
        t.VerticalAlignment = VerticalAlignment.Bottom;
        t.Margin = new Thickness(left, 0, right, bottom);
        return t;
    }

    static FrameworkElement Words(string s, Brush fg, double size, FontWeight? weight = null) => Tt(s, size, fg, Face, weight ?? FontWeights.SemiBold);

    /// <summary>A labelled figure after the rule, as SESSION sits over the session: "BEST" over 4:12.</summary>
    static FrameworkElement Col(string label, string fig)
    {
        var p = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        p.Children.Add(Label(label));
        var t = Tt(fig, 19, White, Face, FontWeights.SemiBold); t.Margin = new Thickness(0, -1, 0, 0);
        p.Children.Add(t);
        return p;
    }

    /// <summary>The cyan rule between the big figure and the labelled one (as before the session).</summary>
    static FrameworkElement RunRule() => new TacSketch((dc, s) =>
        dc.DrawRectangle(Tac.Grad(true, (Tac.A(CyanC, 0), 0), (Tac.A(CyanC, .55), .5), (Tac.A(CyanC, 0), 1)), null, new Rect(0, 0, 1, s.Height)))
        { Width = 1, Height = 36, Margin = new Thickness(14, 0, 13, 0), VerticalAlignment = VerticalAlignment.Center };

    static FrameworkElement Sep() => Tt("  ◇  ", 8, Frozen(Tac.A(CyanC, .6)), Face, FontWeights.Bold);

    /// <summary>
    /// A big row's maker with a labelled part at the glass's right edge (your best, the verdict), as a run timer sets its
    /// comparison: <paramref name="left"/> at the left, <paramref name="right"/> out at the right of the widest line.
    /// </summary>
    static Func<FrameworkElement> Spread(Func<FrameworkElement> left, Func<FrameworkElement>[] right) => right.Length == 0 ? left : () =>
    {
        var g = new Grid();
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        g.Children.Add(left());
        var r = Row(null, right)();
        Grid.SetColumn(r, 2); g.Children.Add(r);
        return g;
    };

    /// <summary>A row's maker: <paramref name="first"/> (when given) and the parts after it, made fresh each time.</summary>
    static Func<FrameworkElement> Row(Func<FrameworkElement>? first, params Func<FrameworkElement>[][] parts) => () =>
    {
        var p = new StackPanel { Orientation = Orientation.Horizontal };
        if (first != null) p.Children.Add(first());
        foreach (var make in parts.SelectMany(x => x))
        {
            var e = make();
            if (e.VerticalAlignment == VerticalAlignment.Stretch) e.VerticalAlignment = VerticalAlignment.Center;
            p.Children.Add(e);
        }
        return p;
    };
}
