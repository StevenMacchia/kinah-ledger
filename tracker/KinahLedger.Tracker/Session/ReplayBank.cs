using System.Drawing;
using System.Drawing.Imaging;
using System.Text.Json;
using System.Text.Json.Nodes;
using KinahLedger.Data;
using KinahLedger.Ocr;

namespace KinahLedger.Session;

/// <summary>
/// The replay test bank (2026-10-23): saved crops of the loot list and the crafting panel, each folder one case with
/// what the tracker must count from them, run through this version's own readers before every release (the release
/// workflow runs `replay-bank tracker/tests/replays`; a case that counts differently fails it). Cases come from players'
/// miscount reports (Options › Sharing › Send report pictures; tools/replays/pull.js brings them here as pending) and
/// from `replay-bank --seed`, which draws a few by hand-measured geometry so the bank is never empty.
///
/// A case folder holds case.json and its frames:
///   { "name", "source", "game": {"w","h"}, "pending": false, "needs_ocr": true|false,
///     "frames": [{"t": ISO time, "k": "loot"|"craft", "f": "frames/x.png", "x", "y"}],   x, y: the crop's place in the game window
///     "expect": {"craft_ok", "craft_fail", "items": {"Name": n}, "kinah": n} }
/// Only what "expect" names is checked. A pending case (a fresh report nobody has checked yet) runs and prints, never
/// fails. `replay-bank <dir> --record <case>` writes what this version counts into that case's "expect" and clears
/// pending: run it only once the count it prints is known to be right.
/// </summary>
public static class ReplayBank
{
    public sealed record Result(string Case, string Status, string Detail);

    /// <summary>Whether Windows' text recognition can run here (a build server may lack the language).</summary>
    public static bool OcrAvailable()
    {
        try { return Windows.Media.Ocr.OcrEngine.TryCreateFromUserProfileLanguages() != null || Windows.Media.Ocr.OcrEngine.AvailableRecognizerLanguages.Count > 0; }
        catch (Exception) { return false; }
    }

