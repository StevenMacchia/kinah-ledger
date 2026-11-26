using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using KinahLedger.Ocr;

namespace KinahLedger.Data;

/// <summary>German item names from the Global client's localization table, keyed by the game's item IDs.</summary>
internal static class GermanItemCatalog
{
    internal const string FileName = "item-names.de-DE.json";
    internal sealed record Entry(ItemInfo Info, bool ExplicitlyBound);

    sealed record Index(Dictionary<long, ItemInfo> Items, Dictionary<string, Entry[]> Names, Dictionary<string, Entry[]> Compact);
    static readonly Lazy<Index> Catalog = new(Load);
    const string BoundSuffix = " (gebunden)";

    internal static ItemInfo? ById(long id) => id > 0 ? Catalog.Value.Items.GetValueOrDefault(id) : null;

    internal static (Entry[] Entries, bool Exact) Match(string name, bool allowGuess = false)
    {
        string key = NameKey(name);
        var catalog = Catalog.Value;
        if (catalog.Names.TryGetValue(key, out var exact)) return (exact, true);
        if (key.StartsWith('[')) return ([], false);

        // OCR inserts punctuation inside words and loses spaces. Keep digits and rating markers.
        string compact = CompactKey(key);
        if (catalog.Compact.TryGetValue(compact, out exact)) return (exact, true);
        if (!allowGuess || compact.Length < 4) return ([], false);

        int budget = compact.Length >= 8 ? NameMatch.Unit + NameMatch.Unit / 2 : NameMatch.Unit / 2;
        var rating = compact.Where(NumberOrRating).ToArray();
        bool hasRatingMark = rating.Any(c => !char.IsDigit(c));
        Entry[]? match = null;
        foreach (var (candidate, entries) in catalog.Compact)
        {
            if (Math.Abs(candidate.Length - compact.Length) > 1) continue;
            if (!NameMatch.CloseFirst(compact[0], candidate[0]) && NameMatch.SubCost(compact[0], candidate[0]) > NameMatch.Unit / 2) continue;
            if ((hasRatingMark || candidate.Any(NumberOrRating)) && !candidate.Where(NumberOrRating).SequenceEqual(rating)) continue;
            if (NameMatch.Distance(compact, candidate, budget) > budget) continue;
            if (match != null) return ([], false);
            match = entries;
        }
        return (match ?? [], false);
    }

    static string CompactKey(string key) => Regex.Replace(key, @"[^\p{L}\p{Nd}★+]|[+](?!\s*\d)", "");
    static bool NumberOrRating(char c) => char.IsDigit(c) || c is '★' or '+';

    static string NameKey(string name) => Regex.Replace(
        ChatParser.StripAccents(name.Normalize(NormalizationForm.FormKC)).ToLowerInvariant()
            .Replace('’', '\'').Replace('–', '-').Replace("ß", "ss"), @"\s+", " ").Trim();

    static Index Load()
    {
        Dictionary<long, ItemInfo> items = new();
        Dictionary<string, List<Entry>> names = new(StringComparer.Ordinal);
        try
        {
            using var stream = DataFiles.Open(FileName);
            if (stream == null) return new(items, new(), new());
            using var json = JsonDocument.Parse(stream);
            foreach (var row in json.RootElement.GetProperty("items").EnumerateArray())
            {
                long id = row.GetProperty("id").GetInt64();
                string key = NameKey(row.GetProperty("de").GetString()!);
                string english = row.GetProperty("n").GetString()!;
                bool marked = key.EndsWith(BoundSuffix, StringComparison.Ordinal);
                if (marked) key = key[..^BoundSuffix.Length];
                var info = new ItemInfo(id, english, row.GetProperty("g").GetString()!,
                    !marked && row.GetProperty("t").GetBoolean(), "", row.GetProperty("c").GetString()!);
                items.Add(id, info);
                if (key == "kinah" || ItemDictionary.IsMonsterSoul(id)) continue;
                if (!names.TryGetValue(key, out var list)) names.Add(key, list = new());
                list.Add(new(info, marked));
            }
        }
        catch (Exception ex)
        {
            CrashLog.Note("German item catalog: " + ex.Message);
            return new(new(), new(), new());
        }
        var compact = names.GroupBy(pair => CompactKey(pair.Key), StringComparer.Ordinal)
            .Where(group => group.Key.Length > 0)
            .ToDictionary(group => group.Key, group => group.SelectMany(pair => pair.Value).ToArray(), StringComparer.Ordinal);
        return new(items, names.ToDictionary(pair => pair.Key, pair => pair.Value.ToArray(), StringComparer.Ordinal), compact);
    }
}
