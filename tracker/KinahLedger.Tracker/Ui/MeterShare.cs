using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using KinahLedger.Parser;
using Path = System.Windows.Shapes.Path;

namespace KinahLedger.Ui;

/// <summary>
/// The meter's share picture (Steven, 2026-11-17: "add a share button on hover on the dps meter overlay that shares a picture
/// of the DPS meter for socials"). The meter exactly as the Parser's own window draws it, in the player's look (Classic,
/// Wacky, Tactical or Glitch) and width, settled (no motion, no hover card), on a clean dark frame: the boss, the place, how
/// it ended and when on top; the Kinah Ledger mark and kinahledger.com at the foot. 1200 px wide (drawn at twice its size,
/// never a screenshot), at least 630 tall, so Discord, Reddit and X show it sharp. Other members' names are hidden by default
/// (Settings › In game › Parser › Hide other names): their class icon and figures stay, each called "Member 2" and on;
/// you keep yours. Copied to the clipboard and saved in Pictures\Kinah Ledger, as the Farming share picture. Nothing is uploaded.
/// </summary>
public static class MeterShare
{
    /// <summary>The picture's width in DIPs, and its least height (1200 × 630 px: the link-card shape).</summary>
    public const double Width = 600, MinHeight = 315;
    /// <summary>The PNG's width at any meter up to the frame's (a wider one widens it).</summary>
    public const int PixelWidth = 1200;
    /// <summary>The frame's sides round the meter.</summary>
    const double Side = 40;

    /// <summary>What the meter is drawn with: the Parser window's look, mode, group details and the Kinah overlay's frame.</summary>
    public sealed record Look(string Key, ParserMode Mode, bool Details, double Radius, Thickness Pad);

    static readonly Color Bg0 = Color.FromRgb(0x1C, 0x1F, 0x29), Bg1 = Color.FromRgb(0x0D, 0x0E, 0x13), Line = Color.FromRgb(0x2A, 0x2E, 0x3D);

    /// <summary>
    /// The record with the other members' names hidden: each named by their place on the meter ("Member 2" beside the rank
    /// chip 2), their class, Combat Power and figures as they were. Yours stays. A copy: the record itself is never touched.
    /// </summary>
    public static Encounter Hidden(Encounter e)
    {
        var c = BossParser.Copy(e);
        var order = c.Ranked().Concat(c.Roster).Distinct().Where(f => !f.Other).ToList();
        for (int i = 0; i < order.Count; i++) if (!order[i].You) order[i].Name = L.F("Member {0}", i + 1);
        return c;
    }

    /// <summary>
    /// The picture, at half its pixel size (600 wide; a meter wider than the frame takes widens it rather than shrink): the
    /// frame, its head, the meter scaled to fill it, its foot.
    /// </summary>
    public static Border Visual(Encounter e, DateTime now, Look look, bool hideNames)
    {
        var shown = hideNames ? Hidden(e) : e;
        var face = ParserOverlay.MeterFace(look.Key, shown, now, look.Mode, look.Details, look.Radius, look.Pad);
        face.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        // the meter as large as the frame takes it; a meter wider than that keeps its own size and the frame grows round it
        double fw = Math.Max(1, face.DesiredSize.Width);
        double scale = Math.Clamp((Width - 2 * Side) / fw, 1, 1.6);
        double width = Math.Max(Width, Math.Ceiling(fw * scale + 2 * Side));
        var st = new ScaleTransform(scale, scale); st.Freeze();
        var meter = new Border { Child = face, LayoutTransform = st, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Tag = "share-meter" };

        var page = new Grid();
        page.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        page.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        page.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        page.Children.Add(Head(e, now));
        var mid = new Border { Child = meter, Margin = new Thickness(0, 18, 0, 18) };
        Grid.SetRow(mid, 1); page.Children.Add(mid);
        var foot = Foot();
        Grid.SetRow(foot, 2); page.Children.Add(foot);

        // the frame: a dark gradient, the outcome's colour glowing softly behind the meter
        var tone = e.Live ? HudKit.Rgb(HudKit.GoldC) : e.Tone == "good" ? HudKit.Rgb(HudKit.MintC) : e.Tone == "bad" ? HudKit.Rgb(HudKit.RecC) : HudKit.Rgb(HudKit.GoldC);
        var bg = new LinearGradientBrush { StartPoint = new Point(0.3, 0), EndPoint = new Point(0.7, 1) };
        bg.GradientStops.Add(new GradientStop(Bg0, 0));
        bg.GradientStops.Add(new GradientStop(Bg1, 0.62));
        bg.GradientStops.Add(new GradientStop(Color.FromRgb(0x0B, 0x0C, 0x10), 1));
        var glow = new Border
        {
            Background = new RadialGradientBrush(Color.FromArgb(0x26, tone.R, tone.G, tone.B), Color.FromArgb(0, tone.R, tone.G, tone.B))
            { Center = new Point(0.5, 0.55), GradientOrigin = new Point(0.5, 0.55), RadiusX = 0.62, RadiusY = 0.5 },
        };
        // a hairline of the outcome's colour along the top edge
        var topLine = new Border
        {
            Height = 2, VerticalAlignment = VerticalAlignment.Top,
            Background = new LinearGradientBrush(new GradientStopCollection { new(Color.FromArgb(0, tone.R, tone.G, tone.B), 0), new(Color.FromArgb(0xCC, tone.R, tone.G, tone.B), 0.5), new(Color.FromArgb(0, tone.R, tone.G, tone.B), 1) }, 0),
        };
        var layers = new Grid { Children = { glow, topLine, new Border { Padding = new Thickness(Side, 22, Side, 16), Child = page } } };
        var card = new Border
        {
            Width = width, MinHeight = MinHeight, Background = bg, BorderBrush = new SolidColorBrush(Line), BorderThickness = new Thickness(1), Child = layers, ClipToBounds = true,
            Tag = "meter-share",
        };
        TextElement.SetFontFamily(card, HudKit.Text);
        TextOptions.SetTextFormattingMode(card, TextFormattingMode.Ideal);
        return card;
    }

