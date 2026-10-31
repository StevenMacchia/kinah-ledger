using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using KinahLedger.Data;
using KinahLedger.Session;
using Cmp = KinahLedger.Session.ZoneCompare;
using Path = System.Windows.Shapes.Path;

namespace KinahLedger.Ui;

/// <summary>
/// Compare (0.9.3, Compare A and B of the kinah-bench canvas, Steven 2026-10-31: "do A and B but make it so its more
/// optional only for people who REALLY care — base app should be light"). All of it waits behind one switch, Options ›
/// General › Compare zones and runs, off by default; off, the window is exactly 0.9.2's.
///   A: LOOT's Week side gets Days | Zones and the Events strip's Week side Days | Places. Zones lists the week's zones
///      (made an hour, time there) and Places its expeditions and events (worth a run, runs), best first, each with a bar,
///      the one here now marked. A row's tick picks it (two at most; the zone here is picked to start with); two picked
///      show side by side with one plain verdict line. A zone under ten minutes this week is left out.
///   B: under the hour on the Farming card, "Your usual here" and "Your best here" once there are ten minutes here today
///      and thirty on earlier days; the Events page's last closed run, against your average and best there and its rank,
///      once the place has three earlier counted runs.
/// Everything is visible click targets in the card look (no menus). From days.json and the run log on this PC
/// (Session/Compare.cs); nothing is sent, and no accuracy figure is shown.
/// </summary>
public partial class MainWindow
{
    /// <summary>LOOT's Week side on Zones (true) or Days; the Events strip's Week side on Places or Days. Kept while the tracker runs.</summary>
    bool _lootZones, _runsPlaces;
    /// <summary>The rows picked to compare, and whether the player has ticked any yet (until then the one here now is picked).</summary>
    List<string> _zonePicks = new(), _placePicks = new();
    bool _zonePicksTouched, _placePicksTouched;

    const double CmpNameW = 178, CmpFigW = 58, CmpSubW = 62, CmpGap = 10;

    /// <summary>Options › Compare zones and runs, saved with the rest: every part that shows it is drawn again.</summary>
    void SetCompare(bool on)
    {
        _prefs.CompareZones = on;
        if (!on) UsualBox.Visibility = Visibility.Collapsed;
        foreach (var k in new[] { "loot", "today", "today-line", "runs" }) _drawnSig.Remove(k);
        Redraw();
        if (_part == "Events") ShowInstances();   // else the Events page draws afresh when shown, its "runs" drawing forgotten
    }

    /// <summary>The zone the player is in: the live zone view's, a render's sample zone, else none.</summary>
    string? HereZone() => _demo ? _demoZone : _watcher.Chat?.ZoneView?.Zone ?? _watcher.Chat?.Zone;

    /* ---------------- the Days | Zones switch ---------------- */

