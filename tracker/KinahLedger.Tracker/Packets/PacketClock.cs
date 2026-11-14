namespace KinahLedger.Packets;

/// <summary>
/// "Now" for the packet reading's decisions (how fresh the reading is, how long a change has waited, how late a count
/// landed): the wall clock, except in packet-test's replays, which step a recording through its own times so a count's
/// delay is measured as it would have been live.
/// </summary>
public static class PacketClock
{
    internal static Func<DateTime>? Fixed;
    public static DateTime Now => Fixed?.Invoke() ?? DateTime.UtcNow;
}
