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
        new("night", "Night", true, "the site's palette: indigo night, Kinah gold and aether teal"),
        new("abyss", "Abyss", true, "violet and teal on near-black"),
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
        switch (key)
        {
            case "abyss":
                Controls("#221D3D", "#5A4E8C", "#241F40", "#2D2752", "#08FFFFFF", "#24F2C97D", "#E9B45A", "#1A8EE3D6", "#738EE3D6", "#B3100E1E", "#2A2546",
                    "#FFFFFF", "#47D9D4EE", "#4A4570", "#08FFFFFF", "#14F2C97D", "#80F2C97D", "#29F2C97D", "#0DF2C97D", "#47E9B45A", "#14F2C97D", "#F2C97D", "#4DF2C97D");
                S("Bg", "#0B0A14"); S("Card", "#151326"); S("Card2", "#1D1A33"); S("Line", "#2A2546"); S("LineStrong", "#443C6E");
                S("Ink", "#EEEAFF"); S("Muted", "#A99FC9"); S("Faint", "#8A81AC");
                S("Gold", "#F2C97D"); S("Violet", "#D2B8FF"); S("Mint", "#8EE3D6"); S("Accent", "#9F7BFF"); S("AccentDim", "#2A1F4F");
                S("Good", "#58D39A"); S("Warn", "#F3A65B"); S("Bad", "#FF7A86");
                S("RowHead", "#19162D"); S("RowHover", "#1F1B38"); S("Mark", "#1C2740"); S("Slot", "#100E1E");
                R("HeroBg", "#2E2440", "#1D1A33", "#151326"); S("HeroLine", "#55F2C97D"); S("Inset", "#38000000"); C("SlotColor", "#100E1E");
                S("CommonInk", "#D9D4EE"); S("GradeRare", "#62D98B"); S("GradeLegend", "#5AA6FF"); S("GradeUnique", "#F3D35E"); S("GradeEpic", "#C8A8FF"); S("BtnPrimaryInk", "#FFFFFF");
                G("BtnPrimary", "#7E5BEF", "#5A3DC2"); G("BtnPrimaryHover", "#8F6EFF", "#6648D6"); G("BtnStop", "#C44E4E", "#8E3434");
                break;
            case "daylight":
                Controls("#E6ECF4", "#9FB0C6", "#E8EEF6", "#DCE6F3", "#E9EEF5", "#FFFFFF", "#9A6B10", "#141F8A6B", "#661F8A6B", "#F1F4F9", "#D9E0EA",
                    "#14202F", "#8FA3BC", "#B9C5D6", "#0A14202F", "#1AD9952F", "#99B07A1A", "#FFF3DA", "#FFFAF0", "#4DB07A1A", "#FFF2DC", "#8A4F00", "#59B86A14");
                S("Bg", "#F3F5F9"); S("Card", "#FFFFFF"); S("Card2", "#EDF1F7"); S("Line", "#D9E0EA"); S("LineStrong", "#B9C5D6");
                S("Ink", "#14202F"); S("Muted", "#5B6B82"); S("Faint", "#637287");
                S("Gold", "#9A6B10"); S("Violet", "#5C46C9"); S("Mint", "#1F8A6B"); S("Accent", "#1F8FCB"); S("AccentDim", "#D7ECF8");
                S("Good", "#1E8E57"); S("Warn", "#B86A14"); S("Bad", "#C2463A");
                S("RowHead", "#E8EDF4"); S("RowHover", "#F2F5F9"); S("Mark", "#E3EBF5"); S("Slot", "#E9EEF5");
                G("BtnPrimary", "#F6CB72", "#D9952F"); G("BtnPrimaryHover", "#F9D78E", "#E0A03C"); G("BtnStop", "#F6CB72", "#D9952F");
                R("HeroBg", "#FFF0D2", "#FFFFFF", "#FFFFFF"); S("HeroLine", "#66D9952F"); S("Inset", "#F4F6FA"); C("SlotColor", "#F1F4F9");
                S("CommonInk", "#2A3444"); S("GradeRare", "#1E8E57"); S("GradeLegend", "#1F6FCB"); S("GradeUnique", "#8F6F06"); S("GradeEpic", "#6E46C9"); S("BtnPrimaryInk", "#261702");
                break;
            default:   // night: the base theme's own values, listed so a switch back is complete
                Controls("#1D2346", "#4A548F", "#1E2D45", "#24375A", "#08FFFFFF", "#24EBAE4C", "#E9B45A", "#1A5ED8CB", "#735ED8CB", "#B30B0E1E", "#2A3154",
                    "#FFFFFF", "#47CDD3E2", "#4A5277", "#08FFFFFF", "#14F8D598", "#80F8D598", "#29F8D598", "#0DF8D598", "#47E9B45A", "#14F8D598", "#F8D598", "#4DF8D598");
                S("Bg", "#090B15"); S("Card", "#12162D"); S("Card2", "#171C38"); S("Line", "#22284A"); S("LineStrong", "#323A66");
                S("Ink", "#EEECF6"); S("Muted", "#B3B7D3"); S("Faint", "#7D84A8");
                S("Gold", "#F8D598"); S("Violet", "#B48BFF"); S("Mint", "#5ED8CB"); S("Accent", "#5ED8CB"); S("AccentDim", "#11303A");
                S("Good", "#62D98B"); S("Warn", "#F2A35E"); S("Bad", "#FF8A7A"); S("Slot", "#0B0E1E");
                S("RowHead", "#0E1124"); S("RowHover", "#171C38"); S("Mark", "#1A1F3D");
                R("HeroBg", "#3A3323", "#171C38", "#12162D"); S("HeroLine", "#52EBAE4C"); S("Inset", "#38000000"); C("SlotColor", "#0B0E1E");
                S("CommonInk", "#CDD3E2"); S("GradeRare", "#62D98B"); S("GradeLegend", "#5AA6FF"); S("GradeUnique", "#F3D35E"); S("GradeEpic", "#B48BFF"); S("BtnPrimaryInk", "#261702");
                G("BtnPrimary", "#F8D598", "#D9952F"); G("BtnPrimaryHover", "#FBE0AE", "#E0A03C"); G("BtnStop", "#F8D598", "#D9952F");
                break;
        }
        return d;
    }

    static Brush Frozen(Brush b) { b.Freeze(); return b; }
}
