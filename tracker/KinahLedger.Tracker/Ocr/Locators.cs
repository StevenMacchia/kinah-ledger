using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;

namespace KinahLedger.Ocr;

/// <summary>Where the game window is, in screen pixels. Borderless and windowed modes both report a real rectangle.</summary>
public static class GameWindow
{
    [StructLayout(LayoutKind.Sequential)] struct RECT { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);
    [DllImport("user32.dll")] static extern bool IsIconic(IntPtr hWnd);

    public static Rectangle? Find(IEnumerable<string> processHints)
    {
        var hints = processHints.Select(h => h.ToLowerInvariant()).ToArray();
        foreach (var p in Process.GetProcesses())
        {
            try
            {
                if (!hints.Any(h => p.ProcessName.ToLowerInvariant().Contains(h))) continue;
                var h = p.MainWindowHandle;
                if (h == IntPtr.Zero || IsIconic(h) || !GetWindowRect(h, out var r)) continue;
                var rect = Rectangle.FromLTRB(r.Left, r.Top, r.Right, r.Bottom);
                if (rect.Width >= 640 && rect.Height >= 360) return rect;
            }
            catch (Exception) { /* process went away */ }
        }
        return null;
    }
}

/// <summary>
/// Finds the chat without anyone dragging a box: the chat's lines sit directly above the "Enter your message" input
/// at the bottom-left of the game, so reading that corner once gives both the position and the line height.
/// </summary>
public static class ChatLocator
{
    static readonly Regex Input = new(@"enter\s+your\s+messag|your\s+message", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>The bottom-left quarter that holds the chat and its input box.</summary>
    public static Rectangle Corner(Rectangle game) => new(game.Left, game.Top + game.Height / 2, game.Width / 2, game.Height / 2);

    public static async Task<(Rectangle? region, string note)> DetectAsync(Rectangle game)
    {
        var corner = Corner(game);
        var lines = await ScreenReader.ReadBoxesAsync(corner, 1);
        var input = lines.FirstOrDefault(l => Input.IsMatch(l.Text));
        if (input == null) return (null, $"couldn't see the chat input box in the bottom-left corner ({lines.Count} lines read)");

        // the input row starts with the channel tab ("Common"); the chat's left edge is there, not where the placeholder text starts
        int rowMid = input.Box.Top + input.Box.Height / 2;
        var row = lines.Where(l => l.Box.Top <= rowMid && l.Box.Bottom >= rowMid && l.Box.Left <= input.Box.Left).ToList();
        int rowLeft = row.Count > 0 ? row.Min(l => l.Box.Left) : input.Box.Left;

        var above = lines
            .Where(l => l != input && l.Box.Bottom <= input.Box.Top + 4
                        && l.Box.Left >= rowLeft - 40 && l.Box.Left <= rowLeft + Math.Max(300, input.Box.Width)
                        && input.Box.Top - l.Box.Bottom < game.Height * 0.4)
            .OrderByDescending(l => l.Box.Top).ToList();

        int lineH = above.Count > 0 ? (int)Math.Round(above.Take(8).Average(l => l.Box.Height) * 1.45) : (int)Math.Round(input.Box.Height * 1.2);
        lineH = Math.Max(lineH, 14);
        int left = Math.Max(game.Left, Math.Min(rowLeft, above.Count > 0 ? above.Min(l => l.Box.Left) : rowLeft) - 12);
        int right = Math.Min(game.Left + (int)(game.Width * 0.55), Math.Max(above.Count > 0 ? above.Max(l => l.Box.Right) : 0, input.Box.Right) + 40);
        int bottom = input.Box.Top - 4;
        int top = Math.Max(game.Top, bottom - lineH * 12);            // room for twelve lines: the chat grows upward
        var region = Rectangle.FromLTRB(left, top, right, bottom);
        return (region, $"chat found above the input box: {above.Count} line(s) visible, ~{lineH}px per line");
    }
}

/// <summary>Reads the zone name shown above the minimap at the top-right, so sessions know where they happened.</summary>
public static class ZoneLocator
{
    // what a zone label looks like: a few capitalized words, no digits, no brackets
    static readonly Regex Shape = new(@"^[A-Z][A-Za-z'’\-]+(?:\s+(?:of|the|and|[A-Z][A-Za-z'’\-]+)){1,5}$", RegexOptions.Compiled);
    static readonly string[] Blocklist = { "Change", "Show", "Must", "Waiting", "Evasion", "Sprint", "Jump", "Mount", "Rest", "Auto", "Hide", "Ground", "Fly", "Level", "Quest", "View", "Menu", "Map", "Options", "Settings", "Inventory", "Chat", "Party", "Legion", "Friends", "Mail", "Shop" };
    // words that appear in place names; a candidate needs one of these or a known-name match, so UI labels never pass
    static readonly string[] PlaceWords = { "Valley", "Highland", "Highlands", "Canyon", "Hill", "Hills", "Sanctum", "Ruins", "Garden", "Forest", "Plains", "Plain", "Cave", "Caves", "Cavern", "Fortress", "Village", "Lake", "Coast", "Shore", "Pass", "Road", "Gate", "Temple", "Swamp", "Marsh", "Desert", "Island", "Isle", "Abyss", "Outpost", "Camp", "Field", "Fields", "Ridge", "Peak", "Mountain", "Mountains", "Basin", "Plateau", "Woods", "Grove", "Gorge", "Hollow", "Ravine", "Cliff", "Cliffs", "Falls", "Spring", "Springs", "River", "Bridge", "Tower", "Citadel", "Stronghold", "Fort", "Shrine", "Sanctuary", "Crypt", "Tomb", "Mine", "Mines", "Quarry", "Den", "Nest", "Lair", "Harbor", "Port", "Bay", "Oasis", "Steppe", "Wasteland", "Tundra", "Crater", "Dunes", "Lowlands", "Vale", "Meadow", "Prairie", "Orchard", "Farm", "Mill", "Crossroads", "Square", "District", "Town", "City", "Palace", "Castle", "Keep", "Hall", "Prison", "Arena", "Academy", "Library", "Observatory", "Laboratory", "Workshop", "Foundry", "Forge", "Market", "Cemetery", "Graveyard", "Catacombs", "Altar", "Portal", "Rift", "Gateway", "Frontier", "Border", "Coastline", "Beach", "Glade", "Thicket", "Jungle", "Volcano", "Furnace", "Wall", "Walls", "Rampart", "Battlefield", "Front", "Encampment", "Garrison", "Barracks", "Landing", "Ascent", "Descent", "Depths", "Heights", "Summit", "Hideout", "Refuge", "Haven", "Hamlet", "Settlement", "Colony", "Waystation", "Station" };

