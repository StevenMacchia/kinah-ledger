using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using Path = System.Windows.Shapes.Path;
using System.Windows.Threading;
using static KinahLedger.Ui.HudKit;

namespace KinahLedger.Ui;

/// <summary>
/// The companion itself: a tiny Shugo merchant (Steven, 2026-11-21: "maybe a tiny shugo lol", then "how on earth is this a
/// shugo": the game's Shugo is a fluffy white stoat), drawn in vectors, our own design in the style, nothing traced. A chibi
/// white stoat: a round head bigger than its body with fluffy cheeks and a little snout, small round ears lined in pink (a gold
/// hoop in the right one), big glossy black eyes, a pink nose and whiskers; a turban with a jewel at its front, a vest with a
/// gold edge, a sash with a gold buckle, gold bangles on its paws, a bushy tail. Its fur stays white in every look; the look
/// the Buddy uses dresses it (turban, vest, sash, jewel): Classic gold and teal, Modern slate and pearl, Tactical gunmetal with
/// cyan in a reticle, Wacky a sticker with a white edge and a hard shadow, Glitch phosphor green with scan lines and a colour
/// split when it reacts, Atreia, Esports, Elements and Overdrive by their looks.
///
/// It idles (breathes, blinks, twitches an ear, sniffs, glances round, wags its tail), squares up while a fight is on, dozes
/// with its ears drooped after a long quiet, and plays a reaction for a moment (<see cref="React"/>): every course goes through
/// Motion (Ui/Motion.cs), so a render can hold any frame (KL_MOTION_AT, Motion.FrozenMs) and nothing moves with Windows's
/// animations off. Between flourishes nothing is drawn.
/// </summary>
public sealed class CompanionSprite : Grid
{
    public enum Mood { Idle, Focused, Sleepy }
    enum Face { Idle, Focused, Sleepy, Happy, Excited, Proud, Fired, Alert, Fierce, Sad, Dazed }

    /// <summary>The sprite's side at 100%.</summary>
    public const double Side = 96;
    /// <summary>A reaction's course, start to back at rest.</summary>
    public const double ReactMs = 1800;
    /// <summary>A flourish of the idle (an ear twitch, a sniff, a glance), and its frames a second: a slow move needs few.</summary>
    const double IdleMs = 2400;
    const int IdleFps = 10;
    /// <summary>A reaction's, a blink's and a bubble's frames a second: smooth enough for a short moment, light over the game.</summary>
    internal const int ReactFps = 24;
    /// <summary>The glow's and the bursts' middle, and where the stoat stands (its belly; its feet are 35 below).</summary>
    const double Cx = 48, Cy = 50, Ry = 48;
    /// <summary>
    /// The stoat is drawn in its own units (a 120 by 170 box, its middle at 60, its feet at 152) and put on the rig at
    /// <see cref="S"/> of that, its feet <see cref="Feet"/> below the rig's middle.
    /// </summary>
    const double S = .52, Feet = 35.5;
    /// <summary>A point of the drawing on the rig (the effects round its head are placed in rig units).</summary>
    static double RX(double x) => (x - 60) * S;
    static double RY(double y) => Feet + (y - 152) * S;

    /// <summary>
    /// A theme's look: the outline and its width (in rig pixels); the glow behind; the eyes' ink; the turban, the vest, the
    /// sash (each with its deep shade), the jewel on the turban and the gold of its trim, bangles and hoop. The fur stays white.
    /// </summary>
    sealed record Palette(Color Rim, double RimW, Color Glow, Color Ink, Color Turban, Color TurbanDeep, Color Vest, Color VestDeep,
        Color Sash, Color SashDeep, Color Jewel, Color Gold);

    static Palette P(uint rim, double rimW, uint glow, uint ink, uint t, uint td, uint v, uint vd, uint s, uint sd, uint jewel, uint gold = 0xE2B23C) =>
        new(Rgb(rim), rimW, Rgb(glow), Rgb(ink), Rgb(t), Rgb(td), Rgb(v), Rgb(vd), Rgb(s), Rgb(sd), Rgb(jewel), Rgb(gold));

    /// <summary>
    /// The clothes for a theme family, and for a look where its family's looks differ too much for one (Elements: frost
    /// against magma; Atreia: Elyos gold against Asmodian crimson; Overdrive's dragon).
    /// </summary>
    static Palette PaletteOf(string theme, string? look) => (theme, look) switch
    {
        // Modern: a slate vest, a periwinkle turban, pearl sash and silver
        (HudThemes.Modern, _) => P(0x4A5873, 1.3, 0xA9C8FF, 0x1D2433, 0x7FA6FF, 0x3E62C4, 0x3A4560, 0x1D2433, 0xF08CA8, 0xB0506E, 0xFF9DB4, 0xC9D3E3),
        // Tactical: gunmetal with the HUD's cyan, steel for gold
        (HudThemes.Tactical, _) => P(0x071B24, 1.4, 0x3FE0F5, 0x03222C, 0x3A4C56, 0x101E26, 0x2B3B44, 0x0E1A20, 0x3FE0F5, 0x0B8EAF, 0x3FE0F5, 0xA9BDC8),
        // Wacky: candy pink, grape and lime
        (HudThemes.Wacky, _) => P(0x1C0A2B, 2.2, 0xFF52C9, 0x1C0A2B, 0xFF6BD2, 0xC21C8E, 0x7B3CFF, 0x3E1590, 0xA4FF6E, 0x34C82A, 0xFFE14D, 0xFFE14D),
        // Glitch: an old green screen
        (HudThemes.Glitch, _) => P(0x04381B, 1.3, 0x3CFF8A, 0x021A0C, 0x1AA858, 0x04381B, 0x0F3A22, 0x021A0C, 0x3CFF8A, 0x0FA852, 0x9DFFC4, 0x6CFFA8),
        // Atreia: Elyos white and gold, Asmodian crimson and shadow, Rift the Abyss's aether violet, Prism glass splitting the light
        (HudThemes.Atreia, HudThemes.Elyos) => P(0x4A3412, 1.3, 0xFFE29A, 0x2A1C08, 0xF5CF6A, 0xA9801E, 0x4C8FD6, 0x1F4F8A, 0xF5CF6A, 0xA9801E, 0xBFEFFF),
        (HudThemes.Atreia, HudThemes.Asmodian) => P(0x14090F, 1.4, 0xFF3048, 0x14070C, 0xE0263E, 0x8A0E22, 0x3A3440, 0x1A1520, 0x8E8597, 0x4C4356, 0xFF5A6E),
        (HudThemes.Atreia, HudThemes.Rift) => P(0x1C1036, 1.4, 0xB070FF, 0x180C30, 0xA05CFF, 0x5A22C0, 0x2E2450, 0x150E2E, 0xE0B8FF, 0x8A5AC8, 0xF0A8FF),
        (HudThemes.Atreia, _) => P(0x2E1E3E, 1.3, 0xA98BFF, 0x1E1430, 0x8E6CF0, 0x4B33A8, 0x6CC9E6, 0x2E7F9C, 0xC66CE0, 0x7A2F96, 0xF2F0FF),
        // Esports: the finals' black and gold, the hype drink's lime, the caster's yellow pen, and the stream's team cyan and magenta
        (HudThemes.Esports, HudThemes.Finals) => P(0x120E06, 1.4, 0xE8C46A, 0x150E04, 0x24211B, 0x0A0906, 0xE8C46A, 0x9C7A2A, 0x2E2A20, 0x0A0906, 0xFFE6A0),
        (HudThemes.Esports, HudThemes.Hype) => P(0x1A1030, 1.5, 0xB6FF2E, 0x1A0E2E, 0xC6FF3D, 0x6FB800, 0x8A3DFF, 0x4A1AA8, 0xFF7AC0, 0xB03A80, 0xC6FF3D),
        (HudThemes.Esports, HudThemes.Telestrator) => P(0x14140E, 1.4, 0xFFD21E, 0x12120C, 0xFFD21E, 0xC08A00, 0x2E2E2A, 0x12120C, 0xFF4A5A, 0xA81E2A, 0xFFF2A8),
        (HudThemes.Esports, _) => P(0x101828, 1.4, 0x22D3FF, 0x0C1220, 0x22D3FF, 0x0A7FB8, 0x283448, 0x101828, 0xFF2E88, 0xA0104E, 0xA8F0FF),
        // Elements: one per element
        (HudThemes.Elements, HudThemes.Magma) => P(0x1E0804, 1.4, 0xFF6A1A, 0x1A0602, 0xE8502A, 0x8E2410, 0x3A2A24, 0x1C120E, 0xF59A2C, 0x9C5310, 0xFFD34A),
        (HudThemes.Elements, HudThemes.Storm) => P(0x120E2E, 1.4, 0x9C8CFF, 0x120E2A, 0x4A4280, 0x1E1848, 0xAFA8D8, 0x5E5690, 0xFFE45A, 0xC9A400, 0xD6F4FF),
        (HudThemes.Elements, HudThemes.Tide) => P(0x06262E, 1.4, 0x4FE0E0, 0x04202A, 0x1EA6D9, 0x0A5A8A, 0x2A5A64, 0x06262E, 0x4FE0E0, 0x1E8A8A, 0xFFB8C8),
        (HudThemes.Elements, _) => P(0x0E2A44, 1.3, 0x8FDCFF, 0x0A1A2E, 0x6FD0FF, 0x1E7FC0, 0x2E5A84, 0x0E2A44, 0x3FB4F0, 0x1A6EA8, 0xBFE6FF, 0xD8ECF8),
        // Overdrive: wild; the dragon's blood red, Manga inked black and red, Aura a powered-up gold, else a power-up's hot orange
        (HudThemes.Overdrive, HudThemes.Dragon) => P(0x14040A, 1.5, 0xFF2A3A, 0x16040A, 0xB3122E, 0x5E0716, 0x22191C, 0x0B0607, 0xD4A642, 0x7E5C17, 0xFF3B3B),
        (HudThemes.Overdrive, HudThemes.Manga) => P(0x0A0A0E, 1.9, 0xFFFFFF, 0x0A0A0E, 0x1A1A20, 0x000000, 0xE8203A, 0x8A0A1A, 0x2A2A30, 0x0A0A0E, 0xE8203A, 0xE6E6EE),
        (HudThemes.Overdrive, HudThemes.Aura) => P(0x10183A, 1.5, 0x4FB4FF, 0x0E1430, 0xF5CF4A, 0xB0820E, 0x2E8BFF, 0x0A4AB8, 0xF5CF4A, 0xB0820E, 0x8FD8FF),
        (HudThemes.Overdrive, _) => P(0x0E1430, 1.5, 0xFF8A2A, 0x0E1020, 0xFF7A1A, 0xB84A00, 0x2A3A5A, 0x0E1430, 0x5AB8FF, 0x1E6AB0, 0xFFD34A),
        // Classic: the Kinah panel's gold turban and vest, a teal sash, a blue jewel
        _ => P(0x3A210C, 1.4, 0xF5B942, 0x141414, 0xE7B43A, 0x9C6B12, 0xD9A12C, 0x8A5A10, 0x2E8F7C, 0x1A5E51, 0x3FA7E0),
    };

