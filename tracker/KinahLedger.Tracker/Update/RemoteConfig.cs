using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace KinahLedger.Update;

/// <summary>
/// A small set of reading settings the site can change without a release (Steven, 2026-10-23): the chat's enlarging
/// factor and target text size, the reading pace, the loot list's place on screen, the words that mark the crafting
/// panel, a reward table or a Force handing out loot, a switch per reader to turn one off when a game patch breaks it,
/// and the staged rollout of a new version. Read from kinahledger.com/v1/config at start and every six hours, kept in
/// %LocalAppData%\KinahLedger\remote-config.json, and used from the next read on.
///
/// The site can only nudge, never break: every number is held to a safe range, a list the tracker doesn't know is
/// ignored, and with no answer and nothing kept the built-in defaults below are exactly what the tracker did before.
/// Only the app version goes with the request.
/// </summary>
public sealed class RemoteConfig
{
    public string Id { get; init; } = "built-in";
    public double TtlHours { get; init; } = 6;

    // ---- reading ----
    public int Scale { get; init; } = 2;
    public int TargetTextPx { get; init; } = 36;
    public int IntervalMs { get; init; } = 700;
    public int FightIntervalMs { get; init; } = 300;

    /// <summary>The loot list's band, in game heights from the screen's centre (LootPopup.Region).</summary>
    public string RegionsId { get; init; } = "b1";
    public double LootLeft { get; init; } = -0.42;
    public double LootRight { get; init; } = -0.02;
    public double LootTop { get; init; } = -0.06;
    public double LootBottom { get; init; } = 0.30;

    // ---- notice words ----
    public string[] CraftPanelWords { get; init; } = { "Crafting" };
    public string[] RewardCollectedWords { get; init; } = { "collected" };
    /// <summary>Added to the built-in pattern (distri…, through Force, loot chest), never in place of it.</summary>
    public string[] DistributionWords { get; init; } = { "distri", "loot chest" };
    public string[] CraftWindowWords { get; init; } = { "acquired design" };
    /// <summary>Added to PickupCheck's built-in pattern (storage, Quick Register, an exchange...), never in place of it.</summary>
    public string[] InventoryWords { get; init; } = { "stored item", "quick register" };
    /// <summary>Added to RelogDetector's built-in phrases for the character screen, never in place of them.</summary>
    public string[] CharacterScreenWords { get; init; } = { "create character" };

    // ---- the pickup check (PickupCheck) ----
    /// <summary>The wardrobe's "(Slot) Skin acquired" lines count as drops. Off: they are appearance unlocks.</summary>
    public bool SkinsCount { get; init; }
    /// <summary>More of one item than this in one line, out in the world, is a reward or a claim, not a pickup.</summary>
    public int MaxOnePickup { get; init; } = 100;
    /// <summary>More Soul Crystals than this in one line, out in the world, are a reward, not a kill's.</summary>
    public int MaxOneCrystal { get; init; } = 10;
    /// <summary>An item out in the world is a drop only with a kill or a gather this close to it, either side; 0 turns the rule off.</summary>
    public double WitnessSeconds { get; init; } = 6;
    /// <summary>
    /// Each finished expedition, Transcendence or event run is sent the moment it ends, on its own (Steven, 2026-10-23:
    /// "the expeditions transcendence and events need to sync after each one to the profile"). On: the site takes a
    /// runs-only send since 2026-10-23 (API 8eb8c211). The site can turn it off ("runs": {"sync": false}); runs then ride
    /// with the farming chunks as before.
    /// </summary>
    public bool RunSync { get; init; } = true;
    /// <summary>The loot ledger (a loot line no read showed, wherever it sits) counts. Off: it only logs what it would count.</summary>
    public bool Ledger { get; init; }   // off until it stops overcounting noisy chat (synthetic, 2026-10-23: +2.7% Kinah)

    // ---- kill switches: true turns that reader off ----
    public HashSet<string> Killed { get; init; } = new(StringComparer.OrdinalIgnoreCase);
    public static readonly string[] Switches = { "loot_list", "craft_panel", "target", "nameplates", "reward_grid", "balance", "zone", "name_colours", "loot_icons", "market", "accuracy_upload", "layout_hint" };
    public bool Off(string reader) => Killed.Contains(reader);

