using System.Globalization;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Interop;
using System.Windows.Media;
using KinahLedger.Parser;

namespace KinahLedger.Ui;

/// <summary>
/// The boss fight over the game (Steven, 2026-11-13: "We should be able to toggle on and off and drag the overlay too"):
/// the boss, the clock, the group with Combat Power and item level, and DPS when the game gives damage. It shows only
/// during a boss fight (and for a few seconds after, with the result), only while the game or the tracker is in front,
/// never takes focus (a tool window that doesn't activate), stays where it was dragged, and lets clicks through when
/// asked. Drawn in the panel's own dark palette (HudKit), like the panel over the game, whatever the window's look.
/// </summary>
public sealed class ParserOverlay : Window
{
    /// <summary>The overlay on screen, if any: the readers step around it.</summary>
    public static ParserOverlay? Current { get; private set; }

    /// <summary>The player dropped it somewhere (pixels, the window's top-left).</summary>
    public event Action<int, int>? Dropped;
    /// <summary>The overlay's own menu (a right-click): a mode picked, hide it, clicks through.</summary>
    public event Action<ParserMode>? ModePicked;
    public event Action? HideAsked;
    public event Action? ClickThroughAsked;
    /// <summary>Each member's Combat Power and item level beside their DPS (Options › In game › Parser › Group details).</summary>
    public bool ShowDetails { get; set; }
    /// <summary>The mode the menu ticks (set by the window).</summary>
    public ParserMode Mode { get; set; }

    readonly bool _still;
    readonly ParserPanel _panel = new();
    readonly TextBlock _hint;
    bool _clickThrough;
    public const double Width0 = 340;

    /// <param name="still">A drawing (layout renders): not the overlay the readers step around.</param>
    public ParserOverlay(bool still = false)
    {
        _still = still;
        // its pictures loaded before the first draw: one first loaded while the window isn't shown yet draws blank
        foreach (var art in new[] { "boss", "dungeon", "medal", "rift" }) GameArt.Image(art);
        if (!still) { Current = this; Closed += (_, _) => { if (Current == this) Current = null; }; }
        ShowActivated = false;
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        Topmost = true;
        ShowInTaskbar = false;
        ResizeMode = ResizeMode.NoResize;
        SizeToContent = SizeToContent.WidthAndHeight;
        WindowStartupLocation = WindowStartupLocation.Manual;
        Title = "Kinah Ledger fight overlay";
        FontFamily = HudKit.Text;
        FontWeight = FontWeights.Medium;
        TextOptions.SetTextFormattingMode(this, TextFormattingMode.Display);
        Left = -10000; Top = -10000;   // placed once it has laid out (Place)
        _hint = new TextBlock { FontSize = 10.5, Foreground = HudKit.Faint, Margin = new Thickness(8, 3, 0, 0), Visibility = Visibility.Collapsed };
        Content = new StackPanel { Children = { _panel, _hint } };
        MouseLeftButtonDown += (_, _) =>
        {
            if (_clickThrough || _still) return;
            try { DragMove(); } catch (InvalidOperationException) { return; }
            var h = new WindowInteropHelper(this).Handle;
            if (h != IntPtr.Zero && GetWindowRect(h, out var r)) Dropped?.Invoke(r.Left, r.Top);
        };
        MouseEnter += (_, _) => ShowHint(); MouseLeave += (_, _) => ShowHint();
        // its small menu: what the Parser keeps, hide, clicks through (never while clicks go through: the game has them)
        MouseRightButtonUp += (_, ev) => { if (_clickThrough || _still) return; ev.Handled = true; OpenMenu(); };
        SourceInitialized += (_, _) => Native.SetClickThrough(new WindowInteropHelper(this).Handle, _clickThrough);
        SizeChanged += (_, _) => PlacePending();
    }

    /// <summary>The overlay's shortcut, said in the drag hint ("Ctrl+Shift+P").</summary>
    public string HotkeyText { get; set; } = "";

