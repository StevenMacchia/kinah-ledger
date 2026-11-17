using System.Globalization;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace KinahLedger.Ui;

/// <summary>
/// The themed looks' parts on the window (1.0; Steven, 2026-11-17: the first skins "just have diff color buttons they dont
/// match the tehemes at all", then "it needs deep work not a color change"). Each themed look draws what its overlay
/// looks are made of, on the cards, their heads and their bars, still and in the element tree (renders show it):
/// <list type="bullet">
/// <item>Wacky (Sticker, Goo): a comic page. Cream sticker cards in a thick ink outline with a hard drop (the look's
/// shape keys), a gloss along their top and halftone in a corner; the hero a yellow sticker in ink; heads on a tilted
/// yellow tag; bars outlined in ink.</item>
/// <item>Tactical (Fighter, Holo): HUD glass. A chamfered frame with L brackets lit at its corners over a fine grid; heads
/// as <c>[ HEAD ]</c> in amber; bars cut into segments.</item>
/// <item>Glitch (VHS, Corrupt): a tape's OSD. A thin frame split magenta and cyan at its edges with notches, scanlines over
/// the card, the hero framed with <c>● REC</c>; heads as a <c>&gt; prompt</c> in mono; bars in blocks.</item>
/// </list>
/// Night, Abyss and Daylight draw nothing: every property touched goes back to what it was.
/// </summary>
public static class SkinDeco
{
    /// <summary>A card's frame: "card", "ref" (a reference card), "group" (an Options group), "hero".</summary>
    public static readonly DependencyProperty FrameProperty = DependencyProperty.RegisterAttached("Frame", typeof(string), typeof(SkinDeco), new PropertyMetadata(null, (d, e) => Hook(d)));
    public static void SetFrame(DependencyObject o, string? v) => o.SetValue(FrameProperty, v);
    public static string? GetFrame(DependencyObject o) => (string?)o.GetValue(FrameProperty);

    /// <summary>A card's head (its title).</summary>
    public static readonly DependencyProperty HeadProperty = DependencyProperty.RegisterAttached("Head", typeof(bool), typeof(SkinDeco), new PropertyMetadata(false, (d, e) => Hook(d)));
    public static void SetHead(DependencyObject o, bool v) => o.SetValue(HeadProperty, v);
    public static bool GetHead(DependencyObject o) => (bool)o.GetValue(HeadProperty);

    /// <summary>A bar's fill.</summary>
    public static readonly DependencyProperty BarProperty = DependencyProperty.RegisterAttached("Bar", typeof(bool), typeof(SkinDeco), new PropertyMetadata(false, (d, e) => Hook(d)));
    public static void SetBar(DependencyObject o, bool v) => o.SetValue(BarProperty, v);
    public static bool GetBar(DependencyObject o) => (bool)o.GetValue(BarProperty);

    /// <summary>A list row (Quest's QRow: a pickup, a goal, a run).</summary>
    public static readonly DependencyProperty RowProperty = DependencyProperty.RegisterAttached("Row", typeof(bool), typeof(SkinDeco), new PropertyMetadata(false, (d, e) => Hook(d)));
    public static void SetRow(DependencyObject o, bool v) => o.SetValue(RowProperty, v);
    public static bool GetRow(DependencyObject o) => (bool)o.GetValue(RowProperty);

    /// <summary>What an element had before a look drew on it, and what the look last set (to tell another writer's change).</summary>
    sealed class State
    {
        public bool Hooked;
        public readonly Dictionary<DependencyProperty, object?> Orig = new();
        public readonly Dictionary<DependencyProperty, object?> Mine = new();
        public string? Text;
        public string? TextShown;
        public bool Busy;
        public ResourceDictionary? Scope;
    }
    static readonly ConditionalWeakTable<DependencyObject, State> _state = new();
    static readonly List<WeakReference<FrameworkElement>> _all = new();

