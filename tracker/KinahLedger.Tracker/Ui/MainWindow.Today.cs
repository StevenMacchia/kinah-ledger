using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using KinahLedger.Data;
using KinahLedger.Session;
using Path = System.Windows.Shapes.Path;

namespace KinahLedger.Ui;

/// <summary>
/// The Today strip (0.8.50, option C of kinah-bench/mock-today, Steven 2026-10-27: "I really like the aesthetic of option
/// C"). One gold line over Farming and Events says the day so far and what it made; opened, the day's loot as inventory
/// slots. Each page's strip is its own day (0.8.51, Steven: "Farming should be todays Farming. Events should be Todays
/// events and expeditions"): on Farming, "2:41 farming · 412 kills", the drops and the gathering; on Events, "3 expeditions
/// · 2 events" and their rewards. Neither shows the other's loot or figures. Zone chips pin one zone's farming, and the pick
/// stays put when the player changes zone. Each slot opens the game-tooltip card on hover: where it came from, the odds,
/// what it is worth. Since midnight on this PC, carried across restarts (GameWatcher.DayView, DayStore). Drawn by hand and
/// only when what it shows has changed (Changed("today")).
/// </summary>
public partial class MainWindow
{
    /// <summary>The pinned zone's farming, or null for every zone (the default). Kept while the tracker runs.</summary>
    string? _todayZone;
    /// <summary>Groups showing every slot, not two rows ("farm", "gather", "runs").</summary>
    readonly HashSet<string> _todayAll = new();
    /// <summary>A sample day for layout renders (never the player's own).</summary>
    SessionEngine? _demoDay;

    const int TodayRows = 2;
    const double TodaySlotSize = 52, TodayGap = 9;

    SessionEngine? TodayEngine => _demo ? _demoDay : _watcher.DayView;

    /// <summary>The runs since midnight on this PC, the ones a pending Clear or Remove hides left out.</summary>
    List<InstanceRun> TodayRunList()
    {
        var log = _watcher.Chat?.Instances ?? _demoInst;
        if (log == null) return new();
        var midnight = DateTime.Today;
        return log.Snapshot().Where(r => !_hiddenRuns.Contains(RunKey(r.Start, r.Zone)) && TodayLoot.IsToday(r, midnight)).ToList();
    }

    void InitToday()
    {
        TodayHead.MouseLeftButtonUp += (_, _) =>
        {
            _prefs.TodayOpen = !_prefs.TodayOpen;
            if (!_demo) _prefs.Save();
            _drawnSig.Remove("today");
            FillToday();
        };
    }

