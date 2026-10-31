using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Media;
using KinahLedger.Data;
using KinahLedger.Session;

namespace KinahLedger.Ui;

/// <summary>
/// A click on a drop or gathered row opens a small card beside it, like a tooltip that stays (Steven, 2026-10-21): this
/// run's figures, the price, what kinahledger.com has for the item in the zone the player is in, and the game's listed
/// drop chances for the enemies there (from MetaBot.GG, credited and linked as its terms ask). A click anywhere else closes it.
/// </summary>
public partial class MainWindow
{
    Popup? _card;

    void ShowItemCard(DropRow r, FrameworkElement at)
    {
        if (_card != null) _card.IsOpen = false;
        _card = new Popup { Child = BuildItemCard(r, _watcher.Chat?.Zone), StaysOpen = false, AllowsTransparency = true, PopupAnimation = PopupAnimation.Fade, Placement = PlacementMode.Mouse, PlacementTarget = at };
        _card.IsOpen = true;
    }

    /// <summary>The card's content for one row, in the zone given.</summary>
    /// <param name="tradeable">How many copies can be sold, as worked out when the card first opened (kept when a typed price redraws it).</param>
    internal Border BuildItemCard(DropRow r, string? zone, int? tradeable = null)
    {
        var name = r.Report; var id = _items.IdOf(name);
        // the row's worth is its tradeable copies at the price the table used: how many copies that is, so a new price can value them
        int sell = tradeable ?? (PriceBook.Price(name) is long p00 && p00 > 0 && r.Worth > 0 ? (int)Math.Round(r.Worth / (double)p00) : 0);
        var info = _items.Get(id);
        bool gathered = r.Sub.StartsWith("Gathering");
        var (card, body) = CkShell(440);
        body.Children.Add(CkBand(name, r.Grade, string.Join(" · ", new[] { info?.Category is { Length: > 0 } c ? c : (gathered ? "Gathering material" : "Drop"), r.Grade }.Where(x => x.Length > 0))));
        if (r.Reported) body.Children.Add(CkNote("Reported: kept out of what's sent to the site."));

        // what is live for the item (direction C): its last pickups, newest first, as the feed counted them
        var mine = (_demo ? _demoFeed : _watcher.Chat?.Live.Recent(FeedTab.Farming, LiveFeed.Keep) ?? new()).Where(l => l.Kind == "item" && string.Equals(l.Name, name, StringComparison.OrdinalIgnoreCase)).OrderByDescending(l => l.At).Take(3).ToList();
        if (_demo && mine.Count > 0)
            mine = new() { mine[0], mine[0] with { At = mine[0].At.AddSeconds(-81) }, mine[0] with { At = mine[0].At.AddSeconds(-195), Amount = 2 } };
        if (mine.Count > 0)
        {
            var lv = new StackPanel();
            var cap = new Grid { Margin = new Thickness(12, 6, 12, 2) };
            cap.ColumnDefinitions.Add(new ColumnDefinition()); cap.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            cap.Children.Add(CkText("YOUR LAST PICKUPS", CkGoldRule, 11));
            var n = CkText($"{r.Drops} this session", CkFaint, 11.5); Grid.SetColumn(n, 1); cap.Children.Add(n);
            lv.Children.Add(cap);
            for (int i = 0; i < mine.Count; i++)
            {
                var l = mine[i];
                var g = new Grid { Height = 32, Margin = new Thickness(12, 0, 12, 0) };
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(70) }); g.ColumnDefinitions.Add(new ColumnDefinition()); g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                g.Children.Add(CkText(l.At.ToLocalTime().ToString("HH:mm:ss"), i == 0 ? CkGold : Color.FromRgb(0x5E, 0x65, 0x88), 11.5, mono: true));
                var w = CkText($"{l.Name} ×{l.Amount:N0}", CkInk, 13, FontWeights.SemiBold); Grid.SetColumn(w, 1); g.Children.Add(w);
                var v = CkText(PickupValue(l).Text, CkGold, 12.5, mono: true); Grid.SetColumn(v, 2); g.Children.Add(v);
                var row = new Border { Child = g, BorderThickness = new Thickness(0, i > 0 ? 1 : 0, 0, 0), BorderBrush = Ck(CkSlotLine, 0x73) };
                if (i == 0) row.Background = new LinearGradientBrush(Color.FromArgb(0x1F, CkGold.R, CkGold.G, CkGold.B), Color.FromArgb(0x05, CkGold.R, CkGold.G, CkGold.B), 0);
                lv.Children.Add(row);
            }
            body.Children.Add(new Border { Child = lv, Margin = new Thickness(16, 14, 16, 0), Padding = new Thickness(0, 2, 0, 4), CornerRadius = new CornerRadius(8), BorderThickness = new Thickness(1), BorderBrush = Ck(CkSlotLine), Background = Ck(CkSlot, 0xA6) });
        }
        // the odds and the hour, in one line of figures
        var trio = new TextBlock { Margin = new Thickness(16, 12, 16, 0), FontSize = 12.5, Foreground = Ck(CkFaint) };
        void Fig(string fig, string label) { if (trio.Inlines.Count > 0) trio.Inlines.Add(new System.Windows.Documents.Run("      ")); trio.Inlines.Add(new System.Windows.Documents.Run(fig) { FontFamily = new FontFamily("Cascadia Mono, Consolas"), FontSize = 15, Foreground = Ck(CkInk) }); trio.Inlines.Add(new System.Windows.Documents.Run("  " + label)); }
        if (mine.Count == 0) Fig(r.Drops, "picked up");
        if (r.DropPct is { Length: > 0 } dp) Fig(dp, gathered ? "a try" : dp.EndsWith("a kill") ? "" : "kills");
        if (r.PerHour.Length > 0) Fig(r.PerHour, "an hour");
        body.Children.Add(trio);
        // the price: where it is from under it, a click to type what the game shows; the board's and the other servers' own lines
        var q = PriceBook.QuoteOf(name);
        bool priced = q != null;
        var server = Server?.Name ?? "";
        body.Children.Add(CkSection(("PRICE · " + (server.Length > 0 ? server : "YOUR SERVER")).ToUpperInvariant()));
        void Redo() { if (_card is { IsOpen: true }) _card.Child = BuildItemCard(r, zone, sell); }
        body.Children.Add(CkPriceLine(name, "Lowest Market price", Redo, server: server));
        if (CardSourceOnLine && q != null)
        {
            if (q.Mine && PriceBook.BoardOf(name) is { } b)
                body.Children.Add(CkKv($"{(server.Length > 0 ? server : "Your server")}'s board", b.Price.ToString("N0"), CkMuted, PriceBook.Describe(new PriceBook.Quote(b.Price, b.From, b.Readings, b.Sure, b.At))));
            if (q.From != PriceBook.From.Region && PriceBook.RegionOf(name) is { Servers: >= 2 } rg)
                body.Children.Add(CkKv($"Other {Server?.Region.Name ?? ""} {Server?.Faction ?? ""} servers".Replace("  ", " "), rg.Price.ToString("N0"), CkMuted, $"middle of {rg.Servers} servers"));
        }
        if (q is { Sure: true } && sell > 0) body.Children.Add(CkKv("Yours are worth", (q.Price * sell).ToString("N0"), CkGold, $"{sell} × {q.Price:N0}, before the Market's cut"));
        else if (q is { Sure: false }) body.Children.Add(CkNote("One listing isn't a price: it stays out of the session total and the value per hour until a second reading backs it. Click it to type what you see."));

        // the site's figures for this zone, last seven days: only when it has them (an empty section is hidden)
        var ze = SiteData.Zone(zone);
        if (ze is { Published: true } && (ze.Drops.TryGetValue(id.ToString(), out var rate) || ze.Drops.TryGetValue(name, out rate)) && rate != null)
        {
            body.Children.Add(CkSection(("KINAHLEDGER.COM · " + (zone ?? "THIS ZONE")).ToUpperInvariant()));
            if (rate.PerKill is double k && k > 0) body.Children.Add(CkKv("Odds per kill", OneIn(k), CkGold));
            if (rate.PerNode is double n && n > 0) body.Children.Add(CkKv("Per try", $"{n:0.##}", CkGold));
            body.Children.Add(CkKv("An hour", $"{rate.PerHour:0.#}", CkGold, $"seen in {rate.Runs} of {ze.Runs} runs, last 7 days"));
        }

        // the game's listed chances: enemies here first, else the best elsewhere; hidden when no enemy lists it
        var listed = SiteData.ListedFor(id, name);
        var bookFrom = LootBook.Current.SourcesOf(name);
        if ((listed?.Sources.Count ?? 0) > 0 || bookFrom.Count > 0) body.Children.Add(CkSection("LISTED DROP CHANCE"));
        bool Here(SiteData.Source s) => zone != null && (string.Equals(s.Zone, zone, StringComparison.OrdinalIgnoreCase) || string.Equals(s.Map, zone, StringComparison.OrdinalIgnoreCase));
        if (listed != null && listed.Sources.Count > 0)
        {
            var here = listed.Sources.Where(Here).OrderByDescending(s => s.Chance).Take(4).ToList();
            if (here.Count > 0) foreach (var s in here) body.Children.Add(CkKv(s.Monster, OneIn(s.Chance), CkGold, s.Level > 0 ? $"Lv {s.Level}" : ""));
            else
            {
                body.Children.Add(CkNote(zone != null ? $"No enemy in {zone} lists it. Best elsewhere:" : "Best listed chances:"));
                foreach (var s in listed.Sources.OrderByDescending(s => s.Chance).Take(3)) body.Children.Add(CkKv(s.Monster, OneIn(s.Chance), CkGold, s.Zone.Length > 0 ? s.Zone : s.Map));
            }
            if (listed.Sources.Count > 4) body.Children.Add(CkNote($"{listed.Sources.Count} enemies list it in all."));
        }
        else
        {
            // the tables built into the tracker, when the site's list doesn't have the item yet
            var book = LootBook.Current;
            var from = book.SourcesOf(name).Select(m => (m, book.Find(m, name, id)?.Chance)).OrderByDescending(x => x.Chance ?? 0).Take(4).ToList();
            foreach (var (m, ch) in from) body.Children.Add(CkKv(m, ch is double c2 && c2 > 0 ? OneIn(c2) : "listed", CkGold));
        }

        // a price alert on this item, on the player's server, as one line (Ui/MainWindow.PriceAlerts.cs)
        if (!gathered || priced) body.Children.Add(CkAlertLine(name, Redo));

        var links = new List<(string, string)> { ("Drops on kinahledger.com", SiteUrl + "/#drops") };
        if (listed?.Url is { Length: > 0 } mu && Uri.IsWellFormedUriString(mu, UriKind.Absolute)) links.Add(("MetaBot.GG page", mu));
        var linkLine = CkLinks(links.ToArray());
        // Watch: this item alerts like a rare drop (Ui/MainWindow.Alerts.cs)
        var watch = new Hyperlink(new Run(IsWatched(name) ? "Stop the drop alert" : "Alert me when it drops")) { Foreground = Ck(CkGold), TextDecorations = null };
        watch.Click += (_, _) => { bool on = ToggleWatch(name); ((Run)watch.Inlines.FirstInline).Text = on ? "Stop the drop alert" : "Alert me when it drops"; Say(on ? $"Watching {name}: a chime and the panel light up when it drops." : $"Stopped watching {name}.", "Faint"); };
        linkLine.Inlines.InsertBefore(linkLine.Inlines.FirstInline, new Run("   ·   ") { Foreground = Ck(CkFaint) });
        linkLine.Inlines.InsertBefore(linkLine.Inlines.FirstInline, watch);
        // Set a goal, beside Watch (0.9): how many of it to pick up today, in the goal sheet (Ui/MainWindow.Goals.cs)
        var goal = new Hyperlink(new Run("Set a goal")) { Foreground = Ck(CkGold), TextDecorations = null };
        goal.Click += (_, _) => { if (_card != null) _card.IsOpen = false; OpenGoalSheetLater(null, GoalDraft.ForItem(name), null); };
        linkLine.Inlines.InsertBefore(linkLine.Inlines.FirstInline, new Run("   ·   ") { Foreground = Ck(CkFaint) });
        linkLine.Inlines.InsertBefore(linkLine.Inlines.FirstInline, goal);
        // Looks wrong? (or its Undo) at the end, what a right-click on a LOOT slot used to hide (2026-10-31)
        var rep = new Hyperlink(new Run(r.Reported ? "Reported · Undo" : "Looks wrong? Report")) { Foreground = Ck(CkGold), TextDecorations = null, ToolTip = "Keeps it out of what this count sends to the site, and saves the last minute of what was read." };
        rep.Click += (_, _) => { if (_card != null) _card.IsOpen = false; ToggleReport(r); };
        linkLine.Inlines.Add(new Run("   ·   ") { Foreground = Ck(CkFaint) });
        linkLine.Inlines.Add(rep);
        linkLine.TextWrapping = TextWrapping.Wrap;   // five links run past the card's width on one line
        linkLine.LineHeight = 20;
        body.Children.Add(linkLine);
        return card;
    }

    /// <summary>For layout review: the card for the first drop row, drawn to a PNG.</summary>
    internal void ShotItemCard(string path, string zone)
    {
        if (Rows.Items.Count == 0 || Rows.Items[0] is not DropRow r) return;
        var b = BuildItemCard(r, zone);
        b.Resources = Resources;
        b.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity)); b.Arrange(new Rect(b.DesiredSize)); b.UpdateLayout();
        var bmp = new System.Windows.Media.Imaging.RenderTargetBitmap((int)Math.Ceiling(b.ActualWidth + 16), (int)Math.Ceiling(b.ActualHeight + 16), 96, 96, PixelFormats.Pbgra32);
        bmp.Render(b);
        var enc = new System.Windows.Media.Imaging.PngBitmapEncoder(); enc.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bmp));
        using var f = File.Create(path); enc.Save(f);
    }

    /// <summary>A click on an item row (not on its report link) opens the card.</summary>
    void ItemRowClick(object sender, System.Windows.Input.MouseButtonEventArgs ev)
    {
        if (ev.OriginalSource is not FrameworkElement fe) return;     // a link inside the row (Looks wrong?) handles its own click
        if (RowOf(fe.DataContext) is { Report.Length: > 0, Header: false } r) ShowItemCard(r, fe);
    }
}
