using System.Text.RegularExpressions;
using KinahLedger.Data;
using KinahLedger.Session;

namespace KinahLedger.Ocr;

/// <summary>German system messages observed in AION 2's Global client.</summary>
internal static partial class GermanChat
{
    const string Number = @"[0-9OoIl]{1,3}(?:\.[0-9OoIl]{3})+|[0-9OoIl]+";
    const RegexOptions Options = RegexOptions.IgnoreCase | RegexOptions.CultureInvariant;
    const string ItemDetails = @"(?<bound>\s*\(\s*gebunden\s*\))?(?:\s*(?:\(\s*(?<count>" + Number + @")\s*[x×]\s*\)|[x×]\s*(?<count>" + Number + @")))?";
    const string ItemVerb = @"erha[l1!]['’]?[tg]en[.,!?:„]{0,2}[a-z]{0,2}";

    [GeneratedRegex(@"^\d[0-9OoIl.]*\s*\bkinah\b.*\b(?:gebunden|erhalten)\b|^seelenpun\s*kte\b", Options)]
    internal static partial Regex Signature();

    [GeneratedRegex(@"^(?<amount>" + Number + @")\s*kinah\s*(?<bound>\(\s*gebunden\s*\))?(?:\s*,\s*|\s+)erhalten[.!?]?$", Options)]
    private static partial Regex Kinah();

    [GeneratedRegex(@"^seelenpun\s*kte\s*\(\s*(?<amount>" + Number + @")\s*[x×]\s*\)\s*erhalten\s*:\s*(?<enemy>[^\[\]]+?)\s*[.!?]?$", Options)]
    private static partial Regex SoulPoints();

    [GeneratedRegex(@"^zielauswahl\s+erforderlich[.!]?$", Options)]
    internal static partial Regex TargetRequired();

    // MSG_ADD_ITEM and MSG_ADD_ITEM_MULTI: "{0} erhalten." and "{0} ({1}x) erhalten."
    [GeneratedRegex(@"^(?<name>\S[^\r\n]{0,199}?)" + ItemDetails + @"(?:\s*[,;:.'’–—-]\s*|\s+)" + ItemVerb + "$", Options)]
    private static partial Regex Item();

    [GeneratedRegex("^" + ItemDetails + @"\s*" + ItemVerb + "$", Options)]
    private static partial Regex PickupTail();

    internal static GameEvent? Currency(string line, DateTime time)
    {
        var k = Kinah().Match(line);
        if (k.Success && ChatParser.Digits(k.Groups["amount"].Value) is > 0 and var kinah)
            return new KinahChanged(time, kinah, k.Groups["bound"].Success);
        var s = SoulPoints().Match(line);
        if (s.Success && ChatParser.Digits(s.Groups["amount"].Value) is > 0 and <= 999999 and var points)
            return new CurrencyGained(time, "Soul Points", (int)points, s.Groups["enemy"].Value.Trim(' ', '\''));
        return null;
    }

    internal static (long Id, int Count, bool Bound, bool Exact)? Pickup(string line, ItemDictionary items)
    {
        var match = Item().Match(line);
        if (!match.Success) return null;
        var (candidates, exact) = GermanItemCatalog.Match(match.Groups["name"].Value, allowGuess: true);
        if (candidates.Length == 0) return null;

        bool bound = match.Groups["bound"].Success;
        // Prefer the proper variant for the marker. An unmarked item can also come only in a bound version.
        var preferred = bound
            ? candidates.Where(c => c.ExplicitlyBound).ToArray()
            : candidates.Where(c => !c.ExplicitlyBound && c.Info.Tradable).ToArray();
        if (preferred.Length == 0) preferred = candidates.Where(c => c.Info.Tradable != bound).ToArray();
        if (preferred.Length == 0) preferred = candidates;

        var first = preferred[0].Info;
        // Several IDs can describe the same grade and variant. Distinct items need more evidence than their text.
        if (preferred.Any(c => c.Info.Name != first.Name || c.Info.Grade != first.Grade)) return null;
        long known = items.Find(first.Name) ?? 0;
        long id = items.FromGame(preferred.Any(c => c.Info.Id == known) ? known : first.Id);
        long count = match.Groups["count"].Success ? ChatParser.Digits(match.Groups["count"].Value) : 1;
        return count > 0 ? (id, (int)Math.Min(count, 9999), bound || !first.Tradable, exact) : null;
    }

    // Give the chat voter one line per pickup, including when only "erhalten." fits on the next row.
    internal static List<string> JoinPickupTails(List<string> lines, ItemDictionary items)
    {
        List<string>? joined = null;
        for (int i = 1; i < lines.Count; i++)
        {
            if (!PickupTail().IsMatch(ChatParser.StripAccents(lines[i].Trim()))) continue;
            var text = $"{lines[i - 1].Trim()} {lines[i].Trim()}";
            if (Pickup(ChatParser.CleanSystemLine(text), items) == null) continue;
            joined ??= new(lines);
            lines = joined;
            lines[i - 1] = text;
            lines.RemoveAt(i--);
        }
        return joined ?? lines;
    }

    internal static bool HasLootVerb(string line)
    {
        if (Kinah().IsMatch(line) || SoulPoints().IsMatch(line)) return true;
        var item = Item().Match(line);
        return item.Success && GermanItemCatalog.Match(item.Groups["name"].Value).Entries.Length > 0;
    }
}