    /// <summary>The overlay's menu, in its own dark look: the three modes (the one on ticked, what each counts on hover), then hide and clicks through.</summary>
    internal ContextMenu BuildMenu()
    {
        var menu = new ContextMenu { Background = HudKit.B(HudKit.Panel2C), BorderBrush = HudKit.FrameLine, Foreground = HudKit.Ink, FontFamily = HudKit.Text, FontSize = 12.5, Padding = new Thickness(2, 4, 2, 4) };
        menu.Items.Add(new MenuItem { Header = "What the Parser keeps", IsEnabled = false, Foreground = HudKit.Faint, FontSize = 11.5 });
        foreach (var md in new[] { ParserMode.Bosses, ParserMode.Run, ParserMode.Fights })
        {
            var item = new MenuItem { Header = BossParser.Name(md), IsCheckable = true, IsChecked = md == Mode, ToolTip = BossParser.Counts(md), Foreground = HudKit.Ink };
            var pick = md;
            item.Click += (_, _) => ModePicked?.Invoke(pick);
            menu.Items.Add(item);
        }
        menu.Items.Add(new Separator { Background = HudKit.FrameLine });
        var hide = new MenuItem { Header = HotkeyText.Length > 0 ? $"Hide the overlay ({HotkeyText})" : "Hide the overlay", Foreground = HudKit.Ink };
        hide.Click += (_, _) => HideAsked?.Invoke();
        menu.Items.Add(hide);
        var through = new MenuItem { Header = "Let clicks through to the game", Foreground = HudKit.Ink, ToolTip = "Turn it off again on the Parser tab" };
        through.Click += (_, _) => ClickThroughAsked?.Invoke();
        menu.Items.Add(through);
        return menu;
    }

    void OpenMenu()
    {
        var menu = BuildMenu();
        menu.PlacementTarget = this;
        menu.IsOpen = true;
    }

    void ShowHint()
    {
        _hint.Text = _clickThrough ? "" : HotkeyText.Length > 0 ? $"drag to move  ·  right-click for the mode  ·  {HotkeyText} hides it" : "drag to move  ·  right-click for the mode";
        _hint.Visibility = !_clickThrough && IsMouseOver && !_still ? Visibility.Visible : Visibility.Collapsed;
    }

    public void SetClickThrough(bool on)
    {
        _clickThrough = on;
        var h = new WindowInteropHelper(this).Handle;
        if (h != IntPtr.Zero) Native.SetClickThrough(h, on);
        Cursor = on ? null : System.Windows.Input.Cursors.SizeAll;
        ShowHint();
    }

    // ---- where it goes ----

    (int X, int Y)? _placeAt;
    System.Drawing.Rectangle? _placeIn;

    /// <summary>Puts it where it was dropped last, or by default near the top left, right of the party list: clear of the party list, the minimap (top-right), the chat (bottom-left) and the skill bar (bottom).</summary>
    public void Place(int? left, int? top, System.Drawing.Rectangle? game)
    {
        // a place on a screen that is still there (a monitor unplugged since: the default place)
        _placeAt = left is int l && top is int t && System.Windows.Forms.Screen.AllScreens.Any(s => s.WorkingArea.Contains(l + 20, t + 20)) ? (l, t) : null;
        _placeIn = game;
        PlacePending();
    }

    void PlacePending()
    {
        var h = new WindowInteropHelper(this).Handle;
        if (h == IntPtr.Zero || ActualWidth < 1) return;
        if (_placeAt is { } p) { Native.MoveTo(h, p.X, p.Y); _placeAt = null; _placeIn = null; return; }
        if (_placeIn is not { } a) return;
        _placeIn = null;
        var dpi = VisualTreeHelper.GetDpi(this);
        // right of the party list and its effects (the left edge to about a third of the height across), under the currencies
        Native.MoveTo(h, a.Left + (int)Math.Round(a.Height * 0.36), a.Top + (int)Math.Round(a.Height * 0.16));
    }

    [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);
    [StructLayout(LayoutKind.Sequential)] struct RECT { public int Left, Top, Right, Bottom; }

    System.Drawing.Rectangle PixelBounds()
    {
        var h = new WindowInteropHelper(this).Handle;
        if (h == IntPtr.Zero || !IsVisible || !GetWindowRect(h, out var r)) return System.Drawing.Rectangle.Empty;
        return System.Drawing.Rectangle.FromLTRB(r.Left, r.Top, r.Right, r.Bottom);
    }

    bool _readHidden;
    /// <summary>True when the overlay should be on screen (set by the window's tick); a read that hid it shows it again only then.</summary>
    public bool Wanted { get; set; }

