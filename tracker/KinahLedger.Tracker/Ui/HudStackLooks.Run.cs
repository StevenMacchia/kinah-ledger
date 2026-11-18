using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using static KinahLedger.Ui.HudKit;

namespace KinahLedger.Ui;

/// <summary>
/// The classic looks in a dungeon run (the "run*" values, MainWindow.HudRun.cs; Steven, 2026-11-18: "Yes, all looks"): the
/// Buddy's speedrun timer (Ui/HudCard.Run.cs) in each look's own parts, its words from RunBuddy so every look says the same.
/// Three tiers: the head (the dungeon and its mode, "Run 3 today · 38 clears"), the big line (the run clock, green ahead of
/// your fastest run's pace and red behind, the pace and your best; after the final boss the clear and how it ranks, NEW BEST
/// in gold; between runs today here) and the small line (the last bosses' splits, gold for a new best split; the run's worth
/// against your average run; today's runs and average clear). Each line takes its fullest form that fits, a part left out
/// before a figure is cut, never wider than the meter's width. The pickup strip stays where the look shows it: the best drop
/// and the last three while the words fit whole beside them, else the last three alone.
/// </summary>
abstract partial class HudStackLook
{
    /// <summary>A piece of a run line: its text, size, colour and weight.</summary>
    protected readonly record struct Bit(string Text, double Size, Brush Fg, FontWeight Weight);

    /// <summary>A look's run sizes: the clock, the pace beside it, the words on the big line, the small line, the head.</summary>
    protected sealed record RunStyle(double Clock, double Pace, double Word, double Small, double Head);

    /// <summary>The run values as last read (null out of a run).</summary>
    protected RunBuddy.State? _runRaw;

    /// <summary>The farming strip placed again on the way out of a run (a run's strip is its own).</summary>
    protected void ForgetStrip() => _stripSig = "";

    static Bit Sep(double size) => new("  ·  ", size, Faint, FontWeights.SemiBold);

    protected static Brush PaceTone(RunBuddy.State s) => s.PaceSign < 0 ? Mint : s.PaceSign > 0 ? Rec : Ink;

    /// <summary>A run line: tabular digits (the clock never shifts as it runs), never trimmed (its forms do the fitting).</summary>
    protected static TextBlock RunText()
    {
        var t = new TextBlock { VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.None };
        Typography.SetNumeralAlignment(t, FontNumeralAlignment.Tabular);
        return t;
    }

    /// <summary><paramref name="t"/> as <paramref name="form"/>; its width.</summary>
    protected static double SetBits(TextBlock t, Bit[] form)
    {
        t.Inlines.Clear();
        foreach (var b in form) t.Inlines.Add(new Run(b.Text) { FontSize = b.Size, Foreground = b.Fg, FontWeight = b.Weight });
        t.Visibility = form.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        return form.Length > 0 ? Wants(t) : 0;
    }

    /// <summary><paramref name="t"/> as the first of <paramref name="forms"/> that fits <paramref name="room"/> (the last when none does).</summary>
    /// <returns>The form's index.</returns>
    protected static int FitBits(TextBlock t, double room, IReadOnlyList<Bit[]> forms)
    {
        for (int i = 0; i < forms.Count; i++)
            if (SetBits(t, forms[i]) <= room + .5 || i == forms.Count - 1) return i;
        return -1;
    }

    /// <summary>The form at <paramref name="i"/>, or the shortest when there are fewer.</summary>
    protected static Bit[] At(IReadOnlyList<Bit[]> forms, int i) => forms.Count == 0 ? Array.Empty<Bit>() : forms[Math.Min(i, forms.Count - 1)];

    /// <summary>The pickup strips a run may show, fullest first (the best drop and the last three, the three alone); none while off.</summary>
    protected List<FrameworkElement> RunStrips(PickStrip.Look look)
    {
        var list = new List<FrameworkElement>();
        if (!PickStrip.Shown) return list;
        foreach (var l in look.Best ? new[] { look, look with { Best = false } } : new[] { look })
            if (PickStrip.Of(_values, l) is { } s) list.Add(s);
        return list;
    }

