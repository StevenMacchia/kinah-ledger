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
    readonly ItemDictionary _dict;
    readonly object _lock = new();
    readonly Dictionary<string, DateTime> _seen = new();
    readonly List<PriceAsk> _pending = new();
    readonly List<PriceSale> _sales = new();
    readonly List<PriceExpired> _expired = new();
    readonly HashSet<string> _items = new();
    DateTime _detectAt = DateTime.MinValue, _firstPendingAt = DateTime.MinValue;
    int _emptyReads;
    bool _enabled;

    public MarketCapture(Settings s, ItemDictionary items, Rectangle? manualRegion, string server)
    {
        _cfg = s.Market; _upload = s.Upload; _parser = new MarketParser(items); _dict = items;
        Region = s.Region; Server = server;
        _enabled = s.Market.Enabled;
        if (manualRegion is { } r) { Layout = MarketLayout.FromRegion(r); Manual = true; }
    }

    /// <summary>The window's switch: off means the Market is never read. Can be flipped while a run is on.</summary>
    public bool Enabled { get => Volatile.Read(ref _enabled); set => Volatile.Write(ref _enabled, value); }
    /// <summary>The server as the player typed it ("Siel (Elyos)"); prices mean nothing without one, so none is sent until it's set.</summary>
    public string Server { get; set; }
    public string Region { get; set; }
    public string ServerKey => ServerKeys.Build(Region, Server);
    public MarketLayout? Layout { get; private set; }
    public bool Manual { get; private set; }
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
    public int Pending { get { lock (_lock) return _pending.Count + _sales.Count + _expired.Count; } }
    /// <summary>The player's own sales and expiries read from the chat this session, and how many of those went out.</summary>
    public int SalesSeen { get; private set; }
    public int ExpiredSeen { get; private set; }
    public int SalesShared { get; private set; }
    public DateTime LastReadAt { get; private set; } = DateTime.MinValue;
    public string LastAsk { get; private set; } = "";
    /// <summary>How the screen is read; tests swap in a screenshot.</summary>
    public MarketLocator.Reader Read { get; set; } = MarketLocator.ScreenRead;

    public static readonly Color MarkColor = Color.FromArgb(155, 232, 211);

    /// <summary>Switches between the title-and-headers search (null) and a hand-chosen area, with immediate effect.</summary>
    public void UseRegion(Rectangle? r)
    {
        Layout = r is { } rr ? MarketLayout.FromRegion(rr) : null;
        Manual = r != null;
        Detected = false; _emptyReads = 0; _detectAt = DateTime.MinValue;
    }

    /// <summary>One read: find the table if it isn't known, then read its rows and queue what's new. Called about once a second.</summary>
    public async Task TickAsync(Rectangle game, int scale)
    {
        var now = DateTime.UtcNow;
        var layout = Layout;
        if (layout == null)
        {
            if ((now - _detectAt).TotalSeconds < _cfg.DetectEverySec) return;
            _detectAt = now;
            var (found, note) = await MarketLocator.DetectAsync(game, Read);
            Note = note;
            if (found == null) { Detected = false; return; }
            Layout = layout = found; Detected = true; _emptyReads = 0;
        }
        var rows = await MarketReader.ReadRowsAsync(layout, scale, Read);
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

    /* ---------------- the player's own sales, from the chat ---------------- */

    /// <summary>Listings, sales and expiries from the chat reader. Sales and expiries are queued for sharing (they follow the same switch as asks); listings stay on this PC.</summary>
    public void OnChatEvent(GameEvent ev)
    {
        if (!Enabled) return;
        var now = ev.Time;
        switch (ev)
        {
            case MarketListed l:
                ListingBook.Add(Canonical(l.Item), l.Qty, now);
                break;
            case MarketSold s:
            {
                var item = Canonical(s.Item);
                var listed = ListingBook.Take(item, s.Qty);
                double? hours = listed is DateTime at && now >= at ? Math.Round((now - at).TotalHours, 1) : null;
                long unit = Math.Max(1, (long)Math.Round(s.Total / (double)Math.Max(1, s.Qty)));
                lock (_lock)
                {
                    if (_pending.Count + _sales.Count + _expired.Count == 0) _firstPendingAt = DateTime.UtcNow;
                    if (_sales.Count < 200) _sales.Add(new PriceSale(item, s.Qty, unit, now.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'"), hours));
                }
                SalesSeen++; LastAsk = $"sold {item} ×{s.Qty} @ {unit:N0}";
                break;
            }
            case MarketExpired x:
            {
                var item = Canonical(x.Item);
                ListingBook.Take(item, x.Qty);
                lock (_lock)
                {
                    if (_pending.Count + _sales.Count + _expired.Count == 0) _firstPendingAt = DateTime.UtcNow;
                    if (_expired.Count < 200) _expired.Add(new PriceExpired(item, x.Qty, now.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'")));
                }
                ExpiredSeen++;
                break;
            }
        }
    }

    /// <summary>The name asks for the same item go out under, so a sale and an ask land on the same row of the site's board.</summary>
    string Canonical(string name) => _dict.NameOf(_dict.IdOf(name));

    public bool FlushDue
    {
        get
        {
            lock (_lock)
            {
                int n = _pending.Count + _sales.Count + _expired.Count;
                return n > 0 && (_pending.Count >= _cfg.BatchSize || (DateTime.UtcNow - _firstPendingAt).TotalSeconds >= _cfg.FlushSec);
            }
        }
    }

    /// <summary>Sends one batch of queued asks, sales and expiries. Without an endpoint or a server the batch is dropped and counted, never kept around.</summary>
    public async Task<bool> FlushAsync()
    {
        List<PriceAsk> batch; List<PriceSale> sales; List<PriceExpired> expired;
        lock (_lock)
        {
            if (_pending.Count + _sales.Count + _expired.Count == 0) return false;
            batch = _pending.Take(_cfg.BatchSize).ToList(); sales = _sales.ToList(); expired = _expired.ToList();
        }
        if (string.IsNullOrWhiteSpace(_upload.PricesUrl) || string.IsNullOrWhiteSpace(Server))
        {
            lock (_lock) { _pending.RemoveRange(0, batch.Count); _sales.Clear(); _expired.Clear(); NotSent += batch.Count + sales.Count + expired.Count; _firstPendingAt = DateTime.UtcNow; }
            return false;
        }
        var ok = await Uploader.SendPricesAsync(new PriceBatch(RunPayload.AppVersion, ServerKey, InstallId.Get(), batch, sales.Count > 0 ? sales : null, expired.Count > 0 ? expired : null), _upload);
        lock (_lock)
        {
            if (ok)
            {
                _pending.RemoveRange(0, batch.Count); _sales.RemoveRange(0, sales.Count); _expired.RemoveRange(0, expired.Count);
                Shared += batch.Count; SalesShared += sales.Count; LastError = "";
            }
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
            if (SalesSeen + ExpiredSeen > 0) counts += $" · your sales: {SalesSeen:N0} sold, {ExpiredSeen:N0} expired";
            string last = LastReadAt == DateTime.MinValue ? "" : " · last read " + Ago(LastReadAt);
            string why = AsksSeen > 0 && Shared == 0
                ? string.IsNullOrWhiteSpace(Server) ? " · choose your server to share" : string.IsNullOrWhiteSpace(_upload.PricesUrl) ? " · no sharing address in settings.json" : LastError.Length > 0 ? " · " + LastError : ""
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
