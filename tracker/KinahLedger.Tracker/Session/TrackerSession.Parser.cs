using System.Collections.Concurrent;
using KinahLedger.Packets;
using KinahLedger.Parser;

namespace KinahLedger.Session;

/// <summary>
/// The Parser (Parser/BossParser.cs). Steven, 2026-11-14: "the parser needs to be SUPER low delay, it can not be laggy, it
/// needs to register in real time". So its events never wait for the session's screen pass: the packet reader hands each
/// one to the Parser as it is decoded, on the reader's own thread (PacketReader.BossSink, .FightSink). The Parser says so
/// at once (BossParser.Changed) and the window redraws. Kills the game names as the player's, and the player's deaths, go
/// straight from the kill events; only a kill with no killer named waits for the kill tally to credit it (its Rank Points
/// rise). The pass does what can wait: the screen's place, the stale check, keeping the records under way, and saving.
/// Records are kept in the character's folder (Parser/EncounterStore.cs).
/// </summary>
public sealed partial class TrackerSession
{
    /// <summary>Where the fight events come from in a test (a list); live, the packet reader hands them over itself.</summary>
    public IBossFeed? FightFeed { get; set; }

    /// <summary>The Parser: the record under way and where the player is.</summary>
    public BossParser Fights => _fights ??= NewFights();
    BossParser? _fights;

    /// <summary>The kept records of the character playing.</summary>
    public static EncounterStore FightStore { get; } = new(() => Characters.DataDir);

    int _fightSaved = -1;
    DateTime _fightSavedAt = DateTime.MinValue;
    /// <summary>Ended records waiting to be written: never on the packet reader's thread (a file write there would hold the next hit).</summary>
    readonly ConcurrentQueue<Encounter> _toSave = new();
    readonly object _saveGate = new();
    [ThreadStatic] static bool _onReaderThread;
    bool _parserWired;

    BossParser NewFights()
    {
        var p = new BossParser();
        p.Ended = e =>
        {
            _toSave.Enqueue(e);
            if (_onReaderThread) Task.Run(SaveEnded); else SaveEnded();
        };
        p.SetMode(ParserMode, DateTime.UtcNow);
        var live = FightStore.LoadLive();
        if (live.Count > 0) p.Resume(live, DateTime.UtcNow);
        return p;
    }

    void SaveEnded()
    {
        lock (_saveGate)
        {
            bool any = false;
            while (_toSave.TryDequeue(out var e)) { FightStore.Add(e); any = true; }
            if (any) FightStore.SaveLive(Fights.LiveAll);
        }
    }

    /// <summary>The packet reader hands the Parser its events as they are decoded (once per session; a new session takes them over).</summary>
    void WireParser()
    {
        if (_parserWired || Packets is not { } r) return;
        _parserWired = true;
        var p = Fights;
        // what queued before the session was up first, in order
        while (r.NextBossEvent() is { } old) p.Take(old);
        r.BossSink = ev => { _onReaderThread = true; try { p.Take(ev); } finally { _onReaderThread = false; } };
        r.FightSink = f => { _onReaderThread = true; try { RawFight(p, f); } finally { _onReaderThread = false; } };
        while (r.NextBossEvent() is { } late) p.Take(late);
    }

    /// <summary>
    /// A kill event as it comes: the player's own kill (the game named them its killer) or the player's death counts for the
    /// run and the fight at once. A kill that named no killer waits for the kill tally (<see cref="FightToParser"/>).
    /// </summary>
    internal static void RawFight(BossParser p, FightEvent f)
    {
        if (f.Kind != FightKind.Kill) return;
        if (f.Flags.HasFlag(FightFlags.VictimMe)) p.Kill(f.At, player: f.KillerName.Length > 0, death: true, f.KillerName);
        else if (f.Flags.HasFlag(FightFlags.Mine)) p.Kill(f.At, player: f.Flags.HasFlag(FightFlags.VictimPlayer), death: false, f.VictimName);
    }

