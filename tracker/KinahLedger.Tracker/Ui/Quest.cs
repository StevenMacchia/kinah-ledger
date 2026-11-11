using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using Path = System.Windows.Shapes.Path;

namespace KinahLedger.Ui;

/// <summary>
/// The Quest look's kit (Steven, 2026-11-07, docs/ui/mockups/directions-2026-11-07, Direction 3): the pieces every page is
/// built from. A line icon at 16 px from the theme's geometries, an icon box tinted in one of the roles (gold the chase,
/// mint what pays, sky a place, lilac a milestone, coral a miss, lime gathering), a ring meter, a chase bar, a row with
/// the icon, a bold name, the small print and a figure on the right (a thin bar under it when it is a share), a chip with
/// a big figure, and a card head with an icon, a title, a note on the right and a chevron when the card opens. Every brush
/// is a skin key, so the three looks each paint it.
/// </summary>
public partial class MainWindow
{
    /* ---------------- icons ---------------- */

    /// <summary>A theme geometry by its short name ("Sword" is IcSword).</summary>
    static Geometry IcGeo(string name) => (Geometry)Application.Current.FindResource("Ic" + name);

    /// <summary>A line icon: the 24-unit path drawn at <paramref name="size"/>, stroked in a skin brush.</summary>
    static Path QIcon(string name, double size = 16, string brush = "Muted", double stroke = 1.7)
    {
        var p = new Path
        {
            Data = IcGeo(name), Width = size, Height = size, Stretch = Stretch.Uniform, StrokeThickness = stroke,
            StrokeLineJoin = PenLineJoin.Round, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round,
            VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center,
        };
        p.SetResourceReference(Shape.StrokeProperty, brush);
        return p;
    }

    /// <summary>The tint an icon box of a role sits on; "Dim" is the surface's second tone with a faint icon.</summary>
    static string QTintOf(string tone) => tone switch
    {
        "Gold" => "GoldIcTint", "Mint" => "MintTint", "Sky" => "SkyTint", "Coral" => "CoralTint", "Lilac" => "LilacTint", "Lime" => "LimeTint", _ => "Card2",
    };

