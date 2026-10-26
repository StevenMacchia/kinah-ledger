using KinahLedger.Data;
using KinahLedger.Session;

namespace KinahLedger.Ocr;

/// <summary>
/// parse-test's checks for the chat's counts and repeats (2026-10-24, branch tracker/chat-qty): quantities read through
/// the recognizer's digit slips, from the loot list's row, from a later read of the same line, or "1 assumed" and logged;
/// Kina lines glued to their amount; two identical lines as two pickups and a re-read as one; a line a read lost at its
/// bottom coming back; a fragment's whole line; and the Kinah counter as the deciding witness for Kina (KinaWitness).
/// </summary>
static class ChatCountTests
{
    public static bool Run(ItemDictionary items, Settings settings)
    {
        bool ok = true;
        void Check(string what, string got, string expect)
        {
            bool pass = got == expect; ok &= pass;
            Console.WriteLine($"  {what,-62} -> {got,-40} {(pass ? "ok" : "MISMATCH, expected " + expect)}");
        }
        string D(GameEvent e) => e switch
        {
            ItemAcquired a => $"{items.NameOf(a.ItemId)} x{a.Count}",
            RewardReceived r => $"reward {r.Name} x{r.Count}",
            KinahChanged k => $"Kina {k.Delta}",
            _ => e.GetType().Name,
        };
        string P(ChatParser p, string line) { var evs = p.Parse(line, DateTime.UtcNow).ToList(); return evs.Count == 0 ? "(nothing)" : string.Join("; ", evs.Select(D)); }

        Console.WriteLine("\nChat counts and repeats (2026-10-24: Odyle x7 filed as x1, the second Riverside Pauldrons lost, seven Kina lines):");
        string T(string s) => ChatParser.QuantityTail(s) is { } q ? $"{q.Head} x{q.Count}" : "none";
        // the digit slips, one per case
        Check("count tail: T for 7 (\"Refining StonevxT\")", T("Refining StonevxT"), "Refining Stonev x7");
        Check("count tail: b for 6 (\"Odyle xb\")", T("Odyle xb"), "Odyle x6");
        Check("count tail: S for 5 (\"Fine Odyle xS.\")", T("Fine Odyle xS."), "Fine Odyle x5");
        Check("count tail: O for 0 (\"Odyle x1O\")", T("Odyle x1O"), "Odyle x10");
        Check("count tail: l and I for 1 (\"Odyle xl2\", \"Odyle×I\")", T("Odyle xl2") + " · " + T("Odyle×I"), "Odyle x12 · Odyle x1");
        Check("count tail: a lone I after a space is a numeral (\"Field Boss I\")", T("Field Boss I") + " · " + T("Odyle ×I"), "none · none");
        Check("count tail: glued to Kina (\"Kinæx382\")", T("Kinæx382"), "Kinæ x382");
        Check("count tail: a word after a space is no count (\"Treasure Box II\")", T("Treasure Box II"), "none");
        Check("count tail: an x with nothing after it (\"Odyle x\")", T("Odyle x"), "none");

        var p = new ChatParser(items);
        Check("parsed: \"Acquired Refining StonevxT.\"", P(p, "Acquired Refining StonevxT."), "Refining Stone x7");
        Check("parsed: \"Acquired Odyle xb.\"", P(p, "Acquired Odyle xb."), "Odyle x6");
        Check("parsed: \"Acquired Fine Odyle xS.\"", P(p, "Acquired Fine Odyle xS."), "Fine Odyle x5");
        Check("parsed: \"Acquired Odyle x1O.\"", P(p, "Acquired Odyle x1O."), "Odyle x10");
        Check("parsed: \"Acquired Refining Stonevx6.\" (as before)", P(p, "Acquired Refining Stonevx6."), "Refining Stone x6");
        Check("parsed: the glued verb and a t for an r (13:07 Pauldrons)", P(p, "Riverside Pauldröns:acquired.") + " · " + P(p, "Riverside Pauldronsacquited,"), "Riverside Pauldrons x1 · Riverside Pauldrons x1");
        // a verb-first line with no count left is unsure; a name-first line is one
        P(p, "Acquired Odyle"); P(p, "Odyle acquired."); P(p, "Acquired Odyle x7.");
        Check("count unsure: \"Acquired Odyle\" yes, name-first and x7 no", $"{p.CountUnsure("Acquired Odyle")} {p.CountUnsure("Odyle acquired.")} {p.CountUnsure("Acquired Odyle x7.")}", "True False False");
        // the loot list's row for the same item gives the count
        var pl = new ChatParser(items) { ListCount = (id, _) => items.NameOf(id) == "Odyle" ? 7 : null };
        Check("loot list fallback: \"Acquired Odyle\" with an Odyle x7 row", P(pl, "Acquired Odyle") + " unsure " + pl.CountUnsure("Acquired Odyle"), "Odyle x7 unsure False");
        // Kina lines glued to their amount, or with a stray comma or accent (13:11 to 13:15)
        Check("Kina: \"Acquired 2367Kina.\" (no space) is Kina, not an item", P(p, "Acquired 2367Kina."), "Kina 2367");
        Check("Kina: \"Acquired 189, Kina.\", \"Acquired 54,Kina.\", \"Acquired 293 Kinå.\"", P(p, "Acquired 189, Kina.") + " · " + P(p, "Acquired 54,Kina.") + " · " + P(p, "Acquired 293 Kinå."), "Kina 189 · Kina 54 · Kina 293");

        var t0 = new DateTime(2026, 10, 24, 17, 0, 0, DateTimeKind.Utc);
        string Log() { try { return File.ReadAllText(Path.Combine(CrashLog.Dir, "reads.log")); } catch (Exception) { return ""; } }
        int N(TrackerSession ss, string name) => ss.Engine.Items.Where(kv => items.NameOf(kv.Key) == name).Sum(kv => kv.Value);

        // the session: the loot list's row with its icon, then the chat line that lost its count
        {
            var s = new TrackerSession(null, settings, items, "Test") { Vote = false };
            s.ProcessChat(new List<string> { "[P1] hello", "[P2] hi there" }, t0);
            s.ProcessList(new List<LootLine> { new(1197, "Odyle x7", 0.62, 0xF0F0F0F0F0F0F0F0UL, 0.14, 0.12, 1920) }, t0.AddSeconds(0.5));
            s.ProcessChat(new List<string> { "[P1] hello", "[P2] hi there", "Acquired Odyle" }, t0.AddSeconds(1));
            s.FlushVotes(t0.AddSeconds(2));
            Check("session: \"Acquired Odyle\" with an Odyle x7 row up: seven", N(s, "Odyle").ToString(), "7");
        }
        // ...and with nothing to say how many: one, logged and tallied
        {
            var before = Accuracy.Get("qty_assumed");
            var s = new TrackerSession(null, settings, items, "Test") { Vote = false };
            var t1 = t0.AddMinutes(5);
            s.ProcessChat(new List<string> { "[P1] hello", "[P2] hi there" }, t1);
            var r = new List<string> { "[P1] hello", "[P2] hi there", "Acquired Odyle" };
            for (int i = 1; i <= 5; i++) s.ProcessChat(r, t1.AddSeconds(i));
            s.FlushVotes(t1.AddSeconds(7));
            Check("session: no count anywhere: one, \"quantity unsure, 1 assumed\" logged", $"{N(s, "Odyle")} {Log().Contains("\"Acquired Odyle\": quantity unsure, 1 assumed")} {s.CountsAssumed}", "1 True 1");
            Check("...and tallied in Accuracy (qty_assumed, on this PC only)", (Accuracy.Get("qty_assumed") - before).ToString(), "1");
            Check("...qty_assumed is not in the payload's keys", Accuracy.Keys.Contains("qty_assumed").ToString(), "False");
        }
        // the reads of 13:07:01 to :07 (trace.log, the Monster Cube's rewards fading in): Odyle x7 from the next read, two Pauldrons
        {
            var s = new TrackerSession(null, settings, items, "Test");
            var tb = new DateTime(2026, 10, 24, 17, 5, 0, DateTimeKind.Utc);
            s.Engine.SetZone("Hidden Lugi"); s.Instances.Zone("Hidden Lugi", tb);
            // the event's chat for a few minutes (its lines unlike the rewards'), then the Monster Cube over the chat: empty reads
            var lobby = new List<string>();
            for (int i = 0; i < 30; i++)
            {
                lobby.Add($"[P{i % 5}] event chat number {i}"); if (lobby.Count > 10) lobby.RemoveAt(0);
                s.ProcessChat(new List<string>(lobby), tb.AddSeconds(i * 3));
            }
            for (int i = 0; i < 5; i++) s.ProcessChat(new List<string>(), tb.AddSeconds(100 + i * 4));
            var reads = new (double At, string[] L)[]
            {
                (121.4, new[] { "Elder Greaves acquired.", "Forgotteh GCoves acquired.", "Riverside Pauldröns:acquired.", "Riverside Pauldronsacquited,", "Acquired Fine Odyle x2.", "Acquired Odyle", "Acquired Refining Stonevx6.", "Resurrection Spiritstone (Bound) acquired.", "Acquired Soul Codex (Bound) x3.", "[P2] Get PvE Damage Boost" }),
                (122.6, new[] { "Forgotten Gloves acquired.", "RiveTSi4e Pauldrons acquired.", "\"RiverSide Pauldtonsacquired.", "Acquired Fine Odyle x2.", "Acquired Odyle x7.", "Acquired Refining Stone x6.", "Resurrection SpiritstoneÅBound) acquired.", "Acquired Soul Codex (Bound) x3.", "[P2] Get PvE Damage Boost", "[P3] 0k" }),
                (123.9, new[] { "Forgotten Gloves acquired.", "RiveiSi4e Pauldrons acquired.", "Riverside* Pauldtonsacquired.", "Acquired Fine Odyle x2.", "Acquired Odyle x 7.", "Acquired Refining Stone x6.", "Resurrection SpiritstoneÅBound) acquired.", "Acquired Soul Codex (Bound) x3.", "[P2] Get PvE Damage Boost", "[P3] 0k" }),
                (125.7, new[] { "Forgotten Gloves acquired.", "RiveiSi4e Pauldrons acquired.", "Riversid&pautdfonsacquired.v", "Acquired Fine Odyle x2.", "Acquired Odyle x 7.", "Acquired Refining Stone x6.", "Resurrection SpiritstoneÅBound) acquired.", "Acquired Soul Codex (Bound) x3.", "[P2] Get PvE Damage Boost", "[P3] 0k" }),
                (127.5, new[] { "FörOoften Glove' acquired.", "Riverside*auldronS acquired.", ".R'iVersidee.Pauldronsuacquired.", "Acquired Fine Odyle x2.", "Acquired", "Odyle", "Acquired Refining Stonex6.", "Resurrection Spiritstone (Bound) acquired.", "Acquired Soul Codex (Bound) x3.", "[P2] Get PvE Damage Boost", "[P3] 0k" }),
            };
            foreach (var (at, l) in reads) s.ProcessChat(l.ToList(), tb.AddSeconds(at));
            s.FlushVotes(tb.AddSeconds(130));
            var run = s.Instances.CurrentRun ?? s.Instances.Snapshot().LastOrDefault(x => x.Zone == "Shugo Festival" && x.Start == tb);
            int R(string name) => (run?.Items ?? new()).Concat(run?.ChatItems ?? new()).Concat(run?.BoundItems ?? new()).Where(kv => items.NameOf(kv.Key) == name).Sum(kv => kv.Value);
            Check("13:07 rewards: Odyle (x7 read a read later), Pauldrons (two lines)", $"Odyle {R("Odyle")} Pauldrons {R("Riverside Pauldrons")}", "Odyle 7 Pauldrons 2");
            Check("13:07 rewards: the rest as before", $"{R("Elder Greaves")} {R("Forgotten Gloves")} {R("Fine Odyle")} {R("Refining Stone")}", "1 1 2 6");
        }

