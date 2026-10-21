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
        switch (key)
        {
            case "abyss":
                S("Bg", "#0B0A14"); S("Card", "#151326"); S("Card2", "#1D1A33"); S("Line", "#2A2546"); S("LineStrong", "#443C6E");
                S("Ink", "#EEEAFF"); S("Muted", "#A99FC9"); S("Faint", "#6F668F");
                S("Gold", "#F2C97D"); S("Violet", "#D2B8FF"); S("Mint", "#8EE3D6"); S("Accent", "#9F7BFF"); S("AccentDim", "#2A1F4F");
                S("Good", "#58D39A"); S("Warn", "#F3A65B"); S("Bad", "#FF7A86");
                S("RowHead", "#19162D"); S("RowHover", "#1F1B38"); S("Mark", "#1C2740"); S("Slot", "#100E1E");
                R("HeroBg", "#2E2440", "#1D1A33", "#151326"); S("HeroLine", "#55F2C97D"); S("Inset", "#38000000"); C("SlotColor", "#100E1E");
                S("CommonInk", "#D9D4EE"); S("GradeRare", "#62D98B"); S("GradeLegend", "#5AA6FF"); S("GradeUnique", "#F3D35E"); S("GradeEpic", "#C8A8FF"); S("BtnPrimaryInk", "#FFFFFF");
                G("BtnPrimary", "#7E5BEF", "#5A3DC2"); G("BtnPrimaryHover", "#8F6EFF", "#6648D6"); G("BtnStop", "#C44E4E", "#8E3434");
                break;
            case "daylight":
                S("Bg", "#F3F5F9"); S("Card", "#FFFFFF"); S("Card2", "#EDF1F7"); S("Line", "#D9E0EA"); S("LineStrong", "#B9C5D6");
                S("Ink", "#14202F"); S("Muted", "#5B6B82"); S("Faint", "#8593A8");
                S("Gold", "#9A6B10"); S("Violet", "#5C46C9"); S("Mint", "#1F8A6B"); S("Accent", "#1F8FCB"); S("AccentDim", "#D7ECF8");
                S("Good", "#1E8E57"); S("Warn", "#B86A14"); S("Bad", "#C2463A");
                S("RowHead", "#E8EDF4"); S("RowHover", "#F2F5F9"); S("Mark", "#E3EBF5"); S("Slot", "#E9EEF5");
                G("BtnPrimary", "#F6CB72", "#D9952F"); G("BtnPrimaryHover", "#F9D78E", "#E0A03C"); G("BtnStop", "#F6CB72", "#D9952F");
                R("HeroBg", "#FFF0D2", "#FFFFFF", "#FFFFFF"); S("HeroLine", "#66D9952F"); S("Inset", "#F4F6FA"); C("SlotColor", "#F1F4F9");
                S("CommonInk", "#2A3444"); S("GradeRare", "#1E8E57"); S("GradeLegend", "#1F6FCB"); S("GradeUnique", "#8F6F06"); S("GradeEpic", "#6E46C9"); S("BtnPrimaryInk", "#261702");
                break;
            default:   // night: the base theme's own values, listed so a switch back is complete
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
