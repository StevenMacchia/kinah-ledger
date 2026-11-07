using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using KinahLedger.Data;
using KinahLedger.Session;
using Path = System.Windows.Shapes.Path;

namespace KinahLedger.Ui;

/// <summary>
/// The Farming page in the Quest look (Steven, 2026-11-07): three hooks above the fold and nothing else. The chase (the hero,
/// Ui/MainWindow.Live.cs FillRate), Next up (the milestones the pace puts minutes away, with today's goals, Ui/MainWindow.Goals.cs)
/// and the stream of what just dropped. This file holds the chips under the hero, Kinah, loot and kills, each opening what is
/// behind it on a click (Kinah by enemy, the bag, kills by enemy), the milestones, and the one-pixel rules that keep the
/// page calm: three cards at rest, one accent per card, every further fact behind a hover or a click.
/// </summary>
public partial class MainWindow
{
    /// <summary>The chip open under the row: "who" (Kinah by enemy), "bag" (LOOT) or "kills"; null for none.</summary>
    string? _qChip;

    /// <summary>The enemies of the count on screen, for the Who pays and Kills panels (from the last FillNumbers).</summary>
    List<SessionEngine.MobStats> _qMobs = new();
    long _qKinah; int _qKills; TimeSpan _qElapsed;

    /// <summary>Wires the hero's chart, the chips, Next up's head and the chase bar's width, once from the constructor.</summary>
    void InitQuest()
    {
        ChartToggle.MouseLeftButtonUp += (_, _) => { _qOpen["chart"] = !QOpen("chart"); ChartBox.Visibility = QOpen("chart") && ChartToggle.Visibility == Visibility.Visible ? Visibility.Visible : Visibility.Collapsed; QSetChev(ChartChev, QOpen("chart")); };
        ChaseTrack.SizeChanged += (_, _) => LayoutChase();
        ChipKinah.MouseLeftButtonUp += (_, _) => ToggleChip("who");
        ChipLoot.MouseLeftButtonUp += (_, _) => ToggleChip("bag");
        ChipKills.MouseLeftButtonUp += (_, _) => ToggleChip("kills");
        GoalsHead.MouseLeftButtonUp += (_, _) => { _qOpen["goals"] = !QOpen("goals"); RedrawGoals(); };
        BagStripHead.MouseLeftButtonUp += (_, _) => ToggleChip("bag");
    }

    /* ---------------- the chase ---------------- */

    double _chaseShare;
    void LayoutChase() => ChaseFill.Width = Math.Max(0, ChaseTrack.ActualWidth * _chaseShare);

    /* ---------------- the bag at a glance ---------------- */