    /// <summary>
    /// Draws the Events page's strip: always its line (cheap, every tick), the slots only when the day's runs, the pin or the
    /// width changed. On Farming the day is LOOT's Today side since 2026-10-28 (Ui/MainWindow.Loot.cs), so no strip there.
    /// </summary>
    void FillToday()
    {
        if (_part == "Farming" && !_welcome) { TodayStrip.Visibility = Visibility.Collapsed; _drawnSig.Remove("today"); FillLoot(); return; }
        var runs = _part == "Events" && !_welcome ? TodayRunList() : null;
        TodayRuns? r = runs != null ? TodayLoot.Runs(runs, _items, IsCurrency) : null;
        // the runs' Week side (0.9, Ui/MainWindow.Week.cs): the strip shows too when only the last days had runs
        var week = r != null ? RunsWeekDays() : null;
        bool any = r?.Runs > 0 || week?.Any(d => d.Runs.Runs > 0) == true;
        TodayStrip.Visibility = any ? Visibility.Visible : Visibility.Collapsed;
        if (r == null || week == null || !any) return;
        TodayDayBtn.Style = (Style)FindResource(_runsWeek ? "SegOff" : "SegOn");
        TodayWeekBtn.Style = (Style)FindResource(_runsWeek ? "SegOn" : "SegOff");
        TodayTitle.Text = _runsWeek ? "WEEK" : "TODAY";
        // the week's bars never scroll; the day's runs do, in a box of their own height
        TodayScroll.MaxHeight = _runsWeek ? double.PositiveInfinity : 360;
        if (_runsWeek) { TodayTop.Children.Clear(); FillRunsWeek(week); return; }

        // the line: today's runs, the figures in the data face, and what they made
        var note = TodayLoot.RunsLine(r);
        long made = r.Value;
        var noteSig = _part + "|" + string.Join("|", note.Select(n => n.Fig + n.Word)) + "|" + made;
        if (Changed("today-line", noteSig))
        {
            TodayNote.Inlines.Clear();
            if (note.Count == 0) TodayNote.Inlines.Add(PR("No runs yet today", "Faint"));
            for (int i = 0; i < note.Count; i++)
            {
                if (i > 0) TodayNote.Inlines.Add(PR(" · ", "Faint"));
                var f = PR(note[i].Fig, "Ink"); f.SetResourceReference(TextElement.FontFamilyProperty, "MonoFont");
                TodayNote.Inlines.Add(f);
                TodayNote.Inlines.Add(PR(note[i].Word, "Muted"));
            }
            TodaySum.Text = made > 0 ? Fk(made) : "";
            TodaySum.ToolTip = made <= 0 ? null
                : "Made in expeditions and events today: Kinah plus rewards at the lowest Market price, " + PriceBook.Cut + ". Gear, currencies, items with no price yet and runs left out of the averages add nothing.";
            TodayHead.ToolTip = "Today's expeditions and events since midnight on this PC. Click to see the rewards.";
        }
        bool open = _prefs.TodayOpen;
        SetSec(TodayChev, TodayBody, open);
        if (!open) { _drawnSig.Remove("today"); return; }

        // the slots, drawn again only when something in them changed
        string Slots(IEnumerable<TodaySlot> l) => string.Join(",", l.Select(s => $"{s.Id}:{s.Count}:{s.Bound}:{s.Worth}"));
        string RunSig(InstanceRun x) => $"{x.Start.Ticks}:{x.Items.Count}:{x.BoundItems.Count}:{x.Kinah}:{x.OutOfAverages}";
        var sig = $"{_part}|{HeadStack.Width}|{_prefs.RunsEach}|{LootSortMode}|{Skins.Current}|{r.Runs}:{r.Places}:{r.Kinah}:{Slots(r.Items)}|{string.Join(",", runs!.Select(RunSig))}";
        if (!Changed("today", sig)) return;
        TodayTop.Children.Clear(); TodayHost.Children.Clear();
        if (r.Runs == 0) TodayHost.Children.Add(TodayEmpty("No runs yet today. Week has the last 7 days."));
        else FillTodayRuns(r, runs!);
    }

    /// <summary>
    /// The Events page's rewards: the heading (the group, its places, the view's dropdown, the day's worth) over the box.
    /// A run at a time, newest first, each with its own slots (Steven, 2026-11-05: "tell you what you got on every single
    /// run"); or every run's rewards together, as one set of slots in the order picked.
    /// </summary>
    void FillTodayRuns(TodayRuns r, List<InstanceRun> runs)
    {
        var pick = PickButton(_prefs.RunsEach ? "Each run" : "Together, by " + LootSortMode, RunsViewEntries, "Each run on its own, or every run's rewards together, by worth, amount or quality.");
        TodayTop.Children.Add(TodayHeading("FROM EXPEDITIONS AND EVENTS", $"{r.Places} place{(r.Places == 1 ? "" : "s")}", r.Value, pick));
        LootOrderForTest.Clear();
        if (_prefs.RunsEach)
        {
            foreach (var run in runs.OrderByDescending(x => x.LastLoot ?? x.Start)) TodayHost.Children.Add(RunGroup(run));
            return;
        }
        var slots = new List<FrameworkElement>();
        if (r.Kinah > 0) slots.Add(KinahSlot(r.Kinah, r.KinahWhere, ""));
        var items = SortSlots(r.Items);
        LootOrderForTest.AddRange(items.Select(s => (s.Name, s.Count, _items.Get(s.Id)?.Grade ?? "", false)));
        foreach (var s in items) slots.Add(ItemSlot(s, ("", ""), true, runs: true));
        if (slots.Count == 0) TodayHost.Children.Add(TodayEmpty("Nothing read from the reward screens yet."));
        else TodayHost.Children.Add(SlotWrap(slots));
    }

