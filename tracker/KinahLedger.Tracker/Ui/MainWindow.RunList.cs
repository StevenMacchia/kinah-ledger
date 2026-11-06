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
/// under the box folded to one line each, the place being run (or the last one run) open; a click on a run opens its
/// place's card with that run's sheet, where the details, a fix and Remove are.
/// </summary>
public partial class MainWindow
{
    /// <summary>The places whose card is the other way from its default: the first place (being run, or the last run) opens on its own, the others fold.</summary>
    readonly HashSet<string> _placesToggled = new(StringComparer.OrdinalIgnoreCase);
    string _firstPlace = "";
    /// <summary>Past the first RunListFirst runs only on request: Earlier can hold hundreds, each with its slots.</summary>
    bool _runListAll;

    const double RunListHeight = 440;
    const int RunListFirst = 40;

    string RunsSortMode => RunsOrder.Of(_prefs.RunsSort);
    /// <summary>The runs the ALL RUNS box was last drawn with (Save as CSV, and the options self-test).</summary>
    List<InstanceRun> _shownRuns = new();
    internal List<InstanceRun> ShownRunsForTest() => _shownRuns;

    bool PlaceOpen(string zone, bool first) => _placesToggled.Contains(zone) ? !first : first;
    void TogglePlace(string zone) { if (!_placesToggled.Remove(zone)) _placesToggled.Add(zone); }
    void OpenPlace(string zone) { if (!PlaceOpen(zone, string.Equals(zone, _firstPlace, StringComparison.OrdinalIgnoreCase))) TogglePlace(zone); }

