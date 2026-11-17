using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using KinahLedger.Parser;
using KinahLedger.Session;

namespace KinahLedger.Ui;

/// <summary>
/// The Runs page and the Parser as one app (Parser v2, Steven 2026-11-15): a run's boss pulls listed under its card on the
/// Runs page, and a click on one flies its row into the Parser's view of that pull (the row lifts and grows into the fight's
/// hero while the Runs page slides out and the Parser comes in); the Parser's back link then goes back to the run.
/// </summary>
public partial class MainWindow
{
    /// <summary>The Parser was opened from a run on the Runs page: its back link returns there.</summary>
    bool _parserFromRuns;

    /// <summary>
    /// The boss pulls of a run: the kept pulls that started while it lasted, and the one under way when the run is open. Null
    /// when it had none (the card isn't drawn), or the Parser is off.
    /// </summary>
    internal FrameworkElement? RunBossFights(InstanceRun? run, bool open)
    {
        if (run == null || ParserReadingOff) return null;
        var from = run.Start.AddMinutes(-1);
        var to = open ? DateTime.UtcNow.AddMinutes(1) : (run.Left ?? run.LastLoot ?? run.Start.AddSeconds(Math.Max(600, run.Seconds))).AddMinutes(1);
        if (_demo) { from = DateTime.MinValue; to = DateTime.MaxValue; }
        var pulls = ParserPast().Where(e => e.ModeKind == ParserMode.Bosses && e.HasDamage && e.Start >= from && e.Start <= to && (!_demo || (e.Instance == ParserLive?.Instance && e.Start >= ParserLive.Start.AddHours(-1)))).OrderBy(e => e.Start).ToList();
        var live = open && ParserLive is { ModeKind: ParserMode.Bosses, Live: true } l ? l : null;
        if (pulls.Count == 0 && live == null) return null;
        var card = PCard(hero: false);
        card.Margin = new Thickness(0, 8, 0, 0);
        var body = new StackPanel();
        var place = BossBook.Strip((live ?? pulls[^1]).Instance);
        body.Children.Add(QHead("Shield", L.F("Boss fights  ·  {0}", pulls.Count + (live != null ? 1 : 0)), place.Length > 0 ? L.F("{0} · group DPS", place) : L.T("group DPS")));
        var rows = pulls.Select(e => (E: e, Live: false)).ToList();
        if (live != null) rows.Add((live, true));
        for (int i = 0; i < rows.Count; i++)
        {
            var (e, isLive) = rows[i];
            var g = new Grid { MinHeight = 38 };
            foreach (var w in new[] { 30.0, -1, 64, 82, 18 }) g.ColumnDefinitions.Add(new ColumnDefinition { Width = w < 0 ? new GridLength(1, GridUnitType.Star) : new GridLength(w) });
            var tone = isLive ? "Gold" : e.Outcome == "kill" ? "Mint" : e.Outcome == "wipe" ? "Coral" : "Muted";
            var mark = new Border { Width = 20, Height = 20, CornerRadius = Skins.Corner(10), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center };
            mark.SetResourceReference(Border.BackgroundProperty, isLive ? "GoldTint" : e.Outcome == "kill" ? "MintTint" : e.Outcome == "wipe" ? "CoralTint" : "ChipBg");
            var mt = PT(isLive ? "●" : e.Outcome == "kill" ? "✓" : e.Outcome == "wipe" ? "✕" : "·", isLive ? 8 : 11, FontWeights.Bold, tone); mt.HorizontalAlignment = HorizontalAlignment.Center;
            mark.Child = mt;
            g.Children.Add(mark);
            var name = new TextBlock { VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
            var r1 = new Run(e.Boss) { FontSize = 13.5, FontWeight = FontWeights.SemiBold }; r1.SetResourceReference(TextElement.ForegroundProperty, "Ink"); name.Inlines.Add(r1);
            var sub = isLive ? "   " + L.T("under way") : e.FinalBoss ? "   " + L.T("final boss") : "";
            if (sub.Length > 0) { var r2 = new Run(sub) { FontSize = 12, FontWeight = FontWeights.Medium }; r2.SetResourceReference(TextElement.ForegroundProperty, isLive ? "Gold" : "Muted"); name.Inlines.Add(r2); }
            Grid.SetColumn(name, 1); g.Children.Add(name);
            var end = isLive ? ParserNow : e.End ?? e.LastAt;
            var ck = Right(PT(BossParser.Clock(e.Duration(end)), 12.5, FontWeights.SemiBold, isLive ? "Gold" : "Text2")); Tab(ck); Grid.SetColumn(ck, 2); g.Children.Add(ck);
            var gd = Right(PT(Math.Round(e.GroupDps(end)).ToString("N0", CultureInfo.InvariantCulture), 14, FontWeights.Bold, "Ink")); Tab(gd); Grid.SetColumn(gd, 3); g.Children.Add(gd);
            var chev = new System.Windows.Shapes.Path { Data = Geometry.Parse("M0,0 L3.5,3.5 L0,7"), StrokeThickness = 1.5, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round };
            chev.SetResourceReference(System.Windows.Shapes.Shape.StrokeProperty, "Link");
            Grid.SetColumn(chev, 4); g.Children.Add(chev);
            var row = new Border { Child = g, CornerRadius = new CornerRadius(8), Padding = new Thickness(6, 0, 6, 0), Margin = new Thickness(-6, 0, -6, 0), Background = Brushes.Transparent, Cursor = Cursors.Hand, Focusable = true, BorderThickness = new Thickness(0, i > 0 ? 1 : 0, 0, 0), Tag = "run-boss" };
            row.SetResourceReference(Border.BorderBrushProperty, "CardHair");
            AutomationProperties.SetName(row, $"{e.Boss}: open it in the Parser");
            row.MouseEnter += (_, _) => row.SetResourceReference(Border.BackgroundProperty, "HoverTint");
            row.MouseLeave += (_, _) => row.Background = Brushes.Transparent;
            string? id = isLive ? null : e.Id;
            row.MouseLeftButtonUp += (_, _) => JumpToFight(row, id);
            row.KeyDown += (_, k) => { if (k.Key is Key.Enter or Key.Space) { k.Handled = true; JumpToFight(row, id); } };
            body.Children.Add(row);
        }
        card.Child = body;
        return card;
    }

    /// <summary>Whether the Parser can't see fights (its setting off): then a run lists no boss fights.</summary>
    bool ParserReadingOff => !_demo && _watcher.Packets is { } pr && !pr.On;

    /// <summary>The flight's picture layer over the window: the Runs page's body leaving, the row's box growing into the hero.</summary>
    sealed class JumpGhost : Adorner
    {
        public BitmapSource? From, Row, Hero; public Rect Body, R0, R1; public double T; public Brush? Card; public Pen? Line;
        public JumpGhost(UIElement on) : base(on) { IsHitTestVisible = false; }
        protected override void OnRender(DrawingContext dc)
        {
            double e = Motion.Out(T), fly = Motion.Out(Math.Clamp(T / 0.75, 0, 1));
            if (From != null)
            {
                dc.PushClip(new CombinedGeometry(GeometryCombineMode.Exclude, new RectangleGeometry(Body), new RectangleGeometry(R0)));
                dc.PushOpacity(1 - Motion.Out(Math.Clamp(T / 0.3, 0, 1)));
                dc.DrawImage(From, new Rect(-28 * e, 0, From.Width, From.Height));
                dc.Pop(); dc.Pop();
            }
            var gr = new Rect(R0.X + (R1.X - R0.X) * fly, R0.Y + (R1.Y - R0.Y) * fly, R0.Width + (R1.Width - R0.Width) * fly, R0.Height + (R1.Height - R0.Height) * fly);
            double rad = 8 + 6 * fly;
            dc.DrawRoundedRectangle(Card, Line, gr, rad, rad);
            dc.PushClip(new RectangleGeometry(gr, rad, rad));
            if (Row != null) { dc.PushOpacity(1 - Motion.Out(Math.Clamp(T / 0.35, 0, 1))); dc.DrawImage(Row, new Rect(gr.X, gr.Y, R0.Width, R0.Height)); dc.Pop(); }
            if (Hero != null) { dc.PushOpacity(Motion.Out(Math.Clamp((T - 0.25) / 0.6, 0, 1))); dc.DrawImage(Hero, new Rect(gr.X, gr.Y, R1.Width, R1.Height)); dc.Pop(); }
            dc.Pop();
        }
    }

    static BitmapSource SnapEl(FrameworkElement el)
    {
        double w = Math.Max(1, el.ActualWidth), h = Math.Max(1, el.ActualHeight);
        var dv = new DrawingVisual();
        using (var dc = dv.RenderOpen()) dc.DrawRectangle(new VisualBrush(el) { ViewboxUnits = BrushMappingMode.Absolute, Viewbox = new Rect(0, 0, w, h), Stretch = Stretch.Fill }, null, new Rect(0, 0, w, h));
        var rtb = new RenderTargetBitmap((int)Math.Ceiling(w), (int)Math.Ceiling(h), 96, 96, PixelFormats.Pbgra32);
        LangCheck.Scan(el, "parser jump"); rtb.Render(dv); rtb.Freeze();
        return rtb;
    }

    static BitmapSource Crop(BitmapSource b, Rect r)
    {
        int x = Math.Clamp((int)Math.Floor(r.X), 0, b.PixelWidth - 1), y = Math.Clamp((int)Math.Floor(r.Y), 0, b.PixelHeight - 1);
        int w = Math.Clamp((int)Math.Ceiling(r.Width), 1, b.PixelWidth - x), h = Math.Clamp((int)Math.Ceiling(r.Height), 1, b.PixelHeight - y);
        var c = new CroppedBitmap(b, new Int32Rect(x, y, w, h)); c.Freeze();
        return c;
    }

    /// <summary>
    /// A boss pull's row on the Runs page opens it in the Parser (<paramref name="fightId"/> null: the pull under way). With
    /// motion, the row flies into the fight's hero (one 460 ms course); without, the Parser simply shows it.
    /// </summary>
    internal void JumpToFight(FrameworkElement row, string? fightId)
    {
        var root = (FrameworkElement)Content;
        bool fly = Motion.On && row.IsVisible;
        BitmapSource? from = null; Rect r0 = default, body = default;
        if (fly)
        {
            from = SnapEl(root);
            r0 = row.TransformToAncestor(root).TransformBounds(new Rect(0, 0, row.ActualWidth, row.ActualHeight));
            body = new Rect(0, LowerScroll.TransformToAncestor(root).Transform(new Point(0, 0)).Y, root.ActualWidth, root.ActualHeight);
        }
        _parserFromRuns = true;
        _parserInspect = null; _parserTab = "Group";
        _parserOpen = fightId;
        ShowPart("Parser", byHand: true);
        RedrawParserPage();
        LowerScroll.ScrollToTop();
        if (!fly || from == null) return;
        UpdateLayout();
        var hero = FindTagged(ParserPage, "parser-hero");
        var layer = AdornerLayer.GetAdornerLayer(root);
        if (hero == null || layer == null) return;
        var r1 = hero.TransformToAncestor(root).TransformBounds(new Rect(0, 0, hero.ActualWidth, hero.ActualHeight));
        var heroPic = Crop(SnapEl(root), r1);
        var ghost = new JumpGhost(root) { From = from, Row = Crop(from, r0), Hero = heroPic, Body = body, R0 = r0, R1 = r1, Card = TryFindResource("Card") as Brush, Line = new Pen(TryFindResource("LineStrong") as Brush ?? Brushes.Gray, 1) };
        layer.Add(ghost);
        var slide = new TranslateTransform(28, 0);
        ParserPage.RenderTransform = slide;
        hero.Opacity = 0;
        Motion.Play(ParserPage, 460, p =>
        {
            ghost.T = p; ghost.InvalidateVisual();
            double e = Motion.Out(Math.Clamp((p - 0.35) / 0.65, 0, 1));
            ParserPage.Opacity = e; slide.X = 28 * (1 - Motion.Out(p));
        }, () => { layer.Remove(ghost); ParserPage.Opacity = 1; slide.X = 0; hero.Opacity = 1; });
    }

    /// <summary>The Parser's back link after a jump: the Runs page again, sliding in from the left.</summary>
    internal void BackToRuns()
    {
        _parserFromRuns = false; _parserOpen = null; _parserInspect = null;
        ShowPart("Events", byHand: true);
        if (!Motion.On) return;
        var slide = new TranslateTransform(-28, 0);
        LowerScroll.RenderTransform = slide;
        Motion.Play(LowerScroll, 320, p => { slide.X = -28 * (1 - Motion.Out(p)); LowerScroll.Opacity = 0.2 + 0.8 * Motion.Out(p); }, () => { slide.X = 0; LowerScroll.Opacity = 1; LowerScroll.RenderTransform = null; });
    }

    static FrameworkElement? FindTagged(DependencyObject root, string tag)
    {
        if (root is FrameworkElement fe && Equals(fe.Tag, tag)) return fe;
        foreach (var ch in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>()) if (FindTagged(ch, tag) is { } f) return f;
        return null;
    }
}
