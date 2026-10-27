using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;

namespace KinahLedger.Update;

/// <summary>The newest tracker, as kinahledger.com/download/latest.json describes it.</summary>
public sealed record Release(Version Version, string Url, string Sha256, long Size, string NotesUrl);

/// <summary>What one check found. Newer is set only when the newest release is above this version.</summary>
public sealed record CheckResult(bool Ok, Release? Newer, string Note);

/// <summary>An update that didn't go through. NeedsSite: trying again won't help (the folder can't be written), the releases page will.</summary>
public sealed class UpdateException(string message, bool needsSite = false) : Exception(message)
{
    public bool NeedsSite { get; } = needsSite;
}

/// <summary>
/// Keeps the tracker current. The site publishes a small manifest (version, download address, size, SHA-256); the
/// tracker reads it at start and every hour (sooner when the release's small latest.json, read every 10 minutes, names a
/// newer version), downloads a newer exe next to the running one, checks its size and
/// checksum, and swaps it in when the window closes (or at once, on Restart now). Windows lets a running exe be renamed
/// but not overwritten, so the swap is: running exe → .old, verified download → the exe's own name. The .old copy is
/// deleted on the next start. Only the app version goes up with these requests, in the User-Agent.
/// </summary>
public static class Updater
{
    /// <summary>
    /// The newest release on GitHub. A manifest on any other address (kinahledger.com/download/latest.json, a local test
    /// server) works too: the answer is read as a GitHub release when it has a tag_name, as a manifest otherwise.
    /// </summary>
    public const string DefaultManifest = "https://api.github.com/repos/StevenMacchia/kinah-ledger/releases/latest";
    /// <summary>Where a release may be downloaded from, besides the manifest's own host.</summary>
    static readonly string[] Hosts = { "kinahledger.com", "www.kinahledger.com", "github.com" };
    const long MinSize = 1_000_000, MaxSize = 400_000_000;

    public static Version Current => ParseVersion(Upload.RunPayload.AppVersion) ?? new Version(0, 0, 0);

    /// <summary>The exe this process runs from: the file an update replaces. Null when not running as KinahLedger's own exe (dotnet run, tests).</summary>
    public static string? ExePath =>
        Environment.ProcessPath is { } p && p.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
        && !Path.GetFileNameWithoutExtension(p).Equals("dotnet", StringComparison.OrdinalIgnoreCase) ? p : null;

    /// <summary>"0.6.6" or "v0.6.6" as major.minor.patch; null when it isn't a version.</summary>
    public static Version? ParseVersion(string? s)
    {
        s = s?.Trim().TrimStart('v', 'V');
        return Version.TryParse(s, out var v) ? new Version(v.Major, v.Minor, Math.Max(v.Build, 0)) : null;
    }

    /// <summary>Reads and checks a manifest. Null, with the reason, when anything is missing or out of bounds.</summary>
    public static Release? Parse(string json, Uri manifestUrl, out string why)
    {
        why = "";
        try
        {
            using var doc = JsonDocument.Parse(json);
            var r = doc.RootElement;
            if (r.ValueKind != JsonValueKind.Object) { why = "not an object"; return null; }
            string Str(string k) => r.TryGetProperty(k, out var e) && e.ValueKind == JsonValueKind.String ? e.GetString() ?? "" : "";
            if (r.TryGetProperty("tag_name", out _)) return ParseGitHub(r, manifestUrl, out why);

            var version = ParseVersion(Str("version"));
            if (version == null) { why = "no version"; return null; }
            var sha = Str("sha256").Trim().ToLowerInvariant();
            if (sha.Length != 64 || !sha.All(Uri.IsHexDigit)) { why = "no SHA-256"; return null; }
            if (!r.TryGetProperty("size", out var se) || se.ValueKind != JsonValueKind.Number || !se.TryGetInt64(out var size) || size < MinSize || size > MaxSize)
            { why = "no plausible size"; return null; }
            var link = Str("url").Trim();
            if (link.Length == 0 || !Uri.TryCreate(manifestUrl, link, out var url) || !AllowedSource(url, manifestUrl)) { why = "download address missing or not allowed"; return null; }
            var notes = Uri.TryCreate(Str("notes_url"), UriKind.Absolute, out var n) && n.Scheme == Uri.UriSchemeHttps ? n.ToString() : "";
            return new Release(version, url.ToString(), sha, size, notes);
        }
        catch (JsonException) { why = "not JSON"; return null; }
    }

