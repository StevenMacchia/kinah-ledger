using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Effects;

namespace KinahLedger.Ui;

/// <summary>
/// The app's looks. Every colour the window uses is a named brush in Theme.xaml, referenced as a DynamicResource,
/// so a skin is just a dictionary of the same keys merged over the base theme; switching one re-paints the window
/// in place. Six to pick from: Night (the site's palette), Abyss (navy on near-black), Daylight (light), and Wacky,
/// Tactical and Glitch, the overlay themes' colours (1.0).
/// </summary>
public static class Skins
{
    public sealed record Skin(string Key, string Name, bool Dark, string Blurb);

    public static readonly Skin[] All =
    {
        new("night", L.T("Night"), true, L.T("near-black with Kinah gold: the site's look")),
        new("abyss", L.T("Abyss"), true, L.T("navy and soft gold on near-black")),
        new("daylight", L.T("Daylight"), false, L.T("light, for a bright room")),
        // the overlay themes' colours on the window (1.0, Steven 2026-11-17: "add the skins for 1.0"): palettes only, no motion
        new(HudThemes.Wacky, L.T("Wacky"), false, L.T("a comic page: sticker cards in thick ink, a yellow hero")),
        new(HudThemes.Tactical, L.T("Tactical"), true, L.T("a pro's HUD: bracketed glass panels over a grid")),
        new(HudThemes.Glitch, L.T("Glitch"), true, L.T("a haunted tape: scanlines, split frames, REC")),
    };

    /// <summary>The app skin that matches an overlay theme (Wacky, Tactical, Glitch share their keys), or null for Classic.</summary>
    public static Skin? ForTheme(string? theme) => theme == HudThemes.Classic ? null
        : theme == HudThemes.Modern ? Get(Default)   // Modern has no skin of its own yet: Night, the site's look
        : All.FirstOrDefault(s => s.Key == theme);

    public const string Default = "night";
    static ResourceDictionary? _current;
    public static string Current { get; private set; } = Default;

    public static Skin Get(string? key) => All.FirstOrDefault(s => s.Key == key) ?? All[0];
    public static bool IsDark(string? key) => Get(key).Dark;
    /// <summary>A look that draws its overlay theme's parts on the window (Wacky, Tactical, Glitch; Ui/SkinDeco.cs).</summary>
    public static bool Themed => Current is HudThemes.Wacky or HudThemes.Tactical or HudThemes.Glitch;

    /// <summary>Merges the skin's brushes over the theme. Safe to call before any window exists and again at any time.</summary>
    public static void Apply(string? key)
    {
        var app = Application.Current;
        if (app == null) return;
        var skin = Get(key);
        var dict = Build(skin.Key);
        if (_current != null) app.Resources.MergedDictionaries.Remove(_current);
        app.Resources.MergedDictionaries.Add(dict);
        _current = dict; Current = skin.Key;
        SkinDeco.Refresh();   // the themed looks' frames, heads and bars (Ui/SkinDeco.cs)
    }

    /// <summary>Night's brushes, for a part drawn dark in every look (the Share sheet sets them on itself).</summary>
    internal static ResourceDictionary Night()
    {
        return Build("night");   // every key a look sets, shapes and faces too, so nothing of the window's look reaches the sheet
    }

