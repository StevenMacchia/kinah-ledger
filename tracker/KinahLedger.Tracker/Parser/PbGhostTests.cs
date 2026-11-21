using System.Text.Json;
using KinahLedger.Packets;

namespace KinahLedger.Parser;

/// <summary>
/// parser-test's part for the PB ghost (Parser/PbGhost.cs): on made-up pulls, the curve kept with the record (set by a new best,
/// written and read back, found for an older record among the fights kept), the race lined up by the boss's HP or by the time,
/// and when it says nothing; then on Steven's real recorded boss fights (kinah-bench/packets/rec-*), each kill raced against the
/// best kill of its fight before it, as the records would have kept it: the live pace at the kill must be the final DPS
/// difference, the PB chip's figure, exactly, and the pace the meter showed in the pull's last second must be close to it.
/// </summary>
static class PbGhostTests
{
    /// <summary>A kept pull of Talisra with your damage <paramref name="perSecond"/> each second (and a member's), the boss falling evenly to its death.</summary>
    static Encounter Pull(string id, DateTime start, long[] perSecond, string outcome = "kill", long otherPerSecond = 1_000)
    {
        int n = perSecond.Length;
        var e = new Encounter
        {
            Id = id, Mode = "boss", Kind = "Expedition", Instance = "Shattered Arkanis", GameMode = "Stage 4", Boss = "Talisra of the Void", BossId = 900,
            Start = start, End = start.AddSeconds(n), LastAt = start.AddSeconds(n), Outcome = outcome, HasDamage = true, TargetsSplit = true,
            BossMaxHp = 1_000_000, BossHp = outcome == "kill" ? 0 : 400_000,
            BossHpPerSecond = Enumerable.Range(0, n).Select(i => outcome == "kill" ? 1 - (i + 1) / (double)n : 1 - 0.6 * (i + 1) / n).ToList(),
        };
        var you = new Fighter { Id = 1, Name = "Swoly", You = true, Class = "Gladiator", BossPerSecond = perSecond.ToList(), PerSecond = perSecond.ToList() };
        you.Damage = you.BossDamage = perSecond.Sum();
        var other = new Fighter { Id = 2, Name = "Aerin", Class = "Cleric", BossPerSecond = Enumerable.Repeat(otherPerSecond, n).ToList(), PerSecond = Enumerable.Repeat(otherPerSecond, n).ToList() };
        other.Damage = other.BossDamage = otherPerSecond * n;
        e.Roster.Add(you); e.Roster.Add(other);
        e.BossDamage = e.Roster.Sum(f => f.BossDamage);
        return e;
    }

    /// <summary>A copy of a kept pull as it stood <paramref name="secs"/> seconds in, still under way (its HP and damage up to then).</summary>
    static Encounter LiveCut(Encounter kept, int secs)
    {
        var e = BossParser.Copy(kept);
        e.End = null; e.Outcome = ""; e.LastAt = e.Start.AddSeconds(secs);
        e.BossHpPerSecond = kept.BossHpPerSecond!.Take(secs).ToList();
        e.BossHp = (long)Math.Round(e.BossHpPerSecond[^1] * e.BossMaxHp);
        foreach (var f in e.Roster)
        {
            f.BossPerSecond = f.BossPerSecond!.Take(secs).ToList(); f.PerSecond = f.PerSecond!.Take(secs).ToList();
            f.Damage = f.BossDamage = f.BossPerSecond.Sum();
        }
        e.BossDamage = e.Roster.Sum(f => f.BossDamage);
        return e;
    }

    public static void Run(Action<bool, string> check, DateTime t, Func<string, string?> recording, Func<string, Action<BossParser, DateTime>?, List<Encounter>> replay)
    {
        bool active0 = Encounter.ActiveBasis, boss0 = Encounter.BossOnly, shown0 = PbGhost.Shown;
        var src0 = PbGhost.Source;
        try
        {
            Encounter.ActiveBasis = false; Encounter.BossOnly = true; PbGhost.Shown = true;
            Made(check, t);
            LockOrder(check, t);
            Recorded(check, recording, replay);
        }
        finally { Encounter.ActiveBasis = active0; Encounter.BossOnly = boss0; PbGhost.Shown = shown0; PbGhost.Source = src0; PbGhost.Forget(); }
    }

