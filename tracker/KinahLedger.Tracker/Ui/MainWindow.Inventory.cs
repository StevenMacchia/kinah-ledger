using System.Windows;
using KinahLedger.Data;
using KinahLedger.Packets;
using KinahLedger.Session;

namespace KinahLedger.Ui;

/// <summary>
/// The Inventory card (Steven, 2026-11-13: "bag value"): what the character holds, from the game's own messages
/// (Packets/BagBook.cs), worth at today's prices (Session/BagValue.cs): the total, the sellable part after the Market's cut,
/// how many kinds have no price yet, the change since the tracker first saw the inventory, and the most valuable items as
/// slots. The game sends the whole inventory when the character connects to a server (log in, teleport to another server,
/// enter or leave the Abyss), so until then the card says when it will know. Shown only while the reader gets it.
/// </summary>
public partial class MainWindow
{
    /// <summary>A layout render's sample inventory (KL_DEMO_BAG: "known" or "unknown"), null otherwise.</summary>
    BagBook? _demoBag;

    void FillInventory()
    {
        var bag = _demo ? _demoBag : _watcher.Packets.ReadsBag ? _watcher.Packets.Bag : null;
        bool show = bag != null && _part == "Farming" && !_welcome;
        InvCard.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        if (!show) return;
        var now = bag!.Totals();
        if (now == null)
        {
            if (!Changed("inventory", "unknown|" + Skins.Current)) return;
            InvNote.Text = ""; InvTotal.Text = ""; InvTotal.ToolTip = null; InvHost.Children.Clear(); InvHost.Visibility = Visibility.Collapsed;
            InvSub.Text = "Known at your next teleport or Abyss trip, when the game sends your whole inventory.";
            return;
        }
        var first = bag.FirstAt != null ? bag.FirstTotals : null;
        var w = BagValue.Of(now, first, _items, n => PriceBook.Price(n), PriceBook.SellerKeeps);
        long? kinah = bag.Money(CurrencyKind.Kinah);
        var top = w.Lines.Take(8).ToList();
        var sig = $"{w.Total}|{w.Sellable}|{w.Unpriced}|{w.Kinds}|{w.Change}|{kinah}|{string.Join(";", top.Select(l => $"{l.Id}:{l.Count}:{l.Value}"))}|{Skins.Current}";
        if (!Changed("inventory", sig)) return;
        InvTotal.Text = w.Total > 0 ? Fk(w.Total) : "";
        InvNote.Text = $"{w.Kinds:N0} kind{(w.Kinds == 1 ? "" : "s")}";
        var parts = new List<string>();
        if (w.Sellable > 0) parts.Add($"sellable {Fk(w.Sellable)} {PriceBook.Cut}");
        if (w.Unpriced > 0) parts.Add($"{w.Unpriced:N0} without a price yet");
        if (w.Change is long c && c != 0 && bag.FirstAt is DateTime fa) parts.Add($"{(c > 0 ? "+" : "−")}{Fk(Math.Abs(c))} since {fa.ToLocalTime():HH:mm}");
        InvSub.Text = string.Join(" · ", parts);
        InvTotal.ToolTip = "Everything the game lists for this character, the gear worn left out, at the lowest Market price the tracker knows; a bound item at its tradable twin's price."
            + (w.Sellable > 0 ? $"\nSellable: {Fk(w.Sellable)}, the tradable items {PriceBook.Cut}." : "")
            + (kinah is long k ? $"\nKinah held: {k:N0} (not in the worth)." : "")
            + (bag.KnownAt is DateTime ka ? $"\nAs the game sent it at {ka.ToLocalTime():HH:mm}, kept up with every change since." : "");
        InvHost.Children.Clear();
        InvHost.Visibility = top.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        foreach (var l in top)
        {
            var info = _items.Get(l.Id);
            var slot = QSlot(QItemIcon(l.Name, l.Id, 42, round: false, grade: info?.Grade ?? ""), CountBadge(l.Count));
            slot.ToolTip = $"{l.Name} ×{l.Count:N0}" + (l.Price is long p ? $": {Fk(l.Value)} ({Fk(p)} each{(l.Bound ? ", bound: its tradable twin's price" : "")})" : ": no price yet")
                + (first != null && l.Change != 0 ? $"\n{(l.Change > 0 ? "+" : "−")}{Math.Abs(l.Change):N0} since {bag.FirstAt!.Value.ToLocalTime():HH:mm}" : "");
            InvHost.Children.Add(slot);
        }
    }

    /// <summary>The render sample's inventory: a snapshot of a gatherer's bag, then two gathers and a sale since.</summary>
    void DemoInventory(string how)
    {
        _demoBag = new BagBook();
        if (how == "unknown") return;
        var t = DateTime.UtcNow.AddMinutes(-42);
        var rows = new List<BagRecord> { new(t, BagRecord.Begin, 11, 0, 0, 0, 0, 0, null) };
        long stack = 100;
        var stackOf = new Dictionary<string, long>();
        void S(string name, long n, int box = 2) { if (_items.IdOf(name) is long id and > 0) { stackOf[name] = stack; rows.Add(new(t, BagRecord.Stack, 0, id, stack++, n, box, 0, null)); } }
        rows.Add(new(t, BagRecord.Stack, 0, 0, stack++, 4_922_940, 1, 0, CurrencyKind.Kinah));
        S("Orichalcum Ore", 773); S("Fine Orichalcum Ore", 658); S("Odyle", 52); S("Radiant Odyle", 11); S("Amplify Stone (Unique)", 112, 1);
        S("Superior Manastone", 5); S("Lesser Manastone", 5); S("Spiritstone", 256); S("Life Potion (Bound)", 364, 1); S("Strong Leather", 313);
        rows.Add(new(t, BagRecord.End, rows.Count - 1, 0, 0, 0, 0, 0, null));
        foreach (var r in rows) _demoBag.Take(r);
        if (stackOf.TryGetValue("Orichalcum Ore", out var so)) _demoBag.Take(new ItemChange(t.AddMinutes(30), _items.IdOf("Orichalcum Ore"), ItemOp.Changed, so, 9, 782, false, 1, 1, false));
        if (stackOf.TryGetValue("Odyle", out var sd)) _demoBag.Take(new ItemChange(t.AddMinutes(31), _items.IdOf("Odyle"), ItemOp.Changed, sd, 6, 58, false, 2, 1, false));
    }
}
