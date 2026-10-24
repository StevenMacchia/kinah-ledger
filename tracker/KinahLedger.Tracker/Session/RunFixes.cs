using System.Text.Json;

namespace KinahLedger.Session;

/// <summary>
/// The fix sheet under a flagged run on the Events tab (0.8.30): "Looks like 5 runs. Split at the reward screens · Keep as
/// one · Remove". Split needs to know where the reward screens fell inside the record, which a stored run doesn't keep
/// (it has the count of its tables and the first one's time). So from 0.8.30 every reward screen line the live feed
/// counts into a run is also written to instances.screens.jsonl (the feed's Sink), and Split cuts a record at the screens
/// found inside it with the live rule (<see cref="InstanceLog.TableStarting"/>: a table a minute or more after the last
/// one is the next run's). A record from before that has no screens kept and can't be split: the sheet says so.
/// Keep as one is remembered in instances.kept.txt, so the flag stays off after a restart.
/// </summary>
public static class RunFixes
{
    /// <summary>TableStarting's rule: a reward table this long after the run's last one belongs to the next run.</summary>
    public const double NextTableSeconds = 60;
    /// <summary>A screen this long after the record's last loot still counts as inside it (InstanceLog.RewardGraceSeconds).</summary>
    public const double GraceSeconds = InstanceLog.RewardGraceSeconds;

    static string Dir => System.IO.Path.GetDirectoryName(CrashLog.ReadsPath)!;
    public static string ScreensPath => System.IO.Path.Combine(Dir, "instances.screens.jsonl");
    static string KeptPath => System.IO.Path.Combine(Dir, "instances.kept.txt");

    /// <summary>One reward screen line as kept on disk: when the table was read, where it landed, and what it held.</summary>
    public sealed record ScreenLine(DateTime At, string Where, string Kind, long Id, string Name, long Amount);

    /// <summary>One reward screen: when it was read, its Kinah and its items.</summary>
    public sealed record Screen(DateTime At, long Kinah, Dictionary<long, int> Items);

    static readonly object FileLock = new();

    /// <summary>The live feed's Sink: a reward screen line counted into a run is kept for a later split. Nothing else is written.</summary>
    public static void Record(FeedLine l)
    {
        if (l.Tab != FeedTab.Events || !l.Via.Contains("reward table")) return;
        var json = JsonSerializer.Serialize(new { t = l.At.ToUniversalTime().ToString("O"), w = l.Where, k = l.Kind, id = l.ItemId, n = l.Name, a = l.Amount });
        lock (FileLock) { try { File.AppendAllText(ScreensPath, json + "\n"); } catch (Exception) { /* a read-only profile: no split for this run */ } }
    }

    /// <summary>The kept screen lines, oldest first (the last 20,000 at most).</summary>
    public static List<ScreenLine> Load(string? path = null)
    {
        var list = new List<ScreenLine>();
        try
        {
            var p = path ?? ScreensPath;
            if (!File.Exists(p)) return list;
            foreach (var line in File.ReadLines(p).TakeLast(20_000))
            {
                try
                {
                    using var d = JsonDocument.Parse(line);
                    var r = d.RootElement;
                    list.Add(new ScreenLine(DateTime.Parse(r.GetProperty("t").GetString()!, null, System.Globalization.DateTimeStyles.RoundtripKind).ToUniversalTime(),
                        r.GetProperty("w").GetString() ?? "", r.GetProperty("k").GetString() ?? "", r.GetProperty("id").GetInt64(), r.GetProperty("n").GetString() ?? "", r.GetProperty("a").GetInt64()));
                }
                catch (Exception) { /* a damaged line is left out */ }
            }
        }
        catch (Exception) { }
        return list;
    }

