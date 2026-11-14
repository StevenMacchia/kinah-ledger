using System.Net;
using KinahLedger.Data;
using KinahLedger.Session;

namespace KinahLedger.Packets;

/// <summary>
/// packet-test: the packet reader's own parts (segments out of order, twice and lost, frames from a card, a capture file,
/// the game's connection) and the counting: a change from the game's traffic counts where a chat line would, the chat's line
/// for it doesn't, a baseline's own pickup is the chat's, a sale counts nothing, and three misses hand a currency back to
/// the screen. The decoder itself is tested where it lives. packet-replay and packet-live run the decoder when the build has it.
/// </summary>
public static class PacketTests
{
    static int _fails;
    static void Check(bool ok, string what) { Console.WriteLine((ok ? "ok    " : "FAIL  ") + what); if (!ok) _fails++; }

    public static int Run(Settings settings)
    {
        _fails = 0;
        var t = new DateTime(2026, 11, 13, 21, 44, 43, DateTimeKind.Utc);

        // the stream: in order, out of order, twice, overlapping, a hole given up on, the sequence rolling over
        {
            var s = new TcpStream(); var got = new List<byte>(); int gaps = 0;
            s.OnData = (d, _) => got.AddRange(d); s.OnGap = () => gaps++;
            byte[] B(int from, int n) => Enumerable.Range(from, n).Select(i => (byte)i).ToArray();
            s.Add(1000, B(0, 10), t); s.Add(1020, B(20, 10), t); s.Add(1010, B(10, 10), t); s.Add(1010, B(10, 10), t); s.Add(1025, B(25, 10), t);
            Check(got.SequenceEqual(B(0, 35)) && gaps == 0, $"the stream puts segments in order once each: {got.Count} bytes");
            s.Add(1045, B(45, 5), t.AddSeconds(1)); s.Add(1050, B(50, 5), t.AddSeconds(4));
            Check(gaps == 1 && got.Count == 45 && got[^1] == 54, $"...and gives up on a hole after {TcpStream.GapSeconds:0} s: {gaps} gap, {got.Count} bytes");
            var w = new TcpStream(); var wg = new List<byte>(); w.OnData = (d, _) => wg.AddRange(d);
            w.Add(0xFFFFFFFA, B(0, 6), t); w.Add(0, B(6, 4), t);
            Check(wg.SequenceEqual(B(0, 10)), "...across the sequence number rolling over");
        }

        // a frame from the card: Ethernet with a VLAN tag, the TCP header's own length
        var server = IPAddress.Parse("193.202.112.2"); var me = IPAddress.Parse("192.168.1.4");
        var payload = Enumerable.Range(0, 32).Select(i => (byte)(i * 7)).ToArray();
        {
            var ip = Ip(server, 13328, me, 57735, 0x01020304, payload);
            var eth = new byte[12].Concat(new byte[] { 0x81, 0x00, 0x00, 0x05, 0x08, 0x00 }).Concat(ip).ToArray();
            var seg = NpcapSource.IpOf(eth, 1) is { } back ? TcpSegment.Parse(back) : null;
            Check(seg is { SrcPort: 13328, DstPort: 57735, Seq: 0x01020304 } sg && sg.Src.Equals(server) && sg.Payload.SequenceEqual(payload),
                $"a frame from the card: {seg?.Src}:{seg?.SrcPort} → {seg?.Dst}:{seg?.DstPort}, {seg?.Payload.Length} bytes");
        }

        // a capture file read back: the server's side of the connection put together, this PC's own packets left out
        {
            var dir = TestTemp.Path("KinahLedger-packet-test"); Directory.CreateDirectory(dir);
            var file = Path.Combine(dir, "capture.pcapng");
            var pk = new List<(DateTime, byte[])>
            {
                (t, Ip(server, 13328, me, 57735, 5000, payload[..20])),
                (t.AddSeconds(1), Ip(server, 13328, me, 57735, 5020, payload[20..])),
                (t.AddSeconds(1), Ip(server, 13328, me, 57735, 5020, payload[20..])),
                (t.AddSeconds(1), Ip(me, 57735, server, 13328, 9000, payload)),
            };
            Pcapng.Write(file, pk.Select(p => (p.Item1, new byte[12].Concat(new byte[] { 0x08, 0x00 }).Concat(p.Item2).ToArray())));
            var down = new List<byte>(); var st = new TcpStream { OnData = (d, _) => down.AddRange(d) };
            int frames = 0;
            foreach (var (at, frame, link) in Pcapng.Read(file))
            {
                frames++;
                if (NpcapSource.IpOf(frame, link) is { } ip && TcpSegment.Parse(ip) is { SrcPort: 13328 } s) st.Add(s.Seq, s.Payload, at, s.Syn);
            }
            Check(frames == 4 && down.SequenceEqual(payload), $"a capture file read back: {frames} frames, the server's {down.Count} bytes once each and in order");
            try { Directory.Delete(dir, true); } catch (Exception) { }
        }

        // the counting: a change counts where a chat line would, the chat's own line for it doesn't, the baseline's own
        // pickup is the chat's, a sale counts nothing, and three misses hand a currency back to the screen
        {
            var items = ItemDictionary.LoadOrEmpty(settings.ItemsFile);
            var reader = new PacketReader(); reader.ReadingForTest();
            var s = new TrackerSession(null, settings, items, "Test") { Packets = reader };
            var t0 = DateTime.UtcNow.AddMinutes(-3);
            s.Engine.SetZone("Western Cantas Valley");
            reader.AmountForTest(CurrencyKind.Kinah, 30_000_000, t0);
            s.CommitForTest(new KinahChanged(t0.AddSeconds(1.5), 300, false) { Via = "chat" });   // the baseline's own pickup: the chat's
            reader.AmountForTest(CurrencyKind.Kinah, 30_000_417, t0.AddSeconds(10));
            s.CommitForTest(new KinahChanged(t0.AddSeconds(11.2), 417, false) { Via = "chat" });   // the same pickup read off the chat
            s.CommitForTest(new KinahChanged(t0.AddSeconds(11.5), 5_193, false) { Via = "chat" });   // a misread
            reader.AmountForTest(CurrencyKind.Kinah, 30_000_936, t0.AddSeconds(12));   // the 519 the chat misread
            reader.AmountForTest(CurrencyKind.Kinah, 30_001_094, t0.AddSeconds(14));   // +158 the chat never read...
            s.CommitForTest(new CurrencyGained(t0.AddSeconds(15), "Fera Soul Points", 1) { Via = "chat" });   // ...though it read the kill
            s.DrainPacketsForTest();
            Check(s.Engine.Kinah == 300 + 417 + 519 + 158, $"Kinah counted from the game's traffic: {s.Engine.Kinah:N0} (300 from the chat before the baseline, then 417 + 519 + 158; the chat's 417 and its misread 5,193 left out)");
            // bound Kinah and Abyss Points in the Abyss
            s.Engine.SetZone("Chaotic Lower Reshanta");
            reader.AmountForTest(CurrencyKind.BoundKinah, 2_946_591, t0.AddSeconds(20)); reader.AmountForTest(CurrencyKind.AbyssPoints, 31_422, t0.AddSeconds(20));
            reader.AmountForTest(CurrencyKind.BoundKinah, 2_946_869, t0.AddSeconds(30)); reader.AmountForTest(CurrencyKind.AbyssPoints, 31_434, t0.AddSeconds(30));
            s.CommitForTest(new CurrencyGained(t0.AddSeconds(31), "Abyss Points", 7) { Via = "chat" });   // "Acquired M 7 Abyss Point(s)"
            s.CommitForTest(new KinahChanged(t0.AddSeconds(31), 278, true) { Via = "chat" });
            s.DrainPacketsForTest();
            // (a kill's bound Kina in the Abyss is its Kinah since 0.10.71: SessionEngine.IsAbyssKillKina)
            Check(s.Engine.AbyssBoundKinah == 278 && s.Engine.KinahBound == 0 && s.Engine.Kinah == 1_394 + 278 && s.Engine.AbyssPoints == 12, $"...bound Kinah +{s.Engine.AbyssBoundKinah} (the kill's Kinah: {s.Engine.Kinah:N0}) and Abyss Points +{s.Engine.AbyssPoints} from the game's traffic, the chat's misread 7 left out");
            // a sale (an NPC in town, nothing killed around it) and a rise in a base are no pickups
            s.Engine.SetZone("Western Cantas Valley");
            reader.AmountForTest(CurrencyKind.Kinah, 30_001_594, t0.AddSeconds(50)); s.DrainPacketsForTest();
            s.Engine.SetZone("Test Base");
            reader.AmountForTest(CurrencyKind.Kinah, 30_002_094, t0.AddSeconds(52)); s.CommitForTest(new CurrencyGained(t0.AddSeconds(52), "Fera Soul Points", 1) { Via = "chat" }); s.DrainPacketsForTest();
            Check(s.Engine.Kinah == 1_394 + 278, $"...a rise with nothing killed around it (a sale) and one in a base count nothing: {s.Engine.Kinah:N0}");
            // three chat pickups the traffic never showed: Kinah goes back to the screen
            s.Engine.SetZone("Western Cantas Valley");
            for (int i = 0; i < 3; i++) s.CommitForTest(new KinahChanged(t0.AddSeconds(70 + i * 20), 400 + i, false) { Via = "chat" });
            reader.AgreeForTest();
            bool broken = reader.Broken(CurrencyKind.Kinah) != null;
            s.CommitForTest(new KinahChanged(t0.AddSeconds(135), 500, false) { Via = "chat" });
            Check(broken && s.Engine.Kinah == 1_894 + 278 && reader.Broken(CurrencyKind.AbyssPoints) == null, $"...three chat pickups the game's traffic never showed: Kinah is the screen's again ({s.Engine.Kinah:N0}), Abyss Points stay the traffic's");
        }

        // at once (0.10.70; Steven, 2026-11-13: "these numbers should be instantaneous"): a rise with a kill already in counts
        // as it comes, not 3 s later; one with no kill yet waits for the kill's line (it lags the message) and counts when it
        // comes; a pickup the traffic counted a few seconds before is a kill's evidence too; an item counts once its chat line is in
        {
            var items = ItemDictionary.LoadOrEmpty(settings.ItemsFile);
            TrackerSession Fresh(out PacketReader r)
            {
                r = new PacketReader(); r.ReadingForTest();
                var x = new TrackerSession(null, settings, items, "Test") { Packets = r };
                x.Engine.SetZone("Western Cantas Valley");
                r.AmountForTest(CurrencyKind.Kinah, 10_000_000, DateTime.UtcNow.AddSeconds(-60));   // the baseline
                return x;
            }
            var now = DateTime.UtcNow;
            var a = Fresh(out var ra);
            a.CommitForTest(new CurrencyGained(now.AddSeconds(-2), "Fera Soul Points", 1, "Fera") { Via = "chat" });
            ra.AmountForTest(CurrencyKind.Kinah, 10_000_400, now); a.DrainPacketsForTest();
            Check(a.Engine.Kinah == 400, $"a rise with a kill just before counts as it comes: {a.Engine.Kinah:N0} at once (400)");
            var b = Fresh(out var rb);
            rb.AmountForTest(CurrencyKind.Kinah, 10_000_400, now); b.DrainPacketsForTest();
            long held = b.Engine.Kinah;
            b.CommitForTest(new CurrencyGained(now.AddSeconds(1.5), "Fera Soul Points", 1, "Fera") { Via = "chat" }); b.DrainPacketsForTest();
            Check(held == 0 && b.Engine.Kinah == 400, $"...one with no kill yet waits for the kill's line and counts as it comes: {held:N0}, then {b.Engine.Kinah:N0}");
            var c = Fresh(out var rc);
            c.CommitForTest(new CurrencyGained(now.AddSeconds(-14.5), "Fera Soul Points", 1, "Fera") { Via = "chat" });
            rc.AmountForTest(CurrencyKind.Kinah, 10_000_400, now.AddSeconds(-5)); rc.AmountForTest(CurrencyKind.Kinah, 10_000_650, now); c.DrainPacketsForTest();
            Check(c.Engine.Kinah == 650, $"...a pickup counted 5 s before (the kill 14.5 s back, out of reach) is evidence too: {c.Engine.Kinah:N0} at once (400 + 250)");
            const long Manastone = 511360001;
            var d = Fresh(out var rd);
            d.CommitForTest(new CurrencyGained(now.AddSeconds(-2), "Fera Soul Points", 1, "Fera") { Via = "chat" });
            rd.ItemsForTest(new ItemChange(now.AddSeconds(-0.2), Manastone, ItemOp.Changed, 1, 1, 3, false, 1, 1, false)); d.DrainPacketsForTest();
            int before = d.Engine.Items.GetValueOrDefault(Manastone);
            d.CommitForTest(new ItemAcquired(now.AddSeconds(1.2), Manastone, 1, false) { Via = "chat" }); d.DrainPacketsForTest();
            Check(before == 0 && d.Engine.Items.GetValueOrDefault(Manastone) == 1, $"...an item waits for the chat's say on it (a drop, or a box's contents) and counts once its line is in: {before}, then {d.Engine.Items.GetValueOrDefault(Manastone)}");
            // the game's own kill (0.10.72's fight events) is the evidence as it comes: the fight's first Kinah counts at once
            var f = Fresh(out var rf);
            rf.FightsForTest(new FightEvent(now.AddSeconds(-0.1), FightKind.Kill, FightFlags.Mine | FightFlags.VictimMonster, 0, 0, 7, 0, 0, "", ""));
            rf.AmountForTest(CurrencyKind.Kinah, 10_000_400, now); f.DrainPacketsForTest();
            Check(f.Engine.Kinah == 400, $"...the game's own kill, just in, is the evidence: the fight's first pickup counts at once ({f.Engine.Kinah:N0}), no chat line needed");
            // the live path: the loop's wait between passes ends on the message, which is counted within a fraction of a second
            var e = Fresh(out var re);
            e.CommitForTest(new CurrencyGained(DateTime.UtcNow.AddSeconds(-1), "Fera Soul Points", 1, "Fera") { Via = "chat" });
            var feed = Task.Run(async () => { for (int i = 1; i <= 5; i++) { await Task.Delay(150); re.AmountForTest(CurrencyKind.Kinah, 10_000_000 + i * 100, DateTime.UtcNow); } });
            e.WaitForTest(1200);
            feed.Wait();
            double worst = e.PacketDelays.Count > 0 ? e.PacketDelays.Max() : 99;
            Check(e.Engine.Kinah == 500 && worst < 0.3, $"...the reading loop's wait counts each message as it comes: {e.PacketDelays.Count} of 5 counted, the slowest {worst * 1000:0} ms after its message (under 300)");
        }

        // kills and deaths (0.10.72): the game says whose kill it was and what died; a kill naming no killer is the player's
        // when their Rank Points rose with it and no kill of their own took that rise; a death counts once
        {
            var t0 = DateTime.UtcNow.AddMinutes(-2);
            FightEvent K(double s, FightFlags f, string victim = "", string killer = "") => new(t0.AddSeconds(s), FightKind.Kill, f, 1, 2, f.HasFlag(FightFlags.VictimMonster) ? 7u : 0, 0, 0, victim, killer);
            FightEvent R(double s, long d) => new(t0.AddSeconds(s), FightKind.RankPoints, FightFlags.None, 0, 0, 0, d, 0, "", "");
            var ft = new FightTally();
            foreach (var e in new[] {
                K(0, FightFlags.Mine | FightFlags.VictimMonster), R(0, 13),                      // mine, a monster, its own rise
                K(5, FightFlags.NoKiller), R(5.3, 13),                                           // no killer named, my points rose: mine
                K(10, FightFlags.NoKiller | FightFlags.VictimPlayer, "Zev"),                     // a player died, no rise: not mine
                K(15, FightFlags.Mine | FightFlags.VictimMonster), K(15.2, FightFlags.NoKiller), R(15.2, 12),   // one rise, taken by my own kill
                K(20, FightFlags.Mine | FightFlags.VictimPlayer, "Pecked"), R(20, 46),
                K(25, FightFlags.VictimMe, killer: "Nana"), K(30, FightFlags.VictimMe | FightFlags.NoKiller) })
                ft.Add(e);
            var early = ft.Take(t0.AddSeconds(2));
            var all = early.Concat(ft.Take(t0.AddSeconds(60))).ToList();
            string Say(FightTally.Outcome o) => o.Death ? $"death{(o.Name.Length > 0 ? ":" + o.Name : "")}" : $"{(o.Player ? "player" : "monster")}{(o.Credited ? "*" : "")}{(o.Name.Length > 0 ? ":" + o.Name : "")}";
            Check(early.Count == 0 && string.Join(" ", all.Select(Say)) == "monster monster* monster player:Pecked death:Nana death" && ft.RankPoints == 84,
                $"fights: held until the points after them are in, then {string.Join(" ", all.Select(Say))}; Rank Points +{ft.RankPoints}");
            // through the session: the counts, the kills the larger of the chat's and the game's, the kill marked for the pickup gate
            var items = ItemDictionary.LoadOrEmpty(settings.ItemsFile);
            var reader = new PacketReader(); reader.ReadingForTest();
            var s = new TrackerSession(null, settings, items, "Test") { Packets = reader };
            s.ApplyZone("Sulfur Tree Island", t0);
            foreach (var e in new[] { K(0, FightFlags.Mine | FightFlags.VictimMonster), R(0, 13), K(4, FightFlags.Mine | FightFlags.VictimMonster), K(8, FightFlags.Mine | FightFlags.VictimPlayer, "Pecked"), K(12, FightFlags.VictimMe, killer: "Nana") })
                reader.FightsForTest(e);
            s.CommitForTest(new CurrencyGained(t0.AddSeconds(1), "Abyss Swarm Soul Points", 1) { Via = "chat" });
            s.DrainPacketsForTest();
            var z = s.Engine.ZoneSnapshot().GetValueOrDefault("Abyss");
            Check(s.Engine.PlayerKills == 1 && s.Engine.MonsterKills == 2 && s.Engine.Deaths == 1 && s.Engine.DeathsToPlayers == 1 && s.Engine.Kills == 3 && z?.PlayerKills == 1 && z.MonsterKills == 2 && z.Deaths == 1,
                $"...counted in the Abyss: {s.Engine.PlayerKills} player, {s.Engine.MonsterKills} monsters, {s.Engine.Deaths} death ({s.Engine.DeathsToPlayers} to a player); kills {s.Engine.Kills} (the chat saw 1); zone Abyss {z?.PlayerKills}/{z?.MonsterKills}/{z?.Deaths}");
            // Rank Points (0.10.76): each rise counted from the game's message like Abyss Points, the chat's own line for it left
            // out; a fall is a death's cost (32 at each of the four deaths of 2026-11-13 18:45 to 18:48), counted as lost
            var r2 = new PacketReader(); r2.ReadingForTest();
            var s2 = new TrackerSession(null, settings, items, "Test") { Packets = r2 };
            s2.ApplyZone("Devastated Remnants", t0);
            FightEvent RT(double sec, long d, long total) => new(t0.AddSeconds(sec), FightKind.RankPoints, FightFlags.None, 0, 0, 0, d, total, "", "");
            foreach (var e in new[] { RT(0, 0, 262_900), K(10, FightFlags.Mine | FightFlags.VictimMonster), RT(10, 13, 262_913), K(20, FightFlags.VictimMe, killer: "Nana"), RT(20, -32, 262_881), K(30, FightFlags.Mine | FightFlags.VictimMonster), RT(30, 15, 262_896) })
                r2.FightsForTest(e);
            s2.CommitForTest(new CurrencyGained(t0.AddSeconds(11), "Rank Points", 13) { Via = "chat" });   // the chat's line for the first rise
            s2.DrainPacketsForTest();
            var z2 = s2.Engine.ZoneSnapshot().GetValueOrDefault("Abyss");
            Check(s2.Engine.RankPoints == 28 && s2.Engine.RankPointsLost == 32 && z2?.RankPoints == 28 && z2.RankLost == 32 && s2.Engine.Deaths == 1,
                $"...Rank Points from the game: +{s2.Engine.RankPoints} (13 + 15, the chat's 13 left out), {s2.Engine.RankPointsLost} lost at the death; zone Abyss +{z2?.RankPoints} −{z2?.RankLost}");
            // out of the Abyss Rank Points are no income of their own (a quest's, an event's)
            var e3 = new SessionEngine(items); e3.SetZone("Ellun River Midstream"); e3.Apply(new CurrencyGained(t0, "Rank Points", 50)); e3.Apply(new RankPointsLost(t0, 32));
            Check(e3.RankPoints == 0 && e3.RankPointsLost == 0, "...and out of the Abyss neither counts");
        }

        // items: a rise counts as a pickup where a chat line would, the chat's own line for it doesn't, a stack's first sight
        // and a line the traffic never showed are the chat's, a rise with nothing around it or with something going down is
        // no pickup, and three chat pickups the traffic never showed hand items back to the screen
        {
            const long Manastone = 511360001, Meat = 610531001, Odyle = 610610013, Kukuru = 610680044, Oil = 630554003, Leather = 610530011;
            var items = ItemDictionary.LoadOrEmpty(settings.ItemsFile);
            var reader = new PacketReader(); reader.ReadingForTest();
            var s = new TrackerSession(null, settings, items, "Test") { Packets = reader };
            var t0 = DateTime.UtcNow.AddMinutes(-4);
            int batch = 0;
            ItemChange Row(double at, long id, ItemOp op, long stack, long delta, long total, bool first = false, int rows = 1, bool fell = false) =>
                new(t0.AddSeconds(at), id, op, stack, delta, total, first, batch, rows, fell);
            s.Engine.SetZone("Chaotic Lower Reshanta");
            s.CommitForTest(new CurrencyGained(t0.AddSeconds(1), "Modified Karnif Soul Points", 1, "Modified Karnif") { Via = "chat" });   // a kill
            batch++; reader.ItemsForTest(Row(2, Manastone, ItemOp.NewStack, 1, 1, 1));   // the chat never read it
            batch++; reader.ItemsForTest(Row(4, Meat, ItemOp.Changed, 2, 0, 3, first: true));   // first sight: its pickup is the chat's...
            s.CommitForTest(new ItemAcquired(t0.AddSeconds(5.4), Meat, 1, false) { Via = "chat" });
            batch++; reader.ItemsForTest(Row(6, Meat, ItemOp.Changed, 2, 1, 4));   // ...the next one the traffic's
            s.CommitForTest(new ItemAcquired(t0.AddSeconds(7.5), Meat, 1, false) { Via = "chat" });   // the chat's line for it: left out
            s.DrainPacketsForTest(); reader.AgreeItemsForTest(DateTime.UtcNow); s.DrainPacketsForTest();
            int M(long id) => s.Engine.Items.GetValueOrDefault(id) + s.Engine.ItemsBound.GetValueOrDefault(id);
            Check(M(Manastone) == 1 && M(Meat) == 2 && reader.ItemsBroken == null,
                $"items from the game's traffic: Lesser Manastone {M(Manastone)} (no chat line), Fresh Raw Meat {M(Meat)} (the first sight's from the chat, the next from the traffic, its chat line left out)");
            // a chat line the traffic never showed is counted from the chat; nothing around a rise, or something going down with it, is no pickup
            s.CommitForTest(new ItemAcquired(t0.AddSeconds(30), Odyle, 1, false) { Via = "chat" });
            s.Engine.SetZone("Western Cantas Valley");
            batch++; reader.ItemsForTest(Row(60, Leather, ItemOp.Changed, 3, 5, 9));   // nothing killed or gathered around it: a purchase, the mail
            s.DrainPacketsForTest();
            batch++; reader.ItemsForTest(Row(70, Meat, ItemOp.Changed, 2, -3, 1, rows: 2, fell: true), Row(70, Kukuru, ItemOp.Changed, 4, 2, 7, rows: 2, fell: true));
            s.CommitForTest(new CurrencyGained(t0.AddSeconds(69), "Fera Soul Points", 1, "Fera") { Via = "chat" });
            s.DrainPacketsForTest(); reader.AgreeItemsForTest(DateTime.UtcNow); s.DrainPacketsForTest();
            Check(M(Odyle) == 1 && M(Leather) == 0 && M(Kukuru) == 0,
                $"...a chat pickup the traffic never showed counts from the chat (Odyle {M(Odyle)}); a rise with nothing around it, and one in an update where something went down, count nothing (Raw Leather {M(Leather)}, Kukuru {M(Kukuru)})");
            for (int i = 0; i < 2; i++) s.CommitForTest(new ItemAcquired(t0.AddSeconds(100 + i * 20), Odyle, 1, false) { Via = "chat" });
            reader.AgreeItemsForTest(DateTime.UtcNow); s.DrainPacketsForTest();
            bool broken = reader.ItemsBroken != null;
            s.CommitForTest(new ItemAcquired(t0.AddSeconds(150), Odyle, 2, false) { Via = "chat" });
            Check(broken && M(Odyle) == 5, $"...three chat pickups the traffic never showed: items are the screen's again (Odyle {M(Odyle)}, every chat line counted)");

            // a bought item read again (Steven, 2026-11-13 18:35: two Wrathful Ego bought, the crafting window over the chat, the
            // same two lines read again 23 s later with a kill beside them, counted as rare drops): a rise the tracker didn't
            // count covers the item's chat line however late it shows; a rise one line took covers that line read again; and
            // with no rise at all (the game's messages showed nothing for it), the chat's line set aside
            // covers its later reading. Only a line no rise and no handled line stands behind is "not shown", from the chat
            {
                const long Ego = 610511015;
                var t1 = DateTime.UtcNow.AddMinutes(-10);
                ItemChange R(double at, long id, long delta, long total) => new(t1.AddSeconds(at), id, ItemOp.Changed, 6, delta, total, false, ++batch, 1, false);
                var pkA = new PacketReader(); pkA.ReadingForTest();
                bool marketOpen = true;
                var m = new TrackerSession(null, settings, items, "Test") { Packets = pkA, MarketOpen = _ => marketOpen };
                m.Engine.SetZone("Chaotic Lower Reshanta");
                int E(long id) => m.Engine.Items.GetValueOrDefault(id) + m.Engine.ItemsBound.GetValueOrDefault(id);
                pkA.ItemsForTest(R(2, Ego, 1, 3));   // bought: the Market open
                m.DrainPacketsForTest();
                marketOpen = false;
                m.CommitForTest(new CurrencyGained(t1.AddSeconds(24), "Modified Karnif Soul Points", 1, "Modified Karnif") { Via = "chat" });   // a kill
                m.CommitForTest(new ItemAcquired(t1.AddSeconds(25), Ego, 1, false) { Via = "chat" });   // its line, read again 23 s later
                m.DrainPacketsForTest(); pkA.AgreeItemsForTest(DateTime.UtcNow); m.DrainPacketsForTest();
                Check(E(Ego) == 0 && pkA.Verdicts.LastOrDefault()?.StartsWith("covered: with the Market open") == true,
                    $"a bought item's rise covers its chat line read 23 s later: Wrathful Ego {E(Ego)} ({pkA.Verdicts.LastOrDefault()})");
                // a rise counted, its line covered, then the same line read again 20 s on: not a second pickup
                m.CommitForTest(new CurrencyGained(t1.AddSeconds(40), "Modified Karnif Soul Points", 1, "Modified Karnif") { Via = "chat" });
                pkA.ItemsForTest(R(41, Meat, 1, 4));
                m.CommitForTest(new ItemAcquired(t1.AddSeconds(42.5), Meat, 1, false) { Via = "chat" });
                m.CommitForTest(new ItemAcquired(t1.AddSeconds(62), Meat, 1, false) { Via = "chat" });
                m.DrainPacketsForTest(); pkA.AgreeItemsForTest(DateTime.UtcNow); m.DrainPacketsForTest();
                Check(E(Meat) == 1 && string.Join(",", pkA.Verdicts.TakeLast(2)) == "covered,covered: read again",
                    $"a rise counted once, its line read again 20 s later: Fresh Raw Meat {E(Meat)} ({string.Join(", ", pkA.Verdicts.TakeLast(2))})");
                // a line no rise or handled line stands behind is still the chat's (nothing lost), past the bought one's time
                m.CommitForTest(new ItemAcquired(t1.AddSeconds(2 + PacketReader.ExcludedSeconds + 20), Ego, 1, false) { Via = "chat" });
                pkA.AgreeItemsForTest(DateTime.UtcNow); m.DrainPacketsForTest();
                Check(E(Ego) == 1 && pkA.Verdicts.LastOrDefault() == "not shown", $"...and a line the traffic never showed, minutes later, counts from the chat: Wrathful Ego {E(Ego)} ({pkA.Verdicts.LastOrDefault()})");
                // the chat's line set aside (bought), no rise at all, then read again as new: covered by the set-aside line
                var pkB = new PacketReader(); pkB.ReadingForTest();
                pkB.ItemExcluded(Ego, t1.AddSeconds(200), "the chat's line for it was set aside: bought at the Market (its window was open)");
                pkB.ScreenSawItem(Ego, 1, t1.AddSeconds(223), true, new object());
                pkB.AgreeItemsForTest(DateTime.UtcNow);
                Check(pkB.NextUncovered() == null && pkB.Verdicts.LastOrDefault()?.StartsWith("covered: the chat's line for it was set aside") == true,
                    $"no rise for a bought item, its set-aside line read again: not counted ({pkB.Verdicts.LastOrDefault()})");
            }

            // crafting: materials going down start a try, the item going up ends it; nothing made before the next try is a
            // fail, twice the yield is a won combo; the panel's races are left out, and no craft's item is a pickup
            var r2 = new PacketReader(); r2.ReadingForTest();
            var c = new TrackerSession(null, settings, items, "Test") { Packets = r2 };
            c.Engine.SetZone("Western Cantas Valley");
            const long Dish = 510716005;
            const string Recipe = "Kukuru Fried Meat";
            void Mats(double at, bool first = false)
            {
                batch++; c.PanelUp(Recipe, t0.AddSeconds(at));
                r2.ItemsForTest(Row(at, Meat, ItemOp.Changed, 2, first ? 0 : -1, 200, first, 3, !first), Row(at, Kukuru, ItemOp.Changed, 4, first ? 0 : -1, 20, first, 3, !first), Row(at, Oil, ItemOp.Changed, 5, first ? 0 : -1, 60, first, 3, !first));
            }
            void Made(double at, long n, long total, bool fresh = false) { batch++; c.PanelUp(Recipe, t0.AddSeconds(at)); r2.ItemsForTest(Row(at, Dish, fresh ? ItemOp.NewStack : ItemOp.Changed, 9, n, total)); }
            c.CommitForTest(new CurrencyGained(t0.AddSeconds(1), "Fera Soul Points", 1, "Fera") { Via = "chat" });   // a kill just before: still no pickup
            Mats(2, first: true); Made(9, 1, 1, fresh: true);
            Mats(11); Made(14, 1, 2);
            Mats(16); Made(30, 2, 4);   // a combo: two
            Mats(32);                   // a fail: nothing made...
            Mats(40); Made(47, 1, 5);   // ...before this try
            c.PanelRace(Recipe, true, false, t0.AddSeconds(14));   // the panel's own race: left out
            c.CommitForTest(new ItemAcquired(t0.AddSeconds(31), Dish, 2, false) { Via = "chat" });   // the dish's chat line
            c.DrainPacketsForTest(); r2.AgreeItemsForTest(DateTime.UtcNow); c.DrainPacketsForTest();
            var rec = c.CraftView.CraftSnapshot().FirstOrDefault(x => x.Name == Recipe);
            Check(rec is { Ok: 4, Fail: 1, Upgraded: 1 } && M2(Dish) == 0,
                $"crafts from the game's traffic: {Recipe} {rec?.Ok} worked, {rec?.Fail} failed, {rec?.Upgraded} combo won, {rec?.MadeCount} made (the panel's race and the dish's chat line left out; {M2(Dish)} counted as pickups)");
            int M2(long id) => c.Engine.Items.GetValueOrDefault(id) + c.Engine.ItemsBound.GetValueOrDefault(id);

            // one material, two crafts' worth of it gone in one update, then the next queue's first update all first sights:
            // no fail from either
            var r3 = new PacketReader(); r3.ReadingForTest();
            var q = new TrackerSession(null, settings, items, "Test") { Packets = r3 };
            const long Strong = 610720011;
            void One(double at, string recipe, params ItemChange[] rows) { batch++; q.PanelUp(recipe, t0.AddSeconds(at)); r3.ItemsForTest(rows.Select(x => x with { Batch = batch, At = t0.AddSeconds(at) }).ToArray()); }
            One(2, "Strong Leather", Row(0, Leather, ItemOp.Changed, 3, 0, 9, first: true));
            One(5, "Strong Leather", Row(0, Strong, ItemOp.NewStack, 6, 1, 1));
            One(15, "Strong Leather", Row(0, Strong, ItemOp.Changed, 6, 1, 2));
            One(17, "Strong Leather", Row(0, Leather, ItemOp.Changed, 3, -4, 5, fell: true));
            One(25, "Strong Leather", Row(0, Strong, ItemOp.Changed, 6, 1, 3));
            One(30, "Kukuru Fried Meat", Row(0, Meat, ItemOp.Changed, 2, 0, 200, true, 3), Row(0, Kukuru, ItemOp.Changed, 4, 0, 20, true, 3), Row(0, Oil, ItemOp.Changed, 5, 0, 60, true, 3));
            One(38, "Kukuru Fried Meat", Row(0, Dish, ItemOp.NewStack, 9, 1, 1));
            q.DrainPacketsForTest(); q.DrainPacketsForTest();
            var sl = q.CraftView.CraftSnapshot().FirstOrDefault(x => x.Name == "Strong Leather");
            var fm = q.CraftView.CraftSnapshot().FirstOrDefault(x => x.Name == Recipe);
            // a try with nothing made while the traffic lost bytes: its item may have gone with them, so no fail
            var r4 = new PacketReader(); r4.ReadingForTest();
            var g4 = new TrackerSession(null, settings, items, "Test") { Packets = r4 };
            void Four(double at, params ItemChange[] rows) { batch++; g4.PanelUp("Strong Leather", t0.AddSeconds(at)); r4.ItemsForTest(rows.Select(x => x with { Batch = batch, At = t0.AddSeconds(at) }).ToArray()); }
            Four(2, Row(0, Leather, ItemOp.Changed, 3, -2, 9, fell: true)); Four(6, Row(0, Strong, ItemOp.NewStack, 6, 1, 1));
            Four(8, Row(0, Leather, ItemOp.Changed, 3, -2, 7, fell: true)); r4.GapForTest(t0.AddSeconds(12));
            Four(16, Row(0, Leather, ItemOp.Changed, 3, -2, 5, fell: true)); Four(20, Row(0, Strong, ItemOp.Changed, 6, 1, 2));
            g4.DrainPacketsForTest(); g4.DrainPacketsForTest();
            var lost = g4.CraftView.CraftSnapshot().FirstOrDefault(x => x.Name == "Strong Leather");
            Check(lost is { Ok: 2, Fail: 0 }, $"...a try with nothing made while the traffic lost bytes is no fail (its item may have gone with them): {lost?.Ok} worked, {lost?.Fail} failed");
            Check(sl is { Ok: 3, Fail: 0 } && fm is { Ok: 1, Fail: 0 },
                $"...a one-material recipe whose two tries went in one update, then another queue: Strong Leather {sl?.Ok} worked {sl?.Fail} failed, {Recipe} {fm?.Ok} worked {fm?.Fail} failed");
        }

        // a build without the decoder reads no traffic and says so
        {
            var keep = PacketReader.Decoders;
            PacketReader.Decoders = () => null;
            var r = new PacketReader();
            r.Tick(new uint[] { 1 }, null);   // no such game connection: it waits, it doesn't fail
            PacketReader.Decoders = keep;
            Check(!r.On && r.State.Length > 0, $"no game connection: \"{r.State}\"");
        }
        Console.WriteLine($"decoder in this build: {(NativeDecoder.Available ? "yes (klnet.dll)" : "no (a build from the public source: the screen counts)")}");
        if (NativeDecoder.Available)
            using (var nd = NativeDecoder.TryOpen())
            {
                // a dll of a later version without the boss export reads no boss fights and doesn't throw
                bool threw = false; try { while (nd?.NextBossEvent() != null) { } } catch (Exception) { threw = true; }
                Check(!threw, $"klnet.dll version {NativeDecoder.Version}: boss fights {(nd?.ReadsBosses == true ? "read" : "not read")}, asking for them never throws");
            }

        // boss fights (klnet.dll version 4): a decoder that reads none hands out none; the decoder's rows become the tracker's
        // events (the roster whole each time, deaths, pulls); the reader queues them in order and keeps the queue bounded; the
        // interface's record is the dll's 256 bytes and its text fields read back
        {
            IGameDecoder none = new NoBosses();
            Check(!none.ReadsBosses && none.NextBossEvent() == null, "a decoder without boss fights hands out none");
            var map = new BossMapper();
            BossRow Row(int kind, int s, long boss = 0, long who = 0, string name = "", bool self = false, long cp = 0, long il = 0, long amount = 0, long max = 0, int outcome = 0, string bossName = "") =>
                new(t.AddSeconds(s), kind, 2, "Fire Temple", 1, bossName, boss, outcome, who, name, "Gladiator", self, cp, il, amount, max);
            var got = new[]
            {
                Row(1, 0), Row(11, 0, bossName: "Conquest"), Row(6, 1, who: 14325, name: "Swoly", self: true, cp: 2637, il: 2637, bossName: "Siel"), Row(6, 1, who: 7093, name: "Frankss", cp: 2400, il: 2500),
                Row(3, 2, boss: 25495, amount: 1000, max: 1000, bossName: "Kromede's Desire"), Row(4, 3, boss: 25495, bossName: "Kromede's Desire"),
                Row(8, 3, boss: 25495, who: 14325, name: "Swoly", self: true, amount: 600), Row(8, 3, boss: 25495, amount: 400), Row(9, 3, boss: 25495, amount: 0, max: 1000),
                Row(10, 3, boss: 25495, who: 25495, amount: 1, bossName: "Kromede's Desire"), Row(5, 3, boss: 25495, outcome: 1), Row(7, 4, who: 7093), Row(7, 4, who: 999), Row(2, 5), Row(42, 6),
            }.Select(map.Map).Where(e => e != null).ToList();
            var roster = got.OfType<GroupChanged>().ToList();
            var dmg = got.OfType<DamageDealt>().ToList();
            Check(got[0] is InstanceEntered { Kind: ContentKind.Expedition, Name: "Fire Temple", InstanceId: 1 } && roster.Count == 3 && roster[1].Members.Count == 2
                && roster[1].Members.Any(m => m is { Name: "Swoly", You: true, CombatPower: 2637, Server: "Siel" }) && got[1] is InstanceMode { Mode: "Conquest" } && roster[2].Members.Single().Name == "Swoly",
                $"the decoder's rows: an instance, the roster whole each time ({string.Join(" ", roster.Select(r => r.Members.Count))}), a leaver gone, an unknown leaver nothing");
            Check(got.OfType<BossEngaged>().Single() is { Boss: "Kromede's Desire", BossId: 25495 } && dmg.Count == 2 && dmg[0] is { SourceId: 14325, SourceName: "Swoly", SourceIsYou: true, Amount: 600, TargetId: 25495 }
                && dmg[1] is { SourceId: 0, Amount: 400, SourceIsYou: false } && got.OfType<BossHealth>().Single() is { BossId: 25495, Hp: 0, Max: 1000 } && got.OfType<UnitDied>().Single() is { Boss: true, Name: "Kromede's Desire" }
                && got.OfType<BossEnded>().Single().Outcome == BossOutcome.Kill && got[^1] is InstanceLeft && !got.Any(e => e is null),
                $"...a pull, its damage (yours, named, and other: source 0), its HP, the boss's death and the kill; a boss sighting and an unknown kind make nothing ({got.Count} events)");
            var r = new PacketReader();
            foreach (var e in got) r.TakeBoss(e);
            var back = new List<BossEvent>(); while (r.NextBossEvent() is { } e) back.Add(e);
            Check(back.SequenceEqual(got) && !r.ReadsBosses, $"the reader queues boss parser events in order ({back.Count}) and says it reads none without the dll");
            for (int i = 0; i < 20_005; i++) r.TakeBoss(new FeedGap(t));
            int n = 0; while (r.NextBossEvent() != null) n++;
            Check(n == 20_000, $"...and keeps at most 20,000 undrained ({n})");
            unsafe
            {
                var buf = new byte[32]; var name = System.Text.Encoding.UTF8.GetBytes("Kromede's Desire"); name.CopyTo(buf, 0);
                fixed (byte* p = buf) Check(NativeDecoder.Text(p, 32) == "Kromede's Desire" && NativeDecoder.BossRecordSize == 256, $"a text field reads back, the record is {NativeDecoder.BossRecordSize} bytes");
                var full = Enumerable.Repeat((byte)'a', 16).ToArray();
                fixed (byte* p = full) Check(NativeDecoder.Text(p, 16) == new string('a', 16), "...a field filled to the end stops at its size");
            }
        }

        // the local recording's keeping (Packets/PacketLog.cs): past 48 hours goes, then the oldest over the size, never the file being written
        {
            var keepFor = PacketLog.KeepFor; var keepBytes = PacketLog.KeepBytes;
            try
            {
                if (Directory.Exists(PacketLog.Dir)) Directory.Delete(PacketLog.Dir, true);
                Directory.CreateDirectory(PacketLog.Dir);
                string F(string name, TimeSpan ago)
                {
                    var p = Path.Combine(PacketLog.Dir, name); File.WriteAllBytes(p, new byte[600]); File.SetLastWriteTimeUtc(p, DateTime.UtcNow - ago); return p;
                }
                var old = F("20261110-01.pcapng", TimeSpan.FromHours(50)); File.WriteAllText(Path.ChangeExtension(old, ".json"), "{}");
                var a = F("20261113-20.pcapng", TimeSpan.FromHours(2)); var b = F("20261113-21.pcapng", TimeSpan.FromHours(1)); var cur = F("20261113-22.pcapng", TimeSpan.Zero);
                PacketLog.KeepBytes = 1500;
                new PacketLog().Prune(cur);
                Check(!File.Exists(old) && !File.Exists(Path.ChangeExtension(old, ".json")) && !File.Exists(a) && File.Exists(b) && File.Exists(cur),
                    "the traffic recording keeps 48 hours and the size limit, oldest out first, never the file being written");
                Check(!PacketLog.Wanted || Environment.GetEnvironmentVariable("KL_PACKET_LOG") != null, "...and is off unless this PC asked for it");
            }
            finally { PacketLog.KeepFor = keepFor; PacketLog.KeepBytes = keepBytes; try { Directory.Delete(PacketLog.Dir, true); } catch (Exception) { } }
        }

        Check(TcpTable.GameServer(new[] { new TcpTable.Link(me, 52139, IPAddress.Parse("34.149.33.84"), 443, 1), new TcpTable.Link(me, 57735, server, 13328, 1), new TcpTable.Link(IPAddress.Loopback, 57066, IPAddress.Loopback, 57065, 1) }) is { RemotePort: 13328 },
            "the game's server connection: not the web (443) and not this PC");
        // the Parser in real time (2026-11-14): what the reader hands over is the Parser's, and on screen's, without delay
        {
            var (ok, line) = KinahLedger.Parser.ParserLatency.ProcessingPath(50);
            Check(ok, line);
            var r = new PacketReader(); var got = new List<BossEvent>();
            r.BossSink = got.Add;
            r.TakeBoss(new BossEngaged(DateTime.UtcNow, "Kaldrix", 1));
            Check(got.Count == 1 && r.NextBossEvent() == null, "...the reader hands a boss event to the Parser as it comes, not to a queue for the next pass");
        }

        Console.WriteLine(_fails == 0 ? "packet self-test passed" : $"packet self-test: {_fails} failed");
        return _fails == 0 ? 0 : 1;
    }

