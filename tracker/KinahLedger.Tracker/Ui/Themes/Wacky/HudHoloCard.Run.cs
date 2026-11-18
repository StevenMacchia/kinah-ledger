using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using static KinahLedger.Ui.HudKit;

namespace KinahLedger.Ui;

/// <summary>
/// The Holo card in a dungeon run (the "run*" values, MainWindow.HudRun.cs; Steven, 2026-11-18: "Yes, all looks"): a speedrun
/// timer on the trading card. Its head names the dungeon and its mode with "Run 3 today · 38 clears" over a foil rule; the run
/// clock big where the hour is, mint ahead of your fastest run and cherry behind (white before a split says), the pace beside
/// it and your best on the foil chip where the session sits; under it the last bosses' splits, each delta in its colour (gold
/// for a new best split). After the final boss: NEW BEST on a foil chip and the clock in gold, or "Cleared in 4:17" with how it
/// ranks on the chip, then the run's worth and your average run. Between runs: today here in gold, the runs and the average
/// clear under it, your clears and fastest in the head. The pickups stay on the card's face; the timer's tag goes on the
/// card's top edge when the line has no room for it.
/// </summary>
sealed partial class HudHoloCard
{
    readonly StackPanel _runHead = new() { Visibility = Visibility.Collapsed };
    readonly StackPanel _runSmall = new() { Orientation = Orientation.Horizontal, Visibility = Visibility.Collapsed, Margin = new Thickness(2, 2, 0, 0) };
    bool _runOn, _beanAbove;
    static readonly Thickness BlobPad = new(13, 10, 17, 11);

    void BuildRun()
    {
        _lineStack.Children.Insert(0, _runHead);
        _col.Children.Insert(1, _runSmall);
    }

    void EndRun()
    {
        if (!_runOn) return;
        _runOn = _beanAbove = false;
        _runHead.Children.Clear(); _runSmall.Children.Clear();
        _runHead.Visibility = _runSmall.Visibility = Visibility.Collapsed;
        _blob.Content.Padding = BlobPad;
    }

    /// <summary>The run drawn on the card's face: the head over its foil rule, the clock and its chip, the splits, the pickups.</summary>
    void DrawRun(RunBuddy.State s, Figures f)
    {
        _runOn = true;
        _stats.Visibility = Visibility.Collapsed;   // the Abyss's stats: the splits have their own row
        double cap = FightWidth - BlobPad.Left - BlobPad.Right;
        double beanW = _bean.Visibility == Visibility.Visible ? KeptW(_bean, _line) + 12 : 0;
        var forms = Forms(s);
        StackPanel? line = null;
        _beanAbove = false;
        foreach (var make in forms)
        {
            var l = make();
            double w = W(l);
            if (w + beanW <= cap + .5) { line = l; break; }
            if (beanW > 0 && w <= cap + .5) { line = l; _beanAbove = true; break; }
        }
        if (line == null) { line = forms[^1](); _beanAbove = beanW > 0; }
        _line.Children.Insert(0, line);   // before the tag when it stays on the line
        _hour = line.Children[0] as FrameworkElement;

        _runSmall.Children.Clear();
        var small = Fit(cap, Smalls(s));
        foreach (var c in small.Children.Cast<UIElement>().ToList()) { small.Children.Remove(c); _runSmall.Children.Add(c); }
        _runSmall.Visibility = _runSmall.Children.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

        _runHead.Children.Clear();
        var count = RunBuddy.Count(s);
        _runHead.Children.Add(Fit(cap, () => Head(s.Head, count), () => Head(s.Head, ""), () => Head(s.Place, "")));
        _runHead.Children.Add(new Border { Height = 1.5, Margin = new Thickness(0, 4, 0, 3), CornerRadius = new CornerRadius(.75), Background = Foil(.8, 46) });
        _runHead.Visibility = Visibility.Visible;
        _blob.Content.Padding = new Thickness(BlobPad.Left, BlobPad.Top + (_beanAbove ? 7 : 0), BlobPad.Right, BlobPad.Bottom);

        _pickHost.Child = RunStrip(cap);
        _pickHost.Margin = new Thickness(3, 4, 0, 2);
        DrawNote(f);
        PlaceBean();
    }

