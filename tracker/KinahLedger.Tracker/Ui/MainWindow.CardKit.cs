using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;

namespace KinahLedger.Ui;

/// <summary>
/// The pieces every card in the tracker is built from (Steven, 2026-10-22, style A "game tooltip"): a dark tooltip body,
/// a header band in the item's grade colour with its icon framed like an inventory slot, gold section headings with a
/// fading rule, inventory slots with count badges, key-value lines, a success/fail bar and a tinted footer. Drawn with
/// fixed colours (like the game's own tooltips), so a card looks the same in every look of the window.
/// </summary>
public partial class MainWindow
{
    static readonly Color CkInk = Color.FromRgb(0xEE, 0xEC, 0xF6), CkMuted = Color.FromRgb(0xB3, 0xB7, 0xD3), CkFaint = Color.FromRgb(0x7D, 0x84, 0xA8),
        CkGold = Color.FromRgb(0xF8, 0xD5, 0x98), CkGoldRule = Color.FromRgb(0xE9, 0xB4, 0x5A), CkGood = Color.FromRgb(0x62, 0xD9, 0x8B), CkBad = Color.FromRgb(0xFF, 0x8A, 0x7A),
        CkWarn = Color.FromRgb(0xF2, 0xA3, 0x5E), CkSlot = Color.FromRgb(0x0B, 0x0E, 0x1E), CkSlotLine = Color.FromRgb(0x2A, 0x31, 0x54), CkFrame = Color.FromRgb(0x4A, 0x52, 0x77);

    static SolidColorBrush Ck(Color c, byte a = 255) => new(Color.FromArgb(a, c.R, c.G, c.B));

    string GradeOf(string item) => _items.Get(_items.IdOf(item))?.Grade ?? "";

    static TextBlock CkText(string text, Color color, double size = 13, FontWeight? weight = null, bool mono = false, bool display = false)
    {
        var t = new TextBlock { Text = text, FontSize = size, Foreground = Ck(color), TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center };
        if (weight is { } w) t.FontWeight = w;
        if (mono) t.FontFamily = new FontFamily("Cascadia Mono, Consolas");
        else if (display) t.FontFamily = new FontFamily("Segoe UI Variable Display, Segoe UI");
        return t;
    }

    /// <summary>The card itself: the tooltip body, its content stacked inside.</summary>
    static (Border Card, StackPanel Body) CkShell(double width)
    {
        var body = new StackPanel { Width = width };
        var card = new Border
        {
            Child = body, CornerRadius = new CornerRadius(6), BorderThickness = new Thickness(1), BorderBrush = Ck(Color.FromRgb(0x3C, 0x44, 0x66)), Margin = new Thickness(10),
            Background = new LinearGradientBrush(Color.FromRgb(0x15, 0x1A, 0x2C), Color.FromRgb(0x0C, 0x0F, 0x1C), 90),
            Effect = new DropShadowEffect { BlurRadius = 26, ShadowDepth = 5, Opacity = 0.6 }, ClipToBounds = false,
        };
        return (card, body);
    }

    /// <summary>An item's icon in a slot frame, or its initial while the icon loads; a count badge at the bottom right.</summary>
    FrameworkElement CkIcon(string item, double size, Color frame, string badge = "", bool glow = false)
    {
        var slot = Slot(_items.IdOf(item));
        var box = new Border
        {
            Width = size, Height = size, CornerRadius = new CornerRadius(5), BorderThickness = new Thickness(glow ? 2 : 1), BorderBrush = Ck(frame, glow ? (byte)255 : (byte)200),
            Background = glow ? new RadialGradientBrush(Color.FromArgb(0x55, frame.R, frame.G, frame.B), CkSlot) { Center = new Point(0.5, 0.4), GradientOrigin = new Point(0.5, 0.4) } : Ck(Color.FromRgb(0x15, 0x1A, 0x30)),
            Effect = glow ? new DropShadowEffect { Color = frame, BlurRadius = 16, ShadowDepth = 0, Opacity = 0.45 } : null,
        };
        // the icon, or the item's initial only while the icon is still downloading: a card drawn before the file is on
        // disk fills in when it lands (every item in the game has one; a letter that stayed read as a missing picture)
        var pic = new Image { Source = slot.Image, Stretch = Stretch.Uniform, Margin = new Thickness(2) };
        var letter = new TextBlock { Text = InitialOf(item), FontSize = size * 0.38, FontWeight = FontWeights.Bold, Foreground = Ck(CkMuted), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
            Visibility = slot.Image == null ? Visibility.Visible : Visibility.Collapsed };
        if (slot.Image == null && !ReferenceEquals(slot, IconCache.None))
        {
            System.ComponentModel.PropertyChangedEventHandler? on = null;
            on = (_, _) => { if (slot.Image == null) return; pic.Source = slot.Image; letter.Visibility = Visibility.Collapsed; slot.PropertyChanged -= on; };
            slot.PropertyChanged += on;
        }
        var face = new Grid(); face.Children.Add(letter); face.Children.Add(pic);
        box.Child = face;
        if (badge.Length == 0) return box;
        var g = new Grid { Width = size + 6, Height = size + 6 };
        box.HorizontalAlignment = HorizontalAlignment.Left; box.VerticalAlignment = VerticalAlignment.Top;
        g.Children.Add(box);
        g.Children.Add(new Border
        {
            HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom, CornerRadius = new CornerRadius(8), Padding = new Thickness(5, 0, 5, 1),
            Background = Ck(CkSlot), BorderBrush = Ck(CkFrame), BorderThickness = new Thickness(1),
            Child = new TextBlock { Text = badge, FontSize = 11.5, FontWeight = FontWeights.Bold, Foreground = Brushes.White },
        });
        return g;
    }

