using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using Path = System.Windows.Shapes.Path;
using System.Windows.Threading;
using static KinahLedger.Ui.HudKit;

namespace KinahLedger.Ui;

/// <summary>
/// The companion itself: a little winged wisp of aether (Daevas have wings; so does Buddy), drawn in vectors, no picture.
/// A teardrop body lit from the top left with a bounce light under it, a glossy highlight, two feathered wings, a face that
/// changes with the mood, a glow behind and a shadow on the ground under it. Each overlay theme gives it its own palette and
/// finish: Classic gold, Modern pearl, Tactical a cyan hologram in a reticle, Wacky a sticker with a white edge and a hard
/// shadow, Glitch phosphor green with scan lines and a colour split when it reacts.
///
/// It idles (floats, blinks, its wings flutter), leans in while a fight is on, dozes after a long quiet, and plays a
/// reaction for a moment (<see cref="React"/>): every course goes through Motion (Ui/Motion.cs), so a render can hold any
/// frame (KL_MOTION_AT, Motion.FrozenMs) and nothing moves with Windows's animations off.
/// </summary>
public sealed class CompanionSprite : Grid
{
    public enum Mood { Idle, Focused, Sleepy }
    enum Face { Idle, Focused, Sleepy, Happy, Excited, Proud, Fired, Alert, Fierce, Sad, Dazed }

    /// <summary>The sprite's side at 100%.</summary>
    public const double Side = 96;
    /// <summary>A reaction's course, start to back at rest.</summary>
    public const double ReactMs = 1800;
    /// <summary>A flourish of the idle (one float), and its frames a second: a slow float needs few.</summary>
    const double IdleMs = 2400;
    const int IdleFps = 10;
    /// <summary>A reaction's, a blink's and a bubble's frames a second: smooth enough for a short moment, light over the game.</summary>
    internal const int ReactFps = 24;
    const double Cx = 48, Cy = 50;

    /// <summary>A theme's look: the body's light, base, deep and rim, the wings, the glow, the face's ink, the cheeks, the rim's width.</summary>
    sealed record Palette(Color Light, Color Base, Color Deep, Color Rim, Color Wing, Color WingEdge, Color Glow, Color Ink, Color Cheek, double RimW);

    static Palette PaletteOf(string theme) => theme switch
    {
        HudThemes.Modern => new(Rgb(0xFFFFFF), Rgb(0xE6EDF8), Rgb(0x9FB1CE), Rgb(0x56657F), Rgb(0xFFFFFF), Rgb(0xB9C9E6), Rgb(0xA9C8FF), Rgb(0x1D2433), Rgb(0xFF9DB4), 1.1),
        HudThemes.Tactical => new(Rgb(0xE8FFFF), Rgb(0x3FE0F5), Rgb(0x0B8EAF), Rgb(0x05465A), Rgb(0xB8F7FF), Rgb(0x3FE0F5), Rgb(0x3FE0F5), Rgb(0x03222C), Rgb(0xFF8FA6), 1.2),
        HudThemes.Wacky => new(Rgb(0xFFE3F7), Rgb(0xFF52C9), Rgb(0xC21C8E), Rgb(0x1C0A2B), Rgb(0xA4FF6E), Rgb(0x34C82A), Rgb(0xFF52C9), Rgb(0x1C0A2B), Rgb(0xFFE14D), 2.2),
        HudThemes.Glitch => new(Rgb(0xE2FFE8), Rgb(0x3CFF8A), Rgb(0x0FA852), Rgb(0x04381B), Rgb(0xA8FFC8), Rgb(0x3CFF8A), Rgb(0x3CFF8A), Rgb(0x021A0C), Rgb(0x3CFF8A), 1.2),
        _ => new(Rgb(0xFFF6D8), Rgb(0xF5B942), Rgb(0xC07812), Rgb(0x6B3E06), Rgb(0xFFE8AC), Rgb(0xF5B942), Rgb(0xF5B942), Rgb(0x2A1606), Rgb(0xFF7A6B), 1.2),
    };

    public string Theme { get; }
    readonly Palette _p;
    readonly Canvas _stage = new() { Width = Side, Height = Side, ClipToBounds = false };
    readonly Canvas _rig = new();
    readonly ScaleTransform _squash = new(1, 1, 0, 22);
    readonly RotateTransform _lean = new(0, 0, 10);
    readonly TranslateTransform _move = new();
    readonly RotateTransform _wingL = new(), _wingR = new();
    readonly Ellipse _aura, _burst, _shadow, _ring, _ring2;
    /// <summary>The white flash over the body as a moment lands (its impact).</summary>
    readonly Path _flash;
    readonly Path _tint, _ghostR, _ghostC;
    readonly Canvas _eyes = new();
    readonly ScaleTransform _eyeScale = new(1, 1, 0, 1);
    /// <summary>Where its eyes look (a glance during an idle flourish).</summary>
    readonly TranslateTransform _eyeLook = new();
    /// <summary>The flourish under way: 0 a glance around, 1 a smile, 2 a plain float (they take turns).</summary>
    int _variant;
    readonly List<UIElement> _openEyes = new();
    readonly Path _happyEyes, _closedEyes, _xEyes, _browsFierce, _browsSad, _browsFocus;
    readonly Ellipse _cheekL, _cheekR, _mouthO;
    readonly Path _mouth;
    readonly Path _crown, _alert, _cloud, _tear;
    readonly Canvas _rays = new(), _flames = new(), _rain = new(), _dizzy = new(), _zs = new(), _slashes = new();
    readonly RotateTransform _raysTurn = new();
    readonly List<(Path P, TranslateTransform At, ScaleTransform Size, RotateTransform Turn)> _sparks = new();
    readonly Canvas _reticle = new();
    readonly ScaleTransform _shadowSize = new(1, 1, 16, 3.5);
    readonly SolidColorBrush _sparkInk = new(), _ringInk = new(), _burstInk = new();
    readonly RadialGradientBrush _burstFill;

    Mood _mood = Mood.Idle;
    Face _face = Face.Idle;
    CompanionKind? _playing;
    Color _accent;
    // the parts the idle and a reaction each put in, added up into the transforms (Compose)
    double _bobY, _wingIdle, _auraIdle = .6, _reX, _reY, _reSx = 1, _reSy = 1, _reTurn, _reWing, _reAura;
    readonly DispatcherTimer _blink, _back, _flourish;
    int _flourishes;
    bool _running;
    int _blinks;

    /// <summary>A reaction ended and the companion is at rest again.</summary>
    public event Action? Rested;

