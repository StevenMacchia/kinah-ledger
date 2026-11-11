using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using KinahLedger.Data;
using KinahLedger.Session;

namespace KinahLedger.Ui;

/// <summary>
/// The Crafting page in the Quest look (Steven, 2026-11-07, step 3): profit is the hero, in mint, with the ring of crafts
/// that worked; three chips (crafts, spent, sells for); Next up nudges toward the next round profit and names the recipe
/// that pays most today; Just crafted shows each item with a worked or failed dot; the recipes this session are small
/// cards; Best crafts today waits behind its head. Every figure comes from FillCraft (Ui/MainWindow.xaml.cs).
/// </summary>
public partial class MainWindow
{
    /// <summary>The recipe that pays most today on the player's server, from Best crafts (FillBestCrafts keeps it): name, profit a craft, success, makes, cost, how many recipes.</summary>
    (string Name, long Id, long Profit, double Success, int Makes, long Cost, int Count)? _bestTop;

    /// <summary>A recipe's profit this session and how many of its crafts worked (FillCraft collects them for Next up).</summary>
    readonly List<(string Name, long Profit, int Ok)> _craftPerRecipe = new();

    /// <summary>The Quest pieces of the Crafting page, after FillCraft's figures: the hero's chase and ring, the chips, Next up, the recipe cards.</summary>
    void FillCraftQuest(int tries, int ok, int itemsMade, long spent, long worth, long net, bool some, bool atMost, List<DropRow> rows)
    {
        bool known = spent > 0 || worth > 0;
        // the hero, lit by the art of the recipe that paid most this session, else the one that pays most today (v2, Ui/Quest.cs HeroArt)
        HeroArt(CraftHero, _craftPerRecipe.Count > 0 ? _craftPerRecipe.OrderByDescending(r => r.Profit).First().Name : _bestTop?.Name);
        double share = tries > 0 ? ok / (double)tries : 0;
        CraftChaseBox.Visibility = tries > 0 ? Visibility.Visible : Visibility.Collapsed;
        // the failed count is the ring's and "4 of 5 worked"'s already (UI audit 2026-11-08: one fact three times)
        CraftChaseLbl.Text = tries > 0 && known ? $"profit {PriceBook.Cut}" : "";
        CraftChaseBest.Text = tries > 0 ? $"{ok:N0} of {tries:N0} worked" : "";
        if (tries > 0) Motion.Slide(CraftChaseFill, "craft-chase", share, s => { _craftShare = s; LayoutCraftChase(); });
        else { Motion.ForgetShare("craft-chase"); _craftShare = 0; LayoutCraftChase(); }
        CraftChaseFill.Effect ??= QGlow("Mint", 10, 0.6);
        CraftProfit.SetResourceReference(TextBlock.ForegroundProperty, known ? (net >= 0 ? "Mint" : "Coral") : "Ink");
        CraftProfit.Visibility = known ? Visibility.Visible : Visibility.Collapsed;   // before the first craft the note stands alone
        CraftProfitNote.Margin = known ? new Thickness(8, 0, 0, 7) : new Thickness(0, 0, 0, 2);
        if (Changed("craft-ring", $"{tries}|{ok}|{Skins.Current}"))
        {
            CraftRingHost.Children.Clear();
            CraftRingHost.Children.Add(tries > 0 ? QRing(76, 7, share, "Mint", $"{Math.Round(share * 100):0}%", "worked") : QRing(76, 7, 0, "Mint", "–", "no crafts yet"));
            CraftRingHost.ToolTip = tries > 0 ? $"{ok:N0} of {tries:N0} crafts worked this session." : null;
        }
        // the chips
        CraftChips.Visibility = tries > 0 ? Visibility.Visible : Visibility.Collapsed;
        if (tries > 0 && Changed("craft-chips", $"{tries}|{ok}|{itemsMade}|{spent}|{worth}|{atMost}|{Skins.Current}"))
        {
            QChipFill(ChipCrafts, QIconBox("Hammer", "Lilac"), $"{tries:N0} craft{(tries == 1 ? "" : "s")}", itemsMade > 0 ? $"{itemsMade:N0} thing{(itemsMade == 1 ? "" : "s")} made" : $"{ok:N0} worked");
            ChipCrafts.ToolTip = $"{tries:N0} crafts this session: {ok:N0} worked, {tries - ok:N0} failed" + (itemsMade > 0 ? $", {itemsMade:N0} things made." : ".");
            QChipFill(ChipSpent, QIconBox("Coin", "Coral"), spent > 0 ? Fk(spent) : "–", "spent on materials");
            ChipSpent.ToolTip = spent > 0 ? $"{spent:N0} Kinah of materials at the lowest Market price when each craft was made; a failed craft uses them too." + (atMost ? " At least: a material has no price yet." : "") : "Materials show once a recipe is in the book.";
            QChipFill(ChipSells, QIconBox("Sack", "Mint"), worth > 0 ? Fk(worth) : "–", "sells for, after the cut");
            ChipSells.ToolTip = worth > 0 ? $"What came out sells for {worth:N0} at today's lowest Market price, {PriceBook.Cut}." : "Shows once what came out has a Market price.";
        }
        // Next up
        var next = new List<FrameworkElement>();
        if (known && net > 0)
        {
            long target = NextRound(net), togo = target - net;
            var best = _craftPerRecipe.Where(x => x.Ok > 0 && x.Profit > 0).Select(x => (x.Name, Per: x.Profit / (double)x.Ok)).OrderByDescending(x => x.Per).FirstOrDefault();
            var sub = $"{Fk(togo)} away" + (best.Name != null && best.Per > 0 ? $" · one {best.Name} that works (+{Fk(best.Per)})" : "");
            var row = QRow(QIconBox("Sack", "Mint", 28, round: true), $"+{Fk(target)} profit", sub, $"{Math.Round(100.0 * net / target):0}%", "Mint", net / (double)target, "Mint", rule: false, inline: true);
            row.Cursor = Cursors.Arrow; row.ToolTip = $"{Signed(net)} so far, {PriceBook.Cut}. The next round figure turns over by itself.";
            // it names the recipe that gets there fastest: a click opens that recipe
            if (best.Name != null && best.Per > 0) { var bn = best.Name; row.Cursor = Cursors.Hand; row.ToolTip += $" Click for {bn}."; row.MouseLeftButtonUp += (_, _) => OpenCraftWindow(bn); }
            next.Add(row);
        }
        else if (known && net < 0 && spent > 0)
        {
            var row = QRow(QIconBox("Sack", "Coral", 28, round: true), "Back to even", $"{Fk(-net)} away · a worked craft that sells for more than its materials", $"{Math.Round(100.0 * worth / spent):0}%", "Coral", worth / (double)spent, "Coral", rule: false, inline: true);
            row.Cursor = Cursors.Arrow; row.ToolTip = $"{Signed(net)} so far: {Fk(worth)} back from {Fk(spent)} spent.";
            next.Add(row);
        }
        if (_bestTop is { } bt)
        {
            var sub = $"+{Fk(bt.Profit)} a craft at {bt.Success:P0}" + (bt.Makes > 1 ? $" · makes ×{bt.Makes}" : "") + $" · costs {Fk(bt.Cost)}";
            var row = QRow(QItemIcon(bt.Name, bt.Id, 28, round: true, grade: GradeOf(bt.Name)), $"{bt.Name} pays most today", sub, SecOpen("Best") ? "fold" : $"all {bt.Count}", "Muted", rule: next.Count > 0, inline: true);
            var g = (Grid)row.Child;
            var chev = QIcon("Chev", 13, "Faint", 2); chev.Margin = new Thickness(4, 0, 0, 0); chev.RenderTransformOrigin = new Point(0.5, 0.5); QSetChev(chev, SecOpen("Best"));
            var right = g.Children.OfType<TextBlock>().FirstOrDefault(t => Grid.GetColumn(t) == 2);
            if (right != null) { g.Children.Remove(right); var sp = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center }; sp.Children.Add(right); sp.Children.Add(chev); Grid.SetColumn(sp, 2); g.Children.Add(sp); }
            row.ToolTip = $"Today's most profitable recipe on {Server?.Name ?? "your server"} at the lowest Market asks. Click for its recipe; ‘all {bt.Count}’ opens the list below.";
            // the recipe opens in the craft window; the "all 6" with its chevron opens Best crafts below
            var btName = bt.Name;
            if (right?.Parent is Panel allBox) { allBox.Cursor = Cursors.Hand; allBox.Background = Brushes.Transparent; allBox.MouseLeftButtonUp += (_, e) => { e.Handled = true; ToggleSec("Best"); }; }
            row.MouseLeftButtonUp += (_, e) => { if (!e.Handled) OpenCraftWindow(btName); };
            next.Add(row);
        }
        // the quests (phase 1, Ui/MainWindow.Quests.cs): the two crafting materials you are farming, as Next up on Farming has them, with their trees
        RefreshQuests();
        var matKeys = _questsNow.Where(q => q.Kind == QuestKind.Material && q.Horizon == Horizon.Session).Take(2).Select(q => q.Key).ToHashSet();
        foreach (var (el, _) in QuestRows(q => matKeys.Contains(q.Key), rule: next.Count > 0, toggled: () => FillCraft(CraftShown))) next.Add(el);
        CraftNextCard.Visibility = next.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        if (next.Count > 0)
        {
            CraftNextHost.Children.Clear();
            CraftNextHost.Children.Add(QHead("Trophy", "Next up", _bestTop != null ? "today's prices" : "at this pace"));
            foreach (var r in next) CraftNextHost.Children.Add(r);
        }
        // the recipes this session as small cards, three a row
        CraftRecipeHost.Children.Clear();
        foreach (var r in rows.Where(x => !x.Child && !x.Header))
        {
            int attempts = (int)Qty(r.Drops), okN = (int)Qty(r.DropPct);
            var sub = r.Under.Length > 0 ? r.Under : $"{attempts:N0} craft{(attempts == 1 ? "" : "s")} · {okN:N0} worked";
            var tile = QRecipeCard(QItemIcon(r.Name, _items.IdOf(r.Name), 30, round: false, grade: r.Grade), r.Name, sub, r.Value, r.Tone == "Bad" ? "Coral" : r.Value.Length > 0 ? "Mint" : "Faint");
            tile.ToolTip = r.Tip; ToolTipService.SetInitialShowDelay(tile, 300);
            var name = r.Name;
            tile.MouseLeftButtonUp += (_, _) => OpenCraftWindow(name);
            CraftRecipeHost.Children.Add(tile);
        }
    }

    double _craftShare;
    void LayoutCraftChase() => CraftChaseFill.Width = Math.Max(0, CraftChaseTrack.ActualWidth * _craftShare);

    /// <summary>A recipe's card: icon, name, the crafts line, the profit big in mint (coral for a loss); 205 px, three a row.</summary>
    static Border QRecipeCard(FrameworkElement icon, string name, string sub, string profit, string tone)
    {
        var sp = new StackPanel();
        icon.HorizontalAlignment = HorizontalAlignment.Left; sp.Children.Add(icon);
        var n = QT(name, 12.5, FontWeights.SemiBold, "Ink"); n.Margin = new Thickness(0, 6, 0, 0); n.TextWrapping = TextWrapping.Wrap; n.MaxHeight = 34; n.LineHeight = 16; n.LineStackingStrategy = LineStackingStrategy.BlockLineHeight; sp.Children.Add(n);
        sp.Children.Add(QT(sub, 11, FontWeights.Medium, "Muted", trim: true));
        var p = QT(profit.Length > 0 ? profit : "no price yet", 15, FontWeights.Bold, tone); p.Margin = new Thickness(0, 4, 0, 0); sp.Children.Add(p);
        var card = new Border { Width = 205, Padding = new Thickness(10), CornerRadius = new CornerRadius(12), Child = sp, Cursor = Cursors.Hand, Margin = new Thickness(0, 0, 8, 8) };
        card.SetResourceReference(Border.BackgroundProperty, "Card2");
        card.MouseEnter += (_, _) => card.SetResourceReference(Border.BackgroundProperty, "ItemSel");
        card.MouseLeave += (_, _) => card.SetResourceReference(Border.BackgroundProperty, "Card2");
        return card;
    }

    /// <summary>A craft line's icon: the item with a mint check or a coral cross in the corner, so a run of failures reads as a pattern.</summary>
    FrameworkElement QCraftIcon(FeedLine l)
    {
        var g = new Grid { Width = 30, Height = 30 };
        var pic = QItemIcon(l.Name, l.ItemId != 0 ? l.ItemId : _items.IdOf(l.Name), 28, round: false, grade: GradeOf(l.Name));
        pic.HorizontalAlignment = HorizontalAlignment.Left; pic.VerticalAlignment = VerticalAlignment.Top;
        g.Children.Add(pic);
        if (l.Kind == "craft")
        {
            var dot = new Border { Width = 15, Height = 15, CornerRadius = new CornerRadius(7.5), HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom, BorderThickness = new Thickness(2) };
            dot.SetResourceReference(Border.BackgroundProperty, l.Ok ? "Mint" : "Coral");
            dot.SetResourceReference(Border.BorderBrushProperty, "Card");
            dot.Child = QIcon(l.Ok ? "Check" : "X", 7, "Card", 3);
            g.Children.Add(dot);
        }
        return g;
    }
}
