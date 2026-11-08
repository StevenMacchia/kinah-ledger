using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using KinahLedger.Session;

namespace KinahLedger.Ui;

/// <summary>
/// Quests in the window (phase 1, 2026-11-08): Next up's rows come from the engine (Session/Quests.cs) instead of the
/// page's own arithmetic. Steven's surface rule holds: three rows at rest, Kinah this session first, then two crafting
/// materials from what was last farmed; "N more" opens the rest; a click on a quest opens its tree inline (its chain upward
/// and its branches, indented rows with chevrons, the points on the right in lilac); a quest just completed wears a mint
/// check and its points for a few minutes, then sits in the Done fold at the card's foot. The Events page's place box and
/// the Crafting page's Next up carry their own rows the same way. "Your score" (this session, today, this week) is on the
/// head and in Options › Sharing; nothing is uploaded in this phase. The log is quests.json (never written by a render).
/// </summary>
public partial class MainWindow
{
    /// <summary>The completed quests and the points (quests.json), or a render's own, never saved.</summary>
    QuestLog _questLog = new();
    /// <summary>What the engine read at the last look; null before there is anything to count.</summary>
    QuestInputs? _questIn;
    /// <summary>The live quests at the last look, in the surface's order.</summary>
    List<Quest> _questsNow = new();
    /// <summary>A completed quest's key|scope and when it was done: it keeps its check and points on the list for <see cref="FreshFor"/>.</summary>
    readonly Dictionary<string, DateTime> _questFresh = new();
    /// <summary>The quest whose tree is open (its key), or null.</summary>
    string? _qTree;
    /// <summary>A render asked for a tree open (KL_QUEST_OPEN=tree): the first material's, else the first row's.</summary>
    bool _qTreeWanted;
    /// <summary>The session (its start) the engine has looked at: a session's first look credits what was passed before it without announcing it.</summary>
    DateTime? _questLookedSession;
    /// <summary>The session's start while no count is on screen yet (the chat not found, a render before its sample): the window's own start, so the scope holds still.</summary>
    readonly DateTime _questWindowStart = DateTime.UtcNow;
    int _questTicks;
    static readonly TimeSpan FreshFor = TimeSpan.FromMinutes(3);
    const int QuestsAtRest = 3;
    /// <summary>How many completed quests the open Done fold lists before "and N earlier".</summary>
    const int DoneShown = 10;

    /// <summary>The log at the start; a layout render or a self-test (no watching) starts empty and never saves.</summary>
    void InitQuests(bool watch) { if (watch) _questLog = Quests.Load(); }

    void SaveQuests() { if (!_demo) Quests.Save(_questLog); }

    /* ---------------- what the engine reads ---------------- */

    static bool IsMaterialRow(DropRow r) => !r.Header;