    public static readonly string[] Known =
    {
        "Western Altamia Highland", "Eastern Altamia Highland", "Southern Altamia Highland", "Altamia Canyon", "Contaminated Sanctum", "Rock Tomb Hill",
        "Garden of the Illusion God", "Altamia Highland Ruins", "Western Cantas Valley", "Eastern Cantas Valley", "Cantas Valley"
    };

    /// <summary>The top-right corner that holds the minimap and its title.</summary>
    public static Rectangle Strip(Rectangle game) => new(game.Right - (int)(game.Width * 0.24), game.Top, (int)(game.Width * 0.24), (int)(game.Height * 0.14));

    public static async Task<(string? zone, string note)> DetectAsync(Rectangle game)
    {
        var strip = Strip(game);
        var lines = await ScreenReader.ReadBoxesAsync(strip, 2);
        var candidates = lines
            .Select(l => (l, text: Clean(l.Text)))
            .Where(x => x.text.Length >= 6 && x.text.Length <= 40 && Shape.IsMatch(x.text) && !Blocklist.Any(b => x.text.StartsWith(b + " ") || x.text == b))
            .OrderBy(x => x.l.Box.Top).ToList();
        if (candidates.Count == 0) return (null, $"no zone-shaped text in the top-right corner ({lines.Count} lines read)");
        // a known name wins (even misread), then a name with a place word; anything else is some other label and is ignored
        foreach (var c in candidates)
        {
            var hit = Known.FirstOrDefault(k => Edit(k.ToLowerInvariant(), c.text.ToLowerInvariant()) <= Math.Max(1, k.Length / 7));
            if (hit != null) return (hit, "zone matched a known name");
        }
        var placed = candidates.FirstOrDefault(c => c.text.Split(' ').Any(w => PlaceWords.Contains(w.Trim('\'', '’'), StringComparer.OrdinalIgnoreCase)));
        if (placed.text != null) return (placed.text, "zone read as shown; not in the known list yet");
        // the minimap title is the top line of the corner; a label there with no place word is still most likely the zone (event areas, instances)
        var top = candidates[0];
        if (top.l.Box.Top - strip.Top < strip.Height * 0.5) return (top.text, "zone read from the minimap title; unverified name");
        return (null, $"top-right text didn't look like a zone name: {string.Join(" | ", candidates.Take(3).Select(c => c.text))}");
    }

    static string Clean(string s) => Regex.Replace(s.Trim(), @"\s+", " ").TrimEnd('.', ',', ':');

    static int Edit(string a, string b)
    {
        var prev = new int[b.Length + 1]; var cur = new int[b.Length + 1];
        for (int j = 0; j <= b.Length; j++) prev[j] = j;
        for (int i = 1; i <= a.Length; i++)
        {
            cur[0] = i;
            for (int j = 1; j <= b.Length; j++) cur[j] = Math.Min(Math.Min(cur[j - 1] + 1, prev[j] + 1), prev[j - 1] + (a[i - 1] == b[j - 1] ? 0 : 1));
            (prev, cur) = (cur, prev);
        }
        return prev[b.Length];
    }
}
