using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace KinahLedger.Ui;

/// <summary>
/// A card opened by a click (an item's card, a menu, a goal sheet, a report, the recap), drawn on a layer inside the
/// tracker's window and kept within its edges, so it never sits over the game (Steven, 2026-11-06: "I don't want there to
/// be a popup box when clicking on things; it can pop up and appear in the box but never overlay the game"). A WPF Popup
/// is a window of its own and hung past the tracker's edge onto the game. The members are the Popup's that the window
/// used (Child, IsOpen, the placement and its offsets, StaysOpen, Opened and Closed), so each card opens as before.
/// A card taller than the window scrolls inside it. A click outside it, the wheel outside it, Esc, the window losing
/// focus or changing size closes it, unless StaysOpen.
/// </summary>
public sealed class InCard
{
    /// <summary>The layer the cards are drawn on: over the window's content, as big as it (MainWindow sets it).</summary>
    public static Canvas? Layer { get; set; }
    /// <summary>The card open now, if any: one at a time, as the window kept one Popup.</summary>
    public static InCard? Current { get; private set; }
    /// <summary>How close a card comes to the window's edges.</summary>
    public const double Edge = 6;

    UIElement? _child;
    bool _open;
    Border? _box;
    ScrollViewer? _scroll;
    Point _anchor;
    double _anchorTop;

    public UIElement? Child
    {
        get => _child;
        set { _child = value; if (_open && _scroll != null) { _scroll.Content = value; Dispatcher()?.BeginInvoke(DispatcherPriority.Loaded, Place); } }
    }
    public bool StaysOpen { get; set; }
    /// <summary>Kept for the Popup's initializers; the layer is always see-through.</summary>
    public bool AllowsTransparency { get; set; }
    public PopupAnimation PopupAnimation { get; set; }
    public PlacementMode Placement { get; set; } = PlacementMode.Bottom;
    public UIElement? PlacementTarget { get; set; }
    public double HorizontalOffset { get; set; }
    public double VerticalOffset { get; set; }
    public event EventHandler? Opened;
    public event EventHandler? Closed;

    public bool IsOpen
    {
        get => _open;
        set { if (value == _open) return; if (value) Open(); else Close(); }
    }

    static Dispatcher? Dispatcher() => Layer?.Dispatcher;

    void Open()
    {
        var layer = Layer;
        if (layer == null || _child == null) return;
        if (Current != null && !ReferenceEquals(Current, this)) Current.Close();
        _open = true;
        Current = this;
        _anchor = AnchorPoint(layer, out _anchorTop);
        _scroll = new ScrollViewer { Content = _child, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Focusable = false };
        _box = new Border { Child = _scroll, Background = Brushes.Transparent };
        layer.Children.Add(_box);
        Place();
        if (PopupAnimation == PopupAnimation.Fade)
        {
            _box.Opacity = 0;
            _box.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(120)));
        }
        layer.Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () => { if (_open) Opened?.Invoke(this, EventArgs.Empty); });
    }

    void Close()
    {
        if (!_open) return;
        _open = false;
        if (ReferenceEquals(Current, this)) Current = null;
        if (_box != null) { if (_scroll != null) _scroll.Content = null; Layer?.Children.Remove(_box); }
        _box = null; _scroll = null;
        Closed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Where the card goes before it is kept inside the window: under the pointer, under its target, or in the target's middle.</summary>
    Point AnchorPoint(Canvas layer, out double top)
    {
        var target = PlacementTarget as FrameworkElement;
        if (Placement == PlacementMode.Mouse || target == null || !target.IsVisible)
        {
            var m = Mouse.GetPosition(layer);
            top = m.Y;
            return new Point(m.X + HorizontalOffset, m.Y + 18 + VerticalOffset);
        }
        var at = target.TranslatePoint(new Point(0, 0), layer);
        top = at.Y;
        if (Placement == PlacementMode.Center) return new Point(at.X + target.ActualWidth / 2, at.Y + target.ActualHeight / 2);
        if (Placement == PlacementMode.Right) return new Point(at.X + target.ActualWidth + HorizontalOffset, at.Y + VerticalOffset);
        return new Point(at.X + HorizontalOffset, at.Y + target.ActualHeight + VerticalOffset);   // Bottom, and anything else
    }

    /// <summary>Sizes the card to fit the window (scrolling when it is taller) and keeps it inside the window's edges.</summary>
    void Place()
    {
        var layer = Layer;
        if (_box == null || layer == null) return;
        double w = Math.Max(0, layer.ActualWidth - 2 * Edge), h = Math.Max(0, layer.ActualHeight - 2 * Edge);
        _box.MaxWidth = w; _box.MaxHeight = h;
        _box.Measure(new Size(w, h));
        var size = _box.DesiredSize;
        double x, y;
        if (Placement == PlacementMode.Center) { x = _anchor.X - size.Width / 2; y = _anchor.Y - size.Height / 2; }
        else
        {
            x = _anchor.X; y = _anchor.Y;
            // no room under it: above where it was opened, as a Popup flips
            if (y + size.Height > layer.ActualHeight - Edge && _anchorTop - size.Height >= Edge) y = _anchorTop - size.Height;
        }
        x = Math.Clamp(x, Edge, Math.Max(Edge, layer.ActualWidth - Edge - size.Width));
        y = Math.Clamp(y, Edge, Math.Max(Edge, layer.ActualHeight - Edge - size.Height));
        Canvas.SetLeft(_box, x); Canvas.SetTop(_box, y);
    }

    bool Holds(object? source) =>
        _box != null && source is DependencyObject d && (ReferenceEquals(d, _box) || Inside(d));

    bool Inside(DependencyObject d)
    {
        for (var p = d; p != null; p = p is Visual or System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetParent(p) : LogicalTreeHelper.GetParent(p))
            if (ReferenceEquals(p, _box)) return true;
        return false;
    }

    /* ---------------- what closes the card (MainWindow hooks these once) ---------------- */

    /// <summary>A press or the wheel somewhere in the window: outside the open card, it closes (unless StaysOpen).</summary>
    public static void Outside(object? source)
    {
        if (Current is { StaysOpen: false } c && !c.Holds(source)) c.Close();
    }

    /// <summary>The window lost focus (a click on the game) or changed size: the open card closes, as a Popup did.</summary>
    public static void Away()
    {
        if (Current is { StaysOpen: false } c) c.Close();
    }

    /// <summary>Esc that no card handled itself.</summary>
    public static bool Escape()
    {
        if (Current is not { } c) return false;
        c.Close();
        return true;
    }

    /// <summary>For options-test: the open card's place and size in the layer, and the layer's size.</summary>
    internal static (Rect Card, Size Layer)? Shown()
    {
        if (Current?._box is not { } b || Layer is not { } l) return null;
        return (new Rect(Canvas.GetLeft(b), Canvas.GetTop(b), b.ActualWidth, b.ActualHeight), new Size(l.ActualWidth, l.ActualHeight));
    }
}
