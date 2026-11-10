using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using KinahLedger.Session;
using KinahLedger.Upload;

namespace KinahLedger.Ui;

/// <summary>
/// The Quests page (2026-11-08, Steven: "a quest tab people can get to within the app, to track their progress or pin
/// goals"): Next up on Farming stays two bars (0.10.28; three before), and everything else lives here. The score, one line
/// on the head once there are points (the breakdown on hover); This session, the full list with trees and a pin on each row
/// (a pinned chain takes a Next up row); Daily, today's repeatable quests with how many days running; All time, the long
/// chains from this PC's tally, two at rest. A counter shows once: a longer horizon's row with the same count as a shorter
/// one's is a branch in that row's tree (0.10.30, after the 2026-11-08 review); Your goals (the
/// Set a goal sheet moved here from Next up); Done, by day. Quests can be turned off in Options › General (default on):
/// the tab, the rows, the credit and the upload all go. Completed quests ride the next upload batch to the site
/// (Upload/RunPayload.cs, phase 2); the site keeps the profile's score and the leaderboard.
/// </summary>
public partial class MainWindow
{
    /// <summary>Options › General › Quests (default on).</summary>
    bool QuestsOn => _prefs.Quests;

    /// <summary>Quests turned off or on in Options: the page and the rows follow at once.</summary>
    void ApplyQuestsSwitch(bool on)
    {
        _prefs.Quests = on;
        QuestsTab.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
        if (!on && _part == "Quests") ShowPart("Farming");
        _questIn = null; _questsNow = new(); _qTree = null;
        _drawnSig.Remove("goals"); _drawnSig.Remove("questspage");
        RefreshQuestPending();
        FillGoals();
        if (_part == "Quests") FillQuestsPage();
    }

    /* ---------------- pins ---------------- */

    /// <summary>How many chains can be pinned: the two material rows of Next up.</summary>
    const int MaxPins = 2;

    bool IsPinned(Quest q) => _questLog.Pins.Any(p => string.Equals(p, q.Chain, StringComparison.OrdinalIgnoreCase));

    /// <summary>Pins a chain to Next up (or unpins it); the oldest pin gives way past <see cref="MaxPins"/>.</summary>
    void TogglePin(Quest q)
    {
        if (q.Kind == QuestKind.BestRun || q.Kind == QuestKind.Kinah) return;   // Kinah is always the first row; a best run is today's own
        var chain = q.Chain;
        if (!_questLog.Pins.RemoveAll(p => string.Equals(p, chain, StringComparison.OrdinalIgnoreCase)).Equals(0)) { }
        else
        {
            _questLog.Pins.Add(chain);
            while (_questLog.Pins.Count > MaxPins) _questLog.Pins.RemoveAt(0);
        }
        SaveQuests();
        _questIn = null; _drawnSig.Remove("goals"); _drawnSig.Remove("questspage");
        FillGoals();
        if (_part == "Quests") FillQuestsPage();
    }

    /// <summary>The pin at a row's right: gold when the chain is pinned; a click toggles it without opening the tree.</summary>
    FrameworkElement PinButton(Quest q)
    {
        bool on = IsPinned(q);
        var b = new Border { Width = 24, Height = 24, CornerRadius = new CornerRadius(8), Margin = new Thickness(6, 0, 0, 0), Background = Brushes.Transparent, Cursor = Cursors.Hand, VerticalAlignment = VerticalAlignment.Center,
            ToolTip = on ? "Pinned to Next up. Click to unpin." : "Pin to Next up: it takes one of the two material rows on the Farming page." };
        var ic = QIcon("Pin", 13, on ? "Gold" : "Faint", 2); ic.HorizontalAlignment = HorizontalAlignment.Center; ic.VerticalAlignment = VerticalAlignment.Center;
        if (on) ic.SetResourceReference(System.Windows.Shapes.Shape.FillProperty, "Gold");   // filled when pinned, so the two states read apart
        b.Child = ic;
        if (on) b.SetResourceReference(Border.BackgroundProperty, "GoldTint");
        b.MouseEnter += (_, _) => b.SetResourceReference(Border.BackgroundProperty, "HoverTint");
        b.MouseLeave += (_, _) => { if (on) b.SetResourceReference(Border.BackgroundProperty, "GoldTint"); else b.Background = Brushes.Transparent; };
        b.MouseLeftButtonUp += (_, e) => { e.Handled = true; TogglePin(q); };
        AutomationProperties.SetName(b, (on ? "Unpin " : "Pin ") + Quests.Title(q, Fk));
        return b;
    }

