using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Shapes;
using KinahLedger.Session;
using Path = System.Windows.Shapes.Path;

namespace KinahLedger.Ui;

/// <summary>
/// The rate chart under the hero, live (2026-11-11, Steven: "cool and flashy"). When a minute's sample lands, the line runs
/// out to it from where it ended while the older minutes slide left to make room, the scale easing to its new range rather
/// than jumping, and the head dot pops once with a ring that fades; past an hour the oldest minute folds away at the left
/// edge. A pickup that moves the newest minute eases it there; the clock nudging the hour between pickups is set at once.
/// Under the mouse a crosshair marks the nearest minute (a guide down through it, a ring on its point) with a readout of its
/// time and rate, fading in and out quickly. The fill under the line is the look's own
/// gold fading to nothing, and the dashed best-here line rides the same scale. Nothing moves without a change, and with
/// motion off (a render, Windows's animations off) every draw is settled at once. Every course goes through
/// <see cref="Motion.Play"/>, so KL_MOTION_AT holds it for a render.
/// </summary>
sealed class RateChart
{
    /// <summary>A minute landing (the line running out to it, the scale easing) takes this long; the head dot's pop, this long.</summary>
    public const double LandMs = 420, PopMs = 480;

    /// <summary>The crosshair fading in or out under the mouse.</summary>
    public const double HoverFadeMs = 180;

    /// <summary>A change that moves no point further than this, in pixels (the clock moving the hour between pickups), is set without a course.</summary>
    const double EaseFromPx = 1.5;

    /// <summary>The head dot's radius (RateDot in MainWindow.xaml is 10 across), its swell at the top of the pop and how far into the pop that is.</summary>
    const double DotR = 5, PopScale = 1.6, PopPeak = 0.3;

    /// <summary>The ring the pop sends out: its size as it starts and ends, and its opacity as it starts (it fades to nothing).</summary>
    const double RingFrom = 10, RingTo = 30, RingOpacity = 0.7;

    /// <summary>The fill's gold at its top and at <see cref="FillMidAt"/> of the way down, clear at the bottom; a light look takes <see cref="LightFill"/> of it.</summary>
    const byte FillTop = 0x99, FillMid = 0x26;
    const double FillMidAt = 0.55, LightFill = 0.5;

    /// <summary>A look whose background is under this luminance (0 to 255) is a dark one.</summary>
    const double DarkBelow = 128;

    /// <summary>
    /// The look's gold and background, held on the fill by resource reference: a change of look sets them, and the fill is
    /// painted again at once in the new gold (<see cref="Repaint"/>).
    /// </summary>
    static readonly DependencyProperty GoldProperty = DependencyProperty.RegisterAttached("Gold", typeof(Brush), typeof(RateChart), new PropertyMetadata(null, Repaint));
    static readonly DependencyProperty BgProperty = DependencyProperty.RegisterAttached("Bg", typeof(Brush), typeof(RateChart), new PropertyMetadata(null, Repaint));

    /// <summary>The best-here line never sits closer than this to the box's top or bottom, so its label stays in.</summary>
    const double BestInset = 8;

    /// <summary>The best-here label sits this far above its line, or this far below it when the line is under this near the top.</summary>
    const double BestLabelAbove = 15, BestLabelBelow = 3, BestLabelRoom = 22;

    /// <summary>The hover's marker across, and the readout's gap above (or below, near the top) the minute it reads.</summary>
    const double MarkSize = 9, TipGap = 10;

