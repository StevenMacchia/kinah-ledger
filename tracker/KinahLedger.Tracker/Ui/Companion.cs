using System.Text.Json;
using System.Windows.Media;
using KinahLedger.Parser;
using KinahLedger.Session;

namespace KinahLedger.Ui;

/// <summary>
/// Buddy 2.0, the companion (Steven, 2026-11-20: "think more overlays and buddies and parsers ... WOW factor for everything"):
/// a tiny Shugo, the game's white stoat merchant (1.0.39's first one was a winged wisp; Steven, 2026-11-21: "maybe a tiny
/// shugo lol"), in its own small overlay that lives through the session with you. It idles (breathes, blinks, twitches its
/// ears, wags its tail), squares up while a boss pull is under way, dozes after a long quiet, and reacts to what the tracker already knows:
/// a rare drop, a new best on a boss, a boss down, a run of big hits, a watched timer coming close, a PvP kill or assist,
/// a wipe, your death. Each reaction is a short animation and, when Lines is on, a two-line bubble ("NEW BEST" over
/// "Gadioton +8.2%"). Nothing here guesses: every moment comes from a figure another part of the tracker shows.
/// Off by default; Settings › Companion and the header's Overlays menu switch it, and each reaction has its own switch.
/// </summary>
public enum CompanionKind { Rare, Best, Kill, Streak, Timer, Pvp, Wipe, Death }

/// <summary>A moment the companion reacts to: the kind, the bubble's small label and main line (with its accent part), and the accent's colour.</summary>
public sealed record CompanionMoment(CompanionKind Kind, string Label, string Text, string Accent, Color Tone, DateTime At)
{
    /// <summary>Which moment wins when two meet: a new best over a rare drop over a kill and so on.</summary>
    public int Rank => Kind switch
    {
        CompanionKind.Best => 8, CompanionKind.Rare => 7, CompanionKind.Pvp => 6, CompanionKind.Death => 5, CompanionKind.Wipe => 5,
        CompanionKind.Timer => 4, CompanionKind.Kill => 3, _ => 2,
    };
}

/// <summary>The companion's settings (companion.json): off by default, where it sits, click-through, the bubble's lines, and the reactions turned off.</summary>
public sealed class CompanionPrefs
{
    public bool On { get; set; }
    public bool ClickThrough { get; set; }
    /// <summary>The bubble with a line beside each reaction (on); off, the companion only moves.</summary>
    public bool Lines { get; set; } = true;
    public double? X { get; set; }
    public double? Y { get; set; }
    /// <summary>The reactions turned off, by key ("streak", "timer"...); every reaction is on by default.</summary>
    public List<string> Off { get; set; } = new();

    public bool Reacts(CompanionKind k) => !Off.Contains(Key(k));
    public void SetReacts(CompanionKind k, bool on)
    {
        Off.Remove(Key(k));
        if (!on) Off.Add(Key(k));
    }

    public static string Key(CompanionKind k) => k.ToString().ToLowerInvariant();

    static readonly JsonSerializerOptions Json = new() { WriteIndented = true };
    [System.Text.Json.Serialization.JsonIgnore] public string? Path { get; private set; }

    public static string DefaultPath => System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "KinahLedger", "companion.json");

    public static CompanionPrefs Load(string? path = null)
    {
        path ??= DefaultPath;
        CompanionPrefs p = new();
        try { if (File.Exists(path)) p = JsonSerializer.Deserialize<CompanionPrefs>(File.ReadAllText(path), Json) ?? new(); }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException) { CrashLog.Note("companion.json not read: " + ex.Message); }
        p.Off ??= new();
        p.Path = path;
        return p;
    }

    /// <summary>Settings that are never saved (renders, self-tests).</summary>
    public static CompanionPrefs Defaults() => new();

    public void Save()
    {
        if (Path == null) return;
        try
        {
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);
            var tmp = Path + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(this, Json));
            File.Move(tmp, Path, true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { CrashLog.Note("companion.json not saved: " + ex.Message); }
    }
}

