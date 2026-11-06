using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using KinahLedger.Data;
using KinahLedger.Ocr;

namespace KinahLedger.Session;

/// <summary>
/// The live-log loop's miner (2026-11-06; tools/log-mine.md is the loop around it). The tracker's own logs from a day's play
/// hold every place it was unsure or overruled itself: a vote whose reads disagreed, a line rescued by no ordinary path, a
/// line read garbled and then clean, a line set aside as a reward or the inventory's, a loot-shaped line that made no event,
/// a line taken for a repeat, a read that couldn't be lined up. Each is a candidate for a parse-test: reads.log says what
/// the tracker decided, trace.log holds the raw chat reads it decided on. This command pairs them by wall time and prints
/// each spot with the frames around it rendered as a LineTracker block that pastes into Ocr/ChatCountTests.cs, with what
/// this build's LineTracker and ChatVoter make of those frames beside each read.
/// </summary>
public static class LogMine
{
    /// <summary>One line of reads.log: its local time, its marker (ok, --, ??), its text (a burst summary's count split off).</summary>
    sealed record Entry(DateTime At, string Mark, string Text, int Burst, string File);
    /// <summary>One chat read from trace.log (its lines as read, top to bottom), or a loot list read (its rows).</summary>
    sealed record Frame(DateTime At, string Kind, List<string> Lines);

    sealed class Spot
    {
        public string Kind = ""; public DateTime At; public DateTime Last; public readonly List<string> Why = new(); public bool Near;
    }

    public static readonly string[] Kinds = { "vote", "rescue", "garble", "aside", "noevent", "repeat", "align" };

    static readonly Regex Row = new(@"^(\d{2}):(\d{2}):(\d{2})\.(\d)  (ok|--|\?\?)  (.*)$", RegexOptions.Compiled);
    static readonly Regex BurstTail = new(@"^(.*?)  \(×(\d+) more, last at \d{2}:\d{2}:\d{2}\)$", RegexOptions.Compiled);

