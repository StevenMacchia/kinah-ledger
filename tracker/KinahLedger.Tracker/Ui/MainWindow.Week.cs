using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using KinahLedger.Session;

namespace KinahLedger.Ui;

/// <summary>
/// History (0.9, History A of kinah-bench/mock-goals-history, "a Week side on the switch", Steven's pick 2026-10-29). LOOT's
/// switch on the Farming page gains a third side, Week: the week's made, time and rate on one line, then seven gold bars, the
/// last seven local days ending today, each with what it made over it and its day and farming time under it. A click on a
/// bar picks its day (today by default): its kills, its hour and its total, and the zones it farmed as chips with what each
/// made. The Events page's Today strip gains the same switch: blue bars of the runs' Kinah a day, the runs under each, and
/// the picked day's runs by name. A day with nothing is a flat line; a day before farming history began says "–". The days
/// come from DayHistory (days.json, the run log), today live from the day engine. Display only, on this PC.
/// </summary>
public partial class MainWindow
{
    /// <summary>True while LOOT shows the Week side (over Today and the first side). Kept while the tracker runs.</summary>
    bool _lootWeek;
    /// <summary>LOOT's picked day on the Week side; null for today.</summary>
    DateTime? _weekPick;
    /// <summary>True while the Events strip shows the week; its picked day, null for today.</summary>
    bool _runsWeek;
    DateTime? _runsPick;
    /// <summary>The Week side's sample days for layout renders (never the player's); null with none.</summary>
    DayFile? _demoDays;
    /// <summary>The Events render's sample runs over seven days (its own three days and four more); never the player's.</summary>
    List<InstanceRun>? _demoWeekRuns;

    const double WeekArea = 150, WeekTallest = 120, WeekBarMax = 58;

    void InitWeek()
    {
        LootWeekBtn.Click += (_, _) => PickLootWeek();
        TodayDayBtn.Click += (_, _) => PickRunsWeek(false);
        TodayWeekBtn.Click += (_, _) => PickRunsWeek(true);
    }

    void PickLootWeek() { _lootWeek = true; _drawnSig.Remove("loot"); FillLoot(); }

    /// <summary>Today or the week on the Events strip; picking a side opens the strip to show it.</summary>
    void PickRunsWeek(bool week)
    {
        _runsWeek = week;
        if (!_prefs.TodayOpen) { _prefs.TodayOpen = true; if (!_demo) _prefs.Save(); }
        _drawnSig.Remove("today"); _drawnSig.Remove("today-line");
        FillToday();
    }

    /// <summary>The kept days: days.json, or a layout render's sample (none: history begins today).</summary>
    DayFile WeekFile() => _demo ? _demoDays ?? new DayFile { Since = DayStore.Today } : DayHistory.Current();

    /// <summary>Every run the Events page knows, the ones a pending Clear or Remove hides left out; in a render, its sample week.</summary>
    List<InstanceRun> WeekRunList()
    {
        if (_demo && _demoWeekRuns != null) return _demoWeekRuns;
        var log = _watcher.Chat?.Instances ?? _demoInst;
        if (log == null) return new();
        var all = log.Snapshot().Where(r => !_hiddenRuns.Contains(RunKey(r.Start, r.Zone))).ToList();
        RunFixes.ApplyKept(all);
        return all;
    }

    List<HistoryDay> FarmWeekDays() => DayHistory.Days(7, TodayEngine, _items, null, WeekFile());
    List<HistoryDay> RunsWeekDays() => DayHistory.Days(7, null, _items, WeekRunList(), WeekFile());

    /// <summary>True when one of the six days before today had farming: LOOT then shows before the day's first kill, for its Week side.</summary>
    bool FarmedThisWeek()
    {
        if (_demo && _demoDays == null) return false;
        var first = DayStore.DayOf(DateTime.Today.AddDays(-6));
        return WeekFile().Days.Any(d => string.CompareOrdinal(d.Day, first) >= 0 && d.Day != DayStore.Today && d.Any);
    }