    /// <summary>The head: "Ferocious Horn Den · Easy · Run 3 today · 38 clears", then without the count, then the dungeon alone.</summary>
    protected static List<Bit[]> RunHeadForms(RunBuddy.State s, double size, Brush? place = null)
    {
        var p = new Bit(s.Place, size, place ?? Ink, FontWeights.SemiBold);
        var mode = s.Mode.Length > 0 ? new[] { new Bit(" · " + s.Mode, size, Muted, FontWeights.SemiBold) } : Array.Empty<Bit>();
        var count = RunBuddy.Count(s);
        var forms = new List<Bit[]>();
        if (count.Length > 0) forms.Add(Join(new[] { p }, mode, new[] { Sep(size), new Bit(count, size, Muted, FontWeights.SemiBold) }));
        forms.Add(Join(new[] { p }, mode));
        if (mode.Length > 0) forms.Add(new[] { p });
        return forms;
    }

    /// <summary>
    /// The big line: the run clock with its pace and your best ("3:20 −0:11 · best 4:12"); the clear ("Cleared in 4:17 · 2nd
    /// fastest", "★ NEW BEST 4:01 −0:11"); between runs today here ("335K today here"). Fullest first, the clock last.
    /// <paramref name="withBest"/> false: the look shows your best elsewhere.
    /// </summary>
    protected static List<Bit[]> RunBigForms(RunBuddy.State s, RunStyle st, bool withBest = true)
    {
        var forms = new List<Bit[]>();
        var bold = FontWeights.Bold; var semi = FontWeights.SemiBold;
        if (s.Run)
        {
            var tone = PaceTone(s);
            Bit[] clock = { new(s.Clock, st.Clock, tone, bold) };
            var pace = s.Pace.Length > 0 ? new[] { new Bit("  " + s.Pace, st.Pace, tone, bold) } : Array.Empty<Bit>();
            var best = !withBest ? Array.Empty<Bit>()
                : s.Best.Length > 0 ? new[] { Sep(st.Word), new Bit(L.T("best") + " ", st.Word, Muted, semi), new Bit(s.Best, st.Word + .5, Ink, bold) }
                : s.Verdict == "first" ? new[] { Sep(st.Word), new Bit(L.T("First clear here"), st.Word, Muted, semi) } : Array.Empty<Bit>();
            if (best.Length > 0) forms.Add(Join(clock, pace, best));
            if (pace.Length > 0) forms.Add(Join(clock, pace));
            forms.Add(clock);
        }
        else if (s.Clear)
        {
            Bit[] clock = { new(s.Clock, st.Clock, Ink, bold) };
            if (s.Verdict == "best")
            {
                Bit[] nb = { new("★ " + L.T("NEW BEST") + "  ", Math.Round(st.Clock * .8, 1), Gold, bold) };
                var pace = s.Pace.Length > 0 ? new[] { new Bit("  " + s.Pace, st.Pace, Mint, bold) } : Array.Empty<Bit>();
                if (pace.Length > 0) forms.Add(Join(nb, clock, pace));
                forms.Add(Join(nb, clock));
                forms.Add(Join(clock, new[] { new Bit("  ★ " + L.T("NEW BEST"), st.Word, Gold, bold) }));
                forms.Add(Join(clock, new[] { new Bit("  ★", st.Word, Gold, bold) }));
            }
            else
            {
                // "Cleared in 4:17", the clock big wherever the language puts it, then the verdict ("2nd fastest")
                var fmt = L.T("Cleared in {0}");
                int at = fmt.IndexOf("{0}", StringComparison.Ordinal);
                var cleared = new List<Bit>();
                if (at > 0) cleared.Add(new(fmt[..at], st.Word, Muted, semi));
                cleared.AddRange(clock);
                if (at >= 0 && at + 3 < fmt.Length) cleared.Add(new(fmt[(at + 3)..], st.Word, Muted, semi));
                var verdict = RunBuddy.Verdict(s);
                var vb = verdict.Length > 0 ? new[] { Sep(st.Word), new Bit(verdict, st.Word + .5, s.Verdict is "rank" or "first" ? Gold : Muted, bold) } : Array.Empty<Bit>();
                forms.Add(Join(cleared.ToArray(), vb));
                if (vb.Length > 0) forms.Add(Join(clock, vb));
                forms.Add(clock);
            }
        }
        else
        {
            if (s.TodayRuns.Length > 0)
            {
                string runs = s.TodayRuns == "1" ? L.T("1 run") : L.F("{0} runs", s.TodayRuns);
                var hero = s.TodayKinah.Length > 0 ? new Bit(s.TodayKinah, st.Clock, Gold, bold) : new Bit(runs, Math.Round(st.Clock * .8, 1), Ink, bold);
                forms.Add(new[] { hero, new Bit("  " + L.T("today here"), st.Word, Muted, semi) });
                forms.Add(new[] { hero });
            }
            else if (s.Best.Length > 0)
            {
                Bit[] best = { new(s.Best, st.Clock, Ink, bold) };
                forms.Add(Join(best, new[] { new Bit("  " + L.T("fastest"), st.Word, Muted, semi) }));
                forms.Add(best);
            }
            else forms.Add(new[] { new Bit(L.T("First clear here"), st.Word + 1, Muted, semi) });
        }
        return forms;
    }

