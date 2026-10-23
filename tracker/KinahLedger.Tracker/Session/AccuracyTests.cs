using System.Drawing;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using KinahLedger.Data;
using KinahLedger.Ocr;
using KinahLedger.Update;
using KinahLedger.Upload;

namespace KinahLedger.Session;

/// <summary>
/// `accuracy-test`: the accuracy pipeline's own checks (2026-10-23), beside parse-test. Reading health counters, the
/// misread names and what may leave the PC, the remote config's safe ranges and the staged rollout, layout fractions and
/// hints, crowd aliases, the rare-drop rule (always counted, second read, proof), the miscount pictures that may be sent
/// (never the chat), and the replay bank. Writes only to a scratch folder.
/// </summary>
public static class AccuracyTests
{
    public static int Run(Settings settings)
    {
        CrashLog.DirOverride = Path.Combine(Path.GetTempPath(), "KinahLedger-accuracy-test");
        Directory.CreateDirectory(CrashLog.DirOverride);
        RemoteConfig.PathOverride = Path.Combine(CrashLog.DirOverride, "remote-config.json");
        RemoteConfig.Current = RemoteConfig.BuiltIn;
        int fails = 0;
        void Check(string what, string got, string expect)
        {
            bool ok = got == expect;
            if (!ok) fails++;
            Console.WriteLine($"  {what,-62} -> {(got.Length > 48 ? got[..48] + "…" : got),-50} {(ok ? "ok" : "MISMATCH (expected " + expect + ")")}");
        }
        var items = ItemDictionary.LoadOrEmpty(settings.ItemsFile);

        Console.WriteLine("Names that may leave the PC:");
        Check("a misread item name", Accuracy.SafeName("Orichalcurn  Ore ") ?? "null", "orichalcurn ore");
        Check("a chat name in brackets", Accuracy.SafeName("[Bob] hello there") ?? "null", "null");
        Check("a message with a colon", Accuracy.SafeName("Bob: wts gear") ?? "null", "null");
        Check("a link", Accuracy.SafeName("www kinah shop com") ?? "null", "null");
        Check("too many words", Accuracy.SafeName("one two three four five six seven eight") ?? "null", "null");
        Check("too long", Accuracy.SafeName(new string('a', 49)) ?? "null", "null");
        Check("an item with an apostrophe and brackets", Accuracy.SafeName("Artisan's Refining Stone (Bound)") ?? "null", "artisan's refining stone (bound)");

        Console.WriteLine("Health counters and names, taken and put back:");
        Accuracy.Reset();
        Accuracy.Add("chat_reads", 10); Accuracy.Add("guessed"); Accuracy.AddReading(61.7);
        Accuracy.Name("Orichalcurn Ore", 610620013, "snapped"); Accuracy.Name("Orichalcurn Ore", 610620013, "snapped"); Accuracy.Name("[Bob] hi", null, "unknown");
        Accuracy.Layout(new LayoutReport(2560, 1440, 18, new[] { 0.01, 0.6, 0.2, 0.3 }, "found", false, false, "b1"));
        var rep = Accuracy.Take();
        Check("counters taken", $"{rep.Health.GetValueOrDefault("chat_reads")} {rep.Health.GetValueOrDefault("guessed")} {rep.Health.GetValueOrDefault("reading_s")}", "10 1 61");
        Check("names: one entry, counted twice, the chat line left out", string.Join(",", rep.Names.Select(n => $"{n.read}|{n.item}|{n.how}|{n.n}")), "orichalcurn ore|610620013|snapped|2");
        Check("the layout goes once", $"{rep.Layout != null} {Accuracy.Take().Layout != null}", "True False");
        Check("taken means started over", $"{Accuracy.Get("chat_reads")}", "0");
        Accuracy.Restore(rep);
        Check("a failed send puts it back", $"{Accuracy.Get("chat_reads")} {Accuracy.Take().Names.Count}", "10 1");
        Accuracy.Muted = true; Accuracy.Add("guessed"); Accuracy.Name("Fresh Frult", 1, "snapped"); Accuracy.Muted = false;
        Check("a second read's look-ups count nothing", $"{Accuracy.Get("guessed")} {Accuracy.Take().Names.Count}", "0 0");
        var payload = JsonSerializer.Serialize(AccuracyPayload.From(new AccuracyReport(new() { ["pickups"] = 3 }, new(), null), "abc"));
        Check("the payload: version, install, counters, nothing else", Regex(payload, "\"(\\w+)\":"), "app,install,health,pickups");

        Console.WriteLine("The parser and the dictionary report what they couldn't read exactly:");
        Accuracy.Reset();
        var p = new ChatParser(items);
        _ = p.Parse("Acquired Flarble Wossname.", DateTime.UtcNow).ToList();
        var r1 = Accuracy.Take();
        Check("a name no item is near: counted as unknown, not sent", $"{r1.Health.GetValueOrDefault("unknown")} {r1.Names.Count}", "1 0");
        _ = p.Parse("Orichalcurn Ore acquired.", DateTime.UtcNow).ToList();
        var r2 = Accuracy.Take();
        Check("a misread that snaps: reported with its item", string.Join(",", r2.Names.Select(n => $"{n.read}|{n.item}|{n.how}")), "orichalcurn ore|610620013|snapped");
        Check("...and counted", $"{r2.Health.GetValueOrDefault("snapped")}", "1");

        Console.WriteLine("Rare drops (Steven, 2026-10-23):");
        Check("an Epic item is rare", $"{RareGate.IsRare(items, 514310110, false)}", "True");
        Check("a Legend material is rare (the rule as written)", $"{RareGate.IsRare(items, 610511001, false)}", "True");
        RemoteConfig.Current = RemoteConfig.Parse("{\"rare\":{\"legend_materials\":false}}");
        Check("...unless the config keeps Legend for gear", $"{RareGate.IsRare(items, 610511001, false)} {RareGate.IsRare(items, 110140002, false)}", "False True");
        RemoteConfig.Current = RemoteConfig.BuiltIn;
        Check("a Common item isn't", $"{RareGate.IsRare(items, 610620013, false)}", "False");
        Check("...and no synthetic id is", $"{RareGate.IsRare(items, -5, true)}", "False");
        const string cut = "ctral Greatsword acquired.";
        var gp = new ChatParser(items) { Fighting = () => new[] { "Abandoned Daeva" }, Rare = _ => false };
        Check("a guess no table backs, not rare: held back as before", Item(gp.Parse(cut, DateTime.UtcNow), items), "nothing");
        gp.Rare = id => RareGate.IsRare(items, id, false);
        Check("the same guess at a Unique weapon: never held back", Item(gp.Parse(cut, DateTime.UtcNow), items), "Spectral Greatsword (guessed)");
        var guess = new ItemAcquired(DateTime.UtcNow, 110130007, 1, false) { Via = "chat · guessed" };
        var pend = new RareGate.Pending(guess, "chat", cut, DateTime.UtcNow);
        Check("second read agrees: confirmed", D(RareGate.Decide(pend, new RareGate.Second(110130007, "Spectral Greatsword acquired.", null), items), items), "Spectral Greatsword confirmed=True unconfirmed=False");
        Check("second read names another item exactly: that one", D(RareGate.Decide(pend, new RareGate.Second(110120001, "x", null), items), items), "Noble Dragon Lord Greatsword confirmed=True unconfirmed=False");
        Check("second read can't tell: counted as read, unconfirmed", D(RareGate.Decide(pend, new RareGate.Second(null, "", null), items), items), "Spectral Greatsword confirmed=False unconfirmed=True");
        Check("no picture to read again: the same", D(RareGate.Decide(pend, null, items), items), "Spectral Greatsword confirmed=False unconfirmed=True");
        var exact = new RareGate.Pending(guess with { Via = "chat" }, "chat", "", DateTime.UtcNow);
        Check("an exact read is never second-guessed", D(RareGate.Decide(exact, new RareGate.Second(110120001, "x", null), items), items), "Spectral Greatsword confirmed=True unconfirmed=False");

        // the session holds a rare pickup until its second read, and a replay (no picture) counts it at the next step
        Accuracy.Reset();
        var s = new TrackerSession(null, settings, items, "Test");
        var t0 = DateTime.UtcNow;
        // the first read of a session is what was already on screen; the pickup arrives in the reads after it
        s.ProcessChat(new List<string> { "Acquired 120 Kina." }, t0);
        for (int i = 1; i <= 4; i++) s.ProcessChat(new List<string> { "Acquired 120 Kina.", "Noble Dragon Lord Greatsword acquired." }, t0.AddSeconds(i * 0.4));
        s.FlushVotes(t0.AddSeconds(2));
        Check("an Epic pickup in the chat: counted once", $"{s.Engine.Items.Where(kv => items.NameOf(kv.Key) == "Noble Dragon Lord Greatsword").Sum(kv => kv.Value)} {s.RareCounted} {s.Rare.Waiting}", "1 1 0");
        var h = Accuracy.Take().Health;
        Check("...and in the health numbers", $"{h.GetValueOrDefault("rare_seen")} {h.GetValueOrDefault("pickups")}", "1 1");
        Check("a loot list row confirmed by a second read isn't 'unconfirmed'", $"{new ItemAcquired(t0, 1, 1, false) { Via = "loot list · confirmed by a second read" }.Unconfirmed}", "False");
        var proofDir = Path.Combine(CrashLog.DirOverride, "proof-test"); Directory.CreateDirectory(proofDir);
        File.WriteAllBytes(Path.Combine(proofDir, "20200101-000000-000-list-Old.png"), new byte[10]);
        File.SetLastWriteTime(Path.Combine(proofDir, "20200101-000000-000-list-Old.png"), DateTime.Now.AddDays(-90));
        File.WriteAllBytes(Path.Combine(proofDir, "notes.png"), new byte[10]);
        Check("old proof pictures go, other files stay", $"{RareGate.Prune(proofDir, DateTime.Now)} {File.Exists(Path.Combine(proofDir, "notes.png"))}", "1 True");
        var crop = RareGate.CropAround(new Rectangle(1100, 520, 200, 30), new Point(1000, 500), new Size(400, 300), withIcon: true);
        Check("a list row's proof holds its icon", $"{crop.X} {crop.Width}", "22 288");

        Console.WriteLine("Remote config: safe ranges, defaults, settings.json wins:");
        var c = RemoteConfig.Parse("{\"id\":\"c1\",\"ocr\":{\"scale\":99,\"target_text_px\":5,\"interval_ms\":\"fast\"},\"regions\":{\"id\":\"r2\",\"loot\":{\"left\":0.1,\"right\":-0.5}},\"words\":{\"craft_panel\":[]},\"kill\":{\"nameplates\":true,\"rm -rf\":true},\"rollout\":{\"version\":\"0.8.12\",\"percent\":250}}");
        Check("numbers held to their ranges", $"{c.Scale} {c.TargetTextPx} {c.IntervalMs}", "4 20 700");
        Check("a loot band turned inside out: the built-in one", $"{c.LootLeft} {c.LootRight}", "-0.42 -0.02");
        Check("an empty word list keeps the built-in words", string.Join(",", c.CraftPanelWords), "Crafting");
        Check("only known readers can be switched off", string.Join(",", c.Killed), "nameplates");
        Check("a percent over 100 is 100", $"{c.RolloutPercent}", "100");
        Check("the built-in config is what the tracker did before", $"{RemoteConfig.BuiltIn.Scale} {RemoteConfig.BuiltIn.TargetTextPx} {RemoteConfig.BuiltIn.IntervalMs} {RemoteConfig.BuiltIn.FightIntervalMs} {RemoteConfig.BuiltIn.LootLeft} {RemoteConfig.BuiltIn.LootBottom} {RemoteConfig.BuiltIn.Killed.Count}", "2 36 700 300 -0.42 0.3 0");
        var cfgS = new Settings();
        RemoteConfig.Parse("{\"ocr\":{\"scale\":3,\"target_text_px\":40}}").ApplyTo(cfgS, "{ \"ocr\": { \"Scale\": 2 } }");
        Check("settings.json's own value wins, the rest follow the site", $"{cfgS.Ocr.Scale} {cfgS.Ocr.TargetTextPx}", "2 40");
        File.WriteAllText(RemoteConfig.PathOverride, "not json");
        Check("a broken kept copy: the built-in config", RemoteConfig.LoadKept().Id, "built-in");
        Check("the config address follows upload.endpoint", RemoteConfig.UrlFor(new UploadSettings()) + " | " + RemoteConfig.UrlFor(new UploadSettings { Endpoint = "" }), "https://kinahledger.com/v1/config | ");

        Console.WriteLine("Staged rollout:");
        var v = new Version(0, 8, 12);
        Check("built in (no site): every update installs", $"{RemoteConfig.BuiltIn.AutoInstall(v, "x").Auto}", "True");
        Check("held: none installs on its own", $"{RemoteConfig.Parse("{\"rollout\":{\"version\":\"0.8.12\",\"percent\":50,\"hold\":true,\"reason\":\"unaligned reads up\"}}").AutoInstall(v, "x").Auto}", "False");
        var ten = RemoteConfig.Parse("{\"rollout\":{\"version\":\"0.8.12\",\"percent\":10}}");
        int inTen = Enumerable.Range(0, 2000).Count(i => ten.AutoInstall(v, "install-" + i).Auto);
        Check("10%: about a tenth of 2,000 installs", inTen is > 140 and < 260 ? "about a tenth" : inTen.ToString(), "about a tenth");
        Check("the same install, the same answer", $"{RemoteConfig.Bucket("abc", v) == RemoteConfig.Bucket("abc", v)} {RemoteConfig.Bucket("abc", v) is >= 0 and < 100}", "True True");
        Check("a version the site doesn't know yet: the default percent", $"{RemoteConfig.Parse("{\"rollout\":{\"version\":\"0.8.11\",\"percent\":10,\"default_percent\":0}}").AutoInstall(v, "x").Auto}", "False");
        Check("a rollout already past this version: installs", $"{RemoteConfig.Parse("{\"rollout\":{\"version\":\"0.8.13\",\"percent\":0}}").AutoInstall(v, "x").Auto}", "True");

        Console.WriteLine("Layout profiles:");
        var game = new Rectangle(100, 50, 2560, 1440);
        var chat = new Rectangle(130, 900, 600, 400);
        var fr = LayoutProfile.Fractions(chat, game);
        Check("fractions and back", $"{string.Join(",", fr)} -> {LayoutProfile.ToRect(fr, game)}", "0.0117,0.5903,0.2344,0.2778 -> {X=130,Y=900,Width=600,Height=400}");
        Check("a box in the top right is no chat", $"{LayoutProfile.Plausible(new[] { 0.7, 0.05, 0.2, 0.2 })} {LayoutProfile.Plausible(fr)}", "False True");
        Check("the text size key", $"{LayoutProfile.UiKey(24, 1440)} {LayoutProfile.UiKey(null, 1440)}", "18 0");
        var hints = AccuracyClient.ParseHints("{\"chat\":[{\"ui\":18,\"box\":[0.01,0.6,0.2,0.3],\"installs\":4},{\"ui\":20,\"box\":[0.9,0.9,0.5,0.5],\"installs\":9},{\"ui\":16,\"box\":[0.02,0.58,0.22,0.3],\"installs\":2}]}");
        Check("hints: impossible boxes dropped", $"{hints.Count}", "2");
        Check("the hint closest to this text size", $"{LayoutProfile.Best(hints, 17)?.Ui} {LayoutProfile.Best(hints, 0)?.Installs}", "18 4");
        Check("layout routes follow upload.endpoint", AccuracyClient.Url(new UploadSettings(), "layout"), "https://kinahledger.com/v1/layout");

        Console.WriteLine("Crowd aliases:");
        var al = ItemDictionary.LoadOrEmpty(settings.ItemsFile);
        al.LoadAliases(new MemoryStream(Encoding.UTF8.GetBytes("{\"aliases\":{\"orichalkum ore\":610620013,\"orichalcum ore\":110120001,\"zzzz qqqq\":424242424242}}")));
        Accuracy.Reset();
        Check("an alias counts as its item", al.NameOf(al.IdOf("Orichalkum Ore")), "Orichalcum Ore");
        Check("...and isn't reported as a misread", $"{Accuracy.Take().Names.Count}", "0");
        Check("a real name is never taken over", al.NameOf(al.IdOf("Orichalcum Ore")), "Orichalcum Ore");
        Check("an alias isn't an exact name, an unknown id is skipped", $"{al.IsExact("Orichalkum Ore")} {al.AliasCount}", "False 1");
        Check("the built-in alias list loads", $"{items.AliasCount >= 0}", "True");
        Check("the alias file is a data update", $"{DataFiles.Names.Contains("aliases.json")} {DataFiles.LooksRight("aliases.json", Encoding.UTF8.GetBytes("{\"aliases\":{\"a b c\":1}}"))}", "True True");

