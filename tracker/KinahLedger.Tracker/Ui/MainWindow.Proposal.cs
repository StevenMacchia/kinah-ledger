using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Shapes;
using KinahLedger.Data;
using KinahLedger.Session;

namespace KinahLedger.Ui;

/// <summary>
/// The redesign proposal's prototype pages (2026-11-14, docs/ui/redesign-proposal/): the real window, filled by the demo,
/// rearranged into the proposed layout. It has one nav row with every page, an overlays switch group in the header, a lean
/// Farm page, and new Character, Timers and Overlays pages built from the window's own pieces and theme. Only
/// `ui-shot --proposal` calls it. Nothing here is wired to a live session.
/// </summary>
public partial class MainWindow
{
    static readonly (string Name, string Art)[] ProposalPages =
    {
        ("Farm", "kinah"), ("Runs", "dungeon"), ("Craft", "morph"), ("Character", "profile"), ("Timers", "boss"), ("Parser", "greatsword"), ("Quests", "checklist"),
    };

    const double PW = 662;   // the ledger column's width

    /// <summary>Rearranges the window into the proposal's <paramref name="page"/>; <paramref name="rail"/> puts the pages in a left rail instead of a row.</summary>
    internal void ProposalPage(string page, bool rail = false)
    {
        ProposalShell(page, rail);
        switch (page)
        {
            case "Farm": ProposalFarm(); break;
            case "Character": ProposalHost(ProposalCharacter()); break;
            case "Timers": ProposalHost(ProposalTimers()); break;
            case "Overlays": ProposalHost(ProposalOverlays()); break;
        }
    }

    // ---------------------------------------------------------------- the shell: header, overlays group, nav row

