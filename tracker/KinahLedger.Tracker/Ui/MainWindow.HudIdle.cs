using System.Globalization;
using System.Windows;
using System.Windows.Media;
using KinahLedger.Data;
using KinahLedger.Ocr;
using KinahLedger.Session;

namespace KinahLedger.Ui;

/// <summary>
/// The overlay's idle line (Steven, 2026-11-17: "if 'Waiting for the first count' just display something instead because if no
/// first count comes cause of a dungeon etc its just an annoying screen. Give something informative."). Worked out here once
/// and handed to every look as the "idle" value; a look shows it where the hour waits for its first figure (HudKit.Waiting),
/// the old words only when it is empty. One short line, the most useful true fact for where the player is:
/// <list type="bullet">
/// <item>in a dungeon or an event: the place and its mode, then the last run there (else the best), then the run's number today;</item>
/// <item>in the Abyss: today's Abyss Points and Rank Points;</item>
/// <item>in the open world: today's made and time, else the last day farmed, else the best hour here;</item>
/// <item>then the next watched timer, when there is room ("Shugo 23m");</item>
/// <item>with no history at all: where counting starts.</item>
/// </list>
/// Only stored figures (the runs log, the day engine, days.json, best.json), never a guess. Worked out again when the place,
/// the day's count or the minute changes, not on every tick.
/// </summary>
public partial class MainWindow
{
    /// <summary>What the idle line can say, each figure as stored (0 or null: not known, so not said).</summary>
    internal sealed record IdleFacts
    {
        /// <summary>The dungeon or event the player is in ("" in the open world and the Abyss), and its mode as the game named it.</summary>
        public string Place { get; init; } = "";
        public string Mode { get; init; } = "";
        public bool Abyss { get; init; }
        /// <summary>The last counted run there: its worth (Kinah plus loot after the Market's cut) and length; -1 for none.</summary>
        public long LastRun { get; init; } = -1;
        public double LastRunSeconds { get; init; }
        /// <summary>The best counted run there; -1 for none.</summary>
        public long BestRun { get; init; } = -1;
        /// <summary>Runs there today, closed.</summary>
        public int RunsHereToday { get; init; }
        /// <summary>Today's farming: made and seconds of farming (the day engine, LOOT's Today side).</summary>
        public long TodayMade { get; init; }
        public double TodayActive { get; init; }
        /// <summary>Today's runs (every place) and what the counted ones made.</summary>
        public int TodayRuns { get; init; }
        public long TodayRunsValue { get; init; }
        /// <summary>Today's Abyss Points and Rank Points.</summary>
        public long TodayAp { get; init; }
        public long TodayRp { get; init; }
        /// <summary>The last day before today with farming in days.json: its date, made and seconds of farming.</summary>
        public DateTime? LastDay { get; init; }
        public long LastDayMade { get; init; }
        public double LastDayActive { get; init; }
        /// <summary>The best hour in this zone (best.json); 0 for none.</summary>
        public long BestHere { get; init; }
        /// <summary>The next watched timer's short name and how long until it ("" for none).</summary>
        public string TimerName { get; init; } = "";
        public TimeSpan TimerIn { get; init; }
    }

    /// <summary>
    /// How wide the line may run, in Inter SemiBold at 13 px: what Buddy, Pop, the design pass's looks and the themes give it
    /// at their narrowest (each holds 330 at Scale 1.1 and the meter's 350), with room to spare.
    /// </summary>
    internal const double IdleWidth = 300;
    /// <summary>Badge's feed line, 220 wide at 12.5 px: the "idleShort" value, fitted to it.</summary>
    internal const double IdleShortWidth = 212, IdleShortSize = 12.5;

    static readonly Typeface IdleFace = new(HudKit.Text, FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal);
    static double ScreenDip => _screenDip ??= VisualTreeHelper.GetDpi(new DrawingVisual()).PixelsPerDip;
    static double? _screenDip;

