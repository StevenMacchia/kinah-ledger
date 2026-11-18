using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using static KinahLedger.Ui.HudKit;

namespace KinahLedger.Ui;

/// <summary>
/// The Neon cabinet in a dungeon run (the "run*" values, MainWindow.HudRun.cs; Steven, 2026-11-18: "Yes, all looks"): an
/// arcade speedrun timer. Over the line, small and slanted, the dungeon and its mode with "Run 3 today · 38 clears"; the run
/// clock lit big where the hour is, in green neon ahead of your fastest run and red behind (white before a split says), the
/// pace in the same tube, then your BEST where the session sits; under it the last bosses' splits, each delta lit in its
/// colour (yellow for a new best split). After the final boss: NEW BEST and the clock in yellow, or "Cleared in 4:17" with how
/// it ranks, then the run's worth and your average run. Between runs: today here lit big, the runs and the average clear
/// under it, your clears and fastest over it. The pickups stay under the line; the timer's tube goes on the cabinet's top
/// edge, its marquee, when the line has no room for it.
/// </summary>
sealed partial class HudNeon
{
    const uint GoN = 0x39FF88, RedN = 0xFF4060;
    readonly StackPanel _runHead = new() { Orientation = Orientation.Horizontal, Visibility = Visibility.Collapsed, Margin = new Thickness(2, 0, 0, 3) };
    readonly StackPanel _runSmall = new() { Orientation = Orientation.Horizontal, Visibility = Visibility.Collapsed, Margin = new Thickness(2, 3, 0, 0) };
    bool _runOn, _tubeUp;

    void BuildRun()
    {
        _lineStack.Children.Insert(0, _runHead);
        _col.Children.Insert(1, _runSmall);
    }

    void EndRun()
    {
        if (!_runOn) return;
        _runOn = _tubeUp = false;
        _runHead.Children.Clear(); _runSmall.Children.Clear();
        _runHead.Visibility = _runSmall.Visibility = Visibility.Collapsed;
        _cab.Content.Padding = new Thickness(10, 7, 12, 7);
    }

    /// <summary>The run drawn in the cabinet: the head, the lit clock and its best, the splits, the pickups.</summary>
    void DrawRun(RunBuddy.State s, Figures f)
    {
        _runOn = true;
        _stats.Visibility = Visibility.Collapsed;   // the Abyss's stats: the splits have their own row
        double cap = FightWidth - 22;   // the cabinet's padding
        double tubeW = _tube.Visibility == Visibility.Visible ? KeptW(_tube, _line) + 14 : 0;
        var forms = Forms(s);
        StackPanel? line = null;
        _tubeUp = false;
        foreach (var make in forms)
        {
            var l = make();
            double w = W(l);
            if (w + tubeW <= cap + .5) { line = l; break; }
            if (tubeW > 0 && w <= cap + .5) { line = l; _tubeUp = true; break; }
        }
        if (line == null) { line = forms[^1](); _tubeUp = tubeW > 0; }
        _line.Children.Insert(0, line);   // before the tube when it stays on the line
        _hour = line.Children[0] as FrameworkElement;

        // the splits, or the run's worth, under the clock
        _runSmall.Children.Clear();
        var small = Fit(cap, Smalls(s));
        foreach (var c in small.Children.Cast<UIElement>().ToList()) { small.Children.Remove(c); _runSmall.Children.Add(c); }
        _runSmall.Visibility = _runSmall.Children.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

        // the head over it all, slanted small; the marquee's room kept over it when the tube is mounted
        _runHead.Children.Clear();
        var count = RunBuddy.Count(s);
        var head = Fit(cap, () => Head(s.Head, count), () => Head(s.Head, ""), () => Head(s.Place, ""));
        _runHead.Children.Add(head);
        _runHead.Visibility = Visibility.Visible;
        _cab.Content.Padding = new Thickness(10, _tubeUp ? 13 : 7, 12, 7);

        _pickHost.Child = RunStrip(cap);
        _pickHost.Margin = new Thickness(2, 4, 0, 2);
        _note.Text = f.Note; _note.Foreground = f.NoteTone;
        _note.Visibility = f.Note.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        PlaceTube();
    }

    /// <summary>A neon tone's glow.</summary>
    static Color GlowOf(uint c) => c == GoN ? Rgb(0x00C060) : c == RedN ? Rgb(0xFF0030) : c == Yel ? Rgb(0xFFB800) : Rgb(Cy);

