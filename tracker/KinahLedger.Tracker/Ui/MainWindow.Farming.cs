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
        // the Quest look (2026-11-07): every card its own rounded surface with the one-pixel edge; the status a plain line under the column
        bool full = _part == "Farming" && !_welcome && FullShown;
        TablesCard.CornerRadius = Skins.Corner(12);
        TablesCard.BorderThickness = new Thickness(1);
        TablesCard.SetResourceReference(Border.BorderBrushProperty, "CardLine");
        TablesCard.Margin = new Thickness(0, full ? 12 : 0, 0, 0);
        TablesCard.Padding = new Thickness(0, 4, 0, 4);
        StatusBar.Margin = new Thickness(0, 8, 0, 0);
        StatusBar.CornerRadius = new CornerRadius(0);
        StatusBar.BorderThickness = new Thickness(0);
        StatusBar.Background = null; StatusBar.BorderBrush = null;
        StatusInner.BorderThickness = new Thickness(0);
        StatusInner.Padding = new Thickness(4, 0, 4, 0);
    }

    /// <summary>
    /// The ⋯ menu beside the run's buttons, in the card's own look (Ui/MainWindow.Menus.cs, 2026-10-31): Set a goal, All
    /// zones as a switch, Clear everything, a missing pickup; then the page's quiet lines and the shortcut that works in the game.
    /// </summary>
    void ShowMoreMenu() => ShowMenu(MoreBtn, MoreEntries());

    internal List<MenuEntry> MoreEntries()
    {
        // a goal for today (0.9): Kinah, an item or runs; up to three, shown over LOOT (Ui/MainWindow.Goals.cs)
        bool room = _goals.Count < Session.Goals.Max;
        var list = new List<MenuEntry>
        {
            // Share and Compact/Full left the header (UX pass 2026-11-13): they act on this page only, and the header's buttons
            // are the same on every page now. Share opens its sheet in this card, as the header's button did
            _shareReady
                ? new MenuEntry(L.T(ShareText), () => ToggleShare("Farming"), Sub: L.T("This session, the last run or the week"), Key: CopyPictureKey,
                    Tip: L.F("A picture to paste in Discord or anywhere. Nothing is uploaded.\n{0} copies this session's picture without opening it.", CopyPictureKey))
                : new MenuEntry(L.T(ShareText), Sub: L.T("Nothing to share yet: it opens once the game is being read.")),
            new(L.T(FullViewText), () => SetView(!FullShown), On: FullShown,
                Sub: L.T(_fullTooWide && !FullShown ? "Needs a wider screen than this one" : "Twice as wide: the drop tables beside the loot"),
                Tip: L.T("On: the window doubles in width, with the drops, Who pays and the odds beside the live card. Off (Compact): one column. Farming only; Crafting, Runs and Quests keep one column.")),
            MenuEntry.Line,
            // Inventory (0.10.85): this session's drops first, the whole bag when chosen, in a window of its own (Ui/MainWindow.Inventory.cs)
            InventoryMenuEntry(),
            room ? new MenuEntry(L.T("Set a goal…"), () => OpenGoalSheet(null, new GoalDraft(), MoreBtn), Tip: L.T("Kinah, an item or runs to reach today. Up to three, shown over LOOT."))
                 : new MenuEntry(L.T("Set a goal…"), Sub: L.T("Three goals are set. Click one to change or remove it.")),
            // Keep above other windows (0.10.26): the same switch as Options › General, for a windowed game on one screen
            new(L.T(AlwaysOnTopText), () => { SetAlwaysOnTop(!_prefs.AlwaysOnTop); if (!_demo) _prefs.Save(); }, On: _prefs.AlwaysOnTop,
                Sub: L.T("Stays over the game and other windows"),
                Tip: L.T("On: this window stays above the game and every other window, for a windowed game on one screen. The panel over the game is on top either way.")),
            // New session's way back into the menu (0.9.13): Start run is the card's fresh start; this clears without a run
            new(L.T(ClearAllText), () => _ = ClearFarmTables(), Sub: L.T("The tables start over, no run"), Key: NewSessionKey,
                Tip: L.T("Start the farming tables over without starting a run. What was counted is shared first, Undo brings the tables back, and the Today strip keeps the day.")),
            // the feed's hint, moved here (2026-10-28): a pickup the feed never showed
            new(L.T("A pickup is missing…"), () => ShowLineReport(null, Session.FeedTab.Farming, FeedBox), Tip: L.T("Report a pickup the feed never showed.")),
            // Save as CSV (0.10.11, Ui/MainWindow.Export.cs): the day's loot for a spreadsheet
            new(L.T(SaveLootText), SaveLootCsv, Sub: L.T("Every item today, with counts, worth and zones"), Tip: L.T("One file for a spreadsheet: today's Kinah, every drop and gathered item, how many, what they are worth after the Market's cut, and the zones each came from. Nothing is uploaded.")),
            new(L.T(SaveWeekText), SaveWeekCsv, Sub: L.T("Seven days: made, time, kills, zones and runs"), Tip: L.T("One file for a spreadsheet, a row a day: what farming made after the Market's cut, the Kinah, the time spent farming, kills, gathering attempts and the zones; the day's runs, how many counted, their Kinah and the places. Nothing is uploaded.")),
        };
        // what the page stopped saying while all is well (2026-10-28): how reading goes, the last line read, what was left out, idle time
        var info = MoreInfo();
        if (info.Count > 0) { list.Add(MenuEntry.Line); foreach (var (text, tip) in info) list.Add(new(text, Tip: tip, Info: true)); }
        list.Add(MenuEntry.Line);
        list.Add(new(HotkeyText.Length > 0 ? L.F("{0} in game starts or stops farming", HotkeyText) : L.T("No shortcut set (Options)"), Info: true));
        return list;
    }

    /// <summary>The ⋯ menu's quiet lines: reading, the last line read, reward lines left out, idle time (each only when there is one).</summary>
    internal List<(string Text, object? Tip)> MoreInfo()
    {
        var info = new List<(string Text, object? Tip)>();
        if (HealthText.Text.Length > 0) info.Add((HealthText.Text, HealthLine.ToolTip));
        if (_lastRead.Length > 0) info.Add((_lastRead, null));
        info.Add((_watcher.ShareLine, _watcher.ShareFailing ? L.F(_watcher.ChunksWaiting == 1 ? "{0} batch waiting; they go out with the next one (every 15 minutes), up to a day's worth." : "{0} batches waiting; they go out with the next one (every 15 minutes), up to a day's worth.", _watcher.ChunksWaiting) : L.T("Counts are shared with kinahledger.com in 15-minute batches. The Share switch in Options turns it off.")));
        if (_leftOut.Length > 0) info.Add((L.F("Left out: {0}", _leftOut), L.T("Quest and payment rewards: bound Kinah, a payment line, or one line far above what the enemies here pay. Never farming income.")));
        if (_idleNote.Length > 0) info.Add((char.ToUpperInvariant(_idleNote[0]) + _idleNote[1..], L.T("A minute or more of the game being read with nothing picked up.")));
        return info;
    }

    /// <summary>
    /// Clear everything (New session on the card in 0.9.9 to 0.9.12): starts the farming tables over (Steven, 2026-10-22);
    /// not during a farming run, which started from zero already. The tables look
    /// empty at once, with Undo; what was counted is shared and the count starts over when the bar goes (Ui/MainWindow.Undo.cs).
    /// </summary>
    async Task ClearFarmTables()
    {
        if (RunOn) { Say(L.T("A run is on, and it started from zero. Stop it first to clear everything."), "Warn"); return; }
        await Offer("tables", L.T("Everything cleared"),
            apply: () => { var blank = new KinahLedger.Session.SessionEngine(_items); blank.SetZone(_watcher.Chat?.ZoneView?.Zone); _clearedView = blank; Redraw(); },
            undo: () => { _clearedView = null; Redraw(); Say(L.T("The farming tables are back as they were."), "Good"); },
            commit: async () =>
            {
                await _watcher.ClearFarmingAsync();
                _clearedView = null; _lastRun = null;
                Redraw();
                Say(L.T("Everything cleared. Counting starts again from here."), "Faint");
            });
    }
}
