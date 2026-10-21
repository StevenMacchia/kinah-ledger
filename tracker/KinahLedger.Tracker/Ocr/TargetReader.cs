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

    /// <summary>The list's spelling of a name read off the bar, allowing a letter of misread per seven; null when it isn't on the list.</summary>
    public static string? Match(string read)
    {
        var low = read.ToLowerInvariant();
        foreach (var k in Known) if (string.Equals(k, read, StringComparison.OrdinalIgnoreCase)) return k;
        foreach (var k in Known)
        {
            int max = Math.Max(1, k.Length / 7);
            if (Math.Abs(k.Length - read.Length) <= max && Edit(k.ToLowerInvariant(), low) <= max) return k;
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

    /// <summary>The last name-shaped text on the bar that isn't on the list, so a missing enemy can be named and added.</summary>
    public string Unlisted { get; private set; } = "";
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
            // the same pixels: the bar hasn't changed, so the name it showed is still the target
            if (Name != null && _pending == Name) NameAt = DateTime.UtcNow;
            return;
        }
        _hash = h;
        var lines = await ScreenReader.ReadBoxesAsync(raw, strip.Location, 3, boost: true);
        Reads++;
        string? found = null;
        foreach (var l in lines.OrderBy(l => l.Box.Top))
        {
            var t = Regex.Replace(Noise().Replace(l.Text, " "), @"\s+", " ").Trim().TrimEnd('.', ',', ':', ';');
            if (t.Length < 3 || !Shape().IsMatch(t) || Not.Contains(t) || ZoneLocator.Known.Contains(t, StringComparer.OrdinalIgnoreCase)) continue;
            var hit = Match(t);
            if (hit == null)
            {
                Unlisted = t;
                if (_noted.Count < 200 && _noted.Add(t)) CrashLog.Note($"target bar: not an enemy on the list, not credited: {t}");
                continue;
            }
            found = hit;
            break;
        }
        if (found == null) { _pending = null; return; }   // nothing targeted: the last name keeps its time for the grace window
        if (found == _pending) { Name = found; NameAt = DateTime.UtcNow; }   // read twice in a row: trusted
        else _pending = found;
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
