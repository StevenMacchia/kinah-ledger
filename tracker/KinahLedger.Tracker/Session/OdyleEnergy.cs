using System.Text.Json;
using KinahLedger.Packets;

namespace KinahLedger.Session;

/// <summary>
/// Each character's Odyle Energy as last read (Steven, 2026-11-19: "Show on the DPS meter very tiny an icon with the
/// energy odyle and then how much energy your character has - update after each run at a minimum"), kept in odyle.json
/// beside characters.json, by the character's id ("c1"; "" before any character was seen). The game says the figure at
/// login, at a character switch, at every move to another server (each instance's entry and exit), when a run's reward
/// takes from it, when an Odyle Energy item charges it, and at its timed refill; the last reading of each character is kept,
/// so the meter shows it from the start until a new one comes.
/// <para>A reading's age is made up for: the natural pool refills 15 every three hours (at 01:00, 04:00 ... 22:00 UTC),
/// offline too, up to its cap (560, or 840 with membership: 840 once the character was seen above 560). Never known: no
/// figure at all, never a 0.</para>
/// <para>A character switch is seen on the screen (the class icon) some seconds after the game sent the new character's
/// list, which was kept for the old one: <see cref="Switched"/> moves that reading to the new character and gives the old
/// one back what it had before. Thread-safe: the reading thread writes, the window reads.</para>
/// </summary>
public sealed class OdyleEnergy
{
    /// <summary>One character's last reading, and the one before it (what a switch gives back).</summary>
    public sealed class Kept
    {
        public DateTime At { get; set; }
        public int Natural { get; set; }
        public int Extra { get; set; }
        public string Why { get; set; } = "";
        /// <summary>The natural pool's cap as far as is known: 840 once seen above 560, else 560.</summary>
        public int Cap { get; set; } = BaseCap;
        public Kept? Before { get; set; }
        public int Total => Natural + Extra;
    }

    sealed class Book
    {
        public int Version { get; set; } = 1;
        public Dictionary<string, Kept> Characters { get; set; } = new();
    }

    public const int BaseCap = 560, MemberCap = 840, RefillBy = 15;
    /// <summary>A list read this long before the screen saw another character came at that character's login.</summary>
    public static readonly TimeSpan SwitchWindow = TimeSpan.FromMinutes(3);

    readonly object _gate = new();
    readonly string? _path;
    Book _book = new();
    int _changes;

    /// <param name="path">odyle.json; null keeps it in memory only (the self-tests).</param>
    public OdyleEnergy(string? path)
    {
        _path = path;
        if (path == null) return;
        try
        {
            if (File.Exists(path)) _book = JsonSerializer.Deserialize<Book>(File.ReadAllText(path)) ?? new Book();
        }
        catch (Exception ex) { CrashLog.Write("odyle load", ex); _book = new Book(); }
        _book.Characters ??= new();
    }

    static OdyleEnergy? _shared;
    static string _sharedDir = "";
    /// <summary>The tracker's own, in its folder (a self-test on a folder of its own has one of its own).</summary>
    public static OdyleEnergy Shared
    {
        get
        {
            var dir = CrashLog.Dir;
            var s = _shared;
            if (s != null && _sharedDir == dir) return s;
            lock (SharedGate)
            {
                if (_shared == null || _sharedDir != dir) { _shared = new OdyleEnergy(Path.Combine(dir, "odyle.json")); _sharedDir = dir; }
                return _shared;
            }
        }
    }
    static readonly object SharedGate = new();

    /// <summary>Bumped on every change: the meter's drawings compare it.</summary>
    public int Changes { get { lock (_gate) return _changes; } }

