using System.Text.Json;
using KinahLedger.Ocr;
using KinahLedger.Data;

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
    /// <summary>The items came from the reward table's picture, not the chat (since 0.8.18).</summary>
    public bool FromPicture { get; set; }
    /// <summary>Slots on the reward table the reader couldn't name (since 0.8.18).</summary>
    public int SlotsUnread { get; set; }
    /// <summary>Those slots as the picture shows them ("Rare x14 · 20261023-175637-...-reward-table.jpg"): shown as Unknown item, never dropped.</summary>
    public List<string> UnknownSlots { get; set; } = new();
    /// <summary>Items the chat printed in the run, never counted: a run's items come from its reward picture (since 0.8.18).</summary>
    public int ChatItemsSetAside { get; set; }
    /// <summary>Those chat items by id, for the log and the tests: where they went, never what the run counts.</summary>
    public Dictionary<long, int> ChatItems { get; set; } = new();
    /// <summary>
    /// Kina the chat printed in a run counted from its reward picture, never the run's Kinah (since 2026-10-25): its Kinah is
    /// the table's coin or the event cards'. On 2026-10-23 and 24 every such line in an Urugugu Canyon, Vakron Sky Island or
    /// Hidden Lugi run was an achievement's reward or a claim (14:15:57, 9,000 after "Soulful Unique Gear I achieved."; 20:43:46
    /// Oct 23, 43,000 after "Foodie I achieved.") or the open world's (18:07:35, 474); no enemy in them dropped Kina.
    /// Kept for the log and the tests.
    /// </summary>
    public long ChatKinahSetAside { get; set; }
    /// <summary>No reward picture was read for the run, so its rewards are not known (since 0.8.18). Kills still count; the chat's Kina is set aside like its items (since 2026-10-25).</summary>
    public bool RewardsNotRead { get; set; }
    /// <summary>The reward table's coin slot (a K or M count) was there but not named, so the run's Kinah is not known (since 0.8.24).</summary>
    public bool KinahNotRead { get; set; }
    /// <summary>
    /// The run's Kinah isn't known: its reward picture wasn't read, or its coin slot wasn't. Such a run is left out of the
    /// averages and totals, never counted as 0 (Steven, 2026-10-23: "should NEVER average 176k kinah a run when I'm getting
    /// 600k"). Kinah 0 is only a run whose table showed no coin.
    /// </summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public bool KinahUnknown => KinahNotRead || RewardsNotRead;
    /// <summary>
    /// Partly read (since 2026-10-24): its reward cards were read, at least one received card of them, but some places
    /// weren't (seen only cut by a card's window, a list running past what was seen, a slot no item matches): what was read
    /// is filed and sent, with this flag so the site can say so, and the run stays out of the averages. Distinct from
    /// <see cref="RewardsNotRead"/> (no card read at all: not sent, parked). <see cref="SlotsUnread"/> says how many.
    /// </summary>
    public bool PartlyRead { get; set; }
    /// <summary>Gear pieces whose icon two items share ("one of Elder Gloves / Relic Collector Gloves"): each counted as a piece of unknown gear, never dropped (since 2026-10-24).</summary>
    public int UnknownGear { get; set; }
    /// <summary>Visible slots no item could be named for, each one unknown item (a count not read counts one); among <see cref="SlotsUnread"/> (since 2026-10-24).</summary>
    public int UnknownItems { get; set; }
    /// <summary>What the unknown gear could be, one line a piece ("Legend gear, one of Elder Gloves / Relic Collector Gloves").</summary>
    public List<string> GearNotes { get; set; } = new();
    /// <summary>Slots on the reward picture it was filed from (a read again from the pictures replaces it only when it reads more).</summary>
    public int PictureSlots { get; set; }
    /// <summary>
    /// Not sent: its rewards were not read when it was due to go to the site, so it is parked here (in instances.jsonl,
    /// never back in the queue, never retried every tick) until <c>replay-event-shots --send</c> reads its saved pictures
    /// and sends it (since 2026-10-24; before that such a run was dropped without a word).
    /// </summary>
    public bool Unsent { get; set; }
    /// <summary>The reward pictures kept for the run (file names in reward-shots), what a recovery reads it from.</summary>
    public List<string> Pictures { get; set; } = new();
    /// <summary>Its saved pictures were read once more after it closed (the retry before it is parked), read or not.</summary>
    public bool Retried { get; set; }
    /// <summary>When a run parked as unsent was read from its pictures and sent.</summary>
    public DateTime? SentLate { get; set; }
    /// <summary>
    /// How much of the run the game's own numbers backed up (AccuracyScore.ForRun), worked out as it is handed to a send
    /// and sent as the payload's <c>acc</c> (since 2026-10-24); null on runs from before, and with nothing judged.
    /// </summary>
    public RunAccuracy? Acc { get; set; }

    /// <summary>
    /// When the one-time repair of gear counts ran on this record (2026-10-24): until PR 17 a gear slot whose corner read
    /// "x1K" was filed as 1,000 pieces, so each 1,000 in a gear count was one piece (<see cref="InstanceLog.RepairGear"/>).
    /// Set once, so the repair never runs on the record again.
    /// </summary>
    public DateTime? GearRepaired { get; set; }
    /// <summary>The repaired items' counts as they stood before the repair, by id: what the site was sent.</summary>
    public Dictionary<long, int> RepairedFrom { get; set; } = new();
    /// <summary>The repaired run goes to the site again as a correction of the copy it already has; cleared once sent.</summary>
    public bool Resend { get; set; }

    /// <summary>
    /// What a read of its pictures found, onto this run: the items, the Kinah, the slots not read and the flags. Its times,
    /// witnesses and the chat items set aside stay as they were.
    /// </summary>
    public void TakeRewardsFrom(InstanceRun read)
    {
        Items = new(read.Items); BoundItems = new(read.BoundItems); Bound = read.Bound; Kinah = read.Kinah;
        FromPicture = read.FromPicture; SlotsUnread = read.SlotsUnread; UnknownSlots = new(read.UnknownSlots);
        KinahNotRead = read.KinahNotRead; RewardsNotRead = !read.FromPicture;
        PartlyRead = read.PartlyRead; UnknownGear = read.UnknownGear; UnknownItems = read.UnknownItems; GearNotes = new(read.GearNotes); PictureSlots = read.PictureSlots;
        PaidAt ??= read.PaidAt;
    }

    /// <summary>A copy through its own JSON (what instances.jsonl keeps).</summary>
    public InstanceRun Clone() => JsonSerializer.Deserialize<InstanceRun>(JsonSerializer.Serialize(this))!;

    /// <summary>Reward tables (or event card screens) read into the run, and when the first came (since 2026-10-24, run data).</summary>
    public int Tables { get; set; }
    public DateTime? TableAt { get; set; }
    /// <summary>
    /// The names of the run's synthetic item ids (negative: a name the item list lacks, hashed), written with the run so
    /// they still read as names after a restart, when the name may not have been read again yet (since 2026-10-24).
    /// </summary>
    public Dictionary<long, string> Names { get; set; } = new();

    /// <summary>
    /// Set on load, never written back: a record from before the tracker kept its flags (no FromPicture, no Ended, no
    /// witnesses: chat-counted, before 0.8.18) that recorded Kinah 0, so its Kinah isn't known (the 17:48 Oct 23 Urugugu
    /// run averaged in as a real run). One with Kinah was read by the chat path and stays counted. Shown as "not read",
    /// left out of the averages.
    /// </summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public bool OldUnread { get; set; }
    /// <summary>
    /// Set on load, never written back: why the record looks wrong ("looks like several runs": over 3x the zone's median
    /// length; "not a run": a false run's shape, see <see cref="InstanceLog.FalseRun"/>), or "". Left out of the averages.
    /// </summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public string Suspect { get; set; } = "";
    /// <summary>Left out of the per-run, per-hour and total figures: its Kinah isn't known, or the record looks wrong.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public bool OutOfAverages => KinahUnknown || PartlyRead || OldUnread || Suspect.Length > 0;

    /// <summary>
    /// The run's pieces, not a quantity sum (Steven, 2026-10-24: two pauldrons read "x1K" made "2,000 items"): every piece
    /// of gear one by one, and each other item (materials, consumables) once however big its stack, bound or not. With
    /// <paramref name="isCurrency"/>, currencies (Abyss Points, Enhance Stone, tokens) are no pieces at all: they are
    /// amounts, kept apart (<see cref="Currencies"/>; the 16:02 Hidden Lugi run showed "3,740 items", its Abyss Points and
    /// stones added up). Unknown gear counts with the gear when <paramref name="withUnknown"/>.
    /// </summary>
    public (int Gear, int Materials) Pieces(Func<long, bool> isGear, Func<long, bool>? isCurrency = null, bool withUnknown = false)
    {
        int gear = withUnknown ? UnknownGear : 0; var other = new HashSet<long>();
        foreach (var (id, n) in Items.Concat(BoundItems))
        {
            if (n <= 0) continue;
            if (isGear(id)) gear += n; else if (isCurrency?.Invoke(id) != true) other.Add(id);
        }
        return (gear, other.Count);
    }

    /// <summary>The run's currencies by item, their amounts (bound and tradeable together): never pieces.</summary>
    public Dictionary<long, long> Currencies(Func<long, bool> isCurrency)
    {
        var d = new Dictionary<long, long>();
        foreach (var (id, n) in Items.Concat(BoundItems)) if (n > 0 && isCurrency(id)) d[id] = d.GetValueOrDefault(id) + n;
        return d;
    }

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
/// left (an open-world zone) once its rewards are in; see <see cref="Route"/>. An expedition's run closes at once when the
/// chat says its room was left (<see cref="RoomLeft"/>), and the next one opens only on a real entry (see there). A run with
/// no loot is not kept.</para>
/// </summary>
public sealed class InstanceLog
{
    readonly object _lock = new();
    readonly List<InstanceRun> _done = new();
    /// <summary>Runs closed since this reader started that haven't gone to the site yet.</summary>
    readonly List<InstanceRun> _unshared = new();
    /// <summary>Runs not sent because their rewards were not read (<see cref="InstanceRun.Unsent"/>), kept across restarts.</summary>
    readonly List<InstanceRun> _parked = new();
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
            if (!string.Equals(zone, _area, StringComparison.OrdinalIgnoreCase)) { _prevArea = _area; _area = zone; _areaSince = now; }
            if (kind != null)
            {
                // an area of an expedition is that expedition: walking from one area to the next is the same run
                var name = ZoneLocator.RunName(zone);
                // the chat said the expedition's room was left (RoomLeft): the minimap may name its areas for minutes after
                // (2026-10-24: "Left Urugugu Canyon Dungeon room." at 14:12:53, Auldor's Sanctum until 14:18:06), and such a read
                // opens no run. The next one needs a real entry: the open world on the minimap first, or the chat's countdown
                if (_held is { } held && _cur == null)
                {
                    // ten seconds out in the open world, as for a re-entry (one read of an open-world name is a flicker)
                    if (_heldOut is DateTime o && (now - o).TotalSeconds >= 10)
                    {
                        CrashLog.Note($"in {zone} again after the open world ({o.ToLocalTime():HH:mm:ss}), since the {held.Run} room was left: a new entry");
                        _held = null;
                    }
                    else if (string.Equals(name, held.Run, StringComparison.OrdinalIgnoreCase))
                    {
                        _heldOut = null;
                        if (_heldMoveAt == null && !_heldAreas.Contains(zone, StringComparer.OrdinalIgnoreCase)) _heldMoveAt = now;
                        if (!_heldSaid) { _heldSaid = true; CrashLog.Note($"the minimap names {zone} after the {held.Run} room was left ({held.At.ToLocalTime():HH:mm:ss}): no new run until the open world shows or the chat's countdown begins"); }
                        return;
                    }
                    else
                    {
                        CrashLog.Note($"the minimap names {zone}, not the {held.Run} room that was left: a place of its own");
                        _held = null;
                    }
                }
                // ...until its reward table was read: moving on from there (back to the village, into the canyon) is the end of
                // that run, and the way into the next one (Steven, 2026-10-23: five Urugugu runs from 18:26 to 19:04 were one).
                // A move holds for two reads or 3 s (one read of another name is a flicker of the minimap), and the run takes
                // its late reward lines for 30 s after its table before the next one begins. For an expedition the move alone
                // is not enough since 2026-10-25: the chat's countdown must say the next run began (TrySplitLocked). Once the
                // run was left for the open world, the instance named again is a new entry, never a split (below)
                if (_cur != null && _cur.Kind != WorldBoss && _paidArea != null && _leftAt == null)
                {
                    if (!PaidAreaLocked(zone))
                    {
                        if (_moveAt == null) { _moveAt = now; _moveReads = 0; }
                        _moveReads++;
                        if (_moveReads >= 2 || (now - _moveAt.Value).TotalSeconds >= MoveHoldSeconds) _moveConfirmed = true;
                    }
                    else if (!_moveConfirmed) { _moveAt = null; _moveReads = 0; }
                    TrySplitLocked(now);
                    if (_cur != null && _moveConfirmed) return;   // still the paid run's: late reward lines, or waiting for the next run's countdown
                }
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
                if (_transcendAt is DateTime ta && (now - ta).TotalMinutes <= 5 && _cur.Kind == "Expedition") { _cur.Kind = Transcendence; CrashLog.Note($"{_cur.Label}: a Transcendence (its stage was set {ta.ToLocalTime():HH:mm:ss})"); }
                _transcendAt = null;
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
                // the chat's countdown just before ("Starting in 10s." at 14:19:20, the minimap at 14:19:30, 2026-10-24)
                if (_startAt is DateTime sa && (now - sa).TotalSeconds is >= 0 and <= StartWitnessSeconds && NeedsCountdown(_cur)) Witness(_cur, "chat: starting", sa);
                CrashLog.Note($"{_cur.Kind.ToLowerInvariant()} run started: {_cur.Label}" + (name != zone && zone != _cur.Event ? $" (in {zone})" : "") + (_cur.Start < now ? $", first seen {(now - _cur.Start).TotalSeconds:0} s earlier ({_cur.Witnesses.Split(" · ")[0]})" : ""));
            }
            else if (zone != SessionEngine.UnknownZone)
            {
                // out in the open world after the chat said a room was left: the next read of that expedition, ten seconds on, is
                // a real entry (InstanceLog.Zone above)
                if (_held is { } h0 && _heldOut == null) { _heldOut = now; CrashLog.Note($"out in the open world ({zone}) after the {h0.Run} room was left: its next entry is a run"); }
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
            // moved on after the run's reward table: a late reward line is still that run's, anything else begins the next (an
            // expedition's next run only once the chat's countdown said so, TrySplitLocked)
            if (_moveConfirmed && _tableAt is DateTime tab && _leftAt == null && (!NeedsCountdown(_cur) || _startAt is DateTime st0 && st0 > tab)
                && !(RewardLike(ev, batch) && (ev.Time - tab).TotalSeconds <= RewardLateSeconds))
                SplitLocked("its reward table was read, and the next run's loot came");
            // the run was left and its time for rewards is over: it closes, and this pickup is farming's
            if (_leftAt is { } left && (ev.Time - left).TotalSeconds > RewardGraceSeconds) { CloseLocked("time for rewards over"); return; }
            // a run's items and Kinah come from its reward picture, never the chat (Steven, 2026-10-23: expeditions,
            // Transcendence and events). A chat item or chat Kina is set aside, counted for the log. Until the picture is read
            // it dates the run and its rewards' arrival; after it, nothing from the chat moves the run's end (2026-10-24: the
            // 14:12:48 table's run "ended" at 14:15:52 on an achievement's 9,000 Kina, after the room was left)
            if (PictureOnly(_cur) && ev is ItemAcquired or KinahChanged { Delta: > 0 } && !ev.Via.Contains("reward table"))
            {
                if (ev is ItemAcquired ci) { _cur.ChatItemsSetAside += ci.Count; _cur.ChatItems[ci.ItemId] = _cur.ChatItems.GetValueOrDefault(ci.ItemId) + ci.Count; }
                else if (ev is KinahChanged ck)
                {
                    _cur.ChatKinahSetAside += ck.Delta;
                    CrashLog.Note($"{_cur.Label}: {ck.Delta:N0} Kina{(ck.Bound ? " (bound)" : "")} in the chat set aside, not the run's: its Kinah comes from its reward {(_cur.Kind == "Event" ? "cards" : "table")}");
                }
                if (_cur.Tables == 0) _cur.LastLoot = ev.Time;
                if (batch >= 3) Paid(ev.Time);
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
                // bound Kina too: money to spend all the same (Steven, 2026-10-22: "its straight cash"). In a run counted from its
                // picture that is the table's coin alone (above); the 2026-10-22 19:20:33 "Acquired 20,000 Kina (Bound)." after
                // leaving Urugugu Canyon was "Urugugu Canyon Conquest Clear IV achieved."'s (19:19:04), not the run's
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
            MarkPaidAreaLocked(t);
        }
    }

