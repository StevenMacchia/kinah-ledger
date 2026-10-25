using System.Globalization;
using System.Text.RegularExpressions;

namespace KinahLedger.Session;

/// <summary>
/// The player's data folder kept to a size (2026-10-25: 919 MB, of which reward-shots 554 MB, reports 277 MB, trace logs
/// 49 MB). At the start of the app and then at most once an hour, on its own low-priority thread, it deletes old pictures
/// from reward-shots (<see cref="TrackerSession.PruneRewardShots"/>) and old miscount reports from reports/ (<see cref="PruneReports"/>).
/// It never works while a read pass is running: before each delete it waits for the pass to end (<see cref="PassStarted"/>
/// and <see cref="PassEnded"/> bracket every pass of the read loop), and it gives up for a few minutes when none ends.
/// Only files and folders the tracker named itself are touched.
/// </summary>
public static class Housekeeping
{
    public const double EveryMinutes = 60;
    /// <summary>After a pass that never let it work, the next try comes this many minutes later.</summary>
    public const double RetryMinutes = 5;
    public static double IdleWaitSeconds { get; set; } = 20;

    // ---- miscount reports (reports/<yyyyMMdd-HHmmss>/, written by ReportBuffer.Write) ----
    /// <summary>A report is kept this long, or until the folder holds <see cref="ReportMaxMB"/>: whichever deletes more.</summary>
    public const int ReportDays = 7;
    public const long ReportMaxMB = 200;
    /// <summary>A report this young is never deleted: its pictures may be on their way to the site (SendReportPictures takes up to a minute).</summary>
    public const double ReportMinAgeHours = 1;
    /// <summary>A report whose pictures were meant for the site and didn't get there (unsent.txt) is exempt from the size limit and kept this long, so the player can still attach it.</summary>
    public const int UnsentReportDays = 14;

    static readonly object Gate = new();
    static int _passes;
    static bool _running;
    static DateTime _startedAt = DateTime.MinValue;
    static ManualResetEventSlim _done = new(true);

    /// <summary>The read loop is starting a pass: housekeeping waits until it ends.</summary>
    public static void PassStarted() => Interlocked.Increment(ref _passes);
    /// <summary>The pass ended (always called, however it ended).</summary>
    public static void PassEnded() { if (Interlocked.Decrement(ref _passes) < 0) Interlocked.Exchange(ref _passes, 0); }

    /// <summary>
    /// Starts a prune of <paramref name="dir"/> (the player's folder by default) on a background thread when none has started
    /// in the last hour; the first call, at the start of the app, always does. Cheap when it isn't due. Returns whether it started.
    /// </summary>
    public static bool RunIfDue(DateTime now, string? dir = null)
    {
        lock (Gate)
        {
            if (_running || (now - _startedAt).TotalMinutes < EveryMinutes) return false;
            _running = true; _startedAt = now; _done = new ManualResetEventSlim(false);
        }
        var folder = dir ?? CrashLog.Dir;
        var done = _done;
        var t = new Thread(() =>
        {
            try { Run(folder, now, WaitForIdle); }
            catch (TimeoutException) { lock (Gate) _startedAt = now.AddMinutes(RetryMinutes - EveryMinutes); }
            catch (Exception ex) { CrashLog.Write("housekeeping", ex); }
            finally { lock (Gate) _running = false; done.Set(); }
        }) { IsBackground = true, Priority = ThreadPriority.BelowNormal, Name = "housekeeping" };
        t.Start();
        return true;
    }

    /// <summary>For tests: waits for the prune that <see cref="RunIfDue"/> started.</summary>
    internal static bool WaitForLast(TimeSpan timeout) => _done.Wait(timeout);

    /// <summary>For tests: the next <see cref="RunIfDue"/> runs at once.</summary>
    internal static void Forget() { lock (Gate) { _startedAt = DateTime.MinValue; } }

    /// <summary>One prune, now, on this thread: pictures, then reports. Returns how many of each were deleted.</summary>
    internal static (int Pictures, int Reports) Run(string dir, DateTime now, Action? idle = null)
    {
        int pictures = 0, reports = 0;
        var shots = Path.Combine(dir, "reward-shots");
        if (Directory.Exists(shots)) pictures = TrackerSession.PruneRewardShots(shots, now, null, idle);
        reports = PruneReports(dir, now, idle);
        if (pictures > 0 || reports > 0) CrashLog.Note($"housekeeping: {pictures} old picture(s) in reward-shots and {reports} old report(s) in reports deleted");
        return (pictures, reports);
    }

    /// <summary>Waits until no read pass is running; gives up (TimeoutException) when one has run for <see cref="IdleWaitSeconds"/> s without a gap.</summary>
    static void WaitForIdle()
    {
        if (Volatile.Read(ref _passes) <= 0) return;
        var until = DateTime.UtcNow.AddSeconds(IdleWaitSeconds);
        while (Volatile.Read(ref _passes) > 0)
        {
            if (DateTime.UtcNow > until) throw new TimeoutException("no gap between read passes");
            Thread.Sleep(20);
        }
    }

