using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;

namespace KinahLedger.Ui;

public static partial class WpfApp
{
    /// <summary>
    /// options-test (<c>KL_OPTIONS_ONLY=typing</c> alone): the Terminal types a new pickup's line out, live (Steven, 2026-11-18: "it
    /// should make it look like its typing it out since its fallout style lol"). A pickup just made grows one character at a time
    /// to the whole line inside the cap, in prefixes of it, the cursor at its end; a second pickup in the middle of the first
    /// types from its start; a refresh of the same pickup, or a window opened on one, shows it whole; and the line's width
    /// never changes while it types. Skipped where Windows's animations are off.
    /// </summary>
    static void TypingChecks(MainWindow w, Action<bool, string> check)
    {
        if (!Motion.On) { check(true, "terminal typing: skipped (Windows animations are off)"); return; }
        var v0 = w.HudValues.ToDictionary(kv => kv.Key, kv => kv.Value);
        foreach (var k in v0.Keys.Where(k => k.StartsWith("pick") || k.StartsWith("top") || k.StartsWith("rare")).ToList()) v0[k] = "";
        Dictionary<string, string> With(string id, string name, string worth, Dictionary<string, string>? from = null)
        {
            var v = new Dictionary<string, string>(from ?? v0) { ["pick0"] = name, ["pick0Id"] = id, ["pick0Worth"] = worth, ["pick0At"] = DateTime.UtcNow.Ticks.ToString() };
            return v;
        }
        var h = new HudWindow(null, "terminal", still: false) { Left = -12000, Top = -12000, ShowActivated = false };
        try
        {
            h.Update(v0);
            h.Show(); DoEvents();
            string Line() => LogicalTexts((FrameworkElement)h.Content).FirstOrDefault(t => t.StartsWith("> ", StringComparison.Ordinal) && !t.Contains("--") && !t.StartsWith("> fight", StringComparison.Ordinal)) ?? "";
            double Width() { var c = (FrameworkElement)h.Content; c.UpdateLayout(); return c.ActualWidth; }
            // sampled until it stops growing or a second passes: every different text, in order, and when the last came
            (List<string> Seen, double Ms, double MaxWidth, double MinWidth) Watch(string whole)
            {
                var seen = new List<string> { Line() };
                double wMin = Width(), wMax = wMin, at = 0;
                var sw = Stopwatch.StartNew();
                while (sw.ElapsedMilliseconds < 1000 && seen[^1] != whole)
                {
                    DoEvents(); System.Threading.Thread.Sleep(4);
                    var t = Line();
                    if (t != seen[^1]) { seen.Add(t); at = sw.Elapsed.TotalMilliseconds; }
                    double x = Width(); wMin = Math.Min(wMin, x); wMax = Math.Max(wMax, x);
                }
                return (seen, at, wMax, wMin);
            }

            var v1 = With("t1", "Spiritstone", "+7.5K");
            h.Update(v1);
            string whole1 = "> +7.5K Spiritstone";
            var first = Line();
            var a = Watch(whole1);
            check(first.Length < whole1.Length && a.Seen.Count >= 6 && a.Seen[^1] == whole1, $"terminal typing: a new pickup types out a character at a time, not at once (started with \"{first}\", {a.Seen.Count} states, ended \"{a.Seen[^1]}\")");
            check(a.Seen.Zip(a.Seen.Skip(1)).All(p => p.Second.StartsWith(p.First, StringComparison.Ordinal) && p.Second.Length > p.First.Length), $"terminal typing: each state is the one before with more written ({string.Join(" | ", a.Seen)})");
            check(a.Ms is > 250 and < 750, $"terminal typing: the line takes about half a second, never past the cap ({a.Ms:0} ms)");
            check(a.MaxWidth - a.MinWidth < .5, $"terminal typing: the window keeps its width while the line types ({a.MinWidth:0.0} to {a.MaxWidth:0.0})");

            // a refresh (the hour moves, the same pickup): whole at once, nothing typed again
            var v1b = new Dictionary<string, string>(v1) { ["rate"] = "1.31M", ["total"] = "901K" };
            h.Update(v1b); DoEvents();
            check(Line() == whole1, $"terminal typing: a refresh of the same pickup leaves the line whole ({Line()})");

            // a new pickup arrives while another types: the first is finished, the second types from its start
            var v2 = With("t2", "Aether Sliver", "+1.2K", v1b);
            h.Update(v2);
            System.Threading.Thread.Sleep(260); DoEvents();
            var mid = Line();
            var v3 = With("t3", "Dawn Legion Badge", "+300", v2);
            h.Update(v3);
            var start3 = Line();
            var c = Watch("> +300 Dawn Legion Badge");
            check(mid.Length > 2 && mid.Length < "> +1.2K Aether Sliver".Length && start3.Length < "> +300 Dawn Legion Badge".Length && c.Seen[^1] == "> +300 Dawn Legion Badge",
                $"terminal typing: a pickup in the middle of another's line finishes it and types its own (\"{mid}\", then \"{start3}\" to \"{c.Seen[^1]}\")");

            // a window opened on a pickup, or its session loaded: whole
            h.Close();
            h = new HudWindow(null, "terminal", still: false) { Left = -12000, Top = -12000, ShowActivated = false };
            h.Update(v3); h.Show(); DoEvents();
            check(Line() == "> +300 Dawn Legion Badge", $"terminal typing: a window opened on a pickup shows it whole ({Line()})");
        }
        finally { h.Close(); }
    }
}