        // repeats by place: two identical lines are two pickups, a re-read of the same screen is one
        {
            var lt = new LineTracker();
            var a = new List<string> { "[Pa] hello there all", "[Pb] who wants a group", "[Pc] selling stuff cheap", "Riverside Pauldrons acquired." };
            lt.NewLines(a);
            var two = lt.NewLines(new List<string> { "[Pb] wh0 wamts a gr0up", "[Pc] se1ling stuf cheep", "Riverside Pauldrons acquired.", "Riverside Pauldrons acquired." });
            Check("a second identical line below the first, neighbours garbled: new", string.Join(" | ", two), "Riverside Pauldrons acquired.");
            var lt2 = new LineTracker();
            lt2.NewLines(a);
            var same = lt2.NewLines(new List<string> { "[Pa] he1lo thera all", "[Pb] wh0 wamts a gr0up", "[Pc] se1ling stuf cheep", "Riverside Pauldrons acquired." });
            Check("the same screen read again, garbled: nothing new", same.Count.ToString(), "0");
            var lt3 = new LineTracker();
            lt3.NewLines(new List<string> { "[Pa] hello there all", "Kukuru acquired.", "Kukuru acquired." });
            lt3.NewLines(new List<string> { "[Pa] hello there all", "Kukuru acquired.", "[Pz] world text", "Kukuru acquired." });
            Check("a stray line between two copies, then gone: no new copy", lt3.NewLines(new List<string> { "[Pa] hello there all", "Kukuru acquired.", "Kukuru acquired." }).Count.ToString(), "0");
            // a read that lost its last line, then the line back where it was, and a new line under it (13:15:01 to :03)
            var lt4 = new LineTracker();
            lt4.NewLines(new List<string> { "Acquired 492 Kina.", "came out", "Acquired 140 Kina.", "Acquired 513 Kina.", "Acquired 214 Kina.", "Acquired 318 Kina.", "Acquired 344 Kina." });
            lt4.NewLines(new List<string> { "came out", "Acquired 140 Kina.", "Acquired 513 Kina.", "Acquired 214 Kina.", "Acquired 318 Kina." });
            Check("a line a read lost at its bottom comes back: the same line", string.Join(" | ", lt4.NewLines(new List<string> { "came out", "Acquired 140 Kina.", "Acquired 513 Kina.", "Acquired 214 Kina.", "Acquired 318 Kina.", "Acquired 344 Kina.", "Acquired 244 Kina." })), "Acquired 244 Kina.");
            // the read again below the overlap: the last read's own line, garbled differently (13:12:08, 408)
            Check("below the overlap: \"cquihed 408 Kina\" is still \"Acquired 408 Kina.\"", string.Join(" | ", Reread.AfterOverlap(new[] { "Acquired 54 Kina.", "cquired 293 Kina", "cquihed 408 Kina", "squired 368 Kina." }, new[] { "Acquired 54 Kina.", "Acquired 293 Kina.", "Acquired 408 Kina.", "Acquired 368 Kina.", "Acquired 189 Kina." })), "Acquired 189 Kina.");
            // the settle: each counted line stands for one line on screen
            var settled = Reread.Settled(new[] { "Riverside Pauldrons acquired.", "Riverside Pauldrons acquired.", "[P] hi" }, new[] { "[P] hi" }, new List<string>(), new[] { "Riverside Pauldrons acquired." });
            Check("settle: one of two identical lines counted: the other is new", settled.Count.ToString(), "1");
        }
        // a fragment while the line faded in, then the whole line in its place (293 at 13:11:54)
        {
            var v = new ChatVoter();
            var pv = new ChatParser(items);
            var r1 = new List<string> { "[P1] ya a long time ago", "3 Kina." };
            var r2 = new List<string> { "[P1] ya a long time ago", "Acquired 293 Kina." };
            var got = v.Push(new List<string> { "3 Kina." }, r1, t0, pv.Meaning).Concat(v.Push(new List<string>(), r2, t0.AddSeconds(1.2), pv.Meaning)).ToList();
            Check("vote: \"3 Kina.\" then \"Acquired 293 Kina.\" in its place", string.Join(" | ", got), "Acquired 293 Kina.");
        }