        Console.WriteLine("Miscount pictures that may be sent:");
        var rd = Path.Combine(CrashLog.DirOverride, "report-test"); if (Directory.Exists(rd)) Directory.Delete(rd, true);
        Directory.CreateDirectory(Path.Combine(rd, "frames"));
        using (var b = new Bitmap(20, 10)) foreach (var f in new[] { "chat-1.png", "loot-1.png", "craft-1.png" }) b.Save(Path.Combine(rd, "frames", f), System.Drawing.Imaging.ImageFormat.Png);
        File.WriteAllLines(Path.Combine(rd, "frames.jsonl"), new[]
        {
            "{\"t\":\"2026-10-23T10:00:00Z\",\"k\":\"chat\",\"f\":\"frames/chat-1.png\",\"x\":110,\"y\":900}",
            "{\"t\":\"2026-10-23T10:00:01Z\",\"k\":\"loot\",\"f\":\"frames/loot-1.png\",\"x\":1300,\"y\":700}",
            "{\"t\":\"2026-10-23T10:00:02Z\",\"k\":\"craft\",\"f\":\"frames/craft-1.png\",\"x\":1500,\"y\":1200}",
            "{\"t\":\"2026-10-23T10:00:03Z\",\"k\":\"loot\",\"f\":\"../../outside.png\",\"x\":0,\"y\":0}",
        });
        File.WriteAllText(Path.Combine(rd, "game.json"), "{\"x\":100,\"y\":50,\"w\":2560,\"h\":1440}");
        File.WriteAllText(Path.Combine(rd, "counted.jsonl"), "{\"t\":\"2026-10-23T10:00:01Z\",\"what\":\"Orichalcum Ore ×2\",\"via\":\"loot list\"}\n");
        var fp = AccuracyClient.BuildFrames(rd, "Report a miscount")!;
        Check("only the loot list and the panel, never the chat", string.Join(",", fp.frames.Select(f => f.k)), "loot,craft");
        Check("places relative to the game window", string.Join(",", fp.frames.Select(f => $"{f.x}:{f.y}")), "1200:650,1400:1150");
        Check("nothing outside the report folder", $"{fp.frames.Count} {fp.game["w"]}x{fp.game["h"]} {fp.counted.Count}", "2 2560x1440 1");