    // ---- the scene classifier (Session/SceneState.cs) ----
    /// <summary>
    /// Readers are skipped (or their reads held) when the scene on screen isn't one they are valid in. Off ("scene":
    /// {"gate": false}): the scene is still worked out and logged every pass, and nothing is gated.
    /// </summary>
    public bool SceneGate { get; init; } = true;

    // ---- rare drops (RareGate) ----
    /// <summary>
    /// Legend-grade materials count as rare (Steven's rule as written: Legend, Unique and Epic drops). About a fifth of the
    /// loot tables' drops are Legend materials, so the site can turn this off and leave Legend for gear only.
    /// </summary>
    public bool LegendMaterialsRare { get; init; } = true;

    // ---- staged rollout ----
    public string RolloutVersion { get; init; } = "";
    public int RolloutPercent { get; init; } = 100;
    public bool RolloutHold { get; init; }
    public string RolloutReason { get; init; } = "";
    /// <summary>For a version the site hasn't been told about yet.</summary>
    public int DefaultPercent { get; init; } = 100;

    public static RemoteConfig BuiltIn { get; } = new();

    static RemoteConfig _current = BuiltIn;
    /// <summary>What the readers use now. Set at start from the kept copy, then from each fetch.</summary>
    public static RemoteConfig Current { get => _current; set => _current = value ?? BuiltIn; }