    /// <summary>The dropdown's choices for the strip: a run at a time, or all together in one of the three orders; the one on carries a tick.</summary>
    List<MenuEntry> RunsViewEntries()
    {
        var l = new List<MenuEntry>();
        void Add(string text, string sub, bool on, Action set) => l.Add(new MenuEntry(text, () => { set(); SavePrefs(); _drawnSig.Remove("today"); FillToday(); }, sub, Key: on ? "✓" : ""));
        Add("Each run", "What every run gave, newest first.", _prefs.RunsEach, () => _prefs.RunsEach = true);
        l.Add(MenuEntry.Line);
        foreach (var mode in LootOrder.Modes)
            Add("Together, by " + mode, LootOrder.Tip(mode), !_prefs.RunsEach && mode == LootSortMode, () => { _prefs.RunsEach = false; _prefs.LootSort = mode; });
        return l;
    }

    /// <summary>The number of run groups the strip shows and the dropdown's words, for options-test.</summary>
    internal (int Groups, int Slots, string Pick) TodayRunsForTest()
        => (TodayHost.Children.OfType<StackPanel>().Count(p => p.Tag is InstanceRun), TodayHost.Children.OfType<Panel>().Sum(p => p.Children.OfType<WrapPanel>().Sum(w => w.Children.Count)) + TodayHost.Children.OfType<WrapPanel>().Sum(w => w.Children.Count),
            TodayTop.Children.OfType<Grid>().SelectMany(g => g.Children.OfType<Border>()).Select(b => InlineText(((StackPanel)b.Child).Children.OfType<TextBlock>().First())).FirstOrDefault() ?? "");

    /// <summary>For options-test: the view picked, as the dropdown's row.</summary>
    internal void SetRunsEachForTest(bool each) { _prefs.RunsEach = each; _drawnSig.Remove("today"); FillToday(); }

