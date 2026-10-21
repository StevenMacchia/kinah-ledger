using System.Text;

namespace KinahLedger.Ocr;

/// <summary>
/// A loot line read while it was still fading in comes out garbled ("ria acquired" for "Aria acquired.", "»eqwacquired."
/// for a second gather landing a moment after the first). It matches no item, so nothing is counted, and on the next read
/// the clean line sits where the garbled one did, so the line tracker takes it for a line already seen. Fast gathers
/// (a critical success, a node in one tap) lost their second item this way.
///
/// So a new line that looks like loot but parsed to nothing is remembered with its place, counted from the bottom of the
/// chat. On the next read the line now at that place (moved up by the new lines that arrived under it) is taken for one
/// more parse when it has changed and is still close to what was read before, so a different line that scrolled into
/// the place is never counted. A line still garbled the same way waits for one more read; after that it is dropped.
/// </summary>
public sealed class Reread
{
    sealed record Pending(string Raw, int FromBottom, DateTime At, int Tries);
    readonly List<Pending> _pending = new();

    public int Recovered { get; private set; }

    /// <summary>Loot-shaped: an acquired or obtained line, or a Kina line.</summary>
    public static bool LooksLikeLoot(string line)
    {
        var k = Key(line);
        return k.Contains("acqui") || k.Contains("obtain") || k.Contains("cquired") || k.EndsWith("kina") || k.EndsWith("kinah");
    }

    /// <summary>A new line that looked like loot and parsed to nothing, at its place counted from the bottom (0 = the last line).</summary>
    public void Remember(string raw, int fromBottom, DateTime at)
    {
        if (_pending.Count < 8) _pending.Add(new Pending(raw, fromBottom, at, 0));
    }

    /// <summary>
    /// The lines of this read worth one more parse: what now sits where a garbled loot line was. <paramref name="freshCount"/>
    /// is how many new lines this read added under the old ones. Call once per read, before remembering this read's lines.
    /// </summary>
    public List<string> Take(IReadOnlyList<string> lines, int freshCount, DateTime now)
    {
        var take = new List<string>();
        if (_pending.Count == 0) return take;
        if (lines.Count == 0) { _pending.RemoveAll(p => (now - p.At).TotalSeconds > 4); return take; }   // faded: wait a little
        var keep = new List<Pending>();
        foreach (var p in _pending)
        {
            int pos = p.FromBottom + freshCount;
            if (pos >= lines.Count) continue;
            var cand = lines[lines.Count - 1 - pos];
            if (cand == p.Raw)
            {
                // still the same garbled text: one more look, then let it go
                if (p.Tries < 1 && (now - p.At).TotalSeconds <= 4) keep.Add(p with { FromBottom = pos, Tries = p.Tries + 1 });
                continue;
            }
            if (!Near(Key(cand), Key(p.Raw))) continue;
            take.Add(cand);
        }
        _pending.Clear();
        _pending.AddRange(keep);
        Recovered += take.Count;
        return take;
    }

    /// <summary>The same line read twice, the first time while it faded in: within about two letters in five.</summary>
    static bool Near(string a, string b)
    {
        if (a.Length < 6 || b.Length < 3) return false;
        int max = Math.Max(2, (int)(Math.Max(a.Length, b.Length) * 0.4));
        return Math.Abs(a.Length - b.Length) <= max && Lev(a, b) <= max;
    }

    static int Lev(string a, string b)
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

    static string Key(string s)
    {
        var sb = new StringBuilder();
        foreach (var c in s.ToLowerInvariant()) if (char.IsLetterOrDigit(c)) sb.Append(c);
        return sb.ToString();
    }
}
