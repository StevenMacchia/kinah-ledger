using System.Globalization;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
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

    readonly bool _still;
    readonly StackPanel _body = new();
    readonly Border _frame;
    readonly TextBlock _hint;
    bool _clickThrough;
    string _sig = "";
    public const double Width0 = 340;

    static readonly SolidColorBrush Coral = HudKit.B(HudKit.RecC), Text2 = HudKit.B(0xC9CDDA), Track = HudKit.B(0xFFFFFF, 0.08);

    /// <param name="still">A drawing (layout renders): not the overlay the readers step around.</param>
    public ParserOverlay(bool still = false)
    {
        _still = still;
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
        _hint = new TextBlock { FontSize = 10.5, Foreground = HudKit.Faint, Margin = new Thickness(0, 6, 0, 0), Visibility = Visibility.Collapsed };
        _frame = new Border
        {
            Width = Width0,
            Background = HudKit.B(HudKit.PanelC, 0.94), BorderBrush = HudKit.FrameLine, BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(12), Padding = new Thickness(12, 10, 12, 10),
            Child = new StackPanel { Children = { _body, _hint } },
        };
        Content = _frame;
        MouseLeftButtonDown += (_, _) =>
        {
            if (_clickThrough || _still) return;
            try { DragMove(); } catch (InvalidOperationException) { return; }
            var h = new WindowInteropHelper(this).Handle;
            if (h != IntPtr.Zero && GetWindowRect(h, out var r)) Dropped?.Invoke(r.Left, r.Top);
        };
        MouseEnter += (_, _) => ShowHint(); MouseLeave += (_, _) => ShowHint();
        SourceInitialized += (_, _) => Native.SetClickThrough(new WindowInteropHelper(this).Handle, _clickThrough);
        SizeChanged += (_, _) => PlacePending();
    }

    /// <summary>The overlay's shortcut, said in the drag hint ("Ctrl+Shift+P").</summary>
    public string HotkeyText { get; set; } = "";

    void ShowHint()
    {
        _hint.Text = _clickThrough ? "" : HotkeyText.Length > 0 ? $"drag to move  ·  {HotkeyText} hides it" : "drag to move";
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

    /// <summary>Puts it where it was dropped last, or by default at the game's left edge a third of the way down: clear of the minimap (top-right), the chat (bottom-left) and the skill bar (bottom).</summary>
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
        Native.MoveTo(h, a.Left + (int)Math.Round(24 * dpi.DpiScaleX), a.Top + (int)Math.Round(a.Height * 0.30));
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

    /// <summary>The fight: under way (the clock running), or just ended (its result at the foot).</summary>
    public void Draw(Encounter e, DateTime now)
    {
        var sig = $"{e.Id}:{(int)e.Duration(now).TotalSeconds}:{e.Outcome}:{e.Roster.Count}:{e.Roster.Sum(f => f.Damage)}:{e.Partial}:{e.BossHp}:{string.Join(",", e.Roster.Select(f => f.CombatPower))}";
        if (sig == _sig) return;
        _sig = sig;
        _body.Children.Clear();
        // the head: the boss's art, its name, the clock
        var head = new Grid();
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var art = GameArt.Element("boss", 22); art.Margin = new Thickness(0, 0, 8, 0);
        head.Children.Add(art);
        var name = T(e.Boss, 13.5, FontWeights.Bold, HudKit.Ink); name.TextTrimming = TextTrimming.CharacterEllipsis;
        Grid.SetColumn(name, 1); head.Children.Add(name);
        var clock = T(BossParser.Clock(e.Duration(now)), 16, FontWeights.Bold, e.Live ? HudKit.Gold : HudKit.Ink);
        clock.Margin = new Thickness(10, 0, 0, 0);
        Grid.SetColumn(clock, 2); head.Children.Add(clock);
        _body.Children.Add(head);
        var sub = T($"{BossBook.Strip(e.Instance)} · {e.Kind}", 11, FontWeights.Medium, HudKit.Faint);
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
            bool gear = e.Members.Any(f => f.CombatPower > 0 || f.ItemLevel > 0);
            var rows = new StackPanel { Margin = new Thickness(0, 8, 0, 0) };
            Grid Row()
            {
                var g = new Grid();
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(22) });
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
            hdr.Margin = new Thickness(0, 0, 0, 2);
            rows.Children.Add(hdr);
            long top = Math.Max(1, e.Roster.Max(f => f.Damage));
            foreach (var f in e.Ranked().Take(8))
            {
                var g = Row(); g.Margin = new Thickness(0, 3, 0, 0);
                var mark = f.Other ? MainWindow.OtherMark(16) : MainWindow.ClassMark(f.Class, 16, f.Name);
                g.Children.Add(mark);
                var nm = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) };
                var n = T(f.Other ? "Other" : f.Name.Length > 0 ? f.Name : "Unnamed", 12, f.Other ? FontWeights.Medium : FontWeights.SemiBold, f.You ? HudKit.Gold : f.Other || f.Name.Length == 0 ? HudKit.Muted : HudKit.Ink); n.TextTrimming = TextTrimming.CharacterEllipsis;
                nm.Children.Add(n);
                if (dmg)
                {
                    var bar = new Grid { Height = 2, Margin = new Thickness(0, 2, 0, 0) };
                    bar.Children.Add(new Border { Background = Track, CornerRadius = new CornerRadius(1) });
                    var fill = new Border { Background = f.You ? HudKit.Gold : HudKit.Muted, CornerRadius = new CornerRadius(1), HorizontalAlignment = HorizontalAlignment.Left };
                    double share = f.Damage / (double)top;
                    bar.SizeChanged += (_, _) => fill.Width = Math.Max(2, bar.ActualWidth * share);
                    bar.Children.Add(fill);
                    nm.Children.Add(bar);
                }
                Grid.SetColumn(nm, 1); g.Children.Add(nm);
                void C(int col, string text, Brush b, FontWeight w) { var t = T(text, 11.5, w, b); t.HorizontalAlignment = HorizontalAlignment.Right; Grid.SetColumn(t, col); g.Children.Add(t); }
                if (gear && !f.Other)
                {
                    C(2, f.CombatPower > 0 ? f.CombatPower.ToString("N0", CultureInfo.InvariantCulture) : "–", Text2, FontWeights.SemiBold);
                    C(3, f.ItemLevel > 0 ? f.ItemLevel.ToString("N0", CultureInfo.InvariantCulture) : "–", HudKit.Muted, FontWeights.Medium);
                }
                if (dmg) C(4, Amounts.Short(e.Dps(f, now)), f.You ? HudKit.Gold : HudKit.Ink, FontWeights.Bold);
                rows.Children.Add(g);
            }
            if (e.Roster.Count > 8) rows.Children.Add(T($"+{e.Roster.Count - 8} more on the Parser page", 10.5, FontWeights.Medium, HudKit.Faint));
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
            var l = T(e.HasDamage ? "Group DPS" : e.Kills.Count(k => !k.Boss) > 0 ? "Kills near you" : "Fighting", 11, FontWeights.Medium, HudKit.Muted);
            foot.Children.Add(l);
            var v = T(e.HasDamage ? Amounts.Short(e.GroupDps(now)) : e.Kills.Count(k => !k.Boss) > 0 ? e.Kills.Count(k => !k.Boss).ToString(CultureInfo.InvariantCulture) : "", 12, FontWeights.Bold, HudKit.Ink);
            Grid.SetColumn(v, 1); foot.Children.Add(v);
        }
        else
        {
            var (text, brush) = e.Outcome switch
            {
                "kill" => ($"Killed in {BossParser.Clock(e.Duration(now))}", HudKit.Mint),
                "wipe" => ($"Wiped after {BossParser.Clock(e.Duration(now))}", Coral),
                _ => ($"Left after {BossParser.Clock(e.Duration(now))}", HudKit.Muted),
            };
            foot.Children.Add(T(text, 12, FontWeights.Bold, brush));
            var s = T("kept on the Parser page", 10.5, FontWeights.Medium, HudKit.Faint);
            Grid.SetColumn(s, 1); foot.Children.Add(s);
        }
        _body.Children.Add(foot);
        _frame.BorderBrush = e.Live ? HudKit.FrameLine : e.Outcome == "kill" ? HudKit.B(HudKit.MintC, 0.55) : e.Outcome == "wipe" ? HudKit.B(HudKit.RecC, 0.55) : HudKit.FrameLine;
    }

    static TextBlock T(string text, double size, FontWeight w, Brush b) => new() { Text = text, FontSize = size, FontWeight = w, Foreground = b, VerticalAlignment = VerticalAlignment.Center };
}
