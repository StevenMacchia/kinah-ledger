using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using KinahLedger.Parser;

namespace KinahLedger.Ui;

/// <summary>
/// Options › DPS meter (Steven, 2026-11-17: its own page on the left bar; the settings revamp put it second, after Buddy).
/// Like Buddy's page: the look in use drawn big over the game first, then its theme (apart from Buddy's: "you shouldnt
/// have to choose the corresponding buddy"), its look, its width and when it shows, its spot and click-through; the
/// meter's extras (minimal by default, Steven 2026-11-14) folded under More. On the page's preview-then-Save model: read
/// from parser.json (and the window's meter switches) when Options opens, written on Save (ParserPrefs.Saved tells the
/// window), dropped on Cancel.
/// </summary>
public partial class OptionsWindow
{
    /// <summary>The page's title: built once (the page's sentinel).</summary>
    TextBlock? _parserLabel;
    /// <summary>The look row, and the "All looks" link (shown with the meter inside the Kinah overlay).</summary>
    FrameworkElement? _parserLookRow, _parserAllLooks;
    /// <summary>The look picked for the meter's own window (saved with the meter's switches).</summary>
    string _parserLookPick = ParserPrefs.StandardLook;
    /// <summary>
    /// The theme whose looks the meter's own window offers, apart from the Kinah overlay's theme (Steven, 2026-11-17: "you
    /// shouldnt have to choose the corresponding buddy if u dont want to in order to choose the DPS overlay"). Null: the
    /// theme of the look in use.
    /// </summary>
    string? _parserTheme;
    WrapPanel? _parserThemeHost;
    FrameworkElement? _parserThemeRow;

    /// <summary>The meter window's looks in a theme: Classic's are Standard and the four drawn for fights; another theme's, all of its own.</summary>
    internal static IReadOnlyList<string> ParserLooksOf(string theme) =>
        theme == HudThemes.Classic ? PanelLooks.Concat(HudLooks.FightLooks).ToList() : HudThemes.Looks(theme).Select(l => l.Key).ToList();
    /// <summary>The Parser panel's own looks: Forge (the "standard" key), Podium, Reactor.</summary>
    internal static readonly string[] PanelLooks = { ParserPrefs.StandardLook, ParserPrefs.PodiumLook, ParserPrefs.ReactorLook };

