using System.Text.Json.Nodes;
using KinahLedger.Data;

namespace KinahLedger.Session;

/// <summary>
/// parse-test's checks for a Market buy counted as a drop and "Not a drop" (Steven, 2026-11-13 18:35: "i bought the egos and
/// they counted as drops it fucked up my totals"). Two "Wrathful Ego acquired." lines were set aside as bought; the crafting
/// window then covered the chat, a garbled read showed one of the two, and the clean reads after it took the lines for new,
/// with kills in the Abyss beside them for witnesses: two rare drops and their quest points. Here: the replay of those reads
/// (tests/replays/text-2026-11-13-market-ego-reread) counts no drop and gives no quest points; the repeat guard keeps a
/// set-aside line as a copy; and "Not a drop" takes one pickup out of every count and the quest steps it completed, keeps
/// it in not-drops.json, and Undo puts it all back. Writes only to scratch folders.
/// </summary>
public static class NotDropTests
{
    public static bool Run(ItemDictionary items, Settings settings)
    {
        bool ok = true;
        void Check(string what, string got, string expect)
        {
            bool pass = got == expect; ok &= pass;
            Console.WriteLine($"  {what,-66} -> {(got.Length > 56 ? got[..56] + "…" : got),-40} {(pass ? "ok" : "MISMATCH, expected " + expect)}");
        }
        Console.WriteLine("\nA Market buy read again after a window over the chat, and \"Not a drop\" (2026-11-13):");
        const string Ego = "Wrathful Ego";
        long egoId = items.IdOf(Ego);
        var t0 = DateTime.UtcNow.AddMinutes(-5);

        // a. the repeat guard: two lines set aside as bought are copies; read again 23 s later with a kill beside them and no
        // more copies on screen, they are those lines; a third copy on screen is a line of its own
        {
            var g = new ItemRepeatGuard();
            var read = new List<string> { "[Pa] hello", "thank you. My alt made me 30mil", "Wrathful Ego acquired.", "Wrathful Ego acquired." };
            g.Aside(Ego, 1, t0, t0, new ItemRepeatGuard.Place(1, 10, "thank you. My alt made me 30mil", "[Pa] hello"), "bought at the Market");
            g.Aside(Ego, 1, t0, t0, new ItemRepeatGuard.Place(0, 10, "Wrathful Ego acquired.", "thank you. My alt made me 30mil"), "bought at the Market");
            g.Kill(t0.AddSeconds(23), "Rank Point");
            var later = t0.AddSeconds(23);
            var screen = new ItemRepeatGuard.Screen(2, t0.AddSeconds(-60), read);
            var other = new ItemRepeatGuard.Place(3, 10, "Wrathful Ego acquired.", "thank you. My alt made meB0mil");
            Check("two bought lines read again, a kill beside them: a repeat", g.Judge(Ego, 1, later, later, other, screen, final: false).V.ToString(), "Repeat");
            Check("...a third such line on screen: a line of its own", g.Judge(Ego, 1, later, later, other, screen with { Shown = 3 }, final: false).V.ToString(), "Count");
            var much = t0.AddSeconds(200);
            // older than the guard's 30 s, a set-aside copy goes by its place, like a counted one
            var same = new ItemRepeatGuard.Place(0, 10, "Wrathful Ego acquired.", "thank you. My alt made me 30mil");
            Check("...200 s later (a long window over the chat), where it stood: a repeat", g.Judge(Ego, 1, much, much, same, screen, final: true).V.ToString(), "Repeat");
            Check("...200 s later somewhere else, a kill beside it: not settled by the copy", g.Judge(Ego, 1, much, much, other, screen, final: false).V.ToString(), "Wait");
            Check("...scrolled off by then (the read's oldest line newer): no copy", g.Judge(Ego, 1, much, much, other, new ItemRepeatGuard.Screen(1, t0.AddSeconds(30), read), final: false).V.ToString(), "Count");
        }

        // b. the replay of Steven's reads: no Wrathful Ego drop, the two set aside as bought, and so no quest points
        {
            var caseDir = FindCase("text-2026-11-13-market-ego-reread");
            if (caseDir == null) Console.WriteLine("  (the replay case is not beside this build: skipped)");
            else
            {
                var bank = TestTemp.Path("kl-notdrop-bank-" + Guid.NewGuid().ToString("N")[..8]);
                CopyDir(caseDir, Path.Combine(bank, Path.GetFileName(caseDir)));
                var r = ReplayBank.Run(bank, settings).Single();
                var got = JsonNode.Parse(r.Detail.Split(" | counted ").Last().Split("\n")[0]) as JsonObject;
                long drops = got?["items"]?[Ego]?.GetValue<long>() ?? 0, bought = got?["rewards"]?[Ego]?.GetValue<long>() ?? 0;
                Check("18:35:20 to 18:36:41 replayed: Wrathful Ego drops, bought", $"{r.Status} · drops {drops} · bought {bought}", "pass · drops 0 · bought 2");
                Check("...the quest points those drops give", QuestPoints(drops).ToString(), "0");
                Check("...(as 0.10.66 counted them, two drops gave points)", (QuestPoints(2) > 0).ToString(), "True");
                try { Directory.Delete(bank, true); } catch (Exception) { }
            }
        }

        // c. the engine: a pickup out and back, its zone's and its enemy's figures with it
        {
            var e = new SessionEngine(items); e.SetZone("Chaotic Lower Reshanta");
            // an enemy whose loot table has it (one without would leave the drop with no enemy)
            e.SetTarget(LootBook.Current.SourcesOf(Ego).FirstOrDefault() ?? "Abyss Klaw", t0);
            e.Apply(new ItemAcquired(t0, egoId, 1, false)); e.Apply(new ItemAcquired(t0.AddSeconds(1), egoId, 1, false));
            string Fig() => $"{e.Items.GetValueOrDefault(egoId)} · zone {e.Zones["Chaotic Lower Reshanta"].Items.GetValueOrDefault(egoId)} drops {e.Zones["Chaotic Lower Reshanta"].Drops} · enemy {e.MobSnapshot().Sum(m => m.Drops.GetValueOrDefault(egoId))}";
            var before = Fig();
            var tk = e.Unapply(new ItemAcquired(t0, egoId, 1, false), "Chaotic Lower Reshanta");
            Check("one of two out of the engine", Fig(), "1 · zone 1 drops 1 · enemy 1");
            e.Restore(tk!);
            Check("...and back (Undo)", Fig(), before);
            Check("...three out of a count that holds two: nothing taken", (e.Unapply(new ItemAcquired(t0, egoId, 3, false)) == null).ToString(), "True");
        }

        // d. the session: out of the 15-minute count, the views and Today, off the feed and the recent pickups; Undo puts it back
        {
            var s = new TrackerSession(null, settings, items, "Test") { Vote = false, DayView = new SessionEngine(items) };
            s.Engine.SetZone("Chaotic Lower Reshanta"); s.DayView.SetZone("Chaotic Lower Reshanta");
            var at = DateTime.UtcNow;
            s.CommitForTest(new ItemAcquired(at, egoId, 1, false) { Via = "chat" });
            var line = s.Live.Recent(FeedTab.Farming, 10).FirstOrDefault(l => l.ItemId == egoId);
            string State() => $"count {s.Engine.Items.GetValueOrDefault(egoId)} · today {s.DayView.Items.GetValueOrDefault(egoId)} · feed {s.Live.Recent(FeedTab.Farming, 10).Count(l => l.ItemId == egoId)} · recent {s.RecentPickups(10).Count(p => p.What.StartsWith(Ego))}";
            Check("a Wrathful Ego counted", State(), "count 1 · today 1 · feed 1 · recent 1");
            if (line != null)
            {
                var tk = s.NotADrop(new ItemAcquired(line.At, egoId, (int)line.Amount, line.Bound), line.Where, line);
                Check("Not a drop on its feed line: out of every count", State() + $" · {tk.Engines.Count} counts", $"count 0 · today 0 · feed 0 · recent 0 · {tk.Engines.Count} counts");
                Check("...Today's part kept for an Undo after a restart", (tk.Day != null).ToString(), "True");
                s.UndoNotADrop(tk);
                Check("...Undo: back everywhere", State(), "count 1 · today 1 · feed 1 · recent 1");
            }
        }

        // e. the quest steps it completed come out with their points, and Undo puts them back
        {
            var log = new QuestLog { AllTimePoints = 100 };
            QuestDone D(long target, int pts, string h = Horizon.Session) => new() { Key = Quests.KeyOf(QuestKind.Material, h, Ego, target), Kind = QuestKind.Material, Horizon = h, Subject = Ego, Target = target, Points = pts, DoneAtUtc = t0.AddSeconds(25), Scope = h == Horizon.Session ? "s:x" : "d:x" };
            log.Done.AddRange(new[] { D(1, 10), D(2, 20), D(1, 15, Horizon.Today) });
            log.AllTimePoints += 45;
            var one = Quests.Revoke(log, Ego, t0.AddSeconds(24), _ => 1);
            Check("one Ego out of two: the step for 2 goes", $"{string.Join(",", one.Select(d => d.Target))} · {log.AllTimePoints} pts", "2 · 125 pts");
            var two = Quests.Revoke(log, Ego, t0.AddSeconds(24), _ => 0);
            Check("...the other too: every step of it goes", $"{two.Count} · {log.AllTimePoints} pts · {log.Done.Count} left", "2 · 100 pts · 0 left");
            Quests.Restore(log, one.Concat(two));
            Check("...Undo: the steps and points are back", $"{log.Done.Count} · {log.AllTimePoints} pts", "3 · 145 pts");
            Check("...a step done before the pickup stays", Quests.Revoke(log, Ego, t0.AddSeconds(60), _ => 0).Count.ToString(), "0");
        }

        // f. not-drops.json keeps it across a restart, and an Undo takes it off
        {
            var dir = TestTemp.Path("kl-notdrop-" + Guid.NewGuid().ToString("N")[..8]);
            var kept = NotDrops.DirOverride;
            NotDrops.DirOverride = dir; NotDrops.Forget();
            try
            {
                var at = new DateTime(2026, 11, 13, 23, 36, 15, 368, DateTimeKind.Utc);
                NotDrops.Add(new NotDrop(at, "2026-11-13", egoId, Ego, 1, false, "Farming · Chaotic Lower Reshanta", new() { new QuestDone { Key = "k", Points = 10 } },
                    new SessionEngine.Taken(egoId, 1, false, "Chaotic Lower Reshanta", "Chaotic Lower Reshanta|Abyss Klaw", null, false, false, false, false)));
                NotDrops.Forget();
                var back = NotDrops.Find(at, egoId, 1);
                Check("not-drops.json read again (a restart)", $"{NotDrops.Has(at, egoId, 1)} · {back?.Quests?.Sum(q => q.Points)} pts · {back?.DayTaken?.Zone}", "True · 10 pts · Chaotic Lower Reshanta");
                NotDrops.Remove(at, egoId, 1); NotDrops.Forget();
                Check("...Undo takes it off for good", NotDrops.Has(at, egoId, 1).ToString(), "False");
            }
            finally { NotDrops.DirOverride = kept; NotDrops.Forget(); try { Directory.Delete(dir, true); } catch (Exception) { } }
        }
        return ok;

        // the material quest points that many Wrathful Ego give this session and today, as the window credits them
        long QuestPoints(long n)
        {
            var now = DateTime.UtcNow;
            var have = n > 0 ? new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase) { [Ego] = n } : new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
            var c = new QuestCounts(0, 3600, have, new Dictionary<string, int>(), 0);
            var mats = new List<QuestMaterial> { new(Ego, Quests.GradeTier("Unique"), 0) };
            var x = new QuestInputs(c, c, QuestCounts.None, mats, new[] { Ego }, new List<BestHere>(), now.AddHours(-1), now);
            return Quests.Advance(new QuestLog(), x, now.ToLocalTime()).Where(d => d.Subject == Ego).Sum(d => (long)d.Points);
        }
    }

    /// <summary>A replay case from the repo's tests/replays, looked for up from the build's folder (null when the build is elsewhere).</summary>
    static string? FindCase(string name)
    {
        for (var d = new DirectoryInfo(AppContext.BaseDirectory); d != null; d = d.Parent)
        {
            var p = Path.Combine(d.FullName, "tests", "replays", name);
            if (Directory.Exists(p)) return p;
            p = Path.Combine(d.FullName, "tracker", "tests", "replays", name);
            if (Directory.Exists(p)) return p;
        }
        return null;
    }

    static void CopyDir(string from, string to)
    {
        Directory.CreateDirectory(to);
        foreach (var f in Directory.GetFiles(from)) File.Copy(f, Path.Combine(to, Path.GetFileName(f)), true);
    }
}
