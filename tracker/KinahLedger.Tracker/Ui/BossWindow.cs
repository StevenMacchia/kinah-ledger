using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using KinahLedger.Session;

namespace KinahLedger.Ui;

/// <summary>
/// The timers' pop-out window (events and world bosses, Ui/MainWindow.Bosses.cs): the full list, to keep open beside the
/// game or on a second screen (Steven, 2026-11-14). A window of its own: in the taskbar, not minimized with the main window,
/// resizable from its corner grip and draggable from anywhere that isn't a control. It remembers where it was and its size
/// (timers.json), and always opens on a screen. The card scrolls inside it and is redrawn in place each second, keeping where
/// the list was scrolled to. Esc or the cross closes it; it closes with the app.
/// </summary>
sealed class BossWindow : Window
{
    /// <summary>The first size: 420 × 640, less on a short screen.</summary>
    public static double DefaultHeight => Math.Min(640, SystemParameters.WorkArea.Height - 24);
    public const double DefaultWidth = 420;

    readonly Grid _host = new();
    readonly ScrollViewer _scroll;
    readonly Window _main;
    readonly TimerPrefs _prefs;

    /// <summary>The card's width for the window's width now (the card has a 10 px margin round it; the scroll bar beside it).</summary>
    public double CardWidth => Math.Max(300, (double.IsNaN(Width) ? DefaultWidth : Width) - 20 - SystemParameters.VerticalScrollBarWidth - 4);

    public BossWindow(Window main, TimerPrefs prefs)
    {
        _main = main;
        _prefs = prefs;
        Title = "Kinah Ledger timers";
        Icon = main.Icon;
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ShowInTaskbar = true;
        ResizeMode = ResizeMode.CanResizeWithGrip;
        SizeToContent = SizeToContent.Manual;
        MinWidth = 340; MinHeight = 240;
        Width = Math.Max(MinWidth, prefs.WinW ?? DefaultWidth);
        Height = Math.Max(MinHeight, prefs.WinH ?? DefaultHeight);
        Content = _host;
        _host.Cursor = Cursors.SizeAll;
        _scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Focusable = false };
        _host.Children.Add(_scroll);
        var close = new Button { Content = "✕", FontSize = 13, Width = 28, Height = 28, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 19, 33, 0), ToolTip = "Close (Esc)" };
        close.SetResourceReference(StyleProperty, "RoundBtn");
        System.Windows.Automation.AutomationProperties.SetName(close, "Close the timers");
        close.Click += (_, _) => Close();
        _host.Children.Add(close);
        MouseLeftButtonDown += (_, e) =>
        {
            if (CraftWindow.Interactive(e.OriginalSource as DependencyObject)) return;
            try { DragMove(); } catch (InvalidOperationException) { }
            Remember();
        };
        PreviewKeyDown += (_, e) => { if (e.Key == Key.Escape) { Close(); e.Handled = true; } };
        Closing += (_, _) => Remember();
    }

    /// <summary>Raised when the size changes, for the card to be drawn at the new width.</summary>
    public event Action? Resized;

    protected override void OnRenderSizeChanged(SizeChangedInfo info)
    {
        base.OnRenderSizeChanged(info);
        if (info.WidthChanged && IsVisible) Resized?.Invoke();
    }

    void Remember()
    {
        if (!IsVisible || Left < SystemParameters.VirtualScreenLeft - 5000) return;   // never a render's spot
        _prefs.WinX = Left; _prefs.WinY = Top; _prefs.WinW = ActualWidth; _prefs.WinH = ActualHeight;
        _prefs.Save();
    }

    /// <summary>Shows <paramref name="card"/>: in place when open (the scroll kept), else where it was last put or beside the main window, on a screen.</summary>
    /// <param name="offscreen">For a render (ui-shot): shown far off the screens.</param>
    public void ShowCard(Border card, bool offscreen = false)
    {
        double y = _scroll.VerticalOffset;
        _scroll.Content = card;
        if (IsVisible) { _scroll.UpdateLayout(); _scroll.ScrollToVerticalOffset(y); return; }
        WindowStartupLocation = WindowStartupLocation.Manual;
        if (offscreen) { Left = -12000; Top = -12000; ShowActivated = false; Show(); return; }
        double w = Width, h = Height, left, top;
        if (_prefs.WinX is { } x && _prefs.WinY is { } yy) { left = x; top = yy; }
        else
        {
            left = _main.Left + _main.ActualWidth + 6;
            if (left + w > SystemParameters.VirtualScreenLeft + SystemParameters.VirtualScreenWidth) left = _main.Left - w - 6;
            top = _main.Top + 56;
        }
        // kept on a screen: a spot from a screen that is gone (a second monitor unplugged) comes back
        left = Math.Clamp(left, SystemParameters.VirtualScreenLeft, SystemParameters.VirtualScreenLeft + SystemParameters.VirtualScreenWidth - Math.Min(w, 200));
        top = Math.Clamp(top, SystemParameters.VirtualScreenTop, SystemParameters.VirtualScreenTop + SystemParameters.VirtualScreenHeight - Math.Min(h, 120));
        Left = left; Top = top;
        Show();
    }
}