    /// <summary>The theme a meter look belongs to (Standard: Classic).</summary>
    static string ParserThemeOf(string look) => PanelLooks.Contains(look) ? HudThemes.Classic : HudThemes.Of(look);
    UniformGrid? _parserLookHost;
    /// <summary>The page's lead: what the looks are for (the meter's own window, or inside the Kinah overlay).</summary>
    TextBlock? _parserLookHint;
    /// <summary>The big preview at the top of the page (Steven, 2026-11-15: "can't really see the previews... they're so small"):
    /// the look under the mouse, else the one picked, drawn at the size it has over the game.</summary>
    Border? _parserLookStage;
    string? _parserLookShown;
    Slider? _parserWidth;
    TextBlock? _parserWidthLbl;
    CheckBox _parserDetailsChk = null!, _parserKillsChk = null!, _parserDeathsChk = null!, _parserFiltersChk = null!, _parserHoversChk = null!, _parserSeparateChk = null!, _parserActiveChk = null!, _parserExpandedChk = null!, _parserCritChk = null!, _parserHealBarsChk = null!, _parserGhostChk = null!, _parserGhostGroupChk = null!, _parserPvpChk = null!, _parserOdyleChk = null!, _parserHideNamesChk = null!, _parserAddsChk = null!;
    /// <summary>The share picture's shortcut as picked here ("Off" for none), its field and the line under it.</summary>
    string _parserShareKey = ParserPrefs.DefaultHotkey;
    TextBox? _parserShareKeyBox;
    TextBlock? _parserShareKeyHint;
    Button? _parserShareKeyOff;
    bool _parserShareCapturing;
    const string ShareKeyHintText = "Copies a picture of the meter, the fight under way or the last one, from anywhere, click-through on too. Off until you pick a key.";
    /// <summary>The meter over the game, its click-through and when it shows (parser.json's Overlay, ClickThrough, AlwaysOn): kept on Save.</summary>
    CheckBox _parserOverlayChk = null!, _parserThroughChk = null!;
    ComboBox _parserWhenBox = null!;
    /// <summary>The More fold's group, where later rows (the meter's share picture) go.</summary>
    StackPanel _parserMoreStack = null!;

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
        _parserAddsChk.IsChecked = p.Targets == "all";
        _parserExpandedChk.IsChecked = p.Expanded;
        _parserCritChk.IsChecked = p.CritNow != "none" || p.BackNow != "none";
        _parserHealBarsChk.IsChecked = p.HealBars;
        _parserGhostChk.IsChecked = p.Ghost; _parserGhostGroupChk.IsChecked = p.GhostGroup;
        _parserPvpChk.IsChecked = p.PvpPanel;
        _parserOdyleChk.IsChecked = p.OdyleEnergy;
        _parserHideNamesChk.IsChecked = p.ShareShowNames;
        _parserShareKey = Platform.Hotkey.Parse(p.ShareHotkey) is { } sk ? sk.ToString() : ParserPrefs.DefaultHotkey;
        RefreshShareKey();
        // the meter's on/off, click-through and when it shows: as the window holds them now (the Overlays menu's quick switch may have moved the first)
        _parserOverlayChk.IsChecked = _host.ParserOverlayOn;
        _parserThroughChk.IsChecked = _host.ParserOverlayClickThrough;
        _parserWhenBox.SelectedIndex = _host.ParserAlwaysOn ? 1 : 0;
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
    /// The meter's choices as loaded, to tell an unsaved change: its switches, look and width live in parser.json, not in
    /// the page's edited copy, so a look picked here alone never lit Save (Steven, 2026-11-16: "i cant save it unless i also change the buddy").
    /// </summary>
    string _parserSnap = "";
    string ParserSnap() => _parserLabel == null ? "" : string.Join("|",
        new[] { _parserDetailsChk, _parserKillsChk, _parserDeathsChk, _parserFiltersChk, _parserHoversChk, _parserSeparateChk, _parserActiveChk, _parserExpandedChk, _parserCritChk, _parserHealBarsChk, _parserGhostChk, _parserGhostGroupChk, _parserPvpChk, _parserOdyleChk, _parserHideNamesChk, _parserOverlayChk, _parserThroughChk, _parserAddsChk }
            .Select(c => c.IsChecked == true ? "1" : "0"))
        + "|" + _parserWhenBox.SelectedIndex + "|" + _parserLookPick + "|" + Math.Round(_parserWidth?.Value ?? 0) + "|" + _parserShareKey;
    /// <summary>Something on the DPS meter page differs from parser.json: Save lights up for it.</summary>
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
        // the window's own switches first (each saves parser.json as the window holds it), then this page's on top
        _host.ParserOverlayOn = _parserOverlayChk.IsChecked == true;
        _host.ParserOverlayClickThrough = _parserThroughChk.IsChecked == true;
        _host.ParserAlwaysOn = _parserWhenBox.SelectedIndex == 1;
        var p = ParserPrefs.Load();
        p.Overlay = _parserOverlayChk.IsChecked == true;
        p.ClickThrough = _parserThroughChk.IsChecked == true;
        p.AlwaysOn = _parserWhenBox.SelectedIndex == 1;
        p.Details = _parserDetailsChk.IsChecked == true;
        p.KillsNear = _parserKillsChk.IsChecked == true;
        p.Deaths = _parserDeathsChk.IsChecked == true;
        p.HonestSettled = true;
        p.Filters = _parserFiltersChk.IsChecked == true;
        p.Hovers = _parserHoversChk.IsChecked == true;
        p.InKinah = _parserSeparateChk.IsChecked == true;
        p.Look = _parserLookPick;
        p.DpsActive = _parserActiveChk.IsChecked == true;
        p.Targets = _parserAddsChk.IsChecked == true ? "all" : "boss";
        p.Expanded = _parserExpandedChk.IsChecked == true;
        p.HealBars = _parserHealBarsChk.IsChecked == true;
        p.Ghost = _parserGhostChk.IsChecked == true; p.GhostGroup = _parserGhostGroupChk.IsChecked == true;
        p.PvpPanel = _parserPvpChk.IsChecked == true;
        p.OdyleEnergy = _parserOdyleChk.IsChecked == true;
        p.ShareShowNames = _parserHideNamesChk.IsChecked == true;
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

