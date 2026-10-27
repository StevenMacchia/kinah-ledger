using System.Windows;
using KinahLedger.Platform;
using KinahLedger.Update;

namespace KinahLedger.Ui;

/// <summary>
/// Updates: a check 20 seconds after start, then a look every 10 minutes at the small latest.json the newest release
/// carries, which makes the full check at once when it names a newer version; the full check also runs every hour
/// whatever it says (0.8.48; hourly since 0.8.31, six hours before). With automatic updates on, a newer
/// version downloads in the background and is installed when the window closes; Restart now installs it at once. Off,
/// the banner asks first. Either way a version found by the timed check also asks once, in a dialog, whether to install
/// it now (AskToInstall). A run is never cut short by an update: the dialog waits for the run to stop and the game to leave the front, and Restart now
/// during a run stops and saves it first, after asking.
/// </summary>
public partial class MainWindow
{
    static readonly TimeSpan FirstCheck = TimeSpan.FromSeconds(20), PeekEvery = TimeSpan.FromMinutes(10), CheckEvery = TimeSpan.FromHours(1), DataEvery = TimeSpan.FromHours(6);
    const string SiteDownloadPage = "https://kinahledger.com/#tracker";
    /// <summary>Always the newest installer: GitHub serves the latest release's asset at this one address.</summary>
    public const string LatestSetupUrl = "https://github.com/StevenMacchia/kinah-ledger/releases/latest/download/KinahLedger-Setup.exe";
    /// <summary>Tells the restarted exe to wait for this one to let go instead of handing over to it.</summary>
    public const string AfterUpdateArg = "--after-update";

    /// <summary>The newest version the site lists, when it is above this one.</summary>
    Release? _release;
    /// <summary>Its verified download beside the exe, installed when the window closes.</summary>
    string? _staged;
    string? _updateError;
    bool _updateNeedsSite, _downloading, _restartForUpdate, _updatesStarted;
    double _updateProgress;
    readonly string? _exe = Updater.ExePath;
    // installed (0.8+): Velopack finds, downloads (small deltas) and applies the update; a loose exe uses the swap above
    Velopack.UpdateInfo? _vInfo;
    bool _offeringInstall;
    /// <summary>The version the install dialog has asked about this start: it asks once per version, so Later means later.</summary>
    Version? _askedAbout;
    /// <summary>The dialog is open, or a found version waits for the run to stop before it asks.</summary>
    bool _asking, _askWhenFree;
    string _updateStatus = "Checks GitHub for a newer release at start and every 10 minutes. Only the app version goes with the request.";

    /// <summary>The update line for Options › Updates.</summary>
    public string UpdateStatusText => _updateStatus;
    /// <summary>A downloaded update is waiting: Options › Updates shows Restart now beside Check now.</summary>
    public bool UpdateReady => _staged != null && !_downloading && _release != null;
    /// <summary>Options › Updates' Restart now: the same as the banner's.</summary>
    public Task RestartToUpdate() => _staged != null ? RestartForUpdate() : Task.CompletedTask;
    void SetUpdateStatus(string text) { _updateStatus = text; _options?.RefreshUpdates(); }

    /// <summary>Starts the update checks. Called by the real app start only, never by layout renders or self-tests.</summary>
    public void StartUpdates()
    {
        if (_updatesStarted) return;
        _updatesStarted = true;
        UpdateBtn.Click += async (_, _) => await UpdateClicked();
        UpdateNotesLink.Click += (_, _) => { if (_release?.NotesUrl is { Length: > 0 } u) Open(u); };
        AnnounceUpdated();
        // the site's reading settings and staged rollout (Update/RemoteConfig.cs): fetched with sharing or update checks on
        if (_settings.Upload.Enabled || _settings.Update.Check) _ = ConfigLoop();
        if (!_settings.Update.Check)
        {
            SetUpdateStatus("Update checks are off (update.check in settings.json). New versions are on the releases page.");
            return;
        }
        _ = UpdateLoop();
    }

    /// <summary>The first start of a new version says so, once.</summary>
    void AnnounceUpdated()
    {
        var now = Updater.Show(Updater.Current);
        var was = _prefs.LastVersion;
        if (was == now) return;
        _prefs.LastVersion = now; _prefs.Save();
        if (Updater.ParseVersion(was) is { } w && w < Updater.Current)
        {
            Say($"Updated to {now}. Everything you set is kept.", "Good");
            SetUpdateStatus($"Updated from {was} to {now}.");
        }
    }