    /// <summary>packet-live: the reader against the game running now, for <paramref name="seconds"/>, each currency change as it comes.</summary>
    public static int Live(IEnumerable<string> processHints, int seconds)
    {
        Ocr.GameWindow.Find(processHints);
        var pids = Ocr.GameWindow.Pids;
        Console.WriteLine($"game processes: {(pids.Count == 0 ? "none" : string.Join(", ", pids))} · Npcap {(PacketSources.NpcapInstalled ? "installed" : "not installed")} · administrator {(PacketSources.IsAdmin() ? "yes" : "no")} · decoder {(NativeDecoder.Available ? "yes" : "no")}");
        using var dec = NativeDecoder.TryOpen();
        if (dec == null) { Console.WriteLine("this build has no decoder (klnet.dll)"); return 1; }
        var link = TcpTable.GameServer(TcpTable.Of(pids));
        if (link == null) { Console.WriteLine("no game server connection: is a character logged in?"); return 1; }
        Console.WriteLine($"game server: {link.Remote}:{link.RemotePort} → {link.Local}:{link.LocalPort}");
        using var src = PacketSources.Open(link, out var why);
        if (src == null) { Console.WriteLine("no packet source: " + why); return 1; }
        Console.WriteLine($"reading with {src.Kind} for {seconds} s");
        var st = new TcpStream { OnData = (d, at) => { dec.Feed(d, at); while (dec.Next() is { } c) Print(c); }, OnGap = dec.Gap };
        long packets = 0;
        var end = DateTime.UtcNow.AddSeconds(seconds);
        while (DateTime.UtcNow < end)
        {
            if (src.Next() is not { } p || TcpSegment.Parse(p.Ip) is not { } s) continue;
            if (s.SrcPort != link.RemotePort || s.DstPort != link.LocalPort || !s.Src.Equals(link.Remote)) continue;
            packets++;
            st.Add(s.Seq, s.Payload, p.At, s.Syn);
        }
        var stats = dec.Stats;
        Console.WriteLine($"{packets:N0} packets, {st.Bytes:N0} bytes, {stats.Messages:N0} messages, {st.Gaps} gaps, {stats.Resyncs} resyncs, {stats.CurrencyMessages} currency messages");
        return packets > 0 ? 0 : 1;
    }