    /// <summary>The pass's part: a test's events, the screen's place, the stale check, the records under way kept, the ended ones saved.</summary>
    void DrainFights()
    {
        var p = Fights;
        try
        {
            WireParser();
            if (FightFeed != null) for (int i = 0; i < 20_000 && FightFeed.NextBossEvent() is { } ev; i++) p.Take(ev);
            else if (!_parserWired && Packets != null) for (int i = 0; i < 20_000 && Packets.NextBossEvent() is { } ev; i++) p.Take(ev);
            var cur = Instances.Current;
            p.ScreenPlace(cur?.Kind, cur?.Zone);
            p.Place(Zone, Ocr.ZoneLocator.IsAbyss(Zone));
            var now = DateTime.UtcNow;
            p.Tick(now);
            SaveEnded();
            // the records under way, at most every 5 s while they change
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

    // ---- the party list in the top-left corner (Ocr/PartyFrame.cs): each member's class for the Parser ----
    DateTime _partyAt = DateTime.MinValue, _partyNamesAt = DateTime.MinValue;
    bool _partySeen;
    int _partyGroupCount;
    string _partySig = "";
    List<string> _partyNames = new();
    /// <summary>Read again this often while a party list was seen last time, and this often to look for one otherwise.</summary>
    const double PartyEverySeconds = 5, PartyLookSeconds = 20, PartyNamesSeconds = 30;

    /// <summary>
    /// The party list, now and then (never in the Parser's path): once when the group changes, every few seconds while a
    /// party list shows, every twenty to look for one. The names are read only when the rows change (and at most every half
    /// minute); the classes come from the icons each time. The overlays step aside while it is read.
    /// </summary>
    async Task PartyFrameDue()
    {
        if (GameRect is not { } g || g.Height < 200) return;
        var now = DateTime.UtcNow;
        int count = Fights.Group.Count;
        bool changed = count != _partyGroupCount;
        _partyGroupCount = count;
        if (!changed && (now - _partyAt).TotalSeconds < (_partySeen || count > 1 ? PartyEverySeconds : PartyLookSeconds)) return;
        _partyAt = now;
        var region = Ocr.PartyFrame.Region(g);
        if (Cover(region) is { } cover && !cover.StartsWith("Kinah Ledger")) return;
        System.Drawing.Bitmap? bmp = null;
        try
        {
            await Ui.HudWindow.HiddenIfOver(region, () => { bmp = Ocr.ScreenReader.Capture(region); return Task.CompletedTask; });
            if (bmp == null) return;
            var rows = Ocr.PartyFrame.Read(bmp, g.Height);
            bool was = _partySeen;
            _partySeen = rows.Count >= 2;
            if (!_partySeen) { if (was) CrashLog.Note("parser: the party list is gone"); return; }
            string sig = string.Join(",", rows.Select(r => r.Class + (r.Online ? "" : "~")));
            if (sig != _partySig || _partyNames.Count != rows.Count || (now - _partyNamesAt).TotalSeconds >= PartyNamesSeconds * 10)
            {
                if (sig != _partySig || (now - _partyNamesAt).TotalSeconds >= PartyNamesSeconds)
                {
                    _partyNamesAt = now;
                    var names = new List<string>();
                    foreach (var r in rows)
                    {
                        var box = System.Drawing.Rectangle.Intersect(r.NameBox, new System.Drawing.Rectangle(0, 0, bmp.Width, bmp.Height));
                        string name = "";
                        if (box.Width > 8 && box.Height > 4)
                        {
                            using var crop = bmp.Clone(box, bmp.PixelFormat);
                            try { name = Ocr.PartyFrame.CleanName((await Ocr.ScreenReader.ReadBitmapAsync(crop, boost: false, scale: 2)).FirstOrDefault() ?? ""); }
                            catch (Exception) { name = ""; }
                        }
                        names.Add(name);
                    }
                    _partyNames = names;
                    CrashLog.Note($"parser: the party list: {string.Join(", ", rows.Select((r, i) => $"{(names[i].Length > 0 ? names[i] : "?")} {(r.Class.Length > 0 ? r.Class : "unknown class")}{(r.Online ? "" : " (away)")}"))}");
                }
                _partySig = sig;
            }
            Fights.ScreenParty(rows.Select((r, i) => (i < _partyNames.Count ? _partyNames[i] : "", r.Class, i == 0)).ToList());
        }
        catch (Exception ex) { CrashLog.Write("party list", ex); }
        finally { bmp?.Dispose(); }
    }

    /// <summary>What the Parser keeps (the Parser page's mode, parser.json), set by the window; a new session starts in it.</summary>
    public static ParserMode ParserMode { get; set; } = ParserMode.Bosses;

    /// <summary>
    /// A kill or death the kill tally decided (TrackerSession.Packets.cs): to the Parser. With the reader handing kill events
    /// over as they come, only the kills it credited (no killer named, the player's Rank Points rose with it) are new here.
    /// </summary>
    void FightToParser(FightTally.Outcome o)
    {
        if (_parserWired && !o.Credited) return;   // the player's own kills and deaths came straight from the kill events
        try { Fights.Kill(o.At, o.Player, o.Death, o.Name); } catch (Exception ex) { CrashLog.Write("parser", ex); }
    }
}
