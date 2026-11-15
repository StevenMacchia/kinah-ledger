using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace KinahLedger.Ui;

/// <summary>
/// A wider meter (Steven, 2026-11-14: "maybe an option to make it wider?"): while the overlay can be moved, dragging the
/// meter's left or right edge widens or narrows it, a plain drag like moving it. The width is one figure for every look
/// (<see cref="HudStackLook.FightWidth"/>, 300 to 640 at 100%), remembered in parser.json, and also set by Settings › In
/// game › Parser › Width. Dragging the left edge keeps the right edge where it was.
/// </summary>
public sealed partial class HudWindow
{
    /// <summary>The player let go of an edge: the meter's new width at 100% (MainWindow keeps it).</summary>
    public event Action<double>? FightWidthSet;

    /// <summary>-1 while the left edge is held, 1 the right, 0 none.</summary>
    int _edge;
    double _edgeX0, _edgeW0, _edgeLeft0;
    const double EdgeGrip = 7;

    /// <summary>Called at the end of the constructor.</summary>
    void AddWidthDrag()
    {
        // ahead of the window's own drag (DragMove): a press on an edge widens instead of moving
        PreviewMouseLeftButtonDown += (_, e) =>
        {
            int edge = EdgeAt(e.GetPosition(this));
            if (edge == 0) return;
            _edge = edge; _edgeX0 = ScreenX(e); _edgeW0 = HudStackLook.FightWidth; _edgeLeft0 = Left;
            CaptureMouse();
            e.Handled = true;
        };
        PreviewMouseMove += (_, e) =>
        {
            if (_edge == 0) { Cursor = EdgeAt(e.GetPosition(this)) != 0 ? Cursors.SizeWE : null; return; }
            double scale = ContentScale;
            double w = Math.Clamp(Math.Round(_edgeW0 + _edge * (ScreenX(e) - _edgeX0) / scale), HudStackLook.FightWidthMin, HudStackLook.FightWidthMax);
            e.Handled = true;
            if (w == HudStackLook.FightWidth) return;
            HudStackLook.FightWidth = w;
            ApplyFightWidth();
            if (_edge < 0) Left = _edgeLeft0 - (w - _edgeW0) * scale;   // the right edge stays put
        };
        PreviewMouseLeftButtonUp += (_, e) =>
        {
            if (_edge == 0) return;
            e.Handled = true;
            EndEdge();
            ReleaseMouseCapture();
        };
        LostMouseCapture += (_, _) => EndEdge();
    }

    void EndEdge()
    {
        if (_edge == 0) return;
        bool moved = _edge < 0 && Math.Abs(Left - _edgeLeft0) > 0.5;
        _edge = 0;
        Cursor = null;
        if (HudStackLook.FightWidth != _edgeW0) FightWidthSet?.Invoke(HudStackLook.FightWidth);
        if (moved) Dragged?.Invoke();   // the left edge moved the overlay: its new spot is remembered as a drag's
    }

    /// <summary>The meter drawn again at <see cref="HudStackLook.FightWidth"/> (an edge dragged, the Width slider moved).</summary>
    public void ApplyFightWidth()
    {
        (_look as HudStackLook)?.ApplyWidth();
        if (_parserPanel != null) _parserPanel.Width = HudStackLook.FightWidth + 10;
    }

    /// <summary>For the options test: the meter's frame (what the edges widen).</summary>
    internal FrameworkElement? FightFrameForTest => (_look as HudStackLook)?.FightFrame;

    /// <summary>The overlay's size (Options › In game › Size): the meter's width is counted at 100%.</summary>
    double ContentScale => _content.LayoutTransform is ScaleTransform s && s.ScaleX > 0 ? s.ScaleX : 1;

    double ScreenX(MouseEventArgs e)
    {
        var p = PointToScreen(e.GetPosition(this));
        return p.X / VisualTreeHelper.GetDpi(this).DpiScaleX;
    }

    /// <summary>-1 over the meter's left edge, 1 over its right, 0 elsewhere (or while no meter shows).</summary>
    int EdgeAt(Point p)
    {
        var frame = (_look as HudStackLook)?.FightFrame
                    ?? (_look is not HudStackLook && _parserSlot.Visibility == Visibility.Visible ? _parserSlot.Child as FrameworkElement : null);
        if (frame is not { IsVisible: true } || frame.ActualWidth < 1) return 0;
        try
        {
            var t = frame.TransformToAncestor(this);
            var tl = t.Transform(new Point(0, 0));
            var br = t.Transform(new Point(frame.ActualWidth, frame.ActualHeight));
            if (p.Y < tl.Y || p.Y > br.Y) return 0;
            // inside the frame: its see-through margin lets clicks go to the game
            if (p.X >= tl.X && p.X <= tl.X + EdgeGrip) return -1;
            if (p.X <= br.X && p.X >= br.X - EdgeGrip) return 1;
        }
        catch (InvalidOperationException) { }
        return 0;
    }
}