    /// <summary>A screen read with the overlay out of the way when it sits over the area read (as the panel over the game does).</summary>
    public static async Task HiddenIfOver(System.Drawing.Rectangle area, Func<Task> read)
    {
        var o = Current;
        bool hide = false;
        if (o != null) { try { hide = await o.Dispatcher.InvokeAsync(() => o.PixelBounds().IntersectsWith(area)); } catch (Exception) { hide = false; } }
        if (hide) { try { await o!.Dispatcher.InvokeAsync(() => { o._readHidden = true; o.Hide(); }); await Task.Delay(120); } catch (Exception) { hide = false; } }
        try { await read(); }
        finally
        {
            if (hide) { try { await o!.Dispatcher.InvokeAsync(() => { o._readHidden = false; if (o.Wanted) o.Show(); }); } catch (Exception) { /* closing */ } }
        }
    }

    /// <summary>Shown or hidden as the window's tick says, never while a read has it out of the way.</summary>
    public void ShowWanted(bool on)
    {
        Wanted = on;
        if (_readHidden) return;
        if (on && !IsVisible) Show();
        else if (!on && IsVisible) Hide();
    }

    // ---- what it shows ----

    /// <summary>The record (the panel draws it: Ui/ParserPanel below, shared with the Kinah overlay's parser section).</summary>
    public void Draw(Encounter e, DateTime now)
    {
        _panel.Mode = Mode; _panel.ShowDetails = ShowDetails;
        _panel.Draw(e, now);
    }
}

/// <summary>
/// What the Parser shows over the game, drawn once for both places it can sit (Steven, 2026-11-14: "expand by default but
/// give option in settings to separate"): the Kinah overlay's parser section (Ui/HudWindow.Parser.cs) and the separate
/// overlay window (above). The record's head (its art, title, clock), what it is and where, the boss's HP or a run's pull
/// and splits, a fight's kills, the members who have dealt damage on meter bars in their class's colour, and its foot.
/// </summary>
public sealed class ParserPanel : Border
{
    readonly StackPanel _body = new();
    string _sig = "";
    /// <summary>The mode the record is counted in, and whether each member's Combat Power and item level show.</summary>
    public ParserMode Mode { get; set; }
    public bool ShowDetails { get; set; }

    static readonly SolidColorBrush Coral = HudKit.B(HudKit.RecC), Text2 = HudKit.B(0xC9CDDA), Track = HudKit.B(0xFFFFFF, 0.08);

    public ParserPanel()
    {
        Width = ParserOverlay.Width0;
        Background = HudKit.B(HudKit.PanelC, 0.94); BorderBrush = HudKit.FrameLine; BorderThickness = new Thickness(1);
        CornerRadius = new CornerRadius(12); Padding = new Thickness(12, 10, 12, 10);
        TextOptions.SetTextFormattingMode(this, TextFormattingMode.Display);
        TextElement.SetFontFamily(this, HudKit.Text);
        TextElement.SetFontWeight(this, FontWeights.Medium);
        Child = _body;
        foreach (var art in new[] { "boss", "dungeon", "medal", "rift" }) GameArt.Image(art);
    }

    // ---- what it shows ----

