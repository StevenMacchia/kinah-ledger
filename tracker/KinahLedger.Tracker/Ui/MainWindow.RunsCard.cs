using System.Globalization;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using Path = System.Windows.Shapes.Path;
using KinahLedger.Data;
using KinahLedger.Session;

namespace KinahLedger.Ui;

/// <summary>
/// The Events part (0.8.31, kinah-bench/mock-events-v2.html, Steven 2026-10-25: "build it as shown"): one card per place,
/// drawn in the window's own look (Daylight is white cards on a light page; the old cards were fixed dark and sat as navy
/// slabs on white, "awful, especially in light mode"). The band names the place and its kind and where its run stands;
/// four labelled figures (an hour, a run, how long a run takes, how many are counted and why the rest aren't); the place
/// being run opens with Now (the last five lines and the run's steps); then the runs as a table with column names, a
/// wrong run flagged in place with its fix sheet under it; then the loot of the counted runs as an inventory row and three
/// folded groups. Only the item icons keep the game's dark slots.
/// </summary>
public partial class MainWindow
{
    /// <summary>Places whose earlier runs are all shown, and the groups open ("place|Materials", "place|loot").</summary>
    readonly HashSet<string> _runsAll = new(StringComparer.OrdinalIgnoreCase), _runsOpen = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>The place a layout render shows as in progress.</summary>
    string? _demoLive;
    /// <summary>Runs shown before "Show all N runs".</summary>
    const int RunsShown = 4;
    /// <summary>The card's width (the window's ledger column).</summary>
    const double PlaceCardWidth = 660;

    void ShowInstances()
    {
        var log = _watcher.Chat?.Instances ?? _demoInst;
        var all = log?.Snapshot() ?? new List<InstanceRun>();
        // the flags the player cleared with Keep as one stay off (RunFixes); a sheet reads the kept reward screens afresh
        RunFixes.ApplyKept(all);
        _screenLines = null;
        if (_demoInst != null && _demoScreens == null) DemoScreens(all);
        // this session's runs; earlier ones kept on this PC only on request, so yesterday's trip doesn't sit there all day
        // (Steven, 2026-10-22: "it says last run 14 hr ago")
        var since = _watcher.ChatSince == default ? DateTime.UtcNow : _watcher.ChatSince;
        var live = _demoInst != null ? _demoLive : log?.InProgress;
        bool Now(InstanceRun r) => (r.LastLoot ?? r.Start) >= since || live == r.Zone;
        var runs = _eventsAll || _demoInst != null ? all : all.Where(Now).ToList();
        int earlier = all.Count(r => !Now(r));
        // the switch shows once there is something earlier to switch to (a render shows it with the demo's runs)
        InstScope.Visibility = earlier > 0 || _demoInst != null ? Visibility.Visible : Visibility.Collapsed;
        bool allShown = _eventsAll || _demoInst != null;
        InstSessionBtn.Style = (Style)FindResource(allShown ? "SegOff" : "SegOn");
        InstEarlierBtn.Style = (Style)FindResource(allShown ? "SegOn" : "SegOff");
        InstEarlierBtn.Content = $"Earlier ({(_demoInst != null ? all.Count : earlier)})";
        InstHost.Children.Clear();
        _eventsRate = "";
        // the panel over the game says so when a run of this session just closed flagged
        var flagged = runs.Where(r => r.OutOfAverages && r.LastLoot is { } l && (DateTime.UtcNow - l).TotalMinutes < 2).OrderByDescending(r => r.LastLoot).FirstOrDefault();
        _hudFlag = flagged == null ? "" : flagged.Suspect == "looks like several runs" ? $"{flagged.Zone} record looks like several runs" : $"{flagged.Zone} run: {FlagReason(flagged, 0).ToLowerInvariant()}";
        long made = 0; int counted = 0;
        bool first = true;
        // the place being run first, then the others by their last run
        foreach (var g in runs.GroupBy(r => r.Zone, StringComparer.OrdinalIgnoreCase)
                     .OrderByDescending(g => string.Equals(g.Key, live, StringComparison.OrdinalIgnoreCase))
                     .ThenByDescending(g => g.Max(r => r.LastLoot ?? r.Start)))
        {
            var list = g.ToList();
            var card = BuildRunsCard(g.Key, list, string.Equals(live, g.Key, StringComparison.OrdinalIgnoreCase), PlaceCardWidth, ShowInstances, withNow: first, allRuns: runs);
            card.Margin = new Thickness(0, 0, 0, 16);
            InstHost.Children.Add(card);
            foreach (var r in list.Where(r => !r.OutOfAverages)) { counted++; made += RunValue(r); }
            first = false;
        }
        InstWhat.Text = InstHost.Children.Count > 0 ? $"{runs.Count} run{(runs.Count == 1 ? "" : "s")} · {counted} counted · {Fk(made)} made" : "";
        InstWhat.ToolTip = InstHost.Children.Count > 0 ? "Made: Kinah plus materials at the lowest Market price, after the Market's cut, over the counted runs. Gear is bound and not in it." : null;
        InstEmpty.Visibility = InstHost.Children.Count > 0 ? Visibility.Collapsed : Visibility.Visible;
    }

    /// <summary>
    /// A run's worth: its Kinah plus the loot that can be sold at the lowest Market price, after the Market's cut. Gear from
    /// an event or expedition is bound to the character, so only the materials count (Steven, 2026-10-22).
    /// </summary>
    long RunValue(InstanceRun r)
    {
        double v = r.Kinah;
        foreach (var (id, count) in r.Items)
            if (!_items.IsGear(id) && PriceBook.Price(_items.NameOf(id)) is long p) v += p * count * PriceBook.SellerKeeps;
        return (long)Math.Round(v);
    }

    /* ---------------- pieces in the window's look ---------------- */

    /// <summary>Text in one of the look's brushes ("Ink", "Muted", "Faint", "Gold", "SecGold"...), so a change of look repaints it.</summary>
    static TextBlock PT(string text, string brush, double size = 13, FontWeight? weight = null, string? font = null)
    {
        var t = new TextBlock { Text = text, FontSize = size, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center };
        t.SetResourceReference(TextBlock.ForegroundProperty, brush);
        if (weight is { } w) t.FontWeight = w;
        if (font != null) t.SetResourceReference(TextBlock.FontFamilyProperty, font);
        return t;
    }

    /// <summary>A run of text in one of the look's brushes.</summary>
    static Run PR(string text, string brush, FontWeight? weight = null)
    {
        var r = new Run(text);
        r.SetResourceReference(TextElement.ForegroundProperty, brush);
        if (weight is { } w) r.FontWeight = w;
        return r;
    }

