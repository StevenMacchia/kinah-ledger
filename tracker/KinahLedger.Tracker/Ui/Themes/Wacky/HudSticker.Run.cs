using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using static KinahLedger.Ui.HudKit;

namespace KinahLedger.Ui;

/// <summary>
/// The Sticker in a dungeon run (the "run*" values, MainWindow.HudRun.cs; Steven, 2026-11-18: "Yes, all looks"): a speedrun
/// timer slapped on in stickers. An ink tag over it names the dungeon and its mode with "Run 3 today · 38 clears"; the hour's
/// sticker carries the run clock huge in black, its paper green when ahead of your fastest run, red when behind, with the pace
/// beside it and your best on the ink tag lapping it (where the session sits); under it the last bosses' splits, each a small
/// sticker in its delta's colour (gold for a new best split). After the final boss: NEW BEST on a gold sticker, or "Cleared in
/// 4:17" with how it ranks on the ink tag, then the run's worth and your average run. Between runs: today here on the sticker,
/// the runs and the average clear on the tag, your clears and fastest in the head. The pickups' tag stays under it all.
/// </summary>
sealed partial class HudSticker
{
    // the head: the dungeon's ink tag, and the timer's tag beside it when the line has no room for it
    readonly StackPanel _runRow = new() { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Left, Visibility = Visibility.Collapsed, Margin = new Thickness(2, 0, 0, 9) };
    bool _runOn;
    /// <summary>Where the timer's tag sits in a run: at the line's end, by the head, or after the splits (when the line has no room and the head would lose its count).</summary>
    enum TagSpot { Line, Head, Smalls }
    TagSpot _tagSpot;

    void BuildRun() => _body.Children.Insert(0, _runRow);

    /// <summary>The farming line back: the run's head away and the splits' row where the Abyss's stats sit.</summary>
    void EndRun()
    {
        if (!_runOn) return;
        _runOn = false; _tagSpot = TagSpot.Line;
        _runRow.Children.Clear();
        _runRow.Visibility = Visibility.Collapsed;
        _stats.Margin = new Thickness(46, 2, 0, 0);
        _stats.MaxWidth = 380;
    }

    /// <summary>The run drawn in the line's parts: the head, the big sticker with its tag, the splits, the pickups.</summary>
    void DrawRun(RunBuddy.State s, Figures f)
    {
        _runOn = true;
        double cap = FightWidth;
        uint paper = s.Run ? (s.PaceSign < 0 ? GoC : s.PaceSign > 0 ? RedC : Yel) : s.Clear && s.Verdict == "best" ? Gold2 : Yel;
        var fill = Wk.S(paper, PaperA);

        // the big sticker and the ink tag lapping it, the fullest that fits; with the timer's tag after them when it fits too
        var forms = Forms(s, fill);
        double tagW = _tag.Visibility == Visibility.Visible ? KeptW(_tag, _line) + 12 : 0;
        StackPanel? line = null;
        bool up = false;
        foreach (var make in forms)
        {
            var l = make();
            double w = W(l);
            if (w + tagW <= cap + .5) { line = l; break; }
            if (tagW > 0 && w <= cap + .5) { line = l; up = true; break; }
        }
        if (line == null) { line = forms[^1](); up = tagW > 0; }
        _figs.Children.Add(line);
        _pill = line.Children[0] as FrameworkElement;

        // the splits, or the run's worth: small stickers in a row under the hour's
        _stats.Margin = new Thickness(4, 3, 0, 0);
        _stats.MaxWidth = cap;
        foreach (var st in Smalls(s, cap)) _stats.Children.Add(st);
        _stats.Visibility = _stats.Children.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

        // the head: the dungeon and its mode, the count after it. A timer's tag with no room on the line goes by the head when
        // the head keeps its count, else after the splits (a row of its own when they fill theirs)
        _runRow.Children.Clear();
        var count = RunBuddy.Count(s);
        _tagSpot = !up ? TagSpot.Line : W(HeadTag(s.Head, count)) + tagW <= cap - 4 + .5 ? TagSpot.Head : TagSpot.Smalls;
        double headRoom = cap - (_tagSpot == TagSpot.Head ? tagW : 0) - 4;
        if (_tagSpot == TagSpot.Smalls) _stats.Visibility = Visibility.Visible;
        var head = Fit(headRoom,
            () => HeadTag(s.Head, count),
            () => HeadTag(s.Head, ""),
            () => HeadTag(s.Place, ""));
        _runRow.Children.Add(head);
        _runRow.Visibility = Visibility.Visible;

        // the pickups' tag under it all, as while farming
        if (RunStrip(cap - 30) is { } strip)
        {
            var tag = InkTag(strip, 1.5, new Thickness(6, 5, 7, 5));
            tag.Margin = new Thickness(6, _stats.Visibility == Visibility.Visible ? 2 : -3, 0, 0);
            _pickHost.Child = tag;
        }
        else _pickHost.Child = null;
        DrawNote(f);
        _figs.Visibility = Visibility.Visible;
        PlaceTag();
    }

