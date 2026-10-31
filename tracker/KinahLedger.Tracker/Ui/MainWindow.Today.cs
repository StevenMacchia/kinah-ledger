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
        TodayRuns? r = _part == "Events" && !_welcome ? TodayLoot.Runs(TodayRunList(), _items, IsCurrency) : null;
        // the runs' Week side (0.9, Ui/MainWindow.Week.cs): the strip shows too when only the last days had runs
        var week = r != null ? RunsWeekDays() : null;
        bool any = r?.Runs > 0 || week?.Any(d => d.Runs.Runs > 0) == true;
        TodayStrip.Visibility = any ? Visibility.Visible : Visibility.Collapsed;
        if (r == null || week == null || !any) return;
        TodayDayBtn.Style = (Style)FindResource(_runsWeek ? "SegOff" : "SegOn");
        TodayWeekBtn.Style = (Style)FindResource(_runsWeek ? "SegOn" : "SegOff");
        TodayTitle.Text = _runsWeek ? "WEEK" : "TODAY";
        if (_runsWeek) { FillRunsWeek(week); return; }

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
                : "Made in expeditions and events today: Kinah plus rewards at the lowest Market price, after the Market's cut. Gear, currencies, items with no price yet and runs left out of the averages add nothing.";
            TodayHead.ToolTip = "Today's expeditions and events since midnight on this PC. Click to see the rewards.";
        }
        bool open = _prefs.TodayOpen;
        SetSec(TodayChev, TodayBody, open);
        if (!open) { _drawnSig.Remove("today"); return; }

        // the slots, drawn again only when something in them changed
        string Slots(IEnumerable<TodaySlot> l) => string.Join(",", l.Select(s => $"{s.Id}:{s.Count}:{s.Bound}:{s.Worth}"));
        var sig = $"{_part}|{HeadStack.Width}|{string.Join(",", _todayAll)}|{Skins.Current}|{r.Runs}:{r.Places}:{r.Kinah}:{Slots(r.Items)}";
        if (!Changed("today", sig)) return;
        TodayHost.Children.Clear();
        int perRow = Math.Max(4, (int)((HeadStack.Width - 2 - 32 + TodayGap) / (TodaySlotSize + TodayGap)));
        if (r.Runs == 0) TodayHost.Children.Add(TodayEmpty("No runs yet today. Week has the last 7 days."));
        else FillTodayRuns(r, perRow);
    }

    /// <summary>The Events page's slots: the rewards of today's expeditions and events, and their Kinah.</summary>
    void FillTodayRuns(TodayRuns r, int perRow)
    {
        var slots = new List<FrameworkElement>();
        if (r.Kinah > 0) slots.Add(KinahSlot(r.Kinah, r.KinahWhere, ""));
        foreach (var s in r.Items) slots.Add(ItemSlot(s, ("", ""), true, runs: true));
        var g = TodayGroup("runs", "FROM EXPEDITIONS AND EVENTS", $"{r.Places} place{(r.Places == 1 ? "" : "s")}", r.Value, slots, perRow);
        if (slots.Count == 0) ((StackPanel)g).Children.Add(TodayEmpty("Nothing read from the reward screens yet."));
        TodayHost.Children.Add(g);
    }

    TextBlock TodayEmpty(string text) { var t = PT(text, "Faint", 12.5); t.Margin = new Thickness(0, 12, 0, 4); return t; }

    /// <summary>A group under the line: its name in the heading gold, a faint word, its worth on the right, then two rows of slots and "Show all".</summary>
    FrameworkElement TodayGroup(string key, string title, string note, long worth, List<FrameworkElement> slots, int perRow)
    {
        var sp = new StackPanel { Margin = new Thickness(0, 14, 0, 0) };
        var head = new Grid { Margin = new Thickness(0, 0, 0, 10) };
        head.ColumnDefinitions.Add(new ColumnDefinition()); head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var t = new TextBlock { FontSize = 11, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
        t.Inlines.Add(PR(title, "SecGold", FontWeights.Bold));
        if (note.Length > 0) { var n = PR("   " + note, "Muted"); n.FontSize = 12; t.Inlines.Add(n); }
        head.Children.Add(t);
        if (worth > 0)
        {
            var v = PT(Fk(worth), "Gold", 13, null, "MonoFont"); v.Margin = new Thickness(12, 0, 0, 0);
            v.ToolTip = "Kinah plus loot at the lowest Market price, after the Market's cut. Bound items and items with no price yet add nothing.";
            Grid.SetColumn(v, 1); head.Children.Add(v);
        }
        sp.Children.Add(head);
        bool all = _todayAll.Contains(key);
        int max = perRow * TodayRows;
        var wrap = new WrapPanel();
        foreach (var s in all ? slots : slots.Take(max)) { s.Margin = new Thickness(0, 0, TodayGap, 8); wrap.Children.Add(s); }
        sp.Children.Add(wrap);
        if (slots.Count > max)
        {
            var m = PT(all ? "Show fewer" : $"Show all {slots.Count}", "Gold", 12, FontWeights.SemiBold);
            m.Cursor = Cursors.Hand; m.Background = Brushes.Transparent; m.HorizontalAlignment = HorizontalAlignment.Left; m.Margin = new Thickness(0, 0, 0, 2);
            m.MouseLeftButtonUp += (_, _) => { if (!_todayAll.Remove(key)) _todayAll.Add(key); FillToday(); };
            sp.Children.Add(m);
        }
        return sp;
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
    FrameworkElement ItemSlot(TodaySlot s, (string Odds, string Hour) rate, bool showWhere, bool runs = false)
    {
        var tile = CkIcon(s.Name, 46, FrameOf(s.Id), badge: CountBadge(s.Count));
        CardTip(tile, () => ItemTodayCard(s, rate, showWhere, runs));
        // a click pins the card with Set a goal at its foot (0.9; a right-click until 2026-10-31); a currency is an amount, not something picked up
        if (!s.Currency) PinOnClick(tile, () => ItemTodayCard(s, rate, showWhere, runs), ("Set a goal for this", () => OpenGoalSheet(null, GoalDraft.ForItem(s.Name), tile)));
        return tile;
    }

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
    internal FrameworkElement ItemTodayCard(TodaySlot s, (string Odds, string Hour) rate, bool showWhere, bool runs)
    {
        var (card, body) = CkShell(300);
        var item = _items.Get(s.Id);
        var grade = item?.Grade ?? "";
        var cat = s.Gear ? "Gear" : s.Currency ? "Currency" : item?.Category is { Length: > 0 } c ? c : runs ? "Reward" : "Drop";
        body.Children.Add(CkBand(s.Name, grade, $"{cat} · ×{s.Count:N0} today"));
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
        body.Children.Add(s.Worth > 0 ? TodayFooter("Worth, after the Market's cut", Fk(s.Worth))
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
        body.Children.Add(TodayFooter(foot, kinah.ToString("N0")));
        return card;
    }

    /// <summary>The card's tinted footer in the heading gold: a label and a figure.</summary>
    static Border TodayFooter(string label, string value, bool muted = false)
    {
        var g = new Grid();
        g.ColumnDefinitions.Add(new ColumnDefinition()); g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        g.Children.Add(CkText(label, CkMuted, 13));
        var v = CkText(value, muted ? CkFaint : CkGold, muted ? 13 : 18, FontWeights.SemiBold, mono: !muted); v.Margin = new Thickness(12, 0, 0, 0);
        Grid.SetColumn(v, 1); g.Children.Add(v);
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
