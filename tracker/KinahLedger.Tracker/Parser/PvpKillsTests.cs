using KinahLedger.Packets;

namespace KinahLedger.Parser;

/// <summary>
/// parser-test's part for the PvP panel (Parser/PvpKills.cs), on made-up fight events: the last five kills newest first,
/// Kill or Assist (and who landed an assist's kill), your damage on each from your hits in that fight only, the breakdown
/// (damage, hits, biggest hit by skill) matching the hits, and the panel's record the meter's looks draw.
/// </summary>
static class PvpKillsTests
{
    const FightFlags Enemy = FightFlags.VictimPlayer | FightFlags.VictimEnemy;

    static FightEvent Hit(DateTime at, uint victim, string name, int skill, long dmg) => new(at, FightKind.Hit, Enemy, victim, 1, (uint)skill, dmg, 0, name, "");
    static FightEvent Mine(DateTime at, uint victim, string name, FightFlags extra = Enemy) => new(at, FightKind.Kill, FightFlags.Mine | extra, victim, 1, 0, 0, 0, name, "");
    static FightEvent Others(DateTime at, uint victim, string name, string killer, FightFlags extra = Enemy) => new(at, FightKind.Kill, FightFlags.OtherKiller | extra, victim, 9, 0, 0, 0, name, killer);

