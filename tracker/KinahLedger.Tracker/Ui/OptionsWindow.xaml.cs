using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using KinahLedger.Data;
using KinahLedger.Platform;

namespace KinahLedger.Ui;

/// <summary>
/// Options, in its own window. Settings are edited on a copy (<see cref="OptionsChoice"/>) and take effect on Save;
/// Cancel, Esc or closing without saving leaves everything as it was (a theme being tried is put back). The Game
/// reading page's buttons are actions, not settings, and act right away, as their page says.
/// </summary>
public partial class OptionsWindow : Window
{
    public enum Page { General, Server, Sharing, Reading, InGame, Updates, About }

    static readonly (Page Page, string Name)[] Pages =
    {
        (Page.General, "General"), (Page.Server, "Server"), (Page.Sharing, "Sharing"), (Page.Reading, "Game reading"),
        (Page.InGame, "In game"), (Page.Updates, "Updates"), (Page.About, "About"),
    };
    static readonly string[] GameStartChoices = { "Open the window", "Stay in the tray" };

    readonly MainWindow _host;
    readonly DispatcherTimer _live = new() { Interval = TimeSpan.FromSeconds(1) };
    OptionsChoice _saved, _edit;
    Page _page;
    bool _loading, _capturing, _closeConfirmed;

    public OptionsWindow(MainWindow host, Page page)
    {
        InitializeComponent();
        _host = host;
        _saved = _edit = host.CurrentOptions();
        Icon = Native.AppIconSource();
        var version = "Version " + Upload.RunPayload.AppVersion;
        NavVersion.Text = version; AboutVersion.Text = version + " · for Aion 2 (global)";

        BuildNav();
        GameStartBox.ItemsSource = GameStartChoices;
        RegionPick.ItemsSource = Servers.Regions;
        Load(_edit);
        Wire();
        Go(page);

        _live.Tick += (_, _) => RefreshLive();
        _live.Start();
        SourceInitialized += (_, _) => Native.DarkTitleBar(new WindowInteropHelper(this).Handle, Skins.IsDark(_edit.Skin));
        Closing += OnClosingOptions;
        Closed += (_, _) => _live.Stop();
    }

    /* ---------------- pages ---------------- */

    void BuildNav()
    {
        Nav.Children.Clear();
        foreach (var (p, name) in Pages)
        {
            var b = new Button { Content = name, Style = (Style)FindResource("NavItem"), Tag = p == _page ? "True" : "False" };
            var target = p;
            b.Click += (_, _) => Go(target);
            Nav.Children.Add(b);
        }
    }

    public void Go(Page page)
    {
        _page = page;
        foreach (var (p, el) in new (Page, FrameworkElement)[] { (Page.General, PageGeneral), (Page.Server, PageServer), (Page.Sharing, PageSharing),
                     (Page.Reading, PageReading), (Page.InGame, PageInGame), (Page.Updates, PageUpdates), (Page.About, PageAbout) })
            el.Visibility = p == page ? Visibility.Visible : Visibility.Collapsed;
        // the style's trigger matches the text "True" (Tag holds an object, so a bool would not match it)
        for (int i = 0; i < Nav.Children.Count; i++) ((Button)Nav.Children[i]).Tag = Pages[i].Page == page ? "True" : "False";
        Scroller.ScrollToTop();
        RefreshLive();
        RefreshPreview();
    }

    /// <summary>True while the Game reading page is on screen: the main window then sends it fresh frames of the game.</summary>
    public bool WantsPreview => IsVisible && _page == Page.Reading;

    /* ---------------- the edited copy ---------------- */

