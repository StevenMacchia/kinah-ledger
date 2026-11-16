using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace KinahLedger.Parser;

/// <summary>
/// Your records, one per fight: a boss in one instance in one of the instance's modes ("Necromancer Duanka · Ferocious Horn
/// Den · Conquest"), never the same boss elsewhere (Steven, 2026-11-16: the best and record pop-ups "ONLY pop up on a per fight
/// basis. Save your records per fight"). Kept per character in records.json beside the fights, so a record outlives the
/// fights read back (EncounterStore.ReadMax); built from every kept fight the first time. Your own figures only, from kills.
/// </summary>
public sealed class FightRecord
{
    public string Key { get; set; } = "";
    public string Boss { get; set; } = "";
    public long BossId { get; set; }
    public string Instance { get; set; } = "";
    public string GameMode { get; set; } = "";
    public string Kind { get; set; } = "";
    public int Kills { get; set; }
    /// <summary>Your best DPS on a kill, the fight it came in, and the best before it (what that fight was measured against).</summary>
    public double BestDps { get; set; }
    public string BestDpsFight { get; set; } = "";
    public DateTime BestDpsAt { get; set; }
    public double PrevBestDps { get; set; }
    /// <summary>Your best healing a second on a kill (0 for one who never healed), as the DPS.</summary>
    public double BestHps { get; set; }
    public string BestHpsFight { get; set; } = "";
    public DateTime BestHpsAt { get; set; }
    public double PrevBestHps { get; set; }
    /// <summary>The fastest kill (seconds), and your biggest hit.</summary>
    public double FastestSeconds { get; set; }
    public DateTime FastestAt { get; set; }
    public long BiggestHit { get; set; }
    public DateTime BiggestHitAt { get; set; }
    public DateTime LastKillAt { get; set; }
    /// <summary>Fights already counted in (a fight saved twice counts once).</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] public List<string>? Seen { get; set; }

    /// <summary>Your best DPS before <paramref name="fightId"/>: the record, unless that fight set it (then the one it beat).</summary>
    public double DpsBefore(string fightId) => BestDpsFight == fightId ? PrevBestDps : BestDps;
    public double HpsBefore(string fightId) => BestHpsFight == fightId ? PrevBestHps : BestHps;
}

public sealed class FightRecords
{
    /// <summary>records2.json since 0.10.128 (healing a second is the part that landed): built afresh from the fights, the old file left.</summary>
    public const string FileName = "records2.json";
    /// <summary>A fight's Seen list keeps this many ids (enough to never count a fight saved twice).</summary>
    const int SeenKept = 64;

    static readonly JsonSerializerOptions Json = new() { WriteIndented = true };
    readonly object _gate = new();
    readonly Func<string> _dir;
    readonly Func<string, IEnumerable<Encounter>> _seed;
    Dictionary<string, FightRecord>? _byKey;
    string _dirLoaded = "";

    /// <param name="dir">The character's folder (asked each time, so a switch moves with it).</param>
    /// <param name="seed">Every kept fight in a folder, oldest first: the records built from them when the folder has none yet.</param>
    public FightRecords(Func<string> dir, Func<string, IEnumerable<Encounter>> seed) { _dir = dir; _seed = seed; }

    /// <summary>The fight a record is kept for: the boss (its id, else its name), the instance and the instance's mode.</summary>
    public static string KeyOf(Encounter e) =>
        $"{(e.BossId > 0 ? e.BossId.ToString(System.Globalization.CultureInfo.InvariantCulture) : e.Boss.Trim().ToLowerInvariant())}|{BossBook.Strip(e.Instance).Trim().ToLowerInvariant()}|{e.GameMode.Trim().ToLowerInvariant()}";

    /// <summary>A fight that counts toward a record: a boss kill with your damage in it.</summary>
    public static bool Counts(Encounter e) => e.ModeKind == ParserMode.Bosses && !e.Live && !e.Wave && e.Outcome == "kill"
        && (e.Boss.Length > 0 || e.BossId > 0) && e.You is { Damage: > 0 };

    /// <summary>The record of this fight (null before your first kill of it).</summary>
    public FightRecord? Of(Encounter e) { lock (_gate) return Load().TryGetValue(KeyOf(e), out var r) ? r : null; }

