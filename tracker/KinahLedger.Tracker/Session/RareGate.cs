using System.Drawing;
using System.Drawing.Imaging;
using KinahLedger.Data;

namespace KinahLedger.Session;

/// <summary>
/// Rare drops (Steven, 2026-10-23): boss gear and Legend, Unique and Epic items are always logged, and logged right.
///  - They always count: a guess at one is never held back (ChatParser.Rare), and the site never hides one.
///  - A picture of the line or loot list row they were read from is kept on this PC (proof/, beside the logs).
///  - One read from a guessed name or from the loot list alone gets a second read before it counts: the same picture
///    read again, larger and with the contrast boost (and, on the loot list, by the icon beside the row). A second read
///    that names the item exactly confirms it; one that names another item exactly counts that item instead (a guess
///    never beats an exact read); one that can't tell leaves the pickup counted as read, tagged unconfirmed.
/// The session holds such a pickup for the moment the second read takes (the same pass), then commits it. Replays have
/// no pictures: what they hold is committed as read at the next step.
/// </summary>
public sealed class RareGate
{
    public static readonly string[] RareGrades = { "Legend", "Legendary", "Unique", "Epic" };

    public static bool IsRareGrade(string? grade) => grade != null && RareGrades.Contains(grade.Trim(), StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// A Legend, Unique or Epic item, or gear (weapon, armour, accessory) while a world boss is being fought or looted.
    /// Synthetic ids (names the item list lacks) are never rare: nothing is known about them.
    /// </summary>
    public static bool IsRare(ItemDictionary items, long id, bool bossNow)
    {
        if (id <= 0) return false;
        bool gear = items.IsGear(id);
        if (bossNow && gear) return true;
        var grade = items.Get(id)?.Grade;
        if (!IsRareGrade(grade)) return false;
        // Legend materials are rare unless the site's config says Legend is for gear only (RemoteConfig.LegendMaterialsRare)
        bool legend = grade!.StartsWith("Legend", StringComparison.OrdinalIgnoreCase);
        return !legend || gear || Update.RemoteConfig.Current.LegendMaterialsRare;
    }

    /// <summary>One rare pickup waiting for its second read: where it was read ("chat" or "list") and the text it came from.</summary>
    public sealed record Pending(GameEvent Event, string Source, string Raw, DateTime At);

    /// <summary>What a second read saw: the item it named exactly (null: it couldn't tell), its text, and the crop it read.</summary>
    public sealed record Second(long? ItemId, string Text, Bitmap? Crop);

    readonly List<Pending> _pending = new();
    readonly object _lock = new();

    public void Hold(Pending p) { lock (_lock) _pending.Add(p); }
    public int Waiting { get { lock (_lock) return _pending.Count; } }

    /// <summary>The pickups waiting from one source ("chat", "list", or null for all), taken out.</summary>
    public List<Pending> Take(string? source = null)
    {
        lock (_lock)
        {
            var got = _pending.Where(p => source == null || p.Source == source).ToList();
            foreach (var p in got) _pending.Remove(p);
            return got;
        }
    }

    /// <summary>
    /// The pickup to count after its second read, and whether it is confirmed. An exact first read stays as read; a
    /// guess or a loot list row takes the second read's exact item; without one it counts as read, unconfirmed.
    /// </summary>
    public static (GameEvent Event, bool Confirmed, string Note) Decide(Pending p, Second? s, ItemDictionary items)
    {
        var ev = p.Event;
        bool unsure = ev.Unconfirmed;
        if (ev is not ItemAcquired ia) return (ev, !unsure, "");
        string name = items.NameOf(ia.ItemId);
        if (!unsure) return (ev, true, s?.ItemId is long o && o != ia.ItemId ? $"read exactly as {name}; a second read said {items.NameOf(o)}, the exact first read stands" : $"read exactly as {name}");
        if (s == null) return (ev, false, $"{name} ({ev.Via}): no picture for a second read, counted as read and marked unconfirmed");
        if (s.ItemId is not long got) return (ev, false, $"{name} ({ev.Via}): the second read couldn't tell (\"{s.Text}\"), counted as read and marked unconfirmed");
        if (got == ia.ItemId) return (ev with { Via = ev.Via + " · confirmed by a second read" }, true, $"{name} ({ev.Via}): confirmed by a second read");
        return (ia with { ItemId = got, Via = ev.Via + " · confirmed by a second read as " + items.NameOf(got) }, true, $"{name} ({ev.Via}): the second read named it {items.NameOf(got)} exactly, counted as that");
    }

    // ---- proof pictures ----

    public const int ProofKeepDays = 60;
    public const long ProofMaxMB = 100;
    public static string ProofDir => Path.Combine(CrashLog.Dir, "proof");

    /// <summary>Keeps the crop a rare pickup was read from, as a PNG named by time and item. Returns its path, or null.</summary>
    public static string? SaveProof(Bitmap crop, string item, string source, DateTime at)
    {
        try
        {
            Directory.CreateDirectory(ProofDir);
            var safe = new string(item.Where(c => char.IsLetterOrDigit(c) || c == ' ').ToArray()).Trim().Replace(' ', '-');
            if (safe.Length > 40) safe = safe[..40];
            var file = Path.Combine(ProofDir, $"{at.ToLocalTime():yyyyMMdd-HHmmss-fff}-{source}-{safe}.png");
            crop.Save(file, ImageFormat.Png);
            Prune(ProofDir, DateTime.Now);
            return file;
        }
        catch (Exception ex) { CrashLog.Write("rare proof", ex); return null; }
    }

    /// <summary>Old proof pictures go after <see cref="ProofKeepDays"/>; past <see cref="ProofMaxMB"/> the oldest go first. Only files named as above.</summary>
    internal static int Prune(string dir, DateTime now)
    {
        int gone = 0;
        var files = new DirectoryInfo(dir).GetFiles("2*-*.png").Where(f => System.Text.RegularExpressions.Regex.IsMatch(f.Name, @"^\d{8}-\d{6}-\d{3}-(chat|list|event)-")).OrderBy(f => f.LastWriteTime).ToList();
        foreach (var f in files.Where(f => (now - f.LastWriteTime).TotalDays > ProofKeepDays).ToList()) { try { f.Delete(); gone++; files.Remove(f); } catch { } }
        long total = files.Sum(f => f.Length);
        foreach (var f in files) { if (total <= ProofMaxMB * 1024 * 1024) break; total -= f.Length; try { f.Delete(); gone++; } catch { } }
        return gone;
    }

    /// <summary>
    /// A crop around one read line, a little taller than the line and, on the loot list, wide enough on the left to hold
    /// the row's icon. <paramref name="box"/> is in screen pixels, <paramref name="origin"/> where the picture's top-left sat.
    /// </summary>
    public static Rectangle CropAround(Rectangle box, Point origin, Size picture, bool withIcon)
    {
        int pad = Math.Max(3, box.Height / 3), left = withIcon ? (int)(box.Height * 2.6) : pad;
        var r = Rectangle.FromLTRB(box.Left - origin.X - left, box.Top - origin.Y - pad, box.Right - origin.X + pad, box.Bottom - origin.Y + pad);
        r.Intersect(new Rectangle(Point.Empty, picture));
        return r;
    }
}