    /// <summary>The fur (white in every look, lit from the top left with a warm cream shade), the pink of its ears, nose and cheeks.</summary>
    static readonly Color FurLight = Rgb(0xFFFFFF), Fur = Rgb(0xFBF9F4), FurShade = Rgb(0xE6DBCB), EarIn = Rgb(0xF3B9C4), NoseC = Rgb(0xE7909F), NoseEdge = Rgb(0xB9626F),
        Cheek = Rgb(0xF3A6B6), MouthC = Rgb(0x5E3E3C), GoldDeep = Rgb(0x8A5A0E);

    /// <summary>The theme families the companion knows (an unknown one is drawn as Classic).</summary>
    static readonly HashSet<string> Families = HudThemes.All.Select(t => t.Key).ToHashSet();

    /// <summary>The theme family it is drawn in.</summary>
    public string Theme { get; }
    /// <summary>The Buddy's look it is tinted for, where its family has more than one palette (null: the family's).</summary>
    public string? Look { get; }
    readonly Palette _p;
    /// <summary>The fur's outline: the theme's outline softened toward the fur, so the white reads as fluffy, not inked.</summary>
    readonly Color _furLine;
    readonly Canvas _stage = new() { Width = Side, Height = Side, ClipToBounds = false };
    readonly Canvas _rig = new();
    /// <summary>The stoat in its own units (<see cref="S"/>).</summary>
    readonly Canvas _art = new() { RenderTransform = new MatrixTransform(S, 0, 0, S, -60 * S, Feet - 152 * S) };
    readonly ScaleTransform _squash = new(1, 1, 0, Feet + 3);
    readonly RotateTransform _lean = new(0, 0, 22);
    readonly TranslateTransform _move = new();
    /// <summary>The ears' turn about their roots: up is perked, down is drooped out to the side (left mirrored).</summary>
    readonly RotateTransform _earL = new(0, 35, 54), _earR = new(0, 85, 54);
    /// <summary>The ears' size: a touch bigger as they perk.</summary>
    readonly ScaleTransform _earLenL = new(1, 1, 35, 54), _earLenR = new(1, 1, 85, 54);
    /// <summary>The arms' turn about the shoulders: 0 the paws at its chest, 90 straight out, 180 straight up (left mirrored).</summary>
    readonly RotateTransform _armL = new(), _armR = new();
    readonly ScaleTransform _armLen = new(1, 1), _armLenR = new(1, 1);
    readonly TranslateTransform _armOutL = new(), _armOutR = new();
    /// <summary>The turban: knocked askew when it falls.</summary>
    readonly TranslateTransform _hat = new();
    readonly RotateTransform _hatTurn = new(0, 60, 40);
    /// <summary>The turban's jewel flaring (a streak, a takedown, a fight).</summary>
    readonly ScaleTransform _jewelSize = new(1, 1, 60, 38);
    readonly UIElement _jewelGlow, _jewelStar;
    readonly ScaleTransform _nose = new(1, 1, 60, 71);
    /// <summary>Its bushy tail's wag about its root.</summary>
    readonly RotateTransform _tail = new(0, 84, 136);
    readonly Ellipse _aura, _burst, _shadow, _ring, _ring2;
    /// <summary>The white flash over the body as a moment lands (its impact).</summary>
    readonly Path _flash;
    readonly Path _tint, _ghostR, _ghostC;
    readonly Canvas _eyes = new();
    readonly ScaleTransform _eyeScale = new(1, 1, 0, 0);
    /// <summary>Where its eyes look (a glance during an idle flourish).</summary>
    readonly TranslateTransform _eyeLook = new();
    /// <summary>The flourish under way: 0 a glance round, 1 a smile with a wiggle of the ears, 2 a sniff and an ear twitch (they take turns).</summary>
    int _variant;
    readonly List<UIElement> _openEyes = new();
    readonly Path _happyEyes, _closedEyes, _xEyes, _browsFierce, _browsSad, _browsFocus;
    readonly Ellipse _cheekL, _cheekR;
    readonly Path _mouth, _mouthO, _tongue;
    readonly Path _crown, _alert, _cloud, _tear;
    readonly TranslateTransform _crownAt = new(0, -33);
    readonly Canvas _coin = new();
    readonly ScaleTransform _coinSpin = new(1, 1);
    readonly TranslateTransform _coinAt = new();
    readonly Canvas _rays = new(), _flames = new(), _rain = new(), _dizzy = new(), _zs = new(), _slashes = new();
    readonly RotateTransform _raysTurn = new();
    readonly List<(Path P, TranslateTransform At, ScaleTransform Size, RotateTransform Turn)> _sparks = new();
    readonly Canvas _reticle = new();
    readonly ScaleTransform _shadowSize = new(1, 1, 18, 3.5);
    readonly SolidColorBrush _sparkInk = new(), _ringInk = new(), _burstInk = new();
    readonly RadialGradientBrush _burstFill;

    Mood _mood = Mood.Idle;
    Face _face = Face.Idle;
    CompanionKind? _playing;
    Color _accent;
    // the parts the idle and a reaction each put in, added up into the transforms (Compose)
    double _bobY, _earIdle, _earTwL, _earTwR, _armIdle, _auraIdle = .5, _jewelIdle, _tailIdle;
    double _reX, _reY, _reSx = 1, _reSy = 1, _reTurn, _reEar, _reEarL, _reEarR, _reArmL, _reArmR, _reAura, _reHat, _reHatTurn, _reJewel, _reTail;
    readonly DispatcherTimer _blink, _back, _flourish;
    int _flourishes;
    bool _running;
    int _blinks;

    /// <summary>A reaction ended and the companion is at rest again.</summary>
    public event Action? Rested;

