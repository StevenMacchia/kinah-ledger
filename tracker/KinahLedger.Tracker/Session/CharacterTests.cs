using System.Drawing;
using System.Text.Json;
using KinahLedger.Data;
using KinahLedger.Ocr;

namespace KinahLedger.Session;

/// <summary>
/// Characters (Session/Characters.cs), part of parse-test. The class icon read from real pictures
/// (tracker/tests/characters: the bottom band of Steven's run pictures of 2026-11-05 to 11, three characters, scaled to
/// a 1080-high window, one to 810): the same icon matches across lights and sizes, two classes never do, a reward
/// screen has none. Then the spotter (steady readings, a switch, a new one), the book (the first character takes over
/// the numbers kept so far, each one's own folder, the current one kept for a shared icon) and the sort of old runs by
/// the pictures taken in them. Writes only to a scratch folder.
/// </summary>
public static class CharacterTests
{
    static string? FixtureDir()
    {
        for (var d = new DirectoryInfo(AppContext.BaseDirectory); d != null; d = d.Parent)
        {
            var p = Path.Combine(d.FullName, "tracker", "tests", "characters");
            if (Directory.Exists(p)) return p;
            p = Path.Combine(d.FullName, "tests", "characters");
            if (Directory.Exists(p)) return p;
        }
        return null;
    }

    /// <summary>A read name over the name typed before it: the typed one is dropped by hand first (as Rename to "" would), then the reading sets it.</summary>
    static string RenameRead(Character c) { Characters.Rename(c, ""); Characters.SetReadName(c, "Swoles"); return Characters.Label(Characters.Current!); }