    static ResourceDictionary Build(string key)
    {
        var d = new ResourceDictionary();
        void S(string k, string hex) => d[k] = Frozen(new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex)!));
        void G(string k, string top, string bottom)
        {
            var g = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(0, 1) };
            g.GradientStops.Add(new GradientStop((Color)ColorConverter.ConvertFromString(top)!, 0));
            g.GradientStops.Add(new GradientStop((Color)ColorConverter.ConvertFromString(bottom)!, 1));
            d[k] = Frozen(g);
        }
        void C(string k, string hex) => d[k] = (Color)ColorConverter.ConvertFromString(hex)!;
        void R(string k, string a, string b, string c)
        {
            var g = new RadialGradientBrush { Center = new Point(1, 0), GradientOrigin = new Point(1, 0), RadiusX = 0.95, RadiusY = 1.35 };
            g.GradientStops.Add(new GradientStop((Color)ColorConverter.ConvertFromString(a)!, 0));
            g.GradientStops.Add(new GradientStop((Color)ColorConverter.ConvertFromString(b)!, 0.62));
            g.GradientStops.Add(new GradientStop((Color)ColorConverter.ConvertFromString(c)!, 1));
            d[k] = Frozen(g);
        }
        // control states, headings, slot frames, chips and the Events cards (0.8.31): every look sets every one, so a switch is complete
        void Controls(bool dark, string btnHover, string btnLine, string itemHover, string itemSel, string segGroup, string segOn, string secGold,
            string chip, string chipLine, string chipOff, string chipOffLine, string onSlot, string slotFrame, string badgeLine, string hover,
            string goldTint, string goldLine, string bandTop, string bandMid, string bandLine, string flagBg, string flagInk, string flagLine)
        {
            S("NavOnBg", dark ? "#24FFFFFF" : "#2414202F"); S("BtnHover", btnHover); S("BtnHoverLine", btnLine); S("ItemHover", itemHover); S("ItemSel", itemSel); S("SegGroup", segGroup); S("SegOnBg", segOn);
            S("SecGold", secGold); S("ChipBg", chip); S("ChipLine", chipLine); S("ChipOff", chipOff); S("ChipOffLine", chipOffLine); S("OnSlot", onSlot);
            S("SlotFrame", slotFrame); S("SlotBadgeLine", badgeLine); S("HoverTint", hover); S("GoldTint", goldTint); S("GoldLine", goldLine);
            var band = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(0, 1) };
            band.GradientStops.Add(new GradientStop((Color)ColorConverter.ConvertFromString(bandTop)!, 0));
            band.GradientStops.Add(new GradientStop((Color)ColorConverter.ConvertFromString(bandMid)!, 0.6));
            band.GradientStops.Add(new GradientStop(Colors.Transparent, 1));
            d["PlaceBand"] = Frozen(band);
            S("PlaceBandLine", bandLine); S("FlagBg", flagBg); S("FlagInk", flagInk); S("FlagLine", flagLine);
        }
        // CardQuiet and CardHair (UX pass, 2026-11-13): the reference tier's surface, a step flatter than Card, and its hairline edge
        // the Quest look's roles (2026-11-07, docs/ui/mockups/directions-2026-11-07): gold is the chase, mint anything that pays,
        // sky a place, lilac a milestone, coral a miss, lime gathering; each with the tint its icon box sits on. SegOnInk is the
        // text on a lit pill, CardLine the one-pixel edge every card has, brighter under the mouse.
        void Quest(string sky, string lilac, string coral, string lime, string goldInk, string segOnInk, string cardLine, string cardLineHover, byte tint)
        {
            S("Sky", sky); S("Lilac", lilac); S("Coral", coral); S("Lime", lime); S("GoldInk", goldInk); S("SegOnInk", segOnInk);
            S("CardLine", cardLine); S("CardLineHover", cardLineHover);
            string T(string hex) => "#" + tint.ToString("X2") + hex.TrimStart('#');
            S("SkyTint", T(sky)); S("LilacTint", T(lilac)); S("CoralTint", T(coral)); S("LimeTint", T(lime));
        }
        // a look's shape (1.0): every look sets every key, so a switch never leaves one behind and the Share sheet (Night) has
        // its own. Night, Abyss and Daylight keep the rounded cards; the themed looks override below
        void Shape(double card, double refCard, double btn, double chip, double edge, double btnEdge, double glow, FontFamily body, FontFamily display, FontFamily label,
            Effect? cardFx = null, Effect? refFx = null, Effect? btnFx = null, Effect? displayFx = null, Effect? primaryFx = null)
        {
            d["CardRadius"] = new CornerRadius(card); d["RefRadius"] = new CornerRadius(refCard); d["BtnRadius"] = new CornerRadius(btn); d["ChipRadius"] = new CornerRadius(chip);
            d["CardEdge"] = new Thickness(edge); d["BtnEdge"] = new Thickness(btnEdge); d["PrimaryGlow"] = glow;
            d["BodyFont"] = body; d["DisplayFont"] = display; d["LabelFont"] = label;
            d["CardFx"] = cardFx; d["RefFx"] = refFx; d["BtnFx"] = btnFx; d["DisplayFx"] = displayFx; d["PrimaryFx"] = primaryFx ?? btnFx;
        }
        Shape(12, 10, 8, 6, 1, 1, 0.5, Inter, InterDisplay, Inter);
        switch (key)
        {
            case "abyss":
                // navy (2026-11-07, step 6 of the Quest look): the Abyss's own night, blue where Night is plum
                Quest("#A9B2D6", "#C4CAE4", "#FF7A86", "#A9B2D6", "#2B1B00", "#05070F", "#24FFFFFF", "#47FFFFFF", 0x29);
                S("MintTint", "#298EE3D6"); S("GoldIcTint", "#29F2C97D"); S("NoticeTint", "#1F7FC4FF");
                Controls(true, "#1A2240", "#3E4C7A", "#172038", "#1E2A4A", "#08FFFFFF", "#ECEFFF", "#E9B45A", "#1A8EE3D6", "#738EE3D6", "#B30A0E1E", "#263056",
                    "#FFFFFF", "#47D9DEF0", "#3E4C7A", "#08FFFFFF", "#14F2C97D", "#80F2C97D", "#29F2C97D", "#0DF2C97D", "#47E9B45A", "#14F2C97D", "#F2C97D", "#4DF2C97D");
                S("Bg", "#05070F"); S("Card", "#0E1328"); S("Card2", "#161C3A"); S("CardQuiet", "#0A0E1E"); S("CardHair", "#1A2142"); S("Line", "#242D52"); S("LineStrong", "#3A4878");
                S("Ink", "#ECEFFF"); S("Muted", "#B0B8D9"); S("Text2", "#C4CAE4"); S("Faint", "#8C96C0");
                S("Link", "#9FB8F0"); S("LinkHover", "#CFDCFF"); S("Progress", "#7F8FC4");
                S("Gold", "#F2C97D"); S("Violet", "#C8A8FF"); S("Mint", "#8EE3D6"); S("Accent", "#7FC4FF"); S("AccentDim", "#15294A");
                S("Good", "#58D39A"); S("Warn", "#F3A65B"); S("Bad", "#FF7A86");
                S("RowHead", "#0C1124"); S("RowHover", "#161C3A"); S("Mark", "#15294A"); S("Slot", "#070A18");
                R("HeroBg", "#1C2A5E", "#0E1328", "#0E1328"); S("HeroLine", "#55F2C97D"); S("Inset", "#38000000"); C("SlotColor", "#070A18");
                S("CommonInk", "#D6DCF0"); S("GradeRare", "#62D98B"); S("GradeLegend", "#5AA6FF"); S("GradeUnique", "#F3D35E"); S("GradeEpic", "#C8A8FF"); S("GradeSpecial", "#3FD3C6"); S("BtnPrimaryInk", "#2B1B00");
                G("BtnPrimary", "#F2C97D", "#D9A94A"); G("BtnPrimaryHover", "#F8D68F", "#E3B457"); G("BtnStop", "#FF7A86", "#C9505C");
                break;
            case "daylight":
                Quest("#4D5C72", "#3A475A", "#C2463A", "#4D5C72", "#261702", "#FFFFFF", "#24000000", "#4A000000", 0x24);
                S("MintTint", "#241F8A6B"); S("GoldIcTint", "#29D9952F"); S("NoticeTint", "#125C46C9");
                Controls(false, "#E6ECF4", "#9FB0C6", "#E8EEF6", "#DCE6F3", "#E9EEF5", "#14202F", "#9A6B10", "#141F8A6B", "#661F8A6B", "#F1F4F9", "#D9E0EA",
                    "#14202F", "#8FA3BC", "#B9C5D6", "#0A14202F", "#1AD9952F", "#99B07A1A", "#FFF3DA", "#FFFAF0", "#4DB07A1A", "#FFF2DC", "#8A4F00", "#59B86A14");
                S("Bg", "#E7EAF2"); S("Card", "#FFFFFF"); S("Card2", "#EEF1F7"); S("CardQuiet", "#F4F6FA"); S("CardHair", "#D7DDE7"); S("Line", "#D4DBE6"); S("LineStrong", "#B9C5D6");
                S("Ink", "#14202F"); S("Muted", "#4D5C72"); S("Text2", "#3A475A"); S("Faint", "#56657A");
                S("Link", "#2F5596"); S("LinkHover", "#1B3768"); S("Progress", "#74829E");
                S("Gold", "#835B0C"); S("Violet", "#5C46C9"); S("Mint", "#167560"); S("Accent", "#1F8FCB"); S("AccentDim", "#D7ECF8");
                S("Good", "#18794A"); S("Warn", "#B86A14"); S("Bad", "#C2463A");
                S("RowHead", "#E8EDF4"); S("RowHover", "#F2F5F9"); S("Mark", "#E3EBF5"); S("Slot", "#E9EEF5");
                G("BtnPrimary", "#F6CB72", "#D9952F"); G("BtnPrimaryHover", "#F9D78E", "#E0A03C"); G("BtnStop", "#F6CB72", "#D9952F");
                R("HeroBg", "#FFF0D2", "#FFFFFF", "#FFFFFF"); S("HeroLine", "#66D9952F"); S("Inset", "#F4F6FA"); C("SlotColor", "#F1F4F9");
                S("CommonInk", "#2A3444"); S("GradeRare", "#1E8E57"); S("GradeLegend", "#1F6FCB"); S("GradeUnique", "#8F6F06"); S("GradeEpic", "#6E46C9"); S("GradeSpecial", "#0E8C83"); S("BtnPrimaryInk", "#261702");
                break;
            case HudThemes.Wacky:
                // Sticker's comic page (Ui/Themes/Wacky/HudSticker.cs): cream sticker cards in thick ink with hard ink drops on a
                // lilac halftone page, the hero a yellow sticker (Ui/SkinDeco.cs), hot pink and cyan the accents, Segoe UI Black
                Quest("#0A6FB8", "#5A2BB0", "#D61F4C", "#2E7D12", "#111111", "#111111", "#111111", "#111111", 0x26);
                S("MintTint", "#260A7A3E"); S("GoldIcTint", "#33F2B705"); S("NoticeTint", "#1F0A6FB8");
                Controls(false, "#FFE98A", "#111111", "#FFF0C2", "#FFE14D", "#F3E3C4", "#FFE14D", "#B0103A", "#26FF3DA6", "#111111", "#FFF3DC", "#5E4F6B",
                    "#111111", "#111111", "#111111", "#14111111", "#33FFE14D", "#111111", "#66FFE14D", "#26FFE14D", "#111111", "#FFE14D", "#111111", "#111111");
                S("Bg", "#E6D3FA"); S("Card", "#FFF8E8"); S("Card2", "#FFEFCC"); S("CardQuiet", "#FFF3DC"); S("CardHair", "#111111"); S("Line", "#2E111111"); S("LineStrong", "#111111");
                S("Ink", "#111111"); S("Muted", "#4A3B55"); S("Text2", "#2E2338"); S("Faint", "#5E4F6B");
                S("Link", "#0A5E9E"); S("LinkHover", "#C2125F"); S("Progress", "#6B5A80");
                S("Gold", "#A34E00"); S("Violet", "#5A2BB0"); S("Mint", "#0A7A3E"); S("Accent", "#C2125F"); S("AccentDim", "#FFD6EA");
                S("Good", "#0A7A3E"); S("Warn", "#A34E00"); S("Bad", "#C2123F");
                S("RowHead", "#FFF0D0"); S("RowHover", "#FFF0C2"); S("Mark", "#FFE14D"); S("Slot", "#FFF3DC");
                R("HeroBg", "#FFE14D", "#FFE14D", "#FFE14D"); S("HeroLine", "#111111"); S("Inset", "#FFF8E8"); C("SlotColor", "#FFF3DC");
                S("CommonInk", "#2A2A2A"); S("GradeRare", "#1A7F45"); S("GradeLegend", "#1F5FB8"); S("GradeUnique", "#8A6400"); S("GradeEpic", "#6A2BC9"); S("GradeSpecial", "#0B7F78"); S("BtnPrimaryInk", "#111111");
                G("BtnPrimary", "#FFE14D", "#FFE14D"); G("BtnPrimaryHover", "#FFEB80", "#FFEB80"); G("BtnStop", "#FF6B8A", "#FF6B8A");
                S("CardLine", "#111111"); S("CardLineHover", "#C2125F"); S("GroupLine", "#111111"); S("BtnPrimaryLine", "#111111");
                S("NavOnBg", "#FF3DA6"); S("NavOnInk", "#111111");   // the page tab on show: a hot pink sticker
                d["BgArt"] = Tiled("#E6D3FA", 10, 10, g =>
                {
                    g.Children.Add(new GeometryDrawing(new SolidColorBrush(Hex("#D4B6F5")), null, new EllipseGeometry(new Point(2.5, 2.5), 1.9, 1.9)));
                    g.Children.Add(new GeometryDrawing(new SolidColorBrush(Hex("#D4B6F5")), null, new EllipseGeometry(new Point(7.5, 7.5), 1.9, 1.9)));
                });
                var black = new FontFamily("Segoe UI Black, Segoe UI");
                Shape(12, 10, 10, 8, 2.5, 2, 0, Inter, black, black,
                    cardFx: Hard("#111111", 5), refFx: Hard("#111111", 4), btnFx: Hard("#111111", 2.5), displayFx: null, primaryFx: Hard("#111111", 3.5));
                break;
            case HudThemes.Tactical:
                // Fighter and Holo (Ui/Themes/Tactical): teal-black HUD glass over a range grid, chamfered frames with lit L
                // brackets (Ui/SkinDeco.cs), amber for Kinah and the [ HEADS ], HUD cyan for focus, the window in Bahnschrift
                Quest("#8FD3EA", "#C9D1DC", "#FF6B78", "#9CE6B4", "#1A1000", "#04121A", "#24414C", "#3E6A78", 0x24);
                S("MintTint", "#245EF0A0"); S("GoldIcTint", "#24FFC84A"); S("NoticeTint", "#1F7FE9FF");
                Controls(true, "#12242B", "#3E6A78", "#10222A", "#173039", "#081114", "#7FE9FF", "#FFC84A", "#147FE9FF", "#597FE9FF", "#071013", "#1C2A33",
                    "#FFFFFF", "#2E5560", "#2E5560", "#0D7FE9FF", "#14FFC84A", "#80FFC84A", "#1F7FE9FF", "#0A7FE9FF", "#4D7FE9FF", "#14FFC84A", "#FFD27A", "#4DFFC84A");
                S("Bg", "#05090C"); S("Card", "#0A1519"); S("Card2", "#102026"); S("CardQuiet", "#08120F"); S("CardHair", "#1C2A33"); S("Line", "#1A2C33"); S("LineStrong", "#2E5560");
                S("Ink", "#EAF6F8"); S("Muted", "#A3BAC2"); S("Text2", "#C3D5DB"); S("Faint", "#859DA6");
                S("Link", "#7FE9FF"); S("LinkHover", "#BFF6FF"); S("Progress", "#6F96A3");
                S("Gold", "#FFC84A"); S("Violet", "#B9A6FF"); S("Mint", "#5EF0A0"); S("Accent", "#7FE9FF"); S("AccentDim", "#0F2A33");
                S("Good", "#5EF0A0"); S("Warn", "#FFA53D"); S("Bad", "#FF6B78");
                S("RowHead", "#0B181C"); S("RowHover", "#102026"); S("Mark", "#123039"); S("Slot", "#071013");
                R("HeroBg", "#0E2E22", "#0A1A16", "#0A1519"); S("HeroLine", "#4D5CFF9A"); S("Inset", "#40000000"); C("SlotColor", "#071013");
                S("CommonInk", "#D9E6EA"); S("GradeRare", "#62D98B"); S("GradeLegend", "#5AA6FF"); S("GradeUnique", "#F3D35E"); S("GradeEpic", "#B98FFF"); S("GradeSpecial", "#3FD3C6"); S("BtnPrimaryInk", "#1A1000");
                G("BtnPrimary", "#FFC84A", "#FFC84A"); G("BtnPrimaryHover", "#FFD67A", "#FFD67A"); G("BtnStop", "#FF6B78", "#FF6B78");
                S("CardLine", "#24414C"); S("CardLineHover", "#7FE9FF"); S("GroupLine", "#24414C"); S("BtnPrimaryLine", "#FFC84A");
                S("NavOnBg", "#E8263A"); S("NavOnInk", "#FFFFFF");   // the page tab on show: the scorebug's red block
                d["BgArt"] = Tiled("#05090C", 32, 32, g =>
                {
                    var grid = new SolidColorBrush(Hex("#0C171C"));
                    g.Children.Add(new GeometryDrawing(grid, null, new RectangleGeometry(new Rect(0, 0, 32, 1))));
                    g.Children.Add(new GeometryDrawing(grid, null, new RectangleGeometry(new Rect(0, 0, 1, 32))));
                    var cross = new SolidColorBrush(Hex("#1A3540"));
                    g.Children.Add(new GeometryDrawing(cross, null, new RectangleGeometry(new Rect(0, 0, 4, 1))));
                    g.Children.Add(new GeometryDrawing(cross, null, new RectangleGeometry(new Rect(0, 0, 1, 4))));
                });
                var din = new FontFamily("Bahnschrift, Segoe UI");
                Shape(0, 0, 0, 0, 0, 1, 0, din, din, din, displayFx: Glow("#7FE9FF", 12, 0.45));
                break;
            case HudThemes.Glitch:
                // VHS and Corrupt (Ui/Themes/Glitch): black tape with scanlines, thin OSD frames split magenta and cyan, the hero
                // boxed with REC (Ui/SkinDeco.cs), white OSD ink, titles split red, a white block for the lit pill, mono heads
                Quest("#2BE6FF", "#C9B8FF", "#FF2B6A", "#3DFF8C", "#0A0A0E", "#050507", "#3A3A46", "#9A9AAA", 0x24);
                S("MintTint", "#243DFF8C"); S("GoldIcTint", "#24FFD24A"); S("NoticeTint", "#1F2BE6FF");
                Controls(true, "#1A1A22", "#9A9AAA", "#16161D", "#20202A", "#0B0B10", "#F2F2F5", "#FFD24A", "#142BE6FF", "#592BE6FF", "#08080B", "#2A2A33",
                    "#FFFFFF", "#4A4A57", "#4A4A57", "#0DFFFFFF", "#14FFD24A", "#80FFD24A", "#1FFF2B6A", "#0AFF2B6A", "#4DFF2B6A", "#14FFD24A", "#FFD24A", "#4DFFD24A");
                S("Bg", "#050507"); S("Card", "#0B0B10"); S("Card2", "#15151C"); S("CardQuiet", "#09090D"); S("CardHair", "#2A2A33"); S("Line", "#24242E"); S("LineStrong", "#4A4A57");
                S("Ink", "#F2F2F5"); S("Muted", "#B4B4C0"); S("Text2", "#CFCFD8"); S("Faint", "#8E8E9C");
                S("Link", "#2BE6FF"); S("LinkHover", "#A8F4FF"); S("Progress", "#8A8A9A");
                S("Gold", "#FFD24A"); S("Violet", "#C9B8FF"); S("Mint", "#3DFF8C"); S("Accent", "#2BE6FF"); S("AccentDim", "#0B2A30");
                S("Good", "#3DFF8C"); S("Warn", "#FFD24A"); S("Bad", "#FF2B6A");
                S("RowHead", "#0E0E13"); S("RowHover", "#15151C"); S("Mark", "#1A1A24"); S("Slot", "#08080B");
                R("HeroBg", "#0B0B10", "#0B0B10", "#0B0B10"); S("HeroLine", "#E8E8EE"); S("Inset", "#40000000"); C("SlotColor", "#08080B");
                S("CommonInk", "#DADAE2"); S("GradeRare", "#62D98B"); S("GradeLegend", "#5AA6FF"); S("GradeUnique", "#F3D35E"); S("GradeEpic", "#C8A8FF"); S("GradeSpecial", "#3FD3C6"); S("BtnPrimaryInk", "#050507");
                G("BtnPrimary", "#F2F2F5", "#F2F2F5"); G("BtnPrimaryHover", "#FFFFFF", "#FFFFFF"); G("BtnStop", "#FF2B6A", "#FF2B6A");
                S("CardLine", "#6C6C7A"); S("CardLineHover", "#F2F2F5"); S("GroupLine", "#6C6C7A"); S("BtnPrimaryLine", "#F2F2F5");
                S("NavOnBg", "#F2F2F5"); S("NavOnInk", "#050507");   // the page tab on show: an OSD's inverted box
                d["BgArt"] = Tiled("#07070A", 4, 3, g => g.Children.Add(new GeometryDrawing(new SolidColorBrush(Hex("#C0000000")), null, new RectangleGeometry(new Rect(0, 2, 4, 1)))));
                var mono = new FontFamily("Cascadia Mono, Consolas");
                Shape(0, 0, 0, 0, 0, 1, 0, Inter, mono, mono,
                    displayFx: Hard("#FF2B6A", 2, 180, 0.85), primaryFx: Hard("#FF2B6A", 3, 180, 0.95));
                break;
            default:   // night, the v2 palette since 2026-11-10 (the site's kl-v2.css tokens): near-black neutral, gold the one
                       // accent, mint and coral for up and down, the game's grade colours; the old candy roles (sky, lilac, lime) are
                       // quiet neutrals, and their icon boxes one neutral tint. The lit pill is ink on near-black, as the site's
                Quest("#AEB5C7", "#C9CDDA", "#F26D6D", "#AEB5C7", "#1D1300", "#0A0B10", "#232736", "#3A4058", 0x24);
                // a card's edge lighter along its top, the site's inset highlight (box-shadow 0 1px 0 rgba(255,255,255,.035) inset)
                G("CardLine", "#30354A", "#1F2330"); G("CardLineHover", "#474D66", "#30354A");
                S("SkyTint", "#17BEC6DC"); S("LilacTint", "#17BEC6DC"); S("LimeTint", "#17BEC6DC");
                S("MintTint", "#17BEC6DC"); S("GoldIcTint", "#24F5B942"); S("NoticeTint", "#17BEC6DC");
                Controls(true, "#1E2230", "#3A4058", "#1E2230", "#262A38", "#111319", "#EDEFF5", "#F5B942", "#14BEC6DC", "#2E3346", "#0D0F14", "#232736",
                    "#FFFFFF", "#2E3346", "#2E3346", "#0DFFFFFF", "#14F5B942", "#80F5B942", "#1FF5B942", "#0AF5B942", "#3DF5B942", "#14F5B942", "#FFD27A", "#4DF5B942");
                S("Bg", "#0A0B10"); S("Card", "#111319"); S("Card2", "#171A22"); S("CardQuiet", "#0E1015"); S("CardHair", "#1F222C"); S("Line", "#232736"); S("LineStrong", "#2E3346");
                S("Ink", "#EDEFF5"); S("Muted", "#AFB5C6"); S("Text2", "#C9CDDA"); S("Faint", "#868DA1");
                S("Link", "#9DB2E8"); S("LinkHover", "#C9D6F7"); S("Progress", "#7F8BAA");
                S("Gold", "#F5B942"); S("Violet", "#B39BFF"); S("Mint", "#3DD68C"); S("Accent", "#F5B942"); S("AccentDim", "#1E2230");
                S("Good", "#3DD68C"); S("Warn", "#F7A072"); S("Bad", "#F26D6D"); S("Slot", "#0D0F14");
                S("RowHead", "#0F1117"); S("RowHover", "#171A22"); S("Mark", "#1A1D27");
                R("HeroBg", "#171A23", "#111319", "#111319"); S("HeroLine", "#2E3346"); S("Inset", "#38000000"); C("SlotColor", "#0D0F14");
                S("CommonInk", "#D9DDE8"); S("GradeRare", "#62D98B"); S("GradeLegend", "#5AA6FF"); S("GradeUnique", "#F3D35E"); S("GradeEpic", "#B48BFF"); S("GradeSpecial", "#3FD3C6"); S("BtnPrimaryInk", "#1D1300");
                G("BtnPrimary", "#F7C257", "#F2B23A"); G("BtnPrimaryHover", "#FFD27A", "#FBC966"); G("BtnStop", "#F26D6D", "#E35D5D");
                break;
        }
        if (!d.Contains("BgArt")) d["BgArt"] = d["Bg"];
        if (!d.Contains("NavOnInk")) d["NavOnInk"] = d["Ink"];
        if (!d.Contains("GroupLine")) d["GroupLine"] = d["Line"];
        if (!d.Contains("BtnPrimaryLine")) d["BtnPrimaryLine"] = d["Gold"];
        return d;
    }

    /// <summary>
    /// A card's corner as the look draws it, for a card built in code (built again when the look changes: its signature has
    /// <see cref="Current"/>): Wacky rounder, Tactical square-cut, Glitch square.
    /// </summary>
    public static CornerRadius Corner(double r) => Current switch
    {
        HudThemes.Wacky => new CornerRadius(r + 6),
        HudThemes.Tactical => new CornerRadius(Math.Min(r, 2)),
        HudThemes.Glitch => new CornerRadius(0),
        _ => new CornerRadius(r),
    };

    // Inter from files on disk, not the exe's resources, which held a stream for each read of the font (Ui/AppFonts.cs)
    static FontFamily Inter => AppFonts.Text;
    static FontFamily InterDisplay => AppFonts.Display;

    static Color Hex(string hex) => (Color)ColorConverter.ConvertFromString(hex)!;

    /// <summary>A hard-edged shadow (no blur): Wacky's sticker drop, Glitch's colour split.</summary>
    static Effect Hard(string hex, double depth, double direction = 315, double opacity = 1)
    {
        var e = new DropShadowEffect { Color = Hex(hex), BlurRadius = 0, ShadowDepth = depth, Direction = direction, Opacity = opacity };
        e.Freeze(); return e;
    }

    /// <summary>A soft glow round the element: Tactical's HUD figures, Glitch's phosphor bloom.</summary>
    static Effect Glow(string hex, double radius, double opacity)
    {
        var e = new DropShadowEffect { Color = Hex(hex), BlurRadius = radius, ShadowDepth = 0, Opacity = opacity };
        e.Freeze(); return e;
    }

    /// <summary>The window's ground: a colour with a pattern tiled over it every <paramref name="w"/> by <paramref name="h"/> px.</summary>
    static Brush Tiled(string ground, double w, double h, Action<DrawingGroup> pattern)
    {
        var g = new DrawingGroup();
        g.Children.Add(new GeometryDrawing(new SolidColorBrush(Hex(ground)), null, new RectangleGeometry(new Rect(0, 0, w, h))));
        pattern(g);
        var b = new DrawingBrush(g) { TileMode = TileMode.Tile, Viewport = new Rect(0, 0, w, h), ViewportUnits = BrushMappingMode.Absolute, Viewbox = new Rect(0, 0, w, h), ViewboxUnits = BrushMappingMode.Absolute, Stretch = Stretch.None };
        b.Freeze(); return b;
    }

    /// <summary>A card's edge lit at its two sides and the ends of its top and bottom: a HUD's bracket frame ([ ]).</summary>
    static Brush Brackets(string lit, string dim, double reach = 0.022)
    {
        var b = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 0) };
        b.GradientStops.Add(new GradientStop(Hex(lit), 0)); b.GradientStops.Add(new GradientStop(Hex(lit), reach));
        b.GradientStops.Add(new GradientStop(Hex(dim), reach)); b.GradientStops.Add(new GradientStop(Hex(dim), 1 - reach));
        b.GradientStops.Add(new GradientStop(Hex(lit), 1 - reach)); b.GradientStops.Add(new GradientStop(Hex(lit), 1));
        b.Freeze(); return b;
    }

    /// <summary>For the options self-test: the resource keys a look sets (every look sets Night's, or a switch leaves some behind).</summary>
    internal static IEnumerable<string> KeysOf(string key) => Build(key).Keys.OfType<string>();

    static Brush Frozen(Brush b) { b.Freeze(); return b; }
}