    /// <summary>
    /// An icon in a box: 30 px, rounded (a circle in a row). A concept with game art (Ui/GameArt.cs: Kinah, a fight, a run, a
    /// quest...) shows the picture on the surface's second tone, as the site's icon chips; an interface mark (a check, a
    /// plus) stays a line in the role's colour on its tint.
    /// </summary>
    static Border QIconBox(string icon, string tone, double size = 30, bool round = false, double iconSize = 16)
    {
        var b = new Border { Width = size, Height = size, CornerRadius = new CornerRadius(round ? size / 2 : Math.Round(size / 3)), VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
        if (GameArt.For(icon) is { } art)
        {
            b.SetResourceReference(Border.BackgroundProperty, "Card2");
            b.Child = GameArt.Element(art, Math.Round(Math.Min(size - 4, iconSize * 1.55)));
            return b;
        }
        b.SetResourceReference(Border.BackgroundProperty, QTintOf(tone));
        b.Child = QIcon(icon, iconSize, tone == "Dim" ? "Faint" : tone, 2);
        return b;
    }

    /// <summary>A concept's game art at <paramref name="size"/>, or the line icon when the name is an interface mark.</summary>
    static FrameworkElement QGlyph(string name, double size, string brush, double stroke = 1.7) =>
        GameArt.For(name) is { } art ? GameArt.Element(art, Math.Round(size * 1.25)) : QIcon(name, size, brush, stroke);

    /// <summary>
    /// An item's own icon from the cache, in a rounded (or round) box on the window's background; its initial while the
    /// icon loads or when the game has none; a Rare or better item wears a thin ring in its grade colour.
    /// </summary>
    FrameworkElement QItemIcon(string name, long id, double size = 30, bool round = true, string grade = "")
    {
        var slot = Slot(id);
        var box = new Border { Width = size, Height = size, CornerRadius = new CornerRadius(round ? size / 2 : Math.Round(size * 0.3)), VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center, ClipToBounds = true };
        box.SetResourceReference(Border.BackgroundProperty, "Bg");
        if (grade is not ("" or "Common")) { box.BorderThickness = new Thickness(2); box.SetResourceReference(Border.BorderBrushProperty, "Grade" + grade); }
        var pic = new Image { Source = slot.Image, Stretch = Stretch.UniformToFill };
        RenderOptions.SetBitmapScalingMode(pic, BitmapScalingMode.HighQuality);
        var letter = KindGlyph(name, id, size);
        letter.Visibility = slot.Image == null ? Visibility.Visible : Visibility.Collapsed;
        if (slot.Image == null && !ReferenceEquals(slot, IconCache.None))
        {
            System.ComponentModel.PropertyChangedEventHandler? on = null;
            on = (_, _) => { if (slot.Image == null) return; pic.Source = slot.Image; letter.Visibility = Visibility.Collapsed; slot.PropertyChanged -= on; };
            slot.PropertyChanged += on;
        }
        var face = new Grid(); face.Children.Add(letter); face.Children.Add(pic);
        box.Child = face;
        return box;
    }

    /// <summary>
    /// What an item's slot shows when it has no picture (a name the list doesn't know, or the icon still on its way): the
    /// game's art for its kind (Ui/GameArt.cs): a character card for gear, Kinah's coin for a currency, ore for a gathered
    /// material, the loot chest for anything else. Until 2026-11-08 it was the name's first letter, which read as a broken
    /// picture (the audit's "letter tiles"); until 2026-11-10 a drawn line icon.
    /// </summary>
    FrameworkElement KindGlyph(string name, long id, double size)
    {
        var info = _items.Get(id);
        var kind = _items.IsGear(id) || name.EndsWith("(bound)", StringComparison.OrdinalIgnoreCase) ? "Shield"
            : info?.Category is "Currency" or "Special Points" ? "Coin"
            : info?.Category == "Gathering Material" || Data.GatheringCatalog.Find(name) != null ? "Leaf"
            : "Sack";
        // faint, so a row of them reads as pictures still to come, not as one item many times (review 2026-11-10)
        var e = GameArt.Element(GameArt.For(kind)!, Math.Round(size * 0.6)); e.Opacity = 0.35;
        return e;
    }

    /// <summary>
    /// A page's hero lit in the colour of what it is about (v2, 2026-11-10, as the site's heroes, kinah-ledger-site
    /// tools/seo/hero-art.js): a glow in the item's grade colour from the top-left corner, under the card's content and
    /// clipped to its corners. <paramref name="item"/> is the session's best drop, the recipe that pays most or a place's
    /// best loot; none gives Kinah's gold. The item's picture faint behind the ring was tried and cut in review: a smudge
    /// on bright drops, unreadable on dark gear. Safe to call on every fill: the layer is made once, only its colour changes.
    /// </summary>
    void HeroArt(Border hero, string? item)
    {
        if (hero.Child is not Grid { Tag: "hero-art" } host)
        {
            var inner = hero.Child as FrameworkElement;
            var pad = hero.Padding;
            hero.Child = null;
            hero.Padding = new Thickness(0);
            host = new Grid { Tag = "hero-art", ClipToBounds = true };
            host.Children.Add(new Border { IsHitTestVisible = false, Tag = "glow" });
            if (inner != null) { inner.Margin = new Thickness(inner.Margin.Left + pad.Left, inner.Margin.Top + pad.Top, inner.Margin.Right + pad.Right, inner.Margin.Bottom + pad.Bottom); host.Children.Add(inner); }
            // the card's rounded corners clip the layers (a Border doesn't clip its child)
            host.SizeChanged += (_, e) => host.Clip = new RectangleGeometry(new Rect(e.NewSize), Math.Max(0, hero.CornerRadius.TopLeft - 1), Math.Max(0, hero.CornerRadius.TopLeft - 1));
            hero.Child = host;
        }
        var glowBox = (Border)host.Children[0];
        Color c;
        if (item is { Length: > 0 } && _items.IdOf(item) is long id && id > 0)
        {
            var grade = GradeOf(item);
            c = grade is "" or "Common" ? Color.FromRgb(0xC9, 0xCE, 0xDB) : TryFindResource("Grade" + grade) is SolidColorBrush gb ? gb.Color : Color.FromRgb(0xF5, 0xB9, 0x42);
        }
        else c = TryFindResource("Gold") is SolidColorBrush g ? g.Color : Color.FromRgb(0xF5, 0xB9, 0x42);
        var glow = new RadialGradientBrush { Center = new Point(0.08, 0), GradientOrigin = new Point(0.08, 0), RadiusX = 0.6, RadiusY = 1.2 };
        glow.GradientStops.Add(new GradientStop(Color.FromArgb(0x30, c.R, c.G, c.B), 0));
        glow.GradientStops.Add(new GradientStop(Color.FromArgb(0, c.R, c.G, c.B), 1));
        glow.Freeze();
        glowBox.Background = glow;
    }

    /// <summary>Kinah's coin: the game's own Kinah art (Art/kinah.png), the game having no item icon for its money.</summary>
    static FrameworkElement QCoin(double size = 30) => GameArt.Element("kinah", size);

    /* ---------------- text, rows, chips, heads ---------------- */

    static TextBlock QT(string text, double size, FontWeight weight, string brush, bool trim = false)
    {
        var t = new TextBlock { Text = text, FontSize = size, FontWeight = weight, VerticalAlignment = VerticalAlignment.Center };
        if (trim) t.TextTrimming = TextTrimming.CharacterEllipsis;
        t.SetResourceReference(TextBlock.ForegroundProperty, brush);
        return t;
    }

    /// <summary>A skin brush's colour, for a glow the effect system needs as a Color (it keeps the colour of the look it was built in).</summary>
    static Color QColor(string key) => (Application.Current.TryFindResource(key) as SolidColorBrush)?.Color ?? Colors.White;

    /// <summary>A soft glow in a role's colour, so a bar or an arc lifts off the surface (Steven, 2026-11-07: "make the progress bars pop").</summary>
    static System.Windows.Media.Effects.DropShadowEffect QGlow(string tone, double blur = 10, double opacity = 0.55)
        => new() { Color = QColor(tone), BlurRadius = blur, ShadowDepth = 0, Opacity = Skins.IsDark(Skins.Current) ? opacity : opacity * 0.3 };

    /// <summary>A bar: the share filled in the role's colour with a light edge and a glow, on the window's background.</summary>
    static Grid QBar(double share, string tone, double height = 7, string? key = null)
    {
        var g = new Grid { Height = height };
        var track = new Border { CornerRadius = new CornerRadius(height / 2) };
        track.SetResourceReference(Border.BackgroundProperty, "Bg");
        g.Children.Add(track);
        var fill = new Border { CornerRadius = new CornerRadius(height / 2), HorizontalAlignment = HorizontalAlignment.Left, Effect = QGlow(tone, 8, 0.6) };
        fill.SetResourceReference(Border.BackgroundProperty, tone);
        fill.Child = new Border { CornerRadius = new CornerRadius(height / 2), Margin = new Thickness(1, 1, 1, 0),
            Background = new LinearGradientBrush(Color.FromArgb(0x55, 255, 255, 255), Color.FromArgb(0, 255, 255, 255), 90) };
        g.Children.Add(fill);
        void Set(double s) => fill.Width = Math.Max(0, g.ActualWidth * Math.Clamp(s, 0, 1));
        // with a key the fill slides from the share last shown under it (a goal's bar after a pickup, its row drawn again)
        if (key == null) g.SizeChanged += (_, _) => Set(share);
        else g.SizeChanged += (_, _) => Motion.Slide(fill, key, share, Set);
        return g;
    }

    /// <summary>An inventory slot: the item's icon at 42 px with its count in a badge at the corner (the old LOOT grid's grammar, kept for the Compact bag strip).</summary>
    /// <param name="roll">The item's key and count: the badge rolls up from the count last shown under it (Motion.Number).</param>
    FrameworkElement QSlot(FrameworkElement icon, string badge, (string Key, long Count)? roll = null)
    {
        var g = new Grid { Width = 46, Height = 46, Margin = new Thickness(0, 0, 6, 6), Cursor = Cursors.Hand, Background = Brushes.Transparent };
        icon.HorizontalAlignment = HorizontalAlignment.Left; icon.VerticalAlignment = VerticalAlignment.Top;
        g.Children.Add(icon);
        var b = new Border { HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom, MinWidth = 18, Height = 16, CornerRadius = new CornerRadius(8), Padding = new Thickness(4, 0, 4, 0), BorderThickness = new Thickness(1) };
        b.SetResourceReference(Border.BackgroundProperty, "Bg"); b.SetResourceReference(Border.BorderBrushProperty, "CardLineHover");
        var t = QT(badge, 10.5, FontWeights.Bold, "Ink");
        if (roll is { } r) Motion.Number(t, r.Key, r.Count, v => CountBadge((long)Math.Round(v)), -1);
        b.Child = t;
        g.Children.Add(b);
        return g;
    }

    /// <summary>
    /// One row: a 30 px icon, the name in bold over the small print, a figure on the right in the tone given, and under
    /// them a thin bar when <paramref name="bar"/> is a share. Lit a little under the mouse; the whole row is the click.
    /// </summary>
    /// <param name="barKey">Who the bar is (a goal, a quest): it slides from the share last shown under it (Motion.Slide).</param>
    static Border QRow(FrameworkElement icon, string title, string sub, string value = "", string valueTone = "Ink", double? bar = null, string barTone = "Lilac", bool rule = true, bool inline = false, string? barKey = null)
    {
        var g = new Grid();
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(28) });
        g.ColumnDefinitions.Add(new ColumnDefinition());
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        icon.VerticalAlignment = VerticalAlignment.Center;
        g.Children.Add(icon);
        FrameworkElement text;
        if (inline)
        {
            // one line: the name in bold, the small print after it in the muted tone
            var t = QT(title, 13, FontWeights.SemiBold, "Ink", trim: true); t.Margin = new Thickness(10, 0, 8, 0);
            if (sub.Length > 0) { var r = new System.Windows.Documents.Run("  " + sub) { FontSize = 11, FontWeight = FontWeights.Medium }; r.SetResourceReference(System.Windows.Documents.TextElement.ForegroundProperty, "Muted"); t.Inlines.Add(r); }
            text = t;
        }
        else
        {
            var st = new StackPanel { Margin = new Thickness(10, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center };
            st.Children.Add(QT(title, 13, FontWeights.SemiBold, "Ink", trim: true));
            if (sub.Length > 0) st.Children.Add(QT(sub, 11, FontWeights.Medium, "Muted", trim: true));
            text = st;
        }
        Grid.SetColumn(text, 1); g.Children.Add(text);
        if (value.Length > 0)
        {
            var v = QT(value, 13, FontWeights.SemiBold, valueTone); v.HorizontalAlignment = HorizontalAlignment.Right;
            Grid.SetColumn(v, 2); g.Children.Add(v);
        }
        if (bar is { } share)
        {
            var b = QBar(share, barTone, 7, barKey); b.Margin = new Thickness(10, inline ? 3 : 5, 0, 0);
            Grid.SetRow(b, 1); Grid.SetColumn(b, 1); Grid.SetColumnSpan(b, 2); g.Children.Add(b);
        }
        var row = new Border { Child = g, Padding = new Thickness(6, inline ? 4 : 7, 6, inline ? 4 : 7), Margin = new Thickness(-6, 0, -6, 0), CornerRadius = new CornerRadius(9), Background = Brushes.Transparent, Cursor = Cursors.Hand };
        if (rule) { row.BorderThickness = new Thickness(0, 1, 0, 0); row.SetResourceReference(Border.BorderBrushProperty, "Line"); }
        row.MouseEnter += (_, _) => row.SetResourceReference(Border.BackgroundProperty, "HoverTint");
        row.MouseLeave += (_, _) => row.Background = Brushes.Transparent;
        return row;
    }

