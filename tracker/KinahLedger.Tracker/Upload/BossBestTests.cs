using System.Text.Json;
using KinahLedger.Parser;

namespace KinahLedger.Upload;

/// <summary>
/// Boss bests on the profile (2026-11-15, Upload/BossBests.cs): what the upload carries from the kept fights, that it is
/// only ever the player's own numbers, and when it goes. Made-up fights; nothing is read from or sent to anywhere. In
/// parse-test, and on its own as `boss-bests-test`.
/// </summary>
public static class BossBestTests
{
    static readonly DateTime T0 = new(2026, 11, 15, 19, 0, 0, DateTimeKind.Utc);

    /// <summary>A boss pull with you and one other member; <paramref name="mine"/> and <paramref name="theirs"/> are the boss damage, every target 10% more.</summary>
    static Encounter Pull(string boss, string outcome, int minute, int seconds, long mine, long theirs = 900_000, string kind = "Expedition", string instance = "Ferocious Horn Den",
        string mode = "Conquest", bool partial = false, bool split = true, string recMode = "boss") => new()
    {
        Mode = recMode, Kind = kind, Instance = instance, GameMode = mode, Boss = boss, Outcome = outcome, Partial = partial,
        HasDamage = true, TargetsSplit = split, Start = T0.AddMinutes(minute), End = T0.AddMinutes(minute).AddSeconds(seconds), LastAt = T0.AddMinutes(minute).AddSeconds(seconds),
        Roster = new()
        {
            new Fighter { Id = 1, Name = "Swoly", Class = "Templar", CombatPower = 141_000, You = true, Damage = mine * 11 / 10, BossDamage = mine },
            new Fighter { Id = 2, Name = "Someoneelse", Class = "Sorcerer", CombatPower = 160_000, Damage = theirs * 11 / 10, BossDamage = theirs },
        },
    };

