using System.Globalization;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using Path = System.Windows.Shapes.Path;
using KinahLedger.Data;
using KinahLedger.Session;

namespace KinahLedger.Ui;

/// <summary>
/// The Events part: one card per place in the game-tooltip look (Steven, 2026-10-24, mock-runs-card.html). The band says
/// the place and its kind, the runs counted, the time, Kinah and value a run and an hour, and "in progress" while a run is
/// open there. Under it the runs, newest first (when, how long, the Kinah, its pieces), a flagged run marked with a flag and
/// three words; then what the counted runs brought, in three folded groups: Materials, Gear (bound), Not read.
/// </summary>
public partial class MainWindow
{
    /// <summary>Places whose earlier runs are all shown, and the item groups open ("place|Materials").</summary>
    readonly HashSet<string> _runsAll = new(StringComparer.OrdinalIgnoreCase), _runsOpen = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>The place a layout render shows as in progress.</summary>
    string? _demoLive;
    /// <summary>Runs shown before "N earlier runs".</summary>
    const int RunsShown = 4;

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
        FillEventsLive(runs, live);
        // the panel over the game says so when a run of this session just closed flagged
        var flagged = runs.Where(r => r.OutOfAverages && r.LastLoot is { } l && (DateTime.UtcNow - l).TotalMinutes < 2).OrderByDescending(r => r.LastLoot).FirstOrDefault();
        _hudFlag = flagged == null ? "" : flagged.Suspect == "looks like several runs" ? $"{flagged.Zone} record looks like several runs" : $"{flagged.Zone} run: {FlagReason(flagged, 0).ToLowerInvariant()}";
        long made = 0; int counted = 0;
        foreach (var g in runs.GroupBy(r => r.Zone, StringComparer.OrdinalIgnoreCase).OrderByDescending(g => g.Max(r => r.LastLoot ?? r.Start)))
        {
            var list = g.ToList();
            var card = BuildRunsCard(g.Key, list, string.Equals(live, g.Key, StringComparison.OrdinalIgnoreCase), 660, ShowInstances);
            card.Margin = new Thickness(0, 0, 0, 16);
            InstHost.Children.Add(card);
            foreach (var r in list.Where(r => !r.OutOfAverages)) { counted++; made += RunValue(r); }
        }
        InstWhat.Text = InstHost.Children.Count > 0 ? $"{counted} run{(counted == 1 ? "" : "s")} counted · {Short(made)} made" : "";
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

