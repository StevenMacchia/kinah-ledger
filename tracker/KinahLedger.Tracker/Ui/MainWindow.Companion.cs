using System.Windows;
using System.Windows.Threading;
using KinahLedger.Parser;
using KinahLedger.Session;

namespace KinahLedger.Ui;

/// <summary>
/// The companion's reach (Ui/Companion.cs): its switch in the header's Overlays menu and Settings › Companion (off by
/// default), and a tick twice a second that hands it what the tracker already knows: the fight that ended (a new best, a
/// boss down, a wipe), your big hits in the one under way, your deaths this session, the PvP panel's kills, the watched
/// timers come close; a rare drop comes from the window's own moment (RareTickDone). It shows while the game is open, rests
/// while a fight is on (leaning in) and dozes after <see cref="SleepMinutes"/> with nothing happening. Its settings save as
/// they change (companion.json).
/// </summary>
public partial class MainWindow
{
    CompanionPrefs _companionPrefs = CompanionPrefs.Defaults();
    CompanionOverlay? _companion;
    DispatcherTimer? _companionTick;
    readonly CompanionWatch _companionWatch = new();
    object? _companionFights;
    string _companionActivity = "";
    DateTime _companionAwake = DateTime.UtcNow;
    /// <summary>Nothing for this long (no pickup, kill, gathering, fight or moment): the companion dozes.</summary>
    const double SleepMinutes = 8;

    /// <summary>Once, as the window opens: the Overlays entry; for a watching window the settings and the tick.</summary>
    void InitCompanion(bool watch)
    {
        RegisterOverlay("Companion", () => CompanionOn, on => SetCompanionOverlay(on), L.T("A little companion that reacts to your drops, bests and fights. Set up in Settings › Companion."));
        if (!watch) return;
        _companionPrefs = CompanionPrefs.Load();
        _companionTick = new DispatcherTimer(TimeSpan.FromMilliseconds(500), DispatcherPriority.Background, (_, _) => CompanionTick(DateTime.UtcNow), Dispatcher);
        _companionTick.Start();
        Closed += (_, _) => { _companionTick?.Stop(); _companion?.Close(); _companion = null; };
    }

    /// <summary>Half a second's work: what happened handed to the companion, its mood, and whether it shows.</summary>
    void CompanionTick(DateTime now)
    {
        if (!_companionPrefs.On || (_demo && !CompanionForceForTest)) { CloseCompanion(); return; }
        List<CompanionMoment> said;
        bool fight;
        try { said = CompanionMoments(now, out fight); }
        catch (Exception ex) { CrashLog.Write("companion", ex); return; }
        bool shown = CompanionForceForTest || _watcher.GameRect != null;
        if (shown && _companion == null) OpenCompanion();
        _companion?.ShowWanted(shown);
        if (_companion == null) return;
        if (said.Count > 0 || fight) _companionAwake = now;
        _companion.SetMood(fight ? CompanionSprite.Mood.Focused : (now - _companionAwake).TotalMinutes >= SleepMinutes ? CompanionSprite.Mood.Sleepy : CompanionSprite.Mood.Idle);
        if (!shown) return;
        foreach (var m in said.Where(m => _companionPrefs.Reacts(m.Kind)).OrderByDescending(m => m.Rank)) _companion.Play(m);
    }

    /// <summary>What the tracker knows now, read by the watch: the moments new since the last tick, and whether a fight is on.</summary>
    List<CompanionMoment> CompanionMoments(DateTime now, out bool fight)
    {
        var fights = ParserFights;
        if (!ReferenceEquals(fights, _companionFights)) { _companionFights = fights; _companionWatch.Reset(); }
        var live = ParserLive ?? fights?.AlwaysLive;
        fight = live is { Live: true, Waiting: false, HasDamage: true };
        var said = _companionWatch.Fights(live, fights?.LastEnded, ParserRows.BestOf, now);
        if (_watcher.Chat is { } chat)
        {
            var eng = chat.Engine;
            if (_companionWatch.Deaths(eng, eng.Deaths, now) is { } d) said.Add(d);
            var act = $"{eng.Kinah}|{eng.Kills}|{eng.Nodes}|{eng.AbyssPoints}";
            if (act != _companionActivity) { _companionActivity = act; _companionAwake = now; }
        }
        if (_companionWatch.Pvp(PvpSource, now) is { } k) said.Add(k);
        if (_companionPrefs.Reacts(CompanionKind.Timer))
            said.AddRange(_companionWatch.Timers(TimerBoard.ForOverlay(TimerItems(now), _timerPrefs, now), now));
        return said;
    }

    /// <summary>A rare drop the window's moment played: the companion's too (MainWindow.RareTickDone).</summary>
    void CompanionRare(string name, string grade)
    {
        if (!_companionPrefs.On || (_demo && !CompanionForceForTest) || _companion is not { Wanted: true } c || !_companionPrefs.Reacts(CompanionKind.Rare)) return;
        _companionAwake = DateTime.UtcNow;
        c.Play(CompanionWatch.Rare(name, grade, DateTime.UtcNow));
    }