    static void Print(CurrencyChange c) =>
        Console.WriteLine($"  {c.At.ToLocalTime():HH:mm:ss.ff}  {c.What,-12} {(c.First ? "now" : (c.Delta >= 0 ? "+" : "−") + Math.Abs(c.Delta).ToString("N0")),8}  {c.Amount:N0}");

    /// <summary>An IPv4 TCP packet carrying <paramref name="payload"/>.</summary>
    internal static byte[] Ip(IPAddress src, int sport, IPAddress dst, int dport, uint seq, byte[] payload)
    {
        int total = 40 + payload.Length;
        var p = new byte[total];
        p[0] = 0x45; p[2] = (byte)(total >> 8); p[3] = (byte)total; p[8] = 64; p[9] = 6;
        src.GetAddressBytes().CopyTo(p, 12); dst.GetAddressBytes().CopyTo(p, 16);
        p[20] = (byte)(sport >> 8); p[21] = (byte)sport; p[22] = (byte)(dport >> 8); p[23] = (byte)dport;
        p[24] = (byte)(seq >> 24); p[25] = (byte)(seq >> 16); p[26] = (byte)(seq >> 8); p[27] = (byte)seq;
        p[32] = 5 << 4; p[33] = 0x18;
        payload.CopyTo(p, 40);
        return p;
    }

