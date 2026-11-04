using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using KinahLedger.Data;
using KinahLedger.Session;

namespace KinahLedger.Ui;

/// <summary>
/// A click on a recipe in the Crafting part opens its breakdown, in the game-tooltip look Steven picked (2026-10-22): the
/// recipe in a band of its grade colour, the listed success against fail as a bar, the materials as inventory slots
/// with count badges and their cost, the cost per craft, this session's crafts and what came out, what it sells for and
/// its upgrades, and the profit per craft in the footer. Vendor materials (never on the Market) take the NPC price
/// shipped with the data, or a cost typed in their slot, kept on this PC, so the recipe gets a full cost. A material
/// ticked "I have these" (farmed, gathered or owned) costs nothing in cash; the card then says what selling it would bring.
/// </summary>
public partial class MainWindow
{
    /// <summary>The recipes as the Crafting table last showed them, for the breakdown a click opens.</summary>
    List<CraftLedger.Recipe> _crafted = new();

    void CraftRowClick(object sender, MouseButtonEventArgs ev)
    {
        if (ev.OriginalSource is not FrameworkElement fe || RowOf(fe.DataContext) is not { Header: false } r) return;
        if (_card != null) _card.IsOpen = false;
        // an upgrade's row (under its recipe) opens its recipe's card
        var recipe = r.Child && r.Key.StartsWith("craft:", StringComparison.Ordinal) ? r.Key["craft:".Length..] : r.Name;
        _card = new Popup { Child = BuildCraftCard(recipe), StaysOpen = false, AllowsTransparency = true, PopupAnimation = PopupAnimation.Fade, Placement = PlacementMode.Mouse, PlacementTarget = fe };
        _card.IsOpen = true;
    }

    /// <summary>A typed vendor cost or an "I have these" tick changed the totals: the open card and the Crafting table redraw.</summary>
    void RefreshCraftCard(string output, PlayerBoards.BestCraft? best = null, int rank = 0)
    {
        if (_card is { IsOpen: true }) _card.Child = BuildCraftCard(output, best, rank);
        FillCraft(CraftShown);
    }

