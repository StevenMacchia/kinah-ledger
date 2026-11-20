using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using KinahLedger.Session;

namespace KinahLedger.Ui;

/// <summary>
/// The Timers page (redesign 2026-11-14): the events and world bosses on a page of their own in the row of pages, so they are
/// never more than one click away (Steven: "I can not for the life of me find the tracker for the world boss spawns").
/// Minimal by default (Steven: "we THRIVE on being minimal"): the timers the player watches, then the events; each map's
/// other bosses stay folded under one line until opened. A row's bell puts that timer on the overlay and its chime
/// (Session/TimerBoard.cs, Ui/MainWindow.Timers.cs); the head has the overlay's switch, Pop out (the timers in a window of
/// their own, for a second screen) and the timers' settings (Options › Timers). The Timers tab carries the next watched
/// timer's countdown once it is under 15 minutes away.
/// </summary>
public partial class MainWindow
{
    DispatcherTimer? _timersPageTick;
    /// <summary>How close the next watched timer must be before its countdown rides on the Timers tab.</summary>
    static readonly TimeSpan TabCountdownFrom = TimeSpan.FromMinutes(15);
    /// <summary>A waiting timer this close shows its countdown in gold.</summary>
    static readonly TimeSpan TimerSoon = TimeSpan.FromMinutes(10);
    /// <summary>The maps whose folded bosses the player opened on the page (this session only).</summary>
    readonly HashSet<string> _timerGroupsOpen = new(StringComparer.Ordinal);
    Button? _timersPopOutBtn;
    /// <summary>
    /// What the page drawn last shows apart from its countdowns (each timer's place, state, colour and bell, the open maps, the
    /// event you're in): the second's tick redraws the page only when this changes, and otherwise sets the countdowns' text in
    /// place (it rebuilt every row each second until 1.0.28, which kept a core busy while the page showed).
    /// </summary>
    string? _timersShape;
    /// <summary>The countdowns on the page drawn last, by timer key ("live" for the event you're in, "fold:" + map for a folded map's line).</summary>
    readonly Dictionary<string, (TextBlock Big, TextBlock? Small)> _timerTexts = new(StringComparer.Ordinal);

    /// <summary>Starts the second's tick that keeps the tab's countdown, and the page while it shows, up to date.</summary>
    void StartTimersTick()
    {
        if (_timersPageTick != null || _demo) return;
        _timersPageTick = new DispatcherTimer(TimeSpan.FromSeconds(1), DispatcherPriority.Background, (_, _) =>
        {
            var now = DateTime.UtcNow;
            List<TimerBoard.Item> items;
            try { items = TimerItems(now); } catch (Exception ex) { CrashLog.Write("timers page", ex); return; }
            FillTimersTab(items, now);
            if (_part == "Timers" && !_welcome && !(TimersShape(items, now) == _timersShape && RefreshTimerTexts(items, now))) FillTimersPage(items, now);
        }, Dispatcher);
        _timersPageTick.Start();
    }

    /// <summary>The page, drawn only while it shows (and cleared when it doesn't).</summary>
    void FillTimersPage()
    {
        if (_part != "Timers" || _welcome) { if (TimersPage.Children.Count > 0) TimersPage.Children.Clear(); _timersShape = null; _timerTexts.Clear(); return; }
        StartTimersTick();
        var now = DateTime.UtcNow;
        List<TimerBoard.Item> items;
        try { items = TimerItems(now); } catch (Exception ex) { CrashLog.Write("timers page", ex); items = new(); }
        FillTimersTab(items, now);
        FillTimersPage(items, now);
    }

