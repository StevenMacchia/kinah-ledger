using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using KinahLedger.Data;
using KinahLedger.Session;

namespace KinahLedger.Ui;

/// <summary>
/// The Expeditions and events page in the Quest look (Steven, 2026-11-07, step 4): the place's box is a column of cards.
/// The hero names the place (its band's dropdown kept) and makes the open run a timer: the ring and the chase bar count
/// it against the average run here, so "almost paid" reads without a number; between runs they hold the last run against
/// the best. Three chips say what the counted runs paid out: Kinah, materials, bound gear. A flagged run's question is a
/// lilac card with the sheet it had. Runs is the last three on their lines, the rest behind "earlier runs" (the every-run
/// box from the ⋯ menu). Paid out here is the loot as 46 px slots with count badges, two rows and Show all. Every figure
/// and every string the figures check reads (tools/ui-review.md) is the same text as before, in a text block of its own.
/// </summary>
public partial class MainWindow
{
    /// <summary>Slots a row in Paid out here: 46 px slots with a 6 px gap in the 660 column (the figures check draws the same two rows).</summary>
    internal const int RunSlotsPerRow = 12;

    // the open run's live pieces: a tick moves them in place (ShowInstances's early return), nothing is rebuilt
    Grid? _liveRingHost; TextBlock? _liveChase; TextBlock? _liveToGo; Border? _liveFill; FrameworkElement? _liveTrack;
    double _liveAvg; int _liveK;

    /// <summary>The icon for a place's kind: a door for an expedition, a star for an event, a bolt for Transcendence, swords for the arena.</summary>
    static string KindIcon(string kind) => kind switch { "Event" => "Star", InstanceLog.Transcendence => "Bolt", "Arena" => "Sword", _ => "Door" };

    /// <summary>A small rounded label: a dot and the words, gold while a run is open, faint after ("Paid · 5:48 in", "last 18:02"). Its text is the figures check's.</summary>
    static Border QPill(string text, bool on)
    {
        var sp = new StackPanel { Orientation = Orientation.Horizontal };
        var dot = new Border { Width = 7, Height = 7, CornerRadius = new CornerRadius(3.5), Margin = new Thickness(0, 1, 7, 0), VerticalAlignment = VerticalAlignment.Center };
        dot.SetResourceReference(Border.BackgroundProperty, on ? "Gold" : "Faint");
        if (on) dot.Effect = QGlow("Gold", 6, 0.7);
        sp.Children.Add(dot);
        sp.Children.Add(QT(text, 11.5, FontWeights.SemiBold, on ? "Gold" : "Muted"));
        var b = new Border { Child = sp, CornerRadius = new CornerRadius(8), BorderThickness = new Thickness(1), Padding = new Thickness(9, 3, 10, 4), VerticalAlignment = VerticalAlignment.Center };
        b.SetResourceReference(Border.BorderBrushProperty, on ? "GoldLine" : "CardLineHover");
        b.SetResourceReference(Border.BackgroundProperty, on ? "GoldTint" : "Card2");
        return b;
    }

    /// <summary>A bar whose fill a tick can move: the track on the window's background, the fill in the tone with its glow.</summary>
    static (Grid Bar, Border Fill) QLiveBar(string tone, double height)
    {
        var g = new Grid { Height = height };
        var track = new Border { CornerRadius = new CornerRadius(height / 2) };
        track.SetResourceReference(Border.BackgroundProperty, "Bg");
        g.Children.Add(track);
        var fill = new Border { CornerRadius = new CornerRadius(height / 2), HorizontalAlignment = HorizontalAlignment.Left, Width = 0, Effect = QGlow(tone, 8, 0.6) };
        fill.SetResourceReference(Border.BackgroundProperty, tone);
        fill.Child = new Border { CornerRadius = new CornerRadius(height / 2), Margin = new Thickness(1, 1, 1, 0), Background = new LinearGradientBrush(Color.FromArgb(0x55, 255, 255, 255), Color.FromArgb(0, 255, 255, 255), 90) };
        g.Children.Add(fill);
        return (g, fill);
    }

