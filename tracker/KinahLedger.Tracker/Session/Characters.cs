using System.Text.Json;
using KinahLedger.Ocr;

namespace KinahLedger.Session;

/// <summary>One of the player's characters: the class icons it has shown (<see cref="CharacterBadge"/>) and its server.</summary>
public sealed class Character
{
    /// <summary>"c1", "c2": the folder its numbers are kept in (characters\c1). Never changes.</summary>
    public string Id { get; set; } = "";
    /// <summary>The name the player gave it; empty until then (the window says "Character 1").</summary>
    public string Name { get; set; } = "";
    /// <summary>The name came from the nameplate (Ocr/CharacterName.cs), not typed: the reader may still correct it. A typed name is never read over.</summary>
    public bool NameRead { get; set; }
    /// <summary>The server's label when it was first seen ("Siel (Elyos)"): a class icon on another server is another character.</summary>
    public string Server { get; set; } = "";
    /// <summary>Its icon's fingerprints, a few of them: the same icon under different light (a reward screen, a glow) reads a little differently.</summary>
    public List<string> Badges { get; set; } = new();
    public DateTime FirstSeen { get; set; }
    public DateTime LastSeen { get; set; }
}

/// <summary>characters.json: every character seen on this PC and the one counted for now.</summary>
public sealed class CharacterBook
{
    public int Version { get; set; } = 1;
    /// <summary>The character counted for now; empty before the first one was seen (the numbers are then in the top folder).</summary>
    public string Current { get; set; } = "";
    public List<Character> List { get; set; } = new();
    /// <summary>The old runs have been sorted by the class icon in their pictures (once, the first time a character is seen).</summary>
    public bool Sorted { get; set; }
    /// <summary>
    /// The history kept before characters (days, bests, goals, quest points, today, runs with no picture) is with the
    /// character it belongs to: the one with the most pictures, not just the first seen (0.10.53; 0.10.51 gave it to the
    /// first seen, which on Steven's PC was an alt).
    /// </summary>
    public bool Placed { get; set; }
    /// <summary>The character that took the history over (Adopt); the place pass moves it on from there.</summary>
    public string Adopter { get; set; } = "";
}

/// <summary>
/// The numbers kept per character (Steven, 2026-11-11: "we should separate the tracker by character since people have
/// alts / different classes etc"). Today, the days, the bests, goals, quest points and the runs live in characters\&lt;id&gt;
/// beside the logs; prices, the item list, the settings and the gathering limit (per server) stay shared. Which character
/// is in the world is read off the class icon between the health and mana bars (<see cref="CharacterBadge"/>,
/// <see cref="CharacterSpotter"/>); the window shows it and lets the player name or switch it.
/// <para>Before the first character is seen the numbers stay where they always were, in the top folder; the first one
/// seen takes them over (<see cref="Adopt"/>), and the old runs are then sorted by the class icon in their pictures
/// (<see cref="CharacterSort"/>).</para>
/// </summary>
public static class Characters
{
    static readonly object Gate = new();
    static CharacterBook? _book;
    static string _bookDir = "";

    /// <summary>The files that belong to one character; everything else in the folder is shared.</summary>
    public static readonly string[] Files =
    {
        "today.json", "days.json", "best.json", "goals.json", "quests.json", "quests.bad.json", "sessions.jsonl",
        "instances.jsonl", "instances.old.jsonl", "instances.removed.jsonl", "instances.repaired.jsonl", "instances.flagged.txt",
    };

    static string BookPath => Path.Combine(CrashLog.Dir, "characters.json");
    static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    static CharacterBook Book
    {
        get
        {
            // a self-test or a replay on a folder of its own has a book of its own (none, usually)
            if (_book != null && _bookDir == CrashLog.Dir) return _book;
            _bookDir = CrashLog.Dir;
            try { _book = File.Exists(BookPath) ? JsonSerializer.Deserialize<CharacterBook>(File.ReadAllText(BookPath), Json) : null; }
            catch (Exception ex) { CrashLog.Write("characters load", ex); _book = null; }
            _book ??= new CharacterBook();
            _book.List ??= new();
            foreach (var c in _book.List) { c.Badges ??= new(); c.Name ??= ""; c.Server ??= ""; }
            return _book;
        }
    }