    void FillTimersPage(List<TimerBoard.Item> items, DateTime now)
    {
        double scroll = LowerScroll.VerticalOffset;
        _timersShape = TimersShape(items, now);
        _timerTexts.Clear();
        TimersPageDrawsForTest++;
        TimersPage.Children.Clear();
        TimersPage.Children.Add(TimersHead());
        if (EventClock.Current(now) is { LeftAt: null } live)
        {
            var stage = EventTimers.StageText(live);
            TimersPage.Children.Add(TimersCard(null, TimerLine(live.Label, live.Name.Length > 0 && live.Family.Length > 0 ? live.Family : "", stage,
                LiveSmall(live, now), live.Stage == Packets.AreaEvent.Stage.UnderWay ? "Good" : "Gold", null, textKey: "live")));
        }
        var watched = items.Where(i => IsTimerWatched(i.Key)).ToList();
        var rest = items.Where(i => !IsTimerWatched(i.Key)).ToList();
        bool mapRead = items.Any(i => i.Key.StartsWith("boss:", StringComparison.Ordinal));   // the Abyss's bosses are on a schedule; the others need the map once
        var watchedRows = watched.Select(TimerRow).ToList();
        if (watchedRows.Count == 0) watchedRows.Add(TimersNote(L.T("Ring the bell on any timer below to watch it: it shows on the overlay and chimes before it starts.")));
        TimersPage.Children.Add(TimersCard(L.T("Watched"), TimersGrid(watchedRows)));
        var events = rest.Where(i => i.Key.StartsWith("event:", StringComparison.Ordinal)).ToList();
        if (events.Count > 0) TimersPage.Children.Add(TimersCard(L.T("Events"), TimersGrid(events.Select(TimerRow))));
        foreach (var map in rest.Where(i => !i.Key.StartsWith("event:", StringComparison.Ordinal)).GroupBy(i => i.Group))
            TimersPage.Children.Add(TimersFold(map.Key, map.ToList()));
        if (!mapRead) TimersPage.Children.Add(TimersCard(L.T("World bosses"), TimersNote(L.T("Open the world map in the game (Exploration › Field Boss) once: the bosses' timers fill in here and count down after you close it."))));
        if (scroll > 0) { LowerScroll.UpdateLayout(); LowerScroll.ScrollToVerticalOffset(scroll); }
    }

    /// <summary>The page's head: its title on the left; the overlay's switch, Pop out and the timers' settings on the right.</summary>
    Grid TimersHead()
    {
        var g = new Grid { Margin = new Thickness(2, 0, 0, 12) };
        g.ColumnDefinitions.Add(new ColumnDefinition());
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var title = new TextBlock { Text = L.T("Timers"), FontSize = 20, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center };
        title.SetResourceReference(TextBlock.FontFamilyProperty, "DisplayFont");
        title.SetResourceReference(TextBlock.ForegroundProperty, "Ink");
        g.Children.Add(title);
        var right = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        // no Overlay switch here (Steven, 2026-11-18: "No settings should be from the app outside settings"): the header's
        // Overlays menu switches it, Settings › Timers sets it up (the gear)
        _timersPopOutBtn = new Button { Content = L.T("Pop out"), Height = 30, FontSize = 12.5, Padding = new Thickness(12, 0, 12, 0), VerticalAlignment = VerticalAlignment.Center,
            ToolTip = L.T("Opens every timer in a window of its own, to keep beside the game or on a second screen.") };
        System.Windows.Automation.AutomationProperties.SetName(_timersPopOutBtn, "Pop out the timers");
        _timersPopOutBtn.Click += (_, _) => OpenTimersPopOut();
        right.Children.Add(_timersPopOutBtn);
        var gear = new Button { Margin = new Thickness(6, 0, 0, 0), ToolTip = L.T("The timers' settings: how far ahead the overlay shows, the chime, which timers ring.") };
        gear.SetResourceReference(StyleProperty, "RoundBtn");
        var gi = new System.Windows.Shapes.Path(); gi.SetResourceReference(StyleProperty, "QIconStyle"); gi.SetResourceReference(System.Windows.Shapes.Path.DataProperty, "IcGear");
        gear.Content = gi;
        System.Windows.Automation.AutomationProperties.SetName(gear, "Timers settings");
        gear.Click += (_, _) => OpenOptions(OptionsWindow.Page.Timers);
        right.Children.Add(gear);
        Grid.SetColumn(right, 1); g.Children.Add(right);
        return g;
    }