    /// <summary>
    /// A bag tile (Full view's inventory feel, with names on): the item's icon, the count big, the name, and its worth in gold
    /// (or "no price yet", "bound", faint). Four to a row at 149 px; lit under the mouse.
    /// </summary>
    /// <param name="roll">The item's key and count: the big count rolls up from the one last shown under it (Motion.Number).</param>
    static Border QTile(FrameworkElement icon, string big, string name, string value, string valueTone = "Gold", double width = 149, (string Key, long Count)? roll = null)
    {
        var sp = new StackPanel();
        icon.HorizontalAlignment = HorizontalAlignment.Left;
        sp.Children.Add(icon);
        var b = QT(big, 16, FontWeights.Bold, "Ink", trim: true); b.Margin = new Thickness(0, 5, 0, 0); b.LineHeight = 18; b.LineStackingStrategy = LineStackingStrategy.BlockLineHeight;
        if (roll is { } r) Motion.Number(b, r.Key, r.Count, v => Math.Round(v).ToString("N0"), -1);
        sp.Children.Add(b);
        sp.Children.Add(QT(name, 11, FontWeights.Medium, "Muted", trim: true));
        if (value.Length > 0) { var v = QT(value, 11, FontWeights.SemiBold, valueTone, trim: true); sp.Children.Add(v); }
        var tile = new Border { Width = width, Padding = new Thickness(10, 9, 10, 9), CornerRadius = new CornerRadius(12), Child = sp, Cursor = Cursors.Hand };
        tile.SetResourceReference(Border.BackgroundProperty, "Card2");
        tile.MouseEnter += (_, _) => tile.SetResourceReference(Border.BackgroundProperty, "ItemSel");
        tile.MouseLeave += (_, _) => tile.SetResourceReference(Border.BackgroundProperty, "Card2");
        return tile;
    }

