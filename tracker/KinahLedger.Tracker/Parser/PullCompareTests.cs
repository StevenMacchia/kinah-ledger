using KinahLedger.Packets;

namespace KinahLedger.Parser;

/// <summary>
/// parser-test's part for pull against pull (Parser/PullCompare.cs, the Parser page's Compare tab): on made-up pulls, which
/// pull is measured against by default and which can be picked, members matched by name, the figures and their changes,
/// and the losses' words; then on Steven's real recorded boss fights (kinah-bench/packets/rec-*), every pull against its
/// best other pull of the same fight, each side's figures checked against that pull's own totals as its page counts them.
/// </summary>
static class PullCompareTests
{
    static Encounter Pull(string id, DateTime start, double secs, string outcome, string mode, params (long Id, string Name, bool You, (int Skill, long Dmg, int Casts)[] Skills)[] members)
    {
        var e = new Encounter
        {
            Id = id, Mode = "boss", Kind = "Expedition", Instance = "Shattered Arkanis", GameMode = mode, Boss = "Talisra of the Void", BossId = 900 + start.Minute,
            Start = start, End = start.AddSeconds(secs), LastAt = start.AddSeconds(secs), Outcome = outcome, HasDamage = true, TargetsSplit = true,
        };
        foreach (var (mid, name, you, skills) in members)
        {
            var f = new Fighter { Id = mid, Name = name, You = you, Class = "Gladiator" };
            foreach (var (skill, dmg, casts) in skills)
            {
                f.Abilities.Add(new AbilityShare { Skill = skill, Damage = dmg, BossDamage = dmg, Hits = casts * 2, BossHits = casts * 2, Casts = casts, StreamHits = casts * 2, Kinds = new() { ["result:3"] = casts } });
                f.Damage += dmg; f.BossDamage += dmg;
            }
            e.Roster.Add(f);
        }
        e.BossDamage = e.Roster.Sum(f => f.BossDamage);
        return e;
    }