    /// <summary>
    /// The small line: the last bosses' splits ("Kwapo 1:02 −0:05 · Duanka 2:44 +0:03", the newest alone when both don't
    /// fit); after a clear its worth against your average run ("+182K · avg 161.3K"); between runs today's runs and average
    /// clear ("2 runs · avg 4:26"). An empty form last: the line may go before a big figure is cut.
    /// </summary>
    protected static List<Bit[]> RunSmallForms(RunBuddy.State s, double size)
    {
        var forms = new List<Bit[]>();
        var bold = FontWeights.Bold; var semi = FontWeights.SemiBold;
        if (s.Clear && s.Kinah.Length > 0)
        {
            Bit[] kin = { new(s.Kinah, size + .5, Gold, bold) };
            if (s.Avg.Length > 0) forms.Add(Join(kin, new[] { Sep(size), new Bit(L.T("avg") + " ", size, Muted, semi), new Bit(s.Avg, size, Ink, semi) }));
            forms.Add(kin);
        }
        else if (s.Run || s.Clear)
        {
            Bit[] One(RunBuddy.Split x)
            {
                var parts = new List<Bit> { new(x.Boss, size, Ink, semi), new(" " + x.Clock, size, Muted, semi) };
                if (x.Delta.Length > 0) parts.Add(new(" " + x.Delta, size, x.Best ? Gold : x.Sign < 0 ? Mint : x.Sign > 0 ? Rec : Muted, bold));
                return parts.ToArray();
            }
            if (s.Splits.Count >= 2) forms.Add(Join(One(s.Splits[^2]), new[] { Sep(size) }, One(s.Splits[^1])));
            if (s.Splits.Count >= 1) forms.Add(One(s.Splits[^1]));
        }
        else if (s.TodayRuns.Length > 0)
        {
            string runs = s.TodayRuns == "1" ? L.T("1 run") : L.F("{0} runs", s.TodayRuns);
            var more = new List<Bit>();
            if (s.TodayKinah.Length > 0) more.Add(new(runs, size, Ink, semi));
            if (s.TodayAvg.Length > 0) { if (more.Count > 0) more.Add(Sep(size)); more.Add(new(L.T("avg") + " ", size, Muted, semi)); more.Add(new(s.TodayAvg, size, Ink, semi)); }
            if (more.Count > 0) forms.Add(more.ToArray());
            if (more.Count > 1) forms.Add(more.Take(1).ToArray());
        }
        // the essential form second whatever the fullest is (a look's order of giving way counts on it), then none
        if (forms.Count == 1) forms.Add(forms[0]);
        forms.Add(Array.Empty<Bit>());
        return forms;
    }

    protected static Bit[] Join(params Bit[][] parts) => parts.SelectMany(p => p).ToArray();

    /// <summary>
    /// <paramref name="n"/> forms (the fullest, ..., the essential; the last repeated where there are fewer), then none: a
    /// form's index means the same in every state, for a look's order of giving way.
    /// </summary>
    protected static List<Bit[]> Shape(List<Bit[]> forms, int n)
    {
        var f = forms.Where(x => x.Length > 0).Take(n).ToList();
        while (f.Count > 0 && f.Count < n) f.Add(f[^1]);
        f.Add(Array.Empty<Bit>());
        return f;
    }
}

