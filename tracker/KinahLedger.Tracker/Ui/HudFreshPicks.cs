using static KinahLedger.Ui.HudKit;

namespace KinahLedger.Ui;

/// <summary>
/// Tells the overlay's looks when a pickup was just made (0.10.55), the one thing their figures roll, their rings sweep and
/// their edges light for: the newest pickup is a new one, no older than the last, and from the last few seconds. Not the
/// first draw, not a price refresh revaluing the session (the total rises with no new pickup), not a session loaded (its
/// pickups minutes old), a session cleared or another character (the pickups gone), or an Undo (the newest taken back).
/// </summary>
sealed class FreshPicks
{
    /// <summary>A pickup older than this when it reaches the panel is history, not one just made.</summary>
    public static readonly TimeSpan Within = TimeSpan.FromSeconds(10);

    string _id = "";
    DateTime _at = DateTime.MinValue;
    bool _seen;

    /// <summary>
    /// Once per update: the pickup just made, or null. <paramref name="back"/>: the pickups shown went away or back in time (a
    /// session cleared, another character, an Undo), so what the look showed is history.
    /// </summary>
    public Pick? Next(IReadOnlyDictionary<string, string> values, out bool back)
    {
        var front = Picks(values).FirstOrDefault();
        back = _seen && (front == null ? _id.Length > 0 : front.At < _at);
        bool fresh = _seen && !back && front != null && front.Id != _id && DateTime.UtcNow - front.At < Within;
        _seen = true;
        _id = front?.Id ?? "";
        if (front != null) _at = front.At;
        return fresh ? front : null;
    }
}
