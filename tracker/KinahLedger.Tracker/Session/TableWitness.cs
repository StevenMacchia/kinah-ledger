namespace KinahLedger.Session;

/// <summary>
/// The chat beside a run's reward picture, judged once its lines are in (2026-10-25). A reward table or an event's cards
/// are filed from the picture; the game prints the same rewards in the chat, one line per item with its total ("Acquired
/// Odyle x9."), but an event's chat is hidden behind its reward screen and is read only once the screen closes (2026-10-24
/// 18:06: the cards filed at 18:06:51, their chat lines read at 18:06:54). So the picture is filed first and the chat is
/// judged here, a few seconds later, as the second witness:
/// <list type="bullet">
/// <item>a piece of gear whose icon two items share ("one of Forgotten Helm / Shade Helm") is the one the chat named in
/// those seconds, when it named exactly one of them;</item>
/// <item>a piece settled by the player's faction (Data/GearFactions.cs) the chat names as its twin goes back to unknown
/// gear: the two disagree, and the server setting may be another character's;</item>
/// <item>a slot whose count is more than <see cref="Ratio"/>x the chat's own line is not read (Hidden Lugi 18:02: Soul
/// Codex 43 on the cards, every other run 3, the chat "x3"): a picture that counts far more than the game printed is a
/// misread. The chat's figure is not taken in its place: the picture's count is contradicted, and the chat alone is one
/// witness;</item>
/// <item>a picture that counts less than the chat's own line by more than <see cref="Ratio"/>x keeps its count, and the
/// slot is one to look at (<see cref="Doubt"/>; 2026-10-25 13:07:04 Hidden Lugi: card 3's Soul Codex x1, the chat "x3",
/// the other two in places the cards only showed cut). The slot was read and what is missing may sit in a slot not read,
/// so it is never dropped; the run stays out of the averages until someone looks;</item>
/// <item>a chat line whose count no read showed ("Acquired Amplify Stone Fragment (Unique)" with its "(Bound) x9." lost,
/// counted as one assumed: <see cref="Line.Assumed"/>) is no witness of a count at all, for or against (2026-10-25
/// 20:07:03: that assumed 1 set the table's x9 not read).</item>
/// </list>
/// A chat line contradicts a count only when it was read together with lines that agree with the picture on two other
/// items, so it is the picture's own printout and not another pickup of the same item in those seconds (2026-10-24 15:31:59,
/// 6 s after the Urugugu table's Enhance Stone x4.0K, a lone "Acquired Enhance Stone x935." from something else).
/// </summary>
public static class TableWitness
{
    /// <summary>Chat lines this long before the picture was filed are its own (as the reward table's 20 s for the chat's pickups).</summary>
    public const double BeforeSeconds = 20;
    /// <summary>...and this long after the reward screen was last seen, when the chat behind it is read again.</summary>
    public const double AfterSeconds = 12;
    /// <summary>Never longer than this after the picture was filed, however long the screen stays up.</summary>
    public const double MaxSeconds = 90;
    /// <summary>Lines read within this of each other were one read of the chat: one printout.</summary>
    public const double BurstSeconds = 1.5;
    /// <summary>A count and the chat's line further apart than this are a disagreement.</summary>
    public const double Ratio = 2;

    /// <summary>
    /// One item line of the chat in the seconds around the picture: when it was counted, the item, how many. Assumed: the
    /// line showed no count, so one was assumed (ChatParser.CountUnsure); its count is no witness.
    /// </summary>
    public sealed record Line(DateTime At, string Name, int Count, bool Assumed = false);

    /// <summary>
    /// Adds a chat line to the lines kept for a picture. A count read again later for a line that showed none
    /// (<paramref name="settle"/>: the session's "count read again" delta) completes that line, which then witnesses its
    /// count; with no such line kept, the delta alone is no witness of a total, so it is kept as assumed.
    /// </summary>
    public static void AddLine(List<Line> chat, Line line, bool settle)
    {
        if (settle)
        {
            int i = chat.FindLastIndex(l => l.Assumed && Same(l.Name, line.Name));
            if (i >= 0) { chat[i] = chat[i] with { Count = chat[i].Count + line.Count, Assumed = false }; return; }
            line = line with { Assumed = true };
        }
        chat.Add(line);
    }

    /// <summary>
    /// A slot as the picture filed it: named (<paramref name="Name"/>, <paramref name="Count"/>), or a piece of unknown gear
    /// (<paramref name="Twins"/> its candidate names, <paramref name="Name"/> empty). A piece the faction settled is named,
    /// with its twins. <paramref name="ItemId"/> and <paramref name="Bound"/> are what the run holds it as; for unknown gear,
    /// the item each candidate name would be held as.
    /// </summary>
    public sealed record Slot(int Index, string Grade, string Name, int Count, long ItemId, bool Bound, bool Gear,
        IReadOnlyList<string> Twins, string Note, IReadOnlyDictionary<string, (long Id, bool Bound)>? TwinIds = null);

    /// <summary>What the chat decided for one slot: settled as an item, back to unknown gear, or not read.</summary>
    public sealed record Decision(Slot Slot, string Kind, string Name, long ItemId, bool Bound, string Why);
    public const string Settle = "settle", Unsettle = "unsettle", Unread = "not read", Doubt = "to look at";

    /// <summary>A picture filed into a run, waiting for its chat.</summary>
    public sealed class Pending
    {
        public DateTime At { get; init; }
        public DateTime RunStart { get; init; }
        public string What { get; init; } = "";
        public List<Slot> Slots { get; init; } = new();
        public List<Line> Chat { get; } = new();
        public bool Judged { get; set; }
    }

