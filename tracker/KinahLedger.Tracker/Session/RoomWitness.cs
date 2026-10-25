using System.Text.RegularExpressions;
using KinahLedger.Ocr;

namespace KinahLedger.Session;

/// <summary>
/// The chat's own lines about an expedition's room, as Steven's trace.log read them on 2026-10-22 to 24 (every Urugugu
/// Canyon run of those days), the witnesses of a run's edges beside the minimap (InstanceLog.RoomLeft, StartLine):
/// <list type="bullet">
/// <item>"Left Urugugu Canyon Dungeon room." as the player leaves the room: after the run's table (2026-10-24 14:12:48 the
/// table, 14:12:53 the line; 19:02:29 and 19:03:01) or partway (17:11:46 in, 17:12:21 out). Read garbled too ("Left Urugugu
/// Canyon Dungeon rpom.", "LeftUrugugu Canyon Dungeon room.") and again a minute or two later ("lefUUrugugu«Camyon Dungeon
/// room.", 19:05:23).</item>
/// <item>"Starting in 10s." to "Starting in 1s." inside the instance as each run begins: at an entry (14:19:20, the minimap
/// naming Urugugu Village at 14:19:30) and at each run back to back (2026-10-23 18:34:51, 18:42:04, 18:50:10, 18:57:21, 46 to
/// 74 s after each table). Read as "Starting in IOS.", "Starting in Is.", "Starting inns.", "A-Starting in 2s.".</item>
/// </list>
/// A player's chat ("[Name] …") never counts, and lines naming other players ("Riziy[ARI] left the room.") are not these.
/// </summary>
public static class RoomWitness
{
    // "Left <place> Dungeon room.": the place as letters, spaces and apostrophes; "Dungeon" read as "Dungeom" (2026-10-24
    // 18:19:04, "Left Urugugu Canyon Dungeomroomt'"), "room" as "rpom" or "roon"
    static readonly Regex LeftRx = new(@"^\W{0,3}l\s?e\s?f\s?t\s*(?<place>[A-Za-z'’ ]{3,40}?)\s*d\s?u\s?n\s?g\s?e\s?o\s?[nmh]\W{0,2}\s*r\s?[o0p]\s?[o0]?\s?[mn]",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);
    // "Starting in 9s.", with up to a stray mark or a letter and a dash in front ("—Starting in Is", "A-Starting in 2s.")
    static readonly Regex StartRx = new(@"^\W{0,4}(?:[A-Za-z]\W)?\s*starting\s*in(?<rest>.*)$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>The place a "Left … Dungeon room." line names ("Urugugu Canyon"), or null.</summary>
    public static string? LeftRoom(string line)
    {
        var t = line.Trim();
        if (t.StartsWith('[')) return null;
        var m = LeftRx.Match(t);
        if (!m.Success) return null;
        var place = Regex.Replace(m.Groups["place"].Value, @"\s+", " ").Trim();
        return place.Length >= 3 ? place : null;
    }

    /// <summary>
    /// A countdown line: "Starting in" then the seconds as read (a short tail with an s or a figure, or nothing), or the
    /// seconds and the next line read as one ("starting in 7s. Guardian captain Ral", 2026-10-23 19:36:51). Not "Starting
    /// inside…", not a sentence ("starting in a minute").
    /// </summary>
    public static bool StartLine(string line)
    {
        var t = line.Trim();
        if (t.StartsWith('[')) return false;
        var m = StartRx.Match(t);
        if (!m.Success) return false;
        var rest = m.Groups["rest"].Value.Trim();
        if (Regex.IsMatch(rest, @"^\W*[\dIlO]{1,2}\s?s\b", RegexOptions.IgnoreCase)) return true;
        if (Regex.IsMatch(rest, "^[a-z]{3,}", RegexOptions.IgnoreCase)) return false;
        return rest.Length == 0 || (rest.Length <= 7 && Regex.IsMatch(rest, @"[sS5\d]"));
    }

    /// <summary>A place read from a room line is this zone: the same letters, a misread per six at most.</summary>
    public static bool SameName(string place, string zone)
    {
        string a = Squash(place), b = Squash(zone);
        return a.Length >= 3 && b.Length > 0 && Edit(a, b) <= Math.Max(1, b.Length / 6);
    }

    /// <summary>The expedition on the instance list a room line's place names (the name its runs file under), or null.</summary>
    public static string? Expedition(string place)
    {
        var a = Squash(place);
        return ZoneLocator.Instances.Where(kv => kv.Value == "Expedition").Select(kv => ZoneLocator.RunName(kv.Key)).Distinct(StringComparer.OrdinalIgnoreCase)
            .Where(n => SameName(place, n)).OrderBy(n => Edit(a, Squash(n))).FirstOrDefault();
    }

    static string Squash(string s) => new(s.ToLowerInvariant().Where(c => c is >= 'a' and <= 'z').ToArray());

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