    /// <summary>The big line's forms, fullest first.</summary>
    List<Func<StackPanel>> Forms(RunBuddy.State s)
    {
        var forms = new List<Func<StackPanel>>();
        uint tone = s.PaceSign < 0 ? GoN : s.PaceSign > 0 ? RedN : 0xFFFFFF;
        UIElement Clock(string c, uint t) => Lit(c, 30, Wk.S(t), GlowOf(t));
        UIElement Unit(string t, Brush fg, double size = 15, bool before = false)
        {
            var u = Slant(Wk.Words(t, size, fg, Wk.Bahn, FontWeights.Bold));
            u.VerticalAlignment = VerticalAlignment.Bottom; u.Margin = before ? new Thickness(0, 0, 5, 7) : new Thickness(5, 0, 0, 7);
            return u;
        }
        // the session's block: a lilac label over a white figure, after the magenta-cyan bar
        UIElement[] Block(string label, string fig, Brush? figTone = null)
        {
            var bar = new Border { Width = 2, Height = 32, Margin = new Thickness(13, 0, 13, 0), Background = Wk.Lin(0, 0, 0, 1, (Mag, 1, 0), (Cy, 1, 1)), RenderTransform = new SkewTransform(Skew, 0), VerticalAlignment = VerticalAlignment.Center };
            var sess = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            if (label.Length > 0) sess.Children.Add(Slant(Wk.Words(label.ToUpperInvariant(), 9.5, LilacB, Wk.Bahn, FontWeights.Bold)));
            var fg = Slant(fig.Length > 0 ? Figure(fig, 18, figTone ?? WhiteB, Wk.Bahn, FontWeights.Bold) : new TextBlock());
            if (label.Length > 0) fg.Margin = new Thickness(0, -3, 0, 0);
            sess.Children.Add(fg);
            return new UIElement[] { bar, sess };
        }
        UIElement[] Verdict(string v, Brush fg)
        {
            var bar = new Border { Width = 2, Height = 28, Margin = new Thickness(12, 0, 11, 0), Background = Wk.Lin(0, 0, 0, 1, (Mag, 1, 0), (Cy, 1, 1)), RenderTransform = new SkewTransform(Skew, 0), VerticalAlignment = VerticalAlignment.Center };
            return new UIElement[] { bar, Slant(Wk.Words(v, 15, fg, Wk.Bahn, FontWeights.Bold)) };
        }
        StackPanel Line(params UIElement[][] parts)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            foreach (var p in parts) foreach (var e in p) row.Children.Add(e);
            return row;
        }
        UIElement[] One(UIElement e) => new[] { e };
        UIElement[] None() => Array.Empty<UIElement>();

