using System.Drawing;
using System.Globalization;
using System.Text.Json;
using KinahLedger.Ocr;

namespace KinahLedger.Session;

/// <summary>
/// The history kept before characters, put with the characters it belongs to, once (Steven, 2026-11-11: "sort where
/// possible"). The pictures kept of each run (reward-shots) mostly show the medallion's class icon, and the in-run ones
/// the nameplate over the character's head. The pictures are grouped by icon; each group of three or more is a
/// character (one already known by that icon, or a new one), named by the names read in its pictures. Then:
/// <list type="bullet">
/// <item>a run whose pictures show a character's icon goes to that character;</item>
/// <item>the character with the most pictures is the main one: the runs with no picture, and the days, bests, goals,
/// quest points and today kept before, go to it. 0.10.51 gave them to the first character seen, which on Steven's PC
/// was an alt (the harp, Swoles), so a 0.10.51 install runs this pass once more and moves them on.</item>
/// </list>
/// </summary>
public static class CharacterSort
{
    public sealed record Shot(DateTime Local, string Print, string File, string? Name = null);

    /// <summary>"20261111-164637-Auldors-Sanctum-in-run.jpg": when the picture was taken, local time.</summary>
    public static DateTime? TimeOf(string file)
    {
        var n = Path.GetFileName(file);
        return n.Length >= 15 && DateTime.TryParseExact(n[..15], "yyyyMMdd-HHmmss", CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var t) ? t : null;
    }

    /// <summary>The icon (and the name over the head, when it reads) in each picture of a folder (slow: every picture is opened; run it off the window's thread).</summary>
    public static List<Shot> ReadShots(string dir, CancellationToken ct = default)
    {
        var shots = new List<Shot>();
        if (!Directory.Exists(dir)) return shots;
        foreach (var f in Directory.EnumerateFiles(dir, "*.jpg").Concat(Directory.EnumerateFiles(dir, "*.png")).OrderBy(x => x, StringComparer.Ordinal))
        {
            if (ct.IsCancellationRequested) break;
            if (TimeOf(f) is not { } t) continue;
            try
            {
                using var full = new Bitmap(f);
                var frame = new Rectangle(0, 0, full.Width, full.Height);
                using var b = full.Clone(CharacterBadge.Band(frame), full.PixelFormat);
                if (CharacterBadge.Read(b, full.Height) is not { } r) continue;
                string? name = null;
                try
                {
                    using var mid = full.Clone(CharacterName.Region(frame), full.PixelFormat);
                    name = CharacterName.ReadAsync(mid, full.Width, full.Height).GetAwaiter().GetResult();
                }
                catch (Exception) { /* the icon still counts */ }
                shots.Add(new Shot(t, r.Print, f, name));
            }
            catch (Exception) { /* a damaged picture tells nothing */ }
        }
        return shots;
    }

    /// <summary>The pictures grouped by icon: each group keeps up to five fingerprints to compare against (the same icon under other light).</summary>
    public static List<List<Shot>> Group(IEnumerable<Shot> shots)
    {
        var groups = new List<(List<string> Reps, List<Shot> Shots)>();
        foreach (var s in shots)
        {
            var best = groups.Select(g => (g, a: g.Reps.Max(r => CharacterBadge.Alike(r, s.Print)))).OrderByDescending(x => x.a).FirstOrDefault();
            if (best.g.Shots != null && best.a >= CharacterBadge.SameIcon)
            {
                best.g.Shots.Add(s);
                if (best.g.Reps.Count < 5 && best.a < 0.9) best.g.Reps.Add(s.Print);
            }
            else groups.Add((new List<string> { s.Print }, new List<Shot> { s }));
        }
        // two groups of one icon (its two lights met in the wrong order): joined when any of their fingerprints agree
        for (int i = 0; i < groups.Count; i++)
            for (int j = groups.Count - 1; j > i; j--)
                if (groups[i].Reps.Any(a => groups[j].Reps.Any(b => CharacterBadge.Alike(a, b) >= CharacterBadge.SameIcon)))
                {
                    groups[i].Shots.AddRange(groups[j].Shots);
                    foreach (var r in groups[j].Reps) if (groups[i].Reps.Count < 6) groups[i].Reps.Add(r);
                    groups.RemoveAt(j);
                }
        return groups.Select(g => g.Shots.OrderBy(s => s.Local).ToList()).OrderByDescending(g => g.Count).ToList();
    }

    /// <summary>The group a run's pictures belong to: those taken from a minute before it began to three after its last loot, by majority; -1 with none.</summary>
    public static int GroupOf(InstanceRun r, IReadOnlyList<List<Shot>> groups)
    {
        var from = r.Start.ToLocalTime().AddMinutes(-1);
        var to = (r.LastLoot ?? r.Start).ToLocalTime().AddMinutes(3);
        var votes = groups.Select((g, i) => (i, n: g.Count(s => s.Local >= from && s.Local <= to))).Where(x => x.n > 0).OrderByDescending(x => x.n).ToList();
        return votes.Count == 0 ? -1 : votes[0].i;
    }

