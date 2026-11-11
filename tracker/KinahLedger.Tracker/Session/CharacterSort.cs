using System.Drawing;
using System.Globalization;
using System.Text.Json;
using KinahLedger.Ocr;

namespace KinahLedger.Session;

/// <summary>
/// The old runs sorted by character, once, when the first character is seen (Steven, 2026-11-11: "sort where possible").
/// The pictures kept of each run (reward-shots: in the run, its reward table, its event rewards) mostly show the
/// medallion with the class icon; the pictures are grouped by icon, each group with three or more becomes a character
/// (the one in the world now keeps its own), and a run whose pictures say another character moves to that character's
/// runs. A run with no picture, or none showing the icon, stays with the first character, as do the days, the bests,
/// goals and quest points kept before (they hold no picture to tell by).
/// </summary>
public static class CharacterSort
{
    public sealed record Shot(DateTime Local, string Print, string File);

    /// <summary>"20261111-164637-Auldors-Sanctum-in-run.jpg": when the picture was taken, local time.</summary>
    public static DateTime? TimeOf(string file)
    {
        var n = Path.GetFileName(file);
        return n.Length >= 15 && DateTime.TryParseExact(n[..15], "yyyyMMdd-HHmmss", CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var t) ? t : null;
    }

    /// <summary>The icon in each picture of a folder that shows one (slow: every picture is opened; run it off the window's thread).</summary>
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
                var band = CharacterBadge.Band(new Rectangle(0, 0, full.Width, full.Height));
                using var b = full.Clone(band, full.PixelFormat);
                if (CharacterBadge.Read(b, full.Height) is { } r) shots.Add(new Shot(t, r.Print, f));
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

    /// <summary>What the sort did, for the log and the window.</summary>
    public sealed record Outcome(int Pictures, int Groups, int Moved, List<(Character Who, int Runs)> To);

    /// <summary>
    /// Sorts <paramref name="first"/>'s runs (its instances.jsonl) by the pictures' icons. Groups of three pictures or more
    /// that are not <paramref name="first"/>'s icon become characters of their own on <paramref name="server"/> (or the
    /// one that already has that icon). <paramref name="rewrite"/> runs the file change (the window passes the runs log's
    /// <see cref="InstanceLog.Reload"/>, so nothing is written while it changes); null writes directly (self-tests).
    /// </summary>
    public static Outcome Sort(Character first, string server, IReadOnlyList<Shot> shots, Action<Action>? rewrite = null)
    {
        var groups = Group(shots).Where(g => g.Count >= 3).ToList();
        var owners = new List<Character?>();
        foreach (var g in groups)
        {
            var prints = g.Select(s => s.Print).ToList();
            if (prints.Any(p => Characters.Alike(first, p) >= CharacterBadge.SameIcon)) { owners.Add(first); continue; }
            var known = Characters.All.Where(c => c.Id != first.Id).FirstOrDefault(c => prints.Any(p => Characters.Alike(c, p) >= CharacterBadge.SameIcon));
            if (known == null)
            {
                known = Characters.Add(prints[0], server);
                foreach (var p in prints.Skip(1)) Characters.Learn(known, p);
                known.FirstSeen = g[0].Local.ToUniversalTime();
                CrashLog.Note($"characters: {Characters.Label(known)} found in {g.Count} old pictures ({g[0].Local:MMM d} to {g[^1].Local:MMM d})");
            }
            owners.Add(known);
        }
        var moved = new Dictionary<string, (Character Who, List<string> Lines)>();
        int movedCount = 0;
        void Change()
        {
            var path = Path.Combine(Characters.DirOf(first), "instances.jsonl");
            if (!File.Exists(path)) return;
            var keep = new List<string>();
            foreach (var line in File.ReadAllLines(path))
            {
                InstanceRun? r = null;
                try { if (line.Length > 2) r = JsonSerializer.Deserialize<InstanceRun>(line); } catch (Exception) { }
                int gi = r == null ? -1 : GroupOf(r, groups);
                var who = gi >= 0 ? owners[gi] : null;
                if (who == null || who.Id == first.Id) { keep.Add(line); continue; }
                if (!moved.TryGetValue(who.Id, out var m)) moved[who.Id] = m = (who, new List<string>());
                m.Lines.Add(line); movedCount++;
            }
            if (movedCount == 0) return;
            foreach (var (who, lines) in moved.Values)
            {
                var dir = Characters.DirOf(who); Directory.CreateDirectory(dir);
                File.AppendAllLines(Path.Combine(dir, "instances.jsonl"), lines);
            }
            var tmp = path + ".tmp";
            File.WriteAllLines(tmp, keep);
            File.Move(tmp, path, overwrite: true);
        }
        if (rewrite != null) rewrite(Change); else Change();
        var to = moved.Values.Select(m => (m.Who, m.Lines.Count)).ToList();
        CrashLog.Note($"characters: old runs sorted by the class icon in {shots.Count} pictures, {groups.Count} character(s): "
            + (movedCount == 0 ? "every run stays with " + Characters.Label(first) : string.Join(", ", to.Select(x => $"{x.Count} run(s) to {Characters.Label(x.Who)}"))));
        Characters.MarkSorted();
        return new Outcome(shots.Count, groups.Count, movedCount, to);
    }
}