    /// <summary>Reads a config, holding every value to a safe range. Unknown or broken parts keep the built-in value.</summary>
    public static RemoteConfig Parse(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var r = doc.RootElement;
        if (r.ValueKind != JsonValueKind.Object) throw new FormatException("not an object");
        var b = BuiltIn;
        JsonElement? Obj(JsonElement e, string k) => e.ValueKind == JsonValueKind.Object && e.TryGetProperty(k, out var v) && v.ValueKind == JsonValueKind.Object ? v : null;
        double Num(JsonElement? e, string k, double def, double min, double max)
        {
            if (e is not { } o || !o.TryGetProperty(k, out var v) || v.ValueKind != JsonValueKind.Number || !v.TryGetDouble(out var d) || double.IsNaN(d)) return def;
            return Math.Clamp(d, min, max);
        }
        string Str(JsonElement? e, string k, string def, int max = 40)
        {
            if (e is not { } o || !o.TryGetProperty(k, out var v) || v.ValueKind != JsonValueKind.String) return def;
            var s = v.GetString() ?? def;
            return s.Length > max ? s[..max] : s;
        }
        string[] Words(JsonElement? e, string k, string[] def)
        {
            if (e is not { } o || !o.TryGetProperty(k, out var v) || v.ValueKind != JsonValueKind.Array) return def;
            var list = v.EnumerateArray().Where(x => x.ValueKind == JsonValueKind.String).Select(x => (x.GetString() ?? "").Trim())
                .Where(s => s.Length >= 3 && s.Length <= 40).Take(12).ToArray();
            return list.Length > 0 ? list : def;   // an empty list would switch a check off: that is what kill switches are for
        }

        var ocr = Obj(r, "ocr");
        var regions = Obj(r, "regions");
        var loot = regions is { } rg ? Obj(rg, "loot") : null;
        var words = Obj(r, "words");
        var kill = Obj(r, "kill");
        var roll = Obj(r, "rollout");
        var rare = Obj(r, "rare");
        var pick = Obj(r, "pickup");
        var scene = Obj(r, "scene");
        // the loot band moves at most a little from where it was measured, and never turns inside out
        double ll = Num(loot, "left", b.LootLeft, -0.60, -0.25), lr = Num(loot, "right", b.LootRight, -0.10, 0.10);
        double lt = Num(loot, "top", b.LootTop, -0.20, 0.05), lb = Num(loot, "bottom", b.LootBottom, 0.15, 0.45);
        if (lr - ll < 0.2) { ll = b.LootLeft; lr = b.LootRight; }
        if (lb - lt < 0.15) { lt = b.LootTop; lb = b.LootBottom; }
        var killed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (kill is { } k)
            foreach (var p in k.EnumerateObject())
                if (p.Value.ValueKind == JsonValueKind.True && Switches.Contains(p.Name, StringComparer.OrdinalIgnoreCase)) killed.Add(p.Name);
        return new RemoteConfig
        {
            Id = r.TryGetProperty("id", out var id) && id.ValueKind == JsonValueKind.String ? (id.GetString() ?? "remote") : "remote",
            TtlHours = Num(r, "ttl_hours", b.TtlHours, 1, 48),
            Scale = (int)Num(ocr, "scale", b.Scale, 1, 4),
            TargetTextPx = (int)Num(ocr, "target_text_px", b.TargetTextPx, 20, 60),
            IntervalMs = (int)Num(ocr, "interval_ms", b.IntervalMs, 300, 3000),
            FightIntervalMs = (int)Num(ocr, "fight_interval_ms", b.FightIntervalMs, 250, 1500),
            RegionsId = loot != null ? Str(regions, "id", "remote", 16) : b.RegionsId,
            LootLeft = ll, LootRight = lr, LootTop = lt, LootBottom = lb,
            CraftPanelWords = Words(words, "craft_panel", b.CraftPanelWords),
            RewardCollectedWords = Words(words, "reward_collected", b.RewardCollectedWords),
            DistributionWords = Words(words, "distribution", b.DistributionWords),
            CraftWindowWords = Words(words, "craft_window", b.CraftWindowWords),
            InventoryWords = Words(words, "inventory", b.InventoryWords),
            CharacterScreenWords = Words(words, "character_screen", b.CharacterScreenWords),
            SkinsCount = pick is { } pk && pk.TryGetProperty("skins_count", out var sc) && sc.ValueKind == JsonValueKind.True,
            MaxOnePickup = (int)Num(pick, "max_one", b.MaxOnePickup, 20, 10000),
            MaxOneCrystal = (int)Num(pick, "max_crystals", b.MaxOneCrystal, 3, 1000),
            WitnessSeconds = Num(pick, "witness_seconds", b.WitnessSeconds, 0, 20),
            RunSync = r.TryGetProperty("runs", out var rs) && rs.ValueKind == JsonValueKind.Object && rs.TryGetProperty("sync", out var rsy) && rsy.ValueKind is JsonValueKind.True or JsonValueKind.False
                ? rsy.ValueKind == JsonValueKind.True : b.RunSync,
            Ledger = pick is { } pl && pl.TryGetProperty("ledger", out var lg) && lg.ValueKind == JsonValueKind.True,
            Killed = killed,
            SceneGate = !(scene is { } sg && sg.TryGetProperty("gate", out var sgv) && sgv.ValueKind == JsonValueKind.False),
            LegendMaterialsRare = !(rare is { } ra && ra.TryGetProperty("legend_materials", out var lm) && lm.ValueKind == JsonValueKind.False),
            RolloutVersion = Str(roll, "version", ""),
            RolloutPercent = (int)Num(roll, "percent", 100, 0, 100),
            RolloutHold = roll is { } ro && ro.TryGetProperty("hold", out var h) && h.ValueKind == JsonValueKind.True,
            RolloutReason = Str(roll, "reason", "", 200),
            DefaultPercent = (int)Num(roll, "default_percent", 100, 0, 100),
        };
    }

    // ---- words ----

    public static bool HasAny(string text, IEnumerable<string> words) => words.Any(w => text.Contains(w, StringComparison.OrdinalIgnoreCase));

    // ---- staged rollout ----

    /// <summary>This install's place, 0 to 99, for one version: the same every time, different per version, and only on this PC.</summary>
    public static int Bucket(string install, Version v)
    {
        var h = SHA256.HashData(Encoding.UTF8.GetBytes($"{install}:{Updater.Show(v)}"));
        return (int)(BitConverter.ToUInt32(h, 0) % 100);
    }

    /// <summary>
    /// Whether a found update installs on its own here. A version under a staged rollout goes to the installs whose
    /// bucket is under its percent; a held one (its reading numbers came out worse than the last version's) to none.
    /// A version the site doesn't know yet goes by the default percent. Check now in Options always shows it; this only
    /// decides the automatic install. The built-in config (no answer, nothing kept) never holds anything back.
    /// </summary>
    public (bool Auto, string Why) AutoInstall(Version v, string install)
    {
        var rv = Updater.ParseVersion(RolloutVersion);
        int pct;
        if (rv != null && rv == v)
        {
            if (RolloutHold) return (false, "held: " + (RolloutReason.Length > 0 ? RolloutReason : "its reading numbers are being checked"));
            pct = RolloutPercent;
        }
        else if (rv != null && rv > v) pct = 100;   // the rollout is already past this version
        else pct = DefaultPercent;
        if (pct >= 100) return (true, "");
        int b = Bucket(install, v);
        return b < pct ? (true, $"in the first {pct}%") : (false, $"rolling out to {pct}% of trackers first");
    }

