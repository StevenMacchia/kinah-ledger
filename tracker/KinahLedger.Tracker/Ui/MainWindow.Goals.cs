using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using KinahLedger.Session;
using Path = System.Windows.Shapes.Path;

namespace KinahLedger.Ui;

/// <summary>
/// GOALS on the Farming page (0.9, Goals B "goals as slots" of kinah-bench/mock-goals-history, Steven's pick 2026-10-29).
/// Between the live card's actions and LOOT, once there is a goal: up to three slots, each a gold ring (from 12 o'clock)
/// round the thing's tile, the count under it ("137K / 300K", "9 / 20", "8 / 8 ✓") and a short name; a dashed "+" slot
/// while fewer than three. A hover opens the game-tooltip card (Have, To go, at today's pace); a click changes the goal, a
/// right-click changes or removes it. Goals are set from ⋯, an item's card, a LOOT slot's right-click or the "+", all in
/// one small sheet. Reaching one turns its ring full gold, and the panel and the status line say it once a day; no sound.
/// Kept in goals.json (Session/Goals.cs); drawn again only when what it shows changed (Changed("goals")).
/// </summary>
public partial class MainWindow
{
    /// <summary>The player's goals (goals.json), or a render's sample ones; at most Goals.Max.</summary>
    List<Goal> _goals = new();
    /// <summary>Today's runs behind a render's sample goals (never the player's); null reads the real ones.</summary>
    List<InstanceRun>? _demoGoalRuns;
    int _goalTicks;

    /// <summary>A gate (the mock's runs glyph), on a 24-unit grid.</summary>
    const string GatePath = "M5,21 V9 A7,7 0 0 1 19,9 V21 M9,21 V14 A3,3 0 0 1 15,14 V21 M3,21 H21";
    /// <summary>A box, for "An item" before one is picked.</summary>
    const string BoxPath = "M4,8 L12,4 L20,8 V16 L12,20 L4,16 Z M4,8 L12,12 L20,8 M12,12 V20";
    const double GoalSlotWidth = 104, GoalGap = 14, GoalRingSize = 64, GoalTileSize = 48;
    static readonly Color GoalGold = Color.FromRgb(0xF8, 0xD5, 0x98);

    /// <summary>The player's goals at the start; a layout render or a self-test (no watching) never reads them.</summary>
    void InitGoals(bool watch)
    {
        if (watch) _goals = Goals.Load();
        // the slim row's one action (2026-10-30): the same sheet as ⋯ › Set a goal, once the click has finished
        GoalsSlim.Click += (_, _) => OpenGoalSheetLater(null, new GoalDraft(), GoalsSlim);
    }

    void SaveGoals() { if (!_demo) Goals.Save(_goals); }

    /// <summary>Today's runs for the goals: a render's sample, or the Events page's today (TodayRunList).</summary>
    List<InstanceRun> GoalRunsToday() => _demoGoalRuns ?? TodayRunList();

    /// <summary>What today counted, for the goals: the Today count (LOOT's Today side) and today's runs (the Events page's).</summary>
    GoalCounts TodayGoalCounts() => GoalCounts.Day(TodayEngine, GoalRunsToday(), DateTime.UtcNow);

    /// <summary>
    /// The counts a goal's period reads. Today is the only period so far; a week would return GoalCounts.Sum of the
    /// day-history store's days and <paramref name="today"/> here, and everything else (the ring, the card, the pace, the
    /// once-a-day alert) reads it the same way.
    /// </summary>
    static GoalCounts CountsFor(Goal g, GoalCounts today) => today;

    GoalProgress ProgressOf(Goal g, GoalCounts today) => Goals.Progress(g, CountsFor(g, today), _items);

    void RedrawGoals() { _drawnSig.Remove("goals"); FillGoals(); }

    /// <summary>
    /// GOALS on Farming: with no goal, the slim one-line row ("⊕ Set a goal for today"), there from the page's first look
    /// (before any kill, whatever LOOT shows) because the ⋯ entry alone was never found (Steven, 2026-10-30); with a goal,
    /// the box of slots in its place, drawn again only when a goal or its count changed. Removing the last goal brings the
    /// row back.
    /// </summary>
    void FillGoals()
    {
        bool farm = _part == "Farming" && !_welcome, show = farm && _goals.Count > 0;
        GoalsCard.Visibility = farm ? Visibility.Visible : Visibility.Collapsed;
        GoalsSlim.Visibility = farm && !show ? Visibility.Visible : Visibility.Collapsed;
        GoalsBox.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        if (!show) { _drawnSig.Remove("goals"); return; }
        var today = TodayGoalCounts();
        var prog = _goals.Select(g => ProgressOf(g, today)).ToList();
        var sig = string.Join(";", _goals.Select((g, i) => $"{g.Kind}:{g.Target}:{g.Name}:{g.Which}:{prog[i].Have}")) + "|" + Skins.Current;
        if (!Changed("goals", sig)) return;
        int done = prog.Count(p => p.Done);
        GoalsNote.Inlines.Clear();
        GoalsNote.Inlines.Add(PR("today · ", "Faint"));
        var d = PR(done.ToString(), "Ink"); d.SetResourceReference(TextElement.FontFamilyProperty, "MonoFont");
        GoalsNote.Inlines.Add(d);
        GoalsNote.Inlines.Add(PR($" of {_goals.Count} done", "Faint"));
        GoalsNote.ToolTip = "Today's goals, from midnight on this PC. Click one to change it; right-click to remove it.";
        GoalsHost.Children.Clear();
        for (int i = 0; i < _goals.Count; i++) GoalsHost.Children.Add(GoalSlot(_goals[i], prog[i]));
        if (_goals.Count < Goals.Max) GoalsHost.Children.Add(AddGoalSlot());
    }

