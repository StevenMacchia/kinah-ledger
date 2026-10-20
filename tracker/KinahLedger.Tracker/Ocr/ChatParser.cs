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

    static int Count(Match m) => m.Groups["n"].Success && int.TryParse(m.Groups["n"].Value, out var n) ? Math.Clamp(n, 1, 99999) : 1;

    static string CleanName(string s)
    {
        var name = Regex.Replace(s, @"\s+", " ").Trim().TrimEnd('.', ',', ';', ':', '\'', '"', '`', '-', ' ').Trim('[', ']', ' ');
        name = Regex.Replace(name, @"^(?:your|the|an?)\s+", "", RegexOptions.IgnoreCase);
        return name.Length > 64 ? name[..64] : name;
    }

    public IEnumerable<GameEvent> Parse(string rawLine, DateTime time)
    {
        var line = Timestamp().Replace(rawLine.Trim(), "");
        if (line.Length == 0) yield break;
        if (line.StartsWith('[') || Title().IsMatch(line)) yield break;   // player chat, or a title being claimed
        if (line.StartsWith("Increased ", StringComparison.OrdinalIgnoreCase)) yield break;   // "Increased Pet Insight Level ...": progress, not loot

        var trade = ParseTrade(line, time).ToList();
        if (trade.Count > 0) { foreach (var ev in trade) yield return ev; yield break; }

        var gf = GatherFail().Match(line);
        if (gf.Success) { yield return new GatherFailed(time, gf.Groups["skill"].Value.Trim()); yield break; }

        var k = Kina().Match(line);
        if (k.Success)
        {
            long amt = Digits(k.Groups["amt"].Value);
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
            if (head.Count > 0 && !cut && Dist(Letters(head[^1]), "soul") <= 2) head.RemoveAt(head.Count - 1);
            var pet = PetName(string.Join(' ', head));
            if (crystal) yield return new PetCrystal(time, (pet.Length > 0 ? pet + " " : "") + "Soul Crystal", Math.Clamp(n, 1, 9999));
            else yield return new CurrencyGained(time, (pet.Length > 0 ? pet + " " : "") + "Soul Points", Math.Clamp(n, 1, 999999));
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
            var lead = System.Text.RegularExpressions.Regex.Match(name, @"^(\d{1,4})[\s\-]+(.+)$");
            int lead_n = 0;
            if (lead.Success) { lead_n = int.Parse(lead.Groups[1].Value); name = lead.Groups[2].Value; }
            bool bound = name.Contains("(bound)", StringComparison.OrdinalIgnoreCase);
            // bound and tradeable copies count as the same drop; the flag is kept, the name isn't split
            if (bound) name = System.Text.RegularExpressions.Regex.Replace(name, @"\s*\(bound\)\s*", " ", RegexOptions.IgnoreCase).Trim();
            int n = it.Groups["n"].Success ? int.Parse(it.Groups["n"].Value) : lead_n > 0 ? lead_n : 1;
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
                yield return new CurrencyGained(time, pts.Success ? pts.Groups["pet"].Value.Trim() + " Soul Points" : name, Math.Clamp(n, 1, 999999));
                yield break;
            }
            // common pickups are name-first ("Odyle acquired."), rarer ones verb-first ("Acquired Fine Odyle x2.");
            // quest, title and box rewards are the verb-first lines marked (Bound), and only those are skipped
            if (!nameFirst && bound)
            {
                yield return new RewardReceived(time, name, Math.Clamp(n, 1, 9999));
                yield break;
            }
            // the item list is the whitelist: a name it doesn't hold, exactly or as a close misread, is never a drop
            if (!_items.Knows(name)) { CrashLog.Note($"not in the item list, not counted: {name}"); yield break; }
            yield return new ItemAcquired(time, _items.IdOf(name), Math.Clamp(n, 1, 9999), bound);
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
    /// <summary>Reads that saw nothing: the chat faded out, or the UI was hidden. The last lines seen stay as the baseline.</summary>
    public int Empty { get; private set; }

    bool _faded;

    /// <summary>
    /// True when the last lines returned were counted by the after-a-fade rule rather than by lining up with the
    /// previous read. Written to reads.log beside each line, so a line counted twice shows which rule counted it.
    /// </summary>
    public bool LastAfterFade { get; private set; }

    /// <summary>After a stretch the chat couldn't be seen, the next read is history again, like the first one: lines that arrived unseen aren't counted.</summary>
    public void Rebase() { _prev = new(); _faded = false; }

    public List<string> NewLines(List<string> cur)
    {
        LastAfterFade = false;
        // the frameless chat fades out when idle; when a loot line brings it back, the read before it is this empty one.
        // A read of a line or two with nothing chat-like in it (a stray "99" from the world behind the chat) counts as
        // empty too: as the new baseline it left the next real read nothing to line up with, and that read's lines were lost.
        if (cur.Count == 0 || (cur.Count <= 2 && !cur.Any(ChatLike)))
        {
            Empty++;
            _faded = _prev.Count > 0;
            return new List<string>();
        }
        var keys = cur.Select(Key).ToList();
        if (_prev.Count == 0)
        {
            _prev = keys;                     // first read: what's already on screen is history, not loot
            return new List<string>();
        }
        if (_faded)
        {
            // after a fade, one or two lines are what arrived since: count them even when they repeat the last ones seen
            // ("Orichalcum Ore acquired." twice in a row). More at once means the history came back (a hover), so align as usual.
            _faded = false;
            if (keys.Count <= 2) { _prev = keys; LastAfterFade = true; return new List<string>(cur); }
        }
        if (keys.SequenceEqual(_prev)) return new List<string>();

        // Line up on the previous read's last line; when that line is gone, on the one before it, and so on. Behind the
        // frameless chat the box also catches text from the world (a nameplate, a sign) that comes and goes as the camera
        // moves; anchoring only on the last line lost every loot line that arrived while such text vanished.
        for (int j = _prev.Count - 1; j >= Math.Max(0, _prev.Count - 4); j--)
        {
            for (int k = Math.Min(3, j + 1); k >= 1; k--)
            {
                var tail = _prev.GetRange(j - k + 1, k);
                for (int i = keys.Count - k; i >= 0; i--)        // last occurrence: fewest new lines, never a double count
                {
                    if (!keys.GetRange(i, k).SequenceEqual(tail)) continue;
                    var fresh = cur.GetRange(i + k, keys.Count - i - k);
                    // The last read's lines after the anchor are still on screen in this one, in the same order, even when
                    // either read garbled them ("mria acquired."): skip one line here for each. Only world text (short,
                    // nothing chat-like) can really be gone, so it holds nothing back. Comparing the text instead re-counted
                    // every garbled line as new.
                    if (j < _prev.Count - 1) fresh = fresh.Skip(_prev.Skip(j + 1).Count(Persists)).ToList();
                    _prev = keys;
                    return fresh;
                }
            }
        }
        Unaligned++;
        _prev = keys;
        return new List<string>();
    }

    /// <summary>A chat line, as opposed to text from the world behind the frameless chat ("Targena", "Cairn Tc"): long, or with a word the chat's own lines use.</summary>
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
