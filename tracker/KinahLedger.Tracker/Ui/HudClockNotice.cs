using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using static KinahLedger.Ui.HudKit;

namespace KinahLedger.Ui;

/// <summary>
/// The That was wrong notice's parts (2026-10-26, mock f3-mark-wrong.png), for the Card and the Ticker: a gold clock,
/// "Saved the last minute" in gold with " · Report in tracker" muted, and a thin gold line under it that runs down over
/// the notice's 3 s. The panel's "noticeClock" value says when it ends ("&lt;end ticks&gt;/&lt;seconds&gt;", from
/// Ui/MainWindow.Notices.cs); a layout render passes "@0.58" for a still line at 58%. Empty: an ordinary notice.
/// </summary>
static class HudClockNotice
{
    /// <summary>The clock the notice starts with (16 units, gold), drawn once.</summary>
    public static readonly DrawingImage Clock = MakeClock();
    static DrawingImage MakeClock()
    {
        var gold = Frozen(Rgb(GoldC));
        var g = new DrawingGroup();
        g.Children.Add(new GeometryDrawing(Brushes.Transparent, null, new RectangleGeometry(new Rect(0, 0, 16, 16))));
        g.Children.Add(new GeometryDrawing(Frozen(Rgb(GoldC, .12)), new Pen(gold, 1.4), new EllipseGeometry(new Point(8, 8), 6.3, 6.3)));
        g.Children.Add(new GeometryDrawing(null, new Pen(gold, 1.5) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round }, Geometry.Parse("M8,4.4 L8,8 L10.7,9.7")));
        var img = new DrawingImage(g); img.Freeze();
        return img;
    }

    public static Image ClockIcon() => new() { Source = Clock, Width = 15, Height = 15, VerticalAlignment = VerticalAlignment.Center };

    /// <summary>How much of the line is left (1 to 0) and the seconds it has to run; null when the value is not a clock notice's.</summary>
    public static (double Left, double Seconds)? Parse(string value)
    {
        if (string.IsNullOrEmpty(value)) return null;
        if (value[0] == '@')
            return double.TryParse(value[1..], NumberStyles.Float, CultureInfo.InvariantCulture, out var f) ? (Math.Clamp(f, 0, 1), 0) : null;
        var parts = value.Split('/');
        if (parts.Length != 2 || !long.TryParse(parts[0], out var end) || !double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var total) || total <= 0) return null;
        var secs = (new DateTime(end, DateTimeKind.Utc) - DateTime.UtcNow).TotalSeconds;
        return (Math.Clamp(secs / total, 0, 1), Math.Max(0, secs));
    }

    /// <summary>The words: up to " · " in gold and semibold, the rest muted ("Saved the last minute" · "Report in tracker").</summary>
    public static void Words(TextBlock t, string text, double size)
    {
        t.Text = "";
        int cut = text.IndexOf(" · ", StringComparison.Ordinal);
        t.Inlines.Add(R(cut < 0 ? text : text[..cut], size, Gold, weight: FontWeights.SemiBold));
        if (cut >= 0) t.Inlines.Add(R(text[cut..], size, Muted));
    }

    static readonly Brush DrainBrush = MakeDrain();
    static Brush MakeDrain()
    {
        var b = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 0) };
        b.GradientStops.Add(new GradientStop(Rgb(GoldC, .85), 0));
        b.GradientStops.Add(new GradientStop(Rgb(GoldC, .35), 1));
        b.Freeze();
        return b;
    }

    /// <summary>The thin gold line under the words, the notice's full width; <see cref="Run"/> sets it going.</summary>
    public static Border Drain() => new()
    {
        Height = 2, CornerRadius = new CornerRadius(1), Background = DrainBrush, VerticalAlignment = VerticalAlignment.Bottom,
        HorizontalAlignment = HorizontalAlignment.Stretch, IsHitTestVisible = false, RenderTransform = new ScaleTransform(1, 1),
    };

    /// <summary>The line at what is left of the notice, running down to nothing over the seconds left (still in a render).</summary>
    public static void Run(Border drain, (double Left, double Seconds) clock, bool still)
    {
        if (drain.RenderTransform is not ScaleTransform s || s.IsFrozen) drain.RenderTransform = s = new ScaleTransform(1, 1);
        s.BeginAnimation(ScaleTransform.ScaleXProperty, null);
        s.ScaleX = clock.Left;
        if (!still && clock.Seconds > 0) s.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(clock.Left, 0, TimeSpan.FromSeconds(clock.Seconds)));
    }
}