    /// <summary>The header band: the item's icon framed and glowing in its grade colour, its name, and a line under it.</summary>
    Border CkBand(string item, string grade, string sub)
    {
        var c = GradeColor(grade);
        var g = new Grid();
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        g.ColumnDefinitions.Add(new ColumnDefinition());
        var icon = CkIcon(item, 64, c, glow: true); icon.Margin = new Thickness(0, 0, 16, 0);
        g.Children.Add(icon);
        var names = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        var name = CkText(item, c, 20, FontWeights.SemiBold, display: true); name.TextWrapping = TextWrapping.Wrap; name.TextTrimming = TextTrimming.None;
        names.Children.Add(name);
        if (sub.Length > 0) { var s = CkText(sub, Color.FromRgb((byte)((c.R + 0xB3) / 2), (byte)((c.G + 0xB7) / 2), (byte)((c.B + 0xD3) / 2)), 13); s.Margin = new Thickness(0, 3, 0, 0); names.Children.Add(s); }
        Grid.SetColumn(names, 1); g.Children.Add(names);
        return new Border
        {
            Child = g, Padding = new Thickness(18, 14, 18, 14), BorderThickness = new Thickness(0, 0, 0, 1), BorderBrush = Ck(c, 0x70), CornerRadius = new CornerRadius(5, 5, 0, 0),
            Background = new LinearGradientBrush(new GradientStopCollection { new(Color.FromArgb(0x48, c.R, c.G, c.B), 0), new(Color.FromArgb(0x18, c.R, c.G, c.B), 0.55), new(Colors.Transparent, 1) }, 0),
        };
    }

    /// <summary>A gold section heading with a rule fading out to its right.</summary>
    static FrameworkElement CkSection(string text)
    {
        var g = new Grid { Margin = new Thickness(20, 16, 20, 8) };
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        g.ColumnDefinitions.Add(new ColumnDefinition());
        var t = CkText(text, CkGoldRule, 11, FontWeights.Bold); t.Margin = new Thickness(0, 0, 10, 0);
        g.Children.Add(t);
        var rule = new Border { Height = 1, VerticalAlignment = VerticalAlignment.Center, Background = new LinearGradientBrush(Color.FromArgb(0x66, CkGoldRule.R, CkGoldRule.G, CkGoldRule.B), Colors.Transparent, 0) };
        Grid.SetColumn(rule, 1); g.Children.Add(rule);
        return g;
    }

    /// <summary>A line with something on the left and its figure on the right; an optional faint line under the left side.</summary>
    static FrameworkElement CkKv(string left, string right, Color rightColor, string sub = "", bool mono = true, double rightSize = 13.5)
    {
        var g = new Grid { Margin = new Thickness(20, 3, 20, 3) };
        g.ColumnDefinitions.Add(new ColumnDefinition()); g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var l = new StackPanel();
        l.Children.Add(CkText(left, CkInk, 13.5));
        if (sub.Length > 0) l.Children.Add(CkText(sub, CkFaint, 11.5));
        g.Children.Add(l);
        var r = CkText(right, rightColor, rightSize, FontWeights.SemiBold, mono); r.Margin = new Thickness(12, 0, 0, 0);
        Grid.SetColumn(r, 1); g.Children.Add(r);
        return g;
    }

