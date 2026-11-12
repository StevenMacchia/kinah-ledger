using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Shapes;

namespace KinahLedger.Ui;

/// <summary>
/// A rare drop is a moment (2026-11-11, Steven: "cool and flashy", "amazing and professional and well done"). When a
/// Legend, Unique or Epic item drops, or one on the watch list, the window shows it for a second and a half over the
/// page: the item's tile lands large in its grade's frame with a ring bursting out from it and a band of light running
/// across the card in the grade's colour, "RARE DROP · LEGEND", the name and what it sells for, then the card lifts away
/// and the page is as it was. It sits on a layer over the page (never a row of its own, so nothing moves), takes no click
/// and never the focus, and plays only while the window shows.
/// The drops alerted in one tick are one moment, the best grade with "+2 more"; a drop while one is held joins it (a
/// better grade takes the lead and plays again, a lesser one counts into "+3 more"), and the overlay's Card and Ticker
/// hold the same lead and count (Ui/HudRare.cs). Never on a demo, a character switch or a drop read a while ago (a
/// history load or a restart counts nothing new, AlertTick's watermark); never with motion off (Ui/Motion.cs).
/// </summary>
public partial class MainWindow
{
    /// <summary>One drop AlertFor alerted, with what the panel holds for it.</summary>
    sealed record RareFind(long Id, string Name, string Count, string Grade, Color Color, DateTime At, string Price, string Where, bool Watched, bool Chime);

    /// <summary>The drops alerted in this tick, the one the moment and the panel show (the best grade), how many more came with it, and until when another joins it.</summary>
    readonly List<RareFind> _rareTick = new();
    RareFind? _rareLead;
    int _rareMore;
    DateTime _rareJoinUntil = DateTime.MinValue;
    /// <summary>Whether the lead was taken in a demo: a lead from before the window turned into a demo (or out of one) is forgotten.</summary>
    bool _rareLeadDemo;

    /// <summary>The moment takes this long, start to gone.</summary>
    const double MomentMs = 1500;
    /// <summary>The least room kept between the card and the window's sides.</summary>
    const double MomentGutter = 16;
    /// <summary>A drop read longer ago than this is no moment (a pickup held for a second read is well inside it).</summary>
    const double MomentFreshSeconds = 10;
    /// <summary>The card's width (narrower in a narrow window, down to its least), the tile's side, and how far the ring grows past the tile, as a share of it.</summary>
    const double MomentWidth = 440, MomentMinWidth = 240, MomentTile = 64, MomentRingGrow = 1.3, MomentRingStroke = 2.5;
    /// <summary>The band of light's width, its strongest share of the grade's colour, and its slant in degrees.</summary>
    const double MomentSheenWidth = 120, MomentSheenAlpha = 0.32, MomentSheenSlant = -18;
    /// <summary>The light behind the card: its strength at the middle, and how far it reaches past the card's sides (three quarters of that above and below).</summary>
    const double MomentAuraAlpha = 0.42, MomentAuraReach = 110;
    /// <summary>The shadow under the card: its softness, its drop and its strength.</summary>
    const double MomentShadowBlur = 28, MomentShadowDepth = 6, MomentShadowOpacity = 0.4;

    /// <summary>A drop AlertFor alerted: the tick's end (<see cref="RareTickDone"/>) makes one moment of all of them.</summary>
    void NoteRare(RareFind f) => _rareTick.Add(f);

    /// <summary>
    /// No drop held for the moment and none waiting (AlertTick's start, a character switch): a drop from before can't lead
    /// the next moment or count into its "+2 more".
    /// </summary>
    void ForgetRare(bool held = true)
    {
        _rareTick.Clear();
        if (!held) return;
        _rareLead = null;
        _rareMore = 0;
        _rareJoinUntil = DateTime.MinValue;
    }

