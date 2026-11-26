using KinahLedger.Ocr;

namespace KinahLedger.Session;

public sealed partial class TrackerSession
{
    const string PayoutReward = "a box's or a reward's contents";
    const double PayoutSettleSeconds = PayoutWindowSeconds + ChatVoter.MaxSeconds;

    sealed record PayoutPickup(ItemAcquired Item, GameEvent Source, string Raw, ChatVoter.Out? Meta, IReadOnlyList<string> Read);

    sealed class PendingPayout(DateTime at, DateTime until, KinahChanged? kinah)
    {
        public DateTime At { get; } = at;
        public DateTime Until { get; } = until;
        public KinahChanged? Kinah { get; } = kinah;
        public bool Kill { get; set; }
        public List<PayoutPickup> Pickups { get; } = new();
    }

    readonly List<PendingPayout> _payouts = new();
    DateTime _payoutKillAt = DateTime.MinValue;

    PendingPayout? PayoutAt(DateTime at) =>
        _payouts.FindLast(p => at >= p.At && at <= p.Until);

    void PayoutKill(DateTime at)
    {
        if (at <= _payoutKillAt) return;
        _payoutKillAt = at;
        var payout = _payouts.FindLast(p => (at - p.At).TotalSeconds is >= 0 and <= PayoutSettleSeconds);
        if (payout != null) payout.Kill = true;
    }

    // A later chat vote or a packet can supply the kill first. Settle before the repeat guard's usual pass.
    void ReleasePayouts(DateTime now, bool final = false)
    {
        if (_payouts.Count == 0) return;
        PayoutKill(_lastKillAt);
        for (int i = 0; i < _payouts.Count;)
        {
            var payout = _payouts[i];
            if (!payout.Kill && !final && now < payout.Until.AddSeconds(ChatVoter.MaxSeconds)) { i++; continue; }
            _payouts.RemoveAt(i);
            if (!payout.Kill && payout.Until > _boxUntil) _boxUntil = payout.Until;
            if (payout.Kinah is { } kinah) Commit(kinah with { KillPay = payout.Kill }, inferKillPay: false);
            foreach (var pickup in payout.Pickups) SettlePayoutPickup(pickup, payout.Kill, now);
        }
    }

    void SettlePayoutPickup(PayoutPickup pickup, bool kill, DateTime now)
    {
        var (item, source, raw, meta, read) = pickup;
        DateTime lineAt = meta?.At ?? item.Time;
        if (!kill)
        {
            var reward = _parser.AsReward(item);
            CrashLog.Note($"{_items.NameOf(item.ItemId)} x{item.Count}: {PayoutReward}, not a drop");
            if (AsideLine(item, raw, PayoutReward, meta, read, lineAt, now)) { _counts.Drop(source); return; }
            _counts.Release(source, reward);
            Commit(reward);
            return;
        }

        string name = _items.NameOf(item.ItemId);
        bool unsure = _parser.CountUnsure(raw);
        var place = meta is { } m ? new ItemRepeatGuard.Place(m.FromBottom, m.Read.Count, m.Above1, m.Above2) : null;
        var screen = ScreenOf(ItemLineKey(name, item.Count), raw, meta?.Read ?? read, lineAt, unsure);
        var verdict = RepeatCheck(item, item, source, raw, lineAt, place, screen, needsWitness: false, unsure, now);
        if (verdict == ItemRepeatGuard.Verdict.Repeat) _counts.Drop(source);
        if (verdict != ItemRepeatGuard.Verdict.Count) return;
        _counts.Release(source);
        CrashLog.Note($"{name} x{item.Count}: a kill confirmed the bound Kinah, counted as a drop");
        FinishChatItem(item, raw, needsWitness: false, now);
    }
}