    /// <summary>
    /// The reward screens inside a record: its lines from entry to last loot (plus the grace for rewards), landed in its
    /// place, grouped into screens. Lines read within <see cref="NextTableSeconds"/> of a screen's first line are that
    /// screen (one table read slot by slot); a line later than that starts the next.
    /// </summary>
    public static List<Screen> ScreensIn(InstanceRun r, IEnumerable<ScreenLine> lines)
    {
        var end = (r.LastLoot ?? r.Start).AddSeconds(GraceSeconds);
        var mine = lines.Where(l => l.At >= r.Start && l.At <= end && l.Where.Contains(r.Zone, StringComparison.OrdinalIgnoreCase)).OrderBy(l => l.At).ToList();
        var screens = new List<Screen>();
        DateTime? first = null;
        foreach (var l in mine)
        {
            if (first == null || (l.At - first.Value).TotalSeconds >= NextTableSeconds) { first = l.At; screens.Add(new Screen(l.At, 0, new())); }
            var s = screens[^1];
            if (l.Kind == "kinah") screens[^1] = s with { Kinah = s.Kinah + l.Amount };
            else s.Items[l.Id] = s.Items.GetValueOrDefault(l.Id) + (int)Math.Min(l.Amount, int.MaxValue);
        }
        return screens;
    }

    /// <summary>
    /// The record cut at its reward screens: one run per screen, the first from the record's entry, each next one from just
    /// after the screen before, each ending at its own screen with that screen's Kinah and items. Empty when there are
    /// fewer than two screens (nothing to split at). What the chat counted outside the screens stays with the original,
    /// kept aside in instances.repaired.jsonl.
    /// </summary>
    public static List<InstanceRun> Split(InstanceRun r, List<Screen> screens)
    {
        var parts = new List<InstanceRun>();
        if (screens.Count < 2) return parts;
        var start = r.Start;
        foreach (var s in screens)
        {
            var p = new InstanceRun
            {
                Zone = r.Zone, Kind = r.Kind, Event = r.Event, Family = r.Family, Start = start, LastLoot = s.At, PaidAt = s.At, Kinah = s.Kinah,
                FromPicture = r.FromPicture, Tables = 1, TableAt = s.At, Ended = "split at its reward screen by the player", Witnesses = r.Witnesses,
                KinahNotRead = s.Kinah == 0 && r.FromPicture,
            };
            foreach (var (id, n) in s.Items) p.Items[id] = n;
            foreach (var (id, name) in r.Names) if (p.Items.ContainsKey(id)) p.Names[id] = name;
            parts.Add(p);
            start = s.At.AddSeconds(1);
        }
        return parts;
    }

    static string Key(InstanceRun r) => $"{r.Start.ToUniversalTime():O}|{r.Zone}";

    static HashSet<string>? _kept;
    static HashSet<string> Kept
    {
        get
        {
            if (_kept != null) return _kept;
            try { _kept = File.Exists(KeptPath) ? File.ReadAllLines(KeptPath).ToHashSet() : new(); } catch (Exception) { _kept = new(); }
            return _kept;
        }
    }

    /// <summary>Keep as one: the record counts as it stands, now and after a restart (instances.kept.txt).</summary>
    public static void KeepAsOne(InstanceRun r)
    {
        r.Suspect = "";
        if (!Kept.Add(Key(r))) return;
        try { File.AppendAllLines(KeptPath, new[] { Key(r) }); } catch (Exception) { /* kept for this session */ }
        CrashLog.Note($"run kept as one by the player: {r.Label} started {r.Start.ToLocalTime():MMM d HH:mm:ss}");
    }

    /// <summary>The flags the player cleared with Keep as one, put back on the records as they load (the load marks them again).</summary>
    public static void ApplyKept(IEnumerable<InstanceRun> runs)
    {
        if (Kept.Count == 0) return;
        foreach (var r in runs) if (r.Suspect.Length > 0 && Kept.Contains(Key(r))) r.Suspect = "";
    }

    /// <summary>For the tests: forget the kept list read from disk.</summary>
    internal static void ResetKept() => _kept = null;
}
