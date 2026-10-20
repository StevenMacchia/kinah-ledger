using System.Text;
using System.Text.RegularExpressions;
using KinahLedger.Data;
using KinahLedger.Protocol;

namespace KinahLedger.Ocr;

/// <summary>
/// Turns recognized chat lines into events. Patterns follow the game's system messages
/// ("Acquired 9,000 Kina (Bound).", "Acquired Drakan hide ×2.", "You defeated Flame Sprite.")
/// and tolerate the usual recognition slips (x for ×, missing periods, O for 0 inside numbers).
/// </summary>
public sealed partial class ChatParser
{
    readonly ItemDictionary _items;
    public ChatParser(ItemDictionary items) { _items = items; }

    [GeneratedRegex(@"^\[?\d{1,2}:\d{2}(?::\d{2})?\]?\s*")]
    private static partial Regex Timestamp();

    [GeneratedRegex(@"\bacquired\s+(?<amt>[0-9OoIl][0-9OoIl,\. ]*?)\s*kinah?\b(?<rest>.*)$", RegexOptions.IgnoreCase)]
    private static partial Regex Kina();

    [GeneratedRegex(@"\b(?:acquired|obtained|looted|picked up)\s+(?<name>.+?)(?:\s*[×xX\*]\s*(?<n>\d{1,4}))?\s*\.?\s*$", RegexOptions.IgnoreCase)]
    private static partial Regex Item();

    /// <summary>The game's own item line: "Fresh Fruit acquired." (name first). "acquire[da]" tolerates the common misread.</summary>
    [GeneratedRegex(@"^(?<name>[^\[\]]+?)(?:\s*[×xX\*]\s*(?<n>\d{1,4}))?\s+acquire[da]\.?\s*$", RegexOptions.IgnoreCase)]
    private static partial Regex ItemNameFirst();

    [GeneratedRegex(@"\bdefeated\s+(?<mob>.+?)\s*\.?\s*$", RegexOptions.IgnoreCase)]
    private static partial Regex Kill();

    [GeneratedRegex(@"\b(?:gained|earned|received|obtained)\s+(?<xp>[0-9OoIl][0-9OoIl,\. ]*?)\s*(?:xp\b|exp\b|experience)", RegexOptions.IgnoreCase)]
    private static partial Regex Xp();

    public IEnumerable<GameEvent> Parse(string rawLine, DateTime time)
    {
        var line = Timestamp().Replace(rawLine.Trim(), "");
        if (line.Length == 0) yield break;

        var k = Kina().Match(line);
        if (k.Success)
        {
            long amt = Digits(k.Groups["amt"].Value);
            if (amt > 0) yield return new KinahChanged(time, amt, k.Groups["rest"].Value.Contains("bound", StringComparison.OrdinalIgnoreCase));
            yield break;
        }

        var kill = Kill().Match(line);
        if (kill.Success)
        {
            yield return new MobKilled(time, Fnv(kill.Groups["mob"].Value));
            yield break;
        }

        var xp = Xp().Match(line);
        if (xp.Success)
        {
            yield return new XpGained(time, Digits(xp.Groups["xp"].Value));
            yield break;
        }

        var it = ItemNameFirst().Match(line);
        if (!it.Success) it = Item().Match(line);
        if (it.Success)
        {
            var name = it.Groups["name"].Value.Trim().TrimEnd('.').Trim('[', ']', ' ');
            if (name.Length < 2) yield break;
            // "Acquired 3 Iron Ore" puts the count first; keep the name clean and the count right
            var lead = System.Text.RegularExpressions.Regex.Match(name, @"^(\d{1,4})\s+(.+)$");
            int lead_n = 0;
            if (lead.Success) { lead_n = int.Parse(lead.Groups[1].Value); name = lead.Groups[2].Value; }
            bool bound = name.Contains("(bound)", StringComparison.OrdinalIgnoreCase);
            // bound and tradeable copies count as the same drop; the flag is kept, the name isn't split
            if (bound) name = System.Text.RegularExpressions.Regex.Replace(name, @"\s*\(bound\)\s*", " ", RegexOptions.IgnoreCase).Trim();
            int n = it.Groups["n"].Success ? int.Parse(it.Groups["n"].Value) : lead_n > 0 ? lead_n : 1;
            yield return new ItemAcquired(time, _items.IdOf(name), Math.Clamp(n, 1, 9999), bound);
        }
    }

    /// <summary>Digits only, with the recognizer's usual letter-for-digit slips corrected.</summary>
    static long Digits(string s)
    {
        var sb = new StringBuilder();
        foreach (var c in s)
        {
            if (char.IsDigit(c)) sb.Append(c);
            else if (c is 'O' or 'o') sb.Append('0');
            else if (c is 'I' or 'l' or '|') sb.Append('1');
        }
        return long.TryParse(sb.ToString(), out var v) ? v : 0;
    }

    static long Fnv(string s) { uint h = 2166136261; foreach (var c in s.ToLowerInvariant()) { h ^= c; h *= 16777619; } return h; }
}

/// <summary>
/// The chat keeps old lines on screen, so each read sees them again. This works out which lines are new
/// by lining up the previous read's last lines against the current read; everything after the overlap is new.
/// When the two can't be aligned (a glitched read, or too many lines at once) it counts nothing rather than double count.
/// </summary>
public sealed class LineTracker
{
    List<string> _prev = new();
    public int Unaligned { get; private set; }

    public List<string> NewLines(List<string> cur)
    {
        var keys = cur.Select(Key).ToList();
        if (_prev.Count == 0)
        {
            _prev = keys;                     // first read: what's already on screen is history, not loot
            return new List<string>();
        }
        if (keys.SequenceEqual(_prev)) return new List<string>();

        for (int k = Math.Min(3, _prev.Count); k >= 1; k--)
        {
            var tail = _prev.GetRange(_prev.Count - k, k);
            for (int i = keys.Count - k; i >= 0; i--)        // last occurrence: fewest new lines, never a double count
            {
                if (!keys.GetRange(i, k).SequenceEqual(tail)) continue;
                var fresh = cur.GetRange(i + k, keys.Count - i - k);
                _prev = keys;
                return fresh;
            }
        }
        Unaligned++;
        _prev = keys;
        return new List<string>();
    }

    static string Key(string s)
    {
        var sb = new StringBuilder();
        foreach (var c in s.ToLowerInvariant()) if (char.IsLetterOrDigit(c)) sb.Append(c);
        return sb.ToString();
    }
}
