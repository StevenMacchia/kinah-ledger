using System.Text.RegularExpressions;
using KinahLedger.Update;

namespace KinahLedger.Session;

/// <summary>
/// The check every pickup passes before it counts as a drop (Steven, 2026-10-23: "you need some type of quality assurance
/// check"). What the chat prints is not always loot: the player's own inventory at work (storage, Quick Register, an
/// exchange, a purchase) prints "acquired" lines for what moved; the wardrobe prints "(Slot) Skin acquired" for every
/// appearance it unlocks, in bursts; a box opened prints its contents; and a reward pays out stacks no enemy drops.
/// Each rule here says why a line is not a drop, so the log can say it. Pure text checks, for the self-tests.
/// </summary>
public static class PickupCheck
{
    /// <summary>A line from another player ("[Name] ..."), or a line wrapped from one ("name] any boss run").</summary>
    public static bool PlayerChat(string line)
    {
        var t = line.TrimStart('\'', '"', ' ', '¯', '`');
        int close = t.IndexOf(']');
        return t.StartsWith('[') || (close >= 0 && close < 25);
    }

    /// <summary>
    /// Each line of a chat read that is another player's text: a "[Name] ..." line, and the lines its message wrapped onto
    /// below it, up to the next line the game itself prints (2026-10-27 17:00:18: "[Okay] i've got splendent true dragon
    /// and" / "artisan dagger sitting in my storage i just can't" / "afford the mats". The second line, without its name,
    /// read as a storage line, and a Raw Leather dropped 9 s later was set aside as the inventory at work). A game line
    /// starts with a capital and ends a sentence, or starts with one of the inventory's own verbs.
    /// </summary>
    public static bool[] PlayerText(IReadOnlyList<string> lines)
    {
        var p = new bool[lines.Count];
        for (int i = 0; i < lines.Count; i++)
            p[i] = PlayerChat(lines[i]) || (i > 0 && p[i - 1] && !GameSentence(lines[i]));
        return p;
    }

    static readonly Regex GameVerbRx = new(@"^\W{0,2}(?:stored|added|purchase|registered|retrieved|withdr[ae]w|dismantled|salvaged|exchanged|acquired|obtained)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>A line the game prints, as far as its shape says: a capital at the start and a sentence's end, or the inventory's own first word.</summary>
    static bool GameSentence(string line)
    {
        var t = line.Trim().TrimStart('\'', '"', '`', '•', '-', '—');
        if (t.Length == 0) return false;
        if (GameVerbRx.IsMatch(t)) return true;
        return char.IsUpper(t[0]) && Regex.IsMatch(t, @"[.!:]\W{0,2}$");
    }

    // storage ("Stored item(s) in Storage.", read "inStorage"), Quick Register ("QuickCRegister", "Quicb Registeritetns"),
    // taking out, dismantling, an exchange or a purchase. Not "extract": a failed gather prints "Essence Extraction failed".
    static readonly Regex InventoryRx = new(@"\bstor(?:ed|age)|storage|quick\s*\S{0,2}\s*regi|\bregist|retriev|withdr[ae]w|dismantl|salvag|exchang|purchas|wardrobe",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>The player's own inventory at work: what prints with it, and just after, moved, it didn't drop.</summary>
    public static bool IsInventoryLine(string line) =>
        !PlayerChat(line) && (InventoryRx.IsMatch(line) || RemoteConfig.HasAny(line, RemoteConfig.Current.InventoryWords));

    // "Dawn Frost (Gloves) Skin acquired.", read "Skimacquired", "Skih acquired", "(Breastplate)'Skin"
    static readonly Regex SkinUnlockRx = new(@"\([^()]{2,24}\)\s*['’]?\s*sk[il1][mnh]", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>
    /// The wardrobe unlocking an appearance ("Set (Slot) Skin acquired."): they come five and eight at a time when gear is
    /// registered or a skin box is opened, never one per kill. Not a drop unless the site turns skins back on.
    /// </summary>
    public static bool IsSkinUnlock(string line) => !PlayerChat(line) && SkinUnlockRx.IsMatch(line);

    // a box, chest or bundle obtained ("Obtained Mystic Light Weapon Skin Chest", read "Skin Chess"); a boss's loot chest
    // is the boss's loot, handled as a distribution
    static readonly Regex BoxLineRx = new(@"(?:acqu|obta|btain).*\b(?:ch[e3]s[st5]?|box|pouch|bundle|cube|coffer|crate|package|parcel)\b|\b(?:ch[e3]s[st5]?|box|pouch|bundle|cube|coffer|crate|package|parcel)\b.*acqu",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>A box obtained or opened: the lines printed with it are its contents.</summary>
    public static bool IsBoxLine(string line) =>
        !PlayerChat(line) && BoxLineRx.IsMatch(line) && !line.Contains("loot chest", StringComparison.OrdinalIgnoreCase);

    /// <summary>Kina is money, never an item: a loot list row named Kina by its icon, with its amount cut off, is not a drop.</summary>
    public static bool IsKina(string name) => Regex.IsMatch(name, @"^\s*k[i1l]nah?\s*(?:\([^()]{0,10}\))?\s*$", RegexOptions.IgnoreCase);   // "Kina (Bognd)" too

    /// <summary>A kill gives a Soul Crystal or two; a line of 200 is a quest's or a reward's (16:22), out in the world.</summary>
    public static string? TooManyCrystals(int count) =>
        count > RemoteConfig.Current.MaxOneCrystal ? $"{count} Soul Crystals at once is a reward, not a kill's (a kill gives one or two)" : null;

    /// <summary>
    /// More of one item in one line than any enemy or node gives (Enhance Stone x446, Oath Coin x680): a reward paying out,
    /// a claim or a box, out in the world. Null when it may be a drop.
    /// </summary>
    public static string? TooMany(int count) =>
        count > RemoteConfig.Current.MaxOnePickup ? $"x{count} is more than one pickup gives (over {RemoteConfig.Current.MaxOnePickup}), so a reward or a claim" : null;
}
