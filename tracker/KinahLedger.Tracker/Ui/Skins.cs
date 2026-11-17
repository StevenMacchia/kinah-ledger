using System.Windows;
using System.Windows.Media;

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
        new(HudThemes.Wacky, L.T("Wacky"), true, L.T("neon arcade: magenta and cyan on deep purple")),
        new(HudThemes.Tactical, L.T("Tactical"), true, L.T("a pro's HUD: steel, amber and cyan lines")),
        new(HudThemes.Glitch, L.T("Glitch"), true, L.T("an old green screen: phosphor and amber on black")),
    };

    /// <summary>The app skin that matches an overlay theme (Wacky, Tactical, Glitch share their keys), or null for Classic.</summary>
    public static Skin? ForTheme(string? theme) => theme == HudThemes.Classic ? null : All.FirstOrDefault(s => s.Key == theme);

    public const string Default = "night";
    static ResourceDictionary? _current;
    public static string Current { get; private set; } = Default;

    public static Skin Get(string? key) => All.FirstOrDefault(s => s.Key == key) ?? All[0];
    public static bool IsDark(string? key) => Get(key).Dark;

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
    }

    /// <summary>Night's brushes, for a part drawn dark in every look (the Share sheet sets them on itself).</summary>
    internal static ResourceDictionary Night()
    {
        var d = Build("night");
        d["DisplayFont"] = new FontFamily(new Uri("pack://application:,,,/"), "./Fonts/#Inter, Segoe UI Variable Display, Segoe UI");   // the base face, whatever the window's look
        return d;
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
                // Neon's arcade cabinet (Ui/Themes/Wacky/HudNeon.cs): deep purple ground, magenta and cyan tubes, a lemon for Kinah
                Quest("#7FE9FF", "#D9C8FF", "#FF6B8A", "#B6FF6B", "#1F1300", "#1A0F00", "#3A2760", "#5C3E94", 0x29);
                S("MintTint", "#2948F59A"); S("GoldIcTint", "#29FFE14D"); S("NoticeTint", "#2900E5FF");
                Controls(true, "#2A1B47", "#5C3E94", "#271843", "#33205A", "#170D28", "#FFE14D", "#FFE14D", "#1A00E5FF", "#6600E5FF", "#140A22", "#33224F",
                    "#FFFFFF", "#4A3470", "#4A3470", "#0DFFFFFF", "#1AFFE14D", "#80FFE14D", "#29FF2BD6", "#0DFF2BD6", "#59FF2BD6", "#1AFFE14D", "#FFE66B", "#59FFE14D");
                S("Bg", "#120A20"); S("Card", "#1C1030"); S("Card2", "#251742"); S("CardQuiet", "#170D28"); S("CardHair", "#2E1F4A"); S("Line", "#33224F"); S("LineStrong", "#4A3470");
                S("Ink", "#F6F0FF"); S("Muted", "#C4B6E0"); S("Text2", "#DCD0F2"); S("Faint", "#A08FC4");
                S("Link", "#7FE9FF"); S("LinkHover", "#C4F7FF"); S("Progress", "#A48CE0");
                S("Gold", "#FFE14D"); S("Violet", "#C9B0FF"); S("Mint", "#48F59A"); S("Accent", "#FF5BDF"); S("AccentDim", "#3A1450");
                S("Good", "#48F59A"); S("Warn", "#FFB35C"); S("Bad", "#FF6B8A");
                S("RowHead", "#180E2A"); S("RowHover", "#251742"); S("Mark", "#2A1B47"); S("Slot", "#140A22");
                R("HeroBg", "#41166A", "#1C1030", "#1C1030"); S("HeroLine", "#80FF2BD6"); S("Inset", "#38000000"); C("SlotColor", "#140A22");
                S("CommonInk", "#E4DCF5"); S("GradeRare", "#62E08F"); S("GradeLegend", "#62AEFF"); S("GradeUnique", "#F5D65E"); S("GradeEpic", "#D2B4FF"); S("GradeSpecial", "#3FDBCE"); S("BtnPrimaryInk", "#1F1300");
                G("BtnPrimary", "#FFE66B", "#FFC21A"); G("BtnPrimaryHover", "#FFEE94", "#FFCF45"); G("BtnStop", "#FF6B8A", "#E0456A");
                break;
            case HudThemes.Tactical:
                // the jet HUD and the scorebug (Ui/Themes/Tactical): gunmetal, amber for Kinah, HUD cyan for focus, a squared face
                Quest("#8FD3EA", "#C9D1DC", "#FF6B78", "#B8C9A0", "#1A1000", "#04121A", "#24303D", "#3E4E60", 0x24);
                S("MintTint", "#245EF0A0"); S("GoldIcTint", "#24FFC84A"); S("NoticeTint", "#1F7FE9FF");
                Controls(true, "#1B232C", "#3E4E60", "#19212A", "#212B36", "#0C1015", "#7FE9FF", "#FFC84A", "#147FE9FF", "#597FE9FF", "#0B0F13", "#222C38",
                    "#FFFFFF", "#34414F", "#34414F", "#0DFFFFFF", "#14FFC84A", "#80FFC84A", "#1F7FE9FF", "#0A7FE9FF", "#4D7FE9FF", "#14FFC84A", "#FFD27A", "#4DFFC84A");
                S("Bg", "#080B0F"); S("Card", "#10151B"); S("Card2", "#161D25"); S("CardQuiet", "#0C1015"); S("CardHair", "#1C242E"); S("Line", "#222C38"); S("LineStrong", "#34414F");
                S("Ink", "#F2F4F7"); S("Muted", "#AEB8C5"); S("Text2", "#C9D1DC"); S("Faint", "#8C97A8");
                S("Link", "#7FE9FF"); S("LinkHover", "#BFF6FF"); S("Progress", "#7F93A8");
                S("Gold", "#FFC84A"); S("Violet", "#B9A6FF"); S("Mint", "#5EF0A0"); S("Accent", "#7FE9FF"); S("AccentDim", "#0F2A33");
                S("Good", "#5EF0A0"); S("Warn", "#FFA53D"); S("Bad", "#FF6B78");
                S("RowHead", "#0E1318"); S("RowHover", "#161D25"); S("Mark", "#18232C"); S("Slot", "#0B0F13");
                R("HeroBg", "#15303B", "#10151B", "#10151B"); S("HeroLine", "#4D7FE9FF"); S("Inset", "#38000000"); C("SlotColor", "#0B0F13");
                S("CommonInk", "#D9DFE8"); S("GradeRare", "#62D98B"); S("GradeLegend", "#5AA6FF"); S("GradeUnique", "#F3D35E"); S("GradeEpic", "#B98FFF"); S("GradeSpecial", "#3FD3C6"); S("BtnPrimaryInk", "#1A1000");
                G("BtnPrimary", "#FFD066", "#F5B42E"); G("BtnPrimaryHover", "#FFDC85", "#FFC24A"); G("BtnStop", "#FF6B78", "#E04A58");
                d["DisplayFont"] = new FontFamily("Bahnschrift, Segoe UI Variable Display, Segoe UI");
                break;
            case HudThemes.Glitch:
                // the Terminal's green screen (Ui/Themes/Glitch/HudTerminal.cs): phosphor on black, amber for Kinah, magenta for a miss
                Quest("#5CE8FF", "#C9B8FF", "#FF5C86", "#A8FF6B", "#1A0F00", "#021008", "#1C2E27", "#2F4A3F", 0x24);
                S("MintTint", "#245CFF9A"); S("GoldIcTint", "#24FFB641"); S("NoticeTint", "#1F5CE8FF");
                Controls(true, "#15201C", "#2F4A3F", "#14201B", "#1A2A23", "#0A0E10", "#5CFF9A", "#FFB641", "#145CFF9A", "#595CFF9A", "#080C0D", "#1C2A26",
                    "#FFFFFF", "#2B4038", "#2B4038", "#0DFFFFFF", "#14FFB641", "#80FFB641", "#1F5CFF9A", "#0A5CFF9A", "#4D5CFF9A", "#14FFB641", "#FFCB73", "#4DFFB641");
                S("Bg", "#05080A"); S("Card", "#0C1113"); S("Card2", "#121A1B"); S("CardQuiet", "#090D0F"); S("CardHair", "#17221F"); S("Line", "#1C2A26"); S("LineStrong", "#2B4038");
                S("Ink", "#E2FFEC"); S("Muted", "#A6C6B3"); S("Text2", "#C2DECD"); S("Faint", "#80A391");
                S("Link", "#5CE8FF"); S("LinkHover", "#B5F5FF"); S("Progress", "#6FA088");
                S("Gold", "#FFB641"); S("Violet", "#C9B8FF"); S("Mint", "#5CFF9A"); S("Accent", "#5CFF9A"); S("AccentDim", "#0C2A1A");
                S("Good", "#5CFF9A"); S("Warn", "#FFD23F"); S("Bad", "#FF5C86");
                S("RowHead", "#0A0F10"); S("RowHover", "#121A1B"); S("Mark", "#13221C"); S("Slot", "#080C0D");
                R("HeroBg", "#0E2C1D", "#0C1113", "#0C1113"); S("HeroLine", "#4D5CFF9A"); S("Inset", "#38000000"); C("SlotColor", "#080C0D");
                S("CommonInk", "#D2E8DA"); S("GradeRare", "#62D98B"); S("GradeLegend", "#5AA6FF"); S("GradeUnique", "#F3D35E"); S("GradeEpic", "#C8A8FF"); S("GradeSpecial", "#3FD3C6"); S("BtnPrimaryInk", "#1A0F00");
                G("BtnPrimary", "#FFC665", "#FFAE2E"); G("BtnPrimaryHover", "#FFD38A", "#FFBC4D"); G("BtnStop", "#FF5C86", "#D93D66");
                d["DisplayFont"] = new FontFamily("Cascadia Mono, Consolas");
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
        return d;
    }

    /// <summary>For the options self-test: the resource keys a look sets (every look sets Night's, or a switch leaves some behind).</summary>
    internal static IEnumerable<string> KeysOf(string key) => Build(key).Keys.OfType<string>();

    static Brush Frozen(Brush b) { b.Freeze(); return b; }
}