    /// <summary>An odds badge: the item's icon, "1 in 4" big, the name small, centred in a small rounded box.</summary>
    static Border QBadge(FrameworkElement icon, string odds, string name)
    {
        var sp = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center };
        icon.HorizontalAlignment = HorizontalAlignment.Center;
        sp.Children.Add(icon);
        var b = QT(odds, 12, FontWeights.Bold, "Ink"); b.HorizontalAlignment = HorizontalAlignment.Center; b.Margin = new Thickness(0, 4, 0, 0);
        sp.Children.Add(b);
        var n = QT(name, 10.5, FontWeights.Medium, "Muted", trim: true); n.HorizontalAlignment = HorizontalAlignment.Center; n.TextAlignment = TextAlignment.Center;
        sp.Children.Add(n);
        var badge = new Border { Padding = new Thickness(6, 8, 6, 8), CornerRadius = new CornerRadius(12), Child = sp, Cursor = Cursors.Hand };
        badge.SetResourceReference(Border.BackgroundProperty, "Card2");
        badge.MouseEnter += (_, _) => badge.SetResourceReference(Border.BackgroundProperty, "ItemSel");
        badge.MouseLeave += (_, _) => badge.SetResourceReference(Border.BackgroundProperty, "Card2");
        return badge;
    }

    /// <summary>A centred faint line under a list: "7 more kinds have no Market price yet".</summary>
    static TextBlock QMore(string text)
    {
        var t = QT(text, 11, FontWeights.SemiBold, "Muted"); t.HorizontalAlignment = HorizontalAlignment.Center; t.TextAlignment = TextAlignment.Center; t.Margin = new Thickness(0, 6, 0, 0); t.TextWrapping = TextWrapping.Wrap;
        return t;
    }

    /// <summary>Fills a chip: the icon, the big figure over its label.</summary>
    /// <paramref name="roll"/>: the big figure's key and value, so it rolls from the last one shown under that key (Motion.Number;
    /// money tints green as it rises, a count of kills doesn't).
    static void QChipFill(Border chip, FrameworkElement icon, string big, string small, (string Key, double Value, bool Money)? roll = null)
    {
        var g = new Grid();
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); g.ColumnDefinitions.Add(new ColumnDefinition());
        icon.VerticalAlignment = VerticalAlignment.Center; g.Children.Add(icon);
        var text = new StackPanel { Margin = new Thickness(10, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        var b = QT(big, 15, FontWeights.SemiBold, "Ink", trim: true); b.LineHeight = 17; b.LineStackingStrategy = LineStackingStrategy.BlockLineHeight;
        if (roll is { } r) Motion.Number(b, r.Key, r.Value, r.Money ? v => Amounts.Short(v) : v => Math.Round(v).ToString("N0"), r.Money ? 0 : -1);
        var sm = QT(small, 11, FontWeights.Medium, "Muted", trim: true); sm.LineHeight = 14; sm.LineStackingStrategy = LineStackingStrategy.BlockLineHeight; sm.Margin = new Thickness(0, 1, 0, 0);
        text.Children.Add(b); text.Children.Add(sm);
        Grid.SetColumn(text, 1); g.Children.Add(text);
        chip.Child = g;
    }

    /// <summary>A card's head: the icon (game art for a concept), the title, a note on the right in the muted tone, and the chevron when the card opens on a click.</summary>
    static Grid QHead(string icon, string title, string right = "", Path? chev = null)
    {
        var g = new Grid { Margin = new Thickness(0, 0, 0, 2), Background = Brushes.Transparent };
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        g.ColumnDefinitions.Add(new ColumnDefinition()); g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var ic = QGlyph(icon, 16, "Lilac", 2); ic.Margin = new Thickness(0, 0, 8, 0); g.Children.Add(ic);
        var t = QT(title, 12, FontWeights.SemiBold, "Ink"); Grid.SetColumn(t, 1); g.Children.Add(t);
        var r = QT(right, 11, FontWeights.Medium, "Muted", trim: true); r.HorizontalAlignment = HorizontalAlignment.Right; r.Margin = new Thickness(10, 0, 0, 0); r.Tag = "right";
        Grid.SetColumn(r, 2); g.Children.Add(r);
        if (chev != null) { Grid.SetColumn(chev, 3); g.Children.Add(chev); g.Cursor = Cursors.Hand; }
        return g;
    }

    /// <summary>The ring meter: the share round a dark track, clockwise from the top, with a big figure and a small word inside.</summary>
    static Grid QRing(double size, double stroke, double share, string tone, string big, string small, string track = "Line")
    {
        var g = new Grid { Width = size, Height = size, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        var bk = new Ellipse { Width = size - stroke, Height = size - stroke, StrokeThickness = stroke, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        bk.SetResourceReference(Shape.StrokeProperty, track);
        g.Children.Add(bk);
        double s = Math.Clamp(share, 0, 1);
        if (s >= 0.999)
        {
            var full = new Ellipse { Width = size - stroke, Height = size - stroke, StrokeThickness = stroke, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Effect = QGlow(tone, 10, 0.5) };
            full.SetResourceReference(Shape.StrokeProperty, tone); g.Children.Add(full);
        }
        else if (s > 0.004)
        {
            double c = size / 2, r = (size - stroke) / 2, a = 2 * Math.PI * s;
            var fig = new PathFigure { StartPoint = new Point(c, c - r), IsClosed = false, IsFilled = false };
            fig.Segments.Add(new ArcSegment(new Point(c + r * Math.Sin(a), c - r * Math.Cos(a)), new Size(r, r), 0, a > Math.PI, SweepDirection.Clockwise, true));
            var geo = new PathGeometry(); geo.Figures.Add(fig);
            var arc = new Path { Data = geo, StrokeThickness = stroke, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round, Width = size, Height = size, Effect = QGlow(tone, 10, 0.5) };
            arc.SetResourceReference(Shape.StrokeProperty, tone);
            g.Children.Add(arc);
        }
        var text = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        var b = QT(big, size >= 60 ? (big.Length > 5 ? 12.5 : 17) : 10, FontWeights.Bold, "Ink"); b.HorizontalAlignment = HorizontalAlignment.Center; b.LineHeight = size >= 60 ? 18 : 11; b.LineStackingStrategy = LineStackingStrategy.BlockLineHeight;
        text.Children.Add(b);
        if (small.Length > 0) { var sm = QT(small.ToUpperInvariant(), 9, FontWeights.SemiBold, "Muted"); sm.HorizontalAlignment = HorizontalAlignment.Center; sm.Margin = new Thickness(0, 1, 0, 0); text.Children.Add(sm); }
        g.Children.Add(text);
        return g;
    }

    /* ---------------- what is open ---------------- */

    /// <summary>The Quest cards the player opened (the hero's chart, a chip's panel, Next up's rest, the zone board), by key.</summary>
    readonly Dictionary<string, bool> _qOpen = new();
    bool QOpen(string key) => _qOpen.GetValueOrDefault(key);

    /// <summary>Turns a chevron to point down when its card is open, and gold.</summary>
    static void QSetChev(Path chev, bool open)
    {
        chev.RenderTransform = new RotateTransform(open ? 90 : 0);
        chev.SetResourceReference(Shape.StrokeProperty, open ? "Gold" : "Faint");
    }

    /// <summary>The percentage the chase bar and the ring show: this hour against your best here, capped at the full bar.</summary>
    static double QShare(double now, double best) => best <= 0 ? 0 : Math.Clamp(now / best, 0, 1);
}
