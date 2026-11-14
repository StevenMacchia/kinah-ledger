using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using KinahLedger.Data;
using KinahLedger.Session;

namespace KinahLedger.Ui;

/// <summary>
/// The Runs page opens on the latest run (Steven, 2026-11-14: "it currently defaults to showing the OVERALL amount of loot
/// added up after a run. It should show the latest run. Underneath the 'Paid out here' and under that an expandable 'Best
/// drops' that is sortable"). The latest run's card: the place (the dropdown of places), the run's number, when it ran and
/// how long it took, what it made big in gold, its Kinah, loot and kills, its drops as slots, and a ring against the best run
/// here. A small switch turns the card to All runs, the place's hour and averages as before. Under Paid out here, Best
/// drops: every item the counted runs here brought, folded until opened, sorted by value, count, name or when.
/// </summary>
public partial class MainWindow
{
    /// <summary>The Runs page's top card: the latest run ("latest", the default) or the place's runs together ("all").</summary>
    string _runsView = "latest";
    /// <summary>Best drops' order: "value" (the default), "count", "name" or "when".</summary>
    string _bestDropsSort = "value";
    /// <summary>How many Best drops show before Show all.</summary>
    const int BestDropsShown = 10;

    /// <summary>The run the latest card shows: the open run while one is, else the newest.</summary>
    static InstanceRun? LatestRunOf(List<InstanceRun> runs, InstanceRun? open) => open ?? runs.OrderByDescending(r => r.LastLoot ?? r.Start).FirstOrDefault();