    /// <summary>The fight: under way (the clock running), or just ended (its result at the foot).</summary>
    public void Draw(Encounter e, DateTime now)
    {
        var sig = $"{e.Id}:{(int)e.Duration(now).TotalSeconds}:{e.Outcome}:{e.Roster.Count}:{e.Roster.Sum(f => f.Damage)}:{e.Partial}:{e.BossHp}:{string.Join(",", e.Roster.Select(f => f.CombatPower))}:{e.Boss}:{e.Splits.Count}:{e.YourKills}:{e.YourDeaths}:{Mode}:{ShowDetails}";
        if (sig == _sig) return;
        _sig = sig;
        _body.Children.Clear();
        // the head: the boss's art, its name, the clock
        var head = new Grid();
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var art = GameArt.Element(e.ModeKind == ParserMode.Run ? "dungeon" : e.ModeKind == ParserMode.Fights && e.Boss.Length == 0 ? (e.Kind == "Abyss" ? "rift" : "medal") : "boss", 22); art.Margin = new Thickness(0, 0, 8, 0);
        head.Children.Add(art);
        var name = T(e.Title, 13.5, FontWeights.Bold, HudKit.Ink); name.TextTrimming = TextTrimming.CharacterEllipsis;
        Grid.SetColumn(name, 1); head.Children.Add(name);
        var clock = T(BossParser.Clock(e.Duration(now)), 16, FontWeights.Bold, e.Live ? HudKit.Gold : HudKit.Ink);
        clock.Margin = new Thickness(10, 0, 0, 0);
        Grid.SetColumn(clock, 2); head.Children.Add(clock);
        _body.Children.Add(head);
        var place = BossBook.Strip(e.Instance);
        var where = place.Length > 0 && place != e.Title && place != e.Kind ? $"{place} · {e.Kind}" : e.Kind;
        // the mode, said once at the head of the sub line, so a glance tells a run from a pull
        var sub = T($"{BossParser.Name(e.ModeKind)} · {where}", 11, FontWeights.Medium, HudKit.Faint);
        sub.TextTrimming = TextTrimming.CharacterEllipsis; sub.Margin = new Thickness(30, 1, 0, 0);
        if (e.Partial)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = sub.Margin };
            sub.Margin = new Thickness(0);
            row.Children.Add(sub);
            row.Children.Add(T("  ·  partial", 11, FontWeights.SemiBold, HudKit.Warn));
            row.ToolTip = string.Join("; ", e.PartialWhy);
            _body.Children.Add(row);
        }
        else _body.Children.Add(sub);

        // a run: the pull under way ("Now: the boss" and its clock) and the bosses done so far
        if (e.ModeKind == ParserMode.Run && e.Live)
        {
            var pull = e.Splits.LastOrDefault(x => x.Outcome == "");
            if (pull != null)
            {
                var now1 = new Grid { Margin = new Thickness(0, 7, 0, 0) };
                now1.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                now1.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                var nl = new StackPanel { Orientation = Orientation.Horizontal };
                nl.Children.Add(T("Now", 11, FontWeights.SemiBold, HudKit.Gold));
                var nb = T(pull.Boss, 12, FontWeights.Bold, HudKit.Ink); nb.Margin = new Thickness(6, 0, 0, 0); nb.TextTrimming = TextTrimming.CharacterEllipsis; nb.MaxWidth = 220;
                nl.Children.Add(nb);
                now1.Children.Add(nl);
                var pc = T(BossParser.Clock(TimeSpan.FromSeconds(pull.Seconds)), 12, FontWeights.Bold, HudKit.Gold);
                Grid.SetColumn(pc, 1); now1.Children.Add(pc);
                _body.Children.Add(now1);
            }
            var done = e.Splits.Where(x => x.Outcome != "").ToList();
            if (done.Count > 0)
            {
                var wp = new WrapPanel { Margin = new Thickness(0, 5, 0, 0) };
                foreach (var x in done.TakeLast(4))
                {
                    var c = x.Outcome == "kill" ? HudKit.Mint : x.Outcome == "wipe" ? Coral : HudKit.Muted;
                    var chip = new Border { CornerRadius = new CornerRadius(6), Padding = new Thickness(6, 1, 6, 2), Margin = new Thickness(0, 0, 4, 3), Background = HudKit.B(0xFFFFFF, 0.06),
                        Child = new StackPanel { Orientation = Orientation.Horizontal, Children = { T(x.Outcome == "kill" ? "✓" : x.Outcome == "wipe" ? "✕" : "·", 10.5, FontWeights.Bold, c), Pad(T($" {Short(x.Boss)} {BossParser.Clock(TimeSpan.FromSeconds(x.Seconds))}", 10.5, FontWeights.Medium, Text2)) } } };
                    wp.Children.Add(chip);
                }
                if (done.Count > 4) wp.Children.Add(T($"+{done.Count - 4}", 10.5, FontWeights.Medium, HudKit.Faint));
                _body.Children.Add(wp);
            }
        }
        // a fight: your kills (players apart) and deaths
        if (e.ModeKind == ParserMode.Fights && (e.YourKills > 0 || e.YourDeaths > 0))
        {
            var line = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 6, 0, 0) };
            if (e.YourKills > 0) line.Children.Add(T($"{e.YourKills} kill{(e.YourKills == 1 ? "" : "s")}{(e.PlayerKills > 0 ? $" · {e.PlayerKills} player{(e.PlayerKills == 1 ? "" : "s")}" : "")}", 12, FontWeights.Bold, HudKit.Mint));
            if (e.YourDeaths > 0) { var d = T($"{(e.YourKills > 0 ? "   " : "")}{e.YourDeaths} death{(e.YourDeaths == 1 ? "" : "s")}", 12, FontWeights.Bold, Coral); line.Children.Add(d); }
            _body.Children.Add(line);
        }

        // the boss's HP, when the game gives it
        if (e.BossHpLeft is { } left && e.Live)
        {
            var hp = new Grid { Margin = new Thickness(0, 7, 0, 0) };
            hp.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            hp.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var track = new Grid { Height = 6, VerticalAlignment = VerticalAlignment.Center };
            track.Children.Add(new Border { Background = Track, CornerRadius = new CornerRadius(3) });
            var fill = new Border { Background = left < 0.34 ? Coral : HudKit.Gold, CornerRadius = new CornerRadius(3), HorizontalAlignment = HorizontalAlignment.Left };
            track.SizeChanged += (_, _) => fill.Width = Math.Max(left > 0 ? 3 : 0, track.ActualWidth * left);
            track.Children.Add(fill);
            hp.Children.Add(track);
            var pct = T($"{left:P0}", 11.5, FontWeights.Bold, left < 0.34 ? Coral : HudKit.Ink); pct.Margin = new Thickness(8, 0, 0, 0);
            Grid.SetColumn(pct, 1); hp.Children.Add(pct);
            _body.Children.Add(hp);
        }

        // the group
        if (e.Roster.Count > 0)
        {
            bool dmg = e.HasDamage;
            bool gear = ShowDetails && e.Members.Any(f => f.CombatPower > 0 || f.ItemLevel > 0);
            var rows = new StackPanel { Margin = new Thickness(0, 8, 0, 0) };
            Grid Row()
            {
                var g = new Grid();
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(25) });
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(!gear ? 0 : dmg ? 50 : 60) });
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(!gear ? 0 : dmg ? 44 : 52) });
                if (dmg) g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(58) });
                return g;
            }
            var hdr = Row();
            void H(int col, string text) { var t = T(text, 10, FontWeights.SemiBold, HudKit.Faint); t.HorizontalAlignment = HorizontalAlignment.Right; Grid.SetColumn(t, col); hdr.Children.Add(t); }
            if (gear) { H(2, "CP"); H(3, "iLvl"); }
            if (dmg) H(4, "DPS");
            hdr.Margin = new Thickness(4, 0, 4, 1);
            rows.Children.Add(hdr);
            // each row on a meter bar in its class's colour (damage, else Combat Power), the class's emblem at its left
            double Measure(Fighter f) => dmg ? f.Damage : f.CombatPower;
            double top = Math.Max(1, e.Roster.Max(Measure));
            foreach (var f in e.Shown().Take(8))
            {
                var g = Row(); g.Margin = new Thickness(4, 0, 4, 0); g.MinHeight = 24;
                var mark = f.Other ? MainWindow.OtherMark(19) : MainWindow.ClassMark(f.Class, 19, f.Name);
                g.Children.Add(mark);
                var nm = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) };
                var n = T(f.Other ? "Other" : f.Name.Length > 0 ? f.Name : "Unnamed", 12, f.Other ? FontWeights.Medium : FontWeights.SemiBold, f.Other || f.Name.Length == 0 ? HudKit.Muted : HudKit.Ink); n.TextTrimming = TextTrimming.CharacterEllipsis;
                nm.Children.Add(n);
                if (f.You) nm.Children.Add(new Border { Background = HudKit.Gold, CornerRadius = new CornerRadius(5), Padding = new Thickness(5, 0, 5, 1), Margin = new Thickness(5, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center,
                    Child = T("you", 9.5, FontWeights.Bold, HudKit.B(0x1D1300)) });
                Grid.SetColumn(nm, 1); g.Children.Add(nm);
                void C(int col, string text, Brush b, FontWeight w) { var t = T(text, 11.5, w, b); t.HorizontalAlignment = HorizontalAlignment.Right; Grid.SetColumn(t, col); g.Children.Add(t); }
                if (gear && !f.Other)
                {
                    C(2, f.CombatPower > 0 ? f.CombatPower.ToString("N0", CultureInfo.InvariantCulture) : "–", Text2, FontWeights.SemiBold);
                    C(3, f.ItemLevel > 0 ? f.ItemLevel.ToString("N0", CultureInfo.InvariantCulture) : "–", HudKit.Muted, FontWeights.Medium);
                }
                if (dmg) C(4, Amounts.Short(e.Dps(f, now)), HudKit.Ink, FontWeights.Bold);
                var meter = ClassArt.MeterRow(g, f.Class, Measure(f) / top, other: f.Other, radius: 5);
                meter.Margin = new Thickness(0, 3, 0, 0);
                rows.Children.Add(meter);
            }
            if (e.Shown().Count() > 8) rows.Children.Add(T($"+{e.Shown().Count() - 8} more on the Parser page", 10.5, FontWeights.Medium, HudKit.Faint));
            _body.Children.Add(rows);
        }
        // the foot: the group's DPS while it runs; the result once it's over
        var foot = new Grid { Margin = new Thickness(0, 8, 0, 0) };
        foot.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        foot.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var sep = new Border { Height = 1, Background = HudKit.Sep, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, -4, 0, 0) };
        Grid.SetColumnSpan(sep, 2); foot.Children.Add(sep);
        if (e.Live)
        {
            int near = e.Kills.Count(k => !k.Boss && !k.Yours);
            var l = T(e.HasDamage ? (e.ModeKind == ParserMode.Run ? "Group DPS (boss pulls)" : "Group DPS") : e.YourKills > 0 ? "Your kills" : near > 0 ? "Kills near you" : "Fighting", 11, FontWeights.Medium, HudKit.Muted);
            foot.Children.Add(l);
            var v = T(e.HasDamage ? Amounts.Short(e.GroupDps(now)) : e.YourKills > 0 ? e.YourKills.ToString(CultureInfo.InvariantCulture) : near > 0 ? near.ToString(CultureInfo.InvariantCulture) : "", 12, FontWeights.Bold, HudKit.Ink);
            Grid.SetColumn(v, 1); foot.Children.Add(v);
        }
        else
        {
            var took = BossParser.Clock(e.Duration(now));
            var (text, brush) = e.Outcome switch
            {
                "kill" when e.ModeKind == ParserMode.Fights => ($"{e.OutcomeLabel} in {took}", HudKit.Mint),
                "kill" => ($"Killed in {took}", HudKit.Mint),
                "cleared" => ($"Cleared in {took}", HudKit.Mint),
                "wipe" => ($"Wiped after {took}", Coral),
                "died" => ($"Died after {took}", Coral),
                "ended" => ($"Ended after {took}", HudKit.Muted),
                _ => ($"Left after {took}", HudKit.Muted),
            };
            foot.Children.Add(T(text, 12, FontWeights.Bold, brush));
            var s = T("kept on the Parser page", 10.5, FontWeights.Medium, HudKit.Faint);
            Grid.SetColumn(s, 1); foot.Children.Add(s);
        }
        _body.Children.Add(foot);
        BorderBrush = e.Live ? HudKit.FrameLine : e.Tone == "good" ? HudKit.B(HudKit.MintC, 0.55) : e.Tone == "bad" ? HudKit.B(HudKit.RecC, 0.55) : HudKit.FrameLine;
    }

    static TextBlock Pad(TextBlock t) { t.Margin = new Thickness(0); return t; }
    /// <summary>A boss's name cut to its last word or two for a split chip ("Kromede's Desire", "Rotan").</summary>
    static string Short(string boss)
    {
        // "Talisra of the Void" is Talisra, "Silver Blade Rotan" is Rotan, "Kromede's Desire" stays whole
        int of = boss.IndexOf(" of ", StringComparison.OrdinalIgnoreCase);
        if (of > 0) return boss[..of];
        var w = boss.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return w.Length <= 2 ? boss : w[^1];
    }
    static TextBlock T(string text, double size, FontWeight w, Brush b) => new() { Text = text, FontSize = size, FontWeight = w, Foreground = b, VerticalAlignment = VerticalAlignment.Center };
}
