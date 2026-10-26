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
///     "expect": {"craft_ok", "craft_fail", "items": {"Name": n}, "kinah": n, "list_unsure": n} }
/// A text case ("trace": "trace.jsonl") holds the reads instead: {"t", "k": "chat"|"list"|"zone"|"gather", "l": [...]}.
/// A "list" read may carry, beside its rows, "y" (each row's top in the window), "c" (its text's contrast, 0 to 1, as
/// LootRows measures it) and "h" (its icon's hash, hex); without them rows sit 30 px apart and contrast is unknown. It
/// may also carry "b" (each row's background) and "s" (the spread where its icon sits), as trace.log writes them since
/// 2026-10-24, for telling rows from nameplates and world text (LootNotRow); a "plate" read names a nameplate or target
/// name on screen then (from reads.log's "fighting:" and "target bar:" lines; trace.log doesn't keep them).
/// Since 2026-10-25 a "list" read may also carry "r" (a row's text as read before its icon looked) and "i" (the icon's
/// scores, "id:distance,…"), for the rows the icon had a verdict on: those rows are settled again (PopupIcons.Replay).
/// A "gather" read is the gathering bar finishing a race: "ok", "fail", "upgrade ok" or "upgrade fail". A "cover" read
/// is a pass that skips the list (a full-screen window over it): the rows up are set aside, as live (LootRows.Reset).
/// A "list" read may carry "p", its band's pixel fingerprint: the same as the last recognized read's, under 2 s after it
/// and with no change on screen since, it stands on that read's lines, as the live read does (ocr.list_skip_same).
/// A "panel" read is one pass of the crafting panel's locator ("l" its text read's lines, [] when not read; "up": false
/// without the panel; "raised": true for the gathering panel), and a "bar" read its bars finishing a race ("ok", "fail",
/// "combo ok", "combo fail"), routed by the session as live: a craft while crafting, else the gathering bar's race.
/// Only what "expect" names is checked. A pending case (a fresh report nobody has checked yet) runs and prints, never
/// fails. `replay-bank <dir> --record <case>` writes what this version counts into that case's "expect" and clears
/// pending: run it only once the count it prints is known to be right.
/// </summary>
public static class ReplayBank
{
    public sealed record Result(string Case, string Status, string Detail);

    /// <summary>
    /// What the last <see cref="Run"/> measured of the chat's vote (ChatVoter): for every loot line counted, the seconds
    /// from the read it came in to the read it counted on, and how many counted on their first read because the loot list
    /// showed the same pickup (TrackerSession.FastChat).
    /// </summary>
    public static List<double> ChatLatencies { get; } = new();
    public static int ChatFast { get; private set; }

    /// <summary>Whether Windows' text recognition can run here (a build server may lack the language).</summary>
    public static bool OcrAvailable()
    {
        try { return Windows.Media.Ocr.OcrEngine.TryCreateFromUserProfileLanguages() != null || Windows.Media.Ocr.OcrEngine.AvailableRecognizerLanguages.Count > 0; }
        catch (Exception) { return false; }
    }

