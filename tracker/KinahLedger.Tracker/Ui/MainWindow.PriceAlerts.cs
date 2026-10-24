using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using KinahLedger.Data;
using KinahLedger.Session;
using WF = System.Windows.Forms;

namespace KinahLedger.Ui;

/// <summary>
/// Price alerts on the cards (0.8.12): a "Price alert" section on every drop and recipe card sets one alert per item on
/// the player's server, below or above a price. Every ten seconds the window checks the prices it already has (the
/// server's board, refreshed every 15 minutes, and asks read on the Market on this PC) and a crossing shows a Windows
/// notice from the tray icon, with the chime the rare-drop alert uses. The list is in Options › Server.
/// </summary>
public partial class MainWindow
{
    DateTime _alertCheck = DateTime.MinValue;

    void PriceAlertTick()
    {
        // the recipe book picks the player's faction's materials (Elyos and Asmodian craft the same items differently)
        RecipeBook.PreferFaction = Server?.Faction switch { "Elyos" => 'E', "Asmodian" => 'A', _ => '\0' };
        if (_demo || Server is not { } s || (DateTime.UtcNow - _alertCheck).TotalSeconds < 10) return;
        _alertCheck = DateTime.UtcNow;
        foreach (var (a, price) in PriceAlerts.Check(s.Label, PriceBook.Price, DateTime.UtcNow))
        {
            var title = $"{a.Item} is {price:N0} on {s.Name}";
            var text = $"{(a.Above ? "Above" : "Below")} your {a.Price:N0} alert. Lowest ask{(PriceBook.SourceOf(a.Item) == PriceBook.From.Market ? ", read on this PC" : " on the server's board")}.";
            try { _tray.ShowBalloonTip(8000, title, text, WF.ToolTipIcon.Info); } catch (Exception ex) { CrashLog.Write("price alert", ex); }
            Chime();
            Say($"Price alert: {title}. {text}", "Good");
        }
    }

