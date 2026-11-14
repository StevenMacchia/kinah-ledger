using KinahLedger.Packets;

namespace KinahLedger.Parser;

/// <summary>Where the Parser's fight events come from: the packet reader (PacketReader.NextFight), or a recording in a test.</summary>
public interface IFightFeed
{
    /// <summary>The next fight event, in order, or null when none is waiting.</summary>
    FightEvent? NextFight();
}

/// <summary>Fight events from a list (tests, the demo).</summary>
public sealed class ListFightFeed : IFightFeed
{
    readonly Queue<FightEvent> _q;
    public ListFightFeed(IEnumerable<FightEvent> events) => _q = new(events);
    public void Add(FightEvent e) => _q.Enqueue(e);
    public FightEvent? NextFight() => _q.TryDequeue(out var e) ? e : null;
}