    /// <summary>
    /// The Compact page's bag strip (Steven, 2026-11-07): what you have got this session as inventory slots with their counts,
    /// Kinah first, then the drops and gathered items by worth, the Soul Crystals last; eleven slots and a "+N" for the rest;
    /// the total on the right. The hover card and the click are the bag's; the head opens the bag card under the chips.
    /// </summary>
    void FillBagStrip()
    {
        var h = _lootHere;
        bool show = _part == "Farming" && !_welcome && !FullShown && _hasSummary && h != null;
        BagStrip.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        if (!show) return;
        var drops = h!.S.Drops.Where(r => !r.Header).Select(r => (Row: r, Gathered: false)).Concat(h.S.Gathered.Where(r => !r.Header).Select(r => (Row: r, Gathered: true)))
            .OrderByDescending(x => x.Row.Worth).ThenByDescending(x => Qty(x.Row.Drops)).ToList();
        var pets = h.S.Pets ?? new();
        long crystals = pets.Sum(r => Qty(r.Drops));
        int kinds = drops.Count + (crystals > 0 ? 1 : 0);
        BagStripNote.Text = kinds > 0 ? $"{kinds} kind{(kinds == 1 ? "" : "s")}" : "";
        BagStripTotal.Text = h.Total > 0 ? Fk(h.Total) : "";
        BagStripTotal.ToolTip = h.TotalTip;
        QSetChev(BagStripChev, _qChip == "bag");
        var sig = $"{h.Kinah}|{h.Total}|{crystals}|{string.Join(";", drops.Select(d => $"{d.Row.Name}:{d.Row.Drops}:{d.Row.Grade}:{d.Row.Reported}"))}|{Skins.Current}";
        if (!Changed("bag-strip", sig)) return;
        BagStripHost.Children.Clear();
        const int Max = 11;
        var where = CountWhere();
        var slots = new List<FrameworkElement>();
        if (h.Kinah > 0)
        {
            var k = QSlot(QCoin(42), CountBadge(h.Kinah));
            CardTip(k, () => KinahTodayCard(h.Kinah, h.ByEnemy, h.Kills > 0 ? $"{h.Kills:N0} kills · {Fk(h.Kinah / (double)h.Kills)} a kill" : "", "Picked up " + where, "BY ENEMY"));
            slots.Add(k);
        }
        foreach (var (r, gathered) in drops)
        {
            var name = r.Report.Length > 0 ? r.Report : r.Name;
            var s = QSlot(QItemIcon(name, _items.IdOf(name), 42, round: false, grade: r.Grade), CountBadge(Qty(r.Drops)));
            if (r.Reported) s.Opacity = 0.55;
            CardTip(s, () => ItemHereCard(r, gathered, where));
            if (r.Report.Length > 0) s.MouseLeftButtonUp += (_, _) => ShowItemCard(r, s);
            slots.Add(s);
        }
        if (crystals > 0) slots.Add(QSlot(QIconBox("Gem", "Lime", 42), CountBadge(crystals)));
        foreach (var s in slots.Take(slots.Count > Max + 1 ? Max : slots.Count)) BagStripHost.Children.Add(s);
        if (slots.Count > Max + 1)
        {
            var more = new Border { Width = 42, Height = 42, CornerRadius = new CornerRadius(12), Margin = new Thickness(0, 0, 6, 6), Cursor = Cursors.Hand, ToolTip = "The rest of the bag" };
            more.SetResourceReference(Border.BackgroundProperty, "Card2");
            more.Child = QT($"+{slots.Count - Max}", 12, FontWeights.Black, "Muted");
            more.MouseLeftButtonUp += (_, _) => { if (_qChip != "bag") ToggleChip("bag"); };
            BagStripHost.Children.Add(more);
        }
    }

    /// <summary>
    /// The chase: this hour against your best here (Session/BestRuns.cs), as the bar toward the flag and the ring beside the
    /// figure; mint with a star once it is beaten. With no best yet the ring is empty and says so; while the rate warms up
    /// the bar waits, as the figure does.
    /// </summary>
    void FillChase(double nowHr, double bestHr, bool warm)
    {
        bool has = nowHr > 0 && bestHr > 0 && !warm;
        bool beat = has && nowHr >= bestHr;
        double share = QShare(nowHr, bestHr);
        string tone = beat ? "Mint" : "Gold";
        ChaseBox.Visibility = has ? Visibility.Visible : Visibility.Collapsed;
        if (has)
        {
            ChaseLbl.Text = beat ? "Past your best here · a new best if it holds" : $"{Fk(bestHr - nowHr)} an hour to your best here";
            ChaseBest.Text = Fk(bestHr);
            ChaseFill.SetResourceReference(Border.BackgroundProperty, tone);
            ChaseFill.Effect = QGlow(tone, 10, 0.6);
            ChaseFlag.SetResourceReference(Shape.StrokeProperty, tone);
            ChaseBest.SetResourceReference(TextBlock.ForegroundProperty, tone);
            ChaseBox.ToolTip = $"Your best hour in this zone on this server: {Amounts.Full(bestHr)}. This hour: {Amounts.Full(nowHr)}.";
            _chaseShare = share; LayoutChase();
        }
        var sig = $"{has}|{beat}|{Math.Round(share, 3)}|{nowHr > 0}|{Skins.Current}";
        if (!Changed("ring", sig)) return;
        RingHost.Children.Clear();
        RingHost.Children.Add(has ? QRing(76, 7, share, tone, beat ? "★" : $"{Math.Round(share * 100):0}%", beat ? "new best" : "of best")
            : QRing(76, 7, 0, "Gold", nowHr > 0 ? "–" : "", nowHr > 0 ? "no best yet" : ""));
        RingHost.ToolTip = has ? ChaseBox.ToolTip : nowHr > 0 ? "No best hour here yet: this one sets it." : null;
    }

    /* ---------------- the chips ---------------- */