    /* ---------------- the page ---------------- */

    void RedrawQuestsPage() { _drawnSig.Remove("questspage"); FillQuestsPage(); }

    /// <summary>Draws the page when it shows and something changed (the tick calls it; the Changed gate keeps it cheap).</summary>
    void FillQuestsPage()
    {
        bool show = _part == "Quests" && !_welcome;
        QuestsPage.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        if (!show) { _drawnSig.Remove("questspage"); return; }
        QuestsOffCard.Visibility = QuestsOn ? Visibility.Collapsed : Visibility.Visible;
        var cards = new[] { QuestsActiveCard, QuestsDailyCard, QuestsAllTimeCard, QuestsGoalsCard, QuestsDoneCard };
        foreach (var c in cards) c.Visibility = QuestsOn ? Visibility.Visible : Visibility.Collapsed;
        if (!QuestsOn) { QuestsScoreLine.Visibility = Visibility.Collapsed; return; }
        var quests = RefreshQuests();
        var x = _questIn;
        var score = ScoreNow();
        var today = TodayGoalCounts();
        var prog = _goals.Select(g => ProgressOf(g, today)).ToList();
        var daily = x != null ? Quests.Daily(x, _questLog.Pins) : new();
        var allTime = x != null ? Quests.AllTimeQuests(x, x.Materials) : new();
        var sig = string.Join(";", quests.Select(q => $"{q.Key}:{q.Have}:{Math.Round((q.SecondsToGo ?? 0) / 60)}")) + "|" + string.Join(";", daily.Select(q => $"{q.Key}:{q.Have}")) + "|" + string.Join(";", allTime.Select(q => $"{q.Key}:{q.Have}"))
            + $"|{_qTree}|{string.Join(",", _questLog.Pins)}|{score.Session}:{score.Today}:{score.Week}:{score.AllTime}|{_questLog.Done.Count}|{string.Join(";", _goals.Select((g, i) => $"{g.Kind}:{g.Target}:{prog[i].Have}"))}|{QOpen("quests-earlier")}:{QOpen("quests-alltime")}|{Skins.Current}";
        if (!Changed("questspage", sig)) return;

        // the score: one line on the page's head once there are points, the breakdown on hover (2026-11-08 review: four
        // "0 pts" tiles were the page's first third; this session's points are on Next up's head already)
        QuestsScoreLine.Text = QuestScoreHead(score);
        QuestsScoreLine.Visibility = score.AllTime > 0 ? Visibility.Visible : Visibility.Collapsed;
        CardTip(QuestsScoreLine, ScoreCard);

        // a counter shows once (2026-11-08 review): a row whose count a shorter horizon's row of the same chain already shows
        // ("300 gathers · 211 so far" today and "1,000 gathers · 211 so far" all time) is that row's branch, in its tree
        var shown = new List<Quest>();
        bool Repeats(Quest q) => shown.Any(s => s.Chain == q.Chain && s.Have == q.Have);

        // This session: the full list with trees and pins, the first two on Next up; no card before the first quest
        QuestsActiveHost.Children.Clear();
        QuestsActiveHost.Children.Add(QHead("Spark", "This session", quests.Count <= Quests.NextUpRows ? "on Next up" : $"the first {Quests.NextUpRows} are on Next up"));
        int i = 0;
        foreach (var q in quests)
        {
            QuestsActiveHost.Children.Add(QuestRow(q, i > 0, toggled: RedrawQuestsPage, pinnable: true));
            if (_qTree == q.Key) foreach (var r in QuestTreeRows(q)) QuestsActiveHost.Children.Add(r);
            shown.Add(q);
            i++;
        }
        if (quests.Count == 0) QuestsActiveCard.Visibility = Visibility.Collapsed;

        // Daily: today's repeatable quests and the streak
        QuestsDailyHost.Children.Clear();
        QuestsDailyHost.Children.Add(QHead("Timer", "Daily", "the same quests every day, from midnight"));
        i = 0;
        var nowLocal = DateTime.Now;
        double toMidnight = (nowLocal.Date.AddDays(1) - nowLocal).TotalSeconds;
        var dailyRows = daily.Where(q => !Repeats(q)).ToList();
        shown.AddRange(dailyRows);
        if (daily.Count > 0 && dailyRows.Count == 0) QuestsDailyCard.Visibility = Visibility.Collapsed;
        foreach (var q in dailyRows)
        {
            int streak = Quests.Streak(_questLog, q.Kind, q.Subject, nowLocal);
            // the pace's "about 47 h" means nothing on a quest that resets at midnight: it says so instead (2026-11-08 review)
            var eta = q.SecondsToGo is double sec ? (sec <= toMidnight ? " · " + Eta(sec) : " · not today at this pace") : "";
            var note = $"{Quests.Amount(q, q.Have, Fk)} so far · {Quests.Amount(q, q.ToGo, Fk)} to go{eta}" + (streak > 1 ? $" · {streak} days running" : "");
            QuestsDailyHost.Children.Add(QuestRow(q, i > 0, note: note, toggled: RedrawQuestsPage));
            i++;
        }
        if (daily.Count == 0) QuestsDailyHost.Children.Add(PageNote("Today's quests appear with the first count of the day."));

        // All time: the long chains from this PC's tally, the nearest to done first; two at rest, the rest behind "N more"
        // (2026-11-08 review: seven "1,000 X · 5 pts" rows read as one)
        QuestsAllTimeHost.Children.Clear();
        QuestsAllTimeHost.Children.Add(QHead("Globe", "All time", "this PC's tally since quests began"));
        var atRows = allTime.Where(q => !Repeats(q)).OrderByDescending(q => q.Share).ToList();
        bool atOpen = QOpen("quests-alltime");
        int atShow = atOpen || atRows.Count <= AllTimeAtRest + 1 ? atRows.Count : AllTimeAtRest;
        i = 0;
        foreach (var q in atRows.Take(atShow))
        {
            QuestsAllTimeHost.Children.Add(QuestRow(q, i > 0, note: $"{Quests.Amount(q, q.Have, Fk)} so far · {Quests.Amount(q, q.ToGo, Fk)} to go", toggled: RedrawQuestsPage));
            i++;
        }
        if (atRows.Count > AllTimeAtRest + 1)
            QuestsAllTimeHost.Children.Add(FoldLink(atOpen ? "Fewer" : $"{atRows.Count - atShow} more  ›", () => { _qOpen["quests-alltime"] = !atOpen; RedrawQuestsPage(); }));
        if (allTime.Count == 0) QuestsAllTimeHost.Children.Add(PageNote("The all-time chains start counting with your first day of quests."));
        else if (atRows.Count == 0) QuestsAllTimeCard.Visibility = Visibility.Collapsed;

        // Your goals: the player's own (the Set a goal sheet lived on Next up until today)
        QuestsGoalsHost.Children.Clear();
        QuestsGoalsHost.Children.Add(QHead("Flag", "Your goals", _goals.Count > 0 ? $"today · {prog.Count(p => p.Done)} of {_goals.Count} done" : "up to three a day · they take a Next up row"));
        for (int g = 0; g < _goals.Count; g++) QuestsGoalsHost.Children.Add(GoalRow(_goals[g], prog[g], g > 0));
        if (_goals.Count < Goals.Max) QuestsGoalsHost.Children.Add(AddGoalRow(_goals.Count > 0));

        // Done: by day, the last seven days, newest first
        QuestsDoneHost.Children.Clear();
        var byDay = Quests.DoneByDay(_questLog, nowLocal);
        // the points are on the page's head and each day's label, so the head counts the quests alone
        QuestsDoneHost.Children.Add(QHead("Check", "Done", _questLog.Done.Count == 0 ? "" : $"{_questLog.Done.Count} quest{(_questLog.Done.Count == 1 ? "" : "s")} in the last {Quests.KeepDays} days"));
        if (byDay.Count == 0) QuestsDoneHost.Children.Add(PageNote("Nothing reached yet. A quest you reach shows on the panel over the game for a few seconds, then lands here."));
        int days = 0; bool earlierOpen = QOpen("quests-earlier"); int hidden = 0, hiddenPts = 0;
        foreach (var (day, done) in byDay)
        {
            bool recent = days < 2 || earlierOpen;
            if (!recent) { hidden += done.Count; hiddenPts += done.Sum(d => d.Points); days++; continue; }
            var label = QT(day == nowLocal.Date ? "Today" : day == nowLocal.Date.AddDays(-1) ? "Yesterday" : day.ToString("ddd MMM d", System.Globalization.CultureInfo.InvariantCulture), 10.5, FontWeights.Bold, "Faint");
            label.Margin = new Thickness(0, days == 0 ? 6 : 10, 0, 2);
            var dayPts = QT(Quests.Pts(done.Sum(d => (long)d.Points)), 10.5, FontWeights.Bold, "Muted"); dayPts.HorizontalAlignment = HorizontalAlignment.Right;
            var lg = new Grid(); lg.Children.Add(label); lg.Children.Add(dayPts);
            QuestsDoneHost.Children.Add(lg);
            int k = 0;
            foreach (var d in done.Take(12)) QuestsDoneHost.Children.Add(DoneRow(d, k++ > 0));
            if (done.Count > 12) QuestsDoneHost.Children.Add(QMore($"and {done.Count - 12} more that day"));
            days++;
        }
        if (hidden > 0 || (earlierOpen && byDay.Count > 2))
            QuestsDoneHost.Children.Add(FoldLink(earlierOpen ? "Fewer days" : $"{byDay.Count - 2} earlier day{(byDay.Count - 2 == 1 ? "" : "s")} · {hidden} quests · {Quests.Pts(hiddenPts)}  ›", () => { _qOpen["quests-earlier"] = !earlierOpen; RedrawQuestsPage(); }));

        // the first card shown sits under the head without the gap the others keep between them
        bool first = true;
        foreach (var c in cards) if (c.Visibility == Visibility.Visible) { c.Margin = new Thickness(0, first ? 0 : 8, 0, 0); first = false; }
    }

