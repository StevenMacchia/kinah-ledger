using System.Globalization;
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
/// The Events part in direction C (2026-10-24): a live card on top (the place, the last five run steps and reward lines
/// beside the hour across its runs, a line of each run's hour, a run and its length, how many reward screens were read, and
/// where the open run stands: Entered, In the run, Reward screen, Paid), then the Runs cards as shipped. A flagged run opens
/// its fix sheet in place: "Looks like 5 runs. Split at the reward screens · Keep as one · Remove", or for a run whose Kinah
/// wasn't read, a box to type it from the reward screen.
/// </summary>
public partial class MainWindow
{
    /// <summary>The flagged run whose fix sheet is open ("start|zone"), or null.</summary>
    string? _fixOpen;
    /// <summary>Reward screen lines for a layout render's merged record (example figures); never the player's.</summary>
    List<RunFixes.ScreenLine>? _demoScreens;
    /// <summary>The kept reward screen lines, read once when a sheet opens.</summary>
    List<RunFixes.ScreenLine>? _screenLines;

    static string RunKey(InstanceRun r) => $"{r.Start.Ticks}|{r.Zone}";

    /// <summary>The live card for the place being run now, or the last one run: built into EventsLiveHost (called from ShowInstances).</summary>
    void FillEventsLive(List<InstanceRun> runs, string? live)
    {
        EventsLiveHost.Children.Clear();
        _eventsRate = "";
        if (runs.Count == 0) return;
        var zone = live ?? runs.OrderBy(r => r.LastLoot ?? r.Start).Last().Zone;
        var here = runs.Where(r => string.Equals(r.Zone, zone, StringComparison.OrdinalIgnoreCase)).OrderBy(r => r.Start).ToList();
        var counted = here.Where(r => !r.OutOfAverages).ToList();
        var kind = here.Last().Kind;
        double secs = counted.Sum(r => r.Seconds);
        long value = counted.Sum(RunValue);

        var card = new Border { BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(6), Margin = new Thickness(0, 0, 0, 16) };
        card.SetResourceReference(Border.BorderBrushProperty, "LineStrong"); card.SetResourceReference(Border.BackgroundProperty, "Card");
        var body = new StackPanel(); card.Child = body;

        // the head: the place's tile, its name, the kind and where the open run stands; live while a run is open
        var head = new Grid { Margin = new Thickness(20, 16, 20, 0) };
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); head.ColumnDefinitions.Add(new ColumnDefinition()); head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var tile = GlyphTile(40, "M5,21 V7 L12,3 L19,7 V21 Z M9.5,21 V14.5 A2.5,2.5 0 0 1 14.5,14.5 V21", gold: true);
        head.Children.Add(tile);
        var names = new StackPanel { Margin = new Thickness(12, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        var nm = new TextBlock { Text = zone, FontSize = 18, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis };
        nm.SetResourceReference(TextBlock.FontFamilyProperty, "DisplayFont"); nm.SetResourceReference(TextBlock.ForegroundProperty, "Gold");
        names.Children.Add(nm);
        var open = live != null ? here.LastOrDefault(r => r.LastLoot != null && string.Equals(live, r.Zone, StringComparison.OrdinalIgnoreCase) && r == here.Last()) : null;
        var lineTxt = kind.Length > 0 ? kind : "Run";
        if (live != null) lineTxt += $" · run {here.Count}" + (open != null ? $", {Clock((open.LastLoot ?? DateTime.UtcNow) - open.Start)} in" : "");
        else lineTxt += $" · {here.Count} run{(here.Count == 1 ? "" : "s")}";
        var ln = new TextBlock { Text = lineTxt, FontSize = 12.5, Margin = new Thickness(0, 2, 0, 0) }; ln.SetResourceReference(TextBlock.ForegroundProperty, "Muted");
        names.Children.Add(ln);
        Grid.SetColumn(names, 1); head.Children.Add(names);
        if (live != null)
        {
            var lv = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Top };
            var dot = new Ellipse { Width = 6, Height = 6, Margin = new Thickness(0, 1, 6, 0), VerticalAlignment = VerticalAlignment.Center }; dot.SetResourceReference(Shape.FillProperty, "Gold");
            lv.Children.Add(dot);
            var lt = new TextBlock { Text = "live", FontSize = 11.5 }; lt.SetResourceReference(TextBlock.ForegroundProperty, "Gold"); lv.Children.Add(lt);
            Grid.SetColumn(lv, 2); head.Children.Add(lv);
        }
        body.Children.Add(head);

        // the strip: the feed and the hour across the runs
        var now = new Grid { Margin = new Thickness(20, 16, 20, 16) };
        now.ColumnDefinitions.Add(new ColumnDefinition()); now.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(24) }); now.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(200) });
        var left = new StackPanel();
        var box = new Border { CornerRadius = new CornerRadius(8), BorderThickness = new Thickness(1), Padding = new Thickness(0, 4, 0, 4), ClipToBounds = true };
        box.SetResourceReference(Border.BorderBrushProperty, "Line"); box.SetResourceReference(Border.BackgroundProperty, "Slot");
        var host = new StackPanel(); var empty = Faint("Each run step and reward line shows here as it is counted."); empty.Margin = new Thickness(12, 10, 12, 10);
        var g0 = new Grid(); g0.Children.Add(host); g0.Children.Add(empty); box.Child = g0;
        left.Children.Add(box);
        // where the open run stands
        left.Children.Add(StepTrack(open));
        now.Children.Add(left);
        _feedDrawn.Remove(host);
        DrawFeed(host, empty, EventsFeed(runs), FeedTab.Events, PickupValue);

        var rate = new StackPanel(); Grid.SetColumn(rate, 2);
        double perHour = secs > 10 ? value / secs * 3600 : 0;
        _eventsRate = perHour > 0 ? Fk(perHour) : "";
        var big = new TextBlock { Text = perHour > 0 ? Fk(perHour) : " ", FontSize = 34, FontWeight = FontWeights.Light };
        big.SetResourceReference(TextBlock.FontFamilyProperty, "DisplayFont"); big.SetResourceReference(TextBlock.ForegroundProperty, "Ink");
        rate.Children.Add(big);
        var unit = new TextBlock { Text = counted.Count > 0 ? $"an hour, across {counted.Count} run{(counted.Count == 1 ? "" : "s")}" : "an hour, once a run is counted", FontSize = 12.5, Margin = new Thickness(0, 4, 0, 0), TextWrapping = TextWrapping.Wrap };
        unit.SetResourceReference(TextBlock.ForegroundProperty, "Muted");
        rate.Children.Add(unit);
        // each counted run's hour, in order
        if (counted.Count >= 2)
        {
            var line = new RateLine(); var t0 = DateTime.UtcNow.AddMinutes(-counted.Count);
            for (int i = 0; i < counted.Count; i++) line.Add(t0.AddMinutes(i), RunValue(counted[i]) / Math.Max(counted[i].Seconds, 10) * 3600);
            rate.Children.Add(Spark(line));
            var ax = new Grid();
            var a1 = new TextBlock { Text = "run 1", FontSize = 11 }; a1.SetResourceReference(TextBlock.ForegroundProperty, "Faint"); ax.Children.Add(a1);
            var a2 = new TextBlock { Text = $"run {counted.Count}", FontSize = 11, HorizontalAlignment = HorizontalAlignment.Right }; a2.SetResourceReference(TextBlock.ForegroundProperty, "Faint"); ax.Children.Add(a2);
            rate.Children.Add(ax);
        }
        if (counted.Count > 0)
        {
            var kv = new TextBlock { FontSize = 12.5, Margin = new Thickness(0, 12, 0, 0), LineHeight = 21 }; kv.SetResourceReference(TextBlock.ForegroundProperty, "Muted");
            void Fig(string text) { var r = new Run(text); r.SetResourceReference(TextElement.FontFamilyProperty, "MonoFont"); r.SetResourceReference(TextElement.ForegroundProperty, "Ink"); kv.Inlines.Add(r); }
            Fig(Fk(value / (double)counted.Count)); kv.Inlines.Add(new Run(" a run")); kv.Inlines.Add(new LineBreak());
            Fig(Clock(TimeSpan.FromSeconds(secs / counted.Count))); kv.Inlines.Add(new Run(" a run"));
            rate.Children.Add(kv);
        }
        // the health line for runs: how many reward screens were read
        int read = here.Count(r => r.FromPicture && !r.RewardsNotRead && !r.KinahUnknown && !r.PartlyRead);
        var ok = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 12, 0, 0) };
        var mk = new Path { Width = 13, Height = 13, Stretch = Stretch.Uniform, StrokeThickness = 1.6, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round, Data = Geometry.Parse("M3.5,8.5 L6.5,11.5 L12.5,4.5"), Margin = new Thickness(0, 0, 6, 0), VerticalAlignment = VerticalAlignment.Center };
        mk.SetResourceReference(Shape.StrokeProperty, "Gold");
        ok.Children.Add(mk);
        var okt = new TextBlock { Text = $"Reward screens read for {read} of {here.Count}", FontSize = 12, VerticalAlignment = VerticalAlignment.Center }; okt.SetResourceReference(TextBlock.ForegroundProperty, "Faint");
        ok.Children.Add(okt);
        rate.Children.Add(ok);
        now.Children.Add(rate);
        body.Children.Add(now);
        EventsLiveHost.Children.Add(card);
    }

    /// <summary>A small rate line, 200 by 40, gold over a fading fill.</summary>
    static FrameworkElement Spark(RateLine line)
    {
        var c = new Canvas { Width = 200, Height = 40, Margin = new Thickness(0, 12, 0, 4), HorizontalAlignment = HorizontalAlignment.Left, ClipToBounds = true };
        var pts = line.Points(200, 40);
        var fig = new PathFigure { StartPoint = new Point(0, 40), IsClosed = true };
        foreach (var p in pts) fig.Segments.Add(new LineSegment(new Point(p.X, p.Y), false));
        fig.Segments.Add(new LineSegment(new Point(200, 40), false));
        c.Children.Add(new Path { Data = new PathGeometry(new[] { fig }), Fill = new LinearGradientBrush(Color.FromArgb(0x42, 0xF8, 0xD5, 0x98), Color.FromArgb(0, 0xF8, 0xD5, 0x98), 90) });
        var pl = new Polyline { Points = new PointCollection(pts.Select(p => new Point(p.X, p.Y))), StrokeThickness = 1.5, StrokeLineJoin = PenLineJoin.Round };
        pl.SetResourceReference(Shape.StrokeProperty, "Gold");
        c.Children.Add(pl);
        return c;
    }

    /// <summary>Entered · In the run · Reward screen · Paid, with where the open run stands lit; all grey with no run open.</summary>
    FrameworkElement StepTrack(InstanceRun? open)
    {
        int at = open == null ? -1 : open.PaidAt != null ? 3 : open.Tables > 0 ? 2 : open.LastLoot != null ? 1 : 0;
        var sp = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 12, 0, 0) };
        var steps = new[] { "Entered", "In the run", "Reward screen", "Paid" };
        for (int i = 0; i < steps.Length; i++)
        {
            bool done = i < at, cur = i == at;
            var dot = new Ellipse { Width = 8, Height = 8, StrokeThickness = 1, Margin = new Thickness(0, 0, 6, 0), VerticalAlignment = VerticalAlignment.Center };
            dot.SetResourceReference(Shape.StrokeProperty, cur ? "Gold" : done ? "Muted" : "LineStrong");
            if (cur) dot.SetResourceReference(Shape.FillProperty, "Gold"); else if (done) dot.SetResourceReference(Shape.FillProperty, "Muted");
            sp.Children.Add(dot);
            var t = new TextBlock { Text = steps[i], FontSize = 12, VerticalAlignment = VerticalAlignment.Center };
            t.SetResourceReference(TextBlock.ForegroundProperty, cur ? "Gold" : done ? "Muted" : "Faint");
            sp.Children.Add(t);
            if (i < steps.Length - 1) { var r = new Border { Width = 20, Height = 1, Margin = new Thickness(8, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center }; r.SetResourceReference(Border.BackgroundProperty, "Line"); sp.Children.Add(r); }
        }
        return sp;
    }

    /// <summary>
    /// The Events feed: the reward lines counted into runs (the live feed), with each run's steps drawn from the run log
    /// (entered, paid) and a flagged record's line ("An older record looks like 5 runs · fix below"); the newest five.
    /// </summary>
    List<FeedLine> EventsFeed(List<InstanceRun> runs)
    {
        var lines = new List<FeedLine>(_demo ? _demoFeed.Where(l => l.Tab == FeedTab.Events) : _watcher.Chat?.Live.Recent(FeedTab.Events, 10) ?? new());
        foreach (var g in runs.GroupBy(r => r.Zone, StringComparer.OrdinalIgnoreCase))
        {
            var list = g.OrderBy(r => r.Start).ToList();
            double med = Median(list.Where(r => !r.OutOfAverages).Select(r => r.Seconds));
            for (int i = 0; i < list.Count; i++)
            {
                var r = list[i];
                var where = $"{r.Kind} · {r.Zone}";
                lines.Add(new FeedLine(r.Start, FeedTab.Events, "step", $"Entered {r.Zone}", 0, $"run {i + 1}", Where: where));
                if (r.PaidAt is { } paid && !r.KinahUnknown) lines.Add(new FeedLine(paid, FeedTab.Events, "step", $"Run {i + 1} paid {Fk(r.Kinah)}", r.Kinah, r.FromPicture ? "reward screen read" : "", Where: where));
                if (FlagReason(r, med) is { Length: > 0 } why && r.Suspect == "looks like several runs")
                    lines.Add(new FeedLine(r.Start, FeedTab.Events, "flag", $"An older record looks like {why.Split(' ')[0]} runs", 0, Where: where));
            }
        }
        return lines.OrderByDescending(l => l.At).Take(LiveFeed.Shown).ToList();
    }

    /* ---------------- the fix sheet ---------------- */

    /// <summary>
    /// The sheet under a flagged run. Several runs saved as one: the reward screens found inside it (each one's Kinah, start and
    /// length), Split at the reward screens (when at least two are known), Keep as one, Remove. Kinah not read: a box to type
    /// it from the reward screen, Save, Count without Kinah, Remove.
    /// </summary>
    FrameworkElement FixSheet(InstanceRun r, double medianSecs, Action redraw)
    {
        var sp = new StackPanel();
        var sheet = new Border { Child = sp, CornerRadius = new CornerRadius(8), BorderThickness = new Thickness(1), BorderBrush = Ck(CkGold, 0x59), Background = Ck(CkGold, 0x0D), Padding = new Thickness(16, 12, 16, 16), Margin = new Thickness(0, 4, 0, 12) };
        var why = FlagReason(r, medianSecs);
        Button Btn(string text, bool primary = false, bool quiet = false)
        {
            var b = new Button { Content = text, Height = 32, Padding = new Thickness(primary ? 20 : 14, 0, primary ? 20 : 14, 0), FontSize = 12.5, Margin = new Thickness(0, 0, 8, 0) };
            if (primary) b.SetResourceReference(StyleProperty, "PrimaryButton");
            if (quiet) { b.Background = Brushes.Transparent; b.BorderBrush = Brushes.Transparent; b.Foreground = Ck(CkFaint); }
            return b;
        }
        var acts = new Grid { Margin = new Thickness(0, 12, 0, 0) };
        acts.ColumnDefinitions.Add(new ColumnDefinition()); acts.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var left = new StackPanel { Orientation = Orientation.Horizontal }; acts.Children.Add(left);
        var remove = Btn("Remove", quiet: true); remove.Margin = new Thickness(0); Grid.SetColumn(remove, 1); acts.Children.Add(remove);
        remove.Click += (_, _) => { RemoveRun(r, redraw); };
        if (r.Suspect == "looks like several runs")
        {
            var screens = RunFixes.ScreensIn(r, _demoInst != null ? _demoScreens ?? new() : _screenLines ??= RunFixes.Load());
            int k = why.Split(' ')[0] is var w && int.TryParse(w, out var kk) ? kk : screens.Count;
            var title = new TextBlock { FontSize = 13, TextWrapping = TextWrapping.Wrap, Foreground = Ck(CkInk) };
            title.Inlines.Add(new Run(k >= 2 ? $"Looks like {k} runs. " : "Looks like several runs. "));
            title.Inlines.Add(new Run(screens.Count >= 2 ? $"The reward screen came {screens.Count} times." : "Its reward screens weren't kept (a record from before 0.8.30), so it can't be split here.") { Foreground = Ck(CkFaint) });
            sp.Children.Add(title);
            if (screens.Count >= 2)
            {
                var parts = RunFixes.Split(r, screens);
                var mini = new UniformGrid { Columns = Math.Min(5, parts.Count), Margin = new Thickness(0, 12, 0, 0) };
                foreach (var p in parts)
                {
                    var c = new StackPanel();
                    c.Children.Add(CkText(Short(p.Kinah), CkGold, 12.5, mono: true));
                    c.Children.Add(CkText($"{p.Start.ToLocalTime():HH:mm} · {Clock(TimeSpan.FromSeconds(p.Seconds))}", CkFaint, 11, mono: true));
                    mini.Children.Add(new Border { Child = c, CornerRadius = new CornerRadius(6), BorderThickness = new Thickness(1), BorderBrush = Ck(CkGold, 0x47), Background = Ck(CkSlot, 0x80), Padding = new Thickness(8, 6, 8, 6), Margin = new Thickness(0, 0, 4, 4) });
                }
                sp.Children.Add(mini);
                var split = Btn("Split at the reward screens", primary: true);
                split.Click += (_, _) => SplitRun(r, parts, redraw);
                left.Children.Add(split);
            }
            var keep = Btn("Keep as one");
            keep.Click += (_, _) => { if (_demoInst != null) return; RunFixes.KeepAsOne(r); _fixOpen = null; Say($"Kept as one run: {r.Zone}, {r.Start.ToLocalTime():MMM d HH:mm}. It counts in the figures now.", "Good"); redraw(); };
            left.Children.Add(keep);
        }
        else if (r.PartlyRead && !r.KinahUnknown && !r.OldUnread)
        {
            // partly read (0.8.30, the event cards): what was read counts and was sent, marked partly read; nothing to type
            var title = new TextBlock { FontSize = 13, TextWrapping = TextWrapping.Wrap, Foreground = Ck(CkInk) };
            title.Inlines.Add(new Run($"Partly read: {r.SlotsUnread} slot{(r.SlotsUnread == 1 ? "" : "s")} on its reward cards weren't read. "));
            title.Inlines.Add(new Run("What was read counts, and was sent marked partly read. Remove it if the run is wrong.") { Foreground = Ck(CkFaint) });
            sp.Children.Add(title);
        }
        else
        {
            var title = new TextBlock { FontSize = 13, TextWrapping = TextWrapping.Wrap, Foreground = Ck(CkInk) };
            title.Inlines.Add(new Run(r.RewardsNotRead ? "Its reward screen wasn't read. " : "Kinah wasn't read. "));
            title.Inlines.Add(new Run("Type it from the reward screen.") { Foreground = Ck(CkFaint) });
            sp.Children.Add(title);
            var box = new TextBox { Width = 140, Height = 32, FontSize = 12.5, Padding = new Thickness(8, 6, 8, 0), Background = Ck(CkSlot), BorderBrush = Ck(CkFrame), Foreground = Ck(CkGold), CaretBrush = Ck(CkGold), Margin = new Thickness(0, 0, 8, 0), ToolTip = "The Kinah on the run's reward screen" };
            var hint = CkText("Kinah", Color.FromRgb(0x5E, 0x65, 0x88), 12.5); hint.IsHitTestVisible = false; hint.Margin = new Thickness(10, 0, 0, 0);
            box.TextChanged += (_, _) => hint.Visibility = box.Text.Length > 0 ? Visibility.Collapsed : Visibility.Visible;
            var bg = new Grid(); bg.Children.Add(box); bg.Children.Add(hint);
            left.Children.Add(bg);
            var save = Btn("Save", primary: true);
            save.Click += (_, _) =>
            {
                long n = long.TryParse(new string(box.Text.Where(char.IsDigit).ToArray()), out var x) ? x : 0;
                if (n <= 0) { Say("Type the Kinah from the run's reward screen first.", "Warn"); return; }
                FixKinah(r, n, redraw);
            };
            left.Children.Add(save);
            var without = Btn("Count without Kinah");
            without.Click += (_, _) => FixKinah(r, 0, redraw);
            left.Children.Add(without);
        }
        sp.Children.Add(acts);
        return sheet;
    }

    /// <summary>Split: the record replaced by its parts (InstanceLog.ReplaceRun, the record kept aside in instances.repaired.jsonl).</summary>
    void SplitRun(InstanceRun r, List<InstanceRun> parts, Action redraw)
    {
        if (_watcher.Chat?.Instances is not { } il || _demoInst != null) return;
        if (MessageBox.Show(this, $"Split this record ({r.Zone}, {r.Start.ToLocalTime():MMM d HH:mm}) into {parts.Count} runs at its reward screens?\n\nThe record as it was is kept aside in instances.repaired.jsonl.",
                "Kinah Ledger", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        if (il.ReplaceRun(r, parts)) { _fixOpen = null; Say($"Split into {parts.Count} runs. They count in the figures now.", "Good"); }
        redraw();
    }

    /// <summary>The Kinah typed from the reward screen (or none, counted as read with 0): written to the run in instances.jsonl, the flag off.</summary>
    void FixKinah(InstanceRun r, long kinah, Action redraw)
    {
        if (_watcher.Chat?.Instances is not { } il || _demoInst != null) return;
        r.Kinah = kinah; r.KinahNotRead = false; r.OldUnread = false;
        il.Save(r);
        RunFixes.KeepAsOne(r);
        CrashLog.Note($"run fixed by the player: {r.Label} started {r.Start.ToLocalTime():MMM d HH:mm:ss}: Kinah {(kinah > 0 ? kinah.ToString("N0") + " typed from the reward screen" : "counted as none")}");
        _fixOpen = null;
        Say(kinah > 0 ? $"Saved: {kinah:N0} Kinah for that run." : "Counted without Kinah.", "Good");
        redraw();
    }

    /// <summary>For a layout render: the merged record's reward screens (they sum to it), example figures.</summary>
    void DemoScreens(List<InstanceRun> runs)
    {
        var merged = runs.FirstOrDefault(r => r.Seconds > 30 * 60);
        if (merged == null) return;
        var where = $"{merged.Kind} · {merged.Zone}";
        var lines = new List<RunFixes.ScreenLine>();
        var start = merged.Start;
        foreach (var (len, kinah) in new[] { (432, 682_000L), (460, 655_000L), (448, 701_000L), (425, 640_000L), (459, 712_000L) })
        {
            var at = start.AddSeconds(len);
            lines.Add(new RunFixes.ScreenLine(at, where, "kinah", 0, "", kinah));
            lines.Add(new RunFixes.ScreenLine(at.AddSeconds(2), where, "item", _items.IdOf("Spiritstone Powder"), "Spiritstone Powder", 18));
            start = at.AddSeconds(1);
        }
        _demoScreens = lines;
        _fixOpen = RunKey(merged);
    }
}
