using System.Windows;
using System.Windows.Controls;

namespace KinahLedger.Ui;

/// <summary>
/// Options › In game › Theme (Steven, 2026-11-16: "let us choose a theme and then let us choose from that theme"): a chip
/// for each theme over the look thumbnails, which show the theme picked. Picking a theme only shows its looks; picking a
/// look picks it (and its theme), on the page's preview-then-Save model like every look.
/// </summary>
public partial class OptionsWindow
{
    /// <summary>The theme whose looks the thumbnails show: the look in use's until another is picked.</summary>
    string? _hudTheme;

    /// <summary>The theme shown for the look <paramref name="current"/>, with its chips drawn.</summary>
    string HudThemeShown(string current)
    {
        _hudTheme ??= HudThemes.Of(current);
        HudThemeHost.Children.Clear();
        foreach (var t in HudThemes.All)
        {
            var key = t.Key;
            var chip = _host.Chip(L.T(t.Name), key == _hudTheme, () => PickHudTheme(key), L.T(t.Hint));
            chip.Tag = "theme:" + key;
            HudThemeHost.Children.Add(chip);
        }
        // the window's skin to match (1.0): tried on like a theme chip under General, kept by Save, put back by Cancel
        // under the row's title, not after the chips: there it took a second line, and the page no longer fit (1.0.8)
        var match = _host.MatchAppLink(_hudTheme, _edit.Skin, skin => { Edit(_edit with { Skin = skin }, previewSkin: true); HudThemeShown(_edit.HudLook); });
        if (match is TextBlock mt) { mt.Margin = new Thickness(0, 2, 0, 0); mt.FontSize = 12; mt.TextWrapping = TextWrapping.Wrap; }
        HudThemeMatchHost.Content = match;
        return _hudTheme;
    }

    void PickHudTheme(string theme)
    {
        if (theme == _hudTheme) return;
        _hudTheme = theme;
        BuildHudLooks(_edit.HudLook);
        DrawParserLooks();
    }

    /// <summary>For the options self-test: a theme's chip clicked, and the theme shown.</summary>
    internal void PickHudThemeForTest(string theme) => PickHudTheme(theme);
    internal string? HudThemeForTest => _hudTheme;

    /// <summary>For the options self-test: the "Match the app" link after the theme chips, clicked; false when none is shown.</summary>
    internal bool MatchAppForTest()
    {
        if (HudThemeMatchHost.Content is not TextBlock { Tag: string s } t || !s.StartsWith("match:")) return false;
        var link = t.Inlines.OfType<System.Windows.Documents.Hyperlink>().First();
        link.RaiseEvent(new RoutedEventArgs(System.Windows.Documents.Hyperlink.ClickEvent));
        return true;
    }
}