    /// <summary>
    /// The decoder over a capture file: every connection from <paramref name="serverPort"/> in turn (an event or the Abyss is
    /// another server), each its own decoder, every currency change, printed. Needs the decoder in the build.
    /// </summary>
    public static int Replay(string pcapng, int serverPort, Settings? settings = null)
    {
        var bossEvents = new List<BossEvent>();
        if (!NativeDecoder.Available) { Console.WriteLine("this build has no decoder (klnet.dll)"); return 1; }
        var segs = new List<(DateTime At, TcpSegment S)>();
        foreach (var (at, frame, link) in Pcapng.Read(pcapng))
            if (NpcapSource.IpOf(frame, link) is { } ip && TcpSegment.Parse(ip) is { } s && s.SrcPort == serverPort) segs.Add((at, s));
        var seen = new List<CurrencyChange>();
        var batches = new List<ItemBatch>();
        var fights = new FightTally(); var fought = new List<FightTally.Outcome>(); bool readsFights = false;
        var gaps = new List<DateTime>();
        bool reads = false;
        foreach (var conn in segs.GroupBy(x => (x.S.Src.ToString(), x.S.DstPort)).OrderBy(g => g.First().At))
        {
            using var dec = NativeDecoder.TryOpen()!;
            reads = dec.ReadsItems; readsFights = dec.ReadsFights;
            var rows = new List<ItemChange>();
            DateTime last = DateTime.MinValue;
            var st = new TcpStream
            {
                OnData = (d, at) =>
                {
                    last = at;
                    dec.Feed(d, at);
                    while (dec.Next() is { } c) seen.Add(c);
                    rows.Clear();
                    while (dec.NextItem() is { } it) rows.Add(it);
                    batches.AddRange(ItemBatch.Cut(rows));
                    while (dec.NextFight() is { } f) fights.Add(f);
                    while (dec.NextBossEvent() is { } b) bossEvents.Add(b);
                },
                OnGap = () => { dec.Gap(); gaps.Add(last); },
            };
            foreach (var (at, s) in conn) st.Add(s.Seq, s.Payload, at, s.Syn);
            fought.AddRange(fights.Flush());   // the next connection's ids are other entities
            var stats = dec.Stats;
            Console.WriteLine($"{conn.Key.Item1}:{serverPort} → :{conn.Key.DstPort}: {st.Bytes:N0} bytes, {stats.Messages:N0} messages, {st.Gaps} gaps, {stats.Resyncs} resyncs, {stats.CurrencyMessages} currency messages");
        }
        foreach (var c in seen) Print(c);
        foreach (var k in Enum.GetValues<CurrencyKind>())
            Console.WriteLine($"{k}: gained {seen.Where(c => c.What == k && c.Delta > 0).Sum(c => c.Delta):N0} in {seen.Count(c => c.What == k && c.Delta > 0)} changes");
        if (readsFights) ReplayFights(pcapng, fought, fights.RankPoints);
        ReplayBosses(bossEvents);
        if (!reads) { Console.WriteLine("this decoder reads no items (klnet.dll version 1)"); return 0; }
        ReplayItems(pcapng, batches, settings, gaps);
        return 0;
    }

