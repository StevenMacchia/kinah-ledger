using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace KinahLedger.Upload;

/// <summary>
/// Updates from inside the app. On launch (when the player leaves the switch on) the latest release of
/// StevenMacchia/kinah-ledger on GitHub is looked up; a newer one shows a line in the window. Only the player's click
/// downloads it: the release's KinahLedger.exe goes next to the running exe as KinahLedger.exe.new, its size is checked
/// against the release (and its SHA-256, when the release notes give one), and a small PowerShell step waits for this
/// app to close, puts the new exe in place and starts it. settings.json and everything under %LOCALAPPDATA%\KinahLedger
/// stay as they are.
/// </summary>
public static class Updater
{
    public const string Repo = "StevenMacchia/kinah-ledger";
    public const string LatestApi = "https://api.github.com/repos/" + Repo + "/releases/latest";
    public const string ReleasesPage = "https://github.com/" + Repo + "/releases/latest";

    /// <summary>A newer release: its version, the exe asset's link and size, the SHA-256 the notes give (or null), and its page.</summary>
    public sealed record Release(Version Version, string Tag, string AssetUrl, long Size, string? Sha256, string Page);

    /// <summary>The release when it is newer than this app, null when it isn't, or when GitHub can't be reached (silently).</summary>
    public static async Task<Release?> CheckAsync()
    {
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
            http.DefaultRequestHeaders.UserAgent.ParseAdd("KinahLedger/" + RunPayload.AppVersion);
            http.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
            var json = await http.GetStringAsync(LatestApi);
            return Parse(json, Current);
        }
        catch (Exception) { return null; }
    }

    public static Version Current => Version.TryParse(RunPayload.AppVersion, out var v) ? v : new Version(0, 0, 0);

    /// <summary>The newer release in a GitHub "latest release" answer, or null. Drafts and prereleases never count; the exe asset must be there.</summary>
    public static Release? Parse(string json, Version current)
    {
        using var doc = JsonDocument.Parse(json);
        var r = doc.RootElement;
        if (r.TryGetProperty("draft", out var d) && d.ValueKind == JsonValueKind.True) return null;
        if (r.TryGetProperty("prerelease", out var p) && p.ValueKind == JsonValueKind.True) return null;
        var tag = r.TryGetProperty("tag_name", out var t) ? t.GetString() ?? "" : "";
        var m = Regex.Match(tag, @"(\d+)\.(\d+)(?:\.(\d+))?");
        if (!m.Success) return null;
        var v = new Version(int.Parse(m.Groups[1].Value), int.Parse(m.Groups[2].Value), m.Groups[3].Success ? int.Parse(m.Groups[3].Value) : 0);
        if (v <= current) return null;
        if (!r.TryGetProperty("assets", out var assets) || assets.ValueKind != JsonValueKind.Array) return null;
        foreach (var a in assets.EnumerateArray())
        {
            var name = a.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "";
            if (!name.Equals("KinahLedger.exe", StringComparison.OrdinalIgnoreCase)) continue;
            var url = a.TryGetProperty("browser_download_url", out var u) ? u.GetString() ?? "" : "";
            long size = a.TryGetProperty("size", out var s) && s.TryGetInt64(out var sz) ? sz : 0;
            if (!url.StartsWith("https://github.com/" + Repo + "/releases/download/", StringComparison.OrdinalIgnoreCase) || size <= 0) return null;
            var body = r.TryGetProperty("body", out var b) ? b.GetString() ?? "" : "";
            var page = r.TryGetProperty("html_url", out var h) ? h.GetString() ?? ReleasesPage : ReleasesPage;
            return new Release(v, tag, url, size, Sha256In(body), page);
        }
        return null;
    }

    /// <summary>A "sha256" line in the release notes ("KinahLedger.exe sha256: 3f…"), or null when there is none.</summary>
    public static string? Sha256In(string notes)
    {
        var m = Regex.Match(notes, @"sha-?256\W{0,6}([0-9a-fA-F]{64})\b", RegexOptions.IgnoreCase);
        return m.Success ? m.Groups[1].Value.ToLowerInvariant() : null;
    }

    /// <summary>
    /// Downloads the release next to the running exe and checks it. Returns the path of the checked file; throws with a
    /// plain message when the download is short, the hash doesn't match, or the folder can't be written.
    /// </summary>
    public static async Task<string> DownloadAsync(Release rel, IProgress<double>? progress = null)
    {
        var exe = Environment.ProcessPath ?? throw new InvalidOperationException("the app's own file couldn't be found");
        var target = Path.Combine(Path.GetDirectoryName(exe)!, "KinahLedger.exe.new");
        using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("KinahLedger/" + RunPayload.AppVersion);
        using var resp = await http.GetAsync(rel.AssetUrl, HttpCompletionOption.ResponseHeadersRead);
        resp.EnsureSuccessStatusCode();
        long got = 0;
        using (var src = await resp.Content.ReadAsStreamAsync())
        using (var dst = File.Create(target))
        {
            var buf = new byte[81920]; int n;
            while ((n = await src.ReadAsync(buf)) > 0) { await dst.WriteAsync(buf.AsMemory(0, n)); got += n; progress?.Report(Math.Min(1, got / (double)rel.Size)); }
        }
        if (got != rel.Size || new FileInfo(target).Length != rel.Size)
        {
            File.Delete(target);
            throw new InvalidOperationException($"the download stopped short ({got:N0} of {rel.Size:N0} bytes)");
        }
        if (rel.Sha256 != null)
        {
            using var f = File.OpenRead(target);
            var hash = Convert.ToHexString(await SHA256.HashDataAsync(f)).ToLowerInvariant();
            if (hash != rel.Sha256) { f.Close(); File.Delete(target); throw new InvalidOperationException("the download doesn't match the release's SHA-256"); }
        }
        return target;
    }

    /// <summary>The swap, as PowerShell: wait for the app to exit, move the new exe over the old one (retrying while Windows holds it), start it.</summary>
    public static string SwapScript(int pid, string newExe, string exe)
    {
        static string Q(string s) => "'" + s.Replace("'", "''") + "'";
        return $"$ErrorActionPreference='SilentlyContinue'; Wait-Process -Id {pid} -Timeout 60; " +
               $"for ($i = 0; $i -lt 30; $i++) {{ try {{ Move-Item -LiteralPath {Q(newExe)} -Destination {Q(exe)} -Force -ErrorAction Stop; break }} catch {{ Start-Sleep -Milliseconds 500 }} }}; " +
               $"Start-Process -FilePath {Q(exe)}";
    }

    /// <summary>
    /// Starts the swap: a hidden PowerShell step waits for this app to exit, moves the new exe over the old one (retrying
    /// while Windows still holds it) and starts it. The caller closes the app right after.
    /// </summary>
    public static void SwapAndRestart(string newExe)
    {
        var exe = Environment.ProcessPath ?? throw new InvalidOperationException("the app's own file couldn't be found");
        var encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes(SwapScript(Environment.ProcessId, newExe, exe)));
        Process.Start(new ProcessStartInfo("powershell.exe", $"-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -EncodedCommand {encoded}") { UseShellExecute = false, CreateNoWindow = true });
    }
}