    /// <summary>
    /// A GitHub release: the version from its tag, the KinahLedger.exe asset's address and size, and the SHA-256 from a
    /// "sha256" line in the release notes ("KinahLedger.exe sha256: 3f…"). Drafts, prereleases and a release without the
    /// exe or the checksum line are not offered: nothing is installed that can't be checked.
    /// </summary>
    static Release? ParseGitHub(JsonElement r, Uri source, out string why)
    {
        why = "";
        if (r.TryGetProperty("draft", out var d) && d.ValueKind == JsonValueKind.True) { why = "a draft"; return null; }
        if (r.TryGetProperty("prerelease", out var pre) && pre.ValueKind == JsonValueKind.True) { why = "a prerelease"; return null; }
        string Str(string k) => r.TryGetProperty(k, out var e) && e.ValueKind == JsonValueKind.String ? e.GetString() ?? "" : "";
        var version = ParseVersion(Str("tag_name"));
        if (version == null) { why = "no version in the tag"; return null; }
        if (!r.TryGetProperty("assets", out var assets) || assets.ValueKind != JsonValueKind.Array) { why = "no files"; return null; }
        foreach (var a in assets.EnumerateArray())
        {
            if (!(a.TryGetProperty("name", out var n) && n.ValueKind == JsonValueKind.String && string.Equals(n.GetString(), "KinahLedger.exe", StringComparison.OrdinalIgnoreCase))) continue;
            var link = a.TryGetProperty("browser_download_url", out var u) && u.ValueKind == JsonValueKind.String ? u.GetString() ?? "" : "";
            if (!a.TryGetProperty("size", out var se) || !se.TryGetInt64(out var size) || size < MinSize || size > MaxSize) { why = "no plausible size"; return null; }
            if (!Uri.TryCreate(link, UriKind.Absolute, out var url) || !AllowedSource(url, source)) { why = "download address missing or not allowed"; return null; }
            var sha = Sha256In(Str("body"));
            if (sha == null) { why = "no sha256 line in the release notes"; return null; }
            var notes = Uri.TryCreate(Str("html_url"), UriKind.Absolute, out var h) && h.Scheme == Uri.UriSchemeHttps ? h.ToString() : "";
            return new Release(version, url.ToString(), sha, size, notes);
        }
        why = "no KinahLedger.exe in the release";
        return null;
    }

    /// <summary>A "sha256" line in release notes ("KinahLedger.exe sha256: 3f…"), lower case; null when there is none.</summary>
    public static string? Sha256In(string notes)
    {
        var m = System.Text.RegularExpressions.Regex.Match(notes, @"sha-?256\W{0,6}([0-9a-fA-F]{64})\b", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        return m.Success ? m.Groups[1].Value.ToLowerInvariant() : null;
    }

    /// <summary>
    /// HTTPS from kinahledger.com, GitHub or the manifest's own host. Plain http only on this PC (a local test server).
    /// </summary>
    static bool AllowedSource(Uri url, Uri manifest)
    {
        if (url.Scheme == Uri.UriSchemeHttp) return url.IsLoopback && manifest.IsLoopback;
        if (url.Scheme != Uri.UriSchemeHttps) return false;
        return Hosts.Contains(url.Host, StringComparer.OrdinalIgnoreCase) || url.Host.Equals(manifest.Host, StringComparison.OrdinalIgnoreCase);
    }

    static HttpClient Client(TimeSpan timeout)
    {
        var http = new HttpClient { Timeout = timeout };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("KinahLedger/" + Upload.RunPayload.AppVersion);
        http.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        return http;
    }

    /// <summary>Asks GitHub (or the configured manifest) for the newest version. Never throws: a failed check is a note, and the next one tries again.</summary>
    public static async Task<CheckResult> CheckAsync(string manifestUrl = DefaultManifest, CancellationToken ct = default)
    {
        try
        {
            var uri = new Uri(manifestUrl);
            using var http = Client(TimeSpan.FromSeconds(20));
            using var res = await http.GetAsync(uri, ct).ConfigureAwait(false);
            if (res.StatusCode == HttpStatusCode.NotFound) return new CheckResult(true, null, "No release with automatic updates published yet.");
            if (!res.IsSuccessStatusCode) return new CheckResult(false, null, $"the update check answered HTTP {(int)res.StatusCode}");
            var body = await res.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            var rel = Parse(body, uri, out var why);
            if (rel == null) return new CheckResult(false, null, "the newest release can't be installed automatically (" + why + ")");
            return rel.Version > Current
                ? new CheckResult(true, rel, $"Version {Show(rel.Version)} is available (you have {Show(Current)}).")
                : new CheckResult(true, null, $"You have the latest version ({Show(Current)}).");
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            return new CheckResult(false, null, ex is TaskCanceledException ? "GitHub didn't answer in time" : "couldn't reach GitHub (" + ex.Message + ")");
        }
    }

    /// <summary>
    /// The small file every release carries (latest.json: {"version":"0.8.47", …}) at GitHub's "latest release" download
    /// address. A download, not an API call, so it doesn't count against GitHub's 60 requests an hour per address: read every
    /// 10 minutes, it says when the full check (Velopack, the API) is worth making (0.8.48; Steven, 2026-10-27: 0.8.47 went
    /// out at 16:28 and a tracker started at 9:10 would only have looked at 17:11).
    /// </summary>
    public const string LatestFileUrl = "https://github.com/StevenMacchia/kinah-ledger/releases/latest/download/latest.json";

    /// <summary>The version a latest.json names; null when it names none (an error page, junk).</summary>
    public static Version? ParseLatestFile(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var r = doc.RootElement;
            return r.ValueKind == JsonValueKind.Object && r.TryGetProperty("version", out var v) && v.ValueKind == JsonValueKind.String
                ? ParseVersion(v.GetString()) : null;
        }
        catch (JsonException) { return null; }
    }