    /// <summary>packet-replay's boss fights: each instance, the group, and each pull with its damage by member (the decoder's, version 4 and later).</summary>
    static void ReplayBosses(List<BossEvent> ev)
    {
        if (ev.Count == 0) { Console.WriteLine("boss fights: none (no Transcendence, Expedition or Sanctuary boss in this recording, or the decoder reads none)"); return; }
        var names = new Dictionary<long, string>();
        var dmg = new Dictionary<long, Dictionary<long, long>>();
        var mine = new Dictionary<long, Dictionary<int, (long Total, int Hits)>>();
        string? boss = null; long open = 0;   // the pull under way: a wave's damage lands on many targets
        foreach (var e in ev)
            switch (e)
            {
                case InstanceEntered i: Console.WriteLine($"{Local(i.At)}  into {i.Name} ({i.Kind})"); break;
                case InstanceLeft l: Console.WriteLine($"{Local(l.At)}  out of the instance"); break;
                case InstanceMode md: Console.WriteLine($"{Local(md.At)}  mode: {md.Mode}"); break;
                case GroupChanged g:
                    foreach (var m in g.Members) names[m.Id] = m.Name;
                    Console.WriteLine($"{Local(g.At)}  group: {string.Join(", ", g.Members.Select(m => $"{m.Name}{(m.You ? " (you)" : "")} {(m.Server.Length > 0 ? $"[{m.Server}] " : "")}{(m.Class.Length > 0 ? m.Class + " " : "")}item level {m.ItemLevel:N0}, Combat Power {m.CombatPower:N0}"))}");
                    break;
                case BossEngaged b: boss = b.Boss; open = b.BossId; dmg[b.BossId] = new(); mine[b.BossId] = new(); Console.WriteLine($"{Local(b.At)}  pull: {b.Boss}"); break;
                case DamageDealt d:
                    long pull = dmg.ContainsKey(d.TargetId) ? d.TargetId : open;
                    if (dmg.TryGetValue(pull, out var by)) by[d.SourceId] = by.GetValueOrDefault(d.SourceId) + d.Amount;
                    if (d.SourceIsYou && mine.TryGetValue(pull, out var sk)) { var w = sk.GetValueOrDefault(d.Skill); sk[d.Skill] = (w.Total + d.Amount, w.Hits + d.Hits); }
                    break;
                case BossEnded x:
                    var t = dmg.GetValueOrDefault(x.BossId) ?? new();
                    long total = t.Values.Sum();
                    Console.WriteLine($"{Local(x.At)}  {x.Outcome}: {boss}, {total:N0} damage: " + string.Join(", ", t.OrderByDescending(kv => kv.Value)
                        .Select(kv => $"{(kv.Key == 0 ? "other" : names.GetValueOrDefault(kv.Key, kv.Key.ToString()))} {kv.Value:N0} ({kv.Value * 100.0 / Math.Max(1, total):F1}%)")));
                    if (mine.GetValueOrDefault(x.BossId) is { Count: > 0 } yours)
                        Console.WriteLine("            your abilities: " + string.Join(", ", yours.OrderByDescending(kv => kv.Value.Total).Take(8)
                            .Select(kv => $"{(kv.Key == 0 ? "over time" : "skill " + kv.Key)} {kv.Value.Total:N0}{(kv.Value.Hits > 0 ? $" in {kv.Value.Hits} hits, {kv.Value.Total / kv.Value.Hits:N0} a hit" : "")}")));
                    break;
            }
    }

