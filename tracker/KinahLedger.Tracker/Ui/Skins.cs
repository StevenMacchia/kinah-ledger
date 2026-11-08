using System.Windows;
using System.Windows.Media;

namespace KinahLedger.Ui;

/// <summary>
/// The app's looks. Every colour the window uses is a named brush in Theme.xaml, referenced as a DynamicResource,
/// so a skin is just a dictionary of the same keys merged over the base theme; switching one re-paints the window
/// in place. Three to pick from: Night (the site's palette), Abyss (violet on near-black) and Daylight (light).
/// </summary>
public static class Skins
{
    public sealed record Skin(string Key, string Name, bool Dark, string Blurb);

    public static readonly Skin[] All =
    {
        new("night", "Night", true, "deep plum, Kinah gold and candy accents: the Quest look"),
        new("abyss", "Abyss", true, "navy and soft gold on near-black"),
        new("daylight", "Daylight", false, "light, for a bright room"),
    };

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
    internal static ResourceDictionary Night() => Build("night");

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
        void Controls(string btnHover, string btnLine, string itemHover, string itemSel, string segGroup, string segOn, string secGold,
            string chip, string chipLine, string chipOff, string chipOffLine, string onSlot, string slotFrame, string badgeLine, string hover,
            string goldTint, string goldLine, string bandTop, string bandMid, string bandLine, string flagBg, string flagInk, string flagLine)
        {
            S("BtnHover", btnHover); S("BtnHoverLine", btnLine); S("ItemHover", itemHover); S("ItemSel", itemSel); S("SegGroup", segGroup); S("SegOnBg", segOn);
            S("SecGold", secGold); S("ChipBg", chip); S("ChipLine", chipLine); S("ChipOff", chipOff); S("ChipOffLine", chipOffLine); S("OnSlot", onSlot);
            S("SlotFrame", slotFrame); S("SlotBadgeLine", badgeLine); S("HoverTint", hover); S("GoldTint", goldTint); S("GoldLine", goldLine);
            var band = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(0, 1) };
            band.GradientStops.Add(new GradientStop((Color)ColorConverter.ConvertFromString(bandTop)!, 0));
            band.GradientStops.Add(new GradientStop((Color)ColorConverter.ConvertFromString(bandMid)!, 0.6));
            band.GradientStops.Add(new GradientStop(Colors.Transparent, 1));
            d["PlaceBand"] = Frozen(band);
            S("PlaceBandLine", bandLine); S("FlagBg", flagBg); S("FlagInk", flagInk); S("FlagLine", flagLine);
        }
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
                Quest("#7FC4FF", "#C8A8FF", "#FF7A86", "#BFE86A", "#2B1B00", "#2B1B00", "#24FFFFFF", "#47FFFFFF", 0x29);
                S("MintTint", "#298EE3D6"); S("GoldIcTint", "#29F2C97D"); S("NoticeTint", "#1F7FC4FF");
                Controls("#1A2240", "#3E4C7A", "#172038", "#1E2A4A", "#08FFFFFF", "#F2C97D", "#E9B45A", "#1A8EE3D6", "#738EE3D6", "#B30A0E1E", "#263056",
                    "#FFFFFF", "#47D9DEF0", "#3E4C7A", "#08FFFFFF", "#14F2C97D", "#80F2C97D", "#29F2C97D", "#0DF2C97D", "#47E9B45A", "#14F2C97D", "#F2C97D", "#4DF2C97D");
                S("Bg", "#05070F"); S("Card", "#0E1328"); S("Card2", "#161C3A"); S("Line", "#242D52"); S("LineStrong", "#3A4878");
                S("Ink", "#ECEFFF"); S("Muted", "#A4ACCF"); S("Faint", "#7A84AE");
                S("Gold", "#F2C97D"); S("Violet", "#C8A8FF"); S("Mint", "#8EE3D6"); S("Accent", "#7FC4FF"); S("AccentDim", "#15294A");
                S("Good", "#58D39A"); S("Warn", "#F3A65B"); S("Bad", "#FF7A86");
                S("RowHead", "#0C1124"); S("RowHover", "#161C3A"); S("Mark", "#15294A"); S("Slot", "#070A18");
                R("HeroBg", "#1C2A5E", "#0E1328", "#0E1328"); S("HeroLine", "#55F2C97D"); S("Inset", "#38000000"); C("SlotColor", "#070A18");
                S("CommonInk", "#D6DCF0"); S("GradeRare", "#62D98B"); S("GradeLegend", "#5AA6FF"); S("GradeUnique", "#F3D35E"); S("GradeEpic", "#C8A8FF"); S("BtnPrimaryInk", "#2B1B00");
                G("BtnPrimary", "#F2C97D", "#D9A94A"); G("BtnPrimaryHover", "#F8D68F", "#E3B457"); G("BtnStop", "#FF7A86", "#C9505C");
                break;
            case "daylight":
                Quest("#1F8FCB", "#5C46C9", "#C2463A", "#5B8A1E", "#261702", "#261702", "#24000000", "#4A000000", 0x24);
                S("MintTint", "#241F8A6B"); S("GoldIcTint", "#29D9952F"); S("NoticeTint", "#125C46C9");
                Controls("#E6ECF4", "#9FB0C6", "#E8EEF6", "#DCE6F3", "#E9EEF5", "#F6CB72", "#9A6B10", "#141F8A6B", "#661F8A6B", "#F1F4F9", "#D9E0EA",
                    "#14202F", "#8FA3BC", "#B9C5D6", "#0A14202F", "#1AD9952F", "#99B07A1A", "#FFF3DA", "#FFFAF0", "#4DB07A1A", "#FFF2DC", "#8A4F00", "#59B86A14");
                S("Bg", "#E7EAF2"); S("Card", "#FFFFFF"); S("Card2", "#EEF1F7"); S("Line", "#D4DBE6"); S("LineStrong", "#B9C5D6");
                S("Ink", "#14202F"); S("Muted", "#5B6B82"); S("Faint", "#637287");
                S("Gold", "#9A6B10"); S("Violet", "#5C46C9"); S("Mint", "#1F8A6B"); S("Accent", "#1F8FCB"); S("AccentDim", "#D7ECF8");
                S("Good", "#1E8E57"); S("Warn", "#B86A14"); S("Bad", "#C2463A");
                S("RowHead", "#E8EDF4"); S("RowHover", "#F2F5F9"); S("Mark", "#E3EBF5"); S("Slot", "#E9EEF5");
                G("BtnPrimary", "#F6CB72", "#D9952F"); G("BtnPrimaryHover", "#F9D78E", "#E0A03C"); G("BtnStop", "#F6CB72", "#D9952F");
                R("HeroBg", "#FFF0D2", "#FFFFFF", "#FFFFFF"); S("HeroLine", "#66D9952F"); S("Inset", "#F4F6FA"); C("SlotColor", "#F1F4F9");
                S("CommonInk", "#2A3444"); S("GradeRare", "#1E8E57"); S("GradeLegend", "#1F6FCB"); S("GradeUnique", "#8F6F06"); S("GradeEpic", "#6E46C9"); S("BtnPrimaryInk", "#261702");
                break;
            default:   // night, the Quest palette since 2026-11-07: deep plum, Kinah gold, mint, sky, lilac, coral
                Quest("#6EC1FF", "#B692FF", "#FF7D6E", "#C6F26B", "#2B1B00", "#2B1B00", "#21FFFFFF", "#45FFFFFF", 0x29);
                S("MintTint", "#295CE1B5"); S("GoldIcTint", "#29FFC857"); S("NoticeTint", "#29B692FF");
                Controls("#2C2552", "#4A4180", "#2C2552", "#352C64", "#221C3D", "#FFC857", "#FFC857", "#1A5CE1B5", "#735CE1B5", "#2C2552", "#362E5E",
                    "#FFFFFF", "#4A4180", "#4A4180", "#09FFFFFF", "#14FFC857", "#80FFC857", "#29FFC857", "#0DFFC857", "#47FFC857", "#14FFC857", "#FFC857", "#4DFFC857");
                S("Bg", "#110D21"); S("Card", "#251E45"); S("Card2", "#312960"); S("Line", "#3B3366"); S("LineStrong", "#4A4180");
                S("Ink", "#F6F2FF"); S("Muted", "#B3ABD2"); S("Faint", "#7F78A3");
                S("Gold", "#FFC857"); S("Violet", "#B692FF"); S("Mint", "#5CE1B5"); S("Accent", "#6EC1FF"); S("AccentDim", "#1F2D4F");
                S("Good", "#5CE1B5"); S("Warn", "#F7A072"); S("Bad", "#FF7D6E"); S("Slot", "#110D21");
                S("RowHead", "#1C1735"); S("RowHover", "#2C2552"); S("Mark", "#2A2352");
                R("HeroBg", "#42327A", "#251E45", "#251E45"); S("HeroLine", "#33FFC857"); S("Inset", "#38000000"); C("SlotColor", "#110D21");
                S("CommonInk", "#E8E2FF"); S("GradeRare", "#62D98B"); S("GradeLegend", "#6EC1FF"); S("GradeUnique", "#FFC857"); S("GradeEpic", "#B692FF"); S("BtnPrimaryInk", "#2B1B00");
                G("BtnPrimary", "#FFC857", "#E59F2B"); G("BtnPrimaryHover", "#FFD97A", "#F0AE3C"); G("BtnStop", "#FF7D6E", "#D9524A");
                break;
        }
        return d;
    }

    static Brush Frozen(Brush b) { b.Freeze(); return b; }
}
