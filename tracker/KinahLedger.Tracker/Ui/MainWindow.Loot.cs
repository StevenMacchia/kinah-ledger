using KinahLedger.Data;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using KinahLedger.Session;

namespace KinahLedger.Ui;

/// <summary>
/// LOOT on the Farming page (2026-10-28, option C of kinah-bench/mock-calm-farming; Steven, of 0.8.53's page: "THIS IS A LOT
/// OF INFO ON ONE WINDOW"). Under the live card, one box of inventory slots for one period at a time: this zone since you
/// arrived (the run while one is on, every zone with All zones on), or Today since midnight on this PC; one total on its
/// line. Each slot opens the game-tooltip card on hover (odds, an hour, worth); a click opens the item's full card (Set a
/// goal, Looks wrong?). It replaced the SESSION line (the same count as the card above it), the Today strip on this page
/// and, in Compact view, the tables (Full view keeps them beside it). Drawn again only when what it shows changed.
/// </summary>
public partial class MainWindow
{
    /// <summary>True while LOOT shows Today; this zone otherwise. Kept while the tracker runs.</summary>
    bool _lootDay;
    /// <summary>What FillNumbers last worked out for the count on screen: LOOT's first side.</summary>
    LootHere? _lootHere;

    /// <param name="Total">Kinah plus loot after the Market's cut, as the total on LOOT's line.</param>
    /// <param name="TotalTip">How the total adds up, on its hover.</param>
    /// <param name="ByEnemy">The Kinah by the enemy it was credited to, most first, for the Kinah slot's card.</param>
    sealed record LootHere(Tables S, long Kinah, int Kills, TimeSpan Elapsed, long Total, string TotalTip, List<(string Where, long Count)> ByEnemy, long Abyss = 0);

    /// <summary>The slots' width: the card's 662 less its two sides and LOOT's 20 px margins.</summary>
    const double LootWidth = 662 - 2 - 40;

    /// <summary>A run on in a layout render (KL_SHOT_STATE=run): the button says Stop run and LOOT's first side This run.</summary>
    bool _demoRun;
    bool RunShown => RunEngine != null || (_demo && _demoRun);

    /// <summary>The first side's name, for what the count on screen covers.</summary>
    string LootHereName => _prefs.AllZones ? "All zones" : RunShown ? "This run" : "This zone";

    void InitLoot()
    {
        LootHereBtn.Click += (_, _) => PickLoot(false);
        LootTodayBtn.Click += (_, _) => PickLoot(true);
        InitWeek();   // LOOT's Week side and the Events page's (Ui/MainWindow.Week.cs)
    }

    void PickLoot(bool day) { _lootDay = day; _lootWeek = false; _drawnSig.Remove("loot"); FillLoot(); }

    /// <summary>The order picked for the loot slots (LootOrder): worth unless the player chose amount or quality.</summary>
    string LootSortMode => LootOrder.Of(_prefs.LootSort);

    List<TodaySlot> SortSlots(IEnumerable<TodaySlot> l) => LootOrder.Sorted(l, LootSortMode, s => s.Count, s => s.Worth, s => _items.Get(s.Id)?.Grade ?? "", s => s.Name);
    List<DropRow> SortRows(IEnumerable<DropRow> l) => LootOrder.Sorted(l, LootSortMode, r => Qty(r.Drops), r => r.Worth, r => r.Grade, r => r.Name);

    /// <summary>The item slots as they were last drawn, in order, for options-test: a name, its count, its grade, and whether it is in the gathered group (ordered on its own).</summary>
    internal List<(string Name, long Count, string Grade, bool Gathered)> LootOrderForTest { get; } = new();

    /// <summary>For options-test: the order picked, as a click on its link.</summary>
    internal void SetLootSortForTest(string mode) { _prefs.LootSort = mode; _drawnSig.Remove("loot"); _drawnSig.Remove("today"); FillToday(); }

