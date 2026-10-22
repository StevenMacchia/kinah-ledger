using System.Windows;
using KinahLedger.Platform;
using KinahLedger.Update;

namespace KinahLedger.Ui;

/// <summary>
/// Updates: a check 20 seconds after start and every six hours. With automatic updates on, a newer version downloads in
/// the background and is installed when the window closes; Restart now installs it at once. Off, the banner asks first.
/// A run is never cut short by an update: Restart now during a run stops and saves it first, after asking.
/// </summary>
public partial class MainWindow
{
    static readonly TimeSpan FirstCheck = TimeSpan.FromSeconds(20), CheckEvery = TimeSpan.FromHours(6);
    const string SiteDownloadPage = "https://kinahledger.com/#tracker";
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
    string _updateStatus = "Checks GitHub for a newer release at start and every few hours. Only the app version goes with the request.";

    /// <summary>The update line for Options › Updates.</summary>
    public string UpdateStatusText => _updateStatus;
    void SetUpdateStatus(string text) { _updateStatus = text; _options?.RefreshUpdates(); }

    /// <summary>Starts the update checks. Called by the real app start only, never by layout renders or self-tests.</summary>
    public void StartUpdates()
    {
        if (_updatesStarted) return;
        _updatesStarted = true;
        UpdateBtn.Click += async (_, _) => await UpdateClicked();
        UpdateNotesLink.Click += (_, _) => { if (_release?.NotesUrl is { Length: > 0 } u) Open(u); };
        AnnounceUpdated();
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

    async Task UpdateLoop()
    {
        // a loose exe offers, once a week at most, to install itself: faster start, small updates, a desktop shortcut
        if (Installer.OfferDue && _exe != null) { await Task.Delay(TimeSpan.FromSeconds(8)); OfferInstall(); }
        // what the last update left behind goes first, so it can't race a new download
        if (_exe != null) await Updater.CleanupAsync(_exe);
        await Task.Delay(FirstCheck);
        while (true)
        {
            try { await CheckForUpdate(manual: false); }
            catch (Exception ex) { CrashLog.Write("update", ex); }
            await Task.Delay(CheckEvery);
        }
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
            if (info == null) { if (_release == null) SetUpdateStatus($"You have the newest version ({Updater.Show(Updater.Current)}). Checked at {when}."); return; }
            var tv = info.TargetFullRelease.Version;
            var rel = new Release(new Version(tv.Major, tv.Minor, tv.Patch), "", info.TargetFullRelease.SHA1 ?? "", info.TargetFullRelease.Size, $"{Installer.RepoUrl}/releases/tag/v{tv.Major}.{tv.Minor}.{tv.Patch}");
            if (_release != null && rel.Version == _release.Version && _updateError == null) { ShowUpdate(); return; }
            _vInfo = info; _release = rel; _staged = null; _updateError = null; _updateNeedsSite = false;
            if (_prefs.AutoUpdate) await FetchUpdate(_release); else ShowUpdate();
            return;
        }
        var r = await Updater.CheckAsync(_settings.Update.Manifest);
        var at = DateTime.Now.ToString("t");
        if (!r.Ok) { if (_release == null) SetUpdateStatus($"Couldn't check for updates at {at}: {r.Note}. It tries again in a few hours."); return; }
        if (r.Newer == null) { if (_release == null) SetUpdateStatus($"{r.Note} Checked at {at}."); return; }
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
            UpdateBtn.Content = _updateNeedsSite ? "Download from the site" : "Try again";
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
        if (_updateNeedsSite) { Open(SiteDownloadPage); return; }
        await FetchUpdate(_release);
    }

    /// <summary>Restart now: a run in progress is stopped and saved first (after asking), then the window closes and the new version starts.</summary>
    async Task RestartForUpdate()
    {
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