    /// <summary>A note in the card's faint ink, wrapped.</summary>
    static FrameworkElement CkNote(string text)
    {
        var t = CkText(text, CkFaint, 12.5); t.TextWrapping = TextWrapping.Wrap; t.TextTrimming = TextTrimming.None; t.Margin = new Thickness(20, 2, 20, 2);
        return t;
    }

    /// <summary>Success against fail as one bar, with their shares above it.</summary>
    static FrameworkElement CkOddsBar(double success)
    {
        var sp = new StackPanel { Margin = new Thickness(20, 14, 20, 0) };
        var top = new Grid();
        top.ColumnDefinitions.Add(new ColumnDefinition()); top.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        top.Children.Add(CkText($"Success {success:P0}", CkMuted, 12.5));
        var f = CkText($"Fail {1 - success:P0}", CkMuted, 12.5); Grid.SetColumn(f, 1); top.Children.Add(f);
        sp.Children.Add(top);
        var bar = new Grid { Height = 8, Margin = new Thickness(0, 6, 0, 0) };
        bar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Math.Max(0.001, success), GridUnitType.Star) });
        bar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Math.Max(0.001, 1 - success), GridUnitType.Star) });
        bar.Children.Add(new Border { CornerRadius = new CornerRadius(4, 0, 0, 4), Background = new LinearGradientBrush(Color.FromRgb(0x3D, 0x8B, 0xFF), Color.FromRgb(0x69, 0xA8, 0xFF), 0) });
        var fb = new Border { CornerRadius = new CornerRadius(0, 4, 4, 0), Background = Ck(Color.FromRgb(0xE8, 0x89, 0x2F), 0xD8) }; Grid.SetColumn(fb, 1); bar.Children.Add(fb);
        sp.Children.Add(bar);
        return sp;
    }

    /// <summary>A rounded box in the slot colour, for totals and slots.</summary>
    static Border CkBox(UIElement child, Thickness margin, Thickness? padding = null) => new()
    {
        Child = child, Margin = margin, Padding = padding ?? new Thickness(12, 9, 12, 9), CornerRadius = new CornerRadius(6),
        Background = Ck(CkSlot), BorderBrush = Ck(CkSlotLine), BorderThickness = new Thickness(1),
    };

    /// <summary>A small rounded label.</summary>
    static Border CkChip(string text, Color ink) => new()
    {
        CornerRadius = new CornerRadius(9), Padding = new Thickness(9, 3, 9, 4), Margin = new Thickness(0, 0, 6, 6),
        Background = Ck(Color.FromRgb(0x17, 0x1C, 0x38)), Child = CkText(text, ink, 12),
    };

    /// <summary>The tinted footer: a label with a small line under it, and a big figure, green or red.</summary>
    static Border CkFooter(string label, string sub, string value, bool good)
    {
        var g = new Grid();
        g.ColumnDefinitions.Add(new ColumnDefinition()); g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var l = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        l.Children.Add(CkText(label, CkMuted, 13.5));
        if (sub.Length > 0) l.Children.Add(CkText(sub, CkFaint, 11.5));
        g.Children.Add(l);
        var v = CkText(value, good ? CkGood : CkBad, 26, FontWeights.SemiBold, display: true); Grid.SetColumn(v, 1); g.Children.Add(v);
        var tint = good ? Color.FromRgb(0x0F, 0x1E, 0x16) : Color.FromRgb(0x1A, 0x0F, 0x12);
        return new Border
        {
            Child = g, Margin = new Thickness(0, 16, 0, 0), Padding = new Thickness(20, 12, 20, 14), BorderThickness = new Thickness(0, 1, 0, 0), BorderBrush = Ck(CkSlotLine),
            CornerRadius = new CornerRadius(0, 0, 5, 5), Background = new LinearGradientBrush(tint, Color.FromRgb((byte)(tint.R * 0.7), (byte)(tint.G * 0.7), (byte)(tint.B * 0.7)), 90),
        };
    }

    /// <summary>A link at the bottom of a card.</summary>
    FrameworkElement CkLinks(params (string Text, string Url)[] links)
    {
        var t = new TextBlock { Margin = new Thickness(20, 12, 20, 14), FontSize = 12 };
        foreach (var (text, url) in links)
        {
            if (t.Inlines.Count > 0) t.Inlines.Add(new System.Windows.Documents.Run("   ·   ") { Foreground = Ck(CkFaint) });
            var h = new System.Windows.Documents.Hyperlink(new System.Windows.Documents.Run(text)) { Foreground = Ck(CkGold), TextDecorations = null };
            h.Click += (_, _) => { Open(url); if (_card != null) _card.IsOpen = false; };
            t.Inlines.Add(h);
        }
        return t;
    }
}