    /// <summary>
    /// Where the table was read: the area the minimap names, and the one it named just before when that changed moments
    /// earlier. As each Urugugu table shows the title reads "Urugugu Canyon" for a few seconds, then Auldor's Sanctum again
    /// (2026-10-24: 4 to 11 s before every table, 14:12:43 for 14:12:48, 18:28:26 for 18:28:31, Auldor's Sanctum at 18:28:52):
    /// both are where the table was, and going back to the second is no move.
    /// </summary>
    void MarkPaidAreaLocked(DateTime t)
    {
        _tableAt = t; _paidArea = _area;
        _paidBefore = _areaSince is DateTime since && (t - since).TotalSeconds <= PaidFlickerSeconds ? _prevArea : null;
    }

    bool PaidAreaLocked(string zone) =>
        string.Equals(zone, _paidArea, StringComparison.OrdinalIgnoreCase) || (_paidBefore != null && string.Equals(zone, _paidBefore, StringComparison.OrdinalIgnoreCase));

    /// <summary>How soon before a table the minimap's change of area may be its title's flicker (<see cref="MarkPaidAreaLocked"/>).</summary>
    public const double PaidFlickerSeconds = 30;

    /// <summary>
    /// A reward table is about to be read into the open run. When this run's table was read already (a minute or more ago),
    /// this one is the next run's, never added onto the first: the first closes and the next starts, from the minimap's move
    /// out of where that table was when it saw one, else the last change of area since that table (2026-10-23: five tables,
    /// one run of 37:04 with 3,000,000 Kinah). A second table sooner than
    /// another run could end (<see cref="TooSoonForNextRun"/>) is no run's: false, and the caller reads nothing from it
    /// (2026-10-24 19:03:54: the open bag after leaving the dungeon, 85 s after the 19:02:29 table, became "run 23" with no
    /// Kinah and nothing named). True: the table may be read into the open run.
    /// </summary>
    public bool TableStarting(DateTime t)
    {
        lock (_lock)
        {
            // no run open because the chat said the expedition's room was left (RoomLeft): a table soon after is another
            // window, no run's (2026-10-24 19:03:54, the open bag 53 s after "Left Urugugu Canyon Dungeon room."). One later
            // than a run could end means a run came that no entry witness saw (the minimap and the countdown both missed):
            // it opens here, from the minimap's first move since (else its last change of area), and the table is its own, never lost
            if (_cur == null && _held is { } h && _area != null && string.Equals(ZoneLocator.RunName(_area), h.Run, StringComparison.OrdinalIgnoreCase))
            {
                double after = (t - h.At).TotalSeconds, soonestHeld = TooSoonForNextRun(UsualSecondsLocked(h.Run));
                if (after < soonestHeld)
                {
                    CrashLog.Note($"a reward table {after:0} s after the {h.Run} room was left, sooner than another run could end ({soonestHeld:0} s): no run's, nothing read from it");
                    return false;
                }
                var from = _heldMoveAt ?? (_areaSince is DateTime a0 && a0 > h.At ? a0 : t);
                _held = null; _heldOut = null;
                _cur = new InstanceRun { Zone = h.Run, Kind = h.Kind, Start = from };
                _byMinimap = true;
                CrashLog.Note($"{h.Kind.ToLowerInvariant()} run started: {h.Run}, a reward table {after / 60:0.0} min after its room was left with no entry seen (from {from.ToLocalTime():HH:mm:ss}, when the minimap first moved since)");
                return true;
            }
            if (_cur == null || _cur.Kind == WorldBoss || _tableAt is not DateTime prev || (t - prev).TotalSeconds < 60) return true;
            double soonest = TooSoonForNextRun(UsualSecondsLocked(_cur.Zone));
            if ((t - prev).TotalSeconds < soonest)
            {
                CrashLog.Note($"a second reward table {(t - prev).TotalSeconds:0} s after {_cur.Label}'s, sooner than another run could end ({soonest:0} s): not the next run's, nothing read from it");
                return false;
            }
            var zone = _cur.Zone; var kind = _cur.Kind; var ev = _cur.Event; var fam = _cur.Family;
            // from the move out of where the first table was, when the minimap saw one (an expedition's run back to back whose
            // countdown the chat didn't show: the move waited for it, TrySplitLocked); else the last change of area since
            var start = _moveConfirmed && _moveAt is DateTime m0 && m0 > prev ? m0 : _areaSince is DateTime a && a > prev ? a : t;
            CloseLocked("a second reward table: the next run's");
            _cur = new InstanceRun { Zone = zone, Kind = kind, Event = ev, Family = fam, Start = start };
            _byMinimap = true;
            CrashLog.Note($"{kind.ToLowerInvariant()} run started: {_cur.Label} (a second reward table at {t.ToLocalTime():HH:mm:ss})");
            return true;
        }
    }

