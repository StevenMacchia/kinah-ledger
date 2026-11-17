using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using static KinahLedger.Ui.HudKit;
using static KinahLedger.Ui.GlitchKit;

namespace KinahLedger.Ui;

/// <summary>
/// The Glitch looks' skills cards (the pop-up on a meter row, Ui/Themes/MemberCard.cs), each drawn as its look's own page:
/// VHS a camcorder's OSD menu page, Corrupted a torn memory dump, Terminal a `perf report`. Every figure the Parser's card
/// has, in the look's face: the name in its class's name colour (gold for you) with the Combat Power beside it, the class,
/// DPS · share · damage, the rates, up to six abilities each with its bar against the top one, its share, damage and the
/// crit/back columns, the specialties after its name, "N more", and the foot (hits, biggest, healed, deaths). Figures are
/// tabular and right-aligned in the table, never animated: on open (only while the look moves) the card settles in under
/// 300 ms around them (an OSD redraw, a decode, lines printed whole). Each card keeps clear room round its shadow, so
/// nothing is cut at the pop-up's edge.
/// </summary>
static class GlitchCards
{
    /// <summary>A crit or back-attack column's width.</summary>
    const double RateW = 38;

    /// <summary>The card plays its entrance: the look moves, and this is no render, no test, no held frame.</summary>
    static bool Enters(bool moves) => moves && !Motion.Still && Motion.FrozenAt == null && Motion.On;

    /// <summary>
    /// A bar grows from its left as the card opens (the <paramref name="i"/>th, 24 ms after the one before), in
    /// <paramref name="steps"/> jumps when set; settled by 290 ms at most. Held or still: drawn full at once.
    /// </summary>
    static void Grow(FrameworkElement bar, int i, bool enter, int steps = 0)
    {
        if (!enter) return;
        var s = new ScaleTransform(0, 1);
        bar.RenderTransformOrigin = new Point(0, .5);
        bar.RenderTransform = s;
        double delay = 40 + 24 * Math.Min(i, 5), len = 130, total = delay + len;
        Motion.Play(bar, total, t =>
        {
            double q = Motion.Out(Math.Clamp((t * total - delay) / len, 0, 1));
            s.ScaleX = steps > 0 ? Math.Ceiling(q * steps) / steps : q;
        }, () => bar.ClearValue(UIElement.RenderTransformProperty));
    }

    static TextBlock R(string s, double size, Brush fg, FontFamily face, FontWeight? w = null)
    {
        var t = Txt(s, size, fg, face, w);
        t.TextAlignment = TextAlignment.Right; t.HorizontalAlignment = HorizontalAlignment.Right;
        return t;
    }

    static T At<T>(Grid g, int col, T el, int row = 0) where T : UIElement { Grid.SetColumn(el, col); Grid.SetRow(el, row); g.Children.Add(el); return el; }

