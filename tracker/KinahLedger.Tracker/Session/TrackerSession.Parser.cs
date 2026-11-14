using KinahLedger.Packets;
using KinahLedger.Parser;

namespace KinahLedger.Session;

/// <summary>
/// The boss-fight Parser (Parser/BossParser.cs) fed from the game's messages on the session's own pass, beside the Kinah
/// and the items (TrackerSession.Packets.cs). The fights are kept in the character's folder (Parser/EncounterStore.cs);
/// the fight under way is kept too, so a restart carries it on.
/// </summary>
public sealed partial class TrackerSession
{
    /// <summary>Where the fight events come from: the packet reader when its decoder reads them (or a test's list).</summary>
    public IFightFeed? FightFeed { get; set; }

    /// <summary>The boss fights: the one under way and where the player is.</summary>
    public BossParser Fights => _fights ??= NewFights();
    BossParser? _fights;

    /// <summary>The kept fights of the character playing.</summary>
    public static EncounterStore FightStore { get; } = new(() => Characters.DataDir);

    int _fightSaved = -1;
    DateTime _fightSavedAt = DateTime.MinValue;

    BossParser NewFights()
    {
        var p = new BossParser { Ended = e => { FightStore.Add(e); FightStore.SaveLive(null); } };
        if (FightStore.LoadLive() is { } live) p.Resume(live, DateTime.UtcNow);
        return p;
    }

    /// <summary>The fight events since the last pass, then the screen's place (when the game's messages haven't said), the stale check, and the fight under way kept.</summary>
    void DrainFights()
    {
        // a test's feed, else the packet reader's fights (the release build's decoder, interface version 4)
        Func<FightEvent?>? next = FightFeed != null ? FightFeed.NextFight : Packets != null ? Packets.NextFight : null;
        var p = Fights;
        try
        {
            if (next != null) for (int i = 0; i < 20_000 && next() is { } ev; i++) p.Take(ev);
            var cur = Instances.Current;
            p.ScreenPlace(cur?.Kind, cur?.Zone);
            var now = DateTime.UtcNow;
            p.Tick(now);
            // the fight under way, at most every 5 s while it changes
            int v = p.Version;
            if (v != _fightSaved && (now - _fightSavedAt).TotalSeconds >= 5)
            {
                _fightSaved = v; _fightSavedAt = now;
                if (p.Live is { } live) FightStore.SaveLive(live);
            }
        }
        catch (Exception ex) { CrashLog.Write("parser", ex); }
    }

    internal void DrainFightsForTest() => DrainFights();
}