    public CompanionSprite(string theme)
    {
        Theme = theme is HudThemes.Modern or HudThemes.Tactical or HudThemes.Wacky or HudThemes.Glitch ? theme : HudThemes.Classic;
        _p = PaletteOf(Theme);
        Width = Side; Height = Side;
        ClipToBounds = false;
        IsHitTestVisible = true;
        Background = Brushes.Transparent;   // the whole square is the companion's to grab (a drag; the grip with click-through on)
        Tag = "companion:" + Theme;
        Children.Add(_stage);

        // the glow behind: the theme's, and a burst in a moment's own colour over it
        _aura = Disc(84, 84, Glowing(_p.Glow, .55));
        Place(_aura, Cx, Cy);
        _burstFill = Glowing(Colors.White, .9);
        _burst = Disc(96, 96, _burstFill); _burst.Opacity = 0;
        Place(_burst, Cx, Cy);
        _stage.Children.Add(_aura);
        _stage.Children.Add(_burst);

        // a new best's rays, turning slowly behind it
        for (int i = 0; i < 12; i++)
        {
            var ray = new Path { Data = Geometry.Parse("M -2.2,-14 L 2.2,-14 L 0.6,-46 L -0.6,-46 Z"), Fill = Frozen(Color.FromArgb(0x70, 0xFF, 0xD7, 0x6A)), RenderTransform = new RotateTransform(i * 30) };
            _rays.Children.Add(ray);
        }
        _rays.RenderTransform = _raysTurn;
        Canvas.SetLeft(_rays, Cx); Canvas.SetTop(_rays, Cy);
        _rays.Opacity = 0;
        _stage.Children.Add(_rays);

        // Tactical: a reticle's four corners round it
        if (Theme == HudThemes.Tactical)
        {
            var rink = Frozen(Color.FromArgb(0xB0, _p.Base.R, _p.Base.G, _p.Base.B));
            foreach (var (sx, sy) in new[] { (-1, -1), (1, -1), (-1, 1), (1, 1) })
                _reticle.Children.Add(new Path { Data = Geometry.Parse(Fmt("M {0},{1} L {0},{2} M {0},{1} L {3},{1}", sx * 40, sy * 40, sy * 31, sx * 31)), Stroke = rink, StrokeThickness = 1.4 });
            _reticle.Children.Add(new Ellipse { Width = 3, Height = 3, Fill = rink, RenderTransform = new TranslateTransform(-1.5, -43.5) });
            Canvas.SetLeft(_reticle, Cx); Canvas.SetTop(_reticle, Cy);
            _reticle.Opacity = .65;
            _stage.Children.Add(_reticle);
        }

        // the shadow on the ground
        _shadow = Disc(32, 7, new RadialGradientBrush(Color.FromArgb(0x80, 0, 0, 0), Color.FromArgb(0, 0, 0, 0)));
        Place(_shadow, Cx, 88);
        _shadow.RenderTransform = _shadowSize;
        _stage.Children.Add(_shadow);

        // the body and all that moves with it
        Canvas.SetLeft(_rig, Cx); Canvas.SetTop(_rig, Cy);
        _rig.RenderTransform = new TransformGroup { Children = { _squash, _lean, _move } };
        _stage.Children.Add(_rig);

        // flames round its head and shoulders (a streak): in front of the wings, behind the body
        var flameFill = new LinearGradientBrush { StartPoint = new Point(0.5, 1), EndPoint = new Point(0.5, 0) };
        flameFill.GradientStops.Add(new GradientStop(Color.FromArgb(0xFF, 0xFF, 0x4A, 0x0E), 0));
        flameFill.GradientStops.Add(new GradientStop(Color.FromArgb(0xF0, 0xFF, 0x9A, 0x1E), 0.45));
        flameFill.GradientStops.Add(new GradientStop(Color.FromArgb(0xC0, 0xFF, 0xE8, 0x8A), 0.8));
        flameFill.GradientStops.Add(new GradientStop(Color.FromArgb(0x00, 0xFF, 0xF6, 0xC8), 1));
        flameFill.Freeze();
        for (int i = 0; i < 7; i++)
        {
            double ang = -84 + i * 28;   // a crown of flames over the top
            var fl = new Path
            {
                Data = Geometry.Parse(i % 2 == 0 ? "M 0,0 C -7,-6 -5,-14 0,-24 C 1,-17 5,-14 4,-8 C 6,-5 5,-2 0,0 Z" : "M 0,0 C -5,-5 -3,-11 0,-18 C 3,-11 5,-5 0,0 Z"),
                Fill = flameFill,
                RenderTransform = new TransformGroup { Children = { new ScaleTransform(1, 1), new TranslateTransform(0, -10), new RotateTransform(ang) } },
            };
            _flames.Children.Add(fl);
        }
        _flames.Opacity = 0;

        // the wings, behind the body: feathered, light at the root, fading to the tips
        _rig.Children.Add(Wing(-1, _wingL));
        _rig.Children.Add(Wing(1, _wingR));
        _rig.Children.Add(_flames);

        var body = Geometry.Parse(BodyPath);
        if (Theme == HudThemes.Wacky)
        {
            // a sticker: a hard shadow, then a thick white edge round the body
            _rig.Children.Add(new Path { Data = body, Fill = Frozen(Color.FromArgb(0x9A, 0x12, 0x04, 0x20)), RenderTransform = new TranslateTransform(2.6, 3.2) });
            _rig.Children.Add(new Path { Data = body, Stroke = Brushes.White, StrokeThickness = 6, StrokeLineJoin = PenLineJoin.Round });
        }
        if (Theme == HudThemes.Glitch)
        {
            // the colour split: a red and a cyan copy behind, apart only while it reacts
            _ghostR = new Path { Data = body, Fill = Frozen(Color.FromArgb(0x90, 0xFF, 0x2A, 0x5A)), RenderTransform = new TranslateTransform(0, 0) };
            _ghostC = new Path { Data = body, Fill = Frozen(Color.FromArgb(0x90, 0x2A, 0xE8, 0xFF)), RenderTransform = new TranslateTransform(0, 0) };
            _rig.Children.Add(_ghostR); _rig.Children.Add(_ghostC);
        }
        else { _ghostR = new Path(); _ghostC = new Path(); }
        var bodyFill = new RadialGradientBrush { GradientOrigin = new Point(0.34, 0.26), Center = new Point(0.42, 0.36), RadiusX = 0.78, RadiusY = 0.78 };
        bodyFill.GradientStops.Add(new GradientStop(_p.Light, 0));
        bodyFill.GradientStops.Add(new GradientStop(_p.Base, 0.42));
        bodyFill.GradientStops.Add(new GradientStop(_p.Deep, 0.92));
        bodyFill.GradientStops.Add(new GradientStop(_p.Rim, 1.1));
        bodyFill.Freeze();
        _rig.Children.Add(new Path { Data = body, Fill = bodyFill, Stroke = Frozen(_p.Rim), StrokeThickness = _p.RimW, StrokeLineJoin = PenLineJoin.Round, Tag = "companion-body" });
        // the bounce light under it and the glow at its heart
        var bounce = Disc(26, 9, new RadialGradientBrush(Color.FromArgb(0x90, _p.Light.R, _p.Light.G, _p.Light.B), Color.FromArgb(0, _p.Light.R, _p.Light.G, _p.Light.B)));
        Place(bounce, 0, 17);
        _rig.Children.Add(bounce);
        if (Theme == HudThemes.Glitch)
        {
            var lines = new DrawingBrush(new GeometryDrawing(Frozen(Color.FromArgb(0x48, 0x00, 0x24, 0x10)), null, new RectangleGeometry(new Rect(0, 0, 4, 1))))
            { TileMode = TileMode.Tile, Viewport = new Rect(0, 0, 4, 2.6), ViewportUnits = BrushMappingMode.Absolute, Viewbox = new Rect(0, 0, 4, 2.6), ViewboxUnits = BrushMappingMode.Absolute };
            lines.Freeze();
            _rig.Children.Add(new Path { Data = body, Fill = lines, IsHitTestVisible = false });
        }
        // a warm flush (a streak) or a grey cast (a wipe, a fall) over the body
        _tint = new Path { Data = body, Fill = new SolidColorBrush(Colors.Transparent), Opacity = 0 };
        _rig.Children.Add(_tint);
        _flash = new Path { Data = body, Fill = new SolidColorBrush(Colors.White), Opacity = 0, IsHitTestVisible = false };
        _rig.Children.Add(_flash);
        // the gloss
        var shine = Disc(11, 6.5, new RadialGradientBrush(Color.FromArgb(0xE0, 0xFF, 0xFF, 0xFF), Color.FromArgb(0x00, 0xFF, 0xFF, 0xFF)));
        Place(shine, -8, -6);
        shine.RenderTransform = new RotateTransform(-38, 5.5, 3.25);
        _rig.Children.Add(shine);
        var glint = Disc(3, 3, Brushes.White); glint.Opacity = .9;
        Place(glint, -10.5, -9.5);
        _rig.Children.Add(glint);

        // the face
        var ink = Frozen(_p.Ink);
        Canvas.SetTop(_eyes, 1);
        _eyes.RenderTransform = new TransformGroup { Children = { _eyeScale, _eyeLook } };
        foreach (double ex in new[] { -7.5, 7.5 })
        {
            var eye = new Rectangle { Width = 6.6, Height = 9.4, RadiusX = 3.3, RadiusY = 3.3, Fill = ink };
            Canvas.SetLeft(eye, ex - 3.3); Canvas.SetTop(eye, -4.7);
            var light = Disc(2.6, 2.6, Brushes.White);
            Place(light, ex - 1.1, -2.2);
            _eyes.Children.Add(eye); _eyes.Children.Add(light);
            _openEyes.Add(eye); _openEyes.Add(light);
        }
        _rig.Children.Add(_eyes);
        Path Stroked(string data, double w = 2) => new() { Data = Geometry.Parse(data), Stroke = ink, StrokeThickness = w, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round, StrokeLineJoin = PenLineJoin.Round, Visibility = Visibility.Collapsed };
        _happyEyes = Stroked("M -11,3 Q -7.5,-3 -4,3 M 4,3 Q 7.5,-3 11,3", 2.2);
        _closedEyes = Stroked("M -11,0.5 Q -7.5,4 -4,0.5 M 4,0.5 Q 7.5,4 11,0.5", 1.9);
        _xEyes = Stroked("M -10,-2 L -5,3 M -5,-2 L -10,3 M 5,-2 L 10,3 M 10,-2 L 5,3", 2);
        _browsFierce = Stroked("M -12,-6.5 L -4,-3.2 M 12,-6.5 L 4,-3.2", 2.1);
        _browsSad = Stroked("M -11.5,-3.6 L -4,-6.6 M 11.5,-3.6 L 4,-6.6", 1.8);
        _browsFocus = Stroked("M -11.5,-5.6 L -4.5,-4.4 M 11.5,-5.6 L 4.5,-4.4", 1.7);
        foreach (var e in new[] { _happyEyes, _closedEyes, _xEyes, _browsFierce, _browsSad, _browsFocus }) _rig.Children.Add(e);
        var cheek = Frozen(Color.FromArgb(0x7A, _p.Cheek.R, _p.Cheek.G, _p.Cheek.B));
        _cheekL = Disc(6, 3.4, cheek); Place(_cheekL, -12.5, 7.5);
        _cheekR = Disc(6, 3.4, cheek); Place(_cheekR, 12.5, 7.5);
        _rig.Children.Add(_cheekL); _rig.Children.Add(_cheekR);
        _mouth = new Path { Stroke = ink, StrokeThickness = 1.8, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round, StrokeLineJoin = PenLineJoin.Round };
        Canvas.SetTop(_mouth, 9.5);
        _mouthO = Disc(3.4, 4, ink); Place(_mouthO, 0, 10.5); _mouthO.Visibility = Visibility.Collapsed;
        _rig.Children.Add(_mouth); _rig.Children.Add(_mouthO);

        // what a moment adds round its head: a crown, an alert mark, a rain cloud and its rain, a tear, dizzy stars, z's
        var crownFill = new LinearGradientBrush(Rgb(0xFFF1A8), Rgb(0xE59A12), 90); crownFill.Freeze();
        _crown = new Path
        {
            Data = Geometry.Parse("M -10,4 L -11.5,-6 L -5.5,-1.2 L 0,-9.5 L 5.5,-1.2 L 11.5,-6 L 10,4 Z M -11.5,-6 m -1.6,0 a 1.6,1.6 0 1 0 3.2,0 a 1.6,1.6 0 1 0 -3.2,0 M 0,-9.5 m -1.8,0 a 1.8,1.8 0 1 0 3.6,0 a 1.8,1.8 0 1 0 -3.6,0 M 11.5,-6 m -1.6,0 a 1.6,1.6 0 1 0 3.2,0 a 1.6,1.6 0 1 0 -3.2,0"),
            Fill = crownFill, Stroke = Frozen(Rgb(0x7A4A04)), StrokeThickness = 1.1, StrokeLineJoin = PenLineJoin.Round, Opacity = 0, RenderTransform = new TranslateTransform(0, -31),
        };
        _rig.Children.Add(_crown);
        _alert = new Path
        {
            Data = Geometry.Parse("M -2.6,-12 Q 0,-14 2.6,-12 L 1.6,1 Q 0,2.4 -1.6,1 Z M 0,4.2 m -2.4,0 a 2.4,2.4 0 1 0 4.8,0 a 2.4,2.4 0 1 0 -4.8,0"),
            Fill = Frozen(Rgb(0xFFC24A)), Stroke = Frozen(Rgb(0x5A3200)), StrokeThickness = 1.2, Opacity = 0, RenderTransform = new TransformGroup { Children = { new ScaleTransform(1, 1), new TranslateTransform(0, -34) } },
        };
        _rig.Children.Add(_alert);
        var cloudGeo = new GeometryGroup { FillRule = FillRule.Nonzero, Children = { new EllipseGeometry(new Point(-6, 0), 6.5, 5.5), new EllipseGeometry(new Point(2, -3), 8, 7), new EllipseGeometry(new Point(9, 1), 5.5, 4.6), new RectangleGeometry(new Rect(-10, 0, 23, 5.2), 2.6, 2.6) } };
        _cloud = new Path { Data = cloudGeo, Fill = new LinearGradientBrush(Rgb(0xC9D2E2), Rgb(0x7F8AA0), 90), Stroke = Frozen(Rgb(0x3E4658)), StrokeThickness = 1, Opacity = 0, RenderTransform = new TranslateTransform(0, -38) };
        _rig.Children.Add(_cloud);
        for (int i = 0; i < 4; i++)
            _rain.Children.Add(new Line { X1 = 0, Y1 = 0, X2 = -1.2, Y2 = 4.5, Stroke = Frozen(Rgb(0x8FC2FF)), StrokeThickness = 1.6, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round, RenderTransform = new TranslateTransform(-7 + i * 5, -30) });
        _rain.Opacity = 0;
        _rig.Children.Add(_rain);
        _tear = new Path { Data = Geometry.Parse("M 0,-3.2 C 1.8,-0.6 2.6,0.8 2.6,1.9 A 2.6,2.6 0 1 1 -2.6,1.9 C -2.6,0.8 -1.8,-0.6 0,-3.2 Z"), Fill = Frozen(Rgb(0x9FD4FF)), Stroke = Frozen(Rgb(0x2F6FB0)), StrokeThickness = .8, Opacity = 0, RenderTransform = new TranslateTransform(10, 6) };
        _rig.Children.Add(_tear);
        for (int i = 0; i < 3; i++)
            _dizzy.Children.Add(new Path { Data = Geometry.Parse(StarPath(3.4)), Fill = Frozen(Rgb(0xFFE07A)), Stroke = Frozen(Rgb(0x7A5200)), StrokeThickness = .6, RenderTransform = new TranslateTransform() });
        _dizzy.Opacity = 0;
        _rig.Children.Add(_dizzy);
        for (int i = 0; i < 2; i++)
            _zs.Children.Add(new Path { Data = Geometry.Parse(i == 0 ? "M 0,0 L 5,0 L 0,5 L 5,5" : "M 0,0 L 3.6,0 L 0,3.6 L 3.6,3.6"), Stroke = Frozen(Color.FromArgb(0xE0, _p.Light.R, _p.Light.G, _p.Light.B)), StrokeThickness = 1.5, StrokeLineJoin = PenLineJoin.Round, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round, RenderTransform = new TranslateTransform() });
        _zs.Opacity = 0;
        _rig.Children.Add(_zs);

        // over everything: a ring, sparks, the slashes of a takedown
        _ring = new Ellipse { Stroke = _ringInk, StrokeThickness = 2.2, Opacity = 0, Width = 20, Height = 20 };
        Place(_ring, Cx, Cy);
        _stage.Children.Add(_ring);
        _ring2 = new Ellipse { Stroke = _ringInk, StrokeThickness = 1.6, Opacity = 0, Width = 20, Height = 20 };
        Place(_ring2, Cx, Cy);
        _stage.Children.Add(_ring2);
        for (int i = 0; i < 10; i++)
        {
            var at = new TranslateTransform(); var size = new ScaleTransform(0, 0); var turn = new RotateTransform();
            var sp = new Path { Data = Geometry.Parse(StarPath(i % 2 == 0 ? 5.2 : 3.6)), Fill = _sparkInk, Opacity = 0, RenderTransform = new TransformGroup { Children = { size, turn, at } } };
            Canvas.SetLeft(sp, Cx); Canvas.SetTop(sp, Cy);
            _stage.Children.Add(sp);
            _sparks.Add((sp, at, size, turn));
        }
        // a takedown: two crossed blades over its head, popping in on a red burst
        var steel = new LinearGradientBrush(Rgb(0xFFFFFF), Rgb(0x9AA6BC), 0); steel.Freeze();
        var hiltInk = Frozen(Rgb(0xD8343A)); var edge = Frozen(Rgb(0x2A1A22));
        var burst = Disc(30, 30, Glowing(Rgb(0xFF4A4A), .75));
        Place(burst, 0, 0);
        _slashes.Children.Add(burst);
        foreach (var ang in new[] { -42.0, 42.0 })
        {
            var sword = new Canvas { RenderTransform = new RotateTransform(ang) };
            sword.Children.Add(new Path { Data = Geometry.Parse("M -1.9,-13 L 0,-17 L 1.9,-13 L 1.9,3.5 L -1.9,3.5 Z"), Fill = steel, Stroke = edge, StrokeThickness = .9, StrokeLineJoin = PenLineJoin.Round });
            sword.Children.Add(new Path { Data = Geometry.Parse("M -6,3.5 L 6,3.5 L 6,6 L -6,6 Z"), Fill = Frozen(Rgb(0xF5C04A)), Stroke = edge, StrokeThickness = .9, StrokeLineJoin = PenLineJoin.Round });
            sword.Children.Add(new Path { Data = Geometry.Parse("M -1.3,6 L 1.3,6 L 1.3,11.5 L -1.3,11.5 Z M 0,13.2 m -2,0 a 2,2 0 1 0 4,0 a 2,2 0 1 0 -4,0"), Fill = hiltInk, Stroke = edge, StrokeThickness = .9 });
            _slashes.Children.Add(sword);
        }
        _slashes.RenderTransform = new TransformGroup { Children = { new ScaleTransform(1, 1), new RotateTransform(0), new TranslateTransform(0, -34) } };
        _slashes.Opacity = 0;
        _rig.Children.Add(_slashes);

        SetFace(Face.Idle);
        Compose();


        _blink = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(3900) };
        _blink.Tick += (_, _) => Blink();
        _flourish = new DispatcherTimer(DispatcherPriority.Background);
        _flourish.Tick += (_, _) => Flourish();
        _back = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(ReactMs) };
        _back.Tick += (_, _) => { _back.Stop(); Rest(); };
        Unloaded += (_, _) => Stop();
    }

    /* ---------------- shapes ---------------- */

    /// <summary>The body: a teardrop of aether, its tip curling up and to the right like a flame.</summary>
    const string BodyPath = "M 3.5,-25 C 4.5,-18 21,-13 21,2.5 C 21,15 12,22 0,22 C -12,22 -21,15 -21,2.5 C -21,-11 -6,-14 -1,-20 C 0,-22 1.8,-23.6 3.5,-25 Z";

    static string Fmt(string f, params double[] v) => string.Format(System.Globalization.CultureInfo.InvariantCulture, f, v.Cast<object>().ToArray());

    /// <summary>A four-pointed star of radius <paramref name="r"/>, its waist pinched.</summary>
    static string StarPath(double r) { double k = r * 0.18; return Fmt("M 0,{0} Q {1},{2} {3},0 Q {1},{1} 0,{3} Q {2},{1} {0},0 Q {2},{2} 0,{0} Z", -r, k, -k, r); }

    /// <summary>A wing on <paramref name="side"/> (-1 left, 1 right): three feathers from the root at the body's shoulder.</summary>
    Path Wing(int side, RotateTransform turn)
    {
        string d = Fmt("M 0,0 C {0},-4 {1},-15 {2},-18 C {3},-13 {4},-10 {5},-8 C {6},-9 {7},-9 {8},-6.5 C {9},-3.5 {10},-1.5 {11},0 C {12},1.5 {13},3.5 {14},7 C {15},6 {16},4 0,2.2 Z",
            side * 6, side * 15, side * 26, side * 23, side * 21, side * 20, side * 24, side * 28, side * 30, side * 26, side * 23, side * 19, side * 22, side * 24, side * 25, side * 17, side * 9);
        var fill = new LinearGradientBrush { StartPoint = new Point(side < 0 ? 1 : 0, 0.7), EndPoint = new Point(side < 0 ? 0 : 1, 0.1) };
        fill.GradientStops.Add(new GradientStop(Color.FromArgb(0xF4, _p.Wing.R, _p.Wing.G, _p.Wing.B), 0));
        fill.GradientStops.Add(new GradientStop(Color.FromArgb(0xC8, _p.WingEdge.R, _p.WingEdge.G, _p.WingEdge.B), 0.65));
        fill.GradientStops.Add(new GradientStop(Color.FromArgb(0x50, _p.WingEdge.R, _p.WingEdge.G, _p.WingEdge.B), 1));
        fill.Freeze();
        var w = new Path { Data = Geometry.Parse(d), Fill = fill, Stroke = Frozen(Color.FromArgb(Theme == HudThemes.Wacky ? (byte)0xFF : (byte)0x90, _p.Rim.R, _p.Rim.G, _p.Rim.B)), StrokeThickness = Theme == HudThemes.Wacky ? 1.6 : .9, StrokeLineJoin = PenLineJoin.Round, RenderTransform = turn };
        Canvas.SetLeft(w, side * 13.5); Canvas.SetTop(w, -3);
        return w;
    }

    static Ellipse Disc(double w, double h, Brush fill) => new() { Width = w, Height = h, Fill = fill };
    static void Place(FrameworkElement el, double cx, double cy) { Canvas.SetLeft(el, cx - el.Width / 2); Canvas.SetTop(el, cy - el.Height / 2); }
    static RadialGradientBrush Glowing(Color c, double a)
    {
        var g = new RadialGradientBrush();
        g.GradientStops.Add(new GradientStop(Color.FromArgb((byte)(255 * a), c.R, c.G, c.B), 0));
        g.GradientStops.Add(new GradientStop(Color.FromArgb((byte)(255 * a * .35), c.R, c.G, c.B), 0.45));
        g.GradientStops.Add(new GradientStop(Color.FromArgb(0, c.R, c.G, c.B), 1));
        return g;
    }

    /* ---------------- the face ---------------- */

    void SetFace(Face f)
    {
        _face = f;
        bool open = f is Face.Idle or Face.Focused or Face.Excited or Face.Fired or Face.Alert or Face.Fierce or Face.Sad;
        foreach (var e in _openEyes) e.Visibility = open ? Visibility.Visible : Visibility.Collapsed;
        _happyEyes.Visibility = f is Face.Happy or Face.Proud ? Visibility.Visible : Visibility.Collapsed;
        _closedEyes.Visibility = f == Face.Sleepy ? Visibility.Visible : Visibility.Collapsed;
        _xEyes.Visibility = f == Face.Dazed ? Visibility.Visible : Visibility.Collapsed;
        _browsFierce.Visibility = f is Face.Fierce or Face.Fired ? Visibility.Visible : Visibility.Collapsed;
        _browsSad.Visibility = f == Face.Sad ? Visibility.Visible : Visibility.Collapsed;
        _browsFocus.Visibility = f == Face.Focused ? Visibility.Visible : Visibility.Collapsed;
        bool blush = f is Face.Happy or Face.Proud or Face.Excited;
        _cheekL.Visibility = _cheekR.Visibility = blush ? Visibility.Visible : Visibility.Collapsed;
        _eyeScale.ScaleX = _eyeScale.ScaleY = f == Face.Alert ? 1.22 : f == Face.Excited ? 1.12 : 1;
        _mouthO.Visibility = f is Face.Alert or Face.Sleepy ? Visibility.Visible : Visibility.Collapsed;
        _mouthO.RenderTransform = f == Face.Sleepy ? new ScaleTransform(.7, .6, 1.7, 2) : Transform.Identity;
        string mouth = f switch
        {
            Face.Happy or Face.Proud => "M -4.5,0 Q 0,4.6 4.5,0",
            Face.Excited => "M -5,-0.5 Q 0,7 5,-0.5 Z",
            Face.Fired => "M -4.5,0 Q 0,5 4.5,0 Z",
            Face.Fierce => "M -3.6,1.2 L 3.6,0.2",
            Face.Focused => "M -2.8,1 L 2.8,1",
            Face.Sad => "M -3.6,2.6 Q 0,-0.8 3.6,2.6",
            Face.Dazed => "M -3.8,1.5 Q -1.9,-0.2 0,1.5 Q 1.9,3.2 3.8,1.5",
            Face.Alert or Face.Sleepy => "",
            _ => "M -3.2,0 Q 0,2.8 3.2,0",
        };
        _mouth.Data = mouth.Length > 0 ? Geometry.Parse(mouth) : null;
        _mouth.Fill = f is Face.Excited or Face.Fired ? Frozen(Color.FromArgb(0xFF, (byte)Math.Min(255, _p.Ink.R + 0x50), _p.Ink.G, _p.Ink.B)) : null;
    }

    Face RestFace => _mood switch { Mood.Focused => Face.Focused, Mood.Sleepy => Face.Sleepy, _ => Face.Idle };

    /* ---------------- idle ---------------- */

    /// <summary>
    /// The idle: still between flourishes (one slow float with a flutter of its wings, every few seconds) and blinks, so it
    /// draws nothing most of the time (Steven, 2026-11-20: "lightweight - not bulky"). Started while it shows, stopped when
    /// it hides.
    /// </summary>
    public void Start()
    {
        if (_running) return;
        _running = true;
        Idle(0);
        if (!Motion.On || Motion.FrozenAt != null) return;
        _blink.Start();
        _flourish.Interval = TimeSpan.FromMilliseconds(1500);
        _flourish.Start();
    }

    public void Stop()
    {
        _running = false;
        Motion.Halt(_stage);
        _blink.Stop();
        _flourish.Stop();
    }

    /// <summary>A flourish: one float of the idle, then still till the next (sooner while a fight is on, later while it dozes).</summary>
    void Flourish()
    {
        int n = ++_flourishes;
        _flourish.Interval = TimeSpan.FromMilliseconds(IdleMs + (_mood == Mood.Focused ? 1800 : _mood == Mood.Sleepy ? 7000 : 4200) + (n * 911) % 1700);
        if (_playing != null) return;
        _variant = n % 3;
        Motion.Play(_stage, IdleMs, Idle, () => { Idle(0); _variant = 2; }, IdleFps);
    }

    /// <summary>Everything stopped: the idle and any reaction under way, which ends without saying so (no <see cref="Rested"/>).</summary>
    public void Quiet()
    {
        Stop();
        Motion.Halt(this);
        _back.Stop();
        if (_playing == null) return;
        _playing = null;
        Clear();
        SetFace(RestFace);
        Compose();
    }

    /// <summary>The mood at rest: idle, leaning in for a fight, dozing after a long quiet.</summary>
    public void SetMood(Mood m)
    {
        if (m == _mood) return;
        _mood = m;
        if (_playing == null) SetFace(RestFace);
        if (!Motion.Playing(_stage)) Idle(Motion.FrozenAt ?? 0);   // its rest pose for the mood (a flourish under way takes it up as it goes)
    }

    public Mood MoodNow => _mood;
    /// <summary>The reaction under way (null at rest): the tests and renders.</summary>
    public CompanionKind? Playing => _playing;
    internal string FaceForTest => _face.ToString();

    void Idle(double p)
    {
        double w = 2 * Math.PI * p;
        Expression(p);
        switch (_mood)
        {
            case Mood.Focused:
                _bobY = 2 * Math.Sin(w * 2); _wingIdle = 10 * Math.Sin(w * 4) - 4; _auraIdle = .72;
                _zs.Opacity = 0;
                break;
            case Mood.Sleepy:
                _bobY = 1.8 * Math.Sin(w) + 2; _wingIdle = 16 + 3 * Math.Sin(w); _auraIdle = .38;
                ZsAt(p);
                break;
            default:
                _bobY = 3.2 * Math.Sin(w); _wingIdle = 7 * Math.Sin(w * 2); _auraIdle = .62;
                _zs.Opacity = 0;
                break;
        }
        Compose();
    }

    /// <summary>
    /// Its face through an idle flourish, awake only (a reaction's face is left alone): a glance one way and back, or a smile
    /// in the middle of the float; between flourishes it looks ahead with its mood's face.
    /// </summary>
    void Expression(double p)
    {
        if (_playing != null || _mood == Mood.Sleepy) { _eyeLook.X = 0; return; }
        _eyeLook.X = _variant == 0 ? 2.2 * Math.Sin(2 * Math.PI * p) : 0;
        _eyeLook.Y = _variant == 0 ? -.6 * Math.Abs(Math.Sin(2 * Math.PI * p)) : 0;
        var want = _variant == 1 && p > .3 && p < .75 ? Face.Happy : RestFace;
        if (_face != want) SetFace(want);
    }

    /// <summary>Two z's float up and fade, one after the other, while it dozes.</summary>
    void ZsAt(double p)
    {
        _zs.Opacity = 1;
        for (int i = 0; i < _zs.Children.Count; i++)
        {
            double q = (p + i * 0.5) % 1;
            var z = (Path)_zs.Children[i];
            var at = (TranslateTransform)z.RenderTransform;
            at.X = 14 + 6 * q + i * 2; at.Y = -20 - 16 * q;
            z.Opacity = Math.Sin(Math.PI * q);
        }
    }

    void Blink()
    {
        _blink.Interval = TimeSpan.FromMilliseconds(3200 + (++_blinks * 1777) % 2600);   // never the same gap twice in a row
        if (_face is not (Face.Idle or Face.Focused) || !Motion.On) return;
        Motion.Play(_eyes, 170, p => _eyeScale.ScaleY = 1 - .9 * Math.Sin(Math.PI * p), () => _eyeScale.ScaleY = 1, ReactFps);
    }

    /* ---------------- reactions ---------------- */

    /// <summary>
    /// Plays a reaction: a hop and sparks for a rare drop (in its grade's colour), a crown and rays for a new best, a happy
    /// bounce and confetti for a boss down, flames for a streak, an alert mark for a timer, slashes for a takedown, a rain
    /// cloud for a wipe, dizzy stars for a fall. Then back to its mood. A reaction under way gives way to the new one.
    /// </summary>
    public void React(CompanionKind kind, Color accent)
    {
        Prepare(kind, accent);
        _back.Stop();
        if (Motion.On) Motion.Play(this, ReactMs, p => Frame(p * ReactMs), Rest, ReactFps);
        else { Frame(KeyMs(kind)); _back.Start(); }   // motion off: the moment's key frame, held as long as the reaction would play
    }

    /// <summary>A reaction's face, colours and inks, set once before its frames.</summary>
    void Prepare(CompanionKind kind, Color accent)
    {
        _playing = kind;
        _accent = accent;
        _sparkInk.Color = Lighten(accent, .25);
        _ringInk.Color = accent;
        SetBurst(kind == CompanionKind.Best ? Rgb(0xFFD25A) : accent);
        for (int i = 0; i < _sparks.Count; i++)
            _sparks[i].P.Fill = kind != CompanionKind.Kill ? _sparkInk : i % 3 == 0 ? Mint : i % 3 == 1 ? ConfettiGold : ConfettiPink;
        SetFace(kind switch
        {
            CompanionKind.Rare => Face.Excited, CompanionKind.Best => Face.Proud, CompanionKind.Kill => Face.Happy, CompanionKind.Streak => Face.Fired,
            CompanionKind.Timer => Face.Alert, CompanionKind.Pvp => Face.Fierce, CompanionKind.Wipe => Face.Sad, _ => Face.Dazed,
        });
        ((SolidColorBrush)_tint.Fill).Color = kind switch { CompanionKind.Streak => Rgb(0xFF6A1A), CompanionKind.Wipe or CompanionKind.Death => Rgb(0x50586A), _ => Colors.Transparent };
    }

    static readonly SolidColorBrush ConfettiGold = B(0xFFE07A), ConfettiPink = B(0xFF8FB8);

    void SetBurst(Color c)
    {
        _burstFill.GradientStops.Clear();
        _burstFill.GradientStops.Add(new GradientStop(Color.FromArgb(0xC0, c.R, c.G, c.B), 0));
        _burstFill.GradientStops.Add(new GradientStop(Color.FromArgb(0x50, c.R, c.G, c.B), 0.4));
        _burstFill.GradientStops.Add(new GradientStop(Color.FromArgb(0, c.R, c.G, c.B), 1));
    }

    static Color Lighten(Color c, double t) => Color.FromRgb((byte)(c.R + (255 - c.R) * t), (byte)(c.G + (255 - c.G) * t), (byte)(c.B + (255 - c.B) * t));

    /// <summary>The reaction's frame at <paramref name="t"/> ms from its start.</summary>
    void Frame(double t)
    {
        Clear();
        double fadeOut = 1 - Win(t, ReactMs - 380, ReactMs);   // everything a moment adds fades in its last 380 ms
        switch (_playing)
        {
            case CompanionKind.Rare:
                Hop(t, 0, 20); Hop(t, 760, 8);
                Flash(t, 330, Colors.White);
                _reWing = -34 * Math.Abs(Math.Sin(2 * Math.PI * t / 170)) * (1 - Win(t, 900, 1500));
                Sparks(t, 360, 700, 10, 42, 0);
                Ring(_ring, t, 360, 560, 26, 94); Ring(_ring2, t, 500, 560, 20, 78);
                _reAura = Math.Max(1.2 * Bump(t, 320, 520), .85 * Bump(t, 250, 1700));
                break;
            case CompanionKind.Best:
                Hop(t, 0, 13);
                Flash(t, 600, Rgb(0xFFF4C8));
                double power = Bump(t, 580, 860);   // the crown lands: it swells with it
                _reSx *= 1 + .14 * power; _reSy *= 1 + .14 * power;
                double drop = Win(t, 240, 640);
                ((TranslateTransform)_crown.RenderTransform).Y = -31 - 30 * (1 - Back(drop));
                _crown.Opacity = Math.Min(1, drop * 3) * fadeOut;
                _rays.Opacity = Win(t, 160, 520) * fadeOut;
                _raysTurn.Angle = t * 0.035;
                Ring(_ring, t, 580, 520, 30, 92); Ring(_ring2, t, 700, 520, 24, 80);
                Sparks(t, 600, 760, 16, 40, 18);
                _reAura = Math.Max(1.2 * Bump(t, 580, 780), .85 * Win(t, 200, 600) * fadeOut);
                _reWing = -16 * Math.Abs(Math.Sin(2 * Math.PI * t / 260)) * (1 - Win(t, 700, 1300));
                break;
            case CompanionKind.Kill:
                Hop(t, 0, 14); Hop(t, 600, 9);
                Flash(t, 0, Colors.White);
                Confetti(t, 330);
                _reAura = .45 * Bump(t, 200, 1500);
                _reWing = -20 * Math.Abs(Math.Sin(2 * Math.PI * t / 220)) * (1 - Win(t, 900, 1400));
                break;
            case CompanionKind.Streak:
                _flames.Opacity = Win(t, 0, 200) * fadeOut;
                for (int i = 0; i < _flames.Children.Count; i++)
                {
                    var g = (TransformGroup)_flames.Children[i].RenderTransform;
                    var s = (ScaleTransform)g.Children[0];
                    double f = Math.Abs(Math.Sin(2 * Math.PI * (t / 260 + i * 0.27)));
                    s.ScaleY = .7 + .6 * f; s.ScaleX = .9 + .25 * (1 - f);
                }
                _tint.Opacity = .26 * Win(t, 0, 240) * fadeOut;
                Embers(t);
                _reX = 3 * Math.Sin(2 * Math.PI * t / 60) * (1 - Win(t, 0, 700));
                Flash(t, 0, Rgb(0xFFD08A));
                _reWing = (-10 - 18 * Math.Abs(Math.Sin(2 * Math.PI * t / 150))) * fadeOut;   // raised, beating: clear of the window's edges as it shakes
                _reAura = .7 * Win(t, 0, 300) * fadeOut;
                Hop(t, 120, 6);
                break;
            case CompanionKind.Timer:
                Hop(t, 0, 7);
                double pop = Win(t, 60, 340);
                var ag = (TransformGroup)_alert.RenderTransform;
                var asz = (ScaleTransform)ag.Children[0];
                asz.ScaleX = asz.ScaleY = Back(pop);
                _alert.Opacity = Math.Min(1, pop * 4) * fadeOut;
                _reTurn = 12 * Math.Sin(2 * Math.PI * t / 220) * (1 - Win(t, 200, 1100));
                _reWing = -16 * (1 - Win(t, 900, 1300));   // wings up in alarm (and clear of the edges as it wobbles)
                Flash(t, 40, Rgb(0xFFE2A0));
                Ring(_ring, t, 0, 560, 30, 92); Ring(_ring2, t, 520, 560, 30, 82);
                _reAura = .6 * Bump(t, 0, 1400);
                break;
            case CompanionKind.Pvp:
                _reX = t < 170 ? 10 * Motion.Out(t / 170) : 10 * (1 - Motion.Out(Win(t, 170, 620)));
                _reSx = 1 + .16 * Bump(t, 0, 300); _reSy = 1 - .14 * Bump(t, 0, 300);
                _reY = 1.6 * Math.Sin(2 * Math.PI * t / 50) * Bump(t, 120, 520);   // the clash shakes it
                Flash(t, 120, Rgb(0xFFD0D0));
                Ring(_ring, t, 140, 480, 24, 84);
                double clash = Win(t, 80, 400);
                var sg = (TransformGroup)_slashes.RenderTransform;
                ((ScaleTransform)sg.Children[0]).ScaleX = ((ScaleTransform)sg.Children[0]).ScaleY = Back(clash);
                ((RotateTransform)sg.Children[1]).Angle = -30 * (1 - Motion.Out(clash));
                _slashes.Opacity = Math.Min(1, clash * 4) * fadeOut;
                _slashes.Children[0].Opacity = Bump(t, 220, 900);
                Sparks(t, 330, 520, 10, 30, 22);
                _reAura = Math.Max(1.1 * Bump(t, 100, 300), .7 * Bump(t, 120, 1300));
                _reWing = -26 * Math.Abs(Math.Sin(2 * Math.PI * t / 170)) * (1 - Win(t, 500, 1000));
                break;
            case CompanionKind.Wipe:
                double sink = Motion.Out(Win(t, 0, 520)) * fadeOut;
                _reY = 6 * sink; _reSy = 1 - .08 * sink; _reSx = 1 + .02 * sink; _reWing = 20 * sink;
                _tint.Opacity = .5 * sink;
                double cloud = Win(t, 100, 480);
                ((TranslateTransform)_cloud.RenderTransform).Y = -44 + 8 * Back(cloud) - 6 * sink;
                _cloud.Opacity = Math.Min(1, cloud * 3) * fadeOut;
                _rain.Opacity = Win(t, 420, 560) * fadeOut;
                for (int i = 0; i < _rain.Children.Count; i++)
                {
                    double q = (t / 420 + i * 0.31) % 1;
                    var r = (TranslateTransform)_rain.Children[i].RenderTransform;
                    r.Y = -32 + 22 * q - 6 * sink; r.X = -7 + i * 5 - 1.5 * q;
                    _rain.Children[i].Opacity = Math.Sin(Math.PI * q);
                }
                double fall = Win(t, 700, 1500);
                ((TranslateTransform)_tear.RenderTransform).Y = 6 + 14 * fall * fall;
                _tear.Opacity = Bump(t, 600, 1600);
                _reAura = -.3 * sink;
                break;
            case CompanionKind.Death:
                double tip = Motion.Out(Win(t, 0, 420)) * fadeOut;
                _reTurn = -9 * tip + 3.5 * Math.Sin(2 * Math.PI * t / 520) * tip; _reY = 5 * tip; _reSy = 1 - .06 * tip; _reWing = 52 * tip;
                Flash(t, 0, Rgb(0xFF8A8A));
                _tint.Opacity = .38 * tip;
                _dizzy.Opacity = Win(t, 200, 420) * fadeOut;
                for (int i = 0; i < _dizzy.Children.Count; i++)
                {
                    double a = t / 520 * 2 * Math.PI + i * 2 * Math.PI / 3;
                    var d = (TranslateTransform)_dizzy.Children[i].RenderTransform;
                    d.X = 14 * Math.Cos(a); d.Y = -31 + 4 * Math.Sin(a);
                    _dizzy.Children[i].Opacity = .55 + .45 * Math.Sin(a);
                }
                _reAura = -.3 * tip;
                break;
        }
        if (Theme == HudThemes.Glitch)
        {
            // the colour split: apart in jolts at the start, as the reaction lands
            double k = Bump(t, 0, 520) * (Math.Sin(t / 37) > 0 ? 1 : .35);
            ((TranslateTransform)_ghostR.RenderTransform).X = -2.6 * k; ((TranslateTransform)_ghostR.RenderTransform).Y = .6 * k;
            ((TranslateTransform)_ghostC.RenderTransform).X = 2.6 * k; ((TranslateTransform)_ghostC.RenderTransform).Y = -.6 * k;
        }
        if (Theme == HudThemes.Tactical) _reticle.Opacity = .65 + .35 * Bump(t, 0, 600);
        _burst.Opacity = Math.Max(0, _reAura);
        Compose();
    }

    /// <summary>What a reaction adds, cleared before each of its frames and at its end.</summary>
    void Clear()
    {
        _reX = _reY = _reTurn = _reWing = _reAura = 0; _reSx = _reSy = 1;
        _crown.Opacity = _alert.Opacity = _cloud.Opacity = _tear.Opacity = 0;
        _rays.Opacity = _flames.Opacity = _rain.Opacity = _dizzy.Opacity = _slashes.Opacity = 0;
        _tint.Opacity = 0; _ring.Opacity = 0; _ring2.Opacity = 0; _burst.Opacity = 0; _flash.Opacity = 0;
        foreach (var s in _sparks) s.P.Opacity = 0;
        if (Theme == HudThemes.Glitch)
        {
            ((TranslateTransform)_ghostR.RenderTransform).X = ((TranslateTransform)_ghostC.RenderTransform).X = 0;
            ((TranslateTransform)_ghostR.RenderTransform).Y = ((TranslateTransform)_ghostC.RenderTransform).Y = 0;
        }
        if (Theme == HudThemes.Tactical) _reticle.Opacity = .65;
    }

    /// <summary>For the tests: the reaction under way ends now (and the next one waiting starts).</summary>
    internal void EndNow() { Motion.Halt(this); if (_playing != null) Rest(); }

    void Rest()
    {
        _back.Stop();
        _playing = null;
        Clear();
        SetFace(RestFace);
        Compose();
        Rested?.Invoke();
    }

    /// <summary>0 before <paramref name="a"/>, 1 after <paramref name="b"/>, a straight line between.</summary>
    static double Win(double t, double a, double b) => b <= a ? (t >= b ? 1 : 0) : Math.Clamp((t - a) / (b - a), 0, 1);
    /// <summary>A rise and fall between <paramref name="a"/> and <paramref name="b"/>.</summary>
    static double Bump(double t, double a, double b) => t <= a || t >= b ? 0 : Math.Sin(Math.PI * (t - a) / (b - a));
    /// <summary>An ease that overshoots a little and settles (a pop).</summary>
    static double Back(double p) { p = Math.Clamp(p, 0, 1); const double s = 1.9; double q = p - 1; return 1 + (s + 1) * q * q * q + s * q * q; }

    /// <summary>A hop from <paramref name="t0"/>: a crouch, up stretched, down, a squash as it lands.</summary>
    void Hop(double t, double t0, double height)
    {
        double a = t - t0;
        if (a < 0 || a > 680) return;
        if (a < 80) { double k = a / 80; _reSy *= 1 - .22 * k; _reSx *= 1 + .15 * k; return; }
        if (a < 500) { double k = (a - 80) / 420; _reY -= height * Math.Sin(Math.PI * k); _reSy *= 1 + .16 * Math.Cos(Math.PI * k); _reSx *= 1 - .1 * Math.Cos(Math.PI * k); return; }
        double l = Math.Sin(Math.PI * (a - 500) / 180); _reSy *= 1 - .24 * l; _reSx *= 1 + .17 * l;
    }

    /// <summary>The sparks fly out from its middle, turning, from <paramref name="t0"/> over <paramref name="ms"/>.</summary>
    void Sparks(double t, double t0, double ms, double from, double to, double turnStart)
    {
        double a = Win(t, t0, t0 + ms);
        if (a <= 0 || a >= 1) return;
        for (int i = 0; i < _sparks.Count; i++)
        {
            var s = _sparks[i];
            double ang = (turnStart + i * 360.0 / _sparks.Count) * Math.PI / 180;
            double r = (from + (to - from) * Motion.Out(a)) * (i % 2 == 0 ? 1 : .78);
            s.At.X = r * Math.Cos(ang); s.At.Y = r * Math.Sin(ang) - 2;
            double k = Math.Sin(Math.PI * a) * 1.45;
            s.Size.ScaleX = s.Size.ScaleY = k;
            s.Turn.Angle = 160 * a;
            s.P.Opacity = Math.Min(1, k);
        }
    }

    /// <summary>Confetti for a boss down: out and up, then falling.</summary>
    void Confetti(double t, double t0)
    {
        double a = Win(t, t0, t0 + 1100);
        if (a <= 0 || a >= 1) return;
        for (int i = 0; i < _sparks.Count; i++)
        {
            var s = _sparks[i];
            double ang = (-160 + i * 140.0 / (_sparks.Count - 1)) * Math.PI / 180;
            double speed = 34 + (i * 7) % 12, sec = a * 1.1;
            s.At.X = speed * Math.Cos(ang) * sec; s.At.Y = speed * Math.Sin(ang) * sec + 46 * sec * sec - 10;
            s.Size.ScaleX = s.Size.ScaleY = .9;
            s.Turn.Angle = 400 * a * (i % 2 == 0 ? 1 : -1);
            s.P.Opacity = 1 - Win(a, .7, 1);
        }
    }

    /// <summary>A shockwave ring that bursts out and fades.</summary>
    static void Ring(Ellipse ring, double t, double t0, double ms, double from, double to)
    {
        double a = Win(t, t0, t0 + ms);
        if (a <= 0 || a >= 1) return;
        double d = from + (to - from) * Motion.Out(a);
        ring.Width = ring.Height = d;
        Place(ring, Cx, Cy);
        ring.StrokeThickness = 3.4 * (1 - a) + .6;
        ring.Opacity = (1 - a) * .95;
    }

    /// <summary>The impact: the body flashes <paramref name="c"/> as the moment lands at <paramref name="t0"/>, gone in 220 ms.</summary>
    void Flash(double t, double t0, Color c)
    {
        if (t < t0 || t > t0 + 220) return;
        ((SolidColorBrush)_flash.Fill).Color = c;
        _flash.Opacity = .8 * (1 - Motion.Out((t - t0) / 220));
    }


    /// <summary>A streak's embers: the sparks, small, rising from round its head and fading as they go.</summary>
    void Embers(double t)
    {
        for (int i = 0; i < _sparks.Count; i++)
        {
            var s = _sparks[i];
            double q = (t / 900 + i * 0.137) % 1;
            s.At.X = -20 + (i * 37) % 40 + 4 * Math.Sin(2 * Math.PI * (q + i * .3)); s.At.Y = -14 - 30 * q;
            s.Size.ScaleX = s.Size.ScaleY = .55 * (1 - q);
            s.Turn.Angle = 90 * q;
            s.P.Opacity = Math.Sin(Math.PI * q) * (1 - Win(t, ReactMs - 380, ReactMs)) * Win(t, 0, 200);
        }
    }

    /// <summary>The idle and the reaction added up into the body's transforms, its wings, its glow and its shadow.</summary>
    void Compose()
    {
        double y = _bobY + _reY;
        _move.X = _reX; _move.Y = y;
        _squash.ScaleX = _reSx; _squash.ScaleY = _reSy;
        _lean.Angle = _reTurn + (_mood == Mood.Focused && _playing == null ? -4 : 0);
        double wing = _wingIdle + _reWing;
        _wingL.Angle = -wing; _wingR.Angle = wing;
        _aura.Opacity = Math.Clamp(_auraIdle + Math.Min(0, _reAura), 0, 1);
        // the higher it hops, the smaller and fainter its shadow (the idle's float leaves it be: one thing less to draw)
        double lift = Math.Clamp(-_reY / 18, -0.4, 1);
        double k = 1 - .35 * lift;
        _shadowSize.ScaleX = _shadowSize.ScaleY = k;
        _shadow.Opacity = .9 - .5 * lift;
    }

    /// <summary>The frame that says a reaction best (renders, the stage's still, motion off): ms from its start.</summary>
    public static double KeyMs(CompanionKind k) => k switch
    {
        CompanionKind.Rare => 470, CompanionKind.Best => 720, CompanionKind.Kill => 520, CompanionKind.Streak => 520, CompanionKind.Timer => 380,
        CompanionKind.Pvp => 300, CompanionKind.Wipe => 980, _ => 640,
    };

    /// <summary>For renders: a reaction's frame at <paramref name="ms"/>, or the idle at <paramref name="idle"/> (0 to 1).</summary>
    internal void HoldForRender(CompanionKind? kind, Color accent, double ms, double idle, int variant = 2)
    {
        _variant = variant;
        Idle(idle);
        if (kind is { } k)
        {
            Prepare(k, accent);
            Frame(ms);
        }
    }
}