    /// <summary>
    /// "Sort  worth · amount · quality", the one picked in ink, the others links (gold until the colour pass): a click keeps it (ui.json) and draws the slots again. Over
    /// LOOT's slots on Farming and in the TODAY strip's heading on Events; shown once there are two slots to order.
    /// </summary>
    FrameworkElement SortLinks(string sigKey, Action redraw)
    {
        var sp = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        var label = PT("Sort", "Muted", 12); label.Margin = new Thickness(0, 0, 7, 0); sp.Children.Add(label);
        for (int i = 0; i < LootOrder.Modes.Length; i++)
        {
            var mode = LootOrder.Modes[i];
            bool on = mode == LootSortMode;
            if (i > 0) { var dot = PT("·", "Muted", 12); dot.Margin = new Thickness(5, 0, 5, 0); sp.Children.Add(dot); }
            var t = PT(mode, "Ink", 12, on ? FontWeights.SemiBold : null);
            if (!on) QLinkify(t);
            t.Cursor = on ? Cursors.Arrow : Cursors.Hand; t.Background = Brushes.Transparent; t.ToolTip = LootOrder.Tip(mode); ToolTipService.SetInitialShowDelay(t, 400);
            t.MouseLeftButtonUp += (_, e) => { e.Handled = true; if (mode == LootSortMode) return; _prefs.LootSort = mode; SavePrefs(); _drawnSig.Remove(sigKey); redraw(); };
            sp.Children.Add(t);
        }
        return sp;
    }

    /// <summary>The sort links on their own right-aligned row over LOOT's slots.</summary>
    void LootSortRow()
    {
        var e = SortLinks("loot", FillLoot);
        e.HorizontalAlignment = HorizontalAlignment.Right; e.Margin = new Thickness(0, 0, 0, 8);
        LootHost.Children.Add(e);
    }

    /// <summary>For options-test and the layout renders: LOOT on Today or on its first side.</summary>
    internal void PickLootForTest(bool day) => PickLoot(day);

    /// <summary>LOOT for the side picked: shown once there is loot here or today, its slots drawn again only when they changed.</summary>
    void FillLoot()
    {
        FillGoals();   // GOALS sits over LOOT once there is a goal (Ui/MainWindow.Goals.cs)
        bool farm = _part == "Farming" && !_welcome;
        TodayFarm? whole = farm && TodayEngine is { } day ? TodayLoot.Farm(day, _items) : null;
        // shown too when only the last days had farming, so the Week side is there before the day's first kill
        bool show = farm && (_hasSummary || whole?.Any == true || FarmedThisWeek());
        // Quest (2026-11-07): the bag opens from the loot chip in Compact; Full view has it as a column of its own
        SummaryCard.Visibility = show && (FullShown || _qChip == "bag") ? Visibility.Visible : Visibility.Collapsed;
        if (!show) return;
        LootHereBtn.Content = LootHereName;
        LootHereBtn.ToolTip = _prefs.AllZones ? "Every zone since your last break (15 minutes with nothing farmed). Click the hour to see this zone only." : RunShown ? "This run, in this zone." : "Since you came to this zone. It starts over when you change zone.";
        LootHereBtn.Style = (Style)FindResource(_lootDay || _lootWeek ? "SegOff" : "SegOn");
        LootTodayBtn.Style = (Style)FindResource(_lootDay && !_lootWeek ? "SegOn" : "SegOff");
        LootWeekBtn.Style = (Style)FindResource(_lootWeek ? "SegOn" : "SegOff");
        FootTotal.Visibility = Visibility.Visible;   // the first side has none (FillLootHere)
        // in Compact the bag card opened from the hero's loot figure stands in for the strip; its head puts it away again
        SessionHead.Cursor = FullShown ? null : Cursors.Hand;
        if (_lootWeek) FillLootWeek(); else if (_lootDay) FillLootDay(whole); else FillLootHere();
        FillBagStrip();   // the Compact page's slots with counts (Ui/MainWindow.QuestFarm.cs)
    }

