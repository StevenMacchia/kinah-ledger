using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace KinahLedger.Ui;

/// <summary>
/// Menus and actions in the card's own look (2026-10-31, Steven: "right click is not intuitive in this kind of app at all
/// and everything should be uniform ... windows-looking settings like when you click on the ⋯"). One menu, drawn with the
/// card kit (dark tooltip body, a gold rule along the top, rows that light gold on hover or keyboard focus, a switch for
/// an on/off), opened by a click; and a row of gold actions at the foot of a card a click pins. Nothing in the window
/// hangs off a right-click or a Windows ContextMenu: every action is something the player can see.
/// </summary>
public partial class MainWindow
{
    /// <summary>One line of a menu: an action (Do), a switch (On), a quiet line of information (Info) or a rule.</summary>
    internal sealed record MenuEntry(string Text, Action? Do = null, string Sub = "", object? Tip = null, bool? On = null, bool Info = false, bool Rule = false)
    {
        public static MenuEntry Line => new("", Rule: true);
    }

    const double MenuWidth = 290;

    /// <summary>The menu under <paramref name="at"/>, its right edge on the button's, closed by a click elsewhere or Esc.</summary>
    void ShowMenu(FrameworkElement at, IList<MenuEntry> entries)
    {
        if (_card != null) _card.IsOpen = false;
        var card = BuildMenu(entries);
        // the card's own margin (10, for its shadow) and border sit outside MenuWidth
        _card = new Popup
        {
            Child = card, StaysOpen = false, AllowsTransparency = true, PopupAnimation = PopupAnimation.Fade, Placement = PlacementMode.Bottom, PlacementTarget = at,
            HorizontalOffset = at.ActualWidth - MenuWidth - 12, VerticalOffset = -4,
        };
        _card.Opened += (_, _) => card.Focus();
        _card.IsOpen = true;
    }

    /// <summary>The menu's card: a gold rule along the top, then each entry; arrow keys move between the rows, Esc closes.</summary>
    internal Border BuildMenu(IList<MenuEntry> entries)
    {
        var (card, body) = CkShell(MenuWidth);
        card.Focusable = true; card.FocusVisualStyle = null;
        KeyboardNavigation.SetDirectionalNavigation(body, KeyboardNavigationMode.Cycle);
        AutomationProperties.SetName(card, "Menu");
        body.Children.Add(new Border
        {
            Height = 2, CornerRadius = new CornerRadius(5, 5, 0, 0), Margin = new Thickness(0, 0, 0, 6),
            Background = new LinearGradientBrush(Color.FromArgb(0xCC, CkGoldRule.R, CkGoldRule.G, CkGoldRule.B), Color.FromArgb(0x10, CkGoldRule.R, CkGoldRule.G, CkGoldRule.B), 0),
        });
        foreach (var e in entries) body.Children.Add(MenuRow(e));
        body.Children.Add(new Border { Height = 6 });
        card.PreviewKeyDown += (_, k) =>
        {
            if (k.Key == Key.Escape && _card != null) { k.Handled = true; _card.IsOpen = false; }
            else if (k.Key is Key.Down or Key.Up && Keyboard.FocusedElement == card)
            {
                k.Handled = true;
                var rows = body.Children.OfType<Border>().Where(b => b.Focusable).ToList();
                (k.Key == Key.Down ? rows.FirstOrDefault() : rows.LastOrDefault())?.Focus();
            }
        };
        return card;
    }

    /// <summary>One entry's row. An action closes the menu and then runs; a switch flips in place and the menu stays.</summary>
    FrameworkElement MenuRow(MenuEntry e)
    {
        if (e.Rule) return new Border { Height = 1, Margin = new Thickness(14, 6, 14, 6), Background = Ck(CkSlotLine, 0xCC) };
        if (e.Info)
        {
            var t = CkText(e.Text, CkFaint, 12); t.TextWrapping = TextWrapping.Wrap; t.TextTrimming = TextTrimming.None; t.Margin = new Thickness(18, 3, 18, 3);
            if (e.Tip != null) { t.ToolTip = e.Tip; ToolTipService.SetInitialShowDelay(t, 400); }
            return t;
        }
        bool live = e.Do != null || e.On != null;
        var g = new Grid();
        g.ColumnDefinitions.Add(new ColumnDefinition()); g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var words = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        var text = CkText(e.Text, live ? CkInk : CkFaint, 13.5);
        words.Children.Add(text);
        if (e.Sub.Length > 0) { var s = CkText(e.Sub, CkFaint, 11.5); s.TextWrapping = TextWrapping.Wrap; s.TextTrimming = TextTrimming.None; s.Margin = new Thickness(0, 2, 0, 0); words.Children.Add(s); }
        g.Children.Add(words);
        bool on = e.On == true;
        Border? sw = null;
        if (e.On != null) { sw = MenuSwitch(on); sw.Margin = new Thickness(14, 0, 0, 0); Grid.SetColumn(sw, 1); g.Children.Add(sw); }
        var row = new Border
        {
            Child = g, MinHeight = 36, Padding = new Thickness(18, 7, 18, 7), Margin = new Thickness(6, 0, 6, 0), CornerRadius = new CornerRadius(5),
            Background = Brushes.Transparent, Focusable = live, FocusVisualStyle = null, Cursor = live ? Cursors.Hand : null,
        };
        AutomationProperties.SetName(row, e.Text);
        if (e.Tip != null) { row.ToolTip = e.Tip; ToolTipService.SetInitialShowDelay(row, 600); ToolTipService.SetShowOnDisabled(row, true); }
        if (!live) return row;
        void Lit(bool lit) { row.Background = lit ? Ck(CkGold, 0x16) : Brushes.Transparent; text.Foreground = Ck(lit ? CkGold : CkInk); }
        row.MouseEnter += (_, _) => Lit(true);
        row.MouseLeave += (_, _) => Lit(row.IsKeyboardFocused);
        row.GotKeyboardFocus += (_, _) => Lit(true);
        row.LostKeyboardFocus += (_, _) => Lit(row.IsMouseOver);
        void Act()
        {
            if (e.On != null) { on = !on; StyleSwitch(sw!, on); e.Do?.Invoke(); return; }
            if (_card != null) _card.IsOpen = false;
            // after the menu has gone: a sheet opened from inside a closing popup would close with it
            Dispatcher.BeginInvoke(DispatcherPriority.Background, e.Do!);
        }
        row.MouseLeftButtonUp += (_, _) => Act();
        row.KeyDown += (_, k) => { if (k.Key is Key.Enter or Key.Space) { k.Handled = true; Act(); } };
        return row;
    }

