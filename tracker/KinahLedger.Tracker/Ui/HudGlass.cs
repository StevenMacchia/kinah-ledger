using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace KinahLedger.Ui;

/// <summary>
/// The overlay's glass (2026-11-14, Steven: "make sure the overlay is fucking nice looking, looks cheap man"): one surface
/// for every part over the game (the Kinah line, Card and Ticker, the timer line, the Parser's section, the separate timer
/// strip and Parser overlay).
/// <list type="bullet">
/// <item>The fill is a dark translucent gradient, a touch lighter at the top.</item>
/// <item>The edge is a hairline that is brighter along the top (the inner highlight) and fades down the sides.</item>
/// <item>Outside that sits a second, dark hairline, so the edge reads over a bright field and over a black dungeon alike.</item>
/// <item>There is one corner radius, and joints where parts stack read as one panel.</item>
/// </list>
/// There is no blurred shadow: it would be drawn again on every update of a window that sits over the game all session.
/// On since 0.10.91; KL_HUD_V3=0 turns it off (<see cref="On"/>), so a render shows the old and the new from one build.
/// </summary>
static class HudGlass
{
    /// <summary>
    /// Which of the three proposed surfaces (KL_HUD_V3): "glass" (1 or glass: translucent, lit at the top edge, round),
    /// "slate" (solid and crisp, small corners, a pro meter's look) or "edge" (darker and see-through, one gold line along
    /// the top, no frame on the sides); "" is the look before the design pass.
    /// </summary>
    public static string Variant = Environment.GetEnvironmentVariable("KL_HUD_V3") switch { "slate" => "slate", "edge" => "edge", "0" => "", _ => "glass" };   // on since 0.10.91; KL_HUD_V3=0 renders the look before

    /// <summary>The design pass's surface on (the new looks, the separate timer strip and Parser overlay).</summary>
    public static bool On => Variant.Length > 0;

    /// <summary>
    /// The surface on Badge, Buddy and Pop too: only when a render asks for it (KL_HUD_V3 set). They keep their own frames,
    /// whose edge lights up with a pickup (Ui/HudWindow.BadgeMotion.cs).
    /// </summary>
    public static bool OldLooks = On && Environment.GetEnvironmentVariable("KL_HUD_V3") is { Length: > 0 } v && v != "0";

    /// <summary>Every panel's corner.</summary>
    public static double Radius => Variant switch { "slate" => 6, "edge" => 10, _ => 12 };

    static SolidColorBrush Frozen(Color c) { var b = new SolidColorBrush(c); b.Freeze(); return b; }

