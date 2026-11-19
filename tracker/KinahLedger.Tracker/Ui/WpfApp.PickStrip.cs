using System.IO;
using System.Windows;
using System.Windows.Media;
using KinahLedger.Parser;
using KinahLedger.Session;

namespace KinahLedger.Ui;

/// <summary>
/// The pickup strip on every look (Steven, 2026-11-19: "the list of 3 items with the best one is not showing up on my buddies
/// consistently now - it need to show up on ALL buddies"): whenever a look shows the Kinah figures and there is a pickup this
/// session, it shows the best drop with its star (unless the best is one of the three) and the last three pickups, farming,
/// in a dungeon run, with timers due, and with a fight up beside the Kinah line. The options test checks every look in
/// each of those states at Steven's Scale 1.1 (<c>KL_OPTIONS_ONLY=picks</c> alone); <c>ui-shot DIR --parser picks</c> draws them
/// (ui-pick-LOOK-STATE.png, and spill.txt for anything cut).
/// </summary>
public static partial class WpfApp
{
    /// <summary>Made-up pickups (no real item's name): name, count, worth, grade, kind. The icons are borrowed from the window's sample.</summary>
    static readonly (string Name, string Count, string Worth, string Grade, string Kind)[] StripPickSample =
    {
        ("Glimmerfen Shard", "×2", "+6.4K", "Rare", "item"),
        ("2,310 Kinah", "2310", "+2.3K", "", "kinah"),
        ("Duskmoth Wing", "×1", "+1.4K", "", "item"),
        ("Hollowpine Resin", "×3", "+2.2K", "", "item"),
    };

    /// <summary>The farming values with <paramref name="n"/> of the made-up pickups (newest first) and the session's best drop: a made-up Legend, or the second pickup when <paramref name="bestInThree"/>.</summary>
    static Dictionary<string, string> WithStripPicks(IReadOnlyDictionary<string, string> v, IReadOnlyDictionary<string, string> sample, int n, bool bestInThree = false)
    {
        var d = new Dictionary<string, string>(v);
        foreach (var k in d.Keys.Where(k => k.StartsWith("pick") || k.StartsWith("top")).ToList()) d[k] = "";
        var t = DateTime.UtcNow;
        for (int i = 0; i < Math.Min(n, StripPickSample.Length); i++)
        {
            var (name, count, worth, grade, kind) = StripPickSample[i];
            var p = "pick" + i;
            d[p] = name; d[p + "Id"] = "strip:" + i; d[p + "At"] = t.AddSeconds(-30 - 5 * i).Ticks.ToString(); d[p + "Count"] = count; d[p + "Worth"] = worth;
            d[p + "Grade"] = grade; d[p + "Kind"] = kind; d[p + "Note"] = "";
            d[p + "Icon"] = sample.GetValueOrDefault(p + "Icon") ?? "";
        }
        // Badge's feed line (its default figure): the newest pickup and the hour, as the window fills them
        if (n > 0) { d["feedText"] = StripPickSample[0].Name; d["feedValue"] = StripPickSample[0].Worth; d["feedRate"] = d.GetValueOrDefault("rate", ""); d["feedUnit"] = "an hour"; }
        if (bestInThree && n >= 2) { d["top"] = StripPickSample[0].Name; d["topCount"] = StripPickSample[0].Count; d["topWorth"] = StripPickSample[0].Worth; d["topGrade"] = StripPickSample[0].Grade; d["topIcon"] = d["pick0Icon"]; }
        else { d["top"] = "Starforged Visage"; d["topCount"] = "×1"; d["topWorth"] = "+31K"; d["topGrade"] = "Legend"; d["topIcon"] = sample.GetValueOrDefault("topIcon") ?? ""; }
        return d;
    }

    /// <summary>One state the strip is checked and drawn in: the values, timers due, the fight, in a dungeon (the Kinah line away), and what the strip should show.</summary>
    sealed record StripState(string Tag, Dictionary<string, string> V, List<TimerBoard.Item>? T, Encounter? E, DateTime At, bool Dungeon, int Tiles, bool Best);