    /// <summary>The big line's forms, fullest first: each the hour's sticker in its paper with the ink tag lapping it.</summary>
    List<Func<StackPanel>> Forms(RunBuddy.State s, Brush fill)
    {
        var forms = new List<Func<StackPanel>>();
        UIElement Clock(string c) => Figure(c, 29, K, Wk.Heavy, Black);
        UIElement Small(string t, double size, double opacity = 1) { var w = Figure(t, size, K, Wk.Heavy, Black); w.VerticalAlignment = VerticalAlignment.Bottom; w.Margin = new Thickness(5, 0, 0, 5); w.Opacity = opacity; return w; }
        UIElement Word(string t, double size, bool before)
        {
            var w = Wk.Words(t, size, K, Wk.Heavy, FontWeights.Bold);
            w.VerticalAlignment = VerticalAlignment.Bottom; w.Margin = before ? new Thickness(0, 0, 5, 7) : new Thickness(4, 0, 0, 7); w.Opacity = .78;
            return w;
        }
        StackPanel Line(UIElement[] big, UIElement? tag)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal };
            foreach (var b in big) row.Children.Add(b);
            var line = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            line.Children.Add(Wk.Tilt(Sticker(row, fill, 10, new Thickness(12, 0, 13, 2)), -1.5));
            if (tag != null)
            {
                var t = InkTag(tag, 2, new Thickness(8, 1, 9, 2));
                t.Margin = new Thickness(-9, 18, 0, 0);
                Panel.SetZIndex(t, 2);
                line.Children.Add(t);
            }
            return line;
        }
        // the ink tag's words: a figure in yellow, its word in paper
        UIElement TagOf(string word, string fig, string word2 = "", string fig2 = "")
        {
            var sp = new StackPanel { Orientation = Orientation.Horizontal };
            void Part(string w, string fg)
            {
                if (w.Length > 0) { var t = Wk.Words(w, 11, PaperB, Wk.Heavy, FontWeights.Bold); t.Opacity = .85; t.Margin = new Thickness(sp.Children.Count > 0 ? 4 : 0, 2, 4, 0); sp.Children.Add(t); }
                if (fg.Length > 0) sp.Children.Add(Figure(fg, 16, YelB, Wk.Heavy, Black));
            }
            Part(word, fig);
            if (word2.Length > 0 || fig2.Length > 0)
            {
                var dot = Wk.Words("·", 11, YelB, Wk.Heavy, Black); dot.Margin = new Thickness(5, 2, 1, 0);
                sp.Children.Add(dot);
                Part(word2, fig2);
            }
            return sp;
        }
        UIElement Verdict(string v, Brush tone) => Wk.Words(v, 12.5, tone, Wk.Heavy, Black);

        if (s.Run)
        {
            if (s.Best.Length > 0) forms.Add(() => Line(Cat(new[] { Clock(s.Clock) }, Pace()), TagOf(L.T("best"), s.Best)));
            else if (s.Verdict == "first") forms.Add(() => Line(Cat(new[] { Clock(s.Clock) }, Pace()), Verdict(L.T("First clear here"), PaperB)));
            forms.Add(() => Line(Cat(new[] { Clock(s.Clock) }, Pace()), null));
            forms.Add(() => Line(new[] { Clock(s.Clock) }, null));
            UIElement[] Pace() => s.Pace.Length > 0 ? new[] { Small(s.Pace, 16, .85) } : Array.Empty<UIElement>();
        }
        else if (s.Clear && s.Verdict == "best")
        {
            UIElement Nb() { var w = Wk.Words("★ " + L.T("NEW BEST"), 15, K, Wk.Heavy, Black); w.VerticalAlignment = VerticalAlignment.Bottom; w.Margin = new Thickness(0, 0, 7, 6); return w; }
            UIElement[] Pace() => s.Pace.Length > 0 ? new[] { Small(s.Pace, 16, .85) } : Array.Empty<UIElement>();
            forms.Add(() => Line(Cat(new[] { Nb(), Clock(s.Clock) }, Pace()), null));
            forms.Add(() => Line(new[] { Nb(), Clock(s.Clock) }, null));
            forms.Add(() => Line(Cat(new[] { Clock(s.Clock) }, Pace()), null));
            forms.Add(() => Line(new[] { Clock(s.Clock) }, null));
        }
        else if (s.Clear)
        {
            var (before, after) = ClearedWords();
            var verdict = RunBuddy.Verdict(s);
            var tone = s.Verdict is "rank" or "first" ? YelB : PaperB;
            UIElement[] Cleared()
            {
                var parts = new List<UIElement>();
                if (before.Length > 0) parts.Add(Word(before, 12.5, true));
                parts.Add(Clock(s.Clock));
                if (after.Length > 0) parts.Add(Word(after, 12.5, false));
                return parts.ToArray();
            }
            if (verdict.Length > 0)
            {
                forms.Add(() => Line(Cleared(), Verdict(verdict, tone)));
                forms.Add(() => Line(new[] { Clock(s.Clock) }, Verdict(verdict, tone)));
            }
            forms.Add(() => Line(Cleared(), null));
            forms.Add(() => Line(new[] { Clock(s.Clock) }, null));
        }
        else if (s.TodayRuns.Length > 0)
        {
            // today here: its Kinah big ("335K today here"), the runs and the average clear on the tag
            var runs = RunsWord(s);
            UIElement Hero() => s.TodayKinah.Length > 0 ? Clock(s.TodayKinah) : Wk.Words(runs, 20, K, Wk.Heavy, Black);
            UIElement Here() => Word(L.T("today here"), 13, false);
            if (s.TodayKinah.Length > 0 && s.TodayAvg.Length > 0) forms.Add(() => Line(new[] { Hero(), Here() }, TagOf(runs, "", L.T("avg"), s.TodayAvg)));
            if (s.TodayKinah.Length > 0) forms.Add(() => Line(new[] { Hero(), Here() }, TagOf(runs, "")));
            else if (s.TodayAvg.Length > 0) forms.Add(() => Line(new[] { Hero(), Here() }, TagOf(L.T("avg"), s.TodayAvg)));
            forms.Add(() => Line(new[] { Hero(), Here() }, null));
            forms.Add(() => Line(new[] { Hero() }, null));
        }
        else if (s.Best.Length > 0)
        {
            forms.Add(() => Line(new[] { Clock(s.Best), Word(L.T("fastest"), 13, false) }, null));
            forms.Add(() => Line(new[] { Clock(s.Best) }, null));
        }
        else
        {
            forms.Add(() => { var l = Line(new UIElement[] { Wk.Words(L.T("First clear here"), 14, K, Wk.Heavy, Black) }, null); return l; });
        }
        return forms;
    }

    static UIElement[] Cat(params UIElement[][] parts) => parts.SelectMany(p => p).ToArray();

    /// <summary>The second row: the last bosses' splits, or after the clear the run's worth and your average run, each a small sticker.</summary>
    List<FrameworkElement> Smalls(RunBuddy.State s, double room)
    {
        FrameworkElement Small(uint paper, int n, params (string Text, bool Figure)[] parts)
        {
            var sp = new StackPanel { Orientation = Orientation.Horizontal };
            foreach (var (text, fig) in parts)
            {
                var t = fig ? Figure(text, 12.5, K, Wk.Heavy, Black) : Wk.Words(text, 11, K, Wk.Segoe, FontWeights.Bold);
                t.Margin = new Thickness(sp.Children.Count > 0 ? 4 : 0, fig ? 0 : 1, 0, 0);
                sp.Children.Add(t);
            }
            var st = Wk.Tilt(Sticker(sp, Wk.S(paper, PaperA), 4, new Thickness(6, 0, 7, 1), 2, 2.5), n % 2 == 0 ? -2 : 1.5);
            st.Margin = new Thickness(0, 6, 6, 0);
            return st;
        }
        FrameworkElement Split(RunBuddy.Split x, int n)
        {
            uint paper = x.Best ? Gold2 : x.Sign < 0 ? GoC : x.Sign > 0 ? RedC : Paper;
            return x.Delta.Length > 0 ? Small(paper, n, (x.Boss, false), (x.Clock, true), (x.Delta, true)) : Small(paper, n, (x.Boss, false), (x.Clock, true));
        }
        var list = new List<FrameworkElement>();
        if (s.Clear && s.Kinah.Length > 0)
        {
            list.Add(Small(Yel, 0, (s.Kinah, true)));
            if (s.Avg.Length > 0) list.Add(Small(Paper, 1, (L.T("avg"), false), (s.Avg, true)));
        }
        else if (s.Run || s.Clear)
        {
            // the newest kept when both don't fit
            var sp = s.Splits;
            if (sp.Count >= 2)
            {
                var two = new List<FrameworkElement> { Split(sp[^2], 0), Split(sp[^1], 1) };
                if (two.Sum(e => W(e) + 6) <= room) return two;
            }
            if (sp.Count >= 1) list.Add(Split(sp[^1], 0));
        }
        return list;
    }

    /// <summary>The head's ink tag: the dungeon and its mode in paper, the count after it in yellow.</summary>
    Grid HeadTag(string head, string count)
    {
        var sp = new StackPanel { Orientation = Orientation.Horizontal };
        sp.Children.Add(Wk.Words(head, 12, PaperB, Wk.Heavy, Black));
        if (count.Length > 0)
        {
            var c = Wk.Words(count, 11, YelB, Wk.Segoe, FontWeights.Bold);
            c.Margin = new Thickness(10, 1, 0, 0);
            sp.Children.Add(c);
        }
        return InkTag(sp, -1, new Thickness(8, 2, 9, 3));
    }
}
