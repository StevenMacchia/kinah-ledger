using System.Text.Json;

namespace KinahLedger.Session;

/// <summary>
/// One character's kept file joined into another's (the place pass, CharacterSort.Place, when the main character already
/// has files of its own: on Steven's PC 0.10.51 gave the history to an alt, and the main kept counting into a fresh
/// folder for a few hours before 0.10.53 moved it over). Each kind of file joins its own way: days by day (the
/// destination's day kept where both have one), quest points added up, the better best, goals up to three, the
/// line-a-record logs appended. Today's count is the destination's (a count can't be added to another; the one set aside
/// stays beside it as .from-&lt;id&gt;).
/// </summary>
public static class CharacterMerge
{
    /// <summary>Joins <paramref name="src"/> into <paramref name="dst"/> (both whole paths of the same file name); the source is gone after, or set aside when it can't be joined.</summary>
    public static void Into(string src, string dst, string asideTag)
    {
        if (!File.Exists(src)) return;
        if (!File.Exists(dst)) { File.Move(src, dst); return; }
        var name = Path.GetFileName(dst);
        try
        {
            switch (name)
            {
                case "days.json": Days(src, dst); break;
                case "quests.json": QuestsJoin(src, dst); break;
                case "best.json": Bests(src, dst); break;
                case "goals.json": GoalsJoin(src, dst); break;
                case "today.json": case "quests.bad.json": File.Move(src, dst + ".from-" + asideTag, overwrite: true); return;
                default: File.AppendAllLines(dst, File.ReadAllLines(src)); break;   // the logs of one record a line
            }
            File.Delete(src);
        }
        catch (Exception ex)
        {
            CrashLog.Write("characters merge " + name, ex);
            try { File.Move(src, dst + ".from-" + asideTag, overwrite: true); } catch (Exception) { }
        }
    }

    static void Days(string src, string dst)
    {
        var a = DayHistory.Load(src); var b = DayHistory.Load(dst);
        foreach (var d in a.Days) if (b.Get(d.Day) == null) b.Days.Add(d);
        b.Days = b.Days.OrderBy(d => d.Day, StringComparer.Ordinal).ToList();
        var since = new[] { a.Since, b.Since }.Where(s => s.Length > 0).OrderBy(s => s, StringComparer.Ordinal).FirstOrDefault();
        if (since != null) b.Since = since;
        DayHistory.Save(b, dst);
    }

    static void QuestsJoin(string src, string dst)
    {
        var a = Quests.Load(src); var b = Quests.Load(dst);
        var seen = b.Done.Select(d => d.Key + "|" + d.DoneAtUtc).ToHashSet();
        foreach (var d in a.Done) if (seen.Add(d.Key + "|" + d.DoneAtUtc)) b.Done.Add(d);
        b.AllTimePoints += a.AllTimePoints;
        foreach (var p in a.Pins) if (!b.Pins.Contains(p)) b.Pins.Add(p);
        var x = a.AllTime; var y = b.AllTime;
        y.Kinah += x.Kinah; y.Gathers += x.Gathers;
        foreach (var (k, v) in x.Items) y.Items[k] = y.Items.GetValueOrDefault(k) + v;
        foreach (var (k, v) in x.Runs) y.Runs[k] = y.Runs.GetValueOrDefault(k) + v;
        if (x.Day == y.Day)
        {
            y.DayKinah += x.DayKinah; y.DayGathers += x.DayGathers;
            foreach (var (k, v) in x.DayItems) y.DayItems[k] = y.DayItems.GetValueOrDefault(k) + v;
            foreach (var (k, v) in x.DayRuns) y.DayRuns[k] = y.DayRuns.GetValueOrDefault(k) + v;
        }
        else if (string.CompareOrdinal(x.Day, y.Day) > 0)
        {
            // the source's day is the later one: its day figures stand, the destination's older day is already in its totals' past
            y.Day = x.Day; y.DayKinah = x.DayKinah; y.DayGathers = x.DayGathers;
            y.DayItems = new(x.DayItems, StringComparer.OrdinalIgnoreCase); y.DayRuns = new(x.DayRuns, StringComparer.OrdinalIgnoreCase);
        }
        Quests.Save(b, dst);
    }

    static void Bests(string src, string dst)
    {
        var a = JsonSerializer.Deserialize<Dictionary<string, BestRuns.Best>>(File.ReadAllText(src)) ?? new();
        var b = JsonSerializer.Deserialize<Dictionary<string, BestRuns.Best>>(File.ReadAllText(dst)) ?? new();
        foreach (var (k, v) in a) if (!b.TryGetValue(k, out var have) || v.PerHr > have.PerHr) b[k] = v;
        File.WriteAllText(dst, JsonSerializer.Serialize(b));
    }

    static void GoalsJoin(string src, string dst)
    {
        var b = Goals.Load(dst);
        var keys = b.Select(g => JsonSerializer.Serialize(g)).ToHashSet();
        foreach (var g in Goals.Load(src)) if (b.Count < Goals.Max && keys.Add(JsonSerializer.Serialize(g))) b.Add(g);
        Goals.Save(b, dst);
    }
}