    /// <summary>A card of the page: its heading (none for the event you're in) over its body.</summary>
    Border TimersCard(string? title, UIElement body)
    {
        var sp = new StackPanel();
        if (title != null) sp.Children.Add(TimersHeading(title, ""));
        sp.Children.Add(body);
        return TimersShell(sp);
    }

    Border TimersShell(UIElement child)
    {
        var b = new Border { Child = child, CornerRadius = Skins.Corner(12), BorderThickness = new Thickness(1), Padding = new Thickness(16, 12, 16, 8), Margin = new Thickness(0, 0, 0, 10) };
        b.SetResourceReference(Border.BackgroundProperty, "Card");
        b.SetResourceReference(Border.BorderBrushProperty, "Line");
        b.SetResourceReference(Border.BorderThicknessProperty, "CardEdge"); b.SetResourceReference(UIElement.EffectProperty, "CardFx");
        if (Skins.Current == HudThemes.Wacky) b.SetResourceReference(Border.BorderBrushProperty, "CardLine");
        SkinDeco.SetFrame(b, "card");   // the themed looks' frame (Ui/SkinDeco.cs)
        return b;
    }

    static Grid TimersHeading(string title, string note, UIElement? right = null)
    {
        var g = new Grid { Margin = new Thickness(0, 0, 0, 6) };
        g.ColumnDefinitions.Add(new ColumnDefinition());
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var sp = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        sp.Children.Add(QT(title, 12.5, FontWeights.SemiBold, "Ink"));
        if (note.Length > 0) { var n = QT(note, 12, FontWeights.Medium, "Faint"); n.Margin = new Thickness(8, 0, 0, 0); sp.Children.Add(n); }
        g.Children.Add(sp);
        if (right != null) { Grid.SetColumn(right, 1); g.Children.Add(right); }
        return g;
    }

