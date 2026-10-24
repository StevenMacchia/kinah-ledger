using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace KinahLedger.Ui;

/// <summary>
/// "Copy for Discord": a picture of the count on screen (zone, the hour's total, three stats, the five best drops) put on
/// the clipboard for a Ctrl+V into Discord, and saved under %LOCALAPPDATA%\KinahLedger\cards. Drawn in the Night look
/// whatever the window's look, with the K tile, so a shared card always looks like Kinah Ledger. Nothing is sent anywhere.
/// </summary>
public partial class MainWindow
{
    /// <summary>The figures on the card, from the last FillNumbers: the same ones the window and the panel show.</summary>
    sealed record CardNumbers(string? Zone, string Kind, TimeSpan Elapsed, long Total, double PerHr, (string Value, string Label)[] Stats);

    CardNumbers? _shareCard;

    const int CardW = 1200, CardH = 630;
    static readonly Color CardInk = Color.FromRgb(0xE9, 0xEF, 0xF7), CardMuted = Color.FromRgb(0x8A, 0x92, 0xB8), CardGold = Color.FromRgb(0xF8, 0xD5, 0x98),
        CardTeal = Color.FromRgb(0x5E, 0xD8, 0xCB), CardLine = Color.FromRgb(0x2A, 0x31, 0x55);

    void CopyShareCard()
    {
        if (_shareCard == null || _shareCard.Total <= 0) { Say("Nothing to share yet: the card needs some Kinah or loot counted first.", "Warn"); return; }
        try
        {
            var bmp = RenderShareCard();
            var dir = System.IO.Path.Combine(CrashLog.Dir, "cards");
            Directory.CreateDirectory(dir);
            var path = System.IO.Path.Combine(dir, $"kinah-ledger-{DateTime.Now:yyyy-MM-dd-HHmm}.png");
            SavePng(bmp, path);
            // the picture as a PNG (Discord keeps it sharp) and as a plain bitmap for everything else
            var data = new DataObject();
            using (var ms = new MemoryStream()) { var enc = new PngBitmapEncoder(); enc.Frames.Add(BitmapFrame.Create(bmp)); enc.Save(ms); data.SetData("PNG", new MemoryStream(ms.ToArray())); }
            data.SetImage(bmp);
            Clipboard.SetDataObject(data, true);
            Say("Card copied. Paste it into Discord with Ctrl+V. A copy is in your cards folder.", "Good");
            Status.ToolTip = path;
        }
        catch (Exception ex) { CrashLog.Write("share card", ex); Say("Couldn't make the card: " + ex.Message, "Bad"); }
    }

    /// <summary>The card for a layout render (ui-shot), next to the window's own picture.</summary>
    public void SaveShareCard(string path) { if (_shareCard != null) SavePng(RenderShareCard(), path); }

    static void SavePng(BitmapSource bmp, string path)
    {
        var enc = new PngBitmapEncoder();
        enc.Frames.Add(BitmapFrame.Create(bmp));
        using var fs = File.Create(path);
        enc.Save(fs);
    }