    /// <summary>The picked day when it is one of these and had history, else today.</summary>
    static DateTime PickedDay(List<HistoryDay> days, DateTime? pick) =>
        pick is { } p && days.Any(d => d.Date == p.Date && !d.BeforeHistory) ? p.Date : days[^1].Date;

    /// <summary>"Fri 23".</summary>
    static string DayName(DateTime d) => d.ToString("ddd d", CultureInfo.InvariantCulture);
    static string DayLabel(HistoryDay d) => d.IsToday ? "Today" : DayName(d.Date);

    /// <summary>Farming time under a bar: "1 h 45 min", "22 min", "&lt;1 min", "0 min".</summary>
    static string BarTime(double secs) => secs >= 60 ? TodayLoot.Dur(secs) : secs > 0 ? "<1 min" : "0 min";

    /* ---------------- LOOT's Week side ---------------- */

    /// <summary>LOOT's Week side: the week's line, the seven bars, the picked day's line and zones.</summary>
    void FillLootWeek()
    {
        var days = FarmWeekDays();
        var picked = PickedDay(days, _weekPick);
        var t = DayHistory.Totals(days.Where(d => !d.BeforeHistory));
        FootTotalNote.Text = $"{DayName(days[0].Date)} to {DayName(days[^1].Date)}";
        FootTotal.Text = t.Made > 0 ? Fk(t.Made) : " ";
        FootTotal.ToolTip = t.Made <= 0 ? null
            : "Made farming in the last 7 days on this PC: Kinah plus drops and gathering at the lowest Market price, after the Market's cut (each earlier day as priced that day). Bound items and items with no price yet add nothing.";
        var pd = days.First(d => d.Date == picked);
        // Compare (0.9.3, Ui/MainWindow.Compare.cs): with it on, Days | Zones over the bars; Zones ranks the week's zones
        if (_prefs.CompareZones && _lootZones) { FillLootZones(days); return; }
        var sig = "week|" + string.Join(";", days.Select(d => $"{d.Key}:{d.BeforeHistory}:{d.Farm.Made}:{(int)(d.Farm.Active / 60)}:{d.Farm.Kills}:{d.Farm.Attempts}"))
            + $"|{picked:yyyyMMdd}|{string.Join(",", pd.Farm.Zones.Select(z => $"{z.Zone}:{z.Made}:{(int)(z.Active / 60)}:{z.Kills}"))}|{Skins.Current}";
        if (!Changed("loot", sig)) return;
        LootHost.Children.Clear();
        if (_prefs.CompareZones) LootHost.Children.Add(CmpSwitch(false, false));
        var right = new List<(string, string)>();
        if (t.Active >= 60) { right.Add((TodayLoot.Dur(t.Active), "")); if (t.Made > 0) right.Add((Fk(t.Made / (t.Active / 3600)), " an hour")); }
        LootHost.Children.Add(WeekSum(Fk(t.Made), "from farming this week", right,
            "Made farming in the last 7 days, the time spent farming, and what it made an hour over that time."));
        LootHost.Children.Add(WeekBars(days, d => d.Farm.Made, d => BarTime(d.Farm.Active), FarmTip, picked, false,
            date => { _weekPick = date; _drawnSig.Remove("loot"); FillLoot(); }));
        // once, under the bars: why the first days say "–" (the first day after them is the day history began)
        if (days.Any(d => d.BeforeHistory))
        {
            var since = days.First(d => !d.BeforeHistory).Date;
            var n = PT($"Farming history starts {(since == DateTime.Today ? "today" : since.ToString("MMM d", CultureInfo.InvariantCulture))}, when 0.9 was installed.", "Faint", 12);
            n.Margin = new Thickness(0, 10, 0, 0); n.TextWrapping = TextWrapping.Wrap; n.TextTrimming = TextTrimming.None;
            LootHost.Children.Add(n);
        }
        LootHost.Children.Add(FarmDayDetail(pd));
    }