    /// <summary>
    /// One run of the day: when it started, its name, how long it took and what it made on one line, its Kinah and every
    /// reward as slots under it (each with its card on hover). A run left out of the figures says so.
    /// </summary>
    FrameworkElement RunGroup(InstanceRun run)
    {
        var one = TodayLoot.Runs(new[] { run }, _items, IsCurrency, all: true);
        var sp = new StackPanel { Margin = new Thickness(0, 10, 0, 0), Tag = run };
        var head = new Grid { Margin = new Thickness(0, 0, 0, 8) };
        for (int i = 0; i < 4; i++) head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        head.ColumnDefinitions.Add(new ColumnDefinition());
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        bool out_ = run.OutOfAverages;
        var local = run.Start.ToLocalTime();
        var when = PT(local.ToString("HH:mm", System.Globalization.CultureInfo.InvariantCulture), "Faint", 12, null, "MonoFont"); when.VerticalAlignment = VerticalAlignment.Center; head.Children.Add(when);
        var name = PT(run.Label, out_ ? "Faint" : "Ink", 12.5, FontWeights.SemiBold); name.Margin = new Thickness(10, 0, 0, 0); name.VerticalAlignment = VerticalAlignment.Center; name.MaxWidth = 300; Grid.SetColumn(name, 1); head.Children.Add(name);
        if (run.Seconds > 0) { var took = PT(Clock(TimeSpan.FromSeconds(run.Seconds)), "Faint", 12, null, "MonoFont"); took.Margin = new Thickness(10, 0, 0, 0); took.VerticalAlignment = VerticalAlignment.Center; Grid.SetColumn(took, 2); head.Children.Add(took); }
        if (out_) { var n = PT("left out of the figures", "Faint", 11.5); n.Margin = new Thickness(10, 0, 0, 0); n.VerticalAlignment = VerticalAlignment.Center; n.ToolTip = "This run's Kinah or rewards weren't read in full, so it isn't in the day's figures. The Runs list below has the fix."; Grid.SetColumn(n, 3); head.Children.Add(n); }
        if (one.Value > 0)
        {
            var v = PT(Fk(one.Value), out_ ? "Faint" : "Gold", 12.5, null, "MonoFont"); v.VerticalAlignment = VerticalAlignment.Center; v.Margin = new Thickness(12, 0, 0, 0);
            v.ToolTip = "This run's Kinah plus its loot at the lowest Market price, " + PriceBook.Cut + ". Gear, currencies and items with no price yet add nothing.";
            Grid.SetColumn(v, 5); head.Children.Add(v);
        }
        sp.Children.Add(head);
        var slots = new List<FrameworkElement>();
        if (one.Kinah > 0) slots.Add(KinahSlot(one.Kinah, one.KinahWhere, "", "Picked up this run"));
        var items = SortSlots(one.Items);
        LootOrderForTest.AddRange(items.Select(s => (s.Name, s.Count, _items.Get(s.Id)?.Grade ?? "", false)));
        foreach (var s in items) slots.Add(ItemSlot(s, ("", ""), false, runs: true, when: "this run"));
        if (slots.Count == 0) { var t = TodayEmpty(run.RewardsNotRead ? "Its reward screen wasn't read." : "Nothing read from its reward screen."); t.Margin = new Thickness(0, 0, 0, 6); sp.Children.Add(t); }
        else sp.Children.Add(SlotWrap(slots));
        return sp;
    }

    TextBlock TodayEmpty(string text) { var t = PT(text, "Faint", 12.5); t.Margin = new Thickness(0, 12, 0, 4); return t; }

    /// <summary>The slots, wrapped, each with the strip's gap.</summary>
    static WrapPanel SlotWrap(List<FrameworkElement> slots)
    {
        var wrap = new WrapPanel();
        foreach (var s in slots) { s.Margin = new Thickness(0, 0, TodayGap, 8); wrap.Children.Add(s); }
        return wrap;
    }