    public static void Run(Action<bool, string> check, DateTime t, Func<string, string?> recording, Func<string, List<Encounter>> replay)
    {
        // three kept pulls of Talisra on Stage 4 and one on Stage 3 (another fight): the best kill is p2 (more DPS than p1),
        // the wipe p3 had the most DPS but isn't a kill; Aerin is in every pull under another id each time
        const int S1 = 11010000, S2 = 11020000, S3 = 11050000;
        var p1 = Pull("p1", t, 100, "kill", "Stage 4", (1, "Swoly", true, new[] { (S1, 500_000L, 10), (S2, 300_000L, 6) }), (2, "Aerin", false, new[] { (S1, 200_000L, 5) }));
        var p2 = Pull("p2", t.AddMinutes(10), 100, "kill", "Stage 4", (1, "Swoly", true, new[] { (S1, 600_000L, 12), (S2, 300_000L, 6), (S3, 100_000L, 2) }), (7, "aerin", false, new[] { (S1, 260_000L, 6) }));
        var p3 = Pull("p3", t.AddMinutes(20), 40, "wipe", "Stage 4", (1, "Swoly", true, new[] { (S1, 600_000L, 11) }));
        var other = Pull("p4", t.AddMinutes(25), 60, "kill", "Stage 3", (1, "Swoly", true, new[] { (S1, 900_000L, 15) }));
        var now = Pull("now", t.AddMinutes(30), 80, "kill", "Stage 4", (1, "Swoly", true, new[] { (S1, 480_000L, 9), (S2, 320_000L, 7) }), (9, "Aerin", false, new[] { (S1, 240_000L, 6) }), (10, "Stranger", false, new[] { (S1, 50_000L, 1) }));
        var past = new List<Encounter> { p1, p2, p3, other, now };
        var others = PullCompare.Others(now, past);
        check(string.Join(",", others.Select(e => e.Id)) == "p3,p2,p1", $"compare: the other pulls of the same fight, newest first, never another stage's or itself ({string.Join(",", others.Select(e => e.Id))})");
        check(PullCompare.Best(others)?.Id == "p2", $"...by default against your best kill (p2, 10,000 DPS), not a wipe with more ({PullCompare.Best(others)?.Id})");
        var v = Ui.FightView.Of(now, now.End!.Value, past);
        var d = v.Diff;
        check(d is { B.Id: "p2", BossOnly: true } && v.HasCompare, $"...the page's view holds it ({d?.B.Id})");
        var you = d?.You;
        check(you is { DamageA: 800_000, DamageB: 1_000_000 } && Math.Abs(you.DpsA - 10_000) < 1e-6 && Math.Abs(you.DpsB - 10_000) < 1e-6 && you.DpsChange is 0,
            $"...your damage and DPS in both: 800,000 over 80 s and 1,000,000 over 100 s, the same 10,000 a second ({you?.DpsA:N0} vs {you?.DpsB:N0})");
        var s3 = you?.Skills.FirstOrDefault(s => s.Skill == Abilities.Canon(S3));
        var s1 = you?.Skills.FirstOrDefault(s => s.Skill == Abilities.Canon(S1));
        check(s1 is { DamageA: 480_000, DamageB: 600_000, CastsA: 9, CastsB: 12, CastsDelta: -3 } && Math.Abs(s1.DpsA - 6_000) < 1e-6 && s1.CritA == null,
            $"...an ability side by side: damage, DPS, casts and their change; no crit before a crit kind is picked ({s1?.DamageA:N0}/{s1?.DamageB:N0}, {s1?.CastsA}/{s1?.CastsB}, crit {s1?.CritA})");
        check(s3 is { UsedA: false, UsedB: true, DamageA: 0, CastsA: 0, CastsB: 2 } && you!.Losses().Select(x => x.Skill).SequenceEqual(new[] { Abilities.Canon(S3) }),
            $"...an ability used only in the other pull is a loss of all its DPS, and the one loss here ({string.Join(",", you?.Losses().Select(x => x.Skill) ?? Array.Empty<int>())})");
        check(Math.Abs(you!.Skills.Sum(x => x.DpsDelta) - you.DpsDelta) < 1e-6, $"...the abilities' DPS changes add up to yours ({you.Skills.Sum(x => x.DpsDelta):N1} = {you.DpsDelta:N1})");
        string name1 = Abilities.Name(Abilities.Canon(S1), "Gladiator"), name3 = Abilities.Name(Abilities.Canon(S3), "Gladiator");
        check(Ui.ParserCommand.LossWords(s3!, "Gladiator") == $"{name3}: not used this pull (1,000 DPS in that one)", $"...the loss in words: {Ui.ParserCommand.LossWords(s3!, "Gladiator")}");
        // against p1 (picked): Slash lost DPS against it? p1's S1 is 5,000/s over 100 s, now 6,000/s: a gain, with 1 less cast
        var vp = Ui.FightView.Of(now, now.End!.Value, past, against: "p1");
        var g1 = vp.Diff?.You?.Skills.FirstOrDefault(s => s.Skill == Abilities.Canon(S1));
        check(vp.Diff?.B.Id == "p1" && g1 != null && Ui.ParserCommand.GainWords(g1, "Gladiator") == $"{name1}: 20.0% more DPS (+1,000), 1 fewer cast",
            $"...a picked pull instead, and a gain in words: {(g1 != null ? Ui.ParserCommand.GainWords(g1, "Gladiator") : "none")}");
        check(Ui.FightView.Of(now, now.End!.Value, past, against: "gone").Diff?.B.Id == "p2", "...a pick that isn't a pull of this fight falls back to the best kill");
        var aerin = d!.Members.FirstOrDefault(m => !m.You);
        check(d.Members.Count == 2 && aerin is { DamageA: 240_000, DamageB: 260_000 } && Math.Abs(aerin.DpsA - 3_000) < 1e-6 && Math.Abs(aerin.ShareA - 240_000 / 1_090_000.0) < 1e-9,
            $"...a member in both pulls is matched by name (the game's id changes): Aerin 3,000 vs 2,600; the stranger only in this pull is left out ({d.Members.Count} members)");
        var key0 = Encounter.CritKey;
        try
        {
            Encounter.CritKey = "result:3";
            var c1 = PullCompare.Of(now, p2)!.You!.Skills.First(s => s.Skill == Abilities.Canon(S1));
            check(c1.CritA is { } ca && Math.Abs(ca - 0.5) < 1e-9 && c1.CritB is { } cb && Math.Abs(cb - 0.5) < 1e-9, $"...with a crit kind picked, each pull's crit rate over its hits seen ({c1.CritA:P1}, {c1.CritB:P1})");
        }
        finally { Encounter.CritKey = key0; }
        var bossView0 = Encounter.BossOnly;
        try
        {
            var old = Pull("old", t.AddMinutes(5), 100, "kill", "Stage 4", (1, "Swoly", true, new[] { (S1, 2_000_000L, 10) }));
            old.TargetsSplit = false; foreach (var f in old.Roster) { f.BossDamage = 0; foreach (var a in f.Abilities) a.BossDamage = 0; }
            var mixed = PullCompare.Of(now, old);
            check(mixed is { BossOnly: false } && mixed.You!.DamageB == 2_000_000 && mixed.You.DamageA == 800_000, "...an older pull that kept no boss damage apart: both counted over every target");
        }
        finally { Encounter.BossOnly = bossView0; }
        check(PullCompare.Of(now, new Encounter { Id = "x", Boss = "Talisra of the Void", HasDamage = true }) == null, "...a pull without you in it is nothing to compare");

        // Steven's recorded boss fights: Ferocious Horn Den three times over (rec-20261116-14), Deus Research Base twice
        // (rec-20261117-17), Ultimate Berk's three wipes (rec-20261117-18). Each kept pull against its fight's best other pull:
        // every figure on each side is that pull's own (its page's DPS, damage, ability rows and casts), and the abilities'
        // changes add up to the member's
        var recs = new[] { "20261116-14", "20261117-17", "20261117-18" }.Select(n => (Name: n, Path: recording(n))).ToList();
        if (NativeDecoder.Version < 10 || recs.Any(r => r.Path == null))
        {
            Console.WriteLine($"skip  compare on Steven's recordings: decoder interface {NativeDecoder.Version}, {recs.Count(r => r.Path != null)} of {recs.Count} recordings here (kinah-bench/packets/rec-*)");
            return;
        }
        int compared = 0, members = 0, skills = 0;
        var said = new HashSet<string>();
        var bad = new List<string>();
        foreach (var (name, path) in recs)
        {
            var pulls = replay(path!).Where(PullCompare.Comparable).ToList();
            foreach (var a in pulls)
            {
                var others2 = PullCompare.Others(a, pulls);
                if (PullCompare.Best(others2) is not { } b) continue;
                var c = PullCompare.Of(a, b);
                if (c == null) { bad.Add($"{name} {a.Boss}: no compare"); continue; }
                compared++;
                if (compared == 1 || (a.Boss == "Ultimate Berk" && !said.Contains(a.Boss)))
                {
                    said.Add(a.Boss);
                    var y = c.You!;
                    Console.WriteLine($"      e.g. {a.Boss} ({name}): you {y.DpsA:N0} vs {y.DpsB:N0} DPS, {y.Skills.Count} abilities, casts {(y.HasCasts ? "carried" : "not carried")}; "
                        + string.Join(" | ", y.Losses().Select(x => Ui.ParserCommand.LossWords(x, y.A.Class))));
                }
                // the default is the best other kill (else the best other pull)
                var kills = others2.Where(FightRecords.Counts).ToList();
                double bestDps = (kills.Count > 0 ? kills : others2).Max(PullCompare.YourDps);
                if (Math.Abs(PullCompare.YourDps(b) - bestDps) > 1e-6) bad.Add($"{name} {a.Boss}: against {PullCompare.YourDps(b):N0}, best {bestDps:N0}");
                foreach (var m in c.Members)
                {
                    members++;
                    foreach (var (e, f, dmg, dps, secs, at) in new[] { (a, m.A, m.DamageA, m.DpsA, m.SecondsA, c.AAt), (b, m.B, m.DamageB, m.DpsB, m.SecondsB, c.BAt) })
                    {
                        // the member's own figures, as the pull's page counts them
                        if (dmg != e.Figure(f)) bad.Add($"{name} {e.Boss} {f.Name}: damage {dmg:N0} vs the pull's {e.Figure(f):N0}");
                        if (Math.Abs(dps - e.Dps(f, at)) > 1e-6) bad.Add($"{name} {e.Boss} {f.Name}: DPS {dps:N3} vs the pull's {e.Dps(f, at):N3}");
                        // each ability's damage is the pull's own row for it (Encounter.AbilityRows, the inspect view's)
                        var rows = e.AbilityRows(f).ToDictionary(r => r.Skill);
                        bool aSide = e == a;
                        foreach (var s in m.Skills)
                        {
                            long mine = aSide ? s.DamageA : s.DamageB;
                            long own = rows.TryGetValue(s.Skill, out var r) ? e.Figure(r) : 0;
                            if (mine != own) bad.Add($"{name} {e.Boss} {f.Name} {s.Skill}: {mine:N0} vs the row's {own:N0}");
                            int? casts = aSide ? s.CastsA : s.CastsB;
                            if (casts != null && r != null && r.Casts is { } rc && casts != rc) bad.Add($"{name} {e.Boss} {f.Name} {s.Skill}: {casts} casts vs the row's {rc}");
                        }
                        if (rows.Keys.Except(m.Skills.Select(s => s.Skill)).Any()) bad.Add($"{name} {e.Boss} {f.Name}: a row missing from the compare");
                        long sum = m.Skills.Sum(s => aSide ? s.DamageA : s.DamageB);
                        if (sum != rows.Values.Sum(e.Figure)) bad.Add($"{name} {e.Boss} {f.Name}: abilities sum {sum:N0} vs rows {rows.Values.Sum(e.Figure):N0}");
                    }
                    skills += m.Skills.Count;
                    // the abilities' DPS changes add up to the member's, to the part of their damage the abilities name
                    double named = m.Skills.Sum(s => s.DpsDelta), whole = (m.Skills.Sum(s => s.DamageA) / m.SecondsA) - (m.Skills.Sum(s => s.DamageB) / m.SecondsB);
                    if (Math.Abs(named - whole) > 1e-3) bad.Add($"{name} {a.Boss} {m.A.Name}: ability changes {named:N2} vs {whole:N2}");
                }
            }
        }
        check(compared >= 15 && bad.Count == 0,
            $"compare on Steven's recordings: {compared} pulls each against its fight's best other pull, {members} members, {skills} abilities; every figure the pull's own{(bad.Count > 0 ? ": " + string.Join("; ", bad.Take(6)) : "")}");
    }
}
