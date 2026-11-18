using System.Text.Json;

namespace KinahLedger.Parser;

/// <summary>
/// The boss fights kept on this PC, per character: encounters.jsonl (one finished fight per line, newest last) and
/// encounter-live.json (the fight under way, so a restart in the middle of one carries it on). Never uploaded, unless the
/// player turns on Show my boss bests on my kinahledger.com profile (off by default): then only their own best kill per boss
/// and their last pull of it go to their profile (Upload/BossBests.cs); other players' numbers never leave this PC.
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
    public EncounterStore(Func<string> dir) { _dir = dir; Records = new FightRecords(dir, ReadEvery); }

    /// <summary>Your records per fight (Parser/FightRecords.cs): each kill kept goes into its fight's record.</summary>
    public FightRecords Records { get; }

    /// <summary>Every finished fight in a folder's file, oldest first as written (the records' first build reads them all).</summary>
    static IEnumerable<Encounter> ReadEvery(string dir)
    {
        var p = System.IO.Path.Combine(dir, FileName);
        if (!File.Exists(p)) yield break;
        foreach (var line in File.ReadLines(p))
        {
            if (line.Length < 2) continue;
            Encounter? e = null;
            try { e = JsonSerializer.Deserialize<Encounter>(line, Json); } catch (JsonException) { }
            if (e is { End: not null, OpenWorld: false }) yield return e;
        }
    }

    string PathIn(string name) => System.IO.Path.Combine(_dir(), name);

    /// <summary>Goes up with every fight added: the page redraws its list only then.</summary>
    public int Version { get { lock (_gate) return _version; } }
    int _version;

    public void Add(Encounter e)
    {
        if (e.OpenWorld) return;   // never kept (Encounter.OpenWorld)
        lock (_gate)
        {
            try
            {
                var dir = _dir();
                Directory.CreateDirectory(dir);
                File.AppendAllText(System.IO.Path.Combine(dir, FileName), JsonSerializer.Serialize(e, Json) + "\n");
                if (_cache != null && _cacheDir == dir) { _cache.RemoveAll(x => x.Id == e.Id); _cache.Add(e); }
                _version++;
                Records.Note(e);
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
            int openWorld = 0;
            try
            {
                var p = System.IO.Path.Combine(dir, FileName);
                if (File.Exists(p))
                    foreach (var line in File.ReadLines(p))
                    {
                        if (line.Length < 2) continue;
                        try
                        {
                            if (JsonSerializer.Deserialize<Encounter>(line, Json) is not { End: not null } e) continue;
                            if (e.OpenWorld) { openWorld++; continue; }   // kept by 0.10.87 to 0.10.89: dropped below
                            if (e.Boss.Length > 0 || e.ModeKind != ParserMode.Bosses) list.Add(e);
                        }
                        catch (JsonException) { /* a line cut by a crash: the rest still reads */ }
                    }
                // open-world fights an older version kept are taken out of the file, so none stays on this PC
                if (openWorld > 0) DropOpenWorld(p);
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

    /// <summary>The file again without its open-world fights (each other line as it was), through a temp file.</summary>
    static void DropOpenWorld(string path)
    {
        try
        {
            var keep = File.ReadLines(path).Where(line =>
            {
                try { return JsonSerializer.Deserialize<Encounter>(line, Json) is not { OpenWorld: true }; }
                catch (JsonException) { return true; }
            }).ToList();
            var tmp = path + ".tmp";
            File.WriteAllLines(tmp, keep);
            File.Move(tmp, path, overwrite: true);
            CrashLog.Note("parser: open-world fights an older version kept were removed");
        }
        catch (Exception ex) { CrashLog.Write("parser: removing open-world fights", ex); }
    }

    /// <summary>Forget the cache (a character switch, a test).</summary>
    public void Reload() { lock (_gate) { _cache = null; _version++; Records.Reset(); } }

    /// <summary>The files rewritten by <paramref name="whileHeld"/> (a one-time repair) while no fight is added, then read back, the records too.</summary>
    public void Rewrite(Action whileHeld)
    {
        lock (_gate)
        {
            try { whileHeld(); }
            finally { _cache = null; _version++; Records.Reset(); }
        }
    }

    /// <summary>Keeps the records under way (none: the file goes).</summary>
    public void SaveLive(IReadOnlyCollection<Encounter> live)
    {
        lock (_gate)
        {
            try
            {
                var p = PathIn(LiveName);
                live = live.Where(e => !e.OpenWorld).ToList();   // never kept, not even while under way
                if (live.Count == 0) { if (File.Exists(p)) File.Delete(p); return; }
                Directory.CreateDirectory(_dir());
                var tmp = p + ".tmp";
                File.WriteAllText(tmp, JsonSerializer.Serialize(live, Json));
                File.Move(tmp, p, overwrite: true);
            }
            catch (Exception ex) { CrashLog.Write("parser: keeping the fight under way", ex); }
        }
    }

    /// <summary>The records that were under way when the tracker closed (a list; one record from the first builds).</summary>
    public List<Encounter> LoadLive()
    {
        lock (_gate)
        {
            try
            {
                var p = PathIn(LiveName);
                if (!File.Exists(p)) return new();
                var text = File.ReadAllText(p).TrimStart();
                var list = text.StartsWith('[') ? JsonSerializer.Deserialize<List<Encounter>>(text, Json) ?? new()
                    : JsonSerializer.Deserialize<Encounter>(text, Json) is { } one ? new List<Encounter> { one } : new();
                return list.Where(e => e is { End: null, OpenWorld: false } && (e.Boss.Length > 0 || e.ModeKind != ParserMode.Bosses)).ToList();
            }
            catch (Exception) { return new(); }
        }
    }
}