    /// <param name="best">Set when the card opens from Best crafts today: the recipe that list priced (how many it makes) and its rank.</param>
    internal Border BuildCraftCard(string output, PlayerBoards.BestCraft? best = null, int rank = 0)
    {
        var mine = _crafted.FirstOrDefault(c => string.Equals(c.Name, output, StringComparison.OrdinalIgnoreCase));
        // the recipe this session crafted most (Orichalcum Ingot has a x1 and a x10 with different materials), unless the card
        // opened from Best crafts, which names the one it priced
        int crafted = mine?.Tries.Where(kv => kv.Key > 0).OrderByDescending(kv => kv.Value).Select(kv => kv.Key).FirstOrDefault() ?? 0;
        var recipe = RecipeBook.For(output, best?.Makes ?? crafted);
        // prices: this PC's (its Market reads, then the board it fetched); opened from Best crafts, the site's prices fill any gap
        long? Price(string item)
        {
            if (PriceBook.Price(item) is long p) return p;
            if (best == null) return null;
            long id = _items.IdOf(item);
            if (id == best.Id && best.Price > 0) return best.Price;
            if (id == best.UpgradeId) return best.UpgradePrice;
            return best.Materials.FirstOrDefault(m => m.Id == id)?.Price;
        }
        void Redo() => RefreshCraftCard(output, best, rank);
        var grade = GradeOf(output);
        var (card, body) = CkShell(460);
        body.Children.Add(CkBand(output, grade, recipe == null ? $"“{output}” is not in the recipe book yet" : string.Join(" · ", new[] { recipe.Skill, grade, $"makes ×{recipe.OutputQty}" }.Where(x => x.Length > 0))));
        if (recipe == null) { body.Children.Add(CkNote($"The recipe book has no recipe for “{output}”, so its materials and cost can't show. They will once the book has it.")); body.Children.Add(new Border { Height = 14 }); return card; }
        // the verdict leads: profit per craft right under the name, filled in once the cost is known (below)
        var top = new StackPanel();
        body.Children.Add(top);
        // what is live for the recipe (direction C): where it ranks on the server today and its upgrade, when the site has it
        var upgrade = recipe.Upgrade;
        if (rank > 0 || (best != null && upgrade != null))
        {
            var lv = new StackPanel();
            var cap = new Grid { Margin = new Thickness(12, 6, 12, 2) };
            cap.ColumnDefinitions.Add(new ColumnDefinition()); cap.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            cap.Children.Add(CkText($"TODAY ON {(Server?.Name ?? "YOUR SERVER").ToUpperInvariant()}", CkGoldRule, 11));
            var age = CkText($"price read {PriceBook.Ago(PlayerBoards.CraftsBoardAt ?? PlayerBoards.CraftsAt ?? DateTime.UtcNow, DateTime.UtcNow)}", CkFaint, 11.5); Grid.SetColumn(age, 1); cap.Children.Add(age);
            lv.Children.Add(cap);
            void Live(string left, string mid, string right, bool first)
            {
                var g = new Grid { Height = 32, Margin = new Thickness(12, 0, 12, 0) };
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(44) }); g.ColumnDefinitions.Add(new ColumnDefinition()); g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                g.Children.Add(CkText(left, first ? CkGold : CkFaint, 11.5, mono: true));
                var m = CkText(mid, CkInk, 13); Grid.SetColumn(m, 1); g.Children.Add(m);
                var r = CkText(right, CkGold, 12.5, mono: true); Grid.SetColumn(r, 2); g.Children.Add(r);
                lv.Children.Add(new Border { Child = g, BorderThickness = new Thickness(0, first ? 0 : 1, 0, 0), BorderBrush = Ck(CkSlotLine, 0x73) });
            }
            if (rank > 0) Live($"#{rank}", $"of every recipe on {Server?.Name ?? "your server"}, by profit a craft", best is { } b0 ? (b0.Profit >= 0 ? "+" : "−") + Fk(Math.Abs(b0.Profit)) : "", true);
            if (upgrade != null && UpgradeChance(recipe, upgrade) is double uc) Live($"{uc:P0}", $"upgrade to {upgrade.Item}", "", rank <= 0);
            body.Children.Add(new Border { Child = lv, Margin = new Thickness(16, 14, 16, 4), Padding = new Thickness(0, 2, 0, 4), CornerRadius = new CornerRadius(8), BorderThickness = new Thickness(1), BorderBrush = Ck(CkSlotLine), Background = Ck(CkSlot, 0xA6) });
        }
        if (recipe.SuccessRate is double sr) body.Children.Add(CkOddsBar(sr));

        // the materials, as inventory slots: icon with the count, name, and what the craft's share costs
        body.Children.Add(CkSection("MATERIALS PER CRAFT"));
        var slots = new WrapPanel { Margin = new Thickness(20, 0, 6, 0) };
        long total = recipe.Kinah ?? 0; int unpriced = 0;
        // the ticked materials, for what selling them instead would bring (vendor ones can't be sold on the Market)
        var farmed = new List<(string Item, int Qty, long? Price)>();
        foreach (var m in recipe.Materials)
        {
            bool vendor = m.Item.Contains("(Bound)", StringComparison.OrdinalIgnoreCase);
            bool have = PriceBook.Farmed(m.Item);
            long? each = vendor ? PriceBook.VendorPrice(m.Item) : Price(m.Item);
            var row = new Grid();
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition());
            var icon = CkIcon(m.Item, 46, CkFrame, m.Qty.ToString("N0")); icon.Margin = new Thickness(0, 0, 10, 0);
            row.Children.Add(icon);
            var info = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            info.Children.Add(CkText(m.Item.Replace(" (Bound)", ""), GradeColor(GradeOf(m.Item)), 13));
            if (have)
            {
                // free in cash: "farmed · 0", with what it would cost dimmed beside it
                var fp = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 2, 0, 0) };
                fp.Children.Add(CkText("farmed · 0", Color.FromRgb(0x5E, 0xD8, 0xCB), 12.5, mono: true));
                if (each is long ref1) { var dim = CkText(vendor ? $"NPC {Amounts.Short(ref1)}" : $"Market {Amounts.Short(ref1)}", CkFaint, 11.5, mono: true); dim.ToolTip = Amounts.Full(ref1) + " each"; dim.Margin = new Thickness(8, 0, 0, 0); fp.Children.Add(dim); }
                info.Children.Add(fp);
                if (!vendor) farmed.Add((m.Item, m.Qty, each));
            }
            else if (vendor) info.Children.Add(VendorCost(m.Item, output, best, rank));
            else
            {
                // the price each, clickable to type what the game shows, and where it is from under it (unsure ones dimmed, left out)
                var mq = PriceBook.QuoteOf(m.Item);
                var ed = CkPriceEditor(m.Item, mq ?? (each is long bp ? new PriceBook.Quote(bp, PriceBook.From.Server, PriceBook.SureReadings, true, PlayerBoards.CraftsAt) : null), Redo, 1, 12.5);
                ed.HorizontalAlignment = HorizontalAlignment.Left;
                info.Children.Add(ed);
                if (CardSourceOnLine && mq != null) info.Children.Add(CkText(PriceBook.Describe(mq), mq.Mine ? Color.FromRgb(0x5E, 0xD8, 0xCB) : mq.Sure ? CkFaint : CkWarn, 11));
                else if (mq == null && each == null) info.Children.Add(CkText("no Market price yet", CkFaint, 11.5));
            }
            info.Children.Add(HaveTick(m.Item, have, Redo));
            // the cash cost: a ticked material adds nothing
            if (!have) { if (each is long e2) total += e2 * m.Qty; else unpriced++; }
            Grid.SetColumn(info, 1); row.Children.Add(info);
            slots.Children.Add(Sized(CkBox(row, new Thickness(0, 0, 14, 10), new Thickness(8)), 196));
        }
        body.Children.Add(slots);
        if (recipe.Kinah is long fee && fee > 0) body.Children.Add(CkTip(CkKv("Crafting fee", Amounts.Short(fee), CkGold), $"{Amounts.Full(fee)} kinah a craft"));

        // the cost per craft, boxed like the game's totals
        var tot = new Grid();
        tot.ColumnDefinitions.Add(new ColumnDefinition()); tot.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var tl = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        tl.Children.Add(CkText("Cost per craft", CkMuted, 13.5));
        if (unpriced > 0) tl.Children.Add(CkText(unpriced == 1 ? "one price unsure or missing, left out: click it to type yours" : $"{unpriced} prices unsure or missing, left out", CkWarn, 11.5));
        tot.Children.Add(tl);
        // a floor says so: "at least 246,619"; with nothing priced it is "no price yet", never "0+" (the CI render of 2026-10-24)
        var tv = total <= 0 && unpriced > 0 ? CkText("no price yet", CkFaint, 15) : CkText((unpriced > 0 ? "at least " : "") + Amounts.Short(total), CkGold, 19, FontWeights.SemiBold, mono: true);
        tv.ToolTip = Amounts.Full(total) + " kinah a craft";
        Grid.SetColumn(tv, 1); tot.Children.Add(tv);
        body.Children.Add(CkBox(tot, new Thickness(20, 4, 20, 0)));

        // this session: tries and how many worked, what came out (hidden until it has been crafted)
        if (mine is { Attempts: > 0 })
        {
            body.Children.Add(CkSection("THIS SESSION"));
            double sessRate = (double)mine.Ok / mine.Attempts;
            body.Children.Add(CkKv($"{mine.Attempts} craft{(mine.Attempts == 1 ? "" : "s")} · {mine.Ok} worked · {mine.Fail} failed", $"{sessRate:P0}", mine.Attempts < CraftMath.ObservedFrom ? CkInk : sessRate >= (recipe.SuccessRate ?? 0) ? CkGood : CkBad,
                recipe.SuccessRate is double lr ? $"listed {lr:P0}" : ""));
            // the upgrade rolls the crafting panel showed after a craft worked
            if (mine.Upgraded + mine.NotUpgraded > 0) body.Children.Add(CkKv($"{(Session.CraftLedger.DoublesOnCombo(mine.Name) ? "Combo rolls (×2)" : "Upgrade rolls")} · {mine.Upgraded} won · {mine.NotUpgraded} lost", $"{mine.Upgraded}/{mine.Upgraded + mine.NotUpgraded}", mine.Upgraded > 0 ? CkGood : CkMuted));
            if (mine.Made.Count > 0)
            {
                var came = new WrapPanel { Margin = new Thickness(20, 6, 14, 0) };
                foreach (var (item, n) in mine.Made.OrderByDescending(x => x.Value)) came.Children.Add(CkChip($"{item} ×{n}", GradeColor(GradeOf(item))));
                body.Children.Add(came);
            }
            if (unpriced == 0)
            {
                // tries of the recipe on the card at its cost; tries of another variant of the item at that variant's own
                var elsewhere = mine.Tries.Where(kv => kv.Key != 0 && kv.Key != recipe.OutputQty).ToDictionary(kv => kv.Key, kv => kv.Value);
                long spentMats = total * (mine.Attempts - elsewhere.Values.Sum());
                if (elsewhere.Count == 0) body.Children.Add(CkTip(CkKv("Spent on materials", Amounts.Short(spentMats), CkInk), Amounts.Full(spentMats) + " kinah"));
                else if (RecipeBook.CostOfTries(output, elsewhere, out int eu, out int ev, out _) is long ec && eu + ev == 0) body.Children.Add(CkTip(CkKv("Spent on materials", Amounts.Short(spentMats + ec), CkInk), Amounts.Full(spentMats + ec) + " kinah"));
            }
        }
        long Sells(string item, int qty) => Price(item) is long p ? (long)Math.Round(p * qty * PriceBook.SellerKeeps) : -1;
        long outValue = Sells(recipe.Output, recipe.OutputQty);
        body.Children.Add(SellsLine(recipe, outValue, Redo));
        // the book's great outcomes, the upgrade inferred when the book lacks it, else the combo that makes two
        var outcomes = recipe.GreatOutcomes.ToList();
        if (recipe.Upgrade is { } inferred && !outcomes.Any(g => string.Equals(g.Item, inferred.Item, StringComparison.OrdinalIgnoreCase))) outcomes.Add(inferred);
        if (outcomes.Count == 0 && recipe.ComboChance is > 0) outcomes.Add(new RecipeBook.Great(recipe.Output, recipe.OutputQty * 2, recipe.ComboChance));
        if (outcomes.Count > 0)
        {
            var ups = new WrapPanel { Margin = new Thickness(20, 6, 14, 0) };
            foreach (var g in outcomes)
            {
                bool more = string.Equals(g.Item, recipe.Output, StringComparison.OrdinalIgnoreCase);
                var uq = more ? null : PriceBook.QuoteOf(g.Item);
                // the chance a craft that worked turns into the upgrade, the figure the profit uses (UpgradeChance); the recipe's
                // own chance is a combo's, before the combo itself has to succeed (0.25 × 0.77 = 19% for the earrings, not 25%)
                ups.Children.Add(CkChip((more ? $"Combo makes ×{g.Qty}" : $"Quality upgrade: {g.Item}") + (UpgradeChance(recipe, g) is double c ? $" · {c:P0}" : "")
                    + (uq != null ? $" · {(uq.From == PriceBook.From.Region ? "≈" : "")}{Amounts.Short(uq.Price)}{(uq.Sure ? "" : ", unsure")}" : ""), more ? Color.FromRgb(0x5E, 0xD8, 0xCB) : GradeColor(GradeOf(g.Item))));
            }
            body.Children.Add(ups);
        }

        // a price alert on what it makes, as one line (the rank on the server's best crafts is at the top)
        body.Children.Add(CkAlertLine(recipe.Output, Redo));

        // the verdict, in the top band: profit per craft at the success chance (the same sum as the site's best crafts: CraftMath),
        // then profit an hour and the break-even price; the materials-for-sale line closes the card
        var (rate, kind) = CraftMath.SuccessUsed(recipe.SuccessRate, mine?.Attempts ?? 0, mine?.Ok ?? 0);
        if (unpriced == 0 && CraftMath.Judge(recipe, Price, total, rate, PriceBook.SellerKeeps) is { } v)
        {
            double per = v.Profit;
            // the chance counted: the listed one, the player's own once it differs over 20 crafts, or measured; unknown is an upper bound
            string chance = kind switch
            {
                CraftMath.RateKind.Yours => $"{recipe.SuccessRate:P0} listed · {rate:P0} yours",
                CraftMath.RateKind.Observed => $"at {rate:P0} success, from {mine!.Attempts} crafts",
                CraftMath.RateKind.Unknown => "success chance unknown",
                _ => $"at {rate:P0} success",
            };
            top.Children.Add(CraftVerdictBand(kind == CraftMath.RateKind.Unknown ? "Profit per craft, at most" : "Profit per craft",
                $"{chance}{(v.Combo > 0 ? (v.Doubles ? $" · {v.Combo:P0} combo ×2" : $" · {v.Combo:P0} upgrade chance") : "")} · {PriceBook.Fee}", per, upTo: kind == CraftMath.RateKind.Unknown));
            if (v.UpgradeUnpriced) top.Children.Add(CkNote($"{recipe.Upgrade?.Item ?? "The upgrade"} has no price: upgrade unpriced, counted as the base item."));
            if (mine is { Attempts: >= 3 } && CraftsPerHour() is double cph)
                top.Children.Add(CkKv(kind == CraftMath.RateKind.Unknown ? "Profit per hour, at most" : "Profit per hour", (kind == CraftMath.RateKind.Unknown ? "up to " : "") + Signed(per * cph), per >= 0 ? CkGood : CkBad, $"{cph:0.#} crafts an hour this session", rightSize: 15));
            if (v.BreakEven is double be)
                top.Children.Add(CkKv("Break-even sell price", Amounts.Short(be), CkGold, kind == CraftMath.RateKind.Unknown ? "each, if every craft worked (a floor)" : "each, at this success chance and yield"));
            top.Children.Add(new Border { Height = 4 });
            SellTheMats(body, farmed, per);
        }
        else
        {
            top.Children.Add(CkNote(unpriced > 0 ? "Profit shows once every price is sure: click one to type what you see." : "Profit shows once it has a sure Market price."));
            top.Children.Add(new Border { Height = 6 });
            SellTheMats(body, farmed, null);
            body.Children.Add(new Border { Height = 6 });
        }
        body.Children.Add(CkLinks(("Recipe data: MetaBot.GG", "https://metabot.gg/en/aion-2/crafting")));
        return card;
    }

    /// <summary>
    /// With materials ticked "I have these": what selling them on the Market would bring per craft instead, after its cut,
    /// and which way wins against the cash profit per craft. Needs a sure price for every ticked material.
    /// </summary>
    static void SellTheMats(StackPanel body, List<(string Item, int Qty, long? Price)> farmed, double? per)
    {
        if (farmed.Count == 0) return;
        if (farmed.FirstOrDefault(f => f.Price == null) is { Item: { } gap }) { body.Children.Add(CkNote($"Sell-the-mats line needs a price for {gap}.")); return; }
        long sell = (long)Math.Round(farmed.Sum(f => f.Price!.Value * f.Qty * PriceBook.SellerKeeps));
        body.Children.Add(CkTip(CkKv("Or sell the mats instead", $"{Amounts.Short(sell)} per craft", CkGold, $"{PriceBook.Fee}"), $"{Amounts.Full(sell)} kinah per craft"));
        if (per is not double p) return;
        double z = p - sell;
        body.Children.Add(CkNote(z >= 0 ? $"Crafting beats selling the mats by {Amounts.Short(z)} per craft." : $"Selling the mats beats crafting by {Amounts.Short(-z)} per craft."));
    }

    /// <summary>"I have these": a material the player farmed, gathered or already owns costs nothing in cash.</summary>
    FrameworkElement HaveTick(string item, bool on, Action redo)
    {
        var cb = new CheckBox
        {
            IsChecked = on, Content = CkText("I have these", CkMuted, 11.5), Margin = new Thickness(0, 4, 0, 0), HorizontalAlignment = HorizontalAlignment.Left,
            ToolTip = "Counts as free: you farmed, gathered or already own these, so no kinah goes out. Untick to price them at the Market.",
        };
        cb.Click += (_, _) => { PriceBook.SetFarmed(item, cb.IsChecked == true); Dispatcher.BeginInvoke(redo); };
        return cb;
    }

    /// <summary>The card's verdict under the recipe name: the label with its detail under it, and the profit per craft big, green or red.</summary>
    static Border CraftVerdictBand(string label, string sub, double per, bool upTo = false)
    {
        bool good = per >= 0;
        var g = new Grid();
        g.ColumnDefinitions.Add(new ColumnDefinition()); g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var l = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        l.Children.Add(CkText(label, CkMuted, 13.5));
        if (sub.Length > 0) l.Children.Add(CkText(sub, CkFaint, 11.5));
        g.Children.Add(l);
        var v = CkText((upTo ? "up to " : "") + Signed(per), good ? CkGood : CkBad, 26, FontWeights.SemiBold, display: true); Grid.SetColumn(v, 1); g.Children.Add(v);
        v.ToolTip = (per >= 0 ? "+" : "−") + Amounts.Full(Math.Abs(per)) + " a craft";
        var tint = good ? Color.FromRgb(0x0F, 0x1E, 0x16) : Color.FromRgb(0x1A, 0x0F, 0x12);
        return new Border
        {
            Child = g, Margin = new Thickness(0, 0, 0, 8), Padding = new Thickness(20, 10, 20, 12), BorderThickness = new Thickness(0, 0, 0, 1), BorderBrush = Ck(CkSlotLine),
            Background = new LinearGradientBrush(tint, Color.FromRgb((byte)(tint.R * 0.7), (byte)(tint.G * 0.7), (byte)(tint.B * 0.7)), 90),
        };
    }

    /// <summary>A tooltip on a card line: the full figure the line shows short.</summary>
    static FrameworkElement CkTip(FrameworkElement e, string tip) { e.ToolTip = tip; return e; }

    /// <summary>
    /// What the recipe sells for, as one line: the net figure after the Market's cut first, the gross Market price under it
    /// (clickable, to type what the game shows) with its source, and how many a craft makes.
    /// </summary>
    FrameworkElement SellsLine(RecipeBook.Recipe recipe, long net, Action redo)
    {
        var q = PriceBook.QuoteOf(recipe.Output);
        var g = new Grid { Margin = new Thickness(20, 3, 20, 3) };
        g.ColumnDefinitions.Add(new ColumnDefinition()); g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var left = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        left.Children.Add(CkText($"Sells for, {PriceBook.Fee}", CkInk, 13.5));
        var sub = new StackPanel { Orientation = Orientation.Horizontal };
        sub.Children.Add(CkText(q == null ? $"no Market price on {Server?.Name ?? "your server"} yet:  " : "Market price  ", CkFaint, 11.5));
        sub.Children.Add(CkPriceEditor(recipe.Output, q, redo, 1, 11.5));
        sub.Children.Add(CkText(q == null ? "" : recipe.OutputQty > 1 ? $"  each · {recipe.OutputQty} made per craft" : "  each", CkFaint, 11.5));
        left.Children.Add(sub);
        if (q != null && CardSourceOnLine) left.Children.Add(CkText(PriceBook.Describe(q), q.Mine ? Color.FromRgb(0x5E, 0xD8, 0xCB) : q.Sure ? CkFaint : CkWarn, 11));
        if (q is { Sure: false }) left.Opacity = 0.75;
        g.Children.Add(left);
        var right = net >= 0 ? CkText(Amounts.Short(net), CkGold, 13.5, FontWeights.SemiBold, mono: true) : CkText("no sure price yet", CkFaint, 13.5, FontWeights.SemiBold);
        if (net >= 0) right.ToolTip = $"{Amounts.Full(net)} kinah for the {recipe.OutputQty} a craft makes, {PriceBook.Fee}";
        right.Margin = new Thickness(12, 0, 0, 0); right.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(right, 1); g.Children.Add(right);
        if (q != null) g.ToolTip = PriceHover(recipe.Output, q);
        return g;
    }

    static Border Sized(Border b, double width) { b.Width = width; return b; }

    /// <summary>
    /// A vendor material's cost, typed in its slot and kept on this PC: the NPC price shipped with the data ("NPC 880")
    /// until the player types their own, "set NPC cost" when there is neither.
    /// </summary>
    FrameworkElement VendorCost(string item, string output, PlayerBoards.BestCraft? best, int rank)
    {
        long? typed = PriceBook.TypedVendorPrice(item), shipped = PriceBook.ShippedVendorPrice(item);
        var box = new TextBox
        {
            Width = 120, FontSize = 12.5, Padding = new Thickness(0), BorderThickness = new Thickness(0, 0, 0, 1), Background = Brushes.Transparent,
            BorderBrush = Ck(CkWarn, 0x88), Foreground = Ck(typed != null ? CkGold : CkWarn), CaretBrush = Ck(CkGold), FontFamily = new FontFamily("Cascadia Mono, Consolas"),
            Text = typed is long v ? $"{v:N0} ea" : "", ToolTip = "What the NPC charges for one. Type yours to change it; kept on this PC.", HorizontalAlignment = HorizontalAlignment.Left,
        };
        var hint = shipped is long sp ? CkText($"NPC {sp:N0}", CkGold, 12.5, mono: true) : CkText("set NPC cost", CkWarn, 12.5); hint.IsHitTestVisible = false;
        hint.Visibility = box.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        box.GotKeyboardFocus += (_, _) => { hint.Visibility = Visibility.Collapsed; box.Text = new string(box.Text.Where(char.IsDigit).ToArray()); };
        void Commit()
        {
            long n = long.TryParse(new string(box.Text.Where(char.IsDigit).ToArray()), out var x) ? x : 0;
            if (n == (PriceBook.TypedVendorPrice(item) ?? 0)) { if (n > 0) box.Text = $"{n:N0} ea"; else hint.Visibility = Visibility.Visible; return; }
            PriceBook.SetVendorPrice(item, n);
            Dispatcher.BeginInvoke(() => RefreshCraftCard(output, best, rank));
        }
        box.LostKeyboardFocus += (_, _) => Commit();
        box.KeyDown += (_, e) => { if (e.Key == Key.Enter) Commit(); };
        var g = new Grid { Margin = new Thickness(0, 2, 0, 0) };
        g.Children.Add(box); g.Children.Add(hint);
        return g;
    }

    /// <summary>For layout review: the breakdown for the first crafted recipe, drawn to a PNG.</summary>
    /// <summary>
    /// How often a craft that worked turns into this upgrade: the combo's chance times the chance the combo then succeeds
    /// (the site's best crafts and the profit per craft use the same: CraftMath). Null when the recipe doesn't list one.
    /// </summary>
    static double? UpgradeChance(RecipeBook.Recipe recipe, RecipeBook.Great g) => CraftMath.UpgradeChance(recipe, g);

    internal void ShotCraftCard(string path)
    {
        if (CraftRows.Items.Count == 0 || CraftRows.Items[0] is not DropRow r) return;
        SaveCard(BuildCraftCard(r.Name), path);
    }

    void SaveCard(Border b, string path)
    {
        b.Resources = Resources;
        b.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity)); b.Arrange(new Rect(b.DesiredSize)); b.UpdateLayout();
        var bmp = new System.Windows.Media.Imaging.RenderTargetBitmap((int)Math.Ceiling(b.ActualWidth + 20), (int)Math.Ceiling(b.ActualHeight + 20), 96, 96, PixelFormats.Pbgra32);
        bmp.Render(b);
        var enc = new System.Windows.Media.Imaging.PngBitmapEncoder(); enc.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bmp));
        using var f = File.Create(path); enc.Save(f);
    }
}
