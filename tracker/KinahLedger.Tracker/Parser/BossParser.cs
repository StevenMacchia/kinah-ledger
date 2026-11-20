using System.Text.Json;
using KinahLedger.Packets;

namespace KinahLedger.Parser;

/// <summary>What the Parser keeps (Steven, 2026-11-13: "allow users to select just boss only for parser or full run etc").</summary>
public enum ParserMode
{
    /// <summary>One record per boss pull in a Transcendence, an Expedition, a Sanctuary, a Nightmare instance or an Ascension Trial, and per world boss pull (the default).</summary>
    Bosses = 0,
    /// <summary>One record per instance run of those dungeons, entering to leaving, trash included, each boss a split.</summary>
    Run = 1,
    /// <summary>One record per fight anywhere (the open world and the Abyss too): the first hit or kill to a pause.</summary>
    Fights = 2,
}

/// <summary>
/// Boss fights, instance runs and fights from the game's messages. Steven, 2026-11-13: "ONLY for bosses within the zone
/// and nothing else ... it should reset every time you start a new boss", then "allow users to select just boss only for
/// parser or full run etc". The rules:
/// <list type="bullet">
/// <item>Boss pulls are followed whatever the mode (a run's splits and a fight's name come from them). A pull exists
///   inside a Transcendence, an Expedition, a Sanctuary, a Nightmare instance or an Ascension Trial, and for a world boss the timers know
///   wherever it is fought (<see cref="World"/>; Steven, 2026-11-17: "add boss names / bars etc for nightmare dungeons and
///   world bosses etc and add them to the parser logs"). The decoder says a pull only for the bosses on its own
///   list; the tracker's list (Parser/BossBook.cs) gives the kind (a Sanctuary), the final boss and the name as players
///   know it. A name that list doesn't have is logged, not dropped: a spelling between the two lists must never lose a
///   fight. Pulling another boss (or a new spawn of the same one) ends the pull under way. A pull ends with the boss's
///   death (kill), the game saying the group fell (wipe), or leaving or going quiet for <see cref="StaleMinutes"/> (left).</item>
/// <item><see cref="ParserMode.Bosses"/> keeps each pull. <see cref="ParserMode.Run"/> keeps each run of those instances,
///   from entering to leaving (cleared when its final boss died), with each pull a split and the run's kills, deaths,
///   group and damage. <see cref="ParserMode.Fights"/> keeps every fight anywhere: from the first kill, death, hit or pull
///   to <see cref="PauseSeconds"/> with none (never while a boss pull is under way), with your kills (players apart) and deaths.</item>
/// <item>Lost traffic, a reconnect or a tracker restart never split a record: it carries on, marked partial with why.
///   Changing the mode ends the record of the old mode where it is (kept, partial) and the new one starts from there.</item>
/// </list>
/// Fed on the session's thread; the window reads copies (<see cref="Live"/>).
/// </summary>
public sealed class BossParser
{
    readonly object _gate = new();
    readonly BossBook _book;

    /// <summary>A record that has ended and is worth keeping: the caller saves it (Parser/EncounterStore.cs).</summary>
    public Action<Encounter>? Ended { get; set; }

    /// <summary>
    /// Something the window draws changed: said at once, on the thread that brought it (the packet reader's, for the game's
    /// events), with the event's capture time (null when no event brought it: a record given up, the mode changed). The
    /// window redraws on it, at most every few tens of ms (Ui/MainWindow.Parser.cs), and measures from that time.
    /// </summary>
    public event Action<DateTime?>? Changed;

    /// <summary>The last record that ended and was kept: the overlay shows its result a few seconds, before it is saved.</summary>
    public Encounter? LastEnded { get { lock (_gate) return _lastEnded; } }
    Encounter? _lastEnded;

    /// <summary>
    /// The pull that just ended (<paramref name="id"/>) as it is now: its own record, a copy, with any heal of its last moment
    /// that came after its end (<see cref="LastEnded"/> is the copy saved, kept as it went out). Null once another pull ended,
    /// or for any other record. The meter that holds the result re-reads it when <see cref="LateHeals"/> moves.
    /// </summary>
    public Encounter? EndedNow(string id) { lock (_gate) return _endedPull is { } ep && ep.Copies.Count > 0 && ep.Copies[0].Id == id ? Copy(ep.Copies[0]) : null; }

    void Finish(List<Encounter> done, DateTime? at)
    {
        if (done.Count > 0) lock (_gate) _lastEnded = done[^1];
        foreach (var e in done) Ended?.Invoke(e);
        try { Changed?.Invoke(at); } catch (Exception ex) { CrashLog.Write("parser changed", ex); }
    }

    /// <summary>A boss pull with nothing heard for this long is given up (left) at the last thing heard.</summary>
    public const double StaleMinutes = 10;
    /// <summary>A run with nothing heard for this long (the player went afk, the tracker lost the game) is given up.</summary>
    public const double RunStaleMinutes = 30;
    /// <summary>A fight ends this long after its last kill, hit or pull (Every fight).</summary>
    public const double PauseSeconds = 20;
    /// <summary>A record saved before a restart carries on when the tracker is back within this long; older, it is kept as it was.</summary>
    public const double ResumeMinutes = 15;
    /// <summary>A pull shorter than this that didn't kill the boss (a body pull, a reset) isn't kept.</summary>
    public const double MinSeconds = 5;
    /// <summary>A run shorter than this with no boss pulled (in and straight out) isn't kept.</summary>
    public const double RunMinSeconds = 30;
    /// <summary>A live record with nothing heard for this long and then the instance again: the connection dropped in between.</summary>
    const double QuietSeconds = 30;
    /// <summary>
    /// A boss pull with damage and then none, and no HP fall, for this long has wiped (or the group walked off) though no end
    /// came: it ends then, at its last damage, not after <see cref="StaleMinutes"/>. Hitting the same boss again opens a new pull.
    /// </summary>
    public const double WipeQuietSeconds = 60;
    /// <summary>A pull starts at its first damage when that comes more than this before or after the pull itself.</summary>
    public const double StartSlackSeconds = 1;
    /// <summary>Damage to a boss this long before its pull counts for the pull (the first damage can come just ahead of it).</summary>
    const double PreSeconds = 10;
    /// <summary>A member who joins this long after the pull (a group list known before) is marked late, damage or not.</summary>
    public const double LateSeconds = 10;
    /// <summary>The boss's HP back to full with no damage for this long is a reset (a heal back to full mid-fight comes under fire).</summary>
    const double ResetQuietSeconds = 5;

    /// <summary>Damage to a boss in the last <see cref="PreSeconds"/> while no pull was under way (its pull may follow).</summary>
    readonly Queue<DamageDealt> _preHits = new();
    /// <summary>The last pull ended by its quiet or its reset, with no end said: hitting that boss again opens a new pull.</summary>
    (string Boss, long BossId, long InstanceId)? _inferred;

    public BossParser(BossBook? book = null) => _book = book ?? BossBook.Shared;

    /// <summary>A pull with no name: the fight is kept under this.</summary>
    public const string UnnamedBoss = "Unnamed boss";

    // where the player is
    ContentKind _kind;
    string _instance = "";
    /// <summary>The instance's mode as the game named it (InstanceMode), "" until it comes.</summary>
    string _gameMode = "";
    /// <summary>
    /// The instance is a Transcendence (the game's map code or mode said "G", or its name a stage), and its stage when known:
    /// an expedition's own place at a stage, filed as a Transcendence, never as the expedition (Steven, 2026-11-17).
    /// </summary>
    bool _transcend; int _stage;
    /// <summary>The stage the chat said the room was set to ("Set to Stage 4."), and when: a Transcendence's stage when its code doesn't say.</summary>
    int _stageSaid; DateTime _stageSaidAt = DateTime.MinValue;
    /// <summary>A chat stage counts for a Transcendence entered this long after it (the room keeps its stage; a new party sets its own).</summary>
    public const double StageSaidHours = 3;
    /// <summary>The instance's boss room closed (its map event's end) after its last boss died: its opening again is a new run.</summary>
    DateTime? _roomClosedAt;
    long _instanceId;
    bool _instanceFromGame;
    /// <summary>When the player last entered an instance (a map event from before it is another place's).</summary>
    DateTime _enteredAt = DateTime.MinValue;
    string _zone = "";
    bool _abyss;
    List<GroupMember> _group = new();
    /// <summary>The boss pull under way (any mode), the run under way (Run), the fight under way (Fights).</summary>
    Encounter? _cur, _run, _fight;
    /// <summary>
    /// The open-world fight under way in Bosses only and Full run (in Every fight it is _fight itself): its your-DPS is the
    /// small figure on the Kinah line while farming. Never kept, never written, dropped after its pause.
    /// </summary>
    Encounter? _farm;
    /// <summary>
    /// Always on (Steven, 2026-11-15: "a toggle in the parser table directly for 'Always on' or 'Only boss fights'"): the
    /// meter's own tally, in every mode, never kept, never written. It opens at the first hit of yours or your group's and
    /// starts again from 0 when a boss is pulled (the pull is the meter then), when you go into or out of a Transcendence,
    /// an Expedition or a Sanctuary (a reconnect or a loading screen back into the same copy is not a new one), and outside
    /// those after <see cref="AlwaysIdleSeconds"/> with nobody of yours fighting. Null between those and the next hit, while
    /// the meter holds what ended last (<see cref="AlwaysEnded"/>).
    /// </summary>
    Encounter? _always;
    /// <summary>When yours last fought in the Always on tally (a hit dealt or taken, a kill, a pull): its idle clock.</summary>
    DateTime _alwaysActiveAt;
    /// <summary>What the Always on meter showed that ended last (its tally or a boss pull), settled: held until the next starts.</summary>
    Encounter? _alwaysEnded;
    /// <summary>
    /// Always on, outside the three: this long with nobody of yours fighting and the fight is over, its result held, the next
    /// hit starting the meter from 0 (Steven, 2026-11-15: "it can stop when you stop DPSing for a few seconds while in the open
    /// world and refresh"; it was 2 minutes).
    /// </summary>
    public const double AlwaysIdleSeconds = 5;

    /// <summary>How a run stood at a moment (a gap, an entry), to end it there when what came after was a new run of the instance.</summary>
    sealed record RunState(DateTime At, bool Partial, List<string> PartialWhy, int Gaps, DateTime LastAt);
    /// <summary>
    /// Back into an instance of the name you were in, its last boss alive: a reconnect into the same copy, or running it
    /// again (2026-11-14: three Vakron Sky Island runs in a row, each with another group). The group's first list decides
    /// (anyone of the group before: the same copy), else a boss already down here pulled again (a new run).
    /// </summary>
    sealed record Reentry(DateTime At, RunState? Before, DateTime? PullLast, HashSet<string> Others);
    Reentry? _reentry;
    RunState? _gapBefore;
    /// <summary>A gap this long before entering the same instance again is the reconnect's (or the new run's) own.</summary>
    const double ReentryGapSeconds = 120;
    const string ReconnectedWhy = "the game reconnected during it";

    RunState? Snapshot(DateTime at) => _run is { } r ? new RunState(at, r.Partial, r.PartialWhy.ToList(), r.Gaps, r.LastAt) : null;
    /// <summary>
    /// One hit this big is a misread, never damage (2026-11-15: a Cleric's Divine Punishment read 261M once in the open
    /// world; it hits for thousands, and a whole boss has 5 to 10M HP): left out of every figure, the record marked partial.
    /// </summary>
    public const long ImpossibleHit = 50_000_000;

    /// <summary>Your hits in the farming fight under way (the last few seconds of them), for the live readout (FarmDpsNow).</summary>
    readonly Queue<(DateTime At, long Amount)> _farmHits = new();
    Encounter? _farmHitsOf;
    DateTime _farmFirstHit, _farmLastHit;

    /// <summary>The window the live farming DPS is over: each hit counts fully when it lands and fades out linearly over this many seconds.</summary>
    public const double RollSeconds = 5;
    /// <summary>With no hit of yours for this long the readout starts easing onto the fight's figure, and is on it after <see cref="EaseSeconds"/> more.</summary>
    public const double EaseAfterSeconds = 1, EaseSeconds = 2;

    /// <summary>The farming fight under way (Every fight's own when it is the open world's), else null.</summary>
    Encounter? FarmRecord => _mode == ParserMode.Fights ? (_fight is { OpenWorld: true } ? _fight : null) : _farm;

    /// <summary>True while a farming fight of yours is under way (a cheap check for the window's 10-a-second readout).</summary>
    public bool FarmLive => Volatile.Read(ref _farmHitsOf) != null;

    /// <summary>
    /// The live farming DPS (Steven, 2026-11-14: "the dps figure needs to be rolling all the time"), or null when you aren't
    /// farming: your hits weighted by how recent they are, each counting fully as it lands and fading out over
    /// <see cref="RollSeconds"/> (since the first hit, when that is shorter), so every hit lifts it at once and it slides down
    /// smoothly between hits. When you stop hitting it eases onto the fight's figure (Encounter.FarmDps) instead of falling
    /// to nothing.
    /// </summary>
    public double? FarmDpsNow(DateTime now)
    {
        lock (_gate)
        {
            var f = FarmRecord;
            if (f == null || !ReferenceEquals(f, _farmHitsOf) || _farmHits.Count == 0) { if (f == null) _farmHitsOf = null; return null; }
            return Rolling(_farmHits, _farmFirstHit, _farmLastHit, f.FarmDps(), now);
        }
    }

    /// <summary>The readout from the hits (oldest first): the rolling rate, eased onto <paramref name="fight"/> once the hits stop.</summary>
    public static double Rolling(IEnumerable<(DateTime At, long Amount)> hits, DateTime first, DateTime last, double fight, DateTime now)
    {
        double At(DateTime when)
        {
            double sum = 0;
            foreach (var (at, amount) in hits)
            {
                double age = Math.Max(0, (when - at).TotalSeconds);
                if (age < RollSeconds) sum += amount * (1 - age / RollSeconds);
            }
            // the weights' area over the time there has been (at least 1 s, at most the window): the rate a steady hitter would read
            double span = Math.Clamp((when - first).TotalSeconds, 1, RollSeconds);
            return sum / (span - span * span / (2 * RollSeconds));
        }
        double idle = Math.Max(0, (now - last).TotalSeconds);
        if (idle <= EaseAfterSeconds) return At(now);
        // stopped: from where the rolling figure stood then, a smooth glide onto the fight's figure (no dip, no drop to 0)
        double from = At(last.AddSeconds(EaseAfterSeconds));
        double k = Math.Clamp((idle - EaseAfterSeconds) / EaseSeconds, 0, 1);
        k = k * k * (3 - 2 * k);
        return from + (fight - from) * k;
    }

    void FarmHit(Encounter farm, DateTime at, long amount)
    {
        if (!ReferenceEquals(farm, _farmHitsOf)) { _farmHits.Clear(); Volatile.Write(ref _farmHitsOf, farm); _farmFirstHit = at; _farmLastHit = at; }
        _farmHits.Enqueue((at, amount));
        if (at > _farmLastHit) _farmLastHit = at;
        while (_farmHits.Count > 0 && (_farmLastHit - _farmHits.Peek().At).TotalSeconds > RollSeconds + 1) _farmHits.Dequeue();
    }
    DateTime _lastEventAt = DateTime.MinValue;
    ParserMode _mode;

