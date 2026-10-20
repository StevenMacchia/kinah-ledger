using System.Drawing;
using KinahLedger.Data;
using KinahLedger.Ocr;

namespace KinahLedger.Session;

/// <summary>
/// Runs for as long as the window is open. Every few seconds it looks for the game; while the game is there it keeps
/// the always-on chat reader going (drops, gathers and Soul Crystals are counted whether or not a run is on) and gives
/// the Market reader its turn. What the always-on reader counts goes to the site in anonymous chunks every
/// <see cref="FlushMinutes"/> and is then forgotten here. "Start farming" puts a second count on top of the same
/// events, timed and labelled by the player, which is what the window shows and what their history keeps.
/// </summary>
public sealed class GameWatcher
{
    readonly Settings _settings;
    readonly ItemDictionary _items;
    readonly CancellationTokenSource _cts = new();
    Task? _loop;
    DateTime _gameAt = DateTime.MinValue, _chatTryAt = DateTime.MinValue, _flushAt = DateTime.UtcNow;

    public MarketCapture Market { get; }
    /// <summary>The always-on chat reader; null until the game and its chat have been found.</summary>
    public TrackerSession? Chat { get; private set; }
    /// <summary>A chat area the player chose by hand; null means the chat is found on its own.</summary>
    public Rectangle? ManualChat { get; set; }
    public string ChatNote { get; private set; } = "";
    public DateTime ChatSince { get; private set; }
    /// <summary>The farming run on top of the always-on count, from Start farming until Stop.</summary>
    public SessionEngine? Farm { get; private set; }
    public string FarmKind { get; private set; } = "Open world";
    public bool Farming => Farm != null;
    /// <summary>Where the game window was last seen; null while the game isn't running or is minimized.</summary>
    public Rectangle? GameRect { get; private set; }
    public string LastError { get; private set; } = "";
    public bool Running => _loop != null && !_loop.IsCompleted;
    /// <summary>The player's region key and server label, stamped on everything shared.</summary>
    public string RegionKey { get; set; } = "";
    public string Server { get; set; } = "";
    /// <summary>How often the always-on count is shared and restarted.</summary>
    public int FlushMinutes { get; set; } = 15;
    public int Flushed { get; private set; }
    public string FlushNote { get; private set; } = "";

    /// <summary>The currency counter at the top-left, read every <see cref="BalanceEverySec"/> while the game is open.</summary>
    public BalanceReader Balance { get; } = new();
    public int BalanceEverySec { get; set; } = 10;
    DateTime _balanceTickAt = DateTime.MinValue;
    DateTime? _balanceApplied;

    public GameWatcher(Settings s, ItemDictionary items, Rectangle? marketRegion, Rectangle? manualChat, string server)
    {
        _settings = s; _items = items; ManualChat = manualChat;
        Market = new MarketCapture(s, items, marketRegion, server);
    }

    /// <summary>The balance just read, when it was trusted in the last 20 seconds; an older one may predate Kina spent or picked up since.</summary>
    long? FreshBalance => Balance.Tracker.Balance is long b && Balance.Tracker.BalanceAt is DateTime at && (DateTime.UtcNow - at).TotalSeconds <= 20 ? b : null;

    async Task TickBalanceAsync(Rectangle game)
    {
        var c = Chat; if (c == null) return;
        if ((DateTime.UtcNow - _balanceTickAt).TotalSeconds < BalanceEverySec) return;
        _balanceTickAt = DateTime.UtcNow;
        // only the game's own counter is read: another window over the top-left corner is skipped, like the chat and the Market
        if (GameWindow.Covered(BalanceReader.Strip(game), _settings.ProcessHints) != null) return;
        await Balance.TickAsync(game, c.KinahPicked);
        // a new trusted value goes to the always-on count and the farming run; each keeps its first and last
        if (Balance.Tracker.BalanceAt is DateTime at && at != _balanceApplied)
        {
            _balanceApplied = at;
            c.Engine.SetBalance(Balance.Tracker.Balance);
            Farm?.SetBalance(Balance.Tracker.Balance);
        }
    }

    public void Start() { _loop ??= Task.Run(() => Loop(_cts.Token)); }

    /// <summary>Finds the chat now (or uses the chosen area) and starts reading. Throws with a plain message when it can't.</summary>
    public async Task<TrackerSession> EnsureChatAsync()
    {
        var c = Chat;
        if (c != null && c.Running) return c;
        var s = new TrackerSession(ManualChat, _settings, _items, "Passive") { RegionKey = RegionKey, Server = Server };
        s.Parsed += ev => { if (ev is MarketSold or MarketListed or MarketExpired) Market.OnChatEvent(ev); };
        await s.StartAsync();
        Chat = s; ChatSince = DateTime.UtcNow; ChatNote = s.DetectNote; _flushAt = DateTime.UtcNow;
        return s;
    }

    /// <summary>Drops the reader (the area was reset or re-chosen); the next tick finds the chat again. A farming run moves over to the new reader.</summary>
    public async Task ResetChatAsync()
    {
        var c = Chat; Chat = null; _chatTryAt = DateTime.MinValue;
        if (c != null) { c.Secondary = null; await c.StopAsync(); }
    }

    public SessionEngine StartFarm(string kind)
    {
        var e = new SessionEngine(_items);
        e.SetZone(Chat?.Zone);
        e.SetBalance(FreshBalance);
        Farm = e; FarmKind = kind;
        if (Chat != null) Chat.Secondary = e;
        return e;
    }