    /// <summary>The small switch at the top of a Week side: Days | Zones (or Places), and what the rows show on the right.</summary>
    FrameworkElement CmpSwitch(bool runs, bool on)
    {
        var g = new Grid { Margin = new Thickness(0, 2, 0, 14), Tag = "cmp-switch" };
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); g.ColumnDefinitions.Add(new ColumnDefinition());
        var seg = new Border { Padding = new Thickness(3), CornerRadius = new CornerRadius(12), BorderThickness = new Thickness(1), HorizontalAlignment = HorizontalAlignment.Left };
        seg.SetResourceReference(Border.BorderBrushProperty, "LineStrong");
        seg.SetResourceReference(Border.BackgroundProperty, "SegGroup");
        var sp = new StackPanel { Orientation = Orientation.Horizontal };
        Button B(string text, bool lit, string tip, Action click)
        {
            var b = new Button { Content = text, Height = 26, FontSize = 12.5, Padding = new Thickness(10, 0, 10, 0), Style = (Style)FindResource(lit ? "SegOn" : "SegOff"), ToolTip = tip };
            b.Click += (_, _) => click();
            return b;
        }
        var days = B("Days", !on, runs ? "The week's runs a bar a day." : "The week's farming a bar a day.", () => PickCmp(runs, false));
        var other = B(runs ? "Places" : "Zones", on, runs ? "Each expedition and event this week, best a run first. Tick two to compare them." : "Each zone you farmed 10 minutes or more this week, best an hour first. Tick two to compare them.", () => PickCmp(runs, true));
        other.Margin = new Thickness(2, 0, 0, 0);
        sp.Children.Add(days); sp.Children.Add(other);
        seg.Child = sp;
        g.Children.Add(seg);
        if (on)
        {
            var l = PT(runs ? "Kinah a run · runs" : "Kinah an hour · time there", "Faint", 12);
            l.HorizontalAlignment = HorizontalAlignment.Right;
            Grid.SetColumn(l, 1); g.Children.Add(l);
        }
        return g;
    }

    void PickCmp(bool runs, bool on)
    {
        if (runs) { _runsPlaces = on; _drawnSig.Remove("today"); FillToday(); }
        else { _lootZones = on; _drawnSig.Remove("loot"); FillLoot(); }
    }

    /* ---------------- A: Zones ---------------- */

    /// <summary>LOOT's Week side on Zones: the week's zones best an hour first, the picks side by side, the verdict.</summary>
    void FillLootZones(List<HistoryDay> days)
    {
        var zones = Cmp.Zones(days);
        var here = HereZone();
        var picks = Cmp.Picks(_zonePicks, _zonePicksTouched, zones.Select(z => z.Zone), here);
        var sig = "zones|" + string.Join(";", zones.Select(z => $"{z.Zone}:{z.Made}:{(int)(z.Active / 60)}:{z.Kills}:{z.BestDrop}")) + $"|{here}|{string.Join(",", picks)}|{Skins.Current}";
        if (!Changed("loot", sig)) return;
        LootHost.Children.Clear();
        LootHost.Children.Add(CmpSwitch(false, true));
        if (zones.Count == 0) { LootEmpty("A zone shows here once you farm it 10 minutes or more this week."); return; }
        double max = Math.Max(1, zones.Max(z => z.PerHour));
        var list = new StackPanel { Tag = "cmp-rows" };
        foreach (var z in zones)
        {
            bool isHere = string.Equals(z.Zone, here, StringComparison.OrdinalIgnoreCase);
            var key = z.Zone;
            list.Children.Add(CmpRow(key, ZoneName(z.Zone), isHere ? "here now" : "", z.PerHour / max, Fk(z.PerHour), BarTime(z.Active), picks.Contains(key, StringComparer.OrdinalIgnoreCase), false,
                $"{ZoneName(z.Zone)}: {Fk(z.Made)} made in {TodayLoot.Dur(z.Active)} of farming this week, {z.Kills:N0} kills. Tick it to compare.",
                () => { _zonePicks = Cmp.Toggle(picks, key); _zonePicksTouched = true; _drawnSig.Remove("loot"); FillLoot(); }));
        }
        LootHost.Children.Add(list);
        var picked = picks.Select(p => zones.First(z => string.Equals(z.Zone, p, StringComparison.OrdinalIgnoreCase))).ToList();
        if (picked.Count < 2) { LootHost.Children.Add(CmpHint(picked.Count == 0 ? "Tick two zones to compare them side by side." : "Tick one more to compare.")); return; }
        // the better hour on the left, as the mock reads
        var (a, b) = picked[0].PerHour >= picked[1].PerHour ? (picked[0], picked[1]) : (picked[1], picked[0]);
        static string Over(ZoneWeek z) => $"{TodayLoot.Dur(z.Active)} over {z.Days} day{(z.Days == 1 ? "" : "s")}";
        LootHost.Children.Add(VsCard(ZoneName(a.Zone), Over(a), ZoneName(b.Zone), Over(b), new()
        {
            ("Kinah an hour", Fk(a.PerHour), Fk(b.PerHour), Win(a.PerHour, b.PerHour)),
            ("Kills an hour", a.Kills > 0 ? Fk(a.KillsHour) : "–", b.Kills > 0 ? Fk(b.KillsHour) : "–", Win(a.KillsHour, b.KillsHour)),
            ("Kinah a kill", a.Kills > 0 ? Fk(a.PerKill) : "–", b.Kills > 0 ? Fk(b.PerKill) : "–", 0),
            ("Best drop", a.BestDrop.Length > 0 ? a.BestDrop : "–", b.BestDrop.Length > 0 ? b.BestDrop : "–", 0),
        }, Win(a.PerHour, b.PerHour) < 0));
        LootHost.Children.Add(Verdict(Cmp.ZoneVerdict(a, b, v => Fk(v))));
    }

    /// <summary>Which side's figure is better: -1 the left, 1 the right, 0 the same or nothing to say.</summary>
    static int Win(double a, double b) => a <= 0 && b <= 0 ? 0 : Math.Abs(a - b) < Math.Max(a, b) * 0.005 ? 0 : a > b ? -1 : 1;

    /* ---------------- A: Places ---------------- */

    /// <summary>The week's runs, each on the local day it started (the Week side's seven days).</summary>
    List<InstanceRun> WeekRuns(List<HistoryDay> days)
    {
        var first = days[0].Date;
        return WeekRunList().Where(r => DayHistory.LocalStart(r).Date >= first).ToList();
    }

    /// <summary>A run's best drop over runs: the sellable material worth most in all, "Fine Odyle ×2" (gear is bound, currencies aren't sold).</summary>
    string RunsBestDrop(IEnumerable<InstanceRun> runs)
    {
        var sum = new Dictionary<long, int>();
        foreach (var r in runs) foreach (var (id, c) in r.Items) sum[id] = sum.GetValueOrDefault(id) + c;
        string best = ""; double bestW = 0;
        foreach (var (id, c) in sum)
        {
            if (_items.IsGear(id) || IsCurrency(id)) continue;
            var name = _items.NameOf(id);
            if (PriceBook.Price(name) is long p && p * c > bestW) { bestW = p * c; best = $"{name} ×{c:N0}"; }
        }
        return best;
    }

    /// <summary>The Events strip's Week side on Places: the week's places best a run first, the picks side by side, the verdict.</summary>
    void FillRunsPlaces(List<HistoryDay> days)
    {
        var runs = WeekRuns(days);
        var places = Cmp.Places(runs, RunValue, RunsBestDrop);
        var live = _demoInst != null ? _demoLive : (_watcher.Chat?.Instances ?? _demoInst)?.InProgress;
        // the place here now (a run open there), else the last one run
        var newest = runs.OrderByDescending(r => r.LastLoot ?? r.Start).FirstOrDefault();
        string? here = live != null ? places.FirstOrDefault(p => string.Equals(p.Place, live, StringComparison.OrdinalIgnoreCase))?.Place : null;
        string? last = newest != null ? Cmp.PlaceOf(newest) : null;
        var picks = Cmp.Picks(_placePicks, _placePicksTouched, places.Select(p => p.Place), here ?? last);
        var sig = "places|" + string.Join(";", places.Select(p => $"{p.Place}:{p.Runs}:{p.Counted}:{p.Value}:{(int)p.Seconds}:{p.BestRun}:{p.BestDrop}")) + $"|{here}|{last}|{string.Join(",", picks)}|{Skins.Current}";
        if (!Changed("today", sig)) return;
        TodayHost.Children.Clear();
        var box = new StackPanel { Margin = new Thickness(0, 14, 0, 0) };
        box.Children.Add(CmpSwitch(true, true));
        if (places.Count == 0)
        {
            var e = PT("A place shows here once one of its runs this week is counted.", "Faint", 12.5); e.TextWrapping = TextWrapping.Wrap; e.TextTrimming = TextTrimming.None;
            box.Children.Add(e); TodayHost.Children.Add(box); return;
        }
        double max = Math.Max(1, places.Max(p => p.PerRun));
        var list = new StackPanel { Tag = "cmp-rows" };
        foreach (var p in places)
        {
            var key = p.Place;
            string mark = string.Equals(key, here, StringComparison.OrdinalIgnoreCase) ? "here now" : here == null && string.Equals(key, last, StringComparison.OrdinalIgnoreCase) ? "last run" : "";
            list.Children.Add(CmpRow(key, key, mark, p.PerRun / max, Fk(p.PerRun), $"{p.Runs:N0} run{(p.Runs == 1 ? "" : "s")}", picks.Contains(key, StringComparer.OrdinalIgnoreCase), true,
                $"{key}: {p.Runs:N0} run{(p.Runs == 1 ? "" : "s")} this week, {Fk(p.Value)} from the {p.Counted:N0} counted. Tick it to compare.",
                () => { _placePicks = Cmp.Toggle(picks, key); _placePicksTouched = true; _drawnSig.Remove("today"); FillToday(); }));
        }
        box.Children.Add(list);
        var picked = picks.Select(k => places.First(p => string.Equals(p.Place, k, StringComparison.OrdinalIgnoreCase))).ToList();
        if (picked.Count < 2) box.Children.Add(CmpHint(picked.Count == 0 ? "Tick two places to compare them side by side." : "Tick one more to compare."));
        else
        {
            var (a, b) = picked[0].PerRun >= picked[1].PerRun ? (picked[0], picked[1]) : (picked[1], picked[0]);
            static string Sub(PlaceWeek p) => $"{p.Counted:N0} run{(p.Counted == 1 ? "" : "s")}" + (p.MinutesARun >= 1 ? $" · {p.MinutesARun:0} min a run" : "");
            box.Children.Add(VsCard(a.Place, Sub(a), b.Place, Sub(b), new()
            {
                ("Kinah a run", Fk(a.PerRun), Fk(b.PerRun), Win(a.PerRun, b.PerRun)),
                ("Kinah an hour", a.PerHour > 0 ? Fk(a.PerHour) : "–", b.PerHour > 0 ? Fk(b.PerHour) : "–", Win(a.PerHour, b.PerHour)),
                ("Best run", Fk(a.BestRun), Fk(b.BestRun), 0),
                ("Best drop", a.BestDrop.Length > 0 ? a.BestDrop : "–", b.BestDrop.Length > 0 ? b.BestDrop : "–", 0),
            }, Win(a.PerRun, b.PerRun) < 0));
            box.Children.Add(Verdict(Cmp.PlaceVerdict(a, b, v => Fk(v))));
        }
        TodayHost.Children.Add(box);
    }

    /* ---------------- the pieces ---------------- */

    /// <summary>
    /// One ranked row (the mock's .zr2): its tick, its name (and "here now"), a bar of its figure against the best, the
    /// figure, and the time or runs. The whole row is the click target; the tick shows what's picked.
    /// </summary>
    FrameworkElement CmpRow(string key, string name, string mark, double frac, string fig, string sub, bool on, bool runs, string tip, Action click)
    {
        var g = new Grid { Height = 30, Background = Brushes.Transparent, Cursor = Cursors.Hand, ToolTip = tip, Tag = "cmp-row|" + key + (on ? "|on" : "") };
        ToolTipService.SetInitialShowDelay(g, 500);
        foreach (var w in new[] { new GridLength(22), new GridLength(CmpNameW + CmpGap), new GridLength(1, GridUnitType.Star), new GridLength(CmpFigW + CmpGap), new GridLength(CmpSubW + CmpGap) })
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = w });
        // the tick: an empty ring, or gold with a dark check
        var tick = new Border { Width = 16, Height = 16, CornerRadius = new CornerRadius(8), BorderThickness = new Thickness(1.5), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center };
        if (on)
        {
            tick.SetResourceReference(Border.BackgroundProperty, "Gold");
            tick.SetResourceReference(Border.BorderBrushProperty, "Gold");
            var check = new Path { Data = Geometry.Parse("M2.5,6.2 L5,8.6 L9.6,3.6"), StrokeThickness = 2, Width = 12, Height = 12, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round, StrokeLineJoin = PenLineJoin.Round, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(-0.5, 0, 0, 0) };
            check.SetResourceReference(Shape.StrokeProperty, "BtnPrimaryInk");
            tick.Child = check;
        }
        else
        {
            tick.Background = Brushes.Transparent;
            tick.SetResourceReference(Border.BorderBrushProperty, "SlotBadgeLine");
        }
        g.Children.Add(tick);
        var t = new TextBlock { FontSize = 12.5, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 0, CmpGap, 0) };
        t.Inlines.Add(PR(name, mark == "here now" ? "Gold" : "Ink"));
        if (mark.Length > 0) { var m = PR("  " + mark, "Faint"); m.FontSize = 11.5; t.Inlines.Add(m); }
        Grid.SetColumn(t, 1); g.Children.Add(t);
        // the bar: a slot-dark track, filled gold (blue for runs) as far as this row's figure goes against the best
        var track = new Border { Height = 8, CornerRadius = new CornerRadius(4), BorderThickness = new Thickness(1), VerticalAlignment = VerticalAlignment.Center, ClipToBounds = true };
        track.SetResourceReference(Border.BackgroundProperty, "Slot");
        track.SetResourceReference(Border.BorderBrushProperty, "ChipOffLine");
        var inner = new Grid();
        double f = Math.Clamp(frac, 0, 1);
        inner.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Math.Max(f, 0.0001), GridUnitType.Star) });
        inner.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Math.Max(1 - f, 0.0001), GridUnitType.Star) });
        if (f > 0) inner.Children.Add(new Border { CornerRadius = new CornerRadius(3), Background = CmpBarFill(runs) });
        track.Child = inner;
        Grid.SetColumn(track, 2); g.Children.Add(track);
        var fv = PT(fig, "Ink", 12.5, null, "MonoFont"); fv.HorizontalAlignment = HorizontalAlignment.Right;
        Grid.SetColumn(fv, 3); g.Children.Add(fv);
        var sv = PT(sub, "Faint", 12.5, null, "MonoFont"); sv.HorizontalAlignment = HorizontalAlignment.Right;
        Grid.SetColumn(sv, 4); g.Children.Add(sv);
        g.MouseEnter += (_, _) => g.SetResourceReference(Panel.BackgroundProperty, "HoverTint");
        g.MouseLeave += (_, _) => g.Background = Brushes.Transparent;
        g.MouseLeftButtonUp += (_, _) => click();
        return g;
    }

    /// <summary>The bar's fill, left to right in the look's own colours: heading gold to gold for zones, the Legend blue for runs.</summary>
    Brush CmpBarFill(bool runs)
    {
        Color Res(string key, Color fallback) => TryFindResource(key) is SolidColorBrush b ? b.Color : fallback;
        Color from, to;
        if (runs) { to = Res("GradeLegend", Color.FromRgb(0x5A, 0xA6, 0xFF)); from = Color.FromRgb((byte)(to.R * 0.7), (byte)(to.G * 0.72), (byte)(to.B * 0.78)); }
        else { from = Res("SecGold", Color.FromRgb(0xE9, 0xB4, 0x5A)); to = Res("Gold", Color.FromRgb(0xF8, 0xD5, 0x98)); }
        var g = new LinearGradientBrush(from, to, new Point(0, 0), new Point(1, 0));
        g.Freeze();
        return g;
    }

    static FrameworkElement CmpHint(string text)
    {
        var t = PT(text, "Faint", 12); t.Margin = new Thickness(0, 10, 0, 2); t.Tag = "cmp-hint";
        return t;
    }

    /// <summary>
    /// Two picks side by side (the mock's .vs): each a card with its name and how much it covers, then the rows, label on
    /// the left and figure on the right; the better figure gold, the side with the better first row ringed in gold.
    /// </summary>
    FrameworkElement VsCard(string aName, string aSub, string bName, string bSub, List<(string Label, string A, string B, int Win)> rows, bool aWins)
    {
        var g = new Grid { Margin = new Thickness(0, 12, 0, 0), Tag = "cmp-vs" };
        g.ColumnDefinitions.Add(new ColumnDefinition()); g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); g.ColumnDefinitions.Add(new ColumnDefinition());
        Border Side(string name, string sub, bool left, bool ring)
        {
            var sp = new StackPanel();
            var h = PT(name, "Ink", 13, FontWeights.SemiBold); sp.Children.Add(h);
            var s = PT(sub, "Faint", 11.5); s.Margin = new Thickness(0, 2, 0, 8); sp.Children.Add(s);
            foreach (var (label, a, b, win) in rows)
            {
                var r = new Grid { Height = 24 };
                r.ColumnDefinitions.Add(new ColumnDefinition()); r.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                r.Children.Add(PT(label, "Muted", 12.5));
                bool best = left ? win < 0 : win > 0;
                var v = PT(left ? a : b, best ? "Gold" : "Ink", 12.5, null, "MonoFont"); v.Margin = new Thickness(10, 0, 0, 0); v.MaxWidth = 150;
                Grid.SetColumn(v, 1); r.Children.Add(v);
                sp.Children.Add(r);
            }
            var box = new Border { Child = sp, CornerRadius = new CornerRadius(8), BorderThickness = new Thickness(1), Padding = new Thickness(14, 11, 14, 9) };
            box.SetResourceReference(Border.BackgroundProperty, "Card2");
            box.SetResourceReference(Border.BorderBrushProperty, ring ? "GoldLine" : "Line");
            return box;
        }
        g.Children.Add(Side(aName, aSub, true, aWins));
        var mid = PT("vs", "Faint", 12); mid.Margin = new Thickness(10, 0, 10, 0); mid.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(mid, 1); g.Children.Add(mid);
        var bs = Side(bName, bSub, false, false); Grid.SetColumn(bs, 2); g.Children.Add(bs);
        return g;
    }

    /// <summary>The one plain verdict line under the pair: its key part in gold.</summary>
    static FrameworkElement Verdict((string Lead, string Bold, string Tail) v)
    {
        var t = new TextBlock { FontSize = 13, Margin = new Thickness(0, 12, 0, 0), TextWrapping = TextWrapping.Wrap, Tag = "cmp-verdict" };
        t.Inlines.Add(PR(v.Lead, "Muted"));
        if (v.Bold.Length > 0) t.Inlines.Add(PR(v.Bold, "Gold", FontWeights.SemiBold));
        if (v.Tail.Length > 0) t.Inlines.Add(PR(v.Tail, "Muted"));
        return t;
    }

    /* ---------------- B: your usual here ---------------- */

    /// <summary>
    /// Under the hour on the Farming card: "Your usual here 152K", how today stands, "Your best here 214K"; only with Compare
    /// on, ten minutes in this zone today and thirty there on earlier days (Session/Compare.cs). True when it shows.
    /// </summary>
    bool FillUsual(string? zone)
    {
        UsualHere? u = null;
        if (_prefs.CompareZones && !string.IsNullOrEmpty(zone) && TodayEngine is { } day
            && TodayLoot.Farm(day, _items).Zones.FirstOrDefault(z => string.Equals(z.Zone, zone, StringComparison.OrdinalIgnoreCase)) is { } tz)
        {
            var today = DayStore.Today;
            u = Cmp.Usual(zone, WeekFile().Days.Where(d => d.Day != today), tz.Made, tz.Active);
        }
        if (u == null) { UsualBox.Visibility = Visibility.Collapsed; return false; }
        UsualLine.Inlines.Clear();
        void Fig(string text) { var r = PR(text, "Ink"); r.SetResourceReference(TextElement.FontFamilyProperty, "MonoFont"); UsualLine.Inlines.Add(r); }
        UsualLine.Inlines.Add(PR("Your usual here ", "Muted")); Fig(Fk(u.Usual));
        var (words, sign) = Cmp.TodayWords(u, v => Fk(v));
        UsualLine.Inlines.Add(new LineBreak());
        UsualLine.Inlines.Add(PR(words, sign > 0 ? "Good" : sign < 0 ? "SecGold" : "Muted"));
        if (u.Best is long best) { UsualLine.Inlines.Add(new LineBreak()); UsualLine.Inlines.Add(PR("Your best here ", "Muted")); Fig(Fk(best)); }
        UsualBox.ToolTip = $"Made an hour in {ZoneName(zone!)} on this PC: your usual over {TodayLoot.Dur(u.EarlierActive)} on earlier days, today's over {TodayLoot.Dur(u.TodayActive)}"
            + (u.Best != null ? ", and your best day there." : ".") + " Kinah plus loot after the Market's cut.";
        UsualBox.Visibility = Visibility.Visible;
        return true;
    }

    /* ---------------- B: a run against your runs there ---------------- */

    /// <summary>
    /// The place card's last closed run against the earlier counted runs there (from the run log): your average, this run,
    /// your best, and "Your 4th best of 20 runs there". Null with Compare off, a run still open or flagged, or fewer than
    /// three earlier runs.
    /// </summary>
    FrameworkElement? RunVsCard(string zone, InstanceRun last)
    {
        if (!_prefs.CompareZones || last.OutOfAverages || last.LastLoot == null) return null;
        var earlier = WeekRunList().Where(r => string.Equals(r.Zone, zone, StringComparison.OrdinalIgnoreCase) && r.Start < last.Start && !r.OutOfAverages).Select(RunValue);
        long value = RunValue(last);
        if (Cmp.VsPlace(value, earlier) is not { } v) return null;
        var sp = new StackPanel { Tag = "cmp-run" };
        var head = new Grid();
        head.ColumnDefinitions.Add(new ColumnDefinition()); head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var t = new TextBlock { VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
        var k = PR("LAST RUN", "SecGold", FontWeights.Bold); k.FontSize = 11; t.Inlines.Add(k);
        var when = (last.LastLoot ?? last.Start).ToLocalTime();
        var w = PR($"   {(when.Date == DateTime.Today ? when.ToString("HH:mm", CultureInfo.InvariantCulture) : when.ToString("MMM d", CultureInfo.InvariantCulture))} · {TodayLoot.Dur(last.Seconds)}", "Faint"); w.FontSize = 12; t.Inlines.Add(w);
        head.Children.Add(t);
        var big = PT(Fk(value), "Gold", 26, FontWeights.Light, "DisplayFont"); big.Margin = new Thickness(12, 0, 0, 0);
        Grid.SetColumn(big, 1); head.Children.Add(big);
        sp.Children.Add(head);
        var row = new Grid { Margin = new Thickness(0, 10, 0, 0) };
        for (int i = 0; i < 3; i++) row.ColumnDefinitions.Add(new ColumnDefinition());
        void Cell(int i, string label, string fig, string brush)
        {
            var c = new StackPanel { Margin = new Thickness(i == 0 ? 0 : 14, 10, 0, 0) };
            c.Children.Add(PT(label, "Faint", 11.5));
            var f = PT(fig, brush, 15, null, "MonoFont"); f.Margin = new Thickness(0, 3, 0, 0); c.Children.Add(f);
            var cell = new Border { Child = c, BorderThickness = new Thickness(i == 0 ? 0 : 1, 0, 0, 0) };
            cell.SetResourceReference(Border.BorderBrushProperty, "Line");
            Grid.SetColumn(cell, i); row.Children.Add(cell);
        }
        long diff = v.Diff;
        bool same = Math.Abs(diff) < Math.Max(1_000, v.Average * 0.03);
        Cell(0, "Your average there", Fk(v.Average), "Ink");
        Cell(1, "This run", same ? "about the same" : $"{Fk(Math.Abs(diff))} {(diff > 0 ? "more" : "less")}", same ? "Ink" : diff > 0 ? "Good" : "SecGold");
        Cell(2, "Your best there", Fk(v.Best), "Ink");
        var rule = new Border { Child = row, BorderThickness = new Thickness(0, 1, 0, 0), Margin = new Thickness(0, 12, 0, 0) };
        rule.SetResourceReference(Border.BorderBrushProperty, "Line");
        sp.Children.Add(rule);
        var line = new TextBlock { FontSize = 13, Margin = new Thickness(0, 12, 0, 0), TextWrapping = TextWrapping.Wrap, Tag = "cmp-rank" };
        line.Inlines.Add(PR("Your ", "Muted"));
        line.Inlines.Add(PR(v.Rank == 1 ? "best yet" : Cmp.Nth(v.Rank) + " best", "Gold", FontWeights.SemiBold));
        line.Inlines.Add(PR($" of {v.Of:N0} {zone} runs.", "Muted"));
        sp.Children.Add(line);
        var card = new Border { Child = sp, CornerRadius = new CornerRadius(8), BorderThickness = new Thickness(1), Padding = new Thickness(16, 14, 16, 14), Margin = new Thickness(20, 16, 20, 0),
            ToolTip = $"This run against your {v.Of - 1:N0} earlier counted runs of {zone} on this PC: Kinah plus materials at the lowest Market price, after the Market's cut, as the figures above count them." };
        card.SetResourceReference(Border.BackgroundProperty, "GoldTint");
        card.SetResourceReference(Border.BorderBrushProperty, "GoldLine");
        ToolTipService.SetInitialShowDelay(card, 500);
        return card;
    }

    /* ---------------- samples, renders and the self-test ---------------- */

    /// <summary>A best drop for each sample zone, so the side-by-side card has one on every day (example figures, never the player's).</summary>
    static void DemoBest(DayFile f)
    {
        var best = new Dictionary<string, (string, int, long)>
        {
            ["Western Cantas Valley"] = ("Spiritstone", 9, 63_000), ["Eastern Cantas Valley"] = ("Thick hide", 14, 42_000), ["Kraka Footpath"] = ("Spiritstone", 4, 28_000),
            ["Pilgrim's Ridge"] = ("Manastone", 3, 15_000), ["Altamia Canyon"] = ("Orichalcum Ore", 8, 16_000), ["Ellun River Midstream"] = ("Thick hide", 5, 15_000), ["Ellun River Marsh"] = ("Fresh Fruit", 6, 6_000),
        };
        foreach (var d in f.Days) foreach (var z in d.Zones) if (best.TryGetValue(z.Zone, out var b)) (z.Best, z.BestCount, z.BestWorth) = b;
    }

    /// <summary>
    /// Layout renders (KL_SHOT_STATE): "week-zones", Compare on and LOOT's Week side on Zones with the zone here and the
    /// best one picked; "compare-on", Compare on with LOOT as it opens, so the Farming card shows your usual here.
    /// </summary>
    internal void ShowCompareSample(string state)
    {
        _prefs.CompareZones = true;   // a render never saves prefs
        if (_demoDays != null) DemoBest(_demoDays);
        if (state != "week-zones") return;
        _lootWeek = true; _lootZones = true;
        var zones = Cmp.Zones(FarmWeekDays());
        _zonePicks = zones.Take(1).Select(z => z.Zone).Concat(new[] { _demoZone ?? "" }).Where(z => z.Length > 0).Distinct().ToList();
        _zonePicksTouched = true;
    }

    /// <summary>
    /// Layout renders of the Events page (KL_SHOT_STATE): "week" the strip's Week side (0.9); "week-places" Compare on and the
    /// strip's Week side on Places, the last place and the best one picked; "run-vs-average" Compare on and no run open, so
    /// the first card's last run shows against your runs there.
    /// </summary>
    internal void ShowEventsStateSample(string state)
    {
        if (state == "week") { ShowRunsWeekSample(); return; }
        _prefs.CompareZones = true;
        if (state == "week-places")
        {
            _runsPlaces = true;
            var places = Cmp.Places(WeekRuns(RunsWeekDays()), RunValue);
            _placePicks = places.Take(1).Select(p => p.Place).Concat(new[] { _demoLive ?? "" }).Where(p => p.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            _placePicksTouched = true;
            ShowRunsWeekSample();
            return;
        }
        if (state == "run-vs-average") { _demoLive = null; _drawnSig.Remove("runs"); ShowInstances(); }
    }

    /// <summary>For options-test: Compare on or off as Save would set it.</summary>
    internal void SetCompareForTest(bool on) => SetCompare(on);

    /// <summary>For options-test: LOOT's Week side on Zones (or Days), as its switch picks it.</summary>
    internal void PickZonesForTest(bool on) => PickCmp(false, on);

    /// <summary>For options-test: a click on row <paramref name="i"/> of Zones, through the row's own handler.</summary>
    internal void PickZoneRowForTest(int i)
    {
        if (WeekWalk(LootHost).OfType<Grid>().Where(g => (g.Tag as string ?? "").StartsWith("cmp-row|")).ElementAtOrDefault(i) is not { } row) return;
        row.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount, MouseButton.Left) { RoutedEvent = UIElement.MouseLeftButtonUpEvent, Source = row });
    }

    /// <summary>For options-test: LOOT as drawn: whether the Days | Zones switch shows, the rows by name, the picked ones, the side-by-side card, the verdict, the usual line.</summary>
    internal (bool Switch, List<string> Rows, List<string> Picked, bool Vs, string Verdict, string Usual) CompareForTest()
    {
        var all = WeekWalk(LootHost).ToList();
        var rows = all.OfType<Grid>().Select(g => g.Tag as string ?? "").Where(t => t.StartsWith("cmp-row|")).ToList();
        var verdict = all.OfType<TextBlock>().FirstOrDefault(t => t.Tag as string == "cmp-verdict") is { } v ? InlineText(v) : "";
        return (all.Any(e => e.Tag as string == "cmp-switch"), rows.Select(r => r.Split('|')[1]).ToList(), rows.Where(r => r.EndsWith("|on")).Select(r => r.Split('|')[1]).ToList(),
            all.Any(e => e.Tag as string == "cmp-vs"), verdict, UsualBox.Visibility == Visibility.Visible ? InlineText(UsualLine) : "");
    }
}