    public static void Run(Action<bool, string> check, DateTime t)
    {
        // four fights: Aster killed (two skills, a rank of one), Bram assisted (Tilly's kill, your hits 6 s before), Cyra killed
        // with no hit of yours seen (another faction's player you can't hit is never one of these: a kill all the same, no
        // damage), Dace killed after an earlier fight with them 40 s before (left out), then Eske and Fion. The panel keeps the last five.
        var p = new PvpKills { Self = () => ("", "Templar"), Place = "Abyss" };
        int changed = 0; p.Changed += () => changed++;
        p.Take(Hit(t, 101, "Aster", 12090000, 10_000));
        p.Take(Hit(t.AddSeconds(1), 101, "Aster", 12060000, 25_000));
        p.Take(Hit(t.AddSeconds(2), 101, "Aster", 12060003, 5_000));   // a rank of the same skill: one row in the breakdown
        p.Take(Hit(t.AddSeconds(3), 101, "Aster", 12090000, 30_000));
        p.Take(Mine(t.AddSeconds(4), 101, "Aster"));
        p.Take(Hit(t.AddSeconds(10), 102, "Bram", 12040000, 7_500));
        p.Take(Hit(t.AddSeconds(12), 102, "Bram", 12040000, 8_500));
        p.Take(Others(t.AddSeconds(18), 102, "Bram", "Tilly"));
        p.Take(Mine(t.AddSeconds(30), 103, "Cyra", FightFlags.VictimPlayer));
        check(p.Last.Count == 3 && changed == 3, $"PvP panel: a kill, an assist and a kill with no hit seen are three rows ({p.Last.Count}, {changed} changes)");
        var a0 = p.Last.Last();
        check(a0 is { Name: "Aster", Assist: false, Damage: 70_000, Hits: 4 } && a0.Fight.Start == t && a0.Fight.End == t.AddSeconds(4),
            $"...Aster: a Kill, your 70,000 damage in 4 hits from your first hit to the death ({a0.Name}, {a0.Damage:N0}, {a0.Hits}, {a0.Fight.Start:HH:mm:ss} to {a0.Fight.End:HH:mm:ss})");
        var you = a0.Fight.You!;
        var rows = a0.Fight.AbilityRows(you);
        check(rows.Count == 2 && rows[0].Skill == Abilities.Canon(12090000) && rows[0].Damage == 40_000 && rows[0].Hits == 2 && rows[0].Max == 30_000
            && rows[1].Damage == 30_000 && rows[1].Hits == 2 && rows[1].Max == 25_000 && rows[1].Min == 5_000 && rows.Sum(r => r.Damage) == you.Damage,
            $"...its breakdown is the hits: by skill, a skill's ranks one row, damage, hits, biggest and smallest ({string.Join("; ", rows.Select(r => $"{r.Skill} {r.Damage:N0}/{r.Hits}/{r.Max}"))})");
        check(you is { You: true, Class: "Templar" } && you.Abilities.All(a => a.Kinds == null), "...your row in it is yours, your class's skill names, no crit made up (the game's hits on players carry no kind)");
        var b0 = p.Last[1];
        check(b0 is { Name: "Bram", Assist: true, Killer: "Tilly", Damage: 16_000, Hits: 2 }, $"...Bram: an Assist, Tilly's kill, your 16,000 damage in 2 hits ({b0.Assist}, {b0.Killer}, {b0.Damage:N0})");
        var c0 = p.Last[0];
        check(c0 is { Name: "Cyra", Assist: false, Damage: 0 } && Encounter.PvpFigure(c0) == "–", $"...Cyra: a Kill with no hit of yours seen shows no damage, never 0 made into a figure ({Encounter.PvpFigure(c0)})");

        p.Take(Hit(t.AddSeconds(40), 104, "Dace", 12010000, 99_000));   // an earlier fight with Dace, 40 s before the one that kills
        p.Take(Hit(t.AddSeconds(80), 104, "Dace", 12010000, 12_000));
        p.Take(Hit(t.AddSeconds(85), 104, "Dace", 12100000, 13_000));
        p.Take(Mine(t.AddSeconds(86), 104, "Dace"));
        var names = p.Last.Select(k => k.Name).ToList();
        check(names.SequenceEqual(new[] { "Dace", "Cyra", "Bram", "Aster" }), $"...a fourth kill: four rows, newest first ({string.Join(", ", names)})");
        check(p.Last[0] is { Damage: 25_000, Hits: 2 } d0 && d0.Fight.Start == t.AddSeconds(80),
            $"...Dace's damage is the fight that killed them: an earlier fight more than {PvpKills.FightGapSeconds:0} s before is left out ({p.Last[0].Damage:N0})");
        p.Take(Hit(t.AddSeconds(100), 105, "Eske", 12010000, 8_000));
        p.Take(Mine(t.AddSeconds(101), 105, "Eske"));
        p.Take(Hit(t.AddSeconds(120), 106, "Fion", 12040000, 6_000));
        p.Take(Others(t.AddSeconds(125), 106, "Fion", "Tilly"));
        names = p.Last.Select(k => k.Name).ToList();
        check(PvpKills.Kept == 5 && names.SequenceEqual(new[] { "Fion", "Eske", "Dace", "Cyra", "Bram" }), $"...a sixth kill: the last five, newest first, the oldest gone ({string.Join(", ", names)})");

        // what is never one: the same death twice, your own death, a monster, an ally's death, a death by others with no hit of yours
        var q = new PvpKills();
        q.Take(Hit(t, 201, "Eryn", 12010000, 5_000));
        q.Take(Mine(t.AddSeconds(1), 201, "Eryn"));
        q.Take(Others(t.AddSeconds(2), 201, "Eryn", "Tilly"));   // the same death said again
        q.Take(new FightEvent(t.AddSeconds(3), FightKind.Kill, FightFlags.VictimMe | FightFlags.VictimPlayer, 1, 301, 0, 0, 0, "", "Gorm"));
        q.Take(new FightEvent(t.AddSeconds(4), FightKind.Kill, FightFlags.Mine | FightFlags.VictimMonster, 401, 1, 5001, 0, 0, "", ""));
        q.Take(Others(t.AddSeconds(5), 202, "Fenna", "Gorm", FightFlags.VictimPlayer));   // an ally (no enemy flag)
        q.Take(Others(t.AddSeconds(6), 203, "Hale", "Tilly"));   // no hit of yours on Hale
        q.Take(Hit(t.AddSeconds(20), 203, "Hale", 12010000, 9_000));   // a hit long after: no assist
        check(q.Last.Count == 1 && q.Last[0].Name == "Eryn", $"PvP panel: one death counts once; your death, a monster, an ally and a death you didn't hit are none ({string.Join(", ", q.Last.Select(k => k.Name))})");

        // your hit landing a moment after the kill row (the kill tally's HitAfter): still an assist
        var r = new PvpKills();
        r.Take(Others(t, 301, "Iven", "Tilly"));
        r.Take(Hit(t.AddSeconds(FightTally.HitAfter - 0.2), 301, "Iven", 12010000, 4_000));
        check(r.Last is [{ Name: "Iven", Assist: true, Damage: 4_000 }], $"...your hit {FightTally.HitAfter - 0.2:0.0} s after the kill row makes it an assist, as the kill tally counts it ({r.Last.Count})");

        // the panel the meter's looks draw: a row per kill, newest first; its figure your damage; Kill or Assist; a row's card your fight with them
        var b = p.Board(t.AddSeconds(90));
        check(b.Pvp != null && b.Shown(t).Select(f => f.Name).SequenceEqual(names) && b.Ranked().Select(f => f.Name).SequenceEqual(names),
            "the panel's record: one row per kill, newest first (never ranked by damage)");
        var first = b.Roster.First(f => f.Name == "Dace");
        check(b.DpsOf(first, t.AddSeconds(90)) == 25_000 && Encounter.PvpWord(b.PvpOf(first)!) == "Kill" && Encounter.PvpWord(b.PvpOf(b.Roster[0])!) == "Assist" && Encounter.PvpBy(b.PvpOf(b.Roster[0])!) == "by Tilly",
            "...its figure is your damage on them; the share column Kill or Assist; an assist says who landed the kill");
        var (ce, cf) = b.CardOf(first);
        check(ReferenceEquals(ce, p.Last.First(k => k.Name == "Dace").Fight) && cf.You && cf.Damage == 25_000 && ce.DpsOf(cf, t)! > 0,
            "...a row's skills card is your fight with that player: your row, its own time");
        check(b.Title == "Last PvP kills" && b.ClockText(t) == "" && !b.HeldAverage && !b.Live && b.Kind == "Abyss", "...titled, no clock, no fight's average badge, where it is");
        var empty = PvpKills.BoardOf(Array.Empty<PvpKill>(), "Abyss", t);
        check(empty is { Waiting: true, HasDamage: false } && empty.ReadyText == "No PvP kills yet", "...before a kill: a waiting card that says so");

        // Combat Power beside the other player's name (Steven, 2026-11-23): the tracker's figure when it has one, nothing when not
        var cps = new Dictionary<string, long> { ["Jory"] = 178_340, ["Kess"] = 142_960, ["Nyx"] = 231_550 };
        var c = new PvpKills { Self = () => ("", "Templar"), CpOf = (_, n) => cps.TryGetValue(n, out var v) ? v : 0 };
        c.Take(Hit(t, 501, "Jory", 12010000, 20_000));
        c.Take(Mine(t.AddSeconds(1), 501, "Jory"));
        c.Take(Hit(t.AddSeconds(10), 502, "Kess", 12010000, 9_000));
        c.Take(Others(t.AddSeconds(11), 502, "Kess", "Tilly"));
        c.Take(Hit(t.AddSeconds(20), 503, "Lune", 12010000, 9_000));
        c.Take(Mine(t.AddSeconds(21), 503, "Lune"));
        var (jory, kess, lune) = (c.Last[2], c.Last[1], c.Last[0]);
        check(jory.Cp == 178_340 && Encounter.PvpBy(jory) == "CP 178.3K", $"PvP panel: a kill shows the player's Combat Power beside the name ({Encounter.PvpBy(jory)})");
        check(kess.Cp == 142_960 && Encounter.PvpBy(kess) == "CP 143K · by Tilly", $"...an assist its CP and who landed the kill ({Encounter.PvpBy(kess)})");
        check(lune.Cp == 0 && Encounter.PvpBy(lune) == "" && c.Board(t).Roster.First(f => f.Name == "Lune").CombatPower == 0,
            $"...an unknown CP shows nothing, no placeholder, no guess (\"{Encounter.PvpBy(lune)}\")");

        // your death at a player's hand: the panel's last row, "Killed by Nyx", their CP, your damage on them, until your next kill
        int was = c.Version;
        c.Take(Hit(t.AddSeconds(30), 504, "Nyx", 12010000, 31_000));
        c.Take(Hit(t.AddSeconds(32), 504, "Nyx", 12040000, 14_000));
        c.Take(new FightEvent(t.AddSeconds(34), FightKind.Kill, FightFlags.VictimMe | FightFlags.VictimPlayer, 1, 504, 0, 0, 0, "", "Nyx"));
        c.Take(new FightEvent(t.AddSeconds(35), FightKind.Kill, FightFlags.VictimMe | FightFlags.VictimPlayer, 1, 504, 0, 0, 0, "", "Nyx"));   // the same death said again
        var died = c.LastDeath;
        var db = c.Board(t.AddSeconds(40));
        var last = db.Roster[^1];
        check(died is { Death: true, Name: "Nyx", Cp: 231_550, Damage: 45_000 } && c.Version == was + 1 && c.Last.Count == 3,
            $"...your death: Killed by Nyx, their CP, your 45,000 damage on them, once, never a kill ({died?.Name}, {died?.Cp:N0}, {died?.Damage:N0}, version +{c.Version - was})");
        check(db.Roster.Count == 4 && last is { Name: "Nyx", Class: Ui.ClassArt.PvpDeath, CombatPower: 231_550 } && Ui.ParserRows.NameOf(last) == "Killed by Nyx" && Ui.ClassArt.KillerOf("Killed by Nyx") == "Nyx" && Encounter.PvpWord(db.PvpOf(last)!) == "Died"
            && Encounter.PvpBy(db.PvpOf(last)!) == "CP 231.6K" && db.DpsOf(last, t) == 45_000 && db.PvpFoot == "2 kills · 1 assist · 83K dealt",
            $"...the panel's last row says Killed by Nyx, Died, their CP, the loss's own tint; the foot counts kills, the damage all of it ({last.Name}, {last.Class}, {db.PvpFoot})");
        var (de, df) = db.CardOf(last);
        check(de.Outcome == "died" && df is { You: true, Damage: 45_000 }, $"...its skills card is your fight with your killer ({de.Outcome}, {df.Damage:N0})");
        c.Take(new FightEvent(t.AddSeconds(50), FightKind.Kill, FightFlags.VictimMe, 1, 0, 9001, 0, 0, "", ""));   // a monster's: no player named
        check(c.LastDeath?.Name == "Nyx", "...a death to a monster leaves the Killed by row as it was");
        c.Take(Hit(t.AddSeconds(60), 505, "Mira", 12010000, 9_000));
        c.Take(Mine(t.AddSeconds(61), 505, "Mira"));
        check(c.LastDeath == null && c.Board(t).Roster.All(f => f.Class != Ui.ClassArt.PvpDeath) && c.Last[0].Name == "Mira", "...your next kill clears it");
        var lone = new PvpKills { CpOf = (_, _) => 0 };
        lone.Take(new FightEvent(t, FightKind.Kill, FightFlags.VictimMe | FightFlags.VictimPlayer, 1, 601, 0, 0, 0, "", "Orin"));
        var lb = lone.Board(t);
        check(lb is { Waiting: false, HasDamage: true } && lb.Roster is [{ Name: "Orin", Class: Ui.ClassArt.PvpDeath }] && Encounter.PvpBy(lb.PvpOf(lb.Roster[0])!) == "" && Encounter.PvpFigure(lb.PvpOf(lb.Roster[0])!) == "–",
            "...a death before any kill: the panel is its Killed by row alone (no CP known: nothing; no hit of yours: –)");
    }
}
