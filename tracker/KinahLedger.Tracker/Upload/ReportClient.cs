using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using KinahLedger.Session;

namespace KinahLedger.Upload;

/// <summary>
/// "Report a miscount" sent to the site (0.9.0). Steven, 2026-10-30: "just 'Report a miscount' should automatically send
/// the logs to a table somewhere that we can diagnose whenever like --- 25 to 50 come in". Until now a report was a folder
/// on the player's PC that they had to zip and post on Discord, which almost nobody did. Now the folder is still written
/// as before (TrackerSession.WriteReport), and its text parts go to POST {site}/v1/reports in the background.
///
/// The contract (the site's ingest is built against exactly this): plain JSON, as /v1/runs is sent (no gzip),
/// {"v":1, "app", "install" (the same random install id /v1/runs carries), "why" (≤500 chars), "flagged" (≤300 chars),
/// "server" (the server key /v1/runs files runs under, or ""), "game":{"w","h"}, "summary", "counted", "trace", "log"},
/// at most <see cref="MaxBodyBytes"/> as UTF-8; the log loses its oldest lines first to fit. The site answers 202 {"id"}.
///
/// Privacy (Steven is a Trust and Safety lead; this is required, not polish): the chat the tracker reads shows other
/// players' names and messages. Before anything is sent, <see cref="FilterLog"/> and <see cref="FilterTrace"/> keep only
/// what the tracker itself wrote and the game's own system lines, and drop every line that is another player's chat, wraps
/// on from one, names a player in brackets, or is another player's share of Force loot. No pictures go with this, ever:
/// the picture route (/v1/frames, Options › Sharing › Send report pictures) is separate and unchanged.
///
/// Never blocks: the body is built and sent off the UI thread, and the reader loop is not involved at all. A report the
/// site can't take yet (no connection, 404 while the route isn't deployed, 408, 429, 5xx) waits on disk in report-queue/
/// beside the logs, at most <see cref="MaxQueued"/> for <see cref="QueuedDays"/> days, and is offered again oldest first on
/// the next start or report, no sooner than the backoff (doubling from a minute to six hours, or the site's Retry-After).
/// Any other 4xx means the site will never take that body, so it is dropped and reads.log says why.
/// </summary>
public static class ReportClient
{
    public const int MaxBodyBytes = 1_000_000;
    public const int MaxWhy = 500, MaxFlagged = 300;
    public const int MaxQueued = 30, QueuedDays = 7;
    static readonly TimeSpan FirstBackoff = TimeSpan.FromMinutes(1), MaxBackoff = TimeSpan.FromHours(6);

    /// <summary>How a report went: sent now, waiting on disk for the site, or not sent at all (sending off, or refused).</summary>
    public enum Outcome { Sent, Queued, Off, Dropped }

    // ---------------- the privacy filter ----------------

