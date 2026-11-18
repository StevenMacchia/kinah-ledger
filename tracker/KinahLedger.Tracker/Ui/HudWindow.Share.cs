using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;

namespace KinahLedger.Ui;

/// <summary>
/// Share on the Kinah overlay's meter (Steven, 2026-11-18: "how am i supposed to copy this ... it should be on the overlay"):
/// with the Parser inside the Kinah overlay there was no Share at all, only on the Parser's own window. The same button sits
/// under the overlay, at its right, while the pointer is over it and a fight is on it. With clicks going through (the panel
/// locked, as it is for most), the window takes the pointer only over the button itself, so a click there copies the picture
/// and everywhere else still goes to the game; the same rule as the Parser's own window (Ui/ParserOverlay.cs WatchGrab).
/// </summary>
public sealed partial class HudWindow
{
    /// <summary>Share under the meter was pressed: the window copies the fight's picture (MainWindow.ShareMeter).</summary>
    public event Action? ShareAsked;

    Button? _share;
    Border? _shareRow;
    TextBlock? _shareSaid;
    System.Windows.Threading.DispatcherTimer? _shareWatch;
    /// <summary>The window has taken the pointer for the button (clicks otherwise go through).</summary>
    bool _shareGrab;
    DateTime _shareSaidUntil = DateTime.MinValue;

    [DllImport("user32.dll")] static extern bool GetCursorPos(out POINT p);
    [StructLayout(LayoutKind.Sequential)] struct POINT { public int X, Y; }

    /// <summary>For meter-load: the watch's ticks and its time on the window's thread (TimeSpan ticks), every overlay together.</summary>
    internal static long ShareWatchTicks, ShareWatchTime;

    /// <summary>A fight on the overlay now: the Parser's section, or a look's own meter.</summary>
    bool FightOnOverlay => ParserShown || (_look is HudStackLook sl && sl.FightFrame != null);

    /// <summary>Called at the end of the constructor: the row under the overlay, and the watch that shows it.</summary>
    void AddShare()
    {
        if (_still || Content is not StackPanel stack) return;
        _share = ParserOverlay.ShareButton();
        _share.Visibility = Visibility.Visible;
        _share.Click += (_, _) => ShareAsked?.Invoke();
        _shareSaid = new TextBlock { FontSize = 11.5, FontWeight = FontWeights.SemiBold, Padding = new Thickness(9, 2, 10, 3), Background = HudKit.B(HudKit.PanelC, 0.92), Visibility = Visibility.Collapsed };
        _shareRow = new Border
        {
            HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 3, 0, 0), Visibility = Visibility.Collapsed, Tag = "hud-share",
            Child = new Grid { Children = { _share, _shareSaid } },
        };
        stack.Children.Add(_shareRow);
        _shareWatch = new System.Windows.Threading.DispatcherTimer(System.Windows.Threading.DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(80) };
        _shareWatch.Tick += (_, _) => { long t0 = System.Diagnostics.Stopwatch.GetTimestamp(); WatchShare(); ShareWatchTicks++; ShareWatchTime += System.Diagnostics.Stopwatch.GetElapsedTime(t0).Ticks; };
        IsVisibleChanged += (_, _) => { if (IsVisible) _shareWatch.Start(); else { _shareWatch.Stop(); HideShare(); } };
    }

    /// <summary>"Picture copied" (or why not) where Share was, for a few seconds.</summary>
    public void FlashShare(string text, bool good)
    {
        if (_shareSaid == null || _share == null || _shareRow == null) return;
        _shareSaid.Text = text;
        _shareSaid.Foreground = good ? HudKit.Mint : HudKit.Warn;
        _shareSaidUntil = DateTime.UtcNow.AddSeconds(2.5);
        _shareSaid.Visibility = Visibility.Visible;
        _share.Visibility = Visibility.Hidden;
        _shareRow.Visibility = Visibility.Visible;
    }

    void HideShare()
    {
        if (_shareRow != null) _shareRow.Visibility = Visibility.Collapsed;
        if (_shareGrab) { _shareGrab = false; var h = new WindowInteropHelper(this).Handle; if (h != IntPtr.Zero) Native.SetClickThrough(h, _unlock.Length == 0); }
    }

    /// <summary>
    /// Every 80 ms while shown: the pointer over the overlay with a fight on it shows Share; over the button itself, with clicks
    /// going through, the window takes the pointer so the click lands; anywhere else the game keeps it.
    /// </summary>
    void WatchShare()
    {
        if (_shareRow == null || _share == null || _shareSaid == null) return;
        var h = new WindowInteropHelper(this).Handle;
        if (h == IntPtr.Zero || !GetCursorPos(out var c) || !GetWindowRect(h, out var r)) return;
        bool saying = _shareSaidUntil > DateTime.UtcNow;
        if (!saying && _shareSaid.Visibility == Visibility.Visible) { _shareSaid.Visibility = Visibility.Collapsed; _share.Visibility = Visibility.Visible; }
        bool over = c.X >= r.Left && c.X < r.Right && c.Y >= r.Top && c.Y < r.Bottom;
        bool want = saying || (over && FightOnOverlay && ShareShown);
        if (!want) { HideShare(); return; }
        if (_shareRow.Visibility != Visibility.Visible) { _shareRow.Visibility = Visibility.Visible; UpdateLayout(); }
        bool onButton = false;
        if (!saying && _share.ActualWidth > 0 && GetWindowRect(h, out r))
        {
            var dpi = VisualTreeHelper.GetDpi(this);
            var tl = _share.TransformToAncestor(this).Transform(new Point(0, 0));
            var br = _share.TransformToAncestor(this).Transform(new Point(_share.ActualWidth, _share.ActualHeight));
            int sx = r.Left + (int)Math.Round(tl.X * dpi.DpiScaleX), sy = r.Top + (int)Math.Round(tl.Y * dpi.DpiScaleY);
            int ex = r.Left + (int)Math.Round(br.X * dpi.DpiScaleX), ey = r.Top + (int)Math.Round(br.Y * dpi.DpiScaleY);
            onButton = c.X >= sx - 2 && c.X < ex + 2 && c.Y >= sy - 2 && c.Y < ey + 2;
        }
        // clicks go through while locked: only over the button does the window take the pointer
        if (_unlock.Length == 0 && onButton != _shareGrab)
        {
            _shareGrab = onButton;
            Native.SetClickThrough(h, !onButton);
        }
    }

    /// <summary>The window has a fight to share (MainWindow: not a dungeon's ready card).</summary>
    public bool ShareShown { get; set; } = true;

    /// <summary>For the options self-test: Share's row as the watch would leave it, and a press on it.</summary>
    internal bool ShareRowShownForTest => _shareRow?.Visibility == Visibility.Visible;
    internal bool HasShareForTest => _share != null;
    internal void PressShareForTest() => ShareAsked?.Invoke();
}
