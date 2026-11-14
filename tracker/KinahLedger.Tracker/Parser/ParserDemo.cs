using KinahLedger.Packets;

namespace KinahLedger.Parser;

/// <summary>
/// The sample the Parser page and its overlay are drawn from in a layout render (ui-shot --parser) and the options test:
/// made-up fight events run through the real rules (BossParser), so the sample shows what the rules would keep.
/// Nothing is saved.
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

    /// <summary>The past fights (oldest first) and, when <paramref name="live"/>, a fight under way, as of <paramref name="now"/>.</summary>
    /// <param name="damage">The game gave damage: DPS rows show. Off, the page shows the group without damage, as when the decoder gives none.</param>
    public static (List<Encounter> Past, Encounter? Live) Sample(DateTime now, bool live = true, bool damage = true)
    {
        var past = new List<Encounter>();
        var p = new BossParser(BossBook.Shared) { Ended = past.Add };
        var rnd = new Random(7);
        var t = now.AddHours(-3);

        void Fight(string boss, long id, int secs, BossOutcome outcome, bool gap = false, int adds = 0, bool deaths = false)
        {
            p.Take(new BossEngaged(t, boss, id));
            for (int s = 3; s < secs; s += 3)
            {
                if (damage) foreach (var m in Group) p.Take(new DamageDealt(t.AddSeconds(s), m.Id, id, (long)(rnd.Next(9_000, 15_000) * (m.Class == "Cleric" ? 0.45 : m.Class == "Templar" ? 0.7 : 1))));
                if (adds > 0 && s % 30 == 0) { p.Take(new UnitDied(t.AddSeconds(s), 5000 + s, "Void Fragment", false)); adds--; }
                if (gap && s == 60) p.Take(new FeedGap(t.AddSeconds(s)));
                if (deaths && s == 81) p.Take(new UnitDied(t.AddSeconds(s), 3, "Thornecrest", false));
            }
            if (outcome == BossOutcome.Kill) p.Take(new UnitDied(t.AddSeconds(secs), id, boss, true));
            else p.Take(new BossEnded(t.AddSeconds(secs), id, outcome));
            t = t.AddSeconds(secs + 140);
        }

        p.Take(new InstanceEntered(t, ContentKind.Expedition, "Fire Temple", 11));
        p.Take(new GroupChanged(t, Group));
        Fight("Silver Blade Rotan", 101, 96, BossOutcome.Kill);
        Fight("Red Spark Ignus", 102, 131, BossOutcome.Kill, adds: 3);
        Fight("Black Smoke Murute", 103, 88, BossOutcome.Wipe);
        Fight("Black Smoke Murute", 104, 154, BossOutcome.Kill, gap: true);
        Fight("Kromede's Desire", 105, 212, BossOutcome.Kill, adds: 4);
        t = t.AddMinutes(20);
        p.Take(new InstanceEntered(t, ContentKind.Sanctuary, "Chalice of Muspel", 12));
        Fight("Iscariel", 201, 245, BossOutcome.Kill);
        Fight("Kaldrix", 202, 301, BossOutcome.Wipe);
        t = now.AddMinutes(-12);
        p.Take(new InstanceEntered(t, ContentKind.Transcendence, "Shattered Arkanis [Stage 3]", 13));
        Fight("Talisra of the Void", 301, 118, BossOutcome.Kill, adds: 2, deaths: true);
        Fight("Siliator of Deceit", 302, 142, BossOutcome.Kill);
        if (live)
        {
            t = now.AddSeconds(-163);
            p.Take(new BossEngaged(t, "Ruthilis of Pain", 303));
            for (int s = 3; s <= 162; s += 3)
            {
                if (damage) foreach (var m in Group) p.Take(new DamageDealt(t.AddSeconds(s), m.Id, 303, (long)(rnd.Next(9_000, 15_000) * (m.Class == "Cleric" ? 0.45 : m.Class == "Templar" ? 0.7 : 1))));
                if (s % 45 == 0) p.Take(new UnitDied(t.AddSeconds(s), 6000 + s, "Pain Shard", false));
            }
        }
        return (past, p.Live);
    }
}