    public static bool Run(ItemDictionary items, Settings settings)
    {
        bool ok = true;
        void Check(string what, string got, string expect)
        {
            bool pass = got == expect; ok &= pass;
            Console.WriteLine($"  {what,-62} -> {got,-40} {(pass ? "ok" : "MISMATCH, expected " + expect)}");
        }
        Console.WriteLine("\nCharacters: the class icon between the health and mana bars, and the numbers kept per character:");

        // ---- the icon, from real pictures ----
        var fx = FixtureDir();
        var prints = new Dictionary<string, string?>();
        if (fx == null) Check("the pictures in tracker/tests/characters", "missing", "found");
        else
        {
            foreach (var f in Directory.GetFiles(fx, "*.jpg").Where(f => !Path.GetFileName(f).StartsWith("plate-")))
            {
                var name = Path.GetFileNameWithoutExtension(f);
                int h = int.Parse(name[(name.LastIndexOf("-h", StringComparison.Ordinal) + 2)..]);
                using var bmp = new Bitmap(f);
                prints[name[..name.LastIndexOf("-h", StringComparison.Ordinal)]] = CharacterBadge.Read(bmp, h)?.Print;
            }
            string P(string k) => prints.TryGetValue(k, out var p) ? p ?? "" : "";
            string Same(string a, string b) => CharacterBadge.Alike(P(a), P(b)) >= CharacterBadge.SameIcon ? "same" : "different";
            Check("the medallion found in all seven pictures that show it", prints.Count(kv => kv.Key != "none-event-rewards" && kv.Value != null).ToString(), "7");
            Check("an event's reward screen hides it: no icon", P("none-event-rewards") == "" ? "none" : "read", "none");
            Check("shield: a reward table and a run five days apart", Same("shield-1", "shield-2"), "same");
            Check("shield: the same picture at 1920 x 810", Same("shield-2", "shield-small"), "same");
            Check("harp: plain and under a glow", Same("harp-1", "harp-2-glare"), "same");
            Check("knot: two runs", Same("knot-1", "knot-2"), "same");
            var classes = new[] { "shield-1", "harp-1", "knot-1", "shield-small", "harp-2-glare", "knot-2" };
            int crossed = 0;
            for (int i = 0; i < classes.Length; i++)
                for (int j = 0; j < classes.Length; j++)
                    if (classes[i].Split('-')[0] != classes[j].Split('-')[0] && Same(classes[i], classes[j]) == "same") crossed++;
            Check("two classes' icons never match (every pair)", crossed.ToString(), "0");

            // the nameplate over the head (the plate cut from two run pictures, 5120 x 2160, kept lossless: the green name over grass is thin)
            string Plate(string file)
            {
                using var bmp = new Bitmap(Path.Combine(fx, file));
                return CharacterName.ReadAsync(bmp, 5120, 2160).GetAwaiter().GetResult() ?? "none";
            }
            Check("the name over the head, teal in a group", Plate("plate-swoles-w5120h2160.png"), "Swoles");
            Check("...and green over grass, a title above it", Plate("plate-swoly-w5120h2160.png").ToLowerInvariant(), "swoly");
        }
        Check("an OCR line as a name: one word, letters and digits", string.Join(" ", new[] { "Swoly", "Swot'", "Has Exceptional Skill", "9,398", "[Swoles]", "S" }.Select(x => CharacterName.Clean(x) ?? "-")), "Swoly Swot - - Swoles -");
        Check("the readings settle on the name the OCR repeats", Characters.Settle(new[] { "Swot", "Swoly", "swoly", "Swoyy", "Swoly", "QOQ", "Swoly" }, 4) ?? "none", "Swoly");
        Check("...not before four of them", Characters.Settle(new[] { "Swot", "Swoly", "swoly", "Swoly" }, 4) ?? "none", "none");
        Check("...nor when another is nearly as common", Characters.Settle(new[] { "Swoly", "Swoly", "Swoly", "Swoly", "Cube", "Cube", "Cube" }, 4) ?? "none", "none");

        // ---- the spotter ----
        var shield = new Character { Id = "c1", Server = "Siel (Elyos)", Badges = { prints.GetValueOrDefault("shield-1") ?? "" } };
        var harp = new Character { Id = "c2", Server = "Siel (Elyos)", Badges = { prints.GetValueOrDefault("harp-1") ?? "" } };
        var book = new List<Character> { shield, harp };
        string Run(CharacterSpotter sp, string current, params string?[] reads)
        {
            var t = new DateTime(2026, 11, 11, 18, 0, 0, DateTimeKind.Utc);
            var said = new List<string>();
            foreach (var r in reads)
            {
                t = t.AddSeconds(5);
                var v = sp.Observe(t, r == null ? null : prints.GetValueOrDefault(r), "Siel (Elyos)", book, current);
                said.Add(v is { } x ? x.Kind + (x.Character != null ? ":" + x.Character.Id : "") : "-");
            }
            return string.Join(" ", said);
        }
        Check("three steady readings say who it is", Run(new(), "c1", "shield-2", "shield-2", "shield-small"), "- - Same:c1");
        Check("a reward screen in between neither breaks nor adds", Run(new(), "c1", "shield-2", null, "shield-2", "shield-small"), "- - - Same:c1");
        Check("the harp after the shield: another character", Run(new(), "c1", "shield-2", "harp-1", "harp-2-glare", "harp-1"), "- - - Switch:c2");
        Check("an icon never seen: new only after six readings", Run(new(), "c1", "knot-1", "knot-2", "knot-1", "knot-2", "knot-1", "knot-2"), "- - - - - New");
        Check("another server's shield is another character", new CharacterSpotter().Observe(DateTime.UtcNow, shield.Badges[0], "Israphel (Asmodian)", book, null) == null
            && Characters.Match(shield.Badges[0], "Israphel (Asmodian)", book, null) == null ? "none" : "matched", "none");
        // two characters of one class: the one counted for keeps counting; one split off by hand takes the icon on screen
        var twin = new Character { Id = "c3", Server = "Siel (Elyos)", Badges = { prints.GetValueOrDefault("shield-2") ?? "" } };
        var twins = new List<Character> { shield, harp, twin };
        Check("one class twice: the current one kept (c3, then c1)", $"{Characters.Match(shield.Badges[0], "Siel (Elyos)", twins, "c3")?.Id} {Characters.Match(shield.Badges[0], "Siel (Elyos)", twins, "c1")?.Id}", "c3 c1");
        var bare = new Character { Id = "c4", Server = "Siel (Elyos)" };
        var sp4 = new CharacterSpotter();
        CharacterSpotter.Verdict? v4 = null;
        foreach (var r in new[] { "shield-2", "shield-2", "shield-2" }) v4 = sp4.Observe(DateTime.UtcNow, prints.GetValueOrDefault(r), "Siel (Elyos)", new List<Character> { shield, bare }, "c4");
        Check("a character split off before its icon was read takes it", v4 is { } b4 ? $"{b4.Kind}:{b4.Character?.Id}" : "-", "Same:c4");

        // ---- the book, the folders, the first character taking over ----
        var keepDir = CrashLog.DirOverride;
        var dir = TestTemp.Path("kl-characters-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(dir);
        CrashLog.DirOverride = dir; Characters.Reset();
        try
        {
            Check("before any character: the numbers in the top folder", Characters.DataDir == dir ? "top" : Characters.DataDir, "top");
            Check("...and an upload names no character", KinahLedger.Upload.CharacterPayload.Now() == null ? "none" : "some", "none");
            File.WriteAllText(Path.Combine(dir, "today.json"), "{}");
            File.WriteAllText(Path.Combine(dir, "goals.json"), "[]");
            File.WriteAllText(Path.Combine(dir, "prices.json"), "{}");
            var run = (string zone, DateTime start) => JsonSerializer.Serialize(new InstanceRun { Zone = zone, Kind = "Expedition", Start = start, LastLoot = start.AddMinutes(8), Kinah = 1 });
            var d0 = new DateTime(2026, 11, 9, 18, 0, 0, DateTimeKind.Utc);
            File.WriteAllLines(Path.Combine(dir, "instances.jsonl"), new[] { run("Fire Temple", d0), run("Bakarmas Subspace", d0.AddHours(1)), run("Auldor's Sanctum", d0.AddHours(2)) });
            var first = Characters.Add(prints.GetValueOrDefault("shield-1") ?? "", "Siel (Elyos)");
            Characters.Adopt(first);
            var cdir = Path.Combine(dir, "characters", "c1");
            Check("the first one takes over today, goals and runs", string.Join(" ", new[] { "today.json", "goals.json", "instances.jsonl" }.Select(f => File.Exists(Path.Combine(cdir, f)) ? "moved" : "left")), "moved moved moved");
            Check("...prices stay shared", File.Exists(Path.Combine(dir, "prices.json")) ? "shared" : "moved", "shared");
            Check("...and its folder is where the numbers go now", $"{Characters.DataDir == cdir} {DayStore.PathFor() == Path.Combine(cdir, "today.json")} {Quests.PathFor().StartsWith(cdir)}", "True True True");
            Check("the book is kept: characters.json", File.Exists(Path.Combine(dir, "characters.json")) ? Characters.Label(Characters.Current!) : "none", "Character 1");
            Check("an upload says which character, by its slot before it has a name", JsonSerializer.Serialize(KinahLedger.Upload.CharacterPayload.Now()), "{\"id\":\"c1\"}");
            Characters.Rename(first, "  Swoly  ");
            Characters.Reset();
            Check("a name, read back after a restart", Characters.Current is { } back ? Characters.Label(back) : "none", "Swoly");
            Check("...and the upload carries it, the run's JSON too", $"{JsonSerializer.Serialize(KinahLedger.Upload.CharacterPayload.Now())} {(JsonSerializer.Serialize(KinahLedger.Upload.RunPayload.RunsOnly("na_e", "Siel (Elyos)", null, new())).Contains("\"character\":{\"id\":\"c1\",\"name\":\"Swoly\"}") ? "in the run" : "not in the run")}", "{\"id\":\"c1\",\"name\":\"Swoly\"} in the run");

            Check("a typed name is never read over", $"{Characters.SetReadName(Characters.Current!, "Swoles")} {Characters.Label(Characters.Current!)}", "False Swoly");
            Check("...a read one is, by a reading that settles", $"{Characters.SetReadName(Characters.Current!, "Swoly") || true} {RenameRead(Characters.Current!)}", "True Swoles");
        }
        finally { CrashLog.DirOverride = keepDir; Characters.Reset(); try { Directory.Delete(dir, true); } catch (Exception) { } }

