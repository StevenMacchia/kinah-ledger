using System.Text.Json;
using KinahLedger.Ocr;

namespace KinahLedger.Session;

/// <summary>
/// One trip into an expedition, an event area, an arena or a legion instance: from the first sign of it (the chat's
/// "joined event", the minimap) to the last loot picked up there. The time after the final reward (walking out, waiting)
/// is not part of the run.
/// </summary>
public sealed class InstanceRun
{
    /// <summary>The name the run is filed under: the expedition, the event's family ("Shugo Festival"), or the event itself when it has no family.</summary>
    public string Zone { get; set; } = "";
    public string Kind { get; set; } = "";
    /// <summary>The event itself ("Hidden Lugi") when it was named; "" for expeditions and when only the family is known. Since 0.8.12.</summary>
    public string Event { get; set; } = "";
    /// <summary>The event's family ("Shugo Festival"); "" for expeditions and for an event in no family. Since 0.8.12.</summary>
    public string Family { get; set; } = "";
    /// <summary>What told the tracker about the run, in order: "chat: joined event 11:00:59 · minimap 11:01:03 · header 11:01:12". Kept on this PC.</summary>
    public string Witnesses { get; set; } = "";
    /// <summary>Why it closed ("left; rewards in", "time for rewards over", "farming began", "the reader was away 51 min").</summary>
    public string Ended { get; set; } = "";
    public DateTime Start { get; set; }
    public DateTime? LastLoot { get; set; }
    /// <summary>When the minimap first named an open-world zone after it.</summary>
    public DateTime? Left { get; set; }
    /// <summary>When its rewards came: a burst of three or more items at once, or the reward table.</summary>
    public DateTime? PaidAt { get; set; }
    public long Kinah { get; set; }
    public int Kills { get; set; }
    /// <summary>Items picked up in the run, tradeable ones, by item id (bound ones can't be sold, so they carry no value).</summary>
    public Dictionary<long, int> Items { get; set; } = new();
    public int Bound { get; set; }
    /// <summary>The bound items by id, so a run's drop list shows them too (they still carry no value). Since 0.8.5.</summary>
    public Dictionary<long, int> BoundItems { get; set; } = new();
    /// <summary>Items the chat printed in the run, never counted: a run's items come from its reward picture (since 0.8.18).</summary>
    public int ChatItemsSetAside { get; set; }
    /// <summary>Those chat items by id, for the log and the tests: where they went, never what the run counts.</summary>
    public Dictionary<long, int> ChatItems { get; set; } = new();
    /// <summary>No reward picture was read for the run, so its rewards are not known (since 0.8.18). Kinah and kills still count.</summary>
    public bool RewardsNotRead { get; set; }
    /// <summary>Entry to last loot, never under ten seconds.</summary>
    public double Seconds => LastLoot is { } l ? Math.Max(10, (l - Start).TotalSeconds) : 0;

    /// <summary>"Hidden Lugi (Shugo Festival)", "Shugo Festival", "Urugugu Canyon".</summary>
    public string Label => Event.Length > 0 && !string.Equals(Event, Zone, StringComparison.OrdinalIgnoreCase) ? $"{Event} ({Zone})" : Zone;

    /// <summary>
    /// Runs written before 0.8.12 name the event alone ("Odyle Flight Frenzy", filed before families existed) or the family
    /// alone: each is filed under its family now, with the event kept beside it, so the Events tab and the site list one
    /// row per family (Steven, 2026-10-22: every Shugo Festival game shares one drop table).
    /// </summary>
    public void FileUnderFamily()
    {
        if (Kind != "Event") return;
        var fam = ZoneLocator.FamilyOf(Zone);
        if (fam.Length > 0 && !string.Equals(fam, Zone, StringComparison.OrdinalIgnoreCase)) { if (Event.Length == 0) Event = Zone; Zone = fam; }
        if (Family.Length == 0) Family = ZoneLocator.FamilyOf(Zone);
    }
}

/// <summary>Where a pickup goes while a run is being left: the run, or not (farming, or a reward screen's).</summary>
public enum RunRoute { Run, NotRun }

