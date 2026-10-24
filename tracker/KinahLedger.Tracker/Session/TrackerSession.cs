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

    // ---- the candidate sets a misread name is settled among (brief item 1, 2026-10-24) ----
    // the time of the read being handled (a replay's own clock), and the enemies lately on the target bar
    DateTime _ctxNow = DateTime.MinValue;
    readonly List<(string Name, DateTime At)> _recentTargets = new();
    public const double RecentTargetSeconds = 60, GatheringContextSeconds = 60;

    /// <summary>
    /// What a misread could be right now, in the order tried: the loot tables of the enemy on the target bar, those lately
    /// on it and those whose nameplates show ("target-table"); the gathering materials while gathering ("zone-gather": the
    /// item list has no zone-by-zone gathering table, so a gather in the last minute stands for "gathering here"); the open
    /// recipe's outputs ("recipe"); this run's items ("run"). Closed (a misread fitting none of them may be refused) only in
    /// the open world while fighting an enemy whose loot table is known: the same condition as a guess held back.
    /// </summary>
    internal NameContext NameContextNow()
    {
        var now = _ctxNow == DateTime.MinValue ? DateTime.UtcNow : _ctxNow;
        if (Target.Name is { } tn && Target.NameAt != DateTime.MinValue)
        {
            _recentTargets.RemoveAll(x => x.Name == tn);
            _recentTargets.Add((tn, Target.NameAt));
        }
        _recentTargets.RemoveAll(x => (now - x.At).TotalSeconds > RecentTargetSeconds || (x.At - now).TotalSeconds > 5);
        if (_recentTargets.Count > 4) _recentTargets.RemoveRange(0, _recentTargets.Count - 4);
        var enemies = _recentTargets.Select(x => x.Name).Concat(Engaged).Distinct(StringComparer.Ordinal).ToList();
        bool outdoors = ZoneLocator.KindOf(Zone) == null && !InRun;
        var book = LootBook.Current;
        var ctx = new NameContext { Closed = outdoors && enemies.Any(book.Knows) };
        ctx.Add(NameMatch.TargetTable, enemies.SelectMany(e => book.TableOf(e).Select(x => x.Item)));
        bool gathering = (now - _lastGatherAt).TotalSeconds <= GatheringContextSeconds;
        lock (_gatherTokens) gathering |= _gatherTokens.Any(t => (now - t.At).TotalSeconds <= GatheringContextSeconds && (t.At - now).TotalSeconds <= 5);
        if (gathering) ctx.Add(NameMatch.ZoneGather, GatheringCatalog.All.Select(g => g.Name));
        if (_panelRecipe.Length >= 2 && (_panelUp || CraftView.CraftPanelActive(now)))
        {
            var r = RecipeBook.ForOutput(_panelRecipe);
            ctx.Add(NameMatch.Recipe, new[] { _panelRecipe }.Concat(r?.GreatOutcomes.Select(g => g.Item) ?? Enumerable.Empty<string>()));
        }
        ctx.Add(NameMatch.Run, Engine.ItemRows().Select(x => x.Name));
        return ctx;
    }

    /// <summary>A loot list row's name, settled the same way; a rare item is never refused (it goes to its second read).</summary>
    NameMatchResult ResolveName(string name)
    {
        var m = _items.Match(name, NameContextNow());
        return m.Source == NameMatch.Refused && m.Id is long r && IsRare(r) ? m with { Source = NameMatch.Full } : m;
    }

    /// <summary>A reader for the chat at <paramref name="r"/>: sized to its text, its captures handed to the miscount report.</summary>
    ScreenReader NewReader(System.Drawing.Rectangle r) =>
        new(r, _settings.Ocr.Scale, _textScale) { Tap = b => Report.Picture("chat", b, r.Location, DateTime.UtcNow), Keep = b => KeepChatShot(b, r.Location), NameSpans = NameDecoder.Present ? ShadowChatNames : null, SkipSame = () => Update.RemoteConfig.Current.ChatSkipSame };

    /// <summary>
    /// The name decoder's shadow on the chat (counts nothing): each pickup line's item name cropped from the capture it was
    /// read from, with what OCR plus NameMatch made of the same name (<see cref="ResolveName"/>), for the decoder's thread.
    /// </summary>
    void ShadowChatNames(System.Drawing.Bitmap raw, IReadOnlyList<(string Line, string Name, System.Drawing.Rectangle Box)> spans)
    {
        foreach (var s in spans)
            if (NameDecoder.PickupLine(s.Line))
                NameDecoder.Shadow("chat", NameDecoder.Crop(raw, s.Box), s.Name, ResolveName(s.Name).Id);
    }

    /// <summary>
    /// The name decoder's shadow on the loot list (counts nothing): each item row cropped from this read's picture, with
    /// what OCR plus NameMatch made of it. Skipped where the list isn't read (a reward screen, the crafting panel or window).
    /// </summary>
    void ShadowListNames(System.Drawing.Bitmap raw, System.Drawing.Point origin, List<OcrLineBox> boxes, List<(int Top, string Text)> lines, DateTime now)
    {
        if (Target.RewardOpen(now) || CraftView.CraftPanelActive(now) || TargetReader.CraftOpen(Target.CraftAt, now)) return;
        for (int i = 0; i < lines.Count && i < boxes.Count; i++)
        {
            if (LootPopup.Rows(new[] { lines[i] }).FirstOrDefault() is not { IsKinah: false } row) continue;
            var box = boxes[i].Box;
            box.Offset(-origin.X, -origin.Y);
            NameDecoder.Shadow("list", NameDecoder.Crop(raw, box), lines[i].Text, ResolveName(row.Name).Id);
        }
    }

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
        // what the tracker thought was going on, and how it changed over the last ten minutes
        sb.AppendLine($"Doing now: {Doing.Current.Label} (since {Doing.Current.Since.ToLocalTime():HH:mm:ss})" + (Instances.Pending is { } pend ? $" · the chat says {pend} is starting" : ""));
        sb.AppendLine("Activity, last 10 minutes: " + string.Join(" → ", Doing.Since(now.AddMinutes(-10)).Select(a => $"{a.Since.ToLocalTime():HH:mm:ss} {a.Label}")));
        if (Instances.Snapshot().LastOrDefault() is { } lastRun) sb.AppendLine($"Last run: {lastRun.Kind} · {lastRun.Label}, {lastRun.Start.ToLocalTime():HH:mm:ss} to {lastRun.LastLoot?.ToLocalTime():HH:mm:ss}{(lastRun.Ended.Length > 0 ? $", closed: {lastRun.Ended}" : ", open")} · seen by {lastRun.Witnesses}");
        sb.AppendLine($"Chat area: {Region} ({(ManualRegion ? "chosen by hand" : "found by itself")}) · read at {factor:0.##}x · text {textPx}");
        sb.AppendLine($"Counted this session: {t.Kinah:N0} Kinah · {t.Kills} kills · {Engine.Items.Values.Sum()} drops");
        sb.AppendLine($"Checks: Kinah counter agreed {bc.Agreed}×, over {bc.Over:N0}, under {bc.Under:N0} · quest kills {QuestKillsGame}, counted {QuestKillsCounted} · loot list added {_cross.Added}, matched {_cross.Matched}, chat repeats skipped {_cross.Duplicates}");
        sb.AppendLine($"Names: settled by colour {_parser.ColourPicks}, by loot table {_parser.TablePicks}, guesses no table backs {_parser.HeldUnlisted}, by a candidate set {_parser.ContextPicks}, refused as on no nearby table {_parser.Refused + _cross.Refused}, loot list rows named by icon {IconNamed}, lines changed by the vote {_voter.Changed}, reads that didn't line up {_tracker.Unaligned}");
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
            Feed(ev, Count(ev));
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
    /// <summary>
    /// The Kinah counter's rise that agrees with a reward slot's <paramref name="amount"/> within 1%: from any reading of the
    /// last minute to one taken within <paramref name="seconds"/> of <paramref name="t"/>, the closest to the amount. Null when
    /// none agrees. A table shows "600.0K"; the counter says exactly what came.
    /// </summary>
    internal long? BalanceRoseNear(long amount, DateTime t, double seconds = 10, double tolerance = 0.01)
    {
        if (amount < 1000) return null;
        lock (_balanceHist)
        {
            var near = _balanceHist.Where(b => (t - b.At).TotalSeconds <= 60 && (b.At - t).TotalSeconds <= seconds).OrderBy(b => b.At).ToList();
            long? best = null;
            for (int j = 1; j < near.Count; j++)
            {
                if (Math.Abs((near[j].At - t).TotalSeconds) > seconds) continue;
                for (int i = 0; i < j; i++)
                {
                    long rise = near[j].Balance - near[i].Balance;
                    if (rise > 0 && Math.Abs(rise - amount) <= amount * tolerance && (best == null || Math.Abs(rise - amount) < Math.Abs(best.Value - amount))) best = rise;
                }
            }
            return best;
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
        _parser.InRun = () => InRun;
        _parser.Likely = () => Engine.ItemRows().Select(r => r.Name)
            .Concat(new[] { Target.Name }.Concat(Engaged).Where(e => e != null).Distinct().SelectMany(e => LootBook.Current.TableOf(e!).Select(x => x.Item))).ToList();
        _parser.Fighting = Fighting;
        _parser.Rare = IsRare;
        // a misread settled by the candidate sets of the moment, and refused when it fits none of them (brief item 1)
        _parser.Context = NameContextNow;
        if (s.Ocr.NameColours) _parser.Colours = GradeColours.Load(ColoursPath);
        _textScale = s.Ocr.AutoScale ? new TextScale(s.Ocr.Scale, s.Ocr.TargetTextPx) { ToTarget = () => Update.RemoteConfig.Current.ChatScaleToTarget } : null;
        Engine = new SessionEngine(items);
        ZoneView = new SessionEngine(items);
        _cross = new LootCrossCheck(items) { Resolve = ResolveName };
        _cross.Gathered = GatherWitnessFor;
        _voter.Confirmed = l => FastChatOn && ListShows(_parser.Meaning(l).Key, _ctxNow);
        _rows = new LootRows(items) { NewGather = (canon, first) => IsGatherName(canon) && GatherRaceAfter(first), NotARow = (obj, why) => _cross.Withdraw(obj, why) };
        // the item-name decoder, shadow only: a look for its model files now, the model itself on first use (NameDecoder)
        NameDecoder.Start(items);
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
            else if (LastChat.For(game.Value) is { } last) { region = last; _chatKept = true; DetectNote = "where the chat was last found (it's hidden now)"; CrashLog.Note($"chat not visible at start: reading from its last place {last}"); }
            // nothing found and nothing kept (a frameless chat has no input box): where other trackers with this screen size found it
            if (region == null && await HintedChatAsync(game.Value) is { } hinted) { region = hinted; _chatKept = true; DetectNote = "found where other trackers with this screen size found the chat"; }
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
            if (zone == null && Engine.Zone == null && (DateTime.UtcNow - _zoneNoteAt).TotalSeconds >= 60) { _zoneNoteAt = DateTime.UtcNow; CrashLog.Note($"zone read: {note}"); }
            ApplyZone(zone, DateTime.UtcNow);
        }
        catch (Exception ex) { CrashLog.Write("zone", ex); }
        _zoneAt = DateTime.UtcNow;
    }

    /// <summary>
    /// A zone read, after the checks above (null: nothing new to say). Also what replay feeds the recorded zone reads
    /// through, so a run's edges are measured on what the minimap really showed.
    /// </summary>
    internal void ApplyZone(string? zone, DateTime now)
    {
        if (zone != null && zone != Engine.Zone)
        {
            if (zone != SessionEngine.UnknownZone) CrashLog.Note($"zone: {zone}" + (ZoneLocator.KindOf(zone) is { } kind ? $" ({kind})" : ""));
            // the trace keeps every zone change beside the chat, so a replay knows where each read happened
            ReadTrace.Signal(now, "zone", zone);
            Report.Lines("zone", new[] { zone }, now);
        }
        Engine.SetZone(zone);
        Secondary?.SetZone(zone);
        Instances.Zone(zone, now);
        // a new zone starts the zone view over: its tables are this zone's alone
        if (zone != null && ZoneView.Zone != null && zone != ZoneView.Zone) { OfferBest(); var fresh = new SessionEngine(_items); fresh.SetZone(zone); ZoneView = fresh; }
        else ZoneView.SetZone(zone);
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

    // the chat area in use came from where it was found last time or from other trackers' hint, because the chat wasn't in
    // view at start: it is looked for on screen again until it is found by its input box (RefindKeptChatAsync)
    bool _chatKept;
    DateTime _keptTryAt = DateTime.MinValue;
    /// <summary>How often a kept chat area is checked against the screen.</summary>
    public const double KeptRetrySeconds = 30;

    /// <summary>
    /// A kept chat area can be wrong for today's chat (2026-10-24, Steven's 5120x2160 screen: every start since the 23rd
    /// read "its last place", whose bottom sat three lines above the chat's newest line, so a line was read only once three
    /// more had pushed it up: pickups counted 2.3 to 9.4 s after they showed, and in a quiet chat not until it scrolled).
    /// Reads that show lines never trigger the empty-read re-find, so this looks for the chat every
    /// <see cref="KeptRetrySeconds"/> while the area is a kept one, and once its input box is found the area becomes the
    /// found one and is saved. No rebase: the lines below the old area's bottom were never read, so the next read's lines
    /// after the overlap count as new, once.
    /// </summary>
    async Task RefindKeptChatAsync()
    {
        if (!_chatKept || ManualRegion || (DateTime.UtcNow - _keptTryAt).TotalSeconds < KeptRetrySeconds) return;
        _keptTryAt = DateTime.UtcNow;
        try
        {
            if (GameWindow.Find(_settings.ProcessHints) is not { } g || Cover(ChatLocator.Corner(g)) != null) return;
            var (found, note) = await ChatLocator.DetectAsync(g);
            // only the input row is sure; a guess from the lines on screen is no better than the kept place
            if (found is not { } r || !note.Contains("input box", StringComparison.OrdinalIgnoreCase)) return;
            _chatKept = false;
            LastChat.Save(g, r);
            if (Region is { } was && was == r) { CrashLog.Note("chat found on screen where it was kept"); return; }
            CrashLog.Note($"chat found on screen at {r.X}, {r.Y} ({r.Width} x {r.Height}); the kept area was {Region}: reading the found one now ({note})");
            Region = r; _anchor = g; _reader = NewReader(r); DetectNote = note;
            ReportLayout();
        }
        catch (System.ComponentModel.Win32Exception) { throw; }
        catch (Exception ex) { CrashLog.Write("chat re-find", ex); }
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
                    // what is on screen, first: every reader below asks it (SceneState, Prompt 9)
                    await ScenePassAsync(p0);
                    var listGate = Verdict("loot_list");
                    var list = off.Off("loot_list") || listGate == Gate.Skip ? NoListRead() : Times.Time("list", () => LootReadAsync());
                    if (listGate == Gate.Skip) _rows.Reset();
                    if (!off.Off("target") && Runs("target")) await Times.Time("target", TargetDueAsync);
                    if (!off.Off("craft_panel")) await Times.Time("craft", CraftPanelDue);
                    var listRead = await list;
                    await Times.Time("list", () => LootGated(listRead, listGate));
                    if (!off.Off("reward_grid") && Runs("reward_grid")) await Times.Time("grid", RewardGridDue);
                    Times.Time("anchors", AnchorsDue);
                    NotePass((DateTime.UtcNow - p0).TotalMilliseconds);
                    Accuracy.Add("list_only", _cross.Added - _accAdded); _accAdded = _cross.Added;
                    Accuracy.Add("list_matched", _cross.Matched - _accMatched); _accMatched = _cross.Matched;
                    // the game is read, but not its chat: chat-not-found time for the health numbers
                    reading = true; Accuracy.AddChatLost((DateTime.UtcNow - t0).TotalSeconds);
                    Audit();
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
                    // what is on screen, first: every reader below asks it (SceneState, Prompt 9)
                    await ScenePassAsync(p0);
                    var listGate = Verdict("loot_list");
                    var list = off.Off("loot_list") || listGate == Gate.Skip ? NoListRead() : Times.Time("list", () => LootReadAsync());
                    if (listGate == Gate.Skip) _rows.Reset();
                    var chatGate = Verdict("chat");
                    if (chatGate != Gate.Skip) await Times.Time("chat", () => ReadOnce(chatGate, () => PeekList(list)));
                    await RefindKeptChatAsync();
                    if (!off.Off("target") && Runs("target")) await Times.Time("target", TargetDueAsync);
                    if (!off.Off("craft_panel")) await Times.Time("craft", CraftPanelDue);
                    var listRead = await list;
                    await Times.Time("list", () => LootGated(listRead, listGate));
                    Times.Time("grid", RewardShot);
                    if (!off.Off("reward_grid") && Runs("reward_grid")) await Times.Time("grid", RewardGridDue);
                    if (!off.Off("nameplates") && Runs("nameplates")) await Times.Time("nameplates", PlatesDue);
                    if (Runs("header")) await Times.Time("header", HeaderDue);
                    Times.Time("anchors", AnchorsDue);
                    NotePass((DateTime.UtcNow - p0).TotalMilliseconds);
                    // the loot list's part in the health numbers: pickups only it saw, and rows that matched a chat line
                    Accuracy.Add("list_only", _cross.Added - _accAdded); _accAdded = _cross.Added;
                    Accuracy.Add("list_matched", _cross.Matched - _accMatched); _accMatched = _cross.Matched;
                    reading = true;
                    Audit();
                }
                if (!Update.RemoteConfig.Current.Off("zone")) await ZoneDueAsync();
                UpdateDoing(DateTime.UtcNow);
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
        _probe?.Dispose(); _probe = null;
        SaveColours();
        Engine.Finish();
    }

    // ---- what is on screen (SceneState, Prompt 9) ----
    /// <summary>The scene classifier: one state per pass, which every reader in <see cref="Loop"/> asks before it runs.</summary>
    public SceneState Scene { get; } = new();
    /// <summary>What the gate says to <paramref name="reader"/> this pass (everything runs before the first frame: replays, the bank).</summary>
    internal Gate Verdict(string reader) => Scene.GateFor(reader);
    bool Runs(string reader) => Verdict(reader) == Gate.Run;
    /// <summary>How long the Kinah counter's numbers and the chat box's lines stand for "the HUD is up".</summary>
    public const double HudAnchorSeconds = 25, ChatAnchorSeconds = 30;

    /// <summary>
    /// The scene's part of a pass, before any reader: the crafting panel's locator, a thumbnail of the game window, the
    /// readers' signs, the classifier, then the reads held while the scene was Unknown, released or dropped.
    /// </summary>
    async Task ScenePassAsync(DateTime now)
    {
        _probe?.Dispose(); _probe = null;
        // the crafting panel's locator is the craft reader's time; the thumbnail, the classifier and the held reads the scene's
        if (!Update.RemoteConfig.Current.Off("craft_panel")) _probe = await Times.Time("craft", CraftProbeAsync);
        var scene = System.Diagnostics.Stopwatch.StartNew();
        System.Drawing.Bitmap? thumb = null;
        var sw = System.Diagnostics.Stopwatch.StartNew();
        try { if (GameRect is { } g) thumb = FrameThumb.Capture(g, _settings.ProcessHints); }
        catch (Exception ex) { CrashLog.Write("scene picture", ex); }
        if (thumb != null) Scene.AddPictureMs(sw.Elapsed.TotalMilliseconds);
        using (thumb) ScenePass(thumb, SignalsNow(now), now);
        await SettleHeldAsync(now);
        Times.Add("scene", scene.Elapsed.TotalMilliseconds);
    }

    /// <summary>One pass of the classifier (also what the tests feed their synthetic frames through).</summary>
    internal bool ScenePass(System.Drawing.Bitmap? thumb, SceneSignals signals, DateTime now) => Scene.Observe(thumb, signals, now);
    internal bool ScenePass(FrameStats? stats, SceneSignals signals, DateTime now) => Scene.Observe(stats, signals, now);

    /// <summary>The readers' signs this pass, from what they last saw.</summary>
    SceneSignals SignalsNow(DateTime now) => new()
    {
        CraftPanel = _probe is { Sample.Visible: true } && _panelIsCraft,
        CraftWindow = Target.CraftAt != DateTime.MinValue && (now - Target.CraftAt).TotalSeconds is >= -1 and <= SceneSignals.WindowSeconds,
        Market = MarketOpen?.Invoke(now) == true,
        RewardTitle = Target.RewardOpen(now),
        RewardTable = _gridSeenAt != DateTime.MinValue && (now - _gridSeenAt).TotalSeconds <= SceneSignals.WindowSeconds,
        EventCards = _eventScreenUp && (now - _eventScreenAt).TotalSeconds <= SceneSignals.WindowSeconds,
        CounterStrip = Anchors.SeenWithin("kinah-strip", now, HudAnchorSeconds),
        ChatBox = Region != null && _chatLinesAt != DateTime.MinValue && (now - _chatLinesAt).TotalSeconds <= ChatAnchorSeconds,
    };

    // ---- reads held while the scene is Unknown ----
    sealed record HeldRead(string Reader, DateTime At, List<string>? Chat, List<LootLine>? List);
    readonly List<HeldRead> _held = new();
    /// <summary>Held reads older than this, or past this many, are dropped while the scene stays Unknown.</summary>
    public const double HoldSeconds = 20; public const int HoldMax = 60;
    /// <summary>Reads held, released into a settled state that allows them, and dropped, since the start.</summary>
    public int SceneHeld { get; private set; }
    public int SceneReleased { get; private set; }
    public int SceneDropped { get; private set; }
    bool _holdOverflowNoted;

    void HoldRead(string reader, DateTime at, List<string>? chat, List<LootLine>? list)
    {
        _held.Add(new HeldRead(reader, at, chat, list));
        SceneHeld++;
        int old = _held.RemoveAll(h => (at - h.At).TotalSeconds > HoldSeconds);
        if (_held.Count > HoldMax) { old += _held.Count - HoldMax; _held.RemoveRange(0, _held.Count - HoldMax); }
        if (old > 0)
        {
            SceneDropped += old;
            if (!_holdOverflowNoted) { _holdOverflowNoted = true; CrashLog.Note($"scene still Unknown: held reads older than {HoldSeconds:0} s dropped ({Scene.Explain()})"); }
        }
    }

    /// <summary>For tests: how many reads are held now.</summary>
    internal int HeldNow => _held.Count;

    /// <summary>
    /// Once the scene settles (or the gate is off), each held read goes where the settled state says: counted, in the
    /// order read, at the time it was read, when its reader is valid there; dropped otherwise. One log line either way.
    /// </summary>
    internal async Task SettleHeldAsync(DateTime now)
    {
        if (_held.Count == 0 || (Scene.Enabled && !Scene.Settled)) return;
        var held = _held.ToList();
        _held.Clear(); _holdOverflowNoted = false;
        var go = held.Where(h => Verdict(h.Reader) == Gate.Run).ToList();
        var drop = held.Except(go).ToList();
        SceneReleased += go.Count; SceneDropped += drop.Count;
        string Readers(List<HeldRead> l) => string.Join(",", l.Select(h => h.Reader).Distinct());
        CrashLog.Note($"scene {Scene.Current}: {go.Count} read(s) held while Unknown released" + (go.Count > 0 ? $" ({Readers(go)})" : "") +
            (drop.Count > 0 ? $"; {drop.Count} dropped ({Readers(drop)}): not valid in {Scene.Current}" : ""));
        foreach (var h in go)
        {
            if (h.Chat != null)
            {
                ProcessChat(h.Chat, h.At);
                if (_rare.Waiting > 0) await ResolveRareAsync("chat", null, default);   // its picture is gone: counted as read, unconfirmed
            }
            else if (h.List != null)
            {
                ProcessList(h.List, h.At);
                if (_rare.Waiting > 0) await ResolveRareAsync("list", null, default);
            }
        }
    }

    /// <summary>
    /// One read of the chat (the game is in view there): count the new lines, re-find a lost chat, re-read the zone when
    /// due. <paramref name="gate"/> Hold (the scene is Unknown): the lines are read and held, not counted (ChatGated).
    /// </summary>
    async Task ReadOnce(Gate gate = Gate.Run, Action? beforeCount = null)
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
                // around the chat's anchor once it has held still (layout.anchor.chat), else the whole bottom-left quarter
                var near = Anchors.ChatCrop.Crop(g.Value, Update.RemoteConfig.Current.AnchorChat);
                var (again, note) = await ChatLocator.DetectAsync(g.Value, near);
                // not there: the whole quarter for this one look, the lock kept (2026-10-24: one miss dropped it and the next
                // pass took it again, 13:07:23 to 13:07:41, the chat on screen throughout). Only a miss in the whole quarter
                // too counts against the anchor; found elsewhere, the next sighting moves it (AnchoredCrop drops it then)
                if (again == null && near != null)
                {
                    (again, note) = await ChatLocator.DetectAsync(g.Value);
                    if (again == null) Anchors.ChatCrop.Missing(g.Value, "the chat wasn't found around its anchor or in the corner");
                }
                if (again != null && again != Region) { Region = again; _reader = NewReader(again.Value); _anchor = g; DetectNote = "re-found: " + note; ReportLayout(); }
                _chatLost = again == null;
            }
        }
        if (gate != Gate.Run) { ChatGated(lines, DateTime.UtcNow, gate); return; }
        beforeCount?.Invoke();   // the loot list read beside it, when it is in already (PeekList)
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
        // counted where it belongs, and stamped with where that was (the activity: a run, farming, a reward screen)
        Feed(ev, Count(ev));
        // crafting is fed by the crafting panel's races alone (CraftPanelDue), never by a pickup or a chat line
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
        _ctxNow = now;
        Seen(now);
        // rare pickups still waiting from an earlier step that had no picture to read again (a replay): counted as read
        CommitHeld();
        try { ProcessChatCore(lines, now); }
        finally { ReleaseAwaiting(now); foreach (var e in new[] { Engine, Secondary, ZoneView }) e?.SettleQuest(now); }
    }

    void ProcessChatCore(List<string> lines, DateTime now)
    {
        int unaligned = _tracker.Unaligned;
        var fresh = _tracker.NewLines(lines);
        // the chat's own word on an event: "joined event" in its lobby, its NPC's lines (EventWitness). A lobby filling up
        // scrolls the chat faster than reads line up (11:33:16 today, ten "joined event" lines at once): from such a read, the
        // lines not seen in the last ten minutes
        var witnessed = _tracker.Unaligned > unaligned ? Reread.Unseen(_recent, lines, now) : fresh;
        foreach (var (src, name) in EventWitness.Read(witnessed)) Instances.Witness(src, name, now, Zone);
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
        // a Transcendence: the dungeon at a stage ("Shattered Arkanis [Stage 1] | Transcendence", "Set to Stage 1")
        if (lines.Any(IsTranscendenceLine)) Instances.MarkTranscendence(now);
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
                // what any path counted just before the garbling began is counted too: read a little differently then, the
                // settle took it for new (the noisy synthetic fights: "Acquired Spiritstone x4." counted twice)
                lock (_countedLines) _countedGarbled.AddRange(_countedLines.Where(x => (now - x.At).TotalSeconds <= 30).Select(x => x.Line));
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
        // an empty read (the chat fading out) is not the last read: the settle needs the last read that showed lines
        // (Steven, 2026-10-23 11:07: all 21 event rewards lost when the chat faded right after the clean read)
        if (lines.Count > 0) { _lastLines = lines; _chatLinesAt = now; }
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
                var why = (parsed is ItemAcquired || parsed is PetCrystal) && !made ? (batchAchievement ? "an achievement's reward" : RewardWhy(parsed.Time, parsed is ItemAcquired bx && BoxName.IsMatch(_items.NameOf(bx.ItemId)), parsed is ItemAcquired ri ? ri.ItemId : 0)) : null;
                // a stack no enemy drops, out in the world (a gathering material is a gather, an event's grid is the run's)
                if (why == null && !made && parsed is ItemAcquired big && !InRun && !IsGatherMaterial(big.ItemId)) why = PickupCheck.TooMany(big.Count);
                if (why == null && parsed is PetCrystal bigc && !InRun) why = PickupCheck.TooManyCrystals(bigc.Count);
                // Soul Crystals from a quest or a reward are not crystals from kills: left out, so the per hour stays the kills'
                if (why != null && parsed is PetCrystal wp) { CrashLog.Note($"{wp.Name} x{wp.Count}: {why}, not counted with the kills"); any = true; continue; }
                if (why != null && parsed is ItemAcquired wa) CrashLog.Note($"{_items.NameOf(wa.ItemId)} x{wa.Count}: {why}, not a drop");
                // in a base only Odyle is farmed: anything else there is a buy, mail or a reward, never a drop
                var ev = (reward || why != null || InBaseNotOdyle(parsed)) && !made ? _parser.AsReward(parsed) : parsed;
                // an item that is a reward only because a window was open doesn't open a new one: a window would chain on
                // and take the next kill's drop (a payout's item, then a Spiritstone 10 s later, 2026-10-23)
                if (why == null) NoteReward(ev, now);
                any = true; _lastActivity = DateTime.UtcNow;
                // a rare drop waits for the second read of this chat picture (ReadOnce, the same pass) before it counts
                if (ev is ItemAcquired ra && IsRare(ra.ItemId)) { _rare.Hold(new RareGate.Pending(ev, "chat", raw, now)); continue; }
                // no single-signal counting: out in the world, an item is a drop only with a kill or a gather around it
                if (ev is ItemAcquired wi && NeedsWitness(wi, made, crafting)) { lock (_awaiting) _awaiting.Add((ev, now)); continue; }
                Commit(ev);
            }
            return any;
        }
        // each new line is voted on across the next reads before it counts (ChatVoter); lines come out in order
        var voted = Vote ? _voter.Push(fresh, lines, now, _parser.Meaning) : fresh;
        // how many loot lines land at once: three or more are a run's rewards coming in (InstanceLog.Route, Loot)
        _batch = voted.Count(Reread.LooksLikeLoot) + reread.Count + settled.Count;
        // an achievement line among them: the items printed with it are its reward, and those in the next few seconds too
        // only an achievement line that is new: one that stays on screen is read a little differently each time
        batchAchievement = voted.Any(l => Reread.IsAchievement(l) && NewLine(l, now));
        if (batchAchievement) { _achieveUntil = now.AddSeconds(RewardWindowSeconds); CrashLog.Note($"achievement: {voted.First(Reread.IsAchievement)}; its rewards are not drops"); }
        var thisRead = new List<string>();   // lines the vote and the read-again count in this read, for the settle below
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
            // while the chat is garbled, what the vote counts is remembered too, so the settle doesn't count it again
            if (any) { LastLine = l; LootLine?.Invoke(l); if (_garbledSince != null) _countedGarbled.Add(l); NoteCounted(l, now); thisRead.Add(l.Trim()); }
        }
        batchAchievement = reread.Concat(fresh).Any(l => Reread.IsAchievement(l) && NewLine(l, now));
        foreach (var l in reread)
        {
            bool any = false;
            foreach (var (_, events) in _parser.ParseBatch(new List<string> { l }, now, wrap: false)) any |= Apply(events, l, "chat · recovered");
            CrashLog.Read(l + "    (read again: it was garbled while fading in)", any);
            if (any) { LastLine = l; LootLine?.Invoke(l); if (_garbledSince != null) _countedGarbled.Add(l); NoteCounted(l, now); thisRead.Add(l.Trim()); }
        }
        // dated when the garbling began, which is when they landed (an event's rewards: inside its time for rewards)
        // only an achievement line that first showed with the garbled reads: one that sat in the chat before is an old one
        if (settledAchievement) settledAchievement = _lastLines.Any(l => Reread.IsAchievement(l) && (Reread.FirstSeen(l, _firstSeen) is not DateTime f || f >= settledAt.AddSeconds(-5)));
        batchAchievement = settledAchievement;
        if (settledAchievement) { _achieveUntil = settledAt.AddSeconds(RewardWindowSeconds); CrashLog.Note("the settled lines came with an achievement: they are its reward, not drops"); }
        foreach (var l in settled)
        {
            // the vote or the read-again in this same read just counted it: the settle picked its lines before they ran
            int dup = thisRead.FindIndex(x => Reread.Same(x, l.Trim()));
            if (dup >= 0) { thisRead.RemoveAt(dup); CrashLog.Read(l + "    (counted already in this read, not again)", false); continue; }
            bool any = false;
            foreach (var (_, events) in _parser.ParseBatch(new List<string> { l }, settledAt, wrap: false)) any |= Apply(events, l, "chat · recovered");
            CrashLog.Read(l + "    (counted once the chat settled)", any);
            if (any) { LastLine = l; LootLine?.Invoke(l); settledAny = true; lock (_settledCounted) _settledCounted.Add((l.Trim(), now)); NoteCounted(l, now); }
        }
        if (settledAny)
        {
            CrashLog.Note($"the chat settled after {(now - settledAt).TotalSeconds:0} s of reads that didn't line up; its loot lines counted from the clean read: {string.Join(" | ", settled)}");
            _garbledSince = null;
        }
        // ---- the loot ledger: a loot line no read in ten minutes showed, that nothing counted, is new wherever it sits ----
        // Lining reads up finds new lines at the bottom. A line one read lost and the next showed sits between lines
        // already seen, and was never counted (the synthetic fights; 497 Kina at 16:16 on 2026-10-23). Read exactly, in
        // two reads in a row, in a read that lined up, and at most two at once (more is history scrolled back into view).
        {
            // what any path took in the last few seconds, by meaning and by text: a garbled copy still waiting for its vote
            // is the same line as the clean one the ledger sees (the noisy synthetic fights counted those twice)
            foreach (var l in fresh.Concat(reread).Concat(settled)) _recentHandled.Add((l.Trim(), _parser.Meaning(l).Key, now));
            _recentHandled.RemoveAll(x => (now - x.At).TotalSeconds > HandledSeconds);
            var handled = new HashSet<string>(_recentHandled.Select(x => x.Key).Where(k => k.Length > 0));
            bool Handled(string line) => _recentHandled.Any(x => Reread.Same(x.Line, line.Trim()));
            var keyed = lines.Select(l => (Line: l, M: _parser.Meaning(l))).Where(x => x.M.Key.Length > 0).ToList();
            if (!_ledgerPrimed) { foreach (var x in keyed) _keySeen[x.M.Key] = now; _ledgerPrimed = true; }
            var unseen = keyed.Where(x => !handled.Contains(x.M.Key) && !(_keySeen.TryGetValue(x.M.Key, out var ks) && (now - ks).TotalMinutes <= LedgerMinutes)).ToList();
            bool aligned = _tracker.Unaligned == unaligned;
            if (aligned && unseen.Count is > 0 and <= 2 && Update.RemoteConfig.Current.Ledger)
                foreach (var (l, m) in unseen.GroupBy(x => x.M.Key).Select(g => g.First()))
                {
                    if (!m.Exact || Handled(l)) continue;
                    int c = _keyCandidate.GetValueOrDefault(m.Key) + 1;
                    if (c < 2) { _keyCandidate[m.Key] = c; continue; }
                    _keyCandidate.Remove(m.Key);
                    bool any = false;
                    foreach (var (_, events) in _parser.ParseBatch(new List<string> { l }, now, wrap: false)) any |= Apply(events, l, "chat · ledger");
                    CrashLog.Read(l + "    (between lines already seen: no read had shown it)", any);
                    if (any) { LastLine = l; LootLine?.Invoke(l); }
                    _keySeen[m.Key] = now;
                }
            var present = keyed.Select(x => x.M.Key).ToHashSet();
            foreach (var k in _keyCandidate.Keys.Where(k => !present.Contains(k)).ToList()) _keyCandidate.Remove(k);
            foreach (var k in present) if (!_keyCandidate.ContainsKey(k)) _keySeen[k] = now;
            foreach (var k in _keySeen.Where(kv => (now - kv.Value).TotalMinutes > LedgerMinutes).Select(kv => kv.Key).ToList()) _keySeen.Remove(k);
        }
        _batch = 1;
        UpdateDoing(now);
        LastError = "";
    }

    // ---- what the player is doing (Activity) ----
    /// <summary>What the player is doing now, as one state; every change is in reads.log. See <see cref="Activity"/>.</summary>
    public ActivityTracker Doing { get; } = new();
    /// <summary>True while the Market's listings are being read (set by the watcher, which runs the Market reader).</summary>
    public Func<DateTime, bool>? MarketOpen { get; set; }
    // the last farming sign in the open world (a kill, a drop, a gather, an enemy on the bar or around the character)
    DateTime _farmAt = DateTime.MinValue;
    /// <summary>Farming stops being the activity this long after its last sign.</summary>
    public const double FarmingIdleSeconds = 120;
    // how many loot lines are landing at once (set per read): three or more are a run's rewards
    int _batch = 1;
    // the last read of the game, for telling a pause from reading (InstanceLog.Away)
    DateTime _seenAt = DateTime.MinValue;

    /// <summary>A read happened at <paramref name="now"/>: a long gap since the last one closes an event that can't still be on.</summary>
    void Seen(DateTime now)
    {
        if (_seenAt != DateTime.MinValue && Instances.Away(_seenAt, now))
        {
            // the zone read before the pause isn't known to hold: Unknown zone until the minimap is read again
            if (ZoneLocator.KindOf(Zone) is { } k && k != ZoneLocator.Ignored) { Engine.SetZone(SessionEngine.UnknownZone); Secondary?.SetZone(SessionEngine.UnknownZone); _zoneAt = DateTime.MinValue; }
        }
        if (now > _seenAt) _seenAt = now;
    }

    /// <summary>Works out the activity from every signal (InstanceLog, the boss, crafting, the Market, reward screens, farming signs, the zone) and logs a change.</summary>
    internal void UpdateDoing(DateTime now)
    {
        Activity next;
        string why;
        var zone = Zone;
        if (_boss != null) { next = new Activity(ActivityKind.WorldBoss, _boss); why = _bossDeadAt == DateTime.MinValue ? "on the target bar" : "its loot window"; }
        else if (Instances.Current is { } run && run.Kind != InstanceLog.WorldBoss && !(run.Leaving && Target.RewardOpen(now) && Target.RewardTitle.Length > 0))
        {
            var kind = run.Kind switch { "Event" => ActivityKind.Event, "Arena" => ActivityKind.Arena, "Legion" => ActivityKind.Legion, _ => ActivityKind.Expedition };
            next = new Activity(kind, run.Zone, run.Event, run.Family, Leaving: run.Leaving);
            why = run.Leaving ? $"the minimap names {zone}" : "the run";
        }
        else if (CraftView.CraftPanelActive(now) || (TargetReader.CraftOpen(Target.CraftAt, now) && now >= Target.CraftAt)) { next = new Activity(ActivityKind.Crafting); why = "the crafting window or panel"; }
        else if (MarketOpen?.Invoke(now) == true) { next = new Activity(ActivityKind.Market); why = "the Market's listings"; }
        else if (Target.RewardOpen(now) && Target.RewardTitle.Length > 0) { next = new Activity(ActivityKind.RewardScreen, Target.RewardTitle); why = "its title at the top"; }
        else if (zone == null) { next = new Activity(ActivityKind.NotRead); why = "no zone read"; }
        else if (((now - _farmAt).TotalSeconds <= FarmingIdleSeconds || (Target.Name != null && (now - Target.NameAt).TotalSeconds is >= 0 and <= 15)) && !ZoneLocator.IsBase(zone)) { next = new Activity(ActivityKind.Farming, zone); why = "kills and drops"; }
        else if (ZoneLocator.IsBase(zone)) { next = new Activity(ActivityKind.Town, zone); why = "a base"; }
        else { next = new Activity(ActivityKind.Idle, zone == SessionEngine.UnknownZone ? "" : zone); why = $"nothing farmed for {FarmingIdleSeconds / 60:0} min"; }
        if (Instances.Pending is { } p && next.Kind is not ActivityKind.Event) why += $"; the chat says {p} is starting";
        Doing.Set(next, now, why);
    }

    /// <summary>The event header under the minimap (EventHeader), read or replayed: a witness for the run.</summary>
    internal void EventHeaderSeen(string name, DateTime t)
    {
        if (!string.Equals(name, _headerSaid, StringComparison.OrdinalIgnoreCase)) { CrashLog.Note($"event header: {name}"); ReadTrace.Signal(t, "header", name); Report.Lines("header", new[] { name }, t); _headerSaid = name; }
        Instances.Witness("header", name, t, Zone);
    }
    string? _headerSaid;
    DateTime _headerAt = DateTime.MinValue;

    /// <summary>
    /// The event header, every 3 s while an event is open or the chat says one is starting, and every 5 s while the zone
    /// is unknown; never while a reward screen is open (the Festival Shop lists the games) and never otherwise in the open
    /// world (its quest list can show a game's countdown in the same place).
    /// </summary>
    async Task HeaderDue()
    {
        var now = DateTime.UtcNow;
        if (GameRect is not { } g || Target.RewardOpen(now)) return;
        bool inEvent = Instances.Current is { Kind: "Event", Leaving: false };
        bool due = inEvent || Instances.Pending != null ? (now - _headerAt).TotalSeconds >= 3 : Zone == SessionEngine.UnknownZone && (now - _headerAt).TotalSeconds >= 5;
        if (!due) return;
        _headerAt = now;
        try
        {
            var region = EventHeader.Region(g);
            if (Cover(region) is { } cover && !cover.StartsWith("Kinah Ledger")) return;
            using var bmp = ScreenReader.Capture(region);
            var (name, _) = await EventHeader.ReadAsync(bmp, region.Location);
            if (name != null) EventHeaderSeen(name, now);
        }
        catch (System.ComponentModel.Win32Exception) { throw; }
        catch (Exception ex) { CrashLog.Write("event header", ex); }
    }

    readonly Dictionary<string, DateTime> _keySeen = new();
    readonly Dictionary<string, int> _keyCandidate = new();
    readonly List<(string Line, string Key, DateTime At)> _recentHandled = new();
    const double HandledSeconds = 8;
    bool _ledgerPrimed;
    const double LedgerMinutes = 10;

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
            if (names.Count > 0) _farmAt = now;
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
            if (GameRect is not { } g || ZoneLocator.KindOf(Zone) is not ("Expedition" or "Event")) { _gridDone = false; FlushEventCards(now, "the event was left"); return; }
            if ((now - _gridAt).TotalSeconds < 1) return;
            _gridAt = now;
            if (Cover(g) is { } cover && !cover.StartsWith("Kinah Ledger")) return;
            using var frame = ScreenReader.Capture(g);
            var band = new Rectangle(frame.Width / 4, 0, frame.Width / 2, frame.Height / 3);
            using var bandBmp = frame.Clone(band, frame.PixelFormat);
            var lines = await ScreenReader.ReadBoxesAsync(bandBmp, band.Location, 1);
            var rc = lines.FirstOrDefault(l => IsRewardCollected(l.Text));
            // an event's reward screen (Shugo Festival "Jump Jump": "Rewards have been sent to your cube.", "Can select 0 time(s)",
            // five cards, the received ones bright with a card icon): its rewards are the run's and come from this picture,
            // never the chat (Steven, 2026-10-23). The picture is kept (a few, a second or two apart), and once the cards are
            // face up ("sent to your cube") they are read (EventCards); a layout the reader doesn't know leaves the run's
            // rewards not read, as before
            // the cards' own frames count too: a banner over the title (the Daeva Pass's, 2026-10-24) must not hide the screen
            bool titled = rc == null && lines.Any(l => IsEventRewardTitle(l.Text));
            bool framed = false;
            if (rc == null && !titled && ZoneLocator.KindOf(Zone) == "Event" && (now - _cardsProbeAt).TotalSeconds >= 2)
            {
                // a quick look every 2 s, every read while they show
                framed = EventCards.LooksLikeCards(frame) && EventCards.Locate(frame, out _) != null;
                _cardsProbeAt = framed ? DateTime.MinValue : now;
            }
            if (titled || framed)
            {
                if (!_eventScreenUp) { _eventScreenUp = true; _eventShots = 0; CrashLog.Note("event reward screen up: its cards are the run's rewards"); }
                _eventScreenAt = now;
                Instances.EventRewardScreen(now);
                RewardScreenSeen(now);
                if (_eventShots < 6 && (now - _eventShotAt).TotalSeconds >= 1.5) { _eventShotAt = now; _eventShots++; SaveShot(frame, "event-rewards"); }
                if (framed && !_cardsByFrames) { _cardsByFrames = true; CrashLog.Note("event cards: found by their frames (the screen's title wasn't read)"); }
                // read once turned face up: "sent to your cube", or, found by the frames alone, slots on every received card
                if (framed || lines.Any(l => IsEventCardsUp(l.Text))) await EventCardsRead(frame, now);
                return;
            }
            _eventScreenUp = false;
            FlushEventCards(now, "the screen closed");
            if (rc == null) { if ((now - _gridSeenAt).TotalSeconds > 5) _gridDone = false; return; }
            _gridSeenAt = now;
            RewardScreenSeen(now);
            if (_gridDone) return;
            _gridDone = true;
            int top = rc.Box.Bottom + (int)(frame.Height * 0.016);
            var reads = new List<List<RewardGrid.Slot>> { await RewardGrid.ReadAsync(frame, top) };
            // the picture the run is counted from, kept (Steven, 2026-10-23: no frame of the Urugugu table existed to check)
            SaveShot(frame, "reward-table");
            // The table fades and slides in after "Reward collected." shows: read it again every 300 ms, up to 3 s, until two
            // reads in a row agree with every slot surely matched (2026-10-23 Urugugu: one early read, 5 slots 80 to 112, no
            // counts; the 10-22 table read right, 23 to 60). The last read is kept as a picture too.
            var started = DateTime.UtcNow;
            while (!RewardGrid.Settled(reads, GridSureDistance) && (DateTime.UtcNow - started).TotalSeconds < 3)
            {
                await Task.Delay(300);
                try
                {
                    using var again = ScreenReader.Capture(g);
                    // the table slides in: its header is found again in each frame, never taken from the first (2026-10-23 20:53,
                    // Vakron: every read after the first was off the slots, every grade Common, the 600.0K coin not named)
                    // no header: the table has closed (2026-10-23 21:11 Vakron, about a second after it opened). What was read
                    // stands; the world where the table was is never read as slots, and no settle is waited for
                    if (await TableTop(again) is not int t2) { CrashLog.Note("reward table: closed before it settled; counted from the cleanest read"); break; }
                    top = t2;
                    reads.Add(await RewardGrid.ReadAsync(again, top));
                    if (RewardGrid.Settled(reads, GridSureDistance) || (DateTime.UtcNow - started).TotalSeconds >= 3) SaveShot(again, "reward-table-settled");
                }
                catch (Exception ex) { CrashLog.Write("reward table again", ex); break; }
            }
            var slots = RewardGrid.Vote(reads, GridSureDistance);
            CrashLog.Note($"reward table: {reads.Count} reads in {(DateTime.UtcNow - started).TotalSeconds:0.0} s ({string.Join(", ", reads.Select(r => r.Count))} slots, {(RewardGrid.Settled(reads, GridSureDistance) ? "settled" : "never settled")}), voted slot by slot: " +
                string.Join(" | ", slots.Select(s => $"{s.Grade} x{s.CountText} {(s.Icons.Count > 0 ? s.Icons[0].Key + " " + s.Icons[0].Distance.ToString("0") : "no icon")}")));
            ApplyGrid(slots, now);
        }
        catch (Exception ex) { CrashLog.Write("reward table", ex); }
    }

    /// <summary>The reward table's slots into the run: each named item once, skipping what the chat already counted.</summary>
    /// <summary>Where a frame's reward table starts: just under its "Reward collected." line, or null when the line isn't there.</summary>
    static async Task<int?> TableTop(System.Drawing.Bitmap frame)
    {
        var band = new System.Drawing.Rectangle(frame.Width / 4, 0, frame.Width / 2, frame.Height / 3);
        using var bandBmp = frame.Clone(band, frame.PixelFormat);
        var lines = await ScreenReader.ReadBoxesAsync(bandBmp, band.Location, 1);
        var rc = lines.FirstOrDefault(l => IsRewardCollected(l.Text));
        return rc == null ? null : rc.Box.Bottom + (int)(frame.Height * 0.016);
    }

    /// <summary>A slot's closest icon is sure: close (right ones 23 to 60, wrong ones 82 to 114), clearly closer than the next, and of the slot's grade.</summary>
    /// <summary>
    /// The slot's sure item among its closest icons, judged against the others of the slot's own grade (2026-10-23
    /// Urugugu: the class gear's runner-up was the same art at another tier, e.g. a bow at 49 against 54, dual swords at 37
    /// against 39, so no gear for another class was ever named). The closest icon with items of the slot's grade wins when
    /// it is close, clearly closer than the next icon of that grade that names something else, and not far behind the
    /// closest icon of any grade (a much closer icon of another grade says the grade or the list is wrong). Null: not sure.
    /// </summary>
    internal static (string Key, double Distance, List<ItemInfo> Graded)? GradedPick(RewardGrid.Slot sl, Dictionary<string, List<ItemInfo>> byIcon, Func<string, bool>? prefer = null)
    {
        var cands = new List<(string Key, double Distance, List<ItemInfo> Graded, HashSet<string> Names)>();
        foreach (var (key, d) in sl.Icons)
        {
            if (key == null || !byIcon.TryGetValue(key, out var its)) continue;
            var g = its.Where(i => string.Equals(i.Grade, sl.Grade, StringComparison.OrdinalIgnoreCase)).ToList();
            if (g.Count > 0) cands.Add((key, d, g, g.Select(i => Unbound(i.Name)).ToHashSet(StringComparer.OrdinalIgnoreCase)));
        }
        if (cands.Count == 0) return null;
        var c0 = cands[0];
        if (c0.Distance > GridSureDistance) return null;
        if (sl.Icons.Count > 0 && c0.Distance > sl.Icons[0].Distance * 1.25) return null;
        var rival = cands.Skip(1).FirstOrDefault(c => !c.Names.SetEquals(c0.Names));
        if (rival.Key != null && rival.Distance < c0.Distance * 1.25)
        {
            // too close to call by the picture: when exactly one of the close ones belongs to a set this table names surely, or
            // came in this expedition's runs before, it is that one (Steven, 2026-10-23: the bow and the earrings tied at 54
            // and 68, Divine Canyon among Divine Canyon pieces; the dagger 49 to 61, Earth among Earth pieces)
            if (prefer == null) return null;
            var liked = cands.Where(c => c.Distance <= GridSureDistance && c.Distance < c0.Distance * 1.25 && c.Names.Count == 1 && prefer(c.Names.First()))
                             .GroupBy(c => c.Names.First(), StringComparer.OrdinalIgnoreCase).ToList();
            if (liked.Count != 1) return null;
            var w = liked[0].OrderBy(c => c.Distance).First();
            return (w.Key, w.Distance, w.Graded);
        }
        return (c0.Key, c0.Distance, c0.Graded);
    }

    /// <summary>A gear set's name: the item's name less its last word ("Divine Canyon Orb" is of "Divine Canyon").</summary>
    internal static string? SetOf(string name)
    {
        var w = name.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return w.Length >= 2 && string.Join(" ", w[..^1]).Length >= 3 ? string.Join(" ", w[..^1]) : null;
    }

    /// <summary>
    /// What a table's own sure names say about its close calls: an item of a set another slot names surely (expedition tables
    /// give out sets), or one this expedition gave before (<paramref name="history"/>).
    /// </summary>
    internal static Func<string, bool> SetMates(IEnumerable<RewardGrid.Slot> slots, Dictionary<string, List<ItemInfo>> byIcon, IEnumerable<string>? history = null)
    {
        var sets = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var s in slots)
            if (s.Count > 0 && GradedPick(s, byIcon) is { } p && p.Graded.Select(i => Unbound(i.Name)).Distinct(StringComparer.OrdinalIgnoreCase).ToList() is { Count: 1 } n && SetOf(n[0]) is { } set)
                sets.Add(set);
        var seen = new HashSet<string>(history ?? Array.Empty<string>(), StringComparer.OrdinalIgnoreCase);
        return name => seen.Contains(name) || (SetOf(name) is { } set && sets.Contains(set));
    }

    /// <summary>For the replay's report: the slot's closest icons that hold items of its grade, with the names they hold.</summary>
    internal static List<(string Key, double Distance, string Names)> GradedCandidates(ItemDictionary items, RewardGrid.Slot sl)
    {
        var byIcon = items.AllWithIcons().Where(i => i.Icon.Length > 0).GroupBy(i => IconIndex.KeyOf(i.Icon)).ToDictionary(x => x.Key, x => x.ToList());
        var res = new List<(string, double, string)>();
        foreach (var (key, d) in sl.Icons)
            if (key != null && byIcon.TryGetValue(key, out var its) && its.Where(i => string.Equals(i.Grade, sl.Grade, StringComparison.OrdinalIgnoreCase)).Select(i => Unbound(i.Name)).Distinct(StringComparer.OrdinalIgnoreCase).ToList() is { Count: > 0 } n)
                res.Add((key, d, string.Join(" / ", n)));
        return res;
    }

    internal static bool SureMatch(RewardGrid.Slot sl, int gradedCount) =>
        sl.Icons.Count > 0 && sl.Icons[0].Distance <= GridSureDistance && (sl.Icons.Count < 2 || sl.Icons[1].Distance >= sl.Icons[0].Distance * 1.25) && gradedCount > 0;

    /// <summary>
    /// The item a slot names, the way ApplyGrid takes it (the replay bank scores the reader with it): the gold coin as "Kina",
    /// a sure match of one name, or null (not read: unsure, or one of several names).
    /// </summary>
    internal static string? SlotName(ItemDictionary items, RewardGrid.Slot sl, Func<string, bool>? prefer = null)
    {
        if (sl.Count <= 0) return null;
        var byIcon = ByIcon(items);
        var best = sl.Icons.FirstOrDefault();
        if (best.Key == null || !byIcon.TryGetValue(best.Key, out var its)) return null;
        if (its.Any(i => string.Equals(Unbound(i.Name), "Kina", StringComparison.OrdinalIgnoreCase)) && best.Distance <= 100 && sl.Count >= 1000) return "Kina";
        if (GradedPick(sl, byIcon, prefer) is not { } pick) return null;
        var names = pick.Graded.Select(i => Unbound(i.Name)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        return names.Count == 1 ? names[0] : null;
    }

    static Dictionary<string, List<ItemInfo>>? _byIconAll;
    static ItemDictionary? _byIconOf;
    /// <summary>The item list's items by icon key (kept while the list is the same).</summary>
    internal static Dictionary<string, List<ItemInfo>> ByIcon(ItemDictionary items)
    {
        if (_byIconAll == null || !ReferenceEquals(_byIconOf, items))
        { _byIconAll = items.AllWithIcons().Where(i => i.Icon.Length > 0).GroupBy(i => IconIndex.KeyOf(i.Icon)).ToDictionary(x => x.Key, x => x.ToList()); _byIconOf = items; }
        return _byIconAll;
    }

    internal void ApplyGrid(List<RewardGrid.Slot> slots, DateTime now, IReadOnlyList<string>? moreNotRead = null, bool kinahHidden = false, string what = "reward table")
    {
        // a read of one slot or none, with nothing on it surely matched, is no reward table (2026-10-23 21:12:30: a Daeva Pass
        // screen read 11 times, 0 to 1 slot): it can't start, split or pay a run. A lone coin (a K or M count) still counts
        if (slots.Count <= 1 && !slots.Any(s => (s.Icons.Count > 0 && s.Icons[0].Distance <= GridSureDistance)
                                               || (s.Count >= 1000 && System.Text.RegularExpressions.Regex.IsMatch(s.CountText ?? "", "[KkMm]"))))
        {
            if (slots.Count > 0) CrashLog.Note($"{what}: one slot, nothing on it surely matched; not a reward table, the run is left as it was");
            return;
        }
        // a second table in a run that was paid already is the next run's
        Instances.TableStarting(now);
        _byIcon ??= _items.AllWithIcons().Where(i => i.Icon.Length > 0).GroupBy(i => IconIndex.KeyOf(i.Icon)).ToDictionary(x => x.Key, x => x.ToList());
        int added = 0, known = 0, unnamed = 0;
        var fromGrid = new List<GameEvent>();   // every slot the picture names, counted from the chat or not: the run's rewards
        // a close call is settled by the table's own sets, or by what this expedition gave in runs before (GradedPick)
        var history = Instances.Snapshot().Where(r => string.Equals(r.Zone, Instances.Open, StringComparison.OrdinalIgnoreCase)).SelectMany(r => r.Items.Keys.Concat(r.BoundItems.Keys)).Select(id => _items.NameOf(id)).ToList();
        var prefer = SetMates(slots, _byIcon!, history);
        var notRead = new List<string>();       // every slot it can't name, as "Rare x14 (picture)": shown as Unknown item, never dropped
        // a slot not read that shows a K or M count, or whose closest icon is the coin, leaves the run's Kinah not known
        bool kinahUnread = false, bigUnread = false;   // the second: a K or M slot not read, Kinah unknown unless the table's coin was read
        void NotRead(RewardGrid.Slot s)
        {
            notRead.Add($"{s.Grade} x{(s.Count > 0 ? s.Count.ToString() : "?")}{(_gridShot.Length > 0 ? " · " + _gridShot : "")}");
            bool coinLike = s.Icons.Count > 0 && s.Icons[0].Key is { } k && _byIcon!.TryGetValue(k, out var ci) && ci.Any(i => string.Equals(Unbound(i.Name), "Kina", StringComparison.OrdinalIgnoreCase));
            if (coinLike) kinahUnread = true;
            else if (System.Text.RegularExpressions.Regex.IsMatch(s.CountText ?? "", @"\d\s*[KkMm]")) bigUnread = true;
        }
        foreach (var sl in slots)
        {
            // figures on the slot the reader couldn't tell: its count isn't known, so the slot is not read (never one assumed)
            if (sl.Count <= 0) { unnamed++; NotRead(sl); CrashLog.Note($"reward table: a {sl.Grade} slot's count couldn't be read (\"{sl.CountText}\"); not counted"); continue; }
            var best = sl.Icons.FirstOrDefault();
            bool kCount = sl.Count >= 1000 && System.Text.RegularExpressions.Regex.IsMatch(sl.CountText ?? "", "[KkMm]");
            bool coinIcon = best.Key != null && _byIcon.TryGetValue(best.Key, out var its0) && best.Distance <= 100
                            && its0.Any(i => string.Equals(Unbound(i.Name), "Kina", StringComparison.OrdinalIgnoreCase));
            // a K or M count the Kinah counter agrees with is the coin, whatever its picture matched (2026-10-23 21:11 Vakron:
            // the 600.0K coin at 115, read as 605.0K, so the run lost its Kinah). The counter's rise is what counts, and only
            // when the slot's count agrees with it within 1%: the counter alone never makes Kinah
            if (kCount && !coinIcon && GradedPick(sl, _byIcon, prefer) == null && BalanceRoseNear(sl.Count, now) is long rose)
            {
                int kseen2 = _runPickups.FindIndex(p => (now - p.At).TotalSeconds <= 20 && p.Name == "Kina" && p.Count == rose);
                if (kseen2 >= 0) { _runPickups.RemoveAt(kseen2); known++; fromGrid.Add(new KinahChanged(now, rose, false) { Via = "reward table" }); continue; }
                CrashLog.Note($"reward table: a slot x{sl.CountText} (closest {best.Distance:0}) is Kinah: the Kinah counter rose {rose:N0} with it");
                Instances.Loot(new KinahChanged(now, rose, false) { Via = "reward table" }); _gridKina.Add((now, rose));
                Feed(new KinahChanged(now, rose, false) { Via = "reward table" }, RunWhere());
                fromGrid.Add(new KinahChanged(now, rose, false) { Via = "reward table" });
                added++;
                continue;
            }
            if (best.Key == null || !_byIcon.TryGetValue(best.Key, out var its)) { unnamed++; NotRead(sl); CrashLog.Note($"reward table: a {sl.Grade} slot x{sl.CountText} with no item to its icon; not counted"); continue; }
            // the gold coin: Kinah when the Kinah counter rose by exactly the slot's amount around then (2026-10-22, Urugugu:
            // 17,889 to 617,889 for the 600.0K slot). The coin's picture on the item list differs a little from the game's
            // (closest 84), so the counter, not the picture alone, makes it sure; without it the slot isn't counted.
            if (its.Any(i => string.Equals(Unbound(i.Name), "Kina", StringComparison.OrdinalIgnoreCase)) && best.Distance <= 100 && sl.Count >= 1000)
            {
                long amt = Math.Min(sl.Count, 1_000_000_000);
                int kseen = _runPickups.FindIndex(p => (now - p.At).TotalSeconds <= 20 && p.Name == "Kina" && p.Count == amt);
                if (kseen >= 0) { _runPickups.RemoveAt(kseen); known++; fromGrid.Add(new KinahChanged(now, amt, false) { Via = "reward table" }); continue; }
                // a clear match to the coin as the game draws it is sure on its own; a closer call needs the counter to agree
                bool clearCoin = best.Distance <= GridSureDistance && (sl.Icons.Count < 2 || sl.Icons[1].Distance >= best.Distance * 1.25);
                // the counter's rise within 1% of the slot is the amount (a 600.0K coin read as 605.0K, 2026-10-23)
                long? rise = BalanceRoseNear(amt, now);
                if (!clearCoin && rise == null && !BalanceRose(amt, now)) { unnamed++; NotRead(sl); CrashLog.Note($"reward table: a coin slot x{sl.CountText} (closest {best.Distance:0}) the Kinah counter didn't confirm; not counted"); continue; }
                if (rise is long r1 && r1 != amt) { CrashLog.Note($"reward table: the coin slot read {amt:N0}, the Kinah counter rose {r1:N0}: the counter's amount counts"); amt = r1; }
                Instances.Loot(new KinahChanged(now, amt, false) { Via = "reward table" }); _gridKina.Add((now, amt));
                Feed(new KinahChanged(now, amt, false) { Via = "reward table" }, RunWhere());
                fromGrid.Add(new KinahChanged(now, amt, false) { Via = "reward table" });
                added++;
                continue;
            }
            // a sure match only: close (measured on the Urugugu table: right ones 23 to 60, wrong ones 82 to 114), clearly
            // closer than the next icon of the slot's own grade that names something else (GradedPick); anything else is the
            // event's own item the list lacks, or a twin too close to call
            var pick = GradedPick(sl, _byIcon, prefer);
            if (pick != null && GradedPick(sl, _byIcon) == null) CrashLog.Note($"reward table: a {sl.Grade} slot too close to call by its picture, named {_items.NameOf(pick.Value.Graded[0].Id)} by the table's set or this expedition's runs");
            var graded = pick?.Graded ?? new List<ItemInfo>();
            bool sure = pick != null;
            if (!sure) { unnamed++; NotRead(sl); CrashLog.Note($"reward table: a {sl.Grade} slot x{sl.CountText} matches no item surely (closest {best.Key}, {best.Distance:0}); not counted"); continue; }
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
                Feed(new KinahChanged(now, amt, bnd) { Via = "reward table" }, RunWhere());
                added++;
                continue;
            }
            int count = (int)Math.Clamp(sl.Count, 1, 99999);
            int seen = _runPickups.FindIndex(p => (now - p.At).TotalSeconds <= 20 && p.Count == count && names.Contains(p.Name, StringComparer.OrdinalIgnoreCase));
            bool boundSlot = graded.All(i => i.Name.Contains("(Bound)", StringComparison.OrdinalIgnoreCase));
            var gridItem = graded.OrderBy(i => i.Name.Contains("(Bound)", StringComparison.OrdinalIgnoreCase)).First();
            // every slot ends counted or not read: a chat line can't decide between the names a picture leaves open
            if (seen >= 0 && names.Count == 1) { _runPickups.RemoveAt(seen); known++; fromGrid.Add(new ItemAcquired(now, gridItem.Id, count, boundSlot) { Via = "reward table" }); continue; }
            if (names.Count != 1) { unnamed++; NotRead(sl); CrashLog.Note($"reward table: a {sl.Grade} slot x{sl.CountText} is one of {string.Join(" / ", names.Take(3))}{(names.Count > 3 ? " …" : "")}; not counted"); continue; }
            var item = graded.OrderBy(i => i.Name.Contains("(Bound)", StringComparison.OrdinalIgnoreCase)).First();
            Instances.Loot(new ItemAcquired(now, item.Id, count, false) { Via = "reward table" });
            Feed(new ItemAcquired(now, item.Id, count, false) { Via = "reward table" }, RunWhere());
            fromGrid.Add(new ItemAcquired(now, item.Id, count, boundSlot) { Via = "reward table" });
            added++;
        }
        // the run is counted from the picture alone (Steven: expeditions, dungeons and events come from the reward pictures,
        // never the chat): what the chat put in the run gives way to the table's slots, and a slot it can't name is kept as
        // a slot not read, never filled in from the chat
        // places the picture never showed (an event card's list past its window): each one more slot not read, and the run's
        // Kinah not known, since a coin may be among them
        if (moreNotRead != null) { notRead.AddRange(moreNotRead); unnamed += moreNotRead.Count; }
        if (slots.Count > 0) Instances.RewardTable(now, fromGrid, unnamed, notRead, kinahHidden || kinahUnread || (bigUnread && !fromGrid.OfType<KinahChanged>().Any()));
        CrashLog.Note($"{what} read: {slots.Count} slots, {known} already counted from the chat, {added} added to the run, {unnamed} not named");
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
    /// <summary>After a bound Kina payout, what lands this long after is the payout's: short, so a kill just after it still counts.</summary>
    public const double PayoutWindowSeconds = 3;
    /// <summary>A loot list row out in the world with no kill or gather this long before it is a box or a reward.</summary>
    public const double NoKillSeconds = 15;
    DateTime _achieveUntil = DateTime.MinValue, _boxUntil = DateTime.MinValue, _lastKillAt = DateTime.MinValue, _lastGatherAt = DateTime.MinValue;
    static readonly Regex BoxName = new(@"\b(?:chest|box|pouch|bundle|cube|coffer|crate|package|parcel)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    static HashSet<string>? _gatherNames;
    /// <summary>
    /// Inside an expedition or event: the minimap names one, or a run is open while the minimap names no listed zone, or
    /// the run was just left and its rewards may still land (InstanceLog.Route decides those). The open world's checks
    /// (box contents, oversized stacks, a kill to witness a drop) don't apply there: Urugugu Canyon's payout, Enhance
    /// Stone x470 37 s after leaving, is the run's, not a claim (2026-10-22).
    /// </summary>
    bool InRun => (ZoneLocator.KindOf(Zone) is { } rk && rk != ZoneLocator.Ignored)
        || (Instances.Current is { } cur && cur.Kind != InstanceLog.WorldBoss && (cur.Leaving || Zone == null || Zone == SessionEngine.UnknownZone));

    /// <summary>Why a pickup at <paramref name="t"/> is a reward, or null: an achievement's (anywhere), a box's or a reward's (out in the world).</summary>
    string? RewardWhy(DateTime t, bool isBox = false, long itemId = 0) =>
        t <= _achieveUntil ? "an achievement's reward"
        // an event's or expedition's reward screen was up: what lands for a minute after is its rewards, from the picture,
        // never farming or gathering, even once its run is closed or removed (Steven, 2026-10-24 10:07: "2 odyle just randomly
        // entered my gathering" from Hidden Lugi's rewards, read again 40 s later). A gather under way is still a gather
        : !InRun && t <= _rewardScreenUntil && !(itemId != 0 && GatheringNow(itemId, t)) ? "an event's or expedition's reward (its reward screen was up)"
        : t <= _inventoryUntil ? "the inventory at work (storage, Quick Register, the wardrobe)"
        : !InRun && !isBox && t <= _boxUntil ? "a box's or a reward's contents" : null;
    /// <summary>Until when what lands is an event's or expedition's reward: a minute after its reward screen was last seen.</summary>
    DateTime _rewardScreenUntil = DateTime.MinValue;
    public const double RewardScreenSeconds = 60;
    /// <summary>For replay and tests: an event's or expedition's reward screen seen now.</summary>
    internal void RewardScreenSeen(DateTime t) { var u = t.AddSeconds(RewardScreenSeconds); if (u > _rewardScreenUntil) _rewardScreenUntil = u; }

    // what the gathering panel named lately (its node's item line), for telling a gather from a reward that lands meanwhile
    DateTime _gatherPanelAt = DateTime.MinValue;
    readonly HashSet<string> _gatherPanelItems = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>The gathering panel named this item (or the item it upgrades: "Fine Odyle" from "Odyle") in the last 15 s.</summary>
    bool GatheringNow(long itemId, DateTime t)
    {
        if ((t - _gatherPanelAt).TotalSeconds > 15 || (_gatherPanelAt - t).TotalSeconds > 5) return false;
        var name = _items.NameOf(itemId);
        return _gatherPanelItems.Any(p => name.Equals(p, StringComparison.OrdinalIgnoreCase) || name.EndsWith(" " + p, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Until when what lands is the player's own inventory at work (PickupCheck.IsInventoryLine, IsSkinUnlock).</summary>
    DateTime _inventoryUntil = DateTime.MinValue;

    /// <summary>A reward line, or an item that is a box ("Growth Pet Chest"), out in the world opens the window for what comes out of it.</summary>
    void NoteReward(GameEvent ev, DateTime now)
    {
        if (InRun || _boss != null) return;
        // a Kina payout marked (Bound) (a quest, a pass or a claim): an item printed with it, in the same read or the next
        // few seconds, is the payout's too (Steven, 2026-10-23 12:39: 11,000 Kina (Bound) and a refining stone, counted as a drop)
        if (ev is KinahChanged { Bound: true, Delta: > 0 })
        {
            var until = now.AddSeconds(PayoutWindowSeconds);
            if (until > _boxUntil) _boxUntil = until;
            return;
        }
        string? name = ev switch { RewardReceived r => r.Name, ItemAcquired a => _items.NameOf(a.ItemId), _ => null };
        if (name == null || name.Contains("Loot Chest", StringComparison.OrdinalIgnoreCase)) return;
        if (ev is RewardReceived || BoxName.IsMatch(name)) _boxUntil = now.AddSeconds(RewardWindowSeconds);
    }

    /// <summary>
    /// The crafting panel's title, read the way the recognizer reads it: "Crafting in Progress", but also "Grafting
    /// Progress", "Gråfting in Progresso" and "•raftingQsin Progresso" (Steven, 2026-10-23 17:12: both Superior Courage
    /// Scrolls were left to the chat because the title came back garbled, so a batch counted only after it ended).
    /// </summary>
    /// <summary>
    /// A Transcendence's own system lines, never an item or a notice that happens to hold the word: the room's stage line
    /// ("Set to Stage 1.") and the room title ("Shattered Arkanis [Stage 1] | Transcendence").
    /// </summary>
    public static bool IsTranscendenceLine(string line) =>
        !PickupCheck.PlayerChat(line)
        && (Regex.IsMatch(line.Trim(), @"^\W*set\s*to\s*stage\s*\d{1,2}\W*$", RegexOptions.IgnoreCase)
            || Regex.IsMatch(line, @"\[\s*stage\s*\d{1,2}\s*\]\s*\W{0,3}\s*transcend", RegexOptions.IgnoreCase));

    bool _eventScreenUp; int _eventShots; DateTime _eventShotAt = DateTime.MinValue, _eventScreenAt = DateTime.MinValue;

    // ---- the event reward cards (Ocr/EventCards.cs) ----
    // Read once a second while "sent to your cube" shows, each read placed in the cards' lists and voted. Filed into the run
    // through ApplyGrid, like an expedition's table, as soon as the last two reads agree and every received card's whole
    // list has been seen; otherwise when the screen closes, the event is left, after MaxCardReads reads or CardsSeconds.
    // Filed once per screen (a screen back within CardsRearmSeconds is the same one).
    EventCards.Merger? _cards; DateTime _cardsFirst, _cardsProbeAt = DateTime.MinValue; bool _cardsFiled, _cardsByFrames; int _cardsUnknownLayout; string _cardsWhy = "";
    public const int MaxCardReads = 12;
    public const double CardsSeconds = 20, CardsRearmSeconds = 10, CardsGoneSeconds = 2.5;
    /// <summary>What the last filed event screen held, card by card, for the replay bank: received, its list as voted, whether its list ran past what was seen.</summary>
    internal List<(int Card, bool Received, List<RewardGrid.Slot> Slots, bool More)>? LastEventCards { get; private set; }

    /// <summary>One read of the event reward screen's cards (live from RewardGridDue, or a kept picture in the replay bank).</summary>
    internal async Task EventCardsRead(System.Drawing.Bitmap frame, DateTime now)
    {
        try
        {
            if (_cardsFiled) return;   // this screen's cards are in
            var m = _cards ??= new EventCards.Merger(GridSureDistance);
            var (screen, why) = await EventCards.ReadAsync(frame, m.Cache);
            if (screen == null)
            {
                if (_cardsUnknownLayout++ == 0) CrashLog.Note($"event cards: a read not recognised, left out ({why})");
                _cardsWhy = why;
                return;
            }
            if (!screen.FaceUp) return;   // still face down, or a card mid-flip
            if (m.Reads == 0) _cardsFirst = now;
            if (!m.Add(screen, frame)) { CrashLog.Note("event cards: a read with other cards received than the ones before; left out"); return; }
            CrashLog.Note($"event cards read {m.Reads}: " + string.Join(" | ", screen.Cards.Select(c => !c.Received ? $"card {c.Index + 1} not received"
                : $"card {c.Index + 1} {c.Slots.Count(x => !x.Clipped)} slots{(c.Slots.Any(x => x.Clipped) ? $" + {c.Slots.Count(x => x.Clipped)} cut" : "")}{(c.Scrolled ? " scrolled" : "")}{(c.End ? ", end shows" : "")}{(c.Covered ? ", covered" : "")}"))
                + (m.Unaligned > 0 ? $" ({m.Unaligned} card read(s) couldn't be placed in their lists)" : ""));
            if ((m.Settled() && m.Complete) || m.Reads >= MaxCardReads || (now - _cardsFirst).TotalSeconds >= CardsSeconds)
                FileEventCards(now, m.Settled() && m.Complete ? "settled, every list seen to its end" : m.Reads >= MaxCardReads ? $"{MaxCardReads} reads" : $"{CardsSeconds:0} s");
        }
        catch (Exception ex) { CrashLog.Write("event cards", ex); }
    }

    /// <summary>The screen closed or the event was left: what was read of its cards is filed; with no read, a layout the reader didn't know is said once.</summary>
    internal void FlushEventCards(DateTime now, string why, bool force = false)
    {
        // the title missed for one read isn't the screen closing: it must have been gone a few seconds
        bool gone = force || why == "the event was left" || (now - _eventScreenAt).TotalSeconds >= CardsGoneSeconds;
        if (_cards != null && !_cardsFiled && gone)
        {
            if (_cards.Reads > 0) FileEventCards(now, why);
            else
            {
                if (_cardsUnknownLayout > 0) CrashLog.Note($"event cards: layout not recognised, pictures kept ({_cardsUnknownLayout} read(s), nothing read; the run's rewards stay not read: {_cardsWhy})");
                _cards.Dispose(); _cards = null; _cardsUnknownLayout = 0;
            }
        }
        if (_cardsFiled && ((now - _eventScreenAt).TotalSeconds > CardsRearmSeconds || why == "the event was left")) { _cardsFiled = false; _cardsUnknownLayout = 0; _cardsByFrames = false; }
    }

    /// <summary>
    /// Files the cards read as the run's rewards: every received card's list as voted, in order, through ApplyGrid (each
    /// slot named surely, or kept as an unknown item with its crop; never guessed, never dropped). A card not received is
    /// logged, never counted. A card whose list may run past what was seen adds one place not read, and the run's Kinah is
    /// then not known.
    /// </summary>
    void FileEventCards(DateTime now, string why)
    {
        var m = _cards; if (m == null) return;
        _cardsFiled = true;
        try
        {
            var all = new List<RewardGrid.Slot>(); var more = new List<string>(); var report = new List<(int, bool, List<RewardGrid.Slot>, bool)>();
            var byIcon = ByIcon(_items);
            for (int i = 0; i < EventCards.CardCount; i++)
            {
                if (m.Received[i] != true) { CrashLog.Note($"event cards: card {i + 1} not received (gold frame, no card icon): not counted"); report.Add((i, false, new List<RewardGrid.Slot>(), false)); continue; }
                // gear never stacks: a piece's slot shows no count, so figures read on it are its art (2026-10-24: greaves "x11",
                // pauldrons "x5"). A slot whose sure icon holds only gear is one piece
                var list = m.Voted(i).Select(x => x.Icons.Count > 0 && x.Icons[0].Distance <= GridSureDistance && byIcon.TryGetValue(x.Icons[0].Key, out var its) && its.All(it => _items.IsGear(it.Id)) && x.Count != 1
                    ? x with { Count = 1, CountText = "" } : x).ToList();
                bool open = !m.Cards.TryGetValue(i, out var cl) || cl.Length == null;
                int cut = list.Count(x => x.Count <= 0 && x.Icons.Count == 0);
                CrashLog.Note($"event cards: card {i + 1} received, {list.Count} slot(s){(cut > 0 ? $", {cut} only ever seen cut by the card's window (count not shown: not read)" : "")}{(open ? ", and its list may run on past what was seen" : "")}: "
                    + string.Join(" | ", list.Select(x => $"{x.Grade} x{(x.Count > 0 ? x.CountText.Length > 0 ? x.CountText : "1" : "?")} {(x.Icons.Count > 0 ? $"{x.Icons[0].Key} {x.Icons[0].Distance:0} → {SlotName(_items, x) ?? "not sure"}" : "cut")}")));
                all.AddRange(list);
                if (open) more.Add($"card {i + 1}: partly read, {list.Count} slot(s) seen, more below what the screen showed{(_gridShot.Length > 0 ? " · " + _gridShot : "")}");
                report.Add((i, true, list, open));
                // the crop of each slot that can't be named, kept beside the logs
                if (cl != null)
                    for (int at = 0; at < list.Count; at++)
                        if (SlotName(_items, list[at]) == null && cl.Crops.TryGetValue(at, out var crop))
                            RareGate.SaveProof(crop, $"card{i + 1}-slot{at + 1}-{list[at].Grade}", "event", now);
            }
            LastEventCards = report;
            CrashLog.Note($"event cards: filed after {m.Reads} read(s) ({why}){(m.Unaligned > 0 ? $"; {m.Unaligned} card read(s) left out, not placeable in their lists" : "")}");
            ApplyGrid(all, now, more, kinahHidden: more.Count > 0, what: "event cards");
        }
        catch (Exception ex) { CrashLog.Write("event cards filing", ex); }
        finally { m.Dispose(); _cards = null; }
    }

    /// <summary>An event's reward screen by its words: "Rewards have been sent to your cube.", "Can select 0 time(s)".</summary>
    /// <summary>
    /// The expedition table's "Reward collected." line as the recognizer reads it: "Reward cooectedo" too (the CI reader on
    /// the 2026-10-23 Urugugu table). The word alone was missed, and with it the whole table.
    /// </summary>
    public static bool IsRewardCollected(string line) =>
        Update.RemoteConfig.HasAny(line, Update.RemoteConfig.Current.RewardCollectedWords)
        || Regex.IsMatch(line, @"rew[a-z]{0,2}r?d\W{0,2}c[a-z]{1,6}t", RegexOptions.IgnoreCase);

    public static bool IsEventRewardTitle(string line) =>
        Regex.IsMatch(line, @"sent\s*to\s*your\s*cube|can\s*select\s*\d+\s*time", RegexOptions.IgnoreCase);

    /// <summary>
    /// The event reward screen with its cards turned face up: "Rewards have been sent to your cube." ("Can select 0 time(s)"
    /// shows during the pick too, over cards still face down: "Obtain rewards by clicking the Selection Complete button.").
    /// </summary>
    public static bool IsEventCardsUp(string line) => Regex.IsMatch(line, @"sent\s*to\s*your\s*c[uo]be", RegexOptions.IgnoreCase);

    public static bool IsCraftTitle(string line) =>
        Update.RemoteConfig.HasAny(line, Update.RemoteConfig.Current.CraftPanelWords)
        || Regex.IsMatch(line, @"r[aåäà4]ft(?:ing|in|ng)|r[aåäà4]fting", RegexOptions.IgnoreCase);

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
    /// With <paramref name="near"/> (a loot list row's first sight), the race nearest it: back-to-back gathers each keep
    /// their own (Steven's Kukuru, 2026-10-24: a row judged at 10:01:07 would otherwise take the race of the gather after it).
    bool TakeGatherToken(DateTime now, string by, DateTime? near = null)
    {
        lock (_gatherTokens)
        {
            int best = -1;
            for (int i = _gatherTokens.Count - 1; i >= 0; i--)
            {
                var t = _gatherTokens[i];
                if (t.Used || (now - t.At).TotalSeconds > GatherTokenSeconds || (now - t.At).TotalSeconds < -2) continue;
                if (near is not DateTime n) { best = i; break; }
                if (best < 0 || Math.Abs((t.At - n).TotalSeconds) < Math.Abs((_gatherTokens[best].At - n).TotalSeconds)) best = i;
            }
            if (best < 0) return false;
            _gatherTokens[best] = _gatherTokens[best] with { Used = true };
            return true;
        }
    }

    /// <summary>Whether an unclaimed successful gather of the last few seconds is there to claim (TakeGatherToken's rule), without claiming it.</summary>
    bool HasGatherToken(DateTime now)
    {
        lock (_gatherTokens) return _gatherTokens.Any(t => !t.Used && (now - t.At).TotalSeconds <= GatherTokenSeconds && (now - t.At).TotalSeconds >= -2);
    }

    /// <summary>Whether a gathering bar race finished on Success more than a second after <paramref name="since"/> (the next gather's).</summary>
    bool GatherRaceAfter(DateTime since)
    {
        lock (_gatherTokens) return _gatherTokens.Any(t => (t.At - since).TotalSeconds > 1);
    }

    bool IsGatherMaterial(long id)
    {
        _gatherNames ??= GatheringCatalog.All.Select(g => g.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return _gatherNames.Contains(_items.NameOf(id));
    }

    bool IsGatherName(string name)
    {
        _gatherNames ??= GatheringCatalog.All.Select(g => g.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return _gatherNames.Contains(name.Trim());
    }

    // ---- the gathering panel's item line: which item is being gathered (2026-10-24) ----
    // The bar race says a gather finished on Success; it doesn't say what. The panel's own lines name the node ("Aria |
    // Profes | Success", "Orich | Proficiency | Orichalcum Ore | Success"), so a gathering material the panel didn't name
    // isn't this gather's. Its "Success" and "Fail" are the bars' fixed labels, on every panel: never an outcome.
    (string Item, DateTime At) _panelItem = ("", DateTime.MinValue);
    public const double PanelItemSeconds = 15;

    /// <summary>A gathering material's base name, its tier aside ("Fresh Aria" → "aria", "Fine Orichalcum Ore" → "orichalcum ore").</summary>
    internal static string GatherBase(string name) =>
        Regex.Replace(name.Trim(), @"^(?:fine|pure|radiant|fresh)\s+", "", RegexOptions.IgnoreCase).ToLowerInvariant();

    /// <summary>The gathering material the panel's lines name, by its base name ("" when none is read).</summary>
    internal static string PanelItemOf(IEnumerable<string> lines)
    {
        foreach (var l in lines)
        {
            var t = Regex.Replace(l.ToLowerInvariant(), @"x\d+\s*$", "");
            t = Regex.Replace(t, "[^a-z]", "");
            if (t.Length < 4) continue;
            // nodes only: a gemstone rides along with ore and is never what the panel is gathering
            foreach (var g in GatheringCatalog.All.Where(x => x.IsNode).Select(x => GatherBase(x.Name)).Distinct())
            {
                var whole = g.Replace(" ", ""); var first = g.Split(' ')[0];
                // the line is the name, holds it, or is its start cut short ("Orich" for Orichalcum)
                if (t == whole || t == first || (first.Length >= 4 && t.Contains(first)) || (t.Length >= 5 && first.StartsWith(t))) return g;
            }
        }
        return "";
    }

    /// <summary>False when the panel named another gathering material in the last few seconds: then this row isn't that gather's.</summary>
    bool PanelAgrees(string itemName, DateTime now)
    {
        if (_panelItem.Item.Length == 0 || (now - _panelItem.At).TotalSeconds > PanelItemSeconds) return true;
        var b = GatherBase(itemName);
        return b == _panelItem.Item || b.StartsWith(_panelItem.Item + " ") || _panelItem.Item.StartsWith(b + " ");
    }

    /// <summary>Whether a gathering bar race stands behind a loot list row of this item now (the cross-check's question).</summary>
    bool GatherWitnessFor(string itemName, DateTime now) =>
        IsGatherName(itemName) && PanelAgrees(itemName, now) && HasGatherToken(now);

    /// <summary>
    /// Counts one event where it belongs and says where that was, as the activity's label ("Event · Hidden Lugi (Shugo
    /// Festival)", "Farming · Eastern Cantas Valley", "World boss · Bloodfang"): the stamp each pickup carries.
    /// <para>Inside a run (the minimap names an instance, or names no zone on the list while a run is open) everything is
    /// the run's. Just out of one, InstanceLog.Route decides: what looks like its rewards is the run's, loose Kina and kills
    /// are the open world's and close it. Everywhere else it is farming.</para>
    /// </summary>
    string Count(GameEvent ev)
    {
        // a chat line for a gathering material claims the gathering bar's success, so the loot list can't count it again
        if (ev is ItemAcquired cg0 && !ev.Via.Contains("loot list") && IsGatherMaterial(cg0.ItemId)) TakeGatherToken(ev.Time, "the chat");
        // what a kill or a gather looks like, for telling a box's contents in the loot list from a drop
        if (ev is MobKilled or EnemySoul or XpGained || ev is CurrencyGained { Enemy.Length: > 0 } || (ev is CurrencyGained cg && cg.Name.EndsWith("Soul Points", StringComparison.OrdinalIgnoreCase)) || ev is KinahChanged { Bound: false, Delta: > 0 })
        { _lastKillAt = ev.Time; Witness(ev.Time); }
        if (ev is PetCrystal) Witness(ev.Time);
        if (ev is GatherFailed) { _lastGatherAt = ev.Time; Witness(ev.Time); }
        else if (ev is ItemAcquired ga)
        {
            _gatherNames ??= GatheringCatalog.All.Select(g => g.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
            if (_gatherNames.Contains(_items.NameOf(ga.ItemId))) { _lastGatherAt = ev.Time; Witness(ev.Time); }
        }
        if (ev is not CraftResult && BossTakes(ev)) return _boss is { } b ? $"World boss · {b}" : "World boss";
        var kind = ZoneLocator.KindOf(Zone);
        bool inside = kind != null && kind != ZoneLocator.Ignored;
        // out of an event already (its rewards came, then the open world's Kina or a kill), the minimap a read behind
        if (inside && kind == "Event" && Instances.OutAfterRewards(ev))
        {
            inside = false; kind = null;
            Engine.SetZone(SessionEngine.UnknownZone); Secondary?.SetZone(SessionEngine.UnknownZone); ZoneView.SetZone(SessionEngine.UnknownZone);
            _zoneAt = DateTime.MinValue;
        }
        if (!inside && kind == null && Instances.Current is { } run && run.Kind != InstanceLog.WorldBoss && ev is not CraftResult)
        {
            // an inner area the list lacks (Unknown zone), or no zone read yet, while a run is open: the run's
            if (!run.Leaving && (Zone == null || Zone == SessionEngine.UnknownZone)) inside = true;
            // just out of it: its rewards may still be landing
            else if (run.Leaving) inside = Instances.Route(ev, _batch, Target.RewardOpen(ev.Time)) == RunRoute.Run;
        }
        if (inside)
        {
            // the reward table's coin slot, read first, then the same Kina in the chat: once
            if (ev is KinahChanged { Delta: > 0 } dup && _gridKina.FindIndex(g => (ev.Time - g.At).TotalSeconds is >= 0 and <= 90 && g.Amount == dup.Delta) is int gi and >= 0)
            {
                _gridKina.RemoveAt(gi);
                CrashLog.Note($"{dup.Delta:N0} Kina in the chat was the reward table's, already counted");
                return RunWhere();
            }
            var where = RunWhere();
            Instances.Loot(ev, _batch);
            Landed?.Invoke(ev, where);
            // kept a while, so the reward table read after it doesn't count the same items again
            if (ev is ItemAcquired ra) _runPickups.Add((ev.Time, Unbound(_items.NameOf(ra.ItemId)), ra.Count));
            else if (ev is RewardReceived rr) _runPickups.Add((ev.Time, Unbound(rr.Name), rr.Count));
            else if (ev is KinahChanged { Delta: > 0 } rk) _runPickups.Add((ev.Time, "Kina", (int)Math.Min(rk.Delta, int.MaxValue)));
            if (_runPickups.Count > 60) _runPickups.RemoveAt(0);
            return where;
        }
        Engine.Apply(ev); Secondary?.Apply(ev); ZoneView.Apply(ev);
        // a farming sign: the activity is farming in this zone for a while
        if (ev is MobKilled or EnemySoul or GatherFailed or XpGained or CurrencyGained or ItemAcquired { Bound: false } or KinahChanged { Bound: false, Delta: > 0 } && kind != ZoneLocator.Ignored && !ZoneLocator.IsBase(Zone)) _farmAt = ev.Time;
        UpdateDoing(ev.Time);
        var label = FarmWhere(ev);
        Landed?.Invoke(ev, label);
        return label;
    }

    /// <summary>The open run as an activity label, for a pickup that went to it.</summary>
    string RunWhere()
    {
        if (Instances.Current is not { } r) return "Event or expedition";
        var kind = r.Kind switch { "Event" => ActivityKind.Event, "Arena" => ActivityKind.Arena, "Legion" => ActivityKind.Legion, InstanceLog.WorldBoss => ActivityKind.WorldBoss, _ => ActivityKind.Expedition };
        return new Activity(kind, r.Zone, r.Event, r.Family, Leaving: r.Leaving).Label;
    }

    /// <summary>Where a pickup that didn't go to a run landed: farming in the zone, a reward screen, crafting, a base.</summary>
    string FarmWhere(GameEvent ev)
    {
        var a = Doing.Current;
        // only a craft from the panel is Crafting; a pickup while crafting is what it is (a drop, a town pickup), never a craft
        if (ev is CraftResult) return "Crafting";
        if (a.Kind == ActivityKind.Crafting) return ZoneLocator.IsBase(Zone) ? $"Town · {Zone}" : $"Farming · {Zone ?? "no zone"}";
        if (ev is RewardReceived or KinahChanged { Bound: true }) return a.Kind == ActivityKind.RewardScreen ? a.Label : $"Reward · {Zone ?? "no zone"}";
        if (a.Kind is ActivityKind.RewardScreen or ActivityKind.Market or ActivityKind.Town) return a.Label;
        return $"Farming · {Zone ?? "no zone"}";
    }

    /// <summary>For replay: each counted event and where it landed ("run: Shugo Festival", "farming: Eastern Cantas Valley").</summary>
    internal Action<GameEvent, string>? Landed;

    // ---- the audit: how the count holds up, once a minute in reads.log ----
    // The quest tracker's "Defeat … (n/20)" lines count kills of quest enemies exactly, so where every enemy fought is a
    // quest enemy, the kills counted should rise as fast as the counter. Pass times say how often the screen is read.
    int _passes; double _passMs; DateTime _auditAt = DateTime.UtcNow; int _auditKills = -1, _questSteps, _auditAdded, _auditDups;
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
                Witness(p.At);
            }
            else _questPending.Remove(of);
            if (!known || n > was || n < was - 3) _questMax[of] = n;
        }
    }
    /// <summary>Where each pass's time went, region by region, for the audit line (PassTimes).</summary>
    public PassTimes Times { get; } = new();
    double _decoderMsAt;
    int _chatSameAt, _chatReadsAt; ScreenReader? _chatCounted;
    /// <summary>The audit's note on chat reads whose pixels hadn't changed and so weren't recognized again (ocr.chat_skip_same).</summary>
    string ChatSameNote()
    {
        if (_reader is not { } r) return "";
        if (!ReferenceEquals(r, _chatCounted)) { _chatCounted = r; _chatSameAt = 0; _chatReadsAt = 0; }   // a new reader since the last line
        int same = r.SameSkips - _chatSameAt, reads = r.CaptureReads - _chatReadsAt;
        _chatSameAt = r.SameSkips; _chatReadsAt = r.CaptureReads;
        return $", {same} of {reads} reads unchanged and not recognized again";
    }

    /// <summary>One pass's time into the audit (the read loop; tests).</summary>
    internal void NotePass(double ms) { _passes++; _passMs += ms; }

    /// <summary>
    /// The once-a-minute audit line. Written every minute the game was read (2026-10-24: it was written only in minutes
    /// with a kill or a quest step, so a run of events, the arena or a market stop left no audit and no pass times at all,
    /// 12:47 to 13:08), and from passes that read the game without its chat too.
    /// </summary>
    internal void Audit(DateTime? at = null)
    {
        var now = at ?? DateTime.UtcNow;
        if (_auditKills < 0) { _auditKills = Engine.Totals().Kills; _auditAdded = _cross.Added; _auditDups = _cross.Duplicates; }
        if ((now - _auditAt).TotalSeconds < 60) return;
        var sameNote = ChatSameNote();
        // the decoder runs on its own thread: its time over the minute goes in at the same per-pass rate
        double dec = NameDecoder.TotalMs; Times.Add("decoder", dec - _decoderMsAt); _decoderMsAt = dec;
        SaveColours();
        ReportLayout();   // the text size is known by now: the layout's key gets it
        int kills = Engine.Totals().Kills;
        // the minutes when the quest counter moved: what the game counted against what the tracker counted, for the meter
        if (_questSteps > 0 && kills >= _auditKills) { QuestKillsGame += _questSteps; QuestKillsCounted += kills - _auditKills; }
        if (_passes > 0)
            CrashLog.Note($"audit: last {(now - _auditAt).TotalSeconds:0} s · kills counted +{kills - _auditKills} · quest counter +{_questSteps} · from the loot list +{_cross.Added - _auditAdded} · chat repeats skipped {_cross.Duplicates - _auditDups} · Kinah counter agreed {_balanceCheck.Agreed}×, over {_balanceCheck.Over:N0}, under {_balanceCheck.Under:N0} so far · {PassTimes.AuditPart(_passes, _passMs, Times)} · names settled by colour {_parser.ColourPicks}, by loot table {_parser.TablePicks}, guesses no table backs {_parser.HeldUnlisted}, by a candidate set {_parser.ContextPicks}, refused as on no nearby table {_parser.Refused + _cross.Refused}, loot list rows named by icon {IconNamed} · chat read at {_reader?.Factor ?? 0:0.##}x{sameNote}");
        _auditAt = now; _auditKills = kills; _questSteps = 0; _passes = 0; _passMs = 0; Times.Reset(); _auditAdded = _cross.Added; _auditDups = _cross.Duplicates;
    }

    /// <summary>One pickup as counted, for the recent pickups strip: when, what, and how it was read.</summary>
    /// <param name="Where">The activity it landed in ("Event · Hidden Lugi (Shugo Festival)", "Farming · Eastern Cantas Valley").</param>
    public sealed record Pickup(DateTime At, string What, string Via, bool Unconfirmed, string Where = "");
    readonly List<Pickup> _pickups = new();
    /// <summary>The last pickups counted, newest first: items and Kinah only, rewards and bound Kinah left out.</summary>
    public List<Pickup> RecentPickups(int n) { lock (_pickups) return _pickups.TakeLast(n).Reverse().ToList(); }
    void Feed(GameEvent ev, string where = "")
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
        Report.Counted(ev.Time, what, ev.Via, where);
        lock (_pickups) { _pickups.Add(new Pickup(ev.Time, what, ev.Via, ev.Unconfirmed, where)); if (_pickups.Count > 50) _pickups.RemoveRange(0, _pickups.Count - 50); }
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
        // through the scene gate as a live pass is: a bank picture is a crop, never a whole frame, so nothing is gated
        await LootGated((lines, (System.Drawing.Bitmap)raw.Clone(), origin), Verdict("loot_list"), t);   // disposes its picture
    }

    static Task<(List<OcrLineBox>, System.Drawing.Bitmap, System.Drawing.Point)?> NoListRead() => Task.FromResult<(List<OcrLineBox>, System.Drawing.Bitmap, System.Drawing.Point)?>(null);

    /// <summary>
    /// One loot list read under the scene gate: counted (Run), measured and held without its picture (Hold: the scene is
    /// Unknown; a rare drop released from hold counts without its second read, tagged unconfirmed), or dropped (Skip).
    /// </summary>
    async Task LootGated((List<OcrLineBox> Lines, System.Drawing.Bitmap Raw, System.Drawing.Point Origin)? read, Gate gate, DateTime? at = null)
    {
        if (read is not { } r) return;
        if (gate == Gate.Run) { await LootProcess(read, at); return; }
        var now = at ?? DateTime.UtcNow;
        using (r.Raw) ListGated(gate == Gate.Hold ? LootMeasure(r, now) : new List<LootLine>(), now, gate);
    }

    /// <summary>A loot list read as lines, under the gate: what the live pass and the replay bank's text cases both go through.</summary>
    internal void ListGated(List<LootLine> lines, DateTime now, Gate gate)
    {
        if (gate == Gate.Run) { ProcessList(lines, now); return; }
        if (gate == Gate.Hold) { HoldRead("loot_list", now, null, lines); return; }
        _rows.Reset();   // not read in this scene: the rows start over, as the crafting panel's exclusion does
    }

    /// <summary>A chat read under the gate: counted (Run), held (Hold), or dropped (Skip: the chat box keeps its lines, so they count from the next read that shows them).</summary>
    internal void ChatGated(List<string> lines, DateTime now, Gate gate)
    {
        if (gate == Gate.Run) { ProcessChat(lines, now); return; }
        if (gate == Gate.Hold && lines.Count > 0) HoldRead("chat", now, lines, null);   // an empty read has nothing to hold
    }

    async Task LootProcess((List<OcrLineBox> Lines, System.Drawing.Bitmap Raw, System.Drawing.Point Origin)? read, DateTime? at = null)
    {
        if (read is not { } r) return;
        var now = at ?? DateTime.UtcNow;
        using (r.Raw)
        {
            ProcessList(LootMeasure(r, now), now);
            // a rare drop only the list showed: this picture read again before it counts (RareGate)
            if (_rare.Waiting > 0) await ResolveRareAsync("list", r.Raw, r.Origin);
        }
    }

    /// <summary>A loot list picture's lines, each named by its icon where the text isn't an item, with its contrast and icon hash (LootRows).</summary>
    List<LootLine> LootMeasure((List<OcrLineBox> Lines, System.Drawing.Bitmap Raw, System.Drawing.Point Origin) r, DateTime now)
    {
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
            // how settled each line's text looks and its icon, measured on this picture: rows are tracked by them (LootRows)
            var measured = new List<LootLine>(lines.Count);
            for (int i = 0; i < lines.Count; i++)
            {
                (double c, ulong h, double bg, double sp) = (-1, 0, -1, -1);
                try { (c, h, bg, sp) = LootRows.MeasureAll(r.Raw, r.Origin, r.Lines[i].Box); } catch (Exception ex) { CrashLog.Write("loot list contrast", ex); }
                measured.Add(new LootLine(lines[i].Top, lines[i].Text, c, h, bg, sp));
            }
            // the name decoder's shadow read of the rows (logs only, counts nothing), on the picture while it is here
            if (NameDecoder.Present) try { ShadowListNames(r.Raw, r.Origin, r.Lines, lines, now); } catch (Exception ex) { CrashLog.Write("name decoder", ex); }
            return measured;
        }
    }

    /// <summary>Loot list reads that reached the counting path (the scene tests: none while the gate skips the list).</summary>
    internal int ListReadsCounted { get; private set; }

    /// <summary>The loot list's rows as tracked objects (LootRows): a row is its place in the stack, its icon and its name, not its y or its text.</summary>
    readonly LootRows _rows;
    public LootRows Rows => _rows;
    /// <summary>Loot list rows that went before their text settled: unsure, logged, never counted.</summary>
    public int ListUnsure => _rows.Unsure;

    /// <summary>Lines in the loot list's band that aren't rows (a nameplate, a zone's name, text over the world): dropped before LootRows.</summary>
    internal LootNotRow NotRow => _notRow ??= new LootNotRow(_items);
    LootNotRow? _notRow;
    /// <summary>Those drops as logged and counted (list_notrow): once per text per minute.</summary>
    public int ListNotRow => _notRow?.Logged ?? 0;

    /// <summary>
    /// The names on screen that the loot list's band can catch, for LootNotRow: the nameplates around the character, the
    /// target bar's name (an enemy on the list, or the last name-shaped read that isn't, 8 letters or more), the zone.
    /// </summary>
    void NoteScreenNames(DateTime now)
    {
        var nr = NotRow;
        foreach (var e in Engaged) nr.See(e, now, LootNotRow.Plate);
        if (Target.Name is { } tn && (DateTime.UtcNow - Target.NameAt).TotalSeconds <= LootNotRow.NameSeconds) nr.See(tn, now, LootNotRow.Plate);
        if (Target.Unlisted.Count(char.IsLetter) >= 8 && (DateTime.UtcNow - Target.UnlistedAt).TotalSeconds <= LootNotRow.NameSeconds) nr.See(Target.Unlisted, now, LootNotRow.Plate);
        if (Zone is { } z && z != SessionEngine.UnknownZone) nr.See(z, now, LootNotRow.Zone);
        if (ZoneLocator.LastUnlisted is { } u && u.Count(char.IsLetter) >= 8) nr.See(u, now, LootNotRow.Zone);
    }

    // ---- a chat line the loot list already shows (2026-10-24) ----
    /// <summary>
    /// A new chat line waits for three reads or a second before it counts (ChatVoter), so a misread can be outvoted. When
    /// the loot list beside the character shows a row with its icon that means exactly the same pickup (the same Kinah
    /// amount, the same item and count; an "Obtained 2" line and two x1 rows of it in one read), the two reads agree and the
    /// line counts on the read it came in. Off with the remote "chat_fast" switch, or <see cref="FastChat"/> (replays).
    /// </summary>
    internal bool FastChat = true;
    bool FastChatOn => FastChat && !Update.RemoteConfig.Current.Off("chat_fast");
    /// <summary>How long a list row's pickup stands for this: a row is up 2.5 s, and its chat line comes 0.1 to 0.2 s before it.</summary>
    public const double ListKeySeconds = 4;
    readonly List<(string Key, DateTime At, int Read)> _listKeys = new();
    int _listKeyRead;

    /// <summary>The pickup a list row means, as the chat's lines are keyed (ChatParser.Meaning): "k:463", "i:raw leather x1"; null for a soul, an unknown name or Kinah under 10.</summary>
    string? ListKey(LootPopup.Row row)
    {
        if (row.IsKinah) return row.Amount >= LootCrossCheck.MinKinah ? "k:" + row.Amount : null;
        if (LootCrossCheck.SoulOf(row.Name) != null) return null;
        var name = Regex.Replace(row.Name, @"\s*\(bound\)\s*", " ", RegexOptions.IgnoreCase).Trim();
        bool was = Accuracy.Muted;
        Accuracy.Muted = true;   // asking the item list isn't a misread
        try { return name.Length >= 2 && _items.Knows(name) ? "i:" + _items.NameOf(_items.IdOf(name)).ToLowerInvariant() + "x" + row.Count : null; }
        finally { Accuracy.Muted = was; }
    }

    /// <summary>The pickups this list read shows on rows with an icon beside them, kept <see cref="ListKeySeconds"/>.</summary>
    void NoteListKeys(IEnumerable<LootLine> lines, DateTime now)
    {
        int read = ++_listKeyRead;
        foreach (var l in lines)
        {
            if (l.Icon <= 1 || (l.Spread >= 0 && l.Spread < LootNotRow.VouchSpread)) continue;
            if (LootPopup.Rows(new[] { (l.Top, l.Text) }).FirstOrDefault() is not { } row || ListKey(row) is not { } key) continue;
            _listKeys.Add((key, now, read));
        }
        _listKeys.RemoveAll(x => Math.Abs((now - x.At).TotalSeconds) > ListKeySeconds);
    }

    /// <summary>Whether the list showed this pickup lately: the same key, or for "i:name x2" two "i:name x1" rows in one read.</summary>
    bool ListShows(string key, DateTime now)
    {
        if (key.Length == 0 || _listKeys.Count == 0) return false;
        var recent = _listKeys.Where(x => Math.Abs((now - x.At).TotalSeconds) <= ListKeySeconds).ToList();
        if (recent.Any(x => x.Key == key)) return true;
        int xi = key.LastIndexOf('x');
        if (!key.StartsWith("i:") || xi < 0 || !int.TryParse(key[(xi + 1)..], out var n) || n < 2) return false;
        var one = key[..xi] + "x1";
        return recent.Where(x => x.Key == one).GroupBy(x => x.Read).Any(g => g.Count() == n);
    }

    /// <summary>
    /// The loot list's picture of this pass, when its read is in before the chat's is counted: its rows with an icon are
    /// noted, so a chat line in this very read that the list shows needn't wait for the vote (live passes only).
    /// </summary>
    void PeekList(Task<(List<OcrLineBox> Lines, System.Drawing.Bitmap Raw, System.Drawing.Point Origin)?> list)
    {
        if (!FastChatOn || !list.IsCompletedSuccessfully || list.Result is not { } r) return;
        var lines = new List<LootLine>(r.Lines.Count);
        foreach (var l in r.Lines)
        {
            try { var m = LootRows.MeasureAll(r.Raw, r.Origin, l.Box); lines.Add(new LootLine(l.Box.Top, l.Text, m.Contrast, m.Icon, m.Background, m.Spread)); }
            catch (Exception ex) { CrashLog.Write("loot list peek", ex); }
        }
        NoteListKeys(lines, DateTime.UtcNow);
    }

    /// <summary>One read of the loot list as text only (a trace, a test): no contrast, no icons, every read counts.</summary>
    internal void ProcessList(List<(int Top, string Text)> lines, DateTime now) => ProcessList(lines.Select(l => new LootLine(l.Top, l.Text)).ToList(), now);

    /// <summary>
    /// One read of the loot list, checked against the chat, each line with its contrast and icon when the picture gave
    /// them. Also what replay feeds a recorded fight through.
    /// </summary>
    internal void ProcessList(List<LootLine> lines, DateTime now)
    {
        ListReadsCounted++;
        _ctxNow = now;
        try
        {
            CommitHeld();   // anything a step without a picture left waiting (a replay) counts as read first
            // a shop or reward screen covers the place the loot list shows: nothing there is loot
            if (Target.RewardOpen(now)) { _rows.Reset(); return; }
            // the crafting panel or window covers the place the loot list shows (its title read as a row, "Expert's Orichalcum
            // Cloak", 2026-10-23 21:17:53), and a craft's output is the panel's alone: the list isn't read while either is up,
            // nor for the panel's tail after it goes
            if (CraftView.CraftPanelActive(now) || TargetReader.CraftOpen(Target.CraftAt, now))
            {
                if (LootPopup.Rows(lines.Select(l => (l.Top, l.Text))).Count > 0 && (now - _craftListNoteAt).TotalSeconds > 30) { _craftListNoteAt = now; CrashLog.Note("loot list not read: the crafting panel or window is up"); }
                _rows.Reset();
                return;
            }
            // the gathering panel ("Orichalcum · Proficiency · Orichalcum Ore x1 · Success") sits at the bottom of the band:
            // its lines are what is being gathered, never a pickup (Steven, 2026-10-23 11:28)
            var uncut = lines;
            if (GatherPanelCut(lines.Select(l => (l.Top, l.Text)).ToList(), now) is int cut) lines = lines.Where(l => l.Top < cut).ToList();
            // lines that aren't loot list rows at all (a nameplate or the zone's name standing in the band, text over the
            // world with no icon and no dark strip behind it) go before they become rows (LootNotRow, 2026-10-24); the trace
            // keeps them, so a replay drops them the same way
            NoteScreenNames(now);
            lines = NotRow.Filter(lines, now);
            NoteListKeys(lines, now);
            // the rows as read, for the trace (with each one's place and contrast, so a replay tracks them the same way).
            // The trace keeps the gathering panel's lines too, before the cut: a replay cuts them again, and can see which
            // item the panel named (2026-10-24: the panel's lines weren't kept, so a gathering miscount couldn't be replayed)
            var read = new List<(LootPopup.Row Row, LootLine Line)>();
            foreach (var l in lines.OrderBy(l => l.Top)) if (LootPopup.Rows(new[] { (l.Top, l.Text) }).FirstOrDefault() is { } rr) read.Add((rr, l));
            var readAll = new List<(LootPopup.Row Row, LootLine Line)>();
            foreach (var l in uncut.OrderBy(l => l.Top)) if (LootPopup.Rows(new[] { (l.Top, l.Text) }).FirstOrDefault() is { } rr) readAll.Add((rr, l));
            if (read.Count > 0) _lastActivity = DateTime.UtcNow;
            if (read.Count > 0 && !_lootSeen) { _lootSeen = true; ReportLayout(); }
            if (readAll.Count > 0)
            {
                var traced = readAll.Select(x => x.Row.IsKinah ? $"{x.Row.Amount} Kina" : $"{x.Row.Name} x{x.Row.Count}").ToList();
                bool measuredAll = readAll.All(x => x.Line.Contrast >= 0);
                ReadTrace.List(now, traced, readAll.Select(x => x.Line.Top).ToList(), measuredAll ? readAll.Select(x => Math.Round(x.Line.Contrast, 3)).ToList() : null,
                    measuredAll && readAll.Any(x => x.Line.Background >= 0) ? readAll.Select(x => Math.Round(x.Line.Background, 3)).ToList() : null,
                    measuredAll && readAll.Any(x => x.Line.Spread >= 0) ? readAll.Select(x => Math.Round(x.Line.Spread, 3)).ToList() : null,
                    measuredAll && readAll.Any(x => x.Line.Icon != 0) ? readAll.Select(x => x.Line.Icon.ToString("x")).ToList() : null);
                Report.Lines("list", traced, now);
            }
            Seen(now);
            // each row tracked as an object in the list's stack: shown once its text has settled, one row one pickup, a change in place one item
            // with its final text (LootRows); a row that went before it settled is unsure and never shown
            var rows = _rows.Feed(lines, now);
            var add = _cross.Popup(rows, now);
            if (add.Count == 0) { UpdateDoing(now); return; }
            _batch = add.Count;
            bool reward = Target.RewardOpen(now);
            foreach (var e in new[] { Engine, Secondary, ZoneView, CraftView }) e?.SetCraftWindow(Target.CraftAt);
            Engine.SetTarget(Target.Name, Target.NameAt); Secondary?.SetTarget(Target.Name, Target.NameAt); ZoneView.SetTarget(Target.Name, Target.NameAt);
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
                bool made = false;   // a craft's output never comes from the list (0.8.24)
                // loot list rows count only with a chat line (Steven, 2026-10-23: "chat-confirmed only"); an enemy's soul row
                // is a kill, not a pickup
                // ...except a gather the chat missed: a gathering material, with a gathering bar that finished on Success in
                // the last few seconds that no chat line has claimed (step 1 of Steven's plan, 2026-10-23): two witnesses
                // While gathering the chat is only a cross-check (2026-10-24): its pickup lines come 6 to 11 s after the row,
                // when they come at all (world chat buried them), so the row counts on the race and a late chat line for it
                // adds nothing (LootCrossCheck.Chat). The panel's item line has to agree when it was read (PanelAgrees).
                if (!made && !_settings.Ocr.LootListAdds && parsed is ItemAcquired gl && IsGatherMaterial(gl.ItemId) && PanelAgrees(_items.NameOf(gl.ItemId), now) && TakeGatherToken(now, "the loot list", gl.Time))
                    CrashLog.Note(gl.Count >= 5
                        ? $"gathering: {_items.NameOf(gl.ItemId)} x{gl.Count} counted as shown, the gathering bar's Success race is the second witness"
                        : $"gathering: {_items.NameOf(gl.ItemId)} x{gl.Count} counted, the gathering bar's Success race is the second witness (no chat line yet)");
                // a rare drop is the other exception: it goes to its second read, and counts when that read confirms it (CommitRare)
                else if (!made && !_settings.Ocr.LootListAdds && parsed is ItemAcquired or KinahChanged && !(parsed is ItemAcquired rl && IsRare(rl.ItemId)))
                {
                    var what = parsed is ItemAcquired na ? $"{_items.NameOf(na.ItemId)} x{na.Count}" : $"{((KinahChanged)parsed).Delta:N0} Kina";
                    CrashLog.Note(parsed is ItemAcquired ng && IsGatherMaterial(ng.ItemId)
                        ? (PanelAgrees(_items.NameOf(ng.ItemId), now)
                            ? $"loot list: {what} not counted yet: no gathering bar race finished on Success for it and no chat line; a chat line for it within {LootCrossCheck.MatchSeconds:0} s counts it"
                            : $"loot list: {what} not counted yet: the gathering panel names {_panelItem.Item}, not it; a chat line for it within {LootCrossCheck.MatchSeconds:0} s counts it")
                        : $"loot list: {what} not counted yet: no chat line confirmed it; one within {LootCrossCheck.MatchSeconds:0} s counts it");
                    _cross.Declined(parsed, now);
                    continue;
                }
                // in a base, a loot-list row the chat never confirmed that isn't Odyle is left out entirely
                if (!made && InBaseNotOdyle(parsed)) { CrashLog.Note($"in a base, so not counted from the loot list: {_items.NameOf(((ItemAcquired)parsed).ItemId)}"); _cross.Declined(parsed, now); continue; }
                // an achievement's reward or a box's contents shows in the list too; out in the world, a row with no kill or
                // gather around it is one (a box opened in town). Never a drop (Steven, 2026-10-23)
                if (!made && parsed is ItemAcquired lr)
                {
                    var why = RewardWhy(now, false, lr.ItemId);
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
            _batch = 1;
            UpdateDoing(now);
        }
        catch (Exception ex) { CrashLog.Write("loot list", ex); }
    }
    /// <summary>The previous read as it came, for the log when a read can't be lined up with it.</summary>
    List<string> _prevRead = new();
    DateTime _craftListNoteAt = DateTime.MinValue;
    readonly Reread _reread = new();
    // when each line was first on screen: an achievement line that stays up is read a little differently each time, and
    // only a new one makes the items with it its reward
    readonly Dictionary<string, (DateTime First, DateTime Last)> _firstSeen = new();
    bool NewLine(string line, DateTime now) => Reread.FirstSeen(line, _firstSeen) is not DateTime f || (now - f).TotalSeconds <= 20;
    // reads that couldn't be lined up, in a row: since when, the lines seen before them, what was counted meanwhile
    DateTime? _garbledSince; HashSet<string> _beforeGarbled = new(); readonly List<string> _countedGarbled = new();
    /// <summary>Every chat line counted in the last minute, by any path, for the settle not to count it again.</summary>
    readonly List<(string Line, DateTime At)> _countedLines = new();
    void NoteCounted(string l, DateTime now) { lock (_countedLines) { _countedLines.Add((l, now)); _countedLines.RemoveAll(x => (now - x.At).TotalSeconds > 60); } }
    /// <summary>Garbled reads longer than this before the chat settles aren't one burst; nothing is taken from the settled read.</summary>
    public const double SettleSeconds = 45;
    // every line of the last ten minutes of reads, for telling a burst's new lines from history
    readonly Dictionary<string, DateTime> _recent = new();
    List<string> _lastLines = new();
    DateTime _chatLinesAt = DateTime.MinValue, _chatSightedAt = DateTime.MinValue;

    // ---- anchors, shadow only (brief item 4, 2026-10-24) ----
    /// <summary>Where the interface really is, from anchors, logged beside the fixed-fraction crops; changes nothing read.</summary>
    public Anchors Anchors { get; } = new();

    /// <summary>
    /// On its own 2 s timer (Anchors.Tick): the chat box counts as an anchor while reads of it show lines; template anchors
    /// are looked for when there are any; a minute line per region goes to reads.log.
    /// </summary>
    void AnchorsDue()
    {
        try
        {
            if (GameRect is not { } g) return;
            var now = DateTime.UtcNow;
            // a sighting only when a read showed lines since the last one: the same box handed over again on every pass is no
            // new evidence (2026-10-24 it re-took the lock a second after a drop)
            if (Region is { } chat && (now - _chatLinesAt).TotalSeconds <= 30)
            {
                if (_chatLinesAt != _chatSightedAt) { _chatSightedAt = _chatLinesAt; Anchors.ObserveChat(g, chat, DetectNote, ManualRegion, _chatLinesAt); }
            }
            else Anchors.ChatCrop.Missing(g, "no chat lines in 30 s", now);
            Anchors.Tick(g, now, r => ScreenReader.Capture(r));
        }
        catch (Exception ex) { CrashLog.Write("anchors", ex); }
    }

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

    /// <summary>A picture of the game kept in reward-shots (only where that folder exists, within the day's limit), named for the zone and what it shows.</summary>
    string _gridShot = "";
    void SaveShot(System.Drawing.Bitmap bmp, string title)
    {
        _gridShot = "";
        try
        {
            var dir = Path.Combine(CrashLog.Dir, "reward-shots");
            if (!Directory.Exists(dir)) return;
            if (Directory.GetFiles(dir, DateTime.Now.ToString("yyyyMMdd") + "-*.jpg").Length >= RewardShotsPerDay) return;
            var zoneName = new string((Zone ?? "").Where(c => char.IsLetterOrDigit(c) || c == ' ').ToArray()).Trim().Replace(' ', '-');
            var jpg = System.Drawing.Imaging.ImageCodecInfo.GetImageEncoders().First(e => e.FormatID == System.Drawing.Imaging.ImageFormat.Jpeg.Guid);
            using var ps = new System.Drawing.Imaging.EncoderParameters(1);
            ps.Param[0] = new System.Drawing.Imaging.EncoderParameter(System.Drawing.Imaging.Encoder.Quality, 90L);
            var file = Path.Combine(dir, $"{DateTime.Now:yyyyMMdd-HHmmss}-{zoneName}-{title}.jpg");
            bmp.Save(file, jpg, ps);
            _gridShot = Path.GetFileName(file);
            CrashLog.Note($"{title.Replace('-', ' ')} kept as a picture: {Path.GetFileName(file)}");
        }
        catch (Exception ex) { CrashLog.Write("picture", ex); }
    }

    /// <summary>Deletes old pictures from reward-shots (see above). Only ever .jpg files the tracker named itself.</summary>
    /// <summary>
    /// What a kept picture is, for how long it stays and what goes first when the folder is too big: a run's end (the
    /// expedition table, an event's reward screen) the runs are counted from and replayed against; a shop or trade screen
    /// (most of the folder, 2 to 3 MB each); a picture taken every few seconds (in a run, crafting, a fight, a world boss);
    /// another reward screen (the Daeva Pass, attendance).
    /// </summary>
    internal enum ShotKind { RunEnd, Shop, Frequent, Screen }
    internal static ShotKind KindOfShot(string name) =>
        Regex.IsMatch(name, @"-(?:reward-table(?:-settled)?|event-rewards)\.jpg$", RegexOptions.IgnoreCase) ? ShotKind.RunEnd
        : Regex.IsMatch(name, @"shop\.jpg$|-shop-", RegexOptions.IgnoreCase) ? ShotKind.Shop
        : Regex.IsMatch(name, @"-(?:in-run|fight|crafting|worldboss)\.jpg$", RegexOptions.IgnoreCase) ? ShotKind.Frequent
        : ShotKind.Screen;

    /// <summary>
    /// Keeps reward-shots small without losing what runs are counted from (Steven, 2026-10-23: the Urugugu and Shugo ends
    /// were deleted within minutes, 452 pictures at a time, while a week of shop screens stayed). A run's end stays 7 days
    /// and is never trimmed for size; shop screens 2 days, then the first to go; pictures taken every few seconds a day.
    /// </summary>
    internal static int PruneRewardShots(string dir, DateTime now, long? maxBytes = null)
    {
        int gone = 0;
        var files = new DirectoryInfo(dir).GetFiles("2*-*.jpg")
            .Where(f => Regex.IsMatch(f.Name, @"^\d{8}-\d{6}-"))
            .Select(f => (F: f, K: KindOfShot(f.Name)))
            .ToList();
        void Drop((FileInfo F, ShotKind K) x) { try { x.F.Delete(); gone++; } catch { } }
        double Days(ShotKind k) => k switch { ShotKind.RunEnd => 7, ShotKind.Shop => 2, ShotKind.Frequent => 1, _ => 7 };
        foreach (var x in files.Where(x => (now - x.F.LastWriteTime).TotalDays > Days(x.K)).ToList()) { Drop(x); files.Remove(x); }
        long total = files.Sum(x => x.F.Length), max = maxBytes ?? RewardShotsMaxMB * 1024 * 1024;
        int Order(ShotKind k) => k switch { ShotKind.Shop => 0, ShotKind.Frequent => 1, _ => 2 };
        foreach (var x in files.Where(x => x.K != ShotKind.RunEnd).OrderBy(x => Order(x.K)).ThenBy(x => x.F.LastWriteTime).ToList())
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
                     : inside && (now - _rewardShotAt).TotalSeconds >= (Instances.EventPaidWithin(now, 20) ? 2 : 30);
            if (!due) return;
            var today = DateTime.Now.ToString("yyyyMMdd");
            if (Directory.GetFiles(dir, today + "-*.jpg").Length >= RewardShotsPerDay) return;
            if (Cover(g) is { } cover && !cover.StartsWith("Kinah Ledger")) return;   // only the game, never a window over it
            // and only while the game is the active window: Alt+Tab's switcher, the Start menu or a notification sit over it
            // without counting as a window over it, and Alt+Tab shows thumbnails of everything open (2026-10-22 15:07)
            if (!GameWindow.InFront(_settings.ProcessHints)) return;
            _rewardShotAt = now; if (reward) _rewardShotsOpen++;
            var title = crafting ? "crafting" : !reward ? (Instances.EventPaidWithin(now, 20) ? "event-rewards" : "in-run") : new string(Target.RewardTitle.Where(c => char.IsLetterOrDigit(c) || c == ' ').ToArray()).Trim().Replace(' ', '-');
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

    /// <summary>
    /// The lines of a loot list read without the gathering panel's: a "Success" or "Fail" bar label (or "Proficiency")
    /// marks it, and its block (title, proficiency, item, bars) runs from about three lines above that label down.
    /// </summary>
    internal List<(int Top, string Text)> WithoutGatherPanel(List<(int Top, string Text)> lines, DateTime now) =>
        GatherPanelCut(lines, now) is int cut ? lines.Where(l => l.Top < cut).ToList() : lines;

    /// <summary>Where the gathering panel's block starts in a read (lines from there down are the panel's), or null when it isn't there.</summary>
    int? GatherPanelCut(List<(int Top, string Text)> lines, DateTime now)
    {
        if (lines.Count == 0) return null;
        // "Succe" and "P rofes" too: read cut short, the panel's title came through as a row ("Aria x1" over "Succe x1",
        // 2026-10-24 09:58:21 and :23, a gather counted that never happened)
        var marks = lines.Where(l => System.Text.RegularExpressions.Regex.IsMatch(l.Text.Trim(), @"^\W*[il1|]?\s*(?:succ\w*|fail(?:ed|ure)?|profic\w*|profes\w*|p\s?r\w{0,3}fes\w*)\b", System.Text.RegularExpressions.RegexOptions.IgnoreCase)).ToList();
        if (marks.Count == 0) return null;
        var tops = lines.Select(l => l.Top).OrderBy(t => t).ToList();
        var gaps = tops.Zip(tops.Skip(1), (a, b) => b - a).Where(d => d > 4).OrderBy(d => d).ToList();
        int gap = gaps.Count > 0 ? gaps[gaps.Count / 2] : 30;
        // the panel's block: from three lines above its first marker (the node's name, its proficiency) to the bottom
        int cut = marks.Min(m => m.Top) - (int)(gap * 2.5);
        // which item the panel names: what the gathering bar's race is for (its Success and Fail are labels, not outcomes)
        var named = PanelItemOf(lines.Where(l => l.Top >= cut).Select(l => l.Text));
        if (named.Length > 0)
        {
            if (named != _panelItem.Item) CrashLog.Note($"gathering panel: {named}");
            _panelItem = (named, now);
        }
        if ((now - _gatherPanelNoted).TotalSeconds > 20)
        {
            _gatherPanelNoted = now;
            CrashLog.Note($"the gathering panel shows at the bottom of the loot list's place: its lines aren't pickups ({string.Join(" | ", lines.Where(l => l.Top >= cut).Select(l => l.Text))})");
        }
        // the item the node gives, as the panel names it ("Aria", "Kukuru"): a gather under way, not a reward landing
        _gatherNames ??= GatheringCatalog.All.Select(g => g.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var (_, text) in lines.Where(l => l.Top >= cut))
        {
            var name = System.Text.RegularExpressions.Regex.Replace(text, @"\s*x\d+\s*$", "").Trim();
            if (_gatherNames.Contains(name)) { if ((now - _gatherPanelAt).TotalSeconds > 15) _gatherPanelItems.Clear(); _gatherPanelItems.Add(name); _gatherPanelAt = now; }
        }
        return cut;
    }

    int _panelLevel;
    string _panelRecipe = ""; DateTime _panelTextAt = DateTime.MinValue; bool _panelUp, _panelIsCraft;

    /// <summary>The crafting panel's locator's look this pass: its strip's picture and sample, kept for the readers after the scene pass.</summary>
    sealed record CraftProbe(System.Drawing.Bitmap Bmp, Rectangle Region, CraftPanel.Sample Sample, DateTime At) : IDisposable
    {
        public void Dispose() => Bmp.Dispose();
    }
    CraftProbe? _probe;

    /// <summary>
    /// The crafting panel's locator, every pass before the scene is worked out (it is one of the scene's signs): the
    /// strip's labels measured (pixels only), and its title read when it opens and every few seconds after, which says
    /// crafting or a gathering bar. Counts nothing: what the bars finish is counted by <see cref="CraftPanelDue"/>, behind
    /// the gate. Null when the panel isn't up.
    /// </summary>
    async Task<CraftProbe?> CraftProbeAsync()
    {
        if (GameRect is not { } g) return null;
        System.Drawing.Bitmap? bmp = null;
        try
        {
            var region = CraftPanel.Region(g);
            if (Cover(region) is { } cover && !cover.StartsWith("Kinah Ledger")) return null;
            bmp = ScreenReader.Capture(region);
            var now = DateTime.UtcNow;
            var s = CraftPanel.Measure(bmp, region, g);
            if (!s.Visible)
            {
                if (_panelUp && _panelIsCraft) { CrashLog.Note("crafting panel gone"); Accuracy.Add("craft_batches"); }
                _panelUp = false; _panelIsCraft = false;
                bmp.Dispose();
                return null;
            }
            if (!_panelUp || (now - _panelTextAt).TotalSeconds >= 5)
            {
                _panelTextAt = now;
                var text = await CraftPanel.ReadTextAsync(bmp, region, g);
                // the same Success/Fail bars run when an Odyle cube is opened or a node gathered (2026-10-22 19:26): only a
                // panel headed "Crafting in Progress" is crafting; any other is left alone, and its loot stays loot
                // once seen as crafting it stays so until the panel goes (a later read can come back garbled)
                // the raised panel above the skill bar is the gathering panel, never crafting
                bool craft = !s.Raised && ((_panelUp && _panelIsCraft) || CraftPanel.LastLines.Any(IsCraftTitle));
                if (craft != _panelIsCraft || !_panelUp) CrashLog.Note(craft ? "crafting panel up" : $"Success/Fail bars that aren't crafting (read: {string.Join(" | ", CraftPanel.LastLines)}): not counted");
                _panelIsCraft = craft;
                var recipe = craft && text is { } t ? CraftPanel.ItemOf(t.Recipe) : "";
                if (recipe.Length >= 2 && text is { } tl && CraftPanel.LevelOf(tl.Recipe) is int lv and > 0) _panelLevel = lv;
                else if (recipe.Length >= 2 && recipe != _panelRecipe) _panelLevel = 0;
                if (recipe.Length >= 2 && recipe != _panelRecipe) { _panelRecipe = recipe; CrashLog.Note($"crafting panel: {recipe} (read: {string.Join(" | ", CraftPanel.LastLines)})"); }
            }
            _panelUp = true;
            var probe = new CraftProbe(bmp, region, s, now);
            bmp = null;
            return probe;
        }
        catch (System.ComponentModel.Win32Exception) { bmp?.Dispose(); throw; }
        catch (Exception ex) { bmp?.Dispose(); CrashLog.Write("crafting panel", ex); return null; }
    }

    /// <summary>
    /// What the crafting panel's bars finish this pass, behind the scene gate: a crafting panel's race is a craft
    /// (craft_panel: the Crafting scene only), any other panel's is a gathering bar (gather_bar: the world).
    /// </summary>
    Task CraftPanelDue()
    {
        if (_probe is not { } p) return Task.CompletedTask;
        var (bmp, region, s, now) = (p.Bmp, p.Region, p.Sample, p.At);
        try
        {
            if (!_panelIsCraft)
            {
                if (!Runs("gather_bar")) return Task.CompletedTask;
                // a node being gathered: its bar's outcome, for the audit (a stopped gather's bar never fills: no outcome)
                if (_gatherBar.Feed(now, s) is { } go)
                {
                    _gatherRaces.Add((now, go.Ok)); if (_gatherRaces.Count > 50) _gatherRaces.RemoveAt(0);
                    if (go.Ok) GatherBarOk++; else GatherBarFailed++;
                    CrashLog.Note($"gathering bar: {(go.Combo ? "upgrade roll " : "")}{(go.Ok ? "success" : "fail")} (success bar {s.Success:P0}, fail bar {s.Fail:P0})");
                    GatherOutcome(now, go.Ok, go.Combo);
                }
                return Task.CompletedTask;
            }
            if (!Runs("craft_panel")) return Task.CompletedTask;
            if (!_craftSeen) { _craftSeen = true; ReportLayout(); }
            // the panel's picture for a miscount report (the panel's own strip only, at most once a second)
            if (ReportBuffer.Keeps("craft")) try { Report.Picture("craft", bmp, region.Location, now, minGapSeconds: 1); } catch (Exception ex) { CrashLog.Write("report picture", ex); }
            // the crafting window's hold and its pictures last as long as the queue runs, and the passes keep a fight's pace
            Target.MarkCraft(now); _lastActivity = now;
            PanelUp(_panelRecipe, now);
            if (_panel.Feed(now, s) is not { } o) return Task.CompletedTask;
            CrashLog.Note($"crafting panel: {(o.Combo ? "combo roll " : "")}{(o.Ok ? "success" : "fail")} for {(_panelRecipe.Length > 0 ? _panelRecipe : "an unread recipe")} (success bar {s.Success:P0}, fail bar {s.Fail:P0})");
            PanelRace(_panelRecipe, o.Ok, o.Combo, now, _panelLevel);
        }
        catch (Exception ex) { CrashLog.Write("crafting panel", ex); }
        return Task.CompletedTask;
    }

    /// <summary>The crafting panel is on screen, making this recipe (also what replay and the tests feed).</summary>
    internal void PanelUp(string recipe, DateTime now)
    {
        foreach (var e in new[] { Engine, Secondary, ZoneView, CraftView }) e?.CraftPanelSeen(recipe, now);
    }

    /// <summary>
    /// One finished race of the panel's Success and Fail bars: the only way a craft is counted (Steven, 2026-10-23: "if u
    /// dont see the crafting bars why the fuck are u adding it to a craft window"). Also what replay and the tests feed.
    /// </summary>
    internal void PanelRace(string recipe, bool ok, bool combo, DateTime now, int level = 0)
    {
        PanelUp(recipe, now);
        var ev = new CraftResult(now, recipe, ok, combo) { Via = SessionEngine.CraftPanelVia, Makes = recipe.Length >= 2 ? RecipeBook.YieldFor(recipe, level) : null };
        Count(ev);
        CraftView.Apply(ev);
        Accuracy.Add("craft_panel");
        Report.Counted(now, $"craft {(ok ? "success" : "fail")}{(combo ? " (combo)" : "")}: {(recipe.Length > 0 ? recipe : "?")}", ev.Via);
        try { Parsed?.Invoke(ev); } catch (Exception ex) { CrashLog.Write("parsed event", ex); }
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
        var t = (at ?? DateTime.UtcNow).AddSeconds(ChatVoter.MaxSeconds + 0.1);
        if (_voter.Waiting > 0 && _prevRead.Count > 0) ProcessChat(new List<string>(_prevRead), t);
        CommitHeld();
        ReleaseAwaiting(t.AddSeconds(WitnessWaitSeconds), final: true);   // a stop or the end of a replay: decided now
        foreach (var e in new[] { Engine, Secondary, ZoneView }) e?.SettleQuest(t, final: true);
    }

    // ---- two witnesses for a drop (Steven's rule: no single-signal counting; the audit of 2026-10-23) ----
    // Claims, cubes, mail, storage, quest items and wardrobe unlocks print "acquired" like a drop, and new ones turn up
    // with every patch. What they never have is a kill or a gather: an enemy's Kinah, Soul Points or Soul Crystal line, a
    // soul row, the quest counter moving, or a gather. So out in the world an item line waits up to a few seconds for one,
    // either side of it, and without one it is a reward, not a drop. Rare drops keep their own rule (always counted).
    readonly List<(GameEvent Ev, DateTime At)> _awaiting = new();
    readonly List<DateTime> _witnessAt = new();
    static double WitnessSeconds => Update.RemoteConfig.Current.WitnessSeconds;
    const double WitnessWaitSeconds = 8;

    bool NeedsWitness(ItemAcquired a, bool made, bool crafting) =>
        WitnessSeconds > 0 && !InRun && _boss == null && !made && !IsGatherMaterial(a.ItemId) && !IsRare(a.ItemId) && !RunTakes(a);

    /// <summary>
    /// The open run takes this pickup on its own rules, with no kill or gather to witness it: inside it while the minimap
    /// names no listed zone, or just out of it when it looks like the run's reward (InstanceLog.Route). Hidden Lugi's and
    /// Urugugu Canyon's rewards print after the game has moved the player out (2026-10-22/23).
    /// </summary>
    bool RunTakes(GameEvent ev) =>
        Instances.Current is { } r && r.Kind != InstanceLog.WorldBoss && ZoneLocator.KindOf(Zone) == null
        && (r.Leaving ? Instances.WouldTake(ev, _batch, Target.RewardOpen(ev.Time)) : Zone == null || Zone == SessionEngine.UnknownZone);

    void Witness(DateTime t) { lock (_witnessAt) { _witnessAt.Add(t); _witnessAt.RemoveAll(w => (t - w).TotalSeconds > 120); } }

    bool Witnessed(DateTime t) { lock (_witnessAt) return _witnessAt.Any(w => Math.Abs((w - t).TotalSeconds) <= WitnessSeconds); }

    /// <summary>Items waiting for a kill or a gather: counted once one is seen, a reward once the wait is over.</summary>
    internal void ReleaseAwaiting(DateTime now, bool final = false)
    {
        List<(GameEvent Ev, DateTime At)> due;
        lock (_awaiting)
        {
            if (_awaiting.Count == 0) return;
            due = _awaiting.Where(x => final || Witnessed(x.Ev.Time) || (now - x.At).TotalSeconds > WitnessWaitSeconds).ToList();
            foreach (var d in due) _awaiting.Remove(d);
        }
        foreach (var (ev, _) in due)
        {
            if (Witnessed(ev.Time)) { Commit(ev); continue; }
            if (ev is ItemAcquired a) CrashLog.Note($"{_items.NameOf(a.ItemId)} x{a.Count}: no kill or gather within {WitnessSeconds:0} s of it, so a claim, a box or the inventory; not a drop");
            Commit(_parser.AsReward(ev));
        }
    }

    /// <summary>The player's region key and server label ("na_e", "Siel (Elyos)"), sent with the run so the site can keep drops per server.</summary>
    public string RegionKey { get; set; } = "";
    public string Server { get; set; } = "";

    public Task<bool> UploadAsync(Settings s) => UploadAsync(s, Engine, Kind, Activity);

    /// <summary>
    /// Sends the runs that finished since the last send, on their own, when the site takes that (RemoteConfig.RunSync):
    /// each expedition, Transcendence and event reaches the profile the moment it ends, not with the next farming chunk.
    /// A send that doesn't reach the site puts them back for the next try. False when nothing went.
    /// </summary>
    public async Task<bool> SendRunsAsync(Settings s)
    {
        if (!Update.RemoteConfig.Current.RunSync || !s.Upload.Enabled || string.IsNullOrWhiteSpace(s.Upload.Endpoint) || Instances.UnsharedCount == 0) return false;
        if ((DateTime.UtcNow - _runsSentAt).TotalSeconds < 30) return false;
        _runsSentAt = DateTime.UtcNow;
        var runs = Instances.TakeUnshared(DateTime.UtcNow.AddHours(-6));
        var pay = InstanceRunPayload.From(runs, id => _items.KeyOf(id), id => ReportedDrops.Has(_items.NameOf(id)));
        if (pay == null) return false;
        bool ok = await Uploader.SendAsync(RunPayload.RunsOnly(RegionKey.Length > 0 ? RegionKey : s.Region, Server, _items, pay), s.Upload);
        if (!ok) Instances.Restore(runs);
        CrashLog.Note(ok ? $"runs sent on their own: {string.Join(", ", pay.Select(r => r.zone))}" : "runs not sent: the site didn't answer; kept for the next try");
        return ok;
    }
    DateTime _runsSentAt = DateTime.MinValue;

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
    /// <summary>The player's gear score, typed on the first-run screen or in Options; 0 is not set. Runs are compared with players at the same bracket (Data.GearScore).</summary>
    public int GearScore { get; set; }
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
