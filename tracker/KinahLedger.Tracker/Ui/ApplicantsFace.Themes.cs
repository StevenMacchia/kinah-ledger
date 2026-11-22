using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using KinahLedger.Session;

namespace KinahLedger.Ui;

/// <summary>
/// The Applicants window in the four later theme families (Steven, 2026-11-21: "do the waitlist for expedition invites also
/// match themes" ... "Yes, build them"), each look of a family in its own Buddy's plate (the look's own drawing, called from
/// here) and its own inks, with the family's header: Atreia a titled plate and a gem of a count (Rift's torn violet glass,
/// Elyos's gilded navy under its crest, Asmodian's horned iron, Prism's cut crystal); Esports a broadcast tab and a score box
/// (Broadcast's lower third, the Telestrator's board and highlighter, Hype's lime slab, the Finals' gold under the lamps);
/// Elements a plate of the element (Frost's ice with its crest and icicles, Storm's cloud, Magma's basalt dripping lava,
/// Tide's sea under its breaking wave); Overdrive a fighting game's slab and a burst (Versus's hot stripe, Manga's speech
/// bubble on paper, Dragon's coiled dragon, Aura's power-up flames). The rows are the same in every look (ApplicantsFace.Rows).
/// </summary>
static partial class ApplicantsFace
{
    /// <summary>
    /// A look's skin: its plate (drawn once at the face's size), the room its ornaments take round the plate (crest, icicles,
    /// drips: inside the face, so nothing spills), the plate's padding round the words, the rows' inks, the header, the newest
    /// row's ground and the hairline between rows, and the colours its arrival beat is made of.
    /// </summary>
    sealed record Skin(string Family, string Look, Action<DrawingContext, Size> Plate, Thickness Room, Thickness Pad, RowInk Ink,
        Func<int, FrameworkElement> Head, Func<int, bool, Brush?> Ground, Brush? Rule, double RowPad, CornerRadius? RowCorner,
        Color Accent, Color Hot, Color Deep);

    static Color C(uint rgb) => Color.FromRgb((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb);
    static SolidColorBrush B(uint rgb, double a = 1) => Mod.Br(C(rgb), a);
    static string Count(int n, string fmt = "0") => n.ToString(fmt, System.Globalization.CultureInfo.InvariantCulture);

    static readonly FontFamily Serif = new("Palatino Linotype, Constantia, Georgia, Cambria");

    /// <summary>The look's family's face for <paramref name="look"/> (a look of <paramref name="theme"/>; any other: the family's first look).</summary>
    static Skin? SkinOf(string theme, string? look, double opacity)
    {
        var looks = HudThemes.Looks(theme).Select(l => l.Key).ToList();
        if (looks.Count == 0) return null;
        string key = look != null && looks.Contains(look) ? look : looks[0];
        double op = Math.Clamp(opacity, 0.3, 1);
        return (theme, key) switch
        {
            (HudThemes.Atreia, _) => Atreia(key, op),
            (HudThemes.Esports, _) => Esports(key, op),
            (HudThemes.Elements, _) => Elements(key, op),
            (HudThemes.Overdrive, _) => Overdrive(key, op),
            _ => null,
        };
    }

    /// <summary>The face in a skin: the plate under the header, the older line and the rows; a layer over it all for the arrival.</summary>
    static FrameworkElement Skinned(Skin k, IReadOnlyList<PartyApplicants.Applicant> rows, int older, int total, DateTime now)
    {
        var body = new StackPanel { Margin = k.Pad };
        body.Children.Add(k.Head(total));
        if (Older(older, k.Ink) is { } o) body.Children.Add(o);
        body.Children.Add(Rows(rows, now, k.Ink, k.Ground, k.Rule, k.RowPad, k.RowCorner));
        var art = new TacSketch((dc, s) => { if (s.Width > 24 && s.Height > 24) k.Plate(dc, s); });
        var plate = new Grid { Margin = k.Room, Children = { art, body } };
        var fx = new Canvas { IsHitTestVisible = false };
        var root = new Grid { HorizontalAlignment = HorizontalAlignment.Left, Children = { plate, fx } };
        Parts.AddOrUpdate(root, new FaceParts(k, plate, fx));
        return root;
    }

    /// <summary>A header: the title at the left, the count at the right, an optional rule under them.</summary>
    static FrameworkElement Header(FrameworkElement title, FrameworkElement count, Brush? rule = null, double gap = 6)
    {
        var g = new Grid { Margin = new Thickness(0, 0, 0, gap) };
        g.ColumnDefinitions.Add(new ColumnDefinition()); g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        title.VerticalAlignment = VerticalAlignment.Center;
        count.VerticalAlignment = VerticalAlignment.Center; count.Margin = new Thickness(14, 0, 0, 0);
        Grid.SetColumn(count, 1);
        g.Children.Add(title); g.Children.Add(count);
        if (rule == null) return g;
        var line = new Border { Height = 1, Background = rule, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 0, -4), IsHitTestVisible = false };
        Grid.SetColumnSpan(line, 2); g.Children.Add(line);
        g.Margin = new Thickness(0, 0, 0, gap + 4);
        return g;
    }

