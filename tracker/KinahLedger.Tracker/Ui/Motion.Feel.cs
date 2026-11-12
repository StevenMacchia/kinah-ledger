using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;

namespace KinahLedger.Ui;

/// <summary>How a card or a tile answers the mouse (<see cref="Motion.FeelProperty"/>).</summary>
public enum Feel
{
    None,
    /// <summary>A card or a row that opens something: it lifts a pixel, and a card's edge brightens.</summary>
    Lift,
    /// <summary>An item tile: it lifts, and a soft light passes across it once.</summary>
    Tile,
}

/// <summary>
/// The window under the mouse (2026-11-11, Steven: "amazing and professional"): what answers a click answers the pointer
/// first. A card, a row or a tile that opens something lifts a pixel as the pointer comes on it and settles back as it
/// leaves, in <see cref="HoverMs"/>; a card's edge brightens to the skin's CardLineHover; an item tile has a soft light pass
/// across it once. A button pressed sinks to 97% and springs back on release. Only RenderTransform and a layer over the
/// window move, so no size changes and a still render is the same as before. XAML triggers can't see <see cref="On"/>, so
/// the styles only say what state a thing is in (<see cref="FeelProperty"/>, <see cref="PressedProperty"/>) and the motion
/// is played here: through <see cref="Play"/> (KL_MOTION_AT holds it), and with Windows's animations off a hover or a press
/// is shown at once, unanimated, and the light doesn't pass. Each effect runs on a clock of its own, so it never ends a
/// slot's landing or a row's wash early, and it leaves a transform another effect holds alone.
/// </summary>
public static partial class Motion
{
    /// <summary>A hover's lift and its settling take this long; a press sinks in <see cref="PressMs"/> and springs back in <see cref="ReleaseMs"/>.</summary>
    public const double HoverMs = 160, PressMs = 90, ReleaseMs = 200;
    /// <summary>The light passing across a tile takes this long, once per hover.</summary>
    public const double SheenMs = 480;
    /// <summary>How far a card lifts under the mouse, and how far a pressed button sinks.</summary>
    const double LiftPx = 1, PressedScale = 0.97;
    /// <summary>The light's band, as a share of the tile's width, and how strong it is at its middle on a dark look and on a light one.</summary>
    const double SheenBand = 0.6, SheenDark = 0.13, SheenLight = 0.7;
    /// <summary>The band leans this many degrees from upright, as light catching a surface does.</summary>
    const double SheenTilt = 20;
    /// <summary>The light stays this far inside the tile's edge, so a lift a frame ahead of it never shows it past the edge.</summary>
    const double SheenInset = 1;
    /// <summary>A corner for the light over a tile that draws none of its own.</summary>
    const double SheenCorner = 8;

    /// <summary>Set on a card, a row or a tile (in code, or by a style such as QChip): how it answers the pointer.</summary>
    public static readonly DependencyProperty FeelProperty = DependencyProperty.RegisterAttached("Feel", typeof(Feel), typeof(Motion),
        new PropertyMetadata(Feel.None, (d, e) =>
        {
            if (d is not FrameworkElement el) return;
            el.MouseEnter -= PointerOn; el.MouseLeave -= PointerOff;
            if ((Feel)e.NewValue != Feel.None) { el.MouseEnter += PointerOn; el.MouseLeave += PointerOff; }
        }));
    public static Feel GetFeel(DependencyObject d) => (Feel)d.GetValue(FeelProperty);
    public static void SetFeel(DependencyObject d, Feel value) => d.SetValue(FeelProperty, value);

    /// <summary>Set by the button styles' IsPressed trigger (Theme.xaml): the button is held down.</summary>
    public static readonly DependencyProperty PressedProperty = DependencyProperty.RegisterAttached("Pressed", typeof(bool), typeof(Motion),
        new PropertyMetadata(false, (d, e) => { if (d is UIElement el) PressButton(el, (bool)e.NewValue); }));
    public static bool GetPressed(DependencyObject d) => (bool)d.GetValue(PressedProperty);
    public static void SetPressed(DependencyObject d, bool value) => d.SetValue(PressedProperty, value);

    /// <summary>What one element's hover and press hold: their transforms, their clocks, the edge set and the light passing.</summary>
    sealed class Touch
    {
        public readonly TranslateTransform Move = new();
        public readonly ScaleTransform Scale = new();
        public readonly UIElement LiftClock = new(), PressClock = new(), SheenClock = new();
        public bool Edged;
        public Rectangle? Sheen;
    }
    static readonly ConditionalWeakTable<UIElement, Touch> Touches = new();
    // where the pointer was when the light last passed, so a tile drawn again under a still pointer doesn't shine again
    static Point? _sheenFrom;