    /// <summary>
    /// The ghost asked for while a fight is kept: the meter asks on the window's thread at a pull's first draw
    /// (FightRecords.GhostFor, which looks among the fights kept for an older record's best), while the session keeps the
    /// fight that just ended (EncounterStore.Add, which notes it in the records). In 1.0.40 the two took the store's lock and
    /// the records' in opposite orders, and could hang the window and the session for good.
    /// </summary>
    static void LockOrder(Action<bool, string> check, DateTime t)
    {
        var dir = TestTemp.Path("ghost-lock-" + Guid.NewGuid().ToString("N"));
        var store = new EncounterStore(() => dir);
        // an older record's best with no curve to race (kept before the fights carried their seconds): every ask looks among the fights kept
        Encounter Old(int i) { var e = Pull("old" + i, t.AddMinutes(i), new long[] { 9_000 }); foreach (var f in e.Roster) { f.PerSecond = null; f.BossPerSecond = null; } return e; }
        store.Add(Old(0));
        var now = LiveCut(Pull("now", t.AddHours(2), Enumerable.Repeat(8_000L, 30).ToArray()), 10);
        bool stop = false; int adds = 0, asks = 0;
        var keeping = new Thread(() => { for (int i = 1; !stop && i <= 3000; i++) { store.Add(Old(i)); adds++; } }) { IsBackground = true, Name = "ghost-lock keep" };
        var asking = new Thread(() => { while (!stop) { store.Records.GhostFor(now, () => store.All()); asks++; } }) { IsBackground = true, Name = "ghost-lock ask" };
        keeping.Start(); asking.Start();
        bool kept = keeping.Join(TimeSpan.FromSeconds(60));
        stop = true;
        bool asked = asking.Join(TimeSpan.FromSeconds(5));
        check(kept && asked && adds == 3000,
            $"ghost: asked for while fights are kept, nothing waits on itself ({adds:N0} of 3,000 fights kept, {asks:N0} asks{(kept && asked ? "" : ", then both threads hung")})");
    }

