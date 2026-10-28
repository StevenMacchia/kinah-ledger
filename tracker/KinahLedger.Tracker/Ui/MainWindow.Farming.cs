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
        // Where to farm now: under LOOT on the left in both views since 2026-10-28, with the card's sides
        WhereSection.BorderThickness = new Thickness(1, 0, 1, 0);
        WhereSection.SetResourceReference(Border.BackgroundProperty, "Card");
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
            ToolTip = "Off: the tables start over when you change zone, so they show this zone only. On: they keep counting across every zone since the tracker started (or the run). The Today line at the top keeps the whole day. Either way, what goes to the site is kept apart by zone.",
        };
        all.Click += (_, _) => { _prefs.AllZones = all.IsChecked; if (!_demo) _prefs.Save(); Redraw(); Tick(); };
        var clear = new MenuItem { Header = "Clear the tables", ToolTip = "Start the farming tables over (what was counted is shared first)" };
        clear.Click += async (_, _) => await ClearFarmTables();
        // the feed's hint, moved here (2026-10-28): a pickup the feed never showed
        var missing = new MenuItem { Header = "A pickup is missing…", ToolTip = "Report a pickup the feed never showed. The report is saved on this PC." };
        missing.Click += (_, _) => ShowLineReport(null, Session.FeedTab.Farming, FeedBox);
        var key = new MenuItem { Header = HotkeyText.Length > 0 ? $"{HotkeyText} in game" : "No shortcut set (Options)", IsEnabled = false };
        cm.Items.Add(all); cm.Items.Add(clear); cm.Items.Add(missing); cm.Items.Add(new Separator());
        // what the page stopped saying while all is well (2026-10-28): how reading goes, the last line read, what was left out, idle time
        foreach (var (text, tip) in MoreInfo())
        {
            var mi = new MenuItem { Header = text, IsEnabled = false, ToolTip = tip };
            ToolTipService.SetShowOnDisabled(mi, true);
            cm.Items.Add(mi);
        }
        cm.Items.Add(new Separator()); cm.Items.Add(key);
        cm.IsOpen = true;
    }

    /// <summary>The ⋯ menu's quiet lines: reading, the last line read, reward lines left out, idle time (each only when there is one).</summary>
    internal List<(string Text, object? Tip)> MoreInfo()
    {
        var info = new List<(string Text, object? Tip)>();
        if (HealthText.Text.Length > 0) info.Add((HealthText.Text, HealthLine.ToolTip));
        if (_lastRead.Length > 0) info.Add((_lastRead, null));
        if (_leftOut.Length > 0) info.Add(("Left out: " + _leftOut, "Quest and payment rewards: bound Kinah, a payment line, or one line far above what the enemies here pay. Never farming income."));
        if (_idleNote.Length > 0) info.Add((char.ToUpperInvariant(_idleNote[0]) + _idleNote[1..], "A minute or more of the game being read with nothing picked up."));
        return info;
    }

    /// <summary>
    /// Starts the farming tables over (Steven, 2026-10-22); not during a farming run, which Stop ends. The tables look
    /// empty at once, with Undo; what was counted is shared and the count starts over when the bar goes (Ui/MainWindow.Undo.cs).
    /// </summary>
    async Task ClearFarmTables()
    {
        if (RunOn) { Say("A farming run is on: Stop ends it and starts the tables over.", "Warn"); return; }
        await Offer("tables", "Farming tables cleared",
            apply: () => { var blank = new KinahLedger.Session.SessionEngine(_items); blank.SetZone(_watcher.Chat?.ZoneView?.Zone); _clearedView = blank; Redraw(); },
            undo: () => { _clearedView = null; Redraw(); Say("The farming tables are back as they were.", "Good"); },
            commit: async () =>
            {
                await _watcher.ClearFarmingAsync();
                _clearedView = null; _lastRun = null;
                Redraw();
                Say("Farming tables cleared. Counting starts again from here.", "Faint");
            });
    }
}
