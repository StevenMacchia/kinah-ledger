using System.Text.Json;
using KinahLedger.Data;

namespace KinahLedger.Session;

/// <summary>
/// The live feed (0.8.30, direction C), part of parse-test: five lines a tab, newest first, each tab its own; the tab a
/// pickup's activity belongs to; the rate line's one sample a minute, sixty kept; the payload a tapped line files with the
/// miscount report (its label, the line, the pictures within five seconds); and the fix sheet's split at the reward screens
/// applied to a stored record, with the record kept aside. Writes only to a scratch folder.
/// </summary>
public static class LiveFeedTests
{
    public static bool Run(ItemDictionary items, Settings settings)
    {
        bool ok = true;
        void Check(string what, string got, string expect)
        {
            bool pass = got == expect; ok &= pass;
            Console.WriteLine($"  {what,-62} -> {got,-40} {(pass ? "ok" : "MISMATCH, expected " + expect)}");
        }
        Console.WriteLine("\nLive feed: five lines a tab, the rate line, a tapped line's report, the split (0.8.30):");
        var t = new DateTime(2026, 10, 24, 17, 51, 0, DateTimeKind.Utc);

        // the feed: seven farming lines, two crafts, one run line; Recent gives a tab's five newest, newest first
        var feed = new LiveFeed();
        for (int i = 0; i < 7; i++) feed.Add(new FeedLine(t.AddSeconds(i), FeedTab.Farming, "item", "Thick hide", 1 + i, Where: "Farming · Western Cantas Valley"));
        feed.Add(new FeedLine(t.AddSeconds(3), FeedTab.Crafting, "craft", "Diamond Powder", 1, Ok: true, Where: "Crafting"));
        feed.Add(new FeedLine(t.AddSeconds(9), FeedTab.Crafting, "craft", "Orichalcum Greaves", 1, Ok: false, Where: "Crafting"));
        feed.Add(new FeedLine(t.AddSeconds(20), FeedTab.Events, "kinah", "", 2_410, Where: "Expedition · Urugugu Canyon"));
        var farm = feed.Recent(FeedTab.Farming);
        Check("five lines, newest first", string.Join(",", farm.Select(l => l.Amount)), "7,6,5,4,3");
        Check("a tab shows only its own", string.Join(" | ", feed.Recent(FeedTab.Crafting).Select(l => l.Text)), "Orichalcum Greaves failed | Diamond Powder worked");
        Check("the newest line of any tab (the panel)", feed.Newest()?.Text ?? "none", "2,410 Kinah");
        // two lines with the same time: the one counted later is newer
        feed.Add(new FeedLine(t.AddSeconds(6), FeedTab.Farming, "kinah", "", 1_192, "Cantas Raider", Via: "chat"));
        Check("same second: the later one first", feed.Recent(FeedTab.Farming, 2).Select(l => l.Text).Aggregate((a, b) => a + " | " + b), "1,192 Kinah · Cantas Raider | Thick hide ×7");
        for (int i = 0; i < LiveFeed.Keep + 20; i++) feed.Add(new FeedLine(t.AddMinutes(1).AddSeconds(i), FeedTab.Farming, "item", "Spiritstone", 1));
        Check("kept to 200, the other tabs' lines age out with them", $"{feed.Recent(FeedTab.Farming, 500).Count} {feed.Recent(FeedTab.Crafting).Count}", "200 0");
        Check("activities to tabs", string.Join(",", new[] { "Farming · Western Cantas Valley", "Expedition · Urugugu Canyon", "Event · Hidden Lugi (Shugo Festival)", "World boss · Ragnarok", "Crafting", "Reward screen · Daily", "Town · Sanctum", "" }.Select(w => LiveFeed.TabOf(w).ToString()[0])), "F,E,E,E,C,F,F,F");

        // the rate line: one sample a minute, the later one in a minute wins, sixty kept, drawn lowest at the bottom
        var rl = new RateLine();
        rl.Add(t, 100); rl.Add(t.AddSeconds(30), 120); rl.Add(t.AddMinutes(1), 200);
        Check("a minute's later sample replaces the first", string.Join(",", rl.Samples.Select(s => s.Rate)), "120,200");
        Check("drawn from the left edge to the right, low to high", string.Join(" ", rl.Points(200, 40).Select(p => $"{p.X}:{p.Y}")), "0:38 200:2");
        for (int i = 2; i < 80; i++) rl.Add(t.AddMinutes(i), 300 + i);
        Check("sixty one-minute samples kept, oldest first", $"{rl.Samples.Count} {rl.Samples[0].Minute:HH:mm} {rl.Samples[^1].Minute:HH:mm}", "60 18:11 19:10");
        var flat = new RateLine(); flat.Add(t, 5); flat.Add(t.AddMinutes(1), 5);
        Check("a flat rate sits in the middle", string.Join(" ", flat.Points(200, 40).Select(p => p.Y)), "20 20");

        // a tapped line's report: the label, the line as it was, the report's pictures within five seconds of it
        var line = new FeedLine(t.AddSeconds(11), FeedTab.Farming, "kinah", "", 1_192, "Cantas Raider", Via: "chat", Where: "Farming · Western Cantas Valley");
        var m = new MiscountLine("wrong_amount", line);
        Check("what the report's summary says it was filed for", m.Why, $"Wrong amount: {line.At.ToLocalTime():HH:mm:ss} 1,192 Kinah · Cantas Raider (chat)");
        var frames = new[]
        {
            JsonSerializer.Serialize(new { t = t.AddSeconds(1).ToString("O"), k = "loot", f = "frames/loot-a.png", x = 0, y = 0 }),
            JsonSerializer.Serialize(new { t = t.AddSeconds(9).ToString("O"), k = "chat", f = "frames/chat-b.png", x = 0, y = 0 }),
            JsonSerializer.Serialize(new { t = t.AddSeconds(15).ToString("O"), k = "loot", f = "frames/loot-c.png", x = 0, y = 0 }),
            "not json",
        };
        using (var d = JsonDocument.Parse(m.Json(frames)))
        {
            var r = d.RootElement;
            Check("line.json: label, line and its pictures", $"{r.GetProperty("label").GetString()} · {r.GetProperty("line").GetProperty("text").GetString()} · {r.GetProperty("line").GetProperty("via").GetString()} · {string.Join(",", r.GetProperty("pictures").EnumerateArray().Select(p => p.GetString()))}",
                "wrong_amount · 1,192 Kinah · Cantas Raider · chat · frames/chat-b.png,frames/loot-c.png");
        }
        using (var d = JsonDocument.Parse(new MiscountLine("missing", null).Json(frames)))
            Check("Missing a pickup: no line, no pictures picked", $"{new MiscountLine("missing", null).Why} · {d.RootElement.GetProperty("line").ValueKind} · {d.RootElement.GetProperty("pictures").GetArrayLength()}", "Missing a pickup · Null · 0");
        Check("the four choices", string.Join(" / ", MiscountLine.Choices.Select(c => c.Text)), "Wrong item / Wrong amount / Didn't happen / Missing a pickup");

        // the split: a 37:04 record whose five reward screens were kept; each screen read slot by slot (within a minute)
        var keepDir = CrashLog.DirOverride;
        var dir = TestTemp.Path("kl-livefeed-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(dir);
        CrashLog.DirOverride = dir;
        RunFixes.ResetKept();
        try
        {
            var start = t.AddHours(-2);
            var rec = new InstanceRun { Zone = "Urugugu Canyon", Kind = "Expedition", Start = start, LastLoot = start.AddSeconds(2224), Kinah = 3_390_000, FromPicture = true, Tables = 5, TableAt = start.AddSeconds(432) };
            rec.Items[1] = 90;
            var lines = new List<RunFixes.ScreenLine>();
            var at = start;
            foreach (var (len, kinah) in new[] { (432, 682_000L), (460, 655_000L), (448, 701_000L), (425, 640_000L), (459, 712_000L) })
            {
                at = at.AddSeconds(len);
                lines.Add(new RunFixes.ScreenLine(at, "Expedition · Urugugu Canyon", "kinah", 0, "", kinah));
                lines.Add(new RunFixes.ScreenLine(at.AddSeconds(3), "Expedition · Urugugu Canyon", "item", 1, "Spiritstone Powder", 18));
            }
            lines.Add(new RunFixes.ScreenLine(start.AddSeconds(500), "Expedition · Night Dew Cave", "kinah", 0, "", 9_999));   // another place: not this record's
            var screens = RunFixes.ScreensIn(rec, lines);
            Check("five reward screens found inside the record", $"{screens.Count} {screens.Sum(s => s.Kinah):N0} {screens.Sum(s => s.Items.GetValueOrDefault(1))}", "5 3,390,000 90");
            var parts = RunFixes.Split(rec, screens);
            Check("split: five runs, each from just after the last screen", string.Join(" ", parts.Select(p => $"{TimeSpan.FromSeconds(Math.Round(p.Seconds)):m\\:ss}")), "7:12 7:39 7:27 7:04 7:38");
            Check("one screen is nothing to split at", RunFixes.Split(rec, screens.Take(1).ToList()).Count.ToString(), "0");
            File.WriteAllText(Path.Combine(dir, "instances.jsonl"), JsonSerializer.Serialize(rec) + "\n");
            var log = new InstanceLog();
            var stored = log.Snapshot().Single();
            bool replaced = log.ReplaceRun(stored, parts);
            Check("applied: five runs in the list and the file, the record kept aside", $"{replaced} {log.Snapshot().Count} {File.ReadAllLines(Path.Combine(dir, "instances.jsonl")).Length} {File.ReadAllLines(Path.Combine(dir, "instances.repaired.jsonl")).Length}", "True 5 5 1");
            var again = new InstanceLog();
            Check("after a restart the five stay, none flagged", $"{again.Snapshot().Count} {again.Snapshot().Count(r => r.OutOfAverages)}", "5 0");
            // Keep as one: the flag stays off after a restart
            var flaggedRec = new InstanceRun { Zone = "Night Dew Cave", Kind = "Expedition", Start = start, LastLoot = start.AddMinutes(30), Kinah = 1, Suspect = "looks like several runs" };
            RunFixes.KeepAsOne(flaggedRec);
            RunFixes.ResetKept();
            var reloaded = new InstanceRun { Zone = "Night Dew Cave", Kind = "Expedition", Start = start, LastLoot = start.AddMinutes(30), Kinah = 1, Suspect = "looks like several runs" };
            RunFixes.ApplyKept(new[] { reloaded });
            Check("Keep as one is remembered", $"'{reloaded.Suspect}' {reloaded.OutOfAverages}", "'' False");
            // the feed's sink keeps only reward screen lines counted into a run
            RunFixes.Record(new FeedLine(t, FeedTab.Events, "kinah", "", 604_000, Via: "reward table", Where: "Expedition · Urugugu Canyon"));
            RunFixes.Record(new FeedLine(t, FeedTab.Events, "kinah", "", 2_410, Via: "chat", Where: "Expedition · Urugugu Canyon"));
            RunFixes.Record(new FeedLine(t, FeedTab.Farming, "item", "Thick hide", 1, Via: "reward table"));
            Check("kept for a split: reward screen lines of a run only", string.Join(",", RunFixes.Load().Select(l => l.Amount)), "604000");
        }
        finally { CrashLog.DirOverride = keepDir; RunFixes.ResetKept(); try { Directory.Delete(dir, true); } catch (Exception) { } }
        return ok;
    }
}
