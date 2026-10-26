using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using KinahLedger.Data;

namespace KinahLedger.Ui;

/// <summary>
/// Every price says where it is from (Steven, 2026-10-23): in a table the figure is teal when it is the player's own
/// (typed, or read on this PC's Market), dimmed when it is unsure (one listing: left out of totals), and the hover says
/// the source, readings and age. On a card the line under the price says it, and a click on the price lets the player
/// type what they see in the game: kept on this PC (typed-prices.json), used from then on, shared as their read when
/// sharing is on.
/// </summary>
public partial class MainWindow
{
    /// <summary>Option A (the source on a line under each card price) when true; option B (a tag, details on hover) when false.</summary>
    internal static bool CardSourceOnLine = true;

    /// <summary>A table's Price cell: the figure, its tone (Mine, Unsure or none) and its hover.</summary>
    (string Text, string Tone, string Tip) PriceCell(string name)
    {
        if (PriceBook.QuoteOf(name) is not { } q) return ("", "", "");
        var text = (q.From == PriceBook.From.Region ? "≈" : "") + Short(q.Price);
        var tone = q.Mine ? "Mine" : !q.Sure ? "Unsure" : "";
        return (text, tone, PriceHover(name, q));
    }

    /// <summary>The hover for a price: its source line, the board's own line when the player's price is used, the other servers' middle.</summary>
    string PriceHover(string name, PriceBook.Quote q)
    {
        var lines = new List<string> { $"{q.Price:N0} · {PriceBook.Describe(q)}" };
        var server = Server?.Name ?? "your server";
        if (q.Mine && PriceBook.BoardOf(name) is { } b) lines.Add($"{server}'s board: {b.Price:N0} · {PriceBook.Describe(new PriceBook.Quote(b.Price, b.From, b.Readings, b.Sure, b.At))}");
        if (q.From != PriceBook.From.Region && PriceBook.RegionOf(name) is { Servers: >= 2 } r) lines.Add($"Other {Server?.Faction ?? ""} servers in {Server?.Region.Name ?? "your region"}: {r.Price:N0} · middle of {r.Servers}".Replace("  ", " "));
        if (!q.Sure) lines.Add("Unsure: one reading isn't a price, so it's left out of totals and profit.");
        lines.Add("Open the card and click the price to type what you see in the game.");
        return string.Join("\n", lines);
    }

    /// <summary>
    /// A card's price line: the label with where the price is from under it (option A) or a small tag beside the price
    /// (option B), and the price itself, which a click turns into a box to type what the game shows.
    /// </summary>
    FrameworkElement CkPriceLine(string item, string label, Action changed, int qty = 1, string server = "")
    {
        var q = PriceBook.QuoteOf(item);
        var g = new Grid { Margin = new Thickness(20, 3, 20, 3) };
        g.ColumnDefinitions.Add(new ColumnDefinition()); g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var left = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        left.Children.Add(CkText(label, CkInk, 13.5));
        if (q != null && CardSourceOnLine) left.Children.Add(CkText(PriceBook.Describe(q), q.Mine ? Color.FromRgb(0x5E, 0xD8, 0xCB) : q.Sure ? CkFaint : CkWarn, 11.5));
        if (q == null) left.Children.Add(CkText($"no Market price on {(server.Length > 0 ? server : "your server")} yet", CkFaint, 11.5));
        g.Children.Add(left);
        var right = CkPriceEditor(item, q, changed, qty);
        Grid.SetColumn(right, 1); g.Children.Add(right);
        if (q != null) g.ToolTip = PriceHover(item, q);
        if (q is { Sure: false }) left.Opacity = 0.75;
        return g;
    }