// ================================================================ Capsule

/// <summary>
/// The Capsule in a run: one pill still, and like the farming pill without the place. The big figure at the pill's start
/// (the run clock and its pace; the clear's time, gold for a new best; between runs today here), then the pill's hairline and
/// two small lines stacked: your best and the count over the splits; after a clear how it ranks over the run's worth;
/// between runs today's runs over your clears and fastest. The strip and the timer's chip at the pill's end as while
/// farming; under a fight (its own meter, or Auto's above) the strip is away as then.
/// </summary>
sealed partial class HudCapsule
{
    static readonly RunStyle CapStyle = new(Clock: 20, Pace: 13, Word: 11.5, Small: 11.5, Head: 11.5);
    bool _runOn;
    string _runSig = "";

    /// <summary>The run drawn, or the farming line back on the way out; true while in a run (the farming drawing then waits).</summary>
    bool RunStep()
    {
        var s = KinahHidden ? null : _runRaw;
        if (s == null)
        {
            if (_runOn) { _runOn = false; _runSig = ""; _figSig = ""; ForgetStrip(); _row.Children.Clear(); _strip.Children.Clear(); _strip.Visibility = Visibility.Collapsed; }
            return false;
        }
        _runOn = true;
        DrawRun(s);
        return true;
    }

    /// <summary>The pill's three parts, each fullest first: the big figure, the line over and the line under.</summary>
    static (List<Bit[]> Big, List<Bit[]> Over, List<Bit[]> Under) CapForms(RunBuddy.State s)
    {
        var st = CapStyle;
        var bold = FontWeights.Bold; var semi = FontWeights.SemiBold;
        var big = new List<Bit[]>(); var over = new List<Bit[]>(); var under = new List<Bit[]>();
        Bit W(string t) => new(t, st.Head, Muted, semi);
        Bit F(string t, Brush? fg = null) => new(t, st.Head + .5, fg ?? Ink, bold);
        Bit Dot() => new("  ·  ", st.Head, Faint, semi);
        string? today = s.Today.Length > 0 ? L.F("Run {0} today", s.Today) : null;
        string? clears = s.Clears.Length > 0 ? (s.Clears == "1" ? L.T("1 clear") : L.F("{0} clears", s.Clears)) : null;
        if (s.Run)
        {
            var tone = PaceTone(s);
            Bit clock = new(s.Clock, st.Clock, tone, bold);
            if (s.Pace.Length > 0) big.Add(new[] { clock, new Bit("  " + s.Pace, st.Pace, tone, bold) });
            big.Add(new[] { clock });
            // over: "best 4:12 · Run 3 today · 38 clears", the count giving way first
            var lead = s.Best.Length > 0 ? new[] { W(L.T("best") + " "), F(s.Best) } : s.Verdict == "first" ? new[] { W(L.T("First clear here")) } : Array.Empty<Bit>();
            var counts = new List<Bit[]>();
            if (today != null && clears != null) counts.Add(new[] { W(today), Dot(), W(clears) });
            if (today != null) counts.Add(new[] { W(today) });
            else if (clears != null) counts.Add(new[] { W(clears) });
            foreach (var c in counts) over.Add(lead.Length > 0 ? Join(lead, new[] { Dot() }, c) : c);
            if (lead.Length > 0) over.Add(lead);
            under.AddRange(RunSmallForms(s, st.Small));
        }
        else if (s.Clear)
        {
            bool nb = s.Verdict == "best";
            Bit clock = new(s.Clock, st.Clock, nb ? Gold : Ink, bold);
            if (nb && s.Pace.Length > 0) big.Add(new[] { clock, new Bit("  " + s.Pace, st.Pace, Mint, bold) });
            big.Add(new[] { clock });
            // over: how it ranks ("★ NEW BEST", "2nd fastest", "Best 4:12"), the run's number today after it where it fits
            var verdict = RunBuddy.Verdict(s);
            var vb = verdict.Length == 0 ? Array.Empty<Bit>() : nb ? new[] { F("★ " + verdict, Gold) } : new[] { F(verdict, s.Verdict is "rank" or "first" ? Gold : Muted) };
            if (vb.Length > 0 && today != null) over.Add(Join(vb, new[] { Dot(), W(today) }));
            over.Add(vb);
            under.AddRange(RunSmallForms(s, st.Small));
        }
        else if (s.TodayRuns.Length > 0)
        {
            string runs = s.TodayRuns == "1" ? L.T("1 run") : L.F("{0} runs", s.TodayRuns);
            big.Add(new[] { s.TodayKinah.Length > 0 ? new Bit(s.TodayKinah, st.Clock, Gold, bold) : new Bit(runs, Math.Round(st.Clock * .8, 1), Ink, bold) });
            // over: "today here · 2 runs · avg 4:26"; under: "38 clears · fastest 4:12"
            var here = W(L.T("today here"));
            var avg = s.TodayAvg.Length > 0 ? new[] { Dot(), W(L.T("avg") + " "), F(s.TodayAvg) } : Array.Empty<Bit>();
            if (s.TodayKinah.Length > 0) { over.Add(Join(new[] { here, Dot(), F(runs) }, avg)); over.Add(new[] { here, Dot(), F(runs) }); }
            else if (avg.Length > 0) over.Add(Join(new[] { here }, avg));
            over.Add(new[] { here });
            var best = s.Best.Length > 0 ? new[] { W(L.T("fastest") + " "), F(s.Best) } : Array.Empty<Bit>();
            if (clears != null && best.Length > 0) under.Add(new[] { W(clears), Dot(), best[0], best[1] });
            if (best.Length > 0) under.Add(best);
            else if (clears != null) under.Add(new[] { W(clears) });
            under.Add(Array.Empty<Bit>());
        }
        else if (s.Best.Length > 0)
        {
            big.Add(new[] { new Bit(s.Best, st.Clock, Ink, bold) });
            over.Add(new[] { W(L.T("fastest")) });
            if (clears != null) under.Add(new[] { W(clears) });
        }
        else big.Add(new[] { new Bit(L.T("First clear here"), 13, Muted, semi) });
        return (big, Shape(over, 3), Shape(under, 2));
    }

