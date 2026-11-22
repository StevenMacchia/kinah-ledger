using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using Path = System.Windows.Shapes.Path;

namespace KinahLedger.Ui;

/// <summary>
/// The Applicants window's arrival beat (2026-11-21): when someone new asks to join, their row (the newest, at the bottom)
/// comes in with a short beat in the look's own manner, about a second, then nothing moves until the next arrival. Atreia:
/// light falls on the newcomer (a pillar, a bloom and rays in the look's colours; Asmodian's embers instead of the pillar).
/// Esports: a stinger (slanted bars wipe across the row and it slides in behind them; the Telestrator swipes its highlighter;
/// Hype's sparks, the Finals' gold confetti). Elements: the element flicks (Frost's shards and a glint, Storm's bolt, Magma's
/// embers, Tide's ripples and spray). Overdrive: a slam (the row lands squashed and springs back, the words jolt, an impact
/// burst in the look's own ink). The faces of the first five families slide their newest row in.
/// Every part is a frozen shape made when the beat starts, on a stage of its own over the face (clipped to it), moved and
/// faded by its transforms and opacity on one course (Motion.Play), and taken away at its end; nothing is drawn again while it
/// plays, no effect or cache on any of it, and nothing at all with motion off (Motion.On) or between arrivals.
/// </summary>
static partial class ApplicantsFace
{
    /// <summary>A skinned face's parts: its skin, its plate (art and words) and the layer the beat plays on.</summary>
    sealed record FaceParts(Skin Skin, Grid Plate, Canvas Fx);
    static readonly ConditionalWeakTable<FrameworkElement, FaceParts> Parts = new();

    /// <summary>The look a face is drawn in (null: a family with one face).</summary>
    internal static string? LookOf(FrameworkElement face) => Parts.TryGetValue(face, out var p) ? p.Skin.Look : null;

    /// <summary>For the self-test: the parts on the face's beat stages now (0: no beat under way or left behind).</summary>
    internal static int BeatPartsForTest(FrameworkElement face) =>
        Parts.TryGetValue(face, out var p) ? p.Fx.Children.OfType<Canvas>().Sum(st => Math.Max(1, st.Children.Count)) : 0;

    /// <summary>For the self-test: the newest row's cells still moved or faded, and the rows still clipped (0: all at rest).</summary>
    internal static int MovedForTest(FrameworkElement face)
    {
        if (RowsOf(face) is not { } rows) return 0;
        int last = rows.RowDefinitions.Count - 1;
        int n = rows.Clip != null ? 1 : 0;
        foreach (var c in rows.Children.OfType<FrameworkElement>().Where(c => Grid.GetRow(c) == last))
            if ((c.RenderTransform != null && !c.RenderTransform.Value.IsIdentity) || c.Opacity < 1) n++;
        if (Parts.TryGetValue(face, out var p) && p.Plate.Children.Count > 1 && p.Plate.Children[1].RenderTransform is { } bt && !bt.Value.IsIdentity) n++;
        return n;
    }

