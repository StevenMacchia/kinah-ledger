using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Shapes;
using Path = System.Windows.Shapes.Path;
using static KinahLedger.Ui.HudKit;

namespace KinahLedger.Ui;

/// <summary>
/// The companion's face in its window: the sprite (Ui/CompanionSprite.cs) and, beside it, the room for its bubble, kept
/// whether a bubble shows or not so the companion never moves when one comes. The bubble says a moment in two lines, a small
/// label in the moment's colour ("NEW BEST") over the main line with its figure picked out ("Gadioton +8.2%"), in the
/// overlay theme's own finish: Classic the Kinah panel, Modern frosted glass, Tactical a HUD plate with a coloured edge,
/// Wacky a white sticker with a hard shadow, Glitch a phosphor terminal. It pops in from the companion's side, holds, and
/// fades (<see cref="SayMs"/>), through Motion like the sprite.
/// </summary>
public sealed class CompanionFace : Grid
{
    /// <summary>The bubble's room beside the sprite at 100%, and how long one shows, start to gone.</summary>
    public const double BubbleRoom = 244, SayMs = 4300;

    public CompanionSprite Sprite { get; }
    public string Theme { get; }
    public bool Lines { get; }
    /// <summary>The bubble on the sprite's left (the companion sits in the right half of its screen).</summary>
    public bool BubbleLeft { get; }
    readonly double _opacity;
    readonly Grid _room = new() { Width = BubbleRoom, VerticalAlignment = VerticalAlignment.Center, ClipToBounds = false };
    FrameworkElement? _bubble;
    readonly ScaleTransform _pop = new(1, 1);
    readonly TranslateTransform _slide = new();

    /// <param name="theme">The Buddy's theme family: the sprite's fur and gear in every family, the bubble's finish in the five that have one (Classic's otherwise).</param>
    /// <param name="look">The Buddy's look, for the families whose looks each tint the companion (Elements, Atreia...).</param>
    public CompanionFace(string theme, double opacity, bool lines, bool bubbleLeft, string? look = null)
    {
        Theme = theme is HudThemes.Modern or HudThemes.Tactical or HudThemes.Wacky or HudThemes.Glitch ? theme : HudThemes.Classic;
        _opacity = Math.Clamp(opacity, 0.3, 1);
        Lines = lines; BubbleLeft = bubbleLeft;
        Sprite = new CompanionSprite(theme, look);
        Tag = "companion-face:" + Theme;
        ClipToBounds = false;
        TextOptions.SetTextFormattingMode(this, TextFormattingMode.Display);
        ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(Sprite, bubbleLeft ? 1 : 0);
        Children.Add(Sprite);
        if (lines)
        {
            Grid.SetColumn(_room, bubbleLeft ? 0 : 1);
            Children.Add(_room);
            // the window only as big as the companion while it says nothing (a smaller window over the game costs less to
            // draw); a bubble on the left keeps its room, so the companion never moves when one comes
            if (!bubbleLeft) _room.Visibility = Visibility.Collapsed;
        }
        System.Windows.Automation.AutomationProperties.SetName(this, L.T("Companion"));
    }

    /// <summary>The bubble shown now (tests and renders); null with none.</summary>
    internal FrameworkElement? BubbleForTest => _bubble is { Visibility: Visibility.Visible } b ? b : null;

    /// <summary>Says <paramref name="m"/> in the bubble (nothing with Lines off): a bubble under way gives way to it.</summary>
    public void Say(CompanionMoment m)
    {
        if (!Lines) return;
        Show(m);
        _hush ??= new System.Windows.Threading.DispatcherTimer(System.Windows.Threading.DispatcherPriority.Background, Dispatcher);
        _hush.Stop();
        _hush.Tick -= Fade; _hush.Tick -= HushTick;
        if (Motion.On && Motion.FrozenAt == null)
        {
            // only its pop and its fade draw frames; while it holds, nothing is drawn
            Motion.Play(_bubble!, InMs, p => Pop(p * InMs), null, CompanionSprite.ReactFps);
            _hush.Interval = TimeSpan.FromMilliseconds(SayMs - OutMs);
            _hush.Tick += Fade;
        }
        else
        {
            // motion off: the bubble shown whole for as long as it would play
            Pop(600);
            _hush.Interval = TimeSpan.FromMilliseconds(SayMs);
            _hush.Tick += HushTick;
        }
        _hush.Start();
    }

    const double InMs = 300, OutMs = 340;
    System.Windows.Threading.DispatcherTimer? _hush;

    void Fade(object? s, EventArgs e)
    {
        _hush!.Stop();
        if (_bubble == null) return;
        Motion.Play(_bubble, OutMs, p => Pop(SayMs - OutMs + p * OutMs), Hush, CompanionSprite.ReactFps);
    }