    /// <summary>
    /// The order the pill's parts give way in (big; over: full, no count, essential, none; under: full, newest, none): the
    /// count, the older split, then the line over (your best) or under (the split) when the timer's chip takes the room, the
    /// pace last.
    /// </summary>
    static readonly (int B, int O, int U)[] CapLevels = { (0, 0, 0), (0, 1, 0), (0, 1, 1), (0, 2, 1), (0, 2, 2), (0, 3, 1), (0, 3, 2), (1, 2, 2), (1, 3, 2) };

    void DrawRun(RunBuddy.State s)
    {
        bool stripOn = _lastFight == null && !_fightAbove;
        var sig = $"{RunBuddy.Sig(_values)}|{(stripOn ? PickStrip.Sig(_values, StripLook) : "-")}|{_chip.Visibility}|{_chipText.Text}|{FightWidth}|{_figsNow?.Note}";
        if (sig == _runSig) return;
        _runSig = sig;
        _figSig = "";   // the farming line drawn again on the way out

        var (bigs, overs, unders) = CapForms(s);
        _row.Children.Clear();
        var big = RunText(); big.Margin = new Thickness(8, 0, 0, 0);
        var a = RunText(); var b = RunText();
        a.LineHeight = 15; a.LineStackingStrategy = LineStackingStrategy.BlockLineHeight;
        b.LineHeight = 15; b.LineStackingStrategy = LineStackingStrategy.BlockLineHeight;
        var stack = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Children = { a, b } };
        var rule = Rule(22);
        _row.Children.Add(big); _row.Children.Add(rule); _row.Children.Add(stack);

        // the fullest words with the best drop and the last three; then the three alone; then the words give way, the three kept
        var strips = stripOn ? RunStrips(StripLook) : new List<FrameworkElement>();
        var options = strips.Count > 0 ? strips.Cast<FrameworkElement?>().ToList() : new List<FrameworkElement?> { null };
        bool done = false;
        for (int li = 0; li < CapLevels.Length && !done; li++)
        {
            var (g, o, u) = CapLevels[li];
            SetBits(big, At(bigs, g)); SetBits(a, At(overs, o)); SetBits(b, At(unders, u));
            bool words = a.Visibility == Visibility.Visible || b.Visibility == Visibility.Visible;
            rule.Visibility = stack.Visibility = words ? Visibility.Visible : Visibility.Collapsed;
            rule.Height = a.Visibility == Visibility.Visible && b.Visibility == Visibility.Visible ? 26 : 14;
            foreach (var st in li == 0 ? options : options.TakeLast(1).ToList())
            {
                SetStrip(st);
                if (Wants(_pill) <= FightWidth + .5) { done = true; break; }
            }
        }
        _note.Text = _figsNow?.Note ?? ""; _note.Foreground = _figsNow?.NoteTone ?? Muted;
        _note.Visibility = _note.Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;