    void ProposalShell(string page, bool rail)
    {
        PartTabs.Visibility = Visibility.Collapsed;
        // the header's middle: the three overlays, each one click from any page
        if (PartTabs.Parent is Grid head)
        {
            var group = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0) };
            var lbl = QT("Overlays", 11.5, FontWeights.SemiBold, "Faint"); lbl.Margin = new Thickness(0, 0, 8, 0); lbl.VerticalAlignment = VerticalAlignment.Center;
            group.Children.Add(lbl);
            group.Children.Add(OverlayPill("Kinah", true));
            group.Children.Add(OverlayPill("Timers", true));
            group.Children.Add(OverlayPill("Parser", false));
            Grid.SetColumn(group, 1);
            head.Children.Add(group);
        }
        if (rail) { ProposalRail(page); return; }
        var nav = new Border { CornerRadius = new CornerRadius(12), Padding = new Thickness(4), Margin = new Thickness(0, 10, 0, 0), Width = PW, HorizontalAlignment = HorizontalAlignment.Left };
        nav.SetResourceReference(Border.BackgroundProperty, "Card");
        nav.SetResourceReference(Border.BorderBrushProperty, "Line"); nav.BorderThickness = new Thickness(1);
        var row = new Grid();
        int i = 0;
        foreach (var (name, art) in ProposalPages)
        {
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(name switch { "Character" => 1.3, "Timers" => 1.4, "Quests" => 1.05, _ => 1 }, GridUnitType.Star) });
            var tab = NavTab(name, art, name == page); Grid.SetColumn(tab, i++); row.Children.Add(tab);
        }
        nav.Child = row;
        HeadStack.Children.Insert(1, nav);
    }

    FrameworkElement OverlayPill(string text, bool on)
    {
        var sp = new StackPanel { Orientation = Orientation.Horizontal };
        var dot = new Ellipse { Width = 6, Height = 6, Margin = new Thickness(0, 0, 6, 0), VerticalAlignment = VerticalAlignment.Center };
        dot.SetResourceReference(Shape.FillProperty, on ? "Good" : "Faint");
        if (!on) dot.Opacity = 0.6;
        sp.Children.Add(dot);
        sp.Children.Add(QT(text, 11.5, FontWeights.SemiBold, on ? "Ink" : "Faint"));
        var b = new Border { Child = sp, CornerRadius = new CornerRadius(10), Padding = new Thickness(8, 3, 9, 3), Margin = new Thickness(4, 0, 0, 0), BorderThickness = new Thickness(1), VerticalAlignment = VerticalAlignment.Center };
        b.SetResourceReference(Border.BackgroundProperty, on ? "Card2" : "Bg");
        b.SetResourceReference(Border.BorderBrushProperty, on ? "LineStrong" : "Line");
        b.ToolTip = $"The {text} overlay over the game: {(on ? "on" : "off")}. Click to switch; right-click for its settings.";
        return b;
    }

    FrameworkElement NavTab(string name, string art, bool on)
    {
        var sp = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };
        var pic = GameArt.Element(art, 16); pic.Margin = new Thickness(0, 0, 5, 0); pic.VerticalAlignment = VerticalAlignment.Center;
        if (!on) pic.Opacity = 0.7;
        sp.Children.Add(pic);
        sp.Children.Add(QT(name, 12.5, FontWeights.SemiBold, on ? "Ink" : "Muted"));
        // a page with something about to happen says so on its tab: the next watched boss, a fight under way
        if (name == "Timers") sp.Children.Add(TabBadge("4:52", "Gold"));
        var b = new Border { Child = sp, CornerRadius = new CornerRadius(9), Height = 32, Padding = new Thickness(4, 0, 4, 0) };
        b.SetResourceReference(Border.BackgroundProperty, on ? "ItemSel" : "Card");
        if (on)
        {
            b.Child = null; var g = new Grid(); g.Children.Add(sp);
            var bar = new Border { Height = 2, Width = 22, CornerRadius = new CornerRadius(1), VerticalAlignment = VerticalAlignment.Bottom, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 0, 0, 1) };
            bar.SetResourceReference(Border.BackgroundProperty, "Gold");
            g.Children.Add(bar);
            b.Child = g;
        }
        return b;
    }

    static Border TabBadge(string text, string tone)
    {
        var t = QT(text, 10.5, FontWeights.Bold, tone);
        System.Windows.Documents.Typography.SetNumeralAlignment(t, FontNumeralAlignment.Tabular);
        var b = new Border { Child = t, CornerRadius = new CornerRadius(6), Padding = new Thickness(5, 0, 5, 1), Margin = new Thickness(6, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        b.SetResourceReference(Border.BackgroundProperty, "GoldIcTint");
        return b;
    }

    /// <summary>The rail variant: the pages down the left edge, icon over label; the page itself keeps its width.</summary>
    void ProposalRail(string page)
    {
        var outer = FitHost;
        outer.ColumnDefinitions.Clear();
        outer.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(70) });
        outer.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        foreach (UIElement c in outer.Children) Grid.SetColumn(c, 1);
        var rail = new StackPanel { Margin = new Thickness(0, 56, 12, 0) };
        foreach (var (name, art) in ProposalPages)
        {
            bool on = name == page;
            var sp = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center };
            var pic = GameArt.Element(art, 24); pic.HorizontalAlignment = HorizontalAlignment.Center; if (!on) pic.Opacity = 0.65;
            sp.Children.Add(pic);
            var t = QT(name, 10.5, FontWeights.SemiBold, on ? "Ink" : "Muted"); t.HorizontalAlignment = HorizontalAlignment.Center; t.Margin = new Thickness(0, 3, 0, 0);
            sp.Children.Add(t);
            var g = new Grid(); g.Children.Add(sp);
            if (name == "Timers") { var badge = TabBadge("4:52", "Gold"); badge.Margin = new Thickness(0, 3, 0, 0); badge.HorizontalAlignment = HorizontalAlignment.Center; sp.Children.Add(badge); }
            var b = new Border { Child = g, CornerRadius = new CornerRadius(10), Padding = new Thickness(0, 8, 0, 7), Margin = new Thickness(0, 0, 0, 4) };
            b.SetResourceReference(Border.BackgroundProperty, on ? "ItemSel" : "Bg");
            if (on) { b.BorderThickness = new Thickness(2, 0, 0, 0); b.SetResourceReference(Border.BorderBrushProperty, "Gold"); }
            rail.Children.Add(b);
        }
        outer.Children.Add(rail);
    }

    // ---------------------------------------------------------------- Farm: the live run and only what a farmer glances at

    void ProposalFarm()
    {
        InvCard.Visibility = Visibility.Collapsed;
        GoalsCard.Visibility = Visibility.Collapsed;
        WhereSection.Visibility = Visibility.Collapsed;
        foreach (var t in Descendants<TextBlock>(BagStripHead)) if (t.Text == "The bag") t.Text = "Loot this session";
        var row = new Grid { Margin = new Thickness(0, 10, 0, 0) };
        row.ColumnDefinitions.Add(new ColumnDefinition());
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(10) });
        row.ColumnDefinitions.Add(new ColumnDefinition());
        var a = GlanceChip("Q", "Next up: 1M this session", "367.7K to go · about 24 min · 6 pts", 0.63);
        var b = GlanceChip("Flag", "Northern Tolbas Forest pays more", "1.82M an hour · 43% more than here", null);
        Grid.SetColumn(b, 2);
        row.Children.Add(a); row.Children.Add(b);
        int at = LeftCol.Children.IndexOf(FeedCard);
        LeftCol.Children.Insert(at + 1, row);
    }

    Border GlanceChip(string glyph, string title, string sub, double? bar)
    {
        var g = new Grid();
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(30) });
        g.ColumnDefinitions.Add(new ColumnDefinition());
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var ic = QGlyph(glyph, 18, "Muted"); ic.VerticalAlignment = VerticalAlignment.Center;
        g.Children.Add(ic);
        var txt = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        txt.Children.Add(QT(title, 12.5, FontWeights.SemiBold, "Ink", trim: true));
        txt.Children.Add(QT(sub, 11.5, FontWeights.Medium, "Faint", trim: true));
        if (bar is double share)
        {
            var track = new Grid { Height = 3, Margin = new Thickness(0, 6, 0, 0) };
            var bg = new Border { CornerRadius = new CornerRadius(2) }; bg.SetResourceReference(Border.BackgroundProperty, "Line");
            var fill = new Border { CornerRadius = new CornerRadius(2), HorizontalAlignment = HorizontalAlignment.Left, Width = 250 * share }; fill.SetResourceReference(Border.BackgroundProperty, "Gold");
            track.Children.Add(bg); track.Children.Add(fill);
            txt.Children.Add(track);
        }
        Grid.SetColumn(txt, 1); g.Children.Add(txt);
        var chev = new System.Windows.Shapes.Path { Data = Geometry.Parse("M0,0 L4,4 L0,8"), StrokeThickness = 1.5, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 2, 0) };
        chev.SetResourceReference(Shape.StrokeProperty, "Faint");
        Grid.SetColumn(chev, 2); g.Children.Add(chev);
        return ProposalCard(g, new Thickness(12, 10, 12, 10));
    }

    // ---------------------------------------------------------------- a page of its own in the ledger column

    void ProposalHost(FrameworkElement body)
    {
        foreach (UIElement c in LeftCol.Children) c.Visibility = Visibility.Collapsed;
        TodayStrip.Visibility = Visibility.Collapsed;
        LeftCol.Children.Add(body);
    }

    Border ProposalCard(UIElement child, Thickness? pad = null, Thickness? margin = null)
    {
        var b = new Border { Child = child, CornerRadius = new CornerRadius(12), BorderThickness = new Thickness(1), Padding = pad ?? new Thickness(14, 12, 14, 12), Margin = margin ?? new Thickness(0) };
        b.SetResourceReference(Border.BackgroundProperty, "Card");
        b.SetResourceReference(Border.BorderBrushProperty, "Line");
        return b;
    }

    /// <summary>A card's head line: its art, its name, a muted note, and on the right a figure or a control.</summary>
    Grid CardHead(string glyph, string title, string note, UIElement? right = null)
    {
        var g = new Grid { Margin = new Thickness(0, 0, 0, 10) };
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        g.ColumnDefinitions.Add(new ColumnDefinition());
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var sp = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        var ic = QGlyph(glyph, 14, "Muted"); ic.Margin = new Thickness(0, 0, 8, 0);
        sp.Children.Add(ic);
        sp.Children.Add(QT(title, 12.5, FontWeights.SemiBold, "Ink"));
        if (note.Length > 0) { var n = QT(note, 12, FontWeights.Medium, "Faint"); n.Margin = new Thickness(8, 0, 0, 0); sp.Children.Add(n); }
        g.Children.Add(sp);
        if (right != null) { Grid.SetColumn(right, 2); g.Children.Add(right); }
        return g;
    }

    /// <summary>A page's title band: the name, one line under it, and the page's own controls on the right.</summary>
    Grid PageHead(string title, string sub, params UIElement[] right)
    {
        var g = new Grid { Margin = new Thickness(2, 4, 0, 10) };
        g.ColumnDefinitions.Add(new ColumnDefinition());
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var left = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        var t = QT(title, 20, FontWeights.SemiBold, "Ink"); t.FontFamily = (FontFamily)FindResource("DisplayFont");
        left.Children.Add(t);
        left.Children.Add(QT(sub, 12, FontWeights.Medium, "Faint"));
        g.Children.Add(left);
        var sp = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        foreach (var r in right) sp.Children.Add(r);
        Grid.SetColumn(sp, 1); g.Children.Add(sp);
        return g;
    }

    static FrameworkElement Switch(string label, bool on)
    {
        var sp = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(12, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        var l = QT(label, 12, FontWeights.SemiBold, on ? "Ink" : "Muted"); l.Margin = new Thickness(0, 0, 8, 0);
        sp.Children.Add(l);
        var track = new Border { Width = 32, Height = 18, CornerRadius = new CornerRadius(9), Padding = new Thickness(2) };
        track.SetResourceReference(Border.BackgroundProperty, on ? "Gold" : "LineStrong");
        var knob = new Ellipse { Width = 14, Height = 14, HorizontalAlignment = on ? HorizontalAlignment.Right : HorizontalAlignment.Left };
        knob.SetResourceReference(Shape.FillProperty, on ? "GoldInk" : "Muted");
        track.Child = knob;
        sp.Children.Add(track);
        return sp;
    }

    static Border SmallBtn(string text, bool primary = false)
    {
        var t = QT(text, 12, FontWeights.SemiBold, primary ? "GoldInk" : "Ink");
        var b = new Border { Child = t, CornerRadius = new CornerRadius(8), Padding = new Thickness(10, 5, 10, 5), Margin = new Thickness(10, 0, 0, 0), BorderThickness = new Thickness(1), VerticalAlignment = VerticalAlignment.Center };
        b.SetResourceReference(Border.BackgroundProperty, primary ? "Gold" : "Card2");
        b.SetResourceReference(Border.BorderBrushProperty, primary ? "Gold" : "LineStrong");
        return b;
    }

    /// <summary>A segmented choice: the options side by side, the chosen one lit.</summary>
    static Border Seg(string chosen, params string[] options)
    {
        var sp = new StackPanel { Orientation = Orientation.Horizontal };
        foreach (var o in options)
        {
            bool on = o == chosen;
            var b = new Border { Child = QT(o, 11.5, FontWeights.SemiBold, on ? "Ink" : "Muted"), CornerRadius = new CornerRadius(6), Padding = new Thickness(9, 3, 9, 4) };
            b.SetResourceReference(Border.BackgroundProperty, on ? "ItemSel" : "Card");
            sp.Children.Add(b);
        }
        var g = new Border { Child = sp, CornerRadius = new CornerRadius(8), Padding = new Thickness(2), BorderThickness = new Thickness(1), VerticalAlignment = VerticalAlignment.Center };
        g.SetResourceReference(Border.BackgroundProperty, "Card");
        g.SetResourceReference(Border.BorderBrushProperty, "Line");
        return g;
    }

    // ---------------------------------------------------------------- Character: gear worn, then the bag

    FrameworkElement ProposalCharacter()
    {
        var page = new StackPanel { Width = PW };
        var bag = _demoBag;
        var now = bag?.Totals();
        var w = now != null ? BagValue.Of(now, bag!.FirstAt != null ? bag.FirstTotals : null, _items, n => PriceBook.Price(n), PriceBook.SellerKeeps) : null;
        long kinah = bag?.Money(Packets.CurrencyKind.Kinah) ?? 0;
        page.Children.Add(PageHead("Lysandra", "Gladiator · Siel (Elyos) · as the game sent it at 05:57, kept up since",
            Seg("Bag", "Bag", "Storage", "Both")));

        // gear: every slot in the game's order, a dashed slot where nothing is worn or the reader hasn't named it
        var worn = bag?.WornGear() ?? new();
        var bySlot = worn.GroupBy(g => g.Slot).ToDictionary(g => g.Key, g => g.First());
        var slots = new (string Label, int Slot)[]
        {
            ("Weapon", 1), ("Guard", 2), ("Helm", 3), ("Pauldrons", 4), ("Breastplate", 5), ("Greaves", 6), ("Gloves", 7),
            ("Boots", 8), ("Cloak", 19), ("Necklace", 10), ("Earring", 11), ("Earring", 12), ("Ring", 13), ("Ring", 14),
        };
        var levels = worn.Where(g => g.Level is >= 1 and <= MaxShownLevel).Select(g => g.Level).ToList();
        var avg = levels.Count > 0 ? $"+{levels.Average():0.0} average" : "";
        var gearBody = new StackPanel();
        var sum = new StackPanel { Orientation = Orientation.Horizontal };
        sum.Children.Add(QT(avg, 12.5, FontWeights.SemiBold, "Gold"));
        var cnt = QT($" · {worn.Count} of {slots.Length} slots read", 12, FontWeights.Medium, "Faint"); sum.Children.Add(cnt);
        gearBody.Children.Add(CardHead("Shield", "Gear worn", "", sum));
        var grid = new UniformGrid { Columns = 7 };
        foreach (var (label, slot) in slots)
        {
            var cell = new StackPanel { Margin = new Thickness(0, 0, 0, 8) };
            FrameworkElement tile;
            string name = "";
            if (bySlot.TryGetValue(slot, out var g))
            {
                var info = _items.Get(g.ItemId);
                name = info?.Name ?? _items.NameOf(g.ItemId);
                tile = QSlot(QItemIcon(name, g.ItemId, 50, round: false, grade: info?.Grade ?? ""), g.Level is >= 1 and <= MaxShownLevel ? $"+{g.Level}" : "");
                ((FrameworkElement)tile).Width = 56; ((FrameworkElement)tile).Height = 56;
            }
            else
            {
                var empty = new Border { Width = 50, Height = 50, CornerRadius = new CornerRadius(15), BorderThickness = new Thickness(1.5), Margin = new Thickness(0, 0, 6, 6) };
                empty.BorderBrush = new DrawingBrush(new GeometryDrawing(null, new Pen((Brush)FindResource("LineStrong"), 1.5) { DashStyle = new DashStyle(new double[] { 3, 3 }, 0) }, new RectangleGeometry(new Rect(0, 0, 50, 50), 15, 15)));
                empty.SetResourceReference(Border.BackgroundProperty, "CardQuiet");
                tile = empty;
            }
            tile.HorizontalAlignment = HorizontalAlignment.Center;
            cell.Children.Add(tile);
            var l = QT(label, 11, FontWeights.SemiBold, name.Length > 0 ? "Muted" : "Faint"); l.HorizontalAlignment = HorizontalAlignment.Center;
            cell.Children.Add(l);
            if (name.Length > 0) tile.ToolTip = name;
            grid.Children.Add(cell);
        }
        gearBody.Children.Add(grid);
        page.Children.Add(ProposalCard(gearBody, margin: new Thickness(0, 0, 0, 10)));

        // the bag: what it is worth, then every kind by worth
        var bagBody = new StackPanel();
        var total = QT(w != null ? Fk(w.Total) : "", 22, FontWeights.SemiBold, "Gold"); total.FontFamily = (FontFamily)FindResource("DisplayFont");
        bagBody.Children.Add(CardHead("Bag", "Bag", w != null ? $"{w.Kinds} kinds" : "", total));
        var figs = new UniformGrid { Rows = 1, Columns = 4, Margin = new Thickness(0, -2, 0, 12) };
        figs.Children.Add(Fig(w != null ? Fk(w.Sellable) : "", "sellable after the 12% fee"));
        figs.Children.Add(Fig(Fk(kinah), "Kinah held"));
        figs.Children.Add(Fig(w?.Change is long c && c != 0 ? (c > 0 ? "+" : "−") + Fk(Math.Abs(c)) : "0", "since 05:57", w?.Change > 0 ? "Good" : "Ink"));
        figs.Children.Add(Fig(w != null ? w.Unpriced.ToString() : "", "without a price yet"));
        bagBody.Children.Add(figs);
        var tiles = new WrapPanel();
        if (w != null)
            foreach (var l in w.Lines.Take(24))
            {
                var info = _items.Get(l.Id);
                tiles.Children.Add(QSlot(QItemIcon(l.Name, l.Id, 42, round: false, grade: info?.Grade ?? ""), CountBadge(l.Count)));
            }
        bagBody.Children.Add(tiles);
        // the five worth the most, with what each is worth
        if (w != null)
            foreach (var l in w.Lines.Take(5))
            {
                var r = new Grid { Height = 30 };
                r.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(30) });
                r.ColumnDefinitions.Add(new ColumnDefinition());
                r.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(110) });
                r.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(90) });
                var ic = QItemIcon(l.Name, l.Id, 22, round: false, grade: _items.Get(l.Id)?.Grade ?? ""); ic.HorizontalAlignment = HorizontalAlignment.Left;
                r.Children.Add(ic);
                var nm = QT($"{l.Name} ×{l.Count:N0}", 12.5, FontWeights.SemiBold, "Ink", trim: true); Grid.SetColumn(nm, 1); r.Children.Add(nm);
                var each = QT(l.Price is long p ? $"{Fk(p)} each" : "no price", 12, FontWeights.Medium, "Faint"); each.HorizontalAlignment = HorizontalAlignment.Right; Grid.SetColumn(each, 2); r.Children.Add(each);
                var val = QT(l.Price != null ? Fk(l.Value) : "–", 12.5, FontWeights.SemiBold, "Gold"); val.HorizontalAlignment = HorizontalAlignment.Right; Grid.SetColumn(val, 3); r.Children.Add(val);
                var rule = new Border { BorderThickness = new Thickness(0, 1, 0, 0) }; rule.SetResourceReference(Border.BorderBrushProperty, "CardHair");
                var host = new Grid(); host.Children.Add(rule); host.Children.Add(r);
                bagBody.Children.Add(host);
            }
        var foot = QT("Your character's bag only: storage and the account warehouse are left out unless you pick them above.", 11.5, FontWeights.Medium, "Faint");
        foot.Margin = new Thickness(0, 8, 0, 0); foot.TextWrapping = TextWrapping.Wrap;
        bagBody.Children.Add(foot);
        page.Children.Add(ProposalCard(bagBody));
        return page;
    }


    static FrameworkElement Fig(string big, string small, string tone = "Ink")
    {
        var sp = new StackPanel();
        var t = QT(big, 15, FontWeights.SemiBold, tone);
        sp.Children.Add(t);
        sp.Children.Add(QT(small, 11.5, FontWeights.Medium, "Faint"));
        return sp;
    }

    // ---------------------------------------------------------------- Timers: events and world bosses, bells, the overlay

    FrameworkElement ProposalTimers()
    {
        var page = new StackPanel { Width = PW };
        page.Children.Add(PageHead("Timers", "On the game's clock · the world map read 4 s ago",
            Switch("Overlay", true), SmallBtn("⇱  Pop out")));

        // the alert line: how far ahead, the chime, what is watched
        var alert = new Grid();
        alert.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        alert.ColumnDefinitions.Add(new ColumnDefinition());
        alert.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var bell = Bell(true, 16); bell.Margin = new Thickness(0, 0, 10, 0); alert.Children.Add(bell);
        var at = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        at.Children.Add(QT("The overlay pops up 5 min before a belled boss or event", 12.5, FontWeights.SemiBold, "Ink"));
        at.Children.Add(QT("Kashapa, Gartua, Lagta and the Abyss · bell any row to add it", 11.5, FontWeights.Medium, "Faint"));
        Grid.SetColumn(at, 1); alert.Children.Add(at);
        var ctl = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        ctl.Children.Add(Seg("5 min", "1", "3", "5 min", "10"));
        ctl.Children.Add(Switch("Chime", true));
        Grid.SetColumn(ctl, 2); alert.Children.Add(ctl);
        page.Children.Add(ProposalCard(alert, new Thickness(14, 10, 14, 10), new Thickness(0, 0, 0, 10)));

        // you're in
        var live = new Grid();
        live.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        live.ColumnDefinitions.Add(new ColumnDefinition());
        live.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var dot = new Ellipse { Width = 8, Height = 8, Margin = new Thickness(2, 0, 12, 0), VerticalAlignment = VerticalAlignment.Center }; dot.SetResourceReference(Shape.FillProperty, "Good");
        live.Children.Add(dot);
        var lt = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        lt.Children.Add(QT("You're in Defend Shugo Merchants", 13, FontWeights.SemiBold, "Ink"));
        lt.Children.Add(QT("Shugo Festival · under way, began 1 min ago", 11.5, FontWeights.Medium, "Faint"));
        Grid.SetColumn(lt, 1); live.Children.Add(lt);
        var lv = QT("Under way", 13, FontWeights.SemiBold, "Good"); Grid.SetColumn(lv, 2); live.Children.Add(lv);
        page.Children.Add(ProposalCard(live, new Thickness(14, 10, 14, 10), new Thickness(0, 0, 0, 10)));

        // belled first, then events, then the rest of the map
        var belled = new (string Name, string Place, string When, string Small, string Tone)[]
        {
            ("Soul Ruler Kashapa", "World boss", "4m 52s", "at 07:03", "Gold"),
            ("Eternal Gartua", "Eternal Isle · Verteron", "5h 06m", "at 11:44", "Ink"),
            ("High Commander Lagta", "Lagta Fortress", "1h 12m", "at 07:50", "Ink"),
        };
        page.Children.Add(TimerSection("Belled", "alerts on", belled.Select(b => TimerRow(b.Name, b.Place, b.When, b.Small, b.Tone, true)).Append(AbyssGroupRow()).ToList()));

        var events = new[]
        {
            TimerRow("Dimensional Invasion", "Every hour at half past", "On now", "started 8 min ago", "Good", false),
            TimerRow("Shugo Festival", "Every hour · Verteron and Altgard", "21m 41s", "at 07:00", "Ink", true),
            TimerRow("Spacetime Rift", "Every 3 hours · one of ten zones", "21m 41s", "at 07:00", "Ink", false),
            TimerRow("Artifact Siege", "Mon, Thu, Sat · Lower Reshanta", "21m 41s", "at 07:00", "Ink", false),
        };
        page.Children.Add(TimerSection("Events", "the site's schedule on the game's clock", events.ToList()));

        var bosses = SampleBosses(DateTime.UtcNow.AddSeconds(-4)).Where(b => b.Name != "Eternal Gartua").Take(10)
            .Select(b => b.State == BossTimers.Kind.Up
                ? TimerRow(b.Name, b.Place, "Up now", $"up {(int)(DateTime.UtcNow - b.SpawnPc).TotalMinutes} min", "Good", false)
                : TimerRow(b.Name, b.Place, BossTimers.Span(b.SpawnPc - DateTime.UtcNow), $"at {b.SpawnPc.ToLocalTime():HH:mm}", "Ink", false)).ToList();
        page.Children.Add(TimerSection("Verteron world bosses", "24 on the map · 14 more below", bosses, Seg("Verteron", "Verteron", "Altgard", "Abyss")));
        return page;
    }

    FrameworkElement TimerSection(string title, string note, List<FrameworkElement> rows, UIElement? right = null)
    {
        var body = new StackPanel();
        body.Children.Add(CardHead(title == "Events" ? "Star" : "Flag", title, note, right));
        var grid = new UniformGrid { Columns = 2 };
        foreach (var r in rows) grid.Children.Add(r);
        body.Children.Add(grid);
        return ProposalCard(body, new Thickness(14, 12, 14, 6), new Thickness(0, 0, 0, 10));
    }

    FrameworkElement TimerRow(string name, string place, string when, string small, string tone, bool belled)
    {
        var g = new Grid { Height = 42, Margin = new Thickness(0, 0, 14, 4) };
        g.ColumnDefinitions.Add(new ColumnDefinition());
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(26) });
        var left = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        left.Children.Add(QT(name, 12.5, FontWeights.SemiBold, "Ink", trim: true));
        left.Children.Add(QT(place, 11, FontWeights.Medium, "Faint", trim: true));
        g.Children.Add(left);
        var right = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 8, 0) };
        var w = QT(when, 13, FontWeights.SemiBold, tone); w.HorizontalAlignment = HorizontalAlignment.Right;
        System.Windows.Documents.Typography.SetNumeralAlignment(w, FontNumeralAlignment.Tabular);
        right.Children.Add(w);
        var s = QT(small, 11, FontWeights.Medium, "Faint"); s.HorizontalAlignment = HorizontalAlignment.Right; right.Children.Add(s);
        Grid.SetColumn(right, 1); g.Children.Add(right);
        var bell = Bell(belled, 14); bell.HorizontalAlignment = HorizontalAlignment.Right; bell.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(bell, 2); g.Children.Add(bell);
        var rule = new Border { BorderThickness = new Thickness(0, 0, 0, 1), Margin = new Thickness(0, 0, 14, 0) }; rule.SetResourceReference(Border.BorderBrushProperty, "CardHair");
        var host = new Grid(); host.Children.Add(rule); host.Children.Add(g);
        return host;
    }

    FrameworkElement AbyssGroupRow() =>
        TimerRow("Abyss world bosses (8)", "Lower Reshanta · open the Abyss map to time them", "No timer", "all 8 belled", "Muted", true);

    /// <summary>The bell: filled gold when the row alerts, an outline when it doesn't.</summary>
    static FrameworkElement Bell(bool on, double size)
    {
        var geo = Geometry.Parse("M6,1 C3.6,1 2,2.9 2,5.2 L2,8.2 L0.6,10.2 L11.4,10.2 L10,8.2 L10,5.2 C10,2.9 8.4,1 6,1 Z M4.4,11.4 C4.7,12.4 5.3,12.9 6,12.9 C6.7,12.9 7.3,12.4 7.6,11.4 Z");
        var p = new System.Windows.Shapes.Path { Data = geo, Stretch = Stretch.Uniform, Width = size, Height = size, StrokeThickness = 1.2 };
        if (on) p.SetResourceReference(Shape.FillProperty, "Gold");
        else { p.SetResourceReference(Shape.StrokeProperty, "Faint"); p.Opacity = 0.8; }
        return p;
    }

    // ---------------------------------------------------------------- the timers overlay over the game

    /// <summary>The in-game timers overlay: hidden until a belled boss or event is near, then this card with a soft chime.</summary>
    internal Border ProposalTimerOverlay()
    {
        var body = new StackPanel { Width = 300 };
        var head = new Grid { Margin = new Thickness(12, 10, 12, 0) };
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        head.ColumnDefinitions.Add(new ColumnDefinition());
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var art = GameArt.Element("boss", 30); art.Margin = new Thickness(0, 0, 10, 0); head.Children.Add(art);
        var nm = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        nm.Children.Add(HudText("Soul Ruler Kashapa", 13.5, FontWeights.SemiBold, Color.FromRgb(0xED, 0xEF, 0xF5)));
        nm.Children.Add(HudText("World boss · spawns 07:03", 11.5, FontWeights.Medium, Color.FromRgb(0xAF, 0xB5, 0xC6)));
        Grid.SetColumn(nm, 1); head.Children.Add(nm);
        var t = HudText("4:52", 24, FontWeights.SemiBold, Color.FromRgb(0xF5, 0xB9, 0x42)); t.VerticalAlignment = VerticalAlignment.Center;
        System.Windows.Documents.Typography.SetNumeralAlignment(t, FontNumeralAlignment.Tabular);
        Grid.SetColumn(t, 2); head.Children.Add(t);
        body.Children.Add(head);
        var track = new Grid { Height = 3, Margin = new Thickness(12, 9, 12, 0) };
        track.Children.Add(new Border { CornerRadius = new CornerRadius(2), Background = new SolidColorBrush(Color.FromRgb(0x23, 0x27, 0x36)) });
        track.Children.Add(new Border { CornerRadius = new CornerRadius(2), Width = 276 * (292.0 / 300), HorizontalAlignment = HorizontalAlignment.Left, Background = new SolidColorBrush(Color.FromRgb(0xF5, 0xB9, 0x42)) });
        body.Children.Add(track);
        var next = new Grid { Margin = new Thickness(12, 8, 12, 9) };
        next.ColumnDefinitions.Add(new ColumnDefinition());
        next.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        next.Children.Add(HudText("Then: Dimensional Invasion at 07:30", 11.5, FontWeights.Medium, Color.FromRgb(0x86, 0x8D, 0xA1)));
        var b = Bell(true, 11); b.HorizontalAlignment = HorizontalAlignment.Right; Grid.SetColumn(b, 1); next.Children.Add(b);
        body.Children.Add(next);
        return new Border
        {
            Child = body, CornerRadius = new CornerRadius(12), BorderThickness = new Thickness(1),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x66, 0xF5, 0xB9, 0x42)),
            Background = new SolidColorBrush(Color.FromArgb(0xEE, 0x0B, 0x0C, 0x10)),
        };
    }

    static TextBlock HudText(string s, double size, FontWeight w, Color c) =>
        new() { Text = s, FontSize = size, FontWeight = w, Foreground = new SolidColorBrush(c), FontFamily = HudKit.Text, TextTrimming = TextTrimming.CharacterEllipsis };

    // ---------------------------------------------------------------- Overlays: the three, side by side, in Options

    FrameworkElement ProposalOverlays()
    {
        var page = new StackPanel { Width = PW };
        page.Children.Add(PageHead("Overlays", "Three panels over the game, each separate. Drag one to move it; right-click it for these settings.", SmallBtn("Place them")));
        page.Children.Add(OverlayBlock("Kinah", "Your run's Kinah an hour, pickups and best, over the game.", true,
            ("Look", Seg("Buddy", "Badge", "Buddy", "Pop")), ("Shows", QT("an hour · session · top drop · rare drops", 12, FontWeights.Medium, "Muted")), ("Click-through", Switch("", true))));
        page.Children.Add(OverlayBlock("Timers", "Hidden until a belled boss or event is near; then it shows the countdown, with a chime.", true,
            ("Pops up", Seg("5 min", "1", "3", "5 min", "10")), ("Chime", Switch("", true)), ("Belled", QT("Kashapa, Gartua, Lagta, Abyss (8) · edit on Timers", 12, FontWeights.Medium, "Muted"))));
        page.Children.Add(OverlayBlock("Parser", "Boss fights: DPS by member, the boss's HP, the pull timer.", false,
            ("Mode", Seg("Bosses only", "Bosses only", "Full run", "Every fight")), ("Shows", QT("group DPS · top 6 · boss HP", 12, FontWeights.Medium, "Muted")), ("Click-through", Switch("", false))));
        return page;
    }

    FrameworkElement OverlayBlock(string name, string what, bool on, params (string Label, FrameworkElement Ctl)[] rows)
    {
        var body = new StackPanel();
        body.Children.Add(CardHead(name switch { "Kinah" => "Coin", "Timers" => "Flag", _ => "Sword" }, $"{name} overlay", "", Switch(on ? "On" : "Off", on)));
        var w = QT(what, 12, FontWeights.Medium, "Faint"); w.Margin = new Thickness(0, -4, 0, 8); w.TextWrapping = TextWrapping.Wrap;
        body.Children.Add(w);
        foreach (var (label, ctl) in rows)
        {
            var g = new Grid { Height = 34 };
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(130) });
            g.ColumnDefinitions.Add(new ColumnDefinition());
            g.Children.Add(QT(label, 12.5, FontWeights.SemiBold, "Text2"));
            ctl.HorizontalAlignment = HorizontalAlignment.Left;
            if (ctl is StackPanel sp && sp.Margin.Left == 12) sp.Margin = new Thickness(-8, 0, 0, 0);
            Grid.SetColumn(ctl, 1); g.Children.Add(ctl);
            var rule = new Border { BorderThickness = new Thickness(0, 1, 0, 0) }; rule.SetResourceReference(Border.BorderBrushProperty, "CardHair");
            var host = new Grid(); host.Children.Add(rule); host.Children.Add(g);
            body.Children.Add(host);
        }
        return ProposalCard(body, new Thickness(14, 12, 14, 6), new Thickness(0, 0, 0, 10));
    }

    static IEnumerable<T> Descendants<T>(DependencyObject d) where T : DependencyObject
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(d); i++)
        {
            var c = VisualTreeHelper.GetChild(d, i);
            if (c is T t) yield return t;
            foreach (var x in Descendants<T>(c)) yield return x;
        }
    }
}