    /// <summary>The soonest a place's next table can come after one: 90 s, or 40% of its usual run when that is longer.</summary>
    public static double TooSoonForNextRun(double usualSeconds) => Math.Max(SecondTableSeconds, 0.4 * usualSeconds);
    public const double SecondTableSeconds = 90;

    /// <summary>A place's usual run: the median length of its counted records (at least three), or 0.</summary>
    double UsualSecondsLocked(string zone)
    {
        var xs = _done.Where(r => string.Equals(r.Zone, zone, StringComparison.OrdinalIgnoreCase) && !r.OutOfAverages && r.Seconds > 0).Select(r => r.Seconds).ToList();
        return xs.Count < 3 ? 0 : Median(xs);
    }
    string? _area, _prevArea, _paidArea, _paidBefore; DateTime? _areaSince, _tableAt, _moveAt; int _moveReads; bool _moveConfirmed;
    public const double MoveHoldSeconds = 3, RewardLateSeconds = 30;

    /// <summary>The paid run closes and the next opens, from when the move out of the paid area was first seen.</summary>
    void SplitLocked(string why)
    {
        if (_cur == null) return;
        var zone = _cur.Zone; var kind = _cur.Kind; var ev = _cur.Event; var fam = _cur.Family; var start = _moveAt ?? DateTime.UtcNow; var st = _startAt;
        CloseLocked(why);
        _cur = new InstanceRun { Zone = zone, Kind = kind, Event = ev, Family = fam, Start = start };
        _byMinimap = true;
        // an expedition's: the minimap's move and the chat's countdown that made it a run (an event's split keeps none, as before)
        if (NeedsCountdown(_cur)) { Witness(_cur, "minimap", start); if (st is DateTime s && s >= start.AddSeconds(-StartWitnessSeconds)) Witness(_cur, "chat: starting", s); }
        CrashLog.Note($"{kind.ToLowerInvariant()} run started: {_cur.Label} (moved on after the last one's reward table, {start.ToLocalTime():HH:mm:ss}{(NeedsCountdown(_cur) && st is DateTime s2 ? $"; the chat's countdown {s2.ToLocalTime():HH:mm:ss}" : "")})");
    }

    // ---- the chat's dungeon room lines as a run's edges (2026-10-25) ----
    // "Left Urugugu Canyon Dungeon room." prints as the player leaves the expedition's room: every Urugugu run of 2026-10-24
    // that he left ended with it, after its table (14:12:48 table, 14:12:53 the line; 19:02:29, 19:03:01) or partway (17:11:46
    // in, 17:12:21 out). It closes the open run at once: nothing after it is that run's. The minimap may name the expedition's
    // areas for minutes after it (Auldor's Sanctum, 14:12:55 to 14:18:06, which opened "run 27" at 14:12:55 and made it 16:09
    // long), so those reads open no run until a real entry: the minimap naming the open world first, or the chat's countdown
    // ("Starting in 10s.", printed inside the instance as each run begins: 14:19:20 for the 14:19:30 entry, and 54 to 74 s
    // after each table of the five runs back to back on 2026-10-23, with no room line between them). The countdown is also
    // the second witness an expedition's run back to back needs besides the minimap's move (TrySplitLocked).

    /// <summary>The expedition whose room the chat said was left, its kind, and when; null once an entry was seen.</summary>
    (string Run, string Kind, DateTime At)? _held;
    bool _heldSaid;
    /// <summary>When the minimap first named the open world after the room was left (an entry needs ten seconds of it).</summary>
    DateTime? _heldOut;
    /// <summary>The areas the minimap named as the room was left (and as its table showed), and the first read of another of the expedition's since.</summary>
    List<string> _heldAreas = new(); DateTime? _heldMoveAt;
    /// <summary>The chat's last countdown line ("Starting in 9s.").</summary>
    DateTime? _startAt;
    /// <summary>The last run a room line closed: its zone, its kind, from its start to the line.</summary>
    (string Zone, string Kind, DateTime From, DateTime To)? _roomClosed;
    /// <summary>The last room line taken for each expedition, for telling it read again from a new one.</summary>
    readonly Dictionary<string, DateTime> _exits = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>
    /// A run left this soon after entry with no table and no kills never began (2026-10-24 17:11:46 in, 17:12:21 "Left Urugugu
    /// Canyon Dungeon room.", no table, no kills): not kept, never parked as rewards not read.
    /// </summary>
    public const double LeftEarlySeconds = 120;
    /// <summary>The same room line read again this long after the first is that line (19:03:01, again at 19:05:24).</summary>
    public const double ExitEchoSeconds = 180;
    /// <summary>A countdown this long before the minimap's entry (or a move) is that run's.</summary>
    public const double StartWitnessSeconds = 30;

    /// <summary>Expeditions and Transcendence begin each run with the chat's countdown; events, arenas and legions don't.</summary>
    static bool NeedsCountdown(InstanceRun r) => r.Kind is "Expedition" or Transcendence;

    /// <summary>
    /// The paid run closes and the next opens when both witnesses say so (expeditions and Transcendence): the minimap moved
    /// on from where its table was read, and the chat's countdown began after that table (2026-10-24: 17:48:18 table, 17:48:57
    /// Urugugu Village, 17:49:42 "Starting in 1s."). A move alone is an area flicker, no run (18:28:52: Auldor's Sanctum again
    /// after the 18:28:31 table, then the room left at 18:29:04). Events, arenas and legions still split on the move.
    /// </summary>
    void TrySplitLocked(DateTime now)
    {
        if (_cur == null || _cur.Kind == WorldBoss || _tableAt is not DateTime tab || _leftAt != null || (now - tab).TotalSeconds < RewardLateSeconds) return;
        // the move holds: two reads, or no read back in the paid area for 3 s
        bool moved = _moveConfirmed || (_moveAt is DateTime m && (now - m).TotalSeconds >= MoveHoldSeconds);
        if (!moved) return;
        if (NeedsCountdown(_cur) && (_startAt is not DateTime st || st <= tab)) return;
        SplitLocked(NeedsCountdown(_cur) ? $"its reward table was read, then {_area}, and the chat's countdown began" : $"its reward table was read, then {_area}");
    }

    /// <summary>
    /// The chat's "Left &lt;place&gt; Dungeon room." (RoomWitness.LeftRoom): the open expedition run there closes at once,
    /// and until a real entry (the open world on the minimap, or the chat's countdown) the minimap naming that expedition
    /// opens no run (<see cref="HeldHere"/>). A run left within <see cref="LeftEarlySeconds"/> of entry with no table and
    /// no kills is not kept ("left before it began").
    /// </summary>
    public void RoomLeft(string place, DateTime t)
    {
        lock (_lock)
        {
            string? run = _cur is { } c && NeedsCountdown(c) && RoomWitness.SameName(place, c.Zone) ? c.Zone : RoomWitness.Expedition(place);
            if (run == null) { CrashLog.Note($"the chat says the {place} room was left: no expedition on the list by that name, nothing closed"); return; }
            // the same line read again a minute or two later, garbled another way: a run opened since stays open
            if (_exits.TryGetValue(run, out var le) && (t - le).TotalSeconds is >= 0 and <= ExitEchoSeconds && (_cur == null || _cur.Start > le))
            {
                if (_cur != null) CrashLog.Note($"\"Left {run} Dungeon room.\" read again {(t - le).TotalSeconds:0} s after the first: the same line, the run since {_cur.Start.ToLocalTime():HH:mm:ss} stays open");
                return;
            }
            _exits[run] = t;
            var kind = ZoneLocator.KindOf(run) ?? "Expedition";
            // where the minimap was as the room was left: those names read again are no move (a stale minimap)
            var areasNow = new[] { _area, _paidArea, _paidBefore, _areaSince is DateTime s1 && (t - s1).TotalSeconds <= PaidFlickerSeconds ? _prevArea : null }.OfType<string>().ToList();
            bool closes = _cur != null && NeedsCountdown(_cur) && string.Equals(_cur.Zone, run, StringComparison.OrdinalIgnoreCase);
            if (closes && _cur != null)
            {
                kind = _cur.Kind;
                _roomClosed = (_cur.Zone, _cur.Kind, _cur.Start, t);
                double secs = (t - _cur.Start).TotalSeconds;
                if (_cur.Tables == 0 && _cur.Kills == 0 && secs < LeftEarlySeconds)
                {
                    CrashLog.Note($"left before it began: {_cur.Label} {_cur.Start.ToLocalTime():HH:mm:ss}, its room left {secs:0} s after entry with no reward table and no kills ({_cur.ChatItemsSetAside} chat item(s), {_cur.ChatKinahSetAside:N0} Kina set aside); not kept");
                    _cur = null; _byMinimap = false; ResetEdgesLocked();
                }
                else CloseLocked($"its room was left ({t.ToLocalTime():HH:mm:ss}, the chat)");
            }
            // a room of another expedition left meanwhile (2026-10-24 14:16:35, "Left Draupnir Dungeon room.", 11 s after creating
            // it, the minimap still on Auldor's Sanctum): the first hold stands, since the minimap's stale name is that one's
            if (!closes && _held is { } other && !string.Equals(other.Run, run, StringComparison.OrdinalIgnoreCase))
            {
                CrashLog.Note($"the chat says the {run} room was left ({t.ToLocalTime():HH:mm:ss}), no run of it open: the {other.Run} room left at {other.At.ToLocalTime():HH:mm:ss} still holds");
                return;
            }
            _held = (run, kind, t); _heldSaid = false; _heldOut = null; _heldAreas = areasNow; _heldMoveAt = null;
            CrashLog.Note($"the chat says the {run} room was left ({t.ToLocalTime():HH:mm:ss}): the minimap naming it opens no run until the open world shows or the chat's countdown begins");
        }
    }

    /// <summary>
    /// The chat's countdown ("Starting in 10s.", RoomWitness.StartLine): a run beginning. After a room was left it is the
    /// entry (the next read of the expedition opens the run); after a table it is the second witness of a run back to back;
    /// in an open run before its table it is a witness of that run.
    /// </summary>
    public void StartLine(DateTime t)
    {
        lock (_lock)
        {
            bool first = _startAt is not DateTime s0 || (t - s0).TotalSeconds > 15;
            _startAt = t;
            if (_cur == null && _held is { } h)
            {
                _held = null; _heldOut = null;
                CrashLog.Note($"the chat's countdown ({t.ToLocalTime():HH:mm:ss}) after the {h.Run} room was left: an entry, the next read of it opens the run");
                return;
            }
            if (_cur == null || !NeedsCountdown(_cur)) return;
            if (_tableAt is DateTime tab && t > tab) { if (first) CrashLog.Note($"{_cur.Label}: the chat's countdown {(t - tab).TotalSeconds:0} s after its reward table: the next run begins once the minimap has moved on"); TrySplitLocked(t); return; }
            if (_cur.Tables == 0) Witness(_cur, "chat: starting", t);
        }
    }

    /// <summary>
    /// The run a pickup dated <paramref name="t"/> happened in when its room line has closed it already (a chat line the vote
    /// or the rare gate let through after "Left … Dungeon room.": 2026-10-24, the cube's "Divine Canyon Cloak acquired." of
    /// 14:12:48, through after the 14:12:53 line): its zone and kind, or null. Such a pickup is that run's, which counts
    /// nothing from the chat, never farming.
    /// </summary>
    public (string Zone, string Kind)? LeftRunOf(DateTime t)
    {
        lock (_lock) return _roomClosed is { } c && t >= c.From && t <= c.To && (_cur == null || t < _cur.Start) ? (c.Zone, c.Kind) : null;
    }

    /// <summary>
    /// True while the room of the expedition <paramref name="zone"/> is in was left and no entry was seen since: the minimap's
    /// name for it opens no run, so what lands meanwhile is inside with no run, counted nowhere (2026-10-24 14:13:12: 10,000
    /// Kina (Bound), 19 s after the room was left, with the minimap still on Auldor's Sanctum). For the log and the tests.
    /// </summary>
    public bool HeldHere(string? zone)
    {
        lock (_lock) return _cur == null && _held is { } h && zone != null && ZoneLocator.KindOf(zone) is { } k && k != ZoneLocator.Ignored
            && string.Equals(ZoneLocator.RunName(zone), h.Run, StringComparison.OrdinalIgnoreCase);
    }

    void ResetEdgesLocked() { _leftAt = null; _paidArea = null; _paidBefore = null; _tableAt = null; _moveAt = null; _moveReads = 0; _moveConfirmed = false; }

    /// <summary>A reward's kind of pickup: bound, Kinah by the thousand, a burst of three or more, or a reward table's slot.</summary>
    static bool RewardLike(GameEvent ev, int batch) =>
        batch >= 3 || ev.Via.Contains("reward table") || ev is ItemAcquired { Bound: true } || ev is KinahChanged { Bound: true } || ev is KinahChanged { Delta: >= 1000 };


    /// <summary>
    /// The open run is a Transcendence (Shattered Arkanis at a stage: "Shattered Arkanis [Stage 1] | Transcendence", "Set to
    /// Stage 1"): its own kind from here on. The zone stays the dungeon's, so the reward table and the time for rewards after
    /// leaving work as for any expedition (Steven, 2026-10-23: Transcendence rewards come from the reward picture too).
    /// </summary>
    public void MarkTranscendence(DateTime t)
    {
        lock (_lock)
        {
            // printed in town as the room is set up ("Set to Stage 1."), before the run opens: the next expedition run is one
            if (_cur == null) { _transcendAt = t; return; }
            if (_cur.Kind == WorldBoss || _cur.Kind == Transcendence || _cur.Kind == "Event") return;
            _cur.Kind = Transcendence;
            CrashLog.Note($"{_cur.Label}: a Transcendence ({t.ToLocalTime():HH:mm:ss})");
        }
    }
    public const string Transcendence = "Transcendence";

    /// <summary>
    /// Runs counted from the reward picture alone: expeditions and Transcendence (the reward table) and events (their reward
    /// cards, Ocr/EventCards.cs, since 2026-10-24). A run whose picture wasn't read keeps its rewards marked not read.
    /// </summary>
    static bool PictureOnly(InstanceRun r) => r.Kind is "Expedition" or Transcendence or "Event";

    /// <summary>An event's reward screen is up: its rewards are in (read from its cards, Ocr/EventCards.cs).</summary>
    public void EventRewardScreen(DateTime t) { lock (_lock) { if (_cur?.Kind == "Event") Paid(t); } }

    /// <summary>An event's rewards came in the last <paramref name="seconds"/> s (for its reward pictures, every 2 s).</summary>
    public bool EventPaidWithin(DateTime now, double seconds)
    {
        lock (_lock) return _cur is { Kind: "Event", PaidAt: DateTime p } && (now - p).TotalSeconds <= seconds;
    }
    DateTime? _transcendAt;

    /// <summary>
    /// The reward table was read: the open run's items are its slots, and only those. What the chat had put in the run gives
    /// way, bound rewards count as the table shows them, and later chat items don't reach this run.
    /// </summary>
    public void RewardTable(DateTime t, IEnumerable<GameEvent> slots, int unread, IEnumerable<string>? unknown = null, bool kinahUnread = false,
        int unknownItems = 0, IReadOnlyList<string>? unknownGear = null, bool partly = false, int pictureSlots = 0)
    {
        lock (_lock)
        {
            Paid(t);
            if (_cur == null || _cur.Kind == WorldBoss) return;
            MarkPaidAreaLocked(t);   // the run ends when the player moves on from here (0.8.20)
            _cur.Tables++; _cur.TableAt ??= t;
            int chatItems = _cur.Items.Values.Sum() + _cur.Bound;
            _cur.Items.Clear(); _cur.BoundItems.Clear(); _cur.Bound = 0;
            foreach (var ev in slots)
                if (ev is ItemAcquired a)
                {
                    if (a.Bound) { _cur.Bound += a.Count; _cur.BoundItems[a.ItemId] = _cur.BoundItems.GetValueOrDefault(a.ItemId) + a.Count; }
                    else _cur.Items[a.ItemId] = _cur.Items.GetValueOrDefault(a.ItemId) + a.Count;
                    _cur.LastLoot = t;
                }
            _cur.FromPicture = true; _cur.SlotsUnread += unread;
            if (kinahUnread) _cur.KinahNotRead = true;
            if (unknown != null) _cur.UnknownSlots.AddRange(unknown);
            _cur.UnknownItems += unknownItems; _cur.PictureSlots += pictureSlots;
            if (unknownGear != null) { _cur.UnknownGear += unknownGear.Count; _cur.GearNotes.AddRange(unknownGear); }
            if (partly) _cur.PartlyRead = true;
            CrashLog.Note($"{_cur.Label}: counted from the reward {(partly || pictureSlots > 0 ? "cards" : "table")}: {PiecesNote(_cur)} from its slots, {unread} slot(s) not read{(partly ? $" (partly read: {_cur.SlotsUnread} slot(s) not read; sent, out of the averages)" : "")}; the chat's {chatItems} set aside");
        }
    }

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
        ResetEdgesLocked();
        if (_cur != null) _cur.Ended = why;
        if (_cur != null && PictureOnly(_cur) && !_cur.FromPicture)
        {
            _cur.RewardsNotRead = true;
            if (_cur.LastLoot != null) CrashLog.Note($"{_cur.Label}: no reward picture was read, so its rewards are marked not read ({_cur.ChatItemsSetAside} chat item(s) and {_cur.ChatKinahSetAside:N0} Kina from the chat not counted)");
        }
        if (_cur?.LastLoot != null && NothingNamed(_cur))
        {
            // a "reward table" that named nothing and showed no coin: another window read as one, never a run's table
            // (2026-10-24 19:03:54, the open bag after leaving the dungeon: 13 slots, none named, no Kinah)
            CrashLog.Note($"not a run: {_cur.Label} {_cur.Start.ToLocalTime():HH:mm:ss}, its reward picture named nothing and showed no Kinah ({_cur.SlotsUnread} slot(s) not named; {why}); not kept");
            _cur = null; _byMinimap = false;
            return;
        }
        if (_cur?.LastLoot != null && FalseRun(_cur))
        {
            // a reward screen read as the run's table moments after the minimap named the instance (2026-10-24 14:03:25:
            // "Urugugu Village" during the "Starting in 9s" countdown, a Daeva Pass Level Reward read as a 5-slot table at
            // 14:03:29, kept as a 10 s run with 1,008 items and no Kinah): never kept, never sent
            CrashLog.Note($"not a run: {_cur.Label} {_cur.Start.ToLocalTime():HH:mm:ss}, {(_cur.LastLoot.Value - _cur.Start).TotalSeconds:0} s, no Kinah, only the minimap saw it, its one table {((TableTime(_cur) ?? _cur.Start) - _cur.Start).TotalSeconds:0} s after entry ({why}); not kept");
            _cur = null; _byMinimap = false;
            return;
        }
        if (_cur?.LastLoot != null)
        {
            NameIds(_cur);
            _done.Add(_cur);
            _unshared.Add(_cur);
            try { File.AppendAllText(PathOf, JsonSerializer.Serialize(_cur) + "\n"); } catch (Exception) { /* a read-only profile keeps it for this session */ }
            CrashLog.Note($"{_cur.Kind.ToLowerInvariant()} run done: {_cur.Label}, {TimeSpan.FromSeconds(_cur.Seconds):m\\:ss}, {_cur.Kinah:N0} Kinah, {PiecesNote(_cur)}{(_cur.PartlyRead ? $", partly read: {_cur.SlotsUnread} slot(s) not read" : "")} ({why}; {_cur.Witnesses})");
        }
        else if (_cur != null) CrashLog.Note($"{_cur.Kind.ToLowerInvariant()} run closed with no loot, not kept: {_cur.Label} ({why})");
        _cur = null; _byMinimap = false;
    }