    readonly Canvas _canvas;
    readonly Path _fill;
    readonly Polyline _line;
    readonly Ellipse _dot;
    readonly Line _best;
    readonly TextBlock _bestLbl;
    readonly double _w, _h;
    readonly PathFigure _fillFig = new() { IsClosed = true };
    readonly PolyLineSegment _fillSeg = new() { IsStroked = false };
    readonly ScaleTransform _dotScale = new(1, 1);
    readonly Ellipse _ring = new() { Width = RingFrom, Height = RingFrom, StrokeThickness = 1.5, IsHitTestVisible = false, Visibility = Visibility.Collapsed };
    readonly Canvas _hover = new() { IsHitTestVisible = false, Opacity = 0, Visibility = Visibility.Collapsed };
    readonly Line _guide = new() { StrokeThickness = 1, Opacity = 0.7 };
    readonly Ellipse _mark = new() { Width = MarkSize, Height = MarkSize, StrokeThickness = 2 };
    readonly Border _tip = new() { CornerRadius = new CornerRadius(6), BorderThickness = new Thickness(1), Padding = new Thickness(7, 2, 7, 3) };
    readonly Run _tipTime = new(), _tipRate = new();

    // what is drawn: a point a minute; during a course the minutes that fell off the left lead (_lead of them), folding onto the first
    Point[] _shown = Array.Empty<Point>();
    DateTime[] _minutes = Array.Empty<DateTime>();
    int _lead;
    double _bestY, _bestRate;
    // the last draw's samples (what the readout reads, one per point after the lead) and the course under way: each point's
    // start and end (the lead's end is the first minute), and the settled line it leaves
    (DateTime Minute, double Rate)[] _samples = Array.Empty<(DateTime, double)>();
    Point[] _from = Array.Empty<Point>(), _to = Array.Empty<Point>(), _end = Array.Empty<Point>();
    double _bestFrom, _bestTo;
    bool _wasLive;
    // while the chart is closed: the line and best of the last draw, drawn when it opens (Open)
    (RateLine Line, double Best)? _closed;
    // the minute under the mouse (an index into _samples; -1: none) and where the mouse is across, to pick again after the line moved
    int _hovered = -1;
    double _mouseX;

    public RateChart(Canvas canvas, Path fill, Polyline line, Ellipse dot, Line best, TextBlock bestLbl)
    {
        (_canvas, _fill, _line, _dot, _best, _bestLbl) = (canvas, fill, line, dot, best, bestLbl);
        _w = canvas.Width; _h = canvas.Height;
        _fillFig.Segments.Add(_fillSeg);
        _fill.Data = new PathGeometry(new[] { _fillFig });
        _fill.SetResourceReference(BgProperty, "Bg");
        _fill.SetResourceReference(GoldProperty, "Gold");
        _dot.RenderTransformOrigin = new Point(0.5, 0.5);
        _dot.RenderTransform = _dotScale;
        _ring.SetResourceReference(Shape.StrokeProperty, "Gold");
        canvas.Children.Insert(canvas.Children.IndexOf(dot), _ring);
        _guide.SetResourceReference(Shape.StrokeProperty, "Muted");
        _mark.SetResourceReference(Shape.StrokeProperty, "Gold");
        _mark.SetResourceReference(Shape.FillProperty, "Card");
        _tip.SetResourceReference(Border.BackgroundProperty, "Card2");
        _tip.SetResourceReference(Border.BorderBrushProperty, "LineStrong");
        var text = new TextBlock { FontSize = 11, FontWeight = FontWeights.SemiBold };
        var sep = new Run(" · ");
        var unit = new Run(" /h");
        foreach (var r in new[] { _tipTime, sep, unit }) r.SetResourceReference(TextElement.ForegroundProperty, "Faint");
        _tipRate.SetResourceReference(TextElement.ForegroundProperty, "Ink");
        _tipRate.SetResourceReference(TextElement.FontFamilyProperty, "MonoFont");
        text.Inlines.Add(_tipTime); text.Inlines.Add(sep); text.Inlines.Add(_tipRate); text.Inlines.Add(unit);
        _tip.Child = text;
        foreach (UIElement el in new UIElement[] { _guide, _mark, _tip }) _hover.Children.Add(el);
        canvas.Children.Add(_hover);
        // the box's empty part takes the mouse too, so the crosshair follows anywhere over the chart
        canvas.Background ??= Brushes.Transparent;
        canvas.MouseMove += (_, e) => { _mouseX = e.GetPosition(_canvas).X; Hover(Nearest(_mouseX)); };
        canvas.MouseLeave += (_, _) => Unhover();
    }

