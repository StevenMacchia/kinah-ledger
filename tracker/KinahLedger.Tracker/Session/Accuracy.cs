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
    /// <summary>Names settled per candidate set (NameMatch sources), since the tracker started; never sent.</summary>
    static readonly Dictionary<string, int> SourceCounts = new();
    public static Dictionary<string, int> Sources() { lock (Lock) return new(SourceCounts); }
    /// <summary>
    /// Shadow readers' tallies (the name decoder: decode_agree, decode_disagree, decode_other), since the tracker started.
    /// On this PC only: never in <see cref="Keys"/>, never sent, never cleared by <see cref="Take"/>.
    /// </summary>
    static readonly Dictionary<string, long> ShadowCounts = new();
    public static void Shadow(string key) { lock (Lock) ShadowCounts[key] = ShadowCounts.GetValueOrDefault(key) + 1; }
    public static Dictionary<string, long> Shadows() { lock (Lock) return new(ShadowCounts); }
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

    /// <summary>
    /// <paramref name="source"/> says which candidate set settled the name (Data.NameMatch: target-table, zone-gather,
    /// recipe, run, full, refused). It is tallied here and written to reads.log by the caller, on this PC only: what goes to
    /// the site stays "how" (snapped, guessed, unknown), so the backend's protocol doesn't change.
    /// </summary>
    public static void Name(string read, long? item, string how, string source = "")
    {
        if (Muted) return;
        if (source.Length > 0) lock (Lock) SourceCounts[source] = SourceCounts.GetValueOrDefault(source) + 1;
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
            return new AccuracyReport(health, names, layout, MeterHealth.Take());
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
        if (r.Meter != null) MeterHealth.Restore(r.Meter);
    }

    /// <summary>For tests: everything back to zero (the meter's counts too).</summary>
    internal static void Reset() { lock (Lock) { Counts.Clear(); Names.Clear(); SourceCounts.Clear(); ShadowCounts.Clear(); SnapSeen.Clear(); _readingS = 0; _chatLostS = 0; _layout = null; _layoutSent = false; } MeterHealth.Reset(); }

    /// <summary>A misread looked up again within this long of its last look-up is the same line on screen, not a new one.</summary>
    public const double SameLineSeconds = 10;
    static readonly Dictionary<string, DateTime> SnapSeen = new(StringComparer.Ordinal);
    /// <summary>The time a look-up happens at (the tests set it).</summary>
    internal static Func<DateTime> Clock = () => DateTime.UtcNow;

    /// <summary>
    /// A misread that snapped to an item (ItemDictionary.IdOf, ChatParser's context match), counted once per line, not per
    /// look-up (2026-10-25: one report said "snapped 3586", all of them "expertls splendent orichalcum longsword x", the
    /// crafting panel's recipe read once and then looked up by the window on every refresh for 15 minutes; the same chat
    /// line parsed again on every pass did the same). The same misread looked up again within <see cref="SameLineSeconds"/>
    /// of its last look-up is the line still on screen; one looked up after a pause that long is a new line.
    /// </summary>
    public static void Snapped(string read, long item, string source)
    {
        if (Muted) return;
        var key = Regex.Replace((read ?? "").ToLowerInvariant(), @"\s+", " ").Trim();
        var now = Clock();
        lock (Lock)
        {
            bool again = SnapSeen.TryGetValue(key, out var last) && (now - last).TotalSeconds < SameLineSeconds && (last - now).TotalSeconds < 5;
            SnapSeen[key] = now;
            if (SnapSeen.Count > 2000) foreach (var old in SnapSeen.Where(kv => (now - kv.Value).TotalSeconds >= SameLineSeconds).Select(kv => kv.Key).ToList()) SnapSeen.Remove(old);
            if (again) return;
        }
        Name(read ?? "", item, "snapped", source);
        Add("snapped");
    }
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

public sealed record AccuracyReport(Dictionary<string, long> Health, List<NameReport> Names, LayoutReport? Layout, MeterPayload? Meter = null)
{
    public bool Empty => Health.Count == 0 && Names.Count == 0 && Layout == null && Meter == null;
}

