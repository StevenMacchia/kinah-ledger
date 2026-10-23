using System.Text;
using KinahLedger.Data;

namespace KinahLedger.Session;

/// <summary>
/// Synthetic stress test (Steven, 2026-10-23: "only synthetic test if its quick"). A scripted fight, with its true counts
/// known, shown through a chat box the way the game shows it: ten lines, a read about once a second, and the
/// recognizer's mistakes (letters swapped, a line lost, the chat fading out, a read gone to noise). The tracker counts
/// it as it would live, and the score is how far its counts are from the truth: missed and phantom Kinah, items, kills.
/// Text only, no pictures, so thousands of reads run in a second or two.
/// </summary>
public static class SyntheticChat
{
    public sealed record Score(int Seeds, long KinahTrue, long KinahCounted, int ItemsTrue, int ItemsCounted, int Phantom, int Missed, int KillsTrue, int KillsCounted)
    {
        public double KinahError => KinahTrue == 0 ? 0 : Math.Abs(KinahCounted - KinahTrue) / (double)KinahTrue;
        public double KillError => KillsTrue == 0 ? 0 : Math.Abs(KillsCounted - KillsTrue) / (double)KillsTrue;
        public override string ToString() =>
            $"{Seeds} fights: Kinah {KinahCounted:N0} of {KinahTrue:N0} ({KinahError:P1} off) · items {ItemsCounted} of {ItemsTrue} ({Missed} missed, {Phantom} phantom) · kills {KillsCounted} of {KillsTrue} ({KillError:P1} off)";
    }

    // drops every enemy here can give (all in the built-in item list)
    static readonly string[] Drops = { "Spiritstone", "Riverside Helm" };
    const string Pet = "Varian";

    /// <summary>Runs <paramref name="seeds"/> fights with the given noise and sums the score.</summary>
    public static Score Run(Settings settings, ItemDictionary items, int seeds = 12, double noise = 1.0, bool quest = true)
    {
        long kt = 0, kc = 0; int it = 0, ic = 0, ph = 0, mi = 0, lt = 0, lc = 0;
        for (int seed = 1; seed <= seeds; seed++)
        {
            var (truth, reads) = Fight(new Random(seed * 7919), noise, quest);
            var s = new TrackerSession(null, settings, items, "Synthetic");
            foreach (var (t, lines) in reads) { s.QuestCounter(lines, t); s.ProcessChat(lines, t); }
            s.FlushVotes(reads[^1].T.AddSeconds(2));
            var tot = s.Engine.Totals();
            kt += truth.Kinah; kc += tot.Kinah;
            lt += truth.Kills; lc += tot.Kills;
            foreach (var d in Drops)
            {
                int want = truth.Items.GetValueOrDefault(d), got = s.Engine.Items.Where(kv => items.NameOf(kv.Key) == d).Sum(kv => kv.Value);
                it += want; ic += got;
                if (got > want) ph += got - want; else mi += want - got;
            }
            // anything counted that the fight never dropped is a phantom too
            ph += s.Engine.Items.Where(kv => !Drops.Contains(items.NameOf(kv.Key))).Sum(kv => kv.Value);
        }
        return new Score(seeds, kt, kc, it, ic, ph, mi, lt, lc);
    }

