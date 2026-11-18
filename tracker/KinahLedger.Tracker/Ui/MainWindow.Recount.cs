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
        if (!watch || _demo) return;
        if (!Characters.CategoriesRepaired) Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.ApplicationIdle, () => _ = RepairCategoriesAsync());
        // the re-read (0.10.134): every table this PC read before this start was read by the reader that lays icons on one
        // flat colour, so the ones that left slots unnamed are read again, once, after the re-count below if that runs too
        _rereadBefore = DateTime.UtcNow;
        if (Characters.Recounted) { if (!Characters.Reread) Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.ApplicationIdle, () => _ = RereadRunsAsync()); return; }
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
                SayOnLine(L.F("Old expedition runs counted again from their reward pictures: {0} changed, {1} piece(s) of gear removed that were the floor under the table, not loot.", written, plan.GearRemoved), "Faint");
                _statusHoldUntil = DateTime.UtcNow.AddSeconds(20);
            }
        }
        catch (Exception ex) { CrashLog.Write("recount", ex); Characters.MarkRecounted(); }
        finally { _recounting = false; }
        if (!Characters.Reread) await RereadRunsAsync();
    }

    /// <summary>
    /// The one-time repair of fights and runs kept in the wrong category (Session/CategoryRepair.cs; Steven, 2026-11-17: his
    /// Transcendence runs were filed as expeditions, their second runs' bosses as "Unnamed boss"). Off the window's thread, the
    /// files rewritten while the run log and the fight store hold their locks, then read back. Once per PC.
    /// </summary>
    async Task RepairCategoriesAsync()
    {
        try
        {
            if (Characters.CategoriesRepaired || Characters.NeedsPlacing || _placing) return;
            var dataDir = CrashLog.Dir;
            CategoryRepair.Result? res = null;
            await Task.Run(() =>
            {
                void Apply() => TrackerSession.FightStore.Rewrite(() => res = CategoryRepair.Run(dataDir));
                if (_watcher.Chat is { } ch) ch.Instances.Reload(Apply); else Apply();
            });
            Characters.MarkCategoriesRepaired();
            if (res == null) return;
            try { File.WriteAllLines(Path.Combine(dataDir, CategoryRepair.LogName), res.Log); } catch (Exception) { /* the summary is in reads.log either way */ }
            CrashLog.Note($"{res.Log[0]}{(res.Files > 0 ? $"; the files as they were kept beside them ({CategoryRepair.BackupSuffix})" : "")}; every change in {CategoryRepair.LogName}");
            if (res.Fights + res.Runs > 0)
            {
                BestRuns.Reset();
                Redraw();
                SayOnLine(L.F("Past fights and runs put in their right place: {0} fight(s) and {1} run(s), Transcendence and boss names among them.", res.Fights, res.Runs), "Faint");
                _statusHoldUntil = DateTime.UtcNow.AddSeconds(20);
            }
        }
        catch (Exception ex) { CrashLog.Write("categories repair", ex); Characters.MarkCategoriesRepaired(); }
    }

    DateTime _rereadBefore = DateTime.UtcNow;

    /// <summary>
    /// The re-read (Steven, 2026-11-16: the profile "is missing all of the gold gear for ferocious den ... HAS to be
    /// accurate"): the runs whose table left slots unnamed, read again from their pictures with the reader that sees the
    /// slot's real backing. A run takes the new read only on the re-count's terms (its Kinah the same, no more slots, fewer
    /// unnamed, nothing it named named otherwise); what it raised goes to the site as a correction that adds. Once per PC.
    /// </summary>
    async Task RereadRunsAsync()
    {
        if (_recounting) return;
        _recounting = true;
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(RecountDelaySeconds));
            if (Characters.Reread || Characters.NeedsPlacing || _placing) return;
            var dataDir = CrashLog.Dir;
            var settings = _settings; var items = _items; var before = _rereadBefore;
            var servers = RunRecount.ServersOf(dataDir, _prefs.Server);
            var plan = await Task.Run(() => RunRecount.PlanAsync(dataDir, EventShotRecovery.ShotsDir, settings, items, before, servers,
                between: () => Task.Delay(TimeSpan.FromSeconds(RecountPauseSeconds)), reread: true));
            int written = 0;
            if (plan.Changes.Count > 0)
            {
                if (_watcher.Chat is { } ch) ch.Instances.Reload(() => written = RunRecount.Apply(plan)); else written = RunRecount.Apply(plan);
                BestRuns.Reset();
                Redraw();
            }
            Characters.MarkReread();
            int named = plan.Changes.Sum(c => Math.Max(0, (c.Before.SlotsUnread + c.Before.UnknownItems + c.Before.UnknownGear) - (c.After.SlotsUnread + c.After.UnknownItems + c.After.UnknownGear)));
            try { File.WriteAllLines(Path.Combine(dataDir, "reread.log"), new[] { $"re-read of tables with slots not read: {plan.Looked} looked at, {written} changed, {named} slot(s) named now", "" }.Concat(RunRecount.Report(plan, items))); }
            catch (Exception) { }
            CrashLog.Note($"re-read of tables with slots not read: {plan.Looked} looked at, {written} run(s) written, {named} slot(s) named now; every run in reread.log");
            if (written > 0)
            {
                SayOnLine(L.F("Past expedition rewards read again from their pictures: {0} item(s) named that were left unread, in {1} run(s).", named, written), "Faint");
                _statusHoldUntil = DateTime.UtcNow.AddSeconds(20);
            }
        }
        catch (Exception ex) { CrashLog.Write("reread", ex); Characters.MarkReread(); }
        finally { _recounting = false; }
    }
}
