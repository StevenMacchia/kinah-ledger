using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using WPath = System.Windows.Shapes.Path;
using Shape = System.Windows.Shapes.Shape;

namespace KinahLedger.Ui;

/// <summary>
/// Options › In game › Rare drops (2026-10-26, kinah-bench/mock-hud-next f1-options): the watch list ("These always
/// alert, whatever their grade") as item tiles with a × each, an "Add item…" box that completes names from the item
/// list, and the chime: on or off, its volume and Play to hear it. Built here and added as the page's last group, on
/// the page's preview-then-Save model: nothing changes until Save, Cancel drops the edits (Play is only a preview).
/// </summary>
public partial class OptionsWindow
{
    TextBlock? _rareLabel;
    WrapPanel _watchHost = null!;
    TextBlock _watchEmpty = null!, _watchHint = null!, _chimeVolLbl = null!;
    TextBox _watchBox = null!;
    Popup _watchPop = null!;
    StackPanel _watchSuggest = null!;
    CheckBox _chimeChk = null!;
    Slider _chimeVol = null!;
    Button _chimePlay = null!;
    readonly Dictionary<string, Button> _watchRemove = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Load: the saved watch list and chime into the group (built the first time).</summary>
    void LoadRareDrops(OptionsChoice c)
    {
        if (_rareLabel == null) BuildRareDrops();
        _chimeChk.IsChecked = c.RareChime;
        _chimeVol.Value = c.RareVolume;
        _chimeVolLbl.Text = $"{c.RareVolume:P0}";
        ChimeRowState(c.RareChime);
        BuildWatchTiles(c.RareWatch);
        _watchBox.Text = "";
    }