    /// <summary>The remote config at start (a few seconds in) and every few hours; a failed fetch keeps what is in use.</summary>
    async Task ConfigLoop()
    {
        await Task.Delay(TimeSpan.FromSeconds(5));
        while (true)
        {
            try
            {
                using var http = new System.Net.Http.HttpClient { Timeout = TimeSpan.FromSeconds(15) };
                http.DefaultRequestHeaders.UserAgent.ParseAdd("KinahLedger/" + Upload.RunPayload.AppVersion);
                if (await RemoteConfig.FetchAsync(http, RemoteConfig.UrlFor(_settings.Upload)) is { } rc)
                {
                    RemoteConfig.Current = rc;
                    var sp = System.IO.Path.Combine(AppContext.BaseDirectory, "settings.json");
                    rc.ApplyTo(_settings, System.IO.File.Exists(sp) ? System.IO.File.ReadAllText(sp) : null);
                }
            }
            catch (Exception ex) { CrashLog.Note("remote config: " + ex.Message); }
            await Task.Delay(TimeSpan.FromHours(RemoteConfig.Current.TtlHours));
        }
    }

    /// <summary>
    /// Staged rollout: whether a found version installs on its own here (RemoteConfig.AutoInstall). When not, nothing is
    /// shown; the Options line says it comes a little later, and Check now there installs it at once.
    /// </summary>
    bool RolloutAllows(Version v, bool manual)
    {
        if (manual) return true;
        var (auto, why) = RemoteConfig.Current.AutoInstall(v, Upload.InstallId.Get());
        if (auto) return true;
        CrashLog.Note($"update {Updater.Show(v)} found, not installed yet: {why}");
        SetUpdateStatus($"Version {Updater.Show(v)} is out and reaches this tracker a little later ({why}). Check now installs it today.");
        return false;
    }

    async Task UpdateLoop()
    {
        // a loose exe offers, once a week at most, to install itself: faster start, small updates, a desktop shortcut
        if (Installer.OfferDue && _exe != null) { await Task.Delay(TimeSpan.FromSeconds(8)); OfferInstall(); }
        // what the last update left behind goes first, so it can't race a new download
        if (_exe != null) await Updater.CleanupAsync(_exe);
        await Task.Delay(FirstCheck);
        var dataDue = DateTime.MinValue;
        var fullAt = DateTime.MinValue;
        while (true)
        {
            // the release's small latest.json every 10 minutes (with the default manifest only; tests and custom manifests
            // keep the hourly check): the full check runs at once when it names a version above this one that no check has
            // looked for yet, and every hour whatever it says, so an unreadable file never stops updates
            Version? file = null;
            if (_settings.Update.Manifest == Updater.DefaultManifest) file = await Updater.LatestFileVersionAsync();
            var seen = _release?.Version is { } rv && (_fileChecked == null || rv > _fileChecked) ? rv : _fileChecked;
            if (Updater.FullCheckDue(file, Updater.Current, seen, DateTime.UtcNow - fullAt, CheckEvery))
            {
                if (file != null && file > Updater.Current && (seen == null || file > seen))
                {
                    _fileChecked = file;
                    CrashLog.Note($"update: latest.json names {Updater.Show(file)} (this is {Updater.Show(Updater.Current)}): checking now");
                }
                fullAt = DateTime.UtcNow;
                try
                {
                    await CheckForUpdate(manual: false);
                    await AskToInstall();
                }
                catch (Exception ex) { CrashLog.Write("update", ex); }
            }
            // the game data (zones, events, enemies, loot tables, skins…) between releases: see Data/DataFiles.cs. It
            // keeps its six hours; only the version check went hourly
            if (DateTime.UtcNow >= dataDue)
            {
                dataDue = DateTime.UtcNow + DataEvery;
                try
                {
                    using var http = new System.Net.Http.HttpClient { Timeout = TimeSpan.FromMinutes(2) };
                    http.DefaultRequestHeaders.UserAgent.ParseAdd("KinahLedger/" + Upload.RunPayload.AppVersion);
                    var got = await KinahLedger.Data.DataFiles.UpdateAsync(http);
                    if (got.Count > 0) SetUpdateStatus($"Game data updated ({string.Join(", ", got)}); in use from the next start.");
                }
                catch (Exception ex) { CrashLog.Note("data update: " + ex.Message); }
            }
            await Task.Delay(PeekEvery);
        }
    }

    /// <summary>The newest version latest.json named that a full check was made for (it isn't made again for it until the hourly one).</summary>
    Version? _fileChecked;

