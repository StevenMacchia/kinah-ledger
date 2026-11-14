using KinahLedger.Data;
using KinahLedger.Packets;
using KinahLedger.Session;

namespace KinahLedger.Parser;

/// <summary>
/// parser-test: the boss-fight rules on made-up fight events. Fights only inside a Transcendence, an Expedition or a
/// Sanctuary and only for a boss; one per boss, a new boss starts afresh; a gap, a reconnect or a restart marks a fight
/// partial and never splits it; quiet fights are given up; the group, damage only when the game gives it (the damage rows
/// are the group until a group list comes, Other keeps the rows adding up), the boss's HP; the kept file.
/// </summary>
public static class ParserTests
{
    static int _fails;
    static void Check(bool ok, string what) { Console.WriteLine((ok ? "ok    " : "FAIL  ") + what); if (!ok) _fails++; }

    const string Book = """
        # test list
        Shattered Arkanis|Expedition|Talisra of the Void|
        Shattered Arkanis|Expedition|Siliator of Deceit|
        Shattered Arkanis|Expedition|Ruthilis of Pain|final
        Chalice of Muspel|Sanctuary|Iscariel|
        Chalice of Muspel|Sanctuary|Kaldrix|final
        Snowfield of Sorrow|Sanctuary
        """;

    static readonly GroupMember Me = new(1, "Swoly", "Gladiator", 312_400, 5_620, true);
    static readonly GroupMember Ally = new(2, "Aerin", "Cleric", 298_100, 5_580, false);
    static readonly GroupMember Ally2 = new(3, "Thorne", "Templar", 305_000, 5_600, false);

