namespace KinahLedger.Session;

/// <summary>
/// Drops the player flagged with "Something looks wrong" (Steven, 2026-10-21). A flagged item still shows in the window,
/// marked, but nothing about it leaves this PC: not in the run's items, a zone's items, an enemy's drops or an
/// expedition run. Kept by item name for as long as the app runs; a second click takes the flag off.
/// </summary>
public static class ReportedDrops
{
    static readonly HashSet<string> _names = new(StringComparer.OrdinalIgnoreCase);
    static readonly object _lock = new();

    public static bool Has(string name) { lock (_lock) return _names.Contains(name.Trim()); }

    /// <summary>Flags the item, or takes the flag off; returns whether it is flagged now.</summary>
    public static bool Toggle(string name)
    {
        lock (_lock)
        {
            var n = name.Trim();
            if (_names.Remove(n)) return false;
            _names.Add(n);
            return true;
        }
    }

    public static int Count { get { lock (_lock) return _names.Count; } }
}