    /// <summary>A small switch: a gold track with the knob on the right when on, a dark one with it on the left when off.</summary>
    static Border MenuSwitch(bool on)
    {
        var knob = new Border { Width = 12, Height = 12, CornerRadius = new CornerRadius(6), VerticalAlignment = VerticalAlignment.Center };
        var track = new Border { Width = 30, Height = 18, CornerRadius = new CornerRadius(9), BorderThickness = new Thickness(1), Padding = new Thickness(2), Child = knob, VerticalAlignment = VerticalAlignment.Center };
        StyleSwitch(track, on);
        return track;
    }

    static void StyleSwitch(Border track, bool on)
    {
        track.Background = on ? Ck(CkGold, 0x33) : Ck(CkSlot);
        track.BorderBrush = on ? Ck(CkGold, 0xAA) : Ck(CkFrame);
        if (track.Child is Border knob) { knob.Background = Ck(on ? CkGold : CkMuted); knob.HorizontalAlignment = on ? HorizontalAlignment.Right : HorizontalAlignment.Left; }
    }

    /// <summary>A card's foot of actions: gold words under a rule, the same as the links at the foot of an item's card.</summary>
    internal FrameworkElement CkActions(params (string Text, Action Do)[] actions)
    {
        var t = new TextBlock { FontSize = 12.5, TextWrapping = TextWrapping.Wrap };
        foreach (var (text, act) in actions)
        {
            if (t.Inlines.Count > 0) t.Inlines.Add(new Run("   ·   ") { Foreground = Ck(CkFaint) });
            var h = new Hyperlink(new Run(text)) { Foreground = Ck(CkGold), TextDecorations = null };
            h.Click += (_, _) => { if (_card != null) _card.IsOpen = false; Dispatcher.BeginInvoke(DispatcherPriority.Background, act); };
            t.Inlines.Add(h);
        }
        return new Border { Child = t, Margin = new Thickness(0, 12, 0, 0), Padding = new Thickness(20, 10, 20, 12), BorderThickness = new Thickness(0, 1, 0, 0), BorderBrush = Ck(CkSlotLine, 0xCC) };
    }

    /// <summary>
    /// A click on a slot pins its hover card beside it, with the slot's actions at its foot (what a right-click used to
    /// hide); a click anywhere else closes it.
    /// </summary>
    void PinOnClick(FrameworkElement tile, Func<FrameworkElement> build, params (string Text, Action Do)[] actions)
    {
        tile.Cursor = Cursors.Hand;
        tile.MouseLeftButtonUp += (_, _) =>
        {
            if (tile.ToolTip is ToolTip tt) tt.IsOpen = false;
            if (_card != null) _card.IsOpen = false;
            _card = new Popup { Child = PinnedCard(build, actions), StaysOpen = false, AllowsTransparency = true, PopupAnimation = PopupAnimation.Fade, Placement = PlacementMode.Mouse, PlacementTarget = tile };
            _card.IsOpen = true;
        };
    }

    /// <summary>The hover card, with the actions added at its foot.</summary>
    internal FrameworkElement PinnedCard(Func<FrameworkElement> build, (string Text, Action Do)[] actions)
    {
        var card = build();
        if (actions.Length > 0 && card is Border { Child: StackPanel body }) body.Children.Add(CkActions(actions));
        return card;
    }

    /* ---------------- self-tests and layout renders ---------------- */

    /// <summary>For options-test: every element in the window with a right-click menu (there should be none).</summary>
    internal List<string> RightClickMenusForTest()
    {
        var found = new List<string>();
        void Walk(DependencyObject d)
        {
            if (d is FrameworkElement fe && fe.ContextMenu != null) found.Add(fe.Name.Length > 0 ? fe.Name : fe.GetType().Name + (fe.Tag is string tag ? ":" + tag : ""));
            int n = d is Visual or System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetChildrenCount(d) : 0;
            for (int i = 0; i < n; i++) Walk(VisualTreeHelper.GetChild(d, i));
        }
        Walk(this);
        return found;
    }
}