    /// <summary>The switch at the top card's corner: Latest run or All runs; a click redraws the page.</summary>
    FrameworkElement RunsViewSwitch(Action redraw)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        foreach (var (key, text) in new[] { ("latest", "Latest run"), ("all", "All runs") })
        {
            var b = new Button { Content = text, Height = 24, FontSize = 11.5, Padding = new Thickness(9, 0, 9, 0), Margin = new Thickness(row.Children.Count > 0 ? 2 : 0, 0, 0, 0), Tag = "runs-view:" + key };
            b.SetResourceReference(StyleProperty, _runsView == key ? "SegOn" : "SegOff");
            b.ToolTip = key == "latest" ? "The latest run here on its own: what it made and what it dropped." : "Every run here together: the hour, a run on average, the best run.";
            var k = key;
            b.Click += (_, _) => { if (_runsView == k) return; _runsView = k; _drawnSig.Remove("runs"); redraw(); };
            row.Children.Add(b);
        }
        var box = new Border { Child = row, Padding = new Thickness(2), CornerRadius = new CornerRadius(9), BorderThickness = new Thickness(1), VerticalAlignment = VerticalAlignment.Top };
        box.SetResourceReference(Border.BackgroundProperty, "SegGroup");
        box.SetResourceReference(Border.BorderBrushProperty, "CardLine");
        return box;
    }

    /// <summary>
    /// The latest run's card: the place and the run (its number, when, how long, or its step while open), what it made, its
    /// Kinah, loot and kills on one line, the ring against the best run here, then its drops as slots.
    /// </summary>
    Border QLatestRun(string zone, List<InstanceRun> runs, List<InstanceRun> counted, InstanceRun run, InstanceRun? open, string kind, Func<List<MenuEntry>>? pickPlace, Action redraw)
    {
        var card = new Border { Padding = new Thickness(16, 12, 16, 12), Tag = "latest-run" };
        card.SetResourceReference(StyleProperty, "QCard");
        card.SetResourceReference(Border.BackgroundProperty, "HeroBg");
        var g = new Grid();
        g.ColumnDefinitions.Add(new ColumnDefinition()); g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(14) }); g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var left = new StackPanel();

        // the kicker: the place's icon, its name with its kind (the dropdown of places), the run's line
        var kick = new Grid { Background = Brushes.Transparent };
        kick.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); kick.ColumnDefinitions.Add(new ColumnDefinition());
        var ic = QIconBox(PlaceIcon(zone, kind), "Sky", 36, round: false, iconSize: 19); ic.VerticalAlignment = VerticalAlignment.Center; kick.Children.Add(ic);
        var titles = new StackPanel { Margin = new Thickness(10, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        var nameRow = new StackPanel { Orientation = Orientation.Horizontal };
        var name = new TextBlock { TextTrimming = TextTrimming.CharacterEllipsis, MaxWidth = 330 };
        name.Inlines.Add(Q(zone, 15, FontWeights.Bold, "Ink"));
        if (kind.Length > 0) name.Inlines.Add(Q(" · " + kind, 12, FontWeights.Medium, "Muted"));
        nameRow.Children.Add(name);
        if (pickPlace != null)
        {
            var chev = QIcon("Chev", 12, "Faint", 2); chev.Margin = new Thickness(7, 2, 0, 0); chev.VerticalAlignment = VerticalAlignment.Center; chev.RenderTransformOrigin = new Point(0.5, 0.5); chev.RenderTransform = new RotateTransform(90);
            nameRow.Children.Add(chev);
            kick.Cursor = Cursors.Hand; kick.ToolTip = "Click to pick another place: its latest run, what it paid out, its best drops."; ToolTipService.SetInitialShowDelay(kick, 500);
            kick.MouseLeftButtonUp += (_, e) => { e.Handled = true; ShowMenu(kick, pickPlace()); };
        }
        titles.Children.Add(nameRow);
        int number = runs.OrderBy(r => r.Start).ToList().IndexOf(run) + 1;
        var local = run.Start.ToLocalTime();
        string when = local.Date == DateTime.Today ? local.ToString("HH:mm", CultureInfo.InvariantCulture) : local.ToString("MMM d HH:mm", CultureInfo.InvariantCulture);
        bool isOpen = ReferenceEquals(run, open);
        string line = isOpen ? $"Run {number} · open since {when} · {RunPillText(run).ToLowerInvariant()}" : $"Run {number} · {when} · took {Clock(TimeSpan.FromSeconds(run.Seconds))}";
        var ml = QT(line, 12, FontWeights.Medium, "Muted", trim: true); ml.Margin = new Thickness(0, 1, 0, 0); ml.Tag = "latest-line"; titles.Children.Add(ml);
        Grid.SetColumn(titles, 1); kick.Children.Add(titles);
        left.Children.Add(kick);

        // what it made, big in gold (a flagged run says why it is out of the place's figures)
        long value = RunValue(run);
        bool known = value > 0 || run.Kinah > 0;
        var big = new TextBlock { Margin = new Thickness(0, 10, 0, 0), TextTrimming = TextTrimming.CharacterEllipsis };
        if (known) { big.Inlines.Add(Q(Fk(value), 40, FontWeights.Bold, "Gold")); big.Inlines.Add(Q(isOpen ? " so far" : " this run", 13, FontWeights.Medium, "Muted")); }
        else big.Inlines.Add(Q(isOpen ? "Nothing yet" : "no prices yet", 22, FontWeights.Bold, "Muted"));
        big.ToolTip = "Kinah plus materials at the lowest Market price, " + PriceBook.Cut + ". Gear is bound and not in the value.";
        left.Children.Add(big);
        var why = FlagReason(run, Median(counted.Select(r => r.Seconds)));
        // the figures line: its Kinah, its loot, its kills
        var parts = new List<(string Fig, string Word)>();
        if (run.Kinah > 0) parts.Add((Fk(run.Kinah), "Kinah"));
        if (value - run.Kinah > 0) parts.Add((Fk(value - run.Kinah), "loot"));
        if (run.Kills > 0) parts.Add((run.Kills.ToString("N0", CultureInfo.InvariantCulture), run.Kills == 1 ? "kill" : "kills"));
        if (parts.Count > 0 || why.Length > 0)
        {
            var fl = new TextBlock { FontSize = 12, Margin = new Thickness(0, 8, 0, 0), TextTrimming = TextTrimming.CharacterEllipsis };
            for (int i = 0; i < parts.Count; i++)
            {
                if (i > 0) fl.Inlines.Add(Q("   ·   ", 12, FontWeights.Bold, "Faint"));
                fl.Inlines.Add(Q(parts[i].Fig, 13, FontWeights.SemiBold, "Ink"));
                fl.Inlines.Add(Q(" " + parts[i].Word, 12, FontWeights.Medium, "Muted"));
            }
            if (why.Length > 0) { if (parts.Count > 0) fl.Inlines.Add(Q("   ·   ", 12, FontWeights.Bold, "Faint")); fl.Inlines.Add(Q((why.StartsWith("Kinah", StringComparison.Ordinal) ? why : char.ToLowerInvariant(why[0]) + why[1..]) + ", out of the figures", 12, FontWeights.SemiBold, "Coral")); }
            left.Children.Add(fl);
        }
        g.Children.Add(left);

        // the corner: the switch, and under it the ring against the best run here
        var right = new StackPanel { HorizontalAlignment = HorizontalAlignment.Right };
        right.Children.Add(RunsViewSwitch(redraw));
        long best = counted.Count > 0 ? counted.Max(RunValue) : 0;
        if (known && !isOpen && counted.Count > 1 && best > 0 && !run.OutOfAverages)
        {
            bool isBest = value >= best;
            var ring = isBest ? QRing(76, 7, 1, "Gold", "★", "best run") : QRing(76, 7, QShare(value, best), "Gold", $"{Math.Round(100.0 * value / best):0}%", "of best");
            ring.ToolTip = $"This run, {Fk(value)}, against the best run here, {Fk(best)}.";
            ring.HorizontalAlignment = HorizontalAlignment.Right; ring.Margin = new Thickness(0, 10, 0, 0);
            right.Children.Add(ring);
        }
        Grid.SetColumn(right, 2); g.Children.Add(right);

        // its drops: the slots, a row deep, then Show all; its gear apart, bound
        var tr = TodayLoot.Runs(new List<InstanceRun> { run }, _items, IsCurrency);
        var slots = SortSlots(tr.Items).Select(s => RunItemSlot(s, "this run")).ToList();
        var outer = new StackPanel();
        outer.Children.Add(g);
        if (slots.Count > 0)
        {
            string key = "latest:" + zone;
            bool all = _todayAll.Contains(key);
            var wrap = new WrapPanel { Margin = new Thickness(0, 12, -6, 0) };
            foreach (var s in all ? slots : slots.Take(RunSlotsPerRow)) wrap.Children.Add(s);
            outer.Children.Add(wrap);
            if (slots.Count > RunSlotsPerRow)
            {
                var m = QLinkify(QT(all ? "Show fewer" : $"Show all {slots.Count}", 12, FontWeights.SemiBold, "Link"));
                m.HorizontalAlignment = HorizontalAlignment.Left; m.Margin = new Thickness(0, 2, 0, 0);
                m.MouseLeftButtonUp += (_, _) => { if (!_todayAll.Remove(key)) _todayAll.Add(key); _drawnSig.Remove("runs"); redraw(); };
                outer.Children.Add(m);
            }
        }
        else if (!isOpen)
        {
            var t = QT(run.Kinah > 0 ? "Kinah only: nothing else read from its reward screen." : "Nothing read from its reward screen.", 12, FontWeights.Medium, "Faint");
            t.Margin = new Thickness(0, 10, 0, 0); outer.Children.Add(t);
        }
        if (GearRow(tr, "this run", slots: false) is FrameworkElement gn) { gn.Margin = new Thickness(0, slots.Count == 0 ? 10 : 6, 0, 0); outer.Children.Add(gn); }
        card.Child = outer;
        var bestDrop = RunsBestDrop(new[] { run });
        HeroArt(card, bestDrop.Length > 0 ? bestDrop[..(bestDrop.LastIndexOf(" ×") is int x and > 0 ? x : bestDrop.Length)] : null);
        return card;
    }

    /// <summary>One item over the place's counted runs: how many, what they sell for, when it last dropped.</summary>
    sealed record BestDrop(long Id, string Name, long Count, long Value, bool Priced, bool Gear, DateTime Last);

    List<BestDrop> BestDrops(List<InstanceRun> counted)
    {
        var by = new Dictionary<long, (long Count, DateTime Last)>();
        void Add(long id, long n, DateTime at)
        {
            if (id <= 0 || n <= 0 || IsCurrency(id)) return;
            by[id] = by.TryGetValue(id, out var x) ? (x.Count + n, at > x.Last ? at : x.Last) : (n, at);
        }
        foreach (var r in counted)
        {
            var at = r.LastLoot ?? r.Start;
            foreach (var (id, n) in r.Items) Add(id, n, at);
            foreach (var (id, n) in r.BoundItems) Add(id, n, at);
        }
        var list = new List<BestDrop>();
        foreach (var (id, x) in by)
        {
            var nm = _items.NameOf(id);
            bool gear = _items.IsGear(id);
            long? price = gear ? null : PriceBook.Price(nm);
            list.Add(new BestDrop(id, nm, x.Count, price is long p ? (long)Math.Round(p * x.Count * PriceBook.SellerKeeps) : 0, price != null, gear, x.Last));
        }
        return SortBestDrops(list, _bestDropsSort);
    }

    /// <summary>Best drops in an order: by value (priced first, then the rest, gear last), count, name, or when it last dropped.</summary>
    static List<BestDrop> SortBestDrops(List<BestDrop> l, string by) => by switch
    {
        "count" => l.OrderByDescending(d => d.Count).ThenBy(d => d.Name, StringComparer.OrdinalIgnoreCase).ToList(),
        "name" => l.OrderBy(d => d.Name, StringComparer.OrdinalIgnoreCase).ToList(),
        "when" => l.OrderByDescending(d => d.Last).ThenByDescending(d => d.Value).ToList(),
        _ => l.OrderByDescending(d => d.Priced).ThenBy(d => d.Gear).ThenByDescending(d => d.Value).ThenByDescending(d => d.Count).ToList(),
    };

    /// <summary>
    /// Best drops: every item the counted runs here brought, folded under its head until a click opens it; open, the sort
    /// (Value, Count, Name, When) and the first ten rows (icon, name, how many and when it last dropped, what they sell for),
    /// then Show all.
    /// </summary>
    Border? QBestDrops(string zone, List<InstanceRun> counted, Action redraw)
    {
        var drops = BestDrops(counted);
        if (drops.Count == 0) return null;
        string openKey = zone + "|best";
        bool open = _runsOpen.Contains(openKey);
        var sp = new StackPanel();
        var card = new Border { Child = sp, Margin = new Thickness(0, 8, 0, 0), Tag = "best-drops" };
        card.SetResourceReference(StyleProperty, "QCard");
        var chev = QIcon("Chev", 12, "Faint", 2); QSetChev(chev, open); chev.Margin = new Thickness(12, 0, 2, 0);
        int n = counted.Count;
        var head = QHead("Trophy", "Best drops", $"{drops.Count} kind{(drops.Count == 1 ? "" : "s")} · {(n == 1 ? "from the run here" : $"from the {n} runs here")}", chev);
        head.ToolTip = open ? "Click to fold." : "Click to see every item the runs here brought, sorted as you like.";
        head.MouseLeftButtonUp += (_, e) => { e.Handled = true; if (!_runsOpen.Remove(openKey)) _runsOpen.Add(openKey); _drawnSig.Remove("runs"); redraw(); };
        sp.Children.Add(head);
        if (!open) return card;

        // the sort: four small pills, the chosen one lit
        var sort = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 6, 0, 4) };
        var sl = QT("Sort", 11.5, FontWeights.Medium, "Faint"); sl.Margin = new Thickness(0, 0, 6, 0); sl.VerticalAlignment = VerticalAlignment.Center; sort.Children.Add(sl);
        foreach (var (key, text) in new[] { ("value", "Value"), ("count", "Count"), ("name", "Name"), ("when", "When") })
        {
            var b = new Button { Content = text, Height = 22, FontSize = 11.5, Padding = new Thickness(8, 0, 8, 0), Margin = new Thickness(2, 0, 0, 0), Tag = "best-sort:" + key };
            b.SetResourceReference(StyleProperty, _bestDropsSort == key ? "SegOn" : "SegOff");
            var k = key;
            b.Click += (_, _) => { if (_bestDropsSort == k) return; _bestDropsSort = k; _drawnSig.Remove("runs"); redraw(); };
            sort.Children.Add(b);
        }
        sp.Children.Add(sort);
        string allKey = "best:" + zone;
        bool all = _todayAll.Contains(allKey);
        int i = 0;
        foreach (var d in all ? drops : drops.Take(BestDropsShown))
        {
            var local = d.Last.ToLocalTime();
            string when = local.Date == DateTime.Today ? local.ToString("HH:mm", CultureInfo.InvariantCulture) : local.ToString("MMM d", CultureInfo.InvariantCulture);
            string val = d.Gear ? "bound" : d.Priced ? Fk(d.Value) : "no price";
            var row = QRow(QItemIcon(d.Name, d.Id, 28, round: false, grade: _items.Get(d.Id)?.Grade ?? ""), d.Name, $"×{d.Count.ToString("N0", CultureInfo.InvariantCulture)} · last {when}", val, d.Priced && !d.Gear ? "Gold" : "Faint", rule: i > 0);
            row.Tag = "best-row:" + d.Name;
            sp.Children.Add(row); i++;
        }
        if (drops.Count > BestDropsShown)
        {
            var m = QLinkify(QT(all ? "Show fewer" : $"Show all {drops.Count}", 12, FontWeights.SemiBold, "Link"));
            m.HorizontalAlignment = HorizontalAlignment.Left; m.Margin = new Thickness(0, 6, 0, 0);
            m.MouseLeftButtonUp += (_, _) => { if (!_todayAll.Remove(allKey)) _todayAll.Add(allKey); _drawnSig.Remove("runs"); redraw(); };
            sp.Children.Add(m);
        }
        return card;
    }

    /* ---------------- for the options self-test and the renders ---------------- */

    internal string RunsViewForTest { get => _runsView; set { _runsView = value; _drawnSig.Remove("runs"); ShowInstances(); } }
    internal bool LatestRunShownForTest => InstHost.Children.OfType<Border>().SelectMany(b => LogicalChildren<Border>(b)).Any(b => b.Tag as string == "latest-run");
    /// <summary>Best drops opened or folded and sorted (for a render or a check), the page drawn again.</summary>
    internal void BestDropsForTest(bool open, string sort)
    {
        var zone = _placesShown.Count > 0 ? PickedPlace(_placesShown, _demoInst != null ? _demoLive : _watcher.Chat?.Instances?.InProgress) : "";
        if (open) _runsOpen.Add(zone + "|best"); else _runsOpen.Remove(zone + "|best");
        _bestDropsSort = sort;
        _drawnSig.Remove("runs"); ShowInstances();
    }
    /// <summary>Best drops' rows as drawn, in order (empty while folded).</summary>
    internal List<string> BestDropRowsForTest() =>
        InstHost.Children.OfType<Border>().SelectMany(b => LogicalChildren<Border>(b)).Where(b => b.Tag is string t && t.StartsWith("best-row:", StringComparison.Ordinal)).Select(b => ((string)b.Tag)[9..]).ToList();
    internal bool BestDropsCardShownForTest => InstHost.Children.OfType<Border>().SelectMany(b => LogicalChildren<Border>(b)).Any(b => b.Tag as string == "best-drops");
    /// <summary>The drops Best drops would list for the shown place, in an order (what the check compares the rows with).</summary>
    internal List<string> BestDropsExpectedForTest(string sort)
    {
        var log = _watcher.Chat?.Instances ?? _demoInst;
        var all = log?.Snapshot() ?? new List<InstanceRun>();
        RunFixes.ApplyKept(all);
        var zone = _placesShown.Count > 0 ? PickedPlace(_placesShown, _demoInst != null ? _demoLive : log?.InProgress) : "";
        var counted = all.Where(r => string.Equals(r.Zone, zone, StringComparison.OrdinalIgnoreCase) && !r.OutOfAverages).ToList();
        var was = _bestDropsSort; _bestDropsSort = sort;
        var l = BestDrops(counted).Select(d => d.Name).ToList();
        _bestDropsSort = was;
        return l;
    }

    static IEnumerable<T> LogicalChildren<T>(DependencyObject d) where T : DependencyObject
    {
        foreach (var c in LogicalTreeHelper.GetChildren(d).OfType<DependencyObject>())
        {
            if (c is T t) yield return t;
            foreach (var x in LogicalChildren<T>(c)) yield return x;
        }
    }
}