    /// <summary>Goes up with every change the window should draw.</summary>
    public int Version { get { lock (_gate) return _version; } }
    int _version;

    /// <summary>When the game last said anything about a fight (MinValue: never, as in a build without the decoder).</summary>
    public DateTime LastEventAt { get { lock (_gate) return _lastEventAt; } }

    /// <summary>Where the player is: the content kind (Other outside the three) and the instance's name.</summary>
    public (ContentKind Kind, string Instance) Where { get { lock (_gate) return (_kind, _instance); } }
    /// <summary>The instance's mode as the game named it ("Conquest", "Hard"), "" until it comes or outside one: the overlay's idle line.</summary>
    public string GameMode { get { lock (_gate) return _gameMode; } }

    /// <summary>The zone the session reads (the open world, the Abyss), for Every fight's records.</summary>
    public string Zone { get { lock (_gate) return _zone; } }

    /// <summary>The group as the game last said it.</summary>
    public IReadOnlyList<GroupMember> Group { get { lock (_gate) return _group.ToList(); } }

    public ParserMode Mode { get { lock (_gate) return _mode; } }

    /// <summary>
    /// A copy of the record under way in the mode: the boss pull, the run (with the pull under way as its last split and
    /// its boss's HP), or the fight; null when there is none.
    /// </summary>
    public Encounter? Live => LiveAt(DateTime.UtcNow);

    /// <summary><see cref="Live"/> as of <paramref name="now"/> (the pull under way's clock in a run).</summary>
    public Encounter? LiveAt(DateTime now)
    {
        {
            lock (_gate)
            {
                switch (_mode)
                {
                    case ParserMode.Run:
                        if (_run == null) return null;
                        var r = Copy(_run);
                        if (_cur != null)
                        {
                            r.Splits.Add(Split.From(_cur, now, live: true));
                            r.Boss = _cur.Boss; r.BossId = _cur.BossId; r.BossHp = _cur.BossHp; r.BossMaxHp = _cur.BossMaxHp;
                        }
                        else { r.Boss = ""; r.BossHp = 0; r.BossMaxHp = 0; }
                        return r;
                    case ParserMode.Fights: return _fight == null ? null : Copy(_fight);
                    default: return _cur == null || _cur.Wave ? null : Copy(_cur);
                }
            }
        }
    }

    /// <summary>
    /// The Always on meter now: the boss pull under way (a pull always starts the meter from 0), else the tally under way,
    /// else null (then the meter holds <see cref="AlwaysEnded"/>).
    /// </summary>
    public Encounter? AlwaysLive { get { lock (_gate) return _cur is { Wave: false } c ? Copy(c) : _always != null ? Tally(Copy(_always)) : null; } }
    static Encounter Tally(Encounter e) { e.Always = true; return e; }

    /// <summary>What the Always on meter showed that ended last (a tally or a pull, with its end), or null.</summary>
    public Encounter? AlwaysEnded { get { lock (_gate) return _alwaysEnded == null ? null : _alwaysEnded.Mode == "fight" ? Tally(Copy(_alwaysEnded)) : Copy(_alwaysEnded); } }

    /// <summary>Copies of every record under way (the pull, the run, the fight), for encounter-live.json.</summary>
    public List<Encounter> LiveAll { get { lock (_gate) return new[] { _cur, _run, _fight }.Where(e => e != null && !e.OpenWorld).Select(e => Copy(e!)).ToList(); } }

    /// <summary>The open-world fight under way, in any mode (null when none, or inside an instance): the Parser page's live DPS. Never kept.</summary>
    public Encounter? Farm
    {
        get
        {
            lock (_gate)
            {
                var f = _mode == ParserMode.Fights ? (_fight is { OpenWorld: true } ? _fight : null) : _farm;
                return f == null ? null : Copy(f);
            }
        }
    }

    /// <summary>A copy of a record (the live copy the window draws), its big hits with it (they are never written).</summary>
    public static Encounter Copy(Encounter e)
    {
        var c = JsonSerializer.Deserialize<Encounter>(JsonSerializer.Serialize(e))!;
        if (e.BigHits is { Count: > 0 } b) c.BigHits = b.ToList();
        // the big hit's measure too (a record restored from a copy keeps its members' averages); the round trip keeps the order
        for (int i = 0; i < c.Roster.Count && i < e.Roster.Count; i++) { c.Roster[i].StreamHitCount = e.Roster[i].StreamHitCount; c.Roster[i].StreamHitSum = e.Roster[i].StreamHitSum; }
        return c;
    }

    /// <summary>A dungeon whose fights are kept (Encounter.Dungeon): a Transcendence, an Expedition, a Sanctuary, a Nightmare instance.</summary>
    static bool Kept(ContentKind k) => Encounter.Dungeon(k);

    /// <summary>
    /// The world bosses whose pulls are kept wherever they are fought (Parser/WorldBosses.cs): the session gives the timers'
    /// list; none by default, so a boss outside a dungeon is the live-only meter's.
    /// </summary>
    public WorldBossBook World { get; set; } = WorldBossBook.None;

    // ---- the mode ----

    /// <summary>
    /// What is kept from now: the record of the old mode ends where it is (kept when worth it, partial: "the Parser's mode
    /// changed"); a run starts at once when the player is in one of the three (partial: counted from partway through).
    /// A boss pull under way carries on whatever the mode, and is kept when it ends if the mode is then Bosses only.
    /// </summary>
    public void SetMode(ParserMode mode, DateTime now)
    {
        List<Encounter> done = new();
        lock (_gate)
        {
            if (mode == _mode) return;
            const string why = "the Parser's mode changed";
            if (_run != null) { MarkPartial(_run, why); CloseRun(RunEnd(_run), Max(_run.LastAt, Min(now, _run.LastAt.AddMinutes(1))), done, why); }
            if (_fight != null) { MarkPartial(_fight, why); CloseFight(_fight.LastAt, done, why); }
            _farm = null;
            _mode = mode;
            if (mode == ParserMode.Run && Kept(_kind)) OpenRun(now, midway: true);
            Log($"mode: {Name(mode)}");
            _version++;
        }
        Finish(done, null);
    }

    public static string Name(ParserMode m) => m switch { ParserMode.Run => "Full run", ParserMode.Fights => "Every fight", _ => "Bosses only" };

    /// <summary>What the mode counts, in one sentence (the Parser page's head, the overlay's menu).</summary>
    public static string Counts(ParserMode m) => m switch
    {
        ParserMode.Run => "Each Transcendence, Expedition, Sanctuary, Nightmare or Ascension Trial run, from entering to leaving, trash included, with every boss as a split.",
        ParserMode.Fights => $"Every fight anywhere, the open world and the Abyss too: from the first hit or kill to {PauseSeconds:0} seconds without one. Open-world and Abyss fights show while they last and are never kept.",
        _ => "Each boss pull in Transcendence, Expeditions, Sanctuaries, Nightmare and Ascension Trials, and each world boss; trash and the rest of the open world never count.",
    };

    public static string ModeKey(ParserMode m) => m switch { ParserMode.Run => "run", ParserMode.Fights => "fight", _ => "boss" };
    public static ParserMode ModeOf(string? key) => key switch { "run" => ParserMode.Run, "fight" => ParserMode.Fights, _ => ParserMode.Bosses };

    // ---- the events ----

    public void Take(BossEvent ev)
    {
        List<Encounter> done = new();
        DateTime came;
        lock (_gate)
        {
            _lastEventAt = ev.At;
            if (ev.At > _newestAt) _newestAt = ev.At;
            // an estimated heal comes about a second after the heal itself, by design: the window's capture-to-screen time counts
            // it from when it came (the newest event's time by then), so that line says how fast the tracker draws (2026-11-18:
            // those heals alone made its 95th percentile read ~1 s in a party with a healer, while every hit was drawn within tens of ms)
            came = ev is HealDone { Estimate: true } ? _newestAt : ev.At;
            TakeLocked(ev, done);
            _version++;
        }
        Finish(done, came);
    }
    /// <summary>The newest time any event said so far: when an estimated heal reached the Parser, by the events' own clock.</summary>
    DateTime _newestAt = DateTime.MinValue;

