using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using KinahLedger.Data;
using KinahLedger.Session;

namespace KinahLedger.Ui;

/// <summary>
/// A window state for the render-and-review loop (tools/ui-review.md): one file under tracker/tests/ui/states, read by
/// <c>ui-shot --state name</c> and the <c>--sweep</c>. A state is either the sample runs as the plain --demo render has them,
/// with the window changed (the size slider, the view), or its own runs, written in the file by name and loaded the way
/// the tracker loads its own (the flags come from the same rules). Example figures, never a player's.
/// </summary>
public sealed class UiState
{
    /// <summary>The file's name without .json: part of the render's file name.</summary>
    [JsonIgnore] public string Name { get; set; } = "";
    public string Note { get; set; } = "";
    /// <summary>The place with a run open (its band shows the step and the clock); none when null.</summary>
    public string? Live { get; set; }
    /// <summary>The Market board, by item name; an empty object is no prices at all. Absent: the demo's board.</summary>
    public Dictionary<string, long>? Prices { get; set; }
    /// <summary>The window size slider (0.8 to 1.5), as Options › General › Window size sets it.</summary>
    public double? Scale { get; set; }
    /// <summary>"compact" or "full".</summary>
    public string? View { get; set; }
    /// <summary>The look (Options › General › Look): "night" (the default), "abyss" or "daylight". A sweep renders a state without one in Night, whatever this PC's own look is.</summary>
    public string? Skin { get; set; }
    /// <summary>The character in the header (Ui/MainWindow.Characters.cs); absent, the demo's "Lysandra".</summary>
    public string? Character { get; set; }
    /// <summary>The TODAY strip: "open" (the default, so its rows can be reviewed) or "closed".</summary>
    public string Strip { get; set; } = "open";
    /// <summary>The runs; absent, the plain demo's (DemoRuns). An empty list is no runs at all.</summary>
    public List<UiStateRun>? Runs { get; set; }

    /// <summary>A state with runs of its own is an Events page state; one without applies to every page.</summary>
    [JsonIgnore] public bool EventsOnly => Runs != null;

    static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true, ReadCommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true };

    public static UiState Load(string path)
    {
        var s = JsonSerializer.Deserialize<UiState>(File.ReadAllText(path), Json) ?? new UiState();
        s.Name = System.IO.Path.GetFileNameWithoutExtension(path);
        if (s.Scale is { } sc && (sc < 0.5 || sc > 2)) throw new InvalidOperationException($"{s.Name}: scale {sc} is outside 0.5..2");
        if (s.Skin != null && Skins.All.All(k => k.Key != s.Skin)) throw new InvalidOperationException($"{s.Name}: no look named '{s.Skin}' (night, abyss, daylight)");
        return s;
    }

    /// <summary>The states folder: the one given, else tracker/tests/ui/states from the working folder or above the exe.</summary>
    public static string? FindDir(string? given)
    {
        if (given != null) return Directory.Exists(given) ? System.IO.Path.GetFullPath(given) : null;
        foreach (var rel in new[] { "tracker/tests/ui/states", "tests/ui/states", "ui/states", "states" })
            if (Directory.Exists(rel)) return System.IO.Path.GetFullPath(rel);
        for (var d = new DirectoryInfo(AppContext.BaseDirectory); d != null; d = d.Parent)
        {
            var p = System.IO.Path.Combine(d.FullName, "tests", "ui", "states");
            if (Directory.Exists(p)) return p;
        }
        return null;
    }

    /// <summary>The render's file name for a page in this state: "events-many-runs", "farming-scale-80".</summary>
    public static string PageName(string kind) => kind switch
    {
        "Open world" => "farming", "Gathering" => "gathering", "Pets" => "pets", "Market" => "market", "Mixed" => "mixed",
        "Crafting" => "crafting", "CraftingEmpty" => "crafting-empty", "CraftingUpgrades" => "crafting-upgrades", "Events" => "events", "Quests" => "quests", _ => kind.ToLowerInvariant()
    };
}

