using System.Text.Json;

namespace KinahLedger.Session;

/// <summary>Which tab a counted thing belongs to: the live feed at the top of each tab shows only its own.</summary>
public enum FeedTab { Farming, Crafting, Events }

/// <summary>
/// One thing counted, as the live feed shows it (0.8.30, direction C "Live"): when, which tab, what kind ("item", "kinah",
/// "craft"), the item or recipe, how many, and how it was read. What it is worth is worked out when it is drawn, from
/// the prices of the moment, so a price read later still values the line.
/// </summary>
/// <param name="Name">The item's name, the recipe, or "" for Kinah.</param>
/// <param name="Amount">How many of the item, the Kinah, or 1 for a craft.</param>
/// <param name="Enemy">For Kinah: the enemy the target bar credited it to, or "".</param>
/// <param name="Ok">For a craft: it worked.</param>
/// <param name="Via">How it was read (GameEvent.Via): "chat", "loot list", "reward table", "crafting panel".</param>
/// <param name="Where">The activity it landed in (Activity.Label): "Farming · Western Cantas Valley", "Expedition · Urugugu Canyon".</param>
public sealed record FeedLine(DateTime At, FeedTab Tab, string Kind, string Name, long Amount, string Enemy = "", bool Ok = true, string Via = "", string Where = "", long ItemId = 0, bool Bound = false)
{
    /// <summary>The line in plain words, as a miscount report files it: "1,192 Kinah · Cantas Raider", "Spiritstone ×1", "Orichalcum Greaves failed".</summary>
    public string Text => Kind switch
    {
        "kinah" => $"{Amount:N0} Kinah" + (Enemy.Length > 0 ? $" · {Enemy}" : ""),
        "craft" => $"{(Name.Length > 0 ? Name : "A craft")} {(Ok ? "worked" : "failed")}",
        _ => $"{Name} ×{Amount:N0}" + (Bound ? " (bound)" : ""),
    };
}

/// <summary>
/// The things counted lately, newest last, for the live feed on each tab: fed from the one place a count is committed
/// (TrackerSession.Feed and the crafting panel's race), read by the window. Keeps the last 200 so each tab still has
/// five after a long stretch of another; nothing is written to disk unless <see cref="Sink"/> is set.
/// </summary>
public sealed class LiveFeed
{
    public const int Keep = 200, Shown = 5;
    readonly List<FeedLine> _lines = new();
    readonly object _lock = new();

    /// <summary>Called with every line added (the Events tab keeps its reward screen lines on disk this way: RunScreens).</summary>
    public Action<FeedLine>? Sink { get; set; }

    /// <summary>Everything ever added, for the tests and the health line.</summary>
    public int Added { get; private set; }

    public void Add(FeedLine line)
    {
        lock (_lock)
        {
            _lines.Add(line); Added++;
            if (_lines.Count > Keep) _lines.RemoveRange(0, _lines.Count - Keep);
        }
        try { Sink?.Invoke(line); } catch (Exception ex) { CrashLog.Write("live feed", ex); }
    }

    /// <summary>The last <paramref name="n"/> lines of one tab, newest first (by time, then by the order they came).</summary>
    public List<FeedLine> Recent(FeedTab tab, int n = Shown)
    {
        lock (_lock)
            return _lines.Select((l, i) => (l, i)).Where(x => x.l.Tab == tab).OrderByDescending(x => x.l.At).ThenByDescending(x => x.i).Take(n).Select(x => x.l).ToList();
    }

    /// <summary>The newest line of any tab, for the panel over the game.</summary>
    public FeedLine? Newest(FeedTab? tab = null)
    {
        lock (_lock) return _lines.Where(l => tab == null || l.Tab == tab).OrderByDescending(l => l.At).FirstOrDefault();
    }

    public void Clear() { lock (_lock) _lines.Clear(); }

    /// <summary>
    /// The tab an activity label belongs to: a run's (event, expedition, arena, legion, world boss) is Events, the
    /// crafting panel's is Crafting, everything else (farming, town, a reward screen, the Market) is Farming.
    /// </summary>
    public static FeedTab TabOf(string where)
    {
        if (where.StartsWith("Crafting", StringComparison.Ordinal)) return FeedTab.Crafting;
        foreach (var p in new[] { "Event", "Expedition", "Arena", "Legion", "World boss", "Transcendence" })
            if (where.StartsWith(p, StringComparison.Ordinal)) return FeedTab.Events;
        return FeedTab.Farming;
    }
}