    /// <summary>The table's columns: the skill (the rest), share, damage, then crit and back when the card has them.</summary>
    static Grid Table(MemberCard c, double height, double shareW, double dmgW, bool shareFirst = false)
    {
        var g = new Grid { Height = height };
        if (shareFirst) g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(shareW) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        if (!shareFirst) g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(shareW) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(dmgW) });
        if (c.CritCol) g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(RateW) });
        if (c.BackCol) g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(RateW) });
        return g;
    }

    static double Wide(MemberCard c, double w) => w + (c.CritCol ? RateW : 0) + (c.BackCol ? RateW : 0);

    /// <summary>The rates as their own words ("30% crit", "30% of the damage from crits", "19% from behind"), each whole.</summary>
    static List<string> Rates(MemberCard c)
    {
        var l = new List<string>();
        if (c.Crit.Length > 0) l.Add(L.F("{0} crit", c.Crit));
        if (c.CritShare.Length > 0) l.Add(L.F("{0} of the damage from crits", c.CritShare));
        if (c.Back.Length > 0) l.Add(L.F("{0} from behind", c.Back));
        return l;
    }

    /// <summary>Words in a row that wrap whole at the card's edge, a separator between them.</summary>
    /// <param name="sep">The separator after each item but the last (it stays on the item's line when the next wraps); null: a gap.</param>
    static WrapPanel Words(IEnumerable<(string Text, Brush Fg)> items, double size, FontFamily face, string? sep, Brush sepFg, FontWeight? w = null, double gap = 14)
    {
        var p = new WrapPanel { Orientation = Orientation.Horizontal };
        var list = items.ToList();
        for (int i = 0; i < list.Count; i++)
        {
            var (text, fg) = list[i];
            var t = Txt("", size, fg, face, w);
            t.Inlines.Add(Rn(text, fg));
            if (sep != null && i < list.Count - 1) t.Inlines.Add(Rn(sep, sepFg));
            if (sep == null && i < list.Count - 1) t.Margin = new Thickness(0, 0, gap, 0);
            p.Children.Add(t);
        }
        return p;
    }

    /// <summary>The foot's words: hits, the biggest hit, the healing (green), the deaths (red).</summary>
    static List<(string, Brush)> Foot(MemberCard c, Func<string, string> words, Brush ink, Brush heal, Brush death)
    {
        var l = new List<(string, Brush)>();
        if (c.Hits.Length > 0) l.Add((words(c.Hits), ink));
        if (c.Biggest.Length > 0) l.Add((words(c.Biggest), ink));
        if (c.Healed.Length > 0) l.Add((words(c.Healed), heal));
        if (c.DeathsText.Length > 0) l.Add((words(c.DeathsText), death));
        return l;
    }

    /// <summary>A skill's name and, after it, the specialties its hits named, smaller, in the look's accent.</summary>
    static TextBlock SkillName(CardAbility a, double size, Brush fg, Brush picks, FontFamily face, FontWeight w)
    {
        var t = Txt("", size, fg, face, w, trim: true);
        t.Inlines.Add(Rn(a.Name, fg));
        if (a.Picks.Length > 0) t.Inlines.Add(Rn("  " + a.Picks, picks, size - 1.5, FontWeights.Bold));
        return t;
    }

    static void Tip(FrameworkElement row, CardAbility a) { if (a.Tip.Length > 0) row.ToolTip = a.Tip; }

    /// <summary>The card in clear room for its shadow: nothing drawn past this room (the pop-up is cut at its bounds).</summary>
    static Border Room(FrameworkElement panel, double room, Effect? shadow)
    {
        var holder = new Border { Child = panel, Effect = shadow };
        var card = new Border { Padding = new Thickness(room), Background = Brushes.Transparent, Child = holder, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top };
        TextOptions.SetTextFormattingMode(card, TextFormattingMode.Display);
        return card;
    }

    // ===================================================================================================================
    // VHS: the camcorder's OSD menu page

    public static FrameworkElement Vhs(MemberCard c, bool moves)
    {
        var face = VhsFace;
        bool enter = Enters(moves);
        const uint RedC = 0xFF3B30, YellowC = 0xFFD23F, CyanC = 0x28E0FF, GreenC = 0x45FF86;
        Brush white = Br(0xFFFFFF), dim = Br(0xFFFFFF, .64), faint = Br(0xFFFFFF, .42), black = Br(0x000000), yellow = Br(YellowC), red = Br(0xFF5A4F);
        var cc = c.ClassColor;
        var lit = Frozen(Color.FromRgb((byte)Math.Min(255, cc.R + 25), (byte)Math.Min(255, cc.G + 25), (byte)Math.Min(255, cc.B + 25)));
        double w = Wide(c, 300);
        var body = new StackPanel { Width = w, Margin = new Thickness(13, 10, 13, 11) };
        string Up(string s) => s.ToUpper(CultureInfo.CurrentCulture).Replace("/S", "/s").Replace("/H", "/h");

        // the menu's tab: "▶ PLAYER" inverted, a dotted leader, the class in its colour with its tick
        var tab = new Grid();
        tab.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); tab.ColumnDefinitions.Add(new ColumnDefinition()); tab.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        tab.Children.Add(new Border { Background = white, Padding = new Thickness(4, 1, 5, 0), Child = Txt("▶ " + Up(L.T("Player")), 9.5, black, face) });
        At(tab, 1, new Rectangle { Height = 1, Fill = Dots, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(7, 1, 7, 0) });
        var cls = new StackPanel { Orientation = Orientation.Horizontal };
        cls.Children.Add(new Border { Width = 3, Height = 11, Background = Frozen(cc), Margin = new Thickness(0, 0, 5, 0), VerticalAlignment = VerticalAlignment.Center });
        cls.Children.Add(Txt(Up(c.Class), 10, lit, face));
        At(tab, 2, cls);
        body.Children.Add(tab);

        // the name in its class's name colour (gold for you, the REC dot before it), the Combat Power beside it
        var nameRow = new DockPanel { HorizontalAlignment = HorizontalAlignment.Left, LastChildFill = true, Margin = new Thickness(0, 8, 0, 0) };
        if (c.You)
        {
            var dot = new Ellipse { Width = 8, Height = 8, Fill = Br(RedC), Margin = new Thickness(0, 0, 6, 0), VerticalAlignment = VerticalAlignment.Center };
            DockPanel.SetDock(dot, Dock.Left); nameRow.Children.Add(dot);
        }
        if (c.Cp.Length > 0)
        {
            var cp = Txt("", 11.5, Br(0xFFFFFF, .76), face);
            cp.Inlines.Add(Rn(L.T("CP") + " ", faint, 10)); cp.Inlines.Add(Rn(c.Cp, Br(0xFFFFFF, .76)));
            cp.Margin = new Thickness(9, 3, 0, 0);
            DockPanel.SetDock(cp, Dock.Right); nameRow.Children.Add(cp);
        }
        nameRow.Children.Add(Txt(c.Name, 16, c.You ? yellow : Frozen(c.NameColor), face, FontWeights.Bold, trim: true));
        body.Children.Add(nameRow);

        // the figures as three OSD fields, each split red/cyan behind its steady digits
        var sums = new Grid { Margin = new Thickness(0, 8, 0, 0) };
        var cells = new List<(string Label, string Value)>();
        if (c.Dps.Length > 0) cells.Add(("DPS", c.Dps));
        cells.Add((Up(L.T("share")), c.Share));
        cells.Add((Up(L.T("damage")), c.Damage));
        for (int i = 0; i < cells.Count; i++)
        {
            sums.ColumnDefinitions.Add(new ColumnDefinition());
            var cell = new StackPanel { Margin = new Thickness(i == 0 ? 0 : 9, 0, 0, 0) };
            cell.Children.Add(Txt(cells[i].Label, 9, faint, face));
            var v = Split(cells[i].Value, 18, face, FontWeights.Bold, white, Rgb(RedC, .8), Rgb(CyanC, .8), 1.3);
            v.HorizontalAlignment = HorizontalAlignment.Left; v.Margin = new Thickness(0, 1, 0, 0);
            cell.Children.Add(v);
            At(sums, i, cell);
            if (i > 0) At(sums, i, new Border { Width = 1, Background = Br(0xFFFFFF, .22), HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 2, 0, 2) });
        }
        body.Children.Add(sums);
        var rates = Rates(c);
        if (rates.Count > 0)
        {
            var rw = Words(rates.Select(r => (Up(r), yellow)), 9.5, face, null, faint, gap: 12);
            rw.Margin = new Thickness(0, 6, 0, 0);
            body.Children.Add(rw);
        }

        // the skills as the menu's items: the top one under the cursor, each a segmented level in the class's colour
        if (c.Abilities.Count > 0)
        {
            body.Children.Add(new Rectangle { Height = 1, Fill = Dots, Margin = new Thickness(0, 9, 0, 4) });
            var head = Table(c, 13, 46, 54);
            var h0 = Txt(Up(L.T("Skills")), 9, faint, face); h0.Margin = new Thickness(13, 0, 0, 0); At(head, 0, h0);
            At(head, 1, R(Up(L.T("share")), 9, faint, face));
            At(head, 2, R(Up(L.T("damage")), 9, faint, face));
            if (c.CritCol) At(head, 3, R(Up(L.T("crit")), 9, faint, face));
            if (c.BackCol) At(head, c.CritCol ? 4 : 3, R(Up(L.T("back")), 9, faint, face));
            body.Children.Add(head);
            var cellsBrush = Segments(Rgb(0x000000, .85), 4, 1.25);
            for (int i = 0; i < c.Abilities.Count; i++)
            {
                var a = c.Abilities[i];
                bool top = i == 0;
                var row = Table(c, 25, 46, 54);
                row.Margin = new Thickness(0, 1, 0, 0);
                if (top)
                {
                    var sel = new Border { BorderBrush = Br(0xFFFFFF, .85), BorderThickness = new Thickness(1), Background = Br(0xFFFFFF, .07), Margin = new Thickness(-4, 0, -4, 0) };
                    Grid.SetColumnSpan(sel, row.ColumnDefinitions.Count); row.Children.Add(sel);
                }
                var name = new Grid { Margin = new Thickness(0, 2, 0, 3) };
                name.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(13) }); name.ColumnDefinitions.Add(new ColumnDefinition());
                name.RowDefinitions.Add(new RowDefinition()); name.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                if (top) At(name, 0, Txt("▶", 9, white, face));
                At(name, 1, SkillName(a, 11.5, top ? white : Br(0xFFFFFF, .9), yellow, face, FontWeights.Bold));
                var level = new GlitchLevel(4, Br(0xFFFFFF, .12), cellsBrush) { Share = a.OfTop };
                level.Fill.Background = lit;
                level.Root.Margin = new Thickness(0, 2, 6, 0);
                At(name, 1, level.Root, 1);
                Grow(level.Fill, i, enter, 8);
                row.Children.Add(name);
                At(row, 1, R(a.ShareText, 10.5, dim, face));
                At(row, 2, R(a.Damage, 12, white, face));
                if (c.CritCol) At(row, 3, R(a.Crit, 10.5, a.Crit == "–" ? faint : yellow, face));
                if (c.BackCol) At(row, c.CritCol ? 4 : 3, R(a.Back, 10.5, a.Back == "–" ? faint : dim, face));
                Tip(row, a);
                body.Children.Add(row);
            }
            if (c.MoreLine.Length > 0)
            {
                var more = Txt("+ " + Up(c.MoreLine), 9.5, Br(0xFFFFFF, .7), face); more.Margin = new Thickness(13, 4, 0, 0);
                body.Children.Add(more);
            }
        }
        var foot = Foot(c, Up, dim, Br(GreenC), red);
        if (foot.Count > 0)
        {
            body.Children.Add(new Rectangle { Height = 1, Fill = Dots, Margin = new Thickness(0, 8, 0, 5) });
            body.Children.Add(Words(foot, 10, face, null, faint, gap: 16));
        }

        // the viewfinder: black glass, vignette, the head-switching noise at its foot, scanlines and corner marks over the words
        var panel = new Grid { ClipToBounds = true };
        panel.Children.Add(new Border { Background = Br(0x050507, .93), CornerRadius = new CornerRadius(2) });
        panel.Children.Add(new Rectangle { Fill = Vignette(Colors.Black, .4), IsHitTestVisible = false });
        panel.Children.Add(new Rectangle { Fill = NoiseBrush(.3), Height = 2, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 0, 1), IsHitTestVisible = false });
        panel.Children.Add(body);
        panel.Children.Add(new Rectangle { Fill = Lines(Rgb(0xFFFFFF, .045), 3, 1), IsHitTestVisible = false });
        panel.Children.Add(Corners(Br(0xFFFFFF, .7), 8, 1.4, 4));
        if (enter)
        {
            // the OSD redraws: the page opens in three jumps from a line, a bright scan line runs down it once
            var s = new ScaleTransform(1, .06);
            panel.RenderTransformOrigin = new Point(.5, .5); panel.RenderTransform = s;
            Motion.Play(panel, 110, t => s.ScaleY = t < .34 ? .06 : t < .67 ? .5 : 1, () => panel.ClearValue(UIElement.RenderTransformProperty));
            var scan = new Rectangle { Height = 2, Fill = Br(0xFFFFFF, .55), VerticalAlignment = VerticalAlignment.Top, IsHitTestVisible = false };
            var st = new TranslateTransform(); scan.RenderTransform = st;
            panel.Children.Add(scan);
            Motion.Play(scan, 260, t => { st.Y = (panel.ActualHeight > 0 ? panel.ActualHeight : 300) * t; scan.Opacity = 1 - t * .6; }, () => panel.Children.Remove(scan));
        }
        return Room(panel, 12, new DropShadowEffect { Color = Colors.Black, BlurRadius = 9, ShadowDepth = 2, Opacity = .6 });
    }

    static readonly Brush Dots = MakeDots();
    static Brush MakeDots()
    {
        var d = new DrawingGroup();
        d.Children.Add(new GeometryDrawing(Brushes.Transparent, null, new RectangleGeometry(new Rect(0, 0, 4, 1))));
        d.Children.Add(new GeometryDrawing(Br(0xFFFFFF, .42), null, new RectangleGeometry(new Rect(0, 0, 2, 1))));
        var b = new DrawingBrush(d) { TileMode = TileMode.Tile, Viewport = new Rect(0, 0, 4, 1), ViewportUnits = BrushMappingMode.Absolute, Stretch = Stretch.Fill };
        b.Freeze();
        return b;
    }

    // ===================================================================================================================
    // Corrupted: a torn memory dump

    /// <summary>The card's frame slices: torn through the table's middle, never through a figure's own pixels (the slab tears, the words stay).</summary>
    static readonly (double, double, double)[] DumpSlices = { (0, .14, 0), (.14, .2, 4), (.2, .58, 0), (.58, .63, -4), (.63, 1, 0) };
    /// <summary>How far each skill's slab sits sideways (the slab only).</summary>
    static readonly double[] SlabTorn = { 0, 3, -2, 3, -1, 2 };

    public static FrameworkElement Corrupt(MemberCard c, bool moves)
    {
        var face = CodeFace;
        bool enter = Enters(moves);
        const uint MagC = 0xFF2B6A, CyanC = 0x2BE6FF, LilacC = 0xC9B8FF, GoldC = 0xFFD24A, GreenC = 0x3DFF8C;
        Brush white = Br(0xFFFFFF), lilac = Br(LilacC), lilacDim = Br(LilacC, .66), mag = Br(MagC), cyan = Br(CyanC), gold = Br(GoldC), red = Br(0xFF5A6A);
        var cc = c.ClassColor;
        double w = Wide(c, 300);
        var body = new StackPanel { Width = w, Margin = new Thickness(12, 9, 12, 10) };
        string Low(string s) => s.ToLower(CultureInfo.CurrentCulture);
        string Key(string s) => Low(s).Replace(' ', '_');

        // the dump's header: the broken stream's tick, MEMBER_DUMP, the class in its colour
        var meta = new Grid();
        meta.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); meta.ColumnDefinitions.Add(new ColumnDefinition()); meta.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var tick = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) };
        foreach (var (bw, b) in new[] { (7.0, mag), (3.0, cyan), (2.0, white), (5.0, Br(LilacC, .5)) })
            tick.Children.Add(new Border { Width = bw, Height = 7, Background = b, Margin = new Thickness(0, 0, 1.5, 0) });
        meta.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Children = { tick, Txt(L.T("MEMBER_DUMP"), 9.5, lilacDim, face) } });
        var cls = Txt(Low(c.Class), 10.5, Frozen(Color.FromArgb(0xE6, cc.R, cc.G, cc.B)), face);
        At(meta, 2, cls);
        body.Children.Add(meta);

        // the name (gold for you, a cyan pointer before it) with fixed magenta/cyan ghosts, cp= beside it
        var nameRow = new DockPanel { HorizontalAlignment = HorizontalAlignment.Left, LastChildFill = true, Margin = new Thickness(0, 6, 0, 0) };
        if (c.You) { var you = Txt("▶", 11, cyan, face); you.Margin = new Thickness(0, 1, 6, 0); DockPanel.SetDock(you, Dock.Left); nameRow.Children.Add(you); }
        if (c.Cp.Length > 0)
        {
            var cp = Txt("", 10.5, lilac, face);
            cp.Inlines.Add(Rn(Low(L.T("CP")) + "=", lilacDim)); cp.Inlines.Add(Rn(c.Cp, lilac));
            cp.Margin = new Thickness(10, 3, 0, 0);
            DockPanel.SetDock(cp, Dock.Right); nameRow.Children.Add(cp);
        }
        var nameFg = c.You ? gold : Frozen(c.NameColor);
        var nm = new Grid { VerticalAlignment = VerticalAlignment.Center };
        foreach (var (dx, col) in new[] { (-.9, Rgb(MagC, .45)), (.9, Rgb(CyanC, .45)) })
        {
            var g = Txt(c.Name, 16, Frozen(col), face, FontWeights.Bold, trim: true); g.RenderTransform = new TranslateTransform(dx, 0); nm.Children.Add(g);
        }
        nm.Children.Add(Txt(c.Name, 16, nameFg, face, FontWeights.Bold, trim: true));
        nameRow.Children.Add(nm);
        body.Children.Add(nameRow);

        // the figures as key=value cells, each with a torn colour edge on its left
        var sums = new Grid { Margin = new Thickness(0, 7, 0, 0) };
        var cells = new List<(string Label, string Value)>();
        if (c.Dps.Length > 0) cells.Add(("dps", c.Dps));
        cells.Add((Key(L.T("share")), c.Share));
        cells.Add((Key(L.T("damage")), c.Damage));
        Brush[] edges = { mag, cyan, Br(LilacC, .8) };
        for (int i = 0; i < cells.Count; i++)
        {
            sums.ColumnDefinitions.Add(new ColumnDefinition());
            var cell = new StackPanel { Margin = new Thickness(7, 0, 4, 0) };
            cell.Children.Add(Txt(cells[i].Label, 9.5, lilacDim, face));
            var v = Split(cells[i].Value, 17, face, FontWeights.Bold, white, Rgb(MagC, .75), Rgb(CyanC, .75), 1.5);
            v.HorizontalAlignment = HorizontalAlignment.Left;
            cell.Children.Add(v);
            At(sums, i, new Border { Width = 2, Background = edges[i % edges.Length], HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 2, 0, 1 + 3 * (i % 2)) });
            At(sums, i, cell);
        }
        body.Children.Add(sums);
        var rates = Rates(c);
        if (rates.Count > 0)
        {
            var rw = Words(rates.Select(r => (r, gold)), 10.5, face, "  // ", Br(MagC, .8));
            rw.Margin = new Thickness(0, 6, 0, 0);
            body.Children.Add(rw);
        }

        // the skills: torn slabs, an offset and a class edge on each, a thin level with its magenta and cyan ghosts along its foot
        var slabs = new List<TranslateTransform>();
        if (c.Abilities.Count > 0)
        {
            var rule = new Grid { Height = 2, Margin = new Thickness(0, 8, 0, 4) };
            rule.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(3, GridUnitType.Star) }); rule.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); rule.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(2, GridUnitType.Star) });
            rule.Children.Add(new Border { Background = Br(MagC, .7), Height = 1, VerticalAlignment = VerticalAlignment.Top });
            At(rule, 1, new Border { Background = Br(CyanC, .7), Height = 1, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(3, 0, 0, 0) });
            At(rule, 2, new Border { Background = Br(MagC, .7), Height = 1, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(-2, 0, 0, 0) });
            body.Children.Add(rule);
            var head = Table(c, 13, 48, 54);
            var h0 = Txt("// " + Key(L.T("Skills")), 9.5, lilacDim, face); h0.Margin = new Thickness(6, 0, 0, 0); At(head, 0, h0);
            At(head, 1, R(Key(L.T("share")), 9.5, lilacDim, face));
            At(head, 2, R(Key(L.T("damage")), 9.5, lilacDim, face));
            if (c.CritCol) At(head, 3, R(Key(L.T("crit")), 9.5, lilacDim, face));
            if (c.BackCol) At(head, c.CritCol ? 4 : 3, R(Key(L.T("back")), 9.5, lilacDim, face));
            head.Margin = new Thickness(0, 0, 4, 1);
            body.Children.Add(head);
            for (int i = 0; i < c.Abilities.Count; i++)
            {
                var a = c.Abilities[i];
                bool top = i == 0;
                var row = new Grid { Height = 22, Margin = new Thickness(0, 2, 0, 0) };
                // the slab, knocked sideways (never the words), the class's edge on its left
                var slab = new Grid { Margin = new Thickness(-3, 0, -3, 0) };
                var tt = new TranslateTransform(SlabTorn[i % SlabTorn.Length], 0); slab.RenderTransform = tt; slabs.Add(tt);
                slab.Children.Add(new Border { Background = top ? Br(0x2A2140, .95) : Br(0x16132A, .9), BorderBrush = top ? Br(0xFFFFFF, .55) : null, BorderThickness = new Thickness(top ? 1 : 0) });
                slab.Children.Add(new Border { Width = 3, HorizontalAlignment = HorizontalAlignment.Left, Background = Frozen(cc) });
                row.Children.Add(slab);
                var level = new GlitchLevel(2, Br(LilacC, .08));
                level.Echo(-2, 0).Background = Br(MagC, .65);
                level.Echo(2, 0).Background = Br(CyanC, .65);
                level.Fill.Background = Frozen(cc);
                level.Share = a.OfTop;
                level.Root.VerticalAlignment = VerticalAlignment.Bottom; level.Root.Margin = new Thickness(0, 0, 8, 1);
                Grow(level.Root, i, enter, 6);
                var t = Table(c, 22, 48, 54);
                t.Margin = new Thickness(6, 0, 4, 2);
                var name = new DockPanel { LastChildFill = true, VerticalAlignment = VerticalAlignment.Center };
                var idx = new Border { Background = top ? mag : null, Padding = new Thickness(2, 0, 2, 0), Margin = new Thickness(0, 0, 6, 0), VerticalAlignment = VerticalAlignment.Center, Child = Txt((i).ToString("00", CultureInfo.InvariantCulture), 9.5, top ? white : lilacDim, face) };
                DockPanel.SetDock(idx, Dock.Left); name.Children.Add(idx);
                name.Children.Add(SkillName(a, 11.5, top ? white : Br(0xEDE6FF), gold, face, FontWeights.Bold));
                t.Children.Add(name);
                At(t, 1, R(a.ShareText, 10.5, lilac, face));
                At(t, 2, R(a.Damage, 12, white, face));
                if (c.CritCol) At(t, 3, R(a.Crit, 10.5, a.Crit == "–" ? lilacDim : gold, face));
                if (c.BackCol) At(t, c.CritCol ? 4 : 3, R(a.Back, 10.5, a.Back == "–" ? lilacDim : lilac, face));
                // the level runs under the skill's name only
                var levelHost = Table(c, 22, 48, 54); levelHost.IsHitTestVisible = false; levelHost.Margin = new Thickness(6, 0, 4, 0);
                At(levelHost, 0, level.Root);
                row.Children.Add(levelHost);
                row.Children.Add(t);
                Tip(row, a);
                body.Children.Add(row);
            }
            if (c.MoreLine.Length > 0)
            {
                var more = Txt("/* " + c.MoreLine + " */", 10, lilacDim, face); more.Margin = new Thickness(6, 5, 0, 0);
                body.Children.Add(more);
            }
        }
        var foot = Foot(c, s => s, lilac, Br(GreenC), red);
        if (foot.Count > 0)
        {
            var fw = Words(foot, 10.5, face, "  // ", Br(MagC, .7));
            fw.Margin = new Thickness(0, 8, 0, 0);
            body.Children.Add(fw);
        }

        // the broken frame: the sliced ground with its ghost edges (drawn past the panel: the room keeps them), scanlines over it
        var ground = new SlicedGround(DumpSlices) { Alpha = .95 };
        var panel = new Grid();
        panel.Children.Add(ground);
        panel.Children.Add(body);
        panel.Children.Add(new Rectangle { Fill = Lines(Rgb(0xFFFFFF, .03), 2, 1), IsHitTestVisible = false });
        if (enter)
        {
            // the dump decodes: the slabs snap home from wide tears in jumps, two slices of the frame jump and settle
            double[] k = { 11, -8, 5, 0 };
            Motion.Play(body, 240, t =>
            {
                for (int i = 0; i < slabs.Count; i++)
                {
                    int s = (int)Math.Floor(Math.Clamp((t * 240 - 20 * i) / 140, 0, 1) * (k.Length - 1));
                    slabs[i].X = SlabTorn[i % SlabTorn.Length] + k[s] * (i % 2 == 0 ? 1 : -1);
                }
                int f = (int)(t * 6);
                ground.Tear(1, t < .7 ? (f % 2 == 0 ? 6 : -4) : 0);
                ground.Tear(3, t < .55 ? (f % 2 == 0 ? -5 : 4) : 0);
            }, () =>
            {
                for (int i = 0; i < slabs.Count; i++) slabs[i].X = SlabTorn[i % SlabTorn.Length];
                ground.Tear(1, 0); ground.Tear(3, 0);
            });
        }
        return Room(panel, 15, new DropShadowEffect { Color = Colors.Black, BlurRadius = 10, ShadowDepth = 2, Opacity = .6 });
    }

    // ===================================================================================================================
    // Terminal: `perf report`

    public static FrameworkElement Terminal(MemberCard c, bool moves)
    {
        var face = CrtFace;
        bool enter = Enters(moves);
        const uint PhosC = 0x5CFF9A, PaleC = 0xD6FFE4, AmberC = 0xFFB641, GroundC = 0x031008, MintC = 0x7DFFC4;
        Brush phos = Br(PhosC), phosDim = Br(PhosC, .74), phosFaint = Br(PhosC, .5), pale = Br(PaleC), amber = Br(AmberC), ground = Br(GroundC), red = Br(0xFF6B5E);
        var cc = c.ClassColor;
        var glow = Rgb(PhosC);
        double w = Wide(c, 300);
        var body = new StackPanel { Width = w, Margin = new Thickness(12, 8, 12, 10) };
        string Low(string s) => s.ToLower(CultureInfo.CurrentCulture);

        // the command, then the name (gold for you) with its cp beside it, the class on the right
        var cmd = Txt("", 11, phosDim, face, FontWeights.SemiBold);
        cmd.Inlines.Add(Rn("$ ", phosFaint)); cmd.Inlines.Add(Rn("perf report --sort sym", phosDim));
        body.Children.Add(cmd);
        var head = new Grid { Margin = new Thickness(0, 4, 0, 0) };
        head.ColumnDefinitions.Add(new ColumnDefinition()); head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var nameRow = new DockPanel { HorizontalAlignment = HorizontalAlignment.Left, LastChildFill = true };
        var hash = Txt("# ", 14, phosFaint, face, FontWeights.Bold); DockPanel.SetDock(hash, Dock.Left); nameRow.Children.Add(hash);
        if (c.Cp.Length > 0)
        {
            var cp = Txt("", 11, phosDim, face, FontWeights.SemiBold);
            cp.Inlines.Add(Rn(Low(L.T("CP")) + " ", phosFaint)); cp.Inlines.Add(Rn(c.Cp, phos));
            cp.Margin = new Thickness(9, 3, 0, 0);
            DockPanel.SetDock(cp, Dock.Right); nameRow.Children.Add(cp);
        }
        if (c.You) { var star = Txt("*", 14, amber, face, FontWeights.Bold); DockPanel.SetDock(star, Dock.Right); nameRow.Children.Add(star); }
        var nameFg = c.You ? Br(0xFFC861) : Frozen(c.NameColor);
        var nm = Txt(c.Name, 15, nameFg, face, FontWeights.Bold, trim: true);
        nameRow.Children.Add(nm);
        head.Children.Add(nameRow);
        var cls = Txt(Low(c.Class), 11, Frozen(cc), face, FontWeights.SemiBold); cls.Margin = new Thickness(10, 2, 0, 0);
        At(head, 1, cls);
        body.Children.Add(head);

        // the figures: "dps 4.1K   share 32.0%   damage 649.3K", the values pale with a phosphor glow
        var sums = new WrapPanel { Margin = new Thickness(0, 5, 0, 0) };
        var cells = new List<(string Label, string Value)>();
        if (c.Dps.Length > 0) cells.Add(("dps", c.Dps));
        cells.Add((Low(L.T("share")), c.Share));
        cells.Add((Low(L.T("damage")), c.Damage));
        foreach (var (label, value) in cells)
        {
            var sp = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 10, 0) };
            var l = Txt(label + " ", 10.5, phosDim, face, FontWeights.SemiBold); l.VerticalAlignment = VerticalAlignment.Bottom; l.Margin = new Thickness(0, 0, 0, 2);
            sp.Children.Add(l);
            sp.Children.Add(Phosphor(value, 15, face, FontWeights.Bold, pale, glow, .8, .3));
            sums.Children.Add(sp);
        }
        body.Children.Add(sums);
        var rates = Rates(c);
        if (rates.Count > 0)
        {
            var rw = Words(rates.Select(r => (r, amber)), 11, face, null, phosFaint, FontWeights.SemiBold, 16);
            rw.Margin = new Thickness(0, 3, 0, 0);
            body.Children.Add(rw);
        }

        // the report: an inverted head, then a line a skill: its share first as perf puts the overhead, the symbol over a
        // bar of block cells in the class's colour, the damage and the rates right-aligned
        if (c.Abilities.Count > 0)
        {
            var th = Table(c, 15, 46, 54, shareFirst: true);
            th.Margin = new Thickness(0, 7, 0, 2);
            var bar = new Border { Background = Br(PhosC, .85) }; Grid.SetColumnSpan(bar, th.ColumnDefinitions.Count); th.Children.Add(bar);
            var s0 = R(L.T("SHR%"), 10, ground, face); s0.Margin = new Thickness(0, 0, 3, 0); At(th, 0, s0);
            var s1 = Txt(L.T("SYMBOL"), 10, ground, face); s1.Margin = new Thickness(8, 0, 0, 0); At(th, 1, s1);
            var s2 = R(L.T("DMG"), 10, ground, face); s2.Margin = new Thickness(0, 0, 2, 0); At(th, 2, s2);
            if (c.CritCol) { var x = R(L.T("CRIT"), 10, ground, face); x.Margin = new Thickness(0, 0, 2, 0); At(th, 3, x); }
            if (c.BackCol) { var x = R(L.T("BACK"), 10, ground, face); x.Margin = new Thickness(0, 0, 3, 0); At(th, c.CritCol ? 4 : 3, x); }
            body.Children.Add(th);
            var cellsBrush = Segments(Rgb(GroundC), 5, 1.5);
            for (int i = 0; i < c.Abilities.Count; i++)
            {
                var a = c.Abilities[i];
                bool top = i == 0;
                var row = Table(c, 19, 46, 54, shareFirst: true);
                if (top) { var band = new Border { Background = Br(PhosC, .14), Margin = new Thickness(-3, 0, -3, 0) }; Grid.SetColumnSpan(band, row.ColumnDefinitions.Count); row.Children.Add(band); }
                var sh = R(a.ShareText, 11, top ? pale : phos, face, FontWeights.SemiBold); sh.Margin = new Thickness(0, 0, 3, 0); At(row, 0, sh);
                // a solid band under the letters (no cells through them), its edge lit; the block cells on a strip at its foot
                var level = new GlitchLevel(15, Br(PhosC, .035)) { Share = a.OfTop };
                level.Fill.Background = Frozen(Color.FromArgb(0x3A, cc.R, cc.G, cc.B));
                level.Fill.BorderBrush = Frozen(Color.FromArgb(0xD0, cc.R, cc.G, cc.B)); level.Fill.BorderThickness = new Thickness(2, 0, 0, 0);
                var footBar = new Rectangle { Height = 3, VerticalAlignment = VerticalAlignment.Bottom, Fill = Frozen(Color.FromArgb(0xB0, cc.R, cc.G, cc.B)) };
                var footCells = new Rectangle { Height = 3, VerticalAlignment = VerticalAlignment.Bottom, Fill = cellsBrush };
                level.Fill.Child = new Grid { Children = { footBar, footCells } };
                level.Root.Margin = new Thickness(6, 0, 4, 0); level.Root.VerticalAlignment = VerticalAlignment.Center;
                At(row, 1, level.Root);
                Grow(level.Fill, i, enter, 10);
                var nmc = SkillName(a, 11.5, top ? pale : Br(0xC4F5D4), amber, face, FontWeights.SemiBold);
                nmc.Margin = new Thickness(9, 0, 6, 0);
                At(row, 1, nmc);
                var d = R(a.Damage, 12, pale, face, FontWeights.Bold); d.Margin = new Thickness(0, 0, 2, 0); At(row, 2, d);
                if (c.CritCol) { var x = R(a.Crit, 11, a.Crit == "–" ? phosFaint : amber, face, FontWeights.SemiBold); x.Margin = new Thickness(0, 0, 2, 0); At(row, 3, x); }
                if (c.BackCol) { var x = R(a.Back, 11, a.Back == "–" ? phosFaint : phosDim, face, FontWeights.SemiBold); x.Margin = new Thickness(0, 0, 3, 0); At(row, c.CritCol ? 4 : 3, x); }
                Tip(row, a);
                body.Children.Add(row);
            }
            if (c.MoreLine.Length > 0)
            {
                var more = Txt("        [" + c.MoreLine + "]", 10.5, phosDim, face, FontWeights.SemiBold); more.Margin = new Thickness(0, 3, 0, 0);
                body.Children.Add(more);
            }
        }
        var foot = Foot(c, s => s, phosDim, Br(MintC), red);
        if (foot.Count > 0)
        {
            body.Children.Add(new Rectangle { Height = 1, Fill = Dashes, Margin = new Thickness(0, 7, 0, 4) });
            var fw = Words(foot, 11, face, null, phosFaint, FontWeights.SemiBold, 16);
            body.Children.Add(fw);
        }

        // the tube: dark green glass in a faint green rim, its centre glow and darker curve, scanlines over the words
        var panel = new Grid();
        panel.Children.Add(new Border { Background = Br(GroundC, .96), CornerRadius = new CornerRadius(7), BorderBrush = Br(PhosC, .32), BorderThickness = new Thickness(1) });
        panel.Children.Add(new Border { CornerRadius = new CornerRadius(7), Background = Vignette(Colors.Black, .5, 0), Margin = new Thickness(1), IsHitTestVisible = false });
        panel.Children.Add(new Border { CornerRadius = new CornerRadius(7), Background = CentreGlow, Margin = new Thickness(1), IsHitTestVisible = false });
        panel.Children.Add(body);
        panel.Children.Add(new Border { CornerRadius = new CornerRadius(7), Background = Lines(Rgb(0x000000, .22), 3, 1), Margin = new Thickness(1), IsHitTestVisible = false });
        if (enter)
        {
            // the report prints: its lines come up whole, one after another (nothing typed on), the bars fill as they land
            var lines = body.Children.OfType<UIElement>().ToList();
            foreach (var l in lines) l.Opacity = 0;
            Motion.Play(body, 220, t => { int n = (int)Math.Ceiling(t * 220 / 14); for (int i = 0; i < lines.Count; i++) lines[i].Opacity = i < n ? 1 : 0; },
                () => { foreach (var l in lines) l.Opacity = 1; });
        }
        // a phosphor glow round the tube, a dark shadow under it for a bright scene
        var glowHolder = new Border { Child = panel, Effect = new DropShadowEffect { Color = glow, BlurRadius = 9, ShadowDepth = 0, Opacity = .35 } };
        return Room(glowHolder, 13, new DropShadowEffect { Color = Colors.Black, BlurRadius = 9, ShadowDepth = 2, Opacity = .65 });
    }

    static readonly Brush CentreGlow = MakeCentre();
    static Brush MakeCentre()
    {
        var b = new RadialGradientBrush { Center = new Point(.5, .4), GradientOrigin = new Point(.5, .4), RadiusX = .75, RadiusY = .8 };
        b.GradientStops.Add(new GradientStop(Rgb(0x5CFF9A, .07), 0));
        b.GradientStops.Add(new GradientStop(Rgb(0x5CFF9A, 0), 1));
        b.Freeze();
        return b;
    }

    static readonly Brush Dashes = MakeDashes();
    static Brush MakeDashes()
    {
        var d = new DrawingGroup();
        d.Children.Add(new GeometryDrawing(Brushes.Transparent, null, new RectangleGeometry(new Rect(0, 0, 6, 1))));
        d.Children.Add(new GeometryDrawing(Br(0x5CFF9A, .45), null, new RectangleGeometry(new Rect(0, 0, 4, 1))));
        var b = new DrawingBrush(d) { TileMode = TileMode.Tile, Viewport = new Rect(0, 0, 6, 1), ViewportUnits = BrushMappingMode.Absolute, Stretch = Stretch.Fill };
        b.Freeze();
        return b;
    }
}
