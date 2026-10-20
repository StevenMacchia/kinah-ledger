using System.Drawing;
using KinahLedger.Data;
using KinahLedger.Ocr;
using KinahLedger.Upload;

namespace KinahLedger.Session;

/// <summary>
/// Reads the Market's listings while the Market window is on screen, at most once a second, and queues each new ask
/// (item, stack, unit price) for sharing. The same ask seen again within the repeat window isn't queued twice.
/// Only the screen is read: nothing is clicked or typed, and seller names never reach anything.
/// </summary>
public sealed class MarketCapture
{
    readonly MarketSettings _cfg;
    readonly UploadSettings _upload;
    readonly MarketParser _parser;
    readonly object _lock = new();
    readonly Dictionary<string, DateTime> _seen = new();
    readonly List<PriceAsk> _pending = new();
    readonly HashSet<string> _items = new();
    DateTime _detectAt = DateTime.MinValue, _firstPendingAt = DateTime.MinValue;
    int _emptyReads;
    bool _enabled;

    public MarketCapture(Settings s, ItemDictionary items, Rectangle? manualRegion, string server)
    {
        _cfg = s.Market; _upload = s.Upload; _parser = new MarketParser(items);
        Region = s.Region; Server = server;
        _enabled = s.Market.Enabled;
        if (manualRegion is { } r) { Layout = MarketLayout.FromRegion(r); Manual = true; }
    }

    /// <summary>The window's switch: off means the Market is never read. Can be flipped while a run is on.</summary>
    public bool Enabled { get => Volatile.Read(ref _enabled); set => Volatile.Write(ref _enabled, value); }
    /// <summary>The server as the player typed it ("Siel (Elyos)"); prices mean nothing without one, so none is sent until it's set.</summary>
    public string Server { get; set; }
    public string Region { get; }
    public string ServerKey => ServerKeys.Build(Region, Server);
    public MarketLayout? Layout { get; private set; }
    public bool Manual { get; }
    /// <summary>True while listings are being read: the table was found (auto) or the chosen area is showing listings (manual).</summary>
    public bool Detected { get; private set; }
    public string Note { get; private set; } = "";
    public string LastError { get; set; } = "";
    public long Reads { get; private set; }
    /// <summary>Every row that parsed, repeats included.</summary>
    public int AsksRead { get; private set; }
    /// <summary>New asks after de-duplication: what was queued.</summary>
    public int AsksSeen { get; private set; }
    public int Shared { get; private set; }
    /// <summary>Queued asks that couldn't be sent because there is no endpoint or no server yet.</summary>
    public int NotSent { get; private set; }
    public int Items { get { lock (_lock) return _items.Count; } }
    public int Pending { get { lock (_lock) return _pending.Count; } }
    public DateTime LastReadAt { get; private set; } = DateTime.MinValue;
    public string LastAsk { get; private set; } = "";
    /// <summary>How the screen is read; tests swap in a screenshot.</summary>
    public MarketLocator.Reader Read { get; set; } = MarketLocator.ScreenRead;

    public static readonly Color MarkColor = Color.FromArgb(155, 232, 211);