    static string Local(DateTime utc) => utc.ToLocalTime().ToString("HH:mm:ss.f");

    /// <summary>
    /// The player's kills, told apart, and deaths, as the tracker would count them from a capture, against the capture's
    /// chat.log (beside it, as kinah-bench's capture.ps1 keeps it): its Rank Point and Soul Points lines the reader counted,
    /// one a kill, and its resurrection lines, one a death.
    /// </summary>
    static void ReplayFights(string pcapng, List<FightTally.Outcome> fought, long rank)
    {
        foreach (var o in fought)
            Console.WriteLine($"  {o.At.ToLocalTime():HH:mm:ss.ff}  {(o.Death ? $"died{(o.Name.Length > 0 ? " to " + o.Name : "")}" : $"killed {(o.Player ? "the player " + o.Name : $"a monster{(o.MonsterId != 0 ? $" ({o.MonsterId})" : "")}")}{(o.Credited ? ", no killer named, Rank Points rose with it" : "")}")}");
        var kills = fought.Where(o => !o.Death).ToList();
        int players = kills.Count(o => o.Player), deaths = fought.Count(o => o.Death);
        Console.WriteLine($"kills: {kills.Count} ({players} players, {kills.Count - players} monsters; {kills.Count(o => o.Credited)} with no killer named), {deaths} deaths ({fought.Count(o => o.Death && o.Player)} to players), Rank Points +{rank:N0}");
        var log = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(pcapng)) ?? ".", "chat.log");
        if (!File.Exists(log)) return;
        int rankLines = 0, soulLines = 0, rez = 0;
        foreach (var l in File.ReadLines(log))
        {
            if (System.Text.RegularExpressions.Regex.IsMatch(l, @"^\S+\s+ok\s+Acquired .*R\w{2,3}.?\s*Point", System.Text.RegularExpressions.RegexOptions.IgnoreCase)) rankLines++;
            else if (System.Text.RegularExpressions.Regex.IsMatch(l, @"^\S+\s+ok\s+Obtained .*Soul\s*Points", System.Text.RegularExpressions.RegexOptions.IgnoreCase)) soulLines++;
            if (System.Text.RegularExpressions.Regex.IsMatch(l, @"Character has resurrected", System.Text.RegularExpressions.RegexOptions.IgnoreCase)) rez++;
        }
        Console.WriteLine($"chat.log: {rankLines} Rank Point lines and {soulLines} Soul Points lines counted (a kill each), {rez} resurrection lines");
    }

    /// <summary>
    /// The items and crafts the tracker would count from a capture's inventory updates. The capture's chat.log, when beside
    /// it (kinah-bench's capture.ps1 keeps the tracker's reads.log lines of those minutes), says when crafting was under way
    /// (the crafting panel's and window's lines) and which pickups the chat read; without it nothing is crafting. The gate
    /// a live session adds (a kill or gather nearby, not in a base, no Market or reward screen) is left out here.
    /// </summary>
    static void ReplayItems(string pcapng, List<ItemBatch> batches, Settings? settings, List<DateTime> gaps)
    {
        var items = ItemDictionary.LoadOrEmpty((settings ?? new Settings()).ItemsFile);
        var dir = Path.GetDirectoryName(Path.GetFullPath(pcapng)) ?? ".";
        var crafting = new List<DateTime>();
        Func<DateTime, string> recipeAt = _ => "";
        var chat = new List<(DateTime At, long Id, int N, string Text)>();
        var log = Path.Combine(dir, "chat.log");
        var info = Path.Combine(dir, "info.json");
        if (File.Exists(log) && File.Exists(info))
        {
            using var j = System.Text.Json.JsonDocument.Parse(File.ReadAllText(info));
            var day = DateTime.Parse(j.RootElement.GetProperty("localDate").GetString()!, System.Globalization.CultureInfo.InvariantCulture);
            int off = j.RootElement.GetProperty("utcOffsetMinutes").GetInt32();
            var line = new System.Text.RegularExpressions.Regex(@"^﻿?(\d\d):(\d\d):(\d\d\.\d)\s+(\S+)\s+(.*)$");
            var acq = new System.Text.RegularExpressions.Regex(@"^(?:Acquired (?<n1>.+?) x(?<c>\d+)|(?<n2>.+?) acquired)\W*$");
            DateTime last = DateTime.MinValue;
            var panel = new List<(DateTime At, string Recipe)>();
            foreach (var l in File.ReadLines(log))
            {
                var m = line.Match(l);
                if (!m.Success) continue;
                var at = day.AddHours(int.Parse(m.Groups[1].Value)).AddMinutes(int.Parse(m.Groups[2].Value))
                    .AddSeconds(double.Parse(m.Groups[3].Value, System.Globalization.CultureInfo.InvariantCulture)).AddMinutes(-off);
                at = DateTime.SpecifyKind(at, DateTimeKind.Utc);
                // capture.ps1 takes reads.old.log's lines of the same time of day too, from an earlier day: only the last run that goes forward is this capture's
                if ((last - at).TotalSeconds > 60) { crafting.Clear(); chat.Clear(); panel.Clear(); }
                last = at;
                string tag = m.Groups[4].Value, text = System.Text.RegularExpressions.Regex.Replace(m.Groups[5].Value, @"\s{2,}\(.*$", "").Trim();
                if (text.StartsWith("crafting panel") || text.StartsWith("crafting window") || text.StartsWith("crafting:")) crafting.Add(at);
                var pr = System.Text.RegularExpressions.Regex.Match(text, @"^crafting panel: (?<r>.+?), this queue's recipe");
                if (pr.Success) panel.Add((at, pr.Groups["r"].Value));
                if (tag != "ok" || System.Text.RegularExpressions.Regex.IsMatch(text, "Kina|Rank|Abyss|Soul|Point", System.Text.RegularExpressions.RegexOptions.IgnoreCase)) continue;
                var a = acq.Match(text);
                if (!a.Success) continue;
                var name = a.Groups["n1"].Success ? a.Groups["n1"].Value : a.Groups["n2"].Value;
                if ((items.Find(name) ?? items.Guess(name)) is long id) chat.Add((at, id, a.Groups["c"].Success ? int.Parse(a.Groups["c"].Value) : 1, text));
            }
            Console.WriteLine($"chat.log: {chat.Count} item pickup lines, {crafting.Count} crafting lines");
            recipeAt = t => panel.LastOrDefault(p => p.At <= t.AddSeconds(3)).Recipe ?? "";
        }
        bool CraftingAt(DateTime t) => crafting.Any(c => (t - c).TotalSeconds is >= -3 and <= CraftLedger.PanelSeconds);
        var crafts = new PacketCrafts(items.NameOf, id => items.Get(id) != null) { GapBetween = (a, b) => gaps.Any(g => g >= a.AddSeconds(-1) && g <= b.AddSeconds(1)) };
        var made = new List<PacketCrafts.Outcome>();
        int pickups = 0, withLine = 0; long count = 0;
        var used = new HashSet<int>();
        void Craft(IEnumerable<PacketCrafts.Outcome> os)
        {
            foreach (var o in os)
            {
                made.Add(o);
                Console.WriteLine($"  {o.At.ToLocalTime():HH:mm:ss.ff}  craft {(o.Lost ? "not known" : o.Combo ? "combo won" : o.Ok ? "worked" : "failed")} · {(o.Recipe.Length > 0 ? o.Recipe : "?")}{(o.Makes is int mk ? $" · makes {mk}" : "")}  ({o.Why})");
            }
        }
        foreach (var b in batches.OrderBy(b => b.At))
        {
            if (crafts.Open && (b.At - crafts.LastAt).TotalSeconds > 20 && !CraftingAt(b.At)) Craft(crafts.End(crafts.LastAt));
            string said = string.Join(", ", b.Net().Select(n => $"{items.NameOf(n.ItemId)} {(n.Unknown ? "(first seen)" : n.Net >= 0 ? $"+{n.Net:N0}" : $"−{-n.Net:N0}")}"));
            if (CraftingAt(b.At))
            {
                var os = crafts.Take(b, recipeAt(b.At));
                Console.WriteLine($"  {b.At.ToLocalTime():HH:mm:ss.ff}  crafting: {said} (tries {crafts.Tally.Tries}, worked {crafts.Tally.Ok}, failed {crafts.Tally.Failed})");
                Craft(os);
                continue;
            }
            if (b.Fell) { Console.WriteLine($"  {b.At.ToLocalTime():HH:mm:ss.ff}  left out ({said}: something went down with it)"); continue; }
            foreach (var (id, n, unknown) in b.Net())
            {
                if (items.Get(id) == null) continue;   // not on the item list (soul crystals): the chat's
                if (unknown) { Console.WriteLine($"  {b.At.ToLocalTime():HH:mm:ss.ff}  first seen · {items.NameOf(id)} (its pickup is the chat's)"); continue; }
                if (n <= 0) continue;
                int ci = -1;
                for (int i = 0; i < chat.Count; i++)
                    if (!used.Contains(i) && chat[i].Id == id && (chat[i].At - b.At).TotalSeconds is >= -0.5 and <= 6) { ci = i; break; }
                if (ci >= 0) { used.Add(ci); withLine++; }
                pickups++; count += n;
                Console.WriteLine($"  {b.At.ToLocalTime():HH:mm:ss.ff}  pickup · {items.NameOf(id)} +{n:N0}  ({(ci >= 0 ? $"chat {chat[ci].At.ToLocalTime():HH:mm:ss.f}: {chat[ci].Text}" : "no chat line")})");
            }
        }
        if (crafts.Open) Craft(crafts.End(crafts.LastAt));
        Console.WriteLine($"items: {pickups} pickups (+{count:N0}), {withLine} with a chat line, {pickups - withLine} the chat didn't read; chat item lines no pickup matched: {chat.Count - used.Count}");
        foreach (var g in made.GroupBy(o => o.Recipe))
        {
            int ok = g.Count(o => o.Ok && !o.Combo), combos = g.Count(o => o.Combo);
            int makes = g.Where(o => o.Ok && !o.Combo).Sum(o => o.Makes ?? 0) + combos * g.Where(o => o.Ok && !o.Combo).Select(o => o.Makes ?? 0).DefaultIfEmpty(0).Max();
            Console.WriteLine($"crafts: {(g.Key.Length > 0 ? g.Key : "?")} {ok} worked, {g.Count(o => !o.Ok && !o.Lost)} failed, {g.Count(o => o.Lost)} not known (bytes lost), {combos} combos won, {makes} made");
        }
    }
}

