using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace KinahLedger.Ui;

/// <summary>
/// The hero in a themed look (1.0; Steven, 2026-11-17: the skins must look like the themes, "deep work not a color
/// change"): the hour and the ring give way to the theme's own overlay art, drawn large from the same figures the overlay
/// over the game shows (Ui/Themes): Sticker's pill, ring and session tag for Wacky, the Scorebug's broadcast bar for
/// Tactical, the VHS OSD for Glitch, or the overlay look in use when it is one of the theme's. Still: no motion here.
/// The place, the chase, the figures line and Start run stay as they are.
/// </summary>
public partial class MainWindow
{
    HudThemeLook? _heroLook;
    string? _heroLookKey;

    /// <summary>The theme's look the hero draws: the overlay's own when it is the theme's, else the theme's signature one.</summary>
    string HeroLookKey(string theme) => HudThemes.Of(HudLook) == theme ? HudLook : theme switch
    {
        HudThemes.Wacky => HudThemes.Sticker,
        HudThemes.Tactical => HudThemes.Scorebug,
        _ => HudThemes.Vhs,
    };

    const double HeroLookScale = 1.3;

    /// <summary>Draws the hero's figure as the theme's art (or the plain figure and ring in Night, Abyss and Daylight).</summary>
    void FillThemeHero()
    {
        bool themed = Skins.Themed;
        HeroRateRow.Visibility = themed ? Visibility.Collapsed : Visibility.Visible;
        RingHost.Visibility = themed ? Visibility.Collapsed : Visibility.Visible;
        HeroThemeStage.Visibility = themed ? Visibility.Visible : Visibility.Collapsed;
        if (!themed) { HeroThemeStage.Child = null; _heroLook = null; _heroLookKey = null; return; }
        var key = HeroLookKey(Skins.Current);
        try
        {
            if (_heroLook == null || _heroLookKey != key)
            {
                _heroLook = HudThemes.Make(key, still: true);
                _heroLookKey = key;
                if (_heroLook == null) { HeroThemeStage.Child = null; return; }
                _heroLook.SetOpacity(1);
                var root = _heroLook.Root;
                var i = _heroLook.Inset;
                // the look's clear room for its effects cropped by a wrapper (a look may keep that room in its own Margin: Scorebug)
                var inner = new Border { Child = root, Margin = new Thickness(-i.Left, -i.Top, -i.Right, -i.Bottom) };
                HeroThemeStage.Child = new Border { Child = inner, ClipToBounds = true, LayoutTransform = new ScaleTransform(HeroLookScale, HeroLookScale), HorizontalAlignment = HorizontalAlignment.Left };
            }
            HudRunValues(); HudFeed();
            var values = new Dictionary<string, string>(_hudValues) { ["show"] = "1" };
            _heroLook?.Update(values);
        }
        catch (Exception ex)
        {
            CrashLog.Write("themed hero", ex);
            HeroThemeStage.Visibility = Visibility.Collapsed; HeroRateRow.Visibility = Visibility.Visible; RingHost.Visibility = Visibility.Visible;
        }
    }
}