    /// <summary>The heading over the strip's box: the group's name in the heading gold, a faint word, the view's dropdown and the worth on the right.</summary>
    FrameworkElement TodayHeading(string title, string note, long worth, FrameworkElement? pick)
    {
        var head = new Grid { Margin = new Thickness(0, 12, 0, 4) };
        head.ColumnDefinitions.Add(new ColumnDefinition()); head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var t = new TextBlock { FontSize = 11, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
        t.Inlines.Add(PR(title, "SecGold", FontWeights.Bold));
        if (note.Length > 0) { var n = PR("   " + note, "Muted"); n.FontSize = 12; t.Inlines.Add(n); }
        head.Children.Add(t);
        if (pick != null) { Grid.SetColumn(pick, 1); head.Children.Add(pick); }
        if (worth > 0)
        {
            var v = PT(Fk(worth), "Gold", 13, null, "MonoFont"); v.Margin = new Thickness(12, 0, 0, 0); v.VerticalAlignment = VerticalAlignment.Center;
            v.ToolTip = "Kinah plus loot at the lowest Market price, " + PriceBook.Cut + ". Bound items and items with no price yet add nothing.";
            Grid.SetColumn(v, 2); head.Children.Add(v);
        }
        return head;
    }

    /// <summary>A zone chip: the name and its farming time; the pinned one gold with a pin.</summary>
    Border ZoneChip(string text, string time, bool on, Action pick, string tip = "Every zone of the day together.")
    {
        var sp = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        if (on)
        {
            var pin = new Path { Data = Geometry.Parse("M6,2 H10 L9.4,6 L12,8.4 H4 L6.6,6 Z M8,8.4 V14"), StrokeThickness = 1.4, Width = 12, Height = 12, Stretch = Stretch.Uniform, Margin = new Thickness(0, 0, 6, 0), VerticalAlignment = VerticalAlignment.Center, StrokeLineJoin = PenLineJoin.Round };
            pin.SetResourceReference(Shape.StrokeProperty, "Gold");
            sp.Children.Add(pin);
        }
        sp.Children.Add(PT(text, on ? "Gold" : "Ink", 12.5, on ? FontWeights.SemiBold : null));
        if (time.Length > 0) { var tm = PT(time, on ? "Gold" : "Muted", 12, null, "MonoFont"); tm.Margin = new Thickness(7, 0, 0, 0); sp.Children.Add(tm); }
        var b = new Border { Child = sp, Height = 30, Padding = new Thickness(12, 0, 12, 0), CornerRadius = new CornerRadius(15), BorderThickness = new Thickness(1), Margin = new Thickness(0, 0, 8, 6), Cursor = Cursors.Hand, ToolTip = tip };
        b.SetResourceReference(Border.BackgroundProperty, on ? "SegOnBg" : "ChipOff");
        b.SetResourceReference(Border.BorderBrushProperty, on ? "GoldLine" : "ChipOffLine");
        ToolTipService.SetInitialShowDelay(b, 400);
        b.MouseLeftButtonUp += (_, _) => { pick(); FillToday(); };
        return b;
    }

    /// <summary>"1 in 7 kills" and "23 an hour" for a drop, against the kills and the farming time of what the strip shows.</summary>
    static (string Odds, string Hour) FarmOdds(TodaySlot s, int kills, double activeSecs)
    {
        var odds = kills <= 0 || s.Count <= 0 ? "" : s.Count >= kills ? $"{s.Count / (double)kills:0.0} a kill" : $"1 in {Math.Round(kills / (double)s.Count):N0} kills";
        return (odds, HourText(s.Count, activeSecs));
    }

    static string HourText(long count, double activeSecs) => activeSecs < 60 ? "" : $"{Whole(count / (activeSecs / 3600))} an hour";   // whole numbers, as the tables

    /// <summary>An item's slot: its icon in its grade frame with the stack count, the game-tooltip card on hover, pinned with Set a goal on a click.</summary>
    FrameworkElement ItemSlot(TodaySlot s, (string Odds, string Hour) rate, bool showWhere, bool runs = false, string when = "today")
    {
        var tile = CkIcon(s.Name, 46, FrameOf(s.Id), badge: CountBadge(s.Count));
        CardTip(tile, () => ItemTodayCard(s, rate, showWhere, runs, when));
        // a click pins the card with Set a goal at its foot (0.9; a right-click until 2026-10-31); a currency is an amount, not something picked up
        if (!s.Currency) PinOnClick(tile, () => ItemTodayCard(s, rate, showWhere, runs, when), ("Set a goal for this", () => OpenGoalSheet(null, GoalDraft.ForItem(s.Name), tile)));
        return tile;
    }

    /// <summary>
    /// The Abyss Points picked up in the Abyss's zones, a slot beside the Kinah's: a currency, so an amount and a pace, no price.
    /// <paramref name="when"/> is what the card says it covers ("today", "here", "this run").
    /// </summary>
    FrameworkElement AbyssSlot(long points, double activeSeconds, string when) =>
        ItemSlot(new TodaySlot(_items.IdOf("Abyss Points"), "Abyss Points", points, 0, 0, false, false, true, new()), ("", HourText(points, activeSeconds)), false, when: when);

    /// <param name="foot">The card's footer label ("Picked up today", LOOT's "Picked up here").</param>
    /// <param name="whereTitle">The heading over <paramref name="where"/>: zones for the day, enemies for LOOT's first side.</param>
    FrameworkElement KinahSlot(long kinah, List<(string Where, long Count)> where, string line, string foot = "Picked up today", string whereTitle = "WHERE")
    {
        var tile = WithBadge(CoinTile(46), CountBadge(kinah), "");
        tile.ToolTip = null;
        CardTip(tile, () => KinahTodayCard(kinah, where, line, foot, whereTitle));
        // a click on farming's Kinah pins its card with Set a Kinah goal (0.9); a run's Kinah isn't farming's, so not on the Events page
        if (_part == "Farming") PinOnClick(tile, () => KinahTodayCard(kinah, where, line, foot, whereTitle), ("Set a Kinah goal", () => OpenGoalSheet(null, new GoalDraft(), tile)));
        return tile;
    }

    /// <summary>A hover that shows a game-tooltip card (Ui/MainWindow.CardKit.cs), built the first time it opens.</summary>
    static void CardTip(FrameworkElement el, Func<FrameworkElement> build)
    {
        var tt = new ToolTip { Background = Brushes.Transparent, BorderThickness = new Thickness(0), Padding = new Thickness(0), HasDropShadow = false, Placement = PlacementMode.Bottom };
        el.ToolTip = tt;
        ToolTipService.SetInitialShowDelay(el, 200);
        ToolTipService.SetShowDuration(el, 60_000);
        el.ToolTipOpening += (_, _) =>
        {
            if (tt.Content != null) return;
            try { tt.Content = build(); }
            catch (Exception ex) { CrashLog.Write("today card", ex); tt.Content = new TextBlock { Text = "Couldn't draw this card." }; }
        };
    }

    /// <summary>The item's card: its band, where it came from today, its odds and pace (farming), and what it is worth.</summary>
    internal FrameworkElement ItemTodayCard(TodaySlot s, (string Odds, string Hour) rate, bool showWhere, bool runs, string when = "today")
    {
        var (card, body) = CkShell(300);
        var item = _items.Get(s.Id);
        var grade = item?.Grade ?? "";
        var cat = s.Gear ? "Gear" : s.Currency ? "Currency" : item?.Category is { Length: > 0 } c ? c : runs ? "Reward" : "Drop";
        body.Children.Add(CkBand(s.Name, grade, $"{cat} · ×{s.Count:N0} {when}"));
        if (showWhere && s.Where.Count > 0)
        {
            body.Children.Add(CkSection("WHERE"));
            foreach (var (where, n) in s.Where.Take(6)) body.Children.Add(CkKv(where, n.ToString("N0"), CkInk));
            if (s.Where.Count > 6) body.Children.Add(CkNote($"and {s.Where.Count - 6} more"));
        }
        if (rate.Odds.Length + rate.Hour.Length > 0)
        {
            body.Children.Add(CkSection(runs ? "PACE" : "ODDS"));
            body.Children.Add(CkKv(rate.Odds.Length > 0 ? rate.Odds : rate.Hour, rate.Odds.Length > 0 ? rate.Hour : "", CkInk));
        }
        if (s.Bound > 0 && s.Bound < s.Count) body.Children.Add(CkNote($"{s.Bound:N0} of these are bound and add nothing."));
        body.Children.Add(s.Worth > 0 ? TodayFooter("Worth, " + PriceBook.Cut, Fk(s.Worth))
            : s.Gear || s.Bound >= s.Count ? TodayFooter("Bound to you", "can't be sold", muted: true)
            : s.Currency ? TodayFooter("A currency", "not sold", muted: true)
            : TodayFooter("No Market price yet", "adds nothing", muted: true));
        return card;
    }

    /// <summary>The Kinah's card: the coin, where it was picked up, and the total.</summary>
    internal FrameworkElement KinahTodayCard(long kinah, List<(string Where, long Count)> where, string line, string foot = "Picked up today", string whereTitle = "WHERE")
    {
        var (card, body) = CkShell(300);
        var g = new Grid();
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); g.ColumnDefinitions.Add(new ColumnDefinition());
        var coin = CoinTile(64); coin.Margin = new Thickness(0, 0, 16, 0); g.Children.Add(coin);
        var names = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        names.Children.Add(CkText("Kinah", CkGold, 20, FontWeights.SemiBold, display: true));
        if (line.Length > 0) { var l = CkText(line, CkMuted, 13); l.Margin = new Thickness(0, 3, 0, 0); names.Children.Add(l); }
        Grid.SetColumn(names, 1); g.Children.Add(names);
        body.Children.Add(new Border
        {
            Child = g, Padding = new Thickness(18, 14, 18, 14), BorderThickness = new Thickness(0, 0, 0, 1), BorderBrush = Ck(CkGold, 0x70), CornerRadius = new CornerRadius(5, 5, 0, 0),
            Background = new LinearGradientBrush(new GradientStopCollection { new(Color.FromArgb(0x40, CkGold.R, CkGold.G, CkGold.B), 0), new(Color.FromArgb(0x14, CkGold.R, CkGold.G, CkGold.B), 0.55), new(Colors.Transparent, 1) }, 0),
        });
        if (where.Count > 1 || (where.Count == 1 && (line.Length == 0 || whereTitle != "WHERE")))
        {
            body.Children.Add(CkSection(whereTitle));
            foreach (var (w, n) in where.Take(6)) body.Children.Add(CkKv(w, Fk(n), CkInk));
            if (where.Count > 6) body.Children.Add(CkNote($"and {where.Count - 6} more"));
        }
        body.Children.Add(TodayFooter(foot, Fk(kinah), tip: Amounts.Full(kinah)));
        return card;
    }

