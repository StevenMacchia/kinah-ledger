using System.Drawing;
using System.Text.Json;
using System.Text.RegularExpressions;
using KinahLedger.Data;
using KinahLedger.Ocr;
using KinahLedger.Upload;

namespace KinahLedger.Session;

/// <summary>
/// One farming run: find the chat (or use a chosen area), read, parse, count, track the zone, and at the end optionally share.
/// The Market reader is not part of a run; it lives in <see cref="GameWatcher"/> and runs whenever the window is open.
/// </summary>
public sealed class TrackerSession
{
    readonly Settings _settings;
    readonly ItemDictionary _items;
    readonly ChatParser _parser;
    /// <summary>The chat's enlarging factor, sized to its text on this screen (ocr.autoScale); kept across re-found chats.</summary>
    readonly TextScale? _textScale;
    public TextScale? TextScale => _textScale;
    public ChatParser Parser => _parser;
    /// <summary>Each item grade's colour as printed on this PC (GradeColours), kept beside the logs.</summary>
    static string ColoursPath => Path.Combine(CrashLog.Dir, "grade-colours.json");
    int _coloursSaved;

    /// <summary>
    /// The open-world enemies being fought now: the target bar's (read in the last 15 s) and the nameplates around the
    /// character. Nothing inside events and expeditions, where loot comes from chests and rewards no table lists.
    /// </summary>
    IReadOnlyCollection<string> Fighting()
    {
        if (ZoneLocator.KindOf(Zone) != null) return Array.Empty<string>();
        var list = Engaged.ToList();
        if (Target.Name is { } tn && (DateTime.UtcNow - Target.NameAt).TotalSeconds <= 15) list.Add(tn);
        return list.Distinct().ToList();
    }

    /// <summary>A reader for the chat at <paramref name="r"/>: sized to its text, its captures handed to the miscount report.</summary>
    ScreenReader NewReader(System.Drawing.Rectangle r) =>
        new(r, _settings.Ocr.Scale, _textScale) { Tap = b => Report.Picture("chat", b, r.Location, DateTime.UtcNow), Keep = b => KeepChatShot(b, r.Location) };

    // ---- rare drops: held for a second read of the picture they came from (RareGate) ----
    readonly RareGate _rare = new();
    public RareGate Rare => _rare;
    // the last chat capture, for a rare pickup's second read and its proof picture; this PC only, replaced every read
    System.Drawing.Bitmap? _chatShot; System.Drawing.Point _chatShotAt;
    readonly object _chatShotLock = new();
    void KeepChatShot(System.Drawing.Bitmap b, System.Drawing.Point at)
    {
        // the reader hands the capture over once read (no copy); the one before it goes
        lock (_chatShotLock) { _chatShot?.Dispose(); _chatShot = b; _chatShotAt = at; }
    }
    /// <summary>A world boss is being fought or looted now: its gear counts as rare.</summary>
    bool BossNow => _boss != null || (Instances.Open is { } open && WorldBossOf(open, _learnedBosses) != null);
    bool IsRare(long id) => RareGate.IsRare(_items, id, BossNow);
    /// <summary>Rare pickups: counted, read a second time, and confirmed (for the audit line).</summary>
    public int RareCounted { get; private set; }
    public int RareConfirmed { get; private set; }

    /// <summary>The last minute of reads, pickups and (when the player said yes) pictures, for "Report a miscount".</summary>
    public ReportBuffer Report { get; } = new();

