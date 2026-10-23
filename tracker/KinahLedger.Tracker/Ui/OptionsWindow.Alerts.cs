using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using KinahLedger.Session;

namespace KinahLedger.Ui;

/// <summary>
/// Options › Server › Price alerts: the watch list for the chosen server, each with its rule and today's price, and
/// Remove. Alerts are set from a drop or recipe card; removing one here takes effect at once (like Watch on a card).
/// </summary>
public partial class OptionsWindow
{
    void BuildAlerts(string server)
    {
        AlertsHost.Children.Clear();
        var list = server.Length > 0 ? PriceAlerts.For(server) : new();
        if (list.Count == 0)
        {
            var t = new TextBlock { Text = server.Length > 0 ? "None yet. Set one from any drop or recipe card: a click on its row." : "Pick a server first: alerts follow its prices.", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(16, 10, 16, 10) };
            t.SetResourceReference(StyleProperty, "RowHint");
            AlertsHost.Children.Add(t);
            return;
        }
        foreach (var a in list.OrderBy(a => a.Item))
        {
            var row = new Grid();
            row.SetResourceReference(StyleProperty, "Row");
            row.ColumnDefinitions.Add(new ColumnDefinition()); row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var left = new StackPanel();
            var name = new TextBlock { Text = a.Item }; name.SetResourceReference(StyleProperty, "RowTitle");
            var now = Data.PriceBook.Price(a.Item) is long p ? $" · now {p:N0}" : " · no price yet";
            var rule = new TextBlock { Text = a.Rule + now + (a.Armed ? "" : " · told you, waits for it to cross back") }; rule.SetResourceReference(StyleProperty, "RowHint");
            left.Children.Add(name); left.Children.Add(rule);
            row.Children.Add(left);
            var link = new Hyperlink(new Run("Remove"));
            link.SetResourceReference(FrameworkContentElement.StyleProperty, "LinkStyle");
            var item = a.Item;
            link.Click += (_, _) => { PriceAlerts.Remove(server, item); BuildAlerts(server); };
            var holder = new TextBlock(link) { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 0) };
            Grid.SetColumn(holder, 1); row.Children.Add(holder);
            AlertsHost.Children.Add(row);
        }
    }
}