    static List<StripState> StripStates(MainWindow w)
    {
        var kinah = w.HudValues.ToDictionary(kv => kv.Key, kv => kv.Value);
        var sample = w.HudSample();
        var now = new DateTime(2026, 11, 13, 21, 30, 0, DateTimeKind.Utc);
        var (past, live) = ParserDemo.Sample(now, live: true, damage: true);
        var kill = past.Last(e => e.Outcome == "kill");
        var due = new List<TimerBoard.Item>
        {
            new("event:Shugo Festival", "Events", "Shugo Festival", "", TimerBoard.Kind.On, now.AddSeconds(161), "2m 41s", "left to enter") { Short = "Shugo" },
            new("event:Dimensional Invasion", "Events", "Dimensional Invasion", "", TimerBoard.Kind.Waiting, now.AddSeconds(279), "4m 39s", "") { Short = "Invasion" },
        };
        Dictionary<string, string> P(Dictionary<string, string> v, int n, bool inThree = false) => WithStripPicks(v, sample, n, inThree);
        Dictionary<string, string> Run(string kind)
        {
            var v = new Dictionary<string, string>(kinah) { ["idle"] = "", ["chase"] = "", ["toBest"] = "" };
            foreach (var (k, x) in MainWindow.RunBuddyValues(MainWindow.RunBuddySample(kind))) v[k] = x;
            return P(v, 4);
        }
        var warming = new Dictionary<string, string>(kinah) { ["warm"] = "1", ["rate"] = "warming up", ["time"] = "RUN 0:48", ["total"] = "21.4K", ["chase"] = "", ["toBest"] = "" };
        var best = new Dictionary<string, string>(kinah) { ["chase"] = "1.000", ["toBest"] = "New best here", ["rate"] = "1.52M" };
        var notice = new Dictionary<string, string>(kinah) { ["paused"] = "Paused: Chrome covers the chat", ["show"] = "dim" };
        var wide = new Dictionary<string, string>(kinah) { ["rate"] = "12.84M", ["total"] = "184.27M", ["kinah"] = "9.12M", ["loot"] = "3.72M", ["toBest"] = "1.27M to your best here", ["time"] = "RUN 11:42:17" };
        return new()
        {
            new("farm-1", P(kinah, 1), null, null, now, false, 1, true),
            new("farm-3", P(kinah, 3), null, null, now, false, 3, true),
            new("farm-5", P(kinah, 4), null, null, now, false, 3, true),
            new("farm-best-in-three", P(kinah, 4, true), null, null, now, false, 3, false),
            new("farm-wide", P(wide, 4), null, null, now, false, 3, true),
            new("farm-timers", P(kinah, 4), due, null, now, false, 3, true),
            new("warming", P(warming, 2), null, null, now, false, 2, true),
            new("best", P(best, 4), null, null, now, false, 3, true),
            new("notice", P(notice, 4), null, null, now, false, 3, true),
            new("run", Run("run"), null, null, now, false, 3, true),
            new("run-ahead", Run("ahead"), null, null, now, false, 3, true),
            new("run-between", Run("between"), null, null, now, false, 3, true),
            new("run-clear", Run("clear"), null, null, now, false, 3, true),
            new("run-timers", Run("ahead"), due, null, now, false, 3, true),
            new("fight", P(kinah, 4), null, live, now, false, 3, true),
            new("fight-timers", P(kinah, 4), due, live, now, false, 3, true),
            new("fight-over", P(kinah, 4), null, kill, kill.End!.Value, false, 3, true),
            new("fight-run", Run("ahead"), null, live, now, false, 3, true),
            // a dungeon's fight puts the Kinah line away, the strip with it
            new("dungeon-fight", P(kinah, 4), null, live, now, true, 0, false),
        };
    }

    /// <summary>A look's window drawn in a state at <paramref name="scale"/>, shown off screen and laid out.</summary>
    static HudWindow StripWindow(string look, StripState s, double scale)
    {
        var h = new HudWindow(null, look, still: true) { Left = -12000, Top = -12000, ShowActivated = false };
        h.SetScale(scale);
        h.Update(s.V);
        if (s.T != null) h.ShowTimers(s.T, s.At);
        if (s.E != null) h.ShowParser(s.E, s.At, s.E.ModeKind, details: false);
        if (s.Dungeon) h.SetKinahHidden(true);
        h.Show(); DoEvents();
        return h;
    }