    public static bool Run()
    {
        bool ok = true;
        void Check(string what, string got, string expect)
        {
            bool pass = got == expect; ok &= pass;
            Console.WriteLine($"  {what,-62} -> {got,-40} {(pass ? "ok" : "MISMATCH, expected " + expect)}");
        }
        Console.WriteLine("\nBoss bests on the profile (opt-in): the player's own best kill per boss, and when it goes:");
        var keepFights = BossBests.Fights;
        bool keepOn = BossBests.Enabled, keepClear = BossBests.ClearPending;
        var keepCleared = BossBests.Cleared;
        try
        {
            var fights = new List<Encounter>
            {
                Pull("Nuakum", "kill", 0, 200, 800_000),           // 4,000
                Pull("Nuakum", "kill", 10, 160, 800_000),          // 5,000: the best
                Pull("Nuakum", "wipe", 20, 100, 300_000),          // the latest pull: 3,000, a wipe
                Pull("Nuakum", "kill", 30, 100, 900_000, partial: true),  // missed some of it: never a best, never the last
                Pull("Krao", "wipe", 40, 100, 500_000, instance: "Krao Cave", mode: "Normal"),   // never killed: not sent
                Pull("Nuakum", "kill", 50, 100, 300_000, mode: "Normal"),  // another mode: its own boss
                Pull("Kromede", "kill", 60, 300, 600_000, kind: "Sanctuary", instance: "Kromede's Sanctuary", mode: "", split: false),
                Pull("Wolf", "kill", 70, 10, 50_000, kind: "Other", instance: "", mode: "", recMode: "fight"),   // the open world: never
            };
            var list = BossBests.From(fights);
            Check("one per instance, mode and boss, killed at least once", string.Join(", ", list.Select(b => $"{b.boss}/{b.mode}")), "Kromede/, Nuakum/Normal, Nuakum/Conquest");
            var n = list.First(b => b.boss == "Nuakum" && b.mode == "Conquest");
            Check("the best is the highest DPS kill, the boss alone", $"{n.dps} {n.basis} {n.seconds}s {n.at}", "5000 boss 160s 2026-11-15T19:10:00Z");
            Check("...the last pull is the newest whole one, wipe or kill", $"{n.last_dps} {n.last_outcome} {n.last_seconds}s", "3000 wipe 100s");
            Check("...kills counted, partial ones left out", n.kills.ToString(), "2");
            Check("...your class, combat power and the group's size", $"{n.cls} {n.cp} {n.group}", "Templar 141000 2");
            var k = list.First(b => b.boss == "Kromede");
            Check("an older record that kept every target together says so", $"{k.basis} {k.dps} {k.kind}", "all 2200 Sanctuary");

            var json = JsonSerializer.Serialize(list);
            Check("nothing about anyone else: no name, class, DPS of theirs", $"{json.Contains("Someoneelse")} {json.Contains("Sorcerer")} {json.Contains("160000")} {json.Contains("Swoly")}", "False False False False");
            using (var doc = JsonDocument.Parse(json))
                Check("the fields the site reads", string.Join(",", doc.RootElement[2].EnumerateObject().Select(p => p.Name)),
                    "boss,instance,mode,kind,dps,basis,seconds,at,class,cp,group,kills,last_dps,last_seconds,last_at,last_outcome");
            var many = Enumerable.Range(0, 60).Select(i => Pull("Boss " + i, "kill", i, 100, 100_000)).ToList();
            Check("at most 40 bosses, the latest fought", $"{BossBests.From(many).Count} {BossBests.From(many)[0].boss}", "40 Boss 59");

            // ---- when it goes ----
            BossBests.Reset();
            BossBests.Fights = () => fights;
            BossBests.Enabled = false; BossBests.ClearPending = false;
            Check("off (the default): nothing rides along", BossBests.Pending("c1", T0) == null ? "none" : "sent", "none");
            var off = RunPayload.RunsOnly("na_e", "Siel (Elyos)", null, new());
            Check("...and the upload has no boss_bests", JsonSerializer.Serialize(off).Contains("boss_bests").ToString(), "False");
            BossBests.Enabled = true;
            var first = BossBests.Pending("c1", T0);
            Check("on: the whole list goes", first?.Count.ToString() ?? "none", "3");
            var on = RunPayload.RunsOnly("na_e", "Siel (Elyos)", null, new());
            Check("...in the run upload", (on.boss_bests?.Count ?? 0).ToString(), "3");
            var p = on with { character = new CharacterPayload("c1") };
            BossBests.Took(p, new SendResult(true, 202, null, "{\"ok\":true,\"boss_bests\":{\"stored\":3,\"dropped\":0,\"linked\":true}}"), T0);
            Check("the site kept it: not again while nothing changed", BossBests.Pending("c1", T0.AddHours(5)) == null ? "none" : "sent", "none");
            Check("...another character's list is its own", BossBests.Pending("c2", T0) == null ? "none" : "sent", "sent");
            fights.Add(Pull("Nuakum", "kill", 80, 100, 600_000));
            Check("a new pull changes the list: it goes again", BossBests.Pending("c1", T0)?.Count.ToString() ?? "none", "3");
            BossBests.Took(p with { boss_bests = BossBests.Pending("c1", T0) }, new SendResult(true, 202, null, "{\"ok\":true,\"boss_bests\":{\"stored\":0,\"dropped\":0,\"linked\":false}}"), T0);
            Check("not linked to a profile: kept nowhere, not again for an hour", BossBests.Pending("c1", T0.AddMinutes(30)) == null ? "none" : "sent", "none");
            Check("...and again after it", BossBests.Pending("c1", T0.AddMinutes(61)) == null ? "none" : "sent", "sent");
            BossBests.Took(p, new SendResult(false, 0, null, ""), T0);
            Check("a send that failed changes nothing", BossBests.Pending("c1", T0.AddMinutes(61)) == null ? "none" : "sent", "sent");

            // ---- switched off: the site is told to forget ----
            int cleared = 0;
            BossBests.Cleared = () => cleared++;
            BossBests.Enabled = false; BossBests.ClearPending = true;
            var clear = BossBests.Pending("c1", T0);
            Check("off again: an empty list once", clear == null ? "none" : clear.Count.ToString(), "0");
            Check("...serialized as [], so the site forgets", JsonSerializer.Serialize(RunPayload.RunsOnly("na_e", "", null, new())).Contains("\"boss_bests\":[]").ToString(), "True");
            BossBests.Took(p with { boss_bests = new() }, new SendResult(true, 202, null, "{\"ok\":true,\"boss_bests\":{\"stored\":0,\"dropped\":0,\"cleared\":true,\"linked\":true}}"), T0);
            Check("...the site forgot: nothing more goes", $"{BossBests.ClearPending} {cleared} {(BossBests.Pending("c1", T0) == null ? "none" : "sent")}", "False 1 none");
        }
        finally
        {
            BossBests.Fights = keepFights; BossBests.Enabled = keepOn; BossBests.ClearPending = keepClear; BossBests.Cleared = keepCleared;
            BossBests.Reset();
        }
        Console.WriteLine(ok ? "  boss bests: all passed" : "  boss bests: MISMATCH, see above");
        return ok;
    }
}
