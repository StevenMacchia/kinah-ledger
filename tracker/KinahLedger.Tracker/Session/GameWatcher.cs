using System.Drawing;
using KinahLedger.Data;
using KinahLedger.Ocr;

namespace KinahLedger.Session;

/// <summary>
/// Runs for as long as the window is open, whether or not a farming run is on: finds the game window every few
/// seconds and, while the game is there, gives the always-on readers their turn. Today that is the Market.
/// A farming run (Start tracking) is a separate thing with its own loop; this one never counts loot.
/// </summary>
public sealed class GameWatcher
{
    readonly Settings _settings;
    readonly CancellationTokenSource _cts = new();
    Task? _loop;
    DateTime _gameAt = DateTime.MinValue;

    public MarketCapture Market { get; }
    /// <summary>Where the game window was last seen; null while the game isn't running or is minimized.</summary>
    public Rectangle? GameRect { get; private set; }
    public string LastError { get; private set; } = "";
    public bool Running => _loop != null && !_loop.IsCompleted;

    public GameWatcher(Settings s, ItemDictionary items, Rectangle? marketRegion, string server)
    {
        _settings = s;
        Market = new MarketCapture(s, items, marketRegion, server);
    }

    public void Start() { _loop ??= Task.Run(() => Loop(_cts.Token)); }

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
                if (Market.Enabled && GameRect is { } game) await Market.TickAsync(game, _settings.Ocr.Scale);
                if (Market.FlushDue) await Market.FlushAsync();
                LastError = "";
            }
            catch (Exception ex) { LastError = ex.Message; Market.LastError = ex.Message; CrashLog.Write("watcher", ex); }
            int spent = (int)(DateTime.UtcNow - t0).TotalMilliseconds;
            if (spent < interval) { try { await Task.Delay(interval - spent, ct); } catch (TaskCanceledException) { } }
        }
        // what was read in the last seconds still goes out when the window closes
        try { while (Market.Pending > 0 && await Market.FlushAsync()) { } }
        catch (Exception ex) { CrashLog.Write("watcher flush", ex); }
    }

    /// <summary>Stops the loop and gives the last batch a few seconds to go out, never longer.</summary>
    public async Task StopAsync()
    {
        _cts.Cancel();
        if (_loop != null) { try { await Task.WhenAny(_loop, Task.Delay(4000)); } catch (Exception) { /* loop ended */ } }
    }
}