    /// <summary>The three chips under the hero: Kinah, loot at Market prices, kills; and the Just dropped head's note.</summary>
    void FillChips(SessionEngine e, long kinah, Tables s, int kills, TimeSpan elapsed, long totalNow, int nodes = 0)
    {
        bool farm = _part == "Farming" && !_welcome && _hasSummary;
        ChipsRow.Visibility = farm ? Visibility.Visible : Visibility.Collapsed;
        FeedNote.Text = farm ? "last five" : "";   // the bag strip and the bag card carry the total
        if (!farm) { ChipPanel.Visibility = Visibility.Collapsed; return; }
        _qMobs = e.MobSnapshot(); _qKinah = kinah; _qKills = kills; _qElapsed = elapsed;
        int kinds = s.Drops.Count(r => !r.Header) + s.Gathered.Count(r => !r.Header) + (s.Pets is { Count: > 0 } ? 1 : 0);
        bool gather = _mode == Mode.Gather;
        var sig = $"{kinah}|{s.Value}|{s.Priced}|{kinds}|{kills}|{nodes}|{s.GatherCount}|{gather}|{_qChip}|{FullShown}|{Skins.Current}";
        if (!Changed("chips", sig)) return;
        if (gather)
        {
            // a gathering stretch (the Gathering mode): attempts and what they gave, in place of Kinah and kills, which would read 0
            double perTry = nodes > 0 ? s.GatherCount / (double)nodes : 0;
            QChipFill(ChipKinah, QIconBox("Pick", "Lime"), nodes.ToString("N0"), $"gathering attempt{(nodes == 1 ? "" : "s")}");
            ChipKinah.ToolTip = $"{nodes:N0} gathering attempts {CountWhere()}" + (elapsed.TotalMinutes >= 1 ? $", {Fk(nodes / elapsed.TotalHours)} an hour" : "") + ".";
            QChipFill(ChipLoot, QIconBox("Bag", "Mint"), s.Value > 0 ? Fk(s.Value) : kinds > 0 ? kinds.ToString("N0") : "–", kinds > 0 ? $"gathered · {kinds} kind{(kinds == 1 ? "" : "s")}" : "gathered");
            ChipLoot.ToolTip = kinds == 0 ? "Nothing gathered yet. Click for the bag." : $"{kinds} kind{(kinds == 1 ? "" : "s")} {CountWhere()}" + (s.Priced > 0 ? $": {Fk(s.Value)} at the lowest Market price, {PriceBook.Cut}" : "") + ". Click for the bag.";
            QChipFill(ChipKills, QIconBox("Leaf", "Lime"), Fk(s.GatherCount), nodes > 0 ? $"items · {perTry:0.0} a try" : "items");
            ChipKills.ToolTip = nodes > 0 ? $"{s.GatherCount:N0} items from {nodes:N0} attempts, {perTry:0.0} a try." : "Items show here as nodes are gathered.";
            foreach (var chip in new[] { ChipKinah, ChipKills }) { chip.Cursor = Cursors.Arrow; chip.ClearValue(Border.BorderBrushProperty); }
            ChipLoot.Cursor = Cursors.Hand;
            if (_qChip == "bag") ChipLoot.SetResourceReference(Border.BorderBrushProperty, "Mint"); else ChipLoot.ClearValue(Border.BorderBrushProperty);
            FillChipPanel();
            return;
        }
        foreach (var chip in new[] { ChipKinah, ChipLoot, ChipKills }) chip.Cursor = FullShown ? Cursors.Arrow : Cursors.Hand;
        QChipFill(ChipKinah, QCoin(30), Fk(kinah), "Kinah picked up");
        ChipKinah.ToolTip = $"{kinah:N0} Kinah {CountWhere()}" + (kills > 0 ? $", {Fk(kinah / (double)kills)} a kill" : "") + ". Quest rewards and bound Kinah are left out. Click for Kinah by enemy.";
        QChipFill(ChipLoot, QIconBox("Bag", "Mint"), s.Value > 0 ? Fk(s.Value) : kinds > 0 ? kinds.ToString("N0") : "–", kinds > 0 ? $"loot · {kinds} kind{(kinds == 1 ? "" : "s")}" : "loot");
        ChipLoot.ToolTip = kinds == 0 ? "Nothing picked up yet. Click for the bag."
            : $"{kinds} kind{(kinds == 1 ? "" : "s")} {CountWhere()}" + (s.Priced > 0 ? $": {Fk(s.Value)} at the lowest Market price, {PriceBook.Cut}" : "") + (s.Unpriced > 0 ? $" · {s.Unpriced} with no price yet" : "") + ". Click for the bag.";
        double perMin = elapsed.TotalMinutes >= 1 ? kills / elapsed.TotalMinutes : 0;
        QChipFill(ChipKills, QIconBox("Sword", "Coral"), kills.ToString("N0"), kills > 0 ? $"kill{(kills == 1 ? "" : "s")} · {Fk(kinah / (double)kills)} a kill" : "kills");
        ChipKills.ToolTip = kills > 0 ? $"{kills:N0} kills in {TodayLoot.Dur(elapsed.TotalSeconds)}" + (perMin > 0 ? $", {perMin:0.0} a minute" : "") + ". Click for kills by enemy." : "Kills show here as enemies pay out.";
        foreach (var (chip, key, tone) in new[] { (ChipKinah, "who", "Gold"), (ChipLoot, "bag", "Mint"), (ChipKills, "kills", "Coral") })
        {
            if (_qChip == key) chip.SetResourceReference(Border.BorderBrushProperty, tone); else chip.ClearValue(Border.BorderBrushProperty);
        }
        FillChipPanel();
    }