        // the place pass, as on Steven's PC on 2026-11-11: 0.10.51 saw the harp first (an alt, Swoles), which took all
        // the history; the shield (Swoly) has the most pictures, so the history from before and its runs go to it
        dir = TestTemp.Path("kl-characters-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(dir);
        CrashLog.DirOverride = dir; Characters.Reset();
        try
        {
            var d0 = DateTime.UtcNow.AddDays(-2);
            var run = (string zone, DateTime start) => JsonSerializer.Serialize(new InstanceRun { Zone = zone, Kind = "Expedition", Start = start, LastLoot = start.AddMinutes(8), Kinah = 1 });
            File.WriteAllText(Path.Combine(dir, "today.json"), "{}");
            File.WriteAllText(Path.Combine(dir, "days.json"), """{"Version":1,"Since":"2026-10-29","Days":[{"Day":"2026-10-29","Made":500000},{"Day":"2026-11-10","Made":900000}]}""");
            File.WriteAllText(Path.Combine(dir, "quests.json"), """{"Version":2,"AllTimePoints":4000,"Done":[{"Key":"a","Kind":"material","Horizon":"day","Subject":"Ore","Target":10,"Tier":1,"Points":3,"DoneAtUtc":"2026-11-09T10:00:00Z","Scope":"day"}],"Pins":[],"AllTime":{}}""");
            File.WriteAllLines(Path.Combine(dir, "instances.jsonl"), new[] { run("Fire Temple", d0.AddDays(-10)), run("Fire Temple", d0), run("Bakarmas Subspace", d0.AddHours(1)) });
            var harpie = Characters.Add(prints.GetValueOrDefault("harp-1") ?? "", "Siel (Elyos)");
            Characters.Adopt(harpie);
            // 0.10.51's sort had already made the shield a character, and it counted into its own folder since
            var shieldC = Characters.Add(prints.GetValueOrDefault("shield-1") ?? "", "Siel (Elyos)");
            var sdir = Characters.DirOf(shieldC); Directory.CreateDirectory(sdir);
            File.WriteAllText(Path.Combine(sdir, "days.json"), """{"Version":1,"Since":"2026-11-11","Days":[{"Day":"2026-11-11","Made":70000}]}""");
            File.WriteAllText(Path.Combine(sdir, "quests.json"), """{"Version":2,"AllTimePoints":12,"Done":[{"Key":"b","Kind":"material","Horizon":"day","Subject":"Ore","Target":10,"Tier":1,"Points":3,"DoneAtUtc":"2026-11-11T23:00:00Z","Scope":"day"}],"Pins":[],"AllTime":{}}""");
            File.WriteAllText(Path.Combine(sdir, "today.json"), "{}");
            Check("0.10.51's first character took everything; the pass is due", $"{Characters.Adopter?.Id} {Characters.NeedsPlacing}", "c1 True");
            var local = (DateTime u) => u.ToLocalTime();
            string Pr(string k) => prints.GetValueOrDefault(k) ?? "";
            var shots = new List<CharacterSort.Shot>
            {
                new(local(d0.AddMinutes(2)), Pr("shield-1"), "a", "Swoly"), new(local(d0.AddMinutes(5)), Pr("shield-2"), "b", "Swot"),
                new(local(d0.AddMinutes(9)), Pr("shield-small"), "c", "swoly"), new(local(d0.AddDays(-1)), Pr("shield-2"), "d"),
                new(local(d0.AddHours(1).AddMinutes(1)), Pr("harp-1"), "e", "Swoles"), new(local(d0.AddHours(1).AddMinutes(4)), Pr("harp-2-glare"), "f", "Swoles"),
                new(local(d0.AddHours(1).AddMinutes(9)), Pr("harp-1"), "g"),
                new(local(d0.AddDays(-3)), Pr("knot-1"), "h"), new(local(d0.AddDays(-3).AddMinutes(1)), Pr("knot-2"), "i"),   // the knot twice: too few
            };
            var o = CharacterSort.Place(harpie, "Siel (Elyos)", shots);
            var main = o.Main!;
            Check("pictures grouped: shield, harp (the knot's two too few)", o.Groups.ToString(), "2");
            Check("the main character: the shield, most pictures", $"{main.Id} {Characters.Label(main)}", "c2 Swoly");
            Check("names read from the pictures (the slips outvoted)", string.Join(", ", o.Named.Select(x => x.Who.Id + "=" + x.Name).OrderBy(x => x)), "c1=Swoles, c2=Swoly");
            string Runs(string id) { var f = Path.Combine(dir, "characters", id, "instances.jsonl"); return File.Exists(f) ? File.ReadAllLines(f).Length.ToString() : "0"; }
            Check("runs: the shield's and the one from before pictures to Swoly", $"{o.Moved} c1:{Runs("c1")} c2:{Runs("c2")}", "2 c1:1 c2:2");
            Check("days, today, quest points to Swoly; none left with the harp", string.Join(" ", new[] { "today.json", "days.json", "quests.json" }.Select(f => File.Exists(Path.Combine(dir, "characters", "c2", f)) && !File.Exists(Path.Combine(dir, "characters", "c1", f)) ? "c2" : "?")), "c2 c2 c2");
            var days = DayHistory.Load(Path.Combine(sdir, "days.json"));
            Check("...days joined: the old ones and Swoly's own since", $"{days.Days.Count} {days.Since} {string.Join(",", days.Days.Select(d => d.Made))}", "3 2026-10-29 500000,900000,70000");
            var ql = Quests.Load(Path.Combine(sdir, "quests.json"));
            Check("...quest points added up, both quests kept", $"{ql.AllTimePoints} {ql.Done.Count}", "4012 2");
            Check("...today stays Swoly's own, the old count set aside", $"{File.Exists(Path.Combine(sdir, "today.json"))} {File.Exists(Path.Combine(sdir, "today.json.from-c1"))}", "True True");
            Check("...and the pass is done, once", Characters.NeedsPlacing ? "due" : "done", "done");
            Check("the harp is still the one counted for", Characters.Current?.Id ?? "none", "c1");
            Characters.Reset();
            var v = new[] { "Swot", "Swoly", "swoly", "Swoyy", "Swoly" };
            var c3 = Characters.Add(Pr("knot-1"), "Siel (Elyos)");
            Check("live readings name a new character once they settle", string.Join(" ", v.Append("Swoly").Select(x => Characters.VoteName(c3, x) ? "named" : "-")) + " " + Characters.Label(Characters.All.First(c => c.Id == c3.Id)), "- - - - - named Swoly");
        }
        finally { CrashLog.DirOverride = keepDir; Characters.Reset(); try { Directory.Delete(dir, true); } catch (Exception) { } }
        return ok;
    }
}
