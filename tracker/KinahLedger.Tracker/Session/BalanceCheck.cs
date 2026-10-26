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
    long _bound0; DateTime _at0;
    long _gap; DateTime _gapSince;
    /// <summary>Where each decision goes (the session: reads.log). An agreement and a fall are one line each; a judged gap is the session's own line.</summary>
    public Action<string>? Log { get; set; }
    /// <summary>Readings where the counter had fallen since the last agreement: Kinah spent (the Market's cut, a repair, a teleport), never judged.</summary>
    public int Spent { get; private set; }

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
    public long Observe(long balance, long counted, DateTime at) => Observe(balance, counted, 0, at);

    /// <summary>
    /// The same, with <paramref name="counted"/> the Kina counted (and read, waiting for the counter) without the bound Kina,
    /// and <paramref name="bound"/> the bound Kina counted so far. The counter the tracker finds rose by neither the 60,000,
    /// the 70,000 nor the 16,000 bound Kina of 2026-10-25 (each became an "under" of exactly its amount), so the check
    /// leaves bound Kina out; a rise of the counted Kina plus the bound agrees too, for a counter that holds both. A fall
    /// since the last agreement is Kinah spent: no verdict, the check starts again from it (until 2026-10-26 a spend was
    /// judged "under").
    /// </summary>
    public long Observe(long balance, long counted, long bound, DateTime at)
    {
        if (_balance0 == null || _counted0 == null) { Rebase(balance, counted, bound, at); return 0; }
        long rise = balance - _balance0.Value, chat = counted - _counted0.Value, boundRise = bound - _bound0;
        if (rise < 0)
        {
            Spent++;
            Log?.Invoke($"Kinah counter check: the counter fell {-rise:N0} since {_at0.ToLocalTime():HH:mm:ss} while the chat counted {chat:N0} (Kinah spent: the Market's cut, a repair, a teleport): no verdict, the check starts again here");
            Rebase(balance, counted, bound, at);
            return 0;
        }
        long gap = rise - chat;
        bool withBound = gap != 0 && boundRise > 0 && gap == boundRise;
        if (gap == 0 || withBound)
        {
            if (_gap != 0 || rise != 0)
            {
                Agreed++;
                Log?.Invoke($"Kinah counter check: rose {rise:N0} since {_at0.ToLocalTime():HH:mm:ss}, the chat counted {chat:N0}{(withBound ? $" and {boundRise:N0} bound" : "")}: agrees");
            }
            Rebase(balance, counted, bound, at);
            return 0;
        }
        if (gap != _gap) { _gap = gap; _gapSince = at; return 0; }
        if ((at - _gapSince).TotalSeconds < SettleSeconds) return 0;
        // the gap held: judged once, then the count starts again from here
        if (gap > 0) Over += gap; else Under += -gap;
        Judged++;
        if (gap > 0 && gap <= MaxPickupGap) PickupMisses++;
        Rebase(balance, counted, bound, at);
        return gap;
    }

    void Rebase(long balance, long counted, long bound, DateTime at) { _balance0 = balance; _counted0 = counted; _bound0 = bound; _at0 = at; _gap = 0; }
}