    /// <summary>One place's card. Figures count only the runs not flagged (<see cref="InstanceRun.OutOfAverages"/>).</summary>
    Border BuildRunsCard(string zone, List<InstanceRun> runs, bool live, double width, Action redraw)
    {
        var (card, body) = CkShell(width);
        card.Margin = new Thickness(0);
        var counted = runs.Where(r => !r.OutOfAverages).ToList();
        int n = counted.Count;
        double secs = counted.Sum(r => r.Seconds);
        long value = counted.Sum(RunValue);
        var kind = runs.OrderBy(r => r.LastLoot ?? r.Start).Last().Kind;

        // the band: the place's tile, "Urugugu Canyon · Expedition", the figures in one line, and in progress while it's on
        var band = new Grid();
        band.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        band.ColumnDefinitions.Add(new ColumnDefinition());
        band.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var tile = new Border
        {
            Width = 56, Height = 56, CornerRadius = new CornerRadius(6), BorderThickness = new Thickness(2), BorderBrush = Ck(CkGold), VerticalAlignment = VerticalAlignment.Center,
            Background = new RadialGradientBrush(Color.FromArgb(0x54, CkGold.R, CkGold.G, CkGold.B), CkSlot) { Center = new Point(0.5, 0.4), GradientOrigin = new Point(0.5, 0.4) },
            Effect = new System.Windows.Media.Effects.DropShadowEffect { Color = CkGold, BlurRadius = 16, ShadowDepth = 0, Opacity = 0.28 },
            Child = new TextBlock { Text = InitialOf(zone), FontSize = 20, FontWeight = FontWeights.SemiBold, FontFamily = new FontFamily("Segoe UI Variable Display, Segoe UI"), Foreground = Ck(CkMuted), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center },
        };
        band.Children.Add(tile);
        var titles = new StackPanel { Margin = new Thickness(16, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        var title = CkText("", CkGold, 20, FontWeights.SemiBold, display: true);
        title.Inlines.Add(new Run(zone));
        if (kind.Length > 0) { title.Inlines.Add(new Run(" · ") { Foreground = Ck(CkFaint), FontWeight = FontWeights.Normal }); title.Inlines.Add(new Run(kind)); }
        titles.Children.Add(title);
        var stats = CkText("", CkMuted, 13); stats.Margin = new Thickness(0, 4, 0, 0);
        void Stat(string text) { if (stats.Inlines.Count > 0) stats.Inlines.Add(new Run("  ·  ") { Foreground = Ck(CkFaint) }); stats.Inlines.Add(new Run(text)); }
        if (n == 0) Stat("No run counted yet");
        else
        {
            Stat($"{n} run{(n == 1 ? "" : "s")} counted");
            Stat($"{Clock(TimeSpan.FromSeconds(secs / n))} a run");
            Stat($"{Short(value / n)} a run");
            Stat($"{Short((long)(value / Math.Max(secs, 10) * 3600))} an hour");
        }
        stats.ToolTip = "Over the counted runs: Kinah plus materials at the lowest Market price, after the Market's cut. Gear is bound and not in the value. Flagged runs are left out.";
        titles.Children.Add(stats);
        Grid.SetColumn(titles, 1); band.Children.Add(titles);
        if (live)
        {
            var dotRow = new StackPanel { Orientation = Orientation.Horizontal };
            dotRow.Children.Add(new Ellipse { Width = 6, Height = 6, Fill = Ck(CkGold), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 1, 6, 0) });
            dotRow.Children.Add(CkText("in progress", CkGold, 11.5));
            var pill = new Border
            {
                Child = dotRow, CornerRadius = new CornerRadius(10), BorderThickness = new Thickness(1), BorderBrush = Ck(CkGold, 0x73), Background = Ck(CkGold, 0x14),
                Padding = new Thickness(8, 2, 9, 3), VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(12, 3, 0, 0), ToolTip = "A run is open here now.",
            };
            Grid.SetColumn(pill, 2); band.Children.Add(pill);
        }
        body.Children.Add(new Border
        {
            Child = band, Padding = new Thickness(20, 16, 20, 14), BorderThickness = new Thickness(0, 0, 0, 1), BorderBrush = Ck(CkGold, 0x47), CornerRadius = new CornerRadius(5, 5, 0, 0),
            Background = new LinearGradientBrush(new GradientStopCollection { new(Color.FromArgb(0x29, CkGold.R, CkGold.G, CkGold.B), 0), new(Color.FromArgb(0x0D, CkGold.R, CkGold.G, CkGold.B), 0.55), new(Colors.Transparent, 1) }, 0),
        });

        // the runs, newest first; the rest behind "N earlier runs"
        var sec = CkSection("RUNS"); sec.Margin = new Thickness(20, 20, 20, 6); body.Children.Add(sec);
        var order = runs.OrderByDescending(r => r.LastLoot ?? r.Start).ToList();
        bool showAll = _runsAll.Contains(zone);
        double med = Median(counted.Select(r => r.Seconds));
        var list = new StackPanel { Margin = new Thickness(20, 0, 20, 0) };
        foreach (var r in showAll ? order : order.Take(RunsShown))
        {
            list.Children.Add(RunRow(r, med, redraw));
            // a flagged run's fix sheet, open in place under it
            if (_fixOpen == RunKey(r) && FlagReason(r, med).Length > 0) list.Children.Add(FixSheet(r, med, redraw));
        }
        if (order.Count > RunsShown)
        {
            int more = order.Count - RunsShown;
            var m = CkText(showAll ? "Show fewer" : $"{more} earlier run{(more == 1 ? "" : "s")}", CkFaint, 12.5);
            m.Margin = new Thickness(0, 8, 0, 2); m.Cursor = Cursors.Hand; m.HorizontalAlignment = HorizontalAlignment.Left; m.Background = Brushes.Transparent;
            m.MouseEnter += (_, _) => m.Foreground = Ck(CkGold); m.MouseLeave += (_, _) => m.Foreground = Ck(CkFaint);
            m.MouseLeftButtonUp += (_, _) => { if (!_runsAll.Remove(zone)) _runsAll.Add(zone); redraw(); };
            list.Children.Add(m);
        }
        body.Children.Add(list);

        // what the counted runs brought, by group: materials (with their value), gear (bound: no value), what wasn't read
        var mats = new Dictionary<long, (int Count, int Runs, long Value, bool Priced)>(); var gear = new Dictionary<long, int>(); int unread = 0, unknownGear = 0;
        // currencies (Abyss Points, Enhance Stone, tokens) are amounts, not pieces: a group of their own
        var curr = new Dictionary<long, long>();
        foreach (var r in counted)
        {
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
        var isec = CkSection("ITEMS"); isec.Margin = new Thickness(20, 20, 20, 6); body.Children.Add(isec);
        var note = CkText(n == 1 ? "Across the one counted run." : $"Across the {n} counted runs.", CkFaint, 12.5); note.Margin = new Thickness(20, 0, 20, 8);
        body.Children.Add(note);
        var groups = new StackPanel { Margin = new Thickness(20, 0, 20, 12) };
        if (mats.Count > 0)
        {
            long mv = mats.Values.Sum(x => x.Value);
            var sum = new List<Inline> { new Run($"{mats.Count} kind{(mats.Count == 1 ? "" : "s")}") };
            sum.Add(new Run("  ·  ") { Foreground = Ck(CkFaint) });
            if (mv > 0) { sum.Add(new Run(Short(mv)) { Foreground = Ck(CkGold), FontFamily = new FontFamily("Cascadia Mono, Consolas") }); sum.Add(new Run(" value")); }
            else sum.Add(new Run("no prices yet"));
            groups.Children.Add(RunsGroup(zone, "Materials", sum, () =>
            {
                var sp = new StackPanel();
                var vn = CkText("Value is after the Market's cut.", CkFaint, 12.5); vn.Margin = new Thickness(0, 8, 0, 4); sp.Children.Add(vn);
                foreach (var (id, x) in mats.OrderByDescending(x => x.Value.Value).ThenByDescending(x => x.Value.Count))
                    sp.Children.Add(RunsItem(_items.NameOf(id), x.Count, $"in {x.Runs} of {n} runs", x.Value > 0 ? Short(x.Value) : "", x.Value > 0 ? "" : x.Priced ? "bound" : "no price yet"));
                return sp;
            }, redraw));
        }
        if (curr.Count > 0)
        {
            groups.Children.Add(RunsGroup(zone, "Currencies", new List<Inline> { new Run($"{curr.Count} kind{(curr.Count == 1 ? "" : "s")}") }, () =>
            {
                var sp = new StackPanel { Margin = new Thickness(0, 4, 0, 0) };
                foreach (var (id, c) in curr.OrderByDescending(x => x.Value))
                    sp.Children.Add(RunsItem(_items.NameOf(id), (int)Math.Min(c, int.MaxValue), "", "", ""));
                return sp;
            }, redraw));
        }
        if (gear.Count > 0 || unknownGear > 0)
        {
            int pieces = gear.Values.Sum() + unknownGear;
            groups.Children.Add(RunsGroup(zone, "Gear (bound)", new List<Inline> { new Run($"{pieces} piece{(pieces == 1 ? "" : "s")}") }, () =>
            {
                var sp = new StackPanel { Margin = new Thickness(0, 4, 0, 0) };
                foreach (var (id, c) in gear.OrderByDescending(x => GradeRank(_items.Get(x.Key)?.Grade ?? "")).ThenByDescending(x => x.Value))
                    sp.Children.Add(RunsItem(_items.NameOf(id), c, "", "", ""));
                // a piece whose icon two items share: gear all the same, its name not known
                if (unknownGear > 0) sp.Children.Add(RunsItem("Unknown gear", unknownGear, "its icon is shared by two items", "", "", unknown: true));
                return sp;
            }, redraw));
        }
        if (unread > 0)
        {
            groups.Children.Add(RunsGroup(zone, "Not read", new List<Inline> { new Run(unread.ToString("N0")) }, () =>
            {
                var sp = new StackPanel { Margin = new Thickness(0, 4, 0, 0) };
                sp.Children.Add(RunsItem("Unknown item", unread, "", "", "", unknown: true));
                return sp;
            }, redraw));
        }
        if (groups.Children.Count == 0)
        {
            var none = CkText(n == 0 ? "Nothing counted yet." : "Only Kinah so far.", CkFaint, 12.5); none.Margin = new Thickness(0, 0, 0, 8);
            groups.Children.Add(none);
        }
        body.Children.Add(groups);
        return card;
    }

    /// <summary>One run: when (today "14:07", earlier "Oct 23 19:45"), how long, its Kinah and its pieces; a flagged run says why in three words. A click removes a wrong run.</summary>
    FrameworkElement RunRow(InstanceRun r, double medianSecs, Action redraw)
    {
        var why = FlagReason(r, medianSecs);
        bool flag = why.Length > 0;
        var g = new Grid();
        foreach (var w in new[] { 118.0, 70, 76 }) g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(w) });
        g.ColumnDefinitions.Add(new ColumnDefinition());
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(34) });
        var local = r.Start.ToLocalTime();
        g.Children.Add(CkText(local.Date == DateTime.Today ? local.ToString("HH:mm", CultureInfo.InvariantCulture) : local.ToString("MMM d HH:mm", CultureInfo.InvariantCulture), CkFaint, 12.5, mono: true));
        var len = CkText(Clock(TimeSpan.FromSeconds(r.Seconds)), flag ? CkFaint : CkInk, 13, mono: true); Grid.SetColumn(len, 1); g.Children.Add(len);
        var kin = CkText(r.KinahUnknown || r.OldUnread ? "" : Short(r.Kinah), flag ? CkFaint : CkInk, 13, mono: true); kin.HorizontalAlignment = HorizontalAlignment.Right;
        Grid.SetColumn(kin, 2); g.Children.Add(kin);
        TextBlock? chev = null;
        if (flag)
        {
            var sp = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            sp.Children.Add(new Viewbox
            {
                Width = 14, Height = 14, Margin = new Thickness(0, 0, 7, 0), VerticalAlignment = VerticalAlignment.Center,
                Child = new Canvas { Width = 16, Height = 16, Children = { new Path { Data = Geometry.Parse("M3.5,14 V2.5 M3.5,3 H11.5 L9.9,5.8 L11.5,8.6 H3.5"), Stroke = Ck(CkGold), StrokeThickness = 1.5, StrokeLineJoin = PenLineJoin.Round, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round } } },
            });
            sp.Children.Add(CkText(why, CkGold, 12.5));
            Grid.SetColumn(sp, 3); g.Children.Add(sp);
            chev = CkText("›", CkFaint, 15); chev.HorizontalAlignment = HorizontalAlignment.Right;
            Grid.SetColumn(chev, 4); g.Children.Add(chev);
        }
        else
        {
            var pcs = CkText(PiecesText(r), CkFaint, 12.5); pcs.HorizontalAlignment = HorizontalAlignment.Right; pcs.Margin = new Thickness(14, 0, 0, 0);
            Grid.SetColumn(pcs, 3); g.Children.Add(pcs);
        }
        var row = new Border
        {
            Child = g, Height = 38, BorderThickness = new Thickness(0, 0, 0, 1), BorderBrush = Ck(CkSlotLine, 0x8C), Background = Brushes.Transparent, Cursor = Cursors.Hand,
            ToolTip = flag ? FlagTip(r, medianSecs) + " Left out of the figures. Click to fix it." : "Wrong run? Click to remove it. It won't be sent to the site.",
        };
        ToolTipService.SetInitialShowDelay(row, 250);
        row.MouseEnter += (_, _) => { if (flag) row.Background = Ck(CkGold, 0x0D); if (chev != null) chev.Foreground = Ck(CkGold); };
        row.MouseLeave += (_, _) => { row.Background = Brushes.Transparent; if (chev != null) chev.Foreground = Ck(CkFaint); };
        // a flagged run opens its fix sheet (split, keep as one, type its Kinah, remove); any other run, the remove question
        row.MouseLeftButtonUp += (_, _) =>
        {
            if (!flag) { RemoveRun(r, redraw); return; }
            _fixOpen = _fixOpen == RunKey(r) ? null : RunKey(r);
            redraw();
        };
        return row;
    }

    /// <summary>"31 pieces · 2 gear" (InstanceRun.Pieces: each piece of gear, each other item once), or "Kinah only".</summary>
    string PiecesText(InstanceRun r)
    {
        // currencies are amounts, never pieces (the 16:02 Hidden Lugi run read as "3,740 items": its Abyss Points and stones)
        var (gearN, other) = r.Pieces(_items.IsGear, IsCurrency, withUnknown: true);
        int pcs = gearN + other + r.UnknownItems;
        if (pcs == 0) return r.RewardsNotRead ? "rewards not read" : "Kinah only";
        return $"{pcs} piece{(pcs == 1 ? "" : "s")}" + (gearN > 0 ? $" · {gearN} gear" : "");
    }

    /// <summary>Why a run is left out, in three words: "5 runs merged", "Kinah not read", "Rewards not read", "Not a run"; "" when it counts.</summary>
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
        if (r.PartlyRead) return $"Partly read: {r.SlotsUnread}";
        return "";
    }

    /// <summary>A currency (Abyss Points, Enhance Stone, a token): an amount, never a piece.</summary>
    bool IsCurrency(long id) => _items.Get(id)?.Category == "Currency";

    /// <summary>The flag's reason in a sentence, on hover.</summary>
    static string FlagTip(InstanceRun r, double medianSecs) =>
        r.Suspect == "looks like several runs" ? $"Looks like several runs saved as one: {Clock(TimeSpan.FromSeconds(r.Seconds))} against a usual {(medianSecs > 0 ? Clock(TimeSpan.FromSeconds(medianSecs)) : "run")}."
        : r.Suspect == "not a run" ? "Not a run: no Kinah, and its one reward screen came moments after entering."
        : r.RewardsNotRead ? "Its reward screen wasn't read, so what it paid isn't known."
        : r.KinahNotRead || r.OldUnread ? "The Kinah on its reward screen wasn't read, so it isn't counted as 0."
        : r.PartlyRead ? $"Partly read: {r.SlotsUnread} slot{(r.SlotsUnread == 1 ? "" : "s")} on its reward cards weren't read (cut by a card's window, or below what the screen showed). What was read counts and was sent, marked partly read."
        : r.Suspect;

    /// <summary>A folded group under Items: one line (name, its summary) until a click opens it.</summary>
    FrameworkElement RunsGroup(string zone, string name, List<Inline> summary, Func<FrameworkElement> content, Action redraw)
    {
        var key = zone + "|" + name;
        bool open = _runsOpen.Contains(key);
        var sp = new StackPanel();
        var box = new Border { Child = sp, CornerRadius = new CornerRadius(6), BorderThickness = new Thickness(1), BorderBrush = Ck(open ? CkFrame : CkSlotLine), Background = Ck(CkSlot, 0x99), Margin = new Thickness(0, 0, 0, 8) };
        var head = new Grid { Height = 44, Margin = new Thickness(14, 0, 14, 0), Background = Brushes.Transparent, Cursor = Cursors.Hand };
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        head.ColumnDefinitions.Add(new ColumnDefinition());
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        head.Children.Add(new Path
        {
            Data = Geometry.Parse("M0,0 L4,4 L0,8"), Stroke = Ck(open ? CkGold : CkFaint), StrokeThickness = 1.5, Width = 8, Height = 8, VerticalAlignment = VerticalAlignment.Center,
            RenderTransformOrigin = new Point(0.5, 0.5), RenderTransform = new RotateTransform(open ? 90 : 0),
        });
        var nm = CkText(name, CkInk, 14, FontWeights.SemiBold); nm.Margin = new Thickness(12, 0, 0, 0); Grid.SetColumn(nm, 1); head.Children.Add(nm);
        var sum = CkText("", CkMuted, 13); sum.Inlines.AddRange(summary); Grid.SetColumn(sum, 2); head.Children.Add(sum);
        head.MouseEnter += (_, _) => head.Background = Ck(Colors.White, 0x06);
        head.MouseLeave += (_, _) => head.Background = Brushes.Transparent;
        head.MouseLeftButtonUp += (_, _) => { if (!_runsOpen.Remove(key)) _runsOpen.Add(key); redraw(); };
        sp.Children.Add(head);
        if (open) sp.Children.Add(new Border { Child = content(), BorderThickness = new Thickness(0, 1, 0, 0), BorderBrush = Ck(CkSlotLine, 0xB3), Padding = new Thickness(14, 2, 14, 10) });
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
        else icon = CkIcon(name, 34, CkFrame, badge: count.ToString("N0"));
        icon.VerticalAlignment = VerticalAlignment.Center;
        g.Children.Add(icon);
        var names = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 12, 0) };
        names.Children.Add(CkText(name, CkInk, 13.5, FontWeights.SemiBold));
        if (sub.Length > 0) { var s = CkText(sub, CkFaint, 11.5); s.Margin = new Thickness(0, 1, 0, 0); names.Children.Add(s); }
        Grid.SetColumn(names, 1); g.Children.Add(names);
        var v = value.Length > 0 ? CkText(value, CkGold, 13, mono: true) : CkText(noValue, CkFaint, 12.5);
        v.HorizontalAlignment = HorizontalAlignment.Right; Grid.SetColumn(v, 2); g.Children.Add(v);
        return g;
    }

    /// <summary>A wrong run, removed on the player's say (Steven, 2026-10-23): it leaves the list and won't be sent to the site.</summary>
    void RemoveRun(InstanceRun r, Action redraw)
    {
        if (_watcher.Chat?.Instances is not { } il || _demoInst != null) return;
        var when = r.Start.ToLocalTime();
        if (MessageBox.Show(this, $"Remove this run ({r.Zone}, {when:MMM d HH:mm}, {Short(RunValue(r))} made)?\n\nUse it for a run the tracker got wrong. It leaves the list and won't be sent to the site; a copy is kept aside in instances.removed.jsonl.",
                "Kinah Ledger", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        il.Remove(r.Start, r.Zone);
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
    /// one whose Kinah wasn't read), written to a scratch folder and loaded the way the tracker loads its own, so the flags
    /// come from the same rules. Never the player's runs. Example figures.
    /// </summary>
    InstanceLog DemoRuns()
    {
        var dir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "KinahLedger-demo-runs");
        Directory.CreateDirectory(dir);
        foreach (var f in Directory.GetFiles(dir)) { try { File.Delete(f); } catch (Exception) { } }
        PriceBook.SetBoard(new() { ["Spiritstone Powder"] = 3_330, ["Artisan's Ultimate Refining Stone"] = 330_000, ["Sapphire Powder"] = 5_080, ["Magic Crystal"] = 4_550, ["Diamond Powder"] = 5_780 });
        var today = DateTime.Today; var yday = today.AddDays(-1); var before = today.AddDays(-2);
        var runs = new List<InstanceRun>();
        void Run(DateTime startLocal, int secs, long kinah, (string Name, int N)[] items, string[]? gearNames = null, bool kinahNotRead = false, int unread = 0)
        {
            var start = startLocal.ToUniversalTime();
            var r = new InstanceRun
            {
                Zone = "Urugugu Canyon", Kind = "Expedition", Start = start, LastLoot = start.AddSeconds(secs), PaidAt = start.AddSeconds(secs - 20), Kinah = kinah, KinahNotRead = kinahNotRead,
                FromPicture = true, SlotsUnread = unread, Tables = 1, TableAt = start.AddSeconds(secs - 20), Ended = "left; rewards in",
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
        var runs = log.Snapshot().Where(r => r.Zone == "Urugugu Canyon").ToList();
        if (runs.Count == 0) return;
        void Save(string file)
        {
            var card = BuildRunsCard("Urugugu Canyon", runs, true, 660, () => { });
            card.Margin = new Thickness(10);
            IconCache.Drain(TimeSpan.FromSeconds(8));
            Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.Background);
            SaveCard(card, System.IO.Path.Combine(dir, file));
        }
        _runsOpen.Clear();
        var fix = _fixOpen; _fixOpen = null;
        Save("ui-runs-card.png");
        _runsOpen.Add("Urugugu Canyon|Materials");
        Save("ui-runs-card-materials.png");
        _runsOpen.Clear();
        // the merged record's fix sheet open (direction C): Split at the reward screens, Keep as one, Remove
        _fixOpen = fix;
        if (_fixOpen != null) Save("ui-runs-card-fix.png");
    }
}