    static string FarmTip(HistoryDay d)
    {
        var f = d.Farm;
        if (!f.Any) return $"{DayLabel(d)}: nothing farmed{(d.IsToday ? " yet" : "")}.";
        var what = f.Kills > 0 ? $", {f.Kills:N0} kills" : f.Attempts > 0 ? $", {f.Attempts:N0} attempts" : "";
        return $"{DayLabel(d)}: {Fk(f.Made)} made in {TodayLoot.Dur(f.Active)} of farming{what}. Click to see its zones.";
    }

    /// <summary>The picked day under the bars: its name, kills and hour, its total; then its zones with what each made (read-only).</summary>
    FrameworkElement FarmDayDetail(HistoryDay d)
    {
        var f = d.Farm;
        string line;
        if (!f.Any) line = d.IsToday ? "Nothing farmed yet today." : "Nothing farmed.";
        else
        {
            var parts = new List<string>();
            if (f.Kills > 0) parts.Add($"{f.Kills:N0} {(f.Kills == 1 ? "kill" : "kills")}");
            else if (f.Attempts > 0) parts.Add($"{f.Attempts:N0} {(f.Attempts == 1 ? "attempt" : "attempts")}");
            if (f.Active >= 60 && f.Made > 0) parts.Add($"{Fk(f.Made / (f.Active / 3600))} an hour");
            line = string.Join(" · ", parts);
        }
        // the zones farmed a minute or more, as the Today side's chips (2026-10-28: a zone passed through gets none)
        var chips = f.Zones.Where(z => z.Active >= 60).OrderByDescending(z => z.Made).ThenByDescending(z => z.Active)
            .Select(z => WeekChip(ZoneName(z.Zone), Fk(z.Made), $"{ZoneName(z.Zone)}: {TodayLoot.Dur(z.Active)} farming, {z.Kills:N0} kills, {Fk(z.Made)} made after the Market's cut.")).ToList();
        return DayDetail(DayLabel(d), line, f.Made > 0 ? Fk(f.Made) : "", chips, "Gold");
    }

    /* ---------------- the Events strip's Week side ---------------- */

    /// <summary>The Events strip on Week: the range and the week's run Kinah on its line; opened, the bars and the picked day's runs.</summary>
    void FillRunsWeek(List<HistoryDay> days)
    {
        var t = DayHistory.Totals(days);
        var range = $"{DayName(days[0].Date)} to {DayName(days[^1].Date)}";
        if (Changed("today-line", $"week|{range}|{t.RunKinah}|{t.Runs}"))
        {
            TodayNote.Inlines.Clear();
            TodayNote.Inlines.Add(PR(range, "Faint"));
            TodaySum.Text = t.RunKinah > 0 ? Fk(t.RunKinah) : "";
            TodaySum.ToolTip = t.RunKinah <= 0 ? null : "Kinah from expeditions and events in the last 7 days, each run on the day it started. Runs left out of the averages add nothing.";
            TodayHead.ToolTip = "The last 7 days of expeditions and events on this PC. Click to fold or open.";
        }
        bool open = _prefs.TodayOpen;
        SetSec(TodayChev, TodayBody, open);
        if (!open) { _drawnSig.Remove("today"); return; }
        // Compare (0.9.3, Ui/MainWindow.Compare.cs): with it on, Days | Places over the bars; Places ranks the week's places
        if (_prefs.CompareZones && _runsPlaces) { FillRunsPlaces(days); return; }
        var picked = PickedDay(days, _runsPick);
        var pd = days.First(d => d.Date == picked);
        var sig = "week|" + string.Join(";", days.Select(d => $"{d.Key}:{d.Runs.Runs}:{d.Runs.Counted}:{d.Runs.Kinah}"))
            + $"|{picked:yyyyMMdd}|{string.Join(",", pd.Runs.Names.Select(n => $"{n.Name}:{n.Count}:{n.Kinah}"))}|{Skins.Current}";
        if (!Changed("today", sig)) return;
        TodayHost.Children.Clear();
        var box = new StackPanel { Margin = new Thickness(0, 14, 0, 0) };
        if (_prefs.CompareZones) box.Children.Add(CmpSwitch(true, false));
        var right = new List<(string, string)>();
        if (t.Counted > 0 && t.RunKinah > 0) right.Add((Fk(t.RunKinah / (double)t.Counted), " a run"));
        box.Children.Add(WeekSum(Fk(t.RunKinah), $"from {t.Runs:N0} run{(t.Runs == 1 ? "" : "s")} this week", right,
            "Kinah from the week's runs; a run is the counted runs' average (a run whose Kinah wasn't read, or that looks wrong, is left out)."));
        box.Children.Add(WeekBars(days, d => d.Runs.Kinah, d => $"{d.Runs.Runs:N0} run{(d.Runs.Runs == 1 ? "" : "s")}", RunTip, picked, true,
            date => { _runsPick = date; _drawnSig.Remove("today"); FillToday(); }));
        box.Children.Add(RunDayDetail(pd));
        TodayHost.Children.Add(box);
    }

