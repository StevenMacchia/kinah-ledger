using System.Windows;
using System.Windows.Media;
using System.Windows.Shapes;

namespace KinahLedger.Ui;

public static partial class Motion
{
    /// <summary>A light sweep crossing a figure takes this long.</summary>
    public const double LightSweepMs = 1100;

    // the band: where it fades in and out across the brush, its gold shoulders either side of the centre, how much white
    // its hot core has (so it reads as light on a dark figure and on Daylight's ink alike), and how far it slants
    const double BandFrom = 0.34, BandTo = 0.66, Shoulder = 0.05, CoreWhite = 0.6, BandSlant = 0.9;
    // the band's centre travels from a little left of the figure to a little right of it, so it enters and leaves cleanly
    const double SweepStart = -0.75, SweepTravel = 1.5;

    /// <summary>
    /// One slanted band of light in <paramref name="tone"/>, white-hot at its centre, crossing <paramref name="over"/>'s own
    /// pixels (a figure's glyphs) from left to right, quick to start and settling out. <paramref name="light"/> is a rectangle
    /// laid exactly over it (the same grid cell), shown only for the course: its fill is the band sliding, masked by a live
    /// picture of the element, so the light falls on the figure and nowhere else, follows it while it rolls, and nothing is
    /// laid out again.
    /// </summary>
    public static void LightSweep(Rectangle light, FrameworkElement over, string tone)
    {
        if (!On) return;
        var c = Brush(tone, Colors.Gold);
        var clear = Color.FromArgb(0, c.R, c.G, c.B);
        var slide = new TranslateTransform(SweepStart, 0);
        var band = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, BandSlant), RelativeTransform = slide };
        band.GradientStops.Add(new GradientStop(clear, BandFrom));
        band.GradientStops.Add(new GradientStop(c, 0.5 - Shoulder));
        band.GradientStops.Add(new GradientStop(Mix(c, Colors.White, CoreWhite), 0.5));
        band.GradientStops.Add(new GradientStop(c, 0.5 + Shoulder));
        band.GradientStops.Add(new GradientStop(clear, BandTo));
        var mask = new VisualBrush(over) { Stretch = Stretch.Fill, ViewboxUnits = BrushMappingMode.Absolute };
        light.Fill = band;
        light.OpacityMask = mask;
        light.Visibility = Visibility.Visible;
        Play(light, LightSweepMs, p =>
        {
            // the picture is the element's box at its size now, so a figure that rolls wider is still lit glyph for glyph
            mask.Viewbox = new Rect(0, 0, Math.Max(1, over.ActualWidth), Math.Max(1, over.ActualHeight));
            slide.X = SweepStart + SweepTravel * Out(p);
        }, () =>
        {
            light.Visibility = Visibility.Collapsed;
            light.ClearValue(Shape.FillProperty);
            light.ClearValue(UIElement.OpacityMaskProperty);
        });
    }
}
