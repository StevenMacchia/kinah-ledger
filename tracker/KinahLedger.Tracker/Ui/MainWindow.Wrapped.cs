using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using KinahLedger.Data;
using KinahLedger.Session;

namespace KinahLedger.Ui;

/// <summary>
/// Week wrapped and Day wrapped (2026-11-11, Steven: "cool and flashy", "amazing and professional and well done"): a recap
/// sheet of the week or of today, for the character counted for, made to be screenshotted and shared. "Week wrapped" sits
/// at the foot of LOOT's Week side, "Day wrapped" at the foot of its Today side, and the Events strip's head has the one for
/// the side it shows. The sheet opens in a window of its own (Ui/WrappedWindow.cs) and plays once: the total counts up,
/// then the time and the hour's rate, the top zone, the best run, the biggest drop (its tile landing and glowing in its
/// grade), crafting, and last, on the week, the change against the week before, up green or down red; all settled in under
/// 2.5 s (Ui/Motion.Wrapped.cs). The figures come from the lists and sums the views use (Session/Wrapped.cs): farming as
/// the Week side and LOOT's Today side make it, the runs (each on the day it started) as the TODAY strip sums them and the
/// Events cards count a run. With too little to wrap it says what will appear and when. Copy picture and Save picture draw the
/// settled sheet again at twice its size, as Share does; nothing leaves the PC.
/// </summary>
public partial class MainWindow
{
    WrappedWindow? _wrapWin;
    /// <summary>What the open sheet shows, so its picture is the same sheet even when the count has moved on since.</summary>
    (WrappedFacts Facts, WrappedText Text)? _wrapShown;
    /// <summary>A layout render's crafting (example figures, never the player's); null for none.</summary>
    WrappedCraft? _demoCraft;

    /// <summary>The sheet's width; the picture is twice it.</summary>
    const double WrapWidth = 520;
    /// <summary>The game's art the sheet is known by: on its buttons and in its head.</summary>
    const string WrapArt = "weekly";
    /// <summary>A highlight's picture: the drop's item tile, or the game's art in a box of the same size.</summary>
    const double WrapIcon = 44;
    /// <summary>A highlight's name, a line of it: two at most.</summary>
    const double NameLine = 19;

    // the sequence (ms after the sheet opens): the head, the total counting up, the line under it, the tiles one after
    // another, the highlights one after another (the biggest drop's tile landing a little into its row), the change last;
    // a thin sheet's few parts one after another, ThinGap apart
    const double CueMs = 380, CountAt = 80, CountMs = 1000, LineAt = 260, TilesAt = 650, TileGap = 140, RowsAt = 1080, RowGap = 160,
        DropAt = 60, DropMs = 820, ChangeAfter = 120, FootAt = 900, ThinGap = 120;

    void InitWrapped() => TodayWrapBtn.Click += (_, _) => OpenWrapped(_runsWeek ? Wrapped.Week : Wrapped.Day);

    static string WrapLabel(string scope) => scope == Wrapped.Week ? "Week wrapped" : "Day wrapped";

    /// <summary>A button's face: the sheet's medallion and its name.</summary>
    static void WrapFace(Button b, string scope)
    {
        if (b.Tag as string == "wrapped-" + scope) return;
        var sp = new StackPanel { Orientation = Orientation.Horizontal };
        var art = GameArt.Element(WrapArt, 18); art.Margin = new Thickness(0, 0, 7, 0);
        sp.Children.Add(art);
        sp.Children.Add(new TextBlock { Text = WrapLabel(scope), VerticalAlignment = VerticalAlignment.Center });
        b.Content = sp;
        b.Tag = "wrapped-" + scope;
        b.ToolTip = scope == Wrapped.Week ? "Your last 7 days on one sheet, to copy or save as a picture." : "Today on one sheet, to copy or save as a picture.";
        AutomationProperties.SetName(b, WrapLabel(scope));
    }

    /// <summary>The Events strip's head button, for the side the strip shows (from FillToday).</summary>
    void SetWrapButton(string scope) => WrapFace(TodayWrapBtn, scope);

