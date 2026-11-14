using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using KinahLedger.Data;
using KinahLedger.Session;
using Path = System.Windows.Shapes.Path;

namespace KinahLedger.Ui;

/// <summary>
/// The Farming page in the Quest look (Steven, 2026-11-07): three hooks above the fold and nothing else. The chase (the hero,
/// Ui/MainWindow.Live.cs FillRate), Next up (the milestones the pace puts minutes away, with today's goals, Ui/MainWindow.Goals.cs)
/// and the stream of what just dropped. This file holds the chips under the hero, Kinah, loot and kills, each opening what is
/// behind it on a click (Kinah by enemy, the bag, kills by enemy), the milestones, and the one-pixel rules that keep the
/// page calm: three cards at rest, one accent per card, every further fact behind a hover or a click.
/// </summary>
public partial class MainWindow
{
    /// <summary>The chip open under the row: "who" (Kinah by enemy), "bag" (LOOT) or "kills"; null for none.</summary>
    string? _qChip;

    /// <summary>The enemies of the count on screen, for the Who pays and Kills panels (from the last FillNumbers).</summary>
    List<SessionEngine.MobStats> _qMobs = new();
    long _qKinah; int _qKills; TimeSpan _qElapsed;

    /// <summary>Wires the hero's chart, the chips, Next up's head and the chase bar's width, once from the constructor.</summary>
    void InitQuest()
    {
        ChartToggle.MouseLeftButtonUp += (_, _) => { _qOpen["chart"] = !QOpen("chart"); ShowChart(QOpen("chart") && ChartToggle.Visibility == Visibility.Visible); QSetChev(ChartChev, QOpen("chart")); };
        ChaseTrack.SizeChanged += (_, _) => LayoutChase();
        CraftChaseTrack.SizeChanged += (_, _) => LayoutCraftChase();
        GoalsHead.MouseLeftButtonUp += (_, _) => { if (QuestsOn) ShowPart("Quests"); };   // the rest of the quests live on their page (2026-11-08)
        BagStripHead.MouseLeftButtonUp += (_, _) => ToggleChip("bag");
        // the open bag card's head puts it back to the strip in Compact (its scope buttons handle their own clicks)
        SessionHead.MouseLeftButtonUp += (_, _) => { if (!FullShown && _qChip == "bag") ToggleChip("bag"); };
        HeroSpark.MouseLeftButtonUp += (_, e) => { e.Handled = true; _qOpen["chart"] = !QOpen("chart"); ShowChart(QOpen("chart") && ChartToggle.Visibility == Visibility.Visible); QSetChev(ChartChev, QOpen("chart")); };
        HeroSpark.SizeChanged += (_, _) => DrawHeroSpark();
        LowerScroll.ScrollChanged += (_, _) => FadeLowerScroll();
    }

