using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;
using System.Windows.Media;

namespace KinahLedger.Ui;

/// <summary>
/// Share on the Kinah overlay's meter (Steven, 2026-11-18: "it should be on the overlay"; then "THE SHARE BUTTON FLOATS AND
/// BOUNCES AROUND IT SHOULD BE directly CONNECTED TO THE DPS METER AND EASY TO CLICK ON MOUSE OVER").
/// A tab fixed to the meter's top-right edge, in a small window of its own (a Popup): showing it never changes the overlay's
/// size, so nothing moves, and it sits exactly on the meter's edge wherever the meter is. It shows while the pointer is over
/// the meter or the tab, and the tab always takes clicks (never focus), click-through or not, so a click there copies the
/// picture and everywhere else still goes to the game.
/// </summary>
public sealed partial class HudWindow
{
    /// <summary>Share on the meter was pressed: the window copies the fight's picture (MainWindow.ShareMeter).</summary>
    public event Action? ShareAsked;

    Popup? _sharePop;
    Button? _share;
    TextBlock? _shareSaid;
    System.Windows.Threading.DispatcherTimer? _shareWatch;
    DateTime _shareSaidUntil = DateTime.MinValue;
    /// <summary>The pointer last over the meter or the tab: the tab stays a moment after it leaves, so it can be reached.</summary>
    DateTime _shareSeen = DateTime.MinValue;

    [DllImport("user32.dll")] static extern bool GetCursorPos(out POINT p);
    [StructLayout(LayoutKind.Sequential)] struct POINT { public int X, Y; }

    /// <summary>The fight on the overlay now (the Parser's section, or a look's own meter), or null.</summary>
    FrameworkElement? FightElement => ParserShown ? _parserSlot : _look is HudStackLook sl ? sl.FightFrame : null;

    /// <summary>Called at the end of the constructor: the tab, and the watch that shows it.</summary>
    void AddShare()
    {
        if (_still) return;
        _share = ParserOverlay.ShareButton();
        _share.Visibility = Visibility.Visible;
        _share.Padding = new Thickness(0);
        _share.Click += (_, _) => ShareAsked?.Invoke();
        // the tab: the button's own pill with a few pixels of grip round it, so it is easy to land on
        _shareSaid = new TextBlock { FontSize = 11.5, FontWeight = FontWeights.SemiBold, Padding = new Thickness(9, 2, 10, 3), Background = HudKit.B(HudKit.PanelC, 0.92), Visibility = Visibility.Collapsed };
        var tab = new Border
        {
            Padding = new Thickness(4, 4, 4, 2), Background = new SolidColorBrush(Color.FromArgb(1, 0, 0, 0)),   // alpha 1: takes the pointer, never seen
            Child = new Grid { Children = { _share, _shareSaid } }, Tag = "hud-share",
        };
        _sharePop = new Popup
        {
            Child = tab, AllowsTransparency = true, StaysOpen = true, Focusable = false, Placement = PlacementMode.Custom,
            CustomPopupPlacementCallback = (popup, target, _) =>
                // right-aligned on the meter, its foot just over the meter's top edge: it reads as part of it
                new[] { new CustomPopupPlacement(new Point(target.Width - popup.Width, -popup.Height + 3), PopupPrimaryAxis.None) },
        };
        // its own small window never takes focus and always takes clicks (the overlay's style without click-through)
        _sharePop.Opened += (_, _) =>
        {
            if (PresentationSource.FromVisual(tab) is HwndSource src) Native.SetClickThrough(src.Handle, false);
        };
        _shareWatch = new System.Windows.Threading.DispatcherTimer(System.Windows.Threading.DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(80) };
        _shareWatch.Tick += (_, _) => WatchShare();
        IsVisibleChanged += (_, _) => { if (IsVisible) _shareWatch.Start(); else { _shareWatch.Stop(); HideShare(); } };
        Closed += (_, _) => { _shareWatch.Stop(); HideShare(); };
    }

