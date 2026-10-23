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
/// its upgrades, and the profit per craft in the footer. Vendor materials (never on the Market) take a cost typed in
/// their slot, kept on this PC, so the recipe gets a full cost.
/// </summary>
public partial class MainWindow
{
    /// <summary>The recipes as the Crafting table last showed them, for the breakdown a click opens.</summary>
    List<CraftLedger.Recipe> _crafted = new();

    void CraftRowClick(object sender, MouseButtonEventArgs ev)
    {
        if (ev.OriginalSource is not FrameworkElement fe || fe.DataContext is not DropRow { Header: false } r) return;
        if (_card != null) _card.IsOpen = false;
        _card = new Popup { Child = BuildCraftCard(r.Name), StaysOpen = false, AllowsTransparency = true, PopupAnimation = PopupAnimation.Fade, Placement = PlacementMode.Mouse, PlacementTarget = fe };
        _card.IsOpen = true;
    }

    /// <summary>A typed vendor cost changed the totals: the open card and the Crafting table redraw.</summary>
    void RefreshCraftCard(string output)
    {
        if (_card is { IsOpen: true }) _card.Child = BuildCraftCard(output);
        FillCraft(_watcher.Chat?.CraftView);
    }

    internal Border BuildCraftCard(string output)
    {
        var recipe = RecipeBook.ForOutput(output);
        var mine = _crafted.FirstOrDefault(c => string.Equals(c.Name, output, StringComparison.OrdinalIgnoreCase));
        var grade = GradeOf(output);
        var (card, body) = CkShell(460);
        body.Children.Add(CkBand(output, grade, recipe == null ? "Not in the recipe book yet" : string.Join(" · ", new[] { recipe.Skill, grade, $"makes ×{recipe.OutputQty}" }.Where(x => x.Length > 0))));
        if (recipe == null) { body.Children.Add(CkNote("Its materials and cost show once the recipe book has it.")); body.Children.Add(new Border { Height = 14 }); return card; }
        if (recipe.SuccessRate is double sr) body.Children.Add(CkOddsBar(sr));

        // the materials, as inventory slots: icon with the count, name, and what the craft's share costs
        body.Children.Add(CkSection("MATERIALS PER CRAFT"));
        var slots = new WrapPanel { Margin = new Thickness(20, 0, 6, 0) };
        long total = recipe.Kinah ?? 0; int unpriced = 0;
        foreach (var m in recipe.Materials)
        {
            bool vendor = m.Item.Contains("(Bound)", StringComparison.OrdinalIgnoreCase);
            long? each = vendor ? PriceBook.VendorPrice(m.Item) : PriceBook.Price(m.Item);
            var row = new Grid();
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition());
            var icon = CkIcon(m.Item, 46, CkFrame, m.Qty.ToString("N0")); icon.Margin = new Thickness(0, 0, 10, 0);
            row.Children.Add(icon);
            var info = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            info.Children.Add(CkText(m.Item.Replace(" (Bound)", ""), GradeColor(GradeOf(m.Item)), 13));
            if (vendor) info.Children.Add(VendorCost(m.Item, each, output));
            else if (each is long p) info.Children.Add(CkText($"{(PriceBook.SourceOf(m.Item) == PriceBook.From.Region ? "≈" : "")}{p:N0} ea · {p * m.Qty:N0}", CkGold, 12.5, mono: true));
            else info.Children.Add(CkText("no Market price yet", CkFaint, 12));
            if (each is long e2) total += e2 * m.Qty; else unpriced++;
            Grid.SetColumn(info, 1); row.Children.Add(info);
            slots.Children.Add(Sized(CkBox(row, new Thickness(0, 0, 14, 10), new Thickness(8)), 196));
        }
        body.Children.Add(slots);
        if (recipe.Kinah is long fee && fee > 0) body.Children.Add(CkKv("Crafting fee", fee.ToString("N0"), CkGold));

        // the cost per craft, boxed like the game's totals
        var tot = new Grid();
        tot.ColumnDefinitions.Add(new ColumnDefinition()); tot.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var tl = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        tl.Children.Add(CkText("Cost per craft", CkMuted, 13.5));
        if (unpriced > 0) tl.Children.Add(CkText(unpriced == 1 ? "one material still needs a cost" : $"{unpriced} materials still need a cost", CkWarn, 11.5));
        tot.Children.Add(tl);
        var tv = CkText(total.ToString("N0") + (unpriced > 0 ? "+" : ""), CkGold, 19, FontWeights.SemiBold, mono: true); Grid.SetColumn(tv, 1); tot.Children.Add(tv);
        body.Children.Add(CkBox(tot, new Thickness(20, 4, 20, 0)));

