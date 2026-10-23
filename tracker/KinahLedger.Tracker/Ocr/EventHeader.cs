using System.Drawing;
using System.Text.RegularExpressions;

namespace KinahLedger.Ocr;

/// <summary>
/// The event's header under the minimap: its name, then its clock ("Hidden Lugi / Until Event Start / 01:12", then
/// "Hidden Lugi / 02:20 / Role-Specific Rule Description"), seen on Steven's 5120×2160 pictures of 2026-10-23. A second
/// witness beside the minimap's title: it confirms or corrects the event, and names it where the minimap can't be read.
/// Only an event on the list counts, and only with its clock or its event words on the next lines, so the Festival Shop's
/// list of games (which also shows "Jump Jump") never does; the session doesn't read it while a reward screen is open.
/// </summary>
public static class EventHeader
{
    /// <summary>
    /// The header's place: under the minimap at the right edge. The game draws its interface to the window's height, so
    /// the box is measured in heights from the right edge (on 5120×2160: x 4400 to 5120, y 580 to 980).
    /// </summary>
    public static Rectangle Region(Rectangle game)
    {
        int w = Math.Min(game.Width / 3, (int)(game.Height * 0.36));
        return new Rectangle(game.Right - w, game.Top + (int)(game.Height * 0.26), w, (int)(game.Height * 0.20));
    }

    static readonly Regex Clock = new(@"\b\d{1,2}\s*[:.]\s*\d{2}\b", RegexOptions.Compiled);
    static readonly Regex Words = new(@"\b(?:until|event|start|rule|remaining|time|description|round|wave|participants?)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>The event the header names, from the lines read in <see cref="Region"/> (top first), or null.</summary>
    public static string? Find(IReadOnlyList<(int Top, string Text)> lines)
    {
        var sorted = lines.OrderBy(l => l.Top).ToList();
        for (int i = 0; i < sorted.Count; i++)
        {
            // the header's icon reads as a stray letter or mark in front of the name
            var text = Regex.Replace(sorted[i].Text, @"^[^A-Za-z]+|^[a-z]\s+", "").Trim();
            if (text.Length < 5) continue;
            var name = ZoneLocator.MatchEvent(text);
            if (name == null) continue;
            // the open world's list of upcoming games sits in the same place: a countdown with "Enter" beside it
            if (sorted.Skip(i + 1).Take(2).Any(l => Regex.IsMatch(l.Text, @"\bEnter\b", RegexOptions.IgnoreCase))) continue;
            for (int j = i + 1; j < Math.Min(sorted.Count, i + 3); j++)
                if (Clock.IsMatch(sorted[j].Text) || Words.IsMatch(sorted[j].Text)) return name;
        }
        return null;
    }

    /// <summary>The header read from a picture of its region (<paramref name="origin"/>: where it sits on the screen).</summary>
    public static async Task<(string? Name, List<string> Lines)> ReadAsync(Bitmap region, Point origin)
    {
        var lines = await ScreenReader.ReadBoxesAsync(region, origin, 2);
        var found = Find(lines.Select(l => (l.Box.Top, l.Text)).ToList());
        if (found == null)
        {
            // white letters over bright scenery: the contrast-boosted read finds what the plain one can't
            var boosted = await ScreenReader.ReadBoxesAsync(region, origin, 2, boost: true);
            found = Find(boosted.Select(l => (l.Box.Top, l.Text)).ToList());
            lines.AddRange(boosted);
        }
        return (found, lines.Select(l => l.Text).ToList());
    }
}
