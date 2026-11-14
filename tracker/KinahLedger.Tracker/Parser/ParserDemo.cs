using KinahLedger.Packets;

namespace KinahLedger.Parser;

/// <summary>
/// The sample the Parser page and its overlay are drawn from in a layout render (ui-shot --parser) and the options test:
/// made-up events run through the real rules (BossParser) in the mode asked for, the way the decoder sends them (the
/// boss's HP after each fall, the damage that made it, a small Other share, the game's kill events), so the sample shows
/// what the rules would keep. Nothing is saved.
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

    /// <summary>The kept records (oldest first) and, when <paramref name="live"/>, the record under way, as of <paramref name="now"/>.</summary>
    /// <param name="damage">The game gave damage: DPS rows and the boss's HP show. Off, the page shows the group without them.</param>
    /// <param name="group">The group list came (Combat Power, item level); off, the damage rows are the group, as before the decoder maps it.</param>
    public static (List<Encounter> Past, Encounter? Live) Sample(DateTime now, bool live = true, bool damage = true, bool group = true, ParserMode mode = ParserMode.Bosses)
    {
        var past = new List<Encounter>();
        var p = new BossParser(BossBook.Shared) { Ended = past.Add };
        var rnd = new Random(7);
        var t = now.AddHours(-3);
        p.SetMode(mode, t);

        void Trash(int n)
        {
            for (int i = 0; i < n; i++) { t = t.AddSeconds(6); p.Kill(t, player: false, death: false, ""); }
            t = t.AddSeconds(30);
        }

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
                    if (adds > 0 && s % 30 == 0) { p.Take(new UnitDied(at, 5000 + s, "Void Fragment", false)); p.Kill(at, player: false, death: false, ""); adds--; }
                    if (gap && s == 60) p.Take(new FeedGap(at));
                    if (deaths && s == 81) { p.Take(new UnitDied(at, 3, "Thornecrest", false)); }
                }
            }
            if (outcome == null) return;
            if (outcome == BossOutcome.Kill) p.Take(new UnitDied(t.AddSeconds(secs), id, boss, true));
            else p.Take(new BossEnded(t.AddSeconds(secs), id, outcome.Value));
            t = t.AddSeconds(secs + 40);
            p.Tick(t);
        }

        // the open world and the Abyss first (only Every fight keeps anything from them)
        p.Place("Western Cantas Valley", abyss: false);
        Trash(3); p.Tick(t.AddMinutes(1)); t = t.AddMinutes(4);
        p.Place("Abyss", abyss: true);
        p.Kill(t, player: true, death: false, "Rhavos"); t = t.AddSeconds(9);
        p.Kill(t, player: false, death: false, ""); t = t.AddSeconds(14);
        p.Kill(t, player: true, death: false, "Velkaria"); p.Tick(t.AddMinutes(1)); t = t.AddMinutes(6);
        p.Kill(t, player: false, death: false, ""); t = t.AddSeconds(11);
        p.Kill(t, player: true, death: true, "Ostrend"); p.Tick(t.AddMinutes(1)); t = t.AddMinutes(10);

        p.Take(new InstanceEntered(t, ContentKind.Expedition, "Fire Temple", 11));
        if (group) p.Take(new GroupChanged(t, Group));
        Trash(4);
        Pull("Silver Blade Rotan", 101, 96, BossOutcome.Kill);
        Trash(5);
        Pull("Red Spark Ignus", 102, 131, BossOutcome.Kill, adds: 3);
        Trash(3);
        Pull("Black Smoke Murute", 103, 88, BossOutcome.Wipe);
        Pull("Black Smoke Murute", 104, 154, BossOutcome.Kill, gap: true);
        Trash(4);
        Pull("Kromede's Desire", 105, 212, BossOutcome.Kill, adds: 4);
        p.Take(new InstanceLeft(t)); p.Tick(t.AddMinutes(1));
        t = t.AddMinutes(20);
        p.Take(new InstanceEntered(t, ContentKind.Sanctuary, "Chalice of Muspel", 12));
        if (group) p.Take(new GroupChanged(t, Group));
        Trash(2);
        Pull("Iscariel", 201, 245, BossOutcome.Kill);
        Trash(3);
        Pull("Kaldrix", 202, 301, BossOutcome.Wipe);
        p.Take(new InstanceLeft(t)); p.Tick(t.AddMinutes(1));
        t = now.AddMinutes(-14);
        p.Take(new InstanceEntered(t, ContentKind.Transcendence, "Shattered Arkanis [Stage 3]", 13));
        if (group) p.Take(new GroupChanged(t, Group));
        Trash(3);
        Pull("Talisra of the Void", 301, 118, BossOutcome.Kill, adds: 2, deaths: true);
        Trash(2);
        Pull("Siliator of Deceit", 302, 142, BossOutcome.Kill);
        if (live)
        {
            p.Tick(now.AddSeconds(-163));
            t = now.AddSeconds(-163);
            Pull("Ruthilis of Pain", 303, 162, null, adds: 3);
        }
        else { p.Take(new InstanceLeft(t)); p.Tick(t.AddMinutes(1)); }
        return (past, p.LiveAt(now));
    }
}