    /// <summary>
    /// The engine's inputs from the counts on screen: this count's loose Kinah, time and crafting materials (the LOOT rows,
    /// gear and currencies left out, as Next up's materials were in 0.10.23), today's from the Today count and today's runs,
    /// the week's Kinah and time from the day history and its runs from the run log, each place's best run before today
    /// against today's best there, and the kinds last picked up from the feed.
    /// </summary>
    QuestInputs QuestInputsNow()
    {
        var now = DateTime.UtcNow;
        var h = _lootHere;
        var mats = new List<QuestMaterial>();
        var sItems = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        QuestCounts session = QuestCounts.None;
        if (h != null && _hasSummary)
        {
            void Take(IEnumerable<DropRow> rows, bool drops)
            {
                foreach (var r in rows.Where(IsMaterialRow))
                {
                    long n = Qty(r.Drops);
                    if (n <= 0) continue;
                    long id = _items.IdOf(r.Name);
                    if (_items.IsGear(id) || IsCurrency(id)) continue;
                    sItems[r.Name] = sItems.GetValueOrDefault(r.Name) + n;
                    if (mats.All(m => !string.Equals(m.Name, r.Name, StringComparison.OrdinalIgnoreCase)))
                        mats.Add(new QuestMaterial(r.Name, Quests.TierOf(drops && h.Kills > 0 ? h.Kills / (double)n : null, r.Grade), r.Worth > 0 ? r.Worth / Math.Max(1, n) : 0));
                }
            }
            Take(h.S.Drops, drops: true);
            Take(h.S.Gathered, drops: false);
            session = new QuestCounts(h.Kinah, h.Elapsed.TotalSeconds, sItems, new Dictionary<string, int>(), h.S.Skills?.Sum(s => s.Attempts) ?? 0);
        }
        // today: the Today count (as the goals read it) and today's runs
        var tc = TodayGoalCounts();
        var tItems = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        foreach (var (id, n) in tc.Farmed.Concat(tc.FromRuns))
        {
            if (n <= 0 || _items.IsGear(id) || IsCurrency(id)) continue;
            var nm = _items.NameOf(id);
            tItems[nm] = tItems.GetValueOrDefault(nm) + n;
        }
        var todayRuns = GoalRunsToday();
        var today = new QuestCounts(tc.Kinah, tc.FarmSeconds, tItems, RunsByPlace(todayRuns), TodayEngine?.Nodes ?? 0);
        // the week: the day history's Kinah and farming time (no item counts there), the run log's runs since Monday
        long wk = 0; double ws = 0;
        try { foreach (var d in FarmWeekDays()) { wk += d.Farm.Kinah; ws += d.Farm.Active; } } catch (Exception) { }
        var log = _watcher.Chat?.Instances ?? _demoInst;
        var all = log?.Snapshot() ?? new List<InstanceRun>();
        var midnight = DateTime.Today;
        var monday = midnight.AddDays(-(((int)midnight.DayOfWeek + 6) % 7));
        var weekRuns = all.Where(r => r.Start.ToLocalTime() >= monday && !_hiddenRuns.Contains(RunKey(r.Start, r.Zone))).ToList();
        var week = new QuestCounts(wk, ws, new Dictionary<string, long>(), RunsByPlace(weekRuns), 0);
        // beat your best run here: the best before today against today's best, for the places run today
        var best = new List<BestHere>();
        foreach (var g in todayRuns.Where(r => !r.OutOfAverages).GroupBy(r => r.Zone))
        {
            long before = all.Where(r => r.Zone == g.Key && !r.OutOfAverages && !TodayLoot.IsToday(r, midnight)).Select(RunValue).DefaultIfEmpty(0L).Max();
            if (before > 0) best.Add(new BestHere(g.Key, before, g.Select(RunValue).Max()));
        }
        var last = FeedFor(FeedTab.Farming).Where(l => l.Kind == "item").Select(l => l.Name).Distinct().ToList();
        var started = (_demo ? _demoEngine?.Started : Shown()?.Started) ?? _questWindowStart;
        return new QuestInputs(session, today, week, mats, last, best, started, now);
    }

    static Dictionary<string, int> RunsByPlace(IEnumerable<InstanceRun> runs) =>
        runs.Where(r => !r.OutOfAverages).GroupBy(r => r.Zone).ToDictionary(g => g.Key, g => g.Count());

    /// <summary>One look: the inputs and the live list, kept for the tick and the other pages.</summary>
    List<Quest> RefreshQuests()
    {
        try
        {
            _questIn = QuestInputsNow(); _questsNow = Quests.Live(_questIn);
            // the first look (here, so a render and the first draw have the score and the Done fold without waiting for a tick):
            // what was passed before it is credited without a word
            if (_questLookedSession != _questIn.SessionStartUtc && _questsNow.Count > 0) QuestAdvance(redraw: false);
        }
        catch (Exception ex) { CrashLog.Write("quests", ex); _questsNow = new(); }
        // a render's tree (KL_QUEST_OPEN=tree): the first material's, once the session has one
        if (_qTreeWanted && _qTree == null && _questsNow.FirstOrDefault(q => q.Kind == QuestKind.Material) is { } mat) { _qTree = mat.Key; _qTreeWanted = false; }
        return _questsNow;
    }

