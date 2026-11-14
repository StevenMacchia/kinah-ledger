using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using KinahLedger.Data;
using KinahLedger.Session;

namespace KinahLedger.Ui;

/// <summary>
/// The Farming page's Full view in the Quest look (Steven, 2026-11-07, step 2): the second column opens the accounts the
/// Compact page keeps behind its chips. The bag as icon tiles (LOOT's card, moved here by ApplyView), Kinah by enemy as
/// bars, drop odds as "1 in 4" badges, the zone board as bars (Where next, moved here too) and gathering as two chips.
/// Every figure is one the page already computed in FillNumbers.
/// </summary>
public partial class MainWindow
{
    /// <summary>Kinah by enemy, as rows with gold bars; the host is a chip's panel in Compact or the Who pays card in Full.</summary>
    void BuildWho(Panel host)
    {
        host.Children.Clear();
        var mobs = _qMobs.Where(m => m.Kinah > 0).OrderByDescending(m => m.Kinah).ToList();
        long named = mobs.Sum(m => m.Kinah);
        // "578.1K came with a name": the Kinah it is out of is the hero line's, beside it (UX pass: it said "of 632.3K" too)
        host.Children.Add(QHead("Sword", "Who pays", named > 0 && _qKinah > 0 ? $"{Fk(named)} came with a name" : "", quiet: true));
        var top = mobs.Take(6).ToList();
        long max = top.Count > 0 ? top[0].Kinah : 0;
        for (int i = 0; i < top.Count; i++)
        {
            var m = top[i];
            host.Children.Add(QRow(QIconBox("Sword", "Coral", 30, round: true), m.Name, m.Kills > 0 ? $"{m.Kills:N0} kill{(m.Kills == 1 ? "" : "s")} · {Fk(m.Kinah / (double)m.Kills)} a kill" : "", Fk(m.Kinah), "Ink", max > 0 ? m.Kinah / (double)max : null, "Gold", rule: i > 0));
        }
        if (top.Count == 0) host.Children.Add(QMore("No enemy named yet: Kinah lines name the enemy once the target bar is read."));
        else if (_qKinah - named > 0) host.Children.Add(QMore($"{_qKinah - named:N0} came with no enemy named"));
    }

    /// <summary>Kills by enemy, as rows: the count on the right, Kinah a kill and the enemy's commonest drop in the small print.</summary>
    void BuildKills(Panel host)
    {
        host.Children.Clear();
        double perMin = _qElapsed.TotalMinutes >= 1 ? _qKills / _qElapsed.TotalMinutes : 0;
        // the kills and the minutes are the hero's (its line and its "Open world · 42 min"); the pace is this card's own
        host.Children.Add(QHead("Sword", "Kills", perMin > 0 ? $"{perMin:0.0} a minute" : "", quiet: true));
        var top = _qMobs.Where(m => m.Kills > 0).OrderByDescending(m => m.Kills).Take(6).ToList();
        int named = 0;
        for (int i = 0; i < top.Count; i++)
        {
            var m = top[i]; named += m.Kills;
            host.Children.Add(QRow(QIconBox("Sword", "Coral", 30, round: true), m.Name, KillsSub(m, id => _items.Get(id)?.Name), m.Kills.ToString("N0"), "Ink", rule: i > 0));
        }
        if (top.Count == 0) host.Children.Add(QMore("No enemy named yet."));
        else if (_qKills - named > 0) host.Children.Add(QMore($"{_qKills - named:N0} kills came with no enemy named"));
    }

    /// <summary>
    /// A Kills row's small print: "1.2K Kinah a kill · drops Raw Leather 1 in 4". An enemy with no named drop (none yet, or
    /// only items the list can't name) has no drop part: the empty tuple's name was null, and the panel crashed on any
    /// such enemy (Steven, 2026-11-11 14:53: "when i click on kills on the farm page it gives me an error").
    /// </summary>
    internal static string KillsSub(SessionEngine.MobStats m, Func<long, string?> nameOf)
    {
        var drop = m.Drops.OrderByDescending(d => d.Value).Select(d => (Name: nameOf(d.Key) ?? "", d.Value)).FirstOrDefault(d => d.Name.Length > 0);
        var kinah = m.Kills > 0 ? $"{Fk(m.Kinah / (double)m.Kills)} Kinah a kill" : "";
        return kinah + (drop.Name is { Length: > 0 } && drop.Value > 0 && m.Kills >= drop.Value ? $" · drops {drop.Name} 1 in {Math.Round(m.Kills / (double)drop.Value):0}" : "");
    }