    static void Made(Action<bool, string> check, DateTime t)
    {
        // the best: 100 s, a slow start (5K a second for 50 s) then 15K: 1,000,000 over 100 s, 10,000 DPS
        var best = Pull("best", t, Enumerable.Range(0, 100).Select(i => i < 50 ? 5_000L : 15_000L).ToArray());
        var slow = Pull("slow", t.AddMinutes(5), Enumerable.Repeat(8_000L, 100).ToArray());
        var g = GhostCurve.Of(best)!;
        check(g != null && g.Fight == "best" && g.Boss && Math.Abs(g.Dps - 10_000) < 1e-9 && Math.Abs(g.GroupDps - 11_000) < 1e-9 && g.YouPer.Length == 100 && g.HpPer.Length == 100 && g.HpPer[^1] == 0,
            $"ghost: a kill's curve is your damage and the group's by the second, the boss's HP by the second, its finish the pull's DPS ({g?.Dps:N0}, group {g?.GroupDps:N0})");
        var (mid, byHp) = g.InstantFor(30, 0.5);
        check(byHp && Math.Abs(mid - 50) < 1e-9, $"ghost: lined up by the boss's HP: the boss at half its HP stands beside second 50 of the best, whatever the time now ({mid:0.##})");
        var (dps50, _) = g.DpsAt(50);
        check(Math.Abs(dps50 - 5_000) < 1e-9 && Math.Abs(g.DpsAt(75).You - 25_000 * 50 / 75.0 / 5 * 5 / 5 * 1) < 1e9 && Math.Abs(g.DpsAt(75).You - (250_000 + 375_000) / 75.0) < 1e-9,
            $"ghost: its DPS at a point is its damage so far over its time so far ({dps50:N0} at 50 s, {g.DpsAt(75).You:N0} at 75 s)");
        check(g.InstantFor(30, null) is (30, false) && g.InstantFor(130, null) is (100, false), "ghost: without the boss's HP, lined up by the time into the pull (past its end: its finish)");
        check(g.InstantFor(10, 0) is (100, true), "ghost: the boss dead beside the ghost's kill: its finish");

        // records: a new best sets the curve, written and read back; the fight that set it races the one before
        var map = FightRecords.From(new[] { best, slow });
        var r = map.Values.Single();
        var back = JsonSerializer.Deserialize<List<FightRecord>>(JsonSerializer.Serialize(new List<FightRecord> { r }))![0];
        check(back.Ghost is { Fight: "best" } bg && bg.YouPer.SequenceEqual(g.YouPer) && bg.HpPer.SequenceEqual(g.HpPer) && Math.Abs(bg.Dps - back.BestDps) < 1e-9,
            $"ghost: kept with the record of the fight (records3.json), read back the same, its finish the record's best DPS ({back.Ghost?.Dps:N0} = {back.BestDps:N0})");

        // the race: a pull at 8,000 a second against the best, live at 30 s (boss at 70%) and at 80 s, then killed
        string dir = Path.Combine(Path.GetTempPath(), "kl-ghost-test-" + Guid.NewGuid().ToString("N")[..8]);
        var store = new FightRecords(() => dir, _ => new[] { best });
        var now = Pull("now", t.AddMinutes(10), Enumerable.Repeat(8_000L, 100).ToArray());
        check(store.GhostFor(now, () => new[] { best }) is { Fight: "best" }, "ghost: a record with no curve (kept before 1.0.38) gets it from its best fight among the fights kept");
        check(store.GhostFor(best, () => new[] { best }) == null, "ghost: the fight that set the best has no ghost of itself");
        PbGhost.Forget();
        PbGhost.Source = e => (store.GhostFor(e, () => new[] { best }), store.Of(e)?.DpsBefore(e.Id) ?? 0);
        var at30 = LiveCut(now, 30);
        var p30 = PbGhost.Of(at30, at30.LastAt);
        // at 30 s the boss is at 70%: the best's boss was at 70% at second 30 too (both fall evenly): the best had 5,000 a second then
        check(p30 is { Final: false, ByHp: true } && Math.Abs(p30.Ghost - 5_000) < 1e-6 && Math.Abs(p30.Delta - 0.6) < 1e-9 && PbGhost.Words(p30) == "+60.0% vs best",
            $"ghost: live, 8,000 against the best's 5,000 at the same point: \"{(p30 != null ? PbGhost.Words(p30) : "none")}\"");
        var at80 = LiveCut(now, 80);
        var p80 = PbGhost.Of(at80, at80.LastAt);
        double want80 = 8_000 / ((250_000 + 30 * 15_000) / 80.0) - 1;
        check(p80 is { Ahead: false } && Math.Abs(p80.Delta - want80) < 1e-9, $"ghost: at 80 s behind the best's fast finish ({p80?.Delta:P1}, {want80:P1})");
        PbGhost.Forget();
        var killed = PbGhost.Of(now, now.End!.Value);
        check(killed is { Final: true, NewBest: false } && Math.Abs(killed.Delta - (8_000 / 10_000.0 - 1)) < 1e-9 && PbGhost.Words(killed) == "20.0% off your best",
            $"ghost: at the kill, the final figures: \"{(killed != null ? PbGhost.Words(killed) : "none")}\"");
        check(PbGhost.LiveAt(now, now.End!.Value, g) is { } lk && Math.Abs(lk.Delta - killed!.Delta) < 1e-12, "ghost: the live pace at the kill is the final DPS difference");
        var fast = Pull("fast", t.AddMinutes(20), Enumerable.Repeat(12_000L, 100).ToArray());
        PbGhost.Forget();
        var won = PbGhost.Of(fast, fast.End!.Value);
        check(won is { NewBest: true } && PbGhost.Words(won) == "New best by 20.0%!" && PbGhost.Short(won) == "PB +20.0%", $"ghost: a kill past the best: \"{(won != null ? PbGhost.Words(won) : "none")}\"");

        // when it says nothing: too early, a wipe at its end, the switch off, DPS over active time, all targets against a boss curve
        PbGhost.Forget();
        var at2 = LiveCut(now, 2);
        check(PbGhost.Of(at2, at2.LastAt) == null, "ghost: nothing in a pull's first seconds");
        var wipe = Pull("wipe", t.AddMinutes(30), Enumerable.Repeat(8_000L, 60).ToArray(), outcome: "wipe");
        check(PbGhost.Of(wipe, wipe.End!.Value) == null, "ghost: a wipe says nothing at its end");
        PbGhost.Shown = false; PbGhost.Forget();
        check(PbGhost.Of(at30, at30.LastAt) == null, "ghost: nothing with Settings › PB ghost off");
        PbGhost.Shown = true; Encounter.ActiveBasis = true; PbGhost.Forget();
        check(PbGhost.Of(at30, at30.LastAt) == null, "ghost: nothing with DPS over active time (the ghost's seconds are the fight's)");
        Encounter.ActiveBasis = false; Encounter.BossOnly = false; PbGhost.Forget();
        check(PbGhost.Of(at30, at30.LastAt) == null, "ghost: nothing while the meter counts every target and the best was kept as boss damage");
        Encounter.BossOnly = true; PbGhost.Forget();
        check(PbGhost.Pct(-0.031) == "−3.1%" && PbGhost.Pct(0.062) == "+6.2%", "ghost: its figures with a true minus");
        try { if (Directory.Exists(dir)) Directory.Delete(dir, true); } catch (IOException) { }
    }