    /// <summary>
    /// AlertTick's end: the tick's drops become one moment. The best grade leads (the first of equals); while a lead is
    /// held a better one takes over and plays, a lesser one is counted with it. The panel is held on the lead with the
    /// count ("rareMore"), since AlertFor held each drop in turn.
    /// </summary>
    void RareTickDone()
    {
        if (_rareTick.Count == 0) return;
        var best = _rareTick.MaxBy(f => GradeRank(f.Grade))!;
        int more = _rareTick.Count - 1;
        _rareTick.Clear();
        var now = DateTime.UtcNow;
        bool leads = true;
        if (_rareLeadDemo != _demo) { _rareLead = null; _rareLeadDemo = _demo; }
        if (_rareLead != null && now < _rareJoinUntil)
        {
            more += 1 + _rareMore;
            if (GradeRank(best.Grade) <= GradeRank(_rareLead.Grade)) { best = _rareLead; leads = false; }
        }
        _rareLead = best;
        _rareMore = more;
        _rareJoinUntil = now.AddSeconds(HudRareSeconds);
        HoldRareOnHud(best.Name, best.Count, best.Grade, best.Color, best.At);
        HoldRareExtras(best.Id, best.Price, best.Where, best.Watched, best.Chime);
        _hudRare["rareMore"] = more > 0 ? more.ToString() : "";
        if (!leads) _moment?.SetMore(more);
        else if (MomentWanted(best)) ShowMoment(best, more);
    }

    /// <summary>Whether a drop plays its moment: the tracker reading for real, the window showing, motion on, and the drop just read.</summary>
    bool MomentWanted(RareFind f) =>
        !_demo && !_switchingCharacter && Motion.On && IsVisible && WindowState != WindowState.Minimized && (DateTime.UtcNow - f.At).TotalSeconds <= MomentFreshSeconds;

    /* ---------------- the moment ---------------- */

    /// <summary>The layer the moment shows on: no size of its own at the page's centre, so it never moves the page; made once.</summary>
    Canvas? _momentLayer;
    RareMomentCard? _moment;

    /// <summary>The moment for a drop: a moment under way gives way to it.</summary>
    void ShowMoment(RareFind f, int more)
    {
        if (_momentLayer == null)
        {
            _momentLayer = new Canvas { Width = 0, Height = 0, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, IsHitTestVisible = false, Focusable = false };
            Panel.SetZIndex(_momentLayer, int.MaxValue);
            FitHost.Children.Add(_momentLayer);
        }
        var layer = _momentLayer;
        if (_moment != null) { Motion.Halt(_moment.Stage); layer.Children.Remove(_moment.Stage); }
        var width = Math.Min(MomentWidth, Math.Max(MomentMinWidth, FitHost.ActualWidth - 2 * MomentGutter));
        var card = new RareMomentCard(f, more, width, QItemIcon(f.Name, f.Id, MomentTile, round: false, grade: f.Grade), VisualTreeHelper.GetDpi(this).DpiScaleX);
        _moment = card;
        layer.Visibility = Visibility.Visible;
        layer.Children.Add(card.Stage);
        card.Stage.Measure(new Size(width, double.PositiveInfinity));
        Canvas.SetLeft(card.Stage, -width / 2);
        Canvas.SetTop(card.Stage, -card.Stage.DesiredSize.Height / 2);
        card.Step(0);
        Motion.Play(card.Stage, MomentMs, card.Step, () =>
        {
            layer.Children.Remove(card.Stage);
            if (_moment == card) { _moment = null; layer.Visibility = Visibility.Collapsed; }
        });
    }

    /// <summary>
    /// The moment's card, in the skin's brushes (Night, Abyss, Daylight): the tile in its grade's frame on the left with
    /// the ring over it, the label (and "+2 more"), the name in the grade's colour and the price; a soft light of the
    /// grade's colour behind it, and the band of light inside its rounded edge.
    /// </summary>
    sealed class RareMomentCard
    {
        public Grid Stage { get; }
        readonly ScaleTransform _scale = new(1, 1), _tileScale = new(1, 1);
        readonly TranslateTransform _lift = new(), _sheenAt = new();
        readonly FrameworkElement _tile, _aura, _sheen;
        readonly Ellipse _ring;
        readonly TextBlock _label;
        readonly Border _morePill;
        readonly TextBlock _moreText;
        readonly Run? _figure;
        readonly string _price;
        readonly double _width;

