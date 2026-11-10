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
        new("night", "Night", true, "near-black with Kinah gold: the site's look"),
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
            default:   // night, the v2 palette since 2026-11-10 (the site's kl-v2.css tokens): near-black neutral, gold the one
                       // accent, mint and coral for up and down, the game's grade colours; the old candy roles (sky, lilac, lime) are
                       // quiet neutrals, and their icon boxes one neutral tint. The lit pill is ink on near-black, as the site's
                Quest("#AEB5C7", "#C9CDDA", "#F26D6D", "#AEB5C7", "#1D1300", "#0A0B10", "#232736", "#3A4058", 0x24);
                S("SkyTint", "#17BEC6DC"); S("LilacTint", "#17BEC6DC"); S("LimeTint", "#17BEC6DC");
                S("MintTint", "#17BEC6DC"); S("GoldIcTint", "#24F5B942"); S("NoticeTint", "#17BEC6DC");
                Controls("#1E2230", "#3A4058", "#1E2230", "#262A38", "#111319", "#EDEFF5", "#F5B942", "#14BEC6DC", "#2E3346", "#0D0F14", "#232736",
                    "#FFFFFF", "#2E3346", "#2E3346", "#0DFFFFFF", "#14F5B942", "#80F5B942", "#1FF5B942", "#0AF5B942", "#3DF5B942", "#14F5B942", "#FFD27A", "#4DF5B942");
                S("Bg", "#0A0B10"); S("Card", "#111319"); S("Card2", "#171A22"); S("Line", "#232736"); S("LineStrong", "#2E3346");
                S("Ink", "#EDEFF5"); S("Muted", "#A2A8BA"); S("Faint", "#6D7387");
                S("Gold", "#F5B942"); S("Violet", "#B39BFF"); S("Mint", "#3DD68C"); S("Accent", "#F5B942"); S("AccentDim", "#1E2230");
                S("Good", "#3DD68C"); S("Warn", "#F7A072"); S("Bad", "#F26D6D"); S("Slot", "#0D0F14");
                S("RowHead", "#0F1117"); S("RowHover", "#171A22"); S("Mark", "#1A1D27");
                R("HeroBg", "#171A23", "#111319", "#111319"); S("HeroLine", "#2E3346"); S("Inset", "#38000000"); C("SlotColor", "#0D0F14");
                S("CommonInk", "#D9DDE8"); S("GradeRare", "#62D98B"); S("GradeLegend", "#5AA6FF"); S("GradeUnique", "#F3D35E"); S("GradeEpic", "#B48BFF"); S("BtnPrimaryInk", "#1D1300");
                G("BtnPrimary", "#F7C257", "#F2B23A"); G("BtnPrimaryHover", "#FFD27A", "#FBC966"); G("BtnStop", "#F26D6D", "#E35D5D");
                break;
        }
        return d;
    }

    static Brush Frozen(Brush b) { b.Freeze(); return b; }
}