    /// <summary>
    /// Draws the line for <paramref name="line"/>'s samples (two or more), with <paramref name="best"/> (0: none) as the dashed
    /// line on the same scale. <paramref name="live"/>: the chart is open. A draw that follows an open one moves to the new
    /// samples (a minute landed, a pickup, the scale changed); the first one after the chart opened is set as it is, since the
    /// chart draws itself in (<see cref="Motion.Reveal"/>). A closed chart draws nothing until it opens (<see cref="Open"/>),
    /// and an open one whose samples didn't change (the same count, first and last) does nothing at all.
    /// </summary>
    public void Draw(RateLine line, double best, bool live)
    {
        if (!live) { Close(line, best); return; }
        var now = line.Samples;
        if (_wasLive && now.Count == _samples.Length && best == _bestRate && now[0] == _samples[0] && now[^1] == _samples[^1]) return;
        var samples = now.ToArray();
        var pts = line.Points(_w, _h).Select(p => new Point(p.X, p.Y)).ToArray();
        var minutes = samples.Select(s => s.Minute).ToArray();
        double lo = samples.Min(s => s.Rate), hi = samples.Max(s => s.Rate);
        bool hasBest = best > 0 && hi - lo > 1e-9;
        // the same mapping as RateLine.Points: the lowest sample 2 px off the bottom, the highest 2 px off the top
        double bestY = hasBest ? Math.Clamp(_h - 2 - (_h - 4) * (best - lo) / (hi - lo), BestInset, _h - BestInset) : _bestY;
        bool bestNew = hasBest && _best.Visibility != Visibility.Visible;
        _best.Visibility = _bestLbl.Visibility = hasBest ? Visibility.Visible : Visibility.Collapsed;
        if (hasBest) _bestLbl.Text = "best here " + Amounts.Short(best);
        _bestRate = best;
        bool moving = _wasLive && Motion.On;
        _wasLive = true;
        _samples = samples;
        if (bestNew) _bestY = bestY;
        if (!moving || !Plan(minutes, pts, out var from, out var to, out int dropped, out bool added)) { Settle(pts, minutes, bestY); return; }
        // a course under way toward these same minutes: it ends where this draw says (a tick's nudge), or starts again from
        // where it is when this draw moved them further (a pickup mid-course)
        if (Motion.Playing(_line) && !added && dropped == _lead && Apart(_to, to, _bestTo, bestY) < EaseFromPx)
        {
            _to = to; _end = pts; _bestTo = bestY;
            return;
        }
        if (!added && Apart(from, to, _bestY, bestY) < EaseFromPx) { Settle(pts, minutes, bestY); return; }
        _from = from; _to = to; _end = pts;
        _bestFrom = _bestY; _bestTo = bestY;
        _shown = (Point[])from.Clone();
        _minutes = _minutes[..dropped].Concat(minutes).ToArray();
        _lead = dropped;
        Motion.Play(_line, LandMs, p =>
        {
            double q = Motion.Out(p);
            for (int i = 0; i < _shown.Length; i++) _shown[i] = new Point(_from[i].X + (_to[i].X - _from[i].X) * q, _from[i].Y + (_to[i].Y - _from[i].Y) * q);
            _bestY = _bestFrom + (_bestTo - _bestFrom) * q;
            Apply();
        }, () => Settle(_end, _minutes[_lead..], _bestTo));
        if (added) Pop();
    }

