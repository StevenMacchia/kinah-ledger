using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using KinahLedger.Data;
using KinahLedger.Ocr;

namespace KinahLedger.Session;

/// <summary>
/// The one-time re-count of old reward tables (0.10.61). Until 0.10.54 (PR 191) the expedition reward-table reader often
/// took rows of the floor under a table for slots: on 2026-11-11 every Urugugu Canyon run got about 24 gear, a "Unique
/// Tranquility Bow" among them that Steven never got, and Auldor's Sanctum tables read 36 to 48 slots for 25 to 32. The
/// fixed reader reads 206 of 208 hand-counted tables exactly and never too many (kinah-bench\reward-tables). Every
/// expedition run's table was kept as a picture (reward-shots, "-reward-table.jpg" and "-reward-table-settled.jpg"),
/// so the runs counted before the fix are read again from those pictures with this reader, the way the live read filed
/// them (both frames voted slot by slot, ApplyGrid, the chat's lines kept on the run judged against it).
/// <para>A run takes the new read only when it is surely better: it read a table, its coin agrees with the run's Kinah
/// (the run's Kinah stays as it was), it has no more slots than the run had, and it leaves no more slots unnamed. Its
/// items, bound items and slots not read then become the new read's; anything else (a run with no picture, two tables, a
/// read that names less) is left alone. Under-counting is far better than phantom loot. The counts before go into
/// <see cref="InstanceRun.RepairedFrom"/> and a run the site has is sent again as a correction (the gear repair's way,
/// the site's corrections.ts), so the site takes the phantom gear back from the area's and the profile's rows.</para>
/// <para>Run once at a start (Ui/MainWindow.Recount.cs, flag <see cref="Characters.Recounted"/>), with each changed file
/// copied to <see cref="BackupName"/> beside it first; and by hand as <c>recount-runs &lt;data dir&gt; [--dry-run]</c>.</para>
/// </summary>
public static class RunRecount
{
    /// <summary>When 0.10.54, the fixed reader, was released: tables read before were read by the old one.</summary>
    public static readonly DateTime FixedReaderAt = new(2026, 11, 12, 3, 6, 47, DateTimeKind.Utc);

    /// <summary>The copy of a runs file kept beside it before the re-count first writes it.</summary>
    public const string BackupName = "instances.pre-recount.jsonl";

    /// <summary>The first picture of a table is kept this soon after its read began (the read takes a second or two).</summary>
    public const double FirstBeforeSeconds = 3, FirstAfterSeconds = 10;
    /// <summary>The settled picture follows the first within this long (the re-reads stop after 3 s).</summary>
    public const double SettledSeconds = 8;

