using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace KinahLedger.Ui;

/// <summary>
/// The timers' window (events and world bosses, Ui/MainWindow.Bosses.cs), in the recipe window's way (Ui/CraftWindow.cs): a card that
/// drags from anywhere that isn't a control, to sit beside the game, and stays open while the player plays. Esc or the cross
/// closes it; it remembers where it was put until the app closes, and minimizes and closes with the main window (its owner).
/// The card is redrawn in place each second, keeping where the list was scrolled to.
/// </summary>
sealed class BossWindow : Window
{
    static Point? _at;

    readonly Grid _host = new();
    readonly ScrollViewer _scroll;

    public BossWindow(Window owner)
    {
        Owner = owner;
        Title = "Timers";
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ShowInTaskbar = false;
        ResizeMode = ResizeMode.NoResize;
        SizeToContent = SizeToContent.WidthAndHeight;
        Topmost = owner.Topmost;
        Content = _host;
        _host.Cursor = Cursors.SizeAll;
        _scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Focusable = false, MaxHeight = Math.Min(720, SystemParameters.WorkArea.Height - 24) };
        _host.Children.Add(_scroll);
        var close = new Button { Content = "✕", FontSize = 13, Width = 28, Height = 28, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 19, 19, 0), ToolTip = "Close (Esc)" };
        close.SetResourceReference(StyleProperty, "RoundBtn");
        System.Windows.Automation.AutomationProperties.SetName(close, "Close the timers");
        close.Click += (_, _) => Close();
        _host.Children.Add(close);
        MouseLeftButtonDown += (_, e) =>
        {
            if (CraftWindow.Interactive(e.OriginalSource as DependencyObject)) return;
            try { DragMove(); } catch (InvalidOperationException) { }
            _at = new Point(Left, Top);
        };
        PreviewKeyDown += (_, e) => { if (e.Key == Key.Escape) { Close(); e.Handled = true; } };
    }

    /// <summary>Shows <paramref name="card"/>: in place when open (the scroll kept), else beside the main window or where it was last put.</summary>
    /// <param name="offscreen">For a render (ui-shot): shown far off the screens.</param>
    public void ShowCard(Border card, bool offscreen = false)
    {
        double y = _scroll.VerticalOffset;
        _scroll.Content = card;
        if (IsVisible) { _scroll.UpdateLayout(); _scroll.ScrollToVerticalOffset(y); return; }
        if (offscreen) { _scroll.MaxHeight = double.PositiveInfinity; WindowStartupLocation = WindowStartupLocation.Manual; Left = -12000; Top = -12000; ShowActivated = false; Show(); return; }
        card.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        double w = card.DesiredSize.Width, h = Math.Min(card.DesiredSize.Height, _scroll.MaxHeight);
        var o = Owner;
        double left, top;
        if (_at is { } p) { left = p.X; top = p.Y; }
        else
        {
            left = o.Left + o.ActualWidth + 6;
            if (left + w > SystemParameters.VirtualScreenLeft + SystemParameters.VirtualScreenWidth) left = o.Left - w - 6;
            top = o.Top + 56;
        }
        left = Math.Clamp(left, SystemParameters.VirtualScreenLeft, SystemParameters.VirtualScreenLeft + SystemParameters.VirtualScreenWidth - Math.Min(w, 200));
        top = Math.Clamp(top, SystemParameters.VirtualScreenTop, SystemParameters.VirtualScreenTop + SystemParameters.VirtualScreenHeight - Math.Min(h, 120));
        WindowStartupLocation = WindowStartupLocation.Manual;
        Left = left; Top = top;
        Show();
    }
}