    /// <summary>
    /// How the line drawn now moves to the new points: each minute from where it is drawn to its new place, a new minute
    /// from where the line ended, a minute that fell off the left folding onto the first. False when the new samples don't
    /// carry on the ones drawn (another count, more than one new minute at once): that line is set as it is.
    /// </summary>
    bool Plan(DateTime[] minutes, Point[] pts, out Point[] from, out Point[] to, out int dropped, out bool added)
    {
        from = to = Array.Empty<Point>(); added = false;
        dropped = Array.IndexOf(_minutes, minutes[0]);
        if (dropped < 0) return false;
        int kept = _minutes.Length - dropped;
        if (kept > minutes.Length || minutes.Length - kept > 1) return false;
        for (int i = 0; i < kept; i++) if (_minutes[dropped + i] != minutes[i]) return false;
        added = minutes.Length > kept;
        int m = dropped + minutes.Length;
        from = new Point[m]; to = new Point[m];
        for (int i = 0; i < dropped; i++) { from[i] = _shown[i]; to[i] = pts[0]; }
        for (int i = 0; i < kept; i++) { from[dropped + i] = _shown[dropped + i]; to[dropped + i] = pts[i]; }
        if (added) { from[m - 1] = _shown[^1]; to[m - 1] = pts[^1]; }
        return true;
    }

    /// <summary>The furthest any point (or the best-here line) is from its place in the other set, in pixels.</summary>
    static double Apart(Point[] a, Point[] b, double bestA, double bestB)
    {
        if (a.Length != b.Length) return double.MaxValue;
        double most = Math.Abs(bestA - bestB);
        for (int i = 0; i < a.Length; i++) most = Math.Max(most, Math.Max(Math.Abs(a[i].X - b[i].X), Math.Abs(a[i].Y - b[i].Y)));
        return most;
    }

    /// <summary>
    /// The line drawn settled on these points (no course under way), the crosshair picked again under the mouse. A tick that
    /// leaves every point where it was draws nothing again, so an open chart with nothing happening costs nothing.
    /// </summary>
    void Settle(Point[] pts, DateTime[] minutes, double bestY)
    {
        bool same = !Motion.Playing(_line) && _lead == 0 && _minutes.SequenceEqual(minutes) && Apart(_shown, pts, _bestY, bestY) == 0;
        Motion.Halt(_line);
        _shown = pts; _minutes = minutes; _lead = 0; _bestY = bestY;
        if (same) { if (_hovered >= 0) PlaceHover(); return; }
        Apply();
        if (_hovered >= 0) Hover(Nearest(_mouseX));
    }

    /// <summary>What is shown, put on the screen: the line's and the fill's points (in place, so a course allocates nothing a frame), the head dot, its ring, the best-here line and the crosshair.</summary>
    void Apply()
    {
        int n = _shown.Length;
        var linePts = _line.Points;
        if (linePts == null || linePts.IsFrozen || linePts.Count != n) _line.Points = new PointCollection(_shown);
        else for (int i = 0; i < n; i++) linePts[i] = _shown[i];
        var fillPts = _fillSeg.Points;
        if (fillPts.Count != n + 1) { fillPts = new PointCollection(n + 1); for (int i = 0; i <= n; i++) fillPts.Add(default); _fillSeg.Points = fillPts; }
        for (int i = 0; i < n; i++) fillPts[i] = _shown[i];
        var last = _shown[^1];
        fillPts[n] = new Point(last.X, _h);
        _fillFig.StartPoint = new Point(_shown[0].X, _h);
        _dot.Visibility = Visibility.Visible;
        Canvas.SetLeft(_dot, HeadX() - DotR); Canvas.SetTop(_dot, last.Y - DotR);
        PlaceRing();
        _best.Y1 = _best.Y2 = _bestY;
        Canvas.SetTop(_bestLbl, _bestY < BestLabelRoom ? _bestY + BestLabelBelow : _bestY - BestLabelAbove);
        if (_hovered >= 0) PlaceHover();
    }

    /// <summary>The head dot's centre across: the newest minute's, kept inside the box at the right edge.</summary>
    double HeadX() => Math.Min(_shown[^1].X, _w - DotR);