    /// <summary>Runs every case under <paramref name="dir"/>; with <paramref name="record"/>, writes that case's expectations instead.</summary>
    public static List<Result> Run(string dir, Settings settings, string? record = null)
    {
        var results = new List<Result>();
        bool ocr = OcrAvailable();
        var items = ItemDictionary.LoadOrEmpty(settings.ItemsFile);
        foreach (var caseDir in Directory.GetDirectories(dir).OrderBy(d => d, StringComparer.Ordinal))
        {
            var file = Path.Combine(caseDir, "case.json");
            if (!File.Exists(file)) continue;
            var name = Path.GetFileName(caseDir);
            if (record != null && !name.Equals(record, StringComparison.OrdinalIgnoreCase)) continue;
            try
            {
                var node = JsonNode.Parse(File.ReadAllText(file))!.AsObject();
                bool needsOcr = node["needs_ocr"]?.GetValue<bool>() ?? (node["frames"] is JsonArray fa && fa.Any(f => f!["k"]!.GetValue<string>() == "loot"));
                if (needsOcr && !ocr) { results.Add(new Result(name, "skipped", "no text recognition on this machine")); continue; }
                var got = RunCase(caseDir, node, settings, items);
                if (record != null)
                {
                    node["expect"] = got.DeepClone();
                    node["pending"] = false;
                    node["recorded_with"] = Upload.RunPayload.AppVersion;
                    File.WriteAllText(file, node.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) + "\n");
                    results.Add(new Result(name, "recorded", got.ToJsonString()));
                    continue;
                }
                var expect = node["expect"] as JsonObject ?? new JsonObject();
                var diffs = Compare(expect, got);
                bool pending = node["pending"]?.GetValue<bool>() ?? false;
                results.Add(diffs.Count == 0 ? new Result(name, "pass", got.ToJsonString())
                          : new Result(name, pending ? "pending" : "FAIL", string.Join("; ", diffs) + " | counted " + got.ToJsonString()));
            }
            catch (Exception ex) { results.Add(new Result(name, "FAIL", "the case didn't run: " + ex.Message)); }
        }
        return results;
    }

    /// <summary>What expect names, against what was counted: one line per difference.</summary>
    public static List<string> Compare(JsonObject expect, JsonObject got)
    {
        var d = new List<string>();
        static long N(JsonNode? n) => n == null ? 0 : long.TryParse(n.ToJsonString(), out var v) ? v : 0;
        foreach (var k in new[] { "craft_ok", "craft_fail", "kinah" })
            if (expect[k] is { } e && N(e) != N(got[k])) d.Add($"{k}: expected {e}, counted {got[k]?.ToString() ?? "0"}");
        if (expect["runs"] is JsonObject er)
        {
            var gr = got["runs"] as JsonObject ?? new JsonObject();
            foreach (var (n, v) in er) if (N(gr[n]) != N(v)) d.Add($"run {n}: expected {v} item(s), counted {gr[n]?.ToString() ?? "0"}");
        }
        if (expect["items"] is JsonObject ei)
        {
            var gi = got["items"] as JsonObject ?? new JsonObject();
            foreach (var (n, v) in ei) if (N(gi[n]) != N(v)) d.Add($"{n}: expected {v}, counted {gi[n]?.ToString() ?? "0"}");
            foreach (var (n, v) in gi) if (ei[n] == null) d.Add($"{n}: not expected, counted {v}");
        }
        return d;
    }

    /// <summary>
    /// One case through the readers as a live session runs them: the crafting panel's strip measured and raced, the loot
    /// list read (text, then icons) and counted, rare drops read a second time. Returns what was counted.
    /// </summary>
    static JsonObject RunCase(string caseDir, JsonObject node, Settings settings, ItemDictionary items)
    {
        int w = node["game"]?["w"]?.GetValue<int>() ?? 1920, h = node["game"]?["h"]?.GetValue<int>() ?? 1080;
        // the game window off the screen's origin, as on a real desktop, so a place mixed up with a size shows
        var game = new Rectangle(100, 50, w, h);
        // each case keeps its runs in a folder of its own, so the runs log (instances.jsonl) starts empty
        var keptDir = CrashLog.DirOverride;
        CrashLog.DirOverride = Path.Combine(Path.GetTempPath(), "kl-bank-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(CrashLog.DirOverride);
        try { return RunCaseIn(caseDir, node, settings, items, game); }
        finally { var made = CrashLog.DirOverride; CrashLog.DirOverride = keptDir; try { Directory.Delete(made, true); } catch (Exception) { } }
    }

    static JsonObject RunCaseIn(string caseDir, JsonObject node, Settings settings, ItemDictionary items, Rectangle game)
    {
        var session = new TrackerSession(null, settings, items, "Replay");
        var panel = new CraftPanelTracker();
        int ok = 0, fail = 0;
        DateTime last = DateTime.UtcNow;
        // a text case: the reads as trace.log keeps them (chat lines, loot list rows), and the zone as the minimap read it
        if (node["trace"]?.GetValue<string>() is { } traceFile)
        {
            foreach (var raw in File.ReadLines(Path.Combine(caseDir, traceFile)))
            {
                if (raw.Trim().Length == 0) continue;
                using var d = JsonDocument.Parse(raw);
                var r = d.RootElement;
                var t = DateTime.Parse(r.GetProperty("t").GetString()!, null, System.Globalization.DateTimeStyles.RoundtripKind).ToUniversalTime();
                var ls = r.GetProperty("l").EnumerateArray().Select(x => x.GetString() ?? "").ToList();
                switch (r.GetProperty("k").GetString())
                {
                    case "chat": session.QuestCounter(ls); session.ProcessChat(ls, t); break;
                    case "list": session.ProcessList(ls.Select((x, i) => (100 + i * 30, System.Text.RegularExpressions.Regex.Replace(x, @"^(\d+) Kina$", "Kina x$1"))).ToList(), t); break;
                    case "zone": if (ls.FirstOrDefault() is { Length: > 0 } z) { session.Engine.SetZone(z); session.Instances.Zone(z, t); } break;
                }
                last = t;
            }
            session.FlushVotes(last);
        }
        foreach (var f in node["frames"] as JsonArray ?? new JsonArray())
        {
            var t = DateTime.Parse(f!["t"]!.GetValue<string>(), null, System.Globalization.DateTimeStyles.RoundtripKind).ToUniversalTime();
            var at = new Point(game.X + f["x"]!.GetValue<int>(), game.Y + f["y"]!.GetValue<int>());
            using var bmp = Task.Run(() => ScreenReader.LoadAsync(Path.Combine(caseDir, f["f"]!.GetValue<string>()))).GetAwaiter().GetResult();
            if (f["k"]!.GetValue<string>() == "craft")
            {
                var s = CraftPanel.Measure(bmp, new Rectangle(at, bmp.Size), game);
                if (panel.Feed(t, s) is { } o) { if (o.Ok) ok++; else fail++; }
            }
            else Task.Run(() => session.ReplayListFrameAsync(bmp, at, t)).GetAwaiter().GetResult();
            last = t;
        }
        // rows still waiting for a chat line that never comes are settled the way a live run settles them: later reads
        foreach (var dt in new[] { 2.0, 5.0, 9.0 }) session.ProcessList(new List<(int, string)>(), last.AddSeconds(dt));
        session.CommitHeld();
        var e = session.Engine;
        var outItems = new JsonObject();
        foreach (var (id, n) in e.Items.OrderBy(kv => items.NameOf(kv.Key), StringComparer.Ordinal)) outItems[items.NameOf(id)] = n;
        var res = new JsonObject { ["craft_ok"] = ok, ["craft_fail"] = fail, ["items"] = outItems };
        if (e.Totals().Kinah > 0) res["kinah"] = e.Totals().Kinah;
        // what went to expedition and event runs rather than farming: items per run
        var runs = new JsonObject();
        foreach (var run in session.Instances.Snapshot()) runs[run.Zone] = run.Items.Values.Sum() + run.BoundItems.Values.Sum();
        if (runs.Count > 0) res["runs"] = runs;
        return res;
    }

    // ---- seeding ----

    /// <summary>
    /// Writes the generated cases: crafting panel races drawn from the panel's measured geometry (5120×2160, the screen it
    /// was measured on, and 1920×1080), and a loot list row drawn as text, for the reader's whole path.
    /// </summary>
    public static List<string> Seed(string dir)
    {
        var made = new List<string>();
        var t0 = new DateTime(2026, 10, 23, 12, 0, 0, DateTimeKind.Utc);
        // three races: success, fail, success; then the panel goes
        var races = new (double S, double F)[] { (0.05, 0.05), (0.5, 0.3), (1.0, 0.6), (0.05, 0.05), (0.4, 0.7), (0.6, 1.0), (0.05, 0.05), (0.8, 0.5), (1.0, 0.9) };
        foreach (var (w, h) in new[] { (5120, 2160), (1920, 1080) })
        {
            var name = $"seed-craft-{w}x{h}";
            var cd = Path.Combine(dir, name); Directory.CreateDirectory(Path.Combine(cd, "frames"));
            var game = new Rectangle(0, 0, w, h);
            var region = CraftPanel.Region(game);
            var frames = new JsonArray();
            for (int i = 0; i <= races.Length; i++)
            {
                using var bmp = DrawPanel(region, game, i < races.Length ? races[i] : null);
                var f = $"frames/{i:00}.png"; bmp.Save(Path.Combine(cd, f), ImageFormat.Png);
                frames.Add(new JsonObject { ["t"] = t0.AddSeconds(i * 1.5).ToString("O"), ["k"] = "craft", ["f"] = f, ["x"] = region.X, ["y"] = region.Y });
            }
            Write(cd, new JsonObject
            {
                ["name"] = $"crafting panel, three races on {w}×{h}: success, fail, success",
                ["source"] = "generated by replay-bank --seed from CraftPanel's measured geometry",
                ["game"] = new JsonObject { ["w"] = w, ["h"] = h }, ["pending"] = false, ["needs_ocr"] = false,
                ["frames"] = frames, ["expect"] = new JsonObject { ["craft_ok"] = 2, ["craft_fail"] = 1 },
            });
            made.Add(name);
        }
        {
            var name = "seed-loot-row";
            var cd = Path.Combine(dir, name); Directory.CreateDirectory(Path.Combine(cd, "frames"));
            var game = new Rectangle(0, 0, 2560, 1440);
            var region = LootPopup.Region(game);
            var frames = new JsonArray();
            // the row shows for two seconds, read four times, then the list is empty
            for (int i = 0; i < 6; i++)
            {
                using var bmp = DrawLoot(region, game, i < 4 ? new[] { "Orichalcum Ore x2" } : Array.Empty<string>());
                var f = $"frames/{i:00}.png"; bmp.Save(Path.Combine(cd, f), ImageFormat.Png);
                frames.Add(new JsonObject { ["t"] = t0.AddSeconds(i * 0.5).ToString("O"), ["k"] = "loot", ["f"] = f, ["x"] = region.X, ["y"] = region.Y });
            }
            Write(cd, new JsonObject
            {
                ["name"] = "loot list, one row the chat never showed: counted from the list",
                ["source"] = "generated by replay-bank --seed",
                ["game"] = new JsonObject { ["w"] = game.Width, ["h"] = game.Height }, ["pending"] = false, ["needs_ocr"] = true,
                ["frames"] = frames, ["expect"] = new JsonObject { ["items"] = new JsonObject { ["Orichalcum Ore"] = 2 } },
            });
            made.Add(name);
        }
        return made;
    }

    /// <summary>
    /// Whole-window pictures of the same three races (5120×2160), named and dated in order, for `craft-test --dir <dir>
    /// --glob *.png` on a PC without saved crafting pictures. Not a bank case.
    /// </summary>
    public static int SeedFullFrames(string dir)
    {
        Directory.CreateDirectory(dir);
        var game = new Rectangle(0, 0, 5120, 2160);
        var races = new (double S, double F)?[] { (0.05, 0.05), (0.5, 0.3), (1.0, 0.6), (0.05, 0.05), (0.4, 0.7), (0.6, 1.0), (0.05, 0.05), (0.8, 0.5), (1.0, 0.9), null };
        var t0 = DateTime.Now.AddMinutes(-5);
        for (int i = 0; i < races.Length; i++)
        {
            using var bmp = DrawPanel(game, game, races[i]);
            var f = Path.Combine(dir, $"{i:00}-crafting.png");
            bmp.Save(f, ImageFormat.Png);
            File.SetLastWriteTime(f, t0.AddSeconds(i * 1.5));
        }
        return races.Length;
    }

    static void Write(string caseDir, JsonObject node) =>
        File.WriteAllText(Path.Combine(caseDir, "case.json"), node.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) + "\n");

    /// <summary>
    /// The panel's strip as the game draws it, for <paramref name="bars"/> (null: no panel): dark see-through backing,
    /// the white Success and Fail labels at their measured cells, the blue and orange bars filled that far.
    /// </summary>
    internal static Bitmap DrawPanel(Rectangle region, Rectangle game, (double S, double F)? bars)
    {
        var bmp = new Bitmap(region.Width, region.Height, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(bmp)) g.Clear(Color.FromArgb(255, 34, 40, 34));
        if (bars is not { } b) return bmp;
        int h = game.Height, cx = game.X + game.Width / 2;
        double k = h / 2160.0;
        void Px(int gx, int gy, Color c) { int x = gx - region.X, y = gy - region.Y; if (x >= 0 && y >= 0 && x < bmp.Width && y < bmp.Height) bmp.SetPixel(x, y, c); }
        int x0 = cx + (int)((2227 - 2560) / 2160.0 * h), x1 = cx + (int)((2998 - 2560) / 2160.0 * h);
        int ys = game.Y + (int)(1910 / 2160.0 * h), yf = game.Y + (int)(1956 / 2160.0 * h);
        void Bar(int y, double frac, Color c) { int end = x0 + (int)Math.Round(frac * (x1 - x0)); for (int x = x0; x <= end; x++) for (int dy = -2; dy <= 2; dy++) Px(x, y + dy, c); }
        Bar(ys, b.S, Color.FromArgb(40, 120, 230));
        Bar(yf, b.F, Color.FromArgb(232, 140, 40));
        // the labels' cells: dark ones first, white ones over them (on a small screen two cells can share a pixel, and the
        // game's letters are drawn over the panel, not under it)
        foreach (bool white in new[] { false, true })
            foreach (var (X0, X1, Y0, Y1, Mask) in CraftPanel.LabelShapes)
                for (int j = 0; j < 10; j++)
                    for (int i = 0; i < 48; i++)
                    {
                        if ((Mask[j * 48 + i] == '1') != white) continue;
                        int x = cx + (int)Math.Round((X0 - 2560 + (X1 - X0) * (i + 0.5) / 48) * k);
                        int y = game.Y + (int)Math.Round((Y0 + (Y1 - Y0) * (j + 0.5) / 10) * k);
                        Px(x, y, white ? Color.FromArgb(245, 245, 245) : Color.FromArgb(34, 40, 34));
                    }
        return bmp;
    }

    /// <summary>The loot list's band with <paramref name="rows"/> written as the game writes them (white text on the world's dark).</summary>
    internal static Bitmap DrawLoot(Rectangle region, Rectangle game, string[] rows)
    {
        var bmp = new Bitmap(region.Width, region.Height, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(bmp);
        g.Clear(Color.FromArgb(255, 28, 34, 30));
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
        float px = game.Height * 0.019f;
        using var font = new Font("Segoe UI", px, FontStyle.Regular, GraphicsUnit.Pixel);
        for (int i = 0; i < rows.Length; i++)
        {
            float y = region.Height * 0.35f + i * px * 1.8f, x = region.Width * 0.25f;
            using (var icon = new SolidBrush(Color.FromArgb(120, 110, 90))) g.FillRectangle(icon, x - px * 2.2f, y, px * 1.6f, px * 1.6f);
            g.DrawString(rows[i], font, Brushes.White, x, y);
        }
        return bmp;
    }
}