/// <summary>
/// The runs in expeditions and events, kept on this PC across restarts (instances.jsonl beside the logs), so an event
/// that comes round once an hour still builds up an average.
/// <para>A run opens at its first witness: the minimap naming an instance, or for an event the chat's "joined event" (up
/// to two minutes before the minimap confirms it), an event NPC's line or the event's header under the minimap. Unknown
/// zone (an inner area the list lacks) keeps it open. It closes when the minimap names another instance, or after it was
/// left (an open-world zone) once its rewards are in; see <see cref="Route"/>. A run with no loot is not kept.</para>
/// </summary>
public sealed class InstanceLog
{
    readonly object _lock = new();
    readonly List<InstanceRun> _done = new();
    /// <summary>Runs closed since this reader started that haven't gone to the site yet.</summary>
    readonly List<InstanceRun> _unshared = new();
    InstanceRun? _cur;
    /// <summary>True when the minimap itself named the open run (not only the chat or the header).</summary>
    bool _byMinimap;
    static string PathOf => System.IO.Path.Combine(System.IO.Path.GetDirectoryName(CrashLog.ReadsPath)!, "instances.jsonl");

    public InstanceLog() { Load(); }

    /// <summary>
    /// An event's rewards print a little after the game has moved the player out (Steven, 2026-10-23, Hidden Lugi: back in
    /// Ruin Exploration Campsite at 11:07:23, the rewards in the chat at 11:07:34; Urugugu Canyon, 2026-10-22: left at
    /// 19:19:56, "Enhance Stone x470" and the 20,000 bound Kina at 19:20:33). For this long after the minimap names an
    /// open-world zone, what looks like a reward is still the run's; see <see cref="Route"/>.
    /// </summary>
    public const double RewardGraceSeconds = 45;
    /// <summary>A "joined event" with no event named by the minimap, a header or an NPC within this long is let go.</summary>
    public const double PendingSeconds = 120;
    /// <summary>
    /// The reader away this long (the game covered, minimized, the tracker paused) with an event open: the event is over
    /// by the time it reads again (Steven, 2026-10-22: Incursion of the Spirits stayed open for 67 minutes across a pause
    /// and took ten skins claimed an hour later). Expeditions last longer: <see cref="AwayExpeditionMinutes"/>.
    /// </summary>
    public const double AwayEventMinutes = 3, AwayExpeditionMinutes = 15;
    /// <summary>Inside an instance the minimap is read every 3 s: a pickup this close before the change may already be outside.</summary>
    public const double MinimapLagSeconds = 3;
    // the event closed by OutAfterRewards, and when: the minimap may name it for a read more
    string? _outZone; DateTime _outAt = DateTime.MinValue;
    DateTime? _leftAt;

    // an event heard of before the minimap names it: since when, what (null: an event, not which), and who said so
    DateTime? _pendingSince; string? _pendingName; readonly List<string> _pendingSeen = new();

    /// <summary>True while the run just left is still taking its rewards (<see cref="RewardGraceSeconds"/> after leaving), for a pickup at <paramref name="t"/>.</summary>
    public bool Ending(DateTime t)
    {
        lock (_lock) return _cur != null && _leftAt is { } l && (t - l).TotalSeconds is >= -5 and <= RewardGraceSeconds;
    }

    /// <summary>True while a run is open and has been left (the minimap names an open-world zone): its rewards may still land.</summary>
    public bool Leaving { get { lock (_lock) return _cur != null && _leftAt != null; } }