    /// <summary>True when <paramref name="line"/> fits <paramref name="width"/> at <paramref name="size"/> (the looks' line, by default).</summary>
    internal static bool IdleFits(string line, double width = IdleWidth, double size = 13)
    {
        // the way the overlays draw it: Display mode at the screen's scale (Ideal at 1.0 read a few pixels short on a 100% screen,
        // and a line that "fit" was cut off in the window; options-test caught it on a 100% runner)
        var ft = new FormattedText(line, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, IdleFace, size, Brushes.White, null, TextFormattingMode.Display, ScreenDip);
        return ft.WidthIncludingTrailingWhitespace <= width;
    }

    /// <summary>The short line for Badge's feed line.</summary>
    internal static string IdleShortLine(IdleFacts f) => IdleLine(f, l => IdleFits(l, IdleShortWidth, IdleShortSize));

    /// <summary>"2h 14m", "23m", "1h": a length in whole minutes, in the timers' short form and the language in use.</summary>
    internal static string IdleSpan(TimeSpan t)
    {
        int m = (int)Math.Max(1, Math.Round(t.TotalMinutes));
        string s = m < 60 ? $"{m}m" : m % 60 == 0 ? $"{m / 60}h" : $"{m / 60}h {m % 60}m";
        return TimerSay.Text(s);
    }

    /// <summary>
    /// The idle line from <paramref name="f"/>: the facts in order, each written the longest way that still fits (a shorter
    /// form, or left out); the first fact always, in its shortest form when no form fits.
    /// </summary>
    internal static string IdleLine(IdleFacts f, Func<string, bool>? fits = null)
    {
        fits ??= l => IdleFits(l);
        var parts = new List<string[]>();
        if (f.Place.Length > 0)
        {
            parts.Add(f.Mode.Length > 0 ? new[] { $"{f.Place} · {f.Mode}", f.Place } : new[] { f.Place });
            if (f.LastRun >= 0) parts.Add(new[] { L.F("Last run {0} · {1}", Fk(f.LastRun), Clock(TimeSpan.FromSeconds(f.LastRunSeconds))), L.F("Last run {0}", Fk(f.LastRun)) });
            else if (f.BestRun >= 0) parts.Add(new[] { L.F("Best run {0}", Fk(f.BestRun)) });
            if (f.RunsHereToday > 0) parts.Add(new[] { L.F("Run {0} today", f.RunsHereToday + 1) });
        }
        else if (f.Abyss && f.TodayAp > 0)
            parts.Add(f.TodayRp > 0 ? new[] { L.F("Today {0} AP · {1} RP", Fk(f.TodayAp), Fk(f.TodayRp)), L.F("Today {0} AP", Fk(f.TodayAp)) } : new[] { L.F("Today {0} AP", Fk(f.TodayAp)) });
        else if (f.TodayMade > 0 && f.TodayActive >= 60)
            parts.Add(new[] { L.F("Today {0} · {1}", Fk(f.TodayMade), IdleSpan(TimeSpan.FromSeconds(f.TodayActive))), L.F("Today {0}", Fk(f.TodayMade)) });
        else if (f.TodayRuns > 0)
            parts.Add(f.TodayRunsValue > 0
                ? new[] { L.F(f.TodayRuns == 1 ? "Today 1 run · {1}" : "Today {0} runs · {1}", f.TodayRuns, Fk(f.TodayRunsValue)), L.F(f.TodayRuns == 1 ? "Today 1 run" : "Today {0} runs", f.TodayRuns) }
                : new[] { L.F(f.TodayRuns == 1 ? "Today 1 run" : "Today {0} runs", f.TodayRuns) });
        else if (f.LastDay is DateTime d && f.LastDayMade > 0 && f.LastDayActive >= 60)
        {
            var day = d.Date == DateTime.Today.AddDays(-1) ? L.T("Yesterday") : d.Date > DateTime.Today.AddDays(-7) ? WeekdayShort(d) : DayShort(d);
            parts.Add(new[] { L.F("{0} {1} · {2}/h", day, Fk(f.LastDayMade), Fk(f.LastDayMade / (f.LastDayActive / 3600))), $"{day} {Fk(f.LastDayMade)}" });
        }
        else if (f.BestHere > 0) parts.Add(new[] { L.F("Best here {0}/h", Fk(f.BestHere)) });
        else parts.Add(new[] { L.T("Counting starts with your first Kinah or pickup"), L.T("Counting starts at your first pickup") });
        if (f.TimerName.Length > 0) parts.Add(new[] { $"{f.TimerName} {IdleSpan(f.TimerIn)}" });
        var line = parts[0].FirstOrDefault(fits) ?? parts[0][^1];
        foreach (var forms in parts.Skip(1))
            if (forms.Select(p => line + " · " + p).FirstOrDefault(fits) is { } longer) line = longer;
        return line;
    }