        /// <param name="dpi">The screen's scale, which the shadow's cached picture is drawn at.</param>
        public RareMomentCard(RareFind f, int more, double width, FrameworkElement tile, double dpi)
        {
            _width = width;
            _price = f.Price;
            var tone = f.Grade is "Rare" or "Legend" or "Unique" or "Epic" or "Special" ? "Grade" + f.Grade : "Gold";
            var c = Application.Current?.TryFindResource(tone) is SolidColorBrush b ? b.Color : f.Color;
            static Color A(Color c, double a) => Color.FromArgb((byte)Math.Round(255 * a), c.R, c.G, c.B);

            // the light behind the card: the grade's colour fading out past its edges
            var auraFill = new RadialGradientBrush { GradientOrigin = new Point(0.5, 0.5) };
            auraFill.GradientStops.Add(new GradientStop(A(c, MomentAuraAlpha), 0));
            auraFill.GradientStops.Add(new GradientStop(A(c, MomentAuraAlpha * 0.4), 0.55));
            auraFill.GradientStops.Add(new GradientStop(A(c, 0), 1));
            auraFill.Freeze();
            _aura = new Ellipse { Fill = auraFill, Margin = new Thickness(-MomentAuraReach, -MomentAuraReach * 0.75, -MomentAuraReach, -MomentAuraReach * 0.75), IsHitTestVisible = false };

            _tile = tile;
            tile.RenderTransformOrigin = new Point(0.5, 0.5);
            tile.RenderTransform = _tileScale;
            _ring = Motion.BurstRing(c, MomentTile, MomentRingStroke);

            _label = new TextBlock { Text = HudRare.LabelOf(f.Grade, f.Watched), FontSize = 11.5, FontWeight = FontWeights.Bold, VerticalAlignment = VerticalAlignment.Center };
            _label.SetResourceReference(TextBlock.ForegroundProperty, tone);
            _moreText = new TextBlock { FontSize = 11.5, FontWeight = FontWeights.SemiBold };
            _moreText.SetResourceReference(TextBlock.ForegroundProperty, "Muted");
            _morePill = new Border { CornerRadius = new CornerRadius(9), Padding = new Thickness(8, 1, 8, 2), BorderThickness = new Thickness(1), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0), Child = _moreText };
            _morePill.SetResourceReference(Border.BackgroundProperty, "Card2");
            _morePill.SetResourceReference(Border.BorderBrushProperty, "Line");
            SetMore(more);
            var head = new Grid();
            head.ColumnDefinitions.Add(new ColumnDefinition());
            head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            head.Children.Add(_label);
            Grid.SetColumn(_morePill, 1); head.Children.Add(_morePill);

            var name = new TextBlock { FontSize = 20, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap, TextTrimming = TextTrimming.CharacterEllipsis, LineHeight = 24, LineStackingStrategy = LineStackingStrategy.BlockLineHeight, MaxHeight = 48, Margin = new Thickness(0, 3, 0, 0) };   // a long name takes a second line, never a third
            name.SetResourceReference(TextBlock.FontFamilyProperty, "DisplayFont");
            name.SetResourceReference(TextBlock.ForegroundProperty, tone);
            name.Inlines.Add(new Run(f.Name));
            if (f.Count is { Length: > 0 } n && n != "×1") { var cnt = new Run(" " + n) { FontSize = 14, FontWeight = FontWeights.Medium }; cnt.SetResourceReference(TextElement.ForegroundProperty, "Muted"); name.Inlines.Add(cnt); }

            var price = new TextBlock { FontSize = 13, Margin = new Thickness(0, 4, 0, 0), TextTrimming = TextTrimming.CharacterEllipsis };
            Run Part(string text, string brush, bool strong = false) { var r = new Run(text) { FontWeight = strong ? FontWeights.SemiBold : FontWeights.Normal }; r.SetResourceReference(TextElement.ForegroundProperty, brush); price.Inlines.Add(r); return r; }
            if (f.Price.Length == 0) Part("no price yet", "Faint");
            else
            {
                Part("sells for ", "Muted");
                _figure = Part(f.Price, "Gold", strong: true);
                if (f.Where.Length > 0) Part(" " + f.Where, "Muted");
            }
            var words = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(16, 0, 0, 0), Children = { head, name, price } };

            var content = new Grid { Margin = new Thickness(18, 16, 18, 16) };
            content.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            content.ColumnDefinitions.Add(new ColumnDefinition());
            content.Children.Add(tile);
            Grid.SetColumn(words, 1); content.Children.Add(words);