/// <summary>
/// What the companion has seen, so each moment plays once: the fight that ended last, your big hits, the session's deaths,
/// the PvP kills, the timers come close. The first look at each only takes its place (a restart, a character switch or the
/// companion switched on mid-session never replays what came before). Pure: the window feeds it and plays what it returns.
/// </summary>
public sealed class CompanionWatch
{
    /// <summary>A run of big hits: this many of yours within <see cref="StreakSeconds"/>, then quiet for <see cref="StreakRestSeconds"/>.</summary>
    public const int StreakHits = 3;
    public const double StreakSeconds = 6, StreakRestSeconds = 25;
    /// <summary>A fight that ended longer ago than this is old news (read back after a restart).</summary>
    public const double FreshSeconds = 30;

    string? _ended;
    bool _endedSeen;
    long _hitSeq = -1;
    string _hitFight = "";
    readonly List<(DateTime At, bool? Crit)> _hits = new();
    DateTime _streakAt = DateTime.MinValue;
    object? _engine;
    int _deaths;
    object? _pvp;
    long _pvpSeq = -1;
    readonly HashSet<string> _timers = new();

    static readonly Color GoldTone = HudKit.Rgb(HudKit.GoldC), MintTone = HudKit.Rgb(HudKit.MintC), RedTone = HudKit.Rgb(0xFF5A5A),
        WarnTone = HudKit.Rgb(0xFFB547), FlameTone = HudKit.Rgb(0xFF8A3D), BlueTone = HudKit.Rgb(0x8FA6C8);

    /// <summary>Forget everything seen (a character switch): the next look at each only takes its place again.</summary>
    public void Reset()
    {
        _ended = null; _endedSeen = false; _hitSeq = -1; _hitFight = ""; _hits.Clear(); _streakAt = DateTime.MinValue;
        _engine = null; _deaths = 0; _pvp = null; _pvpSeq = -1; _timers.Clear();
    }

    /// <summary>A rare drop the window's moment played (MainWindow.RareTickDone): its grade over its name.</summary>
    public static CompanionMoment Rare(string name, string grade, DateTime now) =>
        new(CompanionKind.Rare, L.F("{0} DROP", L.T(grade).ToUpperInvariant()), "", name, HudKit.GradeColor(grade), now);

    /// <summary>
    /// The fights: the one that ended last (a kill: a new best on it measured as the meter's PB chip is, else a boss down;
    /// a wipe), and your big hits in the one under way (<see cref="StreakHits"/> close together: a streak, called crits only
    /// when every one of them is known to be a crit).
    /// </summary>
    public List<CompanionMoment> Fights(Encounter? live, Encounter? ended, Func<Encounter, PullRef?>? bestOf, DateTime now)
    {
        var said = new List<CompanionMoment>();
        if (ended != null && ended.Id != _ended)
        {
            bool first = !_endedSeen;
            _ended = ended.Id; _endedSeen = true;
            bool fresh = (now - (ended.End ?? now)).TotalSeconds <= FreshSeconds;
            if (!first && fresh && Ended(ended, bestOf, now) is { } m) said.Add(m);
        }
        else if (ended == null && !_endedSeen) _endedSeen = true;
        if (live is { Live: true } && live.You is { } you)
        {
            if (live.Id != _hitFight) { _hitFight = live.Id; _hits.Clear(); _hitSeq = live.BigHits is { Count: > 0 } b0 ? b0[^1].Seq : 0; }
            if (live.BigHits is { Count: > 0 } hits)
            {
                foreach (var h in hits)
                    if (h.Seq > _hitSeq && h.Member == you.Id && Math.Abs((now - h.At).TotalSeconds) <= StreakSeconds) _hits.Add((h.At, h.Crit));
                _hitSeq = Math.Max(_hitSeq, hits[^1].Seq);
            }
            _hits.RemoveAll(h => (now - h.At).TotalSeconds > StreakSeconds);
            if (_hits.Count >= StreakHits && (now - _streakAt).TotalSeconds >= StreakRestSeconds)
            {
                _streakAt = now;
                int n = _hits.Count;
                double span = Math.Max(1, Math.Ceiling((_hits[^1].At - _hits[0].At).TotalSeconds));
                bool crits = _hits.All(h => h.Crit == true);
                said.Add(new(CompanionKind.Streak, crits ? L.T("CRIT STREAK") : L.T("HOT STREAK"), crits ? L.F("{0} crits in {1} s", n, span) : L.F("{0} big hits in {1} s", n, span), "", FlameTone, now));
                _hits.Clear();
            }
        }
        return said;
    }