    /// <summary>What the pass did, for the log and the window.</summary>
    public sealed record Outcome(int Pictures, int Groups, int Moved, List<(Character Who, int Runs)> To, Character? Main, bool HistoryMoved, List<(Character Who, string Name)> Named);

    /// <summary>
    /// Puts the history <paramref name="adopter"/> took over with the characters it belongs to (see the class). Groups of
    /// three pictures or more become characters on <paramref name="server"/> when no known one has their icon.
    /// <paramref name="rewrite"/> runs the file changes (the window passes the runs log's <see cref="InstanceLog.Reload"/>,
    /// so nothing is written while they change); null changes them directly (self-tests, the dry run).
    /// </summary>
    public static Outcome Place(Character adopter, string server, IReadOnlyList<Shot> shots, Action<Action>? rewrite = null)
    {
        var groups = Group(shots).Where(g => g.Count >= 3).ToList();
        var owners = new List<Character>();
        foreach (var g in groups)
        {
            var prints = g.Select(s => s.Print).ToList();
            var known = Characters.All
                .Select(c => (c, a: prints.Max(p => Characters.Alike(c, p))))
                .Where(x => x.a >= CharacterBadge.SameIcon).OrderByDescending(x => x.a).Select(x => x.c).FirstOrDefault();
            if (known == null)
            {
                known = Characters.Add(prints[0], server);
                CrashLog.Note($"characters: {Characters.Label(known)} found in {g.Count} old pictures ({g[0].Local:MMM d} to {g[^1].Local:MMM d})");
            }
            foreach (var p in prints) Characters.Learn(known, p);
            owners.Add(known);
        }
        // the main character: the most pictures; with none, the history stays where it is
        var main = owners.Count > 0 ? owners[0] : adopter;
        var named = new List<(Character, string)>();
        for (int i = 0; i < groups.Count; i++)
            if (Characters.Settle(groups[i].Where(s => s.Name != null).Select(s => s.Name!), 2, 1.5) is { } name && Characters.SetReadName(owners[i], name))
                named.Add((owners[i], name));
        var moved = new Dictionary<string, (Character Who, List<string> Lines)>();
        int movedCount = 0; bool historyMoved = false;
        void Change()
        {
            var from = Characters.DirOf(adopter);
            var path = Path.Combine(from, "instances.jsonl");
            if (File.Exists(path))
            {
                var keep = new List<string>();
                foreach (var line in File.ReadAllLines(path))
                {
                    InstanceRun? r = null;
                    try { if (line.Length > 2) r = JsonSerializer.Deserialize<InstanceRun>(line); } catch (Exception) { }
                    int gi = r == null ? -1 : GroupOf(r, groups);
                    // no picture: kept from before characters, the main one's; counted since the adopter was first seen, its own
                    var who = gi >= 0 ? owners[gi] : r != null && r.Start >= adopter.FirstSeen ? adopter : main;
                    if (who.Id == adopter.Id) { keep.Add(line); continue; }
                    if (!moved.TryGetValue(who.Id, out var m)) moved[who.Id] = m = (who, new List<string>());
                    m.Lines.Add(line); movedCount++;
                }
                if (movedCount > 0)
                {
                    foreach (var (who, lines) in moved.Values)
                    {
                        var dir = Characters.DirOf(who); Directory.CreateDirectory(dir);
                        File.AppendAllLines(Path.Combine(dir, "instances.jsonl"), lines);
                    }
                    var tmp = path + ".tmp";
                    File.WriteAllLines(tmp, keep);
                    File.Move(tmp, path, overwrite: true);
                }
            }
            if (main.Id == adopter.Id) return;
            // the rest of the history the adopter took over: to the main character, joined with what it counted since (CharacterMerge)
            var to = Characters.DirOf(main); Directory.CreateDirectory(to);
            foreach (var f in Characters.Files.Where(f => f != "instances.jsonl"))
            {
                var src = Path.Combine(from, f);
                if (!File.Exists(src)) continue;
                CharacterMerge.Into(src, Path.Combine(to, f), adopter.Id);
                historyMoved = true;
            }
        }
        if (rewrite != null) rewrite(Change); else Change();
        var toList = moved.Values.Select(m => (m.Who, m.Lines.Count)).ToList();
        CrashLog.Note($"characters: kept history placed by the class icon in {shots.Count} pictures, {groups.Count} character(s); "
            + (movedCount == 0 ? "no run moved" : string.Join(", ", toList.Select(x => $"{x.Count} run(s) to {Characters.Label(x.Who)}")))
            + (historyMoved ? $"; days, bests, goals, quest points and today to {Characters.Label(main)}, the character with the most pictures" : ""));
        Characters.MarkSorted();
        return new Outcome(shots.Count, groups.Count, movedCount, toList, main, historyMoved, named);
    }
}
