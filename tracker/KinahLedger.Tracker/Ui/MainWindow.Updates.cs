using System.Windows;
using KinahLedger.Platform;
using KinahLedger.Update;

namespace KinahLedger.Ui;

/// <summary>
/// Updates: a check 20 seconds after start, then a look every 10 minutes at the small latest.json the newest release
/// carries, which makes the full check at once when it names a newer version; the full check also runs every hour
/// whatever it says (0.8.48; hourly since 0.8.31, six hours before). With automatic updates on, a newer
/// version downloads in the background and is installed when the window closes; Restart now installs it at once. Off,
/// the banner asks first. The banner is the one prompt: a version found by the timed check is also said once on the
/// panel and, with the window hidden, in a tray notice (TellAboutUpdate), and nothing asks on top of it. A run is never
/// cut short by an update: Restart now during a run stops and saves it first, after asking.
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
    /// <summary>The version the panel and the tray have been told about this start: once per version.</summary>
    Version? _askedAbout;
    string _updateStatus = L.T("Checks GitHub for a newer release at start and every 10 minutes. Only the app version goes with the request.");

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
            SetUpdateStatus(L.T("Update checks are off (update.check in settings.json). New versions are on the releases page."));
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
            Say(L.F("Updated to {0}. Everything you set is kept.", now), "Good");
            SetUpdateStatus(L.F("Updated from {0} to {1}.", was, now));
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
        SetUpdateStatus(L.F("Version {0} is out and reaches this tracker a little later ({1}). Check now installs it today.", Updater.Show(v), why));
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
                    TellAboutUpdate();
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
                    if (got.Count > 0) SetUpdateStatus(L.F("Game data updated ({0}); in use from the next start.", string.Join(", ", got)));
                }
                catch (Exception ex) { CrashLog.Note("data update: " + ex.Message); }
            }
            await Task.Delay(PeekEvery);
        }
    }

    /// <summary>The newest version latest.json named that a full check was made for (it isn't made again for it until the hourly one).</summary>
    Version? _fileChecked;

    /// <summary>
    /// One prompt for an update, the banner (Steven, 2026-10-28: "Yes, fix it" to the Windows pop-up that showed beside the
    /// banner asking the same thing; 0.8.31 to 0.8.57 asked "Install it now?" in a dialog, his ask of 2026-10-25). A version
    /// the timed check found, and that the staged rollout lets install here, is said once per version where the player
    /// is: the banner on the window (ShowUpdate), the panel's line for its first minute in view (HudNotice), and a tray
    /// notice when the window is in the tray or minimized. Nothing asks and nothing takes focus from the game: with
    /// automatic updates on it installs when the tracker closes, and Restart now on the banner installs it at once.
    /// </summary>
    void TellAboutUpdate()
    {
        var r = _release;
        if (r == null || _askedAbout == r.Version || _downloading || _updateNeedsSite || _updateError != null || _offeringInstall) return;
        if (_quitting || _restartForUpdate) return;
        _askedAbout = r.Version;
        var v = Updater.Show(r.Version);
        bool ready = _staged != null;
        _updateNotice = ready ? UpdateReadyNotice : L.F("Kinah Ledger {0} is out · Update on the tracker window", v);
        _updateNoticeUntil = DateTime.MaxValue;   // its minute starts when the panel is first in view
        UpdateHud();
        // its own sound, three soft rising notes (ready.wav; Steven, 2026-11-04), once per version like the notice, with
        // the chime switch and volume; never in a farming run or an expedition, where a sound means a drop
        if (!_demo && _hudPrefs.Chime && !Farming && _watcher.Chat?.Instances.InProgress == null) PlaySound("ready", _hudPrefs.ChimeVolume);
        bool hidden = !IsVisible || WindowState == WindowState.Minimized;
        CrashLog.Note($"update {v}: on the banner and the panel{(hidden ? ", and in a tray notice (the window is hidden)" : "")}; nothing asks");
        if (!hidden || _demo) return;
        try
        {
            _tray.ShowBalloonTip(8000, L.F("Kinah Ledger {0} is ready", v),
                ready ? L.T("It installs when you close the tracker. To install it now, open the tracker and pick Restart now.")
                      : L.T("Open the tracker and pick Update to install it."),
                System.Windows.Forms.ToolTipIcon.Info);
        }
        catch (Exception ex) { CrashLog.Write("update notice", ex); }
    }

    /// <summary>The panel's line for a downloaded update.</summary>
    static string UpdateReadyNotice => L.T("Update ready · installs when you close the tracker");

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
        if (manual) SetUpdateStatus(L.T("Checking…"));
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
                if (_release == null) SetUpdateStatus(L.F("You have the newest version ({0}). Checked at {1}.", Updater.Show(Updater.Current), when));
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
        if (!r.Ok) { if (_release == null) SetUpdateStatus(L.F("Couldn't check for updates at {0}: {1}. It tries again in an hour.", at, r.Note)); return; }
        if (r.Newer == null) { if (_release == null) SetUpdateStatus(L.F("{0} Checked at {1}.", r.Note, at)); return; }
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
            UpdateText.Text = L.F("Downloading Kinah Ledger {0}… {1}%", v, (int)(_updateProgress * 100));
            UpdateBtn.Content = L.T("Downloading"); UpdateBtn.IsEnabled = false;
        }
        else if (_staged != null)
        {
            UpdateText.Text = L.F("Kinah Ledger {0} is ready. It installs when you close the tracker, or now with Restart.", v);
            UpdateBtn.Content = L.T("Restart now");
        }
        else if (_updateError != null)
        {
            UpdateText.Text = L.F("Couldn't update to {0}: {1}.", v, _updateError.TrimEnd('.'));
            UpdateBtn.Content = _updateNeedsSite ? (Installer.IsInstalled ? L.T("Get the installer") : L.T("Download from the site")) : L.T("Try again");
        }
        else
        {
            UpdateText.Text = L.F("Kinah Ledger {0} is out (you have {1}).", v, Updater.Show(Updater.Current));
            UpdateBtn.Content = L.T("Update");
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
        _askedAbout = _release?.Version;   // pressed already: the panel and the tray needn't say it again
        if (Farming)
        {
            var answer = MessageBox.Show(this, L.T("Restart now to install the update?\n\nYour run stops first and is saved (and shared, if sharing is on) as usual."),
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
                MessageBox.Show(L.F("The update couldn't be installed ({0}). Kinah Ledger starts again as it was; the update is tried again later.", ex.Message),
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
        UpdateText.Text = L.T("Install Kinah Ledger: it starts faster, updates in seconds, and gets a desktop shortcut. Your settings and history stay.");
        UpdateBtn.Content = L.T("Install"); UpdateBtn.IsEnabled = true;
        SetUpdateStatus(L.T("This copy runs as a single file. Installing it makes it start faster and update in seconds."));
    }

    async Task InstallClicked()
    {
        if (Farming)
        {
            var answer = MessageBox.Show(this, L.T("Install now?\n\nYour run stops first and is saved (and shared, if sharing is on) as usual. The installed tracker opens by itself."),
                "Kinah Ledger", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (answer != MessageBoxResult.Yes) return;
            await StopTracking();
        }
        UpdateBtn.IsEnabled = false; UpdateBtn.Content = L.T("Downloading");
        try
        {
            var progress = new Progress<double>(f => UpdateText.Text = L.F("Downloading the installer… {0}%", (int)(f * 100)));
            var setup = await Installer.DownloadSetupAsync(progress);
            Installer.StartSetup(setup, _exe);
            _quitting = true;
            Close();
        }
        catch (Exception ex)
        {
            CrashLog.Write("install", ex);
            UpdateText.Text = L.F("Couldn't install: {0}. The tracker keeps working as it is.", ex.Message.TrimEnd('.'));
            UpdateBtn.Content = L.T("Not now"); UpdateBtn.IsEnabled = true;
            _offeringInstall = false; Installer.NotNow();
            UpdateBtn.Click += (_, _) => UpdateCard.Visibility = Visibility.Collapsed;
        }
    }
}