    static Grid? RowsOf(DependencyObject d)
    {
        if (d is Grid { Tag: "applicant-rows" } g) return g;
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(d); i++) if (RowsOf(VisualTreeHelper.GetChild(d, i)) is { } x) return x;
        return null;
    }

    static double Seg(double p, double a, double b) => Math.Clamp((p - a) / (b - a), 0, 1);
    static double Out(double p) => Motion.Out(p);

    /// <summary>
    /// Plays the arrival beat on <paramref name="face"/> (laid out, shown): its newest row comes in. Returns how long it plays
    /// in ms (0: none, with motion off or no row). With Motion.FrozenAt set (a render's frame) it is held at that point instead.
    /// </summary>
    public static double Arrive(FrameworkElement face)
    {
        if (!Motion.On) return 0;
        var rows = RowsOf(face);
        if (rows == null || rows.RowDefinitions.Count == 0 || rows.ActualWidth <= 0) return 0;
        int last = rows.RowDefinitions.Count - 1;
        var cells = rows.Children.OfType<FrameworkElement>().Where(c => Grid.GetRow(c) == last).ToList();
        if (cells.Count == 0) return 0;
        double top = 0;
        for (int i = 0; i < last; i++) top += rows.RowDefinitions[i].ActualHeight;
        double rh = rows.RowDefinitions[last].ActualHeight;
        // each cell scales round the row's middle and slides; the rows are clipped while it plays (nothing leaves the face)
        var moves = cells.Select(c =>
        {
            var mid = rows.TranslatePoint(new Point(rows.ActualWidth / 2, top + rh / 2), c);
            var sc = new ScaleTransform(1, 1, mid.X, mid.Y); var tt = new TranslateTransform();
            c.RenderTransform = new TransformGroup { Children = { sc, tt } };
            return (El: c, S: sc, T: tt);
        }).ToList();
        var clip = new RectangleGeometry(new Rect(-4, -2, rows.ActualWidth + 8, rows.ActualHeight + 4)); clip.Freeze();
        rows.Clip = clip;
        void Cells(double dx, double sx, double sy, double a)
        {
            foreach (var m in moves) { m.T.X = dx; m.S.ScaleX = sx; m.S.ScaleY = sy; m.El.Opacity = a; }
        }
        void Rest()
        {
            foreach (var m in moves) { m.El.RenderTransform = null; m.El.Opacity = 1; }
            rows.ClearValue(UIElement.ClipProperty);
        }

        if (!Parts.TryGetValue(face, out var p))
        {
            // the first five families: the row slides in from the left and settles
            const double plain = 420;
            Motion.Play(rows, plain, t => Cells(-14 * (1 - Out(t)), 1, 1, Seg(t, 0, .45)), Rest);
            return plain;
        }

        var fx = p.Fx;
        var body = p.Plate.Children.Count > 1 ? p.Plate.Children[1] as FrameworkElement : null;
        var st = new Canvas { IsHitTestVisible = false };
        var stClip = new RectangleGeometry(new Rect(1, 1, Math.Max(1, face.ActualWidth - 2), Math.Max(1, face.ActualHeight - 2))); stClip.Freeze();
        st.Clip = stClip;
        fx.Children.Add(st);
        var o = rows.TranslatePoint(new Point(0, top), fx);
        var row = new Rect(o.X - 4, o.Y, rows.ActualWidth + 8, rh);
        var iconEl = cells.FirstOrDefault(c => Grid.GetColumn(c) == 0 && Grid.GetColumnSpan(c) == 1);
        var ic = iconEl != null ? iconEl.TranslatePoint(new Point(iconEl.ActualWidth / 2, iconEl.ActualHeight / 2), fx) : new Point(row.X + 14, row.Y + rh / 2);
        var k = p.Skin;
        var parts = new List<Action<double>>();
        double ms;
        // the row's wash: the look's accent over the row, flashed by its opacity
        Rectangle Wash(Brush fill)
        {
            var r = new Rectangle { Width = row.Width, Height = row.Height, Fill = fill, Opacity = 0, IsHitTestVisible = false, RadiusX = 3, RadiusY = 3 };
            Canvas.SetLeft(r, row.X); Canvas.SetTop(r, row.Y);
            st.Children.Add(r);
            return r;
        }
        var wash = Wash(Mod.Grad(false, (Mod.A(Mod.Mix(k.Accent, k.Hot, .4), .75), 0), (Mod.A(k.Accent, .35), .55), (Mod.A(k.Accent, 0), 1)));

        switch (k.Family)
        {
            case HudThemes.Atreia:
            {
                // light falls on the newcomer: a pillar onto their emblem, a bloom, rays and a ring; Asmodian's embers fly instead
                ms = 1050;
                if (k.Look == HudThemes.Asmodian)
                {
                    var bits = new AtreiaFx.Bits(st);
                    bits.Burst(10, AtreiaFx.Flame, i => AtreiaFx.Br(i % 3 == 0 ? Color.FromRgb(0xFF, 0xD0, 0x8A) : i % 2 == 0 ? Color.FromRgb(0xFF, 0x8A, 0x3D) : Color.FromRgb(0xFF, 0x4A, 0x3D)),
                        ic, 46, 5, .14, .72, fall: -18, dir: -90, spread: 150, spin: 60, seed: 5, sway: 3);
                    parts.Add(bits.At);
                    parts.Add(AtreiaFx.Crack(st, new Rect(row.X + 4, row.Y + 2, Math.Min(90, row.Width / 3), row.Height - 4), k.Accent, 9, .12, .34, .8));
                }
                else parts.Add(AtreiaFx.Pillar(st, new Point(ic.X, ic.Y + 6), 22, Math.Min(ic.Y + 6, rh * 2.2), k.Hot, 0, .22, .7));   // from a row above, onto the newcomer only
                parts.Add(AtreiaFx.Bloom(st, ic, 30, 30, AtreiaFx.Glow(k.Accent, .75, k.Hot), .2, .26, .45));   // the emblem's size: the name stays readable
                Color[] spec = { Color.FromRgb(0xFF, 0x4F, 0x64), Color.FromRgb(0xFF, 0x9B, 0x3D), Color.FromRgb(0xFF, 0xE5, 0x4F), Color.FromRgb(0x5B, 0xF0, 0x8E), Color.FromRgb(0x4F, 0xAE, 0xFF), Color.FromRgb(0xAE, 0x78, 0xFF) };
                parts.Add(AtreiaFx.Rays(st, ic, 12, 7, 27, 3.5, i => k.Look == HudThemes.Prism ? spec[i % 6] : i % 2 == 0 ? k.Accent : k.Hot, .2, .28, .62, turn: 24, max: .7));
                parts.Add(AtreiaFx.Ring(st, ic, 30, 30, AtreiaFx.Br(k.Hot, .9), 1.6, .45, 2.4, .24, .78));
                parts.Add(AtreiaFx.Streak(st, new Point(row.X + row.Width / 2, row.Bottom - 1), row.Width * .9, 2.2, k.Accent, .26, .36, .82));
                parts.Add(t =>
                {
                    wash.Opacity = (k.Look == HudThemes.Asmodian ? .55 : .8) * AtreiaFx.Flash(t, .2, .3, .92);
                    Cells(-10 * (1 - Out(Seg(t, .22, .6))), 1, 1, Seg(t, .2, .36));
                });
                break;
            }
            case HudThemes.Esports:
            {
                ms = 900;
                if (k.Look == HudThemes.Telestrator)
                {
                    // the analyst's highlighter swiped across the row, then the row is there under it
                    var pen = new Rectangle { Width = row.Width, Height = row.Height - 2, Fill = Mod.Br(k.Accent, .4), RadiusX = 2, RadiusY = 2, Opacity = 0, IsHitTestVisible = false };
                    var grow = new ScaleTransform(0, 1, 0, 0);
                    pen.RenderTransform = grow;
                    Canvas.SetLeft(pen, row.X); Canvas.SetTop(pen, row.Y + 1);
                    st.Children.Add(pen);
                    var ink = new Path { Data = Ring(new Point(ic.X, ic.Y), 15, 12), Stroke = Mod.Br(k.Hot, .95), StrokeThickness = 2, StrokeLineJoin = PenLineJoin.Round, IsHitTestVisible = false };
                    var draw = new ScaleTransform(.3, .3, ic.X, ic.Y); ink.RenderTransform = draw; ink.Opacity = 0;
                    st.Children.Add(ink);
                    parts.Add(t =>
                    {
                        grow.ScaleX = Out(Seg(t, 0, .34));
                        pen.Opacity = t <= 0 ? 0 : t < .5 ? 1 : 1 - Seg(t, .5, 1);
                        draw.ScaleX = draw.ScaleY = .3 + .7 * Out(Seg(t, .3, .52));
                        ink.Opacity = AtreiaFx.Flash(t, .3, .42, 1);
                        Cells(0, 1, 1, Seg(t, .12, .34));
                    });
                }
                else
                {
                    // the stinger: two slanted bars wipe across the row, the row slides in behind them
                    var bar1 = new Path { Data = Tac.Lean(new Rect(0, 0, 54, row.Height), 10), Fill = Mod.Br(k.Accent), IsHitTestVisible = false, Opacity = 0 };
                    var bar2 = new Path { Data = Tac.Lean(new Rect(0, 0, 16, row.Height), 10), Fill = Mod.Br(k.Hot), IsHitTestVisible = false, Opacity = 0 };
                    var t1 = new TranslateTransform(); var t2 = new TranslateTransform();
                    bar1.RenderTransform = t1; bar2.RenderTransform = t2;
                    Canvas.SetTop(bar1, row.Y); Canvas.SetTop(bar2, row.Y);
                    // the bars run in the row's own lane, never past it
                    var laneClip = new RectangleGeometry(new Rect(row.X + 1, row.Y, Math.Max(1, row.Width - 2), row.Height)); laneClip.Freeze();
                    var lane = new Canvas { IsHitTestVisible = false, Clip = laneClip };
                    st.Children.Add(lane);
                    lane.Children.Add(bar2); lane.Children.Add(bar1);
                    double from = row.X - 70, run = row.Width + 20;   // the bars stop short of the time column and fade there
                    parts.Add(t =>
                    {
                        double u1 = Seg(t, 0, .4), u2 = Seg(t, .06, .46);
                        t1.X = from + run * Ez.InOut(u1); t2.X = from - 26 + run * Ez.InOut(u2);
                        bar1.Opacity = u1 is > 0 and < 1 ? 1 - Seg(u1, .7, 1) : 0; bar2.Opacity = u2 is > 0 and < 1 ? 1 - Seg(u2, .6, 1) : 0;
                        wash.Opacity = .7 * AtreiaFx.Flash(t, .34, .4, .9);
                        // the row is revealed behind the first bar: it slides the last few px in as the bar passes
                        double behind = Math.Clamp((from + run * Ez.InOut(u1) - row.X) / Math.Max(1, row.Width), 0, 1);
                        Cells(12 * (1 - Out(Seg(t, .1, .5))), 1, 1, u1 >= 1 ? 1 : behind);
                    });
                    // sparks and confetti kept to the newest row and a little over it
                    var air = new RectangleGeometry(new Rect(row.X, row.Y - 10, row.Width, row.Height + 10)); air.Freeze();
                    var sky = new Canvas { IsHitTestVisible = false, Clip = air };
                    st.Children.Add(sky);
                    if (k.Look == HudThemes.Hype)
                        EsportsFx.Spray(sky, new Point(row.Right - 20, row.Y + row.Height / 2), 10, 46, -165, -15, new[] { k.Accent, k.Hot, Colors.White }, ms, 10, .38, sparks: true);
                    else if (k.Look == HudThemes.Finals)
                        EsportsFx.Spray(sky, new Point(row.X + row.Width * .5, row.Y + 2), 12, 52, -160, -20, new[] { k.Accent, k.Hot, Color.FromRgb(0xFF, 0xE1, 0x8E), Color.FromRgb(0x9A, 0x70, 0x26) }, ms, 34, .36);
                    else
                        EsportsFx.Spray(sky, new Point(row.Right - 16, row.Y + row.Height / 2), 6, 30, -150, -30, new[] { k.Accent, Colors.White }, ms, 6, .4, sparks: true);
                }
                break;
            }
            case HudThemes.Elements:
            {
                ms = 1000;
                var bits = new AtreiaFx.Bits(st);
                switch (k.Look)
                {
                    case HudThemes.Storm:
                    {
                        // a bolt from the cloud onto the newcomer's emblem, flickering twice
                        double h = Math.Max(16, ic.Y - 4);
                        var bolt = new Path { Data = ElemFx.Bolt(22, h, 4, 3), Fill = Mod.Br(k.Hot), IsHitTestVisible = false, Opacity = 0 };
                        var halo = new Path { Data = ElemFx.Bolt(22, h, 4, 8), Fill = Mod.Br(k.Accent, .45), IsHitTestVisible = false, Opacity = 0 };
                        foreach (var b in new[] { halo, bolt }) { Canvas.SetLeft(b, ic.X - 11); Canvas.SetTop(b, 0); st.Children.Add(b); }
                        parts.Add(AtreiaFx.Bloom(st, ic, 70, 70, AtreiaFx.Glow(k.Accent, .95, k.Hot), .1, .16, .7));
                        bits.Burst(8, AtreiaFx.Dot, _ => AtreiaFx.Br(k.Hot), ic, 30, 1.6, .12, .5, seed: 3);
                        parts.Add(t =>
                        {
                            double f = t < .08 ? 0 : t < .13 ? 1 : t < .17 ? .1 : t < .23 ? 1 : .35 * (1 - Seg(t, .23, .38));   // two strikes, then a short afterglow
                            bolt.Opacity = f; halo.Opacity = f * .9;
                            wash.Opacity = .75 * Math.Max(AtreiaFx.Flash(t, .08, .12, .2), AtreiaFx.Flash(t, .19, .22, .8));
                            Cells(0, 1, 1, Seg(t, .1, .2));
                        });
                        break;
                    }
                    case HudThemes.Magma:
                    {
                        // the row glows up out of the rock, embers rising off it
                        for (int i = 0; i < 10; i++)
                        {
                            double x = row.X + 10 + (row.Width - 20) * ((i * 0.618 + .13) % 1), up = 10 + 8 * ((i * 0.414 + .3) % 1);
                            bits.Add(AtreiaFx.Flame, AtreiaFx.Br(i % 3 == 0 ? Color.FromRgb(0xFF, 0xD2, 0x5E) : Color.FromRgb(0xEE, 0x57, 0x12)), new Point(x, row.Bottom - 2), new Vector((i % 2 == 0 ? 1 : -1) * 4, -up),
                                rot0: -90, s0: 4.5, s1: 2, delay: .1 + .04 * (i % 5), life: .5, sway: 2);
                        }
                        parts.Add(AtreiaFx.Bloom(st, ic, 74, 60, AtreiaFx.Glow(k.Accent, .95, k.Hot), .1, .22, .8));
                        parts.Add(t =>
                        {
                            wash.Opacity = .85 * AtreiaFx.Flash(t, .08, .24, .95);
                            Cells(0, 1, 1, Seg(t, .14, .4));
                        });
                        break;
                    }
                    case HudThemes.Tide:
                    {
                        // ripples out of the emblem, spray thrown up, the row surfacing
                        parts.Add(AtreiaFx.Ring(st, ic, 60, 24, AtreiaFx.Br(k.Hot, .9), 1.6, .3, 1.8, .1, .62));
                        parts.Add(AtreiaFx.Ring(st, ic, 60, 24, AtreiaFx.Br(k.Accent, .8), 1.2, .3, 2.4, .22, .78));
                        bits.Burst(9, AtreiaFx.Dot, i => AtreiaFx.Br(i % 2 == 0 ? k.Hot : k.Accent), ic, 34, 2.2, .1, .6, fall: 34, dir: -90, spread: 140, seed: 8);
                        parts.Add(t =>
                        {
                            wash.Opacity = .7 * AtreiaFx.Flash(t, .1, .26, .9);
                            Cells(0, 1, 1, Seg(t, .1, .38));
                            foreach (var m in moves) m.T.Y = 6 * (1 - Out(Seg(t, .1, .5)));
                        });
                        break;
                    }
                    default:
                    {
                        // Frost: ice shards burst from the emblem, a glint runs the row's top edge
                        bits.Burst(9, AtreiaFx.Shard, i => AtreiaFx.Br(i % 2 == 0 ? k.Hot : Color.FromRgb(0xA8, 0xE6, 0xFF)), ic, 38, 6, .08, .62, spin: 90, seed: 11);
                        bits.Add(AtreiaFx.Star, AtreiaFx.Br(Colors.White), new Point(row.X + 8, row.Y + 1), new Vector(row.Width - 16, 0), s0: 7, s1: 4, delay: .26, life: .5);
                        parts.Add(AtreiaFx.Ring(st, ic, 30, 30, AtreiaFx.Br(k.Hot, .9), 1.4, .4, 2, .08, .6));
                        parts.Add(t =>
                        {
                            wash.Opacity = .75 * AtreiaFx.Flash(t, .08, .2, .9);
                            Cells(0, 1, 1, Seg(t, .08, .3));
                        });
                        break;
                    }
                }
                parts.Add(bits.At);
                break;
            }
            default:
            {
                // Overdrive: the slam; the row lands squashed and springs back, the words jolt, an impact burst behind the emblem
                ms = 950;
                double hit = .2;
                if (k.Look == HudThemes.Dragon)
                {
                    var bits = new AtreiaFx.Bits(st);
                    bits.Burst(10, AtreiaFx.Flame, i => AtreiaFx.Br(i % 2 == 0 ? k.Accent : Color.FromRgb(0xFF, 0x86, 0x2A)), ic, 44, 5, hit, .6, fall: -12, spin: 40, seed: 13);
                    parts.Add(bits.At);
                    parts.Add(AtreiaFx.Bloom(st, ic, 90, 70, AtreiaFx.Glow(k.Accent, .95, k.Hot), hit - .02, hit + .06, .8));
                    parts.Add(AtreiaFx.Ring(st, ic, 34, 34, AtreiaFx.Br(Color.FromRgb(0x34, 0xF0, 0xAE), .9), 1.6, .4, 2.6, hit, .7));
                }
                else if (k.Look == HudThemes.Aura)
                {
                    parts.Add(AtreiaFx.Bloom(st, ic, 90, 70, AtreiaFx.Glow(k.Accent, .95, Color.FromRgb(0xC8, 0xEC, 0xFF)), hit - .02, hit + .06, .8));
                    parts.Add(AtreiaFx.Rays(st, ic, 10, 8, 40, 5, i => i % 2 == 0 ? k.Accent : k.Hot, hit, hit + .08, .7, turn: 30));
                    parts.Add(AtreiaFx.Ring(st, ic, 34, 34, AtreiaFx.Br(Color.FromRgb(0xC8, 0xEC, 0xFF), .9), 2, .4, 2.6, hit, .66));
                }
                else
                {
                    // Versus: a hot star burst; Manga: an ink burst on the page
                    bool manga = k.Look == HudThemes.Manga;
                    var burst = new Path
                    {
                        Data = Spikes(9, 26, 14, 21), IsHitTestVisible = false, Opacity = 0, StrokeLineJoin = PenLineJoin.Miter,
                        Fill = manga ? Mod.Br(Colors.White) : Mod.Grad(true, (Color.FromRgb(0xFF, 0xF3, 0x9A), 0), (Color.FromRgb(0xFF, 0xD8, 0x1F), .4), (Color.FromRgb(0xFF, 0x8A, 0x0A), 1)),
                        Stroke = Mod.Br(manga ? Color.FromRgb(0x0A, 0x0A, 0x0D) : Color.FromRgb(0xFF, 0x2E, 0x22)), StrokeThickness = manga ? 2.2 : 1.6,
                    };
                    var bs = new ScaleTransform(.2, .2); var bt = new TranslateTransform(ic.X, ic.Y);
                    burst.RenderTransform = new TransformGroup { Children = { bs, bt } };
                    st.Children.Insert(0, burst);
                    parts.Add(t =>
                    {
                        bs.ScaleX = bs.ScaleY = .2 + 1.05 * Out(Seg(t, hit - .03, hit + .14));
                        burst.Opacity = AtreiaFx.Flash(t, hit - .03, hit + .02, .72);
                    });
                    // speed lines across the row as it lands
                    var line = manga ? Color.FromRgb(0x0A, 0x0A, 0x0D) : Colors.White;
                    for (int i = 0; i < 2; i++)   // along the row's top and bottom edges, clear of its words
                        parts.Add(AtreiaFx.Streak(st, new Point(row.X + row.Width * (.5 + .08 * i), i == 0 ? row.Y + 1 : row.Bottom - 1), row.Width * .75, 1.4, line, hit - .1 + .02 * i, hit - .01, hit + .16, manga ? .75 : .65));
                }
                parts.Add(t =>
                {
                    // in fast from the right (unseen till close), squashed on the hit, a spring back to rest
                    double come = Seg(t, 0, hit), land = Seg(t, hit, hit + .42);
                    double dx = 70 * Math.Pow(1 - come, 3);
                    double sy = t < hit ? 1.25 : 1 + (-.22 * Math.Cos(Math.PI * 2.2 * land) * Math.Exp(-4.5 * land)) * (land < 1 ? 1 : 0);
                    const double sx = 1;   // squashed upright only: the row never grows past its columns
                    Cells(dx, sx, sy, Seg(t, hit * .5, hit));
                    wash.Opacity = .9 * AtreiaFx.Flash(t, hit - .01, hit + .02, .6);
                    if (body != null)
                    {
                        if (body.RenderTransform is not TranslateTransform jt) body.RenderTransform = jt = new TranslateTransform();
                        double s = Seg(t, hit, hit + .32);
                        jt.X = Ez.Jolt(s, 3, 6); jt.Y = Ez.Jolt(s, 1.6, 5);
                    }
                });
                break;
            }
        }

        Motion.Play(st, ms, t => { foreach (var a in parts) a(t); }, () =>
        {
            fx.Children.Remove(st);
            Rest();
            if (body != null) body.RenderTransform = null;
        });
        return ms;
    }

    /// <summary>A ring of ink round <paramref name="c"/> (the Telestrator's circle), a touch open and uneven, as one figure.</summary>
    static Geometry Ring(Point c, double rx, double ry)
    {
        var g = new StreamGeometry();
        using (var x = g.Open())
        {
            const int n = 28;
            for (int i = 0; i <= n + 3; i++)
            {
                double a = -Math.PI * .6 + 2 * Math.PI * i / n, w = 1 + .08 * Math.Sin(i * 1.7);
                var pt = new Point(c.X + Math.Cos(a) * rx * w, c.Y + Math.Sin(a) * ry * w);
                if (i == 0) x.BeginFigure(pt, false, false); else x.LineTo(pt, true, true);
            }
        }
        g.Freeze();
        return g;
    }

    /// <summary>A spiky burst round 0,0 (<paramref name="n"/> spikes between <paramref name="rIn"/> and about <paramref name="rOut"/>), uneven by <paramref name="seed"/>.</summary>
    static Geometry Spikes(double rIn, double rOut, int n, int seed)
    {
        var rnd = new Random(seed);
        var g = new StreamGeometry();
        using (var x = g.Open())
        {
            for (int i = 0; i < 2 * n; i++)
            {
                double a = Math.PI * i / n, r = i % 2 == 0 ? rOut * (.72 + .28 * rnd.NextDouble()) : rIn * (.85 + .3 * rnd.NextDouble());
                var pt = new Point(Math.Cos(a) * r, Math.Sin(a) * r);
                if (i == 0) x.BeginFigure(pt, true, true); else x.LineTo(pt, true, true);
            }
        }
        g.Freeze();
        return g;
    }
}
