using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using KinahLedger.Data;
using KinahLedger.Session;

namespace KinahLedger.Ui;

/// <summary>
/// The Expeditions and events page's runs one at a time (Steven, 2026-11-05, of the page with a tall card per place: "the
/// bottom part is just insanely long and too lengthy ... I would rather this be scrollable and tell you what you got on
/// every single run and then have a menu at the top where you could drop down and choose to sort by it"). ALL RUNS is one
/// box that scrolls: every run on its line (started, place, took, what it made) with its Kinah and rewards as slots under
/// it, each with its card on hover; a dropdown picks the order (newest, best, by place, longest). The places' cards sit
/// a second box under the place's (Steven, 2026-11-06, of the page with it on top: "look at how much info is on the
/// screen"), from the ⋯ menu's Show every run; a click on a run picks its place and opens that run's sheet in the place's
/// box, where the details, a fix and Remove are. The place's box, its band a dropdown of every place with runs, is
/// BuildPlaceBox (Ui/MainWindow.RunsCard.cs).
/// </summary>
public partial class MainWindow
{
    /// <summary>The place picked from the card's band (or by a click on a run); "" until one is, and the place being run (or the last run) while it has no runs on the page.</summary>
    string _placePick = "";
    /// <summary>The places with runs on the page as last drawn, in the band's order (the options self-test).</summary>
    List<string> _placesShown = new();
    /// <summary>Past the first RunListFirst runs only on request: All time can hold hundreds, each with its slots.</summary>
    bool _runListAll;
    /// <summary>The runs box under the place's box, from the ⋯ menu's Show every run; off as the page opens.</summary>
    bool _runListShown;

    /// <summary>The page's ⋯ menu: Share the last run, every run on its line, Save as CSV, Clear.</summary>
    List<MenuEntry> EventsMoreEntries()
    {
        var list = new List<MenuEntry>();
        if (EventsShareBtn.IsEnabled) list.Add(new MenuEntry("Share the last run", () => ToggleShare("Events"), "A picture of the last run, to paste in Discord or anywhere. Nothing is uploaded."));
        list.Add(new MenuEntry(_runListShown ? "Hide every run" : "Show every run", () => { _runListShown = !_runListShown; _drawnSig.Remove("runs"); ShowInstances(); }, "Each run on its line with what it gave, in a box under the place's. A click on a run opens it in the place's box."));
        list.Add(new MenuEntry(SaveRunsText + "…", () => SaveRunsCsv(_shownRuns), "The runs shown, one line each, for a spreadsheet. A dialog asks where; nothing is uploaded."));
        list.Add(new MenuEntry("", Rule: true));
        list.Add(new MenuEntry("Clear the runs shown", () => _ = ClearRuns(), "Clears the runs on this page. Undo is on the status line for a while."));
        return list;
    }

    const double RunListHeight = 440;
    const int RunListFirst = 40;

    string RunsSortMode => RunsOrder.Of(_prefs.RunsSort);
    /// <summary>The runs the ALL RUNS box was last drawn with (Save as CSV, and the options self-test).</summary>
    List<InstanceRun> _shownRuns = new();
    internal List<InstanceRun> ShownRunsForTest() => _shownRuns;

    /// <summary>The place whose card is under the box: the one picked while it has runs on the page, else the place being run, else the last one run (the first of <paramref name="places"/>).</summary>
    string PickedPlace(List<string> places, string? live) =>
        places.FirstOrDefault(p => _placePick.Length > 0 && string.Equals(p, _placePick, StringComparison.OrdinalIgnoreCase))
        ?? places.FirstOrDefault(p => string.Equals(p, live, StringComparison.OrdinalIgnoreCase)) ?? places[0];
    void OpenPlace(string zone) => _placePick = zone;

