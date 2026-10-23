using System.Text.Json;
using KinahLedger.Ocr;

namespace KinahLedger.Session;

/// <summary>
/// One trip into an expedition, an event area, an arena or a legion instance: from the moment the minimap named it to the
/// last loot picked up there. The time after the final reward (walking out, waiting) is not part of the run.
/// </summary>
public sealed class InstanceRun
{
    public string Zone { get; set; } = "";
    public string Kind { get; set; } = "";
    public DateTime Start { get; set; }
    public DateTime? LastLoot { get; set; }
    public long Kinah { get; set; }
    public int Kills { get; set; }
    /// <summary>Items picked up in the run, tradeable ones, by item id (bound ones can't be sold, so they carry no value).</summary>
    public Dictionary<long, int> Items { get; set; } = new();
    public int Bound { get; set; }
    /// <summary>The bound items by id, so a run's drop list shows them too (they still carry no value). Since 0.8.5.</summary>
    public Dictionary<long, int> BoundItems { get; set; } = new();
    /// <summary>Entry to last loot, never under ten seconds.</summary>
    public double Seconds => LastLoot is { } l ? Math.Max(10, (l - Start).TotalSeconds) : 0;
}

/// <summary>
/// The runs in expeditions and events, kept on this PC across restarts (instances.jsonl beside the logs), so an event
/// that comes round once an hour still builds up an average. A run opens when the minimap names an instance and closes
/// when it names an open-world zone or another instance; Unknown zone (an inner area the list lacks) keeps it open.
/// A run with no loot is not kept.
/// </summary>
public sealed class InstanceLog
{
    readonly object _lock = new();
    readonly List<InstanceRun> _done = new();
    /// <summary>Runs closed since this reader started that haven't gone to the site yet.</summary>
    readonly List<InstanceRun> _unshared = new();
    InstanceRun? _cur;
    static string PathOf => System.IO.Path.Combine(System.IO.Path.GetDirectoryName(CrashLog.ReadsPath)!, "instances.jsonl");

    public InstanceLog() { Load(); }

    /// <summary>
    /// An event's rewards print a little after the game has moved the player out (Steven, 2026-10-23, Hidden Lugi: back in
    /// Ruin Exploration Campsite at 11:07:23, the rewards in the chat at 11:07:34). For this long after the minimap names an
    /// open-world zone, what is picked up is still the run's; then the run closes.
    /// </summary>
    public const double RewardGraceSeconds = 30;
    DateTime? _leftAt;

    /// <summary>True while the run just left is still taking its rewards (<see cref="RewardGraceSeconds"/> after leaving), for a pickup at <paramref name="t"/>.</summary>
    public bool Ending(DateTime t)
    {
        lock (_lock) return _cur != null && _leftAt is { } l && (t - l).TotalSeconds is >= -5 and <= RewardGraceSeconds;
    }

    /// <summary>The zone just read (null: nothing new to say).</summary>
    public void Zone(string? zone, DateTime now)
    {
        if (zone == null) return;
        lock (_lock)
        {
            var kind = ZoneLocator.KindOf(zone);
            // an area left alone (Nightmare: the same rewards every time): no run, and the one before ends here
            if (kind == ZoneLocator.Ignored) { CloseLocked(); return; }
            if (kind != null)
            {
                // an area of an expedition is that expedition: walking from one area to the next is the same run
                var name = ZoneLocator.RunName(zone);
                if (_cur != null && string.Equals(_cur.Zone, name, StringComparison.OrdinalIgnoreCase)) { _leftAt = null; return; }
                CloseLocked();
                _cur = new InstanceRun { Zone = name, Kind = ZoneLocator.KindOf(name) ?? kind, Start = now };
                CrashLog.Note($"{_cur.Kind.ToLowerInvariant()} run started: {name}" + (name != zone ? $" (in {zone})" : ""));
            }
            else if (zone != SessionEngine.UnknownZone)
            {
                // out in the open world: an event or expedition run takes its rewards for a little longer before it closes
                if (_cur != null && _cur.Kind != WorldBoss)
                {
                    if (_leftAt == null) { _leftAt = now; CrashLog.Note($"left {_cur.Zone}: what lands in the next {RewardGraceSeconds:0} s is still its rewards"); return; }
                    if ((now - _leftAt.Value).TotalSeconds <= RewardGraceSeconds) return;
                }
                CloseLocked();
            }
        }
    }

    /// <summary>An event as it was counted (rewards already turned into rewards): loot and kills go to the open run.</summary>
    public void Loot(GameEvent ev)
    {
        lock (_lock)
        {
            if (_cur == null) return;
            // the run was left and its time for rewards is over: it closes, and this pickup is farming's
            if (_leftAt is { } left && (ev.Time - left).TotalSeconds > RewardGraceSeconds) { CloseLocked(); return; }
            switch (ev)
            {
                case ItemAcquired a:
                    if (a.Bound) { _cur.Bound += a.Count; _cur.BoundItems[a.ItemId] = _cur.BoundItems.GetValueOrDefault(a.ItemId) + a.Count; }
                    else _cur.Items[a.ItemId] = _cur.Items.GetValueOrDefault(a.ItemId) + a.Count;
                    _cur.LastLoot = a.Time;
                    break;
                // bound Kina too: an expedition's or a Transcendence's end reward ("Acquired 20,000 Kina (Bound).") is money to
                // spend all the same (Steven, 2026-10-22: "its straight cash")
                case KinahChanged k when k.Delta > 0:
                    _cur.Kinah += k.Delta; _cur.LastLoot = k.Time;
                    break;
                // a world boss run is one kill, the boss's: the enemies dying around it are not
                case MobKilled when _cur.Kind != WorldBoss:
                case CurrencyGained c when _cur.Kind != WorldBoss && c.Name.EndsWith("Soul Points", StringComparison.OrdinalIgnoreCase):
                    _cur.Kills++;
                    break;
            }
        }
    }

    /// <summary>Closes the open run (the reader is stopping).</summary>
    public void Close() { lock (_lock) CloseLocked(); }

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
            CloseLocked();
            _cur = new InstanceRun { Zone = boss, Kind = WorldBoss, Start = now, Kills = 1 };
            CrashLog.Note($"world boss run started: {boss}");
            return true;
        }
    }

    /// <summary>The world boss's loot window is over: its run closes. Any other open run is left alone.</summary>
    public void EndBoss() { lock (_lock) if (_cur?.Kind == WorldBoss) CloseLocked(); }

    void CloseLocked()
    {
        _leftAt = null;
        if (_cur?.LastLoot != null)
        {
            _done.Add(_cur);
            _unshared.Add(_cur);
            try { File.AppendAllText(PathOf, JsonSerializer.Serialize(_cur) + "\n"); } catch (Exception) { /* a read-only profile keeps it for this session */ }
            CrashLog.Note($"{_cur.Kind.ToLowerInvariant()} run done: {_cur.Zone}, {TimeSpan.FromSeconds(_cur.Seconds):m\\:ss}, {_cur.Kinah:N0} Kinah, {_cur.Items.Values.Sum()} items");
        }
        _cur = null;
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
                if (line.Length > 2 && JsonSerializer.Deserialize<InstanceRun>(line) is { } r && r.LastLoot != null) _done.Add(r);
        }
        catch (Exception) { /* a damaged history starts over; the file is kept for a look */ }
    }
}
