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
    /// <summary>No send before this: the site answered 429 and said how long to wait (a minute when it didn't say).</summary>
    DateTime _holdUntil = DateTime.MinValue;
    int _emptyReads;
    bool _enabled;

    public MarketCapture(Settings s, ItemDictionary items, Rectangle? manualRegion, string server)
    {
        _cfg = s.Market; _upload = s.Upload; _parser = new MarketParser(items); _dict = items;
        Region = s.Region; Server = server;
        _enabled = s.Market.Enabled;
        if (manualRegion is { } r) { Layout = MarketLayout.FromRegion(r); Manual = true; }
    }

    /// <summary>
    /// The Market reading switch (settings.json market.enabled): off means the Market is never read. Reading is for this
    /// PC too (the player's own asks price their drops at once), so it stays on with sharing off; only queueing for the
    /// site follows the Share switch (<see cref="Sharing"/>). Can be flipped while a run is on.
    /// </summary>
    public bool Enabled { get => Volatile.Read(ref _enabled); set => Volatile.Write(ref _enabled, value); }
    /// <summary>The server as the player typed it ("Siel (Elyos)"); prices mean nothing without one, so none is sent until it's set.</summary>
    public string Server { get; set; }
    public string Region { get; set; }
    public string ServerKey => ServerKeys.Build(Region, Server);
    /// <summary>The Share switch: asks, sales and typed prices are queued for the site only while it is on.</summary>
    public bool Sharing => _upload.Enabled;
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
    /// <summary>Asks, sales and expiries the site refused (a 4xx other than 408 and 429): dropped, never sent again.</summary>
    public int Refused { get; private set; }
    /// <summary>Asks read but never queued because the site couldn't take them (a name longer than it accepts, say).</summary>
    public int Unshareable { get; private set; }
    /// <summary>How each batch goes out; tests swap in a fake site.</summary>
    public Func<PriceBatch, UploadSettings, Task<SendResult>> Send { get; set; } = Uploader.SendPricesAsync;
    public int Items { get { lock (_lock) return _items.Count; } }
    public int Pending { get { lock (_lock) return _pending.Count + _sales.Count + _expired.Count; } }
    /// <summary>The player's own sales and expiries read from the chat this session, and how many of those went out.</summary>
    public int SalesSeen { get; private set; }
    public int ExpiredSeen { get; private set; }
    public int SalesShared { get; private set; }
    public DateTime LastReadAt { get; private set; } = DateTime.MinValue;
    /// <summary>When a read last found listings on screen: the Market is open (the activity says so for a few seconds after).</summary>
    public DateTime SeenAt { get; private set; } = DateTime.MinValue;
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
            PriceBook.Observe(ask.Item, ask.UnitPrice, now);   // the window values drops at the lowest ask seen
            bool fresh;
            lock (_lock)
            {
                fresh = !_seen.TryGetValue(ask.Key, out var at) || (now - at).TotalMinutes >= _cfg.RepeatMinutes;
                if (fresh && Sharing)
                {
                    _seen[ask.Key] = now;
                    _items.Add(ask.Item);
                    Enqueue(ask.Item, ask.UnitPrice, ask.Qty, now);
                }
            }
            if (fresh) { AsksSeen++; LastAsk = ask.ToString(); }
        }
        if (parsed == 0)
        {
            // the Market closed or moved: after a few empty reads go back to looking for it (a chosen area just waits)
            if (++_emptyReads >= _cfg.LostAfterReads) { Detected = false; if (!Manual) Layout = null; }
        }
        else { _emptyReads = 0; Detected = true; SeenAt = now; }
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
            case MarketSold s when Sharing:
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
            case MarketExpired x when Sharing:
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

    /// <summary>Queues one ask for the site, under the lock, when the site can take it; one it can't is counted, never queued.</summary>
    void Enqueue(string item, long unitPrice, int qty, DateTime now)
    {
        if (!Shareable(item, unitPrice, qty)) { Unshareable++; return; }
        if (_pending.Count == 0) _firstPendingAt = now;
        if (_pending.Count < 2000) _pending.Add(new PriceAsk(item.Trim(), unitPrice, qty, now.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'")));
    }

    /// <summary>The site's own limits for one ask (backend validate.ts, priceAsk): a name of 1 to 64 characters with no control characters, a price of 1 to 10^12, a stack of 1 to 100,000.</summary>
    public const int MaxItemLength = 64;
    public const long MaxUnitPrice = 1_000_000_000_000;
    public const int MaxQty = 100_000;

    /// <summary>
    /// Whether the site would take this ask. A Market row read whole as the name ("0 6576 0 6,576 2d 23h …", well over 64
    /// characters) made every batch it rode in a 400 (2026-10-28); such an ask stays on this PC.
    /// </summary>
    public static bool Shareable(string? item, long unitPrice, int qty)
    {
        var t = (item ?? "").Trim();
        return t.Length is >= 1 and <= MaxItemLength && !t.Any(char.IsControl)
            && unitPrice is >= 1 and <= MaxUnitPrice && qty is >= 1 and <= MaxQty;
    }

    /// <summary>A price the player typed on a card ("what I see in the game"), queued as their read when sharing is on.</summary>
    public void QueueTyped(string item, long unitPrice)
    {
        if (!Sharing || unitPrice <= 0 || string.IsNullOrWhiteSpace(item)) return;
        var now = DateTime.UtcNow;
        lock (_lock)
        {
            Enqueue(Canonical(item), unitPrice, 1, now);
        }
    }

    public bool FlushDue
    {
        get
        {
            lock (_lock)
            {
                int n = _pending.Count + _sales.Count + _expired.Count;
                if (DateTime.UtcNow < _holdUntil) return false;   // the site asked to wait (429)
                return n > 0 && (_pending.Count >= _cfg.BatchSize || (DateTime.UtcNow - _firstPendingAt).TotalSeconds >= _cfg.FlushSec);
            }
        }
    }

    /// <summary>Sends one batch of queued asks, sales and expiries. Without an endpoint or a server the batch is dropped and counted, never kept around.</summary>
    // the watcher's loop and the Share button can both flush; one at a time, so a batch never goes out twice
    readonly SemaphoreSlim _flushGate = new(1, 1);

    public async Task<bool> FlushAsync()
    {
        await _flushGate.WaitAsync();
        try { return await FlushCoreAsync(); }
        finally { _flushGate.Release(); }
    }

    /// <summary>
    /// The Share Market prices button: finds the Market on screen now, reads every row showing, queues them all (even ones
    /// shared minutes ago: the player asked) and sends them straight away. The site lists them as the server's current
    /// prices, ahead of scraped data. Returns what was read and whether it went out, with a plain note when it didn't.
    /// </summary>
    public async Task<(int Asks, int Items, bool Sent, string Note)> ShareNowAsync(Rectangle game, int scale)
    {
        var layout = Layout;
        if (layout == null || (!Detected && !Manual))
        {
            var (found, note) = await MarketLocator.DetectAsync(game, Read);
            Note = note;
            if (found == null) return (0, 0, false, "The Market isn't open on screen. Open it in the game, then press Share again.");
            Layout = layout = found; Detected = true; _emptyReads = 0;
        }
        var rows = await MarketReader.ReadRowsAsync(layout, scale, Read);
        Reads++; LastReadAt = DateTime.UtcNow;
        var now = DateTime.UtcNow; int n = 0; var names = new HashSet<string>();
        foreach (var row in rows)
        {
            var ask = _parser.Parse(row);
            if (ask == null) continue;
            n++; AsksRead++; names.Add(ask.Item);
            PriceBook.Observe(ask.Item, ask.UnitPrice, now);
            lock (_lock)
            {
                _seen[ask.Key] = now; _items.Add(ask.Item);
                Enqueue(ask.Item, ask.UnitPrice, ask.Qty, now);
            }
            AsksSeen++; LastAsk = ask.ToString();
        }
        if (n == 0)
        {
            if (!Manual) { Layout = null; Detected = false; }
            return (0, 0, false, "No prices could be read. Open a Market page with listings showing, then press Share again.");
        }
        bool sent = true;
        for (int i = 0; i < 20 && Pending > 0; i++) if (!await FlushAsync()) { sent = false; break; }
        return (n, names.Count, sent, sent ? "" : LastError.Length > 0 ? LastError : "nothing could be sent");
    }

    async Task<bool> FlushCoreAsync()
    {
        List<PriceAsk> batch; List<PriceSale> sales; List<PriceExpired> expired;
        lock (_lock)
        {
            if (_pending.Count + _sales.Count + _expired.Count == 0) return false;
            if (DateTime.UtcNow < _holdUntil) return false;   // the site asked to wait (429); LastError says how long
            batch = _pending.Take(_cfg.BatchSize).ToList(); sales = _sales.ToList(); expired = _expired.ToList();
        }
        if (string.IsNullOrWhiteSpace(_upload.PricesUrl) || string.IsNullOrWhiteSpace(Server))
        {
            lock (_lock) { _pending.RemoveRange(0, batch.Count); _sales.Clear(); _expired.Clear(); NotSent += batch.Count + sales.Count + expired.Count; _firstPendingAt = DateTime.UtcNow; }
            return false;
        }
        var res = await Send(new PriceBatch(RunPayload.AppVersion, ServerKey, InstallId.Get(), batch, sales.Count > 0 ? sales : null, expired.Count > 0 ? expired : null), _upload);
        string? note = null;
        lock (_lock)
        {
            if (res.Ok)
            {
                _pending.RemoveRange(0, batch.Count); _sales.RemoveRange(0, sales.Count); _expired.RemoveRange(0, expired.Count);
                Shared += batch.Count; SalesShared += sales.Count; LastError = "";
            }
            else if (res.Refused)
            {
                // refused for what is in it: sent again it gets the same answer, so it is dropped. Before 2026-10-28 it stayed at
                // the front of the queue and went out every ten seconds for good, and nothing queued after it was ever shared
                int n = batch.Count + sales.Count + expired.Count;
                _pending.RemoveRange(0, batch.Count); _sales.RemoveRange(0, sales.Count); _expired.RemoveRange(0, expired.Count);
                Refused += n;
                var why = res.Status + (res.Field.Length > 0 ? " " + res.Field : "");
                LastError = $"the site refused {n:N0} price{(n == 1 ? "" : "s")} ({why}); not sent again";
                note = $"market prices: the site answered {why}; {n} dropped, not sent again";
                _firstPendingAt = DateTime.UtcNow;
            }
            else
            {
                // may pass later, so kept and sent again; a 429 first waits as long as the site asked (a minute when it didn't say)
                if (res.TooMany || res.RetryAfter != null)
                {
                    var wait = res.RetryAfter ?? TimeSpan.FromMinutes(1);
                    _holdUntil = DateTime.UtcNow + wait;
                    LastError = $"the site asked to wait {Math.Max(1, (int)Math.Ceiling(wait.TotalSeconds))} s";
                }
                else LastError = "couldn't reach the site";
                _firstPendingAt = DateTime.UtcNow;
                if (_pending.Count > 500) _pending.RemoveRange(0, _pending.Count - 500);   // keep the newest for the retry
            }
        }
        if (note != null) CrashLog.NoteBurst(note);
        return res.Ok;
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
