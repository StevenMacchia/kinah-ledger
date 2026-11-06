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
        // 2026-10-25 13:48: "Acquired Daeva Pass Shop Coin x2,OOO." came in as "not in the item list" 15 times
        Check("count tail: thousands marks and O for 0 (\"Daeva Pass Shop Coin x2,OOO\")", T("Daeva Pass Shop Coin x2,OOO") + " · " + T("Enhance Stone x5,OOO.") + " · " + T("Odyle x1.2OO"), "Daeva Pass Shop Coin x2000 · Enhance Stone x5000 · Odyle x1200");
        Check("count tail: a mark with no digit before it is no count (\"Odyle xO,OOO\")", T("Odyle xO,OOO"), "none");

        var p = new ChatParser(items);
        Check("parsed: \"Acquired Refining StonevxT.\"", P(p, "Acquired Refining StonevxT."), "Refining Stone x7");
        Check("parsed: \"Acquired Odyle xb.\"", P(p, "Acquired Odyle xb."), "Odyle x6");
        Check("parsed: \"Acquired Fine Odyle xS.\"", P(p, "Acquired Fine Odyle xS."), "Fine Odyle x5");
        Check("parsed: \"Acquired Odyle x1O.\"", P(p, "Acquired Odyle x1O."), "Odyle x10");
        Check("parsed: \"Acquired Refining Stonevx6.\" (as before)", P(p, "Acquired Refining Stonevx6."), "Refining Stone x6");
        Check("parsed: \"Acquired Daeva Pass Shop Coin x2,OOO.\" (13:48)", P(p, "Acquired Daeva Pass Shop Coin x2,OOO."), "Daeva Pass Shop Coin x2000");
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
            // a soul line read garbled as it faded in, its verb lost, then clean a read later (bench 131029, 13:11:42 to :43): the
            // same line, not new; and the Kina line the garbled read lost at its bottom comes back as itself
            var lt5 = new LineTracker();
            lt5.NewLines(new List<string> { "['Xin] but vakropldoesn:t for", "cosmetic purpose'!", "Sapphire Gemstonejacquired.", "Acquired 665 Kina.", "Acquired 213 Kina.\"", "Obtained 1' Cogni Soyl Crystals.-", "Acquired 54 Kina." });
            lt5.NewLines(new List<string> { "'lmgh", "cosmetic ave * .", ".Sapphire Gemstone acquire'", "Zike&gs fer", "Acquired 665 Kina.", "cquired 213 Kina:", ")btained 1 cogni soul cwslals" });
            Check("a soul line read garbled while fading, then clean: not new", string.Join(" | ", lt5.NewLines(new List<string> { "zikel g'fpr", "cosmetic", "Sapphire Gemstone acquirsd.", "Acquired 665 Kina.", "Acquired 213 Kinae", "Obtained 1 Cogni-Soul Crystals.", "Acquired 54 Kina." })), "");
            Check("a soul line read garbled while fading: a new Kina line after it is new", string.Join(" | ", lt5.NewLines(new List<string> { "cosmetic purposes!", "-sapphire Gemst&ne acquired", "Acquired 665 Kina.", "Acquired* 213 Kina.", "Obtained 1 Cognisoul Crysta!s:", "Acquired 54 Kina.", "Rr uired 236 Kina." })), "Rr uired 236 Kina.");
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

        // ---- the live-log loop's first run (2026-11-06, tools/log-mine.md): spots mined from reads.old.log and trace.old.log of
        // 2026-11-05, each test built from the reads the log kept ----
        Console.WriteLine("\nLive-log spots of 2026-11-05 (log-mine): a count cut off is the same line; another player's clipped line; a wrapped soul points line:");
        {
            string D2(GameEvent e) => e is CurrencyGained c ? $"{c.Amount} {(c.Enemy.Length > 0 ? c.Enemy + " " : "")}{c.Name}" : D(e);
            // the same line read with its count and without it (Reread.CountLost)
            Check("count lost: \"Acquired Fine Odyle x4.\" and \"Acquired Fine Odyle\"", Reread.CountLost("Acquired Fine Odyle x4.", "Acquired Fine Odyle").ToString(), "True");
            Check("count lost: \"Acquired Ulmus Oil (Bound) x201.\" and \"... (Bound)\"", Reread.CountLost("Acquired Ulmus Oil (Bound) x201.", "Acquired Ulmus Oil (Bound)").ToString(), "True");
            Check("count lost: never two Kina amounts, never two counts, never another item", $"{Reread.CountLost("Acquired 463 Kina.", "Acquired 46 Kina.")} {Reread.CountLost("Acquired Odyle x12.", "Acquired Odyle x1")} {Reread.CountLost("Acquired Fine Odyle x4.", "Acquired Pure Odyle")}", "False False False");
            // 18:31:29 to :31: "Acquired Fresh Raw Meat" cut at the chat's edge in one read, whole in the next two: the vote takes the whole line
            var vc = new ChatVoter(); var pc = new ChatParser(items);
            var c1 = new List<string> { "[Tryphon] Wb train aullamu", "Acquired Fresh Raw Meat" };
            var c2 = new List<string> { "[Tryphon] Wb train aullamu", "Acquired Fresh Raw Meat x 100." };
            var voted = vc.Push(new List<string> { "Acquired Fresh Raw Meat" }, c1, t0, pc.Meaning).Concat(vc.Push(new List<string>(), c2, t0.AddSeconds(0.4), pc.Meaning)).Concat(vc.Push(new List<string>(), c2, t0.AddSeconds(0.8), pc.Meaning)).ToList();
            Check("vote: a count cut off, then the whole line twice: the whole line", string.Join(" | ", voted), "Acquired Fresh Raw Meat x 100.");
            // 14:35:13 to :15 (Incursion of the Spirits' rewards): the chat covered, then a read of 11 lines that lined up with nothing; its
            // "Acquired Fine Odyle x4." counted from the burst, then the reads after cut the line to "Acquired Fine Odyle" at the chat's
            // edge, read the same three times: the unaligned watch counted a fifth Fine Odyle
            {
                var s = new TrackerSession(null, settings, items, "Test");
                var ta = new DateTime(2026, 11, 5, 19, 35, 0, DateTimeKind.Utc);
                s.ProcessChat(new List<string> { "[Pa] hello there all", "[Pb] who wants a group", "[Pc] selling stuff cheap" }, ta);
                for (int i = 1; i <= 3; i++) s.ProcessChat(new List<string>(), ta.AddSeconds(i));
                var f1 = new List<string> { "Radiant Odyle acquired.", "Pure Odyle acquired.", "Acquired Fine Odyle x4.", "Acquired Odyle x5.", "Expert's Refining Stone acquired.", "Acquired Refining Stone x5.", "Acquired Soul Codex (Bound) x3.", "super whale", "[Malenia[NEZ]] detected", "[Malenia[NEZ]] enchantedd", "z'" };
                var f2 = new List<string> { "Pure Odyle acquired.", "Acquired Fine Odyle", "Acquired Odyle x5.", "Expert's Refining Stone acquired.", "Acquired Refining Stone x5.", "Acquired Soul Codex (Bound) x3.", "[Malenia[NEZ]] super whale", "detected", "enchantedd", "[zMochi] [Force Recruitment] WB Ansas" };
                s.ProcessChat(f1, ta.AddSeconds(13.7));
                foreach (var at in new[] { 14.1, 14.4, 15.0, 15.4 }) s.ProcessChat(new List<string>(f2), ta.AddSeconds(at));
                s.FlushVotes(ta.AddSeconds(17));
                Check("14:35:13: \"Acquired Fine Odyle x4.\" from the burst, then cut to \"Acquired Fine Odyle\": four", $"Fine Odyle {N(s, "Fine Odyle")} · Odyle {N(s, "Odyle")} · Refining Stone {N(s, "Refining Stone")}", "Fine Odyle 4 · Odyle 5 · Refining Stone 5");
            }
            // 14:40:15: another player's line wrapped, its name cut off at the chat's edge ("Psymonator[NEM] acquired Soul: Intermediate" /
            // "Fire Spirit (Bound) through party distribution."): counted for the run as an item named "Soul: Intermediate"
            Check("\"acquired Soul: Intermediate\" (another player's head, clipped): nothing", P(p, "acquired Soul: Intermediate") + " · " + p.Meaning("acquired Soul: Intermediate").Key + " · " + P(p, "Acquired 463 Kina."), "(nothing) ·  · Kina 463");
            // 17:13:28: an enemy's name long enough to wrap its soul points line; the five single-line soul lines around it counted, this one not
            var pw = new ChatParser(items);
            var wrapped = new List<string> { "Obtained 5 Mudthorn Soul Points.", "Obtained 10 Spirit King's Bodyguard Warrior", "Soul Points.", "Obtained 5 Furious Feruk Soul Points." };
            Check("\"Obtained 10 Spirit King's Bodyguard Warrior\" / \"Soul Points.\": one soul points line", string.Join(" | ", pw.ParseBatch(wrapped, t0).Select(b => b.Events.Count == 0 ? (ReferenceEquals(b.Line, ChatParser.JoinedMark) ? "(joined)" : "(nothing)") : string.Join("; ", b.Events.Select(D2)))), "5 Mudthorn Soul Points | (joined) | 10 Spirit King's Bodyguard Warrior Soul Points | 5 Furious Feruk Soul Points");
            var tails = pw.JoinWrappedTails(new List<string> { "Obtained 10 Spirit King's Bodyguard Warrior" }, wrapped, t0);
            Check("...and from a burst read, parsed one line at a time (JoinWrappedTails)", string.Join(" | ", tails.Select(x => x.Text)), "Obtained 10 Spirit King's Bodyguard Warrior Soul Points.");
            {
                var s = new TrackerSession(null, settings, items, "Test");
                var tw = new DateTime(2026, 11, 5, 22, 13, 0, DateTimeKind.Utc);
                s.ProcessChat(new List<string> { "[Pa] hello there all", "[Pb] who wants a group", "[Pc] selling stuff cheap" }, tw);
                for (int i = 1; i <= 3; i++) s.ProcessChat(new List<string>(), tw.AddSeconds(i));
                var b1 = new List<string> { "Obtained 10 Forest Monster Soul Points?", "Obtained 5 Parasite Swarm Soul Points.", "Obtained 5 Superior EarthSpirit Soul Points.", "Obtained 5 Mudthorn Soul Points.", "Obtained 10 Spirit King's Bodyguard Warrior", "Soul Points.", "Obtained 5 Furious Feruk Soul Points.", "Increased Pet Insight Level Krao Test Subject.", "[Lur] meow", "Increased Pet Insight Level Forest Monster." };
                s.ProcessChat(b1, tw.AddSeconds(28.2));
                s.ProcessChat(new List<string>(b1), tw.AddSeconds(29.0));
                s.FlushVotes(tw.AddSeconds(31));
                Check("17:13:28: six soul points lines in a burst read, one of them wrapped: six", s.Engine.CurrencyLines.ToString(), "6");
            }
        }

        // ---- the live-log loop's second run (2026-11-06, tools/log-mine.md): spots mined from reads.log and trace.log of
        // 2026-11-06 09:00 to 11:44 (tracker 0.10.6 live), and Steven's miscount report of 11:00:58 ----
        Console.WriteLine("\nLive-log spots of 2026-11-06 (log-mine): a garbled copy is its line; a wrapped count tail; a fading read's digits; a kill's payout in the Abyss Corridor:");
        {
            string D2(GameEvent e) => e is CurrencyGained c ? $"{c.Amount} {(c.Enemy.Length > 0 ? c.Enemy + " " : "")}{c.Name}" : D(e);
            // 09:09:47.8: a chat of ten Kukuru lines, read once with "Acquired Kukuru x3." cut to "Acquired Kukuru" at the chat's
            // edge: lining up on its tail found an earlier copy of the lines around it and took seven lines for new (nine Kukuru
            // and two Pure Orichalcum Ore counted again)
            {
                var kk = new List<string> { "Acquired Kukuru x3.", "Kukuru acquired.", "Kukuru acquired.", "Acquired Kukuru x3.", "Acquired Kukuru x3.", "Kukuru acquired.", "Acquired Pure Orichalcum Ore x2.", "Acquired Kukuru x3.", "Kukuru acquired.", "Kukuru acquired." };
                var lt = new LineTracker();
                lt.NewLines(new List<string>(kk));
                lt.NewLines(new List<string>(kk));
                var cut = new List<string>(kk); cut[7] = "Acquired Kukuru";
                Check("09:09:47 a count cut off in a chat full of one line: the same screen, nothing new", string.Join(" | ", lt.NewLines(cut)), "");
                Check("...and the clean read after it: nothing new", string.Join(" | ", lt.NewLines(new List<string>(kk))), "");
                var ore = new List<string>(kk.Skip(1)) { "Orichalcum Ore acquired." };
                Check("...a line that did arrive under them is new", string.Join(" | ", lt.NewLines(ore)), "Orichalcum Ore acquired.");
            }
            // 09:03:01.4: the chat covered for 20 s (the world behind it, a window's labels), then a read lined up with the last
            // full read: "Kulsuru-acquired." under "Kukuru acquired." (two letters off) lined up one line short, so the garbled
            // copy counted as a third Kukuru ("the read shows 3 such lines")
            {
                var lt = new LineTracker();
                lt.NewLines(new List<string> { "[Bfirblocked", "[MrGaines] English Legion <Grace> Recruiting", "friendly active members!", "Orichalcum Ore acquired.", "Kukuru acquired.", "[uSubs4Heals] friendly rgbtq.:kekekee", "Acquired Kukuru x2.", "Acquired Kukuru x6.", "Kukuru acquired.", "Kukuru acquired." });   // 09:02:38.5
                lt.NewLines(new List<string> { "ltle Papis" });                       // 09:02:41, the world behind the chat
                lt.NewLines(new List<string> { "iff", "Wraith Evasion", "Open" });    // 09:02:49, a window's labels
                Check("09:03:01 lined up with the last full read: a garbled copy of a line is that line, the joined lines are new", string.Join(" | ", lt.NewLines(new List<string> { "Acquired KukurXl", "Kukuru acquired.", "Kulsuru-acquired.", "Rav joined event.", "ImRumi joined event.", "Cailia joined event.", "Teemo joined event.", "Astraeus joined event.", "ikarus joined event.", "Ylva joined event." })), "Rav joined event. | ImRumi joined event. | Cailia joined event. | Teemo joined event. | Astraeus joined event. | ikarus joined event. | Ylva joined event.");
            }
            // 09:29:28.4 to 29.2 (the Abyss Corridor): a read of seven lines garbled as they faded in ("c quired 787 Rank Point(s)"
            // for 181, "quired 790 Abyss Point(s)" for 190), then the clean read: the kill's 181 Rank, 80 Kina and 190 Abyss Points,
            // counted from the read before, came up as new a second time (181 Rank and 80 Kina counted again)
            {
                var lt = new LineTracker();
                lt.NewLines(new List<string> { "Acquired GI 89", "Acquired\" 245 Ra", "Acquired!214 Kina.", "Acq�ir�d 257", "Acquired 203", "Acquired 180 Kina.", "Acquired 213 Abyss Point(s).", "Acquired 181 Rank Point(s).", "Acquired 80 Kina.", "Acquired 190 Abyss Point(s)." });   // 09:29:27.9
                Check("09:29:28 a read garbled as it faded in: nothing new", string.Join(" | ", lt.NewLines(new List<string> { "quired 245 Rank�point(s)", "Acquire d 277 Kina", "1_4fguihed 257 Abyss Point(s)", "Acquire d 203 Rank Point(s)", "cguired 213 Abyss Point(s)", "c quired 787 Rank Point(s)", "quired 790 Abyss Point(s)" })), "");
                Check("09:29:29 the clean read after it: the lines the garbled read misread are not new", string.Join(" | ", lt.NewLines(new List<string> { "Acquired 213 Abyss Point(s).", "Acquired 181 Rank Point(s).", "Acquired 80 Kina.", "Acquired 190 Abyss Point(s).", "Point(s).", "Acquired )182 Kina.", "Acquired 248Abyss Point(s).", "Acquired 207 Rank Point(s).", "Acquired 84 Kina.", "Acquired 217 Abyss Point(s)." })), "Point(s). | Acquired )182 Kina. | Acquired 248Abyss Point(s). | Acquired 207 Rank Point(s). | Acquired 84 Kina. | Acquired 217 Abyss Point(s).");
            }
            // 11:05:37.6 to 41.6: a gather's two "Aria acquired." after a fade, then a read the recognizer missed (the chat faint)
            // and the same two lines once more, the second half gone: "Aria acquired." counted a third time (the loot list showed two)
            {
                var lt = new LineTracker();
                lt.NewLines(new List<string> { "[Pa] hello there all", "[Pb] who wants a group", "[Pc] selling stuff cheap" });
                lt.NewLines(new List<string>());
                Check("11:05:37 after a fade, two Aria lines: both new", string.Join(" | ", lt.NewLines(new List<string> { "Aria acquired.", "Aria acquired." })), "Aria acquired. | Aria acquired.");
                lt.NewLines(new List<string> { "Aria acquired.", "Aria acquired." });
                lt.NewLines(new List<string>());
                Check("11:05:41 the same two lines after a missed read, one half gone: fading out, not a third", string.Join(" | ", lt.NewLines(new List<string> { "Aria acquired.", "A u. • eauired." })), "");
                lt.NewLines(new List<string>());
                Check("...a clean line after a fade is still what arrived", string.Join(" | ", lt.NewLines(new List<string> { "Aria acquired." })), "Aria acquired.");
            }
            // 11:34:24.5: "Acquired Artwork Scrap: Field Boss I (Elyos)" with its "(Bound) x 10." wrapped onto the next line: the item
            // only comes bound, so the first half read as a reward, and the tail rule joined item lines only: x1, "quantity unsure"
            var pt = new ChatParser(items);
            Check("wrapped tail: \"Acquired Artwork Scrap: Field Boss I (Elyos)\" / \"(Bound) x 10.\"", string.Join(" | ", pt.ParseBatch(new List<string> { "Acquired 202 Kina.", "Acquired Artwork Scrap: Field Boss I (Elyos)", "(Bound) x 10." }, t0).Select(b => b.Events.Count == 0 ? (ReferenceEquals(b.Line, ChatParser.JoinedMark) ? "(joined)" : "(nothing)") : string.Join("; ", b.Events.Select(D)))), "Kina 202 | reward Artwork Scrap: Field Boss I (Elyos) x10 | (joined)");
            // 09:36:10.3 to 11.5: three readings of "Acquired Instant Clear Ticket: Daily Dungeon", the third clean but for "AQquired":
            // the vote let the guessed "D9ilY>Dungeofi" stand for the group, refused as on no table nearby; the x14 never counted
            Check("\"AQquired Instant Clear Ticket: Daily Dungeon\" is the verb", ChatParser.CleanSystemLine("AQquired Instant Clear Ticket: Daily Dungeon"), "Acquired Instant Clear Ticket: Daily Dungeon");
            {
                var v = new ChatVoter(); var pv = new ChatParser(items);
                var h = new List<string> { "[Vamp] need force", "[Vamp] for Wb" };
                var r1 = new List<string>(h) { "Aqquiced�lnstant Cl�ar'Ticket: Daily Dungeon", "(Bound) x 14." };
                var r2 = new List<string>(h) { "Acquired Instant Clear Ticket: D9ilY>Dungeofi", "(Bound) x 14." };
                var r3 = new List<string>(h) { "AQquired Instant Clear Ticket: Daily Dungeon", "(Bound) x 14." };
                var got = v.Push(r1.Skip(2).ToList(), r1, t0, pv.Meaning).Concat(v.Push(new List<string>(), r2, t0.AddSeconds(0.6), pv.Meaning)).Concat(v.Push(new List<string>(), r3, t0.AddSeconds(1.2), pv.Meaning)).ToList();
                Check("vote: the reading read exactly stands for its group, not the guessed one", string.Join(" | ", got), "AQquired Instant Clear Ticket: Daily Dungeon | (Bound) x 14.");
                Check("...and parsed with its tail: x14", string.Join(" | ", pv.ParseBatch(got, t0).Where(b => b.Events.Count > 0).Select(b => string.Join("; ", b.Events.Select(D)))), "reward Instant Clear Ticket: Daily Dungeon x14");
            }
            // Steven's miscount report of 11:00:58 (reads.log 10:56:20.8): "Acquired-wrathfuVMind¯x2." read so in three reads, a dash
            // where the space after the verb was: nothing counted, while the two "Wrathful Mind acquired." beside it counted: 2 of 4
            Check("\"Acquired-wrathfuVMind¯x2.\" (a dash after the verb, the name a letter off): Wrathful Mind x2", P(p, "Acquired-wrathfuVMind¯x2.") + " · " + P(p, "Acquired-WrathfuleMind x2."), "Wrathful Mind x2 · Wrathful Mind x2");
            // 09:30:06.8 to 10.0 (the Abyss Corridor): a read of fifteen lines garbled as they faded in; "? quired 756 Kina" (156 Kina)
            // read again at its place and counted as 756, then "Acquired 156 Kina." from the clean read: a kill's Kina twice, 912 for 156
            {
                var s = new TrackerSession(null, settings, items, "Test");
                var t4 = new DateTime(2026, 11, 6, 14, 30, 0, DateTimeKind.Utc);
                s.ProcessChat(new List<string> { "Acquired 221 Abyss Point(s).", "Acquired 208 Rane", "Acquired 228 Kina.", "Acquired 218 Abyss Point(s).", "Acquired 252 Rank Point(s).", "Acquired 92 Kina.", "Acquired 264 Abyss Point(s)." }, t4);   // 09:30:06.8, the history
                s.ProcessChat(new List<string> { "Acquired 221 Abyss Point(s).", "Acquired*208 RanksPoint(s).", "Acquired 228 Kiha.", "Acquired 218 Abyss Doings).", "Acquired 252 Rank Point(s).", "Acquired 92 Kina.", "Acquired 264 Abyss Point(s).", "Acquired 235 Rank Point(s).", "Acquired 107 Kina.", "Acquired 246 Abyss Point(s)." }, t4.AddSeconds(0.6));   // 07.4
                s.ProcessChat(new List<string> { "2S \" \"UJYSsrp-", "A c quired 92 Rina", "Acquire d 264 A4bRyssi�..", "Qin�s)�", "A c quired 235 R�nk-p", "4 c quired�07 Kina", "�031ht(k�", "c quired 246 Abyss p", "t(sF�", "c quired 776 Rank p", "? quired 756 Kina", "oint(s)", "oint(s)", "70ired 784 Abyss P", "oint(s)" }, t4.AddSeconds(1.2));   // 08.0
                s.ProcessChat(new List<string> { "Acquired 21 &", "Acqui red952", "Acquired-92 Kina:", "Acquired", "Acquired235 Rank Point(S).", "Acquired 107 Kina.", "Acquired 246 Abyss Point(s).", "Acquired 176 Rank Point(s).", "Acquired 156 Kina.", "Acquired 184 Abyss Point(s)." }, t4.AddSeconds(1.9));   // 08.7
                s.ProcessChat(new List<string> { "Acquired 264 Abyss Point(s).", "Acqbired 235 Rank Point(s).", "AcquiredM07 Kina.", "Acquired 246 Abyss Point(s).", "Acquired 176\\Rank Point(s).", "Acquired 156' Kina.", "Acquired 184 Abyss Point(s).", "Acquired 184 Rank Point(s).", "Acquired 178 Kina.", "Acquired 193 Abyss Point(s)." }, t4.AddSeconds(2.6));   // 09.4
                s.ProcessChat(new List<string> { ":quired 264", "cquired 235 Rank point(s).", "cquired 107 Kina.", "Acquired 246 Abyss point(s)", "hi_s", "Acquired 176 Rank point(s),", "Acquired 156 Kipa.", "Acquired 184 Abyss point(s).", "Acquired 184 Rank", "Acquired 178 Kina.", "Abyss Pont(s)." }, t4.AddSeconds(3.2));   // 10.0
                s.FlushVotes(t4.AddSeconds(5));
                Check("09:30:08 a line read again still garbled, digits in it: not counted; the clean read counts once (107, 156, 178)", s.Engine.Totals().Kinah.ToString(), "441");
            }
            // 09:31:08.8 to 09.5: "cquired 137 Kina." read as the line faded in, then "Acquired 131 Kina." clean in its place: the
            // vote kept 137 (two readings a digit apart are two lines) and the loot list's 131 row waited for nothing
            {
                var v = new ChatVoter(); var pv = new ChatParser(items);
                var r1 = new List<string> { "Acquired 204 Kina.", "cquired 137 Kina." };
                var r2 = new List<string> { "Acquired 204 Kina.", "Acquired 131 Kina.", "Acquired 214 Abyss Point(s)." };
                var got = v.Push(new List<string> { "cquired 137 Kina." }, r1, t0, pv.Meaning).Concat(v.Push(new List<string> { "Acquired 214 Abyss Point(s)." }, r2, t0.AddSeconds(0.7), pv.Meaning)).Concat(v.Push(new List<string>(), r2, t0.AddSeconds(1.4), pv.Meaning)).Concat(v.Flush(pv.Meaning)).ToList();
                Check("vote: a fading first reading, then a clean one a digit apart in its place: the clean one", string.Join(" | ", got), "Acquired 131 Kina. | Acquired 214 Abyss Point(s).");
            }
            // 09:27 to 09:30 (the Abyss Corridor): Rank and Abyss Point lines read garbled or cut at the chat's edge were counted
            // for the run as items named "Rank' Points", "RankiPoints", "Pointe)" and "Abyss" (eight lines)
            var pr = new ChatParser(items) { InRun = () => true };
            string R6(string l) => string.Join("; ", pr.Parse(l, t0).Select(e => e is ItemAcquired a ? $"{items.NameOf(a.ItemId)} x{a.Count}" : D2(e))) is { Length: > 0 } s6 ? s6 : "(nothing)";
            Check("in a run: \"Acquired 207 Rank' Point(S).\", \"Acquired 213 RankiPoints.\", \"Acquired 189 Abyss\"", R6("Acquired 207 Rank' Point(S).") + " · " + R6("Acquired 213 RankiPoints.") + " · " + R6("Acquired 189 Abyss"), "Rank Points x207 · Rank Points x213 · Abyss Points x189");
            Check("...and with a stray mark around the amount: \"Acquired 179. Rane Point(s):\", \"Acquired -187 Abyss Point(s).\", \": quired 220 Kinö.\"", R6("Acquired 179. Rane Point(s):") + " · " + R6("Acquired -187 Abyss Point(s).") + " · " + P(p, ": quired 220 Kinö."), "Rank Points x179 · Abyss Points x187 · Kina 220");
            Check("...\"Acquired 213 Pointe)\" names no currency: nothing; \"Abyss Doings)\" is Abyss Points", R6("Acquired 213 Pointe)") + " · " + R6("Acquired 218 Abyss Doings)."), "(nothing) · Abyss Points x218");
            Check("out of a run the same lines are the kill's points", string.Join(" · ", new[] { "Acquired 207 Rank' Point(S).", "Acquired 189 Abyss" }.Select(l => string.Join("; ", p.Parse(l, t0).Select(D2)))), "207 Rank Points · 189 Abyss Points");
            Check("meaning: \"Acquired 181 Abyss Point(s).\" is exactly Abyss Points x181", p.Meaning("Acquired 181 Abyss Point(s).").ToString(), "(i:abyss pointsx181, True)");
            Check("a kill's payout lines are no reward batch", $"{p.IsKillPay("Acquired 181 Rank Point(s).")} {p.IsKillPay("Acquired 80 Kina.")} {p.IsKillPay("Acquired 190 Abyss Point(s).")} {p.IsKillPay("Acquired 17T Rank, Point(s).")} {p.IsKillPay("c quired 223 Abyss P")} {p.IsKillPay("Acquired 1,000 Abyss Point(s).")} {p.IsKillPay("Acquired Odyle x5.")} {p.IsKillPay("Vagabond Cloak acquired.")}", "True True True True True False False False");
            Check("Rank and Abyss Points are never \"more than one pickup gives\"", $"{PickupCheck.IsKillPoints("Rank Points")} {PickupCheck.IsKillPoints("Abyss Points")} {PickupCheck.IsKillPoints("Enhance Stone")}", "True True False");
            // 09:26 to 09:32: the Abyss Corridor is an event on the list, so a kill's three lines (Rank Points, Kina, Abyss Points)
            // were "its rewards coming in", the next kill's Kina "the open world's" and each 10 s run closed with its chat set aside
            // for a reward picture that never comes: ten runs, 440 chat items and the kills' Kina marked not read
            {
                var s = new TrackerSession(null, settings, items, "Test");
                var tc = new DateTime(2026, 11, 6, 14, 26, 0, DateTimeKind.Utc);
                s.ApplyZone("Abyss Corridor", tc);
                var chat = new List<string> { "[Pa] hello there all", "[Pb] who wants a group", "[Pc] selling stuff cheap" };
                s.ProcessChat(new List<string>(chat), tc.AddSeconds(1));
                var kills = new[] { (233, 183, 244), (252, 214, 264), (211, 126, 221), (198, 220, 207) };
                double at = 2;
                foreach (var (rank, kina, ap) in kills)
                {
                    chat.Add($"Acquired {rank} Rank Point(s)."); chat.Add($"Acquired {kina} Kina."); chat.Add($"Acquired {ap} Abyss Point(s).");
                    while (chat.Count > 10) chat.RemoveAt(0);
                    for (int i = 0; i < 3; i++) s.ProcessChat(new List<string>(chat), tc.AddSeconds(at + i * 0.5));
                    s.ApplyZone("Abyss Corridor", tc.AddSeconds(at + 1.5));
                    at += 8;
                }
                s.FlushVotes(tc.AddSeconds(at));
                var run = s.Instances.CurrentRun;
                Check("Abyss Corridor: one run, still open, counted from the chat (its Kina, its kills)", run == null ? "no run" : $"{run.Kind} {run.Zone} Kinah {run.Kinah} kills {run.Kills} paid {run.PaidAt != null}", "Event Abyss Corridor Kinah 743 kills 4 paid False");
                Check("...and a kill's three lines are no \"rewards came in\"", Log().Contains("Abyss Corridor: its rewards came in").ToString(), "False");
            }
            // 09:30:51.8: a read that couldn't be lined up (its neighbours garbled differently) held "Acquired 165 Kina." in its
            // middle; the loot list's 165 Kina row waited for a chat line and expired. The line read exactly and the row are two
            // witnesses of one pickup (as an item line and its row have been since 2026-10-27)
            {
                var s = new TrackerSession(null, settings, items, "Test") { Vote = false };
                var t8 = new DateTime(2026, 11, 6, 14, 30, 50, DateTimeKind.Utc);
                s.ProcessChat(new List<string> { "[P1] hello there all", "[P2] who wants a group", "Acquired 200 Kina.", "[P3] selling stuff cheap", "[P4] looking for group now" }, t8);
                s.ProcessList(new List<LootLine> { new(1197, "Kina x165", 0.62, 0xF0F0F0F0F0F0F0F0UL, 0.14, 0.12, 1920) }, t8.AddSeconds(0.6));
                s.ProcessList(new List<LootLine> { new(1197, "Kina x165", 0.62, 0xF0F0F0F0F0F0F0F0UL, 0.14, 0.12, 1920) }, t8.AddSeconds(1.0));
                s.ProcessChat(new List<string> { "[P2] wh0 wamts a gr0up", "Acqu1red 200 K1na.", "Acquired 165 Kina.", "[P3] se11ing stuf cheep", "[P4] l00king f0r gr0up n0w" }, t8.AddSeconds(1.2));
                s.FlushVotes(t8.AddSeconds(3));
                Check("09:30:51 a Kina line in a read that didn't line up, its loot list row waiting: counted, two witnesses", s.Engine.Totals().Kinah.ToString(), "165");
            }
            // 08:55:07.4: "Successfully obtained Drops Reward Item." printed with its Odyle Energy x6 and a Content Usage Ticket
            // Selection Chest: the inventory at work, like the mail's "Obtained items attached to mail." (08:54:08.6)
            Check("\"Successfully obtained Drops Reward Item.\" is the inventory at work, like the mail's line", $"{PickupCheck.IsInventoryLine("Successfully obtained Drops Reward Item.")} {PickupCheck.IsInventoryLine("Obtained items attached to mail.")}", "True True");
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

        // 2026-10-28 09:18:53, Steven's Daeva Pass level reward: "Acquired 9,000 Kina (Bouna)." claimed in Dawn Legion Base as
        // its window closed was counted as farming Kinah and a kill. Kina in a base is a reward, a sale or the mail, never
        // farming: the line is bound Kina (tracked apart), and a rise of the counter no line claims isn't counted; out in a
        // field the same line and the same rise are farming as before
        {
            var kb0 = t0.AddHours(3);
            string Line(string zone)
            {
                var s = new TrackerSession(null, settings, items, "Test") { Vote = false };
                s.ApplyZone(zone, kb0);
                var calm = new List<string> { "[P1] hello", "[P2] hi there" };
                s.ProcessChat(calm, kb0.AddSeconds(1));
                for (int i = 2; i <= 6; i++) s.ProcessChat(new List<string>(calm) { "Acquired 9,000 Kina (Bouna)." }, kb0.AddSeconds(i));
                s.FlushVotes(kb0.AddSeconds(8));
                var t = s.Engine.Totals();
                return $"{t.Kinah} {t.Kills} {s.Engine.KinahBound}";
            }
            string Counter(string zone)
            {
                var s = new TrackerSession(null, settings, items, "Test") { Vote = false };
                s.ApplyZone(zone, kb0);
                s.CheckBalance(1_000_000, kb0.AddSeconds(1));
                s.CheckBalance(1_000_000, kb0.AddSeconds(3));
                s.CheckBalance(1_001_200, kb0.AddSeconds(5));
                s.CheckBalance(1_001_200, kb0.AddSeconds(8));
                s.CheckBalance(1_001_200, kb0.AddSeconds(15));
                s.FlushVotes(kb0.AddSeconds(20));
                return s.Engine.Totals().Kinah.ToString();
            }
            Check("Kina in a base: a reward, never farming or a kill (Kinah kills bound)", Line("Dawn Legion Base"), "0 0 9000");
            Check("...the same line in a field is farming", Line("Western Cantas Valley").Split(' ') is var f ? $"{f[0]} {f[2]}" : "", "9000 0");
            Check("...a counter rise no line claims: not counted in a base, counted in a field", $"{Counter("Dawn Legion Base")} {Counter("Western Cantas Valley")}", "0 1200");
            Check("...a forward base is a base too", Line("Andsha Forward Base"), "0 0 9000");
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

        // 2026-11-06, the data-fill loop (tools/data-fill.js): an item it drafts into data/extra-items.json from its MetaBot page
        // is one a chat line resolves to by name, bound or not. Content Usage Ticket Selection Chest (Bound) is the fill's own
        // draft of that entry (its proof run reproduced the committed entry field for field): a Special Reward Chest, not
        // tradable, so the verb-first line counts inside a run and the name-first line counts anywhere.
        {
            var pf = new ChatParser(items) { InRun = () => true };
            Check("fill: \"Acquired Content Usage Ticket Selection Chest (Bound) x1.\" in a run", P(pf, "Acquired Content Usage Ticket Selection Chest (Bound) x1."), "Content Usage Ticket Selection Chest (Bound) x1");
            Check("fill: \"Content Usage Ticket Selection Chest (Bound) acquired.\"", P(p, "Content Usage Ticket Selection Chest (Bound) acquired."), "Content Usage Ticket Selection Chest (Bound) x1");
            Check("fill: the entry's grade and category as the page said", items.Get(items.IdOf("Content Usage Ticket Selection Chest (Bound)")) is { } fi ? $"{fi.Grade} {fi.Category}, tradable {fi.Tradable}" : "missing", "Special Reward Chest, tradable False");
        }
        return ok;
    }
}
