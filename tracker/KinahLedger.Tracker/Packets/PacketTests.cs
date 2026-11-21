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

        // the party finder (the decoder's version 11): the Applicants window's list, through the reader as the decoder hands them
        {
            var reader = new PacketReader();
            PartyApplication A(double at, PartyApplication.Kind k, string key, string name = "", string cls = "", long cp = 0) =>
                new(t.AddSeconds(at), k, key, name, cls, 45, 2_651, cp, "Zikel", t.AddSeconds(at + 17.7));
            reader.ApplicantForTest(A(0, PartyApplication.Kind.Applied, "1", "Kyralis", "Gladiator", 133_774));
            reader.ApplicantForTest(A(5, PartyApplication.Kind.Applied, "2", "Varrenthos", "Gladiator", 128_195));
            reader.ApplicantForTest(A(7, PartyApplication.Kind.Applied, "3", "Embervale", "Sorcerer", 125_846));
            var three = reader.Applicants.Pending(t.AddSeconds(8));
            reader.ApplicantForTest(A(9, PartyApplication.Kind.Accepted, "2", "Varrenthos"));
            reader.ApplicantForTest(A(10, PartyApplication.Kind.Removed, "9", "nobody"));   // someone not on the list: nothing
            var two = reader.Applicants.Pending(t.AddSeconds(10));
            reader.ApplicantForTest(A(11, PartyApplication.Kind.Applied, "1", "Kyralis", "Gladiator", 133_774));   // asked again: newest, at the bottom
            var again = reader.Applicants.Pending(t.AddSeconds(11));
            int v0 = reader.Applicants.Version;
            var stale = reader.Applicants.Pending(t.AddSeconds(7) + PartyApplicants.StaleAfter);
            reader.ApplicantForTest(A(30, PartyApplication.Kind.Applied, "4", "Halvric", "Cleric", 110_552));
            reader.ApplicantForTest(A(31, PartyApplication.Kind.Cleared, ""));
            var none = reader.Applicants.Pending(t.AddSeconds(31));
            Check(three.Select(a => a.Name).SequenceEqual(new[] { "Kyralis", "Varrenthos", "Embervale" }) && three[0] is { Class: "Gladiator", Level: 45, CombatPower: 133_774, Server: "Zikel" }
                && two.Select(a => a.Name).SequenceEqual(new[] { "Kyralis", "Embervale" }) && again.Select(a => a.Name).SequenceEqual(new[] { "Embervale", "Kyralis" })
                && stale.Select(a => a.Name).SequenceEqual(new[] { "Kyralis" }) && reader.Applicants.Version > v0 && none.Count == 0,
                $"applicants: three asking, oldest first ({string.Join(", ", three.Select(a => a.Name))}); one joined; asked again at the bottom ({string.Join(", ", again.Select(a => a.Name))}); " +
                $"one with no word for {PartyApplicants.StaleAfter.TotalMinutes:0} minutes dropped ({string.Join(", ", stale.Select(a => a.Name))} left); every request gone at once");
            Check(PartyApplicants.Ago(t, t.AddSeconds(12)) == "12s" && PartyApplicants.Ago(t, t.AddSeconds(185)) == "3m" && PartyApplicants.Ago(t, t.AddHours(2)) == "2h" && PartyApplicants.Ago(t.AddSeconds(5), t) == "0s",
                "applicants: how long ago, short (12s, 3m, 2h; never below 0)");
            if (NativeDecoder.Version >= 11) Check(NativeDecoder.ApplicantRecordSize == 128, $"applicants: the decoder's record is {NativeDecoder.ApplicantRecordSize} bytes (128)");
        }

        // Odyle Energy (the decoder's version 15, Session/OdyleEnergy.cs): kept per character, kept across a restart, moved
        // with a character switch the screen sees late, never shown before it was read, topped up by the refills since
        {
            var file = TestTemp.Path("KinahLedger-odyle-test.json");
            try { File.Delete(file); } catch (IOException) { }
            var book = new OdyleEnergy(file);
            string who = "c1";
            var reader = new PacketReader { Energy = book, CharacterNow = () => who };
            OdyleReading R(double minutes, int natural, int extra, OdyleReading.Why why, int by = 0) => new(t.AddMinutes(minutes), natural, extra, why, by);
            bool unknown = book.Now("c1", t) == null && book.Now("", t) == null;
            reader.EnergyForTest(R(0, 280, 105, OdyleReading.Why.List));
            reader.EnergyForTest(R(15, 200, 105, OdyleReading.Why.Spent));
            reader.EnergyForTest(R(20, 200, 265, OdyleReading.Why.Charged, 160));
            bool kept = book.Now("c1", t.AddMinutes(20)) == 465 && book.Now("c2", t.AddMinutes(20)) == null;
            // c2 logs in: its list comes, then the screen sees the class icon 40 s later
            reader.EnergyForTest(R(30, 40, 1_975, OdyleReading.Why.List));
            bool before = book.Now("c1", t.AddMinutes(30)) == 2_015;
            book.Switched("c1", "c2", t.AddMinutes(30).AddSeconds(40)); who = "c2";
            bool moved = book.Now("c2", t.AddMinutes(31)) == 2_015 && book.Now("c1", t.AddMinutes(31)) == 465;
            reader.EnergyForTest(R(40, 0, 1_935, OdyleReading.Why.Spent));
            // back to c1 an hour later with no list since: nothing moves (the reading isn't a fresh list)
            book.Switched("c2", "c1", t.AddMinutes(100)); who = "c1";
            bool stays = book.Now("c2", t.AddMinutes(100)) == 1_935 && book.Now("c1", t.AddMinutes(100)) == 465;
            // a restart: the same file, each character's last reading
            var again = new OdyleEnergy(file);
            bool restored = again.Now("c1", t.AddMinutes(100)) == 465 && again.Now("c2", t.AddMinutes(100)) == 1_935 && again.Of("c1") is { Natural: 200, Extra: 265 };
            Check(unknown && kept && before && moved && stays && restored,
                $"Odyle Energy: none before it is read (unknown {unknown}); kept for the character counted for (kept {kept}); a login's list read before the screen saw the switch moves to the new character and the old one gets its own back (before {before}, moved {moved}); an old reading stays put (stays {stays}); a restart keeps both (restored {restored})");
            // the refills: 15 at 01:00, 04:00 ... 22:00 UTC to the natural pool, offline too, up to 560 (840 once seen above it)
            var r0 = new DateTime(2026, 11, 19, 15, 30, 0, DateTimeKind.Utc);
            var tops = new OdyleEnergy(null);
            tops.Take(new OdyleReading(r0, 100, 50, OdyleReading.Why.List), "a");
            tops.Take(new OdyleReading(r0, 550, 0, OdyleReading.Why.List), "b");
            tops.Take(new OdyleReading(r0, 600, 0, OdyleReading.Why.List), "m");
            int at1559 = tops.Now("a", r0.AddMinutes(29))!.Value, at1602 = tops.Now("a", r0.AddMinutes(32))!.Value, nextDay = tops.Now("a", r0.AddHours(24))!.Value;
            Check(at1559 == 150 && at1602 == 165 && nextDay == 150 + 8 * 15 && tops.Now("b", r0.AddHours(24)) == 560 && tops.Now("m", r0.AddHours(24)) == 720
                && OdyleEnergy.RefillsBetween(r0, r0.AddHours(3)) == 1 && OdyleEnergy.RefillsBetween(r0.AddHours(3), r0) == 0,
                $"Odyle Energy: a reading topped up by the refills since: {at1559} at 15:59, {at1602} at 16:02, {nextDay} a day later; 550 stops at 560, a member's 600 climbs to 840 ({tops.Now("m", r0.AddHours(24))} after 8 refills)");
            // before any character was seen, the readings are the first one's
            var first = new OdyleEnergy(null);
            first.Take(new OdyleReading(t, 10, 20, OdyleReading.Why.Spent), "");
            first.Switched("", "c1", t.AddHours(5));
            Check(first.Now("c1", t) == 30 && first.Now("", t) == null, "Odyle Energy: what was read before any character was seen goes to the first one");
            if (NativeDecoder.Version >= 15) Check(NativeDecoder.EnergyRecordSize == 32, $"Odyle Energy: the decoder's record is {NativeDecoder.EnergyRecordSize} bytes (32)");
            try { File.Delete(file); } catch (IOException) { }
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
            // a kill's pay is its own evidence (2026-11-20 10:15:42, Steven's log): in the Abyss three kills' bound Kinah, Rank
            // and Abyss Points came 2 s after the last counted pickup's 10 s were up, their kills were told 4 to 7 s later, so
            // each waited its 3 s and was left out as "a sale, the mail or a reward". Rank Points rise only for a kill.
            var rk = new PacketReader(); rk.ReadingForTest();
            var k = new TrackerSession(null, settings, items, "Test") { Packets = rk };
            k.Engine.SetZone("Chaotic Lower Reshanta");
            var kt = DateTime.UtcNow.AddSeconds(-60);
            rk.AmountForTest(CurrencyKind.BoundKinah, 27_051, kt); rk.AmountForTest(CurrencyKind.RankPoints, 16_546, kt); rk.AmountForTest(CurrencyKind.AbyssPoints, 7_895, kt);
            var p1 = DateTime.UtcNow.AddSeconds(-4.5);   // the three kills' pay, each already past its 3 s hold
            // first, Abyss Points alone with nothing killed (a supply delivery's 1,640 at 10:31:25): a reward, not counted
            rk.AmountForTest(CurrencyKind.AbyssPoints, 7_895 + 1_640, p1.AddSeconds(-8));
            long bk = 27_051, rp = 16_546, ap = 7_895 + 1_640;
            foreach (var (dbk, drp, dap, dt) in new[] { (105L, 52L, 26L, 0.0), (53L, 34L, 17L, 1.3), (26L, 19L, 9L, 1.5) })
            {
                rk.AmountForTest(CurrencyKind.BoundKinah, bk += dbk, p1.AddSeconds(dt));
                rk.AmountForTest(CurrencyKind.RankPoints, rp += drp, p1.AddSeconds(dt + 0.05));
                rk.AmountForTest(CurrencyKind.AbyssPoints, ap += dap, p1.AddSeconds(dt + 0.1));
            }
            k.DrainPacketsForTest();
            // the chat's line for one item of an update is the whole update's (2026-11-20 11:34:49, Gartua's drop): Refining Stone
            // and Artisan's Refining Stone came in one update, no kill of the player's around it (a world boss), the chat's line
            // only for the Artisan's; the plain one was judged first and left out
            {
                long plain = items.IdOf("Refining Stone"), artisan = items.IdOf("Artisan's Refining Stone");
                var g = Fresh(out var rg);
                var gt = DateTime.UtcNow.AddSeconds(-4);   // past the wait for the plain one's line
                g.CommitForTest(new ItemAcquired(gt.AddSeconds(0.3), artisan, 1, false) { Via = "chat" });
                rg.ItemsForTest(new ItemChange(gt, plain, ItemOp.Changed, 11, 1, 7, false, 1, 2, false), new ItemChange(gt, artisan, ItemOp.Changed, 12, 1, 2, false, 1, 2, false));
                g.DrainPacketsForTest();
                Check(g.Engine.Items.GetValueOrDefault(plain) == 1 && g.Engine.Items.GetValueOrDefault(artisan) == 1,
                    $"...the chat's line for one item of an update stands for all of it: Refining Stone {g.Engine.Items.GetValueOrDefault(plain)}, Artisan's Refining Stone {g.Engine.Items.GetValueOrDefault(artisan)} (1 and 1)");
            }
            Check(k.Engine.AbyssBoundKinah == 184 && k.Engine.RankPoints == 105 && k.Engine.AbyssPoints == 52,
                $"...a kill's pay (bound Kinah, Rank and Abyss Points in one update) counts though its kill is told later: bound Kinah +{k.Engine.AbyssBoundKinah} (184), Rank Points +{k.Engine.RankPoints} (105), Abyss Points +{k.Engine.AbyssPoints} (52; the lone 1,640 before them no kill's)");
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
                K(25, FightFlags.VictimMe, killer: "Nana"), K(30, FightFlags.VictimMe | FightFlags.NoKiller),
                K(35, FightFlags.NoKiller | FightFlags.VictimPlayer, "MayB"), R(35.2, 13) })   // a player near you died, your points rose (a kill of yours): not your PvP
                ft.Add(e);
            var early = ft.Take(t0.AddSeconds(2));
            var all = early.Concat(ft.Take(t0.AddSeconds(60))).ToList();
            string Say(FightTally.Outcome o) => o.Death ? $"death{(o.Name.Length > 0 ? ":" + o.Name : "")}" : $"{(o.Player ? "player" : "monster")}{(o.Credited ? "*" : "")}{(o.Name.Length > 0 ? ":" + o.Name : "")}";
            Check(early.Count == 0 && string.Join(" ", all.Select(Say)) == "monster monster* monster player:Pecked death:Nana death" && ft.RankPoints == 97,
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

            // assists (1.0.15; Steven, 2026-11-18: "assists should count as kills in abyss"): an enemy player's death by
            // someone else's hand counts as a PvP kill when the player hit them in the 30 s before; once each; never an
            // ally's death, the player's own, a hit too long before, or a monster. Made-up events in the decoder's version 12 shape
            {
                const FightFlags Enemy = FightFlags.VictimPlayer | FightFlags.VictimEnemy;
                FightEvent KP(double sec, FightFlags f, uint victim, string name = "", string killer = "") => new(t0.AddSeconds(sec), FightKind.Kill, f, victim, f.HasFlag(FightFlags.Mine) ? 1u : 9u, f.HasFlag(FightFlags.VictimMonster) ? 7u : 0, 0, 0, name, killer);
                FightEvent H(double sec, uint victim, string name) => new(t0.AddSeconds(sec), FightKind.Hit, Enemy, victim, 1, 12020000, 4_000, 0, name, "");
                var evs = new[] {
                    H(5, 100, "Miquella"), KP(10, FightFlags.Mine | Enemy, 100, "Miquella", "Swoly"),          // my kill
                    H(20, 101, "Rezzy"), H(23, 101, "Rezzy"), KP(40, FightFlags.OtherKiller | Enemy, 101, "Rezzy", "Streamer"),   // last hit 17 s before: an assist
                    KP(40.2, FightFlags.OtherKiller | Enemy, 101, "Rezzy", "Streamer"),                           // the same death said again: once
                    H(0, 102, "Dyskord"), KP(45, FightFlags.OtherKiller | Enemy, 102, "Dyskord", "Faromir"),       // hit 45 s before: not an assist
                    H(48, 103, "Faromir"), KP(50, FightFlags.OtherKiller | FightFlags.VictimPlayer, 103, "Faromir", "Vitals"),   // an ally: never
                    KP(52, FightFlags.OtherKiller | Enemy, 104, "awoo", "Kassandra"),                             // an enemy I never hit
                    KP(55, FightFlags.Mine | FightFlags.VictimMonster, 200),                                      // a monster: not PvP
                    H(57, 105, "Vitals"), KP(58, FightFlags.VictimMe, 15650, "", "Vitals"),                       // my own death
                    KP(58.5, FightFlags.OtherKiller | Enemy, 105, "Vitals", "Raid") };                            // and the one I hit, a moment before: an assist
                var at = new FightTally();
                foreach (var e in evs) at.Add(e);
                var got = at.Take(t0.AddSeconds(120));
                string SayA(FightTally.Outcome o) => o.Death ? "death" : o.Assist ? $"assist:{o.Name}({o.Killer},{o.HitBefore:0}s)" : $"{(o.Player ? "player" : "monster")}{(o.Name.Length > 0 ? ":" + o.Name : "")}";
                Check(string.Join(" ", got.Select(SayA)) == "player:Miquella assist:Rezzy(Streamer,17s) monster death assist:Vitals(Raid,2s)",
                    $"assists: my kill, a hit 17 s before someone else's kill, not 45 s before, not an ally, not unhit, a monster apart: {string.Join(" ", got.Select(SayA))}");
                // a replay adds a whole connection before deciding: a later fight's many hits on the same player never push out the earlier fight's
                var late = new FightTally();
                late.Add(H(60, 106, "Rezzy")); late.Add(KP(62, FightFlags.OtherKiller | Enemy, 106, "Rezzy", "Streamer"));
                for (int i = 0; i < 100; i++) late.Add(H(600 + i * 0.5, 106, "Rezzy"));
                late.Add(KP(659.5, FightFlags.OtherKiller | Enemy, 106, "Rezzy", "Xidau"));
                string both = string.Join(" ", late.Flush().Select(SayA));
                Check(both == "assist:Rezzy(Streamer,2s) assist:Rezzy(Xidau,10s)", $"...both of a player's deaths ten minutes apart, decided at once (a replay): {both}");
                // version 12's VictimPlayer is a player's own appearance only: a summon named after its player, dying with no killer
                // named beside a rise of the player's points, is still no kill of theirs (a monster kill with no name still is)
                var named = new FightTally();
                foreach (var e in new[] { KP(0, FightFlags.NoKiller, 18479, "MayB"), R(0.2, 13), KP(5, FightFlags.NoKiller, 22001), R(5.2, 9) }) named.Add(e);
                Check(string.Join(" ", named.Flush().Select(SayA)) == "monster", "...a player's summon dying near you is no kill of yours; an unnamed thing with your rise still is");
                // a decoder before version 12 sends no hits and no others' kills: the same fights count as before
                var old = new FightTally();
                foreach (var e in evs.Where(e => e.Kind != FightKind.Hit && !e.Flags.HasFlag(FightFlags.OtherKiller))) old.Add(e with { Flags = e.Flags & ~FightFlags.VictimEnemy });
                Check(string.Join(" ", old.Take(t0.AddSeconds(120)).Select(SayA)) == "player:Miquella monster death", "...an older decoder: no assists, the rest as before");

                // through the session: the PvP figure counts both, the split kept, carried over a restart, the overlay's hover
                var ra = new PacketReader(); ra.ReadingForTest();
                var sa = new TrackerSession(null, settings, items, "Test") { Packets = ra };
                sa.ApplyZone("Sulfur Tree Island", t0);
                foreach (var e in evs) ra.FightsForTest(e);
                sa.DrainPacketsForTest();
                var za = sa.Engine.ZoneSnapshot().GetValueOrDefault("Abyss");
                Check(sa.Engine.PlayerKills == 3 && sa.Engine.PlayerAssists == 2 && sa.Engine.MonsterKills == 1 && sa.Engine.Deaths == 1 && za?.PlayerKills == 3,
                    $"...counted: {sa.Engine.PlayerKills} PvP ({sa.Engine.PlayerAssists} of them assists), {sa.Engine.MonsterKills} monster, {sa.Engine.Deaths} death; zone Abyss {za?.PlayerKills} PvP");
                var seeded = new SessionEngine(items); seeded.Seed(sa.Engine.ToCarry("2026-11-18"));
                Check(seeded.PlayerKills == 3 && seeded.PlayerAssists == 2, $"...kept over a restart: {seeded.PlayerKills} PvP, {seeded.PlayerAssists} assists");
                string tip = KinahLedger.Ui.HudKit.AbyssTip(new Dictionary<string, string> { ["abPvp"] = "9", ["abAssists"] = "3" }) ?? "";
                string none = KinahLedger.Ui.HudKit.AbyssTip(new Dictionary<string, string> { ["abPvp"] = "9", ["abAssists"] = "" }) ?? "(none)";
                Check(tip == "9 PvP kills · 3 of them assists" && none == "(none)", $"...the overlay's hover: \"{tip}\", without assists {none}");
            }
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

        // bound drops in the Abyss (Steven, 2026-11-17: "tracker also missed a blue refining stone" ... "and raw leather"), from
        // the live log of 12:41:36 to 12:42:08 and of 2026-11-16 16:59:05 to 16:59:20: each side left the count to the other
        {
            var items = ItemDictionary.LoadOrEmpty(settings.ItemsFile);
            long stone = items.Find("Artisan's Refining Stone (Bound)") ?? 0, plainStone = items.Find("Artisan's Refining Stone") ?? 0;
            long leather = items.Find("Raw Leather (Bound)") ?? 0, dagger = items.Find("Earth Dagger") ?? 0, silentium = items.Find("Silentium (Bound)") ?? 0;
            const long Soul = 534660012, OathCoin = 930100019, Stigma = 531900010;
            Check(stone != 0 && plainStone != 0 && stone != plainStone && leather != 0 && dagger != 0 && silentium != 0, "bound drops: the item list has the items of the log");
            var t0 = DateTime.UtcNow.AddMinutes(-5);
            DateTime T(double s) => t0.AddSeconds(s);
            int batch = 1000;
            ItemChange Up(double at, long id, long delta, long total) => new(T(at), id, ItemOp.Changed, id % 97, delta, total, false, ++batch, 1, false);
            TrackerSession Abyss(out PacketReader r)
            {
                r = new PacketReader(); r.ReadingForTest();
                var x = new TrackerSession(null, settings, items, "Test") { Packets = r };
                x.Engine.SetZone("Chaotic Lower Reshanta");
                return x;
            }
            int B(TrackerSession x, long id) => x.Engine.ItemsBound.GetValueOrDefault(id);
            int U(TrackerSession x, long id) => x.Engine.Items.GetValueOrDefault(id);

            // 12:41:36.7 a monster's soul +1 (the kill; the game names no kill of the player's), 12:41:37.0 Artisan's Refining
            // Stone (Bound) +1, 12:41:40.0 the chat's "CArtisanIS$ Refining Stone (Bound) acquired." guessed as the plain stone
            var a = Abyss(out var ra);
            ra.ItemsForTest(Up(0, Soul, 1, 1)); ra.ItemsForTest(new ItemChange(T(0.1), Soul, ItemOp.Changed, 9, -1, 0, false, ++batch, 1, true));
            ra.ItemsForTest(Up(0.3, stone, 1, 1));
            a.DrainPacketsForTest();
            a.CommitForTest(new ItemAcquired(T(3.3), plainStone, 1, true) { Via = "chat · guessed" });
            a.DrainPacketsForTest(); ra.AgreeItemsForTest(DateTime.UtcNow); a.DrainPacketsForTest();
            Check(B(a, stone) == 1 && B(a, plainStone) + U(a, plainStone) + U(a, stone) == 0 && ra.Verdicts.LastOrDefault() == "covered",
                $"...a bound drop right after a kill (a monster's soul) counts once, bound, and the chat's guessed line of its plain twin is that pickup: {B(a, stone)} bound, {B(a, plainStone) + U(a, plainStone)} plain, the line {ra.Verdicts.LastOrDefault()}");
            Check(a.Engine.Items.Keys.Concat(a.Engine.ItemsBound.Keys).All(k => !ItemDictionary.IsMonsterSoul(k)), "...and the monster's soul, taken into the Soul Codex at once, is no loot");

            // 12:42:03.3 a soul +1, 12:42:04.9 Raw Leather (Bound) +1: the loot list showed it (one witness, never handed on), no chat line
            var l = Abyss(out var rl);
            rl.ItemsForTest(Up(0, Soul, 1, 1)); rl.ItemsForTest(Up(1.6, leather, 1, 1));
            l.DrainPacketsForTest(); rl.AgreeItemsForTest(DateTime.UtcNow); l.DrainPacketsForTest();
            Check(B(l, leather) == 1, $"...a bound drop no line named, a kill just before it, counts: Raw Leather (Bound) {B(l, leather)}");

            // 2026-11-16 16:59: bound Kinah counted every second or so from the game's data, no kill of the player's named for
            // 15 s (a Force's kills), then Earth Dagger +1 at 16:59:20 (the loot list showed it): still farming, a drop
            var d = Abyss(out var rd);
            d.CommitForTest(new CurrencyGained(T(0), "Swarm Soul Points", 1, "Swarm") { Via = "chat" });
            rd.AmountForTest(CurrencyKind.BoundKinah, 166_000, T(0.5));
            long kina = 166_000;
            foreach (var sec in new[] { 2.0, 6, 10, 14, 18 }) { kina += 20; rd.AmountForTest(CurrencyKind.BoundKinah, kina, T(sec)); d.DrainPacketsForTest(); }
            rd.ItemsForTest(Up(19, dagger, 1, 1));
            d.DrainPacketsForTest();
            Check(U(d, dagger) == 1, $"...a drop among the game's counted Kinah, no kill of the player's named for 19 s, counts: Earth Dagger {U(d, dagger)}");

            // rewards stay out: Oath Coin +1,280 by its game id (09:01:16, a reward screen just closed), Stigma Shard +5 gone in
            // the next update (08:51:09, bought), Silentium (Bound) +1 with no kill or pickup line around it (09:01:16)
            var w = Abyss(out var rw);
            w.CommitForTest(new CurrencyGained(T(0), "Swarm Soul Points", 1, "Swarm") { Via = "chat" });
            rw.ItemsForTest(Up(1, OathCoin, 1_280, 79_500));
            rw.ItemsForTest(Up(2, Stigma, 5, 5)); rw.ItemsForTest(new ItemChange(T(2.1), Stigma, ItemOp.Changed, 3, -5, 0, false, ++batch, 1, true));
            rw.ItemsForTest(Up(40, silentium, 1, 2));
            w.DrainPacketsForTest();
            long oath = items.FromGame(OathCoin), stigma = items.FromGame(Stigma);
            Check(oath != OathCoin && U(w, oath) + B(w, oath) == 0 && U(w, stigma) + B(w, stigma) == 0 && B(w, silentium) == 0,
                $"...rewards stay out: Oath Coin x1,280 at once {U(w, oath) + B(w, oath)}, Stigma Shard used at once {U(w, stigma) + B(w, stigma)}, Silentium (Bound) with no kill {B(w, silentium)}");
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
            // a craft the game told is a craft done: the activity is Crafting (0.10.87), as it is from the panel's own race
            c.UpdateDoing(t0.AddSeconds(48));
            Check(c.Doing.Current.Kind == Session.ActivityKind.Crafting, $"...a craft told by the game makes the activity Crafting: {c.Doing.Current.Label}");

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
            Check(unknownFirst && tot.GetValueOrDefault(Id("Orichalcum Ore")) == 31 && !tot.ContainsKey(Id("Orichalcum Guard")) && bag.Totals(all: true)!.GetValueOrDefault(Id("Orichalcum Ore")) == 774 && bag.BagStacks == 3
                && bag.WornGear() is [{ Slot: 2, Level: 12 }]
                && after.GetValueOrDefault(Id("Orichalcum Ore")) == 32 && after.GetValueOrDefault(Id("Odyle")) == 1 && !after.ContainsKey(Id("Life Potion (Bound)")) && bag.Money(CurrencyKind.Kinah) == 4_728_099 && bag.LastRows == (5, 5),
                $"the inventory: unknown before its snapshot, then the bag only: Orichalcum Ore {tot.GetValueOrDefault(Id("Orichalcum Ore"))} (the 742 in storage left out), the gear worn apart; after the changes {after.GetValueOrDefault(Id("Orichalcum Ore"))} ore, {after.GetValueOrDefault(Id("Odyle"))} Odyle, the potions gone, Kinah {bag.Money(CurrencyKind.Kinah):N0}");
            // its worth: the tradable items at the price, a bound item no value (it can't be sold), unpriced counted; the change since the first snapshot
            var prices = new Dictionary<string, long> { ["Orichalcum Ore"] = 2_000, ["Life Potion"] = 100 };
            var w = Session.BagValue.Of(after, bag.FirstTotals, items, n => prices.TryGetValue(n, out var v) ? v : null, 0.9);
            Check(w.Total == 32 * 2_000 && w.Sellable == (long)Math.Round(32 * 2_000 * 0.9) && w.Unpriced == 1 && w.Change == 2_000 && w.BoundKinds == 0 && w.Lines[0].Name == "Orichalcum Ore",
                $"...worth {w.Total:N0}, sellable {w.Sellable:N0} after a 10% cut, {w.Unpriced} kind without a price (Odyle), {w.Change:N0} since the first (one ore up; the bound potions gone had no value)");
            // a bound item has no value and is counted apart
            var wb = Session.BagValue.Of(new Dictionary<long, long> { [Id("Life Potion (Bound)")] = 10 }, null, items, n => prices.TryGetValue(n, out var v) ? v : null, 0.9);
            Check(wb.Total == 0 && wb.Sellable == 0 && wb.Unpriced == 0 && wb.Change == null && wb.Lines[0].Bound && wb.Lines[0].Price == null && wb is { BoundKinds: 1, BoundCount: 10 },
                $"...a bound item has no value (it can't be sold), counted apart: worth {wb.Total:N0}, {wb.BoundCount} bound");
            // a new connection: unknown until its own snapshot; the first snapshot stays the session's start
            r.Bag.Unknown();
            Check(bag.Totals() == null && bag.FirstAt == t0 && bag.FirstTotals.GetValueOrDefault(Id("Orichalcum Ore")) == 31, "...a new connection makes it unknown again, and the session's start stays the first snapshot");
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
                    Check(differ == 0 && agree > 40 && kinahSame && a.LastRows.Said == a.LastRows.Read && b2.LastRows.Said == b2.LastRows.Read,
                        $"the inventory in the recordings: the 18:49 snapshot ({a.LastRows.Read} of {a.LastRows.Said} stacks) kept up with every change equals the 20:01 one ({b2.LastRows.Read} of {b2.LastRows.Said}) for {agree} items in the bag, {differ} differ; Kinah at the first connection's end {a.Money(CurrencyKind.Kinah):N0} (the second snapshot says 4,922,940)");
                }
                else Check(false, $"the inventory in the recordings: {books.Count(b => b.Snapshots > 0)} connections with a snapshot (expected 2)");
            }
        }

        // a gather's yield counts at once with the gather as its evidence, a first-seen stack too (the gather's count, one
        // for a rare find), topped up when the chat's line says more; one gather's two items are two rises, so the ore's line
        // never covers the bundle's; a first-seen kill drop counts at its chat line, not 15 s later (2026-11-14 13:09:57)
        {
            const long Ore = 610620013, Fine = 610620014, Poko = 533700110, Spirit = 0;
            var items = ItemDictionary.LoadOrEmpty(settings.ItemsFile);
            long spirit = items.Find("Spiritstone") ?? Spirit;
            var t0 = DateTime.UtcNow.AddMinutes(-2);
            var clock = t0;
            PacketClock.Fixed = () => clock;
            try
            {
                var reader = new PacketReader(); reader.ReadingForTest(); reader.GathersForTest();
                var s = new TrackerSession(null, settings, items, "Test") { Packets = reader };
                s.Engine.SetZone("Ellun River Midstream");
                var when = new Dictionary<long, double>();
                s.Parsed += ev => { if (ev is ItemAcquired ia && !when.ContainsKey(ia.ItemId)) when[ia.ItemId] = (clock - ia.Time).TotalSeconds; };
                ItemChange R(double at, long id, ItemOp op, long stack, long d, long total, bool first, int batch, int rows) => new(t0.AddSeconds(at), id, op, stack, d, total, first, batch, rows, false);
                void At(double sec) { clock = t0.AddSeconds(sec); reader.ReadingForTest(); s.DrainPacketsForTest(); }
                // 1 s: the gather (an ore) and, in the same update, the ore +1 and the bundle's stack seen for the first time
                At(1); reader.GathersForTest(new GatherResult(t0.AddSeconds(1), 7, 1, GatherEnd.Worked, Ore, 1, 2));
                reader.ItemsForTest(R(1, Ore, ItemOp.Changed, 1, 1, 195, false, 1, 2), R(1, Poko, ItemOp.Changed, 2, 0, 2, true, 1, 2));
                At(1.05);
                // the chat's lines, 0.8 and 1.7 s later
                At(1.8); s.CommitForTest(new ItemAcquired(t0.AddSeconds(1.8), Ore, 1, false) { Via = "chat" });
                At(2.7); s.CommitForTest(new ItemAcquired(t0.AddSeconds(2.7), Poko, 1, true) { Via = "chat" });
                // 10 s: an upgrade roll's Fine Orichalcum Ore, its stack first seen; the gather says one, the chat says two
                At(10); reader.GathersForTest(new GatherResult(t0.AddSeconds(10), 8, 1, GatherEnd.Upgrade, Fine, 1, 1));
                reader.ItemsForTest(R(10, Fine, ItemOp.Changed, 3, 0, 38, true, 2, 1));
                At(10.05);
                At(11.5); s.CommitForTest(new ItemAcquired(t0.AddSeconds(11.5), Fine, 2, false) { Via = "chat" });
                // 30 s: a kill's Spiritstone, its stack first seen (no gather): its chat line 1 s later counts it then
                At(29); reader.FightsForTest(new FightEvent(t0.AddSeconds(29), FightKind.Kill, FightFlags.Mine, 1, 2, 3, 0, 0, "", ""));
                At(30); reader.ItemsForTest(R(30, spirit, ItemOp.Changed, 4, 0, 6, true, 3, 1));
                At(31); s.CommitForTest(new ItemAcquired(t0.AddSeconds(31), spirit, 1, false) { Via = "chat" });
                for (double x = 32; x <= 60; x += 1) { At(x); reader.AgreeItemsForTest(clock); s.DrainPacketsForTest(); }
                int M(long id) => s.Engine.Items.GetValueOrDefault(id) + s.Engine.ItemsBound.GetValueOrDefault(id);
                Check(M(Ore) == 1 && M(Poko) == 1 && M(Fine) == 2 && (spirit == 0 || M(spirit) == 1)
                        && when.GetValueOrDefault(Ore, 9) <= 0.1 && when.GetValueOrDefault(Poko, 9) <= 0.1 && when.GetValueOrDefault(Fine, 9) <= 0.1 && (spirit == 0 || when.GetValueOrDefault(spirit, 9) <= 1.1),
                    $"a gather's yield counts at once: Orichalcum Ore {M(Ore)} and Poko's Skin Bundle {M(Poko)} (one gather, its first sight; {when.GetValueOrDefault(Poko, -1):0.00} s), Fine Orichalcum Ore {M(Fine)} (the gather's one, the chat's line topped it up to two), a kill's first-seen Spiritstone {(spirit == 0 ? "(not on this list)" : $"{M(spirit)} at its chat line ({when.GetValueOrDefault(spirit, -1):0.00} s)")}");
            }
            finally { PacketClock.Fixed = null; }
        }

        // two harvest procs on one gather (Steven, 2026-11-15: "sometimes if plentiful and bountiful harvest proc at same time
        // it only logs 1 of the resources"). No such gather is in his logs (Nov 14 10:34 to Nov 15 14:23), so these are built
        // from the formats the game uses: "Acquired Orichalcum Ore
        // x2." and "Orichalcum Ore acquired.", the rows of one update, a gather's report. Each shape counts every resource at
        // its own amount. A stack seen for the first time is counted from the gather's report, and its chat lines add up (two
        // lines for one gather: x2 and x1 are three). The item's next rise ends that, so the next gather's line never tops up
        // the first one. A chat read shown over and over counts its two lines once each.
        {
            const long Ore = 610620013, Fine = 610620014;
            var items = ItemDictionary.LoadOrEmpty(settings.ItemsFile);
            var t0 = DateTime.UtcNow.AddMinutes(-2);
            string Two(Action<PacketReader, TrackerSession, Action<double>, Func<double, long, long, long, bool, int, int, ItemChange>> body)
            {
                var clock = t0; PacketClock.Fixed = () => clock;
                try
                {
                    var reader = new PacketReader(); reader.ReadingForTest(); reader.GathersForTest();
                    var s = new TrackerSession(null, settings, items, "Test") { Packets = reader, Vote = false };
                    s.Engine.SetZone("Ellun River Midstream");
                    void At(double sec) { clock = t0.AddSeconds(sec); reader.ReadingForTest(); s.DrainPacketsForTest(); }
                    ItemChange R(double at, long id, long d, long total, bool first, int batch, int rows) => new(t0.AddSeconds(at), id, ItemOp.Changed, 1, d, total, first, batch, rows, false);
                    body(reader, s, At, R);
                    for (double x = 20; x <= 60; x += 1) { At(x); reader.AgreeItemsForTest(clock); s.DrainPacketsForTest(); }
                    int M(long id) => s.Engine.Items.GetValueOrDefault(id) + s.Engine.ItemsBound.GetValueOrDefault(id);
                    return $"Ore {M(Ore)}, Fine {M(Fine)}{(reader.ItemsBroken != null ? ", items broken" : "")}";
                }
                finally { PacketClock.Fixed = null; }
            }
            ItemAcquired C(double at, long id, int n) => new(t0.AddSeconds(at), id, n, false) { Via = "chat" };
            GatherResult G(double at, int attempt = 1, long item = Ore) => new(t0.AddSeconds(at), 7, 1, GatherEnd.Worked, item, 1, attempt);
            var got = new List<(string What, string Got, string Want)>
            {
                ("two items in one update", Two((rd, s, At, R) => { At(1); rd.GathersForTest(G(1)); rd.ItemsForTest(R(1, Ore, 2, 100, false, 1, 2), R(1, Fine, 1, 10, false, 1, 2)); At(1.05); At(2); s.CommitForTest(C(2, Ore, 2)); s.CommitForTest(C(2, Fine, 1)); }), "Ore 2, Fine 1"),
                ("one item's two rises in one update", Two((rd, s, At, R) => { At(1); rd.GathersForTest(G(1)); rd.ItemsForTest(R(1, Ore, 2, 100, false, 1, 2), R(1, Ore, 1, 101, false, 1, 2)); At(1.05); At(2); s.CommitForTest(C(2, Ore, 2)); s.CommitForTest(C(2, Ore, 1)); }), "Ore 3, Fine 0"),
                ("one item in two updates, 1.8 s apart", Two((rd, s, At, R) => { At(1); rd.GathersForTest(G(1)); rd.ItemsForTest(R(1, Ore, 2, 100, false, 1, 1)); At(1.05); At(2.8); rd.ItemsForTest(R(2.8, Ore, 1, 101, false, 2, 1)); At(2.85); At(3); s.CommitForTest(C(3, Ore, 2)); At(3.5); s.CommitForTest(C(3.5, Ore, 1)); }), "Ore 3, Fine 0"),
                ("first sight, lines x2 and x1", Two((rd, s, At, R) => { At(1); rd.GathersForTest(G(1)); rd.ItemsForTest(R(1, Ore, 0, 100, true, 1, 1)); At(1.05); At(2); s.CommitForTest(C(2, Ore, 2)); At(2.4); s.CommitForTest(C(2.4, Ore, 1)); }), "Ore 3, Fine 0"),
                ("first sight, two lines x1", Two((rd, s, At, R) => { At(1); rd.GathersForTest(G(1)); rd.ItemsForTest(R(1, Ore, 0, 100, true, 1, 1)); At(1.05); At(2); s.CommitForTest(C(2, Ore, 1)); At(2.4); s.CommitForTest(C(2.4, Ore, 1)); }), "Ore 2, Fine 0"),
                ("first sight of both items, x2 and Fine x1", Two((rd, s, At, R) => { At(1); rd.GathersForTest(G(1)); rd.ItemsForTest(R(1, Ore, 0, 100, true, 1, 2), R(1, Fine, 0, 10, true, 1, 2)); At(1.05); At(2); s.CommitForTest(C(2, Ore, 2)); s.CommitForTest(C(2, Fine, 1)); }), "Ore 2, Fine 1"),
                ("first sight x1, the next gather's x2 5 s on", Two((rd, s, At, R) => { At(1); rd.GathersForTest(G(1)); rd.ItemsForTest(R(1, Ore, 0, 100, true, 1, 1)); At(1.05); At(2); s.CommitForTest(C(2, Ore, 1)); At(6); rd.GathersForTest(G(6, 2)); rd.ItemsForTest(R(6, Ore, 2, 102, false, 2, 1)); At(6.05); At(7); s.CommitForTest(C(7, Ore, 2)); }), "Ore 3, Fine 0"),
                // Steven's live log, 2026-11-15 12:13:20.2: an upgrade roll's Fine Orichalcum Ore, its stack first seen (counted
                // as the report's one), "Acquired Fine Orichalcum Ore x2." at 21.5 (one more); the next gather's report and its
                // +2 at 23.9, and that gather's own x2 line read at 24.2, before the session drained the rise: 4, not 6
                ("12:13:20 a first sight's x2, then the next gather's x2 read ahead of its rise", Two((rd, s, At, R) =>
                {
                    At(0.2); rd.GathersForTest(new GatherResult(t0.AddSeconds(0.2), 9, 1, GatherEnd.Upgrade, Fine, 1, 0)); rd.ItemsForTest(R(0.2, Fine, 0, 18, true, 1, 1)); At(0.4);
                    At(1.5); s.CommitForTest(C(1.5, Fine, 2));
                    rd.GathersForTest(new GatherResult(t0.AddSeconds(3.9), 9, 1, GatherEnd.Upgrade, Fine, 1, 0)); rd.ItemsForTest(R(3.9, Fine, 2, 20, false, 2, 1));
                    s.CommitForTest(C(4.2, Fine, 2)); At(4.2);
                }), "Ore 0, Fine 4"),
                ("first sight, its two lines read over and over", Two((rd, s, At, R) =>
                {
                    var calm = new List<string> { "[P1] so the boss", "[P2] spawns at ten", "[P3] ok thanks" };
                    At(0.5); s.ProcessChat(calm, t0.AddSeconds(0.5));
                    At(1); rd.GathersForTest(G(1)); rd.ItemsForTest(R(1, Ore, 0, 100, true, 1, 1)); At(1.05);
                    for (double x = 1.8; x <= 12; x += 0.6) { At(x); s.ProcessChat(new List<string>(calm) { "Acquired Orichalcum Ore x2.", "Orichalcum Ore acquired." }, t0.AddSeconds(x)); }
                    s.FlushVotes(t0.AddSeconds(14));
                }), "Ore 3, Fine 0"),
            };
            foreach (var (what, g, want) in got) Check(g == want, $"two harvest procs on one gather, {what}: {g} (expected {want})");
        }

        // Steven's gathering of 2026-11-14 13:00-13:15 (kinah-bench/packets/rec-20261114-13 and -13-2, the tracker's own
        // recording, with reads.log's lines of those minutes): through this build's decoder and a whole session, stepped on
        // the recording's clock. The Poko's Skin Bundle (Bound) of 13:09:57 counts (0.10.92 dropped it: a stack seen for the
        // first time, its chat line taken as a misread of the ore's), and every gathered item counts within 0.3 s of the
        // game's message (a Fine Orichalcum Ore waited 1.7 s for its line, the first-seen ones 17 and 19 s)
        {
            string? bench2 = Environment.GetEnvironmentVariable("KL_BENCH") is { Length: > 0 } kb2 ? kb2 : BenchPackets();
            var recs = new[] { "rec-20261114-13", "rec-20261114-13-2" }.Select(r => bench2 == null ? "" : Path.Combine(bench2, r)).ToList();
            if (!NativeDecoder.Available || NativeDecoder.Version < 7) Console.WriteLine("skip  the 13:00 gathering replay: this build's decoder reads no gathering");
            else if (recs.Any(r => !File.Exists(Path.Combine(r, "capture.pcapng")))) Console.WriteLine("skip  the 13:00 gathering replay: kinah-bench/packets/rec-20261114-13* not found (KL_BENCH)");
            else
            {
                var items = ItemDictionary.LoadOrEmpty(settings.ItemsFile);
                var res = ReplaySession(settings, recs, "Ellun River Midstream");
                const long Poko = 533700110;
                bool poko = res.Counted.Any(c => c.Ev.ItemId == Poko);
                var gathered = res.Reported.Where(r => r.Gathered).ToList();
                var late = gathered.Where(r => r.Delay is not double d || d > 0.3).ToList();
                var all = res.Reported.Where(r => r.Delay is double).Select(r => r.Delay!.Value).OrderBy(d => d).ToList();
                Console.WriteLine($"      the 13:00-13:15 recording: the game reported {res.Reported.Count} pickups ({gathered.Count} gathered), {res.Reported.Count(r => r.Delay != null)} counted; delay median {(all.Count > 0 ? all[all.Count / 2] : 0):0.00} s, max {(all.Count > 0 ? all[^1] : 0):0.00} s; {res.Counted.Count} counts, {res.Counted.Sum(c => c.Ev.Count)} items");
                foreach (var r in res.Reported.Where(r => r.Delay is not double d || d > 0.3 || !r.Gathered))
                    Console.WriteLine($"      {r.At.ToLocalTime():HH:mm:ss.ff} {items.NameOf(r.Id)} {(r.Delay is double d ? $"counted {d:0.00} s after" : "NOT counted")}{(r.Gathered ? " (gathered)" : "")}");
                // a first sight no gather gave (a kill's drop) can only count from its chat line: counted, at the line's lag
                Check(poko && late.Count == 0 && res.Reported.All(r => r.Delay != null),
                    $"the 13:00 gathering, replayed: Poko's Skin Bundle (Bound) {(poko ? "counted" : "NOT counted")}; {gathered.Count - late.Count} of {gathered.Count} gathered items within 0.3 s of the game's message; {res.Reported.Count(r => r.Delay != null)} of {res.Reported.Count} pickups the game reported counted");
            }
        }

        // the Shugo Festival runs of 2026-11-15 (07:03 and 11:03 local) and 2026-11-14 (11:03, 12:03), each hour's recording through this build's
        // decoder and a whole session with its currencies and the event's own parts: the run opens, its rewards come and it
        // closes as live, with no reward cards read at all. Live, both were filed from the cards as partly read (07:05: 4 of
        // 40 places not read, Abyss Points x1,020; 11:05: 12 of 40, Abyss Points x800) and each was one to check
        {
            string? bench3 = Environment.GetEnvironmentVariable("KL_BENCH") is { Length: > 0 } kb3 ? kb3 : BenchPackets();
            var want = new (string Rec, long Stone, long Root, long Ap, int Gear, string Hour, string Event)[]
            {
                ("rec-20261115-07", 2100, 150, 1000, 11, "Nov 15 07:03", "Defend Shugo Merchants"), ("rec-20261115-11", 3300, 190, 2000, 10, "Nov 15 11:03", "Defend Shugo Merchants"),
                // and the day before's (live: 4 and 6 places not read): Hidden Lugi, whose cards read Abyss Points x2,100 for the game's 2,080
                ("rec-20261114-11", 3300, 190, 2080, 10, "Nov 14 11:03", "Hidden Lugi"), ("rec-20261114-12", 3300, 180, 2000, 10, "Nov 14 12:03", "Defend Shugo Merchants"),
            };
            if (!NativeDecoder.Available || NativeDecoder.Version < 8) Console.WriteLine("skip  the Shugo Festival replays: this build's decoder reads no inventory");
            else foreach (var w in want)
            {
                var rec = bench3 == null ? "" : Path.Combine(bench3, w.Rec);
                if (!File.Exists(Path.Combine(rec, "capture.pcapng"))) { Console.WriteLine($"skip  the {w.Hour} Shugo Festival replay: kinah-bench/packets/{w.Rec} not found (KL_BENCH)"); continue; }
                var items = ItemDictionary.LoadOrEmpty(settings.ItemsFile);
                var res = ReplaySession(settings, new[] { rec }, "Eastern Cantas Valley", events: true);
                var run = res.Runs?.LastOrDefault(r => r.Event == w.Event);
                long Of(string name) => run == null ? -1 : run.Items.GetValueOrDefault(items.IdOf(name)) + run.BoundItems.GetValueOrDefault(items.IdOf(name));
                int gear = run?.Items.Where(kv => items.IsGear(kv.Key)).Sum(kv => kv.Value) ?? -1;
                Check(run is { FromGame: true, PartlyRead: false, SlotsUnread: 0, ToLookAt: false, KinahUnknown: false, Kinah: 0 } && run.GameUnknown.Count == 0
                    && Of("Enhance Stone") == w.Stone && Of("Centuryroot Token") == w.Root && Of("Abyss Points") == w.Ap && gear == w.Gear,
                    $"the {w.Hour} {w.Event}, replayed: {(run == null ? "no run" : $"{(run.FromGame ? "from the game" : "not from the game")}, {(run.PartlyRead ? "partly read" : "complete")}, {(run.ToLookAt ? "to check" : "nothing to check")}")}; "
                    + $"{gear} gear, Enhance Stone x{Of("Enhance Stone"):N0}, Centuryroot Token x{Of("Centuryroot Token")}, Abyss Points x{Of("Abyss Points"):N0}, {run?.Kinah:N0} Kinah, {run?.GameUnknown.Count} not known");
                // the reward's chat lines, however late they came, are the run's to the agreement check: no misses, items stay the game's
                var vs = res.Verdicts ?? new();
                Check(res.ItemsBroken == null,
                    $"...its agreement check: {(res.ItemsBroken == null ? "items stay the game's" : "BROKEN (" + res.ItemsBroken + ")")}; {vs.Count} line(s): {string.Join(", ", vs.GroupBy(v => v).Select(g => $"{g.Key} {g.Count()}"))}");
            }
        }

        // an event's rewards from the game's own item data (2026-11-15, Steven: "all shugo festival runs right now are requiring
        // review - it shouldn't"). Defend Shugo Merchants, 11:03 to 11:05:39 local that day: the reward cards' lists run past
        // their windows, so the live read filed "23 piece(s) (8 gear, 11 unknown item(s)); currencies: Enhance Stone x2,500,
        // Abyss Points x800, Centuryroot Token x100 from its slots, 12 slot(s) not read (partly read ...)", one to check. The
        // recording of that hour (packets\20261115-11.pcapng, replayed) has the whole reward in one update at 11:04:59.04 and
        // Abyss Points +2,000; no Kinah came. These are its rows.
        {
            var items = ItemDictionary.LoadOrEmpty(settings.ItemsFile);
            DateTime L(string hms) => new DateTime(2026, 11, 15, 5, 0, 0, DateTimeKind.Utc) + TimeSpan.Parse(hms);   // local (UTC-5) to UTC
            var reward = new (string Name, long Id, long N)[]
            {
                ("Forgotten Mace", 0, 1), ("item 930100023", 930100023, 3300), ("item 930100011", 930100011, 190), ("Ethereal Helm", 0, 1), ("Temple Guardian Helm", 0, 1),
                ("Forgotten Breastplate", 0, 1), ("Forgotten Spellbook", 0, 1), ("Ethereal Spellbook", 0, 2), ("Riverside Gloves", 0, 2), ("Forgotten Cloak", 0, 1),
                ("Fine Odyle", 0, 3), ("Odyle", 0, 7), ("Expert's Refining Stone", 0, 1), ("Refining Stone", 0, 5), ("item 535400001", 535400001, 3), ("Seed of Detection", 0, 1),
            };
            long IdOf(string name, long id) => id != 0 ? id : items.IdOf(name);
            int batch = 900;
            ItemChange[] Rows(DateTime at, IEnumerable<(string Name, long Id, long N)> rs, bool first = false)
            {
                batch++; var l = rs.ToList();
                return l.Select((r, i) => new ItemChange(at, IdOf(r.Name, r.Id), ItemOp.Changed, 7000 + i, first ? 0 : r.N, 50 + r.N, first, batch, l.Count, false)).ToArray();
            }
            AreaEvent Area(string hms, AreaEvent.Kind k, AreaEvent.Stage st) => new(L(hms), null, k, st, L("11:03:07"), "Shugo Festival", "Defend Shugo Merchants", "", 1);
            // the live read's filing of its cards (TrackerSession.ApplyGrid): what it named, 12 of 40 places not read
            void Cards(TrackerSession s, string hms)
            {
                var at = L(hms);
                var slots = new GameEvent[]
                {
                    new ItemAcquired(at, items.IdOf("Abyss Points"), 800, false) { Via = "reward table" }, new ItemAcquired(at, items.IdOf("Enhance Stone"), 2500, false) { Via = "reward table" },
                    new ItemAcquired(at, items.IdOf("Centuryroot Token"), 100, false) { Via = "reward table" }, new ItemAcquired(at, items.IdOf("Forgotten Mace"), 1, false) { Via = "reward table" },
                };
                s.Instances.RewardTable(at, slots, 12, Enumerable.Range(0, 12).Select(i => $"c3.{i} · not read").ToList(), false, 11, null, partly: true, pictureSlots: 40);
            }
            (TrackerSession S, PacketReader R) Run(bool cardsFirst, IEnumerable<(string Name, long Id, long N)>? rs = null, bool firstSeen = false, long kinah = 0, bool farmAfter = false, Action<TrackerSession, PacketReader>? chat = null)
            {
                var r = new PacketReader(); r.ReadingForTest();
                var s = new TrackerSession(null, settings, items, "Test") { Packets = r };
                s.ApplyZone("Shugo Festival", L("11:03:18"));
                r.AmountForTest(CurrencyKind.AbyssPoints, 10_348, L("11:03:03.59")); r.AmountForTest(CurrencyKind.Kinah, 6_063_883, L("11:03:03.59")); r.AmountForTest(CurrencyKind.BoundKinah, 32_640, L("11:03:03.59"));
                s.Instances.GameSaid(Area("11:03:08.3", AreaEvent.Kind.Phase, AreaEvent.Stage.Preparing), "Shugo Festival");
                s.Instances.GameSaid(Area("11:03:21.1", AreaEvent.Kind.Phase, AreaEvent.Stage.UnderWay), "Shugo Festival");
                s.Instances.GameSaid(Area("11:04:48.7", AreaEvent.Kind.Over, AreaEvent.Stage.Over), "Shugo Festival");
                if (cardsFirst) Cards(s, "11:04:58.9");
                r.AmountForTest(CurrencyKind.AbyssPoints, 12_348, L("11:04:59.04"));
                if (kinah > 0) r.AmountForTest(CurrencyKind.Kinah, 6_063_883 + kinah, L("11:04:59.04"));
                r.ItemsForTest(Rows(L("11:04:59.04"), rs ?? reward, firstSeen));
                s.DrainPacketsForTest();
                chat?.Invoke(s, r);
                if (!cardsFirst) Cards(s, "11:05:12.1");
                s.Instances.GameSaid(Area("11:05:39.7", AreaEvent.Kind.Closed, AreaEvent.Stage.UnderWay), "Shugo Festival");
                if (farmAfter)
                {
                    // out in the open world within its time for rewards: a kill's Abyss Points and a gathered Odyle are farming's
                    r.AmountForTest(CurrencyKind.AbyssPoints, 12_398, L("11:05:50"));
                    r.ItemsForTest(Rows(L("11:05:50"), new[] { ("Odyle", 0L, 1L) }));
                    s.DrainPacketsForTest();
                }
                s.Instances.Close();
                return (s, r);
            }
            string Say(InstanceRun? x) => x == null ? "no run" : $"{(x.FromGame ? "from the game" : "from the cards")}, {(x.PartlyRead ? "partly read" : "complete")}, {x.SlotsUnread} not read, {(x.ToLookAt ? "to check" : "nothing to check")}";
            long N(InstanceRun? x, string name) => x == null ? -1 : x.Items.GetValueOrDefault(items.IdOf(name)) + x.BoundItems.GetValueOrDefault(items.IdOf(name));

            var (s1, _) = Run(cardsFirst: false);
            var r1 = s1.Instances.Snapshot().LastOrDefault();
            var sum1 = s1.Score.Snapshot(r1 == null ? null : new[] { r1 });
            int gear1 = r1?.Items.Where(kv => items.IsGear(kv.Key)).Sum(kv => kv.Value) ?? -1;
            Check(r1 is { FromGame: true, PartlyRead: false, SlotsUnread: 0, ToLookAt: false, KinahUnknown: false, Kinah: 0 } && sum1.RunsPartly == 0 && sum1.RunsFull == 1,
                $"11:05 Defend Shugo Merchants, its cards read in part (12 of 40 not read): {Say(r1)}; {sum1.RunsPartly} run(s) to check");
            Check(N(r1, "Enhance Stone") == 3300 && N(r1, "Centuryroot Token") == 190 && N(r1, "Abyss Points") == 2000 && gear1 == 10 && N(r1, "Odyle") == 7 && N(r1, "Fine Odyle") == 3 && N(r1, "Refining Stone") == 5,
                $"...the game's counts: Enhance Stone x{N(r1, "Enhance Stone"):N0} (cards 2,500), Centuryroot Token x{N(r1, "Centuryroot Token")} (cards 100), Abyss Points x{N(r1, "Abyss Points"):N0} (cards 800), {gear1} gear, Odyle x{N(r1, "Odyle")}");
            long codex = items.IdOf("Soul Codex (Bound)");
            Check(r1 != null && r1.BoundItems.GetValueOrDefault(codex) == 3 && !r1.Items.ContainsKey(codex) && r1.Bound == 3,
                $"...Soul Codex (Bound) x{r1?.BoundItems.GetValueOrDefault(codex)} among the bound items (no value), {r1?.Bound} bound in all");
            // the cards filed before the game's update came: the update replaces them all the same
            var (s2, _) = Run(cardsFirst: true);
            var r2 = s2.Instances.Snapshot().LastOrDefault();
            Check(r2 is { FromGame: true, PartlyRead: false, SlotsUnread: 0 } && N(r2, "Abyss Points") == 2000 && N(r2, "Enhance Stone") == 3300,
                $"...the cards filed first, the game's update after: {Say(r2)}, Abyss Points x{N(r2, "Abyss Points"):N0}");
            var (s7, _) = Run(cardsFirst: false, farmAfter: true);
            var r7 = s7.Instances.Snapshot().LastOrDefault();
            Check(r7 is { FromGame: true } && N(r7, "Abyss Points") == 2000 && N(r7, "Odyle") == 7, $"...after leaving it, a kill's Abyss Points and a gathered Odyle aren't its: Abyss Points x{N(r7, "Abyss Points"):N0}, Odyle x{N(r7, "Odyle")}");
            // the reward's chat lines, read late (2026-11-15: the reward screen held the chat unread from 11:04:59 to 11:05:11,
            // and the 16 lines then scrolled by): live, the agreement check called seven of them misses at 11:05:29.8 ("the
            // screen read Riverside Gloves x1 at 11:05:11.4 that the game's messages didn't show") and items went back to the
            // screen. The run took those rises as its rewards: each covers its own line, however late, and nothing is a miss.
            // These are the lines it read then (Fine Odyle's x3 read as x35)
            var lateLines = new (string Name, int N, bool Bound)[]
            {
                ("Riverside Gloves", 1, false), ("Forgotten Cloak", 1, false), ("Fine Odyle", 35, false), ("Expert's Refining Stone", 1, false),
                ("Refining Stone", 5, false), ("Odyle", 7, false), ("Soul Codex (Bound)", 3, true),
            };
            var (s8, r8) = Run(cardsFirst: false, chat: (s, r) =>
            {
                foreach (var (name, n, bound) in lateLines) s.CommitForTest(new ItemAcquired(L("11:05:11.4"), items.IdOf(name), n, bound) { Via = "chat" });
                s.DrainPacketsForTest(); r.AgreeItemsForTest(DateTime.UtcNow); s.DrainPacketsForTest();
            });
            var r8r = s8.Instances.Snapshot().LastOrDefault();
            int E8(string name) => s8.Engine.Items.GetValueOrDefault(items.IdOf(name)) + s8.Engine.ItemsBound.GetValueOrDefault(items.IdOf(name));
            int E1(string name) => s1.Engine.Items.GetValueOrDefault(items.IdOf(name)) + s1.Engine.ItemsBound.GetValueOrDefault(items.IdOf(name));
            bool sameCounts = lateLines.All(l => E8(l.Name) == E1(l.Name)) && s8.Engine.Items.Values.Sum() == s1.Engine.Items.Values.Sum();
            var v8 = r8.Verdicts.ToList();
            Check(r8.ItemsBroken == null && v8.Count == 7 && v8.All(v => v == "covered: the run's reward") && r8r is { FromGame: true, PartlyRead: false, ToLookAt: false }
                && N(r8r, "Odyle") == 7 && N(r8r, "Fine Odyle") == 3 && sameCounts,
                $"...its lines read 12.4 s late, as at 11:05:11.4: {(r8.ItemsBroken == null ? "items still the game's" : "BROKEN: " + r8.ItemsBroken)}; {v8.Count(v => v == "covered: the run's reward")} of {v8.Count} lines the run's reward ({string.Join(", ", v8.Distinct())}); "
                + $"{Say(r8r)}; farming's counts {(sameCounts ? "as without the lines" : "CHANGED by the lines")} (Odyle {E8("Odyle")}, Fine Odyle {E8("Fine Odyle")}, {s8.Engine.Items.Values.Sum()} items in all)");
            // a real disagreement still trips the check: out of the run, an Odyle line past what the reward gave (its line in
            // two minutes before) and two Fresh Raw Meat lines, none of which the game's updates showed: three misses
            foreach (var (hms, name, n) in new[] { ("11:07:30", "Odyle", 7), ("11:07:40", "Fresh Raw Meat", 1), ("11:07:50", "Fresh Raw Meat", 1) })
                s8.CommitForTest(new ItemAcquired(L(hms), items.IdOf(name), n, false) { Via = "chat" });
            s8.DrainPacketsForTest(); r8.AgreeItemsForTest(DateTime.UtcNow); s8.DrainPacketsForTest();
            Check(r8.ItemsBroken != null && r8.Verdicts.Count == 10 && r8.Verdicts.Skip(7).All(v => v == "not shown"),
                $"...and three lines the game's updates never showed, the reward's line long in: {string.Join(", ", r8.Verdicts.Skip(7))}; {(r8.ItemsBroken != null ? "items back to the screen" : "NOT tripped")}");
            // an event's Kinah, by the game's count: the run's, not set aside
            var (s3, _) = Run(cardsFirst: false, kinah: 12_000);
            var r3 = s3.Instances.Snapshot().LastOrDefault();
            Check(r3 is { Kinah: 12_000, KinahUnknown: false, FromGame: true, ChatKinahSetAside: 0 }, $"...an event's Kinah with its rewards, by the game's count: {r3?.Kinah:N0} the run's, {r3?.ChatKinahSetAside:N0} set aside");
            // a stack the game's data shows for the first time (its count not known): that one item not read, the rest the game's
            var (s4, _) = Run(cardsFirst: false, rs: reward.Take(15));
            var r4a = s4.Instances.Snapshot().LastOrDefault();
            var r5 = new PacketReader(); r5.ReadingForTest();
            var s5 = new TrackerSession(null, settings, items, "Test") { Packets = r5 };
            s5.ApplyZone("Shugo Festival", L("11:03:18"));
            s5.Instances.GameSaid(Area("11:03:21.1", AreaEvent.Kind.Phase, AreaEvent.Stage.UnderWay), "Shugo Festival");
            r5.ItemsForTest(Rows(L("11:04:59.04"), reward.Take(15)).Concat(Rows(L("11:04:59.04"), reward.Skip(15), first: true).Select(x => x with { Batch = batch - 1, BatchRows = 16 })).ToArray());
            s5.DrainPacketsForTest(); Cards(s5, "11:05:12.1"); s5.Instances.Close();
            var r5r = s5.Instances.Snapshot().LastOrDefault();
            Check(r4a is { FromGame: true, SlotsUnread: 0 } && r5r is { FromGame: true, SlotsUnread: 1, PartlyRead: true } && r5r.UnknownSlots.Single().StartsWith("Seed of Detection"),
                $"...a stack first seen: {Say(r5r)} ({r5r?.UnknownSlots.FirstOrDefault()})");
            // without the decoder the cards stand, read in part as before
            var s6 = new TrackerSession(null, settings, items, "Test");
            s6.ApplyZone("Shugo Festival", L("11:03:18"));
            s6.Instances.GameSaid(Area("11:03:21.1", AreaEvent.Kind.Phase, AreaEvent.Stage.UnderWay), "Shugo Festival");
            Cards(s6, "11:05:12.1"); s6.Instances.Close();
            var r6 = s6.Instances.Snapshot().LastOrDefault();
            Check(r6 is { FromGame: false, PartlyRead: true, SlotsUnread: 12, ToLookAt: true }, $"...a build without the decoder: {Say(r6)}");
            // the chat's word on the picture's slots, and a read again of the kept pictures, leave the game's rewards alone
            var amend = s1.Instances.AmendTable(r1!.Start, "event cards", new[] { new TableWitness.Decision(new TableWitness.Slot(0, "Common", "Odyle", 7, items.IdOf("Odyle"), false, false, new List<string> { "Odyle" }, "Common x7"), TableWitness.Unread, "Odyle", items.IdOf("Odyle"), false, "the chat said x8") }, new List<TableWitness.Line>());
            var reread = new InstanceRun { Kind = "Event", FromPicture = true, PictureSlots = 54, SlotsUnread = 0 };
            Check(amend && !TrackerSession.TakeReread(r1, reread) && r1 is { PartlyRead: false, SlotsUnread: 0 } && N(r1, "Odyle") == 7,
                $"...the chat's decisions and a read of the kept pictures change nothing: {Say(r1)}, Odyle x{N(r1, "Odyle")}");
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
        BossTimerChecks(t);
        TimerBoardChecks();
        EventTimerChecks(settings);

        // packets first (1.0.20): the screen is the backup only while the game's data counts items and Kinah, and then
        // reads at a slow pace with a capped share of a core; off, stopped or not counting yet, everything as before
        {
            var items = ItemDictionary.LoadOrEmpty(settings.ItemsFile);
            var none = new TrackerSession(null, settings, items, "Test");
            Check(!none.ScreenBackup(DateTime.UtcNow) && none.FullReadingWhy(DateTime.UtcNow) == "there is no game reader", "packets first: no game reader, the screen reads everything");
            var r = new PacketReader();
            var s = new TrackerSession(null, settings, items, "Test") { Packets = r };
            Check(!s.ScreenBackup(DateTime.UtcNow), $"...a reader with no capture running: everything ({s.FullReadingWhy(DateTime.UtcNow)})");
            r.ReadingForTest();
            Check(!s.ScreenBackup(DateTime.UtcNow), $"...reading, but no Kinah baseline yet: everything ({s.FullReadingWhy(DateTime.UtcNow)})");
            r.AmountForTest(CurrencyKind.Kinah, 1_000_000, DateTime.UtcNow.AddSeconds(-10));
            Check(s.ScreenBackup(DateTime.UtcNow) && s.ScreenIsBackup, "...counting items and Kinah: the screen is the backup");
            r.Enabled = false;
            Check(!s.ScreenBackup(DateTime.UtcNow) && s.FullReadingWhy(DateTime.UtcNow) == "the game reader is off", "...switched off in Options: everything again, at once");
            r.Enabled = true;
            Check(s.ScreenBackup(DateTime.UtcNow), "...and back on: the backup again");
            Check(r.ScreenLagSeconds == TrackerSession.BackupLagSeconds, $"...the agreement check allows the slow reads' later lines: {r.ScreenLagSeconds:0} s");
            s.Target.MarkReward(DateTime.UtcNow, "Daeva Pass Level Reward");
            Check(!s.ScreenBackup(DateTime.UtcNow) && s.FullReadingWhy(DateTime.UtcNow) == "a reward screen is open", "...a reward screen open: everything (its rewards are the screen's reading)");
            Check(TrackerSession.PassWait(600, 300, false) == -300 && TrackerSession.PassWait(200, 750, false) == 550, "...reading everything, the pace is the usual one");
            Check(TrackerSession.PassWait(100, 300, true) == 2400, $"...as the backup, a quick pass waits out {TrackerSession.BackupPassMs} ms start to start: {TrackerSession.PassWait(100, 300, true)}");
            Check(TrackerSession.PassWait(920, 300, true) == 2760, $"...a slow one waits {1 / TrackerSession.BackupCoreShare - 1:0} times its own time (at most {TrackerSession.BackupCoreShare:P0} of a core): {TrackerSession.PassWait(920, 300, true)}");
            Check(TrackerSession.PassWait(4000, 300, true) >= 4000 * 3, "...and never back to back");
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

    /// <summary>This build's decoder over a capture: every gathering attempt of the player's, each connection its own decoder.</summary>
    /// <summary>kinah-bench/packets beside the repo, or null.</summary>
    static string? BenchPackets()
    {
        for (var d = new DirectoryInfo(AppContext.BaseDirectory); d != null; d = d.Parent)
        {
            var p = Path.Combine(d.FullName, "kinah-bench", "packets");
            if (Directory.Exists(p)) return p;
        }
        return null;
    }

    /// <summary>A pickup the game reported in a replay: its item, when, whether a gather gave it, and how long after its message it counted (null: never).</summary>
    internal sealed record Reported(DateTime At, long Id, bool Gathered, double? Delay);
    internal sealed record SessionReplayResult(List<Reported> Reported, List<(ItemAcquired Ev, DateTime CountedAt)> Counted, List<InstanceRun>? Runs = null, string? ItemsBroken = null, List<string>? Verdicts = null);

    /// <summary>
    /// Recordings (folders with capture.pcapng, info.json and the reads.log lines of their minutes as chat.log) through this
    /// build's decoder and a whole session, on the recording's own clock (PacketClock): the game's gathering, kills and
    /// inventory updates go in at their times, the chat's pickup lines at theirs, and the session drains every 50 ms. Each
    /// file is its own reading (as live: the reader starts again with the tracker). With <paramref name="events"/>, the
    /// currencies and the timed events' own parts go in too (an event run opens, gets its rewards and closes as live), and
    /// the session's runs come back once the reader stops.
    /// </summary>
    internal static SessionReplayResult ReplaySession(Settings settings, IList<string> folders, string zone, bool events = false)
    {
        var items = ItemDictionary.LoadOrEmpty(settings.ItemsFile);
        var evs = new List<(DateTime At, int Kind, object What)>();   // 0 items, 1 gather, 2 fight, 3 chat line
        foreach (var dir in folders)
        {
            var segs = new List<(DateTime At, TcpSegment S)>();
            foreach (var (at, frame, link) in Pcapng.Read(Path.Combine(dir, "capture.pcapng")))
                if (NpcapSource.IpOf(frame, link) is { } ip && TcpSegment.Parse(ip) is { } s && s.SrcPort == 13328) segs.Add((at, s));
            foreach (var conn in segs.GroupBy(x => (x.S.Src.ToString(), x.S.DstPort)).OrderBy(g => g.First().At))
            {
                using var dec = NativeDecoder.TryOpen()!;
                var rows = new List<ItemChange>();
                var st = new TcpStream
                {
                    OnData = (d, at) =>
                    {
                        dec.Feed(d, at);
                        while (dec.Next() is { } c) if (events) evs.Add((c.At, 4, c));
                        while (events && dec.NextAreaEvent() is { } a) evs.Add((a.At, 5, a));
                        rows.Clear();
                        while (dec.NextItem() is { } it) rows.Add(it);
                        if (rows.Count > 0) evs.Add((rows[0].At, 0, rows.ToArray()));
                        while (dec.NextFight() is { } f) evs.Add((f.At, 2, f));
                        while (dec.NextGather() is { } g) evs.Add((g.At, 1, g));
                    },
                    OnGap = dec.Gap,
                };
                foreach (var (at, s) in conn) st.Add(s.Seq, s.Payload, at, s.Syn);
            }
            var log = Path.Combine(dir, "chat.log");
            if (dir == folders[^1] && File.Exists(log) && File.Exists(Path.Combine(dir, "info.json")))
            {
                using var j = System.Text.Json.JsonDocument.Parse(File.ReadAllText(Path.Combine(dir, "info.json")));
                var start = j.RootElement.GetProperty("startUtc").GetDateTime().ToUniversalTime();
                int off = j.RootElement.GetProperty("utcOffsetMinutes").GetInt32();
                // a recording copied while its hour was written ends before its chat.log does: a line past the capture's last
                // frame has no update to agree with (rec-20261115-11 ends 11:21:24 local, rec-20261114-12 12:13:52), so it stays out
                var lastFrame = j.RootElement.TryGetProperty("lastFrameUtc", out var lf) ? lf.GetDateTime().ToUniversalTime() : DateTime.MaxValue;
                var day = start.AddMinutes(off).Date;
                var line = new System.Text.RegularExpressions.Regex(@"^﻿?(\d\d):(\d\d):(\d\d\.\d)\s+ok\s+(.*)$");
                var acq = new System.Text.RegularExpressions.Regex(@"^(?:Acquired (?<n1>.+?) x(?<c>\d+)|(?<n2>.+?) acquired)\W*$");
                foreach (var l in File.ReadLines(log))
                {
                    var m = line.Match(l);
                    if (!m.Success) continue;
                    var text = System.Text.RegularExpressions.Regex.Replace(m.Groups[4].Value, @"\s{2,}\(.*$", "").Trim();
                    if (System.Text.RegularExpressions.Regex.IsMatch(text, "Kina|Rank|Abyss|Soul|Point", System.Text.RegularExpressions.RegexOptions.IgnoreCase)) continue;
                    var a = acq.Match(text);
                    if (!a.Success) continue;
                    var name = a.Groups["n1"].Success ? a.Groups["n1"].Value : a.Groups["n2"].Value;
                    if ((items.Find(name) ?? items.Guess(name)) is not long id) continue;
                    var at = DateTime.SpecifyKind(day.AddHours(int.Parse(m.Groups[1].Value)).AddMinutes(int.Parse(m.Groups[2].Value))
                        .AddSeconds(double.Parse(m.Groups[3].Value, System.Globalization.CultureInfo.InvariantCulture)).AddMinutes(-off), DateTimeKind.Utc);
                    int n = a.Groups["c"].Success ? int.Parse(a.Groups["c"].Value) : 1;
                    if (at > lastFrame) continue;
                    evs.Add((at, 3, new ItemAcquired(at, id, n, items.NameOf(id).EndsWith("(Bound)", StringComparison.OrdinalIgnoreCase)) { Via = "chat" }));
                }
            }
        }
        evs = evs.OrderBy(e => e.At).ThenBy(e => e.Kind).ToList();
        var counted = new List<(ItemAcquired Ev, DateTime CountedAt)>();
        if (evs.Count == 0) return new SessionReplayResult(new(), counted);
        var clock = evs[0].At;
        PacketClock.Fixed = () => clock;
        try
        {
            var reader = new PacketReader(); reader.ReadingForTest(); reader.GathersForTest();
            var s = new TrackerSession(null, settings, items, "Test") { Packets = reader };
            s.Engine.SetZone(zone);
            s.Parsed += ev => { if (ev is ItemAcquired ia) counted.Add((ia, clock)); };
            var batches = new List<ItemBatch>();
            int i = 0;
            var end = evs[^1].At.AddSeconds(20);
            DateTime agreedAt = clock;
            for (; clock <= end; clock = clock.AddMilliseconds(50))
            {
                reader.ReadingForTest();
                for (; i < evs.Count && evs[i].At <= clock; i++)
                {
                    switch (evs[i].What)
                    {
                        case ItemChange[] rs: reader.ItemsForTest(rs); batches.AddRange(ItemBatch.Cut(rs)); break;
                        case GatherResult g: reader.GathersForTest(g); break;
                        case FightEvent f: reader.FightsForTest(f); break;
                        case ItemAcquired line: s.CommitForTest(line); break;
                        case CurrencyChange c: reader.AmountForTest(c.What, c.Amount, c.At); break;
                        case AreaEvent a: reader.AreaForTest(a); break;
                    }
                }
                s.DrainPacketsForTest();
                if ((clock - agreedAt).TotalSeconds >= 1) { agreedAt = clock; reader.AgreeItemsForTest(clock); reader.AgreeGathersForTest(clock); s.DrainPacketsForTest(); }
            }
            // what the game reported: each item that went up in an update with nothing going down (a first sight too, when a
            // gather gave it), and whether a count of it landed: from its message, or from its chat line within 8 s
            var gathers = evs.Where(e => e.What is GatherResult { Outcome: not GatherEnd.Failed }).Select(e => e.At).ToList();
            var reported = new List<Reported>();
            var taken = new HashSet<int>();
            foreach (var b in batches.Where(b => !b.Fell))
            {
                bool gathered = gathers.Any(g => Math.Abs((g - b.At).TotalSeconds) <= 1.5);
                foreach (var (id, n, unknown) in b.Net())
                {
                    if (items.Get(id) == null || (!unknown && n <= 0)) continue;
                    int k = counted.FindIndex(c => !taken.Contains(counted.IndexOf(c)) && c.Ev.ItemId == id && (c.Ev.Time - b.At).TotalSeconds is >= -0.01 and <= 8);
                    if (k >= 0) taken.Add(k);
                    reported.Add(new Reported(b.At, id, gathered, k >= 0 ? Math.Max(0, (counted[k].CountedAt - b.At).TotalSeconds) : null));
                }
            }
            s.Instances.Close();
            return new SessionReplayResult(reported, counted, s.Instances.Snapshot(), reader.ItemsBroken, reader.Verdicts.ToList());
        }
        finally { PacketClock.Fixed = null; }
    }

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
        var server = new DateTime(2026, 11, 14, 1, 29, 0, DateTimeKind.Utc);   // Fri 20:29 US Eastern (Sat 10:29 at UTC+9)
        string Say(EventTimers.Row r) => r.OnNow ? $"{r.Event.Name} on, {r.When}" : $"{r.Event.Name} in {r.When} at {r.StartPc:HH:mm:ss.f}Z";
        string All(DateTime sv, string region) => string.Join(" | ", EventTimers.Schedule.Select(r => EventTimers.RowOf(r, sv, lag, EventTimers.ClockOf(region))).Select(Say));
        DateTime U(int mo, int d, int h, int mi = 0) => new(2026, mo, d, h, mi, 0, DateTimeKind.Utc);
        var naE = EventTimers.ClockOf("na_e"); var eu = EventTimers.ClockOf("eu");
        var rift = EventTimers.Schedule.First(r => r.Name == "Spacetime Rift"); var siege = EventTimers.Schedule.First(r => r.Name == "Artifact Siege");
        // NA East on US Eastern time (checked 2026-11-14); the regions not checked yet on their own local time (Europe: Paris)
        string na = All(server, "na_e"), other = All(server, "eu");
        Check(naE.Checked && !eu.Checked && na == "Shugo Festival in 31m 00s at 02:00:14.6Z | Dimensional Invasion in 1m 00s at 01:30:14.6Z | Spacetime Rift in 2h 31m 00s at 04:00:14.6Z | Artifact Siege in 1d 0h 31m at 02:00:14.6Z",
            $"event timers, NA East (US Eastern): {na} (the starts on this PC's clock, 14.6 s after the game's)");
        Check(other == "Shugo Festival in 31m 00s at 02:00:14.6Z | Dimensional Invasion in 1m 00s at 01:30:14.6Z | Spacetime Rift in 2h 31m 00s at 04:00:14.6Z | Artifact Siege in 18h 31m 00s at 20:00:14.6Z"
            && eu.Note.Contains("not checked") && EventTimers.ClockOf("asia").Zone.BaseUtcOffset == TimeSpan.FromHours(9) && EventTimers.ClockOf("na_w").Zone.BaseUtcOffset == TimeSpan.FromHours(-8),
            $"...a region not checked yet (Europe) on Central European time, marked so: {other}; NA West Pacific, Asia Korea");
        // the ground truth in NA East: the Abyss server's own next siege (sent Fri 17:15 ET: Sat 21:00 ET), the chat's Rift lines
        var (lastSiege, nextSiege) = EventTimers.Around(siege, U(11, 13, 22, 15), naE);
        Check(nextSiege == U(11, 15, 2) && lastSiege == U(11, 13, 2), $"...the Artifact Siege: next {nextSiege:ddd HH:mm}Z (the game's own message: Sat 21:00 ET = Sun 02:00Z), last {lastSiege:ddd HH:mm}Z (Thu 21:00 ET; it ended 21:16:51 ET)");
        var rifts = new[] { U(11, 13, 15, 59), U(11, 13, 18, 59), U(11, 13, 21, 59) }.Select(t0 => EventTimers.Around(rift, t0, naE).Next).ToList();
        Check(rifts.SequenceEqual(new[] { U(11, 13, 16), U(11, 13, 19), U(11, 13, 22) }), $"...the Spacetime Rift at {string.Join(", ", rifts.Select(x => x.AddHours(-5).ToString("HH:mm")))} ET (the chat: \"A Spacetime Rift has appeared\" 11:00:17, 14:00:26, 17:00:18 ET)");
        // daylight saving: the same Eastern wall-clock grid in October (aion2-data's 00:00 UTC Rift anchor of 2026-10-07), across
        // the autumn change, and over the hour skipped in spring
        var summer = EventTimers.Around(rift, U(10, 7, 22, 30), naE).Next;
        var thu = EventTimers.Around(siege, U(10, 15, 12), naE).Next;
        var (fallLast, fallNext) = EventTimers.Around(rift, U(11, 1, 4), naE);
        var spring = EventTimers.Around(rift, new DateTime(2027, 3, 14, 5, 0, 0, DateTimeKind.Utc), naE).Next;
        Check(summer == U(10, 8, 0) && thu == U(10, 16, 1) && fallLast == U(11, 1, 3) && fallNext == U(11, 1, 7) && spring == new DateTime(2027, 3, 14, 7, 0, 0, DateTimeKind.Utc),
            $"...daylight saving: the Rift at {summer:HH:mm}Z on 2026-10-07 (20:00 EDT), the siege Thu 21:00 EDT = {thu:ddd HH:mm}Z; on 2026-11-01 {fallLast:HH:mm}Z then {fallNext:HH:mm}Z (23:00 EDT, 02:00 EST); on 2027-03-14 02:00 doesn't exist: {spring:HH:mm}Z (03:00 EDT)");
        // the Shugo Festival's 3 minutes to enter (Steven, 2026-11-16): a minute past, 2 left; at 3 past, the next one
        var at1 = EventTimers.RowOf(EventTimers.Schedule[0], U(11, 14, 2, 1), lag, naE);
        var at3 = EventTimers.RowOf(EventTimers.Schedule[0], U(11, 14, 2, 3), lag, naE);
        var dim = EventTimers.RowOf(EventTimers.Schedule[1], U(11, 14, 2, 32), lag, naE);
        Check(at1 is { OnNow: true, When: "2m 00s" } && at1.Left == TimeSpan.FromMinutes(2) && at3 is { OnNow: false, When: "57m 00s" }
            && dim is { OnNow: true, When: "1m 00s" },
            $"...the Shugo Festival and the Dimensional Invasion count down their 3 minutes to enter, then the next start: {Say(at1)}; at 3 past: {Say(at3)}; Dimensional 2 past: {Say(dim)}");
        var siegeOn = EventTimers.RowOf(siege, U(11, 15, 2, 5), lag, naE);
        var siegeNext = EventTimers.RowOf(siege, U(11, 15, 2, 25), lag, naE);
        Check(siegeOn.OnNow && !siegeNext.OnNow && siegeNext.StartPc == U(11, 20, 2) + lag && siegeNext.When == "4d 23h 35m",
            $"...the Artifact Siege on Saturday 21:05 ET, then next on Thursday 21:00 ET (twice a week since the Nov 4 patch): {Say(siegeNext)}");

        var et = new EventTimers();
        var pc = server + lag;
        Check(!et.ClockKnown && !et.ClockLive(pc) && et.ClockLine(pc) == "", "...before the clock is known, this PC's clock, and the card says nothing of it");
        et.Take(new AreaEvent(pc, server, AreaEvent.Kind.Clock, AreaEvent.Stage.Unknown, null, "", "", "", 0));
        var sf = et.Rows(pc, "na_e").First(r => r.Event.Name == "Shugo Festival");
        Check(et.ClockKnown && et.ClockLive(pc) && !et.ClockLive(pc.AddMinutes(5)) && Math.Abs(et.Offset.TotalSeconds - 14.6) < 0.01 && sf.Left == TimeSpan.FromMinutes(31)
            && et.ClockLine(pc) == "" && et.ClockLine(pc.AddSeconds(3600)).StartsWith("Last read"),
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
        // Hidden Lugi (2026-11-14, 11:03 ET): its game's part and its lobby come in one bundle on arrival, the game's first;
        // the stage stays "Under way", with the game's start
        var lugi = new EventTimers();
        lugi.Take(A(0, AreaEvent.Kind.Phase, AreaEvent.Stage.UnderWay, -0.1, name: "Hidden Lugi"));
        lugi.Take(A(0, AreaEvent.Kind.Phase, AreaEvent.Stage.Preparing, -12.2, name: "Hidden Lugi"));
        var lg = lugi.Current(e0.AddSeconds(5));
        Check(lg is { Stage: AreaEvent.Stage.UnderWay, Label: "Hidden Lugi" } && lg.StartedPc == e0.AddSeconds(-0.1) && EventTimers.StageText(lg) == "Under way",
            $"...a lobby that comes after the game's own part never steps the stage back: {(lg == null ? "-" : EventTimers.StageText(lg))} (Hidden Lugi, 2026-11-14)");
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
        var afterSat = AbyssBosses.Around(AbyssBosses.Schedule.First(b => b.Name == "Executor Tamasa"), new DateTime(2026, 11, 15, 2, 45, 0, DateTimeKind.Utc), zone).Next;
        Check(afterSat == new DateTime(2026, 11, 20, 2, 30, 0, DateTimeKind.Utc), $"...the Artifact Bosses twice a week since the Nov 4 patch: after Saturday's, Thursday 21:30 ET ({afterSat:ddd dd HH:mm}Z), not Monday");
        var dramos = AbyssBosses.Around(AbyssBosses.Schedule.First(b => b.Name == "Executioner Dramos"), new DateTime(2026, 11, 15, 2, 45, 0, DateTimeKind.Utc), zone).Next;
        Check(dramos == new DateTime(2026, 11, 17, 2, 30, 0, DateTimeKind.Utc), $"...Middle Reshanta's, which the patch didn't name, still Monday too ({dramos:ddd dd HH:mm}Z)");
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
            && !prefs.Watched(TimerBoard.BossKey("Heretic Layla")) && !prefs.Watched(TimerBoard.BossKey("Kusan the Mad Gladiator")) && prefs.OverlayOn && !prefs.ClickThrough && prefs.Count == 4,
            "watched by default: the events, Kashapa, Gartua, Lagta and every Abyss boss; Layla and Kusan off; the overlay on, not click-through (it drags), 4 at most");

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

        // where the line goes (Steven, 2026-11-14): on top of the Kinah overlay by default; its own strip when separated or
        // with the Kinah overlay off; nowhere with the timers off
        Check(!TimerPrefs.Defaults().Separate && TimerPrefs.Host(true, false, true) == "kinah" && TimerPrefs.Host(true, true, true) == "strip"
            && TimerPrefs.Host(true, false, false) == "strip" && TimerPrefs.Host(false, false, true) == "" && TimerPrefs.Host(false, true, false) == "",
            "the timer line's home: on top of the Kinah overlay by default, its own strip when separated or with the Kinah overlay off, nowhere when off");

        // the settings file
        var dir = TestTemp.Path("KinahLedger-packet-test"); Directory.CreateDirectory(dir);
        var file = Path.Combine(dir, "timers.json"); File.Delete(file);
        var saved = TimerPrefs.Load(file);
        saved.OverlayOn = false; saved.ClickThrough = true; saved.X = 1500; saved.Y = 200; saved.Count = 6; saved.WindowMinutes = 10; saved.ChimeMinutes = 2; saved.Volume = 0.3; saved.Details = true;
        saved.WinX = 2000; saved.WinY = 100; saved.WinW = 500; saved.WinH = 900;
        saved.SetWatched(TimerBoard.BossKey("Heretic Layla"), true); saved.SetWatched(TimerBoard.EventKey("Spacetime Rift"), false); saved.SetChimes(TimerBoard.EventKey("Shugo Festival"), false);
        saved.Save();
        var back = TimerPrefs.Load(file);
        Check(back is { OverlayOn: false, ClickThrough: true, X: 1500, Y: 200, Count: 6, WindowMinutes: 10, ChimeMinutes: 2, Volume: 0.3, Details: true, WinX: 2000, WinW: 500, WinH: 900 }
            && back.Watched(TimerBoard.BossKey("Heretic Layla")) && !back.Watched(TimerBoard.EventKey("Spacetime Rift")) && back.Watched(TimerBoard.EventKey("Shugo Festival")) && !back.Chimes(TimerBoard.EventKey("Shugo Festival"))
            && TimerPrefs.Load(Path.Combine(dir, "none.json")) is { OverlayOn: true, ClickThrough: false },
            "timers.json: the overlay, its place, click-through, minutes, how many, the chime, the Timers window's place, size and details, the watched and muted timers, read back; no file is the defaults (overlay on, not click-through)");
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
    /// <summary>
    /// packet-replay: what the decoder reads from a recording. More files (the tracker cuts its recording into hours) are
    /// read on as one, so a connection that runs on into the next file keeps what it knew (the player's faction, who is who).
    /// </summary>
    public static int Replay(string pcapng, int serverPort, Settings? settings = null, IReadOnlyList<string>? more = null)
    {
        var bossEvents = new List<BossEvent>();
        if (!NativeDecoder.Available) { Console.WriteLine("this build has no decoder (klnet.dll)"); return 1; }
        var segs = new List<(DateTime At, TcpSegment S)>();
        foreach (var file in new[] { pcapng }.Concat(more ?? Array.Empty<string>()))
            foreach (var (at, frame, link) in Pcapng.Read(file))
                if (NpcapSource.IpOf(frame, link) is { } ip && TcpSegment.Parse(ip) is { } s && s.SrcPort == serverPort) segs.Add((at, s));
        var seen = new List<CurrencyChange>();
        var batches = new List<ItemBatch>();
        var fights = new FightTally(); var fought = new List<FightTally.Outcome>(); bool readsFights = false;
        var gaps = new List<DateTime>();
        var bosses = new BossTimers();
        var bossLines = new List<string>();
        var eventLines = new List<string>(); var timers = new EventTimers();
        var applicants = new List<PartyApplication>();
        var energy = new List<OdyleReading>();
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
                    while (dec.NextApplicant() is { } ap) applicants.Add(ap);
                    while (dec.NextEnergy() is { } en) energy.Add(en);
                    while (dec.NextBossEvent() is { } b) bossEvents.Add(b);
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
        ReplayBosses(bossEvents);
        if (NativeDecoder.Version >= 11) ReplayApplicants(applicants);
        // Odyle Energy (version 15): each reading, as the tracker's log says it
        foreach (var en in energy.OrderBy(x => x.At))
            Console.WriteLine($"{Local(en.At)}  Odyle Energy {en.Total:N0} ({en.Natural:N0} + {en.Extra:N0} charged; {en.Reason}{(en.ChargedBy > 0 ? $" by {en.ChargedBy:N0}" : "")})");
        if (NativeDecoder.Version >= 15) Console.WriteLine($"Odyle Energy: {energy.Count} readings");
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
    /// What a recording's decoder gives, in order, for replay --pcap (Program.Replay): each currency change, each data
    /// chunk's item changes, fight, gather and event part, with its time, from every connection to the game's server port.
    /// The tracker cuts its recording into hours: the files are read as one, so a connection that runs on into the next
    /// file goes on with what its stacks held. Empty without the decoder.
    /// </summary>
    public static List<(DateTime At, object Ev)> Decode(IEnumerable<string> pcapngs, int serverPort = 13328)
    {
        var o = new List<(DateTime At, object Ev)>();
        if (!NativeDecoder.Available) return o;
        var segs = new List<(DateTime At, TcpSegment S)>();
        foreach (var pcapng in pcapngs)
            foreach (var (at, frame, link) in Pcapng.Read(pcapng))
                if (NpcapSource.IpOf(frame, link) is { } ip && TcpSegment.Parse(ip) is { } s && s.SrcPort == serverPort) segs.Add((at, s));
        foreach (var conn in segs.GroupBy(x => (x.S.Src.ToString(), x.S.DstPort)).OrderBy(g => g.First().At))
        {
            using var dec = NativeDecoder.TryOpen()!;
            var st = new TcpStream
            {
                OnData = (d, at) =>
                {
                    dec.Feed(d, at);
                    while (dec.NextBag() is not null) { }
                    while (dec.Next() is { } c) o.Add((c.At, c));
                    var rows = new List<ItemChange>();
                    while (dec.NextItem() is { } it) rows.Add(it);
                    if (rows.Count > 0) o.Add((rows[0].At, rows));
                    while (dec.NextBoss() is not null) { }
                    while (dec.NextAreaEvent() is { } ae) o.Add((ae.At, ae));
                    while (dec.NextGather() is { } g) o.Add((g.At, g));
                    while (dec.NextFight() is { } f) o.Add((f.At, f));
                    while (dec.NextBossEvent() is not null) { }
                    while (dec.NextHit() is not null) { }
                },
                OnGap = () => dec.Gap(),
            };
            foreach (var (at, s) in conn) st.Add(s.Seq, s.Payload, at, s.Syn);
        }
        return o;
    }

    /// <summary>
    /// packet-replay's party finder (the decoder's version 11): each request to join the group the player leads, in order, through
    /// the Applicants window's own list (Session/PartyApplicants.cs), with who is waiting after each, as the window would show them.
    /// </summary>
    static void ReplayApplicants(List<PartyApplication> ev)
    {
        if (ev.Count == 0) { Console.WriteLine("applicants: none (nobody asked to join a group the player led)"); return; }
        var board = new PartyApplicants();
        foreach (var a in ev.OrderBy(a => a.At))
        {
            board.Take(a);
            var waiting = board.Pending(a.At);
            string what = a.What switch
            {
                PartyApplication.Kind.Applied => $"asked to join: {a.Name} ({(a.Class.Length > 0 ? a.Class : "class not known")}, level {a.Level}, item level {a.ItemLevel:N0}, Combat Power {a.CombatPower:N0}{(a.Server.Length > 0 ? ", " + a.Server : "")}{(a.AppliedAt is { } ap ? $", applied {Local(ap)} on the game's clock" : "")})",
                PartyApplication.Kind.Accepted => $"joined the group: {a.Name}",
                PartyApplication.Kind.Removed => $"request gone: {a.Name}",
                _ => "every request gone (the recruit closed, or the player went into another group)",
            };
            Console.WriteLine($"{Local(a.At)}  applicants: {what} · waiting {waiting.Count}{(waiting.Count > 0 ? ": " + string.Join(", ", waiting.Select(w => w.Name)) : "")}");
        }
        var last = ev.Max(a => a.At);
        var left = board.Pending(last);
        if (left.Count > 0)
        {
            var gone = left.Min(a => a.At) + PartyApplicants.StaleAfter;
            Console.WriteLine($"{Local(gone)}  applicants: the window would drop {string.Join(", ", left.Where(a => a.At + PartyApplicants.StaleAfter <= gone).Select(a => a.Name))} by its own clock ({PartyApplicants.StaleAfter.TotalMinutes:0} minutes with no word)");
        }
        Console.WriteLine($"applicants: {ev.Count(a => a.What == PartyApplication.Kind.Applied)} requests, {ev.Count(a => a.What == PartyApplication.Kind.Accepted)} joined, {ev.Count(a => a.What == PartyApplication.Kind.Removed)} gone one by one, {ev.Count(a => a.What == PartyApplication.Kind.Cleared)} times every request went at once");
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
                    Console.WriteLine($"{Local(g.At)}  group: {string.Join(", ", g.Members.Select(m => $"{m.Name}{(m.You ? " (you)" : "")} {(m.Server.Length > 0 ? $"[{m.Server}] " : "")}{(m.Class.Length > 0 ? m.Class + " " : "")}item level {(m.ItemLevel > 0 ? m.ItemLevel.ToString("N0") : "unknown")}, Combat Power {(m.CombatPower > 0 ? m.CombatPower.ToString("N0") : "unknown")}"))}");
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
            Console.WriteLine($"  {o.At.ToLocalTime():HH:mm:ss.ff}  {(o.Death ? $"died{(o.Name.Length > 0 ? " to " + o.Name : "")}" : o.Assist ? $"an assist on the player {o.Name}: killed by {o.Killer}, your last hit {o.HitBefore:0.0} s before" : $"killed {(o.Player ? "the player " + o.Name : $"a monster{(o.MonsterId != 0 ? $" ({o.MonsterId})" : "")}")}{(o.Credited ? ", no killer named, Rank Points rose with it" : "")}")}");
        var kills = fought.Where(o => !o.Death).ToList();
        int players = kills.Count(o => o.Player), deaths = fought.Count(o => o.Death), assists = kills.Count(o => o.Assist);
        Console.WriteLine($"kills: {kills.Count} ({players} players, {assists} of them assists, {kills.Count - players} monsters; {kills.Count(o => o.Credited)} with no killer named), {deaths} deaths ({fought.Count(o => o.Death && o.Player)} to players), Rank Points +{rank:N0}");
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