    /// <summary>When a pending picture is judged: <see cref="AfterSeconds"/> after the later of its filing and the reward screen's last sight, at most <see cref="MaxSeconds"/> after the filing.</summary>
    public static DateTime Due(DateTime filed, DateTime screenSeen) =>
        new DateTime(Math.Min((screenSeen > filed ? screenSeen : filed).AddSeconds(AfterSeconds).Ticks, filed.AddSeconds(MaxSeconds).Ticks), DateTimeKind.Utc);

    static bool Same(string a, string b) => string.Equals(a.Trim(), b.Trim(), StringComparison.OrdinalIgnoreCase);

    /// <summary>The chat's decisions for a picture's slots, from its lines. Pure: nothing is changed here.</summary>
    public static List<Decision> Judge(IReadOnlyList<Slot> slots, IReadOnlyList<Line> chat)
    {
        var res = new List<Decision>();
        int Said(string name) => chat.Where(l => Same(l.Name, name)).Sum(l => l.Count);

        // 1. unknown gear: the chat's name among its candidates. Pieces with the same candidates are one group: one name
        //    said settles as many pieces as it was said; several names said settle each as often as said, when that is no
        //    more than the pieces (a piece is a piece whichever slot it sat in); more than that and the chat can't tell
        foreach (var g in slots.Where(s => s.Gear && s.Name.Length == 0 && s.Twins.Count > 1)
                               .GroupBy(s => string.Join("/", s.Twins.OrderBy(n => n, StringComparer.OrdinalIgnoreCase)), StringComparer.OrdinalIgnoreCase))
        {
            var pieces = g.ToList();
            var said = pieces[0].Twins.Select(n => (Name: n, N: Said(n))).Where(x => x.N > 0).ToList();
            if (said.Count == 0 || (said.Count > 1 && said.Sum(x => x.N) > pieces.Count)) continue;
            int at = 0;
            foreach (var (name, n) in said)
                for (int i = 0; i < n && at < pieces.Count; i++, at++)
                {
                    var p = pieces[at];
                    var (id, bound) = p.TwinIds != null && p.TwinIds.TryGetValue(name, out var t) ? t : (0L, false);
                    if (id == 0) continue;
                    res.Add(new Decision(p, Settle, name, id, bound, $"the chat said \"{name}\" in those seconds"));
                }
        }

        // 2. a piece the faction settled that the chat names as its twin, and never as itself: back to unknown gear
        foreach (var g in slots.Where(s => s.Gear && s.Name.Length > 0 && s.Twins.Count > 1).GroupBy(s => s.Name, StringComparer.OrdinalIgnoreCase))
        {
            var first = g.First();
            if (Said(first.Name) > 0) continue;
            var other = first.Twins.FirstOrDefault(n => !Same(n, first.Name) && Said(n) > 0);
            if (other == null) continue;
            foreach (var p in g) res.Add(new Decision(p, Unsettle, p.Name, p.ItemId, p.Bound, $"the chat said \"{other}\", not \"{p.Name}\""));
        }

        // 3. counts: the chat's line for an item against the picture's slots of it. Only a line read with two others that
        //    agree with the picture (a named item's total, or one of a piece's candidates) is the picture's own printout
        var totals = slots.Where(s => s.Name.Length > 0 && s.Count > 0).GroupBy(s => s.Name, StringComparer.OrdinalIgnoreCase)
                          .ToDictionary(x => x.Key, x => x.Sum(s => s.Count), StringComparer.OrdinalIgnoreCase);
        var twins = slots.Where(s => s.Gear).SelectMany(s => s.Twins).ToHashSet(StringComparer.OrdinalIgnoreCase);
        bool Agrees(Line l) => !l.Assumed && ((totals.TryGetValue(l.Name, out var t) && t == l.Count) || (twins.Contains(l.Name) && l.Count == 1));
        bool Printout(Line l) =>
            chat.Where(o => !Same(o.Name, l.Name) && Math.Abs((o.At - l.At).TotalSeconds) <= BurstSeconds && Agrees(o))
                .Select(o => o.Name.ToLowerInvariant()).Distinct().Count() >= 2;
        foreach (var g in slots.Where(s => !s.Gear && s.Name.Length > 0 && s.Count > 0).GroupBy(s => s.Name, StringComparer.OrdinalIgnoreCase))
        {
            // a line whose count was assumed (none shown) witnesses nothing about a count
            var lines = chat.Where(l => Same(l.Name, g.Key) && !l.Assumed).ToList();
            var own = lines.Where(Printout).ToList();
            int c = own.Sum(l => l.Count);
            if (c <= 0) continue;
            var list = g.ToList();
            foreach (var s in list)
                if (s.Count > Ratio * c)
                    res.Add(new Decision(s, Unread, s.Name, s.ItemId, s.Bound, $"the slot read x{s.Count}, the chat's own line x{c} ({string.Join(", ", own.Select(l => $"{l.At.ToLocalTime():HH:mm:ss}"))})"));
            // the picture short of the printout: kept as read, one to look at (the rest may be in a slot not read)
            int total = list.Sum(s => s.Count);
            if (total * Ratio < c && !res.Any(d => d.Kind == Unread && Same(d.Name, g.Key)))
                res.Add(new Decision(list[0], Doubt, list[0].Name, list[0].ItemId, list[0].Bound, $"the picture read x{total}, the chat's own line x{c}"));
        }
        return res;
    }
}