    /// <summary>Runs every case under <paramref name="dir"/>; with <paramref name="record"/>, writes that case's expectations instead.</summary>
    public static List<Result> Run(string dir, Settings settings, string? record = null, bool fastChat = true)
    {
        var results = new List<Result>();
        ChatLatencies.Clear(); ChatFast = 0;
        _fastChat = fastChat;
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
                          : new Result(name, pending ? "pending" : "FAIL", string.Join("; ", diffs) + " | counted " + got.ToJsonString() + _dump));
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
        foreach (var k in new[] { "craft_ok", "craft_fail", "kinah", "pets", "kills", "list_unsure", "list_notrow", "list_same", "runs_kept", "runs_from_picture", "run_kinah", "run_gear", "run_materials",
                                    "runs_partly_read", "runs_sent", "run_not_read", "run_pieces", "run_unknown_gear", "run_unknown_items", "run_abyss_points", "run_kinah_set_aside" })
            if (expect[k] is { } e && N(e) != N(got[k])) d.Add($"{k}: expected {e}, counted {got[k]?.ToString() ?? "0"}");
        // the chat's alignment (2026-10-25): reads that couldn't be lined up, and lines the settle took that came after them
        foreach (var k in new[] { "chat_unaligned", "chat_settled_late" })
            if (expect[k] is { } e && N(e) != N(got[k])) d.Add($"{k}: expected {e}, counted {got[k]?.ToString() ?? "0"}");
        // a run's items by name: each one expect names must match (the others aren't checked)
        if (expect["run_items"] is JsonObject eri)
        {
            var gri = got["run_items"] as JsonObject ?? new JsonObject();
            foreach (var (n, v) in eri) if (N(gri[n]) != N(v)) d.Add($"run item {n}: expected {v}, counted {gri[n]?.ToString() ?? "0"}");
        }
        // what the crafts made, by item (a text case's "bar" reads): each one expect names must match
        if (expect["craft_made"] is JsonObject ecm)
        {
            var gcm = got["craft_made"] as JsonObject ?? new JsonObject();
            foreach (var (n, v) in ecm) if (N(gcm[n]) != N(v)) d.Add($"craft made {n}: expected {v}, counted {gcm[n]?.ToString() ?? "0"}");
        }
        // ...and the chat's items a run set aside (its rewards are a picture's to count), the same way
        if (expect["run_chat_items"] is JsonObject erci)
        {
            var grci = got["run_chat_items"] as JsonObject ?? new JsonObject();
            foreach (var (n, v) in erci) if (N(grci[n]) != N(v)) d.Add($"run chat item {n}: expected {v}, set aside {grci[n]?.ToString() ?? "0"}");
        }
        if (expect["run_start"] is { } rstart && rstart.ToString() != (got["run_start"]?.ToString() ?? "")) d.Add($"run_start: expected {rstart}, counted {got["run_start"]?.ToString() ?? "none"}");
        if (expect["run_starts"] is { } rstarts && rstarts.ToString() != (got["run_starts"]?.ToString() ?? "")) d.Add($"run_starts: expected {rstarts}, counted {got["run_starts"]?.ToString() ?? "none"}");
        if (expect["runs"] is JsonObject er)
        {
            var gr = got["runs"] as JsonObject ?? new JsonObject();
            foreach (var (n, v) in er) if (N(gr[n]) != N(v)) d.Add($"run {n}: expected {v} item(s), counted {gr[n]?.ToString() ?? "0"}");
        }
        // a reward table, slot by slot: the count always; the name when the truth knows it; "?" (not read) when the item list
        // lacks it. A slot whose name the truth doesn't know may be named surely or left not read, never something else
        if (expect["table"] is JsonArray et)
        {
            var gt = got["table"] as JsonArray ?? new JsonArray();
            if (gt.Count != et.Count) d.Add($"table: expected {et.Count} slots, read {gt.Count}");
            int right = 0;
            for (int i = 0; i < Math.Min(gt.Count, et.Count); i++)
            {
                var e = et[i]!.AsObject(); var g = gt[i]!.AsObject();
                long en = N(e["n"]), gn = N(g["n"]);
                string gname = g["name"]?.GetValue<string>() ?? "?";
                string? ename = e["name"]?.GetValue<string>();
                bool unlisted = e["unlisted"]?.GetValue<bool>() ?? false;
                var miss = new List<string>();
                if (en != gn) miss.Add($"count {gn}, expected {en}");
                if (unlisted && gname != "?") miss.Add($"named {gname}, but the item list lacks it");
                else if (ename != null && !unlisted && !gname.Contains(ename, StringComparison.OrdinalIgnoreCase)) miss.Add(gname == "?" ? $"not read, expected {ename}" : $"named {gname}, expected {ename}");
                if (miss.Count == 0) right++;
                else d.Add($"slot {i + 1} ({g["grade"]}, {g["icon"]}): {string.Join("; ", miss)}");
            }
            d.Insert(0, $"table score: {right} of {et.Count} slots right");
            if (right == et.Count && gt.Count == et.Count) d.RemoveAt(0);
        }
        // an event's reward cards, card by card: received or not; a received card's list slot by slot like a table (a
        // "hidden" slot is one the screen only ever showed cut by the card's window: it must stay not read), and "more" when
        // the list may run on past what the screen showed
        if (expect["cards"] is JsonArray ec)
        {
            var gc = got["cards"] as JsonArray ?? new JsonArray();
            if (gc.Count != ec.Count) d.Add($"cards: expected {ec.Count}, read {gc.Count}");
            int right = 0, total = 0;
            for (int ci = 0; ci < Math.Min(gc.Count, ec.Count); ci++)
            {
                var e = ec[ci]!.AsObject(); var g = gc[ci]!.AsObject();
                bool erc = e["received"]?.GetValue<bool>() ?? false, grc = g["received"]?.GetValue<bool>() ?? false;
                if (erc != grc) { d.Add($"card {ci + 1}: expected {(erc ? "received" : "not received")}, read {(grc ? "received" : "not received")}"); continue; }
                if (!erc) continue;
                if (e["more"] is { } em && em.GetValue<bool>() != (g["more"]?.GetValue<bool>() ?? false)) d.Add($"card {ci + 1}: expected its list {(em.GetValue<bool>() ? "to run on past the screen" : "seen to its end")}");
                var es = e["slots"] as JsonArray ?? new JsonArray(); var gs = g["slots"] as JsonArray ?? new JsonArray();
                if (es.Count != gs.Count) d.Add($"card {ci + 1}: expected {es.Count} slots, read {gs.Count}");
                for (int i = 0; i < Math.Min(es.Count, gs.Count); i++)
                {
                    total++;
                    var x = es[i]!.AsObject(); var y = gs[i]!.AsObject();
                    string gname = y["name"]?.GetValue<string>() ?? "?";
                    string? ename = x["name"]?.GetValue<string>();
                    bool hidden = x["hidden"]?.GetValue<bool>() ?? false, unlisted = x["unlisted"]?.GetValue<bool>() ?? false;
                    var miss = new List<string>();
                    if (hidden) { if (gname != "?" || N(y["n"]) > 0) miss.Add($"read {gname} x{y["n"]}, but the screen only showed it cut"); }
                    else
                    {
                        if (N(x["n"]) != N(y["n"])) miss.Add($"count {y["n"]}, expected {x["n"]}");
                        if (unlisted && gname != "?") miss.Add($"named {gname}, but the item list lacks it");
                        else if (ename != null && !unlisted && !gname.Equals(ename, StringComparison.OrdinalIgnoreCase)) miss.Add(gname == "?" ? $"not read, expected {ename}" : $"named {gname}, expected {ename}");
                    }
                    if (miss.Count == 0) right++;
                    else d.Add($"card {ci + 1} slot {i + 1} ({y["grade"]}, {y["icon"]}): {string.Join("; ", miss)}");
                }
            }
            if (right < total || d.Any(x => x.StartsWith("card"))) d.Insert(0, $"cards score: {right} of {total} slots right");
        }
        // gathering attempts, "worked/failed" per skill and per node: every one named must match, and no other may be counted
        foreach (var key in new[] { "attempts", "nodes" })
            if (expect[key] is JsonObject ea)
            {
                var ga = got[key] as JsonObject ?? new JsonObject();
                foreach (var (n, v) in ea) if ((ga[n]?.ToString() ?? "0/0") != v?.ToString()) d.Add($"{key} {n}: expected {v}, counted {ga[n]?.ToString() ?? "none"}");
                foreach (var (n, v) in ga) if (ea[n] == null) d.Add($"{key} {n}: not expected, counted {v}");
            }
        // the loot list's rows by name: each one expect names must match (0: never shown); the others aren't checked
        if (expect["list_rows"] is JsonObject elr)
        {
            var glr = got["list_rows"] as JsonObject ?? new JsonObject();
            foreach (var (n, v) in elr) if (N(glr[n]) != N(v)) d.Add($"list row {n}: expected {v}, shown {glr[n]?.ToString() ?? "0"}");
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
        CrashLog.DirOverride = TestTemp.Path("kl-bank-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(CrashLog.DirOverride);
        _dump = "";
        try { return RunCaseIn(caseDir, node, settings, items, game); }
        // KL_BANK_KEEP set: each case's reads.log is kept and its folder printed, for reading what a case did
        finally { var made = CrashLog.DirOverride; CrashLog.DirOverride = keptDir;
            // "dump": true in a case adds its reads.log to a failed result (for reading on a build server what a case did)
            if (node["dump"]?.GetValue<bool>() == true && made != null) try { _dump = "\n  | " + string.Join("\n  | ", File.ReadLines(Path.Combine(made, "reads.log"))); } catch (Exception) { }
            if (Environment.GetEnvironmentVariable("KL_BANK_KEEP") is { Length: > 0 }) Console.WriteLine($"  ({Path.GetFileName(caseDir)}: log kept in {made})"); else try { Directory.Delete(made, true); } catch (Exception) { } }
    }

    static bool _fastChat = true;
    static string _dump = "";

    /// <summary>
    /// One picture of an event's reward screen into a session, as RewardGridDue reads it live: the title band read, the
    /// screen marked up, and once "sent to your cube" shows, its cards read (EventCardsRead). The bank's cases and the
    /// recovery of a run whose rewards were not read (EventShotRecovery, `replay-event-shots`) both feed pictures here; the
    /// cards are filed with <see cref="TrackerSession.FlushEventCards"/> after the last one.
    /// </summary>
    internal static async Task FeedCardsAsync(TrackerSession session, Bitmap bmp, DateTime t, Action<string>? say)
    {
        var band = new Rectangle(bmp.Width / 4, 0, bmp.Width / 2, bmp.Height / 3);
        using var bandBmp = bmp.Clone(band, bmp.PixelFormat);
        var lines = await ScreenReader.ReadBoxesAsync(bandBmp, band.Location, 1);
        bool up = lines.Any(l => TrackerSession.IsEventCardsUp(l.Text));
        say?.Invoke($"  cards band: " + string.Join(" | ", lines.Select(l => l.Text)) + (up ? "" : " -- no \"sent to your cube\" line"));
        session.Instances.EventRewardScreen(t);
        if (up) await session.EventCardsRead(bmp, t);
    }

    static JsonObject RunCaseIn(string caseDir, JsonObject node, Settings settings, ItemDictionary items, Rectangle game)
    {
        // "server": the player's server label, for gear twins settled by the faction (Data/GearFactions.cs)
        var session = new TrackerSession(null, settings, items, "Replay") { FastChat = _fastChat, Server = node["server"]?.GetValue<string>() ?? "", ListRows = new() };
        var panel = new CraftPanelTracker();
        int ok = 0, fail = 0;
        JsonArray? table = null;
        bool cards = false; JsonArray? res0Cards = null;
        DateTime last = DateTime.UtcNow;
        // a text case: the reads as trace.log keeps them (chat lines, loot list rows), and the zone as the minimap read it;
        // "after" holds the reads that come once an event's pictures are filed (the chat behind its reward screen)
        void FeedTrace(string file)
        {
            foreach (var raw in File.ReadLines(Path.Combine(caseDir, file)))
            {
                if (raw.Trim().Length == 0) continue;
                using var d = JsonDocument.Parse(raw);
                var r = d.RootElement;
                var t = DateTime.Parse(r.GetProperty("t").GetString()!, null, System.Globalization.DateTimeStyles.RoundtripKind).ToUniversalTime();
                var ls = r.GetProperty("l").EnumerateArray().Select(x => x.GetString() ?? "").ToList();
                switch (r.GetProperty("k").GetString())
                {
                    // through the scene gate as a live pass is: a text case has no frame, so the scene never sees one
                    // and every read runs, exactly as before the scene existed (SceneState.HasFrame)
                    case "chat": session.QuestCounter(ls, t); session.ChatGated(ls, t, session.Verdict("chat")); break;
                    case "list":
                    {
                        // where each row sat, how settled its text was and its icon, when the trace kept them (LootRows)
                        var ys = r.TryGetProperty("y", out var ye) ? ye.EnumerateArray().Select(v => v.GetInt32()).ToList() : null;
                        var cs = r.TryGetProperty("c", out var ce) ? ce.EnumerateArray().Select(v => v.GetDouble()).ToList() : null;
                        var hs = r.TryGetProperty("h", out var he) ? he.EnumerateArray().Select(v => Convert.ToUInt64(v.GetString() ?? "0", 16)).ToList() : null;
                        var bs = r.TryGetProperty("b", out var be) ? be.EnumerateArray().Select(v => v.GetDouble()).ToList() : null;
                        var ss = r.TryGetProperty("s", out var se) ? se.EnumerateArray().Select(v => v.GetDouble()).ToList() : null;
                        var xs = r.TryGetProperty("x", out var xe) ? xe.EnumerateArray().Select(v => v.GetInt32()).ToList() : null;
                        // a row the icon had a verdict on (since 2026-10-25): its text as read and the icon's scores, settled again
                        // with this version's rules (PopupIcons.Replay) instead of taking what the live read made of it
                        var rs = r.TryGetProperty("r", out var re) ? re.EnumerateArray().Select(v => v.GetString() ?? "").ToList() : null;
                        var ics = r.TryGetProperty("i", out var ie) ? ie.EnumerateArray().Select(v => v.GetString() ?? "").ToList() : null;
                        var lootLines = ls.Select((x, i) =>
                        {
                            var line = new LootLine(ys != null && i < ys.Count ? ys[i] : 100 + i * 30,
                                System.Text.RegularExpressions.Regex.Replace(x, @"^(\d+) Kina$", "Kina x$1"),
                                cs != null && i < cs.Count ? cs[i] : -1, hs != null && i < hs.Count ? hs[i] : 0,
                                bs != null && i < bs.Count ? bs[i] : -1, ss != null && i < ss.Count ? ss[i] : -1,
                                xs != null && i < xs.Count ? xs[i] : -1);
                            if (rs == null || ics == null || i >= rs.Count || i >= ics.Count || rs[i].Length == 0 || ics[i].Length == 0) return line;
                            var look = PopupIcons.Replay(items, rs[i], ics[i]);
                            if (look.Unsure) CrashLog.Note($"loot list: \"{rs[i]}\": {look.Note}");
                            else if (look.Text != rs[i]) CrashLog.Note($"loot list: \"{rs[i]}\" read as \"{look.Text}\": {look.Note}");
                            return line with { Text = look.Text, Before = rs[i], IconScores = ics[i], Unsure = look.Unsure };
                        }).ToList();
                        // "p": the band's pixel fingerprint, as the live read takes it (ocr.list_skip_same): a read with the last
                        // recognized read's fingerprint, under 2 s after it and with no change on screen since, stands on its lines
                        if (r.TryGetProperty("p", out var pe) && pe.GetString() is { Length: > 0 } print) lootLines = session.TraceListRead(print, lootLines, t);
                        session.ListGated(lootLines, t, session.Verdict("loot_list"));
                        break;
                    }
                    case "gather":
                        // the gathering bar finishing a race (what CraftPanelDue feeds GatherOutcome live)
                        foreach (var g in ls) session.GatherOutcome(t, g.EndsWith("ok", StringComparison.OrdinalIgnoreCase), g.StartsWith("upgrade", StringComparison.OrdinalIgnoreCase));
                        break;
                    // one pass of the crafting panel's locator (2026-10-25): "l" the lines its text read gave, empty when the
                    // text wasn't read that pass; "up": false for a pass without the panel; "raised": true for the gathering
                    // panel above the skill bar. The session decides crafting or a gathering bar as CraftProbeAsync does
                    case "panel":
                    {
                        bool up = !r.TryGetProperty("up", out var ue) || ue.GetBoolean();
                        bool raised = r.TryGetProperty("raised", out var re) && re.GetBoolean();
                        session.PanelLook(t, up, raised, ls.Count > 0 ? ls : null);
                        if (up && session.PanelCrafting) session.CraftingPass(t);
                        break;
                    }
                    // the panel's bars finishing a race ("ok", "fail", "combo ok", "combo fail"), routed by the session's own
                    // decision: a craft while it is crafting, else the gathering bar's race (CraftPanelDue)
                    case "bar":
                        foreach (var b in ls) session.PanelBars(t, b.EndsWith("ok", StringComparison.OrdinalIgnoreCase), b.StartsWith("combo", StringComparison.OrdinalIgnoreCase) || b.StartsWith("upgrade", StringComparison.OrdinalIgnoreCase));
                        break;
                    case "zone": if (ls.FirstOrDefault() is { Length: > 0 } z) { session.Engine.SetZone(z); session.Instances.Zone(z, t); } break;
                    // a nameplate or the target bar's name on screen then (LootNotRow)
                    case "plate": foreach (var p in ls) session.NotRow.See(p, t, LootNotRow.Plate); break;
                    // the target bar's text that names no enemy on the list (reads.log's "target bar:" lines): a gathering node
                    // when it matches one (TargetReader.SeeNode), as live
                    case "target": foreach (var p in ls) { session.Target.SeeNode(p, t); session.TraceTarget(p, t); } break;
                    // the list covered (a full-screen window, a scene it isn't read in): this pass skips it, as the scene gate does
                    case "cover": session.ListGated(new List<LootLine>(), t, Gate.Skip); break;
                    // a pass, shop or reward window's title in the top band (reads.log's "reward screen open (...)")
                    case "reward": if (ls.FirstOrDefault() is { Length: > 0 } title) session.Target.MarkReward(t, title); break;
                    // a reward table as voted (reads.log's "voted slot by slot" line), one slot per entry:
                    // "grade|count as read|closest icon|its distance", through ApplyGrid as RewardGridDue files it
                    case "table":
                        session.ApplyGrid(ls.Select(x => x.Split('|')).Select(p => new RewardGrid.Slot(new Rectangle(0, 0, 10, 10), p[0], p[1], RewardGrid.ParseCount(p[1]),
                            p.Length >= 4 && p[2].Length > 0 ? new List<(string, double)> { (p[2], double.Parse(p[3], System.Globalization.CultureInfo.InvariantCulture)) } : new List<(string, double)>())).ToList(), t);
                        break;
                    // a trusted reading of the Kinah counter (the watcher's BalanceTracker, as live): the Kina witness's evidence
                    case "balance": if (ls.FirstOrDefault() is { } bv && long.TryParse(bv, out var bal)) session.CheckBalance(bal, t); break;
                }
                // a reward picture filed a few seconds before is judged against the chat once its lines are in, as every live pass does
                session.TableWitnessDue(t);
                last = t;
            }
        }
        if (node["trace"]?.GetValue<string>() is { } traceFile)
        {
            FeedTrace(traceFile);
            session.FlushVotes(last);
            // chat gathers still waiting for a race are settled (GatherAttempts)
            session.GatherTick(last.AddSeconds(10));
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
            else if (f["k"]!.GetValue<string>() == "cards")
            {
                // an event's reward screen: the picture is the game window. The run is open (the case's "zone"), the title is
                // read as RewardGridDue reads it, and the frames of one case are one screen, read in turn and filed at the end
                if (!cards && node["zone"]?.GetValue<string>() is { Length: > 0 } z) { session.Engine.SetZone(z); session.Instances.Zone(z, t.AddMinutes(-5)); }
                cards = true;
                Task.Run(() => FeedCardsAsync(session, bmp, t, Console.WriteLine)).GetAwaiter().GetResult();
            }
            else if (f["k"]!.GetValue<string>() == "table")
            {
                // an expedition's reward table: the picture is the game window; read as RewardGridDue reads it, each slot named
                // the way ApplyGrid names it (2026-10-23 Urugugu: 29 slots, 14 not named by 0.8.17)
                var band = new Rectangle(bmp.Width / 4, 0, bmp.Width / 2, bmp.Height / 3);
                using var bandBmp = bmp.Clone(band, bmp.PixelFormat);
                var lines = Task.Run(() => ScreenReader.ReadBoxesAsync(bandBmp, band.Location, 1)).GetAwaiter().GetResult();
                var rc = TrackerSession.CollectedLine(lines, bmp.Width);
                if (rc == null)
                {
                    lines = Task.Run(() => ScreenReader.ReadBoxesAsync(bandBmp, band.Location, 2)).GetAwaiter().GetResult();
                    rc = TrackerSession.CollectedLine(lines, bmp.Width);
                }
                Console.WriteLine($"  table band ({bmp.Width}x{bmp.Height}, {bmp.PixelFormat}): " + string.Join(" | ", lines.Select(l => $"{l.Text} @{l.Box.Y}")) + (rc == null ? " -- no \"collected\" line" : ""));
                table = new JsonArray();
                // the same picture read as 0.8.17 read it (no badge mask), for the before-and-after in the log
                if (rc != null)
                {
                    RewardGrid.MaskBadges = false;
                    try
                    {
                        var before = Task.Run(() => RewardGrid.ReadAsync(bmp, rc.Box.Bottom + (int)(bmp.Height * 0.016))).GetAwaiter().GetResult();
                        Console.WriteLine($"  table without the badge mask: {before.Count(s => TrackerSession.SlotName(items, s) != null)} of {before.Count} slots named, {before.Take(29).Count(s => TrackerSession.SlotName(items, s) != null)} of the first 29: " + string.Join(" | ", before.Select(s => $"{TrackerSession.SlotName(items, s) ?? "?"} x{s.Count} ({(s.Icons.Count > 0 ? s.Icons[0].Distance.ToString("0") : "-")})")));
                    }
                    finally { RewardGrid.MaskBadges = true; }
                }
                if (rc != null)
                {
                    var read = Task.Run(() => RewardGrid.ReadAsync(bmp, rc.Box.Bottom + (int)(bmp.Height * 0.016))).GetAwaiter().GetResult();
                    var prefer = TrackerSession.SetMates(read, TrackerSession.ByIcon(items));
                    foreach (var sl in read)
                        table.Add(new JsonObject
                        {
                            ["n"] = sl.Count, ["grade"] = sl.Grade, ["name"] = TrackerSession.SlotName(items, sl, prefer) ?? "?",
                            ["cands"] = string.Join(" | ", TrackerSession.GradedCandidates(items, sl).Take(3).Select(c => $"{c.Names} ({c.Key} {c.Distance:0})")),
                            ["icon"] = sl.Icons.Count > 0 ? $"{sl.Icons[0].Key} {sl.Icons[0].Distance:0}" + (sl.Icons.Count > 1 ? $" / {sl.Icons[1].Distance:0}" : "") : "none",
                        });
                }
            }
            else Task.Run(() => session.ReplayListFrameAsync(bmp, at, t)).GetAwaiter().GetResult();
            if (table != null)
            {
                for (int si = 0; si < table.Count; si++)
                    Console.WriteLine($"  slot {si + 1,2} {table[si]!["grade"]} x{table[si]!["n"]}: {table[si]!["name"]} · best {table[si]!["icon"]} · same grade: {table[si]!["cands"]}");
                int namedMasked = table.Count(x => x!["name"]?.GetValue<string>() != "?");
                int namedFirst = table.Take(29).Count(x => x!["name"]?.GetValue<string>() != "?");
                Console.WriteLine("  table with the badge mask: " + namedMasked + " of " + table.Count + " slots named, " + namedFirst + " of the first 29");
            }
            last = t;
        }
        if (cards)
        {
            session.FlushEventCards(last.AddSeconds(5), "the case ended", force: true);
            var outCards = new JsonArray();
            foreach (var (ci, rec, sl, more) in session.LastEventCards ?? new())
            {
                var c = new JsonObject { ["received"] = rec };
                if (rec)
                {
                    var arr = new JsonArray();
                    var prefer = TrackerSession.SetMates(sl, TrackerSession.ByIcon(items));
                    foreach (var x in sl)
                        arr.Add(new JsonObject
                        {
                            ["n"] = x.Count, ["grade"] = x.Grade, ["name"] = TrackerSession.SlotName(items, x, prefer) ?? "?",
                            ["icon"] = x.Icons.Count > 0 ? $"{x.Icons[0].Key} {x.Icons[0].Distance:0}" + (x.Icons.Count > 1 ? $" / {x.Icons[1].Key} {x.Icons[1].Distance:0}" : "") : "none",
                        });
                    c["slots"] = arr; c["more"] = more;
                    for (int i = 0; i < arr.Count; i++) Console.WriteLine($"  card {ci + 1} slot {i + 1,2} {arr[i]!["grade"]} x{arr[i]!["n"]}: {arr[i]!["name"]} · {arr[i]!["icon"]} · same grade: {string.Join(" | ", TrackerSession.GradedCandidates(items, sl[i]).Take(3).Select(g => $"{g.Names} ({g.Key} {g.Distance:0})"))}");
                }
                outCards.Add(c);
            }
            res0Cards = outCards;
        }
        if (node["after"]?.GetValue<string>() is { } afterFile) { FeedTrace(afterFile); session.FlushVotes(last); }
        // a picture still waiting for its chat is judged on what came
        session.TableWitnessDue(last.AddSeconds(TableWitness.MaxSeconds), force: true);
        // rows still waiting for a chat line that never comes are settled the way a live run settles them: later reads
        foreach (var dt in new[] { 2.0, 5.0, 9.0 }) session.ProcessList(new List<(int, string)>(), last.AddSeconds(dt));
        ChatLatencies.AddRange(session.Voter.Latencies); ChatFast += session.Voter.Fast;
        session.CommitHeld();
        session.ReleaseAwaiting(last.AddSeconds(30), final: true);
        var e = session.Engine;
        var outItems = new JsonObject();
        foreach (var (id, n) in e.Items.OrderBy(kv => items.NameOf(kv.Key), StringComparer.Ordinal)) outItems[items.NameOf(id)] = n;
        // a text case's crafts: the races its "bar" reads gave the panel, as the Crafting part of the window counts them,
        // and what they made by item (the panel is the output's witness, never the loot list)
        var made = new JsonObject();
        foreach (var rc in session.CraftView.CraftSnapshot())
        {
            ok += rc.Ok; fail += rc.Fail;
            foreach (var (mi, mn) in rc.Made) made[mi] = (made[mi]?.GetValue<int>() ?? 0) + mn;
        }
        var res = new JsonObject { ["craft_ok"] = ok, ["craft_fail"] = fail, ["items"] = outItems };
        if (made.Count > 0) res["craft_made"] = made;
        if (table != null) res["table"] = table;
        if (res0Cards != null) res["cards"] = res0Cards;
        if (e.Totals().Kinah > 0) res["kinah"] = e.Totals().Kinah;
        // Soul Crystals from kills and the kills counted, for the cases about them (2026-10-23)
        if (e.PetCrystals > 0) res["pets"] = e.PetCrystals;
        if (e.Totals().Kills > 0) res["kills"] = e.Totals().Kills;
        // loot list rows that went before their text settled: logged as unsure, never counted (LootRows)
        if (session.ListUnsure > 0) res["list_unsure"] = session.ListUnsure;
        // lines in the band that weren't rows (a nameplate, a zone's name, text over the world): dropped, logged once per text per minute (LootNotRow)
        if (session.ListNotRow > 0) res["list_notrow"] = session.ListNotRow;
        // loot list reads that stood on the last recognized read's lines, their pixels unchanged (ocr.list_skip_same)
        if (session.ListSameSkips > 0) res["list_same"] = session.ListSameSkips;
        // the loot list's item rows shown to the cross-check, by the name each one ended on (one per row): what the list
        // said the pickups were, whether or not a chat line made them count (2026-10-25, the icon against the text)
        if (session.ListRows is { Count: > 0 } lr)
        {
            var named = new JsonObject();
            foreach (var g in lr.Values.GroupBy(n => n, StringComparer.Ordinal).OrderBy(g => g.Key, StringComparer.Ordinal)) named[g.Key] = g.Count();
            res["list_rows"] = named;
        }
        // chat reads that couldn't be lined up, and lines the settle counted that were first on screen after them (2026-10-25)
        if (session.Unaligned > 0) res["chat_unaligned"] = session.Unaligned;
        if (session.SettledLate > 0) res["chat_settled_late"] = session.SettledLate;
        // gathering attempts (GatherAttempts): per skill "ok/failed", and per node
        var skills = new JsonObject();
        foreach (var (f, st) in e.SkillSnapshot().Where(kv => kv.Value.Nodes > 0).OrderBy(kv => kv.Key)) skills[f.ToString()] = $"{st.Nodes - st.Failed}/{st.Failed}";
        if (skills.Count > 0) res["attempts"] = skills;
        var nodes = new JsonObject();
        foreach (var (n, ns) in e.NodeSnapshot().OrderBy(kv => kv.Key, StringComparer.Ordinal)) nodes[n] = $"{ns.Ok}/{ns.Failed}";
        if (nodes.Count > 0) res["nodes"] = nodes;
        // what went to expedition and event runs rather than farming: items per run
        var runs = new JsonObject();
        foreach (var run in session.Instances.Snapshot()) runs[run.Zone] = run.Items.Values.Sum() + run.BoundItems.Values.Sum();
        if (runs.Count > 0) res["runs"] = runs;
        // how the runs were kept: how many, how many counted from a reward picture, their Kinah, their pieces and when the
        // first began (UTC), for the run data cases (2026-10-24)
        var kept = session.Instances.Snapshot();
        if (kept.Count > 0)
        {
            res["runs_kept"] = kept.Count;
            res["runs_from_picture"] = kept.Count(r => r.FromPicture);
            res["run_kinah"] = kept.Sum(r => r.Kinah);
            res["run_gear"] = kept.Sum(r => r.Pieces(items.IsGear).Gear);
            res["run_materials"] = kept.Sum(r => r.Pieces(items.IsGear).Materials);
            res["run_start"] = kept[0].Start.ToUniversalTime().ToString("HH:mm:ss") + "Z";
            // every kept run's start, and the chat's Kina set aside from picture runs (2026-10-25: run edges from the chat's room lines)
            res["run_starts"] = string.Join(" ", kept.Select(r => r.Start.ToUniversalTime().ToString("HH:mm:ss") + "Z"));
            res["run_kinah_set_aside"] = kept.Sum(r => r.ChatKinahSetAside);
            // partly read runs (2026-10-24): flagged, sent all the same; pieces with unknown gear and without currencies, the
            // currencies as amounts (Abyss Points), the slots not read
            bool Cur(long id) => items.Get(id)?.Category == "Currency";
            res["runs_partly_read"] = kept.Count(r => r.PartlyRead);
            res["runs_sent"] = Upload.InstanceRunPayload.From(kept.Select(r => r.Clone()).ToList(), id => items.KeyOf(id))?.Count ?? 0;
            res["run_not_read"] = kept.Sum(r => r.SlotsUnread);
            res["run_pieces"] = kept.Sum(r => { var p = r.Pieces(items.IsGear, Cur, withUnknown: true); return p.Gear + p.Materials + r.UnknownItems; });
            res["run_unknown_gear"] = kept.Sum(r => r.UnknownGear);
            res["run_unknown_items"] = kept.Sum(r => r.UnknownItems);
            res["run_abyss_points"] = kept.Sum(r => r.Currencies(Cur).Where(kv => items.NameOf(kv.Key).StartsWith("Abyss Points", StringComparison.OrdinalIgnoreCase)).Sum(kv => kv.Value));
            // the runs' items by name, bound and tradeable together (expect names only the ones it checks), and the gear notes
            var runItems = new JsonObject();
            foreach (var g in kept.SelectMany(r => r.Items.Concat(r.BoundItems)).GroupBy(kv => items.NameOf(kv.Key), StringComparer.Ordinal).OrderBy(g => g.Key, StringComparer.Ordinal))
                runItems[g.Key] = g.Sum(kv => kv.Value);
            res["run_items"] = runItems;
            // the chat's items set aside by runs whose rewards a picture decides (by name; expect names only the ones it checks)
            var chatItems = new JsonObject();
            foreach (var g in kept.SelectMany(r => r.ChatItems).GroupBy(kv => items.NameOf(kv.Key), StringComparer.Ordinal).OrderBy(g => g.Key, StringComparer.Ordinal))
                chatItems[g.Key] = g.Sum(kv => kv.Value);
            res["run_chat_items"] = chatItems;
            res["run_gear_notes"] = string.Join(" | ", kept.SelectMany(r => r.GearNotes));
        }
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
                ["name"] = "loot list, one row the chat never showed: not counted (loot list rows count only with a chat line, 2026-10-23)",
                ["source"] = "generated by replay-bank --seed",
                ["game"] = new JsonObject { ["w"] = game.Width, ["h"] = game.Height }, ["pending"] = false, ["needs_ocr"] = true,
                ["frames"] = frames, ["expect"] = new JsonObject { ["items"] = new JsonObject() },
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
