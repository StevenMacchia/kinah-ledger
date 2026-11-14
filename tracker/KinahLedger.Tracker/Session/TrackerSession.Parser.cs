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
    public IBossFeed? FightFeed { get; set; }

    /// <summary>The boss fights: the one under way and where the player is.</summary>
    public BossParser Fights => _fights ??= NewFights();
    BossParser? _fights;

    /// <summary>The kept fights of the character playing.</summary>
    public static EncounterStore FightStore { get; } = new(() => Characters.DataDir);

    int _fightSaved = -1;
    DateTime _fightSavedAt = DateTime.MinValue;

    BossParser NewFights()
    {
        var p = new BossParser();
        p.Ended = e => { FightStore.Add(e); FightStore.SaveLive(p.LiveAll); };
        p.SetMode(ParserMode, DateTime.UtcNow);
        var live = FightStore.LoadLive();
        if (live.Count > 0) p.Resume(live, DateTime.UtcNow);
        return p;
    }

    /// <summary>The fight events since the last pass, then the screen's place (when the game's messages haven't said), the stale check, and the fight under way kept.</summary>
    void DrainFights()
    {
        // a test's feed, else the packet reader's fights (the release build's decoder, interface version 4)
        Func<BossEvent?>? next = FightFeed != null ? FightFeed.NextBossEvent : Packets != null ? Packets.NextBossEvent : null;
        var p = Fights;
        try
        {
            if (next != null) for (int i = 0; i < 20_000 && next() is { } ev; i++) p.Take(ev);
            var cur = Instances.Current;
            p.ScreenPlace(cur?.Kind, cur?.Zone);
            p.Place(Zone, Ocr.ZoneLocator.IsAbyss(Zone));
            var now = DateTime.UtcNow;
            p.Tick(now);
            // the fight under way, at most every 5 s while it changes
            int v = p.Version;
            if (v != _fightSaved && (now - _fightSavedAt).TotalSeconds >= 5)
            {
                _fightSaved = v; _fightSavedAt = now;
                FightStore.SaveLive(p.LiveAll);
            }
        }
        catch (Exception ex) { CrashLog.Write("parser", ex); }
    }

    internal void DrainFightsForTest() => DrainFights();

    /// <summary>What the Parser keeps (the Parser page's mode, parser.json), set by the window; a new session starts in it.</summary>
    public static ParserMode ParserMode { get; set; } = ParserMode.Bosses;

    /// <summary>A kill or death of the player's from the game's kill events (TrackerSession.Packets.cs): to the Parser's run and fight.</summary>
    void FightToParser(FightTally.Outcome o) { try { Fights.Kill(o.At, o.Player, o.Death, o.Name); } catch (Exception ex) { CrashLog.Write("parser", ex); } }
}