    void OpenCompanion()
    {
        if (_companion != null) return;
        _companion = new CompanionOverlay(_companionPrefs)
        {
            KinahBox = () => _hud is { IsVisible: true } h && h.ActualWidth > 0 ? new Rect(h.Left, h.Top, h.ActualWidth, h.ActualHeight) : null,
        };
        MatchCompanion();
        _companion.SetClickThrough(_companionPrefs.ClickThrough);
        _companion.Closed += (_, _) => _companion = null;
    }

    void MatchCompanion() => _companion?.Match(HudThemes.Of(HudLook), _hudPreview?.HudOpacity ?? _prefs.HudOpacity, _hudPreview?.HudScale ?? _hudPrefs.Scale);

    void CloseCompanion() { _companion?.Close(); _companion = null; }

    /* ---------------- callable from Options and the Overlays menu ---------------- */

    internal CompanionPrefs CompanionChoices => _companionPrefs;
    internal bool CompanionOn => _companionPrefs.On;

    /// <summary>The companion on or off (and saved).</summary>
    internal void SetCompanionOverlay(bool on, bool save = true)
    {
        _companionPrefs.On = on;
        if (save) _companionPrefs.Save();
        if (!on) CloseCompanion();
        else { _companionAwake = DateTime.UtcNow; CompanionTick(DateTime.UtcNow); }
        DrawOverlayPills();
    }

    /// <summary>Reset position: the spot forgotten; it sits just right of the Kinah overlay again.</summary>
    internal void ResetCompanionPosition()
    {
        _companionPrefs.X = null; _companionPrefs.Y = null; _companionPrefs.Save();
        _companion?.ResetSpot();
    }

    internal void SetCompanionClickThrough(bool on)
    {
        _companionPrefs.ClickThrough = on; _companionPrefs.Save();
        _companion?.SetClickThrough(on);
    }

    internal void SetCompanionLines(bool on)
    {
        _companionPrefs.Lines = on; _companionPrefs.Save();
        _companion?.LinesChanged();
    }

    internal void SetCompanionReacts(CompanionKind k, bool on) { _companionPrefs.SetReacts(k, on); _companionPrefs.Save(); }

    /// <summary>A reaction played now, as a sample (Settings › Companion's Try buttons): the moment as it would come.</summary>
    internal void TryCompanion(CompanionKind k)
    {
        if (_companion is not { Wanted: true } c) return;
        c.Play(CompanionSample(k, DateTime.UtcNow));
    }

    /// <summary>The window drawn again in the theme and at the opacity and size in use (after Options' Save, a look picked).</summary>
    internal void RefreshCompanion() => MatchCompanion();

    /// <summary>A sample of each moment, with names the game uses (renders, the Try buttons, the tests).</summary>
    internal static CompanionMoment CompanionSample(CompanionKind k, DateTime now) => k switch
    {
        CompanionKind.Rare => CompanionWatch.Rare("Ancient Kaisinel Greatsword", "Legend", now),
        CompanionKind.Best => new(k, L.T("NEW BEST"), "Gadioton ", PK.Signed(0.082), HudKit.Rgb(HudKit.GoldC), now),
        CompanionKind.Kill => new(k, L.T("BOSS DOWN"), "Gadioton ", "3:12", HudKit.Rgb(HudKit.MintC), now),
        CompanionKind.Streak => new(k, L.T("CRIT STREAK"), L.F("{0} crits in {1} s", 4, 3), "", HudKit.Rgb(0xFF8A3D), now),
        CompanionKind.Timer => new(k, L.T("HEADS UP"), "Eternal Gartua ", L.F("in {0} min", 5), HudKit.Rgb(0xFFB547), now),
        CompanionKind.Pvp => new(k, L.T("TAKEDOWN"), "", "Kyralis", HudKit.Rgb(0xFF5A5A), now),
        CompanionKind.Wipe => new(k, L.T("WIPED"), L.T("Shake it off. Next pull."), "", HudKit.Rgb(0x8FA6C8), now),
        _ => new(k, L.T("YOU FELL"), L.T("Up you get. Again!"), "", HudKit.Rgb(0xFF5A5A), now),
    };

    /// <summary>For the self-test: the settings, the tick, the window, and a window shown with no game open.</summary>
    internal CompanionPrefs CompanionPrefsForTest { get => _companionPrefs; set => _companionPrefs = value; }
    internal void CompanionTickForTest(DateTime now) => CompanionTick(now);
    internal CompanionOverlay? CompanionOverlayForTest => _companion;
    internal bool CompanionForceForTest { get; set; }
    internal void CompanionRareForTest(string name, string grade) => CompanionRare(name, grade);
}
