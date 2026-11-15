using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using KinahLedger.Parser;

namespace KinahLedger.Ui;

/// <summary>
/// Options › In game › Parser (Steven, 2026-11-14: minimal by default; the extras are switches in Settings): the Parser
/// page and its overlay show the boss, the clock and each member's DPS; these add the rest. On the page's preview-then-Save
/// model: read from parser.json when Options opens, written on Save (ParserPrefs.Saved tells the window), dropped on Cancel.
/// </summary>
public partial class OptionsWindow
{
    TextBlock? _parserLabel;
    /// <summary>The Parser's look row (the four looks that draw a fight, each in a boss fight), and its tiles' host.</summary>
    FrameworkElement? _parserLookRow;
    UniformGrid? _parserLookHost;
    TextBlock? _parserLookHint;
    Slider? _parserWidth;
    TextBlock? _parserWidthLbl;
    CheckBox _parserDetailsChk = null!, _parserKillsChk = null!, _parserDeathsChk = null!, _parserFiltersChk = null!, _parserHoversChk = null!, _parserSeparateChk = null!, _parserActiveChk = null!, _parserFarmChk = null!, _parserExpandedChk = null!;

    void LoadParser()
    {
        if (_parserLabel == null) BuildParser();
        var p = ParserPrefs.Load();
        _parserDetailsChk.IsChecked = p.Details;
        _parserKillsChk.IsChecked = p.KillsNear;
        _parserDeathsChk.IsChecked = p.Deaths;
        _parserFiltersChk.IsChecked = p.Filters;
        _parserHoversChk.IsChecked = p.Hovers;
        _parserSeparateChk.IsChecked = p.Separate;
        _parserActiveChk.IsChecked = p.DpsActive;
        _parserFarmChk.IsChecked = p.FarmDps;
        _parserExpandedChk.IsChecked = p.Expanded;
        if (_parserWidth != null) _parserWidth.Value = HudStackLook.FightWidth = p.Width;
        DrawParserLooks();
    }

    /// <summary>Options closed without saving: the meter back to its saved width.</summary>
    void EndParserWidthPreview()
    {
        if (_parserWidth == null) return;
        var w = ParserPrefs.Load().Width;
        if (w != HudStackLook.FightWidth) _host.PreviewFightWidth(w);
    }

    void SaveParser()
    {
        if (_parserLabel == null) return;
        var p = ParserPrefs.Load();
        p.Details = _parserDetailsChk.IsChecked == true;
        p.KillsNear = _parserKillsChk.IsChecked == true;
        p.Deaths = _parserDeathsChk.IsChecked == true;
        p.HonestSettled = true;
        p.Filters = _parserFiltersChk.IsChecked == true;
        p.Hovers = _parserHoversChk.IsChecked == true;
        p.Separate = _parserSeparateChk.IsChecked == true;
        p.DpsActive = _parserActiveChk.IsChecked == true;
        p.FarmDps = _parserFarmChk.IsChecked == true;
        p.Expanded = _parserExpandedChk.IsChecked == true;
        if (_parserWidth != null) p.Width = Math.Round(_parserWidth.Value);
        p.Save();
        ParserPrefs.RaiseSaved();
    }

    void BuildParser()
    {
        _parserLabel = Styled(new TextBlock { Text = "PARSER" }, "GroupLabel");
        var stack = new StackPanel();
        CheckBox Row(string title, string hint, string name)
        {
            var g = Styled(new Grid(), "Row");
            g.ColumnDefinitions.Add(new ColumnDefinition());
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var sp = new StackPanel();
            sp.Children.Add(Styled(new TextBlock { Text = title }, "RowTitle"));
            sp.Children.Add(Styled(new TextBlock { Text = hint }, "RowHint"));
            g.Children.Add(sp);
            var cb = Styled(new CheckBox(), "Switch");
            AutomationProperties.SetName(cb, title);
            Grid.SetColumn(cb, 1);
            g.Children.Add(cb);
            if (stack.Children.Count > 0) stack.Children.Add(Styled(new Border(), "Divider"));
            stack.Children.Add(g);
            return cb;
        }
        _parserExpandedChk = Row("Expanded meter", "The overlay's meter shows everyone with their damage. Off, the top four, and you always. A click on the meter switches it too.", "Parser expanded meter");
        _parserDetailsChk = Row("Group details", "Each member's class, Combat Power, item level and damage beside their DPS, on the Parser page and its overlay.", "Parser group details");
        _parserActiveChk = Row("DPS over active time", "Off, each member's DPS is their damage over the whole fight's length (a run's: its boss pulls). On, over the time they were dealing damage: their first hit to their last, pauses over 20 seconds left out. The Parser page's group card switches it too.", "Parser DPS over active time");
        _parserDeathsChk = Row("Deaths", "The group's deaths on boss pulls and runs: how many in the figures line, each member's in the record's group. Every fight always shows your deaths.", "Parser deaths");
        _parserKillsChk = Row("Kills near you", "Enemies that died near you during boss pulls and runs. Every fight always shows your kills.", "Parser kills near you");
        _parserFiltersChk = Row("List filters", "Pick a kind and a boss or an instance on the Parser's list of past records.", "Parser list filters");
        _parserHoversChk = Row("Explanations on hover", "What a figure, a row or a switch means, when the pointer rests on it.", "Parser explanations on hover");
        _parserFarmChk = Row("Open-world DPS on the Kinah line", "While you fight in the open world or the Abyss, your DPS shows small on the Kinah overlay's line. Those fights are counted while they last and never kept.", "Parser open-world DPS");
        _parserSeparateChk = Row("Separate parser overlay", "The Parser in its own overlay you can drag anywhere. Off, it opens above the Kinah overlay when a fight starts and folds away after.", "Separate parser overlay");
        _parserSeparateChk.Click += (_, _) => DrawParserLooks();
        // the overlay's look, first in the group (Steven, 2026-11-14: "idk how to change the style"): the four looks that draw
        // a fight, each drawn in a boss fight; a click picks it, the same choice as The panel › Panel look
        stack.Children.Insert(0, Styled(new Border(), "Divider"));
        stack.Children.Insert(0, BuildParserWidthRow());
        stack.Children.Insert(0, Styled(new Border(), "Divider"));
        stack.Children.Insert(0, BuildParserLookRow());
        var group = Styled(new Border { Child = stack }, "Group");
        // before Rare drops, which stays the page's last group
        int at = _rareLabel != null ? PageInGame.Children.IndexOf(_rareLabel) : -1;
        if (at < 0) { PageInGame.Children.Add(_parserLabel); PageInGame.Children.Add(group); }
        else { PageInGame.Children.Insert(at, group); PageInGame.Children.Insert(at, _parserLabel); }
    }

