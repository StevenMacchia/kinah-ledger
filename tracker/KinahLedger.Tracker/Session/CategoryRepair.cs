using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using KinahLedger.Packets;
using KinahLedger.Parser;

namespace KinahLedger.Session;

/// <summary>
/// The one-time repair of fights and runs kept in the wrong category (1.0.7). Steven, 2026-11-17: "it didnt properly log any
/// of my transcendence that i did with Tilly ... it put them in expeditions ... the deus research base ... make sure these are
/// properly logged in the correct categories". Until this build:
/// <list type="bullet">
/// <item>a Transcendence the game entered with no name was kept as its expedition, in the mode "G" (the game's own mark of a
/// Transcendence, Parser/BossBook.cs IsTranscendenceWord);</item>
/// <item>the second run of an instance in the same copy kept every boss as "Unnamed boss" (the last run's bosses were still
/// down);</item>
/// <item>a piece of the place's name was kept as its mode (Fire Temple's "Temple");</item>
/// <item>and the runs of those Transcendences were filed as expeditions.</item>
/// </list>
/// Each character's encounters.jsonl and instances.jsonl get the fix where the evidence is in the file itself: a fight in the
/// mode "G" is a Transcendence (its stage where Steven's logs proved it, <see cref="Stages"/>), an unnamed boss of a listed
/// instance takes the next boss of the list in route order as the live Parser now names it, and a run whose own fights are a
/// Transcendence is one. Nothing else changes; every other line is written back as it was. Each file is copied beside it first
/// (<see cref="BackupSuffix"/>), what changed is written to <see cref="LogName"/>, and the records per fight are built again
/// from the fights. Run once at a start (Ui/MainWindow.Recount.cs, flag <see cref="Characters.CategoriesRepaired"/>).
/// </summary>
public static class CategoryRepair
{
    /// <summary>The copy kept beside a file before the repair first writes it: encounters.pre-categories.jsonl, instances.pre-categories.jsonl.</summary>
    public const string BackupSuffix = ".pre-categories";
    public const string LogName = "categories.log";

    /// <summary>
    /// The stage of Steven's Transcendences where his own logs prove it (the records keep no map code): 2026-11-17 Shattered
    /// Arkanis and Deus Research Base at Stage 4 ("Set to Stage 4." 13:48 and 15:42, "Shattered Arkanis Stage 4" and "Deus
    /// Research Base Stage 4" achieved, the codes Arkanis_G_09 and DeusCenter_G_09); 2026-11-16 22:00 UTC Shattered Arkanis at
    /// Stage 3 ("Set to Stage 3." at 22:06:53, the code Arkanis_G_06). Any other fight in the mode "G": no stage known.
    /// </summary>
    static readonly (DateTime From, DateTime To, string Instance, int Stage)[] Stages =
    {
        (Utc(2026, 11, 17, 18, 0), Utc(2026, 11, 17, 23, 30), "Shattered Arkanis", 4),
        (Utc(2026, 11, 17, 18, 0), Utc(2026, 11, 17, 23, 30), "Deus Research Base", 4),
        (Utc(2026, 11, 16, 22, 0), Utc(2026, 11, 16, 22, 30), "Shattered Arkanis", 3),
    };
    static DateTime Utc(int y, int mo, int d, int h, int mi) => new(y, mo, d, h, mi, 0, DateTimeKind.Utc);

    /// <summary>A run's own fights this long before its start or after its end still date it (the minimap opens a run a few seconds late).</summary>
    const double RunSlackSeconds = 120;
    /// <summary>Two pulls of an instance this far apart are never the same copy (a left run, a later entry).</summary>
    const double CopyGapMinutes = 20;

    public sealed record Result(List<string> Log, int Fights, int Runs, int Files);

    /// <summary>The repair over every character folder of <paramref name="dataDir"/> (characters\*), and the top folder's own files; <paramref name="dryRun"/> writes nothing.</summary>
    public static Result Run(string dataDir, bool dryRun = false, BossBook? book = null)
    {
        book ??= BossBook.Shared;
        var log = new List<string>();
        int fights = 0, runs = 0, files = 0;
        var dirs = new List<string> { dataDir };
        var chars = Path.Combine(dataDir, "characters");
        if (Directory.Exists(chars)) dirs.AddRange(Directory.GetDirectories(chars).OrderBy(d => d, StringComparer.OrdinalIgnoreCase));
        foreach (var dir in dirs)
        {
            try
            {
                var (f, r, w) = RepairDir(dir, book, dryRun, log);
                fights += f; runs += r; files += w;
            }
            catch (Exception ex) { log.Add($"{dir}: not repaired ({ex.GetType().Name}: {ex.Message})"); }
        }
        log.Insert(0, $"categories repair{(dryRun ? " (dry run)" : "")}: {fights} fight(s) and {runs} run(s) changed in {files} file(s)");
        return new Result(log, fights, runs, files);
    }

