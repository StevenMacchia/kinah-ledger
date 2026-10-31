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
    sealed record LootHere(Tables S, long Kinah, int Kills, TimeSpan Elapsed, long Total, string TotalTip, List<(string Where, long Count)> ByEnemy);

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
        SummaryCard.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        if (!show) return;
        LootHereBtn.Content = LootHereName;
        LootHereBtn.ToolTip = _prefs.AllZones ? "Every zone since the tracker started (All zones is on, in ⋯)." : RunShown ? "This run, in this zone." : "Since you came to this zone. It starts over when you change zone.";
        LootHereBtn.Style = (Style)FindResource(_lootDay || _lootWeek ? "SegOff" : "SegOn");
        LootTodayBtn.Style = (Style)FindResource(_lootDay && !_lootWeek ? "SegOn" : "SegOff");
        LootWeekBtn.Style = (Style)FindResource(_lootWeek ? "SegOn" : "SegOff");
        if (_lootWeek) FillLootWeek(); else if (_lootDay) FillLootDay(whole); else FillLootHere();
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
        var drops = h.S.Drops.Where(r => !r.Header).ToList();
        var gathered = h.S.Gathered.Where(r => !r.Header).ToList();
        var pets = h.S.Pets ?? new();
        long crystals = pets.Sum(r => Qty(r.Drops));
        int kinds = drops.Count + gathered.Count + (crystals > 0 ? 1 : 0);
        FootTotalNote.Text = (kinds > 0 ? $"{kinds} kind{(kinds == 1 ? "" : "s")} · " : "") + TodayLoot.Dur(h.Elapsed.TotalSeconds);
        FootTotal.Text = h.Total > 0 ? Fk(h.Total) : " ";
        FootTotal.ToolTip = h.TotalTip;
        string Row(DropRow r) => $"{r.Name}:{r.Drops}:{r.DropPct}:{r.PerHour}:{r.Value}:{r.Reported}:{r.Grade}:{r.Sub}";
        var sig = $"here|{LootHereName}|{h.Kinah}|{h.Kills}|{crystals}|{string.Join(";", drops.Select(Row))}|{string.Join(";", gathered.Select(Row))}|"
            + $"{string.Join(",", h.ByEnemy.Take(6).Select(x => x.Where + x.Count))}|{string.Join(",", _todayAll)}|{Skins.Current}";
        if (!Changed("loot", sig)) return;
        LootHost.Children.Clear();
        string where = _prefs.AllZones ? "since the tracker started" : RunShown ? "this run" : "here";
        var slots = new List<FrameworkElement>();
        if (h.Kinah > 0) slots.Add(KinahSlot(h.Kinah, h.ByEnemy, h.Kills > 0 ? $"{h.Kills:N0} kills · {Fk(h.Kinah / (double)h.Kills)} a kill" : "", "Picked up " + where, "BY ENEMY"));
        foreach (var r in drops) slots.Add(HereSlot(r, false, where));
        foreach (var r in gathered) slots.Add(HereSlot(r, true, where));
        if (crystals > 0) slots.Add(SoulSlot(pets, crystals, where));
        if (slots.Count == 0) LootEmpty("Nothing picked up here yet.");
        else LootGroup("here", slots);
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
        FootTotal.Text = made > 0 ? Fk(made) : " ";
        FootTotal.ToolTip = made <= 0 ? null : $"Made farming today{(_todayZone != null ? " in " + ZoneName(_todayZone) : ", every zone")}: Kinah plus drops and gathering at the lowest Market price, after the Market's cut. Bound items and items with no price yet add nothing.";
        string Slots(IEnumerable<TodaySlot> l) => string.Join(",", l.Select(s => $"{s.Id}:{s.Count}:{s.Bound}:{s.Worth}"));
        var sig = $"day|{_todayZone}|{string.Join(";", zones.Select(z => $"{z.Zone}:{(int)(z.Active / 60)}"))}|{farm.Kinah}:{farm.Kills}:{farm.Attempts}:{(int)(farm.Active / 60)}|"
            + $"{Slots(farm.Drops)}|{Slots(farm.Gathered)}|{string.Join(",", _todayAll)}|{Skins.Current}";
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
                    $"{ZoneName(zone)}: {TodayLoot.Dur(z.Active)} farming, {z.Kills:N0} kills, {Fk(z.Made)} made after the Market's cut. Click to see only its loot; it stays pinned when you move."));
            }
            LootHost.Children.Add(chips);
        }
        bool showWhere = _todayZone == null && whole.Zones.Count > 1;
        var kills = new List<FrameworkElement>();
        if (farm.Kinah > 0) kills.Add(KinahSlot(farm.Kinah, farm.KinahWhere, farm.Kills > 0 ? $"{farm.Kills:N0} kills · {Fk(farm.Kinah / (double)farm.Kills)} a kill" : ""));
        foreach (var s in farm.Drops) kills.Add(ItemSlot(s, FarmOdds(s, farm.Kills, farm.Active), showWhere));
        var gather = farm.Gathered.Select(s => ItemSlot(s, farm.Attempts > 0 ? ($"{s.Count / (double)farm.Attempts:0.00} a try", HourText(s.Count, farm.Active)) : ("", HourText(s.Count, farm.Active)), showWhere)).ToList();
        // one group needs no heading; with both, each says what it is
        bool both = kills.Count > 0 && gather.Count > 0;
        if (kills.Count > 0) LootGroup("farm", kills, both ? "FROM KILLS" : "");
        if (gather.Count > 0) LootGroup("gather", gather, both ? "GATHERED" : "");
        if (kills.Count + gather.Count == 0) LootEmpty(_todayZone != null ? "Nothing picked up here yet today." : "Nothing picked up from farming yet today.");
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
        int perRow = Math.Max(4, (int)((LootWidth + TodayGap) / (TodaySlotSize + TodayGap)));
        var sp = new StackPanel { Margin = new Thickness(0, LootHost.Children.Count > 0 && title.Length > 0 ? 6 : 0, 0, 0) };
        if (title.Length > 0) { var t = PT(title, "SecGold", 11, FontWeights.Bold); t.Margin = new Thickness(0, 0, 0, 10); sp.Children.Add(t); }
        bool all = _todayAll.Contains(key);
        int max = perRow * TodayRows;
        var wrap = new WrapPanel();
        foreach (var s in all ? slots : slots.Take(max)) { s.Margin = new Thickness(0, 0, TodayGap, 8); wrap.Children.Add(s); }
        sp.Children.Add(wrap);
        if (slots.Count > max)
        {
            var m = PT(all ? "Show fewer" : $"Show all {slots.Count}", "Gold", 12, FontWeights.SemiBold);
            m.Cursor = Cursors.Hand; m.Background = Brushes.Transparent; m.HorizontalAlignment = HorizontalAlignment.Left; m.Margin = new Thickness(0, 0, 0, 2);
            m.MouseLeftButtonUp += (_, _) => { if (!_todayAll.Remove(key)) _todayAll.Add(key); _drawnSig.Remove("loot"); FillLoot(); };
            sp.Children.Add(m);
        }
        LootHost.Children.Add(sp);
    }

    /// <summary>A drop or gathered item of the count on screen: its slot, the card on hover, its full card (Set a goal, Looks wrong?) on a click.</summary>
    FrameworkElement HereSlot(DropRow r, bool gathered, string where)
    {
        var name = r.Report.Length > 0 ? r.Report : r.Name;
        var tile = CkIcon(name, 46, FrameOf(_items.IdOf(name)), badge: CountBadge(Qty(r.Drops)));
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
        body.Children.Add(r.Value.Length > 0 ? TodayFooter("Worth, after the Market's cut", r.Value)
            : bound ? TodayFooter("Bound to you", "can't be sold", muted: true)
            : TodayFooter("No Market price yet", "adds nothing", muted: true));
        return card;
    }

    /// <summary>The Soul Crystals of the count on screen as one slot, each kind on its card.</summary>
    FrameworkElement SoulSlot(List<DropRow> pets, long crystals, string where)
    {
        var tile = WithBadge(GlyphTile(46, "M12,2 L18,9 L12,22 L6,9 Z M6,9 H18", tint: Color.FromRgb(0x3E, 0x9E, 0x62)), CountBadge(crystals), "");
        CardTip(tile, () =>
        {
            var (card, body) = CkShell(300);
            body.Children.Add(CkSection("SOUL CRYSTALS"));
            foreach (var p in pets.Take(6)) body.Children.Add(CkKv(p.Name, p.Drops, CkInk));
            if (pets.Count > 6) body.Children.Add(CkNote($"and {pets.Count - 6} more"));
            body.Children.Add(TodayFooter("Picked up " + where, crystals.ToString("N0")));
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
    /// "stopped" (just after Stop run), "empty" (the game open, nothing counted yet), "goals" and "goals-add" (the GOALS box with
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
            case "stopped":
                Say("Run stopped (00:42:17 · 632K Kinah · 161 kills) and shared anonymously. Saved to your history (12 runs). Still counting.", "Good");
                break;
            case "empty":
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
        }
        _drawnSig.Remove("loot");
        Redraw();
        if (state == "paused" && !TableTitle.Text.EndsWith(" · paused")) TableTitle.Text += " · paused";
        // Share (0.9.3): the sheet open on This session, Last run or This week; share-picture also draws the pictures themselves
        if (state.StartsWith("share-")) OpenShareSample(state);
    }

    /// <summary>The chat out of view in a layout render (KL_SHOT_STATE=paused).</summary>
    bool _demoPaused;
}