    /// <summary>One read: find the table if it isn't known, then read its rows and queue what's new. Called about once a second.</summary>
    public async Task TickAsync(Rectangle game, int scale)
    {
        var now = DateTime.UtcNow;
        if (Layout == null)
        {
            if ((now - _detectAt).TotalSeconds < _cfg.DetectEverySec) return;
            _detectAt = now;
            var (layout, note) = await MarketLocator.DetectAsync(game, Read);
            Note = note;
            if (layout == null) { Detected = false; return; }
            Layout = layout; Detected = true; _emptyReads = 0;
        }
        var rows = await MarketReader.ReadRowsAsync(Layout, scale, Read);
        Reads++; LastReadAt = DateTime.UtcNow;
        int parsed = 0;
        foreach (var row in rows)
        {
            var ask = _parser.Parse(row);
            if (ask == null) continue;
            parsed++; AsksRead++;
            bool fresh;
            lock (_lock)
            {
                fresh = !_seen.TryGetValue(ask.Key, out var at) || (now - at).TotalMinutes >= _cfg.RepeatMinutes;
                if (fresh)
                {
                    _seen[ask.Key] = now;
                    _items.Add(ask.Item);
                    if (_pending.Count == 0) _firstPendingAt = now;
                    if (_pending.Count < 2000) _pending.Add(new PriceAsk(ask.Item, ask.UnitPrice, ask.Qty, now.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'")));
                }
            }
            if (fresh) { AsksSeen++; LastAsk = ask.ToString(); }
        }
        if (parsed == 0)
        {
            // the Market closed or moved: after a few empty reads go back to looking for it (a chosen area just waits)
            if (++_emptyReads >= _cfg.LostAfterReads) { Detected = false; if (!Manual) Layout = null; }
        }
        else { _emptyReads = 0; Detected = true; }
        if (Reads % 60 == 0)
            lock (_lock) foreach (var k in _seen.Where(kv => (now - kv.Value).TotalMinutes >= _cfg.RepeatMinutes).Select(kv => kv.Key).ToList()) _seen.Remove(k);
    }

    public bool FlushDue
    {
        get { lock (_lock) return _pending.Count > 0 && (_pending.Count >= _cfg.BatchSize || (DateTime.UtcNow - _firstPendingAt).TotalSeconds >= _cfg.FlushSec); }
    }

    /// <summary>Sends one batch of queued asks. Without an endpoint or a server the batch is dropped and counted, never kept around.</summary>
    public async Task<bool> FlushAsync()
    {
        List<PriceAsk> batch;
        lock (_lock) { if (_pending.Count == 0) return false; batch = _pending.Take(_cfg.BatchSize).ToList(); }
        if (string.IsNullOrWhiteSpace(_upload.PricesUrl) || string.IsNullOrWhiteSpace(Server))
        {
            lock (_lock) { _pending.RemoveRange(0, batch.Count); NotSent += batch.Count; _firstPendingAt = DateTime.UtcNow; }
            return false;
        }
        var ok = await Uploader.SendPricesAsync(new PriceBatch(RunPayload.AppVersion, ServerKey, InstallId.Get(), batch), _upload);
        lock (_lock)
        {
            if (ok) { _pending.RemoveRange(0, batch.Count); Shared += batch.Count; LastError = ""; }
            else
            {
                LastError = "couldn't reach the site";
                _firstPendingAt = DateTime.UtcNow;
                if (_pending.Count > 500) _pending.RemoveRange(0, _pending.Count - 500);   // keep the newest for the retry
            }
        }
        return ok;
    }

    /// <summary>One line for the window: what the reader is doing and what it has shared this session.</summary>
    public string Status
    {
        get
        {
            if (!Enabled) return "Off. The Market is never read; everything else keeps working.";
            string state = Detected ? "Market open" : Manual ? "Watching the chosen area" : "Watching for the Market window";
            string counts = AsksSeen == 0 ? "nothing read yet" : $"{AsksSeen:N0} ask{(AsksSeen == 1 ? "" : "s")} read · {Items:N0} item{(Items == 1 ? "" : "s")} · {Shared:N0} shared";
            string last = LastReadAt == DateTime.MinValue ? "" : " · last read " + Ago(LastReadAt);
            string why = AsksSeen > 0 && Shared == 0
                ? string.IsNullOrWhiteSpace(Server) ? " · type your server to share" : string.IsNullOrWhiteSpace(_upload.PricesUrl) ? " · sharing isn't live yet" : LastError.Length > 0 ? " · " + LastError : ""
                : "";
            return $"{state} · {counts}{last}{why}";
        }
    }

    static string Ago(DateTime at)
    {
        var s = (DateTime.UtcNow - at).TotalSeconds;
        return s < 60 ? $"{Math.Max(0, (int)s)} s ago" : s < 3600 ? $"{(int)(s / 60)} min ago" : $"{(int)(s / 3600)} h ago";
    }
}
