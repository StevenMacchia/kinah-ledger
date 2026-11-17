using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Shapes;

namespace KinahLedger.Ui;

/// <summary>
/// The meter's big moments, directed (Steven, 2026-11-16: "improving the animations for catching fire / the post fight
/// animations… production quality like a damn movie"). Two of them:
///
/// The finale, when a fight ends seen live: a bright blade of light cuts across the rows with a soft trail, the meter takes a
/// small punch (a kill only), a shine runs down the bars top to bottom, then the TOP DPS / TOP HEALS plates drop in and glint,
/// and last, on its own beat, a personal best: the PB chip drops in, gold sparks burst off it, your DPS flares gold and your
/// frame pulses. Each beat waits for the one before, so nothing lands on anything else.
///
/// The fire, while your pace beats your best here: it ignites (a ring of flame light flashes round your row and embers burst
/// off it), then embers keep rising off your row, its foot line flickers like firelight, and your rank chip burns.
///
/// Cheap by design (the meter must never lag): particles are small gradient dots with no effects, at most
/// <see cref="EmbersMax"/> alive, spawned on the meter's own draws (no timer of its own), and none at all with motion off. A
/// render holds every course at its frame (Motion.FrozenAt), the embers at fixed places.
/// </summary>
public sealed partial class ParserRows
{
    readonly Canvas _fx = new() { IsHitTestVisible = false, ClipToBounds = false };   // embers may rise past the top row into the header
    int _embers;
    DateTime _lastEmber;
    static readonly Random Rng = new();

    /// <summary>The finale's beats, in ms from the fight's end: the blade, the shine down the bars (each row this much after the one above), the plates, the PB.</summary>
    public const double BladeMs = 560, ShineAt = 140, ShineStep = 70, ShineMs = 420, PlatesAt = 420, PbAt = 900, FinaleMs = 1700;
    /// <summary>Embers alive at most, and the least time between two.</summary>
    public const int EmbersMax = 9;
    public const double EmberEveryMs = 120;

    static readonly Color Flame = Color.FromRgb(0xFF, 0x8A, 0x2A), Ember = Color.FromRgb(0xFF, 0xD8, 0x7A), Spark = Color.FromRgb(0xFF, 0xE6, 0xA0);

    /* ---------------- the finale ---------------- */

    /// <summary>The fight just ended, seen live: the blade, the punch (a kill), the shine down the bars. The plates and the PB follow on their own beats.</summary>
    void Finale(string tone)
    {
        if (!Motion.On) return;
        Sweep(tone);
        if (tone == "good") Punch();
        int i = 0;
        foreach (var r in _rows.Values.Where(x => x.Root.Visibility == Visibility.Visible).OrderBy(x => x.Y)) Shine(r, i++, tone);
    }

    /// <summary>A kill's punch: the rows push out a touch and settle (a quick 1.4% and back), as a hit lands.</summary>
    void Punch()
    {
        var st = new ScaleTransform(1, 1);
        _host.RenderTransformOrigin = new Point(0.5, 0.5);
        _host.RenderTransform = st;
        Motion.Play(_host, 300, p =>
        {
            double k = p < 0.25 ? Motion.Out(p / 0.25) : 1 - Motion.Out((p - 0.25) / 0.75);
            st.ScaleX = st.ScaleY = 1 + 0.014 * k;
        }, () => { st.ScaleX = st.ScaleY = 1; });
    }