    /// <summary>The count under a slot: "137K / 300K", "9 / 20", "8 / 8 ✓".</summary>
    static string GoalCountText(Goal g, GoalProgress p)
    {
        string N(long n) => g.Kind == Goal.KindKinah ? Fk(n) : n.ToString("N0");
        return $"{N(p.Have)} / {N(p.Target)}" + (p.Done ? " ✓" : "");
    }

    /// <summary>An amount in the goal's own words: "300K" Kinah, "20" pieces or runs.</summary>
    static string GoalAmount(Goal g, long n) => g.Kind == Goal.KindKinah ? Fk(n) : n.ToString("N0");

    /// <summary>The goal's tile: the coin for Kinah, the item's icon in its grade frame, the gate for runs.</summary>
    FrameworkElement GoalTileFor(Goal g, double size, bool gold = false) => g.Kind switch
    {
        Goal.KindItem => CkIcon(g.Name, size, FrameOf(_items.IdOf(g.Name))),
        Goal.KindRuns => GlyphTile(size, GatePath, gold: gold),
        _ => CoinTile(size),
    };

    /// <summary>One goal as a slot: the ring round its tile, the count, the name; the card on hover, Change on a click, Change and Remove on a right-click.</summary>
    FrameworkElement GoalSlot(Goal g, GoalProgress p)
    {
        var slot = new StackPanel { Width = GoalSlotWidth, Background = Brushes.Transparent, Cursor = Cursors.Hand, Tag = "goal", Margin = new Thickness(0, 0, GoalGap, 0) };
        var ring = new Grid { Width = GoalRingSize, Height = GoalRingSize, HorizontalAlignment = HorizontalAlignment.Center };
        // the hovered slot's outline, round the ring (the mock's .ring.sel)
        var sel = new Ellipse { Width = GoalRingSize + 8, Height = GoalRingSize + 8, Margin = new Thickness(-4), StrokeThickness = 2, Visibility = Visibility.Hidden };
        sel.SetResourceReference(Shape.StrokeProperty, "Gold");
        ring.Children.Add(sel);
        ring.Children.Add(GoalTrack());
        if (p.Done) { var full = GoalTrack(); full.SetResourceReference(Shape.StrokeProperty, "Gold"); ring.Children.Add(full); }
        else if (p.Fraction > 0) ring.Children.Add(GoalArc(p.Fraction));
        var tile = GoalTileFor(g, GoalTileSize);
        tile.HorizontalAlignment = HorizontalAlignment.Center; tile.VerticalAlignment = VerticalAlignment.Center;
        ring.Children.Add(tile);
        slot.Children.Add(ring);
        var count = PT(GoalCountText(g, p), p.Done ? "Gold" : "Ink", 12.5, null, "MonoFont");
        count.HorizontalAlignment = HorizontalAlignment.Center; count.Margin = new Thickness(0, 7, 0, 0);
        slot.Children.Add(count);
        var name = PT(Goals.Label(g), "Faint", 11.5);
        name.HorizontalAlignment = HorizontalAlignment.Center; name.TextAlignment = TextAlignment.Center; name.TextWrapping = TextWrapping.Wrap; name.MaxHeight = 31; name.Margin = new Thickness(0, 3, 0, 0);
        slot.Children.Add(name);
        AutomationProperties.SetName(slot, $"Goal: {Goals.Label(g)}, {GoalCountText(g, p)}");
        CardTip(slot, () => GoalCard(g));
        slot.MouseEnter += (_, _) => sel.Visibility = Visibility.Visible;
        slot.MouseLeave += (_, _) => sel.Visibility = Visibility.Hidden;
        slot.MouseLeftButtonUp += (_, _) => OpenGoalSheet(g, GoalDraft.From(g), slot);
        var change = new MenuItem { Header = "Change…" };
        change.Click += (_, _) => OpenGoalSheetLater(g, GoalDraft.From(g), slot);
        var remove = new MenuItem { Header = "Remove", ToolTip = "Takes the goal off, with Undo." };
        remove.Click += (_, _) => RemoveGoal(g);
        var menu = new ContextMenu(); menu.Items.Add(change); menu.Items.Add(remove);
        slot.ContextMenu = menu;
        return slot;
    }

