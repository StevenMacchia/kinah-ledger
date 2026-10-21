using System.Drawing;
using System.Text.RegularExpressions;

namespace KinahLedger.Ocr;

/// <summary>
/// Reads the target bar at the top-centre of the game: the name of the enemy being fought. What it says is a best
/// guess about who paid the Kinah that lands a moment later. With several enemies on the player the bar shows one of
/// them, so every figure credited this way is marked as a guess, here and on the site. A name has to be read twice
/// in a row before it is trusted, and the last trusted name keeps its time, so a pickup within the grace window after
/// the bar emptied (the enemy died) still goes to it.
/// </summary>
public sealed partial class TargetReader
{
    /// <summary>The top-centre band that holds the target's name and health bar.</summary>
    public static Rectangle Strip(Rectangle game) =>
        new(game.Left + (int)(game.Width * 0.33), game.Top, (int)(game.Width * 0.34), Math.Max(40, (int)(game.Height * 0.11)));

    /// <summary>A name: capitalized words, letters, apostrophes and hyphens; no digits, no brackets.</summary>
    [GeneratedRegex(@"^[A-Z][A-Za-z'’\- ]{2,39}$")]
    private static partial Regex Shape();

    /// <summary>Level tags, percentages and bracketed bits beside the name.</summary>
    [GeneratedRegex(@"\b(?:lv|lvl|level)\.?\s*\d+\b|\d+\s*%|\[[^\]]*\]|\bhp\b", RegexOptions.IgnoreCase)]
    private static partial Regex Noise();

    /// <summary>
    /// The game's own notices, which pop up in the same top-centre band as the target bar ("Title acquired", "Quest
    /// completed"): a line with one of these words is a notice, never an enemy, and isn't even noted as an unlisted name.
    /// </summary>
    [GeneratedRegex(@"\b(?:acquired|obtained|completed?|unlocked|learned|registered|received|gained|achieved|achievement|reached|succeeded|has been|you have)\b", RegexOptions.IgnoreCase)]
    internal static partial Regex Notice();

    // the tracker's own panel labels and a few UI words that can land in the band
    static readonly HashSet<string> Not = new(StringComparer.OrdinalIgnoreCase)
    {
        "Kinah", "Kinah Ledger", "Target", "Elite", "Boss", "Normal", "Hero", "Legendary", "Total Estimated Value", "Drops Value", "Kinah Hr",
    };

    /// <summary>
    /// Enemy names as the game prints them on the target bar: data/mobs.txt, built into the exe. The whitelist for Kinah
    /// by enemy, the same file the site reads: a name not on it, exactly or as a close misread, is never credited or sent.
    /// </summary>
    public static readonly string[] Known = LoadMobs();

    static string[] LoadMobs()
    {
        try
        {
            using var stream = typeof(TargetReader).Assembly.GetManifestResourceStream("mobs.txt");
            if (stream == null) return Array.Empty<string>();
            using var r = new StreamReader(stream);
            var names = new List<string>();
            while (r.ReadLine() is { } line) { var t = line.Trim(); if (t.Length > 0 && !t.StartsWith('#')) names.Add(t); }
            return names.ToArray();
        }
        catch (Exception) { return Array.Empty<string>(); }
    }

    /// <summary>
    /// Lower case, with "rn" read as "m" on both sides: the recognizer often reads an m as rn ("Kalgolern" for Kalgolem),
    /// which is two edits and more than the misread allowance for a short name. No two names on the list collide this way.
    /// </summary>
    static string Norm(string s) => s.ToLowerInvariant().Replace("rn", "m");
    static readonly string[] KnownNorm = Known.Select(Norm).ToArray();

    /// <summary>The list's spelling of a name read off the bar, allowing a letter of misread per seven; null when it isn't on the list.</summary>
    public static string? Match(string read)
    {
        var low = Norm(read.Trim());
        for (int i = 0; i < Known.Length; i++) if (KnownNorm[i] == low) return Known[i];
        for (int i = 0; i < Known.Length; i++)
        {
            int max = Math.Max(1, KnownNorm[i].Length / 7);
            if (Math.Abs(KnownNorm[i].Length - low.Length) <= max && Edit(KnownNorm[i], low) <= max) return Known[i];
        }
        return null;
    }

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

