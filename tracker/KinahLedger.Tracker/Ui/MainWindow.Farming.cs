using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using Path = System.Windows.Shapes.Path;

namespace KinahLedger.Ui;

/// <summary>
/// The Farming part as Steven approved it (2026-10-24, mock-farming.html): one card at both widths, made of the run card,
/// the session line, the drops table and a footer; the "…" menu beside the run's buttons; a folded box per gathering skill.
/// </summary>
public partial class MainWindow
{
    /// <summary>
    /// Joins the Farming part's blocks into one card. Compact: the run card, the session line, the drops table and the
    /// status as its footer, square where they meet. Full view: the run card and the session line on the left, the table
    /// on its own on the right, the status a plain line under both. Any other part: the status a plain line.
    /// </summary>
    void JoinCards()
    {
        bool farm = _part == "Farming" && !_welcome, full = farm && _prefs.FullView;
        bool sum = SummaryCard.Visibility == Visibility.Visible;
        SessionCard.CornerRadius = new CornerRadius(6, 6, full && !sum ? 6 : 0, full && !sum ? 6 : 0);
        SessionCard.BorderThickness = new Thickness(1, 1, 1, full && !sum ? 1 : 0);
        SummaryCard.CornerRadius = new CornerRadius(0, 0, full ? 6 : 0, full ? 6 : 0);
        SummaryCard.BorderThickness = new Thickness(1, 0, 1, full ? 1 : 0);
        TablesCard.CornerRadius = new CornerRadius(full ? 6 : 0, full ? 6 : 0, full ? 6 : 0, full ? 6 : 0);
        TablesCard.BorderThickness = new Thickness(1, full ? 1 : 0, 1, full ? 1 : 0);
        TablesCard.Margin = new Thickness(0, full ? 16 : 0, 0, 0);
        bool foot = farm && !full;
        StatusBar.Margin = new Thickness(0, foot ? 0 : 14, 0, 0);
        StatusBar.CornerRadius = new CornerRadius(0, 0, foot ? 6 : 0, foot ? 6 : 0);
        StatusBar.BorderThickness = foot ? new Thickness(1, 0, 1, 1) : new Thickness(0);
        StatusInner.BorderThickness = new Thickness(0, foot ? 1 : 0, 0, 0);
        StatusInner.Padding = foot ? new Thickness(20, 12, 20, 12) : new Thickness(0);
        if (foot) { StatusBar.SetResourceReference(Border.BackgroundProperty, "Card"); StatusBar.SetResourceReference(Border.BorderBrushProperty, "LineStrong"); }
        else { StatusBar.Background = null; StatusBar.BorderBrush = null; }
    }

    /// <summary>A folded box's arrow: pointing right while folded, down and gold once open.</summary>
    void SetArrow(Path arrow, bool open)
    {
        arrow.RenderTransform = new RotateTransform(open ? 90 : 0);
        arrow.SetResourceReference(Shape.StrokeProperty, open ? "Gold" : "Faint");
    }