    /// <summary>
    /// What a window shows of the strip: its visible tiles ("pick-tile") and best drops ("pick-best"), and any of them drawn
    /// outside the window (cut off).
    /// </summary>
    static (int Tiles, int Best, int Cut) StripShown(Window h)
    {
        int tiles = 0, best = 0, cut = 0;
        if (h.Content is not FrameworkElement root) return (0, 0, 0);
        var bounds = new Rect(0, 0, root.ActualWidth, root.ActualHeight);
        bounds.Inflate(1, 1);
        void Walk(DependencyObject d)
        {
            if (d is UIElement u && !u.IsVisible) return;
            if (d is FrameworkElement fe && fe.Tag is "pick-tile" or "pick-best" && fe.ActualWidth > 0 && fe.Opacity > 0.05)
            {
                if (fe.Tag is "pick-tile") tiles++; else best++;
                var r = fe.TransformToAncestor(root).TransformBounds(new Rect(0, 0, fe.ActualWidth, fe.ActualHeight));
                if (!bounds.Contains(r)) cut++;
            }
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(d); i++) Walk(VisualTreeHelper.GetChild(d, i));
        }
        Walk(root);
        return (tiles, best, cut);
    }

    /// <summary>
    /// The best drops a state should show: one unless the best is one of the three. The Pop pins its top drop toast whatever
    /// (Steven, 2026-10-26: "the 3 most recent and the Top Drop"), the same item as a pickup only when it came more than once.
    /// </summary>
    static int WantBest(string look, StripState s) => s.Tiles > 0 && (s.Best || look == HudLooks.Ticker) ? 1 : 0;

    /// <summary>options-test: every look shows the best drop and the last three wherever it shows the Kinah figures.</summary>
    static void PickStripChecks(MainWindow w, Action<bool, string> check)
    {
        bool shown0 = PickStrip.Shown;
        PickStrip.Shown = true;
        var states = StripStates(w);
        foreach (var url in states.SelectMany(s => s.V).Where(kv => kv.Key.EndsWith("Icon") && kv.Value.Length > 0).Select(kv => kv.Value).Distinct()) IconCache.Get(url);
        IconCache.Drain(TimeSpan.FromSeconds(10)); DoEvents();
        var bad = new List<string>();
        double w0 = HudStackLook.FightWidth;
        // the meter's default width and its narrowest (the looks fit the strip to it)
        foreach (var width in new[] { HudStackLook.FightWidthDefault, HudStackLook.FightWidthMin })
        {
            HudStackLook.FightWidth = width;
            foreach (var look in HudLooks.All.Select(l => l.Key))
                foreach (var s in states)
                {
                    var h = StripWindow(look, s, 1.1);
                    var (tiles, best, cut) = StripShown(h);
                    h.Close();
                    int wantBest = WantBest(look, s);
                    if (tiles != s.Tiles || best != wantBest || cut > 0)
                        bad.Add($"{look} {s.Tag} at {width}: {tiles} of {s.Tiles} tiles, {(best == wantBest ? "best ok" : best == 0 ? "no best" : $"{best} best")}{(cut > 0 ? $", {cut} cut off" : "")}");
                }
        }
        HudStackLook.FightWidth = w0;
        check(bad.Count == 0, $"every look ({HudLooks.All.Length}) shows the best drop and the last three with the Kinah figures, farming, in a run, with timers and beside a fight, at 1.1x and the meter's default and narrowest widths" + (bad.Count > 0 ? ": " + string.Join("; ", bad) : ""));
        // Settings › Buddy › More › Pickup icons off: no look draws a tile
        PickStrip.Shown = false;
        var off = new List<string>();
        foreach (var look in HudLooks.All.Select(l => l.Key).Where(k => k != HudLooks.Ticker))   // the Pop's toasts are its pickups, not icons on a panel
        {
            var h = StripWindow(look, states[2], 1.1);
            var (tiles, best, _) = StripShown(h);
            h.Close();
            if (tiles + best > 0) off.Add(look);
        }
        check(off.Count == 0, "with Pickup icons off no look draws the strip" + (off.Count > 0 ? ": " + string.Join(", ", off) : ""));
        PickStrip.Shown = shown0;
    }

    /// <summary>ui-shot DIR --parser picks: every look in every strip state at 1.1x (ui-pick-LOOK-STATE.png; KL_THEME_LOOK=fighter,auto for those only), spill.txt for anything cut.</summary>
    static void ShotPickStrips(MainWindow w, string dir)
    {
        var only = (Environment.GetEnvironmentVariable("KL_THEME_LOOK") ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        try { File.Delete(Path.Combine(dir, "spill.txt")); } catch (IOException) { }
        bool shown0 = PickStrip.Shown;
        PickStrip.Shown = true;
        var states = StripStates(w);
        foreach (var url in states.SelectMany(s => s.V).Where(kv => kv.Key.EndsWith("Icon") && kv.Value.Length > 0).Select(kv => kv.Value).Distinct()) IconCache.Get(url);
        IconCache.Drain(TimeSpan.FromSeconds(10)); DoEvents();
        var report = new List<string>();
        double w0 = HudStackLook.FightWidth;
        // every state at the meter's default width; the tightest ones again at its narrowest ("-w300")
        var narrow = new[] { "farm-wide", "farm-timers", "fight-timers", "run-timers" };
        foreach (var look in HudLooks.All.Select(l => l.Key).Where(k => only.Length == 0 || only.Contains(k)))
            foreach (var (s, width) in states.Select(s => (s, HudStackLook.FightWidthDefault)).Concat(states.Where(s => narrow.Contains(s.Tag)).Select(s => (s, HudStackLook.FightWidthMin))))
            {
                HudStackLook.FightWidth = width;
                var h = StripWindow(look, s, 1.1);
                var (tiles, best, cut) = StripShown(h);
                var file = $"ui-pick-{look}-{s.Tag}{(width == HudStackLook.FightWidthDefault ? "" : "-w" + width)}.png";
                Save((FrameworkElement)h.Content, Path.Combine(dir, file), 0, ShotGround);
                SpillNote(dir, file, (FrameworkElement)h.Content);
                bool ok = tiles == s.Tiles && best == WantBest(look, s) && cut == 0;
                report.Add($"{(ok ? "ok  " : "BAD ")} {file}: {tiles} tiles, {best} best, {cut} cut");
                h.Close();
            }
        HudStackLook.FightWidth = w0;
        PickStrip.Shown = shown0;
        File.WriteAllLines(Path.Combine(dir, "picks.txt"), report);
        Console.WriteLine($"{report.Count} pickup-strip renders, {report.Count(r => r.StartsWith("BAD"))} without the strip as they should show it (picks.txt)");
    }
}