    /// <summary>
    /// The install dialog (Steven, 2026-10-25: check every hour and ask whether to install; every 10 minutes since 0.8.48,
    /// Steven, 2026-10-27). A version the timed check
    /// found, and that the staged rollout lets install here, asks once per start: Yes downloads it if it isn't yet and
    /// restarts into it, No leaves the banner (with automatic updates on it still installs when the tracker closes).
    /// It never asks during a run or with the game in front: it waits for both (AskWhenFree, from the window's tick). No is the default
    /// button, so Enter or Space meant for the game can't restart the tracker. Check now in Options never asks: its own
    /// Restart now is right there.
    /// </summary>
    async Task AskToInstall()
    {
        var r = _release;
        if (r == null || _asking || _askedAbout == r.Version || _downloading || _updateNeedsSite || _updateError != null || _offeringInstall) return;
        if (_quitting || _restartForUpdate) return;
        // never in the middle of play: a run, or the game in front (always-on counting, gathering and crafting are play too,
        // and an ownerless dialog there takes the game's focus). The panel says so once; the banner stays on the window
        if (Farming || _welcome || GameInFront()) { _askWhenFree = true; _updateNotice = UpdateWaitingNotice; UpdateHud(); return; }
        _askWhenFree = false; _askedAbout = r.Version; _asking = true;
        _updateNotice = ""; UpdateHud();
        try
        {
            var v = Updater.Show(r.Version);
            bool ready = _staged != null;
            var text = $"Kinah Ledger {v} is available (you have {Updater.Show(Updater.Current)}).\n\n" +
                       "Install it now? The tracker closes and opens again on the new version. Your settings and history are kept.\n\n" +
                       (ready ? "No leaves it for later: it installs by itself the next time the tracker closes."
                              : "No leaves it for later: Update on the banner installs it whenever you like.");
            const string title = "Kinah Ledger update";
            // in the tray or minimized there is no window to sit on (a dialog owned by a minimized window stays hidden with
            // it): the dialog stands on its own then, with its own taskbar button
            var answer = IsVisible && WindowState != WindowState.Minimized
                ? MessageBox.Show(this, text, title, MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No)
                : MessageBox.Show(text, title, MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No);
            CrashLog.Note($"update {v}: install dialog answered {(answer == MessageBoxResult.Yes ? "yes" : "later")}");
            if (answer != MessageBoxResult.Yes || !ReferenceEquals(_release, r)) return;
            if (_staged == null) await FetchUpdate(r);
            if (_staged != null && ReferenceEquals(_release, r)) await RestartForUpdate();
        }
        finally { _asking = false; }
    }

    /// <summary>The panel's line while a found version waits for the player to leave the game.</summary>
    const string UpdateWaitingNotice = "Update ready · installs when you leave the game";

    /// <summary>
    /// From the window's tick: a found version that waited asks once no run is on and the game is no longer in front
    /// (alt-tab, the game closed or minimized); a tracker closed before that asks on its next start.
    /// </summary>
    void AskWhenFree()
    {
        if (!_askWhenFree || _asking || Farming || _welcome || _quitting || GameInFront()) return;
        _askWhenFree = false;
        _updateNotice = "";   // AskToInstall puts it back if it has to wait again
        _ = AskToInstall();
    }

    /// <summary>The automatic-install choice changed in Options: a found version starts downloading at once when it was turned on.</summary>
    void AutoUpdateChanged(bool on)
    {
        _prefs.AutoUpdate = on;
        if (on && _release != null && _staged == null && !_downloading) _ = FetchUpdate(_release);
    }