    /// <summary>The gathering skills whose box is open, by name.</summary>
    readonly HashSet<string> _skillOpen = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// One folded box per gathering skill under the drops ("Mining · ore and gemstones · 6 attempts · 8.5K"): opened, how
    /// many attempts failed, how many came plentiful and the pace. Its items are listed under Gathered.
    /// </summary>
    void FillSkills(List<SkillBox> skills)
    {
        SkillHost.Children.Clear();
        foreach (var sk in skills)
        {
            bool open = _skillOpen.Contains(sk.Name);
            var box = new Border { Style = (Style)FindResource("GroupBox") };
            box.SetResourceReference(Border.BorderBrushProperty, open ? "LineStrong" : "Line");
            var sp = new StackPanel(); box.Child = sp;
            var head = new Grid { Style = (Style)FindResource("GroupHead") };
            head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            head.ColumnDefinitions.Add(new ColumnDefinition());
            head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var arrow = new Path { Style = (Style)FindResource("GroupArrow") }; SetArrow(arrow, open);
            head.Children.Add(arrow);
            var name = new TextBlock { Text = sk.Name, Style = (Style)FindResource("GroupName") }; Grid.SetColumn(name, 1); head.Children.Add(name);
            var sum = new TextBlock { Style = (Style)FindResource("GroupSum") };
            sum.Inlines.Add(new Run($"{sk.Attempts:N0} attempt{(sk.Attempts == 1 ? "" : "s")}"));
            if (sk.Value > 0)
            {
                var dot = new Run("  ·  "); dot.SetResourceReference(TextElement.ForegroundProperty, "Faint"); sum.Inlines.Add(dot);
                var v = new Run(Short(sk.Value)) { FontFamily = (FontFamily)FindResource("MonoFont") }; v.SetResourceReference(TextElement.ForegroundProperty, "Gold"); sum.Inlines.Add(v);
            }
            Grid.SetColumn(sum, 2); head.Children.Add(sum);
            var key = sk.Name;
            head.MouseLeftButtonUp += (_, _) => { if (!_skillOpen.Remove(key)) _skillOpen.Add(key); Redraw(); };
            sp.Children.Add(head);
            if (open)
            {
                var body = new StackPanel { Margin = new Thickness(14, 0, 14, 10) };
                var rule = new Border { BorderThickness = new Thickness(0, 1, 0, 0), Margin = new Thickness(-14, 0, -14, 6) }; rule.SetResourceReference(Border.BorderBrushProperty, "Line");
                body.Children.Add(rule);
                int ok = sk.Attempts - sk.Failed;
                void Line(string left, string right)
                {
                    var g = new Grid { Margin = new Thickness(0, 3, 0, 3) };
                    g.ColumnDefinitions.Add(new ColumnDefinition()); g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                    var l = new TextBlock { Text = left, FontSize = 13 }; l.SetResourceReference(TextBlock.ForegroundProperty, "Muted"); g.Children.Add(l);
                    var r = new TextBlock { Text = right, FontSize = 13, FontFamily = (FontFamily)FindResource("MonoFont") }; r.SetResourceReference(TextBlock.ForegroundProperty, "Ink");
                    Grid.SetColumn(r, 1); g.Children.Add(r);
                    body.Children.Add(g);
                }
                Line("Attempts an hour", Rate(sk.PerHour));
                Line("Failed", sk.Attempts > 0 ? $"{sk.Failed:N0} of {sk.Attempts:N0}" : "—");
                Line("Plentiful (extra yield)", ok > 0 ? $"{Math.Round(100.0 * sk.Plentiful / ok)}% of {ok:N0} that worked" : "—");
                sp.Children.Add(body);
            }
            SkillHost.Children.Add(box);
        }
    }

    /// <summary>The "…" menu beside the run's buttons: All zones, clearing the tables, and the shortcut that works in the game.</summary>
    void ShowMoreMenu()
    {
        var cm = new ContextMenu { PlacementTarget = MoreBtn, Placement = PlacementMode.Bottom, HorizontalOffset = -150 };
        var all = new MenuItem
        {
            Header = "All zones", IsCheckable = true, IsChecked = _prefs.AllZones,
            ToolTip = "Off: the tables start over when you change zone, so they show this zone only. On: they keep counting across every zone. Either way, what goes to the site is kept apart by zone.",
        };
        all.Click += (_, _) => { _prefs.AllZones = all.IsChecked; if (!_demo) _prefs.Save(); Redraw(); Tick(); };
        var clear = new MenuItem { Header = "Clear the tables", ToolTip = "Start the farming tables over (what was counted is shared first)" };
        clear.Click += async (_, _) => await ClearFarmTables();
        var key = new MenuItem { Header = HotkeyText.Length > 0 ? $"{HotkeyText} in game" : "No shortcut set (Options)", IsEnabled = false };
        cm.Items.Add(all); cm.Items.Add(clear); cm.Items.Add(new Separator()); cm.Items.Add(key);
        cm.IsOpen = true;
    }

    /// <summary>Starts the farming tables over (Steven, 2026-10-22); not during a farming run, which Stop ends.</summary>
    async Task ClearFarmTables()
    {
        if (Farming) { Say("A farming run is on: Stop ends it and starts the tables over.", "Warn"); return; }
        if (MessageBox.Show(this, "Clear the farming tables and start counting again? What was counted is shared with the site first, as usual.", "Kinah Ledger", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        await _watcher.ClearFarmingAsync();
        _lastRun = null;
        Redraw();
        Say("Farming tables cleared. Counting starts again from here.", "Faint");
    }
}
