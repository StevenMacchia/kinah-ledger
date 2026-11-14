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

        // gathering (0.10.80): each attempt from the game's messages is an attempt of its node's skill, a node when it is the
        // first try on it, an upgrade or a fail as the game says; the screen's races for the same attempts are set aside and
        // covered by them; a race the traffic never showed counts from the screen; three such and the screen counts again
        {
            const long Ore = 610620013, Fine = 610620014, Odyle = 610610013;
            var items = ItemDictionary.LoadOrEmpty(settings.ItemsFile);
            var reader = new PacketReader(); reader.ReadingForTest();
            var s = new TrackerSession(null, settings, items, "Test") { Packets = reader };
            s.Engine.SetZone("Ellun River Midstream");
            var t0 = DateTime.UtcNow.AddMinutes(-4);
            GatherResult G(double at, long node, long kind, GatherEnd end, long item, int n, int attempt) => new(t0.AddSeconds(at), node, kind, end, item, n, attempt);
            reader.GathersForTest(G(0, 33768, 4, GatherEnd.Worked, Ore, 1, 1), G(11, 33768, 4, GatherEnd.Failed, Ore, 0, 2), G(21, 33768, 4, GatherEnd.Upgrade, Fine, 1, 3),
                G(95, 20373, 0, GatherEnd.Worked, Ore, 1, 0), G(120, 41201, 5, GatherEnd.Worked, Odyle, 1, 1));
            s.DrainPacketsForTest();
            // the screen's races of the same attempts, then one of its own that the traffic never showed
            s.GatherOutcome(t0.AddSeconds(0.3), true, false); s.GatherOutcome(t0.AddSeconds(12.9), false, false);
            s.GatherOutcome(t0.AddSeconds(21.4), true, true); s.GatherOutcome(t0.AddSeconds(95.2), true, false);
            s.GatherOutcome(t0.AddSeconds(150), true, false);
            reader.AgreeGathersForTest(DateTime.UtcNow); s.DrainPacketsForTest();
            var e = s.Engine; var tot = e.Totals(); var ns = e.NodeSnapshot(); var sk = e.SkillSnapshot();
            string Ns(string n) => ns.TryGetValue(n, out var x) ? $"{x.Ok}/{x.Failed} up {x.Upgrades} nodes {x.Visits}" : "-";
            Check(tot.Nodes == 6 && tot.Failed == 1 && e.Upgrades == 1 && e.NodeVisits == 3 && Ns("Orichalcum Ore") == "3/1 up 1 nodes 2" && Ns("Odyle") == "2/0 up 0 nodes 1"
                && string.Join(" ", reader.GatherVerdicts) == "covered covered covered covered not shown" && reader.GathersBroken == null,
                $"gathering from the game's messages: {tot.Nodes} tries ({tot.Failed} failed, {e.Upgrades} upgrade), {e.NodeVisits} nodes; Orichalcum Ore {Ns("Orichalcum Ore")}, Odyle {Ns("Odyle")} (the last a race the traffic never showed, counted from the screen); the screen's races: {string.Join(" ", reader.GatherVerdicts)}");
            // today.json keeps the new figures: a restart carries them on
            var carry = System.Text.Json.JsonSerializer.Deserialize<SessionEngine.Carry>(System.Text.Json.JsonSerializer.Serialize(e.ToCarry("2026-11-13"), SessionEngine.CarryJson), SessionEngine.CarryJson)!;
            var e2 = new SessionEngine(items); e2.Seed(carry);
            var sk2 = e2.SkillSnapshot(); var ns2 = e2.NodeSnapshot();
            Check(e2.Upgrades == 1 && e2.NodeVisits == 3 && sk2.Values.Sum(x => x.Visits) == 3 && sk2.Values.Sum(x => x.Upgrades) == 1 && ns2["Orichalcum Ore"] is { Upgrades: 1, Visits: 2 },
                $"...and today.json carries them across a restart: {e2.Upgrades} upgrade, {e2.NodeVisits} nodes");
            // three races in five minutes the traffic never showed: gathering goes back to the screen
            var r2 = new PacketReader(); r2.ReadingForTest(); r2.GathersForTest();
            var s2 = new TrackerSession(null, settings, items, "Test") { Packets = r2 };
            s2.Engine.SetZone("Ellun River Midstream");
            foreach (var at in new[] { 0.0, 30, 60 }) s2.GatherOutcome(t0.AddSeconds(at), true, false);
            r2.AgreeGathersForTest(DateTime.UtcNow); s2.DrainPacketsForTest();
            int handed = s2.Engine.Totals().Nodes;
            s2.GatherOutcome(t0.AddSeconds(200), true, false);   // now the screen's at once
            r2.GathersForTest(G(210, 1, 4, GatherEnd.Worked, Ore, 1, 1)); s2.DrainPacketsForTest();   // and the traffic's left out
            Check(handed == 3 && s2.Engine.Totals().Nodes == 4 && r2.GathersBroken != null,
                $"...three races the traffic never showed in five minutes: counted from the screen ({handed}), and gathering is the screen's again ({s2.Engine.Totals().Nodes} after one more race and one game message; \"{r2.GathersBroken}\")");
        }

        // the tracker's recordings of 2026-11-13 (kinah-bench/packets/rec-*) through this build's decoder: the 22 attempts
        // Steven made, 18:50 to 20:03, with their exact items and counts (the screen logged 19 of them)
        {
            string? bench = Environment.GetEnvironmentVariable("KL_BENCH");
            for (var d = new DirectoryInfo(AppContext.BaseDirectory); bench == null && d != null; d = d.Parent)
                if (Directory.Exists(Path.Combine(d.FullName, "kinah-bench", "packets"))) bench = Path.Combine(d.FullName, "kinah-bench", "packets");
            var recs = new[] { "rec-20261113-18", "rec-20261113-19", "rec-20261113-19-2", "rec-20261113-20" }.Select(r => bench == null ? "" : Path.Combine(bench, r, "capture.pcapng")).ToList();
            if (!NativeDecoder.Available || NativeDecoder.Version < 7) Console.WriteLine("skip  gathering in the recordings: this build's decoder reads no gathering");
            else if (recs.Any(r => !File.Exists(r))) Console.WriteLine("skip  gathering in the recordings: kinah-bench/packets/rec-20261113-* not found (KL_BENCH)");
            else
            {
                var all = recs.SelectMany(r => ReplayGathers(r, 13328)).ToList();
                const long O = 610620013, F = 610620014, P = 610620015, D = 610610013;
                var want = new (GatherEnd E, long Item, int N)[] { (GatherEnd.Worked, O, 1), (GatherEnd.Failed, O, 0), (GatherEnd.Upgrade, F, 1), (GatherEnd.Upgrade, F, 1), (GatherEnd.Worked, O, 1), (GatherEnd.Worked, O, 1),
                    (GatherEnd.Worked, D, 1), (GatherEnd.Worked, D, 1), (GatherEnd.Worked, D, 1), (GatherEnd.Worked, D, 1), (GatherEnd.Worked, D, 1), (GatherEnd.Worked, D, 1),
                    (GatherEnd.Worked, O, 1), (GatherEnd.Worked, O, 1), (GatherEnd.Worked, O, 1), (GatherEnd.Upgrade, F, 1), (GatherEnd.Worked, O, 1), (GatherEnd.Worked, O, 1),
                    (GatherEnd.Worked, O, 1), (GatherEnd.Worked, O, 1), (GatherEnd.Upgrade, P, 1), (GatherEnd.Worked, O, 1) };
                bool same = all.Count == want.Length && all.Zip(want).All(x => x.First.Outcome == x.Second.E && x.First.ItemId == x.Second.Item && x.First.Count == x.Second.N);
                Check(same && all.Select(g => g.NodeId).Distinct().Count() == 8,
                    $"gathering in the recordings of 2026-11-13: {all.Count} attempts (expected 22) on {all.Select(g => g.NodeId).Distinct().Count()} nodes, {all.Count(g => g.Outcome == GatherEnd.Upgrade)} upgrades and {all.Count(g => g.Outcome == GatherEnd.Failed)} fail, items exact: {same}");
            }
        }

        // the inventory (0.10.81): the game's snapshot makes it known, every change after it keeps it exact, a new connection
        // makes it unknown again; its worth at the known prices, the sellable part after the cut, the change since the first
        {
            var items = ItemDictionary.LoadOrEmpty(settings.ItemsFile);
            long Id(string n) => items.IdOf(n);
            var t0 = DateTime.UtcNow.AddMinutes(-10);
            var r = new PacketReader(); r.ReadingForTest();
            var bag = r.Bag;
            bool unknownFirst = bag.Totals() == null;
            r.BagForTest(new BagRecord(t0, BagRecord.Begin, 5, 0, 0, 0, 0, 0, null),
                new BagRecord(t0, BagRecord.Stack, 0, 1, 10, 4_727_973, 1, 0, CurrencyKind.Kinah),
                new BagRecord(t0, BagRecord.Stack, 0, Id("Orichalcum Ore"), 11, 31, 1, 40, null),
                new BagRecord(t0, BagRecord.Stack, 0, Id("Orichalcum Ore"), 12, 742, 18, 79, null),
                new BagRecord(t0, BagRecord.Stack, 0, Id("Life Potion (Bound)"), 13, 364, 1, 2, null),
                new BagRecord(t0, BagRecord.Stack, 0, Id("Orichalcum Guard"), 14, 1, BagBook.Worn, 2, null, 12),
                new BagRecord(t0, BagRecord.End, 5, 0, 0, 0, 0, 0, null));
            var tot = bag.Totals()!;
            r.ItemsForTest(new ItemChange(t0.AddSeconds(30), Id("Orichalcum Ore"), ItemOp.Changed, 11, 1, 32, false, 1, 1, false),
                new ItemChange(t0.AddSeconds(31), Id("Odyle"), ItemOp.NewStack, 15, 1, 1, false, 2, 1, false),
                new ItemChange(t0.AddSeconds(32), Id("Life Potion (Bound)"), ItemOp.Gone, 13, -364, 0, false, 3, 1, true));
            r.AmountForTest(CurrencyKind.Kinah, 4_727_973, t0.AddSeconds(29)); r.AmountForTest(CurrencyKind.Kinah, 4_728_099, t0.AddSeconds(30));
            var after = bag.Totals()!;
            Check(unknownFirst && tot.GetValueOrDefault(Id("Orichalcum Ore")) == 773 && !tot.ContainsKey(Id("Orichalcum Guard")) && bag.Totals(worn: true)!.ContainsKey(Id("Orichalcum Guard"))
                && bag.WornGear() is [{ Slot: 2, Level: 12 }]
                && after.GetValueOrDefault(Id("Orichalcum Ore")) == 774 && after.GetValueOrDefault(Id("Odyle")) == 1 && !after.ContainsKey(Id("Life Potion (Bound)")) && bag.Money(CurrencyKind.Kinah) == 4_728_099 && bag.LastRows == (5, 5),
                $"the inventory: unknown before its snapshot, then Orichalcum Ore {tot.GetValueOrDefault(Id("Orichalcum Ore"))} (two stacks), the gear worn apart; after the changes {after.GetValueOrDefault(Id("Orichalcum Ore"))} ore, {after.GetValueOrDefault(Id("Odyle"))} Odyle, the potions gone, Kinah {bag.Money(CurrencyKind.Kinah):N0}");
            // its worth: tradable at the price, bound at its tradable twin's, unpriced counted; the change since the first snapshot
            var prices = new Dictionary<string, long> { ["Orichalcum Ore"] = 2_000, ["Life Potion"] = 100 };
            var w = Session.BagValue.Of(after, bag.FirstTotals, items, n => prices.TryGetValue(n, out var v) ? v : null, 0.9);
            Check(w.Total == 774 * 2_000 && w.Sellable == (long)Math.Round(774 * 2_000 * 0.9) && w.Unpriced == 1 && w.Change == 2_000 - 364 * 100 && w.Lines[0].Name == "Orichalcum Ore",
                $"...worth {w.Total:N0}, sellable {w.Sellable:N0} after a 10% cut, {w.Unpriced} kind without a price (Odyle), {w.Change:N0} since the first (one ore up, the bound potions gone at their twin's price)");
            // a bound item counts at its twin's price and never as sellable
            var wb = Session.BagValue.Of(new Dictionary<long, long> { [Id("Life Potion (Bound)")] = 10 }, null, items, n => prices.TryGetValue(n, out var v) ? v : null, 0.9);
            Check(wb.Total == 1_000 && wb.Sellable == 0 && wb.Unpriced == 0 && wb.Change == null && wb.Lines[0].Bound, $"...a bound item: worth {wb.Total:N0} at its twin's price, sellable {wb.Sellable}");
            // a new connection: unknown until its own snapshot; the first snapshot stays the session's start
            r.Bag.Unknown();
            Check(bag.Totals() == null && bag.FirstAt == t0 && bag.FirstTotals.GetValueOrDefault(Id("Orichalcum Ore")) == 773, "...a new connection makes it unknown again, and the session's start stays the first snapshot");
        }

        // the inventory in the recordings of 2026-11-13 through this build's decoder: the 18:49:42 snapshot kept up with every
        // change is the 20:01:47 snapshot, item by item
        {
            string? bench = Environment.GetEnvironmentVariable("KL_BENCH");
            for (var d = new DirectoryInfo(AppContext.BaseDirectory); bench == null && d != null; d = d.Parent)
                if (Directory.Exists(Path.Combine(d.FullName, "kinah-bench", "packets"))) bench = Path.Combine(d.FullName, "kinah-bench", "packets");
            var recs = new[] { "rec-20261113-18", "rec-20261113-19", "rec-20261113-19-2", "rec-20261113-20" }.Select(x => bench == null ? "" : Path.Combine(bench, x, "capture.pcapng")).ToList();
            if (!NativeDecoder.Available || NativeDecoder.Version < 8) Console.WriteLine("skip  the inventory in the recordings: this build's decoder reads no inventory");
            else if (recs.Any(x => !File.Exists(x))) Console.WriteLine("skip  the inventory in the recordings: kinah-bench/packets/rec-20261113-* not found (KL_BENCH)");
            else
            {
                var books = ReplayBags(recs, 13328);
                var a = books.FirstOrDefault(b => b.Snapshots > 0); var b2 = books.LastOrDefault(b => b.Snapshots > 0);
                int agree = 0, differ = 0;
                if (a != null && b2 != null && a != b2)
                {
                    var x = a.Totals()!; var y = b2.FirstTotals;   // the first connection at its end; the second at its snapshot (gear worn apart in both)
                    foreach (var id in x.Keys.Union(y.Keys)) { if (x.GetValueOrDefault(id) == y.GetValueOrDefault(id)) agree++; else differ++; }
                    bool kinahSame = a.Money(CurrencyKind.Kinah) == 4_922_940;   // the Kinah the 20:01:47 snapshot holds
                    // the gear worn and its levels, against Steven's screenshot of 22:20 (the same gear): +12 the guard, +11 a ring,
                    // +1 and +2 the two teal pieces, +10 the other fifteen, and the weapon's 18 (a badge in the game)
                    var worn = b2.WornGear()!.ToDictionary(g => g.Slot, g => g.Level);
                    var want = new Dictionary<int, int> { [1] = 18, [2] = 12, [13] = 11, [23] = 1, [24] = 2 };
                    foreach (var sl in new[] { 3, 4, 5, 6, 7, 8, 10, 11, 12, 14, 15, 16, 17, 19, 22 }) want[sl] = 10;
                    Check(worn.Count == 20 && want.All(kv => worn.GetValueOrDefault(kv.Key) == kv.Value),
                        $"the gear worn in the recordings, by slot: {string.Join(" ", worn.OrderBy(kv => kv.Key).Select(kv => $"{kv.Key}:+{kv.Value}"))} (Steven's screenshot: 19 of 19 levels)");
                    Check(differ == 0 && agree > 200 && kinahSame && a.LastRows.Said == a.LastRows.Read && b2.LastRows.Said == b2.LastRows.Read,
                        $"the inventory in the recordings: the 18:49 snapshot ({a.LastRows.Read} of {a.LastRows.Said} stacks) kept up with every change equals the 20:01 one ({b2.LastRows.Read} of {b2.LastRows.Said}) for {agree} items, {differ} differ; Kinah at the first connection's end {a.Money(CurrencyKind.Kinah):N0} (the second snapshot says 4,922,940)");
                }
                else Check(false, $"the inventory in the recordings: {books.Count(b => b.Snapshots > 0)} connections with a snapshot (expected 2)");
            }
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
        BossTimerChecks(t);
        TimerBoardChecks();
        EventTimerChecks(settings);

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

    /// <summary>This build's decoder over a capture: every gathering attempt of the player's, each connection its own decoder.</summary>
    internal static List<GatherResult> ReplayGathers(string pcapng, int serverPort)
    {
        var got = new List<GatherResult>();
        if (!NativeDecoder.Available) return got;
        var segs = new List<(DateTime At, TcpSegment S)>();
        foreach (var (at, frame, link) in Pcapng.Read(pcapng))
            if (NpcapSource.IpOf(frame, link) is { } ip && TcpSegment.Parse(ip) is { } s && s.SrcPort == serverPort) segs.Add((at, s));
        foreach (var conn in segs.GroupBy(x => (x.S.Src.ToString(), x.S.DstPort)).OrderBy(g => g.First().At))
        {
            using var dec = NativeDecoder.TryOpen()!;
            var st = new TcpStream { OnData = (d, at) => { dec.Feed(d, at); while (dec.Next() is not null) { } while (dec.NextItem() is not null) { } while (dec.NextFight() is not null) { } while (dec.NextGather() is { } g) got.Add(g); }, OnGap = dec.Gap };
            foreach (var (at, s) in conn) st.Add(s.Seq, s.Payload, at, s.Syn);
        }
        return got.OrderBy(g => g.At).ToList();
    }

    /// <summary>This build's decoder over capture files, one stream per connection across the files: each connection's inventory at its end.</summary>
    internal static List<BagBook> ReplayBags(IEnumerable<string> files, int serverPort)
    {
        var segs = new List<(DateTime At, TcpSegment S)>();
        foreach (var f in files)
            foreach (var (at, frame, link) in Pcapng.Read(f))
                if (NpcapSource.IpOf(frame, link) is { } ip && TcpSegment.Parse(ip) is { } s && s.SrcPort == serverPort) segs.Add((at, s));
        var books = new List<BagBook>();
        foreach (var conn in segs.GroupBy(x => (x.S.Src.ToString(), x.S.DstPort)).OrderBy(g => g.First().At))
        {
            using var dec = NativeDecoder.TryOpen()!;
            var book = new BagBook();
            var st = new TcpStream
            {
                OnData = (d, at) =>
                {
                    dec.Feed(d, at);
                    while (dec.NextBag() is { } b) book.Take(b);
                    while (dec.Next() is { } c) book.Take(c);
                    while (dec.NextItem() is { } i) book.Take(i);
                    while (dec.NextFight() is not null) { }
                },
                OnGap = dec.Gap,
            };
            foreach (var (at, s) in conn.OrderBy(x => x.At)) st.Add(s.Seq, s.Payload, at, s.Syn);
            books.Add(book);
        }
        return books;
    }

    /// <summary>
    /// World boss timers (Session/BossTimers.cs) from boss sightings as the decoder hands them out, the server's clock 15 s
    /// behind this PC's as it was on 2026-11-13: the countdown follows the server's time left, not the PC's wall time; a live
    /// boss is up while the map sends the list and "up when last seen" after; a spawn that passed is due; the same reading
    /// again changes nothing, a kill does; the order; the saved copy read back after a restart, with its age.
    /// </summary>
    /// <summary>
    /// Timed events (Session/EventTimers.cs, InstanceLog.GameSaid; 0.10.83): the schedule counted on the game's clock (UTC+9,
    /// 14.6 s behind this PC's as on 2026-11-13), on-now windows and weekdays; the event the player is in, part by part, from
    /// its own server; and an event run that starts at the game's message, keeps its start and name over the minimap's, is
    /// left when the game says so (the minimap's lagging reads neither undo that nor start a new run) and closes after the
    /// reward time, as the 17:01 Defend Shugo Merchants run of 2026-11-13 came.
    /// </summary>
    static void EventTimerChecks(Settings settings)
    {
        var lag = TimeSpan.FromSeconds(14.6);
        var server = new DateTime(2026, 11, 14, 1, 29, 0, DateTimeKind.Utc);   // Sat 10:29 on the game's clock, Fri 20:29 at UTC-5
        string Say(EventTimers.Row r) => r.OnNow ? $"{r.Event.Name} on, {r.When}" : $"{r.Event.Name} in {r.When} at {r.StartPc:HH:mm:ss.f}Z";
        var rows = EventTimers.Schedule.Select(r => EventTimers.RowOf(r, server, lag)).ToList();
        Check(string.Join(" | ", rows.Select(Say)) == "Shugo Festival in 31m 00s at 02:00:14.6Z | Dimensional Invasion in 1m 00s at 01:30:14.6Z | Spacetime Rift in 1h 31m 00s at 03:00:14.6Z | Artifact Siege in 10h 31m 00s at 12:00:14.6Z",
            $"event timers: {string.Join(" | ", rows.Select(Say))} (the starts on this PC's clock, 14.6 s after the game's)");
        var at3 = EventTimers.RowOf(EventTimers.Schedule[0], new DateTime(2026, 11, 14, 2, 3, 0, DateTimeKind.Utc), lag);
        var at6 = EventTimers.RowOf(EventTimers.Schedule[0], new DateTime(2026, 11, 14, 2, 6, 0, DateTimeKind.Utc), lag);
        Check(at3 is { OnNow: true, When: "started 3 min ago" } && at6 is { OnNow: false, When: "54m 00s" }, $"...the Shugo Festival 3 min past the hour: {Say(at3)}; at 6 past: {Say(at6)}");
        var siegeOn = EventTimers.RowOf(EventTimers.Schedule[3], new DateTime(2026, 11, 14, 12, 5, 0, DateTimeKind.Utc), lag);
        var siegeNext = EventTimers.RowOf(EventTimers.Schedule[3], new DateTime(2026, 11, 14, 12, 25, 0, DateTimeKind.Utc), lag);
        Check(siegeOn.OnNow && !siegeNext.OnNow && siegeNext.StartPc == new DateTime(2026, 11, 16, 12, 0, 0, DateTimeKind.Utc) + lag && siegeNext.When == "1d 23h 35m",
            $"...the Artifact Siege on Saturday 21:05 on the game's clock, then next on Monday: {Say(siegeNext)}");

        var et = new EventTimers();
        var pc = server + lag;
        Check(!et.ClockKnown && et.ClockLine(pc).StartsWith("On this PC's clock"), "...before the traffic says, this PC's clock (and the card says so)");
        et.Take(new AreaEvent(pc, server, AreaEvent.Kind.Clock, AreaEvent.Stage.Unknown, null, "", "", "", 0));
        var sf = et.Rows(pc).First(r => r.Event.Name == "Shugo Festival");
        Check(et.ClockKnown && Math.Abs(et.Offset.TotalSeconds - 14.6) < 0.01 && sf.Left == TimeSpan.FromMinutes(31) && et.ClockLine(pc) == "On the game's clock (15 s behind this PC's)",
            $"...the game's clock from its traffic: {et.Offset.TotalSeconds:F1} s; the Shugo Festival in {BossTimers.Countdown(sf.Left)}; \"{et.ClockLine(pc)}\"");

        // the event the player is in: 17:01, 2026-11-13 (UTC 22:01), as the event server said it
        var e0 = new DateTime(2026, 11, 13, 22, 1, 20, 361, DateTimeKind.Utc);
        AreaEvent A(double sec, AreaEvent.Kind k, AreaEvent.Stage st, double? startSec = null, bool moved = false, string name = "Defend Shugo Merchants") =>
            new(e0.AddSeconds(sec), e0.AddSeconds(sec) - lag, k, st, startSec is { } ss ? e0.AddSeconds(ss) - lag : null, "Shugo Festival", name, "", 1, moved);
        var prep = A(0, AreaEvent.Kind.Phase, AreaEvent.Stage.Preparing, -16.1);
        var main = A(116.5, AreaEvent.Kind.Phase, AreaEvent.Stage.UnderWay, 116.5);
        var over = A(185.9, AreaEvent.Kind.Over, AreaEvent.Stage.Over);
        var shut = A(207.3, AreaEvent.Kind.Closed, AreaEvent.Stage.Over);
        var shutPrep = A(207.3, AreaEvent.Kind.Closed, AreaEvent.Stage.Preparing);
        var lv = new EventTimers();
        lv.Take(prep);
        var l1 = lv.Current(prep.At);
        lv.Take(main); lv.Take(A(120, AreaEvent.Kind.Closed, AreaEvent.Stage.Preparing));   // a getting-ready part closing as the event begins
        var l2 = lv.Current(main.At.AddSeconds(5));
        lv.Take(over); var l3 = lv.Current(over.At);
        lv.Take(shut); lv.Take(shutPrep);
        var l4 = lv.Current(shut.At.AddSeconds(10)); var l5 = lv.Current(shut.At.AddSeconds(EventTimers.KeepLiveSeconds + 1));
        Check(l1 is { Stage: AreaEvent.Stage.Preparing, Label: "Defend Shugo Merchants" } && l1.StartedPc == e0.AddSeconds(-16.1)
            && l2 is { Stage: AreaEvent.Stage.UnderWay, LeftAt: null } && l3 is { Stage: AreaEvent.Stage.Over } && l4 is { LeftAt: { } } && l5 == null
            && EventTimers.StageText(l1) == "Getting ready" && EventTimers.StageText(l2) == "Under way" && EventTimers.StageText(l3) == "Over: rewards next" && EventTimers.StageText(l4) == "Left",
            $"...the event you're in: {(l1 == null ? "-" : EventTimers.StageText(l1))}, {(l2 == null ? "-" : EventTimers.StageText(l2))} (its lobby closing is no leaving), {(l3 == null ? "-" : EventTimers.StageText(l3))}, {(l4 == null ? "-" : EventTimers.StageText(l4))}, gone {EventTimers.KeepLiveSeconds:0} s after");
        var mv = new EventTimers(); mv.Take(prep); mv.Take(A(30, AreaEvent.Kind.Closed, AreaEvent.Stage.Preparing, moved: true));
        Check(mv.Current(prep.At.AddSeconds(31)) is { LeftAt: { } }, "...the game moving off the event's server in its lobby is leaving it");

        // the run, through the session: started by the game's message 10 s before the minimap, left by it, closed after the reward time
        var items = ItemDictionary.LoadOrEmpty(settings.ItemsFile);
        var reader = new PacketReader(); reader.ReadingForTest();
        var s = new TrackerSession(null, settings, items, "Test") { Packets = reader };
        s.ApplyZone("Ellun River Midstream", e0.AddSeconds(-60));
        reader.AreaForTest(prep); s.DrainPacketsForTest();
        var r1 = s.Instances.Current;
        s.ApplyZone("Defend Shugo Merchants", e0.AddSeconds(9.9));   // the minimap, 17:01:30.3 on that day
        reader.AreaForTest(main); s.DrainPacketsForTest();
        var r2 = s.Instances.Current;
        reader.AreaForTest(over); reader.AreaForTest(shut); reader.AreaForTest(shutPrep); s.DrainPacketsForTest();
        var r3 = s.Instances.Current;
        s.ApplyZone("Defend Shugo Merchants", shut.At.AddSeconds(12));   // the minimap, still on the event 12 s later
        var r4 = s.Instances.Current;
        s.ApplyZone("Ellun River Midstream", shut.At.AddSeconds(20));
        s.ApplyZone("Ellun River Midstream", shut.At.AddSeconds(InstanceLog.RewardGraceSeconds + 1));
        var r5 = s.Instances.Current;
        Check(r1 is { Kind: "Event", Zone: "Shugo Festival", Event: "Defend Shugo Merchants", Leaving: false } && r1.Value.Start == prep.At
            && r2 is { Leaving: false } && r2.Value.Start == prep.At && r3 is { Leaving: true } && r4 is { Leaving: true } && r4.Value.Start == prep.At && r5 == null,
            $"...the run: opened by the game at {prep.At:HH:mm:ss.f}Z as {r1?.Label} (the minimap's read 10 s later keeps it), left at its close ({(r3?.Leaving == true ? "leaving" : "open")}), the minimap still on it 12 s on: {(r4?.Leaving == true ? "still leaving, no new run" : "undone")}; closed after the reward time: {(r5 == null ? "yes" : "no")}");
        // only the family known: the run files under it, and the header names the game
        var fam = new TrackerSession(null, settings, items, "Test") { Packets = reader };
        fam.ApplyZone("Ellun River Midstream", e0.AddHours(1).AddSeconds(-60));
        reader.AreaForTest(A(3600, AreaEvent.Kind.Phase, AreaEvent.Stage.Preparing, 3584, name: "")); fam.DrainPacketsForTest();
        var f1 = fam.Instances.Current;
        fam.Instances.Witness("header", "Hidden Lugi", e0.AddHours(1).AddSeconds(5), "Ellun River Midstream");
        var f2 = fam.Instances.Current;
        Check(f1 is { Zone: "Shugo Festival", Event: "" } && f2 is { Event: "Hidden Lugi" }, $"...a game the decoder can't name yet: {f1?.Label}, then the header names it: {f2?.Label}");
        var unk = new TrackerSession(null, settings, items, "Test") { Packets = reader };
        reader.AreaForTest(new AreaEvent(e0, e0 - lag, AreaEvent.Kind.Phase, AreaEvent.Stage.UnderWay, null, "", "", "unknown-event", 9)); unk.DrainPacketsForTest();
        Check(unk.Instances.Current == null, "...an event the decoder doesn't know at all opens no run (the screen's to find)");
    }

    /// <summary>
    /// The Abyss's bosses on their schedule against Steven's map of Chaotic Lower Reshanta (NA East, 2026-11-14, the game's
    /// clock at 11:38:40 UTC): Watcher Kaira 21min 20s, the Executors 14h 51min 20s, Guardian Lord Nahma 1d 14h 21min. Then
    /// the timers as one list for the overlay (what it keeps, how many), the chime (once per spawn, only for the timers picked,
    /// not too late, not at volume 0) and the settings file.
    /// </summary>
    static void TimerBoardChecks()
    {
        var server = new DateTime(2026, 11, 14, 11, 38, 40, DateTimeKind.Utc);
        var (zone, sure) = AbyssBosses.ZoneFor("na_e");
        TimeSpan Left(string name) => AbyssBosses.RowOf(AbyssBosses.Schedule.First(b => b.Name == name), server, TimeSpan.Zero, zone).Left;
        var kaira = Left("Watcher Kaira"); var tamasa = Left("Executor Tamasa"); var argo = Left("Executor Argo"); var nahma = Left("Guardian Lord Nahma");
        Check(sure && kaira == new TimeSpan(0, 21, 20) && tamasa == new TimeSpan(14, 51, 20) && argo == tamasa && nahma == new TimeSpan(1, 14, 21, 20),
            $"Abyss bosses on NA East's clock, as Steven's map read: Watcher Kaira {kaira} (21min 20s), the Executors {tamasa} (14h 51min 20s), Guardian Lord Nahma {nahma} (1d 14h 21min)");
        var kst = TimeZoneInfo.CreateCustomTimeZone("kst", TimeSpan.FromHours(9), "kst", "kst");
        var onKst = AbyssBosses.RowOf(AbyssBosses.Schedule.First(b => b.Name == "Executor Tamasa"), server, TimeSpan.Zero, kst).Left;
        Check(onKst != tamasa, $"...and not on UTC+9, which would say {onKst} for the Executors");
        var spawned = AbyssBosses.RowOf(AbyssBosses.Schedule.First(b => b.Name == "Watcher Kaira"), server.AddMinutes(31.5), TimeSpan.Zero, zone);
        var after = AbyssBosses.RowOf(AbyssBosses.Schedule.First(b => b.Name == "Watcher Kaira"), server.AddMinutes(52), TimeSpan.Zero, zone);
        Check(spawned is { Spawned: true, When: "10 min ago" } && !after.Spawned && after.Left == new TimeSpan(2, 29, 20),
            $"...Watcher Kaira \"Spawned, {spawned.When}\" 10 min after 07:00, then counting to 10:00 ({after.When})");

        // the timers as one list
        var lag = TimeSpan.FromSeconds(15);
        var now = server + lag;
        var ev = new EventTimers(); ev.SetForTest(lag, true, null);
        var bt = new BossTimers();
        bt.SetForTest(new[]
        {
            new BossTimers.Boss("k1", "Bloodfang Pnyn", "Northern Tolbas Forest", "Verteron", BossTimers.Kind.Up, now.AddMinutes(-14), now, false),
            new BossTimers.Boss("k2", "Heretic Layla", "Rock Tomb Hill", "Verteron", BossTimers.Kind.Waiting, now.AddMinutes(4), now, false),
            new BossTimers.Boss("k3", "Kusan the Mad Gladiator", "Verteron Fortress Ruins", "Verteron", BossTimers.Kind.Waiting, now.AddMinutes(12), now, false),
            new BossTimers.Boss("k4", "Soul Ruler Kashapa", "Eastern Altamia Highland", "Verteron", BossTimers.Kind.Waiting, now.AddMinutes(3), now, false),
        });
        var items = TimerBoard.Build(now, ev, bt, "na_e");
        var prefs = TimerPrefs.Defaults();
        string Short(string name) => items.First(i => i.Name.StartsWith(name, StringComparison.Ordinal)).Short;
        Check(items.Count == EventTimers.Schedule.Length + 4 + AbyssBosses.Schedule.Length && items[0].Key == TimerBoard.BossKey("Bloodfang Pnyn") && items[0].State == TimerBoard.Kind.On
            && Short("Kusan") == "Kusan" && Short("Soul Ruler") == "Kashapa" && Short("Shugo") == "Shugo" && Short("Watcher Kaira") == "Watcher Kaira" && Short("Executor Kaira") == "Executor Kaira"
            && Short("Guardian Lord Nahma") == "Nahma ×3" && Short("Bloodfang") == "Bloodfang" && Short("Dimensional") == "Invasion",
            $"timers as one list: {items.Count} (events, world bosses, the Abyss), the boss that's up first; short names {Short("Kusan")}, {Short("Soul Ruler")}, {Short("Shugo")}, {Short("Watcher Kaira")} / {Short("Executor Kaira")}, {Short("Guardian Lord Nahma")}");

        // watched by default (Steven, 2026-11-14): the events, Kashapa, Gartua, Lagta and the Abyss; every other boss off
        Check(prefs.Watched(TimerBoard.EventKey("Shugo Festival")) && prefs.Watched(TimerBoard.BossKey("Soul Ruler Kashapa")) && prefs.Watched(TimerBoard.BossKey("Eternal Gartua"))
            && prefs.Watched(TimerBoard.BossKey("High Commander Lagta")) && prefs.Watched(TimerBoard.AbyssKey("Executor Tamasa"))
            && !prefs.Watched(TimerBoard.BossKey("Heretic Layla")) && !prefs.Watched(TimerBoard.BossKey("Kusan the Mad Gladiator")) && prefs.OverlayOn && prefs.ClickThrough && prefs.Count == 4,
            "watched by default: the events, Kashapa, Gartua, Lagta and every Abyss boss; Layla and Kusan off; the overlay on, click-through, 4 at most");

        // the overlay: only watched timers within 5 min, soonest first; gone once up; hidden with none
        string Names(List<TimerBoard.Item> l) => l.Count == 0 ? "none" : string.Join(", ", l.Select(i => i.Short));
        var near = TimerBoard.ForOverlay(items, prefs, now);
        Check(near.Count == 1 && near[0].Short == "Kashapa",
            $"...the overlay shows only watched timers within 5 min: {Names(near)} (Kashapa 3 min; not Layla 4 min, not watched; not Kusan 12 min; not Bloodfang, up)");
        prefs.SetWatched(TimerBoard.BossKey("Heretic Layla"), true);
        prefs.SetWatched(TimerBoard.BossKey("Soul Ruler Kashapa"), false);
        var swapped = TimerBoard.ForOverlay(items, prefs, now);
        var at21 = now.AddMinutes(19);
        var kairaNear = TimerBoard.ForOverlay(TimerBoard.Build(at21, ev, bt, "na_e"), prefs, at21);
        var at22 = now.AddMinutes(21.5);
        var up = TimerBoard.ForOverlay(TimerBoard.Build(at22, ev, bt, "na_e"), prefs, at22);
        Check(swapped.Count == 1 && swapped[0].Short == "Layla" && kairaNear.Any(i => i.Short == "Watcher Kaira") && !up.Any(i => i.Short == "Watcher Kaira")
            && prefs.Watched(TimerBoard.BossKey("Heretic Layla")) && !prefs.Watched(TimerBoard.BossKey("Soul Ruler Kashapa")),
            $"...Layla turned on and Kashapa off: {Names(swapped)}; 2 min before 12:00 {Names(kairaNear)}; once Kaira is up she's gone ({Names(up)})");
        prefs.WindowMinutes = 15;
        var wide = TimerBoard.ForOverlay(items, prefs, now);
        prefs.SetWatched(TimerBoard.BossKey("Kusan the Mad Gladiator"), true); prefs.Count = 1;
        var capped = TimerBoard.ForOverlay(items, prefs, now);
        Check(wide.Count == 1 && wide[0].Short == "Layla" && capped.Count == 1 && capped[0].Short == "Layla" && TimerBoard.Clock(TimeSpan.FromSeconds(292)) == "4:52" && TimerBoard.Clock(TimeSpan.FromSeconds(730)) == "12:10" && TimerBoard.Clock(TimeSpan.FromSeconds(3920)) == "1:05:20",
            $"...within 15 min ({Names(wide)}), at most 1 ({Names(capped)}); the countdown reads 4:52, 12:10, 1:05:20");
        prefs.WindowMinutes = 5; prefs.Count = 4;

        // the chime: once per spawn, for watched timers not muted, not too late, not at volume 0
        var ch = new TimerChimes();
        var first = ch.Due(now, items, prefs);
        var again = ch.Due(now.AddSeconds(1), TimerBoard.Build(now.AddSeconds(1), ev, bt, "na_e"), prefs);
        Check(first.Count == 1 && first[0].Short == "Layla" && again.Count == 0,
            $"chime: Layla, watched, 4 min away: one chime ({Names(first)}), not again a second later; Kashapa (turned off) and Kusan (12 min) none");
        var at17 = now.AddMinutes(17).AddSeconds(30);   // Kaira 3min 50s away
        var kairaChime = ch.Due(at17, TimerBoard.Build(at17, ev, bt, "na_e"), prefs);
        prefs.SetChimes(TimerBoard.AbyssKey("Watcher Kaira"), false);
        var muted = new TimerChimes().Due(at17, TimerBoard.Build(at17, ev, bt, "na_e"), prefs);
        prefs.SetChimes(TimerBoard.AbyssKey("Watcher Kaira"), true);
        var late = new TimerChimes().Due(now.AddMinutes(21).AddSeconds(10), TimerBoard.Build(now.AddMinutes(21).AddSeconds(10), ev, bt, "na_e"), prefs);
        prefs.Volume = 0;
        var silent = new TimerChimes().Due(at17, TimerBoard.Build(at17, ev, bt, "na_e"), prefs);
        Check(kairaChime.Any(i => i.Short == "Watcher Kaira") && !muted.Any(i => i.Short == "Watcher Kaira") && !late.Any(i => i.Short == "Watcher Kaira") && silent.Count == 0,
            "...Watcher Kaira's 5 min before (watched by default); none with her chime off, none 10 s before (too late to help), none at volume 0");

        // the settings file
        var dir = TestTemp.Path("KinahLedger-packet-test"); Directory.CreateDirectory(dir);
        var file = Path.Combine(dir, "timers.json"); File.Delete(file);
        var saved = TimerPrefs.Load(file);
        saved.OverlayOn = false; saved.ClickThrough = false; saved.X = 1500; saved.Y = 200; saved.Count = 6; saved.WindowMinutes = 10; saved.ChimeMinutes = 2; saved.Volume = 0.3; saved.Details = true;
        saved.WinX = 2000; saved.WinY = 100; saved.WinW = 500; saved.WinH = 900;
        saved.SetWatched(TimerBoard.BossKey("Heretic Layla"), true); saved.SetWatched(TimerBoard.EventKey("Spacetime Rift"), false); saved.SetChimes(TimerBoard.EventKey("Shugo Festival"), false);
        saved.Save();
        var back = TimerPrefs.Load(file);
        Check(back is { OverlayOn: false, ClickThrough: false, X: 1500, Y: 200, Count: 6, WindowMinutes: 10, ChimeMinutes: 2, Volume: 0.3, Details: true, WinX: 2000, WinW: 500, WinH: 900 }
            && back.Watched(TimerBoard.BossKey("Heretic Layla")) && !back.Watched(TimerBoard.EventKey("Spacetime Rift")) && back.Watched(TimerBoard.EventKey("Shugo Festival")) && !back.Chimes(TimerBoard.EventKey("Shugo Festival"))
            && TimerPrefs.Load(Path.Combine(dir, "none.json")) is { OverlayOn: true, ClickThrough: true },
            "timers.json: the overlay, its place, click-through, minutes, how many, the chime, the Timers window's place, size and details, the watched and muted timers, read back; no file is the defaults (overlay on, click-through)");
    }

    static void BossTimerChecks(DateTime t)
    {
        var lag = TimeSpan.FromSeconds(14.87);   // the server's clock behind this PC's
        BossSighting Dead(string key, string name, DateTime at, TimeSpan left, string place = "Rock Tomb Hill") =>
            new(at, at - lag, key, name, place, "Verteron", BossSighting.Kind.Dead, at - lag + left, false);
        BossSighting Alive(string key, string name, DateTime at, TimeSpan since) =>
            new(at, at - lag, key, name, "Northern Tolbas Forest", "Verteron", BossSighting.Kind.Alive, at - lag - since, false);

        var bt = new BossTimers();
        var layla = TimeSpan.FromSeconds(2 * 3600 + 40 * 60 + 1.2);
        Check(bt.Take(Dead("a", "Heretic Layla", t, layla)), "boss timers: a boss first seen is a change");
        var row = bt.Rows(t.AddSeconds(60)).Single();
        Check(row.When == "2h 39m 02s" && row.Boss.SpawnPc == t + layla && !row.Stale,
            $"...the countdown is the server's time left, from when the list came: {row.When} a minute later (not {BossTimers.Countdown(t - lag + layla - t.AddSeconds(60))} against this PC's clock)");
        Check(!bt.Take(Dead("a", "Heretic Layla", t.AddSeconds(1), layla - TimeSpan.FromSeconds(1.003))) && bt.Rows(t.AddSeconds(60)).Single().Boss.SpawnPc == t + layla,
            "...the same reading a second later (the map still open) changes nothing");
        var later = BossTimers.RowOf(bt.Rows(t).Single().Boss, t + layla + TimeSpan.FromMinutes(4));
        Check(later is { Due: true, Status: "Due", When: "4 min ago" }, $"...past its spawn with no newer list: {later.Status}, {later.When}");

        Check(bt.Take(Alive("b", "Bloodfang Pnyn", t, TimeSpan.FromMinutes(14))), "...a live boss");
        var up = bt.Rows(t.AddSeconds(30)).First();
        Check(up is { UpNow: true, Status: "Up now", When: "up 14 min" } && up.Boss.Name == "Bloodfang Pnyn", $"...is up while the map sends the list ({up.Status}, {up.When}), and listed first");
        var gone = BossTimers.RowOf(up.Boss, t.AddMinutes(30));
        Check(gone is { UpNow: false, Stale: true, Status: "Up when last seen", When: "seen 30 min ago" }, $"...and after: {gone.Status}, {gone.When} (nobody knows it is still up)");
        var killed = Dead("b", "Bloodfang Pnyn", t.AddSeconds(28), TimeSpan.FromHours(3));
        Check(bt.Take(killed) && bt.Rows(t.AddSeconds(29)).Last().Boss.Name == "Bloodfang Pnyn", "...killed: a change, its 3 h wait puts it after Layla");

        bt.Take(Dead("c", "Chaser Taulo", t, TimeSpan.FromMinutes(20), "Forest of Exiles"));
        bt.Take(new BossSighting(t, t - lag, "d", "", "", "Verteron", BossSighting.Kind.Dead, t - lag + TimeSpan.FromMinutes(5), false));
        var order = bt.Rows(t.AddSeconds(2)).Select(r => r.Boss.Name).ToList();
        Check(order.SequenceEqual(new[] { "A Verteron boss", "Chaser Taulo", "Heretic Layla", "Bloodfang Pnyn" }), $"...in spawn order, a boss the decoder can't name kept as its region's: {string.Join(", ", order)}");
        var guess = new BossTimers();
        guess.Take(new BossSighting(t, null, "e", "Furious Saursus", "", "Verteron", BossSighting.Kind.Dead, t + TimeSpan.FromMinutes(10), false));
        Check(guess.Rows(t).Single() is { Boss.ClockGuessed: true, When: "10m 00s" }, "...with no server clock yet, this PC's is taken (and marked)");
        Check(BossTimers.Countdown(TimeSpan.FromSeconds(45.2)) == "46s" && BossTimers.Countdown(TimeSpan.FromSeconds(3600 * 5 + 61)) == "5h 01m 01s" && BossTimers.Countdown(TimeSpan.Zero) == "0s",
            "...the countdown's form: 46s, 5h 01m 01s, 0s");

        // the saved copy: a restart shows the timers and how old they are
        var dir = TestTemp.Path("KinahLedger-packet-test"); Directory.CreateDirectory(dir);
        var file = Path.Combine(dir, "bosses.json"); File.Delete(file);
        var now = DateTime.UtcNow;
        var saved = new BossTimers(file);
        saved.Take(Dead("a", "Heretic Layla", now.AddHours(-1), TimeSpan.FromHours(2)));
        saved.Take(Alive("b", "Bloodfang Pnyn", now.AddHours(-1), TimeSpan.FromMinutes(5)));
        saved.Save();
        var back = new BossTimers(file);
        var rows = back.Rows(now);
        Check(back.Count == 2 && rows.Any(r => r.Boss.Name == "Heretic Layla" && Math.Abs((r.Left - TimeSpan.FromHours(1)).TotalSeconds) < 1) && rows.Any(r => r is { Status: "Up when last seen" })
            && back.Freshness(now) == "Read 1 h ago: open the world map to refresh",
            $"...saved and read back after a restart: {back.Count} bosses, \"{back.Freshness(now)}\"");
        var old = new BossTimers();
        Check(old.Freshness(now) == "No reading yet" && old.Rows(now).Count == 0, "...and before any reading: no rows, \"No reading yet\"");
    }

    /// <summary>
    /// The decoder over a capture file: every connection from <paramref name="serverPort"/> in turn (an event or the Abyss is
    /// another server), each its own decoder, every currency change, printed. Needs the decoder in the build.
    /// </summary>
    public static int Replay(string pcapng, int serverPort, Settings? settings = null)
    {
        if (!NativeDecoder.Available) { Console.WriteLine("this build has no decoder (klnet.dll)"); return 1; }
        var segs = new List<(DateTime At, TcpSegment S)>();
        foreach (var (at, frame, link) in Pcapng.Read(pcapng))
            if (NpcapSource.IpOf(frame, link) is { } ip && TcpSegment.Parse(ip) is { } s && s.SrcPort == serverPort) segs.Add((at, s));
        var seen = new List<CurrencyChange>();
        var batches = new List<ItemBatch>();
        var fights = new FightTally(); var fought = new List<FightTally.Outcome>(); bool readsFights = false;
        var gaps = new List<DateTime>();
        var bosses = new BossTimers();
        var bossLines = new List<string>();
        var eventLines = new List<string>(); var timers = new EventTimers();
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
                    while (dec.NextBoss() is { } b)
                        if (bosses.Take(b)) bossLines.Add($"{PacketReader.Local(b.At)}  world boss: {(b.Name.Length > 0 ? b.Name : "(not named)")} {b.State}{(b.Time is { } t && b.ServerNow is { } sv ? (b.State == BossSighting.Kind.Dead ? $", spawns in {BossTimers.Countdown(t - sv)}" : $", up {BossTimers.Span(sv - t)}") : "")}");
                    while (dec.NextFight() is { } f) fights.Add(f);
                    while (dec.NextAreaEvent() is { } ae)
                    {
                        timers.Take(ae);
                        if (ae.What != AreaEvent.Kind.Clock)
                            eventLines.Add($"{PacketReader.Local(ae.At)}  event: {(ae.Label.Length > 0 ? ae.Label : "(not known: " + ae.Code + ")")} {ae.What}{(ae.What == AreaEvent.Kind.Phase ? " " + ae.Phase : "")}{(ae.PhaseStart is { } ps && ae.ServerNow is { } sv ? $", began {PacketReader.Local(ps + (ae.At - sv))}" : "")}");
                    }
                },
                OnGap = () => { dec.Gap(); gaps.Add(last); },
            };
            foreach (var (at, s) in conn) st.Add(s.Seq, s.Payload, at, s.Syn);
            fought.AddRange(fights.Flush());   // the next connection's ids are other entities
            var stats = dec.Stats;
            Console.WriteLine($"{conn.Key.Item1}:{serverPort} → :{conn.Key.DstPort}: {st.Bytes:N0} bytes, {stats.Messages:N0} messages, {st.Gaps} gaps, {stats.Resyncs} resyncs, {stats.CurrencyMessages} currency messages");
        }
        foreach (var c in seen) Print(c);
        foreach (var l in bossLines) Console.WriteLine(l);
        foreach (var l in eventLines) Console.WriteLine(l);
        if (timers.ClockKnown) Console.WriteLine($"the game's clock: {timers.Offset.TotalSeconds:F1} s behind the capture's");
        if (bosses.Count > 0) Console.WriteLine($"world bosses: {bosses.Count} on the map's list, {bossLines.Count} readings (a boss's first, then each change)");
        foreach (var k in Enum.GetValues<CurrencyKind>())
            Console.WriteLine($"{k}: gained {seen.Where(c => c.What == k && c.Delta > 0).Sum(c => c.Delta):N0} in {seen.Count(c => c.What == k && c.Delta > 0)} changes");
        if (readsFights) ReplayFights(pcapng, fought, fights.RankPoints);
        if (NativeDecoder.Version >= 7)
        {
            var items0 = ItemDictionary.LoadOrEmpty((settings ?? new Settings()).ItemsFile);
            var gs = ReplayGathers(pcapng, serverPort);
            foreach (var g in gs)
                Console.WriteLine($"  {g.At.ToLocalTime():HH:mm:ss.ff}  gathered: {(g.Outcome == GatherEnd.Upgrade ? "the upgrade roll won" : g.Outcome == GatherEnd.Failed ? "failed" : "worked")}, {items0.NameOf(g.ItemId)}{(g.Outcome == GatherEnd.Failed ? "" : $" x{g.Count}")} · node {g.NodeId}{(g.NodeKind != 0 ? $" (kind {g.NodeKind})" : "")}{(g.Attempt > 0 ? $", try {g.Attempt}" : "")}");
            Console.WriteLine($"gathering: {gs.Count} attempts on {gs.Select(g => g.NodeId).Distinct().Count()} nodes ({gs.Count(g => g.Outcome == GatherEnd.Worked)} worked, {gs.Count(g => g.Outcome == GatherEnd.Upgrade)} upgrade rolls won, {gs.Count(g => g.Outcome == GatherEnd.Failed)} failed); "
                + string.Join(", ", gs.Where(g => g.Count > 0).GroupBy(g => g.ItemId).Select(x => $"{items0.NameOf(x.Key)} {x.Sum(g => g.Count)}")));
        }
        if (!reads) { Console.WriteLine("this decoder reads no items (klnet.dll version 1)"); return 0; }
        ReplayItems(pcapng, batches, settings, gaps);
        return 0;
    }

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