    /// <summary>LOOT's first side: the Kinah, every drop and gathered item, and the Soul Crystals of the count on screen.</summary>
    void FillLootHere()
    {
        var h = _lootHere;
        if (h == null || !_hasSummary)
        {
            FootTotalNote.Text = ""; FootTotal.Text = " "; FootTotal.ToolTip = null;
            if (Changed("loot", "here-empty")) { LootHost.Children.Clear(); LootEmpty("Loot shows here as it's picked up. Today has what came in other zones."); }
            return;
        }
        var drops = SortRows(h.S.Drops.Where(r => !r.Header));
        var gathered = SortRows(h.S.Gathered.Where(r => !r.Header));
        var pets = h.S.Pets ?? new();
        long crystals = pets.Sum(r => Qty(r.Drops));
        int kinds = drops.Count + gathered.Count + (crystals > 0 ? 1 : 0);
        // the UX pass (2026-11-13): this side's Kinah, loot and how long are the hero's (its line and its "Open world · 42 min"),
        // so the bag says only how many kinds; no Kinah slot and no total, which was the hero line's two figures added up
        FootTotalNote.Text = kinds > 0 ? $"{kinds} kind{(kinds == 1 ? "" : "s")}" : "";
        Motion.Blank(FootTotal, "foot|here|" + LootHereName, " ");
        FootTotal.ToolTip = null;
        FootTotal.Visibility = Visibility.Collapsed;
        string Row(DropRow r) => $"{r.Name}:{r.Drops}:{r.DropPct}:{r.PerHour}:{r.Value}:{r.Reported}:{r.Grade}:{r.Sub}";
        var sig = $"here|{LootHereName}|{LootSortMode}|{h.Kinah}|{h.Abyss}|{h.Kills}|{crystals}|{string.Join(";", drops.Select(Row))}|{string.Join(";", gathered.Select(Row))}|"
            + $"{string.Join(",", h.ByEnemy.Take(6).Select(x => x.Where + x.Count))}|{string.Join(",", _todayAll)}|{FullShown}|{Skins.Current}";
        if (!Changed("loot", sig)) return;
        LootHost.Children.Clear();
        LootOrderForTest.Clear(); LootOrderForTest.AddRange(drops.Select(r => (r.Name, Qty(r.Drops), r.Grade, false)).Concat(gathered.Select(r => (r.Name, Qty(r.Drops), r.Grade, true))));
        if (drops.Count + gathered.Count >= 2) LootSortRow();
        string where = _prefs.AllZones ? "since your last break" : RunShown ? "this run" : "here";
        var slots = new List<FrameworkElement>();
        if (h.Abyss > 0) slots.Add(AbyssSlot(h.Abyss, h.Elapsed.TotalSeconds, where));
        // a drop that lands grows in, a rare one glows once (Motion.Slot); Kinah and Abyss Points rise with every kill and stay still
        var landHost = "loot-here|" + LootHereName;
        Motion.Begin(landHost);
        foreach (var (r, g) in drops.Select(r => (r, false)).Concat(gathered.Select(r => (r, true))))
        {
            var t = HereSlot(r, g, where);
            Motion.Slot(t, landHost, r.Name, Qty(r.Drops), r.Grade);
            slots.Add(t);
        }
        if (crystals > 0) { var c = SoulSlot(pets, crystals, where); Motion.Slot(c, landHost, "soul crystals", crystals); slots.Add(c); }
        if (slots.Count == 0) LootEmpty("Nothing picked up here yet.");
        else LootGroup("here", slots);
        Motion.End(landHost);
    }