    /// <summary>The big line's forms, fullest first.</summary>
    List<Func<StackPanel>> Forms(RunBuddy.State s)
    {
        var forms = new List<Func<StackPanel>>();
        var tone = s.PaceSign < 0 ? Wk.S(Mint) : s.PaceSign > 0 ? Wk.S(Cherry) : WhiteB;
        UIElement Clock(string c, Brush fg) => Dropped(c, 29, fg, Deep, FontWeights.Bold, true, 1.8);
        UIElement Word(string t, Brush fg, double size, bool before = false, bool figure = false)
        {
            var u = Dropped(t, size, fg, Deep, figure ? FontWeights.Bold : FontWeights.SemiBold, figure);
            u.VerticalAlignment = VerticalAlignment.Bottom; u.Margin = before ? new Thickness(0, 0, 5, 6) : new Thickness(5, 0, 0, 6);
            return u;
        }
        // the session's foil chip: a word in slate and a figure in ink
        UIElement Chip(string word, string fig, bool wordBold = false)
        {
            var sp = new StackPanel { Orientation = Orientation.Horizontal };
            if (word.Length > 0) { var w = Wk.Words(word, wordBold ? 12.5 : 11, wordBold ? InkB : Wk.S(0x3A4670), Face, wordBold ? FontWeights.Bold : FontWeights.SemiBold); w.Margin = new Thickness(0, wordBold ? 0 : 1, fig.Length > 0 ? 4 : 0, 0); sp.Children.Add(w); }
            if (fig.Length > 0) sp.Children.Add(Figure(fig, 14.5, InkB, Face, FontWeights.Bold));
            var chip = FoilChip(sp, 9, new Thickness(10, 2, 11, 3), .42);
            chip.Margin = new Thickness(11, 2, 0, 0);
            return chip;
        }
        StackPanel Line(params UIElement[] parts)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            foreach (var e in parts) row.Children.Add(e);
            return row;
        }
        UIElement[] Cat(params UIElement[][] p) => p.SelectMany(x => x).ToArray();