    /// <summary>No transform of its own: free for a hover or a press to take.</summary>
    static bool TransformFree(Transform? t) => t == null || t == Transform.Identity || t.Value.IsIdentity && t.IsFrozen;

    static void PointerOn(object sender, MouseEventArgs e) => HoverFeel((FrameworkElement)sender, true);
    static void PointerOff(object sender, MouseEventArgs e) => HoverFeel((FrameworkElement)sender, false);

    /// <summary>
    /// The pointer came on (<paramref name="on"/>) or left an element with a <see cref="Feel"/>. Only what answers a click
    /// (the hand pointer) answers the mouse: a card that opens nothing stays still.
    /// </summary>
    internal static void HoverFeel(FrameworkElement el, bool on)
    {
        if (on && el.Cursor != Cursors.Hand) return;
        var t = Touches.GetValue(el, _ => new Touch());
        HoverLift(el, t, on);
        HoverEdge(el, t, on);
        if (on && GetFeel(el) == Feel.Tile) HoverSheen(el, t);
    }

    /// <summary>The lift: up a pixel on the way in, back down on the way out, eased; at once with motion off.</summary>
    static void HoverLift(FrameworkElement el, Touch t, bool up)
    {
        if (!ReferenceEquals(el.RenderTransform, t.Move))
        {
            // a slot landing or a row washing holds the transform: it plays out undisturbed
            if (!up || !TransformFree(el.RenderTransform)) return;
            t.Move.Y = 0;
            el.RenderTransform = t.Move;
        }
        double from = t.Move.Y, to = up ? -LiftPx : 0;
        void Settled() { if (!up && ReferenceEquals(el.RenderTransform, t.Move)) el.ClearValue(UIElement.RenderTransformProperty); }
        if (!On) { Halt(t.LiftClock); t.Move.Y = to; Settled(); return; }
        Play(t.LiftClock, HoverMs, p => t.Move.Y = from + (to - from) * Out(p), Settled);
    }

    /// <summary>A card's edge to the skin's brighter one while the pointer is on it (a card whose edge its page sets keeps that).</summary>
    static void HoverEdge(FrameworkElement el, Touch t, bool on)
    {
        if (el is not Border b || b.BorderThickness.Left <= 0 || GetFeel(el) != Feel.Lift) return;
        if (on && !t.Edged && b.ReadLocalValue(Border.BorderBrushProperty) == DependencyProperty.UnsetValue)
        {
            b.SetResourceReference(Border.BorderBrushProperty, "CardLineHover");
            t.Edged = true;
        }
        else if (!on && t.Edged) { b.ClearValue(Border.BorderBrushProperty); t.Edged = false; }
    }