    static (int Fights, int Runs, int Files) RepairDir(string dir, BossBook book, bool dryRun, List<string> log)
    {
        string who = Path.GetFileName(dir.TrimEnd(Path.DirectorySeparatorChar));
        int files = 0;
        var encPath = Path.Combine(dir, EncounterStore.FileName);
        var fightLines = File.Exists(encPath) ? File.ReadAllLines(encPath).ToList() : new List<string>();
        var fightNodes = fightLines.Select(Parse).ToList();
        int fights = RepairFights(fightNodes, book, who, log);
        if (fights > 0 && !dryRun) { Write(encPath, fightLines, fightNodes); files++; }

        // the runs: a run of an expedition whose own fights (as they stand now) are a Transcendence's is one
        var trans = fightNodes.OfType<JsonObject>()
            .Where(n => Str(n, "Kind") == "Transcendence" && Str(n, "Mode") == "boss" && Time(n, "Start") is not null)
            .Select(n => (Instance: BossBook.Strip(Str(n, "Instance")), At: Time(n, "Start")!.Value)).ToList();
        int runs = 0;
        var runPath = Path.Combine(dir, "instances.jsonl");
        if (trans.Count > 0 && File.Exists(runPath))
        {
            var runLines = File.ReadAllLines(runPath).ToList();
            var runNodes = runLines.Select(Parse).ToList();
            foreach (var n in runNodes.OfType<JsonObject>())
            {
                if (Str(n, "Kind") != "Expedition" || Time(n, "Start") is not DateTime start) continue;
                var zone = Str(n, "Zone");
                var end = new[] { Time(n, "LastLoot"), Time(n, "Left"), Time(n, "TableAt"), Time(n, "PaidAt") }.Where(x => x != null).Select(x => x!.Value).DefaultIfEmpty(start).Max();
                var hit = trans.Where(x => x.Instance.Equals(zone, StringComparison.OrdinalIgnoreCase)
                                           && (x.At - start).TotalSeconds >= -RunSlackSeconds && (x.At - end).TotalSeconds <= RunSlackSeconds).ToList();
                if (hit.Count == 0) continue;
                n["Kind"] = "Transcendence";
                n["__changed"] = true;
                runs++;
                log.Add($"{who}: run {zone} {start.ToLocalTime():yyyy-MM-dd HH:mm:ss}: Expedition -> Transcendence ({hit.Count} of its boss pulls were a Transcendence's)");
            }
            if (runs > 0 && !dryRun) { Write(runPath, runLines, runNodes); files++; }
        }
        // the records per fight are built from the fights: built again from the repaired ones
        if (fights > 0 && !dryRun)
        {
            var rec = Path.Combine(dir, FightRecords.FileName);
            if (File.Exists(rec)) File.Move(rec, rec + BackupSuffix, overwrite: true);
        }
        return (fights, runs, files);
    }

    /// <summary>The fights of one file put right in place (each changed node marked); the number changed.</summary>
    internal static int RepairFights(List<JsonNode?> nodes, BossBook book, string who, List<string> log)
    {
        int changed = 0;
        foreach (var n in nodes.OfType<JsonObject>())
        {
            var mode = Str(n, "GameMode").Trim();
            var kind = Str(n, "Kind");
            var start = Time(n, "Start");
            var inst = BossBook.Strip(Str(n, "Instance"));
            var was = $"{kind}/{mode}";
            if (BossBook.IsTranscendenceWord(mode))
            {
                if (kind == "Expedition") n["Kind"] = "Transcendence";
                int stage = start is DateTime s ? Stages.Where(x => s >= x.From && s <= x.To && x.Instance.Equals(inst, StringComparison.OrdinalIgnoreCase)).Select(x => x.Stage).FirstOrDefault() : 0;
                n["GameMode"] = BossBook.StageMode(stage);
            }
            else if (mode.Length > 0 && !BossBook.IsModeWord(mode) && book.IsCodePart(mode)) n["GameMode"] = "";
            var now = $"{Str(n, "Kind")}/{Str(n, "GameMode")}";
            if (now != was)
            {
                n["__changed"] = true; changed++;
                log.Add($"{who}: {Str(n, "Mode")} {Name(n)} in {inst} {start?.ToLocalTime():yyyy-MM-dd HH:mm:ss}: {was} -> {now}");
            }
        }
        NameUnnamed(nodes, book, who, log);
        return nodes.OfType<JsonObject>().Count(n => n.ContainsKey("__changed"));   // each fight once, its kind and its name both put right
    }