    /// <summary>The idle line of the renders' sample day (the open world, 1.27M made in 2 h 14 min today, Shugo in 23 min): never the player's.</summary>
    internal static string IdleSample() => IdleLine(IdleSampleFacts("open"));

    /// <summary>
    /// The sample facts for a place: "dungeon" (Fire Temple in Conquest, a run of 412K in 6:31 there, one run today), "open"
    /// (today 1.27M in 2 h 14 min), "history" (nothing today, 894K yesterday in 48 min), "abyss" (12.4K AP and 3.1K RP today),
    /// "fresh" (a new install: nothing stored). Each with Shugo in 23 min but the fresh one.
    /// </summary>
    internal static IdleFacts IdleSampleFacts(string kind) => kind switch
    {
        "dungeon" => new IdleFacts { Place = "Fire Temple", Mode = "Conquest", LastRun = 412_000, LastRunSeconds = 391, BestRun = 610_000, RunsHereToday = 1, TodayMade = 1_270_000, TodayActive = 8040, TimerName = "Shugo", TimerIn = TimeSpan.FromMinutes(23) },
        "open" => new IdleFacts { TodayMade = 1_270_000, TodayActive = 8040, BestHere = 1_400_000, TimerName = "Shugo", TimerIn = TimeSpan.FromMinutes(23) },
        "history" => new IdleFacts { LastDay = DateTime.Today.AddDays(-1), LastDayMade = 894_000, LastDayActive = 2880, BestHere = 1_400_000, TimerName = "Shugo", TimerIn = TimeSpan.FromMinutes(23) },
        "abyss" => new IdleFacts { Abyss = true, TodayAp = 12_400, TodayRp = 3_100, TimerName = "Shugo", TimerIn = TimeSpan.FromMinutes(23) },
        _ => new IdleFacts(),
    };

    /// <summary>The short form of the renders' sample, for Badge.</summary>
    internal static string IdleShortSample() => IdleShortLine(IdleSampleFacts("open"));

    string _idleKey = "", _idleLine = "", _idleShort = "", _idleTodayRate = "", _idleTodayTotal = "";

    /// <summary>
    /// The "idle" and "idleShort" values: worked out again only when the place, the instance's mode, the day's count or the
    /// minute changed, never on every tick.
    /// </summary>
    (string Line, string Short) HudIdleLines()
    {
        var zone = _watcher.Chat?.ZoneView?.Zone ?? _demoZone ?? "";
        var key = $"{zone}|{ParserFights?.GameMode}|{TodayEngine?.Events}|{DateTime.Now:yyyyMMddHHmm}|{L.Code}";
        if (key == _idleKey) return (_idleLine, _idleShort);
        _idleKey = key;
        try
        {
            var f = IdleFactsNow(zone, DateTime.UtcNow); _idleLine = IdleLine(f); _idleShort = IdleShortLine(f);
            (_idleTodayRate, _idleTodayTotal) = TodayFigures(f);
        }
        catch (Exception ex) { CrashLog.Write("the overlay's idle line", ex); _idleLine = _idleShort = _idleTodayRate = _idleTodayTotal = ""; }
        return (_idleLine, _idleShort);
    }

