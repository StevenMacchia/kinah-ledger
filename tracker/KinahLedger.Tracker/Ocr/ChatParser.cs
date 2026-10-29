using System.Text;
using System.Text.RegularExpressions;
using KinahLedger.Data;
using KinahLedger.Session;

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

    /// <summary>"Acquired 520 Kina." The recognizer sometimes glues the verb to the number ("Acquiredn521 Kina?"), so the gap is loose.</summary>
    [GeneratedRegex(@"\bacquired[\sn]{0,2}[.,\-'`]?\s?(?<amt>[0-9OoIl][0-9OoIl,\. ]*?)\s*kinah?\b(?<rest>.*)$", RegexOptions.IgnoreCase)]
    private static partial Regex Kina();

    /// <summary>"A total payment of 34,916 Kina has been completed." The game prints it before the "Acquired 34,916 Kina." of a quest or payment reward.</summary>
    [GeneratedRegex(@"\bpayment\s+of\s+(?<amt>[0-9OoIl][0-9OoIl,\. ]*?)\s*kinah?\b", RegexOptions.IgnoreCase)]
    private static partial Regex Payment();

    // what the last payment line still owes and when it was read: the Kinah lines that make it up are payment, not
    // farming. A Market settlement pays out in several lines ("Acquired 44,998 Kina." "A total payment of 61,765 Kina has
    // been completed." "Acquired 16,767 Kina."), some before the total and some after it.
    long _paymentLeft; DateTime _paymentAt;

    [GeneratedRegex(@"\b(?:acquired|obtained|looted|picked up)\s+(?<name>.+?)(?:\s*[×xX\*]\s*(?<n>\d{1,4}))?\s*\.?\s*$", RegexOptions.IgnoreCase)]
    private static partial Regex Item();

    /// <summary>The game's own item line: "Fresh Fruit acquired." (name first). "acquire[da]" tolerates the common misread.</summary>
    [GeneratedRegex(@"^(?<name>[^\[\]]+?)(?:\s*[×xX\*]\s*(?<n>\d{1,4}))?\s+acqu[il1]r[eé]+[dao]?[.,]?\s*[a-z.]{0,2}$", RegexOptions.IgnoreCase)]
    private static partial Regex ItemNameFirst();

    [GeneratedRegex(@"\bdefeated\s+(?<mob>.+?)\s*\.?\s*$", RegexOptions.IgnoreCase)]
    private static partial Regex Kill();

    [GeneratedRegex(@"\b(?:gained|earned|received|obtained)\s+(?<xp>[0-9OoIl][0-9OoIl,\. ]*?)\s*(?:xp\b|exp\b|experience)", RegexOptions.IgnoreCase)]
    private static partial Regex Xp();

    [GeneratedRegex(@"^\s*title\s+acquired\b", RegexOptions.IgnoreCase)]
    private static partial Regex Title();

    /// <summary>"Essence Extraction failed": a gather attempt with no yield.</summary>
    [GeneratedRegex(@"^(?<skill>[^\[\]]*?(?:extraction|gathering|mining|logging|harvest(?:ing)?|collect(?:ion|ing)))\s+failed\.?\s*$", RegexOptions.IgnoreCase)]
    private static partial Regex GatherFail();

    // Market, craft and enchant lines (0.7). Only "Successfully crafted <item>" has been seen in a real reads.log so far;
    // the other shapes are the likely wordings, kept loose until the client's own lines are collected.
    const string Stack = @"(?:\s*\(?\s*[×xX\*]\s*(?<n>\d{1,5})\s*\)?)?";
    const string Price = @"(?<price>[0-9][0-9OoIl,\. ]*?)\s*kinah?\b";

    /// <summary>"Your Spiritstone x10 sold for 75,200 Kina." / "Spiritstone x10 has been sold for 75,200 Kina."</summary>
    [GeneratedRegex(@"^(?:your\s+)?(?<item>[^\[\]]+?)" + Stack + @"\s+(?:has\s+been\s+|have\s+been\s+|was\s+|were\s+)?sold\b(?:\s+(?:on|at|in|through)\s+the\s+market)?(?:\s+for)?[^0-9]*?" + Price, RegexOptions.IgnoreCase)]
    private static partial Regex SoldItemFirst();

    /// <summary>"Sold Spiritstone x10 for 75,200 Kina." / "The Market sold your Spiritstone x10 for 75,200 Kina."</summary>
    [GeneratedRegex(@"^(?:the\s+market\s+)?sold\s+(?:your\s+)?(?<item>[^\[\]]+?)" + Stack + @"\s+for\s+" + Price, RegexOptions.IgnoreCase)]
    private static partial Regex SoldVerbFirst();

    /// <summary>"Registered Spiritstone x10 on the Market for 75,200 Kina." / "You listed ..."</summary>
    [GeneratedRegex(@"^(?:you\s+(?:have\s+)?)?(?:registered|listed)\s+(?<item>[^\[\]]+?)" + Stack + @"\s+(?:on|to|in)\s+the\s+market(?:\s+for\s+" + Price + ")?", RegexOptions.IgnoreCase)]
    private static partial Regex ListedVerbFirst();

    /// <summary>"Spiritstone x10 has been registered on the Market for 75,200 Kina."</summary>
    [GeneratedRegex(@"^(?:your\s+)?(?<item>[^\[\]]+?)" + Stack + @"\s+(?:has\s+been\s+|was\s+)?(?:registered|listed)\b(?<market>\s+(?:on|to|in)\s+the\s+market)?(?:\s+for\s+" + Price + ")?", RegexOptions.IgnoreCase)]
    private static partial Regex ListedItemFirst();

    /// <summary>"Listing for Spiritstone x10 has expired." / "Registration period for Spiritstone has expired."</summary>
    [GeneratedRegex(@"^(?:the\s+)?(?:market\s+)?(?:listing|registration)(?:\s+period)?\s+(?:for|of)\s+(?:your\s+)?(?<item>[^\[\]]+?)" + Stack + @"\s+(?:has\s+)?expired", RegexOptions.IgnoreCase)]
    private static partial Regex ExpiredListingFirst();

    /// <summary>"Your Spiritstone x10 listing has expired." / "Spiritstone x10 has expired on the Market."</summary>
    [GeneratedRegex(@"^(?:your\s+)?(?<item>[^\[\]]+?)" + Stack + @"\s+(?:(?:market\s+)?(?:listing|registration)\s+(?:has\s+)?expired|(?:has\s+)?expired\b.*\bmarket)", RegexOptions.IgnoreCase)]
    private static partial Regex ExpiredItemFirst();

    /// <summary>"Successfully crafted Orichalcum Ingot." (seen in game), with a possible "Great success!" in front or a stack after.</summary>
    [GeneratedRegex(@"^(?:(?:great|critical)\s+success[!.:]?\s*)?(?:you\s+(?:have\s+)?)?successfully\s+crafted\s+(?<item>[^\[\]]+?)" + Stack + @"\s*[.!]?$", RegexOptions.IgnoreCase)]
    private static partial Regex Crafted();

    /// <summary>"Crafting failed." / "Failed to craft Orichalcum Ingot." / "Orichalcum Ingot crafting failed."</summary>
    [GeneratedRegex(@"^(?:craft(?:ing)?(?:\s+of\s+(?<item>[^\[\]]+?))?\s+(?:has\s+)?failed\b.*|(?:you\s+(?:have\s+)?)?failed\s+(?:to\s+craft|crafting)\s+(?<item>[^\[\]]+?)\s*[.!]?|(?<item>[^\[\]]+?)\s+craft(?:ing)?\s+(?:has\s+)?failed\b.*)$", RegexOptions.IgnoreCase)]
    private static partial Regex CraftFailed();

    [GeneratedRegex(@"\benchant(?:ed|ing|ment)?\b", RegexOptions.IgnoreCase)]
    private static partial Regex EnchantWord();

    [GeneratedRegex(@"\b(?:fail(?:ed|ure)?|unsuccessful)\b", RegexOptions.IgnoreCase)]
    private static partial Regex FailWord();

    [GeneratedRegex(@"\b(?:succe(?:ss|ssful|ssfully|eded)|succeeded)\b", RegexOptions.IgnoreCase)]
    private static partial Regex SuccessWord();

    [GeneratedRegex(@"\+\s?(?<lvl>\d{1,2})\b")]
    private static partial Regex Level();

    [GeneratedRegex(@"\b(?:dropp?ed|decreased|reduced|reset|fell|lowered)\b", RegexOptions.IgnoreCase)]
    private static partial Regex DropWord();

    /// <summary>What's left of an enchant line once the verb phrases and levels are taken out: the item's name, or "" when the line has none.</summary>
    [GeneratedRegex(@"(?:\byou\s+have\b|\bsuccessfully\b|\bhas\s+been\b|\bwas\b|\bfailed\s+to\s+enchant\b|\benchant(?:ed|ing|ment)?\b|\bsucce(?:ss|ssful|eded)\b|\bfail(?:ed|ure)?\b|\bunsuccessful\b|\bto\b\s*(?=\+)|\+\s?\d{1,2}\b|[:!])", RegexOptions.IgnoreCase)]
    private static partial Regex EnchantNoise();

    /// <summary>Market, craft and enchant lines. Checked before the loot rules, since a sale line mentions Kina and a craft is followed by its item.</summary>
    IEnumerable<GameEvent> ParseTrade(string line, DateTime time)
    {
        Match m;
        if ((m = SoldVerbFirst().Match(line)).Success || (m = SoldItemFirst().Match(line)).Success)
        {
            var item = CleanName(m.Groups["item"].Value); long total = Digits(m.Groups["price"].Value);
            if (item.Length >= 2 && total > 0) yield return new MarketSold(time, item, Count(m), total);
            yield break;
        }
        if ((m = ListedVerbFirst().Match(line)).Success || ((m = ListedItemFirst().Match(line)).Success && (m.Groups["market"].Success || m.Groups["price"].Success)))
        {
            var item = CleanName(m.Groups["item"].Value);
            if (item.Length >= 2) yield return new MarketListed(time, item, Count(m), m.Groups["price"].Success ? Digits(m.Groups["price"].Value) : 0);
            yield break;
        }
        if ((m = ExpiredListingFirst().Match(line)).Success || (m = ExpiredItemFirst().Match(line)).Success)
        {
            var item = CleanName(m.Groups["item"].Value);
            if (item.Length >= 2) yield return new MarketExpired(time, item, Count(m));
            yield break;
        }
        if ((m = Crafted().Match(line)).Success)
        {
            var item = CleanName(m.Groups["item"].Value);
            if (item.Length >= 2) yield return new CraftResult(time, item, true);
            yield break;
        }
        if ((m = CraftFailed().Match(line)).Success)
        {
            yield return new CraftResult(time, CleanName(m.Groups["item"].Value), false);
            yield break;
        }
        if (EnchantWord().IsMatch(line) && !line.Contains("acqui", StringComparison.OrdinalIgnoreCase) && !line.Contains("obtain", StringComparison.OrdinalIgnoreCase))
        {
            bool fail = FailWord().IsMatch(line), ok = !fail && SuccessWord().IsMatch(line);
            if (!fail && !ok) yield break;   // "Enchantment Stone" in some other line: neither outcome, not an attempt
            var levels = Level().Matches(line).Select(x => int.Parse(x.Groups["lvl"].Value)).ToList();
            int from = -1, to = -1;
            if (ok) { if (levels.Count >= 2) (from, to) = (levels[0], levels[1]); else if (levels.Count == 1) (from, to) = (levels[0] - 1, levels[0]); }
            else if (levels.Count >= 2) (from, to) = (levels[0], levels[0] + 1);
            else if (levels.Count == 1 && !DropWord().IsMatch(line)) (from, to) = (levels[0], levels[0] + 1);
            // "Enchanting failed. The level dropped to +9.": the sentence about the new level doesn't name the item
            var named = string.Join(' ', line.Split('.').Where(s => !DropWord().IsMatch(s)));
            var item = CleanName(EnchantNoise().Replace(named, " "));
            yield return new EnchantResult(time, item, from, to, ok);
        }
    }

    /// <summary>
    /// One read's new lines, parsed in order. A payment line then also claims the Kinah lines just before it in the same
    /// read when they make up what it still owes, exactly: they were the payment's first lines, not loot.
    /// </summary>
    public List<(string Line, List<GameEvent> Events)> ParseBatch(IReadOnlyList<string> lines, DateTime time, bool wrap = true)
    {
        var outp = new List<(string Line, List<GameEvent> Events)>();
        int pay = -1;
        // "Purchase complete." claims the item lines after it in this read, and in the next read when it comes within
        // PurchaseSeconds: what was bought is never a drop
        bool claim = _purchaseCarry && (time - _purchaseAt).TotalSeconds <= PurchaseSeconds;
        _purchaseCarry = false;
        var parsed = wrap ? Unwrap(lines, time) : lines.Select(l => (Text: l, Events: Parse(l, time).ToList())).ToList();
        for (int i = 0; i < lines.Count; i++)
        {
            var (text, evs) = parsed[i];
            if (Purchase().IsMatch(text)) { claim = true; _purchaseAt = time; _purchaseCarry = true; }
            else if (claim) evs = evs.Select(AsReward).ToList();
            outp.Add((text, evs));
            if (Payment().IsMatch(text)) pay = i;
        }
        if (pay < 0 || _paymentLeft <= 0) return outp;
        var cands = new List<(int line, int ev, long amt)>();
        for (int i = pay - 1; i >= 0 && cands.Count < 12; i--)
            for (int j = 0; j < outp[i].Events.Count; j++)
                if (outp[i].Events[j] is KinahChanged { Bound: false, Delta: > 0 } k) cands.Add((i, j, k.Delta));
        for (int mask = 1; mask < (1 << cands.Count); mask++)
        {
            long sum = 0;
            for (int b = 0; b < cands.Count; b++) if ((mask & (1 << b)) != 0) sum += cands[b].amt;
            if (sum != _paymentLeft) continue;
            for (int b = 0; b < cands.Count; b++)
                if ((mask & (1 << b)) != 0) { var c = cands[b]; outp[c.line].Events[c.ev] = new KinahChanged(time, c.amt, true); }
            _paymentLeft = 0;
            break;
        }
        return outp;
    }

    /// <summary>
    /// The chat wraps a line at about 40 characters, so a long item name comes as two lines ("Artisan's Orichalcum" /
    /// "Longsword acquired.", "Acquired" / "169 Kina."), and neither half counts on its own, or the second half reads as
    /// a shorter item. A line that parsed to nothing is joined to the next when the two together make an item the list
    /// holds, or Kinah. When the second half is itself a listed item, the join wins only if the joined name ends with that
    /// item's name (so a stray word before "Raw Leather acquired." never changes it). The last line of a read that parsed
    /// to nothing is held for WrapSeconds, since its second half can come in the next read. Every line keeps its place:
    /// a consumed second half is returned as JoinedMark with no events.
    /// </summary>
    List<(string Text, List<GameEvent> Events)> Unwrap(IReadOnlyList<string> lines, DateTime time)
    {
        var res = lines.Select(l => (Text: l, Events: Parse(l, time).ToList())).ToList();
        string? carry = _wrapCarry != null && (time - _wrapAt).TotalSeconds <= WrapSeconds ? _wrapCarry : null;
        _wrapCarry = null;
        for (int i = carry != null ? -1 : 0; i + 1 < res.Count; i++)
        {
            string a = i < 0 ? carry! : res[i].Text;
            bool aEmpty = i < 0 || res[i].Events.Count == 0;
            var b = res[i + 1];
            if (!aEmpty || !Joinable(a) || !Joinable(b.Text)) continue;
            var bItem = b.Events.Count == 1 && b.Events[0] is ItemAcquired bi ? bi : null;
            if (b.Events.Count > 0 && bItem == null) continue;
            var joined = a.Trim() + " " + b.Text.Trim();
            var j = Parse(joined, time).ToList();
            if (j.Count != 1 || j[0].Via.Length > 0) continue;   // a guessed name is no proof two lines belong together
            bool take = bItem == null
                ? j[0] is ItemAcquired || j[0] is KinahChanged
                : j[0] is ItemAcquired ji && ji.ItemId != bItem.ItemId
                  && _items.NameOf(ji.ItemId).EndsWith(_items.NameOf(bItem.ItemId), StringComparison.OrdinalIgnoreCase);
            if (!take) continue;
            res[i + 1] = (joined, j);
            if (i >= 0) res[i] = (JoinedMark, new List<GameEvent>());
            i++;
        }
        // a wrapped tail under an item line that already read as a whole name: "Acquired Artisan's Ultimate Refining Stone"
        // then "(Bound) x2." (Steven, 2026-10-23 12:39: counted as one drop, though the "(Bound)" made it a reward). The
        // tail belongs to the line above: joined, its count and its "(Bound)" count too
        for (int i = 0; i + 1 < res.Count; i++)
        {
            if (!WrappedTail.IsMatch(res[i + 1].Text) || res[i + 1].Events.Count > 0 || ReferenceEquals(res[i].Text, JoinedMark)) continue;
            if (res[i].Events.Count != 1 || res[i].Events[0] is not ItemAcquired || Regex.IsMatch(res[i].Text, @"\(\s*b[o0]und\s*\)", RegexOptions.IgnoreCase)) continue;
            var joined = res[i].Text.Trim() + " " + res[i + 1].Text.Trim();
            var j = Parse(joined, time).ToList();
            if (j.Count != 1 || j[0] is not (ItemAcquired or RewardReceived)) continue;
            res[i] = (joined, j);
            res[i + 1] = (JoinedMark, new List<GameEvent>());
        }
        if (res.Count > 0 && res[^1].Events.Count == 0 && !ReferenceEquals(res[^1].Text, JoinedMark) && Joinable(res[^1].Text) && res[^1].Text.Trim().Length >= 3)
        { _wrapCarry = res[^1].Text; _wrapAt = time; }
        return res;
    }

    /// <summary>
    /// The same tail rule for lines that are parsed one at a time (a burst read that didn't line up, the lines counted once
    /// the chat settled): "Acquired Amplify Stone Fragment (Unique)" then "(Bound) x9." in the read is one line, x9
    /// (2026-10-25 20:06:51: the burst's first half counted as one, "quantity unsure, 1 assumed", and the table's witness took
    /// that 1 for the chat's own count and set the slot's x9 not read). Two lines are joined only when they stand one under
    /// the other in <paramref name="read"/> (the read they came from), the first an item line without "(Bound)" and the
    /// second a tail alone, and the joined line parses to one item. The tail may be missing from <paramref name="lines"/>
    /// (the settle takes only lines with a verb): it is the read's line under the item line. Each result keeps the lines it
    /// was made of.
    /// </summary>
    public List<(string Text, IReadOnlyList<string> Parts)> JoinWrappedTails(IReadOnlyList<string> lines, IReadOnlyList<string> read, DateTime time)
    {
        var res = new List<(string, IReadOnlyList<string>)>();
        for (int i = 0; i < lines.Count; i++)
        {
            if (TailUnder(read, lines[i]) is { } tail && !Regex.IsMatch(lines[i], @"\(\s*b[o0]und\s*\)", RegexOptions.IgnoreCase))
            {
                var first = Parse(lines[i], time).ToList();
                if (first.Count == 1 && first[0] is ItemAcquired or RewardReceived)
                {
                    var joined = lines[i].Trim() + " " + tail.Trim();
                    var j = Parse(joined, time).ToList();
                    if (j.Count == 1 && j[0] is ItemAcquired or RewardReceived)
                    {
                        res.Add((joined, new[] { lines[i], tail }));
                        if (i + 1 < lines.Count && lines[i + 1].Trim() == tail.Trim()) i++;
                        continue;
                    }
                }
            }
            res.Add((lines[i], new[] { lines[i] }));
        }
        return res;
    }

    /// <summary>The wrapped tail ("(Bound) x9.") right under <paramref name="a"/> in the read, or null.</summary>
    static string? TailUnder(IReadOnlyList<string> read, string a)
    {
        for (int k = 0; k + 1 < read.Count; k++)
            if (read[k].Trim() == a.Trim() && WrappedTail.IsMatch(read[k + 1])) return read[k + 1];
        return null;
    }

    /// <summary>The text returned in place of the first half of a line joined to the one after it.</summary>
    public const string JoinedMark = "(wrapped: joined to the next line)";
    /// <summary>The end of a wrapped item line on its own: "(Bound) x2.", "(Bound).", "x12.".</summary>
    static readonly Regex WrappedTail = new(@"^\s*(?:\(\s*b[o0]und\s*\)\s*(?:[x×*]\s*[0-9][0-9,]*)?|[x×]\s*[0-9][0-9,]*)\s*[.,:;]?\s*$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    public const double WrapSeconds = 3;
    string? _wrapCarry; DateTime _wrapAt;
    static bool Joinable(string s) { var t = s.TrimStart(); return t.Length > 0 && !t.StartsWith('[') && !Payment().IsMatch(t) && !Purchase().IsMatch(t); }

    [GeneratedRegex(@"^\W*purchase\s+(?:complete|completed|successful)\b", RegexOptions.IgnoreCase)]
    private static partial Regex Purchase();


    public const double PurchaseSeconds = 5;
    DateTime _purchaseAt; bool _purchaseCarry;

    /// <summary>
    /// The same event as a reward (a reward screen was open, or it was bought): Kinah becomes reward Kinah, an item a
    /// skipped reward line. Neither is farming, a drop or a kill; both show in the summary's "reward lines skipped".
    /// </summary>
    public GameEvent AsReward(GameEvent ev) => ev switch
    {
        KinahChanged { Bound: false } k => new KinahChanged(k.Time, k.Delta, true),
        ItemAcquired a => new RewardReceived(a.Time, _items.NameOf(a.ItemId), a.Count),
        _ => ev,
    };

    static int Count(Match m) => m.Groups["n"].Success && int.TryParse(m.Groups["n"].Value, out var n) ? Math.Clamp(n, 1, 99999) : 1;

    // ---- quantities (2026-10-24, 13:07: "Acquired Odyle x7." read as "Acquired Odyle" and filed as one) ----

    /// <summary>
    /// A count at the end of a name: "x", "×", "X" or "*", then up to four digits or letters the recognizer reads for digits,
    /// or a big count with its thousands marks ("x2,000", read "x2,OOO": 2026-10-25 13:48, a Daeva Pass Shop Coin line that
    /// came in as "not in the item list" with the count in its name, 15 times that day).
    /// </summary>
    static readonly Regex CountTailRx = new(@"^(?<head>.*?[^\s×xX\*])(?<gap>\s*)[×xX\*](?<sp>\s*)(?<n>[0-9OoIl|SsbTt]{1,3}(?:[,.][0-9OoIl|]{3})+|[0-9OoIl|SsbTt]{1,4})\s*[.,:;!]?\s*$", RegexOptions.Compiled);

    /// <summary>
    /// The count at the end of a chat line's name, read through the recognizer's usual digit slips (O and o for 0; l, I and
    /// | for 1; S and s for 5; b for 6; T and t for 7): "Refining StonevxT" is Refining Stonev x7, "Odyle xb" x6,
    /// "Kinæx382" Kinæ x382, "Daeva Pass Shop Coin x2,OOO" Daeva Pass Shop Coin x2000 (thousands marks drop out). A count
    /// of letters only must follow the x straight away and be one or two long, so a word after a space ("Treasure Box II")
    /// is never a count; one with thousands marks must start with a digit. Null when the text ends in no count. The caller
    /// keeps the whole name when it is an item's own name.
    /// </summary>
    public static (string Head, int Count)? QuantityTail(string text)
    {
        var m = CountTailRx.Match(text.Trim());
        if (!m.Success) return null;
        var tok = m.Groups["n"].Value;
        if (!tok.Any(char.IsDigit) && (tok.Length > 2 || m.Groups["sp"].Length > 0 || (m.Groups["gap"].Length > 0 && tok.All(c => c is 'I' or 'l' or '|')))) return null;
        if (tok.IndexOfAny(new[] { ',', '.' }) >= 0 && !char.IsDigit(tok[0])) return null;
        var sb = new StringBuilder();
        foreach (var c in tok)
            if (c is not (',' or '.'))
                sb.Append(c switch { 'O' or 'o' => '0', 'I' or 'l' or '|' => '1', 'S' or 's' => '5', 'b' => '6', 'T' or 't' => '7', _ => c });
        if (!int.TryParse(sb.ToString(), out var n) || n < 1) return null;
        var head = m.Groups["head"].Value.TrimEnd();
        return head.Length >= 2 ? (head, n) : null;
    }

    /// <summary>True when the line names a count after its item ("x7.", "xT", "× 12") or before it ("Acquired 3 ..."), however garbled.</summary>
    public static bool HasCount(string raw)
    {
        var line = CleanSystemLine(Timestamp().Replace(raw.Trim(), ""));
        return QuantityTail(line) != null || Regex.IsMatch(line, @"^\W*acqu\w*\s+\d", RegexOptions.IgnoreCase);
    }

    /// <summary>
    /// Set by the session: the count of the loot list's row for this item, read in the last few seconds with its icon, or
    /// null. Asked only when the chat line's own count is missing or unreadable and its name is settled.
    /// </summary>
    public Func<long, DateTime, int?>? ListCount { get; set; }

    // the raw lines whose count was left unsure on their last parse (verb-first, no count readable, none on the loot list)
    readonly HashSet<string> _countUnsure = new();
    readonly Queue<string> _countUnsureOrder = new();

    /// <summary>
    /// Whether the last parse of this line counted an item without knowing how many: a verb-first line ("Acquired Odyle")
    /// whose count was lost or unreadable and that the loot list didn't show. The session then looks at the line's place in
    /// the next reads (Reread.CountWatch) and, failing that, logs "quantity unsure, 1 assumed". A name-first line ("Odyle
    /// acquired.") is always one.
    /// </summary>
    public bool CountUnsure(string raw) { lock (_countUnsure) return _countUnsure.Contains(raw.Trim()); }

    void NoteCount(string raw, bool unsure)
    {
        var k = raw.Trim();
        lock (_countUnsure)
        {
            if (!unsure) { _countUnsure.Remove(k); return; }
            if (_countUnsure.Add(k)) { _countUnsureOrder.Enqueue(k); if (_countUnsureOrder.Count > 200) _countUnsure.Remove(_countUnsureOrder.Dequeue()); }
        }
    }

    /// <summary>Lines whose count was taken from their own garbled tail, and from the loot list's row.</summary>
    public int CountsFromTail { get; private set; }
    public int CountsFromList { get; private set; }

    bool KnowsQuiet(string name)
    {
        bool was = Session.Accuracy.Muted;
        Session.Accuracy.Muted = true;   // asking the item list isn't a misread
        try { return _items.Knows(name); }
        finally { Session.Accuracy.Muted = was; }
    }

    static string CleanName(string s)
    {
        var name = Regex.Replace(s, @"\s+", " ").Trim().TrimEnd('.', ',', ';', ':', '\'', '"', '`', '-', ' ').Trim('[', ']', ' ');
        name = Regex.Replace(name, @"^(?:your|the|an?)\s+", "", RegexOptions.IgnoreCase);
        return name.Length > 64 ? name[..64] : name;
    }

    /// <summary>
    /// A system line as the recognizer garbles it, put back in shape before it is parsed. Fast kills make the chat scroll
    /// while lines fade in, and the reads that come back carry stray marks around the words that decide what a line is:
    /// "Raw Leather *acquired.«", "Raw Leatheracquired.", "SpiritStone—acquired.", "Spiritstone acquired:-",
    /// "cquired 347 Kina." (all from a live reads.log, 2026-10-21). Item names themselves are left alone; the item
    /// list still decides what an item is, so a cleaned line can never turn something unlisted into a drop.
    /// </summary>
    public static string CleanSystemLine(string line)
    {
        // accents the recognizer adds to plain letters ("Acquiréd 169 Kina.", "AcquireÖ"): the game's English text has none
        var s = StripAccents(line);
        // marks that never belong to a chat line; a * only stays when it is a count ("*2" for ×2)
        s = Regex.Replace(s, @"[«»•·¤~^`´¨""“”|¦]", " ");
        s = Regex.Replace(s, @"\*(?!\s?\d)", " ");
        // the verb's r read as a t ("Riverside Pauldronsacquited,", 2026-10-24 13:07): one letter, still the verb
        s = Regex.Replace(s, @"(acqu[il1])t(?=[eé]+[dao]?(?:\W|$))", "$1r", RegexOptions.IgnoreCase);
        // a dash, underscore, colon or no space at all between the name and the verb: "SpiritStone—acquired",
        // "Leatheracquired", "Riverside Pauldröns:acquired." (the second of two Pauldrons lost to it, 2026-10-24 13:07)
        s = Regex.Replace(s, @"(?<=\w)[\-—–_:;,]+(?=acqu)", " ", RegexOptions.IgnoreCase);
        s = Regex.Replace(s, @"(?<=[a-z\)])(?=acqu[il1]r)", " ");
        // the first letter or two of "Acquired" lost while the line faded in, before an amount of Kina
        s = Regex.Replace(s, @"^\W*(?:a?c|c?q)?quired(?=\s*[0-9OoIl])", "Acquired", RegexOptions.IgnoreCase);
        s = Regex.Replace(s, @"^\W*cquired\b", "Acquired", RegexOptions.IgnoreCase);
        // a stray mark inside a Kinah amount ("Acquired 31>2. Kina." for 312, 2026-10-22 11:02): the digits are joined
        if (Regex.IsMatch(s, @"acqu\w*\s.*\bkin", RegexOptions.IgnoreCase)) s = Regex.Replace(s, @"(?<=\d)[^\d\s.,]{1,2}(?=\d)", "");
        // the verb's last letter misread before an amount: "AcquireO 169 Kina.", "Acquirea 212 Kina."
        s = Regex.Replace(s, @"^\W*acqu[il1]re[dao0]?(?=\s+[0-9OoIl])", "Acquired", RegexOptions.IgnoreCase);
        // trailing marks after the line's own full stop: "acquired:-", "acquired.«", "acquiréd.i"
        s = Regex.Replace(s, @"(acqu[il1]r[eé]+[dao]?)\s*[.,:;]?[\W_]*[a-z]?\W*$", "$1.", RegexOptions.IgnoreCase);
        return Regex.Replace(s, @"\s+", " ").Trim();
    }

    internal static string StripAccents(string s)
    {
        if (s.All(c => c < 128)) return s;
        var d = s.Normalize(System.Text.NormalizationForm.FormD);
        var b = new System.Text.StringBuilder(d.Length);
        foreach (var c in d)
            if (System.Globalization.CharUnicodeInfo.GetUnicodeCategory(c) != System.Globalization.UnicodeCategory.NonSpacingMark) b.Append(c);
        return b.ToString().Normalize(System.Text.NormalizationForm.FormC);
    }

    /// <summary>Item names likely right now (this run's items, the loot tables of the enemies being fought), for guessing a partly hidden name.</summary>
    public Func<IEnumerable<string>>? Likely { get; set; }

    /// <summary>
    /// The enemies being fought in the open world right now (the target bar, the nameplates), whose loot tables can say an
    /// item can't drop here. Empty inside events and expeditions, where loot comes from chests and rewards.
    /// </summary>
    public Func<IReadOnlyCollection<string>>? Fighting { get; set; }

    /// <summary>True for an item that must always count (Legend, Unique, Epic, boss gear): a guess at one is never held back.</summary>
    public Func<long, bool>? Rare { get; set; }
    /// <summary>A run is open (an event, an expedition, a Transcendence, or just left): every reward there is the run's. Never set while farming.</summary>
    public Func<bool>? InRun { get; set; }

    /// <summary>Each grade's colour as printed on this PC, learned from names read exactly (null: names aren't judged by colour).</summary>
    public GradeColours? Colours { get; set; }

    /// <summary>Close names settled by the name's colour, by the enemy's loot table or the run's items, and guesses held back as no table's.</summary>
    public int ColourPicks { get; private set; }
    public int TablePicks { get; private set; }
    public int HeldUnlisted { get; private set; }

    /// <summary>
    /// The candidate sets for the moment (Data.NameContext: the enemies' loot tables, the gathering materials, the open
    /// recipe, the run's items), for settling a misread name; null: the whole list only, as before 2026-10-24.
    /// </summary>
    public Func<Data.NameContext?>? Context { get; set; }
    /// <summary>Misreads settled by a context set other than the list's own closest, and misreads refused as on no nearby table.</summary>
    public int ContextPicks { get; private set; }
    public int Refused { get; private set; }

    // the last reads' name colours by line text (ScreenReader.LastAccents): a voted or re-read line is one of those texts
    readonly Dictionary<string, System.Drawing.Color> _accents = new();
    readonly Queue<string> _accentOrder = new();

    /// <summary>The name colours of a read, kept for the last few hundred lines.</summary>
    public void NoteAccents(IEnumerable<(string Text, System.Drawing.Color Accent)> lines)
    {
        lock (_accents)
            foreach (var (t, c) in lines)
            {
                if (!_accents.ContainsKey(t)) { _accentOrder.Enqueue(t); if (_accentOrder.Count > 400) _accents.Remove(_accentOrder.Dequeue()); }
                _accents[t] = c;
            }
    }

    System.Drawing.Color? AccentOf(string rawLine)
    {
        lock (_accents) return _accents.TryGetValue(rawLine.Trim(), out var c) ? c : null;
    }

    string GradeOf(long id) => _items.Get(id)?.Grade ?? "";

    /// <summary>Items the name's colour doesn't rule out, or null when the colour says nothing (none measured, nothing learned).</summary>
    Func<long, bool>? GradeFilter(System.Drawing.Color? accent) =>
        accent is { } a && Colours is { } book && book.Learned().Count >= 2 ? id => book.Agrees(GradeOf(id), a) != false : null;

    /// <summary>
    /// The item a name on the item list is. Read exactly: that item, and its colour teaches the grade's colour. A misread:
    /// the closest item, as before, unless the close ones say otherwise. The name's colour rules out items of another
    /// grade (so "right letters, wrong grade" goes to the item of the right grade), and of two items as close as each
    /// other the one on this run or the enemy's loot table wins, then the one whose grade the colour confirms; still a
    /// tie, the list's own closest (2026-10-23).
    /// </summary>
    long Choose(string name, System.Drawing.Color? accent)
    {
        if (_items.IsExact(name))
        {
            long id = _items.IdOf(name);
            if (accent is { } a && Colours != null && GradeOf(id) is { Length: > 0 } g) Colours.Observe(g, a);
            return id;
        }
        long snap = _items.IdOf(name);
        var near = _items.Near(name, Math.Max(1, name.Trim().Length / 6));
        if (near.Count == 0) return snap;
        var pool = near;
        var filter = GradeFilter(accent);
        if (filter != null) { var kept = near.Where(c => filter(c.Id)).ToList(); if (kept.Count > 0) pool = kept; }
        int best = pool.Min(c => c.Distance);
        var tied = pool.Where(c => c.Distance == best).ToList();
        string why = filter != null && pool.Count < near.Count ? "its colour" : "";
        if (tied.Count > 1)
        {
            var likely = Likely?.Invoke()?.Select(x => x.ToLowerInvariant()).ToHashSet() ?? new HashSet<string>();
            var listed = tied.Where(c => likely.Contains(c.Name.ToLowerInvariant())).ToList();
            var coloured = accent is { } a2 && Colours != null ? tied.Where(c => Colours.Agrees(c.Grade, a2) == true).ToList() : new();
            if (listed.Count == 1) { tied = listed; why = "the run or the enemy's loot table"; }
            else if (coloured.Count == 1) { tied = coloured; why = "its colour"; }
            else return snap;
        }
        // nothing said otherwise: the list's own closest, exactly as before
        if (why.Length == 0) return snap;
        long pick = tied[0].Id;
        if (pick != snap && why.Length > 0)
        {
            if (why == "its colour") ColourPicks++; else TablePicks++;
            CrashLog.Note($"\"{name}\" is {_items.NameOf(pick)}, not {_items.NameOf(snap)}: {why} says so");
        }
        return pick;
    }

    /// <summary>
    /// The open-world enemies being fought whose loot tables are known, when none of them lists the item; null when a table
    /// lists it, when no table is known, or for a gathering material (those come from nodes, not loot tables).
    /// </summary>
    List<string>? Unlisted(long id)
    {
        var fought = Fighting?.Invoke();
        if (fought == null || fought.Count == 0) return null;
        if (_items.Get(id)?.Category == "Gathering Material") return null;
        var book = LootBook.Current;
        var known = fought.Where(book.Knows).Distinct().ToList();
        if (known.Count == 0) return null;
        var nm = _items.NameOf(id);
        return known.Any(e => book.Find(e, nm, id) != null) ? null : known;
    }

    /// <summary>
    /// What a line counts as, for the vote across reads (ChatVoter), without counting anything: "k:463" for Kinah,
    /// "i:raw leather x1" for an item on the list, and whether the name was read exactly; ("", false) for anything else.
    /// </summary>
    public (string Key, bool Exact) Meaning(string raw)
    {
        var line = Timestamp().Replace(raw.Trim(), "");
        if (line.Length == 0 || line.StartsWith('[')) return ("", false);
        line = CleanSystemLine(line);
        if (SomeoneElses(line)) return ("", false);
        line = WithoutDistribution(line);
        var k = Kina().Match(line);
        if (k.Success) { long a = Digits(k.Groups["amt"].Value); return a > 0 ? ("k:" + a, true) : ("", false); }
        var it = ItemNameFirst().Match(line);
        if (!it.Success) it = Item().Match(line);
        if (!it.Success) return ("", false);
        var name = it.Groups["name"].Value.Trim().TrimEnd('.', ',', ';', ':', '\'', '"', '`', '-', ' ');
        int n = it.Groups["n"].Success && int.TryParse(it.Groups["n"].Value, out var nn) ? nn : 1;
        var lead = Regex.Match(name, @"^(\d{1,4})[\s\-]+(.+)$");
        if (lead.Success) { n = int.Parse(lead.Groups[1].Value); name = lead.Groups[2].Value; }
        name = Regex.Replace(name, @"\s*\(bound\)\s*", " ", RegexOptions.IgnoreCase).Trim();
        // a count the recognizer garbled ("Odyle xT", "Refining StonevxT"): read through its digit slips
        if (!it.Groups["n"].Success && !lead.Success && !_items.IsExact(name) && QuantityTail(name) is { } qt && KnowsQuiet(qt.Head)) { name = qt.Head; n = qt.Count; }
        if (name.Length < 2 || !_items.Knows(name)) return ("", false);
        var canon = _items.NameOf(_items.IdOf(name));
        return ("i:" + canon.ToLowerInvariant() + "x" + n, string.Equals(canon, name, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Another player's loot, as the Force (or a group) shares it: "Yarie acquired Soul: Ursus (Bound) through Force
    /// distribution." Something stands before "acquired" and something follows it, where the player's own lines are
    /// "Acquired 23 Kina." or "Spiritstone acquired." Never the player's drop or kill (Steven, 2026-10-22, a world boss
    /// counted as killed once per Force member who got its soul).
    /// </summary>
    internal static bool SomeoneElses(string line) =>
        Regex.IsMatch(line, @"^\W*(?!acqu)\S+.*?acqu\w*\s+\S{3}", RegexOptions.IgnoreCase);

    /// <summary>
    /// The player's own share of a Force's loot ("Acquired Artwork: Furious Saursus through Force distribution."): the
    /// item is what comes before "through", so the name is found in the item list.
    /// </summary>
    internal static string WithoutDistribution(string line) =>
        Regex.Replace(line, @"\s+through(?:\s+\S*)?\s*(?:distri\S*)?\s*\.?\s*$", ".", RegexOptions.IgnoreCase);

    public IEnumerable<GameEvent> Parse(string rawLine, DateTime time)
    {
        var line = Timestamp().Replace(rawLine.Trim(), "");
        if (line.Length == 0) yield break;
        if (!line.StartsWith('[')) line = CleanSystemLine(line);
        if (line.Length == 0) yield break;
        if (line.StartsWith('[') || Title().IsMatch(line)) yield break;   // player chat, or a title being claimed
        if (SomeoneElses(line)) yield break;
        line = WithoutDistribution(line);
        if (line.StartsWith("Increased ", StringComparison.OrdinalIgnoreCase)) yield break;   // "Increased Pet Insight Level ...": progress, not loot

        var trade = ParseTrade(line, time).ToList();
        if (trade.Count > 0) { foreach (var ev in trade) yield return ev; yield break; }

        var gf = GatherFail().Match(line);
        if (gf.Success) { yield return new GatherFailed(time, gf.Groups["skill"].Value.Trim()); yield break; }

        var pay = Payment().Match(line);
        if (pay.Success) { _paymentLeft = Digits(pay.Groups["amt"].Value); _paymentAt = time; yield break; }

        var k = Kina().Match(line);
        if (k.Success)
        {
            long amt = Digits(k.Groups["amt"].Value);
            // a quest or payment reward: the payment line just before named this amount, or the line says what it is.
            // Counted like bound Kinah: shown, never farming income, never a kill.
            bool paid = amt > 0 && amt <= _paymentLeft && (time - _paymentAt).TotalSeconds <= 15;
            if (paid || Regex.IsMatch(line, @"\b(?:quest|reward|payment|completed)\b", RegexOptions.IgnoreCase))
            {
                if (paid) _paymentLeft -= amt;
                if (amt > 0) yield return new KinahChanged(time, amt, true);
                yield break;
            }
            // Kina from a sale or the mail isn't farming income: logged, never counted as a kill
            if (Regex.IsMatch(line, @"\b(?:market|sale|sold|mail|proceeds)\b", RegexOptions.IgnoreCase)) { if (amt > 0) yield return new RewardReceived(time, "Market proceeds", 1); yield break; }
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

        // Every "Obtained ..." line the game prints is a Soul Crystal or Soul Points line (71 of 71 in the logs so far), and the
        // recognizer garbles "Soul" freely ("Sput Crystals", "SouUPoints", "Soul-Points"). So the verb decides: the line is a pet
        // crystal when its last word is close to "Crystals", a per-kill payout when it is close to "Points" (or the line is cut
        // off at "Soul"), and otherwise nothing. An Obtained line is never a drop.
        var ob = ObtainedLine().Match(line);
        if (ob.Success)
        {
            var words = ob.Groups["name"].Value.Split(new[] { ' ', '-', '‑', '–' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(w => w.Trim('.', ',', ';', ':', '!', '?', '\'', '"', '`')).Where(w => w.Length > 0).ToList();
            if (words.Count == 0) yield break;
            string last = Letters(words[^1]);
            bool crystal = last.Contains("cryst") || last.Contains("ystal") || Dist(last, "crystals") <= 2 || Dist(last, "crystal") <= 2;
            bool points = !crystal && (last.Contains("poin") || last.EndsWith("ints") || Dist(last, "points") <= 2);
            bool cut = !crystal && !points && Dist(last, "soul") <= 1;
            if (!crystal && !points && !cut) yield break;
            int n = ob.Groups["n"].Success ? int.Parse(ob.Groups["n"].Value) : 1;
            // the pet's name is what comes before "Soul"; a one-word "SouUPoints" or a cut-off "Papis Soul" keeps the word before it
            var head = words.Take(words.Count - 1).ToList();
            // "Obtained 2 Stigma Shard points." is another currency, not a soul: no "Soul" before "points", no kill
            bool soulish = cut || crystal || (head.Count > 0 && Dist(Letters(head[^1]), "soul") <= 2) || last.Contains("soul") || last.StartsWith("sou");
            if (points && !soulish) { yield return new CurrencyGained(time, string.Join(' ', words), Math.Clamp(n, 1, 999999)); yield break; }
            if (head.Count > 0 && !cut && Dist(Letters(head[^1]), "soul") <= 2) head.RemoveAt(head.Count - 1);
            // a Soul Points line names the enemy's kind ("Sparkie", "Unstable Spider"), not a pet: only a crystal's name is a pet's
            var pet = crystal || KnownPets.Any(k => string.Join(' ', head).Contains(k, StringComparison.OrdinalIgnoreCase)) ? PetName(string.Join(' ', head)) : "";
            if (crystal) yield return new PetCrystal(time, (pet.Length > 0 ? pet + " " : "") + "Soul Crystal", Math.Clamp(n, 1, 9999));
            else yield return new CurrencyGained(time, (pet.Length > 0 ? pet + " " : "") + "Soul Points", Math.Clamp(n, 1, 999999), cut ? "" : string.Join(' ', head));
            yield break;
        }

        var it = ItemNameFirst().Match(line);
        bool nameFirst = it.Success;
        if (!it.Success) it = Item().Match(line);
        if (it.Success)
        {
            // the recognizer leaves stray marks on line ends ("Fera Soul Crystals;"); none of them belong to a name
            var name = it.Groups["name"].Value.Trim().TrimEnd('.', ',', ';', ':', '\'', '"', '`', '-', ' ').Trim('[', ']', ' ');
            if (name.Length < 2) yield break;
            // "Acquired 3 Iron Ore" puts the count first; keep the name clean and the count right
            // ...and a big one with its thousands mark ("Acquired 1,000 Abyss Point(s)."): the count, not part of the name
            var lead = System.Text.RegularExpressions.Regex.Match(name, @"^(\d{1,3}(?:,\d{3})+|\d{1,6})[\s\-]+(.+)$");
            int lead_n = 0;
            if (lead.Success) { lead_n = (int)Math.Min(int.MaxValue, long.Parse(lead.Groups[1].Value.Replace(",", ""))); name = lead.Groups[2].Value; }
            name = System.Text.RegularExpressions.Regex.Replace(name, @"\(s\)$", "s");   // "Abyss Point(s)" is "Abyss Points"
            bool bound = name.Contains("(bound)", StringComparison.OrdinalIgnoreCase);
            // bound and tradeable copies count as the same drop; the flag is kept, the name isn't split
            if (bound) name = System.Text.RegularExpressions.Regex.Replace(name, @"\s*\(bound\)\s*", " ", RegexOptions.IgnoreCase).Trim();
            // an item that only comes bound, read without its "(Bound)": the tail wrapped onto the next line or was cut off, so
            // the line is bound all the same, a reward where a bound line is one (0.8.60)
            else if (_items.BoundOnly(name)) { bound = true; CrashLog.NoteBurst($"\"{name}\" only comes bound: its \"(Bound)\" was lost from the line"); }
            int n = it.Groups["n"].Success ? int.Parse(it.Groups["n"].Value) : lead_n > 0 ? lead_n : 1;
            // the count lost or garbled (2026-10-24 13:07: "Acquired Odyle" for x7; "Refining StonevxT"): the line's own tail
            // read through the digit slips first; a verb-first line with no count left is unsure (a name-first line is one)
            bool countUnsure = false;
            if (!it.Groups["n"].Success && lead_n == 0)
            {
                if (!_items.IsExact(name) && QuantityTail(name) is { } qt && KnowsQuiet(qt.Head))
                {
                    CountsFromTail++;
                    CrashLog.Note($"\"{name}\": its count read through the recognizer's digit slips as x{qt.Count}");
                    name = qt.Head; n = qt.Count;
                }
                else countUnsure = !nameFirst;
            }
            // the loot list's row for the same item, read in the last few seconds: its count, when the chat lost its own
            int Filled(long id)
            {
                if (!countUnsure || ListCount?.Invoke(id, time) is not int ln || ln < 1) return n;
                countUnsure = false; CountsFromList++;
                Session.Accuracy.Add("qty_from_list");
                CrashLog.Note($"{_items.NameOf(id)}: the chat line lost its count; the loot list's row says x{ln}");
                return ln;
            }
            // the chat names a skin "Dawn Frost (Cloak) Skin"; the item list as "Dawn Frost (Skin: Cloak)"
            var skin = System.Text.RegularExpressions.Regex.Match(name, @"^(?<set>.+?)\s*\((?<slot>[^()]+)\)\s*Skin$", RegexOptions.IgnoreCase);
            // ...but that line is the wardrobe unlocking an appearance (five and eight at a time, with Enhance Stones for the
            // duplicates), not loot: not a pickup unless the site turns skins back on (Steven, 2026-10-23 16:09)
            if (skin.Success && !Update.RemoteConfig.Current.SkinsCount) yield break;
            if (skin.Success) name = $"{skin.Groups["set"].Value.Trim()} (Skin: {skin.Groups["slot"].Value.Trim()})";
            // "Acquired .107 Kina" with a mark the Kina rule didn't expect: Kina is never an item, and a garbled amount isn't counted
            if (System.Text.RegularExpressions.Regex.IsMatch(name, @"\bk[i1l]nah?\s*$", RegexOptions.IgnoreCase)) yield break;
            // "Obtained 1 Natura Soul Crystals.": a pet crystal, kept apart from drops and rated by zone, not by kill.
            // The recognizer garbles these often ("Varian SouI Crystals", "Papis Soul poin!s"), so the match is loose:
            // anything with "soul" followed by "cryst" or "poin" is one of the two, never a drop.
            var pet = System.Text.RegularExpressions.Regex.Match(name, @"^(?<pet>.+?)[\s\-]*sou[l1iu]{1,2}[\s\-]*crys", RegexOptions.IgnoreCase);
            if (pet.Success)
            {
                yield return new PetCrystal(time, pet.Groups["pet"].Value.Trim() + " Soul Crystal", Math.Clamp(n, 1, 9999));
                yield break;
            }
            // "Obtained 1 Oculazen Soul Points." is a per-kill payout, not loot; "Papis Soul poin!s" and a cut-off "Papis Soul" are the same line
            var pts = System.Text.RegularExpressions.Regex.Match(name, @"^(?<pet>.+?)[\s\-]*sou[l1iu]{1,2}(?:[\s\-]*po[i1l]n[^a-z]*t?[^a-z]*s?[^a-z]*)?$", RegexOptions.IgnoreCase);
            if (pts.Success || System.Text.RegularExpressions.Regex.IsMatch(name, @"\b(?:points?|souls?)$", RegexOptions.IgnoreCase))
            {
                // inside a run a currency other than an enemy's Soul Points (Abyss Points, a token) is one of the run's rewards
                if (!pts.Success && InRun?.Invoke() == true)
                {
                    yield return new ItemAcquired(time, _items.IdOf(name), Math.Clamp(n, 1, 999999), bound) { Via = _items.Knows(name) ? "" : "unlisted" };
                    yield break;
                }
                yield return new CurrencyGained(time, pts.Success ? pts.Groups["pet"].Value.Trim() + " Soul Points" : name, Math.Clamp(n, 1, 999999), pts.Success ? pts.Groups["pet"].Value.Trim() : "");
                yield break;
            }
            // common pickups are name-first ("Odyle acquired."), rarer ones verb-first ("Acquired Fine Odyle x2.");
            // quest, title and box rewards are the verb-first lines marked (Bound), and only those are skipped
            // ...except inside a run (an event, an expedition, a Transcendence): there every reward is the run's, bound or not,
            // sellable or not (Steven, 2026-10-23: "even if they are not rewards that can sell or gear they should display")
            if (!nameFirst && bound && InRun?.Invoke() == true)
            {
                long bid = _items.IdOf(name);
                if (_items.Knows(name)) n = Filled(bid);
                NoteCount(rawLine, countUnsure);
                yield return new ItemAcquired(time, bid, Math.Clamp(n, 1, 99999), true) { Via = _items.Knows(name) ? "" : "unlisted" };
                yield break;
            }
            if (!nameFirst && bound)
            {
                if (KnowsQuiet(name)) n = Filled(_items.IdOf(name));
                NoteCount(rawLine, countUnsure);
                yield return new RewardReceived(time, name, Math.Clamp(n, 1, 9999));
                yield break;
            }
            // the colour the game printed the name in, when the reader could measure it: it says the item's grade
            var accent = AccentOf(rawLine);
            // the item list is the whitelist: a name it doesn't hold, exactly or as a close misread, is never a drop
            if (!_items.Knows(name))
            {
                // part of the name hidden or garbled: its closest item, when only one fits (the run's items and the
                // current enemy's table first; items of a grade the name's colour rules out never). Counted, and tagged
                // as guessed so it shows in the window
                var (guess, fromLikely) = _items.GuessWhy(name, Likely?.Invoke(), GradeFilter(accent));
                if (guess is long gid)
                {
                    // a guess no loot table backs while fighting enemies whose tables are known: left for the loot list
                    // beside the character to confirm (it adds the pickup itself if it showed it), never counted from a guess
                    Session.Accuracy.Name(name, gid, "guessed");
                    // a Legend, Unique or Epic item, or boss gear, is never held back (Steven, 2026-10-23: rare drops always
                    // count): it goes to the session's second read instead (RareGate), which confirms or corrects it
                    if (!fromLikely && Rare?.Invoke(gid) != true && Unlisted(gid) is { } fought)
                    {
                        HeldUnlisted++;
                        Session.Accuracy.Add("held");
                        CrashLog.Note($"guessed \"{name}\" as {_items.NameOf(gid)}, but {string.Join(", ", fought)} doesn't drop it: not counted from the chat, left for the loot list");
                        yield break;
                    }
                    CrashLog.Note($"guessed \"{name}\" as {_items.NameOf(gid)} (closest match{(fromLikely ? ", on this run or the enemy's loot table" : "")})");
                    // a guessed name isn't settled: its count isn't taken from the loot list, but a lost one is still watched for
                    NoteCount(rawLine, countUnsure);
                    yield return new ItemAcquired(time, gid, Math.Clamp(n, 1, 9999), bound) { Via = "guessed" };
                    yield break;
                }
                // inside a run a reward the item list lacks (an event's own token, a voucher) is still the run's, by its name
                if (InRun?.Invoke() == true && System.Text.RegularExpressions.Regex.IsMatch(name, @"^[A-Za-z][A-Za-z0-9'’:\-\s\(\)]{2,60}$"))
                {
                    CrashLog.Note($"not in the item list, counted for the run by its name: {name}");
                    NoteCount(rawLine, countUnsure);
                    yield return new ItemAcquired(time, _items.IdOf(name), Math.Clamp(n, 1, 99999), bound) { Via = "unlisted" };
                    yield break;
                }
                CrashLog.NoteBurst($"not in the item list, not counted: {name}");   // 46 of one name in 36 minutes (2026-10-24): once a minute, then counted
                Session.Accuracy.Add("unknown");
                // reported only when it reads like a garbled item name (some item is near it), never other text
                if (_items.Near(name, Math.Max(2, name.Length / 3)).Count > 0 || _items.Fragment(name, 3).Count > 0) Session.Accuracy.Name(name, null, "unknown");
                yield break;
            }
            // a misread (never an exact name): settled by what could drop or be made here first (brief item 1, 2026-10-24)
            if (!_items.IsExact(name) && Context?.Invoke() is { Empty: false } ctx)
            {
                var m = _items.Match(name, ctx);
                // closest to an item no table nearby lists, and more than a slip or two off: unsure, never counted. A rare
                // item is never refused (Steven's rule): it goes to its second read like any rare pickup
                if (m.Source == Data.NameMatch.Refused && m.Id is long rid && Rare?.Invoke(rid) != true)
                {
                    Refused++;
                    Session.Accuracy.Add("refused");
                    Session.Accuracy.Name(name, null, "unknown", Data.NameMatch.Refused);
                    CrashLog.Note($"\"{name}\" is nearest {_items.NameOf(rid)} ({m.Cost / (double)Data.NameMatch.Unit:0.#} edits), which no loot table, gathering or recipe nearby has: unsure, not counted");
                    yield break;
                }
                if (m.Id is long cid && Data.NameMatch.IsContext(m.Source) && (GradeFilter(accent)?.Invoke(cid) ?? true))
                {
                    // once per line, not per parse: the same line comes in again on later passes
                    Session.Accuracy.Snapped(name, cid, m.Source);
                    if (m.FullId is long fid && fid != cid)
                    {
                        ContextPicks++;
                        CrashLog.Note($"\"{name}\" is {_items.NameOf(cid)}, not {_items.NameOf(fid)}: {m.Source} has it");
                    }
                    n = Filled(cid);
                    NoteCount(rawLine, countUnsure);
                    yield return new ItemAcquired(time, cid, Math.Clamp(n, 1, 9999), bound);
                    yield break;
                }
            }
            long chosen = Choose(name, accent);
            n = Filled(chosen);
            NoteCount(rawLine, countUnsure);
            yield return new ItemAcquired(time, chosen, Math.Clamp(n, 1, 9999), bound);
        }
    }

    /// <summary>Digits only, with the recognizer's usual letter-for-digit slips corrected. Thousands separators and spaces drop out. Shared with the Market parser.</summary>
    internal static long Digits(string s)
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


    /// <summary>"Obtained 2 Natura Soul Crystals." in any state of repair: the verb, an optional count, then the rest.</summary>
    [GeneratedRegex(@"^\W*obtain[a-z]*[\W_]*(?:(?<n>\d{1,4})[\s\-]+)?(?<name>.+?)[\s.:;!?]*$", RegexOptions.IgnoreCase)]
    private static partial Regex ObtainedLine();

    static string Letters(string w) { var sb = new StringBuilder(); foreach (var c in w) if (char.IsLetter(c)) sb.Append(char.ToLowerInvariant(c)); return sb.ToString(); }

    /// <summary>Levenshtein distance, for words a few letters long.</summary>
    static int Dist(string a, string b)
    {
        var d = new int[a.Length + 1, b.Length + 1];
        for (int i = 0; i <= a.Length; i++) d[i, 0] = i;
        for (int j = 0; j <= b.Length; j++) d[0, j] = j;
        for (int i = 1; i <= a.Length; i++)
            for (int j = 1; j <= b.Length; j++)
                d[i, j] = Math.Min(Math.Min(d[i - 1, j] + 1, d[i, j - 1] + 1), d[i - 1, j - 1] + (a[i - 1] == b[j - 1] ? 0 : 1));
        return d[a.Length, b.Length];
    }

    /// <summary>
    /// The pets whose Soul Crystals and Soul Points the game prints, as seen in real reads. This is the whitelist for pet
    /// names: a read that isn't one of these, exactly, by containing one ("IfVarian"), or by one letter off, counts as a plain
    /// Soul Crystal or Soul Points line with no pet name, and is logged, so a misread never becomes a new name on the site.
    /// Add a pet here when the game shows a new one.
    /// </summary>
    public static readonly string[] KnownPets = { "Natura", "Varian", "Fera", "Ivera", "Papis", "Oculazen" };

    static string PetName(string raw)
    {
        var name = Regex.Replace(raw, @"\s+", " ").Trim();
        if (name.Length == 0) return "";
        foreach (var k in KnownPets)
        {
            if (string.Equals(k, name, StringComparison.OrdinalIgnoreCase)) return k;
            if (name.Contains(k, StringComparison.OrdinalIgnoreCase)) return k;
            if (k.Length >= 5 && Dist(k.ToLowerInvariant(), Letters(name)) <= 1) return k;
        }
        CrashLog.Note($"not a pet on the list, counted without a name: {name}");
        return "";
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
    // the last read's loot lines that this read lost at its bottom (keys), expected back in the next read
    List<string> _lost = new();
    /// <summary>Loot lines that came back after a read lost them at its bottom, taken for the lines they were (not new).</summary>
    public int LostBack { get; private set; }

    /// <summary>
    /// A loot line the last read lost at its bottom (a read that cut the chat short) is back now right where it was: the
    /// same line, not a new pickup (2026-10-24 13:12:17 and 13:15:03: 189 and 344 Kina counted twice). New lines that
    /// begin with the lost ones, in order, lose them.
    /// </summary>
    List<string> Back(List<string> fresh)
    {
        int back = 0;
        while (back < fresh.Count && back < _lost.Count && Similar(Key(fresh[back]), _lost[back])) back++;
        _lost = new();
        if (back == 0) return fresh;
        LostBack += back;
        return fresh.Skip(back).ToList();
    }
    /// <summary>Reads that saw nothing: the chat faded out, or the UI was hidden. The last lines seen stay as the baseline.</summary>
    public int Empty { get; private set; }

    bool _faded;

    /// <summary>
    /// True when the last lines returned were counted by the after-a-fade rule rather than by lining up with the
    /// previous read. Written to reads.log beside each line, so a line counted twice shows which rule counted it.
    /// </summary>
    public bool LastAfterFade { get; private set; }

    /// <summary>
    /// True when the last lines returned were found by lining up with the last full read of the chat, because the read
    /// just before was a stray line or two (a game window's label over the chat, "Show acquired designs").
    /// </summary>
    public bool LastFromAnchor { get; private set; }

    // The last read of three or more lines that was lined up (or the first): the fallback when a read can't be lined up
    // with the one just before it. Lines returned since that read (after a fade, from a short read) are kept in _since,
    // so lining up on the older read never counts them twice.
    List<string> _anchor = new();
    readonly List<string> _since = new();

    /// <summary>After a stretch the chat couldn't be seen, the next read is history again, like the first one: lines that arrived unseen aren't counted.</summary>
    public void Rebase() { _prev = new(); _anchor = new(); _since.Clear(); _faded = false; }

    // only a read that holds a line the chat prints (a pickup, a player's line) can be the anchor: a game window over the
    // chat read twice the same ("Can craft | Orichalcum Mace", "MAX | 3 | MAX | 3") became it, and the next real read lined
    // up with nothing (2026-10-25: 484 reads unaligned)
    List<string> _cur = new();
    bool Anchor(List<string> keys)
    {
        if (keys.Count < 3 || !_cur.Any(l => l.TrimStart().StartsWith('[') || LootKey(Key(l)))) return false;
        _anchor = keys; _since.Clear();
        return true;
    }

    /// <summary>Reads that were a game window over the chat, not the chat (<see cref="Panel"/>): skipped, never the baseline.</summary>
    public int Panels { get; private set; }
    /// <summary>True when the last read was such a panel (the session doesn't keep it as the last read either).</summary>
    public bool LastPanel { get; private set; }

    /// <summary>
    /// A game window over the chat: three lines or more, none the chat would print, numbers among them ("MAX | 3 | MAX | 3",
    /// a storage window's "16 | 21 | 89 | 70 | 105 | 10", "U Lv. 15 | U Lv. 20 | Lv. 25"; 69 reads on 2026-10-25, and 35 more
    /// that couldn't line up after one).
    /// </summary>
    static bool Panel(List<string> cur) => cur.Count >= 3 && !cur.Any(ChatLike) && cur.Any(l => Key(l).Any(char.IsDigit));

    List<string> Returned(List<string> keys, List<string> fresh)
    {
        if (!Anchor(keys)) _since.AddRange(fresh.Select(Key));
        return fresh;
    }

    public List<string> NewLines(List<string> cur)
    {
        LastAfterFade = false; LastFromAnchor = false; LastPanel = false;
        _cur = cur;
        if (Panel(cur)) { Panels++; LastPanel = true; return new List<string>(); }
        // the frameless chat fades out when idle; when a loot line brings it back, the read before it is this empty one.
        // A read of a line or two with nothing chat-like in it (a stray "99" from the world behind the chat) counts as
        // empty too: as the new baseline it left the next real read nothing to line up with, and that read's lines were lost.
        if (cur.Count == 0 || (cur.Count <= 2 && !cur.Any(ChatLike)))
        {
            Empty++;
            // the chat is faded (or hidden) now, whatever came before: right after a start or a pause too, so the loot
            // line that brings it back is counted and not taken for history
            _faded = true;
            return new List<string>();
        }
        var keys = cur.Select(Key).ToList();
        if (_faded)
        {
            // after a fade, one or two lines are what arrived since: count them even when they repeat the last ones seen
            // ("Orichalcum Ore acquired." twice in a row). More at once means the history came back (a hover), so align as usual.
            _faded = false;
            if (keys.Count <= 2) { _prev = keys; LastAfterFade = true; return Returned(keys, new List<string>(cur)); }
        }
        if (_prev.Count == 0)
        {
            _prev = keys;                     // first read: what's already on screen is history, not loot
            Anchor(keys);
            return new List<string>();
        }
        if (keys.Count == _prev.Count && Enumerable.Range(0, keys.Count).All(i => Similar(keys[i], _prev[i]))) { Anchor(keys); return new List<string>(); }

        // The chat grew and the last read is exactly its top: nothing scrolled off, so the lines below are new, even when
        // they repeat the line above them ("Raw Leather acquired." twice in a row). Lining up on the last line alone took
        // the second copy for the first and lost it (Steven, 2026-10-21 16:25).
        if (keys.Count > _prev.Count && Enumerable.Range(0, _prev.Count).All(i => Similar(keys[i], _prev[i])))
        {
            var grown = Back(cur.GetRange(_prev.Count, keys.Count - _prev.Count));
            _lost = new();
            _prev = keys;
            return Returned(keys, grown);
        }

        if (Align(_prev, keys, cur, out var lost) is { } fresh)
        {
            fresh = Back(fresh);
            _lost = lost;
            _prev = keys; return Returned(keys, fresh);
        }

        // Couldn't line up with the read just before. When that read was a stray line or two (a game window's label over
        // the chat, like the crafting window's "Show acquired designs"), the last full read of the chat still lines up:
        // what follows it is new, less the lines already counted since. Without this, the read holding
        // "Successfully crafted Orichalcum Ingot." and its item line was dropped (2026-10-21 09:38).
        if (_anchor.Count >= 3 && !ReferenceEquals(_anchor, _prev) && Align(_anchor, keys, cur) is { } again)
        {
            var seen = new List<string>(_since);
            var kept = new List<string>();
            foreach (var l in again)
            {
                int at = seen.FindIndex(s => Similar(s, Key(l)));
                if (at >= 0) seen.RemoveAt(at); else kept.Add(l);
            }
            LastFromAnchor = true;
            _prev = keys;
            return Returned(keys, kept);
        }
        Unaligned++;
        _prev = keys;
        return new List<string>();
    }

    /// <summary>
    /// The lines of <paramref name="cur"/> that come after <paramref name="prev"/>'s last lines, or null when the two
    /// can't be lined up. Lines up on the previous read's last line; when that line is gone, on the one before it, and so
    /// on. Behind the frameless chat the box also catches text from the world (a nameplate, a sign) that comes and goes as
    /// the camera moves; anchoring only on the last line lost every loot line that arrived while such text vanished.
    /// </summary>
    static List<string>? Align(List<string> prev, List<string> keys, List<string> cur) => Align(prev, keys, cur, out _);

    static List<string>? Align(List<string> prev, List<string> keys, List<string> cur, out List<string> lost)
    {
        lost = new List<string>();
        for (int j = prev.Count - 1; j >= Math.Max(0, prev.Count - 4); j--)
        {
            for (int k = Math.Min(3, j + 1); k >= 1; k--)
            {
                var tail = prev.GetRange(j - k + 1, k);
                for (int i = keys.Count - k; i >= 0; i--)        // last occurrence: fewest new lines, never a double count
                {
                    if (!Enumerable.Range(0, k).All(x => Similar(keys[i + x], tail[x]))) continue;
                    // the anchor is a run of one line repeated ("Riverside Pauldrons acquired." twice): lined up on its last
                    // copy, a new copy below the old ones would be taken for an old one. The run is as long now as it was
                    // unless new copies came: the copies past the last read's run are new (the same screen place, the same line)
                    if (j == prev.Count - 1 && tail.All(x => Similar(x, prev[j])))
                    {
                        // copies in the last read's bottom lines as many as the run now holds, a stray line between them or not
                        // (world text read between two "Kukuru acquired." is no new copy)
                        int runCur = 0; for (int x = i + k - 1; x >= 0 && Similar(keys[x], prev[j]); x--) runCur++;
                        int runPrev = 0; for (int x = j; x >= 0 && x >= j - runCur; x--) if (Similar(prev[x], prev[j])) runPrev++;
                        if (runPrev >= k && runCur > runPrev) i = Math.Max(0, i - (runCur - runPrev));
                    }
                    var fresh = cur.GetRange(i + k, keys.Count - i - k);
                    // The last read's lines after the anchor are still on screen in this one, in the same order, even when
                    // either read garbled them ("mria acquired."): skip one line here for each. Only world text (short,
                    // nothing chat-like) can really be gone, so it holds nothing back. Comparing the text instead re-counted
                    // every garbled line as new.
                    // ...except a loot line standing where the last read had a chat line that is gone now: the chat line
                    // was garbled out of this read, and the loot line is new (Steven, 2026-10-23 10:45: 355 Kina skipped)
                    if (j < prev.Count - 1)
                    {
                        int skip = 0, used = 0;
                        var after = prev.Skip(j + 1).Where(Persists).ToList();
                        foreach (var pl in after)
                        {
                            if (skip >= fresh.Count) break;
                            used++;
                            var fk = Key(fresh[skip]);
                            if (!Similar(fk, pl) && LootKey(fk) && !LootKey(pl)) continue;
                            skip++;
                        }
                        fresh = fresh.Skip(skip).ToList();
                        // the last read's loot lines below the anchor this read doesn't show: lost at its bottom, due back
                        lost = after.Skip(used).Where(LootKey).ToList();
                    }
                    return fresh;
                }
            }
        }
        return null;
    }

    /// <summary>
    /// The same line read twice, allowing for the recognizer: two reads of one line differ by a letter or two
    /// ("obtainedipapissoulpoints" / "obtainedfpapissoulpoints"). Exact for short keys; a letter per eight for long ones.
    /// Without this, a busy chat whose lines garble differently on every read never lined up, and all its loot was lost.
    /// </summary>
    static bool Similar(string a, string b)
    {
        if (a == b) return true;
        if (a.Length < 8 || b.Length < 8) return false;
        // a different number is a different line: "acquired285kina" isn't "acquired286kina", and the quest's "(19/25)"
        // isn't "(20/25)". Matching them took the newest Kina line or kill for one already seen (Steven, 2026-10-23: 25
        // kills counted as 20, 20 Hard Kalgolem as 12). Only a digit lost to a letter (a different count of digits) is
        // left to the letter allowance below.
        var da = DigitsOf(a); var db = DigitsOf(b);
        if (da.Length == db.Length && da != db) return false;
        int max = Math.Max(1, Math.Min(a.Length, b.Length) / 8);
        if (Math.Abs(a.Length - b.Length) > max) return false;
        return Lev(a, b, max) <= max;
    }

    static string DigitsOf(string s) => new(s.Where(char.IsDigit).ToArray());

    static int Lev(string a, string b, int max)
    {
        var prev = new int[b.Length + 1]; var cur = new int[b.Length + 1];
        for (int j = 0; j <= b.Length; j++) prev[j] = j;
        for (int i = 1; i <= a.Length; i++)
        {
            cur[0] = i; int rowMin = i;
            for (int j = 1; j <= b.Length; j++)
            {
                cur[j] = Math.Min(Math.Min(cur[j - 1] + 1, prev[j] + 1), prev[j - 1] + (a[i - 1] == b[j - 1] ? 0 : 1));
                rowMin = Math.Min(rowMin, cur[j]);
            }
            if (rowMin > max) return max + 1;
            (prev, cur) = (cur, prev);
        }
        return prev[b.Length];
    }

    /// <summary>A chat line, as opposed to text from the world behind the frameless chat ("Targena", "Cairn Tc"): long, or with a word the chat's own lines use.</summary>
    static bool LootKey(string key) => key.Contains("acqui") || key.Contains("obtain") || key.EndsWith("kina");

    static bool Persists(string key) => key.Length >= 10 || key.Contains("acqui") || key.Contains("obtain") || key.Contains("kina") || key.Contains("fail");

    /// <summary>The same test on a line as read: a player's line ("[Theo] next") is chat however short it is.</summary>
    static bool ChatLike(string line) => line.TrimStart().StartsWith('[') || Persists(Key(line));

    static string Key(string s)
    {
        var sb = new StringBuilder();
        foreach (var c in s.ToLowerInvariant()) if (char.IsLetterOrDigit(c)) sb.Append(c);
        return sb.ToString();
    }
}
