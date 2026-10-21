using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;

namespace KinahLedger.Ocr;

/// <summary>
/// Where the game window is, in screen pixels, and whether a part of it can be seen. Borderless and windowed modes both
/// report a real rectangle. The tracker reads the screen, not the game, so it only reads a part of the game nothing covers.
/// </summary>
public static class GameWindow
{
    [StructLayout(LayoutKind.Sequential)] struct RECT { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);
    [DllImport("user32.dll")] static extern bool IsIconic(IntPtr hWnd);
    [DllImport("user32.dll")] static extern bool IsWindow(IntPtr hWnd);
    [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr hWnd);
    [DllImport("user32.dll")] static extern IntPtr GetWindow(IntPtr hWnd, uint cmd);
    [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint pid);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] static extern IntPtr GetWindowLongPtr(IntPtr hWnd, int index);
    [DllImport("user32.dll")] static extern bool GetLayeredWindowAttributes(IntPtr hWnd, out uint key, out byte alpha, out uint flags);
    [DllImport("dwmapi.dll")] static extern int DwmGetWindowAttribute(IntPtr hWnd, int attr, out int value, int size);
    [DllImport("dwmapi.dll")] static extern int DwmGetWindowAttribute(IntPtr hWnd, int attr, out RECT value, int size);

    const uint GW_HWNDPREV = 3, LWA_ALPHA = 2;
    const int GWL_EXSTYLE = -20, WS_EX_TRANSPARENT = 0x20, WS_EX_LAYERED = 0x80000, DWMWA_EXTENDED_FRAME_BOUNDS = 9, DWMWA_CLOAKED = 14;

    /// <summary>The game's window and every process id that belongs to the game, from the last successful <see cref="Find"/>.</summary>
    sealed record Found(IntPtr Handle, HashSet<uint> Pids);
    static volatile Found? _found;

    /// <summary>True when the last <see cref="Find"/> saw a game process, window or not (still loading, minimized).</summary>
    public static bool ProcessSeen { get; private set; }

    public static Rectangle? Find(IEnumerable<string> processHints)
    {
        var hints = processHints.Select(h => h.ToLowerInvariant()).ToArray();
        var pids = new HashSet<uint>();
        Rectangle? found = null; var handle = IntPtr.Zero;
        foreach (var p in Process.GetProcesses())
        {
            try
            {
                if (!hints.Any(h => p.ProcessName.ToLowerInvariant().Contains(h))) continue;
                pids.Add((uint)p.Id);
                if (found != null) continue;
                var h = p.MainWindowHandle;
                if (h == IntPtr.Zero || IsIconic(h) || !GetWindowRect(h, out var r)) continue;
                var rect = Rectangle.FromLTRB(r.Left, r.Top, r.Right, r.Bottom);
                if (rect.Width >= 640 && rect.Height >= 360) { found = rect; handle = h; }
            }
            catch (Exception) { /* process went away */ }
        }
        if (found != null) _found = new Found(handle, pids);
        ProcessSeen = pids.Count > 0;
        return found;
    }

    /// <summary>True while the game is the window the player is using.</summary>
    public static bool InFront(IEnumerable<string> processHints)
    {
        var f = Current(processHints);
        if (f == null) return false;
        GetWindowThreadProcessId(GetForegroundWindow(), out var pid);
        return f.Pids.Contains(pid);
    }

    /// <summary>
    /// What keeps this part of the game from being read, in a few words ("chrome", "the game is closed or minimized"), or
    /// null when it can be read. A window on top of the game would otherwise be read as if it were the game: its text
    /// could be counted as loot. Windows that let clicks through (frame counters, recording badges), fully transparent
    /// ones, windows on another desktop and the game's own windows don't count.
    /// </summary>
    public static string? Covered(Rectangle area, IEnumerable<string> processHints)
    {
        var f = Current(processHints);
        if (f == null) return "the game is closed or minimized";
        int n = 0;
        for (var h = GetWindow(f.Handle, GW_HWNDPREV); h != IntPtr.Zero && n < 5000; h = GetWindow(h, GW_HWNDPREV), n++)
        {
            if (!IsWindowVisible(h) || IsIconic(h)) continue;
            GetWindowThreadProcessId(h, out var pid);
            if (f.Pids.Contains(pid)) continue;
            long ex = GetWindowLongPtr(h, GWL_EXSTYLE).ToInt64();
            if ((ex & WS_EX_TRANSPARENT) != 0) continue;
            if ((ex & WS_EX_LAYERED) != 0 && GetLayeredWindowAttributes(h, out _, out var alpha, out var how) && (how & LWA_ALPHA) != 0 && alpha == 0) continue;
            if (DwmGetWindowAttribute(h, DWMWA_CLOAKED, out int cloaked, sizeof(int)) == 0 && cloaked != 0) continue;
            var r = Bounds(h);
            if (r.Width <= 0 || r.Height <= 0 || !r.IntersectsWith(area)) continue;
            return NameOf(pid);
        }
        return null;
    }

    static Found? Current(IEnumerable<string> processHints)
    {
        var f = _found;
        if (f != null && IsWindow(f.Handle) && !IsIconic(f.Handle)) return f;
        return Find(processHints) != null ? _found : null;
    }

    static Rectangle Bounds(IntPtr h)
    {
        // what is drawn: GetWindowRect also counts the invisible resize border around ordinary windows
        if (DwmGetWindowAttribute(h, DWMWA_EXTENDED_FRAME_BOUNDS, out RECT d, Marshal.SizeOf<RECT>()) == 0) return Rectangle.FromLTRB(d.Left, d.Top, d.Right, d.Bottom);
        return GetWindowRect(h, out var r) ? Rectangle.FromLTRB(r.Left, r.Top, r.Right, r.Bottom) : Rectangle.Empty;
    }

    static string NameOf(uint pid)
    {
        if (pid == (uint)Environment.ProcessId) return "Kinah Ledger's own window";
        try { using var p = Process.GetProcessById((int)pid); return p.ProcessName; }
        catch (Exception) { return "another window"; }
    }
}