    void TakeLocked(BossEvent ev, List<Encounter> done)
    {
        ClassOf = _screenClass.Count > 0 || _youClass.Length > 0 ? ApplyClass : null;
        switch (ev)
        {
            case InstanceEntered ie:
            {
                var kind = ie.Kind != ContentKind.Other ? ie.Kind : _book.KindOf(ie.Name);
                // an instance the game's messages can't place (no kind, no name, or a mode word for one): its map events'
                // code (MapEvent) or the screen's place (ScreenPlace) names it
                if (BossBook.IsModeWord(ie.Name)) ie = ie with { Name = "" };
                bool unplaced = kind == ContentKind.Other && ie.Name.Trim().Length == 0;
                _enteredAt = ie.At;
                // never by the decoder's number for the entry (the tracker's decoder numbers every entry, a reconnect into the
                // same copy too; a replay's restarts at each connection): by name. The same name again is a reconnect or a new
                // run of it: after its last boss died, a new run at once; else what comes next decides (Reentry)
                // (a Transcendence and its expedition are two places: the same name of another kind is another copy)
                bool sameName = !unplaced && Kept(kind) && Kept(_kind) && kind == _kind && BossBook.Strip(ie.Name).Equals(BossBook.Strip(_instance), StringComparison.OrdinalIgnoreCase);
                bool again = sameName && (_run is { } r0 && RunEnd(r0) == "cleared" || _book.BossesOf(_instance).Any(b => b.Final && _downHere.Contains(b.Name)));
                _reentry = null;
                if (sameName && !again)
                {
                    var before = _gapBefore is { } gb && (ie.At - gb.At).TotalSeconds <= ReentryGapSeconds ? gb : Snapshot(ie.At);
                    _reentry = new Reentry(ie.At, before, _cur?.LastAt,
                        _group.Where(m => !m.You && m.Name.Trim().Length > 0).Select(m => m.Name.Trim()).ToHashSet(StringComparer.OrdinalIgnoreCase));
                }
                _gapBefore = null;
                if (again) Log($"entered {ie.Name.Trim()} again after its last boss died: a new run");
                bool SameCopy(Encounter e) => !again && (unplaced || (BossBook.Strip(ie.Name).Equals(BossBook.Strip(e.Instance), StringComparison.OrdinalIgnoreCase) && Kept(kind) && Encounter.KindOf(e.Kind) == kind));
                if (_cur != null)
                {
                    if (SameCopy(_cur)) Rejoined(_cur, ie);
                    else ClosePull(_cur, BossOutcome.Left, _cur.LastAt, done, "left for another place", yours: true);
                }
                _preHits.Clear(); _inferred = null; _recentTaken.Clear(); _pendingDeaths.Clear(); _effectsOn.Clear(); _castsLanded.Clear();
                if (_run != null)
                {
                    if (SameCopy(_run)) Rejoined(_run, ie);
                    else CloseRun(RunEnd(_run), _run.LastAt, done, "left for another place");
                }
                // Always on: into or out of a Transcendence, an Expedition or a Sanctuary starts the meter from 0; back into the same copy does not
                {
                    bool was = Kept(_kind), to = Kept(kind);
                    // by name, never the id (the decoder numbers every entry, a reconnect into the same copy too); running the
                    // same dungeon again comes after leaving it (InstanceLeft), which started the meter again already. A place
                    // the game's messages can't name: the screen's place decides (ScreenPlace)
                    bool sameCopy = !again && (unplaced || (was == to && (!to || BossBook.Strip(ie.Name).Equals(BossBook.Strip(_instance), StringComparison.OrdinalIgnoreCase))));
                    if (!sameCopy) EndAlways(ie.At, to ? $"entered {ie.Name.Trim()}" : "left the instance");
                }
                // the bosses down and the names given: this copy's, kept through a reconnect into it
                if (!sameName || again) { _bossNames.Clear(); _downHere.Clear(); _downIds.Clear(); _gameMode = ""; _modeCode = null; _transcend = false; _stage = 0; _roomClosedAt = null; }
                _bossFromGame = false; _trialByCode = false; TrialReset();
                // a Transcendence the game names as one ("Shattered Arkanis [Stage 3]", its own kind): its stage from the name
                if (kind == ContentKind.Transcendence) { _transcend = true; _stage = BossBook.StageIn(ie.Name) is > 0 and var sn ? sn : _stage; _gameMode = BossBook.StageMode(StageNow(ie.At)); }
                // a new run of the same instance: the group comes again from the game (the one before was the last run's)
                if (again) _group = _group.Where(m => m.You).ToList();
                _instanceId = ie.InstanceId;
                if (unplaced) { _instanceFromGame = false; Log("entered an instance the game's messages don't name: its map events or the screen's place name it"); break; }
                _kind = kind; _instance = ie.Name.Trim(); _instanceFromGame = Kept(kind);
                if (Kept(kind)) _farm = null;   // farming is over: an instance's fights are its runs and pulls
                if (_mode == ParserMode.Run && _run == null && Kept(kind)) OpenRun(ie.At, midway: false);
                if (_fight != null && !Kept(kind)) { } // a fight carries on through a loading screen only by its own pause
                Log($"entered {(_instance.Length > 0 ? _instance : "a place")}{(Kept(kind) ? $" ({Encounter.KindName(kind)})" : "")}");
                break;
            }
            case InstanceLeft il:
                LeftPlace(il.At, done);
                break;
            case InstanceMode im:
            {
                var m = im.Mode.Trim();
                // "G": the game's mark of a Transcendence, never a mode to show (2026-11-17: Shattered Arkanis and Deus Research
                // Base were kept as expeditions in the mode "G")
                if (BossBook.IsTranscendenceWord(m)) { Transcend(im.At, 0, "the game's mode"); break; }
                // a piece of the place's own name ("Temple" of Fire_Temple), never its mode
                if (!BossBook.IsModeWord(m) && _book.IsCodePart(m)) { Log($"{(_instance.Length > 0 ? _instance : "the instance")}: \"{m}\" is a piece of the place's name, not its mode: left out"); break; }
                if (_transcend) { Log($"{_instance}: \"{m}\" left out, a Transcendence's mode is its stage"); break; }
                _gameMode = m;
                foreach (var e in Records())
                    if (e.ModeKind != ParserMode.Fights || Kept(_kind)) e.GameMode = _gameMode;
                Log($"{(_instance.Length > 0 ? _instance : "the instance")}: {(_gameMode.Length > 0 ? _gameMode : "no mode named")}");
                break;
            }
            case BossEngaged be:
                _bossFromGame = true;   // the game marks this place's bosses: no trial boss is worked out from the damage (TrialHit)
                Engage(be, done);
                break;
            case BossEnded end:
                // one of bosses fought together died: the pull goes on until the last of them
                if (_cur != null && end.Outcome == BossOutcome.Kill && end.BossId != 0 && _cur.WithIds != null && _cur.IsBoss(end.BossId) && !JointDown(end.BossId, end.At)) break;
                if (_cur != null && (end.BossId == 0 || _cur.IsBoss(end.BossId))) ClosePull(_cur, end.Outcome, end.At, done, null);
                break;
            case UnitDied ud:
            {
                // your death comes twice (this row and the kill tally's): counted once
                bool youCounted = ud.Id != 0 && Records().Any(r => r.You?.Id == ud.Id) && SameDeathOfYours(ud.At);
                if (_trialTaken.TryGetValue(ud.Id, out var tt) && !tt.Dead) { tt.Dead = true; _trialDeadMax = Math.Max(_trialDeadMax, tt.Damage); }
                bool bossDied = _cur != null && ((ud.Id != 0 && _cur.IsBoss(ud.Id)) || (ud.Boss && ud.Name.Trim().Equals(_cur.Boss, StringComparison.OrdinalIgnoreCase) && (ud.Id == 0 || _cur.BossId == 0)));
                // one of bosses fought together: a kill of it, and the pull goes on until the last of them died
                if (bossDied && ud.Id != 0 && _cur!.WithIds != null && !JointDown(ud.Id, ud.At))
                {
                    _cur.LastAt = Max(_cur.LastAt, ud.At);
                    _cur.Kills.Add(new KillNear { At = ud.At, Name = _pullBossNames.GetValueOrDefault(ud.Id, _cur.Boss), Boss = true });
                    break;
                }
                foreach (var e in Records())
                {
                    if (ReferenceEquals(e, _cur) && bossDied) continue;
                    e.LastAt = Max(e.LastAt, ud.At);
                    if (bossDied) e.Kills.Add(new KillNear { At = ud.At, Name = _cur!.Boss, Boss = true });
                    else if (e.Roster.FirstOrDefault(f => ud.Id != 0 && f.Id == ud.Id && !f.Other) is { } member) { if (!member.You || !youCounted) member.Deaths++; Died(e, member, ud); }
                    else if (ud.Name.Trim().Length > 0) e.Kills.Add(new KillNear { At = ud.At, Name = ud.Name.Trim(), Boss = ud.Boss });
                }
                if (bossDied)
                {
                    _cur!.LastAt = Max(_cur.LastAt, ud.At);
                    _cur.Kills.Add(new KillNear { At = ud.At, Name = _cur.Boss, Boss = true });
                    ClosePull(_cur, BossOutcome.Kill, ud.At, done, null);
                }
                break;
            }
            case GroupChanged gc:
                // back into an instance of the same name: the group says which. Anyone of the group before: a reconnect into
                // the same copy; others only: a new run of it, with another group
                if (_reentry is { } re && gc.Members.Where(m => !m.You && m.Name.Trim().Length > 0).Select(m => m.Name.Trim()).ToList() is { Count: > 0 } others)
                {
                    if (others.Any(re.Others.Contains)) { _reentry = null; Log($"{_instance}: the same group, a reconnect into the same copy"); }
                    else NewCopy(done, re.Others.Count > 0 ? "another group" : "a group where you were on your own");
                }
                _group = gc.Members.Where(m => m.Name.Trim().Length > 0 || m.You).ToList();
                foreach (var e in Records()) { MergeGroup(e, _group, gc.At); ApplyClasses(e); }
                break;
            case DamageDealt dd:
            {
                if (dd.Amount <= 0) break;
                if (dd.Amount / Math.Max(1, dd.Hits) >= ImpossibleHit)
                {
                    Log($"a hit of {dd.Amount:N0} left out (skill {dd.Skill}): no hit is that big, a misread");
                    foreach (var e in Records()) MarkPartial(e, "a misread hit left out");
                    break;
                }
                // the boss of a pull that ended with no end said (a wipe found by its quiet or its reset) hit again: a new pull
                if (_cur == null && _inferred is { } inf && dd.TargetId != 0 && dd.TargetId == inf.BossId
                    && (Kept(_kind) ? _instanceId == inf.InstanceId : World.Find(inf.Boss) != null))
                {
                    _inferred = null;
                    Log($"{inf.Boss} hit again after its pull ended: a new pull");
                    Engage(new BossEngaged(dd.At, inf.Boss, inf.BossId), done);
                }
                if (_mode == ParserMode.Fights && _fight == null) OpenFight(dd.At);
                FarmAt(dd.At);
                if (Ours(dd.SourceId, dd.SourceIsYou)) AlwaysFight(dd.At, open: true);
                TrialHit(dd, done);
                // a dungeon's trash (Steven, 2026-11-17: "this needs to track the dps but only LOG the bosses"): the live meter's,
                // the run's and Every fight's, never a boss pull's (an add hit during a pull included)
                if (dd.Trash)
                {
                    foreach (var e in Records()) if (!ReferenceEquals(e, _cur)) Credit(e, dd, toBoss: false);
                    if (FarmRecord is { } ft && (dd.SourceIsYou || ft.Roster.Any(x => x.You && x.Id == dd.SourceId && dd.SourceId != 0))) FarmHit(ft, dd.At, dd.Amount);
                    break;
                }
                // damage to a boss just ahead of its pull: kept for it (Engage takes it); out of a dungeon for a world boss's
                if (_cur == null && dd.TargetId != 0)
                {
                    _preHits.Enqueue(dd);
                    while (_preHits.Count > 0 && (dd.At - _preHits.Peek().At).TotalSeconds > PreSeconds) _preHits.Dequeue();
                }
                // a pull starts at its first damage when that comes well after the pull itself
                if (_cur is { HasDamage: false } c0 && (dd.At - c0.Start).TotalSeconds > StartSlackSeconds)
                {
                    Log($"{c0.Boss}: the first damage {(dd.At - c0.Start).TotalSeconds:0.0} s after the pull, which starts there");
                    c0.MoveStart(dd.At);
                }
                bool toBoss = _cur != null && (_cur.Wave || _cur.IsBoss(dd.TargetId));
                foreach (var e in Records()) Credit(e, dd, toBoss);
                // your hit while farming: the live readout's (FarmDpsNow)
                if (FarmRecord is { } fr && (dd.SourceIsYou || fr.Roster.Any(x => x.You && x.Id == dd.SourceId && dd.SourceId != 0))) FarmHit(fr, dd.At, dd.Amount);
                break;
            }
            case BossHealth bh0:
                if (_cur == null || (bh0.BossId != 0 && _cur.BossId != 0 && !_cur.IsBoss(bh0.BossId))) break;
                var bh = JointHealth(bh0);
                _cur.LastAt = Max(_cur.LastAt, bh.At);
                // back to full after falling, with no damage for a while: the boss reset, the group wiped though no end came
                if (bh.Max > 0 && bh.Hp >= bh.Max && _cur.LastHpFallAt != null && _cur.BossHp < bh.Max
                    && (bh.At - (_cur.LastDamageAt ?? _cur.LastHpFallAt.Value)).TotalSeconds >= ResetQuietSeconds)
                {
                    _inferred = (_cur.Boss, _cur.BossId, _instanceId);
                    ClosePull(_cur, BossOutcome.Wipe, bh.At, done, "the boss reset to full HP");
                    break;
                }
                if (_cur.BossMaxHp > 0 ? bh.Hp < _cur.BossHp : bh.Max > 0 && bh.Hp < bh.Max) _cur.LastHpFallAt = bh.At;
                _cur.BossHp = Math.Max(0, bh.Hp);
                if (bh.Max > 0) _cur.BossMaxHp = bh.Max;
                // the timeline's HP: the last value in each second, carried over seconds with none
                if (_cur.BossHpLeft is { } hpLeft)
                {
                    var hps = _cur.BossHpPerSecond ??= new();
                    int s0 = _cur.SecondOf(bh.At);
                    while (hps.Count <= s0) hps.Add(hps.Count > 0 ? hps[^1] : hpLeft);
                    hps[s0] = hpLeft;
                }
                if (_fight != null) { _fight.LastAt = Max(_fight.LastAt, bh.At); _fight.BossHp = _cur.BossHp; _fight.BossMaxHp = _cur.BossMaxHp; }
                if (_run != null) _run.LastAt = Max(_run.LastAt, bh.At);
                break;
            case HitDealt hd:
            {
                if (hd.Source == 0 || hd.Amount >= ImpossibleHit) break;   // a misread hit (DamageDealt says so once): in no figure
                if (_trialTaken.TryGetValue(hd.Target, out var th) && !th.Dead && th.Hits.Count < TrialRowsKept) th.Hits.Add(hd);
                // a cast counts once, with its first hit that lands here (a cast whose hits all went elsewhere, or missed, isn't
                // this fight's): so an ability's casts are never more than its hits
                bool firstOfCast = !hd.NoRow && !hd.Tick && hd.Skill > 0 && _castsLanded.Add((hd.Actor, hd.Cast, hd.Skill));
                long seq = 0;
                foreach (var e in Records())
                    if (!(hd.Trash && ReferenceEquals(e, _cur)) && Member(e, hd.Source) is { } m)   // trash: never a boss pull's
                    {
                        var a = m.Ability(hd.Skill);
                        StreamHit(a, hd);
                        if (firstOfCast) a.Casts = (a.Casts ?? 0) + 1;
                        BigHitOf(e, m, hd, ref seq);
                    }
                break;
            }
            case HealDone hl:
            {
                if (hl.Source == 0 || hl.Amount <= 0 || hl.Amount >= ImpossibleHit) break;
                foreach (var e in Records()) Heal(e, hl);
                // a heal of a pull's last moment can come after its end (a heal waits for its target's HP): it is still that pull's
                if (_endedPull is { } ep)
                {
                    if ((hl.At - ep.End).TotalSeconds > LateHealSeconds) _endedPull = null;
                    else if (hl.At <= ep.End) { foreach (var e in ep.Copies) Heal(e, hl); LateHeals += hl.Amount; }
                }
                break;
            }
            case CastMade cm:
                // a new cast by this actor: its sequence number is free again for counting (it wraps)
                _castsLanded.RemoveWhere(c => c.Actor == cm.Actor && c.Cast == cm.Cast);
                break;
            case HitTaken ht:
                if (Ours(ht.Target, false)) AlwaysFight(ht.At, open: false);
                Taken(ht);
                break;
            case EffectChanged ec:
                Effect(ec);
                break;
            case FeedGap fg:
                // a new connection, often a run's end: a run cleared ends here, missing nothing; else how it stood is kept, for
                // a new run of the same instance coming next (Reentry) to end it there
                if (_run is { } done0 && _cur == null && RunEnd(done0) == "cleared") CloseRun("cleared", done0.LastAt, done, "a new connection after its last boss");
                _gapBefore = Snapshot(fg.At);
                foreach (var e in Records())
                {
                    e.Gaps++;
                    e.PartialWhy.RemoveAll(w => w.EndsWith("the tracker missed"));
                    MarkPartial(e, e.Gaps == 1 ? "a moment the tracker missed" : $"{e.Gaps} moments the tracker missed");
                }
                break;
        }
    }

    /// <summary>The records under way, the pull first.</summary>
    IEnumerable<Encounter> Records() { if (_cur != null) yield return _cur; if (_run != null) yield return _run; if (_fight != null) yield return _fight; if (_farm != null) yield return _farm; if (_always != null) yield return _always; }

    /// <summary>A fighter of yours: you, or a member of your group (by the game's id).</summary>
    bool Ours(long id, bool you) => you || (id != 0 && (_group.Any(g => g.Id == id) || (_always?.Roster.Any(f => f.Id == id && !f.Other) ?? false)));

    /// <summary>The Always on tally ends here (kept nowhere): the meter holds it, settled, until the next hit starts a new one.</summary>
    void EndAlways(DateTime at, string why)
    {
        if (_always is not { } a) return;
        _always = null;
        a.End = Max(Min(at, a.LastAt), a.Start);
        a.LastAt = Max(a.LastAt, a.End.Value);
        a.Outcome = a.YourDeaths > 0 ? "died" : a.YourKills > 0 || a.Kills.Any(k => k.Boss) ? "kill" : "ended";
        Settle(a);
        _alwaysEnded = a;
        FightHealth(a);
        Log($"always on: the meter starts again ({why})");
    }

    /// <summary>The meter's health numbers (Session/Accuracy.cs, MeterHealth): a fight ended with your damage in it, or others' only.</summary>
    internal static void FightHealth(Encounter e) =>
        Session.MeterHealth.Fight(e.Roster.Any(f => f.You && f.Damage > 0), e.Roster.Any(f => !f.You && !f.Other && f.Damage > 0));

    /// <summary>
    /// A fight of one hit folded into the held fight before it: its damage, abilities, kills and deaths added, and its time,
    /// at least <see cref="Encounter.MinDpsSeconds"/>, added to the fight's length (the quiet between them never counts: no
    /// penalty for stopping). The fold is the meter's only (Always on is never kept).
    /// </summary>
    static void Fold(Encounter into, Encounter one)
    {
        double add = Math.Max(Encounter.MinDpsSeconds, ((one.End ?? one.LastAt) - one.Start).TotalSeconds);
        into.End = into.End!.Value.AddSeconds(add);
        into.LastAt = Max(into.LastAt, into.End.Value);
        into.HasDamage |= one.HasDamage;
        into.BossDamage += one.BossDamage; into.Uncredited += one.Uncredited; into.UncreditedBoss += one.UncreditedBoss;
        into.YourKills += one.YourKills; into.PlayerKills += one.PlayerKills; into.YourDeaths += one.YourDeaths;
        if (one.Outcome == "kill" || one.YourKills > 0) into.Outcome = into.YourDeaths > 0 ? "died" : "kill";
        foreach (var f in one.Roster)
        {
            var g = into.Roster.FirstOrDefault(x => x.Other == f.Other && ((f.Id != 0 && x.Id == f.Id) || (f.Name.Length > 0 && x.Name.Equals(f.Name, StringComparison.OrdinalIgnoreCase)) || (f.You && x.You)));
            if (g == null) { f.PerSecond = null; f.BossPerSecond = null; f.HealPerSecond = null; f.TakenPerSecond = null; into.Roster.Add(f); continue; }
            g.Damage += f.Damage; g.BossDamage += f.BossDamage; g.Deaths += f.Deaths;
            g.Healing += f.Healing; g.HealingEffective += f.HealingEffective; g.HealingEstimated += f.HealingEstimated; g.HealingSelf += f.HealingSelf; g.HealingPotions += f.HealingPotions;
            foreach (var h in f.Heals ?? new())
            {
                var gl = g.Heals ??= new();
                var gh = gl.FirstOrDefault(x => x.Skill == h.Skill);
                if (gh == null) gl.Add(gh = new HealShare { Skill = h.Skill, Potion = h.Potion });
                gh.Healing += h.Healing; gh.Effective += h.Effective; gh.Hits += h.Hits; gh.Self += h.Self;
            }
            foreach (var a in f.Abilities)
            {
                var ga = g.Ability(a.Skill);
                ga.Damage += a.Damage; ga.Hits += a.Hits; ga.Uncounted += a.Uncounted;
                ga.BossDamage += a.BossDamage; ga.BossHits += a.BossHits; ga.BossUncounted += a.BossUncounted;
            }
        }
    }