        void SetStrip(FrameworkElement? st)
        {
            _strip.Children.Clear();
            if (st == null) { _strip.Visibility = Visibility.Collapsed; return; }
            _strip.Children.Add(Rule()); _strip.Children.Add(st);
            _strip.Visibility = Visibility.Visible;
        }
    }
}

// ================================================================ Meter

/// <summary>
/// The Meter in a run: the header names the dungeon and its mode, the strip at its right where it sits while farming (the
/// farming clock away); the foot is the run, the clock big where the hour is and its splits under it where the chase is, and
/// at the right where the session is your best over the count ("Run 3 today · 38 clears"). After a clear the verdict and the
/// run's worth, the run's number today and your clears at the right; between runs today here, your fastest and clears at the
/// right. A fight takes the header as always; the foot keeps the run, smaller.
/// </summary>
sealed partial class HudMeter
{
    static readonly RunStyle FootStyle = new(Clock: 22, Pace: 13.5, Word: 12, Small: 11.5, Head: 13.5);
    static readonly RunStyle FightFootStyle = new(Clock: 15, Pace: 11.5, Word: 11, Small: 11, Head: 13.5);
    /// <summary>The count ("Run 3 today · 38 clears") at the header's right where the farming clock sits, while it fits there.</summary>
    readonly TextBlock _runCount = new() { FontSize = 11.5, FontWeight = FontWeights.SemiBold, Foreground = Muted, VerticalAlignment = VerticalAlignment.Center, Visibility = Visibility.Collapsed };
    bool _runOn, _runPut;
    string _runSig = "";

    bool RunStep()
    {
        var s = KinahHidden ? null : _runRaw;
        if (s == null)
        {
            if (_runOn)
            {
                _runOn = false; _runSig = ""; _figSig = ""; ForgetStrip();
                _clock.Visibility = Visibility.Visible; _runCount.Visibility = Visibility.Collapsed;
                _strip.Children.Clear(); _strip.Visibility = Visibility.Collapsed;
                if (_figs is { } f && !_fight) _title.Text = f.Zone.Length > 0 ? f.Zone : "Kinah Ledger";
            }
            return false;
        }
        _runOn = true;
        if (!_runPut) { _runPut = true; _right.Children.Add(_runCount); }
        DrawRun(s);
        return true;
    }

    /// <summary>The foot's right side, two lines each fullest first: your best (or the run's number today, or your fastest) over the count.</summary>
    /// <param name="countInHead">The count is in the header: the side gives your best alone (after a clear and between runs, nothing).</param>
    static (List<Bit[]> Top, List<Bit[]> Bottom) SideForms(RunBuddy.State s, bool fight, bool countInHead)
    {
        double fs = fight ? 12 : 13, ws = fight ? 10.5 : 11;
        var semi = FontWeights.SemiBold; var bold = FontWeights.Bold;
        Bit W(string t) => new(t, ws, Muted, semi);
        Bit Dot() => new("  ·  ", ws, Faint, semi);
        string? today = s.Today.Length > 0 ? L.F("Run {0} today", s.Today) : null;
        string? clears = s.Clears.Length > 0 ? (s.Clears == "1" ? L.T("1 clear") : L.F("{0} clears", s.Clears)) : null;
        var top = new List<Bit[]>(); var bottom = new List<Bit[]>();
        if (s.Run)
        {
            if (s.Best.Length > 0) top.Add(new[] { W(L.T("best") + " "), new Bit(s.Best, fs, Ink, bold) });
            if (today != null && clears != null) bottom.Add(new[] { W(today), Dot(), W(clears) });
            if (today != null) bottom.Add(new[] { W(today) });
            else if (clears != null) bottom.Add(new[] { W(clears) });
        }
        else if (s.Clear)
        {
            if (today != null) top.Add(new[] { W(today) });
            if (clears != null) bottom.Add(new[] { W(clears) });
        }
        else
        {
            if (s.TodayRuns.Length > 0 && s.Best.Length > 0) top.Add(new[] { W(L.T("fastest") + " "), new Bit(s.Best, fs, Ink, bold) });
            if (clears != null) bottom.Add(new[] { W(clears) });
        }
        if (countInHead) { bottom.Clear(); if (!s.Run) top.Clear(); }
        return (Shape(top, 1), Shape(bottom, 2));
    }