    BitmapSource RenderShareCard()
    {
        var c = _shareCard!;
        var display = TryFindResource("DisplayFont") as FontFamily ?? new FontFamily("Segoe UI");
        var body = TryFindResource("BodyFont") as FontFamily ?? new FontFamily("Segoe UI");
        TextBlock T(string text, double size, Color color, FontWeight? weight = null, FontFamily? font = null) =>
            new() { Text = text, FontSize = size, Foreground = new SolidColorBrush(color), FontWeight = weight ?? FontWeights.Normal, FontFamily = font ?? body, TextTrimming = TextTrimming.CharacterEllipsis };

        var root = new Grid { Width = CardW, Height = CardH, ClipToBounds = true };
        root.Children.Add(new Border { Background = new LinearGradientBrush(Color.FromRgb(0x0B, 0x0E, 0x1C), Color.FromRgb(0x16, 0x1B, 0x36), 35) });
        // the gold glow behind the rate, as the run card's hero
        root.Children.Add(new Border { Background = new RadialGradientBrush(Color.FromArgb(0x30, 0x6D, 0x5D, 0xF6), Colors.Transparent) { Center = new Point(0.2, 0.55), GradientOrigin = new Point(0.2, 0.55), RadiusX = 0.6, RadiusY = 0.9 } });

        var page = new Grid { Margin = new Thickness(56, 44, 56, 40) };
        page.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        page.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        page.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.Children.Add(page);

        // the mark, the name, the server; the site on the right
        var head = new Grid();
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        head.ColumnDefinitions.Add(new ColumnDefinition());
        var brand = new StackPanel { Orientation = Orientation.Horizontal };
        if (TryFindResource("AppMark") is ImageSource mark) brand.Children.Add(new Image { Source = mark, Width = 48, Height = 48, Margin = new Thickness(0, 0, 14, 0) });
        var names = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        names.Children.Add(T("Kinah Ledger", 24, CardInk, FontWeights.Bold, display));
        names.Children.Add(T(Server is { } sv ? $"{sv.Label} · {sv.Region.Name}" : "Aion 2", 17, CardMuted));
        brand.Children.Add(names);
        head.Children.Add(brand);
        var site = T("kinahledger.com", 18, CardMuted); site.HorizontalAlignment = HorizontalAlignment.Right; site.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(site, 1); head.Children.Add(site);
        page.Children.Add(head);

        // left: where, how long, the hour's total, three stats; right: the five best drops
        var mid = new Grid { Margin = new Thickness(0, 30, 0, 0) };
        mid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(560) });
        mid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        Grid.SetRow(mid, 1); page.Children.Add(mid);

        var left = new StackPanel { Margin = new Thickness(0, 0, 36, 0) };
        left.Children.Add(T($"{c.Kind} · {Clock(c.Elapsed)}", 20, CardGold, FontWeights.SemiBold));
        left.Children.Add(T(c.Zone ?? "Aion 2", 44, CardInk, FontWeights.SemiBold, display));
        var rate = T($"{Fk(c.PerHr)}/hr", 76, CardGold, FontWeights.SemiBold, display); rate.Margin = new Thickness(0, 14, 0, 0);
        left.Children.Add(rate);
        left.Children.Add(T("Kinah plus loot, after tax", 18, CardMuted));
        var stats = new Grid { Margin = new Thickness(0, 26, 0, 0) };
        var cells = new List<(string, string, Color)> { (Fk(c.Total), "session total", CardTeal) };
        cells.AddRange(c.Stats.Select(s => (s.Value, s.Label, CardInk)));
        for (int i = 0; i < cells.Count; i++)
        {
            stats.ColumnDefinitions.Add(new ColumnDefinition());
            var cell = new StackPanel();
            cell.Children.Add(T(cells[i].Item1, 30, cells[i].Item3, FontWeights.SemiBold, display));
            cell.Children.Add(T(cells[i].Item2, 16, CardMuted));
            Grid.SetColumn(cell, i); stats.Children.Add(cell);
        }
        left.Children.Add(stats);
        // one line: how much of the count the game's own numbers backed up (the Accuracy card's share)
        if (_accView?.Discord is { } acc) { var al = T(acc, 17, CardTeal); al.Margin = new Thickness(0, 22, 0, 0); left.Children.Add(al); }
        mid.Children.Add(left);

        var right = new Border { VerticalAlignment = VerticalAlignment.Top, CornerRadius = new CornerRadius(18), BorderThickness = new Thickness(1), BorderBrush = new SolidColorBrush(CardLine), Background = new SolidColorBrush(Color.FromArgb(0x66, 0x0A, 0x0D, 0x1C)), Padding = new Thickness(24, 18, 24, 12) };
        var list = new StackPanel();
        list.Children.Add(T(_topRows.Count > 0 ? "TOP DROPS" : "LOOT", 15, CardMuted, FontWeights.Bold));
        if (_topRows.Count == 0) { var none = T("Kinah only so far.", 20, CardInk); none.Margin = new Thickness(0, 14, 0, 0); list.Children.Add(none); }
        foreach (var r in _topRows.Take(5))
        {
            var row = new Grid { Margin = new Thickness(0, 12, 0, 0) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition());
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var tile = new Border { Width = 46, Height = 46, CornerRadius = new CornerRadius(9), BorderThickness = new Thickness(1), BorderBrush = new SolidColorBrush(Color.FromArgb(0x6B, 0xCD, 0xD3, 0xE2)), Background = new SolidColorBrush(Color.FromRgb(0x1B, 0x21, 0x3E)), Margin = new Thickness(0, 0, 14, 0) };
            tile.Child = r.Icon?.Image is ImageSource img ? new Image { Source = img, Margin = new Thickness(3), Stretch = Stretch.Uniform }
                : new TextBlock { Text = r.Initial, FontSize = 18, FontWeight = FontWeights.Bold, Foreground = new SolidColorBrush(CardMuted), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            row.Children.Add(tile);
            var what = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            what.Children.Add(T(r.Name, 20, GradeColor(r.Grade), FontWeights.SemiBold));
            what.Children.Add(T("×" + r.Drops, 15, CardMuted));
            Grid.SetColumn(what, 1); row.Children.Add(what);
            var val = T(Fk(r.Worth), 22, CardGold, FontWeights.SemiBold, display); val.VerticalAlignment = VerticalAlignment.Center; val.Margin = new Thickness(12, 0, 0, 0);
            Grid.SetColumn(val, 2); row.Children.Add(val);
            list.Children.Add(row);
        }
        right.Child = list;
        Grid.SetColumn(right, 1); mid.Children.Add(right);

        var foot = T($"{DateTime.Now:d MMM yyyy} · drops at the lowest Market ask on the server · per hour and total after {1 - Data.PriceBook.SellerKeeps:P0} tax", 15, CardMuted);
        foot.Margin = new Thickness(0, 18, 0, 0);
        Grid.SetRow(foot, 2); page.Children.Add(foot);

        root.Measure(new Size(CardW, CardH));
        root.Arrange(new Rect(0, 0, CardW, CardH));
        root.UpdateLayout();
        // twice the pixels, so it stays sharp when Discord scales it
        var rtb = new RenderTargetBitmap(CardW * 2, CardH * 2, 192, 192, PixelFormats.Pbgra32);
        rtb.Render(root);
        rtb.Freeze();
        return rtb;
    }

    /// <summary>The game's grade colours, fixed (the card is always drawn dark): Rare green, Legend blue, Unique yellow, Epic violet.</summary>
    static Color GradeColor(string grade) => grade switch
    {
        "Rare" => Color.FromRgb(0x62, 0xD9, 0x8B), "Legend" => Color.FromRgb(0x5A, 0xA6, 0xFF), "Unique" => Color.FromRgb(0xF3, 0xD3, 0x5E), "Epic" => Color.FromRgb(0xB4, 0x8B, 0xFF),
        _ => CardInk,
    };
}