    /// <summary>
    /// Rows two to a line, the pair as tall as the taller of the two (a long name wraps to a second line), so the columns
    /// stay level and a card never leaves a ragged gap.
    /// </summary>
    static Grid TimersGrid(IEnumerable<FrameworkElement> rows)
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        int n = 0;
        foreach (var r in rows)
        {
            if (n % 2 == 0) grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            Grid.SetRow(r, n / 2); Grid.SetColumn(r, n % 2);
            grid.Children.Add(r);
            n++;
        }
        return grid;
    }

    static FrameworkElement TimersNote(string text)
    {
        var t = QT(text, 12, FontWeights.Medium, "Muted");
        t.TextWrapping = TextWrapping.Wrap; t.TextTrimming = TextTrimming.None; t.Margin = new Thickness(0, 2, 14, 8);
        return t;
    }

    /// <summary>A map's bosses the player doesn't watch: one line saying how many, the soonest and a chevron; a click opens the rows.</summary>
    Border TimersFold(string group, List<TimerBoard.Item> items)
    {
        bool open = _timerGroupsOpen.Contains(group);
        var sp = new StackPanel();
        string note = FoldNote(group, items);
        var chev = new System.Windows.Shapes.Path { Data = Geometry.Parse(open ? "M0,4 L4,0 L8,4" : "M0,0 L4,4 L8,0"), StrokeThickness = 1.5, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 2, 0) };
        chev.SetResourceReference(Shape.StrokeProperty, "Muted");
        var head = TimersHeading(L.F("{0} world bosses", group), note, chev);
        if (head.Children[0] is StackPanel hs && hs.Children.Count > 1 && hs.Children[1] is TextBlock nt) _timerTexts["fold:" + group] = (nt, null);
        head.Background = Brushes.Transparent; head.Cursor = Cursors.Hand;
        if (!open) head.Margin = new Thickness(0, 0, 0, 4);
        head.MouseLeftButtonUp += (_, e) => { if (!_timerGroupsOpen.Remove(group)) _timerGroupsOpen.Add(group); FillTimersPage(); e.Handled = true; };
        sp.Children.Add(head);
        if (open) sp.Children.Add(TimersGrid(items.Select(TimerRow)));
        return TimersShell(sp);
    }

    /// <summary>One timer: its name and place, its countdown and when, and the bell that watches it.</summary>
    FrameworkElement TimerRow(TimerBoard.Item i)
    {
        bool soon = i.State == TimerBoard.Kind.Waiting && i.AtPc is { } at && at - DateTime.UtcNow <= TimerSoon;
        string tone = i.State switch { TimerBoard.Kind.On => "Good", TimerBoard.Kind.Due => "Warn", TimerBoard.Kind.WasUp => "Muted", _ => soon ? "Gold" : "Ink" };
        return TimerLine(i.Name, i.Sub, i.Big, i.Small, tone, i.Key);
    }

    FrameworkElement TimerLine(string name, string sub, string big, string small, string tone, string? key, string? textKey = null)
    {
        var g = new Grid { MinHeight = 42, Margin = new Thickness(0, 4, 14, 5) };
        g.ColumnDefinitions.Add(new ColumnDefinition());
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        if (key != null) g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(26) });
        sub = TimerSay.Text(sub); big = TimerSay.Text(big); small = TimerSay.Text(small);
        var left = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        var nm = QT(name, 12.5, FontWeights.SemiBold, "Ink");
        nm.TextWrapping = TextWrapping.Wrap; nm.TextTrimming = TextTrimming.None;   // a full name always shows: a long one takes a second line
        left.Children.Add(nm);
        if (sub.Length > 0) left.Children.Add(QT(sub, 11, FontWeights.Medium, "Faint", trim: true));
        g.Children.Add(left);
        var right = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, key != null ? 6 : 0, 0) };
        var b = QT(big, 13, FontWeights.SemiBold, tone); b.HorizontalAlignment = HorizontalAlignment.Right;
        System.Windows.Documents.Typography.SetNumeralAlignment(b, FontNumeralAlignment.Tabular);   // the seconds tick without the line shifting
        right.Children.Add(b);
        TextBlock? s = null;
        if (small.Length > 0) { s = QT(small, 11, FontWeights.Medium, "Faint"); s.HorizontalAlignment = HorizontalAlignment.Right; right.Children.Add(s); }
        if ((key ?? textKey) is { } tk) _timerTexts[tk] = (b, s);
        Grid.SetColumn(right, 1); g.Children.Add(right);
        if (key != null) { var bell = BellButton(key, name); Grid.SetColumn(bell, 2); g.Children.Add(bell); }
        var rule = new Border { BorderThickness = new Thickness(0, 0, 0, 1), Margin = new Thickness(0, 0, 14, 0) };
        rule.SetResourceReference(Border.BorderBrushProperty, "CardHair");
        var host = new Grid(); host.Children.Add(rule); host.Children.Add(g);
        return host;
    }

    /// <summary>The bell: filled gold while the timer is watched (on the overlay, with its chime), an outline while it isn't; a click switches it.</summary>
    FrameworkElement BellButton(string key, string name)
    {
        bool on = IsTimerWatched(key);
        var geo = Geometry.Parse("M6,1 C3.6,1 2,2.9 2,5.2 L2,8.2 L0.6,10.2 L11.4,10.2 L10,8.2 L10,5.2 C10,2.9 8.4,1 6,1 Z M4.4,11.4 C4.7,12.4 5.3,12.9 6,12.9 C6.7,12.9 7.3,12.4 7.6,11.4 Z");
        var p = new System.Windows.Shapes.Path { Data = geo, Stretch = Stretch.Uniform, Width = 14, Height = 14, StrokeThickness = 1.2 };
        if (on) p.SetResourceReference(Shape.FillProperty, "Gold");
        else { p.SetResourceReference(Shape.StrokeProperty, "Faint"); }
        var b = new Border { Child = p, Width = 24, Height = 24, CornerRadius = new CornerRadius(6), Background = Brushes.Transparent, Cursor = Cursors.Hand, Focusable = true,
            HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center, Tag = "bell:" + key,
            ToolTip = on ? L.F("Watched: {0} shows on the overlay and chimes before it starts. Click to stop.", name) : L.F("Watch {0}: it shows on the overlay and chimes before it starts.", name) };
        System.Windows.Automation.AutomationProperties.SetName(b, (on ? "Stop watching " : "Watch ") + name);
        void Flip() { SetTimerWatched(key, !IsTimerWatched(key)); FillTimersPage(); }
        b.MouseLeftButtonUp += (_, e) => { Flip(); e.Handled = true; };
        b.KeyDown += (_, e) => { if (e.Key is Key.Enter or Key.Space) { Flip(); e.Handled = true; } };
        return b;
    }

    /// <summary>A folded map's line: how many, and the soonest while it is folded.</summary>
    string FoldNote(string group, List<TimerBoard.Item> items)
    {
        var soonest = items.Where(i => i.State == TimerBoard.Kind.Waiting).OrderBy(i => i.AtPc).FirstOrDefault();
        return string.Format(L.T("{0} more", "timers"), items.Count) + (!_timerGroupsOpen.Contains(group) && soonest != null ? L.F(" · next {0} in {1}", soonest.Short, TimerSay.Text(soonest.Big)) : "");
    }

    static string LiveSmall(EventTimers.Live live, DateTime now) => live.StartedPc is { } st ? $"began {BossTimers.Ago(now - st)}" : "";

    static bool IsMapTimer(TimerBoard.Item i) => !i.Key.StartsWith("event:", StringComparison.Ordinal);

    /// <summary>Everything the page shows but its countdowns (<see cref="_timersShape"/>).</summary>
    string TimersShape(List<TimerBoard.Item> items, DateTime now)
    {
        var sb = new System.Text.StringBuilder();
        sb.Append(Skins.Current).Append('|').Append(L.T("Timers")).Append('|').Append(string.Join(",", _timerGroupsOpen.OrderBy(g => g, StringComparer.Ordinal))).Append(';');
        if (EventClock.Current(now) is { LeftAt: null } live)
            sb.Append("live|").Append(live.Label).Append('|').Append(live.Name).Append('|').Append(live.Family).Append('|').Append(live.Stage).Append('|').Append(LiveSmall(live, now).Length > 0).Append(';');
        foreach (var i in items)
        {
            bool soon = i.State == TimerBoard.Kind.Waiting && i.AtPc is { } at && at - DateTime.UtcNow <= TimerSoon;
            sb.Append(i.Key).Append('|').Append(i.Group).Append('|').Append(i.Name).Append('|').Append(i.Sub).Append('|').Append(i.State).Append('|')
              .Append(IsTimerWatched(i.Key)).Append('|').Append(soon).Append('|').Append(i.Small.Length > 0).Append(';');
        }
        return sb.ToString();
    }

    /// <summary>The second's tick on an unchanged page: each countdown's text set in place. False when one isn't on the page (redraw it).</summary>
    bool RefreshTimerTexts(List<TimerBoard.Item> items, DateTime now)
    {
        if (TimersPage.Children.Count == 0) return false;
        static void Set(TextBlock t, string v) { if (t.Text != v) t.Text = v; }
        if (EventClock.Current(now) is { LeftAt: null } live)
        {
            if (!_timerTexts.TryGetValue("live", out var lt)) return false;
            Set(lt.Big, TimerSay.Text(EventTimers.StageText(live)));
            if (lt.Small != null) Set(lt.Small, TimerSay.Text(LiveSmall(live, now)));
        }
        var rest = items.Where(i => !IsTimerWatched(i.Key)).ToList();
        foreach (var i in items.Where(i => IsTimerWatched(i.Key)).Concat(rest.Where(i => !IsMapTimer(i))))
        {
            if (!_timerTexts.TryGetValue(i.Key, out var t)) return false;
            Set(t.Big, TimerSay.Text(i.Big));
            if (t.Small != null) Set(t.Small, TimerSay.Text(i.Small));
        }
        foreach (var map in rest.Where(IsMapTimer).GroupBy(i => i.Group))
        {
            if (!_timerTexts.TryGetValue("fold:" + map.Key, out var f)) return false;
            Set(f.Big, FoldNote(map.Key, map.ToList()));
            if (!_timerGroupsOpen.Contains(map.Key)) continue;
            foreach (var i in map)
            {
                if (!_timerTexts.TryGetValue(i.Key, out var t)) return false;
                Set(t.Big, TimerSay.Text(i.Big));
                if (t.Small != null) Set(t.Small, TimerSay.Text(i.Small));
            }
        }
        return true;
    }

    /// <summary>For the options self-test: how many times the page was drawn whole (the second's tick only redraws a changed page).</summary>
    internal int TimersPageDrawsForTest { get; private set; }
    /// <summary>For the options self-test: every text on the page, in order.</summary>
    internal string TimersPageTextForTest() => string.Join("|", Descendants<TextBlock>(TimersPage).Select(t => t.Text));
    /// <summary>For the options self-test: one second's tick of the page, as its timer runs it.</summary>
    internal void TickTimersPageForTest(DateTime now)
    {
        var items = TimerItems(now);
        if (!(TimersShape(items, now) == _timersShape && RefreshTimerTexts(items, now))) FillTimersPage(items, now);
    }

    /// <summary>The Timers tab's countdown: the soonest watched timer when it is under <see cref="TabCountdownFrom"/>, else nothing.</summary>
    void FillTimersTab(List<TimerBoard.Item> items, DateTime now)
    {
        var next = items.Where(i => i.State == TimerBoard.Kind.Waiting && i.AtPc is { } at && at > now && IsTimerWatched(i.Key)).OrderBy(i => i.AtPc).FirstOrDefault();
        if (next?.AtPc is { } when && when - now <= TabCountdownFrom)
        {
            TimersTabNext.Text = TimerBoard.Clock(when - now);
            TimersTabNext.ToolTip = L.F("{0} in {1}", next.Name, TimerBoard.Clock(when - now));
            TimersTabNext.Visibility = Visibility.Visible;
        }
        else TimersTabNext.Visibility = Visibility.Collapsed;
    }

    /// <summary>For the options self-test: the page's Pop out button, and the bells drawn (key, watched).</summary>
    internal Button? TimersPopOutForTest => _timersPopOutBtn;
    internal List<(string Key, bool On)> TimerBellsForTest() =>
        Descendants<Border>(TimersPage).Where(b => b.Tag is string t && t.StartsWith("bell:", StringComparison.Ordinal)).Select(b => (((string)b.Tag)[5..], IsTimerWatched(((string)b.Tag)[5..]))).ToList();
    /// <summary>For the options self-test: a map's folded bosses opened, as a click on its line does.</summary>
    internal void OpenTimerGroupForTest(string group) { _timerGroupsOpen.Add(group); FillTimersPage(); }
    /// <summary>For the options self-test: a click on a timer's bell on the page.</summary>
    internal void ClickTimerBellForTest(string key)
    {
        foreach (var b in Descendants<Border>(TimersPage).Where(b => b.Tag as string == "bell:" + key).ToList())
        { b.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left) { RoutedEvent = UIElement.MouseLeftButtonUpEvent }); return; }
    }

    static IEnumerable<T> Descendants<T>(DependencyObject d) where T : DependencyObject
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(d); i++)
        {
            var c = VisualTreeHelper.GetChild(d, i);
            if (c is T t) yield return t;
            foreach (var x in Descendants<T>(c)) yield return x;
        }
    }
}

public partial class MainWindow
{
    /// <summary>ui-shot --timers-page and options-test: the page's sample readings, as the timers' window renders take them (live or empty; off puts the real ones back).</summary>
    internal void TimersSampleForShot(string state)
    {
        if (state == "off") { _bossesForShot = null; _eventsForShot = null; return; }
        var now = DateTime.UtcNow;
        var t = new BossTimers();
        if (state != "empty") t.SetForTest(SampleBosses(now.AddSeconds(-4)));
        _bossesForShot = t;
        var ev = new EventTimers();
        ev.SetForTest(state == "empty" ? TimeSpan.Zero : TimeSpan.FromSeconds(14.6), state != "empty", state == "live"
            ? new EventTimers.Live("Shugo Festival", "Defend Shugo Merchants", Packets.AreaEvent.Stage.UnderWay, now.AddSeconds(-70), now.AddSeconds(-1), null, null) : null);
        _eventsForShot = ev;
    }
}