    void HushTick(object? s, EventArgs e) { _hush!.Stop(); Hush(); }

    /// <summary>For renders: the bubble for <paramref name="m"/> held at <paramref name="ms"/> from its start.</summary>
    internal void HoldForRender(CompanionMoment m, double ms) { if (!Lines) return; Show(m); Pop(ms); }

    public void Hush()
    {
        _hush?.Stop();
        if (_bubble != null) { Motion.Halt(_bubble); _bubble.Visibility = Visibility.Collapsed; }
        if (!BubbleLeft) _room.Visibility = Visibility.Collapsed;
    }

    void Show(CompanionMoment m)
    {
        if (_bubble != null) { Motion.Halt(_bubble); _room.Children.Remove(_bubble); }
        _bubble = Bubble(m);
        _bubble.HorizontalAlignment = BubbleLeft ? HorizontalAlignment.Right : HorizontalAlignment.Left;
        _bubble.VerticalAlignment = VerticalAlignment.Center;
        _bubble.RenderTransformOrigin = new Point(BubbleLeft ? 1 : 0, 0.5);
        _bubble.RenderTransform = new TransformGroup { Children = { _pop, _slide } };
        _room.Children.Add(_bubble);
        _room.Visibility = Visibility.Visible;
    }

    /// <summary>The bubble at <paramref name="t"/> ms: it pops out of the companion's side, holds, then fades away.</summary>
    void Pop(double t)
    {
        if (_bubble == null) return;
        _bubble.Visibility = Visibility.Visible;
        double inP = Math.Clamp(t / (InMs - 20), 0, 1), outP = Math.Clamp((t - (SayMs - OutMs)) / OutMs, 0, 1);
        double s = .62 + .38 * Overshoot(inP);
        _pop.ScaleX = _pop.ScaleY = s - .06 * outP;
        _slide.X = (BubbleLeft ? 10 : -10) * (1 - Motion.Out(inP));
        _slide.Y = -4 * outP;
        _bubble.Opacity = Math.Min(1, t / 150) * (1 - outP);
    }

    static double Overshoot(double p) { const double k = 2.4; double q = p - 1; return 1 + (k + 1) * q * q * q + k * q * q; }

    /* ---------------- the bubble, theme by theme ---------------- */

    sealed record Inks(FontFamily Face, Brush Main, Brush Label, Brush Accent, FontWeight MainWeight, FontWeight LabelWeight, double Size);