    // ---- run data rules (2026-10-24) ----

    /// <summary>The false run's limits: shorter than this, and its one table this soon after entry.</summary>
    public const double FalseRunSeconds = 60, FalseRunTableSeconds = 15;

    /// <summary>
    /// A false run: no Kinah, opened by the minimap and seen by nothing else (a run a second table opened has no witness and
    /// starts at its table, so it never counts as one), under <see cref="FalseRunSeconds"/> from entry to last loot,
    /// and its only reward table read within <see cref="FalseRunTableSeconds"/> of entry. No expedition pays that soon:
    /// it was another reward screen read as a table while the minimap already named the instance. The chat's countdown
    /// ("chat: starting", since 2026-10-25) says a run began, not that a table that soon is its own, so it doesn't count
    /// as another witness here (the 14:03:25 Oct 24 phantom came during the countdown).
    /// </summary>
    public static bool FalseRun(InstanceRun r) =>
        r.Kind != WorldBoss && r.Kinah == 0 && TableTime(r) is DateTime ta && r.LastLoot is DateTime l
        && (l - r.Start).TotalSeconds < FalseRunSeconds && (ta - r.Start).TotalSeconds <= FalseRunTableSeconds
        && r.Witnesses.Length > 0 && r.Witnesses.Split(" · ", StringSplitOptions.RemoveEmptyEntries).All(w => w.StartsWith("minimap ", StringComparison.Ordinal) || w.StartsWith("chat: starting ", StringComparison.Ordinal));