    /// <summary>The zone just read (null: nothing new to say).</summary>
    public void Zone(string? zone, DateTime now)
    {
        if (zone == null) return;
        lock (_lock)
        {
            ExpirePending(now);
            var kind = ZoneLocator.KindOf(zone);
            // an area left alone (Nightmare: the same rewards every time): no run, and the one before ends here
            if (kind == ZoneLocator.Ignored) { CloseLocked("an area that isn't counted"); return; }
            // the area the minimap names now, and when it changed (an expedition's areas all file under the expedition)
            if (!string.Equals(zone, _area, StringComparison.OrdinalIgnoreCase)) { _area = zone; _areaSince = now; }
            if (kind != null)
            {
                // an area of an expedition is that expedition: walking from one area to the next is the same run
                var name = ZoneLocator.RunName(zone);
                // ...until its reward table was read: moving on from there (back to the village, into the canyon) is the end of
                // that run, and the way into the next one (Steven, 2026-10-23: five Urugugu runs from 18:26 to 19:04 were one)
                if (_cur != null && _cur.Kind != WorldBoss && _paidArea != null && !string.Equals(zone, _paidArea, StringComparison.OrdinalIgnoreCase))
                    CloseLocked($"its reward table was read, then {zone}");
                var ev = kind == "Event" && !string.Equals(name, zone, StringComparison.OrdinalIgnoreCase) ? zone : kind == "Event" && ZoneLocator.FamilyOf(zone) == "" ? zone : "";
                // the event just closed by the open world's Kina or a kill, still on the minimap for a read: not a new run
                if (_cur == null && _outZone != null && string.Equals(_outZone, name, StringComparison.OrdinalIgnoreCase) && (now - _outAt).TotalSeconds <= 30) return;
                // back in the same instance after leaving it: a flicker of the minimap keeps the run; ten seconds out in the open
                // world and a new entry is a new run (Urugugu Canyon, 2026-10-22: out at 19:19:56, in again at 19:20:39)
                bool reentry = _cur != null && _leftAt is { } out0 && (now - out0).TotalSeconds >= 10;
                if (_cur != null && _cur.Kind != WorldBoss && !reentry && string.Equals(_cur.Zone, name, StringComparison.OrdinalIgnoreCase))
                {
                    _leftAt = null; _cur.Left = null;
                    if (!_byMinimap) { _byMinimap = true; Witness(_cur, "minimap", now); CrashLog.Note($"the minimap confirms the run: {zone}"); }
                    // the minimap names the game where only the family was known: it is more exact
                    if (ev.Length > 0 && _cur.Event.Length == 0) { _cur.Event = ev; CrashLog.Note($"the minimap names the event: {ev}"); }
                    else if (ev.Length > 0 && !string.Equals(_cur.Event, ev, StringComparison.OrdinalIgnoreCase))
                    {
                        CrashLog.Note($"witnesses disagree: the run says {_cur.Event}, the minimap says {ev}; the minimap is kept");
                        _cur.Event = ev;
                    }
                    return;
                }
                CloseLocked(reentry && string.Equals(_cur?.Zone, name, StringComparison.OrdinalIgnoreCase) ? "left, then a new entry" : "another run began");
                _cur = new InstanceRun { Zone = name, Kind = ZoneLocator.KindOf(name) ?? kind, Event = ev, Family = kind == "Event" ? ZoneLocator.FamilyOf(name) : "", Start = now };
                _byMinimap = true;
                // the chat or the header saw it first: the run starts there (an event's lobby fills before the minimap changes)
                if (kind == "Event" && _pendingSince is { } since && (now - since).TotalSeconds <= PendingSeconds)
                {
                    _cur.Start = since;
                    _cur.Witnesses = string.Join(" · ", _pendingSeen);
                    if (_pendingName != null && ev.Length > 0 && !string.Equals(_pendingName, ev, StringComparison.OrdinalIgnoreCase) && !string.Equals(_pendingName, name, StringComparison.OrdinalIgnoreCase))
                        CrashLog.Note($"witnesses disagree: the chat said {_pendingName}, the minimap says {ev}; the minimap is kept");
                    else if (_pendingName != null && _cur.Event.Length == 0 && ZoneLocator.FamilyOf(_pendingName) == _cur.Zone && _pendingName != _cur.Zone) _cur.Event = _pendingName;
                }
                ClearPending();
                Witness(_cur, "minimap", now);
                CrashLog.Note($"{_cur.Kind.ToLowerInvariant()} run started: {_cur.Label}" + (name != zone && zone != _cur.Event ? $" (in {zone})" : "") + (_cur.Start < now ? $", first seen {(now - _cur.Start).TotalSeconds:0} s earlier ({_cur.Witnesses.Split(" · ")[0]})" : ""));
            }
            else if (zone != SessionEngine.UnknownZone)
            {
                // out in the open world: an event or expedition run takes its rewards for a little longer before it closes
                if (_cur != null && _cur.Kind != WorldBoss)
                {
                    if (_leftAt == null)
                    {
                        _leftAt = now; _cur.Left = now;
                        CrashLog.Note($"left {_cur.Label}: rewards that land in the next {RewardGraceSeconds:0} s are still its own" + (_cur.PaidAt != null ? " (its rewards came already)" : ""));
                        return;
                    }
                    if ((now - _leftAt.Value).TotalSeconds <= RewardGraceSeconds) return;
                    CloseLocked("time for rewards over");
                }
            }
        }
    }