    /* ---------------- completion ---------------- */

    /// <summary>From the window's tick, about every two seconds: quests reached are credited and said once.</summary>
    void QuestTick()
    {
        if (_welcome || ++_questTicks % 2 != 0) return;
        try { QuestAdvance(); }
        catch (Exception ex) { CrashLog.Write("quests", ex); }
    }

    /// <summary>
    /// The engine's look at the counts: every step passed and not yet credited goes to the log (saved), the window says it
    /// ("Quest done · 30 Spiritstone · +51 pts", gold on the panel's alert line and the status line, no sound) and the row
    /// keeps its check for a few minutes. A session's first look (the tracker starting, a render's planted counts) credits
    /// what was passed before it without a word (nothing was "just" done).
    /// </summary>
    List<string> QuestAdvance(bool redraw = true)
    {
        var x = _questIn ??= QuestInputsNow();
        var done = Quests.Advance(_questLog, x, DateTime.Now);
        bool first = _questLookedSession != x.SessionStartUtc; _questLookedSession = x.SessionStartUtc;
        if (done.Count == 0) return new();
        SaveQuests();
        var said = new List<string>();
        if (!first)
        {
            foreach (var d in done) _questFresh[d.Key + "|" + d.Scope] = DateTime.UtcNow;
            var line = done.Count == 1 ? $"Quest done · {Quests.Title(done[0], Fk)} · {Quests.Pts(done[0].Points, plus: true)}"
                : $"Quests done · {string.Join(" · ", done.Select(d => Quests.Title(d, Fk)))} · {Quests.Pts(done.Sum(d => d.Points), plus: true)}";
            said.Add(line);
            if (HudLook == HudLooks.Strip) _hud?.Alert(line, GoalGold, 6); else FlashHud(line);
            Say(line, "Gold");
            CrashLog.Note(line);
        }
        if (redraw) RedrawGoals();
        return said;
    }

    QuestScore ScoreNow() => Quests.Score(_questLog, _questIn?.SessionStartUtc ?? DateTime.UtcNow, DateTime.Now);

    /// <summary>The completed quests of this session, the fresh ones first.</summary>
    List<QuestDone> DoneNow() => Quests.DoneThisSession(_questLog, _questIn?.SessionStartUtc ?? DateTime.UtcNow);

    bool IsFresh(QuestDone d) => _questFresh.TryGetValue(d.Key + "|" + d.Scope, out var at) && DateTime.UtcNow - at < FreshFor;

    void DropStaleFresh() { foreach (var k in _questFresh.Where(kv => DateTime.UtcNow - kv.Value >= FreshFor).Select(kv => kv.Key).ToList()) _questFresh.Remove(k); }

    /// <summary>"143 pts this session · 310 today · 1,220 this week"; a sentence before the first point.</summary>
    public string QuestScoreLine()
    {
        var s = ScoreNow();
        if (s.AllTime == 0) return "No points yet. Quests appear in Next up on the Farming page once you have farmed for a couple of minutes.";
        return $"{Quests.Pts(s.Session)} this session · {Quests.Pts(s.Today)} today · {Quests.Pts(s.Week)} this week";
    }

    /* ---------------- rows ---------------- */

    static string QuestTone(Quest q) => q.Kind switch
    {
        QuestKind.Material => "Mint", QuestKind.Runs => "Sky", QuestKind.BestRun => "Gold", QuestKind.Gather => "Lime", _ => "Lilac",
    };

