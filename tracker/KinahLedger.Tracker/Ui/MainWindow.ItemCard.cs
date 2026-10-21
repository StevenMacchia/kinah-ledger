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
    internal Border BuildItemCard(DropRow r, string? zone)
    {
        var name = r.Report; var id = _items.IdOf(name);
        var info = _items.Get(id);

        var body = new StackPanel { Width = 360 };
        TextBlock Line(string text, string brush = "Ink", double size = 13, bool bold = false, Thickness? margin = null)
        {
            var t = new TextBlock { Text = text, FontSize = size, TextWrapping = TextWrapping.Wrap, Margin = margin ?? new Thickness(0, 2, 0, 0) };
            t.SetResourceReference(TextBlock.ForegroundProperty, brush);
            if (bold) t.FontWeight = FontWeights.SemiBold;
            body.Children.Add(t); return t;
        }
        void Head(string text) { var t = Line(text, "Gold", 11, true, new Thickness(0, 12, 0, 2)); t.SetResourceReference(TextBlock.FontFamilyProperty, "BodyFont"); }

        var title = Line(name, r.Grade switch { "Rare" => "GradeRare", "Legend" => "GradeLegend", "Unique" => "GradeUnique", "Epic" => "GradeEpic", _ => "CommonInk" }, 16, true, new Thickness(0));
        var kind = string.Join(" · ", new[] { info?.Category is { Length: > 0 } c ? c : (r.Sub.StartsWith("Gathering") ? "Gathering material" : "Drop"), r.Grade }.Where(x => x.Length > 0));
        Line(kind, "Faint", 12);

        Head("THIS RUN");
        var run = new List<string> { $"{r.Drops} picked up" };
        if (r.DropPct.Length > 0 && r.DropPct != "—") run.Add(r.DropPct + (r.Sub.StartsWith("Gathering") ? " per try" : " per kill"));
        if (r.PerHour.Length > 0) run.Add(r.PerHour + " an hour");
        Line(string.Join(" · ", run));
        if (r.Reported) Line("Reported: not sent to the site.", "Warn", 12);
        Line(r.Price is { Length: > 0 } p && p != "—" && p != "-" ? $"Lowest Market price {p}" + (r.Value is { Length: > 0 } v && v != "—" ? $" · yours worth {v}" : "") : "No Market price seen yet on your server.", "Muted", 12.5);

        Head(("ON KINAHLEDGER.COM · " + (zone ?? "zone not read yet") + " · LAST 7 DAYS").ToUpperInvariant());
        var ze = SiteData.Zone(zone);
        if (SiteData.ZonesAt == null) Line("Not loaded yet. It loads every 15 minutes while sharing is on and a server is picked.", "Faint", 12.5);
        else if (zone == null) Line("Once the zone is read, the site's figures for it show here.", "Faint", 12.5);
        else if (ze == null) Line($"No runs from {zone} on your server yet. Yours will be the first.", "Faint", 12.5);
        else if (!ze.Published) Line($"Not published yet: {ze.Runs} of {SiteData.MinRuns} runs. Your runs count toward it.", "Faint", 12.5);
        else if ((ze.Drops.TryGetValue(id.ToString(), out var rate) || ze.Drops.TryGetValue(name, out rate)) && rate != null)
        {
            var parts = new List<string>();
            if (rate.PerKill is double k && k > 0) parts.Add(OneIn(k) + " per kill");
            if (rate.PerNode is double n && n > 0) parts.Add($"{n:0.##} per try");
            parts.Add($"{rate.PerHour:0.#} an hour");
            Line(string.Join(" · ", parts));
            Line($"seen in {rate.Runs} of {ze.Runs} runs", "Faint", 12);
        }
        else Line($"Not seen in {zone} yet, across {ze.Runs} runs.", "Faint", 12.5);

        Head("LISTED DROP CHANCE · GAME FILES VIA METABOT.GG");
        var listed = SiteData.ListedFor(id, name);
        bool Here(SiteData.Source s) => zone != null && (string.Equals(s.Zone, zone, StringComparison.OrdinalIgnoreCase) || string.Equals(s.Map, zone, StringComparison.OrdinalIgnoreCase));
        if (listed != null && listed.Sources.Count > 0)
        {
            var here = listed.Sources.Where(Here).OrderByDescending(s => s.Chance).Take(4).ToList();
            if (here.Count > 0) foreach (var s in here) Line($"{s.Monster}{(s.Level > 0 ? $" (Lv {s.Level})" : "")} · {OneIn(s.Chance)}");
            else
            {
                Line(zone != null ? $"No enemy in {zone} lists it. Best elsewhere:" : "Best listed chances:", "Faint", 12.5);
                foreach (var s in listed.Sources.OrderByDescending(s => s.Chance).Take(3)) Line($"{s.Monster} · {OneIn(s.Chance)} · {(s.Zone.Length > 0 ? s.Zone : s.Map)}");
            }
            if (listed.Sources.Count > 4) Line($"{listed.Sources.Count} enemies list it in all.", "Faint", 12);
        }
        else
        {
            // the tables built into the tracker, when the site's list doesn't have the item yet
            var book = LootBook.Current;
            var from = book.SourcesOf(name).Select(m => (m, book.Find(m, name, id)?.Chance)).OrderByDescending(x => x.Chance ?? 0).Take(4).ToList();
            if (from.Count > 0) foreach (var (m, ch) in from) Line(m + (ch is double c2 && c2 > 0 ? " · " + OneIn(c2) : ""));
            else Line(r.Sub.StartsWith("Gathering") ? "Gathered, not dropped: no enemy table." : "No enemy table lists it yet.", "Faint", 12.5);
        }

        // links: the site, and MetaBot.GG's page for the item when there is one (its terms ask for the link)
        var links = new TextBlock { Margin = new Thickness(0, 12, 0, 0), FontSize = 12.5 };
        void Link(string text, string url)
        {
            if (links.Inlines.Count > 0) links.Inlines.Add(new Run("   ·   "));
            var h = new Hyperlink(new Run(text)) { NavigateUri = new Uri(url) };
            h.SetResourceReference(FrameworkContentElement.StyleProperty, "LinkStyle");
            h.Click += (_, _) => { Open(url); if (_card != null) _card.IsOpen = false; };
            links.Inlines.Add(h);
        }
        Link("Drops on kinahledger.com", SiteUrl + "/#drops");
        if (listed?.Url is { Length: > 0 } mu && Uri.IsWellFormedUriString(mu, UriKind.Absolute)) Link("MetaBot.GG page", mu);
        body.Children.Add(links);

        var border = new Border { Child = body, Padding = new Thickness(18, 14, 18, 14), CornerRadius = new CornerRadius(12), BorderThickness = new Thickness(1),
            Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 18, ShadowDepth = 3, Opacity = 0.45 }, Margin = new Thickness(8) };
        border.SetResourceReference(Border.BackgroundProperty, "Bg");
        border.SetResourceReference(Border.BorderBrushProperty, "LineStrong");
        return border;
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