    /// <summary>
    /// Saves the last minute (ReportBuffer) with the tracker's state to a new folder under reports/ and returns it.
    /// <paramref name="why"/> is what the player flagged, or "" for the button.
    /// </summary>
    public string WriteReport(string why)
    {
        var now = DateTime.UtcNow;
        var t = Engine.Totals();
        var bc = _balanceCheck;
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"Kinah Ledger {Upload.RunPayload.AppVersion} · miscount report · {now.ToLocalTime():yyyy-MM-dd HH:mm:ss}");
        if (why.Length > 0) sb.AppendLine($"Flagged: {why}");
        var fighting = string.Join(", ", Fighting());
        var factor = _reader?.Factor ?? _settings.Ocr.Scale;
        var textPx = _textScale?.TextPx is double px ? $"{px:0} px tall" : "height not measured yet";
        sb.AppendLine($"Zone: {Zone ?? "unknown"} · fighting: {(fighting.Length > 0 ? fighting : "nobody seen")} · target bar: {Target.Name ?? "none"}");
        sb.AppendLine($"Chat area: {Region} ({(ManualRegion ? "chosen by hand" : "found by itself")}) · read at {factor:0.##}x · text {textPx}");
        sb.AppendLine($"Counted this session: {t.Kinah:N0} Kinah · {t.Kills} kills · {Engine.Items.Values.Sum()} drops");
        sb.AppendLine($"Checks: Kinah counter agreed {bc.Agreed}×, over {bc.Over:N0}, under {bc.Under:N0} · quest kills {QuestKillsGame}, counted {QuestKillsCounted} · loot list added {_cross.Added}, matched {_cross.Matched}, chat repeats skipped {_cross.Duplicates}");
        sb.AppendLine($"Names: settled by colour {_parser.ColourPicks}, by loot table {_parser.TablePicks}, guesses no table backs {_parser.HeldUnlisted}, loot list rows named by icon {IconNamed}, lines changed by the vote {_voter.Changed}, reads that didn't line up {_tracker.Unaligned}");
        if (_parser.Colours is { } book) sb.AppendLine("Grade colours learned: " + (book.Learned() is { Count: > 0 } l ? string.Join(", ", l.Select(g => $"{g.Grade} {g.Colour.R},{g.Colour.G},{g.Colour.B} ({g.Seen})")) : "none yet"));
        sb.AppendLine($"Rare drops: {RareCounted} counted, {RareConfirmed} read exactly or confirmed by a second read; {_rare.Waiting} waiting · aliases loaded {_items.AliasCount} · remote config {Update.RemoteConfig.Current.Id}");
        sb.AppendLine($"Settings: autoScale {_settings.Ocr.AutoScale} (target {_settings.Ocr.TargetTextPx} px, scale {_settings.Ocr.Scale}) · nameColours {_settings.Ocr.NameColours} · lootIcons {_settings.Ocr.LootIcons} · window capture {WindowCapture.Active} · pictures kept {ReportBuffer.KeepFrames}");
        IEnumerable<string> tail = Array.Empty<string>();
        try { if (File.Exists(CrashLog.ReadsPath)) tail = File.ReadLines(CrashLog.ReadsPath).TakeLast(600).ToList(); } catch (Exception) { /* the log is being written: the report goes without it */ }
        var dir = Report.Write(CrashLog.Dir, sb.ToString(), tail, now);
        // where the game window sat, so the pictures' places can be made relative to it (AccuracyClient.BuildFrames)
        if (GameRect is { } gr) try { File.WriteAllText(Path.Combine(dir, "game.json"), JsonSerializer.Serialize(new { x = gr.X, y = gr.Y, w = gr.Width, h = gr.Height })); } catch (Exception) { }
        CrashLog.Note($"miscount report saved: {dir}" + (why.Length > 0 ? $" ({why})" : ""));
        return dir;
    }

    /// <summary>Saves the grade colours when names taught it something since the last save.</summary>
    void SaveColours()
    {
        if (_parser.Colours is not { } book || book.Observed == _coloursSaved) return;
        _coloursSaved = book.Observed;
        book.Save(ColoursPath);
    }
    readonly LineTracker _tracker = new();
    readonly CancellationTokenSource _cts = new();
    readonly int _interval;
    ScreenReader? _reader;
    Task? _loop;
    DateTime _zoneAt = DateTime.MinValue;

    public SessionEngine Engine { get; private set; }
    /// <summary>
    /// What the window shows unless the player keeps the tables across zones: the same events, started over whenever the
    /// zone changes (and when a run starts). Only shown, never shared; what is shared comes from the other counts, split by zone.
    /// </summary>
    public SessionEngine ZoneView { get; private set; }
    /// <summary>
    /// The Crafting part of the window: the same events, never rotated and never reset by a zone change, so the crafting
    /// ledger runs for as long as the tracker does (or until the player starts it over). Only shown, never shared.
    /// </summary>
    public SessionEngine CraftView { get; private set; }
    /// <summary>Runs in expeditions and events, entry to last loot, kept across restarts.</summary>
    public InstanceLog Instances { get; } = new();
    public void ResetCraftView() => CraftView = new SessionEngine(_items);
    /// <summary>Starts the zone view over in the zone the player is in now.</summary>
    public void ResetZoneView() { var z = new SessionEngine(_items); z.SetZone(Engine.Zone); ZoneView = z; }

    /// <summary>The zone view's stay, offered as the zone's best hour on this server (kept only past ten minutes and a better rate).</summary>
    public void OfferBest() { try { BestRuns.Offer(Server, ZoneView); } catch (Exception ex) { CrashLog.Write("best runs", ex); } }
    /// <summary>The target bar at the top-centre: which enemy is being fought, for Kinah by enemy (a best guess).</summary>
    public TargetReader Target { get; } = new();
    /// <summary>Why the target bar can't be read right now ("the panel sits over it"); empty while it is read.</summary>
    public string TargetNote { get; private set; } = "";
    public string Kind { get; set; }
    /// <summary>A second engine fed the same events: the farming run that sits on top of the always-on reader.</summary>
    public SessionEngine? Secondary { get; set; }
    public Rectangle? Region { get; private set; }
    public bool ManualRegion { get; }
    public string? Zone => Engine.Zone;
    /// <summary>When the minimap's zone name was last read (RelogDetector: the interface is gone when this goes stale).</summary>
    public DateTime ZoneSeenAt { get; private set; } = DateTime.UtcNow;
    /// <summary>Seconds spent per detected zone, so the run can be attributed to where most of it happened.</summary>
    public Dictionary<string, double> ZoneSeconds => Engine.ZoneSnapshot().Where(kv => kv.Key != SessionEngine.UnknownZone).ToDictionary(kv => kv.Key, kv => kv.Value.Seconds);
    public long Reads { get; private set; }
    public int Unaligned => _tracker.Unaligned;
    public int Empty => _tracker.Empty;
    public string LastError { get; private set; } = "";
    public string LastLine { get; private set; } = "";
    public string DetectNote { get; private set; } = "";
    /// <summary>What the last zone read saw when it found no zone on the list ("top-right text isn't a zone on the list: …"); empty once one is read.</summary>
    public string ZoneNote { get; private set; } = "";
    /// <summary>Why nothing is being read right now ("chrome covers the chat"); empty while reading.</summary>
    public string Paused { get; private set; } = "";
    public bool Running => _loop != null && !_loop.IsCompleted;
    public bool Eligible => Engine.Elapsed.TotalMinutes >= 5 && Engine.Events > 0;
    public Bitmap? Thumb => _reader?.Thumb;
    public Rectangle? GameRect { get; private set; }

    public static readonly Color ChatColor = Color.FromArgb(76, 195, 245), ZoneColor = Color.FromArgb(241, 208, 140);

    /// <summary>The whole game with the chat and map areas outlined, plus any extra marks (the Market table); falls back to the chat crop when the game window isn't known.</summary>
    public Bitmap? Frame(int maxWidth, params (Rectangle box, Color color)[] extra)
    {
        var game = GameRect ?? GameWindow.Find(_settings.ProcessHints);
        if (game == null || Region == null) return Thumb;
        GameRect = game;
        var marks = new List<(Rectangle, Color)> { (Region.Value, ChatColor), (ZoneLocator.Strip(game.Value), ZoneColor) };
        marks.AddRange(extra);
        try { return ScreenReader.Frame(game.Value, maxWidth, marks.ToArray()); }
        catch (Exception) { return Thumb; }
    }

    /// <summary>The kind plus the zone most of the run happened in, e.g. "Open world · Western Cantas Valley".</summary>
    public string Activity => ActivityOf(Engine, Kind);

    public static string ActivityOf(SessionEngine e, string kind)
    {
        // the label is shared with the run, so only a zone the tracker is sure of goes into it
        var zs = e.ZoneSnapshot().Where(kv => kv.Key != SessionEngine.UnknownZone && ZoneLocator.IsVerified(kv.Key)).ToList();
        var z = zs.Count > 0 ? zs.MaxBy(kv => kv.Value.Seconds).Key : (e.Zone is { } cur && ZoneLocator.IsVerified(cur) ? cur : null);
        return z != null ? $"{kind} · {z}" : kind;
    }

    /// <summary>Starts a fresh engine for the always-on reader and hands back the finished one, with the zone carried over.</summary>
    public SessionEngine Rotate()
    {
        var old = Engine;
        var fresh = new SessionEngine(_items);
        fresh.SetZone(old.Zone);
        Engine = fresh;
        old.Finish();
        return old;
    }

    public event Action<string>? LootLine;
    public event Action? Updated;
    /// <summary>Every event read from the chat, as it is counted: the Market reader takes the player's own sales from here.</summary>
    public event Action<GameEvent>? Parsed;

    long _kinahPicked;
    /// <summary>All tradeable Kina picked up since the reader started, across chunks: what the balance counter is checked against.</summary>
    public long KinahPicked => Interlocked.Read(ref _kinahPicked);
    long _kinahBound;
    /// <summary>The pace of reading while fighting, and how long after the last loot it lasts (measured 2026-10-22: a chat and loot list pass takes about 260 ms).</summary>
    public const int FightIntervalMs = 300;
    public const double FightSeconds = 10;
    DateTime _lastActivity = DateTime.MinValue;

    // ---- the balance counter as a check on the count ----
    readonly BalanceCheck _balanceCheck = new();
    public BalanceCheck BalanceCheck => _balanceCheck;
    /// <summary>
    /// The game's Kinah counter, read every few seconds, against the Kinah counted. A rise the count lacks, of exactly an
    /// amount the loot list showed in one read only, is that pickup: two sources agree, and it is counted. Any other gap
    /// is written to reads.log and left alone, since a sale, mail or purchase moves the counter too.
    /// </summary>
    public void CheckBalance(long balance, DateTime at)
    {
        if (Update.RemoteConfig.Current.Off("balance")) return;
        lock (_balanceHist) { if (_balanceHist.Count == 0 || _balanceHist[^1].Balance != balance) _balanceHist.Add((at, balance)); _balanceHist.RemoveAll(b => (at - b.At).TotalMinutes > 3); }
        long gap = _balanceCheck.Observe(balance, KinahPicked + Interlocked.Read(ref _kinahBound), at);
        if (gap == 0) return;
        if (gap > 0 && gap <= BalanceCheck.MaxPickupGap && _cross.TakeSingleKinah(gap, at))
        {
            var ev = new KinahChanged(at, gap, false) { Via = "loot list · balance" };
            Count(ev); Feed(ev);
            Interlocked.Add(ref _kinahPicked, gap);
            _balanceCheck.Observe(balance, KinahPicked + Interlocked.Read(ref _kinahBound), at);   // the count matches the counter again
            CrashLog.Note($"the Kinah counter rose {gap:N0} more than counted, and the loot list showed {gap:N0} Kina once: counted from the two");
            BalanceAdded++;
            try { Parsed?.Invoke(ev); } catch (Exception ex) { CrashLog.Write("parsed event", ex); }
            return;
        }
        CrashLog.Note(gap > 0
            ? $"the Kinah counter rose {gap:N0} more than counted (a missed pickup, or a sale or mail the chat didn't show); not counted"
            : $"the Kinah counter rose {-gap:N0} less than counted (Kinah spent, or a pickup counted twice)");
    }
    public int BalanceAdded { get; private set; }
    // the Kinah counter's readings of the last few minutes, for confirming a reward table's coin slot
    readonly List<(DateTime At, long Balance)> _balanceHist = new();
    /// <summary>True when the Kinah counter rose by exactly <paramref name="amount"/> between two readings within a minute of <paramref name="t"/>.</summary>
    internal bool BalanceRose(long amount, DateTime t)
    {
        lock (_balanceHist)
        {
            var near = _balanceHist.Where(b => Math.Abs((b.At - t).TotalSeconds) <= 60).OrderBy(b => b.At).ToList();
            for (int i = 0; i < near.Count; i++) for (int j = i + 1; j < near.Count; j++) if (near[j].Balance - near[i].Balance == amount) return true;
            return false;
        }
    }
    /// <summary>For tests: a Kinah counter reading.</summary>
    internal void SeedBalance(DateTime at, long balance) { lock (_balanceHist) _balanceHist.Add((at, balance)); }

    public TrackerSession(Rectangle? manualRegion, Settings s, ItemDictionary items, string kind, int? intervalMs = null)
    {
        _settings = s;
        _items = items;
        _parser = new ChatParser(items);
        // a partly hidden name is guessed among this run's items and the current enemy's loot table first
        // a partly hidden or close name is settled among this run's items and the loot tables of the enemies being fought:
        // the one on the target bar and those whose nameplates show around the character (2026-10-23)
        _parser.Likely = () => Engine.ItemRows().Select(r => r.Name)
            .Concat(new[] { Target.Name }.Concat(Engaged).Where(e => e != null).Distinct().SelectMany(e => LootBook.Current.TableOf(e!).Select(x => x.Item))).ToList();
        _parser.Fighting = Fighting;
        _parser.Rare = IsRare;
        if (s.Ocr.NameColours) _parser.Colours = GradeColours.Load(ColoursPath);
        _textScale = s.Ocr.AutoScale ? new TextScale(s.Ocr.Scale, s.Ocr.TargetTextPx) : null;
        Engine = new SessionEngine(items);
        ZoneView = new SessionEngine(items);
        _cross = new LootCrossCheck(items);
        CraftView = new SessionEngine(items);
        Kind = kind;
        Region = manualRegion;
        ManualRegion = manualRegion != null;
        _interval = Math.Clamp(intervalMs ?? s.Ocr.IntervalMs, 250, 5000);
    }

    /// <summary>Locates the chat when no area was chosen, reads the zone once, and starts the loop. Throws with a plain message when the chat can't be found.</summary>
    public async Task StartAsync()
    {
        var game = GameWindow.Find(_settings.ProcessHints);
        GameRect = game;
        if (Region == null)
        {
            if (game == null) throw new InvalidOperationException("The game window wasn't found. Start the game (borderless or windowed), or choose the chat yourself.");
            // the chat isn't needed to start (Steven, 2026-10-22: the crafting screen hides it): the crafting panel, the loot
            // list, the zone and the target bar are read without it, and the chat is looked for every ten seconds
            Rectangle? region = null; string note = "";
            if (Cover(ChatLocator.Corner(game.Value)) is null) (region, note) = await ChatLocator.DetectAsync(game.Value);
            DetectNote = note;
            if (region is { } found) LastChat.Save(game.Value, found);
            else if (LastChat.For(game.Value) is { } last) { region = last; DetectNote = "where the chat was last found (it's hidden now)"; CrashLog.Note($"chat not visible at start: reading from its last place {last}"); }
            // nothing found and nothing kept (a frameless chat has no input box): where other trackers with this screen size found it
            if (region == null && await HintedChatAsync(game.Value) is { } hinted) { region = hinted; DetectNote = "found where other trackers with this screen size found the chat"; }
            if (region == null) { CrashLog.Note("chat not in view at start: reading the crafting panel, the loot list and the zone meanwhile, and looking for the chat every 10 seconds"); _chatTryAt = DateTime.UtcNow; }
            else { Region = region; _anchor = game; }
        }
        if (Region is { } r0) _reader = NewReader(r0);
        ReportLayout();
        if (game != null && Cover(ZoneLocator.Strip(game.Value)) == null) await ReadZone(game.Value);
        _loop ??= Task.Run(() => Loop(_cts.Token));
    }

    /// <summary>
    /// The chat area other trackers found on a game window of this size (/v1/layout), when a read there shows chat
    /// lines; null otherwise. Only ever a first place to look: the reading itself is the same as anywhere.
    /// </summary>
    async Task<Rectangle?> HintedChatAsync(Rectangle game)
    {
        try
        {
            var hints = await AccuracyClient.LayoutAsync(_settings.Upload, game.Width, game.Height);
            if (hints == null || hints.Count == 0) return null;
            var best = LayoutProfile.Best(hints, 0);
            if (best == null) return null;
            var r = LayoutProfile.ToRect(best.Box, game);
            var lines = await ScreenReader.ReadBoxesAsync(r, 2, boost: true);
            if (!ChatLocator.LooksLikeChat(lines)) { CrashLog.Note($"the chat area other trackers use on {game.Width}x{game.Height} shows no chat lines here"); return null; }
            CrashLog.Note($"chat found where {best.Installs} other tracker(s) on {game.Width}x{game.Height} found it");
            return r;
        }
        catch (Exception ex) { CrashLog.Write("layout hint", ex); return null; }
    }

    string? _pendingZone; string? _unlistedTitle;

    /// <summary>Two reads of the same title, allowing the recognizer a letter or two.</summary>
    static bool Close(string a, string b)
    {
        a = a.ToLowerInvariant(); b = b.ToLowerInvariant();
        if (a == b) return true;
        int max = Math.Max(1, Math.Min(a.Length, b.Length) / 6);
        if (Math.Abs(a.Length - b.Length) > max) return false;
        var prev = new int[b.Length + 1]; var cur = new int[b.Length + 1];
        for (int j = 0; j <= b.Length; j++) prev[j] = j;
        for (int i = 1; i <= a.Length; i++)
        {
            cur[0] = i;
            for (int j = 1; j <= b.Length; j++) cur[j] = Math.Min(Math.Min(cur[j - 1] + 1, prev[j] + 1), prev[j - 1] + (a[i - 1] == b[j - 1] ? 0 : 1));
            (prev, cur) = (cur, prev);
        }
        return prev[b.Length] <= max;
    } DateTime _zoneNoteAt = DateTime.MinValue;

    async Task ReadZone(Rectangle game)
    {
        try
        {
            var (zone, note) = await ZoneLocator.DetectAsync(game);
            ZoneNote = zone == null ? note : "";
            if (zone != null) ZoneSeenAt = DateTime.UtcNow;
            if (zone != null && !ZoneLocator.IsVerified(zone))
            {
                // an unfamiliar label becomes the zone only when two reads in a row agree; a stray read (a button, a tooltip) keeps the last zone
                if (!string.Equals(_pendingZone, zone, StringComparison.Ordinal)) { _pendingZone = zone; zone = null; }
            }
            else _pendingZone = null;
            // the minimap names a place on no list, twice in a row (an expedition's inner area, an event area): from now on
            // the count goes under Unknown zone, never under the last zone that was read (Steven, 2026-10-21: refining
            // stones from an expedition landed on Eastern Cantas Valley)
            if (zone == null && ZoneLocator.LastUnlisted is { } u)
            {
                if (_unlistedTitle != null && Close(_unlistedTitle, u) && Engine.Zone != SessionEngine.UnknownZone)
                {
                    zone = SessionEngine.UnknownZone;
                    CrashLog.Note($"zone: \"{u}\" is on no list, so this counts under Unknown zone, not under {Engine.Zone ?? "the last zone"}");
                }
                _unlistedTitle = u;
            }
            else if (zone != null) _unlistedTitle = null;
            if (zone != null && zone != Engine.Zone && zone != SessionEngine.UnknownZone) CrashLog.Note($"zone: {zone}" + (ZoneLocator.KindOf(zone) is { } kind ? $" ({kind})" : ""));
            else if (zone == null && Engine.Zone == null && (DateTime.UtcNow - _zoneNoteAt).TotalSeconds >= 60) { _zoneNoteAt = DateTime.UtcNow; CrashLog.Note($"zone read: {note}"); }
            Engine.SetZone(zone);
            Secondary?.SetZone(zone);
            Instances.Zone(zone, DateTime.UtcNow);
            // a new zone starts the zone view over: its tables are this zone's alone
            if (zone != null && ZoneView.Zone != null && zone != ZoneView.Zone) { OfferBest(); var fresh = new SessionEngine(_items); fresh.SetZone(zone); ZoneView = fresh; }
            else ZoneView.SetZone(zone);
        }
        catch (Exception ex) { CrashLog.Write("zone", ex); }
        _zoneAt = DateTime.UtcNow;
    }

    int _emptyReads;

    // the game window as it was when the chat area was found (auto mode): the area is kept relative to it, so the game
    // moving (a tab out and back, a drag) moves the area with it, and the game changing size (fullscreen to windowed,
    // a resolution change) finds the chat again at once instead of after twenty empty reads
    Rectangle? _anchor;
    DateTime _refindAt = DateTime.MinValue;

    /// <summary>Keeps the chat area on the game: shifted when the game moved, found again when the game changed size.</summary>
    /// <summary>True once the chat has been found; until then the rest of the game's screen is read without it.</summary>
    public bool ChatFound => Region != null;
    DateTime _chatTryAt = DateTime.MinValue;

    /// <summary>With no chat yet, looks for it every ten seconds; found, it is read from then on and its place kept for next time.</summary>
    async Task FindChatAsync()
    {
        if (ManualRegion || (DateTime.UtcNow - _chatTryAt).TotalSeconds < 10) return;
        _chatTryAt = DateTime.UtcNow;
        if (GameWindow.Find(_settings.ProcessHints) is not { } g) return;
        GameRect = g;
        if (Cover(ChatLocator.Corner(g)) != null) return;
        var (region, note) = await ChatLocator.DetectAsync(g);
        if (region is not { } r) return;
        Region = r; _anchor = g; _reader = NewReader(r); DetectNote = note;
        _tracker.Rebase();
        LastChat.Save(g, r);
        CrashLog.Note($"chat found: {r.Width} x {r.Height} at {r.X}, {r.Y}");
        ReportLayout();
    }

    /// <summary>
    /// Where the chat was last found automatically, per game window size, in %LOCALAPPDATA%\KinahLedger\chat-last.json
    /// (relative to the game window), so a start while the chat is hidden still reads it once it shows.
    /// </summary>
    static class LastChat
    {
        static string PathOf => Path.Combine(CrashLog.Dir, "chat-last.json");

        public static void Save(Rectangle game, Rectangle chat)
        {
            try { File.WriteAllText(PathOf, System.Text.Json.JsonSerializer.Serialize(new[] { game.Width, game.Height, chat.X - game.X, chat.Y - game.Y, chat.Width, chat.Height })); }
            catch (Exception ex) { CrashLog.Write("chat last place", ex); }
        }

        public static Rectangle? For(Rectangle game)
        {
            try
            {
                if (!File.Exists(PathOf)) return null;
                var v = System.Text.Json.JsonSerializer.Deserialize<int[]>(File.ReadAllText(PathOf));
                if (v is not { Length: 6 } || v[0] != game.Width || v[1] != game.Height) return null;
                return new Rectangle(game.X + v[2], game.Y + v[3], v[4], v[5]);
            }
            catch (Exception) { return null; }
        }
    }

    /// <summary>An item picked up in a base that isn't Odyle: the only thing farmed in a base (Steven, 2026-10-22).</summary>
    bool InBaseNotOdyle(GameEvent ev) => ev is ItemAcquired a && ZoneLocator.IsBase(Zone) && !_items.NameOf(a.ItemId).Contains("Odyle", StringComparison.OrdinalIgnoreCase);

    async Task FollowGameAsync()
    {
        if (ManualRegion || _anchor is not { } a) return;
        var game = GameWindow.Find(_settings.ProcessHints);
        if (game is not { } g) return;   // minimized or gone: the cover check pauses, and the area waits where it was
        GameRect = g;
        if (g == a) return;
        if (g.Size == a.Size)
        {
            // same window, new place: the chat sits where it did inside it
            var moved = Region!.Value; moved.Offset(g.X - a.X, g.Y - a.Y);
            Region = moved; _reader = NewReader(moved); _anchor = g;
            DetectNote = "the chat area moved with the game";
            return;
        }
        if ((DateTime.UtcNow - _refindAt).TotalSeconds < 5 || Cover(ChatLocator.Corner(g)) != null) return;
        _refindAt = DateTime.UtcNow;
        var (again, note) = await ChatLocator.DetectAsync(g);
        if (again == null) return;   // the chat may be faded: the empty-read rule keeps trying
        Region = again; _reader = NewReader(again.Value); _anchor = g;
        _tracker.Rebase();
        DetectNote = "re-found after the game changed size: " + note;
        ReportLayout();
    }

    async Task Loop(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            var t0 = DateTime.UtcNow;
            bool reading = false;
            try
            {
                await FollowGameAsync();
                if (Region == null) await FindChatAsync();
                if (Region == null)
                {
                    // no chat yet: everything else on the game's screen is still read
                    if (GameWindow.Find(_settings.ProcessHints) is { } gw) GameRect = gw;
                    Paused = "";
                    var p0 = DateTime.UtcNow;
                    var off = Update.RemoteConfig.Current;
                    var list = off.Off("loot_list") ? Task.FromResult<(List<OcrLineBox>, System.Drawing.Bitmap, System.Drawing.Point)?>(null) : LootReadAsync();
                    if (!off.Off("target")) await TargetDueAsync();
                    if (!off.Off("craft_panel")) await CraftPanelDue();
                    await LootProcess(await list);
                    if (!off.Off("reward_grid")) await RewardGridDue();
                    _passes++; _passMs += (DateTime.UtcNow - p0).TotalMilliseconds;
                    Accuracy.Add("list_only", _cross.Added - _accAdded); _accAdded = _cross.Added;
                    Accuracy.Add("list_matched", _cross.Matched - _accMatched); _accMatched = _cross.Matched;
                    // the game is read, but not its chat: chat-not-found time for the health numbers
                    reading = true; Accuracy.AddChatLost((DateTime.UtcNow - t0).TotalSeconds);
                }
                // only the game is read: while it is minimized or another window sits over the chat, nothing is read and the clocks stop
                else if (Cover(Region.Value) is { } cover)
                {
                    Engine.Pause(); Secondary?.Pause(); ZoneView.Pause();
                    Paused = cover.StartsWith("the game") ? cover : cover + " covers the chat";
                    LastError = "";
                }
                else
                {
                    // the loot list is read beside the chat, not after it: a pass takes the slower of the two
                    var p0 = DateTime.UtcNow;
                    var off = Update.RemoteConfig.Current;
                    var list = off.Off("loot_list") ? Task.FromResult<(List<OcrLineBox>, System.Drawing.Bitmap, System.Drawing.Point)?>(null) : LootReadAsync();
                    await ReadOnce();
                    _chatMs += (DateTime.UtcNow - p0).TotalMilliseconds;
                    if (!off.Off("target")) await TargetDueAsync();
                    if (!off.Off("craft_panel")) await CraftPanelDue();
                    await LootProcess(await list);
                    RewardShot();
                    if (!off.Off("reward_grid")) await RewardGridDue();
                    if (!off.Off("nameplates")) await PlatesDue();
                    _passes++; _passMs += (DateTime.UtcNow - p0).TotalMilliseconds;
                    // the loot list's part in the health numbers: pickups only it saw, and rows that matched a chat line
                    Accuracy.Add("list_only", _cross.Added - _accAdded); _accAdded = _cross.Added;
                    Accuracy.Add("list_matched", _cross.Matched - _accMatched); _accMatched = _cross.Matched;
                    reading = true;
                    Audit();
                }
                if (!Update.RemoteConfig.Current.Off("zone")) await ZoneDueAsync();
            }
            catch (System.ComponentModel.Win32Exception)
            {
                // the screen can't be copied: the session is locked, a secure prompt is up, or a display is switching. Wait, like a cover.
                Engine.Pause(); Secondary?.Pause(); ZoneView.Pause();
                Paused = "the screen can't be read right now (locked, or a system prompt is up)";
                LastError = "";
            }
            catch (Exception ex) { LastError = ex.Message; CrashLog.Write("read loop", ex); }
            Updated?.Invoke();

            int spent = (int)(DateTime.UtcNow - t0).TotalMilliseconds;
            // during a fight (loot, a kill or a loot list row in the last FightSeconds) about three reads a second, so a line
            // or a row up for a moment is read more than once; idle, the usual pace, since back-to-back reading costs a CPU core
            int every = (DateTime.UtcNow - _lastActivity).TotalSeconds <= FightSeconds ? Math.Min(_interval, Update.RemoteConfig.Current.FightIntervalMs) : _interval;
            if (spent < every) { try { await Task.Delay(every - spent, ct); } catch (TaskCanceledException) { } }
            // reading time for the health numbers: the passes the game was read, wait included
            if (reading) Accuracy.AddReading((DateTime.UtcNow - t0).TotalSeconds);
        }
        SaveColours();
        Engine.Finish();
    }

    /// <summary>One read of the chat (the game is in view there): count the new lines, re-find a lost chat, re-read the zone when due.</summary>
    async Task ReadOnce()
    {
        // back in view: lines that arrived while it was covered weren't seen, so the first read is history and the clocks restart
        if (Paused.Length > 0) { Paused = ""; _tracker.Rebase(); }
        Engine.Resume(); Secondary?.Resume(); ZoneView.Resume();
        var lines = await _reader!.ReadAsync();
        _parser.NoteAccents(_reader.LastAccents);
        if (lines.Count > 0) { ReadTrace.Chat(DateTime.UtcNow, lines); Report.Lines("chat", lines, DateTime.UtcNow); QuestCounter(lines); }
        Reads++;
        Accuracy.Add("chat_reads");
        var readAt = DateTime.UtcNow;
        // chat-not-found time: from a re-find that found nothing until a read shows lines again
        if (lines.Count > 0) _chatLost = false;
        else if (_chatLost && _lastChatReadAt != DateTime.MinValue) Accuracy.AddChatLost((readAt - _lastChatReadAt).TotalSeconds);
        _lastChatReadAt = readAt;
        // the chat moved or the UI was toggled: after a stretch of empty reads, find it again (auto mode only)
        _emptyReads = lines.Count == 0 ? _emptyReads + 1 : 0;
        if (!ManualRegion && _emptyReads >= 20)
        {
            _emptyReads = 0;
            var g = GameWindow.Find(_settings.ProcessHints);
            if (g != null && Cover(ChatLocator.Corner(g.Value)) == null)
            {
                var (again, note) = await ChatLocator.DetectAsync(g.Value);
                if (again != null && again != Region) { Region = again; _reader = NewReader(again.Value); _anchor = g; DetectNote = "re-found: " + note; ReportLayout(); }
                _chatLost = again == null;
            }
        }
        ProcessChat(lines, DateTime.UtcNow);
        // a rare drop in this read: the same picture read again, then counted (RareGate)
        if (_rare.Waiting > 0)
        {
            System.Drawing.Bitmap? shot; System.Drawing.Point at;
            lock (_chatShotLock) { shot = _chatShot; at = _chatShotAt; _chatShot = null; }
            using (shot) await ResolveRareAsync("chat", shot, at);
        }
    }
    bool _chatLost; DateTime _lastChatReadAt = DateTime.MinValue;
    int _accAdded, _accMatched;

    // ---- rare drops: second read and commit ----

    ChatParser? _secondParser;

    /// <summary>
    /// Reads the picture a held rare pickup came from a second time (larger, boosted, by the row's icon on the loot list)
    /// and counts each pickup as <see cref="RareGate.Decide"/> says, keeping the crop of its line as proof.
    /// </summary>
    async Task ResolveRareAsync(string source, System.Drawing.Bitmap? shot, System.Drawing.Point origin)
    {
        var held = _rare.Take(source);
        if (held.Count == 0) return;
        List<OcrLineBox>? again = null;
        if (shot != null)
        {
            try { again = await ScreenReader.ReadBoxesAsync(shot, origin, source == "list" ? 4 : 3, boost: true); }
            catch (Exception ex) { CrashLog.Write("rare second read", ex); }
        }
        foreach (var p in held)
        {
            RareGate.Second? s = null;
            if (shot != null && again != null)
                try { s = SecondRead(p, shot, origin, again); } catch (Exception ex) { CrashLog.Write("rare second read", ex); }
            CommitRare(p, s, attempted: shot != null && again != null);
        }
    }

    /// <summary>The line of the second read that is the held pickup's, what it names exactly, and its crop.</summary>
    RareGate.Second SecondRead(RareGate.Pending p, System.Drawing.Bitmap shot, System.Drawing.Point origin, List<OcrLineBox> again)
    {
        var target = p.Source == "list" && p.Event is ItemAcquired li ? _items.NameOf(li.ItemId) : p.Raw;
        var line = again.Select(l => (L: l, S: Similarity(l.Text, target))).Where(x => x.S >= 0.45).OrderByDescending(x => x.S).Select(x => x.L).FirstOrDefault();
        if (line == null) return new RareGate.Second(null, "", null);
        long? id = null;
        Accuracy.Muted = true;
        try
        {
            if (p.Source == "list")
            {
                var row = LootPopup.Rows(new[] { (line.Box.Top, line.Text) }).FirstOrDefault();
                if (row != null && !row.IsKinah && _items.IsExact(row.Name)) id = _items.IdOf(row.Name);
                else if (_settings.Ocr.LootIcons)
                {
                    var look = PopupIcons.Name(_items, shot, origin, line);
                    var named = LootPopup.Rows(new[] { (line.Box.Top, look.Text) }).FirstOrDefault();
                    if (look.Text != line.Text && named != null && _items.IsExact(named.Name)) id = _items.IdOf(named.Name);
                }
            }
            else
            {
                // parsed the way a chat line is, with no hints: only a name the line spells out counts as exact
                _secondParser ??= new ChatParser(_items);
                foreach (var ev in _secondParser.Parse(line.Text, p.At))
                    if (ev is ItemAcquired a && ev.Via.Length == 0 && Contains(line.Text, _items.NameOf(a.ItemId))) { id = a.ItemId; break; }
            }
        }
        finally { Accuracy.Muted = false; }
        var crop = RareGate.CropAround(line.Box, origin, shot.Size, withIcon: p.Source == "list");
        var bmp = crop.Width > 4 && crop.Height > 4 ? shot.Clone(crop, shot.PixelFormat) : null;
        return new RareGate.Second(id, line.Text, bmp);
    }

    /// <summary>True when <paramref name="line"/> spells <paramref name="name"/> (letters and digits only, "(Bound)" aside).</summary>
    static bool Contains(string line, string name)
    {
        static string K(string s) => Regex.Replace(Regex.Replace(s, @"\(bound\)", "", RegexOptions.IgnoreCase).ToLowerInvariant(), @"[^a-z0-9]", "");
        var n = K(name);
        return n.Length >= 3 && K(line).Contains(n);
    }

    /// <summary>How alike two texts are, 0 to 1 (letters and digits only, case aside).</summary>
    static double Similarity(string a, string b)
    {
        static string K(string s) => Regex.Replace(s.ToLowerInvariant(), @"[^a-z0-9]", "");
        string x = K(a), y = K(b);
        if (x.Length == 0 || y.Length == 0) return 0;
        if (x.Contains(y) || y.Contains(x)) return Math.Min(x.Length, y.Length) / (double)Math.Max(x.Length, y.Length) * 0.5 + 0.5;
        var prev = new int[y.Length + 1]; var cur = new int[y.Length + 1];
        for (int j = 0; j <= y.Length; j++) prev[j] = j;
        for (int i = 1; i <= x.Length; i++)
        {
            cur[0] = i;
            for (int j = 1; j <= y.Length; j++) cur[j] = Math.Min(Math.Min(cur[j - 1] + 1, prev[j] + 1), prev[j - 1] + (x[i - 1] == y[j - 1] ? 0 : 1));
            (prev, cur) = (cur, prev);
        }
        return 1 - prev[y.Length] / (double)Math.Max(x.Length, y.Length);
    }

    /// <summary>One held rare pickup decided and counted, its proof kept, the audit and the health counters told.</summary>
    void CommitRare(RareGate.Pending p, RareGate.Second? s, bool attempted)
    {
        var (ev, ok, note) = RareGate.Decide(p, s, _items);
        bool unsure = p.Event.Unconfirmed;
        // loot list rows count only with a second witness (chat-confirmed only, 2026-10-23): for a rare one only the list
        // showed, the second read is that witness; unconfirmed, its proof is kept and it isn't counted
        if (p.Source == "list" && !ok && !_settings.Ocr.LootListAdds)
        {
            string? kept = null;
            if (s?.Crop is { } c0) { using (c0) kept = RareGate.SaveProof(c0, ev is ItemAcquired ka ? _items.NameOf(ka.ItemId) : "item", p.Source, p.At); }
            CrashLog.Note("rare drop only the loot list showed, and the second read didn't confirm it: not counted" + (kept != null ? $" (proof: {Path.GetFileName(kept)})" : ""));
            if (unsure) Accuracy.Add("rare_second_read");
            return;
        }
        Accuracy.Add("rare_seen"); RareCounted++;
        if (unsure && attempted) Accuracy.Add("rare_second_read");
        if (ok) { RareConfirmed++; if (unsure) Accuracy.Add("rare_confirmed"); }
        string? proof = null;
        if (s?.Crop is { } crop) { using (crop) proof = RareGate.SaveProof(crop, ev is ItemAcquired a ? _items.NameOf(a.ItemId) : "item", p.Source, p.At); }
        CrashLog.Note("rare drop: " + note + (proof != null ? $" (proof: {Path.GetFileName(proof)})" : ""));
        Commit(ev);
    }

    /// <summary>Held rare pickups with no picture to read again (a replay, a stop): counted as read.</summary>
    internal void CommitHeld()
    {
        foreach (var p in _rare.Take()) CommitRare(p, null, attempted: false);
    }

    /// <summary>One pickup counted everywhere it goes: the run, the window, the crafting part, the balance check, listeners.</summary>
    void Commit(GameEvent ev)
    {
        Count(ev);
        Feed(ev);
        if (ev is CraftResult or ItemAcquired) CraftView.Apply(ev);
        if (ev is KinahChanged { Bound: false } kc) Interlocked.Add(ref _kinahPicked, kc.Delta);
        else if (ev is KinahChanged { Bound: true, Delta: > 0 } kb) Interlocked.Add(ref _kinahBound, kb.Delta);
        if (ev.Via.Contains("guessed")) Accuracy.Add("guessed");
        try { Parsed?.Invoke(ev); } catch (Exception ex) { CrashLog.Write("parsed event", ex); }
    }

    // ---- layout reports (LayoutProfile) ----
    bool _lootSeen, _craftSeen;

    /// <summary>Where the chat is (and whether the loot list and the panel showed where they were looked for), for the next accuracy report.</summary>
    void ReportLayout()
    {
        try
        {
            if (GameRect is not { } g || g.Width < 200 || g.Height < 200) return;
            var chat = Region is { } r ? LayoutProfile.Fractions(r, g) : null;
            if (chat != null && !LayoutProfile.Plausible(chat)) chat = null;
            Accuracy.Layout(new LayoutReport(g.Width, g.Height, LayoutProfile.UiKey(_textScale?.TextPx, g.Height), chat, ManualRegion ? "chosen" : "found", _lootSeen, _craftSeen, Update.RemoteConfig.Current.RegionsId));
        }
        catch (Exception ex) { CrashLog.Write("layout report", ex); }
    }

    /// <summary>One read of the chat, counted: lined up with the last, parsed, checked against the loot list and applied. Also what replay feeds a recorded fight through.</summary>
    internal void ProcessChat(List<string> lines, DateTime now)
    {
        // rare pickups still waiting from an earlier step that had no picture to read again (a replay): counted as read
        CommitHeld();
        int unaligned = _tracker.Unaligned;
        var fresh = _tracker.NewLines(lines);
        bool afterFade = _tracker.LastAfterFade;
        // a read that couldn't be lined up with the last one counts nothing; the log says so, so a missed line can be traced
        if (_tracker.LastFromAnchor) CrashLog.Note($"the last read was a stray line, so this one was lined up with the last full read of the chat ({fresh.Count} new)");
        if (_tracker.Unaligned > unaligned)
        {
            Accuracy.Add("unaligned");
            CrashLog.Note($"couldn't line this read up with the last one, so only a burst or the lines after the overlap can count from it ({lines.Count} lines; last: {lines[^1]})");
            // both reads in full, so a lost burst of kills can be traced line by line
            CrashLog.Note("   before: " + string.Join("  |  ", _prevRead));
            CrashLog.Note("   now:    " + string.Join("  |  ", lines));
        }
        _prevRead = lines;
        // the enemy on the target bar, as last read, goes with this read's events (Kinah by enemy, a best guess)
        if (fresh.Count > 0) { Engine.SetTarget(Target.Name, Target.NameAt); Secondary?.SetTarget(Target.Name, Target.NameAt); ZoneView.SetTarget(Target.Name, Target.NameAt); }
        // parsed as one batch: a payment line claims the Kinah lines just before it in the same read
        // a Daeva Pass, shop or reward window open in the band: what lands now is a reward, not farming
        // a reward screen open turns what lands into rewards, except inside an event or expedition: there everything is the
        // run's, and a level-up popup mid-event must not set the event's loot aside (Accursed Sword, 2026-10-22 14:35)
        bool reward = Target.RewardOpen(now) && !(ZoneLocator.KindOf(Zone) is { } rk && rk != ZoneLocator.Ignored);
        // the crafting window: its skill tabs in the band, or its own "Show acquired designs" box over the chat
        if (lines.Any(l => Update.RemoteConfig.HasAny(l, Update.RemoteConfig.Current.CraftWindowWords)))
        {
            if ((now - Target.CraftAt).TotalSeconds > 5) CrashLog.Note("crafting window seen (its \"Show acquired designs only\" over the chat)");
            Target.MarkCraft(now);
        }
        foreach (var e in new[] { Engine, Secondary, ZoneView, CraftView }) e?.SetCraftWindow(Target.CraftAt);
        bool crafting = TargetReader.CraftOpen(Target.CraftAt, now);
        // ---- the pickup check (PickupCheck; Steven, 2026-10-23: "you need some type of quality assurance check") ----
        // the player's own inventory at work (storage, Quick Register, an exchange) or the wardrobe unlocking skins: what
        // prints with it, and in the next few seconds, moved or unlocked; it didn't drop (16:09: Enhance Stone x446)
        if (lines.FirstOrDefault(l => (PickupCheck.IsInventoryLine(l) || PickupCheck.IsSkinUnlock(l)) && NewLine(l, now)) is { } invLine)
        {
            if (now > _inventoryUntil) CrashLog.Note($"the inventory at work (\"{invLine}\"): what prints with it is not a drop");
            _inventoryUntil = now.AddSeconds(RewardWindowSeconds);
        }
        // a box obtained in this read, out in the world: what prints with it is its contents, even the lines read before its own
        if (!InRun && _boss == null && lines.FirstOrDefault(l => PickupCheck.IsBoxLine(l) && NewLine(l, now)) is { } boxLine)
        {
            if (now > _boxUntil) CrashLog.Note($"a box (\"{boxLine}\"): what prints with it is its contents, not drops");
            _boxUntil = now.AddSeconds(RewardWindowSeconds);
        }
        // a Force's loot being handed out ("Yarie acquired Soul: Ursus (Bound) through Force distribution."): a world boss
        // died, and for a while the panel beside the character shows the boss's loot table, not the player's pickups
        // A boss's loot chest ("Pnyn Loot Chest (Bound) acquired.") shows its loot table in the same place (2026-10-22 18:39)
        if (lines.Any(l => Regex.IsMatch(l, @"distri\S*|through\s+Fo\S*ce|loot\s*chest", RegexOptions.IgnoreCase) || Update.RemoteConfig.HasAny(Regex.Replace(l, @"\s+", " "), Update.RemoteConfig.Current.DistributionWords)))
        {
            if ((now - _distributionAt).TotalSeconds > DistributionSeconds) CrashLog.Note("a Force is handing out loot or a loot chest came: rows beside the character count only when the chat says they are yours");
            _distributionAt = now;
            if (_boss != null) BossTick(now, died: true);
        }
        // "[Force Recruitment] WB Bloodfang": a world boss's name, learned for this session
        foreach (var l in lines)
            if (Regex.Match(l, @"\bWB\s+([A-Z][A-Za-z']{3,})") is { Success: true } wb && _learnedBosses.Add(wb.Groups[1].Value))
                CrashLog.Note($"world boss named in the chat: {wb.Groups[1].Value}");
        // loot lines that were read garbled while they faded in, now read cleanly at the same place: one more parse each
        var reread = _reread.Take(lines, fresh.Count, now);
        // more loot than the chat can show at once (an expedition's rewards, a box opened): the read shares no line with
        // the last one, so nothing lines up. Its loot lines unseen in the last ten minutes are new, and are counted
        if (_tracker.Unaligned > unaligned)
        {
            var burst = Reread.Burst(_lastLines, lines, _recent, now);
            if (burst.Count > 0) { CrashLog.Note($"a burst of {burst.Count} loot line(s) arrived faster than the chat could show; counted from the read anyway"); reread.AddRange(burst); }
            else
            {
                // it shares lines with the last read but no run of them lined up: what follows the last shared line is new
                var after = Reread.AfterOverlap(_lastLines, lines);
                if (after.Count > 0) { CrashLog.Note($"counted {after.Count} loot line(s) below the last line this read shares with the last one: {string.Join(" | ", after)}"); reread.AddRange(after); }
            }
        }
        // garbled reads in a row (a reward burst fading in): once the chat settles, the clean read's loot lines count
        var settled = new List<string>();
        DateTime settledAt = now; bool settledAny = false, settledAchievement = false;
        if (_tracker.Unaligned > unaligned)
        {
            if (_garbledSince == null)
            {
                _garbledSince = now;
                _beforeGarbled = new HashSet<string>(_recent.Where(kv => (now - kv.Value).TotalMinutes <= 10).Select(kv => kv.Key));
                _countedGarbled.Clear();
            }
        }
        else if (_garbledSince is { } gs && lines.Count > 0)
        {
            // garbled reads can line up with each other: the watch stays open until a clean read gives something, or time is up
            if ((now - gs).TotalSeconds > SettleSeconds) _garbledSince = null;
            else { settled = Reread.Settled(_lastLines, lines, _beforeGarbled, _countedGarbled, out settledAchievement); settledAt = gs; }
        }
        Reread.NoteFirstSeen(_firstSeen, lines, now);
        Reread.Remember(_recent, lines, now);
        _lastLines = lines;
        bool batchAchievement = false;
        bool Apply(IEnumerable<GameEvent> events, string raw, string via = "chat")
        {
            bool any = false;
            foreach (var p0 in events)
            {
                var parsed = p0 with { Via = p0.Via.Length > 0 ? via + " · " + p0.Via : via };
                // the loot list beside the character already added this pickup (this chat line came late): it counts once
                if (_cross.Chat(parsed, now)) { any = true; continue; }
                // with the crafting window open, an item a recipe makes is a craft, never a reward or a drop
                bool made = crafting && parsed is ItemAcquired ia && RecipeBook.ForOutput(_items.NameOf(ia.ItemId)) != null;
                // an achievement's reward or a box's contents is never a drop (Steven, 2026-10-23)
                var why = (parsed is ItemAcquired || parsed is PetCrystal) && !made ? (batchAchievement ? "an achievement's reward" : RewardWhy(parsed.Time, parsed is ItemAcquired bx && BoxName.IsMatch(_items.NameOf(bx.ItemId)))) : null;
                // a stack no enemy drops, out in the world (a gathering material is a gather, an event's grid is the run's)
                if (why == null && !made && parsed is ItemAcquired big && !InRun && !IsGatherMaterial(big.ItemId)) why = PickupCheck.TooMany(big.Count);
                if (why == null && parsed is PetCrystal bigc && !InRun) why = PickupCheck.TooManyCrystals(bigc.Count);
                // Soul Crystals from a quest or a reward are not crystals from kills: left out, so the per hour stays the kills'
                if (why != null && parsed is PetCrystal wp) { CrashLog.Note($"{wp.Name} x{wp.Count}: {why}, not counted with the kills"); any = true; continue; }
                if (why != null && parsed is ItemAcquired wa) CrashLog.Note($"{_items.NameOf(wa.ItemId)} x{wa.Count}: {why}, not a drop");
                // in a base only Odyle is farmed: anything else there is a buy, mail or a reward, never a drop
                var ev = (reward || why != null || InBaseNotOdyle(parsed)) && !made ? _parser.AsReward(parsed) : parsed;
                NoteReward(ev, now);
                any = true; _lastActivity = DateTime.UtcNow;
                // a rare drop waits for the second read of this chat picture (ReadOnce, the same pass) before it counts
                if (ev is ItemAcquired ra && IsRare(ra.ItemId)) { _rare.Hold(new RareGate.Pending(ev, "chat", raw, now)); continue; }
                Commit(ev);
            }
            return any;
        }
        // each new line is voted on across the next reads before it counts (ChatVoter); lines come out in order
        var voted = Vote ? _voter.Push(fresh, lines, now, _parser.Meaning) : fresh;
        // an achievement line among them: the items printed with it are its reward, and those in the next few seconds too
        // only an achievement line that is new: one that stays on screen is read a little differently each time
        batchAchievement = voted.Any(l => Reread.IsAchievement(l) && NewLine(l, now));
        if (batchAchievement) { _achieveUntil = now.AddSeconds(RewardWindowSeconds); CrashLog.Note($"achievement: {voted.First(Reread.IsAchievement)}; its rewards are not drops"); }
        // a line the settle just counted shows up again as new once the reads line up: the same pickup, counted once
        // (Steven, 2026-10-23 16:22: 200 Natura Soul Crystals and 50,000 Kina counted twice)
        lock (_settledCounted)
        {
            _settledCounted.RemoveAll(x => (now - x.At).TotalSeconds > SettledDupSeconds);
            if (_settledCounted.Count > 0)
                voted = voted.Where(l =>
                {
                    int i = _settledCounted.FindIndex(x => Reread.Same(x.Line, l.Trim()));
                    if (i < 0) return true;
                    _settledCounted.RemoveAt(i);
                    CrashLog.Note($"{l}: counted already when the chat settled, not again");
                    return false;
                }).ToList();
        }
        foreach (var (l, events) in _parser.ParseBatch(voted, now))
        {
            bool any = Apply(events, l);
            CrashLog.Read(l, any, afterFade);
            if (any) { LastLine = l; LootLine?.Invoke(l); }
        }
        batchAchievement = reread.Concat(fresh).Any(l => Reread.IsAchievement(l) && NewLine(l, now));
        foreach (var l in reread)
        {
            bool any = false;
            foreach (var (_, events) in _parser.ParseBatch(new List<string> { l }, now, wrap: false)) any |= Apply(events, l, "chat · recovered");
            CrashLog.Read(l + "    (read again: it was garbled while fading in)", any);
            if (any) { LastLine = l; LootLine?.Invoke(l); if (_garbledSince != null) _countedGarbled.Add(l); }
        }
        // dated when the garbling began, which is when they landed (an event's rewards: inside its time for rewards)
        // only an achievement line that first showed with the garbled reads: one that sat in the chat before is an old one
        if (settledAchievement) settledAchievement = _lastLines.Any(l => Reread.IsAchievement(l) && (Reread.FirstSeen(l, _firstSeen) is not DateTime f || f >= settledAt.AddSeconds(-5)));
        batchAchievement = settledAchievement;
        if (settledAchievement) { _achieveUntil = settledAt.AddSeconds(RewardWindowSeconds); CrashLog.Note("the settled lines came with an achievement: they are its reward, not drops"); }
        foreach (var l in settled)
        {
            bool any = false;
            foreach (var (_, events) in _parser.ParseBatch(new List<string> { l }, settledAt, wrap: false)) any |= Apply(events, l, "chat · recovered");
            CrashLog.Read(l + "    (counted once the chat settled)", any);
            if (any) { LastLine = l; LootLine?.Invoke(l); settledAny = true; lock (_settledCounted) _settledCounted.Add((l.Trim(), now)); }
        }
        if (settledAny)
        {
            CrashLog.Note($"the chat settled after {(now - settledAt).TotalSeconds:0} s of reads that didn't line up; its loot lines counted from the clean read: {string.Join(" | ", settled)}");
            _garbledSince = null;
        }
        LastError = "";
    }

    DateTime _targetAt = DateTime.MinValue;
    /// <summary>Lines the settle counted, for the next reads not to count them again (each once).</summary>
    readonly List<(string Line, DateTime At)> _settledCounted = new();
    const double SettledDupSeconds = 15;
    readonly ChatVoter _voter = new();
    internal bool Vote = true;   // off only for replay --novote, to measure what voting changes
    public ChatVoter Voter => _voter;

    /// <summary>
    /// What covers part of the game, or null. While the game is read from its own frames (WindowCapture), a window over it
    /// hides nothing, so reading goes on (0.8.1); otherwise the old check: another window over the area pauses its read.
    /// </summary>
    string? Cover(Rectangle r) => WindowCapture.Active ? null : GameWindow.Covered(r, _settings.ProcessHints);

    // ---- the enemies fighting the player, from their nameplates ----
    // In the open world, once a second while fighting (every 3 s otherwise), the game's picture is searched for the red
    // health bars around the character and the names above them (Nameplates). The counts tell which enemy's bar just went
    // when its loot lands; see SessionEngine.SetEngaged. Inside events and expeditions loot is the run's, so it's skipped.
    DateTime _platesAt = DateTime.MinValue;
    public IReadOnlyList<string> Engaged { get; private set; } = Array.Empty<string>();
    async Task PlatesDue()
    {
        try
        {
            var now = DateTime.UtcNow;
            if (GameRect is not { } g || ZoneLocator.KindOf(Zone) != null) return;
            double every = (now - _lastActivity).TotalSeconds <= 60 ? 1 : 3;
            if ((now - _platesAt).TotalSeconds < every) return;
            _platesAt = now;
            if (Cover(g) is { } cover && !cover.StartsWith("Kinah Ledger")) return;
            using var frame = ScreenReader.Capture(g);
            var plates = await Nameplates.ReadAsync(frame);
            // one plate read as two bars a little apart is one enemy
            var kept = new List<Nameplates.Plate>();
            foreach (var p in plates)
                if (!kept.Any(k => k.Enemy == p.Enemy && Math.Abs(k.Bar.Y - p.Bar.Y) < frame.Height * 0.02 && Math.Abs(k.Bar.X - p.Bar.X) < frame.Height * 0.06)) kept.Add(p);
            var names = kept.Select(p => p.Enemy).ToList();
            if (!names.SequenceEqual(Engaged)) CrashLog.Note("fighting: " + (names.Count == 0 ? "nobody" : string.Join(", ", names)));
            Engaged = names;
            foreach (var e in new[] { Engine, Secondary, ZoneView }) e?.SetEngaged(names, now);
        }
        catch (Exception ex) { CrashLog.Write("nameplates", ex); }
    }

    // ---- the reward table at the end of an expedition or event ----
    // "Obtained Items / Reward collected." lists every reward as an icon with a count; the chat shows only the last few
    // lines of it (2026-10-22, Urugugu Canyon). Inside an expedition or event, once a second, the top of the screen is
    // checked for "Reward collected."; when it is there the grid is read once (RewardGrid, the other session's reader).
    // A slot the chat already counted (same item, same count, last 20 s) is skipped; a slot named as exactly one item is
    // added to the run alone; a slot that names only a family of items, or nothing, is written to reads.log, never guessed.
    DateTime _gridAt = DateTime.MinValue, _gridSeenAt = DateTime.MinValue; bool _gridDone;
    public const double GridSureDistance = 70;
    static string Unbound(string name) => System.Text.RegularExpressions.Regex.Replace(name, @"\s*\(bound\)\s*$", "", System.Text.RegularExpressions.RegexOptions.IgnoreCase).Trim();
    Dictionary<string, List<ItemInfo>>? _byIcon;
    readonly List<(DateTime At, string Name, int Count)> _runPickups = new();
    // the reward table's Kina, for the chat line that may print the same amount after it
    readonly List<(DateTime At, long Amount)> _gridKina = new();

    async Task RewardGridDue()
    {
        try
        {
            var now = DateTime.UtcNow;
            if (GameRect is not { } g || ZoneLocator.KindOf(Zone) is not ("Expedition" or "Event")) { _gridDone = false; return; }
            if ((now - _gridAt).TotalSeconds < 1) return;
            _gridAt = now;
            if (Cover(g) is { } cover && !cover.StartsWith("Kinah Ledger")) return;
            using var frame = ScreenReader.Capture(g);
            var band = new Rectangle(frame.Width / 4, 0, frame.Width / 2, frame.Height / 3);
            using var bandBmp = frame.Clone(band, frame.PixelFormat);
            var lines = await ScreenReader.ReadBoxesAsync(bandBmp, band.Location, 1);
            var rc = lines.FirstOrDefault(l => Update.RemoteConfig.HasAny(l.Text, Update.RemoteConfig.Current.RewardCollectedWords));
            if (rc == null) { if ((now - _gridSeenAt).TotalSeconds > 5) _gridDone = false; return; }
            _gridSeenAt = now;
            if (_gridDone) return;
            _gridDone = true;
            var slots = await RewardGrid.ReadAsync(frame, rc.Box.Bottom + (int)(frame.Height * 0.016));
            ApplyGrid(slots, now);
        }
        catch (Exception ex) { CrashLog.Write("reward table", ex); }
    }

    /// <summary>The reward table's slots into the run: each named item once, skipping what the chat already counted.</summary>
    internal void ApplyGrid(List<RewardGrid.Slot> slots, DateTime now)
    {
        _byIcon ??= _items.AllWithIcons().Where(i => i.Icon.Length > 0).GroupBy(i => IconIndex.KeyOf(i.Icon)).ToDictionary(x => x.Key, x => x.ToList());
        int added = 0, known = 0, unnamed = 0;
        foreach (var sl in slots)
        {
            var best = sl.Icons.FirstOrDefault();
            if (best.Key == null || !_byIcon.TryGetValue(best.Key, out var its)) { unnamed++; CrashLog.Note($"reward table: a {sl.Grade} slot x{sl.CountText} with no item to its icon; not counted"); continue; }
            // the gold coin: Kinah when the Kinah counter rose by exactly the slot's amount around then (2026-10-22, Urugugu:
            // 17,889 to 617,889 for the 600.0K slot). The coin's picture on the item list differs a little from the game's
            // (closest 84), so the counter, not the picture alone, makes it sure; without it the slot isn't counted.
            if (its.Any(i => string.Equals(Unbound(i.Name), "Kina", StringComparison.OrdinalIgnoreCase)) && best.Distance <= 100 && sl.Count >= 1000)
            {
                long amt = Math.Min(sl.Count, 1_000_000_000);
                int kseen = _runPickups.FindIndex(p => (now - p.At).TotalSeconds <= 20 && p.Name == "Kina" && p.Count == amt);
                if (kseen >= 0) { _runPickups.RemoveAt(kseen); known++; continue; }
                // a clear match to the coin as the game draws it is sure on its own; a closer call needs the counter to agree
                bool clearCoin = best.Distance <= GridSureDistance && (sl.Icons.Count < 2 || sl.Icons[1].Distance >= best.Distance * 1.25);
                if (!clearCoin && !BalanceRose(amt, now)) { unnamed++; CrashLog.Note($"reward table: a coin slot x{sl.CountText} (closest {best.Distance:0}) the Kinah counter didn't confirm; not counted"); continue; }
                Instances.Loot(new KinahChanged(now, amt, false) { Via = "reward table" }); _gridKina.Add((now, amt));
                Feed(new KinahChanged(now, amt, false) { Via = "reward table" });
                added++;
                continue;
            }
            var graded = its.Where(i => string.Equals(i.Grade, sl.Grade, StringComparison.OrdinalIgnoreCase)).ToList();
            // a sure match only: close (measured on the Urugugu table: right ones 23 to 60, wrong ones 82 to 114), clearly
            // closer than the next icon, and of the slot's own grade; anything else is the event's own item the list lacks
            bool sure = best.Distance <= GridSureDistance && (sl.Icons.Count < 2 || sl.Icons[1].Distance >= best.Distance * 1.25) && graded.Count > 0;
            if (!sure) { unnamed++; CrashLog.Note($"reward table: a {sl.Grade} slot x{sl.CountText} matches no item surely (closest {best.Key}, {best.Distance:0}); not counted"); continue; }
            // the bound and tradeable copies of an item are the same item
            var names = graded.Select(i => Unbound(i.Name)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            // the gold coin is Kinah, not an item (600.0K on the Urugugu table): the run's Kinah, bound when the list says so
            if (names.Count == 1 && string.Equals(names[0], "Kina", StringComparison.OrdinalIgnoreCase))
            {
                long amt = Math.Clamp(sl.Count, 1, 1_000_000_000);
                int kseen = _runPickups.FindIndex(p => (now - p.At).TotalSeconds <= 20 && p.Name == "Kina" && p.Count == amt);
                if (kseen >= 0) { _runPickups.RemoveAt(kseen); known++; continue; }
                bool bnd = graded.All(i => i.Name.Contains("(Bound)", StringComparison.OrdinalIgnoreCase));
                Instances.Loot(new KinahChanged(now, amt, bnd) { Via = "reward table" }); _gridKina.Add((now, amt));
                Feed(new KinahChanged(now, amt, bnd) { Via = "reward table" });
                added++;
                continue;
            }
            int count = (int)Math.Clamp(sl.Count, 1, 99999);
            int seen = _runPickups.FindIndex(p => (now - p.At).TotalSeconds <= 20 && p.Count == count && names.Contains(p.Name, StringComparer.OrdinalIgnoreCase));
            if (seen >= 0) { _runPickups.RemoveAt(seen); known++; continue; }
            if (names.Count != 1) { unnamed++; CrashLog.Note($"reward table: a {sl.Grade} slot x{sl.CountText} is one of {string.Join(" / ", names.Take(3))}{(names.Count > 3 ? " …" : "")}; not counted"); continue; }
            var item = graded.OrderBy(i => i.Name.Contains("(Bound)", StringComparison.OrdinalIgnoreCase)).First();
            Instances.Loot(new ItemAcquired(now, item.Id, count, false) { Via = "reward table" });
            Feed(new ItemAcquired(now, item.Id, count, false) { Via = "reward table" });
            added++;
        }
        CrashLog.Note($"reward table read: {slots.Count} slots, {known} already counted from the chat, {added} added to the run, {unnamed} not named");
    }

    /// <summary>
    /// Where a pickup counts. Inside an expedition, event, arena or legion area it belongs to that run alone: never the
    /// Farming page's drops, kills or Kinah by enemy, and never the site's farming numbers (Steven, 2026-10-22: events and
    /// expeditions never on the Farming page; the Urugugu Canyon reward cube's loot was counted as drops from kills). The
    /// run is shared on its own. Everywhere else it counts as farming, and the run log sees it too.
    /// </summary>
    // ---- world bosses ----
    // Steven, 2026-10-22, at a world boss: "when a world boss is seen for the top enemy health bar just stop parsing for
    // anything but world boss until its killed" and "dont count world boss toward actual farming". So once the target bar
    // names a world boss, nothing counts (the adds dying around it, their Kinah and drops: set aside, logged). When it dies
    // (the Force's distribution lines, or its bar gone for BossGoneSeconds), what lands in the next BossLootSeconds is its
    // loot: a run of its own on the Events tab and the site (kind "World Boss"), never Kinah/hr.
    // the ones seen so far; the Force recruitment lines ("[Force Recruitment] WB Bloodfang") add more as they are read
    static readonly string[] WorldBosses = { "Saursus", "Bloodfang", "Pnyn", "Kutar" };
    readonly HashSet<string> _learnedBosses = new(StringComparer.OrdinalIgnoreCase);
    public const double BossGoneSeconds = 20, BossLootSeconds = 30, BossGiveUpSeconds = 120;
    string? _boss; DateTime _bossSeenAt, _bossDeadAt = DateTime.MinValue; int _bossSetAside;
    // a Force handing out loot: for this long after its last line, the panel beside the character may show a loot table
    public const double DistributionSeconds = 60;
    DateTime _distributionAt = DateTime.MinValue;

    /// <summary>The world boss a target bar's name is, or null: a boss's name with or without its title ("Furious Saursus").</summary>
    public static string? WorldBossOf(string? name, IEnumerable<string>? more = null) =>
        name == null ? null : WorldBosses.Concat(more ?? Enumerable.Empty<string>()).FirstOrDefault(b => Regex.IsMatch(name, $@"\b{Regex.Escape(b)}\b", RegexOptions.IgnoreCase));

    /// <summary>What the boss state is, for the window: "", "fighting X", "X's loot".</summary>
    public string BossNote => _boss == null ? "" : _bossDeadAt == DateTime.MinValue ? $"world boss: {_boss} (nothing counts until it dies)" : $"world boss down: {_boss}'s loot";

    void BossTick(DateTime now, bool died = false)
    {
        var seen = (now - Target.NameAt).TotalSeconds <= 3 ? WorldBossOf(Target.Name, _learnedBosses) : null;
        if (_boss == null)
        {
            if (seen == null || Instances.Open != null) return;
            _boss = seen; _bossSeenAt = now; _bossDeadAt = DateTime.MinValue; _bossSetAside = 0;
            CrashLog.Note($"world boss on the target bar: {seen}. Nothing counts toward farming until it dies");
            return;
        }
        if (_bossDeadAt == DateTime.MinValue)
        {
            if (seen != null) _bossSeenAt = now;
            if (died || (now - _bossSeenAt).TotalSeconds >= BossGoneSeconds)
            {
                _bossDeadAt = now;
                bool run = Instances.StartBoss(_boss, now);
                CrashLog.Note($"world boss down ({(died ? "the Force's loot was handed out" : $"its bar gone {BossGoneSeconds:0} s")}): {_boss}; {_bossSetAside} pickup(s) set aside during the fight; the next {BossLootSeconds:0} s are its loot{(run ? "" : " (inside a run: kept by that run)")}");
            }
            else if ((now - _bossSeenAt).TotalSeconds >= BossGiveUpSeconds) EndBoss("left the fight");
            return;
        }
        if ((now - _bossDeadAt).TotalSeconds >= BossLootSeconds) EndBoss("loot window over");
    }

    void EndBoss(string why)
    {
        CrashLog.Note($"world boss mode off ({why}): {_boss}");
        Instances.EndBoss();
        _boss = null; _bossDeadAt = DateTime.MinValue;
    }

    /// <summary>True when the world boss took this event: set aside during the fight, or kept as its loot after.</summary>
    bool BossTakes(GameEvent ev)
    {
        if (_boss == null) return false;
        if (_bossDeadAt == DateTime.MinValue)
        {
            // a rare drop (boss gear, Legend, Unique, Epic) is never set aside, fight or not (Steven, 2026-10-23)
            if (ev is ItemAcquired ra && IsRare(ra.ItemId))
            {
                CrashLog.Note($"world boss fight: {Describe(ev)} is a rare drop, so it counts");
                return false;
            }
            if (ev is ItemAcquired or KinahChanged or MobKilled or CurrencyGained or EnemySoul or XpGained or PetCrystal)
            {
                _bossSetAside++;
                CrashLog.Note($"world boss fight: {Describe(ev)} set aside, not farming");
            }
            return true;
        }
        Instances.Loot(ev);
        return true;
    }

    string Describe(GameEvent ev) => ev switch
    {
        ItemAcquired a => $"{_items.NameOf(a.ItemId)} x{a.Count}",
        KinahChanged k => $"{k.Delta:N0} Kina",
        CurrencyGained c => c.Name,
        EnemySoul s => "the soul of " + s.Kind,
        _ => ev.GetType().Name,
    };

    // ---- achievement rewards and box contents: never drops (Steven, 2026-10-23) ----
    /// <summary>After an achievement line, or a reward or box line out in the world, what lands this long after is a reward.</summary>
    public const double RewardWindowSeconds = 10;
    /// <summary>A loot list row out in the world with no kill or gather this long before it is a box or a reward.</summary>
    public const double NoKillSeconds = 15;
    DateTime _achieveUntil = DateTime.MinValue, _boxUntil = DateTime.MinValue, _lastKillAt = DateTime.MinValue, _lastGatherAt = DateTime.MinValue;
    static readonly Regex BoxName = new(@"\b(?:chest|box|pouch|bundle|cube|coffer|crate|package|parcel)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    static HashSet<string>? _gatherNames;
    bool InRun => ZoneLocator.KindOf(Zone) is { } rk && rk != ZoneLocator.Ignored;

    /// <summary>Why a pickup at <paramref name="t"/> is a reward, or null: an achievement's (anywhere), a box's or a reward's (out in the world).</summary>
    string? RewardWhy(DateTime t, bool isBox = false) =>
        t <= _achieveUntil ? "an achievement's reward"
        : t <= _inventoryUntil ? "the inventory at work (storage, Quick Register, the wardrobe)"
        : !InRun && !isBox && t <= _boxUntil ? "a box's or a reward's contents" : null;
    /// <summary>Until when what lands is the player's own inventory at work (PickupCheck.IsInventoryLine, IsSkinUnlock).</summary>
    DateTime _inventoryUntil = DateTime.MinValue;

    /// <summary>A reward line, or an item that is a box ("Growth Pet Chest"), out in the world opens the window for what comes out of it.</summary>
    void NoteReward(GameEvent ev, DateTime now)
    {
        if (InRun || _boss != null) return;
        string? name = ev switch { RewardReceived r => r.Name, ItemAcquired a => _items.NameOf(a.ItemId), _ => null };
        if (name == null || name.Contains("Loot Chest", StringComparison.OrdinalIgnoreCase)) return;
        if (ev is RewardReceived || BoxName.IsMatch(name)) _boxUntil = now.AddSeconds(RewardWindowSeconds);
    }

    // ---- the gathering bar as a witness (Steven's plan, step 1, 2026-10-23) ----
    // The gathering panel is the crafting panel's widget: one race per gather, Success or Fail, and when the game rolls an
    // upgrade (the panel turns blue, an "x3" badge) a second race on the same gather. Each base race that ends on Success
    // is one gather that happened; a chat line for a gathering material claims it, and a loot list row the chat missed
    // may only count by claiming one. A stopped gather never finishes its race, so it leaves nothing to claim.
    public const double GatherTokenSeconds = 10;
    readonly List<(DateTime At, bool Upgraded, bool Used)> _gatherTokens = new();

    /// <summary>One finished race of the gathering bar (also what tests feed).</summary>
    internal void GatherOutcome(DateTime at, bool ok, bool upgradeRoll)
    {
        lock (_gatherTokens)
        {
            if (upgradeRoll) { if (_gatherTokens.Count > 0) _gatherTokens[^1] = _gatherTokens[^1] with { Upgraded = ok }; return; }
            if (ok) _gatherTokens.Add((at, false, false));
            _gatherTokens.RemoveAll(t => (at - t.At).TotalSeconds > 60);
        }
    }

    /// <summary>Claims the latest unclaimed successful gather of the last few seconds; false when there is none.</summary>
    bool TakeGatherToken(DateTime now, string by)
    {
        lock (_gatherTokens)
        {
            for (int i = _gatherTokens.Count - 1; i >= 0; i--)
            {
                var t = _gatherTokens[i];
                if (t.Used || (now - t.At).TotalSeconds > GatherTokenSeconds || (now - t.At).TotalSeconds < -2) continue;
                _gatherTokens[i] = t with { Used = true };
                return true;
            }
            return false;
        }
    }

    bool IsGatherMaterial(long id)
    {
        _gatherNames ??= GatheringCatalog.All.Select(g => g.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return _gatherNames.Contains(_items.NameOf(id));
    }

    void Count(GameEvent ev)
    {
        // a chat line for a gathering material claims the gathering bar's success, so the loot list can't count it again
        if (ev is ItemAcquired cg0 && !ev.Via.Contains("loot list") && IsGatherMaterial(cg0.ItemId)) TakeGatherToken(ev.Time, "the chat");
        // what a kill or a gather looks like, for telling a box's contents in the loot list from a drop
        if (ev is MobKilled or EnemySoul or XpGained || ev is CurrencyGained { Enemy.Length: > 0 } || (ev is CurrencyGained cg && cg.Name.EndsWith("Soul Points", StringComparison.OrdinalIgnoreCase)) || ev is KinahChanged { Bound: false, Delta: > 0 })
            _lastKillAt = ev.Time;
        if (ev is GatherFailed) _lastGatherAt = ev.Time;
        else if (ev is ItemAcquired ga)
        {
            _gatherNames ??= GatheringCatalog.All.Select(g => g.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
            if (_gatherNames.Contains(_items.NameOf(ga.ItemId))) _lastGatherAt = ev.Time;
        }
        if (ev is not CraftResult && BossTakes(ev)) return;
        // inside an event or expedition, or just out of one while its rewards still land (InstanceLog.RewardGraceSeconds)
        bool ending = ZoneLocator.KindOf(Zone) == null && Instances.Ending(ev.Time);
        if ((ZoneLocator.KindOf(Zone) is { } kind && kind != ZoneLocator.Ignored) || ending)
        {
            // the reward table's coin slot, read first, then the same Kina in the chat: once
            if (ev is KinahChanged { Delta: > 0 } dup && _gridKina.FindIndex(g => (ev.Time - g.At).TotalSeconds is >= 0 and <= 90 && g.Amount == dup.Delta) is int gi and >= 0)
            {
                _gridKina.RemoveAt(gi);
                CrashLog.Note($"{dup.Delta:N0} Kina in the chat was the reward table's, already counted");
                return;
            }
            Instances.Loot(ev);
            // kept a while, so the reward table read after it doesn't count the same items again
            if (ev is ItemAcquired ra) _runPickups.Add((ev.Time, Unbound(_items.NameOf(ra.ItemId)), ra.Count));
            else if (ev is RewardReceived rr) _runPickups.Add((ev.Time, Unbound(rr.Name), rr.Count));
            else if (ev is KinahChanged { Delta: > 0 } rk) _runPickups.Add((ev.Time, "Kina", (int)Math.Min(rk.Delta, int.MaxValue)));
            if (_runPickups.Count > 60) _runPickups.RemoveAt(0);
            return;
        }
        Engine.Apply(ev); Secondary?.Apply(ev); ZoneView.Apply(ev); Instances.Loot(ev);
    }

    // ---- the audit: how the count holds up, once a minute in reads.log ----
    // The quest tracker's "Defeat … (n/20)" lines count kills of quest enemies exactly, so where every enemy fought is a
    // quest enemy, the kills counted should rise as fast as the counter. Pass times say how often the screen is read.
    int _passes; double _passMs, _chatMs; DateTime _auditAt = DateTime.UtcNow; int _auditKills = -1, _questSteps, _auditAdded, _auditDups;
    readonly Dictionary<int, int> _questMax = new();
    /// <summary>A new quest number seen once, waiting for the next read to agree (and when it was first seen).</summary>
    readonly Dictionary<int, (int N, DateTime At)> _questPending = new();
    /// <summary>Steps of the quest tracker's kill counters seen since the start: kills of quest enemies, counted by the game.</summary>
    public int QuestStepsTotal { get; private set; }
    /// <summary>For the accuracy meter: in the minutes the quest counter moved, the kills it counted and the kills the tracker counted.</summary>
    public int QuestKillsGame { get; private set; }
    public int QuestKillsCounted { get; private set; }
    internal void QuestCounter(IReadOnlyList<string> lines, DateTime? at = null)
    {
        var t = at ?? DateTime.UtcNow;
        var best = new Dictionary<int, int>();
        for (int i = 0; i < lines.Count; i++)
        {
            var m = System.Text.RegularExpressions.Regex.Match(lines[i], @"\((\d{1,3})\s*/\s*(\d{1,3})\)\s*$");
            if (!m.Success) continue;
            // the counter line, or its wrapped half, follows a "Defeat …" line
            bool quest = lines[i].Contains("Defeat", StringComparison.OrdinalIgnoreCase) || (i > 0 && lines[i - 1].Contains("Defeat", StringComparison.OrdinalIgnoreCase));
            if (!quest) continue;
            int n = int.Parse(m.Groups[1].Value), of = int.Parse(m.Groups[2].Value);
            if (n > of || of < 2) continue;
            best[of] = Math.Max(best.GetValueOrDefault(of), n);
        }
        foreach (var (of, n) in best)
        {
            bool known = _questMax.TryGetValue(of, out var was);
            if (known && n > was && n - was <= 5)
            {
                // two reads agree on the new number before it counts: one misread "(24/25)" is not three kills
                if (!(_questPending.TryGetValue(of, out var p) && p.N == n)) { _questPending[of] = (n, t); continue; }
                _questPending.Remove(of);
                int steps = n - was;
                _questSteps += steps; QuestStepsTotal += steps;
                // each step is a kill the game counted: evidence for the kill count, joining the kill its Kinah or Soul
                // line showed, or a kill of its own when those were lost (Steven, 2026-10-23: 25 kills counted as 20)
                for (int s = 0; s < steps; s++) foreach (var e in new[] { Engine, Secondary, ZoneView }) e?.QuestStep(p.At);
            }
            else _questPending.Remove(of);
            if (!known || n > was || n < was - 3) _questMax[of] = n;
        }
    }
    void Audit()
    {
        var now = DateTime.UtcNow;
        if (_auditKills < 0) { _auditKills = Engine.Totals().Kills; _auditAdded = _cross.Added; _auditDups = _cross.Duplicates; }
        if ((now - _auditAt).TotalSeconds < 60) return;
        SaveColours();
        ReportLayout();   // the text size is known by now: the layout's key gets it
        int kills = Engine.Totals().Kills;
        // the minutes when the quest counter moved: what the game counted against what the tracker counted, for the meter
        if (_questSteps > 0 && kills >= _auditKills) { QuestKillsGame += _questSteps; QuestKillsCounted += kills - _auditKills; }
        if (_passes > 0 && (kills > _auditKills || _questSteps > 0))
            CrashLog.Note($"audit: last {(now - _auditAt).TotalSeconds:0} s · kills counted +{kills - _auditKills} · quest counter +{_questSteps} · from the loot list +{_cross.Added - _auditAdded} · chat repeats skipped {_cross.Duplicates - _auditDups} · Kinah counter agreed {_balanceCheck.Agreed}×, over {_balanceCheck.Over:N0}, under {_balanceCheck.Under:N0} so far · {_passes} passes, {_passMs / _passes:0} ms each (chat {_chatMs / _passes:0} ms) · names settled by colour {_parser.ColourPicks}, by loot table {_parser.TablePicks}, guesses no table backs {_parser.HeldUnlisted}, loot list rows named by icon {IconNamed} · chat read at {_reader?.Factor ?? 0:0.##}x");
        _auditAt = now; _auditKills = kills; _questSteps = 0; _passes = 0; _passMs = 0; _chatMs = 0; _auditAdded = _cross.Added; _auditDups = _cross.Duplicates;
    }

    /// <summary>One pickup as counted, for the recent pickups strip: when, what, and how it was read.</summary>
    public sealed record Pickup(DateTime At, string What, string Via, bool Unconfirmed);
    readonly List<Pickup> _pickups = new();
    /// <summary>The last pickups counted, newest first: items and Kinah only, rewards and bound Kinah left out.</summary>
    public List<Pickup> RecentPickups(int n) { lock (_pickups) return _pickups.TakeLast(n).Reverse().ToList(); }
    void Feed(GameEvent ev)
    {
        // Kina is money, never an item: a loot list row the icon named Kina, its amount cut off ("Vana" read as "Kina x1"),
        // landed in Drops from kills (Steven, 2026-10-23 16:17). The amount is unknown, so nothing counts from it
        if (ev is ItemAcquired km && PickupCheck.IsKina(_items.NameOf(km.ItemId)))
        {
            CrashLog.Note($"Kina x{km.Count} as an item ({ev.Via}): Kina is money, not a drop, and its amount wasn't read; not counted");
            return;
        }
        string? what = ev switch
        {
            ItemAcquired a => $"{_items.NameOf(a.ItemId)} ×{a.Count}" + (a.Bound ? " (bound)" : ""),
            KinahChanged { Bound: false, Delta: > 0 } k => $"{k.Delta:N0} Kinah",
            _ => null,
        };
        if (what == null) return;
        Accuracy.Add("pickups");
        Report.Counted(ev.Time, what, ev.Via);
        lock (_pickups) { _pickups.Add(new Pickup(ev.Time, what, ev.Via, ev.Unconfirmed)); if (_pickups.Count > 50) _pickups.RemoveRange(0, _pickups.Count - 50); }
    }

    /// <summary>The chat and the loot list beside the character, checked against each other: see <see cref="LootCrossCheck"/>.</summary>
    readonly LootCrossCheck _cross;
    public LootCrossCheck CrossCheck => _cross;

    /// <summary>
    /// One read of the loot list beside the character, every pass while the game is in view. Its rows wait for the chat;
    /// a row the chat never matched is counted from the list, through the same rules as a chat line (reward screens too).
    /// </summary>
    /// <summary>One read of the loot list beside the character, started before the chat read so the two run side by side. Null when it can't be read.</summary>
    async Task<(List<OcrLineBox> Lines, System.Drawing.Bitmap Raw, System.Drawing.Point Origin)?> LootReadAsync()
    {
        if (GameRect is not { } g) return null;
        var region = LootPopup.Region(g);
        System.Drawing.Bitmap? raw = null;
        try
        {
            if (Cover(region) != null) return null;   // something over it: nothing read, nothing added
            // the picture is kept for the read's rows' icons (PopupIcons); the caller disposes it
            raw = ScreenReader.Capture(region);
            return (await ScreenReader.ReadBoxesAsync(raw, region.Location, 3, boost: true), raw, region.Location);
        }
        catch (Exception ex) { raw?.Dispose(); CrashLog.Write("loot list", ex); return null; }
    }

    /// <summary>Loot list rows named by their icon (PopupIcons), for the audit line.</summary>
    public int IconNamed { get; private set; }

    /// <summary>One loot list picture read and counted the way a live pass does it (text, icons, the rare drops' second read): replays and the replay bank.</summary>
    internal async Task ReplayListFrameAsync(System.Drawing.Bitmap raw, System.Drawing.Point origin, DateTime t)
    {
        var lines = await ScreenReader.ReadBoxesAsync(raw, origin, 3, boost: true);
        await LootProcess((lines, (System.Drawing.Bitmap)raw.Clone(), origin), t);   // LootProcess disposes its picture
    }

    async Task LootProcess((List<OcrLineBox> Lines, System.Drawing.Bitmap Raw, System.Drawing.Point Origin)? read, DateTime? at = null)
    {
        if (read is not { } r) return;
        var now = at ?? DateTime.UtcNow;
        using (r.Raw)
        {
            var lines = r.Lines.Select(l => (l.Box.Top, l.Text)).ToList();
            // the list's picture for the miscount report, when it shows rows (at most once a second: the world moves behind it)
            if (ReportBuffer.Keeps("loot") && LootPopup.Rows(lines).Count > 0)
                try { Report.Picture("loot", r.Raw, r.Origin, now, minGapSeconds: 1); } catch (Exception ex) { CrashLog.Write("report picture", ex); }
            if (_settings.Ocr.LootIcons && !Update.RemoteConfig.Current.Off("loot_icons") && r.Lines.Count > 0)
            {
                // a row whose text is cut off or garbled is named by the icon beside it, when one icon is clearly it
                for (int i = 0; i < r.Lines.Count; i++)
                {
                    try
                    {
                        var look = PopupIcons.Name(_items, r.Raw, r.Origin, r.Lines[i]);
                        if (look.Text == r.Lines[i].Text) continue;
                        lines[i] = (lines[i].Top, look.Text);
                        IconNamed++;
                        CrashLog.Note($"loot list: \"{r.Lines[i].Text}\" read as \"{look.Text}\": {look.Note} (icon {look.Verdict?.Distance:0}, next {(look.Verdict?.Next < 1e6 ? look.Verdict.Next.ToString("0") : "none")})");
                    }
                    catch (Exception ex) { CrashLog.Write("loot list icon", ex); }
                }
            }
            ProcessList(lines, now);
            // a rare drop only the list showed: this picture read again before it counts (RareGate)
            if (_rare.Waiting > 0) await ResolveRareAsync("list", r.Raw, r.Origin);
        }
    }

    /// <summary>One read of the loot list, checked against the chat. Also what replay feeds a recorded fight through.</summary>
    internal void ProcessList(List<(int Top, string Text)> lines, DateTime now)
    {
        try
        {
            CommitHeld();   // anything a step without a picture left waiting (a replay) counts as read first
            // a shop or reward screen covers the place the loot list shows: nothing there is loot
            if (Target.RewardOpen(now)) return;
            // the gathering panel ("Orichalcum · Proficiency · Orichalcum Ore x1 · Success") sits at the bottom of the band:
            // its lines are what is being gathered, never a pickup (Steven, 2026-10-23 11:28)
            lines = WithoutGatherPanel(lines, now);
            // a row that changed in place a moment ago is the same item, its final text (an upgrade, a redraw)
            Supersede(lines, now);
            var rows = LootPopup.Rows(lines);
            if (rows.Count > 0) _lastActivity = DateTime.UtcNow;
            if (rows.Count > 0 && !_lootSeen) { _lootSeen = true; ReportLayout(); }
            if (rows.Count > 0) { var traced = rows.Select(r => r.IsKinah ? $"{r.Amount} Kina" : $"{r.Name} x{r.Count}").ToList(); ReadTrace.List(now, traced); Report.Lines("list", traced, now); }
            var add = _cross.Popup(rows, now);
            if (add.Count == 0) return;
            bool reward = Target.RewardOpen(now);
            foreach (var e in new[] { Engine, Secondary, ZoneView, CraftView }) e?.SetCraftWindow(Target.CraftAt);
            Engine.SetTarget(Target.Name, Target.NameAt); Secondary?.SetTarget(Target.Name, Target.NameAt); ZoneView.SetTarget(Target.Name, Target.NameAt);
            bool crafting = TargetReader.CraftOpen(Target.CraftAt, now);
            // with the crafting panel up or just gone, every item the list shows is what the crafts made
            bool panel = CraftView.CraftPanelActive(now);
            foreach (var p0 in add)
            {
                var parsed = p0 with { Via = "loot list" };
                // a Force handing out a boss's loot: the panel lists the loot table (Steven, 2026-10-22: "i didnt get the
                // helm or the greaves... that was just showing us the loot table"). Only the chat can say an item is the player's
                if ((now - _distributionAt).TotalSeconds <= DistributionSeconds && parsed is ItemAcquired dist)
                {
                    CrashLog.Note($"a Force is handing out loot: {_items.NameOf(dist.ItemId)} x{dist.Count} beside the character set aside, the chat never said it was yours");
                    continue;
                }
                bool made = (panel && parsed is ItemAcquired) || (crafting && parsed is ItemAcquired ia && RecipeBook.ForOutput(_items.NameOf(ia.ItemId)) != null);
                // loot list rows count only with a chat line (Steven, 2026-10-23: "chat-confirmed only"); what a craft made
                // still counts from the list, as the crafting panel's flow did; an enemy's soul row is a kill, not a pickup
                // ...except a gather the chat missed: a gathering material, with a gathering bar that finished on Success in
                // the last few seconds that no chat line has claimed (step 1 of Steven's plan, 2026-10-23): two witnesses
                if (!made && !_settings.Ocr.LootListAdds && parsed is ItemAcquired gl && IsGatherMaterial(gl.ItemId) && TakeGatherToken(now, "the loot list"))
                    CrashLog.Note($"loot list: {_items.NameOf(gl.ItemId)} x{gl.Count} counted: the chat missed it, and the gathering bar finished on Success");
                // a rare drop is the other exception: it goes to its second read, and counts when that read confirms it (CommitRare)
                else if (!made && !_settings.Ocr.LootListAdds && parsed is ItemAcquired or KinahChanged && !(parsed is ItemAcquired rl && IsRare(rl.ItemId)))
                {
                    CrashLog.Note($"loot list: {(parsed is ItemAcquired na ? $"{_items.NameOf(na.ItemId)} x{na.Count}" : $"{((KinahChanged)parsed).Delta:N0} Kina")} not counted: no chat line confirmed it");
                    continue;
                }
                // with the crafting window open, the loot list shows what the crafts gave: a recipe's item is a craft (the
                // engines take it as one), anything else is set aside until the window itself can be read, never a drop
                if (crafting && !made && parsed is ItemAcquired other)
                {
                    CrashLog.Note($"crafting window open: {_items.NameOf(other.ItemId)} x{other.Count} from the loot list set aside, not a drop");
                    continue;
                }
                // in a base, a loot-list row the chat never confirmed that isn't Odyle is left out entirely
                if (!made && InBaseNotOdyle(parsed)) { CrashLog.Note($"in a base, so not counted from the loot list: {_items.NameOf(((ItemAcquired)parsed).ItemId)}"); continue; }
                // an achievement's reward or a box's contents shows in the list too; out in the world, a row with no kill or
                // gather around it is one (a box opened in town). Never a drop (Steven, 2026-10-23)
                if (!made && parsed is ItemAcquired lr)
                {
                    var why = RewardWhy(now);
                    // a gathering material is a gather itself (the chat may have missed its line), never a box's contents
                    _gatherNames ??= GatheringCatalog.All.Select(g => g.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
                    bool gathered = _gatherNames.Contains(_items.NameOf(lr.ItemId));
                    if (why == null && !gathered && !InRun && (now - _lastKillAt).TotalSeconds > NoKillSeconds && (now - _lastGatherAt).TotalSeconds > NoKillSeconds) why = $"no kill or gather in the last {NoKillSeconds:0} s, so a box or a reward";
                    if (why == null && !gathered && !InRun) why = PickupCheck.TooMany(lr.Count);
                    if (why != null) { CrashLog.Note($"loot list: {_items.NameOf(lr.ItemId)} x{lr.Count} set aside: {why}"); continue; }
                }
                var ev = reward && !made ? _parser.AsReward(parsed) : parsed;
                // a rare drop only the list showed waits for its second read (LootProcess, this pass)
                if (ev is ItemAcquired ra && IsRare(ra.ItemId)) { _rare.Hold(new RareGate.Pending(ev, "list", _items.NameOf(ra.ItemId), now)); continue; }
                Commit(ev);
            }
        }
        catch (Exception ex) { CrashLog.Write("loot list", ex); }
    }
    /// <summary>The previous read as it came, for the log when a read can't be lined up with it.</summary>
    List<string> _prevRead = new();
    readonly Reread _reread = new();
    // when each line was first on screen: an achievement line that stays up is read a little differently each time, and
    // only a new one makes the items with it its reward
    readonly Dictionary<string, (DateTime First, DateTime Last)> _firstSeen = new();
    bool NewLine(string line, DateTime now) => Reread.FirstSeen(line, _firstSeen) is not DateTime f || (now - f).TotalSeconds <= 20;
    // reads that couldn't be lined up, in a row: since when, the lines seen before them, what was counted meanwhile
    DateTime? _garbledSince; HashSet<string> _beforeGarbled = new(); readonly List<string> _countedGarbled = new();
    /// <summary>Garbled reads longer than this before the chat settles aren't one burst; nothing is taken from the settled read.</summary>
    public const double SettleSeconds = 45;
    // every line of the last ten minutes of reads, for telling a burst's new lines from history
    readonly Dictionary<string, DateTime> _recent = new();
    List<string> _lastLines = new();

    /// <summary>
    /// The target bar every 2 seconds while the chat is read. Unlike the minimap, the panel isn't hidden for it: a read every
    /// 2 seconds would make it flicker. It is read through instead, and the window asks to move it only when nothing is read.
    /// </summary>
    // ---- reward screens, kept as pictures ----
    // Events and expeditions pay out on their own screens (cards picked at the end of an event, the big reward table at
    // the end of an expedition), not in the chat (Steven, 2026-10-22). To learn to read them, a picture of the game window
    // is kept each time one opens: a few seconds apart while it stays open, on this PC only, in reward-shots beside the
    // logs, at most RewardShotsPerDay a day. Inside an event or expedition a picture every 30 s too, since a card pick may
    // show no title to recognise. Nothing else on screen is captured, nothing is sent anywhere, and it only happens when the
    // reward-shots folder exists (made by hand on a PC that helps build the reader; never by the app).
    // The folder cleans itself (Steven, 2026-10-22: 212 pictures, 472 MB in a day): in-run pictures go after a day, reward
    // screens after a week, and past RewardShotsMaxMB the oldest go first, in-run ones before reward screens. Fight
    // pictures stopped once the nameplate reader was built from them. While the crafting window is open (or within its
    // hold), a picture every 2 s, "-crafting.jpg", for a reader of the window's recipe, queue and result; kept a day.
    public const int RewardShotsPerDay = 300;
    bool _craftShotNoted;
    public const long RewardShotsMaxMB = 300;
    DateTime _rewardShotAt = DateTime.MinValue, _rewardPrunedAt = DateTime.MinValue; int _rewardShotsOpen;

    /// <summary>Deletes old pictures from reward-shots (see above). Only ever .jpg files the tracker named itself.</summary>
    internal static int PruneRewardShots(string dir, DateTime now)
    {
        int gone = 0;
        var files = new DirectoryInfo(dir).GetFiles("2*-*.jpg")
            .Where(f => System.Text.RegularExpressions.Regex.IsMatch(f.Name, @"^\d{8}-\d{6}-"))
            .Select(f => (F: f, Run: f.Name.EndsWith("-in-run.jpg") || f.Name.EndsWith("-fight.jpg") || f.Name.EndsWith("-crafting.jpg")))
            .ToList();
        void Drop((FileInfo F, bool Run) x) { try { x.F.Delete(); gone++; } catch { } }
        foreach (var x in files.Where(x => (now - x.F.LastWriteTime).TotalDays > (x.Run ? 1 : 7)).ToList()) { Drop(x); files.Remove(x); }
        long total = files.Sum(x => x.F.Length), max = RewardShotsMaxMB * 1024 * 1024;
        foreach (var x in files.OrderByDescending(x => x.Run).ThenBy(x => x.F.LastWriteTime).ToList())
        {
            if (total <= max) break;
            total -= x.F.Length; Drop(x);
        }
        return gone;
    }
    void RewardShot()
    {
        try
        {
            var now = DateTime.UtcNow;
            var dir = Path.Combine(CrashLog.Dir, "reward-shots");
            if (GameRect is not { } g || !Directory.Exists(dir)) return;
            if ((now - _rewardPrunedAt).TotalMinutes >= 30)
            {
                _rewardPrunedAt = now;
                int gone = PruneRewardShots(dir, DateTime.Now);
                if (gone > 0) CrashLog.Note($"reward-shots: {gone} old pictures deleted");
            }
            bool reward = Target.RewardOpen(now), inside = ZoneLocator.KindOf(Zone) is "Event" or "Expedition" || Zone == SessionEngine.UnknownZone;
            // the crafting window, to learn to read it: recipe, queue, result, item made (a picture every 2 s while open)
            bool crafting = !reward && TargetReader.CraftOpen(Target.CraftAt, now) && now >= Target.CraftAt;
            if (!reward) _rewardShotsOpen = 0;
            bool due = reward ? _rewardShotsOpen < 6 && (now - _rewardShotAt).TotalSeconds >= 2
                     : crafting ? (now - _rewardShotAt).TotalSeconds >= 2
                     : inside && (now - _rewardShotAt).TotalSeconds >= 30;
            if (!due) return;
            var today = DateTime.Now.ToString("yyyyMMdd");
            if (Directory.GetFiles(dir, today + "-*.jpg").Length >= RewardShotsPerDay) return;
            if (Cover(g) is { } cover && !cover.StartsWith("Kinah Ledger")) return;   // only the game, never a window over it
            // and only while the game is the active window: Alt+Tab's switcher, the Start menu or a notification sit over it
            // without counting as a window over it, and Alt+Tab shows thumbnails of everything open (2026-10-22 15:07)
            if (!GameWindow.InFront(_settings.ProcessHints)) return;
            _rewardShotAt = now; if (reward) _rewardShotsOpen++;
            var title = crafting ? "crafting" : !reward ? "in-run" : new string(Target.RewardTitle.Where(c => char.IsLetterOrDigit(c) || c == ' ').ToArray()).Trim().Replace(' ', '-');
            var zoneName = new string((Zone ?? "").Where(c => char.IsLetterOrDigit(c) || c == ' ').ToArray()).Trim().Replace(' ', '-');
            using var bmp = ScreenReader.Capture(g);
            var jpg = System.Drawing.Imaging.ImageCodecInfo.GetImageEncoders().First(e => e.FormatID == System.Drawing.Imaging.ImageFormat.Jpeg.Guid);
            using var ps = new System.Drawing.Imaging.EncoderParameters(1);
            ps.Param[0] = new System.Drawing.Imaging.EncoderParameter(System.Drawing.Imaging.Encoder.Quality, 85L);
            var file = Path.Combine(dir, $"{DateTime.Now:yyyyMMdd-HHmmss}-{zoneName}-{title}.jpg");
            bmp.Save(file, jpg, ps);
            if (_rewardShotsOpen == 1 || (crafting && !_craftShotNoted)) CrashLog.Note($"{(crafting ? "crafting window" : "reward screen")} kept as a picture: {Path.GetFileName(file)}");
            _craftShotNoted = crafting;
        }
        catch (Exception ex) { CrashLog.Write("reward picture", ex); }
    }

    // ---- the crafting panel ----
    // While crafts run, the crafting window is closed and the chat may never show a craft (Steven, 2026-10-22: "you can
    // queue like 100 crafts and never see the chat"). The panel at the bottom centre races a Success bar against a Fail
    // bar for each craft; see CraftPanel. Its strip is read every pass (pixels only; a race lasts about five seconds), its
    // recipe line by OCR when the panel opens and every few seconds after, and each race's outcome is one craft.
    readonly CraftPanelTracker _panel = new();
    // the same Success/Fail bars when a node is gathered: each race's outcome, for the audit and the loot list's gathers
    readonly CraftPanelTracker _gatherBar = new();
    readonly List<(DateTime At, bool Ok)> _gatherRaces = new();
    /// <summary>Gathers the bar finished, ok and failed, since the start (the panel's own count).</summary>
    public int GatherBarOk { get; private set; }
    public int GatherBarFailed { get; private set; }

    // ---- the loot list band: the gathering panel, and rows that change in place ----
    DateTime _gatherPanelNoted = DateTime.MinValue;
    List<(int Top, string Text, DateTime At)> _prevRows = new();

    /// <summary>
    /// The lines of a loot list read without the gathering panel's: a "Success" or "Fail" bar label (or "Proficiency")
    /// marks it, and its block (title, proficiency, item, bars) runs from about three lines above that label down.
    /// </summary>
    internal List<(int Top, string Text)> WithoutGatherPanel(List<(int Top, string Text)> lines, DateTime now)
    {
        if (lines.Count == 0) return lines;
        var marks = lines.Where(l => System.Text.RegularExpressions.Regex.IsMatch(l.Text.Trim(), @"^\W*[il1|]?\s*(?:success|fail(?:ed|ure)?|profic\w*|profes\w*|pr\w{0,3}fes\w*)\b", System.Text.RegularExpressions.RegexOptions.IgnoreCase)).ToList();
        if (marks.Count == 0) return lines;
        var tops = lines.Select(l => l.Top).OrderBy(t => t).ToList();
        var gaps = tops.Zip(tops.Skip(1), (a, b) => b - a).Where(d => d > 4).OrderBy(d => d).ToList();
        int gap = gaps.Count > 0 ? gaps[gaps.Count / 2] : 30;
        // the panel's block: from three lines above its first marker (the node's name, its proficiency) to the bottom
        int cut = marks.Min(m => m.Top) - (int)(gap * 2.5);
        var kept = lines.Where(l => l.Top < cut).ToList();
        if ((now - _gatherPanelNoted).TotalSeconds > 20)
        {
            _gatherPanelNoted = now;
            CrashLog.Note($"the gathering panel shows at the bottom of the loot list's place: its lines aren't pickups ({string.Join(" | ", lines.Where(l => l.Top >= cut).Select(l => l.Text))})");
        }
        return kept;
    }

    /// <summary>
    /// A row whose text changed in its place within two seconds (the game upgrading "Orichalcum Ore" to "Fine Orichalcum
    /// Ore", a redraw) is one item: the earlier text stops waiting for the chat, so only the final one can be a pickup.
    /// </summary>
    void Supersede(List<(int Top, string Text)> lines, DateTime now)
    {
        var tops = lines.Select(l => l.Top).OrderBy(t => t).ToList();
        var gaps = tops.Zip(tops.Skip(1), (a, b) => b - a).Where(d => d > 4).OrderBy(d => d).ToList();
        int near = Math.Max(6, (int)((gaps.Count > 0 ? gaps[gaps.Count / 2] : 30) * 0.6));
        foreach (var (top, text) in lines)
        {
            var row = LootPopup.Rows(new[] { (top, text) }).FirstOrDefault();
            if (row == null || row.IsKinah) continue;
            foreach (var p in _prevRows.Where(p => Math.Abs(p.Top - top) <= near && (now - p.At).TotalSeconds <= 2))
            {
                var was = LootPopup.Rows(new[] { (p.Top, p.Text) }).FirstOrDefault();
                if (was == null || was.IsKinah || string.Equals(was.Name, row.Name, StringComparison.OrdinalIgnoreCase)) continue;
                // the same item's family: one name holds the other ("Fine Orichalcum Ore" holds "Orichalcum Ore")
                string a = was.Name.ToLowerInvariant(), b = row.Name.ToLowerInvariant();
                if (!(a.Contains(b) || b.Contains(a))) continue;
                if (_cross.Supersede(_cross.KeyOf(was))) CrashLog.Note($"loot list: \"{was.Name}\" turned into \"{row.Name}\" in its place: one item, the last text");
            }
        }
        _prevRows = lines.Select(l => (l.Top, l.Text, now)).ToList();
    }
    string _panelRecipe = ""; DateTime _panelTextAt = DateTime.MinValue; bool _panelUp, _panelIsCraft;

    async Task CraftPanelDue()
    {
        if (GameRect is not { } g) return;
        try
        {
            var region = CraftPanel.Region(g);
            if (Cover(region) is { } cover && !cover.StartsWith("Kinah Ledger")) return;
            using var bmp = ScreenReader.Capture(region);
            var now = DateTime.UtcNow;
            var s = CraftPanel.Measure(bmp, region, g);
            if (!s.Visible)
            {
                if (_panelUp && _panelIsCraft) { CrashLog.Note("crafting panel gone"); Accuracy.Add("craft_batches"); }
                _panelUp = false; _panelIsCraft = false;
                return;
            }
            if (!_panelUp || (now - _panelTextAt).TotalSeconds >= 5)
            {
                _panelTextAt = now;
                var text = await CraftPanel.ReadTextAsync(bmp, region, g);
                // the same Success/Fail bars run when an Odyle cube is opened or a node gathered (2026-10-22 19:26): only a
                // panel headed "Crafting in Progress" is crafting; any other is left alone, and its loot stays loot
                // once seen as crafting it stays so until the panel goes (a later read can come back garbled)
                bool craft = (_panelUp && _panelIsCraft) || CraftPanel.LastLines.Any(l => Update.RemoteConfig.HasAny(l, Update.RemoteConfig.Current.CraftPanelWords));
                if (craft != _panelIsCraft || !_panelUp) CrashLog.Note(craft ? "crafting panel up" : $"Success/Fail bars that aren't crafting (read: {string.Join(" | ", CraftPanel.LastLines)}): not counted");
                _panelIsCraft = craft;
                var recipe = craft && text is { } t ? CraftPanel.ItemOf(t.Recipe) : "";
                if (recipe.Length >= 2 && recipe != _panelRecipe) { _panelRecipe = recipe; CrashLog.Note($"crafting panel: {recipe} (read: {string.Join(" | ", CraftPanel.LastLines)})"); }
            }
            _panelUp = true;
            if (!_panelIsCraft)
            {
                // a node being gathered: its bar's outcome, for the audit (a stopped gather's bar never fills: no outcome)
                if (_gatherBar.Feed(now, s) is { } go)
                {
                    _gatherRaces.Add((now, go.Ok)); if (_gatherRaces.Count > 50) _gatherRaces.RemoveAt(0);
                    if (go.Ok) GatherBarOk++; else GatherBarFailed++;
                    CrashLog.Note($"gathering bar: {(go.Combo ? "upgrade roll " : "")}{(go.Ok ? "success" : "fail")} (success bar {s.Success:P0}, fail bar {s.Fail:P0})");
                    GatherOutcome(now, go.Ok, go.Combo);
                }
                return;
            }
            if (!_craftSeen) { _craftSeen = true; ReportLayout(); }
            // the panel's picture for a miscount report (the panel's own strip only, at most once a second)
            if (ReportBuffer.Keeps("craft")) try { Report.Picture("craft", bmp, region.Location, now, minGapSeconds: 1); } catch (Exception ex) { CrashLog.Write("report picture", ex); }
            // the crafting window's hold and its pictures last as long as the queue runs, and the passes keep a fight's pace
            Target.MarkCraft(now); _lastActivity = now;
            foreach (var e in new[] { Engine, Secondary, ZoneView, CraftView }) e?.CraftPanelSeen(_panelRecipe, now);
            if (_panel.Feed(now, s) is not { } o) return;
            var ev = new CraftResult(now, _panelRecipe, o.Ok, o.Combo) { Via = SessionEngine.CraftPanelVia };
            CrashLog.Note($"crafting panel: {(o.Combo ? "combo roll " : "")}{(o.Ok ? "success" : "fail")} for {(_panelRecipe.Length > 0 ? _panelRecipe : "an unread recipe")} (success bar {s.Success:P0}, fail bar {s.Fail:P0})");
            Count(ev);
            CraftView.Apply(ev);
            Accuracy.Add("craft_panel");
            Report.Counted(now, $"craft {(o.Ok ? "success" : "fail")}{(o.Combo ? " (combo)" : "")}: {(_panelRecipe.Length > 0 ? _panelRecipe : "?")}", ev.Via);
            try { Parsed?.Invoke(ev); } catch (Exception ex) { CrashLog.Write("parsed event", ex); }
        }
        catch (System.ComponentModel.Win32Exception) { throw; }
        catch (Exception ex) { CrashLog.Write("crafting panel", ex); }
    }

    async Task TargetDueAsync()
    {
        if ((DateTime.UtcNow - _targetAt).TotalSeconds < 1) return;   // every second: a fast kill is over in two or three
        _targetAt = DateTime.UtcNow;
        if (GameRect is not { } g) return;
        try
        {
            var cover = Cover(TargetReader.Strip(g));
            bool own = cover != null && cover.StartsWith("Kinah Ledger");
            // the panel (or this window) over part of the band is read through: its own labels are never on the enemy list.
            // Only when no enemy has been read for a minute does the window ask to drag it aside.
            if (cover == null || own)
            {
                await Target.TickAsync(g);
                BossTick(DateTime.UtcNow);
                TargetNote = own && (Target.Name == null || (DateTime.UtcNow - Target.NameAt).TotalSeconds > 60) ? "the panel sits over the target bar; drag it aside if enemies don't show" : "";
            }
            else TargetNote = cover!.StartsWith("the game") ? "" : cover + " covers the target bar";
        }
        catch (Exception ex) { CrashLog.Write("target bar", ex); }
    }

    /// <summary>
    /// The zone on its own clock, whether or not the chat can be read: every ZoneEverySec while a zone is known, every
    /// 30 seconds while none is ("not read yet"), as long as nothing covers the minimap corner.
    /// </summary>
    bool _rewardWasOpen;
    async Task ZoneDueAsync()
    {
        int every = Engine.Zone == null ? 5 : _settings.Ocr.ZoneEverySec;   // no zone yet (a menu, a loading screen): try again every 5 s, as long as it takes
        // inside an event or expedition, every 3 s: when it ends the game moves the player on, and what drops there must
        // never land in the event (Steven, 2026-10-22). A reward screen closing is read at once (below).
        if (ZoneLocator.KindOf(Engine.Zone) != null) every = Math.Min(every, 3);
        bool rewardNow = Target.RewardOpen(DateTime.UtcNow);
        if (_rewardWasOpen && !rewardNow) _zoneAt = DateTime.MinValue;
        _rewardWasOpen = rewardNow;
        if ((DateTime.UtcNow - _zoneAt).TotalSeconds < every) return;
        var game = GameWindow.Find(_settings.ProcessHints);
        GameRect = game ?? GameRect;
        if (game is not { } g) { _zoneAt = DateTime.UtcNow; return; }
        // the panel over the game steps aside for the read when it sits on the minimap corner; the minimap then has its own
        // cover check: another window over it alone keeps the last zone and lets the chat be read
        // read from the game's own frames, the panel isn't in the picture: no stepping aside, no flicker
        async Task Read() { if (Cover(ZoneLocator.Strip(g)) == null) await ReadZone(g); else _zoneAt = DateTime.UtcNow; }
        if (WindowCapture.Active) await Read();
        else await Ui.HudWindow.HiddenIfOver(ZoneLocator.Strip(g), Read);
    }

    public async Task StopAsync()
    {
        OfferBest();   // the window closing ends the stay in this zone too
        _cts.Cancel();
        if (_loop != null) { try { await _loop; } catch (Exception) { /* loop ended */ } }
        FlushVotes();   // lines still waiting for their vote count as read, before the run closes
        CommitHeld();   // and a rare pickup still waiting for its second read
        lock (_chatShotLock) { _chatShot?.Dispose(); _chatShot = null; }
        Instances.Close();
        Engine.Finish();
    }

    /// <summary>
    /// Counts the lines still waiting for a vote (a stop, the end of a replay): the last read once more, just after the
    /// vote's time is up, so they go through the same path as any other line, reward screen and crafting window included.
    /// </summary>
    internal void FlushVotes(DateTime? at = null)
    {
        if (_voter.Waiting == 0 || _prevRead.Count == 0) return;
        ProcessChat(new List<string>(_prevRead), (at ?? DateTime.UtcNow).AddSeconds(ChatVoter.MaxSeconds + 0.1));
        CommitHeld();
    }

    /// <summary>The player's region key and server label ("na_e", "Siel (Elyos)"), sent with the run so the site can keep drops per server.</summary>
    public string RegionKey { get; set; } = "";
    public string Server { get; set; } = "";

    public Task<bool> UploadAsync(Settings s) => UploadAsync(s, Engine, Kind, Activity);

    /// <summary>
    /// Shares one engine's totals (a farming run, or a chunk of the always-on count) under the given kind, with the
    /// expedition and event runs that finished meanwhile. Under five minutes or empty: nothing is sent. A send that
    /// doesn't reach the site puts the runs back for the next one; with sharing off they are let go, never sent later.
    /// </summary>
    public async Task<bool> UploadAsync(Settings s, SessionEngine e, string kind, string activity)
    {
        if (e.Elapsed.TotalMinutes < 5 || e.Events == 0) return false;
        var runs = Instances.TakeUnshared(e.Started);
        bool ok = await Uploader.SendAsync(RunPayload.Build(e, s, kind, activity, RegionKey.Length > 0 ? RegionKey : s.Region, Server, _items, runs), s.Upload);
        if (!ok && s.Upload.Enabled && !string.IsNullOrWhiteSpace(s.Upload.Endpoint)) Instances.Restore(runs);
        // the reading health since the last report goes with the run (numbers, item names as read, the layout)
        try { await AccuracyClient.SendAsync(s.Upload); } catch (Exception ex) { CrashLog.Write("accuracy report", ex); }
        return ok;
    }
}

/// <summary>What the window remembers between runs, kept per user.</summary>
public sealed class UserPrefs
{
    public string Activity { get; set; } = "Open world";
    public string Place { get; set; } = "";
    public bool Hud { get; set; } = true;
    /// <summary>How solid the panel over the game is, 0.3 to 1.</summary>
    public double HudOpacity { get; set; } = 0.9;
    public bool Share { get; set; } = true;
    /// <summary>Keep the last minute of chat and loot list pictures for miscount reports (ReportBuffer). Off until the player says yes: they can show other players' names.</summary>
    public bool ReportFrames { get; set; }
    /// <summary>Send a miscount report's loot list and crafting panel pictures (never the chat) to the site, where they become replay tests. Off until the player turns it on.</summary>
    public bool SendReportFrames { get; set; }
    /// <summary>True once the player chose the chat by hand; auto-detection then stays off until they switch back.</summary>
    public bool ManualChat { get; set; }
    /// <summary>No longer used: the window is always the live count, with setup in Options. Kept so older ui.json files load.</summary>
    public bool Compact { get; set; }
    /// <summary>The Drops and Gathered sections both show at once; each folds to its header on a click, and stays folded.</summary>
    public bool DropsOpen { get; set; } = true;
    public bool GatherOpen { get; set; } = true;
    public bool MobsOpen { get; set; } = true;
    /// <summary>Soul Crystals sit folded at the bottom until opened.</summary>
    public bool PetsOpen { get; set; }
    /// <summary>Off (the default): the tables start over when the zone changes. On: they keep counting across zones. What is shared is split by zone either way.</summary>
    public bool AllZones { get; set; }
    /// <summary>The Odds column as a percentage ("4.9%") instead of "1 in 21"; a click on its header switches.</summary>
    public bool OddsAsPercent { get; set; }
    /// <summary>Which part of the window shows: "Farming" (the run, the stats, the tables) or "Crafting" (recipes, cost, success, profit).</summary>
    public string Part { get; set; } = "Farming";
    /// <summary>Market prices: read and share the listings while the Market is open. On by default; off keeps everything else working.</summary>
    public bool ShareMarket { get; set; } = true;
    /// <summary>True once the player chose the Market area by hand; the title-and-headers search then stays off until they switch back.</summary>
    public bool ManualMarket { get; set; }
    /// <summary>The chosen server's label ("Siel (Elyos)"), sent with prices and runs so they land on the right board. Nothing is shared without it.</summary>
    public string Server { get; set; } = "";
    /// <summary>The chosen server's region key ("na_e"); empty means settings.json's region.</summary>
    public string Region { get; set; } = "";
    /// <summary>True once the first-run screen (region, faction, server, look) has been completed.</summary>
    public bool Welcomed { get; set; }
    /// <summary>The look: night, abyss or daylight.</summary>
    public string Skin { get; set; } = "night";
    /// <summary>Install a newer version on its own (downloaded in the background, swapped in when the window closes). Off: the window asks first.</summary>
    public bool AutoUpdate { get; set; } = true;
    /// <summary>The version that last opened the window, so the first start after an update can say so.</summary>
    public string LastVersion { get; set; } = "";
    /// <summary>When the game starts while the tracker waits in the tray: "open" shows the window, "tray" stays in the tray. Reading starts either way.</summary>
    public string OnGameStart { get; set; } = "open";
    /// <summary>Hide to the tray once the game has been closed for a few seconds (never during a run).</summary>
    public bool HideWhenGameCloses { get; set; }
    /// <summary>The window's close button hides it to the tray; Quit in the tray menu ends the tracker. On for new installs since 0.7.6, so a habit close doesn't stop the count.</summary>
    public bool CloseToTray { get; set; } = true;
    /// <summary>Minimizing always hides to the tray, not only during a run. On for new installs since 0.7.6.</summary>
    public bool MinimizeToTray { get; set; } = true;
    /// <summary>What the panel over the game shows, in this order: time, rate, kinah, loot, total, best, last (HudFields.All). The player picks in Options › In game.</summary>
    public string HudFields { get; set; } = Ui.HudFields.Default;
    /// <summary>Full view: the window doubles in width with every table beside the summary. Off (Compact, the default since 0.7.6): the run card and the summary only.</summary>
    public bool FullView { get; set; }
    /// <summary>The start/stop shortcut, as "Ctrl+Shift+K"; empty is off.</summary>
    public string Hotkey { get; set; } = Platform.Hotkey.Default;
    public int? WinX { get; set; }
    public int? WinY { get; set; }
    /// <summary>Where the over-game panel was last dragged to.</summary>
    public double? HudX { get; set; }
    public double? HudY { get; set; }

    static string PathFor()
    {
        var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "KinahLedger");
        Directory.CreateDirectory(dir);
        return Path.Combine(dir, "ui.json");
    }

    public static UserPrefs Load()
    {
        try { var p = PathFor(); if (File.Exists(p)) return JsonSerializer.Deserialize<UserPrefs>(File.ReadAllText(p)) ?? new UserPrefs(); }
        catch (Exception) { /* corrupt prefs: start fresh */ }
        return new UserPrefs();
    }

    public void Save() { try { File.WriteAllText(PathFor(), JsonSerializer.Serialize(this)); } catch (Exception) { /* read-only profile */ } }
}