    /// <summary>
    /// What the chat or the event header said about an event (EventWitness, EventHeader): <paramref name="name"/> is an
    /// event or a family on the list, or null for "an event is starting" ("joined event"). <paramref name="zone"/> is the
    /// zone as last read. The minimap is the main witness; these open a run before it (dated from the first of them),
    /// confirm or correct the event's name, and open one alone where the minimap names no zone on the list.
    /// </summary>
    public void Witness(string source, string? name, DateTime t, string? zone)
    {
        lock (_lock)
        {
            ExpirePending(t);
            if (name != null) name = ZoneLocator.MatchEvent(name) ?? name;
            var fam = name == null ? "" : ZoneLocator.FamilyOf(name);
            var filed = name == null ? null : fam.Length > 0 ? fam : name;
            if (_cur != null && _cur.Kind == "Event")
            {
                if (name == null || string.Equals(filed, _cur.Zone, StringComparison.OrdinalIgnoreCase))
                {
                    // the same event: the name gets more exact when the run knew only the family
                    if (name != null && _cur.Event.Length == 0 && !string.Equals(name, _cur.Zone, StringComparison.OrdinalIgnoreCase)) { _cur.Event = name; CrashLog.Note($"{source} names the event: {name}"); }
                    else if (name != null && _cur.Event.Length > 0 && !string.Equals(name, _cur.Zone, StringComparison.OrdinalIgnoreCase) && !string.Equals(name, _cur.Event, StringComparison.OrdinalIgnoreCase))
                    {
                        // a witness that disagrees is logged, not listed with the ones that saw the run
                        if (_byMinimap) { CrashLog.Note($"witnesses disagree: the minimap says {_cur.Event}, {source} says {name}; the minimap is kept"); return; }
                        CrashLog.Note($"witnesses disagree: the run said {_cur.Event}, {source} says {name}; now {name}"); _cur.Event = name;
                    }
                    Witness(_cur, source, t);
                    return;
                }
                if (name != null)
                {
                    CrashLog.Note($"witnesses disagree: the run is {_cur.Label}, {source} says {name}" + (_byMinimap ? "; the minimap is kept" : "; it is corrected"));
                    if (_byMinimap) return;
                    _cur.Zone = filed!; _cur.Family = fam; _cur.Event = name == filed ? "" : name;
                    Witness(_cur, source, t);
                    return;
                }
                return;
            }
            if (_cur != null && _cur.Kind != WorldBoss)
            {
                // an expedition, an arena: an event line in the chat there is someone else's (the world chat), not this run's
                if (name != null) CrashLog.Note($"witnesses disagree: in {_cur.Label}, {source} says {name}; not a run");
                return;
            }
            // nothing open: the start of an event, heard before the minimap names it
            bool fresh = _pendingSince == null;
            _pendingSince ??= t;
            if (name != null) _pendingName ??= name;
            var w = $"{source} {t.ToLocalTime():HH:mm:ss}";
            if (!_pendingSeen.Any(x => x.StartsWith(source))) _pendingSeen.Add(w);
            if (fresh) CrashLog.Note($"{source}: an event is starting{(name != null ? $" ({name})" : "")}; the run starts here once the minimap or the header confirms it");
            // the minimap names no zone on the list (an inner area, a title it can't read): a named witness is enough
            if (name != null && (zone == null || zone == SessionEngine.UnknownZone || ZoneLocator.KindOf(zone) == "Event"))
            {
                CloseLocked("another run began");
                _cur = new InstanceRun { Zone = filed!, Kind = "Event", Event = name == filed ? "" : name, Family = fam, Start = _pendingSince.Value, Witnesses = string.Join(" · ", _pendingSeen) };
                _byMinimap = false;
                ClearPending();
                CrashLog.Note($"event run started: {_cur.Label}, from {source} (the minimap names {zone ?? "no zone yet"})");
            }
        }
    }

