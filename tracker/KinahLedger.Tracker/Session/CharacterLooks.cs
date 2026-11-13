using System.Text.Json;
using KinahLedger.Data;

namespace KinahLedger.Session;

/// <summary>
/// One character's last seven days and best hour, as the header's character switcher shows them (Ui/MainWindow.Characters.cs):
/// the farming each day made, the runs and their Kinah, and the best hour on this server. The days are the Week side's days
/// (<see cref="DayHistory.Days"/>), so a character's figure here is the one its own Week side shows.
/// </summary>
public sealed record CharacterLook(List<HistoryDay> Days, BestRuns.Best? Best, string BestZone)
{
    /// <summary>The seven days' farming made (after the Market's cut), its time, and the runs: how many and the counted ones' Kinah.</summary>
    public long Farmed => Kept.Sum(d => d.Farm.Made);
    public double FarmSeconds => Kept.Sum(d => d.Farm.Active);
    public int Runs => Kept.Sum(d => d.Runs.Runs);
    public long RunKinah => Kept.Sum(d => d.Runs.Kinah);
    /// <summary>A day's farming and its runs' Kinah together, for the day's bar.</summary>
    public static long DayMade(HistoryDay d) => d.Farm.Made + d.Runs.Kinah;
    /// <summary>Anything to show: a day with farming or runs, or a best.</summary>
    public bool Any => Farmed > 0 || Runs > 0 || Best != null;

    IEnumerable<HistoryDay> Kept => Days.Where(d => !d.BeforeHistory);
}

/// <summary>
/// Reads a character's numbers from its own folder (characters\cN) without touching them: nothing here writes, and the window's
/// loaded state (the counted character's today, runs, goals) is never swapped. That is why the switcher can show every
/// character while the tracker keeps counting for the one in the game.
/// </summary>
public static class CharacterLooks
{
    /// <summary>The runs a folder's instances.jsonl keeps, read as the Events page reads them (the last 2,000; areas left alone out; old records marked).</summary>
    public static List<InstanceRun> ReadRuns(string dir)
    {
        var list = new List<InstanceRun>();
        var path = Path.Combine(dir, "instances.jsonl");
        try
        {
            if (!File.Exists(path)) return list;
            foreach (var line in File.ReadLines(path).TakeLast(2000))
            {
                if (line.Length <= 2) continue;
                InstanceRun? r = null;
                try { r = JsonSerializer.Deserialize<InstanceRun>(line); } catch (JsonException) { /* one bad line doesn't stop the rest */ }
                if (r?.LastLoot == null || InstanceLog.LeftAlone(r)) continue;
                r.FileUnderFamily();
                list.Add(r);
            }
            InstanceLog.MarkOld(list);
        }
        catch (Exception ex) { CrashLog.Write("character look runs", ex); }
        return list;
    }

    /// <summary>The best hour on <paramref name="server"/> in a folder's best.json, and its zone; null when there is none.</summary>
    public static (BestRuns.Best Best, string Zone)? BestIn(string dir, string server)
    {
        var path = Path.Combine(dir, "best.json");
        try
        {
            if (!File.Exists(path)) return null;
            var all = JsonSerializer.Deserialize<Dictionary<string, BestRuns.Best>>(File.ReadAllText(path));
            if (all == null) return null;
            (BestRuns.Best Best, string Zone)? top = null;
            foreach (var (key, b) in all)
            {
                int bar = key.IndexOf('|');
                if (bar < 0) continue;
                var (srv, zone) = (key[..bar].Trim(), key[(bar + 1)..].Trim());
                if (server.Trim().Length > 0 && !string.Equals(srv, server.Trim(), StringComparison.OrdinalIgnoreCase)) continue;
                if (top == null || b.PerHr > top.Value.Best.PerHr) top = (b, zone);
            }
            return top;
        }
        catch (Exception ex) { CrashLog.Write("character look best", ex); return null; }
    }

    /// <summary>A character's seven days and best from its folder (<paramref name="dir"/>), read only.</summary>
    public static CharacterLook Read(string dir, string server, ItemDictionary items, DateTime? nowLocal = null)
    {
        // days.json read here, not through DayHistory.Load, which keeps a broken file aside (a write)
        DayFile file;
        try { var p = Path.Combine(dir, "days.json"); file = File.Exists(p) ? JsonSerializer.Deserialize<DayFile>(File.ReadAllText(p)) ?? new() : new(); }
        catch (Exception) { file = new(); }
        var days = DayHistory.Days(7, null, items, ReadRuns(dir), file, nowLocal);
        var best = BestIn(dir, server);
        return new CharacterLook(days, best?.Best, best?.Zone ?? "");
    }
}