    static string RunTip(HistoryDay d) => d.Runs.Runs == 0 ? $"{DayLabel(d)}: no runs."
        : $"{DayLabel(d)}: {d.Runs.Runs:N0} run{(d.Runs.Runs == 1 ? "" : "s")}, {Fk(d.Runs.Kinah)} Kinah. Click to see them.";

    /// <summary>The picked day's runs: their kinds and Kinah, then each name with how many ("Urugugu Canyon ×7").</summary>
    FrameworkElement RunDayDetail(HistoryDay d)
    {
        var r = d.Runs;
        var line = r.Runs == 0 ? (d.IsToday ? "No runs yet today." : "No runs.") : DayHistory.KindsLine(r);
        var chips = r.Names.Select(n => WeekChip(n.Name, $"×{n.Count:N0}", $"{n.Name}: {n.Count:N0} run{(n.Count == 1 ? "" : "s")}{(n.Kinah > 0 ? $", {Fk(n.Kinah)} Kinah" : "")}.")).ToList();
        return DayDetail(DayLabel(d), line, r.Kinah > 0 ? Fk(r.Kinah) : "", chips, "Gold");
    }

    /* ---------------- the pieces ---------------- */

    /// <summary>The week on one line: the big figure and what it is, then the time and rate on the right (the mock's .wsum).</summary>
    FrameworkElement WeekSum(string big, string words, List<(string Fig, string Word)> right, string tip)
    {
        var g = new Grid { Margin = new Thickness(0, 2, 0, 14), ToolTip = tip, Background = Brushes.Transparent };
        g.ColumnDefinitions.Add(new ColumnDefinition()); g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var left = new TextBlock { VerticalAlignment = VerticalAlignment.Bottom, TextTrimming = TextTrimming.CharacterEllipsis };
        var b = PR(big, "Ink", FontWeights.Light); b.FontSize = 30; b.SetResourceReference(TextElement.FontFamilyProperty, "DisplayFont");
        left.Inlines.Add(b);
        var w = PR("   " + words, "Muted"); w.FontSize = 12.5; left.Inlines.Add(w);
        g.Children.Add(left);
        if (right.Count > 0)
        {
            // the small line's baseline beside the big figure's
            var r = new TextBlock { FontSize = 12.5, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(12, 0, 0, 5) };
            for (int i = 0; i < right.Count; i++)
            {
                if (i > 0) r.Inlines.Add(PR(" · ", "Muted"));
                var f = PR(right[i].Fig, "Ink"); f.SetResourceReference(TextElement.FontFamilyProperty, "MonoFont"); r.Inlines.Add(f);
                if (right[i].Word.Length > 0) r.Inlines.Add(PR(right[i].Word, "Muted"));
            }
            Grid.SetColumn(r, 1); g.Children.Add(r);
        }
        return g;
    }

