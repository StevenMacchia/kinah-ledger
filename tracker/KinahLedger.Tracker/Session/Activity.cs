namespace KinahLedger.Session;

/// <summary>What the player is doing, as one state (Steven, 2026-10-23: "what event is happening, what event tree it's part of, what the user is doing").</summary>
public enum ActivityKind { NotRead, Idle, Town, Farming, Event, Expedition, Arena, Legion, WorldBoss, Crafting, Market, RewardScreen }

/// <summary>
/// One state of play: the kind, where (the zone, the expedition, the boss, the reward screen's title), and for an event
/// the event itself and its family.
/// </summary>
public sealed record Activity(ActivityKind Kind, string Where = "", string Event = "", string Family = "", DateTime Since = default, bool Leaving = false)
{
    /// <summary>"Event · Hidden Lugi (Shugo Festival)", "Farming · Eastern Cantas Valley", "Expedition · Urugugu Canyon", "Crafting".</summary>
    public string Label => Kind switch
    {
        ActivityKind.Event => "Event · " + (Event.Length > 0 && !string.Equals(Event, Where, StringComparison.OrdinalIgnoreCase) ? $"{Event} ({Where})" : Where) + (Leaving ? " · rewards landing" : ""),
        ActivityKind.Expedition or ActivityKind.Arena or ActivityKind.Legion => $"{Kind} · {Where}" + (Leaving ? " · rewards landing" : ""),
        ActivityKind.WorldBoss => $"World boss · {Where}",
        ActivityKind.Farming => $"Farming · {Where}",
        ActivityKind.Town => $"Town · {Where}",
        ActivityKind.Idle => Where.Length > 0 ? $"Idle · {Where}" : "Idle",
        ActivityKind.Crafting => "Crafting",
        ActivityKind.Market => "Market",
        ActivityKind.RewardScreen => $"Reward screen · {Where}",
        _ => "Not read yet",
    };

    /// <summary>
    /// Whether a pickup's place (its <see cref="Label"/>) is a run's reward: an event, an expedition, an arena, a legion area,
    /// a world boss or a reward screen, never the open world (the rare-drop chime plays only out there, 0.8.62).
    /// </summary>
    public static bool IsRunLabel(string label) =>
        System.Text.RegularExpressions.Regex.IsMatch(label, @"^(?:Event|Expedition|Arena|Legion|World boss|Reward screen)b", System.Text.RegularExpressions.RegexOptions.IgnoreCase);

    /// <summary>The same state, apart from when it began.</summary>
    public bool SameAs(Activity o) => Kind == o.Kind && Where == o.Where && Event == o.Event && Family == o.Family && Leaving == o.Leaving;
}

/// <summary>
/// The activity over time: the current one, and the changes of the last hour, so a pickup can be stamped with the
/// activity it landed in and a miscount report can show what the tracker thought was going on. Every change is written to
/// reads.log ("activity: Farming · Eastern Cantas Valley → Event · Hidden Lugi (Shugo Festival) (minimap)").
/// </summary>
public sealed class ActivityTracker
{
    readonly object _lock = new();
    readonly List<Activity> _history = new();
    public Activity Current { get; private set; } = new(ActivityKind.NotRead);

    /// <summary>Sets the activity; a change is logged with <paramref name="why"/>. True when it changed.</summary>
    public bool Set(Activity next, DateTime now, string why = "")
    {
        lock (_lock)
        {
            if (next.SameAs(Current)) return false;
            next = next with { Since = now };
            CrashLog.Note($"activity: {Current.Label} → {next.Label}" + (why.Length > 0 ? $" ({why})" : ""));
            Current = next;
            _history.Add(next);
            _history.RemoveAll(a => (now - a.Since).TotalMinutes > 60 && a != next);
            return true;
        }
    }

    /// <summary>The activity at <paramref name="t"/>: the last change at or before it (the current one when none is that old).</summary>
    public Activity At(DateTime t)
    {
        lock (_lock) return _history.LastOrDefault(a => a.Since <= t) ?? Current;
    }

    /// <summary>The changes since <paramref name="t"/>, oldest first, with the one under way at <paramref name="t"/> in front.</summary>
    public List<Activity> Since(DateTime t)
    {
        lock (_lock)
        {
            var list = _history.Where(a => a.Since > t).ToList();
            var before = _history.LastOrDefault(a => a.Since <= t);
            if (before != null) list.Insert(0, before);
            return list;
        }
    }
}
