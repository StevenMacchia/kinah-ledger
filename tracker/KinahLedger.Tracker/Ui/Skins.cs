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
        new("night", "Night", true, "the site's palette: deep blue, gold and mint"),
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
        switch (key)
        {
            case "abyss":
                S("Bg", "#0B0A14"); S("Card", "#151326"); S("Card2", "#1D1A33"); S("Line", "#2A2546"); S("LineStrong", "#443C6E");
                S("Ink", "#EEEAFF"); S("Muted", "#A99FC9"); S("Faint", "#6F668F");
                S("Gold", "#F2C97D"); S("Violet", "#D2B8FF"); S("Mint", "#8EE3D6"); S("Accent", "#9F7BFF"); S("AccentDim", "#2A1F4F");
                S("Good", "#58D39A"); S("Warn", "#F3A65B"); S("Bad", "#FF7A86");
                S("RowHead", "#19162D"); S("RowHover", "#1F1B38"); S("Mark", "#1C2740");
                G("BtnPrimary", "#7E5BEF", "#5A3DC2"); G("BtnPrimaryHover", "#8F6EFF", "#6648D6"); G("BtnStop", "#C44E4E", "#8E3434");
                break;
            case "daylight":
                S("Bg", "#F3F5F9"); S("Card", "#FFFFFF"); S("Card2", "#EDF1F7"); S("Line", "#D9E0EA"); S("LineStrong", "#B9C5D6");
                S("Ink", "#14202F"); S("Muted", "#5B6B82"); S("Faint", "#8593A8");
                S("Gold", "#9A6B10"); S("Violet", "#5C46C9"); S("Mint", "#1F8A6B"); S("Accent", "#1F8FCB"); S("AccentDim", "#D7ECF8");
                S("Good", "#1E8E57"); S("Warn", "#B86A14"); S("Bad", "#C2463A");
                S("RowHead", "#E8EDF4"); S("RowHover", "#F2F5F9"); S("Mark", "#E3EBF5");
                G("BtnPrimary", "#3C90E8", "#2766B8"); G("BtnPrimaryHover", "#4C9FF4", "#2F74CC"); G("BtnStop", "#D45A5A", "#A33C3C");
                break;
            default:   // night: the base theme's own values, listed so a switch back is complete
                S("Bg", "#0A0F18"); S("Card", "#111A28"); S("Card2", "#162236"); S("Line", "#232F45"); S("LineStrong", "#3A4E70");
                S("Ink", "#E9EFF7"); S("Muted", "#93A3B8"); S("Faint", "#62718A");
                S("Gold", "#F1D08C"); S("Violet", "#C5BBFF"); S("Mint", "#9BE8D3"); S("Accent", "#4CC3F5"); S("AccentDim", "#163650");
                S("Good", "#4ED08C"); S("Warn", "#F4A65A"); S("Bad", "#FF7B6B");
                S("RowHead", "#101826"); S("RowHover", "#121C2E"); S("Mark", "#1A2740");
                G("BtnPrimary", "#3C90E8", "#2766B8"); G("BtnPrimaryHover", "#4C9FF4", "#2F74CC"); G("BtnStop", "#C44E4E", "#8E3434");
                break;
        }
        return d;
    }

    static Brush Frozen(Brush b) { b.Freeze(); return b; }
}