    public static int Run(Settings settings, List<string> pos, Dictionary<string, string> o)
    {
        var where = pos.FirstOrDefault() ?? CrashLog.Dir;
        List<string> readsFiles, traceFiles;
        if (Directory.Exists(where))
        {
            readsFiles = new[] { "reads.old.log", "reads.log" }.Select(f => Path.Combine(where, f)).Where(File.Exists).ToList();
            traceFiles = new[] { "trace.old.log", "trace.log" }.Select(f => Path.Combine(where, f)).Where(File.Exists).ToList();
        }
        else if (File.Exists(where))
        {
            readsFiles = new() { where };
            var dir = Path.GetDirectoryName(Path.GetFullPath(where))!;
            traceFiles = new[] { "trace.old.log", "trace.log" }.Select(f => Path.Combine(dir, f)).Where(File.Exists).ToList();
        }
        else { Console.Error.WriteLine($"no folder or reads.log at {where}"); return 1; }
        if (o.TryGetValue("trace", out var tr) && tr != "true") traceFiles = tr.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
        if (readsFiles.Count == 0) { Console.Error.WriteLine($"no reads.log in {where}"); return 1; }
        var lastDay = ReplaySignals.Date(o.GetValueOrDefault("date"));
        double before = Num(o, "before", 4), after = Num(o, "after", 2);
        int max = o.ContainsKey("all") ? int.MaxValue : (int)Num(o, "max", 20);
        TimeSpan? from = o.TryGetValue("from", out var f) && TimeSpan.TryParse(f, out var ft) ? ft : null;
        TimeSpan? to = o.TryGetValue("to", out var t0) && TimeSpan.TryParse(t0, out var tt) ? tt : null;
        var kinds = o.TryGetValue("kinds", out var kv) && kv != "true" ? kv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Select(k => k.ToLowerInvariant()).ToHashSet() : Kinds.ToHashSet();
        foreach (var k in kinds) if (!Kinds.Contains(k)) { Console.Error.WriteLine($"no spot kind \"{k}\"; the kinds are {string.Join(", ", Kinds)}"); return 1; }

        // this command only reads the player's logs; its own notes (a parser guess while it computes a meaning) go to a scratch folder
        CrashLog.DirOverride = TestTemp.Path("KinahLedger-log-mine");
        var items = ItemDictionary.LoadOrEmpty(settings.ItemsFile);
        var parser = new ChatParser(items);

        var entries = new List<Entry>();
        foreach (var rf in readsFiles) entries.AddRange(ReadLog(rf, rf == readsFiles[^1] ? lastDay : null));
        var frames = new List<Frame>();
        foreach (var tf in traceFiles) frames.AddRange(ReadTrace(tf));
        frames.Sort((a, b) => a.At.CompareTo(b.At));
        var chat = frames.Where(x => x.Kind == "chat").ToList();
        var lists = frames.Where(x => x.Kind == "list").ToList();

        var spots = FindSpots(entries, kinds);
        if (o.TryGetValue("at", out var at) && at != "true" && TimeSpan.TryParse(at, out var atT))
        {
            // a time of day with no note to trigger it: the frames there, on the last day the log read a chat line (--date names another)
            var day = lastDay ?? (entries.LastOrDefault(e => e.Mark != "??") ?? entries.LastOrDefault())?.At.Date ?? DateTime.Today;
            var s = new Spot { Kind = "at", At = day.Add(atT), Last = day.Add(atT) }; s.Why.Add($"the reads around {atT:hh\\:mm\\:ss} (--at)");
            spots.Insert(0, s);
        }
        if (from != null || to != null) spots = spots.Where(s => s.Kind == "at" || ((from == null || s.At.TimeOfDay >= from) && (to == null || s.At.TimeOfDay <= to))).ToList();

        var sb = new StringBuilder();
        string Span(IEnumerable<DateTime> ts) { var l = ts.ToList(); return l.Count == 0 ? "nothing" : $"{l.Min():yyyy-MM-dd HH:mm:ss} to {l.Max():yyyy-MM-dd HH:mm:ss}"; }
        sb.AppendLine($"# log-mine: {string.Join(" + ", readsFiles.Select(Path.GetFileName))} ({Span(entries.Select(e => e.At))}), {string.Join(" + ", traceFiles.Select(Path.GetFileName))} ({chat.Count:N0} chat reads, {lists.Count:N0} loot list reads, {Span(frames.Select(x => x.At))})");
        sb.AppendLine($"chat rows: {entries.Count(e => e.Mark == "ok"):N0} counted (ok) · {entries.Count(e => e.Mark == "--"):N0} without an event (--) · {entries.Count(e => e.Mark == "??"):N0} notes");
        var byKind = spots.Where(s => s.Kind != "at").GroupBy(s => s.Kind).ToDictionary(g => g.Key, g => g.ToList());
        sb.AppendLine("spots: " + string.Join(" · ", Kinds.Where(kinds.Contains).Select(k => $"{k} {(byKind.TryGetValue(k, out var l) ? l.Count : 0)}" + (k == "aside" && byKind.TryGetValue(k, out var al) ? $" ({al.Count(s => s.Near)} near a counted pickup of the same item)" : ""))));
        sb.AppendLine();
        sb.AppendLine("Each spot: the note(s) that made it, the reads.log lines around it, then the trace's chat reads from " + $"{before:0} s before to {after:0} s after as a LineTracker block.");
        sb.AppendLine("Beside each read: what a LineTracker fed these reads alone returns as new (the live one carried earlier state, so a difference is itself a clue),");
        sb.AppendLine("each new line's meaning for the vote ([k:463] Kina, [i:name xN] an item, [-] nothing), and what the ChatVoter lets through on that read.");
        sb.AppendLine("The Check pins what this build returns for the last read: confirm it against the game before keeping it, flip it when the log shows a miscount.");

        foreach (var kind in new[] { "at" }.Concat(Kinds))
        {
            var list = spots.Where(s => s.Kind == kind).ToList();
            if (list.Count == 0) continue;
            sb.AppendLine(); sb.AppendLine($"## {kind} ({list.Count}{(list.Count > max ? $", the first {max} shown; --all or --max for more" : "")})");
            sb.AppendLine(KindText(kind));
            int shown = 0;
            foreach (var s in list)
            {
                if (shown++ >= max) break;
                Render(sb, s, entries, chat, lists, parser, before, after);
            }
        }
        var text = sb.ToString();
        Console.Out.Write(text);
        if (o.TryGetValue("out", out var outPath) && outPath != "true") { File.WriteAllText(outPath, text, new UTF8Encoding(false)); Console.WriteLine($"(written to {outPath})"); }
        return 0;
    }

