using System.Drawing;
using System.IO.Compression;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using KinahLedger.Session;

namespace KinahLedger.Upload;

/// <summary>
/// Sends "Help improve counting" (Session/LabelWriter.cs): the confirmed reads as a gzipped JSON-lines batch to
/// POST /v1/labels, then the small crops kept with them to POST /v1/label-crops with their pair's id. The first line of a
/// batch says who sends it (the app version and the same install ID runs carry, never the link code); each next line is
/// one pair. Every address comes from upload.endpoint, so blanking it in settings.json stops this too, as do sharing off,
/// the setting off, and the remote switch "labels". A site that doesn't answer keeps the batch for the next try.
/// </summary>
public static class LabelClient
{
    public const int MaxPairs = 500;
    public const int CropsPerPost = 20;
    public const long MaxCropPostBytes = 900_000;

    /// <summary>The first line of a batch.</summary>
    public sealed record BatchHead(string app, string install, int pairs);
    /// <summary>One crop as sent: its pair, the pair's kind, which part (name, icon, slot), the PNG.</summary>
    public sealed record CropPayload(string pair, string kind, string part, string png);
    public sealed record CropsPayload(string app, string install, List<CropPayload> crops);

    /// <summary>The batch as sent: gzip of the head line and one line per pair.</summary>
    public static byte[] BuildBatch(string app, string install, IReadOnlyList<LabelPair> pairs)
    {
        var sb = new StringBuilder();
        sb.Append(JsonSerializer.Serialize(new BatchHead(app, install, pairs.Count))).Append('\n');
        foreach (var p in pairs.Take(MaxPairs)) sb.Append(JsonSerializer.Serialize(p)).Append('\n');
        using var ms = new MemoryStream();
        using (var gz = new GZipStream(ms, CompressionLevel.Optimal, leaveOpen: true))
        {
            var bytes = Encoding.UTF8.GetBytes(sb.ToString());
            gz.Write(bytes, 0, bytes.Length);
        }
        return ms.ToArray();
    }

    /// <summary>A batch read back (parse-test checks the shape the site gets).</summary>
    public static (BatchHead? Head, List<LabelPair> Pairs) ReadBatch(byte[] gz)
    {
        using var ms = new MemoryStream(gz);
        using var z = new GZipStream(ms, CompressionMode.Decompress);
        using var r = new StreamReader(z, Encoding.UTF8);
        var head = r.ReadLine() is { } h ? JsonSerializer.Deserialize<BatchHead>(h) : null;
        var pairs = new List<LabelPair>();
        string? line;
        while ((line = r.ReadLine()) != null) if (line.Length > 0 && JsonSerializer.Deserialize<LabelPair>(line) is { } p) pairs.Add(p);
        return (head, pairs);
    }

    static HttpClient Client(TimeSpan timeout)
    {
        var http = new HttpClient { Timeout = timeout };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("KinahLedger/" + RunPayload.AppVersion);
        return http;
    }

    /// <summary>The name decoder's read of a pair's name crop, when the decoder is already loaded (never loads it for this).</summary>
    static string? DecodeName(LabelWriter w, LabelPair p)
    {
        if (Ocr.NameDecoder.State != "on" || !p.Crops.Contains("name") || w.CropPath(p.Id, "name") is not { } path) return null;
        try
        {
            using var bmp = new Bitmap(path);
            var r = Ocr.NameDecoder.Decode(bmp);
            return r == null ? null : r.Kind == Ocr.DecodeKind.Item ? r.Name : r.Kind == Ocr.DecodeKind.Other ? "other" : null;
        }
        catch (Exception) { return null; }
    }

    /// <summary>One send: the next batch, then its crops. True when a batch went up.</summary>
    public static async Task<bool> SendAsync(UploadSettings u, LabelWriter w)
    {
        if (!w.Enabled) return false;
        var url = AccuracyClient.Url(u, "labels");
        var cropUrl = AccuracyClient.Url(u, "label-crops");
        if (!u.Enabled || url.Length == 0 || Update.RemoteConfig.Current.Off("labels")) return false;
        var batch = w.TakeBatch(MaxPairs);
        if (batch.Count == 0) return false;
        batch = batch.Select(p => p.Decoded == null && DecodeName(w, p) is { } d ? p with { Decoded = d } : p).ToList();
        var install = InstallId.Get();
        try
        {
            using var http = Client(TimeSpan.FromSeconds(30));
            using var body = new ByteArrayContent(BuildBatch(RunPayload.AppVersion, install, batch));
            body.Headers.ContentType = new MediaTypeHeaderValue("application/x-ndjson");
            body.Headers.ContentEncoding.Add("gzip");
            using var r = await http.PostAsync(url, body);
            if (!r.IsSuccessStatusCode)
            {
                // a shape the site refuses would be refused again: let go. Anything else waits for the next try
                if ((int)r.StatusCode is 400 or 404 or 413 or 415) w.BatchDone(keepCrops: false);
                CrashLog.Note($"help improve counting: the site answered {(int)r.StatusCode} for {batch.Count} pair(s)");
                return false;
            }
            if (cropUrl.Length > 0) await SendCropsAsync(http, cropUrl, install, w, batch);
            w.BatchDone(keepCrops: false);
            return true;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException) { return false; }
    }

    static async Task SendCropsAsync(HttpClient http, string url, string install, LabelWriter w, List<LabelPair> batch)
    {
        var chunk = new List<CropPayload>(); long bytes = 0;
        async Task Flush()
        {
            if (chunk.Count == 0) return;
            try { using var r = await http.PostAsJsonAsync(url, new CropsPayload(RunPayload.AppVersion, install, chunk)); }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException) { /* crops are best effort: the pairs went up */ }
            chunk = new(); bytes = 0;
        }
        foreach (var p in batch)
            foreach (var part in p.Crops)
            {
                if (w.CropPath(p.Id, part) is not { } path) continue;
                var png = File.ReadAllBytes(path);
                if (bytes + png.Length > MaxCropPostBytes || chunk.Count >= CropsPerPost) await Flush();
                chunk.Add(new CropPayload(p.Id, p.Kind, part, Convert.ToBase64String(png)));
                bytes += png.Length;
            }
        await Flush();
    }

    static CancellationTokenSource? _loop;
    static readonly object LoopLock = new();

    /// <summary>
    /// Sends now and then while the window is open: two minutes after the start, then every ten. Does nothing while the
    /// writer is off. Called once by the window; calling again keeps the one loop.
    /// </summary>
    public static void Start(Func<UploadSettings> settings, LabelWriter w)
    {
        lock (LoopLock)
        {
            if (_loop != null) return;
            _loop = new CancellationTokenSource();
            var ct = _loop.Token;
            _ = Task.Run(async () =>
            {
                try
                {
                    await Task.Delay(TimeSpan.FromMinutes(2), ct);
                    while (!ct.IsCancellationRequested)
                    {
                        try { if (w.Enabled) while (await SendAsync(settings(), w)) { } }
                        catch (Exception ex) { CrashLog.Write("help improve counting", ex); }
                        await Task.Delay(TimeSpan.FromMinutes(10), ct);
                    }
                }
                catch (TaskCanceledException) { }
            }, ct);
        }
    }
}
