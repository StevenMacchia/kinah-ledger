using System.Text.Json;

namespace KinahLedger.Parser;

/// <summary>
/// The boss fights kept on this PC, per character: encounters.jsonl (one finished fight per line, newest last) and
/// encounter-live.json (the fight under way, so a restart in the middle of one carries it on). Never uploaded.
/// </summary>
public sealed class EncounterStore
{
    public const string FileName = "encounters.jsonl", LiveName = "encounter-live.json";
    /// <summary>The newest this many are read back; the file keeps them all.</summary>
    public const int ReadMax = 2000;

    static readonly JsonSerializerOptions Json = new();
    readonly object _gate = new();
    readonly Func<string> _dir;
    List<Encounter>? _cache;
    string _cacheDir = "";

    /// <param name="dir">The folder to keep them in (the character's: Characters.DataDir); asked each time, so a character switch moves with it.</param>
    public EncounterStore(Func<string> dir) => _dir = dir;

    string PathIn(string name) => System.IO.Path.Combine(_dir(), name);

    /// <summary>Goes up with every fight added: the page redraws its list only then.</summary>
    public int Version { get { lock (_gate) return _version; } }
    int _version;

    public void Add(Encounter e)
    {
        lock (_gate)
        {
            try
            {
                var dir = _dir();
                Directory.CreateDirectory(dir);
                File.AppendAllText(System.IO.Path.Combine(dir, FileName), JsonSerializer.Serialize(e, Json) + "\n");
                if (_cache != null && _cacheDir == dir) { _cache.RemoveAll(x => x.Id == e.Id); _cache.Add(e); }
                _version++;
            }
            catch (Exception ex) { CrashLog.Write("parser: saving a fight", ex); }
        }
    }

    /// <summary>The kept fights, oldest first (a line that doesn't read is skipped; a fight kept twice counts once, the later copy).</summary>
    public List<Encounter> All()
    {
        lock (_gate)
        {
            var dir = _dir();
            if (_cache != null && _cacheDir == dir) return _cache.ToList();
            var list = new List<Encounter>();
            try
            {
                var p = System.IO.Path.Combine(dir, FileName);
                if (File.Exists(p))
                    foreach (var line in File.ReadLines(p))
                    {
                        if (line.Length < 2) continue;
                        try { if (JsonSerializer.Deserialize<Encounter>(line, Json) is { Boss.Length: > 0, End: not null } e) list.Add(e); }
                        catch (JsonException) { /* a line cut by a crash: the rest still reads */ }
                    }
            }
            catch (Exception ex) { CrashLog.Write("parser: reading the fights", ex); }
            var byId = new Dictionary<string, Encounter>();
            foreach (var e in list) byId[e.Id] = e;
            var unique = byId.Values.OrderBy(e => e.Start).ToList();
            if (unique.Count > ReadMax) unique = unique.Skip(unique.Count - ReadMax).ToList();
            _cache = unique; _cacheDir = dir;
            return unique.ToList();
        }
    }

    /// <summary>Forget the cache (a character switch, a test).</summary>
    public void Reload() { lock (_gate) { _cache = null; _version++; } }

    /// <summary>Keeps the fight under way (null: none, the file goes).</summary>
    public void SaveLive(Encounter? live)
    {
        lock (_gate)
        {
            try
            {
                var p = PathIn(LiveName);
                if (live == null) { if (File.Exists(p)) File.Delete(p); return; }
                Directory.CreateDirectory(_dir());
                var tmp = p + ".tmp";
                File.WriteAllText(tmp, JsonSerializer.Serialize(live, Json));
                File.Move(tmp, p, overwrite: true);
            }
            catch (Exception ex) { CrashLog.Write("parser: keeping the fight under way", ex); }
        }
    }

    /// <summary>The fight that was under way when the tracker closed, or null.</summary>
    public Encounter? LoadLive()
    {
        lock (_gate)
        {
            try
            {
                var p = PathIn(LiveName);
                if (!File.Exists(p)) return null;
                var e = JsonSerializer.Deserialize<Encounter>(File.ReadAllText(p), Json);
                return e is { Boss.Length: > 0, End: null } ? e : null;
            }
            catch (Exception) { return null; }
        }
    }
}