    /// <summary>LOOT's Today side: the zones farmed for a minute or more (one pinned, or all), then the day's loot.</summary>
    void FillLootDay(TodayFarm? whole)
    {
        if (whole == null || !whole.Any)
        {
            FootTotalNote.Text = ""; FootTotal.Text = " "; FootTotal.ToolTip = null;
            if (Changed("loot", "day-empty")) { LootHost.Children.Clear(); LootEmpty("Nothing picked up from farming yet today."); }
            return;
        }
        // a zone under a minute gets no chip (2026-10-28: "Drana Cultivation Area 0:00", "Zone not read 0:00"); its loot stays in All zones
        var zones = whole.Zones.Where(z => z.Active >= 60).ToList();
        if (_todayZone != null && !zones.Any(z => z.Zone == _todayZone)) _todayZone = null;
        var farm = _todayZone != null ? TodayLoot.Farm(TodayEngine!, _items, _todayZone) : whole;
        FootTotalNote.Text = string.Join(" · ", TodayLoot.FarmLine(farm).Select(n => n.Fig + n.Word));
        long made = farm.Made;
        if (made > 0) Motion.Number(FootTotal, "foot|day|" + _todayZone, made, v => Fk(v)); else Motion.Blank(FootTotal, "foot|day|" + _todayZone, " ");
        FootTotal.ToolTip = made <= 0 ? null : $"Made farming today{(_todayZone != null ? " in " + ZoneName(_todayZone) : ", every zone")}: Kinah plus drops and gathering at the lowest Market price, " + PriceBook.Cut + ". Bound items and items with no price yet add nothing.";
        string Slots(IEnumerable<TodaySlot> l) => string.Join(",", l.Select(s => $"{s.Id}:{s.Count}:{s.Bound}:{s.Worth}"));
        var sig = $"day|{_todayZone}|{LootSortMode}|{string.Join(";", zones.Select(z => $"{z.Zone}:{(int)(z.Active / 60)}"))}|{farm.Kinah}:{farm.Abyss}:{farm.Kills}:{farm.Attempts}:{(int)(farm.Active / 60)}|"
            + $"{Slots(farm.Drops)}|{Slots(farm.Gathered)}|{string.Join(",", _todayAll)}|{FullShown}|{Skins.Current}";
        if (!Changed("loot", sig)) return;
        LootHost.Children.Clear();
        if (zones.Count >= 2)
        {
            var chips = new WrapPanel { Margin = new Thickness(0, 0, 0, 8) };
            chips.Children.Add(ZoneChip("All zones", "", _todayZone == null, () => _todayZone = null));
            foreach (var z in zones)
            {
                var zone = z.Zone;
                chips.Children.Add(ZoneChip(ZoneName(zone), TodayLoot.Dur(z.Active), _todayZone == zone, () => _todayZone = zone,
                    $"{ZoneName(zone)}: {TodayLoot.Dur(z.Active)} farming, {z.Kills:N0} kills, {Fk(z.Made)} made {PriceBook.Cut}. Click to see only its loot; it stays pinned when you move."));
            }
            LootHost.Children.Add(chips);
        }
        bool showWhere = _todayZone == null && whole.Zones.Count > 1;
        var dayDrops = SortSlots(farm.Drops); var dayGathered = SortSlots(farm.Gathered);
        LootOrderForTest.Clear(); LootOrderForTest.AddRange(dayDrops.Select(s => (s.Name, s.Count, _items.Get(s.Id)?.Grade ?? "", false)).Concat(dayGathered.Select(s => (s.Name, s.Count, _items.Get(s.Id)?.Grade ?? "", true))));
        if (dayDrops.Count + dayGathered.Count >= 2) LootSortRow();
        var kills = new List<FrameworkElement>();
        if (farm.Kinah > 0) kills.Add(KinahSlot(farm.Kinah, farm.KinahWhere, farm.Kills > 0 ? $"{farm.Kills:N0} kills · {Fk(farm.Kinah / (double)farm.Kills)} a kill" : ""));
        if (farm.Abyss > 0) kills.Add(AbyssSlot(farm.Abyss, farm.Active, "today"));
        foreach (var s in dayDrops) kills.Add(ItemSlot(s, FarmOdds(s, farm.Kills, farm.Active), showWhere));
        var gather = dayGathered.Select(s => ItemSlot(s, farm.Attempts > 0 ? ($"{s.Count / (double)farm.Attempts:0.00} a try", HourText(s.Count, farm.Active)) : ("", HourText(s.Count, farm.Active)), showWhere)).ToList();
        // one group needs no heading; with both, each says what it is
        bool both = kills.Count > 0 && gather.Count > 0;
        if (kills.Count > 0) LootGroup("farm", kills, both ? "FROM KILLS" : "");
        if (gather.Count > 0) LootGroup("gather", gather, both ? "GATHERED" : "");
        if (kills.Count + gather.Count == 0) LootEmpty(_todayZone != null ? "Nothing picked up here yet today." : "Nothing picked up from farming yet today.");
        LootHost.Children.Add(WrappedButton(Wrapped.Day));
    }