        if (s.Run)
        {
            UIElement[] Pace() => s.Pace.Length > 0 ? new[] { Word(s.Pace, tone, 15, figure: true) } : Array.Empty<UIElement>();
            if (s.Best.Length > 0) forms.Add(() => Line(Cat(new[] { Clock(s.Clock, tone) }, Pace(), new[] { Chip(L.T("best"), s.Best) })));
            else if (s.Verdict == "first") forms.Add(() => Line(Cat(new[] { Clock(s.Clock, tone) }, Pace(), new[] { Word(L.T("First clear here"), SteelB, 12.5) })));
            forms.Add(() => Line(Cat(new[] { Clock(s.Clock, tone) }, Pace())));
            forms.Add(() => Line(Clock(s.Clock, tone)));
        }
        else if (s.Clear && s.Verdict == "best")
        {
            UIElement Nb() { var c = Chip("★ " + L.T("NEW BEST"), "", true); ((FrameworkElement)c).Margin = new Thickness(0, 2, 10, 0); return c; }
            UIElement[] Pace() => s.Pace.Length > 0 ? new[] { Word(s.Pace, Wk.S(Mint), 15, figure: true) } : Array.Empty<UIElement>();
            forms.Add(() => Line(Cat(new[] { Nb(), Clock(s.Clock, GoldB) }, Pace())));
            forms.Add(() => Line(Nb(), Clock(s.Clock, GoldB)));
            forms.Add(() => Line(Cat(new[] { Clock(s.Clock, GoldB) }, Pace())));
            forms.Add(() => Line(Clock(s.Clock, GoldB)));
        }
        else if (s.Clear)
        {
            var (before, after) = ClearedWords();
            var verdict = RunBuddy.Verdict(s);
            bool ranked = s.Verdict is "rank" or "first";
            UIElement Verdict() => ranked ? Chip(verdict, "", true) : Word(verdict, SteelB, 12.5);
            UIElement[] Cleared()
            {
                var parts = new List<UIElement>();
                if (before.Length > 0) parts.Add(Word(before, SteelB, 12.5, true));
                parts.Add(Clock(s.Clock, WhiteB));
                if (after.Length > 0) parts.Add(Word(after, SteelB, 12.5));
                return parts.ToArray();
            }
            if (verdict.Length > 0)
            {
                forms.Add(() => Line(Cat(Cleared(), new[] { Verdict() })));
                forms.Add(() => Line(Clock(s.Clock, WhiteB), Verdict()));
            }
            forms.Add(() => Line(Cleared()));
            forms.Add(() => Line(Clock(s.Clock, WhiteB)));
        }
        else if (s.TodayRuns.Length > 0)
        {
            UIElement Hero() => s.TodayKinah.Length > 0 ? Clock(s.TodayKinah, GoldB) : Dropped(RunsWord(s), 21, WhiteB, Deep, FontWeights.Bold, false);
            forms.Add(() => Line(Hero(), Word(L.T("today here"), SteelB, 13)));
            forms.Add(() => Line(Hero()));
        }
        else if (s.Best.Length > 0)
        {
            forms.Add(() => Line(Clock(s.Best, WhiteB), Word(L.T("fastest"), SteelB, 13)));
            forms.Add(() => Line(Clock(s.Best, WhiteB)));
        }
        else
        {
            forms.Add(() => { var w = Dropped(L.T("First clear here"), 14.5, WhiteB, Deep, FontWeights.Bold, false); w.Margin = new Thickness(4, 4, 4, 4); return Line(w); });
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
                if (sp.Children.Count > 0) { var dot = Wk.Words("·", 12, SteelB, Face, FontWeights.Bold); dot.Margin = new Thickness(7, 0, 7, 0); sp.Children.Add(dot); }
                bool first = true;
                foreach (var (text, fg, fig) in g)
                {
                    var t = fig ? Figure(text, 13, fg, Face, FontWeights.Bold) : Wk.Words(text, 11.5, fg, Face, FontWeights.SemiBold);
                    t.Margin = new Thickness(first ? 0 : 4, fig ? 0 : 1, 0, 0);
                    sp.Children.Add(t);
                    first = false;
                }
            }
            return sp;
        }
        (string, Brush, bool)[] Split(RunBuddy.Split x)
        {
            var parts = new List<(string, Brush, bool)> { (x.Boss, IceB, false), (x.Clock, WhiteB, true) };
            if (x.Delta.Length > 0) parts.Add((x.Delta, x.Best ? GoldB : x.Sign < 0 ? Wk.S(Mint) : x.Sign > 0 ? Wk.S(Cherry) : SteelB, true));
            return parts.ToArray();
        }
        (string, Brush, bool)[] Avg(string a) => new (string, Brush, bool)[] { (L.T("avg"), SteelB, false), (a, WhiteB, true) };
        if (s.Clear && s.Kinah.Length > 0)
        {
            var kin = new (string, Brush, bool)[] { (s.Kinah, GoldB, true) };
            if (s.Avg.Length > 0) return new Func<StackPanel>[] { () => Row(kin, Avg(s.Avg)), () => Row(kin) };
            return new Func<StackPanel>[] { () => Row(kin) };
        }
        if (s.Between && s.TodayRuns.Length > 0)
        {
            var runs = new (string, Brush, bool)[] { (RunsWord(s), IceB, false) };
            if (s.TodayKinah.Length > 0 && s.TodayAvg.Length > 0) return new Func<StackPanel>[] { () => Row(runs, Avg(s.TodayAvg)), () => Row(runs) };
            if (s.TodayKinah.Length > 0) return new Func<StackPanel>[] { () => Row(runs) };
            if (s.TodayAvg.Length > 0) return new Func<StackPanel>[] { () => Row(Avg(s.TodayAvg)) };
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

    /// <summary>The head: the dungeon and its mode in ice, the count after it in steel.</summary>
    static StackPanel Head(string head, string count)
    {
        var sp = new StackPanel { Orientation = Orientation.Horizontal };
        sp.Children.Add(Wk.Words(head, 11.5, IceB, Face, FontWeights.SemiBold));
        if (count.Length > 0)
        {
            var c = Wk.Words(count, 11, SteelB, Face, FontWeights.Medium);
            c.Margin = new Thickness(10, 0, 0, 0);
            sp.Children.Add(c);
        }
        return sp;
    }
}
