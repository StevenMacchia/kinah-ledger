namespace KinahLedger.Ui;

/// <summary>
/// The easing the Overdrive looks (and Arcade, a ModernLook too) share for their moments (2026-11-21, "take the animation to
/// the next level while keeping it light"): every curve here is a number a moment's course sets a transform or an opacity to.
/// </summary>
static class Ez
{
    /// <summary>Accelerating in (a cubic).</summary>
    public static double In(double p) { p = Math.Clamp(p, 0, 1); return p * p * p; }

    /// <summary>Slow, fast, slow (a cubic both ways).</summary>
    public static double InOut(double p) { p = Math.Clamp(p, 0, 1); return p < .5 ? 4 * p * p * p : 1 - Math.Pow(-2 * p + 2, 3) / 2; }

    /// <summary>A decaying wobble: <paramref name="cycles"/> swings over the course, dying away at <paramref name="decay"/>.</summary>
    public static double Wob(double t, double cycles, double decay) => Math.Sin(t * cycles * 2 * Math.PI) * Math.Exp(-decay * t);

    /// <summary>A hit's jolt over its part of the course (0 to 1): <paramref name="n"/> half swings dying to rest, at most <paramref name="amp"/>.</summary>
    public static double Jolt(double s, double amp, double n = 5) => s <= 0 || s >= 1 ? 0 : amp * Math.Sin(s * n * Math.PI) * (1 - s);

    /// <summary>
    /// A moment's stage round a chip (a Border): the chip's words move into a Grid with a zero-size canvas at its middle under
    /// them, where the moment lays its pieces (drawn past the chip's edges, never laid out); the returned action puts the
    /// words back as they were. The chip's size and place do not change.
    /// </summary>
    public static (System.Windows.Controls.Canvas Mid, Action Restore) Stage(System.Windows.Controls.Border chip, bool over = false)
    {
        var words = chip.Child;
        var mid = new System.Windows.Controls.Canvas { Width = 0, Height = 0, IsHitTestVisible = false, HorizontalAlignment = System.Windows.HorizontalAlignment.Center, VerticalAlignment = System.Windows.VerticalAlignment.Center };
        chip.Child = null;
        var inner = new System.Windows.Controls.Border { Padding = chip.Padding, Child = words };
        var g = new System.Windows.Controls.Grid { Margin = new System.Windows.Thickness(-chip.Padding.Left, -chip.Padding.Top, -chip.Padding.Right, -chip.Padding.Bottom) };
        if (over) { g.Children.Add(inner); g.Children.Add(mid); } else { g.Children.Add(mid); g.Children.Add(inner); }
        chip.Child = g;
        return (mid, () => { if (ReferenceEquals(chip.Child, g)) { inner.Child = null; chip.Child = words; } });
    }

    /// <summary>A pulse: 0 to 1 and back over the part, its peak at <paramref name="peak"/>.</summary>
    public static double Pulse(double s, double peak = .3) => s <= 0 || s >= 1 ? 0 : s < peak ? Motion.Out(s / peak) : 1 - (s - peak) / (1 - peak);
}