    /// <summary>The clickable price: "80,000 ✎" (times qty when given), or "set price" when there is none; a click opens a box.</summary>
    FrameworkElement CkPriceEditor(string item, PriceBook.Quote? q, Action changed, int qty = 1, double size = 13.5)
    {
        var host = new Grid { VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Right };
        var shown = new StackPanel { Orientation = Orientation.Horizontal, Cursor = Cursors.Hand, Background = Brushes.Transparent, ToolTip = "Click to type what you see in the game" };
        var figure = q == null ? "set price" : (q.From == PriceBook.From.Region ? "≈" : "") + (q.Price * qty).ToString("N0");
        var t = CkText(figure, q == null ? CkWarn : q.Mine ? Color.FromRgb(0x5E, 0xD8, 0xCB) : CkGold, size, FontWeights.SemiBold, mono: true);
        if (q is { Sure: false }) t.Opacity = 0.6;
        shown.Children.Add(new Border { Child = t, BorderThickness = new Thickness(0, 0, 0, 1), BorderBrush = Ck(CkGold, 0x55) });
        shown.Children.Add(CkText(" ✎", CkFaint, 11));
        if (!CardSourceOnLine && q != null)
            shown.Children.Add(new Border
            {
                Margin = new Thickness(6, 0, 0, 0), CornerRadius = new CornerRadius(6), Padding = new Thickness(6, 1, 6, 2), VerticalAlignment = VerticalAlignment.Center,
                Background = Ck(q.Mine ? Color.FromRgb(0x5E, 0xD8, 0xCB) : q.Sure ? CkMuted : CkWarn, 0x24),
                Child = CkText(q.Mine ? "YOURS" : !q.Sure ? "UNSURE" : q.From switch { PriceBook.From.Sold => "SOLD", PriceBook.From.Region => "≈ REGION", _ => "BOARD" }, q.Mine ? Color.FromRgb(0x5E, 0xD8, 0xCB) : q.Sure ? CkMuted : CkWarn, 10.5, FontWeights.SemiBold),
            });
        host.Children.Add(shown);

        var edit = new StackPanel { Orientation = Orientation.Horizontal, Visibility = Visibility.Collapsed };
        var box = new TextBox
        {
            Width = 104, FontSize = 13, Padding = new Thickness(6, 3, 6, 3), Background = Ck(CkSlot), BorderBrush = Ck(CkGold), BorderThickness = new Thickness(1), Foreground = Ck(CkGold),
            CaretBrush = Ck(CkGold), FontFamily = new FontFamily("Cascadia Mono, Consolas"), TextAlignment = TextAlignment.Right,
            Text = q?.Price.ToString("N0") ?? "", ToolTip = "One, in Kinah, as the game's Market shows it. Kept on this PC; shared as your read when sharing is on.",
        };
        var save = new Button { Content = "Save", Margin = new Thickness(6, 0, 0, 0), Padding = new Thickness(10, 0, 10, 0), Height = 26, FontSize = 12 };
        save.SetResourceReference(StyleProperty, "PrimaryButton");
        edit.Children.Add(box); edit.Children.Add(save);
        host.Children.Add(edit);

        void Open() { shown.Visibility = Visibility.Collapsed; edit.Visibility = Visibility.Visible; box.Focus(); box.SelectAll(); }
        void Commit()
        {
            long n = long.TryParse(new string(box.Text.Where(char.IsDigit).ToArray()), out var x) ? x : 0;
            if (n <= 0) { shown.Visibility = Visibility.Visible; edit.Visibility = Visibility.Collapsed; return; }
            SetTypedPrice(item, n);
            Dispatcher.BeginInvoke(changed);
        }
        shown.MouseLeftButtonUp += (_, e) => { e.Handled = true; Open(); };
        save.Click += (_, _) => Commit();
        box.KeyDown += (_, e) => { if (e.Key == Key.Enter) Commit(); else if (e.Key == Key.Escape) { shown.Visibility = Visibility.Visible; edit.Visibility = Visibility.Collapsed; } };
        return host;
    }

    /// <summary>A price the player typed: kept on this PC, used at once everywhere, queued as their read when sharing is on.</summary>
    void SetTypedPrice(string item, long price)
    {
        PriceBook.SetTyped(item, price, DateTime.UtcNow);
        _watcher.Market.QueueTyped(item, price);
        Say($"{item}: {price:N0}, as you typed it. Used from now on{(_settings.Upload.Enabled && HasServer ? " and shared as your read" : "")}.", "Good");
        Redraw();
        if (_part == "Crafting") FillCraft(CraftShown);
    }
}