    static readonly Regex TableShot = new(@"^(\d{8}-\d{6})-.+?-reward-table(-settled)?\.jpg$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>A kept table picture: its file name, when it was taken (UTC, from the name's local time), and whether it is the settled frame.</summary>
    public sealed record Picture(string Name, DateTime T, bool Settled);

    public static Picture? Parse(string name)
    {
        var m = TableShot.Match(Path.GetFileName(name));
        if (!m.Success || !DateTime.TryParseExact(m.Groups[1].Value, "yyyyMMdd-HHmmss", CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var t)) return null;
        return new Picture(Path.GetFileName(name), t.ToUniversalTime(), m.Groups[2].Success);
    }

    /// <summary>Every kept table picture in <paramref name="dir"/>, oldest first.</summary>
    public static List<Picture> PicturesIn(string dir) =>
        Directory.Exists(dir) ? Directory.GetFiles(dir, "*-reward-table*.jpg").Select(Parse).Where(p => p != null).Select(p => p!).OrderBy(p => p.T).ToList() : new();

    /// <summary>
    /// The run's table pictures: the one first picture taken from <see cref="FirstBeforeSeconds"/> before its table's read to
    /// <see cref="FirstAfterSeconds"/> after, then the settled one after it within <see cref="SettledSeconds"/>. Empty when
    /// there is none, or two first pictures (two tables in that window: which one was the run's isn't sure).
    /// </summary>
    public static List<Picture> PicturesFor(InstanceRun r, IReadOnlyList<Picture> all)
    {
        if (r.TableAt is not DateTime at) return new();
        var first = all.Where(p => !p.Settled && (p.T - at).TotalSeconds is >= -FirstBeforeSeconds and <= FirstAfterSeconds).ToList();
        if (first.Count != 1) return new();
        var f = first[0];
        var settled = all.FirstOrDefault(p => p.Settled && (p.T - f.T).TotalSeconds is >= 0 and <= SettledSeconds);
        return settled != null ? new() { f, settled } : new() { f };
    }

    /// <summary>A run the re-count looks at: an expedition's (or Transcendence's) one table, read from its picture before the fix, never touched by a repair since.</summary>
    public static string? NotEligible(InstanceRun r, DateTime before)
    {
        if (r.Kind is not ("Expedition" or InstanceLog.Transcendence)) return "not an expedition";
        if (!r.FromPicture || r.RewardsNotRead) return "not counted from a reward table";
        if (r.Tables != 1 || r.TableAt is not DateTime at) return "not one table";
        if (at >= before) return "read by the fixed reader";
        if (r.Recounted != null) return "counted again already";
        if (r.GearRepaired != null || r.Resend || r.RepairedFrom.Count > 0) return "repaired before, its correction kept as it is";
        return null;
    }

    // ---- the read ----

    /// <summary>
    /// Reads a run's kept table pictures into a copy of it, the way the live read files a table: each frame's slots under
    /// its own "Reward collected." line, voted slot by slot, the unread ones filled from the other frame, through ApplyGrid,
    /// then the chat lines kept on the run judged against the slots. The Kinah counter is taken to have risen by the run's
    /// Kinah at its table, so a coin read within 1% of it is confirmed as it was live; any other coin is not. Notes go to
    /// <paramref name="notes"/>, never reads.log. <paramref name="history"/>: the same place's runs, which settle a close
    /// call between two items as live. Null: no frame showed the table's header.
    /// </summary>
    public static async Task<InstanceRun?> ReadAsync(InstanceRun run, IReadOnlyList<string> paths, Settings settings, ItemDictionary items, string server,
        IEnumerable<InstanceRun> history, List<string> notes)
    {
        using var quiet = CrashLog.CaptureNotes(notes);
        var at = run.TableAt ?? run.PaidAt ?? run.Start;
        var reads = new List<List<RewardGrid.Slot>>();
        foreach (var p in paths)
        {
            using var bmp = await ScreenReader.LoadAsync(p);
            if (await TopOf(bmp) is not int top) { notes.Add($"recount: {Path.GetFileName(p)}: no \"Reward collected.\" line read"); continue; }
            reads.Add(await RewardGrid.ReadAsync(bmp, top));
        }
        if (reads.Count == 0) return null;
        var session = new TrackerSession(null, settings, items, "Recovery") { Server = server };
        session.Instances.UseKept(history);
        var copy = run.Clone();
        copy.Items.Clear(); copy.BoundItems.Clear(); copy.Bound = 0; copy.Kinah = 0; copy.FromPicture = false; copy.SlotsUnread = 0; copy.UnknownSlots.Clear();
        copy.KinahNotRead = false; copy.PartlyRead = false; copy.UnknownGear = 0; copy.UnknownItems = 0; copy.GearNotes.Clear(); copy.PictureSlots = 0;
        copy.SlotDoubts.Clear(); copy.PictureCounts.Clear(); copy.CardsReceived = ""; copy.Tables = 0; copy.TableAt = null; copy.TableChat = new();
        session.Engine.SetZone(run.Zone); session.ZoneView.SetZone(run.Zone);
        session.Instances.Adopt(copy);
        if (run.Kinah > 0) { const long b = 1_000_000_000; session.SeedBalance(at.AddSeconds(-5), b); session.SeedBalance(at.AddSeconds(1), b + run.Kinah); }
        var slots = RewardGrid.Vote(reads, TrackerSession.GridSureDistance);
        slots = RewardGrid.FillUnread(slots, reads, TrackerSession.GridSureDistance, out var filled);
        foreach (var f in filled) CrashLog.Note($"reward table: {f}");
        session.GridShot = Path.GetFileName(paths[^1]);
        session.ApplyGrid(slots, at);
        session.TableWitnessDue(at.AddSeconds(5), force: true, kept: run.TableChat);
        return session.Instances.Release();
    }

    /// <summary>Where a frame's table starts: under its centred "Reward collected." line, read at the reader's scale first, then larger and boosted (a header over a glare).</summary>
    static async Task<int?> TopOf(System.Drawing.Bitmap frame)
    {
        var band = new System.Drawing.Rectangle(frame.Width / 4, 0, frame.Width / 2, frame.Height / 3);
        using var bb = frame.Clone(band, frame.PixelFormat);
        foreach (var (sc, boost) in new[] { (1, false), (2, false), (1, true), (2, true) })
            if (TrackerSession.CollectedLine(await ScreenReader.ReadBoxesAsync(bb, band.Location, sc, boost), frame.Width) is { } rc)
                return rc.Box.Bottom + (int)(frame.Height * 0.016);
        return null;
    }

    // ---- the verdict ----

    /// <summary>What a read of the pictures is against the run as it stands: taken or not, why, and the slots and gear before and after.</summary>
    public sealed record Verdict(bool Take, bool Same, string Why, int SlotsBefore, int SlotsAfter, int GearBefore, int GearAfter);

    /// <summary>
    /// A run's slots as filed: each piece of gear one slot (gear never stacks), every other item one slot a stack (bound and
    /// tradeable apart, as the table shows them), each piece of unknown gear and each slot not read one, and the coin.
    /// </summary>
    public static int Slots(InstanceRun r, Func<long, InstanceRun, bool?> gearOf)
    {
        int n = r.UnknownGear + r.UnknownItems + (r.Kinah > 0 ? 1 : 0);
        foreach (var (id, c) in r.Items.Concat(r.BoundItems)) if (c > 0) n += gearOf(id, r) == true ? c : 1;
        return n;
    }

    /// <summary>The run's pieces of gear, unknown gear among them.</summary>
    public static int Gear(InstanceRun r, Func<long, InstanceRun, bool?> gearOf) =>
        r.UnknownGear + r.Items.Concat(r.BoundItems).Where(kv => kv.Value > 0 && gearOf(kv.Key, r) == true).Sum(kv => kv.Value);

    public static Verdict Judge(InstanceRun stored, InstanceRun? read, Func<long, InstanceRun, bool?> gearOf)
    {
        int sb = Slots(stored, gearOf), gb = Gear(stored, gearOf);
        if (read == null || !read.FromPicture) return new(false, false, "its pictures weren't read as a table", sb, 0, gb, 0);
        int sa = Slots(read, gearOf), ga = Gear(read, gearOf);
        Verdict V(bool take, string why, bool same = false) => new(take, same, why, sb, sa, gb, ga);
        if (read.Kinah != stored.Kinah) return V(false, $"its coin read {read.Kinah:N0}{(read.KinahNotRead ? " (not read)" : "")}, the run has {stored.Kinah:N0}");
        if (sa > sb) return V(false, $"the new read has more slots ({sa}) than the run ({sb})");
        if (SameCounts(stored.Items, read.Items) && SameCounts(stored.BoundItems, read.BoundItems) && stored.UnknownGear == read.UnknownGear && stored.UnknownItems == read.UnknownItems)
            return V(false, "the same rewards", same: true);
        int ub = stored.UnknownGear + stored.UnknownItems, ua = read.UnknownGear + read.UnknownItems;
        if (ua > ub) return V(false, $"the new read leaves more slots unnamed ({ua}) than the run ({ub})");
        // as many slots, so no floor to take away: only slots not read before may be named now. One the run named that the
        // new read names otherwise is a close call two reads made two ways (a run's live read voted ten frames and heard the
        // chat), so the run keeps its own (2026-11-05 15:07 Fire Temple: Earth Staff live, Corrupted Judicator Cloak now)
        if (sa == sb)
        {
            var was = Combined(stored); var now = Combined(read);
            if (was.Any(kv => now.GetValueOrDefault(kv.Key) < kv.Value)) return V(false, "as many slots, but an item the run named is named otherwise");
        }
        return V(true, sa < sb ? $"{sb - sa} slot(s) fewer" : "other items");
    }

    /// <summary>
    /// What the run had bound stays bound in the new read: a picture can't tell a bound piece from a tradeable one (the
    /// chat's line said it live, TrackerSession.ApplyGrid's boundSlot), so up to the run's own bound count of each item is
    /// moved from the read's tradeable items to its bound ones. A piece the picture alone files as bound stays so.
    /// </summary>
    public static void KeepBound(InstanceRun stored, InstanceRun read)
    {
        foreach (var (id, nb) in stored.BoundItems)
        {
            int free = read.Items.GetValueOrDefault(id), want = nb - read.BoundItems.GetValueOrDefault(id);
            int move = Math.Min(free, want);
            if (move <= 0) continue;
            if ((read.Items[id] -= move) == 0) read.Items.Remove(id);
            read.BoundItems[id] = read.BoundItems.GetValueOrDefault(id) + move;
            read.Bound += move;
        }
    }

    static bool SameCounts(Dictionary<long, int> a, Dictionary<long, int> b) =>
        a.Where(kv => kv.Value != 0).OrderBy(kv => kv.Key).SequenceEqual(b.Where(kv => kv.Value != 0).OrderBy(kv => kv.Key));

    /// <summary>
    /// The run with the new read's rewards: its items, bound items, slots not read, unknown gear and the chat's verdicts
    /// on them. Its Kinah, times, witnesses and everything else stay. The counts that went down are kept in
    /// <see cref="InstanceRun.RepairedFrom"/>, and a run the site has goes again as a correction.
    /// </summary>
    public static InstanceRun Recounted(InstanceRun stored, InstanceRun read, IReadOnlyList<string> pictures, ItemDictionary items, DateTime now)
    {
        var r = stored.Clone();
        var before = new Dictionary<long, int>();
        foreach (var (id, n) in stored.Items.Concat(stored.BoundItems)) before[id] = before.GetValueOrDefault(id) + n;
        r.Items = new(read.Items); r.BoundItems = new(read.BoundItems); r.Bound = read.Bound;
        r.SlotsUnread = read.SlotsUnread; r.UnknownSlots = new(read.UnknownSlots); r.UnknownItems = read.UnknownItems;
        r.UnknownGear = read.UnknownGear; r.GearNotes = new(read.GearNotes); r.SlotDoubts = new(read.SlotDoubts);
        r.PictureCounts = new(read.PictureCounts); r.PartlyRead = read.PartlyRead;
        if (read.TableChat.Count > 0) r.TableChat = read.TableChat.ToList();
        var after = new Dictionary<long, int>();
        foreach (var (id, n) in r.Items.Concat(r.BoundItems)) after[id] = after.GetValueOrDefault(id) + n;
        foreach (var (id, n) in before) if (after.GetValueOrDefault(id) < n) r.RepairedFrom[id] = n;
        foreach (var id in after.Keys) if (id < 0 && !r.Names.ContainsKey(id) && items.Get(id)?.Name is { Length: > 0 } nm) r.Names[id] = nm;
        r.Pictures = pictures.ToList();
        r.Recounted = now;
        r.Resend = r.RepairedFrom.Count > 0 && InstanceLog.WentToSite(stored);
        return r;
    }

    /// <summary>Gear or not, by the item list: a real id by its groups, a synthetic one by its name (the list's or the run's) matched to a real item. Null: not known.</summary>
    public static Func<long, InstanceRun, bool?> GearOf(ItemDictionary items) => (id, r) =>
    {
        if (id > 0) return items.Get(id) != null ? items.IsGear(id) : null;
        var name = items.Get(id)?.Name ?? r.Names.GetValueOrDefault(id);
        if (string.IsNullOrWhiteSpace(name)) return null;
        return items.Find(name) is long real && real > 0 && items.IsGear(real);
    };

    // ---- the pass over the files ----

    /// <summary>One run the re-count changes: the file, the line as it was read, the record before and after, the verdict and what the read said.</summary>
    public sealed record Change(string File, string Line, InstanceRun Before, InstanceRun After, Verdict Verdict, List<string> Pictures, List<string> Notes);

    /// <summary>One run looked at and left as it is, and why.</summary>
    public sealed record Left(string File, InstanceRun Run, string Why, Verdict? Verdict, List<string>? Pictures = null);

    public sealed class Plan
    {
        public List<Change> Changes { get; } = new();
        public List<Left> Left { get; } = new();
        /// <summary>Runs looked at: eligible ones (counted from a table before the fix).</summary>
        public int Looked { get; set; }
        /// <summary>Of those, read again to the same rewards.</summary>
        public int Same { get; set; }
        public int SlotsRemoved => Changes.Sum(c => c.Verdict.SlotsBefore - c.Verdict.SlotsAfter);
        public int GearRemoved => Changes.Sum(c => c.Verdict.GearBefore - c.Verdict.GearAfter);
        /// <summary>The line for reads.log.</summary>
        public string Summary => $"recount of old reward tables: {Looked} run(s) counted before 0.10.54 looked at, {Changes.Count} changed ({SlotsRemoved} slot(s) and {GearRemoved} piece(s) of gear removed), {Same} the same, {Left.Count} left as they were"
            + (Left.Count > 0 ? " (" + string.Join(", ", Left.GroupBy(l => Reason(l.Why)).OrderByDescending(g => g.Count()).Select(g => $"{g.Count()} {g.Key}")) + ")" : "");
        static string Reason(string why) =>
            why.StartsWith("its coin read", StringComparison.Ordinal) ? "their coin read otherwise"
            : why.StartsWith("the new read has more slots", StringComparison.Ordinal) ? "read with more slots"
            : why.StartsWith("the new read leaves more slots unnamed", StringComparison.Ordinal) ? "read with more slots unnamed"
            : why.StartsWith("its pictures couldn't be read", StringComparison.Ordinal) ? "pictures that couldn't be read"
            : why.Split(" (")[0];
    }

    /// <summary>The runs files under a data folder: the top folder's (before characters) and every character's.</summary>
    public static List<string> RunFiles(string dataDir)
    {
        var res = new List<string>();
        var top = Path.Combine(dataDir, "instances.jsonl");
        if (File.Exists(top)) res.Add(top);
        var chars = Path.Combine(dataDir, "characters");
        if (Directory.Exists(chars))
            foreach (var d in Directory.GetDirectories(chars).OrderBy(d => d, StringComparer.Ordinal))
                if (File.Exists(Path.Combine(d, "instances.jsonl"))) res.Add(Path.Combine(d, "instances.jsonl"));
        return res;
    }

    /// <summary>
    /// Every eligible run of every runs file under <paramref name="dataDir"/> read again from its pictures in
    /// <paramref name="shotsDir"/>, and judged. Nothing is written. <paramref name="serverOf"/> gives a runs file's server
    /// (its character's, for the faction of gear twins). <paramref name="between"/> runs after each run read (a pause,
    /// so a start's background pass leaves the game its share of the PC).
    /// </summary>
    public static async Task<Plan> PlanAsync(string dataDir, string shotsDir, Settings settings, ItemDictionary items, DateTime before, Func<string, string> serverOf,
        Action<string>? say = null, Func<Task>? between = null, CancellationToken ct = default)
    {
        var plan = new Plan();
        var pictures = PicturesIn(shotsDir);
        var gearOf = GearOf(items);
        foreach (var file in RunFiles(dataDir))
        {
            var lines = File.ReadAllLines(file);
            var runs = new List<(string Line, InstanceRun Run)>();
            foreach (var l in lines)
            {
                InstanceRun? r = null;
                try { if (l.Length > 2) r = JsonSerializer.Deserialize<InstanceRun>(l); } catch (Exception) { }
                if (r != null) runs.Add((l, r));
            }
            var server = serverOf(file);
            foreach (var (line, r) in runs)
            {
                ct.ThrowIfCancellationRequested();
                if (NotEligible(r, before) != null) continue;
                plan.Looked++;
                var pics = PicturesFor(r, pictures);
                if (pics.Count == 0) { plan.Left.Add(new(file, r, "no table picture kept", null)); continue; }
                var notes = new List<string>();
                InstanceRun? read = null;
                try
                {
                    var history = runs.Select(x => x.Run).Where(x => !ReferenceEquals(x, r) && string.Equals(x.Zone, r.Zone, StringComparison.OrdinalIgnoreCase)).ToList();
                    read = await ReadAsync(r, pics.Select(p => Path.Combine(shotsDir, p.Name)).ToList(), settings, items, server, history, notes);
                }
                catch (Exception ex) { CrashLog.Write("recount: read", ex); plan.Left.Add(new(file, r, "its pictures couldn't be read: " + ex.Message, null)); continue; }
                if (read != null) KeepBound(r, read);
                var v = Judge(r, read, gearOf);
                var names = pics.Select(p => p.Name).ToList();
                if (v.Take) plan.Changes.Add(new(file, line, r, Recounted(r, read!, names, items, DateTime.UtcNow), v, names, notes));
                else if (v.Same) plan.Same++;
                else plan.Left.Add(new(file, r, v.Why, v, names));
                say?.Invoke($"{r.Zone} {r.Start.ToLocalTime():MMM d HH:mm}: {(v.Take ? "CHANGE" : v.Same ? "same" : "left")} slots {v.SlotsBefore} -> {v.SlotsAfter}, gear {v.GearBefore} -> {v.GearAfter} ({v.Why})");
                if (between != null) await between();
            }
        }
        return plan;
    }

    /// <summary>
    /// Writes the plan's changes: each changed file is first copied to <see cref="BackupName"/> beside it (once: a copy
    /// there already is kept), then each changed run's line is replaced in place, only when the line is still the one that
    /// was read (a run written meanwhile is left as it now is). The new file goes in through a temp file. Returns the runs
    /// written.
    /// </summary>
    public static int Apply(Plan plan)
    {
        int written = 0;
        foreach (var g in plan.Changes.GroupBy(c => c.File))
        {
            try
            {
                var path = g.Key;
                if (!File.Exists(path)) continue;
                var lines = File.ReadAllLines(path).ToList();
                var byLine = g.GroupBy(c => c.Line).ToDictionary(x => x.Key, x => x.First());
                int n = 0;
                for (int i = 0; i < lines.Count; i++)
                    if (byLine.TryGetValue(lines[i], out var c)) { lines[i] = JsonSerializer.Serialize(c.After); n++; }
                if (n == 0) continue;
                var backup = Path.Combine(Path.GetDirectoryName(path)!, BackupName);
                if (!File.Exists(backup)) File.Copy(path, backup);
                var tmp = path + ".tmp";
                File.WriteAllLines(tmp, lines);
                File.Move(tmp, path, overwrite: true);
                written += n;
            }
            catch (Exception ex) { CrashLog.Write("recount: write", ex); }
        }
        return written;
    }

    /// <summary>One line a changed run, for recount.log: what it was and what it is now.</summary>
    public static IEnumerable<string> Report(Plan plan, ItemDictionary items)
    {
        var gearOf = GearOf(items);
        foreach (var c in plan.Changes)
        {
            var gone = new List<string>(); var came = new List<string>();
            var b = Combined(c.Before); var a = Combined(c.After);
            foreach (var (id, n) in b) { int m = a.GetValueOrDefault(id); if (m < n) gone.Add($"{Name(items, c.Before, id)} x{n - m}"); }
            foreach (var (id, n) in a) { int m = b.GetValueOrDefault(id); if (n > m) came.Add($"{Name(items, c.After, id)} x{n - m}"); }
            if (c.Before.UnknownGear != c.After.UnknownGear) (c.After.UnknownGear < c.Before.UnknownGear ? gone : came).Add($"unknown gear x{Math.Abs(c.Before.UnknownGear - c.After.UnknownGear)}");
            if (c.Before.UnknownItems != c.After.UnknownItems) (c.After.UnknownItems < c.Before.UnknownItems ? gone : came).Add($"slots not read x{Math.Abs(c.Before.UnknownItems - c.After.UnknownItems)}");
            yield return $"{c.Before.Label} {c.Before.Start.ToLocalTime():yyyy-MM-dd HH:mm:ss} ({Path.GetFileName(Path.GetDirectoryName(c.File))}): slots {c.Verdict.SlotsBefore} -> {c.Verdict.SlotsAfter}, gear {c.Verdict.GearBefore} -> {c.Verdict.GearAfter}, Kinah {c.After.Kinah:N0} kept"
                + (gone.Count > 0 ? "; removed " + string.Join(", ", gone) : "") + (came.Count > 0 ? "; named now " + string.Join(", ", came) : "")
                + $" [{string.Join(", ", c.Pictures)}]{(c.After.Resend ? "; sent to the site again as a correction" : "")}";
        }
        foreach (var l in plan.Left)
            yield return $"left as it was: {l.Run.Label} {l.Run.Start.ToLocalTime():yyyy-MM-dd HH:mm:ss} ({Path.GetFileName(Path.GetDirectoryName(l.File))}): {l.Why}" + (l.Verdict is { } v ? $" (slots {v.SlotsBefore} -> {v.SlotsAfter})" : "") + (l.Pictures is { Count: > 0 } lp ? $" [{string.Join(", ", lp)}]" : "");
    }

    static Dictionary<long, int> Combined(InstanceRun r)
    {
        var d = new Dictionary<long, int>();
        foreach (var (id, n) in r.Items.Concat(r.BoundItems)) d[id] = d.GetValueOrDefault(id) + n;
        return d;
    }

    static string Name(ItemDictionary items, InstanceRun r, long id) => items.Get(id)?.Name ?? r.Names.GetValueOrDefault(id) ?? $"item {id}";

    /// <summary>A runs file's server: its character's in characters.json, else the one ui.json names (the top folder, before characters).</summary>
    public static Func<string, string> ServersOf(string dataDir, string fallback = "")
    {
        var byId = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            var book = Path.Combine(dataDir, "characters.json");
            if (File.Exists(book) && JsonNode.Parse(File.ReadAllText(book))?["List"] is JsonArray list)
                foreach (var c in list) if (c?["Id"]?.GetValue<string>() is { } id) byId[id] = c?["Server"]?.GetValue<string>() ?? "";
            var ui = Path.Combine(dataDir, "ui.json");
            if (fallback.Length == 0 && File.Exists(ui)) fallback = JsonNode.Parse(File.ReadAllText(ui))?["Server"]?.GetValue<string>() ?? "";
        }
        catch (Exception) { /* the server only settles gear twins; without it they stay unknown gear */ }
        return file =>
        {
            var dir = Path.GetFileName(Path.GetDirectoryName(file) ?? "");
            return byId.TryGetValue(dir, out var s) && s.Length > 0 ? s : fallback;
        };
    }
}