    /// <summary>The band's dropdown: every place with runs on the page, each with its kind, its runs, what they made and when it was last run; the picked one ticked.</summary>
    List<MenuEntry> PlaceEntries(List<(string Zone, List<InstanceRun> Runs)> groups, string picked, string? live, Action redraw)
    {
        var list = new List<MenuEntry>();
        foreach (var (zone, runs) in groups)
        {
            var counted = runs.Where(r => !r.OutOfAverages).ToList();
            long value = counted.Sum(RunValue);
            var newest = runs.OrderByDescending(r => r.LastLoot ?? r.Start).First();
            var last = (newest.LastLoot ?? newest.Start).ToLocalTime();
            var parts = new List<string>();
            if (newest.Kind.Length > 0) parts.Add(newest.Kind);
            parts.Add($"{runs.Count} run{(runs.Count == 1 ? "" : "s")}");
            if (value > 0) parts.Add($"{Fk(value)} made");
            parts.Add(string.Equals(zone, live, StringComparison.OrdinalIgnoreCase) ? "open now" : last.Date == DateTime.Today ? $"last {last:HH:mm}" : $"last {last.ToString("MMM d", CultureInfo.InvariantCulture)}");
            list.Add(new MenuEntry(zone, () => { OpenPlace(zone); _drawnSig.Remove("runs"); redraw(); }, string.Join(" · ", parts), Key: string.Equals(zone, picked, StringComparison.OrdinalIgnoreCase) ? "✓" : ""));
        }
        return list;
    }

    /// <summary>The box: its heading (ALL RUNS, how many, the order's dropdown) over the runs, which scroll past RunListHeight.</summary>
    Border BuildRunList(List<InstanceRun> runs, Action redraw)
    {
        var body = new StackPanel { Width = PlaceCardWidth - 2 };
        var card = new Border { Child = body, Tag = "list" };
        card.SetResourceReference(StyleProperty, "QCard");
        card.Padding = new Thickness(0);
        // the head (the Quest look, 2026-11-07): the icon and the title, the count, the order's dropdown and Save as CSV on the right
        var head = QHead("Timer", "Every run", $"{runs.Count} run{(runs.Count == 1 ? "" : "s")} · what each one gave");
        head.Margin = new Thickness(14, 12, 14, 6);
        var pick = PickButton(RunsOrder.Label(RunsSortMode), () => RunsSortEntries(redraw), "The order of the runs: newest first, the best first, by place, or the longest first.");
        // Save as CSV (0.10.11, Ui/MainWindow.Export.cs): the runs shown, in this order, for a spreadsheet
        var save = PillButton(SaveRunsText, () => SaveRunsCsv(runs), "One file for a spreadsheet: these runs in this order, each with when it started, the place, how long it took, its Kinah, kills, worth and items. Nothing is uploaded.");
        save.Margin = new Thickness(0, 0, 8, 0);
        var tools = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        tools.Children.Add(save); tools.Children.Add(pick);
        tools.Margin = new Thickness(10, 0, 0, 0); Grid.SetColumn(tools, 3); head.Children.Add(tools);
        _shownRuns = runs;
        body.Children.Add(head);
        var host = new StackPanel();
        var order = RunsOrder.Sorted(runs, RunsSortMode, RunValue);
        foreach (var run in _runListAll ? order : order.Take(RunListFirst)) host.Children.Add(RunGroup(run, redraw));
        if (order.Count > RunListFirst)
        {
            var m = QT(_runListAll ? "Show fewer" : $"Show all {order.Count} runs", 11.5, FontWeights.SemiBold, "Gold");
            m.Cursor = Cursors.Hand; m.Background = Brushes.Transparent; m.Margin = new Thickness(0, 6, 0, 8);
            m.MouseLeftButtonUp += (_, _) => { _runListAll = !_runListAll; redraw(); };
            host.Children.Add(m);
        }
        var scroll = new ScrollViewer { Content = host, MaxHeight = RunListHeight, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Focusable = false, Padding = new Thickness(14, 0, 10, 10) };
        body.Children.Add(scroll);
        return card;
    }

    /// <summary>The dropdown's choices for the runs' order, the one on with a tick; a pick keeps it (ui.json) and draws the page again.</summary>
    List<MenuEntry> RunsSortEntries(Action redraw)
        => RunsOrder.Modes.Select(mode => new MenuEntry(RunsOrder.Label(mode), () => { _prefs.RunsSort = mode; SavePrefs(); _drawnSig.Remove("runs"); redraw(); }, RunsOrder.Tip(mode), Key: mode == RunsSortMode ? "✓" : "")).ToList();

