using Microsoft.Win32;
using Velopack;
using Velopack.Sources;

namespace KinahLedger.Platform;

/// <summary>
/// Kinah Ledger as an installed app (0.8, Steven 2026-10-22): Velopack installs it per user under
/// %LocalAppData%\KinahLedgerTracker (no admin prompt), with Start menu and desktop shortcuts, an entry in Windows'
/// installed apps, and small delta updates from the GitHub releases. Installed, the app runs as normal files instead
/// of one self-extracting exe, so it starts faster and holds less memory.
///
/// A loose KinahLedger.exe (every 0.7 copy, and the exe every release still carries so 0.7's updater can reach it)
/// keeps working as before, and offers to install itself once. The installed copy then takes over the start-with-Windows
/// entry and the kinahledger:// links. Settings, history and logs live in %LocalAppData%\KinahLedger either way.
/// </summary>
public static class Installer
{
    public const string RepoUrl = "https://github.com/StevenMacchia/kinah-ledger";
    /// <summary>The release asset a loose exe downloads to install itself.</summary>
    public const string SetupAsset = "KinahLedger-Setup.exe";
    public const string Scheme = "kinahledger";

    /// <summary>
    /// The first thing Main does. Velopack starts the exe with its own arguments while it installs, updates or uninstalls;
    /// those runs end here. Otherwise it returns and the app starts as usual.
    /// </summary>
    public static void RunHooks()
    {
        try
        {
            VelopackApp.Build()
                .OnAfterInstallFastCallback(_ => { RegisterScheme(Environment.ProcessPath); })
                .OnAfterUpdateFastCallback(_ => { RegisterScheme(Environment.ProcessPath); })
                .OnBeforeUninstallFastCallback(_ => { UnregisterScheme(); AutoStart.Set(false, Environment.ProcessPath ?? "", out var _); })
                .OnFirstRun(_ => TakeOverFromLooseExe())
                .Run();
        }
        catch (Exception ex) { CrashLog.Write("installer hooks", ex); }
    }

    static UpdateManager? _manager;
    /// <summary>The update manager for the installed app; null when this is a loose exe.</summary>
    public static UpdateManager? Manager
    {
        get
        {
            try
            {
                _manager ??= new UpdateManager(new GithubSource(RepoUrl, null, false));
                return _manager.IsInstalled ? _manager : null;
            }
            catch (Exception) { return null; }
        }
    }

    /// <summary>True when this copy was installed (Velopack); false for a loose KinahLedger.exe.</summary>
    public static bool IsInstalled => Manager != null;

    // ---- kinahledger:// links: the site's "Open in tracker" and "Link this PC" ----

    /// <summary>Registers kinahledger:// for this user, pointing at the installed exe.</summary>
    public static void RegisterScheme(string? exe)
    {
        if (string.IsNullOrEmpty(exe)) return;
        try
        {
            using var k = Registry.CurrentUser.CreateSubKey($@"Software\Classes\{Scheme}");
            k.SetValue("", "URL:Kinah Ledger");
            k.SetValue("URL Protocol", "");
            using (var icon = k.CreateSubKey("DefaultIcon")) icon.SetValue("", $"\"{exe}\",0");
            using var cmd = k.CreateSubKey(@"shell\open\command");
            cmd.SetValue("", $"\"{exe}\" \"%1\"");
        }
        catch (Exception ex) { CrashLog.Write("register kinahledger://", ex); }
    }

    public static void UnregisterScheme()
    {
        try { Registry.CurrentUser.DeleteSubKeyTree($@"Software\Classes\{Scheme}", throwOnMissingSubKey: false); }
        catch (Exception) { /* nothing to remove */ }
    }

    /// <summary>True for an argument that is a kinahledger:// link.</summary>
    public static bool IsLink(string arg) => arg.StartsWith(Scheme + ":", StringComparison.OrdinalIgnoreCase);

    // ---- a loose exe installing itself ----