    void DrawRun(RunBuddy.State s)
    {
        bool stripOn = !_fight;
        var sig = $"{RunBuddy.Sig(_values)}|{(stripOn ? PickStrip.Sig(_values, StripLook) : "-")}|{_fight}|{_tab.Visibility}|{_tabText.Text}|{FightWidth}|{_figsNow?.Note}";
        if (sig == _runSig) return;
        _runSig = sig;
        _figSig = "";

        // the header (a fight's own while one shows): the dungeon and its mode, the strip at the right
        _clock.Visibility = _fight ? Visibility.Visible : Visibility.Collapsed;
        _runCount.Visibility = Visibility.Collapsed;
        bool countInHead = false;
        if (_fight) { _strip.Children.Clear(); _strip.Visibility = Visibility.Collapsed; }
        else
        {
            var place = new Bit(s.Place, 13.5, Ink, FontWeights.Bold);
            var titles = new List<Bit[]>();
            if (s.Mode.Length > 0) titles.Add(new[] { place, new Bit(" · " + s.Mode, 12.5, Muted, FontWeights.SemiBold) });
            titles.Add(new[] { place });
            var strips = RunStrips(StripLook);
            var options = strips.Count > 0 ? strips.Cast<FrameworkElement?>().ToList() : new List<FrameworkElement?> { null };
            bool done = false;
            // the count at the right while it fits with the strip; else it goes to the foot's right side
            var count = RunBuddy.Count(s);
            if (count.Length > 0)
            {
                SetBits(_title, titles[0]);
                _runCount.Text = count; _runCount.Visibility = Visibility.Visible;
                foreach (var st in options)
                {
                    _strip.Children.Clear();
                    if (st != null) _strip.Children.Add(st);
                    _strip.Visibility = st != null ? Visibility.Visible : Visibility.Collapsed;
                    if (Wants(_title) + Wants(_hpPct) + Wants(_right) <= W - 20 + .5) { done = countInHead = true; break; }
                }
                if (!done) _runCount.Visibility = Visibility.Collapsed;
            }
            for (int li = 0; li < titles.Count && !done; li++)
            {
                SetBits(_title, titles[li]);
                foreach (var st in li == 0 ? options : options.TakeLast(1).ToList())
                {
                    _strip.Children.Clear();
                    if (st != null) _strip.Children.Add(st);
                    _strip.Visibility = st != null ? Visibility.Visible : Visibility.Collapsed;
                    if (Wants(_title) + Wants(_hpPct) + Wants(_right) <= W - 20 + .5) { done = true; break; }
                }
            }
        }

        // the foot: the run where the hour is, your best and the count where the session is
        _foot.Visibility = Visibility.Visible;
        _foot.Children.Clear(); _foot.ColumnDefinitions.Clear();
        _foot.Margin = new Thickness(10, _fight ? 6 : 8, 10, _fight ? 7 : 9);
        _foot.ColumnDefinitions.Add(new ColumnDefinition()); _foot.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var st0 = _fight ? FightFootStyle : FootStyle;
        var (tops, bottoms) = SideForms(s, _fight, countInHead);
        var side = new StackPanel { VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(12, 0, 0, 0) };
        var t1 = RunText(); var t2 = RunText();
        t1.HorizontalAlignment = t2.HorizontalAlignment = HorizontalAlignment.Right;
        side.Children.Add(t1); if (!_fight) side.Children.Add(t2);
        Grid.SetColumn(side, 1); _foot.Children.Add(side);
        var mid = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        var big = RunText(); var small = RunText();
        small.Margin = new Thickness(0, 1, 0, 0);
        mid.Children.Add(big); mid.Children.Add(small);
        _foot.Children.Add(mid);
        // the run's own lines first (fullest, with the side's fullest); then the side gives way to them, never a figure cut
        var bigs = RunBigForms(s, st0, withBest: !(s.Run && s.Best.Length > 0));
        var smalls = RunSmallForms(s, st0.Small);
        double inner = W - 20;
        foreach (var (ti, bi) in new[] { (0, 0), (0, 1), (0, 2), (1, 2) })
        {
            SetBits(t1, At(tops, ti)); SetBits(t2, _fight ? Array.Empty<Bit>() : At(bottoms, bi));
            double room = inner - (t1.Visibility == Visibility.Visible || t2.Visibility == Visibility.Visible ? Wants(side) : 0);
            int ib = FitBits(big, room, bigs);
            int iSmall = 0;
            if (_fight) small.Visibility = Visibility.Collapsed; else iSmall = FitBits(small, room, smalls);
            // enough: the big line whole and a split (or the worth, or today's runs) under it
            if (ib == 0 && (iSmall < smalls.Count - 1 || smalls.Count == 1)) break;
        }
        side.Visibility = t1.Visibility == Visibility.Visible || t2.Visibility == Visibility.Visible ? Visibility.Visible : Visibility.Collapsed;
        _note.Text = _figsNow?.Note ?? ""; _note.Foreground = _figsNow?.NoteTone ?? Muted;
        _note.Visibility = _note.Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
    }
}