/// <summary>Just enough pcapng to read pktmon's and Wireshark's captures and to write a test one (Ethernet, microseconds).</summary>
public static class Pcapng
{
    public static IEnumerable<(DateTime At, byte[] Frame, int Link)> Read(string path)
    {
        byte[] d;   // a file the tracker is still writing reads too, up to its last whole block
        using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete)) { d = new byte[fs.Length]; fs.ReadExactly(d); }
        int pos = 0;
        var ifaces = new List<(int Link, double Res)>();
        while (pos + 12 <= d.Length)
        {
            uint type = BitConverter.ToUInt32(d, pos), len = BitConverter.ToUInt32(d, pos + 4);
            if (len < 12 || pos + len > d.Length) yield break;
            int body = pos + 8;
            if (type == 0x0A0D0D0A) ifaces.Clear();
            else if (type == 1)
            {
                int link = BitConverter.ToUInt16(d, body); double res = 1e-6;
                for (int o = body + 8; o + 4 <= pos + len - 4;)
                {
                    int code = BitConverter.ToUInt16(d, o), ol = BitConverter.ToUInt16(d, o + 2);
                    if (code == 0) break;
                    if (code == 9 && ol >= 1) { byte v = d[o + 4]; res = (v & 0x80) != 0 ? Math.Pow(2, -(v & 0x7F)) : Math.Pow(10, -v); }
                    o += 4 + ((ol + 3) & ~3);
                }
                ifaces.Add((link, res));
            }
            else if (type == 6 && ifaces.Count > 0)
            {
                int iid = (int)BitConverter.ToUInt32(d, body);
                ulong ts = ((ulong)BitConverter.ToUInt32(d, body + 4) << 32) | BitConverter.ToUInt32(d, body + 8);
                int cap = (int)BitConverter.ToUInt32(d, body + 12);
                var (link, res) = ifaces[iid < ifaces.Count ? iid : 0];
                var at = DateTime.UnixEpoch.AddTicks((long)(ts * res * TimeSpan.TicksPerSecond));
                yield return (at, d.AsSpan(body + 20, Math.Min(cap, (int)len - 32)).ToArray(), link);
            }
            pos += (int)len;
        }
    }

    /// <summary>A whole capture file of Ethernet frames at once (the tests); <see cref="PcapngWriter"/> writes one as it comes.</summary>
    public static void Write(string path, IEnumerable<(DateTime At, byte[] Frame)> frames)
    {
        using var w = new PcapngWriter(path, 1);
        foreach (var (at, f) in frames) w.Add(at, f);
    }
}