    /// <summary>
    /// When its one reward table was read: <see cref="InstanceRun.TableAt"/>, or for a record from before tables were kept
    /// (before 0.8.27: no Tables, no TableAt) a picture-read run's PaidAt (the 14:03:25 Oct 24 record, paid 4 s after entry).
    /// Null with more than one table, or none.
    /// </summary>
    static DateTime? TableTime(InstanceRun r) =>
        r.Tables == 1 ? r.TableAt : r.Tables == 0 && r.TableAt == null && r.FromPicture && r.Kind != "Event" ? r.PaidAt : null;

    /// <summary>
    /// A reward table that named nothing: an expedition or Transcendence run read from its picture, no Kinah (and its coin
    /// not merely unread), no item named on it. A run's table always shows its coin or names something, so this was another
    /// window read as one (2026-10-24 19:03:54: the open bag after leaving the dungeon, whose item tooltip's "Special Reward
    /// Chest" passed for "Reward collected."; 13 slots, none named). Events are left alone: their cards can pay no Kinah.
    /// </summary>
    public static bool NothingNamed(InstanceRun r) =>
        r.Kind is "Expedition" or Transcendence && r.FromPicture && r.Kinah == 0 && !r.KinahUnknown
        && r.Items.Values.Sum() == 0 && r.BoundItems.Values.Sum() == 0;