    /// <summary>One fight in detail, for finding what the score counts: Kinah missed and extra, and the kill counters.</summary>
    public static string Diagnose(Settings settings, ItemDictionary items, int seed, double noise, bool quest)
    {
        var (truth, reads) = Fight(new Random(seed * 7919), noise, quest);
        try { File.Delete(CrashLog.ReadsPath); } catch (Exception) { }
        var s = new TrackerSession(null, settings, items, "Synthetic");
        foreach (var (t, lines) in reads) { s.QuestCounter(lines, t); s.ProcessChat(lines, t); }
        s.FlushVotes(reads[^1].T.AddSeconds(2));
        var e = s.Engine;
        var counted = s.RecentPickups(100000).Select(p => p.What).Where(w => w.EndsWith(" Kinah")).Select(w => long.Parse(w[..^6].Replace(",", ""))).ToList();
        var want = truth.KinahLines.ToList();
        var missing = new List<long>(want); foreach (var c in counted) missing.Remove(c);
        var extra = new List<long>(counted); foreach (var w in want) extra.Remove(w);
        // the tracker's own notes around each missed line, to see which step lost it
        var log = File.Exists(CrashLog.ReadsPath) ? File.ReadAllLines(CrashLog.ReadsPath) : Array.Empty<string>();
        var around = new List<string>();
        foreach (var m in missing.Concat(extra).Distinct().Take(3))
        {
            int at = Array.FindIndex(log, l => l.Contains($"{m} Kina"));
            if (at >= 0) around.Add($"\n    [{m}] " + string.Join("\n         ", log.Skip(Math.Max(0, at - 6)).Take(10)));
        }
        // the line matcher alone over the same reads: what it called new around each miss
        var lt = new KinahLedger.Ocr.LineTracker();
        var trail = new List<string>();
        foreach (var m in missing.Distinct().Take(2))
        {
            int first = reads.FindIndex(x => x.Lines.Any(l => l.Contains($" {m} Kina")));
            if (first < 0) continue;
            lt = new KinahLedger.Ocr.LineTracker();
            for (int i = 0; i < reads.Count; i++)
            {
                var fresh = lt.NewLines(reads[i].Lines);
                if (i >= first - 2 && i <= first + 2)
                    trail.Add($"
    [{m}] read {i}: {string.Join(" | ", reads[i].Lines.Select(l => l.Length > 22 ? l[..22] : l))}
           new: {string.Join(" | ", fresh)}");
            }
        }
        around.AddRange(trail);
        return $"seed {seed} noise {noise} quest {quest}: kills true {truth.Kills}, counted {e.Totals().Kills} (seen {e.KillsSeen}, Kinah drops {e.KinahDrops}, currency lines {e.CurrencyLines}, defeated {e.KillsDefeated}, xp {e.KillsXp}) · Kinah lines missed [{string.Join(", ", missing)}] extra [{string.Join(", ", extra)}]" + string.Concat(around);
    }

    sealed class Truth { public long Kinah; public int Kills; public Dictionary<string, int> Items = new(); public List<long> KinahLines = new(); }

    /// <summary>One fight: about 40 kills over four minutes, player chat between them, a kill quest's counter.</summary>
    static (Truth, List<(DateTime T, List<string> Lines)>) Fight(Random r, double noise, bool withQuest = true)
    {
        var truth = new Truth();
        var t0 = new DateTime(2026, 10, 23, 18, 0, 0, DateTimeKind.Utc);
        // what prints, and when
        var printed = new List<(double At, string Line)>();
        double now = 5;
        int quest = 0;
        for (int k = 0; k < 40; k++)
        {
            now += 2 + r.NextDouble() * 6;
            truth.Kills++;
            long kina = r.Next(20, 600);
            truth.Kinah += kina; truth.KinahLines.Add(kina);
            var kill = new List<string> { $"Acquired {kina} Kina." };
            if (r.NextDouble() < 0.5) kill.Add($"Obtained 1 {Pet} Soul Crystals.");
            if (r.NextDouble() < 0.25)
            {
                var d = Drops[r.Next(Drops.Length)];
                int n = d == "Spiritstone" && r.NextDouble() < 0.4 ? r.Next(2, 6) : 1;
                kill.Insert(r.Next(kill.Count + 1), n == 1 ? $"{d} acquired." : $"Acquired {d} x{n}.");
                truth.Items[d] = truth.Items.GetValueOrDefault(d) + n;
            }
            foreach (var l in kill) printed.Add((now + r.NextDouble() * 0.4, l));
            // the quest's two lines print together, in order, just after the kill's
            if (withQuest && quest < 30) { quest++; printed.Add((now + 0.45, "Defeat Kalgolem")); printed.Add((now + 0.451, $"and Papis ({quest}/30)")); }
            // players talk between kills
            for (int c = r.Next(0, 3); c > 0; c--) printed.Add((now + 0.5 + r.NextDouble() * 2, $"[Player{r.Next(1, 9)}] {Words(r)}"));
        }
        printed.Sort((a, b) => a.At.CompareTo(b.At));
        // reads of the ten-line box, with the recognizer's mistakes
        var reads = new List<(DateTime, List<string>)>();
        double end = printed[^1].At + 6, at = 3;
        while (at < end)
        {
            var shown = printed.Where(p => p.At <= at).Select(p => p.Line).TakeLast(10).ToList();
            double roll = r.NextDouble();
            List<string> read;
            if (roll < 0.02 * noise) { read = new List<string>(); }                         // the chat faded
            else if (roll < 0.035 * noise) read = shown.Select(l => Garble(r, l, 0.35)).ToList();         // a read gone to noise
            else
            {
                read = new List<string>();
                foreach (var l in shown)
                {
                    if (r.NextDouble() < 0.02 * noise) continue;                                            // a line lost
                    read.Add(r.NextDouble() < 0.12 * noise ? Garble(r, l, 0.06) : l);                       // a letter or two off
                }
            }
            reads.Add((t0.AddSeconds(at), read));
            at += 0.7 + r.NextDouble() * 0.6;
        }
        return (truth, reads);
    }

    static readonly string[] Vocab = { "lfg", "boss", "run", "anyone", "selling", "wts", "pst", "lol", "where", "is", "the", "camp", "need", "heal", "tank", "spot" };
    static string Words(Random r) => string.Join(" ", Enumerable.Range(0, r.Next(2, 6)).Select(_ => Vocab[r.Next(Vocab.Length)]));

    // the recognizer's usual slips on this font
    static readonly (char A, char B)[] Slips = { ('i', 'l'), ('l', 'I'), ('o', '0'), ('e', 'c'), ('u', 'ü'), ('r', 'n'), ('a', 'å'), ('c', 'e'), ('n', 'h'), ('.', ',') };

    static string Garble(Random r, string line, double rate)
    {
        var sb = new StringBuilder();
        foreach (var ch in line)
        {
            if (char.IsDigit(ch) || r.NextDouble() >= rate) { sb.Append(ch); continue; }   // digits stay: the counter checks those
            var slip = Slips.FirstOrDefault(s => s.A == char.ToLowerInvariant(ch));
            if (slip != default) sb.Append(slip.B);
            else if (r.NextDouble() < 0.3) continue;                                         // a letter dropped
            else sb.Append(ch);
        }
        if (r.NextDouble() < rate) sb.Append(r.NextDouble() < 0.5 ? "." : "'");                // a stray mark at the end
        return sb.ToString();
    }
}
