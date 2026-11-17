using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using KinahLedger.Data;

namespace KinahLedger.Ui;

/// <summary>
/// The first-run screen and the skin picker. Everything the tracker shares is filed per server, so the window
/// shows nothing else until region, faction and server are chosen; the same choices sit in Options afterwards.
/// </summary>
public partial class MainWindow
{
    bool _welcome;
    string _wRegion = "na_e", _wFaction = "Elyos";
    GameServer? _wServer;

    static readonly string[] Factions = { "Elyos", "Asmodian" };

    void InitWelcome()
    {
        _wRegion = RegionKey; _wServer = Server; _wFaction = Server?.Faction ?? "Elyos";
        BuildRegionChips(); BuildFactionChips(); BuildServerChips();
        BuildSkinChips(SkinChips);
        WelcomeGo.Click += (_, _) => WelcomeServerDone();
        WelcomeLookGo.Click += (_, _) => FinishWelcome();
        WelcomeSentLink.Click += (_, _) => WhatsSent();
        WelcomePrivacyLink.Click += (_, _) => Open(PrivacyUrl);
        WelcomeNext.Text = WelcomeNextLine(HotkeyText.Replace("+", "\u2060+\u2060"));   // the key kept on one line (word joiners)
        SetWelcome(!_prefs.Welcomed || !HasServer);
    }

    /// <summary>Welcome on: only the header and the welcome card. Off: the live count.</summary>
    void SetWelcome(bool on)
    {
        _welcome = on;
        WelcomeCard.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
        WelcomeLookCard.Visibility = Visibility.Collapsed;   // the look step comes after Continue
        if (on) WelcomeShareChk.IsChecked = _prefs.Share;
        // the page tabs and the site button wait too (0.10.11): nothing to switch to before a server is chosen
        foreach (var fe in new FrameworkElement[] { StatusRow, OptionsBtn, WhoRow, PartTabs, OverlaysBtn })
            fe.Visibility = on ? Visibility.Collapsed : Visibility.Visible;
        ApplyView();   // the run card, the summary and the tables follow the first-run screen
        Tagline.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
        if (on) WelcomeHint.Text = HasServer ? "" : L.T("Pick your server to continue.");
    }

    /// <summary>The welcome card's last line: what to do once Continue is pressed, with the in-game key when one is set.</summary>
    internal static string WelcomeNextLine(string hotkey) =>
        hotkey.Length > 0 ? L.F("Then: open Aion 2, keep the chat on screen, and press Start run (or {0} in the game).", hotkey)
                          : L.T("Then: open Aion 2, keep the chat on screen, and press Start run.");

    /// <summary>The server step's Continue: on to the look step, once a server is picked.</summary>
    void WelcomeServerDone()
    {
        if (_wServer == null) { WelcomeHint.Text = L.T("Pick your server to continue."); WelcomeHint.SetResourceReference(TextBlock.ForegroundProperty, "Warn"); return; }
        _wLook = HudLook;   // Buddy on a new install (HudLooks.Default)
        _wTheme = HudThemes.Of(_wLook);
        WelcomeCard.Visibility = Visibility.Collapsed;
        WelcomeLookCard.Visibility = Visibility.Visible;
        BuildWelcomeLooks();
        WelcomeLookGo.Focus();
    }

    void FinishWelcome()
    {
        if (_wServer == null) { SetWelcome(true); return; }
        ApplyServer(_wRegion, _wServer.Label);
        ApplyHudLook(_wLook);   // a pick by hand, Buddy kept included: written with the prefs below
        bool share = WelcomeShareChk.IsChecked == true;
        if (int.TryParse(new string(WelcomeGear.Text.Where(char.IsDigit).ToArray()), out var gear) && gear > 0) SetGearScore(gear);
        if (share != _prefs.Share) { _prefs.Share = share; MarketSwitch(share); ApplyCounting(); }
        _prefs.Welcomed = true;
        SavePrefs();
        SetWelcome(false);
        // first run: check the chat, the loot list and the HP bar read, step by step; then place the panel over the game
        OpenCalibration(this).Closed += (_, _) => PlacementGuideOwed();
        Say(L.F("Server set to {0} · {1}. Reading starts on its own when the game is open; the chat and the zone are found by themselves.", _wServer.LabelShown, _wServer.Region.Shown), "Good");
    }