/// <summary>One line of a state's runs: a run, or <see cref="Repeat"/> runs a <see cref="Step"/> apart on each of <see cref="Days"/> days.</summary>
public sealed class UiStateRun
{
    public string Zone { get; set; } = "Urugugu Canyon";
    /// <summary>"Expedition", "Event", "Transcendence", "World Boss".</summary>
    public string Kind { get; set; } = "Expedition";
    /// <summary>The day, counted from today: 0 today, -1 yesterday.</summary>
    public int Day { get; set; }
    /// <summary>The first run's start on that day, local time, "HH:mm".</summary>
    public string Time { get; set; } = "18:00";
    /// <summary>How long the run took.</summary>
    public int Secs { get; set; } = 450;
    public long Kinah { get; set; }
    /// <summary>The rewards by item name; a name the dictionary doesn't know is made up (no icon, no price).</summary>
    public Dictionary<string, int> Items { get; set; } = new();
    /// <summary>Gear from the run, bound to the character.</summary>
    public List<string> Gear { get; set; } = new();
    /// <summary>Reward slots that were not read.</summary>
    public int Unread { get; set; }
    public bool KinahNotRead { get; set; }
    /// <summary>The reward screen was never read: the run is flagged and out of the figures.</summary>
    public bool RewardsNotRead { get; set; }
    public bool Partly { get; set; }
    /// <summary>Runs on each day.</summary>
    public int Repeat { get; set; } = 1;
    /// <summary>Minutes between the runs of a day.</summary>
    public int Step { get; set; } = 12;
    /// <summary>Days in a row, from <see cref="Day"/> on.</summary>
    public int Days { get; set; } = 1;
    /// <summary>Spread of Kinah and seconds around the figure given (0.1 is ±10%), the same every render.</summary>
    public double Vary { get; set; }
}

public partial class MainWindow
{
    /// <summary>The state a layout render was asked for; null for the plain demo.</summary>
    UiState? _uiState;

    /// <summary>
    /// One sample run as DemoRuns writes them: read from a reward picture, paid twenty seconds before the end, with the
    /// witnesses a real run carries, so the loading pass treats it like the tracker's own.
    /// </summary>
    InstanceRun SampleRun(DateTime startLocal, int secs, long kinah, IEnumerable<(string Name, int N)> items, IEnumerable<string>? gearNames = null, bool kinahNotRead = false, int unread = 0, string zone = "Urugugu Canyon", string kind = "Expedition", bool partly = false, bool rewardsNotRead = false)
    {
        var start = startLocal.ToUniversalTime();
        var r = new InstanceRun
        {
            Zone = zone, Kind = kind, Start = start, LastLoot = start.AddSeconds(secs), PaidAt = start.AddSeconds(secs - 20), Kinah = kinah, KinahNotRead = kinahNotRead, RewardsNotRead = rewardsNotRead,
            FromPicture = true, SlotsUnread = unread, PartlyRead = partly, Tables = 1, TableAt = start.AddSeconds(secs - 20), Ended = "left; rewards in",
            Witnesses = $"minimap {startLocal:HH:mm:ss} · header {startLocal.AddSeconds(4):HH:mm:ss}",
        };
        foreach (var (name, c) in items) r.Items[_items.IdOf(name)] = c;
        foreach (var gname in gearNames ?? Array.Empty<string>()) { var id = _items.IdOf(gname); r.BoundItems[id] = r.BoundItems.GetValueOrDefault(id) + 1; r.Bound++; }
        return r;
    }

