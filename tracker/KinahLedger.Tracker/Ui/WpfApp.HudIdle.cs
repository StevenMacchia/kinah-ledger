using System.Globalization;
using System.Windows;

namespace KinahLedger.Ui;

public static partial class WpfApp
{
    /// <summary>
    /// options-test: the overlay's idle line (Steven, 2026-11-17: "Give something informative" in place of "Waiting for the first
    /// count"). It is filled in a dungeon, in the open world with history and on a fresh install; every look shows it where
    /// the hour waits, never the old words; and it is never cut off, in English, Korean and Traditional Chinese, at the meter's
    /// 350 and Steven's Scale 1.1.
    /// </summary>
    static void HudIdleChecks(MainWindow w, Action<bool, string> check)
    {
        static string Norm(string s) => new string(s.Where(c => !char.IsWhiteSpace(c)).ToArray()).ToUpperInvariant();
        string Line(string kind) => MainWindow.IdleLine(MainWindow.IdleSampleFacts(kind));

        // the facts each place gives
        string dungeon = Line("dungeon"), open = Line("open"), history = Line("history"), abyss = Line("abyss"), fresh = Line("fresh");
        check(dungeon.StartsWith("Fire Temple · Conquest", StringComparison.Ordinal) && dungeon.Contains("412K") && dungeon.Contains("6:31"),
            $"idle line in a dungeon: the place and its mode, then the last run there ({dungeon})");
        check(open.StartsWith("Today 1.27M · 2h 14m", StringComparison.Ordinal) && history.StartsWith("Yesterday 894K · 1.12M/h", StringComparison.Ordinal) && abyss.StartsWith("Today 12,400 AP · 3,100 RP", StringComparison.Ordinal),
            $"idle line in the open world: today's made and time, else the last day farmed; in the Abyss today's points ({open} | {history} | {abyss})");
        check(fresh == "Counting starts with your first Kinah or pickup" && MainWindow.IdleLine(new MainWindow.IdleFacts { BestHere = 1_400_000 }) == $"Best here {Amounts.Short(1_400_000)}/h",
            $"idle line on a fresh install: where counting starts; with only a best here, that ({fresh})");
        check(dungeon.Contains("Shugo 23m") || !MainWindow.IdleFits(dungeon + " · Shugo 23m"), $"idle line: the next watched timer at its end when there is room ({dungeon})");
        string live = "";
        try { live = MainWindow.IdleLine(w.IdleFactsNow("Western Cantas Valley", DateTime.UtcNow)); } catch (Exception ex) { live = "threw " + ex.Message; }
        check(live.Length > 0 && !live.StartsWith("threw", StringComparison.Ordinal) && !live.Contains("Waiting for", StringComparison.Ordinal), $"idle line from the window's own stores: filled ({live})");

        // every look, every place, three languages: the line shown whole, never the old words, nothing cut
        var culture = CultureInfo.CurrentCulture; var ui = CultureInfo.CurrentUICulture;
        var defCulture = CultureInfo.DefaultThreadCurrentCulture; var defUi = CultureInfo.DefaultThreadCurrentUICulture;
        var lines = new List<(string Code, string Kind, string Line, string Short)>();
        try
        {
            foreach (var code in new[] { "en", "ko", "zh-TW" })
            {
                L.Init(code);
                foreach (var kind in new[] { "dungeon", "open", "history", "abyss", "fresh" }) lines.Add((code, kind, Line(kind), MainWindow.IdleShortLine(MainWindow.IdleSampleFacts(kind))));
            }
        }
        finally
        {
            L.Init("en");
            CultureInfo.CurrentCulture = culture; CultureInfo.CurrentUICulture = ui;
            CultureInfo.DefaultThreadCurrentCulture = defCulture; CultureInfo.DefaultThreadCurrentUICulture = defUi;
        }
        var baseV = w.HudValues.ToDictionary(kv => kv.Key, kv => kv.Value);
        var idleV = new Dictionary<string, string>(baseV) { ["run"] = "", ["time"] = "0:00", ["rate"] = "", ["kinah"] = "", ["loot"] = "", ["total"] = "0", ["feedText"] = "", ["feedValue"] = "", ["feedRate"] = "", ["chase"] = "", ["toBest"] = "", ["paused"] = "", ["notice"] = "", ["flash"] = "", ["flag"] = "", ["show"] = "", ["todayRate"] = "", ["todayTotal"] = "" };
        foreach (var k in idleV.Keys.Where(k => k.StartsWith("pick") || k.StartsWith("top") || k.StartsWith("rare")).ToList()) idleV[k] = "";
        foreach (var k in MainWindow.AbyssKeys) idleV[k] = "";
        double w0 = HudStackLook.FightWidth; HudStackLook.FightWidth = 350;
        var missing = new List<string>(); var old = new List<string>(); var cut = new List<string>();
        try
        {
            foreach (var look in HudLooks.All.Select(l => l.Key))
                foreach (var (code, kind, full, brief) in lines)
                {
                    var v = new Dictionary<string, string>(idleV) { ["idle"] = full, ["idleShort"] = brief };
                    var line = look == HudLooks.Strip ? brief : full;   // Badge's feed line takes the short one
                    if (kind == "abyss") { v["abyss"] = "1"; v["zone"] = KinahLedger.Ocr.ZoneLocator.Abyss; }
                    var h = new HudWindow(null, look, still: true) { Left = -12000, Top = -12000, ShowActivated = false };
                    h.SetScale(1.1);
                    h.Update(v);
                    h.Show(); DoEvents();
                    var texts = VisibleTexts(h);
                    var all = Norm(string.Join("", texts));
                    if (!all.Contains(Norm(line), StringComparison.Ordinal)) missing.Add($"{look} {code} {kind}");
                    if (texts.Any(t => t.Contains("Waiting for the first count", StringComparison.OrdinalIgnoreCase) || t.Contains("Waiting for Abyss Points", StringComparison.OrdinalIgnoreCase))) old.Add($"{look} {code} {kind}");
                    var head = Norm(line)[..Math.Min(6, Norm(line).Length)];
                    foreach (var t in MeterShare.Trimmed((DependencyObject)h.Content).Where(t => Norm(t.TrimEnd('…', '.')).StartsWith(head, StringComparison.Ordinal) || Norm(line).StartsWith(Norm(t.TrimEnd('…', '.')), StringComparison.Ordinal)))
                        cut.Add($"{look} {code} {kind}: \"{t}\"");
                    h.Close();
                }
        }
        finally { HudStackLook.FightWidth = w0; }
        // with farming done today, a themed look draws its whole card on today's figures, named "today" (Steven, 2026-11-21)
        {
            var tv = new Dictionary<string, string>(idleV) { ["idle"] = lines[0].Line, ["idleShort"] = lines[0].Short, ["todayRate"] = "568K", ["todayTotal"] = "1.27M" };
            var th = new HudWindow(null, HudLooks.Default, still: true) { Left = -12000, Top = -12000, ShowActivated = false };
            th.SetScale(1.1); th.Update(tv); th.Show(); DoEvents();
            var tt = VisibleTexts(th);
            var tall = string.Join(" ", tt);
            th.Close();
            check(tall.Contains("568K") && tall.Contains("1.27M") && tall.Contains("today", StringComparison.OrdinalIgnoreCase) && !tall.Contains(lines[0].Line),
                $"before the first count with farming done today, the Buddy ({HudLooks.Default}) shows its whole card on today's figures, not the idle line ({tall})");
        }
        check(missing.Count == 0, "every look shows the idle line whole where the hour waits, in English, Korean and Chinese" + (missing.Count > 0 ? ": " + string.Join("; ", missing.Take(16)) : ""));
        check(old.Count == 0, "no look says \"Waiting for the first count\" or \"Waiting for Abyss Points\" while there is an idle line" + (old.Count > 0 ? ": " + string.Join("; ", old.Take(16)) : ""));
        check(cut.Count == 0, "the idle line is never cut off, in any look or language, at width 350 and Scale 1.1" + (cut.Count > 0 ? ": " + string.Join("; ", cut.Take(80)) : ""));
    }
}