    /// <summary>Puts a set of choices into the controls without counting it as an edit.</summary>
    void Load(OptionsChoice c)
    {
        _loading = true;
        StartupChk.IsChecked = c.StartWithWindows;
        StartupChk.IsEnabled = _host.CanStartWithWindows;
        if (!_host.CanStartWithWindows) StartupHint.Text = "Available in the downloaded KinahLedger.exe.";
        GameStartBox.SelectedIndex = c.OnGameStart == "tray" ? 1 : 0;
        HideOnCloseChk.IsChecked = c.HideWhenGameCloses;
        CloseToTrayChk.IsChecked = c.CloseToTray;
        MinToTrayChk.IsChecked = c.MinimizeToTray;
        RegionPick.SelectedItem = Servers.Region(c.Region) ?? Servers.Regions[0];
        FillServers(c.Region, c.Server);
        ShareChk.IsChecked = c.Share;
        GearBox.Text = c.GearScore > 0 ? c.GearScore.ToString() : "";
        BuildAlerts(c.Server);
        ReportFramesChk.IsChecked = c.ReportFrames;
        SendFramesChk.IsChecked = c.SendFrames;
        HelpCountingChk.IsChecked = c.HelpCounting;
        ShowAccuracyChk.IsChecked = c.ShowAccuracy;
        HudChk.IsChecked = c.Hud;
        HudLockChk.IsChecked = c.HudLocked;
        HudOpacity.Value = c.HudOpacity; HudOpacityLbl.Text = $"{c.HudOpacity:P0}";
        HudScale.Value = c.HudScale; HudScaleLbl.Text = $"{c.HudScale:P0}";
        AutoUpdateChk.IsChecked = c.AutoUpdate;
        AutoUpdateChk.IsEnabled = _host.AppSettings.Update.Check;
        BuildSkins(c.Skin);
        BuildHudFields(c.HudFields);
        _loading = false;
        Refresh();
    }

    void FillServers(string region, string label)
    {
        ServerPick.ItemsSource = Servers.In(region);
        ServerPick.SelectedItem = Servers.Find(region, label);
    }

    void BuildSkins(string current)
    {
        SkinHost.Children.Clear();
        foreach (var s in Skins.All)
        {
            var key = s.Key;
            var chip = _host.Chip(s.Name, current == key, () => Edit(_edit with { Skin = key }, previewSkin: true), s.Blurb);
            SkinHost.Children.Add(chip);
        }
        SkinHint.Text = Skins.Get(current).Blurb is var b ? char.ToUpperInvariant(b[0]) + b[1..] + "." : "";
    }

    /// <summary>One chip per figure the panel can show; a click adds or takes it away (the last one stays).</summary>
    void BuildHudFields(string saved)
    {
        HudFieldHost.Children.Clear();
        var on = HudFields.Parse(saved);
        foreach (var f in HudFields.All)
        {
            var key = f.Key;
            var chip = _host.Chip(f.Name, on.Contains(key), () =>
            {
                var now = HudFields.Parse(_edit.HudFields);
                if (!now.Remove(key)) now.Add(key);
                if (now.Count == 0) return;   // the panel needs one figure at least
                Edit(_edit with { HudFields = HudFields.Join(now) });
                BuildHudFields(_edit.HudFields);
            }, f.Hint);
            HudFieldHost.Children.Add(chip);
        }
    }

