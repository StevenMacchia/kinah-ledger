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
        WelcomeGo.Click += (_, _) => FinishWelcome();
        WelcomeSentLink.Click += (_, _) => WhatsSent();
        WelcomePrivacyLink.Click += (_, _) => Open(PrivacyUrl);
        SetWelcome(!_prefs.Welcomed || !HasServer);
    }

    /// <summary>Welcome on: only the header and the welcome card. Off: the live count.</summary>
    void SetWelcome(bool on)
    {
        _welcome = on;
        WelcomeCard.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
        if (on) WelcomeShareChk.IsChecked = _prefs.Share;
        foreach (var fe in new FrameworkElement[] { StatusRow, OptionsBtn, ServerChip })
            fe.Visibility = on ? Visibility.Collapsed : Visibility.Visible;
        ApplyView();   // the run card, the summary and the tables follow the first-run screen
        Tagline.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
        if (on) WelcomeHint.Text = HasServer ? "" : "Pick your server to continue.";
    }

    void FinishWelcome()
    {
        if (_wServer == null) { WelcomeHint.Text = "Pick your server to continue."; WelcomeHint.SetResourceReference(TextBlock.ForegroundProperty, "Warn"); return; }
        ApplyServer(_wRegion, _wServer.Label);
        bool share = WelcomeShareChk.IsChecked == true;
        if (int.TryParse(new string(WelcomeGear.Text.Where(char.IsDigit).ToArray()), out var gear) && gear > 0) SetGearScore(gear);
        if (share != _prefs.Share) { _prefs.Share = share; MarketSwitch(share); ApplyCounting(); }
        _prefs.Welcomed = true;
        SavePrefs();
        SetWelcome(false);
        // first run: check the chat, the loot list and the HP bar read, step by step; then place the panel over the game
        OpenCalibration(this).Closed += (_, _) => PlacementGuideOwed();
        Say($"Server set to {_wServer.Label} · {_wServer.Region.Name}. Reading starts on its own when the game is open; the chat and the zone are found by themselves.", "Good");
    }

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
        b.FontWeight = on ? FontWeights.SemiBold : FontWeights.Normal;
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
            FactionChips.Children.Add(Chip(f, f == _wFaction, () => { _wFaction = name; _wServer = null; BuildFactionChips(); BuildServerChips(); }));
        }
    }

    void BuildServerChips()
    {
        ServerChips.Children.Clear();
        foreach (var s in Servers.In(_wRegion).Where(s => s.Faction == _wFaction))
        {
            var srv = s;
            ServerChips.Children.Add(Chip(s.Name, _wServer?.Label == s.Label, () => { _wServer = srv; BuildServerChips(); WelcomeHint.Text = $"{srv.Label} · {srv.Region.Name}"; WelcomeHint.Foreground = B("Good"); }));
        }
        WelcomeGo.IsEnabled = _wServer != null;
        if (_wServer == null) { WelcomeHint.Text = "Pick your server to continue."; WelcomeHint.SetResourceReference(TextBlock.ForegroundProperty, "Muted"); }
    }

    /* ---------------- skins ---------------- */

    void BuildSkinChips(Panel host)
    {
        host.Children.Clear();
        foreach (var s in Skins.All)
        {
            var key = s.Key;
            host.Children.Add(Chip(s.Name, Skins.Current == key, () => SetSkin(key), s.Blurb));
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
        var h = new WindowInteropHelper(this).Handle;
        if (h != IntPtr.Zero) Native.DarkTitleBar(h, Skins.IsDark(key));
        _options?.SkinChanged();
        BuildSkinChips(SkinChips);
        BuildRegionChips(); BuildFactionChips(); BuildServerChips();   // chips hold their own brushes, so repaint them
    }

    /// <summary>The first-run screen for a layout render, with a sample choice half made.</summary>
    public void ShowWelcomeDemo(string skin = "night")
    {
        _demo = true;
        Skins.Apply(skin);
        _wRegion = "na_e"; _wFaction = "Elyos"; _wServer = Servers.Find("na_e", "Siel (Elyos)");
        BuildRegionChips(); BuildFactionChips(); BuildServerChips(); BuildSkinChips(SkinChips);
        SetWelcome(true);
    }
}