/// <summary>Exactly what goes to /v1/accuracy: the app version, the install ID, the counters, the names, the layout, the meter's counts.</summary>
public sealed record AccuracyPayload(
    string app, string install, Dictionary<string, long> health,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] List<NameReport>? names,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] LayoutPayload? layout,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] MeterPayload? meter = null)
{
    /// <param name="app">The version that took the report (a kept report goes out under the version that read it); this one by default.</param>
    public static AccuracyPayload From(AccuracyReport r, string install, string? app = null) => new(
        app ?? Upload.RunPayload.AppVersion, install, r.Health, r.Names.Count > 0 ? r.Names : null,
        r.Layout is { } l ? new LayoutPayload(l.W, l.H, l.Ui, l.Chat?.Select(v => Math.Round(v, 4)).ToArray(), l.ChatHow, l.LootOk, l.CraftOk, l.Fracs) : null,
        r.Meter);
}

/// <summary>
/// The "meter" object of a health report (1.0.22): one session's counts since the last accepted report, by the names the
/// site reads (backend meter_day). <paramref name="reader"/> is one of <see cref="MeterHealth.ReaderWords"/>; every other
/// value is a count, 0 to <see cref="MeterHealth.Max"/> (the 0/1 ones say whether it happened at all). Never a name or an id.
/// </summary>
public sealed record MeterPayload(string reader, long reader_on_s, long self_known, long self_wait_s, long self_conns, long conns,
    long own_hits, long other_hits, long fights_own, long fights_none, long self_check_shown, long fullscreen,
    long passes, long backup_passes, long pass_ms);

/// <summary>
/// Whether the DPS meter works on this PC (Steven, 2026-11-18: "does it work with everyone? ... that's the biggest problem"):
/// the game reader's state, whether the player's own fighter was known and how long that took on each connection, the
/// hit rows of the player and of others, fights with and without the player's damage, the self-check line and exclusive
/// fullscreen seen, and the screen passes. Each is added from the thread that saw it (the reader's, the session's, the
/// window's 3-second look), a few additions under a lock (a hit row an Interlocked add); <see cref="Accuracy.Take"/> takes
/// them with the health counters and <see cref="Accuracy.Restore"/> puts them back, so they start over exactly when health
/// does, and with sharing off they are let go with it. Counts only: no names, no ids, no chat.
/// </summary>
public static class MeterHealth
{
    /// <summary>The keys of <see cref="MeterPayload"/>, in order, as the site reads them.</summary>
    public static readonly string[] Keys =
    {
        "reader", "reader_on_s", "self_known", "self_wait_s", "self_conns", "conns", "own_hits", "other_hits",
        "fights_own", "fights_none", "self_check_shown", "fullscreen", "passes", "backup_passes", "pass_ms",
    };
    /// <summary>The game reader's state words (Ui/MainWindow.MeterBlock.cs, MeterBlocks.ReaderWord).</summary>
    public static readonly string[] ReaderWords = { "on", "off", "no_npcap", "admins_only", "no_server", "failed" };
    /// <summary>The backend's COUNTER_MAX: no value goes out above it.</summary>
    public const long Max = 10_000_000;

    static readonly object Lock = new();
    static string? _reader;
    static double _readerOnS, _selfWaitS, _passMs;
    static long _selfConns, _conns, _fightsOwn, _fightsNone, _passes, _backupPasses;
    static long _ownHits, _otherHits;
    static bool _selfKnown, _selfCheckShown, _fullscreen;

    /// <summary>
    /// The reader's state as the window last told it apart (on, off, no_npcap, admins_only, no_server, failed). Null (a
    /// wait for the game or its server, nothing told yet) keeps the last word: a report goes out with the state last known.
    /// </summary>
    public static void Reader(string? word) { if (word != null && Array.IndexOf(ReaderWords, word) >= 0) lock (Lock) _reader = word; }
    public static string? ReaderNow { get { lock (Lock) return _reader; } }