    FrameworkElement BuildParserLookRow()
    {
        var box = new StackPanel { Margin = new Thickness(18, 13, 18, 15) };
        var top = new Grid();
        top.ColumnDefinitions.Add(new ColumnDefinition());
        top.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        top.Children.Add(Styled(new TextBlock { Text = "Overlay look" }, "RowTitle"));
        // the older looks (Badge, Buddy, Pop) and the real-size preview stay with The panel's picker
        var all = new TextBlock { Text = "All looks  ›", FontSize = 12, FontWeight = FontWeights.SemiBold, Cursor = Cursors.Hand, Background = Brushes.Transparent, VerticalAlignment = VerticalAlignment.Center };
        all.SetResourceReference(TextBlock.ForegroundProperty, "Link");
        all.MouseEnter += (_, _) => all.TextDecorations = TextDecorations.Underline;
        all.MouseLeave += (_, _) => all.TextDecorations = null;
        all.MouseLeftButtonUp += (_, _) => HudLookHost.BringIntoView();
        AutomationProperties.SetName(all, "All panel looks");
        Grid.SetColumn(all, 1); top.Children.Add(all);
        box.Children.Add(top);
        _parserLookHint = Styled(new TextBlock(), "RowHint");
        box.Children.Add(_parserLookHint);
        _parserLookHost = new UniformGrid { Columns = 4, Margin = new Thickness(0, 10, 0, 0) };
        box.Children.Add(_parserLookHost);
        _parserLookRow = box;
        return box;
    }