    static void SaveLocked()
    {
        try
        {
            var tmp = BookPath + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(Book, Json));
            File.Move(tmp, BookPath, overwrite: true);
        }
        catch (Exception ex) { CrashLog.Write("characters save", ex); }
    }

    /// <summary>Forgets the loaded book (self-tests).</summary>
    public static void Reset() { lock (Gate) { _book = null; _bookDir = ""; Votes.Clear(); } }

    /// <summary>The character counted for now, or null before the first one was seen.</summary>
    public static Character? Current { get { lock (Gate) return Book.List.FirstOrDefault(c => c.Id == Book.Current); } }

    public static List<Character> All { get { lock (Gate) return Book.List.ToList(); } }

    /// <summary>The folder the current character's numbers are in: characters\c1, or the top folder before any was seen.</summary>
    public static string DataDir
    {
        get
        {
            var c = Current;
            if (c == null) return CrashLog.Dir;
            var d = Path.Combine(CrashLog.Dir, "characters", c.Id);
            Directory.CreateDirectory(d);
            return d;
        }
    }

    public static string DirOf(Character c) => Path.Combine(CrashLog.Dir, "characters", c.Id);

    /// <summary>"Swoly", or "Character 2" before it has a name.</summary>
    public static string Label(Character c) => c.Name.Trim().Length > 0 ? c.Name.Trim() : "Character " + (int.TryParse(c.Id.TrimStart('c'), out var n) ? n : 1);

    /// <summary>How alike a fingerprint is to a character's icon: the best of its kept fingerprints.</summary>
    public static double Alike(Character c, string print) => c.Badges.Count == 0 ? 0 : c.Badges.Max(b => CharacterBadge.Alike(b, print));

    /// <summary>
    /// The character a steady icon belongs to on <paramref name="server"/>: the current one when it matches (two
    /// characters of one class can't be told apart by the icon, so the one already counted keeps counting), else the
    /// best match. Null when none does.
    /// </summary>
    public static Character? Match(string print, string server, IReadOnlyList<Character>? list = null, string? current = null)
    {
        list ??= All; current ??= Current?.Id;
        var here = list.Where(c => SameServer(c.Server, server)).ToList();
        var cur = here.FirstOrDefault(c => c.Id == current);
        if (cur != null && Alike(cur, print) >= CharacterBadge.SameIcon) return cur;
        return here.Select(c => (c, a: Alike(c, print))).Where(x => x.a >= CharacterBadge.SameIcon).OrderByDescending(x => x.a).Select(x => x.c).FirstOrDefault();
    }

    static bool SameServer(string a, string b) => a.Length == 0 || b.Length == 0 || string.Equals(a.Trim(), b.Trim(), StringComparison.OrdinalIgnoreCase);

    /// <summary>Keeps a reading of a character's icon when it adds something: less alike than 0.9 to every kept one, at most six.</summary>
    public static void Learn(Character c, string print)
    {
        lock (Gate)
        {
            var mine = Book.List.FirstOrDefault(x => x.Id == c.Id) ?? c;
            mine.LastSeen = DateTime.UtcNow;
            if (mine.Badges.Count < 6 && (mine.Badges.Count == 0 || mine.Badges.Max(b => CharacterBadge.Alike(b, print)) < 0.9)) { mine.Badges.Add(print); SaveLocked(); }
        }
    }

    /// <summary>A new character with this icon on this server (not yet the current one).</summary>
    public static Character Add(string print, string server)
    {
        lock (Gate)
        {
            int n = 1;
            while (Book.List.Any(c => c.Id == "c" + n)) n++;
            var c = new Character { Id = "c" + n, Server = server, Badges = print.Length > 0 ? new() { print } : new(), FirstSeen = DateTime.UtcNow, LastSeen = DateTime.UtcNow };
            Book.List.Add(c);
            SaveLocked();
            return c;
        }
    }

    /// <summary>Counts for this character from now on (its folder made if new).</summary>
    public static void SetCurrent(Character c)
    {
        lock (Gate)
        {
            Book.Current = c.Id;
            Directory.CreateDirectory(DirOf(c));
            SaveLocked();
        }
    }

    /// <summary>A name typed by the player: kept as it is, never read over.</summary>
    public static void Rename(Character c, string name)
    {
        lock (Gate)
        {
            var mine = Book.List.FirstOrDefault(x => x.Id == c.Id); if (mine == null) return;
            mine.Name = name.Trim().Length > 24 ? name.Trim()[..24] : name.Trim();
            mine.NameRead = false;
            SaveLocked();
        }
    }

    /// <summary>
    /// The name a set of nameplate readings settles on, or null: the most read spelling (letters' case aside) with at
    /// least <paramref name="least"/> readings and <paramref name="lead"/> times as many as the next one (twice live; 1.5
    /// over a character's old pictures, where a pet's plate, "Cube", reads now and then). The OCR's slips ("Swot", "Swoyy",
    /// "swoly" for Swoly) scatter, the name repeats; written as it was read most often.
    /// </summary>
    public static string? Settle(IEnumerable<string> reads, int least, double lead = 2)
    {
        var groups = reads.Where(r => r.Length >= 3).GroupBy(r => r, StringComparer.OrdinalIgnoreCase)
            .Select(g => (Spelling: g.GroupBy(x => x, StringComparer.Ordinal).OrderByDescending(x => x.Count()).ThenBy(x => char.IsUpper(x.Key[0]) ? 0 : 1).First().Key, N: g.Count()))
            .OrderByDescending(x => x.N).ToList();
        if (groups.Count == 0 || groups[0].N < least) return null;
        if (groups.Count > 1 && groups[0].N < groups[1].N * lead) return null;
        return groups[0].Spelling;
    }

    static readonly Dictionary<string, List<string>> Votes = new();
    public const int VotesKept = 12, VotesNeeded = 4;

    /// <summary>
    /// One reading of the nameplate for the character counted for: the name is set once the last
    /// <see cref="VotesKept"/> readings settle on one (<see cref="Settle"/>, <see cref="VotesNeeded"/>), unless the
    /// player typed one. True when the name changed.
    /// </summary>
    public static bool VoteName(Character c, string read)
    {
        lock (Gate)
        {
            var mine = Book.List.FirstOrDefault(x => x.Id == c.Id); if (mine == null) return false;
            if (mine.Name.Length > 0 && !mine.NameRead) return false;
            if (!Votes.TryGetValue(mine.Id, out var v)) Votes[mine.Id] = v = new();
            v.Add(read); if (v.Count > VotesKept) v.RemoveAt(0);
            return Settle(v, VotesNeeded) is { } name && SetReadLocked(mine, name);
        }
    }

    /// <summary>A name read for a character (from its pictures or the screen), unless the player typed one. True when it changed.</summary>
    public static bool SetReadName(Character c, string name)
    {
        lock (Gate)
        {
            var mine = Book.List.FirstOrDefault(x => x.Id == c.Id);
            return mine != null && !(mine.Name.Length > 0 && !mine.NameRead) && SetReadLocked(mine, name);
        }
    }

    static bool SetReadLocked(Character mine, string name)
    {
        if (string.Equals(mine.Name, name, StringComparison.Ordinal)) return false;
        CrashLog.Note($"characters: {Label(mine)} is {name} (read off the nameplate)");
        mine.Name = name; mine.NameRead = true;
        SaveLocked();
        return true;
    }

    /// <summary>Whether the nameplate is worth reading for this character: no name yet, or one read (it may still be corrected); never a typed one.</summary>
    public static bool WantsName(Character c) => c.Name.Length == 0 || c.NameRead;
    public static bool HasReadName(Character c) => c.Name.Length > 0 && c.NameRead;

    /// <summary>Marks the old runs as sorted (CharacterSort ran, or had nothing to do).</summary>
    public static void MarkSorted() { lock (Gate) { Book.Sorted = true; Book.Placed = true; SaveLocked(); } }
    public static bool Sorted { get { lock (Gate) return Book.Sorted; } }

    /// <summary>The kept history still waits for the place pass: characters were seen, and it has not run (a 0.10.51 or 0.10.52 install, or a first character just adopted).</summary>
    public static bool NeedsPlacing { get { lock (Gate) return Book.List.Count > 0 && !Book.Placed; } }

    /// <summary>
    /// The character that took the history over: the one the book says, else (a 0.10.51 book) the one whose folder holds
    /// days.json or today.json, else the current one.
    /// </summary>
    public static Character? Adopter
    {
        get
        {
            lock (Gate)
            {
                var named = Book.List.FirstOrDefault(c => c.Id == Book.Adopter);
                if (named != null) return named;
                return Book.List.FirstOrDefault(c => File.Exists(Path.Combine(DirOf(c), "days.json")) || File.Exists(Path.Combine(DirOf(c), "today.json")))
                    ?? Book.List.FirstOrDefault(c => c.Id == Book.Current);
            }
        }
    }

    /// <summary>
    /// The first character seen takes over the numbers kept so far: each of <see cref="Files"/> in the top folder moves
    /// into its folder (one already there is kept, and the top one renamed .before-characters). The caller holds the
    /// readers still while this runs (the window does it between saving and loading).
    /// </summary>
    public static void Adopt(Character c)
    {
        var dir = DirOf(c);
        Directory.CreateDirectory(dir);
        foreach (var f in Files)
        {
            var from = Path.Combine(CrashLog.Dir, f);
            if (!File.Exists(from)) continue;
            var to = Path.Combine(dir, f);
            try
            {
                if (File.Exists(to)) File.Move(from, from + ".before-characters", overwrite: true);
                else File.Move(from, to);
            }
            catch (Exception ex) { CrashLog.Write("characters adopt " + f, ex); }
        }
        CrashLog.Note($"characters: {Label(c)} takes over the numbers kept so far ({dir})");
        lock (Gate) { Book.Adopter = c.Id; }
        SetCurrent(c);
    }
}

