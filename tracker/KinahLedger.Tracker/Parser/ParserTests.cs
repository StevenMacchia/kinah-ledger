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
        Ferocious Horn Den|Expedition|Watchdog Kwapo|
        Ferocious Horn Den|Expedition|Totem room|wave
        Ferocious Horn Den|Expedition|Necromancer Duanka|
        Ferocious Horn Den|Expedition|Ferocious Horn Nuakum|final
        """;

    static readonly GroupMember Me = new(1, "Swoly", "Gladiator", 312_400, 5_620, true);
    static readonly GroupMember Ally = new(2, "Aerin", "Cleric", 298_100, 5_580, false);
    static readonly GroupMember Ally2 = new(3, "Thorne", "Templar", 305_000, 5_600, false);

    /// <summary>
    /// parser-replay &lt;capture.pcapng&gt; [--mode boss|run|fight]: a recording through this build's decoder and the Parser as
    /// fast as it goes, each piece stamped with its own capture time: every record kept, with each member's share of the
    /// damage (as the tab shows it) and their top abilities. Needs a decoder of interface version 4 or later beside the exe.
    /// </summary>
    public static int ParserReplay(string pcapng, string mode, int port = 13328)
    {
        if (NativeDecoder.Version < 4) { Console.WriteLine($"this build's decoder reads no fights for the Parser (interface version {NativeDecoder.Version})"); return 1; }
        var kept = new List<Encounter>();
        var p = new BossParser { Ended = kept.Add };
        p.SetMode(BossParser.ModeOf(mode), DateTime.MinValue.AddYears(2000));
        var segs = new List<(DateTime At, TcpSegment S)>();
        foreach (var (at, frame, link) in Pcapng.Read(pcapng))
            if (NpcapSource.IpOf(frame, link) is { } ip && TcpSegment.Parse(ip) is { } s && s.SrcPort == port) segs.Add((at, s));
        DateTime last = DateTime.MinValue;
        // the open world's farming as the Kinah line would show it, sampled each second of the recording (never kept: only printed here)
        var farms = new List<(DateTime Start, DateTime Last, long Yours, double Fighting, List<double> Shown)>();
        Encounter? farmNow = null; var shown = new List<double>(); DateTime sampled = DateTime.MinValue;
        void Sample(DateTime at)
        {
            if ((at - sampled).TotalSeconds < 1) return;
            sampled = at;
            var f = p.Farm;
            if (f != null && p.FarmDpsNow(at) is { } now0 && now0 > 0) shown.Add(now0);   // what the Kinah line reads now (the live readout)
            if (farmNow != null && (f == null || f.Start != farmNow.Start))
            {
                farms.Add((farmNow.Start, farmNow.LastAt, farmNow.You?.Damage ?? 0, farmNow.You?.FightingSeconds ?? 0, shown));
                shown = new();
            }
            farmNow = f;
        }
        foreach (var conn in segs.GroupBy(x => (x.S.Src.ToString(), x.S.DstPort)).OrderBy(g => g.First().At))
        {
            using var dec = NativeDecoder.TryOpen()!;
            var st = new TcpStream
            {
                OnData = (d, at) =>
                {
                    last = at;
                    dec.Feed(d, at);
                    while (dec.Next() is not null) { }
                    while (dec.NextItem() is not null) { }
                    while (dec.NextFight() is { } f) TrackerSession.RawFight(p, f);
                    while (dec.NextBossEvent() is { } b) p.Take(b);
                },
                OnGap = () => { dec.Gap(); p.Take(new FeedGap(last)); },
            };
            foreach (var (at, s) in conn) { st.Add(s.Seq, s.Payload, at, s.Syn); p.Tick(at); Sample(at); }
        }
        p.Tick(DateTime.MaxValue.AddDays(-1));
        Sample(DateTime.MaxValue.AddDays(-1));
        foreach (var f in farms)
        {
            var sorted = f.Shown.OrderBy(x => x).ToList();
            string Q(double q) => sorted.Count == 0 ? "-" : Ui.Amounts.Short(sorted[(int)Math.Min(sorted.Count - 1, Math.Floor(q * sorted.Count))]);
            Console.WriteLine($"farming {f.Start.ToLocalTime():HH:mm:ss}-{f.Last.ToLocalTime():HH:mm:ss}: your damage {Ui.Amounts.Short(f.Yours)} in {f.Fighting:0} s fighting = {(f.Fighting > 0 ? Ui.Amounts.Short(f.Yours / Math.Max(1, f.Fighting)) : "-")}/s; the Kinah line read {Q(0.1)} / {Q(0.5)} / {Q(0.9)} (10th / median / 90th, {sorted.Count} s)");
        }
        foreach (var e in kept)
        {
            var end = e.End ?? e.LastAt;
            Console.WriteLine($"{e.Title} · {e.Instance} · {e.Kind} · {e.OutcomeLabel} in {BossParser.Clock(e.Duration(end))}{(e.BossMaxHp > 0 ? $" · {Ui.Amounts.Short(e.BossMaxHp)} HP" : "")}{(e.Wave ? " · wave" : "")}{(e.Partial ? " · partial: " + string.Join("; ", e.PartialWhy) : "")}");
            foreach (var x in e.Splits) Console.WriteLine($"   split {x.Boss}{(x.Wave ? " (wave)" : "")}: {x.Outcome} in {BossParser.Clock(TimeSpan.FromSeconds(x.Seconds))}");
            long total = Math.Max(1, e.Roster.Sum(f => f.Damage));
            foreach (var f in e.Ranked())
                Console.WriteLine($"   {(f.Other ? "Other" : f.Name.Length > 0 ? f.Name : "Unnamed"),-16} {(f.Class.Length > 0 ? f.Class : "-"),-13} {f.Damage * 100.0 / total,5:0.0}%  {Ui.Amounts.Short(f.Damage),8}  {(e.Shown().Contains(f) ? "" : "(no row: no damage)")}"
                    + (f.Abilities.Count > 0 ? "  top: " + string.Join(", ", f.Abilities.OrderByDescending(a => a.Damage).Take(3).Select(a => $"{Abilities.Name(a.Skill)} {Ui.Amounts.Short(a.Damage)} in {a.Hits}")) : ""));
        }
        Console.WriteLine($"{kept.Count} records kept ({BossParser.Name(p.Mode)})");
        return 0;
    }

    /// <summary>party-scan: the party list read from each picture in a folder (5120 × 2160 reward pictures and the like); classes counted.</summary>
    public static int PartyScan(string dir, bool rows, bool names = false)
    {
        var counts = new SortedDictionary<string, int>(); int frames = 0, withParty = 0, members = 0, unnamed = 0;
        foreach (var f in Directory.EnumerateFiles(dir).Where(f => f.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".png", StringComparison.OrdinalIgnoreCase)).OrderBy(f => f))
        {
            using var full = new System.Drawing.Bitmap(f);
            frames++;
            var region = Ocr.PartyFrame.Region(new System.Drawing.Rectangle(0, 0, full.Width, full.Height));
            using var crop = full.Clone(region, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            var list = Ocr.PartyFrame.Read(crop, full.Height);
            if (list.Count == 0) continue;
            withParty++;
            foreach (var m in list) { members++; if (m.Class.Length == 0) unnamed++; else counts[m.Class] = counts.GetValueOrDefault(m.Class) + 1; }
            var read = new List<string>();
            if (names)
                foreach (var m in list)
                {
                    var box = System.Drawing.Rectangle.Intersect(m.NameBox, new System.Drawing.Rectangle(0, 0, crop.Width, crop.Height));
                    using var nb = crop.Clone(box, crop.PixelFormat);
                    read.Add(Ocr.PartyFrame.CleanName(Ocr.ScreenReader.ReadBitmapAsync(nb, boost: false, scale: 2).GetAwaiter().GetResult().FirstOrDefault() ?? ""));
                }
            if (rows) Console.WriteLine($"{Path.GetFileName(f)}: {string.Join(", ", list.Select((m, i) => $"{(names ? (read[i].Length > 0 ? read[i] : "?") + " " : "")}{(m.Class.Length > 0 ? m.Class : "?")}{(m.Online ? "" : " (away)")} {m.Score:0.00}"))}");
        }
        Console.WriteLine($"{frames} pictures, {withParty} with a party list, {members} members: {string.Join(", ", counts.Select(kv => $"{kv.Key} {kv.Value}"))}, unnamed {unnamed}");
        return 0;
    }

    /// <summary>The party list's test icons: tracker/tests/party, looked for upward from the exe.</summary>
    static string? PartyFixtures()
    {
        for (var d = new DirectoryInfo(AppContext.BaseDirectory); d != null; d = d.Parent)
        {
            var p = Path.Combine(d.FullName, "tracker", "tests", "party");
            if (Directory.Exists(p)) return p;
            p = Path.Combine(d.FullName, "tests", "party");
            if (Directory.Exists(p)) return p;
        }
        return null;
    }

    /// <summary>An icon as the party list draws an away member's: grey.</summary>
    static System.Drawing.Bitmap Grey(System.Drawing.Bitmap src)
    {
        var b = new System.Drawing.Bitmap(src.Width, src.Height);
        for (int y = 0; y < src.Height; y++)
            for (int x = 0; x < src.Width; x++)
            {
                var c = src.GetPixel(x, y);
                int l = (int)(0.299 * c.R + 0.587 * c.G + 0.114 * c.B);
                l = (int)(l * 0.8);
                b.SetPixel(x, y, System.Drawing.Color.FromArgb(c.A, l, l, l));
            }
        return b;
    }

    public static int Run(Settings settings)
    {
        _fails = 0;
        var book = BossBook.Parse(Book);
        var t = new DateTime(2026, 11, 13, 21, 0, 0, DateTimeKind.Utc);

        (BossParser P, List<Encounter> Kept) New() { var kept = new List<Encounter>(); return (new BossParser(book) { Ended = kept.Add }, kept); }

        // the boss list: kinds, stages, finals
        Check(book.Count == 9 && book.KindOf("Chalice of Muspel") == ContentKind.Sanctuary && book.KindOf("Snowfield of Sorrow") == ContentKind.Sanctuary && !book.Knows("Snowfield of Sorrow"),
            $"the boss list reads 9 entries, the Sanctuaries' kind, an instance with no bosses yet: {book.Count}");
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
            Check(p.Live is { Partial: true } l && l.PartialWhy.Single() == "2 moments the tracker missed" && l.Kind == "Transcendence",
                $"lost traffic marks the fight partial, said once: {string.Join("; ", p.Live?.PartialWhy ?? new())}");
            p.Take(new InstanceEntered(t.AddSeconds(120), ContentKind.Transcendence, "Shattered Arkanis [Stage 2]", 78));   // reconnected, same copy
            p.Take(new GroupChanged(t.AddSeconds(121), new[] { Me, Ally, Ally2 }));
            Check(kept.Count == 0 && p.Live is { BossId: 910 } l2 && l2.PartialWhy.Contains("the game reconnected during it") && l2.Roster.Single(f => f.Name == "Thorne").Late,
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
            Check(!l.Ranked().Any(f => f.Other) && !l.Shown().Any(f => f.Other) && l.Roster.Sum(f => f.Damage) == 60_000 && l.BossDamage == 60_000 && l.BossHpLeft == 0.4,
                $"...no Other row shown (its {l.Roster.Single(f => f.Other).Damage:N0} kept in the record), the boss at {l.BossHpLeft:P0}");
            Check(Math.Abs(l.Members.Sum(f => l.Share(f)) - 1) < 1e-9 && l.Share(l.Roster.Single(f => f.Other)) == 0 && Math.Abs(l.Share(l.You!) - 30.0 / 59) < 1e-9,
                $"...the shares are of the members' damage and add up to 100% ({string.Join(", ", l.Members.Select(f => Encounter.Pct(l.Share(f))))})");
            var ready = Encounter.Ready(ContentKind.Expedition, "Vakron Sky Island", "", t);
            Check(ready is { Waiting: true, Live: false, Title: "Vakron Sky Island", Kind: "Expedition" } && ready.Duration(t.AddMinutes(3)) == TimeSpan.Zero && !ready.Shown().Any(),
                "a dungeon's ready card (the overlay before its first pull): its name, no clock, no rows");
            p.Take(new GroupChanged(t.AddSeconds(9), new[] { new GroupMember(11, "Swoly", "Gladiator", 4_800, 5_640, true), new GroupMember(12, "Aerin", "Cleric", 4_600, 5_600, false) }));
            l = p.Live!;
            var me = l.Roster.Single(f => f.Id == 11);
            Check(me is { CombatPower: 4_800, ItemLevel: 5_640, Class: "Gladiator", Damage: 30_000, Late: false, FromDamage: false } && !l.Roster.Single(f => f.Id == 13).Gone,
                "...and the group list fills in class, CP and item level on the same rows, nobody marked late or gone for it");
            p.Take(new BossEngaged(t.AddSeconds(20), "Ruthilis the Unlisted", 951));
            Check(kept.Count == 1 && p.Live?.Boss == "Ruthilis the Unlisted", "a pull the boss list doesn't name is still a fight under the game's name (a spelling must never lose one)");
            p.Take(new BossEngaged(t.AddSeconds(40), "", 952));
            Check(p.Live?.Boss == "Talisra of the Void", $"...and one with no name at all takes the instance's next boss on the list not yet down: {p.Live?.Boss}");
            p.Take(new BossEnded(t.AddSeconds(60), 952, BossOutcome.Wipe));
            p.Take(new BossEngaged(t.AddSeconds(90), "", 952));
            Check(p.Live?.Boss == "Talisra of the Void", "...the same spawn keeps that name when it is pulled again after a wipe");
            p.Take(new BossEnded(t.AddSeconds(150), 952, BossOutcome.Kill));
            p.Take(new BossEngaged(t.AddSeconds(200), "", 953));
            Check(p.Live?.Boss == "Siliator of Deceit", $"...and the next unnamed one the boss after it: {p.Live?.Boss}");
            var (u, _) = New();
            u.Take(new InstanceEntered(t, ContentKind.Other, "", 0));
            u.Take(new BossEngaged(t.AddSeconds(5), "", 960));
            Check(u.Live == null, "an instance the game's messages can't place keeps nothing until the screen names it...");
            u.ScreenPlace("Expedition", "Shattered Arkanis");
            u.Take(new BossEngaged(t.AddSeconds(10), "", 961));
            Check(u.Live is { Boss: "Talisra of the Void", Kind: "Expedition" }, "...then the screen's place makes it one of the three, and its bosses count");
        }

        // Full run: one record from entering to leaving, trash included, each boss a split, cleared by the final boss
        {
            var (p, kept) = New();
            p.SetMode(ParserMode.Run, t);
            p.Take(new InstanceEntered(t, ContentKind.Expedition, "Shattered Arkanis", 85));
            p.Take(new GroupChanged(t.AddSeconds(1), new[] { Me, Ally }));
            p.Kill(t.AddSeconds(30), player: false, death: false, "");
            p.Kill(t.AddSeconds(35), player: false, death: false, "");
            p.Take(new BossEngaged(t.AddSeconds(60), "Talisra of the Void", 960));
            p.Take(new DamageDealt(t.AddSeconds(70), 1, 960, 40_000));
            p.Take(new BossEnded(t.AddSeconds(120), 960, BossOutcome.Kill));
            p.Take(new BossEngaged(t.AddSeconds(200), "Siliator of Deceit", 961));
            Check(p.Live is { ModeKind: ParserMode.Run, Title: "Shattered Arkanis", Boss: "Siliator of Deceit" } lr && lr.Splits.Count == 2 && lr.Splits[1].Outcome == "",
                $"Full run: the run under way, its last split the pull under way: {string.Join(", ", p.Live?.Splits.Select(x => x.Boss + " " + x.Outcome) ?? Array.Empty<string>())}");
            p.Take(new UnitDied(t.AddSeconds(230), 1, "Swoly", false));
            p.Kill(t.AddSeconds(230), player: false, death: true, "");
            p.Take(new BossEnded(t.AddSeconds(260), 961, BossOutcome.Wipe));
            p.Take(new BossEngaged(t.AddSeconds(400), "Siliator of Deceit", 962));
            p.Take(new BossEnded(t.AddSeconds(470), 962, BossOutcome.Kill));
            p.Take(new BossEngaged(t.AddSeconds(600), "Ruthilis of Pain", 963));
            p.Take(new FeedGap(t.AddSeconds(650)));
            p.Take(new BossEnded(t.AddSeconds(700), 963, BossOutcome.Kill));
            Check(kept.Count == 0, "...no boss pull kept on its own in Full run");
            p.Take(new InstanceLeft(t.AddSeconds(760)));
            var run = kept.SingleOrDefault();
            Check(run is { ModeKind: ParserMode.Run, Outcome: "cleared", Partial: true } && run.Duration(t).TotalSeconds == 760 && run.Splits.Count == 4,
                $"...and ends as one run on leaving: {run?.OutcomeLabel}, {run?.Splits.Count} splits, {run?.Duration(t).TotalSeconds} s, partial ({string.Join("; ", run?.PartialWhy ?? new())})");
            Check(run!.Splits.Select(x => x.Outcome).SequenceEqual(new[] { "kill", "wipe", "kill", "kill" }) && run.Splits[3].Final && run.Splits[0].Seconds == 60,
                "...its splits in order with their outcomes and times, the last the final boss");
            Check(run.YourKills == 2 && run.YourDeaths == 1 && run.Roster.First(f => f.You).Deaths == 1 && run.Roster.First(f => f.You).Damage == 40_000 && run.Roster.Count == 2,
                $"...with the run's totals: {run.YourKills} kills, {run.YourDeaths} death, the group and its damage");
            p.Take(new InstanceEntered(t.AddSeconds(800), ContentKind.Expedition, "Shattered Arkanis", 86));
            p.Take(new InstanceLeft(t.AddSeconds(810)));
            Check(kept.Count == 1, $"a run of under {BossParser.RunMinSeconds:0} s with no boss isn't kept");
            p.Take(new InstanceEntered(t.AddSeconds(900), ContentKind.Expedition, "Fire Temple", 87));
            p.Take(new BossEngaged(t.AddSeconds(960), "Silver Blade Rotan", 964));
            p.Take(new BossEnded(t.AddSeconds(1000), 964, BossOutcome.Kill));
            p.Take(new InstanceEntered(t.AddSeconds(1100), ContentKind.Expedition, "Fire Temple", 87));   // a loading screen, same copy
            p.Take(new InstanceLeft(t.AddSeconds(1200)));
            Check(kept.Count == 2 && kept[1] is { Outcome: "left", Instance: "Fire Temple" } && kept[1].Splits.Count == 1,
                "...a loading screen in the same copy doesn't end it; leaving before the final boss is left");
        }

        // Every fight: anywhere, the first kill or hit to a pause; deaths and players apart; a boss pull keeps it open
        {
            var (p, kept) = New();
            p.SetMode(ParserMode.Fights, t);
            p.Place("Abyss", abyss: true);
            p.Kill(t, player: true, death: false, "Rhavos");
            p.Kill(t.AddSeconds(8), player: false, death: false, "");
            p.Tick(t.AddSeconds(20));
            Check(p.Live is { ModeKind: ParserMode.Fights, Kind: "Abyss", YourKills: 2, PlayerKills: 1, Title: "vs Rhavos" }, $"Every fight: kills in the Abyss make a fight: {p.Live?.Title} in {p.Live?.Kind}");
            Check(p.Live is { OpenWorld: true } && p.LiveAll.Count == 0 && p.Farm is { Kind: "Abyss" },
                "...shown while it lasts, but an open-world or Abyss fight is never among the records kept under way (nothing to write)");
            p.Tick(t.AddSeconds(8 + BossParser.PauseSeconds));
            // Steven, 2026-11-14: "you can have open world count for the parser inside of the app but it shouldn't log it anywhere ever"
            Check(kept.Count == 0 && p.Live == null && p.LastEnded == null && p.Farm == null,
                $"...and gone {BossParser.PauseSeconds:0} s after its last kill: an Abyss fight is never kept");
            p.Place("Western Cantas Valley", abyss: false);
            p.Kill(t.AddSeconds(100), player: false, death: false, "");
            p.Kill(t.AddSeconds(110), player: true, death: true, "Rhavos");
            p.Tick(t.AddSeconds(140));
            Check(kept.Count == 0, "...nor an open-world one, a death and all");
            p.Take(new InstanceEntered(t.AddSeconds(200), ContentKind.Sanctuary, "Chalice of Muspel", 88));
            p.Take(new BossEngaged(t.AddSeconds(210), "Kaldrix", 970));
            p.Take(new DamageDealt(t.AddSeconds(215), 1, 970, 10_000));
            p.Tick(t.AddSeconds(300));
            Check(p.Live is { Title: "Kaldrix", Kind: "Sanctuary" }, "...a boss pull keeps its fight open past the pause, named for the boss");
            p.Take(new BossEnded(t.AddSeconds(320), 970, BossOutcome.Kill));
            p.Tick(t.AddSeconds(345));
            Check(kept.Count == 1 && kept[0] is { Title: "Kaldrix", Outcome: "kill", HasDamage: true, OpenWorld: false }, "...and ends after it, kept as one fight: a fight inside an instance is kept as before");
            p.Take(new DamageDealt(t.AddSeconds(400), 1, 0, 50));
            p.Tick(t.AddSeconds(401 + BossParser.PauseSeconds));
            Check(kept.Count == 1, "a stray hit with no kill, death or pull isn't kept");
        }

        // farming in the open world in Bosses only and Full run: a live fight for the Kinah line's small DPS, never a record
        {
            var (p, kept) = New();
            p.Place("Western Cantas Valley", abyss: false);
            p.Take(new GroupChanged(t, new[] { new GroupMember(1, "Swoly", "Gladiator", 0, 0, true) }));
            for (int i = 0; i <= 10; i++) p.Take(new DamageDealt(t.AddSeconds(i), 1, 5000 + i, 12_000, SourceIsYou: true));
            var farm = p.Farm;
            Check(p.Live == null && farm is { OpenWorld: true, Kind: "Open world" } && farm.You is { Damage: 132_000 } && Math.Abs(farm.Dps(farm.You!, t.AddSeconds(10)) - 13_200) < 0.01,
                $"farming in the open world in Bosses only: no pull, but a live fight with your DPS for the Kinah line ({(farm?.You != null ? farm.Dps(farm.You, t.AddSeconds(10)) : 0):0})");
            // a 14 s walk to the next monster, then 5 s on it: the Kinah line's DPS is over the fighting, not the walk
            for (int i = 24; i <= 29; i++) p.Take(new DamageDealt(t.AddSeconds(i), 1, 5100, 12_000, SourceIsYou: true));
            var walked = p.Farm!;
            Check(walked.You!.FightingSeconds == 15 && Math.Abs(walked.FarmDps() - 17 * 12_000 / 15.0) < 0.01 && walked.Dps(walked.You, t.AddSeconds(29)) < walked.FarmDps(),
                $"...its DPS on the Kinah line is over your fighting time ({walked.You.FightingSeconds:0} s of 29: stretches of hits with gaps up to {Fighter.StretchGapSeconds:0} s), so walking between monsters doesn't pull it down ({walked.FarmDps():0})");
            p.Tick(t.AddSeconds(29 + BossParser.PauseSeconds));
            Check(p.Farm == null && kept.Count == 0 && p.LiveAll.Count == 0, "...gone after its pause, nothing kept");
            p.Take(new DamageDealt(t.AddSeconds(100), 1, 6000, 5_000, SourceIsYou: true));
            p.Take(new InstanceEntered(t.AddSeconds(105), ContentKind.Expedition, "Fire Temple", 89));
            Check(p.Farm == null, "...and entering an instance ends it: an instance's fights are its pulls and runs");
            p.Take(new DamageDealt(t.AddSeconds(110), 1, 6001, 5_000, SourceIsYou: true));
            Check(p.Farm == null, "...trash inside an instance is never farming");
        }

        // the live farming readout (Steven, 2026-11-14: "the dps figure needs to be rolling all the time"): every hit lifts it,
        // it slides down between hits, and when you stop it eases onto the fight's figure, never down to nothing
        {
            var (p, _) = New();
            p.Place("Western Cantas Valley", abyss: false);
            p.Take(new GroupChanged(t, new[] { new GroupMember(1, "Swoly", "Gladiator", 0, 0, true) }));
            for (int i = 0; i <= 20; i++) p.Take(new DamageDealt(t.AddSeconds(i * 0.5), 1, 5000, 10_000, SourceIsYou: true));   // 20K a second for 10 s
            var at = t.AddSeconds(10);
            double steady = p.FarmDpsNow(at.AddMilliseconds(250)) ?? 0;
            Check(Math.Abs(steady - 20_000) < 2_000, $"farming readout: a steady 20K a second reads about that ({steady:0})");
            double before = p.FarmDpsNow(at.AddMilliseconds(490)) ?? 0;
            p.Take(new DamageDealt(at.AddMilliseconds(500), 1, 5000, 10_000, SourceIsYou: true));
            double after = p.FarmDpsNow(at.AddMilliseconds(500)) ?? 0;
            Check(after > before + 1_000, $"...each hit lifts it at once ({before:0} to {after:0})");
            var slide = Enumerable.Range(1, 9).Select(k => p.FarmDpsNow(at.AddMilliseconds(500 + k * 100)) ?? 0).ToList();
            Check(slide.Zip(slide.Skip(1)).All(x => x.Second < x.First && x.First - x.Second < 1_000), $"...and slides down smoothly between hits, a little each tenth of a second ({slide[0]:0} to {slide[^1]:0})");
            var fight = p.Farm!.FarmDps();
            var stop = at.AddMilliseconds(500);
            var eased = Enumerable.Range(0, 60).Select(k => p.FarmDpsNow(stop.AddMilliseconds(k * 100)) ?? 0).ToList();
            Check(eased.All(v => v > fight * 0.5) && Math.Abs(eased[^1] - fight) < 1 && eased.Zip(eased.Skip(1)).All(x => Math.Abs(x.First - x.Second) < 1_500),
                $"...when you stop it eases onto the fight's figure ({fight:0}) within {BossParser.EaseAfterSeconds + BossParser.EaseSeconds:0} s, never jumping and never down to 0 (lowest {eased.Min():0})");
            Check(Ui.MainWindow.FarmFigure(850) == "850" && Ui.MainWindow.FarmFigure(9_940) == "9.9K" && Ui.MainWindow.FarmFigure(13_240) == "13.2K" && Ui.MainWindow.FarmFigure(1_254_000) == "1.25M",
                "...in one steady format: 850, 9.9K, 13.2K, 1.25M");
            p.Tick(stop.AddSeconds(BossParser.PauseSeconds + 1));
            Check(p.FarmDpsNow(stop.AddSeconds(BossParser.PauseSeconds + 1)) == null && !p.FarmLive, "...and is gone with the fight");
        }

        // open-world fights write nothing to disk: not on ending, not while under way, and an older version's are removed
        {
            var dir = TestTemp.Path("KinahLedger-parser-openworld-" + Guid.NewGuid().ToString("N")[..8]);
            var store = new EncounterStore(() => dir);
            var p = new BossParser(BossBook.Shared) { Ended = store.Add };
            p.SetMode(ParserMode.Fights, t);
            p.Place("Western Cantas Valley", abyss: false);
            p.Take(new GroupChanged(t, new[] { new GroupMember(1, "Swoly", "Gladiator", 0, 0, true) }));
            p.Take(new DamageDealt(t.AddSeconds(1), 1, 5000, 40_000, SourceIsYou: true));
            p.Kill(t.AddSeconds(2), player: false, death: false, "Kobold");
            store.SaveLive(p.LiveAll);
            bool nothingLive = !Directory.Exists(dir) || Directory.GetFiles(dir).Length == 0;
            p.Tick(t.AddSeconds(2 + BossParser.PauseSeconds));
            var ow = new Encounter { Mode = "fight", Kind = "Open world", Instance = "Western Cantas Valley", Start = t, End = t.AddSeconds(30), LastAt = t.AddSeconds(30), YourKills = 3 };
            store.Add(ow);
            store.SaveLive(new[] { new Encounter { Mode = "fight", Kind = "Abyss", Start = t, LastAt = t } });
            bool nothing = !Directory.Exists(dir) || Directory.GetFiles(dir).Length == 0;
            Check(nothingLive && nothing, $"open-world fights write nothing to disk: ending, under way, or handed to the store ({(Directory.Exists(dir) ? Directory.GetFiles(dir).Length : 0)} files)");
            // a file an older version wrote: its open-world fights are dropped from it, the rest kept as they were
            Directory.CreateDirectory(dir);
            var boss = new Encounter { Mode = "boss", Kind = "Sanctuary", Instance = "Chalice of Muspel", Boss = "Kaldrix", Start = t, End = t.AddSeconds(90), LastAt = t.AddSeconds(90), Outcome = "kill" };
            var jo = new System.Text.Json.JsonSerializerOptions();
            File.WriteAllLines(Path.Combine(dir, EncounterStore.FileName), new[] { System.Text.Json.JsonSerializer.Serialize(ow, jo), System.Text.Json.JsonSerializer.Serialize(boss, jo) });
            var again = new EncounterStore(() => dir).All();
            var lines = File.ReadAllLines(Path.Combine(dir, EncounterStore.FileName));
            Check(again.Count == 1 && again[0].Boss == "Kaldrix" && lines.Length == 1 && !lines[0].Contains("Open world"),
                $"...and the open-world fights an older version kept are removed from the file, the rest left as they were ({again.Count} read, {lines.Length} lines)");
            try { Directory.Delete(dir, true); } catch (IOException) { }
        }

        // changing the mode ends the old mode's record where it is and starts the new one there
        {
            var (p, kept) = New();
            p.SetMode(ParserMode.Run, t);
            p.Take(new InstanceEntered(t, ContentKind.Expedition, "Fire Temple", 89));
            p.Take(new BossEngaged(t.AddSeconds(30), "Silver Blade Rotan", 980));
            p.Take(new BossEnded(t.AddSeconds(90), 980, BossOutcome.Kill));
            p.SetMode(ParserMode.Bosses, t.AddSeconds(100));
            Check(kept.Count == 1 && kept[0] is { ModeKind: ParserMode.Run, Partial: true } && kept[0].PartialWhy.Contains("the Parser's mode changed"), "switching from Full run keeps the run so far, partial");
            p.Take(new BossEngaged(t.AddSeconds(120), "Red Spark Ignus", 981));
            p.SetMode(ParserMode.Run, t.AddSeconds(130));
            Check(p.Live is { ModeKind: ParserMode.Run, Partial: true } l2 && l2.PartialWhy.Contains("counted from partway through the run") && l2.Splits.Count == 1,
                "...switching to Full run inside an instance starts a run there, partial, with the pull under way");
            p.Take(new BossEnded(t.AddSeconds(150), 981, BossOutcome.Kill));
            Check(kept.Count == 1, "...a pull started under Bosses only and ended under Full run is the run's split, not a record of its own");
        }

        // Steven's Ferocious Horn Den (2026-11-14): damage by ability, no row before a member's first damage, the Totem room a wave
        {
            var (p, kept) = New();
            p.Take(new InstanceEntered(t, ContentKind.Expedition, "Ferocious Horn Den", 70));
            p.Take(new GroupChanged(t, new[] { new GroupMember(1, "Swoly", "Templar", 0, 0, true), new GroupMember(2, "Felicity", "Chanter", 0, 0, false) }));
            p.Take(new BossEngaged(t.AddSeconds(5), "Watchdog Kwapo", 700));
            p.Take(new DamageDealt(t.AddSeconds(6), 1, 700, 600_000, Skill: 12240000, Hits: 20));
            p.Take(new DamageDealt(t.AddSeconds(7), 1, 700, 55_000, Skill: 12240000, Hits: 4));
            p.Take(new DamageDealt(t.AddSeconds(8), 1, 700, 1_000, Skill: 0, Hits: 0));
            var l = p.Live!;
            var me = l.Roster.Single(f => f.Id == 1);
            var judgment = me.Abilities.Single(a => a.Skill == 12240000);
            Check(judgment is { Damage: 655_000, Hits: 24 } && Math.Abs(judgment.Average - 655_000 / 24.0) < 0.01 && me.Abilities.Single(a => a.Skill == 0).Hits == 1
                && Abilities.Name(12240000) == "Judgment" && Abilities.Name(0).Length > 0 && Abilities.Name(12060000) == "Ability 12060000",
                $"damage by ability: each skill's total, hits and average a hit, named from the skill list ({Abilities.Count} skills); a skill it doesn't list says its number");
            // each ability the skill list names has its icon in the exe; one it doesn't list has none (the class's glyph stands in)
            Check(Ui.SkillArt.Count == Abilities.Count && Ui.SkillArt.Image(12240000) != null && Ui.SkillArt.Image(12060000) == null && Ui.SkillArt.Image(0) == null,
                $"ability icons: {Ui.SkillArt.Count} of the {Abilities.Count} listed skills carry their icon; an unlisted one and damage over time have none");
            Check(l.Shown().Count() == 1 && l.Shown().Single().Id == 1 && l.Roster.Count == 2,
                "a member with no damage yet has no row (Felicity), though the record keeps them for its detail");
            p.Take(new BossEnded(t.AddSeconds(50), 700, BossOutcome.Kill));
            p.Take(new BossEngaged(t.AddSeconds(80), "Totem room", 701));
            Check(p.Live == null, "the Totem room is a wave, not a boss: Bosses only shows no pull under way...");
            p.Take(new DamageDealt(t.AddSeconds(90), 1, 701, 300_000));
            p.Take(new BossEnded(t.AddSeconds(116), 701, BossOutcome.Kill));
            Check(kept.Count == 1 && kept[0].Boss == "Watchdog Kwapo", "...and keeps no record of it");
            var (q, keptRun) = New();
            q.SetMode(ParserMode.Run, t);
            q.Take(new InstanceEntered(t, ContentKind.Expedition, "Ferocious Horn Den", 71));
            q.Take(new BossEngaged(t.AddSeconds(80), "Totem room", 711));
            q.Take(new DamageDealt(t.AddSeconds(90), 1, 711, 300_000));
            q.Take(new BossEnded(t.AddSeconds(116), 711, BossOutcome.Kill));
            q.Take(new InstanceLeft(t.AddSeconds(200)));
            Check(keptRun.Count == 1 && keptRun[0].Splits.Single() is { Boss: "Totem room", Wave: true, Outcome: "kill", Seconds: 36 },
                "in Full run it is a split of its own, its time and its result");
        }

        // the instance's mode, each member's server, DPS and share, DPS over the fight or over each member's active time
        {
            var (p, kept) = New();
            p.Take(new InstanceEntered(t, ContentKind.Expedition, "Ferocious Horn Den", 72));
            p.Take(new GroupChanged(t, new[] { new GroupMember(1, "Swoly", "templar", 0, 0, true, "Siel"), new GroupMember(2, "Felicity", "Chanter", 0, 0, false, "Israphel") }));
            p.Take(new BossEngaged(t.AddSeconds(2), "Watchdog Kwapo", 720));
            p.Take(new InstanceMode(t.AddSeconds(3), "Conquest"));
            var l = p.Live!;
            Check(l.GameMode == "Conquest" && l.Place == "Ferocious Horn Den · Conquest" && l.Where == "Ferocious Horn Den · Conquest · Expedition",
                $"the instance's mode, when it comes after the pull: the header says {l.Place}");
            Check(l.Roster.Single(f => f.Id == 1) is { Server: "Siel", Class: "templar" } && KinahLedger.Ui.ClassArt.Key("templar") == "templar" && l.Roster.Single(f => f.Id == 2).Server == "Israphel",
                "each member's server from the group list; a class name in any case is the class");
            // Swoly hits from 0 to 100 s; Felicity from 0 to 20 s, then once more at 90 s after a long pause
            for (int i = 0; i <= 100; i += 10) p.Take(new DamageDealt(t.AddSeconds(2 + i), 1, 720, 30_000));
            p.Take(new DamageDealt(t.AddSeconds(2), 2, 720, 10_000));
            p.Take(new DamageDealt(t.AddSeconds(22), 2, 720, 10_000));
            p.Take(new DamageDealt(t.AddSeconds(92), 2, 720, 10_000));
            p.Take(new UnitDied(t.AddSeconds(102), 720, "Watchdog Kwapo", true));
            var e = kept.Single();
            var me = e.Roster.Single(f => f.Id == 1); var fel = e.Roster.Single(f => f.Id == 2);
            var end = e.End!.Value;
            bool basis = Encounter.ActiveBasis;
            Encounter.ActiveBasis = false;
            double fightDps = e.Dps(fel, end);
            Encounter.ActiveBasis = true;
            double activeDps = e.Dps(fel, end), myActive = e.Dps(me, end);
            Encounter.ActiveBasis = basis;
            Check(e.GameMode == "Conquest" && Math.Abs(fightDps - 30_000 / 100.0) < 0.01 && fel.ActiveSeconds == 20 && Math.Abs(activeDps - 30_000 / 20.0) < 0.01
                && me.ActiveSeconds == 100 && Math.Abs(myActive - 330_000 / 100.0) < 0.01,
                $"DPS over the fight by default ({fightDps:0}); over a member's active time, a pause over 20 s left out ({activeDps:0}, {fel.ActiveSeconds:0} s)");
            Check(Math.Abs(e.Share(me) - 330 / 360.0) < 1e-9 && Encounter.Pct(e.Share(me)) == "91.7%" && Encounter.Pct(1) == "100%" && Encounter.Pct(0.2512) == "25.1%",
                $"each row's share of the group's damage: {Encounter.Pct(e.Share(me))} and {Encounter.Pct(e.Share(fel))}");
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
            store.SaveLive(p.LiveAll);
            File.AppendAllText(Path.Combine(dir, EncounterStore.FileName), "{\"Id\":\"cut");   // a line cut by a crash
            var again = new EncounterStore(() => dir);
            var all = again.All();
            Check(all.Count == 1 && all[0] is { Boss: "Talisra of the Void", Outcome: "kill" } && all[0].Roster.Count == 2,
                $"the kept fights read back, a cut line skipped: {all.Count}");
            var live = again.LoadLive();
            var (q, kept2) = New();
            q.Ended = e => { kept2.Add(e); again.Add(e); };
            q.Resume(live, t.AddSeconds(160));
            Check(q.Live is { Boss: "Siliator of Deceit", Partial: true } l && l.PartialWhy.Contains("the tracker restarted during it") && q.Where.Instance == "Shattered Arkanis",
                "a restart in the middle of a fight carries it on, partial, in its instance");
            q.Take(new InstanceEntered(t.AddSeconds(170), ContentKind.Expedition, "Shattered Arkanis", 83));
            q.Take(new BossEnded(t.AddSeconds(200), 941, BossOutcome.Kill));
            Check(kept2.Count == 1 && kept2[0].Duration(t).TotalSeconds == 100 && again.All().Count == 2, "...and ends as the one fight it was, from its own pull");
            live = new EncounterStore(() => dir).LoadLive();
            var (r, kept3) = New();
            r.Resume(live, t.AddMinutes(40));
            Check(r.Live == null && kept3.Count == 1 && kept3[0] is { Outcome: "left", Partial: true } && kept3[0].End == live[0].LastAt,
                "a fight saved long ago is kept as it was, ended at the last thing heard");
            again.Add(kept2[0]);   // the same fight twice counts once
            Check(new EncounterStore(() => dir).All().Count == 2, "a fight kept twice reads back once");
            // the first builds kept one record, not a list: still read
            File.WriteAllText(Path.Combine(dir, EncounterStore.LiveName), System.Text.Json.JsonSerializer.Serialize(live[0]));
            var old = new EncounterStore(() => dir).LoadLive();
            Check(old.Count == 1 && old[0].Boss == "Siliator of Deceit" && old[0].ModeKind == ParserMode.Bosses, "a live file of one record (the first builds) still reads, as a boss pull");
            try { Directory.Delete(dir, true); } catch (Exception) { }
        }

        // the party list in the top-left corner: rows by their mana bars, each class from its icon (the game's own icons, cut from
        // Steven's pictures: tracker/tests/party), an away member's row from the spacing; then the Parser's members named by it
        {
            var dir = PartyFixtures();
            Check(dir != null, "the party list's test icons are found (tracker/tests/party)");
            if (dir != null)
            {
                const int gh = 2160;
                string[] want = { "chanter", "templar", "cleric", "gladiator", "ranger", "sorcerer", "spiritmaster", "assassin" };
                var region = Ocr.PartyFrame.Region(new System.Drawing.Rectangle(0, 0, 5120, gh));
                using var frame = new System.Drawing.Bitmap(region.Width, region.Height, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
                using (var gfx = System.Drawing.Graphics.FromImage(frame))
                {
                    gfx.Clear(System.Drawing.Color.FromArgb(48, 40, 34));
                    for (int k = 0; k < want.Length; k++)
                    {
                        int y = 375 - region.Top + k * 96;   // the rows as Steven's pictures have them at 2160: 96 px apart, the bars from x 100
                        using var icon = new System.Drawing.Bitmap(Path.Combine(dir, want[k] + ".png"));
                        var away = k == 3;   // the fourth member is away: a grey icon, no bars
                        using var draw = away ? Grey(icon) : (System.Drawing.Bitmap)icon.Clone();
                        gfx.DrawImage(draw, 100 - 72, y - 23 - 36, icon.Width, icon.Height);   // the icon 36 px left of the bars and 23 up, 72 square
                        if (away) continue;
                        gfx.FillRectangle(new System.Drawing.SolidBrush(System.Drawing.Color.FromArgb(232, 60, 72)), 100, y - 16, 290, 10);
                        gfx.FillRectangle(new System.Drawing.SolidBrush(System.Drawing.Color.FromArgb(40, 180, 230)), 100, y - 5, k == 5 ? 60 : 290, 10);   // the sixth low on mana
                    }
                }
                var rows = Ocr.PartyFrame.Read(frame, gh);
                Check(rows.Select(r => r.Class).SequenceEqual(want) && rows.Count(r => !r.Online) == 1 && !rows[3].Online,
                    $"the party list: eight rows, each class from its icon, the away member's row from the spacing: {string.Join(", ", rows.Select(r => r.Class + (r.Online ? "" : " (away)")))}");
            }
            Check(Ocr.PartyFrame.CleanName(".ııl SwolyDaBooly") == "SwolyDaBooly" && Ocr.PartyFrame.CleanName("OHaa[ISR]") == "OHaa" && Ocr.PartyFrame.CleanName("... Goldlemon[ISR]") == "Goldlemon",
                "a name as the party list prints it: the signal bars before it and the guild's tag after it dropped");

            var path = Path.Combine(TestTemp.Path("KinahLedger-parser-test-classes"), "class-numbers.json");
            if (File.Exists(path)) File.Delete(path);
            ClassNumbers.PathOverride = path; ClassNumbers.Reset();
            var (p, kept) = New();
            p.Take(new InstanceEntered(t, ContentKind.Expedition, "Fire Temple", 99));
            p.Take(new GroupChanged(t, new[] { new GroupMember(11, "Swoly", "", 0, 0, true), new GroupMember(12, "LMZ111", "7", 0, 0, false), new GroupMember(13, "ImBrickedUp", "", 0, 0, false) }));
            p.Take(new BossEngaged(t.AddSeconds(5), "Silver Blade Rotan", 990));
            p.Take(new DamageDealt(t.AddSeconds(6), 14, 990, 1_000, "Felicity"));
            p.ScreenParty(new[] { ("", "templar", true), ("LMZI 11", "cleric", false), ("lmBrickedUp", "templar", false), ("Felicity", "chanter", false) });
            var l = p.Live!;
            Check(l.Roster.Single(f => f.You).Class == "templar" && l.Roster.Single(f => f.Id == 12).Class == "cleric" && l.Roster.Single(f => f.Id == 13).Class == "templar" && l.Roster.Single(f => f.Id == 14).Class == "chanter",
                $"the Parser's members take their class from the party list: you from the first row, the others by name, misreads forgiven: {string.Join(", ", l.Roster.Select(f => f.Name + " " + f.Class))}");
            Check(ClassNumbers.Name(7) == "cleric", "...and a class that came as a number is paired with the class the party list shows");
            var (q, _) = New();
            q.Take(new InstanceEntered(t, ContentKind.Expedition, "Fire Temple", 98));
            q.Take(new GroupChanged(t, new[] { new GroupMember(21, "Another", "7", 0, 0, false) }));
            q.Take(new BossEngaged(t.AddSeconds(5), "Silver Blade Rotan", 991));
            Check(q.Live?.Roster.Single().Class == "cleric", "...so the number alone names the class afterwards, with no party list in view");
            ClassNumbers.PathOverride = null; ClassNumbers.Reset();
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

        // Group details on by default (Steven, 2026-11-14): a fresh install has it on; a parser.json from before moves to on
        // once; after that move a player's off stays off
        {
            var fresh = new ParserPrefs();
            var old = ParserPrefs.FromText("{\"Overlay\":true,\"Details\":false,\"Mode\":\"boss\"}");
            var chosenOff = ParserPrefs.FromText("{\"Details\":false,\"DetailsSettled\":true}");
            Check(fresh.Details && old.Details && old.DetailsSettled && !chosenOff.Details,
                $"Group details: on for a new player ({fresh.Details}), moved to on once for a player from before ({old.Details}), an off chosen after that kept ({!chosenOff.Details})");
        }

        Console.WriteLine(_fails == 0 ? "parser-test: all passed" : $"parser-test: {_fails} failed");
        return _fails == 0 ? 0 : 1;
    }
}
