using KinahLedger.Packets;

namespace KinahLedger.Parser;

/// <summary>
/// parser-test's part for the Timeline tab (Parser/PullTimeline.cs, Ui/ParserTimeline.cs): on a made-up pull fed through the
/// Parser, what it keeps for the timeline (your abilities' seconds, each cast as it landed with its hits' damage, your biggest
/// hits) and what the timeline makes of it (lanes, the fold, the HP's phases, the spikes, deaths); then on Steven's real
/// recorded boss fights (kinah-bench/packets/rec-*): every kept pull's lanes add up to the pull's own ability rows and your
/// damage, its casts are the rows' casts, and its deaths are the fight's deaths, each with its recap.
/// </summary>
static class PullTimelineTests
{
    public static void Run(Action<bool, string> check, DateTime t, Func<(BossParser P, List<Encounter> Kept)> fresh, GroupMember me, GroupMember ally,
        Func<string, string?> recording, Func<string, List<Encounter>> replay)
    {
        // ---- a made-up pull through the Parser: two casts of one skill (one hit twice), a skill with no cast, a death, a kill
        {
            var (p, kept) = fresh();
            const int Slash = 12240000, Burst = 12250000, Dot = -12240000;
            p.Take(new InstanceEntered(t, ContentKind.Expedition, "Shattered Arkanis", 880));
            p.Take(new GroupChanged(t, new[] { me, ally }));
            p.Take(new BossEngaged(t.AddSeconds(1), "Talisra of the Void", 881));
            p.Take(new BossHealth(t.AddSeconds(1), 881, 100_000, 100_000));
            // cast 7 of Slash: two hits (3,000 and 1,000) and a part with no hit under it
            p.Take(new CastMade(t.AddSeconds(1.4), 1, 1, Slash, 7));
            p.Take(new HitDealt(t.AddSeconds(1.5), 1, 1, 881, 3_000, Slash, 7, 1, false, false, 3, 0, 0, 0));
            p.Take(new HitDealt(t.AddSeconds(1.5), 1, 1, 881, 1_000, Slash, 7, 2, false, false, 2, 0, 0, 0));
            p.Take(new HitDealt(t.AddSeconds(1.5), 1, 1, 881, 200, Slash, 0, 0, true, false, 0, 0, 0, 0));
            p.Take(new DamageDealt(t.AddSeconds(1.5), 1, 881, 4_200, Skill: Slash, Hits: 2));
            p.Take(new BossHealth(t.AddSeconds(1.5), 881, 95_800, 100_000));
            // Aerin hits too (no lane of hers: the lanes are yours), then a damage over time tick of yours
            p.Take(new HitDealt(t.AddSeconds(2.2), 2, 2, 881, 9_000, 16010000, 3, 1, false, false, 2, 0, 0, 0));
            p.Take(new DamageDealt(t.AddSeconds(2.2), 2, 881, 9_000, Skill: 16010000, Hits: 1));
            p.Take(new DamageDealt(t.AddSeconds(3.1), 1, 881, 600, Skill: Dot, Hits: 1));
            p.Take(new BossHealth(t.AddSeconds(3.1), 881, 86_200, 100_000));
            // Burst at 4.0 s (one hit of 20,000: the biggest), Slash again at 6.3 s (a new cast, the same cast number reused)
            p.Take(new CastMade(t.AddSeconds(3.9), 1, 1, Burst, 8));
            p.Take(new HitDealt(t.AddSeconds(4.0), 1, 1, 881, 20_000, Burst, 8, 1, false, false, 3, 0, 0, 0));
            p.Take(new DamageDealt(t.AddSeconds(4.0), 1, 881, 20_000, Skill: Burst, Hits: 1));
            p.Take(new BossHealth(t.AddSeconds(4.0), 881, 66_200, 100_000));
            p.Take(new HitTaken(t.AddSeconds(5.0), 881, 2, 7_000, 9000100, 2300908));
            p.Take(new UnitDied(t.AddSeconds(5.2), 2, "Aerin", false, 881, 9000100));
            p.Take(new CastMade(t.AddSeconds(6.2), 1, 1, Slash, 7));
            p.Take(new HitDealt(t.AddSeconds(6.3), 1, 1, 881, 2_500, Slash, 7, 1, false, false, 2, 0, 0, 0));
            p.Take(new DamageDealt(t.AddSeconds(6.3), 1, 881, 2_500, Skill: Slash, Hits: 1));
            p.Take(new BossHealth(t.AddSeconds(6.3), 881, 40_000, 100_000));
            p.Take(new DamageDealt(t.AddSeconds(9.0), 2, 881, 40_000, Skill: 16010000, Hits: 1));
            p.Take(new BossHealth(t.AddSeconds(9.0), 881, 0, 100_000));
            p.Take(new UnitDied(t.AddSeconds(9.0), 881, "Talisra of the Void", true));
            var e = kept.Single();
            var you = e.You!; var aerin = e.Members.Single(f => f.Id == 2);
            var slash = you.Abilities.Single(a => a.Skill == Slash);
            check(you.Abilities.All(a => a.Seconds!.Values.Sum() == a.Damage && a.BossSeconds!.Values.Sum() == a.BossDamage) && aerin.Abilities.All(a => a.Seconds == null && a.CastLog == null),
                $"timeline: each of your abilities' damage by second adds up to its damage and its boss damage; a member's other than yours keeps none ({string.Join(" ", slash.Seconds!.OrderBy(x => x.Key).Select(x => $"{x.Key}:{x.Value}"))})");
            double s0 = (t.AddSeconds(1.5) - e.Start).TotalSeconds;
            check(slash.CastLog is { Count: 2 } cl && slash.Casts == 2 && cl[0].Damage == 4_000 && cl[0].Hits == 2 && Math.Abs(cl[0].At - s0) < 0.011 && cl[1].Damage == 2_500 && Math.Abs(cl[1].At - (s0 + 4.8)) < 0.011,
                $"...each cast as it landed: when, its hits' damage (the part with no hit left out) and their count, as many as the casts counted ({string.Join(", ", slash.CastLog?.Select(c => $"{c.At}s {c.Damage}/{c.Hits}") ?? Array.Empty<string>())})");
            check(you.TopHits?.Select(h => h.Amount).SequenceEqual(new long[] { 20_000, 3_000, 2_500, 1_000 }) == true && you.TopHits[0].Skill == Burst && you.TopHits[0].Kinds!.Contains("result:3") && aerin.TopHits == null,
                $"...your biggest hits, biggest first, with their kinds; none kept for another member ({string.Join(" ", you.TopHits?.Select(h => h.Amount) ?? Array.Empty<long>())})");
            var back = System.Text.Json.JsonSerializer.Deserialize<Encounter>(System.Text.Json.JsonSerializer.Serialize(e))!;
            var bs = back.You!.Abilities.Single(a => a.Skill == Slash);
            check(bs.CastLog!.Count == 2 && bs.CastLog[1].Damage == 2_500 && bs.Seconds!.Values.Sum() == slash.Damage && back.You.TopHits!.Count == 4,
                "...kept and read back whole");

            var tl = PullTimeline.Of(back)!;
            check(tl != null && tl.Abilities.Count == 3 && tl.Abilities.All(l => l.LaneDamage == l.Damage) && tl.Abilities.Sum(l => l.Damage) == back.Figure(back.You!) && tl.Folded == null,
                $"...the timeline: a lane for each of your abilities, each adding up to the pull's own row, all of them to your damage ({string.Join(", ", tl?.Abilities.Select(l => $"{l.Skill} {l.LaneDamage}/{l.Damage}") ?? Array.Empty<string>())})");
            var lane = tl!.Abilities.First(l => l.Skill == Abilities.Canon(Slash));
            check(lane.Casts?.Count == 2 && lane.CastCount == 2, $"...a lane's casts are the row's casts ({lane.Casts?.Count}, {lane.CastCount})");
            check(tl.Deaths.Count == 1 && tl.DeathsCounted == 1 && tl.Deaths[0].F.Id == 2 && tl.Deaths[0].Last.Single().Amount == 7_000,
                $"...the deaths: Aerin's, with her last hit taken ({tl.Deaths.Count} of {tl.DeathsCounted})");
            check(tl.Members.Count == 2 && tl.Members.All(m => m.PerSecond.Sum() == m.Damage) && tl.Group.Sum() == back.Members.Sum(back.Figure),
                "...each member's damage by second adds up to theirs, the group's to the group's");
            check(tl.Spikes.Count >= 1 && tl.Spikes.OrderByDescending(s => s.Damage).First().Damage == tl.Group.Max(),
                $"...the spikes: the group's biggest second first ({string.Join(" ", tl.Spikes.Select(s => $"{s.Second}:{s.Damage}"))})");
            check(tl.Phases.Select(ph => ph.Mark).SequenceEqual(new[] { 0.75, 0.5, 0.25 }) && tl.Phases.Zip(tl.Phases.Skip(1)).All(x => x.First.At <= x.Second.At),
                $"...the boss's HP crossed 75, 50 and 25%, in order ({string.Join(" ", tl.Phases.Select(ph => $"{ph.Mark:P0}@{ph.At}"))})");
            var hpCurve = new[] { 0.9, 0.8, 0.7, 0.6, 0.5 };
            var ph = PullTimeline.PhasesOf(hpCurve);
            check(ph.Count == 2 && Math.Abs(ph[0].At - 2.5) < 1e-9 && Math.Abs(ph[1].At - 5) < 1e-9,
                $"...a crossing falls between the two seconds around it: 75% at 2.5 s, 50% at 5 s ({string.Join(" ", ph.Select(x => $"{x.Mark}@{x.At}"))})");
            check(PullTimeline.PhasesOf(new[] { 0.6, 0.55, 0.4 }) is var late && late.Count == 1 && late[0].Mark == 0.5,
                "...a pull that began under 75% (the boss already hurt) never crossed it");
            check(!PullTimeline.Has(Saved()), "...a pull kept before the timeline (no lanes kept) has no Timeline tab");
        }
        // the fold: past six lanes, the rest are one lane that adds up to them
        {
            var e = new Encounter { Id = "f", Mode = "boss", Kind = "Expedition", Instance = "Fire Temple", Boss = "Kromede", Start = t, End = t.AddSeconds(60), LastAt = t.AddSeconds(60), Outcome = "kill", HasDamage = true, TargetsSplit = true };
            var f = new Fighter { Id = 1, Name = "Swoly", You = true, Class = "Gladiator" };
            for (int i = 0; i < 10; i++)
            {
                var a = f.AddAbility(11010000 + i * 10000, 1_000 * (10 - i), 1, boss: true);
                a.Seconds = new() { [i] = a.Damage }; a.BossSeconds = new() { [i] = a.Damage };
                a.CastLog = new() { new CastMark { At = i + 0.5, Damage = a.Damage, Hits = 1 } }; a.Casts = 1;
                f.Damage += a.Damage; f.BossDamage += a.Damage;
            }
            e.Roster.Add(f);
            var tl = PullTimeline.Of(e)!;
            check(tl.Folded is { } fd && fd.Of!.Count == 4 && fd.Damage == 1_000 + 2_000 + 3_000 + 4_000 && fd.LaneDamage == fd.Damage && fd.Casts!.Count == 4 && tl.Abilities.Take(PullTimeline.TopLanes).All(l => l.Damage >= 5_000),
                $"timeline: past {PullTimeline.TopLanes} lanes the rest fold into one lane that adds up to them ({tl.Folded?.Of?.Count} folded, {tl.Folded?.Damage:N0})");
        }

        // ---- Steven's recorded boss fights: every kept pull's timeline against the pull's own figures and deaths
        var names = new[] { "20261116-14", "20261117-17", "20261117-18", "20261118-12", "20261119-13-nightmare-aponos" };
        var recs = names.Select(n => (Name: n, Path: recording(n))).Where(r => r.Path != null).ToList();
        if (NativeDecoder.Version < 10 || recs.Count < 3)
        {
            Console.WriteLine($"skip  timeline on Steven's recordings: decoder interface {NativeDecoder.Version}, {recs.Count} of {names.Length} recordings here (kinah-bench/packets/rec-*)");
            return;
        }
        int pulls = 0, lanes = 0, casts = 0, deaths = 0, recaps = 0, phases = 0, withHp = 0;
        var bad = new List<string>();
        var said = new HashSet<string>();
        foreach (var (name, path) in recs)
        {
            foreach (var e in replay(path!).Where(x => x.ModeKind == ParserMode.Bosses && x.Boss.Length > 0 && x.HasDamage && x.You != null))
            {
                // a round trip first: the page reads a kept pull back from the store
                var k = System.Text.Json.JsonSerializer.Deserialize<Encounter>(System.Text.Json.JsonSerializer.Serialize(e))!;
                var tl = PullTimeline.Of(k);
                if (tl == null) { if (k.You!.Damage > 0) bad.Add($"{name} {k.Boss}: no timeline though you dealt {k.You.Damage:N0}"); continue; }
                pulls++;
                var you = k.You!;
                var rows = k.AbilityRows(you);
                if (tl.Abilities.Count != rows.Count) bad.Add($"{name} {k.Boss}: {tl.Abilities.Count} lanes vs {rows.Count} rows");
                foreach (var (l, r) in tl.Abilities.Zip(rows))
                {
                    lanes++;
                    if (l.Skill != r.Skill || l.Damage != k.Figure(r)) bad.Add($"{name} {k.Boss} {l.Skill}: lane {l.Damage:N0} vs row {k.Figure(r):N0}");
                    if (l.LaneDamage != l.Damage) bad.Add($"{name} {k.Boss} {l.Name}: seconds add to {l.LaneDamage:N0}, the row says {l.Damage:N0}");
                    if (l.Casts != null || r.Casts != null)
                    {
                        if ((l.Casts?.Count ?? 0) != (r.Casts ?? 0)) bad.Add($"{name} {k.Boss} {l.Name}: {l.Casts?.Count ?? 0} cast marks vs {r.Casts ?? 0} casts");
                        casts += l.Casts?.Count ?? 0;
                        if (l.Casts?.Any(c => c.At < 0 || c.At > tl.N + 1 || c.Hits < 1) == true) bad.Add($"{name} {k.Boss} {l.Name}: a cast mark out of the pull");
                    }
                }
                if (tl.Abilities.Sum(l => l.LaneDamage) != k.Figure(you)) bad.Add($"{name} {k.Boss}: lanes {tl.Abilities.Sum(l => l.LaneDamage):N0} vs your {k.Figure(you):N0}");
                if (tl.Folded is { } fd && (fd.LaneDamage != fd.Of!.Sum(x => x.LaneDamage) || fd.Damage != fd.Of.Sum(x => x.Damage))) bad.Add($"{name} {k.Boss}: the folded lane doesn't add up");
                // the deaths: the fight's own count, each on the timeline with its recap
                int counted = k.Members.Sum(f => f.Deaths);
                if (tl.DeathsCounted != counted || tl.Deaths.Count != counted) bad.Add($"{name} {k.Boss}: {tl.Deaths.Count} deaths on the timeline, {counted} in the fight");
                deaths += tl.Deaths.Count; recaps += tl.Deaths.Count(d => d.Last.Count > 0);
                if (tl.Deaths.Any(d => d.At < 0 || d.At > tl.N + 1)) bad.Add($"{name} {k.Boss}: a death out of the pull");
                if (tl.Members.Any(m => m.PerSecond.Sum() != m.Damage)) bad.Add($"{name} {k.Boss}: a member's seconds don't add up");
                if (tl.BigHits.Count > 0 && tl.BigHits.Max(h => h.Amount) != you.Abilities.Max(a => a.Max ?? 0)) bad.Add($"{name} {k.Boss}: biggest hit {tl.BigHits.Max(h => h.Amount):N0} vs your max {you.Abilities.Max(a => a.Max ?? 0):N0}");
                if (tl.Hp != null) { withHp++; phases += tl.Phases.Count; if (tl.Phases.Zip(tl.Phases.Skip(1)).Any(x => x.First.At > x.Second.At)) bad.Add($"{name} {k.Boss}: phases out of order"); }
                if (k.Outcome == "kill" && tl.Hp != null && tl.Phases.Count < 3 && tl.Hp[0] >= 0.8) bad.Add($"{name} {k.Boss}: a kill from full with {tl.Phases.Count} phases");
                if (said.Add(name))
                    Console.WriteLine($"      e.g. {k.Boss} ({name}, {k.Outcome}, {PK0(tl.Length)}): {tl.Abilities.Count} lanes, {tl.Abilities.Sum(l => l.Casts?.Count ?? 0)} casts, top {tl.Abilities.FirstOrDefault()?.Name} "
                        + $"{tl.Abilities.FirstOrDefault()?.Share:P0}; {tl.Deaths.Count} deaths; phases {string.Join(" ", tl.Phases.Select(p => $"{p.Mark:P0}@{p.At:0.0}"))}; biggest hit {tl.BigHits.FirstOrDefault()?.Amount:N0}");
            }
        }
        check(pulls >= 15 && bad.Count == 0,
            $"timeline on Steven's recordings: {pulls} pulls, {lanes} ability lanes each adding up to its row, {casts:N0} cast marks equal to the casts counted, {deaths} deaths ({recaps} with a recap) equal to the fights', "
            + $"{phases} HP phases in {withHp} pulls with HP{(bad.Count > 0 ? ": " + string.Join("; ", bad.Take(6)) : "")}");
    }

    static string PK0(double s) => BossParser.Clock(TimeSpan.FromSeconds(s));

    /// <summary>A pull as kept before 1.0.38: its abilities with no seconds or casts.</summary>
    static Encounter Saved()
    {
        var e = new Encounter { Id = "o", Mode = "boss", Boss = "Kromede", Start = DateTime.UtcNow, End = DateTime.UtcNow.AddSeconds(30), HasDamage = true };
        var f = new Fighter { Id = 1, You = true, Name = "Swoly", Damage = 100 };
        f.AddAbility(11010000, 100, 1);
        e.Roster.Add(f);
        return e;
    }
}