/// <summary>
/// Which character is in the world, from the class icon read every few seconds (GameWatcher). An icon counts once
/// <see cref="Steady"/> readings in a row agree (a reward screen or a loading screen gives none, which neither breaks
/// nor adds to the run); a character not seen before needs <see cref="SteadyNew"/>. Pure logic, for the self-tests.
/// </summary>
public sealed class CharacterSpotter
{
    public const int Steady = 3, SteadyNew = 6;

    readonly List<string> _run = new();
    /// <summary>The last steady icon (the one on screen now, as far as is known).</summary>
    public string? Print { get; private set; }
    public DateTime? PrintAt { get; private set; }

    public enum Kind { Same, Switch, New }
    public readonly record struct Verdict(Kind Kind, Character? Character, string Print);

    /// <summary>
    /// One reading (null: no medallion this time). A verdict once the icon is steady: the current character (Same), another
    /// known one (Switch), or none known (New, once it has held <see cref="SteadyNew"/> readings).
    /// </summary>
    public Verdict? Observe(DateTime t, string? print, string server, IReadOnlyList<Character> list, string? current)
    {
        if (print == null) return null;
        if (_run.Count > 0 && CharacterBadge.Alike(_run[^1], print) < CharacterBadge.SameIcon) _run.Clear();
        _run.Add(print);
        if (_run.Count > SteadyNew) _run.RemoveAt(0);
        if (_run.Count < Steady) return null;
        Print = print; PrintAt = t;
        // a character with no icon yet (split off by hand before the icon was read) takes the one on screen
        if (list.FirstOrDefault(c => c.Id == current) is { Badges.Count: 0 } bare) return new Verdict(Kind.Same, bare, print);
        var match = Characters.Match(print, server, list, current);
        if (match != null) return new Verdict(match.Id == current ? Kind.Same : Kind.Switch, match, print);
        return _run.Count >= SteadyNew ? new Verdict(Kind.New, null, print) : null;
    }

    /// <summary>Starts over (the game closed, or the player picked a character by hand).</summary>
    public void Clear() { _run.Clear(); Print = null; PrintAt = null; }
}