    static void Hook(DependencyObject d)
    {
        if (d is not FrameworkElement el) return;
        var st = _state.GetOrCreateValue(el);
        if (st.Hooked) { if (el.IsLoaded) Paint(el); return; }
        st.Hooked = true;
        lock (_all) { _all.Add(new WeakReference<FrameworkElement>(el)); if (_all.Count % 256 == 0) _all.RemoveAll(w => !w.TryGetTarget(out _)); }
        el.Loaded += (_, _) => Paint(el);
        el.SizeChanged += (_, e) => { if (GetFrame(el) != null && (Math.Abs(e.NewSize.Width - e.PreviousSize.Width) >= 1 || Math.Abs(e.NewSize.Height - e.PreviousSize.Height) >= 1)) Paint(el); };
        if (el is TextBlock tb)
            System.ComponentModel.DependencyPropertyDescriptor.FromProperty(TextBlock.TextProperty, typeof(TextBlock)).AddValueChanged(tb, (_, _) => { if (!st.Busy && tb.Text != st.TextShown) Paint(el); });
        if (el.IsLoaded) Paint(el);
    }

    /// <summary>Every element drawn again in the look now on (Skins.Apply).</summary>
    public static void Refresh()
    {
        List<FrameworkElement> live = new();
        lock (_all) foreach (var w in _all) if (w.TryGetTarget(out var el)) live.Add(el);
        foreach (var el in live) Paint(el);
    }

    static string Look => Skins.Current;
    static bool Themed => Look is HudThemes.Wacky or HudThemes.Tactical or HudThemes.Glitch;

