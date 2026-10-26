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
    /// Joins the Farming part's blocks into one card (direction C). Compact: the live card, Session, the drops and the chips
    /// (with Where to farm now after them) and the status as its footer, square where they meet. Full view: the live card,
    /// Session, Where to farm now and the footer as one card on the left; the drops and the chips as one card on the right.
    /// Crafting: its own card, the status a plain line under it. Events: the status a plain line.
    /// </summary>
    void JoinCards()
    {
        bool farm = _part == "Farming" && !_welcome, full = farm && FullShown;
        bool foot = farm;
        SessionCard.CornerRadius = new CornerRadius(6, 6, 0, 0);
        SessionCard.BorderThickness = new Thickness(1, 1, 1, 0);
        SummaryCard.CornerRadius = new CornerRadius(0);
        SummaryCard.BorderThickness = new Thickness(1, 0, 1, 0);
        TablesCard.CornerRadius = new CornerRadius(full ? 6 : 0);
        TablesCard.BorderThickness = new Thickness(1, full ? 1 : 0, 1, full ? 1 : 0);
        TablesCard.Margin = new Thickness(0, full ? 16 : 0, 0, 0);
        TablesCard.Padding = new Thickness(0, full ? 4 : 0, 0, full ? 4 : 0);
        // Where to farm now: its own sides on the left in Full view; inside the tables' card in Compact
        WhereSection.BorderThickness = full ? new Thickness(1, 0, 1, 0) : new Thickness(0);
        if (full) WhereSection.SetResourceReference(Border.BackgroundProperty, "Card"); else WhereSection.Background = null;
        CraftCard.CornerRadius = new CornerRadius(6);
        CraftCard.BorderThickness = new Thickness(1);
        StatusBar.Margin = new Thickness(0, foot ? 0 : 14, 0, 0);
        StatusBar.CornerRadius = new CornerRadius(0, 0, foot ? 6 : 0, foot ? 6 : 0);
        StatusBar.BorderThickness = foot ? new Thickness(1, 0, 1, 1) : new Thickness(0);
        StatusInner.BorderThickness = new Thickness(0, foot ? 1 : 0, 0, 0);
        StatusInner.Padding = foot ? new Thickness(20, 12, 20, 12) : new Thickness(0);
        if (foot) { StatusBar.SetResourceReference(Border.BackgroundProperty, "Card"); StatusBar.SetResourceReference(Border.BorderBrushProperty, "LineStrong"); }
        else { StatusBar.Background = null; StatusBar.BorderBrush = null; }
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