    // ---- reward screens ----
    // The same top-centre band shows the title of the Daeva Pass, a shop or a reward window while one is open ("Daeva
    // Pass", "Pass Shop", "Daeva Pass Level Reward", "Trade Shop", "Quna Shop"). Kinah and items that land while one is
    // open, and for RewardGraceSeconds after its title was last seen, are rewards: never farming Kinah, never drops.
    public const double RewardGraceSeconds = 3;

    [GeneratedRegex(@"\b(?:pass|shop|shops|reward|rewards)\b", RegexOptions.IgnoreCase)]
    private static partial Regex RewardWord();

    /// <summary>A pass, shop or reward window's title, and not an enemy's name.</summary>
    public static bool IsRewardTitle(string text) => RewardWord().IsMatch(text) && Match(text.Trim()) == null;

    // ---- the crafting window ----
    // Open, it lists the crafting skills along the top of the screen, in the same band. While it is open the chat
    // underneath is mostly hidden, so "Successfully crafted" is often never read: the item line alone says what was made.
    public const double CraftGraceSeconds = 10;
    static readonly string[] CraftTabs = { "Weaponsmithing", "Blacksmithing", "Armorsmithing", "Handicrafting", "Alchemy", "Cooking", "Cookinq" };
    public static bool IsCraftTab(string text) => CraftTabs.Any(t => text.Trim().Equals(t, StringComparison.OrdinalIgnoreCase));
    /// <summary>When the crafting window's skill tabs were last seen in the band.</summary>
    public DateTime CraftAt { get; private set; } = DateTime.MinValue;
    bool _craftSeen;
    public void MarkCraft(DateTime t) => CraftAt = t;

    /// <summary>When a reward screen's title was last seen in the band.</summary>
    public DateTime RewardAt { get; private set; } = DateTime.MinValue;
    bool _rewardSeen;
    public void MarkReward(DateTime t) => RewardAt = t;
    /// <summary>True while a reward screen is open, or was seen within the last <see cref="RewardGraceSeconds"/>.</summary>
    public bool RewardOpen(DateTime t) => (t - RewardAt).TotalSeconds is >= -1 and <= RewardGraceSeconds;

    /// <summary>The last name-shaped text on the bar that isn't on the list, so a missing enemy can be named and added.</summary>
    public string Unlisted { get; private set; } = "";
    /// <summary>When <see cref="Unlisted"/> was last read: the window only mentions it while it is recent, not for the rest of the session.</summary>
    public DateTime UnlistedAt { get; private set; } = DateTime.MinValue;
    readonly HashSet<string> _noted = new(StringComparer.OrdinalIgnoreCase);

    string? _pending;
    ulong _hash;
    public long Reads { get; private set; }
    /// <summary>The last trusted target name, and when it was last seen on the bar.</summary>
    public string? Name { get; private set; }
    public DateTime NameAt { get; private set; }

    public async Task TickAsync(Rectangle game)
    {
        var strip = Strip(game);
        using var raw = ScreenReader.Capture(strip);
        var h = Hash(raw);
        if (h == _hash && Reads > 0)
        {
            // the same pixels: the bar hasn't changed, so the name it showed is still the target, and a reward screen is still open
            if (Name != null && _pending == Name) NameAt = DateTime.UtcNow;
            if (_rewardSeen) RewardAt = DateTime.UtcNow;
            if (_craftSeen) CraftAt = DateTime.UtcNow;
            return;
        }
        _hash = h;
        // the enemy's name is gold over bright scenery: the name-colour read finds it where the brightness read can't;
        // the brightness read covers names drawn in white
        using var gold = ScreenReader.NameMask(raw);
        var lines = await ScreenReader.ReadBoxesAsync(gold, strip.Location, 3);
        lines.AddRange(await ScreenReader.ReadBoxesAsync(raw, strip.Location, 3, boost: true));
        Reads++;
        _craftSeen = lines.Any(l => IsCraftTab(l.Text));
        if (_craftSeen) CraftAt = DateTime.UtcNow;
        var title = _craftSeen ? null : lines.Select(l => l.Text.Trim()).FirstOrDefault(IsRewardTitle);
        _rewardSeen = title != null;
        if (title != null)
        {
            if ((DateTime.UtcNow - RewardAt).TotalSeconds > RewardGraceSeconds) CrashLog.Note($"reward screen open ({title}): its Kinah and items are rewards, not farming");
            RewardAt = DateTime.UtcNow;
        }
        var found = FindName(lines.Select(l => (l.Box.Top, l.Text)), out var unlisted);
        if (found == null && unlisted.Length > 0)
        {
            Unlisted = unlisted; UnlistedAt = DateTime.UtcNow;
            if (_noted.Count < 200 && _noted.Add(unlisted)) CrashLog.Note($"target bar: not an enemy on the list, not credited: {unlisted}");
        }
        if (found == null)
        {
            // a garbled read of the enemy already on the bar ("Hgrcl Kalgolem", or a bare "Kalgolem", while fighting Hard
            // Kalgolems) keeps it: farming one kind of mob, the bar names the same enemy, and one clean read says which
            if (Name != null && unlisted.Length > 0 && Close(unlisted, Name)) { NameAt = DateTime.UtcNow; _pending = Name; return; }
            _pending = null; return;   // nothing targeted: the last name keeps its time for the grace window
        }
        // a name on the enemy list is trusted on its first read: the list is the guard against garbage, and a mob that
        // dies in two seconds was never read twice (Steven, 2026-10-21: 8 kills, 1 credited)
        Name = found; NameAt = DateTime.UtcNow; _pending = found;
    }

