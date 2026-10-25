using KinahLedger.Data;
using KinahLedger.Ocr;

namespace KinahLedger.Session;

/// <summary>
/// The reward picture's second witnesses (2026-10-25): the player's faction for gear twins on one icon, settled as the
/// picture is filed, and the chat's lines in the seconds around it, judged a few seconds later (Session/TableWitness.cs)
/// and written onto the run (InstanceLog.AmendTable).
/// </summary>
public sealed partial class TrackerSession
{
    /// <summary>The player's faction from the server label ("Siel (Elyos)"), or null when no server is set.</summary>
    internal string? Faction => GearFactions.OfServer(Server);

    readonly object _witnessLock = new();
    TableWitness.Pending? _witness;

    /// <summary>The chat's item lines in the seconds before a picture is filed (as the reward table's own 20 s).</summary>
    List<TableWitness.Line> WitnessChatBefore(DateTime now) =>
        _runPickups.Where(p => p.Name != "Kina" && (now - p.At).TotalSeconds is >= 0 and <= TableWitness.BeforeSeconds)
                   .Select(p => new TableWitness.Line(p.At, p.Name, p.Count)).ToList();

    /// <summary>
    /// The gear twin a slot is by the player's faction: the one name of <paramref name="names"/> the faction list gives the
    /// player's faction, every other name the other's. Null without a server, or when the list doesn't know them all.
    /// </summary>
    string? FactionTwin(IReadOnlyList<string> names) => GearFactions.Pick(names, Faction);

    /// <summary>What the run holds a candidate name of a piece of gear as: its tradeable copy first, bound when every copy is.</summary>
    static Dictionary<string, (long Id, bool Bound)> TwinIds(IEnumerable<ItemInfo> graded) =>
        graded.GroupBy(i => Unbound(i.Name), StringComparer.OrdinalIgnoreCase)
              .ToDictionary(g => g.Key, g => (g.OrderBy(i => i.Name.Contains("(Bound)", StringComparison.OrdinalIgnoreCase)).First().Id,
                                              g.All(i => i.Name.Contains("(Bound)", StringComparison.OrdinalIgnoreCase))), StringComparer.OrdinalIgnoreCase);

    /// <summary>A picture was filed into the open run: its slots wait for the chat's lines (judged by <see cref="TableWitnessDue"/>).</summary>
    void WitnessFiled(DateTime now, string what, List<TableWitness.Slot> slots, List<TableWitness.Line> before)
    {
        // one still waiting (a second picture within seconds) is judged on what it has first
        TableWitnessDue(now, force: true);
        if (slots.Count == 0 || Instances.CurrentRun is not { } run) return;
        lock (_witnessLock)
        {
            var p = new TableWitness.Pending { At = now, RunStart = run.Start, What = what, Slots = slots };
            p.Chat.AddRange(before);
            _witness = p;
        }
    }

    /// <summary>A pickup counted from the chat: one more line for the picture waiting for it, when one is.</summary>
    void WitnessChat(GameEvent ev)
    {
        if (ev.Via.Contains("reward table")) return;
        var line = ev switch
        {
            ItemAcquired a => new TableWitness.Line(ev.Time, Unbound(_items.NameOf(a.ItemId)), a.Count),
            RewardReceived r => new TableWitness.Line(ev.Time, Unbound(r.Name), r.Count),
            _ => null,
        };
        if (line == null) return;
        lock (_witnessLock)
            if (_witness is { Judged: false } p && (line.At - p.At).TotalSeconds >= -TableWitness.BeforeSeconds && line.At <= TableWitness.Due(p.At, WitnessScreenSeen()))
                p.Chat.Add(line);
    }

    /// <summary>The reward screen's last sight: the event cards' screen or the reward table, whichever was later.</summary>
    DateTime WitnessScreenSeen() => _eventScreenAt > _gridSeenAt ? _eventScreenAt : _gridSeenAt;

    /// <summary>
    /// Judges the waiting picture once its chat is in (<see cref="TableWitness.Due"/>), or now with <paramref name="force"/>
    /// (a recovery read of saved pictures, with the lines kept on the run; the replay bank). Every pass calls it.
    /// </summary>
    internal void TableWitnessDue(DateTime now, bool force = false, IEnumerable<TableWitness.Line>? kept = null)
    {
        TableWitness.Pending? p;
        lock (_witnessLock)
        {
            p = _witness;
            if (p == null || p.Judged || (!force && now < TableWitness.Due(p.At, WitnessScreenSeen()))) return;
            if (kept != null) foreach (var l in kept) if (!p.Chat.Contains(l)) p.Chat.Add(l);
            p.Judged = true; _witness = null;
        }
        try
        {
            var got = TableWitness.Judge(p.Slots, p.Chat);
            foreach (var d in got)
                CrashLog.Note(d.Kind switch
                {
                    TableWitness.Settle => $"{p.What}: a {d.Slot.Grade} slot, one of {string.Join(" / ", d.Slot.Twins)}, is {d.Name}: {d.Why}",
                    TableWitness.Unsettle => $"{p.What}: a {d.Slot.Grade} slot counted as {d.Name} by the server ({Server}) is one piece of unknown gear again: {d.Why}; is the server setting this character's?",
                    _ => $"{p.What}: a {d.Slot.Grade} slot named {d.Name} is not read: {d.Why}",
                });
            Instances.AmendTable(p.RunStart, p.What, got, p.Chat);
        }
        catch (Exception ex) { CrashLog.Write("reward picture's chat", ex); }
    }
}
