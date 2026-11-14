using System.Text.Json;
using KinahLedger.Packets;

namespace KinahLedger.Parser;

/// <summary>What the Parser keeps (Steven, 2026-11-13: "allow users to select just boss only for parser or full run etc").</summary>
public enum ParserMode
{
    /// <summary>One record per boss pull in a Transcendence, an Expedition or a Sanctuary (the default).</summary>
    Bosses = 0,
    /// <summary>One record per instance run of those three, entering to leaving, trash included, each boss a split.</summary>
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
///   only inside a Transcendence, an Expedition or a Sanctuary. The decoder says a pull only for the bosses on its own
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

    public BossParser(BossBook? book = null) => _book = book ?? BossBook.Shared;

    /// <summary>A pull with no name: the fight is kept under this.</summary>
    public const string UnnamedBoss = "Unnamed boss";

    // where the player is
    ContentKind _kind;
    string _instance = "";
    long _instanceId;
    bool _instanceFromGame;
    string _zone = "";
    bool _abyss;
    List<GroupMember> _group = new();
    /// <summary>The boss pull under way (any mode), the run under way (Run), the fight under way (Fights).</summary>
    Encounter? _cur, _run, _fight;
    DateTime _lastEventAt = DateTime.MinValue;
    ParserMode _mode;

    /// <summary>Goes up with every change the window should draw.</summary>
    public int Version { get { lock (_gate) return _version; } }
    int _version;

    /// <summary>When the game last said anything about a fight (MinValue: never, as in a build without the decoder).</summary>
    public DateTime LastEventAt { get { lock (_gate) return _lastEventAt; } }

    /// <summary>Where the player is: the content kind (Other outside the three) and the instance's name.</summary>
    public (ContentKind Kind, string Instance) Where { get { lock (_gate) return (_kind, _instance); } }

    /// <summary>The zone the session reads (the open world, the Abyss), for Every fight's records.</summary>
    public string Zone { get { lock (_gate) return _zone; } }

    /// <summary>The group as the game last said it.</summary>
    public IReadOnlyList<GroupMember> Group { get { lock (_gate) return _group.ToList(); } }

    public ParserMode Mode { get { lock (_gate) return _mode; } }