    public static int Run(Settings settings)
    {
        _fails = 0;
        var book = BossBook.Parse(Book);
        var t = new DateTime(2026, 11, 13, 21, 0, 0, DateTimeKind.Utc);

        (BossParser P, List<Encounter> Kept) New() { var kept = new List<Encounter>(); return (new BossParser(book) { Ended = kept.Add }, kept); }

        // the boss list: kinds, stages, finals
        Check(book.Count == 5 && book.KindOf("Chalice of Muspel") == ContentKind.Sanctuary && book.KindOf("Snowfield of Sorrow") == ContentKind.Sanctuary && !book.Knows("Snowfield of Sorrow"),
            $"the boss list reads 5 bosses, the Sanctuaries' kind, an instance with no bosses yet: {book.Count}");
        Check(book.Find("Shattered Arkanis [Stage 3]", "ruthilis of pain") is { Final: true }, "...a Transcendence stage is its expedition on the list, names in any case");
        Check(BossBook.Shared.Count >= 40 && BossBook.Shared.Find("Fire Temple", "Kromede's Desire") is { Final: true }, $"...the built-in list carries the bosses: {BossBook.Shared.Count}");

        // a boss in an expedition makes a fight; trash there doesn't; a boss in the open world doesn't
        {
            var (p, kept) = New();
            p.Take(new GroupChanged(t, new[] { Me, Ally }));
            p.Take(new BossEngaged(t.AddSeconds(1), "Talisra of the Void", 900));   // not in an instance yet: the open world
            Check(p.Live == null, "a boss pulled outside the three kinds makes no fight");
            p.Take(new InstanceEntered(t.AddSeconds(2), ContentKind.Expedition, "Shattered Arkanis", 77));
            p.Take(new BossEngaged(t.AddSeconds(20), "Talisra of the Void", 902));
            var live = p.Live;
            Check(live is { Boss: "Talisra of the Void", Kind: "Expedition", Instance: "Shattered Arkanis" } && live.Roster.Count == 2 && live.You?.Name == "Swoly",
                $"a listed boss in an expedition starts a fight with the group at the pull: {live?.Boss}, {live?.Roster.Count} in it");
            Check(live?.Roster.First(f => f.Name == "Aerin") is { CombatPower: 298_100, ItemLevel: 5_580, Class: "Cleric" }, "...each member with class, Combat Power and item level");
            p.Take(new BossEngaged(t.AddSeconds(25), "Talisra of the Void", 902));   // the same pull said again
            p.Take(new UnitDied(t.AddSeconds(40), 950, "Void Spawn", false));
            p.Take(new UnitDied(t.AddSeconds(70), 2, "Aerin", false));
            p.Take(new BossEnded(t.AddSeconds(95), 902, BossOutcome.Kill));
            Check(kept.Count == 1 && kept[0] is { Outcome: "kill", Partial: false } && kept[0].Duration(t).TotalSeconds == 75,
                $"...and ends with the kill, kept once: {kept.Count} kept, {kept.FirstOrDefault()?.Outcome}, {kept.FirstOrDefault()?.Duration(t).TotalSeconds} s");
            Check(kept[0].Kills.Count == 1 && kept[0].Kills[0].Name == "Void Spawn" && kept[0].Roster.First(f => f.Id == 2).Deaths == 1,
                "...with the kills near you and a member's death (a member is never a kill)");
            Check(!kept[0].HasDamage && kept[0].GroupDps(t) == 0, "...no damage from the game: none shown, none made up");
        }

        // one fight per boss: a new boss ends the fight under way and starts afresh
        {
            var (p, kept) = New();
            p.Take(new InstanceEntered(t, ContentKind.Expedition, "Shattered Arkanis", 77));
            p.Take(new GroupChanged(t, new[] { Me, Ally }));
            p.Take(new BossEngaged(t.AddSeconds(10), "Talisra of the Void", 902));
            p.Take(new DamageDealt(t.AddSeconds(20), 1, 902, 50_000));
            p.Take(new DamageDealt(t.AddSeconds(30), 2, 902, 20_000));
            p.Take(new BossEngaged(t.AddSeconds(60), "Siliator of Deceit", 903));
            var live = p.Live;
            Check(kept.Count == 1 && kept[0] is { Boss: "Talisra of the Void", Outcome: "left", HasDamage: true, BossDamage: 70_000 } && live is { Boss: "Siliator of Deceit" },
                $"a new boss pulled ends the last fight and starts its own: {string.Join(", ", kept.Select(k => k.Boss + " " + k.Outcome))}; now {live?.Boss}");
            Check(live!.Roster.Sum(f => f.Damage) == 0 && !live.HasDamage && live.Kills.Count == 0, "...the new fight starts from nothing (damage, kills)");
            Check(Math.Abs(kept[0].Dps(kept[0].Roster.First(f => f.You), t) - 50_000 / 20.0) < 0.01 && kept[0].Ranked().First().Name == "Swoly",
                $"...ended at the last thing heard of it; its DPS from the game's damage, ranked by damage: {kept[0].Dps(kept[0].Roster.First(f => f.You), t):0}");
            p.Take(new DamageDealt(t.AddSeconds(150), 1, 903, 1_000));
            p.Take(new BossEngaged(t.AddSeconds(200), "Siliator of Deceit", 904));   // a new spawn of the same boss (after a reset)
            Check(kept.Count == 2 && p.Live?.BossId == 904, "...so does a new spawn of the same boss (a reset, a second pull)");
            p.Take(new UnitDied(t.AddSeconds(260), 904, "Siliator of Deceit", true));
            Check(kept.Count == 3 && kept[2] is { Outcome: "kill" } && kept[2].Kills.Last().Boss, "the boss's own death is the kill when the game says no end");
            p.Take(new BossEngaged(t.AddSeconds(300), "Ruthilis of Pain", 905));
            p.Take(new BossEnded(t.AddSeconds(302), 905, BossOutcome.Wipe));
            Check(kept.Count == 3, $"a pull of under {BossParser.MinSeconds:0} s that killed nothing isn't kept");
            p.Take(new BossEngaged(t.AddSeconds(400), "Ruthilis of Pain", 906));
            p.Take(new BossEnded(t.AddSeconds(500), 906, BossOutcome.Wipe));
            Check(kept.Count == 4 && kept[3] is { Outcome: "wipe", FinalBoss: true }, "a wipe is kept, the final boss marked");
        }

        // a gap, a reconnect: one fight, partial, never split
        {
            var (p, kept) = New();
            p.Take(new InstanceEntered(t, ContentKind.Transcendence, "Shattered Arkanis [Stage 2]", 78));
            p.Take(new GroupChanged(t, new[] { Me, Ally }));
            p.Take(new BossEngaged(t.AddSeconds(5), "Ruthilis of Pain", 910));
            p.Take(new FeedGap(t.AddSeconds(30)));
            p.Take(new FeedGap(t.AddSeconds(40)));
            Check(p.Live is { Partial: true } l && l.PartialWhy.Single() == "2 gaps in the game's traffic" && l.Kind == "Transcendence",
                $"lost traffic marks the fight partial, said once: {string.Join("; ", p.Live?.PartialWhy ?? new())}");
            p.Take(new InstanceEntered(t.AddSeconds(120), ContentKind.Transcendence, "Shattered Arkanis [Stage 2]", 78));   // reconnected, same copy
            p.Take(new GroupChanged(t.AddSeconds(121), new[] { Me, Ally, Ally2 }));
            Check(kept.Count == 0 && p.Live is { BossId: 910 } l2 && l2.PartialWhy.Contains("the game reconnected during the fight") && l2.Roster.Single(f => f.Name == "Thorne").Late,
                "back in the same instance after a reconnect: the same fight carries on, a joiner marked late");
            p.Take(new UnitDied(t.AddSeconds(180), 910, "Ruthilis of Pain", true));
            Check(kept.Count == 1 && kept[0] is { Outcome: "kill", Partial: true }, "...and ends as one fight, partial");
            p.Take(new BossEngaged(t.AddSeconds(200), "Talisra of the Void", 911));
            p.Take(new DamageDealt(t.AddSeconds(215), 1, 911, 1_000));
            p.Take(new InstanceEntered(t.AddSeconds(230), ContentKind.Expedition, "Shattered Arkanis", 79));   // a new copy
            Check(kept.Count == 2 && kept[1].Outcome == "left" && p.Live == null, "a new copy of the instance ends the fight under way (left)");
            p.Take(new BossEngaged(t.AddSeconds(240), "Talisra of the Void", 912));
            p.Take(new InstanceLeft(t.AddSeconds(260)));
            Check(kept.Count == 3 && kept[2].Outcome == "left" && p.Where.Kind == ContentKind.Other, "leaving the instance ends it too");
        }

        // a Sanctuary the game names Other is one by the list; an instance the list has no bosses for keeps the game's boss
        {
            var (p, kept) = New();
            p.Take(new InstanceEntered(t, ContentKind.Other, "Chalice of Muspel", 80));
            p.Take(new BossEngaged(t.AddSeconds(5), "Kaldrix", 920));
            Check(p.Live is { Kind: "Sanctuary", FinalBoss: true }, "a Sanctuary by the list makes fights for its bosses");
            var (q, _) = New();
            q.Take(new InstanceEntered(t, ContentKind.Sanctuary, "Snowfield of Sorrow", 81));
            q.Take(new BossEngaged(t.AddSeconds(5), "Storm Legion Commander Deltras", 921));
            Check(q.Live is { Boss: "Storm Legion Commander Deltras" }, "an instance with no listed bosses keeps what the game marks a boss");
            var (r, _) = New();
            r.Take(new InstanceEntered(t, ContentKind.Other, "Western Cantas Valley", 0));
            r.Take(new BossEngaged(t.AddSeconds(5), "Field Boss", 922));
            Check(r.Live == null, "the open world keeps nothing, even a boss the game marks");
        }

        // the decoder's pull: the boss's HP, damage by source before any group list, Other, then the group list filling in
        {
            var (p, kept) = New();
            p.Take(new InstanceEntered(t, ContentKind.Expedition, "Shattered Arkanis", 84));
            p.Take(new BossEngaged(t.AddSeconds(1), "Siliator of Deceit", 950));
            p.Take(new BossHealth(t.AddSeconds(1), 950, 100_000, 100_000));
            p.Take(new DamageDealt(t.AddSeconds(5), 11, 950, 30_000, "Swoly", true));
            p.Take(new DamageDealt(t.AddSeconds(6), 12, 950, 20_000, "Aerin"));
            p.Take(new DamageDealt(t.AddSeconds(7), 13, 950, 9_000));             // a pet or a player not named yet
            p.Take(new DamageDealt(t.AddSeconds(8), 0, 950, 1_000));              // Other: no hit came with the fall
            p.Take(new BossHealth(t.AddSeconds(8), 950, 40_000, 100_000));
            var l = p.Live!;
            Check(l.Roster.Count == 4 && l.Members.Count() == 3 && l.You?.Name == "Swoly" && l.Roster.Any(f => f.Id == 13 && f.Name == "" && f.FromDamage),
                $"with no group list, who hit the boss is the group (you marked, an unnamed source kept): {string.Join(", ", l.Ranked().Select(f => (f.Other ? "Other" : f.Name) + " " + f.Damage))}");
            Check(l.Ranked().Last().Other && l.Roster.Sum(f => f.Damage) == 60_000 && l.BossDamage == 60_000 && l.BossHpLeft == 0.4,
                $"...Other last, the rows adding up to the HP lost ({l.Roster.Sum(f => f.Damage):N0}), the boss at {l.BossHpLeft:P0}");
            p.Take(new GroupChanged(t.AddSeconds(9), new[] { new GroupMember(11, "Swoly", "Gladiator", 4_800, 5_640, true), new GroupMember(12, "Aerin", "Cleric", 4_600, 5_600, false) }));
            l = p.Live!;
            var me = l.Roster.Single(f => f.Id == 11);
            Check(me is { CombatPower: 4_800, ItemLevel: 5_640, Class: "Gladiator", Damage: 30_000, Late: false, FromDamage: false } && !l.Roster.Single(f => f.Id == 13).Gone,
                "...and the group list fills in class, CP and item level on the same rows, nobody marked late or gone for it");
            p.Take(new BossEngaged(t.AddSeconds(20), "Ruthilis the Unlisted", 951));
            Check(kept.Count == 1 && p.Live?.Boss == "Ruthilis the Unlisted", "a pull the boss list doesn't name is still a fight under the game's name (a spelling must never lose one)");
            p.Take(new BossEngaged(t.AddSeconds(40), "", 952));
            Check(p.Live?.Boss == BossParser.UnnamedBoss, "...and one with no name at all is kept too");
        }

        // a fight with nothing heard for ten minutes is given up at the last thing heard
        {
            var (p, kept) = New();
            p.Take(new InstanceEntered(t, ContentKind.Expedition, "Shattered Arkanis", 82));
            p.Take(new BossEngaged(t.AddSeconds(5), "Talisra of the Void", 930));
            p.Take(new DamageDealt(t.AddSeconds(65), 0, 930, 10));
            p.Tick(t.AddMinutes(5));
            Check(p.Live != null, "a quiet fight is kept going for a while");
            p.Tick(t.AddMinutes(12));
            Check(kept.Count == 1 && kept[0].End == t.AddSeconds(65) && kept[0].Partial && kept[0].Outcome == "left",
                $"...and given up after {BossParser.StaleMinutes:0} minutes, ending at the last thing heard: {kept.FirstOrDefault()?.End:HH:mm:ss}");
        }

        // the kept file and a restart in the middle of a fight
        {
            var dir = TestTemp.Path("KinahLedger-parser-test"); if (Directory.Exists(dir)) Directory.Delete(dir, true);
            var store = new EncounterStore(() => dir);
            var (p, _) = New();
            p.Ended = store.Add;
            p.Take(new InstanceEntered(t, ContentKind.Expedition, "Shattered Arkanis", 83));
            p.Take(new GroupChanged(t, new[] { Me, Ally }));
            p.Take(new BossEngaged(t.AddSeconds(5), "Talisra of the Void", 940));
            p.Take(new BossEnded(t.AddSeconds(65), 940, BossOutcome.Kill));
            p.Take(new BossEngaged(t.AddSeconds(100), "Siliator of Deceit", 941));
            p.Take(new DamageDealt(t.AddSeconds(130), 1, 941, 5_000));
            store.SaveLive(p.Live);
            File.AppendAllText(Path.Combine(dir, EncounterStore.FileName), "{\"Id\":\"cut");   // a line cut by a crash
            var again = new EncounterStore(() => dir);
            var all = again.All();
            Check(all.Count == 1 && all[0] is { Boss: "Talisra of the Void", Outcome: "kill" } && all[0].Roster.Count == 2,
                $"the kept fights read back, a cut line skipped: {all.Count}");
            var live = again.LoadLive();
            var (q, kept2) = New();
            q.Ended = e => { kept2.Add(e); again.Add(e); };
            q.Resume(live!, t.AddSeconds(160));
            Check(q.Live is { Boss: "Siliator of Deceit", Partial: true } l && l.PartialWhy.Contains("the tracker restarted during the fight") && q.Where.Instance == "Shattered Arkanis",
                "a restart in the middle of a fight carries it on, partial, in its instance");
            q.Take(new InstanceEntered(t.AddSeconds(170), ContentKind.Expedition, "Shattered Arkanis", 83));
            q.Take(new BossEnded(t.AddSeconds(200), 941, BossOutcome.Kill));
            Check(kept2.Count == 1 && kept2[0].Duration(t).TotalSeconds == 100 && again.All().Count == 2, "...and ends as the one fight it was, from its own pull");
            live = new EncounterStore(() => dir).LoadLive();
            var (r, kept3) = New();
            r.Resume(live!, t.AddMinutes(40));
            Check(r.Live == null && kept3.Count == 1 && kept3[0] is { Outcome: "left", Partial: true } && kept3[0].End == live!.LastAt,
                "a fight saved long ago is kept as it was, ended at the last thing heard");
            again.Add(kept2[0]);   // the same fight twice counts once
            Check(new EncounterStore(() => dir).All().Count == 2, "a fight kept twice reads back once");
            try { Directory.Delete(dir, true); } catch (Exception) { }
        }

        // the session: the feed drained on its pass, the screen's place used until the game's messages say one
        {
            var items = ItemDictionary.LoadOrEmpty(settings.ItemsFile);
            var feed = new ListBossFeed(Array.Empty<BossEvent>());
            var s = new TrackerSession(null, settings, items, "Test") { FightFeed = feed };
            var now = DateTime.UtcNow;
            feed.Add(new InstanceEntered(now.AddSeconds(-30), ContentKind.Expedition, "Shattered Arkanis", 90));
            feed.Add(new GroupChanged(now.AddSeconds(-30), new[] { Me }));
            feed.Add(new BossEngaged(now.AddSeconds(-20), "Talisra of the Void", 990));
            s.DrainFightsForTest();
            Check(s.Fights.Live is { Boss: "Talisra of the Void" }, "the session drains the fight events on its pass");
            feed.Add(new BossEnded(now, 990, BossOutcome.Kill));
            s.DrainFightsForTest();
            Check(s.Fights.Live == null && TrackerSession.FightStore.All().Any(e => e.BossId == 990), "...and the fight lands in the character's kept fights");
        }

        Console.WriteLine(_fails == 0 ? "parser-test: all passed" : $"parser-test: {_fails} failed");
        return _fails == 0 ? 0 : 1;
    }
}