    /// <summary>
    /// The unnamed boss pulls of a listed instance named as the live Parser names them now: per instance in time order, the
    /// next boss of the list in route order not down yet in this copy (a spawn keeps the name it got), a new copy after the
    /// last boss died or a long gap.
    /// </summary>
    static int NameUnnamed(List<JsonNode?> nodes, BossBook book, string who, List<string> log)
    {
        int changed = 0;
        var pulls = nodes.OfType<JsonObject>().Where(n => Str(n, "Mode") == "boss" && Time(n, "Start") != null && book.Knows(Str(n, "Instance")))
            .OrderBy(n => Time(n, "Start")).GroupBy(n => BossBook.Strip(Str(n, "Instance")), StringComparer.OrdinalIgnoreCase);
        foreach (var g in pulls)
        {
            var list = book.BossesOf(g.Key);
            var down = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var downIds = new HashSet<long>();
            var names = new Dictionary<long, string>();
            DateTime? lastEnd = null;
            foreach (var n in g)
            {
                var start = Time(n, "Start")!.Value;
                long id = n["BossId"]?.GetValue<long>() ?? 0;
                var boss = Str(n, "Boss");
                bool finalDown = list.Any(b => b.Final && down.Contains(b.Name));
                bool sameSpawn = id != 0 && (downIds.Contains(id) || names.ContainsKey(id));
                if ((lastEnd is DateTime le && (start - le).TotalMinutes > CopyGapMinutes) || (finalDown && !sameSpawn)) { down.Clear(); downIds.Clear(); names.Clear(); }
                if (boss == BossParser.UnnamedBoss)
                {
                    string? name = id != 0 && names.TryGetValue(id, out var had) ? had
                        : list.FirstOrDefault(b => !b.Wave && !down.Contains(b.Name) && !names.ContainsValue(b.Name))?.Name;
                    if (name != null)
                    {
                        if (id != 0) names[id] = name;
                        var listed = book.Find(g.Key, name)!;
                        n["Boss"] = name; n["FinalBoss"] = listed.Final; n["Wave"] = listed.Wave;
                        n["__changed"] = true; changed++;
                        log.Add($"{who}: boss pull in {g.Key} {start.ToLocalTime():yyyy-MM-dd HH:mm:ss} (spawn {id}): {BossParser.UnnamedBoss} -> {name} (the next on the boss list)");
                        boss = name;
                    }
                }
                if (Str(n, "Outcome") == "kill") { down.Add(boss); if (id != 0) downIds.Add(id); }
                lastEnd = Time(n, "End") ?? Time(n, "LastAt") ?? start;
            }
        }
        return changed;
    }

    static JsonNode? Parse(string line)
    {
        if (line.Trim().Length < 2) return null;
        try { return JsonNode.Parse(line); } catch (JsonException) { return null; }
    }

    static string Str(JsonObject n, string key) => n[key] is JsonValue v && v.TryGetValue<string>(out var s) ? s : "";

    static DateTime? Time(JsonObject n, string key) =>
        n[key] is JsonValue v && v.TryGetValue<string>(out var s) && DateTime.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var t) ? t : null;

    static string Name(JsonObject n) => Str(n, "Boss") is { Length: > 0 } b ? b : Str(n, "Mode");

    /// <summary>The file again: each changed line from its node (the mark left out), every other line as it was; the old one kept beside it first.</summary>
    static void Write(string path, List<string> lines, List<JsonNode?> nodes)
    {
        var backup = Path.Combine(Path.GetDirectoryName(path)!, Path.GetFileNameWithoutExtension(path) + BackupSuffix + Path.GetExtension(path));
        if (!File.Exists(backup)) File.Copy(path, backup);
        var outp = new List<string>(lines.Count);
        for (int i = 0; i < lines.Count; i++)
        {
            if (nodes[i] is JsonObject o && o.Remove("__changed")) outp.Add(o.ToJsonString());
            else outp.Add(lines[i]);
        }
        var nl = File.ReadAllText(path).Contains("\r\n") ? "\r\n" : "\n";   // the file's own line ends
        var tmp = path + ".tmp";
        File.WriteAllText(tmp, string.Join(nl, outp) + nl);
        File.Move(tmp, path, overwrite: true);
    }
}