    /// <summary>
    /// Sample runs written to a scratch folder's instances.jsonl and loaded the way the tracker loads its own, so the flags
    /// (several runs saved as one, Kinah not read) come from the same rules. Never the player's folder.
    /// </summary>
    InstanceLog LoadSampleRuns(List<InstanceRun> runs, string folder)
    {
        var dir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), folder);
        Directory.CreateDirectory(dir);
        foreach (var f in Directory.GetFiles(dir)) { try { File.Delete(f); } catch (Exception) { } }
        var keep = CrashLog.DirOverride;
        try
        {
            CrashLog.DirOverride = dir;   // the sample's file and notes, never the player's
            File.WriteAllLines(System.IO.Path.Combine(dir, "instances.jsonl"), runs.Select(r => JsonSerializer.Serialize(r)));
            var log = new InstanceLog();
            log.UseNames(_items);
            return log;
        }
        finally { CrashLog.DirOverride = keep; }
    }

    /// <summary>The demo's Market board (DemoRuns): the sample materials priced, the rest not.</summary>
    static Dictionary<string, long> DemoBoard() => new() { ["Spiritstone Powder"] = 3_330, ["Artisan's Ultimate Refining Stone"] = 330_000, ["Sapphire Powder"] = 5_080, ["Magic Crystal"] = 4_550, ["Diamond Powder"] = 5_780 };

    /// <summary>A state's own runs, built from its file as DemoRuns builds the sample, each line repeated over its days.</summary>
    InstanceLog StateRuns(UiState st)
    {
        PriceBook.SetBoard(st.Prices ?? DemoBoard());
        var today = DateTime.Today;
        var runs = new List<InstanceRun>();
        foreach (var e in st.Runs ?? new())
        {
            var at = TimeSpan.TryParseExact(e.Time, @"h\:mm", CultureInfo.InvariantCulture, out var tt) ? tt : new TimeSpan(18, 0, 0);
            int i = 0;
            for (int d = 0; d < Math.Max(1, e.Days); d++)
                for (int k = 0; k < Math.Max(1, e.Repeat); k++, i++)
                {
                    // a spread that is the same every render: ±Vary over the line's runs, by their index
                    double f = e.Vary <= 0 ? 1 : 1 + e.Vary * (((i * 37) % 21) - 10) / 10.0;
                    var start = today.AddDays(e.Day + d).Add(at).AddMinutes(k * Math.Max(1, e.Step));
                    runs.Add(SampleRun(start, Math.Max(10, (int)Math.Round(e.Secs * f)), (long)Math.Round(e.Kinah * f), e.Items.Select(kv => (kv.Key, kv.Value)), e.Gear,
                        e.KinahNotRead, e.Unread, e.Zone, e.Kind, e.Partly, e.RewardsNotRead));
                }
        }
        _demoLive = st.Live;
        return LoadSampleRuns(runs, "KinahLedger-state-runs");
    }

    /// <summary>How many runs a state's file asks for, by place: the fixture's own count, before any loading rule.</summary>
    static Dictionary<string, int> StateRunCounts(UiState st)
    {
        var d = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var e in st.Runs ?? new()) d[e.Zone] = d.GetValueOrDefault(e.Zone) + Math.Max(1, e.Days) * Math.Max(1, e.Repeat);
        return d;
    }

    /// <summary>
    /// The figures check (tools/ui-review.md): what the Events page should show for the runs it was given, from the data
    /// the page is fed (the run log and the price board) through the same arithmetic the page uses, never from the drawing;
    /// and whether each figure is on the page as a visible, untrimmed text. Written beside the render as .figures.json.
    /// </summary>
    internal Dictionary<string, object?> EventsFigures(UiState? st)
    {
        var fig = new Dictionary<string, object?>();
        var log = _demoInst;
        var all = log?.Snapshot() ?? new List<InstanceRun>();
        RunFixes.ApplyKept(all);
        var counted = all.Where(r => !r.OutOfAverages).ToList();
        if (st?.Runs != null) fig["fixture"] = new Dictionary<string, object?> { ["runs"] = StateRunCounts(st).Values.Sum(), ["byPlace"] = StateRunCounts(st) };
        fig["data"] = new Dictionary<string, object?>
        {
            ["runs"] = all.Count, ["counted"] = counted.Count, ["flagged"] = all.Count - counted.Count,
            ["places"] = all.Select(r => r.Zone).Distinct(StringComparer.OrdinalIgnoreCase).Count(),
            ["kinah"] = counted.Sum(r => r.Kinah), ["made"] = counted.Sum(RunValue),
        };
        // the expected texts: each must be on the page whole, in a visible text that isn't trimmed; "whole" texts equal a text, the rest are part of one
        var expect = new List<(string What, string Text, bool Whole)>();
        bool severalPlaces = all.Select(r => r.Zone).Distinct(StringComparer.OrdinalIgnoreCase).Count() > 1;
        if (all.Count > 0) expect.Add(("heading", $"{all.Count} run{(all.Count == 1 ? "" : "s")}" + (severalPlaces ? $" · {Fk(counted.Sum(RunValue))} made" : ""), !severalPlaces));
        else expect.Add(("empty", "Run an expedition or join an event.", false));
        var live = _demoLive;
        var places = all.GroupBy(r => r.Zone, StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(g => string.Equals(g.Key, live, StringComparison.OrdinalIgnoreCase))
            .ThenByDescending(g => g.Max(r => r.LastLoot ?? r.Start)).Select(g => g.Key).ToList();
        if (places.Count > 0)
        {
            var picked = PickedPlace(places, live);
            var runs = all.Where(r => string.Equals(r.Zone, picked, StringComparison.OrdinalIgnoreCase)).ToList();
            var pc = runs.Where(r => !r.OutOfAverages).ToList();
            int n = pc.Count; double secs = pc.Sum(r => r.Seconds); long value = pc.Sum(RunValue);
            bool known = value > 0 || pc.Any(r => r.Kinah > 0);
            var place = new Dictionary<string, object?> { ["name"] = picked, ["runs"] = runs.Count, ["counted"] = n };
            expect.Add(("place name", picked, false));
            // the band's line and pill, as PlaceBand writes them: the runs, the open run's number and start, its step and clock (or when the place was last run)
            var open = string.Equals(live, picked, StringComparison.OrdinalIgnoreCase) ? runs.OrderBy(r => r.Start).LastOrDefault() : null;
            // the top card: the latest run (the default since 2026-11-14, Ui/MainWindow.LatestRun.cs) or All runs
            bool allView = _runsView == "all";
            place["view"] = allView ? "all runs" : "latest run";
            var newest = runs.OrderByDescending(r => r.LastLoot ?? r.Start).First();
            if (allView)
            {
                var meta = RunBandLine(runs, open);
                place["band"] = meta;
                expect.Add(("band line", meta, true));
                var lastAt = (newest.LastLoot ?? newest.Start).ToLocalTime();
                var pill = open != null ? RunPillText(open)
                    : lastAt.Date == DateTime.Today ? $"last {lastAt:HH:mm}" : $"last {lastAt.ToString("MMM d", CultureInfo.InvariantCulture)}";
                place["pill"] = pill;
                expect.Add(("band pill", pill, true));
            }
            else
            {
                var latest = LatestRunOf(runs, open)!;
                int number = runs.OrderBy(r => r.Start).ToList().IndexOf(latest) + 1;
                var lv = RunValue(latest);
                place["latest"] = new Dictionary<string, object?> { ["number"] = number, ["value"] = lv > 0 || latest.Kinah > 0 ? Fk(lv) : null, ["kinah"] = latest.Kinah > 0 ? Fk(latest.Kinah) : null, ["kills"] = latest.Kills };
                expect.Add(("latest run's number", $"Run {number} · ", false));
                if (lv > 0 || latest.Kinah > 0) expect.Add(("latest run's worth", Fk(lv), false));
                if (latest.Kinah > 0) expect.Add(("latest run's Kinah", Fk(latest.Kinah) + " Kinah", false));
                if (latest.Kills > 0) expect.Add(("latest run's kills", latest.Kills.ToString("N0", CultureInfo.InvariantCulture) + (latest.Kills == 1 ? " kill" : " kills"), false));
            }
            if (n > 0 && allView)
            {
                if (known && secs > 10) { place["anHour"] = Fk(value / secs * 3600); expect.Add(("an hour", Fk(value / secs * 3600) + " an hour", false)); }
                if (known) { place["aRun"] = Fk(value / (double)n); expect.Add(("a run", Fk(value / (double)n) + " a run", false)); }
                place["aRunTime"] = Clock(TimeSpan.FromSeconds(secs / n)); expect.Add(("a run's time", Clock(TimeSpan.FromSeconds(secs / n)) + " a run", false));
                if (known && n > 1) { place["bestRun"] = Fk(pc.Max(RunValue)); expect.Add(("best run", Fk(pc.Max(RunValue)) + " best run", false)); }
                if (!known) { place["noPrices"] = true; expect.Add(("no prices", "no prices yet", false)); }
                var trAll = TodayLoot.Runs(pc, _items, IsCurrency);
                place["kinah"] = trAll.Kinah > 0 ? Fk(trAll.Kinah) : null;
                if (trAll.Kinah > 0) expect.Add(("Kinah chip", Fk(trAll.Kinah), true));
            }
            if (n > 0)
            {
                // Paid out here, under either top card
                var tr = TodayLoot.Runs(pc, _items, IsCurrency);
                place["lootWorth"] = tr.Value > 0 ? Fk(tr.Value) : null;
                if (tr.Value > 0) expect.Add(("loot worth", Fk(tr.Value), true));
                place["loot"] = tr.Items.ToDictionary(s => s.Name, s => s.Count);
                // the slot badges: the first row is drawn, the rest sit behind Show all
                int shown = RunSlotsPerRow;   // Paid out here (Ui/MainWindow.QuestRuns.cs): one row of slots, then Show all
                foreach (var s in tr.Items.Take(_todayAll.Contains("place:" + picked) ? int.MaxValue : shown)) expect.Add(($"loot {s.Name}", CountBadge(s.Count), true));
                expect.Add(("loot line", n == 1 ? "from the run here" : $"from the {n} runs here", false));
            }
            else if (allView) expect.Add(("no run counted", runs.Count == 0 ? "No run here yet." : "No run counted yet.", false));   // the latest run's card shows the run itself
            fig["place"] = place;
        }
        // the TODAY strip's line and sum (the Today side is its line since 0.10.21; the day's loot is the place box's)
        var tl = TodayLoot.Runs(TodayRunList(), _items, IsCurrency);
        var line = TodayLoot.RunsLine(tl);
        var today = new Dictionary<string, object?>
        {
            ["runs"] = tl.Runs, ["line"] = line.Count == 0 ? "No runs yet today" : string.Join(" · ", line.Select(x => x.Fig + x.Word)),
            ["sum"] = tl.Value > 0 ? Fk(tl.Value) : null, ["kinah"] = tl.Kinah > 0 ? Fk(tl.Kinah) : null, ["loot"] = tl.Items.ToDictionary(s => s.Name, s => s.Count),
        };
        fig["today"] = today;
        if (TodayStrip.IsVisible)
        {
            expect.Add(("today line", (string)today["line"]!, false));
            if (tl.Value > 0) expect.Add(("today sum", Fk(tl.Value), true));
        }
        // the page's texts, as drawn
        var texts = VisibleTexts();
        // a figure (a number) missing or trimmed fails the check; a label (a name, a sentence) missing fails it, trimmed is a
        // finding for the review (the rubric's "cut off"), not a failure
        var found = new List<string>(); var missing = new List<string>(); var trimmed = new List<string>(); var labelsTrimmed = new List<string>();
        foreach (var (what, text, whole) in expect)
        {
            var hits = texts.Where(t => whole ? t.Text == text : t.Text.Contains(text, StringComparison.Ordinal)).ToList();
            if (hits.Count == 0) missing.Add($"{what}: \"{text}\"");
            else if (hits.All(h => h.Clipped)) (text.Any(char.IsDigit) ? trimmed : labelsTrimmed).Add($"{what}: \"{text}\" (trimmed on the page: \"{hits[0].Text}\")");
            else found.Add($"{what}: \"{text}\"");
        }
        var clipped = texts.Where(t => t.Clipped).Select(t => t.Text).Distinct().ToList();
        var wide = texts.Where(t => t.PastEdge).Select(t => t.Text).Distinct().ToList();
        fig["check"] = new Dictionary<string, object?>
        {
            ["pass"] = missing.Count == 0 && trimmed.Count == 0 && wide.Count == 0,
            ["found"] = found, ["missing"] = missing, ["trimmed"] = trimmed, ["labelsTrimmed"] = labelsTrimmed,
            ["clippedTexts"] = clipped, ["pastWindowEdge"] = wide,
        };
        return fig;
    }

    /// <summary>
    /// Every text drawn on the page: visible text blocks, with whether the block trims its text (an ellipsis: its natural
    /// width is more than it got) and whether it reaches past the window's right edge. Measuring dirties the layout, so
    /// this runs after the render is saved.
    /// </summary>
    internal List<(string Text, bool Clipped, bool PastEdge)> VisibleTexts()
    {
        var list = new List<(string, bool, bool)>();
        double edge = ActualWidth;
        void Walk(DependencyObject d)
        {
            int n = VisualTreeHelper.GetChildrenCount(d);
            for (int i = 0; i < n; i++)
            {
                var c = VisualTreeHelper.GetChild(d, i);
                if (c is TextBlock tb && tb.IsVisible && tb.ActualWidth > 0 && new System.Windows.Documents.TextRange(tb.ContentStart, tb.ContentEnd).Text is { Length: > 0 } text)
                {
                    bool past = false;
                    try { var r = tb.TransformToAncestor(this).TransformBounds(new Rect(0, 0, tb.ActualWidth, tb.ActualHeight)); past = r.Right > edge + 0.5; } catch (InvalidOperationException) { }
                    bool clipped = false;
                    if (tb.TextTrimming != TextTrimming.None && tb.TextWrapping == TextWrapping.NoWrap)
                    {
                        double shown = tb.ActualWidth;
                        tb.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                        clipped = tb.DesiredSize.Width - tb.Margin.Left - tb.Margin.Right > shown + 0.6;
                    }
                    list.Add((text, clipped, past));
                }
                Walk(c);
            }
        }
        Walk(this);
        return list;
    }
}