    static string ZoneName(string zone) => zone == SessionEngine.UnknownZone ? "Zone not read" : zone;

    static long Qty(string drops) => long.TryParse(drops, System.Globalization.NumberStyles.AllowThousands, System.Globalization.CultureInfo.InvariantCulture, out var n) ? n
        : long.TryParse(drops, System.Globalization.NumberStyles.AllowThousands, null, out n) ? n : 0;

    void LootEmpty(string text)
    {
        var t = PT(text, "Faint", 12.5); t.TextWrapping = TextWrapping.Wrap; t.TextTrimming = TextTrimming.None; t.Margin = new Thickness(0, 0, 0, 4);
        LootHost.Children.Add(t);
    }

    /// <summary>A group of slots: an optional heading, two rows, then "Show all" (the Today strip's grammar).</summary>
    void LootGroup(string key, List<FrameworkElement> slots, string title = "")
    {
        const int perRow = 4;   // Quest tiles, 149 px each with 8 px between, in the 632 px card
        var sp = new StackPanel { Margin = new Thickness(0, LootHost.Children.Count > 0 && title.Length > 0 ? 6 : 0, 0, 0) };
        if (title.Length > 0) { var t = QT(title, 12, FontWeights.SemiBold, "Muted"); t.Margin = new Thickness(0, 0, 0, 8); sp.Children.Add(t); }
        bool all = _todayAll.Contains(key);
        int max = perRow * (FullShown ? 3 : TodayRows);
        var wrap = new WrapPanel();
        foreach (var s in all ? slots : slots.Take(max)) { s.Margin = new Thickness(0, 0, 8, 8); wrap.Children.Add(s); }
        sp.Children.Add(wrap);
        if (slots.Count > max)
        {
            var m = QLinkify(PT(all ? "Show fewer" : $"{slots.Count - max} more", "Link", 12, FontWeights.SemiBold));   // tiles, not kinds: Kinah has one too
            m.HorizontalAlignment = HorizontalAlignment.Left; m.Margin = new Thickness(0, 0, 0, 2);
            m.MouseLeftButtonUp += (_, _) => { if (!_todayAll.Remove(key)) _todayAll.Add(key); _drawnSig.Remove("loot"); FillLoot(); };
            sp.Children.Add(m);
        }
        LootHost.Children.Add(sp);
    }

    /// <summary>A drop or gathered item of the count on screen: its slot, the card on hover, its full card (Set a goal, Looks wrong?) on a click.</summary>
    FrameworkElement HereSlot(DropRow r, bool gathered, string where)
    {
        var name = r.Report.Length > 0 ? r.Report : r.Name;
        // the Quest tile (2026-11-07): the game's icon, the count, the name, the worth or why there is none
        var tile = QTile(QItemIcon(name, _items.IdOf(name), 34, round: false, grade: r.Grade), Qty(r.Drops).ToString("N0"), name,
            r.Worth > 0 ? r.Value : r.Sub.Contains("bound", StringComparison.OrdinalIgnoreCase) ? "bound" : "no price yet", r.Worth > 0 ? "Gold" : "Faint",
            roll: ("loot-count|" + LootHereName + "|" + name, Qty(r.Drops)));
        if (r.Reported) tile.Opacity = 0.55;
        CardTip(tile, () => ItemHereCard(r, gathered, where));
        if (r.Report.Length == 0) return tile;
        tile.Cursor = Cursors.Hand;
        // a click opens its full card, whose foot has Set a goal and Looks wrong? (no right-click, 2026-10-31)
        tile.MouseLeftButtonUp += (_, _) => ShowItemCard(r, tile);
        return tile;
    }

