using System.Security.Cryptography;
using System.Text.Json;

namespace KinahLedger.Data;

/// <summary>
/// The game data the tracker reads (zones, events, enemies, loot tables, skins, recipes, enemy pictures, gathering
/// materials), updated without a new release (0.8, Steven 2026-10-22: "a new event zone works the same day").
///
/// Every release carries a copy of each file inside the exe. Once a day the tracker reads data/manifest.json from the
/// repository's main branch, which lists every file with its SHA-256; a file whose hash differs is downloaded, checked
/// against that hash and against the same shape as the built-in copy (it parses, and holds at least half as many
/// entries), and only then kept in %LocalAppData%\KinahLedger\data. The next start reads the kept copy; anything that
/// fails a check leaves the built-in copy in use. The lists stay strict whitelists: they come only from this repository.
/// </summary>
public static class DataFiles
{
    /// <summary>Where the files come from: the repository's main branch. Settable for the data-update test command only.</summary>
    public static string FileUrl { get; set; } = "https://raw.githubusercontent.com/StevenMacchia/kinah-ledger/main/data/";
    public static string ManifestUrl => FileUrl + "manifest.json";
    /// <summary>The files that can update this way; each is also built into the exe under the same name.</summary>
    public static readonly string[] Names = { "zones.txt", "instances.txt", "mobs.txt", "loot-tables.json", "skins.json", "recipes.json", "mob-images.json", "gathering-items.json", "aliases.json" };