    /// <summary>
    /// Seven days as bars (the mock's .wk and .wkx): each day's figure over its bar, its name and a word under it, the picked
    /// one lit. A day with nothing is a flat line; a day before history began says "–" and can't be picked. Each day's whole
    /// column is its click target. Gold for farming, blue for the runs (<paramref name="runs"/>).
    /// </summary>
    Grid WeekBars(List<HistoryDay> days, Func<HistoryDay, long> fig, Func<HistoryDay, string> under, Func<HistoryDay, string> tip, DateTime picked, bool runs, Action<DateTime> pick)
    {
        long max = Math.Max(1, days.Where(d => !d.BeforeHistory).Select(fig).DefaultIfEmpty(0).Max());
        var (fill, edge, onFill, onEdge) = BarBrushes(runs);
        string accent = runs ? "GradeLegend" : "Gold";
        var grid = new Grid { Tag = "week-bars" };
        for (int i = 0; i < days.Count; i++) grid.ColumnDefinitions.Add(new ColumnDefinition());
        for (int i = 0; i < days.Count; i++)
        {
            var d = days[i];
            long v = d.BeforeHistory ? 0 : fig(d);
            bool on = d.Date == picked.Date;
            var col = new StackPanel { Background = Brushes.Transparent, Margin = new Thickness(i == 0 ? 0 : 5, 0, i == days.Count - 1 ? 0 : 5, 0), Tag = d.Key + (on ? "|on" : "") };
            var stack = new StackPanel { VerticalAlignment = VerticalAlignment.Bottom };
            var val = PT(d.BeforeHistory ? "–" : Fk(v), on ? accent : v > 0 ? "Muted" : "Faint", 11.5, null, "MonoFont");
            val.HorizontalAlignment = HorizontalAlignment.Center; val.Margin = new Thickness(0, 0, 0, 6);
            stack.Children.Add(val);
            Border bar;
            if (v > 0)
                bar = new Border
                {
                    Height = Math.Max(4, Math.Round(WeekTallest * v / max)), MaxWidth = WeekBarMax, CornerRadius = new CornerRadius(5, 5, 2, 2),
                    BorderThickness = new Thickness(1), Background = on ? onFill : fill, BorderBrush = on ? onEdge : edge,
                };
            else
            {
                // nothing that day (or no history yet): a thin flat line, not a bar
                bar = new Border { Height = 2, MaxWidth = WeekBarMax, CornerRadius = new CornerRadius(1) };
                bar.SetResourceReference(Border.BackgroundProperty, "LineStrong");
            }
            stack.Children.Add(bar);
            var area = new Grid { Height = WeekArea };
            area.Children.Add(stack);
            col.Children.Add(area);
            var name = PT(DayLabel(d), on ? accent : "Muted", 12, FontWeights.SemiBold);
            name.HorizontalAlignment = HorizontalAlignment.Center; name.Margin = new Thickness(0, 8, 0, 0);
            col.Children.Add(name);
            var word = PT(d.BeforeHistory ? "–" : under(d), "Faint", 11.5);
            word.HorizontalAlignment = HorizontalAlignment.Center; word.Margin = new Thickness(0, 1, 0, 0);
            col.Children.Add(word);
            if (!d.BeforeHistory)
            {
                col.Cursor = Cursors.Hand; col.ToolTip = tip(d);
                ToolTipService.SetInitialShowDelay(col, 400);
                var date = d.Date;
                col.MouseLeftButtonUp += (_, _) => pick(date);
            }
            Grid.SetColumn(col, i);
            grid.Children.Add(col);
        }
        return grid;
    }

