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
    public static int ParserReplay(string pcapng, string mode, int port = 13328, string? save = null, bool crit = false, bool always = false)
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
        var farmRecords = new List<Encounter>();   // the open-world fights themselves, for --save
        Encounter? farmNow = null; var shown = new List<double>(); DateTime sampled = DateTime.MinValue;
        // --always: the Always on meter's stretches, each from its start (from 0) to what ended it
        Encounter? alwaysNow = null;
        var stretches = new List<Encounter>();
        void Stretch(Encounter? a)
        {
            bool same = a != null && alwaysNow != null && a.Start == alwaysNow.Start && a.Mode == alwaysNow.Mode;
            if (alwaysNow != null && !same) stretches.Add(p.AlwaysEnded is { } en && en.Start == alwaysNow.Start && en.Mode == alwaysNow.Mode ? en : alwaysNow);
            if (a != null) alwaysNow = a; else if (!same) alwaysNow = null;
        }
        void Sample(DateTime at)
        {
            if (always) Stretch(p.AlwaysLive);
            if ((at - sampled).TotalSeconds < 1) return;
            sampled = at;
            var f = p.Farm;
            if (f != null && p.FarmDpsNow(at) is { } now0 && now0 > 0) shown.Add(now0);   // what the Kinah line reads now (the live readout)
            if (farmNow != null && (f == null || f.Start != farmNow.Start))
            {
                farms.Add((farmNow.Start, farmNow.LastAt, farmNow.You?.Damage ?? 0, farmNow.You?.FightingSeconds ?? 0, shown));
                farmRecords.Add(farmNow);
                shown = new();
            }
            farmNow = f;
        }
        // one decoder for the whole recording, reset at each new connection, as the tracker does (PacketReader): its count
        // of instances entered carries on, and the Parser hears of each new connection as a gap
        using var dec = NativeDecoder.TryOpen()!;
        bool events = Environment.GetEnvironmentVariable("KL_REPLAY_EVENTS") == "1";
        bool firstConn = true;
        foreach (var conn in segs.GroupBy(x => (x.S.Src.ToString(), x.S.DstPort)).OrderBy(g => g.First().At))
        {
            dec.Reset();
            if (!firstConn) p.Take(new FeedGap(conn.First().At));
            firstConn = false;
            var st = new TcpStream
            {
                OnData = (d, at) =>
                {
                    last = at;
                    dec.Feed(d, at);
                    while (dec.Next() is not null) { }
                    while (dec.NextItem() is not null) { }
                    while (dec.NextFight() is { } f) TrackerSession.RawFight(p, f);
                    while (dec.NextBossEvent() is { } b)
                    {
                        if (events && b is InstanceEntered or InstanceLeft or GroupChanged or BossEngaged or BossEnded)
                            Console.WriteLine($"event {b.At.ToLocalTime():HH:mm:ss} {(b is GroupChanged g ? "group " + string.Join(", ", g.Members.Select(m => $"{m.Name}#{m.Id}{(m.You ? " (you)" : "")}")) : b.ToString())}");
                        p.Take(b);
                    }
                    while (dec.NextHit() is { } h) p.Take(h);
                },
                OnGap = () => { dec.Gap(); p.Take(new FeedGap(last)); },
            };
            foreach (var (at, s) in conn) { st.Add(s.Seq, s.Payload, at, s.Syn); p.Tick(at); Sample(at); }
        }
        p.Tick(DateTime.MaxValue.AddDays(-1));
        Sample(DateTime.MaxValue.AddDays(-1));
        if (always)
            foreach (var a in stretches)
            {
                var end = a.End ?? a.LastAt;
                Console.WriteLine($"always on {a.Start.ToLocalTime():HH:mm:ss}-{end.ToLocalTime():HH:mm:ss} ({BossParser.Clock(a.Duration(end))}) · {(a.Mode == "boss" ? "boss pull " + a.Boss : a.Boss.Length > 0 ? "fight, " + a.Boss : "fight")} · {(a.Kind.Length > 0 ? a.Kind : "?")} {a.Instance}"
                    + $" · group {Ui.Amounts.Short(a.Members.Sum(f => f.Damage))}, you {Ui.Amounts.Short(a.You?.Damage ?? 0)}{(a.Outcome.Length > 0 ? " · " + a.Outcome : "")}"
                    + $" · your DPS {(a.You is { } y ? a.DpsText(y, end) : "–")} over {a.DpsSeconds(end):0} s");
            }
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
            if (e.Coverage is { } cov) Console.WriteLine($"   {Encounter.Pct(cov)} of the boss's HP credited ({Ui.Amounts.Short(e.UncreditedBoss)} not)");
            if (crit) PrintCrit(e);
            var ms = e.Members.ToList();
            if (ms.Any(f => f.PerSecond != null || f.Taken != null))
                Console.WriteLine($"   depth: {ms.Sum(f => f.Abilities.Sum(a => a.Casts ?? 0)):N0} casts, {ms.Sum(f => f.Abilities.Sum(a => a.StreamHits ?? 0)):N0} hits seen, {Ui.Amounts.Short(ms.Sum(f => f.Taken?.Sum(x => x.Damage) ?? 0))} taken, "
                    + $"{ms.Sum(f => f.DeathLog?.Count ?? 0)} deaths logged, {ms.Sum(f => f.Buffs?.Count ?? 0)} uptimes, {e.BossHpPerSecond?.Count ?? 0} s of boss HP");
            foreach (var x in e.Splits) Console.WriteLine($"   split {x.Boss}{(x.Wave ? " (wave)" : "")}: {x.Outcome} in {BossParser.Clock(TimeSpan.FromSeconds(x.Seconds))}");
            long total = Math.Max(1, e.Roster.Sum(f => f.Damage));
            foreach (var f in e.Ranked())
                Console.WriteLine($"   {(f.Name.Length > 0 ? f.Name : "Unnamed"),-16} {(f.Class.Length > 0 ? f.Class : "-"),-13} {f.Damage * 100.0 / total,5:0.0}%  {Ui.Amounts.Short(f.Damage),8}  {(e.Shown().Contains(f) ? "" : "(no row: no damage)")}"
                    + (f.Abilities.Count > 0 ? "  top: " + string.Join(", ", f.Abilities.OrderByDescending(a => a.Damage).Take(3).Select(a => $"{Abilities.Name(a.Skill, f.Class)} {Ui.Amounts.Short(a.Damage)} in {(a.Hits > 0 ? a.Hits.ToString() : "–")}")) : ""));
        }
        Console.WriteLine($"{kept.Count} records kept ({BossParser.Name(p.Mode)})");
        // --save <file>: the records as the tracker keeps them (encounters.jsonl's lines), for tools/skill-fill.js --fights
        // (the open-world fights too: a recording that starts inside an instance has no boss records, but its damage by ability is there)
        if (save != null) File.AppendAllLines(save, kept.Concat(farmRecords).Select(e => System.Text.Json.JsonSerializer.Serialize(e)));
        return 0;
    }

    /// <summary>The two crit candidates the hits carry: the result byte's 3 (crit-like, x1.5 to x2.1 a caster) and mark 0x08 (a fixed x2 proc).</summary>
    public static readonly (string Key, string Label)[] CritCandidates = { ("result:3", "result 3"), ("mark:3", "mark 0x08") };

    /// <summary>
    /// parser-replay --crit: a record's hits by member and skill, laid out like the game's Combat Analysis (detailed): each
    /// skill's damage, share, hits, then for each crit candidate its hits, rate and its hits' average against the others'
    /// (the multiplier), and the biggest hit. All targets, the hits seen one by one (a share with no hit under it left out).
    /// </summary>
    static void PrintCrit(Encounter e)
    {
        bool bossOnly = Encounter.BossOnly;
        Encounter.BossOnly = false;
        try
        {
            string Rate(int n, int of) => of > 0 ? $"{n * 100.0 / of,5:0.0}%" : "    -";
            string Mult((int Hits, long Damage, long Max) k, int hits, long dmg)
            {
                int rest = hits - k.Hits; long restDmg = dmg - k.Damage;
                return k.Hits > 0 && rest > 0 && restDmg > 0 ? $"x{k.Damage / (double)k.Hits / (restDmg / (double)rest),4:0.00}" : "    -";
            }
            string Head() => $"      {"Skill",-34} {"Damage",9} {"Share",6} {"Hits",5}" + string.Concat(CritCandidates.Select(c => $" | {c.Label,-9} {"rate",6} {"mult",5}")) + $" | {"back %",6} | {"Max",8}";
            foreach (var f in e.Ranked().Where(f => f.Abilities.Any(a => a.StreamHits > 0)))
            {
                var rows = e.AbilityRows(f).Where(a => a.StreamHits > 0).ToList();
                int hits = rows.Sum(a => a.StreamHits ?? 0); long dmg = rows.Sum(a => a.StreamDamage ?? 0);
                var line = $"   {f.Name} ({(f.Class.Length > 0 ? f.Class : "-")}): {Ui.Amounts.Short(f.Damage)} damage, {hits:N0} hits seen";
                foreach (var (key, label) in CritCandidates)
                {
                    var k = rows.Select(a => Encounter.KindTotals(a, key)).Aggregate((Hits: 0, Damage: 0L, Max: 0L), (s, x) => (s.Hits + x.Hits, s.Damage + x.Damage, Math.Max(s.Max, x.Max)));
                    line += $"; {label}: {k.Hits:N0} ({Rate(k.Hits, hits).Trim()}), {Mult(k, hits, dmg).Trim()}, {(dmg > 0 ? k.Damage * 100.0 / dmg : 0):0.0}% of the damage, max {Ui.Amounts.Short(k.Max)}";
                }
                int back = rows.Sum(a => Encounter.KindTotals(a, "side:1").Hits), sided = rows.Sum(a => (a.Kinds?.GetValueOrDefault("side:1") ?? 0) + (a.Kinds?.GetValueOrDefault("side:2") ?? 0));
                line += $"; back {back:N0} ({Rate(back, hits).Trim()} of hits, {(sided > 0 ? Rate(back, sided).Trim() : "-")} of those with a side)";
                Console.WriteLine(line);
                Console.WriteLine(Head());
                foreach (var a in rows)
                {
                    int h = a.StreamHits ?? 0; long d = a.StreamDamage ?? 0;
                    var r = $"      {Abilities.Name(a.Skill, f.Class),-34} {Ui.Amounts.Short(a.Damage),9} {(f.Damage > 0 ? a.Damage * 100.0 / f.Damage : 0),5:0.0}% {h,5}";
                    foreach (var (key, _) in CritCandidates) { var k = Encounter.KindTotals(a, key); r += $" | {k.Hits,-9} {Rate(k.Hits, h),6} {Mult(k, h, d),5}"; }
                    Console.WriteLine(r + $" | {Rate(Encounter.KindTotals(a, "side:1").Hits, h),6} | {Ui.Amounts.Short(a.Max ?? 0),8}");
                }
            }
        }
        finally { Encounter.BossOnly = bossOnly; }
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

    /// <summary>
    /// A recording replayed through this build's decoder into a Bosses-only Parser, as parser-replay does (each piece at its
    /// own capture time, the tick at each): the pulls kept. <paramref name="hits"/> false leaves the hit stream undrained,
    /// as a decoder before interface version 10 would.
    /// </summary>
    static List<Encounter> Replay(string pcapng, bool hits, int port = 13328)
    {
        var kept = new List<Encounter>();
        var p = new BossParser { Ended = kept.Add };
        var segs = new List<(DateTime At, TcpSegment S)>();
        foreach (var (at, frame, link) in Pcapng.Read(pcapng))
            if (NpcapSource.IpOf(frame, link) is { } ip && TcpSegment.Parse(ip) is { } s && s.SrcPort == port) segs.Add((at, s));
        DateTime last = DateTime.MinValue;
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
                    while (dec.NextHit() is { } h) { if (hits) p.Take(h); }
                },
                OnGap = () => { dec.Gap(); p.Take(new FeedGap(last)); },
            };
            foreach (var (at, s) in conn) { st.Add(s.Seq, s.Payload, at, s.Syn); p.Tick(at); }
        }
        p.Tick(DateTime.MaxValue.AddDays(-1));
        return kept;
    }

    /// <summary>Steven's recordings for the store v2 cases (his tracker's own, 2026-11-14 14:00 to 17:00), when they are on this PC.</summary>
    static List<string> Recordings() => new[] { "20261114-14", "20261114-15", "20261114-16" }
        .Select(n => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "KinahLedger", "packets", n + ".pcapng")).Where(File.Exists).ToList();

    /// <summary>Steven's saved fights (tracker/tests/parser/NAME.jsonl, his encounters.jsonl as kept), read as the tracker reads them.</summary>
    static List<Encounter> Saved(string name)
    {
        for (var d = new DirectoryInfo(AppContext.BaseDirectory); d != null; d = d.Parent)
            foreach (var p in new[] { Path.Combine(d.FullName, "tracker", "tests", "parser", name + ".jsonl"), Path.Combine(d.FullName, "tests", "parser", name + ".jsonl") })
                if (File.Exists(p))
                    return File.ReadAllLines(p).Where(l => l.Trim().Length > 0).Select(l => System.Text.Json.JsonSerializer.Deserialize<Encounter>(l)!).ToList();
        return new();
    }

    /// <summary>
    /// A saved boss pull played back as the events that made it: the instance, the group (less a late joiner until
    /// <paramref name="lateAt"/>; less a leaver from <paramref name="leaveAt"/>), the pull (<paramref name="engageShift"/>
    /// seconds from its start), each member's abilities as up to 12 hits each spread evenly from their first hit to their
    /// last (with their counts; damage over time with none), its uncredited damage as hits with no source, the boss's HP after
    /// each hit on it, any <paramref name="extra"/> hits, and its end as kept (unless <paramref name="end"/> is false).
    /// </summary>
    static List<BossEvent> EventsOf(Encounter rec, long inst, double engageShift = 0, bool end = true, long lateId = 0, DateTime? lateAt = null,
        long leaveId = 0, DateTime? leaveAt = null, IEnumerable<DamageDealt>? extra = null)
    {
        var all = new List<(DateTime At, int Seq, BossEvent E)>();
        void Add(BossEvent e) => all.Add((e.At, all.Count, e));
        var members = rec.Members.ToList();
        GroupMember M(Fighter f) => new(f.Id, f.Name, f.Class, f.CombatPower, f.ItemLevel, f.You, f.Server);
        var endAt = rec.End ?? rec.LastAt;
        long max = Math.Max(rec.BossMaxHp, rec.BossDamage);
        Add(new InstanceEntered(rec.Start.AddSeconds(-60), Encounter.KindOf(rec.Kind), rec.Instance, inst));
        Add(new GroupChanged(rec.Start.AddSeconds(-30), members.Where(f => f.Id != lateId).Select(M).ToList()));
        Add(new BossEngaged(rec.Start.AddSeconds(engageShift), rec.Boss, rec.BossId));
        Add(new BossHealth(rec.Start.AddSeconds(engageShift), rec.BossId, max, max));
        var hits = new List<DamageDealt>();
        foreach (var f in members)
        {
            if (f.FirstHit is not { } a || f.LastHit is not { } b) continue;
            foreach (var ab in f.Abilities)
            {
                int n = Math.Clamp(ab.Hits, 1, 12);
                long left = ab.Damage; int hl = ab.Skill == 0 ? 0 : ab.Hits;
                for (int i = 0; i < n; i++)
                {
                    var when = n == 1 ? b : a + (b - a) * (i / (double)(n - 1));
                    long amount = i == n - 1 ? left : ab.Damage / n; left -= amount;
                    int h = ab.Skill == 0 ? 0 : i == n - 1 ? hl : ab.Hits / n; hl -= h;
                    hits.Add(new DamageDealt(when, f.Id, rec.BossId, amount, "", f.You, ab.Skill, h));
                }
            }
        }
        for (int i = 0, n = rec.Uncredited > 0 ? 5 : 0; i < n; i++)
            hits.Add(new DamageDealt(rec.Start + (endAt - rec.Start) * ((i + 0.5) / n), 0, rec.BossId, i == n - 1 ? rec.Uncredited - rec.Uncredited / n * (n - 1) : rec.Uncredited / n));
        if (extra != null) hits.AddRange(extra);
        long hp = max;
        foreach (var h in hits.OrderBy(h => h.At))
        {
            Add(h);
            if (h.TargetId == rec.BossId) { hp -= h.Amount; Add(new BossHealth(h.At, rec.BossId, Math.Max(0, hp), max)); }
        }
        if (lateAt is { } la) Add(new GroupChanged(la, members.Select(M).ToList()));
        if (leaveAt is { } lv) Add(new GroupChanged(lv, members.Where(f => f.Id != leaveId).Select(M).ToList()));
        if (end)
            Add(rec.Outcome switch
            {
                "kill" => new UnitDied(endAt, rec.BossId, rec.Boss, true),
                "wipe" => new BossEnded(endAt, rec.BossId, BossOutcome.Wipe),
                _ => new InstanceLeft(endAt),
            });
        return all.OrderBy(x => x.At).ThenBy(x => x.Seq).Select(x => x.E).ToList();
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
            Check(kept[0].Start == t.AddSeconds(20) && kept[0].End == t.AddSeconds(30) && Math.Abs(kept[0].Dps(kept[0].Roster.First(f => f.You), t) - 50_000 / 10.0) < 0.01 && kept[0].Ranked().First().Name == "Swoly",
                $"...from its first damage to its last (the pull came 10 s before the first hit); its DPS from the game's damage, ranked by damage: {kept[0].Dps(kept[0].Roster.First(f => f.You), t):0}");
            p.Take(new DamageDealt(t.AddSeconds(150), 1, 903, 1_000));
            p.Take(new DamageDealt(t.AddSeconds(165), 1, 903, 1_000));
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
            p.Take(new DamageDealt(t.AddSeconds(222), 1, 911, 1_000));
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
            p.Take(new DamageDealt(t.AddSeconds(8), 0, 950, 1_000, Reason: 1));   // no hit came with the fall: uncredited
            p.Take(new BossHealth(t.AddSeconds(8), 950, 40_000, 100_000));
            var l = p.Live!;
            Check(l.Roster.Count == 3 && l.Members.Count() == 3 && l.You?.Name == "Swoly" && l.Roster.Any(f => f.Id == 13 && f.Name == "" && f.FromDamage),
                $"with no group list, who hit the boss is the group (you marked, an unnamed source kept): {string.Join(", ", l.Ranked().Select(f => f.Name + " " + f.Damage))}");
            Check(!l.Roster.Any(f => f.Other) && l.Uncredited == 1_000 && l.UncreditedBoss == 1_000 && l.UncreditedWhy[1] == 1_000 && l.Credited == 59_000 && l.BossDamage == 60_000 && l.BossHpLeft == 0.4,
                $"...damage no hit names is no member and no row: the record's uncredited {l.Uncredited:N0} (its reason kept), the boss at {l.BossHpLeft:P0}");
            Check(Math.Abs(l.Coverage!.Value - 59.0 / 60) < 1e-9 && Encounter.Pct(l.Coverage.Value) == "98.3%",
                $"...the group card's coverage: {Encounter.Pct(l.Coverage.Value)} of the boss's HP credited");
            Check(Math.Abs(l.Members.Sum(f => l.Share(f)) - 1) < 1e-9 && Math.Abs(l.Share(l.You!) - 30.0 / 59) < 1e-9,
                $"...the shares are of the credited damage and add up to 100% ({string.Join(", ", l.Members.Select(f => Encounter.Pct(l.Share(f))))})");
            var ready = Encounter.Ready(ContentKind.Expedition, "Vakron Sky Island", "", t);
            Check(ready is { Waiting: true, Live: false, Title: "Vakron Sky Island", Kind: "Expedition" } && ready.Duration(t.AddMinutes(3)) == TimeSpan.Zero && !ready.Shown().Any(),
                "a dungeon's ready card (the overlay before its first pull): its name, no clock, no rows");
            p.Take(new GroupChanged(t.AddSeconds(9), new[] { new GroupMember(11, "Swoly", "Gladiator", 4_800, 5_640, true), new GroupMember(12, "Aerin", "Cleric", 4_600, 5_600, false) }));
            l = p.Live!;
            var me = l.Roster.Single(f => f.Id == 11);
            Check(me is { CombatPower: 4_800, ItemLevel: 5_640, Class: "Gladiator", Damage: 30_000, Late: false, FromDamage: false } && !l.Roster.Single(f => f.Id == 13).Gone,
                "...and the group list fills in class, CP and item level on the same rows, nobody marked late or gone for it");
            p.Take(new DamageDealt(t.AddSeconds(12), 11, 950, 5_000));
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

        // back into an instance of the same name: a reconnect into the same copy or running it again (2026-11-14: three
        // Vakron Sky Island runs in a row, each with another group, read as one 35-minute run with three rows for you).
        // Never by the decoder's number for the entry: the tracker's numbers every entry, a replay's restarts at 1
        {
            var (p, kept) = New();
            p.SetMode(ParserMode.Run, t);
            p.Take(new InstanceEntered(t, ContentKind.Expedition, "Shattered Arkanis", 1));
            p.Take(new GroupChanged(t.AddSeconds(1), new[] { Me, Ally }));
            p.Take(new BossEngaged(t.AddSeconds(60), "Talisra of the Void", 960));
            p.Take(new DamageDealt(t.AddSeconds(70), 1, 960, 40_000, "Swoly", true));
            p.Take(new DamageDealt(t.AddSeconds(71), 2, 960, 10_000, "Aerin"));
            p.Take(new BossEnded(t.AddSeconds(120), 960, BossOutcome.Kill));
            // left after one boss; the next run of it: another group, you under a new id, the entry numbered 1 again
            var meB = Me with { Id = 41 };
            var kora = new GroupMember(42, "Kora", "Sorcerer", 300_000, 5_600, false);
            p.Take(new FeedGap(t.AddSeconds(300)));
            p.Take(new InstanceEntered(t.AddSeconds(310), ContentKind.Expedition, "Shattered Arkanis", 1));
            p.Take(new GroupChanged(t.AddSeconds(311), new[] { kora }));
            var first = kept.SingleOrDefault();
            Check(first is { ModeKind: ParserMode.Run, Outcome: "left", Partial: false } && first.End == t.AddSeconds(120) && first.Splits.Count == 1,
                $"same instance again, another group: a new run; the run before ends where it stood, no reconnect or gap of the new run's on it: {first?.Outcome}, ended {(first?.End - t)?.TotalSeconds} s, partial ({string.Join("; ", first?.PartialWhy ?? new())})");
            p.Take(new GroupChanged(t.AddSeconds(312), new[] { meB, kora }));
            p.Take(new BossEngaged(t.AddSeconds(360), "Talisra of the Void", 970));
            p.Take(new DamageDealt(t.AddSeconds(370), 41, 970, 30_000, "Swoly", true));
            p.Take(new DamageDealt(t.AddSeconds(371), 42, 970, 20_000, "Kora"));
            p.Take(new BossEnded(t.AddSeconds(400), 970, BossOutcome.Kill));
            p.Take(new InstanceLeft(t.AddSeconds(450)));
            var second = kept.Count == 2 ? kept[1] : null;
            Check(second is { Partial: false } && second.Start == t.AddSeconds(310) && second.Splits.Count == 1 && second.Roster.Count == 2
                && second.Roster.Single(f => f.You).Damage == 30_000 && second.Roster.Any(f => f.Name == "Kora") && !second.Roster.Any(f => f.Name == "Aerin"),
                $"...from its entry, with its own group and one row for you: {string.Join(", ", second?.Roster.Select(f => $"{f.Name} {f.Damage}") ?? Array.Empty<string>())}");
        }
        // a reconnect: the same group back (anyone of it), everyone under new ids: one run, one row per player, nothing lost
        {
            var (p, kept) = New();
            p.SetMode(ParserMode.Run, t);
            p.Take(new InstanceEntered(t, ContentKind.Expedition, "Shattered Arkanis", 5));
            p.Take(new GroupChanged(t.AddSeconds(1), new[] { Me, Ally }));
            p.Take(new BossEngaged(t.AddSeconds(60), "Talisra of the Void", 960));
            p.Take(new DamageDealt(t.AddSeconds(70), 1, 960, 40_000, "Swoly", true));
            p.Take(new DamageDealt(t.AddSeconds(71), 2, 960, 10_000, "Aerin"));
            p.Take(new BossEnded(t.AddSeconds(120), 960, BossOutcome.Kill));
            p.Take(new FeedGap(t.AddSeconds(200)));
            p.Take(new InstanceEntered(t.AddSeconds(230), ContentKind.Expedition, "Shattered Arkanis", 6));
            p.Take(new GroupChanged(t.AddSeconds(231), new[] { Ally with { Id = 52 } }));
            p.Take(new GroupChanged(t.AddSeconds(232), new[] { Me with { Id = 51 }, Ally with { Id = 52 } }));
            p.Take(new BossEngaged(t.AddSeconds(300), "Siliator of Deceit", 961));
            p.Take(new DamageDealt(t.AddSeconds(310), 51, 961, 20_000, "Swoly", true));
            p.Take(new DamageDealt(t.AddSeconds(311), 52, 961, 5_000, "Aerin"));
            p.Take(new BossEnded(t.AddSeconds(360), 961, BossOutcome.Kill));
            p.Take(new InstanceLeft(t.AddSeconds(400)));
            var run = kept.SingleOrDefault();
            Check(run != null && run.Splits.Count == 2 && run.Roster.Count == 2 && run.Roster.Single(f => f.You).Damage == 60_000 && run.Roster.Single(f => f.Name == "Aerin").Damage == 15_000
                && run.PartialWhy.Contains("the game reconnected during it") && run.Start == t,
                $"a reconnect, the same group under new ids: one run, one row each, every hit kept: {kept.Count} runs, {string.Join(", ", run?.Roster.Select(f => $"{f.Name} {f.Damage}") ?? Array.Empty<string>())}");
            // you alone under a new id, before any group list: still your row
            var (q, qk) = New();
            q.SetMode(ParserMode.Run, t);
            q.Take(new InstanceEntered(t, ContentKind.Expedition, "Shattered Arkanis", 5));
            q.Take(new BossEngaged(t.AddSeconds(10), "Talisra of the Void", 960));
            q.Take(new DamageDealt(t.AddSeconds(11), 1, 960, 1_000, "Swoly", true));
            q.Take(new DamageDealt(t.AddSeconds(12), 77, 960, 2_000, "Swoly", true));
            Check(q.Live is { } ql && ql.Roster.Count(f => f.You) == 1 && ql.Roster.Single(f => f.You).Damage == 3_000, $"...you under a new id are one row: {q.Live?.Roster.Count} rows");
        }
        // after the last boss died, the same instance again is a new run at once, whatever the group; the cleared run unmarked
        {
            var (p, kept) = New();
            p.SetMode(ParserMode.Run, t);
            p.Take(new InstanceEntered(t, ContentKind.Expedition, "Shattered Arkanis", 7));
            p.Take(new GroupChanged(t.AddSeconds(1), new[] { Me, Ally }));
            p.Take(new BossEngaged(t.AddSeconds(60), "Ruthilis of Pain", 963));
            p.Take(new DamageDealt(t.AddSeconds(70), 1, 963, 40_000, "Swoly", true));
            p.Take(new BossEnded(t.AddSeconds(120), 963, BossOutcome.Kill));
            p.Take(new FeedGap(t.AddSeconds(200)));
            Check(kept.SingleOrDefault() is { Outcome: "cleared", Partial: false } c1 && c1.End == t.AddSeconds(120),
                $"a gap after the last boss's kill ends the run there, cleared, nothing missed: {kept.FirstOrDefault()?.Outcome}, partial ({string.Join("; ", kept.FirstOrDefault()?.PartialWhy ?? new())})");
            p.Take(new InstanceEntered(t.AddSeconds(210), ContentKind.Expedition, "Shattered Arkanis", 7));
            p.Take(new GroupChanged(t.AddSeconds(211), new[] { Me, Ally }));   // the same group runs it again
            p.Take(new BossEngaged(t.AddSeconds(260), "Talisra of the Void", 964));
            p.Take(new BossEnded(t.AddSeconds(300), 964, BossOutcome.Kill));
            p.Take(new InstanceLeft(t.AddSeconds(320)));
            Check(kept.Count == 2 && kept[1].Start == t.AddSeconds(210) && kept[1].Splits.Count == 1 && !kept[1].Partial,
                $"...and running it again is a new run from its entry, the same group too: {kept.Count} runs");
            // no gap between: the entry itself ends it
            var (q, qk) = New();
            q.SetMode(ParserMode.Run, t);
            q.Take(new InstanceEntered(t, ContentKind.Expedition, "Shattered Arkanis", 7));
            q.Take(new BossEngaged(t.AddSeconds(60), "Ruthilis of Pain", 963));
            q.Take(new BossEnded(t.AddSeconds(120), 963, BossOutcome.Kill));
            q.Take(new InstanceEntered(t.AddSeconds(300), ContentKind.Expedition, "Shattered Arkanis", 8));
            Check(qk.SingleOrDefault() is { Outcome: "cleared", Partial: false } && q.Live is { ModeKind: ParserMode.Run } nl && nl.Start == t.AddSeconds(300),
                "...with no gap before, the entry itself ends it and starts the next");
            // quiet for half an hour after the last boss: over, not partial
            var (r, rk) = New();
            r.SetMode(ParserMode.Run, t);
            r.Take(new InstanceEntered(t, ContentKind.Expedition, "Shattered Arkanis", 7));
            r.Take(new BossEngaged(t.AddSeconds(60), "Ruthilis of Pain", 963));
            r.Take(new BossEnded(t.AddSeconds(120), 963, BossOutcome.Kill));
            r.Tick(t.AddHours(1));
            Check(rk.SingleOrDefault() is { Outcome: "cleared", Partial: false }, $"...and quiet after the last boss is the run over, never partial ({string.Join("; ", rk.FirstOrDefault()?.PartialWhy ?? new())})");
        }
        // on your own: a boss already down here pulled again is a new run; another boss, the same copy
        {
            var (p, kept) = New();
            p.SetMode(ParserMode.Run, t);
            p.Take(new InstanceEntered(t, ContentKind.Expedition, "Shattered Arkanis", 9));
            p.Take(new GroupChanged(t.AddSeconds(1), new[] { Me }));
            p.Take(new BossEngaged(t.AddSeconds(60), "Talisra of the Void", 960));
            p.Take(new DamageDealt(t.AddSeconds(70), 1, 960, 40_000, "Swoly", true));
            p.Take(new BossEnded(t.AddSeconds(120), 960, BossOutcome.Kill));
            p.Take(new InstanceEntered(t.AddSeconds(300), ContentKind.Expedition, "Shattered Arkanis", 10));
            p.Take(new GroupChanged(t.AddSeconds(301), new[] { Me with { Id = 61 } }));
            Check(kept.Count == 0, "on your own, back in the same instance: nothing decided by the group");
            p.Take(new BossEngaged(t.AddSeconds(360), "Talisra of the Void", 970));
            Check(kept.SingleOrDefault() is { Outcome: "left", Partial: false } s1 && s1.End == t.AddSeconds(120) && p.Live is { ModeKind: ParserMode.Run } s2 && s2.Start == t.AddSeconds(300),
                $"...a boss already down pulled again: a new run from the entry, the one before ended where it stood: {kept.Count} runs");
            var (q, qk) = New();
            q.SetMode(ParserMode.Run, t);
            q.Take(new InstanceEntered(t, ContentKind.Expedition, "Shattered Arkanis", 9));
            q.Take(new GroupChanged(t.AddSeconds(1), new[] { Me }));
            q.Take(new BossEngaged(t.AddSeconds(60), "Talisra of the Void", 960));
            q.Take(new BossEnded(t.AddSeconds(120), 960, BossOutcome.Kill));
            q.Take(new InstanceEntered(t.AddSeconds(300), ContentKind.Expedition, "Shattered Arkanis", 10));
            q.Take(new BossEngaged(t.AddSeconds(360), "Siliator of Deceit", 961));
            q.Take(new BossEnded(t.AddSeconds(400), 961, BossOutcome.Kill));
            q.Take(new InstanceLeft(t.AddSeconds(420)));
            Check(qk.SingleOrDefault() is { } one && one.Splits.Count == 2 && one.Start == t, $"...the next boss pulled: the same run, reconnected ({qk.Count} runs)");
        }
        // Always on: running the same instance again starts the meter from 0; a reconnect carries it on
        {
            var (p, _) = New();
            p.Take(new InstanceEntered(t, ContentKind.Expedition, "Shattered Arkanis", 11));
            p.Take(new GroupChanged(t.AddSeconds(1), new[] { Me, Ally }));
            p.Take(new DamageDealt(t.AddSeconds(10), 1, 6000, 1_000, "Swoly", true));
            p.Take(new InstanceEntered(t.AddSeconds(100), ContentKind.Expedition, "Shattered Arkanis", 12));
            p.Take(new GroupChanged(t.AddSeconds(101), new[] { Ally with { Id = 52 } }));
            p.Take(new DamageDealt(t.AddSeconds(110), 51, 6000, 1_000, "Swoly", true));
            Check(p.AlwaysLive is { } a1 && a1.Roster.Sum(f => f.Damage) == 2_000, $"Always on: a reconnect into the same copy carries the meter on ({p.AlwaysLive?.Roster.Sum(f => f.Damage)})");
            p.Take(new InstanceEntered(t.AddSeconds(200), ContentKind.Expedition, "Shattered Arkanis", 13));
            p.Take(new GroupChanged(t.AddSeconds(201), new[] { new GroupMember(42, "Kora", "Sorcerer", 300_000, 5_600, false) }));
            Check(p.AlwaysLive == null, "...running it again with another group starts it from 0");
            p.Take(new DamageDealt(t.AddSeconds(210), 81, 6000, 500, "Swoly", true));
            Check(p.AlwaysLive is { } a2 && a2.Roster.Sum(f => f.Damage) == 500 && a2.Start == t.AddSeconds(210), "...from the first hit there");
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
            Check(run!.Splits.Select(x => x.Outcome).SequenceEqual(new[] { "kill", "wipe", "kill", "kill" }) && run.Splits[3].Final && run.Splits[0].Seconds == 50,
                "...its splits in order with their outcomes and times (from the first damage), the last the final boss");
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
            p.Tick(t.AddSeconds(270));
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
            var dot = me.Abilities.Single(a => a.Skill == 0);
            Check(judgment is { Damage: 655_000, Hits: 24 } && Math.Abs(judgment.Average!.Value - 655_000 / 24.0) < 0.01 && dot is { Hits: 0, Average: null }
                && Abilities.Name(12240000) == "Judgment" && Abilities.Name(0) == "Damage over time",
                $"damage by ability: each skill's total, hits and average a hit, named from the skill list ({Abilities.Count} skills)");
            // Steven, 2026-11-14: "wtf is 'Ability 16050000'": the abilities his saved fights showed as numbers, by name
            Check(Abilities.Name(16050000) == "Ashy Call" && Abilities.Name(12060000) == "Punishing Strike" && Abilities.Name(18050000) == "Bursting Blow"
                && Abilities.Name(18400000) == "Piercing Strike" && Abilities.Name(100051) == "Ancient Spirit: Basic Attack" && Abilities.Name(16001101) == "Fire Spirit: Flame Explosion"
                && Abilities.Name(3000117) == "Theostone: Traufnir's Courage" && Abilities.ClassOf(12060000) == "templar" && Abilities.ClassOf(100051) == "spiritmaster",
                "abilities his saved fights showed as numbers: Ashy Call, Punishing Strike, Bursting Blow, Piercing Strike, a spirit's attack, a spirit command, a Theostone");
            // a Theostone from the game's item dictionary (Boreas's Encouragement, 3000121, in his fights) and a spirit's attack at another rank, with their icons
            Check(Abilities.Name(3000121) == "Theostone: Boreas's Encouragement" && Abilities.Name(100012) == "Fire Spirit: Basic Attack" && Abilities.ClassOf(100012) == "spiritmaster"
                && Ui.SkillArt.Image(3000121) != null && Ui.SkillArt.Image(100012) != null && Abilities.Canon(100012) == Abilities.Canon(100014),
                "a Theostone from the item dictionary and a spirit's attack at another rank: named, with icons, one row with its spirit's other ranks");
            // the same skill under another id (its Korean name is a named id's): spirit commands at each form, a Brawler's Focused Block; one the sources disagree on stays its class's
            Check(Abilities.Name(16001604) == "Fire Spirit: Summon Meteor" && Abilities.Name(16001617) == "Ancient Spirit: Magnetic Storm" && Abilities.Name(19350000) == "Focused Block"
                && Abilities.Name(16001612, "Spiritmaster") == "Spiritmaster skill" && Abilities.Canon(16001604) == Abilities.Canon(16001301) && Ui.SkillArt.Image(16001604) == Ui.SkillArt.Image(16001301),
                "the same skill under another id: Fire Spirit: Summon Meteor at another form (one row with it, its icon), Ancient Spirit: Magnetic Storm, a Brawler's Focused Block; a disputed one stays Spiritmaster skill");
            // a class skill's rank or variant (the last four digits) is its base's: name and icon
            Check(Abilities.Resolve(12060003) == 12060000 && Abilities.Name(12060003) == "Punishing Strike" && Abilities.Name(16100005) == "Summon: Fire Spirit"
                && Abilities.Resolve(16001102) == 16001102 && Abilities.Resolve(100051) == 100051 && Abilities.Resolve(99_999_999) == null && Ui.SkillArt.Image(12060003) == Ui.SkillArt.Image(12060000) && Ui.SkillArt.Image(12060003) != null,
                "variants: 12060003 is Punishing Strike's (its name and icon), a spirit command and a spirit's attack are their own");
            // the ids of one ability are one row: a spirit's attack at two ranks, a spirit command at two
            var merged = Abilities.Merged(new[] { new AbilityShare { Skill = 100014, Damage = 700, Hits = 3 }, new AbilityShare { Skill = 100018, Damage = 500, Hits = 2 },
                new AbilityShare { Skill = 16001101, Damage = 900, Hits = 1 }, new AbilityShare { Skill = 16001104, Damage = 100, Hits = 1 }, new AbilityShare { Skill = 0, Damage = 50, Hits = 4 } });
            Check(merged.Count == 3 && merged[0] is { Damage: 1_200, Hits: 5 } && Abilities.Name(merged[0].Skill) == "Fire Spirit: Basic Attack" && merged[0].Skill == Abilities.Canon(100018) && merged[1] is { Skill: 16001101, Damage: 1_000, Hits: 2 } && merged[2].Skill == 0,
                $"one row per ability: Fire Spirit: Basic Attack's two ids as one ({string.Join(", ", merged.Select(a => $"{Abilities.Name(a.Skill)} {a.Damage}"))})");
            // an id the list doesn't name: never a number; its class from the id's family or the member, else "Unknown skill"; noted once
            var notes = new List<string>();
            using (CrashLog.CaptureNotes(notes))
            {
                var fallbacks = new[] { Abilities.Name(16000000), Abilities.Name(16000000, "Spiritmaster"), Abilities.Name(12999999), Abilities.Name(2_999_999, "Chanter"), Abilities.Name(2_999_998), Abilities.Name(2_999_997, "Elementalist") };
                Check(fallbacks.SequenceEqual(new[] { "Spiritmaster skill", "Spiritmaster skill", "Templar skill", "Chanter skill", "Unknown skill", "Spiritmaster skill" }) && fallbacks.All(n => !n.Any(char.IsDigit))
                    && notes.Count(n => n.StartsWith("ability unnamed: 16000000 (Spiritmaster)")) == 1 && notes.Count(n => n.StartsWith("ability unnamed: 2999998 (no class)")) == 1 && notes.Count == 5,
                    $"an unnamed ability: {string.Join(" / ", fallbacks)}, each id noted once ({notes.Count} notes)");
            }
            // damage over time by skill: minus the effect's id (the skill's id x 100 + n), named for its skill with " (over time)", one row per skill
            Check(Abilities.Name(-1206000003) == "Punishing Strike (over time)" && Abilities.Name(-1206000301) == "Punishing Strike (over time)" && Abilities.Name(-1605000001, "Spiritmaster") == "Ashy Call (over time)"
                && Abilities.Canon(-1206000003) == -1206000000 && Abilities.Canon(-1206000301) == -1206000000 && Abilities.ClassOf(-1206000003) == "templar"
                && Ui.SkillArt.Image(-1206000003) != null && Ui.SkillArt.Image(-1206000003) == Ui.SkillArt.Image(12060000) && Abilities.Name(0) == "Damage over time",
                "damage over time: -1206000003 and a rank's -1206000301 are Punishing Strike (over time) with its icon; old records' 0 keeps its row");
            // the other form Steven's Templar showed (2026-11-15): a class skill's id short of its last digit, x 100 + n
            Check(Abilities.SkillOfEffect(122400001) == 12240000 && Abilities.Name(-122400001) == "Judgment (over time)" && Abilities.SkillOfEffect(120700201) == 12070020
                && Abilities.Name(Abilities.SkillOfEffect(120700201)) == "Doom Shield" && Abilities.SkillOfEffect(1206000003) == 12060000 && Abilities.SkillOfEffect(1702000101) == 17020001
                && Abilities.SkillOfEffect(300011701) == 3000117 && Abilities.Canon(-122400001) == Abilities.Canon(-1224000003) && Ui.SkillArt.Image(-122400001) == Ui.SkillArt.Image(12240000),
                "an effect's skill: 122400001 is Judgment's and 120700201 Doom Shield's (an id short of its last digit), 1206000003 Punishing Strike's, a Theostone's as it is; one row either way");
            var dots = Abilities.Merged(new[] { new AbilityShare { Skill = -1206000003, Damage = 300, Hits = 3 }, new AbilityShare { Skill = -1206000301, Damage = 200, Hits = 2 },
                new AbilityShare { Skill = 12060000, Damage = 1_000, Hits = 4 } });
            Check(dots.Count == 2 && dots[0].Skill == 12060000 && dots[1] is { Skill: -1206000000, Damage: 500, Hits: 5 } && Abilities.Name(dots[1].Skill) == "Punishing Strike (over time)",
                $"...one row per skill's damage over time, beside the skill's own ({string.Join(", ", dots.Select(a => $"{Abilities.Name(a.Skill)} {a.Damage}"))})");
            var dotNotes = new List<string>();
            using (CrashLog.CaptureNotes(dotNotes))
            {
                var unknownDots = new[] { Abilities.Name(-1298888801), Abilities.Name(-299999901, "Chanter"), Abilities.Name(-299999801), Abilities.Name(-5), Abilities.Name(-5, "Cleric"), Abilities.Name(int.MinValue) };
                Check(unknownDots.SequenceEqual(new[] { "Templar skill (over time)", "Chanter skill (over time)", "Unknown skill (over time)", "Unknown skill (over time)", "Cleric skill (over time)", "Unknown skill (over time)" })
                    && unknownDots.All(n => !n.Any(char.IsDigit)) && dotNotes.Any(n => n.StartsWith("ability unnamed: 12988888 (Templar)")) && Ui.SkillArt.Image(-1298888801) == null,
                    $"...over time from a skill the list doesn't name: {string.Join(" / ", unknownDots)}, never a number, its skill noted ({dotNotes.Count} notes)");
            }
            {
                // through the Parser and a saved record: the negative id is kept as it came, and named on the way out
                var (pd, keptDot) = New();
                pd.Take(new InstanceEntered(t, ContentKind.Expedition, "Ferocious Horn Den", 90));
                pd.Take(new GroupChanged(t, new[] { new GroupMember(1, "Swoly", "Templar", 0, 0, true) }));
                pd.Take(new BossEngaged(t.AddSeconds(5), "Watchdog Kwapo", 900));
                pd.Take(new DamageDealt(t.AddSeconds(6), 1, 900, 40_000, Skill: 12060000, Hits: 2));
                pd.Take(new DamageDealt(t.AddSeconds(7), 1, 900, 9_000, Skill: -1206000003, Hits: 3));
                pd.Take(new DamageDealt(t.AddSeconds(8), 1, 900, 1_000, Skill: -1206000301, Hits: 1));
                pd.Take(new BossEnded(t.AddSeconds(20), 900, BossOutcome.Kill));
                var saved = System.Text.Json.JsonSerializer.Deserialize<Encounter>(System.Text.Json.JsonSerializer.Serialize(keptDot.Single()))!;
                var rows = Abilities.Merged(saved.Roster.Single(f => f.Id == 1).Abilities);
                Check(rows.Count == 2 && rows[1] is { Damage: 10_000, Hits: 4 } && rows.Select(a => Abilities.Name(a.Skill, "Templar")).SequenceEqual(new[] { "Punishing Strike", "Punishing Strike (over time)" })
                    && saved.Roster.Single(f => f.Id == 1).Abilities.Any(a => a.Skill == -1206000003),
                    $"...through a pull and its saved record: {string.Join(", ", rows.Select(a => $"{Abilities.Name(a.Skill)} {a.Damage} in {a.Hits}"))}");
            }
            // each ability the skill list names that has art carries its icon in the exe; an unnamed one and damage over time have none (the class's emblem / a glyph stands in)
            Check(Ui.SkillArt.Count >= 350 && Ui.SkillArt.Image(12240000) != null && Ui.SkillArt.Image(16050000) != null && Ui.SkillArt.Image(3000117) != null && Ui.SkillArt.Image(16000000) == null && Ui.SkillArt.Image(0) == null,
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
            Check(keptRun.Count == 1 && keptRun[0].Splits.Single() is { Boss: "Totem room", Wave: true, Outcome: "kill", Seconds: 26 },
                "in Full run it is a split of its own, its time (from its first damage) and its result");
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

        // a fight with no damage known and nothing heard for ten minutes is given up at the last thing heard
        {
            var (p, kept) = New();
            p.Take(new InstanceEntered(t, ContentKind.Expedition, "Shattered Arkanis", 82));
            p.Take(new BossEngaged(t.AddSeconds(5), "Talisra of the Void", 930));
            p.Take(new UnitDied(t.AddSeconds(65), 4400, "Void Spawn", false));
            p.Tick(t.AddMinutes(5));
            Check(p.Live != null, "a quiet fight with no damage known is kept going for a while");
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
            p.Take(new DamageDealt(t.AddSeconds(140), 1, 941, 5_000));
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
            Check(kept2.Count == 1 && kept2[0].Duration(t).TotalSeconds == 70 && again.All().Count == 2, "...and ends as the one fight it was, from its first damage");
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

        // the honest pass on Steven's saved fights (tracker/tests/parser: his encounters.jsonl of 2026-11-14, names as they
        // were): read as kept, and played back through this build's rules as the events that made them
        {
            var vak = Saved("vakron-sky-island"); var krao = Saved("krao-cave");
            Check(vak.Count == 6 && krao.Count == 4, $"Steven's saved fights are found: {vak.Count} at Vakron Sky Island, {krao.Count} at Krao Cave");
            if (vak.Count == 6 && krao.Count == 4)
            {
                bool bossOnly0 = Encounter.BossOnly, active0 = Encounter.ActiveBasis;
                Encounter.BossOnly = true; Encounter.ActiveBasis = false;
                string S(double x) => Ui.Amounts.Short(x);

                // 1. no Other row anywhere: an old record's Other becomes its uncredited damage, no member; the coverage line says how much is credited
                var tiere = vak.Single(e => e.Id == "3b91dbb05626");
                var tEnd = tiere.End!.Value;
                Check(!tiere.Roster.Any(f => f.Other) && tiere.Members.Count() == 3 && tiere.Uncredited == 3_087_359 && tiere.Credited == 2_132_641 && Encounter.Pct(tiere.Coverage!.Value) == "40.9%",
                    $"1. a saved Tiere pull's Other ({S(tiere.Uncredited)}) is no row and no member: {Encounter.Pct(tiere.Coverage ?? 0)} of the boss's HP credited");
                Check(Math.Abs(tiere.Members.Sum(f => tiere.Share(f)) - 1) < 1e-9 && Math.Abs(tiere.GroupDps(tEnd) - 2_132_641 / tiere.Duration(tEnd).TotalSeconds) < 0.01,
                    $"...its shares add up to 100% and its group DPS is the credited damage over the pull ({tiere.GroupDpsText(tEnd)})");
                var (p1, k1) = New();
                foreach (var ev in EventsOf(vak.Single(e => e.Id == "3c9d770857ce"), 1)) p1.Take(ev);
                var r1 = k1.Single();
                Check(!r1.Roster.Any(f => f.Other) && r1.Members.Count() == 5 && r1.Uncredited == 191_581 && r1.UncreditedBoss == 191_581 && r1.Credited == 5_028_419
                    && Encounter.Pct(r1.Coverage!.Value) == "96.3%" && r1.Credited + r1.UncreditedBoss == r1.BossDamage,
                    $"...played back, the first Tiere pull: 5 members, no Other, {S(r1.Uncredited)} uncredited, {Encounter.Pct(r1.Coverage ?? 0)} of the boss's HP credited");

                // 3. boss and adds apart: Enhanced Harcon with a stretch of damage to an add; the figures are the boss's by default
                var harcon = krao.Single(e => e.Id == "1d7c8530ab6a");
                var piggie = harcon.Members.Single(f => f.Name == "Piggie"); var swoly = harcon.Members.Single(f => f.You);
                var adds = new List<DamageDealt>();
                for (int i = 0; i < 10; i++)
                {
                    adds.Add(new DamageDealt(harcon.Start.AddSeconds(10 + i * 9), piggie.Id, 9001, 40_000, Skill: 16000000, Hits: 2));
                    adds.Add(new DamageDealt(harcon.Start.AddSeconds(12 + i * 9), swoly.Id, 9001, 25_000, SourceIsYou: true, Skill: 12240000, Hits: 1));
                }
                var (p3, k3) = New();
                foreach (var ev in EventsOf(harcon, 3, extra: adds)) p3.Take(ev);
                var h3 = k3.Single(); var e3 = h3.End!.Value; double d3 = h3.Duration(e3).TotalSeconds;
                var pig3 = h3.Members.Single(f => f.Id == piggie.Id);
                bool bossFigs = h3.HasAdds && h3.BossView && h3.Members.All(f => h3.Figure(f) == harcon.Members.Single(o => o.Id == f.Id).Damage)
                    && h3.BossDamage == 18_576_000 && h3.Credited + h3.UncreditedBoss == h3.BossDamage;
                double bossGroup = h3.GroupDps(e3), bossShare = h3.Share(pig3), bossTop = h3.TopMeasure();
                Encounter.BossOnly = false;
                double allGroup = h3.GroupDps(e3), allShare = h3.Share(pig3);
                bool allFigs = !h3.BossView && pig3.Damage == piggie.Damage + 400_000 && h3.Credited == 18_576_000 + 650_000 && Math.Abs(h3.Members.Sum(f => h3.Share(f)) - 1) < 1e-9;
                Encounter.BossOnly = true;
                Check(bossFigs && Math.Abs(bossGroup - 18_576_000 / d3) < 0.01 && Math.Abs(bossShare - piggie.Damage / 18_576_000.0) < 1e-9 && bossTop == h3.Members.Max(f => f.BossDamage),
                    $"3. boss figures by default: each member's figure is their saved damage to Harcon, the rows add up to the boss's HP lost, group DPS {S(bossGroup)} and Piggie's {Encounter.Pct(bossShare)} of it");
                Check(allFigs && Math.Abs(allGroup - (18_576_000 + 650_000) / d3) < 0.01 && allShare > bossShare,
                    $"...All targets counts the adds too: group DPS {S(allGroup)}, Piggie's share {Encounter.Pct(allShare)}");
                Check(pig3.Abilities.Single(a => a.Skill == 16000000) is { } ps && ps.Damage - ps.BossDamage == 400_000 && ps.Hits - ps.BossHits == 20,
                    "...and each ability keeps its damage to the boss apart from its damage to adds");

                // 4. pull timing: Ultimate Berk's wipe, the pull said 4 s before the first damage, no end said, the boss back to full 20 s after the last hit
                var berk = krao.Single(e => e.Id == "28c792c2f924");
                var firstHit = berk.Members.Min(f => f.FirstHit!.Value); var lastHit = berk.Members.Max(f => f.LastHit!.Value);
                long berkMax = Math.Max(berk.BossMaxHp, berk.BossDamage);
                var (p4, k4) = New();
                foreach (var ev in EventsOf(berk, 4, engageShift: -4, end: false)) p4.Take(ev);
                p4.Take(new BossHealth(lastHit.AddSeconds(20), berk.BossId, berkMax, berkMax));
                Check(k4.Count == 1 && k4[0] is { Outcome: "wipe" } w4 && w4.Start == firstHit && w4.End == lastHit && p4.Live == null,
                    $"4. a wipe with no end said ends when the boss resets, at the last damage ({k4.FirstOrDefault()?.End:HH:mm:ss.f}), and starts at the first ({k4.FirstOrDefault()?.Start:HH:mm:ss.f}), not the pull 4 s before");
                var (q4, kq4) = New();
                foreach (var ev in EventsOf(berk, 5, end: false)) q4.Take(ev);
                q4.Tick(lastHit.AddSeconds(30));
                bool still = q4.Live != null;
                q4.Tick(lastHit.AddSeconds(BossParser.WipeQuietSeconds + 1));
                Check(still && kq4.Count == 1 && kq4[0] is { Outcome: "wipe" } w5 && w5.End == lastHit,
                    $"...one with no reset either ends {BossParser.WipeQuietSeconds:0} s on, at the last damage ({kq4.FirstOrDefault()?.End:HH:mm:ss.f}), not after {BossParser.StaleMinutes:0} minutes");
                q4.Take(new DamageDealt(lastHit.AddSeconds(90), swoly.Id, berk.BossId, 10_000, SourceIsYou: true, Skill: 12240000, Hits: 1));
                Check(q4.Live is { Boss: "Ultimate Berk" } again4 && again4.Start == lastHit.AddSeconds(90), "...and hitting that boss again opens a new pull");
                var (r4, kr4) = New();
                foreach (var ev in EventsOf(berk, 6, engageShift: 3)) r4.Take(ev);
                Check(kr4.Single() is { Outcome: "wipe" } w6 && w6.Start == firstHit && w6.Credited == berk.Credited && w6.End == lastHit,
                    $"...a pull said 3 s after the first damage starts at that damage and keeps it ({S(kr4.Single().Credited)} of {S(berk.Credited)}); its wipe ends at the last damage");
                var gad = krao.Single(e => e.Id == "46ce190379c9");
                var (s4, ks4) = New();
                foreach (var ev in EventsOf(gad, 7, end: false)) s4.Take(ev);
                s4.Take(new InstanceLeft(gad.End!.Value.AddSeconds(40)));
                Check(ks4.Single() is { Outcome: "left" } l4 && l4.End == gad.Members.Single(f => f.You).LastHit,
                    $"...a pull you left ends at your last damage ({ks4.Single().End:HH:mm:ss.f}), not when you left");

                // 5. late joiners and leavers, flagged with damage already dealt; each member's active time and idle share
                var jinners = harcon.Members.Single(f => f.Name == "Jinners");
                var joined = jinners.FirstHit!.Value.AddSeconds(-0.5);
                var (p5, k5) = New();
                foreach (var ev in EventsOf(harcon, 8, lateId: jinners.Id, lateAt: joined)) p5.Take(ev);
                var h5 = k5.Single(); var j5 = h5.Members.Single(f => f.Id == jinners.Id);
                Check(j5 is { Late: true, Gone: false } && j5.JoinedAt == joined && h5.Members.Where(f => f.Id != jinners.Id).All(f => !f.Late),
                    $"5. Jinners, on the group list {(joined - h5.Start).TotalSeconds:0.0} s into Harcon with damage already dealt, is marked late; the others aren't");
                Check(Math.Abs(h5.PresentSeconds(j5, h5.End!.Value) - (h5.End.Value - joined).TotalSeconds) < 0.01 && h5.IdleShare(j5, h5.End.Value) is { } ji && ji < 0.02,
                    $"...their time in the group is from joining ({h5.PresentSeconds(j5, h5.End.Value):0} s), idle {Encounter.Pct(h5.IdleShare(j5, h5.End.Value) ?? 0)} of it");
                var tiere2 = vak.Single(e => e.Id == "7c5875ccae66");
                var arconai = tiere2.Members.Single(f => f.Name == "Arconai");
                var gone = arconai.LastHit!.Value.AddSeconds(8);
                var (p6, k6) = New();
                foreach (var ev in EventsOf(tiere2, 9, leaveId: arconai.Id, leaveAt: gone)) p6.Take(ev);
                var t6 = k6.Single(); var a6 = t6.Members.Single(f => f.Id == arconai.Id); var end6 = t6.End!.Value;
                double active = (arconai.LastHit.Value - arconai.FirstHit!.Value).TotalSeconds, present = (gone - t6.Start).TotalSeconds;
                Check(a6 is { Gone: true } && a6.LeftAt == gone && Math.Abs(a6.ActiveSeconds - active) < 0.01 && Math.Abs(t6.IdleShare(a6, end6)!.Value - (1 - active / present)) < 1e-6,
                    $"...Arconai, gone from the list after their damage, is marked left; active {a6.ActiveSeconds:0} s, idle {Encounter.Pct(t6.IdleShare(a6, end6) ?? 0)} of their {present:0} s");
                double fightDps = t6.Dps(a6, end6);
                Encounter.ActiveBasis = true;
                double activeDps = t6.Dps(a6, end6);
                Encounter.ActiveBasis = false;
                Check(Math.Abs(fightDps - a6.BossDamage / t6.Duration(end6).TotalSeconds) < 0.01 && Math.Abs(activeDps - a6.BossDamage / active) < 0.01,
                    $"...both DPS modes: {S(fightDps)} over the fight, {S(activeDps)} over their active time");

                // 6. short fights: a figure from the first hit (Steven, 2026-11-15: "the dps meter takes like 2-3 seconds after
                // first hit to start working ... too long"), never a hit over a split second: under 1 s it is over 1 s
                var (p7, _) = New();
                var at7 = harcon.Start.AddSeconds(2.5);
                foreach (var ev in EventsOf(harcon, 10, end: false).Where(ev => ev.At <= at7)) p7.Take(ev);
                var l7 = p7.LiveAt(at7)!;
                Check(l7.HasDamage && l7.Credited > 0 && l7.GroupDpsOf(at7) is { } g7 && Math.Abs(g7 - l7.Credited / (at7 - l7.Start).TotalSeconds) < 0.01
                    && l7.Members.Where(f => f.Damage > 0).All(f => l7.DpsText(f, at7) != "–"),
                    $"6. Harcon's first 2.5 s ({S(l7.Credited)} dealt): group and member DPS show a figure already ({l7.GroupDpsText(at7)}), the damage over the 2.5 s");
                var early = l7.Start.AddSeconds(0.3);
                double ge = l7.GroupDps(early);
                Check(Math.Abs(ge - l7.Credited / Encounter.MinDpsSeconds) < 0.01,
                    $"...0.3 s in, the damage so far over 1 s ({Ui.Amounts.Short(ge)}), not over 0.3 s");
                var (p8, _) = New();
                var at8 = harcon.Start.AddSeconds(4);
                foreach (var ev in EventsOf(harcon, 11, end: false).Where(ev => ev.At <= at8)) p8.Take(ev);
                var l8 = p8.LiveAt(at8)!;
                var once = l8.Members.First(f => f.Damage > 0 && f.ActiveSeconds < Encounter.MinDpsSeconds);
                Encounter.ActiveBasis = true;
                string onceText = l8.DpsText(once, at8);
                Encounter.ActiveBasis = false;
                Check(Math.Abs(l8.GroupDps(at8) - l8.Credited / (at8 - l8.Start).TotalSeconds) < 0.01 && onceText == Ui.Amounts.Short(l8.Figure(once) / Encounter.MinDpsSeconds),
                    $"...at 4 s the group DPS is the damage over the 4 s exactly ({l8.GroupDpsText(at8)}); over active time, {once.Name}'s {once.ActiveSeconds:0.0} s reads their damage over 1 s ({onceText}), never over a split second");

                // 8. hit counts: a share with no count adds no hit; the average is over the counted hits; no count at all is "–"
                var pigTop = piggie.Abilities.Where(a => a.Skill != 0).OrderByDescending(a => a.Damage).First();
                var uncounted = new[] { new DamageDealt(harcon.Start.AddSeconds(30), piggie.Id, 9002, 50_000, Skill: pigTop.Skill, Hits: 0) };
                var (p9, k9) = New();
                foreach (var ev in EventsOf(harcon, 12, extra: uncounted)) p9.Take(ev);
                var h9 = k9.Single(); var top9 = h9.Members.Single(f => f.Id == piggie.Id).Abilities.Single(a => a.Skill == pigTop.Skill);
                var dots = h9.Members.SelectMany(f => f.Abilities).Where(a => a.Skill == 0).ToList();
                Check(top9.Hits == pigTop.Hits && top9.Uncounted == 50_000 && Math.Abs(top9.Average!.Value - pigTop.Damage / (double)pigTop.Hits) < 0.01,
                    $"8. Piggie's {Abilities.Name(top9.Skill)}: {top9.Hits} counted hits, the 50K that came with no count adds no hit and stays out of the average ({S(top9.Average ?? 0)})");
                Check(dots.Count == 5 && dots.All(a => a is { Hits: 0, Average: null }) && Abilities.Name(0) == "Damage over time",
                    "...each member's damage over time, with no count, has 0 hits and no average (shown –), not a made-up 1 a share");
                Encounter.BossOnly = bossOnly0; Encounter.ActiveBasis = active0;
            }
        }

        // 2. damage over time by skill (a negative id, Parser/Abilities.cs) is a row of its own beside the skill's, one row however
        // many of its effects ticked, and keeps its boss damage and hits through the inspect view's rows; a share with no source
        // gives its reason in the decoder's damage row (Outcome), shown on the coverage line's hover
        {
            var (p, _) = New();
            p.Take(new InstanceEntered(t, ContentKind.Expedition, "Shattered Arkanis", 860));
            p.Take(new GroupChanged(t, new[] { Me }));
            p.Take(new BossEngaged(t.AddSeconds(1), "Talisra of the Void", 861));
            p.Take(new DamageDealt(t.AddSeconds(1), 1, 861, 20_000, Skill: 12060000, Hits: 2));
            p.Take(new DamageDealt(t.AddSeconds(2), 1, 861, 6_000, Skill: -1206000003, Hits: 3));
            p.Take(new DamageDealt(t.AddSeconds(3), 1, 861, 1_000, Skill: -1206000301, Hits: 1));
            p.Take(new DamageDealt(t.AddSeconds(4), 1, 4444, 2_000, Skill: -1206000003, Hits: 1));   // an add
            var l = p.Live!; var rows = l.AbilityRows(l.You!);
            Check(rows.Count == 2 && rows.Select(a => Abilities.Name(a.Skill, "Templar")).SequenceEqual(new[] { "Punishing Strike", "Punishing Strike (over time)" })
                && rows[1] is { BossDamage: 7_000, BossHits: 4, Damage: 9_000, Hits: 5 } && l.Figure(rows[1]) == 7_000,
                $"2. damage over time is its skill's own row ({string.Join(", ", rows.Select(a => $"{Abilities.Name(a.Skill)} {l.Figure(a)}"))}), boss damage apart");
            var none = new BossMapper().Map(new BossRow(t, 8, 0, "", 1, "", 861, 3, 0, "", "", false, 0, 0, 900, 0, 0));
            Check(none is DamageDealt { SourceId: 0, Reason: 3 } && Encounter.WhyWords(3) == "a moment the tracker missed" && Encounter.WhyWords(1) == Encounter.WhyWords(2) && Encounter.WhyWords(7) == "an attacker the tracker couldn't place" && Encounter.WhyWords(99) == "another reason",
                "...the decoder's damage row with no source gives its reason in Outcome (shown on the coverage line's hover)");
        }

        // your own death comes twice (the group's death row and the kill tally): one death, whichever comes first
        {
            var (p, kept) = New();
            p.Take(new InstanceEntered(t, ContentKind.Expedition, "Shattered Arkanis", 880));
            p.Take(new GroupChanged(t, new[] { Me, Ally }));
            p.Take(new BossEngaged(t.AddSeconds(1), "Talisra of the Void", 881));
            p.Take(new UnitDied(t.AddSeconds(10), 1, "Swoly", false));
            p.Kill(t.AddSeconds(10.4), player: false, death: true, "");
            p.Kill(t.AddSeconds(30), player: false, death: true, "");
            p.Take(new UnitDied(t.AddSeconds(30.6), 1, "Swoly", false));
            p.Take(new UnitDied(t.AddSeconds(40), 2, "Aerin", false));
            p.Take(new BossEnded(t.AddSeconds(50), 881, BossOutcome.Wipe));
            var e = kept.Single();
            Check(e.Members.Single(f => f.You).Deaths == 2 && e.Members.Single(f => f.Id == 2).Deaths == 1,
                $"deaths: your two deaths, each said twice, count 2 ({e.Members.Single(f => f.You).Deaths}); a member's one counts 1");
        }

        // 7. serious defaults (the honest pass): deaths, list filters and explanations on hover on; kills near you off; boss
        // figures over the fight. A parser.json from before (Steven's as it was) moves to them once; one whose player turned an
        // extra on keeps their choices; after the move, an off stays off
        {
            var fresh = new ParserPrefs();
            var steven = ParserPrefs.FromText("{\"Overlay\":true,\"ClickThrough\":false,\"Left\":null,\"Top\":null,\"Hotkey\":\"Off\",\"Mode\":\"boss\",\"ListMode\":\"boss\",\"Details\":true,\"DetailsSettled\":true,\"Separate\":false,\"DpsActive\":false,\"FarmDps\":true,\"Width\":330,\"KillsNear\":false,\"Filters\":false,\"Hovers\":false,\"KindFilter\":\"\",\"BossFilter\":\"\"}");
            var chose = ParserPrefs.FromText("{\"Details\":true,\"DetailsSettled\":true,\"KillsNear\":true,\"Filters\":false,\"Hovers\":true}");
            var later = ParserPrefs.FromText("{\"DetailsSettled\":true,\"HonestSettled\":true,\"Deaths\":false,\"Filters\":false,\"Hovers\":false}");
            Check(fresh is { Deaths: true, Filters: true, Hovers: true, KillsNear: false, Targets: "boss", DpsActive: false } && steven is { Deaths: true, Filters: true, Hovers: true, KillsNear: false, HonestSettled: true }
                && chose is { Deaths: true, Filters: false, Hovers: true, KillsNear: true } && later is { Deaths: false, Filters: false, Hovers: false },
                "7. serious defaults: Deaths, List filters and Explanations on hover on for a new player and once for Steven's parser.json as it was; a player who had chosen keeps their choice; an off after the move stays off");
        }

        // store v2 on made-up hit rows: per-second damage, the boss's HP timeline, casts, biggest and smallest hits, raw hit
        // kinds, damage taken, a death with its killer and last hits (the killing blow arriving just after the death), uptime
        {
            var (p, kept) = New();
            p.Take(new InstanceEntered(t, ContentKind.Expedition, "Shattered Arkanis", 870));
            p.Take(new GroupChanged(t, new[] { Me, Ally }));
            p.Take(new BossEngaged(t.AddSeconds(1), "Talisra of the Void", 871));
            p.Take(new BossHealth(t.AddSeconds(1), 871, 100_000, 100_000));
            p.Take(new CastMade(t.AddSeconds(1.5), 1, 1, 12240000, 7));
            p.Take(new HitDealt(t.AddSeconds(1.6), 1, 1, 871, 3_000, 12240000, 7, 1, false, false, 3, 0x08, 0, 0));
            p.Take(new HitDealt(t.AddSeconds(1.6), 1, 1, 871, 1_000, 12240000, 7, 2, false, false, 2, 0, 1, 0));
            p.Take(new DamageDealt(t.AddSeconds(1.6), 1, 871, 4_000, Skill: 12240000, Hits: 2));
            p.Take(new BossHealth(t.AddSeconds(1.6), 871, 96_000, 100_000));
            p.Take(new HitDealt(t.AddSeconds(3.2), 1, 1, 871, 500, 12240000, 0, 0, true, false, 0, 0, 0, 0));
            p.Take(new DamageDealt(t.AddSeconds(3.2), 1, 871, 500, Skill: 12240000, Hits: 0));
            p.Take(new BossHealth(t.AddSeconds(3.2), 871, 95_500, 100_000));
            p.Take(new EffectChanged(t.AddSeconds(2), 2, 4, 1702000101, true, 10_000));
            p.Take(new HitTaken(t.AddSeconds(4), 871, 2, 8_000, 9000100, 2300908));
            p.Take(new HitTaken(t.AddSeconds(5), 871, 2, 9_000, 9000200, 2300908));
            p.Take(new UnitDied(t.AddSeconds(6), 2, "Aerin", false, 871, 9000300));
            p.Take(new HitTaken(t.AddSeconds(6.4), 871, 2, 12_000, 9000300, 2300908));   // the killing blow, just after the death
            p.Take(new EffectChanged(t.AddSeconds(6), 2, 4, 1702000101, false, 4_000));
            p.Take(new DamageDealt(t.AddSeconds(8), 1, 871, 5_000, Skill: 12240000, Hits: 1));
            p.Take(new BossHealth(t.AddSeconds(8), 871, 0, 100_000));
            p.Take(new UnitDied(t.AddSeconds(8), 871, "Talisra of the Void", true));
            var e = kept.Single(); var me = e.Members.Single(f => f.Id == 1); var aerin = e.Members.Single(f => f.Id == 2);
            var j = me.Abilities.Single(a => a.Skill == 12240000);
            Check(me.PerSecond!.Sum() == me.Damage && me.BossPerSecond!.Sum() == me.BossDamage && me.PerSecond.Count == e.SecondOf(e.End!.Value) + 1 && me.PerSecond[0] == 4_000 && me.PerSecond[2] == 500,
                $"store v2: each member's damage by second from the pull's start adds up to their damage ({string.Join(" ", me.PerSecond)})");
            Check(e.BossHpPerSecond is { } hp && hp.Count == e.SecondOf(e.End.Value) + 1 && hp[0] == 0.96 && hp[^1] == 0,
                $"...the boss's HP at each second, to the kill ({string.Join(" ", e.BossHpPerSecond ?? new())})");
            Check(j is { Casts: 1, StreamHits: 2, Max: 3_000, Min: 1_000, Hits: 3 } && j.Kinds!["result:3"] == 1 && j.Kinds["mark:3"] == 1 && j.Kinds["multi"] == 1,
                "...an ability's casts, its hits seen one by one with the biggest and smallest (a part with no hit under it counts as none), its raw kinds counted");
            var death = aerin.DeathLog!.Single();
            Check(death is { At: 5.0, KillerId: 871, KillerSkill: 9000300, Killer: "Talisra of the Void", BossHpLeft: 0.955 } && death.Last.Select(h => h.Amount).SequenceEqual(new long[] { 8_000, 9_000, 12_000 })
                && aerin.Taken!.Sum(x => x.Damage) == 29_000 && aerin.Taken.All(x => x.Source == "Talisra of the Void"),
                $"...a death: when, the boss's HP then, the killer and its skill, the last hits taken with the killing blow that came just after ({death.Last.Count}); damage taken by skill and source");
            Check(aerin.Buffs!.Single() is { Id: 1702000101, Skill: 17020001, Seconds: 4 } b && Math.Abs(b.Share - 4 / e.Duration(e.End.Value).TotalSeconds) < 1e-9,
                $"...an effect's uptime: {aerin.Buffs.Single().Seconds} s, {Encounter.Pct(aerin.Buffs.Single().Share)} of the pull");
            var back = System.Text.Json.JsonSerializer.Deserialize<Encounter>(System.Text.Json.JsonSerializer.Serialize(e))!;
            var old = Saved("krao-cave").First();
            Check(back.Members.Single(f => f.Id == 2).DeathLog!.Single().Last.Count == 3 && back.BossHpPerSecond!.Count == e.BossHpPerSecond!.Count
                && old.BossHpPerSecond == null && old.Members.All(f => f.PerSecond == null && f.Taken == null && f.DeathLog == null && f.Abilities.All(a => a.Casts == null && a.Max == null)),
                "...kept and read back whole; a record kept before reads with every new field empty");
        }

        // store v2 on Steven's recordings (20261114-14 to -16), replayed through this build's decoder: totals as without the hit
        // stream, buckets adding up, casts at or below hits, deaths with a killer. Only where the decoder (interface version 10)
        // and the recordings are on this PC
        {
            var recs = Recordings();
            if (NativeDecoder.Version < 10 || recs.Count == 0)
                Console.WriteLine($"skip  store v2 on Steven's recordings: decoder interface {NativeDecoder.Version}, {recs.Count} recordings here");
            else
            {
                var with = recs.SelectMany(r => Replay(r, hits: true)).ToList();
                var without = recs.SelectMany(r => Replay(r, hits: false)).ToList();
                string Totals(List<Encounter> l) => string.Join("|", l.Select(e => $"{e.Boss}:{e.Outcome}:{e.Credited}:{e.Uncredited}:{string.Join(",", e.Members.OrderBy(f => f.Id).Select(f => $"{f.Id}={f.Damage}/{f.BossDamage}"))}"));
                Check(with.Count > 0 && Totals(with) == Totals(without),
                    $"store v2, Steven's recordings: {with.Count} pulls, every total the same with the hit stream as without ({Ui.Amounts.Short(with.Sum(e => e.Credited))} credited)");
                var members = with.SelectMany(e => e.Members.Where(f => f.Damage > 0)).ToList();
                Check(members.All(f => f.PerSecond != null && f.PerSecond.Sum() == f.Damage && (f.BossPerSecond?.Sum() ?? 0) == f.BossDamage),
                    $"...each member's per-second damage adds up to their damage ({members.Count} members)");
                var abilities = members.SelectMany(f => f.Abilities).Where(a => a.Damage > 0 && a.Skill > 0).ToList();
                int withCasts = abilities.Count(a => a.Casts > 0), over = abilities.Count(a => a.Casts > (a.StreamHits ?? 0));
                Check(withCasts > 0 && over == 0 && abilities.Where(a => a.StreamHits > 0).All(a => a.Max >= a.Min && a.Min > 0),
                    $"...casts at or below hits for every ability ({withCasts} with casts, {abilities.Sum(a => a.Casts ?? 0):N0} casts, {abilities.Sum(a => a.StreamHits ?? 0):N0} hits; {over} over), each with its biggest and smallest hit");
                var deaths = members.SelectMany(f => f.DeathLog ?? new()).ToList();
                Check(deaths.All(d => d.KillerId != 0 && d.KillerSkill != 0 && d.Last.Count > 0) && members.Sum(f => f.DeathLog?.Count ?? 0) == members.Sum(f => f.Deaths),
                    $"...every death with its killer, killing skill and last hits ({deaths.Count} deaths, {deaths.Count(d => d.Killer.Length > 0)} by the boss by name), one record each");
                Check(members.Any(f => f.Taken is { Count: > 0 }) && members.Any(f => f.Buffs is { Count: > 0 }) && with.All(e => e.BossHpPerSecond is { Count: > 0 }),
                    $"...damage taken, uptimes and the boss's HP timeline filled ({members.Sum(f => f.Taken?.Sum(x => x.Damage) ?? 0):N0} taken, {members.Sum(f => f.Buffs?.Count ?? 0)} uptimes)");
            }
        }

        // crit, ready to switch on: each hit kind's hits, damage and biggest hit kept per ability (store v2); nothing shown as
        // crit until ParserPrefs.CritSource picks a kind; then the inspect view and the overlay get crit rate, crit share, max crit
        {
            var (p, kept) = New();
            p.Take(new InstanceEntered(t, ContentKind.Expedition, "Shattered Arkanis", 890));
            p.Take(new GroupChanged(t, new[] { Me }));
            p.Take(new BossEngaged(t.AddSeconds(1), "Talisra of the Void", 891));
            // four hits of Judgment: two normal (1,000), one result-3 (1,700), one result-3 with the x2 mark (3,400); one rank's id apart
            p.Take(new HitDealt(t.AddSeconds(2), 1, 1, 891, 1_000, 12240000, 1, 1, false, false, 2, 0, 0, 0));
            p.Take(new HitDealt(t.AddSeconds(3), 1, 1, 891, 1_000, 12240000, 2, 1, false, false, 2, 0, 0, 0));
            p.Take(new HitDealt(t.AddSeconds(4), 1, 1, 891, 1_700, 12240000, 3, 1, false, false, 3, 0, 0, 0));
            p.Take(new HitDealt(t.AddSeconds(5), 1, 1, 891, 3_400, 12240003, 4, 1, false, false, 3, 0x08, 0, 0));
            p.Take(new DamageDealt(t.AddSeconds(5), 1, 891, 7_100, Skill: 12240000, Hits: 4));
            p.Take(new BossEnded(t.AddSeconds(9), 891, BossOutcome.Kill));
            var e = kept.Single(); var me = e.Members.Single();
            var row = e.AbilityRows(me).Single();
            Check(row is { StreamHits: 4, StreamDamage: 7_100 } && Encounter.KindTotals(row, "result:3") == (2, 5_100, 3_400) && Encounter.KindTotals(row, "mark:3") == (1, 3_400, 3_400),
                "crit candidates: each kind's hits, damage and biggest hit kept per ability, a rank's id merged into its skill's row");
            string? key0 = Encounter.CritKey;
            Encounter.CritKey = Encounter.CritKeyOf(new ParserPrefs().CritNow);
            var none = Ui.FightView.Of(e, e.End!.Value, null).Members.Single();
            Encounter.CritKey = Encounter.CritKeyOf("result3");
            var r3 = Ui.FightView.Of(e, e.End.Value, null);
            Encounter.CritKey = Encounter.CritKeyOf("mark08");
            var m8 = Ui.FightView.Of(e, e.End.Value, null).Members.Single();
            Encounter.CritKey = key0;
            Check(new ParserPrefs().CritNow == "none" && none is { Crit: null, CritShare: null, MaxCrit: null } && !Ui.FightView.Of(e, e.End.Value, null).HasCrit,
                "...nothing shown as crit by default (CritSource none)");
            var r3m = r3.Members.Single();
            Check(r3.HasCrit && r3m is { Crit: 0.5, MaxCrit: 3_400 } && Math.Abs(r3m.CritShare!.Value - 5_100 / 7_100.0) < 1e-9 && r3m.Abilities.Single().Crit == 0.5
                && m8 is { Crit: 0.25, MaxCrit: 3_400 } && Math.Abs(m8.CritShare!.Value - 3_400 / 7_100.0) < 1e-9,
                $"...CritSource result3: crit {Encounter.Pct(r3m.Crit ?? 0)}, {Encounter.Pct(r3m.CritShare ?? 0)} of the damage, max crit {r3m.MaxCrit:N0}; mark08: {Encounter.Pct(m8.Crit ?? 0)}");
            Check(Encounter.CritKeyOf("result3") == "result:3" && Encounter.CritKeyOf("mark08") == "mark:3" && Encounter.CritKeyOf("none") == null && Encounter.CritKeyOf("junk") == null,
                "...the switch's values: none, result3, mark08 (anything else is none)");
        }

        // the release's defaults reach everyone: a crit or back-attack choice is written only when one is made, so a release that
        // changes the default (the morning's flip) reaches players whose settings were saved before; a choice in the file stays
        {
            var saved = System.Text.Json.JsonSerializer.Serialize(new ParserPrefs { Details = false });
            var back0 = ParserPrefs.FromText(saved);
            var chose = ParserPrefs.FromText("{\"DetailsSettled\":true,\"HonestSettled\":true,\"CritSource\":\"mark08\",\"BackSource\":\"none\"}");
            Check(!saved.Contains("CritSource") && !saved.Contains("BackSource") && back0.CritSource == null && back0.CritNow == ParserPrefs.CritDefault && back0.BackNow == ParserPrefs.BackDefault
                && chose is { CritNow: "mark08", BackNow: "none" },
                $"crit and back attacks: the default never written (saved settings follow a release's new default: crit {ParserPrefs.CritDefault}, back {ParserPrefs.BackDefault}); a choice in the file kept");
        }

        // back attacks: the side each hit came from kept whole per ability ("side:1" behind, "side:2" front); shown only when
        // ParserPrefs.BackSource picks it, as a share of the hits seen
        {
            var (p, kept) = New();
            p.Take(new InstanceEntered(t, ContentKind.Expedition, "Shattered Arkanis", 892));
            p.Take(new GroupChanged(t, new[] { Me }));
            p.Take(new BossEngaged(t.AddSeconds(1), "Talisra of the Void", 893));
            p.Take(new HitDealt(t.AddSeconds(2), 1, 1, 893, 1_000, 12240000, 1, 1, false, false, 2, 0x4000 | 1 << 12, 0, 0));           // behind
            p.Take(new HitDealt(t.AddSeconds(3), 1, 1, 893, 1_000, 12240000, 2, 1, false, false, 3, 0x4000 | 1 << 12 | 0x08, 0, 0));  // behind, a crit and the x2 mark
            p.Take(new HitDealt(t.AddSeconds(4), 1, 1, 893, 1_000, 12240000, 3, 1, false, false, 2, 0x4000 | 2 << 12, 0, 0));           // front
            p.Take(new HitDealt(t.AddSeconds(5), 1, 1, 893, 1_000, 12240000, 4, 1, false, false, 2, 0, 0, 0));                     // no side said (bit 14 clear)
            p.Take(new DamageDealt(t.AddSeconds(5), 1, 893, 4_000, Skill: 12240000, Hits: 4));
            p.Take(new BossEnded(t.AddSeconds(9), 893, BossOutcome.Kill));
            var e = kept.Single(); var a = e.Members.Single().Abilities.Single();
            string? bk0 = Encounter.BackKey;
            Encounter.BackKey = Encounter.BackKeyOf(new ParserPrefs().BackNow);
            var off = Ui.FightView.Of(e, e.End!.Value, null);
            Encounter.BackKey = Encounter.BackKeyOf("side1");
            var on = Ui.FightView.Of(e, e.End.Value, null);
            Encounter.BackKey = bk0;
            Check(a.Kinds!["side:1"] == 2 && a.Kinds["side:2"] == 1 && !a.Kinds.ContainsKey("side:0") && a.Kinds["mark:3"] == 1 && !a.Kinds.Keys.Any(k => k is "mark:12" or "mark:13" or "mark:14"),
                "back attacks: each hit's side kept when the row says one (2 behind, 1 front; 1 with none not counted), apart from the other marks");
            Check(!off.HasBack && off.Members.Single().Back == null && on.HasBack && on.Members.Single().Back == 0.5 && on.Members.Single().Abilities.Single().Back == 0.5 && Encounter.BackKeyOf("junk") == null,
                $"...nothing shown by default; BackSource side1: {Encounter.Pct(on.Members.Single().Back ?? 0)} of the hits from behind, per member and per ability");
        }

        // both crit candidates on Steven's recordings (20261114-14, ABI 10 decoder): kept for every member and skill, within what
        // the hits can hold, at the rates and multipliers the statistics found (result 3: a caster's x1.5 to x2.1 at 10 to 60%;
        // mark 0x08: about x2 at a few to 25%). Only where the decoder and the recording are on this PC
        {
            var rec = Recordings().FirstOrDefault(r => r.EndsWith("20261114-14.pcapng"));
            if (NativeDecoder.Version < 10 || rec == null) Console.WriteLine($"skip  crit candidates on Steven's recordings: decoder interface {NativeDecoder.Version}, {(rec == null ? "no" : "the")} recording here");
            else
            {
                var pulls = Replay(rec, hits: true);
                var members = pulls.SelectMany(e => e.Members.Select(f => (e, f))).Where(x => x.f.Abilities.Sum(a => a.StreamHits ?? 0) >= 50).ToList();
                bool within = members.All(x => x.f.Abilities.All(a => CritCandidates.All(c =>
                    Encounter.KindTotals(a, c.Key) is var k && k.Hits <= (a.StreamHits ?? 0) && k.Damage <= (a.StreamDamage ?? 0) && k.Max <= (a.Max ?? 0))));
                (double Rate, double Mult) Of(Fighter f, string key)
                {
                    int hits = f.Abilities.Sum(a => a.StreamHits ?? 0); long dmg = f.Abilities.Sum(a => a.StreamDamage ?? 0);
                    var k = f.Abilities.Select(a => Encounter.KindTotals(a, key)).Aggregate((H: 0, D: 0L), (s, x) => (s.H + x.Hits, s.D + x.Damage));
                    double mult = k.H > 0 && hits > k.H ? k.D / (double)k.H / ((dmg - k.D) / (double)(hits - k.H)) : 0;
                    return (k.H / (double)Math.Max(1, hits), mult);
                }
                double Median(IEnumerable<double> xs) { var l = xs.OrderBy(x => x).ToList(); return l.Count == 0 ? 0 : l[l.Count / 2]; }
                var r3 = members.Select(x => Of(x.f, "result:3")).ToList(); var m8 = members.Select(x => Of(x.f, "mark:3")).ToList();
                Check(members.Count >= 10 && within && r3.All(x => x.Rate is > 0.05 and < 0.9) && Median(r3.Select(x => x.Mult)) is > 1.3 and < 2.3,
                    $"crit candidates, Steven's 14:00 recording: result 3 kept for {members.Count} member pulls, every count within its hits; rate {Encounter.Pct(r3.Min(x => x.Rate))} to {Encounter.Pct(r3.Max(x => x.Rate))}, median x{Median(r3.Select(x => x.Mult)):0.00}");
                Check(m8.All(x => x.Rate is > 0 and < 0.3) && Median(m8.Select(x => x.Mult)) is > 1.5 and < 2.5,
                    $"...mark 0x08: rate {Encounter.Pct(m8.Min(x => x.Rate))} to {Encounter.Pct(m8.Max(x => x.Rate))}, median x{Median(m8.Select(x => x.Mult)):0.00}");
                // the side, where this decoder says it (its version from 2026-11-14 23:32 on): the tanks mostly in front, the casters more behind
                var sides = members.Select(x => (x.f, B: x.f.Abilities.Sum(a => a.Kinds?.GetValueOrDefault("side:1") ?? 0), F: x.f.Abilities.Sum(a => a.Kinds?.GetValueOrDefault("side:2") ?? 0))).ToList();
                if (sides.Sum(s => s.B + s.F) == 0) Console.WriteLine("skip  back attacks on Steven's recording: this decoder says no side");
                else
                {
                    double Behind(string cls) => sides.Where(s => s.f.Class.Equals(cls, StringComparison.OrdinalIgnoreCase)).Sum(s => (double)s.B) / Math.Max(1, sides.Where(s => s.f.Class.Equals(cls, StringComparison.OrdinalIgnoreCase)).Sum(s => s.B + s.F));
                    var behind = sides.Where(s => s.B + s.F > 0).Select(s => s.B / (double)(s.B + s.F)).ToList();
                    Check(behind.Count >= 10 && Behind("templar") < Behind("sorcerer") && Behind("templar") < Behind("cleric"),
                        $"back attacks, Steven's 14:00 recording: kept for {sides.Count(s => s.B + s.F > 0)} member pulls, {Encounter.Pct(behind.Min())} to {Encounter.Pct(behind.Max())} of sided hits from behind; the tank in front more than the casters (Templar {Encounter.Pct(Behind("templar"))}, Sorcerer {Encounter.Pct(Behind("sorcerer"))}, Cleric {Encounter.Pct(Behind("cleric"))})");
                }
            }
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

        // Always on (Steven, 2026-11-15: "always be on and then reset to 0 during a boss fight ... in the open world it will
        // reset after 2 min of not fighting anything. Same w/ abyss. If you enter a dungeon a new instance/run starts")
        {
            var (p, kept) = New();
            p.Take(new GroupChanged(t, new[] { Me, Ally }));
            p.Take(new DamageDealt(t.AddSeconds(1), 0, 5000, 7_000, Reason: 4));              // a stranger's hit nearby (the decoder: outside the group)
            Check(p.AlwaysLive == null, "Always on: a stranger's hit nearby never starts the meter");
            p.Take(new DamageDealt(t.AddSeconds(2), 1, 5000, 10_000, "Swoly", true));
            p.Take(new DamageDealt(t.AddSeconds(3), 2, 5000, 5_000, "Aerin"));
            var a = p.AlwaysLive;
            Check(a is { Kind: "Open world", HasDamage: true } && a.Members.Sum(f => f.Damage) == 15_000 && !a.Waiting,
                $"...your first hit in the open world starts it, your group's hits count: {a?.Kind}, {a?.Members.Sum(f => f.Damage)}");
            // a few seconds without fighting ends it (Steven, 2026-11-15: "it can stop when you stop DPSing for a few seconds while
            // in the open world and refresh"; it was 2 minutes)
            p.Take(new DamageDealt(t.AddSeconds(5), 0, 5001, 7_000, Reason: 4));              // only strangers fight on
            p.Tick(t.AddSeconds(3 + BossParser.AlwaysIdleSeconds - 1));
            Check(p.AlwaysLive != null, $"...{BossParser.AlwaysIdleSeconds - 1:0} s after your group's last hit it carries on");
            p.Tick(t.AddSeconds(3 + BossParser.AlwaysIdleSeconds));
            var held = p.AlwaysEnded;
            Check(BossParser.AlwaysIdleSeconds <= 6 && p.AlwaysLive == null && held is { Live: false } && held.End == t.AddSeconds(3) && held.Members.Sum(f => f.Damage) == 15_000,
                $"...{BossParser.AlwaysIdleSeconds:0} s without your group fighting (strangers' hits don't count) and it ends at your last hit, held with its figures: {held?.End:HH:mm:ss}, {held?.Members.Sum(f => f.Damage)}");
            // the meter's foot (Steven, 2026-11-15: "have the bottom be total AND group dps avg"): the total and the group's DPS;
            // held, the whole fight's average with its "Avg. DPS" badge; no TOP plates out in the world
            string S(double v) => Ui.Amounts.Short(v);
            Check(a != null && !a.HeldAverage && a.FootText(t.AddSeconds(3)) == $"Total {S(15_000)} · Group DPS {S(15_000)}"
                && held != null && held.HeldAverage && held.FootText(t.AddSeconds(30)) == $"Total {S(15_000)} · Group {S(15_000)} · Avg. DPS" && !held.TopsShown && held.TopDps(t) == null,
                $"...the foot says the total and the group's DPS, live \"{a?.FootText(t.AddSeconds(3))}\"; held, the fight's average badged \"{held?.FootText(t.AddSeconds(30))}\"; no TOP DPS in the open world");
            p.Take(new DamageDealt(t.AddSeconds(200), 1, 5002, 4_000, "Swoly", true));
            Check(p.AlwaysLive is { } a2 && a2.Members.Sum(f => f.Damage) == 4_000 && a2.Start == t.AddSeconds(200), "...and the next hit starts it from 0");
            Check(kept.Count == 0, $"...never kept in Bosses only ({kept.Count} kept)");
            // being hit is fighting too
            p.Take(new HitTaken(t.AddSeconds(203), 7000, 1, 3_000, 0, 7000));
            p.Tick(t.AddSeconds(200 + BossParser.AlwaysIdleSeconds + 1));
            Check(p.AlwaysLive != null, "...a hit taken by you keeps it going (past the idle time after your last hit, inside it after the hit taken)");
            p.Tick(t.AddSeconds(203 + BossParser.AlwaysIdleSeconds));
            Check(p.AlwaysLive == null, "...and it ends that long after the hit taken");

            // a field or world boss out here: from 0 under its name; the same boss said again carries on
            p.Take(new DamageDealt(t.AddSeconds(500), 1, 5003, 9_000, "Swoly", true));
            p.Take(new BossEngaged(t.AddSeconds(510), "Kashapa", 4400));
            Check(p.AlwaysLive is { Boss: "Kashapa" } wb && !wb.HasDamage, $"...a boss pulled in the open world starts it from 0 under its name: {p.AlwaysLive?.Boss}, {p.AlwaysLive?.Members.Sum(f => f.Damage)}");
            p.Take(new DamageDealt(t.AddSeconds(511), 1, 4400, 20_000, "Swoly", true));
            p.Take(new BossEngaged(t.AddSeconds(530), "Kashapa", 4400));
            Check(p.AlwaysLive is { Boss: "Kashapa" } wb2 && wb2.Members.Sum(f => f.Damage) == 20_000, "...the same boss said again carries on");

            // into a dungeon: a new run, from 0; a reconnect or an unnamed loading screen into the same copy is not
            p.Take(new InstanceEntered(t.AddSeconds(600), ContentKind.Expedition, "Shattered Arkanis", 77));
            Check(p.AlwaysLive == null && p.AlwaysEnded is { Boss: "Kashapa" }, "...entering a dungeon starts it from 0 (the open world's held until the first hit there)");
            p.Take(new DamageDealt(t.AddSeconds(620), 1, 6000, 8_000, "Swoly", true));
            Check(p.AlwaysLive is { Kind: "Expedition", Instance: "Shattered Arkanis" } d1 && d1.Members.Sum(f => f.Damage) == 8_000, $"...the first hit inside starts the dungeon's: {p.AlwaysLive?.Kind}, {p.AlwaysLive?.Members.Sum(f => f.Damage)}");
            p.Take(new InstanceEntered(t.AddSeconds(630), ContentKind.Expedition, "Shattered Arkanis", 78));   // a reconnect: the decoder numbers every entry
            p.Take(new InstanceEntered(t.AddSeconds(635), ContentKind.Other, "", 79));                         // a loading screen the messages can't name
            p.Take(new DamageDealt(t.AddSeconds(640), 1, 6000, 2_000, "Swoly", true));
            Check(p.AlwaysLive is { } d2 && d2.Members.Sum(f => f.Damage) == 10_000, $"...a reconnect and an unnamed loading screen into the same copy carry it on: {p.AlwaysLive?.Members.Sum(f => f.Damage)}");
            p.Tick(t.AddSeconds(640 + 300));
            Check(p.AlwaysLive != null, "...inside a dungeon 5 minutes of quiet don't start it again (the bosses and the door do)");

            // a boss pulled: the pull is the meter, from 0; its end is held; the next trash hit starts from 0
            p.Take(new BossEngaged(t.AddSeconds(1000), "Talisra of the Void", 902));
            Check(p.AlwaysLive is { Boss: "Talisra of the Void", Mode: "boss" } b0 && b0.Members.Sum(f => f.Damage) == 0, $"...a boss pulled in the dungeon starts it from 0, the pull itself: {p.AlwaysLive?.Boss}, {p.AlwaysLive?.Mode}");
            p.Take(new DamageDealt(t.AddSeconds(1001), 1, 902, 50_000, "Swoly", true));
            p.Take(new DamageDealt(t.AddSeconds(1002), 2, 6001, 1_000, "Aerin"));            // an add
            p.Take(new DamageDealt(t.AddSeconds(1030), 2, 902, 30_000, "Aerin"));
            Check(p.AlwaysLive is { } b1 && b1.Members.Sum(f => f.BossDamage) == 80_000, $"...the pull's damage only: {p.AlwaysLive?.Members.Sum(f => f.BossDamage)}");
            p.Take(new BossEnded(t.AddSeconds(1040), 902, BossOutcome.Kill));
            Check(p.AlwaysLive == null && p.AlwaysEnded is { Outcome: "kill", Boss: "Talisra of the Void" } && kept.Count == 1 && kept[0].Boss == "Talisra of the Void",
                $"...the kill is held on the meter and kept as a boss pull as ever (bests and Compare): {p.AlwaysEnded?.Outcome}, {kept.Count} kept");
            {
                // the end of a boss fight in an Expedition: TOP DPS on the highest DPS's row; no TOP HEALS while heals aren't known
                var k = p.AlwaysEnded!;
                Check(k.TopsShown && k.HeldAverage && k.TopDps(t.AddSeconds(2000))?.Name == "Swoly" && k.TopHealer(t.AddSeconds(2000)) == null && k.FootText(t.AddSeconds(2000)).EndsWith("· Avg. DPS", StringComparison.Ordinal)
                    && p.AlwaysLive == null,
                    $"...a boss kill in an Expedition puts TOP DPS on the top row ({k.TopDps(t.AddSeconds(2000))?.Name}), no TOP HEALS without heal figures, the foot badged ({k.FootText(t.AddSeconds(2000))})");
            }
            p.Take(new DamageDealt(t.AddSeconds(1060), 1, 6002, 3_000, "Swoly", true));
            Check(p.AlwaysLive is { Mode: "fight" } tr && tr.Members.Sum(f => f.Damage) == 3_000 && tr.Start == t.AddSeconds(1060) && !tr.TopsShown && !tr.HeldAverage, "...the next trash hit after the boss starts it from 0 (live: no badge, no plates)");

            // out of the dungeon: from 0 again
            p.Take(new InstanceLeft(t.AddSeconds(1100)));
            Check(p.AlwaysLive == null, "...leaving the dungeon starts it from 0");
            p.Take(new InstanceEntered(t.AddSeconds(1110), ContentKind.Other, "Verteron", 80));
            p.Take(new DamageDealt(t.AddSeconds(1120), 1, 5004, 1_000, "Swoly", true));
            Check(p.AlwaysLive is { Kind: "Open world" } ow && ow.Members.Sum(f => f.Damage) == 1_000, $"...and the open world's first hit starts its own: {p.AlwaysLive?.Kind}");
            // the same dungeon run again: leaving it started the meter again, so entering it is a new run too
            p.Take(new InstanceEntered(t.AddSeconds(1200), ContentKind.Expedition, "Shattered Arkanis", 81));
            p.Take(new DamageDealt(t.AddSeconds(1210), 1, 6003, 5_000, "Swoly", true));
            Check(p.AlwaysLive is { Kind: "Expedition" } r2 && r2.Members.Sum(f => f.Damage) == 5_000, "...the same dungeon run again is a new run");
        }

        // Always on from the screen's place (a build or a moment without the game's messages): only going into a dungeon
        // starts it again; a minimap misread mid-dungeon (a name a letter off) never does
        {
            var (p, _) = New();
            p.Take(new GroupChanged(t, new[] { Me }));
            p.ScreenPlace("Expedition", "Fire Temple");
            p.Take(new DamageDealt(t.AddSeconds(1), 1, 6000, 1_000, "Swoly", true));
            p.ScreenPlace("Expedition", "Fire Tempe");
            p.Take(new DamageDealt(t.AddSeconds(2), 1, 6000, 1_000, "Swoly", true));
            Check(p.AlwaysLive is { } s1 && s1.Members.Sum(f => f.Damage) == 2_000, $"Always on: a misread of the dungeon's name on the minimap never starts the meter again ({p.AlwaysLive?.Members.Sum(f => f.Damage)})");
            p.ScreenPlace(null, null);
            p.Take(new DamageDealt(t.AddSeconds(3), 1, 5000, 1_000, "Swoly", true));
            p.ScreenPlace("Sanctuary", "Chalice of Muspel");
            Check(p.AlwaysLive == null, "...going into a dungeon by the screen's place does");
        }

        // a misread hit (2026-11-15: Divine Punishment read 261M once in the open world): left out, the record partial
        {
            var (p, _) = New();
            p.Take(new GroupChanged(t, new[] { Me }));
            p.Take(new DamageDealt(t.AddSeconds(1), 1, 5000, 4_000, "Swoly", true, 17050000, 1));
            p.Take(new DamageDealt(t.AddSeconds(2), 1, 5000, 261_040_996, "Swoly", true, 17050000, 1));
            p.Take(new DamageDealt(t.AddSeconds(3), 1, 5000, 60_000_000, "Swoly", true, 17050000, 10));   // ten hits of 6M: real, a DoT's sum
            Check(p.AlwaysLive is { } m && m.You?.Damage == 60_004_000 && m.Partial && m.PartialWhy.Contains("a misread hit left out"),
                $"a single hit of 261M is a misread: left out, the figure partial; ten hits summing 60M count ({p.AlwaysLive?.You?.Damage:N0}, {string.Join("; ", p.AlwaysLive?.PartialWhy ?? new())})");
        }

        // the Always on pick: off for everyone until picked, and kept
        {
            Check(!new ParserPrefs().AlwaysOn && ParserPrefs.FromText("{\"AlwaysOn\":true}").AlwaysOn, "Always on: off by default (Only boss fights), a pick kept in parser.json");
        }

        // DPS over the real time since the start (Steven, 2026-11-15: "i left and didnt kill anything for a while and it ... didnt
        // drop the dps ... at all" and "moving it depending on if youre attacking or not"): fighting raises it, a second
        // without a hit lowers it
        {
            var (p, _) = New();
            p.Take(new GroupChanged(t, new[] { Me }));
            p.Take(new DamageDealt(t, 1, 5000, 10_000, "Swoly", true));
            p.Take(new DamageDealt(t.AddSeconds(2), 1, 5000, 10_000, "Swoly", true));
            p.Take(new DamageDealt(t.AddSeconds(4), 1, 5000, 10_000, "Swoly", true));
            var a = p.AlwaysLive!;
            double At(Encounter e, double s) => e.Dps(e.You!, t.AddSeconds(s));
            Check(Math.Abs(At(a, 4) - 7_500) < 1, $"DPS: 30K in 4 s reads 7,500 ({At(a, 4):N0})");
            Check(At(a, 4.1) < At(a, 4) && At(a, 10) < At(a, 5) && Math.Abs(At(a, 60) - 500) < 1 && At(a, 110) < At(a, 60),
                $"...and it falls every moment without a hit ({At(a, 4.1):N0} a tenth of a second on, {At(a, 10):N0} at 10 s, {At(a, 60):N0} at a minute, {At(a, 110):N0} later): it never holds while you walk away");
            p.Take(new DamageDealt(t.AddSeconds(100), 1, 5001, 40_000, "Swoly", true));
            var b = p.AlwaysLive!;
            Check(At(b, 100) > At(a, 99.9), $"...a hit raises it at once ({At(a, 99.9):N0} to {At(b, 100):N0})");
            p.Tick(t.AddSeconds(100 + BossParser.AlwaysIdleSeconds));
            Check(p.AlwaysLive == null && p.AlwaysEnded is { } fin && Math.Abs(fin.Dps(fin.You!, t.AddSeconds(500)) - 70_000 / 100.0) < 1
                && fin.Dps(fin.You!, t.AddSeconds(500)) > At(b, 100 + BossParser.AlwaysIdleSeconds - 0.1),
                $"...a few seconds without a hit end it: the live figure falls through them ({At(b, 100 + BossParser.AlwaysIdleSeconds - 0.1):N0}), then goes back to the fight's DPS at its last hit, held (70K over its 100 s), the next hit starting from 0");
            // a fight of one hit joins the fight before it (Steven, 2026-11-15: "yes, merge them"): held alone it read the hit
            // over a second (119.7K for one 119.7K hit), the spike of a fight's start
            {
                var (pj, _) = New();
                pj.Take(new GroupChanged(t, new[] { Me }));
                pj.Take(new DamageDealt(t, 1, 5000, 50_000, "Swoly", true));
                pj.Take(new DamageDealt(t.AddSeconds(10), 1, 5000, 50_000, "Swoly", true));
                pj.Tick(t.AddSeconds(10 + BossParser.AlwaysIdleSeconds));
                double before = pj.AlwaysEnded!.Dps(pj.AlwaysEnded.You!, t.AddSeconds(20));
                pj.Take(new DamageDealt(t.AddSeconds(30), 1, 5001, 20_000, "Swoly", true));
                pj.Tick(t.AddSeconds(30 + BossParser.AlwaysIdleSeconds));
                var joined = pj.AlwaysEnded!;
                double after = joined.Dps(joined.You!, t.AddSeconds(60));
                Check(Math.Abs(before - 10_000) < 1 && Math.Abs(after - 120_000 / 11.0) < 1 && joined.You!.Damage == 120_000 && pj.AlwaysLive == null,
                    $"...a fight of one hit joins the fight before it: {before:N0} then {after:N0} (120K over 10 + 1 s, the quiet between them not counted), never the hit's 20,000 over a second");
                // a fight of several hits stays its own
                pj.Take(new DamageDealt(t.AddSeconds(60), 1, 5002, 30_000, "Swoly", true));
                pj.Take(new DamageDealt(t.AddSeconds(63), 1, 5002, 30_000, "Swoly", true));
                pj.Tick(t.AddSeconds(63 + BossParser.AlwaysIdleSeconds));
                Check(pj.AlwaysEnded is { } own && own.You!.Damage == 60_000 && Math.Abs(own.Dps(own.You!, t.AddSeconds(90)) - 20_000) < 1,
                    $"...a fight of several hits stays its own ({pj.AlwaysEnded?.You?.Damage:N0} over 3 s)");
            }
            // a hit taken keeps it going, but the held figure is still at the last hit dealt
            {
                var (ph, _) = New();
                ph.Take(new GroupChanged(t, new[] { Me }));
                ph.Take(new DamageDealt(t, 1, 5000, 10_000, "Swoly", true));
                ph.Take(new DamageDealt(t.AddSeconds(4), 1, 5000, 10_000, "Swoly", true));
                ph.Take(new HitTaken(t.AddSeconds(7), 5000, 1, 2_000, 0, 5000));
                ph.Tick(t.AddSeconds(7 + BossParser.AlwaysIdleSeconds));
                Check(ph.AlwaysEnded is { } hh && hh.End == t.AddSeconds(4) && Math.Abs(hh.Dps(hh.You!, t.AddSeconds(60)) - 5_000) < 1,
                    $"...a hit taken after your last one keeps the meter up, but the held DPS is at your last hit dealt ({ph.AlwaysEnded?.End:HH:mm:ss}, 20K over 4 s)");
            }
            // a boss pull: over the pull's own time
            var (q, _) = New();
            q.Take(new GroupChanged(t, new[] { Me }));
            q.Take(new InstanceEntered(t, ContentKind.Expedition, "Fire Temple", 1));
            q.Take(new BossEngaged(t.AddSeconds(1), "Flame Lord Kromede", 90));
            q.Take(new DamageDealt(t.AddSeconds(2), 1, 90, 60_000, "Swoly", true));
            q.Take(new DamageDealt(t.AddSeconds(20), 1, 90, 60_000, "Swoly", true));
            var pull = q.AlwaysLive!;
            Check(pull.Mode == "boss" && Math.Abs(pull.DpsSeconds(t.AddSeconds(31)) - pull.Duration(t.AddSeconds(31)).TotalSeconds) < 0.01,
                $"...a boss pull is over the pull's own time ({pull.DpsSeconds(t.AddSeconds(31)):0} s)");
        }
        // the Kinah line's farming figure: never over less than 3 s (a mob killed in a second read 209.7K a second)
        {
            var e = new Encounter { Mode = "fight", Start = t, LastAt = t, HasDamage = true };
            var me = new Fighter { Id = 1, You = true, Damage = 209_700 };
            me.Hit(t); me.Hit(t.AddSeconds(0.9));
            e.Roster.Add(me);
            Check(Math.Abs(e.FarmDps() - 69_900) < 1, $"farming DPS: a 209.7K mob killed in a second reads {e.FarmDps():N0}, over 3 s, not 209.7K");
        }

        Console.WriteLine(_fails == 0 ? "parser-test: all passed" : $"parser-test: {_fails} failed");
        return _fails == 0 ? 0 : 1;
    }
}