        Console.WriteLine("Replay bank:");
        var bank = Path.Combine(CrashLog.DirOverride, "replays"); if (Directory.Exists(bank)) Directory.Delete(bank, true);
        Directory.CreateDirectory(bank);
        var seeded = ReplayBank.Seed(bank);
        var results = ReplayBank.Run(bank, settings);
        foreach (var rr in results) Console.WriteLine($"    {rr.Case}: {rr.Status} {rr.Detail}");
        Check("generated crafting cases pass", string.Join(",", results.Where(x => x.Case.StartsWith("seed-craft")).Select(x => x.Status)), "pass,pass");
        Check("the loot list case passes (or is skipped without text recognition)", results.First(x => x.Case == "seed-loot-row").Status is "pass" or "skipped" ? "ok" : results.First(x => x.Case == "seed-loot-row").Detail, "ok");
        var caseFile = Path.Combine(bank, "seed-craft-1920x1080", "case.json");
        File.WriteAllText(caseFile, File.ReadAllText(caseFile).Replace("\"craft_fail\": 1", "\"craft_fail\": 2"));
        Check("a case that counts differently fails", ReplayBank.Run(bank, settings).First(x => x.Case == "seed-craft-1920x1080").Status, "FAIL");
        File.WriteAllText(caseFile, File.ReadAllText(caseFile).Replace("\"pending\": false", "\"pending\": true"));
        Check("...unless nobody has checked it yet (pending)", ReplayBank.Run(bank, settings).First(x => x.Case == "seed-craft-1920x1080").Status, "pending");
        ReplayBank.Run(bank, settings, record: "seed-craft-1920x1080");
        Check("--record writes what this version counts", ReplayBank.Run(bank, settings).First(x => x.Case == "seed-craft-1920x1080").Status, "pass");
        Check("compare: an item counted that nobody expected", string.Join(";", ReplayBank.Compare(JsonNode.Parse("{\"items\":{}}")!.AsObject(), JsonNode.Parse("{\"items\":{\"X\":1}}")!.AsObject())), "X: not expected, counted 1");