    /// <summary>Full view: whether Just dropped and Where to farm sit in the second column; null until the view's first balance.</summary>
    (bool Feed, bool Where)? _fullSides;

    /// <summary>For options-test: the Full view's two columns' heights as laid out, and where Just dropped and Where to farm went.</summary>
    internal (double Left, double Right, bool FeedRight, bool WhereRight) FullBalanceForTest()
    {
        double Sum(Panel p) => p.Children.OfType<FrameworkElement>().Where(c => c.Visibility == Visibility.Visible).Sum(c => c.ActualHeight + c.Margin.Top + c.Margin.Bottom);
        return (Sum(LeftCol), Sum(QuestRight) + QuestRight.Margin.Top, FeedCard.Parent == QuestRight, WhereSection.Parent == QuestRight);
    }

    /// <summary>
    /// Full view's two columns kept level (UX pass, 2026-11-13; Steven's "no empty space"): the left column used to end
    /// halfway down beside the bag, Who pays, the odds, the zone board and the gathering cards. Just dropped and Where to farm
    /// each go to the column that leaves the smaller gap at the bottom, measured as they are now: with Who pays and the odds
    /// on the right both stay left under Next up; a gathering stretch (no enemies) sends Just dropped beside the bag. A
    /// placement only changes for a gap 48 px better than the current one, so a pickup doesn't make cards hop.
    /// </summary>
    void BalanceFull()
    {
        if (!(_part == "Farming" && !_welcome && FullShown)) return;
        var size = new Size(662, double.PositiveInfinity);
        double H(UIElement e) { if (e.Visibility != Visibility.Visible) return 0; e.Measure(size); return e.DesiredSize.Height; }
        double left = LeftCol.Children.OfType<UIElement>().Where(c => c != FeedCard && c != WhereSection).Sum(H);
        double right = QuestRight.Margin.Top + QuestRight.Children.OfType<UIElement>().Where(c => c != FeedCard && c != WhereSection).Sum(H);
        double feed = H(FeedCard), where = H(WhereSection);
        (bool Feed, bool Where) best = (false, false);
        double bestGap = double.PositiveInfinity, nowGap = double.PositiveInfinity;
        foreach (var f in new[] { false, true })
            foreach (var w in new[] { false, true })
            {
                double gap = Math.Abs(left + (f ? 0 : feed) + (w ? 0 : where) - (right + (f ? feed : 0) + (w ? where : 0)));
                if (gap < bestGap - 0.5) { bestGap = gap; best = (f, w); }
                if (_fullSides == (f, w)) nowGap = gap;
            }
        if (_fullSides is { } now && nowGap - bestGap < 48) best = now;
        _fullSides = best;
        // the left column: Next up, then Just dropped and Where to farm (those that stay), then the footer;
        // the right column: the bag, Who pays, Drop odds, then Just dropped and Where to farm (those that go), the gathering cards last
        static void Place(FrameworkElement el, Panel to, UIElement after)
        {
            if (ReferenceEquals(el.Parent, to) && to.Children.IndexOf(el) == to.Children.IndexOf(after) + 1) return;
            (el.Parent as Panel)?.Children.Remove(el);
            to.Children.Insert(to.Children.IndexOf(after) + 1, el);
        }
        Place(FeedCard, best.Feed ? QuestRight : LeftCol, best.Feed ? QuestOdds : GoalsCard);
        Place(WhereSection, best.Where ? QuestRight : LeftCol, best.Where == best.Feed ? FeedCard : best.Where ? QuestOdds : GoalsCard);
    }

