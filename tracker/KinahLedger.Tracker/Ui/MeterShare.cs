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
/// Wacky, Tactical or Glitch), settled (no motion, no hover card), wide enough that no name or label is cut off, on a clean
/// dark frame: a slim line on top with only what the look doesn't say (the portrait, the place and mode, the day), the Kinah
/// Ledger mark and kinahledger.com at the foot. 1200 px wide (drawn at twice its size,
/// never a screenshot), at least 630 tall, so Discord, Reddit and X show it sharp. Other members' names are hidden by default
/// (Settings › In game › Parser › Show party names, off since 1.0.36): their class icon and figures stay, each called by their class ("Chanter");
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
    /// The record with the other members' names hidden (the default since 1.0.36: a picture one player posts names only them):
    /// each called by their class in the language in use ("Chanter"; a second of a class "Chanter 2", in meter order), one whose
    /// class isn't known by their place ("Member 3"). Their class icon, Combat Power and figures stay as they were. Yours stays.
    /// A copy: the record itself is never touched.
    /// </summary>
    public static Encounter Hidden(Encounter e)
    {
        var c = BossParser.Copy(e);
        var order = c.Ranked().Concat(c.Roster).Distinct().Where(f => !f.Other).ToList();
        var seen = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < order.Count; i++)
        {
            if (order[i].You) continue;
            var cls = ClassLabel(order[i].Class);
            if (cls.Length == 0) { order[i].Name = L.F("Member {0}", i + 1); continue; }
            int n = seen[cls] = seen.GetValueOrDefault(cls) + 1;
            order[i].Name = n == 1 ? cls : $"{cls} {n}";
        }
        return c;
    }

    /// <summary>A class as the share picture names a member by it ("chanter" is "Chanter", 호법성 in Korean); "" when not known.</summary>
    public static string ClassLabel(string cls)
    {
        var t = (cls ?? "").Trim();
        if (t.Length == 0) return "";
        return L.T(char.ToUpperInvariant(t[0]) + t[1..].ToLowerInvariant());
    }

    /// <summary>
    /// The picture around a meter already laid out (<see cref="With{T}"/> makes it): its head, the meter scaled to fill the
    /// frame (600 wide; a meter wider than that keeps its size and the frame grows round it), its foot.
    /// </summary>
    /// <param name="view">A Parser page view's face (Ui/ParserShare.cs): one without the boss's name of its own gets the outcome pill beside it, live too.</param>
    /// <param name="pb">A personal best this pull set (<see cref="PbOf"/>): the gold badge in the head, the frame's glow gold.</param>
    static Border Frame(Encounter e, DateTime now, FrameworkElement face, bool view = false, Pb? pb = null)
    {
        double fw = Math.Max(1, face.DesiredSize.Width);
        // a few pixels spare: the meter sits a pixel or two in from the side once laid out, and a full-width one lost its right edge
        double scale = Math.Clamp((Width - 2 * Side - 8) / fw, 1, 1.6);
        double width = Math.Max(Width, Math.Ceiling(fw * scale + 2 * Side + 2));   // + the frame's own 1 px edges, so a face as wide as the room is never cut
        var st = new ScaleTransform(scale, scale); st.Freeze();
        var meter = new Border { Child = face, LayoutTransform = st, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Tag = "share-meter" };

        var page = new Grid();
        page.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        page.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        page.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        page.Children.Add(Head(e, now, face, view, pb));
        var mid = new Border { Child = meter, Margin = new Thickness(0, 14, 0, 18) };
        Grid.SetRow(mid, 1); page.Children.Add(mid);
        var foot = Foot();
        Grid.SetRow(foot, 2); page.Children.Add(foot);

        // the frame: a dark gradient, the outcome's colour glowing softly behind the meter
        var tone = e.Live || pb != null ? HudKit.Rgb(HudKit.GoldC) : e.Tone == "good" ? HudKit.Rgb(HudKit.MintC) : e.Tone == "bad" ? HudKit.Rgb(HudKit.RecC) : HudKit.Rgb(HudKit.GoldC);
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
        var layers = new Grid { Children = { glow, topLine, new Border { Padding = new Thickness(Side, 18, Side, 16), Child = page } } };
        var card = new Border
        {
            Width = width, MinHeight = MinHeight, Background = bg, BorderBrush = new SolidColorBrush(Line), BorderThickness = new Thickness(1), Child = layers, ClipToBounds = true,
            Tag = "meter-share",
        };
        TextElement.SetFontFamily(card, HudKit.Text);
        TextOptions.SetTextFormattingMode(card, TextFormattingMode.Ideal);
        return card;
    }

    /// <summary>
    /// The head, never saying what the meter already says (Steven's coordinator, 2026-11-17: "No repeats"): a slim muted line,
    /// the boss's portrait (when the look draws none), the place and mode (when the look doesn't name them) and the day. The
    /// boss's name large and the outcome pill come back only for a look that draws no title or no outcome of its own.
    /// </summary>
    static FrameworkElement Head(Encounter e, DateTime now, FrameworkElement face, bool view = false, Pb? pb = null)
    {
        string said = string.Join(" | ", Texts(face));
        bool Has(string s) => s.Length > 0 && said.Contains(s, StringComparison.OrdinalIgnoreCase);
        // a page view names the boss as a title of its own only in the hero ("your DPS on Kromede" is not one)
        bool title = view ? Texts(face).Any(t => t.Trim().Equals(e.Title, StringComparison.OrdinalIgnoreCase)) : Has(e.Title);
        var (outText, ink) = e.Live ? ($"{L.T("Fighting")} {e.ClockText(now)}", HudKit.Gold) : ParserPanel.OutcomeOf(e, now);
        string word = L.T(e.Outcome switch { "kill" => "Killed", "wipe" => "Wiped", "cleared" => "Cleared", "ended" => "Ended", "left" => "Left", _ => "Over" });
        bool outcome = e.Live || Has(outText) || Has(word) || Has(L.T("Over"));
        // a table or the timeline says no outcome of its own ("Over time" and "left" are a column and a tag there, not one)
        if (view && !title) outcome = false;
        string artKey = e.ModeKind == ParserMode.Run ? "dungeon" : e.ModeKind == ParserMode.Fights && e.Boss.Length == 0 ? (e.Kind == "Abyss" ? "rift" : "medal") : "boss";
        bool art = Images(face).Any(s => ReferenceEquals(s, GameArt.Image(artKey)));
        var where = PK.WhereOf(e);
        // the day and the clock the pull started ("Nov 13, 2026 · 21:30")
        var day = L.Day(e.Start.ToLocalTime()) + "  ·  " + e.Start.ToLocalTime().ToString("HH:mm", System.Globalization.CultureInfo.InvariantCulture);

        var box = new StackPanel { Tag = "share-head" };
        if (!title)
        {
            // a look without a title of its own: the boss large, the outcome beside it
            var top = new Grid();
            top.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            top.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            top.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            if (!art) { var a = GameArt.Element(artKey, 30); a.Margin = new Thickness(0, 0, 12, 0); a.VerticalAlignment = VerticalAlignment.Center; top.Children.Add(a); }
            var big = new TextBlock { Text = e.Title, FontSize = 22, FontWeight = FontWeights.Bold, Foreground = HudKit.Ink, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center, Tag = "share-title" };
            Grid.SetColumn(big, 1); top.Children.Add(big);
            var right = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            if (!outcome) right.Children.Add(Pill(outText, ink));
            if (pb != null) right.Children.Add(Badge(pb, Has));
            Grid.SetColumn(right, 2); top.Children.Add(right);
            box.Children.Add(top);
            box.Children.Add(Slim(Has(where) ? "" : where, day, null, !art ? 42 : 0, pb != null));
            return box;
        }
        // the slim line: the portrait, the place and mode the look doesn't say, the day; the outcome only if the look has none
        var line = new Grid();
        line.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        line.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        line.Children.Add(Slim(Has(where) ? "" : where, day, art ? null : GameArt.Element(artKey, 20), 0, pb != null));
        var side = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        if (!outcome) side.Children.Add(Pill(outText, ink));
        if (pb != null) side.Children.Add(Badge(pb, Has));
        Grid.SetColumn(side, 1); line.Children.Add(side);
        box.Children.Add(line);
        return box;
    }

    /// <summary>"[portrait] Ferocious Horn Den · Conquest · Expedition · Nov 13, 2026", small and muted, wrapping rather than cut.</summary>
    /// <param name="split">The place on one line and the day under it (the PB badge beside them takes the room a single line needs).</param>
    static FrameworkElement Slim(string where, string day, FrameworkElement? art, double indent, bool split = false)
    {
        // a dock, not a row: the words get the room left beside the outcome and the PB badge, and wrap in it rather than run under them
        var row = new DockPanel { LastChildFill = true, Margin = new Thickness(indent, art == null && indent > 0 ? 2 : 0, 0, 0), Tag = "share-sub" };
        if (art != null) { art.Margin = new Thickness(0, 0, 9, 0); art.VerticalAlignment = VerticalAlignment.Center; DockPanel.SetDock(art, Dock.Left); row.Children.Add(art); }
        var t = new TextBlock { FontSize = 12.5, FontWeight = FontWeights.Medium, Foreground = HudKit.Muted, VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap, MaxWidth = Width - 2 * Side - 60 };
        if (where.Length > 0) { t.Inlines.Add(new Run(where)); t.Inlines.Add(split ? new LineBreak() : new Run("  ·  ") { Foreground = HudKit.Faint }); }
        t.Inlines.Add(new Run(day) { Foreground = HudKit.Faint });
        row.Children.Add(t);
        return row;
    }

    /// <summary>How it ended and how long it took ("Killed in 2:31"), on a pill of its colour.</summary>
    static Border Pill(string text, SolidColorBrush ink)
    {
        var c = ink.Color;
        var pill = new Border
        {
            CornerRadius = new CornerRadius(8), Padding = new Thickness(10, 3, 11, 4), Margin = new Thickness(14, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center,
            Background = new SolidColorBrush(Color.FromArgb(0x24, c.R, c.G, c.B)), BorderBrush = new SolidColorBrush(Color.FromArgb(0x80, c.R, c.G, c.B)), BorderThickness = new Thickness(1),
            Child = new TextBlock { Text = text, FontSize = 13, FontWeight = FontWeights.Bold, Foreground = ink, Tag = "share-outcome" },
        };
        Typography.SetNumeralAlignment(pill, FontNumeralAlignment.Tabular);
        return pill;
    }

    // ---- the personal best (Share this pull, 1.0.36) ----

    /// <summary>A personal best a pull set: how far past your best before it, DPS and healing a second (null: not beaten).</summary>
    public sealed record Pb(double? Dps, double? Hps);

    /// <summary>
    /// The record this pull beat, measured as the meter's own PB chip is (Ui/ParserRows.cs): a kill, your figures against your
    /// best before this pull on the same fight (<paramref name="before"/>: FightRecords, the boss in that instance and mode).
    /// Null when nothing was beaten, or there was no best before it (a first kill is no record).
    /// </summary>
    public static Pb? PbOf(Encounter e, PullRef? before)
    {
        if (e.Live || e.Outcome != "kill" || e.You is not { } you || before is null) return null;
        var end = e.End ?? e.LastAt;
        double? d = before.Dps > 0 && e.Dps(you, end) / before.Dps - 1 is > 0 and var dd ? dd : null;
        double? h = before.Hps > 0 && e.HpsOf(you, end) is { } hps && hps / before.Hps - 1 is > 0 and var hh ? hh : null;
        return d == null && h == null ? null : new Pb(d, h);
    }

    /// <summary>A crown, drawn 16 × 13: three points on a band, a bar under it.</summary>
    const string CrownMark = "M1,9.5 L2,2.5 L5.6,5.9 L8,0.8 L10.4,5.9 L14,2.5 L15,9.5 Z M1.5,11.6 L14.5,11.6 L14.5,12.6 L1.5,12.6 Z";

    /// <summary>
    /// The head's gold badge: a crown and "NEW PERSONAL BEST" (healing alone: "NEW HEALING BEST") on a gold gradient with a soft
    /// glow, and how far past the best ("+8.0%") when the meter doesn't say it on your row already ("PB +8.0%"): said once.
    /// </summary>
    /// <param name="said">Whether the meter's face already says a text.</param>
    static Border Badge(Pb pb, Func<string, bool> said)
    {
        var ink = new SolidColorBrush(Color.FromRgb(0x2B, 0x1D, 0x04));
        var crown = new Path
        {
            Data = Geometry.Parse(CrownMark), Width = 16, Height = 13, Stretch = Stretch.None, Fill = ink,
            VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 7, 0),
        };
        var words = new TextBlock
        {
            Text = pb.Dps != null ? L.T("NEW PERSONAL BEST") : L.T("NEW HEALING BEST"), FontSize = 12, FontWeight = FontWeights.Black, Foreground = ink,
            VerticalAlignment = VerticalAlignment.Center, Tag = "share-pb-text",
        };
        var line = new StackPanel { Orientation = Orientation.Horizontal, Children = { crown, words } };
        // the figure: DPS when that's the record (the healing beside it when both), else the healing
        string figure = pb.Dps is { } d ? PK.Signed(d) + (pb.Hps is { } h0 ? "  ·  " + L.F("HEAL {0}", PK.Signed(h0)) : "") : PK.Signed(pb.Hps ?? 0);
        if (!said(ParserRows.PbText(pb.Dps, pb.Hps)))
        {
            line.Children.Add(new Border { Width = 1, Margin = new Thickness(9, 2, 9, 2), Background = new SolidColorBrush(Color.FromArgb(0x55, 0x2B, 0x1D, 0x04)) });
            var fig = new TextBlock { Text = figure, FontSize = 12.5, FontWeight = FontWeights.Black, Foreground = ink, VerticalAlignment = VerticalAlignment.Center, Tag = "share-pb-figure" };
            Typography.SetNumeralAlignment(fig, FontNumeralAlignment.Tabular);
            line.Children.Add(fig);
        }
        var fill = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 1) };
        fill.GradientStops.Add(new GradientStop(Color.FromRgb(0xFF, 0xE6, 0x9A), 0));
        fill.GradientStops.Add(new GradientStop(Color.FromRgb(0xF5, 0xC2, 0x4C), 0.48));
        fill.GradientStops.Add(new GradientStop(Color.FromRgb(0xD9, 0x95, 0x22), 1));
        return new Border
        {
            CornerRadius = new CornerRadius(8), Padding = new Thickness(10, 4, 12, 5), Margin = new Thickness(12, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center,
            Background = fill, BorderBrush = new SolidColorBrush(Color.FromRgb(0xFF, 0xEF, 0xBF)), BorderThickness = new Thickness(1),
            Effect = new System.Windows.Media.Effects.DropShadowEffect { Color = Color.FromRgb(0xF5, 0xC2, 0x4C), BlurRadius = 16, ShadowDepth = 0, Opacity = 0.55 },
            Child = line, Tag = "share-pb",
        };
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

    // ---- no cut-off label (the coordinator, 2026-11-17: "No cut-off labels anywhere") ----

    /// <summary>How much wider the meter is tried at each step until no name or label on it is cut off.</summary>
    const double WidthStep = 20;

    static void LayOut(FrameworkElement el, double width = double.PositiveInfinity)
    {
        el.Measure(new Size(width, double.PositiveInfinity));
        el.Arrange(new Rect(0, 0, double.IsInfinity(width) ? el.DesiredSize.Width : width, el.DesiredSize.Height));
        el.UpdateLayout();
    }

    /// <summary>
    /// The picture laid out, settled, handed to <paramref name="use"/> while it is still at its size. The meter is drawn at the
    /// player's width, and a step wider each time until nothing on it ends cut off ("Member 4" with a heal chip in Neon), up
    /// to the widest meter; only past the frame's room does the frame grow.
    /// </summary>
    static T With<T>(Encounter e, DateTime now, Look look, bool hideNames, Func<Border, T> use, PullRef? before = null)
    {
        bool still0 = Motion.Still; Motion.Still = true;
        double w0 = HudStackLook.FightWidth;
        // the PB ghost stays on the meter over the game: the picture has its own PB badge, and never says "off your best"
        bool ghost0 = PbGhost.Shown; PbGhost.Shown = false;
        try
        {
            var shown = hideNames ? Hidden(e) : e;
            FrameworkElement face;
            for (double w = w0; ; w = Math.Min(HudStackLook.FightWidthMax, w + WidthStep))
            {
                HudStackLook.FightWidth = w;
                face = ParserOverlay.MeterFace(look.Key, shown, now, look.Mode, look.Details, look.Radius, look.Pad);
                LayOut(face);
                if (Trimmed(face).Count == 0 || HudStackLook.FightWidth >= HudStackLook.FightWidthMax) break;
            }
            var v = Frame(e, now, face, pb: PbOf(e, before));
            LayOut(v, v.Width);
            IconCache.Drain(TimeSpan.FromSeconds(2));
            v.UpdateLayout();
            return use(v);
        }
        finally { HudStackLook.FightWidth = w0; Motion.Still = still0; PbGhost.Shown = ghost0; }
    }

    /// <summary>The picture laid out at its size (600 wide or more, as tall as it needs, 315 at least), settled.</summary>
    /// <param name="before">Your best on this fight before this pull (the records kept): a beaten one puts the PB badge in the head.</param>
    public static Border Laid(Encounter e, DateTime now, Look look, bool hideNames, PullRef? before = null) => With(e, now, look, hideNames, v => v, before);

    /// <summary>The PNG: the picture drawn at twice its size (1200 px wide), settled; never a screenshot.</summary>
    public static BitmapSource Render(Encounter e, DateTime now, Look look, bool hideNames, PullRef? before = null) => With(e, now, look, hideNames, v => Snap(v, "meter share picture"), before);

    /// <summary>A picture laid out at its size, drawn at twice it (whole pixels), its words checked for the language in use.</summary>
    internal static BitmapSource Snap(Border v, string what)
    {
        int h = (int)Math.Ceiling(v.ActualHeight) * 2;   // twice the height, whole pixels
        var rtb = new RenderTargetBitmap((int)Math.Round(v.Width * 2), h, 192, 192, PixelFormats.Pbgra32);
        LangCheck.Scan(v, what);
        rtb.Render(v);
        rtb.Freeze();
        return rtb;
    }

    /// <summary>
    /// The same frame (head, glow, foot) round any face already built at its width: the Parser page's views (Ui/ParserShare.cs).
    /// Laid out settled, handed to <paramref name="use"/> while it is still at its size.
    /// </summary>
    internal static T Framed<T>(Encounter e, DateTime now, Func<FrameworkElement> build, string tag, Func<Border, T> use)
    {
        bool still0 = Motion.Still; Motion.Still = true;
        try
        {
            var face = build();
            LayOut(face);
            var v = Frame(e, now, face, view: true);
            v.Tag = tag;
            LayOut(v, v.Width);
            IconCache.Drain(TimeSpan.FromSeconds(2));
            v.UpdateLayout();
            return use(v);
        }
        finally { Motion.Still = still0; }
    }

    /// <summary>The words of every text drawn under <paramref name="root"/> (a part collapsed left out), its runs joined.</summary>
    internal static List<string> Texts(DependencyObject root) => Blocks(root).Select(Plain).Where(s => s.Length > 0).ToList();

    static IEnumerable<TextBlock> Blocks(DependencyObject d)
    {
        if (d is UIElement u && u.Visibility != Visibility.Visible) yield break;
        if (d is TextBlock t) yield return t;
        if (d is Visual) for (int i = 0; i < VisualTreeHelper.GetChildrenCount(d); i++) foreach (var x in Blocks(VisualTreeHelper.GetChild(d, i))) yield return x;
    }

    static IEnumerable<ImageSource> Images(DependencyObject d)
    {
        if (d is UIElement u && u.Visibility != Visibility.Visible) yield break;
        if (d is Image { Source: { } s }) yield return s;
        if (d is Visual) for (int i = 0; i < VisualTreeHelper.GetChildrenCount(d); i++) foreach (var x in Images(VisualTreeHelper.GetChild(d, i))) yield return x;
    }

    static string Plain(TextBlock tb)
    {
        if (tb.Inlines.Count == 0) return tb.Text;
        var sb = new System.Text.StringBuilder();
        void Add(InlineCollection ins) { foreach (var i in ins) { if (i is Run r) sb.Append(r.Text); else if (i is Span s) Add(s.Inlines); else if (i is LineBreak) sb.Append(' '); } }
        Add(tb.Inlines);
        return sb.ToString();
    }

    /// <summary>
    /// The texts drawn cut off under <paramref name="root"/>: one that ends in "…", or one whose trimming cut it (its words
    /// need more room than it was given). Measured on a copy with the same face, size, weight and runs.
    /// </summary>
    internal static List<string> Trimmed(DependencyObject root)
    {
        var cut = new List<string>();
        foreach (var tb in Blocks(root))
        {
            var text = Plain(tb);
            if (text.Length == 0 || tb.ActualWidth <= 0) continue;
            if (text.TrimEnd().EndsWith('…') || text.TrimEnd().EndsWith("...", StringComparison.Ordinal)) { cut.Add(text); continue; }
            if (tb.TextTrimming == TextTrimming.None) continue;
            var copy = new TextBlock { FontFamily = tb.FontFamily, FontSize = tb.FontSize, FontWeight = tb.FontWeight, FontStyle = tb.FontStyle, FontStretch = tb.FontStretch, Padding = tb.Padding };
            TextOptions.SetTextFormattingMode(copy, TextOptions.GetTextFormattingMode(tb));
            Typography.SetNumeralAlignment(copy, Typography.GetNumeralAlignment(tb));
            if (tb.Inlines.Count == 0) copy.Text = tb.Text;
            else
            {
                void Copy(InlineCollection ins)
                {
                    foreach (var i in ins)
                    {
                        if (i is Run r) copy.Inlines.Add(new Run(r.Text) { FontFamily = r.FontFamily, FontSize = r.FontSize, FontWeight = r.FontWeight, FontStyle = r.FontStyle });
                        else if (i is Span s) Copy(s.Inlines);
                    }
                }
                Copy(tb.Inlines);
            }
            copy.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            if (copy.DesiredSize.Width > tb.ActualWidth + 1) cut.Add(text);
        }
        return cut;
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
