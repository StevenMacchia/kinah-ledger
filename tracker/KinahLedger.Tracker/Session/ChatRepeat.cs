using KinahLedger.Data;
using KinahLedger.Ocr;

namespace KinahLedger.Session;

/// <summary>
/// Item lines the chat shows again (2026-10-24 15:59, Steven's Kukuru and Orichalcum gathers: the chat path counted Kukuru
/// 2 and Orichalcum Ore 4 for 1 and 3). A Kina line has the Kinah counter to decide a repeat (KinaWitness); an item line
/// had nothing, so one that came back after the chat scrolled, or after a read that couldn't be lined up, counted again.
/// <para>An item line is a repeat when it stands where a counted copy stood (the same place from the bottom of a read as
/// long, under the same two lines), or when an identical line counted in the last <see cref="RepeatSeconds"/> and nothing
/// stands behind a second pickup: a loot list row of the item first seen around it, a gathering bar race that worked on
/// its node, or a kill (for an item that isn't a gathering material). Each counted copy takes one such witness, so a
/// witness one pickup used never makes its repeat a second one; a genuine second pickup always brings its own. A read that
/// shows more copies of the line than were counted shows a second line, no witness needed. A line still without a witness
/// waits <see cref="HoldSeconds"/> after it came in (a row is read a moment after the chat line), then is left out.</para>
/// </summary>
public sealed class ItemRepeatGuard
{
    public const double RepeatSeconds = 30, BeforeSeconds = 12, AfterSeconds = 3, RaceAfterSeconds = 2.5, KillBeforeSeconds = 5, KillAfterSeconds = 4,
        ClusterSeconds = 2.5, KillNearSeconds = 1.5, KillSameSeconds = 0.5, HoldSeconds = 5;

    /// <summary>Where a line came in: its place from the bottom of the read, how long the read was, and the two lines above it.</summary>
    public sealed record Place(int FromBottom, int ReadLength, string Above1, string Above2);

    public enum Verdict { Count, Repeat, Wait }

    /// <summary>
    /// What the read shows of the line: how many copies, and since when the read's oldest other line has been on screen
    /// (a copy counted before that has scrolled off; DateTime.MinValue when the read can't tell).
    /// </summary>
    public readonly record struct Screen(int Shown, DateTime Since);

    sealed record Copy(string Item, int Count, DateTime LineAt, DateTime CountedAt, Place? Place);
    readonly List<Copy> _copies = new();
    // "row" (an item's name), "race" (a node, "" when unnamed, and its skill), "kill"
    readonly List<(string Kind, string Item, GatherFamily Family, DateTime At, int Obj)> _witness = new();
    readonly object _lock = new();

    /// <summary>Whether an item is a gathering material (a race stands behind it, never a kill).</summary>
    public Func<string, bool> IsGather { get; set; } = n => GatheringCatalog.NodeOf(n) != null;

    /// <summary>Repeats left out, for the log and the tests.</summary>
    public int Skipped { get; private set; }

    /// <summary>A loot list row of an item, by when it first showed (a tracked row once: <paramref name="obj"/> &gt; 0).</summary>
    public void Row(string item, DateTime first, int obj)
    {
        lock (_lock)
        {
            if (obj > 0 && _witness.Any(w => w.Obj == obj)) return;
            if (obj <= 0 && _witness.Any(w => w.Kind == "row" && w.Obj <= 0 && Eq(w.Item, item) && Math.Abs((w.At - first).TotalSeconds) < 0.5)) return;
            Add(("row", item, GatherFamily.Unknown, first, obj), first);
        }
    }

    /// <summary>A tracked row that turned out to be no row (a title, a prompt): no witness.</summary>
    public void Withdraw(int obj) { if (obj > 0) lock (_lock) _witness.RemoveAll(w => w.Obj == obj); }

    /// <summary>A gathering bar race that ended on Success, on the node that named it ("" when nothing did).</summary>
    public void Race(DateTime at, string node, GatherFamily family) { lock (_lock) Add(("race", node, family, at, 0), at); }

    /// <summary>A kill, by the line that showed it (<paramref name="what"/>: an enemy's Kinah, Soul Points, soul row, XP): two of one kind at once are two kills.</summary>
    public void Kill(DateTime at, string what = "") { lock (_lock) Add(("kill", what, GatherFamily.Unknown, at, 0), at); }

    void Add((string, string, GatherFamily, DateTime, int) w, DateTime at)
    {
        _witness.Add(w);
        _witness.RemoveAll(x => (at - x.At).TotalSeconds > 180);
    }