    static Border Chip(string s, Brush bg, Brush? edge, Brush fg, FontFamily face, double size = 11, double radius = 4, FontWeight? w = null, Thickness? pad = null) => new()
    {
        Background = bg, BorderBrush = edge, BorderThickness = new Thickness(edge == null ? 0 : 1), CornerRadius = new CornerRadius(radius),
        Padding = pad ?? new Thickness(6, 0, 6, 0), Child = Text(s, size, fg, face, w ?? FontWeights.Bold),
    };

    /// <summary>A rule fading out to the right in <paramref name="c"/>.</summary>
    static Brush Fade(Color c, double a = .6) => Mod.Grad(false, (Mod.A(c, a), 0), (Mod.A(c, a * .35), .6), (Mod.A(c, 0), 1));

    /* ---------------- Atreia: the world's plates ---------------- */

    static Skin Atreia(string look, double op)
    {
        var face = Mod.Face;
        switch (look)
        {
            case HudThemes.Elyos:
            {
                // gilded navy under the sun crest: a serif title in gold, the count a gold gem, ivory figures
                var ink = new RowInk(face, 12, B(0xFFF8EA), B(0xDCB462, .9), B(0xFFF8EA, .62), B(0xF7DE96), FontWeights.SemiBold, FontWeights.SemiBold, Icon: 18);
                return new Skin(HudThemes.Atreia, look, (dc, s) => HudElyos.PlateArt(dc, s, op, crest: true),
                    new Thickness(1, 22, 1, 4), new Thickness(13, 11, 14, 11), ink,
                    n => Header(Text(Title, 13.5, B(0xF7DE96), Serif, FontWeights.SemiBold),
                        Chip(Count(n), Mod.Grad(true, (C(0xF7DE96), 0), (C(0xB5862F), 1)), B(0xFFF8EA, .7), B(0x0A1230), HudThemes.Bahn), Fade(C(0xDCB462), .7)),
                    (i, newest) => newest ? B(0xDCB462, .16) : null, B(0xDCB462, .14), 3.5, new CornerRadius(3),
                    C(0xF7DE96), C(0xFFF8EA), C(0x13224A));
            }
            case HudThemes.Asmodian:
            {
                // horned iron: bone-white caps, the count a crimson seal, ember for the newest
                var ink = new RowInk(face, 12, B(0xF6EEEE), B(0xFF8A3D, .85), B(0xF0DDDE, .72), B(0xFFD08A), FontWeights.SemiBold, FontWeights.SemiBold, Icon: 18);
                var room = HudAsmodian.LineRoom;
                return new Skin(HudThemes.Atreia, look, (dc, s) => HudAsmodian.WarPlate(dc, s, op, room, 9, 30, crest: true),
                    new Thickness(0), new Thickness(room.Left + 9, room.Top + 6, room.Right + 12, room.Bottom + 6), ink,
                    n => Header(Text(Title.ToUpperInvariant(), 12, B(0xF6EEEE), HudThemes.Bahn, FontWeights.Bold),
                        Chip(Count(n), Mod.Grad(true, (C(0xFF4A3D), 0), (C(0x7A0C16), 1)), B(0xFF8A3D, .8), B(0xF6EEEE), HudThemes.Bahn, radius: 2), Fade(C(0xC21F2E), .8)),
                    (i, newest) => newest ? Mod.Grad(false, (Mod.A(C(0xC21F2E), .32), 0), (Mod.A(C(0xC21F2E), .08), 1)) : null, B(0x4A2026, .9), 3.5, new CornerRadius(2),
                    C(0xFF4A3D), C(0xFFD08A), C(0x170C0E));
            }
            case HudThemes.Prism:
            {
                // cut crystal: white words, the count rimmed in the spectrum, mint for the newest
                var spec = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 0) };
                foreach (var (c, at) in new[] { (0xFF4F64u, 0.0), (0xFF9B3Du, .2), (0xFFE54Fu, .4), (0x5BF08Eu, .6), (0x4FAEFFu, .8), (0xAE78FFu, 1.0) }) spec.GradientStops.Add(new GradientStop(C(c), at));
                spec.Freeze();
                var specFaint = spec.Clone(); specFaint.Opacity = .5; specFaint.Freeze();
                var ink = new RowInk(face, 12, Mod.Br(Colors.White, .95), Mod.Br(Colors.White, .5), Mod.Br(Colors.White, .62), B(0x5DF2A6), FontWeights.SemiBold, FontWeights.SemiBold, Icon: 18);
                return new Skin(HudThemes.Atreia, look, (dc, s) => HudPrism.Crystal(dc, s, op, -1, Colors.White),
                    new Thickness(0, 5, 0, 0), new Thickness(12, 10, 15, 10), ink,
                    n => Header(Text(Title, 12.5, Mod.Br(Colors.White, .9), face, FontWeights.SemiBold),
                        Chip(Count(n), Mod.Br(Colors.White, .08), spec, Mod.Br(Colors.White), face, radius: 3), specFaint),
                    (i, newest) => newest ? Mod.Grad(false, (Mod.A(C(0x4FAEFF), .16), 0), (Mod.A(C(0xAE78FF), .12), .5), (Mod.A(C(0xFF4F64), .04), 1)) : null, Mod.Br(Colors.White, .08), 3.5, new CornerRadius(3),
                    C(0xBFE4FF), Colors.White, C(0x17191F));
            }
            default:
            {
                // Rift: the Abyss torn open, violet glass and starlight, the count a violet shard, cyan for the newest
                var ink = new RowInk(face, 12, B(0xF7F4FF), B(0xC2A8FF, .8), B(0xD8CCFF, .66), B(0x9CF2FF), FontWeights.SemiBold, FontWeights.SemiBold, Icon: 18);
                return new Skin(HudThemes.Atreia, HudThemes.Rift, (dc, s) => HudRift.RiftPanel(dc, s, 12, op, wound: true),
                    new Thickness(6, 6, 5, 6), new Thickness(12, 17, 14, 10), ink,
                    n => Header(Text(Title, 12.5, B(0xEEE8FF, .92), face, FontWeights.SemiBold),
                        Chip(Count(n), B(0x8E5CFF, .3), B(0xC2A8FF, .7), B(0xF7F4FF), face, radius: 3)),
                    (i, newest) => newest ? Mod.Grad(false, (Mod.A(C(0x3A1F7A), .66), 0), (Mod.A(C(0x0D0716), .48), 1)) : B(0x0D0716, .42), B(0xC2A8FF, .12), 3.5, new CornerRadius(4),
                    C(0x8E5CFF), C(0x9CF2FF), C(0x170A2C));
            }
        }
    }

    /* ---------------- Esports: the stream's package ---------------- */

    /// <summary>A slanted broadcast tab: <paramref name="child"/> on a lean of <paramref name="fill"/>.</summary>
    static Grid Tab(FrameworkElement child, Brush fill, double lean = 6, Thickness? pad = null, Brush? edge = null)
    {
        var art = new TacSketch((dc, s) => dc.DrawGeometry(fill, edge == null ? null : Mod.P(edge, 1), Tac.Lean(new Rect(.5, .5, Math.Max(1, s.Width - 1), Math.Max(1, s.Height - 1)), lean)));
        child.Margin = pad ?? new Thickness(lean + 4, 1, lean + 5, 1);
        child.VerticalAlignment = VerticalAlignment.Center;
        return new Grid { Children = { art, child }, HorizontalAlignment = HorizontalAlignment.Left };
    }

    static Skin Esports(string look, double op)
    {
        var bahn = HudThemes.Bahn;
        switch (look)
        {
            case HudThemes.Telestrator:
            {
                // the analyst's board: white words, the newest row swiped with the yellow highlighter, the count circled in it
                var ink = new RowInk(Mod.Face, 12, Mod.Br(Colors.White, .95), B(0xFFE13B, .8), Mod.Br(Colors.White, .6), B(0xFFE13B), FontWeights.SemiBold, FontWeights.SemiBold, Icon: 18);
                return new Skin(HudThemes.Esports, look, (dc, s) => HudTelestrator.Board(dc, s, 5, op),
                    new Thickness(2, 0, 2, 5), new Thickness(12, 10, 14, 10), ink,
                    n =>
                    {
                        var title = new StackPanel { HorizontalAlignment = HorizontalAlignment.Left, Children = { Text(Title, 12.5, Mod.Br(Colors.White, .92), Mod.Face, FontWeights.SemiBold),
                            new Border { Height = 2.4, Background = B(0xFFE13B, .85), CornerRadius = new CornerRadius(1.2), Margin = new Thickness(-1, 1, 0, 0), HorizontalAlignment = HorizontalAlignment.Stretch } } };
                        return Header(title, Chip(Count(n), B(0xFFE13B), null, B(0x14120A), Mod.Face, radius: 9, pad: new Thickness(7, 0, 7, 0)));
                    },
                    (i, newest) => newest ? B(0xFFE13B, .2) : B(0x0B0D11, .55), Mod.Br(Colors.White, .08), 3.5, new CornerRadius(2),
                    C(0xFFE13B), C(0xFF47B0), C(0x171A20));
            }
            case HudThemes.Hype:
            {
                // the sponsor's slab: italic lime caps on black, the count a magenta block
                var ink = new RowInk(bahn, 12.5, Mod.Br(Colors.White, .96), B(0xE2FF7A, .85), Mod.Br(Colors.White, .62), B(0xC6FF00), FontWeights.Bold, FontWeights.Bold, Icon: 18, Italic: true);
                return new Skin(HudThemes.Esports, look, (dc, s) => HudHype.Plate(dc, s, op, C(0xC6FF00), s.Height, 9, tail: true),
                    new Thickness(0, 0, 30, 0), new Thickness(15, 9, 25, 13), ink,
                    n => Header(Tab(Text(Title.ToUpperInvariant(), 12, B(0x08090B), bahn, FontWeights.Bold).Italic(), B(0xC6FF00), 5),
                        Tab(Text(Count(n), 13, Mod.Br(Colors.White), bahn, FontWeights.Bold).Italic(), B(0xFF2BD6), 5)),
                    (i, newest) => newest ? Mod.Grad(false, (Mod.A(C(0xC6FF00), .24), 0), (Mod.A(C(0xC6FF00), .04), 1)) : null, B(0x232830, .9), 3.5, null,
                    C(0xC6FF00), C(0xFF2BD6), C(0x050608));
            }
            case HudThemes.Finals:
            {
                // the grand final: black and gold under the lamps, a spaced gold title, the count a gold plate
                var ink = new RowInk(bahn, 12.5, B(0xFFF1C8), B(0xE3B54E, .85), Mod.Br(Colors.White, .6), B(0xFFE18E), FontWeights.SemiBold, FontWeights.SemiBold, Icon: 18);
                return new Skin(HudThemes.Esports, look, (dc, s) => HudFinals.Plate(dc, s, 4, op, lamps: 3, beams: true),
                    new Thickness(3, 2, 3, 7), new Thickness(12, 13, 15, 10), ink,
                    n => Header(Text(Title.ToUpperInvariant(), 12.5, B(0xFFE18E), bahn, FontWeights.SemiBold),
                        Chip(Count(n), Mod.Grad(true, (C(0xFFE18E), 0), (C(0xE3B54E), .5), (C(0x9A7026), 1)), B(0xFFF1C8, .6), B(0x131315), bahn, 12, 2), Fade(C(0xE3B54E), .8)),
                    (i, newest) => newest ? Mod.Grad(false, (Mod.A(C(0xE3B54E), .26), 0), (Mod.A(C(0xE3B54E), .05), 1)) : null, B(0xE3B54E, .14), 3.5, null,
                    C(0xE3B54E), C(0xFFF1C8), C(0x131315));
            }
            default:
            {
                // Broadcast: the lower third, a cyan tab, the count in the stream's red
                var ink = new RowInk(bahn, 12.5, Mod.Br(Colors.White, .96), B(0x1CE3FF, .8), Mod.Br(Colors.White, .62), B(0x1CE3FF), FontWeights.SemiBold, FontWeights.SemiBold, Icon: 18);
                return new Skin(HudThemes.Esports, HudThemes.Broadcast, (dc, s) => HudBroadcast.LinePlateArt(dc, s, false, true),
                    new Thickness(0, 4, 6, 3), new Thickness(16, 8, 18, 10), ink,
                    n => Header(Tab(Text(Title.ToUpperInvariant(), 12, B(0x060A16), bahn, FontWeights.Bold), B(0x1CE3FF), 5),
                        Tab(Text(Count(n, "00"), 13, Mod.Br(Colors.White), bahn, FontWeights.Bold), B(0xFF1F45), 5)),
                    (i, newest) => newest ? Mod.Grad(false, (Mod.A(C(0x1CE3FF), .24), 0), (Mod.A(C(0x1CE3FF), .04), 1)) : null, Mod.Br(Colors.White, .08), 3.5, null,
                    C(0x1CE3FF), C(0xFF1F45), C(0x060A16));
            }
        }
    }

    /* ---------------- Elements: the element's plate ---------------- */

    static Skin Elements(string look, double op)
    {
        var face = Mod.Face;
        switch (look)
        {
            case HudThemes.Storm:
            {
                var ink = new RowInk(face, 12, Mod.Br(Colors.White, .95), B(0x8FD8FF, .8), Mod.Br(Colors.White, .6), B(0xE8F6FF), FontWeights.SemiBold, FontWeights.SemiBold, Icon: 18);
                return new Skin(HudThemes.Elements, look, (dc, s) => HudStorm.StormPanel(dc, s, op, 10, 3, crest: 14),
                    new Thickness(1, 0, 2, 4), new Thickness(12, 21, 14, 9), ink,
                    n => Header(Text(Title, 12.5, B(0xE8F6FF, .95), face, FontWeights.SemiBold),
                        Chip(Count(n), B(0x9B7CFF, .3), B(0x8FD8FF, .7), B(0xE8F6FF), face, radius: 3)),
                    (i, newest) => newest ? Mod.Grad(false, (Mod.A(C(0x8FD8FF), .24), 0), (Mod.A(C(0x9B7CFF), .06), 1)) : null, Mod.Br(Colors.White, .08), 3.5, new CornerRadius(3),
                    C(0x8FD8FF), C(0xE8F6FF), C(0x151A33));
            }
            case HudThemes.Magma:
            {
                var heavy = HudThemes.Heavy;
                var ink = new RowInk(face, 12, B(0xFFF0E4), B(0xFF9A2A, .85), B(0xFFE6D4, .66), B(0xFFD25E), FontWeights.SemiBold, FontWeights.Bold, Icon: 18);
                return new Skin(HudThemes.Elements, look, (dc, s) => HudMagma.Slab(dc, s, Math.Clamp(.5 + .5 * op, .6, 1), 11, river: true),
                    new Thickness(0, 0, 0, 12), new Thickness(12, 9, 14, 10), ink,
                    n => Header(Text(Title.ToUpperInvariant(), 12.5, B(0xFFF0E4), heavy, FontWeights.Black),
                        Chip(Count(n), Mod.Grad(true, (C(0xFFD25E), 0), (C(0xEE5712), .55), (C(0xB02408), 1)), B(0xFFF6DC, .6), B(0x2E1004), heavy, 12, 3, FontWeights.Black)),
                    (i, newest) => newest ? Mod.Grad(false, (Mod.A(C(0xEE5712), .32), 0), (Mod.A(C(0xB02408), .06), 1)) : null, B(0x4A372C, .9), 3.5, new CornerRadius(3),
                    C(0xEE5712), C(0xFFD25E), C(0x1A1310));
            }
            case HudThemes.Tide:
            {
                var ink = new RowInk(face, 12, B(0xF0FFFF), B(0x6FE6EA, .8), B(0xCFF2F2, .66), B(0x9FF2F2), FontWeights.SemiBold, FontWeights.SemiBold, Icon: 18);
                return new Skin(HudThemes.Elements, look, (dc, s) => HudTide.SeaPanel(dc, s, 13, op, surface: true, crest: .6),
                    new Thickness(2, 25, 3, 7), new Thickness(13, 7, 15, 11), ink,
                    n => Header(Text(Title, 12.5, B(0xE8FCFC, .95), face, FontWeights.SemiBold),
                        Chip(Count(n), B(0x1AA9B8, .3), B(0x6FE6EA, .7), B(0xF0FFFF), face, radius: 9, pad: new Thickness(7, 0, 7, 0))),
                    (i, newest) => newest ? Mod.Grad(false, (Mod.A(C(0x6FE6EA), .22), 0), (Mod.A(C(0x6FE6EA), .04), 1)) : null, B(0xA4F8F2, .1), 3.5, new CornerRadius(8),
                    C(0x6FE6EA), C(0xF0FFFF), C(0x082A33));
            }
            default:
            {
                // Frost: a slab of ice, a crystal crest on its corners, icicles under it
                var art = new HudFrost.IceArt(11, 7, 3, crest: .5, icicles: .8);
                var ink = new RowInk(face, 12, B(0xF6FDFF), B(0xA8E6FF, .8), B(0xB8DCEE, .78), B(0xEAFAFF), FontWeights.SemiBold, FontWeights.SemiBold, Icon: 18);
                return new Skin(HudThemes.Elements, HudThemes.Frost, (dc, s) => { art.Fit(s); HudFrost.IcePanel(dc, s, art, op); },
                    new Thickness(11, 20, 7, 19), new Thickness(12, 9, 14, 9), ink,
                    n => Header(Text(Title, 12.5, B(0xDCF2FF, .95), face, FontWeights.SemiBold),
                        Chip(Count(n), B(0x5CD6FF, .22), B(0xA8E6FF, .75), B(0xF6FDFF), face, radius: 2)),
                    (i, newest) => newest ? Mod.Grad(false, (Mod.A(C(0xA8E6FF), .22), 0), (Mod.A(C(0xA8E6FF), .04), 1)) : null, B(0x9EE2FF, .12), 3.5, new CornerRadius(2),
                    C(0x5CD6FF), C(0xEAFAFF), C(0x0D2A3A));
            }
        }
    }

    /* ---------------- Overdrive: a fighting game's slab ---------------- */

    static readonly Brush HotStripe = Mod.Grad(true, (C(0xFFD81F), 0), (C(0xFF8A0A), .55), (C(0xFF2E22), 1));

    static Skin Overdrive(string look, double op)
    {
        switch (look)
        {
            case HudThemes.Manga:
            {
                // a speech bubble on the page: ink words on paper, names in their class's colour inked dark, a black caption and a red count
                var ink = new RowInk(HudThemes.Heavy, 12, B(0x0A0A0D), B(0x0A0A0D, .74), B(0x0A0A0D, .7), B(0xE81C2B), FontWeights.Black, FontWeights.Black, Icon: 18, Name: cls => ClassArt.NameBrush(cls, dark: false));
                return new Skin(HudThemes.Overdrive, look, (dc, s) => MangaBubble(dc, s, op),
                    new Thickness(0), new Thickness(12, 9, 15, 16), ink,
                    n => Header(new Border { Background = B(0x0A0A0D), Padding = new Thickness(7, 1, 8, 2), HorizontalAlignment = HorizontalAlignment.Left,
                            Child = Text(Title.ToUpperInvariant(), 12, Mod.Br(Colors.White), HudThemes.Heavy, FontWeights.Black) },
                        Chip(Count(n), B(0xE81C2B), B(0x0A0A0D), Mod.Br(Colors.White), HudThemes.Impact, 13.5, 0, FontWeights.Normal, new Thickness(7, 0, 7, 0))),
                    (i, newest) => newest ? B(0xE81C2B, .14) : null, B(0x0A0A0D, .5), 3.5, null,
                    C(0x0A0A0D), C(0xE81C2B), C(0xFBF9F2));
            }
            case HudThemes.Dragon:
            {
                var ink = new RowInk(Mod.Face, 12, Mod.Br(Colors.White, .95), B(0xE2C48A, .85), Mod.Br(Colors.White, .6), B(0xFFD77A), FontWeights.SemiBold, FontWeights.SemiBold, Icon: 18);
                double ground = Math.Round(Math.Clamp(.55 + .4 * op, .62, .96) * 20) / 20;
                return new Skin(HudThemes.Overdrive, look, (dc, s) => dc.DrawDrawing(HudDragon.LineCoil(Math.Floor(s.Width), Math.Floor(s.Height), ground, true, false)),
                    new Thickness(1, 1, 0, 0), new Thickness(14, 13, 22, 15), ink,
                    n =>
                    {
                        var h = Header(Text(Title, 13.5, B(0xFFD77A), Serif, FontWeights.SemiBold),
                            Chip(Count(n), B(0x34F0AE, .14), B(0x34F0AE, .7), B(0x7DFFCF), Mod.Face, radius: 3));
                        h.Margin = new Thickness(40, 4, 0, 8);   // the dragon's head sits at its left
                        return h;
                    },
                    (i, newest) => newest ? Mod.Grad(false, (Mod.A(C(0xFFC445), .22), 0), (Mod.A(C(0xFF862A), .04), 1)) : null, B(0xB8935A, .16), 3.5, new CornerRadius(3),
                    C(0xFFC445), C(0xFFECB0), C(0x1C1723));
            }
            case HudThemes.Aura:
            {
                var ink = new RowInk(Mod.Face, 12, Mod.Br(Colors.White, .96), B(0x9FDDFF, .8), Mod.Br(Colors.White, .62), B(0xC8ECFF), FontWeights.SemiBold, FontWeights.SemiBold, Icon: 18);
                return new Skin(HudThemes.Overdrive, look, (dc, s) => HudAura.LinePlateArt(dc, s, false, .7 + .35 * op),
                    new Thickness(1, 0, 9, 1), new Thickness(16, 18, 20, 10), ink,
                    n => Header(Text(Title.ToUpperInvariant(), 12.5, B(0xC8ECFF), HudThemes.Bahn, FontWeights.Bold).Italic(),
                        Chip(Count(n), Mod.Grad(true, (C(0xC8ECFF), 0), (C(0x2F9BFF), 1)), B(0xFFF4C8, .6), B(0x0A0D14), HudThemes.Bahn, 12.5, 2)),
                    (i, newest) => newest ? Mod.Grad(false, (Mod.A(C(0x2F9BFF), .3), 0), (Mod.A(C(0x2F9BFF), .05), 1)) : null, B(0x9FDDFF, .1), 3.5, null,
                    C(0x2F9BFF), C(0xFFC42E), C(0x10121A));
            }
            default:
            {
                // Versus: the hot-striped slab, the title slammed in italic yellow, the count on a red badge
                var ink = new RowInk(new FontFamily("Segoe UI"), 12, Mod.Br(Colors.White, .96), B(0xFFD81F, .85), Mod.Br(Colors.White, .64), B(0xFFF39A), FontWeights.Bold, FontWeights.Bold, Icon: 18, Italic: true);
                return new Skin(HudThemes.Overdrive, HudThemes.Versus, (dc, s) => HudVersus.PlateArt(dc, s, Math.Min(.2, 16 / Math.Max(1, s.Height)), HotStripe),
                    new Thickness(0, 0, 2, 0), new Thickness(30, 9, 24, 9), ink,
                    n =>
                    {
                        // the title clear of the hot stripe at the slab's top left
                        var h = Header(Text(Title.ToUpperInvariant(), 15, B(0xFFD81F), HudThemes.Impact, FontWeights.Normal).Italic(),
                            Tab(Text(Count(n), 15, Mod.Br(Colors.White), HudThemes.Impact, FontWeights.Normal).Italic(), Mod.Grad(true, (C(0xFF2E22), 0), (C(0xB0061A), 1)), 4, edge: B(0xFFD81F, .8)));
                        h.Margin = new Thickness(10, 0, 0, 6);
                        return h;
                    },
                    (i, newest) => newest ? Mod.Grad(false, (Mod.A(C(0xFF8A0A), .3), 0), (Mod.A(C(0xFF2E22), .05), 1)) : null, Mod.Br(Colors.White, .08), 3.5, null,
                    C(0xFF8A0A), C(0xFFD81F), C(0x070812));
            }
        }
    }

    /// <summary>Manga's speech bubble (as the Buddy's, Ui/Themes/Overdrive/HudManga.cs DrawBubble): paper in a brushed ink line, a tail at its lower left, screentone in a corner.</summary>
    static void MangaBubble(DrawingContext dc, Size s, double op)
    {
        const double TailH = 7;
        double h = s.Height - TailH, w = s.Width;
        var body = new Rect(1.5, 1.5, w - 3, h - 3);
        double r = Math.Min(15, h / 2.4);
        Geometry shape = new RectangleGeometry(body, r, r);
        var tail = new StreamGeometry();
        using (var c = tail.Open())
        {
            c.BeginFigure(new Point(18, h - 4), true, true);
            c.LineTo(new Point(9, h - 1 + TailH - .5), true, true);
            c.LineTo(new Point(33, h - 4), true, true);
        }
        var all = Geometry.Combine(shape, tail, GeometryCombineMode.Union, null);
        var inkPen = new Pen(B(0x0A0A0D), 2.4) { LineJoin = PenLineJoin.Round }; inkPen.Freeze();
        dc.DrawGeometry(Mod.Br(C(0xFBF9F2), Math.Max(.85, op)), inkPen, all);
        HudManga.Tone(dc, new RectangleGeometry(new Rect(w - 30, h - 20, 28, 18)), new Point(w, h), 30, 3.6, 1.2, B(0x0A0A0D, .5));
        HudManga.Tone(dc, new RectangleGeometry(new Rect(2, 2, 34, 18)), new Point(0, 0), 30, 3.6, 1.1, B(0x0A0A0D, .35));
        dc.DrawGeometry(null, inkPen, all);
    }
}

static class TextItalic
{
    /// <summary>The text in italic (Hype's and Versus's words lean).</summary>
    public static TextBlock Italic(this TextBlock t) { t.FontStyle = FontStyles.Italic; return t; }
}