    /// <summary>"Picture copied" (or why not) on the tab for a few seconds.</summary>
    public void FlashShare(string text, bool good)
    {
        if (_shareSaid == null || _share == null) return;
        _shareSaid.Text = text;
        _shareSaid.Foreground = good ? HudKit.Mint : HudKit.Warn;
        _shareSaidUntil = DateTime.UtcNow.AddSeconds(2.5);
        _shareSaid.Visibility = Visibility.Visible;
        _share.Visibility = Visibility.Hidden;
        _shareSeen = DateTime.UtcNow;
        if (FightElement is { } f) ShowShareOn(f);
    }

    void ShowShareOn(FrameworkElement target)
    {
        if (_sharePop == null) return;
        // never for a meter parked off every screen (a self-test's, a render's): Windows would pull the tab onto the screen
        try { if (PresentationSource.FromVisual(target) == null || target.PointToScreen(new Point(0, 0)).X < -5000) return; }
        catch (InvalidOperationException) { return; }
        if (!ReferenceEquals(_sharePop.PlacementTarget, target)) { _sharePop.IsOpen = false; _sharePop.PlacementTarget = target; }
        if (!_sharePop.IsOpen) _sharePop.IsOpen = true;
        else { _sharePop.HorizontalOffset += 0.01; _sharePop.HorizontalOffset -= 0.01; }   // follows the meter as it moves or grows
    }

    void HideShare()
    {
        if (_sharePop is { IsOpen: true }) _sharePop.IsOpen = false;
    }

    /// <summary>Every 80 ms while shown: the pointer over the meter, or over the tab itself, keeps the tab up.</summary>
    void WatchShare()
    {
        if (_sharePop == null || _share == null || _shareSaid == null) return;
        bool saying = _shareSaidUntil > DateTime.UtcNow;
        if (!saying && _shareSaid.Visibility == Visibility.Visible) { _shareSaid.Visibility = Visibility.Collapsed; _share.Visibility = Visibility.Visible; }
        var fight = FightElement;
        if (fight == null || !fight.IsVisible || !ShareShown) { if (!saying) HideShare(); return; }
        if (!GetCursorPos(out var c)) return;
        bool over = Within(fight, c) || (_sharePop.IsOpen && _sharePop.Child is FrameworkElement tab && tab.IsVisible && Within(tab, c));
        if (over) _shareSeen = DateTime.UtcNow;
        // a moment's grace after the pointer leaves, so crossing from the meter onto the tab never drops it
        if (saying || (DateTime.UtcNow - _shareSeen).TotalMilliseconds < 400) ShowShareOn(fight);
        else HideShare();
    }

    /// <summary>Whether the screen point (pixels) is on <paramref name="e"/>, a few pixels of slack round it.</summary>
    static bool Within(FrameworkElement e, POINT c)
    {
        try
        {
            if (e.ActualWidth <= 0 || PresentationSource.FromVisual(e) == null) return false;
            var tl = e.PointToScreen(new Point(0, 0));
            var br = e.PointToScreen(new Point(e.ActualWidth, e.ActualHeight));
            return c.X >= Math.Min(tl.X, br.X) - 4 && c.X <= Math.Max(tl.X, br.X) + 4 && c.Y >= Math.Min(tl.Y, br.Y) - 4 && c.Y <= Math.Max(tl.Y, br.Y) + 4;
        }
        catch (InvalidOperationException) { return false; }
    }

    /// <summary>The window has a fight to share (MainWindow: not a dungeon's ready card).</summary>
    public bool ShareShown { get; set; } = true;

    /// <summary>For the options self-test: the tab as the watch would leave it, and a press on it.</summary>
    internal bool ShareRowShownForTest => _sharePop?.IsOpen == true;
    internal bool HasShareForTest => _share != null;
    internal void PressShareForTest() => ShareAsked?.Invoke();
}
