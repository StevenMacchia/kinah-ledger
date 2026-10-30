using System.Text.Json;
using KinahLedger.Data;

namespace KinahLedger.Session;

/// <summary>
/// today.json beside sessions.jsonl: Today's farming count as of the last save, so a restart of the tracker (an update, a
/// reboot) carries the day on. A day is the calendar day on this PC (Steven, 2026-10-27: "Midnight on this PC"); a file
/// from another day is left alone and a fresh count starts. Never sent anywhere.
/// </summary>
public static class DayStore
{
    public static string PathFor() => Path.Combine(CrashLog.Dir, "today.json");

    /// <summary>"2026-10-27": the local calendar day of <paramref name="local"/>.</summary>
    public static string DayOf(DateTime local) => local.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
    public static string Today => DayOf(DateTime.Now);

    /// <summary>A fresh count for today, seeded with what today.json holds for today (nothing for another day or no file).</summary>
    public static SessionEngine Load(ItemDictionary items, string? path = null)
    {
        var e = new SessionEngine(items);
        try
        {
            var p = path ?? PathFor();
            if (!File.Exists(p)) return e;
            var c = JsonSerializer.Deserialize<SessionEngine.Carry>(File.ReadAllText(p), SessionEngine.CarryJson);
            if (c != null && c.Day == Today) e.Seed(c);
        }
        catch (Exception ex) { CrashLog.Write("today load", ex); }
        return e;
    }

    /// <summary>
    /// Writes the day's figures (to a temporary file first, so a crash mid-write never leaves half a file), and the day's
    /// entry in days.json beside it (0.9, DayHistory): the Week side's history, updated with every save.
    /// </summary>
    public static void Save(SessionEngine day, string dayKey, string? path = null)
    {
        SessionEngine.Carry? carry = null;
        try
        {
            var p = path ?? PathFor();
            var tmp = p + ".tmp";
            carry = day.ToCarry(dayKey);
            File.WriteAllText(tmp, JsonSerializer.Serialize(carry, SessionEngine.CarryJson));
            File.Move(tmp, p, overwrite: true);
        }
        catch (Exception ex) { CrashLog.Write("today save", ex); }
        if (carry == null) return;
        try { DayHistory.Record(carry, day.ItemBook, path != null ? DayHistory.PathBeside(path) : null); }
        catch (Exception ex) { CrashLog.Write("days save", ex); }
    }

    /// <summary>
    /// At midnight on this PC (<paramref name="nowKey"/> is no longer <paramref name="dayKey"/>): the old day's count saved
    /// for good, today.json and its final entry in days.json, and a fresh count for the new day. Null while the day is the same.
    /// </summary>
    public static SessionEngine? Roll(SessionEngine day, string dayKey, string nowKey, string? path = null)
    {
        if (nowKey == dayKey) return null;
        Save(day, dayKey, path);
        return new SessionEngine(day.ItemBook);
    }
}
