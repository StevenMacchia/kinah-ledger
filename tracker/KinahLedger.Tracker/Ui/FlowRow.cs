using System.Windows;
using System.Windows.Controls;

namespace KinahLedger.Ui;

/// <summary>
/// A row of items laid left to right with a gap, each centred on its line (the Parser page's head and its fight strip,
/// 2026-11-18). What doesn't fit: an item marked <see cref="DropProperty"/> is left out (drawn at no size); otherwise, with
/// <see cref="Wrap"/>, it starts a new line, and without it, it gets the room left (a trimming text ends in "…"). An item
/// marked <see cref="RightProperty"/> starts the line's right-hand part: it and the items after it on that line sit against
/// the right edge.
/// </summary>
sealed class FlowRow : Panel
{
    public double Gap { get; set; } = 12;
    public double LineGap { get; set; } = 6;
    public bool Wrap { get; set; }

    public static readonly DependencyProperty DropProperty = DependencyProperty.RegisterAttached("Drop", typeof(bool), typeof(FlowRow), new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsParentMeasure));
    public static readonly DependencyProperty RightProperty = DependencyProperty.RegisterAttached("Right", typeof(bool), typeof(FlowRow), new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsParentArrange));
    public static T Drop<T>(T el) where T : UIElement { el.SetValue(DropProperty, true); return el; }
    public static T Right<T>(T el) where T : UIElement { el.SetValue(RightProperty, true); return el; }

    /// <summary>Where each item went in the last measure: its line, its left edge and its size; a left-out item has line -1.</summary>
    readonly List<(int Line, double X, Size Size)> _at = new();
    readonly List<double> _lineH = new();

    /// <summary>The items left out at the last measure (for the checks: a hint dropped for want of room).</summary>
    public IEnumerable<UIElement> Dropped => InternalChildren.Cast<UIElement>().Where((c, i) => i < _at.Count && _at[i].Line < 0);

    protected override Size MeasureOverride(Size avail)
    {
        _at.Clear(); _lineH.Clear();
        double max = avail.Width, x = 0, widest = 0;
        int line = 0;
        _lineH.Add(0);
        foreach (UIElement c in InternalChildren)
        {
            if (c.Visibility == Visibility.Collapsed) { _at.Add((-1, 0, default)); continue; }
            c.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            var d = c.DesiredSize;
            double gap = x > 0 ? Gap : 0;
            if (x + gap + d.Width > max + 0.5)
            {
                if ((bool)c.GetValue(DropProperty)) { c.Measure(new Size(0, 0)); _at.Add((-1, 0, default)); continue; }
                if (Wrap && x > 0) { line++; _lineH.Add(0); x = 0; gap = 0; }
                if (x + gap + d.Width > max + 0.5)
                {
                    // the room left: a trimming text ends in "…"
                    double room = Math.Max(0, max - x - gap);
                    c.Measure(new Size(room, double.PositiveInfinity));
                    d = new Size(Math.Min(room, c.DesiredSize.Width), c.DesiredSize.Height);
                }
            }
            _at.Add((line, x + gap, d));
            x += gap + d.Width;
            widest = Math.Max(widest, x);
            _lineH[line] = Math.Max(_lineH[line], d.Height);
        }
        double h = _lineH.Sum() + LineGap * (_lineH.Count - 1);
        return new Size(double.IsPositiveInfinity(max) ? widest : Math.Min(widest, max), h);
    }

    protected override Size ArrangeOverride(Size final)
    {
        var kids = InternalChildren.Cast<UIElement>().ToList();
        // each line's right-hand part: from its first item marked Right to its end, against the right edge
        var shift = new double[_lineH.Count];
        var from = Enumerable.Repeat(-1, _lineH.Count).ToArray();
        for (int i = 0; i < kids.Count && i < _at.Count; i++)
        {
            var (l, x, s) = _at[i];
            if (l < 0) continue;
            if (from[l] < 0 && (bool)kids[i].GetValue(RightProperty)) from[l] = i;
            if (from[l] >= 0) shift[l] = Math.Max(0, final.Width - (x + s.Width));
        }
        double y0 = 0;
        var tops = new double[_lineH.Count];
        for (int l = 0; l < _lineH.Count; l++) { tops[l] = y0; y0 += _lineH[l] + LineGap; }
        for (int i = 0; i < kids.Count; i++)
        {
            if (i >= _at.Count || _at[i].Line < 0) { kids[i].Arrange(new Rect(0, 0, 0, 0)); continue; }
            var (l, x, s) = _at[i];
            double dx = from[l] >= 0 && i >= from[l] ? shift[l] : 0;
            kids[i].Arrange(new Rect(x + dx, tops[l] + (_lineH[l] - s.Height) / 2, s.Width, s.Height));
        }
        return final;
    }
}