        // this session: tries and how many worked, what came out, and what one sells for
        body.Children.Add(CkSection("THIS SESSION"));
        if (mine == null || mine.Attempts == 0) body.Children.Add(CkNote("Not crafted since the tracker started."));
        else
        {
            double rate = (double)mine.Ok / mine.Attempts;
            body.Children.Add(CkKv($"{mine.Attempts} craft{(mine.Attempts == 1 ? "" : "s")} · {mine.Ok} worked · {mine.Fail} failed", $"{rate:P0}", rate >= 0.5 ? CkGood : CkBad));
            // the upgrade rolls the crafting panel showed after a craft worked
            if (mine.Upgraded + mine.NotUpgraded > 0) body.Children.Add(CkKv($"Upgrade rolls · {mine.Upgraded} won · {mine.NotUpgraded} lost", $"{mine.Upgraded}/{mine.Upgraded + mine.NotUpgraded}", mine.Upgraded > 0 ? CkGood : CkMuted));
            if (mine.Made.Count > 0)
            {
                var came = new WrapPanel { Margin = new Thickness(20, 6, 14, 0) };
                foreach (var (item, n) in mine.Made.OrderByDescending(x => x.Value)) came.Children.Add(CkChip($"{item} ×{n}", GradeColor(GradeOf(item))));
                body.Children.Add(came);
            }
            if (unpriced == 0) body.Children.Add(CkKv("Spent on materials", (total * mine.Attempts).ToString("N0"), CkInk));
        }
        long Sells(string item, int qty) => PriceBook.Price(item) is long p ? (long)Math.Round(p * qty * PriceBook.SellerKeeps) : -1;
        long outValue = Sells(recipe.Output, recipe.OutputQty);
        body.Children.Add(CkKv($"Sells for, after the Market's {1 - PriceBook.SellerKeeps:P0}", outValue >= 0 ? outValue.ToString("N0") : "no price yet", outValue >= 0 ? CkGold : CkFaint));
        if (recipe.GreatOutcomes.Count > 0)
        {
            var ups = new WrapPanel { Margin = new Thickness(20, 6, 14, 0) };
            foreach (var g in recipe.GreatOutcomes)
            {
                bool more = string.Equals(g.Item, recipe.Output, StringComparison.OrdinalIgnoreCase);
                ups.Children.Add(CkChip((more ? $"Quantity upgrade ×{g.Qty}" : $"Quality upgrade: {g.Item}") + (g.Chance is double c ? $" · {c:P0}" : ""), more ? Color.FromRgb(0x5E, 0xD8, 0xCB) : GradeColor(GradeOf(g.Item))));
            }
            body.Children.Add(ups);
        }

        // the profit per craft at the listed success rate, in the footer
        if (unpriced == 0 && outValue >= 0)
        {
            double rate = recipe.SuccessRate ?? (mine is { Attempts: > 0 } ? (double)mine.Ok / mine.Attempts : 1);
            double per = rate * outValue - total;
            body.Children.Add(CkFooter("Profit per craft", $"at {rate:P0} success, after the Market's cut", $"{(per >= 0 ? "+" : "−")}{Math.Abs(per):N0}", per >= 0));
        }
        else
        {
            body.Children.Add(CkNote(unpriced > 0 ? "Profit shows once every material has a cost." : "Profit shows once it has a Market price."));
            body.Children.Add(new Border { Height = 6 });
        }
        body.Children.Add(CkLinks(("Recipe data: MetaBot.GG", "https://metabot.gg/en/aion-2/crafting")));
        return card;
    }

    static Border Sized(Border b, double width) { b.Width = width; return b; }

    /// <summary>A vendor material's cost, typed in its slot: "set NPC cost" until it has one, kept on this PC.</summary>
    FrameworkElement VendorCost(string item, long? each, string output)
    {
        var box = new TextBox
        {
            Width = 120, FontSize = 12.5, Padding = new Thickness(0), BorderThickness = new Thickness(0, 0, 0, 1), Background = Brushes.Transparent,
            BorderBrush = Ck(CkWarn, 0x88), Foreground = Ck(each != null ? CkGold : CkWarn), CaretBrush = Ck(CkGold), FontFamily = new FontFamily("Cascadia Mono, Consolas"),
            Text = each is long v ? $"{v:N0} ea" : "", ToolTip = "What the NPC charges for one. Kept on this PC.", HorizontalAlignment = HorizontalAlignment.Left,
        };
        var hint = CkText("set NPC cost", CkWarn, 12.5); hint.IsHitTestVisible = false;
        hint.Visibility = box.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        box.GotKeyboardFocus += (_, _) => { hint.Visibility = Visibility.Collapsed; box.Text = new string(box.Text.Where(char.IsDigit).ToArray()); };
        void Commit()
        {
            long n = long.TryParse(new string(box.Text.Where(char.IsDigit).ToArray()), out var x) ? x : 0;
            if (n == (PriceBook.VendorPrice(item) ?? 0)) { if (n > 0) box.Text = $"{n:N0} ea"; else hint.Visibility = Visibility.Visible; return; }
            PriceBook.SetVendorPrice(item, n);
            Dispatcher.BeginInvoke(() => RefreshCraftCard(output));
        }
        box.LostKeyboardFocus += (_, _) => Commit();
        box.KeyDown += (_, e) => { if (e.Key == Key.Enter) Commit(); };
        var g = new Grid { Margin = new Thickness(0, 2, 0, 0) };
        g.Children.Add(box); g.Children.Add(hint);
        return g;
    }

    /// <summary>For layout review: the breakdown for the first crafted recipe, drawn to a PNG.</summary>
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
