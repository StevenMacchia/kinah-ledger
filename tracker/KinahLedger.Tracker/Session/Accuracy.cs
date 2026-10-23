using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace KinahLedger.Session;

/// <summary>
/// How well reading went, counted across the whole time the tracker runs (Steven, 2026-10-23: "reading-health numbers").
/// Every chat read, every pickup and every name the reader had to guess adds to one of these counters; after each run is
/// shared, what added up since the last accepted send goes to kinahledger.com/v1/accuracy with the app version and the
/// install ID, and starts over. The site adds them up per version, which is what holds back a staged update whose
/// reading is worse than the last one's. Nothing here is chat text or a name of anyone: the counters are numbers, the
/// names are item names as they were read (checked by <see cref="SafeName"/>), the layout is fractions of the window.
/// </summary>
public static class Accuracy
{
    /// <summary>The counters, by the names the site reads (see the protocol in the backend README).</summary>
    public static readonly string[] Keys =
    {
        "reading_s", "chat_reads", "unaligned", "pickups", "guessed", "held", "snapped", "unknown", "list_only",
        "list_matched", "chat_lost_s", "craft_batches", "craft_panel", "rare_seen", "rare_second_read", "rare_confirmed",
    };

    static readonly object Lock = new();
    static readonly Dictionary<string, long> Counts = new();
    static double _readingS, _chatLostS;
    /// <summary>Names read, by (read as normalised, item key or "", how): how many times.</summary>
    static readonly Dictionary<(string Read, string Item, string How), int> Names = new();
    static LayoutReport? _layout;
    static bool _layoutSent;
    public const int MaxNames = 50, MaxNameKeys = 400;

    public static void Add(string key, long n = 1)
    {
        if (n <= 0 || Muted) return;
        lock (Lock) Counts[key] = Counts.GetValueOrDefault(key) + n;
    }

    public static void AddReading(double seconds) { if (seconds > 0 && seconds < 600) lock (Lock) _readingS += seconds; }
    public static void AddChatLost(double seconds) { if (seconds > 0 && seconds < 600) lock (Lock) _chatLostS += seconds; }

    public static long Get(string key) { lock (Lock) return key == "reading_s" ? (long)_readingS : key == "chat_lost_s" ? (long)_chatLostS : Counts.GetValueOrDefault(key); }

    /// <summary>
    /// A name the reader didn't read exactly: snapped to an item by a close match, guessed as its closest item, or not
    /// an item at all. Kept only when it looks like an item name (<see cref="SafeName"/>); anything else stays here.
    /// </summary>
    /// <summary>Set around a rare drop's second read: its look-ups are not the reader's own misreads.</summary>
    [ThreadStatic] public static bool Muted;

    public static void Name(string read, long? item, string how)
    {
        if (Muted) return;
        var norm = SafeName(read);
        if (norm == null) return;
        var key = (norm, item is long id && id > 0 ? id.ToString() : "", how);
        lock (Lock)
        {
            if (!Names.ContainsKey(key) && Names.Count >= MaxNameKeys) return;
            Names[key] = Names.GetValueOrDefault(key) + 1;
        }
    }

    static readonly Regex NameShape = new(@"^[A-Za-z0-9][A-Za-z0-9 '\-(),]*$", RegexOptions.Compiled);

    /// <summary>
    /// The read as it may leave the PC, or null: letters, digits, spaces and ' - ( ) , only, 3 to 48 characters, at most
    /// seven words, no brackets of a chat name ("[Name]"), no colon (a channel or a player's message), no "http". The
    /// parser only ever hands item names here; this makes sure nothing else could slip out.
    /// </summary>
    public static string? SafeName(string read)
    {
        var s = Regex.Replace(read ?? "", @"\s+", " ").Trim();
        if (s.Length < 3 || s.Length > 48) return null;
        if (!NameShape.IsMatch(s)) return null;
        if (s.Split(' ').Length > 7) return null;
        if (s.Contains("http", StringComparison.OrdinalIgnoreCase) || s.Contains("www", StringComparison.OrdinalIgnoreCase)) return null;
        if (s.Count(char.IsLetter) < 3) return null;
        return s.ToLowerInvariant();
    }

    /// <summary>Where the chat (and the other regions) were found, once per session; sent with the next report.</summary>
    public static void Layout(LayoutReport l)
    {
        lock (Lock)
        {
            // a later report of the same session adds what it learned (the loot list showed rows, the panel was seen)
            if (_layout != null && _layout.W == l.W && _layout.H == l.H)
                l = l with { Chat = l.Chat ?? _layout.Chat, ChatHow = l.Chat != null ? l.ChatHow : _layout.ChatHow, LootOk = l.LootOk || _layout.LootOk, CraftOk = l.CraftOk || _layout.CraftOk, Ui = l.Ui > 0 ? l.Ui : _layout.Ui };
            if (_layout == l) return;
            _layout = l; _layoutSent = false;
        }
    }