    /// <summary>The box: its heading (ALL RUNS, how many, the order's dropdown) over the runs, which scroll past RunListHeight.</summary>
    Border BuildRunList(List<InstanceRun> runs, Action redraw)
    {
        var body = new StackPanel { Width = PlaceCardWidth - 2 };
        var card = new Border { Child = body, Tag = "list", CornerRadius = new CornerRadius(10), BorderThickness = new Thickness(1), Effect = new DropShadowEffect { BlurRadius = 18, ShadowDepth = 2, Opacity = 0.14, Direction = 270 } };
        card.SetResourceReference(Border.BackgroundProperty, "Card");
        card.SetResourceReference(Border.BorderBrushProperty, "LineStrong");
        var head = new Grid { Margin = new Thickness(20, 14, 20, 8) };
        head.ColumnDefinitions.Add(new ColumnDefinition()); head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var t = new TextBlock { FontSize = 11, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
        t.Inlines.Add(PR("ALL RUNS", "SecGold", FontWeights.Bold));
        var n = PR($"   {runs.Count} run{(runs.Count == 1 ? "" : "s")} · what each one gave", "Muted"); n.FontSize = 12; t.Inlines.Add(n);
        head.Children.Add(t);
        var pick = PickButton(RunsOrder.Label(RunsSortMode), () => RunsSortEntries(redraw), "The order of the runs: newest first, the best first, by place, or the longest first.");
        // Save as CSV (0.10.11, Ui/MainWindow.Export.cs): the runs shown, in this order, for a spreadsheet
        var save = PillButton(SaveRunsText, () => SaveRunsCsv(runs), "One file for a spreadsheet: these runs in this order, each with when it started, the place, how long it took, its Kinah, kills, worth and items. Nothing is uploaded.");
        save.Margin = new Thickness(0, 0, 8, 0);
        var tools = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        tools.Children.Add(save); tools.Children.Add(pick);
        Grid.SetColumn(tools, 1); head.Children.Add(tools);
        _shownRuns = runs;
        body.Children.Add(head);
        var host = new StackPanel();
        var order = RunsOrder.Sorted(runs, RunsSortMode, RunValue);
        foreach (var run in _runListAll ? order : order.Take(RunListFirst)) host.Children.Add(RunGroup(run, redraw));
        if (order.Count > RunListFirst)
        {
            var m = PT(_runListAll ? "Show fewer" : $"Show all {order.Count} runs", "Gold", 12.5, FontWeights.SemiBold);
            m.Cursor = Cursors.Hand; m.Background = Brushes.Transparent; m.Margin = new Thickness(0, 6, 0, 8);
            m.MouseLeftButtonUp += (_, _) => { _runListAll = !_runListAll; redraw(); };
            host.Children.Add(m);
        }
        var scroll = new ScrollViewer { Content = host, MaxHeight = RunListHeight, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Focusable = false, Padding = new Thickness(20, 0, 12, 10) };
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
        var sp = new StackPanel { Margin = new Thickness(0, 4, 0, 4), Tag = run };
        var head = new Grid();
        for (int i = 0; i < 4; i++) head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        head.ColumnDefinitions.Add(new ColumnDefinition());
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        bool out_ = run.OutOfAverages;
        var local = run.Start.ToLocalTime();
        var when = PT(local.Date == DateTime.Today ? local.ToString("HH:mm", CultureInfo.InvariantCulture) : local.ToString("MMM d HH:mm", CultureInfo.InvariantCulture), "Faint", 12, null, "MonoFont");
        when.VerticalAlignment = VerticalAlignment.Center; head.Children.Add(when);
        var name = PT(run.Label, out_ ? "Faint" : "Ink", 12.5, FontWeights.SemiBold); name.Margin = new Thickness(10, 0, 0, 0); name.VerticalAlignment = VerticalAlignment.Center; name.MaxWidth = 280; Grid.SetColumn(name, 1); head.Children.Add(name);
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

    /// <summary>A place's card folded to one line: its tile, name and kind, its runs and what they made, when it was last run; a click opens the card.</summary>
    Border FoldedPlace(string zone, List<InstanceRun> runs, bool live, Action open)
    {
        var counted = runs.Where(r => !r.OutOfAverages).ToList();
        long value = counted.Sum(RunValue), best = counted.Count > 0 ? counted.Max(RunValue) : 0;
        var newest = runs.OrderByDescending(r => r.LastLoot ?? r.Start).First();
        var g = new Grid();
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); g.ColumnDefinitions.Add(new ColumnDefinition());
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        g.Children.Add(PlaceTile(newest.Kind, 36));
        var titles = new StackPanel { Margin = new Thickness(12, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        var title = new TextBlock { FontSize = 15.5, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis };
        title.SetResourceReference(TextBlock.FontFamilyProperty, "DisplayFont");
        title.Inlines.Add(PR(zone, "Gold"));
        if (newest.Kind.Length > 0) title.Inlines.Add(PR(" · " + newest.Kind, "Faint", FontWeights.Normal));
        titles.Children.Add(title);
        var meta = $"{runs.Count} run{(runs.Count == 1 ? "" : "s")}" + (value > 0 ? $" · {Fk(value)} made" : "") + (best > 0 && counted.Count > 1 ? $" · best {Fk(best)}" : "");
        var ml = PT(meta, "Muted", 12); ml.Margin = new Thickness(0, 2, 0, 0); titles.Children.Add(ml);
        Grid.SetColumn(titles, 1); g.Children.Add(titles);
        var last = (newest.LastLoot ?? newest.Start).ToLocalTime();
        var pill = live ? PPill("open now", true) : PPill(last.Date == DateTime.Today ? $"last {last:HH:mm}" : $"last {last.ToString("MMM d", CultureInfo.InvariantCulture)}", false);
        pill.VerticalAlignment = VerticalAlignment.Center; Grid.SetColumn(pill, 2); g.Children.Add(pill);
        var chev = PT("›", "Faint", 16); chev.Margin = new Thickness(12, 0, 0, 0); chev.VerticalAlignment = VerticalAlignment.Center; Grid.SetColumn(chev, 3); g.Children.Add(chev);
        var card = new Border { Child = g, Tag = "folded", CornerRadius = new CornerRadius(10), BorderThickness = new Thickness(1), Padding = new Thickness(16, 10, 18, 10), Cursor = Cursors.Hand, Effect = new DropShadowEffect { BlurRadius = 18, ShadowDepth = 2, Opacity = 0.14, Direction = 270 },
            ToolTip = "Click to open this place: its figures, its runs and their loot." };
        card.SetResourceReference(Border.BackgroundProperty, "Card"); card.SetResourceReference(Border.BorderBrushProperty, "LineStrong");
        ToolTipService.SetInitialShowDelay(card, 500);
        card.MouseEnter += (_, _) => chev.SetResourceReference(TextBlock.ForegroundProperty, "Gold");
        card.MouseLeave += (_, _) => chev.SetResourceReference(TextBlock.ForegroundProperty, "Faint");
        card.MouseLeftButtonUp += (_, _) => open();
        return card;
    }

    /// <summary>
    /// A small dropdown in the chips' look: the choice and a chevron; a click opens the menu of choices under it (Steven,
    /// 2026-11-05: "a menu at the top where you could drop down and choose to sort by it").
    /// </summary>
    Border PickButton(string text, Func<List<MenuEntry>> entries, string tip)
    {
        var sp = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        sp.Children.Add(PT(text, "Ink", 12));
        var chev = PT("⌄", "Muted", 13); chev.Margin = new Thickness(6, -4, 0, 0); sp.Children.Add(chev);
        var b = new Border { Child = sp, Height = 26, Padding = new Thickness(11, 0, 9, 0), CornerRadius = new CornerRadius(13), BorderThickness = new Thickness(1), Cursor = Cursors.Hand, ToolTip = tip, VerticalAlignment = VerticalAlignment.Center };
        b.SetResourceReference(Border.BackgroundProperty, "ChipOff"); b.SetResourceReference(Border.BorderBrushProperty, "ChipOffLine");
        ToolTipService.SetInitialShowDelay(b, 400);
        b.MouseLeftButtonUp += (_, e) => { e.Handled = true; ShowMenu(b, entries()); };
        return b;
    }

    /* ---------------- for options-test ---------------- */

    /// <summary>The run list and the places' cards as drawn: the runs shown, their values in order, the dropdown's words, the open and folded places.</summary>
    internal (int Runs, List<long> Values, string Pick, List<string> Open, List<string> Folded) RunListForTest()
    {
        var list = InstHost.Children.OfType<Border>().FirstOrDefault(b => b.Tag is "list");
        var groups = list == null ? new List<StackPanel>() : ((StackPanel)list.Child).Children.OfType<ScrollViewer>().SelectMany(s => ((StackPanel)s.Content).Children.OfType<StackPanel>()).Where(p => p.Tag is InstanceRun).ToList();
        var pick = list == null ? "" : ((StackPanel)list.Child).Children.OfType<Grid>().SelectMany(g => g.Children.OfType<StackPanel>()).SelectMany(p => p.Children.OfType<Border>()).Where(b => b.Child is StackPanel).Select(b => ((TextBlock)((StackPanel)b.Child).Children[0]).Text).FirstOrDefault() ?? "";
        var open = InstHost.Children.OfType<Border>().Where(b => b.Tag is "open").Select(b => _placeOfCard.GetValueOrDefault(b, "")).ToList();
        var folded = InstHost.Children.OfType<Border>().Where(b => b.Tag is "folded").Select(b => _placeOfCard.GetValueOrDefault(b, "")).ToList();
        return (groups.Count, groups.Select(p => RunValue((InstanceRun)p.Tag!)).ToList(), pick, open, folded);
    }

    /// <summary>Which place each card on the page is, for the test above.</summary>
    readonly Dictionary<Border, string> _placeOfCard = new();

    internal string? FixOpenForTest => _fixOpen;
    internal void SetRunsSortForTest(string mode) { _prefs.RunsSort = mode; _drawnSig.Remove("runs"); ShowInstances(); }
    internal void TogglePlaceForTest(string zone) { TogglePlace(zone); _drawnSig.Remove("runs"); ShowInstances(); }
    /// <summary>A click on the run list's k-th line.</summary>
    internal void ClickRunForTest(int k)
    {
        var list = InstHost.Children.OfType<Border>().First(b => b.Tag is "list");
        var run = (InstanceRun)((StackPanel)list.Child).Children.OfType<ScrollViewer>().SelectMany(s => ((StackPanel)s.Content).Children.OfType<StackPanel>()).Where(p => p.Tag is InstanceRun).ElementAt(k).Tag!;
        OpenPlace(run.Zone); _fixOpen = RunKey(run); _drawnSig.Remove("runs"); ShowInstances();
    }
}
