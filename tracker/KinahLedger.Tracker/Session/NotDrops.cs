using System.Text.Json;

namespace KinahLedger.Session;

/// <summary>
/// One pickup the player said was no drop ("Not a drop" on its feed line or its row under YOUR LAST PICKUPS): when it was
/// counted, the item, how many, where it landed, the local day, and the quest steps it had completed (their keys and
/// points), so Undo puts them back.
/// </summary>
public sealed record NotDrop(DateTime At, string Day, long ItemId, string Name, int Count, bool Bound, string Where, List<QuestDone>? Quests = null, SessionEngine.Taken? DayTaken = null, DateTime? Run = null);

/// <summary>
/// The pickups taken out of the player's own totals by "Not a drop" (Steven, 2026-11-13: "i bought the egos and they
/// counted as drops it fucked up my totals"). A pickup taken out leaves every count it was in (the run, the zone, today and
/// the week, the enemy's drops, the 15-minute count not sent yet), the rare-drop alert and the quest points it gave; today
/// is saved at once, so a restart keeps it. This list (not-drops.json, per character) is what the window marks and what
/// Undo works from; a restart also drops a marked pickup that comes back from a replay of the same moment
/// (<see cref="Has"/>). Kept 30 days.
/// </summary>
public static class NotDrops
{
    static readonly object Gate = new();
    static List<NotDrop>? _list;
    static string _dir = "";

    /// <summary>Where the list is kept: not-drops.json beside today.json (the character's folder). Tests point it elsewhere.</summary>
    public static string? DirOverride { get; set; }
    static string Dir => DirOverride ?? Characters.DataDir;
    static string PathNow => Path.Combine(Dir, "not-drops.json");

    static List<NotDrop> List()
    {
        if (_list != null && _dir == Dir) return _list;
        _dir = Dir;
        try { _list = File.Exists(PathNow) ? JsonSerializer.Deserialize<List<NotDrop>>(File.ReadAllText(PathNow)) ?? new() : new(); }
        catch (Exception ex) { CrashLog.Write("not-drops.json", ex); _list = new(); }
        return _list;
    }

    static void Save()
    {
        try
        {
            Directory.CreateDirectory(Dir);
            var tmp = PathNow + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(_list ?? new()));
            File.Move(tmp, PathNow, true);
        }
        catch (Exception ex) { CrashLog.Write("not-drops.json", ex); }
    }

    /// <summary>The same pickup: its time to the tick, its item and its count.</summary>
    static bool Same(NotDrop d, DateTime at, long id, int count) => d.At == at && d.ItemId == id && d.Count == count;

    public static bool Has(DateTime at, long itemId, int count) { lock (Gate) return List().Any(d => Same(d, at, itemId, count)); }

    public static NotDrop? Find(DateTime at, long itemId, int count) { lock (Gate) return List().FirstOrDefault(d => Same(d, at, itemId, count)); }

    public static void Add(NotDrop d)
    {
        lock (Gate)
        {
            var l = List();
            l.RemoveAll(x => Same(x, d.At, d.ItemId, d.Count) || (DateTime.UtcNow - x.At).TotalDays > 30);
            l.Add(d);
            Save();
        }
    }

    public static bool Remove(DateTime at, long itemId, int count)
    {
        lock (Gate)
        {
            bool any = List().RemoveAll(x => Same(x, at, itemId, count)) > 0;
            if (any) Save();
            return any;
        }
    }

    /// <summary>The pickups taken out on a local day (yyyy-MM-dd), oldest first.</summary>
    public static List<NotDrop> On(string day) { lock (Gate) return List().Where(d => d.Day == day).OrderBy(d => d.At).ToList(); }

    /// <summary>For the tests and a character switch: the list is read again from its folder.</summary>
    public static void Forget() { lock (Gate) { _list = null; _dir = ""; } }
}