        Console.WriteLine("A site without the accuracy routes (the live one until its update):");
        using (var site = new NotFoundSite())
        {
            var u = new UploadSettings { Endpoint = $"http://127.0.0.1:{site.Port}/v1/runs" };
            RemoteConfig.Current = RemoteConfig.BuiltIn;
            using var http = new System.Net.Http.HttpClient { Timeout = TimeSpan.FromSeconds(10) };
            var fetched = RemoteConfig.FetchAsync(http, RemoteConfig.UrlFor(u)).GetAwaiter().GetResult();
            Check("remote config: no answer, the built-in settings stay", $"{fetched == null} {RemoteConfig.Current.Id}", "True built-in");
            Check("...and updates install as before (nothing held back)", $"{RemoteConfig.Current.AutoInstall(new Version(0, 8, 13), "x").Auto}", "True");
            Accuracy.Reset(); Accuracy.Add("chat_reads", 5);
            var sent = AccuracyClient.SendAsync(u).GetAwaiter().GetResult();
            Check("health report: not sent, and dropped (not retried every run)", $"{sent} {Accuracy.Get("chat_reads")}", "False 0");
            var hints = AccuracyClient.LayoutAsync(u, 1234, 777).GetAwaiter().GetResult();
            Check("layout hints: none", $"{hints == null}", "True");
            var (fok, fnote) = AccuracyClient.SendFramesAsync(u, rd, "test").GetAwaiter().GetResult();
            Check("report pictures: not sent, said plainly", $"{fok} {fnote}", "False the site answered 404");
            Check("the site was asked each time", $"{site.Requests >= 4}", "True");
        }