    /// <summary>
    /// One run: when it started, its place, how long it took, "left out of the figures" when it is, and what it made on
    /// one line; its Kinah and every reward as slots under it. A click on the line opens its place's card with the run's
    /// sheet (the details, a fix if it needs one, Remove).
    /// </summary>
    FrameworkElement RunGroup(InstanceRun run, Action redraw)
    {
        var one = TodayLoot.Runs(new[] { run }, _items, IsCurrency, all: true);
        var sp = new StackPanel { Margin = new Thickness(0, 2, 0, 2), Tag = run };
        bool out_ = run.OutOfAverages;
        var local = run.Start.ToLocalTime();
        var whenQ = local.Date == DateTime.Today ? local.ToString("HH:mm", CultureInfo.InvariantCulture) : local.ToString("MMM d HH:mm", CultureInfo.InvariantCulture);
        long valueQ = RunValue(run);
        var subQ = whenQ + (run.Seconds > 0 ? " · took " + Clock(TimeSpan.FromSeconds(run.Seconds)) : "") + " · " + RunSub(run, out_ ? FlagReason(run, 0) : "") + (out_ ? " · left out of the figures" : "");
        var iconQ = out_ ? QIconBox("Q", "Coral", 28, round: true) : QIconBox(PlaceIcon(run.Zone, run.Kind), "Sky", 28, round: true);
        var line = QRow(iconQ, run.Label, subQ, out_ || valueQ <= 0 ? "–" : Fk(valueQ), out_ || valueQ <= 0 ? "Faint" : "Gold", rule: false);
        line.ToolTip = out_ ? "This run's Kinah or rewards weren't read in full, so it isn't in the figures. Click for the fix, in its place's card." : "Click for this run in its place's card: the details, or Remove if it's wrong.";
        ToolTipService.SetInitialShowDelay(line, 500);
        line.MouseLeftButtonUp += (_, e) => { e.Handled = true; OpenPlace(run.Zone); _fixOpen = RunKey(run); _drawnSig.Remove("runs"); redraw(); };
        sp.Children.Add(line);
        var slotsQ = new List<FrameworkElement>();
        if (one.Kinah > 0) slotsQ.Add(RunKinahSlot(one.Kinah, one.KinahWhere, "Picked up this run"));
        foreach (var s in SortSlots(one.Items)) slotsQ.Add(RunItemSlot(s, "this run"));
        if (slotsQ.Count == 0 && one.Gear == 0) { var t = QT(run.RewardsNotRead ? "Its reward screen wasn't read." : "Nothing read from its reward screen.", 11.5, FontWeights.Medium, "Faint"); t.Margin = new Thickness(38, 0, 0, 6); sp.Children.Add(t); }
        else if (slotsQ.Count > 0) { var wrap = new WrapPanel { Margin = new Thickness(38, 2, 0, 2) }; foreach (var s in slotsQ) wrap.Children.Add(s); sp.Children.Add(wrap); }
        if (GearRow(one, "this run") is FrameworkElement gn) { gn.Margin = new Thickness(38, 2, 0, 6); sp.Children.Add(gn); }
        return sp;
    }