public partial class MainWindow
{
    /// <summary>
    /// The place's runs, folded to one line ("Runs · 11 here") until a click opens the last three (QRunsCard); a run's
    /// sheet being open keeps it open. A click on the open card's head folds it again.
    /// </summary>
    Border QRunsFold(string zone, List<InstanceRun> runs, InstanceRun? open, double med, Action redraw)
    {
        string key = zone + "|runs";
        bool isOpen = _runsOpen.Contains(key) || _fixOpen != null;
        void Flip() { if (!_runsOpen.Remove(key)) _runsOpen.Add(key); _drawnSig.Remove("runs"); redraw(); }
        var chev = QIcon("Chev", 12, "Faint", 2); QSetChev(chev, isOpen); chev.Margin = new Thickness(12, 0, 2, 0);
        if (isOpen)
        {
            var card = QRunsCard(runs, open, med, redraw);
            if (card.Child is StackPanel sp && sp.Children.Count > 0 && sp.Children[0] is Grid head)
            {
                Grid.SetColumn(chev, 3); head.Children.Add(chev); head.Cursor = Cursors.Hand;
                head.MouseLeftButtonUp += (_, e) => { e.Handled = true; Flip(); };
            }
            return card;
        }
        var placeIcon = runs.FirstOrDefault() is { } r0 ? PlaceIcon(r0.Zone, r0.Kind) : "Timer";
        var h = QHead(placeIcon, "Runs", $"{runs.Count} here · newest first", chev);
        h.ToolTip = "Click to see the runs here, newest first: a click on one opens its details.";
        h.MouseLeftButtonUp += (_, e) => { e.Handled = true; Flip(); };
        var c = new Border { Child = h, Margin = new Thickness(0, 8, 0, 0), Tag = "runs-fold" };
        c.SetResourceReference(StyleProperty, "QCard");
        return c;
    }
}