    /// <summary>For options-test: a chip's click.</summary>
    internal void ToggleChipForTest(string key) => ToggleChip(key);

    string CountWhere() => _prefs.AllZones ? "since the tracker started" : RunShown ? "this run" : "here";

    void ToggleChip(string key)
    {
        if (_mode == Mode.Gather && key != "bag") return;   // a gathering stretch has no enemies behind its chips
        if (FullShown) return;   // Full view has the bag, Who pays and the odds in its second column already
        _qChip = _qChip == key ? null : key;
        _drawnSig.Remove("chips"); _drawnSig.Remove("loot");
        Redraw();
    }

    /// <summary>What a chip opens under the row: Kinah by enemy or kills by enemy as rows with bars; the bag is LOOT's card (FillLoot shows it).</summary>
    void FillChipPanel()
    {
        bool panel = _qChip is "who" or "kills" && _mode != Mode.Gather && !FullShown;
        ChipPanel.Visibility = panel ? Visibility.Visible : Visibility.Collapsed;
        if (!panel) return;
        if (_qChip == "who") BuildWho(ChipPanelHost); else BuildKills(ChipPanelHost);   // Ui/MainWindow.QuestFull.cs
    }

    /* ---------------- Next up's milestones ---------------- */

    /// <summary>A milestone minutes away at this pace: the round figure the bag is about to pass, the stack of the thing collected most, the next round kill count.</summary>
    sealed record Milestone(string Sig, FrameworkElement Icon, string Title, string Sub, double Share, string Tone, string Tip);

    /// <summary>The milestones from the count on screen (none until there is one and two minutes have passed).</summary>
    List<Milestone> Milestones()
    {
        var list = new List<Milestone>();
        var h = _lootHere;
        if (h == null || !_hasSummary || _part != "Farming") return list;
        double mins = h.Elapsed.TotalMinutes;
        if (mins < 2) return list;
        if (h.Total > 0)
        {
            long target = NextRound(h.Total), togo = target - h.Total;
            double eta = togo / (h.Total / mins) * 60;
            list.Add(new($"k{target}", QIconBox("Sack", "Lilac", 28, round: true), $"{Fk(target)} this session", $"{Fk(togo)} away · {Eta(eta)}", h.Total / (double)target, "Lilac",
                $"{Amounts.Full(h.Total)} so far, Kinah and loot {PriceBook.Cut}, at {Fk(h.Total / mins)} a minute. The next round figure turns over by itself."));
        }
        var top = h.S.Drops.Concat(h.S.Gathered).Where(r => !r.Header).Select(r => (Row: r, N: Qty(r.Drops))).Where(x => x.N >= 3).OrderByDescending(x => x.Row.Worth).ThenByDescending(x => x.N).FirstOrDefault();
        if (top.Row != null)
        {
            long target = NextStack(top.N), togo = target - top.N;
            double eta = togo / (top.N / mins) * 60;
            list.Add(new($"i{top.Row.Name}{target}", QItemIcon(top.Row.Name, _items.IdOf(top.Row.Name), 28, round: true, grade: top.Row.Grade), $"{target:N0} {top.Row.Name}", $"{top.N:N0} so far · {togo:N0} to go · {Eta(eta)}", top.N / (double)target, "Mint",
                $"{top.N:N0} picked up in {TodayLoot.Dur(h.Elapsed.TotalSeconds)}, one every {TodayLoot.Dur(h.Elapsed.TotalSeconds / top.N)}."));
        }
        if (h.Kills >= 10)
        {
            long target = NextStack(h.Kills), togo = target - h.Kills;
            double eta = togo / (h.Kills / mins) * 60;
            list.Add(new($"x{target}", QIconBox("Sword", "Coral", 28, round: true), $"{target:N0} kills", $"{h.Kills:N0} so far · {togo:N0} to go · {Eta(eta)}", h.Kills / (double)target, "Coral",
                $"{h.Kills:N0} kills at {h.Kills / mins:0.0} a minute."));
        }
        return list;
    }