        if (s.Run)
        {
            UIElement[] Pace() => s.Pace.Length > 0 ? One(Unit(s.Pace, Wk.S(tone))) : None();
            if (s.Best.Length > 0) forms.Add(() => Line(One(Clock(s.Clock, tone)), Pace(), Block(L.T("best"), s.Best)));
            else if (s.Verdict == "first") forms.Add(() => Line(One(Clock(s.Clock, tone)), Pace(), Verdict(L.T("First clear here"), LilacB)));
            forms.Add(() => Line(One(Clock(s.Clock, tone)), Pace()));
            forms.Add(() => Line(One(Clock(s.Clock, tone))));
        }
        else if (s.Clear && s.Verdict == "best")
        {
            UIElement Nb() { var w = Lit(L.T("NEW BEST"), 19, YelB, Rgb(0xFFB800)); w.VerticalAlignment = VerticalAlignment.Bottom; w.Margin = new Thickness(0, 0, 9, 3); return w; }
            UIElement[] Pace() => s.Pace.Length > 0 ? One(Unit(s.Pace, Wk.S(GoN))) : None();
            forms.Add(() => Line(One(Nb()), One(Clock(s.Clock, Yel)), Pace()));
            forms.Add(() => Line(One(Nb()), One(Clock(s.Clock, Yel))));
            forms.Add(() => Line(One(Clock(s.Clock, Yel)), Pace()));
            forms.Add(() => Line(One(Clock(s.Clock, Yel))));
        }
        else if (s.Clear)
        {
            var (before, after) = ClearedWords();
            var verdict = RunBuddy.Verdict(s);
            var vTone = s.Verdict is "rank" or "first" ? YelB : LilacB;
            UIElement[] Cleared()
            {
                var parts = new List<UIElement>();
                if (before.Length > 0) parts.Add(Unit(before, LilacB, 12.5, true));
                parts.Add(Clock(s.Clock, 0xFFFFFF));
                if (after.Length > 0) parts.Add(Unit(after, LilacB, 12.5));
                return parts.ToArray();
            }
            if (verdict.Length > 0)
            {
                forms.Add(() => Line(Cleared(), Verdict(verdict, vTone)));
                forms.Add(() => Line(One(Clock(s.Clock, 0xFFFFFF)), Verdict(verdict, vTone)));
            }
            forms.Add(() => Line(Cleared()));
            forms.Add(() => Line(One(Clock(s.Clock, 0xFFFFFF))));
        }
        else if (s.TodayRuns.Length > 0)
        {
            UIElement Hero() => s.TodayKinah.Length > 0 ? Clock(s.TodayKinah, Yel) : Lit(RunsWord(s), 22, WhiteB, Rgb(Cy));
            forms.Add(() => Line(One(Hero()), One(Unit(L.T("today here"), MagB, 13.5))));
            forms.Add(() => Line(One(Hero())));
        }
        else if (s.Best.Length > 0)
        {
            forms.Add(() => Line(One(Clock(s.Best, 0xFFFFFF)), One(Unit(L.T("fastest"), MagB, 13.5))));
            forms.Add(() => Line(One(Clock(s.Best, 0xFFFFFF))));
        }
        else
        {
            forms.Add(() => { var w = Slant(Wk.Words(L.T("First clear here"), 15, LilacB, Wk.Bahn, FontWeights.SemiBold)); w.Margin = new Thickness(2, 4, 4, 4); return Line(One(w)); });
        }
        return forms;
    }

    /// <summary>The second line's forms: the last bosses' splits (the newest alone when both don't fit), the run's worth, or today's runs and average.</summary>
    Func<StackPanel>[] Smalls(RunBuddy.State s)
    {
        StackPanel Row(params (string Text, Brush Fg, bool Fig)[][] groups)
        {
            var sp = new StackPanel { Orientation = Orientation.Horizontal };
            foreach (var g in groups)
            {
                if (sp.Children.Count > 0) sp.Children.Add(Slant(Wk.Words("  ·  ", 11, MagB, Wk.Bahn, FontWeights.Bold)));
                bool first = true;
                foreach (var (text, fg, fig) in g)
                {
                    var t = Slant(fig ? Figure(text, 13.5, fg, Wk.Bahn, FontWeights.Bold) : Wk.Words(text, 11.5, fg, Wk.Bahn, FontWeights.SemiBold));
                    t.Margin = new Thickness(first ? 0 : 4, fig ? 0 : 1, 0, 0);
                    sp.Children.Add(t);
                    first = false;
                }
            }
            return sp;
        }
        (string, Brush, bool)[] Split(RunBuddy.Split x)
        {
            var parts = new List<(string, Brush, bool)> { (x.Boss, LilacB, false), (x.Clock, WhiteB, true) };
            if (x.Delta.Length > 0) parts.Add((x.Delta, x.Best ? YelB : x.Sign < 0 ? Wk.S(GoN) : x.Sign > 0 ? Wk.S(RedN) : LilacB, true));
            return parts.ToArray();
        }
        if (s.Clear && s.Kinah.Length > 0)
        {
            var kin = new (string, Brush, bool)[] { (s.Kinah, YelB, true) };
            if (s.Avg.Length > 0) return new Func<StackPanel>[] { () => Row(kin, new (string, Brush, bool)[] { (L.T("avg"), LilacB, false), (s.Avg, WhiteB, true) }), () => Row(kin) };
            return new Func<StackPanel>[] { () => Row(kin) };
        }
        if (s.Between && s.TodayRuns.Length > 0)
        {
            var runs = new (string, Brush, bool)[] { (RunsWord(s), WhiteB, false) };
            if (s.TodayKinah.Length > 0 && s.TodayAvg.Length > 0) return new Func<StackPanel>[] { () => Row(runs, new (string, Brush, bool)[] { (L.T("avg"), LilacB, false), (s.TodayAvg, WhiteB, true) }), () => Row(runs) };
            if (s.TodayKinah.Length > 0) return new Func<StackPanel>[] { () => Row(runs) };
            if (s.TodayAvg.Length > 0) return new Func<StackPanel>[] { () => Row(new (string, Brush, bool)[] { (L.T("avg"), LilacB, false), (s.TodayAvg, WhiteB, true) }) };
            return new Func<StackPanel>[] { () => Row() };
        }
        if (s.Between) return new Func<StackPanel>[] { () => Row() };
        var sp = s.Splits;
        var forms = new List<Func<StackPanel>>();
        if (sp.Count >= 2) forms.Add(() => Row(Split(sp[^2]), Split(sp[^1])));
        if (sp.Count >= 1) forms.Add(() => Row(Split(sp[^1])));
        if (forms.Count == 0) forms.Add(() => Row());
        return forms.ToArray();
    }

    /// <summary>The head: the dungeon and its mode in ice capitals, the count after it in lilac, slanted small.</summary>
    StackPanel Head(string head, string count)
    {
        var sp = new StackPanel { Orientation = Orientation.Horizontal };
        sp.Children.Add(Slant(Wk.Words(head.ToUpperInvariant(), 10.5, IceB, Wk.Bahn, FontWeights.Bold)));
        if (count.Length > 0)
        {
            var c = Slant(Wk.Words(count, 10.5, LilacB, Wk.Bahn, FontWeights.SemiBold));
            c.Margin = new Thickness(10, 0, 0, 0);
            sp.Children.Add(c);
        }
        return sp;
    }
}
