namespace KinahLedger.Session;

/// <summary>
/// How often the crafting panel's locator looks (ocr.craft_probe_slow, 2026-10-26, branch tracker/pass-cost). Every pass
/// by default. With the switch on, about once a second (<see cref="SlowSeconds"/>) while no panel has been seen for
/// <see cref="HoldSeconds"/> and the caller has no reason to watch closely (the scene is Crafting or not settled, the
/// crafting window or a gathering sign was seen lately); every pass again from the first pass with a reason or a panel.
/// The hold is time, not passes: a pass skipped is never a look that saw nothing, so "panel gone" and any stickiness after
/// it run on looks at the full rate, as before. Each switch between the two paces is logged once.
/// </summary>
public sealed class ProbeCadence
{
    /// <summary>While slow, a look when this long has passed since the last one.</summary>
    public const double SlowSeconds = 1.0;
    /// <summary>Every pass for this long after a look saw the panel (crafting or gathering).</summary>
    public const double HoldSeconds = 10;

    DateTime _lastLook = DateTime.MinValue, _seenAt = DateTime.MinValue;
    bool _slow;

    /// <summary>Passes asked about and looks taken since the start (the audit line).</summary>
    public int Passes { get; private set; }
    public int Looks { get; private set; }
    /// <summary>Whether the last pass asked about was at the slow pace.</summary>
    public bool Slow => _slow;

    /// <summary>
    /// Whether to look this pass. <paramref name="slowOn"/>: the switch; <paramref name="fullBecause"/>: the caller's reason
    /// to look every pass, null when it has none. <paramref name="note"/>: a line for reads.log when the pace switched.
    /// </summary>
    public bool Due(DateTime now, bool slowOn, string? fullBecause, out string? note)
    {
        Passes++;
        note = null;
        double sinceSeen = (now - _seenAt).TotalSeconds;
        string? why = !slowOn ? "ocr.craft_probe_slow is off"
            : fullBecause ?? (_seenAt != DateTime.MinValue && sinceSeen >= 0 && sinceSeen < HoldSeconds ? $"a panel was seen {sinceSeen:0.#} s ago" : null);
        bool slow = why == null;
        if (slow != _slow)
        {
            _slow = slow;
            note = slow
                ? $"crafting panel: looked for about once a second (no crafting or gathering panel for {HoldSeconds:0} s, the scene isn't Crafting, no crafting window or gathering sign)"
                : $"crafting panel: looked for every pass again ({why})";
        }
        bool look = !slow || _lastLook == DateTime.MinValue || now < _lastLook || (now - _lastLook).TotalSeconds >= SlowSeconds;
        if (look) { _lastLook = now; Looks++; }
        return look;
    }

    /// <summary>A look saw a panel (crafting or gathering): every pass for <see cref="HoldSeconds"/> from now.</summary>
    public void Seen(DateTime now) => _seenAt = now;
}