            // the band of light: the grade's colour, strongest in its middle, slanted, run across inside the card's edge
            var band = new LinearGradientBrush { StartPoint = new Point(0, 0.5), EndPoint = new Point(1, 0.5) };
            band.GradientStops.Add(new GradientStop(A(c, 0), 0));
            band.GradientStops.Add(new GradientStop(A(c, MomentSheenAlpha), 0.5));
            band.GradientStops.Add(new GradientStop(A(c, 0), 1));
            band.Freeze();
            _sheen = new Rectangle
            {
                Width = MomentSheenWidth, Fill = band, HorizontalAlignment = HorizontalAlignment.Left, IsHitTestVisible = false, Opacity = 0,
                RenderTransform = new TransformGroup { Children = { new SkewTransform(MomentSheenSlant, 0), _sheenAt } },
            };
            var face = new Grid { Children = { content, _sheen } };
            const double corner = 16, edge = 1.5;
            face.SizeChanged += (_, e) => face.Clip = new RectangleGeometry(new Rect(e.NewSize), corner - edge, corner - edge);
            var card = new Border { CornerRadius = new CornerRadius(corner), BorderThickness = new Thickness(edge), Child = face };
            // its lift off the page: a soft shadow under it, on a plate of its own so the card's words stay sharp. The plate is
            // drawn once into a cached picture (at the screen's scale; the card is never shown past 100%, and the window's fit
            // only shrinks it), so the blur isn't worked out again on each frame while the card grows and fades
            var shade = new Border
            {
                CornerRadius = new CornerRadius(corner),
                Effect = new System.Windows.Media.Effects.DropShadowEffect { Color = Colors.Black, BlurRadius = MomentShadowBlur, ShadowDepth = MomentShadowDepth, Direction = 270, Opacity = MomentShadowOpacity },
            };
            shade.SetResourceReference(Border.BackgroundProperty, "Card");
            var plate = new Border { Child = shade, CacheMode = new BitmapCache { RenderAtScale = Math.Max(1, dpi), SnapsToDevicePixels = true } };
            card.SetResourceReference(Border.BackgroundProperty, "Card");
            card.SetResourceReference(Border.BorderBrushProperty, tone);

            // the ring over the tile's centre: the tile sits at the card's edge and padding, in the middle of its height
            _ring.HorizontalAlignment = HorizontalAlignment.Left;
            _ring.Margin = new Thickness(edge + content.Margin.Left, 0, 0, 0);