    /// <summary>The ring's dark track: a 3 px circle of radius 29 inside the 64 px slot, as the mock's SVG.</summary>
    static Ellipse GoalTrack()
    {
        var e = new Ellipse { Width = 61, Height = 61, StrokeThickness = 3, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        e.SetResourceReference(Shape.StrokeProperty, "LineStrong");
        return e;
    }

    /// <summary>The gold arc from 12 o'clock, clockwise, for the share done (0 to 1, short of full).</summary>
    static Path GoalArc(double share)
    {
        const double c = 32, r = 29;
        double a = 2 * Math.PI * Math.Clamp(share, 0.005, 0.999);
        var fig = new PathFigure { StartPoint = new Point(c, c - r), IsClosed = false, IsFilled = false };
        fig.Segments.Add(new ArcSegment(new Point(c + r * Math.Sin(a), c - r * Math.Cos(a)), new Size(r, r), 0, a > Math.PI, SweepDirection.Clockwise, true));
        var geo = new PathGeometry(); geo.Figures.Add(fig);
        var p = new Path { Data = geo, StrokeThickness = 3, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round, Width = 64, Height = 64 };
        p.SetResourceReference(Shape.StrokeProperty, "Gold");
        return p;
    }

    /// <summary>The dashed "+" slot after the goals, while fewer than three: a click opens Set a goal.</summary>
    FrameworkElement AddGoalSlot()
    {
        var slot = new StackPanel { Width = GoalSlotWidth, Background = Brushes.Transparent, Cursor = Cursors.Hand, Tag = "add", ToolTip = "Set a goal: Kinah, an item or runs, for today." };
        ToolTipService.SetInitialShowDelay(slot, 400);
        var ring = new Grid { Width = GoalRingSize, Height = GoalRingSize, HorizontalAlignment = HorizontalAlignment.Center };
        var dash = new Ellipse { Width = 62, Height = 62, StrokeThickness = 1.2, StrokeDashArray = new DoubleCollection { 3, 3 }, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        dash.SetResourceReference(Shape.StrokeProperty, "LineStrong");
        ring.Children.Add(dash);
        var plus = PT("+", "Faint", 26, FontWeights.Light, "DisplayFont");
        plus.HorizontalAlignment = HorizontalAlignment.Center; plus.Margin = new Thickness(0, -4, 0, 0);
        ring.Children.Add(plus);
        slot.Children.Add(ring);
        var name = PT("Add a goal", "Faint", 11.5); name.HorizontalAlignment = HorizontalAlignment.Center; name.Margin = new Thickness(0, 7, 0, 0);
        slot.Children.Add(name);
        AutomationProperties.SetName(slot, "Add a goal");
        slot.MouseEnter += (_, _) => dash.SetResourceReference(Shape.StrokeProperty, "Gold");
        slot.MouseLeave += (_, _) => dash.SetResourceReference(Shape.StrokeProperty, "LineStrong");
        slot.MouseLeftButtonUp += (_, _) => OpenGoalSheet(null, new GoalDraft(), slot);
        return slot;
    }

    /// <summary>The card a goal's hover opens: its band, a thin gold bar, then TODAY: Have, To go and the time at today's pace.</summary>
    internal Border GoalCard(Goal g)
    {
        var p = ProgressOf(g, TodayGoalCounts());
        var (card, body) = CkShell(300);
        var sub = g.Kind == Goal.KindKinah ? $"Goal · {Fk(g.Target)} from farming today" : $"Goal · {g.Target:N0} today";
        body.Children.Add(g.Kind == Goal.KindItem ? CkBand(g.Name, GradeOf(g.Name), sub)
            : GoalBand(GoalTileFor(g, 64, gold: true), g.Kind == Goal.KindKinah ? "Kinah" : Goals.Label(g), sub));
        body.Children.Add(GoalBar(p.Fraction));
        body.Children.Add(CkSection("TODAY"));
        body.Children.Add(CkKv("Have", GoalAmount(g, p.Have), CkInk));
        body.Children.Add(p.Done ? CkKv("Reached", "✓", CkGold) : CkKv("To go", GoalAmount(g, p.ToGo), CkInk));
        if (Goals.PaceText(p) is { Length: > 0 } pace) body.Children.Add(CkKv("At today's pace", pace, CkInk, mono: false));
        if (g.Kind == Goal.KindItem) { var n = CkNote("Drops, gathering and run rewards picked up today."); n.Margin = new Thickness(20, 8, 20, 0); body.Children.Add(n); }
        var how = CkNote("Click to change · right-click to remove"); how.Margin = new Thickness(20, 12, 20, 14);
        body.Children.Add(how);
        return card;
    }

    /// <summary>A gold header band for a tile with no grade (the coin, the gate): the tile, a name, a line under it.</summary>
    static Border GoalBand(FrameworkElement tile, string title, string sub)
    {
        var g = new Grid();
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); g.ColumnDefinitions.Add(new ColumnDefinition());
        tile.Margin = new Thickness(0, 0, 16, 0); g.Children.Add(tile);
        var names = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        names.Children.Add(CkText(title, CkGold, 20, FontWeights.SemiBold, display: true));
        if (sub.Length > 0) { var l = CkText(sub, CkMuted, 13); l.Margin = new Thickness(0, 3, 0, 0); names.Children.Add(l); }
        Grid.SetColumn(names, 1); g.Children.Add(names);
        return new Border
        {
            Child = g, Padding = new Thickness(18, 14, 18, 14), BorderThickness = new Thickness(0, 0, 0, 1), BorderBrush = Ck(CkGold, 0x70), CornerRadius = new CornerRadius(5, 5, 0, 0),
            Background = new LinearGradientBrush(new GradientStopCollection { new(Color.FromArgb(0x40, CkGold.R, CkGold.G, CkGold.B), 0), new(Color.FromArgb(0x14, CkGold.R, CkGold.G, CkGold.B), 0.55), new(Colors.Transparent, 1) }, 0),
        };
    }

    /// <summary>The card's thin progress bar: the slot colour, filled gold to the share done.</summary>
    static FrameworkElement GoalBar(double share)
    {
        var bar = new Grid { Height = 8, Margin = new Thickness(20, 16, 20, 0) };
        bar.Children.Add(new Border { CornerRadius = new CornerRadius(4), Background = Ck(CkSlot), BorderBrush = Ck(CkSlotLine), BorderThickness = new Thickness(1) });
        if (share > 0)
        {
            var fill = new Grid();
            fill.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(share, GridUnitType.Star) });
            fill.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Math.Max(0.0001, 1 - share), GridUnitType.Star) });
            fill.Children.Add(new Border { CornerRadius = new CornerRadius(4), Background = new LinearGradientBrush(CkGoldRule, CkGold, 0) });
            bar.Children.Add(fill);
        }
        return bar;
    }

    /// <summary>Remove from a goal's right-click: gone at once, with the Undo bar (no "Are you sure?", Ui/MainWindow.Undo.cs).</summary>
    void RemoveGoal(Goal g)
    {
        int at = _goals.IndexOf(g);
        if (at < 0) return;
        _ = Offer("goal", "Goal removed",
            apply: () => { _goals.Remove(g); SaveGoals(); RedrawGoals(); },
            undo: () =>
            {
                if (!_goals.Contains(g) && _goals.Count < Goals.Max) { _goals.Insert(Math.Min(at, _goals.Count), g); SaveGoals(); }
                RedrawGoals(); Say("The goal is back.", "Good");
            },
            commit: () => Task.CompletedTask);
    }

    /// <summary>"Set a goal…" for a menu (⋯, a LOOT slot's right-click): the sheet with <paramref name="draft"/>'s kind and thing.</summary>
    MenuItem GoalMenuItem(Func<GoalDraft> draft, FrameworkElement at, string tip)
    {
        var mi = new MenuItem { Header = "Set a goal…", ToolTip = tip };
        mi.Click += (_, _) => OpenGoalSheetLater(null, draft(), at);
        return mi;
    }

    /* ---------------- Set a goal: one small sheet ---------------- */

    /// <summary>What the sheet holds while it is open: the kind, an amount as typed for each kind, the item, which runs.</summary>
    sealed class GoalDraft
    {
        public string Kind = Goal.KindKinah;
        public readonly Dictionary<string, string> Amount = new() { [Goal.KindKinah] = "300K", [Goal.KindItem] = "20", [Goal.KindRuns] = "5" };
        public string Item = "";
        public string Which = Goal.AnyRun;

        public static GoalDraft From(Goal g)
        {
            var d = new GoalDraft { Kind = g.Kind, Item = g.Name, Which = g.Which };
            d.Amount[g.Kind] = AmountText(g.Target);
            return d;
        }

        public static GoalDraft ForItem(string name) => new() { Kind = Goal.KindItem, Item = name };

        /// <summary>The goal it makes; Target 0 when the amount isn't one (Goals.Valid says no).</summary>
        public Goal ToGoal() => new()
        {
            Kind = Kind, Target = Goals.ParseAmount(Amount.GetValueOrDefault(Kind, "")) ?? 0,
            Name = Kind == Goal.KindItem ? Item : "", Which = Kind == Goal.KindRuns ? Which : Goal.AnyRun,
        };
    }

    /// <summary>An amount the way it is typed back: "300K", "1.5M", "20" (exact: 150,500 stays 150500).</summary>
    static string AmountText(long n) =>
        n >= 1_000_000 && n % 10_000 == 0 ? (n / 1e6).ToString("0.##", System.Globalization.CultureInfo.InvariantCulture) + "M"
        : n >= 1_000 && n % 100 == 0 && n >= 10_000 ? (n / 1e3).ToString("0.#", System.Globalization.CultureInfo.InvariantCulture) + "K"
        : n.ToString(System.Globalization.CultureInfo.InvariantCulture);

    static readonly Dictionary<string, long[]> GoalPresets = new()
    {
        [Goal.KindKinah] = new long[] { 100_000, 200_000, 300_000, 500_000, 1_000_000 },
        [Goal.KindItem] = new long[] { 10, 20, 50, 100 },
        [Goal.KindRuns] = new long[] { 3, 5, 8, 10 },
    };

    /// <summary>The sheet, once the menu or card that asked for it has closed (a popup opened from inside another closes with it).</summary>
    void OpenGoalSheetLater(Goal? editing, GoalDraft d, FrameworkElement? at) =>
        Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() => OpenGoalSheet(editing, d, at)));

    /// <param name="editing">The goal a click or Change opened, replaced in place on Set goal; null adds one.</param>
    void OpenGoalSheet(Goal? editing, GoalDraft d, FrameworkElement? at)
    {
        if (editing == null && _goals.Count >= Goals.Max) { Say("Three goals are set. Right-click one to change or remove it.", "Warn"); return; }
        if (_card != null) _card.IsOpen = false;
        _card = new Popup { Child = BuildGoalSheet(d, editing), StaysOpen = false, AllowsTransparency = true, PopupAnimation = PopupAnimation.Fade, Placement = PlacementMode.Mouse, PlacementTarget = at ?? (UIElement)Content };
        _card.IsOpen = true;
    }

    /// <summary>
    /// The sheet (the mock's GoalsA sheet, in the card kit): three kind buttons (Kinah, An item, Runs), the item or which
    /// runs, the amount with a few presets, a one-line hint with the time at today's pace, then Set goal and Cancel.
    /// </summary>
    Border BuildGoalSheet(GoalDraft d, Goal? editing)
    {
        var (card, body) = CkShell(420);
        void Redo() { if (_card is { IsOpen: true }) _card.Child = BuildGoalSheet(d, editing); }
        var title = CkText(editing != null ? "Change this goal" : "Set a goal", CkInk, 16, FontWeights.SemiBold, display: true); title.Margin = new Thickness(20, 16, 20, 2);
        body.Children.Add(title);
        var sub = CkText("It counts today, from midnight on this PC.", CkMuted, 12.5); sub.Margin = new Thickness(20, 0, 20, 12);
        body.Children.Add(sub);

        // the three kinds
        var kinds = new Grid { Margin = new Thickness(20, 0, 20, 14) };
        for (int i = 0; i < 5; i++) kinds.ColumnDefinitions.Add(new ColumnDefinition { Width = i % 2 == 1 ? new GridLength(8) : new GridLength(1, GridUnitType.Star) });
        void Kind(int col, string kind, FrameworkElement tile, string label)
        {
            bool on = d.Kind == kind;
            var sp = new StackPanel();
            tile.HorizontalAlignment = HorizontalAlignment.Center; sp.Children.Add(tile);
            var t = CkText(label, on ? CkGold : CkMuted, 12.5, on ? FontWeights.SemiBold : null); t.HorizontalAlignment = HorizontalAlignment.Center; t.Margin = new Thickness(0, 8, 0, 0);
            sp.Children.Add(t);
            var b = new Border
            {
                Child = sp, Padding = new Thickness(6, 12, 6, 10), CornerRadius = new CornerRadius(8), BorderThickness = new Thickness(1), Cursor = Cursors.Hand,
                BorderBrush = on ? Ck(CkGold, 0x99) : Ck(CkSlotLine), Background = on ? Ck(CkGold, 0x14) : Ck(CkSlot, 0x99),
            };
            AutomationProperties.SetName(b, label);
            b.MouseLeftButtonUp += (_, _) => { if (d.Kind != kind) { d.Kind = kind; Redo(); } };
            Grid.SetColumn(b, col); kinds.Children.Add(b);
        }
        Kind(0, Goal.KindKinah, CoinTile(36), "Kinah");
        Kind(2, Goal.KindItem, d.Item.Length > 0 ? CkIcon(d.Item, 36, FrameOf(_items.IdOf(d.Item))) : GlyphTile(36, BoxPath), "An item");
        Kind(4, Goal.KindRuns, GlyphTile(36, GatePath), "Runs");
        body.Children.Add(kinds);

        var today = TodayGoalCounts();
        TextBox? find = null;
        // An item: the one picked, today's pickups to pick from, and a search of the item list
        if (d.Kind == Goal.KindItem)
        {
            var row = SheetRow("Item");
            var pick = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            if (d.Item.Length > 0)
            {
                var tile = CkIcon(d.Item, 26, FrameOf(_items.IdOf(d.Item))); tile.Margin = new Thickness(0, 0, 10, 0); pick.Children.Add(tile);
                var g = GradeOf(d.Item);
                pick.Children.Add(CkText(d.Item, g is "" or "Common" ? CkInk : GradeColor(g), 13.5, FontWeights.SemiBold));
            }
            else pick.Children.Add(CkText("Pick one below", CkFaint, 12.5));
            Grid.SetColumn(pick, 1); row.Children.Add(pick);
            body.Children.Add(row);
            var mine = today.Farmed.Concat(today.FromRuns).GroupBy(kv => kv.Key).Select(gr => (Id: gr.Key, N: gr.Sum(x => x.Value))).Where(x => x.N > 0)
                .OrderByDescending(x => x.N).Take(7).ToList();
            if (mine.Count > 0)
            {
                var tiles = new WrapPanel { Margin = new Thickness(96, 0, 20, 4) };
                foreach (var (id, n) in mine)
                {
                    var nm = _items.NameOf(id);
                    bool on = string.Equals(nm, d.Item, StringComparison.OrdinalIgnoreCase);
                    var t = CkIcon(nm, 34, FrameOf(id), glow: on);
                    t.Margin = new Thickness(0, 0, 6, 6); t.Cursor = Cursors.Hand; t.ToolTip = $"{nm} · {n:N0} today";
                    AutomationProperties.SetName(t, nm);
                    t.MouseLeftButtonUp += (_, _) => { d.Item = nm; Redo(); };
                    tiles.Children.Add(t);
                }
                body.Children.Add(tiles);
            }
            var finding = new TextBox { Height = 30, FontSize = 12.5, Padding = new Thickness(8, 0, 8, 0), VerticalContentAlignment = VerticalAlignment.Center, Background = Ck(CkSlot), BorderBrush = Ck(CkFrame), Foreground = Ck(CkInk), CaretBrush = Ck(CkGold) };
            find = finding;
            AutomationProperties.SetName(finding, "Find an item");
            var findHint = CkText(d.Item.Length > 0 ? "Find another item…" : "Find an item…", CkFaint, 12.5); findHint.Margin = new Thickness(10, 0, 0, 0); findHint.IsHitTestVisible = false;
            var findBox = new Grid { Margin = new Thickness(96, 2, 20, 0) }; findBox.Children.Add(finding); findBox.Children.Add(findHint);
            body.Children.Add(findBox);
            var found = new StackPanel { Margin = new Thickness(96, 4, 20, 4) };
            body.Children.Add(found);
            void Suggest()
            {
                findHint.Visibility = finding.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
                found.Children.Clear();
                var text = finding.Text.Trim();
                if (text.Length < 2) return;
                foreach (var it in _items.Like(text, 5))
                {
                    var g = new Grid { Height = 28, Background = Brushes.Transparent, Cursor = Cursors.Hand };
                    g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); g.ColumnDefinitions.Add(new ColumnDefinition());
                    var t = CkIcon(it.Name, 22, FrameOf(it.Id)); t.Margin = new Thickness(0, 0, 8, 0); g.Children.Add(t);
                    var n = CkText(it.Name, it.Grade is "" or "Common" ? CkInk : GradeColor(it.Grade), 12.5); Grid.SetColumn(n, 1); g.Children.Add(n);
                    var name = it.Name;
                    g.MouseEnter += (_, _) => g.Background = Ck(CkGold, 0x14);
                    g.MouseLeave += (_, _) => g.Background = Brushes.Transparent;
                    g.MouseLeftButtonUp += (_, _) => { d.Item = name; Redo(); };
                    found.Children.Add(g);
                }
                if (found.Children.Count == 0) found.Children.Add(CkText("No item by that name.", CkFaint, 12));
            }
            finding.TextChanged += (_, _) => Suggest();
            // Enter takes the first name offered (a name typed by hand never makes a goal: only the item list's)
            finding.PreviewKeyDown += (_, e) =>
            {
                if (e.Key != Key.Enter) return;
                e.Handled = true;
                if (_items.Like(finding.Text.Trim(), 1).FirstOrDefault() is { } first && finding.Text.Trim().Length >= 2) { d.Item = first.Name; Redo(); }
            };
        }
        // Runs: which ones count
        if (d.Kind == Goal.KindRuns)
        {
            var row = SheetRow("Which");
            var pills = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            foreach (var (key, text) in new[] { (Goal.Expeditions, "Expeditions"), (Goal.Events, "Events"), (Goal.AnyRun, "Any run") })
            {
                var pill = SheetPill(text, d.Which == key, mono: false);
                pill.MouseLeftButtonUp += (_, _) => { d.Which = key; Redo(); };
                pills.Children.Add(pill);
            }
            Grid.SetColumn(pills, 1); row.Children.Add(pills);
            body.Children.Add(row);
        }

        // the amount, with its presets under it
        var amt = SheetRow(d.Kind == Goal.KindKinah ? "Make" : d.Kind == Goal.KindItem ? "Pick up" : "Do");
        amt.Margin = new Thickness(20, d.Kind == Goal.KindKinah ? 0 : 8, 20, 0);
        var box = new TextBox
        {
            Height = 34, FontSize = 13, Padding = new Thickness(10, 0, 10, 0), VerticalContentAlignment = VerticalAlignment.Center, Text = d.Amount[d.Kind],
            Background = Ck(CkSlot), BorderBrush = Ck(CkFrame), Foreground = Ck(CkInk), CaretBrush = Ck(CkGold), FontFamily = new FontFamily("Cascadia Mono, Consolas"),
        };
        AutomationProperties.SetName(box, "How much");
        var unit = CkText(d.Kind == Goal.KindKinah ? "from farming, today" : "today", CkFaint, 12); unit.HorizontalAlignment = HorizontalAlignment.Right; unit.Margin = new Thickness(0, 0, 12, 0); unit.IsHitTestVisible = false;
        var amtBox = new Grid(); amtBox.Children.Add(box); amtBox.Children.Add(unit);
        Grid.SetColumn(amtBox, 1); amt.Children.Add(amtBox);
        body.Children.Add(amt);
        var presets = new WrapPanel { Margin = new Thickness(96, 8, 20, 8) };
        var pillOf = new List<(Border Pill, long Value)>();
        foreach (var v in GoalPresets[d.Kind])
        {
            var text = d.Kind == Goal.KindKinah ? (v >= 1_000_000 ? $"{v / 1_000_000}M" : $"{v / 1000}K") : v.ToString();
            var pill = SheetPill(text, false, mono: true);
            pill.MouseLeftButtonUp += (_, _) => { box.Text = text; box.Focus(); box.CaretIndex = box.Text.Length; };
            presets.Children.Add(pill); pillOf.Add((pill, v));
        }
        body.Children.Add(presets);
        var hint = CkText("", CkFaint, 12); hint.TextWrapping = TextWrapping.Wrap; hint.TextTrimming = TextTrimming.None; hint.Margin = new Thickness(20, 0, 20, 14);
        body.Children.Add(hint);

        // Set goal and Cancel, under a rule
        var acts = new StackPanel { Orientation = Orientation.Horizontal };
        var set = new Button { Content = "Set goal", Height = 30, Padding = new Thickness(16, 0, 16, 0), FontSize = 12.5 };
        set.SetResourceReference(StyleProperty, "PrimaryButton");
        acts.Children.Add(set);
        var cancel = new Button { Content = "Cancel", Height = 30, Padding = new Thickness(14, 0, 14, 0), FontSize = 12.5, Margin = new Thickness(8, 0, 0, 0) };
        cancel.Click += (_, _) => { if (_card != null) _card.IsOpen = false; };
        acts.Children.Add(cancel);
        body.Children.Add(new Border { Child = acts, Padding = new Thickness(20, 12, 20, 14), BorderThickness = new Thickness(0, 1, 0, 0), BorderBrush = Ck(CkSlotLine, 0xCC) });

        void Update()
        {
            d.Amount[d.Kind] = box.Text;
            var g = d.ToGoal();
            set.IsEnabled = Goals.Valid(g);
            hint.Text = GoalHint(g, today);
            foreach (var (pill, v) in pillOf) StylePill(pill, g.Target == v);
        }
        box.TextChanged += (_, _) => Update();
        void Commit() { if (set.IsEnabled) SetGoalFromSheet(d.ToGoal(), editing); }
        set.Click += (_, _) => Commit();
        box.PreviewKeyDown += (_, e) => { if (e.Key == Key.Enter) { e.Handled = true; Commit(); } };
        card.PreviewKeyDown += (_, e) => { if (e.Key == Key.Escape && _card != null) { e.Handled = true; _card.IsOpen = false; } };
        Update();
        // the cursor in the amount, or in the search while no item is picked
        TextBox focus = d.Kind == Goal.KindItem && d.Item.Length == 0 && find != null ? find : box;
        card.Loaded += (_, _) => { focus.Focus(); if (focus == box) box.SelectAll(); };
        return card;
    }

    /// <summary>A row of the sheet: a muted label 76 px wide, then what it sets.</summary>
    static Grid SheetRow(string label)
    {
        var g = new Grid { Margin = new Thickness(20, 0, 20, 8), MinHeight = 34 };
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(76) }); g.ColumnDefinitions.Add(new ColumnDefinition());
        g.Children.Add(CkText(label, CkMuted, 12.5));
        return g;
    }

    /// <summary>A small round pill (a preset, a kind of run): gold when picked.</summary>
    static Border SheetPill(string text, bool on, bool mono)
    {
        var t = CkText(text, CkMuted, 12, mono: mono);
        var b = new Border { Child = t, Height = 26, Padding = new Thickness(10, 0, 10, 0), Margin = new Thickness(0, 0, 6, 6), CornerRadius = new CornerRadius(13), BorderThickness = new Thickness(1), Cursor = Cursors.Hand };
        StylePill(b, on);
        return b;
    }

    static void StylePill(Border b, bool on)
    {
        b.BorderBrush = on ? Ck(CkGold, 0x99) : Ck(CkSlotLine);
        b.Background = on ? Ck(CkGold, 0x14) : Brushes.Transparent;
        if (b.Child is TextBlock t) t.Foreground = Ck(on ? CkGold : CkMuted);
    }

    /// <summary>The sheet's one line: what's missing, or how long at today's pace, and that the panel says it once with no sound.</summary>
    string GoalHint(Goal g, GoalCounts today)
    {
        if (g.Kind == Goal.KindItem && g.Name.Length == 0) return "Pick an item.";
        if (g.Target <= 0) return g.Kind == Goal.KindKinah ? "Type an amount, like 300K." : "Type how many.";
        var p = ProgressOf(g, today);
        if (p.Done) return $"Already reached today ({GoalAmount(g, p.Have)} so far).";
        var pace = Goals.PaceText(p);
        var first = pace.Length > 0 ? $"At today's pace, {pace} to go."
            : g.Kind == Goal.KindKinah ? "No Kinah from farming yet today."
            : g.Kind == Goal.KindRuns ? (p.Have > 0 ? $"{p.Have:N0} so far today." : "No runs yet today.")
            : p.Have > 0 ? $"{p.Have:N0} so far today, from runs." : "None picked up yet today.";
        return first + " When it's reached, the game panel says so once. No sound.";
    }

    /// <summary>Set goal: added (or put in place of the one changed), saved, drawn, and said in the status line.</summary>
    void SetGoalFromSheet(Goal g, Goal? editing)
    {
        if (!Goals.Valid(g)) return;
        var p = ProgressOf(g, TodayGoalCounts());
        Goals.Settle(g, p, DayStore.Today);   // already reached when set: nothing announced for it
        int at = editing != null ? _goals.IndexOf(editing) : -1;
        if (at >= 0) _goals[at] = g;
        else if (!Goals.Add(_goals, g)) { Say("Three goals are set. Right-click one to change or remove it.", "Warn"); return; }
        SaveGoals();
        if (_card != null) _card.IsOpen = false;
        RedrawGoals();
        Say((at >= 0 ? "Goal changed: " : "Goal set: ") + Goals.What(g, g.Target, Fk) + " today" + (p.Done ? ". Already reached." : "."), "Good");
    }

    /* ---------------- reaching one ---------------- */

    /// <summary>From the window's tick, about once a second: a goal reached is said once (the panel and the status line).</summary>
    void GoalTick()
    {
        if (_goals.Count == 0 || _welcome || ++_goalTicks % 2 != 0) return;
        try { CheckGoals(); }
        catch (Exception ex) { CrashLog.Write("goals", ex); }
    }

    /// <summary>
    /// The goals reached since the last look and not yet said today: marked said and saved (so a restart doesn't say them
    /// again), the ring redrawn full gold, and "Goal reached · 300K from farming" said once on the panel (in gold on the
    /// Strip's alert line; the Card and the Ticker show it as their short line) and in the status line. Never a sound.
    /// </summary>
    List<string> CheckGoals()
    {
        var today = TodayGoalCounts();
        var reached = Goals.JustReached(_goals, g => CountsFor(g, today), _items, DayStore.Today);
        if (reached.Count == 0) return new();
        SaveGoals();
        var said = reached.Select(g => Goals.ReachedText(g, Fk)).ToList();
        var line = said.Count == 1 ? said[0] : "Goals reached · " + string.Join(" · ", reached.Select(g => Goals.What(g, g.Target, Fk)));
        if (HudLook == HudLooks.Strip) _hud?.Alert(line, GoalGold, 6); else FlashHud(line);
        Say(line, "Gold");
        CrashLog.Note(line);
        RedrawGoals();
        return said;
    }

    /* ---------------- self-tests and layout renders ---------------- */

    /// <summary>For options-test: goals as given (never saved: the window is a demo), drawn now.</summary>
    internal void SetGoalsForTest(IEnumerable<Goal>? goals) { _goals = goals?.ToList() ?? new(); RedrawGoals(); }

    /// <summary>For options-test: a page shown as the activity would show it.</summary>
    internal void ShowPartForTest(string part) => ShowPart(part, byHand: false);

    /// <summary>For options-test: one look for goals reached, as the tick does; the lines said.</summary>
    internal List<string> GoalTickForTest() => CheckGoals();

    /// <summary>
    /// For options-test: whether the GOALS box (with goals) shows, its goal slots, the "+" slot, its line, that it sits between
    /// the actions and LOOT, and whether the slim no-goal row shows instead (Slim).
    /// </summary>
    internal (bool Shown, int Slots, bool Add, string Note, bool Placed, bool Slim) GoalsForTest()
    {
        int at = LeftCol.Children.IndexOf(GoalsCard);
        bool placed = at > 0 && LeftCol.Children.IndexOf(SessionCard) == at - 1 && LeftCol.Children.IndexOf(SummaryCard) == at + 1;
        var slots = GoalsHost.Children.OfType<FrameworkElement>().ToList();
        var note = string.Concat(GoalsNote.Inlines.OfType<Run>().Select(r => r.Text));
        bool card = GoalsCard.Visibility == Visibility.Visible;
        return (card && GoalsBox.Visibility == Visibility.Visible, slots.Count(s => s.Tag as string == "goal"), slots.Any(s => s.Tag as string == "add"), note, placed,
            card && GoalsSlim.Visibility == Visibility.Visible);
    }

    /// <summary>
    /// Sample goals over the sample day (KL_SHOT_STATE=goals: three, the expeditions one reached; goals-add: two and the "+"
    /// slot; goals-none: none, the slim row), for the layout renders and options-test. Example figures, never the player's
    /// goals or runs.
    /// </summary>
    internal void ShowGoalsSample(int count)
    {
        var now = DateTime.UtcNow;
        InstanceRun Exp(double hoursAgo) => new() { Zone = "Urugugu Canyon", Kind = "Expedition", Start = now.AddHours(-hoursAgo), LastLoot = now.AddHours(-hoursAgo).AddMinutes(7) };
        _demoGoalRuns = new() { Exp(2.2), Exp(1.4), Exp(0.6) };
        var all = new List<Goal>
        {
            new() { Kind = Goal.KindKinah, Target = 2_000_000 },
            new() { Kind = Goal.KindItem, Name = _items.NameOf(_items.IdOf("Spiritstone")), Target = 100 },
            new() { Kind = Goal.KindRuns, Which = Goal.Expeditions, Target = 3 },
        };
        _goals = all.Take(Math.Clamp(count, 0, Goals.Max)).ToList();
        var today = TodayGoalCounts();
        foreach (var g in _goals) Goals.Settle(g, ProgressOf(g, today), DayStore.Today);
        RedrawGoals();
    }

    /// <summary>Layout renders: a goal's hover card (the item's and the Kinah's) and the Set a goal sheet (Kinah, and from an item).</summary>
    internal void ShotGoalCards(string dir)
    {
        if (_goals.FirstOrDefault(g => g.Kind == Goal.KindItem) is { } item) SaveCard(GoalCard(item), System.IO.Path.Combine(dir, "ui-goal-card.png"));
        if (_goals.FirstOrDefault(g => g.Kind == Goal.KindKinah) is { } kinah) SaveCard(GoalCard(kinah), System.IO.Path.Combine(dir, "ui-goal-card-kinah.png"));
        SaveCard(BuildGoalSheet(new GoalDraft(), null), System.IO.Path.Combine(dir, "ui-goal-sheet.png"));
        SaveCard(BuildGoalSheet(GoalDraft.ForItem(_items.NameOf(_items.IdOf("Spiritstone"))), null), System.IO.Path.Combine(dir, "ui-goal-sheet-item.png"));
    }
}