    /// <summary>
    /// A copy of the record under way in the mode: the boss pull, the run (with the pull under way as its last split and
    /// its boss's HP), or the fight; null when there is none.
    /// </summary>
    public Encounter? Live
    {
        get
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
                            r.Splits.Add(Split.From(_cur, DateTime.UtcNow, live: true));
                            r.Boss = _cur.Boss; r.BossId = _cur.BossId; r.BossHp = _cur.BossHp; r.BossMaxHp = _cur.BossMaxHp;
                        }
                        else { r.Boss = ""; r.BossHp = 0; r.BossMaxHp = 0; }
                        return r;
                    case ParserMode.Fights: return _fight == null ? null : Copy(_fight);
                    default: return _cur == null ? null : Copy(_cur);
                }
            }
        }
    }

    /// <summary>Copies of every record under way (the pull, the run, the fight), for encounter-live.json.</summary>
    public List<Encounter> LiveAll { get { lock (_gate) return new[] { _cur, _run, _fight }.Where(e => e != null).Select(e => Copy(e!)).ToList(); } }

    public static Encounter Copy(Encounter e) => JsonSerializer.Deserialize<Encounter>(JsonSerializer.Serialize(e))!;

    static bool Kept(ContentKind k) => k is ContentKind.Transcendence or ContentKind.Expedition or ContentKind.Sanctuary;

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
            _mode = mode;
            if (mode == ParserMode.Run && Kept(_kind)) OpenRun(now, midway: true);
            Log($"mode: {Name(mode)}");
            _version++;
        }
        foreach (var e in done) Ended?.Invoke(e);
    }

    public static string Name(ParserMode m) => m switch { ParserMode.Run => "Full run", ParserMode.Fights => "Every fight", _ => "Bosses only" };

    /// <summary>What the mode counts, in one sentence (the Parser page's head, the overlay's menu).</summary>
    public static string Counts(ParserMode m) => m switch
    {
        ParserMode.Run => "Each Transcendence, Expedition or Sanctuary run, from entering to leaving, trash included, with every boss as a split.",
        ParserMode.Fights => $"Every fight anywhere, the open world and the Abyss too: from the first hit or kill to {PauseSeconds:0} seconds without one.",
        _ => "Each boss pull in Transcendence, Expeditions and Sanctuaries; trash and the open world never count.",
    };

    public static string ModeKey(ParserMode m) => m switch { ParserMode.Run => "run", ParserMode.Fights => "fight", _ => "boss" };
    public static ParserMode ModeOf(string? key) => key switch { "run" => ParserMode.Run, "fight" => ParserMode.Fights, _ => ParserMode.Bosses };

    // ---- the events ----

    public void Take(BossEvent ev)
    {
        List<Encounter> done = new();
        lock (_gate)
        {
            _lastEventAt = ev.At;
            TakeLocked(ev, done);
            _version++;
        }
        foreach (var e in done) Ended?.Invoke(e);
    }

    void TakeLocked(BossEvent ev, List<Encounter> done)
    {
        switch (ev)
        {
            case InstanceEntered ie:
            {
                var kind = ie.Kind != ContentKind.Other ? ie.Kind : _book.KindOf(ie.Name);
                bool SameCopy(Encounter e) => ie.InstanceId != 0 && e.InstanceId != 0 ? ie.InstanceId == e.InstanceId
                    : BossBook.Strip(ie.Name).Equals(BossBook.Strip(e.Instance), StringComparison.OrdinalIgnoreCase) && Kept(kind);
                if (_cur != null)
                {
                    if (SameCopy(_cur)) Rejoined(_cur, ie);
                    else ClosePull(_cur, BossOutcome.Left, _cur.LastAt, done, "left for another place");
                }
                if (_run != null)
                {
                    if (SameCopy(_run)) Rejoined(_run, ie);
                    else CloseRun(RunEnd(_run), _run.LastAt, done, "left for another place");
                }
                _kind = kind; _instance = ie.Name.Trim(); _instanceId = ie.InstanceId; _instanceFromGame = true;
                if (_mode == ParserMode.Run && _run == null && Kept(kind)) OpenRun(ie.At, midway: false);
                if (_fight != null && !Kept(kind)) { } // a fight carries on through a loading screen only by its own pause
                Log($"entered {(_instance.Length > 0 ? _instance : "a place")}{(Kept(kind) ? $" ({Encounter.KindName(kind)})" : "")}");
                break;
            }
            case InstanceLeft il:
                if (_cur != null) ClosePull(_cur, BossOutcome.Left, il.At, done, "left the instance");
                if (_run != null) CloseRun(RunEnd(_run), il.At, done, "left the instance");
                _kind = ContentKind.Other; _instance = ""; _instanceId = 0;
                break;
            case BossEngaged be:
                Engage(be, done);
                break;
            case BossEnded end:
                if (_cur != null && (end.BossId == 0 || end.BossId == _cur.BossId)) ClosePull(_cur, end.Outcome, end.At, done, null);
                break;
            case UnitDied ud:
            {
                bool bossDied = _cur != null && ((ud.Id != 0 && ud.Id == _cur.BossId) || (ud.Boss && ud.Name.Trim().Equals(_cur.Boss, StringComparison.OrdinalIgnoreCase) && (ud.Id == 0 || _cur.BossId == 0)));
                foreach (var e in Records())
                {
                    if (ReferenceEquals(e, _cur) && bossDied) continue;
                    e.LastAt = Max(e.LastAt, ud.At);
                    if (bossDied) e.Kills.Add(new KillNear { At = ud.At, Name = _cur!.Boss, Boss = true });
                    else if (e.Roster.FirstOrDefault(f => ud.Id != 0 && f.Id == ud.Id && !f.Other) is { } member) member.Deaths++;
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
                _group = gc.Members.Where(m => m.Name.Trim().Length > 0 || m.You).ToList();
                foreach (var e in Records()) MergeGroup(e, _group, late: true);
                break;
            case DamageDealt dd:
                if (dd.Amount <= 0) break;
                if (_mode == ParserMode.Fights && _fight == null) OpenFight(dd.At);
                foreach (var e in Records())
                {
                    e.LastAt = Max(e.LastAt, dd.At);
                    e.HasDamage = true;
                    if (dd.TargetId != 0 && _cur != null && dd.TargetId == _cur.BossId) e.BossDamage += dd.Amount;
                    SourceOf(e, dd).Damage += dd.Amount;
                }
                break;
            case BossHealth bh:
                if (_cur == null || (bh.BossId != 0 && _cur.BossId != 0 && bh.BossId != _cur.BossId)) break;
                _cur.LastAt = Max(_cur.LastAt, bh.At);
                _cur.BossHp = Math.Max(0, bh.Hp);
                if (bh.Max > 0) _cur.BossMaxHp = bh.Max;
                if (_fight != null) { _fight.LastAt = Max(_fight.LastAt, bh.At); _fight.BossHp = _cur.BossHp; _fight.BossMaxHp = _cur.BossMaxHp; }
                if (_run != null) _run.LastAt = Max(_run.LastAt, bh.At);
                break;
            case FeedGap:
                foreach (var e in Records())
                {
                    e.Gaps++;
                    e.PartialWhy.RemoveAll(w => w.EndsWith("in the game's traffic"));
                    MarkPartial(e, e.Gaps == 1 ? "a gap in the game's traffic" : $"{e.Gaps} gaps in the game's traffic");
                }
                break;
        }
    }

    /// <summary>The records under way, the pull first.</summary>
    IEnumerable<Encounter> Records() { if (_cur != null) yield return _cur; if (_run != null) yield return _run; if (_fight != null) yield return _fight; }

    void Rejoined(Encounter e, InstanceEntered ie)
    {
        // back in the same copy of the instance (a reconnect, a load screen): the record carries on
        if ((ie.At - e.LastAt).TotalSeconds >= QuietSeconds) MarkPartial(e, "the game reconnected during it");
        if (e.InstanceId == 0) e.InstanceId = ie.InstanceId;
        e.LastAt = Max(e.LastAt, ie.At);
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
            foreach (var e in new[] { _run, _fight })
            {
                if (e == null) continue;
                e.LastAt = Max(e.LastAt, at);
                if (death) e.YourDeaths++;
                else { e.YourKills++; if (player) e.PlayerKills++; }
                if (player && name.Trim().Length > 0) e.Opponent = name.Trim();
                if (!death) e.Kills.Add(new KillNear { At = at, Name = name.Trim().Length > 0 ? name.Trim() : player ? "a player" : "a monster", Player = player, Yours = true });
            }
            if (death && _cur?.You is { } me) me.Deaths++;
            _version++;
        }
        foreach (var e in done) Ended?.Invoke(e);
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

    void Engage(BossEngaged be, List<Encounter> done)
    {
        var name = be.Boss.Trim();
        if (name.Length == 0) name = UnnamedBoss;   // a pull the decoder couldn't name: still a fight
        if (!Kept(_kind))
        {
            Log($"{name}: not in a Transcendence, Expedition or Sanctuary{(_instance.Length > 0 ? $" ({_instance})" : "")}, no boss pull kept");
            if (_mode == ParserMode.Fights) { if (_fight == null) OpenFight(be.At); _fight!.LastAt = Max(_fight.LastAt, be.At); if (_fight.Boss.Length == 0) _fight.Boss = name; }
            return;
        }
        BossBook.Boss? listed = _book.Find(_instance, name);
        if (_book.Knows(_instance) && listed == null && name != UnnamedBoss) Log($"{name}: not on data/bosses.txt for {_instance}; kept under the game's name");
        if (_cur != null)
        {
            bool same = be.BossId != 0 && _cur.BossId != 0 ? be.BossId == _cur.BossId : name.Equals(_cur.Boss, StringComparison.OrdinalIgnoreCase);
            if (same) { _cur.LastAt = Max(_cur.LastAt, be.At); return; }
            // a new boss (or a new spawn of this one): the pull under way is over, and this one starts afresh
            ClosePull(_cur, BossOutcome.Left, Max(_cur.LastAt, _cur.Start), done, $"{name} was pulled");
        }
        _cur = new Encounter
        {
            Mode = "boss", Kind = Encounter.KindName(_kind), Instance = _instance, InstanceId = _instanceId,
            Boss = listed?.Name ?? name, BossId = be.BossId, FinalBoss = listed?.Final == true,
            Start = be.At, LastAt = be.At, App = Upload.RunPayload.AppVersion,
        };
        MergeGroup(_cur, _group, late: false);
        if (_mode == ParserMode.Run && _run == null) OpenRun(be.At, midway: true);
        if (_mode == ParserMode.Fights)
        {
            if (_fight == null) OpenFight(be.At);
            _fight!.LastAt = Max(_fight.LastAt, be.At);
            _fight.Boss = _cur.Boss; _fight.BossId = _cur.BossId; _fight.FinalBoss = _cur.FinalBoss;
        }
        if (_run != null) _run.LastAt = Max(_run.LastAt, be.At);
        Log($"{_cur.Boss} pulled in {_cur.Instance} ({_cur.Kind}), {_cur.Roster.Count} in the group: a new pull");
    }

    void OpenRun(DateTime at, bool midway)
    {
        _run = new Encounter
        {
            Mode = "run", Kind = Encounter.KindName(_kind), Instance = _instance, InstanceId = _instanceId,
            Start = at, LastAt = at, App = Upload.RunPayload.AppVersion,
        };
        if (midway) MarkPartial(_run, "counted from partway through the run");
        MergeGroup(_run, _group, late: false);
        Log($"run: {_instance} ({_run.Kind}){(midway ? ", from partway through" : "")}");
    }

    void OpenFight(DateTime at)
    {
        bool inside = Kept(_kind);
        _fight = new Encounter
        {
            Mode = "fight", Kind = inside ? Encounter.KindName(_kind) : _abyss ? "Abyss" : "Open world",
            Instance = inside ? _instance : _zone, InstanceId = inside ? _instanceId : 0,
            Start = at, LastAt = at, App = Upload.RunPayload.AppVersion,
        };
        MergeGroup(_fight, _group, late: false);
    }

    /// <summary>
    /// The row a hit goes to: the member by id; else a row made from the hit (its name when the decoder knows it, you
    /// marked), so the damage rows are the group until a group list comes. Id 0 is the Other row.
    /// </summary>
    static Fighter SourceOf(Encounter e, DamageDealt dd)
    {
        if (dd.SourceId == 0)
        {
            var other = e.Roster.FirstOrDefault(f => f.Other);
            if (other == null) e.Roster.Add(other = new Fighter { Name = "Other", Other = true });
            return other;
        }
        var f = e.Roster.FirstOrDefault(r => r.Id == dd.SourceId && !r.Other);
        if (f == null) e.Roster.Add(f = new Fighter { Id = dd.SourceId, FromDamage = true });
        if (f.Name.Length == 0 && dd.SourceName.Trim().Length > 0) f.Name = dd.SourceName.Trim();
        f.You |= dd.SourceIsYou;
        return f;
    }

    /// <summary>The group onto a record's roster: new members (Late after its start), known ones updated, missing ones Gone.</summary>
    static void MergeGroup(Encounter e, List<GroupMember> group, bool late)
    {
        foreach (var m in group)
        {
            var f = e.Roster.FirstOrDefault(r => (m.Id != 0 && r.Id == m.Id) || (m.Id == 0 && r.Name.Equals(m.Name.Trim(), StringComparison.OrdinalIgnoreCase)));
            if (f == null) { f = Fighter.From(m); f.Late = late && !e.HasDamage; e.Roster.Add(f); continue; }
            f.Gone = false; f.FromDamage = false;
            if (m.Name.Trim().Length > 0) f.Name = m.Name.Trim();
            if (m.Class.Trim().Length > 0) f.Class = m.Class.Trim();
            if (m.CombatPower > 0) f.CombatPower = m.CombatPower;
            if (m.ItemLevel > 0) f.ItemLevel = m.ItemLevel;
            f.You |= m.You;
        }
        if (late)
            foreach (var f in e.Roster)
                if (!f.Other && !f.FromDamage && !f.You && !group.Any(m => (m.Id != 0 && m.Id == f.Id) || (m.Id == 0 && m.Name.Trim().Equals(f.Name, StringComparison.OrdinalIgnoreCase)))) f.Gone = true;
    }

    static void MarkPartial(Encounter e, string why)
    {
        e.Partial = true;
        if (!e.PartialWhy.Contains(why)) e.PartialWhy.Add(why);
    }

    // ---- ends ----

    void ClosePull(Encounter e, BossOutcome outcome, DateTime at, List<Encounter> done, string? why)
    {
        if (!ReferenceEquals(e, _cur)) return;
        _cur = null;
        e.End = Max(at, e.Start);
        e.LastAt = Max(e.LastAt, e.End.Value);
        e.Outcome = Encounter.OutcomeName(outcome);
        var secs = e.Duration(e.End.Value).TotalSeconds;
        bool worth = outcome == BossOutcome.Kill || secs >= MinSeconds;
        Log($"{e.Boss}: {e.OutcomeLabel.ToLowerInvariant()} after {Clock(e.Duration(e.End.Value))}{(why != null ? $" ({why})" : "")}{(e.Partial ? $", partial: {string.Join(", ", e.PartialWhy)}" : "")}{(worth ? "" : ", too short to keep")}");
        // the run and the fight around it heard of it too; a kill is the fight's (its own pause decides its end)
        foreach (var r in new[] { _run, _fight })
        {
            if (r == null) continue;
            r.LastAt = Max(r.LastAt, e.End.Value);
            if (outcome == BossOutcome.Kill && !r.Kills.Any(k => k.Boss && k.At == e.End.Value)) r.Kills.Add(new KillNear { At = e.End.Value, Name = e.Boss, Boss = true });
        }
        if (!worth) return;
        if (_run != null) _run.Splits.Add(Split.From(e, e.End.Value, live: false));
        if (_mode == ParserMode.Bosses) done.Add(Copy(e));
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
        r.Boss = "";
        var secs = r.Duration(r.End.Value).TotalSeconds;
        bool worth = r.Splits.Count > 0 || secs >= RunMinSeconds;
        Log($"run: {r.Instance} {r.OutcomeLabel.ToLowerInvariant()} after {Clock(r.Duration(r.End.Value))}, {r.Splits.Count} bosses{(why != null ? $" ({why})" : "")}{(worth ? "" : ", too short to keep")}");
        if (worth) done.Add(Copy(r));
    }

    void CloseFight(DateTime at, List<Encounter> done, string? why)
    {
        if (_fight is not { } f) return;
        _fight = null;
        f.End = Max(at, f.Start);
        f.LastAt = Max(f.LastAt, f.End.Value);
        f.Outcome = f.YourDeaths > 0 ? "died" : f.YourKills > 0 || f.Kills.Any(k => k.Boss) ? "kill" : "ended";
        bool worth = f.YourKills > 0 || f.YourDeaths > 0 || f.Boss.Length > 0 || (f.HasDamage && f.Duration(f.End.Value).TotalSeconds >= MinSeconds);
        Log($"fight: {f.Title} in {f.Instance} {f.OutcomeLabel.ToLowerInvariant()} after {Clock(f.Duration(f.End.Value))}{(why != null ? $" ({why})" : "")}{(worth ? "" : ", nothing to keep")}");
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
            if (_cur is { } c && (now - c.LastAt).TotalMinutes >= StaleMinutes)
            {
                MarkPartial(c, $"nothing heard of it for {StaleMinutes:0} minutes");
                ClosePull(c, BossOutcome.Left, c.LastAt, done, null);
                _version++;
            }
            if (_run is { } r && (now - r.LastAt).TotalMinutes >= RunStaleMinutes)
            {
                MarkPartial(r, $"nothing heard of it for {RunStaleMinutes:0} minutes");
                CloseRun(RunEnd(r), r.LastAt, done, null);
                _version++;
            }
            if (_fight is { } f && _cur == null && (now - f.LastAt).TotalSeconds >= PauseSeconds)
            {
                CloseFight(f.LastAt, done, null);
                _version++;
            }
        }
        foreach (var e in done) Ended?.Invoke(e);
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
            var k = Encounter.KindOf(kind);
            if (k == ContentKind.Other && instance != null) k = _book.KindOf(instance);
            var name = instance?.Trim() ?? "";
            if (k == _kind && name == _instance) return;
            _kind = instance == null ? ContentKind.Other : k; _instance = name;
            _version++;
        }
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
                if (s0.End != null) continue;
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
                    if (m != ParserMode.Fights) { _kind = Encounter.KindOf(s.Kind); _instance = s.Instance; _instanceId = s.InstanceId; }
                    Log($"{s.Title}: carries on after the tracker restarted");
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
            _version++;
        }
        foreach (var e in done) Ended?.Invoke(e);
    }

    /// <summary>The single fight saved by 0.10.80's first builds (one record, a boss pull): as <see cref="Resume(IEnumerable{Encounter}, DateTime)"/>.</summary>
    public void Resume(Encounter saved, DateTime now) => Resume(new[] { saved }, now);

    static DateTime Max(DateTime a, DateTime b) => a > b ? a : b;
    static DateTime Min(DateTime a, DateTime b) => a < b ? a : b;

    public static string Clock(TimeSpan d) => d.TotalHours >= 1 ? $"{(int)d.TotalHours}:{d.Minutes:00}:{d.Seconds:00}" : $"{(int)d.TotalMinutes}:{d.Seconds:00}";

    static void Log(string text) => CrashLog.Note("parser: " + text);
}