    /// <summary>The head: the boss (or the run, the fight) large, its outcome on the right; the place and the day under it.</summary>
    static FrameworkElement Head(Encounter e, DateTime now)
    {
        var box = new StackPanel { Tag = "share-head" };
        var top = new Grid();
        top.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        top.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        top.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var art = GameArt.Element(e.ModeKind == ParserMode.Run ? "dungeon" : e.ModeKind == ParserMode.Fights && e.Boss.Length == 0 ? (e.Kind == "Abyss" ? "rift" : "medal") : "boss", 30);
        art.Margin = new Thickness(0, 0, 12, 0); art.VerticalAlignment = VerticalAlignment.Center;
        top.Children.Add(art);
        var title = new TextBlock { Text = e.Title, FontSize = 22, FontWeight = FontWeights.Bold, Foreground = HudKit.Ink, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center, Tag = "share-title" };
        Grid.SetColumn(title, 1); top.Children.Add(title);
        // how it ended and how long it took ("Killed in 2:31"), on a pill of its colour; a fight under way says its clock
        var (said, ink) = e.Live ? ($"{L.T("Fighting")} {BossParser.Clock(e.Duration(now))}", HudKit.Gold) : ParserPanel.OutcomeOf(e, now);
        var c = ink.Color;
        var outcome = new Border
        {
            CornerRadius = new CornerRadius(8), Padding = new Thickness(10, 3, 11, 4), Margin = new Thickness(14, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center,
            Background = new SolidColorBrush(Color.FromArgb(0x24, c.R, c.G, c.B)), BorderBrush = new SolidColorBrush(Color.FromArgb(0x80, c.R, c.G, c.B)), BorderThickness = new Thickness(1),
            Child = new TextBlock { Text = said, FontSize = 13, FontWeight = FontWeights.Bold, Foreground = ink, Tag = "share-outcome" },
        };
        Typography.SetNumeralAlignment(outcome, FontNumeralAlignment.Tabular);
        Grid.SetColumn(outcome, 2); top.Children.Add(outcome);
        box.Children.Add(top);
        // the place and the day: "Ferocious Horn Den · Conquest · Expedition  ·  Nov 17, 2026"
        var where = PK.WhereOf(e);
        var day = L.Day(e.Start.ToLocalTime());
        var sub = new TextBlock { FontSize = 12.5, FontWeight = FontWeights.Medium, Foreground = HudKit.Muted, Margin = new Thickness(42, 2, 0, 0), TextTrimming = TextTrimming.CharacterEllipsis, Tag = "share-sub" };
        if (where.Length > 0) { sub.Inlines.Add(new Run(where)); sub.Inlines.Add(new Run("   ·   ") { Foreground = HudKit.Faint }); }
        sub.Inlines.Add(new Run(day) { Foreground = HudKit.Faint });
        box.Children.Add(sub);
        return box;
    }

    /// <summary>The foot: a hairline, the mark and Kinah Ledger on the left, kinahledger.com on the right.</summary>
    static FrameworkElement Foot()
    {
        var foot = new Grid { Tag = "share-foot" };
        foot.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        foot.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        foot.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var rule = new Border { Height = 1, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 0, 0, 0), Background = new SolidColorBrush(Line) };
        Grid.SetColumnSpan(rule, 3); foot.Children.Add(rule);
        var brand = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 12, 0, 0) };
        var tile = new Border
        {
            Width = 22, Height = 22, CornerRadius = new CornerRadius(6), BorderThickness = new Thickness(1), BorderBrush = new SolidColorBrush(Color.FromRgb(0x35, 0x39, 0x49)),
            Background = new LinearGradientBrush(Color.FromRgb(0x27, 0x2A, 0x36), Color.FromRgb(0x16, 0x18, 0x1F), new Point(0.33, 0), new Point(0.67, 1)), VerticalAlignment = VerticalAlignment.Center,
        };
        tile.Child = Application.Current?.TryFindResource("AppMark") is ImageSource mark
            ? new Image { Source = mark, Width = 15, Height = 15, Stretch = Stretch.Uniform }
            : new Path { Width = 12, Height = 12, Stretch = Stretch.Uniform, Fill = HudKit.Gold, Data = Geometry.Parse("M6,3 H9.4 V10.2 L15.6,3 H19.7 L13.1,10.6 L20,21 H15.8 L10.8,13.2 L9.4,14.8 V21 H6 Z") };
        brand.Children.Add(tile);
        brand.Children.Add(new TextBlock { Text = "Kinah Ledger", FontSize = 13, FontWeight = FontWeights.SemiBold, Foreground = HudKit.Ink, Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center });
        foot.Children.Add(brand);
        var site = new TextBlock { Text = "kinahledger.com", FontSize = 13, FontWeight = FontWeights.SemiBold, Foreground = HudKit.Gold, Margin = new Thickness(0, 12, 0, 0), VerticalAlignment = VerticalAlignment.Center, Tag = "share-site" };
        Grid.SetColumn(site, 2); foot.Children.Add(site);
        return foot;
    }

    /// <summary>The picture laid out at its size (600 wide, as tall as it needs, 315 at least), settled.</summary>
    public static Border Laid(Encounter e, DateTime now, Look look, bool hideNames)
    {
        bool still0 = Motion.Still; Motion.Still = true;
        try
        {
            var v = Visual(e, now, look, hideNames);
            v.Measure(new Size(v.Width, double.PositiveInfinity));
            v.Arrange(new Rect(0, 0, v.Width, v.DesiredSize.Height));
            v.UpdateLayout();
            IconCache.Drain(TimeSpan.FromSeconds(2));
            v.UpdateLayout();
            return v;
        }
        finally { Motion.Still = still0; }
    }

    /// <summary>The PNG: the visual laid out and drawn at twice its size (1200 px wide), settled; never a screenshot.</summary>
    public static BitmapSource Render(Encounter e, DateTime now, Look look, bool hideNames)
    {
        bool still0 = Motion.Still; Motion.Still = true;
        try
        {
            var v = Laid(e, now, look, hideNames);
            int h = (int)Math.Ceiling(v.ActualHeight) * 2;   // twice the height, whole pixels
            var rtb = new RenderTargetBitmap((int)Math.Round(v.Width * 2), h, 192, 192, PixelFormats.Pbgra32);
            LangCheck.Scan(v, "meter share picture");
            rtb.Render(v);
            rtb.Freeze();
            return rtb;
        }
        finally { Motion.Still = still0; }
    }

    /// <summary>"kinah-ledger-2026-11-17-2130-fight-Kromede.png": the day and time it started and the boss or the place, safe for a file name.</summary>
    public static string FileName(Encounter e)
    {
        var what = new string((e.Boss.Length > 0 ? e.Boss : e.Instance.Length > 0 ? BossBook.Strip(e.Instance) : "fight").Select(ch => char.IsLetterOrDigit(ch) ? ch : '-').ToArray()).Trim('-');
        while (what.Contains("--")) what = what.Replace("--", "-");
        if (what.Length > 40) what = what[..40].TrimEnd('-');
        return $"kinah-ledger-{e.Start.ToLocalTime():yyyy-MM-dd-HHmm}-fight{(what.Length > 0 ? "-" + what : "")}.png";
    }
}
