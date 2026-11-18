using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using static KinahLedger.Ui.HudKit;

namespace KinahLedger.Ui;

/// <summary>
/// The session's best drop and the last three pickups as item tiles in one row (Steven, 2026-11-18: "i love when the icons
/// appear of the last 3 so having like the last 3 or best drop + last three in most of them would be ideal"), for a look's
/// free room beside its big figure. The best drop first with a small gold star on its corner and a thin rule after it (left
/// out while it is one of the three); the newest of the three lit for a few seconds after the pickup. Each tile's name and
/// worth on hover. Nothing before the first pickup. Every look but the Buddy and Pop (which have their own) uses this one,
/// sized and tinted to fit.
/// </summary>
static class PickStrip
{
    /// <summary>How a look wears the strip.</summary>
    /// <param name="Size">A tile's side, DIPs.</param>
    /// <param name="Gap">The air between tiles.</param>
    /// <param name="Best">The best drop first (false: the last three alone, for a look with little room).</param>
    /// <param name="Rule">The rule between the best drop and the three (the look's hairline); a soft white by default.</param>
    /// <param name="Star">The best drop's star (the look's accent); the gold by default.</param>
    public sealed record Look(double Size = 26, double Gap = 4, bool Best = true, Brush? Rule = null, Brush? Star = null);

    /// <summary>
    /// Settings › Buddy › More › Pickup icons (Steven, 2026-11-18: "allow users to toggle them off in settings but still
    /// track for the site"): off, no look draws the strip (nor the Buddy its tiles). What is drawn only; every pickup is still
    /// counted, saved and sent.
    /// </summary>
    public static bool Shown { get; set; } = true;

    /// <summary>How long the newest tile stays lit after its pickup.</summary>
    public static readonly TimeSpan Lit = TimeSpan.FromSeconds(5);

    /// <summary>A signature of what the strip shows (the looks redraw it only when this changes).</summary>
    public static string Sig(IReadOnlyDictionary<string, string> v, Look look) =>
        (Shown ? "" : "off|") + string.Join(",", Picks(v).Take(3).Select(p => p.Id + p.Count + p.Worth + p.Icon)) + "|" + (look.Best ? Top(v)?.Id + Top(v)?.Icon : "");

    /// <summary>The strip, or null before the first pickup (or when the values carry none, as in the Abyss).</summary>
    public static FrameworkElement? Of(IReadOnlyDictionary<string, string> v, Look look)
    {
        if (!Shown) return null;
        var picks = Picks(v).Take(3).ToList();
        var top = look.Best ? Top(v) : null;
        if (top != null && picks.Any(p => p.Name == top.Name)) top = null;   // the best drop is one of the three: shown once
        if (picks.Count == 0 && top == null) return null;
        var row = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Tag = "pick-strip" };
        if (top != null)
        {
            var best = new Grid { ToolTip = Tip(top, L.T("Best drop")), Tag = "pick-best" };
            best.Children.Add(Tile(top, look.Size));
            // the star on the tile's top-left corner, on a canvas so it hangs past the edge instead of being cut
            var star = new TextBlock { Text = "★", FontSize = Math.Max(9, look.Size * .38), FontWeight = FontWeights.Bold, Foreground = look.Star ?? Gold, IsHitTestVisible = false,
                Effect = new System.Windows.Media.Effects.DropShadowEffect { Color = Colors.Black, BlurRadius = 3, ShadowDepth = 0, Opacity = .9 } };
            Canvas.SetLeft(star, -3); Canvas.SetTop(star, -5);
            best.Children.Add(new Canvas { Width = look.Size, Height = look.Size, IsHitTestVisible = false, Children = { star } });
            row.Children.Add(best);
            if (picks.Count > 0)
                row.Children.Add(new Border { Width = 1, Height = look.Size * .62, Margin = new Thickness(look.Gap + 2, 0, look.Gap + 2, 0), VerticalAlignment = VerticalAlignment.Center,
                    Background = look.Rule ?? new SolidColorBrush(Color.FromArgb(0x40, 0xFF, 0xFF, 0xFF)) });
        }
        for (int i = 0; i < picks.Count; i++)
        {
            var p = picks[i];
            var cell = new Grid { ToolTip = Tip(p, ""), Tag = "pick-tile", Margin = new Thickness(i == 0 ? 0 : look.Gap, 0, 0, 0) };
            cell.Children.Add(Tile(p, look.Size));
            if (i == 0) Light(cell, p, look.Size);
            row.Children.Add(cell);
        }
        return row;
    }

    /// <summary>The strip's width for a look (best + rule + three), to keep room for it: a look's crowding rule reads this.</summary>
    public static double Width(Look look, int tiles = 3, bool best = true) =>
        tiles * look.Size + Math.Max(0, tiles - 1) * look.Gap + (best ? look.Size + 2 * look.Gap + 5 : 0);

    /// <summary>The newest tile's light: a gold ring with a soft glow fading over <see cref="Lit"/> from the pickup.</summary>
    static void Light(Grid cell, Pick p, double size)
    {
        var left = Lit - (DateTime.UtcNow - p.At);
        if (p.At == DateTime.MinValue || left <= TimeSpan.Zero || Motion.Still) return;
        var ring = Glow(Rgb(GoldC), 5, 12, .85, 1.5);
        ring.Width = size; ring.Height = size;
        cell.Children.Add(ring);
        var fade = new DoubleAnimation(1, 0, new Duration(left)) { EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseIn } };
        ring.BeginAnimation(UIElement.OpacityProperty, fade);
    }

    /// <summary>A tile's hover: its name and count, its worth, and a label ("Best drop") when it has one.</summary>
    static string Tip(Pick p, string label)
    {
        var s = (label.Length > 0 ? label + ": " : "") + p.Name + (p.Count.Length > 0 ? " " + p.Count : "");
        return p.Worth.Length > 0 ? s + " · " + p.Worth : s;
    }
}