    /// <summary>
    /// The light passing across a tile once: a soft, leaning band in the skin's lightest tone, from the left edge to the right,
    /// inside the tile's own corners. Drawn on the window's card layer just under the hover cards (so a tile's card opening
    /// over it covers it) and moved with the tile's lift. Not with motion off, and not again while the pointer hasn't moved.
    /// </summary>
    static void HoverSheen(FrameworkElement el, Touch t)
    {
        if (!On || InCard.Layer is not { } layer || !ReferenceEquals(Window.GetWindow(el), Window.GetWindow(layer))) return;
        var pointer = Mouse.GetPosition(layer);
        if (FrozenAt == null && _sheenFrom == pointer) return;
        _sheenFrom = pointer;
        var surface = el is Panel p && p.Children.Count > 0 && p.Children[0] is Border first ? first : el;
        double w = surface.ActualWidth, h = surface.ActualHeight;
        if (w <= 2 * SheenInset || h <= 2 * SheenInset) return;
        if (t.Sheen != null) { Halt(t.SheenClock); layer.Children.Remove(t.Sheen); t.Sheen = null; }
        double corner = surface is Border sb ? sb.CornerRadius.TopLeft : SheenCorner;
        // where the surface is on the layer with the lift taken out; the lift is put back by the same transform as the tile's
        if (surface.TransformToVisual(layer) is not Transform toLayer) return;
        var rest = toLayer.Value;
        if (ReferenceEquals(el.RenderTransform, t.Move)) rest = new Matrix(1, 0, 0, 1, 0, -t.Move.Y) * rest;
        var place = new TransformGroup();
        place.Children.Add(new TranslateTransform(SheenInset, SheenInset));
        if (ReferenceEquals(el.RenderTransform, t.Move)) place.Children.Add(t.Move);
        place.Children.Add(new MatrixTransform(rest));
        // the band starts wholly left of the tile and ends wholly right of it, its lean taken into account
        double band = w * SheenBand, lean = h * Math.Tan(SheenTilt * Math.PI / 180);
        var light = SheenLightColor();
        var sweep = new TranslateTransform(-band, 0);
        var tilt = new TransformGroup();
        tilt.Children.Add(new SkewTransform(-SheenTilt, 0));
        tilt.Children.Add(sweep);
        var fill = new LinearGradientBrush
        {
            MappingMode = BrushMappingMode.Absolute, StartPoint = new Point(0, 0), EndPoint = new Point(band, 0), Transform = tilt,
            GradientStops = { new GradientStop(Color.FromArgb(0, light.R, light.G, light.B), 0), new GradientStop(light, 0.5), new GradientStop(Color.FromArgb(0, light.R, light.G, light.B), 1) },
        };
        var sheen = new Rectangle
        {
            Width = w - 2 * SheenInset, Height = h - 2 * SheenInset, RadiusX = Math.Max(0, corner - SheenInset), RadiusY = Math.Max(0, corner - SheenInset),
            Fill = fill, IsHitTestVisible = false, RenderTransform = place,
        };
        Panel.SetZIndex(sheen, -1);
        layer.Children.Add(sheen);
        t.Sheen = sheen;
        // eased in and out (smoothstep), so the light glides across at an even pace instead of lingering at the far edge
        Play(t.SheenClock, SheenMs, q => sweep.X = -band + (w + band + lean) * q * q * (3 - 2 * q), () =>
        {
            layer.Children.Remove(sheen);
            if (ReferenceEquals(t.Sheen, sheen)) t.Sheen = null;
        });
    }

    /// <summary>The light's colour at its middle: the skin's lightest tone (its ink on a dark look, its card on a light one).</summary>
    static Color SheenLightColor()
    {
        var ink = Brush("Ink", Colors.White);
        var card = Brush("Card", Colors.Black);
        bool dark = SheenLuma(ink) > SheenLuma(card);
        var c = dark ? ink : card;
        return Color.FromArgb((byte)(255 * (dark ? SheenDark : SheenLight)), c.R, c.G, c.B);
    }
    static double SheenLuma(Color c) => 0.2126 * c.R + 0.7152 * c.G + 0.0722 * c.B;

    /// <summary>A button held down sinks to 97%; let go, it springs back with a touch of overshoot. At once with motion off.</summary>
    static void PressButton(UIElement el, bool down)
    {
        var t = Touches.GetValue(el, _ => new Touch());
        if (!ReferenceEquals(el.RenderTransform, t.Scale))
        {
            if (!down || !TransformFree(el.RenderTransform)) return;
            t.Scale.ScaleX = t.Scale.ScaleY = 1;
            el.RenderTransformOrigin = new Point(0.5, 0.5);
            el.RenderTransform = t.Scale;
        }
        double from = t.Scale.ScaleX, to = down ? PressedScale : 1;
        void Released()
        {
            if (down || !ReferenceEquals(el.RenderTransform, t.Scale)) return;
            el.ClearValue(UIElement.RenderTransformProperty);
            el.ClearValue(UIElement.RenderTransformOriginProperty);
        }
        if (!On) { Halt(t.PressClock); t.Scale.ScaleX = t.Scale.ScaleY = to; Released(); return; }
        Play(t.PressClock, down ? PressMs : ReleaseMs, p =>
        {
            double s = from + (to - from) * (down ? Out(p) : PressSpring(p));
            t.Scale.ScaleX = t.Scale.ScaleY = s;
        }, Released);
    }

    /// <summary>An ease that overshoots its end a little and settles back (ease-out-back): a press springing back.</summary>
    static double PressSpring(double p)
    {
        const double c1 = 1.70158, c3 = c1 + 1;
        double x = Math.Clamp(p, 0, 1) - 1;
        return 1 + c3 * x * x * x + c1 * x * x;
    }

    /// <summary>For options-test: an element's lift (0 at rest, -1 lifted), its press scale (1 at rest), and whether light is passing on it.</summary>
    internal static (double Lift, double Scale, bool Shining) TouchOf(UIElement el)
        => Touches.TryGetValue(el, out var t)
            ? (ReferenceEquals(el.RenderTransform, t.Move) ? t.Move.Y : 0, ReferenceEquals(el.RenderTransform, t.Scale) ? t.Scale.ScaleX : 1, t.Sheen != null)
            : (0, 1, false);
}