    /// <summary>A chat item line counted: a copy the next identical lines are judged against.</summary>
    public void Counted(string item, int count, DateTime lineAt, DateTime now, Place? place)
    {
        lock (_lock)
        {
            _copies.Add(new Copy(item, count, lineAt, now, place));
            _copies.RemoveAll(c => (now - c.CountedAt).TotalSeconds > RepeatSeconds + BeforeSeconds);
        }
    }

    /// <summary>
    /// One chat item line before it counts. <paramref name="screen"/>: what the read shows of it;
    /// <paramref name="final"/>: no more waiting (a stop, the end of a replay). The reason says what decided.
    /// </summary>
    public (Verdict V, string Why) Judge(string item, int count, DateTime lineAt, DateTime now, Place? place, Screen screen, bool final)
    {
        lock (_lock)
        {
            var prior = _copies.Where(c => Eq(c.Item, item) && c.Count == count && (now - c.CountedAt).TotalSeconds <= RepeatSeconds).ToList();
            if (prior.Count == 0) return (Verdict.Count, "");
            // the read shows more copies than the counted ones still on screen: a line of its own
            int onScreen = prior.Count(c => screen.Since == DateTime.MinValue || c.LineAt >= screen.Since.AddSeconds(-1));
            if (screen.Shown >= onScreen + 1) return (Verdict.Count, $"the read shows {screen.Shown} such lines" + (onScreen < prior.Count ? $" and {prior.Count - onScreen} counted copy had scrolled off" : ""));
            if (place != null && prior.FirstOrDefault(c => SamePlace(c.Place, place)) is { } at)
                return (Verdict.Repeat, $"it stands where the copy counted at {at.LineAt.ToLocalTime():HH:mm:ss} stood");
            if (Supported(item, lineAt, prior)) return (Verdict.Count, "a second witness");
            if (!final && (now - lineAt).TotalSeconds < HoldSeconds) return (Verdict.Wait, "");
            var last = prior.MaxBy(c => c.CountedAt)!;
            return (Verdict.Repeat, $"counted at {last.LineAt.ToLocalTime():HH:mm:ss}, and no new loot list row, gathering race or kill for a second one");
        }
    }

    public void NoteSkipped() => Skipped++;

    /// <summary>
    /// Whether the witnesses of this item cover one copy more with this line than without it: each copy takes one cluster
    /// (a race and its row a moment apart are one pickup; kills are their own), oldest copy first, earliest cluster first.
    /// </summary>
    bool Supported(string item, DateTime lineAt, List<Copy> prior)
    {
        bool gather = IsGather(item);
        var node = GatheringCatalog.NodeOf(item);
        var pts = _witness.Where(w => w.Kind switch
        {
            "row" => Eq(w.Item, item),
            "race" => gather && (w.Item.Length == 0 || node == null || Eq(w.Item, node.Name) || (node.Name == GatheringCatalog.GemNode && w.Family == node.Family)),
            "kill" => !gather,
            _ => false,
        }).OrderBy(w => w.At).ToList();
        if (pts.Count == 0) return false;
        // rows and races chain into clusters; a kill joins one it stands beside, else kills chain on their own
        var clusters = new List<List<(string Kind, DateTime At, string What)>>();
        foreach (var p in pts.Where(p => p.Kind != "kill"))
        {
            if (clusters.Count > 0 && (p.At - clusters[^1][^1].At).TotalSeconds <= ClusterSeconds) clusters[^1].Add((p.Kind, p.At, p.Item));
            else clusters.Add(new() { (p.Kind, p.At, p.Item) });
        }
        int solid = clusters.Count;
        foreach (var k in pts.Where(p => p.Kind == "kill"))
        {
            var near = clusters.Take(solid).FirstOrDefault(c => c.Any(m => m.Kind != "kill" && Math.Abs((m.At - k.At).TotalSeconds) <= KillNearSeconds));
            if (near != null) { near.Add(("kill", k.At, k.Item)); continue; }
            var lastKill = clusters.Skip(solid).LastOrDefault();
            // one kill's lines (its Kinah, Soul Points, soul row) land in one read: kills a moment apart are two kills
            if (lastKill != null && (k.At - lastKill[0].At).TotalSeconds <= KillSameSeconds) lastKill.Add(("kill", k.At, k.Item));
            else clusters.Add(new() { ("kill", k.At, k.Item) });
        }
        clusters = clusters.OrderBy(c => c.Min(m => m.At)).ToList();
        // how many pickups a cluster stands for: two rows, two races, or two kills' lines of one kind at once are two
        var room = clusters.Select(c => Math.Max(Math.Max(c.Count(m => m.Kind == "row"), c.Count(m => m.Kind == "race")),
            c.Where(m => m.Kind == "kill").GroupBy(m => m.What).Select(g => g.Count()).DefaultIfEmpty(0).Max())).ToArray();
        // a row shows around its line; a race ends before its line (the chat at most a pass ahead of the bar); a kill's lines print beside the drop's
        bool Fits(List<(string Kind, DateTime At, string What)> c, DateTime l) => c.Any(m => (m.At - l).TotalSeconds is var d && m.Kind switch
        {
            "kill" => d >= -KillBeforeSeconds && d <= KillAfterSeconds,
            "race" => d >= -BeforeSeconds && d <= RaceAfterSeconds,
            _ => d >= -BeforeSeconds && d <= AfterSeconds,
        });
        int Matched(IEnumerable<DateTime> lines)
        {
            var used = new int[clusters.Count]; int n = 0;
            foreach (var l in lines.OrderBy(x => x))
                for (int i = 0; i < clusters.Count; i++)
                    if (used[i] < room[i] && Fits(clusters[i], l)) { used[i]++; n++; break; }
            return n;
        }
        var before = prior.Select(c => c.LineAt).ToList();
        return Matched(before.Append(lineAt)) > Matched(before);
    }