    /// <summary>
    /// The meter's width (Steven, 2026-11-14: "maybe an option to make it wider?"), apart from the overlay's size: the rows
    /// and names stretch, the figures stay at the right. Shown on the overlay as it moves; Save keeps it, Cancel puts it back.
    /// </summary>
    FrameworkElement BuildParserWidthRow()
    {
        var g = Styled(new Grid(), "Row");
        g.ColumnDefinitions.Add(new ColumnDefinition());
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var sp = new StackPanel();
        sp.Children.Add(Styled(new TextBlock { Text = "Width" }, "RowTitle"));
        sp.Children.Add(Styled(new TextBlock { Text = "How wide the meter is over the game. You can also drag its left or right edge while the overlay can be moved." }, "RowHint"));
        g.Children.Add(sp);
        _parserWidth = new Slider { Width = 140, Minimum = HudStackLook.FightWidthMin, Maximum = HudStackLook.FightWidthMax, TickFrequency = 10, IsSnapToTickEnabled = true, VerticalAlignment = VerticalAlignment.Center, Value = HudStackLook.FightWidth };
        AutomationProperties.SetName(_parserWidth, "Parser overlay width");
        _parserWidthLbl = new TextBlock { FontSize = 12.5, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0), Width = 40 };
        _parserWidthLbl.SetResourceReference(TextBlock.ForegroundProperty, "Muted");
        void Label() => _parserWidthLbl!.Text = WidthWords(_parserWidth!.Value);
        Label();
        _parserWidth.ValueChanged += (_, e) =>
        {
            Label();
            if (Math.Round(e.NewValue) != HudStackLook.FightWidth) _host.PreviewFightWidth(e.NewValue);
        };
        var right = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Children = { _parserWidth, _parserWidthLbl } };
        Grid.SetColumn(right, 1); g.Children.Add(right);
        return g;
    }

    /// <summary>"100%" at the default width, "194%" at the widest.</summary>
    static string WidthWords(double w) => $"{Math.Round(w / HudStackLook.FightWidthDefault * 100):0}%";

    /// <summary>For the options test: the Width slider.</summary>
    internal Slider? ParserWidthForTest => _parserWidth;

    /// <summary>The look tiles, the one in use outlined; dimmed while the Parser has its own overlay (the looks are the Kinah overlay's).</summary>
    void DrawParserLooks()
    {
        if (_parserLookHost == null || _parserLookHint == null) return;
        _parserLookHost.Children.Clear();
        var current = HudLooks.Parse(_edit.HudLook);
        bool separate = _parserSeparateChk?.IsChecked == true;
        var inUse = HudLooks.All.First(l => l.Key == current);
        _parserLookHint.Text = separate
            ? "The looks are the Kinah overlay's. With a separate parser overlay, the Parser keeps its own."
            : HudLooks.FightLooks.Contains(current) ? "How a boss fight shows over the game, in a dungeon in place of the Kinah line. Picking one here picks the Kinah overlay's look too."
            : $"In use: {inUse.Name}, the Parser above it. Pick one drawn for fights, or see all looks.";
        var sample = _host.HudSample();
        var now = new DateTime(2026, 11, 13, 21, 30, 0, DateTimeKind.Utc);
        var (_, fight) = ParserDemo.Sample(now, live: true);
        var scene = new LinearGradientBrush(Color.FromRgb(0x19, 0x1B, 0x23), Color.FromRgb(0x0C, 0x0D, 0x11), 90);
        scene.Freeze();
        foreach (var key in HudLooks.FightLooks)
        {
            var l = HudLooks.All.First(x => x.Key == key);
            bool on = key == current && !separate;
            FrameworkElement face;
            try { face = HudWindow.PreviewFight(key, sample, fight!, now); }
            catch (Exception ex) { CrashLog.Write("parser look thumbnail", ex); continue; }
            var thumb = new Viewbox { Child = face, Stretch = Stretch.Uniform, StretchDirection = StretchDirection.DownOnly, Height = 112, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Bottom };
            var stage = new Border { Child = thumb, Background = scene, CornerRadius = new CornerRadius(6), Padding = new Thickness(5), Height = 122 };
            var name = new TextBlock { Text = l.Name, FontSize = 12, FontWeight = on ? FontWeights.Bold : FontWeights.SemiBold, Margin = new Thickness(1, 0, 0, 5) };
            name.SetResourceReference(TextBlock.ForegroundProperty, on ? "Ink" : "Muted");
            var card = new Border
            {
                CornerRadius = new CornerRadius(9), Margin = new Thickness(0, 0, 6, 0), Padding = new Thickness(6, 6, 6, 6), Cursor = Cursors.Hand, BorderThickness = new Thickness(on ? 2 : 1),
                Child = new StackPanel { Children = { name, stage } }, ToolTip = l.Hint, Focusable = true, Tag = key, Opacity = separate ? 0.45 : 1,
            };
            card.SetResourceReference(Border.BackgroundProperty, "Card2");
            card.SetResourceReference(Border.BorderBrushProperty, on ? "Link" : "LineStrong");
            card.FocusVisualStyle = (Style)FindResource("FocusRing");
            AutomationProperties.SetName(card, l.Name + " overlay look" + (on ? ", in use" : ""));
            card.MouseLeftButtonUp += (_, _) => PickHudLook(key);
            card.KeyDown += (_, e) => { if (e.Key is Key.Enter or Key.Space) { PickHudLook(key); e.Handled = true; } };
            _parserLookHost.Children.Add(card);
        }
    }

    /// <summary>In game, scrolled to the Parser's look row (the Parser page's "Look" link).</summary>
    public void ShowParserLook()
    {
        Go(Page.InGame);
        Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded, () =>
        {
            if (_parserLabel == null) return;
            // the group's label at the top of the view, the look row under it
            var at = _parserLabel.TransformToAncestor(Scroller).Transform(new Point(0, 0));
            Scroller.ScrollToVerticalOffset(Math.Max(0, Scroller.VerticalOffset + at.Y - 8));
        });
    }

    /// <summary>For the options test: the Parser's look tiles (their keys) and which is outlined.</summary>
    internal (List<string> Keys, string? On) ParserLooksForTest => (_parserLookHost?.Children.OfType<Border>().Select(b => (string)b.Tag).ToList() ?? new(),
        _parserLookHost?.Children.OfType<Border>().FirstOrDefault(b => b.BorderThickness.Left > 1)?.Tag as string);

    /// <summary>For the options test: the Parser group's switches.</summary>
    internal (CheckBox Details, CheckBox Kills, CheckBox Deaths, CheckBox Filters, CheckBox Hovers, CheckBox Separate, CheckBox Active, CheckBox Farm) ParserSwitchesForTest => (_parserDetailsChk, _parserKillsChk, _parserDeathsChk, _parserFiltersChk, _parserHoversChk, _parserSeparateChk, _parserActiveChk, _parserFarmChk);
}