    /// <summary>The bars' paint from the look's own colours: gold (the heading gold below it) for farming, the Legend blue for runs; the picked bar solid.</summary>
    (Brush Fill, Brush Edge, Brush OnFill, Brush OnEdge) BarBrushes(bool runs)
    {
        Color Res(string key, Color fallback) => TryFindResource(key) is SolidColorBrush b ? b.Color : fallback;
        static Color A(Color c, byte a) => Color.FromArgb(a, c.R, c.G, c.B);
        static Color Mix(Color c, Color to, double t) => Color.FromRgb((byte)(c.R + (to.R - c.R) * t), (byte)(c.G + (to.G - c.G) * t), (byte)(c.B + (to.B - c.B) * t));
        static Brush Down(Color top, Color bottom) { var g = new LinearGradientBrush(top, bottom, new Point(0, 0), new Point(0, 1)); g.Freeze(); return g; }
        static Brush Solid(Color c) { var s = new SolidColorBrush(c); s.Freeze(); return s; }
        if (runs)
        {
            var blue = Res("GradeLegend", Color.FromRgb(0x5A, 0xA6, 0xFF));
            var light = Mix(blue, Colors.White, 0.35);
            return (Down(A(blue, 0x80), A(blue, 0x24)), Solid(A(blue, 0x73)), Down(light, Mix(blue, Colors.Black, 0.15)), Solid(light));
        }
        var gold = Res("Gold", Color.FromRgb(0xF8, 0xD5, 0x98));
        var deep = Res("SecGold", Color.FromRgb(0xE9, 0xB4, 0x5A));
        // Daylight's gold and heading gold are one colour: the picked bar still fades from a lighter top
        var top = gold == deep ? Mix(gold, Colors.White, 0.3) : gold;
        return (Down(A(gold, 0x6B), A(deep, 0x29)), Solid(A(gold, 0x52)), Down(top, deep), Solid(gold));
    }

    /// <summary>The picked day under the bars, over a rule: "SUN 25", its line, its total on the right; then its chips.</summary>
    FrameworkElement DayDetail(string day, string line, string total, List<Border> chips, string totalBrush)
    {
        var sp = new StackPanel();
        var head = new Grid { Margin = new Thickness(0, 0, 0, chips.Count > 0 ? 10 : 0) };
        head.ColumnDefinitions.Add(new ColumnDefinition()); head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var t = new TextBlock { FontSize = 12, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis, Tag = "week-line" };
        var dn = PR(day.ToUpperInvariant(), "SecGold", FontWeights.Bold); dn.FontSize = 11;
        t.Inlines.Add(dn);
        if (line.Length > 0) t.Inlines.Add(PR("   " + line, "Muted"));
        head.Children.Add(t);
        if (total.Length > 0)
        {
            var v = PT(total, totalBrush, 12.5, null, "MonoFont"); v.Margin = new Thickness(12, 0, 0, 0);
            Grid.SetColumn(v, 1); head.Children.Add(v);
        }
        sp.Children.Add(head);
        if (chips.Count > 0)
        {
            var wrap = new WrapPanel { Tag = "week-chips" };
            foreach (var c in chips) wrap.Children.Add(c);
            sp.Children.Add(wrap);
        }
        var rule = new Border { Child = sp, BorderThickness = new Thickness(0, 1, 0, 0), Padding = new Thickness(0, 14, 0, 0), Margin = new Thickness(0, 16, 0, 0) };
        rule.SetResourceReference(Border.BorderBrushProperty, "Line");
        return rule;
    }

    /// <summary>A read-only chip in the Today side's chip look: a name and its figure.</summary>
    static Border WeekChip(string text, string fig, string tip)
    {
        var sp = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        sp.Children.Add(PT(text, "Ink", 12.5));
        if (fig.Length > 0) { var f = PT(fig, "Muted", 12, null, "MonoFont"); f.Margin = new Thickness(7, 0, 0, 0); sp.Children.Add(f); }
        var b = new Border { Child = sp, Height = 30, Padding = new Thickness(12, 0, 12, 0), CornerRadius = new CornerRadius(15), BorderThickness = new Thickness(1), Margin = new Thickness(0, 0, 8, 6), ToolTip = tip };
        b.SetResourceReference(Border.BackgroundProperty, "ChipOff");
        b.SetResourceReference(Border.BorderBrushProperty, "ChipOffLine");
        ToolTipService.SetInitialShowDelay(b, 400);
        return b;
    }

    /* ---------------- samples, renders and the self-test ---------------- */