    /// <summary>A shine across one row's bar, its turn in the run down the meter (row <paramref name="i"/>).</summary>
    void Shine(RowEl r, int i, string tone)
    {
        double w = r.Track.ActualWidth, h = _rowH - 2;
        if (w <= 0) return;
        var c = tone == "bad" ? PK.Coral : tone == "good" ? Color.FromRgb(0xE8, 0xFF, 0xF4) : Colors.White;
        var band = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 0) };
        GradientStop a = new(Color.FromArgb(0, c.R, c.G, c.B), 0), m = new(Color.FromArgb(0, c.R, c.G, c.B), 0), b = new(Color.FromArgb(0, c.R, c.G, c.B), 0);
        band.GradientStops.Add(a); band.GradientStops.Add(m); band.GradientStops.Add(b);
        var shine = new Border { Width = w, Height = h, CornerRadius = new CornerRadius(3), Background = band, IsHitTestVisible = false, Tag = "finale-shine" };
        Canvas.SetLeft(shine, 0); Canvas.SetTop(shine, r.Y);
        _fx.Children.Add(shine);
        double start = ShineAt + ShineStep * i, total = start + ShineMs;
        Motion.Play(shine, total, p =>
        {
            double l = Math.Clamp((p * total - start) / ShineMs, 0, 1);
            double at = -0.25 + 1.5 * Motion.Out(l);
            a.Offset = at - 0.18; m.Offset = at; b.Offset = at + 0.06;   // a soft lead and a crisp tail: it moves
            m.Color = Color.FromArgb((byte)(0x90 * Math.Sin(Math.PI * l)), c.R, c.G, c.B);
        }, () => _fx.Children.Remove(shine));
    }

    /// <summary>
    /// A plate's entrance on the finale's beat: it waits for the blade, drops onto its place from above with a stamp (down from
    /// a touch larger, never wider than its room once it lands), then a glint runs over it.
    /// </summary>
    static void PlateIn(Border plate, Border glint, GradientStop a, GradientStop m, GradientStop b)
    {
        var tt = new TranslateTransform(); var st = new ScaleTransform(1, 1);
        plate.RenderTransformOrigin = new Point(0.5, 0.5);
        plate.RenderTransform = new TransformGroup { Children = { st, tt } };
        double total = PlatesAt + PlateMs + 260;
        Motion.Play(plate, total, p =>
        {
            double t = p * total;
            if (t < PlatesAt) { plate.Opacity = 0; return; }
            double l = Math.Clamp((t - PlatesAt) / PlateMs, 0, 1);
            double drop = Motion.Back(Math.Min(1, l / 0.7), 1.6);
            tt.Y = -9 * (1 - drop);
            st.ScaleX = st.ScaleY = 1.25 - 0.25 * Math.Min(1, drop);
            plate.Opacity = Math.Clamp(l / 0.3, 0, 1);
            double g = Math.Clamp((t - PlatesAt - PlateMs * 0.55) / 360, 0, 1), at = -0.3 + 1.6 * g;
            a.Offset = at - 0.25; m.Offset = at; b.Offset = at + 0.25;
            m.Color = Color.FromArgb((byte)(0xE0 * Math.Sin(Math.PI * g)), 0xFF, 0xFF, 0xFF);
        }, () => { tt.Y = 0; st.ScaleX = st.ScaleY = 1; plate.Opacity = 1; m.Color = Color.FromArgb(0, 0xFF, 0xFF, 0xFF); });
    }

    /// <summary>
    /// A personal best, last on the finale's beat: the PB chip drops in with a flash, gold sparks burst off it, your DPS flares
    /// gold and settles back to white, your frame pulses.
    /// </summary>
    void PbIn(RowEl r)
    {
        var tt = new TranslateTransform();
        r.Pb.RenderTransform = tt;
        var glow = r.Frame.Effect as DropShadowEffect;
        var dpsBrush = new SolidColorBrush(((HudKit.Ink as SolidColorBrush)?.Color) ?? Colors.White);
        r.Dps.Foreground = dpsBrush;
        var ink = dpsBrush.Color;
        var flash = new Border { CornerRadius = new CornerRadius(3), Background = Brushes.White, IsHitTestVisible = false, Opacity = 0 };
        var word = ((Border)r.Pb).Child;
        if (word is not Grid) { r.Pb.Child = null; r.Pb.Child = new Grid { Children = { word, flash } }; }
        else if (word is Grid g0 && g0.Children.Count > 1 && g0.Children[1] is Border f0) flash = f0;
        double total = PbAt + 1100;
        bool burst = false;
        Motion.Play(r.Pb, total, p =>
        {
            double t = p * total;
            if (t < PbAt) { r.Pb.Opacity = 0; return; }
            double l = (t - PbAt) / 1100;
            double d = Motion.Back(Math.Min(1, l / 0.3), 1.8);
            tt.Y = -8 * (1 - d);
            r.Pb.Opacity = Math.Clamp(l / 0.15, 0, 1);
            flash.Opacity = l < 0.08 ? 0.9 * l / 0.08 : Math.Max(0, 0.9 * (1 - (l - 0.08) / 0.3));
            if (!burst && l > 0.05) { burst = true; Burst(r); }
            // the figure: gold at once, back to white over the last half
            double gk = l < 0.1 ? l / 0.1 : l < 0.5 ? 1 : 1 - (l - 0.5) / 0.5;
            dpsBrush.Color = Mix(ink, Color.FromRgb(0xFF, 0xD3, 0x5C), Math.Clamp(gk, 0, 1));
            if (glow != null) { double k = Math.Sin(Math.PI * Math.Min(1, l / 0.7)); glow.BlurRadius = r.FrameBlur0 + 24 * k; glow.Opacity = Math.Min(1, r.FrameOpacity0 + 0.5 * k); }
        }, () =>
        {
            tt.Y = 0; r.Pb.Opacity = 1; flash.Opacity = 0; dpsBrush.Color = ink;
            if (glow != null) { glow.BlurRadius = r.FrameBlur0; glow.Opacity = r.FrameOpacity0; }
        });
    }

    /// <summary>Gold sparks off the PB chip: a ring of them flying out and falling, fading as they go.</summary>
    void Burst(RowEl r)
    {
        var (cx, cy) = CentreOf(r.Pb, r);
        int n = 14;
        for (int i = 0; i < n; i++)
        {
            double ang = 2 * Math.PI * i / n + (i % 2) * 0.2, speed = 26 + 16 * ((i * 37) % 7) / 7.0;
            Particle(cx, cy, Math.Cos(ang) * speed, Math.Sin(ang) * speed * 0.7 - 6, i % 3 == 0 ? 3.8 : 2.8, Spark, 620 + 40 * (i % 4), gravity: 16, tag: "pb-spark", bornMs: PbAt + 55);
        }
    }

    /* ---------------- the fire ---------------- */

    /// <summary>
    /// Your row on fire (your pace beats your best): its chip burns, its foot line flickers like firelight, embers rise. Called
    /// on each draw of your row; the first draw at that heat ignites it.
    /// </summary>
    void Fire(RowEl r, bool beat, bool ignite)
    {
        if (!beat)
        {
            if (Equals(r.Chip.Tag, "fire")) { r.Chip.Background = PK.C(PK.Gold, 0.95); r.Chip.Tag = null; }
            if (_flames.TryGetValue(r, out var fl)) { (fl.Parent as Panel)?.Children.Remove(fl); _flames.Remove(r); }
            return;
        }
        if (!Equals(r.Chip.Tag, "fire"))
        {
            var burn = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(0, 1) };
            burn.GradientStops.Add(new GradientStop(Color.FromRgb(0xFF, 0xEC, 0xA8), 0)); burn.GradientStops.Add(new GradientStop(Color.FromRgb(0xFF, 0xB0, 0x3A), 0.5)); burn.GradientStops.Add(new GradientStop(Color.FromRgb(0xF0, 0x5A, 0x1E), 1));
            burn.Freeze();
            r.Chip.Background = burn; r.Chip.Tag = "fire";
        }
        // firelight: the foot line, the wash and the flames breathe on a few sines (never the same twice), on the meter's own
        // draws; a render holds them at a fixed moment (and still, with no motion: the flames stand, no embers)
        double s = Motion.FrozenAt != null || !Motion.On ? 1.7 : Environment.TickCount64 / 1000.0;
        double flick = 0.72 + 0.16 * Math.Sin(s * 11.3) + 0.08 * Math.Sin(s * 23.7 + 1.3) + 0.04 * Math.Sin(s * 41.1);
        if (r.Heat.BorderBrush is SolidColorBrush line && !line.IsFrozen) line.Color = Mix(Flame, Ember, flick);
        if (r.Heat.Background is LinearGradientBrush wash && !wash.IsFrozen) wash.GradientStops[0].Color = Color.FromArgb((byte)(0x26 + 0x16 * flick), 0xFF, 0x5A, 0x18);   // an ember-red wash the flames stand out on
        Flames(r, s);
        if (!Motion.On) return;
        if (ignite) Ignite(r);
        if (Motion.FrozenAt != null) { if (_embers == 0) for (int i = 0; i < 9; i++) EmberAt(r, i / 9.0 + 0.05, (i * 0.37) % 1); return; }
        var now = DateTime.UtcNow;
        if (_embers < EmbersMax && (now - _lastEmber).TotalMilliseconds >= EmberEveryMs) { _lastEmber = now; EmberAt(r, Rng.NextDouble(), Rng.NextDouble()); }
    }

    /// <summary>
    /// Your row catches fire: a wave of flame runs across it left to right (a hot white-gold front, an orange body, a glow left
    /// behind that cools), the frame flares, and a burst of embers goes up along it.
    /// </summary>
    void Ignite(RowEl r)
    {
        double w = r.Track.ActualWidth, h = _rowH - 2;
        if (w <= 0) return;
        var band = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 0) };
        GradientStop tail = new(Color.FromArgb(0, 0xFF, 0x8A, 0x2A), 0), body = new(Color.FromArgb(0, 0xFF, 0x8A, 0x2A), 0), front = new(Color.FromArgb(0, 0xFF, 0xF0, 0xC0), 0), lead = new(Color.FromArgb(0, 0xFF, 0xF0, 0xC0), 0);
        band.GradientStops.Add(tail); band.GradientStops.Add(body); band.GradientStops.Add(front); band.GradientStops.Add(lead);
        var wave = new Border { Width = w, Height = h, CornerRadius = new CornerRadius(4), Background = band, IsHitTestVisible = false, Tag = "ignite" };
        Canvas.SetLeft(wave, 0); Canvas.SetTop(wave, r.Y);
        _fx.Children.Add(wave);
        var glow = r.FrameGlow;
        Motion.Play(wave, 700, p =>
        {
            double at = Motion.Out(Math.Min(1, p / 0.7)) * 1.15, k = p < 0.7 ? 1 : 1 - (p - 0.7) / 0.3;
            tail.Offset = 0; body.Offset = Math.Max(0, at - 0.12); front.Offset = Math.Max(0, at - 0.02); lead.Offset = at + 0.04;
            tail.Color = Color.FromArgb((byte)(0x30 * k), 0xFF, 0x8A, 0x2A);
            body.Color = Color.FromArgb((byte)(0x48 * k), 0xFF, 0x9A, 0x30);
            front.Color = Color.FromArgb((byte)(0xD0 * k), 0xFF, 0xF0, 0xC0);
            if (glow != null) { double f = Math.Sin(Math.PI * p); glow.BlurRadius = 26 + 18 * f; glow.Opacity = Math.Min(1, 0.95 + 0.05 * f); }
        }, () => _fx.Children.Remove(wave));
        for (int i = 0; i < 10; i++) EmberAt(r, (i + 0.5) / 10, (i * 0.61) % 1, burst: true, bornMs: 40 * i);
    }

    /// <summary>A flame tongue: a wide base narrowing to a tip that leans a touch, as a flame does (one shape for every tongue).</summary>
    static readonly Geometry TongueShape = MakeTongue();
    static Geometry MakeTongue() { var g = Geometry.Parse("M 0,1 C 0.02,0.62 0.3,0.5 0.4,0.2 C 0.45,0.06 0.52,0 0.56,0.02 C 0.5,0.22 0.66,0.45 0.86,0.62 C 0.97,0.72 1,0.86 1,1 Z"); g.Freeze(); return g; }

    /// <summary>The flame tongues along each burning row's foot (behind its text), kept while it burns.</summary>
    readonly Dictionary<RowEl, Canvas> _flames = new();
    public const int Tongues = 13, BackTongues = 8;

    /// <summary>
    /// Flames along your row's foot, behind its words: <see cref="Tongues"/> soft tongues spread along your bar, each a
    /// gradient from a hot yellow base through orange to nothing at its tip, its height flickering on its own sines (at
    /// <paramref name="s"/> seconds), so the edge of the fire never sits still. Low: they never reach the name or the figure.
    /// </summary>
    void Flames(RowEl r, double s)
    {
        if (r.Frame.Child is not Grid inner) return;
        double w = r.Track.ActualWidth, h = _rowH - 2;
        if (w <= 0) return;
        if (!_flames.TryGetValue(r, out var layer))
        {
            layer = new Canvas { IsHitTestVisible = false, ClipToBounds = true, Tag = "flames" };
            // the back layer first (taller, deeper red, dimmer), then the front (shorter, hotter): a fire with depth
            for (int i = 0; i < Tongues + BackTongues; i++)
            {
                bool back = i < BackTongues;
                var g = new LinearGradientBrush { StartPoint = new Point(0.5, 1), EndPoint = new Point(0.5, 0) };
                g.GradientStops.Add(back ? new GradientStop(Color.FromArgb(0xB0, 0xFF, 0x9A, 0x30), 0) : new GradientStop(Color.FromArgb(0xF0, 0xFF, 0xF4, 0xC0), 0));
                g.GradientStops.Add(back ? new GradientStop(Color.FromArgb(0x90, 0xE8, 0x48, 0x14), 0.45) : new GradientStop(Color.FromArgb(0xC8, 0xFF, 0x86, 0x22), 0.4));
                g.GradientStops.Add(new GradientStop(Color.FromArgb(0x00, 0xB0, 0x20, 0x08), 1));
                g.Freeze();
                // every other tongue leans the other way
                layer.Children.Add(new System.Windows.Shapes.Path { Data = TongueShape, Stretch = Stretch.Fill, Fill = g, IsHitTestVisible = false,
                    RenderTransformOrigin = new Point(0.5, 1), RenderTransform = i % 2 == 0 ? new ScaleTransform(-1, 1) : Transform.Identity });
            }
            // behind the words: just under the columns in the row's own grid
            int at = inner.Children.IndexOf(r.Cols);
            inner.Children.Insert(at < 0 ? inner.Children.Count : at, layer);
            _flames[r] = layer;
        }
        double reach = w - 6;   // the whole row's foot burns
        for (int i = 0; i < Tongues + BackTongues; i++)
        {
            var t = (System.Windows.Shapes.Path)layer.Children[i];
            bool back = i < BackTongues;
            int j = back ? i : i - BackTongues, n = back ? BackTongues : Tongues;
            double phase = i * 1.93, k = Math.Clamp(0.55 + 0.25 * Math.Sin(s * (7.1 + j * 0.9) + phase) + 0.2 * Math.Sin(s * (13.3 + j * 1.7) + phase * 2.1), 0, 1);
            // sizes vary along the row (a fixed pattern, not a zigzag); the back ones taller and wider, set between the front ones
            double size = 0.6 + 0.4 * (((j * 7) % 5) / 4.0);
            double tw = back ? 30 + 16 * size : 16 + 14 * size, th = back ? (7 + 11 * k) * size + 3 : (5 + 9 * k) * size + 2;
            t.Width = tw; t.Height = th + 3;   // its base a little under the row's foot
            double x = (j + (back ? 0 : 0.5)) / n * reach + (back ? reach / n * 0.5 : 0);
            Canvas.SetLeft(t, x - tw / 2 + 2.2 * Math.Sin(s * 3.1 + phase));
            Canvas.SetTop(t, h - th);
            t.Opacity = back ? 0.5 + 0.3 * k : 0.6 + 0.35 * k;
        }
    }

    /// <summary>
    /// One ember off your row: from a point along your bar (<paramref name="across"/>, 0 to 1 of the fill), it rises, drifts on
    /// a slow sway, cools from pale gold to deep orange, shrinks and fades. <paramref name="seed"/> varies its course.
    /// </summary>
    void EmberAt(RowEl r, double across, double seed, bool burst = false, double bornMs = 0)
    {
        double w = r.Track.ActualWidth;
        if (w <= 0) return;
        double reach = Math.Max(40, r.BarFill.ActualWidth > 0 ? r.BarFill.ActualWidth : w * 0.5);
        double x0 = 8 + across * (reach - 8), y0 = r.Y + _rowH - 5 - 7 * seed;
        double rise = (burst ? 30 : 20) + 16 * seed, sway = 3 + 4 * seed, ms = (burst ? 700 : 950) + 450 * seed;
        // a burst in a real-time render is held at its own age; a steady ember in a render sits at a fixed point of its course
        double? frozen = Motion.FrozenAt != null && !(burst && Motion.FrozenMs != null) ? 0.2 + 0.6 * ((across + seed) % 1) : null;
        Particle(x0, y0, 0, -rise, 2.6 + 2 * seed, Ember, ms, sway: sway, swayPhase: seed * 6.28, cool: true, tag: "ember", count: true, frozenAt: frozen, bornMs: bornMs);
    }

    /// <summary>
    /// A particle on the effects layer: a soft dot (a radial gradient, no effect) at (x0, y0) moving by (dx, dy) over its life
    /// (gravity bends it down, sway rocks it sideways), fading in quickly and out over its second half, shrinking as it goes;
    /// <paramref name="cool"/> turns it from its colour to deep orange. A render places it once at <paramref name="frozenAt"/>.
    /// </summary>
    void Particle(double x0, double y0, double dx, double dy, double size, Color c, double ms, double gravity = 0, double sway = 0, double swayPhase = 0, bool cool = false, string tag = "", bool count = false, double? frozenAt = null, double bornMs = 0)
    {
        var core = new GradientStop(c, 0); var mid = new GradientStop(Color.FromArgb(0x90, c.R, c.G, c.B), 0.45);
        var fill = new RadialGradientBrush { GradientStops = { core, mid, new GradientStop(Color.FromArgb(0, c.R, c.G, c.B), 1) } };
        double d = size * 2.6;   // the glow round the core
        var dot = new Ellipse { Width = d, Height = d, Fill = fill, IsHitTestVisible = false, Tag = tag };
        _fx.Children.Add(dot);
        if (count) _embers++;
        void At(double l)
        {
            double sx = sway * Math.Sin(swayPhase + l * 5.5);
            double x = x0 + dx * Motion.Out(l) + sx, y = y0 + dy * (gravity > 0 ? Motion.Out(l) : l) + gravity * l * l;
            double k = 1 - 0.55 * l;
            dot.Width = dot.Height = d * k;
            Canvas.SetLeft(dot, x - dot.Width / 2); Canvas.SetTop(dot, y - dot.Height / 2);
            dot.Opacity = l < 0.12 ? l / 0.12 : l > 0.5 ? Math.Max(0, 1 - (l - 0.5) / 0.5) : 1;
            if (cool) { var cc = Mix(c, Color.FromRgb(0xF0, 0x4A, 0x14), l); core.Color = cc; mid.Color = Color.FromArgb(0x90, cc.R, cc.G, cc.B); }
        }
        void Gone() { if (_fx.Children.Contains(dot)) { _fx.Children.Remove(dot); if (count) _embers = Math.Max(0, _embers - 1); } }
        // a render: placed once, cleared with the meter; held in real time (FrozenMs), at its own age then (born bornMs into the moment)
        if (Motion.FrozenMs is double fm && frozenAt == null) { double l = (fm - bornMs) / ms; if (l < 0 || l >= 1) Gone(); else At(l); return; }
        if (frozenAt is double fz) { At(fz); return; }
        Motion.Play(dot, ms, At, Gone);
    }

    /// <summary>The middle of an element of a row, on the effects layer (laid out first if it just appeared).</summary>
    (double X, double Y) CentreOf(FrameworkElement el, RowEl r)
    {
        try
        {
            if (el.ActualWidth <= 0) r.Root.UpdateLayout();
            var p = el.TranslatePoint(new Point(el.ActualWidth / 2, el.ActualHeight / 2), _fx);
            return (p.X, p.Y);
        }
        catch (InvalidOperationException) { return (NameEnd(r), r.Y + _rowH / 2); }
    }

    /// <summary>For the tests: the effects layer's pieces now, by kind (ember, pb-spark, finale-shine, ignite).</summary>
    internal Dictionary<string, int> FxForTest() => _fx.Children.OfType<FrameworkElement>().GroupBy(e => e.Tag as string ?? "").ToDictionary(g => g.Key, g => g.Count());
    internal string? ChipFireForTest() => _rows.Values.FirstOrDefault(r => r.You)?.Chip.Tag as string;
}
