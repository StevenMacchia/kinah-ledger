using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;

namespace KinahLedger.Ui;

/// <summary>
/// A recipe's card in a window of its own (2026-11-10, Steven: "in the crafting window you should be able to click on
/// ANYTHING and it should open a window in the app, like a popup you can drag somewhere, that tells you how to craft it,
/// price, market insights"). Any item on the Crafting page opens it: Next up, Just crafted, the recipe cards, Best crafts
/// today, the Full view table. It holds the same card the click used to pop up at the mouse (MainWindow.BuildCraftCard,
/// with its MARKET section), stays open while the player looks around, and drags from anywhere that isn't a control, to
/// sit beside the game. One window: the next item replaces the card in place. Esc or the cross closes it; it remembers
/// where it was put until the app closes, and minimizes and closes with the main window (its owner).
/// </summary>
sealed class CraftWindow : Window
{
    /// <summary>Where the player last put it, this run of the app.</summary>
    static Point? _at;

    readonly Grid _host = new();

    /// <summary>The item the card is for.</summary>
    public string Output { get; private set; } = "";

    public CraftWindow(Window owner)
    {
        Owner = owner;
        Title = "Recipe";
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ShowInTaskbar = false;
        ResizeMode = ResizeMode.NoResize;
        SizeToContent = SizeToContent.WidthAndHeight;
        Topmost = owner.Topmost;
        Content = _host;
        _host.Cursor = Cursors.SizeAll;   // it drags from anywhere that isn't a control; controls keep their own cursor
        // drag from anywhere that isn't a control: the band, the figures, the empty room between the rows
        MouseLeftButtonDown += (_, e) =>
        {
            if (Interactive(e.OriginalSource as DependencyObject)) return;
            try { DragMove(); } catch (InvalidOperationException) { }
            _at = new Point(Left, Top);
        };
        PreviewKeyDown += (_, e) => { if (e.Key == Key.Escape) { Close(); e.Handled = true; } };
    }

    /// <summary>Shows <paramref name="card"/> for <paramref name="output"/>: in place when the window is open, else beside the main window (or where it was last put).</summary>
    /// <param name="offscreen">For a render (ui-shot): shown far off the screens, never where the player would see it.</param>
    public void ShowCard(string output, Border card, bool offscreen = false)
    {
        Output = output;
        _host.Children.Clear();
        var scroll = new ScrollViewer { Content = card, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Focusable = false, MaxHeight = SystemParameters.WorkArea.Height - 24 };
        _host.Children.Add(scroll);
        var close = new Button { Content = "✕", FontSize = 13, Width = 28, Height = 28, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, card.Margin.Top + 9, card.Margin.Right + 9, 0), ToolTip = "Close (Esc)" };
        close.SetResourceReference(StyleProperty, "RoundBtn");
        AutomationPropertiesName(close, "Close the recipe window");
        close.Click += (_, _) => Close();
        _host.Children.Add(close);
        if (IsVisible) { Activate(); return; }
        if (offscreen) { WindowStartupLocation = WindowStartupLocation.Manual; Left = -12000; Top = -12000; ShowActivated = false; Show(); return; }
        Place(card);
        Show();
    }

    /// <summary>The first spot: where it was last put, else right of the main window, else over its right side; always on a screen.</summary>
    void Place(Border card)
    {
        card.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        double w = card.DesiredSize.Width, h = Math.Min(card.DesiredSize.Height, SystemParameters.WorkArea.Height - 24);
        var o = Owner;
        double left, top;
        if (_at is { } p) { left = p.X; top = p.Y; }
        else
        {
            left = o.Left + o.ActualWidth + 6;
            if (left + w > SystemParameters.VirtualScreenLeft + SystemParameters.VirtualScreenWidth) left = o.Left + o.ActualWidth - w - 24;
            top = o.Top + 56;
        }
        left = Math.Clamp(left, SystemParameters.VirtualScreenLeft, SystemParameters.VirtualScreenLeft + SystemParameters.VirtualScreenWidth - Math.Min(w, 200));
        top = Math.Clamp(top, SystemParameters.VirtualScreenTop, SystemParameters.VirtualScreenTop + SystemParameters.VirtualScreenHeight - Math.Min(h, 120));
        WindowStartupLocation = WindowStartupLocation.Manual;
        Left = left; Top = top;
    }

    /// <summary>A control the press belongs to (a text box, a button, a tick box, a link, the scroll bar): no drag from there.</summary>
    static bool Interactive(DependencyObject? d)
    {
        for (; d != null; d = d is Visual or System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetParent(d) ?? LogicalTreeHelper.GetParent(d) : LogicalTreeHelper.GetParent(d))
            if (d is TextBoxBase or ButtonBase or Hyperlink or ScrollBar or Thumb or ComboBox) return true;
        return false;
    }

    static void AutomationPropertiesName(DependencyObject d, string name) => System.Windows.Automation.AutomationProperties.SetName(d, name);
}