    /// <summary>Reads latest.json. Never throws: null when it can't be read, and the hourly full check still runs.</summary>
    public static async Task<Version?> LatestFileVersionAsync(CancellationToken ct = default)
    {
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
            http.DefaultRequestHeaders.UserAgent.ParseAdd("KinahLedger/" + Upload.RunPayload.AppVersion);
            using var res = await http.GetAsync(LatestFileUrl, ct).ConfigureAwait(false);
            if (!res.IsSuccessStatusCode) return null;
            return ParseLatestFile(await res.Content.ReadAsStringAsync(ct).ConfigureAwait(false));
        }
        catch (Exception) { return null; }
    }

    /// <summary>
    /// Whether this 10-minute tick makes the full check: the hourly one is due (<paramref name="sinceFull"/> at least
    /// <paramref name="every"/>), or latest.json names a version above this one that no check has looked for yet
    /// (<paramref name="found"/>: the newest version a check found or was made for).
    /// </summary>
    public static bool FullCheckDue(Version? fileVersion, Version current, Version? found, TimeSpan sinceFull, TimeSpan every) =>
        sinceFull >= every || (fileVersion != null && fileVersion > current && (found == null || fileVersion > found));

    public static string Show(Version v) => $"{v.Major}.{v.Minor}.{Math.Max(v.Build, 0)}";

    public static string PartPath(string exe) => exe + ".part";
    public static string StagedPath(string exe) => exe + ".update";
    public static string OldPath(string exe) => exe + ".old";

    /// <summary>
    /// Downloads the release next to <paramref name="exe"/> (same folder, so the swap is a rename) and checks its size and
    /// SHA-256 while it arrives. Returns the verified file. A mismatch or a folder that can't be written throws UpdateException.
    /// </summary>
    public static async Task<string> DownloadAsync(Release r, string exe, IProgress<double>? progress = null, CancellationToken ct = default)
    {
        var part = PartPath(exe); var staged = StagedPath(exe);
        // already fetched (Update clicked twice, or a check while the first download sat ready)
        if (File.Exists(staged) && new FileInfo(staged).Length == r.Size && await HashAsync(staged, ct).ConfigureAwait(false) == r.Sha256) return staged;

        FileStream dst;
        try { dst = new FileStream(part, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 16, useAsync: true); }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
        {
            throw new UpdateException($"Kinah Ledger can't write to its folder ({Path.GetDirectoryName(exe)}). Move it to a folder of your own, such as Downloads or Desktop, or download the new version from the releases page.", needsSite: true);
        }

        long got = 0;
        string hex;
        try
        {
            await using (dst)
            {
                using var http = Client(TimeSpan.FromMinutes(15));
                using var res = await http.GetAsync(r.Url, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
                if (!res.IsSuccessStatusCode) throw new UpdateException($"the download answered HTTP {(int)res.StatusCode}");
                if (res.Content.Headers.ContentLength is long len && len != r.Size) throw new UpdateException($"the download is {len:N0} bytes, the release says {r.Size:N0}");
                using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                await using var src = await res.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
                var buf = new byte[1 << 16];
                int n, lastPct = -1;
                while ((n = await src.ReadAsync(buf, ct).ConfigureAwait(false)) > 0)
                {
                    got += n;
                    if (got > r.Size) throw new UpdateException("the download is larger than the release says");
                    hash.AppendData(buf, 0, n);
                    await dst.WriteAsync(buf.AsMemory(0, n), ct).ConfigureAwait(false);
                    int pct = (int)(got * 100 / r.Size);
                    if (pct != lastPct) { lastPct = pct; progress?.Report(got / (double)r.Size); }
                }
                hex = Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
            }
        }
        catch (Exception ex)
        {
            TryDelete(part);
            if (ex is UpdateException || (ex is OperationCanceledException && ct.IsCancellationRequested)) throw;
            throw new UpdateException(ex is TaskCanceledException ? "the download timed out" : "the download failed (" + ex.Message + ")");
        }

        if (got != r.Size) { TryDelete(part); throw new UpdateException($"the download stopped at {got:N0} of {r.Size:N0} bytes"); }
        if (hex != r.Sha256) { TryDelete(part); throw new UpdateException("the download didn't match the release's checksum, so it was thrown away"); }
        File.Move(part, staged, overwrite: true);
        return staged;
    }

    static async Task<string> HashAsync(string path, CancellationToken ct)
    {
        await using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 16, useAsync: true);
        return Convert.ToHexString(await SHA256.HashDataAsync(fs, ct).ConfigureAwait(false)).ToLowerInvariant();
    }

    /// <summary>
    /// Puts the verified download in place of <paramref name="exe"/>: the running file is renamed to .old (Windows allows
    /// that for a running exe), the download takes its name. If the second step fails the first is undone.
    /// </summary>
    public static void Apply(string staged, string exe)
    {
        if (!File.Exists(staged)) throw new UpdateException("the downloaded update is gone");
        var old = OldPath(exe);
        TryDelete(old);
        // an .old still held by a process that hasn't exited yet: park this one under its own name
        if (File.Exists(old)) old = $"{exe}.{Guid.NewGuid().ToString("N")[..8]}.old";
        Move(exe, old);
        try { Move(staged, exe); }
        catch
        {
            try { Move(old, exe); } catch (Exception) { /* leave both files; the next start still finds .old */ }
            throw;
        }
    }

    /// <summary>A rename, tried a few times: antivirus often holds a freshly written exe for a moment while it scans it.</summary>
    static void Move(string from, string to)
    {
        for (int attempt = 1; ; attempt++)
        {
            try { File.Move(from, to); return; }
            catch (IOException) when (attempt < 6 && File.Exists(from)) { Thread.Sleep(400); }
            catch (UnauthorizedAccessException) when (attempt < 6 && File.Exists(from)) { Thread.Sleep(400); }
        }
    }

    /// <summary>Starts the (new) exe; the caller then closes. It opens the window as a double-click does.</summary>
    public static void Relaunch(string exe, string args = "") =>
        Process.Start(new ProcessStartInfo(exe, args) { UseShellExecute = false, WorkingDirectory = Path.GetDirectoryName(exe) ?? "" });

    /// <summary>
    /// Removes what an update leaves behind: the previous exe (.old), a half download (.part) and a download that was never
    /// applied (.update; a newer check fetches it again). A just-replaced exe may still be closing, so this retries for a while.
    /// </summary>
    public static async Task CleanupAsync(string exe)
    {
        var dir = Path.GetDirectoryName(exe); var name = Path.GetFileName(exe);
        if (dir == null) return;
        for (int attempt = 0; attempt < 10; attempt++)
        {
            bool left = false;
            try
            {
                foreach (var f in Directory.EnumerateFiles(dir, name + ".*"))
                {
                    var tail = f[(Path.Combine(dir, name).Length)..];
                    if (tail != ".part" && tail != ".update" && !tail.EndsWith(".old", StringComparison.OrdinalIgnoreCase)) continue;
                    if (!TryDelete(f)) left = true;
                }
            }
            catch (Exception) { return; }   // the folder can't be listed: nothing an update could have left either
            if (!left) return;
            await Task.Delay(3000).ConfigureAwait(false);
        }
    }

    static bool TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); return true; }
        catch (Exception) { return false; }
    }
}