    /// <summary>
    /// Sample days for the Week side's renders: six days of farming before the sample Today (DemoDay), example figures,
    /// never the player's. <paramref name="newHistory"/>: history began two days ago, the day before yesterday had nothing.
    /// </summary>
    DayFile DemoDays(bool newHistory = false)
    {
        var today = DateTime.Today;
        var f = new DayFile { Since = DayStore.DayOf(today.AddDays(newHistory ? -2 : -40)) };
        void Day(int ago, params (string Zone, int Mins, int Kills, long Made)[] zones)
        {
            var d = new FarmDay { Day = DayStore.DayOf(today.AddDays(-ago)) };
            foreach (var (zone, mins, kills, made) in zones.OrderByDescending(z => z.Made)) d.Zones.Add(new FarmDayZone { Zone = zone, Active = mins * 60, Kills = kills, Made = made });
            d.Made = zones.Sum(z => z.Made); d.Active = zones.Sum(z => z.Mins * 60.0); d.Kills = zones.Sum(z => z.Kills); d.Kinah = d.Made * 7 / 10;
            f.Days.Add(d);
        }
        if (newHistory)
        {
            Day(1, ("Eastern Cantas Valley", 54, 201, 720_000), ("Western Cantas Valley", 30, 117, 420_000));
            return f;
        }
        Day(6, ("Western Cantas Valley", 30, 104, 380_000));
        Day(5, ("Eastern Cantas Valley", 40, 131, 520_000));
        Day(4, ("Kraka Footpath", 41, 164, 560_000), ("Pilgrim's Ridge", 24, 96, 330_000), ("Ellun River Midstream", 19, 76, 260_000), ("Ellun River Marsh", 13, 52, 170_000), ("Eastern Cantas Valley", 8, 32, 100_000));
        Day(3, ("Altamia Canyon", 22, 77, 240_000));
        Day(2, ("Western Cantas Valley", 51, 188, 690_000), ("Altamia Canyon", 28, 96, 370_000));
        Day(1, ("Eastern Cantas Valley", 54, 201, 720_000), ("Western Cantas Valley", 30, 117, 420_000));
        return f;
    }

    /// <summary>
    /// The Events render's sample week: its own runs (the last three days, DemoRuns) and four days more of expeditions,
    /// Transcendence, events and a world boss. Example figures, never the player's; kept apart so the other Events renders
    /// keep their three days.
    /// </summary>
    List<InstanceRun> DemoWeekRuns(InstanceLog log)
    {
        var runs = log.Snapshot();
        var today = DateTime.Today;
        void Add(int ago, string zone, string kind, int n, long kinah, int hour)
        {
            for (int i = 0; i < n; i++)
            {
                var s = today.AddDays(-ago).AddHours(hour).AddMinutes(i * 11);
                runs.Add(new InstanceRun { Zone = zone, Kind = kind, Start = s.ToUniversalTime(), LastLoot = s.AddMinutes(8).ToUniversalTime(), PaidAt = s.AddMinutes(7.5).ToUniversalTime(), Kinah = kinah + (i % 3) * 21_000, FromPicture = true, Tables = 1 });
            }
        }
        Add(6, "Urugugu Canyon", "Expedition", 7, 566_000, 17); Add(6, "Vakron Sky Island", InstanceLog.Transcendence, 4, 590_000, 20); Add(6, "Shugo Festival", "Event", 2, 0, 12); Add(6, "Kashapa", InstanceLog.WorldBoss, 1, 140_000, 22);
        Add(5, "Urugugu Canyon", "Expedition", 6, 572_000, 18); Add(5, "Vakron Sky Island", InstanceLog.Transcendence, 3, 600_000, 21); Add(5, "Shugo Festival", "Event", 3, 0, 13);
        Add(4, "Urugugu Canyon", "Expedition", 4, 581_000, 19); Add(4, "Shugo Festival", "Event", 4, 0, 11);
        Add(3, "Vakron Sky Island", InstanceLog.Transcendence, 2, 610_000, 20); Add(3, "Urugugu Canyon", "Expedition", 1, 560_000, 21);
        return runs;
    }

    /// <summary>A layout render (KL_SHOT_STATE=week|week-new): LOOT on Week, a past day picked as the mock has it, or history that began two days ago.</summary>
    internal void ShowWeekSample(bool newHistory)
    {
        if (newHistory) _demoDays = DemoDays(newHistory: true);
        _lootWeek = true;
        _weekPick = newHistory ? null : DateTime.Today.AddDays(-4);
    }