    /// <summary>All time's rows at rest; a fold of one is not worth its row, so three show as three.</summary>
    const int AllTimeAtRest = 2;

    /// <summary>A card's fold at its foot: "5 more  ›" in gold, a click opens or closes it.</summary>
    static TextBlock FoldLink(string text, Action click)
    {
        var t = QT(text, 11.5, FontWeights.SemiBold, "Gold");
        t.Cursor = Cursors.Hand; t.Background = Brushes.Transparent; t.Margin = new Thickness(0, 8, 0, 0);
        t.MouseLeftButtonUp += (_, _) => click();
        return t;
    }

    /// <summary>The page head's score: "56 pts today · 1,220 all time"; one figure when the two are the same.</summary>
    static string QuestScoreHead(QuestScore s) =>
        s.Today <= 0 ? Quests.Pts(s.AllTime) + " all time"
        : s.AllTime > s.Today ? $"{Quests.Pts(s.Today)} today · {s.AllTime.ToString("N0", System.Globalization.CultureInfo.InvariantCulture)} all time"
        : Quests.Pts(s.Today) + " today";

    /// <summary>A completed quest on the Done list: the mint check, its title, when, "+N pts" in mint.</summary>
    FrameworkElement DoneRow(QuestDone d, bool rule)
    {
        // Kinah's title carries its horizon already ("500K today")
        var sub = $"{d.DoneAtUtc.ToLocalTime():HH:mm}" + (d.Kind == QuestKind.Kinah ? "" : " · " + Horizon.Word(d.Horizon)) + (d.Points < d.Worth ? $" · worth {Quests.Pts(d.Worth)}, capped" : "");
        var row = QRow(QIconBox("Check", "Mint", 28, round: true), Quests.Title(d, Fk), sub, Quests.Pts(d.Points, plus: true), "Mint", rule: rule, inline: true);
        row.Tag = "done"; row.Cursor = Cursors.Arrow;
        row.ToolTip = $"{Quests.Title(d, Fk)} reached at {d.DoneAtUtc.ToLocalTime():HH:mm}, {Quests.Pts(d.Points, plus: true)}" + (d.Points < d.Worth ? $" of {Quests.Pts(d.Worth)}: the hour's cap ({Quests.CapPerHour} an hour of play)." : ".") + (d.Sent ? " Sent to your profile." : " Goes to your profile with the next upload.");
        ToolTipService.SetInitialShowDelay(row, 400);
        return row;
    }

