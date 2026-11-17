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
    FrameworkElement? _parserLookRow, _parserLookDivider, _parserAllLooks;
    /// <summary>The look picked for the Parser's own window (saved with the Parser's switches).</summary>
    string _parserLookPick = ParserPrefs.StandardLook;
    UniformGrid? _parserLookHost;
    TextBlock? _parserLookHint;
    /// <summary>The big preview under the look buttons (Steven, 2026-11-15: "can't really see the previews... they're so small"):
    /// the look under the mouse, else the one picked, drawn at the size it has over the game.</summary>
    Border? _parserLookStage;
    string? _parserLookShown;
    Slider? _parserWidth;
    TextBlock? _parserWidthLbl;
    CheckBox _parserDetailsChk = null!, _parserKillsChk = null!, _parserDeathsChk = null!, _parserFiltersChk = null!, _parserHoversChk = null!, _parserSeparateChk = null!, _parserActiveChk = null!, _parserExpandedChk = null!, _parserCritChk = null!, _parserHealBarsChk = null!, _parserHideNamesChk = null!;
    /// <summary>The share picture's shortcut as picked here ("Off" for none), its field and the line under it.</summary>
    string _parserShareKey = ParserPrefs.DefaultHotkey;
    TextBox? _parserShareKeyBox;
    TextBlock? _parserShareKeyHint;
    Button? _parserShareKeyOff;
    bool _parserShareCapturing;
    const string ShareKeyHintText = "Copies a picture of the meter, the fight under way or the last one, from anywhere, click-through on too. Off until you pick a key.";

    void LoadParser()
    {
        if (_parserLabel == null) BuildParser();
        var p = ParserPrefs.Load();
        _parserDetailsChk.IsChecked = p.Details;
        _parserKillsChk.IsChecked = p.KillsNear;
        _parserDeathsChk.IsChecked = p.Deaths;
        _parserFiltersChk.IsChecked = p.Filters;
        _parserHoversChk.IsChecked = p.Hovers;
        _parserSeparateChk.IsChecked = p.InKinah;
        _parserLookPick = ParserPrefs.LookOf(p.Look);
        _parserActiveChk.IsChecked = p.DpsActive;
        _parserExpandedChk.IsChecked = p.Expanded;
        _parserCritChk.IsChecked = p.CritNow != "none" || p.BackNow != "none";
        _parserHealBarsChk.IsChecked = p.HealBars;
        _parserHideNamesChk.IsChecked = p.ShareHideNames;
        _parserShareKey = Platform.Hotkey.Parse(p.ShareHotkey) is { } sk ? sk.ToString() : ParserPrefs.DefaultHotkey;
        RefreshShareKey();
        // the slider shows the saved width; opening Settings never moves the meter (it reset the meter to the base width,
        // dropping the expanded meter's extra: Steven, 2026-11-15, "should never revert unless you change it")
        _parserWidthLoading = true;
        if (_parserWidth != null) _parserWidth.Value = p.Width;
        _parserWidthLoading = false;
        _parserWidthLoaded = p.Width;
        DrawParserLooks();
        _parserSnap = ParserSnap();
    }
    bool _parserWidthLoading;

    /// <summary>
    /// The Parser group as loaded, to tell an unsaved change: its switches, look and width live in parser.json, not in the
    /// page's edited copy, so a look picked here alone never lit Save (Steven, 2026-11-16: "i cant save it unless i also change the buddy").
    /// </summary>
    string _parserSnap = "";
    string ParserSnap() => _parserLabel == null ? "" : string.Join("|",
        new[] { _parserDetailsChk, _parserKillsChk, _parserDeathsChk, _parserFiltersChk, _parserHoversChk, _parserSeparateChk, _parserActiveChk, _parserExpandedChk, _parserCritChk, _parserHealBarsChk, _parserHideNamesChk }
            .Select(c => c.IsChecked == true ? "1" : "0"))
        + "|" + _parserLookPick + "|" + Math.Round(_parserWidth?.Value ?? 0) + "|" + _parserShareKey;
    /// <summary>Something in the Parser group differs from parser.json: Save lights up for it.</summary>
    internal bool ParserDirty => _parserLabel != null && ParserSnap() != _parserSnap;
    double _parserWidthLoaded = double.NaN;

    /// <summary>Options closed without saving: the meter back to its saved width, only if the slider moved it.</summary>
    void EndParserWidthPreview()
    {
        if (_parserWidth == null || double.IsNaN(_parserWidthLoaded) || Math.Round(_parserWidth.Value) == Math.Round(_parserWidthLoaded)) return;
        _host.PreviewFightWidth(ParserPrefs.Load().Width);
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
        p.InKinah = _parserSeparateChk.IsChecked == true;
        p.Look = _parserLookPick;
        p.DpsActive = _parserActiveChk.IsChecked == true;
        p.Expanded = _parserExpandedChk.IsChecked == true;
        p.HealBars = _parserHealBarsChk.IsChecked == true;
        p.ShareHideNames = _parserHideNamesChk.IsChecked == true;
        p.ShareHotkey = _parserShareKey;
        // crit and back attacks: the switch picks the kinds the hits carry (crit: result 3, back: side 1); off is the release's default (none)
        CritSwitch(p, _parserCritChk.IsChecked == true);
        // the slider's width only when it was moved here: a width dragged on the overlay since stays
        if (_parserWidth != null && Math.Round(_parserWidth.Value) != Math.Round(_parserWidthLoaded)) { p.Width = Math.Round(_parserWidth.Value); p.WidthPicked = true; }
        p.Save();
        ParserPrefs.RaiseSaved();
    }

    /// <summary>
    /// The crit and back-attack switch into the settings: on picks the kinds (crit result 3, back side 1); off writes null
    /// while the release's default is none, so a release that turns them on reaches the player, and "none" once it isn't.
    /// </summary>
    internal static void CritSwitch(ParserPrefs p, bool on)
    {
        p.CritSource = on ? ParserPrefs.CritPick : ParserPrefs.CritDefault == "none" ? null : "none";
        p.BackSource = on ? ParserPrefs.BackPick : ParserPrefs.BackDefault == "none" ? null : "none";
    }

    void BuildParser()
    {
        _parserLabel = Styled(new TextBlock { Text = L.T("PARSER") }, "GroupLabel");
        var stack = new StackPanel();
        CheckBox Row(string title, string hint, string name)
        {
            var g = Styled(new Grid(), "Row");
            g.ColumnDefinitions.Add(new ColumnDefinition());
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var sp = new StackPanel();
            sp.Children.Add(Styled(new TextBlock { Text = L.T(title) }, "RowTitle"));
            sp.Children.Add(Styled(new TextBlock { Text = L.T(hint) }, "RowHint"));
            g.Children.Add(sp);
            var cb = Styled(new CheckBox(), "Switch");
            AutomationProperties.SetName(cb, title);
            Grid.SetColumn(cb, 1);
            g.Children.Add(cb);
            if (stack.Children.Count > 0) stack.Children.Add(Styled(new Border(), "Divider"));
            stack.Children.Add(g);
            cb.Click += (_, _) => Refresh();   // the footer says there is something to save
            return cb;
        }
        _parserExpandedChk = Row("Expanded meter", "The overlay's meter shows everyone with their damage. Off, the top four, and you always. A click on the meter switches it too.", "Parser expanded meter");
        _parserHealBarsChk = Row("Heal bars", "A green bar along each healer's row on the overlay's meter: their share of the group's healing. The HEAL chip after the name keeps their healing a second either way.", "Parser heal bars");
        _parserDetailsChk = Row("Group details", "Each member's class, Combat Power, item level and damage beside their DPS, on the Parser page and its overlay.", "Parser group details");
        _parserActiveChk = Row("DPS over active time", "Off, each member's DPS is their damage over the whole fight's length (a run's: its boss pulls). On, over the time they were dealing damage: their first hit to their last, pauses over 20 seconds left out. The Parser page's group card switches it too.", "Parser DPS over active time");
        _parserDeathsChk = Row("Deaths", "The group's deaths on boss pulls and runs: how many in the figures line, each member's in the record's group. Every fight always shows your deaths.", "Parser deaths");
        _parserKillsChk = Row("Kills near you", "Enemies that died near you during boss pulls and runs. Every fight always shows your kills.", "Parser kills near you");
        _parserFiltersChk = Row("List filters", "Pick a kind and a boss or an instance on the Parser's list of past records.", "Parser list filters");
        _parserCritChk = Row("Crit and back-attack rates", "Each member's crit rate, their crits' share of the damage and their biggest crit, and how often they hit from behind, with each skill's own rates, on the Parser page and in the overlay's pop-up on a member. New: close but still being checked against the game's Combat Analysis.", "Parser crit and back-attack rates");
        _parserHoversChk = Row("Explanations on hover", "What a figure, a row or a switch means, when the pointer rests on it.", "Parser explanations on hover");
        _parserSeparateChk = Row("Parser inside the Kinah overlay", "Off (the default), the Parser has its own overlay you can drag anywhere, and the Kinah overlay stays as it is. On, it opens above the Kinah overlay when a fight starts, takes its place in a dungeon, and takes the overlay's look.", "Parser inside the Kinah overlay");
        _parserSeparateChk.Click += (_, _) => DrawParserLooks();
        // the meter's share picture (Ui/MeterShare.cs): other members' names hidden on it, and a shortcut of its own
        _parserHideNamesChk = Row("Hide other names", "On the share picture, other members show as their class and \"Member 2\"; you keep your name. Their figures stay.", "Parser share picture hide other names");
        stack.Children.Add(Styled(new Border(), "Divider"));
        stack.Children.Add(BuildShareKeyRow());
        // the overlay's look, first in the group (Steven, 2026-11-14: "idk how to change the style"): the four looks that draw
        // a fight, each drawn in a boss fight; a click picks it, the same choice as The panel › Panel look
        stack.Children.Insert(0, Styled(new Border(), "Divider"));
        stack.Children.Insert(0, BuildParserWidthRow());
        stack.Children.Insert(0, _parserLookDivider = Styled(new Border(), "Divider"));
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
        top.Children.Add(Styled(new TextBlock { Text = L.T("Overlay look") }, "RowTitle"));
        // the older looks (Badge, Buddy, Pop) and the real-size preview stay with The panel's picker
        var all = new TextBlock { Text = L.T("All looks  ›"), FontSize = 12, FontWeight = FontWeights.SemiBold, Cursor = Cursors.Hand, Background = Brushes.Transparent, VerticalAlignment = VerticalAlignment.Center };
        all.SetResourceReference(TextBlock.ForegroundProperty, "Link");
        all.MouseEnter += (_, _) => all.TextDecorations = TextDecorations.Underline;
        all.MouseLeave += (_, _) => all.TextDecorations = null;
        all.MouseLeftButtonUp += (_, _) => HudLookHost.BringIntoView();
        AutomationProperties.SetName(all, "All panel looks");
        Grid.SetColumn(all, 1); top.Children.Add(all);
        _parserAllLooks = all;
        box.Children.Add(top);
        _parserLookHint = Styled(new TextBlock(), "RowHint");
        box.Children.Add(_parserLookHint);
        _parserLookHost = new UniformGrid { Columns = 4, Margin = new Thickness(0, 10, 0, 0) };
        box.Children.Add(_parserLookHost);
        _parserLookStage = new Border { CornerRadius = new CornerRadius(8), Padding = new Thickness(16, 14, 16, 14), Margin = new Thickness(0, 8, 0, 0), MinHeight = 200 };
        AutomationProperties.SetName(_parserLookStage, "Overlay look preview");
        box.Children.Add(_parserLookStage);
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
        sp.Children.Add(Styled(new TextBlock { Text = L.T("Width") }, "RowTitle"));
        sp.Children.Add(Styled(new TextBlock { Text = L.T("How wide the meter is over the game. You can also drag its left or right edge while the overlay can be moved.") }, "RowHint"));
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
            if (!_parserWidthLoading) { _host.PreviewFightWidth(Math.Round(e.NewValue)); Refresh(); }
        };
        var right = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Children = { _parserWidth, _parserWidthLbl } };
        Grid.SetColumn(right, 1); g.Children.Add(right);
        return g;
    }

    /// <summary>
    /// Share picture shortcut: recorded as the In game shortcuts are (Change, then the keys; Esc cancels), Off by default, never
    /// the start/stop, That was wrong or overlay key. Saved with the Parser's switches.
    /// </summary>
    FrameworkElement BuildShareKeyRow()
    {
        var box = new StackPanel { Margin = new Thickness(18, 13, 18, 15) };
        box.Children.Add(Styled(new TextBlock { Text = L.T("Share picture shortcut") }, "RowTitle"));
        _parserShareKeyHint = Styled(new TextBlock { Text = L.T(ShareKeyHintText) }, "RowHint");
        box.Children.Add(_parserShareKeyHint);
        var row = new WrapPanel { Margin = new Thickness(0, 12, 0, 0) };
        _parserShareKeyBox = new TextBox { Width = 190, IsReadOnly = true, HorizontalContentAlignment = HorizontalAlignment.Center, FontFamily = new FontFamily("Consolas"), FontSize = 14, Cursor = Cursors.Arrow, Margin = new Thickness(0, 0, 8, 0) };
        AutomationProperties.SetName(_parserShareKeyBox, "Share picture shortcut");
        var change = new Button { Content = L.T("Change"), Margin = new Thickness(0, 0, 8, 0) };
        _parserShareKeyOff = new Button { Content = L.T("Turn off") };
        AutomationProperties.SetName(_parserShareKeyOff, "Turn off the share picture shortcut");
        change.Click += (_, _) => StartShareCapture();
        _parserShareKeyBox.PreviewMouseLeftButtonDown += (_, e) => { if (!_parserShareCapturing) { StartShareCapture(); e.Handled = true; } };
        _parserShareKeyBox.PreviewKeyDown += (_, e) =>
        {
            if (!_parserShareCapturing) return;
            e.Handled = true;
            var key = e.Key == Key.System ? e.SystemKey : e.Key;
            if (key == Key.Escape) { EndShareCapture(); Keyboard.ClearFocus(); return; }
            if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin) return;
            RecordShareKey(Keyboard.Modifiers, key);
        };
        _parserShareKeyBox.LostKeyboardFocus += (_, _) => EndShareCapture();
        _parserShareKeyOff.Click += (_, _) => { _parserShareKey = ParserPrefs.DefaultHotkey; RefreshShareKey(); Refresh(); };
        row.Children.Add(_parserShareKeyBox); row.Children.Add(change); row.Children.Add(_parserShareKeyOff);
        box.Children.Add(row);
        RefreshShareKey();
        return box;
    }

    void RefreshShareKey()
    {
        if (_parserShareKeyBox == null) return;
        bool on = Platform.Hotkey.Parse(_parserShareKey) != null;
        _parserShareKeyBox.Text = _parserShareCapturing ? L.T("Press the keys…") : on ? _parserShareKey : L.T("Off");
        if (_parserShareKeyOff != null) _parserShareKeyOff.IsEnabled = on;
    }

    void StartShareCapture()
    {
        if (_parserShareKeyBox == null || _parserShareKeyHint == null) return;
        if (_capturing) EndCapture();   // one field records at a time
        _parserShareCapturing = true;
        _host.SuspendHotkey(true);
        _parserShareKeyHint.Text = L.T("Hold Ctrl and Shift (or Alt and Shift) and press a letter, or Ctrl, Alt or Shift with an F key. Esc cancels.");
        _parserShareKeyHint.Foreground = (Brush)FindResource("Accent");
        _parserShareKeyBox.Focus();
        RefreshShareKey();
    }

    void EndShareCapture()
    {
        if (!_parserShareCapturing) return;
        _parserShareCapturing = false;
        _host.SuspendHotkey(false);
        if (_parserShareKeyHint != null) { _parserShareKeyHint.Text = L.T(ShareKeyHintText); _parserShareKeyHint.Foreground = (Brush)FindResource("Muted"); }
        RefreshShareKey();
    }

    /// <summary>A combination pressed while recording the share shortcut: refused with the reason under it, else taken. The reason, or null.</summary>
    string? RecordShareKey(ModifierKeys mods, Key key)
    {
        var text = new Platform.Hotkey(mods, key).ToString();
        var why = Platform.Hotkey.Refusal(mods, key)
            ?? (Platform.Hotkey.Same(text, _edit.Hotkey) ? L.T("That's the start/stop shortcut. Pick another key.")
            : Platform.Hotkey.Same(text, _edit.MarkWrongKey) ? L.T("That's the That was wrong shortcut. Pick another key.")
            : Platform.Hotkey.Same(text, ParserPrefs.Load().Hotkey) ? L.T("That's the Parser overlay's shortcut. Pick another key.") : null);
        if (why != null)
        {
            _parserShareKeyHint!.Text = why;
            _parserShareKeyHint.Foreground = (Brush)FindResource("Warn");
            return why;
        }
        _parserShareCapturing = false;
        _host.SuspendHotkey(false);
        _parserShareKey = text;
        RefreshShareKey();
        Refresh();
        _parserShareKeyHint!.Text = L.F("{0} it is. Save to start using it.", text);
        _parserShareKeyHint.Foreground = (Brush)FindResource("Good");
        Keyboard.ClearFocus();
        return null;
    }

    /// <summary>For the options test: a combination recorded in the share shortcut's field; the reason it was refused, or null. And the key picked.</summary>
    internal string? RecordShareKeyForTest(ModifierKeys mods, Key key)
    {
        StartShareCapture();
        var why = RecordShareKey(mods, key);
        if (why != null) EndShareCapture();
        return why;
    }
    internal string ShareKeyForTest => _parserShareKey;
    internal void ReloadParserForTest() => LoadParser();
    internal CheckBox ParserHideNamesForTest => _parserHideNamesChk;

    /// <summary>"100%" at the default width, "194%" at the widest.</summary>
    static string WidthWords(double w) => $"{Math.Round(w / HudStackLook.FightWidthDefault * 100):0}%";

    /// <summary>For the options test: the Width slider.</summary>
    internal Slider? ParserWidthForTest => _parserWidth;
    internal bool? ParserLookRowShownForTest => _parserLookRow?.Visibility == Visibility.Visible;
    /// <summary>For the options test: a click on a look button (the Parser's own window's look, or inside the Kinah overlay the Kinah overlay's).</summary>
    internal void PickParserLookForTest(string key)
    {
        var card = _parserLookHost?.Children.OfType<Border>().FirstOrDefault(b => (string)b.Tag == key);
        card?.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left) { RoutedEvent = UIElement.MouseLeftButtonUpEvent });
    }

    /// <summary>
    /// The look buttons, the one in use outlined, the big stage drawing the one hovered or picked. With the Parser in its
    /// own window (the default) they are that window's looks, apart from the Kinah overlay's: Standard (its own panel) and
    /// the four drawn for fights, fight alone. Inside the Kinah overlay they are the Kinah overlay's four fight looks.
    /// </summary>
    void DrawParserLooks()
    {
        if (_parserLookHost == null || _parserLookHint == null) return;
        _parserLookHost.Children.Clear();
        if (_parserLookStage != null) _parserLookStage.Tag = null;
        bool separate = _parserSeparateChk?.IsChecked != true;
        if (_parserLookRow != null) _parserLookRow.Visibility = Visibility.Visible;
        if (_parserLookDivider != null) _parserLookDivider.Visibility = Visibility.Visible;
        if (_parserAllLooks != null) _parserAllLooks.Visibility = separate ? Visibility.Collapsed : Visibility.Visible;
        var current = separate ? _parserLookPick : HudLooks.Parse(_edit.HudLook);
        var themeLooks = HudThemes.FightLooks(_hudTheme ?? HudThemes.Of(_edit.HudLook));   // the theme picked's (Ui/OptionsWindow.Themes.cs)
        var keys = separate ? new[] { ParserPrefs.StandardLook }.Concat(themeLooks).ToArray() : themeLooks.ToArray();
        if (_parserLookHost is UniformGrid ug) ug.Columns = keys.Length;
        string inUseName = separate ? MainWindow.ParserLookName(current) : HudLooks.All.First(l => l.Key == current).Name;
        _parserLookHint.Text = separate
            ? L.T("How the Parser's own window looks over the game. The Kinah overlay keeps its own look.")
            : HudLooks.FightLooks.Contains(current) ? L.T("How a boss fight shows over the game, in a dungeon in place of the Kinah line. Picking one here picks the Kinah overlay's look too.")
            : L.F("In use: {0}, the Parser above it. Pick one drawn for fights, or see all looks.", L.T(inUseName));
        var sample = _host.HudSample();
        var now = new DateTime(2026, 11, 13, 21, 30, 0, DateTimeKind.Utc);
        var (_, fight) = ParserDemo.Sample(now, live: true);
        var scene = HudKit.GameScene();   // over the game (Steven, 2026-11-16)
        _parserLookShown = keys.Contains(current) ? current : keys[0];
        foreach (var key in keys)
        {
            string lookName = separate ? MainWindow.ParserLookName(key) : HudLooks.All.First(x => x.Key == key).Name;
            string hint = L.T(key == ParserPrefs.StandardLook ? "The Parser's own panel: the boss, the clock, the group on class-coloured bars." : HudLooks.All.First(x => x.Key == key).Hint);
            bool on = key == current;
            // a button per look, the big stage below draws it: four thumbnails side by side came out ~110 px tall, unreadable
            var name = new TextBlock { Text = L.T(lookName), FontSize = 13, FontWeight = on ? FontWeights.Bold : FontWeights.SemiBold, HorizontalAlignment = HorizontalAlignment.Center };
            name.SetResourceReference(TextBlock.ForegroundProperty, on ? "Ink" : "Muted");
            var card = new Border
            {
                CornerRadius = new CornerRadius(8), Margin = new Thickness(0, 0, 6, 0), Padding = new Thickness(8, 7, 8, 7), Cursor = Cursors.Hand, BorderThickness = new Thickness(on ? 2 : 1),
                Child = name, ToolTip = hint, Focusable = true, Tag = key,
            };
            card.SetResourceReference(Border.BackgroundProperty, "Card2");
            card.SetResourceReference(Border.BorderBrushProperty, on ? "Link" : "LineStrong");
            card.FocusVisualStyle = (Style)FindResource("FocusRing");
            AutomationProperties.SetName(card, lookName + " overlay look" + (on ? ", in use" : ""));
            void Pick() { if (separate) { _parserLookPick = key; DrawParserLooks(); Refresh(); } else PickHudLook(key); }
            card.MouseLeftButtonUp += (_, _) => Pick();
            card.KeyDown += (_, e) => { if (e.Key is Key.Enter or Key.Space) { Pick(); e.Handled = true; } };
            card.MouseEnter += (_, _) => ShowParserLook(key, sample, fight!, now, scene);
            card.GotKeyboardFocus += (_, _) => ShowParserLook(key, sample, fight!, now, scene);
            card.MouseLeave += (_, _) => ShowParserLook(_parserLookShown, sample, fight!, now, scene);
            _parserLookHost.Children.Add(card);
        }
        ShowParserLook(_parserLookShown, sample, fight!, now, scene);
    }

    /// <summary>The stage: one look in a boss fight at its real size over a dark game scene, scaled down only past the page's width.</summary>
    void ShowParserLook(string? key, IReadOnlyDictionary<string, string> sample, Encounter fight, DateTime now, Brush scene)
    {
        if (_parserLookStage == null || key == null) return;
        if (_parserLookStage.Tag as string == key) return;
        FrameworkElement face;
        bool separate = _parserSeparateChk?.IsChecked != true;
        try { face = separate ? ParserOverlay.PreviewFace(key, fight, now, fight.ModeKind, false) : HudWindow.PreviewFight(key, sample, fight, now); }
        catch (Exception ex) { CrashLog.Write("parser look preview", ex); return; }
        face.HorizontalAlignment = HorizontalAlignment.Center;
        face.VerticalAlignment = VerticalAlignment.Center;
        _parserLookStage.Background = scene;
        _parserLookStage.Tag = key;
        _parserLookStage.Child = new Viewbox { Child = face, Stretch = Stretch.Uniform, StretchDirection = StretchDirection.DownOnly, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
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
    /// <summary>For the options test: the look the big preview draws.</summary>
    internal string? ParserLookStageForTest => _parserLookStage?.Tag as string;

    /// <summary>For the options test: the mouse entering a look button (a key), or leaving it (null).</summary>
    internal void HoverParserLookForTest(string? key)
    {
        var card = _parserLookHost?.Children.OfType<Border>().FirstOrDefault(b => key == null ? b.IsMouseOver == false : (string)b.Tag == key);
        card?.RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice, 0) { RoutedEvent = key == null ? UIElement.MouseLeaveEvent : UIElement.MouseEnterEvent });
    }

    internal (List<string> Keys, string? On) ParserLooksForTest => (_parserLookHost?.Children.OfType<Border>().Select(b => (string)b.Tag).ToList() ?? new(),
        _parserLookHost?.Children.OfType<Border>().FirstOrDefault(b => b.BorderThickness.Left > 1)?.Tag as string);

    /// <summary>For the options test: the Parser group's switches.</summary>
    internal (CheckBox Details, CheckBox Kills, CheckBox Deaths, CheckBox Filters, CheckBox Hovers, CheckBox Separate, CheckBox Active) ParserSwitchesForTest => (_parserDetailsChk, _parserKillsChk, _parserDeathsChk, _parserFiltersChk, _parserHoversChk, _parserSeparateChk, _parserActiveChk);

    /// <summary>For the options test: the crit and back-attack switch.</summary>
    internal CheckBox ParserCritForTest => _parserCritChk;
    internal CheckBox ParserHealBarsForTest => _parserHealBarsChk;

    /// <summary>For the options test: every switch's title in the Parser group.</summary>
    internal List<string> ParserSwitchTitlesForTest => new[] { _parserDetailsChk, _parserKillsChk, _parserDeathsChk, _parserFiltersChk, _parserHoversChk, _parserSeparateChk, _parserActiveChk, _parserExpandedChk, _parserCritChk, _parserHealBarsChk, _parserHideNamesChk }
        .Select(c => AutomationProperties.GetName(c)).ToList();
}