    // ---- fetching and keeping ----

    static string KeptPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "KinahLedger", "remote-config.json");
    public static string? PathOverride { get; set; }
    static string Kept => PathOverride ?? KeptPath;

    /// <summary>The kept copy, when there is one that parses; the built-in defaults otherwise.</summary>
    public static RemoteConfig LoadKept()
    {
        try { if (File.Exists(Kept)) return Parse(File.ReadAllText(Kept)); }
        catch (Exception ex) { CrashLog.Note("remote config: the kept copy didn't read (" + ex.Message + "); using the built-in settings"); }
        return BuiltIn;
    }

    /// <summary>Where the config comes from: the runs address with /v1/config in place of /v1/runs; "" when sharing has no site address.</summary>
    public static string UrlFor(UploadSettings u)
    {
        var e = (u.Endpoint ?? "").Trim().TrimEnd('/');
        return e.EndsWith("/v1/runs", StringComparison.OrdinalIgnoreCase) ? e[..^4] + "config" : "";
    }

    /// <summary>One fetch. Keeps and returns the new config, or null (the current one stays) when the site can't be reached or answers nonsense.</summary>
    public static async Task<RemoteConfig?> FetchAsync(HttpClient http, string url, CancellationToken ct = default)
    {
        if (url.Length == 0) return null;
        try
        {
            var full = url + (url.Contains('?') ? "&" : "?") + "app=" + Uri.EscapeDataString(Upload.RunPayload.AppVersion);
            using var res = await http.GetAsync(full, ct);
            if (!res.IsSuccessStatusCode) { CrashLog.Note($"remote config: HTTP {(int)res.StatusCode}; keeping {Current.Id}"); return null; }
            var body = await res.Content.ReadAsStringAsync(ct);
            if (body.Length > 64 * 1024) return null;
            var cfg = Parse(body);
            try { Directory.CreateDirectory(Path.GetDirectoryName(Kept)!); File.WriteAllText(Kept, body); } catch (Exception) { /* kept next time */ }
            if (cfg.Id != Current.Id) CrashLog.Note($"remote config {cfg.Id}: scale {cfg.Scale}, text {cfg.TargetTextPx} px, readers off: {(cfg.Killed.Count > 0 ? string.Join(", ", cfg.Killed) : "none")}, rollout {(cfg.RolloutVersion.Length > 0 ? $"{cfg.RolloutVersion} at {cfg.RolloutPercent}%{(cfg.RolloutHold ? " (held)" : "")}" : "none")}");
            return cfg;
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            CrashLog.Note("remote config: " + ex.Message);
            return null;
        }
    }

    /// <summary>
    /// The site's reading numbers in place of the defaults in <paramref name="s"/>, for each one settings.json doesn't set
    /// itself (a hand-set value always wins). <paramref name="fileJson"/> is settings.json's text, or null without one.
    /// </summary>
    public void ApplyTo(Settings s, string? fileJson)
    {
        bool Set(string section, string key)
        {
            if (fileJson == null) return false;
            try
            {
                using var d = JsonDocument.Parse(fileJson, new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
                foreach (var sec in d.RootElement.EnumerateObject())
                    if (sec.Name.Equals(section, StringComparison.OrdinalIgnoreCase) && sec.Value.ValueKind == JsonValueKind.Object)
                        foreach (var p in sec.Value.EnumerateObject()) if (p.Name.Equals(key, StringComparison.OrdinalIgnoreCase)) return true;
            }
            catch (JsonException) { }
            return false;
        }
        if (!Set("ocr", "scale")) s.Ocr.Scale = Scale;
        if (!Set("ocr", "targetTextPx")) s.Ocr.TargetTextPx = TargetTextPx;
        if (!Set("ocr", "intervalMs")) s.Ocr.IntervalMs = IntervalMs;
        if (Off("name_colours")) s.Ocr.NameColours = false;
        if (Off("loot_icons")) s.Ocr.LootIcons = false;
    }
}
