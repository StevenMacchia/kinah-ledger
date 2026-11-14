using System.Text.Json;
using KinahLedger.Packets;

namespace KinahLedger.Parser;

/// <summary>
/// Boss fights from the game's messages (Steven, 2026-11-13: "ONLY for bosses within the zone and nothing else ... it
/// should reset every time you start a new boss"). The rules:
/// <list type="bullet">
/// <item>A fight exists only inside a Transcendence, an Expedition or a Sanctuary, and only for a boss pull. The decoder
///   says a pull only for the bosses on its own list; the tracker's list (Parser/BossBook.cs) gives the kind (a Sanctuary),
///   the final boss and the name as players know it. A name that list doesn't have is logged, not dropped: a spelling
///   between the two lists must never lose a fight. Trash, elites and anything in the open world never make one.</item>
/// <item>One fight per boss: pulling another boss (or a new spawn of the same one) ends the fight under way and starts a new one.</item>
/// <item>It ends with the boss's death (kill), the game saying the group fell (wipe), or the player leaving the instance
///   or the fight going quiet for <see cref="StaleMinutes"/> (left).</item>
/// <item>Lost traffic, a reconnect or a tracker restart never split a fight: it carries on, marked partial with why.</item>
/// </list>
/// Fed on the session's thread; the window reads copies (<see cref="Live"/>).
/// </summary>
public sealed class BossParser
{
    readonly object _gate = new();
    readonly BossBook _book;

    /// <summary>A fight that has ended and is worth keeping: the caller saves it (Parser/EncounterStore.cs).</summary>
    public Action<Encounter>? Ended { get; set; }

    /// <summary>A fight with nothing heard for this long is given up (left) at the last thing heard.</summary>
    public const double StaleMinutes = 10;
    /// <summary>A fight saved before a restart carries on when the tracker is back within this long; older, it is kept as it was.</summary>
    public const double ResumeMinutes = 15;
    /// <summary>A pull shorter than this that didn't kill the boss (a body pull, a reset) isn't kept.</summary>
    public const double MinSeconds = 5;
    /// <summary>A live fight with nothing heard for this long and then the instance again: the connection dropped in between.</summary>
    const double QuietSeconds = 30;

    public BossParser(BossBook? book = null) => _book = book ?? BossBook.Shared;

    /// <summary>A pull with no name: the fight is kept under this.</summary>
    public const string UnnamedBoss = "Unnamed boss";

    // where the player is
    ContentKind _kind;
    string _instance = "";
    long _instanceId;
    bool _instanceFromGame;
    List<GroupMember> _group = new();
    Encounter? _cur;
    int _gaps;
    DateTime _lastEventAt = DateTime.MinValue;

    /// <summary>Goes up with every change the window should draw.</summary>
    public int Version { get { lock (_gate) return _version; } }
    int _version;

    /// <summary>When the game last said anything about a fight (MinValue: never, as in a build without the decoder).</summary>
    public DateTime LastEventAt { get { lock (_gate) return _lastEventAt; } }

    /// <summary>Where the player is: the content kind (Other outside the three) and the instance's name.</summary>
    public (ContentKind Kind, string Instance) Where { get { lock (_gate) return (_kind, _instance); } }

    /// <summary>The group as the game last said it.</summary>
    public IReadOnlyList<GroupMember> Group { get { lock (_gate) return _group.ToList(); } }

    /// <summary>A copy of the fight under way, or null.</summary>
    public Encounter? Live { get { lock (_gate) return _cur == null ? null : Copy(_cur); } }

    public static Encounter Copy(Encounter e) => JsonSerializer.Deserialize<Encounter>(JsonSerializer.Serialize(e))!;

