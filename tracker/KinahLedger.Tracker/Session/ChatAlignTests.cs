using KinahLedger.Data;
using KinahLedger.Ocr;

namespace KinahLedger.Session;

/// <summary>
/// parse-test's checks for the chat's alignment, the audit's kill arithmetic and two items the list lacked (2026-10-25,
/// branch tracker/chat-align). That day's reads.log had 484 "couldn't line this read up" lines: 69 were a game window over
/// the chat read as numbers ("MAX | 3 | MAX | 3", a storage window's counts), which then stood as the last read, and as the
/// anchor once read twice the same, so 35 more real reads after one lined up with nothing; 149 came after a fade, 29 after
/// a one-word scrap of a nameplate. The settle that follows such reads stayed open up to 42 s and took new lines from the
/// vote, dated when the garbling began. The replay case is tests/replays/text-2026-10-25-chat-panel-settle-1638; these
/// are the same rules on reads small enough to follow by hand. Writes only to parse-test's scratch log.
/// </summary>
public static class ChatAlignTests
{
    public static bool Run(ItemDictionary items, Settings settings)
    {
        bool ok = true;
        void Check(string what, string got, string expect)
        {
            bool pass = got == expect; ok &= pass;
            Console.WriteLine($"  {what,-62} -> {(got.Length > 60 ? got[..60] + "…" : got),-40} {(pass ? "ok" : "MISMATCH, expected " + expect)}");
        }
        Console.WriteLine("\nChat alignment: a window over the chat is not a read of it; the anchor is a read of the chat (2026-10-25):");
        var chat = new List<string> { "[Pa] hello there all", "[Pb] who wants a group", "Acquired 528 Kina.", "[Pc] selling stuff cheap", "Obtained 1 Sylphen Soul Points." };

        // a. a pet window read as numbers, three times, then a nameplate scrap, then the chat with one new line
        {
            var lt = new LineTracker();
            lt.NewLines(chat);
            var panel = new List<string> { "MAX", "3", "MAX", "3" };
            string got = string.Join(",", lt.NewLines(panel)) + "|" + string.Join(",", lt.NewLines(panel)) + "|" + string.Join(",", lt.NewLines(panel)) + "|" + string.Join(",", lt.NewLines(new List<string> { "mandu" }));
            Check("\"MAX | 3 | MAX | 3\" three times, then \"mandu\": nothing new", got, "|||");
            var back = lt.NewLines(chat.Skip(1).Append("Acquired 157 Kina.").ToList());
            Check("...then the chat with one more line: that line, nothing unaligned", $"{string.Join(",", back)} · panels {lt.Panels} · unaligned {lt.Unaligned}", "Acquired 157 Kina. · panels 3 · unaligned 0");
            Check("a storage window's counts are a panel too", $"{new LineTracker().Also(chat, new List<string> { "16", "21", "89", "70", "105", "10" })}", "panel");
            Check("a read of the chat with a number in it is not", $"{new LineTracker().Also(chat, new List<string> { "[Pb] who wants a group", "Acquired 528 Kina.", "[Pc] selling stuff cheap", "(3/20)" })}", "chat");
        }

        // b. the crafting window's list over the chat, read twice the same: it never becomes the anchor, so the chat lines
        // up on its last real read when it comes back (before: the second read made it the anchor and the line was lost)
        {
            var lt = new LineTracker();
            lt.NewLines(chat);
            var craft = new List<string> { "Can craft", "Orichalcum Mace", "Can craft", "Orichalcum Guard", "Can craft" };
            lt.NewLines(craft); lt.NewLines(craft);
            var back = lt.NewLines(chat.Skip(2).Append("Acquired 99 Kina.").ToList());
            Check("a crafting list read twice, then the chat: the new line counts", $"{string.Join(",", back)} · from the anchor {lt.LastFromAnchor} · unaligned {lt.Unaligned}", "Acquired 99 Kina. · from the anchor True · unaligned 1");
        }

        // c. the audit counts kills across the 15-minute chunks: a chunk's end is never a negative minute
        Console.WriteLine("\nThe audit's kills across a chunk's end (2026-10-25 14:56: \"kills counted +-234\"):");
        {
            string[] AuditLines() { try { return File.ReadAllLines(CrashLog.ReadsPath).Where(l => l.Contains("  ??  audit: ")).ToArray(); } catch (Exception) { return Array.Empty<string>(); } }
            var s = new TrackerSession(null, settings, items, "Test") { Vote = false };
            var a0 = DateTime.UtcNow;
            int before = AuditLines().Length;
            s.Audit(a0);
            for (int i = 0; i < 5; i++) s.Engine.Apply(new KinahChanged(a0.AddSeconds(1 + i), 300 + i, false));
            s.NotePass(900);
            s.Audit(a0.AddSeconds(61));
            for (int i = 0; i < 3; i++) s.Engine.Apply(new KinahChanged(a0.AddSeconds(70 + i), 400 + i, false));
            var finished = s.Rotate();   // the chunk ends: its 8 kills go with it, the fresh engine starts at 0
            for (int i = 0; i < 2; i++) s.Engine.Apply(new KinahChanged(a0.AddSeconds(80 + i), 500 + i, false));
            s.NotePass(900);
            s.Audit(a0.AddSeconds(122));
            var got = AuditLines().Skip(before).Select(l => System.Text.RegularExpressions.Regex.Match(l, @"kills counted \+(-?\d+)").Groups[1].Value).ToList();
            Check("5 kills, then 3 more, the chunk's end, 2 more: +5, then +5", string.Join(" ", got), "5 5");
            Check("...the finished chunk kept its 8, the session counts 10", $"{finished.Totals().Kills} {s.KillsSoFar}", "8 10");
        }

        // d. two items the list lacked (data/extra-items.json)
        Console.WriteLine("\nItems the list lacked (2026-10-25: Candy Key Fragment (Event), Balaur's Essence):");
        {
            var p = new ChatParser(items);
            string P(string line) { var evs = p.Parse(line, DateTime.UtcNow).ToList(); return evs.Count == 0 ? "(nothing)" : string.Join("; ", evs.Select(e => e is ItemAcquired a ? $"{items.NameOf(a.ItemId)} x{a.Count}" : e.GetType().Name)); }
            Check("\"Acquired Candy Key Fragment (Event) x6.\"", P("Acquired Candy Key Fragment (Event) x6."), "Candy Key Fragment (Event) x6");
            Check("\"Acquired Balaur's Essence x70.\"", P("Acquired Balaur's Essence x70."), "Balaur's Essence x70");
            Check("\"Acquired Balaurls Essence x70.\" (the apostrophe read as l)", P("Acquired Balaurls Essence x70."), "Balaur's Essence x70");
            Check("Balaur's Essence: an Epic crafting material", items.Get(items.IdOf("Balaur's Essence")) is { } bi ? $"{bi.Grade} {bi.Category}" : "missing", "Epic Crafting Material");
        }
        return ok;
    }

    /// <summary>For the checks above: whether <paramref name="read"/>, after <paramref name="first"/>, was taken for a panel.</summary>
    static string Also(this LineTracker lt, List<string> first, List<string> read) { lt.NewLines(first); lt.NewLines(read); return lt.LastPanel ? "panel" : "chat"; }
}
