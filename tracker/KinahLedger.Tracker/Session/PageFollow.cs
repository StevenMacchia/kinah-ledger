namespace KinahLedger.Session;

/// <summary>
/// Which page the window shows, following what the player is doing (Steven, 2026-10-28: "the app should load on to the
/// farming screen and switch between farming / crafting / event based on what it detected automatically (turn this OFF in
/// settings)"). It adds no detection: it reads the session's <see cref="Activity"/> once a window tick.
/// <list type="bullet">
/// <item>The window opens on Farming, whatever page it closed on, so that is where this starts.</item>
/// <item>Farming shows Farming; the crafting window or panel shows Crafting; an expedition, event, arena, Legion run or
/// world boss shows Events. A base, the Market, a reward screen and idle belong to no page: they never switch it.</item>
/// <item>The page follows only when the activity has moved and held there for <see cref="HoldSeconds"/>, so a read or two
/// of something else (a blip) never moves it.</item>
/// <item>A page the player picks by hand stays until the activity moves on from what it was at that moment.</item>
/// <item>With the setting off nothing switches, but this keeps up, so turning it back on doesn't jump to something old.</item>
/// </list>
/// </summary>
public sealed class PageFollow
{
    public const string Farming = "Farming", Crafting = "Crafting", Events = "Events";
    /// <summary>How long a new activity holds before the page follows it.</summary>
    public const double HoldSeconds = 5;

    // what the page was last settled against: a page, None for an activity with no page, Unknown when the player picked a
    // page before anything was read (the first activity read then is what was already going on, not a change)
    const string None = "", Unknown = "?";
    string _basis = Farming;
    // an activity that differs from the basis, and since when it has held
    string? _candidate;
    DateTime _since;
    ActivityKind _last = ActivityKind.NotRead;

    /// <summary>The page an activity belongs to, or null for one with no page of its own.</summary>
    public static string? PageOf(ActivityKind kind) => kind switch
    {
        ActivityKind.Farming => Farming,
        ActivityKind.Crafting => Crafting,
        ActivityKind.Event or ActivityKind.Expedition or ActivityKind.Arena or ActivityKind.Legion or ActivityKind.WorldBoss => Events,
        _ => null,
    };

    /// <summary>
    /// One look at the activity (every window tick). Returns the page to show when the activity has moved to another
    /// page's kind and held there, and <paramref name="on"/>; else null.
    /// </summary>
    public string? Step(ActivityKind kind, DateTime now, bool on)
    {
        _last = kind;
        // nothing read (no zone yet): not a change, and whatever was starting has to hold again once reads come back
        if (kind == ActivityKind.NotRead) { _candidate = null; return null; }
        var page = PageOf(kind) ?? None;
        if (page == _basis) { _candidate = null; return null; }
        if (page != _candidate) { _candidate = page; _since = now; return null; }
        if ((now - _since).TotalSeconds < HoldSeconds) return null;
        bool first = _basis == Unknown;
        _basis = page;
        _candidate = null;
        return on && !first && page != None ? page : null;
    }

    /// <summary>The player picked a page by hand: it stays until the activity moves on from the one under way now.</summary>
    public void Picked()
    {
        _basis = _last == ActivityKind.NotRead ? Unknown : PageOf(_last) ?? None;
        _candidate = null;
    }
}