    /// <summary>A reading of the character in the world, <paramref name="who"/> as the tracker counts it now.</summary>
    public void Take(OdyleReading r, string who)
    {
        lock (_gate)
        {
            _book.Characters.TryGetValue(who, out var was);
            var k = new Kept { At = r.At, Natural = r.Natural, Extra = r.Extra, Why = r.Reason.ToString(), Cap = Math.Max(was?.Cap ?? BaseCap, r.Natural > BaseCap ? MemberCap : BaseCap) };
            if (was != null) { was.Before = null; k.Before = was; }
            _book.Characters[who] = k;
            _changes++;
            SaveLocked();
        }
    }

    /// <summary>
    /// The tracker now counts for <paramref name="to"/>, until now for <paramref name="from"/>: a list read in the last
    /// <see cref="SwitchWindow"/> and kept for <paramref name="from"/> was the new character's login, so it moves, and
    /// <paramref name="from"/> gets back what it had before. Before any character was seen (<paramref name="from"/> ""), every
    /// reading goes to the first one.
    /// </summary>
    public void Switched(string from, string to, DateTime now)
    {
        if (from == to) return;
        lock (_gate)
        {
            if (!_book.Characters.TryGetValue(from, out var k)) return;
            bool fresh = k.Why == nameof(OdyleReading.Why.List) && now - k.At <= SwitchWindow && k.At <= now.AddSeconds(5);
            if (from.Length > 0 && !fresh) return;
            var moved = new Kept { At = k.At, Natural = k.Natural, Extra = k.Extra, Why = k.Why, Cap = Math.Max(k.Natural > BaseCap ? MemberCap : BaseCap, _book.Characters.GetValueOrDefault(to)?.Cap ?? BaseCap) };
            if (_book.Characters.TryGetValue(to, out var had)) { had.Before = null; moved.Before = had; }
            _book.Characters[to] = moved;
            if (from.Length == 0 || k.Before == null) _book.Characters.Remove(from);
            else _book.Characters[from] = k.Before;
            _changes++;
            SaveLocked();
        }
    }

    /// <summary>The character's last reading, or null when it was never read.</summary>
    public Kept? Of(string who) { lock (_gate) return _book.Characters.TryGetValue(who, out var k) ? Copy(k) : null; }

    /// <summary>The character's Odyle Energy now: its last reading with the refills since, or null when never read.</summary>
    public int? Now(string who, DateTime now)
    {
        lock (_gate) return _book.Characters.TryGetValue(who, out var k) ? Projected(k, now) : null;
    }

    /// <summary>The refill times from <paramref name="from"/> (after) to <paramref name="to"/> (up to): 01:00, 04:00 ... 22:00 UTC.</summary>
    public static int RefillsBetween(DateTime from, DateTime to)
    {
        if (to <= from) return 0;
        // each refill, counted a minute into its hour: the game's own came 4 to 55 s into it (2026-11-17 to 19)
        static long Slot(DateTime t) => (long)Math.Floor((t.ToUniversalTime() - Epoch).TotalHours / 3.0);
        return (int)Math.Clamp(Slot(to) - Slot(from), 0, 100_000);
    }
    static readonly DateTime Epoch = new(2000, 1, 1, 1, 1, 0, DateTimeKind.Utc);   // a refill time

    /// <summary>A reading's total at <paramref name="now"/>: the natural pool topped up 15 a refill since, never past its cap.</summary>
    public static int Projected(Kept k, DateTime now)
    {
        int natural = k.Natural;
        if (natural < k.Cap) natural = Math.Min(k.Cap, natural + RefillBy * RefillsBetween(k.At, now));
        return natural + k.Extra;
    }

    static Kept Copy(Kept k) => new() { At = k.At, Natural = k.Natural, Extra = k.Extra, Why = k.Why, Cap = k.Cap, Before = k.Before == null ? null : Copy(k.Before) };

    void SaveLocked()
    {
        if (_path == null) return;
        try
        {
            var tmp = _path + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(_book, new JsonSerializerOptions { WriteIndented = true }));
            File.Move(tmp, _path, overwrite: true);
        }
        catch (Exception ex) { CrashLog.Write("odyle save", ex); }
    }
}