    /// <summary>Cards whose price alert is being set or changed: the full editor shows in place of the one line.</summary>
    readonly HashSet<string> _alertEdit = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// The price alert as one line (direction C): "Alert when below 30,000 · change", or "No price alert · set one"; the
    /// link opens the editor (below or above, the price, Set alert) in its place.
    /// </summary>
    FrameworkElement CkAlertLine(string item, Action redo)
    {
        if (_alertEdit.Contains(item) || Server is not { } s) return CkAlertSection(item);
        var cur = PriceAlerts.Find(s.Label, item);
        var g = new Grid { Height = 32, Margin = new Thickness(12, 0, 12, 0) };
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); g.ColumnDefinitions.Add(new ColumnDefinition()); g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        g.Children.Add(new System.Windows.Shapes.Path
        {
            Data = Geometry.Parse("M6,10 A6,6 0 0 1 18,10 V15 L20,18 H4 L6,15 Z M10,20.5 A2,2 0 0 0 14,20.5"), Stroke = Ck(CkFaint), StrokeThickness = 1.5, Width = 14, Height = 14, Stretch = Stretch.Uniform,
            VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0),
        });
        var t = CkText(cur != null ? $"Alert when {(cur.Above ? "above" : "below")} {cur.Price:N0}" : "No price alert", cur != null ? CkMuted : CkFaint, 12.5); Grid.SetColumn(t, 1); g.Children.Add(t);
        var link = new TextBlock { FontSize = 12.5, VerticalAlignment = VerticalAlignment.Center };
        var h = new System.Windows.Documents.Hyperlink(new System.Windows.Documents.Run(cur != null ? "change" : "set one")) { Foreground = Ck(CkGold), TextDecorations = null };
        h.Click += (_, _) => { _alertEdit.Add(item); redo(); };
        link.Inlines.Add(h); Grid.SetColumn(link, 2); g.Children.Add(link);
        return new Border
        {
            Child = g, Margin = new Thickness(16, 12, 16, 0), CornerRadius = new CornerRadius(6), BorderThickness = new Thickness(1), BorderBrush = Ck(CkSlotLine),
            ToolTip = cur != null ? $"A Windows notice when {s.Name}'s lowest ask crosses it. Kept on this PC." : null,
        };
    }

    /// <summary>The card's "Price alert" section: below or above, the price, Set alert; the alert set now, and Remove.</summary>
    FrameworkElement CkAlertSection(string item)
    {
        var sp = new StackPanel();
        sp.Children.Add(CkSection("PRICE ALERT"));
        if (Server is not { } s) { sp.Children.Add(CkNote("Pick your server in Options to set alerts on its prices.")); return sp; }
        var cur = PriceAlerts.Find(s.Label, item);
        bool above = cur?.Above ?? false;

        var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(20, 0, 20, 0) };
        row.Children.Add(CkText("Tell me when it's", CkMuted, 13));
        Border Pill(string text, bool on) => new()
        {
            Child = CkText(text, on ? CkGold : CkMuted, 12.5), Padding = new Thickness(10, 3, 10, 4), Cursor = Cursors.Hand,
            Background = on ? Ck(Color.FromRgb(0x2A, 0x24, 0x14)) : Brushes.Transparent,
        };
        var pills = new StackPanel { Orientation = Orientation.Horizontal };
        var pillBox = new Border { Child = pills, CornerRadius = new CornerRadius(8), BorderThickness = new Thickness(1), BorderBrush = Ck(CkSlotLine), Margin = new Thickness(8, 0, 8, 0), ClipToBounds = true, VerticalAlignment = VerticalAlignment.Center };
        void Draw()
        {
            pills.Children.Clear();
            var b = Pill("below", !above); b.MouseLeftButtonUp += (_, _) => { above = false; Draw(); };
            var a = Pill("above", above); a.MouseLeftButtonUp += (_, _) => { above = true; Draw(); };
            pills.Children.Add(b); pills.Children.Add(a);
        }
        Draw();
        row.Children.Add(pillBox);
        var start = cur?.Price ?? PriceBook.Price(item) ?? PlayerBoards.Crafts.FirstOrDefault(c => c.Id == _items.IdOf(item))?.Price;
        var box = new TextBox
        {
            Width = 96, FontSize = 13, Padding = new Thickness(6, 3, 6, 3), BorderThickness = new Thickness(1), Background = Ck(CkSlot), BorderBrush = Ck(CkFrame),
            Foreground = Ck(CkGold), CaretBrush = Ck(CkGold), FontFamily = new FontFamily("Cascadia Mono, Consolas"), VerticalAlignment = VerticalAlignment.Center,
            Text = start is long p0 ? p0.ToString("N0") : "", ToolTip = "The price, in Kinah. The alert fires when the lowest ask crosses it.",
        };
        row.Children.Add(box);
        var set = new Button { Content = cur != null ? "Update" : "Set alert", Margin = new Thickness(8, 0, 0, 0), Padding = new Thickness(12, 0, 12, 0), Height = 28, FontSize = 12.5, VerticalAlignment = VerticalAlignment.Center };
        set.SetResourceReference(StyleProperty, "PrimaryButton");
        row.Children.Add(set);
        sp.Children.Add(row);

        var note = CkNote(cur != null ? $"Set: {cur.Rule}{(cur.Armed ? "" : " · told you, waits for the price to cross back")}." : $"A Windows notice when {s.Name}'s lowest ask crosses it. Kept on this PC.");
        note.Margin = new Thickness(20, 6, 20, 0);
        sp.Children.Add(note);
        void Commit()
        {
            long n = long.TryParse(new string(box.Text.Where(char.IsDigit).ToArray()), out var x) ? x : 0;
            if (n <= 0) { ((TextBlock)note).Text = "Type a price in Kinah first."; return; }
            PriceAlerts.Set(s.Label, item, above, n);
            ((TextBlock)note).Text = $"Set: {(above ? "above" : "below")} {n:N0}. A Windows notice when it crosses; Options › Server has the list.";
            set.Content = "Update";
            Say($"Price alert set: {item} {(above ? "above" : "below")} {n:N0} on {s.Name}.", "Good");
        }
        set.Click += (_, _) => Commit();
        box.KeyDown += (_, e) => { if (e.Key == Key.Enter) Commit(); };
        if (cur != null)
        {
            var rm = new TextBlock { Margin = new Thickness(20, 4, 20, 0), FontSize = 12 };
            var h = new System.Windows.Documents.Hyperlink(new System.Windows.Documents.Run("Remove the alert")) { Foreground = Ck(CkGold), TextDecorations = null };
            h.Click += (_, _) => { PriceAlerts.Remove(s.Label, item); ((TextBlock)note).Text = "Alert removed."; rm.Visibility = Visibility.Collapsed; set.Content = "Set alert"; };
            rm.Inlines.Add(h);
            sp.Children.Add(rm);
        }
        return sp;
    }
}
