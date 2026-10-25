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
/// The Events part's live pieces and sheets (0.8.31): the steps of the open run (Entered, In the run, Reward screen,
/// Paid) and the feed lines Now shows inside the place's card (MainWindow.RunsCard.cs); the sheet that opens under a
/// run: for a flagged run its fix ("Looks like 5 runs. Split at the reward screens · Keep as one · Remove", a box to type
/// Kinah that wasn't read, "This wasn't a run · It was a run"), for any other its details and Remove. Every sheet is drawn
/// in the window's look; Remove needs no Windows dialog, the sheet is the second step.
/// </summary>
public partial class MainWindow
{
    /// <summary>The run whose sheet is open ("start|zone"), or null.</summary>
    string? _fixOpen;
    /// <summary>Reward screen lines for a layout render's merged record (example figures); never the player's.</summary>
    List<RunFixes.ScreenLine>? _demoScreens;
    /// <summary>The kept reward screen lines, read once when a sheet opens.</summary>
    List<RunFixes.ScreenLine>? _screenLines;

    static string RunKey(InstanceRun r) => $"{r.Start.Ticks}|{r.Zone}";

    /// <summary>Entered · In the run · Reward screen · Paid, with where the open run stands lit; all grey with no run open.</summary>
    FrameworkElement StepTrack(InstanceRun? open)
    {
        int at = open == null ? -1 : StepOf(open);
        var sp = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 12, 0, 6) };
        var steps = new[] { "Entered", "In the run", "Reward screen", "Paid" };
        for (int i = 0; i < steps.Length; i++)
        {
            bool done = i < at, cur = i == at;
            var dot = new Ellipse { Width = 9, Height = 9, StrokeThickness = 1.5, Margin = new Thickness(0, 0, 6, 0), VerticalAlignment = VerticalAlignment.Center };
            dot.SetResourceReference(Shape.StrokeProperty, cur ? "Gold" : done ? "Muted" : "LineStrong");
            if (cur) dot.SetResourceReference(Shape.FillProperty, "Gold"); else if (done) dot.SetResourceReference(Shape.FillProperty, "Muted");
            sp.Children.Add(dot);
            sp.Children.Add(PT(steps[i], cur ? "Gold" : done ? "Muted" : "Faint", 12, cur ? FontWeights.SemiBold : null));
            if (i < steps.Length - 1) { var r = new Border { Width = 22, Height = 1.5, Margin = new Thickness(8, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center }; r.SetResourceReference(Border.BackgroundProperty, done ? "Muted" : "Line"); sp.Children.Add(r); }
        }
        return sp;
    }

    /// <summary>
    /// The Events feed: the reward lines counted into runs (the live feed), with each run's steps drawn from the run log
    /// (entered, paid) and a flagged record's line ("An older record looks like 5 runs · fix below"); the newest five.
    /// </summary>
    List<FeedLine> EventsFeed(List<InstanceRun> runs, string? zone = null)
    {
        var live = _demo ? _demoFeed.Where(l => l.Tab == FeedTab.Events) : _watcher.Chat?.Live.Recent(FeedTab.Events, 10) ?? new();
        // with a place, its own reward lines only (a line that names no place is kept: it is the open run's)
        var lines = new List<FeedLine>(zone == null ? live : live.Where(l => l.Where.Length == 0 || l.Where.EndsWith(zone, StringComparison.OrdinalIgnoreCase)));
        foreach (var g in runs.GroupBy(r => r.Zone, StringComparer.OrdinalIgnoreCase))
        {
            var list = g.OrderBy(r => r.Start).ToList();
            double med = Median(list.Where(r => !r.OutOfAverages).Select(r => r.Seconds));
            for (int i = 0; i < list.Count; i++)
            {
                var r = list[i];
                // a run that wasn't one never gets steps of its own in the feed ("Run 23 paid 0" was the open bag)
                if (r.Suspect == "not a run") continue;
                var where = $"{r.Kind} · {r.Zone}";
                lines.Add(new FeedLine(r.Start, FeedTab.Events, "step", $"Entered {r.Zone}", 0, $"run {i + 1}", Where: where));
                if (r.PaidAt is { } paid && !r.KinahUnknown)
                    lines.Add(new FeedLine(paid, FeedTab.Events, "step", r.Kinah > 0 ? $"Run {i + 1} paid {Fk(r.Kinah)}" : $"Run {i + 1}: rewards in", r.Kinah, r.FromPicture ? "reward screen read" : "", Where: where));
                if (FlagReason(r, med) is { Length: > 0 } why && r.Suspect == "looks like several runs")
                    lines.Add(new FeedLine(r.Start, FeedTab.Events, "flag", $"An older record looks like {why.Split(' ')[0]} runs", 0, Where: where));
            }
        }
        return lines.OrderByDescending(l => l.At).Take(LiveFeed.Shown).ToList();
    }

    /* ---------------- the sheets ---------------- */

    /// <summary>A sheet under a run, in the look: a flagged run's in the flag's tint, any other run's plain.</summary>
    static (Border Sheet, StackPanel Body) SheetShell(bool flagged)
    {
        var sp = new StackPanel();
        var sheet = new Border { Child = sp, CornerRadius = new CornerRadius(8), BorderThickness = new Thickness(1), Padding = new Thickness(14, 12, 14, 12), Margin = new Thickness(0, 6, 0, 10) };
        sheet.SetResourceReference(Border.BorderBrushProperty, flagged ? "FlagLine" : "LineStrong");
        sheet.SetResourceReference(Border.BackgroundProperty, flagged ? "FlagBg" : "Card2");
        return (sheet, sp);
    }

    /// <summary>A sheet's sentence: the first part in ink, the rest muted.</summary>
    static TextBlock SheetText(string lead, string rest)
    {
        var t = new TextBlock { FontSize = 13, TextWrapping = TextWrapping.Wrap, LineHeight = 19 };
        t.Inlines.Add(PR(lead, "Ink", FontWeights.SemiBold));
        if (rest.Length > 0) t.Inlines.Add(PR(" " + rest, "Muted", FontWeights.Normal));
        return t;
    }

    static Button SheetBtn(string text, bool primary = false, bool quiet = false)
    {
        var b = new Button { Content = text, Height = 32, Padding = new Thickness(primary ? 18 : 14, 0, primary ? 18 : 14, 0), FontSize = 12.5, Margin = new Thickness(0, 0, 8, 0) };
        if (primary) b.SetResourceReference(StyleProperty, "PrimaryButton");
        if (quiet) { b.Background = Brushes.Transparent; b.BorderBrush = Brushes.Transparent; b.SetResourceReference(Control.ForegroundProperty, "Faint"); }
        return b;
    }

    /// <summary>The actions row: the run's own actions on the left, Remove on the right.</summary>
    (Grid Row, StackPanel Left) SheetActions(InstanceRun r, Action redraw, string removeText = "Remove")
    {
        var acts = new Grid { Margin = new Thickness(0, 12, 0, 0) };
        acts.ColumnDefinitions.Add(new ColumnDefinition()); acts.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var left = new StackPanel { Orientation = Orientation.Horizontal }; acts.Children.Add(left);
        var remove = SheetBtn(removeText, quiet: true); remove.Margin = new Thickness(0);
        remove.ToolTip = "It leaves the list and the figures; a copy is kept aside in instances.removed.jsonl.";
        Grid.SetColumn(remove, 1); acts.Children.Add(remove);
        remove.Click += (_, _) => RemoveRun(r, redraw);
        return (acts, left);
    }

    /// <summary>Any run's details under it (when, how long, what it paid and brought) and Remove for one that is wrong.</summary>
    FrameworkElement RunSheet(InstanceRun r, Action redraw)
    {
        var (sheet, sp) = SheetShell(false);
        var local = r.Start.ToLocalTime();
        var paid = r.KinahUnknown ? "its Kinah not read" : r.Kinah > 0 ? $"{r.Kinah:N0} Kinah" : "no Kinah";
        sp.Children.Add(SheetText($"{r.Zone}, {local.ToString("MMM d HH:mm", CultureInfo.InvariantCulture)}.",
            $"{Clock(TimeSpan.FromSeconds(r.Seconds))} long, {paid}, {PiecesText(r)}{(r.SlotsUnread > 0 ? $", {r.SlotsUnread} slot{(r.SlotsUnread == 1 ? "" : "s")} not named" : "")}. Worth {Fk(RunValue(r))} with the Market's prices."));
        var (acts, _) = SheetActions(r, redraw, "Remove, it's wrong");
        sp.Children.Add(acts);
        return sheet;
    }

    /// <summary>
    /// The sheet under a flagged run. Several runs saved as one: the reward screens found inside it (each one's Kinah, start and
    /// length), Split at the reward screens (when at least two are known), Keep as one, Remove. Not a run: what it was, It was a
    /// run, Remove. Kinah not read: a box to type it from the reward screen, Save, Count without Kinah, Remove.
    /// </summary>
    FrameworkElement FixSheet(InstanceRun r, double medianSecs, Action redraw)
    {
        var (sheet, sp) = SheetShell(true);
        var why = FlagReason(r, medianSecs);
        var (acts, left) = SheetActions(r, redraw, r.Suspect == "not a run" ? "Remove it" : "Remove");
        if (r.Suspect == "looks like several runs")
        {
            var screens = RunFixes.ScreensIn(r, _demoInst != null ? _demoScreens ?? new() : _screenLines ??= RunFixes.Load());
            int k = why.Split(' ')[0] is var w && int.TryParse(w, out var kk) ? kk : screens.Count;
            sp.Children.Add(SheetText(k >= 2 ? $"Looks like {k} runs." : "Looks like several runs.",
                screens.Count >= 2 ? $"The reward screen came {screens.Count} times." : "Its reward screens weren't kept (a record from before 0.8.30), so it can't be split here."));
            if (screens.Count >= 2)
            {
                var parts = RunFixes.Split(r, screens);
                var mini = new UniformGrid { Columns = Math.Min(5, parts.Count), Margin = new Thickness(0, 12, 0, 0) };
                foreach (var p in parts)
                {
                    var c = new StackPanel();
                    c.Children.Add(PT(Fk(p.Kinah), "Gold", 12.5, null, "MonoFont"));
                    c.Children.Add(PT($"{p.Start.ToLocalTime():HH:mm} · {Clock(TimeSpan.FromSeconds(p.Seconds))}", "Faint", 11, null, "MonoFont"));
                    var cell = new Border { Child = c, CornerRadius = new CornerRadius(6), BorderThickness = new Thickness(1), Padding = new Thickness(8, 6, 8, 6), Margin = new Thickness(0, 0, 4, 4) };
                    cell.SetResourceReference(Border.BorderBrushProperty, "FlagLine"); cell.SetResourceReference(Border.BackgroundProperty, "Card");
                    mini.Children.Add(cell);
                }
                sp.Children.Add(mini);
                var split = SheetBtn("Split at the reward screens", primary: true);
                split.Click += (_, _) => SplitRun(r, parts, redraw);
                left.Children.Add(split);
            }
            var keep = SheetBtn("Keep as one");
            keep.Click += (_, _) => { if (_demoInst != null) return; RunFixes.KeepAsOne(r); _fixOpen = null; Say($"Kept as one run: {r.Zone}, {r.Start.ToLocalTime():MMM d HH:mm}. It counts in the figures now.", "Good"); redraw(); };
            left.Children.Add(keep);
        }
        else if (r.Suspect == "not a run")
        {
            // the bag after leaving the dungeon read as a reward screen (2026-10-24 19:03:54), or another screen moments after entering
            sp.Children.Add(SheetText("This wasn't a run.",
                r.Seconds < InstanceLog.FalseRunSeconds ? "Another reward screen was read as its table moments after entering. It's left out of the figures."
                                                        : "Its \"reward screen\" named nothing and showed no Kinah: another window, like your bag, was read as one. It's left out of the figures."));
            var itWas = SheetBtn("It was a run");
            itWas.ToolTip = "Count it in the figures as it stands.";
            itWas.Click += (_, _) => { if (_demoInst != null) return; RunFixes.KeepAsOne(r); _fixOpen = null; Say($"Counted as a run: {r.Zone}, {r.Start.ToLocalTime():MMM d HH:mm}.", "Good"); redraw(); };
            left.Children.Add(itWas);
        }
        else if (r.PartlyRead && !r.KinahUnknown && !r.OldUnread)
        {
            // partly read (0.8.30, the event cards): what was read was sent, marked partly read; nothing to type
            sp.Children.Add(SheetText($"Partly read: {r.SlotsUnread} slot{(r.SlotsUnread == 1 ? "" : "s")} on its reward cards weren't read.",
                "What was read was sent, marked partly read, and the run is left out of the figures. Remove it if the run is wrong."));
        }
        else
        {
            sp.Children.Add(SheetText(r.RewardsNotRead ? "Its reward screen wasn't read." : "Kinah wasn't read.", "Type it from the reward screen."));
            var box = new TextBox { Width = 150, Height = 32, FontSize = 12.5, Margin = new Thickness(0, 0, 8, 0), Tag = "Kinah", ToolTip = "The Kinah on the run's reward screen" };
            System.Windows.Automation.AutomationProperties.SetName(box, "Kinah from the reward screen");
            left.Children.Add(box);
            var save = SheetBtn("Save", primary: true);
            save.Click += (_, _) =>
            {
                long n = long.TryParse(new string(box.Text.Where(char.IsDigit).ToArray()), out var x) ? x : 0;
                if (n <= 0) { Say("Type the Kinah from the run's reward screen first.", "Warn"); return; }
                FixKinah(r, n, redraw);
            };
            left.Children.Add(save);
            var without = SheetBtn("Count without Kinah");
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
