using System.Drawing;
using KinahLedger.Data;
using KinahLedger.Ocr;

namespace KinahLedger.Session;

/// <summary>
/// The reward picture's second witnesses (2026-10-25), part of parse-test: gear twins on one icon settled by the player's
/// faction (Data/GearFactions.cs) and by the chat's line in those seconds, and a count the chat's own line contradicts
/// not read (Session/TableWitness.cs). Built from Steven's 2026-10-24 records and trace.log (the chat lines as read, players'
/// lines replaced by fillers). Writes only to a scratch folder.
/// </summary>
public static class TableWitnessTests
{
    public static bool Run(ItemDictionary items, Settings settings)
    {
        bool ok = true;
        void Check(string what, string got, string expect)
        {
            bool pass = got == expect; ok &= pass;
            Console.WriteLine($"  {what,-62} -> {got,-40} {(pass ? "ok" : "MISMATCH, expected " + expect)}");
        }
        Console.WriteLine("\nThe reward picture's second witnesses: gear twins by faction and chat, counts the chat contradicts (2026-10-25):");
        string Unb(string n) => System.Text.RegularExpressions.Regex.Replace(n, @"\s*\(bound\)\s*$", "", System.Text.RegularExpressions.RegexOptions.IgnoreCase).Trim();

        // ---- a. the faction list ----
        Check("faction list: names, each side", $"{GearFactions.Count} {GearFactions.Of("Forgotten Helm")} {GearFactions.Of("Shade Helm")} {GearFactions.Of("Elyos Chanter's Breastplate")} {GearFactions.Of("Worn Bow") ?? "-"}",
            "474 Elyos Asmodian Elyos -");
        Check("the server's faction", $"{GearFactions.OfServer("Siel (Elyos)")} {GearFactions.OfServer("Israphel (Asmodian)")} {GearFactions.OfServer("") ?? "-"}", "Elyos Asmodian -");
        // every name on the list is an item of the list's gear, and in each pair on one icon and grade the Elyos one has the
        // lower id (the guess the list never relies on, checked here against it: 237 of 237 when it was built)
        var gear = items.AllWithIcons().Where(i => items.IsGear(i.Id)).ToList();
        var allNames = gear.Select(i => Unb(i.Name)).Where(n => GearFactions.Of(n) != null).Distinct(StringComparer.OrdinalIgnoreCase).Count();
        Check("...every name on it is gear on the item list", $"{allNames}", "474");
        int pairs = 0, lower = 0;
        foreach (var g in gear.GroupBy(i => IconIndex.KeyOf(i.Icon) + "|" + i.Grade))
        {
            var ns = g.Select(i => Unb(i.Name)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            if (ns.Count != 2 || ns.Any(n => GearFactions.Of(n) == null)) continue;
            var ely = ns.Single(n => GearFactions.Of(n) == "Elyos"); var asm = ns.Single(n => GearFactions.Of(n) == "Asmodian");
            pairs++;
            if (g.Where(i => Unb(i.Name) == ely).Min(i => i.Id) < g.Where(i => Unb(i.Name) == asm).Min(i => i.Id)) lower++;
        }
        Check("...pairs on one icon, the Elyos id the lower in each", $"{pairs} {lower}", "237 237");
        // the 35 pieces of unknown gear in Steven's runs of 2026-10-24 (instances.jsonl GearNotes): 31 are twins the list
        // knows (Elder Gloves / Relic Collector Gloves too: Elder drops on Elyos maps, 98 to 1); Worn Bow / Training Bow (3)
        // and Dawn Bracelet / Old Mercenary Commander Bracelet are not on it
        var notes = new[]
        {
            "Elder Gloves / Relic Collector Gloves", "Rainy Forest Boots / Tempest Boots", "Forgotten Boots / Shade Boots", "Riverside Cloak / Phantasm Cloak",
            "Comet Breastplate / Luminous Breastplate", "Riverside Gloves / Phantasm Gloves", "Forgotten Boots / Shade Boots", "Riverside Longsword / Phantasm Longsword",
            "Riverside Breastplate / Phantasm Breastplate", "Comet Pauldrons / Luminous Pauldrons", "Riverside Guard / Phantasm Guard", "Forgotten Helm / Shade Helm",
            "Ethereal Helm / Chaos Helm", "Riverside Mace / Phantasm Mace", "Forgotten Greaves / Shade Greaves", "Riverside Dagger / Phantasm Dagger",
            "Forgotten Boots / Shade Boots", "Riverside Bow / Phantasm Bow", "Rainy Forest Orb / Tempest Orb", "Forgotten Staff / Shade Staff",
            "Forgotten Helm / Shade Helm", "Illusion Orb / Destruction Orb", "Comet Pauldrons / Luminous Pauldrons", "Elyos Chanter's Breastplate / Asmodian Chanter's Breastplate",
            "Forgotten Orb / Shade Orb", "Ethereal Boots / Chaos Boots", "Forgotten Longsword / Shade Longsword", "Forgotten Greaves / Shade Greaves",
            "Rainy Forest Orb / Tempest Orb", "Riverside Spellbook / Phantasm Spellbook", "Forgotten Helm / Shade Helm", "Worn Bow / Training Bow",
            "Dawn Bracelet / Old Mercenary Commander Bracelet", "Worn Bow / Training Bow", "Worn Bow / Training Bow",
        };
        var picks = notes.Select(n => GearFactions.Pick(n.Split(" / "), "Elyos")).ToList();
        Check("2026-10-24's 35 unknown gear: settled by the Elyos faction", $"{picks.Count(p => p != null)} of {notes.Length}", "31 of 35");
        Check("...each the first name, the one the chat named live", $"{notes.Zip(picks).Count(x => x.Second != null && x.Second == x.First.Split(" / ")[0])}", "31");
        Check("...an Asmodian character: the other one; no server: none", $"{GearFactions.Pick(new[] { "Forgotten Helm", "Shade Helm" }, "Asmodian")} {GearFactions.Pick(new[] { "Forgotten Helm", "Shade Helm" }, null) ?? "-"}", "Shade Helm -");
        Check("...a name the list lacks among them: none", GearFactions.Pick(new[] { "Forgotten Helm", "Shade Helm", "Worn Bow" }, "Elyos") ?? "-", "-");

        // ---- b. the judge alone, on the lines as the chat gave them ----
        var t18 = new DateTime(2026, 10, 24, 22, 6, 51, DateTimeKind.Utc);   // the cards filed, 18:06:51 local
        TableWitness.Slot Named(string name, int n, bool g = false, params string[] twins) => new(0, "Common", name, n, 1, false, g, twins.Length > 0 ? twins : new[] { name }, $"Common x{n}");
        TableWitness.Slot Twin(params string[] names) => new(0, "Common", "", 1, 0, false, true, names, $"Common gear, one of {string.Join(" / ", names)}",
            names.ToDictionary(n => n, n => ((long)(100 + Array.IndexOf(names, n)), false)));
        var burst = t18.AddSeconds(3.39);   // 18:06:54.39, the read the screen's closing showed
        var lines18 = new List<TableWitness.Line>
        {
            new(burst, "Forgotten Helm", 1), new(burst, "Ethereal Boots", 1), new(burst, "Radiant Odyle", 1), new(burst, "Pure Odyle", 2),
            new(burst, "Fine Odyle", 1), new(burst, "Odyle", 9), new(burst, "Refining Stone", 6), new(burst, "Soul Codex", 3),
        };
        var cards18 = new List<TableWitness.Slot> { Named("Soul Codex", 1), Named("Soul Codex", 1), Named("Soul Codex", 41), Named("Radiant Odyle", 1), Named("Pure Odyle", 1), Named("Pure Odyle", 1), Named("Fine Odyle", 1) };
        cards18.AddRange(Enumerable.Range(0, 8).Select(_ => Named("Odyle", 1)));
        cards18.AddRange(Enumerable.Range(0, 6).Select(_ => Named("Refining Stone", 1)));
        cards18.Add(Named("Forgotten Helm", 1, true, "Forgotten Helm", "Shade Helm")); cards18.Add(Named("Forgotten Helm", 1, true, "Forgotten Helm", "Shade Helm"));
        cards18.Add(Named("Ethereal Boots", 1, true, "Ethereal Boots", "Chaos Boots"));
        string J(List<TableWitness.Decision> ds) => ds.Count == 0 ? "none" : string.Join(" | ", ds.Select(d => $"{d.Kind} {d.Name} x{d.Slot.Count}"));
        Check("18:06 cards: Soul Codex x41 beside the chat's own x3: not read", J(TableWitness.Judge(cards18, lines18)), "not read Soul Codex x41");
        Check("...the chat's x3 alone, read with nothing it agrees with: kept", J(TableWitness.Judge(cards18, new List<TableWitness.Line> { new(burst, "Soul Codex", 3), new(burst, "Odyle", 9) })), "none");
        Check("...Odyle x8 on the cards, x9 in the chat: within 2x, kept", J(TableWitness.Judge(cards18.Where(s => s.Name != "Soul Codex").ToList(), lines18)), "none");
        // the 15:31:54 Urugugu table (its record: Enhance Stone 4,000, Soul Codex 5, Silentium 14): its printout read at
        // 15:31:52.58; "Acquired Enhance Stone x935." came 7 s later with three skins the table never showed
        var t15 = new DateTime(2026, 10, 24, 19, 31, 53, DateTimeKind.Utc);
        var p15 = t15.AddSeconds(-0.42); var skins = t15.AddSeconds(6.88);
        var lines15 = new List<TableWitness.Line>
        {
            new(p15, "Vagabond Gloves", 1), new(p15, "Divine Canyon Gloves", 1), new(p15, "Divine Canyon Pauldrons", 1), new(p15, "Divine Canyon Pauldrons", 1),
            new(p15, "Divine Canyon Ring", 1), new(p15, "Silentium", 14), new(p15, "Expert's Refining Stone", 1), new(p15, "Refining Stone", 6), new(p15, "Soul Codex", 5),
            new(skins, "Adamant Guardian (Gloves) Skin", 1), new(skins, "Netherworld Explorer (Gloves) Skin", 1), new(skins, "Skyfeather Guardian (Gloves) Skin", 1), new(skins, "Enhance Stone", 935),
        };
        var table15 = new List<TableWitness.Slot> { Named("Enhance Stone", 4000), Named("Soul Codex", 5), Named("Silentium", 14), Named("Refining Stone", 6), Named("Expert's Refining Stone", 1), Named("Vagabond Gloves", 1, true) };
        Check("15:31 table: Enhance Stone x4.0K kept beside a lone x935 7 s later", J(TableWitness.Judge(table15, lines15)), "none");
        Check("...the same x935 in the table's own printout: not read", J(TableWitness.Judge(table15, lines15.Select(l => l.Name == "Enhance Stone" ? l with { At = p15 } : l).ToList())), "not read Enhance Stone x4000");
        // the picture short of its printout keeps its count and is one to look at (2026-10-25 13:07:04 Hidden Lugi: card 3's
        // Soul Codex x1, the chat x3, the other two in places the cards only showed cut)
        Check("...one slot x3 where its printout says x30: kept, to look at", J(TableWitness.Judge(new List<TableWitness.Slot> { Named("Odyle", 3), Named("Refining Stone", 6), Named("Silentium", 14) },
            new List<TableWitness.Line> { new(p15, "Odyle", 30), new(p15, "Refining Stone", 6), new(p15, "Silentium", 14) })), "to look at Odyle x3");
        // ---- the chat's count that no read showed (2026-10-25 20:06:51 to 20:07:06, Vakron) ----
        var t20 = new DateTime(2026, 10, 26, 0, 6, 51, DateTimeKind.Utc);
        var table20 = new List<TableWitness.Slot> { Named("Amplify Stone Fragment (Unique)", 9), Named("Silentium", 14), Named("Soul Codex", 5), Named("Refining Stone", 3) };
        var burst20 = new List<TableWitness.Line> { new(t20, "Amplify Stone Fragment (Unique)", 1, Assumed: true), new(t20, "Silentium", 14), new(t20, "Soul Codex", 5), new(t20, "Refining Stone", 3) };
        Check("20:06 table: Amplify x9 beside the chat's assumed 1: kept", J(TableWitness.Judge(table20, burst20)), "none");
        Check("...the same 1 read as a count: not read, as before", J(TableWitness.Judge(table20, burst20.Select(l => l with { Assumed = false }).ToList())), "not read Amplify Stone Fragment (Unique) x9");
        Check("...an assumed line agrees with nothing: Soul Codex x5 beside x1 and two assumed lines kept",
            J(TableWitness.Judge(new List<TableWitness.Slot> { Named("Soul Codex", 5), Named("Silentium", 14), Named("Refining Stone", 3) },
                new List<TableWitness.Line> { new(t20, "Soul Codex", 1), new(t20, "Silentium", 1, Assumed: true), new(t20, "Refining Stone", 1, Assumed: true) })), "none");
        var kept20 = new List<TableWitness.Line>();
        TableWitness.AddLine(kept20, new(t20, "Amplify Stone Fragment (Unique)", 1, Assumed: true), settle: false);
        TableWitness.AddLine(kept20, new(t20.AddSeconds(4), "Amplify Stone Fragment (Unique)", 8), settle: true);
        TableWitness.AddLine(kept20, new(t20.AddSeconds(5), "Odyle", 2), settle: true);
        Check("its count read again later completes the line; a lone settle stays assumed", string.Join(" | ", kept20.Select(l => $"{l.Name} x{l.Count}{(l.Assumed ? " assumed" : "")}")),
            "Amplify Stone Fragment (Unique) x9 | Odyle x2 assumed");
        // the parser joins a wrapped "(Bound) x9." under its line when the lines are parsed one at a time (a burst)
        var cp = new ChatParser(items);
        var read20 = new List<string> { "Earth Gloves acquired.", "Acquired Amplify Stone Fragment (Unique)", "(Bound) x9.", "Acquired Silentium (Bound) x14." };
        string JT(IReadOnlyList<string> ls, IReadOnlyList<string> rd) => string.Join(" | ", cp.JoinWrappedTails(ls, rd, t20).Select(x => x.Text));
        Check("burst: the tail joined to its line", JT(read20, read20), "Earth Gloves acquired. | Acquired Amplify Stone Fragment (Unique) (Bound) x9. | Acquired Silentium (Bound) x14.");
        Check("...the settle's lines (no tail among them): taken from the read", JT(new[] { "Acquired Amplify Stone Fragment (Unique)", "Acquired Silentium (Bound) x14." }, read20),
            "Acquired Amplify Stone Fragment (Unique) (Bound) x9. | Acquired Silentium (Bound) x14.");
        Check("...a tail that isn't under it in the read: left alone", JT(new[] { "Acquired Amplify Stone Fragment (Unique)", "(Bound) x9." }, new[] { "(Bound) x9.", "Acquired Amplify Stone Fragment (Unique)" }),
            "Acquired Amplify Stone Fragment (Unique) | (Bound) x9.");
        var joined20 = cp.ParseBatch(new List<string> { "Acquired Amplify Stone Fragment (Unique) (Bound) x9." }, t20, wrap: false).SelectMany(x => x.Events).ToList();
        Check("...and parsed as x9", string.Join(" ", joined20.Select(e => e is ItemAcquired a ? $"{Unb(items.NameOf(a.ItemId))} x{a.Count}" : e is RewardReceived r ? $"{Unb(r.Name)} x{r.Count}" : e.GetType().Name)), "Amplify Stone Fragment (Unique) x9");
        // gear twins by the chat: Worn Bow / Training Bow isn't on the faction list
        var bows = new List<TableWitness.Slot> { Twin("Worn Bow", "Training Bow"), Twin("Worn Bow", "Training Bow") };
        Check("twins by the chat: one line names one of two pieces", J(TableWitness.Judge(bows, new List<TableWitness.Line> { new(p15, "Training Bow", 1) })), "settle Training Bow x1");
        Check("...two lines, one of each: both pieces", J(TableWitness.Judge(bows, new List<TableWitness.Line> { new(p15, "Training Bow", 1), new(p15, "Worn Bow", 1) })), "settle Worn Bow x1 | settle Training Bow x1");
        Check("...one piece, both named: the chat can't tell", J(TableWitness.Judge(bows.Take(1).ToList(), new List<TableWitness.Line> { new(p15, "Training Bow", 1), new(p15, "Worn Bow", 1) })), "none");
        Check("...no line: unknown gear as before", J(TableWitness.Judge(bows, new List<TableWitness.Line>())), "none");
        Check("a faction-settled piece the chat names as its twin: unknown gear again", J(TableWitness.Judge(new List<TableWitness.Slot> { Named("Forgotten Helm", 1, true, "Forgotten Helm", "Shade Helm") }, new List<TableWitness.Line> { new(burst, "Shade Helm", 1) })), "unsettle Forgotten Helm x1");
        Check("...the chat naming it as itself: kept", J(TableWitness.Judge(new List<TableWitness.Slot> { Named("Forgotten Helm", 1, true, "Forgotten Helm", "Shade Helm") }, new List<TableWitness.Line> { new(burst, "Forgotten Helm", 1), new(burst, "Shade Helm", 1) })), "none");
        Check("judged when due: 12 s after the screen's last sight, 90 s at most", $"{(TableWitness.Due(t18, t18.AddSeconds(1)) - t18).TotalSeconds} {(TableWitness.Due(t18, t18.AddMinutes(5)) - t18).TotalSeconds}", "13 90");

        // ---- b2. two reads of one screen (2026-10-25) ----
        Check("a count read with a minus before it is not read (13:08:29 \"-400\")", $"{RewardGrid.ParseCount("-400")} {RewardGrid.ParseCount("400")} {RewardGrid.ParseCount("4.0K")}", "0 400 4000");
        var bx = new Rectangle(0, 0, 10, 10);
        RewardGrid.Slot G(string key, double d, long n, string text = "") => new(bx, "Common", text.Length > 0 ? text : n.ToString(), n, new() { (key, d) });
        var r1 = new List<RewardGrid.Slot> { G("A", 30, 2), G("B", 95, 6), G("C", 40, 1) };
        var r2 = new List<RewardGrid.Slot> { G("A", 31, 2), G("B", 50, 6), G("C", 41, 1), G("World", 60, 1) };
        var voted2 = RewardGrid.Vote(new List<List<RewardGrid.Slot>> { r1 }, 70);
        var filled2 = RewardGrid.FillUnread(voted2, new List<List<RewardGrid.Slot>> { r1, r2 }, 70, out var said2);
        Check("a table slot the vote left unsure, read surely in a read of another slot count: filled", $"{string.Join(" ", filled2.Select(s => $"{s.Icons[0].Key}{s.Icons[0].Distance:0}x{s.Count}"))} | {said2.Count}", "A30x2 B50x6 C40x1 | 1");
        var r3 = new List<RewardGrid.Slot> { G("A", 31, 2), G("B2", 50, 6), G("C", 41, 1) };
        Check("...two reads naming other icons there: left not read", string.Join(" ", RewardGrid.FillUnread(voted2, new List<List<RewardGrid.Slot>> { r1, r2, r3 }, 70, out _).Select(s => $"{s.Icons[0].Key}{s.Icons[0].Distance:0}")), "A30 B95 C40");
        Check("...a slot the vote read is never changed", string.Join(" ", RewardGrid.FillUnread(voted2, new List<List<RewardGrid.Slot>> { r1, new() { G("A", 20, 3), G("B", 50, 6), G("C", 20, 4) } }, 70, out _).Select(s => $"x{s.Count}")), "x2 x6 x1");
        // the live read against the read again of the kept pictures, place by place (18:06:11 x340, 18:07:41 x320)
        var live = new InstanceRun { Zone = "Shugo Festival", Kind = "Event", Event = "Defend Shugo Merchants", Start = t18, FromPicture = true, PartlyRead = true, PictureSlots = 46, SlotsUnread = 2,
            Items = { [700] = 340 + 440, [701] = 50 }, PictureCounts = { "c1.3|700|False|440", "c2.3|700|False|340", "c2.4|701|False|30", "c2.5|701|False|20" } };
        var again = live.Clone(); again.Items[700] = 320 + 440; again.PictureCounts[1] = "c2.3|700|False|320";
        Check("two reads of one screen, one place x340 and x320: not read on the run kept", $"{TrackerSession.TakeReread(live, again)} {live.Items[700]} {live.SlotsUnread} {live.UnknownItems} {live.PartlyRead} {live.UnknownSlots.LastOrDefault()}",
            "False 440 3 1 True c2.3 · x340 or x320: two reads of the screen disagreed");
        var shifted = live.Clone(); shifted.PictureCounts = new() { "c2.3|701|False|30", "c2.4|701|False|20" };
        Check("...a card whose places don't line up (a row off) is not compared", $"{TrackerSession.CountsDiffer(new[] { "c2.4|701|False|30", "c2.5|701|False|20" }, shifted.PictureCounts).Count}", "0");
        var doubted = new InstanceRun { Zone = "Vakron Sky Island", Kind = "Expedition", FromPicture = true, SlotDoubts = { "Special x1 · Soul Codex: the picture read x1, the chat's own line x3" } };
        Check("a count in doubt: the run out of the averages, one to look at", $"{doubted.OutOfAverages} {doubted.ToLookAt}", "True True");

        // ---- c. a session: the cards filed, the chat read after the screen closed, the judge on the run ----
        var keepDir = CrashLog.DirOverride;
        var dir = TestTemp.Path("kl-witness-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(dir);
        CrashLog.DirOverride = dir;
        try
        {
            var box = new Rectangle(0, 0, 10, 10);
            ItemInfo Of(string name) => items.AllWithIcons().First(i => string.Equals(Unb(i.Name), name, StringComparison.OrdinalIgnoreCase) && i.Icon.Length > 0);
            RewardGrid.Slot S(string name, string text = "1") { var i = Of(name); return new RewardGrid.Slot(box, i.Grade, text, RewardGrid.ParseCount(text), new() { (IconIndex.KeyOf(i.Icon), 40), ("Other", 120) }); }
            List<RewardGrid.Slot> Cards() => new List<RewardGrid.Slot> { S("Soul Codex"), S("Soul Codex"), S("Soul Codex", "41"), S("Fine Odyle"), S("Forgotten Helm"), S("Forgotten Helm"), S("Ethereal Boots"), S("Worn Bow") }
                .Concat(Enumerable.Range(0, 8).Select(_ => S("Odyle"))).Concat(Enumerable.Range(0, 6).Select(_ => S("Refining Stone"))).ToList();
            // the reads of 18:06:54 to 18:07:16 (trace.log), once the Monster Cube closed: the event's lines above them as fillers
            var fill = new[] { "[P1] filler 34", "[P2] filler 39" };
            var reads = new (double At, string[] L)[]
            {
                (3.39, new[] { "Forgotten Helm acquired.", "Ethereal Boots acquired.", "Radiant Odyle acquired.", "Acquired Pure-Odyle x2.", "Fine Odyle acquired.", "Acquired Odyle x9.", "Acquired Expert's Refining Stone x2.—•—", "Acquired Refining Stone x6.", "Acquired Soul Codex (Bound) x3." }),
                (4.55, new[] { "Forgotten Helm acquired.", "Ethereal Boots acquired.", "Radiant Odyle acquired.", "Acquired Pure-Odyle x2.", "Fine Odyle acquired.", "Acquired Odyle x9.", "Acquired Expert's Refining Stone x2.—-", "Acquired Refining Stone x6.", "Acquired Soul Codex (Bound) x3." }),
                (6.10, new[] { "Forgotten Helm acquired.", "Ethereal Boots acquired.", "Radiant Odyle acquired.", "Acquired Pure-Odyle x2.", "Fine Odyle acquired.", "Acquired Odyle x9.", "Acquired Expert's Refining Stone", "Acquired Refining Stone x6.", "Acquired Soul Codex (Bound) x3." }),
                (12.61, new[] { "Radiant Odyle acquired.", "Acquired Pure Odyle x2.", "Fine Odyle acquired.", "Acquired Odyle x9.", "Acquired Expert's Refining Stone x2.", "Acquired Refining Stone x6.", "Acquired Soul Codex (Bound) x3.", "[P3] filler 20", "[P4] filler 21" }),
                (14.51, new[] { "Fine Odyle acquired.", "Acquired Odyle x9.", "Acquired Expert's Refining Stone x2.", "Acquired Refining Stone x6.", "Acquired Soul Codex (Bound) x3.", "[P5] filler 22", "[P4] filler 21", "[P6] filler 30", "[P7] filler 31" }),
                (20.56, new[] { "Acquired Odyle x9.", "Acquired Expert's Refining Stone x2.", "Acquired Refining Stone x6.", "Acquired Soul-Codex (Bound) x3.", "[P5] filler 22", "[P4] filler 21", "[P8] filler 40", "[P7] filler 31", "[P9] filler 9" }),
                (25.47, new[] { "Acquired Odyle x9.", "Acquired Expert's Refininå stone x2.", "Acquired Refining St6he x6.", "Acquired Soul Codex (Bound) x3.", "[P5] filler 22", "[P4] filler 21", "[P8] filler 40" }),
            };
            InstanceRun RunCards(string server, bool chat)
            {
                var s = new TrackerSession(null, settings, items, "Test") { Server = server };
                var t0 = new DateTime(2026, 10, 24, 22, 2, 15, DateTimeKind.Utc);
                s.Engine.SetZone("Hidden Lugi"); s.Instances.Zone("Hidden Lugi", t0);
                // the event's chat (its "found" lines and players' lines), then the Monster Cube over it: empty reads
                var lobby = new List<string>();
                for (int i = 0; i < 20; i++) { lobby.Add(i % 3 == 0 ? $"Alpha (Shugo) found Bravo{i} (Lugi)." : $"[P{i % 5}] event chat {i}"); if (lobby.Count > 9) lobby.RemoveAt(0); s.ProcessChat(new List<string>(lobby), t0.AddSeconds(200 + i * 3)); }
                for (int i = 0; i < 4; i++) s.ProcessChat(new List<string>(), t18.AddSeconds(-12 + i * 3));
                s.ApplyGrid(Cards(), t18, what: "event cards");
                if (chat) foreach (var (at, l) in reads) { s.ProcessChat(l.Concat(fill).Take(10).ToList(), t18.AddSeconds(at)); s.TableWitnessDue(t18.AddSeconds(at)); }
                s.FlushVotes(t18.AddSeconds(28));
                s.TableWitnessDue(t18.AddSeconds(30));
                return s.Instances.CurrentRun!;
            }
            string R(InstanceRun r, string name) => r.Items.Concat(r.BoundItems).Where(kv => Unb(items.NameOf(kv.Key)) == name).Sum(kv => kv.Value).ToString();
            var plain = RunCards("", chat: false);
            Check("cards, no server, no chat: twins unknown, the x41 counted", $"gear {plain.UnknownGear} codex {R(plain, "Soul Codex")} unread {plain.SlotsUnread}", "gear 4 codex 43 unread 0");
            var ely = RunCards("Siel (Elyos)", chat: false);
            Check("cards, Siel (Elyos): the twins are the Elyos ones", $"gear {ely.UnknownGear} helm {R(ely, "Forgotten Helm")} boots {R(ely, "Ethereal Boots")} bow notes {string.Join("; ", ely.GearNotes)}", "gear 1 helm 2 boots 1 bow notes Common gear, one of Worn Bow / Training Bow");
            var full = RunCards("Siel (Elyos)", chat: true);
            Check("...and the chat after: Soul Codex x41 not read, x1 x1 kept", $"codex {R(full, "Soul Codex")} unread {full.SlotsUnread} partly {full.PartlyRead}", "codex 2 unread 1 partly True");
            Check("...the twins stand, the chat agreeing", $"gear {full.UnknownGear} helm {R(full, "Forgotten Helm")} boots {R(full, "Ethereal Boots")}", "gear 1 helm 2 boots 1");
            Check("...the lines judged are kept on the run", $"{full.TableChat.Any(l => l.Name == "Soul Codex" && l.Count == 3)} {full.TableChat.Any(l => l.Name == "Forgotten Helm")}", "True True");
            Check("...and said in reads.log", File.ReadAllText(CrashLog.ReadsPath).Contains("slot named Soul Codex is not read: the slot read x41, the chat's own line x3") ? "logged" : "no line", "logged");
            var chatOnly = RunCards("", chat: true);
            Check("cards, no server, the chat after: the named pieces settled by it", $"gear {chatOnly.UnknownGear} helm {R(chatOnly, "Forgotten Helm")} boots {R(chatOnly, "Ethereal Boots")} codex {R(chatOnly, "Soul Codex")}", "gear 2 helm 1 boots 1 codex 2");
            var asm = RunCards("Israphel (Asmodian)", chat: true);
            Check("an Asmodian server, the chat naming the Elyos pieces: unknown again", $"gear {asm.UnknownGear} shade {R(asm, "Shade Helm")} chaos {R(asm, "Chaos Boots")}", "gear 4 shade 0 chaos 0");
        }
        finally { CrashLog.DirOverride = keepDir; try { Directory.Delete(dir, true); } catch (Exception) { } }
        return ok;
    }
}