    /// <summary>The card a drop's slot opens on hover: its band, the odds and the hour's pace, why it may be off, and what it is worth.</summary>
    internal FrameworkElement ItemHereCard(DropRow r, bool gathered, string where)
    {
        var (card, body) = CkShell(300);
        var name = r.Report.Length > 0 ? r.Report : r.Name;
        var cat = gathered ? "Gathering material" : _items.Get(_items.IdOf(name))?.Category is { Length: > 0 } c ? c : "Drop";
        body.Children.Add(CkBand(name, r.Grade, $"{cat} · ×{r.Drops} {where}"));
        if (r.Reported) body.Children.Add(CkNote("Reported: kept out of what's sent to the site."));
        if (r.DropPct.Length + r.PerHour.Length > 0)
        {
            body.Children.Add(CkSection(gathered ? "PACE" : "ODDS"));
            if (r.DropPct.Length > 0) body.Children.Add(CkKv(gathered ? "A try" : "Odds", r.DropPct, CkInk));
            if (r.PerHour.Length > 0) body.Children.Add(CkKv("An hour", r.PerHour, CkInk));
        }
        if (r.Tip.Length > 0) { var n = CkNote(r.Tip); n.Margin = new Thickness(20, 10, 20, 2); body.Children.Add(n); }
        if (r.Report.Length > 0) { var n = CkNote(r.Reported ? "Reported · click to set a goal or undo the report" : "Click to set a goal or report it"); n.Margin = new Thickness(20, 10, 20, 0); body.Children.Add(n); }
        bool bound = r.Sub.Contains("bound");
        body.Children.Add(r.Value.Length > 0 ? TodayFooter("Worth, " + PriceBook.Cut, r.Value)
            : bound ? TodayFooter("Bound to you", "can't be sold", muted: true)
            : TodayFooter("No Market price yet", "adds nothing", muted: true));
        return card;
    }

    /// <summary>The Soul Crystals of the count on screen as one slot, each kind on its card.</summary>
    FrameworkElement SoulSlot(List<DropRow> pets, long crystals, string where)
    {
        var tile = QTile(QIconBox("Gem", "Lime", 34), crystals.ToString("N0"), "Soul Crystals", "pet food", "Faint");
        CardTip(tile, () =>
        {
            var (card, body) = CkShell(300);
            body.Children.Add(CkSection("SOUL CRYSTALS"));
            foreach (var p in pets.Take(6)) body.Children.Add(CkKv(p.Name, p.Drops, CkInk));
            if (pets.Count > 6) body.Children.Add(CkNote($"and {pets.Count - 6} more"));
            body.Children.Add(TodayFooter("Picked up " + where, Fk(crystals), tip: Amounts.Full(crystals)));
            return card;
        });
        return tile;
    }

    /// <summary>The count on screen's loot as LOOT shows it, for options-test: the slots drawn and the line's words.</summary>
    internal (int Slots, string Note, string Total, string Here) LootForTest()
        => (LootHost.Children.OfType<Panel>().Sum(p => p.Children.OfType<WrapPanel>().Sum(w => w.Children.Count)), FootTotalNote.Text, FootTotal.Text, LootHereBtn.Content as string ?? "");

    /// <summary>The zone chips LOOT's Today side shows, by their words, for options-test.</summary>
    internal List<string> LootChipsForTest()
        => LootHost.Children.OfType<WrapPanel>().SelectMany(w => w.Children.OfType<Border>())
            .Select(b => string.Join(" ", ((Panel)b.Child).Children.OfType<TextBlock>().Select(t => t.Text))).ToList();