    /// <summary>The fill at an opacity (Options › In game, 0.3 to 1): 1A1D27 at the top to 0C0D12 at the foot, a step under the opacity at the top.</summary>
    public static Brush Fill(double opacity = 0.94)
    {
        double a = Math.Clamp(opacity, 0.3, 1);
        if (Variant == "slate") return Frozen(Color.FromArgb((byte)Math.Round(255 * Math.Min(1, a + 0.04)), 0x12, 0x14, 0x1B));
        if (Variant == "edge")
        {
            var e = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(0, 1) };
            e.GradientStops.Add(new GradientStop(Color.FromArgb((byte)Math.Round(255 * a * 0.80), 0x10, 0x11, 0x17), 0));
            e.GradientStops.Add(new GradientStop(Color.FromArgb((byte)Math.Round(255 * a * 0.88), 0x07, 0x08, 0x0B), 1));
            e.Freeze();
            return e;
        }
        var b = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(0, 1) };
        b.GradientStops.Add(new GradientStop(Color.FromArgb((byte)Math.Round(255 * a * 0.97), 0x1A, 0x1D, 0x27), 0));
        b.GradientStops.Add(new GradientStop(Color.FromArgb((byte)Math.Round(255 * a), 0x0C, 0x0D, 0x12), 1));
        b.Freeze();
        return b;
    }

    /// <summary>The inner edge: white at 18% along the top, 7% down the sides and the foot.</summary>
    public static Brush Edge => Variant switch { "slate" => SlateEdge, "edge" => GoldTop, _ => GlassEdge };
    static readonly Brush GlassEdge = MakeEdge();
    static readonly Brush SlateEdge = Frozen(Color.FromRgb(0x2E, 0x33, 0x46));
    /// <summary>The edge-lit look's frame: gold along the top edge only, gone a few pixels down the sides.</summary>
    static readonly Brush GoldTop = MakeGoldTop();
    static Brush MakeGoldTop()
    {
        var b = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(0, 1), MappingMode = BrushMappingMode.Absolute };
        b.StartPoint = new Point(0, 0); b.EndPoint = new Point(0, 8);
        b.GradientStops.Add(new GradientStop(Color.FromArgb(0xE6, 0xF5, 0xB9, 0x42), 0));
        b.GradientStops.Add(new GradientStop(Color.FromArgb(0x00, 0xF5, 0xB9, 0x42), 1));
        b.Freeze();
        return b;
    }
    static Brush MakeEdge()
    {
        var b = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(0, 1) };
        b.GradientStops.Add(new GradientStop(Color.FromArgb(0x2E, 0xFF, 0xFF, 0xFF), 0));
        b.GradientStops.Add(new GradientStop(Color.FromArgb(0x14, 0xFF, 0xFF, 0xFF), 0.35));
        b.GradientStops.Add(new GradientStop(Color.FromArgb(0x12, 0xFF, 0xFF, 0xFF), 1));
        b.Freeze();
        return b;
    }

    /// <summary>The outer hairline: black at 55%, what keeps the edge over a bright scene.</summary>
    public static Brush Outer => Variant == "edge" ? Brushes.Transparent : OuterBlack;
    static readonly Brush OuterBlack = Frozen(Color.FromArgb(0x8C, 0x00, 0x00, 0x00));

    /// <summary>A rule inside a panel (between stacked parts, under a head): white at 8%.</summary>
    public static readonly Brush Rule = Frozen(Color.FromArgb(0x14, 0xFF, 0xFF, 0xFF));

    /// <summary>A meter's track: white at 7%.</summary>
    public static readonly Brush Track = Frozen(Color.FromArgb(0x12, 0xFF, 0xFF, 0xFF));

    /// <summary>Puts the glass on a frame: the fill, the inner edge, the corner (the padding is the frame's own).</summary>
    public static void Apply(Border frame, double opacity = 0.94, CornerRadius? corner = null)
    {
        frame.Background = Fill(opacity);
        frame.BorderBrush = Edge;
        frame.BorderThickness = Variant == "edge" ? new Thickness(0, 2, 0, 0) : new Thickness(1);
        frame.CornerRadius = corner ?? new CornerRadius(Radius);
    }

    /// <summary>The dark outer hairline round a glass frame (its own corner one wider).</summary>
    public static Border Ring(UIElement frame, CornerRadius? corner = null) => new()
    {
        Child = frame, BorderBrush = Outer, BorderThickness = new Thickness(1),
        CornerRadius = corner is { } c ? new CornerRadius(c.TopLeft + 1, c.TopRight + 1, c.BottomRight + 1, c.BottomLeft + 1) : new CornerRadius(Radius + 1),
    };

    /// <summary>The "you" chip: gold words on a faint gold wash with a gold hairline, not a solid gold block.</summary>
    public static Border YouChip() => new()
    {
        Background = Frozen(Color.FromArgb(0x24, 0xF5, 0xB9, 0x42)), BorderBrush = Frozen(Color.FromArgb(0x8C, 0xF5, 0xB9, 0x42)), BorderThickness = new Thickness(1),
        CornerRadius = new CornerRadius(4), Padding = new Thickness(4, 0, 4, 0), Margin = new Thickness(6, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center,
        Child = new TextBlock { Text = "YOU", FontSize = 8.5, FontWeight = FontWeights.Bold, Foreground = HudKit.Gold },
    };
}

public sealed partial class HudWindow
{
    Border? _timerRing;

    /// <summary>
    /// The glass on the strip (the default look): the timer line, the Parser's section and the Kinah line read as one panel.
    /// The parts showing stand flush, as wide as the widest, the panel's corners on its top and foot only, and a hairline
    /// where two parts meet. Card and Ticker keep their own shapes, each part its own glass with a 4 px gap.
    /// </summary>
    void ComposeStack()
    {
        if (!HudGlass.OldLooks || _look != null) return;
        var parts = new List<(Border Ring, Border Frame)>();
        if (_timerSlot.Visibility == Visibility.Visible && _timerRing is { Child: Border tf }) parts.Add((_timerRing, tf));
        if (_parserSlot.Visibility == Visibility.Visible && _parserRing is { Child: Border pf }) parts.Add((_parserRing, pf));
        if (_content is Border kr && kr.Child is Border kf) parts.Add((kr, kf));
        double r = HudGlass.Radius;
        if (parts.Count == 0) return;
        for (int i = 0; i < parts.Count; i++)
        {
            bool first = i == 0, last = i == parts.Count - 1;
            var (ring, frame) = parts[i];
            frame.CornerRadius = new CornerRadius(first ? r : 0, first ? r : 0, last ? r : 0, last ? r : 0);
            ring.CornerRadius = new CornerRadius(first ? r + 1 : 0, first ? r + 1 : 0, last ? r + 1 : 0, last ? r + 1 : 0);
            ring.BorderThickness = new Thickness(1, first ? 1 : 0, 1, last ? 1 : 0);
            // the inner edge's bright top is the hairline between two parts; the part above has no foot line of its own
            frame.BorderThickness = new Thickness(1, 1, 1, last ? 1 : 0);
            if (HudGlass.Variant == "edge") { frame.BorderBrush = first ? HudGlass.Edge : HudGlass.Rule; frame.BorderThickness = first ? new Thickness(0, 2, 0, 0) : new Thickness(0, 1, 0, 0); }
        }
    }
}
