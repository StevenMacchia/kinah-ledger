using System.Drawing;

namespace KinahLedger.Session;

/// <summary>
/// Where the game's parts sit, as fractions of the game window, keyed by the window's size and the size of the game's
/// text (2026-10-23, "layout profiles"). Once a tracker finds the chat (or the player chooses it), and once the loot
/// list and the crafting panel show where they were looked for, that is reported with the next accuracy report. A new
/// install whose game window has the same size asks the site where those trackers found the chat, and looks there first
/// when its own search finds nothing (the frameless chat that fades out has no input box to find it by).
/// </summary>
public static class LayoutProfile
{
    /// <summary>The area as x, y, width, height fractions of the game window.</summary>
    public static double[] Fractions(Rectangle area, Rectangle game) => new[]
    {
        Math.Round((area.X - game.X) / (double)game.Width, 4), Math.Round((area.Y - game.Y) / (double)game.Height, 4),
        Math.Round(area.Width / (double)game.Width, 4), Math.Round(area.Height / (double)game.Height, 4),
    };

    /// <summary>Fractions back into screen pixels on this game window.</summary>
    public static Rectangle ToRect(double[] f, Rectangle game) => new(
        game.X + (int)Math.Round(f[0] * game.Width), game.Y + (int)Math.Round(f[1] * game.Height),
        Math.Max(1, (int)Math.Round(f[2] * game.Width)), Math.Max(1, (int)Math.Round(f[3] * game.Height)));

    /// <summary>
    /// A chat box that could be a chat: inside the window, in its left half and lower part, not a sliver and not most of
    /// the screen. Anything else from the site is ignored.
    /// </summary>
    public static bool Plausible(double[]? f) =>
        f is { Length: 4 } && f.All(v => v >= 0 && v <= 1)
        && f[0] + f[2] <= 1.001 && f[1] + f[3] <= 1.001
        && f[0] < 0.5 && f[1] + f[3] > 0.45
        && f[2] >= 0.08 && f[2] <= 0.6 && f[3] >= 0.08 && f[3] <= 0.6;

    /// <summary>
    /// The UI's text size as a key: the chat's measured text height scaled to a 1080-pixel-tall window, rounded. Two
    /// screens with the same size and the same in-game UI scale give the same number; 0 while it isn't measured.
    /// </summary>
    public static int UiKey(double? textPx, int gameHeight) =>
        textPx is double px && px > 0 && gameHeight > 0 ? (int)Math.Round(px * 1080.0 / gameHeight) : 0;

    /// <summary>The hint closest to this screen's text size (or the most reported one while the size isn't known).</summary>
    public static Upload.AccuracyClient.ChatHint? Best(IEnumerable<Upload.AccuracyClient.ChatHint> hints, int ui) =>
        ui > 0 ? hints.OrderBy(h => h.Ui > 0 ? Math.Abs(h.Ui - ui) : 99).ThenByDescending(h => h.Installs).FirstOrDefault()
               : hints.OrderByDescending(h => h.Installs).FirstOrDefault();
}