    public SessionEngine? StopFarm()
    {
        var e = Farm; if (e == null) return null;
        if (Chat != null) Chat.Secondary = null;
        e.Finish(); Farm = null;
        return e;
    }

    /// <summary>Which mode a count looks like, from what it holds: gathers, Soul Crystals, kills, or (with none of those) crafting and enchanting.</summary>
    public static string DominantKind(SessionEngine e)
    {
        var t = e.Totals();
        if (t.Nodes > t.Kills) return "Gathering";
        if (t.Pets > 0 && t.Pets * 5 >= Math.Max(1, t.Kills)) return "Pets";
        var o = e.OddsTotals();
        if (t.Kills == 0 && t.Kinah == 0 && o.Crafts + o.Enchants > 0) return "Crafting";
        return "Open world";
    }

    /// <summary>True when a count holds something the public tables can use: kills, Kinah, gathers, Soul Crystals, or craft and enchant results.</summary>
    public static bool Worth(SessionEngine e)
    {
        var t = e.Totals(); var o = e.OddsTotals();
        return t.Kills > 0 || t.Kinah > 0 || t.Nodes > 0 || t.Pets > 0 || o.Crafts + o.Enchants > 0;
    }

    async Task Loop(CancellationToken ct)
    {
        int interval = Math.Clamp(_settings.Market.IntervalMs, 1000, 10000);
        while (!ct.IsCancellationRequested)
        {
            var t0 = DateTime.UtcNow;
            try
            {
                // the game window is looked up every few seconds; without it there is nothing to read and the loop just waits
                if ((t0 - _gameAt).TotalSeconds >= 5) { GameRect = GameWindow.Find(_settings.ProcessHints); _gameAt = t0; }
                if (GameRect is { } game)
                {
                    // the chat reader starts as soon as the game is there and is retried every 20 s while the chat can't be seen
                    if ((Chat == null || !Chat.Running) && (t0 - _chatTryAt).TotalSeconds >= 20)
                    {
                        _chatTryAt = t0;
                        try { var c = await EnsureChatAsync(); if (Farm != null) { c.Secondary = Farm; } }
                        catch (Exception ex) { ChatNote = ex.Message; }
                    }
                    if (Market.Enabled && MarketInView()) await Market.TickAsync(game, _settings.Ocr.Scale);
                    try { await TickBalanceAsync(game); } catch (Exception ex) { CrashLog.Write("balance", ex); }
                }
                if (Market.FlushDue) await Market.FlushAsync();
                await FlushPassiveAsync(false);
                LastError = "";
            }
            catch (Exception ex) { LastError = ex.Message; Market.LastError = ex.Message; CrashLog.Write("watcher", ex); }
            int spent = (int)(DateTime.UtcNow - t0).TotalMilliseconds;
            if (spent < interval) { try { await Task.Delay(interval - spent, ct); } catch (TaskCanceledException) { } }
        }
        // what was read in the last minutes still goes out when the window closes
        try
        {
            while (Market.Pending > 0 && await Market.FlushAsync()) { }
            await FlushPassiveAsync(true);
        }
        catch (Exception ex) { CrashLog.Write("watcher flush", ex); }
    }

    /// <summary>The Market is read only where it can be seen: a found table nothing covers, or, while looking for it, the game in front.</summary>
    bool MarketInView() => Market.Layout is { } l
        ? GameWindow.Covered(l.Table, _settings.ProcessHints) == null
        : GameWindow.InFront(_settings.ProcessHints);

    /// <summary>
    /// Every FlushMinutes (and once at close) the always-on count is shared as one anonymous chunk under the kind it
    /// looks like, then restarted. A chunk under five minutes or with nothing in it is just restarted. Nothing is kept here.
    /// </summary>
    public async Task FlushPassiveAsync(bool final)
    {
        var c = Chat; if (c == null) return;
        if (!final && (DateTime.UtcNow - _flushAt).TotalMinutes < FlushMinutes) return;
        _flushAt = DateTime.UtcNow;
        var e = c.Engine;
        if (e.Elapsed.TotalMinutes < 5 || e.Events == 0) { if (e.Elapsed.TotalMinutes >= FlushMinutes) RotateChat(c); return; }
        var old = RotateChat(c);
        if (!_settings.Upload.Enabled) { FlushNote = "sharing is off"; return; }
        // time in town with a stray line or two isn't farming: sharing it would add a zero to some zone's Kinah per hour
        if (!Worth(old)) { FlushNote = "nothing farmed in the last chunk, so nothing shared"; return; }
        var kind = DominantKind(old);
        bool ok = await c.UploadAsync(_settings, old, kind, TrackerSession.ActivityOf(old, kind));
        if (ok) { Flushed++; FlushNote = $"shared {old.Events} lines as {kind.ToLowerInvariant()}"; }
        else FlushNote = "couldn't reach the site";
    }

    /// <summary>Restarts the always-on count; the fresh one starts from the balance just read, when there is one.</summary>
    SessionEngine RotateChat(TrackerSession c)
    {
        var old = c.Rotate();
        c.Engine.SetBalance(FreshBalance);
        return old;
    }

    /// <summary>Stops the loop, gives the last batches a few seconds to go out, then stops the chat reader.</summary>
    public async Task StopAsync()
    {
        _cts.Cancel();
        if (_loop != null) { try { await Task.WhenAny(_loop, Task.Delay(6000)); } catch (Exception) { /* loop ended */ } }
        var c = Chat; Chat = null;
        if (c != null) { try { await c.StopAsync(); } catch (Exception) { /* reader ended */ } }
    }
}
