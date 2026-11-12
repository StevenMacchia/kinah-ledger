using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace KinahLedger.Ui;

/// <summary>
/// A page the player opens (a tab, a shortcut, a link to another page) cross-fades with the one it replaces (2026-11-11,
/// Steven: "cool and flashy"; it rose in alone since 2026-11-10, the old page snapping away): the page being left is kept as
/// a picture over the window and fades out while the new one rises <see cref="TurnRisePx"/> into place as it fades in, and
/// the tab's light slides across meanwhile (Ui/Motion.Glide.cs). The header stays still. Played through Motion.Play, so
/// not in a still render (KL_MOTION_AT holds it for a render of the frame) and not with Windows's animations off.
/// </summary>
public partial class MainWindow
{
    /// <summary>A page turn takes this long; the page being left is gone by this share of it.</summary>
    const double TurnMs = 260, TurnLeaveShare = 0.6;
    /// <summary>How far the new page rises into place.</summary>
    const double TurnRisePx = 8;

    /// <summary>The page turn under way puts everything back with this (a turn started before it ended ends it first).</summary>
    Action? _turnEnd;

    /// <summary>
    /// The page about to be left, as a picture of everything under the header on the window's background, at the screen's
    /// pixels; null with motion off or before the window is laid out. Taken before the new page's parts are shown.
    /// </summary>
    (BitmapSource Picture, Rect Where)? LeavingPage()
    {
        if (!Motion.On || !IsLoaded || LedgerCol.ActualWidth <= 0 || HeadStack.Children.Count == 0 || HeadStack.Children[0] is not FrameworkElement head) return null;
        _turnEnd?.Invoke();
        double top = head.TranslatePoint(new Point(0, head.ActualHeight), LedgerCol).Y;
        // the tables' scroll bar hangs into the window's right padding (LowerScroll's margin), so the picture takes that in too
        var where = new Rect(0, top, LedgerCol.ActualWidth + Root.Padding.Right, Math.Max(0, LedgerCol.ActualHeight - top));
        if (where.Height < 1) return null;
        var dpi = VisualTreeHelper.GetDpi(this);
        double scale = LedgerCol.TransformToAncestor(this) is Transform t ? t.Value.M11 : 1;
        int pw = (int)Math.Ceiling(where.Width * scale * dpi.DpiScaleX), ph = (int)Math.Ceiling(where.Height * scale * dpi.DpiScaleY);
        var shot = new DrawingVisual();
        using (var dc = shot.RenderOpen())
        {
            var size = new Rect(0, 0, where.Width, where.Height);
            dc.DrawRectangle(B("Bg"), null, size);
            dc.DrawRectangle(new VisualBrush(LedgerCol) { Viewbox = where, ViewboxUnits = BrushMappingMode.Absolute, Stretch = Stretch.Fill }, null, size);
        }
        var bmp = new RenderTargetBitmap(pw, ph, 96 * scale * dpi.DpiScaleX, 96 * scale * dpi.DpiScaleY, PixelFormats.Pbgra32);
        bmp.Render(shot);
        bmp.Freeze();
        return (bmp, where);
    }

    /// <summary>
    /// The turn: the picture of the page left fades out over the first <see cref="TurnLeaveShare"/> of it while the new page's
    /// parts (under the header) rise into place and fade in, eased. At its end the picture goes and every part is as it was.
    /// </summary>
    void TurnPage((BitmapSource Picture, Rect Where) leaving)
    {
        var picture = new Image { Source = leaving.Picture, Width = leaving.Where.Width, Height = leaving.Where.Height, Stretch = Stretch.Fill, IsHitTestVisible = false };
        var cover = new Canvas { IsHitTestVisible = false };
        Canvas.SetTop(picture, leaving.Where.Top);
        cover.Children.Add(picture);
        Grid.SetRowSpan(cover, LedgerCol.RowDefinitions.Count); Grid.SetColumnSpan(cover, LedgerCol.ColumnDefinitions.Count);
        LedgerCol.Children.Add(cover);   // a canvas asks for no room, so the page is measured as without it
        var parts = HeadStack.Children.OfType<FrameworkElement>().Skip(1).Append(LeftCol).Append(LowerScroll)
            .Where(e => e.Visibility == Visibility.Visible)
            .Select(e => (El: e, Move: new TranslateTransform(0, TurnRisePx), Opacity: e.Opacity)).ToList();
        foreach (var (el, move, _) in parts) el.RenderTransform = move;
        bool ended = false;
        void End()
        {
            if (ended) return;
            ended = true;
            _turnEnd = null;
            LedgerCol.Children.Remove(cover);
            foreach (var (el, move, opacity) in parts)
            {
                if (ReferenceEquals(el.RenderTransform, move)) el.ClearValue(RenderTransformProperty);
                el.Opacity = opacity;
            }
        }
        _turnEnd = End;
        Motion.Play(LedgerCol, TurnMs, p =>
        {
            double q = Motion.Out(p);
            foreach (var (el, move, opacity) in parts) { move.Y = TurnRisePx * (1 - q); el.Opacity = opacity * q; }
            picture.Opacity = 1 - Motion.Out(p / TurnLeaveShare);
        }, End);
    }
}