    static bool SamePlace(Place? a, Place b) =>
        a != null && a.FromBottom == b.FromBottom && a.ReadLength == b.ReadLength && a.Above1.Length > 0 && Close(a.Above1, b.Above1) && Close(a.Above2, b.Above2);

    static string Key(string s) => new(s.ToLowerInvariant().Where(char.IsLetterOrDigit).ToArray());

    /// <summary>The same chat line read a little differently ("Orichalcum Ore&amp;cqüired."): two letters apart at most, the same digits.</summary>
    public static bool SameLine(string a, string b)
    {
        var x = Key(a); var y = Key(b);
        if (x == y) return x.Length > 0;
        if (x.Length < 8 || y.Length < 8 || Math.Abs(x.Length - y.Length) > 2) return false;
        if (new string(x.Where(char.IsDigit).ToArray()) != new string(y.Where(char.IsDigit).ToArray())) return false;
        return Lev(x, y) <= 2;
    }

    static int Lev(string x, string y)
    {
        var prev = new int[y.Length + 1]; var cur = new int[y.Length + 1];
        for (int j = 0; j <= y.Length; j++) prev[j] = j;
        for (int i = 1; i <= x.Length; i++)
        {
            cur[0] = i;
            for (int j = 1; j <= y.Length; j++) cur[j] = Math.Min(Math.Min(cur[j - 1] + 1, prev[j] + 1), prev[j - 1] + (x[i - 1] == y[j - 1] ? 0 : 1));
            (prev, cur) = (cur, prev);
        }
        return prev[y.Length];
    }

    /// <summary>Two reads of one line: the same digits, letters a quarter apart at most.</summary>
    static bool Close(string a, string b)
    {
        var x = Key(a); var y = Key(b);
        if (x == y) return true;
        if (x.Length < 6 || y.Length < 6) return false;
        if (new string(x.Where(char.IsDigit).ToArray()) != new string(y.Where(char.IsDigit).ToArray())) return false;
        int max = Math.Max(2, Math.Min(x.Length, y.Length) / 4);
        if (Math.Abs(x.Length - y.Length) > max) return false;
        return Lev(x, y) <= max;
    }

    static bool Eq(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
}

public sealed partial class TrackerSession
{
    readonly ItemRepeatGuard _repeats = new();
    public ItemRepeatGuard Repeats => _repeats;
    // item lines waiting a moment for a witness of a second pickup (ItemRepeatGuard.HoldSeconds)
    readonly List<(GameEvent Ev, string Raw, DateTime LineAt, ItemRepeatGuard.Place? Place, ItemRepeatGuard.Screen Screen, bool NeedsWitness)> _repeatHeld = new();
    // the vote's lines as they came in (ChatVoter.LastOut), for where each new line stood
    readonly List<ChatVoter.Out> _voteMeta = new();
    // the last Apply left its line out as a repeat (the log then says "--", not "ok")
    bool _lastRepeatOnly;

    /// <summary>How many copies of this item line a read shows: lines that mean it, or read it a letter or two off.</summary>
    int ShownIn(string key, string raw, IReadOnlyList<string> read) => key.Length == 0 ? 0 : read.Count(l => _parser.Meaning(l).Key == key || ItemRepeatGuard.SameLine(l, raw));