    /// <summary>A layout render of the Events page (KL_SHOT_STATE=week --events): the strip on Week, opened, the first day picked as the mock has it.</summary>
    internal void ShowRunsWeekSample()
    {
        _runsWeek = true; _runsPick = DateTime.Today.AddDays(-6);
        _prefs.TodayOpen = true;
        _drawnSig.Remove("today"); _drawnSig.Remove("today-line");
        FillToday();
    }

    internal void PickLootWeekForTest() => PickLootWeek();

    /// <summary>For options-test: the Events strip on Week or Today, as its switch picks it (which opens the strip).</summary>
    internal void PickRunsWeekForTest(bool week) => PickRunsWeek(week);

    /// <summary>For options-test: the Events strip folded or open, so the test can put back what it found.</summary>
    internal bool TodayOpenForTest
    {
        get => _prefs.TodayOpen;
        set { _prefs.TodayOpen = value; _drawnSig.Remove("today"); FillToday(); }
    }

    /// <summary>For options-test: what a text says, its runs put together when it is built of them.</summary>
    internal static string InlineText(TextBlock t) => t.Inlines.Count == 0 ? t.Text : string.Concat(t.Inlines.OfType<Run>().Select(r => r.Text));

    static IEnumerable<FrameworkElement> WeekWalk(DependencyObject d)
    {
        foreach (var c in LogicalTreeHelper.GetChildren(d).OfType<DependencyObject>())
        {
            if (c is FrameworkElement fe) yield return fe;
            foreach (var x in WeekWalk(c)) yield return x;
        }
    }

    /// <summary>
    /// For options-test: the Week side as drawn (LOOT's, or the Events strip's with <paramref name="runs"/>): how many bars,
    /// the picked day's name, the day's line under them, its chips, and the figure over today's bar.
    /// </summary>
    internal (int Bars, string Picked, string Line, List<string> Chips, string TodayFig) WeekForTest(bool runs = false)
    {
        var host = runs ? (FrameworkElement)TodayHost : LootHost;
        var all = WeekWalk(host).ToList();
        var bars = all.OfType<Grid>().FirstOrDefault(g => g.Tag as string == "week-bars");
        if (bars == null) return (0, "", "", new(), "");
        var cols = bars.Children.OfType<StackPanel>().ToList();
        var on = cols.FirstOrDefault(c => (c.Tag as string ?? "").EndsWith("|on"));
        string picked = on?.Children.OfType<TextBlock>().FirstOrDefault()?.Text ?? "";
        string Fig(StackPanel c) => c.Children.OfType<Grid>().FirstOrDefault()?.Children.OfType<StackPanel>().FirstOrDefault()?.Children.OfType<TextBlock>().FirstOrDefault()?.Text ?? "";
        var line = all.OfType<TextBlock>().FirstOrDefault(t => t.Tag as string == "week-line") is { } lt ? InlineText(lt) : "";
        var chips = all.OfType<WrapPanel>().Where(w => w.Tag as string == "week-chips").SelectMany(w => w.Children.OfType<Border>())
            .Select(b => string.Join(" ", ((Panel)b.Child).Children.OfType<TextBlock>().Select(t => t.Text))).ToList();
        return (cols.Count, picked, line, chips, cols.Count > 0 ? Fig(cols[^1]) : "");
    }

    /// <summary>For options-test: a click on day <paramref name="i"/>'s bar (0 the oldest), through the column's own handler.</summary>
    internal void PickWeekDayForTest(int i, bool runs = false)
    {
        var host = runs ? (FrameworkElement)TodayHost : LootHost;
        var bars = WeekWalk(host).OfType<Grid>().FirstOrDefault(g => g.Tag as string == "week-bars");
        if (bars?.Children.OfType<StackPanel>().ElementAtOrDefault(i) is not { } col) return;
        col.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount, MouseButton.Left) { RoutedEvent = UIElement.MouseLeftButtonUpEvent, Source = col });
    }
}