    void ExpirePending(DateTime now)
    {
        if (_pendingSince is { } p && (now - p).TotalSeconds > PendingSeconds)
        {
            CrashLog.Note($"the chat said an event was starting ({string.Join(", ", _pendingSeen)}), but no event followed in {PendingSeconds / 60:0} minutes: not a run");
            ClearPending();
        }
    }

    void ClearPending() { _pendingSince = null; _pendingName = null; _pendingSeen.Clear(); }

    static void Witness(InstanceRun r, string source, DateTime t)
    {
        if (r.Witnesses.Split(" · ").Any(w => w.StartsWith(source + " "))) return;
        r.Witnesses = (r.Witnesses.Length > 0 ? r.Witnesses + " · " : "") + $"{source} {t.ToLocalTime():HH:mm:ss}";
    }

    /// <summary>
    /// Where a pickup goes while the open run is being left (the minimap names an open-world zone). Only what looks like
    /// a reward is the run's: a bound item or bound Kina, Kina by the thousand, a stack of two or more, or one of three or
    /// more lines that landed at once (<paramref name="batch"/>). Loose Kina and a kill are the open world's: farming began,
    /// and the run closes (Steven, 2026-10-22: after Accursed Sword the next minute's Kina drops landed in the event). A
    /// lone item counts for the run only before its rewards came. With a reward screen open it is that screen's.
    /// </summary>
    public RunRoute Route(GameEvent ev, int batch, bool rewardScreen)
    {
        lock (_lock)
        {
            var r = Decide(ev, batch, rewardScreen, out var close);
            if (close != null) CloseLocked(close);
            return r;
        }
    }

    /// <summary>The same decision as <see cref="Route"/>, without closing anything: whether the run would take this pickup.</summary>
    public bool WouldTake(GameEvent ev, int batch, bool rewardScreen)
    {
        lock (_lock) return Decide(ev, batch, rewardScreen, out _) == RunRoute.Run;
    }

    RunRoute Decide(GameEvent ev, int batch, bool rewardScreen, out string? close)
    {
        close = null;
        if (_cur == null || _cur.Kind == WorldBoss || _leftAt is not { } left) return RunRoute.NotRun;
        // dated well before it was left (a read that settled late): it happened inside. Within the minimap's few seconds of
        // lag before the change it may be the open world's already, so it takes the test below
        if (ev.Time < left.AddSeconds(-MinimapLagSeconds) && ev.Time >= _cur.Start.AddSeconds(-3)) return RunRoute.Run;
        if ((ev.Time - left).TotalSeconds > RewardGraceSeconds) { close = "time for rewards over"; return RunRoute.NotRun; }
        if (rewardScreen) return RunRoute.NotRun;
        switch (ev)
        {
            case RewardReceived:
            case ItemAcquired { Bound: true }:
            case KinahChanged { Bound: true, Delta: > 0 }:
            case KinahChanged { Delta: >= 1000 }:
            case ItemAcquired { Count: >= 2 }:
                return RunRoute.Run;
            case ItemAcquired when batch >= 3:
                return RunRoute.Run;
            case ItemAcquired:
                return _cur.PaidAt == null ? RunRoute.Run : RunRoute.NotRun;
            case KinahChanged { Delta: > 0 }:
            case MobKilled:
            case CurrencyGained:
            case EnemySoul:
            case XpGained:
            case GatherFailed:
                close = (ev.Time - left).TotalSeconds >= 1 ? $"farming began {(ev.Time - left).TotalSeconds:0} s after leaving ({ev.GetType().Name})" : $"farming began as it was left ({ev.GetType().Name})";
                return RunRoute.NotRun;
            default:
                return RunRoute.NotRun;
        }
    }

