using KinahLedger.Packets;

namespace KinahLedger.Parser;

/// <summary>
/// The sample the Parser page and its overlay are drawn from in a layout render (ui-shot --parser) and the options test:
/// made-up fight events run through the real rules (BossParser), the way the decoder sends them (the boss's HP after
/// each fall, the damage that made it, a small Other share), so the sample shows what the rules would keep. Nothing is saved.
/// </summary>
public static class ParserDemo
{
    static readonly GroupMember[] Group =
    {
        new(1, "Swoly", "Gladiator", 4_812, 5_640, true),
        new(2, "Aerinwyn", "Cleric", 4_655, 5_610, false),
        new(3, "Thornecrest", "Templar", 4_590, 5_585, false),
        new(4, "Kaelith", "Sorcerer", 4_730, 5_620, false),
    };

    static double Weight(string cls) => cls == "Cleric" ? 0.45 : cls == "Templar" ? 0.7 : 1;

    /// <summary>The past fights (oldest first) and, when <paramref name="live"/>, a fight under way, as of <paramref name="now"/>.</summary>
    /// <param name="damage">The game gave damage: DPS rows and the boss's HP show. Off, the page shows the group without them.</param>
    /// <param name="group">The group list came (Combat Power, item level); off, the damage rows are the group, as before the decoder maps it.</param>
    public static (List<Encounter> Past, Encounter? Live) Sample(DateTime now, bool live = true, bool damage = true, bool group = true)
    {
        var past = new List<Encounter>();
        var p = new BossParser(BossBook.Shared) { Ended = past.Add };
        var rnd = new Random(7);
        var t = now.AddHours(-3);

        // one pull: hits every 3 s from each member (and now and then Other), the boss's HP after each, then its end
        void Pull(string boss, long id, int secs, BossOutcome? outcome, bool gap = false, int adds = 0, bool deaths = false)
        {
            var ticks = new List<(int S, long Src, long Amount)>();
            for (int s = 3; s < secs || (outcome == null && s <= secs); s += 3)
            {
                foreach (var m in Group) ticks.Add((s, m.Id, (long)(rnd.Next(9_000, 15_000) * Weight(m.Class))));
                if (s % 12 == 0) ticks.Add((s, 0, rnd.Next(800, 1_600)));
            }
            long total = ticks.Sum(x => x.Amount);
            long max = outcome == BossOutcome.Kill ? total : outcome == null ? (long)(total * 1.9) : (long)(total * 1.45);
            long hp = max;
            p.Take(new BossEngaged(t, boss, id));
            if (damage) p.Take(new BossHealth(t, id, hp, max));
            foreach (var (s, src, amount) in ticks)
            {
                var at = t.AddSeconds(s);
                if (damage)
                {
                    var who = Group.FirstOrDefault(g => g.Id == src);
                    p.Take(new DamageDealt(at, src, id, amount, group ? "" : who?.Name ?? "", who?.You == true));
                    hp -= amount;
                    p.Take(new BossHealth(at, id, Math.Max(0, hp), max));
                }
                if (src == 1)
                {
                    if (adds > 0 && s % 30 == 0) { p.Take(new UnitDied(at, 5000 + s, "Void Fragment", false)); adds--; }
                    if (gap && s == 60) p.Take(new FeedGap(at));
                    if (deaths && s == 81) p.Take(new UnitDied(at, 3, "Thornecrest", false));
                }
            }
            if (outcome == BossOutcome.Kill) p.Take(new UnitDied(t.AddSeconds(secs), id, boss, true));
            else if (outcome is { } o) p.Take(new BossEnded(t.AddSeconds(secs), id, o));
            t = t.AddSeconds(secs + 140);
        }

        p.Take(new InstanceEntered(t, ContentKind.Expedition, "Fire Temple", 11));
        if (group) p.Take(new GroupChanged(t, Group));
        Pull("Silver Blade Rotan", 101, 96, BossOutcome.Kill);
        Pull("Red Spark Ignus", 102, 131, BossOutcome.Kill, adds: 3);
        Pull("Black Smoke Murute", 103, 88, BossOutcome.Wipe);
        Pull("Black Smoke Murute", 104, 154, BossOutcome.Kill, gap: true);
        Pull("Kromede's Desire", 105, 212, BossOutcome.Kill, adds: 4);
        t = t.AddMinutes(20);
        p.Take(new InstanceEntered(t, ContentKind.Sanctuary, "Chalice of Muspel", 12));
        Pull("Iscariel", 201, 245, BossOutcome.Kill);
        Pull("Kaldrix", 202, 301, BossOutcome.Wipe);
        t = now.AddMinutes(-12);
        p.Take(new InstanceEntered(t, ContentKind.Transcendence, "Shattered Arkanis [Stage 3]", 13));
        Pull("Talisra of the Void", 301, 118, BossOutcome.Kill, adds: 2, deaths: true);
        Pull("Siliator of Deceit", 302, 142, BossOutcome.Kill);
        if (live)
        {
            t = now.AddSeconds(-163);
            Pull("Ruthilis of Pain", 303, 162, null, adds: 3);
        }
        return (past, p.Live);
    }
}