    static double Num(Dictionary<string, string> o, string key, double dflt) => o.TryGetValue(key, out var v) && double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) ? d : dflt;

    static string KindText(string kind) => kind switch
    {
        "vote" => "A line's reads disagreed and the vote chose; or a line was left to the vote by the settle or the two-witness path. A wrong winner is a miscount.",
        "rescue" => "A line counted by no ordinary read: a burst, the lines below the overlap, the settle after unaligned reads, the unaligned watch, a count settled later. Each is a place a line could be doubled or lost.",
        "garble" => "A line read garbled (while fading in, a digit slip, a guessed name) and then taken for a line: the garbled read and the clean one must be one pickup, and the name and count must be the right ones.",
        "aside" => "A line set aside from the count (a reward, the inventory at work, a box, an achievement, a run's). Set aside rightly it is not a pickup; a real drop set aside is a miss. 'near' marks a counted pickup of the same item within 10 s.",
        "noevent" => "A loot-shaped chat line that made no event, a name no list knows, or a loot list row the chat never showed: each may be a missed pickup, or rightly nothing (another player's loot, a skin).",
        "repeat" => "A line taken for a repeat and skipped, or a repeat counted as a second pickup: the place doubles and losses of identical lines come from.",
        "align" => "A read that couldn't be lined up with the one before (both reads follow), a read lined up on the last full read, a game window over the chat: where lines get lost or counted twice.",
        _ => "The reads around a time given on the command line.",
    };

    // ---- reads.log --------------------------------------------------------------------------------------------------

    /// <summary>
    /// The lines of one reads.log, dated as the replay dates them (ReplaySignals): the file keeps the time of day only, its
    /// days are told apart where the clock runs back by more than two hours, and the last day is the day the file was last
    /// written (or <paramref name="lastDay"/>).
    /// </summary>
    static List<Entry> ReadLog(string path, DateTime? lastDay)
    {
        var raw = new List<(TimeSpan T, string Mark, string Text, int Burst, int Day)>();
        int day = 0; TimeSpan prev = TimeSpan.Zero; bool any = false;
        foreach (var l in File.ReadLines(path))
        {
            var m = Row.Match(l);
            if (!m.Success) continue;
            var t = new TimeSpan(0, int.Parse(m.Groups[1].Value), int.Parse(m.Groups[2].Value), int.Parse(m.Groups[3].Value), int.Parse(m.Groups[4].Value) * 100);
            if (any && t < prev && (prev - t).TotalHours > 2) day++;
            prev = t; any = true;
            var text = m.Groups[6].Value; int burst = 0;
            if (m.Groups[5].Value == "??" && BurstTail.Match(text) is { Success: true } b) { text = b.Groups[1].Value; burst = int.Parse(b.Groups[2].Value); }
            raw.Add((t, m.Groups[5].Value, text, burst, day));
        }
        var last = (lastDay ?? File.GetLastWriteTime(path)).Date;
        var name = Path.GetFileName(path);
        return raw.Select(r => new Entry(DateTime.SpecifyKind(last.AddDays(r.Day - day).Add(r.T), DateTimeKind.Local), r.Mark, r.Text, r.Burst, name)).ToList();
    }

    // ---- trace.log --------------------------------------------------------------------------------------------------

    static IEnumerable<Frame> ReadTrace(string path)
    {
        foreach (var raw in File.ReadLines(path))
        {
            Frame? fr = null;
            try
            {
                using var doc = JsonDocument.Parse(raw);
                var r = doc.RootElement;
                var k = r.GetProperty("k").GetString() ?? "";
                if (k is not ("chat" or "list")) continue;
                var t = DateTime.Parse(r.GetProperty("t").GetString()!, null, DateTimeStyles.RoundtripKind).ToLocalTime();
                fr = new Frame(t, k, r.GetProperty("l").EnumerateArray().Select(x => x.GetString() ?? "").ToList());
            }
            catch (Exception) { /* a torn line at the end of a file being written */ }
            if (fr != null) yield return fr;
        }
    }

    // ---- the spots --------------------------------------------------------------------------------------------------

    /// <summary>Every note or row the chat path writes where the tracker was unsure or overruled itself, by kind, in the order Session/TrackerSession.cs, ChatVoter.cs, ChatRepeat.cs and Ocr/ChatParser.cs write them.</summary>
    static readonly (string Kind, Regex Rx)[] Notes =
    {
        // (a) the vote
        ("vote", new Regex(@"^voted over \d+ reads: ", RegexOptions.Compiled)),
        ("vote", new Regex(@": first on screen after the reads lined up again, left to the vote$", RegexOptions.Compiled)),
        ("vote", new Regex(@": read whole, the loot list's row waiting, but its vote is still open", RegexOptions.Compiled)),
        // (f) repeats, before the rescues: "counted already when the chat settled" is a skip, not a settle
        ("repeat", new Regex(@"^chat: '.*' seen again after a scroll: not counted again", RegexOptions.Compiled)),
        ("repeat", new Regex(@"^chat: '.*' again: counted, ", RegexOptions.Compiled)),
        ("repeat", new Regex(@": counted already when the chat settled, not again$", RegexOptions.Compiled)),
        ("repeat", new Regex(@": below the overlap of a read that didn't line up, but counted .*: the same line, not again$", RegexOptions.Compiled)),
        ("repeat", new Regex(@"^a chat line for a pickup the loot list already added came late: counted once$", RegexOptions.Compiled)),
        ("repeat", new Regex(@"^a chat line for .*, which the loot list showed but couldn't count alone, came late", RegexOptions.Compiled)),
        ("repeat", new Regex(@"^the chat read .* Kina again within .* s and the loot list showed it", RegexOptions.Compiled)),
        ("repeat", new Regex(@"the same drop, counted once$", RegexOptions.Compiled)),
        ("repeat", new Regex(@": counted already from the Kinah counter's rise before this line was read: once$", RegexOptions.Compiled)),
        // (b) rescues: a line counted by no ordinary read
        ("rescue", new Regex(@"^a burst of \d+ loot line\(s\) arrived faster than the chat could show", RegexOptions.Compiled)),
        ("rescue", new Regex(@"^counted \d+ loot line\(s\) below the last line this read shares with the last one", RegexOptions.Compiled)),
        ("rescue", new Regex(@"^the chat settled after \d+ s of reads that didn't line up", RegexOptions.Compiled)),
        ("rescue", new Regex(@"^the settled lines came with an achievement", RegexOptions.Compiled)),
        ("rescue", new Regex(@": quantity settled x\d+ \(was 1 assumed\)", RegexOptions.Compiled)),
        ("rescue", new Regex(@": quantity unsure, 1 assumed", RegexOptions.Compiled)),
        ("rescue", new Regex(@": its count read twice in later reads as x1", RegexOptions.Compiled)),
        ("rescue", new Regex(@"and the Kinah counter rose by .*: counted \(unaligned\)$", RegexOptions.Compiled)),
        ("rescue", new Regex(@"^the Kinah counter rose .* that no chat line claimed", RegexOptions.Compiled)),
        // (c) garbles: a line read wrong and taken for the right one
        ("garble", new Regex(@": its count read through the recognizer's digit slips as x\d+$", RegexOptions.Compiled)),
        ("garble", new Regex(@": the chat line lost its count; the loot list's row says x\d+$", RegexOptions.Compiled)),
        ("garble", new Regex(@"^guessed "".*"" as .* \(closest match", RegexOptions.Compiled)),
        ("garble", new Regex(@"^"".*"" is .*, not .*: .* (says so|has it)$", RegexOptions.Compiled)),
        ("garble", new Regex(@"^"".*"" only comes bound: its ""\(Bound\)"" was lost from the line$", RegexOptions.Compiled)),
        // (e) no event: a line or a row that counted nothing, or a name no list knows
        ("noevent", new Regex(@"^not in the item list, (not counted|counted for the run by its name): ", RegexOptions.Compiled)),
        ("noevent", new Regex(@"^not a pet on the list, counted without a name: ", RegexOptions.Compiled)),
        ("noevent", new Regex(@"^rare drop only the loot list showed, and the second read didn't confirm it: not counted", RegexOptions.Compiled)),
        ("noevent", new Regex(@"^guessed "".*"" as .*, but .* doesn't drop it: not counted from the chat", RegexOptions.Compiled)),
        ("noevent", new Regex(@"^"".*"" is nearest .*: unsure, not counted$", RegexOptions.Compiled)),
        ("noevent", new Regex(@": the Kinah counter didn't rise by .* for it .*: not counted$", RegexOptions.Compiled)),
        ("noevent", new Regex(@"^the loot list showed .* and the chat read missed it", RegexOptions.Compiled)),
        ("noevent", new Regex(@"^the loot list showed .* in one read only and the chat didn't: not counted$", RegexOptions.Compiled)),
        ("noevent", new Regex(@"^gather: .* from the chat .*: .*no attempt$", RegexOptions.Compiled)),
        // (d) set aside: a reward, the inventory, a box, a run's, a base's
        ("aside", new Regex(@"^the inventory at work \("".*""\): what prints with it is not a drop$", RegexOptions.Compiled)),
        ("aside", new Regex(@"^a box \("".*""\): what prints with it is its contents, not drops$", RegexOptions.Compiled)),
        ("aside", new Regex(@"^a Force is handing out loot", RegexOptions.Compiled)),
        ("aside", new Regex(@"^achievement: .*; its rewards are not drops$", RegexOptions.Compiled)),
        ("aside", new Regex(@"[,;] not a drop$", RegexOptions.Compiled)),
        ("aside", new Regex(@", not counted with the kills$", RegexOptions.Compiled)),
        ("aside", new Regex(@" window open, but a kill came just before it: counted with the kills$", RegexOptions.Compiled)),
        ("aside", new Regex(@", but the loot list shows a row of it: a pickup$", RegexOptions.Compiled)),
        ("aside", new Regex(@"^\S.* Kina in a base \(.*\): a reward, a sale or the mail, never farming$", RegexOptions.Compiled)),
        ("aside", new Regex(@"^the Kinah counter rose .* in a base \(", RegexOptions.Compiled)),
        ("aside", new Regex(@": the gathering panel or its bar just produced it, so a gather, not ", RegexOptions.Compiled)),
        ("aside", new Regex(@": in the minute after a reward screen, but its line came after the screen", RegexOptions.Compiled)),
        ("aside", new Regex(@"^world boss fight: ", RegexOptions.Compiled)),
        ("aside", new Regex(@": dated \d{2}:\d{2}:\d{2}, inside the .* run before its room was left: that run's, not counted, not farming$", RegexOptions.Compiled)),
        ("aside", new Regex(@": gathered here, counted with gathering, not the run's loot$", RegexOptions.Compiled)),
        ("aside", new Regex(@": the .* screen's reward, not the run's$", RegexOptions.Compiled)),
        ("aside", new Regex(@" Kina in the chat was the reward table's, already counted$", RegexOptions.Compiled)),
        ("aside", new Regex(@"^Kina x\d+ as an item \(", RegexOptions.Compiled)),
        // (h) alignment
        ("align", new Regex(@"^couldn't line this read up with the last one", RegexOptions.Compiled)),
        ("align", new Regex(@"^the last read was a stray line, so this one was lined up with the last full read of the chat", RegexOptions.Compiled)),
        ("align", new Regex(@"^a game window over the chat \(numbers, no line the chat prints\)", RegexOptions.Compiled)),
    };

    /// <summary>The suffixes a chat row (ok or --) carries when a path other than the plain read made it (CrashLog.Read calls in TrackerSession.cs).</summary>
    static readonly (string Kind, string Tail)[] RowTails =
    {
        ("garble", "(read again: it was garbled while fading in)"),
        ("garble", "(after a fade)"),
        ("rescue", "(counted once the chat settled)"),
        ("rescue", "(read whole but taken by no path; the loot list's row for it is the second witness)"),
        ("rescue", "(between lines already seen: no read had shown it)"),
        ("repeat", "(counted already in this read, not again)"),
    };
    static readonly Regex UnalignedTail = new(@"    \(unaligned: first shown by a read that didn't line up, read the same \d+ times, never counted\)$", RegexOptions.Compiled);
    static readonly Regex CountTail = new(@"    \(the count the first read of "".*"" lost: x\d+, \d+ more counted\)$", RegexOptions.Compiled);
    /// <summary>The item an aside note names: "Enhance Stone x2750: the inventory at work ..." or the line quoted in "the inventory at work ("...")".</summary>
    static readonly Regex AsideItem = new(@"^(?:the inventory at work|a box) \(""(?<line>.*)""\)|^(?<name>[^:""]+?) x[\d,]+: ", RegexOptions.Compiled);

    static (string Kind, string Why)? Classify(Entry e)
    {
        if (e.Mark == "??")
        {
            foreach (var (kind, rx) in Notes) if (rx.IsMatch(e.Text)) return (kind, e.Text);
            return null;
        }
        // a chat row: its suffix says which path made it
        foreach (var (kind, tail) in RowTails) if (e.Text.EndsWith("    " + tail, StringComparison.Ordinal)) return (kind, $"{e.Mark}  {e.Text}");
        if (UnalignedTail.IsMatch(e.Text) || CountTail.IsMatch(e.Text)) return ("rescue", $"{e.Mark}  {e.Text}");
        // a plain row with no event, shaped like loot and not another player's: a pickup the parser didn't take, or rightly nothing
        if (e.Mark == "--" && !e.Text.Contains("    (", StringComparison.Ordinal) && !PickupCheck.PlayerChat(e.Text) && Reread.LooksLikeLoot(e.Text)
            && !ChatParser.SomeoneElses(ChatParser.CleanSystemLine(e.Text))) return ("noevent", $"--  {e.Text}");
        return null;
    }

    static List<Spot> FindSpots(List<Entry> entries, HashSet<string> kinds)
    {
        var spots = new List<Spot>();
        var counted = entries.Where(e => e.Mark == "ok").ToList();
        Spot? last = null;
        foreach (var e in entries)
        {
            if (Classify(e) is not { } c || !kinds.Contains(c.Kind)) continue;
            // the same kind within a second of the last spot of it: one spot (a burst and the settle that followed it, a vote and its row)
            if (last != null && last.Kind == c.Kind && (e.At - last.Last).TotalSeconds <= 1.0) { last.Last = e.At; last.Why.Add(c.Why); continue; }
            var s = new Spot { Kind = c.Kind, At = e.At, Last = e.At };
            s.Why.Add(c.Why);
            if (c.Kind == "aside" && AsideItem.Match(e.Text) is { Success: true } am)
            {
                // the same item counted as a pickup within 10 s: the line set aside may be a real drop (or the counted one the reward).
                // The set-aside line's own row is written ok too (a reward is an event), in the same tenth of a second: not that one
                var name = am.Groups["name"].Success ? am.Groups["name"].Value.Trim() : ItemOf(am.Groups["line"].Value);
                if (name.Length >= 3)
                {
                    var near = counted.FirstOrDefault(k => Math.Abs((k.At - e.At).TotalSeconds) is <= 10 and > 0.3 && k.Text.Contains(name, StringComparison.OrdinalIgnoreCase));
                    if (near != null) { s.Near = true; s.Why.Add($"near: a counted pickup of the same item at {near.At:HH:mm:ss.f}: {near.Text}"); }
                }
            }
            spots.Add(s); last = s;
        }
        return spots;
    }

    /// <summary>The item in a quoted chat line: "Nameless Master (Longsword) Skin acquired." → "Nameless Master (Longsword) Skin"; "Acquired Odyle x2." → "Odyle".</summary>
    static string ItemOf(string line)
    {
        var m = Regex.Match(line, @"^Acquired (?<n>.+?)(?: x[\d,]+)?\.?$|^(?<n>.+?) acquired\.?$", RegexOptions.IgnoreCase);
        return m.Success ? m.Groups["n"].Value.Trim() : "";
    }

    // ---- the report -------------------------------------------------------------------------------------------------

    /// <summary>Notes that place the tracker or measure it, not about a chat line: left out of a spot's log context.</summary>
    static readonly string[] Noise = { "audit:", "anchor", "scene", "decode ", "balance strip", "zone read:", "zone:", "target bar", "event cards", "activity:", "fighting:", "Kinah counter search", "reads.log turned over", "loot list: static text", "loot list: \"", "node:", "gather attempt at", "the gathering panel shows", "gathering bar:", "reward table" };

    static void Render(StringBuilder sb, Spot s, List<Entry> entries, List<Frame> chat, List<Frame> lists, ChatParser parser, double before, double after)
    {
        sb.AppendLine();
        sb.AppendLine($"### {s.At:yyyy-MM-dd HH:mm:ss.f}  {s.Kind}" + (s.Near ? "  (near a counted pickup)" : ""));
        foreach (var w in s.Why) sb.AppendLine("  " + w);
        var ctx = entries.Where(e => e.At >= s.At.AddSeconds(-1.5) && e.At <= s.Last.AddSeconds(1.5) && !s.Why.Contains(e.Mark == "??" ? e.Text : $"{e.Mark}  {e.Text}") && !Noise.Any(n => e.Text.StartsWith(n, StringComparison.Ordinal))).Take(30).ToList();
        if (ctx.Count > 0)
        {
            sb.AppendLine("  log:");
            foreach (var e in ctx) sb.AppendLine($"    {e.At:HH:mm:ss.f}  {e.Mark}  {e.Text}" + (e.Burst > 0 ? $"  (×{e.Burst} more)" : ""));
        }
        var lo = s.At.AddSeconds(-before); var hi = s.Last.AddSeconds(after);
        var reads = chat.Where(x => x.At >= lo && x.At <= hi).ToList();
        var rows = lists.Where(x => x.At >= lo && x.At <= hi).ToList();
        if (reads.Count == 0)
        {
            sb.AppendLine(chat.Count == 0 ? "  (no trace.log given, so no reads)" : $"  (no chat reads in the trace between {lo:HH:mm:ss.f} and {hi:HH:mm:ss.f}: the trace for that stretch is gone, or the tracker read nothing then)");
            return;
        }
        if (rows.Count > 0) sb.AppendLine("  loot list reads: " + string.Join("  ·  ", rows.Take(12).Select(r => $"{r.At:HH:mm:ss.f} {string.Join(" | ", r.Lines)}")) + (rows.Count > 12 ? $"  · and {rows.Count - 12} more" : ""));
        sb.AppendLine($"  chat reads ({reads.Count}, {reads[0].At:HH:mm:ss.f} to {reads[^1].At:HH:mm:ss.f}), as a LineTracker test:");
        sb.AppendLine("```csharp");
        var lt = new LineTracker(); var voter = new ChatVoter();
        string Name = Short($"{s.At:HH:mm:ss} {s.Kind}: {s.Why[0]}", 70);
        sb.AppendLine($"            // log-mine {s.At:yyyy-MM-dd HH:mm:ss.f} {s.Kind}: {s.Why[0]}");
        sb.AppendLine("            var lt = new LineTracker();");
        for (int i = 0; i < reads.Count; i++)
        {
            var r = reads[i];
            var fresh = lt.NewLines(new List<string>(r.Lines));
            var voted = voter.Push(fresh, r.Lines, r.At.ToUniversalTime(), parser.Meaning);
            var flags = (lt.LastAfterFade ? " after a fade" : "") + (lt.LastFromAnchor ? " from the anchor" : "") + (lt.LastPanel ? " a panel" : "");
            var got = fresh.Count == 0 ? (i == 0 ? "(history)" : "(nothing new)") : string.Join(" | ", fresh.Select(l => $"\"{l}\" [{Meaning(parser, l)}]"));
            var outp = voted.Count == 0 ? "" : "   voted out: " + string.Join(" | ", voted.Select(l => $"\"{l}\""));
            var call = $"lt.NewLines(new List<string> {{ {string.Join(", ", r.Lines.Select(Lit))} }})";
            if (i < reads.Count - 1) sb.AppendLine($"            {call};   // {r.At:HH:mm:ss.f} -> {got}{flags}{outp}");
            else sb.AppendLine($"            Check({Lit(Name)}, string.Join(\" | \", {call}), {Lit(string.Join(" | ", fresh))});   // {r.At:HH:mm:ss.f} -> {got}{flags}{outp}");
        }
        var left = voter.Flush(parser.Meaning);
        if (left.Count > 0) sb.AppendLine($"            // still waiting for their vote at the end: {string.Join(" | ", left.Select(l => $"\"{l}\""))}");
        sb.AppendLine("```");
    }

    static string Meaning(ChatParser p, string line) { var m = p.Meaning(line); return m.Key.Length == 0 ? "-" : m.Key + (m.Exact ? "" : " ~"); }

    // ---- parse-test ---------------------------------------------------------------------------------------------------

    /// <summary>
    /// parse-test's checks: one reads.log line of each note the chat path writes lands in its kind (so a reworded note in
    /// TrackerSession.cs fails here, not silently in the report), a burst summary classifies as its note, the rows that are
    /// no spot stay none, and a log that crosses midnight is dated on both sides of it.
    /// </summary>
    public static bool SelfTest()
    {
        bool ok = true;
        void Check(string what, string got, string expect)
        {
            bool pass = got == expect; ok &= pass;
            Console.WriteLine($"  {what,-62} -> {got,-40} {(pass ? "ok" : "MISMATCH, expected " + expect)}");
        }
        Console.WriteLine("\nlog-mine (2026-11-06): the notes of the chat path sorted into spot kinds, and reads.log dated across midnight:");
        var t = new DateTime(2026, 11, 5, 15, 0, 0, DateTimeKind.Local);
        string K(string mark, string text) => Classify(new Entry(t, mark, text, 0, "reads.log"))?.Kind ?? "none";
        Check("vote: voted over N reads", K("??", "voted over 3 reads: \"Acquired Enhance Stone x1 ,420.\" is \"Acquired Enhance Stone x1,420.\""), "vote");
        Check("vote: left to the vote after the reads lined up", K("??", "Acquired Enhance Stone x140.: first on screen after the reads lined up again, left to the vote"), "vote");
        Check("rescue: a burst", K("??", "a burst of 9 loot line(s) arrived faster than the chat could show; counted from the read anyway"), "rescue");
        Check("rescue: the settle", K("??", "the chat settled after 5 s of reads that didn't line up; its loot lines counted from the clean read: Acquired Silentium (Bound) x14."), "rescue");
        Check("rescue: below the overlap", K("??", "counted 2 loot line(s) below the last line this read shares with the last one: squired | cquired"), "rescue");
        Check("rescue: a count settled later", K("??", "Refining Stone: quantity settled x4 (was 1 assumed), read twice at 15:40:00.2 and 15:40:00.7"), "rescue");
        Check("rescue: the row of a settled line", K("ok", "Acquired Silentium (Bound) x14.    (counted once the chat settled)"), "rescue");
        Check("rescue: the row of the unaligned watch", K("ok", "Vagabond Boots acquired.    (unaligned: first shown by a read that didn't line up, read the same 3 times, never counted)"), "rescue");
        Check("rescue: the row of a count the first read lost", K("ok", "Acquired Refining Stone x4.    (the count the first read of \"Acquired Refining Stone\" lost: x4, 3 more counted)"), "rescue");
        Check("garble: read again while fading in", K("ok", "Corrupted Judicator Necklace acquired.    (read again: it was garbled while fading in)"), "garble");
        Check("garble: a digit slip", K("??", "\"Enhance Stone x2,750\": its count read through the recognizer's digit slips as x2750"), "garble");
        Check("garble: a guessed name", K("??", "guessed \"Balaurls Essence\" as Balaur's Essence (closest match)"), "garble");
        Check("garble: the row's count from the list", K("??", "Orichalcum Ore: the chat line lost its count; the loot list's row says x2"), "garble");
        Check("aside: the inventory at work", K("??", "the inventory at work (\"Nameless Master (Longsword) Skin acquired.\"): what prints with it is not a drop"), "aside");
        Check("aside: not a drop", K("??", "Enhance Stone x2750: the inventory at work (storage, Quick Register, the wardrobe), not a drop"), "aside");
        Check("aside: a box", K("??", "a box (\"Customization Chest (Bound) acquired.\"): what prints with it is its contents, not drops"), "aside");
        Check("aside: an achievement", K("??", "achievement: completed.; its rewards are not drops"), "aside");
        Check("aside: a gather, not the inventory", K("??", "Odyle x1: the gathering panel or its bar just produced it, so a gather, not the inventory at work (storage, Quick Register, the wardrobe)"), "aside");
        Check("aside: no kill or gather near it", K("??", "Silentium (Bound) x1: no kill or gather within 6 s of it, so a claim, a box or the inventory; not a drop"), "aside");
        Check("noevent: not in the item list", K("??", "not in the item list, not counted: McKenna joined the room. Orichalcum Ore"), "noevent");
        Check("noevent: the list saw it, the chat didn't", K("??", "the loot list showed Odyle x1 twice and the chat read missed it: one witness, the caller decides (it counts only with a second)"), "noevent");
        Check("noevent: a loot-shaped row with no event", K("--", "Acquired Refining Stone"), "noevent");
        Check("repeat: seen again after a scroll", K("??", "chat: 'Acquired Fine Odyle' seen again after a scroll: not counted again (counted at 14:35:13, and no new loot list row, gathering race or kill for a second one)"), "repeat");
        Check("repeat: a second copy counted", K("??", "chat: 'Vagabond Cloak acquired.' again: counted, the read shows 2 such lines"), "repeat");
        Check("repeat: counted already when the chat settled", K("??", "Acquired Fresh Raw Meat x 100.: counted already when the chat settled, not again"), "repeat");
        Check("repeat: a late chat line for a listed pickup", K("??", "a chat line for odylex1, which the loot list showed but couldn't count alone, came late: counted now, the list and the chat are its two witnesses"), "repeat");
        Check("align: couldn't line up", K("??", "couldn't line this read up with the last one, so only a burst or the lines after the overlap can count from it (10 lines; last: [Delust6nal] sorry)"), "align");
        Check("align: lined up on the last full read", K("??", "the last read was a stray line, so this one was lined up with the last full read of the chat (2 new)"), "align");
        Check("align: a game window over the chat", K("??", "a game window over the chat (numbers, no line the chat prints): not a read of the chat, skipped"), "align");
        Check("no spot: a player's line", K("--", "[Delusional] Do yall shave?"), "none");
        Check("no spot: another player's loot", K("--", "TillyT acquired Soul: Superior Fire Spirit"), "none");
        Check("no spot: a plain counted row", K("ok", "Odyle acquired."), "none");
        Check("no spot: the audit", K("??", "audit: last 60 s · kills counted +0"), "none");
        // a burst summary is its note with the count split off
        var dir = TestTemp.Path("KinahLedger-log-mine-test");
        Directory.CreateDirectory(dir);
        var log = Path.Combine(dir, "reads.log");
        File.WriteAllText(log, "23:59:59.5  ??  a game window over the chat (numbers, no line the chat prints): not a read of the chat, skipped  (×13 more, last at 23:58:10)\n00:00:01.0  ok  Odyle acquired.\n00:00:02.5  --  binding\n", new UTF8Encoding(false));
        var es = ReadLog(log, new DateTime(2026, 11, 6));
        Check("a burst summary: its note, and the count", es.Count > 0 ? $"{Classify(es[0])?.Kind} ×{es[0].Burst}" : "no lines", "align ×13");
        Check("dated across midnight: the day before, then the last day", es.Count == 3 ? $"{es[0].At:MM-dd HH:mm:ss.f} | {es[1].At:MM-dd HH:mm:ss.f} | {es[2].Mark}" : $"{es.Count} lines", "11-05 23:59:59.5 | 11-06 00:00:01.0 | --");
        Check("a literal for the test block", Lit("say \"hi\"\\there"), "\"say \\\"hi\\\"\\\\there\"");
        return ok;
    }

    static string Short(string s, int max) => s.Length <= max ? s : s[..(max - 1)] + "…";

    /// <summary>A C# string literal for a line as read, so the block pastes into ChatCountTests.cs as it is.</summary>
    static string Lit(string s)
    {
        var sb = new StringBuilder("\"");
        foreach (var c in s)
        {
            switch (c)
            {
                case '\\': sb.Append("\\\\"); break;
                case '"': sb.Append("\\\""); break;
                case '\t': sb.Append("\\t"); break;
                case '\r': sb.Append("\\r"); break;
                case '\n': sb.Append("\\n"); break;
                default: if (c < ' ') sb.Append($"\\u{(int)c:x4}"); else sb.Append(c); break;
            }
        }
        return sb.Append('"').ToString();
    }
}
