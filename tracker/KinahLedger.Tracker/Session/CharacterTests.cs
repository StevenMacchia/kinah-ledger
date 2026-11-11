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
            foreach (var f in Directory.GetFiles(fx, "*.jpg"))
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
        }

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
            Characters.Rename(first, "  Swoly  ");
            Characters.Reset();
            Check("a name, read back after a restart", Characters.Current is { } back ? Characters.Label(back) : "none", "Swoly");

            // the sort: pictures of the shield before and after, the harp during the middle run
            var local = (DateTime u) => u.ToLocalTime();
            var shots = new List<CharacterSort.Shot>
            {
                new(local(d0.AddMinutes(2)), prints.GetValueOrDefault("shield-1") ?? "", "a"), new(local(d0.AddMinutes(5)), prints.GetValueOrDefault("shield-2") ?? "", "b"),
                new(local(d0.AddMinutes(9)), prints.GetValueOrDefault("shield-small") ?? "", "c"),
                new(local(d0.AddHours(1).AddMinutes(1)), prints.GetValueOrDefault("harp-1") ?? "", "d"), new(local(d0.AddHours(1).AddMinutes(4)), prints.GetValueOrDefault("harp-2-glare") ?? "", "e"),
                new(local(d0.AddHours(1).AddMinutes(9)), prints.GetValueOrDefault("harp-1") ?? "", "f"),
                new(local(d0.AddDays(-3)), prints.GetValueOrDefault("knot-1") ?? "", "g"),   // a knot seen twice only: not enough for a character
                new(local(d0.AddDays(-3).AddMinutes(1)), prints.GetValueOrDefault("knot-2") ?? "", "h"),
            };
            var outcome = CharacterSort.Sort(Characters.Current!, "Siel (Elyos)", shots);
            Check("pictures grouped: shield, harp (the knot's two too few)", outcome.Groups.ToString(), "2");
            Check("the harp's run moves to a second character", $"{outcome.Moved} {string.Join(",", outcome.To.Select(x => Characters.Label(x.Who) + ":" + x.Runs))}", "1 Character 2:1");
            Check("...the run with no picture stays with the first", $"{File.ReadAllLines(Path.Combine(cdir, "instances.jsonl")).Length} {File.ReadAllLines(Path.Combine(dir, "characters", "c2", "instances.jsonl")).Length}", "2 1");
            Check("...the second character knows the harp now", Characters.Match(prints.GetValueOrDefault("harp-2-glare") ?? "", "Siel (Elyos)") is { } m ? m.Id : "none", "c2");
            Check("...and the sort runs once", Characters.Sorted ? "sorted" : "not", "sorted");
            Characters.SetCurrent(Characters.All.First(c => c.Id == "c2"));
            Check("switching: the second one's folder is used", Characters.DataDir == Path.Combine(dir, "characters", "c2") ? "c2" : Characters.DataDir, "c2");
        }
        finally { CrashLog.DirOverride = keepDir; Characters.Reset(); try { Directory.Delete(dir, true); } catch (Exception) { } }
        return ok;
    }
}