    /// <summary>Every record, the newest kill first.</summary>
    public List<FightRecord> All() { lock (_gate) return Load().Values.OrderByDescending(r => r.LastKillAt).ToList(); }

    /// <summary>A fight just kept: into its record, and the file saved when it changed.</summary>
    public void Note(Encounter e)
    {
        if (!Counts(e)) return;
        lock (_gate)
        {
            var map = Load();
            if (Fold(map, e)) Save(map);
        }
    }

    /// <summary>Folds one fight into its record; true when it changed.</summary>
    static bool Fold(Dictionary<string, FightRecord> map, Encounter e)
    {
        if (!Counts(e)) return false;
        string key = KeyOf(e);
        if (!map.TryGetValue(key, out var r)) map[key] = r = new FightRecord { Key = key };
        r.Seen ??= new();
        if (r.Seen.Contains(e.Id)) return false;
        r.Seen.Add(e.Id);
        if (r.Seen.Count > SeenKept) r.Seen.RemoveRange(0, r.Seen.Count - SeenKept);
        var end = e.End ?? e.LastAt;
        var you = e.You!;
        r.Boss = e.Boss; r.BossId = e.BossId; r.Instance = BossBook.Strip(e.Instance); r.GameMode = e.GameMode; r.Kind = e.Kind;
        r.Kills++;
        if (end > r.LastKillAt) r.LastKillAt = end;
        double dps = e.Dps(you, end);
        if (dps > r.BestDps) { r.PrevBestDps = r.BestDps; r.BestDps = dps; r.BestDpsFight = e.Id; r.BestDpsAt = end; }
        if (e.HpsOf(you, end) is { } hps && hps > r.BestHps) { r.PrevBestHps = r.BestHps; r.BestHps = hps; r.BestHpsFight = e.Id; r.BestHpsAt = end; }
        double secs = e.Duration(end).TotalSeconds;
        if (secs > 0 && (r.FastestSeconds <= 0 || secs < r.FastestSeconds)) { r.FastestSeconds = secs; r.FastestAt = end; }
        long big = you.Abilities.Select(a => a.Max ?? 0).DefaultIfEmpty(0).Max();
        if (big > r.BiggestHit) { r.BiggestHit = big; r.BiggestHitAt = end; }
        return true;
    }

    /// <summary>Built from fights alone (tests, the replay): the records as the file would hold them.</summary>
    public static Dictionary<string, FightRecord> From(IEnumerable<Encounter> fights)
    {
        var map = new Dictionary<string, FightRecord>();
        foreach (var e in fights.OrderBy(x => x.Start)) Fold(map, e);
        return map;
    }

    Dictionary<string, FightRecord> Load()
    {
        string dir = _dir();
        if (_byKey != null && _dirLoaded == dir) return _byKey;
        var path = Path.Combine(dir, FileName);
        Dictionary<string, FightRecord>? map = null;
        try
        {
            if (File.Exists(path)) map = (JsonSerializer.Deserialize<List<FightRecord>>(File.ReadAllText(path), Json) ?? new()).Where(r => r.Key.Length > 0).GroupBy(r => r.Key).ToDictionary(g => g.Key, g => g.First());
        }
        catch (Exception ex) { CrashLog.Write("parser: reading the records", ex); map = null; }
        if (map == null)
        {
            // none yet: every fight kept here, oldest first (the store's whole file, not only the newest it reads back)
            map = new();
            try { foreach (var e in _seed(dir)) Fold(map, e); } catch (Exception ex) { CrashLog.Write("parser: building the records", ex); }
            if (map.Count > 0) Save(map, dir);
        }
        _byKey = map; _dirLoaded = dir;
        return map;
    }

    void Save(Dictionary<string, FightRecord> map, string? dir = null)
    {
        try
        {
            dir ??= _dir();
            Directory.CreateDirectory(dir);
            var path = Path.Combine(dir, FileName);
            var tmp = path + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(map.Values.OrderBy(r => r.Key).ToList(), Json));
            File.Move(tmp, path, overwrite: true);
        }
        catch (Exception ex) { CrashLog.Write("parser: saving the records", ex); }
    }
}