    Func<long, string?>? _nameOf;
    ItemDictionary? _dict;

    /// <summary>
    /// A run's rewards in a log line: its pieces (each piece of gear, each other item once, currencies never) and its
    /// currencies by amount ("12 piece(s), 5 gear, 2 unknown gear; currencies: Abyss Points x1,760, Enhance Stone x2,500").
    /// </summary>
    string PiecesNote(InstanceRun r)
    {
        if (_dict is not { } d) return $"{r.Items.Values.Sum() + r.Bound} item(s)";
        bool Cur(long id) => d.Get(id)?.Category == "Currency";
        var (gear, mats) = r.Pieces(d.IsGear, Cur, withUnknown: true);
        var cur = r.Currencies(Cur);
        return $"{gear + mats + r.UnknownItems} piece(s) ({gear} gear{(r.UnknownGear > 0 ? $", {r.UnknownGear} of it unknown" : "")}{(r.UnknownItems > 0 ? $", {r.UnknownItems} unknown item(s)" : "")})"
            + (cur.Count > 0 ? "; currencies: " + string.Join(", ", cur.OrderByDescending(kv => kv.Value).Select(kv => $"{d.NameOf(kv.Key)} x{kv.Value:N0}")) : "");
    }

    /// <summary>
    /// The item list the runs are named from: a closing run keeps the names of its synthetic ids (<see cref="InstanceRun.Names"/>),
    /// and the names kept with the runs already loaded go back into the list, so "item -1222936757" never shows after a restart.
    /// </summary>
    public void UseNames(ItemDictionary items)
    {
        lock (_lock)
        {
            _nameOf = id => items.Get(id) is { } i ? i.Name : null; _dict = items;
            foreach (var r in _done) foreach (var (id, name) in r.Names) items.Remember(id, name);
            RepairGearLocked(items);
        }
    }

    // ---- gear counts inflated by the "x1K" misread (2026-10-24) ----
    // Until PR 17 a gear slot whose corner read "x1K" in a reward table was filed as 1,000 pieces (two Divine Canyon
    // Pauldrons made 2,000; the Events card said "3163 pieces"), and those runs went to the site that way. Gear never
    // stacks, so each 1,000 in a gear count was one piece. On load, once per record, such counts are repaired, the original
    // line is kept in instances.repaired.jsonl, and a run the site already has is sent again as a correction
    // (InstanceRunPayload.correction), which the site takes in place of the counts it holds.

    /// <summary>A gear count this big holds at least one "x1K" misread.</summary>
    public const int GearMisread = 1000;

    /// <summary>Each 1,000 was one piece read "x1K": 3,004 is 3 + 4 = 7.</summary>
    public static int RepairedGearCount(int q) => q % GearMisread + q / GearMisread;

    /// <summary>
    /// Repairs the gear counts of <see cref="GearMisread"/> or more on records not repaired before. <paramref name="gearOf"/>
    /// says whether an item is gear: true, false, or null when it can't be known (no name in the item list or in the record),
    /// and such an item is left as it is. Items that are not gear are never touched. A repaired record is stamped
    /// (<see cref="InstanceRun.GearRepaired"/>), keeps its old counts in <see cref="InstanceRun.RepairedFrom"/>, and is marked to
    /// be sent again (<see cref="InstanceRun.Resend"/>) when the site already has it. One entry per record touched.
    /// </summary>
    internal static List<(InstanceRun Run, List<string> Fixed, List<(long Id, int Count)> Unknown)> RepairGear(IEnumerable<InstanceRun> runs, Func<long, InstanceRun, bool?> gearOf, Func<long, string> nameOf, DateTime now)
    {
        var outp = new List<(InstanceRun, List<string>, List<(long, int)>)>();
        foreach (var r in runs)
        {
            if (r.GearRepaired != null) continue;
            var fixedParts = new List<string>(); var unknown = new List<(long, int)>();
            var before = new Dictionary<long, int>();
            foreach (var (id, n) in r.Items.Concat(r.BoundItems)) before[id] = before.GetValueOrDefault(id) + n;
            foreach (var bound in new[] { false, true })
            {
                var d = bound ? r.BoundItems : r.Items;
                foreach (var (id, q) in d.ToList())
                {
                    if (q < GearMisread) continue;
                    var gear = gearOf(id, r);
                    if (gear == null) { unknown.Add((id, q)); continue; }
                    if (gear == false) continue;
                    int nq = RepairedGearCount(q);
                    d[id] = nq;
                    if (bound) r.Bound = Math.Max(0, r.Bound - (q - nq));
                    r.RepairedFrom[id] = before[id];
                    fixedParts.Add($"{nameOf(id)} {q:N0} -> {nq:N0}");
                }
            }
            if (fixedParts.Count > 0) { r.GearRepaired = now; r.Resend = WentToSite(r); }
            if (fixedParts.Count > 0 || unknown.Count > 0) outp.Add((r, fixedParts, unknown));
        }
        return outp;
    }

    /// <summary>The site has the run: it wasn't parked as unsent, and its rewards were read (or it was sent later from its pictures).</summary>
    static bool WentToSite(InstanceRun r) => !r.Unsent && (r.SentLate != null || !r.KinahUnknown);

    /// <summary>
    /// The repair on the runs loaded from instances.jsonl, with the item list that says what is gear: a real id by the
    /// list's own groups, a synthetic one by its name (the list's, or the one the run saved) matched to a real item.
    /// Repaired records are written back in place (the original lines appended to instances.repaired.jsonl first) and get
    /// one line each in reads.log; an item that can't be known gets "not repaired: unknown item", once ever.
    /// </summary>
    void RepairGearLocked(ItemDictionary items)
    {
        // without the item list nothing can be told gear: the next start that has it repairs
        if (items.AllItems().Count == 0) return;
        bool? GearOf(long id, InstanceRun r)
        {
            if (id > 0) return items.Get(id) != null ? items.IsGear(id) : null;
            var name = items.Get(id)?.Name ?? r.Names.GetValueOrDefault(id);
            if (string.IsNullOrWhiteSpace(name)) return null;
            return items.Find(name) is long real && real > 0 && items.IsGear(real);
        }
        List<(InstanceRun Run, List<string> Fixed, List<(long Id, int Count)> Unknown)> done;
        try { done = RepairGear(_done, GearOf, items.NameOf, DateTime.UtcNow); }
        catch (Exception ex) { CrashLog.Write("repair gear counts", ex); return; }
        if (done.Count == 0) return;
        var repaired = done.Where(x => x.Fixed.Count > 0).Select(x => x.Run).ToList();
        if (repaired.Count > 0) PersistRepairs(repaired);
        foreach (var (r, fx, _) in done)
            if (fx.Count > 0) CrashLog.Note($"run {r.Zone} {r.Start.ToLocalTime():MMM d HH:mm:ss}: gear counts repaired ({string.Join(", ", fx)})" + (r.Resend ? "; to be sent to the site again as a correction" : ""));
        try
        {
            var seenPath = PathOf.Replace(".jsonl", ".flagged.txt");
            var seen = File.Exists(seenPath) ? File.ReadAllLines(seenPath).ToHashSet() : new HashSet<string>();
            var fresh = new List<string>();
            foreach (var (r, _, unk) in done)
            {
                if (unk.Count == 0) continue;
                var key = $"{r.Start.ToUniversalTime():O}|{r.Zone}|gear unknown|{string.Join(",", unk.Select(u => u.Id))}";
                if (!seen.Add(key)) continue;
                fresh.Add(key);
                CrashLog.Note($"run {r.Zone} {r.Start.ToLocalTime():MMM d HH:mm:ss}: not repaired: unknown item ({string.Join(", ", unk.Select(u => $"item {u.Id} x{u.Count:N0}"))})");
            }
            if (fresh.Count > 0) File.AppendAllLines(seenPath, fresh);
        }
        catch (Exception) { /* said again on the next start */ }
    }

