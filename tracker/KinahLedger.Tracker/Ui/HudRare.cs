using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using static KinahLedger.Ui.HudKit;

namespace KinahLedger.Ui;

/// <summary>
/// The rare drop, upgraded (2026-10-26, kinah-bench/mock-hud-next f1): what dropped and whether it matters at a glance.
/// The item's tile in its grade frame, "RARE DROP · LEGEND" small, the name in the grade's colour, "sells for 28K on
/// Siel" or "no price yet", a bell (struck through when the chime is off) and a gold "watching" tag when the item is on
/// the player's watch list. The Card swaps its figures for it while the window holds the drop (about 5 s, 360 × 101);
/// the Ticker's rare toast grows to 66 px. Everything comes from the window's "rare…" values (MainWindow.HoldRareOnHud
/// and MainWindow.RareWatch.cs): "rarePrice", "rareWhere", "rareWatched", "rareChime" on top of the drop's own.
/// </summary>
static class HudRare
{
    /// <param name="Label">"RARE DROP · LEGEND"; "WATCHED DROP · RARE" or "WATCHED DROP" for a lower grade on the watch list.</param>
    /// <param name="Price">"28K", "≈28K" for the region's price, "" for no price yet.</param>
    /// <param name="Where">"on Siel", "each on Siel" for more than one.</param>
    public sealed record Drop(Pick Pick, Color Color, string Label, string Price, string Where, bool Watched, bool Chime);

    /// <summary>The drop the window holds, or null.</summary>
    public static Drop? Of(IReadOnlyDictionary<string, string> v)
    {
        if (Rare(v) is not { } p) return null;
        string V(string k) => v.TryGetValue(k, out var s) ? s : "";
        var g = p.Grade;
        var label = Glows(g) ? "RARE DROP · " + g.ToUpperInvariant() : g is "" or "Common" ? "WATCHED DROP" : "WATCHED DROP · " + g.ToUpperInvariant();
        return new Drop(p, ColorOf(p.Color, g), label, V("rarePrice"), V("rareWhere"), V("rareWatched") == "1", V("rareChime") == "1");
    }

    /// <summary>What the drop's drawing depends on (the looks' Changed gate).</summary>
    public static string Sig(Drop? d) => d == null ? "" : $"{d.Pick.Id}|{d.Pick.Count}|{d.Pick.Icon}|{d.Label}|{d.Color}|{d.Price}|{d.Where}|{d.Watched}|{d.Chime}";

    /* ---------------- the parts both looks share ---------------- */

    static readonly DrawingImage BellOn = MakeBell(true), BellOff = MakeBell(false), Eye = MakeEye();