    static bool Kept(ContentKind k) => k is ContentKind.Transcendence or ContentKind.Expedition or ContentKind.Sanctuary;

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
                if (kind == ContentKind.Other && _book.KindOf(ie.Name) is var listed && listed != ContentKind.Other) kind = listed;
                if (_cur != null)
                {
                    bool same = ie.InstanceId != 0 && _cur.InstanceId != 0 ? ie.InstanceId == _cur.InstanceId
                        : BossBook.Strip(ie.Name).Equals(BossBook.Strip(_cur.Instance), StringComparison.OrdinalIgnoreCase) && Kept(kind);
                    if (same)
                    {
                        // back in the same copy of the instance (a reconnect, a load screen): the fight carries on
                        if ((ie.At - _cur.LastAt).TotalSeconds >= QuietSeconds) MarkPartial(_cur, "the game reconnected during the fight");
                        if (_cur.InstanceId == 0) _cur.InstanceId = ie.InstanceId;
                        _cur.LastAt = Max(_cur.LastAt, ie.At);
                    }
                    else Close(_cur, BossOutcome.Left, _cur.LastAt, done, "left for another place");
                }
                _kind = kind; _instance = ie.Name.Trim(); _instanceId = ie.InstanceId; _instanceFromGame = true;
                Log($"entered {(_instance.Length > 0 ? _instance : "a place")}{(Kept(kind) ? $" ({Encounter.KindName(kind)}): boss fights are kept" : ": no boss fights kept here")}");
                break;
            }
            case InstanceLeft il:
                if (_cur != null) Close(_cur, BossOutcome.Left, il.At, done, "left the instance");
                _kind = ContentKind.Other; _instance = ""; _instanceId = 0;
                break;
            case BossEngaged be:
                Engage(be, done);
                break;
            case BossEnded end:
                if (_cur != null && (end.BossId == 0 || end.BossId == _cur.BossId)) Close(_cur, end.Outcome, end.At, done, null);
                break;
            case UnitDied ud:
                if (_cur == null) break;
                _cur.LastAt = Max(_cur.LastAt, ud.At);
                if ((ud.Id != 0 && ud.Id == _cur.BossId) || (ud.Boss && ud.Name.Trim().Equals(_cur.Boss, StringComparison.OrdinalIgnoreCase) && (ud.Id == 0 || _cur.BossId == 0)))
                {
                    _cur.Kills.Add(new KillNear { At = ud.At, Name = _cur.Boss, Boss = true });
                    Close(_cur, BossOutcome.Kill, ud.At, done, null);
                }
                else if (_cur.Roster.FirstOrDefault(f => ud.Id != 0 && f.Id == ud.Id) is { } member) member.Deaths++;
                else if (ud.Name.Trim().Length > 0) _cur.Kills.Add(new KillNear { At = ud.At, Name = ud.Name.Trim(), Boss = ud.Boss });
                break;
            case GroupChanged gc:
                _group = gc.Members.Where(m => m.Name.Trim().Length > 0 || m.You).ToList();
                if (_cur != null) MergeGroup(_cur, _group, late: true);
                break;
            case DamageDealt dd:
                if (_cur == null || dd.Amount <= 0) break;
                _cur.LastAt = Max(_cur.LastAt, dd.At);
                _cur.HasDamage = true;
                if (dd.TargetId != 0 && dd.TargetId == _cur.BossId) _cur.BossDamage += dd.Amount;
                SourceOf(_cur, dd).Damage += dd.Amount;
                break;
            case BossHealth bh:
                if (_cur == null || (bh.BossId != 0 && _cur.BossId != 0 && bh.BossId != _cur.BossId)) break;
                _cur.LastAt = Max(_cur.LastAt, bh.At);
                _cur.BossHp = Math.Max(0, bh.Hp);
                if (bh.Max > 0) _cur.BossMaxHp = bh.Max;
                break;
            case FeedGap:
                if (_cur == null) break;
                _gaps++;
                _cur.PartialWhy.RemoveAll(w => w.EndsWith("in the game's traffic"));
                MarkPartial(_cur, _gaps == 1 ? "a gap in the game's traffic" : $"{_gaps} gaps in the game's traffic");
                break;
        }
    }

    void Engage(BossEngaged be, List<Encounter> done)
    {
        var name = be.Boss.Trim();
        if (name.Length == 0) name = UnnamedBoss;   // a pull the decoder couldn't name: still a fight
        if (!Kept(_kind))
        {
            Log($"{name}: not in a Transcendence, Expedition or Sanctuary{(_instance.Length > 0 ? $" ({_instance})" : "")}, no fight kept");
            return;
        }
        BossBook.Boss? listed = _book.Find(_instance, name);
        if (_book.Knows(_instance) && listed == null && name != UnnamedBoss) Log($"{name}: not on data/bosses.txt for {_instance}; kept under the game's name");
        if (_cur != null)
        {
            bool same = be.BossId != 0 && _cur.BossId != 0 ? be.BossId == _cur.BossId : name.Equals(_cur.Boss, StringComparison.OrdinalIgnoreCase);
            if (same) { _cur.LastAt = Max(_cur.LastAt, be.At); return; }
            // a new boss (or a new spawn of this one): the fight under way is over, and this one starts afresh
            Close(_cur, BossOutcome.Left, Max(_cur.LastAt, _cur.Start), done, $"{name} was pulled");
        }
        _gaps = 0;
        _cur = new Encounter
        {
            Kind = Encounter.KindName(_kind), Instance = _instance, InstanceId = _instanceId,
            Boss = listed?.Name ?? name, BossId = be.BossId, FinalBoss = listed?.Final == true,
            Start = be.At, LastAt = be.At, App = Upload.RunPayload.AppVersion,
        };
        MergeGroup(_cur, _group, late: false);
        Log($"{_cur.Boss} pulled in {_cur.Instance} ({_cur.Kind}), {_cur.Roster.Count} in the group: a new fight");
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

    /// <summary>The group onto the fight's roster: new members (Late after the pull), known ones updated, missing ones Gone.</summary>
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

    void Close(Encounter e, BossOutcome outcome, DateTime at, List<Encounter> done, string? why)
    {
        if (!ReferenceEquals(e, _cur)) return;
        _cur = null;
        e.End = Max(at, e.Start);
        e.LastAt = Max(e.LastAt, e.End.Value);
        e.Outcome = Encounter.OutcomeName(outcome);
        var secs = e.Duration(e.End.Value).TotalSeconds;
        if (outcome != BossOutcome.Kill && secs < MinSeconds)
        {
            Log($"{e.Boss}: {secs:0.#} s and no kill{(why != null ? $" ({why})" : "")}, not kept");
            return;
        }
        Log($"{e.Boss}: {e.OutcomeLabel.ToLowerInvariant()} after {Clock(e.Duration(e.End.Value))}{(why != null ? $" ({why})" : "")}{(e.Partial ? $", partial: {string.Join(", ", e.PartialWhy)}" : "")}");
        done.Add(Copy(e));
    }

    /// <summary>The window's tick: a fight with nothing heard for <see cref="StaleMinutes"/> is given up.</summary>
    public void Tick(DateTime now)
    {
        List<Encounter> done = new();
        lock (_gate)
        {
            if (_cur is { } c && (now - c.LastAt).TotalMinutes >= StaleMinutes)
            {
                MarkPartial(c, $"nothing heard of it for {StaleMinutes:0} minutes");
                Close(c, BossOutcome.Left, c.LastAt, done, null);
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
    /// The fight that was under way when the tracker closed (encounter-live.json): within <see cref="ResumeMinutes"/> it
    /// carries on, partial; older, it is kept as it was (left at the last thing heard).
    /// </summary>
    public void Resume(Encounter saved, DateTime now)
    {
        if (saved.End != null) return;
        List<Encounter> done = new();
        lock (_gate)
        {
            if (_cur != null) return;
            saved = Copy(saved);
            _cur = saved;
            if ((now - saved.LastAt).TotalMinutes <= ResumeMinutes)
            {
                MarkPartial(saved, "the tracker restarted during the fight");
                _kind = Encounter.KindOf(saved.Kind); _instance = saved.Instance; _instanceId = saved.InstanceId;
                Log($"{saved.Boss}: carries on after the tracker restarted");
            }
            else
            {
                MarkPartial(saved, "the tracker closed during the fight");
                Close(saved, BossOutcome.Left, saved.LastAt, done, null);
            }
            _version++;
        }
        foreach (var e in done) Ended?.Invoke(e);
    }

    static DateTime Max(DateTime a, DateTime b) => a > b ? a : b;

    public static string Clock(TimeSpan d) => d.TotalHours >= 1 ? $"{(int)d.TotalHours}:{d.Minutes:00}:{d.Seconds:00}" : $"{(int)d.TotalMinutes}:{d.Seconds:00}";

    static void Log(string text) => CrashLog.Note("parser: " + text);
}
