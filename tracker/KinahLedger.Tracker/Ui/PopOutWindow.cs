using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shell;
using System.Windows.Threading;
using KinahLedger.Session;

namespace KinahLedger.Ui;

/// <summary>
/// A pop-out window (Steven, 2026-11-18: "Yes, build pop-outs"): an overlay's drawing in a normal desktop window, for a second
/// screen beside a game in exclusive fullscreen, where no window can draw over the game. Modelled on the timers' pop-out
/// (Ui/BossWindow.cs): a window of its own, in the taskbar, not owned by the main window (so it stays when that is minimized
/// or in the tray), takes focus like any window, draggable from anywhere that isn't a control, and it remembers where it sat
/// and how wide it was (popouts.json), always opening on a screen. Its edges resize it: the drawing is scaled to the window's
/// width and the height follows it, so nothing is cut and no empty strip is left. A head in the app's skin: the title, "On
/// top" (keeps it above other windows; off by default), the window's own buttons, and a cross. Under it the overlay's own
/// drawing on a dark stage (the overlay looks are drawn for the game behind them). Unlike the overlays it never hides
/// because the game is covered, minimized or out of focus: it is a desktop window, not an overlay, and nothing is put into
/// the game.
/// </summary>
abstract class PopOutWindow : Window
{
    public const double HeadHeight = 36;
    /// <summary>Room round the drawing on the stage.</summary>
    public const double StagePad = 8;

    readonly Window _main;
    readonly PopOutPrefs.Spot _spot;
    readonly Action _save;
    readonly Border _stage;
    readonly Viewbox _fit = new() { Stretch = Stretch.Uniform, StretchDirection = StretchDirection.Both };
    readonly TextBlock _empty;
    readonly TextBlock _title, _flash;
    readonly string _titleText;
    readonly Button _onTop;
    readonly System.Windows.Shapes.Path _pinIcon;
    DispatcherTimer? _flashTimer;
    /// <summary>The head's own buttons (Share), before "On top".</summary>
    protected readonly StackPanel HeadButtons = new() { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };

    /// <summary>For the self-tests and renders: windows shown while it is set are parked far off every screen, never activated.</summary>
    internal static bool OffscreenForTest;

