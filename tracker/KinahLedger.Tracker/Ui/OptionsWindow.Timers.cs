using System.Windows;
using System.Windows.Controls;
using KinahLedger.Session;

namespace KinahLedger.Ui;

/// <summary>
/// Options › Timers (0.10.85, Steven 2026-11-14: "the toggles for the timers should be in the settings ... make them
/// scrollable"): the timers overlay over the game (on, click-through, how close a timer must be to show, how many at once),
/// the chime (on, how long before, how loud), the Timers window's details and, timer by timer, which ones are watched (on the
/// overlay, with the chime) and which of those chime. The page scrolls with the rest of Options. These save as they change
/// (timers.json), apart from Options' Save: the overlay and the chime act at once, as the player tries them.
/// </summary>
public partial class OptionsWindow
{
    /// <summary>The page's boxes per timer, by key: (watched: on the overlay, its chime). For the options self-test.</summary>
    internal readonly Dictionary<string, (CheckBox Overlay, CheckBox Chime)> TimerBoxes = new();
    internal CheckBox? TimersOverlayChk { get; private set; }
    internal Button? TimersMoveBtn { get; private set; }
    internal Button? TimersResetBtn { get; private set; }
    internal CheckBox? TimersDetailsChk { get; private set; }

    void FillTimers()
    {
        PageTimers.Children.Clear();
        TimerBoxes.Clear();
        var p = _host.TimerChoices;
        void Add(UIElement e) => PageTimers.Children.Add(e);
        Add(Styled(new TextBlock { Text = "Timers" }, "PageTitle"));
        Add(Styled(new TextBlock { Text = "A small strip over the game with the events and world bosses you watch, shown only as one comes close, and a soft chime before. These save as you change them." }, "PageLead"));

        // the overlay
        Add(Styled(new TextBlock { Text = "OVERLAY" }, "GroupLabel"));
        var overlay = new StackPanel();
        TimersOverlayChk = Switch(p.OverlayOn, on => _host.SetTimersOverlay(on));
        overlay.Children.Add(Row("Show the timers overlay", "Apart from the panel and the parser. Hidden until a watched timer is close; it hides again once the boss is up or the event starts.", TimersOverlayChk));
        overlay.Children.Add(Styled(new Border(), "Divider"));
        var place = new StackPanel { Orientation = Orientation.Horizontal };
        TimersMoveBtn = new Button { Content = "Move", Margin = new Thickness(0, 0, 8, 0), ToolTip = "Shows the overlay now so you can drag it where you want it. Done or Esc keeps the spot." };
        TimersMoveBtn.Click += (_, _) => _host.StartTimersPlacement();
        TimersResetBtn = new Button { Content = "Reset position", ToolTip = "Back to the top right of the main screen." };
        TimersResetBtn.Click += (_, _) => _host.ResetTimersOverlayPosition();
        place.Children.Add(TimersMoveBtn); place.Children.Add(TimersResetBtn);
        overlay.Children.Add(Row("Where it sits", "Shows it now, with the next timers, to drag anywhere on any screen.", place));
        overlay.Children.Add(Styled(new Border(), "Divider"));
        overlay.Children.Add(Row("Clicks go through it", "On: it never blocks the game. Hold Ctrl to drag it or to right-click it for its menu.", Switch(p.ClickThrough, on => _host.SetTimersClickThrough(on))));
        overlay.Children.Add(Styled(new Border(), "Divider"));
        overlay.Children.Add(Row("Show a timer from", "How long before it starts or spawns. A timer turns gold in its last 5 minutes.",
            Choice(TimerPrefs.MinuteChoices, m => $"{m} min before", p.WindowMinutes, m => { p.WindowMinutes = m; p.Save(); })));
        overlay.Children.Add(Styled(new Border(), "Divider"));
        overlay.Children.Add(Row("At most", "Timers on the overlay at once.", Choice(TimerPrefs.CountChoices, c => $"{c} timers", p.Count, c => { p.Count = c; p.Save(); })));
        Add(Styled(new Border { Child = overlay }, "Group"));

        // the chime
        Add(Styled(new TextBlock { Text = "CHIME" }, "GroupLabel"));
        var chime = new StackPanel();
        chime.Children.Add(Row("Chime before a watched timer", "A soft chime, once. Turn it off for one timer below.", Switch(p.ChimeOn, on => { p.ChimeOn = on; p.Save(); })));
        chime.Children.Add(Styled(new Border(), "Divider"));
        chime.Children.Add(Row("How long before", "", Choice(TimerPrefs.MinuteChoices, m => $"{m} min", p.ChimeMinutes, m => { p.ChimeMinutes = m; p.Save(); })));
        chime.Children.Add(Styled(new Border(), "Divider"));
        var vol = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        var slider = new Slider { Minimum = 0, Maximum = 1, Value = p.Volume, Width = 160, VerticalAlignment = VerticalAlignment.Center, SmallChange = 0.05, LargeChange = 0.1 };
        System.Windows.Automation.AutomationProperties.SetName(slider, "Chime volume");
        slider.ValueChanged += (_, e) => { p.Volume = Math.Round(e.NewValue, 2); p.Save(); };
        var test = new Button { Content = "Test", Margin = new Thickness(10, 0, 0, 0) };
        test.Click += (_, _) => _host.PlayChime(p.Volume);
        vol.Children.Add(slider); vol.Children.Add(test);
        chime.Children.Add(Row("Volume", "", vol));
        Add(Styled(new Border { Child = chime }, "Group"));

        // the Timers window
        Add(Styled(new TextBlock { Text = "TIMERS WINDOW" }, "GroupLabel"));
        var win = new StackPanel();
        TimersDetailsChk = Switch(p.Details, on => { p.Details = on; p.Save(); });
        win.Children.Add(Row("Details", "Each row's second line: the place, how often, the time it starts.", TimersDetailsChk));
        win.Children.Add(Styled(new Border(), "Divider"));
        var pop = new Button { Content = "Open" };
        pop.Click += (_, _) => _host.OpenTimersPopOut();
        win.Children.Add(Row("The full list", "Events and every map's world bosses, in a window you can keep on a second screen.", pop));
        Add(Styled(new Border { Child = win }, "Group"));

        // timer by timer
        var items = _host.TimerItemsNow();
        foreach (var group in items.GroupBy(i => i.Group).OrderBy(g => g.Key == "Events" ? 0 : g.Key.Contains("Reshanta") ? 2 : 1))
        {
            Add(Styled(new TextBlock { Text = group.Key == "Events" ? "EVENTS" : group.Key.Contains("Reshanta") ? group.Key.ToUpperInvariant() + " · ABYSS" : group.Key.ToUpperInvariant() + " · WORLD BOSSES" }, "GroupLabel"));
            var list = new StackPanel();
            bool first = true;
            foreach (var i in group.OrderBy(i => i.Name, StringComparer.OrdinalIgnoreCase))
            {
                if (TimerBoxes.ContainsKey(i.Key)) continue;
                if (!first) list.Children.Add(Styled(new Border(), "Divider"));
                first = false;
                var key = i.Key;
                var ov = new CheckBox { Content = "Watch", IsChecked = p.Watched(key), Margin = new Thickness(0, 0, 16, 0), VerticalAlignment = VerticalAlignment.Center };
                var ch = new CheckBox { Content = "Chime", IsChecked = p.Chimes(key), IsEnabled = p.Watched(key), VerticalAlignment = VerticalAlignment.Center };
                ov.Click += (_, _) => { _host.SetTimerWatched(key, ov.IsChecked == true); ch.IsEnabled = ov.IsChecked == true; ch.IsChecked = p.Chimes(key); };
                ch.Click += (_, _) => _host.SetTimerChime(key, ch.IsChecked == true);
                System.Windows.Automation.AutomationProperties.SetName(ov, $"Watch {i.Name}");
                System.Windows.Automation.AutomationProperties.SetName(ch, $"Chime before {i.Name}");
                var boxes = new StackPanel { Orientation = Orientation.Horizontal };
                boxes.Children.Add(ov); boxes.Children.Add(ch);
                TimerBoxes[key] = (ov, ch);
                list.Children.Add(Row(i.Name, i.Sub, boxes));
            }
            Add(Styled(new Border { Child = list }, "Group"));
        }
        if (!items.Any(i => i.Key.StartsWith("boss:", StringComparison.Ordinal)))
            Add(Styled(new TextBlock { Text = "Verteron's world bosses show here once the game's world map has been opened (Exploration › Field Boss) with packet reading on (Game reading).", Margin = new Thickness(4, 10, 0, 0), TextWrapping = TextWrapping.Wrap }, "RowHint"));
    }

    /// <summary>A row in the page's look: a title and a hint on the left, a control on the right.</summary>
    Grid Row(string title, string hint, FrameworkElement control)
    {
        var g = Styled(new Grid(), "Row");
        g.ColumnDefinitions.Add(new ColumnDefinition()); g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var left = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        left.Children.Add(Styled(new TextBlock { Text = title }, "RowTitle"));
        if (hint.Length > 0) left.Children.Add(Styled(new TextBlock { Text = hint, TextWrapping = TextWrapping.Wrap }, "RowHint"));
        g.Children.Add(left);
        control.Margin = new Thickness(16, 0, 0, 0);
        control.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(control, 1); g.Children.Add(control);
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
        var box = new ComboBox { Width = 150, ItemsSource = list.Select(text).ToList(), SelectedIndex = list.IndexOf(now) };
        box.SelectionChanged += (_, _) => { if (box.SelectedIndex >= 0) set(list[box.SelectedIndex]); };
        return box;
    }
}