    /// <summary>The mock's 16-unit bell: muted with a faint fill when the chime played, faint and struck through when it is off.</summary>
    static DrawingImage MakeBell(bool on)
    {
        var c = on ? Rgb(MutedC) : Rgb(FaintC);
        var pen = new Pen(Frozen(c), 1.3) { LineJoin = PenLineJoin.Round, StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
        pen.Freeze();
        var g = new DrawingGroup();
        g.Children.Add(new GeometryDrawing(Brushes.Transparent, null, new RectangleGeometry(new Rect(0, 0, 16, 16))));
        g.Children.Add(new GeometryDrawing(on ? B(MutedC, .14) : null, pen,
            Geometry.Parse("M8,2.1 C5.7,2.1 4.2,3.9 4.2,6.1 L4.2,8.6 L2.8,10.7 L2.8,11.5 L13.2,11.5 L13.2,10.7 L11.8,8.6 L11.8,6.1 C11.8,3.9 10.3,2.1 8,2.1 Z")));
        g.Children.Add(new GeometryDrawing(null, pen, Geometry.Parse("M6.5,12.9 A1.6,1.6 0 0 0 9.5,12.9")));
        if (!on)
        {
            // a dark gap under the stroke, so the line reads as crossing the bell out
            g.Children.Add(new GeometryDrawing(null, new Pen(B(PanelC), 3.2) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round }, Geometry.Parse("M2,2 L14,14")));
            g.Children.Add(new GeometryDrawing(null, new Pen(Frozen(c), 1.4) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round }, Geometry.Parse("M2.4,2.4 L13.6,13.6")));
        }
        var img = new DrawingImage(g); img.Freeze();
        return img;
    }

    static DrawingImage MakeEye()
    {
        var gold = B(GoldC);
        var g = new DrawingGroup();
        g.Children.Add(new GeometryDrawing(Brushes.Transparent, null, new RectangleGeometry(new Rect(0, 0, 16, 16))));
        g.Children.Add(new GeometryDrawing(null, new Pen(gold, 1.5) { LineJoin = PenLineJoin.Round },
            Geometry.Parse("M1.4,8 C1.4,8 4,3.6 8,3.6 C12,3.6 14.6,8 14.6,8 C14.6,8 12,12.4 8,12.4 C4,12.4 1.4,8 1.4,8 Z")));
        g.Children.Add(new GeometryDrawing(gold, null, new EllipseGeometry(new Point(8, 8), 2.1, 2.1)));
        var img = new DrawingImage(g); img.Freeze();
        return img;
    }

    public static Image Bell(bool on) => new() { Source = on ? BellOn : BellOff, Width = 15, Height = 15, VerticalAlignment = VerticalAlignment.Center };

    /// <summary>The gold "watching" tag: an eye and the word, in a 16 px pill.</summary>
    public static Border WatchTag() => new()
    {
        Height = 16, Padding = new Thickness(5, 0, 6, 0), CornerRadius = new CornerRadius(8), BorderThickness = new Thickness(1),
        Background = B(GoldC, .10), BorderBrush = B(GoldC, .40), VerticalAlignment = VerticalAlignment.Center,
        Child = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Children = { new Image { Source = Eye, Width = 11, Height = 11, Margin = new Thickness(0, 0, 4, 0), VerticalAlignment = VerticalAlignment.Center }, T("watching", 10.5, Gold, weight: FontWeights.SemiBold) },
        },
    };

    static TextBlock Label(Drop d) => T(d.Label, 10.5, Frozen(Color.FromArgb(0xD9, d.Color.R, d.Color.G, d.Color.B)), weight: FontWeights.Bold);

    static TextBlock NameLine(Drop d)
    {
        var t = new TextBlock { TextTrimming = TextTrimming.CharacterEllipsis, LineHeight = 18, LineStackingStrategy = LineStackingStrategy.BlockLineHeight };
        t.Inlines.Add(R(d.Pick.Name, 15, Frozen(d.Color), weight: FontWeights.SemiBold));
        if (d.Pick.Count.Length > 0) t.Inlines.Add(R(" " + d.Pick.Count, 12, Muted, mono: true));
        return t;
    }

    /// <summary>"sells for 28K on Siel" (the figure gold, as the board's price in the window's rows), or "no price yet".</summary>
    static TextBlock PriceLine(Drop d)
    {
        var t = new TextBlock { TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center, LineHeight = 15, LineStackingStrategy = LineStackingStrategy.BlockLineHeight };
        if (d.Price.Length == 0) { t.Inlines.Add(R("no price yet", 12, Faint)); return t; }
        t.Inlines.Add(R("sells for ", 12, Muted));
        t.Inlines.Add(R(d.Price, 12, Gold, mono: true));
        if (d.Where.Length > 0) t.Inlines.Add(R(" " + d.Where, 12, Muted));
        return t;
    }

    /* ---------------- the Card ---------------- */

    /// <summary>
    /// The Card's parts for the drop: in the head the hour's rate and the bell (over the zone and the newest pickup), and
    /// the body (over the figures and the tiles). <see cref="Swap"/> holds what they stand in for.
    /// </summary>
    public sealed class CardParts
    {
        public Grid Head { get; } = new() { Visibility = Visibility.Collapsed };
        public Border Body { get; } = new() { Visibility = Visibility.Collapsed };
        public UIElement[] Swap { get; set; } = Array.Empty<UIElement>();
        readonly TextBlock _rate = new() { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0) };
        readonly Image _bell = Bell(false);

        public CardParts()
        {
            Head.ColumnDefinitions.Add(new ColumnDefinition());
            Head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            Head.Children.Add(_rate);
            Grid.SetColumn(_bell, 1); Head.Children.Add(_bell);
        }

        public void Show(Drop? d)
        {
            bool on = d != null;
            Head.Visibility = Body.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
            foreach (var e in Swap) e.Visibility = on ? Visibility.Collapsed : Visibility.Visible;
            if (d == null) { Body.Child = null; return; }
            _bell.Source = d.Chime ? BellOn : BellOff;
            var col = new StackPanel { Margin = new Thickness(12, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
            var l1 = new StackPanel { Orientation = Orientation.Horizontal, Height = 16, Children = { Label(d) } };
            if (d.Watched) { var w = WatchTag(); w.Margin = new Thickness(8, 0, 0, 0); l1.Children.Add(w); }
            var name = NameLine(d); name.Margin = new Thickness(0, 1, 0, 0);
            var price = PriceLine(d); price.Margin = new Thickness(0, 1, 0, 0);
            col.Children.Add(l1); col.Children.Add(name); col.Children.Add(price);
            var g = new Grid();
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            g.ColumnDefinitions.Add(new ColumnDefinition());
            g.Children.Add(Tile(d.Pick, 40));
            Grid.SetColumn(col, 1); g.Children.Add(col);
            Body.Child = g;
        }

        /// <summary>The hour's rate in the head while the drop shows ("1.27M /h"), as the Ticker's pill says it.</summary>
        public void Rate(string rate)
        {
            _rate.Inlines.Clear();
            if (rate.Length == 0) return;
            _rate.Inlines.Add(R(rate, 13, Gold, mono: true));
            _rate.Inlines.Add(R(" /h", 12, Faint));
        }
    }

    /* ---------------- the Ticker ---------------- */

    /// <summary>The Ticker's rare toast: 270 × 66, framed in the grade's colour with a glow; label and bell, the name, the price and the tag.</summary>
    public static Border TickerToast(Drop d, double alpha)
    {
        var c = d.Color;
        var l1 = new Grid();
        l1.ColumnDefinitions.Add(new ColumnDefinition()); l1.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        l1.Children.Add(Label(d));
        var bell = Bell(d.Chime); Grid.SetColumn(bell, 1); l1.Children.Add(bell);
        var l3 = new Grid { Margin = new Thickness(0, 2, 0, 0) };
        l3.ColumnDefinitions.Add(new ColumnDefinition()); l3.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        l3.Children.Add(PriceLine(d));
        if (d.Watched) { var w = WatchTag(); w.Margin = new Thickness(6, 0, 0, 0); Grid.SetColumn(w, 1); l3.Children.Add(w); }
        var name = NameLine(d); name.Margin = new Thickness(0, 2, 0, 0);
        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 0, 0), Children = { l1, name, l3 } };
        var row = new Grid();
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); row.ColumnDefinitions.Add(new ColumnDefinition());
        row.Children.Add(Tile(d.Pick, 40));
        Grid.SetColumn(text, 1); row.Children.Add(text);
        var wash = new RadialGradientBrush { Center = new Point(0, .5), GradientOrigin = new Point(0, .5), RadiusX = .9, RadiusY = 1.6 };
        wash.GradientStops.Add(new GradientStop(Color.FromArgb((byte)Math.Round(.32 * 255), c.R, c.G, c.B), 0));
        wash.GradientStops.Add(new GradientStop(Rgb(0x0C1228, .95 * Math.Min(1, alpha)), .7));
        wash.Freeze();
        var body = new Border
        {
            Width = 270, Height = 66, Padding = new Thickness(8, 0, 10, 0), CornerRadius = new CornerRadius(6), BorderThickness = new Thickness(3, 1, 1, 1),
            BorderBrush = Frozen(c), Background = wash, Child = row,
        };
        // the glow sits under the toast in its own ring, so the words stay sharp
        return new Border { Child = new Grid { Children = { Glow(c, 6, 16, .5), body } } };
    }
}