    /// <summary>What the counted runs paid out, for the chips: the Kinah, the materials' worth and kinds, the bound gear's pieces, the slots not named, the currencies.</summary>
    (long Kinah, long Mats, int MatKinds, int Gear, int Unread, int Curr) RunLootSums(List<InstanceRun> counted)
    {
        long kinah = 0, mats = 0; var kinds = new HashSet<long>(); int gear = 0, unread = 0; var curr = new HashSet<long>();
        foreach (var r in counted)
        {
            kinah += r.Kinah; unread += r.SlotsUnread; gear += r.UnknownGear;
            foreach (var (id, _) in r.Currencies(IsCurrency)) curr.Add(id);
            foreach (var (id, c) in r.Items.Where(x => x.Value > 0))
            {
                if (IsCurrency(id)) continue;
                if (_items.IsGear(id)) { gear += c; continue; }
                kinds.Add(id);
                if (PriceBook.Price(_items.NameOf(id)) is long p) mats += (long)Math.Round(p * c * PriceBook.SellerKeeps);
            }
            foreach (var (id, c) in r.BoundItems.Where(x => x.Value > 0))
            {
                if (IsCurrency(id)) continue;
                if (_items.IsGear(id)) { gear += c; continue; }
                kinds.Add(id);
            }
        }
        return (kinah, mats, kinds.Count, gear, unread, curr.Count);
    }