    public async Task CheckForUpdate(bool manual)
    {
        if (_downloading) return;
        if (!_settings.Update.Check) return;
        if (manual) SetUpdateStatus("Checking…");
        if (Installer.Manager is { } um)
        {
            var info = await um.CheckForUpdatesAsync();
            var when = DateTime.Now.ToString("t");
            if (info == null)
            {
                // the newest GitHub release decides, not only the update packages: a newer release that carries no packages
                // for installed copies (it shouldn't happen; the release workflow refuses one) still reaches the player,
                // as its installer, which updates the installed copy in place
                var gh = await Updater.CheckAsync(_settings.Update.Manifest);
                if (gh.Ok && gh.Newer != null && RolloutAllows(gh.Newer.Version, manual))
                {
                    _release = gh.Newer; _vInfo = null; _staged = null; _updateNeedsSite = true;
                    _updateError = "this version comes as an installer";
                    ShowUpdate();
                    return;
                }
                if (_release == null) SetUpdateStatus($"You have the newest version ({Updater.Show(Updater.Current)}). Checked at {when}.");
                return;
            }
            var tv = info.TargetFullRelease.Version;
            var rel = new Release(new Version(tv.Major, tv.Minor, tv.Patch), "", info.TargetFullRelease.SHA1 ?? "", info.TargetFullRelease.Size, $"{Installer.RepoUrl}/releases/tag/v{tv.Major}.{tv.Minor}.{tv.Patch}");
            if (_release == null && !RolloutAllows(rel.Version, manual)) return;
            if (_release != null && rel.Version == _release.Version && _updateError == null) { ShowUpdate(); return; }
            _vInfo = info; _release = rel; _staged = null; _updateError = null; _updateNeedsSite = false;
            if (_prefs.AutoUpdate) await FetchUpdate(_release); else ShowUpdate();
            return;
        }
        var r = await Updater.CheckAsync(_settings.Update.Manifest);
        var at = DateTime.Now.ToString("t");
        if (!r.Ok) { if (_release == null) SetUpdateStatus($"Couldn't check for updates at {at}: {r.Note}. It tries again in an hour."); return; }
        if (r.Newer == null) { if (_release == null) SetUpdateStatus($"{r.Note} Checked at {at}."); return; }
        if (_release == null && !RolloutAllows(r.Newer.Version, manual)) return;
        if (_release != null && r.Newer.Version == _release.Version && _updateError == null) { ShowUpdate(); return; }
        _release = r.Newer; _staged = null; _updateError = null; _updateNeedsSite = false;
        if (_prefs.AutoUpdate && _exe != null) await FetchUpdate(_release);
        else ShowUpdate();
    }

    async Task FetchUpdate(Release r)
    {
        if (_downloading) return;
        if (Installer.Manager is { } vm && _vInfo != null)
        {
            _downloading = true; _updateProgress = 0; _updateError = null; ShowUpdate();
            try
            {
                await vm.DownloadUpdatesAsync(_vInfo, p => Dispatcher.Invoke(() => { _updateProgress = p / 100.0; if (_downloading) ShowUpdate(); }));
                if (ReferenceEquals(_release, r)) _staged = "installed";
            }
            catch (Exception ex) { _updateError = ex.Message; CrashLog.Write("update", ex); }
            finally { _downloading = false; ShowUpdate(); }
            return;
        }
        if (_exe == null) { _updateError = "this copy isn't running as KinahLedger.exe"; _updateNeedsSite = true; ShowUpdate(); return; }
        _downloading = true; _updateProgress = 0; _updateError = null; _updateNeedsSite = false;
        ShowUpdate();
        try
        {
            var progress = new Progress<double>(f => { _updateProgress = f; if (_downloading) ShowUpdate(); });
            var staged = await Updater.DownloadAsync(r, _exe, progress);
            if (ReferenceEquals(_release, r)) _staged = staged;
        }
        catch (UpdateException ex) { _updateError = ex.Message; _updateNeedsSite = ex.NeedsSite; }
        catch (Exception ex) { _updateError = ex.Message; CrashLog.Write("update", ex); }
        finally { _downloading = false; ShowUpdate(); }
    }

    /// <summary>The banner and the line in Options › Updates, for whatever state the update is in.</summary>
    void ShowUpdate()
    {
        var r = _release;
        if (r == null) { UpdateCard.Visibility = Visibility.Collapsed; return; }
        var v = Updater.Show(r.Version);
        UpdateCard.Visibility = Visibility.Visible;
        UpdateNotesText.Visibility = r.NotesUrl.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        UpdateBtn.IsEnabled = true;
        if (_downloading)
        {
            UpdateText.Text = $"Downloading Kinah Ledger {v}… {(int)(_updateProgress * 100)}%";
            UpdateBtn.Content = "Downloading"; UpdateBtn.IsEnabled = false;
        }
        else if (_staged != null)
        {
            UpdateText.Text = $"Kinah Ledger {v} is ready. It installs when you close the tracker, or now with Restart.";
            UpdateBtn.Content = "Restart now";
        }
        else if (_updateError != null)
        {
            UpdateText.Text = $"Couldn't update to {v}: {_updateError.TrimEnd('.')}.";
            UpdateBtn.Content = _updateNeedsSite ? (Installer.IsInstalled ? "Get the installer" : "Download from the site") : "Try again";
        }
        else
        {
            UpdateText.Text = $"Kinah Ledger {v} is out (you have {Updater.Show(Updater.Current)}).";
            UpdateBtn.Content = "Update";
        }
        SetUpdateStatus(UpdateText.Text);
    }