    FrameworkElement RunGroupOld(InstanceRun run, Action redraw)
    {
        var one = TodayLoot.Runs(new[] { run }, _items, IsCurrency, all: true);
        var sp = new StackPanel { Margin = new Thickness(0, 4, 0, 4), Tag = run };
        var head = new Grid();
        for (int i = 0; i < 4; i++) head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        head.ColumnDefinitions.Add(new ColumnDefinition());
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        bool out_ = run.OutOfAverages;
        var local = run.Start.ToLocalTime();
        var when = PT(local.Date == DateTime.Today ? local.ToString("HH:mm", CultureInfo.InvariantCulture) : local.ToString("MMM d HH:mm", CultureInfo.InvariantCulture), "Faint", 12, null, "MonoFont");
        when.VerticalAlignment = VerticalAlignment.Center; head.Children.Add(when);
        var name = PT(run.Label, out_ ? "Faint" : "Ink", 12.5, FontWeights.Normal); name.Margin = new Thickness(10, 0, 0, 0); name.VerticalAlignment = VerticalAlignment.Center; name.MaxWidth = 280; Grid.SetColumn(name, 1); head.Children.Add(name);
        if (run.Seconds > 0) { var took = PT(Clock(TimeSpan.FromSeconds(run.Seconds)), "Faint", 12, null, "MonoFont"); took.Margin = new Thickness(10, 0, 0, 0); took.VerticalAlignment = VerticalAlignment.Center; Grid.SetColumn(took, 2); head.Children.Add(took); }
        if (out_) { var o = PT("left out of the figures", "Faint", 11.5); o.Margin = new Thickness(10, 0, 0, 0); o.VerticalAlignment = VerticalAlignment.Center; Grid.SetColumn(o, 3); head.Children.Add(o); }
        long value = RunValue(run);
        if (value > 0)
        {
            var v = PT(Fk(value), out_ ? "Faint" : "Gold", 12.5, null, "MonoFont"); v.VerticalAlignment = VerticalAlignment.Center; v.Margin = new Thickness(12, 0, 0, 0);
            v.ToolTip = "This run's Kinah plus its loot at the lowest Market price, " + PriceBook.Cut + ". Gear is bound and not in it.";
            Grid.SetColumn(v, 5); head.Children.Add(v);
        }
        var line = new Border { Child = head, Padding = new Thickness(8, 5, 8, 5), Margin = new Thickness(-8, 0, -8, 4), CornerRadius = new CornerRadius(6), Background = Brushes.Transparent, Cursor = Cursors.Hand,
            ToolTip = out_ ? "This run's Kinah or rewards weren't read in full, so it isn't in the figures. Click for the fix, in its place's card." : "Click for this run in its place's card: the details, or Remove if it's wrong." };
        ToolTipService.SetInitialShowDelay(line, 500);
        line.MouseEnter += (_, _) => { line.SetResourceReference(Border.BackgroundProperty, "HoverTint"); name.SetResourceReference(TextBlock.ForegroundProperty, "Gold"); };
        line.MouseLeave += (_, _) => { line.Background = Brushes.Transparent; name.SetResourceReference(TextBlock.ForegroundProperty, out_ ? "Faint" : "Ink"); };
        line.MouseLeftButtonUp += (_, e) => { e.Handled = true; OpenPlace(run.Zone); _fixOpen = RunKey(run); _drawnSig.Remove("runs"); redraw(); };
        sp.Children.Add(line);
        var slots = new List<FrameworkElement>();
        if (one.Kinah > 0) slots.Add(KinahSlot(one.Kinah, one.KinahWhere, "", "Picked up this run"));
        foreach (var s in SortSlots(one.Items)) slots.Add(ItemSlot(s, ("", ""), false, runs: true, when: "this run"));
        if (slots.Count == 0) { var t = PT(run.RewardsNotRead ? "Its reward screen wasn't read." : "Nothing read from its reward screen.", "Faint", 12); t.Margin = new Thickness(0, 0, 0, 6); sp.Children.Add(t); }
        else sp.Children.Add(SlotWrap(slots));
        return sp;
    }

    /// <summary>The slots, wrapped, each with the strip's gap.</summary>
    static WrapPanel SlotWrap(List<FrameworkElement> slots)
    {
        var wrap = new WrapPanel();
        foreach (var s in slots) { s.Margin = new Thickness(0, 0, TodayGap, 8); wrap.Children.Add(s); }
        return wrap;
    }