    static string DeclinedPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "KinahLedger", "install-not-now.txt");
    /// <summary>"Not now" on the install offer: asked again a week later.</summary>
    public static bool OfferDue => !IsInstalled && (!File.Exists(DeclinedPath) || DateTime.UtcNow - File.GetLastWriteTimeUtc(DeclinedPath) > TimeSpan.FromDays(7));
    public static void NotNow() { try { Directory.CreateDirectory(Path.GetDirectoryName(DeclinedPath)!); File.WriteAllText(DeclinedPath, DateTime.UtcNow.ToString("o")); } catch (Exception) { } }

    /// <summary>
    /// Downloads the installer from the newest GitHub release, checks it against the SHA-256 its notes give for it
    /// ("KinahLedger-Setup.exe sha256: …"), and returns its path. Throws with a plain message when any step fails.
    /// </summary>
    public static async Task<string> DownloadSetupAsync(IProgress<double>? progress = null, CancellationToken ct = default)
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("KinahLedger/" + Upload.RunPayload.AppVersion);
        var json = await http.GetStringAsync("https://api.github.com/repos/StevenMacchia/kinah-ledger/releases/latest", ct);
        using var doc = System.Text.Json.JsonDocument.Parse(json);
        var body = doc.RootElement.TryGetProperty("body", out var b) ? b.GetString() ?? "" : "";
        var m = System.Text.RegularExpressions.Regex.Match(body, System.Text.RegularExpressions.Regex.Escape(SetupAsset) + @"\s+sha-?256\W{0,6}([0-9a-fA-F]{64})", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        if (!m.Success) throw new InvalidOperationException("the newest release has no installer yet");
        string? url = null;
        foreach (var a in doc.RootElement.GetProperty("assets").EnumerateArray())
            if (a.GetProperty("name").GetString() == SetupAsset) url = a.GetProperty("browser_download_url").GetString();
        if (url == null) throw new InvalidOperationException("the newest release has no installer yet");

        var path = Path.Combine(Path.GetTempPath(), "KinahLedger-Setup-" + Guid.NewGuid().ToString("N")[..8] + ".exe");
        using (var r = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct))
        {
            r.EnsureSuccessStatusCode();
            long total = r.Content.Headers.ContentLength ?? 0, got = 0;
            await using var src = await r.Content.ReadAsStreamAsync(ct);
            await using var dst = File.Create(path);
            var buf = new byte[81920]; int n;
            while ((n = await src.ReadAsync(buf, ct)) > 0) { await dst.WriteAsync(buf.AsMemory(0, n), ct); got += n; if (total > 0) progress?.Report((double)got / total); }
        }
        string sha;
        await using (var f = File.OpenRead(path)) sha = Convert.ToHexString(await System.Security.Cryptography.SHA256.HashDataAsync(f, ct)).ToLowerInvariant();
        if (sha != m.Groups[1].Value.ToLowerInvariant()) { File.Delete(path); throw new InvalidOperationException("the download didn't match its checksum, so it wasn't run"); }
        return path;
    }

    /// <summary>Starts the verified installer after noting where this loose exe lives, so the installed copy can take over.</summary>
    public static void StartSetup(string setup, string? looseExe)
    {
        if (looseExe != null) WriteHandoff(looseExe);
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(setup) { UseShellExecute = true });
    }

    // ---- moving over from a loose exe ----

    /// <summary>Written by a loose exe just before it starts the installer: where it lived, and whether it started with Windows.</summary>
    static string HandoffPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "KinahLedger", "install-handoff.txt");

    public static void WriteHandoff(string looseExe)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(HandoffPath)!);
            File.WriteAllLines(HandoffPath, new[] { looseExe, AutoStart.IsOn() ? "autostart" : "" });
        }
        catch (Exception ex) { CrashLog.Write("install handoff", ex); }
    }

    /// <summary>
    /// The installed app's first start: the start-with-Windows entry moves to the installed exe (when it was on), the
    /// kinahledger:// links point here, and the loose exe it replaces is removed once it has exited.
    /// </summary>
    static void TakeOverFromLooseExe()
    {
        var exe = Environment.ProcessPath;
        RegisterScheme(exe);
        try
        {
            string? loose = null; bool autostart = AutoStart.IsOn();
            if (File.Exists(HandoffPath))
            {
                var lines = File.ReadAllLines(HandoffPath);
                loose = lines.ElementAtOrDefault(0); autostart |= lines.ElementAtOrDefault(1) == "autostart";
                File.Delete(HandoffPath);
            }
            if (autostart && exe != null) AutoStart.Set(true, exe, out _);
            if (loose != null && File.Exists(loose) && !string.Equals(loose, exe, StringComparison.OrdinalIgnoreCase))
                _ = Task.Run(async () =>
                {
                    // the loose exe exits right after starting the installer; give it a moment, then tidy it away
                    for (int i = 0; i < 30; i++)
                    {
                        try { File.Delete(loose); foreach (var f in Directory.GetFiles(Path.GetDirectoryName(loose)!, Path.GetFileName(loose) + ".*")) File.Delete(f); return; }
                        catch (Exception) { await Task.Delay(1000); }
                    }
                });
        }
        catch (Exception ex) { CrashLog.Write("install takeover", ex); }
    }
}