    /// <summary>
    /// Inside an event whose rewards came already, loose Kina (under a thousand, unbound) or a kill is the open world's:
    /// the player is out and the minimap hasn't said so yet (it is read every 3 s; Steven, 2026-10-22: after Accursed Sword,
    /// 498 Kina two seconds before the minimap changed). The event closes and the pickup is farming. True when it closed.
    /// </summary>
    public bool OutAfterRewards(GameEvent ev)
    {
        lock (_lock)
        {
            if (_cur is not { Kind: "Event", PaidAt: { } paid } || _leftAt != null || ev.Time < paid) return false;
            bool open = ev is KinahChanged { Bound: false, Delta: > 0 and < 1000 } || ev is CurrencyGained c && c.Name.EndsWith("Soul Points", StringComparison.OrdinalIgnoreCase) || ev is MobKilled or EnemySoul;
            if (!open) return false;
            _outZone = _cur.Zone; _outAt = ev.Time;
            CloseLocked($"its rewards came at {paid.ToLocalTime():HH:mm:ss}, then the open world's {(ev is KinahChanged ? "Kina" : "kill")} at {ev.Time.ToLocalTime():HH:mm:ss}, before the minimap changed");
            return true;
        }
    }

    /// <summary>
    /// The reader wasn't reading from <paramref name="from"/> to <paramref name="to"/> (paused, covered, minimized). An
    /// event open across a long gap is over; it closes where it was last seen. True when a run was closed.
    /// </summary>
    public bool Away(DateTime from, DateTime to)
    {
        lock (_lock)
        {
            if (_cur == null || _cur.Kind == WorldBoss) return false;
            double mins = (to - from).TotalMinutes;
            double limit = _cur.Kind == "Event" ? AwayEventMinutes : AwayExpeditionMinutes;
            if (mins < limit) return false;
            CloseLocked($"the reader was away {mins:0} min from {from.ToLocalTime():HH:mm}");
            return true;
        }
    }

    /// <summary>An event as it was counted (rewards already turned into rewards): loot and kills go to the open run.</summary>
    /// <param name="batch">How many loot lines landed with it at once: three or more items are the run's rewards coming in.</param>
    public void Loot(GameEvent ev, int batch = 1)
    {
        lock (_lock)
        {
            if (_cur == null) return;
            // the run was left and its time for rewards is over: it closes, and this pickup is farming's
            if (_leftAt is { } left && (ev.Time - left).TotalSeconds > RewardGraceSeconds) { CloseLocked("time for rewards over"); return; }
            // an event's items come from its reward screen's picture, never the chat (Steven, 2026-10-23). A chat item is set
            // aside, counted for the log; it still dates the run and its rewards' arrival. Expeditions keep their reading for now
            if (_cur.Kind == "Event" && ev is ItemAcquired ci && !ev.Via.Contains("reward table"))
            {
                _cur.ChatItemsSetAside += ci.Count; _cur.ChatItems[ci.ItemId] = _cur.ChatItems.GetValueOrDefault(ci.ItemId) + ci.Count; _cur.LastLoot = ci.Time;
                if (batch >= 3) Paid(ci.Time);
                return;
            }
            switch (ev)
            {
                case ItemAcquired a:
                    if (a.Bound) { _cur.Bound += a.Count; _cur.BoundItems[a.ItemId] = _cur.BoundItems.GetValueOrDefault(a.ItemId) + a.Count; }
                    else _cur.Items[a.ItemId] = _cur.Items.GetValueOrDefault(a.ItemId) + a.Count;
                    _cur.LastLoot = a.Time;
                    if (batch >= 3 || a.Via.Contains("reward table")) Paid(a.Time);
                    break;
                case RewardReceived r when batch >= 3:
                    Paid(r.Time);
                    break;
                // bound Kina too: an expedition's or a Transcendence's end reward ("Acquired 20,000 Kina (Bound).") is money to
                // spend all the same (Steven, 2026-10-22: "its straight cash")
                case KinahChanged k when k.Delta > 0:
                    _cur.Kinah += k.Delta; _cur.LastLoot = k.Time;
                    if (batch >= 3 || k.Via.Contains("reward table")) Paid(k.Time);
                    break;
                // a world boss run is one kill, the boss's: the enemies dying around it are not
                case MobKilled when _cur.Kind != WorldBoss:
                case CurrencyGained c when _cur.Kind != WorldBoss && c.Name.EndsWith("Soul Points", StringComparison.OrdinalIgnoreCase):
                    _cur.Kills++;
                    break;
            }
        }
    }

