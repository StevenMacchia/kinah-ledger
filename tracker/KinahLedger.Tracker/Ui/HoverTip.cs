using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace KinahLedger.Ui;

/// <summary>
/// The hover tips of the tracker's window, drawn on the click cards' layer (InCard.Layer) inside the window and kept within
/// its edges, so a tip never sits over the game (Steven, 2026-11-06: "never overlay the game"). A WPF ToolTip is a window of
/// its own and hung past the tracker's edge. The tooltips stay set as they were (ToolTip = a string, an element, or a
/// LazyTip); the window's tooltip is cancelled as it opens, after its usual delay, and the same content is shown here.
/// It stays up while the pointer is on its element; leaving it, a press, the wheel, the window losing focus or changing
/// size takes it away. It takes no clicks, so it never steals the hover from what is under it.
/// </summary>
public static class HoverTip
{
    /// <summary>The element whose tip shows now, if any.</summary>
    public static FrameworkElement? Owner { get; private set; }
    /// <summary>Raised before a tip is read, so the window can bring it up to date (Start run's says whether it clears the counts).</summary>
    public static event Action<FrameworkElement>? Opening;
    /// <summary>The window whose tips are drawn here; another window (Options) keeps its own.</summary>
    public static Window? Host { get; set; }

    static Border? _box;
    static bool _hooked;

    /// <summary>Cancels every tooltip of the host window as it opens and shows it on the layer instead. Once per run.</summary>
    public static void Hook()
    {
        if (_hooked) return;
        _hooked = true;
        EventManager.RegisterClassHandler(typeof(FrameworkElement), FrameworkElement.ToolTipOpeningEvent, new ToolTipEventHandler(OnOpening), true);
    }

    static void OnOpening(object sender, ToolTipEventArgs e)
    {
        if (sender is not FrameworkElement el || Host == null || InCard.Layer == null) return;
        if (!ReferenceEquals(Window.GetWindow(el), Host)) return;
        e.Handled = true;   // the window's own tooltip stays shut
        Show(el);
    }

    /// <summary>Shows el's tip on the layer: under the pointer (or under el when the pointer is not on it), kept inside the window.</summary>
    public static void Show(FrameworkElement el)
    {
        var layer = InCard.Layer;
        if (layer == null) return;
        Hide();
        Opening?.Invoke(el);
        var (content, card) = Content(el.ToolTip);
        if (content == null) return;
        Owner = el;
        _box = card
            ? new Border { Child = content }   // a game-tooltip card draws its own frame
            : new Border
            {
                Child = content,
                BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(6), Padding = new Thickness(10, 6, 10, 6),
            };
        if (!card)
        {
            _box.SetResourceReference(Border.BackgroundProperty, "Card2");
            _box.SetResourceReference(Border.BorderBrushProperty, "LineStrong");
        }
        _box.IsHitTestVisible = false;
        Panel.SetZIndex(_box, 1000);
        layer.Children.Add(_box);
        Place(el, layer);
        _box.Opacity = 0;
        _box.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(90)));
        el.MouseLeave += Left;
        el.IsVisibleChanged += Gone;
        el.Unloaded += GoneRouted;
    }

    /// <summary>What a ToolTip value shows: a string as text, an element as itself, a LazyTip's card built once.</summary>
    static (UIElement? Content, bool Card) Content(object? tip)
    {
        switch (tip)
        {
            case null: return (null, false);
            case LazyTip lazy: return (lazy.Card(), true);
            case ToolTip t: return Content(t.Content);
            case UIElement u: return (u, false);
            default:
                var text = tip.ToString();
                if (string.IsNullOrWhiteSpace(text)) return (null, false);
                var tb = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, MaxWidth = 380, FontSize = 12.5 };
                tb.SetResourceReference(TextBlock.ForegroundProperty, "Ink");
                return (tb, false);
        }
    }

    static void Place(FrameworkElement el, Canvas layer)
    {
        if (_box == null) return;
        double edge = InCard.Edge;
        double w = Math.Max(0, layer.ActualWidth - 2 * edge), h = Math.Max(0, layer.ActualHeight - 2 * edge);
        _box.MaxWidth = w; _box.MaxHeight = h;
        _box.Measure(new Size(w, h));
        var size = _box.DesiredSize;
        double x, y, top;
        if (el.IsMouseOver)
        {
            var m = Mouse.GetPosition(layer);
            x = m.X; y = m.Y + 20; top = m.Y - 4;
        }
        else
        {
            var at = el.TranslatePoint(new Point(0, 0), layer);
            x = at.X; y = at.Y + el.ActualHeight + 4; top = at.Y - 4;
        }
        // no room under it: above, as the window's tooltip flips
        if (y + size.Height > layer.ActualHeight - edge && top - size.Height >= edge) y = top - size.Height;
        x = Math.Clamp(x, edge, Math.Max(edge, layer.ActualWidth - edge - size.Width));
        y = Math.Clamp(y, edge, Math.Max(edge, layer.ActualHeight - edge - size.Height));
        Canvas.SetLeft(_box, x); Canvas.SetTop(_box, y);
    }

    static void Left(object sender, MouseEventArgs e) => Hide();
    static void Gone(object sender, DependencyPropertyChangedEventArgs e) => Hide();
    static void GoneRouted(object sender, RoutedEventArgs e) => Hide();

    /// <summary>Takes the tip away (the pointer left, a press, the wheel, the window lost focus or changed size).</summary>
    public static void Hide()
    {
        if (Owner is { } el) { el.MouseLeave -= Left; el.IsVisibleChanged -= Gone; el.Unloaded -= GoneRouted; }
        Owner = null;
        if (_box != null)
        {
            _box.Child = null;   // the element goes back to being its tooltip's (a built card is kept for its next hover)
            InCard.Layer?.Children.Remove(_box);
            _box = null;
        }
    }

    /// <summary>For options-test: the tip's place and size in the layer, and the layer's size.</summary>
    internal static (Rect Card, Size Layer)? Shown()
    {
        if (_box is not { } b || InCard.Layer is not { } l) return null;
        return (new Rect(Canvas.GetLeft(b), Canvas.GetTop(b), b.ActualWidth, b.ActualHeight), new Size(l.ActualWidth, l.ActualHeight));
    }
}

/// <summary>A hover card built the first time it shows (a game-tooltip card of an item or kinah), then kept.</summary>
public sealed class LazyTip
{
    readonly Func<FrameworkElement> _build;
    FrameworkElement? _card;
    public LazyTip(Func<FrameworkElement> build) => _build = build;

    public FrameworkElement Card()
    {
        if (_card != null) return _card;
        try { _card = _build(); }
        catch (Exception ex) { CrashLog.Write("today card", ex); _card = new TextBlock { Text = "Couldn't draw this card." }; }
        return _card;
    }

    public override string ToString() => "";
}
