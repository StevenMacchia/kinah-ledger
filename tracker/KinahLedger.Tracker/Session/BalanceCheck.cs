namespace KinahLedger.Session;

/// <summary>
/// The Kinah counter at the top of the game against the Kinah the tracker counted (Steven, 2026-10-22: "check Kinah
/// against the balance"). Between two moments, the counter should rise by exactly what was picked up, less what was
/// spent. A gap that holds for <see cref="SettleSeconds"/> (the chat can lag the counter by a read or two) is a verdict:
/// the counter rose by more than was counted (a missed pickup, or a sale or mail the chat didn't show), or by less (Kinah
/// spent, or a pickup counted twice). The check itself never decides which; the session only acts on a rise that a
/// second source confirms. Pure arithmetic, no screen: tested in parse-test.
/// </summary>
public sealed class BalanceCheck
{
    public const double SettleSeconds = 6;
    /// <summary>Gaps beyond this either way are a purchase, a sale or a mail claim, not a pickup: reported, never acted on.</summary>
    public const long MaxPickupGap = 20_000;

    long? _balance0, _counted0;
    long _gap; DateTime _gapSince;

    public int Agreed { get; private set; }
    /// <summary>Gaps that held and were judged (the counter disagreed with the count).</summary>
    public int Judged { get; private set; }
    /// <summary>
    /// Judged gaps that look like a missed pickup: the counter rose by a pickup-sized amount the count lacks. Spending
    /// (teleports, repairs, purchases) lowers the counter and large rises are sales or mail, so neither counts here.
    /// </summary>
    public int PickupMisses { get; private set; }
    public long Over { get; private set; }
    public long Under { get; private set; }

    /// <summary>
    /// A new reading of the counter with the Kinah counted so far (pickups, bound included). Returns the gap once it has
    /// held long enough to judge: positive when the counter rose by more than was counted. Zero otherwise.
    /// </summary>
    public long Observe(long balance, long counted, DateTime at)
    {
        if (_balance0 == null || _counted0 == null) { _balance0 = balance; _counted0 = counted; return 0; }
        long gap = (balance - _balance0.Value) - (counted - _counted0.Value);
        if (gap == 0) { if (_gap != 0 || balance != _balance0) Agreed++; Rebase(balance, counted); return 0; }
        if (gap != _gap) { _gap = gap; _gapSince = at; return 0; }
        if ((at - _gapSince).TotalSeconds < SettleSeconds) return 0;
        // the gap held: judged once, then the count starts again from here
        if (gap > 0) Over += gap; else Under += -gap;
        Judged++;
        if (gap > 0 && gap <= MaxPickupGap) PickupMisses++;
        Rebase(balance, counted);
        return gap;
    }

    void Rebase(long balance, long counted) { _balance0 = balance; _counted0 = counted; _gap = 0; }
}
