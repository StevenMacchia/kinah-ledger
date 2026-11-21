using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using Path = System.Windows.Shapes.Path;
using System.Windows.Threading;
using static KinahLedger.Ui.HudKit;

namespace KinahLedger.Ui;

/// <summary>
/// The companion itself: a tiny Shugo-style merchant critter (Steven, 2026-11-21: "maybe a tiny shugo lol"), drawn in vectors,
/// our own design, no picture. A round head bigger than its body, two long ears that perk and flop with its mood, a big round
/// nose, beady glossy eyes, goggles pushed up on its forehead, a scarf, stubby paws and feet, and a coin pouch at its hip: it
/// is a Kinah tracker's pet, so a rare drop has it toss a Kinah coin. Lit from the top left with a rim light behind, a glow
/// behind it and a shadow on the ground under it. Each overlay theme family gives it its own fur and gear (Classic gold,
/// Modern pearl, Tactical gunmetal with cyan gear in a reticle, Wacky a sticker with a white edge and a hard shadow, Glitch
/// phosphor green with scan lines and a colour split when it reacts, Atreia aether violet, Esports team colours, Elements and
/// Overdrive by their looks), so it matches whatever look the Buddy uses.
///
/// It idles (breathes, blinks, twitches an ear, sniffs, glances round), pulls its goggles down while a fight is on, dozes with
/// its ears drooped after a long quiet, and plays a reaction for a moment (<see cref="React"/>): every course goes through
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
    /// <summary>The glow's and the bursts' middle, and where the critter stands (its belly; its feet are 35 below).</summary>
    const double Cx = 48, Cy = 50, Ry = 48;
    /// <summary>The ears' size against their drawn path: long, yet inside the window however they turn.</summary>
    const double EarK = .86;
    /// <summary>How far its goggles come down over its eyes (a fight, a takedown, a streak).</summary>
    const double GogDown = 14.5;

    /// <summary>
    /// A theme's look: the fur's light, base, deep and its outline; the belly and muzzle, the inner ears, the nose; the scarf,
    /// the goggles' metal and glass, the pouch's leather; the glow behind; the eyes' ink, the cheeks; the outline's width.
    /// </summary>
    sealed record Palette(Color FurLight, Color Fur, Color FurDeep, Color Rim, Color Belly, Color EarIn, Color Nose,
        Color Scarf, Color ScarfDeep, Color Metal, Color Lens, Color Leather, Color Glow, Color Ink, Color Cheek, double RimW);

    /// <summary>
    /// The fur and gear for a theme family, and for a look where its family's looks differ too much for one (Elements: frost
    /// against magma; Atreia: Elyos gold against Asmodian crimson; Overdrive's dragon).
    /// </summary>
    static Palette PaletteOf(string theme, string? look) => (theme, look) switch
    {
        (HudThemes.Modern, _) => new(Rgb(0xFFFFFF), Rgb(0xE8EEF7), Rgb(0xA4B3CC), Rgb(0x4A5873), Rgb(0xFFFFFF), Rgb(0xFFC4D2), Rgb(0xF08CA8),
            Rgb(0x7FA6FF), Rgb(0x3E62C4), Rgb(0xD9E2F0), Rgb(0xBFD6FF), Rgb(0x6C7A96), Rgb(0xA9C8FF), Rgb(0x1D2433), Rgb(0xFF9DB4), 1.3),
        (HudThemes.Tactical, _) => new(Rgb(0xD5E3EA), Rgb(0x8FA6B3), Rgb(0x4B6272), Rgb(0x071B24), Rgb(0xE4F4F8), Rgb(0x7EE6F2), Rgb(0x2E4A58),
            Rgb(0x3FE0F5), Rgb(0x0B8EAF), Rgb(0x2B3B44), Rgb(0x3FE0F5), Rgb(0x34454F), Rgb(0x3FE0F5), Rgb(0x03222C), Rgb(0xFF8FA6), 1.4),
        (HudThemes.Wacky, _) => new(Rgb(0xFFE6F8), Rgb(0xFF6BD2), Rgb(0xC21C8E), Rgb(0x1C0A2B), Rgb(0xFFF2FB), Rgb(0xA4FF6E), Rgb(0xFFE14D),
            Rgb(0xA4FF6E), Rgb(0x34C82A), Rgb(0x7B3CFF), Rgb(0x7FF0FF), Rgb(0x7B3CFF), Rgb(0xFF52C9), Rgb(0x1C0A2B), Rgb(0xFFE14D), 2.2),
        (HudThemes.Glitch, _) => new(Rgb(0xE2FFE8), Rgb(0x3CFF8A), Rgb(0x0FA852), Rgb(0x04381B), Rgb(0xB8FFD2), Rgb(0xA8FFC8), Rgb(0x0C8A43),
            Rgb(0x0E6A35), Rgb(0x04381B), Rgb(0x0E6A35), Rgb(0x9DFFC4), Rgb(0x0B5A2C), Rgb(0x3CFF8A), Rgb(0x021A0C), Rgb(0x3CFF8A), 1.3),
        // Atreia: Elyos white and gold, Asmodian crimson and shadow, Rift and Prism the Abyss's aether violet
        (HudThemes.Atreia, HudThemes.Elyos) => new(Rgb(0xFFFAEE), Rgb(0xF1E2C4), Rgb(0xBFA272), Rgb(0x4A3412), Rgb(0xFFFDF6), Rgb(0xFFC9B0), Rgb(0xE08A6A),
            Rgb(0xFFD873), Rgb(0xC99528), Rgb(0xF5CF6A), Rgb(0xBFEFFF), Rgb(0x8A6A3A), Rgb(0xFFE29A), Rgb(0x2A1C08), Rgb(0xFF9A80), 1.3),
        (HudThemes.Atreia, HudThemes.Asmodian) => new(Rgb(0xD9D2DE), Rgb(0x8E8597), Rgb(0x4C4356), Rgb(0x14090F), Rgb(0xE6DFEA), Rgb(0xE3708A), Rgb(0x7A3A4E),
            Rgb(0xE0263E), Rgb(0x8A0E22), Rgb(0x3A3440), Rgb(0xFF5A6E), Rgb(0x2E2230), Rgb(0xFF3048), Rgb(0x14070C), Rgb(0xFF6A82), 1.4),
        (HudThemes.Atreia, HudThemes.Rift) => new(Rgb(0xF6EEFF), Rgb(0xCDB9F2), Rgb(0x7A5FB8), Rgb(0x1C1036), Rgb(0xFBF7FF), Rgb(0xF0A8FF), Rgb(0xB0609E),
            Rgb(0xA05CFF), Rgb(0x5A22C0), Rgb(0xD9C8FF), Rgb(0xE0B8FF), Rgb(0x4A3470), Rgb(0xB070FF), Rgb(0x180C30), Rgb(0xFF8FD0), 1.4),
        (HudThemes.Atreia, _) => new(Rgb(0xFFF6EA), Rgb(0xE9D6BA), Rgb(0xA98C6A), Rgb(0x2E1E3E), Rgb(0xFFFAF2), Rgb(0xD9B8FF), Rgb(0xC97A8E),
            Rgb(0x9B7BFF), Rgb(0x5A3FC0), Rgb(0xF0C46A), Rgb(0xC9B8FF), Rgb(0x6A4A3A), Rgb(0xA98BFF), Rgb(0x1E1430), Rgb(0xFF8FB0), 1.3),
        // Esports: the finals' black and gold, the hype drink's lime, and the stream's team cyan and magenta for the rest
        (HudThemes.Esports, HudThemes.Finals) => new(Rgb(0xFFF1CC), Rgb(0xE2BC7A), Rgb(0xA07A3A), Rgb(0x120E06), Rgb(0xFFF8E6), Rgb(0xFFB59A), Rgb(0xC9664E),
            Rgb(0x1C1A16), Rgb(0x0A0906), Rgb(0xE8C46A), Rgb(0xFFE6A0), Rgb(0x2A2418), Rgb(0xE8C46A), Rgb(0x150E04), Rgb(0xFF8A70), 1.4),
        (HudThemes.Esports, HudThemes.Hype) => new(Rgb(0xFFF0D6), Rgb(0xEDBF82), Rgb(0xAE7A42), Rgb(0x1A1030), Rgb(0xFFF8EA), Rgb(0xC6FF3D), Rgb(0xD9655A),
            Rgb(0xC6FF3D), Rgb(0x6FB800), Rgb(0x8A3DFF), Rgb(0xE0B8FF), Rgb(0x5A2AB0), Rgb(0xB6FF2E), Rgb(0x1A0E2E), Rgb(0xFF7AC0), 1.5),
        (HudThemes.Esports, HudThemes.Telestrator) => new(Rgb(0xFFF0D6), Rgb(0xEAB97C), Rgb(0xAA753E), Rgb(0x14140E), Rgb(0xFFF8EA), Rgb(0xFFB0A0), Rgb(0xD9655A),
            Rgb(0xFFD21E), Rgb(0xC08A00), Rgb(0xF2F2F2), Rgb(0xFFF2A8), Rgb(0x2E2E2A), Rgb(0xFFD21E), Rgb(0x12120C), Rgb(0xFF7A8E), 1.4),
        (HudThemes.Esports, _) => new(Rgb(0xFFF0D6), Rgb(0xEAB97C), Rgb(0xAA753E), Rgb(0x101828), Rgb(0xFFF8EA), Rgb(0xFF8FD0), Rgb(0xD9655A),
            Rgb(0x22D3FF), Rgb(0x0A7FB8), Rgb(0xFF2E88), Rgb(0xA8F0FF), Rgb(0x283448), Rgb(0x22D3FF), Rgb(0x0C1220), Rgb(0xFF7A8E), 1.4),
        // Elements: one per element
        (HudThemes.Elements, HudThemes.Magma) => new(Rgb(0xFFD9A8), Rgb(0xC9703A), Rgb(0x6E2E14), Rgb(0x1E0804), Rgb(0xFFE2BE), Rgb(0xFF8A3D), Rgb(0x8A2A12),
            Rgb(0xFF5A1E), Rgb(0xB82A08), Rgb(0x3A2A24), Rgb(0xFFC85A), Rgb(0x2A1A14), Rgb(0xFF6A1A), Rgb(0x1A0602), Rgb(0xFF8A3D), 1.4),
        (HudThemes.Elements, HudThemes.Storm) => new(Rgb(0xEDEBFF), Rgb(0xAFA8D8), Rgb(0x5E5690), Rgb(0x120E2E), Rgb(0xF4F2FF), Rgb(0xFFE45A), Rgb(0x7A6AB8),
            Rgb(0xFFE45A), Rgb(0xC9A400), Rgb(0x3A3460), Rgb(0xD6F4FF), Rgb(0x3A3460), Rgb(0x9C8CFF), Rgb(0x120E2A), Rgb(0xFFB0D0), 1.4),
        (HudThemes.Elements, HudThemes.Tide) => new(Rgb(0xE6FBF8), Rgb(0x8FD6CC), Rgb(0x3A8A8A), Rgb(0x06262E), Rgb(0xF0FFFC), Rgb(0xFFB8C8), Rgb(0xE07A8E),
            Rgb(0x1EA6D9), Rgb(0x0A5A8A), Rgb(0xE0C27A), Rgb(0xB8F4FF), Rgb(0x2A5A64), Rgb(0x4FE0E0), Rgb(0x04202A), Rgb(0xFF9AB0), 1.4),
        (HudThemes.Elements, _) => new(Rgb(0xFFFFFF), Rgb(0xDDEFFC), Rgb(0x8FB4D6), Rgb(0x0E2A44), Rgb(0xFFFFFF), Rgb(0xBFE6FF), Rgb(0x8CB8E8),
            Rgb(0x6FD0FF), Rgb(0x1E7FC0), Rgb(0xB8D8F0), Rgb(0xE6F8FF), Rgb(0x4A6A8A), Rgb(0x8FDcFF), Rgb(0x0A1A2E), Rgb(0xFF9EC0), 1.3),
        // Overdrive: wild; the dragon's blood red, else a power-up's hot orange
        (HudThemes.Overdrive, HudThemes.Dragon) => new(Rgb(0xFFE0C0), Rgb(0xD69A5E), Rgb(0x7A4220), Rgb(0x14040A), Rgb(0xFFEAD2), Rgb(0xFF6A5A), Rgb(0xA0382A),
            Rgb(0xD8142E), Rgb(0x6E0614), Rgb(0x2E2A2E), Rgb(0xFF3A3A), Rgb(0x3A1A14), Rgb(0xFF2A3A), Rgb(0x16040A), Rgb(0xFF6A6A), 1.5),
        // Manga: inked, white and black with a red flash; Aura: a powered-up gold with an electric blue aura
        (HudThemes.Overdrive, HudThemes.Manga) => new(Rgb(0xFFFFFF), Rgb(0xF2F2F2), Rgb(0xB8B8C0), Rgb(0x0A0A0E), Rgb(0xFFFFFF), Rgb(0xFF9AAE), Rgb(0x2A2A30),
            Rgb(0xE8203A), Rgb(0x8A0A1A), Rgb(0x2A2A30), Rgb(0xE6E6EE), Rgb(0x1A1A20), Rgb(0xFFFFFF), Rgb(0x0A0A0E), Rgb(0xFF7A8E), 1.9),
        (HudThemes.Overdrive, HudThemes.Aura) => new(Rgb(0xFFF8C8), Rgb(0xF5CF4A), Rgb(0xB0820E), Rgb(0x10183A), Rgb(0xFFFBE0), Rgb(0x8FD0FF), Rgb(0xE0784A),
            Rgb(0x2E8BFF), Rgb(0x0A4AB8), Rgb(0xDDE8FF), Rgb(0x9FE0FF), Rgb(0x203060), Rgb(0x4FB4FF), Rgb(0x0E1430), Rgb(0xFF9A7A), 1.5),
        (HudThemes.Overdrive, _) => new(Rgb(0xFFF0DA), Rgb(0xF0B86E), Rgb(0xA86A2A), Rgb(0x0E1430), Rgb(0xFFF6E8), Rgb(0xFF9A6A), Rgb(0xE05A4A),
            Rgb(0xFF7A1A), Rgb(0xB84A00), Rgb(0x2A3A5A), Rgb(0x5AB8FF), Rgb(0x1E2E50), Rgb(0xFF8A2A), Rgb(0x0E1020), Rgb(0xFF7A9A), 1.5),
        // Classic: the Kinah panel's gold
        _ => new(Rgb(0xFFF0D2), Rgb(0xEDC38A), Rgb(0xB07B42), Rgb(0x3A210C), Rgb(0xFFF7E6), Rgb(0xFFB09E), Rgb(0xD9675A),
            Rgb(0xF5B942), Rgb(0xB8740E), Rgb(0xF5C452), Rgb(0x9EEBFF), Rgb(0x7A4A22), Rgb(0xF5B942), Rgb(0x22130A), Rgb(0xFF7A6B), 1.4),
    };

    /// <summary>The theme families the companion knows (an unknown one is drawn as Classic).</summary>
    static readonly HashSet<string> Families = HudThemes.All.Select(t => t.Key).ToHashSet();

    /// <summary>The theme family it is drawn in.</summary>
    public string Theme { get; }
    /// <summary>The Buddy's look it is tinted for, where its family has more than one palette (null: the family's).</summary>
    public string? Look { get; }
    readonly Palette _p;
    readonly Canvas _stage = new() { Width = Side, Height = Side, ClipToBounds = false };
    readonly Canvas _rig = new();
    readonly ScaleTransform _squash = new(1, 1, 0, 35);
    readonly RotateTransform _lean = new(0, 0, 22);
    readonly TranslateTransform _move = new();
    /// <summary>The ears' turn about their roots: up is perked, down is drooped (left mirrored).</summary>
    readonly RotateTransform _earL = new(), _earR = new();
    /// <summary>The ears' length as they swing: shorter as one passes level (it swings toward you), so it stays inside the window.</summary>
    readonly ScaleTransform _earLenL = new(1, 1), _earLenR = new(1, 1);
    /// <summary>The arms' turn about the shoulders: 0 hanging, 90 straight out, 180 straight up (left mirrored).</summary>
    readonly RotateTransform _armL = new(), _armR = new();
    readonly ScaleTransform _armLen = new(1, 1), _armLenR = new(1, 1);
    readonly TranslateTransform _armOutL = new(), _armOutR = new();
    readonly TranslateTransform _gog = new();
    readonly RotateTransform _gogTurn = new(0, 0, -20);
    readonly ScaleTransform _nose = new(1, 1, 0, 0.5);
    /// <summary>Its little curled tail's wag about its root.</summary>
    readonly RotateTransform _tail = new(0, 10, 26);
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
    double _bobY, _earIdle, _earTwL, _earTwR, _armIdle, _auraIdle = .5, _gogIdle, _tailIdle;
    double _reX, _reY, _reSx = 1, _reSy = 1, _reTurn, _reEar, _reEarL, _reEarR, _reArmL, _reArmR, _reAura, _reGog, _reGogTurn, _reTail;
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
            var rink = Frozen(Color.FromArgb(0xB0, _p.Scarf.R, _p.Scarf.G, _p.Scarf.B));
            foreach (var (sx, sy) in new[] { (-1, -1), (1, -1), (-1, 1), (1, 1) })
                _reticle.Children.Add(new Path { Data = Geometry.Parse(Fmt("M {0},{1} L {0},{2} M {0},{1} L {3},{1}", sx * 44, sy * 44, sy * 35, sx * 35)), Stroke = rink, StrokeThickness = 1.4 });
            _reticle.Children.Add(new Ellipse { Width = 3, Height = 3, Fill = rink, RenderTransform = new TranslateTransform(-1.5, -47) });
            Canvas.SetLeft(_reticle, Cx); Canvas.SetTop(_reticle, Cy);
            _reticle.Opacity = .65;
            _stage.Children.Add(_reticle);
        }

        // the shadow on the ground
        _shadow = Disc(36, 7, new RadialGradientBrush(Color.FromArgb(0x88, 0, 0, 0), Color.FromArgb(0, 0, 0, 0)));
        Place(_shadow, Cx, Ry + 35.5);
        _shadow.RenderTransform = _shadowSize;
        _stage.Children.Add(_shadow);

        // the critter and all that moves with it
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
        for (int i = 0; i < 7; i++)
        {
            double ang = -66 + i * 22;   // a crown of flames over the top
            var fl = new Path
            {
                Data = Geometry.Parse(i % 2 == 0 ? "M 0,0 C -7,-6 -5,-14 0,-24 C 1,-17 5,-14 4,-8 C 6,-5 5,-2 0,0 Z" : "M 0,0 C -5,-5 -3,-11 0,-18 C 3,-11 5,-5 0,0 Z"),
                Fill = flameFill,
                RenderTransform = new TransformGroup { Children = { new ScaleTransform(1, 1), new TranslateTransform(0, -13), new RotateTransform(ang) } },
            };
            _flames.Children.Add(fl);
        }
        Canvas.SetTop(_flames, -6);
        _flames.Opacity = 0;
        _rig.Children.Add(_flames);

        // the silhouette (head, body, feet): the sticker's edge, the colour split, the flush and the flash
        var head = Geometry.Parse(HeadPath);
        var body = Geometry.Parse(BodyPath);
        var feet = new GeometryGroup { Children = { new EllipseGeometry(new Point(-7.5, 33), 6, 3.6), new EllipseGeometry(new Point(7.5, 33), 6, 3.6) } };
        var shape = new GeometryGroup { FillRule = FillRule.Nonzero, Children = { head, body, feet } };
        shape.Freeze();
        var earL = new TransformGroup { Children = { _earLenL, new ScaleTransform(-EarK, EarK), _earL, new TranslateTransform(-15.5, -16) } };
        var earR = new TransformGroup { Children = { _earLenR, new ScaleTransform(EarK, EarK), _earR, new TranslateTransform(15.5, -16) } };
        if (Theme == HudThemes.Wacky)
        {
            // a sticker: a hard shadow, then a thick white edge round it all (the ears with it), drawn under every fill
            var hard = Frozen(Color.FromArgb(0x9A, 0x12, 0x04, 0x20));
            _rig.Children.Add(new Path { Data = shape, Fill = hard, RenderTransform = new TranslateTransform(2.6, 3.2) });
            foreach (var t in new[] { earL, earR })
                _rig.Children.Add(new Path { Data = Geometry.Parse(EarPath), Stroke = Brushes.White, StrokeThickness = 5, StrokeLineJoin = PenLineJoin.Round, RenderTransform = t });
            _rig.Children.Add(new Path { Data = shape, Stroke = Brushes.White, StrokeThickness = 5, StrokeLineJoin = PenLineJoin.Round });
        }
        if (Theme == HudThemes.Glitch)
        {
            // the colour split: a red and a cyan copy behind, apart only while it reacts
            _ghostR = new Path { Data = shape, Fill = Frozen(Color.FromArgb(0x90, 0xFF, 0x2A, 0x5A)), RenderTransform = new TranslateTransform(0, 0) };
            _ghostC = new Path { Data = shape, Fill = Frozen(Color.FromArgb(0x90, 0x2A, 0xE8, 0xFF)), RenderTransform = new TranslateTransform(0, 0) };
            _rig.Children.Add(_ghostR); _rig.Children.Add(_ghostC);
        }
        else { _ghostR = new Path(); _ghostC = new Path(); }

        // the ears, behind the head: long and pointed, the inner ear in its own colour
        _rig.Children.Add(Ear(earL, -1));
        _rig.Children.Add(Ear(earR, 1));

        var rim = Frozen(_p.Rim);
        var fur = Fur();
        // a little curled tail peeking out on the right, behind it all
        var tailFill = new LinearGradientBrush(_p.FurLight, _p.Fur, 45); tailFill.Freeze();
        _rig.Children.Add(new Path { Data = Geometry.Parse("M 9,28.5 C 17,30.5 23.5,25 21.5,18.5 C 20.4,15 15.6,15.4 16.2,19 C 16.7,22 14.5,24.6 9.5,23.6 Z"), Fill = tailFill, Stroke = rim, StrokeThickness = _p.RimW, StrokeLineJoin = PenLineJoin.Round, RenderTransform = _tail });
        // the feet, then the body with its belly, the pouch at its hip and the scarf at its neck
        foreach (double fx in new[] { -7.5, 7.5 })
        {
            var foot = new Ellipse { Width = 12, Height = 7.2, Fill = Frozen(_p.FurDeep), Stroke = rim, StrokeThickness = _p.RimW };
            Place(foot, fx, 33);
            _rig.Children.Add(foot);
            var toe = Disc(5, 2.2, Frozen(Color.FromArgb(0x70, _p.FurLight.R, _p.FurLight.G, _p.FurLight.B)));
            Place(toe, fx - .5, 31.6);
            _rig.Children.Add(toe);
        }
        _rig.Children.Add(new Path { Data = body, Fill = fur, Stroke = rim, StrokeThickness = _p.RimW, StrokeLineJoin = PenLineJoin.Round, Tag = "companion-body" });
        var belly = Disc(15, 13.5, Soft(_p.Belly, .95));
        Place(belly, 0, 23.5);
        _rig.Children.Add(belly);
        _rig.Children.Add(Pouch());
        _rig.Children.Add(Scarf());

        // the head: fur lit from the top left, a lighter muzzle round the nose, a rim light behind its right side
        _rig.Children.Add(new Path { Data = head, Fill = fur, Stroke = rim, StrokeThickness = _p.RimW, StrokeLineJoin = PenLineJoin.Round, Tag = "companion-head" });
        var muzzle = Disc(25, 15, Soft(_p.Belly, .92));
        Place(muzzle, 0, 3.5);
        _rig.Children.Add(muzzle);
        _rig.Children.Add(new Path
        {
            Data = Geometry.Parse("M 7,-24.2 C 14.5,-22 20,-15.5 20.2,-6.5 C 20.2,-1 18,3.5 14.5,6.8"), Stroke = Frozen(Color.FromArgb(0xC8, Lighten(_p.Glow, .45).R, Lighten(_p.Glow, .45).G, Lighten(_p.Glow, .45).B)),
            StrokeThickness = 1.5, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round, IsHitTestVisible = false,
        });
        if (Theme == HudThemes.Glitch)
        {
            var lines = new DrawingBrush(new GeometryDrawing(Frozen(Color.FromArgb(0x48, 0x00, 0x24, 0x10)), null, new RectangleGeometry(new Rect(0, 0, 4, 1))))
            { TileMode = TileMode.Tile, Viewport = new Rect(0, 0, 4, 2.6), ViewportUnits = BrushMappingMode.Absolute, Viewbox = new Rect(0, 0, 4, 2.6), ViewboxUnits = BrushMappingMode.Absolute };
            lines.Freeze();
            _rig.Children.Add(new Path { Data = shape, Fill = lines, IsHitTestVisible = false });
        }
        // a warm flush (a streak) or a grey cast (a wipe, a fall) over it
        _tint = new Path { Data = shape, Fill = new SolidColorBrush(Colors.Transparent), Opacity = 0 };
        _rig.Children.Add(_tint);
        _flash = new Path { Data = shape, Fill = new SolidColorBrush(Colors.White), Opacity = 0, IsHitTestVisible = false };
        _rig.Children.Add(_flash);
        // the gloss on its crown
        var shine = Disc(9, 5, new RadialGradientBrush(Color.FromArgb(0xC8, 0xFF, 0xFF, 0xFF), Color.FromArgb(0x00, 0xFF, 0xFF, 0xFF)));
        Place(shine, -13.5, -13);
        shine.RenderTransform = new RotateTransform(-52, 4.5, 2.5);
        _rig.Children.Add(shine);

        // the face: beady glossy eyes, the big round nose, the mouth under it, the cheeks
        var ink = Frozen(_p.Ink);
        Canvas.SetTop(_eyes, -7);
        _eyes.RenderTransform = new TransformGroup { Children = { _eyeScale, _eyeLook } };
        foreach (double ex in new[] { -8, 8 })
        {
            var eye = new Ellipse { Width = 6.4, Height = 8, Fill = ink };
            Canvas.SetLeft(eye, ex - 3.2); Canvas.SetTop(eye, -4);
            var light = Disc(2.8, 2.8, Brushes.White);
            Place(light, ex - 1, -1.6);
            var spark = Disc(1.3, 1.3, Brushes.White);
            Place(spark, ex + 1.3, 1.6);
            _eyes.Children.Add(eye); _eyes.Children.Add(light); _eyes.Children.Add(spark);
            _openEyes.Add(eye); _openEyes.Add(light); _openEyes.Add(spark);
        }
        _rig.Children.Add(_eyes);
        Path Stroked(string data, double w = 2) => new() { Data = Geometry.Parse(data), Stroke = ink, StrokeThickness = w, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round, StrokeLineJoin = PenLineJoin.Round, Visibility = Visibility.Collapsed };
        _happyEyes = Stroked("M -11.5,-5.5 Q -8,-11.5 -4.5,-5.5 M 4.5,-5.5 Q 8,-11.5 11.5,-5.5", 2.3);
        _closedEyes = Stroked("M -11.5,-7 Q -8,-3.5 -4.5,-7 M 4.5,-7 Q 8,-3.5 11.5,-7", 2);
        _xEyes = Stroked("M -10.5,-9.5 L -5.5,-4.5 M -5.5,-9.5 L -10.5,-4.5 M 5.5,-9.5 L 10.5,-4.5 M 10.5,-9.5 L 5.5,-4.5", 2.1);
        _browsFierce = Stroked("M -12.5,-15.5 L -4.5,-12 M 12.5,-15.5 L 4.5,-12", 2.3);
        _browsSad = Stroked("M -12,-12.4 L -4.5,-15.4 M 12,-12.4 L 4.5,-15.4", 1.9);
        _browsFocus = Stroked("M -12,-14.6 L -4.8,-13.2 M 12,-14.6 L 4.8,-13.2", 1.9);
        foreach (var e in new[] { _happyEyes, _closedEyes, _xEyes, _browsFierce, _browsSad, _browsFocus }) _rig.Children.Add(e);
        var cheek = Frozen(Color.FromArgb(0x80, _p.Cheek.R, _p.Cheek.G, _p.Cheek.B));
        _cheekL = Disc(7, 4, cheek); Place(_cheekL, -13.5, 1.5);
        _cheekR = Disc(7, 4, cheek); Place(_cheekR, 13.5, 1.5);
        _rig.Children.Add(_cheekL); _rig.Children.Add(_cheekR);
        _mouth = new Path { Stroke = ink, StrokeThickness = 1.7, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round, StrokeLineJoin = PenLineJoin.Round };
        Canvas.SetTop(_mouth, 6.2);
        _mouthO = new Path { Data = new EllipseGeometry(new Point(0, 0), 1.9, 2.3), Fill = ink };
        Canvas.SetTop(_mouthO, 7.6);
        _tongue = new Path { Data = Geometry.Parse("M -2.6,3 Q 0,0.6 2.6,3 Q 0,5.4 -2.6,3 Z"), Fill = Frozen(Rgb(0xFF6F86)), Visibility = Visibility.Collapsed };
        Canvas.SetTop(_tongue, 6.2);
        _rig.Children.Add(_mouth); _rig.Children.Add(_tongue); _rig.Children.Add(_mouthO);
        _rig.Children.Add(Nose());

        // the goggles up on its forehead (down over its eyes in a fight)
        _rig.Children.Add(Goggles());

        // its paws, in front: they wave, cheer, punch and hang
        _rig.Children.Add(Arm(-1, new TransformGroup { Children = { _armLen, _armL, _armOutL } }));
        _rig.Children.Add(Arm(1, new TransformGroup { Children = { _armLenR, _armR, _armOutR } }));

        // the Kinah coin it tosses on a rare drop
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
            Fill = Frozen(Rgb(0xFFC24A)), Stroke = Frozen(Rgb(0x5A3200)), StrokeThickness = 1.2, Opacity = 0, RenderTransform = new TransformGroup { Children = { new ScaleTransform(1, 1), new RotateTransform(12), new TranslateTransform(0, -27) } },
        };
        _rig.Children.Add(_alert);
        var cloudGeo = new GeometryGroup { FillRule = FillRule.Nonzero, Children = { new EllipseGeometry(new Point(-6, 0), 6.5, 5.5), new EllipseGeometry(new Point(2, -3), 8, 7), new EllipseGeometry(new Point(9, 1), 5.5, 4.6), new RectangleGeometry(new Rect(-10, 0, 23, 5.2), 2.6, 2.6) } };
        _cloud = new Path { Data = cloudGeo, Fill = new LinearGradientBrush(Rgb(0xC9D2E2), Rgb(0x7F8AA0), 90), Stroke = Frozen(Rgb(0x3E4658)), StrokeThickness = 1, Opacity = 0, RenderTransform = new TranslateTransform(0, -36) };
        _rig.Children.Add(_cloud);
        for (int i = 0; i < 4; i++)
            _rain.Children.Add(new Line { X1 = 0, Y1 = 0, X2 = -1.2, Y2 = 4.5, Stroke = Frozen(Rgb(0x8FC2FF)), StrokeThickness = 1.6, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round, RenderTransform = new TranslateTransform(-7 + i * 5, -28) });
        _rain.Opacity = 0;
        _rig.Children.Add(_rain);
        _tear = new Path { Data = Geometry.Parse("M 0,-3.2 C 1.8,-0.6 2.6,0.8 2.6,1.9 A 2.6,2.6 0 1 1 -2.6,1.9 C -2.6,0.8 -1.8,-0.6 0,-3.2 Z"), Fill = Frozen(Rgb(0x9FD4FF)), Stroke = Frozen(Rgb(0x2F6FB0)), StrokeThickness = .8, Opacity = 0, RenderTransform = new TranslateTransform(10.5, -2) };
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
        _slashes.RenderTransform = new TransformGroup { Children = { new ScaleTransform(.8, .8), new RotateTransform(0), new TranslateTransform(0, -31) } };
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

    /// <summary>The head: round, a little wider at the cheeks, bigger than the body (chibi).</summary>
    const string HeadPath = "M 0,-26 C 12.5,-26 21,-18.5 21,-7.5 C 21,3.5 12.5,10.5 0,10.5 C -12.5,10.5 -21,3.5 -21,-7.5 C -21,-18.5 -12.5,-26 0,-26 Z";
    /// <summary>The body: a small bean under the head.</summary>
    const string BodyPath = "M -11.5,7 C -15,14 -15.5,25 -11,30.5 C -6.5,34.5 6.5,34.5 11,30.5 C 15.5,25 15,14 11.5,7 Z";
    /// <summary>An ear pointing right from its root (turned up and mirrored for the left): long, pointed, a soft curl to the tip.</summary>
    const string EarPath = "M 0,-7 C 7,-14 18,-20 30,-21 C 31.6,-21.1 32.2,-19.8 31.2,-18.6 C 24,-10 13,-1 0,6 Z";
    const string EarInPath = "M 3.5,-4.6 C 9.5,-10.4 18,-15.2 27,-17.4 C 21,-11 13,-4.6 4.5,1.8 Z";

    static string Fmt(string f, params double[] v) => string.Format(System.Globalization.CultureInfo.InvariantCulture, f, v.Cast<object>().ToArray());

    /// <summary>A four-pointed star of radius <paramref name="r"/>, its waist pinched.</summary>
    static string StarPath(double r) { double k = r * 0.18; return Fmt("M 0,{0} Q {1},{2} {3},0 Q {1},{1} 0,{3} Q {2},{1} {0},0 Q {2},{2} 0,{0} Z", -r, k, -k, r); }

    /// <summary>The fur, lit from the top left.</summary>
    RadialGradientBrush Fur()
    {
        var f = new RadialGradientBrush { GradientOrigin = new Point(0.32, 0.22), Center = new Point(0.42, 0.34), RadiusX = 0.8, RadiusY = 0.8 };
        f.GradientStops.Add(new GradientStop(_p.FurLight, 0));
        f.GradientStops.Add(new GradientStop(_p.Fur, 0.45));
        f.GradientStops.Add(new GradientStop(_p.FurDeep, 0.98));
        f.Freeze();
        return f;
    }

    /// <summary>A soft-edged patch (the belly, the muzzle).</summary>
    static RadialGradientBrush Soft(Color c, double a)
    {
        var g = new RadialGradientBrush();
        g.GradientStops.Add(new GradientStop(Color.FromArgb((byte)(255 * a), c.R, c.G, c.B), 0));
        g.GradientStops.Add(new GradientStop(Color.FromArgb((byte)(255 * a * .85), c.R, c.G, c.B), 0.62));
        g.GradientStops.Add(new GradientStop(Color.FromArgb(0, c.R, c.G, c.B), 1));
        g.Freeze();
        return g;
    }

    /// <summary>An ear on <paramref name="side"/> (-1 left, 1 right), turned by <paramref name="at"/> about its root at the head's side.</summary>
    Canvas Ear(Transform at, int side)
    {
        var ear = new Canvas { RenderTransform = at };
        var fill = new LinearGradientBrush { StartPoint = new Point(0, 1), EndPoint = new Point(1, 0) };
        fill.GradientStops.Add(new GradientStop(_p.Fur, 0));
        fill.GradientStops.Add(new GradientStop(side < 0 ? _p.FurLight : _p.Fur, 0.55));
        fill.GradientStops.Add(new GradientStop(_p.FurDeep, 1));
        fill.Freeze();
        ear.Children.Add(new Path { Data = Geometry.Parse(EarPath), Fill = fill, Stroke = Frozen(_p.Rim), StrokeThickness = _p.RimW, StrokeLineJoin = PenLineJoin.Round });
        var inner = new LinearGradientBrush(Lighten(_p.EarIn, .25), _p.EarIn, 0); inner.Freeze();
        ear.Children.Add(new Path { Data = Geometry.Parse(EarInPath), Fill = inner });
        // the rim light along the top edge
        ear.Children.Add(new Path { Data = Geometry.Parse("M 3,-7.8 C 9,-13.4 18,-18.6 28.5,-20"), Stroke = Frozen(Color.FromArgb(0x90, Lighten(_p.Glow, .5).R, Lighten(_p.Glow, .5).G, Lighten(_p.Glow, .5).B)), StrokeThickness = 1.1, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round });
        return ear;
    }

    /// <summary>The big round nose, glossy, its sniff a little swell.</summary>
    Canvas Nose()
    {
        var c = new Canvas { RenderTransform = _nose };
        Canvas.SetTop(c, 0.5);
        var fill = new RadialGradientBrush { GradientOrigin = new Point(0.35, 0.25), Center = new Point(0.45, 0.4), RadiusX = .65, RadiusY = .65 };
        fill.GradientStops.Add(new GradientStop(Lighten(_p.Nose, .45), 0));
        fill.GradientStops.Add(new GradientStop(_p.Nose, .5));
        fill.GradientStops.Add(new GradientStop(Darken(_p.Nose, .55), 1));
        fill.Freeze();
        c.Children.Add(new Path { Data = Geometry.Parse("M 0,-4 C 4.5,-4 6.4,-1.6 6.4,0.8 C 6.4,3.6 3.6,4.6 0,4.6 C -3.6,4.6 -6.4,3.6 -6.4,0.8 C -6.4,-1.6 -4.5,-4 0,-4 Z"), Fill = fill, Stroke = Frozen(_p.Rim), StrokeThickness = _p.RimW * .8 });
        var hi = Disc(4.4, 2.4, Frozen(Color.FromArgb(0xE0, 0xFF, 0xFF, 0xFF)));
        Place(hi, -2, -1.9);
        hi.RenderTransform = new RotateTransform(-14, 2.2, 1.2);
        c.Children.Add(hi);
        return c;
    }

    /// <summary>Goggles pushed up on its forehead: a strap round the head, two round lenses with metal rims.</summary>
    Canvas Goggles()
    {
        var g = new Canvas { RenderTransform = new TransformGroup { Children = { _gogTurn, _gog } } };
        var strap = new LinearGradientBrush(_p.Scarf, _p.ScarfDeep, 90); strap.Freeze();
        g.Children.Add(new Path { Data = Geometry.Parse("M -20.6,-12.5 Q 0,-26 20.6,-12.5"), Stroke = Frozen(_p.Rim), StrokeThickness = 5, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round });
        g.Children.Add(new Path { Data = Geometry.Parse("M -20.6,-12.5 Q 0,-26 20.6,-12.5"), Stroke = strap, StrokeThickness = 3.2, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round });
        var metal = new LinearGradientBrush(Lighten(_p.Metal, .5), Darken(_p.Metal, .7), 90); metal.Freeze();
        var glass = new RadialGradientBrush { GradientOrigin = new Point(.3, .25) };
        glass.GradientStops.Add(new GradientStop(Color.FromArgb(0xE8, Lighten(_p.Lens, .7).R, Lighten(_p.Lens, .7).G, Lighten(_p.Lens, .7).B), 0));
        glass.GradientStops.Add(new GradientStop(Color.FromArgb(0x90, _p.Lens.R, _p.Lens.G, _p.Lens.B), .55));
        glass.GradientStops.Add(new GradientStop(Color.FromArgb(0xB0, Darken(_p.Lens, .6).R, Darken(_p.Lens, .6).G, Darken(_p.Lens, .6).B), 1));
        glass.Freeze();
        g.Children.Add(new Path { Data = Geometry.Parse("M -3,-21.5 Q 0,-23 3,-21.5"), Stroke = Frozen(_p.Rim), StrokeThickness = 2.6, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round });
        foreach (double lx in new[] { -7.2, 7.2 })
        {
            var rimE = new Ellipse { Width = 12.4, Height = 11.4, Fill = metal, Stroke = Frozen(_p.Rim), StrokeThickness = _p.RimW * .9 };
            Place(rimE, lx, -20.5);
            var lens = new Ellipse { Width = 8.4, Height = 7.6, Fill = glass, Stroke = Frozen(Color.FromArgb(0xA0, _p.Rim.R, _p.Rim.G, _p.Rim.B)), StrokeThickness = .7 };
            Place(lens, lx, -20.5);
            var hi = new Path { Data = Geometry.Parse(Fmt("M {0},{1} Q {2},{3} {4},{5}", lx - 2.6, -20.2, lx - 2.2, -23.2, lx + .6, -23.4)), Stroke = Brushes.White, StrokeThickness = 1.3, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round, Opacity = .9 };
            g.Children.Add(rimE); g.Children.Add(lens); g.Children.Add(hi);
        }
        return g;
    }

    /// <summary>The scarf at its neck, its knot and tail on the right.</summary>
    Canvas Scarf()
    {
        var c = new Canvas();
        var fill = new LinearGradientBrush(Lighten(_p.Scarf, .3), _p.ScarfDeep, 90); fill.Freeze();
        var rim = Frozen(_p.Rim);
        c.Children.Add(new Path { Data = Geometry.Parse("M 8,10 C 10.5,14 12.5,18 15.5,20.5 L 11.5,21.4 C 10,19 8.6,16.5 6.2,12.6 Z"), Fill = fill, Stroke = rim, StrokeThickness = _p.RimW * .85, StrokeLineJoin = PenLineJoin.Round });
        c.Children.Add(new Path { Data = Geometry.Parse("M -14,7.2 Q 0,14.2 14,7.2 L 13.4,11.8 Q 0,18.8 -13.4,11.8 Z"), Fill = fill, Stroke = rim, StrokeThickness = _p.RimW * .85, StrokeLineJoin = PenLineJoin.Round });
        var knot = new Ellipse { Width = 5.6, Height = 5, Fill = fill, Stroke = rim, StrokeThickness = _p.RimW * .85 };
        Place(knot, 8.2, 12.4);
        c.Children.Add(knot);
        c.Children.Add(new Path { Data = Geometry.Parse("M -11,9.6 Q 0,15 11,9.6"), Stroke = Frozen(Color.FromArgb(0x80, 0xFF, 0xFF, 0xFF)), StrokeThickness = 1, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round });
        return c;
    }

    /// <summary>The coin pouch at its left hip: a leather sack, its tie, a coin peeking out.</summary>
    Canvas Pouch()
    {
        var c = new Canvas();
        Canvas.SetLeft(c, -11.5); Canvas.SetTop(c, 24);
        var rim = Frozen(_p.Rim);
        var leather = new LinearGradientBrush(Lighten(_p.Leather, .3), Darken(_p.Leather, .7), 90); leather.Freeze();
        var peek = new Ellipse { Width = 6, Height = 3.4, Fill = Frozen(Rgb(0xFFC93C)), Stroke = Frozen(Rgb(0x6B3E06)), StrokeThickness = .8 };
        Place(peek, 0.6, -4.2);
        c.Children.Add(peek);
        c.Children.Add(new Path { Data = Geometry.Parse("M -3.4,-3.6 C -6.8,-1 -7,4.6 -3.4,6 C -1.2,6.8 1.2,6.8 3.4,6 C 7,4.6 6.8,-1 3.4,-3.6 Z"), Fill = leather, Stroke = rim, StrokeThickness = _p.RimW * .85, StrokeLineJoin = PenLineJoin.Round });
        c.Children.Add(new Path { Data = Geometry.Parse("M -4,-3.4 L 4,-3.4"), Stroke = Frozen(_p.Scarf), StrokeThickness = 1.6, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round });
        var stud = Disc(2.4, 2.4, Frozen(_p.Metal)); Place(stud, 0, 1.6);
        c.Children.Add(stud);
        return c;
    }

    /// <summary>An arm on <paramref name="side"/>: a stubby limb from the shoulder ending in a round paw, turned by <paramref name="turn"/>.</summary>
    Canvas Arm(int side, Transform turn)
    {
        var a = new Canvas { RenderTransform = turn };
        Canvas.SetLeft(a, side * 11); Canvas.SetTop(a, 12.5);
        double ax = side * 3.4, ay = 10.6;
        a.Children.Add(new Line { X1 = 0, Y1 = 0, X2 = ax, Y2 = ay, Stroke = Frozen(_p.Rim), StrokeThickness = 6.2 + 2 * _p.RimW, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round });
        var limb = new LinearGradientBrush(_p.Fur, _p.FurDeep, 0); limb.Freeze();
        a.Children.Add(new Line { X1 = 0, Y1 = 0, X2 = ax, Y2 = ay, Stroke = limb, StrokeThickness = 6.2, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round });
        var paw = new Ellipse { Width = 7.8, Height = 7.4, Fill = Frozen(_p.FurLight), Stroke = Frozen(_p.Rim), StrokeThickness = _p.RimW };
        Place(paw, ax + side * .4, ay + .6);
        a.Children.Add(paw);
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
        _eyeScale.ScaleX = _eyeScale.ScaleY = f == Face.Alert ? 1.25 : f == Face.Excited ? 1.14 : 1;
        _mouthO.Visibility = f is Face.Alert or Face.Sleepy ? Visibility.Visible : Visibility.Collapsed;
        _mouthO.RenderTransform = f == Face.Sleepy ? new ScaleTransform(.7, .6) : Transform.Identity;
        _tongue.Visibility = f is Face.Excited or Face.Happy ? Visibility.Visible : Visibility.Collapsed;
        string mouth = f switch
        {
            Face.Happy or Face.Excited => "M -5,0.4 Q 0,8.4 5,0.4 Z",
            Face.Proud => "M -4.6,0.6 Q -2.3,3.6 0,1.4 Q 2.3,3.6 4.6,0.6",
            Face.Fired => "M -4.8,0.4 Q 0,6.4 4.8,0.4 Z",
            Face.Fierce => "M -4.4,1.6 L 4.4,1.6 M -2.2,1.6 L -2.2,3.4 M 0,1.6 L 0,3.4 M 2.2,1.6 L 2.2,3.4 M -4.4,3.4 L 4.4,3.4",
            Face.Focused => "M -3,1.6 L 3,1.4",
            Face.Sad => "M -4,3.6 Q 0,-0.4 4,3.6",
            Face.Dazed => "M -4.2,2 Q -2.1,0 0,2 Q 2.1,4 4.2,2",
            Face.Alert or Face.Sleepy => "",
            _ => "M -3.8,0.8 Q -1.9,3.4 0,1.2 Q 1.9,3.4 3.8,0.8",
        };
        _mouth.Data = mouth.Length > 0 ? Geometry.Parse(mouth) : null;
        _mouth.Fill = f is Face.Happy or Face.Excited or Face.Fired ? Frozen(Darken(_p.Nose, .35)) : null;
    }

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

    /// <summary>The mood at rest: idle, goggles down for a fight, dozing after a long quiet.</summary>
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
    /// <summary>Its fur and gear (the tests: each theme family and tinted look its own).</summary>
    internal string TintForTest => _p.Fur + "|" + _p.Scarf;

    void Idle(double p)
    {
        double w = 2 * Math.PI * p, s = Math.Sin(Math.PI * p);
        Expression(p);
        _earTwL = _earTwR = 0; _tailIdle = 0;
        _nose.ScaleX = _nose.ScaleY = 1;
        switch (_mood)
        {
            case Mood.Focused:
                // goggles down, ears perked, paws up and ready, bouncing on its toes
                _bobY = -1.6 * Math.Abs(Math.Sin(w * 2)); _earIdle = -10 + 3 * Math.Sin(w * 2); _armIdle = 40 + 8 * Math.Sin(w * 2); _auraIdle = .62; _gogIdle = 1;
                _zs.Opacity = 0;
                break;
            case Mood.Sleepy:
                // ears drooped, nodding, z's rising
                _bobY = 1.2 * Math.Sin(w) + 1; _earIdle = 56 + 4 * Math.Sin(w); _armIdle = -4; _auraIdle = .3; _gogIdle = 0;
                ZsAt(p);
                break;
            default:
                _bobY = -1.4 * s * s; _earIdle = 3 * Math.Sin(w); _armIdle = 6 * s; _auraIdle = .5; _gogIdle = 0;
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
            at.X = 13 + 7 * q + i * 2; at.Y = -27 - 13 * q;
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
    /// Plays a reaction: a Kinah coin tossed high for a rare drop (sparks in its grade's colour), a crown and rays for a new
    /// best, a cheer and confetti for a boss down, flames and pumping fists for a streak, ears shot up and an alert mark for a
    /// timer, goggles down and crossed blades for a takedown, drooped ears and a rain cloud for a wipe, dizzy stars and
    /// flopped ears for a fall. Then back to its mood. A reaction under way gives way to the new one.
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
                // it digs a Kinah coin out of its pouch and flips it high, paws up, ears perked
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
                // the crown lands on its goggles, it puffs up, one paw punching the air
                Hop(t, 0, 5);
                Flash(t, 600, Rgb(0xFFF4C8));
                double power = Bump(t, 580, 860);
                _reSx *= 1 + .08 * power; _reSy *= 1 + .08 * power;
                double drop = Win(t, 400, 720);
                ((TranslateTransform)_crown.RenderTransform).Y = -29 - 8 * (1 - Back(drop));
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
                Hop(t, 0, 7); Hop(t, 620, 6);
                Flash(t, 0, Colors.White);
                Confetti(t, 330);
                double wave = 14 * Math.Sin(2 * Math.PI * t / 260);
                _reArmL = (118 + wave) * Motion.Out(Win(t, 0, 180)) * settle; _reArmR = (118 - wave) * Motion.Out(Win(t, 0, 180)) * settle;
                _reEar = 9 * Math.Abs(Math.Sin(2 * Math.PI * t / 300)) * settle;   // floppy with joy
                _reTail = 20 * Math.Sin(2 * Math.PI * t / 180) * settle;
                _reAura = .45 * Bump(t, 200, 1500);
                break;
            case CompanionKind.Streak:
                // fired up: flames round its head, goggles down, fists pumping, ears swept back
                _flames.Opacity = Win(t, 0, 200) * fadeOut;
                for (int i = 0; i < _flames.Children.Count; i++)
                {
                    var g = (TransformGroup)_flames.Children[i].RenderTransform;
                    var s = (ScaleTransform)g.Children[0];
                    double f = Math.Abs(Math.Sin(2 * Math.PI * (t / 260 + i * 0.27)));
                    s.ScaleY = .7 + .45 * f; s.ScaleX = .9 + .25 * (1 - f);
                }
                _tint.Opacity = .22 * Win(t, 0, 240) * fadeOut;
                Embers(t);
                _reX = 1.1 * Math.Sin(2 * Math.PI * t / 60) * (1 - Win(t, 0, 700));
                Flash(t, 0, Rgb(0xFFD08A));
                _reGog = GogDown * Motion.Out(Win(t, 0, 140)) * settle;
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
                // goggles slammed down, a lunge, blades crossed over its head, fists out
                _reX = t < 170 ? 3 * Motion.Out(t / 170) : 3 * (1 - Motion.Out(Win(t, 170, 620)));
                _reSx = 1 + .05 * Bump(t, 0, 300); _reSy = 1 - .12 * Bump(t, 0, 300);
                _reY = 1.4 * Math.Sin(2 * Math.PI * t / 50) * Bump(t, 120, 520);   // the clash shakes it
                Flash(t, 120, Rgb(0xFFD0D0));
                Ring(_ring, t, 140, 480, 46, 88);
                _reGog = GogDown * Motion.Out(Win(t, 0, 110)) * settle;
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
                _tint.Opacity = .45 * sink;
                double cloud = Win(t, 100, 480);
                ((TranslateTransform)_cloud.RenderTransform).Y = -42 + 8 * Back(cloud) - 4 * sink;
                _cloud.Opacity = Math.Min(1, cloud * 3) * fadeOut;
                _rain.Opacity = Win(t, 420, 560) * fadeOut;
                for (int i = 0; i < _rain.Children.Count; i++)
                {
                    double q = (t / 420 + i * 0.31) % 1;
                    var r = (TranslateTransform)_rain.Children[i].RenderTransform;
                    r.Y = -30 + 16 * q - 4 * sink; r.X = -7 + i * 5 - 1.5 * q;
                    _rain.Children[i].Opacity = Math.Sin(Math.PI * q);
                }
                double fall = Win(t, 700, 1500);
                ((TranslateTransform)_tear.RenderTransform).Y = -2 + 12 * fall * fall;
                _tear.Opacity = Bump(t, 600, 1600);
                _reAura = -.3 * sink;
                break;
            case CompanionKind.Death:
                // knocked silly: it tips over, its goggles askew, ears flopped every which way, stars round its head
                double tip = Motion.Out(Win(t, 0, 420)) * fadeOut;
                _reTurn = -5 * tip + 2.5 * Math.Sin(2 * Math.PI * t / 520) * tip; _reY = 3 * tip; _reSy = 1 - .06 * tip;
                _reEar = 52 * tip; _reEarL = 14 * tip; _reEarR = -12 * tip;
                _reArmL = 24 * tip; _reArmR = -6 * tip;
                _reGog = 3 * tip; _reGogTurn = 16 * tip;
                Flash(t, 0, Rgb(0xFF8A8A));
                _tint.Opacity = .34 * tip;
                _dizzy.Opacity = Win(t, 200, 420) * fadeOut;
                for (int i = 0; i < _dizzy.Children.Count; i++)
                {
                    double a = t / 520 * 2 * Math.PI + i * 2 * Math.PI / 3;
                    var d = (TranslateTransform)_dizzy.Children[i].RenderTransform;
                    d.X = 16 * Math.Cos(a); d.Y = -30 + 4.5 * Math.Sin(a);
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

    /// <summary>The coin's flight on a rare drop: out of the pouch, up over its head spinning, held there shining, then gone.</summary>
    void Coin(double t)
    {
        if (t < 80) return;
        double up = Motion.Out(Win(t, 80, 440));
        double x0 = -11, y0 = 20, x1 = 1, y1 = -27;
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
        _reEar = _reEarL = _reEarR = _reArmL = _reArmR = _reGog = _reGogTurn = _reTail = 0;
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

    /// <summary>The idle and the reaction added up into its transforms: body, ears, paws, goggles, glow and shadow.</summary>
    void Compose()
    {
        double y = _bobY + _reY;
        _move.X = _reX; _move.Y = y;
        _squash.ScaleX = _reSx; _squash.ScaleY = _reSy;
        _lean.Angle = _reTurn + (_mood == Mood.Focused && _playing == null ? -4 : 0);
        // an ear's turn: its rest tilt up, then the idle's and the reaction's droop (positive down), each ear its own twitch
        double droop = _earIdle + _reEar;
        double dR = Math.Clamp(droop + _earTwR + _reEarR, -48, 75), dL = Math.Clamp(droop + _earTwL + _reEarL, -48, 75);
        _earR.Angle = dR; _earL.Angle = -dL;
        _earLenR.ScaleX = EarLength(dR); _earLenL.ScaleX = EarLength(dL);
        bool rest = _playing == null;
        double aR = rest ? _armIdle : _reArmR, aL = rest ? _armIdle : _reArmL;
        _armR.Angle = -aR; _armL.Angle = aL;
        Reach(aL, _armLen, _armOutL, -1); Reach(aR, _armLenR, _armOutR, 1);
        _gog.Y = rest ? GogDown * _gogIdle : _reGog;
        _gogTurn.Angle = _reGogTurn;
        _tail.Angle = rest ? _tailIdle : _reTail;
        _aura.Opacity = Math.Clamp(_auraIdle + Math.Min(0, _reAura), 0, 1);
        // the higher it hops, the smaller and fainter its shadow (the idle's bounce leaves it be: one thing less to draw)
        double lift = Math.Clamp(-_reY / 16, -0.4, 1);
        double k = 1 - .35 * lift;
        _shadowSize.ScaleX = _shadowSize.ScaleY = k;
        _shadow.Opacity = .9 - .5 * lift;
    }

    /// <summary>An ear's length at a droop of <paramref name="droop"/> degrees (it rests 32 degrees up): shortest as it passes level.</summary>
    static double EarLength(double droop) { double f = (droop - 32) / 30; return 1 - .26 * Math.Exp(-f * f); }

    /// <summary>An arm lifted <paramref name="raise"/> degrees: its shoulder out and up and the arm longer, so a paw in the air clears the head.</summary>
    static void Reach(double raise, ScaleTransform len, TranslateTransform at, int side)
    {
        double k = Math.Clamp(raise / 130, 0, 1);
        len.ScaleX = len.ScaleY = 1 + .24 * k;
        at.X = side * 5 * k; at.Y = -2.5 * k;
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
