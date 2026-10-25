using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using KinahLedger.Session;

namespace KinahLedger.Upload;

/// <summary>
/// The accuracy routes beside /v1/runs (2026-10-23): reading health and misread names (/v1/accuracy), where other
/// trackers with this screen size found the chat (/v1/layout), and a miscount report's pictures when the player chose to
/// send them (/v1/frames). Every address comes from upload.endpoint, so blanking it in settings.json stops all of them.
/// A site that doesn't answer (or an older one that doesn't know a route) changes nothing: the tracker keeps working.
/// </summary>
public static class AccuracyClient
{
    /// <summary>The runs address with <paramref name="route"/> in place of "runs"; "" when it doesn't end in /v1/runs.</summary>
    public static string Url(UploadSettings u, string route)
    {
        var e = (u.Endpoint ?? "").Trim().TrimEnd('/');
        return e.EndsWith("/v1/runs", StringComparison.OrdinalIgnoreCase) ? e[..^4] + route : "";
    }

    static HttpClient Client(TimeSpan timeout)
    {
        var http = new HttpClient { Timeout = timeout };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("KinahLedger/" + RunPayload.AppVersion);
        return http;
    }

    // ---- reports the site could not take yet ----
    // The live site answered 404 to every report on 2026-10-24 (19:10 and 19:25): /v1/accuracy is on the backend/accuracy
    // branch of the site's repo and the deploys came from main, which lacks it. Those reports were dropped. A report the
    // site doesn't know a route for is now kept, up to MaxQueued of them (the oldest go first), in accuracy-queue.json
    // beside the logs, and offered again, oldest first, with the next send; one that the site refuses for its shape
    // (400, 413, 415) is still dropped, as it would be refused again.

    /// <summary>Most reports kept for a later send, and the longest they are kept.</summary>
    public const int MaxQueued = 50;
    public const int QueuedDays = 7;

    /// <summary>One report waiting for the site: when it was taken, the version that took it, the report.</summary>
    public sealed record Queued(DateTime At, string App, AccuracyReport Report);

    static readonly object QueueLock = new();
    static string QueuePath => Path.Combine(CrashLog.Dir, "accuracy-queue.json");

    static List<Queued> LoadQueue()
    {
        lock (QueueLock)
        {
            try
            {
                if (!File.Exists(QueuePath)) return new();
                var q = JsonSerializer.Deserialize<List<Queued>>(File.ReadAllText(QueuePath)) ?? new();
                return q.Where(x => x.Report != null && (DateTime.UtcNow - x.At).TotalDays < QueuedDays).ToList();
            }
            catch (Exception) { return new(); }
        }
    }

    static void SaveQueue(List<Queued> q)
    {
        lock (QueueLock)
        {
            try
            {
                if (q.Count == 0) { if (File.Exists(QueuePath)) File.Delete(QueuePath); return; }
                File.WriteAllText(QueuePath, JsonSerializer.Serialize(q.TakeLast(MaxQueued).ToList()));
            }
            catch (Exception) { /* kept in memory for this send only */ }
        }
    }

    /// <summary>How many reports wait for the site (the tests, the log line).</summary>
    internal static int QueuedCount() => LoadQueue().Count;

    /// <summary>How the site answered one report.</summary>
    enum Answer { Delivered, Refused, Missing, Failed }

    static async Task<(Answer A, int Status)> PostAsync(HttpClient http, string url, AccuracyPayload body)
    {
        try
        {
            using var r = await http.PostAsJsonAsync(url, body);
            int s = (int)r.StatusCode;
            if (r.IsSuccessStatusCode) return (Answer.Delivered, s);
            return (s is 400 or 413 or 415 ? Answer.Refused : s == 404 ? Answer.Missing : Answer.Failed, s);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException) { return (Answer.Failed, 0); }
    }

    /// <summary>One send at a time: the kept reports are read, changed and written back as a whole.</summary>
    static readonly SemaphoreSlim SendGate = new(1, 1);

    /// <summary>
    /// Sends what added up since the last accepted send (Accuracy.Take), after the reports kept for the site (oldest first,
    /// stopping at the first it can't take). A report the site has no route for (404) is kept for the next send; one that
    /// failed otherwise goes back into the counters, as before; one the site refuses for its shape is dropped. Sharing off
    /// or no site address: nothing is sent, the counters start over and nothing is kept. The accuracy_upload kill switch
    /// drops this report and leaves the kept ones. Returns whether this send's report was accepted.
    /// </summary>
    public static async Task<bool> SendAsync(UploadSettings u)
    {
        await SendGate.WaitAsync();
        try { return await SendCoreAsync(u); }
        finally { SendGate.Release(); }
    }