    /// <summary>The next round Kinah figure above <paramref name="v"/>: 50K steps to 250K, 250K to 1M, 500K to 5M, then 1M.</summary>
    static long NextRound(long v)
    {
        long step = v < 250_000 ? 50_000 : v < 1_000_000 ? 250_000 : v < 5_000_000 ? 500_000 : 1_000_000;
        return (v / step + 1) * step;
    }

    /// <summary>The next round count above <paramref name="n"/>: 5, 10, 20, 30, 50, 100, 150, 200, 300, 500, 1,000, then thousands.</summary>
    static long NextStack(long n)
    {
        foreach (var m in new long[] { 5, 10, 20, 30, 50, 100, 150, 200, 300, 500, 1000 }) if (m > n) return m;
        long step = n < 10_000 ? 1_000 : 10_000;
        return (n / step + 1) * step;
    }

    static string Eta(double secs) => secs < 60 ? "under a minute" : "about " + TodayLoot.Dur(secs);

    /// <summary>A milestone as a Next up row: its icon, the target, how far, the share as the percentage and the bar.</summary>
    FrameworkElement MileRow(Milestone m, bool rule)
    {
        var row = QRow(m.Icon, m.Title, m.Sub, $"{Math.Round(m.Share * 100):0}%", m.Tone, m.Share, m.Tone, rule, inline: true);
        row.Tag = "mile"; row.ToolTip = m.Tip; row.Cursor = Cursors.Arrow;
        ToolTipService.SetInitialShowDelay(row, 400);
        return row;
    }

    /// <summary>A goal as a Next up row: the item, coin or door, the goal, have and to go with the pace, the share; the card on hover, the sheet on a click.</summary>
    FrameworkElement GoalRow(Goal g, GoalProgress p, bool rule)
    {
        FrameworkElement icon = g.Kind switch
        {
            Goal.KindItem => QItemIcon(g.Name, _items.IdOf(g.Name), 28, round: true, grade: GradeOf(g.Name)),
            Goal.KindRuns => QIconBox("Door", "Sky", 28, round: true),
            _ => QCoin(28),
        };
        var pace = Goals.PaceText(p);
        var sub = p.Done ? "reached today ✓" : $"{GoalAmount(g, p.Have)} so far · {GoalAmount(g, p.ToGo)} to go" + (pace.Length > 0 ? " · " + pace : "");
        var tone = p.Done ? "Gold" : "Lilac";
        var row = QRow(icon, Goals.Label(g), sub, p.Done ? "✓" : $"{Math.Round(p.Fraction * 100):0}%", tone, p.Fraction, tone, rule, inline: true);
        row.Tag = "goal";
        AutomationProperties.SetName(row, $"Goal: {Goals.Label(g)}, {GoalCountText(g, p)}");
        CardTip(row, () => GoalCard(g));
        row.MouseLeftButtonUp += (_, _) => OpenGoalSheet(g, GoalDraft.From(g), row);
        return row;
    }

    /// <summary>The "Set a goal" row after the goals, while fewer than three: a click opens Set a goal.</summary>
    FrameworkElement AddGoalRow(bool rule)
    {
        var row = QRow(QIconBox("Plus", "Dim", 28, round: true), "Set a goal for today", "Kinah, an item or runs · up to three", "", "Ink", rule: rule, inline: true);
        row.Tag = "add";
        ToolTipService.SetInitialShowDelay(row, 400);
        AutomationProperties.SetName(row, "Add a goal");
        row.MouseLeftButtonUp += (_, _) => OpenGoalSheet(null, new GoalDraft(), row);
        return row;
    }
}
