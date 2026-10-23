using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using KinahLedger.Data;

namespace KinahLedger.Ui;

/// <summary>
/// What kinahledger.com works out from every player (0.8.12, Steven's picks of 2026-10-23):
///   • Best crafts today, on the Crafting part: the server's most profitable recipes at its lowest asks, a ranked list
///     (option A); a row opens its recipe card. Vendor costs the player typed come off each profit here.
///   • "Players at your gear: 1.46M/hr here" under "Your best here" on the run card (option A), from the zone's
///     shared runs at the player's gear bracket (Options › Server › Gear score), else every player's.
///   • Where to farm now, in Full view: the best zones for that bracket, with bars.
/// Each shows only what the site has published (5 runs from 3 players); nothing is made up.
/// </summary>
public partial class MainWindow
{
    bool _bestAll;

    /// <summary>The player's gear bracket ("1400-1599"), or null when the gear score isn't set.</summary>
    string? GearBracket => _demo ? "1400-1599" : GearScore.Bracket(_prefs.GearScore);

    /// <summary>For layout review: the recipe card of a best craft (Crafting) or the first drop's card (farming), drawn to a PNG.</summary>
    internal void ShotPlayerCards(string dir, string kind)
    {
        // a card's icons are fetched when it is built: wait for them, let them land, then draw
        void Save(Border card, string file)
        {
            IconCache.Drain(TimeSpan.FromSeconds(8));
            Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.Background);
            SaveCard(card, Path.Combine(dir, file));
        }
        if (kind == "Crafting")
        {
            if (PlayerBoards.Crafts.Count > 2) { var c = PlayerBoards.Crafts[2]; Save(BuildCraftCard(_items.NameOf(c.Id) is { Length: > 1 } n ? n : c.Name, c, 3), "ui-craft-card.png"); }
            Save(BuildCraftCard("Artisan's Orichalcum Longsword"), "ui-craft-card-longsword.png");
        }
        else if (kind == "Open world") ShotItemCard(Path.Combine(dir, "ui-drop-card.png"), _demoZone ?? "Western Cantas Valley");
    }

    /// <summary>A new gear score from Options or the first-run screen: kept, and sent with the next run.</summary>
    void SetGearScore(int score)
    {
        _prefs.GearScore = Math.Clamp(score, 0, 99_999);
        Upload.RunPayload.Gear = GearScore.Payload(_prefs.GearScore);
        if (!_demo) _prefs.Save();
    }

    /// <summary>Which of the three show, for the part and the view (called from ApplyView).</summary>
    void ApplyPlayerView(bool craft, bool farm, bool full)
    {
        BestCraftsCard.Visibility = craft ? Visibility.Visible : Visibility.Collapsed;
        if (craft) FillBestCrafts();
        if (!(full && farm)) WhereFarmCard.Visibility = Visibility.Collapsed;
        else FillWhereFarm(_watcher.Chat?.ZoneView?.Zone ?? _demoZone);
    }

    string? _demoZone;

    /* ---------------- best crafts today ---------------- */

    void FillBestCrafts()
    {
        var host = BestCraftsHost; host.Children.Clear();
        var server = Server?.Name ?? "your server";
        host.Children.Add(Heading($"BEST CRAFTS TODAY · {server.ToUpperInvariant()}"));
        if (PlayerBoards.CraftsAt == null)
        {
            host.Children.Add(Faint(HasServer && _prefs.Share ? "Loads with the price board, every 15 minutes." : "Shows once a server is picked and sharing is on (Options)."));
            return;
        }
        // the site's profit, redone with this player's own prices (typed, or read on this PC) where they have them, and the
        // vendor costs they typed taken off; a recipe still missing a vendor cost says so
        var rows = PlayerBoards.Crafts.Select(c => (c, adj: CraftMath.WithVendor(c with { Profit = OwnProfit(c) }, id => PriceBook.VendorPrice(_items.NameOf(id)))))
            .OrderByDescending(x => x.adj.Profit).ToList();
        if (rows.Count == 0) { host.Children.Add(Faint($"No recipe makes a profit at today's lowest asks on {server}.")); return; }
        int shown = _bestAll ? rows.Count : Math.Min(5, rows.Count);
        for (int i = 0; i < shown; i++) host.Children.Add(BestCraftRow(i + 1, rows[i].c, rows[i].adj.Profit, rows[i].adj.Unpriced, i > 0));
        var foot = new Grid { Margin = new Thickness(0, 8, 0, 0) };
        foot.ColumnDefinitions.Add(new ColumnDefinition()); foot.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var age = (int)Math.Max(0, (DateTime.UtcNow - PlayerBoards.CraftsAt.Value).TotalMinutes);
        foot.Children.Add(Faint($"Lowest asks, updated {(age < 1 ? "just now" : $"{age} min ago")} · profit after the Market's cut"));
        if (rows.Count > 5)
        {
            var more = new TextBlock { FontSize = 12.5, VerticalAlignment = VerticalAlignment.Center };
            var h = new Hyperlink(new Run(_bestAll ? "Show fewer" : $"Show {rows.Count - 5} more"));
            h.SetResourceReference(FrameworkContentElement.StyleProperty, "LinkStyle");
            h.Click += (_, _) => { _bestAll = !_bestAll; FillBestCrafts(); };
            more.Inlines.Add(h);
            Grid.SetColumn(more, 1); foot.Children.Add(more);
        }
        host.Children.Add(foot);
    }

    /// <summary>A best craft's profit with this player's own prices (typed or read on this PC) in place of the board's.</summary>
    long OwnProfit(PlayerBoards.BestCraft c) =>
        CraftMath.OwnProfit(c, id => PriceBook.QuoteOf(_items.NameOf(id)) is { Mine: true } q ? q.Price : null, PriceBook.SellerKeeps);

    FrameworkElement BestCraftRow(int rank, PlayerBoards.BestCraft c, long profit, int vendorUnpriced, bool rule)
    {
        var name = _items.NameOf(c.Id) is { Length: > 1 } n ? n : c.Name;
        var g = new Grid { Background = Brushes.Transparent, Cursor = Cursors.Hand, Margin = new Thickness(0), ToolTip = "Click for the recipe card: materials, what a try costs, what it makes, a price alert." };
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(22) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        g.ColumnDefinitions.Add(new ColumnDefinition());
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var rk = new TextBlock { Text = rank.ToString(), FontSize = 12, VerticalAlignment = VerticalAlignment.Center }; rk.SetResourceReference(TextBlock.ForegroundProperty, "Faint"); rk.SetResourceReference(TextBlock.FontFamilyProperty, "MonoFont");
        g.Children.Add(rk);
        var icon = CkIcon(name, 40, GradeColor(GradeOf(name))); icon.Margin = new Thickness(0, 0, 12, 0); icon.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(icon, 1); g.Children.Add(icon);
        var mid = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        var nt = new TextBlock { Text = name, FontSize = 14.5, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis, Foreground = new SolidColorBrush(GradeColor(GradeOf(name))) };
        var bits = new List<string> { c.Profession + (c.Level is int lv && lv > 0 ? $" {lv}" : ""), $"{c.Success:P0} success", $"costs {Fk(c.Cost)}" };
        if (c.Makes > 1) bits.Insert(1, $"makes ×{c.Makes}");
        var sub = new TextBlock { Text = string.Join(" · ", bits.Where(b => b.Trim().Length > 0)) + (vendorUnpriced > 0 ? $" + {vendorUnpriced} vendor item{(vendorUnpriced == 1 ? "" : "s")}" : ""), FontSize = 12.5, TextTrimming = TextTrimming.CharacterEllipsis };
        sub.SetResourceReference(TextBlock.ForegroundProperty, "Faint");
        mid.Children.Add(nt); mid.Children.Add(sub);
        // where the sell price is from: the player's own, or the board's with its readings and age
        var oq = PriceBook.QuoteOf(name);
        var src = new TextBlock { Text = $"sells {(oq?.Price ?? c.Price):N0} · " + (oq != null ? PriceBook.Describe(oq) : $"kinahledger.com · {c.NObs} readings"), FontSize = 11.5, TextTrimming = TextTrimming.CharacterEllipsis };
        src.SetResourceReference(TextBlock.ForegroundProperty, oq is { Mine: true } ? "Mint" : "Faint");
        mid.Children.Add(src);
        Grid.SetColumn(mid, 2); g.Children.Add(mid);
        var right = new StackPanel { VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(10, 0, 0, 0) };
        var pt = new TextBlock { Text = Signed(profit), FontSize = 17, FontWeight = FontWeights.SemiBold, HorizontalAlignment = HorizontalAlignment.Right };
        pt.SetResourceReference(TextBlock.FontFamilyProperty, "DisplayFont"); pt.SetResourceReference(TextBlock.ForegroundProperty, profit >= 0 ? "Good" : "Bad");
        var per = new TextBlock { Text = vendorUnpriced > 0 ? "per craft, before vendor" : "per craft", FontSize = 11.5, HorizontalAlignment = HorizontalAlignment.Right };
        per.SetResourceReference(TextBlock.ForegroundProperty, "Faint");
        right.Children.Add(pt); right.Children.Add(per);
        Grid.SetColumn(right, 3); g.Children.Add(right);
        var b = new Border { Child = g, Padding = new Thickness(0, 9, 0, 9), BorderThickness = new Thickness(0, rule ? 1 : 0, 0, 0) };
        b.SetResourceReference(Border.BorderBrushProperty, "Line");
        b.MouseLeftButtonUp += (_, _) =>
        {
            if (_card != null) _card.IsOpen = false;
            _card = new Popup { Child = BuildCraftCard(name, c, rank), StaysOpen = false, AllowsTransparency = true, PopupAnimation = PopupAnimation.Fade, Placement = PlacementMode.Mouse, PlacementTarget = b };
            _card.IsOpen = true;
        };
        return b;
    }

    /* ---------------- players at your gear, on the run card ---------------- */

    void ShowFarmLine(string? zone)
    {
        var here = PlayerBoards.Here(GearBracket, zone);
        if (here is not { } h || h.Row.ValueHr is not long v) { FarmLine.Visibility = Visibility.Collapsed; return; }
        FarmLine.Visibility = Visibility.Visible;
        FarmLine.Text = h.AtGear ? $"Players at your gear: {Fk(v)}/hr here" : $"Players here: {Fk(v)}/hr";
        var who = $"{h.Row.Runs} runs from {h.Row.Players} players on {Server?.Label ?? "your server"} in the last 7 days";
        var gear = h.AtGear ? $", gear score {GearBracket}." : GearBracket != null ? $". Not enough players at gear score {GearBracket} yet, so this is everyone's." : ". Set your gear score in Options › Server to compare with players at your gear.";
        FarmLine.ToolTip = $"What players made an hour in {h.Row.Zone}: Kinah plus tradeable loot at today's prices, after the Market's cut.\n{who}{gear}";
        if (_prefs.FullView && _part == "Farming") FillWhereFarm(zone);
    }

    /* ---------------- where to farm now, in Full view ---------------- */

    void FillWhereFarm(string? zone)
    {
        var host = WhereFarmHost; host.Children.Clear();
        if (PlayerBoards.FarmAt == null) { WhereFarmCard.Visibility = Visibility.Collapsed; return; }
        WhereFarmCard.Visibility = Visibility.Visible;
        var (zones, atGear) = PlayerBoards.Top(GearBracket, 5);
        var server = (Server?.Name ?? "your server").ToUpperInvariant();
        host.Children.Add(Heading(atGear ? $"WHERE TO FARM NOW · GEAR {GearBracket!.Replace("-", "–")} · {server}" : $"WHERE TO FARM NOW · ALL PLAYERS · {server}"));
        if (zones.Count == 0)
        {
            host.Children.Add(Faint($"No zone on {Server?.Name ?? "your server"} has {PlayerBoards.MinRuns} runs from {PlayerBoards.MinPlayers} players in the last 7 days yet. Yours count."));
            return;
        }
        long max = zones.Max(z => z.ValueHr ?? 0);
        for (int i = 0; i < zones.Count; i++)
        {
            var z = zones[i];
            var sp = new StackPanel();
            var top = new Grid();
            top.ColumnDefinitions.Add(new ColumnDefinition()); top.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var nameRow = new StackPanel { Orientation = Orientation.Horizontal };
            var zt = new TextBlock { Text = z.Zone, FontSize = 14, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis };
            zt.SetResourceReference(TextBlock.ForegroundProperty, "Ink");
            nameRow.Children.Add(zt);
            if (zone != null && string.Equals(zone, z.Zone, StringComparison.OrdinalIgnoreCase))
            {
                var chip = new Border { CornerRadius = new CornerRadius(8), Padding = new Thickness(7, 1, 7, 2), Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, Background = new SolidColorBrush(Color.FromArgb(0x24, 0x5E, 0xD8, 0xCB)) };
                var ct = new TextBlock { Text = "you are here", FontSize = 11 }; ct.SetResourceReference(TextBlock.ForegroundProperty, "Mint");
                chip.Child = ct; nameRow.Children.Add(chip);
            }
            top.Children.Add(nameRow);
            var vt = new TextBlock { Text = $"{Fk(z.ValueHr ?? 0)}/hr", FontSize = 13.5, FontWeight = FontWeights.SemiBold };
            vt.SetResourceReference(TextBlock.ForegroundProperty, "Gold"); vt.SetResourceReference(TextBlock.FontFamilyProperty, "MonoFont");
            Grid.SetColumn(vt, 1); top.Children.Add(vt);
            sp.Children.Add(top);
            // the bar, as a share of the best zone's hour
            var track = new Grid { Height = 6, Margin = new Thickness(0, 6, 0, 0) };
            double share = max > 0 ? Math.Max(0.02, (double)(z.ValueHr ?? 0) / max) : 0;
            track.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(share, GridUnitType.Star) });
            track.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Math.Max(0.0001, 1 - share), GridUnitType.Star) });
            var bg = new Border { CornerRadius = new CornerRadius(3) }; bg.SetResourceReference(Border.BackgroundProperty, "Inset"); Grid.SetColumnSpan(bg, 2); track.Children.Add(bg);
            track.Children.Add(new Border { CornerRadius = new CornerRadius(3), Background = new LinearGradientBrush(Color.FromRgb(0xB9, 0x78, 0x1F), Color.FromRgb(0xF8, 0xD5, 0x98), 0) });
            sp.Children.Add(track);
            var sub = Faint($"{z.Runs} runs from {z.Players} players, last 7 days · Kinah plus loot after tax"); sub.Margin = new Thickness(0, 4, 0, 0); sub.FontSize = 11.5;
            sp.Children.Add(sub);
            var row = new Border { Child = sp, Padding = new Thickness(0, 8, 0, 8), BorderThickness = new Thickness(0, i > 0 ? 1 : 0, 0, 0) };
            row.SetResourceReference(Border.BorderBrushProperty, "Line");
            host.Children.Add(row);
        }
        host.Children.Add(Faint(atGear ? $"A zone shows once {PlayerBoards.MinRuns} runs from {PlayerBoards.MinPlayers} players at your gear are in. Change your gear score in Options."
            : GearBracket != null ? $"Not enough players at gear score {GearBracket} yet, so this is every player's." : "Every player's runs. Set your gear score in Options › Server to see players at your gear."));
    }

    /* ---------------- small pieces in the window's own look ---------------- */

    FrameworkElement Heading(string text)
    {
        var g = new Grid { Margin = new Thickness(0, 2, 0, 8) };
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); g.ColumnDefinitions.Add(new ColumnDefinition());
        var t = new TextBlock { Text = text, FontSize = 11, FontWeight = FontWeights.Bold, Foreground = Ck(CkGoldRule), Margin = new Thickness(0, 0, 10, 0), VerticalAlignment = VerticalAlignment.Center };
        g.Children.Add(t);
        var rule = new Border { Height = 1, VerticalAlignment = VerticalAlignment.Center, Background = new LinearGradientBrush(Color.FromArgb(0x66, CkGoldRule.R, CkGoldRule.G, CkGoldRule.B), Colors.Transparent, 0) };
        Grid.SetColumn(rule, 1); g.Children.Add(rule);
        return g;
    }

    TextBlock Faint(string text)
    {
        var t = new TextBlock { Text = text, FontSize = 12.5, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center };
        t.SetResourceReference(TextBlock.ForegroundProperty, "Faint");
        return t;
    }

    /// <summary>Sample boards for layout renders (ui-shot --demo): Siel's real zones and recipes, example figures.</summary>
    internal static void LoadPlayerDemo(ItemDictionary items)
    {
        string Id(string n) => items.IdOf(n).ToString();
        var crafts = new[]
        {
            ("Sapphire Powder", "Alchemy", 45, 30, 0.952, 0.0, (string?)null, 0L, 9_999L, "[[\"Sapphire Gemstone\",10,1790,false]]", 17_900L, 233_405L),
            ("Orichalcum Greaves", "Armorsmithing", 3, 1, 0.74, 0.185, null, 0L, 200_000L, "[[\"Orichalcum Ingot\",2,2000,false],[\"Strong Leather\",3,9929,false],[\"Refining Stone\",2,899,false],[\"Odyle\",1,3480,false]]", 39_065L, 91_196L),
            ("Expert's Diamond Earrings", "Handicrafting", 20, 1, 0.768, 0.192, "Expert's Splendent Diamond Earrings", 1_980_000L, 30_000L, "[[\"Splendent Diamond Earrings\",1,60000,false],[\"Expert's Refining Stone\",3,59000,false],[\"Fine Diamond Gemstone\",3,1141,false],[\"Fine Odyle\",2,3098,false]]", 246_619L, 26_886L),
            ("Enhanced Durable Balaur Scale", "Alchemy", 80, 1, 0.952, 0.0, null, 0L, 59_999L, "[[\"Balaur's Essence\",2,14999,false],[\"Superior Dragon Scale Hardener (Bound)\",1,null,true]]", 29_998L, 20_267L),
            ("Fine Durable Balaur Horn", "Blacksmithing", 80, 1, 0.952, 0.0, null, 0L, 59_998L, "[[\"Balaur's Essence\",2,14999,false],[\"Moonstone Hammer (Bound)\",1,null,true]]", 29_998L, 20_266L),
            ("Yggdrasil Wood", "Handicrafting", 25, 1, 0.952, 0.0, null, 0L, 9_555L, "[[\"Yggdrasil Log\",20,3195,false],[\"Sandpaper (Bound)\",10,null,true]]", 63_900L, 16_149L),
        };
        string Mats(string m) => string.Join(",", System.Text.Json.JsonDocument.Parse(m).RootElement.EnumerateArray().Select(a =>
            $"{{\"item\":\"{Id(a[0].GetString()!)}\",\"qty\":{a[1].GetInt32()},\"price\":{(a[2].ValueKind == System.Text.Json.JsonValueKind.Null ? "null" : a[2].GetRawText())},\"vendor\":{a[3].GetRawText()}}}"));
        var cj = "{\"crafts\":[" + string.Join(",", crafts.Select(c =>
            $"{{\"item\":\"{Id(c.Item1)}\",\"name\":\"{c.Item1.Replace("\"", "")}\",\"profession\":\"{c.Item2}\",\"level\":{c.Item3},\"makes\":{c.Item4},\"success\":{c.Item5},\"combo\":{c.Item6},\"upgrade\":{(c.Item7 == null ? "null" : $"{{\"item\":\"{Id(c.Item7)}\",\"price\":{c.Item8}}}")},\"price\":{c.Item9},\"n_obs\":10,\"materials\":[{Mats(c.Item10)}],\"cost\":{c.Item11},\"value\":0,\"profit\":{c.Item12}}}")) + "]}";
        var fj = "{\"publish_rule\":{\"min_runs\":5,\"min_installs\":3},\"brackets\":{" +
            "\"all\":[{\"zone\":\"Northern Tolbas Forest\",\"n_runs\":14,\"n_installs\":6,\"minutes\":900,\"published\":true,\"value_hr\":1640000,\"kina_hr\":1100000},{\"zone\":\"Western Cantas Valley\",\"n_runs\":9,\"n_installs\":4,\"minutes\":600,\"published\":true,\"value_hr\":1310000,\"kina_hr\":900000}]," +
            "\"1400-1599\":[{\"zone\":\"Northern Tolbas Forest\",\"n_runs\":9,\"n_installs\":4,\"minutes\":540,\"published\":true,\"value_hr\":1820000,\"kina_hr\":1200000},{\"zone\":\"Western Cantas Valley\",\"n_runs\":7,\"n_installs\":3,\"minutes\":420,\"published\":true,\"value_hr\":1460000,\"kina_hr\":950000},{\"zone\":\"Night Dew Cave\",\"n_runs\":6,\"n_installs\":3,\"minutes\":360,\"published\":true,\"value_hr\":1310000,\"kina_hr\":880000},{\"zone\":\"Drana Processing Area\",\"n_runs\":5,\"n_installs\":3,\"minutes\":300,\"published\":true,\"value_hr\":1120000,\"kina_hr\":700000},{\"zone\":\"Crimson Isle\",\"n_runs\":3,\"n_installs\":2,\"minutes\":180,\"published\":false}]}}";
        PlayerBoards.Load(cj, fj);
    }
}