    static async Task<bool> SendCoreAsync(UploadSettings u)
    {
        var url = Url(u, "accuracy");
        var report = Accuracy.Take();
        if (!u.Enabled || url.Length == 0) { SaveQueue(new()); return false; }
        if (Update.RemoteConfig.Current.Off("accuracy_upload")) return false;
        var queue = LoadQueue();
        if (report.Empty && queue.Count == 0) return false;
        using var http = Client(TimeSpan.FromSeconds(15));
        var install = InstallId.Get();
        Answer? down = null;   // how the site answered the first kept report it couldn't take
        foreach (var q in queue.ToList())
        {
            var (a, s) = await PostAsync(http, url, AccuracyPayload.From(q.Report, install, q.App));
            if (a is Answer.Delivered or Answer.Refused)
            {
                queue.Remove(q);
                if (a == Answer.Refused) CrashLog.NoteBurst($"accuracy report from {q.At.ToLocalTime():MMM d HH:mm}: the site answered {s}; dropped");
                continue;
            }
            down = a;
            CrashLog.NoteBurst($"accuracy report: the site answered {s}; {queue.Count} kept report(s) waiting");
            break;
        }
        bool sent = false;
        if (!report.Empty)
        {
            Answer a = Answer.Failed; int s = 0;
            if (down == null) (a, s) = await PostAsync(http, url, AccuracyPayload.From(report, install));
            else a = down.Value;
            switch (a)
            {
                case Answer.Delivered: sent = true; break;
                case Answer.Refused: CrashLog.NoteBurst($"accuracy report: the site answered {s}; dropped"); break;
                case Answer.Missing:
                    queue.Add(new Queued(DateTime.UtcNow, RunPayload.AppVersion, report));
                    if (down == null) CrashLog.NoteBurst($"accuracy report: the site answered 404; kept to send again ({Math.Min(queue.Count, MaxQueued)} waiting)");
                    break;
                default: Accuracy.Restore(report); break;   // a failure that may pass: back into the counters for the next report
            }
        }
        SaveQueue(queue);
        return sent;
    }

    // ---- layout hints ----

    /// <summary>A chat area other trackers found on this screen size, best first (fractions of the game window).</summary>
    public sealed record ChatHint(int Ui, double[] Box, int Installs);