    /// <summary>
    /// Deletes miscount reports (folders under reports/) older than <see cref="ReportDays"/> days, then the oldest until what is
    /// left fits <see cref="ReportMaxMB"/>. Never a report younger than <see cref="ReportMinAgeHours"/>, and never (for size, or
    /// before <see cref="UnsentReportDays"/> days) one marked unsent.txt. Only folders named as ReportBuffer.Write names them.
    /// </summary>
    /// <param name="now">Local time (the folders are named in local time).</param>
    internal static int PruneReports(string root, DateTime now, Action? idle = null, long? maxBytes = null)
    {
        var dir = Path.Combine(root, "reports");
        if (!Directory.Exists(dir)) return 0;
        var all = new List<(DirectoryInfo D, DateTime At, long Bytes, bool Held)>();
        foreach (var d in new DirectoryInfo(dir).GetDirectories())
        {
            var m = Regex.Match(d.Name, @"^(\d{8}-\d{6})(?:-\d+)?$");
            if (!m.Success) continue;
            idle?.Invoke();
            var at = DateTime.TryParseExact(m.Groups[1].Value, "yyyyMMdd-HHmmss", CultureInfo.InvariantCulture, DateTimeStyles.None, out var t) ? t : d.LastWriteTime;
            long bytes = 0;
            try { bytes = d.EnumerateFiles("*", SearchOption.AllDirectories).Sum(f => f.Length); } catch (Exception) { }
            bool unsent = File.Exists(Path.Combine(d.FullName, ReportBuffer.UnsentMarker));
            double days = (now - at).TotalDays;
            // held: too young to be sure its pictures are sent, or unsent and not yet two weeks old
            bool held = (now - at).TotalHours < ReportMinAgeHours || (unsent && days < UnsentReportDays);
            all.Add((d, at, bytes, held));
        }
        int gone = 0;
        void Drop((DirectoryInfo D, DateTime At, long Bytes, bool Held) x)
        {
            idle?.Invoke();
            try { x.D.Delete(true); gone++; } catch (Exception) { }
        }
        foreach (var x in all.Where(x => !x.Held && (now - x.At).TotalDays > ReportDays).ToList()) { Drop(x); all.Remove(x); }
        long total = all.Sum(x => x.Bytes), max = maxBytes ?? ReportMaxMB * 1024 * 1024;
        foreach (var x in all.Where(x => !x.Held).OrderBy(x => x.At).ToList())
        {
            if (total <= max) break;
            total -= x.Bytes; Drop(x);
        }
        return gone;
    }
}

/// <summary>
/// How many pictures of each kind reward-shots may take in a day. One shared limit of 300 used to be filled by crafting
/// frames, 203 of them (267 MB) between 19:13 and 19:22 on 2026-10-24, after which no reward table or event card could be
/// kept and the recovery that re-reads them (EventShotRecovery) had nothing to read. Each kind now has its own budget, so
/// one kind can't crowd out another, and a run's end has the largest.
/// </summary>
internal static class ShotBudget
{
    internal enum Kind { RunEnd, Crafting, InRun, Screen }

    /// <summary>A run's end (reward table, event reward screens): what runs are counted from and replayed against. Up to six pictures an event and several an expedition.</summary>
    public const int RunEndPerDay = 200;
    /// <summary>
    /// The crafting window, kept only to tune the crafting panel's reader (`craft-test --glob *crafting.jpg`) by hand: the
    /// reader itself works on live frames, and a miscount report carries its own strip pictures. A frame every 40 s, 20 a day.
    /// </summary>
    public const int CraftingPerDay = 20;
    public const double CraftingGapSeconds = 40;
    /// <summary>A picture every 30 s inside an event or expedition (a card pick can show no title), fight and world boss pictures.</summary>
    public const int InRunPerDay = 60;
    /// <summary>Other reward screens (Daeva Pass, attendance) and shop screens.</summary>
    public const int ScreenPerDay = 60;

    public static int Limit(Kind k) => k switch { Kind.RunEnd => RunEndPerDay, Kind.Crafting => CraftingPerDay, Kind.InRun => InRunPerDay, _ => ScreenPerDay };

    static readonly Regex CraftingName = new(@"-crafting\.jpg$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    static string Label(Kind k) => k switch { Kind.RunEnd => "run-end", Kind.Crafting => "crafting", Kind.InRun => "in-run", _ => "screen" };

    /// <summary>The budget a picture belongs to, from its file name or "-title.jpg".</summary>
    public static Kind Of(string name) =>
        TrackerSession.KindOfShot(name) switch
        {
            TrackerSession.ShotKind.RunEnd => Kind.RunEnd,
            TrackerSession.ShotKind.Frequent => CraftingName.IsMatch(name) ? Kind.Crafting : Kind.InRun,
            _ => Kind.Screen,
        };

    static readonly object Lock = new();
    static string _key = "";
    static readonly int[] Count = new int[4];
    static readonly bool[] FullSaid = new bool[4];

    /// <summary>Loads (once a day, once a folder) how many of each kind today's pictures already are.</summary>
    static void Load(string dir, DateTime today)
    {
        var key = dir + "|" + today.ToString("yyyyMMdd");
        if (key == _key) return;
        _key = key; Array.Clear(Count); Array.Clear(FullSaid);
        try { foreach (var f in Directory.GetFiles(dir, today.ToString("yyyyMMdd") + "-*.jpg")) Count[(int)Of(Path.GetFileName(f))]++; }
        catch (Exception) { /* counted as it goes */ }
    }

    /// <summary>Whether a picture titled <paramref name="title"/> ("crafting", "in-run", "event-rewards", a screen's title) still fits today's budget.</summary>
    public static bool Room(string dir, DateTime today, string title)
    {
        var k = Of($"-{title}.jpg");
        lock (Lock)
        {
            Load(dir, today);
            if (Count[(int)k] < Limit(k)) return true;
            if (!FullSaid[(int)k]) { FullSaid[(int)k] = true; CrashLog.Note($"reward-shots: today's {Limit(k)} {Label(k)} pictures are kept; no more of those today"); }
            return false;
        }
    }

    /// <summary>A picture of that title was saved.</summary>
    public static void Took(string dir, DateTime today, string title)
    {
        var k = Of($"-{title}.jpg");
        lock (Lock) { Load(dir, today); Count[(int)k]++; }
    }
}