    /// <summary>Tests point this elsewhere so the player's own copies are never touched.</summary>
    public static string? DirOverride { get; set; }
    static string Dir => DirOverride ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "KinahLedger", "data");
    static string KeptManifest => Path.Combine(Dir, "manifest.json");

    /// <summary>The kept copy of a data file when one passed its checks, else the copy built into the exe; null when neither exists.</summary>
    public static Stream? Open(string name)
    {
        try
        {
            var p = Path.Combine(Dir, name);
            if (File.Exists(p) && Kept().TryGetValue(name, out var sha) && Sha(p) == sha) return File.OpenRead(p);
        }
        catch (Exception) { /* fall back to the built-in copy */ }
        return typeof(DataFiles).Assembly.GetManifestResourceStream(name);
    }

    /// <summary>Which files are kept, with their hashes, as the last good manifest said.</summary>
    static Dictionary<string, string> Kept()
    {
        try { return File.Exists(KeptManifest) ? Parse(File.ReadAllText(KeptManifest)).Files : new(); }
        catch (Exception) { return new(); }
    }

    public sealed record Manifest(string MinTracker, Dictionary<string, string> Files);

    public static Manifest Parse(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var r = doc.RootElement;
        var files = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (r.TryGetProperty("files", out var f) && f.ValueKind == JsonValueKind.Object)
            foreach (var p in f.EnumerateObject())
                if (p.Value.TryGetProperty("sha256", out var s) && s.GetString() is { Length: 64 } h) files[p.Name] = h.ToLowerInvariant();
        var min = r.TryGetProperty("min_tracker", out var m) ? m.GetString() ?? "" : "";
        return new Manifest(min, files);
    }

    static string Sha(string path) { using var f = File.OpenRead(path); return Convert.ToHexString(SHA256.HashData(f)).ToLowerInvariant(); }

    /// <summary>
    /// One check: fetches the manifest, downloads every file whose hash differs from the kept or built-in copy, checks
    /// each, and records the result. Returns the names that were updated (they take effect at the next start).
    /// </summary>
    public static async Task<List<string>> UpdateAsync(HttpClient http, CancellationToken ct = default)
    {
        var done = new List<string>();
        var man = Parse(await http.GetStringAsync(ManifestUrl, ct));
        if (Update.Updater.ParseVersion(man.MinTracker) is { } need && need > Update.Updater.Current) return done;   // a format this version can't read
        Directory.CreateDirectory(Dir);
        var kept = Kept();
        foreach (var name in Names)
        {
            if (!man.Files.TryGetValue(name, out var want)) continue;
            // kept and intact (the file is hashed again: a copy damaged on disk is fetched again)
            if (kept.TryGetValue(name, out var have) && have == want && File.Exists(Path.Combine(Dir, name)) && Sha(Path.Combine(Dir, name)) == want) continue;
            if (BuiltInSha(name) == want) { kept.Remove(name); TryDelete(Path.Combine(Dir, name)); continue; }   // the built-in copy is current
            var part = Path.Combine(Dir, name + ".part");
            try
            {
                var bytes = await http.GetByteArrayAsync(FileUrl + name, ct);
                if (Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant() != want) { CrashLog.Note($"data update: {name} didn't match its checksum, kept the old copy"); continue; }
                if (!LooksRight(name, bytes)) { CrashLog.Note($"data update: {name} failed its shape check, kept the old copy"); continue; }
                await File.WriteAllBytesAsync(part, bytes, ct);
                File.Move(part, Path.Combine(Dir, name), overwrite: true);
                kept[name] = want; done.Add(name);
            }
            catch (Exception ex) when (ex is not OperationCanceledException) { CrashLog.Note($"data update: {name}: {ex.Message}"); TryDelete(part); }
        }
        var outp = new { min_tracker = man.MinTracker, files = kept.ToDictionary(kv => kv.Key, kv => new { sha256 = kv.Value }) };
        await File.WriteAllTextAsync(KeptManifest, JsonSerializer.Serialize(outp), ct);
        if (done.Count > 0) CrashLog.Note("data update: " + string.Join(", ", done) + " (in use from the next start)");
        return done;
    }

    /// <summary>
    /// The built-in copy's hash, with Windows line endings turned back into the repository's: a Windows checkout writes
    /// CRLF, while the manifest and the downloads carry the files as the repository stores them (LF).
    /// </summary>
    /// <summary>For the data-check command: each file's built-in hash beside the one given.</summary>
    public static string? BuiltInHash(string name) => BuiltInSha(name);

    static string? BuiltInSha(string name)
    {
        using var s = typeof(DataFiles).Assembly.GetManifestResourceStream(name);
        if (s == null) return null;
        using var ms = new MemoryStream(); s.CopyTo(ms);
        var bytes = ms.ToArray();
        var lf = new List<byte>(bytes.Length);
        for (int i = 0; i < bytes.Length; i++) if (!(bytes[i] == 13 && i + 1 < bytes.Length && bytes[i + 1] == 10)) lf.Add(bytes[i]);
        return Convert.ToHexString(SHA256.HashData(lf.ToArray())).ToLowerInvariant();
    }

    /// <summary>A download is used only when it parses like the built-in copy and holds at least half as many entries.</summary>
    public static bool LooksRight(string name, byte[] bytes)
    {
        try
        {
            int Count(Stream s)
            {
                if (name.EndsWith(".txt"))
                {
                    using var r = new StreamReader(s);
                    int n = 0; while (r.ReadLine() is { } l) if (l.Trim().Length > 0 && !l.TrimStart().StartsWith('#')) n++;
                    return n;
                }
                using var doc = JsonDocument.Parse(s);
                var root = doc.RootElement;
                foreach (var key in new[] { "tables", "items", "recipes", "images", "aliases" })
                    if (root.TryGetProperty(key, out var v)) return v.ValueKind == JsonValueKind.Array ? v.GetArrayLength() : v.ValueKind == JsonValueKind.Object ? v.EnumerateObject().Count() : 0;
                return root.ValueKind == JsonValueKind.Array ? root.GetArrayLength() : root.ValueKind == JsonValueKind.Object ? root.EnumerateObject().Count() : 0;
            }
            int got = Count(new MemoryStream(bytes));
            using var built = typeof(DataFiles).Assembly.GetManifestResourceStream(name);
            int was = built == null ? 0 : Count(built);
            return got > 0 && got * 2 >= was;
        }
        catch (Exception) { return false; }
    }

    static void TryDelete(string p) { try { if (File.Exists(p)) File.Delete(p); } catch (Exception) { } }
}
