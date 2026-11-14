using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using KinahLedger.Session;

namespace KinahLedger.Ui;

/// <summary>
/// "Not a drop" (Steven, 2026-11-13: "i bought the egos and they counted as drops it fucked up my totals"): on a pickup's
/// feed line (its sheet) or its row under YOUR LAST PICKUPS on the item's card, the player takes that one pickup out of
/// their own totals: the run's or the farming count's loot and worth, Today and the week, the zone's and the enemy's drops,
/// the rare-drop alert, and the quest points it gave. Today and quests.json are saved at once and the pickup is kept in
/// not-drops.json, so a restart keeps it. Undo (the bar, Ctrl+Z, or "Undo" on the card, today) puts it all back. After a
/// restart the feed is new, so the card offers the same for the newest of the item Today still holds.
/// "Looks wrong? Report" stays what it was: a flag that keeps the item off the site, and a saved report.
/// </summary>
public partial class MainWindow
{
    /// <summary>This run's "Not a drop" takings, by pickup, for an Undo that puts back every count they left.</summary>
    readonly Dictionary<(DateTime At, long Id, int Count), TrackerSession.NotDropTaken> _notDropTaken = new();

    /// <summary>A feed line that is an item pickup the player can take out (not a craft, not Kinah, not a currency).</summary>
    bool CanNotDrop(FeedLine l) => l.Kind == "item" && l.ItemId > 0 && l.Tab != FeedTab.Crafting && !IsCurrency(l.ItemId);

    /// <summary>"Not a drop" on a feed line.</summary>
    void NotADrop(FeedLine l) => NotADrop(l.ItemId, (int)Math.Min(l.Amount, int.MaxValue), l.Bound, l.At, l.Where, l);

    /// <summary>
    /// One pickup out of every count (TrackerSession.NotADrop, or Today alone when the reader isn't running), its quest
    /// points taken back, Today saved, the pickup kept in not-drops.json; then the window redraws and offers Undo.
    /// </summary>
    /// <param name="todayOnly">No pickup time to go by (the feed is new since a restart): Today alone gives one back, and
    /// the item's quest steps completed today that the count no longer reaches go with it.</param>
    void NotADrop(long id, int count, bool bound, DateTime at, string where, FeedLine? line, bool todayOnly = false)
    {
        var name = _items.NameOf(id);
        if (_demo) return;   // a layout render draws the link, and changes nothing
        if (NotDrops.Has(at, id, count)) { Say($"{name} ×{count} is out of your totals already.", "Faint"); return; }
        var a = new ItemAcquired(at, id, count, bound);
        var zone = where.Contains(" · ") ? where[(where.IndexOf(" · ", StringComparison.Ordinal) + 3)..] : null;
        TrackerSession.NotDropTaken? t = null;
        SessionEngine.Taken? day = null;
        try
        {
            if (_watcher.Chat is { } c && !todayOnly) { t = c.NotADrop(a, where, line, _watcher.DayView); day = t.Day; }
            else day = _watcher.DayView.Unapply(a, zone);
        }
        catch (Exception ex) { CrashLog.Write("not a drop", ex); }
        if (!(t?.Any ?? false) && day == null) { Say($"Couldn't find {name} ×{count} in the counts any more: nothing changed.", "Warn"); return; }
        // the counts redrawn first: the quests read the rows on screen and Today
        Redraw();
        var gone = new List<QuestDone>();
        try
        {
            _questInAt = DateTime.MinValue;
            var x = _questIn = QuestInputsNow();
            // with no pickup time (todayOnly) the steps may be an earlier session's: no session holds more than today does
            gone = Quests.Revoke(_questLog, name, todayOnly ? DateTime.Today.ToUniversalTime() : at, h => x.Of(todayOnly && h == Horizon.Session ? Horizon.Today : h).Item(name));
            if (gone.Count > 0) SaveQuests();
        }
        catch (Exception ex) { CrashLog.Write("not a drop: quests", ex); }
        _watcher.SaveDay();
        NotDrops.Add(new NotDrop(at, DayStore.DayOf(at.ToLocalTime()), id, name, count, bound, where, gone, day, t?.Run));
        if (t != null) _notDropTaken[(at, id, count)] = t;
        // a rare drop the panel over the game still holds: let it go
        if (_rareLead is { } rl && string.Equals(rl.Name, name, StringComparison.OrdinalIgnoreCase)) ForgetRare();
        int pts = gone.Sum(d => d.Points);
        CrashLog.Note($"not a drop: {name} ×{count} at {at.ToLocalTime():HH:mm:ss} taken out of the totals{(pts > 0 ? $", {pts} quest point(s) taken back" : "")}");
        Redraw(); RedrawGoals();
        var said = $"{name} ×{count} is out of your totals{(pts > 0 ? $" (−{pts:N0} pts)" : "")}.";
        _ = Offer("notdrop", said, apply: () => { }, undo: () => UndoNotADrop(at, id, count), commit: () => Task.CompletedTask);
        Say(said + " Undo puts it back.", "Good");
    }

