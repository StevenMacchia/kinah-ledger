using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using static KinahLedger.Ui.HudKit;

namespace KinahLedger.Ui;

/// <summary>
/// The hot streak bar (2026-11-12, Steven: "a thin bar that fills as pickups come quickly and drains when you slow
/// down"), on each of the overlay's looks: 2 px, gold running to a warm orange as it fills, with a faint glow once it is
/// nearly full, from the window's "streak" value (0 to 1, MainWindow.HudStreak: the pickups of the last 45 s). Pop draws
/// it along its pill's bottom, Buddy under its figures and Badge along its bottom edge, each laid over what is there so
/// it takes no room: the looks keep their sizes. It slides to each new value (<see cref="Motion.Slide"/>), so it fills and
/// drains smoothly, in steps of a 48th so the drain as pickups age is a few short slides rather than one every tick;
/// hidden at 0 and while nothing is counted. A drawing (Still) sets it as it is; KL_MOTION_AT holds its slide.
/// </summary>
sealed class StreakBar
{
    /// <summary>The bar's thickness, and the steps its value moves in.</summary>
    public const double Thick = 2, Steps = 48;
    /// <summary>The glow comes in from this share up, to this strength at full.</summary>
    const double GlowFrom = 0.8, GlowMax = 0.55;
    /// <summary>Warm orange, the full end of the bar (gold at its start).</summary>
    const uint HotC = 0xF0803C;

    /// <summary>The bar (laid at the bottom of its host's cell, over what is there).</summary>
    public Grid Root { get; }
    readonly Grid _fillHost;
    readonly GradientStop _edgeOn, _edgeOff;
    readonly DropShadowEffect _glow = new() { Color = Rgb(0xF5A040), BlurRadius = 6, ShadowDepth = 0, Opacity = 0 };
    /// <summary>This bar's own key for its slide: two looks (the overlay and Options' preview) never move each other's.</summary>
    readonly string _key = "hud-streak|" + Guid.NewGuid().ToString("N")[..8];

    /// <summary>The share shown now (for the self-test and a render's check).</summary>
    public double Shown { get; private set; }

    public StreakBar()
    {
        var fill = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 0) };
        fill.GradientStops.Add(new GradientStop(Rgb(GoldC), 0));
        fill.GradientStops.Add(new GradientStop(Rgb(0xF5A040), 0.55));
        fill.GradientStops.Add(new GradientStop(Rgb(HotC), 1));
        fill.Freeze();
        // the fill's reach is an opacity mask, so the colours stay where they are along the bar: it warms as it fills
        var mask = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 0) };
        mask.GradientStops.Add(new GradientStop(Colors.Black, 0));
        _edgeOn = new GradientStop(Colors.Black, 0); _edgeOff = new GradientStop(Colors.Transparent, 0);
        mask.GradientStops.Add(_edgeOn); mask.GradientStops.Add(_edgeOff);
        mask.GradientStops.Add(new GradientStop(Colors.Transparent, 1));
        var bar = new Border { CornerRadius = new CornerRadius(Thick / 2), Background = fill, OpacityMask = mask };
        // the glow on a host of the masked fill, so it glows round the fill's reach, not the whole bar
        _fillHost = new Grid { Children = { bar } };
        Root = new Grid
        {
            Height = Thick, VerticalAlignment = VerticalAlignment.Bottom, IsHitTestVisible = false, Visibility = Visibility.Collapsed,
            Children = { new Border { CornerRadius = new CornerRadius(Thick / 2), Background = B(0xFFFFFF, .08) }, _fillHost },
        };
    }

    /// <summary>
    /// The bar for this update: the "streak" value, hidden when it is 0 or nothing is <paramref name="counted"/>. When the look
    /// <paramref name="moves"/> it slides there; a drawing sets it as it is.
    /// </summary>
    public void Show(IReadOnlyDictionary<string, string> values, bool counted, bool moves)
    {
        double? v = Of(values);
        if (v == null || !counted)
        {
            Motion.Halt(Root);
            Motion.ForgetShare(_key);
            Apply(0);
            return;
        }
        if (moves) Motion.Slide(Root, _key, v.Value, Apply);
        else Apply(v.Value);
    }

    /// <summary>For a render of the motion (KL_MOTION_AT): the bar as if it had just filled from <paramref name="from"/> of its value.</summary>
    public void Replay(IReadOnlyDictionary<string, string> values, double from)
    {
        if (Of(values) is not double v || v <= 0) return;
        Motion.ReplaySlide(Root, _key, v * from, v, Apply);
    }

    /// <summary>The "streak" value, in the bar's steps (0 to 1), or null with none.</summary>
    static double? Of(IReadOnlyDictionary<string, string> values) =>
        values.TryGetValue("streak", out var s) && double.TryParse(s, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var v)
            ? Math.Round(Math.Clamp(v, 0, 1) * Steps) / Steps : null;

    /// <summary>The bar at <paramref name="s"/>: filled that far, glowing faintly near full, gone at nothing.</summary>
    void Apply(double s)
    {
        Shown = s;
        Root.Visibility = s > 0.001 ? Visibility.Visible : Visibility.Collapsed;
        _edgeOn.Offset = _edgeOff.Offset = Math.Clamp(s, 0, 1);
        double g = Math.Clamp((s - GlowFrom) / (1 - GlowFrom), 0, 1);
        _glow.Opacity = GlowMax * g;
        // no effect at all below its share: an effect costs its draw even unseen
        if (g > 0 && _fillHost.Effect == null) _fillHost.Effect = _glow;
        else if (g <= 0 && _fillHost.Effect != null) _fillHost.ClearValue(UIElement.EffectProperty);
    }
}