    FrameworkElement Bubble(CompanionMoment m)
    {
        var tone = m.Tone;
        Inks k = Theme switch
        {
            HudThemes.Modern => new(HudKit.Text, B(0xF2F5FA), Frozen(Lift(tone)), Frozen(Lift(tone)), FontWeights.Medium, FontWeights.SemiBold, 13),
            HudThemes.Tactical => new(new FontFamily("Bahnschrift SemiBold, Bahnschrift, Segoe UI"), B(0xE6FBFF), Frozen(Lift(tone)), Frozen(Lift(tone)), FontWeights.Normal, FontWeights.Normal, 13.5),
            HudThemes.Wacky => new(new FontFamily("Segoe UI Black, Segoe UI"), B(0x1C0A2B), Frozen(Rgb(0x1C0A2B)), Frozen(Deepen(tone)), FontWeights.Black, FontWeights.Black, 13),
            HudThemes.Glitch => new(new FontFamily("Cascadia Mono, Consolas"), B(0xD9FFE2), Frozen(Lift(tone)), Frozen(Lift(tone)), FontWeights.SemiBold, FontWeights.Bold, 12.5),
            _ => new(HudKit.Text, Ink, Frozen(Lift(tone)), Frozen(Lift(tone)), FontWeights.SemiBold, FontWeights.Bold, 13),
        };
        var label = new TextBlock { Text = (Theme == HudThemes.Glitch ? "> " : "") + m.Label, FontFamily = k.Face, FontSize = 10, FontWeight = k.LabelWeight, Foreground = k.Label, Tag = "companion-label" };
        var main = new TextBlock { FontFamily = k.Face, FontSize = k.Size, FontWeight = k.MainWeight, Foreground = k.Main, TextWrapping = TextWrapping.Wrap, MaxWidth = BubbleRoom - 40, Margin = new Thickness(0, 1, 0, 0), Tag = "companion-line" };
        if (m.Text.Length > 0) main.Inlines.Add(new Run(m.Text));
        if (m.Accent.Length > 0) main.Inlines.Add(new Run(m.Accent) { Foreground = k.Accent, FontWeight = Theme == HudThemes.Wacky ? FontWeights.Black : FontWeights.Bold });
        Typography.SetNumeralAlignment(main, FontNumeralAlignment.Tabular);
        var words = new StackPanel { Children = { label, main } };
        if (Theme == HudThemes.Wacky)
        {
            // the label as a sticker tag of its own, in the moment's colour
            words.Children.Remove(label);
            label.Foreground = Brushes.White;
            label.FontSize = 9.5;
            var tag = new Border { Background = Frozen(Deepen(tone)), CornerRadius = new CornerRadius(5), Padding = new Thickness(6, 1, 6, 1), HorizontalAlignment = HorizontalAlignment.Left, Child = label, RenderTransform = new RotateTransform(-2.5) };
            words.Children.Insert(0, tag);
        }
        double op = _opacity;
        FrameworkElement box = Theme switch
        {
            HudThemes.Modern => new Border
            {
                Background = new LinearGradientBrush(Color.FromArgb((byte)(0xE0 * op), 0x24, 0x2B, 0x3A), Color.FromArgb((byte)(0xD8 * op), 0x14, 0x18, 0x22), 90),
                BorderBrush = Frozen(Color.FromArgb(0x30, 0xFF, 0xFF, 0xFF)), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(14), Padding = new Thickness(13, 8, 14, 9), Child = words,
            },
            HudThemes.Tactical => Tactical(words, tone, op),
            HudThemes.Wacky => Wacky(words),
            HudThemes.Glitch => Glitch(words, op),
            _ => new Border
            {
                Background = Frozen(Color.FromArgb((byte)(0xF0 * op), 0x0B, 0x0C, 0x10)), BorderBrush = Frozen(Color.FromArgb(0xFF, 0x2A, 0x2E, 0x3D)), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(10),
                Padding = new Thickness(12, 7, 13, 8), Child = new Grid { Children = { words, ClassicEdge(tone) } },
            },
        };
        // the tail toward the companion
        var tail = Tail(Theme, op);
        var g = new Grid { Tag = "companion-bubble" };
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(tail, BubbleLeft ? 1 : 0); Grid.SetColumn(box, BubbleLeft ? 0 : 1);
        g.Children.Add(box); g.Children.Add(tail);
        System.Windows.Automation.AutomationProperties.SetName(g, m.Label + " " + m.Text + m.Accent);
        return g;
    }