    /* ---------------- the look step ---------------- */

    string _wLook = HudLooks.Default;
    /// <summary>The theme whose looks the step shows (Ui/Themes/HudThemes.cs): Classic, Wacky, Tactical or Glitch.</summary>
    string _wTheme = HudThemes.Classic;

    /// <summary>The looks the step offers for a theme: Classic's in the step's own order, a theme's in its picker order.</summary>
    internal static IReadOnlyList<string> WelcomeLooksOf(string theme) =>
        theme == HudThemes.Classic ? WelcomeLooks : HudThemes.Looks(theme).Select(l => l.Key).ToList();

    /// <summary>The look step's buttons, in their order: Buddy, the default, first; then the fight looks; then the other two older ones.</summary>
    internal static readonly string[] WelcomeLooks = { HudLooks.Card, HudLooks.Auto, HudLooks.Capsule, HudLooks.Meter, HudLooks.Float, HudLooks.Strip, HudLooks.Ticker };

    /// <summary>The game under the looks, whatever the window's skin (as Options' look picker: a real farming frame, HudKit.GameScene).</summary>
    static Brush WelcomeScene => HudKit.GameScene();

    /// <summary>A button per look, the picked one outlined; hovering one draws it on the stage, leaving draws the picked one again.</summary>
    void BuildWelcomeLooks()
    {
        // the themes first: a chip each, the one shown lit; its looks under them
        WelcomeThemeHost.Children.Clear();
        foreach (var t in HudThemes.All)
        {
            var tk = t.Key;
            var chip = Chip(L.T(t.Name), tk == _wTheme, () => PickWelcomeTheme(tk), L.T(t.Hint));
            chip.Tag = "theme:" + tk;
            WelcomeThemeHost.Children.Add(chip);
        }
        WelcomeLookHost.Children.Clear();
        foreach (var key in WelcomeLooksOf(_wTheme))
        {
            var l = HudLooks.All.First(x => x.Key == key);
            bool on = key == _wLook;
            var name = new TextBlock { Text = L.T(l.Name), FontSize = 13.5, FontWeight = on ? FontWeights.Bold : FontWeights.SemiBold, HorizontalAlignment = HorizontalAlignment.Center };
            name.SetResourceReference(TextBlock.ForegroundProperty, on ? "Ink" : "Muted");
            var b = new Border
            {
                CornerRadius = new CornerRadius(8), Margin = new Thickness(0, 0, 8, 8), Padding = new Thickness(14, 8, 14, 8), MinWidth = 72, Cursor = System.Windows.Input.Cursors.Hand,
                BorderThickness = new Thickness(on ? 2 : 1), Child = name, ToolTip = L.T(l.Hint), Focusable = true, Tag = key,
            };
            b.SetResourceReference(Border.BackgroundProperty, on ? "AccentDim" : "Card2");
            b.SetResourceReference(Border.BorderBrushProperty, on ? "Accent" : "Line");
            if (TryFindResource("FocusRing") is Style ring) b.FocusVisualStyle = ring;
            System.Windows.Automation.AutomationProperties.SetName(b, l.Name + " overlay look" + (on ? ", picked" : ""));
            b.MouseLeftButtonUp += (_, _) => PickWelcomeLook(key);
            b.KeyDown += (_, e) => { if (e.Key is System.Windows.Input.Key.Enter or System.Windows.Input.Key.Space) { PickWelcomeLook(key); e.Handled = true; } };
            b.MouseEnter += (_, _) => ShowWelcomeLook(key);
            b.GotKeyboardFocus += (_, _) => ShowWelcomeLook(key);
            b.MouseLeave += (_, _) => ShowWelcomeLook(_wLook);
            WelcomeLookHost.Children.Add(b);
        }
        WelcomeLookStage.Tag = null;
        ShowWelcomeLook(_wLook);
    }

    /// <summary>The stage draws a look at most this much larger than in the game at 100%.</summary>
    internal const double WelcomeLookMaxScale = 1.5;