/// <summary>
/// The rate line beside the feed: one sample a minute of the hour's worth (Kinah plus loot after tax), the last 60 kept
/// in memory, drawn from the first sample to now. A new count (zone change, Clear) starts it over.
/// </summary>
public sealed class RateLine
{
    public const int Keep = 60;
    readonly List<(DateTime Minute, double Rate)> _samples = new();

    /// <summary>The samples, oldest first.</summary>
    public IReadOnlyList<(DateTime Minute, double Rate)> Samples => _samples;

    /// <summary>Takes the rate for the minute <paramref name="now"/> falls in: a second sample in the same minute replaces the first.</summary>
    public void Add(DateTime now, double rate)
    {
        var m = new DateTime(now.Year, now.Month, now.Day, now.Hour, now.Minute, 0, now.Kind);
        if (_samples.Count > 0 && _samples[^1].Minute == m) _samples[^1] = (m, rate);
        else _samples.Add((m, rate));
        if (_samples.Count > Keep) _samples.RemoveRange(0, _samples.Count - Keep);
    }

    public void Clear() => _samples.Clear();

    /// <summary>The line's points in a box of <paramref name="w"/> by <paramref name="h"/>, the lowest sample at the bottom, the highest at the top (a flat line sits in the middle).</summary>
    public List<(double X, double Y)> Points(double w, double h)
    {
        var pts = new List<(double, double)>();
        if (_samples.Count == 0) return pts;
        double lo = _samples.Min(s => s.Rate), hi = _samples.Max(s => s.Rate);
        for (int i = 0; i < _samples.Count; i++)
        {
            double x = _samples.Count == 1 ? w : w * i / (_samples.Count - 1);
            double y = hi - lo < 1e-9 ? h / 2 : h - 2 - (h - 4) * (_samples[i].Rate - lo) / (hi - lo);
            pts.Add((Math.Round(x, 1), Math.Round(y, 1)));
        }
        return pts;
    }
}

/// <summary>
/// A feed line tapped as wrong (0.8.30): what the player chose and the line as it was, filed with the miscount report
/// as line.json, so the report is a labelled case for the replay bank. The pictures listed are those the report kept
/// within a few seconds of the line; a line older than the minute the report holds has none.
/// </summary>
public sealed record MiscountLine(string Label, FeedLine? Line)
{
    /// <summary>The four choices, as the sheet says them, and the label each files under.</summary>
    public static readonly (string Label, string Text)[] Choices =
    {
        ("wrong_item", "Wrong item"), ("wrong_amount", "Wrong amount"), ("didnt_happen", "Didn't happen"), ("missing", "Missing a pickup"),
    };

    public const double PictureSeconds = 5;

    /// <summary>What the report's summary says it was filed for: "Wrong amount: 17:51:11 1,192 Kinah · Cantas Raider (chat)".</summary>
    public string Why => Choices.First(c => c.Label == Label).Text + (Line is { } l ? $": {l.At.ToLocalTime():HH:mm:ss} {l.Text}" + (l.Via.Length > 0 ? $" ({l.Via})" : "") : "");

    /// <summary>
    /// line.json: the label, the line (time, tab, kind, item, amount, enemy, how it was read, where it landed) and the
    /// report's pictures within <see cref="PictureSeconds"/> of the line, from the report's frames.jsonl lines.
    /// </summary>
    public string Json(IEnumerable<string> framesIndex)
    {
        var pics = new List<string>();
        if (Line is { } l)
            foreach (var f in framesIndex)
            {
                try
                {
                    using var d = JsonDocument.Parse(f);
                    var r = d.RootElement;
                    if (DateTime.TryParse(r.GetProperty("t").GetString(), null, System.Globalization.DateTimeStyles.RoundtripKind, out var t)
                        && Math.Abs((t.ToUniversalTime() - l.At.ToUniversalTime()).TotalSeconds) <= PictureSeconds)
                        pics.Add(r.GetProperty("f").GetString() ?? "");
                }
                catch (Exception) { /* a line that doesn't read is left out */ }
            }
        var line = Line is { } x ? new { t = x.At.ToUniversalTime().ToString("O"), tab = x.Tab.ToString(), kind = x.Kind, item = x.Name, id = x.ItemId, amount = x.Amount, enemy = x.Enemy, ok = x.Ok, via = x.Via, where = x.Where, text = x.Text } : null;
        return JsonSerializer.Serialize(new { label = Label, line, pictures = pics }, new JsonSerializerOptions { WriteIndented = true });
    }
}