    public CompanionSprite(string theme, string? look = null)
    {
        Theme = theme != null && Families.Contains(theme) ? theme : HudThemes.Classic;
        Look = look;
        _p = PaletteOf(Theme, look);
        _furLine = Mix(_p.Rim, Rgb(0xA0968A), Theme == HudThemes.Wacky ? .3 : .42);
        Width = Side; Height = Side;
        ClipToBounds = false;
        IsHitTestVisible = true;
        Background = Brushes.Transparent;   // the whole square is the companion's to grab (a drag; the grip with click-through on)
        Tag = "companion:" + Theme;
        Children.Add(_stage);

        // the glow behind: the theme's, and a burst in a moment's own colour over it
        _aura = Disc(86, 86, Glowing(_p.Glow, .5));
        Place(_aura, Cx, Cy);
        _burstFill = Glowing(Colors.White, .9);
        _burst = Disc(96, 96, _burstFill); _burst.Opacity = 0;
        Place(_burst, Cx, Cy);
        _stage.Children.Add(_aura);
        _stage.Children.Add(_burst);

        // a new best's rays, turning slowly behind it
        for (int i = 0; i < 12; i++)
        {
            var ray = new Path { Data = Geometry.Parse("M -2.2,-14 L 2.2,-14 L 0.6,-44 L -0.6,-44 Z"), Fill = Frozen(Color.FromArgb(0x70, 0xFF, 0xD7, 0x6A)), RenderTransform = new RotateTransform(i * 30) };
            _rays.Children.Add(ray);
        }
        _rays.RenderTransform = _raysTurn;
        Canvas.SetLeft(_rays, Cx); Canvas.SetTop(_rays, Cy);
        _rays.Opacity = 0;
        _stage.Children.Add(_rays);

        // Tactical: a reticle's four corners round it
        if (Theme == HudThemes.Tactical)
        {
            var rink = Frozen(Color.FromArgb(0xB0, _p.Sash.R, _p.Sash.G, _p.Sash.B));
            foreach (var (sx, sy) in new[] { (-1, -1), (1, -1), (-1, 1), (1, 1) })
                _reticle.Children.Add(new Path { Data = Geometry.Parse(Fmt("M {0},{1} L {0},{2} M {0},{1} L {3},{1}", sx * 44, sy * 44, sy * 35, sx * 35)), Stroke = rink, StrokeThickness = 1.4 });
            _reticle.Children.Add(new Ellipse { Width = 3, Height = 3, Fill = rink, RenderTransform = new TranslateTransform(-1.5, -47) });
            Canvas.SetLeft(_reticle, Cx); Canvas.SetTop(_reticle, Cy);
            _reticle.Opacity = .65;
            _stage.Children.Add(_reticle);
        }

        // the shadow on the ground
        _shadow = Disc(36, 7, new RadialGradientBrush(Color.FromArgb(0x88, 0, 0, 0), Color.FromArgb(0, 0, 0, 0)));
        Place(_shadow, Cx, Ry + Feet + 3);
        _shadow.RenderTransform = _shadowSize;
        _stage.Children.Add(_shadow);

        // the stoat and all that moves with it
        Canvas.SetLeft(_rig, Cx); Canvas.SetTop(_rig, Ry);
        _rig.RenderTransform = new TransformGroup { Children = { _squash, _lean, _move } };
        _stage.Children.Add(_rig);

        // flames round its head (a streak): behind it all
        var flameFill = new LinearGradientBrush { StartPoint = new Point(0.5, 1), EndPoint = new Point(0.5, 0) };
        flameFill.GradientStops.Add(new GradientStop(Color.FromArgb(0xFF, 0xFF, 0x4A, 0x0E), 0));
        flameFill.GradientStops.Add(new GradientStop(Color.FromArgb(0xF0, 0xFF, 0x9A, 0x1E), 0.45));
        flameFill.GradientStops.Add(new GradientStop(Color.FromArgb(0xC0, 0xFF, 0xE8, 0x8A), 0.8));
        flameFill.GradientStops.Add(new GradientStop(Color.FromArgb(0x00, 0xFF, 0xF6, 0xC8), 1));
        flameFill.Freeze();
        for (int i = 0; i < 9; i++)
        {
            double ang = -84 + i * 21;   // a crown of flames round the head
            var fl = new Path
            {
                Data = Geometry.Parse(i % 2 == 0 ? "M 0,0 C -7,-6 -5,-14 0,-24 C 1,-17 5,-14 4,-8 C 6,-5 5,-2 0,0 Z" : "M 0,0 C -5,-5 -3,-11 0,-18 C 3,-11 5,-5 0,0 Z"),
                Fill = flameFill,
                RenderTransform = new TransformGroup { Children = { new ScaleTransform(.58, .58), new TranslateTransform(0, -18), new RotateTransform(ang) } },
            };
            _flames.Children.Add(fl);
        }
        Canvas.SetTop(_flames, -11);
        _flames.Opacity = 0;
        _rig.Children.Add(_flames);

        // the stoat, in its own units
        _rig.Children.Add(_art);
        double rw = _p.RimW / S;   // the outline's width in its units
        var furLine = Frozen(_furLine);

        // the silhouette (head, turban, body, feet): the sticker's edge, the colour split, the flush and the flash
        var head = Geometry.Parse(HeadPath);
        var body = Geometry.Parse(BodyPath);
        var feet = new GeometryGroup { Children = { new EllipseGeometry(new Point(46, 152), 11, 6), new EllipseGeometry(new Point(74, 152), 11, 6) } };
        var shape = new GeometryGroup { FillRule = FillRule.Nonzero, Children = { head, Geometry.Parse(TurbanPath), body, feet } };
        shape.Freeze();
        var earL = new TransformGroup { Children = { _earLenL, _earL } };
        var earR = new TransformGroup { Children = { _earLenR, _earR } };
        if (Theme == HudThemes.Wacky)
        {
            // a sticker: a hard shadow, then a thick white edge round it all (the ears and tail with it), drawn under every fill
            var hard = Frozen(Color.FromArgb(0x9A, 0x12, 0x04, 0x20));
            _art.Children.Add(new Path { Data = shape, Fill = hard, RenderTransform = new TranslateTransform(4.8, 5.9) });
            foreach (var (t, x) in new[] { (earL, 23.0), (earR, 97.0) })
                _art.Children.Add(new Path { Data = new EllipseGeometry(new Point(x, 46), 11, 11), Stroke = Brushes.White, StrokeThickness = 9, RenderTransform = t });
            _art.Children.Add(new Path { Data = Geometry.Parse(TailPath), Stroke = Brushes.White, StrokeThickness = 9, StrokeLineJoin = PenLineJoin.Round, RenderTransform = _tail });
            _art.Children.Add(new Path { Data = shape, Stroke = Brushes.White, StrokeThickness = 9, StrokeLineJoin = PenLineJoin.Round });
        }
        if (Theme == HudThemes.Glitch)
        {
            // the colour split: a red and a cyan copy behind, apart only while it reacts
            _ghostR = new Path { Data = shape, Fill = Frozen(Color.FromArgb(0x90, 0xFF, 0x2A, 0x5A)), RenderTransform = new TranslateTransform(0, 0) };
            _ghostC = new Path { Data = shape, Fill = Frozen(Color.FromArgb(0x90, 0x2A, 0xE8, 0xFF)), RenderTransform = new TranslateTransform(0, 0) };
            _art.Children.Add(_ghostR); _art.Children.Add(_ghostC);
        }
        else { _ghostR = new Path(); _ghostC = new Path(); }

        var fur = FurFill();
        // its bushy tail, out on the right behind it all
        _art.Children.Add(new Path { Data = Geometry.Parse(TailPath), Fill = fur, Stroke = furLine, StrokeThickness = rw, StrokeLineJoin = PenLineJoin.Round, RenderTransform = _tail });
        _art.Children.Add(new Path { Data = Geometry.Parse("M 101,104 C 105,113 102,124 92,131"), Stroke = Frozen(Color.FromArgb(0x90, FurShade.R, FurShade.G, FurShade.B)), StrokeThickness = 2.4, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round, RenderTransform = _tail });

        // the ears, behind the head: small and round, lined in pink, a gold hoop in the right one
        _art.Children.Add(Ear(earL, 23));
        var ear2 = Ear(earR, 97);
        var gold = Frozen(_p.Gold);
        ear2.Children.Add(new Ellipse { Width = 8.4, Height = 8.4, Stroke = gold, StrokeThickness = 2.8, RenderTransform = new TranslateTransform(105 - 4.2, 53.5 - 4.2) });
        ear2.Children.Add(new Ellipse { Width = 6, Height = 6, Stroke = gold, StrokeThickness = 2.4, RenderTransform = new TranslateTransform(102.5 - 3, 60.5 - 3) });
        _art.Children.Add(ear2);

        // the feet, then the body, its vest, its sash with a buckle and a tail of it hanging
        foreach (double fx in new[] { 46.0, 74.0 })
        {
            var foot = new Ellipse { Width = 22, Height = 12, Fill = fur, Stroke = furLine, StrokeThickness = rw };
            Place(foot, fx, 152);
            _art.Children.Add(foot);
            _art.Children.Add(new Path { Data = Geometry.Parse(Fmt("M {0},150 L {0},155 M {1},150 L {1},155", fx - 3, fx + 3)), Stroke = Frozen(Color.FromArgb(0xA0, FurShade.R, FurShade.G, FurShade.B)), StrokeThickness = 1.6, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round });
        }
        _art.Children.Add(new Path { Data = body, Fill = fur, Stroke = furLine, StrokeThickness = rw, StrokeLineJoin = PenLineJoin.Round });
        var vest = new LinearGradientBrush(Lighten(_p.Vest, .18), _p.Vest, 90); vest.Freeze();
        var vestEdge = Frozen(_p.VestDeep);
        _art.Children.Add(new Path { Data = Geometry.Parse("M 34,104 C 30,126 33,146 50,149 L 52,100 C 44,98 37,100 34,104 Z"), Fill = vest, Stroke = vestEdge, StrokeThickness = rw * .85, StrokeLineJoin = PenLineJoin.Round });
        _art.Children.Add(new Path { Data = Geometry.Parse("M 86,104 C 90,126 87,146 70,149 L 68,100 C 76,98 83,100 86,104 Z"), Fill = vest, Stroke = vestEdge, StrokeThickness = rw * .85, StrokeLineJoin = PenLineJoin.Round });
        _art.Children.Add(new Path { Data = Geometry.Parse("M 50.5,101 L 52,148 M 69.5,101 L 68,148"), Stroke = gold, StrokeThickness = 2.6, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round });
        var sash = new LinearGradientBrush(Lighten(_p.Sash, .22), _p.Sash, 90); sash.Freeze();
        var sashEdge = Frozen(_p.SashDeep);
        _art.Children.Add(new Path { Data = Geometry.Parse("M 76,133 C 80,139 84,144 86,150 L 80,148 L 77,152 C 76,146 74,140 71,135 Z"), Fill = sash, Stroke = sashEdge, StrokeThickness = rw * .8, StrokeLineJoin = PenLineJoin.Round });
        _art.Children.Add(new Path { Data = Geometry.Parse("M 33,124 C 48,130 72,130 87,124 L 87,133 C 72,139 48,139 33,133 Z"), Fill = sash, Stroke = sashEdge, StrokeThickness = rw * .8, StrokeLineJoin = PenLineJoin.Round });
        var buckleFill = new RadialGradientBrush(Rgb(0xFFE89A), Rgb(0xE2A82C)) { GradientOrigin = new Point(.35, .3) }; buckleFill.Freeze();
        var buckle = new Rectangle { Width = 11, Height = 10, RadiusX = 2.6, RadiusY = 2.6, Fill = buckleFill, Stroke = Frozen(GoldDeep), StrokeThickness = 1.6 };
        Place(buckle, 60, 130.5);
        _art.Children.Add(buckle);
        var tongueB = new Rectangle { Width = 4.4, Height = 4, Fill = Frozen(_p.SashDeep) };
        Place(tongueB, 60, 130.5);
        _art.Children.Add(tongueB);

        // the head: white fur lit from the top left, fluffy cheeks, a ruff of fur under its chin over the vest
        _art.Children.Add(new Path { Data = head, Fill = fur, Stroke = furLine, StrokeThickness = rw, StrokeLineJoin = PenLineJoin.Round });
        _art.Children.Add(new Path { Data = Geometry.Parse("M 44,89 Q 60,95 76,89 L 75,94 L 70,101 L 66,96 L 63,104 L 60,97 L 57,104 L 54,96 L 50,101 L 45,94 Z"), Fill = Frozen(Fur) });
        _art.Children.Add(new Path { Data = Geometry.Parse("M 44.5,93 L 50,101 L 54,96 L 57,104 L 60,97 L 63,104 L 66,96 L 70,101 L 75.5,93"), Stroke = furLine, StrokeThickness = rw * .8, StrokeLineJoin = PenLineJoin.Round, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round });
        // the snout: a brighter muzzle, its soft edge round the nose and mouth (a stoat's, not a cat's whiskers)
        var muzzle = Disc(34, 23, Soft(FurLight, 1));
        Place(muzzle, 60, 77);
        _art.Children.Add(muzzle);
        _art.Children.Add(new Path { Data = Geometry.Parse("M 48.5,70.5 C 45.5,78 49,87.5 60,88.5 C 71,87.5 74.5,78 71.5,70.5"), Stroke = Frozen(Color.FromArgb(0xC8, 0xD8, 0xCB, 0xB8)), StrokeThickness = 2, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round });
        if (Theme == HudThemes.Glitch)
        {
            var lines = new DrawingBrush(new GeometryDrawing(Frozen(Color.FromArgb(0x24, 0x00, 0x30, 0x14)), null, new RectangleGeometry(new Rect(0, 0, 7.7, 1.9))))
            { TileMode = TileMode.Tile, Viewport = new Rect(0, 0, 7.7, 5), ViewportUnits = BrushMappingMode.Absolute, Viewbox = new Rect(0, 0, 7.7, 5), ViewboxUnits = BrushMappingMode.Absolute };
            lines.Freeze();
            _art.Children.Add(new Path { Data = shape, Fill = lines, IsHitTestVisible = false });
        }

        // the face: big glossy black eyes, the cheeks, the pink nose, the little mouth under it
        var ink = Frozen(_p.Ink);
        Canvas.SetLeft(_eyes, 60); Canvas.SetTop(_eyes, 62);
        _eyes.RenderTransform = new TransformGroup { Children = { _eyeScale, _eyeLook } };
        foreach (double ex in new[] { -15.0, 15.0 })
        {
            var eye = new Ellipse { Width = 13.4, Height = 15.4, Fill = ink };
            Place(eye, ex, 0);
            var light = Disc(5, 5, Brushes.White);
            Place(light, ex + 2.2, -3.2);
            var spark = Disc(2.2, 2.2, Frozen(Color.FromArgb(0xC0, 0xFF, 0xFF, 0xFF)));
            Place(spark, ex - 2, 3.4);
            _eyes.Children.Add(eye); _eyes.Children.Add(light); _eyes.Children.Add(spark);
            _openEyes.Add(eye); _openEyes.Add(light); _openEyes.Add(spark);
        }
        _art.Children.Add(_eyes);
        Path Stroked(string data, double w) => new() { Data = Geometry.Parse(data), Stroke = ink, StrokeThickness = w, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round, StrokeLineJoin = PenLineJoin.Round, Visibility = Visibility.Collapsed };
        _happyEyes = Stroked("M 37,65 Q 45,53 53,65 M 67,65 Q 75,53 83,65", 4);
        _closedEyes = Stroked("M 37,61 Q 45,68 53,61 M 67,61 Q 75,68 83,61", 3.6);
        _xEyes = Stroked("M 40,57 L 50,67 M 50,57 L 40,67 M 70,57 L 80,67 M 80,57 L 70,67", 3.8);
        _browsFierce = Stroked("M 36,48 L 52,54.5 M 84,48 L 68,54.5", 4.2);
        _browsSad = Stroked("M 37,53 L 51,47.5 M 83,53 L 69,47.5", 3.4);
        _browsFocus = Stroked("M 37,50 L 52,52.5 M 83,50 L 68,52.5", 3.4);
        foreach (var e in new[] { _happyEyes, _closedEyes, _xEyes, _browsFierce, _browsSad, _browsFocus }) _art.Children.Add(e);
        var cheek = Frozen(Color.FromArgb(0xA0, Cheek.R, Cheek.G, Cheek.B));
        _cheekL = Disc(11, 6.4, cheek); Place(_cheekL, 37.5, 74);
        _cheekR = Disc(11, 6.4, cheek); Place(_cheekR, 82.5, 74);
        _art.Children.Add(_cheekL); _art.Children.Add(_cheekR);
        var mouthInk = Frozen(MouthC);
        _mouth = new Path { Stroke = mouthInk, StrokeThickness = 2.4, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round, StrokeLineJoin = PenLineJoin.Round };
        _mouthO = new Path { Data = new EllipseGeometry(new Point(60, 82.5), 3.4, 4.2), Fill = mouthInk };
        _tongue = new Path { Data = Geometry.Parse("M 55.5,85 Q 60,81.5 64.5,85 Q 60,89.5 55.5,85 Z"), Fill = Frozen(Rgb(0xFF7A90)), Visibility = Visibility.Collapsed };
        _art.Children.Add(_mouth); _art.Children.Add(_tongue); _art.Children.Add(_mouthO);
        _art.Children.Add(Nose());

        // the turban, its jewel at the front (it flares in a fight)
        var hatGroup = Turban(out _jewelGlow, out _jewelStar);
        _art.Children.Add(hatGroup);

        // its paws, in front: they wave, cheer, punch and hang
        _art.Children.Add(Arm(-1, new TransformGroup { Children = { _armLen, _armL, _armOutL } }));
        _art.Children.Add(Arm(1, new TransformGroup { Children = { _armLenR, _armR, _armOutR } }));

        // a warm flush (a streak) or a grey cast (a wipe, a fall) over it, and the flash
        _tint = new Path { Data = shape, Fill = new SolidColorBrush(Colors.Transparent), Opacity = 0 };
        _art.Children.Add(_tint);
        _flash = new Path { Data = shape, Fill = new SolidColorBrush(Colors.White), Opacity = 0, IsHitTestVisible = false };
        _art.Children.Add(_flash);

        // the Kinah coin it flips on a rare drop
        _coin.RenderTransform = new TransformGroup { Children = { _coinSpin, _coinAt } };
        var coinFill = new RadialGradientBrush { GradientOrigin = new Point(.35, .3), Center = new Point(.45, .4), RadiusX = .7, RadiusY = .7 };
        coinFill.GradientStops.Add(new GradientStop(Rgb(0xFFF6C8), 0));
        coinFill.GradientStops.Add(new GradientStop(Rgb(0xFFC93C), .45));
        coinFill.GradientStops.Add(new GradientStop(Rgb(0xC07812), 1));
        coinFill.Freeze();
        var coinEdge = Frozen(Rgb(0x6B3E06));
        var halo = Disc(21, 21, Glowing(Rgb(0xFFD25A), .7));   // it shines
        Place(halo, 0, 0);
        _coin.Children.Add(halo);
        var c0 = new Ellipse { Width = 14, Height = 14, Fill = coinFill, Stroke = coinEdge, StrokeThickness = 1.3 };
        Place(c0, 0, 0);
        var c1 = new Ellipse { Width = 9.2, Height = 9.2, Stroke = Frozen(Color.FromArgb(0xB0, 0x8A, 0x52, 0x08)), StrokeThickness = 1 };
        Place(c1, 0, 0);
        var mark = new Path { Data = Geometry.Parse("M 0,-3.4 L 2.4,0 L 0,3.4 L -2.4,0 Z"), Fill = Frozen(Rgb(0xFFF3B0)), Stroke = Frozen(Color.FromArgb(0xC0, 0x8A, 0x52, 0x08)), StrokeThickness = .7 };
        var glint = Disc(3.2, 2, Brushes.White); Place(glint, -3, -3.6); glint.Opacity = .85;
        _coin.Children.Add(c0); _coin.Children.Add(c1); _coin.Children.Add(mark); _coin.Children.Add(glint);
        _coin.Opacity = 0;
        _rig.Children.Add(_coin);

        // what a moment adds round its head: a crown on the turban, an alert mark, a rain cloud and its rain, a tear, dizzy stars, z's
        var crownFill = new LinearGradientBrush(Rgb(0xFFF1A8), Rgb(0xE59A12), 90); crownFill.Freeze();
        _crown = new Path
        {
            Data = Geometry.Parse("M -10,4 L -11.5,-6 L -5.5,-1.2 L 0,-9.5 L 5.5,-1.2 L 11.5,-6 L 10,4 Z M -11.5,-6 m -1.6,0 a 1.6,1.6 0 1 0 3.2,0 a 1.6,1.6 0 1 0 -3.2,0 M 0,-9.5 m -1.8,0 a 1.8,1.8 0 1 0 3.6,0 a 1.8,1.8 0 1 0 -3.6,0 M 11.5,-6 m -1.6,0 a 1.6,1.6 0 1 0 3.2,0 a 1.6,1.6 0 1 0 -3.2,0"),
            Fill = crownFill, Stroke = Frozen(Rgb(0x7A4A04)), StrokeThickness = 1.1, StrokeLineJoin = PenLineJoin.Round, Opacity = 0,
            RenderTransform = new TransformGroup { Children = { new ScaleTransform(.8, .8), _crownAt } },
        };
        _rig.Children.Add(_crown);
        _alert = new Path
        {
            Data = Geometry.Parse("M -2.6,-12 Q 0,-14 2.6,-12 L 1.6,1 Q 0,2.4 -1.6,1 Z M 0,4.2 m -2.4,0 a 2.4,2.4 0 1 0 4.8,0 a 2.4,2.4 0 1 0 -4.8,0"),
            Fill = Frozen(Rgb(0xFFC24A)), Stroke = Frozen(Rgb(0x5A3200)), StrokeThickness = 1.2, Opacity = 0, RenderTransform = new TransformGroup { Children = { new ScaleTransform(1, 1), new RotateTransform(14), new TranslateTransform(27, -24) } },
        };
        _rig.Children.Add(_alert);
        var cloudGeo = new GeometryGroup { FillRule = FillRule.Nonzero, Children = { new EllipseGeometry(new Point(-6, 0), 6.5, 5.5), new EllipseGeometry(new Point(2, -3), 8, 7), new EllipseGeometry(new Point(9, 1), 5.5, 4.6), new RectangleGeometry(new Rect(-10, 0, 23, 5.2), 2.6, 2.6) } };
        _cloud = new Path { Data = cloudGeo, Fill = new LinearGradientBrush(Rgb(0xC9D2E2), Rgb(0x7F8AA0), 90), Stroke = Frozen(Rgb(0x3E4658)), StrokeThickness = 1, Opacity = 0, RenderTransform = new TransformGroup { Children = { new ScaleTransform(.8, .8), new TranslateTransform(0, -40) } } };
        _rig.Children.Add(_cloud);
        for (int i = 0; i < 4; i++)
            _rain.Children.Add(new Line { X1 = 0, Y1 = 0, X2 = -1.2, Y2 = 4.5, Stroke = Frozen(Rgb(0x8FC2FF)), StrokeThickness = 1.6, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round, RenderTransform = new TranslateTransform(-6 + i * 4, -34) });
        _rain.Opacity = 0;
        _rig.Children.Add(_rain);
        _tear = new Path { Data = Geometry.Parse("M 0,-3.2 C 1.8,-0.6 2.6,0.8 2.6,1.9 A 2.6,2.6 0 1 1 -2.6,1.9 C -2.6,0.8 -1.8,-0.6 0,-3.2 Z"), Fill = Frozen(Rgb(0x9FD4FF)), Stroke = Frozen(Rgb(0x2F6FB0)), StrokeThickness = .8, Opacity = 0, RenderTransform = new TranslateTransform(RX(80), RY(70)) };
        _rig.Children.Add(_tear);
        for (int i = 0; i < 3; i++)
            _dizzy.Children.Add(new Path { Data = Geometry.Parse(StarPath(3.6)), Fill = Frozen(Rgb(0xFFE07A)), Stroke = Frozen(Rgb(0x7A5200)), StrokeThickness = .6, RenderTransform = new TranslateTransform() });
        _dizzy.Opacity = 0;
        _rig.Children.Add(_dizzy);
        for (int i = 0; i < 2; i++)
            _zs.Children.Add(new Path { Data = Geometry.Parse(i == 0 ? "M 0,0 L 5,0 L 0,5 L 5,5" : "M 0,0 L 3.6,0 L 0,3.6 L 3.6,3.6"), Stroke = Frozen(Color.FromArgb(0xE8, Lighten(_p.Glow, .6).R, Lighten(_p.Glow, .6).G, Lighten(_p.Glow, .6).B)), StrokeThickness = 1.6, StrokeLineJoin = PenLineJoin.Round, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round, RenderTransform = new TranslateTransform() });
        _zs.Opacity = 0;
        _rig.Children.Add(_zs);

        // over everything: a ring, sparks, the blades of a takedown
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
        // a takedown: two crossed blades beside its head, popping in on a red burst
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
        _slashes.RenderTransform = new TransformGroup { Children = { new ScaleTransform(.8, .8), new RotateTransform(0), new TranslateTransform(-31, -29) } };
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

    /* ---------------- shapes (in the stoat's own units) ---------------- */

    /// <summary>The head: round, a little wider at the cheeks, bigger than the body (chibi), with tufts of fur at the cheeks.</summary>
    const string HeadPath = "M 26,64 C 24,40 40,30 60,30 C 80,30 96,40 94,64 C 94,72 93,77 91,81 L 97,83.5 L 90.5,86 L 94.5,90 L 85,90.5 C 78,95 70,96 60,96 C 50,96 42,95 35,90.5 L 25.5,90 L 29.5,86 L 23,83.5 L 29,81 C 27,77 26,72 26,64 Z";
    /// <summary>The body: a small bean under the head.</summary>
    const string BodyPath = "M 34,104 C 30,128 34,148 60,150 C 86,148 90,128 86,104 C 80,92 40,92 34,104 Z";
    /// <summary>The turban: wound round the top of its head, its folds swept to the front.</summary>
    const string TurbanPath = "M 26.5,49 C 20,11 100,11 93.5,49 C 84,41 72,38 60,38 C 48,38 36,41 26.5,49 Z";
    /// <summary>The tail: short and bushy, curling up on the right.</summary>
    const string TailPath = "M 82,143 C 103,144 114,130 112,112 L 116.5,108 L 111,106 C 110,101 107,97 103,96 L 103.5,91 L 99,95 C 95.5,96 95.5,100.5 98,104 C 103,113 99,126 82,130 Z";

    static string Fmt(string f, params double[] v) => string.Format(System.Globalization.CultureInfo.InvariantCulture, f, v.Cast<object>().ToArray());

    /// <summary>A four-pointed star of radius <paramref name="r"/>, its waist pinched.</summary>
    static string StarPath(double r) { double k = r * 0.18; return Fmt("M 0,{0} Q {1},{2} {3},0 Q {1},{1} 0,{3} Q {2},{1} {0},0 Q {2},{2} 0,{0} Z", -r, k, -k, r); }

    /// <summary>The fur: white, lit from the top left, a warm cream at its edges.</summary>
    static RadialGradientBrush FurFill()
    {
        var f = new RadialGradientBrush { GradientOrigin = new Point(0.36, 0.26), Center = new Point(0.45, 0.4), RadiusX = 0.72, RadiusY = 0.72 };
        f.GradientStops.Add(new GradientStop(FurLight, 0));
        f.GradientStops.Add(new GradientStop(Fur, 0.5));
        f.GradientStops.Add(new GradientStop(FurShade, 1));
        f.Freeze();
        return f;
    }

    /// <summary>A soft-edged patch (the muzzle).</summary>
    static RadialGradientBrush Soft(Color c, double a)
    {
        var g = new RadialGradientBrush();
        g.GradientStops.Add(new GradientStop(Color.FromArgb((byte)(255 * a), c.R, c.G, c.B), 0));
        g.GradientStops.Add(new GradientStop(Color.FromArgb((byte)(255 * a * .85), c.R, c.G, c.B), 0.62));
        g.GradientStops.Add(new GradientStop(Color.FromArgb(0, c.R, c.G, c.B), 1));
        g.Freeze();
        return g;
    }

    /// <summary>A round ear at <paramref name="x"/>, lined in pink, turned by <paramref name="at"/> about its root at the head's side.</summary>
    Canvas Ear(Transform at, double x)
    {
        var ear = new Canvas { RenderTransform = at };
        var outer = new Ellipse { Width = 22, Height = 22, Fill = FurFill(), Stroke = Frozen(_furLine), StrokeThickness = _p.RimW / S };
        Place(outer, x, 46);
        var inFill = new RadialGradientBrush(Lighten(EarIn, .3), EarIn) { GradientOrigin = new Point(.5, .3) }; inFill.Freeze();
        var inner = Disc(13, 13, inFill);
        Place(inner, x, 47.5);
        ear.Children.Add(outer); ear.Children.Add(inner);
        return ear;
    }

    /// <summary>Its little pink nose, glossy, its sniff a little swell.</summary>
    Canvas Nose()
    {
        var c = new Canvas { RenderTransform = _nose };
        var fill = new RadialGradientBrush { GradientOrigin = new Point(0.35, 0.25), Center = new Point(0.45, 0.4), RadiusX = .65, RadiusY = .65 };
        fill.GradientStops.Add(new GradientStop(Lighten(NoseC, .45), 0));
        fill.GradientStops.Add(new GradientStop(NoseC, .55));
        fill.GradientStops.Add(new GradientStop(Darken(NoseC, .8), 1));
        fill.Freeze();
        c.Children.Add(new Path { Data = Geometry.Parse("M 54.5,70 C 54.5,66.5 65.5,66.5 65.5,70 C 65.5,73.5 61.2,75.6 60,75.6 C 58.8,75.6 54.5,73.5 54.5,70 Z"), Fill = fill, Stroke = Frozen(NoseEdge), StrokeThickness = 1.2 });
        var hi = Disc(4.4, 2.2, Frozen(Color.FromArgb(0xE0, 0xFF, 0xFF, 0xFF)));
        Place(hi, 58, 68.6);
        c.Children.Add(hi);
        return c;
    }

    /// <summary>
    /// The turban wound round its head: the look's colour lit from the top, its folds, a plume of the sash's colour, and the
    /// jewel at its front in a gold setting, with the glow and the star it flares with (<paramref name="glow"/>, <paramref name="star"/>).
    /// </summary>
    Canvas Turban(out UIElement glow, out UIElement star)
    {
        var g = new Canvas { RenderTransform = new TransformGroup { Children = { _hatTurn, _hat } } };
        double rw = _p.RimW / S;
        var cloth = new LinearGradientBrush(Lighten(_p.Turban, .3), _p.Turban, 90); cloth.Freeze();
        var deep = Frozen(_p.TurbanDeep);
        var plume = new LinearGradientBrush(Lighten(_p.Sash, .3), _p.Sash, 0); plume.Freeze();
        g.Children.Add(new Path { Data = Geometry.Parse("M 60.5,32 C 62,19 73,11.5 86,14 C 79,17 71,23 66.5,33.5 Z"), Fill = plume, Stroke = Frozen(_p.SashDeep), StrokeThickness = rw * .8, StrokeLineJoin = PenLineJoin.Round });
        g.Children.Add(new Path { Data = Geometry.Parse(TurbanPath), Fill = cloth, Stroke = deep, StrokeThickness = rw, StrokeLineJoin = PenLineJoin.Round });
        g.Children.Add(new Path { Data = Geometry.Parse("M 29,41 C 40,30 52,28 60,35 C 68,28 80,30 91,41 M 32,30 C 42,21 53,20 60,27 C 67,20 78,21 88,30 M 40,20.5 C 47,16.5 53,17 60,21 C 67,17 73,16.5 80,20.5"), Stroke = Frozen(Color.FromArgb(0xB0, _p.TurbanDeep.R, _p.TurbanDeep.G, _p.TurbanDeep.B)), StrokeThickness = 1.8, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round, StrokeLineJoin = PenLineJoin.Round });
        var sheen = Disc(16, 6, Frozen(Color.FromArgb(0x70, 0xFF, 0xFF, 0xFF)));
        Place(sheen, 43, 25);
        sheen.RenderTransform = new RotateTransform(-16, 8, 3);
        g.Children.Add(sheen);
        // the jewel: its glow and its star (hidden till it flares), the gold setting, the stone and its glint
        var jg = Disc(30, 30, Glowing(Lighten(_p.Jewel, .2), .95));
        Place(jg, 60, 38);
        jg.Opacity = 0;
        g.Children.Add(jg);
        var jewel = new Canvas { RenderTransform = _jewelSize };
        var setting = new Ellipse { Width = 13.6, Height = 13.6, Fill = Frozen(_p.Gold), Stroke = Frozen(GoldDeep), StrokeThickness = 1.4 };
        Place(setting, 60, 38);
        var stoneFill = new RadialGradientBrush(Lighten(_p.Jewel, .55), Darken(_p.Jewel, .75)) { GradientOrigin = new Point(.35, .3) }; stoneFill.Freeze();
        var stone = Disc(8.4, 8.4, stoneFill);
        Place(stone, 60, 38);
        var glint = Disc(3, 3, Frozen(Color.FromArgb(0xE0, 0xFF, 0xFF, 0xFF)));
        Place(glint, 58.4, 36.4);
        jewel.Children.Add(setting); jewel.Children.Add(stone); jewel.Children.Add(glint);
        g.Children.Add(jewel);
        var st = new Path { Data = Geometry.Parse(StarPath(11)), Fill = Brushes.White, Opacity = 0, RenderTransform = new TranslateTransform(60, 38) };
        g.Children.Add(st);
        glow = jg; star = st;
        return g;
    }

    /// <summary>An arm on <paramref name="side"/>: a short furry limb from the shoulder to a round paw with a gold bangle, turned by <paramref name="turn"/>.</summary>
    Canvas Arm(int side, Transform turn)
    {
        var a = new Canvas { RenderTransform = turn };
        Canvas.SetLeft(a, 60 + side * 20); Canvas.SetTop(a, 101);
        double ax = -side * 4.5, ay = 14.5;
        double rw = _p.RimW / S;
        a.Children.Add(new Line { X1 = 0, Y1 = 0, X2 = ax, Y2 = ay, Stroke = Frozen(_furLine), StrokeThickness = 11 + 2 * rw, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round });
        a.Children.Add(new Line { X1 = 0, Y1 = 0, X2 = ax, Y2 = ay, Stroke = Frozen(Fur), StrokeThickness = 11, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round });
        var paw = new Ellipse { Width = 15.5, Height = 16.5, Fill = FurFill(), Stroke = Frozen(_furLine), StrokeThickness = rw };
        Place(paw, ax, ay + 1);
        a.Children.Add(paw);
        a.Children.Add(new Path { Data = Geometry.Parse(Fmt("M {0},{1} Q {2},{3} {4},{1}", ax - 7.2, ay - 3, ax, ay - .6, ax + 7.2)), Stroke = Frozen(_p.Gold), StrokeThickness = 2.8, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round });
        return a;
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
    static Color Mix(Color a, Color b, double t) => Color.FromRgb((byte)(a.R + (b.R - a.R) * t), (byte)(a.G + (b.G - a.G) * t), (byte)(a.B + (b.B - a.B) * t));

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
        _cheekL.Opacity = _cheekR.Opacity = blush ? 1 : .5;
        _eyeScale.ScaleX = f == Face.Alert ? 1.22 : f == Face.Excited ? 1.12 : 1;
        _eyeScale.ScaleY = f == Face.Alert ? 1.22 : f == Face.Excited ? 1.12 : f is Face.Fierce or Face.Fired ? .74 : 1;   // a determined squint
        _mouthO.Visibility = f is Face.Alert or Face.Sleepy ? Visibility.Visible : Visibility.Collapsed;
        _mouthO.RenderTransform = f == Face.Sleepy ? new ScaleTransform(.7, .6, 60, 82.5) : Transform.Identity;
        _tongue.Visibility = f is Face.Excited or Face.Happy ? Visibility.Visible : Visibility.Collapsed;
        string mouth = f switch
        {
            Face.Happy or Face.Excited => "M 52.5,78.5 Q 60,92 67.5,78.5 Z",
            Face.Proud => "M 53,78.5 C 55.5,83 59,82 60,78.5 C 61,82 64.5,83 67,78.5",
            Face.Fired => "M 53,78.5 Q 60,89 67,78.5 Z",
            Face.Fierce => "M 53.5,84.5 Q 60,79 66.5,84.5",
            Face.Focused => "M 55,81 L 65,80.5",
            Face.Sad => "M 54,84.5 Q 60,78.5 66,84.5",
            Face.Dazed => "M 52.5,82 Q 56.2,78 60,82 Q 63.8,86 67.5,82",
            Face.Alert or Face.Sleepy => "",
            _ => "M 54,79 C 56,82 59,81 60,78 C 61,81 64,82 66,79",
        };
        _mouth.Data = mouth.Length > 0 ? Geometry.Parse(mouth) : null;
        _mouth.Fill = f is Face.Happy or Face.Excited or Face.Fired ? MouthIn : null;
    }

    static readonly SolidColorBrush MouthIn = B(0x8A3A46);

    Face RestFace => _mood switch { Mood.Focused => Face.Focused, Mood.Sleepy => Face.Sleepy, _ => Face.Idle };

    /* ---------------- idle ---------------- */

    /// <summary>
    /// The idle: still between flourishes (an ear twitch, a sniff or a glance with a little bounce, every few seconds) and
    /// blinks, so it draws nothing most of the time (Steven, 2026-11-20: "lightweight - not bulky"). Started while it shows,
    /// stopped when it hides.
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

    /// <summary>A flourish: one move of the idle, then still till the next (sooner while a fight is on, later while it dozes).</summary>
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

    /// <summary>The mood at rest: idle, squared up for a fight, dozing after a long quiet.</summary>
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
    /// <summary>Its clothes (the tests: each theme family and tinted look its own).</summary>
    internal string TintForTest => _p.Turban + "|" + _p.Vest + "|" + _p.Sash;

    void Idle(double p)
    {
        double w = 2 * Math.PI * p, s = Math.Sin(Math.PI * p);
        Expression(p);
        _earTwL = _earTwR = 0; _tailIdle = 0;
        _nose.ScaleX = _nose.ScaleY = 1;
        switch (_mood)
        {
            case Mood.Focused:
                // squared up: ears perked, paws up and ready, the turban's jewel aglow, bouncing on its toes
                _bobY = -1.6 * Math.Abs(Math.Sin(w * 2)); _earIdle = -10 + 3 * Math.Sin(w * 2); _armIdle = 40 + 8 * Math.Sin(w * 2); _auraIdle = .62; _jewelIdle = .35;
                _zs.Opacity = 0;
                break;
            case Mood.Sleepy:
                // ears drooped, nodding, z's rising
                _bobY = 1.2 * Math.Sin(w) + 1; _earIdle = 56 + 4 * Math.Sin(w); _armIdle = -4; _auraIdle = .3; _jewelIdle = 0;
                ZsAt(p);
                break;
            default:
                _bobY = -1.4 * s * s; _earIdle = 3 * Math.Sin(w); _armIdle = 6 * s; _auraIdle = .5; _jewelIdle = 0;
                _zs.Opacity = 0;
                if (_variant == 2)
                {
                    // a sniff and a twitch of one ear
                    double sn = Bump(p, .15, .45);
                    _nose.ScaleX = 1 + .1 * sn * Math.Abs(Math.Sin(w * 6)); _nose.ScaleY = 1 + .06 * sn;
                    _earTwR = -16 * Bump(p, .5, .62) + 8 * Bump(p, .62, .74);
                }
                else if (_variant == 1)
                {
                    // a smile and a wiggle of both ears
                    double wg = Bump(p, .3, .75);
                    _earTwL = 10 * wg * Math.Sin(w * 5); _earTwR = -10 * wg * Math.Sin(w * 5);
                    _armIdle = 6 * s + 22 * wg;
                    _tailIdle = 18 * wg * Math.Sin(w * 4);
                }
                else
                {
                    // a glance one way and back, its ears turning with it
                    double g = Math.Sin(w);
                    _earTwL = -7 * g; _earTwR = 7 * g;
                }
                break;
        }
        Compose();
    }

    /// <summary>
    /// Its face through an idle flourish, awake only (a reaction's face is left alone): a glance one way and back, or a smile
    /// in the middle of it; between flourishes it looks ahead with its mood's face.
    /// </summary>
    void Expression(double p)
    {
        if (_playing != null || _mood == Mood.Sleepy) { _eyeLook.X = 0; _eyeLook.Y = 0; return; }
        _eyeLook.X = _variant == 0 ? 3.6 * Math.Sin(2 * Math.PI * p) : 0;
        _eyeLook.Y = _variant == 0 ? -1 * Math.Abs(Math.Sin(2 * Math.PI * p)) : 0;
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
            at.X = 17 + 7 * q + i * 2; at.Y = -30 - 12 * q;
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
    /// Plays a reaction: a Kinah coin flipped high for a rare drop (sparks in its grade's colour), a crown on its turban and
    /// rays for a new best, a cheer and confetti for a boss down, flames and pumping fists for a streak, ears up and an alert
    /// mark for a timer, a squint, its turban's jewel flaring and crossed blades for a takedown, drooped ears and a rain cloud
    /// for a wipe, dizzy stars and its turban knocked askew for a fall. Then back to its mood. A reaction under way gives way
    /// to the new one.
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
    static Color Darken(Color c, double k) => Color.FromRgb((byte)(c.R * k), (byte)(c.G * k), (byte)(c.B * k));

    /// <summary>The reaction's frame at <paramref name="t"/> ms from its start.</summary>
    void Frame(double t)
    {
        Clear();
        double fadeOut = 1 - Win(t, ReactMs - 380, ReactMs);   // everything a moment adds fades in its last 380 ms
        double settle = 1 - Win(t, 1200, ReactMs);              // the pose eases back to rest at the end
        switch (_playing)
        {
            case CompanionKind.Rare:
                // it pulls a Kinah coin from its sash and flips it high, a paw up, ears perked, tail wagging
                Hop(t, 0, 5); Hop(t, 700, 4.5);
                Flash(t, 420, Colors.White);
                Coin(t);
                _reTail = 16 * Math.Sin(2 * Math.PI * t / 200) * Bump(t, 200, 1500);
                _reArmL = 128 * Motion.Out(Win(t, 60, 300)) * settle; _reArmR = 112 * Motion.Out(Win(t, 40, 240)) * settle + 10 * Math.Sin(2 * Math.PI * t / 300) * Bump(t, 300, 1300);
                _reEar = -5 * Motion.Out(Win(t, 0, 200)) * settle + 2 * Math.Sin(2 * Math.PI * t / 140) * Bump(t, 300, 1200);
                Sparks(t, 380, 700, 10, 42, 0);
                Ring(_ring, t, 380, 560, 48, 88); Ring(_ring2, t, 520, 560, 44, 80);
                _reAura = Math.Max(1.2 * Bump(t, 340, 540), .85 * Bump(t, 250, 1700));
                break;
            case CompanionKind.Best:
                // the crown lands on its turban, it puffs up, one paw punching the air
                Hop(t, 0, 5);
                Flash(t, 600, Rgb(0xFFF4C8));
                double power = Bump(t, 580, 860);
                _reSx *= 1 + .05 * power; _reSy *= 1 + .05 * power;
                double drop = Win(t, 400, 720);
                _crownAt.Y = -33 - 8 * (1 - Back(drop));
                _crown.Opacity = Math.Min(1, drop * 2.5) * fadeOut;
                _rays.Opacity = Win(t, 160, 520) * fadeOut;
                _raysTurn.Angle = t * 0.035;
                _reArmR = 108 * Back(Win(t, 520, 760)) * settle; _reArmL = -18 * Win(t, 520, 700) * settle;
                _reEar = -10 * Win(t, 500, 700) * settle;
                Ring(_ring, t, 580, 520, 48, 88); Ring(_ring2, t, 700, 520, 44, 80);
                Sparks(t, 600, 760, 16, 40, 18);
                _reAura = Math.Max(1.2 * Bump(t, 580, 780), .85 * Win(t, 200, 600) * fadeOut);
                break;
            case CompanionKind.Kill:
                // a cheer: two hops, both paws up and waving, ears flapping
                Hop(t, 0, 6); Hop(t, 620, 5.5);
                Flash(t, 0, Colors.White);
                Confetti(t, 330);
                double wave = 14 * Math.Sin(2 * Math.PI * t / 260);
                _reArmL = (118 + wave) * Motion.Out(Win(t, 0, 180)) * settle; _reArmR = (118 - wave) * Motion.Out(Win(t, 0, 180)) * settle;
                _reEar = 9 * Math.Abs(Math.Sin(2 * Math.PI * t / 300)) * settle;   // floppy with joy
                _reTail = 20 * Math.Sin(2 * Math.PI * t / 180) * settle;
                _reAura = .45 * Bump(t, 200, 1500);
                break;
            case CompanionKind.Streak:
                // fired up: flames round its head, a squint, its turban's jewel ablaze, fists pumping, ears up
                _flames.Opacity = Win(t, 0, 200) * fadeOut;
                for (int i = 0; i < _flames.Children.Count; i++)
                {
                    var g = (TransformGroup)_flames.Children[i].RenderTransform;
                    var s = (ScaleTransform)g.Children[0];
                    double f = Math.Abs(Math.Sin(2 * Math.PI * (t / 260 + i * 0.27)));
                    s.ScaleY = .58 * (.7 + .45 * f); s.ScaleX = .58 * (.9 + .25 * (1 - f));
                }
                _tint.Opacity = .12 * Win(t, 0, 240) * fadeOut;
                Embers(t);
                _reX = 1.1 * Math.Sin(2 * Math.PI * t / 60) * (1 - Win(t, 0, 700));
                Flash(t, 0, Rgb(0xFFD08A));
                _reJewel = Motion.Out(Win(t, 0, 140)) * settle;
                double pump = Math.Sin(2 * Math.PI * t / 300);
                _reArmL = (80 + 45 * Math.Max(0, pump)) * Win(t, 0, 120) * settle; _reArmR = (80 + 45 * Math.Max(0, -pump)) * Win(t, 0, 120) * settle;
                _reEar = (-9 + 5 * Math.Sin(2 * Math.PI * t / 120)) * settle;   // up and buffeted by the heat
                _reAura = .7 * Win(t, 0, 300) * fadeOut;
                break;
            case CompanionKind.Timer:
                // startled: ears shoot straight up, eyes wide, paws out, an alert mark pops beside its head
                _reSy = 1 - .08 * Bump(t, 0, 160); _reSx = 1 + .03 * Bump(t, 0, 160);
                double pop = Win(t, 60, 340);
                var ag = (TransformGroup)_alert.RenderTransform;
                var asz = (ScaleTransform)ag.Children[0];
                asz.ScaleX = asz.ScaleY = .85 * Back(pop);
                _alert.Opacity = Math.Min(1, pop * 4) * fadeOut;
                _reTurn = 4 * Math.Sin(2 * Math.PI * t / 220) * (1 - Win(t, 200, 1100));
                _reEar = -11 * Back(Win(t, 0, 200)) * settle;
                _reEarL = 4 * Math.Sin(2 * Math.PI * t / 90) * Bump(t, 150, 900); _reEarR = -_reEarL;
                _reArmL = _reArmR = 46 * Back(Win(t, 0, 180)) * settle;
                Flash(t, 40, Rgb(0xFFE2A0));
                Ring(_ring, t, 0, 560, 48, 88); Ring(_ring2, t, 520, 560, 44, 82);
                _reAura = .6 * Bump(t, 0, 1400);
                break;
            case CompanionKind.Pvp:
                // a determined squint, its turban's jewel flaring, a lunge, crossed blades beside its head, fists out
                _reX = t < 170 ? 3 * Motion.Out(t / 170) : 3 * (1 - Motion.Out(Win(t, 170, 620)));
                _reSx = 1 + .05 * Bump(t, 0, 300); _reSy = 1 - .12 * Bump(t, 0, 300);
                _reY = 1.4 * Math.Sin(2 * Math.PI * t / 50) * Bump(t, 120, 520);   // the clash shakes it
                Flash(t, 120, Rgb(0xFFD0D0));
                Ring(_ring, t, 140, 480, 46, 88);
                _reJewel = Back(Win(t, 0, 160)) * settle;
                _reArmR = 92 * Back(Win(t, 40, 220)) * settle; _reArmL = 60 * Back(Win(t, 80, 260)) * settle;
                _reEar = -8 * Win(t, 0, 160) * settle;   // ears up and sharp
                double clash = Win(t, 80, 400);
                var sg = (TransformGroup)_slashes.RenderTransform;
                ((ScaleTransform)sg.Children[0]).ScaleX = ((ScaleTransform)sg.Children[0]).ScaleY = .8 * Back(clash);
                ((RotateTransform)sg.Children[1]).Angle = -30 * (1 - Motion.Out(clash));
                _slashes.Opacity = Math.Min(1, clash * 4) * fadeOut;
                _slashes.Children[0].Opacity = Bump(t, 220, 900);
                Sparks(t, 330, 520, 10, 30, 22);
                _reAura = Math.Max(1.1 * Bump(t, 100, 300), .7 * Bump(t, 120, 1300));
                break;
            case CompanionKind.Wipe:
                // it slumps: ears flat down, paws limp, a rain cloud over it and a tear
                double sink = Motion.Out(Win(t, 0, 520)) * fadeOut;
                _reY = 3.5 * sink; _reSy = 1 - .07 * sink; _reSx = 1 + .03 * sink;
                _reEar = 64 * sink; _reArmL = _reArmR = -8 * sink; _reTail = 24 * sink;
                _tint.Opacity = .3 * sink;
                double cloud = Win(t, 100, 480);
                ((TranslateTransform)((TransformGroup)_cloud.RenderTransform).Children[1]).Y = -45 + 8 * Back(cloud) - 4 * sink;
                _cloud.Opacity = Math.Min(1, cloud * 3) * fadeOut;
                _rain.Opacity = Win(t, 420, 560) * fadeOut;
                for (int i = 0; i < _rain.Children.Count; i++)
                {
                    double q = (t / 420 + i * 0.31) % 1;
                    var r = (TranslateTransform)_rain.Children[i].RenderTransform;
                    r.Y = -34 + 12 * q - 4 * sink; r.X = -6 + i * 4 - 1.5 * q;
                    _rain.Children[i].Opacity = Math.Sin(Math.PI * q);
                }
                double fall = Win(t, 700, 1500);
                ((TranslateTransform)_tear.RenderTransform).Y = RY(70) + 12 * fall * fall;
                _tear.Opacity = Bump(t, 600, 1600);
                _reAura = -.3 * sink;
                break;
            case CompanionKind.Death:
                // knocked silly: it tips over, its turban askew, ears flopped every which way, stars round its head
                double tip = Motion.Out(Win(t, 0, 420)) * fadeOut;
                _reTurn = -5 * tip + 2.5 * Math.Sin(2 * Math.PI * t / 520) * tip; _reY = 3 * tip; _reSy = 1 - .06 * tip;
                _reEar = 52 * tip; _reEarL = 14 * tip; _reEarR = -12 * tip;
                _reArmL = 24 * tip; _reArmR = -6 * tip;
                _reHat = 3 / S * tip; _reHatTurn = 16 * tip;
                Flash(t, 0, Rgb(0xFF8A8A));
                _tint.Opacity = .22 * tip;
                _dizzy.Opacity = Win(t, 200, 420) * fadeOut;
                for (int i = 0; i < _dizzy.Children.Count; i++)
                {
                    double a = t / 520 * 2 * Math.PI + i * 2 * Math.PI / 3;
                    var d = (TranslateTransform)_dizzy.Children[i].RenderTransform;
                    d.X = 19 * Math.Cos(a); d.Y = -36 + 4 * Math.Sin(a);
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

    /// <summary>The coin's flight on a rare drop: from its sash's buckle, up over its head spinning, held there shining, then gone.</summary>
    void Coin(double t)
    {
        if (t < 80) return;
        double up = Motion.Out(Win(t, 80, 440));
        double x0 = 0, y0 = RY(130), x1 = -25, y1 = -27.5;
        _coinAt.X = x0 + (x1 - x0) * up;
        _coinAt.Y = y0 + (y1 - y0) * up - 10 * Math.Sin(Math.PI * up) + 1.6 * Math.Sin(2 * Math.PI * t / 500) * Win(t, 440, 700);
        // it spins fast on the way up and slows to show its face
        _coinSpin.ScaleX = t < 440 ? Math.Max(.12, Math.Abs(Math.Cos(t / 36))) : 1 - .1 * Bump(t, 440, 560);
        double s = .8 + .38 * up;
        _coinSpin.ScaleY = s; _coinSpin.ScaleX *= s;
        _coin.Opacity = Math.Min(1, (t - 80) / 60) * (1 - Win(t, ReactMs - 420, ReactMs - 80));
    }

    /// <summary>What a reaction adds, cleared before each of its frames and at its end.</summary>
    void Clear()
    {
        _reX = _reY = _reTurn = _reAura = 0; _reSx = _reSy = 1;
        _reEar = _reEarL = _reEarR = _reArmL = _reArmR = _reHat = _reHatTurn = _reJewel = _reTail = 0;
        _crown.Opacity = _alert.Opacity = _cloud.Opacity = _tear.Opacity = _coin.Opacity = 0;
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
        if (a < 80) { double k = a / 80; _reSy *= 1 - .18 * k; _reSx *= 1 + .05 * k; return; }
        if (a < 500) { double k = (a - 80) / 420; _reY -= height * Math.Sin(Math.PI * k); _reSy *= 1 + .08 * Math.Cos(Math.PI * k); _reSx *= 1 - .08 * Math.Cos(Math.PI * k); return; }
        double l = Math.Sin(Math.PI * (a - 500) / 180); _reSy *= 1 - .2 * l; _reSx *= 1 + .06 * l;
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
            s.At.X = speed * Math.Cos(ang) * sec; s.At.Y = speed * Math.Sin(ang) * sec + 46 * sec * sec - 22;   // from over its head
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

    /// <summary>The impact: it flashes <paramref name="c"/> as the moment lands at <paramref name="t0"/>, gone in 220 ms.</summary>
    void Flash(double t, double t0, Color c)
    {
        if (t < t0 || t > t0 + 220) return;
        ((SolidColorBrush)_flash.Fill).Color = c;
        _flash.Opacity = .75 * (1 - Motion.Out((t - t0) / 220));
    }

    /// <summary>A streak's embers: the sparks, small, rising from round its head and fading as they go.</summary>
    void Embers(double t)
    {
        for (int i = 0; i < _sparks.Count; i++)
        {
            var s = _sparks[i];
            double q = (t / 900 + i * 0.137) % 1;
            s.At.X = -20 + (i * 37) % 40 + 4 * Math.Sin(2 * Math.PI * (q + i * .3)); s.At.Y = -18 - 26 * q;
            s.Size.ScaleX = s.Size.ScaleY = .55 * (1 - q);
            s.Turn.Angle = 90 * q;
            s.P.Opacity = Math.Sin(Math.PI * q) * (1 - Win(t, ReactMs - 380, ReactMs)) * Win(t, 0, 200);
        }
    }

    /// <summary>The idle and the reaction added up into its transforms: body, ears, paws, turban, glow and shadow.</summary>
    void Compose()
    {
        double y = _bobY + _reY;
        _move.X = _reX; _move.Y = y;
        _squash.ScaleX = _reSx; _squash.ScaleY = _reSy;
        _lean.Angle = _reTurn + (_mood == Mood.Focused && _playing == null ? -4 : 0);
        // an ear's turn: its rest tilt up, then the idle's and the reaction's droop (positive down), each ear its own twitch
        double droop = _earIdle + _reEar;
        double dR = Math.Clamp(droop + _earTwR + _reEarR, -30, 75), dL = Math.Clamp(droop + _earTwL + _reEarL, -30, 75);
        _earR.Angle = .8 * dR; _earL.Angle = -.8 * dL;
        _earLenR.ScaleX = _earLenR.ScaleY = EarPerk(dR); _earLenL.ScaleX = _earLenL.ScaleY = EarPerk(dL);
        bool rest = _playing == null;
        double aR = rest ? _armIdle : _reArmR, aL = rest ? _armIdle : _reArmL;
        _armR.Angle = -aR; _armL.Angle = aL;
        Reach(aL, _armLen, _armOutL, -1); Reach(aR, _armLenR, _armOutR, 1);
        _hat.Y = _reHat;
        _hatTurn.Angle = _reHatTurn;
        double jewel = rest ? _jewelIdle : _reJewel;
        _jewelGlow.Opacity = Math.Clamp(jewel, 0, 1);
        _jewelStar.Opacity = Math.Clamp(jewel - .4, 0, .9);
        _jewelSize.ScaleX = _jewelSize.ScaleY = 1 + .3 * Math.Max(0, jewel - .35);
        _tail.Angle = rest ? _tailIdle : _reTail;
        _aura.Opacity = Math.Clamp(_auraIdle + Math.Min(0, _reAura), 0, 1);
        // the higher it hops, the smaller and fainter its shadow (the idle's bounce leaves it be: one thing less to draw)
        double lift = Math.Clamp(-_reY / 16, -0.4, 1);
        double k = 1 - .35 * lift;
        _shadowSize.ScaleX = _shadowSize.ScaleY = k;
        _shadow.Opacity = .9 - .5 * lift;
    }

    /// <summary>An ear's size at a droop of <paramref name="droop"/> degrees: a touch bigger as it perks up (below 0).</summary>
    static double EarPerk(double droop) => 1 + .2 * Math.Clamp(-droop / 11, 0, 1);

    /// <summary>An arm lifted <paramref name="raise"/> degrees: its shoulder out and up and the arm longer, so a paw in the air clears the head.</summary>
    static void Reach(double raise, ScaleTransform len, TranslateTransform at, int side)
    {
        double k = Math.Clamp(raise / 130, 0, 1);
        len.ScaleX = len.ScaleY = 1 + .35 * k;
        at.X = side * 8 * k; at.Y = -4 * k;
    }

    /// <summary>The frame that says a reaction best (renders, the stage's still, motion off): ms from its start.</summary>
    public static double KeyMs(CompanionKind k) => k switch
    {
        CompanionKind.Rare => 900, CompanionKind.Best => 760, CompanionKind.Kill => 300, CompanionKind.Streak => 520, CompanionKind.Timer => 380,
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