    void Paid(DateTime t)
    {
        if (_cur == null || _cur.PaidAt != null) return;
        _cur.PaidAt = t;
        CrashLog.Note($"{_cur.Label}: its rewards came in ({t.ToLocalTime():HH:mm:ss})");
    }

    /// <summary>The reward table at the end was read: the run's rewards are in.</summary>
    public void RewardTable(DateTime t)
    {
        lock (_lock)
        {
            Paid(t);
            if (_cur == null || _cur.Kind == WorldBoss) return;
            _tableAt = t; _paidArea = _area;
        }
    }

    /// <summary>
    /// A reward table is about to be read into the open run. When this run's table was read already (a minute or more ago),
    /// this one is the next run's, never added onto the first: the first closes and the next starts, from the last change
    /// of area since that table (2026-10-23: five tables, one run of 37:04 with 3,000,000 Kinah).
    /// </summary>
    public void TableStarting(DateTime t)
    {
        lock (_lock)
        {
            if (_cur == null || _cur.Kind == WorldBoss || _tableAt is not DateTime prev || (t - prev).TotalSeconds < 60) return;
            var zone = _cur.Zone; var kind = _cur.Kind; var ev = _cur.Event; var fam = _cur.Family;
            var start = _areaSince is DateTime a && a > prev ? a : t;
            CloseLocked("a second reward table: the next run's");
            _cur = new InstanceRun { Zone = zone, Kind = kind, Event = ev, Family = fam, Start = start };
            _byMinimap = true;
            CrashLog.Note($"{kind.ToLowerInvariant()} run started: {_cur.Label} (a second reward table at {t.ToLocalTime():HH:mm:ss})");
        }
    }
    string? _area, _paidArea; DateTime? _areaSince, _tableAt;

    /// <summary>An event's rewards came in the last <paramref name="seconds"/> s (for its reward pictures, every 2 s).</summary>
    public bool EventPaidWithin(DateTime now, double seconds)
    {
        lock (_lock) return _cur is { Kind: "Event", PaidAt: DateTime p } && (now - p).TotalSeconds <= seconds;
    }

    /// <summary>An event's reward screen is up: its rewards are in (their picture is kept until its cards can be read).</summary>
    public void EventRewardScreen(DateTime t) { lock (_lock) { if (_cur?.Kind == "Event") Paid(t); } }

    /// <summary>Closes the open run (the reader is stopping).</summary>
    public void Close() { lock (_lock) CloseLocked("the reader stopped"); }

    public const string WorldBoss = "World Boss";

    /// <summary>
    /// A world boss just died: what lands next is its loot, a run of its own (kind <see cref="WorldBoss"/>, named for the
    /// boss), never farming. False when an expedition or event run is open: there the run keeps everything as before.
    /// </summary>
    public bool StartBoss(string boss, DateTime now)
    {
        lock (_lock)
        {
            if (_cur != null && _cur.Kind != WorldBoss) return false;
            CloseLocked("a world boss died");
            _cur = new InstanceRun { Zone = boss, Kind = WorldBoss, Start = now, Kills = 1 };
            CrashLog.Note($"world boss run started: {boss}");
            return true;
        }
    }

    /// <summary>The world boss's loot window is over: its run closes. Any other open run is left alone.</summary>
    public void EndBoss() { lock (_lock) if (_cur?.Kind == WorldBoss) CloseLocked("its loot window is over"); }

