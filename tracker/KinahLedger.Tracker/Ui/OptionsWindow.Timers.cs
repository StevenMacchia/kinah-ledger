using System.Windows;
using System.Windows.Controls;
using KinahLedger.Session;

namespace KinahLedger.Ui;

/// <summary>
/// Options › Timers (0.10.85, Steven 2026-11-14: "the toggles for the timers should be in the settings ... make them
/// scrollable"; the settings revamp, 2026-11-17, made it the third overlay page): the strip drawn over the game first, then
/// the timers overlay (separate, how close a timer must be to show, how many at once, its spot and click-through), the
/// chime (on, how long before, how loud), and under folds the Timers window's details and, timer by timer, which ones are
/// watched (on the overlay, with the chime) and which of those chime. These save as they change (timers.json), apart from
/// Options' Save: the overlay and the chime act at once, as the player tries them.
/// </summary>
public partial class OptionsWindow
{
    /// <summary>The page's boxes per timer, by key: (watched: on the overlay, its chime). For the options self-test.</summary>
    internal readonly Dictionary<string, (CheckBox Overlay, CheckBox Chime)> TimerBoxes = new();
    internal CheckBox? TimersOverlayChk { get; private set; }
    internal CheckBox? TimersSeparateChk { get; private set; }
    internal Button? TimersResetBtn { get; private set; }
    internal CheckBox? TimersDetailsChk { get; private set; }

    /// <summary>The preview's two timers: a world boss three minutes away and an event in its last five (gold).</summary>
    static List<TimerBoard.Item> TimersSample(DateTime now) => new()
    {
        new("event:Dimensional Invasion", "Events", "Dimensional Invasion", "", TimerBoard.Kind.Waiting, now.AddSeconds(279), "4m 39s", "") { Short = "Invasion" },
        new("boss:Heretic Layla", "Verteron", "Heretic Layla", "Rock Tomb Hill", TimerBoard.Kind.Waiting, now.AddMinutes(12), "12m 00s", "at 07:00"),
    };

    void FillTimers()
    {
        PageTimers.Children.Clear();
        TimerBoxes.Clear();
        var p = _host.TimerChoices;
        void Add(UIElement e) => PageTimers.Children.Add(e);

        // the header: title, lead, and the overlay's on/off
        TimersOverlayChk = Switch(p.OverlayOn, on => _host.SetTimersOverlay(on));
        TimersOverlayChk.ToolTip = L.T("A tiny line on top of the Kinah overlay while a watched timer is close; it folds away once the boss is up or the event starts.");
        Add(OverlayHeader(Styled(new TextBlock { Text = L.T("Timers") }, "PageTitle"),
            Styled(new TextBlock { Text = L.T("The events and world bosses you watch, over the game as one comes close. These save as you change them."),
                ToolTip = L.T("The events and world bosses you watch, in a tiny line over the game only as one comes close, and a soft chime before. These save as you change them.") }, "PageLead"),
            TimersOverlayChk, "Show the timers over the game"));

        // the stage: the strip with two sample timers, at the Kinah overlay's size
        var now = DateTime.UtcNow;
        double scale = _edit.HudScale > 0 ? _edit.HudScale : 1;
        try { Add(Margined(Stage(TimersOverlay.PreviewFace(TimersSample(now), now, scale), L.T("Timers"), L.T("a sample: one event in its last 5 minutes, one boss"), 104), new Thickness(0, 12, 0, 0))); }
        catch (Exception ex) { CrashLog.Write("timers preview", ex); }

        // the overlay and the chime, one group
        var g = new StackPanel();
        TimersSeparateChk = Switch(p.Separate, on => { _host.SetTimersSeparate(on); FillTimers(); });
        g.Children.Add(Row("Separate timer overlay", "A strip of its own to place anywhere, apart from the Kinah overlay.", TimersSeparateChk,
            "The timers in a small strip of their own, apart from the Kinah overlay and the parser, to place anywhere. With the Kinah overlay off they use it anyway."));
        g.Children.Add(Styled(new Border(), "Divider"));
        g.Children.Add(PairRow(
            ("Show a timer from", Choice(TimerPrefs.MinuteChoices, m => L.F("{0} min before", m), p.WindowMinutes, m => { p.WindowMinutes = m; p.Save(); }), "How long before it starts or spawns. A timer turns gold in its last 5 minutes."),
            ("At most", Choice(TimerPrefs.CountChoices, c => L.F("{0} timers", c), p.Count, c => { p.Count = c; p.Save(); }), "Timers shown at once (3 at most on top of the Kinah overlay).")));
        TimersResetBtn = new Button { Content = L.T("Reset position"), ToolTip = L.T("Back to just above the Kinah overlay."), Margin = new Thickness(0, 0, 18, 0) };
        TimersResetBtn.Click += (_, _) => _host.ResetTimersOverlayPosition();
        bool strip = p.Separate || !_host.KinahOverlayOn;
        if (strip)
        {
            // its own strip: its spot and click-through on one row
            var through = Switch(p.ClickThrough, on => _host.SetTimersClickThrough(on));
            System.Windows.Automation.AutomationProperties.SetName(through, "Timers click-through");
            var said = new TextBlock { Text = L.T("Click-through"), FontSize = 13, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0),
                ToolTip = L.T("On: clicks pass through it to the game; its left edge stays grabbable, with a grip when you point at it.") };
            said.SetResourceReference(TextBlock.ForegroundProperty, "Ink");
            g.Children.Add(Styled(new Border(), "Divider"));
            g.Children.Add(Row("Where it sits", "Drag the strip anywhere, as the Kinah overlay.",
                new StackPanel { Orientation = Orientation.Horizontal, Children = { TimersResetBtn, said, through } }));
        }
        g.Children.Add(Styled(new Border(), "Divider"));
        g.Children.Add(Row("Chime before a watched timer", "A soft chime, once. Turn it off for one timer below.", Switch(p.ChimeOn, on => { p.ChimeOn = on; p.Save(); })));
        g.Children.Add(Styled(new Border(), "Divider"));
        var vol = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        var slider = new Slider { Minimum = 0, Maximum = 1, Value = p.Volume, Width = 130, VerticalAlignment = VerticalAlignment.Center, SmallChange = 0.05, LargeChange = 0.1 };
        System.Windows.Automation.AutomationProperties.SetName(slider, "Chime volume");
        slider.ValueChanged += (_, e) => { p.Volume = Math.Round(e.NewValue, 2); p.Save(); };
        var test = new Button { Content = L.T("Test"), Margin = new Thickness(10, 0, 0, 0) };
        test.Click += (_, _) => _host.PlayChime(p.Volume);
        vol.Children.Add(slider); vol.Children.Add(test);
        g.Children.Add(PairRow(("How long before", Choice(TimerPrefs.MinuteChoices, m => L.F("{0} min", m), p.ChimeMinutes, m => { p.ChimeMinutes = m; p.Save(); }), null), ("Volume", vol, null)));
        Add(Styled(new Border { Child = g, Margin = new Thickness(0, 10, 0, 0) }, "Group"));