// ================================================================ Float

/// <summary>
/// The Float in a run: straight over the game with its halo, the head small over the clock, the splits under it, the strip
/// after them as while farming (away under a fight, as then).
/// </summary>
sealed partial class HudFloat
{
    static readonly RunStyle FloatStyle = new(Clock: 22, Pace: 13.5, Word: 12.5, Small: 11.5, Head: 11.5);
    bool _runOn;
    string _runSig = "";

    bool RunStep()
    {
        var s = KinahHidden ? null : _runRaw;
        if (s == null)
        {
            if (_runOn) { _runOn = false; _runSig = ""; _figSig = ""; ForgetStrip(); _strip.Children.Clear(); _strip.Visibility = Visibility.Collapsed; }
            return false;
        }
        _runOn = true;
        DrawRun(s);
        return true;
    }

    void DrawRun(RunBuddy.State s)
    {
        bool stripOn = _lastFight == null;
        var sig = $"{RunBuddy.Sig(_values)}|{(stripOn ? PickStrip.Sig(_values, StripLook) : "-")}|{FightWidth}|{_figsNow?.Note}";
        if (sig == _runSig) return;
        _runSig = sig;
        _figSig = "";

        _line.Children.Clear();
        var figs = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        var a = RunText(); var big = RunText(); var b = RunText();
        figs.Children.Add(a); figs.Children.Add(big); figs.Children.Add(b);
        var heads = RunHeadForms(s, FloatStyle.Head);
        var bigs = RunBigForms(s, FloatStyle);
        var smalls = RunSmallForms(s, FloatStyle.Small);
        // the fullest lines with the best drop and the last three, else the three alone and the lines in what is left
        double full = Math.Max(SetBits(a, heads[0]), Math.Max(SetBits(big, bigs[0]), SetBits(b, smalls[0])));
        double room = FightWidth - 10;
        _strip.Children.Clear(); _strip.Visibility = Visibility.Collapsed;
        var strips = stripOn ? RunStrips(StripLook) : new List<FrameworkElement>();
        FrameworkElement? pick = null;
        foreach (var st in strips)
        {
            pick = st;
            if (full + Wants(st) + _strip.Margin.Left <= room + .5) break;
        }
        if (pick != null) { room -= Wants(pick) + _strip.Margin.Left; _strip.Children.Add(pick); _strip.Visibility = Visibility.Visible; }
        FitBits(a, room, heads); FitBits(big, room, bigs); FitBits(b, room, smalls);
        _line.Children.Add(figs);
        _line.Children.Add(_strip);
        _note.Text = _figsNow?.Note ?? ""; _note.Foreground = _figsNow?.NoteTone ?? Muted;
        _note.Visibility = _note.Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
    }
}