    /// <summary>
    /// Layout renders of the Farming page's other states (KL_SHOT_STATE): "run" (a run on), "paused" (the chat out of view),
    /// "stopped" (just after Stop run), "fresh" (just after Start run with counts, its Undo bar up), "empty" (the game open, nothing counted yet), "goals" and "goals-add" (the GOALS box with
    /// three sample goals, or two and the "+" slot), "goals-none" (no goal: the slim "Set a goal for today" row), "week" and "week-new" (LOOT's Week side, Ui/MainWindow.Week.cs),
    /// "share-session", "share-run", "share-week" and "share-picture" (the Share sheet open, Ui/MainWindow.Share.cs).
    /// Example figures, never the player's.
    /// </summary>
    internal void ShowStateSample(string state)
    {
        switch (state)
        {
            case "run":
                _demoRun = true; SetTrackingUi(true); _tableWhat = "this run";
                Say("Run started · Western Cantas Valley. Minimize this window; the panel and Ctrl+Shift+K keep working.", "Faint");
                break;
            case "paused":
                _demoPaused = true; LiveMark.Visibility = Visibility.Collapsed;
                Tell("Paused: the chat is out of view. Only the game is read, so reading picks up when the chat is in view again", "Warn");
                break;
            // "fresh" (0.9.13): just after Start run with counts on the tables, the run on at zero and its Undo bar up
            case "fresh":
                _demoRun = true; _tableWhat = "this run";
                _ = OfferFromZero();
                Say("Run started from zero · Western Cantas Valley. Minimize this window; the panel and Ctrl+Shift+K keep working.", "Faint");
                SetTrackingUi(true);
                break;
            case "stopped":
                SayOnLine("Run stopped (00:42:17 · 632K Kinah · 161 kills) and shared anonymously. Saved to your history (12 runs). Still counting.", "Good");
                break;
            // "onboard" (2026-11-04): the empty feed with a new player's checklist, halfway through (Ui/MainWindow.Onboard.cs)
            case "empty":
            case "onboard":
                _demoEngine = new SessionEngine(_items); _demoElapsed = TimeSpan.FromSeconds(40);
                _demoFeed.RemoveAll(l => l.Tab == FeedTab.Farming);
                _demoDay = null; _demoScore = null; _rateLine.Clear(); _demoDays = null;
                Tell("", "Faint");
                break;
            // GOALS (0.9, Goals B of kinah-bench/mock-goals-history): three sample goals over the sample day, the expeditions
            // one reached; "goals-add" two of them and the "+" slot (Ui/MainWindow.Goals.cs)
            // "goals-none": no goal, so the slim row in the box's place (2026-10-30)
            case "goals":
            case "goals-add":
            case "goals-none":
                ShowGoalsSample(state == "goals" ? 3 : state == "goals-add" ? 2 : 0);
                break;
            // LOOT's Week side (0.9): a past day picked, as the mock draws it; and history that began two days ago
            case "week":
            case "week-new":
                ShowWeekSample(state == "week-new");
                break;
            // Compare (0.9.3, Ui/MainWindow.Compare.cs): the switch on, LOOT's Week side on Zones; or LOOT as it opens, the usual here under the hour
            case "week-zones":
            case "compare-on":
                ShowCompareSample(state);
                break;
            // What's new (0.9.12): the card over the live card, with this build's notes (Ui/MainWindow.WhatsNew.cs)
            case "whats-new":
                ShowWhatsNewSample();
                break;

        }
        _drawnSig.Remove("loot");
        Redraw();
        if (state == "onboard") ShowOnboardSample(true);
        if (state == "paused" && !TableTitle.Text.EndsWith(" · paused")) TableTitle.Text += " · paused";
        // Share (0.9.3): the sheet open on This session, Last run or This week; share-picture also draws the pictures themselves
        if (state.StartsWith("share-")) OpenShareSample(state);
        // the hero's rate chart open, live (Ui/MainWindow.RateChart.cs)
        if (state is "chart" or "chart-hover") ShowChartSample(state);
        // what a figure of the hero's line opens in Compact (UX pass, 2026-11-13): Who pays, the bag, Kills
        if (state is "who" or "bag" or "kills") ToggleChip(state);
    }

    /// <summary>The chat out of view in a layout render (KL_SHOT_STATE=paused).</summary>
    bool _demoPaused;
}