    /// <summary>The fill in the look's gold, from soft at the line to clear at the bottom (softer on a light look); painted again when the look changes.</summary>
    static void Repaint(DependencyObject d, DependencyPropertyChangedEventArgs _)
    {
        if (d is not Shape fill || fill.GetValue(GoldProperty) is not SolidColorBrush goldBrush) return;
        var gold = goldBrush.Color;
        var bg = (fill.GetValue(BgProperty) as SolidColorBrush)?.Color ?? Colors.Black;
        bool dark = 0.299 * bg.R + 0.587 * bg.G + 0.114 * bg.B < DarkBelow;
        double k = dark ? 1 : LightFill;
        var g = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(0, 1) };
        g.GradientStops.Add(new GradientStop(Color.FromArgb((byte)(FillTop * k), gold.R, gold.G, gold.B), 0));
        g.GradientStops.Add(new GradientStop(Color.FromArgb((byte)(FillMid * k), gold.R, gold.G, gold.B), FillMidAt));
        g.GradientStops.Add(new GradientStop(Color.FromArgb(0, gold.R, gold.G, gold.B), 1));
        g.Freeze();
        fill.Fill = g;
    }

    /* ---------------- the head dot's pop ---------------- */

    /// <summary>A minute landed: the head dot swells to <see cref="PopScale"/> and settles, and a ring goes out from it and fades, once.</summary>
    void Pop()
    {
        _ring.Visibility = Visibility.Visible;
        Motion.Play(_dot, PopMs, p =>
        {
            double s = p < PopPeak ? 1 + (PopScale - 1) * Motion.Out(p / PopPeak) : PopScale - (PopScale - 1) * Motion.Out((p - PopPeak) / (1 - PopPeak));
            _dotScale.ScaleX = _dotScale.ScaleY = s;
            _ring.Width = _ring.Height = RingFrom + (RingTo - RingFrom) * Motion.Out(p);
            _ring.Opacity = RingOpacity * (1 - p);
            PlaceRing();
        }, () => { _dotScale.ScaleX = _dotScale.ScaleY = 1; _ring.Visibility = Visibility.Collapsed; });
    }

    /// <summary>The ring centred on the head dot (it follows the dot while the line moves).</summary>
    void PlaceRing()
    {
        if (_ring.Visibility != Visibility.Visible || _shown.Length == 0) return;
        double r = _ring.Width / 2;
        Canvas.SetLeft(_ring, HeadX() - r); Canvas.SetTop(_ring, _shown[^1].Y - r);
    }

    /* ---------------- the crosshair ---------------- */

    /// <summary>The minute nearest <paramref name="x"/> across the box (an index into the samples), or -1 with no line.</summary>
    int Nearest(double x)
    {
        int best = -1;
        for (int i = _lead; i < _shown.Length; i++)
            if (best < 0 || Math.Abs(_shown[i].X - x) < Math.Abs(_shown[best + _lead].X - x)) best = i - _lead;
        return best;
    }

    /// <summary>For a render and the self-test: the mouse as if on the <paramref name="i"/>th minute.</summary>
    internal void HoverAt(int i)
    {
        if (i >= 0 && i < _samples.Length) _mouseX = _shown[_lead + i].X;
        Hover(i);
    }

    /// <summary>The crosshair on the <paramref name="i"/>th minute with its time and rate, fading in if it wasn't up (-1: away).</summary>
    void Hover(int i)
    {
        if (i < 0 || i >= _samples.Length) { Unhover(); return; }
        // the mouse moving along the same minute: nothing to do (a draw keeps it up to date)
        if (i == _hovered) return;
        bool was = _hovered >= 0;
        _hovered = i;
        PlaceHover();
        if (!was) Fade(1);
    }

    /// <summary>The crosshair fades away (the mouse left the chart).</summary>
    void Unhover()
    {
        if (_hovered < 0) return;
        _hovered = -1;
        Fade(0);
    }

    /// <summary>The crosshair gone at once (the chart closed, or its line went).</summary>
    void HideHover()
    {
        _hovered = -1;
        Motion.Halt(_hover);
        _hover.Opacity = 0; _hover.Visibility = Visibility.Collapsed;
    }

    /// <summary>The crosshair's guide, ring and readout on the minute under the mouse; the readout above it (below, near the top), inside the box.</summary>
    void PlaceHover()
    {
        int i = Math.Min(_hovered, _samples.Length - 1);
        var p = _shown[_lead + i];
        var s = _samples[i];
        _tipTime.Text = s.Minute.ToLocalTime().ToString("HH:mm", CultureInfo.InvariantCulture);
        _tipRate.Text = Amounts.Short(s.Rate);
        _guide.X1 = _guide.X2 = p.X; _guide.Y1 = 0; _guide.Y2 = _h;
        Canvas.SetLeft(_mark, p.X - MarkSize / 2); Canvas.SetTop(_mark, p.Y - MarkSize / 2);
        _tip.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        var size = _tip.DesiredSize;
        double top = p.Y - size.Height - TipGap;
        Canvas.SetLeft(_tip, Math.Clamp(p.X - size.Width / 2, 0, Math.Max(0, _w - size.Width)));
        Canvas.SetTop(_tip, top >= 0 ? top : p.Y + TipGap);
    }

    /// <summary>The crosshair fading to <paramref name="to"/> (1 shown, 0 gone) from where it is; at once with motion off.</summary>
    void Fade(double to)
    {
        double from = _hover.Opacity;
        _hover.Visibility = Visibility.Visible;
        void End() { _hover.Opacity = to; if (to == 0) _hover.Visibility = Visibility.Collapsed; }
        if (!Motion.On) { Motion.Halt(_hover); End(); return; }
        Motion.Play(_hover, HoverFadeMs, p => _hover.Opacity = from + (to - from) * Motion.Out(p), End);
    }

    /* ---------------- the line going ---------------- */

    /// <summary>No line to draw (under two samples): everything under way stops, and the next line is drawn as it is.</summary>
    public void Forget()
    {
        if (_shown.Length == 0 && !_wasLive && _closed == null) return;
        Motion.Halt(_line); StopPop(); HideHover();
        _shown = Array.Empty<Point>(); _minutes = Array.Empty<DateTime>(); _lead = 0;
        _samples = Array.Empty<(DateTime, double)>();
        _wasLive = false; _closed = null;
    }

    /// <summary>The chart closed: whatever was under way stops, and the line waits to be drawn as it is when it opens again.</summary>
    void Close(RateLine line, double best)
    {
        _closed = (line, best);
        if (!_wasLive) return;
        _wasLive = false;
        Motion.Halt(_line); StopPop(); HideHover();
    }

    /// <summary>The chart opening (a click on "chart"): its line drawn as it is now, before it draws itself in.</summary>
    public void Open()
    {
        if (_closed is not { } c) return;
        _closed = null;
        if (c.Line.Samples.Count >= 2) Draw(c.Line, c.Best, live: true);
    }

    /// <summary>The head dot's pop stopped: the dot at rest, the ring gone.</summary>
    void StopPop()
    {
        Motion.Halt(_dot);
        _dotScale.ScaleX = _dotScale.ScaleY = 1;
        _ring.Visibility = Visibility.Collapsed;
    }

    /// <summary>For a render of the motion: the line as if it had ended a minute earlier and its newest minute just landed.</summary>
    internal void ReplayLanding(RateLine line)
    {
        if (line.Samples.Count < 3) return;
        var before = new RateLine();
        foreach (var (minute, rate) in line.Samples.SkipLast(1)) before.Add(minute, rate);
        Draw(before, _bestRate, live: true);
        Draw(line, _bestRate, live: true);
    }

    /// <summary>For the self-test: the readout as drawn, its runs one after the other.</summary>
    internal string Readout => string.Concat(((TextBlock)_tip.Child).Inlines.OfType<Run>().Select(r => r.Text));

    /// <summary>For the self-test: whether the crosshair is up, and where across it stands.</summary>
    internal (bool Shown, double X) Crosshair => (_hover.Visibility == Visibility.Visible && _hover.Opacity > 0, _guide.X1);

    /// <summary>For the self-test: the head dot's scale (1 at rest) and whether the pop's ring is out.</summary>
    internal (double Scale, bool Ring) Head => (_dotScale.ScaleX, _ring.Visibility == Visibility.Visible);
}