        // the Kinah counter as the deciding witness for Kina
        {
            var w = new KinaWitness();
            var k0 = t0.AddMinutes(20);
            KinaWitness.Candidate C(long a, double at, string line) => new(a, k0.AddSeconds(at), line, line);
            var log = new List<string>();
            void Take(KinaWitness.Outcome o)
            {
                log.AddRange(o.Confirmed.Select(c => "+" + c.Amount));
                log.AddRange(o.Refused.Select(c => "x" + c.Amount));
                log.AddRange(o.Unjudged.Select(c => "?" + c.Amount));
                log.AddRange(o.Absorbed.Select(c => "=" + c.Amount));
                log.AddRange(o.FromCounter.Select(f => "c" + f.Amount + (f.Line != null ? "(line)" : "")));
            }
            Take(w.Observe(29_000_000, k0));
            Check("witness: live after a reading", w.Live(k0.AddSeconds(1)).ToString(), "True");
            Take(w.Observe(29_000_408, k0.AddSeconds(1)));
            Take(w.Offer(C(408, 2, "Acquired 408 Kina."), k0.AddSeconds(2)));
            Take(w.Offer(C(408, 6, "Acquired 408 Kina."), k0.AddSeconds(6)));              // the same line read again
            Take(w.Offer(C(2367, 6.5, "Acquired 2367Kina."), k0.AddSeconds(6.5)));         // a misread amount
            Take(w.Observe(29_000_408, k0.AddSeconds(7)));
            Take(w.Observe(29_000_408, k0.AddSeconds(10)));
            Check("witness: a line on its rise counts; a repeat and a misread don't", string.Join(" ", log), "+408 x408 x2367");
            log.Clear();
            w.Seen(665, "Acquired 665 Kina.", k0.AddSeconds(11));                          // shown, never counted (an unaligned read)
            Take(w.Observe(29_001_073, k0.AddSeconds(12)));
            Take(w.Observe(29_001_073, k0.AddSeconds(21)));
            Check("witness: a rise no line claims is the line the chat showed", string.Join(" ", log), "c665(line)");
            log.Clear();
            Take(w.Offer(C(665, 22, "Acquired 665 Kina."), k0.AddSeconds(22)));             // its line, read late
            Take(w.Observe(29_001_581, k0.AddSeconds(23)));
            Take(w.Observe(29_001_581, k0.AddSeconds(32)));
            Check("witness: its late line is that rise; a rise with no line counts from the counter", string.Join(" ", log), "=665 c508");
            log.Clear();
            w = new KinaWitness();
            Take(w.Observe(29_001_581, k0.AddSeconds(32.5)));
            Take(w.Offer(C(344, 33, "Acquired 344 Kina."), k0.AddSeconds(33)));
            Take(w.Observe(29_000_000, k0.AddSeconds(34)));                                  // Kinah spent
            Check("witness: a fall (Kinah spent): what waits counts as read", string.Join(" ", log), "?344");
            log.Clear();
            Take(w.Offer(C(150, 35, "Acquired 150 Kina."), k0.AddSeconds(35)));
            Take(w.Tick(k0.AddSeconds(60)));
            Check("witness: the counter gone quiet: what waits counts as read", string.Join(" ", log) + " live " + w.Live(k0.AddSeconds(60)), "?150 live False");
            // 2026-10-26 (text-2026-10-25-kinah-counter-lag): through a fight the counter wasn't trusted for 15 s, its lines
            // counted without it, and the next reading's rise of 4,076 was counted again from the counter. A reading after
            // the counter went quiet starts the pool again: the rise meanwhile belongs to the lines already counted
            log.Clear();
            Take(w.Observe(29_004_076, k0.AddSeconds(80)));
            Take(w.Observe(29_004_076, k0.AddSeconds(83)));
            Take(w.Tick(k0.AddSeconds(95)));
            Take(w.Flush(k0.AddSeconds(96)));
            Check("witness: read again after going quiet: the rise meanwhile isn't counted again", $"[{string.Join(" ", log)}] live {w.Live(k0.AddSeconds(83))}", "[] live True");
        }

