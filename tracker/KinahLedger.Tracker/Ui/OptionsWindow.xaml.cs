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
    /// <summary>
    /// InGame is the Buddy page (the Kinah overlay); Meter the DPS meter's (Steven, 2026-11-17: "Make a buddy settings in
    /// options and a DPS meter setting in the options on the left bar to make them easier to find"). Since the settings
    /// revamp (Steven, 2026-11-17: "the overlays are the most important thing that people want") Server is a part of Sharing
    /// (Account &amp; sharing) and About a part of Updates; both still open their page.
    /// </summary>
    public enum Page { General, Server, Sharing, Reading, InGame, Meter, Timers, Tips, Updates, About, Alerts, Applicants }

    /// <summary>The left bar: the three overlays first, then the rest.</summary>
    internal static readonly (Page Page, string Name)[] OverlayPages = { (Page.InGame, "Buddy"), (Page.Meter, "DPS meter"), (Page.Timers, "Timers"), (Page.Applicants, "Applicants") };
    internal static readonly (Page Page, string Name)[] SettingsPages =
    {
        (Page.General, "General"), (Page.Sharing, "Account & sharing"), (Page.Alerts, "Alerts & sounds"), (Page.Reading, "Game reading"),
        (Page.Tips, "Shortcuts & tips"), (Page.Updates, "Updates & about"),
    };
    /// <summary>Every page on the left bar, in its order.</summary>
    internal static IEnumerable<(Page Page, string Name)> NavPages => OverlayPages.Concat(SettingsPages);

    /// <summary>The page a page is shown on (Server on Account &amp; sharing, About on Updates &amp; about).</summary>
    internal static Page Shown(Page p) => p switch { Page.Server => Page.Sharing, Page.About => Page.Updates, _ => p };
    static readonly string[] GameStartChoices = { "Open the window", "Stay in the tray" };

    readonly MainWindow _host;
    readonly DispatcherTimer _live = new() { Interval = TimeSpan.FromSeconds(1) };
    OptionsChoice _saved, _edit;
    Page _page;
    bool _loading, _capturing, _closeConfirmed;
    /// <summary>The key being recorded is That was wrong's (MarkKeyBox), not the start/stop one.</summary>
    bool _capMark;
    const string HotkeyHintText = "Works while the game has focus. Click Change, then press the keys.";
    const string MarkHintText = "Marks the newest counted line as wrong and saves the last minute for a report. Off until you pick a key.";
    /// <summary>In game › Reset position pressed and not yet saved (it is an action, not one of the choices).</summary>
    bool _resetSpot;

    public OptionsWindow(MainWindow host, Page page)
    {
        InitializeComponent();
        _host = host;
        _saved = _edit = host.CurrentOptions();
        Icon = Native.AppIconSource();
        var version = L.F("Version {0}", Upload.RunPayload.AppVersion);
        NavVersion.Text = version; AboutVersion.Text = version + L.T(" · for Aion 2 (global)");

        BuildNav();
        GameStartBox.ItemsSource = GameStartChoices.Select(L.T).ToList();
        RegionPick.DisplayMemberPath = "Shown"; ServerPick.DisplayMemberPath = "LabelShown";   // the names as the language writes them (factions, regions)
        RegionPick.ItemsSource = Servers.Regions;
        Load(_edit);
        Wire();
        WireLanguage();
        FillTips();     // built now, so the search finds the keys
        FillTimers();   // and the timers' rows (built again each time the page shows)
        FillApplicants();   // and the Applicants page (Ui/OptionsWindow.Applicants.cs)
        L.Apply(this);   // the window's own words in the player's language (Ui/Lang.cs)
        Title = L.T(Title);
        foreach (var link in new[] { WhatsSentLink, WhatsReadLink, ReleasesLink, ReleasesDiscordLink }) foreach (var run in link.Inlines.OfType<System.Windows.Documents.Run>()) run.Text = L.T(run.Text);   // links are not TextBlocks, L.Apply skips them
        foreach (var body in new Panel[] { InGameMore, GeneralMore, SharingMore, ReadingMore, TipsMore }) Fold(body);   // after L.Apply: a fold's head says its title in the language already
        WireSearch();   // Ui/OptionsWindow.Search.cs
        Go(page);

        _live.Tick += (_, _) => RefreshLive();
        _live.Start();
        SourceInitialized += (_, _) => Native.DarkTitleBar(new WindowInteropHelper(this).Handle, Skins.IsDark(_edit.Skin));
        Closing += OnClosingOptions;
        Closed += (_, _) => _live.Stop();
    }

    /// <summary>
    /// General › Look › Language: English, 한국어 or 繁體中文 (or Windows' own), kept at once on its own (not with Save:
    /// the windows are built in one language, so it takes effect at the next start, and the hint says so).
    /// </summary>
    void WireLanguage()
    {
        LanguagePick.ItemsSource = L.Choices.Select(c => c.Code == "auto" ? $"{L.T(c.Name)} ({L.Choices.First(x => x.Code == L.Resolve("auto")).Name})" : c.Name).ToList();
        string picked = L.Picked;
        LanguagePick.SelectedIndex = Math.Max(0, Array.FindIndex(L.Choices, c => c.Code == picked));
        LanguagePick.SelectionChanged += (_, _) =>
        {
            if (LanguagePick.SelectedIndex < 0) return;
            var code = L.Choices[LanguagePick.SelectedIndex].Code;
            L.Pick(code);
            LanguageHint.Text = L.Resolve(code) == L.Code ? L.T("Takes effect the next time Kinah Ledger starts.")
                : L.T("Saved. Close Kinah Ledger from the tray icon and start it again to switch.");
        };
    }

    /// <summary>For the tests: the language picker.</summary>
    internal System.Windows.Controls.ComboBox LanguagePickForTest => LanguagePick;

    /* ---------------- pages ---------------- */

    readonly Dictionary<Page, Button> _navButtons = new();

    /// <summary>The left bar: OVERLAYS (Buddy, DPS meter, Timers), then SETTINGS (the rest).</summary>
    void BuildNav()
    {
        Nav.Children.Clear();
        _navButtons.Clear();
        foreach (var (section, pages) in new[] { ("OVERLAYS", OverlayPages), ("SETTINGS", SettingsPages) })
        {
            var label = new TextBlock { Text = L.T(section) };
            label.SetResourceReference(StyleProperty, "NavSection");
            if (Nav.Children.Count == 0) label.Margin = new Thickness(14, 6, 0, 5);
            Nav.Children.Add(label);
            foreach (var (p, name) in pages)
            {
                var b = new Button { Content = L.T(name), Style = (Style)FindResource("NavItem"), Tag = p == _page ? "True" : "False", Height = 34 };
                var target = p;
                b.Click += (_, _) => Go(target);
                _navButtons[p] = b;
                Nav.Children.Add(b);
            }
        }
    }

    /// <summary>Each page's panel, as the left bar lists them.</summary>
    IEnumerable<(Page Page, Panel Panel)> PagePanels => new (Page, Panel)[]
    {
        (Page.InGame, PageInGame), (Page.Meter, PageMeter), (Page.Timers, PageTimers), (Page.Applicants, PageApplicants), (Page.General, PageGeneral), (Page.Sharing, PageSharing),
        (Page.Alerts, PageAlerts), (Page.Reading, PageReading), (Page.Tips, PageTips), (Page.Updates, PageUpdates),
    };

    public void Go(Page page)
    {
        page = Shown(page);
        _page = page;
        foreach (var (p, el) in PagePanels) el.Visibility = p == page ? Visibility.Visible : Visibility.Collapsed;
        // the style's trigger matches the text "True" (Tag holds an object, so a bool would not match it)
        foreach (var (p, b) in _navButtons) b.Tag = p == page ? "True" : "False";
        if (page == Page.Tips) FillTips();   // the keys as they are now (Ui/OptionsWindow.Tips.cs)
        if (page == Page.Timers) FillTimers();   // the timers overlay, the chime and each timer's picks (Ui/OptionsWindow.Timers.cs)
        if (page == Page.Applicants) FillApplicants();   // the Applicants window's switch, preview and spot (Ui/OptionsWindow.Applicants.cs)
        RefreshFoldCounts();
        Scroller.ScrollToTop();
        RefreshLive();
        RefreshPreview();
    }

    /// <summary>For the tests and renders: the page on show.</summary>
    internal Page PageShownForTest => _page;

    /// <summary>True while the Game reading page is on screen: the main window then sends it fresh frames of the game.</summary>
    public bool WantsPreview => IsVisible && _page == Page.Reading;

    /* ---------------- the edited copy ---------------- */

    /// <summary>Puts a set of choices into the controls without counting it as an edit.</summary>
    void Load(OptionsChoice c)
    {
        _loading = true;
        StartupChk.IsChecked = c.StartWithWindows;
        StartupChk.IsEnabled = _host.CanStartWithWindows;
        if (!_host.CanStartWithWindows) StartupHint.Text = L.T("Available in the downloaded KinahLedger.exe.");
        GameStartBox.SelectedIndex = c.OnGameStart == "tray" ? 1 : 0;
        HideOnCloseChk.IsChecked = c.HideWhenGameCloses;
        CloseToTrayChk.IsChecked = c.CloseToTray;
        MinToTrayChk.IsChecked = c.MinimizeToTray;
        TopmostChk.IsChecked = c.AlwaysOnTop;
        FollowChk.IsChecked = c.FollowActivity;
        QuestsChk.IsChecked = c.Quests;
        CompareChk.IsChecked = c.Compare;
        GrossChk.IsChecked = c.ShowGross;
        RecapChk.IsChecked = c.RunRecap;
        ExplainChk.IsChecked = c.ExplainCounts;
        InvDetailsChk.IsChecked = c.InventoryDetails;
        PacketChk.IsChecked = c.PacketReading;
        PacketStateLbl.Text = c.PacketState.Length > 0 ? L.T("Now: ") + PacketStateText(c.PacketState) : "";
        GrossHint.Text = L.F("Values and profit show after the Market's {0:P0} fee and tax by default. Turn this on to see what things sell for before it.", Data.PriceBook.FeeRate);
        RegionPick.SelectedItem = Servers.Region(c.Region) ?? Servers.Regions[0];
        FillServers(c.Region, c.Server);
        ShareChk.IsChecked = c.Share;
        GearBox.Text = c.GearScore > 0 ? c.GearScore.ToString() : "";
        BuildAlerts(c.Server);
        ReportFramesChk.IsChecked = c.ReportFrames;
        SendFramesChk.IsChecked = c.SendFrames;
        HelpCountingChk.IsChecked = c.HelpCounting;
        BossBestsChk.IsChecked = c.BossBests;
        ShowAccuracyChk.IsChecked = c.ShowAccuracy;
        HudChk.IsChecked = c.Hud;
        HudLockChk.IsChecked = c.HudLocked;
        HudPickIconsChk.IsChecked = c.HudPickIcons;
        HudOpacity.Value = c.HudOpacity; HudOpacityLbl.Text = $"{c.HudOpacity:P0}";
        HudScale.Value = c.HudScale; HudScaleLbl.Text = $"{c.HudScale:P0}";
        WindowScale.Value = c.WindowScale; WindowScaleLbl.Text = $"{c.WindowScale:P0}";
        AutoUpdateChk.IsChecked = c.AutoUpdate;
        AutoUpdateChk.IsEnabled = _host.AppSettings.Update.Check;
        BuildSkins(c.Skin);
        BuildHudFields(c.HudFields);
        BuildHudLooks(c.HudLook);
        LoadRareDrops(c);   // In game › Rare drops, built in Ui/OptionsWindow.RareDrops.cs
        LoadParser();       // In game › Parser, built in Ui/OptionsWindow.Parser.cs
        ShowKeyNotes();     // a shortcut that is off: its quiet line under it
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
            var chip = _host.Chip(L.T(s.Name), current == key, () => Edit(_edit with { Skin = key }, previewSkin: true), L.T(s.Blurb));
            SkinHost.Children.Add(chip);
        }
        SkinHint.Text = L.T(Skins.Get(current).Blurb) is var b ? char.ToUpperInvariant(b[0]) + b[1..] + "." : "";
    }

    /// <summary>One chip per figure the panel can show; a click adds or takes it away (the last one stays).</summary>
    void BuildHudFields(string saved)
    {
        HudFieldHost.Children.Clear();
        var on = HudFields.Parse(saved);
        foreach (var f in HudFields.All)
        {
            var key = f.Key;
            var chip = _host.Chip(L.T(f.Name), on.Contains(key), () =>
            {
                var now = HudFields.Parse(_edit.HudFields);
                if (!now.Remove(key)) now.Add(key);
                if (now.Count == 0) return;   // the panel needs one figure at least
                Edit(_edit with { HudFields = HudFields.Join(now) });
                BuildHudFields(_edit.HudFields);
                BuildHudLooks(_edit.HudLook);   // the Strip's drawing shows the figures picked
            }, L.T(f.Hint));
            HudFieldHost.Children.Add(chip);
        }
    }

    /// <summary>
    /// The Buddy page's look picker (the settings revamp, 2026-11-17): the look in use drawn big at the top of the page over
    /// a scene from the game, at the Size slider's size (scaled down only past the stage); a tile per look of the theme picked
    /// under it, each with a small live drawing. A tile under the mouse draws its look on the stage; a click picks it. The
    /// figure chips (under More) are the Strip's: greyed for the others, which show a set of their own.
    /// </summary>
    void BuildHudLooks(string current)
    {
        current = HudLooks.Parse(current);
        HudLookHost.Children.Clear();
        var sample = _host.HudSample();
        var fields = HudFields.Parse(_edit.HudFields);
        var scene = HudKit.GameScene();   // a scene from the game, whatever the window's skin (Steven, 2026-11-16: "show them over the game")
        var looks = HudThemes.Looks(HudThemeShown(current));   // the theme picked's looks (Ui/OptionsWindow.Themes.cs)
        HudLookHost.Columns = Math.Max(3, looks.Count);
        foreach (var l in looks)
        {
            var key = l.Key;
            FrameworkElement? Face() { try { return HudWindow.Preview(key, fields, sample); } catch (Exception ex) { CrashLog.Write("panel look thumbnail", ex); return null; } }
            var tile = LookTile(key, L.T(l.Name), L.T(l.Hint), key == current, Face, scene, l.Name + " panel look");
            tile.MouseLeftButtonUp += (_, _) => PickHudLook(key);
            tile.KeyDown += (_, e) => { if (e.Key is Key.Enter or Key.Space) { PickHudLook(key); e.Handled = true; } };
            tile.MouseEnter += (_, _) => DrawHudStage(key);
            tile.GotKeyboardFocus += (_, _) => DrawHudStage(key);
            tile.MouseLeave += (_, _) => DrawHudStage(HudLooks.Parse(_edit.HudLook));
            HudLookHost.Children.Add(tile);
        }
        _hudStageKey = null;
        DrawHudStage(current);
        bool strip = current == HudLooks.Strip;
        HudFieldHost.IsEnabled = strip;
        HudFieldHost.Opacity = strip ? 1 : .45;
        HudFieldsNote.Visibility = strip ? Visibility.Collapsed : Visibility.Visible;
        HudResetBtn.ToolTip = HudPositionHint.Text = current == HudLooks.Ticker
            ? L.T("Back to the left of the target bar, at the top of the game's screen. Pop keeps its own spot.")
            : L.T("Back to the top centre of the game's screen, clear of the minimap and the chat.");
    }

    /// <summary>The look the Buddy page's stage draws now (the one in use, or the one under the mouse).</summary>
    string? _hudStageKey;
    double _hudStageScale = double.NaN;

    /// <summary>The stage: one look at its real size (the Size slider's) over the game scene, its name in the corner.</summary>
    void DrawHudStage(string key)
    {
        double scale = _edit.HudScale > 0 ? _edit.HudScale : 1;
        if (_hudStageKey == key && _hudStageScale == scale && HudLookPreview.Children.Count == 1) return;
        var look = HudLooks.All.FirstOrDefault(x => x.Key == key);
        if (look == null) return;
        FrameworkElement face;
        try { face = HudWindow.Preview(key, HudFields.Parse(_edit.HudFields), _host.HudSample()); }
        catch (Exception ex) { CrashLog.Write("panel look preview", ex); return; }
        _hudStageKey = key; _hudStageScale = scale;
        face.LayoutTransform = new ScaleTransform(scale, scale);
        bool inUse = key == HudLooks.Parse(_edit.HudLook);
        HudLookPreview.Children.Clear();
        HudLookPreview.Children.Add(Stage(face, L.T(look.Name), inUse ? L.F("in use · {0:P0}", scale) : L.T("hover to compare · click to pick"), 160));
    }

    /// <summary>
    /// An overlay page's stage: <paramref name="face"/> centred over a scene from the game, at its own size and scaled down
    /// only past the stage (a look is never cut), the look's name and a note in the top-left corner.
    /// </summary>
    static Border Stage(FrameworkElement face, string name, string note, double height)
    {
        face.HorizontalAlignment = HorizontalAlignment.Center;
        face.VerticalAlignment = VerticalAlignment.Center;
        var label = new TextBlock { FontSize = 12 };
        label.Inlines.Add(new System.Windows.Documents.Run(name) { FontWeight = FontWeights.ExtraBold, Foreground = new SolidColorBrush(Color.FromRgb(0xED, 0xEF, 0xF5)) });
        label.Inlines.Add(new System.Windows.Documents.Run("  ·  " + note) { Foreground = new SolidColorBrush(Color.FromRgb(0xAF, 0xB5, 0xC6)) });
        // the name on a dark pill in the corner, readable over any part of the scene
        var pill = new Border
        {
            Child = label, Background = new SolidColorBrush(Color.FromArgb(0xD8, 0x0B, 0x0C, 0x10)), CornerRadius = new CornerRadius(6), Padding = new Thickness(8, 3, 9, 4),
            HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(8, 8, 0, 0),
        };
        var view = new Viewbox { Child = face, Stretch = Stretch.Uniform, StretchDirection = StretchDirection.DownOnly, Margin = new Thickness(14, 34, 14, 10), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        return new Border
        {
            Height = height, CornerRadius = Skins.Corner(12), Background = HudKit.GameScene(), ClipToBounds = true,
            BorderThickness = new Thickness(1), BorderBrush = new SolidColorBrush(Color.FromRgb(0x23, 0x27, 0x36)),
            Child = new Grid { Children = { view, pill } },
        };
    }

    /// <summary>
    /// A look's tile on an overlay page: a small live drawing over the game scene and the look's name; the one in use
    /// outlined (2 px, the Link colour). The card is the pick itself (a click, or Enter or Space).
    /// </summary>
    Border LookTile(string key, string name, string hint, bool on, Func<FrameworkElement?> face, Brush scene, string automationName)
    {
        var f = face();
        var thumb = new Border
        {
            Background = scene, CornerRadius = new CornerRadius(5), Height = 34, Padding = new Thickness(4, 3, 4, 3), ClipToBounds = true,
            Child = f == null ? null : new Viewbox { Child = f, Stretch = Stretch.Uniform, StretchDirection = StretchDirection.DownOnly, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center },
            IsHitTestVisible = false,
        };
        var label = new TextBlock { Text = name, FontSize = 12, FontWeight = on ? FontWeights.Bold : FontWeights.SemiBold, HorizontalAlignment = HorizontalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 5, 0, 0) };
        label.SetResourceReference(TextBlock.ForegroundProperty, on ? "Ink" : "Muted");
        var card = new Border
        {
            CornerRadius = Skins.Corner(8), Margin = new Thickness(0, 0, 6, 0), Padding = new Thickness(5, 5, 5, 5), Cursor = Cursors.Hand, BorderThickness = new Thickness(on ? 2 : 1),
            Child = new StackPanel { Children = { thumb, label } }, ToolTip = hint, Focusable = true, Tag = key,
        };
        card.SetResourceReference(Border.BackgroundProperty, "Card2");
        card.SetResourceReference(Border.BorderBrushProperty, on ? "Link" : "LineStrong");
        card.FocusVisualStyle = (Style)FindResource("FocusRing");
        System.Windows.Automation.AutomationProperties.SetName(card, automationName + (on ? ", in use" : ""));
        return card;
    }

    /// <summary>For the options self-test: the look the Buddy page's stage draws, and the mouse over a look's tile (null: leaving it).</summary>
    internal string? HudStageForTest => _hudStageKey;
    internal void HoverHudLookForTest(string? key)
    {
        if (key == null) { DrawHudStage(HudLooks.Parse(_edit.HudLook)); return; }
        HudLookHost.Children.OfType<Border>().FirstOrDefault(b => (string)b.Tag == key)?.RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice, 0) { RoutedEvent = UIElement.MouseEnterEvent });
    }

    /// <summary>For the options self-test: a look's thumbnail clicked.</summary>
    internal void PickHudLookForTest(string key) => PickHudLook(key);

    void PickHudLook(string key)
    {
        if (_loading || key == _edit.HudLook) return;
        Edit(_edit with { HudLook = key });
        BuildHudLooks(key);
        DrawParserLooks();   // In game › Parser's look row shows the same choice
    }

    void Wire()
    {
        StartupChk.Click += (_, _) => Edit(_edit with { StartWithWindows = StartupChk.IsChecked == true });
        GameStartBox.SelectionChanged += (_, _) => Edit(_edit with { OnGameStart = GameStartBox.SelectedIndex == 1 ? "tray" : "open" });
        HideOnCloseChk.Click += (_, _) => Edit(_edit with { HideWhenGameCloses = HideOnCloseChk.IsChecked == true });
        CloseToTrayChk.Click += (_, _) => Edit(_edit with { CloseToTray = CloseToTrayChk.IsChecked == true });
        MinToTrayChk.Click += (_, _) => Edit(_edit with { MinimizeToTray = MinToTrayChk.IsChecked == true });
        TopmostChk.Click += (_, _) => Edit(_edit with { AlwaysOnTop = TopmostChk.IsChecked == true });
        FollowChk.Click += (_, _) => Edit(_edit with { FollowActivity = FollowChk.IsChecked == true });
        QuestsChk.Click += (_, _) => Edit(_edit with { Quests = QuestsChk.IsChecked == true });
        CompareChk.Click += (_, _) => Edit(_edit with { Compare = CompareChk.IsChecked == true });
        GrossChk.Click += (_, _) => Edit(_edit with { ShowGross = GrossChk.IsChecked == true });
        RecapChk.Click += (_, _) => Edit(_edit with { RunRecap = RecapChk.IsChecked == true });
        ExplainChk.Click += (_, _) => Edit(_edit with { ExplainCounts = ExplainChk.IsChecked == true });
        InvDetailsChk.Click += (_, _) => Edit(_edit with { InventoryDetails = InvDetailsChk.IsChecked == true });
        PacketChk.Click += (_, _) => Edit(_edit with { PacketReading = PacketChk.IsChecked == true });
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
        BossBestsChk.Click += (_, _) => Edit(_edit with { BossBests = BossBestsChk.IsChecked == true });
        ShowAccuracyChk.Click += (_, _) => Edit(_edit with { ShowAccuracy = ShowAccuracyChk.IsChecked == true });
        HudChk.Click += (_, _) => Edit(_edit with { Hud = HudChk.IsChecked == true });
        HudLockChk.Click += (_, _) => Edit(_edit with { HudLocked = HudLockChk.IsChecked == true });
        HudPickIconsChk.Click += (_, _) => Edit(_edit with { HudPickIcons = HudPickIconsChk.IsChecked == true });
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
            if (_hudStageKey != null) DrawHudStage(_hudStageKey);   // the stage at the new size
        };
        AutoUpdateChk.Click += (_, _) => Edit(_edit with { AutoUpdate = AutoUpdateChk.IsChecked == true });
        // the window's size shows on the window as the slider moves (0.10.11); Save keeps it, Cancel puts the saved one back
        WindowScale.ValueChanged += (_, _) =>
        {
            WindowScaleLbl.Text = $"{WindowScale.Value:P0}";
            if (_loading) return;
            Edit(_edit with { WindowScale = Math.Round(WindowScale.Value, 2) });
            _host.PreviewWindowScale(_edit.WindowScale);
        };
        WindowSizeResetBtn.Click += (_, _) => { _host.ResetWindowSize(); WindowSizeWasReset(); };   // at once, as Ctrl+0

        WhatsSentLink.Click += (_, _) => _host.WhatsSent(this);
        WhatsReadLink.Click += (_, _) => _host.WhatsRead(this);
        LinkBtn.Click += (_, _) => _host.LinkProfile();
        ShareWeekBtn.Click += (_, _) => _host.ShareWeek();
        CopyCodeBtn.Click += (_, _) =>
        {
            try { Clipboard.SetText(LinkCodeText.Text); ShowNote(L.T("Link code copied. Paste it into your profile on the site to see your runs there."), "Good"); }
            catch (Exception) { ShowNote(L.T("Couldn't reach the clipboard; the code is ") + LinkCodeText.Text, "Warn"); }
        };
        ResetIdBtn.Click += (_, _) =>
        {
            var r = MessageBox.Show(this, L.T("Replace this PC's install ID?\n\nRuns shared from now on can't be tied to earlier ones, and the link code changes. Your profile keeps the runs already linked."),
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
        HotkeyBox.PreviewMouseLeftButtonDown += (_, e) => { if (!_capturing || _capMark) { StartCapture(); e.Handled = true; } };
        HotkeyBox.PreviewKeyDown += OnHotkeyKey;
        HotkeyBox.LostKeyboardFocus += (_, _) => { if (!_capMark) EndCapture(); };
        HotkeyOffBtn.Click += (_, _) => Edit(_edit with { Hotkey = "" });
        HotkeyResetBtn.Click += (_, _) => Edit(_edit with { Hotkey = Hotkey.Default });
        // That was wrong: the same recording field, off by default (so no Default button)
        MarkKeyChangeBtn.Click += (_, _) => StartCapture(mark: true);
        MarkKeyBox.PreviewMouseLeftButtonDown += (_, e) => { if (!_capturing || !_capMark) { StartCapture(mark: true); e.Handled = true; } };
        MarkKeyBox.PreviewKeyDown += OnHotkeyKey;
        MarkKeyBox.LostKeyboardFocus += (_, _) => { if (_capMark) EndCapture(); };
        MarkKeyOffBtn.Click += (_, _) => Edit(_edit with { MarkWrongKey = "" });
        InGameKeysBtn.Click += (_, _) => Go(Page.Tips);   // In game's pointer to where the two keys now live (2026-11-08)
        // tried on the panel at once, kept on Save, moved back on Cancel, like the rest of the page
        HudPlaceBtn.Click += (_, _) => { if (_host.OpenPlacementGuide() is { } why) ShowNote(why, "Warn"); };
        HudResetBtn.Click += (_, _) => { _resetSpot = true; _host.PreviewHudSpot(); Refresh(); ShowNote(L.T("The panel moves to the top centre of the game's screen. Save keeps it there."), "Good"); };

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
        // the panel's switch, figures, lock, opacity and size: shown on the panel now; Save keeps them, Cancel puts them back
        bool panel = next.Hud != _edit.Hud || next.HudFields != _edit.HudFields || next.HudLocked != _edit.HudLocked || next.HudOpacity != _edit.HudOpacity || next.HudScale != _edit.HudScale;
        _edit = next;
        if (skin && previewSkin) _host.ApplySkin(next.Skin);   // try the look on now; Cancel puts the saved one back
        if (panel) _host.PreviewHudLook(next);
        Refresh();
    }

    /// <summary>The bits of the page that follow the edited copy, and the footer.</summary>
    void Refresh()
    {
        HotkeyBox.Text = _capturing && !_capMark ? L.T("Press the keys…") : _edit.Hotkey.Length > 0 ? _edit.Hotkey : L.T("Off");
        HotkeyOffBtn.IsEnabled = _edit.Hotkey.Length > 0;
        MarkKeyBox.Text = _capturing && _capMark ? L.T("Press the keys…") : _edit.MarkWrongKey.Length > 0 ? _edit.MarkWrongKey : L.T("Off");
        MarkKeyOffBtn.IsEnabled = _edit.MarkWrongKey.Length > 0;
        HotkeyResetBtn.IsEnabled = _edit.Hotkey != Hotkey.Default;
        StartupTip.Visibility = _edit.StartWithWindows && !_edit.CloseToTray ? Visibility.Visible : Visibility.Collapsed;
        ServerWarn.Visibility = _edit.Server.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        bool dirty = _edit != _saved || _resetSpot || ParserDirty;   // the Parser group lives in parser.json (Ui/OptionsWindow.Parser.cs)
        SaveBtn.IsEnabled = dirty;
        if (dirty) Footer(L.T("Unsaved changes. Save puts them into effect."), "Muted");
        else if (FooterNote.Text == L.T("Unsaved changes. Save puts them into effect.")) Footer("", "Muted");
    }

    void Save()
    {
        if (_edit.Server.Length == 0)
        {
            Go(Page.Server);
            Footer(L.T("Pick a server before saving: runs and prices are filed under it."), "Warn");
            return;
        }
        var problem = _host.ApplyOptions(_edit);
        SaveParser();   // In game › Parser's switches (Ui/OptionsWindow.Parser.cs)
        if (_resetSpot) { _host.ResetHudPosition(); _resetSpot = false; }   // Reset position, tried until now
        _saved = _edit = _host.CurrentOptions();
        Load(_edit);
        if (problem != null) { Footer(L.T("Saved, but: ") + problem, "Warn"); return; }
        // Options stays open after a save (Steven, 2026-11-17: "save shouldn't close the options"); Close or Cancel leaves
        Footer(L.T("Saved."), "Good");
    }

    /// <summary>For the self-tests: the footer says the last Save went through.</summary>
    internal bool SavedForTest => FooterNote.Text == L.T("Saved.");
    /// <summary>For the self-tests: Save, then Options closed as a player would after it (Save keeps it open since 2026-11-17); a Save that stopped on a note stays open.</summary>
    internal void SaveForTest()
    {
        Save();
        if (SavedForTest) { _closeConfirmed = true; Close(); }
    }

    /// <summary>Ctrl+= or Ctrl+- pressed in the window while Options is open: the slider here moves to <paramref name="scale"/>, so Save keeps it.</summary>
    public void SetWindowScale(double scale)
    {
        WindowScale.Value = Math.Round(Math.Clamp(scale, MainWindow.MinWindowScale, MainWindow.MaxWindowScale), 2);
        Go(Page.General);
    }

    /// <summary>
    /// Reset size ran (the button here, or Ctrl+0 in the window): the size is saved at 100% already, so the slider shows it and
    /// neither Save nor Cancel has a size left to keep or put back.
    /// </summary>
    public void WindowSizeWasReset()
    {
        _saved = _saved with { WindowScale = 1 };
        _loading = true;
        WindowScale.Value = 1; WindowScaleLbl.Text = $"{1.0:P0}";
        _loading = false;
        _edit = _edit with { WindowScale = 1 };
        Refresh();
    }

    /// <summary>The tracker is quitting: close without asking, unsaved edits dropped and a theme being tried put back.</summary>
    public void Discard()
    {
        _closeConfirmed = true;
        Close();
    }

    /// <summary>
    /// Parked off every screen (the self-tests and renders): nothing of its own opens on a screen. Its save question and its
    /// pop-up lists stay shut (Windows keeps a pop-up on a screen, so one under a parked window lands in a screen's corner).
    /// </summary>
    internal bool Parked => Left < -5000 || Top < -5000;

    void OnClosingOptions(object? sender, CancelEventArgs e)
    {
        EndCapture();
        // a parked window never asks: its question would open on the player's screen
        if (!_closeConfirmed && !Parked && (_edit != _saved || _resetSpot || ParserDirty))
        {
            var r = MessageBox.Show(this, L.T("Save your changes to Kinah Ledger's options?"), "Kinah Ledger", MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
            if (r == MessageBoxResult.Cancel) { e.Cancel = true; return; }
            if (r == MessageBoxResult.Yes)
            {
                e.Cancel = true;
                Dispatcher.BeginInvoke(Save);
                return;
            }
        }
        if (_edit.Skin != _saved.Skin || Skins.Current != _saved.Skin) _host.ApplySkin(_saved.Skin);   // a theme being tried goes back
        EndParserWidthPreview();   // and the meter at its saved width (Ui/OptionsWindow.Parser.cs)
        _host.EndHudPreview();   // and the panel as it was saved: switch, figures, lock, opacity, size and spot
        _host.EndWindowScalePreview();   // and the window at its saved size
    }

    /* ---------------- live state from the main window ---------------- */

    void RefreshLive()
    {
        LinkCodeText.Text = _host.LinkCode();
        ScoreText.Text = _host.QuestScoreLine();   // the quests' points (Ui/MainWindow.Quests.cs)
        ChatStatus.Text = _host.ChatAreaText();
        MarketStatus.Text = _host.MarketLine();
        AutoChatBtn.IsEnabled = _host.ChatManual;
        AutoMarketBtn.IsEnabled = _host.MarketManual;
        RefreshUpdates();
    }

    public void RefreshUpdates()
    {
        VersionTitle.Text = L.F("Version {0}", Upload.RunPayload.AppVersion);
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
        HudThemeShown(_edit.HudLook);   // the theme chips hold their own brushes, and "Match the app" goes once it matches
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

    /// <summary>The hint under the field being recorded: start/stop's, or That was wrong's.</summary>
    TextBlock CaptureHint => _capMark ? MarkKeyHint : HotkeyHint;

    void StartCapture(bool mark = false)
    {
        if (_capturing && _capMark != mark) EndCapture();   // one field records at a time
        _capturing = true; _capMark = mark;
        _host.SuspendHotkey(true);   // the current shortcuts would otherwise start a run or mark a line instead of being recorded
        CaptureHint.Text = L.T("Hold Ctrl and Shift (or Alt and Shift) and press a letter, or Ctrl, Alt or Shift with an F key. Esc cancels.");
        CaptureHint.Foreground = (Brush)FindResource("Accent");
        (mark ? MarkKeyBox : HotkeyBox).Focus();
        Refresh();
    }

    void EndCapture()
    {
        if (!_capturing) return;
        _capturing = false;
        _host.SuspendHotkey(false);
        CaptureHint.Text = L.T(_capMark ? MarkHintText : HotkeyHintText);
        CaptureHint.Foreground = (Brush)FindResource("Muted");
        ShowKeyNotes();
        Refresh();
    }

    /// <summary>
    /// A shortcut that is off (another app has it, or it breaks the rules) says so in the line under it, quietly: the only
    /// place it is ever said (Steven, 2026-11-14: never a pop-up, a tray notice or a line on the panel). One held by another
    /// Kinah Ledger says nothing at all.
    /// </summary>
    void ShowKeyNotes()
    {
        if (_capturing) return;
        void Note(TextBlock hint, string? note, string plain)
        {
            hint.Text = note ?? plain;
            hint.Foreground = (Brush)FindResource(note != null ? "Warn" : "Muted");
        }
        Note(HotkeyHint, _host.HotkeyNote, L.T(HotkeyHintText));
        Note(MarkKeyHint, _host.MarkKeyNote, L.T(MarkHintText));
    }

    /// <summary>For the options test: the lines under the two shortcuts.</summary>
    internal (string StartStop, string Mark) KeyHintsForTest => (HotkeyHint.Text, MarkKeyHint.Text);

    void OnHotkeyKey(object sender, KeyEventArgs e)
    {
        if (!_capturing) return;
        e.Handled = true;
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (key == Key.Escape) { EndCapture(); Keyboard.ClearFocus(); return; }
        if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin) return;
        Record(Keyboard.Modifiers, key);
    }

    /// <summary>
    /// A combination pressed while recording: refused with the reason under the field (the shortcut rules, or the other
    /// shortcut's keys), else taken into the edited copy. The reason, or null when it was taken.
    /// </summary>
    string? Record(ModifierKeys mods, Key key)
    {
        var hint = CaptureHint;
        var why = Hotkey.Refusal(mods, key)   // Ctrl+C, Alt+Tab and the like would be taken from every app on the PC
            ?? (Hotkey.Same(new Hotkey(mods, key).ToString(), _capMark ? _edit.Hotkey : _edit.MarkWrongKey)
                ? (_capMark ? L.T("That's the start/stop shortcut. Pick another key.") : L.T("That's the That was wrong shortcut. Pick another key.")) : null);
        if (why != null)
        {
            hint.Text = why;
            hint.Foreground = (Brush)FindResource("Warn");
            return why;
        }
        var text = new Hotkey(mods, key).ToString();
        _capturing = false;
        _host.SuspendHotkey(false);
        Edit(_capMark ? _edit with { MarkWrongKey = text } : _edit with { Hotkey = text });
        hint.Text = L.F("{0} it is. Save to start using it.", text);
        hint.Foreground = (Brush)FindResource("Good");
        Keyboard.ClearFocus();
        return null;
    }

    /// <summary>For the options self-test: a combination recorded in a field as if pressed there; the reason it was refused, or null.</summary>
    internal string? RecordForTest(bool mark, ModifierKeys mods, Key key)
    {
        StartCapture(mark);
        var why = Record(mods, key);
        if (why != null) EndCapture();
        return why;
    }

    /// <summary>
    /// The game-reading state (Packets/PacketReader.State, English for the logs) in the player's language: "Off: …", the
    /// waiting states, and "On (kind): counting A, B … from the game's server host:port".
    /// </summary>
    internal static string PacketStateText(string s)
    {
        if (L.Code == "en") return s;
        var on = System.Text.RegularExpressions.Regex.Match(s, @"^On \((?<k>[^)]*)\): counting (?<what>.+) from the game's server (?<at>\S+)$");
        if (on.Success)
        {
            var what = on.Groups["what"].Value.Split(", ").Select(w => L.T(w));
            return L.F("On ({0}): counting {1} from the game's server {2}", on.Groups["k"].Value, string.Join(L.T(", "), what), on.Groups["at"].Value);
        }
        if (s.StartsWith("Off: ", StringComparison.Ordinal)) return L.F("Off: {0}", L.T(s["Off: ".Length..]));
        return L.T(s);
    }
}
