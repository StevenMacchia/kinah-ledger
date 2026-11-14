using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using KinahLedger.Data;
using KinahLedger.Packets;
using KinahLedger.Session;

namespace KinahLedger.Ui;

/// <summary>
/// The Inventory window (Steven, 2026-11-13: "bag value"; 2026-11-14: off the main page, "a separate page for them", and "we
/// cant do 'whats in the bag' because people will have things in there to start... make the runs drops first and make them
/// have to CHOOSE it"): what this session dropped, worth at today's prices, first, every time it opens; the whole bag only
/// when the player picks "The bag". A window of its own (Ui/InventoryWindow.cs), opened from the Farming page's ⋯ menu, as
/// the timers' is from the Events page's.
/// <list type="bullet">
/// <item>This session's drops: what was picked up since the session began (the Farming page's count), worth at today's
///   prices (Session/BagValue.cs), the sellable part after the Market's cut, the kinds with no price yet, the items.</item>
/// <item>The bag (chosen): the character's bag only (the game's Cube; never the storage), from the game's own messages
///   (Packets/BagBook.cs), with what was in it before the session: worth, sellable, unpriced, the change since the tracker
///   first saw it, the items. The game sends it when the character connects to a server, so until then it says when.</item>
/// </list>
/// The gear worn is the character's: it opens with a click on the character's name (Ui/MainWindow.Characters.cs, CharacterGear).
/// </summary>
public partial class MainWindow
{
    InventoryWindow? _invWin;
    DispatcherTimer? _invTick;
    /// <summary>A layout render's sample inventory (KL_DEMO_BAG: "known" or "unknown"), null otherwise.</summary>
    BagBook? _demoBag;
    /// <summary>The inventory the window shows: a render's sample, or the packet reader's while it gets it; null when there is none.</summary>
    BagBook? ShownBag => _bagForShot ?? (_demo ? _demoBag : _watcher.Packets.ReadsBag ? _watcher.Packets.Bag : null);
    BagBook? _bagForShot;

    const double InventoryCardWidth = 470;
    /// <summary>The window shows the whole bag (the player chose it); every opening starts on the session's drops.</summary>
    bool _invShowBag;

    internal MenuEntry InventoryMenuEntry()
    {
        var drops = SessionDrops();
        string sub = drops.Count == 0 ? "This session's drops, and your bag if you choose it"
            : BagValue.Of(drops, null, _items, n => PriceBook.Price(n), PriceBook.SellerKeeps) is { Sellable: > 0 } w ? $"Dropped this session: {Fk(w.Sellable)}" : $"{drops.Count:N0} kinds dropped this session";
        return new MenuEntry("Inventory", OpenInventoryWindow, Sub: sub,
            Tip: "What dropped this session, worth at today's Market prices after the Market's cut (bound items can't be sold and have no value). Pick The bag to see everything in your bag, what you had before too.");
    }

    /// <summary>Opens the inventory's window, or brings it forward; it redraws every two seconds while open.</summary>
    internal void OpenInventoryWindow()
    {
        if (_invWin == null)
        {
            _invWin = new InventoryWindow(this);
            _invWin.Closed += (_, _) => { _invWin = null; _invTick?.Stop(); };
            _invShowBag = false;   // the drops first, every time: the bag is the player's choice
        }
        _invWin.ShowCard(BuildInventoryCard());
        _invTick ??= new DispatcherTimer(TimeSpan.FromSeconds(2), DispatcherPriority.Background, (_, _) =>
        {
            if (_invWin is { IsVisible: true } w && Changed("inventory-window", InventorySig())) w.ShowCard(BuildInventoryCard());
        }, Dispatcher);
        _invTick.Start();
    }

    string InventorySig()
    {
        string Of(IReadOnlyDictionary<long, long>? d) => d == null ? "-" : string.Join(";", d.OrderBy(kv => kv.Key).Select(kv => $"{kv.Key}:{kv.Value}"));
        return $"{_invShowBag}|{Details}|{Of(SessionDrops())}|{Of(ShownBag?.Totals())}|{ShownBag?.Money(CurrencyKind.Kinah)}|{PriceBook.BoardItems}|{Skins.Current}";
    }

