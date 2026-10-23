namespace KinahLedger.Session;

/// <summary>
/// Another character in the world (Steven, 2026-10-23: "detect when we log off the game or character and reset the
/// tracker"). Read from the screen only: going to the character screen hides the whole interface, so the Kinah counter
/// at the top-left and the zone name above the minimap are both gone; a character comes back with a balance of its own.
/// So a switch is: both gone for <see cref="AbsentSeconds"/> or more, then the counter back with a different balance that
/// two reads agree on. A loading screen (both gone, the same balance after), a game window over the counter (the zone
/// still readable) or the same character logging back in (the same balance) is not one. Pure logic, for the self-tests.
/// </summary>
public sealed class RelogDetector
{
    public const double AbsentSeconds = 20, ZoneFreshSeconds = 25;   // the zone is read every 10 s (Ocr.ZoneEverySec): two missed reads

    long? _before;
    DateTime? _absentSince;

    /// <summary>The balance last trusted in the world, before any absence.</summary>
    public long? Before => _before;

    /// <summary>
    /// One look at the Kinah counter. <paramref name="counterSeen"/>: the counter shows on the strip; <paramref name="balance"/>
    /// and <paramref name="balanceAt"/>: the last trusted balance and when it was trusted (BalanceTracker);
    /// <paramref name="zoneSeenAt"/>: the last time the minimap's zone name was read. Returns why it is another
    /// character, or null.
    /// </summary>
    public string? Observe(DateTime t, bool counterSeen, long? balance, DateTime? balanceAt, DateTime zoneSeenAt)
    {
        bool zoneGone = (t - zoneSeenAt).TotalSeconds > ZoneFreshSeconds;
        bool trustedNow = balance != null && balanceAt is DateTime a && Math.Abs((t - a).TotalSeconds) <= 2;
        if (!counterSeen)
        {
            // the whole interface gone (the character screen, a loading screen): an absence starts; the counter alone
            // gone while the zone still reads is a window over it, not the character screen
            if (zoneGone) _absentSince ??= t;
            return null;
        }
        if (_absentSince is DateTime gone)
        {
            if (!trustedNow) return null;   // back, but not read twice the same yet
            long now = balance!.Value;
            double away = (t - gone).TotalSeconds;
            _absentSince = null;
            var was = _before;
            _before = now;
            if (away >= AbsentSeconds && was is long w && w != now)
                return $"the Kinah counter came back at {now:N0} after the game's interface was gone {away:0} s (it was {w:N0}): another character";
            return null;
        }
        if (trustedNow) _before = balance;
        return null;
    }
}