    void BuildRareDrops()
    {
        _rareLabel = Styled(new TextBlock { Text = "RARE DROPS" }, "GroupLabel");
        var stack = new StackPanel();

        // the watch list: tiles, the empty line, and the box to add one
        var watch = new StackPanel { Margin = new Thickness(18, 13, 18, 15) };
        watch.Children.Add(Styled(new TextBlock { Text = "Watch list" }, "RowTitle"));
        watch.Children.Add(Styled(new TextBlock { Text = "These always alert, whatever their grade." }, "RowHint"));
        _watchHost = new WrapPanel { Margin = new Thickness(0, 2, 0, 0) };
        _watchEmpty = Styled(new TextBlock { Text = "None yet. Type a name below, or use \"Alert me when it drops\" on any item's card.", Margin = new Thickness(0, 8, 16, 0) }, "RowHint");
        watch.Children.Add(_watchHost);
        watch.Children.Add(_watchEmpty);
        watch.Children.Add(AddBox());
        stack.Children.Add(watch);
        stack.Children.Add(Styled(new Border(), "Divider"));

        // the chime: the switch, then the volume with Play
        var chime = new StackPanel { Margin = new Thickness(18, 13, 18, 15) };
        var row = new Grid();
        row.ColumnDefinitions.Add(new ColumnDefinition()); row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var title = Styled(new TextBlock { VerticalAlignment = VerticalAlignment.Center }, "RowTitle");
        title.Inlines.Add(new Run("Chime"));
        var sub = new Run("   on a rare drop or a watched item") { FontSize = 12 };
        sub.SetResourceReference(TextElement.ForegroundProperty, "Faint");
        title.Inlines.Add(sub);
        row.Children.Add(title);
        _chimeChk = Styled(new CheckBox(), "Switch");
        AutomationProperties.SetName(_chimeChk, "Chime on a rare drop or a watched item");
        Grid.SetColumn(_chimeChk, 1); row.Children.Add(_chimeChk);
        chime.Children.Add(row);

        _chimeVol = new Slider { Width = 220, Minimum = 0, Maximum = 1, TickFrequency = 0.05, IsSnapToTickEnabled = true, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 10, 0) };
        AutomationProperties.SetName(_chimeVol, "Chime volume");
        _chimeVolLbl = new TextBlock { FontSize = 12.5, FontFamily = new FontFamily("Cascadia Mono, Consolas"), Width = 40, VerticalAlignment = VerticalAlignment.Center };
        _chimeVolLbl.SetResourceReference(TextBlock.ForegroundProperty, "Muted");
        _chimePlay = new Button { Content = "Play", Height = 30, Padding = new Thickness(14, 0, 14, 0), Margin = new Thickness(6, 0, 0, 0), ToolTip = "Hear the chime at this volume" };
        AutomationProperties.SetName(_chimePlay, "Play the chime");
        var volRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 10, 0, 0), Children = { SpeakerIcon(), _chimeVol, _chimeVolLbl, _chimePlay } };
        chime.Children.Add(volRow);
        stack.Children.Add(chime);

        var group = Styled(new Border { Child = stack }, "Group");
        PageInGame.Children.Add(_rareLabel);
        PageInGame.Children.Add(group);

        _chimeChk.Click += (_, _) =>
        {
            ChimeRowState(_chimeChk.IsChecked == true);
            Edit(_edit with { RareChime = _chimeChk.IsChecked == true });
        };
        _chimeVol.ValueChanged += (_, _) =>
        {
            _chimeVolLbl.Text = $"{_chimeVol.Value:P0}";
            if (_loading) return;
            Edit(_edit with { RareVolume = Math.Round(_chimeVol.Value, 2) });
        };
        _chimePlay.Click += (_, _) => _host.PlayChime(_chimeVol.Value);   // a preview: nothing is kept
    }

    /// <summary>The volume follows the switch: greyed while the chime is off (Play still plays it).</summary>
    void ChimeRowState(bool on)
    {
        _chimeVol.IsEnabled = on;
        _chimeVol.Opacity = _chimeVolLbl.Opacity = on ? 1 : .45;
    }

    /* ---------------- the watch list ---------------- */

    void BuildWatchTiles(string text)
    {
        _watchHost.Children.Clear();
        _watchRemove.Clear();
        var names = MainWindow.WatchNames(text);
        foreach (var n in names) _watchHost.Children.Add(WatchTile(n));
        _watchEmpty.Visibility = names.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>One watched item: its tile in its grade frame (the panel's own tile), the name in the grade's colour, and × on the corner.</summary>
    FrameworkElement WatchTile(string name)
    {
        var (grade, icon) = _host.WatchItem(name);
        bool common = grade is "" or "Common";
        var tile = HudKit.Tile(new HudKit.Pick("watch|" + name, name, "", "", grade, icon, "", false, DateTime.MinValue), 44, glow: !common);
        tile.HorizontalAlignment = HorizontalAlignment.Center;
        var label = new TextBlock
        {
            Text = name, FontSize = 11.5, FontWeight = FontWeights.SemiBold, TextAlignment = TextAlignment.Center, TextWrapping = TextWrapping.Wrap,
            TextTrimming = TextTrimming.CharacterEllipsis, MaxHeight = 30, Margin = new Thickness(0, 7, 0, 0),
        };
        label.SetResourceReference(TextBlock.ForegroundProperty, common ? "Ink" : "Grade" + grade);
        var card = new Border
        {
            Width = 104, Margin = new Thickness(0, 10, 10, 0), Padding = new Thickness(6, 10, 6, 8), CornerRadius = new CornerRadius(8), BorderThickness = new Thickness(1),
            Child = new StackPanel { Children = { tile, label } }, ToolTip = common ? name : $"{name} · {grade}",
        };
        card.SetResourceReference(Border.BackgroundProperty, "Card2");
        card.SetResourceReference(Border.BorderBrushProperty, "Line");
        var x = RemoveButton(name);
        return new Grid { Children = { card, x } };
    }

    Button RemoveButton(string name)
    {
        var face = new FrameworkElementFactory(typeof(Border), "b");
        face.SetValue(Border.CornerRadiusProperty, new CornerRadius(9));
        face.SetValue(Border.BorderThicknessProperty, new Thickness(1));
        face.SetResourceReference(Border.BackgroundProperty, "Card");
        face.SetResourceReference(Border.BorderBrushProperty, "LineStrong");
        var cp = new FrameworkElementFactory(typeof(ContentPresenter));
        cp.SetValue(HorizontalAlignmentProperty, HorizontalAlignment.Center);
        cp.SetValue(VerticalAlignmentProperty, VerticalAlignment.Center);
        face.AppendChild(cp);
        var t = new ControlTemplate(typeof(Button)) { VisualTree = face };
        t.Triggers.Add(new Trigger { Property = IsMouseOverProperty, Value = true, Setters = { new Setter(Border.BorderBrushProperty, new DynamicResourceExtension("BtnHoverLine"), "b") } });
        var mark = new TextBlock { Text = "×", FontSize = 12, Margin = new Thickness(0, -2, 0, 0) };
        mark.SetResourceReference(TextBlock.ForegroundProperty, "Muted");
        var x = new Button
        {
            Template = t, Content = mark, Width = 18, Height = 18, Padding = new Thickness(0), Cursor = Cursors.Hand, HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 4, 4, 0), ToolTip = "Stop watching " + name,
        };
        AutomationProperties.SetName(x, "Stop watching " + name);
        x.Click += (_, _) => RemoveWatched(name);
        _watchRemove[name] = x;
        return x;
    }

    void RemoveWatched(string name)
    {
        var names = MainWindow.WatchNames(_edit.RareWatch);
        names.RemoveAll(n => string.Equals(n, name, StringComparison.OrdinalIgnoreCase));
        Edit(_edit with { RareWatch = string.Join("\n", names) });
        BuildWatchTiles(_edit.RareWatch);
    }

    void AddWatched(string name)
    {
        name = name.Trim();
        if (name.Length == 0) return;
        var names = MainWindow.WatchNames(_edit.RareWatch);
        if (!names.Contains(name, StringComparer.OrdinalIgnoreCase)) names.Add(name);
        Edit(_edit with { RareWatch = string.Join("\n", names) });
        BuildWatchTiles(_edit.RareWatch);
        _watchBox.Text = "";
        _watchPop.IsOpen = false;
    }

    /// <summary>Enter in the box: the first name offered, else the name as typed (a name the list doesn't know yet still alerts when the chat says it).</summary>
    void AddWatchedTop()
    {
        if (_watchSuggest.Children.Count > 0 && _watchSuggest.Children[0] is Button { Tag: string first }) AddWatched(first);
        else if (_watchBox.Text.Trim().Length >= 2) AddWatched(_watchBox.Text);
    }

    /* ---------------- "Add item…" with names from the item list ---------------- */

    FrameworkElement AddBox()
    {
        _watchBox = new TextBox { Tag = "Add item…", Height = 34, FontSize = 13.5, Padding = new Thickness(32, 0, 90, 0) };
        AutomationProperties.SetName(_watchBox, "Add an item to the watch list");
        var search = new WPath
        {
            Data = Geometry.Parse("M11.6,7 A4.6,4.6 0 1 1 2.4,7 A4.6,4.6 0 1 1 11.6,7 Z M10.5,10.5 L14,14"), Width = 16, Height = 16, StrokeThickness = 1.5,
            StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(11, 0, 0, 0), IsHitTestVisible = false,
        };
        search.SetResourceReference(Shape.StrokeProperty, "Faint");
        _watchHint = new TextBlock { Text = "type a name", FontSize = 11, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 12, 0), IsHitTestVisible = false };
        _watchHint.SetResourceReference(TextBlock.ForegroundProperty, "Faint");
        _watchSuggest = new StackPanel();
        var list = new Border { CornerRadius = new CornerRadius(8), BorderThickness = new Thickness(1), Padding = new Thickness(4), Child = _watchSuggest, Width = 420 };
        list.SetResourceReference(Border.BackgroundProperty, "Card2");
        list.SetResourceReference(Border.BorderBrushProperty, "LineStrong");
        _watchPop = new Popup { PlacementTarget = _watchBox, Placement = PlacementMode.Bottom, VerticalOffset = 4, StaysOpen = false, AllowsTransparency = true, Child = list };

        _watchBox.TextChanged += (_, _) => Suggest();
        _watchBox.PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter) { AddWatchedTop(); e.Handled = true; }
            else if (e.Key == Key.Down && _watchSuggest.Children.Count > 0) { ((UIElement)_watchSuggest.Children[0]).Focus(); e.Handled = true; }
            else if (e.Key == Key.Escape && _watchPop.IsOpen) { _watchPop.IsOpen = false; e.Handled = true; }
        };
        return new Grid { Width = 420, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 12, 0, 0), Children = { _watchBox, search, _watchHint, _watchPop } };
    }

    /// <summary>Up to eight names from the item list holding what is typed (from two letters), each with its tile and grade; one already watched is left out.</summary>
    void Suggest()
    {
        var text = _watchBox.Text.Trim();
        _watchHint.Visibility = _watchBox.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        _watchSuggest.Children.Clear();
        if (text.Length < 2) { _watchPop.IsOpen = false; return; }
        var have = MainWindow.WatchNames(_edit.RareWatch);
        foreach (var i in _host.ItemsLike(text, 12).Where(i => !have.Contains(i.Name, StringComparer.OrdinalIgnoreCase)).Take(8))
            _watchSuggest.Children.Add(SuggestRow(i.Name, i.Grade, _host.WatchItem(i.Name).Icon));
        _watchPop.IsOpen = _watchSuggest.Children.Count > 0 && IsVisible;
    }

    Button SuggestRow(string name, string grade, string icon)
    {
        bool common = grade is "" or "Common";
        var face = new FrameworkElementFactory(typeof(Border), "b");
        face.SetValue(Border.CornerRadiusProperty, new CornerRadius(6));
        face.SetValue(Border.PaddingProperty, new Thickness(8, 4, 8, 4));
        face.SetValue(Border.BackgroundProperty, Brushes.Transparent);
        var cp = new FrameworkElementFactory(typeof(ContentPresenter));
        cp.SetValue(VerticalAlignmentProperty, VerticalAlignment.Center);
        face.AppendChild(cp);
        var t = new ControlTemplate(typeof(Button)) { VisualTree = face };
        t.Triggers.Add(new Trigger { Property = IsMouseOverProperty, Value = true, Setters = { new Setter(Border.BackgroundProperty, new DynamicResourceExtension("ItemHover"), "b") } });
        t.Triggers.Add(new Trigger { Property = IsKeyboardFocusedProperty, Value = true, Setters = { new Setter(Border.BackgroundProperty, new DynamicResourceExtension("ItemSel"), "b") } });
        var tile = HudKit.Tile(new HudKit.Pick("suggest|" + name, name, "", "", grade, icon, "", false, DateTime.MinValue), 24);
        var text = new TextBlock { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 0, 0), FontSize = 13, FontWeight = FontWeights.Normal };
        var n = new Run(name);
        n.SetResourceReference(TextElement.ForegroundProperty, common ? "Ink" : "Grade" + grade);
        text.Inlines.Add(n);
        if (!common) { var g = new Run("  " + grade) { FontSize = 11.5 }; g.SetResourceReference(TextElement.ForegroundProperty, "Faint"); text.Inlines.Add(g); }
        var b = new Button
        {
            Template = t, Tag = name, Height = 32, HorizontalContentAlignment = HorizontalAlignment.Left, Cursor = Cursors.Hand,
            Content = new StackPanel { Orientation = Orientation.Horizontal, Children = { tile, text } },
        };
        AutomationProperties.SetName(b, "Watch " + name);
        b.Click += (_, _) => { AddWatched(name); _watchBox.Focus(); };
        b.PreviewKeyDown += (_, e) =>
        {
            int at = _watchSuggest.Children.IndexOf(b);
            if (e.Key == Key.Down && at + 1 < _watchSuggest.Children.Count) { ((UIElement)_watchSuggest.Children[at + 1]).Focus(); e.Handled = true; }
            else if (e.Key == Key.Up) { (at > 0 ? (UIElement)_watchSuggest.Children[at - 1] : _watchBox).Focus(); e.Handled = true; }
        };
        return b;
    }

    static FrameworkElement SpeakerIcon()
    {
        var body = new WPath { Data = Geometry.Parse("M2.5,6 L5.1,6 L8.6,3 L8.6,13 L5.1,10 L2.5,10 Z") };
        body.SetResourceReference(Shape.FillProperty, "Muted");
        var waves = new WPath { Data = Geometry.Parse("M10.6,5.6 A3.4,3.4 0 0 1 10.6,10.4 M12.4,3.9 A5.8,5.8 0 0 1 12.4,12.1"), StrokeThickness = 1.3, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round };
        waves.SetResourceReference(Shape.StrokeProperty, "Muted");
        return new Grid { Width = 16, Height = 16, VerticalAlignment = VerticalAlignment.Center, Children = { body, waves } };
    }

    static T Styled<T>(T e, string style) where T : FrameworkElement { e.SetResourceReference(StyleProperty, style); return e; }

    /* ---------------- the self-test and the render ---------------- */

    /// <summary>
    /// The options self-test's part (WpfApp.OptionsSelfTest): the group is the page's last, a typed name is offered from
    /// the item list, the watch list waits for Save and Cancel drops it, × takes one away, the chime's switch and volume
    /// are kept, and a watched Common item alerts with its watched flag while an unwatched one doesn't.
    /// </summary>
    internal static void SelfTestRareDrops(MainWindow w, Func<OptionsWindow> open, Action<bool, string> check)
    {
        static void Click(ButtonBase b) => b.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
        const string item = "Spiritstone";   // Common in the item list
        bool chimeBefore = w.HudChime; double volBefore = w.HudChimeVolume;
        check(!MainWindow.IsWatched(item) && !w.AlertFor(item + " ×1", DateTime.UtcNow), "rare drops: an unwatched Common pickup doesn't alert");

        var o = open();
        o.Go(Page.InGame);
        check(o._rareLabel != null && o.PageInGame.Children.IndexOf(o._rareLabel) == o.PageInGame.Children.Count - 2, "Rare drops is the In game page's last group");
        check(o._chimeChk.IsChecked == chimeBefore && Math.Abs(o._chimeVol.Value - volBefore) < 0.01, "the chime opens as saved");
        int tiles = o._watchHost.Children.Count;
        o._watchBox.Text = "spiritst";
        check(o._watchSuggest.Children.OfType<Button>().FirstOrDefault()?.Tag as string == item, "a typed name is offered from the item list: " + string.Join(", ", o._watchSuggest.Children.OfType<Button>().Select(b => b.Tag)));
        o.AddWatchedTop();
        check(o._watchHost.Children.Count == tiles + 1 && o._watchBox.Text == "" && o.SaveBtn.IsEnabled && !MainWindow.IsWatched(item), "an added item shows as a tile and waits for Save");
        Click(o.CancelBtn);
        DoEvents();
        check(!MainWindow.IsWatched(item), "Cancel drops the added item");

        o = open();
        o.Go(Page.InGame);
        check(o._watchHost.Children.Count == tiles, "Options opens with the saved watch list");
        o._watchBox.Text = item;
        o.AddWatchedTop();
        o._chimeChk.IsChecked = !chimeBefore; Click(o._chimeChk);
        o._chimeVol.Value = 0.3;
        check(w.HudChime == chimeBefore && Math.Abs(w.HudChimeVolume - volBefore) < 0.001, "the chime's switch and volume wait for Save");
        Click(o.SaveBtn);
        DoEvents();
        check(MainWindow.IsWatched(item) && w.HudChime == !chimeBefore && Math.Abs(w.HudChimeVolume - 0.3) < 0.001, "watch list and chime saved");
        bool alerted = w.AlertFor(item + " ×1", DateTime.UtcNow);
        var held = w.RareHeldForTest;
        string H(string k) => held.TryGetValue(k, out var v) ? v : "";
        check(alerted && H("rare") == item && H("rareWatched") == "1" && H("rareGrade") == "Common" && H("rareCount") == "×1" && H("rareItem").Length > 0
              && H("rareChime") == (!chimeBefore ? "1" : ""),
            $"a watched Common item alerts, flagged as watched ({H("rarePrice")} {H("rareWhere")})".Replace(" )", ")").Replace("( ", "("));
        check(HudRare.Of(held) is { Label: "WATCHED DROP", Watched: true }, "the looks say it as a watched drop");

        o = open();
        o.Go(Page.InGame);
        Click(o._watchRemove[item]);
        check(o._watchHost.Children.Count == tiles && MainWindow.IsWatched(item), "× takes the tile away and waits for Save");
        o._chimeChk.IsChecked = chimeBefore; Click(o._chimeChk);
        o._chimeVol.Value = volBefore;
        Click(o.SaveBtn);
        DoEvents();
        check(!MainWindow.IsWatched(item) && w.HudChime == chimeBefore && !w.AlertFor(item + " ×1", DateTime.UtcNow), "saved without it: the item no longer alerts");
    }

    /// <summary>In game scrolled to Rare drops, with a sample watch list (a layout render; nothing is saved).</summary>
    internal static void ShotRareDrops(MainWindow w, string file, Action<FrameworkElement, string> save)
    {
        w.SampleWatchList();
        var o = new OptionsWindow(w, Page.InGame) { WindowStartupLocation = WindowStartupLocation.Manual, Left = -12000, Top = -12000, ShowActivated = false };
        o.Show();
        IconCache.Drain(TimeSpan.FromSeconds(8));
        DoEvents();
        var at = o._rareLabel!.TransformToAncestor(o.Scroller).Transform(new Point(0, 0));
        o.Scroller.ScrollToVerticalOffset(Math.Max(0, o.Scroller.VerticalOffset + at.Y - 24));
        DoEvents();
        save((FrameworkElement)o.Content, file);
        o.Close();
    }

    static void DoEvents()
    {
        var frame = new System.Windows.Threading.DispatcherFrame();
        System.Windows.Threading.Dispatcher.CurrentDispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.ContextIdle, new Action(() => frame.Continue = false));
        System.Windows.Threading.Dispatcher.PushFrame(frame);
    }
}
