using System.Drawing;
using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using KinahLedger.Data;
using KinahLedger.Ocr;

namespace KinahLedger.Session;

/// <summary>
/// The accuracy bench (2026-11-05): a screen recording of a farming run, cut into frames, read through this version's own
/// readers and counted as a live run is, then scored against a hand-made list of every pickup the footage shows. The reads
/// are cached per frame (bench-ocr.jsonl beside the frames), so a parser or session change replays in seconds; --fresh reads
/// the frames again after a reader change. The score is held against a baseline (tracker/tests/bench/baseline.json): a build
/// that misses or doubles more than the baseline fails. See tracker/tests/bench/README.md for the loop this runs in.
/// </summary>
public static class BenchReplay
{
    const int CacheVersion = 1;

    sealed record Landed(DateTime At, string Class, string Key, string What);
    sealed record Truth(string Id, double Clip, string Class, string Key, string What, string Note);

    public static int Run(Settings settings, List<string> pos, Dictionary<string, string> o)
    {
        var dir = pos.FirstOrDefault();
        if (dir == null || !Directory.Exists(dir))
        {
            Console.Error.WriteLine("bench-replay <frames folder> --start \"yyyy-MM-dd HH:mm:ss.f\" [--truth truth.csv] [--reads reads.log] [--fps 2] [--step 1]");
            Console.Error.WriteLine("             [--scale-to 5120x2160] [--chat x,y,w,h] [--offset 1.6] [--fresh] [--baseline baseline.json --run name [--record]]");
            return 1;
        }
        var frames = Directory.GetFiles(dir).Where(f => f.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
            .OrderBy(f => f, StringComparer.Ordinal).ToList();
        if (frames.Count == 0) { Console.Error.WriteLine($"no .jpg or .png frames in {dir}"); return 1; }
        double fps = o.TryGetValue("fps", out var fv) && double.TryParse(fv, NumberStyles.Float, CultureInfo.InvariantCulture, out var fd) ? fd : 2;
        int step = o.TryGetValue("step", out var sv) && int.TryParse(sv, out var sd) ? Math.Max(1, sd) : 1;
        double offset = o.TryGetValue("offset", out var ov) && double.TryParse(ov, NumberStyles.Float, CultureInfo.InvariantCulture, out var od) ? od : 0;
        if (!o.TryGetValue("start", out var startText)) { Console.Error.WriteLine("--start is needed: the wall time (local) of the first frame, or a .start file holding it"); return 1; }
        if (File.Exists(startText)) startText = File.ReadAllText(startText).Trim();
        if (!DateTime.TryParse(startText, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var start)) { Console.Error.WriteLine($"can't read a time from --start \"{startText}\""); return 1; }
        start = DateTime.SpecifyKind(start, DateTimeKind.Local).AddSeconds(offset);
        Size? scaleTo = null;
        if (o.TryGetValue("scale-to", out var st) && Regex.Match(st, @"^(\d+)x(\d+)$") is { Success: true } sm) scaleTo = new Size(int.Parse(sm.Groups[1].Value), int.Parse(sm.Groups[2].Value));
        var chatOpt = o.TryGetValue("chat", out var cv) ? RegionStore.Parse(cv) : null;
        string run = o.GetValueOrDefault("run") ?? Path.GetFileName(Path.TrimEndingDirectorySeparator(dir));

        var items = ItemDictionary.LoadOrEmpty(settings.ItemsFile);
        // the bench's own notes, never the player's logs
        CrashLog.DirOverride = Path.Combine(Path.GetTempPath(), "kinah-bench-replay");
        Directory.CreateDirectory(CrashLog.DirOverride);
        File.WriteAllText(Path.Combine(CrashLog.DirOverride, "reads.log"), "");
        try { File.Delete(Path.Combine(CrashLog.DirOverride, "instances.jsonl")); } catch (Exception) { }
        try
        {
            var cache = Path.Combine(dir, "bench-ocr.jsonl");
            var metaPath = Path.Combine(dir, "bench-ocr.meta.json");
            var meta = JsonSerializer.Serialize(new
            {
                v = CacheVersion, fps, step, frames = frames.Count, scaleTo = scaleTo?.ToString() ?? "", chat = chatOpt?.ToString() ?? "auto",
                ocr = $"{settings.Ocr.Scale}/{settings.Ocr.AutoScale}/{settings.Ocr.TargetTextPx}/{settings.Ocr.LootIcons}/{settings.Ocr.NameColours}",
            });
            bool cached = !o.ContainsKey("fresh") && File.Exists(cache) && File.Exists(metaPath) && File.ReadAllText(metaPath) == meta;
            if (cached) Console.WriteLine($"Reads cached in {cache} ({File.ReadLines(cache).Count()} frames); --fresh reads the frames again.");
            else
            {
                if (!ReplayBank.OcrAvailable()) { Console.Error.WriteLine("no text recognition here, and no cached reads for these frames"); return 2; }
                ReadFrames(settings, items, frames, fps, step, start, scaleTo, chatOpt, cache);
                File.WriteAllText(metaPath, meta);
            }

            var landed = Replay(settings, items, cache, o.GetValueOrDefault("reads"), start);
            var notes = Path.Combine(dir, "bench-reads.log");
            File.Copy(Path.Combine(CrashLog.DirOverride, "reads.log"), notes, true);
            Console.WriteLine($"Counted {landed.Count} events with {Upload.RunPayload.AppVersion}; the replay's notes are in {notes}");
            if (!o.TryGetValue("truth", out var truthPath)) { foreach (var l in landed) Console.WriteLine($"  {(l.At.ToLocalTime() - start).TotalSeconds,7:0.0} s  {l.What}"); return 0; }
            if (!File.Exists(truthPath)) { Console.Error.WriteLine($"no truth file at {truthPath}"); return 1; }
            return Score(run, landed, LoadTruth(truthPath), start, o);
        }
        finally { CrashLog.DirOverride = null; CrashLog.Clock = null; }
    }

    // ---- reading the frames once ------------------------------------------------------------------------------------

    static Bitmap Load(string path, Size? scaleTo)
    {
        var bmp = Task.Run(() => ScreenReader.LoadAsync(path)).GetAwaiter().GetResult();
        if (scaleTo is not { } s || bmp.Size == s) return bmp;
        using (bmp) return new Bitmap(bmp, s);
    }

    static Bitmap Crop(Bitmap bmp, Rectangle r)
    {
        r.Intersect(new Rectangle(0, 0, bmp.Width, bmp.Height));
        return bmp.Clone(r, bmp.PixelFormat);
    }

    static void ReadFrames(Settings settings, ItemDictionary items, List<string> frames, double fps, int step, DateTime start, Size? scaleTo, Rectangle? chatOpt, string cache)
    {
        Rectangle game;
        using (var first = Load(frames[0], scaleTo)) game = new Rectangle(0, 0, first.Width, first.Height);
        // the chat as the live locator finds it. One frame can lie (another window over the game, the chat faded out while
        // idle), so the locator runs on a frame every 5 s through the first few minutes and the place most of them agree on
        // wins; the live tracker settles the same way over its passes
        Rectangle? chat = chatOpt; string note = "given on the command line";
        if (chat == null)
        {
            var votes = new List<(Rectangle R, string Note, int Frame)>();
            int every = Math.Max(1, (int)Math.Round(fps * 5));
            for (int i = 0; i < frames.Count && votes.Count < 36; i += every)
            {
                using var bmp = Load(frames[i], scaleTo);
                var found = Task.Run(() => ChatLocator.DetectAsync(game, null, (r, sc, boost) =>
                {
                    using var crop = Crop(bmp, r);
                    return ScreenReader.ReadBoxesAsync(crop, r.Location, sc, boost);
                })).GetAwaiter().GetResult();
                if (found.region is { } fr) votes.Add((fr, found.note, i));
            }
            // cluster by where the box's bottom-left corner sits: the chat's lines grow up from there
            static bool Same(Rectangle a, Rectangle b) => Math.Abs(a.Left - b.Left) < 80 && Math.Abs(a.Bottom - b.Bottom) < 80;
            var best = votes.Select(v => (V: v, N: votes.Count(w => Same(w.R, v.R)))).OrderByDescending(x => x.N).ThenBy(x => x.V.Frame).FirstOrDefault();
            if (best.V.R.Width > 0)
            {
                chat = best.V.R;
                note = $"{best.V.Note}; {best.N} of {votes.Count} located frames agree, first at frame {best.V.Frame}";
            }
        }
        if (chat is not { } chatRect) throw new InvalidOperationException("the chat wasn't found in the frames; give it with --chat x,y,w,h");
        var loot = LootPopup.Region(game);
        Console.WriteLine($"Frames {game.Width}x{game.Height}, {frames.Count} at {fps} fps, every {step}: chat {chatRect} ({note}), loot list {loot}.");
        var auto = settings.Ocr.AutoScale ? new TextScale(settings.Ocr.Scale, settings.Ocr.TargetTextPx) { ToTarget = () => Update.RemoteConfig.Current.ChatScaleToTarget } : null;
        var reader = new ScreenReader(chatRect, settings.Ocr.Scale, auto);
        using var w = new StreamWriter(cache, false);
        var sw = System.Diagnostics.Stopwatch.StartNew();
        int done = 0;
        for (int i = 0; i < frames.Count; i += step)
        {
            var t = start.AddSeconds(i / fps).ToUniversalTime();
            using var bmp = Load(frames[i], scaleTo);
            List<string> chatLines; List<string[]> accents;
            using (var crop = Crop(bmp, chatRect))
            {
                chatLines = Task.Run(() => reader.ReadCaptureAsync(crop)).GetAwaiter().GetResult();
                accents = reader.LastAccents.Select(a => new[] { a.Text, ColorTranslator.ToHtml(a.Accent) }).ToList();
            }
            var rows = new List<object>();
            using (var crop = Crop(bmp, loot))
            {
                var boxes = Task.Run(() => ScreenReader.ReadBoxesAsync(crop, loot.Location, 3, boost: true)).GetAwaiter().GetResult();
                foreach (var b in boxes)
                {
                    var look = settings.Ocr.LootIcons ? PopupIcons.Name(items, crop, loot.Location, b) : null;
                    var m = LootRows.MeasureAll(crop, loot.Location, b.Box);
                    rows.Add(new { r = b.Text, text = look?.Text ?? b.Text, y = b.Box.Top, x = b.Box.Left, c = Math.Round(m.Contrast, 3), h = m.Icon.ToString("x"), b = Math.Round(m.Background, 3), s = Math.Round(m.Spread, 3), i = look?.Trace ?? "" });
                }
            }
            w.WriteLine(JsonSerializer.Serialize(new { i, t = t.ToString("O"), chat = chatLines, acc = accents, list = rows }));
            if (++done % 50 == 0) Console.WriteLine($"  read {done} frames ({sw.Elapsed.TotalSeconds:0} s)");
        }
        Console.WriteLine($"Read {done} frames in {sw.Elapsed.TotalSeconds:0} s; cached in {cache}.");
    }

    // ---- counting them as a live run would ----------------------------------------------------------------------------

    static List<Landed> Replay(Settings settings, ItemDictionary items, string cache, string? readsLog, DateTime start)
    {
        var s = new TrackerSession(null, settings, items, "Replay") { FastChat = true, ListRows = new() };
        var landed = new List<Landed>();
        s.Landed = (ev, _) =>
        {
            var l = Classify(ev, items);
            if (l != null) landed.Add(l);
        };
        // the zone and reward screens as the live tracker noted them in reads.log beside the recording, repeated every 3 s as live
        var signals = new List<ReplaySignal>();
        if (readsLog != null && File.Exists(readsLog)) signals = ReplaySignals.FromReadsLog(readsLog, start.Date).OrderBy(x => x.At).ToList();
        int si = 0; string? zoneNow = null; DateTime tickAt = DateTime.MinValue, clock = DateTime.MinValue;
        CrashLog.Clock = () => clock == DateTime.MinValue ? DateTime.Now : clock.ToLocalTime();
        void SignalsUntil(DateTime t)
        {
            while (true)
            {
                var nextTick = zoneNow == null ? DateTime.MaxValue : tickAt.AddSeconds(3);
                var nextSig = si < signals.Count ? signals[si].At : DateTime.MaxValue;
                var next = nextTick < nextSig ? nextTick : nextSig;
                if (next > t) return;
                if (nextTick <= nextSig) { tickAt = nextTick; clock = nextTick; s.ApplyZone(zoneNow, nextTick); s.UpdateDoing(nextTick); continue; }
                var sg = signals[si++];
                clock = sg.At;
                switch (sg.Kind)
                {
                    case "zone": zoneNow = sg.Value; tickAt = sg.At; s.ApplyZone(sg.Value, sg.At); break;
                    case "reward": if (sg.Value.Length > 0) s.Target.MarkReward(sg.At, sg.Value); break;
                    case "header": s.EventHeaderSeen(sg.Value, sg.At); break;
                }
            }
        }
        DateTime? last = null; int n = 0;
        foreach (var raw in File.ReadLines(cache))
        {
            using var d = JsonDocument.Parse(raw);
            var r = d.RootElement;
            var t = DateTime.Parse(r.GetProperty("t").GetString()!, null, DateTimeStyles.RoundtripKind).ToUniversalTime();
            SignalsUntil(t);
            clock = t;
            var chat = r.GetProperty("chat").EnumerateArray().Select(x => x.GetString() ?? "").ToList();
            var acc = r.GetProperty("acc").EnumerateArray().Select(a => { var p = a.EnumerateArray().Select(x => x.GetString() ?? "").ToArray(); return (p[0], ColorTranslator.FromHtml(p[1])); }).ToList();
            s.Parser.NoteAccents(acc);
            s.QuestCounter(chat, t);
            s.ChatGated(chat, t, s.Verdict("chat"));
            var lines = r.GetProperty("list").EnumerateArray().Select(x =>
            {
                var line = new LootLine(x.GetProperty("y").GetInt32(), x.GetProperty("text").GetString() ?? "", x.GetProperty("c").GetDouble(),
                    Convert.ToUInt64(x.GetProperty("h").GetString() ?? "0", 16), x.GetProperty("b").GetDouble(), x.GetProperty("s").GetDouble(), x.GetProperty("x").GetInt32());
                var before = x.GetProperty("r").GetString() ?? ""; var scores = x.GetProperty("i").GetString() ?? "";
                if (before.Length == 0 || scores.Length == 0) return line;
                // the icon's verdict settled again with this version's rules, as the bank does (PopupIcons.Replay)
                var look = PopupIcons.Replay(items, before, scores);
                return line with { Text = look.Text, Before = before, IconScores = scores, Unsure = look.Unsure };
            }).ToList();
            s.ListGated(lines, t, s.Verdict("loot_list"));
            s.TableWitnessDue(t);
            last = t; n++;
        }
        if (last is { } end)
        {
            s.FlushVotes(end);
            s.GatherTick(end.AddSeconds(10));
            SignalsUntil(end.AddSeconds(60));
        }
        s.Instances.Close();
        Console.WriteLine($"Replayed {n} frames, {signals.Count(x => x.Kind == "zone")} zone reads from the log.");
        return landed;
    }

    static readonly Regex NotName = new(@"[^a-z0-9]", RegexOptions.Compiled);
    static string Norm(string name) => NotName.Replace(name.ToLowerInvariant().Replace("(bound)", ""), "");

    static Landed? Classify(GameEvent ev, ItemDictionary items) => ev switch
    {
        KinahChanged { Delta: > 0 } k => new Landed(ev.Time, "kina", $"kina|{k.Delta}", $"{k.Delta:N0} Kina" + (k.Bound ? " (bound)" : "")),
        ItemAcquired a => new Landed(ev.Time, "item", $"item|{Norm(items.NameOf(a.ItemId))}|{a.Count}", $"{items.NameOf(a.ItemId)} x{a.Count}" + (a.Bound ? " (bound)" : "")),
        RewardReceived rr => new Landed(ev.Time, "item", $"item|{Norm(rr.Name)}|{rr.Count}", $"{rr.Name} x{rr.Count} (reward)"),
        PetCrystal p => new Landed(ev.Time, "soul", "soul", $"{p.Name} x{p.Count}"),
        // an EnemySoul is the loot list's "Soul: ..." row marking a kill, not a pickup of its own: the crystal line is the pickup
        CurrencyGained c when c.Name.Contains("Soul", StringComparison.OrdinalIgnoreCase) => new Landed(ev.Time, "soul", "soul", $"{c.Amount} {c.Name}"),
        GatherFailed g => new Landed(ev.Time, "gatherfail", "gatherfail", $"{g.Skill} failed"),
        _ => null,
    };

    // ---- the truth list ------------------------------------------------------------------------------------------------

    static List<string> CsvFields(string line)
    {
        var f = new List<string>(); var cur = new System.Text.StringBuilder(); bool q = false;
        for (int i = 0; i < line.Length; i++)
        {
            char c = line[i];
            if (q) { if (c == '"' && i + 1 < line.Length && line[i + 1] == '"') { cur.Append('"'); i++; } else if (c == '"') q = false; else cur.Append(c); }
            else if (c == '"') q = true;
            else if (c == ',') { f.Add(cur.ToString()); cur.Clear(); }
            else cur.Append(c);
        }
        f.Add(cur.ToString());
        return f;
    }

    /// <summary>
    /// The footage's events from a truth CSV. Columns by name: clip_s, item, qty, and when present event/id, kind/source,
    /// result, verdict, notes. Lines starting with # are notes. Reward cards and HUD slots (kind card, reward_hud) aren't
    /// read from the chat, so they are left out; souls and gather fails are scored by kind, not by name.
    /// </summary>
    static List<Truth> LoadTruth(string path)
    {
        var rows = File.ReadLines(path).Where(l => l.Trim().Length > 0 && !l.TrimStart().StartsWith('#')).ToList();
        var head = CsvFields(rows[0]).Select(h => h.Trim().ToLowerInvariant()).ToList();
        int Col(params string[] names) { foreach (var n in names) { int i = head.IndexOf(n); if (i >= 0) return i; } return -1; }
        int cClip = Col("clip_s"), cItem = Col("item"), cQty = Col("qty"), cId = Col("event", "id"), cKind = Col("kind", "source"), cRes = Col("result"), cNote = Col("verdict", "notes");
        if (cClip < 0 || cItem < 0) throw new InvalidOperationException($"{path}: needs clip_s and item columns");
        var list = new List<Truth>();
        foreach (var row in rows.Skip(1))
        {
            var f = CsvFields(row);
            string F(int i) => i >= 0 && i < f.Count ? f[i].Trim() : "";
            if (!double.TryParse(Regex.Match(F(cClip), @"-?\d+(\.\d+)?").Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var clip)) continue;
            // a pickup from before the footage starts (its row already fading at frame 0) can't be read from it
            if (clip < 0) continue;
            string item = F(cItem), kind = F(cKind).ToLowerInvariant(), res = F(cRes).ToLowerInvariant(), id = F(cId);
            int qty = int.TryParse(Regex.Match(F(cQty), @"\d+").Value, out var qn) ? qn : 1;
            if (kind is "card" or "reward_hud") continue;
            // "Cogni Soul Crystals (row: Soul: Dratona (Bound))": the chat's name, then what the loot list showed
            var rowAt = item.IndexOf(" (row:", StringComparison.Ordinal);
            if (rowAt > 0) item = item[..rowAt];
            string cls, key;
            if (res == "fail" || item.Contains("Extraction failed", StringComparison.OrdinalIgnoreCase)) { cls = "gatherfail"; key = "gatherfail"; }
            // a soul crystal or soul points line, or the loot list's "Soul: ..." row; an item named after souls (Soul Codex) is an item
            else if (Regex.IsMatch(item, @"Soul (Crystal|Point)s?|^Soul:", RegexOptions.IgnoreCase)) { cls = "soul"; key = "soul"; }
            else if (item.Equals("Kina", StringComparison.OrdinalIgnoreCase) || item.Equals("Kinah", StringComparison.OrdinalIgnoreCase)) { cls = "kina"; key = $"kina|{qty}"; }
            else { cls = "item"; key = $"item|{Norm(item)}|{qty}"; }
            list.Add(new Truth(id, clip, cls, key, cls == "kina" ? $"Kina {qty:N0}" : cls == "gatherfail" ? item : $"{item} x{qty}", F(cNote)));
        }
        return list.OrderBy(t => t.Clip).ToList();
    }

    // ---- the score -----------------------------------------------------------------------------------------------------

    const double Before = 2, After = 20;

    static int Score(string run, List<Landed> landed, List<Truth> truth, DateTime start, Dictionary<string, string> o)
    {
        double Clip(DateTime at) => (at.ToLocalTime() - start).TotalSeconds;
        var open = landed.Select(l => (L: l, Clip: Clip(l.At))).OrderBy(x => x.Clip).ToList();
        var taken = new bool[open.Count];
        var matched = new List<(Truth T, int I)>(); var missed = new List<Truth>();
        foreach (var t in truth)
        {
            int best = -1;
            for (int i = 0; i < open.Count; i++)
                if (!taken[i] && open[i].L.Key == t.Key && open[i].Clip >= t.Clip - Before && open[i].Clip <= t.Clip + After) { best = i; break; }
            if (best < 0) missed.Add(t); else { taken[best] = true; matched.Add((t, best)); }
        }
        var extra = new List<(Landed L, double Clip, string Why)>();
        for (int i = 0; i < open.Count; i++)
        {
            if (taken[i]) continue;
            var twin = matched.FirstOrDefault(m => open[m.I].L.Key == open[i].L.Key && Math.Abs(open[m.I].Clip - open[i].Clip) <= After);
            extra.Add((open[i].L, open[i].Clip, twin.T != null ? $"double of {twin.T.Id} {twin.T.What} at {twin.T.Clip:0.0} s" : "nothing in the footage"));
        }
        var lat = matched.Select(m => open[m.I].Clip - m.T.Clip).OrderBy(x => x).ToList();
        string Pct(double p) => lat.Count == 0 ? "-" : lat[Math.Min(lat.Count - 1, (int)(p * lat.Count))].ToString("0.0");
        string Mix(IEnumerable<Truth> ts) => string.Join(", ", ts.GroupBy(t => t.Class).OrderByDescending(g => g.Count()).Select(g => $"{g.Count()} {g.Key}"));

        Console.WriteLine();
        Console.WriteLine($"bench {run}: {truth.Count} events in the footage ({Mix(truth)}), {landed.Count} counted");
        Console.WriteLine($"  matched {matched.Count} | missed {missed.Count} | extra {extra.Count} ({extra.Count(e => e.Why.StartsWith("double"))} doubles, {extra.Count(e => !e.Why.StartsWith("double"))} phantoms) | latency median {Pct(0.5)} s, p90 {Pct(0.9)} s");
        foreach (var cls in truth.Select(t => t.Class).Distinct())
            Console.WriteLine($"    {cls,-10} {matched.Count(m => m.T.Class == cls),3} of {truth.Count(t => t.Class == cls),3} matched");
        foreach (var t in missed) Console.WriteLine($"  missed  {t.Id,-4} {t.Clip,6:0.0} s  {t.What}" + (t.Note.Length > 0 ? $"   ({t.Note})" : ""));
        foreach (var e in extra) Console.WriteLine($"  extra        {e.Clip,6:0.0} s  {e.L.What}   ({e.Why})");

        // the baseline: a build must not miss or add more than the last recorded one did
        if (!o.TryGetValue("baseline", out var basePath)) { Console.WriteLine("  no --baseline given: scored only"); return 0; }
        var baseline = File.Exists(basePath) ? JsonSerializer.Deserialize<Dictionary<string, Dictionary<string, JsonElement>>>(File.ReadAllText(basePath)) ?? new() : new();
        var now = new Dictionary<string, JsonElement>
        {
            ["events"] = JsonSerializer.SerializeToElement(truth.Count), ["missed"] = JsonSerializer.SerializeToElement(missed.Count), ["extra"] = JsonSerializer.SerializeToElement(extra.Count),
            ["version"] = JsonSerializer.SerializeToElement(Upload.RunPayload.AppVersion), ["recorded"] = JsonSerializer.SerializeToElement(DateTime.Now.ToString("yyyy-MM-dd")),
        };
        if (o.ContainsKey("record") || !baseline.ContainsKey(run))
        {
            baseline[run] = now;
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(basePath))!);
            File.WriteAllText(basePath, JsonSerializer.Serialize(baseline, new JsonSerializerOptions { WriteIndented = true }));
            Console.WriteLine($"  RECORDED  baseline for {run}: missed {missed.Count}, extra {extra.Count} ({basePath})");
            return 0;
        }
        var b = baseline[run];
        int bm = b["missed"].GetInt32(), bx = b["extra"].GetInt32();
        bool worse = missed.Count > bm || extra.Count > bx, better = missed.Count < bm || extra.Count < bx;
        Console.WriteLine($"  baseline missed {bm}, extra {bx} (recorded with {b["version"]} on {b["recorded"]})");
        if (worse) { Console.WriteLine($"  FAIL      {run}: {(missed.Count > bm ? $"missed {missed.Count} > {bm}" : "")}{(missed.Count > bm && extra.Count > bx ? ", " : "")}{(extra.Count > bx ? $"extra {extra.Count} > {bx}" : "")}"); return 1; }
        Console.WriteLine(better ? $"  PASS      {run}: better than the baseline; --record to keep this as the new one" : $"  PASS      {run}: no regression");
        return 0;
    }
}