    void CloseLocked(string why)
    {
        _leftAt = null; _paidArea = null; _tableAt = null;
        if (_cur != null) _cur.Ended = why;
        if (_cur != null && _cur.Kind == "Event")
        {
            _cur.RewardsNotRead = true;
            if (_cur.LastLoot != null) CrashLog.Note($"{_cur.Label}: its reward screen isn't read yet, so its rewards are marked not read ({_cur.ChatItemsSetAside} chat item(s) not counted)");
        }
        if (_cur?.LastLoot != null)
        {
            _done.Add(_cur);
            _unshared.Add(_cur);
            try { File.AppendAllText(PathOf, JsonSerializer.Serialize(_cur) + "\n"); } catch (Exception) { /* a read-only profile keeps it for this session */ }
            CrashLog.Note($"{_cur.Kind.ToLowerInvariant()} run done: {_cur.Label}, {TimeSpan.FromSeconds(_cur.Seconds):m\\:ss}, {_cur.Kinah:N0} Kinah, {_cur.Items.Values.Sum()} items ({why}; {_cur.Witnesses})");
        }
        else if (_cur != null) CrashLog.Note($"{_cur.Kind.ToLowerInvariant()} run closed with no loot, not kept: {_cur.Label} ({why})");
        _cur = null; _byMinimap = false;
    }

    /// <summary>Every kept run plus the open one when it has loot, oldest first.</summary>
    public List<InstanceRun> Snapshot()
    {
        lock (_lock)
        {
            var all = new List<InstanceRun>(_done);
            if (_cur?.LastLoot != null) all.Add(_cur);
            return all;
        }
    }

    public string? Open { get { lock (_lock) return _cur?.Zone; } }

    /// <summary>The open run as it stands (kind, name, event, family, left or not), or null. A copy of its labels only.</summary>
    public (string Kind, string Zone, string Event, string Family, string Label, bool Leaving, DateTime Start)? Current
    {
        get { lock (_lock) return _cur is { } c ? (c.Kind, c.Zone, c.Event, c.Family, c.Label, _leftAt != null, c.Start) : null; }
    }

    /// <summary>An event heard of in the chat that the minimap hasn't confirmed yet, or null.</summary>
    public string? Pending { get { lock (_lock) return _pendingSince == null ? null : _pendingName ?? "an event"; } }

    /// <summary>
    /// Clears the runs shown (the player's "Clear" on the Events part). The history file is kept aside as
    /// instances.old.jsonl; runs not shared yet, and the one under way, still go to the site as usual.
    /// </summary>
    public void ClearShown()
    {
        lock (_lock)
        {
            _done.Clear();
            try { var old = PathOf.Replace(".jsonl", ".old.jsonl"); if (File.Exists(old)) File.Delete(old); if (File.Exists(PathOf)) File.Move(PathOf, old); }
            catch (Exception) { /* the list is cleared for this session either way */ }
        }
    }

    /// <summary>
    /// The runs to share with a run or a chunk that started at <paramref name="since"/>: those closed since the last share
    /// whose last loot came after it, or up to 20 minutes before it (a run that ends just before a chunk rolls over is
    /// closed by the next zone read and goes with the next chunk). Each run is handed over once: a farming run and the
    /// always-on chunk both share, and the site counts a run with whichever goes first. Older ones are let go.
    /// </summary>
    public List<InstanceRun> TakeUnshared(DateTime since)
    {
        lock (_lock)
        {
            var take = _unshared.Where(r => r.LastLoot is { } l && l >= since.AddMinutes(-20)).ToList();
            _unshared.Clear();
            return take;
        }
    }

    /// <summary>Puts runs back after a send that didn't reach the site, so the next share takes them.</summary>
    public void Restore(IEnumerable<InstanceRun> runs)
    {
        lock (_lock) _unshared.InsertRange(0, runs.Where(r => !_unshared.Contains(r)));
    }

    void Load()
    {
        try
        {
            if (!File.Exists(PathOf)) return;
            foreach (var line in File.ReadLines(PathOf).TakeLast(2000))
                if (line.Length > 2 && JsonSerializer.Deserialize<InstanceRun>(line) is { } r && r.LastLoot != null) { r.FileUnderFamily(); _done.Add(r); }
        }
        catch (Exception) { /* a damaged history starts over; the file is kept for a look */ }
    }
}