    /// <summary>
    /// Steven's recordings with the same fight killed more than once (Ferocious Horn Den three times in rec-20261116-14, Deus
    /// Research Base twice in rec-20261117-17, and the others where a fight repeats): each kill against the best kill of its fight
    /// before it, the curve as the records keep it (written and read back).
    /// </summary>
    static void Recorded(Action<bool, string> check, Func<string, string?> recording, Func<string, Action<BossParser, DateTime>?, List<Encounter>> replay)
    {
        var names = new[] { "20261116-14", "20261116-15-2", "20261116-17", "20261117-17", "20261117-18", "20261118-12", "20261118-13" };
        var recs = names.Select(n => (Name: n, Path: recording(n))).Where(r => r.Path != null).ToList();
        if (NativeDecoder.Version < 10 || recs.Count == 0)
        {
            Console.WriteLine($"skip  the PB ghost on Steven's recordings: decoder interface {NativeDecoder.Version}, {recs.Count} of {names.Length} recordings here (kinah-bench/packets/rec-*)");
            return;
        }
        int raced = 0, liveSeen = 0, byHp = 0;
        var bad = new List<string>();
        var said = new List<string>();
        double worstLast = 0;
        foreach (var (name, path) in recs)
        {
            // the meter's live pulls, a copy each second of capture time (and the last before each pull closed)
            var lastLive = new Dictionary<string, Encounter>();
            DateTime sampled = DateTime.MinValue;
            Encounter? prev = null;
            var kept = replay(path!, (p, at) =>
            {
                if ((at - sampled).TotalSeconds < 1) return;
                sampled = at;
                var live = p.LiveAt(at);
                if (live is { Live: true }) { lastLive[live.Id] = live; prev = live; }
            });
            var kills = kept.Where(FightRecords.Counts).OrderBy(e => e.Start).ToList();
            foreach (var k in kills)
            {
                var before = kills.Where(x => x.Start < k.Start && FightRecords.KeyOf(x) == FightRecords.KeyOf(k)).ToList();
                if (before.Count == 0) continue;
                var rec = FightRecords.From(before)[FightRecords.KeyOf(k)];
                var r = JsonSerializer.Deserialize<List<FightRecord>>(JsonSerializer.Serialize(new List<FightRecord> { rec }))![0];
                if (r.BestDps <= 0) continue;   // kills with none of your damage on the boss (Red Spark Ignus, rec-20261118-13): no best to race, as no PB chip
                if (r.Ghost is not { } g)
                {
                    var b = before.FirstOrDefault(x => x.Id == r.BestDpsFight);
                    bad.Add($"{name} {k.Boss}: the record kept no curve (best {r.BestDpsFight}, found {b != null}: split {b?.TargetsSplit}, boss view {b?.BossView}, your seconds {b?.You?.PerSecond?.Count}/{b?.You?.BossPerSecond?.Count}, figure {(b?.You is { } by ? b.Figure(by) : -1)})");
                    continue;
                }
                raced++;
                if (Math.Abs(g.Dps - r.BestDps) > 1e-6 * Math.Max(1, r.BestDps)) bad.Add($"{name} {k.Boss}: the curve's finish {g.Dps:N2} isn't the record's best {r.BestDps:N2}");
                double final = k.Dps(k.You!, k.End!.Value) / r.DpsBefore(k.Id) - 1;
                var atKill = PbGhost.LiveAt(k, k.End!.Value, g);
                if (atKill == null || Math.Abs(atKill.Delta - final) > 1e-9) bad.Add($"{name} {k.Boss}: the live pace at the kill {atKill?.Delta:P3} isn't the final DPS difference {final:P3}");
                if (atKill?.ByHp == true) byHp++;
                // the meter's own last live copy of this pull (up to a second before the kill)
                if (lastLive.TryGetValue(k.Id, out var lv) && PbGhost.LiveAt(lv, lv.LastAt, g) is { } lp)
                {
                    liveSeen++;
                    double gap = Math.Abs(lp.Delta - final);
                    worstLast = Math.Max(worstLast, gap);
                }
                said.Add($"{k.Boss} {PbGhost.Pct(final)}");
            }
        }
        if (raced == 0) { Console.WriteLine($"skip  the PB ghost on Steven's recordings: no fight killed twice in {string.Join(", ", recs.Select(r => r.Name))}"); return; }
        // (the pace a second before the kill is said, not judged: a short pull's last second moves it a lot, 34% on a 20 s Duanka)
        check(bad.Count == 0 && liveSeen * 10 >= raced * 8,
            $"ghost on Steven's recordings: {raced} kills raced against the best before them ({byHp} lined up by the boss's HP; {string.Join(", ", said.Take(8))}); at each kill the live pace is the final DPS difference exactly; the meter showed the pace live on {liveSeen}, a second before the kill at most {worstLast:P1} from the final{(bad.Count > 0 ? ": " + string.Join("; ", bad.Take(6)) : "")}");
    }
}