        // an item line the chat shows again after a scroll (ItemRepeatGuard; 2026-10-24 15:59, Kukuru 2 and Orichalcum Ore 4
        // for 1 and 3): a read three lines long that shares only its top two with the next one lines that one up on them, so
        // the line counted a moment ago comes back as new. Without a second witness it is that line; with a fresh loot list
        // row or a gathering race behind it, it is a second pickup
        {
            var r0 = t0.AddMinutes(40);
            int Run(Action<TrackerSession>? second)
            {
                var s = new TrackerSession(null, settings, items, "Test") { Vote = false };
                s.ProcessChat(new List<string> { "[P1] so the boss", "[P2] spawns at ten", "[P3] ok thanks" }, r0);
                s.ProcessChat(new List<string> { "[P1] so the boss", "[P2] spawns at ten", "[P3] ok thanks", "Orichalcum Ore acquired." }, r0.AddSeconds(1));
                s.ProcessChat(new List<string> { "[P1] so the boss", "[P2] spawns at ten", "[P9] wts" }, r0.AddSeconds(3));
                second?.Invoke(s);
                s.ProcessChat(new List<string> { "[P1] so the boss", "[P2] spawns at ten", "[P3] ok thanks", "Orichalcum Ore acquired." }, r0.AddSeconds(5));
                for (int i = 6; i <= 14; i += 2) s.ProcessChat(new List<string> { "[P1] so the boss", "[P2] spawns at ten", "[P3] ok thanks", "Orichalcum Ore acquired." }, r0.AddSeconds(i));
                s.FlushVotes(r0.AddSeconds(14));
                return N(s, "Orichalcum Ore");
            }
            Check("repeat: the line back after a scroll, no witness: once", Run(null).ToString(), "1");
            Check("repeat: a second pickup with a fresh loot list row: twice", Run(s =>
            {
                s.ProcessList(new List<LootLine> { new(1197, "Orichalcum Ore x1", 0.62, 0xF0F0F0F0F0F0F0F0UL, 0.14, 0.12, 1920) }, r0.AddSeconds(4.4));
                s.ProcessList(new List<LootLine> { new(1167, "Orichalcum Ore x1", 0.62, 0xF0F0F0F0F0F0F0F0UL, 0.14, 0.12, 1920) }, r0.AddSeconds(4.9));
            }).ToString(), "2");
            Check("repeat: a second pickup with a new gathering race: twice", Run(s => s.GatherOutcome(r0.AddSeconds(4.6), true, false)).ToString(), "2");

            // the guard alone: the same place under the same lines is the same line; a witness one copy took stands for no second
            var g = new ItemRepeatGuard();
            var place = new ItemRepeatGuard.Place(0, 10, "[P2] spawns at ten", "Kukuru acquired.");
            var none = new ItemRepeatGuard.Screen(1, DateTime.MinValue);
            g.Race(r0.AddSeconds(59.8), "", Data.GatherFamily.Unknown);
            g.Counted("Orichalcum Ore", 1, r0.AddSeconds(60), r0.AddSeconds(61), place);
            g.Row("Orichalcum Ore", r0.AddSeconds(61.3), 7);
            Check("repeat: where the counted copy stood, under the same lines", g.Judge("Orichalcum Ore", 1, r0.AddSeconds(63), r0.AddSeconds(64), place with { Above1 = "[P2] spawns at tcn" }, none, false).V.ToString(), "Repeat");
            Check("repeat: the first pickup's race and row are one witness, taken", g.Judge("Orichalcum Ore", 1, r0.AddSeconds(63), r0.AddSeconds(64), null, none, false).V.ToString(), "Wait");
            Check("...and once the wait is over, a repeat", g.Judge("Orichalcum Ore", 1, r0.AddSeconds(63), r0.AddSeconds(69), null, none, false).V.ToString(), "Repeat");
            Check("repeat: a read showing two such lines shows a second line", g.Judge("Orichalcum Ore", 1, r0.AddSeconds(63), r0.AddSeconds(64), null, new(2, DateTime.MinValue), false).V.ToString(), "Count");
            Check("repeat: one line on screen, the counted copy scrolled off: a line of its own", g.Judge("Orichalcum Ore", 1, r0.AddSeconds(80), r0.AddSeconds(81), null, new(1, r0.AddSeconds(70)), false).V.ToString(), "Count");
            Check("repeat: a kill stands behind a drop, not behind a gathered ore", g.Judge("Orichalcum Ore", 1, r0.AddSeconds(63), r0.AddSeconds(69), null, none, true).V + " " + new Func<string>(() => { g.Kill(r0.AddSeconds(62.5)); return g.Judge("Orichalcum Ore", 1, r0.AddSeconds(63), r0.AddSeconds(69), null, none, true).V.ToString(); })(), "Repeat Repeat");
            g.Counted("Spiritstone", 1, r0.AddSeconds(60.2), r0.AddSeconds(61), null);
            g.Kill(r0.AddSeconds(59.9));
            Check("repeat: a drop's second copy with a kill of its own: counted", g.Judge("Spiritstone", 1, r0.AddSeconds(66), r0.AddSeconds(67), null, none, true).V.ToString(), "Count");
            Check("repeat: an item counted over 30 s ago: no repeat check", g.Judge("Orichalcum Ore", 1, r0.AddSeconds(95), r0.AddSeconds(95), null, none, true).V.ToString(), "Count");

            // 2026-10-25 16:44, Steven's Illusion Gloves (replay case text-2026-10-25-illusion-gloves-scrolled): the chat scrolled
            // the line seven places up in five seconds, a garbled read between made the next clean read line up on nothing, and a
            // Krall farm fight's kills, one every second or two, stood for a second pair. Under the same two lines it was counted
            // under, one of them once in the read, it is that copy wherever it sits
            var fight = new List<string> { "IIIC", "Obtained 1 Enhanced Krall Combatant Soul", "Points.", "Acquired 521 Kina.", "Illusion Gloves acquired.", "Acquired 253 Kina.", "Obtained 1 Enhanced Krall Combatant Soul", "Points.", "Acquired 441 Kina.", "Obtained 1 Enhanced Krall Combatant Soul", "Points." };
            var sg = new ItemRepeatGuard();
            sg.Kill(r0.AddSeconds(100.5));
            sg.Counted("Illusion Gloves", 1, r0.AddSeconds(100), r0.AddSeconds(101.2), new ItemRepeatGuard.Place(0, 10, "Acquired 521 Kina.", "Points."));
            sg.Kill(r0.AddSeconds(103.3)); sg.Kill(r0.AddSeconds(104)); sg.Kill(r0.AddSeconds(105.2));
            var up = new ItemRepeatGuard.Place(6, 11, "Acquired 521 Kina.", "Points.");
            Check("repeat: the line scrolled up under the lines it was counted under, kills all round: that copy", sg.Judge("Illusion Gloves", 1, r0.AddSeconds(105.3), r0.AddSeconds(105.3), up, new(1, DateTime.MinValue, fight), false).V.ToString(), "Repeat");
            Check("...a read showing a second such line: a line of its own", sg.Judge("Illusion Gloves", 1, r0.AddSeconds(105.3), r0.AddSeconds(105.3), up, new(2, DateTime.MinValue, fight), false).V.ToString(), "Count");
            Check("...with no read to look at: the kills decide, as before", sg.Judge("Illusion Gloves", 1, r0.AddSeconds(105.3), r0.AddSeconds(105.3), up, none, false).V.ToString(), "Count");
            // under two lines the read shows three times each (the soul line, "Points."): nothing tells one pair from another, the kills decide
            var gg = new ItemRepeatGuard();
            gg.Kill(r0.AddSeconds(100.5));
            gg.Counted("Spiritstone", 1, r0.AddSeconds(100), r0.AddSeconds(101.2), new ItemRepeatGuard.Place(0, 10, "Points.", "Obtained 1 Enhanced Krall Combatant Soul"));
            gg.Kill(r0.AddSeconds(104));
            var fight2 = new List<string> { "Acquired 521 Kina.", "Obtained 1 Enhanced Krall Combatant Soul", "Points.", "Spiritstone acquired.", "Acquired 253 Kina.", "Obtained 1 Enhanced Krall Combatant Soul", "Points.", "Acquired 441 Kina.", "Obtained 1 Enhanced Krall Combatant Soul", "Points." };
            Check("...under lines the read shows three times each: the kills decide", gg.Judge("Spiritstone", 1, r0.AddSeconds(105.3), r0.AddSeconds(105.3), new ItemRepeatGuard.Place(6, 10, "Points.", "Obtained 1 Enhanced Krall Combatant Soul"), new(1, DateTime.MinValue, fight2), false).V.ToString(), "Count");
        }
        return ok;
    }
}