    static TextBlock PageNote(string text)
    {
        var t = QT(text, 12, FontWeights.Medium, "Faint"); t.TextWrapping = TextWrapping.Wrap; t.Margin = new Thickness(0, 8, 0, 2);
        return t;
    }

    /* ---------------- the upload (phase 2) ---------------- */

    /// <summary>The completed quests the next batch carries, as the site reads them; rebuilt on the window's thread, read by the uploader's.</summary>
    volatile List<QuestDonePayload>? _questPending;
    DateTime _questTallySaved = DateTime.MinValue;
    bool _questTallyDirty;

    /// <summary>Hooks the batch builder: the pending completions ride with the next upload, and a batch the site took marks them sent.</summary>
    void InitQuestUpload()
    {
        RunPayload.QuestsPending = () => _questPending;
        RunPayload.QuestsCarried = sent => Dispatcher.BeginInvoke(() =>
        {
            Quests.MarkSent(_questLog, sent.Select(q => q.key + "|" + q.scope));
            SaveQuests();
        });
        RefreshQuestPending();
    }

    void RefreshQuestPending()
    {
        if (_demo || !QuestsOn || !_questLoaded) { _questPending = null; return; }
        // the tier is the grade, 1 to 5 (0.10.30): the site reads it so for a batch from 0.10.30 on (kinah-ledger-site PR #153, which
        // must be live before this ships: an older site drops a tier above 3, and the batch would still mark it sent)
        var list = Quests.Unsent(_questLog).Take(200).Select(d => new QuestDonePayload(d.Key, d.Kind, d.Horizon, d.Subject, d.Target, d.Tier, d.Points, d.Worth, d.Have,
            d.DoneAtUtc.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", System.Globalization.CultureInfo.InvariantCulture), d.Scope)).ToList();
        _questPending = list.Count > 0 ? list : null;
    }

    /* ---------------- self-tests and layout renders ---------------- */

    /// <summary>For options-test: the page's sections and their rows (Active rows, tree rows, Daily, All time, Done, pins), and whether the off card shows.</summary>
    internal (int Active, int Tree, int Daily, int AllTime, int Done, int Pins, bool Off) QuestsPageForTest()
    {
        string Tag(FrameworkElement e) => e.Tag as string ?? "";
        int Count(StackPanel host, string tag) => host.Children.OfType<FrameworkElement>().Count(e => Tag(e) == tag);
        return (Count(QuestsActiveHost, "quest"), Count(QuestsActiveHost, "tree"), Count(QuestsDailyHost, "quest"), Count(QuestsAllTimeHost, "quest"), Count(QuestsDoneHost, "done"), _questLog.Pins.Count, QuestsOffCard.Visibility == Visibility.Visible);
    }

    /// <summary>For options-test: pins the n-th active quest (or unpins it).</summary>
    internal void TogglePinForTest(int row) { if (row < _questsNow.Count) TogglePin(_questsNow[row]); }

    /// <summary>For options-test: the batch's quests as the uploader would read them.</summary>
    internal int QuestPendingForTest => _questPending?.Count ?? 0;
}
