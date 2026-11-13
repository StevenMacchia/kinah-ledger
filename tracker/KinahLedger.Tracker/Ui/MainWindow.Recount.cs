using KinahLedger.Session;

namespace KinahLedger.Ui;

/// <summary>
/// The one-time re-count of old reward tables at a start (Session/RunRecount.cs): the expedition runs counted before
/// 0.10.54, whose reader took rows of the floor for slots, read again from their kept table pictures with the fixed reader.
/// Off the window's thread, a pause between runs so the game keeps its share of the PC; the files change under the run
/// log's lock and are read back into it, as the sort of old runs by character does. Once per PC (characters.json Recounted).
/// </summary>
public partial class MainWindow
{
    /// <summary>How long after the window opens the re-count begins, and the pause after each run read.</summary>
    const double RecountDelaySeconds = 45, RecountPauseSeconds = 0.5;

    /// <summary>Tables read before this were read by the old reader: 0.10.54's release, or now for an install that ran an older version until this start.</summary>
    DateTime _recountBefore = RunRecount.FixedReaderAt;
    bool _recounting;

    /// <summary>From the window's start, when it watches the game: the re-count, unless it ran already.</summary>
    void InitRecount(bool watch)
    {
        if (!watch || _demo || Characters.Recounted) return;
        // the version that ran before this start (ui.json LastVersion, read before AnnounceUpdated writes this one's): one
        // older than 0.10.54 read every table it kept with the old reader, whenever that was
        if (Update.Updater.ParseVersion(_prefs.LastVersion) is { } was && was < new Version(0, 10, 54)) _recountBefore = DateTime.UtcNow;
        Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.ApplicationIdle, () => _ = RecountRunsAsync());
    }

    async Task RecountRunsAsync()
    {
        if (_recounting) return;
        _recounting = true;
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(RecountDelaySeconds));
            // a 0.10.51/52 install's place pass moves runs between the characters' files: the re-count waits for the next start
            if (Characters.Recounted || Characters.NeedsPlacing || _placing) return;
            var dataDir = CrashLog.Dir;
            var settings = _settings; var items = _items; var before = _recountBefore;
            var servers = RunRecount.ServersOf(dataDir, _prefs.Server);
            var plan = await Task.Run(() => RunRecount.PlanAsync(dataDir, EventShotRecovery.ShotsDir, settings, items, before, servers,
                between: () => Task.Delay(TimeSpan.FromSeconds(RecountPauseSeconds))));
            int written = 0;
            if (plan.Changes.Count > 0)
            {
                // the files change while the run log holds its lock, and it reads them back (as CharacterSort's moves do)
                if (_watcher.Chat is { } ch) ch.Instances.Reload(() => written = RunRecount.Apply(plan)); else written = RunRecount.Apply(plan);
                BestRuns.Reset();
                Redraw();
            }
            Characters.MarkRecounted();
            try { File.WriteAllLines(Path.Combine(dataDir, "recount.log"), new[] { plan.Summary, "" }.Concat(RunRecount.Report(plan, items))); }
            catch (Exception) { /* the summary is in reads.log either way */ }
            CrashLog.Note(plan.Summary + $"; {written} run(s) written" + (written > 0 ? $", the files as they were kept as {RunRecount.BackupName} beside them" : "") + "; every run in recount.log");
            if (written > 0)
            {
                SayOnLine($"Old expedition runs counted again from their reward pictures: {written} changed, {plan.GearRemoved} piece(s) of gear removed that were the floor under the table, not loot.", "Faint");
                _statusHoldUntil = DateTime.UtcNow.AddSeconds(20);
            }
        }
        catch (Exception ex) { CrashLog.Write("recount", ex); Characters.MarkRecounted(); }
        finally { _recounting = false; }
    }
}