    /// <summary>A row on this window's pages: a title and a hint on the left, <paramref name="right"/> on the right.</summary>
    Grid SideRow(string title, string hint, FrameworkElement right, string? tip = null)
    {
        var g = Styled(new Grid(), "Row");
        g.ColumnDefinitions.Add(new ColumnDefinition());
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var sp = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        sp.Children.Add(Styled(new TextBlock { Text = L.T(title) }, "RowTitle"));
        if (hint.Length > 0) sp.Children.Add(Styled(new TextBlock { Text = L.T(hint), ToolTip = tip == null ? null : L.T(tip) }, "RowHint"));
        g.Children.Add(sp);
        right.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(right, 1);
        g.Children.Add(right);
        return g;
    }

    /// <summary>A compact row: a fixed-width title, then the control (the overlay pages' Theme and Look rows).</summary>
    Grid LabelRow(string title, FrameworkElement control, string? tip = null)
    {
        var g = Styled(new Grid(), "Row");
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(92) });
        g.ColumnDefinitions.Add(new ColumnDefinition());
        g.Children.Add(Styled(new TextBlock { Text = L.T(title), VerticalAlignment = VerticalAlignment.Center, ToolTip = tip == null ? null : L.T(tip) }, "RowTitle"));
        Grid.SetColumn(control, 1);
        g.Children.Add(control);
        return g;
    }

    /// <summary>An overlay page's header: its title and lead on the left, its "Show over the game" switch on the right.</summary>
    Grid OverlayHeader(TextBlock title, TextBlock lead, CheckBox show, string showName)
    {
        var g = new Grid();
        g.ColumnDefinitions.Add(new ColumnDefinition());
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        g.Children.Add(new StackPanel { Children = { title, lead } });
        var said = new TextBlock { Text = L.T("Show over the game"), FontSize = 12.5, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0) };
        said.SetResourceReference(TextBlock.ForegroundProperty, "Muted");
        AutomationProperties.SetName(show, showName);
        var right = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Children = { said, show } };
        Grid.SetColumn(right, 1);
        g.Children.Add(right);
        return g;
    }

    void BuildParser()
    {
        // the header: the title, what the looks are for, and the meter's on/off
        _parserLabel = Styled(new TextBlock { Text = L.T("DPS meter") }, "PageTitle");
        _parserLookHint = Styled(new TextBlock(), "PageLead");
        _parserOverlayChk = Styled(new CheckBox(), "Switch");
        _parserOverlayChk.ToolTip = L.T("The meter over the game in boss fights and dungeons, or always (When it shows).");
        _parserOverlayChk.Click += (_, _) => Refresh();
        PageMeter.Children.Add(OverlayHeader(_parserLabel, _parserLookHint, _parserOverlayChk, "Show the DPS meter over the game"));
        PageMeter.Children.Add(MeterBlockHost(Page.Meter));   // "Needs Npcap · Get it" while something keeps the meter off (Ui/OptionsWindow.MeterBlock.cs)
        AddPopOutLink(PageMeter, meter: true);   // Open in its own window, under the switch (Ui/OptionsWindow.PopOuts.cs)

        // the stage: the look in use (or under the mouse) in a boss fight, big, over the game
        _parserLookStage = new Border { Margin = new Thickness(0, 12, 0, 0) };
        AutomationProperties.SetName(_parserLookStage, "Overlay look preview");
        PageMeter.Children.Add(_parserLookStage);

        var stack = new StackPanel();
        // its own theme chips: its looks come from here, whatever the Kinah overlay's theme
        _parserThemeHost = new WrapPanel { Margin = new Thickness(0, 0, 0, -6) };
        AutomationProperties.SetName(_parserThemeHost, "Parser overlay theme");
        _parserThemeRow = LabelRow("Theme", _parserThemeHost);
        stack.Children.Add(_parserThemeRow);
        stack.Children.Add(Styled(new Border(), "Divider"));
        // the look tiles; with the meter inside the Kinah overlay, "All looks" goes to Buddy's picker (the older looks)
        _parserLookHost = new UniformGrid { Rows = 1 };
        var lookCol = new StackPanel { Children = { _parserLookHost } };
        var all = new TextBlock { Text = L.T("All looks  ›"), FontSize = 12, FontWeight = FontWeights.SemiBold, Cursor = Cursors.Hand, Background = Brushes.Transparent, Margin = new Thickness(0, 6, 0, 0), HorizontalAlignment = HorizontalAlignment.Left };
        all.SetResourceReference(TextBlock.ForegroundProperty, "Link");
        all.MouseEnter += (_, _) => all.TextDecorations = TextDecorations.Underline;
        all.MouseLeave += (_, _) => all.TextDecorations = null;
        all.MouseLeftButtonUp += (_, _) => Go(Page.InGame);
        AutomationProperties.SetName(all, "All panel looks");
        _parserAllLooks = all;
        lookCol.Children.Add(all);
        _parserLookRow = LabelRow("Overlay look", lookCol);
        stack.Children.Add(_parserLookRow);
        stack.Children.Add(Styled(new Border(), "Divider"));
        stack.Children.Add(BuildParserWidthRow());
        stack.Children.Add(Styled(new Border(), "Divider"));
        // its spot and click-through, on one row
        var reset = new Button { Content = L.T("Reset position"), Margin = new Thickness(0, 0, 18, 0), ToolTip = L.T("Back to its first spot over the game. Acts at once.") };
        reset.Click += (_, _) => { _host.ResetParserOverlayPosition(); Footer(L.T("The meter is back at its first spot."), "Good"); };
        _parserThroughChk = Styled(new CheckBox(), "Switch");
        AutomationProperties.SetName(_parserThroughChk, "DPS meter click-through");
        _parserThroughChk.Click += (_, _) => Refresh();
        var throughSaid = new TextBlock { Text = L.T("Click-through"), FontSize = 13, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0), ToolTip = L.T("On: clicks pass through it to the game. Off: drag it anywhere, and drag its edges to resize it.") };
        throughSaid.SetResourceReference(TextBlock.ForegroundProperty, "Ink");
        var place = new StackPanel { Orientation = Orientation.Horizontal, Children = { reset, throughSaid, _parserThroughChk } };
        stack.Children.Add(SideRow("Meter position", "Drag it anywhere while click-through is off.", place));
        PageMeter.Children.Add(Styled(new Border { Child = stack, Margin = new Thickness(0, 10, 0, 0) }, "Group"));

        // More: the extras, each off or on as the honest pass left them
        var more = new StackPanel { Tag = "More" };
        _parserMoreStack = new StackPanel();
        CheckBox Row(string title, string hint, string name)
        {
            var cb = Styled(new CheckBox(), "Switch");
            AutomationProperties.SetName(cb, title);
            if (_parserMoreStack.Children.Count > 0) _parserMoreStack.Children.Add(Styled(new Border(), "Divider"));
            _parserMoreStack.Children.Add(SideRow(title, hint, cb));
            cb.Click += (_, _) => Refresh();   // the footer says there is something to save
            return cb;
        }
        _parserExpandedChk = Row("Expanded meter", "The overlay's meter shows everyone with their damage. Off, the top four, and you always. A click on the meter switches it too.", "Parser expanded meter");
        _parserHealBarsChk = Row("Heal bars", "A green bar along each healer's row on the overlay's meter: their share of the group's healing. The HEAL chip after the name keeps their healing a second either way.", "Parser heal bars");
        // the PB ghost (Parser/PbGhost.cs): its switch, and the group's pace beside it as a small second switch in the same row
        {
            _parserGhostChk = Styled(new CheckBox(), "Switch");
            AutomationProperties.SetName(_parserGhostChk, "PB ghost");
            _parserGhostGroupChk = Styled(new CheckBox(), "Switch");
            AutomationProperties.SetName(_parserGhostGroupChk, "Group pace");
            var groupSaid = new TextBlock { Text = L.T("Group pace"), FontSize = 12, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0), ToolTip = L.T("The group's DPS against the group's in your best kill, beside yours.") };
            groupSaid.SetResourceReference(TextBlock.ForegroundProperty, "Muted");
            var ghostRight = new StackPanel { Orientation = Orientation.Horizontal, Children = { groupSaid, _parserGhostGroupChk, new Border { Width = 18 }, _parserGhostChk } };
            void GroupOn() => _parserGhostGroupChk.IsEnabled = groupSaid.IsEnabled = _parserGhostChk.IsChecked == true;
            _parserGhostChk.Click += (_, _) => { GroupOn(); Refresh(); };
            _parserGhostGroupChk.Click += (_, _) => Refresh();
            _parserGhostChk.Checked += (_, _) => GroupOn(); _parserGhostChk.Unchecked += (_, _) => GroupOn();
            _parserMoreStack.Children.Add(Styled(new Border(), "Divider"));
            _parserMoreStack.Children.Add(SideRow("PB ghost", "On a boss you've killed before, your row races your best kill of that fight: a line where your best's pace would put you, and \"+6.2% vs best\" or \"−3.1%\", live. At the kill: a new best, or how far off it.", ghostRight));
        }
        _parserPvpChk = Row("PvP kills in PvP", "In the Abyss and the arenas the meter shows your last 5 player kills instead of the DPS: your damage on each, Kill or Assist, and your skills on them when the pointer rests on a row. A boss fight there keeps the DPS meter.", "Parser PvP kills");
        _parserOdyleChk = Row("Odyle Energy", "Your character's Odyle Energy, small, after the meter's total: what's left for a run's final reward. It updates whenever it changes, and after each run at least.", "Parser Odyle Energy");
        _parserDetailsChk = Row("Group details", "Each member's class, Combat Power, item level and damage beside their DPS, on the Parser page and its overlay.", "Parser group details");
        _parserActiveChk = Row("DPS over active time", "Off, each member's DPS is their damage over the whole fight's length, so the rows add up to the group's DPS and your bests compare. On, over the time they were dealing damage: their first hit to their last, pauses over 20 seconds left out.", "Parser DPS over active time");
        _parserAddsChk = Row("Damage to adds", "Off, each member's DPS, share and bar count their damage to the boss alone, as its HP bar does. On, everything they hit, adds included. Only fights with adds differ.", "Parser damage to adds");
        _parserDeathsChk = Row("Deaths", "The group's deaths on boss pulls: how many in the figures line, each member's in the pull's group.", "Parser deaths");
        _parserKillsChk = Row("Kills near you", "Enemies that died near you during boss pulls.", "Parser kills near you");
        _parserFiltersChk = Row("List filters", "Pick a kind and a boss or an instance on the Parser's list of past records.", "Parser list filters");
        _parserCritChk = Row("Crit and back-attack rates", "Each member's crit rate, their crits' share of the damage and their biggest crit, and how often they hit from behind, with each skill's own rates, on the Parser page and in the overlay's pop-up on a member. New: close but still being checked against the game's Combat Analysis.", "Parser crit and back-attack rates");
        _parserHoversChk = Row("Explanations on hover", "What a figure, a row or a switch means, when the pointer rests on it.", "Parser explanations on hover");
        _parserSeparateChk = Row("Parser inside the Kinah overlay", "Off (the default), the Parser has its own overlay you can drag anywhere, and the Kinah overlay stays as it is. On, it opens above the Kinah overlay when a fight starts, takes its place in a dungeon, and takes the overlay's look.", "Parser inside the Kinah overlay");
        _parserSeparateChk.Click += (_, _) => { _parserTheme = null; DrawParserLooks(); };
        more.Children.Add(Styled(new Border { Child = _parserMoreStack, Margin = new Thickness(0, 8, 0, 0) }, "Group"));
        // the share pictures (Ui/MeterShare.cs, 1.0.2): other members' names shown on them (off: their class), and a shortcut of its own
        var share = new StackPanel();
        _parserHideNamesChk = Styled(new CheckBox(), "Switch");
        AutomationProperties.SetName(_parserHideNamesChk, "Show party names");
        _parserHideNamesChk.Click += (_, _) => Refresh();
        share.Children.Add(SideRow("Show party names", "Off (the default), share pictures name only you: the others show as their class, \"Chanter\", with their figures. On, their names show too.", _parserHideNamesChk));
        share.Children.Add(Styled(new Border(), "Divider"));
        share.Children.Add(BuildShareKeyRow());
        more.Children.Add(Styled(new TextBlock { Text = L.T("SHARE PICTURE") }, "GroupLabel"));
        more.Children.Add(Styled(new Border { Child = share }, "Group"));
        PageMeter.Children.Add(more);
        Fold(more);
    }

    /// <summary>
    /// The meter's width (Steven, 2026-11-14: "maybe an option to make it wider?"), apart from the overlay's size: the rows
    /// and names stretch, the figures stay at the right. Shown on the overlay as it moves; Save keeps it, Cancel puts it
    /// back. When it shows shares the row: boss fights and dungeons (the default), or always (parser.json AlwaysOn).
    /// </summary>
    FrameworkElement BuildParserWidthRow()
    {
        var g = Styled(new Grid(), "Row");
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(92) });
        g.ColumnDefinitions.Add(new ColumnDefinition());
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        g.Children.Add(Styled(new TextBlock { Text = L.T("Width"), VerticalAlignment = VerticalAlignment.Center, ToolTip = L.T("How wide the meter is over the game. You can also drag its left or right edge while the overlay can be moved.") }, "RowTitle"));
        _parserWidth = new Slider { Width = 116, Minimum = HudStackLook.FightWidthMin, Maximum = HudStackLook.FightWidthMax, TickFrequency = 10, IsSnapToTickEnabled = true, VerticalAlignment = VerticalAlignment.Center, Value = HudStackLook.FightWidth };
        AutomationProperties.SetName(_parserWidth, "Parser overlay width");
        _parserWidthLbl = new TextBlock { FontSize = 12.5, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0), Width = 40 };
        _parserWidthLbl.SetResourceReference(TextBlock.ForegroundProperty, "Muted");
        void Label() => _parserWidthLbl!.Text = WidthWords(_parserWidth!.Value);
        Label();
        _parserWidth.ValueChanged += (_, e) =>
        {
            Label();
            if (_parserWidthLoading) return;
            _host.PreviewFightWidth(Math.Round(e.NewValue));
            if (_parserLookStage != null) { _parserLookStage.Tag = null; DrawParserStage(_parserLookShown); }   // the stage at the new width
            Refresh();
        };
        var left = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Children = { _parserWidth, _parserWidthLbl } };
        Grid.SetColumn(left, 1); g.Children.Add(left);
        var when = Styled(new TextBlock { Text = L.T("When it shows"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0) }, "RowTitle");
        Grid.SetColumn(when, 2); g.Children.Add(when);
        _parserWhenBox = new ComboBox { Width = 222, VerticalAlignment = VerticalAlignment.Center, ItemsSource = new[] { L.T("Boss fights and dungeons"), L.T("Always") } };
        AutomationProperties.SetName(_parserWhenBox, "When the DPS meter shows");
        _parserWhenBox.ToolTip = L.F(" The meter is always up, the open world too: from 0 at each boss pull, on entering a dungeon, and after {0:0} seconds without fighting.", BossParser.AlwaysIdleSeconds).Trim();
        _parserWhenBox.SelectionChanged += (_, _) => Refresh();
        Grid.SetColumn(_parserWhenBox, 3); g.Children.Add(_parserWhenBox);
        return g;
    }

    /// <summary>
    /// Share picture shortcut: recorded as the In game shortcuts are (Change, then the keys; Esc cancels), Off by default, never
    /// the start/stop, That was wrong or overlay key. Saved with the Parser's switches.
    /// </summary>
    FrameworkElement BuildShareKeyRow()
    {
        var box = new StackPanel { Margin = new Thickness(16, 10, 16, 12) };
        box.Children.Add(Styled(new TextBlock { Text = L.T("Share picture shortcut") }, "RowTitle"));
        _parserShareKeyHint = Styled(new TextBlock { Text = L.T(ShareKeyHintText) }, "RowHint");
        box.Children.Add(_parserShareKeyHint);
        var row = new WrapPanel { Margin = new Thickness(0, 8, 0, 0) };
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
    /// <summary>For the options test: a click on a look tile (the meter's own window's look, or inside the Kinah overlay the Kinah overlay's).</summary>
    internal void PickParserLookForTest(string key)
    {
        var card = _parserLookHost?.Children.OfType<Border>().FirstOrDefault(b => (string)b.Tag == key);
        card?.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left) { RoutedEvent = UIElement.MouseLeftButtonUpEvent });
    }

    /// <summary>
    /// The look tiles, the one in use outlined, the stage drawing the one hovered or picked. With the meter in its own window
    /// (the default) they are that window's looks, apart from the Kinah overlay's: Standard (its own panel) and the four
    /// drawn for fights, fight alone, or another theme's. Inside the Kinah overlay they are the Kinah overlay's fight looks.
    /// </summary>
    void DrawParserLooks()
    {
        if (_parserLookHost == null || _parserLookHint == null) return;
        _parserLookHost.Children.Clear();
        if (_parserLookStage != null) _parserLookStage.Tag = null;
        bool inKinah = _parserSeparateChk?.IsChecked == true;
        string kinahTheme = _hudTheme ?? HudThemes.Of(_edit.HudLook);   // the Kinah overlay's theme (Ui/OptionsWindow.Themes.cs)
        if (_parserLookRow != null) _parserLookRow.Visibility = Visibility.Visible;
        if (_parserAllLooks != null) _parserAllLooks.Visibility = inKinah ? Visibility.Visible : Visibility.Collapsed;
        // every theme's looks are on offer either way (Steven, 2026-11-20: "you should not have to choose the buddy theme to
        // choose the dps meter theme ... the styles should always all be available"): in its own window the meter's theme
        // starts at its look's, inside the Kinah overlay at the overlay's
        _parserTheme ??= inKinah ? kinahTheme : ParserThemeOf(_parserLookPick);
        // the overlay's own fight looks draw inside it; any other look is the meter's own window's (OwnLook)
        bool own = OwnLooksShown;
        var current = own ? _parserLookPick : HudLooks.Parse(_edit.HudLook);
        if (_parserThemeHost != null)
        {
            _parserThemeHost.Children.Clear();
            if (_parserThemeRow != null) _parserThemeRow.Visibility = Visibility.Visible;
            foreach (var t in HudThemes.All)
            {
                var tk = t.Key;
                var chip = _host.Chip(L.T(t.Name), tk == _parserTheme, () => { if (tk == _parserTheme) return; _parserTheme = tk; DrawParserLooks(); }, L.T(t.Hint));
                chip.Tag = "parser-theme:" + tk;
                _parserThemeHost.Children.Add(chip);
            }
        }
        var keys = own ? ParserLooksOf(_parserTheme!).ToArray() : HudThemes.FightLooks(kinahTheme).ToArray();
        _parserLookHost.Columns = Math.Max(3, keys.Length);
        string inUseName = own ? MainWindow.ParserLookName(current) : HudLooks.All.First(l => l.Key == current).Name;
        _parserLookHint.Text = !inKinah
            ? L.T("Its own look over the game, apart from the Buddy's.")
            : own ? L.T("Picking one of these gives the Parser its own overlay in that look. The Kinah overlay keeps its look.")
            : HudLooks.FightLooks.Contains(current) ? L.T("How a boss fight shows over the game, in a dungeon in place of the Kinah line. Picking one here picks the Kinah overlay's look too.")
            : L.F("In use: {0}, the Parser above it. Pick one drawn for fights, or see all looks.", L.T(inUseName));
        var scene = HudKit.GameScene();   // over the game (Steven, 2026-11-16)
        _parserLookShown = keys.Contains(current) ? current : keys[0];
        foreach (var key in keys)
        {
            string lookName = own ? MainWindow.ParserLookName(key) : HudLooks.All.First(x => x.Key == key).Name;
            string hint = L.T(MainWindow.PanelLookHint(key) ?? HudLooks.All.First(x => x.Key == key).Hint);
            bool on = key == current && (!own || !inKinah);
            var k = key;
            var card = LookTile(key, L.T(lookName), hint, on, () => ParserFace(k, own), scene, lookName + " overlay look");
            void Pick()
            {
                if (!own) { PickHudLook(k); return; }
                // a look from another theme than the Kinah overlay's: the Parser takes its own window in it, the overlay stays
                if (inKinah) _parserSeparateChk.IsChecked = false;
                _parserLookPick = k; DrawParserLooks(); Refresh();
            }
            card.MouseLeftButtonUp += (_, _) => Pick();
            card.KeyDown += (_, e) => { if (e.Key is Key.Enter or Key.Space) { Pick(); e.Handled = true; } };
            card.MouseEnter += (_, _) => DrawParserStage(k);
            card.GotKeyboardFocus += (_, _) => DrawParserStage(k);
            card.MouseLeave += (_, _) => DrawParserStage(_parserLookShown);
            _parserLookHost.Children.Add(card);
        }
        DrawParserStage(_parserLookShown);
    }

    /// <summary>
    /// True when the look tiles are the meter's own window's: always with the meter in its own window, and inside the Kinah
    /// overlay when the theme picked is not the overlay's (picking one then gives the meter its own window).
    /// </summary>
    bool OwnLooksShown => _parserSeparateChk?.IsChecked != true || (_parserTheme != null && _parserTheme != (_hudTheme ?? HudThemes.Of(_edit.HudLook)));

    /// <summary>One look in a boss fight (the sample pull), as the meter's own window or the Kinah overlay draws it; null when it fails.</summary>
    FrameworkElement? ParserFace(string key, bool separate)
    {
        var now = new DateTime(2026, 11, 13, 21, 30, 0, DateTimeKind.Utc);
        var (_, fight) = ParserDemo.Sample(now, live: true);
        try { return separate ? ParserOverlay.PreviewFace(key, fight!, now, fight!.ModeKind, false) : HudWindow.PreviewFight(key, _host.HudSample(), fight!, now); }
        catch (Exception ex) { CrashLog.Write("parser look preview", ex); return null; }
    }

    /// <summary>The stage: one look in a boss fight at its real size over the game scene, scaled down only past the stage.</summary>
    void DrawParserStage(string? key)
    {
        if (_parserLookStage == null || key == null) return;
        if (_parserLookStage.Tag as string == key) return;
        bool separate = OwnLooksShown;
        if (ParserFace(key, separate) is not { } face) return;
        bool inUse = separate ? key == _parserLookPick && _parserSeparateChk?.IsChecked != true : key == HudLooks.Parse(_edit.HudLook);
        string name = separate ? MainWindow.ParserLookName(key) : HudLooks.All.First(x => x.Key == key).Name;
        var stage = Stage(face, L.T(name), inUse ? L.T("in use") : L.T("hover to compare · click to pick"), MeterStageHeight);
        _parserLookStage.Tag = key;
        _parserLookStage.Child = stage;
    }

    /// <summary>The DPS meter page (the Parser page's "Look" link).</summary>
    public void ShowParserLook() => Go(Page.Meter);

    /// <summary>For the options test: the look the big preview draws.</summary>
    internal string? ParserLookStageForTest => _parserLookStage?.Tag as string;

    /// <summary>For the options test: the mouse entering a look tile (a key), or leaving it (null).</summary>
    internal void HoverParserLookForTest(string? key)
    {
        var card = _parserLookHost?.Children.OfType<Border>().FirstOrDefault(b => key == null ? b.IsMouseOver == false : (string)b.Tag == key);
        card?.RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice, 0) { RoutedEvent = key == null ? UIElement.MouseLeaveEvent : UIElement.MouseEnterEvent });
    }

    /// <summary>For the options test: the meter window's own theme, and a click on one of its theme chips.</summary>
    internal string? ParserThemeForTest => _parserTheme;
    /// <summary>For the options test: the Kinah overlay's look as edited (not yet saved).</summary>
    internal string HudLookEditForTest => _edit.HudLook;
    internal void PickParserThemeForTest(string theme) { if (theme == _parserTheme) return; _parserTheme = theme; DrawParserLooks(); }

    /// <summary>For the options test: the look tiles (their keys) and which is outlined.</summary>
    internal (List<string> Keys, string? On) ParserLooksForTest => (_parserLookHost?.Children.OfType<Border>().Select(b => (string)b.Tag).ToList() ?? new(),
        _parserLookHost?.Children.OfType<Border>().FirstOrDefault(b => b.BorderThickness.Left > 1)?.Tag as string);

    /// <summary>For the options test: the meter's switches.</summary>
    internal (CheckBox Details, CheckBox Kills, CheckBox Deaths, CheckBox Filters, CheckBox Hovers, CheckBox Separate, CheckBox Active) ParserSwitchesForTest => (_parserDetailsChk, _parserKillsChk, _parserDeathsChk, _parserFiltersChk, _parserHoversChk, _parserSeparateChk, _parserActiveChk);

    /// <summary>For the options test: the crit and back-attack switch, the heal bars, and the header's on/off, click-through and When it shows.</summary>
    internal CheckBox ParserCritForTest => _parserCritChk;
    internal CheckBox ParserAddsForTest => _parserAddsChk;
    internal CheckBox ParserExpandedForTest => _parserExpandedChk;
    internal CheckBox ParserHealBarsForTest => _parserHealBarsChk;
    internal (CheckBox Ghost, CheckBox Group) ParserGhostForTest => (_parserGhostChk, _parserGhostGroupChk);
    internal CheckBox ParserPvpForTest => _parserPvpChk;
    internal CheckBox ParserOdyleForTest => _parserOdyleChk;
    internal (CheckBox Show, CheckBox Through, ComboBox When) ParserOverlaySwitchesForTest => (_parserOverlayChk, _parserThroughChk, _parserWhenBox);

    /// <summary>For the options test: every switch's title under More.</summary>
    internal List<string> ParserSwitchTitlesForTest => new[] { _parserDetailsChk, _parserKillsChk, _parserDeathsChk, _parserFiltersChk, _parserHoversChk, _parserSeparateChk, _parserActiveChk, _parserAddsChk, _parserExpandedChk, _parserCritChk, _parserHealBarsChk, _parserGhostChk, _parserGhostGroupChk, _parserPvpChk, _parserOdyleChk, _parserHideNamesChk }
        .Select(c => AutomationProperties.GetName(c)).ToList();
}