/// <summary>
/// Finds the chat without anyone dragging a box: the chat's lines sit directly above the "Enter your message" input
/// at the bottom-left of the game, so reading that corner once gives both the position and the line height.
/// </summary>
public static class ChatLocator
{
    static readonly Regex Input = new(@"enter\s+your\s+messag|your\s+message", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    // what a chat line looks like on its own: "[Name] text" from a player, or one of the game's own messages
    static readonly Regex ChatLine = new(@"^\[[^\]]{2,20}\]\s|joined event|acquired|Kina|defeated|Obtained|failed|has logged in", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>
    /// The chat window's width at the least: about 40 characters, where the game wraps a chat line, in screen heights so
    /// it holds on any screen. Sizing the area only from the lines on screen at the moment it was found cut every longer
    /// line after that ("Sapphire Gemstone acq", a green armor's name, 2026-10-21 16:56) when those happened to be short.
    /// </summary>
    public static int MinChatWidth(Rectangle game) => (int)(game.Height * 0.40);

    /// <summary>The bottom-left quarter that holds the chat and its input box.</summary>
    public static Rectangle Corner(Rectangle game) => new(game.Left, game.Top + game.Height / 2, game.Width / 2, game.Height / 2);

    public static async Task<(Rectangle? region, string note)> DetectAsync(Rectangle game)
    {
        var corner = Corner(game);
        var lines = await ScreenReader.ReadBoxesAsync(corner, 1);
        var input = lines.FirstOrDefault(l => Input.IsMatch(l.Text));
        if (input == null) return await FromLines(game, lines);

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
        int right = Math.Min(game.Left + (int)(game.Width * 0.55), Math.Max(Math.Max(above.Count > 0 ? above.Max(l => l.Box.Right) : 0, input.Box.Right) + 40, left + MinChatWidth(game)));
        int bottom = input.Box.Top - 4;
        int top = Math.Max(game.Top, bottom - lineH * 12);            // room for twelve lines: the chat grows upward
        var region = Rectangle.FromLTRB(left, top, right, bottom);
        return (region, $"chat found above the input box: {above.Count} line(s) visible, ~{lineH}px per line");
    }

    /// <summary>
    /// The frameless chat: its lines float over the world at the bottom-left with no frame and no input box, and fade out
    /// when idle, so there is no input row to anchor on. The lines are left-aligned, so two or more chat-shaped lines that
    /// share a left edge give the position; the box is sized the way the input-row path sizes it.
    /// </summary>
    static async Task<(Rectangle? region, string note)> FromLines(Rectangle game, List<OcrLineBox> lines)
    {
        var chat = ChatLines(lines);
        bool boosted = false;
        if (chat.Count < 2)
        {
            // floating text over the world is faint: read the corner again with the contrast boost the live reader uses
            lines = await ScreenReader.ReadBoxesAsync(Corner(game), 2, boost: true);
            chat = ChatLines(lines);
            boosted = true;
        }
        if (chat.Count < 2) return (null, $"couldn't see the chat input box or any chat lines in the bottom-left corner ({lines.Count} lines read)");

        int lineH = Math.Max(14, (int)Math.Round(chat.Average(l => l.Box.Height) * 1.45));
        int left = Math.Max(game.Left, chat.Min(l => l.Box.Left) - 12);
        int right = Math.Min(game.Left + (int)(game.Width * 0.55), Math.Max(chat.Max(l => l.Box.Right) + 40, left + MinChatWidth(game)));
        int bottom = Math.Min(game.Bottom, chat.Max(l => l.Box.Bottom) + lineH);   // room for the next line, which arrives below
        int top = Math.Max(game.Top, bottom - lineH * 12);
        var region = Rectangle.FromLTRB(left, top, right, bottom);
        return (region, $"chat found from its lines; the input box isn't shown ({chat.Count} lines{(boosted ? ", boosted read" : "")})");
    }

    /// <summary>Chat-shaped lines that share the most common left edge (within 40 px): the chat is left-aligned, stray UI text isn't.</summary>
    static List<OcrLineBox> ChatLines(List<OcrLineBox> lines)
    {
        var shaped = lines.Where(l => ChatLine.IsMatch(l.Text.Trim())).ToList();
        if (shaped.Count < 2) return shaped;
        var anchor = shaped
            .OrderByDescending(a => shaped.Count(b => Math.Abs(b.Box.Left - a.Box.Left) <= 40))
            .ThenBy(a => a.Box.Left)
            .First();
        return shaped.Where(l => Math.Abs(l.Box.Left - anchor.Box.Left) <= 40).ToList();
    }
}

/// <summary>Reads the zone name shown above the minimap at the top-right, so sessions know where they happened.</summary>
public static class ZoneLocator
{
    // what a zone label looks like: a few capitalized words, no digits, no brackets
    static readonly Regex Shape = new(@"^[A-Z][A-Za-z'’\-]+(?:\s+(?:of|the|and|[A-Z][A-Za-z'’\-]+)){1,5}$", RegexOptions.Compiled);
    static readonly string[] Blocklist = { "Ask", "Gemini", "Copilot", "Google", "Search", "Chrome", "Edge", "Firefox", "Discord", "Steam", "Spotify", "Twitch", "YouTube", "Enter", "Type", "Message", "Reply", "File", "Edit", "Help", "Home", "Back", "Next", "Download", "Share", "Save", "Cancel", "Window", "Minimize", "Bookmarks", "History", "Extensions", "Profile", "Account", "Notifications", "Update", "New", "Sign", "Open", "Close", "Change", "Show", "Must", "Waiting", "Evasion", "Sprint", "Jump", "Mount", "Rest", "Auto", "Hide", "Ground", "Fly", "Level", "Quest", "View", "Menu", "Map", "Options", "Settings", "Inventory", "Chat", "Party", "Legion", "Friends", "Mail", "Shop" };
    // words that appear in place names; a candidate needs one of these or a known-name match, so UI labels never pass
    static readonly string[] PlaceWords = { "Valley", "Highland", "Highlands", "Canyon", "Hill", "Hills", "Sanctum", "Ruins", "Garden", "Forest", "Plains", "Plain", "Cave", "Caves", "Cavern", "Fortress", "Village", "Lake", "Coast", "Shore", "Pass", "Road", "Gate", "Temple", "Swamp", "Marsh", "Desert", "Island", "Isle", "Abyss", "Outpost", "Camp", "Field", "Fields", "Ridge", "Peak", "Mountain", "Mountains", "Basin", "Plateau", "Woods", "Grove", "Gorge", "Hollow", "Ravine", "Cliff", "Cliffs", "Falls", "Spring", "Springs", "River", "Bridge", "Tower", "Citadel", "Stronghold", "Fort", "Shrine", "Sanctuary", "Crypt", "Tomb", "Mine", "Mines", "Quarry", "Den", "Nest", "Lair", "Harbor", "Port", "Bay", "Oasis", "Steppe", "Wasteland", "Tundra", "Crater", "Dunes", "Lowlands", "Vale", "Meadow", "Prairie", "Orchard", "Farm", "Mill", "Crossroads", "Square", "District", "Town", "City", "Palace", "Castle", "Keep", "Hall", "Prison", "Arena", "Academy", "Library", "Observatory", "Laboratory", "Workshop", "Foundry", "Forge", "Market", "Cemetery", "Graveyard", "Catacombs", "Altar", "Portal", "Rift", "Gateway", "Frontier", "Border", "Coastline", "Beach", "Glade", "Thicket", "Jungle", "Volcano", "Furnace", "Wall", "Walls", "Rampart", "Battlefield", "Front", "Encampment", "Garrison", "Barracks", "Landing", "Ascent", "Descent", "Depths", "Heights", "Summit", "Hideout", "Refuge", "Haven", "Hamlet", "Settlement", "Colony", "Waystation", "Station" };

    /// <summary>
    /// Zone and area names as the game prints them on the minimap: data/zones.txt, built into the exe (about 330 names,
    /// both factions and the Abyss). The whitelist for zones; a name not here counts as Unknown zone. The site reads the same file.
    /// </summary>
    public static readonly string[] Known = LoadZones().Concat(LoadInstances().Keys).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();

    /// <summary>Instanced areas (dungeons, trials, arenas, event areas) and the kind a run in one is filed under: data/instances.txt.</summary>
    public static readonly Dictionary<string, string> Instances = LoadInstances();

    /// <summary>"Expedition", "Event", "Arena" or "Legion" for an instanced area; null for an open-world zone or none.</summary>
    public static string? KindOf(string? zone) => zone != null && Instances.TryGetValue(zone, out var k) ? k : null;

    static Dictionary<string, string> LoadInstances()
    {
        var d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            using var stream = typeof(ZoneLocator).Assembly.GetManifestResourceStream("instances.txt");
            if (stream == null) return d;
            using var r = new StreamReader(stream);
            while (r.ReadLine() is { } line)
            {
                var t = line.Trim();
                if (t.Length == 0 || t.StartsWith('#')) continue;
                var bar = t.LastIndexOf('|');
                if (bar <= 0) continue;
                d[t[..bar].Trim()] = t[(bar + 1)..].Trim();
            }
        }
        catch (Exception) { /* without the list, instances read as unlisted titles and count under Unknown zone */ }
        return d;
    }

    /// <summary>
    /// The minimap title read last, when it was name-shaped but on no list (an expedition's inner area, an event area the
    /// list doesn't have yet); null when the last read found a zone or no title at all.
    /// </summary>
    public static string? LastUnlisted { get; private set; }

    static string[] LoadZones()
    {
        try
        {
            using var stream = typeof(ZoneLocator).Assembly.GetManifestResourceStream("zones.txt");
            if (stream != null)
            {
                using var r = new StreamReader(stream);
                var names = new List<string>();
                while (r.ReadLine() is { } line) { var t = line.Trim(); if (t.Length > 0 && !t.StartsWith('#')) names.Add(t); }
                if (names.Count > 0) return names.ToArray();
            }
        }
        catch (Exception) { /* fall back to the few names below */ }
        return new[] { "Western Altamia Highland", "Eastern Altamia Highland", "Southern Altamia Highland", "Altamia Canyon", "Contaminated Sanctum", "Rock Tomb Hill", "Garden of the Illusion God", "Altamia Highland Ruins", "Western Cantas Valley", "Eastern Cantas Valley", "Cantas Valley", "Dawn Legion Base" };
    }

    /// <summary>The top-right corner that holds the minimap and its title.</summary>
    public static Rectangle Strip(Rectangle game) => new(game.Right - (int)(game.Width * 0.24), game.Top, (int)(game.Width * 0.24), (int)(game.Height * 0.14));

    /// <summary>A name from the known list, or one carrying a place word: trusted on a single read. Anything else needs to be read twice in a row.</summary>
    public static bool IsVerified(string zone) =>
        Known.Any(k => string.Equals(k, zone, StringComparison.OrdinalIgnoreCase))
        || zone.Split(' ').Any(w => PlaceWords.Contains(w.Trim('\'', '’'), StringComparer.OrdinalIgnoreCase));

    public static async Task<(string? zone, string note)> DetectAsync(Rectangle game)
    {
        var strip = Strip(game);
        var lines = await ScreenReader.ReadBoxesAsync(strip, 2);
        var candidates = lines
            .Select(l => (l, text: Clean(l.Text)))
            .Where(x => x.text.Length >= 6 && x.text.Length <= 40 && Shape.IsMatch(x.text) && !Blocklist.Any(b => x.text.StartsWith(b + " ") || x.text == b))
            .OrderBy(x => x.l.Box.Top).ToList();
        LastUnlisted = null;
        if (candidates.Count == 0) return (null, $"no zone-shaped text in the top-right corner ({lines.Count} lines read)");
        // the known list is the whitelist: a name on it wins even misread. Anything else is some other label, or a zone the
        // list doesn't have yet; either way the count stays under "Unknown zone" rather than under a misread name.
        foreach (var c in candidates)
        {
            var hit = Known.FirstOrDefault(k => Edit(k.ToLowerInvariant(), c.text.ToLowerInvariant()) <= Math.Max(1, k.Length / 7));
            if (hit != null) return (hit, "zone matched a known name");
        }
        // the top line of the corner is the minimap's title: name-shaped and on no list, it is a place the list lacks
        if (candidates[0].l.Box.Top - strip.Top < strip.Height * 0.5) LastUnlisted = candidates[0].text;
        return (null, $"top-right text isn't a zone on the list: {string.Join(" | ", candidates.Take(3).Select(c => c.text))}");
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