    /// <summary>The card's tinted footer in the heading gold: a label and a figure.</summary>
    static Border TodayFooter(string label, string value, bool muted = false, string? tip = null)
    {
        var g = new Grid();
        g.ColumnDefinitions.Add(new ColumnDefinition()); g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        g.Children.Add(CkText(label, CkMuted, 13));
        var v = CkText(value, muted ? CkFaint : CkGold, muted ? 13 : 18, FontWeights.SemiBold, mono: !muted); v.Margin = new Thickness(12, 0, 0, 0);
        Grid.SetColumn(v, 1); g.Children.Add(v);
        if (tip != null) v.ToolTip = tip;
        return new Border
        {
            Child = g, Margin = new Thickness(0, 14, 0, 0), Padding = new Thickness(20, 11, 20, 12), BorderThickness = new Thickness(0, 1, 0, 0), BorderBrush = Ck(CkGoldRule, 0x50),
            CornerRadius = new CornerRadius(0, 0, 5, 5), Background = Ck(CkGold, 0x12),
        };
    }

    /// <summary>
    /// A sample day for layout renders: three zones of farming (the farming render's Western Cantas Valley drops, grown to a
    /// day) seeded the way a restart seeds Today. Example figures, never the player's.
    /// </summary>
    SessionEngine DemoDay()
    {
        var c = new SessionEngine.Carry { Day = DayStore.Today };
        void Zone(string zone, double active, int kills, long kinah, params (string Item, int N)[] drops)
        {
            var z = new SessionEngine.ZoneStats { Seconds = active + 240, Active = active, KillsSeen = kills, KinahDrops = kills, Kinah = kinah };
            foreach (var (item, n) in drops)
            {
                var id = _items.IdOf(item);
                z.Items[id] = z.Items.GetValueOrDefault(id) + n; c.Items[id] = c.Items.GetValueOrDefault(id) + n;
                z.Drops += n;
            }
            c.Zones[zone] = z;
            c.Active += active; c.Elapsed += active + 240; c.KillsSeen += kills; c.KinahDrops += kills; c.Kinah += kinah; c.Events += kills * 2 + z.Drops;
        }
        Zone("Western Cantas Valley", 72 * 60, 161, 632_000, ("Thick hide", 41), ("Spiritstone", 26), ("Fresh Fruit", 19), ("Manastone", 8), ("Aether shard", 7), ("Enchantment stone", 3), ("Greater manastone", 1), ("Soft Balaur Meat", 3), ("Refining Stone", 4));
        Zone("Eastern Cantas Valley", 58 * 60, 148, 560_000, ("Thick hide", 37), ("Spiritstone", 22), ("Fresh Fruit", 15), ("Manastone", 11), ("Aether shard", 5), ("Enchantment stone", 3), ("Soft Balaur Meat", 3), ("Refining Stone", 3));
        Zone("Altamia Canyon", 31 * 60, 103, 360_000, ("Thick hide", 18), ("Spiritstone", 13), ("Fresh Fruit", 10), ("Aether shard", 3), ("Enchantment stone", 1), ("Greater manastone", 1), ("Soft Balaur Meat", 1), ("Refining Stone", 2), ("Orichalcum Ore", 6));
        // passed through on the way (2026-10-28): under a minute, so it gets no chip, its one kill counted in All zones
        Zone("Drana Cultivation Area", 25, 1, 400);
        var d = new SessionEngine(_items);
        d.Seed(c);
        return d;
    }

