using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using KinahLedger.Data;

namespace KinahLedger.Ui;

/// <summary>
/// The first-run screen and the skin picker. Everything the tracker shares is filed per server, so the window
/// shows nothing else until region, faction and server are chosen; the same choices sit under Settings afterwards.
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
        BuildSkinChips(SkinChips); BuildSkinChips(SkinRow);
        WelcomeGo.Click += (_, _) => FinishWelcome();
        SetWelcome(!_prefs.Welcomed || !HasServer);
    }

    /// <summary>Welcome on: only the header and the welcome card. Off: the normal window, compact or not as before.</summary>
    void SetWelcome(bool on)
    {
        _welcome = on;
        WelcomeCard.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
        foreach (var fe in new FrameworkElement[] { SessionCard, Tiles, TablesCard, StatusRow, SettingsBtn })
            fe.Visibility = on ? Visibility.Collapsed : Visibility.Visible;
        if (on) { SetupCard.Visibility = Visibility.Collapsed; Tagline.Visibility = Visibility.Visible; }
        else SetCompact(_compact, remember: false);
        if (on) WelcomeHint.Text = HasServer ? "" : "Pick your server to continue.";
    }

    void FinishWelcome()
    {
        if (_wServer == null) { WelcomeHint.Text = "Pick your server to continue."; WelcomeHint.Foreground = B("Warn"); return; }
        RegionBox.SelectedItem = Servers.Region(_wRegion) ?? Servers.Regions[0];
        FillServers();
        ServerPick.SelectedItem = Servers.Find(_wRegion, _wServer.Label);
        _prefs.Welcomed = true;
        SavePrefs();
        SetWelcome(false);
        Say($"Server set to {_wServer.Label} · {_wServer.Region.Name}. Press Start whenever you're ready; the chat and the zone are found on their own.", "Good");
    }

    /* ---------------- chips ---------------- */

    Button Chip(string text, bool on, Action click, string? toolTip = null)
    {
        var b = new Button { Content = text, Padding = new Thickness(14, 7, 14, 7), Margin = new Thickness(0, 0, 8, 8), FontSize = 13.5, MinWidth = 72, ToolTip = toolTip };
        PaintChip(b, on);
        b.Click += (_, _) => click();
        return b;
    }

    void PaintChip(Button b, bool on)
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
        if (_wServer == null) { WelcomeHint.Text = "Pick your server to continue."; WelcomeHint.Foreground = B("Muted"); }
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
        Skins.Apply(key);
        _prefs.Skin = key;
        if (!_demo) _prefs.Save();
        var h = new WindowInteropHelper(this).Handle;
        if (h != IntPtr.Zero) Native.DarkTitleBar(h, Skins.IsDark(key));
        BuildSkinChips(SkinChips); BuildSkinChips(SkinRow);
        BuildRegionChips(); BuildFactionChips(); BuildServerChips();   // chips hold their own brushes, so repaint them
        Preview.Source = Preview.Source;   // keep the preview; nothing else to redraw
    }

    /// <summary>The first-run screen for a layout render, with a sample choice half made.</summary>
    public void ShowWelcomeDemo(string skin = "night")
    {
        _demo = true;
        Skins.Apply(skin);
        _wRegion = "na_e"; _wFaction = "Elyos"; _wServer = Servers.Find("na_e", "Siel (Elyos)");
        BuildRegionChips(); BuildFactionChips(); BuildServerChips(); BuildSkinChips(SkinChips); BuildSkinChips(SkinRow);
        SetWelcome(true);
    }
}