    void PickWelcomeLook(string key)
    {
        _wLook = HudLooks.Parse(key);
        BuildWelcomeLooks();
    }

    /// <summary>A theme picked: its looks shown, its first picked (the look in use kept when it is the theme's).</summary>
    void PickWelcomeTheme(string theme)
    {
        if (theme == _wTheme) return;
        _wTheme = theme;
        if (HudThemes.Of(_wLook) != theme) _wLook = WelcomeLooksOf(theme).First();
        WelcomeLookStage.Tag = null;
        BuildWelcomeLooks();
    }

    /// <summary>The stage: one look over the dark scene with the sample figures Options draws, as large as the stage holds.</summary>
    void ShowWelcomeLook(string key)
    {
        if (WelcomeLookStage.Tag as string == key) return;
        var l = HudLooks.All.First(x => x.Key == key);
        FrameworkElement face;
        try { face = HudWindow.Preview(key, HudFields.Parse(_prefs.HudFields), HudSample()); }
        catch (Exception ex) { CrashLog.Write("welcome look preview", ex); return; }
        // as large as the stage holds, up to 1.5 times (the panel's own Size goes to 2): the small pills would be lost at 1x
        // in the stage that Pop, the tallest, needs (Steven, 2026-11-15: the Parser's look thumbnails were too small)
        face.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        var room = new Size((WelcomeLookStage.ActualWidth > 0 ? WelcomeLookStage.ActualWidth : 617) - 34, WelcomeLookStage.Height - 34);
        double scale = Math.Min(WelcomeLookMaxScale, Math.Min(room.Width / Math.Max(1, face.DesiredSize.Width), room.Height / Math.Max(1, face.DesiredSize.Height)));
        face.LayoutTransform = new ScaleTransform(scale, scale);
        face.HorizontalAlignment = HorizontalAlignment.Center;
        face.VerticalAlignment = VerticalAlignment.Center;
        WelcomeLookStage.Background = WelcomeScene;
        WelcomeLookStage.Tag = key;
        WelcomeLookStage.Child = face;
        WelcomeLookName.Inlines.Clear();
        var n = new System.Windows.Documents.Run(L.T(l.Name)) { FontWeight = FontWeights.Bold };
        n.SetResourceReference(System.Windows.Documents.TextElement.ForegroundProperty, "Ink");
        WelcomeLookName.Inlines.Add(n);
        WelcomeLookName.Inlines.Add(new System.Windows.Documents.Run("  ·  " + L.T(l.Hint)));
    }

    /// <summary>For the self-test and the render: the look step (Ui/WpfApp.cs).</summary>
    internal void WelcomeServerDoneForTest() => WelcomeServerDone();
    internal void FinishWelcomeForTest() => FinishWelcome();
    internal void PickWelcomeLookForTest(string key) => PickWelcomeLook(key);
    internal void PickWelcomeThemeForTest(string theme) => PickWelcomeTheme(theme);
    internal (string Theme, List<string> Chips) WelcomeThemeForTest => (_wTheme, WelcomeThemeHost.Children.OfType<FrameworkElement>().Select(c => c.Tag as string ?? "").ToList());
    internal void HoverWelcomeLookForTest(string? key)
    {
        var b = WelcomeLookHost.Children.OfType<Border>().FirstOrDefault(x => (string)x.Tag == (key ?? _wLook));
        b?.RaiseEvent(new System.Windows.Input.MouseEventArgs(System.Windows.Input.Mouse.PrimaryDevice, 0) { RoutedEvent = key == null ? UIElement.MouseLeaveEvent : UIElement.MouseEnterEvent });
    }
    internal (bool StepShown, string Picked, string? Staged, List<string> Buttons, string? Outlined) WelcomeLookForTest => (
        WelcomeLookCard.Visibility == Visibility.Visible && WelcomeCard.Visibility != Visibility.Visible, _wLook, WelcomeLookStage.Tag as string,
        WelcomeLookHost.Children.OfType<Border>().Select(b => (string)b.Tag).ToList(),
        WelcomeLookHost.Children.OfType<Border>().FirstOrDefault(b => b.BorderThickness.Left > 1)?.Tag as string);