    static string HintsPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "KinahLedger", "layout-hints.json");

    /// <summary>The site's chat areas for a game window of this size. Kept per size for a week; null when none is known.</summary>
    public static async Task<List<ChatHint>?> LayoutAsync(UploadSettings u, int w, int h)
    {
        if (Update.RemoteConfig.Current.Off("layout_hint")) return null;
        var res = $"{w}x{h}";
        var kept = ReadHints();
        if (kept.TryGetValue(res, out var k) && (DateTime.UtcNow - k.At).TotalDays < 7) return k.Hints;
        var url = Url(u, "layout");
        if (!u.Enabled || url.Length == 0) return kept.TryGetValue(res, out var old) ? old.Hints : null;
        try
        {
            using var http = Client(TimeSpan.FromSeconds(10));
            using var r = await http.GetAsync($"{url}?w={w}&h={h}");
            if (!r.IsSuccessStatusCode) return null;
            var hints = ParseHints(await r.Content.ReadAsStringAsync());
            kept[res] = (DateTime.UtcNow, hints);
            WriteHints(kept);
            return hints;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException) { return null; }
    }

    /// <summary>The chat boxes of a /v1/layout answer, each held to the game window (fractions 0 to 1, a sensible size).</summary>
    public static List<ChatHint> ParseHints(string json)
    {
        var list = new List<ChatHint>();
        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("chat", out var chat) || chat.ValueKind != JsonValueKind.Array) return list;
        foreach (var c in chat.EnumerateArray())
        {
            if (!c.TryGetProperty("box", out var b) || b.ValueKind != JsonValueKind.Array || b.GetArrayLength() != 4) continue;
            var box = b.EnumerateArray().Select(x => x.ValueKind == JsonValueKind.Number ? x.GetDouble() : -1).ToArray();
            if (!LayoutProfile.Plausible(box)) continue;
            int ui = c.TryGetProperty("ui", out var uv) && uv.TryGetInt32(out var ui0) ? ui0 : 0;
            int n = c.TryGetProperty("installs", out var iv) && iv.TryGetInt32(out var n0) ? n0 : 0;
            list.Add(new ChatHint(ui, box, n));
        }
        return list.OrderByDescending(x => x.Installs).Take(4).ToList();
    }

    static Dictionary<string, (DateTime At, List<ChatHint> Hints)> ReadHints()
    {
        try
        {
            if (!File.Exists(HintsPath)) return new();
            var raw = JsonSerializer.Deserialize<Dictionary<string, HintFile>>(File.ReadAllText(HintsPath)) ?? new();
            return raw.ToDictionary(kv => kv.Key, kv => (kv.Value.At, kv.Value.Hints.Where(h => LayoutProfile.Plausible(h.Box)).ToList()));
        }
        catch (Exception) { return new(); }
    }

    static void WriteHints(Dictionary<string, (DateTime At, List<ChatHint> Hints)> d)
    {
        try { File.WriteAllText(HintsPath, JsonSerializer.Serialize(d.ToDictionary(kv => kv.Key, kv => new HintFile(kv.Value.At, kv.Value.Hints)))); }
        catch (Exception) { /* asked again next time */ }
    }

    sealed record HintFile(DateTime At, List<ChatHint> Hints);

    // ---- miscount pictures ----

    /// <summary>One picture of a miscount report as sent: when, which part (loot list or crafting panel), where in the game window, the PNG.</summary>
    public sealed record FramePayload(string t, string k, int x, int y, string png);
    public sealed record CountedPayload(string t, string what, string via);
    public sealed record FramesPayload(string app, string install, string why, Dictionary<string, int> game, List<CountedPayload> counted, List<FramePayload> frames);

    /// <summary>Most pictures per report and bytes per report, as the site accepts them.</summary>
    public const int MaxFrames = 60;
    public const long MaxBytes = 4_500_000;

    /// <summary>
    /// Sends a saved miscount report's pictures of the loot list and the crafting panel, never the chat. Returns a
    /// plain sentence for the status line.
    /// </summary>
    public static async Task<(bool Ok, string Note)> SendFramesAsync(UploadSettings u, string reportDir, string why)
    {
        var url = Url(u, "frames");
        if (url.Length == 0) return (false, "no site address in settings.json");
        var payload = BuildFrames(reportDir, why);
        if (payload == null || payload.frames.Count == 0) return (false, "no loot list or crafting panel pictures in the last minute");
        try
        {
            using var http = Client(TimeSpan.FromSeconds(60));
            using var r = await http.PostAsJsonAsync(url, payload);
            if (r.IsSuccessStatusCode) return (true, $"{payload.frames.Count} picture(s) sent");
            return (false, $"the site answered {(int)r.StatusCode}");
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException) { return (false, "couldn't reach the site"); }
    }

    /// <summary>
    /// The report folder's loot list and crafting panel pictures (frames.jsonl), newest first up to the limits, with
    /// what was counted. Chat pictures are never read here. Positions are made relative to the game window.
    /// </summary>
    public static FramesPayload? BuildFrames(string reportDir, string why)
    {
        var index = Path.Combine(reportDir, "frames.jsonl");
        if (!File.Exists(index)) return null;
        int gx = 0, gy = 0, gw = 0, gh = 0;
        var gameFile = Path.Combine(reportDir, "game.json");
        if (File.Exists(gameFile))
            try
            {
                using var g = JsonDocument.Parse(File.ReadAllText(gameFile));
                gx = g.RootElement.GetProperty("x").GetInt32(); gy = g.RootElement.GetProperty("y").GetInt32();
                gw = g.RootElement.GetProperty("w").GetInt32(); gh = g.RootElement.GetProperty("h").GetInt32();
            }
            catch (Exception) { }
        var frames = new List<FramePayload>();
        long bytes = 0;
        foreach (var line in File.ReadLines(index).Reverse())
        {
            try
            {
                using var d = JsonDocument.Parse(line);
                var r = d.RootElement;
                var k = r.GetProperty("k").GetString();
                if (k is not ("loot" or "craft")) continue;   // the chat stays on this PC, always
                var file = Path.GetFullPath(Path.Combine(reportDir, r.GetProperty("f").GetString()!));
                if (!file.StartsWith(Path.GetFullPath(reportDir), StringComparison.OrdinalIgnoreCase) || !File.Exists(file)) continue;
                var png = File.ReadAllBytes(file);
                if (bytes + png.Length > MaxBytes || frames.Count >= MaxFrames) break;
                bytes += png.Length;
                frames.Add(new FramePayload(r.GetProperty("t").GetString() ?? "", k, r.GetProperty("x").GetInt32() - gx, r.GetProperty("y").GetInt32() - gy, Convert.ToBase64String(png)));
            }
            catch (Exception) { /* a picture that doesn't read is left out */ }
        }
        frames.Reverse();
        var counted = new List<CountedPayload>();
        var cf = Path.Combine(reportDir, "counted.jsonl");
        if (File.Exists(cf))
            foreach (var line in File.ReadLines(cf).Take(200))
                try
                {
                    using var d = JsonDocument.Parse(line);
                    counted.Add(new CountedPayload(d.RootElement.GetProperty("t").GetString() ?? "", d.RootElement.GetProperty("what").GetString() ?? "", d.RootElement.GetProperty("via").GetString() ?? ""));
                }
                catch (Exception) { }
        var w = why.Length > 120 ? why[..120] : why;
        return new FramesPayload(RunPayload.AppVersion, InstallId.Get(), w, new Dictionary<string, int> { ["w"] = gw, ["h"] = gh }, counted, frames);
    }
}