    FrameworkElement QuestIcon(Quest q, double size = 28) => q.Kind switch
    {
        QuestKind.Material => QItemIcon(q.Subject, _items.IdOf(q.Subject), size, round: true, grade: GradeOf(q.Subject)),
        QuestKind.Runs => QIconBox("Door", "Sky", size, round: true),
        QuestKind.BestRun => QIconBox("Trophy", "Gold", size, round: true),
        QuestKind.Gather => QIconBox("Leaf", "Lime", size, round: true),
        _ => QIconBox("Sack", "Lilac", size, round: true),
    };

    /// <summary>
    /// "26 so far · 4 to go · about 6 min"; a quest over today or the week says so first ("today · 61 so far · …"), since
    /// "100 Spiritstone" today and "30 Spiritstone" this session read alike (Kinah's title carries the horizon already);
    /// a best run "650K today · best 700K".
    /// </summary>
    string QuestSub(Quest q)
    {
        if (q.Kind == QuestKind.BestRun) return $"{Fk(q.Have)} today · best {Fk(q.Target - 1)}";
        var when = q.Horizon != Horizon.Session && q.Kind != QuestKind.Kinah ? Horizon.Word(q.Horizon) + " · " : "";
        var eta = q.SecondsToGo is double s ? " · " + Eta(s) : "";
        return $"{when}{Quests.Amount(q, q.Have, Fk)} so far · {Quests.Amount(q, q.ToGo, Fk)} to go{eta}";
    }