    /// <summary>A section label: "RUNS" in the heading gold, a faint word after it, and a rule fading out to the right.</summary>
    static Grid PSection(string text, string note = "")
    {
        var g = new Grid { Margin = new Thickness(0, 4, 0, 10) };
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        g.ColumnDefinitions.Add(new ColumnDefinition());
        var t = new TextBlock { FontSize = 11, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0) };
        t.Inlines.Add(PR(text, "SecGold", FontWeights.Bold));
        if (note.Length > 0) { var n = PR("  " + note, "Faint"); n.FontSize = 12; t.Inlines.Add(n); }
        g.Children.Add(t);
        var rule = new Border { Height = 1, VerticalAlignment = VerticalAlignment.Center, OpacityMask = new LinearGradientBrush(Colors.Black, Colors.Transparent, 0) };
        rule.SetResourceReference(Border.BackgroundProperty, "PlaceBandLine");
        Grid.SetColumn(rule, 1); g.Children.Add(rule);
        return g;
    }

    /// <summary>The place's tile: a drawn glyph by its kind (a gate for an expedition, a star for an event...) in a lit gold slot.</summary>
    static FrameworkElement PlaceTile(string kind, double size = 48)
    {
        string path = kind switch
        {
            "Event" => "M12,3.2 L14.5,8.5 L20.3,9.2 L16,13.2 L17.1,19 L12,16.2 L6.9,19 L8,13.2 L3.7,9.2 L9.5,8.5 Z",
            InstanceLog.Transcendence => "M12,3 L19,10 H15 V21 H9 V10 H5 Z",
            "Arena" => "M4,4 L14,14 M20,4 L10,14 M6,18 L9,15 M18,18 L15,15 M4.5,19.5 L7,17 M19.5,19.5 L17,17",
            _ => "M5,21 V7 L12,3 L19,7 V21 Z M9.5,21 V14.5 A2.5,2.5 0 0 1 14.5,14.5 V21",
        };
        var p = new Path
        {
            Data = Geometry.Parse(path), Stroke = Ck(CkGold), StrokeThickness = 1.6, StrokeLineJoin = PenLineJoin.Round, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round,
            Stretch = Stretch.Uniform, Margin = new Thickness(size * 0.24),
        };
        return new Border
        {
            Width = size, Height = size, CornerRadius = new CornerRadius(8), BorderThickness = new Thickness(1.5), BorderBrush = Ck(CkGoldRule), VerticalAlignment = VerticalAlignment.Center, Child = p,
            Background = new RadialGradientBrush(Color.FromArgb(0x60, CkGold.R, CkGold.G, CkGold.B), CkSlot) { Center = new Point(0.5, 0.4), GradientOrigin = new Point(0.5, 0.4), RadiusX = 0.78, RadiusY = 0.78 },
            Effect = new DropShadowEffect { Color = CkGoldRule, BlurRadius = 14, ShadowDepth = 0, Opacity = 0.25 },
        };
    }

    /// <summary>A small rounded label in the band: "● Paid · 8:11 in" while a run is open (gold), "● last 18:02" after (faint).</summary>
    static Border PPill(string text, bool on)
    {
        var sp = new StackPanel { Orientation = Orientation.Horizontal };
        var dot = new Ellipse { Width = 7, Height = 7, Margin = new Thickness(0, 1, 7, 0), VerticalAlignment = VerticalAlignment.Center };
        dot.SetResourceReference(Shape.FillProperty, on ? "Gold" : "Faint");
        sp.Children.Add(dot);
        sp.Children.Add(PT(text, on ? "Gold" : "Faint", 12, on ? FontWeights.SemiBold : null));
        var b = new Border { Child = sp, CornerRadius = new CornerRadius(12), BorderThickness = new Thickness(1), Padding = new Thickness(10, 3, 11, 4), VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(12, 4, 0, 0) };
        b.SetResourceReference(Border.BorderBrushProperty, on ? "FlagLine" : "Line");
        if (on) b.SetResourceReference(Border.BackgroundProperty, "GoldTint");
        return b;
    }

    /// <summary>A flag in a run's row: the flag glyph, the reason in three words, a faint detail ("Partly read · 4 slots").</summary>
    static Border PFlag(string reason, string detail)
    {
        var sp = new StackPanel { Orientation = Orientation.Horizontal };
        var glyph = new Path { Data = Geometry.Parse("M3.5,14 V2.5 M3.5,3 H11.5 L9.9,5.8 L11.5,8.6 H3.5"), StrokeThickness = 1.5, StrokeLineJoin = PenLineJoin.Round, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round, Width = 12, Height = 12, Stretch = Stretch.Uniform, Margin = new Thickness(0, 0, 6, 0), VerticalAlignment = VerticalAlignment.Center };
        glyph.SetResourceReference(Shape.StrokeProperty, "FlagInk");
        sp.Children.Add(glyph);
        var t = new TextBlock { FontSize = 12, VerticalAlignment = VerticalAlignment.Center };
        t.Inlines.Add(PR(reason, "FlagInk", FontWeights.SemiBold));
        if (detail.Length > 0) { var d = PR(" · " + detail, "FlagInk"); d.FontWeight = FontWeights.Normal; t.Inlines.Add(d); }
        sp.Children.Add(t);
        var b = new Border { Child = sp, CornerRadius = new CornerRadius(6), BorderThickness = new Thickness(1), Padding = new Thickness(9, 3, 10, 4), HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
        b.SetResourceReference(Border.BackgroundProperty, "FlagBg");
        b.SetResourceReference(Border.BorderBrushProperty, "FlagLine");
        return b;
    }

    /* ---------------- a place's card ---------------- */

    /// <summary>
    /// One place's card. Figures count only the runs not flagged (<see cref="InstanceRun.OutOfAverages"/>). With
    /// <paramref name="withNow"/> (the place being run, or the last one run) it opens with Now and shows its loot; the others
    /// fold their loot behind one line. <paramref name="allRuns"/> feeds Now's lines (every place's steps).
    /// </summary>
    Border BuildRunsCard(string zone, List<InstanceRun> runs, bool live, double width, Action redraw, bool withNow = false, List<InstanceRun>? allRuns = null)
    {
        var body = new StackPanel { Width = width - 2 };
        var card = new Border { Child = body, CornerRadius = new CornerRadius(10), BorderThickness = new Thickness(1), Effect = new DropShadowEffect { BlurRadius = 18, ShadowDepth = 2, Opacity = 0.14, Direction = 270 } };
        card.SetResourceReference(Border.BackgroundProperty, "Card");
        card.SetResourceReference(Border.BorderBrushProperty, "LineStrong");
        var counted = runs.Where(r => !r.OutOfAverages).ToList();
        int n = counted.Count;
        double secs = counted.Sum(r => r.Seconds);
        long value = counted.Sum(RunValue);
        var order = runs.OrderByDescending(r => r.LastLoot ?? r.Start).ToList();
        var newest = order.FirstOrDefault();
        var kind = newest?.Kind ?? "";
        // the open run is the place's last one while the place is in progress (as the live card had it)
        var open = live ? runs.OrderBy(r => r.Start).LastOrDefault() : null;

        // the band: the place's tile, "Urugugu Canyon · Expedition", a line under it, and where its run stands
        var band = new Grid();
        band.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        band.ColumnDefinitions.Add(new ColumnDefinition());
        band.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        band.Children.Add(PlaceTile(kind));
        var titles = new StackPanel { Margin = new Thickness(14, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        var title = new TextBlock { FontSize = 19, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis };
        title.SetResourceReference(TextBlock.FontFamilyProperty, "DisplayFont");
        title.Inlines.Add(PR(zone, "Gold"));
        if (kind.Length > 0) title.Inlines.Add(PR(" · " + kind, "Faint", FontWeights.Normal));
        titles.Children.Add(title);
        var meta = $"{runs.Count} run{(runs.Count == 1 ? "" : "s")}";
        if (open != null) meta += $" · run {runs.OrderBy(r => r.Start).ToList().IndexOf(open) + 1} open since {open.Start.ToLocalTime():HH:mm}";
        var ml = PT(meta, "Muted", 12.5); ml.Margin = new Thickness(0, 3, 0, 0);
        titles.Children.Add(ml);
        Grid.SetColumn(titles, 1); band.Children.Add(titles);
        if (open != null)
        {
            int at = StepOf(open);
            var steps = new[] { "Entered", "In the run", "Reward screen", "Paid" };
            var pill = PPill($"{steps[Math.Max(0, at)]} · {Clock((open.LastLoot ?? DateTime.UtcNow) - open.Start)} in", true);
            pill.ToolTip = "A run is open here now.";
            Grid.SetColumn(pill, 2); band.Children.Add(pill);
        }
        else if (newest != null)
        {
            var last = (newest.LastLoot ?? newest.Start).ToLocalTime();
            var pill = PPill(last.Date == DateTime.Today ? $"last {last:HH:mm}" : $"last {last.ToString("MMM d", CultureInfo.InvariantCulture)}", false);
            Grid.SetColumn(pill, 2); band.Children.Add(pill);
        }
        var bandBox = new Border { Child = band, Padding = new Thickness(20, 16, 20, 14), BorderThickness = new Thickness(0, 0, 0, 1), CornerRadius = new CornerRadius(9, 9, 0, 0) };
        bandBox.SetResourceReference(Border.BackgroundProperty, "PlaceBand");
        bandBox.SetResourceReference(Border.BorderBrushProperty, "PlaceBandLine");
        body.Children.Add(bandBox);

        // the four figures, each with its label under it
        body.Children.Add(Figures(zone, runs, counted, secs, value, redraw));
        if (withNow && secs > 10) _eventsRate = Fk(value / secs * 3600);

        // Now: the place being run (or the last one run), its last five lines and where the open run stands
        if (withNow)
        {
            var now = new StackPanel { Margin = new Thickness(20, 16, 20, 4) };
            now.Children.Add(PSection("NOW", open != null ? $"run {runs.OrderBy(r => r.Start).ToList().IndexOf(open) + 1}" : "the last lines"));
            var box = new Border { CornerRadius = new CornerRadius(8), BorderThickness = new Thickness(1), Padding = new Thickness(0, 4, 0, 4), ClipToBounds = true };
            box.SetResourceReference(Border.BorderBrushProperty, "Line"); box.SetResourceReference(Border.BackgroundProperty, "Card2");
            var host = new StackPanel(); var empty = PT("Each run step and reward line shows here as it is counted.", "Faint", 12.5); empty.Margin = new Thickness(12, 10, 12, 10);
            var g0 = new Grid(); g0.Children.Add(host); g0.Children.Add(empty); box.Child = g0;
            now.Children.Add(box);
            _feedDrawn.Remove(host);
            DrawFeed(host, empty, EventsFeed(allRuns ?? runs), FeedTab.Events, PickupValue);
            now.Children.Add(StepTrack(open));
            body.Children.Add(now);
        }

        // the runs, newest first, with column names; the rest behind "Show all N runs"
        var sec = new StackPanel { Margin = new Thickness(20, 14, 20, 6) };
        sec.Children.Add(PSection("RUNS"));
        bool showAll = _runsAll.Contains(zone);
        double med = Median(counted.Select(r => r.Seconds));
        bool anyOld = order.Any(r => r.Start.ToLocalTime().Date != DateTime.Today);
        double startW = anyOld ? 104 : 66;
        sec.Children.Add(RunHead(startW));
        foreach (var r in showAll ? order : order.Take(RunsShown))
        {
            sec.Children.Add(RunRow(r, med, redraw, startW));
            // the run's sheet, open in place under it: a flagged run's fix, or any other run's details and Remove
            if (_fixOpen == RunKey(r)) sec.Children.Add(FlagReason(r, med).Length > 0 ? FixSheet(r, med, redraw) : RunSheet(r, redraw));
        }
        if (order.Count > RunsShown)
        {
            var m = PT(showAll ? "Show fewer" : $"Show all {order.Count} runs", "Gold", 12.5, FontWeights.SemiBold);
            m.Margin = new Thickness(8, 10, 0, 2); m.Cursor = Cursors.Hand; m.HorizontalAlignment = HorizontalAlignment.Left; m.Background = Brushes.Transparent;
            m.MouseLeftButtonUp += (_, _) => { if (!_runsAll.Remove(zone)) _runsAll.Add(zone); redraw(); };
            sec.Children.Add(m);
        }
        body.Children.Add(sec);

        // what the counted runs brought: open on the first card, one line to open it on the others
        var lootKey = zone + "|loot";
        if (withNow || _runsOpen.Contains(lootKey)) body.Children.Add(LootSection(zone, counted, redraw, foldable: !withNow));
        else if (n > 0)
        {
            var fold = new Grid { Margin = new Thickness(20, 8, 20, 16), Height = 40, Background = Brushes.Transparent, Cursor = Cursors.Hand };
            fold.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); fold.ColumnDefinitions.Add(new ColumnDefinition()); fold.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var chev = new Path { Data = Geometry.Parse("M0,0 L4,4 L0,8"), StrokeThickness = 1.5, Width = 8, Height = 8, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(14, 0, 10, 0) };
            chev.SetResourceReference(Shape.StrokeProperty, "Faint");
            fold.Children.Add(chev);
            var ft = PT("Loot", "Ink", 13.5, FontWeights.SemiBold); Grid.SetColumn(ft, 1); fold.Children.Add(ft);
            var fs = PT($"from the {n} counted run{(n == 1 ? "" : "s")}", "Muted", 12.5); fs.Margin = new Thickness(0, 0, 14, 0); Grid.SetColumn(fs, 2); fold.Children.Add(fs);
            var fb = new Border { Child = fold, CornerRadius = new CornerRadius(8), BorderThickness = new Thickness(1), Margin = new Thickness(20, 4, 20, 18) };
            fold.Margin = new Thickness(0);
            fb.SetResourceReference(Border.BorderBrushProperty, "Line"); fb.SetResourceReference(Border.BackgroundProperty, "Card2");
            fb.MouseLeftButtonUp += (_, _) => { _runsOpen.Add(lootKey); redraw(); };
            body.Children.Add(fb);
        }
        else body.Children.Add(new Border { Height = 10 });
        return card;
    }

    /// <summary>Where an open run stands: 0 entered, 1 in the run (loot came), 2 its reward screen, 3 paid.</summary>
    static int StepOf(InstanceRun r) => r.PaidAt != null ? 3 : r.Tables > 0 ? 2 : r.LastLoot != null ? 1 : 0;

    /// <summary>
    /// The four figures: an hour, a run (Kinah and loot, or loot for a place that pays no Kinah), how long a run takes, and
    /// how many runs are counted, with "why 4 aren't" (the reasons on hover; a click shows every run, the flagged ones too).
    /// </summary>
    FrameworkElement Figures(string zone, List<InstanceRun> runs, List<InstanceRun> counted, double secs, long value, Action redraw)
    {
        int n = counted.Count;
        var g = new Grid();
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.25, GridUnitType.Star) });
        g.ColumnDefinitions.Add(new ColumnDefinition());
        g.ColumnDefinitions.Add(new ColumnDefinition());
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.15, GridUnitType.Star) });
        void Cell(int col, FrameworkElement v, FrameworkElement l)
        {
            var sp = new StackPanel(); sp.Children.Add(v); l.Margin = new Thickness(0, 5, 0, 0); sp.Children.Add(l);
            var b = new Border { Child = sp, Padding = new Thickness(20, 14, 12, 13), BorderThickness = new Thickness(0, 0, col < 3 ? 1 : 0, 0) };
            b.SetResourceReference(Border.BorderBrushProperty, "Line");
            Grid.SetColumn(b, col); g.Children.Add(b);
        }
        TextBlock Big(string text, double size)
        {
            var t = PT(text, "Ink", size, FontWeights.Light, "DisplayFont"); t.TextTrimming = TextTrimming.None;
            return t;
        }
        if (n == 0)
        {
            // nothing counted yet: one line instead of four empty boxes
            var why = runs.Count == 0 ? "No run here yet." : $"No run counted yet. {Reasons(runs)}.";
            var t = PT(why, "Muted", 13); t.TextWrapping = TextWrapping.Wrap; t.TextTrimming = TextTrimming.None;
            var b0 = new Border { Child = t, Padding = new Thickness(20, 14, 20, 14), BorderThickness = new Thickness(0, 0, 0, 1) };
            b0.SetResourceReference(Border.BorderBrushProperty, "Line");
            return b0;
        }
        bool paysKinah = counted.Any(r => r.Kinah > 0);
        Cell(0, Big(secs > 10 ? Fk(value / secs * 3600) : "0", 30), PT("an hour", "Muted", 12));
        var runL = new TextBlock { FontSize = 12 }; runL.Inlines.Add(PR("a run", "Muted")); runL.Inlines.Add(PR(paysKinah ? " · Kinah + loot" : " · loot", "Faint"));
        Cell(1, Big(Fk(value / (double)n), 26), runL);
        Cell(2, Big(Clock(TimeSpan.FromSeconds(secs / n)), 26), PT("a run", "Muted", 12));
        var cv = new TextBlock { TextTrimming = TextTrimming.None, VerticalAlignment = VerticalAlignment.Center };
        cv.SetResourceReference(TextBlock.FontFamilyProperty, "DisplayFont");
        var big = PR(n.ToString(CultureInfo.InvariantCulture), "Ink", FontWeights.Light); big.FontSize = 26; cv.Inlines.Add(big);
        if (runs.Count > n) { var of = PR($" of {runs.Count}", "Muted", FontWeights.Normal); of.FontSize = 15; cv.Inlines.Add(of); }
        var cl = new TextBlock { FontSize = 12 };
        cl.Inlines.Add(PR("counted", "Muted"));
        int left = runs.Count - n;
        if (left > 0)
        {
            cl.Inlines.Add(PR(" · ", "Faint"));
            var link = new Hyperlink(new Run($"why {left} {(left == 1 ? "isn't" : "aren't")}")) { TextDecorations = TextDecorations.Underline, Cursor = Cursors.Hand };
            link.SetResourceReference(TextElement.ForegroundProperty, "Gold");
            link.Click += (_, _) => { _runsAll.Add(zone); redraw(); };
            cl.Inlines.Add(link);
            cl.ToolTip = Reasons(runs) + ". Left out of the figures; the runs show below with what to do about each.";
        }
        Cell(3, cv, cl);
        var box = new Border { Child = g, BorderThickness = new Thickness(0, 0, 0, 1) };
        box.SetResourceReference(Border.BorderBrushProperty, "Line");
        box.ToolTip = "Over the counted runs: Kinah plus materials at the lowest Market price, after the Market's cut. Gear is bound and not in the value. Flagged runs are left out.";
        return box;
    }

    /// <summary>Why runs are left out, counted: "1 not a run, 2 rewards not read, 1 partly read".</summary>
    static string Reasons(List<InstanceRun> runs)
    {
        double med = Median(runs.Where(r => !r.OutOfAverages).Select(r => r.Seconds));
        var parts = runs.Where(r => r.OutOfAverages).Select(r => FlagReason(r, med) is { Length: > 0 } w ? (w.StartsWith("Partly read", StringComparison.Ordinal) ? "partly read" : w.Contains("merged") ? "several runs saved as one" : w.ToLowerInvariant()) : "left out")
            .GroupBy(x => x).Select(x => $"{x.Count()} {x.Key}");
        return string.Join(", ", parts) is { Length: > 0 } s ? char.ToUpperInvariant(s[0]) + s[1..] : "";
    }

    /// <summary>The runs table's column names.</summary>
    static FrameworkElement RunHead(double startW)
    {
        var g = RunGrid(startW);
        void H(string text, int col, bool right = false)
        {
            var t = PT(text.ToUpperInvariant(), "Faint", 10.5, FontWeights.SemiBold);
            if (right) t.HorizontalAlignment = HorizontalAlignment.Right;
            Grid.SetColumn(t, col); g.Children.Add(t);
        }
        H("Started", 0); H("Took", 1); H("Kinah", 2, true); H("Loot", 3, true);
        var b = new Border { Child = g, Padding = new Thickness(8, 0, 8, 8), BorderThickness = new Thickness(0, 0, 0, 1) };
        b.SetResourceReference(Border.BorderBrushProperty, "Line");
        return b;
    }

    static Grid RunGrid(double startW)
    {
        var g = new Grid();
        foreach (var w in new[] { startW, 62.0, 78 }) g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(w) });
        g.ColumnDefinitions.Add(new ColumnDefinition());
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(22) });
        return g;
    }

    /// <summary>
    /// One run: when (today "14:07", earlier "Oct 23 19:45"), how long, its Kinah ("none", or "not read" when its coin
    /// wasn't), and its loot ("21 gear · 5 items · 18 unnamed"); a run left out of the figures carries its flag instead. A
    /// click opens its sheet under it: the fix for a flagged run, the details and Remove for any other.
    /// </summary>
    FrameworkElement RunRow(InstanceRun r, double medianSecs, Action redraw, double startW = 66)
    {
        var why = FlagReason(r, medianSecs);
        bool flag = why.Length > 0, out_ = r.OutOfAverages, isOpen = _fixOpen == RunKey(r);
        var g = RunGrid(startW);
        var local = r.Start.ToLocalTime();
        g.Children.Add(PT(local.Date == DateTime.Today ? local.ToString("HH:mm", CultureInfo.InvariantCulture) : local.ToString("MMM d HH:mm", CultureInfo.InvariantCulture), "Faint", 12.5, null, "MonoFont"));
        var len = PT(Clock(TimeSpan.FromSeconds(r.Seconds)), out_ ? "Faint" : "Ink", 12.5, null, "MonoFont"); Grid.SetColumn(len, 1); g.Children.Add(len);
        var kin = r.KinahUnknown || r.OldUnread ? PT("not read", "Faint", 12.5, null, "MonoFont") : r.Kinah == 0 ? PT("none", "Faint", 12.5, null, "MonoFont") : PT(Fk(r.Kinah), out_ ? "Faint" : "Ink", 12.5, null, "MonoFont");
        kin.HorizontalAlignment = HorizontalAlignment.Right; Grid.SetColumn(kin, 2); g.Children.Add(kin);
        FrameworkElement loot;
        if (flag)
        {
            var (reason, detail) = FlagParts(r, why);
            loot = PFlag(reason, detail);
        }
        else
        {
            var t = new TextBlock { FontSize = 12.5, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
            t.Inlines.Add(PR(LootText(r), "Muted"));
            if (r.SlotsUnread > 0) t.Inlines.Add(PR($" · {r.SlotsUnread} unnamed", "Faint"));
            loot = t;
        }
        loot.Margin = new Thickness(12, 0, 0, 0);
        Grid.SetColumn(loot, 3); g.Children.Add(loot);
        var chev = PT(isOpen ? "⌄" : "›", "Faint", 15); chev.HorizontalAlignment = HorizontalAlignment.Right;
        Grid.SetColumn(chev, 4); g.Children.Add(chev);
        var row = new Border
        {
            Child = g, Height = 42, Padding = new Thickness(8, 0, 8, 0), BorderThickness = new Thickness(0, 0, 0, 1), Background = Brushes.Transparent, Cursor = Cursors.Hand,
            ToolTip = flag ? FlagTip(r, medianSecs) + " Left out of the figures. Click to fix it." : "Click for this run's details, or to remove it if it's wrong.",
        };
        row.SetResourceReference(Border.BorderBrushProperty, "Line");
        ToolTipService.SetInitialShowDelay(row, 350);
        row.MouseEnter += (_, _) => { row.SetResourceReference(Border.BackgroundProperty, "HoverTint"); chev.SetResourceReference(TextBlock.ForegroundProperty, "Gold"); };
        row.MouseLeave += (_, _) => { row.Background = Brushes.Transparent; chev.SetResourceReference(TextBlock.ForegroundProperty, "Faint"); };
        row.MouseLeftButtonUp += (_, _) => { _fixOpen = isOpen ? null : RunKey(r); redraw(); };
        return row;
    }

    /// <summary>"21 gear · 5 items" (each piece of gear, each other item once, currencies never), or "Kinah only".</summary>
    string LootText(InstanceRun r)
    {
        var (gearN, other) = r.Pieces(_items.IsGear, IsCurrency, withUnknown: true);
        if (gearN + other == 0) return r.RewardsNotRead ? "rewards not read" : r.Kinah > 0 ? "Kinah only" : "nothing named";
        var parts = new List<string>();
        if (gearN > 0) parts.Add($"{gearN} gear");
        if (other > 0) parts.Add($"{other} item{(other == 1 ? "" : "s")}");
        return string.Join(" · ", parts);
    }

    /// <summary>The flag's words split for the row: ("Partly read", "4 slots"), ("Not a run", ""), ("5 runs merged", "").</summary>
    static (string Reason, string Detail) FlagParts(InstanceRun r, string why) =>
        r.PartlyRead && why.StartsWith("Partly read", StringComparison.Ordinal) ? ("Partly read", $"{r.SlotsUnread} slot{(r.SlotsUnread == 1 ? "" : "s")}") : (why, "");

    /// <summary>"31 pieces · 2 gear" (InstanceRun.Pieces: each piece of gear, each other item once), or "Kinah only". For logs and sheets.</summary>
    string PiecesText(InstanceRun r)
    {
        // currencies are amounts, never pieces (the 16:02 Hidden Lugi run read as "3,740 items": its Abyss Points and stones)
        var (gearN, other) = r.Pieces(_items.IsGear, IsCurrency, withUnknown: true);
        int pcs = gearN + other + r.UnknownItems;
        if (pcs == 0) return r.RewardsNotRead ? "rewards not read" : "Kinah only";
        return $"{pcs} piece{(pcs == 1 ? "" : "s")}" + (gearN > 0 ? $" · {gearN} gear" : "");
    }

    /// <summary>Why a run is left out, in three words: "5 runs merged", "Kinah not read", "Rewards not read", "Not a run", "Partly read: 4"; "" when it counts.</summary>
    static string FlagReason(InstanceRun r, double medianSecs)
    {
        if (r.Suspect == "looks like several runs")
        {
            int k = medianSecs > 0 ? (int)Math.Round(r.Seconds / medianSecs) : 0;
            return k >= 2 ? $"{k} runs merged" : "Several runs merged";
        }
        if (r.Suspect == "not a run") return "Not a run";
        if (r.Suspect.Length > 0) return char.ToUpperInvariant(r.Suspect[0]) + string.Join(' ', r.Suspect.Split(' ').Take(3))[1..];
        if (r.RewardsNotRead) return "Rewards not read";
        if (r.KinahNotRead || r.OldUnread) return "Kinah not read";
        // a partly read run that still counts (a slot or two not named) carries no flag, only its "unnamed" count
        if (r.PartlyRead && r.OutOfAverages) return $"Partly read: {r.SlotsUnread}";
        return "";
    }

    /// <summary>A currency (Abyss Points, Enhance Stone, a token): an amount, never a piece.</summary>
    bool IsCurrency(long id) => _items.Get(id)?.Category == "Currency";

    /// <summary>The flag's reason in a sentence, on hover.</summary>
    static string FlagTip(InstanceRun r, double medianSecs) =>
        r.Suspect == "looks like several runs" ? $"Looks like several runs saved as one: {Clock(TimeSpan.FromSeconds(r.Seconds))} against a usual {(medianSecs > 0 ? Clock(TimeSpan.FromSeconds(medianSecs)) : "run")}."
        : r.Suspect == "not a run" ? "Not a run: no Kinah, and its reward screen named nothing or came moments after entering (another window read as one)."
        : r.RewardsNotRead ? "Its reward screen wasn't read, so what it paid isn't known."
        : r.KinahNotRead || r.OldUnread ? "The Kinah on its reward screen wasn't read, so it isn't counted as 0."
        : r.PartlyRead ? $"Partly read: {r.SlotsUnread} slot{(r.SlotsUnread == 1 ? "" : "s")} on its reward cards weren't read (cut by a card's window, or below what the screen showed). What was read was sent, marked partly read."
        : r.Suspect;

    /* ---------------- loot ---------------- */

    /// <summary>
    /// The loot of the counted runs: an inventory row of the best of it (the Kinah first, then materials by value, gear by
    /// grade, currencies), then three folded groups: Materials (with their value), Gear, bound, and Not named yet.
    /// </summary>
    FrameworkElement LootSection(string zone, List<InstanceRun> counted, Action redraw, bool foldable)
    {
        int n = counted.Count;
        var mats = new Dictionary<long, (int Count, int Runs, long Value, bool Priced)>(); var gear = new Dictionary<long, int>(); int unread = 0, unknownGear = 0;
        // currencies (Abyss Points, Enhance Stone, tokens) are amounts, not pieces: a group of their own
        var curr = new Dictionary<long, long>();
        long kinah = 0;
        foreach (var r in counted)
        {
            kinah += r.Kinah;
            unknownGear += r.UnknownGear;
            foreach (var (id, c) in r.Currencies(IsCurrency)) curr[id] = curr.GetValueOrDefault(id) + c;
            foreach (var (id, c) in r.Items.Where(x => x.Value > 0))
            {
                if (IsCurrency(id)) continue;
                if (_items.IsGear(id)) { gear[id] = gear.GetValueOrDefault(id) + c; continue; }
                var p = PriceBook.Price(_items.NameOf(id));
                var o = mats.GetValueOrDefault(id);
                mats[id] = (o.Count + c, o.Runs, o.Value + (p is long pp ? (long)Math.Round(pp * c * PriceBook.SellerKeeps) : 0), o.Priced || p != null);
            }
            foreach (var (id, c) in r.BoundItems.Where(x => x.Value > 0))
            {
                if (IsCurrency(id)) continue;
                if (_items.IsGear(id)) { gear[id] = gear.GetValueOrDefault(id) + c; continue; }
                var o = mats.GetValueOrDefault(id);
                mats[id] = (o.Count + c, o.Runs, o.Value, o.Priced);
            }
            foreach (var id in r.Items.Where(x => x.Value > 0).Select(x => x.Key).Concat(r.BoundItems.Where(x => x.Value > 0).Select(x => x.Key)).Distinct().Where(id => !_items.IsGear(id) && !IsCurrency(id)))
                mats[id] = mats[id] with { Runs = mats[id].Runs + 1 };
            unread += r.SlotsUnread;
        }
        var sec = new StackPanel { Margin = new Thickness(20, 8, 20, 18) };
        var head = PSection("LOOT", n == 1 ? "from the one counted run" : $"from the {n} counted runs");
        if (foldable) { head.Cursor = Cursors.Hand; head.Background = Brushes.Transparent; head.MouseLeftButtonUp += (_, _) => { _runsOpen.Remove(zone + "|loot"); redraw(); }; head.ToolTip = "Fold the loot"; }
        sec.Children.Add(head);

        // the inventory row, nine slots at most: the Kinah, the most valuable materials (five at most), the best gear, then
        // whatever is left (more materials, the currencies)
        var pick = new List<FrameworkElement>();
        if (kinah > 0) pick.Add(WithBadge(CoinTile(46), CountBadge(kinah), $"Kinah: {kinah:N0}"));
        var matSlots = mats.OrderByDescending(x => x.Value.Value).ThenByDescending(x => x.Value.Count)
            .Select(x => Tip(CkIcon(_items.NameOf(x.Key), 46, FrameOf(x.Key), badge: CountBadge(x.Value.Count)), _items.NameOf(x.Key) + (x.Value.Value > 0 ? $": {Fk(x.Value.Value)} after the Market's cut" : ""))).ToList();
        var gearSlots = gear.OrderByDescending(x => GradeRank(_items.Get(x.Key)?.Grade ?? "")).ThenByDescending(x => x.Value)
            .Select(x => Tip(CkIcon(_items.NameOf(x.Key), 46, FrameOf(x.Key), badge: CountBadge(x.Value)), _items.NameOf(x.Key) + " (bound)")).ToList();
        var currSlots = curr.OrderByDescending(x => x.Value)
            .Select(x => Tip(CkIcon(_items.NameOf(x.Key), 46, FrameOf(x.Key), badge: CountBadge(x.Value)), _items.NameOf(x.Key))).ToList();
        pick.AddRange(matSlots.Take(5));
        pick.AddRange(gearSlots.Take(Math.Max(0, 9 - pick.Count)));
        pick.AddRange(matSlots.Skip(5).Concat(currSlots).Take(Math.Max(0, 9 - pick.Count)));
        if (pick.Count > 0)
        {
            var inv = new WrapPanel { Margin = new Thickness(0, 2, 0, 12) };
            foreach (var sl in pick) { sl.Margin = new Thickness(0, 0, 14, 8); inv.Children.Add(sl); }
            sec.Children.Add(inv);
        }

        if (mats.Count > 0)
        {
            long mv = mats.Values.Sum(x => x.Value);
            var sum = new List<Inline> { PR($"{mats.Count} kind{(mats.Count == 1 ? "" : "s")}", "Muted") };
            sum.Add(PR(" · ", "Faint"));
            if (mv > 0) { var v = PR(Fk(mv), "Gold"); v.SetResourceReference(TextElement.FontFamilyProperty, "MonoFont"); sum.Add(v); sum.Add(PR(" after the Market's cut", "Muted")); }
            else sum.Add(PR("no prices yet", "Faint"));
            sec.Children.Add(RunsGroup(zone, "Materials", sum, () =>
            {
                var sp = new StackPanel();
                foreach (var (id, x) in mats.OrderByDescending(x => x.Value.Value).ThenByDescending(x => x.Value.Count))
                    sp.Children.Add(RunsItem(_items.NameOf(id), x.Count, $"in {x.Runs} of {n} runs", x.Value > 0 ? Fk(x.Value) : "", x.Value > 0 ? "" : x.Priced ? "bound" : "no price yet"));
                return sp;
            }, redraw));
        }
        if (curr.Count > 0)
        {
            sec.Children.Add(RunsGroup(zone, "Currencies", new List<Inline> { PR($"{curr.Count} kind{(curr.Count == 1 ? "" : "s")}", "Muted") }, () =>
            {
                var sp = new StackPanel();
                foreach (var (id, c) in curr.OrderByDescending(x => x.Value))
                    sp.Children.Add(RunsItem(_items.NameOf(id), (int)Math.Min(c, int.MaxValue), "", "", ""));
                return sp;
            }, redraw));
        }
        if (gear.Count > 0 || unknownGear > 0)
        {
            int pieces = gear.Values.Sum() + unknownGear;
            int fine = gear.Where(x => GradeRank(_items.Get(x.Key)?.Grade ?? "") >= 2).Sum(x => x.Value);
            var sum = new List<Inline> { PR($"{pieces} piece{(pieces == 1 ? "" : "s")}", "Muted") };
            if (fine > 0) { sum.Add(PR(" · ", "Faint")); sum.Add(PR($"{fine} Legend or better", "Muted")); }
            sec.Children.Add(RunsGroup(zone, "Gear, bound", sum, () =>
            {
                var sp = new StackPanel();
                foreach (var (id, c) in gear.OrderByDescending(x => GradeRank(_items.Get(x.Key)?.Grade ?? "")).ThenByDescending(x => x.Value))
                    sp.Children.Add(RunsItem(_items.NameOf(id), c, "", "", ""));
                // a piece whose icon two items share: gear all the same, its name not known
                if (unknownGear > 0) sp.Children.Add(RunsItem("Unknown gear", unknownGear, "its icon is shared by two items", "", "", unknown: true));
                return sp;
            }, redraw));
        }
        if (unread > 0)
        {
            sec.Children.Add(RunsGroup(zone, "Not named yet", new List<Inline> { PR($"{unread:N0} slot{(unread == 1 ? "" : "s")} seen, not named", "Faint") }, () =>
            {
                var sp = new StackPanel();
                var note = PT("Slots on the reward screens the tracker saw but couldn't name. They aren't guessed, and they aren't in the value.", "Faint", 12);
                note.TextWrapping = TextWrapping.Wrap; note.TextTrimming = TextTrimming.None; note.Margin = new Thickness(0, 8, 0, 4);
                sp.Children.Add(note);
                sp.Children.Add(RunsItem("Unknown item", unread, "", "", "", unknown: true));
                return sp;
            }, redraw));
        }
        if (mats.Count + curr.Count + gear.Count + unknownGear + unread == 0 && kinah == 0)
            sec.Children.Add(PT(n == 0 ? "Nothing counted yet." : "Nothing named on the reward screens yet.", "Faint", 12.5));
        return sec;
    }

    /// <summary>An item's grade colour for its slot frame; the plain frame for common items.</summary>
    Color FrameOf(long id) => _items.Get(id)?.Grade is { Length: > 0 } g && g != "Common" ? GradeColor(g) : CkFrame;

    /// <summary>A stack count for a slot's badge: "14", "140", "6.01M", "40K".</summary>
    static string CountBadge(long c) => c < 1000 ? c.ToString("N0", CultureInfo.InvariantCulture) : Fk(c);

    static FrameworkElement Tip(FrameworkElement e, string tip) { e.ToolTip = tip; ToolTipService.SetInitialShowDelay(e, 250); return e; }

    /// <summary>A tile with a count badge at its bottom right (the coin has no icon of its own to carry one).</summary>
    static FrameworkElement WithBadge(FrameworkElement tile, string badge, string tip)
    {
        var g = new Grid { Width = 52, Height = 52, ToolTip = tip };
        tile.HorizontalAlignment = HorizontalAlignment.Left; tile.VerticalAlignment = VerticalAlignment.Top;
        g.Children.Add(tile);
        g.Children.Add(new Border
        {
            HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom, CornerRadius = new CornerRadius(8), Padding = new Thickness(5, 0, 5, 1),
            Background = Ck(CkSlot), BorderBrush = Ck(CkFrame), BorderThickness = new Thickness(1),
            Child = new TextBlock { Text = badge, FontSize = 11, FontWeight = FontWeights.Bold, Foreground = Brushes.White },
        });
        return g;
    }

    /// <summary>A folded group under Loot: one line (name, its summary) until a click opens it.</summary>
    FrameworkElement RunsGroup(string zone, string name, List<Inline> summary, Func<FrameworkElement> content, Action redraw)
    {
        var key = zone + "|" + name;
        bool open = _runsOpen.Contains(key);
        var sp = new StackPanel();
        var box = new Border { Child = sp, CornerRadius = new CornerRadius(8), BorderThickness = new Thickness(1), Margin = new Thickness(0, 0, 0, 8) };
        box.SetResourceReference(Border.BorderBrushProperty, open ? "LineStrong" : "Line");
        box.SetResourceReference(Border.BackgroundProperty, "Card2");
        var head = new Grid { Height = 42, Margin = new Thickness(14, 0, 14, 0), Background = Brushes.Transparent, Cursor = Cursors.Hand };
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        head.ColumnDefinitions.Add(new ColumnDefinition());
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var chev = new Path
        {
            Data = Geometry.Parse("M0,0 L4,4 L0,8"), StrokeThickness = 1.5, Width = 8, Height = 8, VerticalAlignment = VerticalAlignment.Center,
            RenderTransformOrigin = new Point(0.5, 0.5), RenderTransform = new RotateTransform(open ? 90 : 0),
        };
        chev.SetResourceReference(Shape.StrokeProperty, open ? "Gold" : "Faint");
        head.Children.Add(chev);
        var nm = PT(name, "Ink", 13.5, FontWeights.SemiBold); nm.Margin = new Thickness(12, 0, 0, 0); Grid.SetColumn(nm, 1); head.Children.Add(nm);
        var sum = new TextBlock { FontSize = 12.5, VerticalAlignment = VerticalAlignment.Center }; sum.Inlines.AddRange(summary); Grid.SetColumn(sum, 2); head.Children.Add(sum);
        head.MouseEnter += (_, _) => head.SetResourceReference(Panel.BackgroundProperty, "HoverTint");
        head.MouseLeave += (_, _) => head.Background = Brushes.Transparent;
        head.MouseLeftButtonUp += (_, _) => { if (!_runsOpen.Remove(key)) _runsOpen.Add(key); redraw(); };
        sp.Children.Add(head);
        if (open)
        {
            var inner = new Border { Child = content(), BorderThickness = new Thickness(0, 1, 0, 0), Padding = new Thickness(14, 2, 14, 10) };
            inner.SetResourceReference(Border.BorderBrushProperty, "Line");
            sp.Children.Add(inner);
        }
        return box;
    }

    /// <summary>An item in a group: its icon with the count, its name (and, for materials, in how many runs it came), its value.</summary>
    FrameworkElement RunsItem(string name, int count, string sub, string value, string noValue, bool unknown = false)
    {
        var g = new Grid { Margin = new Thickness(0, 6, 0, 6) };
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(52) });
        g.ColumnDefinitions.Add(new ColumnDefinition());
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        FrameworkElement icon;
        if (unknown)
        {
            var box = new Border { Width = 34, Height = 34, CornerRadius = new CornerRadius(5), BorderThickness = new Thickness(1), BorderBrush = Ck(CkFrame, 200), Background = Ck(Color.FromRgb(0x15, 0x1A, 0x30)),
                Child = new TextBlock { Text = "?", FontSize = 13, FontWeight = FontWeights.Bold, Foreground = Ck(CkMuted), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center } };
            var wrap = new Grid { Width = 40, Height = 40 }; box.HorizontalAlignment = HorizontalAlignment.Left; box.VerticalAlignment = VerticalAlignment.Top; wrap.Children.Add(box);
            wrap.Children.Add(new Border { HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom, CornerRadius = new CornerRadius(8), Padding = new Thickness(5, 0, 5, 1),
                Background = Ck(CkSlot), BorderBrush = Ck(CkFrame), BorderThickness = new Thickness(1), Child = new TextBlock { Text = count.ToString("N0"), FontSize = 11.5, FontWeight = FontWeights.Bold, Foreground = Brushes.White } });
            icon = wrap;
        }
        else icon = CkIcon(name, 34, FrameOf(_items.IdOf(name)), badge: count.ToString("N0"));
        icon.VerticalAlignment = VerticalAlignment.Center;
        g.Children.Add(icon);
        var names = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 12, 0) };
        names.Children.Add(PT(name, "Ink", 13.5, FontWeights.SemiBold));
        if (sub.Length > 0) { var s = PT(sub, "Faint", 11.5); s.Margin = new Thickness(0, 1, 0, 0); names.Children.Add(s); }
        Grid.SetColumn(names, 1); g.Children.Add(names);
        var v = value.Length > 0 ? PT(value, "Gold", 13, null, "MonoFont") : PT(noValue, "Faint", 12.5);
        v.HorizontalAlignment = HorizontalAlignment.Right; Grid.SetColumn(v, 2); g.Children.Add(v);
        return g;
    }

    /// <summary>A wrong run, removed on the player's say (Steven, 2026-10-23): it leaves the list and won't be sent to the site.</summary>
    void RemoveRun(InstanceRun r, Action redraw)
    {
        if (_watcher.Chat?.Instances is not { } il || _demoInst != null) return;
        if (il.Remove(r.Start, r.Zone)) Say($"Removed: {r.Zone}, {r.Start.ToLocalTime():MMM d HH:mm}. A copy is kept aside in instances.removed.jsonl.", "Good");
        _fixOpen = null;
        redraw();
    }

    static double Median(IEnumerable<double> xs)
    {
        var l = xs.OrderBy(x => x).ToList();
        if (l.Count == 0) return 0;
        return l.Count % 2 == 1 ? l[l.Count / 2] : (l[l.Count / 2 - 1] + l[l.Count / 2]) / 2;
    }

    /* ---------------- layout renders ---------------- */

    /// <summary>
    /// Sample runs for a layout render: eleven Urugugu Canyon expeditions (nine counted, one that is five runs saved as one,
    /// one whose Kinah wasn't read) and three Shugo Festival events (one partly read), written to a scratch folder and loaded
    /// the way the tracker loads its own, so the flags come from the same rules. Never the player's runs. Example figures.
    /// </summary>
    InstanceLog DemoRuns()
    {
        var dir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "KinahLedger-demo-runs");
        Directory.CreateDirectory(dir);
        foreach (var f in Directory.GetFiles(dir)) { try { File.Delete(f); } catch (Exception) { } }
        PriceBook.SetBoard(new() { ["Spiritstone Powder"] = 3_330, ["Artisan's Ultimate Refining Stone"] = 330_000, ["Sapphire Powder"] = 5_080, ["Magic Crystal"] = 4_550, ["Diamond Powder"] = 5_780 });
        var today = DateTime.Today; var yday = today.AddDays(-1); var before = today.AddDays(-2);
        var runs = new List<InstanceRun>();
        void Run(DateTime startLocal, int secs, long kinah, (string Name, int N)[] items, string[]? gearNames = null, bool kinahNotRead = false, int unread = 0, string zone = "Urugugu Canyon", string kind = "Expedition", bool partly = false)
        {
            var start = startLocal.ToUniversalTime();
            var r = new InstanceRun
            {
                Zone = zone, Kind = kind, Start = start, LastLoot = start.AddSeconds(secs), PaidAt = start.AddSeconds(secs - 20), Kinah = kinah, KinahNotRead = kinahNotRead,
                FromPicture = true, SlotsUnread = unread, PartlyRead = partly, Tables = 1, TableAt = start.AddSeconds(secs - 20), Ended = "left; rewards in",
                Witnesses = $"minimap {startLocal:HH:mm:ss} · header {startLocal.AddSeconds(4):HH:mm:ss}",
            };
            foreach (var (name, c) in items) r.Items[_items.IdOf(name)] = c;
            foreach (var gname in gearNames ?? Array.Empty<string>()) { var id = _items.IdOf(gname); r.BoundItems[id] = r.BoundItems.GetValueOrDefault(id) + 1; r.Bound++; }
            runs.Add(r);
        }
        const string A = "Amplify Stone Fragment (Unique)", S = "Spiritstone Powder", R = "Artisan's Ultimate Refining Stone", P = "Sapphire Powder", M = "Magic Crystal", D = "Diamond Powder";
        Run(before.AddHours(18).AddMinutes(2), 7 * 60 + 41, 512_000, new[] { (S, 18), (P, 9), (M, 6) }, new[] { "Elder Breastplate", "Defender Breastplate" });
        Run(before.AddHours(18).AddMinutes(14), 6 * 60 + 55, 548_300, new[] { (A, 6), (S, 21), (M, 5) }, new[] { "Illusion Breastplate" }, unread: 1);
        Run(before.AddHours(18).AddMinutes(26), 8 * 60 + 12, 590_000, new[] { (S, 19), (P, 8), (D, 6) }, new[] { "Temple Guardian Breastplate", "Pragmatist Breastplate" });
        Run(before.AddHours(18).AddMinutes(39), 7 * 60 + 5, 476_800, new[] { (A, 5), (P, 9), (M, 7) }, new[] { "Elder Breastplate" }, unread: 1);
        Run(before.AddHours(19).AddMinutes(51), 7 * 60 + 30, 621_000, new[] { (S, 20), (R, 1), (P, 10) }, new[] { "Destruction Breastplate", "Relic Collector Breastplate" });
        Run(yday.AddHours(16).AddMinutes(58), 6 * 60 + 40, 534_500, new[] { (A, 7), (S, 22), (M, 6), (D, 6) }, new[] { "Illusion Breastplate" });
        Run(yday.AddHours(17).AddMinutes(20), 8 * 60 + 2, 557_900, new[] { (S, 20), (P, 8), (M, 6) }, new[] { "Whispering Oblivion Breastplate" });
        Run(yday.AddHours(17).AddMinutes(48), 8 * 60 + 27, 0, new[] { (S, 17), (P, 6) }, new[] { "Elder Breastplate" }, kinahNotRead: true);
        Run(yday.AddHours(18).AddMinutes(26), 37 * 60 + 4, 3_390_000, new[] { (A, 22), (S, 90), (P, 41), (M, 30) }, new[] { "Defender Breastplate", "Elder Breastplate", "Illusion Breastplate", "Pragmatist Breastplate", "Temple Guardian Breastplate" });
        Run(yday.AddHours(19).AddMinutes(45), 9 * 60 + 2, 702_000, new[] { (S, 20), (P, 10), (M, 8) }, new[] { "Elder Breastplate" });
        Run(today.AddHours(14).AddMinutes(7), 5 * 60 + 48, 603_500, new[] { (A, 7), (P, 10), (M, 7) }, new[] { "Temple Guardian Breastplate", "Defender Breastplate" });
        // three Shugo Festival events: no Kinah (their cards give items and currencies), one partly read
        const string Od = "Odyle", Fo = "Fine Odyle", Rs = "Refining Stone";
        Run(today.AddHours(11).AddMinutes(2), 4 * 60 + 51, 0, new[] { (Rs, 6), (Od, 8), (Fo, 1) }, zone: "Shugo Festival", kind: "Event");
        Run(today.AddHours(12).AddMinutes(2), 3 * 60 + 58, 0, new[] { (Rs, 6), (Od, 5), (Fo, 2) }, zone: "Shugo Festival", kind: "Event", unread: 4, partly: true);
        Run(today.AddHours(13).AddMinutes(2), 5 * 60 + 4, 0, new[] { (Rs, 6), (Od, 8), (Fo, 1) }, zone: "Shugo Festival", kind: "Event");
        var keep = CrashLog.DirOverride;
        try
        {
            CrashLog.DirOverride = dir;   // the sample's file and notes, never the player's
            File.WriteAllLines(System.IO.Path.Combine(dir, "instances.jsonl"), runs.Select(r => JsonSerializer.Serialize(r)));
            var log = new InstanceLog();
            log.UseNames(_items);
            _demoLive = "Urugugu Canyon";
            return log;
        }
        finally { CrashLog.DirOverride = keep; }
    }

    /// <summary>The Runs card alone for a layout render: folded, and with Materials open (ui-runs-card.png, ui-runs-card-materials.png).</summary>
    internal void ShotRunsCards(string dir)
    {
        var log = _demoInst ?? DemoRuns();
        var all = log.Snapshot();
        var runs = all.Where(r => r.Zone == "Urugugu Canyon").ToList();
        if (runs.Count == 0) return;
        void Save(string file)
        {
            var card = BuildRunsCard("Urugugu Canyon", runs, true, PlaceCardWidth, () => { }, withNow: true, allRuns: all);
            card.Margin = new Thickness(10);
            var frame = new Border { Child = card, Padding = new Thickness(12) };   // the page under the card, in the look
            frame.SetResourceReference(Border.BackgroundProperty, "Bg");
            IconCache.Drain(TimeSpan.FromSeconds(8));
            Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.Background);
            SaveCard(frame, System.IO.Path.Combine(dir, file));
        }
        _runsOpen.Clear();
        var fix = _fixOpen; _fixOpen = null;
        Save("ui-runs-card.png");
        _runsOpen.Add("Urugugu Canyon|Materials");
        Save("ui-runs-card-materials.png");
        _runsOpen.Clear();
        // the merged record's fix sheet open: Split at the reward screens, Keep as one, Remove
        _fixOpen = fix;
        if (_fixOpen != null) Save("ui-runs-card-fix.png");
    }
}
