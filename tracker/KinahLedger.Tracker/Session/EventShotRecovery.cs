using System.Text.RegularExpressions;
using KinahLedger.Data;
using KinahLedger.Ocr;

namespace KinahLedger.Session;

/// <summary>
/// A closed event run whose rewards were not read, read again from the pictures of its reward screen that the tracker kept
/// in reward-shots ("20261024-130655-Hidden-Lugi-event-rewards.jpg"). The pictures go through the replay bank's own
/// feeding (<see cref="ReplayBank.FeedCardsAsync"/>) into a session of their own, with a copy of the run as that session's
/// open run, so the cards are filed by EventCardsRead and ApplyGrid exactly as live. Nothing is written or sent here:
/// the caller decides (the live retry in TrackerSession.SendRunsAsync, `replay-event-shots` in Program.cs).
/// </summary>
public static class EventShotRecovery
{
    static readonly Regex ShotName = new(@"^(\d{8}-\d{6})-.+-event-rewards\.jpg$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>A picture before the run's first witness or this long after its last sign is another run's.</summary>
    public const double BeforeSeconds = 5, AfterSeconds = 90;

    /// <summary>When a kept event-rewards picture was taken (its name is the local time), or null for any other file.</summary>
    public static DateTime? ShotTime(string name)
    {
        var m = ShotName.Match(Path.GetFileName(name));
        if (!m.Success || !DateTime.TryParseExact(m.Groups[1].Value, "yyyyMMdd-HHmmss", null, System.Globalization.DateTimeStyles.AssumeLocal, out var t)) return null;
        return t.ToUniversalTime();
    }

    /// <summary>The reward-shots folder beside the logs.</summary>
    public static string ShotsDir => Path.Combine(CrashLog.Dir, "reward-shots");

    /// <summary>
    /// The event-rewards pictures kept for a run, oldest first: taken from its first witness to <see cref="AfterSeconds"/>
    /// after its last loot (or when it was left, or paid). Names only.
    /// </summary>
    public static List<string> PicturesFor(InstanceRun r, string? dir = null)
    {
        dir ??= ShotsDir;
        if (!Directory.Exists(dir)) return new();
        var end = new[] { r.LastLoot, r.Left, r.PaidAt }.Where(x => x != null).Select(x => x!.Value).DefaultIfEmpty(r.Start).Max();
        return Directory.GetFiles(dir, "*-event-rewards.jpg")
            .Select(f => (Name: Path.GetFileName(f), T: ShotTime(f)))
            .Where(x => x.T is DateTime t && t >= r.Start.AddSeconds(-BeforeSeconds) && t <= end.AddSeconds(AfterSeconds))
            .OrderBy(x => x.T).Select(x => x.Name).ToList();
    }

    /// <summary>
    /// What a read of a run's pictures found. <c>Run</c> is a copy of the run with the cards filed into it (the original is
    /// untouched); <c>Read</c> is true when the cards were filed (the run now counts from its picture).
    /// </summary>
    public sealed record Result(InstanceRun Run, bool Read, int Pictures, int Slots, int Named, int Unknown, long AbyssPoints,
        List<(int Card, bool Received, List<RewardGrid.Slot> Slots, bool More)> Cards);

    /// <summary>
    /// Reads <paramref name="pictures"/> (full paths with the time each was taken) into a copy of <paramref name="run"/>.
    /// <paramref name="say"/> gets the title band of each picture, as the bank prints it.
    /// </summary>
    /// <paramref name="server"/> is the player's server label, so gear twins are settled by the faction as live (Data/GearFactions.cs).
    public static async Task<Result> ReadAsync(InstanceRun run, IEnumerable<(string Path, DateTime T)> pictures, Settings settings, ItemDictionary items, Action<string>? say = null, string server = "")
    {
        var session = new TrackerSession(null, settings, items, "Recovery") { Server = server };
        var copy = run.Clone();
        // the copy starts as the run closed: no reward picture read, nothing from one
        copy.Items.Clear(); copy.BoundItems.Clear(); copy.Bound = 0; copy.FromPicture = false; copy.SlotsUnread = 0; copy.UnknownSlots.Clear(); copy.KinahNotRead = false;
        copy.PartlyRead = false; copy.UnknownGear = 0; copy.UnknownItems = 0; copy.GearNotes.Clear(); copy.PictureSlots = 0;
        copy.SlotDoubts.Clear(); copy.PictureCounts.Clear(); copy.CardsReceived = "";
        session.Engine.SetZone(copy.Event.Length > 0 ? copy.Event : copy.Zone);
        session.Instances.Adopt(copy);
        int n = 0; DateTime last = run.LastLoot ?? run.Start;
        foreach (var (path, t) in pictures.OrderBy(p => p.T))
        {
            using var bmp = await ScreenReader.LoadAsync(path);
            await ReplayBank.FeedCardsAsync(session, bmp, t, say);
            n++; last = t;
        }
        session.FlushEventCards(last.AddSeconds(5), "read again from its pictures", force: true);
        // the chat's lines kept on the run, judged against this read as they were live (TableWitness)
        session.TableWitnessDue(last.AddSeconds(5), force: true, kept: run.TableChat);
        var read = session.Instances.Release() ?? copy;
        var cards = session.LastEventCards ?? new();
        var slots = cards.Where(c => c.Received).SelectMany(c => c.Slots).ToList();
        var byIcon = TrackerSession.ByIcon(items);
        int named = slots.Count(s => TrackerSession.SlotName(items, s, TrackerSession.SetMates(slots, byIcon)) != null);
        long abyss = read.Items.Concat(read.BoundItems).Where(kv => string.Equals(items.NameOf(kv.Key).Replace(" (Bound)", ""), "Abyss Points", StringComparison.OrdinalIgnoreCase)).Sum(kv => (long)kv.Value);
        if (read.FromPicture) { read.RewardsNotRead = false; read.LastLoot = run.LastLoot; }
        else read.RewardsNotRead = true;
        return new Result(read, read.FromPicture, n, slots.Count, named, read.SlotsUnread, abyss, cards);
    }

    /// <summary>The same for a run's kept pictures in <paramref name="dir"/> (reward-shots by default), by their names' times.</summary>
    public static Task<Result> ReadKeptAsync(InstanceRun run, Settings settings, ItemDictionary items, string? dir = null, Action<string>? say = null, string server = "")
    {
        dir ??= ShotsDir;
        var names = run.Pictures.Count > 0 ? run.Pictures : PicturesFor(run, dir);
        var pics = names.Select(nm => (Path: Path.Combine(dir, nm), T: ShotTime(nm))).Where(p => p.T != null && File.Exists(p.Path)).Select(p => (p.Path, p.T!.Value));
        return ReadAsync(run, pics, settings, items, say, server);
    }
}