    static CompanionMoment? Ended(Encounter e, Func<Encounter, PullRef?>? bestOf, DateTime now)
    {
        string boss = e.Boss.Length > 0 ? e.Boss : e.Instance;
        if (e.Outcome == "wipe") return new(CompanionKind.Wipe, L.T("WIPED"), L.T("Shake it off. Next pull."), "", BlueTone, now);
        if (e.Outcome != "kill" || boss.Length == 0) return null;
        if (e.You is { } you && bestOf != null)
        {
            PullRef? best = null;
            try { best = bestOf(e); } catch (Exception) { best = null; }
            if (best != null)
            {
                var end = e.End ?? now;
                double? d = best.Dps > 0 && e.Dps(you, end) / best.Dps - 1 is > 0 and var dd ? dd : null;
                double? h = best.Hps > 0 && e.HpsOf(you, end) is { } hps && hps / best.Hps - 1 is > 0 and var hh ? hh : null;
                if (d != null) return new(CompanionKind.Best, L.T("NEW BEST"), boss + " ", PK.Signed(d.Value), GoldTone, now);
                if (h != null) return new(CompanionKind.Best, L.T("NEW HEALING BEST"), boss + " ", PK.Signed(h.Value), GoldTone, now);
            }
        }
        var secs = ((e.End ?? now) - e.Start).TotalSeconds;
        return new(CompanionKind.Kill, L.T("BOSS DOWN"), boss + (secs >= 1 ? " " : ""), secs >= 1 ? BossParser.Clock(TimeSpan.FromSeconds(Math.Round(secs))) : "", MintTone, now);
    }

    /// <summary>Your deaths this session (SessionEngine.Deaths): one more is a death; another session (a switch, a restart) only takes its place.</summary>
    public CompanionMoment? Deaths(object? engine, int deaths, DateTime now)
    {
        if (engine == null) return null;
        if (!ReferenceEquals(engine, _engine)) { _engine = engine; _deaths = deaths; return null; }
        if (deaths <= _deaths) { _deaths = deaths; return null; }
        _deaths = deaths;
        return new(CompanionKind.Death, L.T("YOU FELL"), L.T("Up you get. Again!"), "", RedTone, now);
    }

    /// <summary>The PvP panel's kills (Parser/PvpKills.cs): the newest one past the last seen, a kill or an assist on its name.</summary>
    public CompanionMoment? Pvp(PvpKills? source, DateTime now)
    {
        if (source == null) return null;
        var last = source.Last;
        long top = last.Count > 0 ? last.Max(k => k.Seq) : 0;
        if (!ReferenceEquals(source, _pvp)) { _pvp = source; _pvpSeq = top; return null; }
        if (top <= _pvpSeq) return null;
        _pvpSeq = top;
        var k = last.First(x => x.Seq == top);
        if ((now - k.At).TotalSeconds > FreshSeconds) return null;
        return new(CompanionKind.Pvp, k.Assist ? L.T("ASSIST") : L.T("TAKEDOWN"), "", k.Name, RedTone, now);
    }

    /// <summary>The watched timers come close (the timers' own window, TimerBoard.ForOverlay): each spawn once, "Gartua in 5 min".</summary>
    public List<CompanionMoment> Timers(IEnumerable<TimerBoard.Item> near, DateTime now)
    {
        var said = new List<CompanionMoment>();
        foreach (var i in near)
        {
            if (i.AtPc is not { } at) continue;
            var key = i.Key + "@" + at.ToString("yyyyMMddHHmm", System.Globalization.CultureInfo.InvariantCulture);
            if (!_timers.Add(key)) continue;
            int min = Math.Max(1, (int)Math.Ceiling((at - now).TotalMinutes));
            said.Add(new(CompanionKind.Timer, L.T("HEADS UP"), i.Short + " ", L.F("in {0} min", min), WarnTone, now));
        }
        if (_timers.Count > 200) _timers.Clear();
        return said;
    }
}
