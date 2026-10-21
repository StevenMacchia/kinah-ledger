namespace KinahLedger.Platform;

/// <summary>
/// One tracker per Windows session. A second start (a double-click while it already runs in the tray) asks the first
/// one to show its window and quits. An update's restart waits for the old process to let go instead.
/// </summary>
public sealed class SingleInstance : IDisposable
{
    const string MutexName = @"Local\KinahLedger.Tracker";
    const string ShowEventName = @"Local\KinahLedger.Tracker.Show";

    readonly Mutex _mutex;
    readonly EventWaitHandle _show;
    RegisteredWaitHandle? _wait;

    SingleInstance(Mutex m, EventWaitHandle show) { _mutex = m; _show = show; }

    /// <summary>This process's claim on being the tracker, or null when another one already holds it after <paramref name="wait"/>.</summary>
    public static SingleInstance? TryAcquire(TimeSpan wait)
    {
        var m = new Mutex(false, MutexName);
        bool got;
        try { got = m.WaitOne(wait); }
        catch (AbandonedMutexException) { got = true; }   // the last one crashed: the claim is ours
        if (!got) { m.Dispose(); return null; }
        return new SingleInstance(m, new EventWaitHandle(false, EventResetMode.AutoReset, ShowEventName));
    }

    /// <summary>Asks the running tracker to bring its window up.</summary>
    public static void ShowExisting()
    {
        try { using var e = EventWaitHandle.OpenExisting(ShowEventName); e.Set(); }
        catch (Exception) { /* it is closing; nothing to show */ }
    }

    /// <summary>Calls <paramref name="show"/> (on a pool thread) each time another start asks for the window.</summary>
    public void OnShowRequested(Action show) =>
        _wait = ThreadPool.RegisterWaitForSingleObject(_show, (_, _) => show(), null, Timeout.Infinite, executeOnlyOnce: false);

    public void Dispose()
    {
        _wait?.Unregister(null);
        try { _mutex.ReleaseMutex(); } catch (Exception) { /* not held on this thread */ }
        _mutex.Dispose();
        _show.Dispose();
    }
}