    /// <summary>
    /// The scrolling column's foot fades out while there is more below (colour pass review, 2026-11-13: the Quests page
    /// read as cut off at the bottom, a card chopped by the footer's rule with nothing saying the page goes on); scrolled to
    /// the end, the fade goes and the last card ends with its own margin.
    /// </summary>
    void FadeLowerScroll()
    {
        double h = LowerScroll.ActualHeight;
        bool more = h > 0 && LowerScroll.ScrollableHeight > 1 && LowerScroll.VerticalOffset < LowerScroll.ScrollableHeight - 1;
        if (!more) { LowerScroll.OpacityMask = null; return; }
        var m = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(0, 1) };
        m.GradientStops.Add(new GradientStop(Colors.Black, 0));
        m.GradientStops.Add(new GradientStop(Colors.Black, Math.Clamp(1 - 36 / h, 0, 1)));
        m.GradientStops.Add(new GradientStop(Colors.Transparent, 1));
        m.Freeze();
        LowerScroll.OpacityMask = m;
    }

    /* ---------------- the chase ---------------- */

    double _chaseShare;
    string ChaseKey => "chase|" + _part + "|" + CountWhere();
    void LayoutChase() => ChaseFill.Width = Math.Max(0, ChaseTrack.ActualWidth * _chaseShare);

    /* ---------------- the bag at a glance ---------------- */

    /// <summary>
    /// The Compact page's bag strip (Steven, 2026-11-07): what you have got this session as inventory slots with their counts,
    /// the drops and gathered items by worth, the Soul Crystals last; eleven slots and a "+N" for the rest. The hover card
    /// and the click are the bag's; the head opens the bag card, which takes the strip's place while it is open.
    /// Since the UX pass (2026-11-13) the strip has no Kinah slot and no total: the hero's line says the Kinah and the
    /// loot, and the strip's total was those two added up, the same count a third time.
    /// </summary>
    void FillBagStrip()
    {
        var h = _lootHere;
        // the run card lit by the art of this place's best drop so far, Kinah's before any (v2, Ui/Quest.cs HeroArt)
        HeroArt(SessionCard, h == null ? null : h.S.Drops.Where(r => !r.Header).Concat(h.S.Gathered.Where(r => !r.Header)).OrderByDescending(r => r.Worth).FirstOrDefault()?.Name);
        var drops = h == null ? new() : h.S.Drops.Where(r => !r.Header).Select(r => (Row: r, Gathered: false)).Concat(h.S.Gathered.Where(r => !r.Header).Select(r => (Row: r, Gathered: true)))
            // a coveted find (Poko's Skin Bundle) first: unpriced, it would sort last and hide behind the "+N"
            .OrderByDescending(x => GatheringCatalog.Find(x.Row.Name) is { AnyNode: true }).ThenByDescending(x => x.Row.Worth).ThenByDescending(x => Qty(x.Row.Drops)).ToList();
        var pets = h?.S.Pets ?? new();
        long crystals = pets.Sum(r => Qty(r.Drops));
        int kinds = drops.Count + (crystals > 0 ? 1 : 0);
        // the open bag card stands in for the strip (the same slots twice otherwise); with nothing but Kinah yet, no strip
        bool show = _part == "Farming" && !_welcome && !FullShown && _hasSummary && h != null && _qChip != "bag" && kinds > 0;
        BagStrip.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        if (!show) return;
        BagStripNote.Text = $"{kinds} kind{(kinds == 1 ? "" : "s")}";
        BagStripTotal.Text = ""; BagStripTotal.ToolTip = null;
        QSetChev(BagStripChev, false);
        var sig = $"{crystals}|{string.Join(";", drops.Select(d => $"{d.Row.Name}:{d.Row.Drops}:{d.Row.Grade}:{d.Row.Reported}"))}|{Skins.Current}";
        if (!Changed("bag-strip", sig)) return;
        BagStripHost.Children.Clear();
        const int Max = 11;
        var where = CountWhere();
        // a drop that lands grows in, a rare one glows once (Motion.Slot)
        var landHost = "bag-strip|" + where;
        Motion.Begin(landHost);
        var slots = new List<FrameworkElement>();
        foreach (var (r, gathered) in drops)
        {
            var name = r.Report.Length > 0 ? r.Report : r.Name;
            var s = QSlot(QItemIcon(name, _items.IdOf(name), 42, round: false, grade: r.Grade), CountBadge(Qty(r.Drops)), ("bag-count|" + where + "|" + name, Qty(r.Drops)));
            if (r.Reported) s.Opacity = 0.55;
            CardTip(s, () => ItemHereCard(r, gathered, where));
            if (r.Report.Length > 0) s.MouseLeftButtonUp += (_, _) => ShowItemCard(r, s);
            Motion.Slot(s, landHost, name, Qty(r.Drops), r.Grade);
            slots.Add(s);
        }
        if (crystals > 0) { var c = QSlot(QIconBox("Gem", "Lime", 42), CountBadge(crystals)); Motion.Slot(c, landHost, "soul crystals", crystals); slots.Add(c); }
        // one row: eleven slots, or ten and the "+N"
        foreach (var s in slots.Take(slots.Count > Max ? Max - 1 : slots.Count)) BagStripHost.Children.Add(s);
        if (slots.Count > Max)
        {
            var more = new Border { Width = 42, Height = 42, CornerRadius = new CornerRadius(12), Margin = new Thickness(0, 0, 6, 6), Cursor = Cursors.Hand, ToolTip = "The rest of the bag" };
            more.SetResourceReference(Border.BackgroundProperty, "Card2");
            more.Child = QT($"+{slots.Count - (Max - 1)}", 12, FontWeights.Bold, "Muted");
            more.MouseLeftButtonUp += (_, _) => { if (_qChip != "bag") ToggleChip("bag"); };
            BagStripHost.Children.Add(more);
        }
        Motion.End(landHost);
    }

    /// <summary>
    /// The chase: this hour against your best here (Session/BestRuns.cs), as the bar toward the flag and the ring beside the
    /// figure; gold beaten or not, a star in the ring once it is. With no best yet the ring is empty and says so; while the rate warms up
    /// the bar waits, as the figure does.
    /// </summary>
    void FillChase(double nowHr, double bestHr, bool warm)
    {
        bool has = nowHr > 0 && bestHr > 0 && !warm;
        bool beat = has && nowHr >= bestHr;
        double share = QShare(nowHr, bestHr);
        // gold beaten or not (colour pass, 2026-11-13): the bar and the ring are progress toward a target; the ring's star says it is beaten
        const string tone = "Gold";
        ChaseBox.Visibility = has ? Visibility.Visible : Visibility.Collapsed;
        if (has)
        {
            ChaseLbl.Text = beat ? "Past your best here · a new best if it holds" : $"{Fk(bestHr - nowHr)} an hour to your best here";
            ChaseBest.Text = Fk(bestHr);
            ChaseFill.SetResourceReference(Border.BackgroundProperty, tone);
            ChaseFill.Effect = QGlow(tone, 10, 0.6);
            ChaseBest.SetResourceReference(TextBlock.ForegroundProperty, tone);
            ChaseBox.ToolTip = $"Your best hour in this zone on this server: {Amounts.Full(bestHr)}. This hour: {Amounts.Full(nowHr)}.";
            // the bar slides to its new share (Motion.Slide); keyed by where it counts, so a switch to this zone doesn't slide
            Motion.Slide(ChaseFill, ChaseKey, share, s => { _chaseShare = s; LayoutChase(); });
        }
        else Motion.ForgetShare(ChaseKey);
        var sig = $"{has}|{beat}|{Math.Round(share, 3)}|{nowHr > 0}|{Skins.Current}";
        if (!Changed("ring", sig)) return;
        RingHost.Children.Clear();
        RingHost.Children.Add(has ? QRing(76, 7, share, tone, beat ? "★" : $"{Math.Round(share * 100):0}%", beat ? "new best" : "of best")
            : QRing(76, 7, 0, "Gold", nowHr > 0 ? "–" : "", nowHr > 0 ? "no best yet" : ""));
        RingHost.ToolTip = has ? ChaseBox.ToolTip : nowHr > 0 ? "No best hour here yet: this one sets it." : null;
    }

    /* ---------------- the hero's trend ---------------- */

    /// <summary>The rate line the hero's trend is drawn from (DrawRateLine hands it over).</summary>
    RateLine? _sparkOf;
    bool _sparkHooked;
    const int SparkMinutes = 30;
    const double SparkMaxWidth = 132;

    /// <summary>
    /// The hour's last 30 minutes as a faint line beside the big figure (UX pass, 2026-11-13): a sample a minute from the
    /// same rate line as the chart, its lowest at the bottom and its highest at the top, a soft fill under it and a dot on
    /// now, right-aligned in the room the figure and its unit leave (none under 56 px). Gold at 60%, so it reads as a trend
    /// and never competes with the figure. Shown from three samples; hidden while a new-best or milestone tag is over it.
    /// "chart" still opens the full chart, and so does a click here.
    /// </summary>
    void DrawHeroSpark()
    {
        if (!_sparkHooked && _bestTag != null && _dayTag != null)
        {
            _sparkHooked = true;
            _bestTag.IsVisibleChanged += (_, _) => DrawHeroSpark();
            _dayTag.IsVisibleChanged += (_, _) => DrawHeroSpark();
        }
        var all = _sparkOf?.Samples;
        var rates = all == null ? new List<double>() : all.Skip(Math.Max(0, all.Count - SparkMinutes)).Select(s => s.Rate).ToList();
        HeroSpark.Visibility = rates.Count >= 3 ? Visibility.Visible : Visibility.Collapsed;
        bool tag = _bestTag?.IsVisible == true || _dayTag?.IsVisible == true;
        HeroSpark.Opacity = tag ? 0 : 1;
        double room = HeroSpark.ActualWidth, h = HeroSpark.Height;
        if (rates.Count < 3 || room < 56) { HeroSparkLine.Points = new PointCollection(); HeroSparkFill.Data = null; HeroSparkDot.Visibility = Visibility.Collapsed; return; }
        double w = Math.Min(room, SparkMaxWidth), x0 = room - w;
        double lo = rates.Min(), hi = rates.Max();
        var pts = new PointCollection();
        for (int i = 0; i < rates.Count; i++)
        {
            double y = hi - lo < 1e-9 ? h / 2 : h - 2 - (h - 4) * (rates[i] - lo) / (hi - lo);
            pts.Add(new Point(Math.Round(x0 + w * i / (rates.Count - 1), 1), Math.Round(y, 1)));
        }
        HeroSparkLine.Points = pts;
        var fig = new PathFigure { StartPoint = new Point(pts[0].X, h), IsClosed = true };
        foreach (var p in pts) fig.Segments.Add(new LineSegment(p, false));
        fig.Segments.Add(new LineSegment(new Point(pts[^1].X, h), false));
        HeroSparkFill.Data = new PathGeometry(new[] { fig });
        var c = QColor("Gold");
        var fill = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(0, 1) };
        fill.GradientStops.Add(new GradientStop(Color.FromArgb(Skins.IsDark(Skins.Current) ? (byte)0x30 : (byte)0x26, c.R, c.G, c.B), 0));
        fill.GradientStops.Add(new GradientStop(Color.FromArgb(0, c.R, c.G, c.B), 1));
        fill.Freeze();
        HeroSparkFill.Fill = fill;
        HeroSparkDot.Visibility = Visibility.Visible;
        Canvas.SetLeft(HeroSparkDot, pts[^1].X - HeroSparkDot.Width / 2); Canvas.SetTop(HeroSparkDot, pts[^1].Y - HeroSparkDot.Height / 2);
    }

    /* ---------------- the Abyss's Kinah ticket ---------------- */

    /// <summary>True while the hero's figure is the Abyss Points hour (FillRate), so a test or a render can ask.</summary>
    bool _heroAbyss;
    /// <summary>Which kind of hour the rate line was sampled in: a switch starts the line over.</summary>
    bool _rateOfAbyss;

    /// <summary>
    /// The Kinah ticket under the hero in the Abyss's zones (Steven, 2026-11-13): the hero says the Abyss Points an hour, and
    /// this small gold ticket the Kinah and loot an hour after tax, the hour the hero says everywhere else. Kinah's art, the
    /// figure in gold, the words muted; dimmed while the hour warms up, as the hero is. Hidden outside the Abyss and before
    /// anything is counted.
    /// </summary>
    void FillHeroTicket(bool abyss, long total, double h, int priced, long loot, bool warm)
    {
        bool show = abyss && total > 0 && h > 0;
        HeroTicket.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        if (!show) return;
        string words = priced > 0 || loot > 0 ? "Kinah and loot an hour" : "Kinah an hour";
        HeroTicket.Opacity = warm ? 0.4 : 1;
        HeroTicket.ToolTip = $"{Amounts.Full(total / h)} {words}" + (priced > 0 || loot > 0 ? $", loot at the lowest Market price {PriceBook.Cut}" : "") + ": the Kinah picked up here and what the loot sells for, the hour the big figure says outside the Abyss.";
        var sig = $"{Fk(total / h)}|{words}|{Skins.Current}";
        if (!Changed("ticket", sig)) return;
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        var art = GameArt.Element("kinah", 14); art.VerticalAlignment = VerticalAlignment.Center; art.Margin = new Thickness(0, 0, 5, 0);
        row.Children.Add(art);
        var fig = QT(Fk(total / h), 13, FontWeights.Bold, "Gold"); fig.VerticalAlignment = VerticalAlignment.Center;
        var word = QT(" " + words, 12, FontWeights.Medium, "Muted"); word.VerticalAlignment = VerticalAlignment.Center;
        row.Children.Add(fig); row.Children.Add(word);
        HeroTicket.Child = row;
        HeroTicket.SetResourceReference(Border.BackgroundProperty, QTintOf("Gold"));
    }

    /// <summary>For options-test: the hero's figure and label, and the ticket's words when it shows ("" when not).</summary>
    internal (string Hero, string Label, string Ticket, bool Abyss) HeroForTest() =>
        (HeadRate.Text, HeadRateLbl.Text,
         HeroTicket.Visibility == Visibility.Visible && HeroTicket.Child is StackPanel sp ? string.Concat(sp.Children.OfType<TextBlock>().Select(t => t.Text)) : "", _heroAbyss);

    /* ---------------- the chips ---------------- */

    /// <summary>
    /// The hero's line of figures (UX pass, 2026-11-13; three chips under the hero until then): Kinah, loot at Market prices
    /// and kills, each the one place the page says it (the bag lost its Kinah slot and its total, Who pays its "of 632.3K");
    /// a click opens what is behind it under the hero: Kinah by enemy, the bag, kills by enemy. A gathering stretch says its
    /// attempts, what they gave and the items instead, which would read 0 as Kinah and kills. Also the Just dropped head's note.
    /// </summary>
    void FillChips(SessionEngine e, long kinah, Tables s, int kills, TimeSpan elapsed, long totalNow, int nodes = 0)
    {
        bool farm = _part == "Farming" && !_welcome && _hasSummary;
        HeroStats.Visibility = farm ? Visibility.Visible : Visibility.Collapsed;
        FeedNote.Text = farm ? "last five" : "";
        if (!farm) { ChipPanel.Visibility = Visibility.Collapsed; return; }
        _qMobs = e.MobSnapshot(); _qKinah = kinah; _qKills = kills; _qElapsed = elapsed;
        int kinds = s.Drops.Count(r => !r.Header) + s.Gathered.Count(r => !r.Header) + (s.Pets is { Count: > 0 } ? 1 : 0);
        bool gather = _mode == Mode.Gather;
        long abyss = _heroAbyss && !gather ? _lootHere?.Abyss ?? 0 : 0;
        var hs = _here;
        var sig = $"{kinah}|{s.Value}|{s.Priced}|{kinds}|{kills}|{nodes}|{s.GatherCount}|{gather}|{_qChip}|{FullShown}|{Skins.Current}|{abyss}|{hs}";
        if (!Changed("chips", sig)) return;
        var at = CountWhere();
        // Full view has the bag, Who pays and the odds in its second column already: the figures there open nothing
        Action? Open(string key) => FullShown ? null : () => ToggleChip(key);
        string kindsText = $"{kinds} kind{(kinds == 1 ? "" : "s")}";
        // how Kinah and loot add up (the bag's total said it on hover until it went): on the loot figure's hover
        string total = _lootHere?.TotalTip is { Length: > 0 } tt ? "\n\n" + tt : "";
        var parts = new List<QStat>();
        // the place's share beside each figure (0.10.70), and on its hover
        string Here(long v, bool money) => hs == null ? "" : (money ? Fk(v) : v.ToString("N0")) + " here";
        string In(long v, bool money, string what) => hs == null ? "" : $"\n{(money ? Fk(v) : v.ToString("N0"))} {what} in {hs.Place}.";
        if (gather)
        {
            double perTry = nodes > 0 ? s.GatherCount / (double)nodes : 0;
            parts.Add(new QStat(nodes.ToString("N0"), $"attempt{(nodes == 1 ? "" : "s")}",
                $"{nodes:N0} gathering attempts {at}" + (elapsed.TotalMinutes >= 1 ? $", {Fk(nodes / elapsed.TotalHours)} an hour" : "") + "."));
            parts.Add(new QStat(s.Value > 0 ? Fk(s.Value) : kinds > 0 ? kinds.ToString("N0") : "–", s.Value > 0 ? "gathered" : kinds > 0 ? (kinds == 1 ? "kind gathered" : "kinds gathered") : "gathered",
                kinds == 0 ? "Nothing gathered yet." : $"{kindsText} {at}" + (s.Priced > 0 ? $": {Fk(s.Value)} at the lowest Market price, {PriceBook.Cut}" : "") + (FullShown ? "." : ". Click for the bag."),
                Open("bag"), _qChip == "bag", "Mint", s.Value > 0 ? ("chip-loot|" + at, s.Value, true) : null));
            parts.Add(new QStat(Fk(s.GatherCount), nodes > 0 ? $"items · {perTry:0.0} a try" : "items",
                nodes > 0 ? $"{s.GatherCount:N0} items from {nodes:N0} attempts, {perTry:0.0} a try." : "Items show here as nodes are gathered."));
        }
        else
        {
            double perMin = elapsed.TotalMinutes >= 1 ? kills / elapsed.TotalMinutes : 0;
            // in the Abyss the points the hero is the hour of lead the line (2026-11-13): their one home on the page, as Kinah's is
            if (abyss > 0)
                parts.Add(new QStat(Fk(abyss), "Abyss Points", $"{abyss:N0} Abyss Points picked up {at}" + (kills > 0 ? $", {Fk(abyss / (double)kills)} a kill" : "") + "." + In(hs?.Abyss ?? 0, true, "of them"),
                    null, false, "Sky", ("chip-abyss|" + at, abyss, false), Here(hs?.Abyss ?? 0, true)));
            parts.Add(new QStat(Fk(kinah), "Kinah",
                $"{kinah:N0} Kinah picked up {at}" + (kills > 0 ? $", {Fk(kinah / (double)kills)} a kill" : "") + (_boundInKinah ? ". Bound Kinah is in it: in the Abyss every Kinah drop is bound." : ". Quest rewards and bound Kinah are left out.") + (FullShown ? "" : " Click for Kinah by enemy.") + In(hs?.Kinah ?? 0, true, "of it"),
                Open("who"), _qChip == "who", "Gold", kinah > 0 ? ("chip-kinah|" + at, kinah, true) : null, Here(hs?.Kinah ?? 0, true)));
            parts.Add(new QStat(s.Value > 0 ? Fk(s.Value) : kinds > 0 ? kinds.ToString("N0") : "–", s.Value > 0 || kinds == 0 ? "loot" : (kinds == 1 ? "kind of loot" : "kinds of loot"),
                (kinds == 0 ? "Nothing picked up yet." : $"{kindsText} {at}" + (s.Priced > 0 ? $": {Fk(s.Value)} at the lowest Market price, {PriceBook.Cut}" : "") + (s.Unpriced > 0 ? $" · {s.Unpriced} with no price yet" : "") + ".")
                + (FullShown ? "" : " Click for the bag.") + (s.Value > 0 ? In(hs?.Value ?? 0, true, "of it") : "") + total,
                Open("bag"), _qChip == "bag", "Mint", s.Value > 0 ? ("chip-loot|" + at, s.Value, true) : null, s.Value > 0 ? Here(hs?.Value ?? 0, true) : ""));
            parts.Add(new QStat(kills.ToString("N0"), kills == 1 ? "kill" : "kills",
                kills > 0 ? $"{kills:N0} kills in {TodayLoot.Dur(elapsed.TotalSeconds)}" + (perMin > 0 ? $", {perMin:0.0} a minute" : "") + $", {Fk(kinah / (double)kills)} Kinah a kill." + (FullShown ? "" : " Click for kills by enemy.") : "Kills show here as enemies pay out.",
                Open("kills"), _qChip == "kills", "Coral", kills > 0 ? ("chip-kills|" + at, kills, false) : null, kills > 0 ? Here(hs?.Kills ?? 0, false) : ""));
        }
        QStatsFill(HeroStats, parts);
        FillChipPanel();
    }

    /// <summary>For options-test: the hover text of the hero line's part whose word is <paramref name="word"/>.</summary>
    internal string HeroStatTipForTest(string word) => HeroStats.Children.OfType<Border>()
        .FirstOrDefault(b => b.Tag as string == "stat" && ((StackPanel)b.Child).Children.OfType<TextBlock>().Last(t => t.Tag as string != "here").Text == word)?.ToolTip as string ?? "";

    /// <summary>For options-test: the bag strip's slots as drawn, and the hero's trend (shown, its points).</summary>
    internal (bool Strip, int Slots, bool Spark, int SparkPoints) BagStripForTest() =>
        (BagStrip.Visibility == Visibility.Visible, BagStripHost.Children.Count, HeroSpark.Visibility == Visibility.Visible, HeroSparkLine.Points.Count);

    /// <summary>For options-test: the hero line's parts as drawn, figure and word ("632.3K Kinah").</summary>
    internal List<string> HeroStatsForTest() => HeroStats.Children.OfType<Border>().Where(b => b.Tag as string == "stat")
        .Select(b => string.Join(" ", ((StackPanel)b.Child).Children.OfType<TextBlock>().Where(t => t.Tag as string != "here").Select(t => t.Text))).ToList();

    /// <summary>For options-test: the place's share beside each part of the hero line ("274.4K here"), "" where there is none.</summary>
    internal List<string> HeroHereForTest() => HeroStats.Children.OfType<Border>().Where(b => b.Tag as string == "stat")
        .Select(b => string.Concat(((StackPanel)b.Child).Children.OfType<TextBlock>().Where(t => t.Tag as string == "here").Select(t => t.Text))).ToList();

    /// <summary>For options-test: a chip's click.</summary>
    internal void ToggleChipForTest(string key) => ToggleChip(key);

    string CountWhere() => RunShown ? "this run" : "since your last break";

    void ToggleChip(string key)
    {
        if (_mode == Mode.Gather && key != "bag") return;   // a gathering stretch has no enemies behind its chips
        if (FullShown) return;   // Full view has the bag, Who pays and the odds in its second column already
        _qChip = _qChip == key ? null : key;
        _drawnSig.Remove("chips"); _drawnSig.Remove("loot");
        Redraw();
    }

    /// <summary>What a chip opens under the row: Kinah by enemy or kills by enemy as rows with bars; the bag is LOOT's card (FillLoot shows it).</summary>
    void FillChipPanel()
    {
        bool panel = _qChip is "who" or "kills" && _mode != Mode.Gather && !FullShown;
        ChipPanel.Visibility = panel ? Visibility.Visible : Visibility.Collapsed;
        if (!panel) return;
        if (_qChip == "who") BuildWho(ChipPanelHost); else BuildKills(ChipPanelHost);   // Ui/MainWindow.QuestFull.cs
    }

    /* ---------------- Next up's figures ---------------- */
    // The milestones' own arithmetic (0.10.23) moved into the quest engine (Session/Quests.cs, 2026-11-08); the rows are
    // Ui/MainWindow.Quests.cs. NextRound stays for the Crafting page's profit row, NextStack for the slots' round counts.

    /// <summary>The next round Kinah figure above <paramref name="v"/>: 50K steps to 250K, 250K to 1M, 500K to 5M, then 1M.</summary>
    static long NextRound(long v)
    {
        long step = v < 250_000 ? 50_000 : v < 1_000_000 ? 250_000 : v < 5_000_000 ? 500_000 : 1_000_000;
        return (v / step + 1) * step;
    }

    /// <summary>The next round count above <paramref name="n"/>: 5, 10, 20, 30, 50, 100, 150, 200, 300, 500, 1,000, then thousands.</summary>
    static long NextStack(long n)
    {
        foreach (var m in new long[] { 5, 10, 20, 30, 50, 100, 150, 200, 300, 500, 1000 }) if (m > n) return m;
        long step = n < 10_000 ? 1_000 : 10_000;
        return (n / step + 1) * step;
    }

    static string Eta(double secs) => secs < 60 ? "under a minute" : "about " + TodayLoot.Dur(secs);

    /// <summary>A goal as a Next up row: the item, coin or door, the goal, have and to go with the pace, the share; the card on hover, the sheet on a click.</summary>
    FrameworkElement GoalRow(Goal g, GoalProgress p, bool rule)
    {
        FrameworkElement icon = g.Kind switch
        {
            Goal.KindItem => QItemIcon(g.Name, _items.IdOf(g.Name), 28, round: true, grade: GradeOf(g.Name)),
            Goal.KindRuns => QIconBox("Door", "Sky", 28, round: true),
            _ => QCoin(28),
        };
        var pace = Goals.PaceText(p);
        var sub = p.Done ? "reached today ✓" : $"{GoalAmount(g, p.Have)} so far · {GoalAmount(g, p.ToGo)} to go" + (pace.Length > 0 ? " · " + pace : "");
        // the bar in the colour of what it tracks (colour pass, 2026-11-13): a Kinah goal gold, an item or runs the Progress
        // tone; the ✓ of a reached goal green, a success
        var tone = g.Kind is Goal.KindItem or Goal.KindRuns ? "Progress" : "Gold";
        var key = $"goal|{g.Kind}|{g.Name}|{g.Which}|{g.Target}";
        var row = QRow(icon, Goals.Label(g), sub, p.Done ? "✓" : $"{Math.Round(p.Fraction * 100):0}%", p.Done ? "Good" : tone == "Gold" ? "Gold" : "Ink", p.Fraction, tone, rule, inline: true, barKey: key);
        Motion.Reached(row, key, p.Done, tone == "Gold" ? "Gold" : "Good");   // the draw where it is reached washes it once
        row.Tag = "goal";
        AutomationProperties.SetName(row, $"Goal: {Goals.Label(g)}, {GoalCountText(g, p)}");
        CardTip(row, () => GoalCard(g));
        row.MouseLeftButtonUp += (_, _) => OpenGoalSheet(g, GoalDraft.From(g), row);
        return row;
    }

    /// <summary>The "Set a goal" row after the goals, while fewer than three: a click opens Set a goal.</summary>
    FrameworkElement AddGoalRow(bool rule)
    {
        var row = QRow(QIconBox("Plus", "Dim", 28, round: true), "Set a goal for today", "Kinah, an item or runs · up to three", "", "Ink", rule: rule, inline: true);
        row.Tag = "add";
        ToolTipService.SetInitialShowDelay(row, 400);
        AutomationProperties.SetName(row, "Add a goal");
        row.MouseLeftButtonUp += (_, _) => OpenGoalSheet(null, new GoalDraft(), row);
        return row;
    }
}
