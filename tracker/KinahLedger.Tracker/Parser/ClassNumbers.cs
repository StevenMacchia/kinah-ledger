using System.Text.Json;

namespace KinahLedger.Parser;

/// <summary>
/// The game's class numbers, learnt by pairing (2026-11-14): when a member's class comes as a number and the party list on
/// the screen names that member's class (Ocr/PartyFrame.cs), the pairing is kept (class-numbers.json in the tracker's
/// folder) and logged, so a class number alone names the class afterwards, with or without the party list in view.
/// </summary>
public static class ClassNumbers
{
    static readonly object Gate = new();
    static Dictionary<int, string>? _map;
    static string _mapDir = "";
    /// <summary>Tests point this elsewhere.</summary>
    public static string? PathOverride { get; set; }
    static string PathNow => PathOverride ?? System.IO.Path.Combine(CrashLog.Dir, "class-numbers.json");

    static Dictionary<int, string> Map
    {
        get
        {
            if (_map != null && _mapDir == PathNow) return _map;
            _mapDir = PathNow;
            try { _map = File.Exists(PathNow) ? JsonSerializer.Deserialize<Dictionary<int, string>>(File.ReadAllText(PathNow)) ?? new() : new(); }
            catch (Exception) { _map = new(); }
            return _map;
        }
    }

    /// <summary>The class a number was paired with ("templar"), or null.</summary>
    public static string? Name(int number) { lock (Gate) return Map.TryGetValue(number, out var c) ? c : null; }

    /// <summary>A pairing seen: kept and logged when new (a number seen with another class later is logged, the first kept).</summary>
    public static void Pair(int number, string cls)
    {
        lock (Gate)
        {
            if (Map.TryGetValue(number, out var had))
            {
                if (had != cls) CrashLog.NoteBurst($"parser: class number {number} was paired with {had}, now seen with {cls}; kept {had}");
                return;
            }
            Map[number] = cls;
            try { Directory.CreateDirectory(System.IO.Path.GetDirectoryName(PathNow)!); File.WriteAllText(PathNow, JsonSerializer.Serialize(Map)); }
            catch (Exception ex) { CrashLog.Write("class numbers", ex); }
            CrashLog.Note($"parser: class number {number} is the {cls}");
        }
    }

    public static IReadOnlyDictionary<int, string> All { get { lock (Gate) return new Dictionary<int, string>(Map); } }
    public static void Reset() { lock (Gate) { _map = null; _mapDir = ""; } }
}