    /// <summary>Seconds the reader was on, from its few-second tick (a gap over two minutes is a sleep, not reading).</summary>
    public static void ReaderOn(double seconds) { if (seconds > 0 && seconds <= 120) lock (Lock) _readerOnS += seconds; }
    /// <summary>A new game server connection the reader follows.</summary>
    public static void Connection() { lock (Lock) _conns++; }
    /// <summary>The player's own id is known now (the reader's once-a-second look).</summary>
    public static void SelfSeen() { lock (Lock) _selfKnown = true; }
    /// <summary>The player's own id became known on this connection, <paramref name="waitSeconds"/> after its first data.</summary>
    public static void SelfFound(double waitSeconds)
    {
        lock (Lock) { _selfKnown = true; _selfConns++; _selfWaitS += Math.Clamp(double.IsFinite(waitSeconds) ? waitSeconds : 0, 0, 24 * 3600); }
    }
    /// <summary>One hit row with damage: the player's own, or another fighter's.</summary>
    public static void Hit(bool own) { if (own) Interlocked.Increment(ref _ownHits); else Interlocked.Increment(ref _otherHits); }
    /// <summary>A fight the parser ended (a boss pull, an Always on tally): with the player's damage, or others' only. No damage at all counts nothing.</summary>
    public static void Fight(bool own, bool others) { if (!own && !others) return; lock (Lock) { if (own) _fightsOwn++; else _fightsNone++; } }
    /// <summary>The "Your own hits aren't showing yet" line went on screen (the Parser page or the Overlays menu).</summary>
    public static void SelfCheckShown() { lock (Lock) _selfCheckShown = true; }
    /// <summary>The game was seen in exclusive fullscreen (the Borderless card).</summary>
    public static void FullscreenSeen() { lock (Lock) _fullscreen = true; }
    /// <summary>One screen-read pass of <paramref name="ms"/>, <paramref name="backup"/> while the screen was the game reader's backup.</summary>
    public static void Pass(double ms, bool backup)
    {
        if (!(ms >= 0 && ms < 600_000)) return;
        lock (Lock) { _passes++; if (backup) _backupPasses++; _passMs += ms; }
    }

    static long C(double v) => (long)Math.Clamp(Math.Floor(v), 0, Max);
    static long C(long v) => Math.Clamp(v, 0, Max);
    static long B(bool v) => v ? 1 : 0;

    /// <summary>
    /// The counts since the last take, taken out (whole seconds and ms go, the part left over stays), or null when the
    /// reader's state was never told (no window watched the game): then nothing goes and the counts start over all the same.
    /// </summary>
    internal static MeterPayload? Take()
    {
        lock (Lock)
        {
            long own = Interlocked.Exchange(ref _ownHits, 0), other = Interlocked.Exchange(ref _otherHits, 0);
            var m = _reader == null ? null : new MeterPayload(_reader, C(_readerOnS), B(_selfKnown), C(_selfWaitS), C(_selfConns), C(_conns),
                C(own), C(other), C(_fightsOwn), C(_fightsNone), B(_selfCheckShown), B(_fullscreen), C(_passes), C(_backupPasses), C(_passMs));
            _readerOnS -= Math.Floor(_readerOnS); _selfWaitS -= Math.Floor(_selfWaitS); _passMs -= Math.Floor(_passMs);
            _selfConns = _conns = _fightsOwn = _fightsNone = _passes = _backupPasses = 0;
            _selfKnown = _selfCheckShown = _fullscreen = false;
            return m;
        }
    }

    /// <summary>A report that didn't reach the site: its counts go back in (the reader's word stays the one known now).</summary>
    internal static void Restore(MeterPayload m)
    {
        lock (Lock)
        {
            _reader ??= m.reader;
            _readerOnS += m.reader_on_s; _selfWaitS += m.self_wait_s; _passMs += m.pass_ms;
            _selfConns += m.self_conns; _conns += m.conns; _fightsOwn += m.fights_own; _fightsNone += m.fights_none;
            _passes += m.passes; _backupPasses += m.backup_passes;
            Interlocked.Add(ref _ownHits, m.own_hits); Interlocked.Add(ref _otherHits, m.other_hits);
            _selfKnown |= m.self_known > 0; _selfCheckShown |= m.self_check_shown > 0; _fullscreen |= m.fullscreen > 0;
        }
    }

    /// <summary>For tests: everything back to zero, the reader's word unknown.</summary>
    internal static void Reset() { lock (Lock) { Take(); _reader = null; _readerOnS = _selfWaitS = _passMs = 0; } }
}

public sealed record LayoutPayload(int w, int h, int ui, double[]? chat, string chat_how, bool loot_ok, bool craft_ok, string fracs);