    /// <summary>Writes the repaired records over their lines in instances.jsonl, the original lines appended to instances.repaired.jsonl first.</summary>
    void PersistRepairs(List<InstanceRun> runs)
    {
        try
        {
            var path = PathOf;
            if (!File.Exists(path)) return;
            var lines = File.ReadAllLines(path).ToList();
            var backup = new List<string>();
            for (int i = 0; i < lines.Count; i++)
            {
                InstanceRun? k = null;
                try { if (lines[i].Length > 2) k = JsonSerializer.Deserialize<InstanceRun>(lines[i]); } catch (Exception) { }
                if (k == null || k.GearRepaired != null) continue;
                var r = runs.FirstOrDefault(x => x.Start == k.Start && x.Kind == k.Kind);
                if (r == null) continue;
                backup.Add(lines[i]);
                lines[i] = JsonSerializer.Serialize(r);
            }
            if (backup.Count == 0) return;
            File.AppendAllLines(path.Replace(".jsonl", ".repaired.jsonl"), backup);
            var tmp = path + ".tmp";
            File.WriteAllLines(tmp, lines);
            File.Move(tmp, path, overwrite: true);
        }
        catch (Exception ex) { CrashLog.Write("repair gear counts: write", ex); }
    }

    /// <summary>Repaired runs the site holds with the old counts, to send again as corrections, oldest first.</summary>
    public List<InstanceRun> Resends { get { lock (_lock) return _done.Where(r => r.Resend).OrderBy(r => r.Start).ToList(); } }

    /// <summary>A correction reached the site (or can't be sent at all): the run is no longer to be sent again.</summary>
    public void Resent(InstanceRun r) { lock (_lock) { r.Resend = false; RewriteLocked(r); } }

    void NameIds(InstanceRun r)
    {
        if (_nameOf == null) return;
        foreach (var id in r.Items.Keys.Concat(r.BoundItems.Keys).Concat(r.ChatItems.Keys))
            if (id < 0 && !r.Names.ContainsKey(id) && _nameOf(id) is { Length: > 0 } n) r.Names[id] = n;
    }

    /// <summary>A zone's figures this many times its median are not one run.</summary>
    public const double SeveralRunsFactor = 3;

    /// <summary>
    /// Marks the records loaded from instances.jsonl that can't count as they stand, in memory only (the file is never
    /// rewritten for it): <see cref="InstanceRun.OldUnread"/> for a record with no FromPicture, no Ended and no witnesses
    /// that recorded Kinah 0 (one with Kinah was read by the chat path and counts as it is);
    /// "looks like several runs" for one over 3x its zone's median length, or over 3x its median Kinah while also over
    /// 1.5x its median length (the 18:26 Oct 23 record: five Urugugu runs, 37:04 and 3,000,000 Kinah); "not a run" for a
    /// record of a false run's shape (<see cref="FalseRun"/>). The median is taken over the zone's clean records, at
    /// least three of them. Each newly marked record gets one line in reads.log, once ever (instances.flagged.txt).
    /// </summary>
    internal static List<(InstanceRun Run, string Why)> MarkOld(List<InstanceRun> runs)
    {
        var marked = new List<(InstanceRun, string)>();
        foreach (var r in runs)
        {
            if (PictureOnly(r) && !r.FromPicture && r.Ended.Length == 0 && r.Witnesses.Length == 0 && r.Kinah == 0 && !r.KinahUnknown)
            { r.OldUnread = true; marked.Add((r, "a record from before the tracker kept its flags (chat-counted) with Kinah 0: Kinah not read")); }
            if (r.FromPicture && FalseRun(r)) { r.Suspect = "not a run"; marked.Add((r, "not a run: no Kinah, only the minimap saw it, its one table moments after entry")); }
            else if (NothingNamed(r)) { r.Suspect = "not a run"; marked.Add((r, "not a run: its reward picture named nothing and showed no Kinah (another window read as a table)")); }
        }
        foreach (var zone in runs.Where(r => r.Kind != WorldBoss).GroupBy(r => r.Zone, StringComparer.OrdinalIgnoreCase))
        {
            var clean = zone.Where(r => !r.OutOfAverages && r.LastLoot != null).ToList();
            if (clean.Count < 3) continue;
            double medSecs = Median(clean.Select(r => r.Seconds));
            var paid = clean.Where(r => r.Kinah > 0).Select(r => (double)r.Kinah).ToList();
            double medKinah = paid.Count >= 3 ? Median(paid) : 0;
            foreach (var r in zone)
            {
                if (r.Suspect.Length > 0 || r.LastLoot == null) continue;
                bool longRun = r.Seconds > SeveralRunsFactor * medSecs;
                bool richRun = medKinah > 0 && r.Kinah > SeveralRunsFactor * medKinah && r.Seconds > 1.5 * medSecs;
                if (!longRun && !richRun) continue;
                r.Suspect = "looks like several runs";
                marked.Add((r, $"looks like several runs: {TimeSpan.FromSeconds(r.Seconds):m\\:ss} and {r.Kinah:N0} Kinah against the zone's median {TimeSpan.FromSeconds(medSecs):m\\:ss}" + (medKinah > 0 ? $" and {medKinah:N0}" : "")));
            }
        }
        return marked;
    }

    static double Median(IEnumerable<double> xs)
    {
        var l = xs.OrderBy(x => x).ToList();
        if (l.Count == 0) return 0;
        return l.Count % 2 == 1 ? l[l.Count / 2] : (l[l.Count / 2 - 1] + l[l.Count / 2]) / 2;
    }

    /// <summary>
    /// The open run, when it is one: it has loot or a reward table and hasn't closed. A run just opened by a split or a
    /// second table, with nothing in it yet, is not in progress (the card said "in progress" while the player stood in town).
    /// </summary>
    public string? InProgress
    {
        get { lock (_lock) return _cur is { } c && (c.LastLoot != null || c.PaidAt != null || c.Tables > 0) ? c.Zone : null; }
    }

    /// <summary>Every kept run plus the open one when it has loot, oldest first.</summary>
    /// <summary>For tests: the open run, loot or not.</summary>
    internal InstanceRun? CurrentRun { get { lock (_lock) return _cur; } }

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
    /// <summary>The open run's kind ("Expedition", "Transcendence", "Event"), or null.</summary>
    public string? CurrentKind { get { lock (_lock) return _cur?.Kind; } }

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
    /// <summary>Finished runs not sent yet.</summary>
    public int UnsharedCount { get { lock (_lock) return _unshared.Count; } }

    public List<InstanceRun> TakeUnshared(DateTime since)
    {
        lock (_lock)
        {
            var take = _unshared.Where(r => r.LastLoot is { } l && l >= since.AddMinutes(-20)).ToList();
            _unshared.Clear();
            return take;
        }
    }

    /// <summary>
    /// Removes one finished run the player says is wrong (Steven, 2026-10-23: five Urugugu runs saved as one before 0.8.20).
    /// It leaves the list and the history file, is never sent to the site from here on, and is kept aside in
    /// instances.removed.jsonl so it can be put back by hand. The open run can't be removed. True when it was found.
    /// </summary>
    public bool Remove(DateTime start, string zone)
    {
        lock (_lock)
        {
            bool Same(InstanceRun r) => r.Start == start && string.Equals(r.Zone, zone, StringComparison.OrdinalIgnoreCase);
            int gone = _done.RemoveAll(Same);
            _unshared.RemoveAll(Same);
            if (gone == 0) return false;
            try
            {
                if (File.Exists(PathOf))
                {
                    var keep = new List<string>(); var removed = new List<string>();
                    foreach (var line in File.ReadAllLines(PathOf))
                    {
                        InstanceRun? r = null;
                        try { if (line.Length > 2) r = JsonSerializer.Deserialize<InstanceRun>(line); } catch (Exception) { }
                        if (r != null && Same(r)) removed.Add(line); else keep.Add(line);
                    }
                    if (removed.Count > 0)
                    {
                        File.AppendAllLines(PathOf.Replace(".jsonl", ".removed.jsonl"), removed);
                        var tmp = PathOf + ".tmp";
                        File.WriteAllLines(tmp, keep);
                        File.Move(tmp, PathOf, overwrite: true);
                    }
                }
            }
            catch (Exception ex) { CrashLog.Write("remove a run", ex); }
            CrashLog.Note($"run removed by the player: {zone} started {start.ToLocalTime():HH:mm:ss} (kept aside in instances.removed.jsonl)");
            return true;
        }
    }