    /// <summary>
    /// Today's hour and made, for the whole Buddy before this count has a figure (Steven, 2026-11-21: "make sure its actually
    /// showing the whole buddy ... its not the whole buddy"): the looks draw their full card with today's figures in place
    /// of the session's, named "today". Empty in the Abyss or with under a minute farmed today.
    /// </summary>
    internal static (string Rate, string Total) TodayFigures(IdleFacts f) =>
        f.Abyss || f.TodayMade <= 0 || f.TodayActive < 60 ? ("", "") : (Fk(f.TodayMade / (f.TodayActive / 3600.0)), Fk(f.TodayMade));

    /// <summary>What the stores say about <paramref name="zone"/> and today, for the idle line.</summary>
    internal IdleFacts IdleFactsNow(string zone, DateTime nowUtc)
    {
        var midnight = DateTime.Today;
        var runs = WeekRunList();
        var f = new IdleFacts { Abyss = ZoneLocator.IsAbyss(zone) };
        // a dungeon or an event: its runs, as the Events page files them (an expedition's areas under the expedition, an event under its family)
        if (zone.Length > 0 && ZoneLocator.KindOf(zone) != null)
        {
            var place = ZoneLocator.RunName(zone);
            var family = ZoneLocator.FamilyOf(place);
            bool Here(InstanceRun r) => string.Equals(r.Zone, place, StringComparison.OrdinalIgnoreCase) || (family.Length > 0 && string.Equals(r.Zone, family, StringComparison.OrdinalIgnoreCase));
            var here = runs.Where(r => Here(r) && r.LastLoot != null).ToList();
            var counted = here.Where(r => !r.OutOfAverages).OrderBy(r => r.LastLoot).ToList();
            var parser = ParserFights;
            string mode = parser != null && KinahLedger.Parser.BossBook.Strip(parser.Where.Instance).Equals(KinahLedger.Parser.BossBook.Strip(place), StringComparison.OrdinalIgnoreCase) ? parser.GameMode : "";
            f = f with
            {
                Place = place, Mode = mode,
                LastRun = counted.Count > 0 ? RunValue(counted[^1]) : -1, LastRunSeconds = counted.Count > 0 ? counted[^1].Seconds : 0,
                BestRun = counted.Count > 0 ? counted.Max(RunValue) : -1,
                RunsHereToday = here.Count(r => TodayLoot.IsToday(r, midnight)),
            };
        }
        if (TodayEngine is { } day)
        {
            var t = TodayLoot.Farm(day, _items);
            f = f with { TodayMade = t.Made, TodayActive = t.Active, TodayAp = day.AbyssPoints, TodayRp = day.RankPoints };
        }
        var today = runs.Where(r => TodayLoot.IsToday(r, midnight)).ToList();
        f = f with { TodayRuns = today.Count, TodayRunsValue = today.Where(r => !r.OutOfAverages).Sum(RunValue) };
        var todayKey = DayStore.Today;
        if (WeekFile().Days.Where(x => x.Day != todayKey && x.Made > 0 && x.Active >= 60).OrderByDescending(x => x.Day, StringComparer.Ordinal).FirstOrDefault() is { } last)
            f = f with { LastDay = DayHistory.ParseDay(last.Day), LastDayMade = last.Made, LastDayActive = last.Active };
        if ((_demo ? new BestRuns.Best(1_400_000, 48, "2026-10-19") : BestRuns.For(Server?.Label ?? "", zone)) is { } best) f = f with { BestHere = best.PerHr };
        try
        {
            var next = TimerItems(nowUtc).Where(i => i.State == TimerBoard.Kind.Waiting && i.AtPc is { } at && at > nowUtc && _timerPrefs.Watched(i.Key)).OrderBy(i => i.AtPc).FirstOrDefault();
            if (next != null) f = f with { TimerName = next.Short, TimerIn = next.AtPc!.Value - nowUtc };
        }
        catch (Exception ex) { CrashLog.Write("the idle line's timers", ex); }
        return f;
    }
}