        // More: the Timers window
        var more = new StackPanel { Tag = "More" };
        var win = new StackPanel();
        TimersDetailsChk = Switch(p.Details, on => { p.Details = on; p.Save(); });
        win.Children.Add(Row("Details", "Each row's second line: the place, how often, the time it starts.", TimersDetailsChk));
        win.Children.Add(Styled(new Border(), "Divider"));
        var pop = new Button { Content = L.T("Open") };
        pop.Click += (_, _) => _host.OpenTimersPopOut();
        win.Children.Add(Row("The full list", "Events and every map's world bosses, in a window you can keep on a second screen.", pop));
        more.Children.Add(Styled(new TextBlock { Text = L.T("TIMERS WINDOW") }, "GroupLabel"));
        more.Children.Add(Styled(new Border { Child = win }, "Group"));
        Add(more);
        Fold(more);

        // timer by timer, folded: which ones are watched and which chime
        var which = new StackPanel { Tag = "Which timers" };
        var items = _host.TimerItemsNow();
        foreach (var group in items.GroupBy(i => i.Group).OrderBy(g => g.Key == "Events" ? 0 : g.Key.Contains("Reshanta") ? 2 : 1))
        {
            which.Children.Add(Styled(new TextBlock { Text = group.Key == "Events" ? L.T("EVENTS") : group.Key.Contains("Reshanta") ? group.Key.ToUpperInvariant() + L.T(" · ABYSS") : group.Key == "World bosses" ? L.T("WORLD BOSSES") : group.Key.ToUpperInvariant() + L.T(" · WORLD BOSSES") }, "GroupLabel"));
            var list = new StackPanel();
            bool first = true;
            foreach (var i in group.OrderBy(i => i.Name, StringComparer.OrdinalIgnoreCase))
            {
                if (TimerBoxes.ContainsKey(i.Key)) continue;
                if (!first) list.Children.Add(Styled(new Border(), "Divider"));
                first = false;
                var key = i.Key;
                var ov = new CheckBox { Content = L.T("Watch"), IsChecked = p.Watched(key), Margin = new Thickness(0, 0, 16, 0), VerticalAlignment = VerticalAlignment.Center };
                var ch = new CheckBox { Content = L.T("Chime"), IsChecked = p.Chimes(key), IsEnabled = p.Watched(key), VerticalAlignment = VerticalAlignment.Center };
                ov.Click += (_, _) => { _host.SetTimerWatched(key, ov.IsChecked == true); ch.IsEnabled = ov.IsChecked == true; ch.IsChecked = p.Chimes(key); RefreshFoldCounts(); };
                ch.Click += (_, _) => _host.SetTimerChime(key, ch.IsChecked == true);
                System.Windows.Automation.AutomationProperties.SetName(ov, $"Watch {i.Name}");
                System.Windows.Automation.AutomationProperties.SetName(ch, $"Chime before {i.Name}");
                var boxes = new StackPanel { Orientation = Orientation.Horizontal };
                boxes.Children.Add(ov); boxes.Children.Add(ch);
                TimerBoxes[key] = (ov, ch);
                list.Children.Add(Row(i.Name, i.Sub, boxes, raw: true));
            }
            which.Children.Add(Styled(new Border { Child = list }, "Group"));
        }
        if (!items.Any(i => i.Key.StartsWith("boss:", StringComparison.Ordinal)))
            which.Children.Add(Styled(new TextBlock { Text = L.T("Verteron's world bosses show here once the game's world map has been opened (Exploration › Field Boss). See Game reading."), Margin = new Thickness(4, 10, 0, 0), TextWrapping = TextWrapping.Wrap }, "RowHint"));
        Add(which);
        Fold(which, () => L.F("{0} of {1} watched", TimerBoxes.Values.Count(b => b.Overlay.IsChecked == true), TimerBoxes.Count));
    }

    static FrameworkElement Margined(FrameworkElement e, Thickness m) { e.Margin = m; return e; }

    /// <summary>A row in the page's look: a title and a hint on the left, a control on the right (the hint's long form on hover).</summary>
    Grid Row(string title, string hint, FrameworkElement control, string? tip = null, bool raw = false)
    {
        var g = Styled(new Grid(), "Row");
        g.ColumnDefinitions.Add(new ColumnDefinition()); g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var left = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        left.Children.Add(Styled(new TextBlock { Text = raw ? title : L.T(title) }, "RowTitle"));   // a timer's name as the game writes it
        if (hint.Length > 0) left.Children.Add(Styled(new TextBlock { Text = raw ? hint : L.T(hint), TextWrapping = TextWrapping.Wrap, ToolTip = tip == null ? null : L.T(tip) }, "RowHint"));
        g.Children.Add(left);
        control.Margin = new Thickness(16, 0, 0, 0);
        control.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(control, 1); g.Children.Add(control);
        return g;
    }

    /// <summary>Two short settings on one row, each its title then its control (the title's hint on hover).</summary>
    Grid PairRow((string Title, FrameworkElement Control, string? Tip) a, (string Title, FrameworkElement Control, string? Tip) b)
    {
        var g = Styled(new Grid(), "Row");
        foreach (var w in new[] { GridLength.Auto, new GridLength(1, GridUnitType.Star), GridLength.Auto, GridLength.Auto }) g.ColumnDefinitions.Add(new ColumnDefinition { Width = w });
        int col = 0;
        foreach (var (title, control, tip) in new[] { a, b })
        {
            var t = Styled(new TextBlock { Text = L.T(title), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 12, 0), ToolTip = tip == null ? null : L.T(tip) }, "RowTitle");
            Grid.SetColumn(t, col++); g.Children.Add(t);
            control.VerticalAlignment = VerticalAlignment.Center;
            control.HorizontalAlignment = HorizontalAlignment.Left;
            control.Margin = new Thickness(0, 0, 16, 0);
            Grid.SetColumn(control, col++); g.Children.Add(control);
        }
        return g;
    }

    CheckBox Switch(bool on, Action<bool> set)
    {
        var c = Styled(new CheckBox { IsChecked = on }, "Switch");
        c.Click += (_, _) => set(c.IsChecked == true);
        return c;
    }

    static ComboBox Choice(IEnumerable<int> values, Func<int, string> text, int now, Action<int> set)
    {
        var list = values.ToList();
        if (!list.Contains(now)) list.Add(now);
        list.Sort();
        var box = new ComboBox { Width = 140, ItemsSource = list.Select(text).ToList(), SelectedIndex = list.IndexOf(now) };
        box.SelectionChanged += (_, _) => { if (box.SelectedIndex >= 0) set(list[box.SelectedIndex]); };
        return box;
    }
}
