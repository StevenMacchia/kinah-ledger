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
        host.Children.Add(QHead("Sword", "Who pays", named > 0 && _qKinah > 0 ? $"{Fk(named)} of {Fk(_qKinah)} Kinah came with a name" : ""));
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
        host.Children.Add(QHead("Sword", "Kills", _qKills > 0 ? $"{_qKills:N0} in {TodayLoot.Dur(_qElapsed.TotalSeconds)}" + (perMin > 0 ? $" · {perMin:0.0} a minute" : "") : ""));
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
    /// A Kills row's small print: "1.2K Kinah a kill · drops Thick hide 1 in 4". An enemy with no named drop (none yet, or
    /// only items the list can't name) has no drop part: the empty tuple's name was null, and the panel crashed on any
    /// such enemy (Steven, 2026-11-11 14:53: "when i click on kills on the farm page it gives me an error").
    /// </summary>
    internal static string KillsSub(SessionEngine.MobStats m, Func<long, string?> nameOf)
    {
        var drop = m.Drops.OrderByDescending(d => d.Value).Select(d => (Name: nameOf(d.Key) ?? "", d.Value)).FirstOrDefault(d => d.Name.Length > 0);
        var kinah = m.Kills > 0 ? $"{Fk(m.Kinah / (double)m.Kills)} Kinah a kill" : "";
        return kinah + (drop.Name is { Length: > 0 } && drop.Value > 0 && m.Kills >= drop.Value ? $" · drops {drop.Name} 1 in {Math.Round(m.Kills / (double)drop.Value):0}" : "");
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
            QuestOddsHost.Children.Add(QHead("Bolt", "Drop odds", kills > 0 ? $"from {kills:N0} kill{(kills == 1 ? "" : "s")}" : ""));
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
            int at = limit.IndexOf(" gathers", StringComparison.Ordinal);
            QChipFill(GatherChip1, QIconBox("Pick", "Sky"), at > 0 ? limit[..at] : (s.Skills?.Sum(k => k.Attempts) ?? 0).ToString("N0"), at > 0 ? limit[(at + 1)..] : "gathering attempts");
            GatherChip1.ToolTip = limit.Length > 0 ? limit + ". The game allows 1,000 gathers a day on a server." : null;
            var parts = (s.Skills ?? new()).Select(k => $"{k.Name.Split(" · ")[0]} {Fk(k.Value)}").ToList();   // "Mining", not "Mining · ore and gemstones"
            if (crystals > 0) parts.Add($"Soul Crystals {crystals:N0}");
            int kinds = gKinds + (crystals > 0 ? 1 : 0);
            // the chip has room for two skills; the rest is "+N" and the tooltip names them all (2026-11-08 release check: "Cook…" cut)
            var shown = parts.Count > 2 ? parts.Take(2).Append($"+{parts.Count - 2} more").ToList() : parts;
            QChipFill(GatherChip2, QIconBox("Leaf", "Lime"), $"{kinds:N0} kind{(kinds == 1 ? "" : "s")} gathered", shown.Count > 0 ? string.Join(" · ", shown) : "");
            GatherChip2.ToolTip = parts.Count > 0 ? "Gathered " + CountWhere() + ": " + string.Join(", ", parts) + ". Each skill's value is at the lowest Market price, " + PriceBook.Cut + "." : null;
        }
    }
}
