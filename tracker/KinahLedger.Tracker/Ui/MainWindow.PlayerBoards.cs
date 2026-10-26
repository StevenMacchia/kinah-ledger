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
    /// <summary>Best crafts rows before "Show all".</summary>
    const int BestCraftsShown = 3;

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

    /// <summary>Best crafts on the Crafting part, Where to farm now on the Farming part (called from ApplyView).</summary>
    void ApplyPlayerView(bool craft, bool farm, bool full)
    {
        if (!craft) { _bestAll = false; _bestOpen.Clear(); }   // folded again the next time the Crafting part opens
        BestCraftsCard.Visibility = craft ? Visibility.Visible : Visibility.Collapsed;
        if (craft) FillBestCrafts();
        if (!farm) WhereSection.Visibility = Visibility.Collapsed;
        else FillWhere(_watcher.Chat?.ZoneView?.Zone ?? _demoZone);
    }

    string? _demoZone;

    /* ---------------- best crafts today ---------------- */

    /// <summary>Best crafts rows tapped open to show the skill, where the price is from and its readings, by recipe.</summary>
    readonly HashSet<string> _bestOpen = new(StringComparer.OrdinalIgnoreCase);

    void FillBestCrafts()
    {
        var host = BestCraftsHost;
        var server = Server?.Name ?? "your server";
        BestNote.Text = $"{server} · profit a craft";
        if (PlayerBoards.CraftsAt == null)
        {
            var note = HasServer && _prefs.Share ? "Loads with the price board, every 15 minutes." : "Shows once a server is picked and sharing is on (Options).";
            if (!Changed("best", note)) return;
            host.Children.Clear();
            host.Children.Add(Faint(note));
            return;
        }
        // the site's profit, with the materials ticked "I have these" taken out of the cost, redone with this player's own
        // prices (typed, or read on this PC) where they have them, and the vendor costs (typed, else shipped) taken off; a
        // recipe still missing a vendor cost says so
        var rows = PlayerBoards.Crafts.Select(c =>
        {
            var f = CraftMath.WithFarmed(c, id => PriceBook.Farmed(_items.NameOf(id)));
            return (c, f.Farmed, cost: f.Craft.Cost, adj: CraftMath.WithVendor(f.Craft with { Profit = OwnProfit(f.Craft) }, id => PriceBook.VendorPrice(_items.NameOf(id))));
        }).OrderByDescending(x => x.adj.Profit).ToList();
        // drawn only when the rows' words change (a price board load, a tick on "I have these", Show all)
        if (!Changed("best", $"{server}|{_bestAll}|" + string.Join(";", rows.Select(x => $"{x.c.Id}|{x.adj.Profit}|{x.adj.Unpriced}|{x.Farmed}|{x.cost}")))) return;
        host.Children.Clear();
        if (rows.Count == 0) { host.Children.Add(Faint($"No recipe makes a profit at today's lowest asks on {server}.")); return; }
        // the column labels, as the drops table's
        var heads = new Grid { Height = 24 };
        heads.ColumnDefinitions.Add(new ColumnDefinition()); heads.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(64) }); heads.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(80) });
        foreach (var (text, col) in new[] { ("Success", 1), ("Profit", 2) })
        {
            var h = new TextBlock { Text = text, FontSize = 11, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 0, 4) };
            h.SetResourceReference(TextBlock.ForegroundProperty, "Faint"); Grid.SetColumn(h, col); heads.Children.Add(h);
        }
        host.Children.Add(heads);
        // the top three; all of them only after a click, and folded again whenever the Crafting part opens (Steven, 2026-10-24)
        int shown = _bestAll ? rows.Count : Math.Min(BestCraftsShown, rows.Count);
        for (int i = 0; i < shown; i++) host.Children.Add(BestCraftRow(i + 1, rows[i].c, rows[i].adj.Profit, rows[i].adj.Unpriced, rows[i].Farmed, rows[i].cost));
        if (rows.Count > BestCraftsShown)
        {
            var more = new TextBlock { FontSize = 12, Margin = new Thickness(0, 8, 0, 0) };
            var h = new Hyperlink(new Run(_bestAll ? "Show top 3" : $"Show all {rows.Count}"));
            h.SetResourceReference(FrameworkContentElement.StyleProperty, "LinkStyle");
            h.Click += (_, _) => { _bestAll = !_bestAll; FillBestCrafts(); };
            more.Inlines.Add(h);
            host.Children.Add(more);
        }
    }

    /// <summary>A best craft's profit with this player's own prices (typed or read on this PC) in place of the board's.</summary>
    long OwnProfit(PlayerBoards.BestCraft c) =>
        CraftMath.OwnProfit(c, id => PriceBook.QuoteOf(_items.NameOf(id)) is { Mine: true } q ? q.Price : null, PriceBook.SellerKeeps);

    /// <summary>
    /// A best craft (direction C): its icon, name and one quiet line (makes, costs, sells each), the success and the profit a
    /// craft in columns. A tap opens the detail (skill and level, where the sell price is from, its readings and age) with the
    /// recipe card a link away.
    /// </summary>
    FrameworkElement BestCraftRow(int rank, PlayerBoards.BestCraft c, long profit, int vendorUnpriced, int farmed = 0, long? cost = null)
    {
        var name = _items.NameOf(c.Id) is { Length: > 1 } n ? n : c.Name;
        bool open = _bestOpen.Contains(name);
        var g = new Grid { Background = Brushes.Transparent, Cursor = Cursors.Hand, ToolTip = open ? null : "Click for the skill, where the price is from and its readings." };
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(40) });
        g.ColumnDefinitions.Add(new ColumnDefinition());
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(64) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(80) });
        var icon = ItemTile(name, c.Id, 30, GradeOf(name)); icon.VerticalAlignment = VerticalAlignment.Top; icon.Margin = new Thickness(0, 2, 0, 0); icon.HorizontalAlignment = HorizontalAlignment.Left;
        g.Children.Add(icon);
        var mid = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) };
        var nt = new TextBlock { Text = name, FontSize = 13, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis, Foreground = new SolidColorBrush(GradeColor(GradeOf(name))) };
        var oq = PriceBook.QuoteOf(name);
        var bits = new List<string>();
        if (c.Makes > 1) bits.Add($"makes ×{c.Makes}");
        bits.Add($"costs {Fk(cost ?? c.Cost)}" + (vendorUnpriced > 0 ? $" + {vendorUnpriced} vendor item{(vendorUnpriced == 1 ? "" : "s")}" : ""));
        bits.Add($"sells {Fk(oq?.Price ?? c.Price)}{(c.Makes > 1 ? " each" : "")}");   // one format on the line (it read "costs 18K · sells 9,999")
        if (farmed > 0) bits.Add($"{farmed} farmed");
        var sub = new TextBlock { Text = string.Join(" · ", bits), FontSize = 12, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 1, 0, 0) };
        sub.SetResourceReference(TextBlock.ForegroundProperty, "Faint");
        mid.Children.Add(nt); mid.Children.Add(sub);
        if (open)
        {
            // behind the tap: the skill, the price's source and its readings, and the recipe card
            var detail = new TextBlock { FontSize = 12, Margin = new Thickness(0, 4, 0, 0), TextWrapping = TextWrapping.Wrap };
            detail.SetResourceReference(TextBlock.ForegroundProperty, "Faint");
            var src = oq != null ? PriceBook.Describe(oq, withAge: false) : $"price from kinahledger.com · {c.NObs} readings";
            var skill = c.Profession + (c.Level is int lv && lv > 0 ? $" {lv}" : "");
            var age = PriceBook.Ago(PlayerBoards.CraftsBoardAt ?? PlayerBoards.CraftsAt ?? DateTime.UtcNow, DateTime.UtcNow);
            detail.Inlines.Add(new Run(string.Join(" · ", new[] { skill, src, age }.Where(x => x.Trim().Length > 0)) + "   "));
            var card = new Hyperlink(new Run("Recipe card")); card.SetResourceReference(FrameworkContentElement.StyleProperty, "LinkStyle");
            card.Click += (_, _) =>
            {
                if (_card != null) _card.IsOpen = false;
                _card = new Popup { Child = BuildCraftCard(name, c, rank), StaysOpen = false, AllowsTransparency = true, PopupAnimation = PopupAnimation.Fade, Placement = PlacementMode.Mouse, PlacementTarget = g };
                _card.IsOpen = true;
            };
            detail.Inlines.Add(card);
            mid.Children.Add(detail);
        }
        Grid.SetColumn(mid, 1); g.Children.Add(mid);
        var sc = new TextBlock { Text = $"{c.Success:P0}", FontSize = 12.5, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 9, 0, 0) };
        sc.SetResourceReference(TextBlock.FontFamilyProperty, "MonoFont"); sc.SetResourceReference(TextBlock.ForegroundProperty, "Faint");
        Grid.SetColumn(sc, 2); g.Children.Add(sc);
        var pt = new TextBlock
        {
            Text = Signed(profit), FontSize = 12.5, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 9, 0, 0),
            ToolTip = vendorUnpriced > 0 ? "A craft's profit before the vendor items without a cost" : "A craft's profit at today's lowest asks, after the Market's cut",
        };
        pt.SetResourceReference(TextBlock.FontFamilyProperty, "MonoFont"); pt.SetResourceReference(TextBlock.ForegroundProperty, profit >= 0 ? "Gold" : "Faint");
        Grid.SetColumn(pt, 3); g.Children.Add(pt);
        var b = new Border { Child = g, Padding = new Thickness(0, 8, 0, 8), BorderThickness = new Thickness(0, 1, 0, 0) };
        b.SetResourceReference(Border.BorderBrushProperty, "Line");
        b.MouseLeftButtonUp += (_, ev) => { if (ev.OriginalSource is FrameworkContentElement) return; if (!_bestOpen.Remove(name)) _bestOpen.Add(name); FillBestCrafts(); };
        return b;
    }

    /* ---------------- players at your gear, on the run card ---------------- */

    void ShowFarmLine(string? zone)
    {
        var here = PlayerBoards.Here(GearBracket, zone);
        if (here is not { } h || h.Row.ValueHr is not long v) { FarmLine.Visibility = Visibility.Collapsed; return; }
        FarmLine.Visibility = Visibility.Visible;
        FarmLine.Text = h.AtGear ? $"Players at your gear {Fk(v)}" : $"Players here {Fk(v)}";   // after "Your best here 1.40M · " (JoinCompare)
        var who = $"{h.Row.Runs} runs from {h.Row.Players} players on {Server?.Label ?? "your server"} in the last 7 days";
        var gear = h.AtGear ? $", gear score {GearBracket}." : GearBracket != null ? $". Not enough players at gear score {GearBracket} yet, so this is everyone's." : ". Set your gear score in Options › Server to compare with players at your gear.";
        FarmLine.ToolTip = $"What players made an hour in {h.Row.Zone}: Kinah plus tradeable loot at today's prices, after the Market's cut.\n{who}{gear}";
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