            Stage = new Grid
            {
                Width = width, IsHitTestVisible = false, Opacity = 0, RenderTransformOrigin = new Point(0.5, 0.5),
                RenderTransform = new TransformGroup { Children = { _scale, _lift } },
                Children = { _aura, plate, card, _ring },
            };
        }

        /// <summary>The course point the pill starts to fade in at: <see cref="MoreFrom"/>, or later when more came during the moment.</summary>
        double _moreAt = MoreFrom, _at;
        const double MoreFrom = 0.3, MoreFade = 0.15;

        /// <summary>"+2 more" while more came with the drop; none for one alone. A pill that comes up during the moment fades in from there.</summary>
        public void SetMore(int more)
        {
            _moreText.Text = $"+{more} more";
            bool comes = more > 0 && _morePill.Visibility != Visibility.Visible;
            _morePill.Visibility = more > 0 ? Visibility.Visible : Visibility.Collapsed;
            if (comes && _at > MoreFrom) { _moreAt = _at; _morePill.Opacity = 0; }
        }

        /// <summary>
        /// The moment at <paramref name="p"/> of its course: the card comes up and grows in from 90% over the first fifth,
        /// the tile lands from 55% as the ring bursts from it, the label comes up and the price rolls in, the band of light
        /// crosses between a fifth and two thirds in, and the light behind settles; over the last fifth the card lifts a
        /// little as it fades away.
        /// </summary>
        public void Step(double p)
        {
            _at = p;
            double gone = Motion.Out(Motion.Span(p, 0.82, 1));
            Stage.Opacity = Motion.Out(Motion.Span(p, 0, 0.16)) * (1 - gone);
            _scale.ScaleX = _scale.ScaleY = 0.9 + 0.1 * Motion.Out(Motion.Span(p, 0, 0.24));
            _lift.Y = -10 * gone;
            _tileScale.ScaleX = _tileScale.ScaleY = 0.55 + 0.45 * Motion.Out(Motion.Span(p, 0.04, 0.3));
            _tile.Opacity = Motion.Out(Motion.Span(p, 0.04, 0.18));
            Motion.Burst(_ring, Motion.Span(p, 0.08, 0.55), MomentRingGrow);
            _aura.Opacity = Motion.Out(Motion.Span(p, 0, 0.14)) * (1 - 0.5 * Motion.Out(Motion.Span(p, 0.14, 0.7)));
            _label.Opacity = Motion.Out(Motion.Span(p, 0.1, 0.3));
            _morePill.Opacity = Motion.Out(Motion.Span(p, _moreAt, _moreAt + MoreFade));
            double s = Motion.Span(p, 0.2, 0.66);
            _sheen.Opacity = s > 0 && s < 1 ? 1 : 0;
            _sheenAt.X = -MomentSheenWidth + (_width + 2 * MomentSheenWidth) * Motion.Out(s);
            if (_figure != null) _figure.Text = Motion.RollIn(_price, Motion.Span(p, 0.12, 0.5));
        }

        /// <summary>For the options self-test: the card's label, "+N more" when it shows, and the price as it reads now.</summary>
        public string Words => _label.Text + (_morePill.Visibility == Visibility.Visible ? " " + _moreText.Text : "") + " | " + (_figure?.Text ?? "");
    }

    /* ---------------- renders and the self-test ---------------- */

    /// <summary>KL_DEMO_RARE=Noble Dragon Lord Greatsword:2: the item a render's moment shows, and how many more came with it.</summary>
    static (string Name, int More)? DemoRare()
    {
        if (Environment.GetEnvironmentVariable("KL_DEMO_RARE") is not { Length: > 0 } v) return null;
        int cut = v.LastIndexOf(':');
        return cut > 0 && int.TryParse(v[(cut + 1)..], out var more) ? (v[..cut].Trim(), Math.Max(0, more)) : (v.Trim(), 0);
    }

    /// <summary>A drop of <paramref name="name"/> as AlertFor would note it now, with the sample's price.</summary>
    RareFind SampleFind(string name)
    {
        var id = _items.IdOf(name);
        var grade = _items.Get(id)?.Grade ?? "";
        var (price, where) = RarePrice(name, "×1");
        return new RareFind(id, name, "×1", grade, HudKit.GradeColor(grade), DateTime.UtcNow, price, where, IsWatched(name), _hudPrefs.Chime);
    }

    /// <summary>A render of the motion (KL_MOTION_AT with KL_DEMO_RARE): the moment for the named item, held at that point over the sample page.</summary>
    internal void RareMomentFrame()
    {
        if (Motion.FrozenAt == null || DemoRare() is not { } demo) return;
        var f = SampleFind(demo.Name);
        IconCache.Get(_items.IconUrl(f.Id));
        IconCache.Drain(TimeSpan.FromSeconds(8));
        ShowMoment(f, demo.More);
        UpdateLayout();
    }

    /// <summary>The "+2 more" a render of the overlay's watched drop shows (KL_DEMO_RARE's count), "" for none.</summary>
    static string DemoRareMore() => DemoRare() is { More: > 0 } d ? d.More.ToString() : "";

    /// <summary>
    /// For the options self-test: <paramref name="whats"/> ("Shade Gloves ×1") alerted in one tick, from no moment held;
    /// what the panel holds then ("rare", "rareMore") and whether the window's moment plays.
    /// </summary>
    internal (string Rare, string More, bool Moment) RareTickForTest(params string[] whats)
    {
        ForgetRare();
        foreach (var w in whats) AlertFor(w, DateTime.UtcNow);
        RareTickDone();
        return (_hudRare.GetValueOrDefault("rare", ""), _hudRare.GetValueOrDefault("rareMore", ""), _moment != null);
    }

    /// <summary>For the options self-test: the moment for <paramref name="name"/> with <paramref name="more"/>, as a render shows it at the motion's held point; its words, or null once it is gone.</summary>
    internal string? RareMomentForTest(string name, int more)
    {
        ShowMoment(SampleFind(name), more);
        UpdateLayout();
        return _moment?.Words;
    }

    /// <summary>For the options self-test: the moment's layer is on the page and takes no room in it.</summary>
    internal bool RareMomentLayerIsFlat => _momentLayer is { ActualWidth: 0, ActualHeight: 0, IsHitTestVisible: false };
}
