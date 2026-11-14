using System.Windows;
using System.Windows.Controls;
using KinahLedger.Session;

namespace KinahLedger.Ui;

/// <summary>
/// Options › Timers (0.10.85, Steven 2026-11-14: "the toggles for the timers should be in the settings ... make them
/// scrollable"): the timers overlay over the game (on, click-through, how close a timer must be to show, how many at once),
/// the chime (on, how long before, how loud) and, timer by timer, which ones go on the overlay and which chime. The page
/// scrolls with the rest of Options. These save as they change (timers.json), apart from Options' Save: the overlay and the
/// chime act at once, as the player tries them.
/// </summary>
public partial class OptionsWindow
{
    /// <summary>The page's switches per timer, by key: (on the overlay, chime). For the options self-test.</summary>
    internal readonly Dictionary<string, (CheckBox Overlay, CheckBox Chime)> TimerBoxes = new();
    internal CheckBox? TimersOverlayChk { get; private set; }

    void FillTimers()
    {
        PageTimers.Children.Clear();
        TimerBoxes.Clear();
        var p = _host.TimerChoices;
        void Add(UIElement e) => PageTimers.Children.Add(e);
        Add(Styled(new TextBlock { Text = "Timers" }, "PageTitle"));
        Add(Styled(new TextBlock { Text = "The game's events and the world bosses' spawns: an overlay over the game with the ones you pick, only when one is a few minutes away, and a soft chime before. These save as you change them." }, "PageLead"));

        // the overlay
        Add(Styled(new TextBlock { Text = "OVERLAY" }, "GroupLabel"));
        var overlay = new StackPanel();
        TimersOverlayChk = Switch(p.OverlayOn, on => _host.SetTimersOverlay(on));
        overlay.Children.Add(Row("Show the timers overlay", $"A small window over the game, apart from the panel and the parser. It appears when a timer you keep on it is {p.ShowMinutes} min away and hides again after.", TimersOverlayChk));
        overlay.Children.Add(Styled(new Border(), "Divider"));
        overlay.Children.Add(Row("Clicks go through it", "On: clicks pass to the game. Off: drag it where you want it. It never takes focus either way.", Switch(p.ClickThrough, on => _host.SetTimersClickThrough(on))));
        overlay.Children.Add(Styled(new Border(), "Divider"));
        overlay.Children.Add(Row("Show a timer from", "How long before a timer starts or spawns it shows, and how long after.",
            Choice(TimerPrefs.MinuteChoices.Where(m => m <= 15), m => $"{m} min before", p.ShowMinutes, m => { p.ShowMinutes = m; p.Save(); })));
        overlay.Children.Add(Styled(new Border(), "Divider"));
        overlay.Children.Add(Row("At most", "Timers on the overlay at once.", Choice(TimerPrefs.CountChoices, c => $"{c} timers", p.Count, c => { p.Count = c; p.Save(); })));
        Add(Styled(new Border { Child = overlay }, "Group"));

        // the chime
        Add(Styled(new TextBlock { Text = "CHIME" }, "GroupLabel"));
        var chime = new StackPanel();
        chime.Children.Add(Row("Chime before a timer", "A soft chime, only for the timers with Chime ticked below.", Switch(p.ChimeOn, on => { p.ChimeOn = on; p.Save(); })));
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

        // timer by timer
        var items = _host.TimerItemsNow();
        foreach (var group in items.GroupBy(i => i.Group).OrderBy(g => g.Key == "Events" ? 0 : g.Key.Contains("Reshanta") ? 2 : 1))
        {
            Add(Styled(new TextBlock { Text = (group.Key == "Events" ? "EVENTS" : group.Key.Contains("Reshanta") ? group.Key.ToUpperInvariant() + " · ABYSS" : group.Key.ToUpperInvariant() + " · WORLD BOSSES") }, "GroupLabel"));
            var list = new StackPanel();
            bool first = true;
            foreach (var i in group.OrderBy(i => i.Name, StringComparer.OrdinalIgnoreCase))
            {
                if (TimerBoxes.ContainsKey(i.Key)) continue;
                if (!first) list.Children.Add(Styled(new Border(), "Divider"));
                first = false;
                var key = i.Key;
                var ov = new CheckBox { Content = "Overlay", IsChecked = p.Shows(key), Margin = new Thickness(0, 0, 16, 0), VerticalAlignment = VerticalAlignment.Center };
                ov.Click += (_, _) => { p.SetShows(key, ov.IsChecked == true); p.Save(); };
                var ch = new CheckBox { Content = "Chime", IsChecked = p.Chimes(key), VerticalAlignment = VerticalAlignment.Center };
                ch.Click += (_, _) => { p.SetChimes(key, ch.IsChecked == true); p.Save(); };
                System.Windows.Automation.AutomationProperties.SetName(ov, $"{i.Name} on the overlay");
                System.Windows.Automation.AutomationProperties.SetName(ch, $"Chime before {i.Name}");
                var boxes = new StackPanel { Orientation = Orientation.Horizontal };
                boxes.Children.Add(ov); boxes.Children.Add(ch);
                TimerBoxes[key] = (ov, ch);
                list.Children.Add(Row(i.Name, i.Sub, boxes));
            }
            Add(Styled(new Border { Child = list }, "Group"));
        }
        if (!items.Any(i => i.Key.StartsWith("boss:", StringComparison.Ordinal)))
            Add(Styled(new TextBlock { Text = "World bosses show here once the game's world map has been opened (Exploration › Field Boss) with packet reading on.", Margin = new Thickness(4, 10, 0, 0), TextWrapping = TextWrapping.Wrap }, "RowHint"));
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
        var box = new ComboBox { Width = 150, ItemsSource = list.Select(text).ToList(), SelectedIndex = Math.Max(0, list.IndexOf(now)) };
        box.SelectionChanged += (_, _) => { if (box.SelectedIndex >= 0) set(list[box.SelectedIndex]); };
        return box;
    }
}