    /// <summary>The Full view's second column: Who pays, Drop odds and the gathering chips (the bag and the zone board are moved in by ApplyView).</summary>
    void FillQuestRight(Tables s, long kinah, int kills, TimeSpan elapsed)
    {
        bool full = _part == "Farming" && !_welcome && FullShown;
        QuestRight.Visibility = full ? Visibility.Visible : Visibility.Collapsed;
        if (!full) return;
        bool has = _hasSummary;
        // Who pays
        bool who = has && _qMobs.Any(m => m.Kinah > 0);
        QuestWho.Visibility = who ? Visibility.Visible : Visibility.Collapsed;
        // Drop odds: the five commonest drops with their odds, as players say them
        var odds = s.Drops.Where(r => !r.Header && r.DropPct.Length > 0).Select(r => (Row: r, N: Qty(r.Drops))).OrderByDescending(x => x.N).Take(5).ToList();
        QuestOdds.Visibility = has && odds.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        // gathering: the day's limit and what was gathered here
        var limit = Session.GatherDay.Line(Server?.Label ?? "", DateTime.UtcNow);
        int gKinds = s.Gathered.Count(r => !r.Header);
        long crystals = (s.Pets ?? new()).Sum(r => Qty(r.Drops));
        bool gather = has && (gKinds > 0 || crystals > 0 || (s.Skills?.Count ?? 0) > 0);
        QuestGather.Visibility = gather ? Visibility.Visible : Visibility.Collapsed;
        var sig = $"{kinah}|{kills}|{string.Join(",", _qMobs.Select(m => m.Name + m.Kinah + ":" + m.Kills))}|{string.Join(",", odds.Select(o => o.Row.Name + o.Row.DropPct))}|{limit}|{gKinds}|{crystals}|"
            + string.Join(",", (s.Skills ?? new()).Select(k => k.Name + k.Attempts + ":" + k.Value)) + "|" + Skins.Current;
        if (!Changed("quest-right", sig)) return;
        if (who) BuildWho(QuestWhoHost);
        if (odds.Count > 0)
        {
            QuestOddsHost.Children.Clear();
            // how many kills they come from is the hero line's figure; each badge's hover says it with the drops
            QuestOddsHost.Children.Add(QHead("Bolt", "Drop odds", "", quiet: true));
            var g = new Grid { Margin = new Thickness(0, 6, 0, 0) };
            for (int i = 0; i < odds.Count; i++)
            {
                if (i > 0) g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(6) });
                g.ColumnDefinitions.Add(new ColumnDefinition());
                var r = odds[i].Row;
                var badge = QBadge(QItemIcon(r.Name, _items.IdOf(r.Name), 30, round: true, grade: r.Grade), r.DropPct, r.Name);
                badge.ToolTip = $"{r.Name} · {odds[i].N:N0} drop{(odds[i].N == 1 ? "" : "s")} from {kills:N0} kills" + (r.PerHour.Length > 0 ? $" · {r.PerHour} an hour" : "");
                ToolTipService.SetInitialShowDelay(badge, 300);
                Grid.SetColumn(badge, i * 2); g.Children.Add(badge);
            }
            QuestOddsHost.Children.Add(g);
        }
        if (gather)
        {
            // "46 of 1,000" over "gathers today · resets 03:00 here", from the game's daily limit line
            // only when the game said its limit line: the attempts alone were the hero line's figure in a gathering stretch, and
            // "N kinds gathered" the bag's (UX pass, 2026-11-13); the other card spans the row then
            int at = limit.IndexOf(" gathers", StringComparison.Ordinal);
            GatherChip1.Visibility = at > 0 ? Visibility.Visible : Visibility.Collapsed;
            if (at > 0) QChipFill(GatherChip1, QIconBox("Pick", "Sky"), limit[..at], limit[(at + 1)..], quiet: true);
            GatherChip1.ToolTip = limit.Length > 0 ? limit + ". The game allows 1,000 gathers a day on a server." : null;
            Grid.SetColumn(GatherChip2, at > 0 ? 2 : 0); Grid.SetColumnSpan(GatherChip2, at > 0 ? 1 : 3);
            // "Mining", not "Mining · ore and gemstones"; the Soul Crystals are the bag's tile beside it
            var parts = (s.Skills ?? new()).Select(k => $"{k.Name.Split(" · ")[0]} {Fk(k.Value)}").ToList();
            // by skill: the first big, the rest under it; the card has room for two under it, the rest is "+N" and the tooltip
            // names them all (2026-11-08 release check: "Cook…" cut)
            var rest = parts.Skip(1).ToList();
            var shown = rest.Count > 2 ? rest.Take(2).Append($"+{rest.Count - 2} more").ToList() : rest;
            QChipFill(GatherChip2, QIconBox("Leaf", "Lime"), parts.Count > 0 ? parts[0] : "Gathered", shown.Count > 0 ? string.Join(" · ", shown) : "gathered " + CountWhere(), quiet: true);
            GatherChip2.ToolTip = parts.Count > 0 ? "Gathered " + CountWhere() + ": " + string.Join(", ", parts) + ". Each skill's value is at the lowest Market price, " + PriceBook.Cut + "."
                // the tries, nodes, upgrades and fails a skill and a kind of node, when the game's messages counted them (0.10.80)
                + (Shown() is { NodeVisits: > 0 } ge ? GatherHow(ge, s, ge.Totals().Nodes, ge.Upgrades) : "") : null;
        }
    }
}
