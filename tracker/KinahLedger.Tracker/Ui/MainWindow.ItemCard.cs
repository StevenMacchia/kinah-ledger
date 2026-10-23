using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Media;
using KinahLedger.Data;

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

        // this run: three slot boxes with the count, the odds and the hour
        body.Children.Add(CkSection("THIS RUN"));
        var tiles = new Grid { Margin = new Thickness(20, 0, 20, 0) };
        for (int i = 0; i < 3; i++) tiles.ColumnDefinitions.Add(new ColumnDefinition());
        void Stat(int col, string value, string label)
        {
            var sp = new StackPanel();
            sp.Children.Add(CkText(value.Length > 0 ? value : "—", CkInk, 19, FontWeights.SemiBold, display: true));
            sp.Children.Add(CkText(label, CkFaint, 11.5));
            var b = CkBox(sp, new Thickness(col == 0 ? 0 : 8, 0, 0, 0), new Thickness(12, 8, 12, 8));
            Grid.SetColumn(b, col); tiles.Children.Add(b);
        }
        Stat(0, r.Drops, "picked up");
        Stat(1, r.DropPct is { Length: > 0 } dp && dp != "—" ? dp : "", gathered ? "per try" : "odds per kill");
        Stat(2, r.PerHour, "an hour");
        body.Children.Add(tiles);
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

        // the site's figures for this zone, last seven days
        body.Children.Add(CkSection(("KINAHLEDGER.COM · " + (zone ?? "THIS ZONE")).ToUpperInvariant()));
        var ze = SiteData.Zone(zone);
        if (SiteData.ZonesAt == null) body.Children.Add(CkNote("Loads every 15 minutes while sharing is on and a server is picked."));
        else if (zone == null) body.Children.Add(CkNote("Shows once the zone is read."));
        else if (ze == null) body.Children.Add(CkNote($"No runs from {zone} on your server yet. Yours will be the first."));
        else if (!ze.Published) body.Children.Add(CkNote($"{ze.Runs} of {SiteData.MinRuns} runs needed before it's shown. Yours count."));
        else if ((ze.Drops.TryGetValue(id.ToString(), out var rate) || ze.Drops.TryGetValue(name, out rate)) && rate != null)
        {
            if (rate.PerKill is double k && k > 0) body.Children.Add(CkKv("Odds per kill", OneIn(k), CkGold));
            if (rate.PerNode is double n && n > 0) body.Children.Add(CkKv("Per try", $"{n:0.##}", CkGold));
            body.Children.Add(CkKv("An hour", $"{rate.PerHour:0.#}", CkGold, $"seen in {rate.Runs} of {ze.Runs} runs, last 7 days"));
        }
        else body.Children.Add(CkNote($"Not seen in {zone} yet, across {ze.Runs} runs."));

        // the game's listed chances: enemies here first, else the best elsewhere
        body.Children.Add(CkSection("LISTED DROP CHANCE"));
        var listed = SiteData.ListedFor(id, name);
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
            if (from.Count > 0) foreach (var (m, ch) in from) body.Children.Add(CkKv(m, ch is double c2 && c2 > 0 ? OneIn(c2) : "listed", CkGold));
            else body.Children.Add(CkNote(gathered ? "Gathered, not dropped: no enemy lists it." : "No enemy table lists it yet."));
        }

        // a price alert on this item, on the player's server (Ui/MainWindow.PriceAlerts.cs)
        if (!gathered || priced) body.Children.Add(CkAlertSection(name));

        var links = new List<(string, string)> { ("Drops on kinahledger.com", SiteUrl + "/#drops") };
        if (listed?.Url is { Length: > 0 } mu && Uri.IsWellFormedUriString(mu, UriKind.Absolute)) links.Add(("MetaBot.GG page", mu));
        var linkLine = CkLinks(links.ToArray());
        // Watch: this item alerts like a rare drop (Ui/MainWindow.Alerts.cs)
        if (linkLine is TextBlock lt)
        {
            lt.Inlines.Add(new Run("   ·   ") { Foreground = Ck(CkFaint) });
            var watch = new Hyperlink(new Run(IsWatched(name) ? "Stop watching" : "Watch: alert me when it drops")) { Foreground = Ck(CkGold), TextDecorations = null };
            watch.Click += (_, _) => { bool on = ToggleWatch(name); ((Run)watch.Inlines.FirstInline).Text = on ? "Stop watching" : "Watch: alert me when it drops"; Say(on ? $"Watching {name}: a chime and the panel light up when it drops." : $"Stopped watching {name}.", "Faint"); };
            lt.Inlines.Add(watch);
        }
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
        if (fe.DataContext is DropRow { Report.Length: > 0, Header: false } r) ShowItemCard(r, fe);
    }
}