/// <summary>
/// A pcapng file written frame by frame, as Wireshark and pktmon's etl2pcap write one: a section header, one interface
/// (its link type, microsecond times) and an enhanced packet block per frame. Flushed now and then, so a recording cut short
/// keeps what it had.
/// </summary>
public sealed class PcapngWriter : IDisposable
{
    readonly FileStream _f;
    readonly BinaryWriter _w;
    DateTime _flushed = DateTime.UtcNow;
    public long Frames { get; private set; }
    public long Bytes => _f.Position;

    public PcapngWriter(string path, int linkType, int snapLen = 0)
    {
        _f = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.Read | FileShare.Delete);   // readable while it is written
        _w = new BinaryWriter(_f);
        // section header: byte-order magic, version 1.0, section length unknown (-1)
        Block(0x0A0D0D0A, w => { w.Write(0x1A2B3C4Du); w.Write((ushort)1); w.Write((ushort)0); w.Write(-1L); });
        // interface description: link type, reserved, snap length (0: no limit); no options, so microseconds
        Block(1, w => { w.Write((ushort)linkType); w.Write((ushort)0); w.Write((uint)snapLen); });
    }

    /// <summary>One frame as captured; <paramref name="wireLength"/> is its length on the wire when more than was kept.</summary>
    public void Add(DateTime atUtc, byte[] frame, int wireLength = 0)
    {
        ulong us = (ulong)((atUtc - DateTime.UnixEpoch).Ticks / 10);
        Block(6, w =>
        {
            w.Write(0u); w.Write((uint)(us >> 32)); w.Write((uint)us);
            w.Write((uint)frame.Length); w.Write((uint)Math.Max(wireLength, frame.Length)); w.Write(frame);
        });
        Frames++;
        if ((DateTime.UtcNow - _flushed).TotalSeconds >= 2) { _w.Flush(); _flushed = DateTime.UtcNow; }
    }

    readonly MemoryStream _body = new();
    void Block(uint type, Action<BinaryWriter> body)
    {
        _body.SetLength(0);
        using (var bw = new BinaryWriter(_body, System.Text.Encoding.UTF8, leaveOpen: true)) body(bw);
        int n = (int)_body.Length, pad = (4 - n % 4) % 4, len = 12 + n + pad;
        _w.Write(type); _w.Write(len); _w.Write(_body.GetBuffer(), 0, n);
        for (int i = 0; i < pad; i++) _w.Write((byte)0);
        _w.Write(len);
    }

    public void Dispose() { _w.Flush(); _w.Dispose(); }
}

/// <summary>A decoder that reads currencies only: the interface's defaults for items, kills and boss fights.</summary>
sealed class NoBosses : IGameDecoder
{
    public void Reset() { }
    public void Gap() { }
    public void Feed(byte[] data, DateTime at) { }
    public CurrencyChange? Next() => null;
    public (long Messages, long Bundles, int Resyncs, int CurrencyMessages) Stats => default;
    public void Dispose() { }
}