    /// <summary>What dropped this session (the Farming page's count: the run, or every zone since the last break), by item.</summary>
    Dictionary<long, long> SessionDrops()
    {
        var d = new Dictionary<long, long>();
        var e = _invDropsForShot ?? (_demo ? _demoEngine : Shown());
        if (e == null) return d;
        foreach (var (id, n) in e.Items) d[id] = d.GetValueOrDefault(id) + n;
        foreach (var (id, n) in e.ItemsBound) d[id] = d.GetValueOrDefault(id) + n;
        return d;
    }
    SessionEngine? _invDropsForShot;
    /// <summary>Options › Game reading › Inventory details: the extra figures and the price hovers (off by default: minimal), or a render's choice.</summary>
    bool Details => _invDetailsForShot ?? _prefs.InventoryDetails;
    bool? _invDetailsForShot;

    /// <summary>The window's card: the heading and the choice (This session, The bag), then the one chosen: its worth and items.</summary>
    internal Border BuildInventoryCard()
    {
        var (card, body) = CkShell(InventoryCardWidth);
        var band = new StackPanel { Margin = new Thickness(20, 16, 56, 4) };
        band.Children.Add(CkText("Inventory", CkInk, 18, FontWeights.Normal, display: true));
        body.Children.Add(band);
        // the choice: the session's drops (always first), or the whole bag, which the player has to pick
        var seg = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(20, 6, 20, 4) };
        void Seg(string text, bool on, bool bagSide, string tip)
        {
            var b = new Button { Content = text, Height = 26, FontSize = 12, Padding = new Thickness(12, 0, 12, 0), Margin = new Thickness(0, 0, 4, 0), ToolTip = tip };
            b.SetResourceReference(StyleProperty, on ? "SegOn" : "SegOff");
            b.Click += (_, _) => { if (_invShowBag == bagSide) return; _invShowBag = bagSide; _invWin?.ShowCard(BuildInventoryCard()); };
            seg.Children.Add(b);
        }
        Seg("This session", !_invShowBag, false, "What dropped since the session began, as the Farming page counts it.");
        Seg("The bag", _invShowBag, true, "Everything in your character's bag now, what you had before the session too (not your storage).");
        body.Children.Add(seg);
        if (_invShowBag) BagSection(body); else SessionDropsSection(body);
        body.Children.Add(new Border { Height = 16 });
        return card;
    }

    /// <summary>The session's drops: their worth, the sellable part, what has no price, and the items, most valuable first.</summary>
    void SessionDropsSection(StackPanel body)
    {
        var drops = SessionDrops();
        if (drops.Count == 0) { body.Children.Add(CkNote("Nothing has dropped this session yet. What you pick up shows here, worth at today's Market prices.")); return; }
        var w = BagValue.Of(drops, null, _items, n => PriceBook.Price(n), PriceBook.SellerKeeps);
        Figures(body, w, null, "What dropped this session, what the tradable items sell for at the lowest Market price the tracker knows, after the Market's cut. Bound items can't be sold: they have no value here.");
        Slots(body, w, null, null);
    }

    /// <summary>The whole bag (chosen): its worth, the sellable part, what has no price, the change since the tracker first saw it, the items.</summary>
    void BagSection(StackPanel body)
    {
        var bag = ShownBag;
        var now = bag?.Totals();
        if (bag == null || now == null)
        {
            body.Children.Add(CkNote(bag == null
                ? "Your bag isn't available right now. Check Options › Game reading."
                : "Known at your next teleport or Abyss trip."));
            return;
        }
        var first = bag.FirstAt != null ? bag.FirstTotals : null;
        var w = BagValue.Of(now, first, _items, n => PriceBook.Price(n), PriceBook.SellerKeeps);
        long? kinah = bag.Money(CurrencyKind.Kinah);
        Figures(body, w, bag, "Everything in your bag now, what you had before the session too (not your storage), what the tradable items sell for at the lowest Market price the tracker knows, after the Market's cut. Bound items can't be sold: they have no value here."
            + (kinah is long k ? $"\nKinah held: {k:N0} (not in the worth)." : "")
            + (bag.KnownAt is DateTime ka ? $"\nUp to date since {ka.ToLocalTime():HH:mm}." : ""));
        Slots(body, w, first, bag.FirstAt);
    }

    void Figures(StackPanel body, BagValue.Worth w, BagBook? bag, string tip)
    {
        var head = new Grid { Margin = new Thickness(20, 8, 20, 0) };
        head.ColumnDefinitions.Add(new ColumnDefinition()); head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var worth = CkText(w.Sellable > 0 ? Fk(w.Sellable) : "No price yet", w.Sellable > 0 ? CkGold : CkMuted, 22, FontWeights.SemiBold);
        head.Children.Add(worth);
        var kinds = CkText($"{w.Kinds:N0} kind{(w.Kinds == 1 ? "" : "s")}" + (bag?.BagStacks is int st ? $" · {st:N0} slots" : ""), CkMuted, 12.5);
        kinds.VerticalAlignment = VerticalAlignment.Center; Grid.SetColumn(kinds, 1); head.Children.Add(kinds);
        body.Children.Add(head);
        worth.ToolTip = Details ? tip : null;
        if (!Details) return;   // minimal by default: the worth and the items (Options › Game reading › Inventory details)
        var parts = new List<string>();
        if (w.Sellable > 0) parts.Add(PriceBook.Cut);
        if (w.BoundKinds > 0) parts.Add($"{w.BoundCount:N0} bound, no value (can't be sold)");
        if (w.Unpriced > 0) parts.Add($"{w.Unpriced:N0} without a price yet");
        if (w.Change is long c && c != 0 && bag?.FirstAt is DateTime fa) parts.Add($"{(c > 0 ? "+" : "−")}{Fk(Math.Abs(c * PriceBook.SellerKeeps))} since {fa.ToLocalTime():HH:mm}");
        if (parts.Count > 0) body.Children.Add(CkNote(string.Join(" · ", parts)));
    }

    void Slots(StackPanel body, BagValue.Worth w, Dictionary<long, long>? first, DateTime? since)
    {
        var slots = new WrapPanel { Margin = new Thickness(20, 8, 14, 0) };
        foreach (var l in w.Lines.Take(16))
        {
            var info = _items.Get(l.Id);
            var slot = QSlot(QItemIcon(l.Name, l.Id, 42, round: false, grade: info?.Grade ?? ""), CountBadge(l.Count));
            slot.ToolTip = !Details ? $"{l.Name} ×{l.Count:N0}" : $"{l.Name} ×{l.Count:N0}" + (l.Bound ? ": bound, can't be sold, no value" : l.Price is long p ? $": {Fk(l.Value)} ({Fk(p)} each, before the Market's cut)" : ": no price yet")
                + (first != null && since is DateTime t && l.Change != 0 ? $"\n{(l.Change > 0 ? "+" : "−")}{Math.Abs(l.Change):N0} since {t.ToLocalTime():HH:mm}" : "");
            slots.Children.Add(slot);
        }
        if (slots.Children.Count > 0) body.Children.Add(slots);
    }

    /// <summary>
    /// The gear the counted character wears, for the character card (a click on the name, Ui/MainWindow.Characters.cs): each
    /// piece with its +level as the game's window says it; a piece the item list doesn't know yet gets a plain slot and says
    /// so. Null when the game hasn't sent the inventory (or packet reading is off).
    /// </summary>
    internal FrameworkElement? CharacterGear(string who = "")
    {
        var worn = ShownBag?.WornGear();
        if (worn is not { Count: > 0 }) return null;
        var panel = new StackPanel { Margin = new Thickness(18, 0, 18, 6) };
        panel.Children.Add(CkText(who.Length > 0 ? $"{who}'s gear" : "Gear worn", CkMuted, 12, FontWeights.SemiBold));
        var gear = new WrapPanel { Margin = new Thickness(0, 6, 0, 0) };
        foreach (var g in worn)
        {
            var info = _items.Get(g.ItemId);
            string level = BagValue.LevelText(g.Level);
            FrameworkElement icon = BagValue.HasIcon(_items, g.ItemId) && info != null ? QItemIcon(info.Name, g.ItemId, 42, round: false, grade: info.Grade) : UnknownGearIcon();
            var slot = QSlot(icon, level);
            if (level.Length == 0 && slot is Grid sg && sg.Children.Count > 1) sg.Children[1].Visibility = Visibility.Collapsed;   // no level said: no empty badge
            slot.ToolTip = info != null ? (level.Length > 0 ? $"{info.Name} {level}" : info.Name)
                : $"A piece the item list doesn't have yet (item {g.ItemId}){(level.Length > 0 ? ", " + level : "")}.";
            gear.Children.Add(slot);
        }
        panel.Children.Add(gear);
        return panel;
    }

    /// <summary>A plain slot for a worn piece the item list lacks (no name, so no picture): never a made-up icon.</summary>
    static FrameworkElement UnknownGearIcon()
    {
        var b = new Border { Width = 42, Height = 42, CornerRadius = new CornerRadius(8), BorderThickness = new Thickness(1) };
        b.SetResourceReference(Border.BackgroundProperty, "Card2");
        b.SetResourceReference(Border.BorderBrushProperty, "Line");
        b.Child = new TextBlock { Text = "?", FontSize = 16, FontWeight = FontWeights.SemiBold, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Foreground = new SolidColorBrush(CkFaint) };
        return b;
    }

    /// <summary>
    /// ui-shot --inventory: the window over the render sample: the session's drops (as it opens), the bag chosen, and the bag
    /// before the game sent it, written as inventory-&lt;state&gt;[-&lt;skin&gt;].png. The PC's own inventory is never read or saved.
    /// </summary>
    internal void ShotInventory(string dir, string? skin)
    {
        string sfx = skin != null ? "-" + skin : "";
        // sample prices for the render only (this process's price book; never the PC's price files)
        PriceBook.SetBoard(new() { ["Orichalcum Ore"] = 1_950, ["Pure Orichalcum Ore"] = 42_000, ["Odyle"] = 2_300, ["Amplify Stone (Unique)"] = 135_000, ["Wrathful Ego"] = 390_000,
            ["Spiritstone"] = 7_499, ["Life Potion"] = 1_200, ["Life Serum"] = 900 });
        _invDropsForShot = new SessionEngine(_items);
        void Drop(string n, int c) { if (_items.IdOf(n) is long id and > 0) _invDropsForShot.Apply(new ItemAcquired(DateTime.UtcNow, id, c, false)); }
        Drop("Orichalcum Ore", 38); Drop("Pure Orichalcum Ore", 2); Drop("Odyle", 12); Drop("Spiritstone", 14); Drop("Wrathful Ego", 1);
        foreach (var (state, details) in new[] { ("drops", false), ("bag", false), ("bag-unknown", false), ("drops-details", true), ("bag-details", true) })
        {
            _invDetailsForShot = details;
            DemoInventory(state == "bag-unknown" ? "unknown" : "known");
            _bagForShot = _demoBag;
            _invShowBag = state.StartsWith("bag");
            var card = BuildInventoryCard();
            var win = new InventoryWindow(this);
            win.ShowCard(card, offscreen: true);
            win.UpdateLayout();
            Dispatcher.Invoke(() => { }, DispatcherPriority.Background);
            if (win.Content is FrameworkElement root)
            {
                var bmp = new System.Windows.Media.Imaging.RenderTargetBitmap((int)Math.Ceiling(root.ActualWidth), (int)Math.Ceiling(root.ActualHeight), 96, 96, PixelFormats.Pbgra32);
                bmp.Render(root);
                var enc = new System.Windows.Media.Imaging.PngBitmapEncoder(); enc.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bmp));
                using var f = File.Create(Path.Combine(dir, $"inventory-{state}{sfx}.png")); enc.Save(f);
            }
            win.Close();
        }
        _demoBag = null; _bagForShot = null; _invDropsForShot = null; _invShowBag = false; _invDetailsForShot = null;
    }

    /// <summary>The render sample's inventory: a gatherer's bag (and some storage, left out), the gear worn with one piece the item list lacks, then two gathers since.</summary>
    void DemoInventory(string how)
    {
        _demoBag = new BagBook();
        if (how == "unknown") return;
        var t = DateTime.UtcNow.AddMinutes(-42);
        var rows = new List<BagRecord> { new(t, BagRecord.Begin, 0, 0, 0, 0, 0, 0, null) };
        long stack = 100;
        var stackOf = new Dictionary<string, long>();
        void S(string name, long n, int box = BagBook.InBag) { if (_items.IdOf(name) is long id and > 0) { stackOf[name + box] = stack; rows.Add(new(t, BagRecord.Stack, 0, id, stack++, n, box, 0, null)); } }
        rows.Add(new(t, BagRecord.Stack, 0, 0, stack++, 6_324_585, BagBook.InBag, 0, CurrencyKind.Kinah));
        S("Orichalcum Ore", 46); S("Pure Orichalcum Ore", 51); S("Odyle", 19); S("Amplify Stone (Unique)", 112); S("Wrathful Ego", 5);
        S("Life Potion (Bound)", 364); S("Life Serum (Bound)", 282); S("Battle Enhance Scroll (Bound)", 218); S("Benediction Scroll (Bound)", 132); S("Spiritstone", 4);
        S("Orichalcum Ore", 742, 2); S("Fine Orichalcum Ore", 653, 2);   // storage: never in the bag's worth
        void G(string name, int slot, int level) { if (_items.IdOf(name) is long id and > 0) rows.Add(new(t, BagRecord.Stack, 0, id, stack++, 1, BagBook.Worn, slot, null, level)); }
        G("Enraged Kromede Sword", 1, 18); G("Nuakum Guard", 2, 12); G("Nuakum Helm", 3, 10); G("Nuakum Pauldrons", 4, 10); G("Nuakum Breastplate", 5, 10); G("Nuakum Greaves", 6, 10);
        G("Nuakum Gloves", 7, 10); G("Nuakum Boots", 8, 10); G("Enraged Kromede Necklace", 10, 10); G("Enraged Kromede Earrings", 11, 10); G("Enraged Kromede Earrings", 12, 10);
        G("Enraged Kromede Ring", 13, 11); G("Enraged Kromede Ring", 14, 10); G("Ascension Bracelet", 15, 10); G("Pilgrim Bracelet", 16, 10); G("Nuakum Cloak", 19, 10);
        rows.Add(new(t, BagRecord.Stack, 0, 310900001, stack++, 1, BagBook.Worn, 23, null, 1));   // a piece the item list lacks
        rows.Add(new(t, BagRecord.End, rows.Count - 1, 0, 0, 0, 0, 0, null));
        foreach (var r in rows) _demoBag.Take(r);
        if (stackOf.TryGetValue("Orichalcum Ore" + BagBook.InBag, out var so)) _demoBag.Take(new ItemChange(t.AddMinutes(30), _items.IdOf("Orichalcum Ore"), ItemOp.Changed, so, 4, 50, false, 1, 1, false));
        if (stackOf.TryGetValue("Odyle" + BagBook.InBag, out var sd)) _demoBag.Take(new ItemChange(t.AddMinutes(31), _items.IdOf("Odyle"), ItemOp.Changed, sd, 3, 22, false, 2, 1, false));
    }
}