    /// <summary>
    /// The hero: the place and its kind (a dropdown of the places when there are several), the band's line under it, the
    /// run's pill on the right; the hour big in gold; the chase (the open run against the average run here, or the last
    /// run against the best); the figures line; and the ring.
    /// </summary>
    Border QRunHero(string zone, List<InstanceRun> runs, List<InstanceRun> counted, bool live, string kind, InstanceRun? open, InstanceRun? newest, Func<List<MenuEntry>>? pickPlace, Action redraw)
    {
        int n = counted.Count;
        double secs = counted.Sum(r => r.Seconds);
        long value = counted.Sum(RunValue);
        bool known = n > 0 && (value > 0 || counted.Any(r => r.Kinah > 0));
        double avg = n > 0 ? secs / n : 0;
        long best = n > 0 ? counted.Max(RunValue) : 0;

        var card = new Border { Padding = new Thickness(16, 12, 16, 12) };
        card.SetResourceReference(StyleProperty, "QCard");
        card.SetResourceReference(Border.BackgroundProperty, "HeroBg");
        var g = new Grid();
        g.ColumnDefinitions.Add(new ColumnDefinition()); g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(14) }); g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var left = new StackPanel();

        // the kicker: the place's icon, its name with its kind, the band's line; the pill on the right
        var kick = new Grid { Background = Brushes.Transparent };
        kick.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); kick.ColumnDefinitions.Add(new ColumnDefinition()); kick.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var ic = QIconBox(KindIcon(kind), "Sky", 36, round: false, iconSize: 19); ic.VerticalAlignment = VerticalAlignment.Center; kick.Children.Add(ic);
        var titles = new StackPanel { Margin = new Thickness(10, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        var nameRow = new Grid();
        nameRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto, MaxWidth = 400 }); nameRow.ColumnDefinitions.Add(new ColumnDefinition());
        var name = new TextBlock { TextTrimming = TextTrimming.CharacterEllipsis };
        name.Inlines.Add(Q(zone, 15, FontWeights.Bold, "Ink"));
        if (kind.Length > 0) name.Inlines.Add(Q(" · " + kind, 12, FontWeights.Medium, "Muted"));
        nameRow.Children.Add(name);
        if (pickPlace != null)
        {
            var chev = QIcon("Chev", 12, "Faint", 2); chev.Margin = new Thickness(7, 2, 0, 0); chev.VerticalAlignment = VerticalAlignment.Center; chev.HorizontalAlignment = HorizontalAlignment.Left; chev.RenderTransformOrigin = new Point(0.5, 0.5); chev.RenderTransform = new RotateTransform(90);
            Grid.SetColumn(chev, 1); nameRow.Children.Add(chev);
            kick.Cursor = Cursors.Hand; kick.ToolTip = "Click to pick another place: its figures, its runs and their loot."; ToolTipService.SetInitialShowDelay(kick, 500);
            kick.MouseLeftButtonUp += (_, e) => { e.Handled = true; ShowMenu(kick, pickPlace()); };
        }
        titles.Children.Add(nameRow);
        titles.SizeChanged += (_, _) => name.MaxWidth = Math.Max(60, titles.ActualWidth - (pickPlace != null ? 26 : 2));
        var meta = RunBandLine(runs, open);
        var ml = QT(meta, 11.5, FontWeights.Medium, "Muted", trim: true); ml.Margin = new Thickness(0, 1, 0, 0); titles.Children.Add(ml);
        Grid.SetColumn(titles, 1); kick.Children.Add(titles);
        if (open != null)
        {
            var pill = QPill(RunPillText(open), true);
            pill.ToolTip = "A run is open here. Where it is: Entered, In the run, Reward screen, Paid. Its clock is the ring."; pill.Margin = new Thickness(10, 0, 0, 0);
            Grid.SetColumn(pill, 2); kick.Children.Add(pill);
            if (live && pill.Child is StackPanel ps && ps.Children.Count > 1 && ps.Children[1] is TextBlock pt) { _livePill = pt; _liveOpen = open; }
        }
        else if (newest != null)
        {
            var last = (newest.LastLoot ?? newest.Start).ToLocalTime();
            var pill = QPill(last.Date == DateTime.Today ? $"last {last:HH:mm}" : $"last {last.ToString("MMM d", CultureInfo.InvariantCulture)}", false);
            pill.Margin = new Thickness(10, 0, 0, 0); Grid.SetColumn(pill, 2); kick.Children.Add(pill);
        }
        left.Children.Add(kick);

        // the hour, big in gold; a place that pays nothing priced says so; a short record says what a run pays
        var big = new TextBlock { Margin = new Thickness(0, 10, 0, 0), TextTrimming = TextTrimming.CharacterEllipsis };
        if (n == 0) { big.Inlines.Add(Q(runs.Count == 0 ? "No run here yet." : "No run counted yet.", 15, FontWeights.SemiBold, "Muted")); }
        else if (!known) { big.Inlines.Add(Q("no prices yet", 22, FontWeights.Bold, "Muted")); big.Inlines.Add(Q("  what the rewards sell for isn't known", 11.5, FontWeights.Medium, "Faint")); }
        else if (secs > 10) { big.Inlines.Add(Q(Fk(value / secs * 3600), 40, FontWeights.Bold, "Gold")); big.Inlines.Add(Q(" an hour", 13, FontWeights.Medium, "Muted")); }
        else { big.Inlines.Add(Q(Fk(value / (double)n), 40, FontWeights.Bold, "Gold")); big.Inlines.Add(Q(" a run", 13, FontWeights.Medium, "Muted")); }
        big.ToolTip = "Over the counted runs: Kinah plus materials at the lowest Market price, " + PriceBook.Cut + ". Gear is bound and not in the value. Flagged runs are left out.";
        left.Children.Add(big);

        // the chase: the open run against the average run here (a timer), or the last run against the best
        _liveRingHost = null; _liveChase = null; _liveToGo = null; _liveFill = null; _liveTrack = null; _liveAvg = 0;
        _liveK = open != null ? runs.OrderBy(r => r.Start).ToList().IndexOf(open) + 1 : 0;
        FrameworkElement? ring = null;
        bool bestInFlag = false;
        if (open != null && avg > 0)
        {
            double el = ((open.LastLoot ?? DateTime.UtcNow) - open.Start).TotalSeconds;
            int k = _liveK;
            var row = new Grid { Margin = new Thickness(0, 6, 0, 0) };
            row.ColumnDefinitions.Add(new ColumnDefinition()); row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var chase = QT($"runs here average {Clock(TimeSpan.FromSeconds(avg))}", 11.5, FontWeights.Medium, "Muted", trim: true); row.Children.Add(chase);
            var togo = QT("", 11.5, FontWeights.SemiBold, "Gold"); togo.Margin = new Thickness(10, 0, 0, 0); Grid.SetColumn(togo, 1); row.Children.Add(togo);
            left.Children.Add(row);
            var (bar, fill) = QLiveBar("Gold", 12); bar.Margin = new Thickness(0, 5, 0, 0); left.Children.Add(bar);
            bar.SizeChanged += (_, _) => LayoutLiveRun();
            _liveToGo = togo; _liveFill = fill; _liveTrack = bar; _liveAvg = avg;
            var host = new Grid { VerticalAlignment = VerticalAlignment.Top }; ring = host; _liveRingHost = host;
            TickRun(open, el);   // fills the words, the bar and the ring
        }
        else if (open != null)
        {
            // the first run here: nothing to chase yet, its clock in the ring
            double el = ((open.LastLoot ?? DateTime.UtcNow) - open.Start).TotalSeconds;
            var host = new Grid { VerticalAlignment = VerticalAlignment.Top }; ring = host; _liveRingHost = host;
            TickRun(open, el);
        }
        else if (known && n > 1 && newest != null && !newest.OutOfAverages)
        {
            long lastV = RunValue(newest);
            bool isBest = lastV >= best;
            var row = new Grid { Margin = new Thickness(0, 6, 0, 0) };
            row.ColumnDefinitions.Add(new ColumnDefinition()); row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var chase = QT($"last run {Fk(lastV)}" + (isBest ? " · your best here" : $" · {Math.Round(100.0 * lastV / best):0}% of your best here"), 11.5, FontWeights.Medium, "Muted", trim: true); row.Children.Add(chase);
            var flag = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(10, 0, 0, 0) };
            var fi = QIcon("Flag", 12, isBest ? "Mint" : "Gold", 2); fi.Margin = new Thickness(0, 0, 5, 0); fi.VerticalAlignment = VerticalAlignment.Center; flag.Children.Add(fi);
            flag.Children.Add(QT($"{Fk(best)} best run", 11.5, FontWeights.SemiBold, isBest ? "Mint" : "Gold"));
            Grid.SetColumn(flag, 1); row.Children.Add(flag);
            left.Children.Add(row);
            var bar = QBar(QShare(lastV, best), isBest ? "Mint" : "Gold", 12); bar.Margin = new Thickness(0, 5, 0, 0); left.Children.Add(bar);
            bestInFlag = true;
            ring = isBest ? QRing(76, 7, 1, "Mint", "★", "best run") : QRing(76, 7, QShare(lastV, best), "Gold", $"{Math.Round(100.0 * lastV / best):0}%", "of best");
            ring.ToolTip = $"The last run here, {Fk(lastV)}, against the best, {Fk(best)}.";
        }

        // the figures line: a run (Kinah and loot), how long a run takes, the best run (when the flag doesn't carry it)
        if (n > 0)
        {
            var figs = new List<(string Fig, string Word)>();
            if (known && secs <= 10) { /* the big figure is the run's */ }
            else if (known) figs.Add((Fk(value / (double)n), "a run"));
            figs.Add((Clock(TimeSpan.FromSeconds(avg)), "a run"));
            if (known && n > 1 && !bestInFlag) figs.Add((Fk(best), "best run"));
            var fl = new TextBlock { FontSize = 11.5, Margin = new Thickness(0, 8, 0, 0), TextTrimming = TextTrimming.CharacterEllipsis };
            for (int i = 0; i < figs.Count; i++)
            {
                if (i > 0) fl.Inlines.Add(Q("   ·   ", 11.5, FontWeights.Medium, "Faint"));
                fl.Inlines.Add(Q(figs[i].Fig, 12.5, FontWeights.Bold, "Ink"));
                fl.Inlines.Add(Q(" " + figs[i].Word, 11.5, FontWeights.Medium, "Muted"));
            }
            fl.ToolTip = big.ToolTip;
            left.Children.Add(fl);
        }
        g.Children.Add(left);
        if (ring != null) { ring.VerticalAlignment = VerticalAlignment.Top; ring.Margin = new Thickness(0, 2, 0, 0); Grid.SetColumn(ring, 2); g.Children.Add(ring); }
        card.Child = g;
        return card;
    }

    /// <summary>A run of text in a size, weight and brush, for a block that mixes a figure with its word.</summary>
    static Run Q(string text, double size, FontWeight weight, string brush)
    {
        var r = new Run(text) { FontSize = size, FontWeight = weight };
        r.SetResourceReference(TextElement.ForegroundProperty, brush);
        if (size >= 30) r.BaselineAlignment = BaselineAlignment.Baseline;
        return r;
    }

    /// <summary>A tick of the open run: the chase words, the bar, the ring and the Runs row move with its clock; the pill moves in ShowInstances.</summary>
    void TickRun(InstanceRun open, double elapsedSecs)
    {
        double share = _liveAvg > 0 ? Math.Clamp(elapsedSecs / _liveAvg, 0, 1) : 0;
        bool past = _liveAvg > 0 && elapsedSecs > _liveAvg + 0.5, at = _liveAvg > 0 && !past && elapsedSecs >= _liveAvg - 0.5;
        var el = Clock(TimeSpan.FromSeconds(elapsedSecs));
        if (_liveToGo != null)
        {
            _liveToGo.Text = past ? "past the average" : at ? "at the average" : $"{Clock(TimeSpan.FromSeconds(_liveAvg - elapsedSecs))} to go";
            _liveToGo.SetResourceReference(TextBlock.ForegroundProperty, past || at ? "Mint" : "Gold");
        }
        if (_liveFill != null) { _liveFill.SetResourceReference(Border.BackgroundProperty, past ? "Mint" : "Gold"); _liveFill.Effect = QGlow(past ? "Mint" : "Gold", 8, 0.6); }
        LayoutLiveRun(share);
        if (_liveRingHost != null)
        {
            _liveRingHost.Children.Clear();
            var ring = QRing(76, 7, share, past ? "Mint" : "Gold", el, $"run {_liveK}");
            ring.ToolTip = _liveAvg <= 0 ? $"Run {_liveK}, {el} in. The first counted run here sets the average to chase." : past ? "This run has gone past the average run here." : $"Run {_liveK}, {el} in; runs here take {Clock(TimeSpan.FromSeconds(_liveAvg))} on average.";
            _liveRingHost.Children.Add(ring);
        }
    }

    double _liveShare;
    void LayoutLiveRun(double? share = null)
    {
        if (share is { } s) _liveShare = s;
        if (_liveFill != null && _liveTrack != null) _liveFill.Width = Math.Max(0, _liveTrack.ActualWidth * _liveShare);
    }

    /// <summary>The three chips: what the counted runs paid out as Kinah, as materials, as bound gear (or the slots not named).</summary>
    FrameworkElement QRunChips(List<InstanceRun> counted, long value)
    {
        var (kinah, mats, matKinds, gear, unread, curr) = RunLootSums(counted);
        var row = new UniformGrid { Columns = 3, Margin = new Thickness(-4, 8, -4, 0) };
        Border Chip(FrameworkElement icon, string big, string small, string tip)
        {
            var b = new Border { Margin = new Thickness(4, 0, 4, 0), ToolTip = tip };
            b.SetResourceReference(StyleProperty, "QChip"); b.Cursor = Cursors.Arrow;
            ToolTipService.SetInitialShowDelay(b, 300);
            QChipFill(b, icon, big, small);
            return b;
        }
        row.Children.Add(Chip(QIconBox("Coin", "Gold"), kinah > 0 ? Fk(kinah) : "–", kinah > 0 && value > 0 ? $"Kinah · {Math.Round(100.0 * kinah / value):0}% of the value" : "Kinah paid out",
            kinah > 0 ? $"{kinah:N0} Kinah paid by the counted runs here." : "No Kinah read from the runs here."));
        row.Children.Add(Chip(QIconBox("Sack", "Mint"), mats > 0 ? Fk(mats) : matKinds > 0 ? "no price" : "–", matKinds > 0 ? $"materials sell for · {matKinds} kind{(matKinds == 1 ? "" : "s")}" : "materials",
            mats > 0 ? $"The materials from the counted runs sell for {Fk(mats)} at the lowest Market price, {PriceBook.Cut}." : "Nothing priced among the materials yet."));
        if (gear > 0 || unread == 0)
            row.Children.Add(Chip(QIconBox("Shield", "Lilac"), gear > 0 ? gear.ToString("N0") : curr > 0 ? curr.ToString("N0") : "–", gear > 0 ? "bound gear · no price" : curr > 0 ? $"currenc{(curr == 1 ? "y" : "ies")}" : "bound gear",
                gear > 0 ? "Gear from an expedition or event is bound to you, so it adds nothing to the value." : curr > 0 ? "Amounts, not pieces: a currency is never sold." : "No gear from the runs here yet."));
        else
            row.Children.Add(Chip(QIconBox("Q", "Coral"), unread.ToString("N0"), $"slot{(unread == 1 ? "" : "s")} not named", "Slots on the reward screens the tracker saw but couldn't name. They aren't guessed, and they aren't in the value."));
        return row;
    }

    /// <summary>A loot slot: the item's icon at 46 px with the count in a badge, the game-tooltip card on hover, pinned with Set a goal on a click.</summary>
    FrameworkElement RunItemSlot(TodaySlot s, string when)
    {
        var slot = QSlot(QItemIcon(s.Name, s.Id, 46, round: false, grade: _items.Get(s.Id)?.Grade ?? ""), CountBadge(s.Count));
        CardTip(slot, () => ItemTodayCard(s, ("", ""), false, true, when));
        if (!s.Currency) PinOnClick(slot, () => ItemTodayCard(s, ("", ""), false, true, when), ("Set a goal for this", () => OpenGoalSheet(null, GoalDraft.ForItem(s.Name), slot)));
        return slot;
    }

    /// <summary>The Kinah's slot: the coin with the amount in its badge, the Kinah card on hover.</summary>
    FrameworkElement RunKinahSlot(long kinah, List<(string Where, long Count)> where, string foot)
    {
        var slot = QSlot(QCoin(46), CountBadge(kinah));
        CardTip(slot, () => KinahTodayCard(kinah, where, "", foot));
        return slot;
    }

    /// <summary>Paid out here: the head with the runs it covers and the worth, the sort links, the slots two rows deep, Show all.</summary>
    Border QPaidOut(string zone, int n, TodayRuns tr, List<TodaySlot> items, Action redraw)
    {
        var sp = new StackPanel();
        var card = new Border { Child = sp, Margin = new Thickness(0, 8, 0, 0) };
        card.SetResourceReference(StyleProperty, "QCard");
        var head = QHead("Sack", "Paid out here", n == 0 ? "" : n == 1 ? "from the run here" : $"from the {n} runs here");
        if (tr.Value > 0)
        {
            var v = QT(Fk(tr.Value), 14, FontWeights.Bold, "Gold"); v.Margin = new Thickness(10, 0, 0, 0); v.VerticalAlignment = VerticalAlignment.Center;
            v.ToolTip = "Kinah plus loot at the lowest Market price, " + PriceBook.Cut + ". Bound items and items with no price yet add nothing.";
            Grid.SetColumn(v, 3); head.Children.Add(v);
        }
        sp.Children.Add(head);
        var slots = new List<FrameworkElement>();
        foreach (var s in items) slots.Add(RunItemSlot(s, "here"));   // the Kinah is the first chip's (2026-11-07 review: the same figure twice)
        if (items.Count >= 2) { var links = SortLinks("runs", redraw); links.HorizontalAlignment = HorizontalAlignment.Right; links.Margin = new Thickness(0, 2, 0, 0); sp.Children.Add(links); }
        string key = "place:" + zone;
        bool all = _todayAll.Contains(key);
        int max = RunSlotsPerRow * TodayRows;
        var wrap = new WrapPanel { Margin = new Thickness(0, 8, -6, 0) };
        foreach (var s in all ? slots : slots.Take(max)) wrap.Children.Add(s);
        sp.Children.Add(wrap);
        if (slots.Count > max)
        {
            var m = QT(all ? "Show fewer" : $"Show all {slots.Count}", 11.5, FontWeights.SemiBold, "Gold");
            m.Cursor = Cursors.Hand; m.Background = Brushes.Transparent; m.HorizontalAlignment = HorizontalAlignment.Left; m.Margin = new Thickness(0, 2, 0, 0);
            m.MouseLeftButtonUp += (_, _) => { if (!_todayAll.Remove(key)) _todayAll.Add(key); redraw(); };
            sp.Children.Add(m);
        }
        if (slots.Count == 0) { var t = QT(n == 0 ? "The rewards show here once a run is counted." : tr.Kinah > 0 ? "Kinah only: nothing else read from the reward screens yet." : "Nothing read from the reward screens yet.", 12, FontWeights.Medium, "Faint"); t.Margin = new Thickness(0, 8, 0, 2); sp.Children.Add(t); }
        return card;
    }

    /// <summary>
    /// Runs: the last three on their lines (when, how long, what they brought, what they paid), the open one first with
    /// its clock; a click opens the run's sheet in the box; the rest behind "N earlier runs", which opens the every-run box.
    /// </summary>
    Border QRunsCard(List<InstanceRun> runs, InstanceRun? open, double med, Action redraw)
    {
        var sp = new StackPanel();
        var card = new Border { Child = sp, Margin = new Thickness(0, 8, 0, 0) };
        card.SetResourceReference(StyleProperty, "QCard");
        sp.Children.Add(QHead("Timer", "Runs", "newest first"));
        var order = runs.OrderByDescending(r => ReferenceEquals(r, open)).ThenByDescending(r => r.LastLoot ?? r.Start).ToList();
        int i = 0;
        foreach (var r in order.Take(3))
        {
            bool isOpen = ReferenceEquals(r, open);
            var why = FlagReason(r, med);
            var local = r.Start.ToLocalTime();
            var when = local.Date == DateTime.Today ? local.ToString("HH:mm", CultureInfo.InvariantCulture) : local.ToString("MMM d HH:mm", CultureInfo.InvariantCulture);
            string title = isOpen ? $"Run {_liveK} · open" : $"{when} · took {Clock(TimeSpan.FromSeconds(r.Seconds))}";
            string sub = isOpen ? $"since {when} · {PiecesText(r)}" : RunSub(r, why);
            long v = RunValue(r);
            var icon = isOpen ? QIconBox("Timer", "Gold", 28, round: true) : why.Length > 0 ? QIconBox("Q", "Coral", 28, round: true) : QIconBox(KindIcon(r.Kind), "Sky", 28, round: true);
            var row = QRow(icon, title, sub, r.OutOfAverages || v <= 0 ? "–" : Fk(v), r.OutOfAverages || v <= 0 ? "Faint" : "Gold", rule: i > 0);
            row.ToolTip = why.Length > 0 ? why + " Click for the fix." : "Click for this run's details, or Remove if it's wrong.";
            ToolTipService.SetInitialShowDelay(row, 500);
            var run = r;
            row.MouseLeftButtonUp += (_, e) => { e.Handled = true; _fixOpen = _fixOpen == RunKey(run) ? null : RunKey(run); _drawnSig.Remove("runs"); redraw(); };
            sp.Children.Add(row); i++;
        }
        if (order.Count > 3)
        {
            var m = QT($"{order.Count - 3} earlier run{(order.Count - 3 == 1 ? "" : "s")}  ›", 11.5, FontWeights.SemiBold, "Gold");
            m.Cursor = Cursors.Hand; m.Background = Brushes.Transparent; m.Margin = new Thickness(0, 6, 0, 0);
            m.ToolTip = "Every run on its line, in a box under this one, with what each one gave.";
            m.MouseLeftButtonUp += (_, _) => { _runListShown = true; _drawnSig.Remove("runs"); redraw(); };
            sp.Children.Add(m);
        }
        return card;
    }

    /// <summary>A run's small print: what it brought, and why it is left out when it is, said once ("rewards not read" is both).</summary>
    string RunSub(InstanceRun r, string why)
    {
        var pieces = PiecesText(r);
        if (why.Length == 0) return pieces;
        var w = char.ToLowerInvariant(why[0]) + why[1..];
        return string.Equals(pieces, w, StringComparison.OrdinalIgnoreCase) ? w : $"{pieces} · {w}";
    }

    /// <summary>The runs with something to look at, one line: a click opens the newest one's sheet.</summary>
    Border QFlaggedLine(List<InstanceRun> flagged, double med, Action redraw)
    {
        var card = new Border { Margin = new Thickness(0, 8, 0, 0), Padding = new Thickness(10, 6, 10, 6) };
        card.SetResourceReference(StyleProperty, "QCard"); card.SetResourceReference(Border.BackgroundProperty, "NoticeTint");
        var row = QRow(QIconBox("Q", "Lilac", 28, round: true), $"{flagged.Count} run{(flagged.Count == 1 ? "" : "s")} to look at", FlagReason(flagged[0], med) + " · out of the figures until fixed", "fix  ›", "Lilac", rule: false, inline: true);
        row.ToolTip = FlagReason(flagged[0], med) + " Click for the fix.";
        row.MouseLeftButtonUp += (_, e) => { e.Handled = true; _fixOpen = RunKey(flagged[0]); _drawnSig.Remove("runs"); redraw(); };
        card.Child = row;
        return card;
    }
}