    /// <summary>
    /// A quest as a Next up row: its icon, the target, how far with the pace, the points in lilac and the bar; the card on
    /// hover; a click opens its tree under it. Depth 1 rows are the tree's: indented, a chevron before the icon.
    /// </summary>
    FrameworkElement QuestRow(Quest q, bool rule, int depth = 0, string? note = null, Action? toggled = null)
    {
        var tone = QuestTone(q);
        var row = QRow(QuestIcon(q), Quests.Title(q, Fk), note ?? QuestSub(q), Quests.Pts(q.Points), "Lilac", q.Share, tone, rule, inline: true);
        row.Tag = depth == 0 ? "quest" : "tree";
        AutomationProperties.SetName(row, $"Quest: {Quests.Title(q, Fk)}, {Quests.Pts(q.Points)}");
        if (depth > 0)
        {
            row.Margin = new Thickness(-6 + 18 * depth, 0, -6, 0);
            var g = (Grid)row.Child;
            var chev = QIcon("Chev", 11, "Faint", 2); chev.Margin = new Thickness(-16, 0, 0, 0); chev.HorizontalAlignment = HorizontalAlignment.Left;
            Grid.SetColumnSpan(chev, 1); g.Children.Add(chev);
            row.Cursor = Cursors.Arrow;
        }
        else
        {
            var g = (Grid)row.Child;
            var chev = QIcon("Chev", 12, "Faint", 2); chev.Margin = new Thickness(6, 0, 0, 0); chev.RenderTransformOrigin = new Point(0.5, 0.5); QSetChev(chev, _qTree == q.Key);
            var right = g.Children.OfType<TextBlock>().FirstOrDefault(t => Grid.GetColumn(t) == 2);
            if (right != null) { g.Children.Remove(right); var sp = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center }; sp.Children.Add(right); sp.Children.Add(chev); Grid.SetColumn(sp, 2); g.Children.Add(sp); }
            row.MouseLeftButtonUp += (_, _) => { _qTree = _qTree == q.Key ? null : q.Key; if (toggled != null) toggled(); else RedrawGoals(); };
        }
        CardTip(row, () => QuestCard(q));
        return row;
    }

    /// <summary>A quest's tree as rows: its chain upward ("unlocks next"), then its branches over the longer horizons.</summary>
    List<FrameworkElement> QuestTreeRows(Quest q)
    {
        var rows = new List<FrameworkElement>();
        if (_questIn == null) return rows;
        var tree = Quests.Tree(q, _questIn);
        Quest prev = q;
        foreach (var up in tree.Up) { rows.Add(QuestRow(up, rule: false, depth: 1, note: (rows.Count == 0 ? "unlocks next" : "after " + Quests.Amount(prev, prev.Target, Fk)) + " · " + Quests.Amount(up, up.Have, Fk) + " so far")); prev = up; }
        foreach (var b in tree.Branches) rows.Add(QuestRow(b, rule: false, depth: 1, note: (b.Kind == QuestKind.Kinah ? Horizon.Word(b.Horizon) + " · " : "") + QuestSub(b)));
        return rows;
    }

    /// <summary>A quest just done: the mint check, its title, "+51 pts" in mint, the bar full.</summary>
    FrameworkElement DoneRow(QuestDone d, bool rule, bool fresh)
    {
        var sub = fresh ? $"done · {Horizon.Word(d.Horizon)}" : $"{d.DoneAtUtc.ToLocalTime():HH:mm} · {Horizon.Word(d.Horizon)}" + (d.Points < d.Worth ? $" · worth {Quests.Pts(d.Worth)}, capped" : "");
        var row = QRow(QIconBox("Check", "Mint", 28, round: true), Quests.Title(d, Fk), sub, Quests.Pts(d.Points, plus: true), "Mint", fresh ? 1 : null, "Mint", rule, inline: true);
        row.Tag = fresh ? "fresh" : "done"; row.Cursor = Cursors.Arrow;
        row.ToolTip = $"{Quests.Title(d, Fk)} reached at {d.DoneAtUtc.ToLocalTime():HH:mm}, {Quests.Pts(d.Points, plus: true)}" + (d.Points < d.Worth ? $" of {Quests.Pts(d.Worth)}: the hour's cap ({Quests.CapPerHour} an hour of play)." : ".");
        ToolTipService.SetInitialShowDelay(row, 400);
        return row;
    }

    /// <summary>
    /// Next up's quest rows for a page: the live quests <paramref name="pick"/> keeps, each with its tree when open, the fresh
    /// completions beside the step that followed them. <paramref name="primary"/> is true for a row that counts towards the
    /// rows at rest (a tree's or a fresh row follows its quest).
    /// </summary>
    List<(FrameworkElement El, bool Primary)> QuestRows(Func<Quest, bool>? pick, bool rule, Action? toggled = null)
    {
        DropStaleFresh();
        var rows = new List<(FrameworkElement, bool)>();
        var fresh = DoneNow().Where(IsFresh).ToList();
        foreach (var q in _questsNow.Where(q => pick == null || pick(q)))
        {
            rows.Add((QuestRow(q, rule || rows.Count > 0, toggled: toggled), true));
            if (_qTree == q.Key) foreach (var r in QuestTreeRows(q)) rows.Add((r, false));
            foreach (var d in fresh.Where(d => d.Kind == q.Kind && d.Horizon == q.Horizon && d.Subject == q.Subject).ToList())
            {
                rows.Add((DoneRow(d, rule: false, fresh: true), false)); fresh.Remove(d);
            }
        }
        foreach (var d in fresh.Where(d => pick == null || pick(Quests.AsQuest(d)))) rows.Add((DoneRow(d, rule: true, fresh: true), true));
        return rows;
    }

    /// <summary>The Done fold: one row at the card's foot ("Done · 12 quests · +143 pts this session"), its rows under it on a click.</summary>
    List<FrameworkElement> DoneFoldRows()
    {
        var rows = new List<FrameworkElement>();
        var done = DoneNow();
        if (done.Count == 0) return rows;
        bool open = QOpen("quests-done");
        var s = ScoreNow();
        var head = QRow(QIconBox("Check", "Dim", 28, round: true), $"Done · {done.Count} quest{(done.Count == 1 ? "" : "s")}", $"{Quests.Pts(s.Today)} today · {Quests.Pts(s.Week)} this week", "", "Mint", rule: true, inline: true);
        head.Tag = "donefold";
        var g = (Grid)head.Child;
        var chev = QIcon("Chev", 12, "Faint", 2); chev.Margin = new Thickness(6, 0, 0, 0); chev.RenderTransformOrigin = new Point(0.5, 0.5); QSetChev(chev, open); chev.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(chev, 2); g.Children.Add(chev);
        AutomationProperties.SetName(head, $"Done: {done.Count} quests, {Quests.Pts(s.Session)} this session");
        CardTip(head, ScoreCard);
        head.MouseLeftButtonUp += (_, _) => { _qOpen["quests-done"] = !open; RedrawGoals(); };
        rows.Add(head);
        // open: the newest DoneShown, then a line for the rest (a session's first look can credit a couple of dozen at once)
        if (open)
        {
            foreach (var d in done.Take(DoneShown)) rows.Add(DoneRow(d, rule: false, fresh: false));
            if (done.Count > DoneShown) { var more = QMore($"and {done.Count - DoneShown} earlier · {Quests.Pts(done.Skip(DoneShown).Sum(d => (long)d.Points))}"); more.Margin = new Thickness(0, 6, 0, 4); rows.Add(more); }
        }
        return rows;
    }

    /* ---------------- cards ---------------- */

    /// <summary>The card a quest's hover opens: its band, the bar, HAVE / TO GO / the pace, the points and the tier, what it unlocks.</summary>
    internal Border QuestCard(Quest q)
    {
        var (card, body) = CkShell(300);
        var sub = $"Quest · {Horizon.Word(q.Horizon)}";
        body.Children.Add(q.Kind == QuestKind.Material ? CkBand(q.Subject, GradeOf(q.Subject), sub) : GoalBand(QuestIcon(q, 64), Quests.Title(q, Fk), sub));
        body.Children.Add(GoalBar(q.Share));
        body.Children.Add(CkSection(Horizon.Word(q.Horizon).ToUpperInvariant()));
        body.Children.Add(CkKv("Have", Quests.Amount(q, q.Have, Fk), CkInk));
        body.Children.Add(q.Done ? CkKv("Reached", "✓", CkGold) : CkKv("To go", Quests.Amount(q, q.ToGo, Fk), CkInk));
        if (q.SecondsToGo is double s && !q.Done) body.Children.Add(CkKv("At this pace", Eta(s), CkInk, mono: false));
        body.Children.Add(CkSection("POINTS"));
        body.Children.Add(CkKv("Worth", Quests.Pts(q.Points), CkGold));
        if (q.Kind == QuestKind.Material) body.Children.Add(CkKv("Tier", $"{q.Tier} · {Quests.TierWord(q.Tier)}", CkInk, mono: false));
        if (_questIn != null && Quests.Tree(q, _questIn) is { } t && t.Up.Count > 0)
        {
            var n = CkNote($"Finishing it unlocks {Quests.Title(t.Up[0], Fk)} ({Quests.Pts(t.Up[0].Points)})" + (t.Branches.Count > 0 ? $"; it counts towards {Quests.Title(t.Branches[0], Fk)}." : "."));
            n.Margin = new Thickness(20, 8, 20, 0); body.Children.Add(n);
        }
        var how = CkNote("Click to open its tree"); how.Margin = new Thickness(20, 12, 20, 14);
        body.Children.Add(how);
        return card;
    }

    /// <summary>The score card (the head's note, the Done fold): this session, today, this week, all time, and where the points go next.</summary>
    internal Border ScoreCard()
    {
        var s = ScoreNow();
        var (card, body) = CkShell(300);
        var title = CkText("Your score", CkGold, 16, FontWeights.SemiBold, display: true); title.Margin = new Thickness(20, 16, 20, 2);
        body.Children.Add(title);
        var lead = CkText("Points from the quests in Next up, at completion.", CkMuted, 12.5); lead.Margin = new Thickness(20, 0, 20, 8);
        body.Children.Add(lead);
        body.Children.Add(CkKv("This session", Quests.Pts(s.Session), CkInk));
        body.Children.Add(CkKv("Today", Quests.Pts(s.Today), CkInk));
        body.Children.Add(CkKv("This week", Quests.Pts(s.Week), CkInk));
        body.Children.Add(CkKv("All time", Quests.Pts(s.AllTime), CkGold));
        var n = CkNote($"Capped at {Quests.CapPerHour} points an hour of play. Once your tracker is linked, the score will go to your profile on kinahledger.com.");
        n.Margin = new Thickness(20, 10, 20, 14); body.Children.Add(n);
        return card;
    }

    /* ---------------- the other pages ---------------- */

    /// <summary>
    /// A page's own Next up card (the Events page's place box): the quests <paramref name="pick"/> keeps, with their trees and
    /// fresh completions, under a head with the session's points; null when the page has none.
    /// </summary>
    Border? QuestNextCard(Func<Quest, bool> pick, Action redraw)
    {
        var rows = QuestRows(pick, rule: false, toggled: redraw);
        if (rows.Count == 0) return null;
        var sp = new StackPanel();
        sp.Children.Add(QHead("Trophy", "Next up", "at this pace"));
        foreach (var (el, _) in rows) sp.Children.Add(el);
        var card = new Border { Child = sp, Margin = new Thickness(0, 8, 0, 0), Tag = "quests" };
        card.SetResourceReference(StyleProperty, "QCard");
        return card;
    }

    /* ---------------- self-tests and layout renders ---------------- */

    /// <summary>
    /// For options-test: Next up's quest rows (on the page and behind the chevron), how many sit at rest, their kinds in
    /// order, the tree rows open, whether the Done fold is there and how many done rows show, and the head's note.
    /// </summary>
    internal (int Rows, int AtRest, string Kinds, int Tree, bool DoneFold, int DoneRows, string Note) QuestsForTest()
    {
        var host = GoalsHost.Children.OfType<FrameworkElement>().ToList();
        var all = host.Concat(GoalsMore.Children.OfType<FrameworkElement>()).Concat(GoalsDone.Children.OfType<FrameworkElement>()).ToList();
        string Tag(FrameworkElement e) => e.Tag as string ?? "";
        return (all.Count(e => Tag(e) == "quest"), host.Count(e => Tag(e) is "quest" or "goal"), string.Join(",", _questsNow.Select(q => q.Kind)),
            all.Count(e => Tag(e) == "tree"), all.Any(e => Tag(e) == "donefold"), GoalsDone.Children.OfType<FrameworkElement>().Count(e => Tag(e) == "done"), GoalsNote.Text);
    }

    /// <summary>For options-test: a click on the n-th quest row (its tree opens, or closes).</summary>
    internal void ToggleQuestTreeForTest(int row) { if (row < _questsNow.Count) { var k = _questsNow[row].Key; _qTree = _qTree == k ? null : k; RedrawGoals(); } }

    /// <summary>For options-test: the Done fold's click.</summary>
    internal void ToggleDoneFoldForTest() { _qOpen["quests-done"] = !QOpen("quests-done"); RedrawGoals(); }

    /// <summary>For options-test: one look of the engine, as the tick does; the lines said (none on the first look).</summary>
    internal List<string> QuestTickForTest() => QuestAdvance();

    /// <summary>For options-test: the live quests now.</summary>
    internal IReadOnlyList<Quest> QuestsNowForTest => _questsNow;

    /// <summary>For options-test: this session's completed quests as "title +pts", newest first.</summary>
    internal string QuestsDoneForTest() => string.Join(" | ", DoneNow().Select(d => $"{Quests.Title(d, Fk)} {Quests.Pts(d.Points, plus: true)}"));

    /// <summary>For options-test: the Crafting page's Next up rows by tag.</summary>
    internal string CraftNextForTest() => string.Join(",", CraftNextHost.Children.OfType<FrameworkElement>().Select(e => e.Tag as string ?? e.GetType().Name));
}