    public static LayoutReport? CurrentLayout { get { lock (Lock) return _layout; } }

    /// <summary>What added up since the last accepted send, taken out (put it back with <see cref="Restore"/> if the send fails).</summary>
    public static AccuracyReport Take()
    {
        lock (Lock)
        {
            var health = new Dictionary<string, long>();
            foreach (var k in Keys)
            {
                long v = k == "reading_s" ? (long)_readingS : k == "chat_lost_s" ? (long)_chatLostS : Counts.GetValueOrDefault(k);
                if (v > 0) health[k] = Math.Min(v, 10_000_000);
            }
            // the whole seconds go out; the part of a second left over stays for the next report
            _readingS -= Math.Floor(_readingS);
            _chatLostS -= Math.Floor(_chatLostS);
            Counts.Clear();
            var names = Names.OrderByDescending(kv => kv.Value).Take(MaxNames)
                .Select(kv => new NameReport(kv.Key.Read, kv.Key.Item.Length > 0 ? kv.Key.Item : null, kv.Key.How, kv.Value)).ToList();
            foreach (var n in names) Names.Remove((n.read, n.item ?? "", n.how));
            var layout = _layoutSent ? null : _layout;
            _layoutSent = true;
            return new AccuracyReport(health, names, layout);
        }
    }

    /// <summary>A report that didn't reach the site goes back in, so the next send carries it.</summary>
    public static void Restore(AccuracyReport r)
    {
        lock (Lock)
        {
            foreach (var (k, v) in r.Health)
            {
                if (k == "reading_s") _readingS += v;
                else if (k == "chat_lost_s") _chatLostS += v;
                else Counts[k] = Counts.GetValueOrDefault(k) + v;
            }
            foreach (var n in r.Names) { var key = (n.read, n.item ?? "", n.how); Names[key] = Names.GetValueOrDefault(key) + n.n; }
            if (r.Layout != null && r.Layout == _layout) _layoutSent = false;
        }
    }

    /// <summary>For tests: everything back to zero.</summary>
    internal static void Reset() { lock (Lock) { Counts.Clear(); Names.Clear(); _readingS = 0; _chatLostS = 0; _layout = null; _layoutSent = false; } }
}

/// <summary>One name read as an item: snapped, guessed or unknown (no item), and how often.</summary>
public sealed record NameReport(string read, string? item, string how, int n);

/// <summary>Where the game's parts sat, keyed by the window size and the UI's text size (see LayoutProfile).</summary>
public sealed record LayoutReport(int W, int H, int Ui, double[]? Chat, string ChatHow, bool LootOk, bool CraftOk, string Fracs)
{
    public bool Equals(LayoutReport? o) => o is not null && W == o.W && H == o.H && Ui == o.Ui && ChatHow == o.ChatHow && LootOk == o.LootOk && CraftOk == o.CraftOk && Fracs == o.Fracs
        && ((Chat == null && o.Chat == null) || (Chat != null && o.Chat != null && Chat.SequenceEqual(o.Chat)));
    public override int GetHashCode() => HashCode.Combine(W, H, Ui, ChatHow, LootOk, CraftOk, Fracs);
}

public sealed record AccuracyReport(Dictionary<string, long> Health, List<NameReport> Names, LayoutReport? Layout)
{
    public bool Empty => Health.Count == 0 && Names.Count == 0 && Layout == null;
}

/// <summary>Exactly what goes to /v1/accuracy: the app version, the install ID, the counters, the names, the layout.</summary>
public sealed record AccuracyPayload(
    string app, string install, Dictionary<string, long> health,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] List<NameReport>? names,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] LayoutPayload? layout)
{
    public static AccuracyPayload From(AccuracyReport r, string install) => new(
        Upload.RunPayload.AppVersion, install, r.Health, r.Names.Count > 0 ? r.Names : null,
        r.Layout is { } l ? new LayoutPayload(l.W, l.H, l.Ui, l.Chat?.Select(v => Math.Round(v, 4)).ToArray(), l.ChatHow, l.LootOk, l.CraftOk, l.Fracs) : null);
}

public sealed record LayoutPayload(int w, int h, int ui, double[]? chat, string chat_how, bool loot_ok, bool craft_ok, string fracs);