    /// <summary>Undo of "Not a drop": the pickup counts again everywhere it left, and its quest points are back.</summary>
    void UndoNotADrop(DateTime at, long id, int count)
    {
        var d = NotDrops.Find(at, id, count);
        var name = d?.Name ?? _items.NameOf(id);
        try
        {
            if (_notDropTaken.Remove((at, id, count), out var t) && _watcher.Chat is { } c) c.UndoNotADrop(t);
            // after a restart only Today still has it to put back (the other counts began after it)
            else if (d?.DayTaken is { } dt) _watcher.DayView.Restore(dt);
            else if (d?.Run is { } run) _watcher.Chat?.Instances.PutItem(run, id, count, d.Bound);
            if (d?.Quests is { Count: > 0 } q) { Quests.Restore(_questLog, q); SaveQuests(); }
        }
        catch (Exception ex) { CrashLog.Write("not a drop: undo", ex); }
        NotDrops.Remove(at, id, count);
        _watcher.SaveDay();
        Redraw(); RedrawGoals();
        Say($"{name} ×{count} counts again.", "Faint");
    }

    /// <summary>
    /// The card's "Not a drop" part for the item: under each of its last pickups a link (in the rows themselves), and below
    /// them the item's pickups taken out today, each with its Undo. With no pickup in the feed (a restart since), the newest
    /// of the item Today still holds can be taken out: "Not a drop? Take one out of today".
    /// </summary>
    TextBlock? NotDropLine(string name, long id, bool anyInFeed)
    {
        if (_demo || id <= 0 || IsCurrency(id)) return null;
        var today = NotDrops.On(DayStore.Today).Where(d => d.ItemId == id).ToList();
        int held = (_watcher.DayView.Items.GetValueOrDefault(id), _watcher.DayView.ItemsBound.GetValueOrDefault(id)) is var (tr, bd) ? tr + bd : 0;
        if (today.Count == 0 && (anyInFeed || held <= 0)) return null;
        var tb = new TextBlock { Margin = new Thickness(16, 10, 16, 0), FontSize = 12, Foreground = Ck(CkFaint), TextWrapping = TextWrapping.Wrap };
        foreach (var d in today)
        {
            if (tb.Inlines.Count > 0) tb.Inlines.Add(new LineBreak());
            tb.Inlines.Add(new Run($"Not a drop: {d.Name} ×{d.Count} at {d.At.ToLocalTime():HH:mm:ss}   "));
            var undo = new Hyperlink(new Run("Undo")) { Foreground = Ck(CkLink), TextDecorations = null, ToolTip = "Counts it again, with the quest points it gave." };
            var (at, n) = (d.At, d.Count);
            undo.Click += (_, _) => { if (_card != null) _card.IsOpen = false; UndoNotADrop(at, id, n); };
            tb.Inlines.Add(undo);
        }
        if (!anyInFeed && held > 0)
        {
            if (tb.Inlines.Count > 0) tb.Inlines.Add(new LineBreak());
            bool bound = _watcher.DayView.Items.GetValueOrDefault(id) <= 0;
            var take = new Hyperlink(new Run("Not a drop? Take one out of today")) { Foreground = Ck(CkLink), TextDecorations = null, ToolTip = "One of it was no drop (bought, a reward): it leaves today's and the week's figures and the quest points it gave." };
            take.Click += (_, _) => { if (_card != null) _card.IsOpen = false; NotADrop(id, 1, bound, DateTime.UtcNow, "Today", null, todayOnly: true); };
            tb.Inlines.Add(take);
        }
        return tb;
    }

    /// <summary>The "Not a drop" link at the end of one of the card's last-pickup rows.</summary>
    UIElement NotDropLink(FeedLine l)
    {
        var tb = new TextBlock { FontSize = 12, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 0, 0) };
        var h = new Hyperlink(new Run("Not a drop")) { Foreground = Ck(CkLink), TextDecorations = null, ToolTip = "This one was no drop (bought at the Market, a reward): it leaves your totals, today's and the week's, and the quest points it gave. Undo puts it back." };
        h.Click += (_, _) => { if (_card != null) _card.IsOpen = false; NotADrop(l); };
        tb.Inlines.Add(h);
        return tb;
    }
}