    static void Paint(FrameworkElement el)
    {
        try
        {
            if (el is Border b && GetFrame(b) is { } kind) PaintFrame(b, kind);
            else if (el is TextBlock t && GetHead(t)) PaintHead(t);
            else if (el is Border bar && GetBar(bar)) PaintBar(bar);
            else if (el is Border row && GetRow(row)) PaintRow(row);
        }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine("SkinDeco: " + ex.Message); }
    }

    // ---- keeping what was there

    static object? ResourceKeyOf(object? local) =>
        local?.GetType().Name == "ResourceReferenceExpression" ? local.GetType().GetProperty("ResourceKey", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(local) : null;

    /// <summary>Puts a property back as it was before the look drew on it; a value another writer set since is left alone.</summary>
    static void Restore(FrameworkElement el, State st, DependencyProperty dp)
    {
        if (!st.Mine.TryGetValue(dp, out var mine)) return;
        st.Mine.Remove(dp);
        var was = st.Orig.TryGetValue(dp, out var o) ? o : null;
        st.Orig.Remove(dp);
        if (!ReferenceEquals(el.ReadLocalValue(dp), mine)) return;
        switch (was)
        {
            case null: el.ClearValue(dp); break;
            case ResKey k: el.SetResourceReference(dp, k.Key); break;
            default: el.SetValue(dp, was); break;
        }
    }
    sealed record ResKey(object Key);
    static object? Snapshot(object? local) =>
        local == DependencyProperty.UnsetValue ? null : ResourceKeyOf(local) is { } k ? new ResKey(k) : local is System.Windows.Expression ? null : local;

    static void Set(FrameworkElement el, State st, DependencyProperty dp, object value)
    {
        if (!st.Mine.ContainsKey(dp)) st.Orig[dp] = Snapshot(el.ReadLocalValue(dp));
        el.SetValue(dp, value);
        st.Mine[dp] = el.ReadLocalValue(dp);
    }

    static void RestoreAll(FrameworkElement el, State st)
    {
        foreach (var dp in st.Mine.Keys.ToList()) Restore(el, st, dp);
    }

    // ---- colours

    static Color C(string hex) => (Color)ColorConverter.ConvertFromString(hex)!;
    static Color Res(FrameworkElement el, string key, string fallback) => (el.TryFindResource(key) as SolidColorBrush)?.Color ?? C(fallback);
    static SolidColorBrush Br(Color c) { var b = new SolidColorBrush(c); b.Freeze(); return b; }
    static SolidColorBrush Br(string hex) => Br(C(hex));
    static Pen P(Color c, double w) { var p = new Pen(Br(c), w) { StartLineCap = PenLineCap.Flat, EndLineCap = PenLineCap.Flat, LineJoin = PenLineJoin.Miter }; p.Freeze(); return p; }
    static Pen P(string hex, double w) => P(C(hex), w);
    static Color A(Color c, double a) => Color.FromArgb((byte)Math.Round(255 * a), c.R, c.G, c.B);

    // ---- frames

    /// <summary>The hero's colours in Wacky: a cyan sticker in ink (Sticker's yellow pill sits on it), Kinah's gold a deep pink that shows on the cyan.</summary>
    static ResourceDictionary WackyHero()
    {
        var d = new ResourceDictionary();
        void S(string k, string hex) => d[k] = Br(hex);
        S("Ink", "#111111"); S("Muted", "#3D3200"); S("Text2", "#2B2300"); S("Faint", "#4D4000"); S("CommonInk", "#111111");
        S("Gold", "#A30E50"); S("Accent", "#A30E50"); S("Mint", "#0B6B35"); S("Good", "#0B6B35"); S("Bad", "#B0103A"); S("Coral", "#B0103A"); S("Warn", "#8A4B00");
        S("Line", "#33111111"); S("LineStrong", "#111111"); S("Bg", "#2E111111"); S("Inset", "#FFF8E8"); S("Card2", "#FFF0B0");
        S("CardLineHover", "#111111"); S("Link", "#111111"); S("LinkHover", "#C2125F"); S("Lilac", "#111111"); S("Sky", "#111111"); S("Violet", "#5A2BB0"); S("Progress", "#5A4A00");
        S("HoverTint", "#1A111111"); S("BtnHover", "#FFE98A"); S("BtnHoverLine", "#111111");
        d["DisplayFx"] = null;
        return d;
    }

    static void PaintFrame(Border b, string kind)
    {
        var st = _state.GetOrCreateValue(b);
        RestoreAll(b, st);
        if (st.Scope != null) { b.Resources.MergedDictionaries.Remove(st.Scope); st.Scope = null; }
        if (!Themed) return;
        bool hero = kind is "hero" or "heroart";   // heroart: the hero that draws the theme's overlay art, which brings its own REC
        if (hero && Look == HudThemes.Wacky)
        {
            st.Scope = WackyHero();
            b.Resources.MergedDictionaries.Add(st.Scope);
        }
        double w = b.ActualWidth, h = b.ActualHeight;
        if (w < 8 || h < 8) return;
        var baseFill = b.Background ?? Brushes.Transparent;
        var g = new DrawingGroup();
        var all = new Rect(0, 0, w, h);
        if (kind == "ground") Ground(g, all, baseFill);
        else switch (Look)
        {
            case HudThemes.Wacky: Wacky(g, all, baseFill, hero, kind == "ref"); break;
            case HudThemes.Tactical: Tactical(g, all, baseFill, hero, kind == "ref", b); break;
            case HudThemes.Glitch: Glitch(g, all, baseFill, hero, kind == "ref", b, rec: kind == "hero"); break;
        }
        g.Freeze();
        var brush = new DrawingBrush(g) { Stretch = Stretch.None, AlignmentX = AlignmentX.Left, AlignmentY = AlignmentY.Top, ViewboxUnits = BrushMappingMode.Absolute, Viewbox = all, ViewportUnits = BrushMappingMode.Absolute, Viewport = all, TileMode = TileMode.None };
        brush.Freeze();
        Set(b, st, Border.BackgroundProperty, brush);
    }

    /// <summary>
    /// The window's ground behind the cards: Wacky a comic page with bursts in its corners, Tactical a radar sweep and the
    /// window's own corner brackets over the grid, Glitch torn signal bands and a tracking band at the foot over the scanlines.
    /// </summary>
    static void Ground(DrawingGroup g, Rect r, Brush tile)
    {
        g.Children.Add(new GeometryDrawing(tile, null, new RectangleGeometry(r)));
        switch (Look)
        {
            case HudThemes.Wacky:
                static Geometry Burst(Point c, double rOut, double rIn, int points, double turn)
                {
                    var sg = new StreamGeometry();
                    using (var x = sg.Open())
                        for (int i = 0; i < points * 2; i++)
                        {
                            double a = turn + Math.PI * i / points, rr = i % 2 == 0 ? rOut : rIn;
                            var p = new Point(c.X + rr * Math.Cos(a), c.Y + rr * Math.Sin(a));
                            if (i == 0) x.BeginFigure(p, true, true); else x.LineTo(p, true, false);
                        }
                    sg.Freeze(); return sg;
                }
                g.Children.Add(new GeometryDrawing(Br(Color.FromArgb(0x55, 0xFF, 0x3D, 0xA6)), P(Color.FromArgb(0x66, 0x11, 0x11, 0x11), 2), Burst(new Point(r.Right - 30, 40), 150, 95, 12, 0.2)));
                g.Children.Add(new GeometryDrawing(Br(Color.FromArgb(0x55, 0x3D, 0xF5, 0xC8)), P(Color.FromArgb(0x66, 0x11, 0x11, 0x11), 2), Burst(new Point(20, r.Bottom - 60), 170, 105, 14, 0.5)));
                g.Children.Add(new GeometryDrawing(Br(Color.FromArgb(0x66, 0xFF, 0xE1, 0x4D)), P(Color.FromArgb(0x66, 0x11, 0x11, 0x11), 2), Burst(new Point(r.Right + 10, r.Height * 0.55), 120, 78, 10, 0.1)));
                // speed lines from the top left, the comic's motion
                var lines = new GeometryGroup();
                for (int i = 0; i < 9; i++) lines.Children.Add(new LineGeometry(new Point(0, 30 + i * 22), new Point(70 + i * 9, 0)));
                g.Children.Add(new GeometryDrawing(null, P(Color.FromArgb(0x40, 0x11, 0x11, 0x11), 2), lines));
                break;
            case HudThemes.Tactical:
                var cy = C("#7FE9FF");
                var radar = new GeometryGroup();
                var rc = new Point(r.Right - 60, 70);
                foreach (var rad in new[] { 90.0, 160, 230, 300 }) radar.Children.Add(new EllipseGeometry(rc, rad, rad));
                radar.Children.Add(new LineGeometry(new Point(rc.X - 320, rc.Y), new Point(rc.X + 320, rc.Y)));
                radar.Children.Add(new LineGeometry(new Point(rc.X, rc.Y - 320), new Point(rc.X, rc.Y + 320)));
                g.Children.Add(new GeometryDrawing(null, P(A(cy, 0.08), 1), radar));
                var sweep = new RadialGradientBrush { Center = new Point(0.5, 0.5), GradientOrigin = new Point(0.5, 0.5) };
                sweep.GradientStops.Add(new GradientStop(A(cy, 0.10), 0)); sweep.GradientStops.Add(new GradientStop(A(cy, 0), 1)); sweep.Freeze();
                g.Children.Add(new GeometryDrawing(sweep, null, new EllipseGeometry(rc, 300, 300)));
                // the window's corner brackets and a range scale down the left edge
                var br = new GeometryGroup();
                double a = 22, o = 4;
                void L(Point p0, Point m, Point p1) { var f = new PathFigure { StartPoint = p0 }; f.Segments.Add(new PolyLineSegment(new[] { m, p1 }, true)); br.Children.Add(new PathGeometry(new[] { f })); }
                L(new Point(o, o + a), new Point(o, o), new Point(o + a, o));
                L(new Point(r.Right - o - a, o), new Point(r.Right - o, o), new Point(r.Right - o, o + a));
                L(new Point(o, r.Bottom - o - a), new Point(o, r.Bottom - o), new Point(o + a, r.Bottom - o));
                L(new Point(r.Right - o - a, r.Bottom - o), new Point(r.Right - o, r.Bottom - o), new Point(r.Right - o, r.Bottom - o - a));
                g.Children.Add(new GeometryDrawing(null, P(A(cy, 0.7), 2), br));
                var scale = new GeometryGroup();
                for (double y = 120; y < r.Bottom - 40; y += 12) scale.Children.Add(new LineGeometry(new Point(o + 2, y), new Point(o + 2 + ((int)(y / 12) % 5 == 0 ? 8 : 4), y)));
                g.Children.Add(new GeometryDrawing(null, P(A(cy, 0.3), 1), scale));
                break;
            case HudThemes.Glitch:
                // torn bands at fixed rows (the same every time: a still picture of a bad signal), magenta over cyan a pixel off
                var rnd = new Random(1987);
                for (int i = 0; i < 9; i++)
                {
                    double y = rnd.NextDouble() * r.Height, hgt = 1 + rnd.Next(4), x0 = rnd.NextDouble() * r.Width * 0.6, wid = r.Width * (0.15 + rnd.NextDouble() * 0.5);
                    g.Children.Add(new GeometryDrawing(Br(Color.FromArgb(0x40, 0x2B, 0xE6, 0xFF)), null, new RectangleGeometry(new Rect(x0 + 3, y + 1, wid, hgt))));
                    g.Children.Add(new GeometryDrawing(Br(Color.FromArgb(0x48, 0xFF, 0x2B, 0x6A)), null, new RectangleGeometry(new Rect(x0, y, wid, hgt))));
                }
                // the tracking band at the foot: a strip of broken light lines
                double ty = r.Bottom - 26;
                for (int i = 0; i < 7; i++)
                {
                    double x0 = rnd.NextDouble() * r.Width, wid = 20 + rnd.NextDouble() * 160;
                    g.Children.Add(new GeometryDrawing(Br(Color.FromArgb((byte)(0x30 + rnd.Next(0x40)), 0xF2, 0xF2, 0xF5)), null, new RectangleGeometry(new Rect(x0, ty + rnd.Next(14), wid, 1))));
                }
                break;
        }
    }

    /// <summary>Sticker and Goo: the card's paper (the hero yellow), a gloss band along its top, halftone dots in its lower right.</summary>
    static void Wacky(DrawingGroup g, Rect r, Brush baseFill, bool hero, bool quiet)
    {
        g.Children.Add(new GeometryDrawing(hero ? Br("#3DF5C8") : baseFill, null, new RectangleGeometry(r)));
        // halftone: dots growing toward the lower-right corner, the comic's shading
        var dot = hero ? C("#22D9AE") : quiet ? C("#F3DDB4") : C("#F6D9A8");
        var dots = new GeometryGroup();
        double step = 9, reach = Math.Min(r.Width * 0.42, 190);
        for (double y = r.Bottom - 4; y > r.Bottom - reach && y > 2; y -= step)
            for (double x = r.Right - 4; x > r.Right - reach && x > 2; x -= step)
            {
                double t = 1 - Math.Sqrt((r.Right - x) * (r.Right - x) + (r.Bottom - y) * (r.Bottom - y)) / reach;
                if (t <= 0.05) continue;
                double rad = 0.4 + 2.1 * t;
                dots.Children.Add(new EllipseGeometry(new Point(x - ((int)((r.Bottom - y) / step) % 2) * step / 2, y), rad, rad));
            }
        g.Children.Add(new GeometryDrawing(Br(dot), null, dots));
        // Goo's gloss: a soft white band across the top third, a candy shine
        var gloss = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(0, 1) };
        gloss.GradientStops.Add(new GradientStop(Color.FromArgb(hero ? (byte)0x66 : (byte)0x8C, 255, 255, 255), 0));
        gloss.GradientStops.Add(new GradientStop(Color.FromArgb(0, 255, 255, 255), 1));
        gloss.Freeze();
        g.Children.Add(new GeometryDrawing(gloss, null, new RectangleGeometry(new Rect(r.X + 6, r.Y + 3, Math.Max(0, r.Width - 12), Math.Min(22, r.Height * 0.3)), 8, 8)));
    }

    /// <summary>Fighter and Holo: glass over a fine grid, a chamfered frame cut at the top right and bottom left, L brackets lit at the corners.</summary>
    static void Tactical(DrawingGroup g, Rect r, Brush baseFill, bool hero, bool quiet, FrameworkElement el)
    {
        double cut = quiet ? 8 : 12;
        var ground = Res(el, "Bg", "#080B0F");
        var lit = hero ? C("#5CFF9A") : C("#7FE9FF");
        var outline = quiet ? C("#1C2A33") : C("#24414C");
        var shape = new StreamGeometry();
        using (var c = shape.Open())
        {
            c.BeginFigure(new Point(0.5, 0.5), true, true);
            c.LineTo(new Point(r.Width - cut, 0.5), true, false); c.LineTo(new Point(r.Width - 0.5, cut), true, false);
            c.LineTo(new Point(r.Width - 0.5, r.Height - 0.5), true, false); c.LineTo(new Point(cut, r.Height - 0.5), true, false);
            c.LineTo(new Point(0.5, r.Height - cut), true, false);
        }
        shape.Freeze();
        // the window's ground where the corners are cut away, then the glass
        g.Children.Add(new GeometryDrawing(Br(ground), null, new RectangleGeometry(r)));
        g.Children.Add(new GeometryDrawing(baseFill, null, shape));
        // Holo's grid, faint, and a lighter band at the top like glass catching the light
        var grid = new GeometryGroup();
        for (double x = 16; x < r.Width; x += 16) grid.Children.Add(new LineGeometry(new Point(x, 1), new Point(x, r.Height - 1)));
        for (double y = 16; y < r.Height; y += 16) grid.Children.Add(new LineGeometry(new Point(1, y), new Point(r.Width - 1, y)));
        g.Children.Add(new GeometryDrawing(null, P(A(lit, quiet ? 0.025 : 0.04), 1), grid));
        var sheen = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(0, 1) };
        sheen.GradientStops.Add(new GradientStop(A(lit, hero ? 0.10 : 0.06), 0)); sheen.GradientStops.Add(new GradientStop(A(lit, 0), 1)); sheen.Freeze();
        g.Children.Add(new GeometryDrawing(sheen, null, new RectangleGeometry(new Rect(1, 1, Math.Max(0, r.Width - cut - 2), Math.Min(26, r.Height / 3)))));
        g.Children.Add(new GeometryDrawing(null, P(outline, 1), shape));
        // L brackets at the two square corners, a lit line along each cut
        double arm = quiet ? 9 : 14, th = quiet ? 1.5 : 2;
        var br = new GeometryGroup();
        void L(Point a, Point m, Point z) { var f = new PathFigure { StartPoint = a }; f.Segments.Add(new PolyLineSegment(new[] { m, z }, true)); br.Children.Add(new PathGeometry(new[] { f })); }
        double o = th / 2 + 0.5;
        L(new Point(o, arm + o), new Point(o, o), new Point(arm + o, o));
        L(new Point(r.Width - arm - o, r.Height - o), new Point(r.Width - o, r.Height - o), new Point(r.Width - o, r.Height - arm - o));
        br.Children.Add(new LineGeometry(new Point(r.Width - cut - 4, o), new Point(r.Width - o, cut + 4)));
        br.Children.Add(new LineGeometry(new Point(o, r.Height - cut - 4), new Point(cut + 4, r.Height - o)));
        g.Children.Add(new GeometryDrawing(null, P(A(lit, quiet ? 0.6 : 0.95), th), br));
        // tick marks along the bottom edge, an instrument's scale
        if (!quiet && r.Width > 120)
        {
            var ticks = new GeometryGroup();
            for (int i = 0; i < 6; i++) ticks.Children.Add(new LineGeometry(new Point(r.Width - 22 - i * 6, r.Height - 1), new Point(r.Width - 22 - i * 6, r.Height - (i == 0 ? 6 : 4))));
            g.Children.Add(new GeometryDrawing(null, P(A(lit, 0.7), 1), ticks));
        }
    }

    /// <summary>VHS and Corrupt: scanlines over a black card, a thin OSD frame, the edges split magenta (left) and cyan (right) with notches; the hero framed with ● REC.</summary>
    static void Glitch(DrawingGroup g, Rect r, Brush baseFill, bool hero, bool quiet, FrameworkElement el, bool rec)
    {
        g.Children.Add(new GeometryDrawing(baseFill, null, new RectangleGeometry(r)));
        var lines = new GeometryGroup();
        for (double y = 2; y < r.Height; y += 3) lines.Children.Add(new RectangleGeometry(new Rect(0, y, r.Width, 1)));
        g.Children.Add(new GeometryDrawing(Br(Color.FromArgb(0x30, 0, 0, 0)), null, lines));
        var mag = C("#FF2B6A"); var cy = C("#2BE6FF");
        var frame = quiet ? C("#3A3A46") : C("#6C6C7A");
        double inset = hero ? 4.5 : 0.5;
        var box = new Rect(inset, inset, Math.Max(0, r.Width - 2 * inset), Math.Max(0, r.Height - 2 * inset));
        if (hero)
        {
            g.Children.Add(new GeometryDrawing(null, P(C("#E8E8EE"), 1.5), new RectangleGeometry(box)));
            if (rec) {
            // ● REC on the frame's top edge, at the right, its ground cut out of the line like an OSD's
            var face = new Typeface(new FontFamily("Cascadia Mono, Consolas"), FontStyles.Normal, FontWeights.Bold, FontStretches.Normal);
            var text = new FormattedText(L.T("REC"), CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, face, 10, Br("#F2F2F5"), 1.0);
            double tw = text.Width + 18, tx = r.Width - tw - 16;
            g.Children.Add(new GeometryDrawing(baseFill, null, new RectangleGeometry(new Rect(tx - 4, 0, tw + 8, 10))));
            g.Children.Add(new GeometryDrawing(Br(mag), null, new EllipseGeometry(new Point(tx + 5, 4.5), 3.5, 3.5)));
            g.Children.Add(new GeometryDrawing(Br("#F2F2F5"), null, text.BuildGeometry(new Point(tx + 13, -2))));
            }
        }
        else g.Children.Add(new GeometryDrawing(null, P(frame, 1), new RectangleGeometry(box)));
        // the split: a magenta bar down the left edge's upper part, cyan up the right edge's lower part, each a pixel off
        double edge = hero ? inset : 0;
        g.Children.Add(new GeometryDrawing(Br(A(mag, 0.95)), null, new RectangleGeometry(new Rect(edge, edge + 6, 2.5, Math.Max(0, (r.Height - 2 * edge) * 0.42)))));
        g.Children.Add(new GeometryDrawing(Br(A(cy, 0.95)), null, new RectangleGeometry(new Rect(r.Width - edge - 2.5, r.Height * 0.55, 2.5, Math.Max(0, r.Height * 0.45 - edge - 6)))));
        if (!quiet)
        {
            // notches at the middle of each side and a torn slice of the top line, the broken signal
            g.Children.Add(new GeometryDrawing(Br(A(cy, 0.9)), null, new RectangleGeometry(new Rect(edge - 0.5, r.Height / 2 - 4, 4, 8))));
            g.Children.Add(new GeometryDrawing(Br(A(mag, 0.9)), null, new RectangleGeometry(new Rect(r.Width - edge - 3.5, r.Height * 0.3 - 4, 4, 8))));
            if (r.Width > 200)
            {
                g.Children.Add(new GeometryDrawing(Br(A(cy, 0.85)), null, new RectangleGeometry(new Rect(r.Width * 0.62, edge - 0.5, 46, 2))));
                g.Children.Add(new GeometryDrawing(Br(A(mag, 0.85)), null, new RectangleGeometry(new Rect(r.Width * 0.62 + 3, edge + 1.5, 46, 1))));
            }
        }
    }

    // ---- heads

    static void PaintHead(TextBlock t)
    {
        var st = _state.GetOrCreateValue(t);
        st.Busy = true;
        try { HeadIn(t, st); } finally { st.Busy = false; }
    }

    static void HeadIn(TextBlock t, State st)
    {
        // the text as written (a look's prefix is never kept): a change by the page since is the new text
        if (st.TextShown == null || t.Text != st.TextShown) st.Text = t.Text;
        RestoreAll(t, st);
        if (st.TextShown != null && t.Text == st.TextShown && st.Text != null) t.Text = st.Text;
        st.TextShown = null;
        var text = st.Text ?? t.Text;
        if (!Themed || string.IsNullOrWhiteSpace(text)) return;
        switch (Look)
        {
            case HudThemes.Wacky:
                Set(t, st, TextBlock.FontFamilyProperty, new FontFamily("Segoe UI Black, Segoe UI"));
                Set(t, st, TextBlock.ForegroundProperty, Br("#111111"));
                Set(t, st, TextBlock.BackgroundProperty, Br("#FFE14D"));
                Set(t, st, TextBlock.PaddingProperty, new Thickness(7, 1, 7, 2));
                Set(t, st, UIElement.RenderTransformProperty, new RotateTransform(-2.5));
                Set(t, st, UIElement.RenderTransformOriginProperty, new Point(0.5, 0.5));
                Set(t, st, UIElement.EffectProperty, new System.Windows.Media.Effects.DropShadowEffect { Color = Colors.Black, BlurRadius = 0, ShadowDepth = 2.5, Direction = 315, Opacity = 1 });
                break;
            case HudThemes.Tactical:
                Set(t, st, TextBlock.ForegroundProperty, Br(Res(t, "Gold", "#FFC84A")));
                Set(t, st, TextBlock.FontWeightProperty, FontWeights.SemiBold);
                Show(t, st, "[ " + text.ToUpper(CultureInfo.CurrentUICulture) + " ]");
                break;
            case HudThemes.Glitch:
                Set(t, st, TextBlock.FontFamilyProperty, new FontFamily("Cascadia Mono, Consolas"));
                Set(t, st, TextBlock.ForegroundProperty, Br("#3DFF8C"));
                Set(t, st, TextBlock.FontWeightProperty, FontWeights.SemiBold);
                Show(t, st, "> " + text.ToLower(CultureInfo.CurrentUICulture).Replace(' ', '_'));
                break;
        }
    }

    static void Show(TextBlock t, State st, string shown) { st.TextShown = shown; t.Text = shown; }

    // ---- rows

    /// <summary>Sticker's rows are white pills in ink; Fighter's sit on a lit tick and a rule; Corrupt's carry a colour bar at their left.</summary>
    static void PaintRow(Border b)
    {
        var st = _state.GetOrCreateValue(b);
        RestoreAll(b, st);
        if (!Themed) return;
        switch (Look)
        {
            case HudThemes.Wacky:
                Set(b, st, Border.BackgroundProperty, Br("#FFFFFF"));
                Set(b, st, Border.BorderBrushProperty, Br("#111111"));
                Set(b, st, Border.BorderThicknessProperty, new Thickness(2));
                Set(b, st, Border.CornerRadiusProperty, new CornerRadius(12));
                Set(b, st, FrameworkElement.MarginProperty, new Thickness(0, 3, 0, 3));
                break;
            case HudThemes.Tactical:
                var tick = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 0) };
                tick.GradientStops.Add(new GradientStop(C("#7FE9FF"), 0)); tick.GradientStops.Add(new GradientStop(C("#7FE9FF"), 0.01));
                tick.GradientStops.Add(new GradientStop(C("#1A2C33"), 0.01)); tick.GradientStops.Add(new GradientStop(C("#1A2C33"), 1)); tick.Freeze();
                Set(b, st, Border.BorderBrushProperty, tick);
                Set(b, st, Border.BorderThicknessProperty, new Thickness(2, 0, 0, 1));
                Set(b, st, Border.CornerRadiusProperty, new CornerRadius(0));
                Set(b, st, FrameworkElement.MarginProperty, new Thickness(0));
                break;
            case HudThemes.Glitch:
                Set(b, st, Border.BorderBrushProperty, Br(Color.FromArgb(0xB0, 0x2B, 0xE6, 0xFF)));
                Set(b, st, Border.BorderThicknessProperty, new Thickness(2, 0, 0, 0));
                Set(b, st, Border.CornerRadiusProperty, new CornerRadius(0));
                Set(b, st, FrameworkElement.MarginProperty, new Thickness(0, 1, 0, 1));
                break;
        }
    }

    // ---- bars

    static Brush Segments(double on, double off)
    {
        var g = new DrawingGroup();
        g.Children.Add(new GeometryDrawing(Brushes.Black, null, new RectangleGeometry(new Rect(0, 0, on, 1))));
        g.Children.Add(new GeometryDrawing(Brushes.Transparent, null, new RectangleGeometry(new Rect(on, 0, off, 1))));
        var b = new DrawingBrush(g) { TileMode = TileMode.Tile, Viewport = new Rect(0, 0, on + off, 1), ViewportUnits = BrushMappingMode.Absolute, Viewbox = new Rect(0, 0, on + off, 1), ViewboxUnits = BrushMappingMode.Absolute, Stretch = Stretch.Fill };
        b.Freeze(); return b;
    }

    static void PaintBar(Border b)
    {
        var st = _state.GetOrCreateValue(b);
        RestoreAll(b, st);
        if (!Themed) return;
        switch (Look)
        {
            case HudThemes.Wacky:
                Set(b, st, Border.BorderBrushProperty, Br("#111111"));
                Set(b, st, Border.BorderThicknessProperty, new Thickness(1.5));
                break;
            case HudThemes.Tactical:
                Set(b, st, UIElement.OpacityMaskProperty, Segments(6, 2));
                Set(b, st, Border.CornerRadiusProperty, new CornerRadius(0));
                break;
            case HudThemes.Glitch:
                Set(b, st, UIElement.OpacityMaskProperty, Segments(4, 2));
                Set(b, st, Border.CornerRadiusProperty, new CornerRadius(0));
                break;
        }
    }
}