    /// <summary>
    /// True when a read that matched nothing is a garbled read of <paramref name="name"/>: within about a third of its
    /// letters, or ending in its last word ("Kalgolem" for "Hard Kalgolem") when that word is five letters or more.
    /// </summary>
    internal static bool Close(string read, string name)
    {
        string r = Norm(read.Trim()), n = Norm(name);
        if (r.Length < 4) return false;
        int max = Math.Max(2, (int)(n.Length * 0.35));
        if (Math.Abs(r.Length - n.Length) <= max && Edit(r, n) <= max) return true;
        var last = n.Split(' ', StringSplitOptions.RemoveEmptyEntries).LastOrDefault() ?? "";
        return last.Length >= 5 && r.EndsWith(last, StringComparison.Ordinal);
    }

    /// <summary>
    /// The enemy named on the bar, from the lines read in the band. The game prints the level first and the distance
    /// after ("45 Highland Karnif 1m"), and the enemy's own target can share the line ("45 Swoly"), so words with digits
    /// are dropped and the name is looked for in the whole line, then in any run of two or more of its words.
    /// <paramref name="unlisted"/> is the first name-shaped text that isn't on the list, for the log.
    /// </summary>
    public static string? FindName(IEnumerable<(int Top, string Text)> lines, out string unlisted)
    {
        unlisted = "";
        foreach (var (_, text) in lines.OrderBy(l => l.Top))
        {
            if (Notice().IsMatch(text)) continue;   // "Title acquired": a game notice in the band, not a name
            var words = Noise().Replace(text, " ").Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .Select(w => w.Trim('.', ',', ':', ';', '|', '(', ')'))
                .Where(w => w.Length >= 2 && !w.Any(char.IsDigit))
                .ToList();
            if (words.Count == 0) continue;
            var t = string.Join(' ', words);
            if (t.Length < 3 || Not.Contains(t) || ZoneLocator.Known.Contains(t, StringComparer.OrdinalIgnoreCase)) continue;
            if (Match(t) is { } whole) return whole;
            for (int len = words.Count - 1; len >= 2; len--)
                for (int at = 0; at + len <= words.Count; at++)
                    if (Match(string.Join(' ', words.Skip(at).Take(len))) is { } part) return part;
            if (unlisted.Length == 0 && Shape().IsMatch(t)) unlisted = t;
        }
        return null;
    }

    /// <summary>A cheap fingerprint of the strip from a grid of sampled pixels, so an unchanged bar isn't recognized again.</summary>
    static ulong Hash(Bitmap b)
    {
        ulong h = 14695981039346656037;
        int sx = Math.Max(1, b.Width / 48), sy = Math.Max(1, b.Height / 8);
        for (int y = 0; y < b.Height; y += sy)
            for (int x = 0; x < b.Width; x += sx)
            {
                var c = b.GetPixel(x, y);
                h ^= (ulong)((c.R >> 3) << 10 | (c.G >> 3) << 5 | (c.B >> 3)); h *= 1099511628211;
            }
        return h;
    }
}