    /// <summary>The button at the foot of LOOT's Week or Today side, on the right.</summary>
    FrameworkElement WrappedButton(string scope)
    {
        var b = new Button { HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 14, 0, 0) };
        b.SetResourceReference(StyleProperty, "PillBtn");
        WrapFace(b, scope);
        b.Click += (_, _) => OpenWrapped(scope);
        return b;
    }

    /* ---------------- the figures ---------------- */

    /// <summary>
    /// The sheet's figures: the week (the last 7 days, against the 7 before) or today, from the lists the views draw from
    /// (days.json with today live, the run log with its fixes kept). Farming is the Week side's and LOOT's Today side's own
    /// made. A run belongs to the day it started, as on the Week side, so one past midnight is in one period only; its made
    /// is Kinah plus rewards, as the TODAY strip sums a day and the Events cards count a run (the Week side's bars count the
    /// Kinah alone, which would let one run out-make the whole week's total).
    /// </summary>
    internal WrappedFacts WrappedFor(string scope)
    {
        bool week = scope == Wrapped.Week;
        int n = week ? 7 : 1;
        var runs = WeekRunList();
        var days = DayHistory.Days(week ? 2 * n : n, TodayEngine, _items, runs, WeekFile());
        var now = days.TakeLast(n).ToList();
        var before = week ? days.Take(n).ToList() : null;
        var nowRuns = Wrapped.RunsIn(runs, now[0].Date, now[^1].Date);
        var beforeRuns = before != null ? Wrapped.RunsIn(runs, before[0].Date, before[^1].Date) : new();
        long Made(List<InstanceRun> rs) => week ? rs.Where(r => !r.OutOfAverages).Sum(RunValue) : TodayLoot.Runs(rs, _items, IsCurrency).Value;
        WrappedRun Run(InstanceRun r) => new(r.Label, ZoneCompare.PlaceOf(r), DayHistory.KindOf(r.Kind) is { Length: > 0 } k ? k : r.Kind, DayHistory.LocalStart(r).Date, RunValue(r), r.Seconds, !r.OutOfAverages);
        // each counted run's rewards that sell, at the lowest Market price after the cut (gear is bound, currencies aren't sold)
        var drops = nowRuns.Where(r => !r.OutOfAverages).SelectMany(r => r.Items
            .Where(kv => kv.Value > 0 && !_items.IsGear(kv.Key) && !IsCurrency(kv.Key) && PriceBook.Price(_items.NameOf(kv.Key)) != null)
            .Select(kv => new WrappedDrop(_items.NameOf(kv.Key), _items.Get(kv.Key)?.Grade ?? "", kv.Value,
                (long)Math.Round(PriceBook.Price(_items.NameOf(kv.Key))!.Value * kv.Value * PriceBook.SellerKeeps), r.Label, DayHistory.LocalStart(r).Date)));
        // crafting is this session's, as the Crafting page sums it: on a sheet only when the session began within its days
        var craft = _demo ? _demoCraft : SumCrafts(CraftShown) is { Any: true } s && SessionSince(now[0].Date) ? new WrappedCraft(s.Net, s.Tries, s.AtMost) : null;
        string Grade(string item) => GatheringCatalog.Find(item)?.Grade ?? GradeOf(item);
        return Wrapped.Of(scope, new WrappedPeriod(now, nowRuns.Select(Run).ToList(), Made(nowRuns)),
            before != null ? new WrappedPeriod(before, beforeRuns.Select(Run).ToList(), Made(beforeRuns)) : null, drops, Grade, craft);
    }

    /// <summary>Whether this run of the tracker (and so the Crafting page's session) began on <paramref name="day"/> or later.</summary>
    static bool SessionSince(DateTime day)
    {
        using var me = System.Diagnostics.Process.GetCurrentProcess();
        return me.StartTime.Date >= day.Date;
    }

    /// <summary>"Aerin" and "Siel · NA East": who the sheet is for, as the header names the character and the server.</summary>
    (string Who, string Server) WrappedWho() => (CharacterLabel, ShareServer());

    /* ---------------- the sheet ---------------- */

    /// <summary>
    /// Opens the sheet for <paramref name="scope"/>, or draws it again in place when the window is up, and plays it. In a
    /// layout render or the self-test (the demo) the window opens off the screens, never where anyone would see it.
    /// </summary>
    void OpenWrapped(string scope)
    {
        var f = WrappedFor(scope);
        var t = Wrapped.Text(f, Fk);
        _wrapShown = (f, t);
        var (sheet, cues) = BuildWrappedSheet(f, t);
        if (_wrapWin == null) { _wrapWin = new WrappedWindow(this); _wrapWin.Closed += (_, _) => { _wrapWin = null; _wrapShown = null; }; }
        var acts = new List<(string, string, Action)>();
        if (!f.Thin)
        {
            acts.Add(("Copy picture", "Puts the sheet on the clipboard as a picture. Paste it with Ctrl+V.", CopyWrapped));
            acts.Add(("Save picture", @"Saves the sheet as a PNG in Pictures\Kinah Ledger.", SaveWrapped));
        }
        // a week of one day: that day has a sheet of its own
        else if (scope == Wrapped.Week && !WrappedFor(Wrapped.Day).Thin)
            acts.Add(("See Day wrapped", "Today on its own sheet.", () => OpenWrapped(Wrapped.Day)));
        _wrapWin.Present(sheet, cues, acts, f.Thin ? "" : SharePicture.Note, offscreen: _demo);
    }

    /// <summary>
    /// The sheet as drawn, and its sequence: the head (the mark, WEEK WRAPPED and the days, who it is for); then the total
    /// big, what it is and how it splits between farming and runs, the change on its right; up to three tiles; the
    /// highlights; the site at the foot. A thin sheet says what will appear and when in the total's place.
    /// </summary>
    (Border Sheet, Motion.Cues Cues) BuildWrappedSheet(WrappedFacts f, WrappedText t)
    {
        var cues = new Motion.Cues();
        void Cue(double at, Action<double> step, double ms = CueMs) => cues.Add(at, ms, step);
        var body = new StackPanel();
        var sheet = new Border { Width = WrapWidth, Child = body, CornerRadius = new CornerRadius(16), BorderThickness = new Thickness(1), Padding = new Thickness(28, 24, 28, 22), Tag = "wrapped-sheet" };
        sheet.SetResourceReference(Border.BackgroundProperty, "HeroBg");
        sheet.SetResourceReference(Border.BorderBrushProperty, "HeroLine");
        AutomationProperties.SetName(sheet, WrapLabel(f.Scope));
        TextOptions.SetTextFormattingMode(sheet, TextFormattingMode.Ideal);

        var head = WrapHead(t);
        body.Children.Add(head);
        Cue(0, Motion.Arrive(head, 0, 8));

        if (f.Thin) { WrapThin(body, t, Cue); return (sheet, cues); }

        // the total, counting up, with the change against the period before on its right
        var totalRow = new Grid { Margin = new Thickness(0, 22, 0, 0) };
        totalRow.ColumnDefinitions.Add(new ColumnDefinition()); totalRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var total = PT(t.Total, "Gold", 56, FontWeights.SemiBold, "DisplayFont");
        total.LineHeight = 60; total.LineStackingStrategy = LineStackingStrategy.BlockLineHeight; total.TextTrimming = TextTrimming.None; total.Tag = "wrapped-total";
        total.ToolTip = Amounts.Full(f.Total);
        // as wide as its final figure from the start, so the count-up never moves the row; a figure too wide for it is drawn smaller
        total.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        total.Width = Math.Ceiling(total.DesiredSize.Width);
        totalRow.Children.Add(FitLeft(total));
        Cue(CountAt, Motion.Arrive(total, 0, 14));
        Cue(CountAt, Motion.CountUp(total, f.Total, Fk), CountMs);
        Border? chip = null;
        if (t.Change.Length > 0)
        {
            string tone = t.Tone > 0 ? "Good" : t.Tone < 0 ? "Bad" : "LineStrong";
            var sp = new StackPanel();
            var big = PT(t.Change, t.Tone == 0 ? "Ink" : tone, 16, FontWeights.SemiBold, "DisplayFont"); big.HorizontalAlignment = HorizontalAlignment.Right;
            sp.Children.Add(big);
            var small = PT(t.ChangeLabel, "Muted", 12); small.HorizontalAlignment = HorizontalAlignment.Right; small.Margin = new Thickness(0, 2, 0, 0);
            sp.Children.Add(small);
            chip = new Border { Child = sp, CornerRadius = new CornerRadius(10), BorderThickness = new Thickness(1), Padding = new Thickness(12, 6, 12, 7), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 0), Tag = "wrapped-change" };
            chip.SetResourceReference(Border.BackgroundProperty, "Inset");
            chip.SetResourceReference(Border.BorderBrushProperty, tone);
            Grid.SetColumn(chip, 1); totalRow.Children.Add(chip);
        }
        body.Children.Add(totalRow);

        // what the total is, and its split between farming and the runs
        var what = new TextBlock { FontSize = 14, Margin = new Thickness(2, 6, 0, 0), TextTrimming = TextTrimming.CharacterEllipsis, Tag = "wrapped-split" };
        what.Inlines.Add(PR(t.TotalLabel, "Ink", FontWeights.Medium));
        if (t.Split.Length > 0) { var s = PR("   " + t.Split, "Muted"); s.FontSize = 13; what.Inlines.Add(s); }
        what.ToolTip = "Made farming and in expeditions and events: Kinah plus drops, gathering and rewards at the lowest Market price, " + PriceBook.Cut + ". Bound items and items with no price yet add nothing.";
        body.Children.Add(what);
        Cue(LineAt, Motion.Arrive(what, 0, 8));

        // the tiles: the time, the hour's rate, the days played (the day: its kills or runs)
        if (t.Stats.Count > 0)
        {
            var tiles = new UniformGrid { Rows = 1, Columns = t.Stats.Count, Margin = new Thickness(0, 20, 0, 0) };
            for (int i = 0; i < t.Stats.Count; i++)
            {
                var sp = new StackPanel();
                var fig = PT(t.Stats[i].Fig, "Ink", 22, FontWeights.SemiBold, "DisplayFont"); fig.Tag = "wrapped-stat"; fig.TextTrimming = TextTrimming.None;
                sp.Children.Add(FitLeft(fig));
                var lab = PT(t.Stats[i].Label, "Muted", 12); lab.Margin = new Thickness(0, 3, 0, 0);
                sp.Children.Add(lab);
                var tile = new Border { Child = sp, CornerRadius = new CornerRadius(12), BorderThickness = new Thickness(1), Padding = new Thickness(14, 11, 14, 12),
                    Margin = new Thickness(i == 0 ? 0 : 5, 0, i == t.Stats.Count - 1 ? 0 : 5, 0) };
                tile.SetResourceReference(Border.BackgroundProperty, "Inset");
                tile.SetResourceReference(Border.BorderBrushProperty, "CardLine");
                tiles.Children.Add(tile);
                Cue(TilesAt + i * TileGap, Motion.Arrive(tile, 0, 10));
            }
            body.Children.Add(tiles);
        }

        // the highlights, one after another
        if (t.Rows.Count > 0)
        {
            var list = new StackPanel { Margin = new Thickness(0, 16, 0, 0) };
            for (int j = 0; j < t.Rows.Count; j++)
            {
                var (row, icon) = WrapRow(t.Rows[j], f, j > 0);
                list.Children.Add(row);
                double at = RowsAt + j * RowGap;
                Cue(at, Motion.Arrive(row, 0, 10));
                if (t.Rows[j].Kind == "drop" && f.Drop is { } d)
                    Cue(at + DropAt, Motion.GlowLand(icon, d.Grade is "" or "Common" ? "Gold" : "Grade" + d.Grade), DropMs);
            }
            body.Children.Add(list);
        }
        // the change, stamped once everything else is in
        if (chip != null) Cue(RowsAt + t.Rows.Count * RowGap + ChangeAfter, Motion.Stamp(chip));

        var foot = WrapFoot();
        body.Children.Add(foot);
        Cue(FootAt, Motion.Arrive(foot, 0, 6));
        return (sheet, cues);
    }

    /// <summary>A figure drawn whole at its size, or smaller when its place is narrower (a time or a figure in billions): never cut.</summary>
    static Viewbox FitLeft(FrameworkElement figure) =>
        new() { Child = figure, Stretch = Stretch.Uniform, StretchDirection = StretchDirection.DownOnly, HorizontalAlignment = HorizontalAlignment.Left };

    /// <summary>The head: the mark in its tile, WEEK WRAPPED with the days, and under it the character and the server.</summary>
    FrameworkElement WrapHead(WrappedText t)
    {
        var g = new Grid { Margin = new Thickness(0, 0, 36, 0) };   // clear of the window's cross
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); g.ColumnDefinitions.Add(new ColumnDefinition());
        var tile = new Border { Width = 38, Height = 38, CornerRadius = new CornerRadius(10), BorderThickness = new Thickness(1), VerticalAlignment = VerticalAlignment.Center };
        tile.SetResourceReference(Border.BackgroundProperty, "Inset");
        tile.SetResourceReference(Border.BorderBrushProperty, "CardLine");
        tile.Child = GameArt.Element(WrapArt, 28);
        g.Children.Add(tile);
        var words = new StackPanel { Margin = new Thickness(12, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        var title = new TextBlock { FontSize = 12, TextTrimming = TextTrimming.CharacterEllipsis, Tag = "wrapped-title" };
        title.Inlines.Add(PR(t.Title, "Ink", FontWeights.Bold));   // a heading: ink (colour pass, 2026-11-13); the total under it is the gold
        var range = PR("   " + t.Range, "Muted", FontWeights.Medium); range.FontSize = 12.5; title.Inlines.Add(range);
        words.Children.Add(title);
        var (who, server) = WrappedWho();
        if (who.Length + server.Length > 0)
        {
            var line = new TextBlock { FontSize = 13.5, Margin = new Thickness(0, 3, 0, 0), TextTrimming = TextTrimming.CharacterEllipsis, Tag = "wrapped-who" };
            if (who.Length > 0) line.Inlines.Add(PR(who, "Ink", FontWeights.SemiBold));
            if (server.Length > 0) line.Inlines.Add(PR((who.Length > 0 ? " · " : "") + server, "Muted"));
            line.ToolTip = who.Length > 0 ? $"The figures of {who}, the character counted for now." : null;
            words.Children.Add(line);
        }
        Grid.SetColumn(words, 1); g.Children.Add(words);
        return g;
    }

    /// <summary>A highlight: its picture (the item's own tile for the drop, the game's art for the rest), the label and the name with its line, the figure on the right.</summary>
    (FrameworkElement Row, FrameworkElement Icon) WrapRow(WrappedRow r, WrappedFacts f, bool rule)
    {
        var g = new Grid();
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); g.ColumnDefinitions.Add(new ColumnDefinition()); g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        FrameworkElement icon;
        if (r.Kind == "drop" && f.Drop is { } d) icon = QItemIcon(d.Name, _items.IdOf(d.Name), WrapIcon, round: false, grade: d.Grade);
        else
        {
            var art = r.Kind switch { "zone" => "greatsword", "place" => "dungeon", "run" => "boss", "day" => "daily", _ => "scroll" };
            var b = new Border { Width = WrapIcon, Height = WrapIcon, CornerRadius = new CornerRadius(12), BorderThickness = new Thickness(1), Child = GameArt.Element(art, 30), VerticalAlignment = VerticalAlignment.Center };
            b.SetResourceReference(Border.BackgroundProperty, "Inset");
            b.SetResourceReference(Border.BorderBrushProperty, "CardLine");
            icon = b;
        }
        g.Children.Add(icon);
        var mid = new StackPanel { Margin = new Thickness(14, 0, 12, 0), VerticalAlignment = VerticalAlignment.Center };
        var lab = PT(r.Label, "Muted", 12, FontWeights.Bold); mid.Children.Add(lab);
        // a long name (a 60-letter item, a place in full) takes a second line before it is cut, so the picture names it
        var name = PT(r.Name, "Ink", 14.5, FontWeights.Medium); name.Margin = new Thickness(0, 2, 0, 0); name.ToolTip = r.Name; name.Tag = "wrapped-row";
        name.TextWrapping = TextWrapping.Wrap; name.LineHeight = NameLine; name.LineStackingStrategy = LineStackingStrategy.BlockLineHeight; name.MaxHeight = 2 * NameLine;
        mid.Children.Add(name);
        if (r.Sub.Length > 0) { var sub = PT(r.Sub, "Muted", 12); sub.Margin = new Thickness(0, 2, 0, 0); sub.ToolTip = r.Sub; mid.Children.Add(sub); }
        Grid.SetColumn(mid, 1); g.Children.Add(mid);
        string figTone = r.Kind == "craft" && f.Craft is { } c ? (c.Net >= 0 ? "Good" : "Bad") : "Gold";
        if (r.Fig.Length > 0)
        {
            var fig = PT(r.Fig, figTone, 18, FontWeights.SemiBold, "DisplayFont"); fig.TextTrimming = TextTrimming.None;
            Grid.SetColumn(fig, 2); g.Children.Add(fig);
        }
        var row = new Border { Child = g, Padding = new Thickness(0, 10, 0, 10), BorderThickness = new Thickness(0, rule ? 1 : 0, 0, 0) };
        row.SetResourceReference(Border.BorderBrushProperty, "Line");
        return (row, icon);
    }

    /// <summary>A sheet with too little to wrap: what will appear and when, then what there is so far.</summary>
    static void WrapThin(StackPanel body, WrappedText t, Action<double, Action<double>, double> cue)
    {
        var head = PT(t.ThinHead, "Ink", 22, FontWeights.SemiBold, "DisplayFont");
        head.TextWrapping = TextWrapping.Wrap; head.TextTrimming = TextTrimming.None; head.Margin = new Thickness(0, 22, 0, 0); head.Tag = "wrapped-thin";
        body.Children.Add(head);
        cue(ThinGap, Motion.Arrive(head, 0, 10), CueMs);
        var then = PT(t.ThinBody, "Muted", 13.5);
        then.TextWrapping = TextWrapping.Wrap; then.TextTrimming = TextTrimming.None; then.Margin = new Thickness(0, 8, 0, 0);
        body.Children.Add(then);
        cue(2 * ThinGap, Motion.Arrive(then, 0, 8), CueMs);
        var g = new Grid();
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); g.ColumnDefinitions.Add(new ColumnDefinition());
        g.Children.Add(GameArt.Element("daily", 28));
        var now = PT(t.ThinSoFar, "Ink", 13); now.TextWrapping = TextWrapping.Wrap; now.TextTrimming = TextTrimming.None; now.Margin = new Thickness(12, 0, 0, 0);
        Grid.SetColumn(now, 1); g.Children.Add(now);
        var box = new Border { Child = g, Margin = new Thickness(0, 18, 0, 0), Padding = new Thickness(14, 12, 14, 12), CornerRadius = new CornerRadius(12), BorderThickness = new Thickness(1) };
        box.SetResourceReference(Border.BackgroundProperty, "Inset");
        box.SetResourceReference(Border.BorderBrushProperty, "CardLine");
        body.Children.Add(box);
        cue(3 * ThinGap, Motion.Arrive(box, 0, 8), CueMs);
        var foot = WrapFoot();
        body.Children.Add(foot);
        cue(4 * ThinGap, Motion.Arrive(foot, 0, 6), CueMs);
    }

    /// <summary>The foot: Kinah Ledger and the site.</summary>
    static FrameworkElement WrapFoot()
    {
        var g = new Grid { Margin = new Thickness(0, 16, 0, 0) };
        g.ColumnDefinitions.Add(new ColumnDefinition()); g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        g.Children.Add(PT("Kinah Ledger", "Faint", 12, FontWeights.Medium));
        var site = PT(SharePicture.Site, "Muted", 12, FontWeights.Medium);
        Grid.SetColumn(site, 1); g.Children.Add(site);
        return g;
    }

    /* ---------------- the picture ---------------- */

    /// <summary>The open sheet, settled, as a picture at twice its size: drawn again on its own from what the sheet shows, square-cornered for the file.</summary>
    BitmapSource? RenderWrapped()
    {
        if (_wrapShown is not { } shown) return null;
        var (sheet, cues) = BuildWrappedSheet(shown.Facts, shown.Text);
        cues.Seek(cues.Length);
        sheet.CornerRadius = new CornerRadius(0);
        sheet.Measure(new Size(WrapWidth, double.PositiveInfinity));
        sheet.Arrange(new Rect(sheet.DesiredSize));
        sheet.UpdateLayout();
        var rtb = new RenderTargetBitmap((int)Math.Ceiling(sheet.ActualWidth * 2), (int)Math.Ceiling(sheet.ActualHeight * 2), 192, 192, PixelFormats.Pbgra32);
        rtb.Render(sheet);
        rtb.Freeze();
        return rtb;
    }

    void CopyWrapped()
    {
        if (RenderWrapped() is not { } bmp) return;
        try { CopyPng(bmp); _wrapWin?.SetNote("Copied. Paste it in Discord or anywhere with Ctrl+V."); }
        catch (Exception ex) { CrashLog.Write("wrapped picture", ex); _wrapWin?.SetNote("Couldn't copy the picture: " + ex.Message); }
    }

    void SaveWrapped()
    {
        if (RenderWrapped() is not { } bmp || _wrapShown is not { } shown) return;
        try
        {
            var path = SavePicture(bmp, Wrapped.FileName(shown.Facts.Scope, DateTime.Now));
            _wrapWin?.SetNote(@"Saved in Pictures\Kinah Ledger as " + System.IO.Path.GetFileName(path));
        }
        catch (Exception ex) { CrashLog.Write("wrapped save", ex); _wrapWin?.SetNote("Couldn't save the picture: " + ex.Message); }
    }

    /* ---------------- layout renders and the self-test ---------------- */

    /// <summary>
    /// The sheet's sample for a layout render (ui-shot --wrapped): the demo's farming days with a best drop a zone, the week
    /// before them (farming and runs, so the change has something to stand against) and a morning's crafting. A state with
    /// runs of its own is an Events page state: its file says nothing of farming, so the sheet farms nothing, and history
    /// begins with its first run. Example figures, never the player's.
    /// </summary>
    internal void ShowWrappedSample(UiState? state)
    {
        if (state?.Runs != null)
        {
            var first = _demoWeekRuns?.Select(r => DayHistory.LocalStart(r).Date).DefaultIfEmpty(DateTime.Today).Min() ?? DateTime.Today;
            _demoDay = null;
            _demoDays = new DayFile { Since = DayStore.DayOf(first) };
            return;
        }
        if (_demoDays == null || _demoInst == null) return;
        DemoBest(_demoDays);
        var today = DateTime.Today;
        foreach (var (ago, zone, mins, kills, made) in new[]
        {
            (7, "Western Cantas Valley", 46, 171, 640_000L), (8, "Eastern Cantas Valley", 38, 130, 495_000L), (9, "Altamia Canyon", 52, 180, 610_000L),
            (10, "Kraka Footpath", 63, 240, 905_000L), (12, "Western Cantas Valley", 30, 112, 420_000L), (13, "Pilgrim's Ridge", 44, 160, 560_000L),
        })
            _demoDays.Days.Add(new FarmDay { Day = DayStore.DayOf(today.AddDays(-ago)), Made = made, Kinah = made * 7 / 10, Active = mins * 60, Kills = kills,
                Zones = { new FarmDayZone { Zone = zone, Made = made, Active = mins * 60, Kills = kills } } });
        // the week before's runs: Urugugu Canyon most evenings and Vakron twice
        var runs = _demoWeekRuns ??= _demoInst.Snapshot();
        foreach (var (ago, zone, kind, count, kinah) in new[] { (7, "Urugugu Canyon", "Expedition", 8, 560_000L), (8, "Urugugu Canyon", "Expedition", 6, 552_000L), (9, "Vakron Sky Island", InstanceLog.Transcendence, 4, 600_000L),
            (10, "Urugugu Canyon", "Expedition", 7, 571_000L), (11, "Urugugu Canyon", "Expedition", 5, 548_000L), (12, "Vakron Sky Island", InstanceLog.Transcendence, 3, 590_000L), (13, "Urugugu Canyon", "Expedition", 6, 566_000L) })
            for (int i = 0; i < count; i++)
                runs.Add(SampleRun(today.AddDays(-ago).AddHours(18).AddMinutes(i * 11), 450, kinah + (i % 3) * 18_000, Array.Empty<(string, int)>(), zone: zone, kind: kind));
        _demoCraft = new WrappedCraft(686_400, 12, false);
    }

    /// <summary>For a layout render: the sheet opened off the screens (KL_MOTION_AT holds its sequence), and the window's face to draw.</summary>
    internal FrameworkElement? OpenWrappedForShot(string scope)
    {
        OpenWrapped(scope);
        return _wrapWin?.Content as FrameworkElement;
    }

    /// <summary>Closes the sheet, if one is open: another character's numbers are about to load, or a render or test is done with it.</summary>
    internal void CloseWrapped() => _wrapWin?.Close();

    internal WrappedFacts WrappedFactsForTest(string scope) => WrappedFor(scope);

    /// <summary>For options-test: the open sheet's texts by their tags (the total, the split, the tiles, the highlights' names, the change, a thin sheet's sentence) and its buttons.</summary>
    internal (string Total, string Split, List<string> Stats, List<string> Rows, string Change, string Thin, List<string> Buttons) WrappedSheetForTest()
    {
        if (_wrapWin?.Content is not DependencyObject root) return ("", "", new(), new(), "", "", new());
        var all = WeekWalk(root).ToList();
        string Tagged(string tag) => all.OfType<TextBlock>().FirstOrDefault(x => x.Tag as string == tag) is { } tb ? InlineText(tb) : "";
        var change = all.OfType<Border>().FirstOrDefault(b => b.Tag as string == "wrapped-change") is { Child: Panel p } ? string.Join(" ", p.Children.OfType<TextBlock>().Select(x => x.Text)) : "";
        var buttons = all.OfType<Button>().Select(b => AutomationProperties.GetName(b)).Where(x => x.Length > 0).ToList();
        return (Tagged("wrapped-total"), Tagged("wrapped-split"), all.OfType<TextBlock>().Where(x => x.Tag as string == "wrapped-stat").Select(x => x.Text).ToList(),
            all.OfType<TextBlock>().Where(x => x.Tag as string == "wrapped-row").Select(x => x.Text).ToList(), change, Tagged("wrapped-thin"), buttons);
    }

    /// <summary>For options-test: a click on a Wrapped button, through its own handler (LOOT's at the foot of its side, or the Events strip's).</summary>
    internal bool ClickWrapButtonForTest(bool events)
    {
        if (WrapButton(events) is not { } b) return false;
        b.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent, b));
        return true;
    }

    /// <summary>For options-test: what a Wrapped button says (LOOT's, or the Events strip's); "" with none.</summary>
    internal string WrapButtonForTest(bool events) => WrapButton(events)?.Tag is string tag ? WrapLabel(tag["wrapped-".Length..]) : "";

    /// <summary>The Events strip's Wrapped button, or the one at the foot of LOOT's side; null when LOOT has none.</summary>
    Button? WrapButton(bool events) => events ? TodayWrapBtn : WeekWalk(LootHost).OfType<Button>().FirstOrDefault(x => (x.Tag as string ?? "").StartsWith("wrapped-"));
}