    /// <summary>
    /// A small dropdown in the chips' look: the choice and a chevron; a click opens the menu of choices under it (Steven,
    /// 2026-11-05: "a menu at the top where you could drop down and choose to sort by it").
    /// </summary>
    Border PickButton(string text, Func<List<MenuEntry>> entries, string tip)
    {
        var sp = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        sp.Children.Add(QT(text, 11.5, FontWeights.SemiBold, "Ink"));
        var chev = QIcon("Chev", 11, "Faint", 2); chev.Margin = new Thickness(6, 1, 0, 0); chev.VerticalAlignment = VerticalAlignment.Center; chev.RenderTransformOrigin = new Point(0.5, 0.5); chev.RenderTransform = new RotateTransform(90); sp.Children.Add(chev);
        var b = new Border { Child = sp, Height = 26, Padding = new Thickness(10, 0, 9, 0), CornerRadius = new CornerRadius(8), BorderThickness = new Thickness(1), Cursor = Cursors.Hand, ToolTip = tip, VerticalAlignment = VerticalAlignment.Center };
        b.SetResourceReference(Border.BackgroundProperty, "Card2"); b.SetResourceReference(Border.BorderBrushProperty, "CardLineHover");
        ToolTipService.SetInitialShowDelay(b, 400);
        b.MouseLeftButtonUp += (_, e) => { e.Handled = true; ShowMenu(b, entries()); };
        return b;
    }

    /* ---------------- for options-test ---------------- */

    /// <summary>The place's box and the runs box as drawn: the runs box's runs (0 with it off), their values in order, the order dropdown's words, the place open, the places the band's dropdown offers, and the place's loot slots.</summary>
    internal (int Runs, List<long> Values, string Pick, List<string> Open, List<string> Places, int Slots) RunListForTest()
    {
        var list = InstHost.Children.OfType<Border>().FirstOrDefault(b => b.Tag is "list");
        var groups = list == null ? new List<StackPanel>() : ((StackPanel)list.Child).Children.OfType<ScrollViewer>().SelectMany(s => ((StackPanel)s.Content).Children.OfType<StackPanel>()).Where(p => p.Tag is InstanceRun).ToList();
        var pick = list == null ? "" : ((StackPanel)list.Child).Children.OfType<Grid>().SelectMany(g => g.Children.OfType<StackPanel>()).SelectMany(p => p.Children.OfType<Border>()).Where(b => b.Child is StackPanel).Select(b => ((TextBlock)((StackPanel)b.Child).Children[0]).Text).FirstOrDefault() ?? "";
        var open = InstHost.Children.OfType<Border>().Where(b => b.Tag is "open").Select(b => _placeOfCard.GetValueOrDefault(b, "")).ToList();
        var box = InstHost.Children.OfType<Border>().FirstOrDefault(b => b.Tag is "open");
        int slots = box?.Child is StackPanel sp ? sp.Children.OfType<Border>().Select(b => b.Child).OfType<StackPanel>().SelectMany(g => g.Children.OfType<WrapPanel>()).Sum(w => w.Children.Count) : 0;
        return (groups.Count, groups.Select(p => RunValue((InstanceRun)p.Tag!)).ToList(), pick, open, new List<string>(_placesShown), slots);
    }

    /// <summary>Which place each card on the page is, for the test above.</summary>
    readonly Dictionary<Border, string> _placeOfCard = new();

    internal string? FixOpenForTest => _fixOpen;
    internal void SetRunsSortForTest(string mode) { _prefs.RunsSort = mode; _drawnSig.Remove("runs"); ShowInstances(); }
    internal void PickPlaceForTest(string zone) { OpenPlace(zone); _drawnSig.Remove("runs"); ShowInstances(); }
    internal void ShowRunListForTest(bool on) { _runListShown = on; _drawnSig.Remove("runs"); ShowInstances(); }
    /// <summary>A click on the run list's k-th line.</summary>
    internal void ClickRunForTest(int k)
    {
        var list = InstHost.Children.OfType<Border>().First(b => b.Tag is "list");
        var run = (InstanceRun)((StackPanel)list.Child).Children.OfType<ScrollViewer>().SelectMany(s => ((StackPanel)s.Content).Children.OfType<StackPanel>()).Where(p => p.Tag is InstanceRun).ElementAt(k).Tag!;
        OpenPlace(run.Zone); _fixOpen = RunKey(run); _drawnSig.Remove("runs"); ShowInstances();
    }
}
