using System.Globalization;
using System.Text;
using KinahLedger.Data;

namespace KinahLedger.Session;

/// <summary>
/// Save as CSV (0.10.11): today's loot from the Farming page's ⋯ menu, and the runs the Events page shows from the ALL RUNS
/// box, as plain comma-separated text for a spreadsheet. Figures are the window's own: counts, worth after the Market's cut,
/// where each thing came from. Written by the window (Ui/MainWindow.Export.cs) with a UTF-8 byte-order mark, which Excel
/// needs to read the × and the item names right. Nothing here leaves the PC.
/// </summary>
public static class CsvExport
{
    public static readonly string LootHeader = "kind,item,count,bound,worth_after_cut,priced,where";
    public static readonly string RunsHeader = "started,kind,place,event,family,took_seconds,kinah,kinah_known,kills,worth_after_cut,left_out,items,bound_items,notes";

    /// <summary>The file's name: kinah-ledger-loot-2026-11-06.csv, kinah-ledger-runs-2026-11-06.csv.</summary>
    public static string FileName(string what, DateTime dayLocal) => $"kinah-ledger-{what}-{dayLocal.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}.csv";

    /// <summary>One cell: quoted when it holds a comma, a quote or a line break, the quotes doubled (RFC 4180).</summary>
    public static string Cell(string? s)
    {
        s ??= "";
        return s.IndexOfAny(new[] { ',', '"', '\n', '\r' }) >= 0 ? "\"" + s.Replace("\"", "\"\"") + "\"" : s;
    }

    static string Row(params string[] cells) => string.Join(",", cells.Select(Cell));
    static string N(long n) => n.ToString(CultureInfo.InvariantCulture);
    static string Where(IEnumerable<(string Where, long Count)> where) => string.Join("; ", where.Select(w => $"{w.Where} {N(w.Count)}"));

    /// <summary>
    /// Today's loot, a row a thing: the Kinah first (its count is the amount), then every drop from kills and every gathered
    /// item, each with how many came, how many of those were bound, what the sellable ones are worth after the Market's cut,
    /// whether a price was known, and the zones it came from with a count each.
    /// </summary>
    public static string Loot(TodayFarm f)
    {
        var sb = new StringBuilder();
        sb.AppendLine(LootHeader);
        if (f.Kinah > 0) sb.AppendLine(Row("kinah", "Kinah", N(f.Kinah), "0", N(f.Kinah), "yes", Where(f.KinahWhere)));
        if (f.Abyss > 0) sb.AppendLine(Row("abyss points", "Abyss Points", N(f.Abyss), "0", "0", "no", ""));
        foreach (var d in f.Drops) sb.AppendLine(Slot("drop", d));
        foreach (var g in f.Gathered) sb.AppendLine(Slot("gathered", g));
        return sb.ToString();
    }

    static string Slot(string kind, TodaySlot s) => Row(kind, s.Name, N(s.Count), N(s.Bound), N(s.Worth), s.Priced ? "yes" : "no", Where(s.Where));

    /// <summary>
    /// The runs, a row a run, in the order given: when it started (this PC's clock), expedition or event, the place, the
    /// event and its family when named, how long it took, its Kinah (blank when it isn't known), kills, its worth after the
    /// Market's cut (<paramref name="value"/>, the window's own figure), whether it is left out of the averages, the items and
    /// the bound items as "name ×count; …", and a note on anything to look at.
    /// </summary>
    public static string Runs(IEnumerable<InstanceRun> runs, ItemDictionary items, Func<InstanceRun, long> value)
    {
        var sb = new StringBuilder();
        sb.AppendLine(RunsHeader);
        foreach (var r in runs)
        {
            var notes = new List<string>();
            if (r.RewardsNotRead) notes.Add("rewards not read");
            else if (r.KinahNotRead) notes.Add("Kinah not read");
            if (r.PartlyRead) notes.Add("partly read");
            if (r.SlotsUnread > 0) notes.Add($"{r.SlotsUnread} slot{(r.SlotsUnread == 1 ? "" : "s")} not read");
            if (r.UnknownGear > 0) notes.Add($"{r.UnknownGear} piece{(r.UnknownGear == 1 ? "" : "s")} of gear not named");
            if (r.SlotDoubts.Count > 0) notes.Add("a count in doubt");
            sb.AppendLine(Row(
                r.Start.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
                r.Kind, r.Zone, r.Event, r.Family,
                r.Seconds > 0 ? N((long)Math.Round(r.Seconds)) : "",
                r.KinahUnknown ? "" : N(r.Kinah), r.KinahUnknown ? "no" : "yes",
                N(r.Kills), N(value(r)), r.OutOfAverages ? "yes" : "no",
                Items(r.Items, items), Items(r.BoundItems, items), string.Join("; ", notes)));
        }
        return sb.ToString();
    }

    static string Items(Dictionary<long, int> by, ItemDictionary items)
        => string.Join("; ", by.OrderByDescending(kv => kv.Value).ThenBy(kv => items.NameOf(kv.Key), StringComparer.Ordinal).Select(kv => $"{items.NameOf(kv.Key)} ×{N(kv.Value)}"));
}