    protected PopOutWindow(Window main, PopOutPrefs.Spot spot, Action save, string title, string name, double defaultWidth, double minWidth)
    {
        _main = main; _spot = spot; _save = save; _titleText = title;
        Title = "Kinah Ledger · " + title;
        Icon = main.Icon;
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.CanResize;
        ShowInTaskbar = true;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.Manual;
        // the edges resize it (no caption of Windows' own: the head is drawn in the app's skin)
        WindowChrome.SetWindowChrome(this, new WindowChrome { CaptionHeight = 0, ResizeBorderThickness = new Thickness(6), GlassFrameThickness = new Thickness(0), CornerRadius = new CornerRadius(0), UseAeroCaptionButtons = false });
        MinWidth = minWidth;
        Width = Math.Max(minWidth, spot.W ?? defaultWidth);
        Topmost = spot.OnTop;
        UseLayoutRounding = true;
        SetResourceReference(BackgroundProperty, "Card");
        SetResourceReference(FontFamilyProperty, "BodyFont");
        System.Windows.Automation.AutomationProperties.SetName(this, name);

        // the head: a dot and the title (a picture copied is said in its place a moment), the window's buttons, On top, the cross
        var head = new Grid { Height = HeadHeight, Margin = new Thickness(12, 0, 6, 0), Background = Brushes.Transparent };
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var dot = new System.Windows.Shapes.Ellipse { Width = 7, Height = 7, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) };
        dot.SetResourceReference(System.Windows.Shapes.Shape.FillProperty, "Gold");
        head.Children.Add(dot);
        _title = new TextBlock { Text = title, FontSize = 13, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
        _title.SetResourceReference(TextBlock.ForegroundProperty, "Ink");
        _flash = new TextBlock { FontSize = 12.5, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis, Visibility = Visibility.Collapsed };
        var words = new Grid { Children = { _title, _flash } };
        Grid.SetColumn(words, 1); head.Children.Add(words);
        var right = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 0, 0) };
        right.Children.Add(HeadButtons);
        // Always on top: a pin, gold while on (a word and a switch left the title no room at the narrowest width)
        _pinIcon = new System.Windows.Shapes.Path
        {
            Data = Geometry.Parse("M4,1.5 L10,1.5 M5,1.5 L5,6 L2.5,8.5 L11.5,8.5 L9,6 L9,1.5 M7,8.5 L7,13"), StrokeThickness = 1.5, Width = 14, Height = 14,
            StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round, StrokeLineJoin = PenLineJoin.Round, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center,
        };
        _onTop = new Button { Content = _pinIcon, Width = 28, Height = 28, Margin = new Thickness(8, 0, 0, 0), ToolTip = L.T("Always on top") + "\n" + L.T("Keep above other windows") };
        _onTop.SetResourceReference(StyleProperty, "RoundBtn");
        System.Windows.Automation.AutomationProperties.SetName(_onTop, name + ": always on top");
        _onTop.Click += (_, _) => SetOnTop(!Topmost);
        ShowPin(spot.OnTop);
        right.Children.Add(_onTop);
        var close = new Button { Content = "✕", FontSize = 12, Width = 28, Height = 28, Margin = new Thickness(8, 0, 0, 0), ToolTip = L.T("Close (Esc)") };
        close.SetResourceReference(StyleProperty, "RoundBtn");
        System.Windows.Automation.AutomationProperties.SetName(close, "Close the " + name);
        close.Click += (_, _) => Close();
        CloseButton = close;
        right.Children.Add(close);
        Grid.SetColumn(right, 2); head.Children.Add(right);

        // the stage: the overlay's drawing, scaled to the width; a quiet line in its place when there is nothing to show
        _empty = new TextBlock { FontSize = 13, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(6, 10, 6, 12), Visibility = Visibility.Collapsed, Foreground = HudKit.Muted };
        _stage = new Border { Padding = new Thickness(StagePad), Child = new Grid { Children = { _fit, _empty } } };
        var root = new DockPanel { LastChildFill = true };
        DockPanel.SetDock(head, Dock.Top);
        root.Children.Add(head);
        root.Children.Add(_stage);
        var frame = new Border { BorderThickness = new Thickness(1), Child = root };
        frame.SetResourceReference(Border.BorderBrushProperty, "CardLine");
        frame.SetResourceReference(Border.BackgroundProperty, "Card");   // (the window's too: a picture of the content shows the head's ground)
        Content = frame;
        PaintStage();

        MouseLeftButtonDown += (_, e) =>
        {
            if (CraftWindow.Interactive(e.OriginalSource as DependencyObject)) return;
            try { DragMove(); } catch (InvalidOperationException) { }
            Remember();
        };
        PreviewKeyDown += (_, e) => { if (e.Key == Key.Escape) { Close(); e.Handled = true; } };
        Closing += (_, _) => Remember();
        SourceInitialized += (_, _) => HwndSource.FromHwnd(new WindowInteropHelper(this).Handle)?.AddHook(SizeHook);
        // the drawing never taller than the screen it is on (it scales down to fit, then)
        _fit.MaxHeight = Math.Max(160, ScreenArea.Work.Height - HeadHeight - 2 * StagePad - 24);
    }

    /// <summary>The cross in the head (the self-test presses it).</summary>
    internal Button CloseButton { get; }

    /// <summary>The drawing on the stage (replaced only when it is another element).</summary>
    protected void SetFace(FrameworkElement? face, string emptyText = "")
    {
        PaintStage();
        if (face == null)
        {
            _fit.Child = null; _fit.Visibility = Visibility.Collapsed;
            _empty.Text = emptyText; _empty.Visibility = Visibility.Visible;
            return;
        }
        _empty.Visibility = Visibility.Collapsed; _fit.Visibility = Visibility.Visible;
        if (!ReferenceEquals(_fit.Child, face)) _fit.Child = face;
    }

    /// <summary>The face on the stage now, or null with the quiet line instead (tests and renders).</summary>
    internal FrameworkElement? FaceForTest => _fit.Visibility == Visibility.Visible ? _fit.Child as FrameworkElement : null;
    [System.Runtime.InteropServices.DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] static extern IntPtr GetWindowLongPtr(IntPtr hWnd, int index);
    /// <summary>For the self-test: the window's extended style (no never-activate, tool-window or click-through bits: a desktop window).</summary>
    internal long ExStyleForTest { get { var h = new WindowInteropHelper(this).Handle; return h == IntPtr.Zero ? 0 : GetWindowLongPtr(h, -20).ToInt64(); } }
    internal string EmptyTextForTest => _empty.Visibility == Visibility.Visible ? _empty.Text : "";
    internal bool OnTopForTest { get => Topmost; set { if (value != Topmost) _onTop.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent)); } }
    /// <summary>The drawing's scale on the stage now (1 at its own width).</summary>
    internal double ScaleForTest => _fit.Child is FrameworkElement f && f.ActualWidth > 0 ? _fit.ActualWidth / f.ActualWidth : 0;

    /// <summary>The stage is dark in every skin: the night ground of a dark one, a fixed near-black under a light one.</summary>
    void PaintStage()
    {
        if (Skins.IsDark(Skins.Current)) _stage.SetResourceReference(Border.BackgroundProperty, "Bg");
        else _stage.Background = StageDark;
    }
    static readonly SolidColorBrush StageDark = Frozen(Color.FromRgb(0x0C, 0x0D, 0x11));
    static SolidColorBrush Frozen(Color c) { var b = new SolidColorBrush(c); b.Freeze(); return b; }

    /// <summary>The pin gold with a gold edge while on, quiet while off.</summary>
    void ShowPin(bool on)
    {
        _pinIcon.SetResourceReference(System.Windows.Shapes.Shape.StrokeProperty, on ? "Gold" : "Muted");
        if (on) _onTop.SetResourceReference(BorderBrushProperty, "Gold"); else _onTop.BorderBrush = Brushes.Transparent;
    }

    void SetOnTop(bool on)
    {
        Topmost = on;
        ShowPin(on);
        _spot.OnTop = on;
        _save();
    }

    /// <summary>"Picture copied" (or why not) in the title's place for a few seconds.</summary>
    public void Flash(string text, bool good)
    {
        _flash.Text = text;
        _flash.SetResourceReference(TextBlock.ForegroundProperty, good ? "Good" : "Warn");
        _flash.Visibility = Visibility.Visible; _title.Visibility = Visibility.Hidden;
        _flashTimer ??= new DispatcherTimer { Interval = TimeSpan.FromSeconds(2.6) };
        _flashTimer.Stop();
        _flashTimer.Tick -= FlashDone; _flashTimer.Tick += FlashDone;
        _flashTimer.Start();
    }
    void FlashDone(object? sender, EventArgs e) { _flashTimer?.Stop(); _flash.Visibility = Visibility.Collapsed; _title.Visibility = Visibility.Visible; }
    internal string FlashForTest => _flash.Visibility == Visibility.Visible ? _flash.Text : "";

    const int WM_EXITSIZEMOVE = 0x0232;

    /// <summary>An edge let go of: the width kept, and the height follows the drawing again (a drag sets it to Manual).</summary>
    IntPtr SizeHook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_EXITSIZEMOVE)
        {
            if (SizeToContent != SizeToContent.Height) SizeToContent = SizeToContent.Height;
            Remember();
        }
        return IntPtr.Zero;
    }

    void Remember()
    {
        if (!IsVisible || OffscreenForTest || Left < SystemParameters.VirtualScreenLeft - 5000) return;   // never a render's spot
        _spot.X = Left; _spot.Y = Top; _spot.W = ActualWidth;
        _save();
    }

    /// <summary>For the self-test: the window as if dragged and resized to this spot, then kept.</summary>
    internal void RememberForTest(double x, double y, double w) { _spot.X = x; _spot.Y = y; _spot.W = w; _save(); }

    /// <summary>Shown where it sat last, else beside the main window (or on a second screen when there is one), on a screen; brought forward when already open.</summary>
    public void Open()
    {
        if (IsVisible) { if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal; Activate(); return; }
        if (OffscreenForTest) { Left = -12000; Top = -12000; ShowActivated = false; Show(); return; }
        var p = SpotFor(_spot, _main, Width, 220);
        Left = p.X; Top = p.Y;
        Show();
    }

    /// <summary>
    /// Where a pop-out opens: its kept spot, else on a screen other than the main one when there is one (the game is usually on
    /// the main screen), else beside the main window; always pulled onto the screens (a monitor unplugged since).
    /// </summary>
    internal static Point SpotFor(PopOutPrefs.Spot spot, Window? main, double w, double h)
    {
        double vl = SystemParameters.VirtualScreenLeft, vt = SystemParameters.VirtualScreenTop, vw = SystemParameters.VirtualScreenWidth, vh = SystemParameters.VirtualScreenHeight;
        double left, top;
        if (spot.X is { } x && spot.Y is { } y) { left = x; top = y; }
        else if (SecondScreen() is { } s) { left = s.X + 40; top = s.Y + 40; }
        else if (main != null && double.IsFinite(main.Left) && main.ActualWidth > 0 && main.Left > vl - 5000)
        {
            left = main.Left + main.ActualWidth + 6;
            if (left + w > vl + vw) left = main.Left - w - 6;
            top = main.Top + 56;
        }
        else { left = SystemParameters.WorkArea.Right - w - 24; top = SystemParameters.WorkArea.Top + 80; }
        left = Math.Clamp(left, vl, vl + vw - Math.Min(w, 200));
        top = Math.Clamp(top, vt, vt + vh - Math.Min(h, 120));
        return new Point(left, top);
    }

    /// <summary>The top-left of a screen other than the main one, in WPF units; null with one screen.</summary>
    static Point? SecondScreen()
    {
        try
        {
            var screens = System.Windows.Forms.Screen.AllScreens;
            if (screens.Length < 2) return null;
            var other = screens.FirstOrDefault(s => !s.Primary);
            if (other == null) return null;
            var prim = System.Windows.Forms.Screen.PrimaryScreen;
            double k = prim != null && prim.Bounds.Width > 0 ? SystemParameters.PrimaryScreenWidth / prim.Bounds.Width : 1;
            return new Point(other.WorkingArea.Left * k, other.WorkingArea.Top * k);
        }
        catch (Exception) { return null; }
    }
}