    async Task UpdateClicked()
    {
        if (_offeringInstall) { await InstallClicked(); return; }
        if (_release == null || _downloading) return;
        if (_staged != null) { await RestartForUpdate(); return; }
        if (_updateNeedsSite) { Open(Installer.IsInstalled ? LatestSetupUrl : SiteDownloadPage); return; }
        await FetchUpdate(_release);
    }

    /// <summary>Restart now: a run in progress is stopped and saved first (after asking), then the window closes and the new version starts.</summary>
    async Task RestartForUpdate()
    {
        _askedAbout = _release?.Version;   // the banner's Restart now answers the dialog's question: it never asks on top of it
        if (Farming)
        {
            var answer = MessageBox.Show(this, "Restart now to install the update?\n\nYour run stops first and is saved (and shared, if sharing is on) as usual.",
                "Kinah Ledger", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (answer != MessageBoxResult.Yes) return;
            await StopTracking();
        }
        _restartForUpdate = true;
        _quitting = true;   // a restart, even when the close button is set to keep the tracker in the tray
        Close();
    }

    /// <summary>The window's last step on close: a verified download replaces the exe, and Restart now starts it.</summary>
    void InstallUpdateOnClose()
    {
        if (_staged == null) return;
        if (Installer.Manager is { } vm && _vInfo != null)
        {
            // Velopack applies it once this process has exited, and starts the new version when Restart now was pressed
            try { vm.WaitExitThenApplyUpdates(_vInfo.TargetFullRelease, silent: true, restart: _restartForUpdate, restartArgs: _restartForUpdate ? new[] { AfterUpdateArg } : Array.Empty<string>()); }
            catch (Exception ex) { CrashLog.Write("update", ex); }
            return;
        }
        if (_exe == null) return;
        try
        {
            Updater.Apply(_staged, _exe);
            _staged = null;
        }
        catch (Exception ex)
        {
            CrashLog.Write("update", ex);
            if (_restartForUpdate)
                MessageBox.Show($"The update couldn't be installed ({ex.Message}). Kinah Ledger starts again as it was; the update is tried again later.",
                    "Kinah Ledger", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        if (!_restartForUpdate) return;
        try { Updater.Relaunch(_exe, AfterUpdateArg); }
        catch (Exception ex) { CrashLog.Write("update relaunch", ex); }
    }

    // ---- a loose exe installing itself (0.8) ----

    /// <summary>The banner offers the install: faster start, small background updates, a shortcut, a clean uninstall.</summary>
    void OfferInstall()
    {
        if (_release != null || Installer.IsInstalled) return;   // an update banner is up: that comes first
        _offeringInstall = true;
        UpdateCard.Visibility = Visibility.Visible;
        UpdateNotesText.Visibility = Visibility.Collapsed;
        UpdateText.Text = "Install Kinah Ledger: it starts faster, updates in seconds, and gets a desktop shortcut. Your settings and history stay.";
        UpdateBtn.Content = "Install"; UpdateBtn.IsEnabled = true;
        SetUpdateStatus("This copy runs as a single file. Installing it makes it start faster and update in seconds.");
    }

    async Task InstallClicked()
    {
        if (Farming)
        {
            var answer = MessageBox.Show(this, "Install now?\n\nYour run stops first and is saved (and shared, if sharing is on) as usual. The installed tracker opens by itself.",
                "Kinah Ledger", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (answer != MessageBoxResult.Yes) return;
            await StopTracking();
        }
        UpdateBtn.IsEnabled = false; UpdateBtn.Content = "Downloading";
        try
        {
            var progress = new Progress<double>(f => UpdateText.Text = $"Downloading the installer… {(int)(f * 100)}%");
            var setup = await Installer.DownloadSetupAsync(progress);
            Installer.StartSetup(setup, _exe);
            _quitting = true;
            Close();
        }
        catch (Exception ex)
        {
            CrashLog.Write("install", ex);
            UpdateText.Text = $"Couldn't install: {ex.Message.TrimEnd('.')}. The tracker keeps working as it is.";
            UpdateBtn.Content = "Not now"; UpdateBtn.IsEnabled = true;
            _offeringInstall = false; Installer.NotNow();
            UpdateBtn.Click += (_, _) => UpdateCard.Visibility = Visibility.Collapsed;
        }
    }
}