    /* ---------------- chips ---------------- */

    public Button Chip(string text, bool on, Action click, string? toolTip = null)
    {
        var b = new Button { Content = text, Padding = new Thickness(14, 7, 14, 7), Margin = new Thickness(0, 0, 8, 8), FontSize = 13.5, MinWidth = 72, ToolTip = toolTip };
        PaintChip(b, on);
        b.Click += (_, _) => click();
        return b;
    }

    public void PaintChip(Button b, bool on)
    {
        b.Background = B(on ? "AccentDim" : "Card2");
        b.BorderBrush = B(on ? "Accent" : "Line");
        b.Foreground = B(on ? "Ink" : "Muted");
        b.FontWeight = on ? FontWeights.Normal : FontWeights.Normal;
        b.Tag = on;
    }

    void BuildRegionChips()
    {
        RegionChips.Children.Clear();
        foreach (var r in Servers.Regions)
        {
            var key = r.Key;
            RegionChips.Children.Add(Chip(r.Name, key == _wRegion, () => { _wRegion = key; _wServer = null; BuildRegionChips(); BuildServerChips(); }));
        }
    }

    void BuildFactionChips()
    {
        FactionChips.Children.Clear();
        foreach (var f in Factions)
        {
            var name = f;
            FactionChips.Children.Add(Chip(L.T(f), f == _wFaction, () => { _wFaction = name; _wServer = null; BuildFactionChips(); BuildServerChips(); }));
        }
    }

    void BuildServerChips()
    {
        ServerChips.Children.Clear();
        foreach (var s in Servers.In(_wRegion).Where(s => s.Faction == _wFaction))
        {
            var srv = s;
            ServerChips.Children.Add(Chip(s.Name, _wServer?.Label == s.Label, () => { _wServer = srv; BuildServerChips(); WelcomeHint.Text = $"{srv.LabelShown} · {srv.Region.Shown}"; WelcomeHint.Foreground = B("Good"); }));
        }
        WelcomeGo.IsEnabled = _wServer != null;
        if (_wServer == null) { WelcomeHint.Text = L.T("Pick your server to continue."); WelcomeHint.SetResourceReference(TextBlock.ForegroundProperty, "Muted"); }
    }

    /* ---------------- skins ---------------- */

    void BuildSkinChips(Panel host)
    {
        host.Children.Clear();
        foreach (var s in Skins.All)
        {
            var key = s.Key;
            host.Children.Add(Chip(L.T(s.Name), Skins.Current == key, () => SetSkin(key), L.T(s.Blurb)));
        }
    }

    void SetSkin(string key)
    {
        ApplySkin(key);
        _prefs.Skin = key;
        if (!_demo) _prefs.Save();
    }

    /// <summary>Paints every open window in a look without saving it (Options previews a look until Save or Cancel).</summary>
    public void ApplySkin(string key)
    {
        Skins.Apply(key);
        RepaintTray();
        var h = new WindowInteropHelper(this).Handle;
        if (h != IntPtr.Zero) Native.DarkTitleBar(h, Skins.IsDark(key));
        _options?.SkinChanged();
        BuildSkinChips(SkinChips);
        BuildRegionChips(); BuildFactionChips(); BuildServerChips();   // chips hold their own brushes, so repaint them
    }

    /// <summary>The first-run screen for a layout render, with a sample choice half made.</summary>
    public void ShowWelcomeDemo(string skin = "night")
    {
        _demo = true; Motion.Still = true;
        Skins.Apply(skin);
        // KL_WELCOME_REGION=eu: the region with the most servers, to check the chips still fit the card (2026-10-26)
        var region = Servers.Region(Environment.GetEnvironmentVariable("KL_WELCOME_REGION"))?.Key ?? "na_e";
        _wRegion = region; _wFaction = "Elyos";
        _wServer = region == "eu" ? Servers.Find("eu", "Tiamat (Elyos)") : Servers.Find(region, "Siel (Elyos)");
        BuildRegionChips(); BuildFactionChips(); BuildServerChips(); BuildSkinChips(SkinChips);
        SetWelcome(true);
    }
}
