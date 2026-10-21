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

    /// <summary>The zone just read (null: nothing new to say).</summary>
    public void Zone(string? zone, DateTime now)
    {
        if (zone == null) return;
        lock (_lock)
        {
            var kind = ZoneLocator.KindOf(zone);
            if (kind != null)
            {
                if (_cur != null && string.Equals(_cur.Zone, zone, StringComparison.OrdinalIgnoreCase)) return;
                CloseLocked();
                _cur = new InstanceRun { Zone = zone, Kind = kind, Start = now };
                CrashLog.Note($"{kind.ToLowerInvariant()} run started: {zone}");
            }
            else if (zone != SessionEngine.UnknownZone) CloseLocked();
        }
    }

    /// <summary>An event as it was counted (rewards already turned into rewards): loot and kills go to the open run.</summary>
    public void Loot(GameEvent ev)
    {
        lock (_lock)
        {
            if (_cur == null) return;
            switch (ev)
            {
                case ItemAcquired a:
                    if (a.Bound) _cur.Bound += a.Count; else _cur.Items[a.ItemId] = _cur.Items.GetValueOrDefault(a.ItemId) + a.Count;
                    _cur.LastLoot = a.Time;
                    break;
                case KinahChanged { Bound: false } k when k.Delta > 0:
                    _cur.Kinah += k.Delta; _cur.LastLoot = k.Time;
                    break;
                case MobKilled or CurrencyGained:
                    _cur.Kills++;
                    break;
            }
        }
    }

    /// <summary>Closes the open run (the reader is stopping).</summary>
    public void Close() { lock (_lock) CloseLocked(); }

    void CloseLocked()
    {
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