    void Wire()
    {
        StartupChk.Click += (_, _) => Edit(_edit with { StartWithWindows = StartupChk.IsChecked == true });
        GameStartBox.SelectionChanged += (_, _) => Edit(_edit with { OnGameStart = GameStartBox.SelectedIndex == 1 ? "tray" : "open" });
        HideOnCloseChk.Click += (_, _) => Edit(_edit with { HideWhenGameCloses = HideOnCloseChk.IsChecked == true });
        CloseToTrayChk.Click += (_, _) => Edit(_edit with { CloseToTray = CloseToTrayChk.IsChecked == true });
        MinToTrayChk.Click += (_, _) => Edit(_edit with { MinimizeToTray = MinToTrayChk.IsChecked == true });
        RegionPick.SelectionChanged += (_, _) =>
        {
            if (_loading || RegionPick.SelectedItem is not GameRegion r) return;
            _loading = true; FillServers(r.Key, _edit.Server); _loading = false;   // keeps the server when the new region has it too
            Edit(_edit with { Region = r.Key, Server = (ServerPick.SelectedItem as GameServer)?.Label ?? "" });
        };
        ServerPick.SelectionChanged += (_, _) => Edit(_edit with { Server = (ServerPick.SelectedItem as GameServer)?.Label ?? "" });
        GearBox.TextChanged += (_, _) => { if (!_loading) Edit(_edit with { GearScore = int.TryParse(new string(GearBox.Text.Where(char.IsDigit).ToArray()), out var g) ? g : 0 }); };
        ShareChk.Click += (_, _) => Edit(_edit with { Share = ShareChk.IsChecked == true });
        ReportFramesChk.Click += (_, _) => Edit(_edit with { ReportFrames = ReportFramesChk.IsChecked == true });
        SendFramesChk.Click += (_, _) => Edit(_edit with { SendFrames = SendFramesChk.IsChecked == true });
        HelpCountingChk.Click += (_, _) => Edit(_edit with { HelpCounting = HelpCountingChk.IsChecked == true });
        ShowAccuracyChk.Click += (_, _) => Edit(_edit with { ShowAccuracy = ShowAccuracyChk.IsChecked == true });
        HudChk.Click += (_, _) => Edit(_edit with { Hud = HudChk.IsChecked == true });
        HudLockChk.Click += (_, _) => Edit(_edit with { HudLocked = HudLockChk.IsChecked == true });
        // opacity and size show on the panel as the sliders move; Save keeps them, Cancel puts the saved ones back
        HudOpacity.ValueChanged += (_, _) =>
        {
            HudOpacityLbl.Text = $"{HudOpacity.Value:P0}";
            if (_loading) return;
            Edit(_edit with { HudOpacity = Math.Round(HudOpacity.Value, 2) });
            _host.PreviewHud(_edit.HudOpacity, _edit.HudScale);
        };
        HudScale.ValueChanged += (_, _) =>
        {
            HudScaleLbl.Text = $"{HudScale.Value:P0}";
            if (_loading) return;
            Edit(_edit with { HudScale = Math.Round(HudScale.Value, 2) });
            _host.PreviewHud(_edit.HudOpacity, _edit.HudScale);
        };
        AutoUpdateChk.Click += (_, _) => Edit(_edit with { AutoUpdate = AutoUpdateChk.IsChecked == true });

        WhatsSentLink.Click += (_, _) => _host.WhatsSent(this);
        WhatsReadLink.Click += (_, _) => _host.WhatsRead(this);
        LinkBtn.Click += (_, _) => _host.LinkProfile();
        CopyCodeBtn.Click += (_, _) =>
        {
            try { Clipboard.SetText(LinkCodeText.Text); ShowNote("Link code copied. Paste it into your profile on the site to see your runs there.", "Good"); }
            catch (Exception) { ShowNote("Couldn't reach the clipboard; the code is " + LinkCodeText.Text, "Warn"); }
        };
        ResetIdBtn.Click += (_, _) =>
        {
            var r = MessageBox.Show(this, "Replace this PC's install ID?\n\nRuns shared from now on can't be tied to earlier ones, and the link code changes. Your profile keeps the runs already linked.",
                "Kinah Ledger", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (r == MessageBoxResult.Yes) { _host.ResetInstallId(); RefreshLive(); }
        };
        DataFolderBtn.Click += (_, _) => MainWindow.Open(MainWindow.DataFolder);

        DetectBtn.Click += async (_, _) => await Act(DetectBtn, _host.DetectNow);
        TestBtn.Click += async (_, _) => await Act(TestBtn, _host.TestRead);
        PickChatBtn.Click += (_, _) => AroundPicker(_host.PickChat);
        CalibrateBtn.Click += (_, _) => _host.OpenCalibration(this);
        AutoChatBtn.Click += (_, _) => { _host.ResetChat(); RefreshLive(); };
        PickMarketBtn.Click += (_, _) => AroundPicker(_host.PickMarket);
        AutoMarketBtn.Click += (_, _) => { _host.ResetMarket(); RefreshLive(); };
        ShareMarketBtn.Click += (_, _) => _host.ShareMarketNow();

        HotkeyChangeBtn.Click += (_, _) => StartCapture();
        HotkeyBox.PreviewMouseLeftButtonDown += (_, e) => { if (!_capturing) { StartCapture(); e.Handled = true; } };
        HotkeyBox.PreviewKeyDown += OnHotkeyKey;
        HotkeyBox.LostKeyboardFocus += (_, _) => EndCapture();
        HotkeyOffBtn.Click += (_, _) => Edit(_edit with { Hotkey = "" });
        HotkeyResetBtn.Click += (_, _) => Edit(_edit with { Hotkey = Hotkey.Default });
        HudResetBtn.Click += (_, _) => { _host.ResetHudPosition(); ShowNote("The panel goes back to the top centre of the game's screen.", "Good"); };

        CheckNowBtn.Click += async (_, _) => await Act(CheckNowBtn, () => _host.CheckForUpdate(manual: true));
        RestartUpdateBtn.Click += async (_, _) => await _host.RestartToUpdate();
        ReleasesLink.Click += (_, _) => MainWindow.Open(MainWindow.RepoUrl + "/releases");

        AboutSupportBtn.Click += (_, _) => MainWindow.Open(MainWindow.SupportUrl);
        AboutSiteBtn.Click += (_, _) => MainWindow.Open(MainWindow.SiteUrl);
        AboutDiscordBtn.Click += (_, _) => MainWindow.Open(MainWindow.DiscordUrl);
        ReleasesDiscordLink.Click += (_, _) => MainWindow.Open(MainWindow.DiscordUrl);
        AboutSourceBtn.Click += (_, _) => MainWindow.Open(MainWindow.RepoUrl);
        AboutSentBtn.Click += (_, _) => _host.WhatsSent(this);
        AboutLogsBtn.Click += (_, _) => MainWindow.Open(CrashLog.Dir);

        SaveBtn.Click += (_, _) => Save();
        CancelBtn.Click += (_, _) => { _closeConfirmed = true; Close(); };
    }

    /// <summary>One control changed: the copy follows, and the footer says whether there is something to save.</summary>
    void Edit(OptionsChoice next, bool previewSkin = false)
    {
        if (_loading) return;
        bool skin = next.Skin != _edit.Skin;
        _edit = next;
        if (skin && previewSkin) _host.ApplySkin(next.Skin);   // try the look on now; Cancel puts the saved one back
        Refresh();
    }

    /// <summary>The bits of the page that follow the edited copy, and the footer.</summary>
    void Refresh()
    {
        HotkeyBox.Text = _capturing ? "Press the keys…" : _edit.Hotkey.Length > 0 ? _edit.Hotkey : "Off";
        HotkeyOffBtn.IsEnabled = _edit.Hotkey.Length > 0;
        HotkeyResetBtn.IsEnabled = _edit.Hotkey != Hotkey.Default;
        StartupTip.Visibility = _edit.StartWithWindows && !_edit.CloseToTray ? Visibility.Visible : Visibility.Collapsed;
        ServerWarn.Visibility = _edit.Server.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        bool dirty = _edit != _saved;
        SaveBtn.IsEnabled = dirty;
        if (dirty) Footer("Unsaved changes. Save puts them into effect.", "Muted");
        else if (FooterNote.Text.StartsWith("Unsaved")) Footer("", "Muted");
    }

    void Save()
    {
        if (_edit.Server.Length == 0)
        {
            Go(Page.Server);
            Footer("Pick a server before saving: runs and prices are filed under it.", "Warn");
            return;
        }
        var problem = _host.ApplyOptions(_edit);
        _saved = _edit = _host.CurrentOptions();
        Load(_edit);
        if (problem != null) { Footer("Saved, but: " + problem, "Warn"); return; }   // stays open so the note can be read
        _closeConfirmed = true;
        Close();
    }

    /// <summary>The tracker is quitting: close without asking, unsaved edits dropped and a theme being tried put back.</summary>
    public void Discard()
    {
        _closeConfirmed = true;
        Close();
    }

    void OnClosingOptions(object? sender, CancelEventArgs e)
    {
        EndCapture();
        if (!_closeConfirmed && _edit != _saved)
        {
            var r = MessageBox.Show(this, "Save your changes to Kinah Ledger's options?", "Kinah Ledger", MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
            if (r == MessageBoxResult.Cancel) { e.Cancel = true; return; }
            if (r == MessageBoxResult.Yes)
            {
                e.Cancel = true;
                Dispatcher.BeginInvoke(Save);
                return;
            }
        }
        if (_edit.Skin != _saved.Skin || Skins.Current != _saved.Skin) _host.ApplySkin(_saved.Skin);   // a theme being tried goes back
        _host.PreviewHud(_saved.HudOpacity, _saved.HudScale);   // and the panel's look being tried
    }

    /* ---------------- live state from the main window ---------------- */

    void RefreshLive()
    {
        LinkCodeText.Text = _host.LinkCode();
        ChatStatus.Text = _host.ChatAreaText();
        MarketStatus.Text = _host.MarketLine();
        AutoChatBtn.IsEnabled = _host.ChatManual;
        AutoMarketBtn.IsEnabled = _host.MarketManual;
        RefreshUpdates();
    }

    public void RefreshUpdates()
    {
        VersionTitle.Text = "Version " + Upload.RunPayload.AppVersion;
        UpdateLine.Text = _host.UpdateStatusText;
        CheckNowBtn.IsEnabled = _host.AppSettings.Update.Check;
        RestartUpdateBtn.Visibility = _host.UpdateReady || Environment.GetEnvironmentVariable("KL_SHOT_UPDATE_READY") == "1" ? Visibility.Visible : Visibility.Collapsed;   // the variable: layout renders only
    }

    public void RefreshPreview()
    {
        Preview.Source = _host.PreviewSource;
        PreviewHint.Visibility = _host.PreviewSource == null ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>A message from an action: on the Game reading page under its buttons, elsewhere in the footer.</summary>
    public void ShowNote(string text, string tone)
    {
        if (_page == Page.Reading)
        {
            ReadingNote.Text = text; ReadingNote.Foreground = (Brush)FindResource(tone); ReadingNote.Visibility = Visibility.Visible;
        }
        else Footer(text, tone);
        RefreshLive();
    }

    void Footer(string text, string tone) { FooterNote.Text = text; FooterNote.Foreground = (Brush)FindResource(tone); }

    /// <summary>The look changed (tried or saved): chips and the title bar follow.</summary>
    public void SkinChanged()
    {
        BuildSkins(_edit.Skin);
        var h = new WindowInteropHelper(this).Handle;
        if (h != IntPtr.Zero) Native.DarkTitleBar(h, Skins.IsDark(Skins.Current));
    }

    /* ---------------- actions ---------------- */

    static async Task Act(Button b, Func<Task> action)
    {
        b.IsEnabled = false;
        try { await action(); }
        finally { b.IsEnabled = true; }
    }

    /// <summary>The box-dragging overlay needs the game in full view: this window steps aside while it is up.</summary>
    void AroundPicker(Action pick)
    {
        Hide();
        try { pick(); }
        finally { Show(); Activate(); RefreshLive(); RefreshPreview(); }
    }

    /* ---------------- recording a shortcut ---------------- */

    void StartCapture()
    {
        _capturing = true;
        _host.SuspendHotkey(true);   // the current shortcut would otherwise start a run instead of being recorded
        HotkeyHint.Text = "Hold Ctrl, Alt or Shift (or several) and press a key. Esc cancels.";
        HotkeyHint.Foreground = (Brush)FindResource("Accent");
        HotkeyBox.Focus();
        Refresh();
    }

    void EndCapture()
    {
        if (!_capturing) return;
        _capturing = false;
        _host.SuspendHotkey(false);
        HotkeyHint.Text = "Works while the game has focus. Click Change, then press the keys.";
        HotkeyHint.Foreground = (Brush)FindResource("Muted");
        Refresh();
    }

    void OnHotkeyKey(object sender, KeyEventArgs e)
    {
        if (!_capturing) return;
        e.Handled = true;
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (key == Key.Escape) { EndCapture(); Keyboard.ClearFocus(); return; }
        if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin) return;
        var mods = Keyboard.Modifiers;
        if (!Hotkey.IsUsable(mods, key))
        {
            HotkeyHint.Text = "Use Ctrl, Alt or Shift together with a key, so typing in the game can't trigger it.";
            HotkeyHint.Foreground = (Brush)FindResource("Warn");
            return;
        }
        var text = new Hotkey(mods, key).ToString();
        _capturing = false;
        _host.SuspendHotkey(false);
        Edit(_edit with { Hotkey = text });
        HotkeyHint.Text = $"{text} it is. Save to start using it.";
        HotkeyHint.Foreground = (Brush)FindResource("Good");
        Keyboard.ClearFocus();
    }
}