        CrashLog.DirOverride = null; RemoteConfig.PathOverride = null; RemoteConfig.Current = RemoteConfig.BuiltIn; Accuracy.Reset();
        Console.WriteLine(fails == 0 ? "\nAll accuracy cases passed." : $"\n{fails} accuracy case(s) failed.");
        return fails == 0 ? 0 : 1;
    }

    /// <summary>
    /// `accuracy-sample <dir>`: the payloads exactly as this tracker sends them (accuracy.json, frames.json with real
    /// crops), and `accuracy-sample --config <file>` / `--layout <file>`: a site answer read back as the tracker reads it.
    /// For checking the tracker and the backend against each other.
    /// </summary>
    public static int Sample(Settings settings, List<string> pos, Dictionary<string, string> opts)
    {
        if (opts.TryGetValue("config", out var cf))
        {
            var c = RemoteConfig.Parse(File.ReadAllText(cf));
            Console.WriteLine($"id {c.Id} · scale {c.Scale} · text {c.TargetTextPx} · interval {c.IntervalMs}/{c.FightIntervalMs} · loot {c.LootLeft},{c.LootRight},{c.LootTop},{c.LootBottom} ({c.RegionsId}) · words {string.Join("/", c.CraftPanelWords)} {string.Join("/", c.RewardCollectedWords)} {string.Join("/", c.DistributionWords)} {string.Join("/", c.CraftWindowWords)} · off {string.Join(",", c.Killed)} · rollout {c.RolloutVersion} {c.RolloutPercent}% hold={c.RolloutHold} default={c.DefaultPercent} · legend materials rare {c.LegendMaterialsRare} · ttl {c.TtlHours}");
            return 0;
        }
        if (opts.TryGetValue("layout", out var lf))
        {
            foreach (var hnt in AccuracyClient.ParseHints(File.ReadAllText(lf))) Console.WriteLine($"ui {hnt.Ui} · box {string.Join(",", hnt.Box)} · installs {hnt.Installs}");
            return 0;
        }
        var dir = pos.FirstOrDefault() ?? ".";
        Directory.CreateDirectory(dir);
        CrashLog.DirOverride = Path.Combine(Path.GetTempPath(), "KinahLedger-accuracy-test");
        Accuracy.Reset();
        foreach (var k in Accuracy.Keys) Accuracy.Add(k, 3);
        for (int i = 0; i < 8; i++) Accuracy.AddReading(450.05);
        Accuracy.AddChatLost(12);
        Accuracy.Name("Orichalcurn Ore", 610620013, "snapped"); Accuracy.Name("ctral Greatsword", 110130007, "guessed"); Accuracy.Name("Orichalcm Oar", null, "unknown");
        Accuracy.Layout(new LayoutReport(2560, 1440, 18, LayoutProfile.Fractions(new Rectangle(130, 900, 600, 400), new Rectangle(100, 50, 2560, 1440)), "found", true, false, "b1"));
        var json = JsonSerializer.Serialize(AccuracyPayload.From(Accuracy.Take(), "0123456789abcdef0123456789abcdef"));
        File.WriteAllText(Path.Combine(dir, "accuracy.json"), json);
        // a report folder with a chat, a loot list and a panel picture, as Report a miscount saves one
        var rd = Path.Combine(dir, "report"); Directory.CreateDirectory(Path.Combine(rd, "frames"));
        var game = new Rectangle(0, 0, 2560, 1440);
        var lr = LootPopup.Region(game); var cr = CraftPanel.Region(game);
        using (var b = ReplayBank.DrawLoot(lr, game, new[] { "Orichalcum Ore x2" })) b.Save(Path.Combine(rd, "frames", "loot-1.png"), System.Drawing.Imaging.ImageFormat.Png);
        using (var b = ReplayBank.DrawPanel(cr, game, (0.4, 0.2))) b.Save(Path.Combine(rd, "frames", "craft-1.png"), System.Drawing.Imaging.ImageFormat.Png);
        using (var b = new Bitmap(300, 200)) b.Save(Path.Combine(rd, "frames", "chat-1.png"), System.Drawing.Imaging.ImageFormat.Png);
        File.WriteAllLines(Path.Combine(rd, "frames.jsonl"), new[]
        {
            JsonSerializer.Serialize(new { t = "2026-10-23T10:00:00.0000000Z", k = "chat", f = "frames/chat-1.png", x = 10, y = 900 }),
            JsonSerializer.Serialize(new { t = "2026-10-23T10:00:01.0000000Z", k = "loot", f = "frames/loot-1.png", x = lr.X, y = lr.Y }),
            JsonSerializer.Serialize(new { t = "2026-10-23T10:00:02.0000000Z", k = "craft", f = "frames/craft-1.png", x = cr.X, y = cr.Y }),
        });
        File.WriteAllText(Path.Combine(rd, "game.json"), "{\"x\":0,\"y\":0,\"w\":2560,\"h\":1440}");
        File.WriteAllText(Path.Combine(rd, "counted.jsonl"), JsonSerializer.Serialize(new { t = "2026-10-23T10:00:01.0000000Z", what = "Orichalcum Ore ×2", via = "loot list" }) + "\n");
        var fp = AccuracyClient.BuildFrames(rd, "Report a miscount")!;
        File.WriteAllText(Path.Combine(dir, "frames.json"), JsonSerializer.Serialize(fp));
        Console.WriteLine($"wrote accuracy.json ({json.Length} bytes) and frames.json ({fp.frames.Count} pictures) to {Path.GetFullPath(dir)}");
        CrashLog.DirOverride = null; Accuracy.Reset();
        return 0;
    }

    /// <summary>A local web server that answers every request 404 (an API that doesn't know a route), for the tests above.</summary>
    sealed class NotFoundSite : IDisposable
    {
        readonly System.Net.Sockets.TcpListener _l = new(System.Net.IPAddress.Loopback, 0);
        readonly CancellationTokenSource _cts = new();
        public int Port { get; }
        int _requests;
        public int Requests => Volatile.Read(ref _requests);
        public NotFoundSite()
        {
            _l.Start();
            Port = ((System.Net.IPEndPoint)_l.LocalEndpoint).Port;
            _ = Task.Run(Serve);
        }
        async Task Serve()
        {
            while (!_cts.IsCancellationRequested)
            {
                try
                {
                    using var c = await _l.AcceptTcpClientAsync(_cts.Token);
                    using var ns = c.GetStream();
                    // read the headers, then the body the request says it has, so a POST is never cut off mid-send
                    var got = new List<byte>(); var buf = new byte[8192];
                    int end = -1, length = 0;
                    while (end < 0)
                    {
                        int n = await ns.ReadAsync(buf, _cts.Token); if (n <= 0) break;
                        got.AddRange(buf.Take(n));
                        var head = Encoding.ASCII.GetString(got.ToArray());
                        end = head.IndexOf("\r\n\r\n", StringComparison.Ordinal);
                        if (end >= 0) { var m = System.Text.RegularExpressions.Regex.Match(head[..end], @"content-length:\s*(\d+)", System.Text.RegularExpressions.RegexOptions.IgnoreCase); length = m.Success ? int.Parse(m.Groups[1].Value) : 0; }
                    }
                    int have = end < 0 ? 0 : got.Count - end - 4;
                    while (have < length) { int n = await ns.ReadAsync(buf, _cts.Token); if (n <= 0) break; have += n; }
                    Interlocked.Increment(ref _requests);
                    var body = "{\"ok\":false,\"error\":\"not found\"}";
                    var resp = $"HTTP/1.1 404 Not Found\r\nContent-Type: application/json\r\nContent-Length: {body.Length}\r\nConnection: close\r\n\r\n{body}";
                    await ns.WriteAsync(Encoding.ASCII.GetBytes(resp), _cts.Token);
                }
                catch (Exception) when (!_cts.IsCancellationRequested) { }
                catch (Exception) { return; }
            }
        }
        public void Dispose() { _cts.Cancel(); _l.Stop(); }
    }

    static string Regex(string s, string pattern) => string.Join(",", System.Text.RegularExpressions.Regex.Matches(s, pattern).Select(m => m.Groups[1].Value));

    static string Item(IEnumerable<GameEvent> evs, ItemDictionary items)
    {
        var a = evs.OfType<ItemAcquired>().FirstOrDefault();
        return a == null ? "nothing" : items.NameOf(a.ItemId) + (a.Via.Contains("guessed") ? " (guessed)" : "");
    }

    static string D((GameEvent Event, bool Confirmed, string Note) d, ItemDictionary items) =>
        $"{(d.Event is ItemAcquired a ? items.NameOf(a.ItemId) : "?")} confirmed={d.Confirmed} unconfirmed={d.Event.Unconfirmed}";
}