    /// <summary>
    /// What a read shows of an item line (ItemRepeatGuard.Screen): its copies, and the first sight of the read's oldest
    /// other line, when three of its other lines were seen before (one garbled read can't make the screen look new).
    /// </summary>
    ItemRepeatGuard.Screen ScreenOf(string key, string raw, IReadOnlyList<string> read, DateTime now)
    {
        var seen = read.Where(l => _parser.Meaning(l).Key != key && !ItemRepeatGuard.SameLine(l, raw)).Select(l => Reread.FirstSeen(l, _firstSeen)).OfType<DateTime>().Where(f => (now - f).TotalSeconds > 2).ToList();
        return new ItemRepeatGuard.Screen(ShownIn(key, raw, read), seen.Count >= 3 ? seen.Min() : DateTime.MinValue);
    }

    static string ItemLineKey(string name, int count) => "i:" + name.ToLowerInvariant() + "x" + count;

    /// <summary>
    /// A chat item line before it counts (ItemRepeatGuard): counted, held for a witness, or left out as a repeat.
    /// </summary>
    ItemRepeatGuard.Verdict RepeatCheck(ItemAcquired a, GameEvent ev, string raw, DateTime lineAt, ItemRepeatGuard.Place? place, ItemRepeatGuard.Screen screen, bool needsWitness, DateTime now)
    {
        var name = _items.NameOf(a.ItemId);
        var (v, why) = _repeats.Judge(name, a.Count, lineAt, now, place, screen, final: false);
        if (v == ItemRepeatGuard.Verdict.Wait) { lock (_repeatHeld) _repeatHeld.Add((ev, raw, lineAt, place, screen, needsWitness)); return v; }
        if (v == ItemRepeatGuard.Verdict.Repeat) { SkipRepeat(raw, why, lineAt); return v; }
        if (why.Length > 0) CrashLog.Note($"chat: '{raw.Trim()}' again: counted, {why}");
        _repeats.Counted(name, a.Count, lineAt, now, place);
        return v;
    }

    void SkipRepeat(string raw, string why, DateTime lineAt)
    {
        _repeats.NoteSkipped();
        Accuracy.Add("chat_repeat");
        CrashLog.Note($"chat: '{raw.Trim()}' seen again after a scroll: not counted again ({why})");
    }

    /// <summary>Item lines held for a witness: counted once one comes, left out once the wait is over (or at a stop).</summary>
    internal void ReleaseRepeats(DateTime now, bool final = false)
    {
        List<(GameEvent Ev, string Raw, DateTime LineAt, ItemRepeatGuard.Place? Place, ItemRepeatGuard.Screen Screen, bool NeedsWitness)> held;
        lock (_repeatHeld) { if (_repeatHeld.Count == 0) return; held = _repeatHeld.ToList(); _repeatHeld.Clear(); }
        foreach (var h in held)
        {
            if (h.Ev is not ItemAcquired a) continue;
            var name = _items.NameOf(a.ItemId);
            var (v, why) = _repeats.Judge(name, a.Count, h.LineAt, now, h.Place, h.Screen, final);
            if (v == ItemRepeatGuard.Verdict.Wait) { lock (_repeatHeld) _repeatHeld.Add(h); continue; }
            if (v == ItemRepeatGuard.Verdict.Repeat) { SkipRepeat(h.Raw, why, h.LineAt); continue; }
            CrashLog.Note($"chat: '{h.Raw.Trim()}' again: counted, {why}");
            _repeats.Counted(name, a.Count, h.LineAt, now, h.Place);
            FinishChatItem(h.Ev, h.Raw, h.NeedsWitness, now);
        }
    }

    /// <summary>The end of a chat item line's way in: a rare drop to its second read, a drop out in the world to its kill or gather, else counted.</summary>
    void FinishChatItem(GameEvent ev, string raw, bool needsWitness, DateTime now)
    {
        if (ev is ItemAcquired ra && IsRare(ra.ItemId)) { _rare.Hold(new RareGate.Pending(ev, "chat", raw, now)); return; }
        if (needsWitness) { lock (_awaiting) _awaiting.Add((ev, now)); return; }
        Commit(ev);
    }

    /// <summary>The loot list's rows of this pass, as witnesses of a pickup (by item, from when each first showed).</summary>
    void RepeatRows(IEnumerable<LootPopup.Row> rows, DateTime now)
    {
        foreach (var r in rows)
            if (!r.IsKinah && r.Count > 0 && _items.Knows(r.Name)) _repeats.Row(_items.NameOf(_items.IdOf(r.Name)), r.First ?? now, r.Obj);
    }
}
