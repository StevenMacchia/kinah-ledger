using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace KinahLedger.Upload;

/// <summary>
/// The heartbeat (1.0.41, Steven 2026-11-20): while the tracker is open and sharing is on, every five minutes it tells the
/// site it is open, so the owner can see which trackers are running, idle ones too (runs and batches are only sent while
/// something is counted). What goes: the install ID (the same one runs carry, hashed by the site), the app version, the
/// character counted for now (its number here, "c1") and whether the game is open: "reading" (its chat is read), "game"
/// (open, chat not found yet) or "idle" (the tracker open, the game not).
///
/// The Share switch turns it off (checked before every beat), as does a blank upload.endpoint and the site's
/// "heartbeat" kill switch (/v1/config). A site without the route, or no connection, changes nothing: the beat is
/// dropped and the next one goes on time. The site's answer may stretch the wait ("every", in seconds).
/// </summary>
public static class Heartbeat
{
    /// <summary>The wait between beats, unless the site says otherwise.</summary>
    public static readonly TimeSpan Every = TimeSpan.FromMinutes(5);
    /// <summary>The first beat goes this long after the window starts, so a tracker shows up soon after it opens.</summary>
    public static readonly TimeSpan FirstAfter = TimeSpan.FromSeconds(20);
    /// <summary>The wait the site may ask for is kept between these.</summary>
    public static readonly TimeSpan Shortest = TimeSpan.FromMinutes(2), Longest = TimeSpan.FromHours(1);

    /// <summary>The state a beat names: the game open and its chat read, the game open, or no game.</summary>
    public static string StateOf(bool gameRunning, bool chatRead) => gameRunning ? (chatRead ? "reading" : "game") : "idle";

    /// <summary>Where beats go: beside /v1/runs; "" when upload.endpoint doesn't end in /v1/runs.</summary>
    public static string Url(UploadSettings u) => AccuracyClient.Url(u, "beat");

    /// <summary>The beat to send now, or null when none may go (sharing off, no site address, the site's kill switch).</summary>
    public static HeartbeatPayload? Payload(UploadSettings u, string state, string? character)
    {
        if (!u.Enabled || Url(u).Length == 0 || Update.RemoteConfig.Current.Off("heartbeat")) return null;
        return new HeartbeatPayload(InstallId.Get(), RunPayload.AppVersion, state, string.IsNullOrEmpty(character) ? null : character);
    }

    /// <summary>The wait the site's answer asks for ("every", seconds), kept between Shortest and Longest; null when it names none.</summary>
    public static TimeSpan? WaitFrom(string body)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.ValueKind == JsonValueKind.Object && doc.RootElement.TryGetProperty("every", out var e) && e.TryGetDouble(out var s) && double.IsFinite(s))
                return TimeSpan.FromSeconds(Math.Clamp(s, Shortest.TotalSeconds, Longest.TotalSeconds));
        }
        catch (JsonException) { }
        return null;
    }

    static readonly Lazy<HttpClient> Http = new(() =>
    {
        var http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("KinahLedger/" + RunPayload.AppVersion);
        return http;
    });

    static bool _failNoted;

    /// <summary>
    /// Sends one beat. Returns whether the site took it and how long to wait before the next (Every unless the site says
    /// otherwise). Nothing is sent when Payload is null. Never throws.
    /// </summary>
    public static async Task<(bool Sent, TimeSpan Next)> SendAsync(UploadSettings u, string state, string? character, HttpClient? http = null)
    {
        var body = Payload(u, state, character);
        if (body == null) return (false, Every);
        try
        {
            using var r = await (http ?? Http.Value).PostAsJsonAsync(Url(u), body);
            if (!r.IsSuccessStatusCode)
            {
                // one line a run, so a site without the route doesn't fill the log every five minutes
                if (!_failNoted) { _failNoted = true; CrashLog.Note($"heartbeat not taken ({(int)r.StatusCode}); trying again every {Every.TotalMinutes:0} min"); }
                return (false, Every);
            }
            return (true, WaitFrom(await r.Content.ReadAsStringAsync()) ?? Every);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or InvalidOperationException)
        {
            if (!_failNoted) { _failNoted = true; CrashLog.Note($"heartbeat not sent ({ex.GetType().Name}); trying again every {Every.TotalMinutes:0} min"); }
            return (false, Every);
        }
    }

    static CancellationTokenSource? _loop;

    /// <summary>
    /// Starts the beats for this run of the app (the watching window only; a render or self-test never calls it).
    /// <paramref name="now"/> says the state and the character at the moment of each beat; the Share switch and the
    /// endpoint are read again each time, so turning sharing off stops the beats from the next one.
    /// </summary>
    public static void Start(UploadSettings u, Func<(string State, string? Character)> now)
    {
        if (_loop != null) return;
        var cts = _loop = new CancellationTokenSource();
        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(FirstAfter, cts.Token);
                while (!cts.IsCancellationRequested)
                {
                    var wait = Every;
                    try
                    {
                        var (state, character) = now();
                        wait = (await SendAsync(u, state, character)).Next;
                    }
                    catch (Exception ex) { CrashLog.Write("heartbeat", ex); }
                    await Task.Delay(wait, cts.Token);
                }
            }
            catch (OperationCanceledException) { }
        });
    }

    /// <summary>Stops the beats (the app closing).</summary>
    public static void Stop()
    {
        _loop?.Cancel();
        _loop = null;
    }
}

/// <summary>One beat as the site reads it (POST /v1/beat).</summary>
public sealed record HeartbeatPayload(
    string install,
    string app,
    string state,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? character = null);