    /// <summary>Your fighting, for the Always on tally: it opens at your or your group's first hit (never during a boss pull, which is the meter then).</summary>
    void AlwaysFight(DateTime at, bool open)
    {
        if (_always == null && open && _cur is not { Wave: false }) { _always = NewFight(at); _alwaysActiveAt = at; }
        if (_always != null) _alwaysActiveAt = Max(_alwaysActiveAt, at);
    }

    /// <summary>A hit, kill or pull outside an instance in Bosses only or Full run: the open-world fight (Farm) opens if none is under way.</summary>
    void FarmAt(DateTime at)
    {
        if (_mode != ParserMode.Fights && _farm == null && !Kept(_kind)) _farm = NewFight(at);
    }

    void Rejoined(Encounter e, InstanceEntered ie)
    {
        // back in the same copy of the instance (a reconnect, a load screen): the record carries on
        if ((ie.At - e.LastAt).TotalSeconds >= QuietSeconds) MarkPartial(e, ReconnectedWhy);
        if (e.InstanceId == 0) e.InstanceId = ie.InstanceId;
        e.LastAt = Max(e.LastAt, ie.At);
    }

    /// <summary>
    /// The entry into the instance of the same name (Reentry) was a new run of it, not a reconnect: the pull and the run
    /// under way end as they stood before it (no reconnect, no gap of the new run's on them), and a new run starts at the entry.
    /// </summary>
    void NewCopy(List<Encounter> done, string why)
    {
        if (_reentry is not { } re) return;
        _reentry = null;
        Log($"{_instance}: {why}, a new run of it, not a reconnect");
        if (_cur is { } c)
        {
            c.PartialWhy.Remove(ReconnectedWhy);
            c.Partial = c.PartialWhy.Count > 0;
            ClosePull(c, BossOutcome.Left, re.PullLast ?? c.LastAt, done, "a new run of the instance", yours: true);
        }
        if (_run is { } r)
        {
            if (re.Before is { } b)
            {
                r.Partial = b.Partial; r.PartialWhy.Clear(); r.PartialWhy.AddRange(b.PartialWhy); r.Gaps = b.Gaps; r.LastAt = b.LastAt;
            }
            else { r.PartialWhy.Remove(ReconnectedWhy); r.Partial = r.PartialWhy.Count > 0; }
            CloseRun(RunEnd(r), r.LastAt, done, "a new run of the instance");
        }
        _bossNames.Clear(); _downHere.Clear(); _downIds.Clear(); _roomClosedAt = null;
        _group = _group.Where(m => m.You).ToList();   // the group before was the other run's
        EndAlways(re.At, $"entered {_instance}");
        if (_mode == ParserMode.Run && Kept(_kind)) OpenRun(re.At, midway: false);
    }

    /// <summary>The copy's last boss on the list died (its final boss is down here).</summary>
    bool FinalDown() => _book.BossesOf(_instance).Any(b => b.Final && _downHere.Contains(b.Name));

    /// <summary>
    /// A new run of the instance in the same copy, with no entry between (Steven, 2026-11-17: his second Transcendence of
    /// Shattered Arkanis and of Deus Research Base came in the copy of the first, and every boss of them was kept as
    /// "Unnamed boss"): the pull and the run under way end as they stood, the bosses down and the names given are the
    /// last run's, the group stays (the same party), and a run starts here in Full run.
    /// </summary>
    void RunAgain(List<Encounter> done, DateTime at, string why)
    {
        Log($"{_instance}: {why}: a new run of it in the same copy");
        if (_cur is { } c) ClosePull(c, BossOutcome.Left, c.LastAt, done, "a new run of the instance", yours: true);
        if (_run is { } r) CloseRun(RunEnd(r), r.LastAt, done, "a new run of the instance");
        _bossNames.Clear(); _downHere.Clear(); _downIds.Clear(); _roomClosedAt = null;
        EndAlways(at, $"a new run of {_instance}");
        if (_mode == ParserMode.Run && Kept(_kind)) OpenRun(at, midway: false);
    }

    /// <summary>The Transcendence's stage now: its code's or name's, else the chat's stage line within <see cref="StageSaidHours"/>, else 0.</summary>
    int StageNow(DateTime at) => _stage > 0 ? _stage : _stageSaid > 0 && Math.Abs((at - _stageSaidAt).TotalHours) <= StageSaidHours ? _stageSaid : 0;

    /// <summary>
    /// The instance is a Transcendence: the place's kind from here on (an expedition's own area at a stage), its stage the
    /// mode ("Stage 4", or none while not known), and the records under way in it filed so too.
    /// </summary>
    void Transcend(DateTime at, int stage, string why)
    {
        bool was = _transcend; int stageWas = StageNow(at);
        _transcend = true;
        if (stage > 0) _stage = stage;
        if (_kind == ContentKind.Expedition) _kind = ContentKind.Transcendence;
        _gameMode = BossBook.StageMode(StageNow(at));
        if (_kind == ContentKind.Transcendence)
            foreach (var e in Records())
                if (!e.OpenWorld && !e.WorldBoss && (e.ModeKind != ParserMode.Fights || Kept(_kind)))
                {
                    if (e.Kind == Encounter.KindName(ContentKind.Expedition)) e.Kind = Encounter.KindName(ContentKind.Transcendence);
                    e.GameMode = _gameMode;
                }
        if (!was || StageNow(at) != stageWas) Log($"{(_instance.Length > 0 ? _instance : "the instance")}: a Transcendence ({why}){(_gameMode.Length > 0 ? $", {_gameMode}" : ", its stage not known yet")}");
        _version++;
    }

    /// <summary>
    /// The chat's stage line ("Set to Stage 4.", the room's; "Shattered Arkanis Stage 4 I achieved.", a clear's): the stage of
    /// a Transcendence entered within <see cref="StageSaidHours"/> whose code doesn't say one, and of the one under way.
    /// </summary>
    public void StageSaid(int stage, DateTime at)
    {
        if (stage is < 1 or > 9) return;
        lock (_gate)
        {
            _stageSaid = stage; _stageSaidAt = at;
            if (_transcend && _stage == 0 && Kept(_kind)) Transcend(at, 0, $"the chat's Stage {stage}");
        }
    }

    /// <summary>The Transcendence the player is in, by the game's own mark (its instance's name and stage), or null.</summary>
    public (string Instance, int Stage)? TranscendenceHere
    {
        get { lock (_gate) return _transcend && _kind == ContentKind.Transcendence && _instance.Length > 0 ? (BossBook.Strip(_instance), StageNow(DateTime.UtcNow)) : null; }
    }

    /// <summary>
    /// A kill or death of the player's from the game's kill events (Packets/FightTally.cs via TrackerSession.Packets.cs):
    /// your kills (players apart) and deaths for the run and the fight under way. In Every fight it starts a fight.
    /// </summary>
    public void Kill(DateTime at, bool player, bool death, string name)
    {
        List<Encounter> done = new();
        lock (_gate)
        {
            _lastEventAt = Max(_lastEventAt, at);
            if (_mode == ParserMode.Fights && _fight == null) OpenFight(at);
            FarmAt(at);
            AlwaysFight(at, open: false);
            foreach (var e in new[] { _run, _fight, _farm, _always })
            {
                if (e == null) continue;
                e.LastAt = Max(e.LastAt, at);
                if (death) e.YourDeaths++;
                else { e.YourKills++; if (player) e.PlayerKills++; }
                if (player && name.Trim().Length > 0) e.Opponent = name.Trim();
                if (!death) e.Kills.Add(new KillNear { At = at, Name = name.Trim().Length > 0 ? name.Trim() : player ? "Player" : "Monster", Player = player, Yours = true });
            }
            // your death comes twice (the kill tally's and the group's death row): counted once
            if (death && _cur?.You is { } me && !SameDeathOfYours(at)) me.Deaths++;
            _version++;
        }
        Finish(done, at);
    }

    /// <summary>When your last death was counted: the same death said again (the other source, within a few seconds) isn't counted twice.</summary>
    DateTime _youDiedAt = DateTime.MinValue;
    bool SameDeathOfYours(DateTime at)
    {
        bool same = Math.Abs((at - _youDiedAt).TotalSeconds) < 3;
        if (!same) _youDiedAt = at;
        return same;
    }

    // ---- the party list's classes (Ocr/PartyFrame.cs, Steven 2026-11-14: "it's literally an icon in the top left") ----
    readonly Dictionary<string, string> _screenClass = new(StringComparer.OrdinalIgnoreCase);
    string _youClass = "";
    /// <summary>The party list's rows as last read, the player's left out: the names <see cref="NameFromScreen"/> may give.</summary>
    List<(string Name, string Class)> _screenNow = new();
    /// <summary>Your class as the party list showed it ("" until it did): your row on a PvP fight's skills card (Parser/PvpKills.cs).</summary>
    public string YouClass { get { lock (_gate) return _youClass; } }

    /// <summary>
    /// The party list as the screen shows it: each member's name (when read) and class, the player first. A member of a record
    /// whose class isn't known takes it: the player's from the first row, the others' by name (one letter off allowed, for a
    /// misread). A member whose class came as a number pairs that number with the class (Parser/ClassNumbers.cs).
    /// </summary>
    public void ScreenParty(IReadOnlyList<(string Name, string Class, bool You)> rows)
    {
        lock (_gate)
        {
            foreach (var (name, cls, you) in rows)
            {
                if (cls.Length == 0) continue;
                if (you) _youClass = cls;
                if (name.Length > 0) _screenClass[name] = cls;
            }
            _screenNow = rows.Where(r => !r.You && r.Name.Length > 0 && r.Class.Length > 0).Select(r => (r.Name, r.Class)).ToList();
            foreach (var e in Records()) ApplyClasses(e);
            _version++;
        }
        try { Changed?.Invoke(null); } catch (Exception ex) { CrashLog.Write("parser changed", ex); }
    }

    /// <summary>The class the party list gives a name, or "".</summary>
    string ScreenClassOf(string name)
    {
        if (name.Length == 0) return "";
        if (_screenClass.TryGetValue(name, out var c)) return c;
        var want = NameKey(name);
        if (want.Length < 4) return "";
        foreach (var (n, cls) in _screenClass)
        {
            var have = NameKey(n);
            if (have == want || (have.Length >= 5 && want.Length >= 5 && Data.NameMatch.Distance(have, want, 1) <= 1)) return cls;
        }
        return "";
    }

    /// <summary>A name as the matching sees it: lower case, no spaces, I, l, 1 and | one letter, 0 an o (the party list's usual misreads).</summary>
    public static string NameKey(string name)
    {
        var sb = new System.Text.StringBuilder(name.Length);
        foreach (var ch in name.Trim().ToLowerInvariant())
        {
            if (char.IsWhiteSpace(ch)) continue;
            sb.Append(ch is 'i' or '1' or '|' or 'ı' ? 'l' : ch == '0' ? 'o' : ch);
        }
        return sb.ToString();
    }

    void ApplyClasses(Encounter e) { foreach (var f in e.Roster) if (!f.Other) ApplyClass(f); NameFromScreen(e); }

    /// <summary>
    /// A member the game never named (Steven, 2026-11-20: "the last fight didn't pick up one of the party members": a Chanter
    /// already in the instance before him sent no name, so her row had none): the one name of its class on the party list as
    /// it shows now that no other row has. Its class is the decoder's, else its skills' (<see cref="Abilities.ClassOfFamily"/>);
    /// two such names, or none, and the row stays nameless.
    /// </summary>
    void NameFromScreen(Encounter e)
    {
        if (_screenNow.Count == 0) return;
        foreach (var f in e.Roster)
        {
            if (f.Other || f.You || f.Name.Length > 0) continue;
            string cls = f.Class.Length > 0 ? f.Class : ClassBySkills(f);
            if (cls.Length == 0) continue;
            var free = _screenNow.Where(s => s.Class.Equals(cls, StringComparison.OrdinalIgnoreCase)
                && !e.Roster.Any(r => !r.Other && r.Name.Length > 0 && NameKey(r.Name) == NameKey(s.Name))).Select(s => s.Name).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            if (free.Count != 1) continue;
            f.Name = free[0];
            if (f.Class.Length == 0) f.SetClass(cls);
            Log($"a member the game never named ({cls}, its hits only): {f.Name}, the party list's only {cls} not on the meter");
        }
    }

    /// <summary>The class most of a fighter's damage comes from by its skills' family, "" when none or under 75% of it.</summary>
    static string ClassBySkills(Fighter f)
    {
        var by = f.Abilities.GroupBy(a => Abilities.ClassOfFamily(a.Skill)).Where(g => g.Key.Length > 0).Select(g => (Cls: g.Key, Dmg: g.Sum(a => a.Damage))).ToList();
        if (by.Count == 0) return "";
        var top = by.MaxBy(x => x.Dmg);
        return top.Dmg * 4 >= by.Sum(x => x.Dmg) * 3 ? top.Cls : "";
    }

    void ApplyClass(Fighter f)
    {
        string cls = f.You && _youClass.Length > 0 ? _youClass : ScreenClassOf(f.Name);
        if (cls.Length == 0) return;
        if (f.ClassNumber != 0) ClassNumbers.Pair(f.ClassNumber, cls);
        // the decoder names a class from the skills a member hits with (after ten hits); the party list fills the moments
        // before that, and the two are checked against each other here
        if (f.Class.Length > 0 && f.ClassNumber == 0 && !string.Equals(f.Class, cls, StringComparison.OrdinalIgnoreCase))
            CrashLog.NoteBurst($"parser: {(f.Name.Length > 0 ? f.Name : "a member")} is a {f.Class} by their skills, a {cls} by the party list's icon; kept {f.Class}");
        if (f.Class.Length == 0 || f.ClassNumber != 0) f.Class = cls;
    }

    /// <summary>Where the session reads the player is (its zone, and whether that is the Abyss): Every fight's place outside the three.</summary>
    public void Place(string? zone, bool abyss)
    {
        lock (_gate)
        {
            var z = zone?.Trim() ?? "";
            if (z == _zone && abyss == _abyss) return;
            _zone = z; _abyss = abyss;
            _version++;
        }
    }