    /// <summary>
    /// The game's own system lines that a miscount is diagnosed from: pickups (Acquired/Obtained … Kina), deliveries, kills,
    /// gathering and crafting results, the inventory's own lines and rewards. These are the words ChatParser and PickupCheck
    /// decide on (ChatParser.LootKey's acqui/obtain/kina, PickupCheck's inventory verbs, the gather and craft outcomes); a
    /// read that has none of them is not something the counter acted on, and may be someone talking, so it stays home.
    /// </summary>
    static readonly Regex SystemWords = new(
        @"acqu|obtain|kin[ae]h?\b|deliver|defeat|\bkill|gather|extract|harvest|\bfail|succe|craft|enchant|reward|experience|\bexp\b|\bxp\b|"
        + @"stor(?:ed|age)|regist|retriev|withdr[ae]w|dismantl|salvag|exchang|purchas|\bsold\b|\bsell|consum|expired|level|quest|\bentered\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>A player's name in brackets, as the chat prints a speaker or a whisper ("[Okay]", "[Bob Smith]"); any line with one is dropped.</summary>
    static readonly Regex BracketName = new(@"\[[^\[\]\d]{2,24}\]", RegexOptions.Compiled);

    /// <summary>
    /// A speaker in front of a message whose bracket the reader lost ("Bob: wts kinah", "Bob] lol"): a short first word
    /// then a colon or a closing bracket. The game's own "Acquired Artwork: …" starts with its verb and is not one.
    /// </summary>
    static readonly Regex Speaker = new(@"^\W{0,3}(?!acqu|obtain)[^\s:\]]{2,16}\s?[:\]]\s", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>
    /// The chat lines of one read, in order, each kept or not: a line that is another player's text (PickupCheck.PlayerText,
    /// the counter's own test, which also catches the lines a long message wraps onto), another player's Force loot
    /// (ChatParser.SomeoneElses: "Yarie acquired …" names them), or carries a bracketed name is dropped; of the rest, only
    /// lines with the game's system words are kept. <paramref name="parsed"/> marks lines the parser already took as game
    /// lines (reads.log's "ok"): those skip the system-word test but never the player tests.
    /// </summary>
    public static bool[] KeepChat(IReadOnlyList<string> lines, IReadOnlyList<bool>? parsed = null)
    {
        var player = PickupCheck.PlayerText(lines);
        var keep = new bool[lines.Count];
        for (int i = 0; i < lines.Count; i++)
        {
            var l = lines[i];
            bool isParsed = parsed != null && parsed[i];
            // a line the parser took ("ok") skips the lost-bracket test: "Artwork: Furious Saursus acquired." has the same shape
            if (player[i] || BracketName.IsMatch(l) || (!isParsed && Speaker.IsMatch(l)) || Ocr.ChatParser.SomeoneElses(l)) continue;
            keep[i] = isParsed || SystemWords.IsMatch(l);
        }
        return keep;
    }

    /// <summary>reads.log's chat lines: "HH:mm:ss.f  ok  text" (parsed) and "HH:mm:ss.f  --  text" (read, not parsed).</summary>
    static readonly Regex LogLine = new(@"^(\S+)\s{2}(ok|--)\s{2}(.*)$", RegexOptions.Compiled);

    /// <summary>
    /// reads.log as it may leave the PC. The tracker's own notes ("??" and any other line not in the chat-line shape) stay,
    /// unless one quotes a bracketed name. The chat lines ("ok" and "--") go through <see cref="KeepChat"/> in order, so a
    /// message that wraps over several reads.log lines is dropped whole.
    /// </summary>
    public static List<string> FilterLog(IReadOnlyList<string> lines)
    {
        var chatIdx = new List<int>(); var chatText = new List<string>(); var chatParsed = new List<bool>();
        for (int i = 0; i < lines.Count; i++)
            if (LogLine.Match(lines[i]) is { Success: true } m)
            {
                chatIdx.Add(i);
                chatText.Add(m.Groups[3].Value.Replace("    (after a fade)", ""));
                chatParsed.Add(m.Groups[2].Value == "ok");
            }
        var keepChat = KeepChat(chatText, chatParsed);
        var drop = new HashSet<int>();
        for (int j = 0; j < chatIdx.Count; j++) if (!keepChat[j]) drop.Add(chatIdx[j]);
        var outp = new List<string>(lines.Count);
        for (int i = 0; i < lines.Count; i++)
            if (!drop.Contains(i) && !BracketName.IsMatch(lines[i])) outp.Add(lines[i]);
        return outp;
    }

    /// <summary>
    /// trace.jsonl as it may leave the PC: each "chat" read's "l" lines through <see cref="KeepChat"/> (a read left with no
    /// lines still goes, empty, so the timing survives); loot list reads and the zone keep their lines, less any bracketed
    /// name. A line that isn't JSON is dropped.
    /// </summary>
    public static List<string> FilterTrace(IEnumerable<string> lines)
    {
        var outp = new List<string>();
        foreach (var line in lines)
        {
            JsonNode? n;
            try { n = JsonNode.Parse(line); } catch (JsonException) { continue; }
            if (n is not JsonObject o) continue;
            if (o["l"] is JsonArray arr)
            {
                var text = arr.Select(x => x?.GetValueKind() == JsonValueKind.String ? x.GetValue<string>() : "").ToList();
                bool chat = o["k"]?.GetValueKind() == JsonValueKind.String && o["k"]!.GetValue<string>() == "chat";
                var keep = chat ? KeepChat(text) : text.Select(t => !BracketName.IsMatch(t)).ToArray();
                var kept = new JsonArray();
                for (int i = 0; i < text.Count; i++) if (keep[i]) kept.Add(text[i]);
                o["l"] = kept;
            }
            outp.Add(o.ToJsonString());
        }
        return outp;
    }

    // ---------------- the body ----------------

    /// <summary>
    /// The JSON body for one saved report folder, filtered, within <see cref="MaxBodyBytes"/>. <paramref name="why"/> is the
    /// player's words or the check they flagged; <paramref name="flagged"/> the flagged check's line ("" for the button).
    /// </summary>
    public static string BuildBody(string reportDir, string why, string flagged, string install, string server, string? app = null)
    {
        string Read(string f) { try { var p = Path.Combine(reportDir, f); return File.Exists(p) ? File.ReadAllText(p) : ""; } catch (Exception) { return ""; } }
        List<string> Lines(string f) { try { var p = Path.Combine(reportDir, f); return File.Exists(p) ? File.ReadAllLines(p).ToList() : new(); } catch (Exception) { return new(); } }
        int w = 0, h = 0;
        try
        {
            var g = Read("game.json");
            if (g.Length > 0) { using var d = JsonDocument.Parse(g); w = d.RootElement.GetProperty("w").GetInt32(); h = d.RootElement.GetProperty("h").GetInt32(); }
        }
        catch (Exception) { /* no window size: 0 x 0 */ }
        // the summary's own lines (zone, target bar, settings) are the tracker's; a bracketed name in one is still dropped
        var summary = string.Join("\n", Read("summary.txt").Replace("\r", "").Split('\n').Where(l => !BracketName.IsMatch(l)));
        return Fit(app ?? RunPayload.AppVersion, install, Cut(why, MaxWhy), Cut(flagged, MaxFlagged), server, w, h, summary,
            Read("counted.jsonl").Replace("\r", ""), FilterTrace(Lines("trace.jsonl")), FilterLog(Lines("reads-log.txt")));
    }

    static string Cut(string s, int n) => (s ?? "").Length > n ? s![..n] : s ?? "";

    static string Serialize(string app, string install, string why, string flagged, string server, int w, int h, string summary, string counted, IEnumerable<string> trace, IEnumerable<string> log) =>
        JsonSerializer.Serialize(new
        {
            v = 1, app, install, why, flagged, server,
            game = new { w, h },
            summary, counted,
            trace = string.Join("\n", trace),
            log = string.Join("\n", log),
        });

    /// <summary>
    /// The body within <see cref="MaxBodyBytes"/>: the log's oldest lines go first (the moment of the miscount is at its end),
    /// then, should the rest still not fit, the trace's oldest reads, then the counted pickups', then the summary is cut.
    /// </summary>
    internal static string Fit(string app, string install, string why, string flagged, string server, int w, int h, string summary, string counted, List<string> trace, List<string> log)
    {
        var countedL = counted.Split('\n', StringSplitOptions.RemoveEmptyEntries).ToList();
        for (int guard = 0; guard < 200; guard++)
        {
            var body = Serialize(app, install, why, flagged, server, w, h, summary, string.Join("\n", countedL), trace, log);
            int size = Encoding.UTF8.GetByteCount(body);
            if (size <= MaxBodyBytes) return body;
            long over = size - MaxBodyBytes;
            // drop at least what is over (escaping can make a line weigh more in JSON than on disk, so measure as written)
            List<string>? from = log.Count > 0 ? log : trace.Count > 0 ? trace : countedL.Count > 0 ? countedL : null;
            if (from == null) { summary = summary.Length > 1000 ? summary[..(summary.Length / 2)] : ""; continue; }
            long removed = 0; int k = 0;
            while (k < from.Count && removed < over) { removed += Encoding.UTF8.GetByteCount(JsonEncodedText.Encode(from[k]).ToString()) + 2; k++; }
            from.RemoveRange(0, Math.Max(1, k));
        }
        return Serialize(app, install, why, flagged, server, w, h, "", "", Array.Empty<string>(), Array.Empty<string>());
    }

    // ---------------- the queue and the send ----------------

    static string QueueDir => Path.Combine(CrashLog.Dir, "report-queue");
    static string StatePath => Path.Combine(QueueDir, "backoff.json");
    static readonly SemaphoreSlim Gate = new(1, 1);

    /// <summary>When the next try may be, and how many tries in a row failed (backoff.json).</summary>
    internal sealed record Backoff(DateTime NextUtc, int Failures);

    static Backoff LoadBackoff()
    {
        try { return File.Exists(StatePath) ? JsonSerializer.Deserialize<Backoff>(File.ReadAllText(StatePath)) ?? new(DateTime.MinValue, 0) : new(DateTime.MinValue, 0); }
        catch (Exception) { return new(DateTime.MinValue, 0); }
    }

    static void SaveBackoff(Backoff b)
    {
        try { if (b.Failures == 0) File.Delete(StatePath); else File.WriteAllText(StatePath, JsonSerializer.Serialize(b)); }
        catch (Exception) { /* tried again sooner: harmless */ }
    }

    /// <summary>The reports waiting, oldest first, after letting go of those past <see cref="QueuedDays"/> and over <see cref="MaxQueued"/>.</summary>
    static List<string> Queued()
    {
        try
        {
            if (!Directory.Exists(QueueDir)) return new();
            var files = Directory.GetFiles(QueueDir, "r-*.json").OrderBy(f => f, StringComparer.Ordinal).ToList();
            foreach (var f in files.Where(f => (DateTime.UtcNow - File.GetLastWriteTimeUtc(f)).TotalDays >= QueuedDays).ToList())
            { TryDelete(f); files.Remove(f); CrashLog.Note($"miscount report upload: {Path.GetFileName(f)} waited {QueuedDays} days; let go"); }
            while (files.Count > MaxQueued) { TryDelete(files[0]); CrashLog.Note($"miscount report upload: more than {MaxQueued} waiting; the oldest let go"); files.RemoveAt(0); }
            return files;
        }
        catch (Exception) { return new(); }
    }

    static void TryDelete(string f) { try { File.Delete(f); } catch (Exception) { } }

    /// <summary>How many reports wait for the site (tests, the log).</summary>
    public static int QueuedCount() => Queued().Count;

    /// <summary>The address: upload.endpoint's /v1/runs with "reports" in place of "runs"; "" when sending is off.</summary>
    public static string Url(UploadSettings u) =>
        !u.Enabled || Update.RemoteConfig.Current.Off("report_upload") ? "" : AccuracyClient.Url(u, "reports");

    /// <summary>
    /// Builds the body of a saved report folder, puts it in the queue, and offers the queue to the site. Run it off the UI
    /// thread (it reads the folder). Returns how this report went.
    /// </summary>
    public static async Task<Outcome> SendAsync(UploadSettings u, string reportDir, string why, string flagged, string server)
    {
        if (Url(u).Length == 0) return Outcome.Off;
        string body;
        try { body = BuildBody(reportDir, why, flagged, InstallId.Get(), server); }
        catch (Exception ex) { CrashLog.Write("miscount report upload", ex); return Outcome.Dropped; }
        string file;
        try
        {
            Directory.CreateDirectory(QueueDir);
            file = Path.Combine(QueueDir, $"r-{DateTime.UtcNow:yyyyMMddHHmmssfff}-{Guid.NewGuid().ToString("N")[..8]}.json");
            await File.WriteAllTextAsync(file, body);
        }
        catch (Exception ex) { CrashLog.Write("miscount report upload", ex); return Outcome.Dropped; }
        var results = await FlushAsync(u);
        return results.TryGetValue(file, out var o) ? o : File.Exists(file) ? Outcome.Queued : Outcome.Dropped;
    }

    /// <summary>
    /// Offers the waiting reports to the site, oldest first, unless the backoff says not yet; stops at the first the site
    /// can't take now. Called at start and with each new report. Returns how each file it tried went.
    /// </summary>
    public static async Task<Dictionary<string, Outcome>> FlushAsync(UploadSettings u)
    {
        var done = new Dictionary<string, Outcome>();
        var url = Url(u);
        if (url.Length == 0) return done;
        await Gate.WaitAsync();
        try
        {
            var queue = Queued();
            if (queue.Count == 0) { SaveBackoff(new(DateTime.MinValue, 0)); return done; }
            var b = LoadBackoff();
            if (DateTime.UtcNow < b.NextUtc) return done;
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
            http.DefaultRequestHeaders.UserAgent.ParseAdd("KinahLedger/" + RunPayload.AppVersion);
            foreach (var f in queue)
            {
                string body;
                try { body = await File.ReadAllTextAsync(f); } catch (Exception) { TryDelete(f); continue; }
                int status = 0; TimeSpan? retry = null; string answer = "";
                try
                {
                    using var content = new StringContent(body, Encoding.UTF8, "application/json");
                    using var r = await http.PostAsync(url, content);
                    status = (int)r.StatusCode; retry = Uploader.RetryAfterOf(r);
                    try { answer = (await r.Content.ReadAsStringAsync()).Trim(); } catch (Exception) { }
                }
                catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException) { status = 0; }
                if (status is >= 200 and < 300)
                {
                    TryDelete(f); done[f] = Outcome.Sent; b = new(DateTime.MinValue, 0);
                    CrashLog.Note($"miscount report upload: sent ({status}{(IdOf(answer) is { } id ? ", id " + id : "")})");
                    continue;
                }
                if (status is >= 400 and < 500 and not 404 and not 408 and not 429)
                {
                    // the site will never take this body: keep it from blocking the rest
                    TryDelete(f); done[f] = Outcome.Dropped;
                    CrashLog.Note($"miscount report upload: the site answered {status}; dropped" + (answer.Length > 0 ? ": " + (answer.Length > 140 ? answer[..140] : answer) : ""));
                    continue;
                }
                // not now: no connection, the route not deployed yet (404), a timeout, too many, a server error
                done[f] = Outcome.Queued;
                int fails = b.Failures + 1;
                var wait = retry ?? TimeSpan.FromTicks(Math.Min(MaxBackoff.Ticks, FirstBackoff.Ticks << Math.Min(fails - 1, 20)));
                b = new(DateTime.UtcNow + wait, fails);
                CrashLog.NoteBurst($"miscount report upload: {(status == 0 ? "couldn't reach the site" : $"the site answered {status}")}; {queue.Count - done.Count(d => d.Value != Outcome.Queued)} waiting, next try after {wait.TotalMinutes:0} min");
                break;
            }
            SaveBackoff(b);
            return done;
        }
        finally { Gate.Release(); }
    }

    static string? IdOf(string answer)
    {
        try { using var d = JsonDocument.Parse(answer); return d.RootElement.TryGetProperty("id", out var id) ? id.ToString() : null; }
        catch (Exception) { return null; }
    }

    /// <summary>For tests: forget the backoff so the next flush tries at once.</summary>
    internal static void ResetBackoff() => SaveBackoff(new(DateTime.MinValue, 0));
}