    /// <summary>Classic: a gold hairline at the top of the panel fading to the right, in the moment's colour.</summary>
    static Border ClassicEdge(Color tone)
    {
        var rule = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 0) };
        rule.GradientStops.Add(new GradientStop(Color.FromArgb(0xB0, tone.R, tone.G, tone.B), 0));
        rule.GradientStops.Add(new GradientStop(Color.FromArgb(0, tone.R, tone.G, tone.B), 1));
        return new Border { Height = 1, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(-12, -7, -13, 0), Background = rule, IsHitTestVisible = false };
    }

    static FrameworkElement Tactical(FrameworkElement words, Color tone, double op)
    {
        var plate = new Border
        {
            Background = new LinearGradientBrush(Color.FromArgb((byte)(0xE6 * op), 0x05, 0x1C, 0x26), Color.FromArgb((byte)(0xD0 * op), 0x03, 0x10, 0x16), 0),
            BorderBrush = Frozen(Color.FromArgb(0x90, 0x3F, 0xE0, 0xF5)), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(2),
            Padding = new Thickness(14, 7, 14, 8), Child = words,
        };
        var edge = new Border { Width = 3, HorizontalAlignment = HorizontalAlignment.Left, Background = Frozen(tone), Margin = new Thickness(1, 1, 0, 1) };
        var g = new Grid { Children = { plate, edge } };
        // the corner brackets, outside the plate
        var ink = Frozen(Color.FromArgb(0xE0, 0x3F, 0xE0, 0xF5));
        foreach (var (h, v) in new[] { (HorizontalAlignment.Left, VerticalAlignment.Top), (HorizontalAlignment.Right, VerticalAlignment.Top), (HorizontalAlignment.Left, VerticalAlignment.Bottom), (HorizontalAlignment.Right, VerticalAlignment.Bottom) })
        {
            double sx = h == HorizontalAlignment.Left ? 1 : -1, sy = v == VerticalAlignment.Top ? 1 : -1;
            g.Children.Add(new Path
            {
                Data = Geometry.Parse(string.Format(System.Globalization.CultureInfo.InvariantCulture, "M 0,{0} L 0,0 L {1},0", 6 * sy, 6 * sx)), Stroke = ink, StrokeThickness = 1.4,
                HorizontalAlignment = h, VerticalAlignment = v, Margin = new Thickness(-3), Stretch = Stretch.None, IsHitTestVisible = false,
            });
        }
        return g;
    }

    static FrameworkElement Wacky(FrameworkElement words)
    {
        var shadow = new Border { Background = Frozen(Color.FromArgb(0xE0, 0x1C, 0x0A, 0x2B)), CornerRadius = new CornerRadius(14), RenderTransform = new TranslateTransform(3.5, 3.5) };
        var face = new Border { Background = Brushes.White, BorderBrush = B(0x1C0A2B), BorderThickness = new Thickness(2.2), CornerRadius = new CornerRadius(14), Padding = new Thickness(11, 6, 13, 8), Child = words };
        return new Grid { Children = { shadow, face }, RenderTransform = new RotateTransform(-1.2), RenderTransformOrigin = new Point(.5, .5), Margin = new Thickness(0, 2, 4, 4) };
    }

    static FrameworkElement Glitch(FrameworkElement words, double op)
    {
        var lines = new DrawingBrush(new GeometryDrawing(Frozen(Color.FromArgb(0x24, 0x3C, 0xFF, 0x8A)), null, new RectangleGeometry(new Rect(0, 0, 4, 1))))
        { TileMode = TileMode.Tile, Viewport = new Rect(0, 0, 4, 3), ViewportUnits = BrushMappingMode.Absolute, Viewbox = new Rect(0, 0, 4, 3), ViewboxUnits = BrushMappingMode.Absolute };
        lines.Freeze();
        var plate = new Border { Background = Frozen(Color.FromArgb((byte)(0xEE * op), 0x01, 0x0A, 0x05)), BorderBrush = Frozen(Color.FromArgb(0xA0, 0x3C, 0xFF, 0x8A)), BorderThickness = new Thickness(1), Padding = new Thickness(11, 6, 13, 8), Child = words };
        var scan = new Border { Background = lines, IsHitTestVisible = false, Margin = new Thickness(1) };
        var g = new Grid { Children = { plate, scan } };
        // the terminal's corner marks
        var ink = Frozen(Color.FromArgb(0xFF, 0x3C, 0xFF, 0x8A));
        g.Children.Add(new Border { Width = 5, Height = 5, Background = ink, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, -2, -2, 0) });
        g.Children.Add(new Border { Width = 5, Height = 5, Background = ink, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(-2, 0, 0, -2) });
        return g;
    }

    /// <summary>The bubble's tail, in its box's colours, pointing at the companion.</summary>
    FrameworkElement Tail(string theme, double op)
    {
        (Color fill, Color line, double w) = theme switch
        {
            HudThemes.Modern => (Color.FromArgb((byte)(0xE0 * op), 0x22, 0x29, 0x37), Color.FromArgb(0x30, 0xFF, 0xFF, 0xFF), 1.0),
            HudThemes.Tactical => (Color.FromArgb((byte)(0xE6 * op), 0x05, 0x1C, 0x26), Color.FromArgb(0x90, 0x3F, 0xE0, 0xF5), 1.0),
            HudThemes.Wacky => (Colors.White, Rgb(0x1C0A2B), 2.2),
            HudThemes.Glitch => (Color.FromArgb((byte)(0xEE * op), 0x01, 0x0A, 0x05), Color.FromArgb(0xA0, 0x3C, 0xFF, 0x8A), 1.0),
            _ => (Color.FromArgb((byte)(0xF0 * op), 0x0B, 0x0C, 0x10), Rgb(0x2A2E3D), 1.0),
        };
        // drawn pointing left; turned for a bubble on the left
        var p = new Path
        {
            Data = Geometry.Parse("M 9,0 L 0,6.5 L 9,13"), Fill = Frozen(fill), Stroke = Frozen(line), StrokeThickness = w, StrokeLineJoin = PenLineJoin.Round,
            VerticalAlignment = VerticalAlignment.Center, Margin = BubbleLeft ? new Thickness(-1 - w, 0, 0, 0) : new Thickness(0, 0, -1 - w, 0),
            RenderTransformOrigin = new Point(.5, .5), RenderTransform = BubbleLeft ? new ScaleTransform(-1, 1) : Transform.Identity,
        };
        Panel.SetZIndex(p, 2);
        return p;
    }

    /// <summary>The moment's colour lifted toward white for words on a dark plate (a grade's green stays readable).</summary>
    static Color Lift(Color c) => Color.FromRgb((byte)(c.R + (255 - c.R) * .12), (byte)(c.G + (255 - c.G) * .12), (byte)(c.B + (255 - c.B) * .12));
    /// <summary>The moment's colour deepened for words on the white sticker.</summary>
    static Color Deepen(Color c) => Color.FromRgb((byte)(c.R * .72), (byte)(c.G * .62), (byte)(c.B * .72));
}