    /// <summary>For a layout render (KL_SHOT_TODAY=1): the strip opened, as after a click on its line.</summary>
    internal void OpenTodaySample() { _prefs.TodayOpen = true; _lootDay = true; _drawnSig.Remove("today"); _drawnSig.Remove("loot"); FillToday(); }

    /// <summary>Layout renders: the cards a hover on today's first farming drop and on its Kinah open (ui-today-card.png, ui-today-kinah.png).</summary>
    internal void ShotTodayCards(string dir)
    {
        if (TodayCardSample() is Border b) SaveCard(b, System.IO.Path.Combine(dir, "ui-today-card.png"));
        // the same card pinned by a click, its action at the foot (2026-10-31)
        if (PinnedCard(() => TodayCardSample() ?? new Border(), new (string, Action)[] { ("Set a goal for this", () => { }) }) is Border pin && pin.Child != null)
            SaveCard(pin, System.IO.Path.Combine(dir, "ui-today-card-pinned.png"));
        var day = TodayEngine; if (day == null) return;
        var f = TodayLoot.Farm(day, _items);
        if (f.Kinah > 0 && KinahTodayCard(f.Kinah, f.KinahWhere, f.Kills > 0 ? $"{f.Kills:N0} kills · {Fk(f.Kinah / (double)f.Kills)} a kill" : "") is Border k) SaveCard(k, System.IO.Path.Combine(dir, "ui-today-kinah.png"));
    }

    /// <summary>Layout render of the Events page: the cards a hover on today's first run reward and on the runs' Kinah open (ui-today-run-card.png, ui-today-run-kinah.png).</summary>
    internal void ShotTodayRunCard(string dir)
    {
        var r = TodayLoot.Runs(TodayRunList(), _items, IsCurrency);
        if (r.Items.FirstOrDefault() is { } s && ItemTodayCard(s, ("", ""), true, true) is Border b) SaveCard(b, System.IO.Path.Combine(dir, "ui-today-run-card.png"));
        if (r.Kinah > 0 && KinahTodayCard(r.Kinah, r.KinahWhere, "") is Border k) SaveCard(k, System.IO.Path.Combine(dir, "ui-today-run-kinah.png"));
    }

    /// <summary>The card a hover on today's first farming drop opens, drawn on its own for review (ui-today-card.png).</summary>
    internal FrameworkElement? TodayCardSample()
    {
        var day = TodayEngine; if (day == null) return null;
        var f = TodayLoot.Farm(day, _items);
        var s = f.Drops.FirstOrDefault(); if (s == null) return null;
        return ItemTodayCard(s, FarmOdds(s, f.Kills, f.Active), f.Zones.Count > 1, false);
    }
}