    /// <summary>
    /// Split by the player (0.8.30, the Events tab's fix sheet, RunFixes.Split): one finished record is replaced by the runs
    /// cut from it at its reward screens, in the list and in instances.jsonl. The record as it was is kept aside as one line
    /// in instances.repaired.jsonl. The parts are not sent to the site from here (the record's own sending already happened
    /// or was let go). True when the record was found.
    /// </summary>
    public bool ReplaceRun(InstanceRun old, IReadOnlyList<InstanceRun> parts)
    {
        if (parts.Count == 0) return false;
        lock (_lock)
        {
            int at = _done.IndexOf(old);
            if (at < 0) return false;
            _done.RemoveAt(at);
            _done.InsertRange(at, parts);
            _unshared.Remove(old);
            try
            {
                File.AppendAllText(PathOf.Replace(".jsonl", ".repaired.jsonl"), JsonSerializer.Serialize(old) + "\n");
                var lines = File.Exists(PathOf) ? File.ReadAllLines(PathOf).ToList() : new List<string>();
                int i = lines.FindIndex(l => { try { return l.Length > 2 && JsonSerializer.Deserialize<InstanceRun>(l) is { } k && k.Start == old.Start && k.Kind == old.Kind && string.Equals(k.Zone, old.Zone, StringComparison.OrdinalIgnoreCase); } catch (Exception) { return false; } });
                var fresh = parts.Select(p => JsonSerializer.Serialize(p)).ToList();
                if (i >= 0) { lines.RemoveAt(i); lines.InsertRange(i, fresh); } else lines.AddRange(fresh);
                var tmp = PathOf + ".tmp";
                File.WriteAllLines(tmp, lines);
                File.Move(tmp, PathOf, overwrite: true);
            }
            catch (Exception ex) { CrashLog.Write("split a run", ex); }
            CrashLog.Note($"run split by the player: {old.Label} started {old.Start.ToLocalTime():MMM d HH:mm:ss} into {parts.Count} runs at its reward screens (the record kept aside in instances.repaired.jsonl)");
            return true;
        }
    }

    // ---- runs whose rewards were not read (2026-10-24) ----
    // A run whose rewards (or its coin) weren't read is never sent (the site would average in the little the chat saw).
    // Before, it left the queue with the rest and was dropped without a line in the log (Hidden Lugi 10:02 and 13:06 that
    // day). Now it is parked: said once in the log, tallied, marked in instances.jsonl with its pictures, and recovered by
    // `replay-event-shots` (Program.cs), which reads its event cards from those pictures and sends it.

    /// <summary>The runs parked as not sent, oldest first.</summary>
    public List<InstanceRun> Parked { get { lock (_lock) return new(_parked); } }

    /// <summary>
    /// Runs not sent because their rewards were not read, started since <paramref name="since"/>: the parked ones, and kept
    /// runs from before parking existed whose rewards were marked not read and that were never sent later.
    /// </summary>
    public List<InstanceRun> Unsent(DateTime since)
    {
        lock (_lock) return _done.Concat(_parked).Distinct().Where(r => r.Start >= since && r.SentLate == null && (r.Unsent || r.KinahUnknown)).OrderBy(r => r.Start).ToList();
    }

    /// <summary>
    /// Runs closed unread whose pictures haven't been read again yet: those in the queue, and on a start those kept from
    /// the last session whose last loot came since <paramref name="since"/> (never parked, never sent, never retried).
    /// Events only: their cards are what a recovery can read.
    /// </summary>
    public List<InstanceRun> ToRetry(DateTime since)
    {
        lock (_lock) return _unshared.Concat(_done.Where(r => r.LastLoot is { } l && l >= since)).Distinct()
            .Where(r => r.Kind == "Event" && (r.KinahUnknown || r.PartlyRead) && !r.Retried && !r.Unsent && r.SentLate == null).ToList();
    }

    /// <summary>
    /// Parks a run whose rewards were not read: one line in the log, <c>runs_unsent</c> tallied, marked in instances.jsonl
    /// with the names of its saved pictures, out of the queue. A run parked already is left as it is.
    /// </summary>
    public void Park(InstanceRun r, IReadOnlyList<string>? pictures = null)
    {
        lock (_lock)
        {
            _unshared.Remove(r);
            if (pictures != null && pictures.Count > 0) r.Pictures = pictures.ToList();
            if (_parked.Contains(r)) return;
            r.Unsent = true;
            _parked.Add(r);
            if (!_done.Contains(r)) _done.Add(r);
            RewriteLocked(r);
            Accuracy.Add("runs_unsent");
            CrashLog.Note($"run not sent: {r.Label} {r.Start.ToLocalTime():HH:mm:ss}: {(r.RewardsNotRead ? "rewards" : "its Kinah")} not read (kept; {r.Pictures.Count} picture(s) saved)");
        }
    }

    /// <summary>A parked or old unread run read from its pictures and sent: it leaves the parked list, marked sent in instances.jsonl.</summary>
    public void SentLater(InstanceRun r, DateTime at)
    {
        lock (_lock)
        {
            r.Unsent = false; r.SentLate = at;
            _parked.Remove(r);
            RewriteLocked(r);
        }
    }

    /// <summary>A kept run changed (read again, parked, sent): its line in instances.jsonl is written again in place.</summary>
    public void Save(InstanceRun r) { lock (_lock) RewriteLocked(r); }

    /// <summary>A run read from its pictures after the queue let it go (a retry on a start): back in the queue to be sent.</summary>
    public void Requeue(InstanceRun r) { lock (_lock) if (!_unshared.Contains(r)) _unshared.Add(r); }

    /// <summary>True when the run waits in the queue to be sent.</summary>
    public bool Queued(InstanceRun r) { lock (_lock) return _unshared.Contains(r); }

    void RewriteLocked(InstanceRun r)
    {
        try
        {
            var path = PathOf;
            var lines = File.Exists(path) ? File.ReadAllLines(path).ToList() : new List<string>();
            bool found = false;
            for (int i = 0; i < lines.Count; i++)
            {
                InstanceRun? k = null;
                try { if (lines[i].Length > 2) k = JsonSerializer.Deserialize<InstanceRun>(lines[i]); } catch (Exception) { }
                if (k == null || k.Start != r.Start || k.Kind != r.Kind) continue;
                lines[i] = JsonSerializer.Serialize(r); found = true;
            }
            if (!found) lines.Add(JsonSerializer.Serialize(r));
            var tmp = path + ".tmp";
            File.WriteAllLines(tmp, lines);
            File.Move(tmp, path, overwrite: true);
        }
        catch (Exception ex) { CrashLog.Write("rewrite a run", ex); }
    }

    /// <summary>
    /// For a read of a closed run's pictures (EventShotRecovery): the run becomes this log's open run, so the reward path
    /// (ApplyGrid and <c>RewardTable</c>) files into it exactly as it would live. Only on a log of its own, with no run open.
    /// </summary>
    internal void Adopt(InstanceRun r)
    {
        lock (_lock)
        {
            if (_cur != null) throw new InvalidOperationException("a run is open");
            ResetEdgesLocked();
            _cur = r; _byMinimap = true;
        }
    }

    /// <summary>The adopted run back, as its pictures left it, without closing it (nothing written, nothing queued).</summary>
    internal InstanceRun? Release()
    {
        lock (_lock)
        {
            var r = _cur; _cur = null; _byMinimap = false;
            ResetEdgesLocked();
            return r;
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
                if (line.Length > 2 && JsonSerializer.Deserialize<InstanceRun>(line) is { } r && r.LastLoot != null) { r.FileUnderFamily(); _done.Add(r); if (r.Unsent) _parked.Add(r); }
        }
        catch (Exception) { /* a damaged history starts over; the file is kept for a look */ }
        // old records that can't count as they stand: marked in memory, said once ever in reads.log, never rewritten
        try
        {
            var marked = MarkOld(_done);
            if (marked.Count == 0) return;
            var seenPath = PathOf.Replace(".jsonl", ".flagged.txt");
            var seen = File.Exists(seenPath) ? File.ReadAllLines(seenPath).ToHashSet() : new HashSet<string>();
            var fresh = new List<string>();
            foreach (var (r, why) in marked)
            {
                var key = $"{r.Start.ToUniversalTime():O}|{r.Zone}|{why.Split(':')[0]}";
                if (!seen.Add(key)) continue;
                fresh.Add(key);
                CrashLog.Note($"old run record: {r.Label} {r.Start.ToLocalTime():MMM d HH:mm:ss}: {why}; left out of the averages (kept as it is in instances.jsonl)");
            }
            if (fresh.Count > 0) File.AppendAllLines(seenPath, fresh);
        }
        catch (Exception) { /* the marks still hold for this session */ }
    }
}
