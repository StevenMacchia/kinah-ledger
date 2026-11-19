using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using static KinahLedger.Ui.GlitchKit;
using static KinahLedger.Ui.HudKit;

namespace KinahLedger.Ui;

/// <summary>
/// Terminal in a dungeon run (the "run*" values in RunBuddy's words, Ui/HudRunBuddy.cs; Steven, 2026-11-18: "Yes, all
/// looks"): the screen runs the run. The prompt names the dungeon and its mode, the block cursor after it blinking with the
/// run clock, "[Run 3 today · 38 clears]" at its right; the run clock big in phosphor where the hour is (green ahead of your
/// fastest run's pace, red behind, pale even), the pace and "[best 4:12]" beside it, in the console's brackets; under it the
/// last bosses' splits where the session is, the pickup strip in its brackets after them. The clear: "[★ NEW BEST]" in amber
/// with the clock lit amber, or "Cleared in 4:17  [2nd fastest]"; then the run's worth against your average run here. Between
/// runs: today here, its runs and average clear. The timer keeps its line; the last pickup's log line rests for the run.
/// </summary>
sealed partial class HudTerminal
{
    readonly Border _runCursor = new() { Width = 7.5, Height = 14, Background = Phos, Margin = new Thickness(3, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, Visibility = Visibility.Collapsed };
    bool _runOn;
    const uint RedRunC = 0xFF6B5E;
    // (from the constants: the other file's brushes may not be set yet when these are)
    static readonly Brush Green = Br(PhosC), Red = Br(RedRunC);

    /// <summary>The block cursor after the prompt, where the record's "OK" stands; hidden until a run.</summary>
    void BuildRun(Grid prompt)
    {
        Grid.SetColumn(_runCursor, 1); prompt.Children.Add(_runCursor);
    }

    /// <summary>The run's lines in place of the hour's, or back: the pickup's log line rests for the run.</summary>
    void ShowRun(bool on)
    {
        _runOn = on;
        _runCursor.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
        _pickLine.Visibility = on ? Visibility.Collapsed : Visibility.Visible;
        if (on) _ok.Visibility = Visibility.Collapsed;
    }

    /// <summary>The prompt in a run: "> Ferocious Horn Den · Easy" and the cursor, "[Run 3 today · 38 clears]" at its right; each the fullest form that fits.</summary>
    void RunPrompt(RunBuddy.State s)
    {
        _runCursor.Opacity = s.Clock.Length > 0 && char.IsDigit(s.Clock[^1]) && (s.Clock[^1] - '0') % 2 == 1 ? 1 : .28;
        double room = FightWidth - _crt.Body.Margin.Left - _crt.Body.Margin.Right - Wide(_runCursor) - 1;
        // between runs the figure line shows your fastest in its brackets (the prompt has no room for both): the clears alone here
        var counts = FastestInFigures(s) ? new[] { s.Clears.Length == 0 ? "" : s.Clears == "1" ? L.T("1 clear") : L.F("{0} clears", s.Clears), "" } : CountForms(s);
        foreach (var head in new[] { s.Head, s.Place })
            foreach (var count in counts)
            {
                _prompt.Inlines.Clear();
                _prompt.Inlines.Add(Rn("> ", PhosDim));
                _prompt.Inlines.Add(Rn(head, Pale, null, FontWeights.Bold));
                _runClock.Text = count.Length > 0 ? $"[{count}]" : "";
                if (Wide(_prompt) + (count.Length > 0 ? Wide(_runClock) : 0) <= room + .5) return;
            }
    }

    /// <summary>The run's figure line and the line under it.</summary>
    void DrawRun(RunBuddy.State s)
    {
        double cap = FightWidth - _crt.Body.Margin.Left - _crt.Body.Margin.Right - 1;   // a pixel kept for the rim and rounding
        _figLine.Children.Clear();
        var forms = FigForms(s);
        var line = forms.FirstOrDefault(f => f.Sum(Wide) <= cap + .5, forms[^1]);
        foreach (var e in line) _figLine.Children.Add(e);
        VerdictDrawn(line.OfType<TextBlock>().FirstOrDefault(t => Equals(t.Tag, TypedTag)));   // a clear's verdict types on (HudTerminal.Type.cs)
        _stats.Inlines.Clear(); _stats.Visibility = Visibility.Collapsed;
        _rareLine.Child = null; _rareTile = null; _rareLine.Visibility = Visibility.Collapsed;
        RunSub(s, cap);
    }

    /// <summary>The figure line's forms, fullest first: the big figure, then its words in the console's brackets.</summary>
    List<FrameworkElement[]> FigForms(RunBuddy.State s)
    {
        var forms = new List<FrameworkElement[]>();
        if (s.Run)
        {
            var tone = PaceTone(s.PaceSign, Green, Red, Pale);
            var glow = s.PaceSign > 0 ? Rgb(RedRunC) : Glow;
            FrameworkElement Clock() => Phosphor(s.Clock, 25, Face, FontWeights.Bold, tone, glow, .9, .32);
            FrameworkElement? Pace() => s.Pace.Length > 0 ? Beside(s.Pace, 14, tone, FontWeights.Bold, new Thickness(7, 0, 0, 4)) : null;
            FrameworkElement? Best() => s.Best.Length > 0 ? Bracket(L.T("best") + " " + s.Best, Phos)
                : s.Verdict == "first" ? Bracket(L.T("First clear here"), PhosDim) : null;
            forms.Add(Parts(Clock(), Pace(), Best())); forms.Add(Parts(Clock(), Pace())); forms.Add(Parts(Clock()));
        }
        else if (s.Clear && s.Verdict == "best")
        {
            // a new best: the clock lit amber, "[★ NEW BEST]"
            FrameworkElement Clock() => Phosphor(s.Clock, 25, Face, FontWeights.Bold, Amber, Rgb(AmberC), .9, .32);
            FrameworkElement? Pace() => s.Pace.Length > 0 ? Beside(s.Pace, 14, Green, FontWeights.Bold, new Thickness(7, 0, 0, 4)) : null;
            FrameworkElement Nb() => Typed(Bracket("★ " + L.T("NEW BEST"), Amber));
            forms.Add(Parts(Clock(), Pace(), Nb())); forms.Add(Parts(Clock(), Nb())); forms.Add(Parts(Clock()));
        }
        else if (s.Clear)
        {
            // "Cleared in 4:17", the clock big wherever the language puts it, then "[2nd fastest]"
            var (before, after) = ClearedIn();
            var verdict = RunBuddy.Verdict(s);
            FrameworkElement Clock() => Phosphor(s.Clock, 25, Face, FontWeights.Bold, Pale, Glow, .9, .32);
            FrameworkElement? Verdict() => verdict.Length > 0 ? Typed(Bracket(verdict, s.Verdict is "rank" or "first" ? Amber : Phos)) : null;
            FrameworkElement? Before() => before.Trim().Length > 0 ? Beside(before.TrimEnd(), 12.5, PhosDim, FontWeights.SemiBold, new Thickness(0, 0, 7, 5)) : null;
            FrameworkElement? After() => after.Trim().Length > 0 ? Beside(after.TrimStart(), 12.5, PhosDim, FontWeights.SemiBold, new Thickness(7, 0, 0, 5)) : null;
            forms.Add(Parts(Before(), Clock(), After(), Verdict())); forms.Add(Parts(Clock(), Verdict())); forms.Add(Parts(Clock()));
        }
        else if (s.TodayRuns.Length > 0)
        {
            // between runs: today here big ("335K today here"); its runs and average clear under it
            string runs = s.TodayRuns == "1" ? L.T("1 run") : L.F("{0} runs", s.TodayRuns);
            FrameworkElement Hero() => s.TodayKinah.Length > 0 ? Phosphor(s.TodayKinah, 25, Face, FontWeights.Bold, Pale, Glow, .9, .32) : Phosphor(runs, 18, Face, FontWeights.Bold, Pale, Glow, .8, .2);
            FrameworkElement Word() => Beside(L.T("today here"), 14, Phos, FontWeights.Bold, new Thickness(8, 0, 0, 4));
            FrameworkElement? Fastest() => FastestInFigures(s) ? Bracket(L.F("fastest {0}", s.Best), Phos) : null;
            forms.Add(Parts(Hero(), Word(), Fastest())); forms.Add(Parts(Hero(), Fastest())); forms.Add(Parts(Hero()));
        }
        else if (s.Best.Length > 0)
        {
            FrameworkElement Hero() => Phosphor(s.Best, 25, Face, FontWeights.Bold, Pale, Glow, .9, .32);
            forms.Add(Parts(Hero(), Beside(L.T("fastest"), 14, Phos, FontWeights.Bold, new Thickness(8, 0, 0, 4)))); forms.Add(Parts(Hero()));
        }
        else forms.Add(Parts(Txt("# " + L.T("First clear here"), 12.5, PhosDim, Face, FontWeights.SemiBold)));
        return forms;
    }

    /// <summary>Between runs with a run here today: your fastest goes in the figure line's brackets, the clears stay at the prompt.</summary>
    static bool FastestInFigures(RunBuddy.State s) => s.Between && s.TodayRuns.Length > 0 && s.Best.Length > 0;

    static FrameworkElement[] Parts(params FrameworkElement?[] parts) => parts.Where(p => p != null).Select(p => p!).ToArray();

    /// <summary>Words beside a big figure, on its baseline.</summary>
    static TextBlock Beside(string s, double size, Brush fg, FontWeight w, Thickness margin)
    {
        var t = Txt(s, size, fg, Face, w); t.VerticalAlignment = VerticalAlignment.Bottom; t.Margin = margin;
        return t;
    }

    const string TypedTag = "type";
    static TextBlock Typed(TextBlock t) { t.Tag = TypedTag; return t; }

    /// <summary>"[best 4:12]": words in the console's brackets, as the hour's "[91% of best]".</summary>
    static TextBlock Bracket(string s, Brush fg) => Beside("[" + s + "]", 12, fg, FontWeights.SemiBold, new Thickness(10, 0, 0, 5));

    /// <summary>
    /// The line under the figures, where the session is: the last bosses' splits (the newest kept when both don't fit); after
    /// a clear the run's worth against your average run here; between runs today's runs and average clear. The pickup strip in
    /// its brackets after it: the best drop and the last three, else the last three, a split given up for them before the
    /// strip goes, as Glass keeps its strip.
    /// </summary>
    void RunSub(RunBuddy.State s, double cap)
    {
        var forms = new List<Run[]>();
        var indent = Rn("  ", PhosDim);
        var sep = Rn("  · ", PhosFaint);
        Run[] Avg(string fig) => new[] { sep, Rn(L.T("avg") + " ", PhosDim), Rn(fig, Pale, 13, FontWeights.Bold) };
        if (s.Clear && s.Kinah.Length > 0)
        {
            Run[] kin = { indent, Rn(s.Kinah, Amber, 14, FontWeights.Bold) };
            if (s.Avg.Length > 0) forms.Add(Join(kin, Avg(s.Avg)));
            forms.Add(kin);
        }
        else if (s.Run || s.Clear)
        {
            Run[] One(RunBuddy.Split x)
            {
                var parts = new List<Run> { Rn(x.Boss + " ", PhosDim), Rn(x.Clock, Pale, 13, FontWeights.Bold) };
                if (x.Delta.Length > 0) parts.Add(Rn(" " + x.Delta, x.Best ? Amber : PaceTone(x.Sign, Green, Red, PhosDim), 13, FontWeights.Bold));
                return parts.ToArray();
            }
            if (s.Splits.Count >= 2) forms.Add(Join(new[] { indent }, One(s.Splits[^2]), new[] { sep }, One(s.Splits[^1])));
            if (s.Splits.Count >= 1) forms.Add(Join(new[] { indent }, One(s.Splits[^1])));
        }
        else if (s.TodayRuns.Length > 0)
        {
            string runs = s.TodayRuns == "1" ? L.T("1 run") : L.F("{0} runs", s.TodayRuns);
            var more = new List<Run> { indent };
            if (s.TodayKinah.Length > 0) more.Add(Rn(runs, Pale, 13, FontWeights.Bold));
            if (s.TodayAvg.Length > 0) { var a = Avg(s.TodayAvg); more.AddRange(more.Count > 1 ? a : a.Skip(1)); }
            if (more.Count > 1) forms.Add(more.ToArray());
            if (s.TodayKinah.Length > 0) forms.Add(more.Take(2).ToArray());
        }
        while (_sessLine.Children.Count > 1) _sessLine.Children.RemoveAt(1);
        _stripLine.Children.Clear(); _stripLine.Visibility = Visibility.Collapsed;
        _sess.Inlines.Clear();
        bool picks = PickStrip.Shown && Picks(Values).Count > 0;
        if (forms.Count == 0 && picks) forms.Add(new[] { indent });   // nothing under the clock yet: the strip alone in its brackets
        if (forms.Count > 0)
        {
            bool placed = false;
            if (picks)
                foreach (var form in forms)
                {
                    FitRuns(_sess, double.PositiveInfinity, form);
                    var (open, close) = Brackets();
                    if (FitStrip(Values, Strip, cap - Wide(_sess) - Wide(open) - Wide(close)) is { } strip)
                    {
                        _sessLine.Children.Add(open); _sessLine.Children.Add(strip); _sessLine.Children.Add(close);
                        placed = true; break;
                    }
                }
            if (!placed) FitRuns(_sess, cap, forms.ToArray());
            if (!placed && forms[0].Length == 1) _sess.Inlines.Clear();   // the indent alone: no line
            if (!placed && picks) StripLine();   // the session line full: the best drop and the three on their own line, never dropped
        }
        _sess.Visibility = _sess.Inlines.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        _sessLine.Visibility = _sess.Visibility;
    }
}