    /// <summary>The names given to unnamed bosses by spawn (a wipe and a pull again keep it), and the bosses down in this copy of the instance.</summary>
    readonly Dictionary<long, string> _bossNames = new();
    readonly HashSet<string> _downHere = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>The spawns killed in this copy (by the game's id): one of them engaged again is the same dead boss, never a new run.</summary>
    readonly HashSet<long> _downIds = new();

    /// <summary>
    /// An unnamed boss's name: the one this spawn had, else the instance's next boss on the boss list in route order that
    /// isn't down yet (waves left out); "Unnamed boss" when the list has none.
    /// </summary>
    string NameUnnamed(long bossId)
    {
        if (bossId != 0 && _bossNames.TryGetValue(bossId, out var had)) return had;
        var next = _book.BossesOf(_instance).FirstOrDefault(b => !b.Wave && !_downHere.Contains(b.Name) && !_bossNames.ContainsValue(b.Name));
        var name = next?.Name ?? UnnamedBoss;
        if (bossId != 0) _bossNames[bossId] = name;
        Log($"an unnamed boss in {(_instance.Length > 0 ? _instance : "an instance")}: {name}{(next != null ? " (the next on the boss list)" : "")}");
        return name;
    }

    void Engage(BossEngaged be, List<Encounter> done)
    {
        var name = be.Boss.Trim();
        // out of a dungeon, a world boss the timers know (by its name, its short name, or the only one they place here when the
        // pull came unnamed) is a kept pull wherever it is fought, under the timers' full name and with its place
        WorldBossBook.Boss? world = null;
        if (!Kept(_kind) && (World.Find(name) ?? (name.Length == 0 ? World.OnlyIn(_zone) : null)) is { } wb)
        {
            world = wb;
            if (!wb.Name.Equals(name, StringComparison.Ordinal)) Log($"{(name.Length > 0 ? name : "an unnamed boss")}: the world boss {wb.Name} on the timers");
            name = wb.Name;
        }
        // a boss of the list pulled after the copy's last boss died, with no entry between (a Transcendence's next try in the
        // same copy: Shattered Arkanis 16:56 and Deus Research Base 17:21 on 2026-11-17, whose bosses were kept as "Unnamed
        // boss"): a new run of the instance, its bosses named again from the first, whether or not the game said a new entry
        if (world == null && Kept(_kind) && _reentry == null && FinalDown() && !(be.BossId != 0 && (_downIds.Contains(be.BossId) || _bossNames.ContainsKey(be.BossId)))
            && (name.Length == 0 || _book.Find(_instance, name) != null))
            RunAgain(done, be.At, $"{(name.Length > 0 ? name : "a boss")} pulled after its last boss died");
        if (name.Length == 0) name = Kept(_kind) ? NameUnnamed(be.BossId) : UnnamedBoss;   // a pull the decoder couldn't name: still a fight
        if (!Kept(_kind) && world == null)
        {
            Log($"{name}: not in a dungeon{(_instance.Length > 0 ? $" ({_instance})" : "")} and not a world boss the timers know, no boss pull kept");
            if (_mode == ParserMode.Fights) { if (_fight == null) OpenFight(be.At); _fight!.LastAt = Max(_fight.LastAt, be.At); if (_fight.Boss.Length == 0) _fight.Boss = name; }
            FarmAt(be.At);
            // Always on: a boss out here (a field or world boss) starts the meter from 0, under its name; the same boss again carries on
            if (_always == null || !_always.Boss.Equals(name, StringComparison.OrdinalIgnoreCase))
            {
                EndAlways(be.At, $"{name} was pulled");
                _always = NewFight(be.At);
                _always.Boss = name;
            }
            _always.LastAt = Max(_always.LastAt, be.At);
            _alwaysActiveAt = Max(_alwaysActiveAt, be.At);
            if (_farm != null) { _farm.LastAt = Max(_farm.LastAt, be.At); if (_farm.Boss.Length == 0) _farm.Boss = name; }
            return;
        }
        BossBook.Boss? listed = world == null ? _book.Find(_instance, name) : null;
        if (world == null && _book.Knows(_instance) && listed == null && name != UnnamedBoss) Log($"{name}: not on data/bosses.txt for {_instance}; kept under the game's name");
        // bosses fought together (bosses.txt "with": Sanctum of Loathing's two): one pull under all their names
        var together = listed != null ? _book.Together(_instance, listed.Name) : Array.Empty<BossBook.Boss>();
        string pullName = together.Count > 0 ? BossBook.JointName(together) : listed?.Name ?? name;
        // back into an instance of the same name (Reentry): a boss already down here pulled again is a new run of it; any
        // other boss, the same copy
        if (_reentry != null && world == null)
        {
            if (_downHere.Contains(listed?.Name ?? name)) NewCopy(done, $"{listed?.Name ?? name} pulled again after its kill");
            else { _reentry = null; Log($"{_instance}: {listed?.Name ?? name} pulled, not down yet, a reconnect into the same copy"); }
        }
        if (_cur != null)
        {
            bool same = be.BossId != 0 && _cur.BossId != 0 ? _cur.IsBoss(be.BossId) : name.Equals(_cur.Boss, StringComparison.OrdinalIgnoreCase) || pullName.Equals(_cur.Boss, StringComparison.OrdinalIgnoreCase);
            if (same) { _cur.LastAt = Max(_cur.LastAt, be.At); return; }
            // another of the bosses fought together with the pull's, none of its name in it yet: the same pull, one boss more
            if (together.Count > 0 && be.BossId != 0 && pullName.Equals(_cur.Boss, StringComparison.OrdinalIgnoreCase) && !_pullBossNames.ContainsValue(listed!.Name))
            {
                Join(_cur, be.BossId, listed.Name, be.At);
                return;
            }
            // a new boss (or a new spawn of this one): the pull under way is over, and this one starts afresh
            ClosePull(_cur, BossOutcome.Left, Max(_cur.LastAt, _cur.Start), done, $"{name} was pulled");
        }
        _cur = world != null
            ? new Encounter
            {
                // a world boss: its kind, its place as the instance (the timers', else where the session reads the player is)
                Mode = "boss", Kind = Encounter.WorldBossKind, Instance = world.Zone.Length > 0 ? world.Zone : _zone, GameMode = "",
                Boss = world.Name, BossId = be.BossId, Start = be.At, LastAt = be.At, App = Upload.RunPayload.AppVersion, TargetsSplit = true,
            }
            : new Encounter
            {
                Mode = "boss", Kind = Encounter.KindName(_kind), Instance = _instance, InstanceId = _instanceId, GameMode = _gameMode, ModeCode = _modeCode,
                Boss = pullName, BossId = be.BossId, FinalBoss = together.Count > 0 ? together.Any(b => b.Final) : listed?.Final == true, Wave = listed?.Wave == true,
                Start = be.At, LastAt = be.At, App = Upload.RunPayload.AppVersion, TargetsSplit = true,
            };
        _pullBossNames.Clear(); _jointDown.Clear(); _jointHp.Clear();
        _pullTogether = together.Count;
        if (be.BossId != 0) _pullBossNames[be.BossId] = listed?.Name ?? name;
        MergeGroup(_cur, _group, null);
        ApplyClasses(_cur);
        _inferred = null;
        // Always on: the pull is the meter now, from 0 (a wave's damage stays in the tally)
        if (!_cur.Wave) EndAlways(be.At, $"{_cur.Boss} was pulled");
        // damage to this boss just ahead of its pull is the pull's; the pull starts at it when it came well before
        if (be.BossId != 0)
        {
            var pre = _preHits.Where(h => h.TargetId == be.BossId && h.At >= be.At.AddSeconds(-PreSeconds) && h.At <= be.At).ToList();
            if (pre.Count > 0)
            {
                var first = pre.Min(h => h.At);
                if ((be.At - first).TotalSeconds > StartSlackSeconds) { _cur.MoveStart(first); Log($"{_cur.Boss}: the first damage {(be.At - first).TotalSeconds:0.0} s ahead of the pull, which starts there"); }
                foreach (var h in pre)
                {
                    Credit(_cur, h, toBoss: true);
                    foreach (var r in new[] { _run, _fight }) if (r != null) Promote(r, h);
                }
            }
        }
        _preHits.Clear();
        if (_mode == ParserMode.Run && _run == null && world == null) OpenRun(be.At, midway: true);   // no run out of a dungeon
        if (_mode == ParserMode.Fights)
        {
            if (_fight == null) OpenFight(be.At);
            _fight!.LastAt = Max(_fight.LastAt, be.At);
            if (!_cur.Wave || _fight.Boss.Length == 0) { _fight.Boss = _cur.Boss; _fight.BossId = _cur.BossId; _fight.FinalBoss = _cur.FinalBoss; }
        }
        if (_run != null) _run.LastAt = Max(_run.LastAt, be.At);
        Log($"{_cur.Boss} pulled in {_cur.Instance} ({_cur.Kind}), {_cur.Roster.Count} in the group: a new pull");
    }

    void OpenRun(DateTime at, bool midway)
    {
        _run = new Encounter
        {
            Mode = "run", Kind = Encounter.KindName(_kind), Instance = _instance, InstanceId = _instanceId, GameMode = _gameMode, ModeCode = _modeCode,
            Start = at, LastAt = at, App = Upload.RunPayload.AppVersion, TargetsSplit = true,
        };
        if (midway) MarkPartial(_run, "counted from partway through the run");
        MergeGroup(_run, _group, null);
        Log($"run: {_instance} ({_run.Kind}){(midway ? ", from partway through" : "")}");
    }

    void OpenFight(DateTime at) => _fight = NewFight(at);

    /// <summary>A fight starting now, where the player is: inside an instance it is the instance's; else the open world's or the Abyss's.</summary>
    Encounter NewFight(DateTime at)
    {
        bool inside = Kept(_kind);
        var f = new Encounter
        {
            Mode = "fight", Kind = inside ? Encounter.KindName(_kind) : _abyss ? "Abyss" : "Open world",
            Instance = inside ? _instance : _zone, InstanceId = inside ? _instanceId : 0, GameMode = inside ? _gameMode : "",
            Start = at, LastAt = at, App = Upload.RunPayload.AppVersion, TargetsSplit = true,
        };
        MergeGroup(f, _group, null);
        return f;
    }

    /// <summary>Names a fighter's class from the party list on the screen, when there is one (set per parser: <see cref="ScreenParty"/>).</summary>
    [ThreadStatic] static Action<Fighter>? ClassOf;

    /// <summary>
    /// One hit onto a record: its damage (to the boss or not) and its time; the member it names gets it with its ability. A
    /// hit with no source (id 0) is the record's uncredited damage, in no member and no row (Steven, 2026-11-14: "no 'Other'").
    /// </summary>
    static void Credit(Encounter e, DamageDealt dd, bool toBoss)
    {
        e.LastAt = Max(e.LastAt, dd.At);
        e.HasDamage = true;
        e.LastDamageAt = e.LastDamageAt is { } ld ? Max(ld, dd.At) : dd.At;
        if (toBoss) e.BossDamage += dd.Amount;
        if (dd.SourceId == 0)
        {
            e.Uncredited += dd.Amount;
            if (toBoss) e.UncreditedBoss += dd.Amount;
            e.UncreditedWhy[dd.Reason] = e.UncreditedWhy.GetValueOrDefault(dd.Reason) + dd.Amount;
            return;
        }
        var src = SourceOf(e, dd);
        src.Damage += dd.Amount;
        if (toBoss) src.BossDamage += dd.Amount;
        int sec = e.SecondOf(dd.At);
        src.PerSecond = Fighter.AddAt(src.PerSecond, sec, dd.Amount);
        if (toBoss) src.BossPerSecond = Fighter.AddAt(src.BossPerSecond, sec, dd.Amount);
        src.AddAbility(dd.Skill, dd.Amount, dd.Hits, toBoss);
        src.Hit(dd.At);
    }

    // ---- store v2: the hit stream (klnet's interface version 10) ----

    /// <summary>The last hits each member took (a few seconds of them), for a death's recap.</summary>
    readonly Dictionary<long, Queue<HitTaken>> _recentTaken = new();
    /// <summary>Deaths whose killing blow may still come (it can follow the death by about a second): the recaps to add it to.</summary>
    readonly List<(long Target, DateTime At, List<(Encounter E, DeathRecord D)> Recaps)> _pendingDeaths = new();
    /// <summary>The effects on now, by member and slot: the effect and when it went on.</summary>
    readonly Dictionary<(long Target, int Slot), (int Effect, DateTime At)> _effectsOn = new();
    /// <summary>The casts (actor, cast number, skill) one of whose hits has landed: each is counted once.</summary>
    readonly HashSet<(long Actor, int Cast, int Skill)> _castsLanded = new();
    /// <summary>A recap's last hits, and how long after the death a hit taken still counts for it.</summary>
    const int RecapHits = 5;
    const double RecapLateSeconds = 1.5;

    static Fighter? Member(Encounter e, long id) => id == 0 ? null : e.Roster.FirstOrDefault(f => f.Id == id && !f.Other);

    long _bigHitSeq;

    /// <summary>
    /// A hit against the member's average hit so far in this record: <see cref="Encounter.BigHitTimes"/> or more (once the
    /// average is a measure) is a big hit, kept on the record for the meter's pop-up, a crit only when the record carries
    /// crit data. Ticks and parts with no hit under them are not hits. One hit is one Seq in every record.
    /// </summary>
    void BigHitOf(Encounter e, Fighter m, HitDealt hd, ref long seq)
    {
        if (hd.NoRow || hd.Tick || hd.Amount <= 0) return;
        int n = m.StreamHitCount;
        bool big = n >= Encounter.BigHitAfter && hd.Amount >= Encounter.BigHitTimes * (m.StreamHitSum / (double)n);
        m.StreamHitCount = n + 1; m.StreamHitSum += hd.Amount;
        if (!big) return;
        if (seq == 0) seq = ++_bigHitSeq;
        bool? crit = Encounter.CritKey switch { "result:3" => hd.Result == 3, "mark:3" => (hd.Marks & (1 << 3)) != 0, _ => null };
        var list = e.BigHits ??= new();
        list.Add(new BigHit(seq, m.Id, hd.Amount, crit, hd.At));
        if (list.Count > Encounter.BigHitsKept) list.RemoveAt(0);
    }

    /// <summary>One hit of the stream onto its ability: counted, its size into the biggest and smallest, its kind into the raw counters.</summary>
    static void StreamHit(AbilityShare a, HitDealt h)
    {
        if (h.NoRow) return;   // a part with no hit under it: its damage is counted already, it is no hit
        a.Picks |= Abilities.PicksOf(h.Ranked);
        a.StreamHits = (a.StreamHits ?? 0) + 1;
        long amt = Math.Max(0, h.Amount);
        a.StreamDamage = (a.StreamDamage ?? 0) + amt;
        if (h.Amount > 0) { a.Max = Math.Max(a.Max ?? 0, h.Amount); a.Min = a.Min is { } mn ? Math.Min(mn, h.Amount) : h.Amount; }
        var k = a.Kinds ??= new();
        var kd = a.KindDamage ??= new();
        var km = a.KindMax ??= new();
        void Count(string key)
        {
            k[key] = k.GetValueOrDefault(key) + 1;
            kd[key] = kd.GetValueOrDefault(key) + amt;
            km[key] = Math.Max(km.GetValueOrDefault(key), amt);
        }
        Count("result:" + h.Result);
        for (int b = 0; b < 12; b++) if ((h.Marks & (1 << b)) != 0) Count("mark:" + b);
        // the side the hit came from, when the row says one (bit 14), kept whole from bits 12-13: 2 in front, 1 behind
        if ((h.Marks & 0x4000) != 0) Count("side:" + ((h.Marks >> 12) & 3));
        if (h.Multi > 0) Count("multi");
        if (h.Tick) Count("tick");
    }

    /// <summary>The pull that just ended (its own record, the Always on meter's held copy): a heal of its last moment can come after its end.</summary>
    (DateTime End, List<Encounter> Copies)? _endedPull;
    /// <summary>A heal comes at most this long after it was dealt (it waits for its target's next HP change, a second at most).</summary>
    const double LateHealSeconds = 2;
    /// <summary>
    /// Healing that came after its pull ended, put into the pull's own record and the meter's held copy (Always on); the copy
    /// already handed to <see cref="Ended"/> (saved, and the overlay's result) is not touched after it went out. Replays print it.
    /// </summary>
    public long LateHeals { get; private set; }

    /// <summary>A heal onto a record: the healer's healing, its effective part (and how much of that is an estimate), self and potions, by ability.</summary>
    static void Heal(Encounter e, HealDone hl)
    {
        if (hl.At < e.Start || (e.End is { } end && hl.At > end) || Member(e, hl.Source) is not { } m) return;
        // a heal of another class's skill is that class's member's (Steven, 2026-11-16: a Chanter's Sprint Mantra came named
        // for each member it healed, as their own self-heal): the group's one member of the skill's class, the heal on others
        // unless it landed on them; two of that class, or none, and it stays where it came
        if (!hl.Potion && CasterOf(e, m, hl.Skill) is { } caster) { m = caster; hl = hl with { Source = caster.Id, Self = hl.Target == caster.Id }; Recredited += hl.Amount; }
        long eff = Math.Clamp(hl.Effective, 0, hl.Amount);
        // a potion is in no heal figure (Steven, 2026-11-15: "self heals should count, potions should not"): kept apart, and
        // listed by ability as a potion; a heal of your own from a skill counts as any heal
        if (hl.Potion) m.HealingPotions += hl.Amount;
        else
        {
            m.Healing += hl.Amount;
            m.HealingEffective += eff;
            if (hl.Estimate) m.HealingEstimated += eff;
            if (hl.Self) m.HealingSelf += hl.Amount;
            if (eff > 0) m.HealPerSecond = Fighter.AddAt(m.HealPerSecond, e.SecondOf(hl.At), eff);   // the page's heal graph
        }
        var list = m.Heals ??= new();
        var a = list.FirstOrDefault(x => x.Skill == hl.Skill);
        if (a == null) list.Add(a = new HealShare { Skill = hl.Skill, Potion = hl.Potion });
        a.Healing += hl.Amount; a.Effective += eff; a.Hits++;
        if (hl.Self) a.Self += hl.Amount;
    }

    /// <summary>
    /// The member a heal's skill belongs to when it isn't the named healer's class: the group's only member of the skill's class
    /// (from the skill list, else the id's family); null when the skill is the healer's own class, has no class, or the group
    /// has no one, or two, of it.
    /// </summary>
    internal static Fighter? CasterOf(Encounter e, Fighter named, int skill)
    {
        int s = skill >= 100_000_000 ? Abilities.SkillOfEffect(skill) : skill;
        string cls = Abilities.ClassOf(s);
        if (cls.Length == 0 || named.Class.Length == 0 || string.Equals(cls, named.Class, StringComparison.OrdinalIgnoreCase)) return null;
        var of = e.Roster.Where(f => !f.Other && string.Equals(f.Class, cls, StringComparison.OrdinalIgnoreCase)).Take(2).ToList();
        return of.Count == 1 ? of[0] : null;
    }

    /// <summary>Healing moved to its skill's caster by <see cref="CasterOf"/> (replays print it).</summary>
    [ThreadStatic] public static long Recredited;

    /// <summary>Who dealt a hit taken, in words: the boss's name when it was the boss, a member's when a member, else "".</summary>
    string SourceName(Encounter e, long attacker) => attacker == 0 ? "" : _cur != null && attacker == _cur.BossId ? _cur.Boss : Member(e, attacker)?.Name ?? "";

    /// <summary>A hit a member took: into each record's damage taken, the member's recent hits, and a death's recap it is late for.</summary>
    void Taken(HitTaken ht)
    {
        foreach (var e in Records())
        {
            if (Member(e, ht.Target) is not { } m) continue;
            var list = m.Taken ??= new();
            var t = list.FirstOrDefault(x => x.Skill == ht.Skill && x.SourceId == ht.Attacker);
            if (t == null) list.Add(t = new TakenShare { Skill = ht.Skill, SourceId = ht.Attacker, Source = SourceName(e, ht.Attacker) });
            t.Damage += Math.Max(0, ht.Amount); t.Hits++;
            if (ht.Amount > 0) m.TakenPerSecond = Fighter.AddAt(m.TakenPerSecond, e.SecondOf(ht.At), ht.Amount);   // the page's damage-taken graph
        }
        if (!_recentTaken.TryGetValue(ht.Target, out var q)) _recentTaken[ht.Target] = q = new();
        q.Enqueue(ht);
        while (q.Count > RecapHits) q.Dequeue();
        _pendingDeaths.RemoveAll(p => (ht.At - p.At).TotalSeconds > RecapLateSeconds);
        foreach (var (target, _, recaps) in _pendingDeaths.Where(p => p.Target == ht.Target))
            foreach (var (e, d) in recaps)
            {
                d.Last.Add(new TakenHit { At = Math.Round((ht.At - e.Start).TotalSeconds, 2), Skill = ht.Skill, Source = SourceName(e, ht.Attacker), Amount = ht.Amount });
                while (d.Last.Count > RecapHits) d.Last.RemoveAt(0);
                if (d.KillerId == 0) { d.KillerId = ht.Attacker; d.KillerSkill = ht.Skill; d.Killer = SourceName(e, ht.Attacker); }
            }
    }

    /// <summary>A member died: the record's death log gets when, the boss's HP then, the killer and skill, and the last hits taken (the killing blow can still follow).</summary>
    void Died(Encounter e, Fighter m, UnitDied ud)
    {
        var d = new DeathRecord
        {
            At = Math.Round((ud.At - e.Start).TotalSeconds, 2), BossHpLeft = _cur?.BossHpLeft, KillerId = ud.KillerId, KillerSkill = ud.KillerSkill,
            Killer = SourceName(e, ud.KillerId),
        };
        if (_recentTaken.TryGetValue(m.Id, out var q))
            foreach (var h in q.Where(h => h.At <= ud.At && (ud.At - h.At).TotalSeconds <= 30))
                d.Last.Add(new TakenHit { At = Math.Round((h.At - e.Start).TotalSeconds, 2), Skill = h.Skill, Source = SourceName(e, h.Attacker), Amount = h.Amount });
        (m.DeathLog ??= new()).Add(d);
        var pending = _pendingDeaths.FirstOrDefault(p => p.Target == m.Id && p.At == ud.At);
        if (pending.Recaps == null) _pendingDeaths.Add((m.Id, ud.At, new() { (e, d) }));
        else pending.Recaps.Add((e, d));
    }

    /// <summary>An effect on or off a member: off, the time it was on (within each record) goes into the member's uptime.</summary>
    void Effect(EffectChanged ec)
    {
        if (ec.On) { _effectsOn[(ec.Target, ec.Slot)] = (ec.Effect, ec.At); return; }
        double secs = ec.Ms > 0 ? ec.Ms / 1000.0 : _effectsOn.TryGetValue((ec.Target, ec.Slot), out var on) ? (ec.At - on.At).TotalSeconds : 0;
        _effectsOn.Remove((ec.Target, ec.Slot));
        if (secs <= 0) return;
        var from = ec.At.AddSeconds(-secs);
        foreach (var e in Records()) if (Member(e, ec.Target) is { } m) AddUptime(e, m, ec.Effect, from, ec.At);
    }

    static void AddUptime(Encounter e, Fighter m, int effect, DateTime from, DateTime to)
    {
        double overlap = (to - (from > e.Start ? from : e.Start)).TotalSeconds;
        if (overlap <= 0) return;
        var list = m.Buffs ??= new();
        var b = list.FirstOrDefault(x => x.Id == effect);
        if (b == null) list.Add(b = new BuffUptime { Id = effect, Skill = Abilities.SkillOfEffect(effect) });
        b.Seconds = Math.Round(b.Seconds + overlap, 2);
        double present = e.PresentSeconds(m, to);
        b.Share = present > 0 ? Math.Clamp(b.Seconds / present, 0, 1) : 0;
    }

    /// <summary>
    /// A record ending: the effects still on count up to its end, each uptime's share is of the member's time in the group,
    /// and the boss's HP timeline runs to the end (none past it).
    /// </summary>
    void Settle(Encounter e)
    {
        var end = e.End ?? e.LastAt;
        foreach (var ((target, _), (effect, at)) in _effectsOn)
            if (Member(e, target) is { } m && at < end) AddUptime(e, m, effect, at, end);
        foreach (var m in e.Members)
            if (m.Buffs != null)
                foreach (var b in m.Buffs) { double p = e.PresentSeconds(m, end); b.Share = p > 0 ? Math.Clamp(b.Seconds / p, 0, 1) : 0; }
        if (e.BossHpPerSecond is { Count: > 0 } hps)
        {
            int last = e.SecondOf(end);
            if (hps.Count > last + 1) hps.RemoveRange(last + 1, hps.Count - last - 1);
            while (hps.Count <= last) hps.Add(hps[^1]);
            if (e.Outcome == "kill") hps[^1] = 0;
        }
    }

    /// <summary>A hit a record already has, found to be on the boss after all (it came just ahead of the pull): counted as boss damage too.</summary>
    static void Promote(Encounter e, DamageDealt dd)
    {
        e.BossDamage += dd.Amount;
        if (dd.SourceId == 0) { e.UncreditedBoss += dd.Amount; return; }
        if (e.Roster.FirstOrDefault(f => f.Id == dd.SourceId && !f.Other) is not { } m) return;
        m.BossDamage += dd.Amount;
        m.BossPerSecond = Fighter.AddAt(m.BossPerSecond, e.SecondOf(dd.At), dd.Amount);
        if (m.Abilities.FirstOrDefault(a => a.Skill == dd.Skill) is { } a)
        {
            a.BossDamage += dd.Amount;
            if (dd.Hits > 0) a.BossHits += dd.Hits; else a.BossUncounted += dd.Amount;
        }
    }

    /// <summary>
    /// The row a hit goes to: the member by id; else a row made from the hit (its name when the decoder knows it, you
    /// marked), so the damage rows are the group until a group list comes.
    /// </summary>
    static Fighter SourceOf(Encounter e, DamageDealt dd)
    {
        var f = e.Roster.FirstOrDefault(r => r.Id == dd.SourceId && !r.Other);
        // you under a new id (the game gives you one after a reconnect): still your row
        if (f == null && dd.SourceIsYou && e.Roster.FirstOrDefault(r => r.You && !r.Other) is { } you) { you.Id = dd.SourceId; f = you; }
        bool fresh = f == null;
        if (f == null) e.Roster.Add(f = new Fighter { Id = dd.SourceId, FromDamage = true });
        if (f.Name.Length == 0 && dd.SourceName.Trim().Length > 0) { f.Name = dd.SourceName.Trim(); fresh = true; }
        f.You |= dd.SourceIsYou;
        if (fresh && f.Class.Length == 0) ClassOf?.Invoke(f);
        return f;
    }

    /// <summary>
    /// The group onto a record's roster (<paramref name="at"/>: when the list came; null as the record opens): known members
    /// updated; a new one marked late when the list comes over <see cref="LateSeconds"/> after the start and a group list was
    /// known before (the first list after damage rows only names who was there all along), damage or not; missing ones gone.
    /// </summary>
    static void MergeGroup(Encounter e, List<GroupMember> group, DateTime? at)
    {
        bool after = at is { } a0 && (a0 - e.Start).TotalSeconds > LateSeconds;
        bool hadList = e.Roster.Any(r => !r.Other && !r.FromDamage);
        foreach (var m in group)
        {
            var name = m.Name.Trim();
            // by the game's id; else by name, and you as you (a reconnect gives new ids): one row per player, never two
            var f = e.Roster.FirstOrDefault(r => (m.Id != 0 && r.Id == m.Id) || (m.Id == 0 && r.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
                ?? (name.Length > 0 ? e.Roster.FirstOrDefault(r => !r.Other && r.Name.Equals(name, StringComparison.OrdinalIgnoreCase)) : null)
                ?? (m.You ? e.Roster.FirstOrDefault(r => r.You && !r.Other) : null);
            if (f != null && m.Id != 0 && f.Id != m.Id && !f.Other) f.Id = m.Id;
            if (f == null)
            {
                f = Fighter.From(m);
                if (after && hadList) { f.Late = true; f.JoinedAt = at; }
                e.Roster.Add(f);
                continue;
            }
            if (f.Gone) { f.Gone = false; f.LeftAt = null; }
            f.FromDamage = false;
            if (m.Name.Trim().Length > 0) f.Name = m.Name.Trim();
            if (m.Class.Trim().Length > 0) f.SetClass(m.Class);
            if (m.CombatPower > 0) f.CombatPower = m.CombatPower;
            if (m.ItemLevel > 0) f.ItemLevel = m.ItemLevel;
            if (m.Server?.Trim() is { Length: > 0 } server) f.Server = server;
            f.You |= m.You;
        }
        if (at != null)
        {
            foreach (var f in e.Roster)
                if (!f.Other && !f.FromDamage && !f.You && !f.Gone && !group.Any(m => (m.Id != 0 && m.Id == f.Id) || (m.Id == 0 && m.Name.Trim().Equals(f.Name, StringComparison.OrdinalIgnoreCase))))
                { f.Gone = true; f.LeftAt = at; }
            // gone at the first lists of the record, having done nothing: the group of the place before, never here
            if (!after) e.Roster.RemoveAll(f => f.Gone && !f.You && f.Damage == 0 && f.Deaths == 0 && f.Abilities.Count == 0 && f.ActiveSeconds == 0);
        }
    }

    static void MarkPartial(Encounter e, string why)
    {
        e.Partial = true;
        if (!e.PartialWhy.Contains(why)) e.PartialWhy.Add(why);
    }

    // ---- ends ----

    /// <summary>
    /// When a pull that ends at <paramref name="at"/> really ended: a kill at the death; a wipe at its last damage or HP fall,
    /// no later; leaving (<paramref name="yours"/>: you left) at your last damage, else the group's; any other end with damage
    /// at the last damage. A pull with no damage known ends at <paramref name="at"/>.
    /// </summary>
    static DateTime EndOf(Encounter e, BossOutcome outcome, DateTime at, bool yours)
    {
        if (outcome == BossOutcome.Kill || !e.HasDamage) return at;
        DateTime? last = e.LastDamageAt is { } d ? (e.LastHpFallAt is { } h ? Max(d, h) : d) : e.LastHpFallAt;
        if (yours && outcome == BossOutcome.Left && e.You?.LastHit is { } mine) last = mine;
        return last is { } l && l < at ? l : at;
    }

    void ClosePull(Encounter e, BossOutcome outcome, DateTime at, List<Encounter> done, string? why, bool yours = false)
    {
        if (!ReferenceEquals(e, _cur)) return;
        _cur = null;
        var end = EndOf(e, outcome, at, yours);
        if (end < at) Log($"{e.Boss}: ends at {(yours && outcome == BossOutcome.Left && e.You?.LastHit != null ? "your" : "the")} last damage, {(at - end).TotalSeconds:0.0} s before its end was known");
        e.End = Max(end, e.Start);
        e.LastAt = Max(e.LastAt, e.End.Value);
        e.Outcome = Encounter.OutcomeName(outcome);
        Settle(e);
        if (!e.Wave) { _alwaysEnded = Copy(e); FightHealth(e); }
        _endedPull = (e.End.Value, e.Wave || _alwaysEnded == null ? new List<Encounter> { e } : new List<Encounter> { e, _alwaysEnded });
        if (outcome == BossOutcome.Kill)
        {
            _downHere.Add(e.Boss); if (e.BossId != 0) _downIds.Add(e.BossId);
            // bosses fought together: each of them is down (the list's final one among them clears the instance)
            foreach (var (id, n) in _pullBossNames) { _downHere.Add(n); _downIds.Add(id); }
            foreach (var b in _book.Together(e.Instance, _pullBossNames.Values.FirstOrDefault() ?? "")) _downHere.Add(b.Name);
        }
        var secs = e.Duration(e.End.Value).TotalSeconds;
        bool worth = outcome == BossOutcome.Kill || secs >= MinSeconds;
        Log($"{e.Boss}: {e.OutcomeWord.ToLowerInvariant()} after {Clock(e.Duration(e.End.Value))}{(why != null ? $" ({why})" : "")}{(e.Partial ? $", partial: {string.Join(", ", e.PartialWhy)}" : "")}{(worth ? "" : ", too short to keep")}");
        // the run and the fight around it heard of it too; a kill is the fight's (its own pause decides its end)
        if (_fight != null && _fight.Boss.Length > 0) _fight.BossResult = e.Outcome;
        foreach (var r in new[] { _run, _fight })
        {
            if (r == null) continue;
            r.LastAt = Max(r.LastAt, e.End.Value);
            if (outcome == BossOutcome.Kill && !r.Kills.Any(k => k.Boss && k.At == e.End.Value)) r.Kills.Add(new KillNear { At = e.End.Value, Name = e.Boss, Boss = true });
        }
        if (!worth) return;
        if (_run != null) _run.Splits.Add(Split.From(e, e.End.Value, live: false));
        // a world boss's pull is kept in every mode: out of a dungeon no run holds it, and the open world's fight is never kept
        if ((_mode == ParserMode.Bosses || e.WorldBoss) && !e.Wave) done.Add(Copy(e));
    }

    /// <summary>A run's end: cleared when its final boss died, else left.</summary>
    static string RunEnd(Encounter run) => run.Splits.Any(s => s.Final && s.Outcome == "kill") ? "cleared" : "left";

    void CloseRun(string outcome, DateTime at, List<Encounter> done, string? why)
    {
        if (_run is not { } r) return;
        _run = null;
        r.End = Max(at, r.Start);
        r.LastAt = Max(r.LastAt, r.End.Value);
        r.Outcome = outcome;
        Settle(r);
        r.Boss = "";
        var secs = r.Duration(r.End.Value).TotalSeconds;
        bool worth = r.Splits.Count > 0 || secs >= RunMinSeconds;
        Log($"run: {r.Instance} {r.OutcomeWord.ToLowerInvariant()} after {Clock(r.Duration(r.End.Value))}, {r.Splits.Count} bosses{(why != null ? $" ({why})" : "")}{(worth ? "" : ", too short to keep")}");
        if (worth) done.Add(Copy(r));
    }

    void CloseFight(DateTime at, List<Encounter> done, string? why)
    {
        if (_fight is not { } f) return;
        _fight = null;
        f.End = Max(at, f.Start);
        f.LastAt = Max(f.LastAt, f.End.Value);
        // a fight around a boss ends as its pull did (a wipe is a wipe, whatever the trash before it); else a death, your kills, or nothing
        f.Outcome = f.Boss.Length > 0 && f.BossResult.Length > 0 ? f.BossResult : f.YourDeaths > 0 ? "died" : f.YourKills > 0 || f.Kills.Any(k => k.Boss) ? "kill" : "ended";
        Settle(f);
        // open world and the Abyss: shown while it lasted, never kept, and the log says no more than that it ended
        if (f.OpenWorld) { Log("fight: an open-world fight ended (shown live only, not kept)"); return; }
        bool worth = f.YourKills > 0 || f.YourDeaths > 0 || f.Boss.Length > 0 || (f.HasDamage && f.Duration(f.End.Value).TotalSeconds >= MinSeconds);
        Log($"fight: {f.TitleWord} in {f.Instance} {f.OutcomeWord.ToLowerInvariant()} after {Clock(f.Duration(f.End.Value))}{(why != null ? $" ({why})" : "")}{(worth ? "" : ", nothing to keep")}");
        if (worth) done.Add(Copy(f));
    }

    /// <summary>
    /// The window's tick: a pull with nothing heard for <see cref="StaleMinutes"/> is given up, a run after
    /// <see cref="RunStaleMinutes"/>, and a fight ends <see cref="PauseSeconds"/> after its last kill, hit or pull (never while a pull is under way).
    /// </summary>
    public void Tick(DateTime now)
    {
        List<Encounter> done = new();
        lock (_gate)
        {
            int was = done.Count;
            // damage, then none and no HP fall for a minute, and no end said: the group wiped (or walked off); it ends at its last damage
            if (_cur is { HasDamage: true } q && (q.LastDamageAt ?? q.LastAt) is { } lastHit
                && (now - (q.LastHpFallAt is { } fall && fall > lastHit ? fall : lastHit)).TotalSeconds >= WipeQuietSeconds)
            {
                _inferred = (q.Boss, q.BossId, _instanceId);
                // a world boss going quiet is you walking off (the crowd fights on, out of sight), never a wipe
                ClosePull(q, q.WorldBoss ? BossOutcome.Left : BossOutcome.Wipe, now, done, $"no damage for {WipeQuietSeconds:0} s and no end said");
                _version++;
            }
            if (_cur is { } c && (now - c.LastAt).TotalMinutes >= StaleMinutes)
            {
                MarkPartial(c, $"nothing heard of it for {StaleMinutes:0} minutes");
                ClosePull(c, BossOutcome.Left, c.LastAt, done, null);
                _version++;
            }
            if (_run is { } r && (now - r.LastAt).TotalMinutes >= RunStaleMinutes)
            {
                // after its last boss died nothing is missing: quiet then is the run being over
                if (RunEnd(r) != "cleared") MarkPartial(r, $"nothing heard of it for {RunStaleMinutes:0} minutes");
                CloseRun(RunEnd(r), r.LastAt, done, null);
                _version++;
            }
            if (_fight is { } f && _cur == null && (now - f.LastAt).TotalSeconds >= PauseSeconds)
            {
                CloseFight(f.LastAt, done, null);
                _version++;
            }
            // Always on: a few seconds with nobody of yours fighting, and the next hit starts the meter from 0; in a dungeon too
            // since 0.10.132 (Steven, 2026-11-16: "in dungeons it should show the DPS meter but restart on the fight (like open
            // world)"): each trash pack its own meter, a boss pull still its own
            if (_always is { } al && (now - _alwaysActiveAt).TotalSeconds >= AlwaysIdleSeconds)
            {
                // held at the group's last hit dealt (Steven, 2026-11-15: "the dps meter can continue to drop for a few seconds
                // before pausing - if nothing else is hit go back to the dps before it started dropping"): the live figure falls
                // through the quiet seconds, then the fight's own DPS as it stood at that hit
                var ourLastHit = al.Members.Select(f => f.LastHit).Where(h => h != null).Max() ?? _alwaysActiveAt;
                var ourFirstHit = al.Members.Select(f => f.FirstHit).Where(h => h != null).Min() ?? ourLastHit;
                // a fight of one hit (a mob killed at once) joins the fight before it out here (Steven, 2026-11-15: "yes, merge
                // them"): held alone it read the hit over a second, the opening spike, never the pace
                if ((ourLastHit - ourFirstHit).TotalSeconds < Encounter.MinDpsSeconds && _alwaysEnded is { Mode: "fight", End: { } } prev
                    && prev.Kind == al.Kind && prev.Instance == al.Instance)
                {
                    _always = null;
                    al.End = Max(Min(ourLastHit, al.LastAt), al.Start);
                    Fold(prev, al);
                    Log($"always on: a fight of one hit joined the one before it ({AlwaysIdleSeconds:0} seconds without fighting)");
                }
                else EndAlways(ourLastHit, $"{AlwaysIdleSeconds:0} seconds without fighting");
                _version++;
            }
            // the open-world fight of Bosses only and Full run: gone after its pause, nothing kept
            if (_farm is { } fm && (now - fm.LastAt).TotalSeconds >= PauseSeconds) { _farm = null; _version++; }
        }
        if (done.Count > 0) Finish(done, null);
    }

    /// <summary>
    /// Where the screen says the player is (the minimap's run, InstanceLog), used only while the game's messages haven't
    /// said: a build without the decoder, or the first seconds after the tracker starts.
    /// </summary>
    public void ScreenPlace(string? kind, string? instance)
    {
        lock (_gate)
        {
            if (_instanceFromGame) return;
            if (instance != null && BossBook.IsModeWord(instance)) return;   // "Easy" is a mode, never a place
            var k = Encounter.KindOf(kind);
            if (k == ContentKind.Other && instance != null) k = _book.KindOf(instance);
            // the run log files the Ascension Trial's instances as expeditions (data/instances.txt): the Parser's list says what they are
            if (instance != null && _book.KindOf(instance) == ContentKind.AscensionTrial) k = ContentKind.AscensionTrial;
            // the game marked this place a Transcendence (its mode "G"): the run log's minimap says only its expedition
            if (_transcend && k == ContentKind.Expedition) k = ContentKind.Transcendence;
            var name = instance?.Trim() ?? "";
            if (k == _kind && name == _instance) return;
            // the run log learned the expedition under way is a Transcendence (its stage line): the records in it too
            if (k == ContentKind.Transcendence && _kind == ContentKind.Expedition && BossBook.Strip(name).Equals(BossBook.Strip(_instance), StringComparison.OrdinalIgnoreCase))
            {
                Transcend(DateTime.UtcNow, 0, "the run log's");
                return;
            }
            var newKind = instance == null ? ContentKind.Other : k;
            // only into one of the three: a minimap misread mid-dungeon (a name a letter off, a frame with none) never starts the meter again
            if (!Kept(_kind) && Kept(newKind)) EndAlways(DateTime.UtcNow, $"entered {name}");
            _kind = newKind; _instance = name;
            if (Kept(_kind)) _farm = null;   // the minimap says an instance: whatever was counted as farming is over
            if (_mode == ParserMode.Run && _run == null && Kept(_kind)) OpenRun(DateTime.UtcNow, midway: false);
            _version++;
        }
    }

    /// <summary>
    /// A map event of the place the player is in, by the game's own code (FierceHornCave_Easy_Mapevent, from the event
    /// server). When the game's messages entered an instance with no name (Ferocious Horn Den on Easy since 2026-11-16: a map
    /// the decoder doesn't list), the code names it from the boss list's codes (data/bosses.txt) and the screen's place isn't
    /// waited for; its mode word (Easy) is the mode when none came, never the name. Any other code (an open-world event) does nothing.
    /// </summary>
    public void MapEvent(string code, DateTime at, bool closed = false, bool over = false)
    {
        List<Encounter> done = new();
        lock (_gate)
        {
            // an Ascension Trial: the game enters it with no word of its own, so its map events place it, end its boss and leave it
            if (_book.FromCode(code) is { } trial && _book.KindOf(trial.Instance) == ContentKind.AscensionTrial && (_instanceId == 0 || _trialByCode))
            {
                TrialEvent(trial.Instance, code, at, closed, over, done);
                _version++;
                goto ended;
            }
            if (over || _instanceId == 0 || at < _enteredAt) return;
            if (_book.FromCode(code) is not { } named) return;
            var (transcend, stage) = _book.GradeOf(code);
            if (_instanceFromGame)
            {
                // the place is named already: its own map events still say a Transcendence, and its boss room opening again
                // after its last boss died is a new run in the same copy (a Transcendence's next try: the room closed at
                // 16:56:09 and opened at 16:56:12 on 2026-11-17, no entry between)
                if (Kept(_kind) && BossBook.Strip(_instance).Equals(named.Instance, StringComparison.OrdinalIgnoreCase))
                {
                    if (transcend && (!_transcend || (stage > 0 && stage != _stage))) Transcend(at, stage, $"its map event ({code.Trim()})");
                    if (closed) { if (FinalDown()) _roomClosedAt = at; }
                    else if (_roomClosedAt is { } rc && rc <= at && FinalDown()) RunAgain(done, at, "its room opened again after its last boss died");
                    _version++;
                }
            }
            else if (!closed && Kept(_book.KindOf(named.Instance)))
                NameByCode(named.Instance, named.Mode, transcend, stage, code, at);
        }
        ended:
        if (done.Count > 0) Finish(done, at);
    }

    // ---- the Ascension Trial (1.0.24) ----
    // Steven, 2026-11-19: "add for the parser instanced boss fights like the ones at end of ascension trial". In the trial
    // (Nightmare Altar, six runs recorded on 2026-11-19) the game says nothing of entering it, of its boss or of the boss's HP;
    // only its map events say when a run is under way, over and closed, and its boss is the one enemy that takes far more
    // damage than any other and never dies before the run is over (Divided Aponos: 1.75M each run, the rooms' enemies 0.2M
    // to 0.3M each). So the boss is worked out from the damage: an enemy taking TrialBossTimes as much as the most any enemy
    // killed this run took (and TrialBossMin at least) is the boss, and its pull starts at its first hit; at the run's end,
    // a boss hit until then died (a kill at its last hit). A place whose game messages name its boss never does this.

    /// <summary>The trial was placed by its map event, not by the game's entry (its closing leaves it).</summary>
    bool _trialByCode;
    /// <summary>The game named a boss in this place (BossEngaged): its pulls are the game's own.</summary>
    bool _bossFromGame;
    /// <summary>The tier the place's map code gave (Encounter.ModeCode), null when none.</summary>
    string? _modeCode;
    /// <summary>A trial's runs get ids of their own (below 0): each run of it is another copy.</summary>
    long _codeEntries;

    /// <summary>An enemy hit in this trial run: the damage it took from anyone, when, dead or not, and its rows (kept for the boss's pull).</summary>
    sealed class TrialTarget
    {
        public long Damage;
        public DateTime First, Last;
        public bool Dead;
        public List<DamageDealt> Rows { get; } = new();
        public List<HitDealt> Hits { get; } = new();
    }
    readonly Dictionary<long, TrialTarget> _trialTaken = new();
    /// <summary>The most damage any enemy killed in this trial run took.</summary>
    long _trialDeadMax;
    /// <summary>An enemy taking this many times the most any killed enemy took is the trial's boss, and at least <see cref="TrialBossMin"/>.</summary>
    public const double TrialBossTimes = 3;
    public const long TrialBossMin = 600_000;
    /// <summary>At the run's end, its boss: hit within this long before it (a kill), else a wipe.</summary>
    public const double TrialEndSeconds = 5;
    /// <summary>At the run's end with no pull open, the boss is the enemy hit up to it that took this many times what any killed one took.</summary>
    const double TrialEndTimes = 2;
    /// <summary>Rows kept per enemy of a trial run (a boss's pull until it shows fits).</summary>
    const int TrialRowsKept = 6000;
    /// <summary>The pull's bosses by the game's id (each boss's own name), the ones of bosses fought together that died, and their HP.</summary>
    readonly Dictionary<long, string> _pullBossNames = new();
    readonly HashSet<long> _jointDown = new();
    readonly Dictionary<long, (long Hp, long Max)> _jointHp = new();
    /// <summary>How many bosses the pull's are fought together with it included (0: one boss alone).</summary>
    int _pullTogether;

    bool TrialInfers => _kind == ContentKind.AscensionTrial && !_bossFromGame;

    void TrialReset() { _trialTaken.Clear(); _trialDeadMax = 0; }

    /// <summary>The player left the instance (the game said so, or a trial's run closed).</summary>
    void LeftPlace(DateTime at, List<Encounter> done, string why = "left the instance")
    {
        _reentry = null; _gapBefore = null;
        if (Kept(_kind)) EndAlways(at, why);
        if (_cur != null) ClosePull(_cur, BossOutcome.Left, at, done, why, yours: true);
        _preHits.Clear(); _inferred = null; _recentTaken.Clear(); _pendingDeaths.Clear(); _effectsOn.Clear(); _castsLanded.Clear();
        if (_run != null) CloseRun(RunEnd(_run), at, done, why);
        _kind = ContentKind.Other; _instance = ""; _instanceId = 0; _gameMode = ""; _modeCode = null; _transcend = false; _stage = 0; _roomClosedAt = null;
        if (_trialByCode) _instanceFromGame = false;
        _trialByCode = false; _bossFromGame = false; TrialReset();
    }

    /// <summary>A trial's map event: under way places it (a new run of it), over ends its boss's pull, closed leaves it.</summary>
    void TrialEvent(string instance, string code, DateTime at, bool closed, bool over, List<Encounter> done)
    {
        bool here = _trialByCode && Kept(_kind) && BossBook.Strip(_instance).Equals(instance, StringComparison.OrdinalIgnoreCase);
        if (over) { if (here) TrialOver(at, done); return; }
        if (closed) { if (here) { Log($"{_instance}: the run is over (its map event)"); LeftPlace(at, done, "the trial's run closed"); } return; }
        if (here) return;   // the same run's other map event
        if (_trialByCode) LeftPlace(at, done, "another trial run");
        _enteredAt = at;
        _instanceId = --_codeEntries;
        _bossNames.Clear(); _downHere.Clear(); _downIds.Clear(); _gameMode = ""; _transcend = false; _stage = 0; _roomClosedAt = null;
        _bossFromGame = false; TrialReset();
        _trialByCode = true;
        _modeCode = _book.TierOf(code);
        NameByCode(instance, "", false, 0, code, at);
        foreach (var e in Records()) if (e.ModeKind != ParserMode.Fights || Kept(_kind)) e.ModeCode = _modeCode;
    }

    /// <summary>A hit in a trial whose boss the game doesn't name: counted per enemy, and the boss's pull opened once it shows.</summary>
    void TrialHit(DamageDealt dd, List<Encounter> done)
    {
        if (!TrialInfers || dd.TargetId == 0 || dd.Amount <= 0) return;
        if (_cur != null && _cur.IsBoss(dd.TargetId)) return;
        if (!_trialTaken.TryGetValue(dd.TargetId, out var t)) _trialTaken[dd.TargetId] = t = new TrialTarget { First = dd.At };
        if (t.Dead) return;
        t.Damage += dd.Amount;
        t.Last = dd.At;
        bool boss = t.Damage >= Math.Max(TrialBossMin, TrialBossTimes * _trialDeadMax);
        // a second boss fought together with the pull's (Sanctum of Loathing's two): it joins the pull
        if (boss && _cur != null && _pullTogether > _pullBossNames.Count && _pullBossNames.Count > 0
            && _book.Together(_instance, _pullBossNames.Values.First()).FirstOrDefault(b => !_pullBossNames.ContainsValue(b.Name)) is { } next)
        {
            _trialTaken.Remove(dd.TargetId);
            Join(_cur, dd.TargetId, next.Name, dd.At, t);
            return;
        }
        if (boss && _cur == null) { TrialBoss(dd.TargetId, t, done, "it took far more damage than any other enemy"); return; }
        if (t.Rows.Count < TrialRowsKept) t.Rows.Add(dd);
    }

    /// <summary>The trial's boss found: its pull opens at its first hit, with every hit it took so far.</summary>
    void TrialBoss(long id, TrialTarget t, List<Encounter> done, string why)
    {
        _trialTaken.Remove(id);
        // all its hits so far are the pull's (not only the last seconds a pull's start takes)
        if (_preHits.Any(h => h.TargetId == id))
        {
            var keep = _preHits.Where(h => h.TargetId != id).ToList();
            _preHits.Clear();
            foreach (var h in keep) _preHits.Enqueue(h);
        }
        Engage(new BossEngaged(t.First, "", id), done);
        if (_cur is not { } c || c.BossId != id) return;
        Log($"{c.Boss}: the trial's boss ({why}), from its first hit");
        foreach (var h in t.Rows)
        {
            Credit(c, h, toBoss: true);
            foreach (var r in new[] { _run, _fight }) if (r != null) Promote(r, h);
        }
        ReplayHits(c, t.Hits);
        c.LastAt = Max(c.LastAt, t.Last);
    }

    /// <summary>A boss's hit rows from before its pull opened, into the pull's abilities (each cast once).</summary>
    void ReplayHits(Encounter c, List<HitDealt> hits)
    {
        var casts = new HashSet<(long, int, int)>();
        long seq = 0;
        foreach (var hd in hits)
            if (Member(c, hd.Source) is { } m)
            {
                var a = m.Ability(hd.Skill);
                StreamHit(a, hd);
                if (!hd.NoRow && !hd.Tick && hd.Skill > 0 && casts.Add((hd.Actor, hd.Cast, hd.Skill))) a.Casts = (a.Casts ?? 0) + 1;
                BigHitOf(c, m, hd, ref seq);
            }
    }

    /// <summary>
    /// The trial's run is over (cleared, or failed): a boss hit until then died, its pull a kill at its last hit; one not hit
    /// for longer is a wipe. With no pull open, the boss is the enemy hit up to the end that took twice what any killed one took.
    /// </summary>
    void TrialOver(DateTime at, List<Encounter> done)
    {
        if (!TrialInfers) return;
        if (_cur == null)
        {
            var last = _trialTaken.Where(kv => !kv.Value.Dead && (at - kv.Value.Last).TotalSeconds <= TrialEndSeconds && kv.Value.Damage >= TrialEndTimes * _trialDeadMax)
                                  .OrderByDescending(kv => kv.Value.Damage).Select(kv => (Id: kv.Key, T: kv.Value)).FirstOrDefault();
            if (last.T != null) TrialBoss(last.Id, last.T, done, "the enemy hit up to the run's end");
        }
        if (_cur is { } c)
        {
            var hit = c.LastDamageAt ?? c.LastAt;
            if ((at - hit).TotalSeconds <= TrialEndSeconds)
            {
                foreach (var id in _pullBossNames.Keys) _jointDown.Add(id);
                c.Kills.Add(new KillNear { At = hit, Name = c.Boss, Boss = true });
                ClosePull(c, BossOutcome.Kill, Max(hit, c.Start), done, "the trial's run was cleared");
            }
            else ClosePull(c, BossOutcome.Wipe, at, done, "the trial's run ended with the boss up");
        }
        TrialReset();
    }

    /// <summary>Another of the bosses fought together joins the pull: its damage is the boss's from now, its hits so far too.</summary>
    void Join(Encounter e, long id, string name, DateTime at, TrialTarget? t = null)
    {
        (e.WithIds ??= new()).Add(id);
        _pullBossNames[id] = name;
        e.LastAt = Max(e.LastAt, at);
        var pre = t?.Rows ?? _preHits.Where(h => h.TargetId == id && h.At >= at.AddSeconds(-PreSeconds)).ToList();
        foreach (var h in pre)
        {
            Credit(e, h, toBoss: true);
            foreach (var r in new[] { _run, _fight }) if (r != null) Promote(r, h);
        }
        if (t != null) ReplayHits(e, t.Hits);
        if (pre.Count > 0 && pre.Min(h => h.At) is var first && first < e.Start) e.MoveStart(first);
        Log($"{name} joins the pull: {e.Boss}, fought together");
    }

    /// <summary>One of the bosses fought together died: true when it was the last of them (the pull is a kill then).</summary>
    bool JointDown(long id, DateTime at)
    {
        _jointDown.Add(id);
        bool all = _pullBossNames.Count >= Math.Max(2, _pullTogether) && _pullBossNames.Keys.All(_jointDown.Contains);
        if (!all) Log($"{_pullBossNames.GetValueOrDefault(id, "a boss")} died at {at:HH:mm:ss}; {_cur?.Boss} goes on until the last of them");
        return all;
    }

    /// <summary>A boss's HP: of bosses fought together, all of theirs added up (each as last heard).</summary>
    BossHealth JointHealth(BossHealth bh)
    {
        if (_cur == null || bh.BossId == 0) return bh;
        _jointHp[bh.BossId] = (bh.Hp, bh.Max);
        if (_cur.WithIds is not { Count: > 0 }) return bh;
        return bh with { Hp = _jointHp.Values.Sum(v => Math.Max(0, v.Hp)), Max = _jointHp.Values.Sum(v => v.Max) };
    }

    /// <summary>The instance the game entered with no name, named by its map event's code: its kind (a Transcendence by its "G"), and its mode or stage.</summary>
    void NameByCode(string instance, string mode, bool transcend, int stage, string code, DateTime at)
    {
        var kind = _book.KindOf(instance);
        if (transcend) { _transcend = true; if (stage > 0) _stage = stage; }
        if (_transcend && kind == ContentKind.Expedition) kind = ContentKind.Transcendence;
        if (!Kept(_kind)) EndAlways(at, $"entered {instance}");
        bool renamed = _kind != kind || !BossBook.Strip(_instance).Equals(instance, StringComparison.OrdinalIgnoreCase);
        _kind = kind; _instance = instance; _instanceFromGame = true;
        _farm = null;
        // the records the screen's place opened under another name (a minimap misread) take the game's
        if (renamed) foreach (var e in Records()) if (!e.OpenWorld) { e.Instance = _instance; e.Kind = Encounter.KindName(kind); }
        // a Transcendence's mode is its stage; an expedition's the code's word when no mode came
        string want = _transcend ? BossBook.StageMode(StageNow(at)) : _gameMode.Length == 0 ? mode : _gameMode;
        if (want != _gameMode || _transcend)
        {
            _gameMode = want;
            foreach (var e in Records()) if (e.ModeKind != ParserMode.Fights || Kept(_kind)) e.GameMode = _gameMode;
        }
        if (_mode == ParserMode.Run && _run == null) OpenRun(at, midway: false);
        Log($"the instance by the game's map event ({code.Trim()}): {_instance}{(_transcend ? " (Transcendence)" : "")}{(_gameMode.Length > 0 ? $", {_gameMode}" : "")}");
        _version++;
    }

    /// <summary>
    /// The records that were under way when the tracker closed (encounter-live.json): within <see cref="ResumeMinutes"/> a
    /// pull or a run carries on, partial; a fight past its pause, a record of another mode, or anything older is kept as it
    /// was (ended at the last thing heard).
    /// </summary>
    public void Resume(IEnumerable<Encounter> saved, DateTime now)
    {
        List<Encounter> done = new();
        lock (_gate)
        {
            foreach (var s0 in saved)
            {
                if (s0.End != null || s0.OpenWorld) continue;   // an open-world fight is never kept, nor carried over
                var s = Copy(s0);
                var m = ModeOf(s.Mode);
                bool fresh = (now - s.LastAt).TotalMinutes <= ResumeMinutes && (m != ParserMode.Fights || (now - s.LastAt).TotalSeconds < PauseSeconds);
                bool carry = fresh && (m == ParserMode.Bosses || m == _mode);
                if (m == ParserMode.Bosses && _cur == null) _cur = s;
                else if (m == ParserMode.Run && _run == null) _run = s;
                else if (m == ParserMode.Fights && _fight == null) _fight = s;
                else continue;
                if (carry)
                {
                    MarkPartial(s, "the tracker restarted during it");
                    // a world boss's pull carries on where the player is, never a place of its own
                    if (m != ParserMode.Fights && !s.WorldBoss)
                    {
                        _kind = Encounter.KindOf(s.Kind); _instance = s.Instance; _instanceId = s.InstanceId;
                        if (_kind == ContentKind.Transcendence) { _transcend = true; _stage = BossBook.StageOfMode(s.GameMode); _gameMode = s.GameMode; }
                    }
                    Log($"{s.TitleWord}: carries on after the tracker restarted");
                    continue;
                }
                MarkPartial(s, "the tracker closed during it");
                switch (m)
                {
                    case ParserMode.Run: CloseRun(RunEnd(s), s.LastAt, done, null); break;
                    case ParserMode.Fights: CloseFight(s.LastAt, done, null); break;
                    default:
                        // a pull of the old session: kept as it was when Bosses only keeps pulls (ClosePull decides)
                        ClosePull(s, BossOutcome.Left, s.LastAt, done, null);
                        break;
                }
            }
            _alwaysEnded = null;   // a record of the last session is not what the Always on meter holds
            _version++;
        }
        Finish(done, null);
    }

    /// <summary>The single fight saved by 0.10.80's first builds (one record, a boss pull): as <see cref="Resume(IEnumerable{Encounter}, DateTime)"/>.</summary>
    public void Resume(Encounter saved, DateTime now) => Resume(new[] { saved }, now);

    static DateTime Max(DateTime a, DateTime b) => a > b ? a : b;
    static DateTime Min(DateTime a, DateTime b) => a < b ? a : b;

    public static string Clock(TimeSpan d) => d.TotalHours >= 1 ? $"{(int)d.TotalHours}:{d.Minutes:00}:{d.Seconds:00}" : $"{(int)d.TotalMinutes}:{d.Seconds:00}";

    /// <summary>
    /// A parser that writes nothing to reads.log: the Parser page's and the Options' sample (ParserDemo), whose made-up fights
    /// wrote "entered Fire Temple" and "a new pull" lines into the player's own log each time the sample was drawn (2026-11-19).
    /// </summary>
    public bool Quiet { get; init; }
    void Log(string text) { if (!Quiet) CrashLog.Note("parser: " + text); }
}
