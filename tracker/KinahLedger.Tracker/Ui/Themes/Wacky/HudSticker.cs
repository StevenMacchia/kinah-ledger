using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Shapes;
using Path = System.Windows.Shapes.Path;
using KinahLedger.Parser;
using KinahLedger.Session;
using static KinahLedger.Ui.HudKit;

namespace KinahLedger.Ui;

/// <summary>
/// The Wacky theme's Sticker look: a sticker bomb. Comic stickers slapped on the screen, each with a 3 px black outline
/// and a hard offset shadow (a second shape behind, never a blur): the ring of your best as a chunky pink ring on black, the
/// hour huge and black on a yellow sticker tilted a little, the session on a black tag, the next timer on a cyan tag
/// (green once its entry is open). A fight is a yellow sticker card whose body is dark ink for the rows. Moments slap
/// burst stickers on: POW for a pickup, a big one in the grade's colour for a rare drop, NEW BEST!, GO!, FIGHT!, K.O.!
/// </summary>
sealed class HudSticker : WackyLook
{
    const uint Yel = 0xFFE14D, KC = 0x111111, Pink = 0xFF3DA6, Cyan = 0x3DF5C8, GoC = 0x6CFF8E, Paper = 0xFFF8E8, CardC = 0x141414, Gold2 = 0xFFC21A, RedC = 0xFF4D4D, Lilac = 0xD9C8FF;
    static readonly Brush K = Wk.S(KC), PaperB = Wk.S(Paper), YelB = Wk.S(Yel), WhiteB = Wk.S(0xFFFFFF);
    static readonly FontWeight Black = FontWeights.Black;

    readonly StackPanel _line = new() { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Left };
    readonly StackPanel _figs = new() { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
    readonly WrapPanel _stats = new() { Orientation = Orientation.Horizontal, Margin = new Thickness(46, 2, 0, 0), Visibility = Visibility.Collapsed, MaxWidth = 380 };
    readonly Border _noteHost = new() { HorizontalAlignment = HorizontalAlignment.Left, Visibility = Visibility.Collapsed };
    // the timer's tag: kept, so its wiggle lives through the second's redraws
    readonly Grid _tag = new() { VerticalAlignment = VerticalAlignment.Center, Visibility = Visibility.Collapsed };
    readonly Border _tagFace = new() { BorderBrush = K, BorderThickness = new Thickness(2.5), CornerRadius = new CornerRadius(5), Padding = new Thickness(8, 2, 9, 3) };
    readonly TextBlock _tagName = Wk.Words("", 11, K, Wk.Heavy, FontWeights.Black), _tagClock;
    readonly RotateTransform _tagTurn = new(3);
    readonly ScaleTransform _tagScale = new();
    // the fight's card
    readonly Grid _card = new() { HorizontalAlignment = HorizontalAlignment.Left, Visibility = Visibility.Collapsed };
    readonly Border _cardFace = new() { BorderBrush = K, BorderThickness = new Thickness(3), CornerRadius = new CornerRadius(12) };
    readonly StackPanel _cardStack = new();
    FrameworkElement? _ring, _pill;

    public HudSticker() : base(new Thickness(12, 56, 56, 16), Wk.Segoe)
    {
        _tagClock = Figure("", 13, K, Wk.Heavy, FontWeights.Black);
        _tagClock.Margin = new Thickness(5, 0, 0, 0);
        _tagFace.Child = new StackPanel { Orientation = Orientation.Horizontal, Children = { _tagName, _tagClock } };
        _tag.Children.Add(new Border { Background = K, CornerRadius = new CornerRadius(5), RenderTransform = new TranslateTransform(3, 3) });
        _tag.Children.Add(_tagFace);
        _tag.RenderTransformOrigin = new Point(.5, .5);
        _tag.RenderTransform = new TransformGroup { Children = { _tagScale, _tagTurn } };
        TextOptions.SetTextFormattingMode(_tag, TextFormattingMode.Ideal);
        _line.Children.Add(_figs);
        _line.Children.Add(_tag);
        _cardFace.Child = _cardStack;
        _card.Children.Add(new Border { Background = K, CornerRadius = new CornerRadius(12), RenderTransform = new TranslateTransform(5, 5) });
        _card.Children.Add(_cardFace);
        _body.Children.Add(_line);
        _body.Children.Add(_stats);
        _body.Children.Add(_noteHost);
        _body.Children.Add(_card);
        Ground(Op);
    }

    public override FrameworkElement? FightFrame => _card.Visibility == Visibility.Visible ? _card : null;

    // the stickers stay stickers at a low opacity (black words need their paper): only part of the fill goes
    double PaperA => 0.74 + 0.26 * Op;
    double InkA => Math.Max(0.62, Op);

    protected override void Ground(double opacity)
    {
        _cardFace.Background = Wk.S(Yel, PaperA);
        if (Figs is { } f) DrawLine(f);
        DrawTag();
        if (_lastFight != null) DrawFight(_lastFight, _lastNow);
    }

    /// <summary>A sticker: <paramref name="child"/> on its paper with a black outline and a hard shadow behind.</summary>
    static Grid Sticker(UIElement child, Brush fill, double radius, Thickness pad, double outline = 3, double shadow = 4, Brush? edge = null)
    {
        var g = new Grid { VerticalAlignment = VerticalAlignment.Center };
        g.Children.Add(new Border { Background = K, CornerRadius = new CornerRadius(radius), RenderTransform = new TranslateTransform(shadow, shadow) });
        g.Children.Add(new Border { Background = fill, BorderBrush = edge ?? K, BorderThickness = new Thickness(outline), CornerRadius = new CornerRadius(radius), Padding = pad, Child = child });
        return g;
    }

    /// <summary>A black tag with a die-cut paper edge (reads over dark scenes and bright ones).</summary>
    Grid InkTag(UIElement child, double tilt, Thickness pad) => Wk.Tilt(Sticker(child, Wk.S(KC, InkA), 5, pad, 2, 3, PaperB), tilt);

    // ---- the line

    protected override void DrawLine(Figures f)
    {
        _figs.Children.Clear(); _stats.Children.Clear();
        _ring = _pill = null;
        uint paper = f.Abyss ? 0xE6DAFF : Yel;   // the Abyss's points are not Kinah: a lilac sticker, not the gold
        bool waiting = !f.Counted || f.Rate.Length == 0;
        bool mark = f.Abyss && f.Counted && f.Icon.Length > 0;
        bool ring = !waiting && !mark && f.Share >= 0 && !f.Warm;
        if (mark) { var m = MarkSticker(f.Icon); _figs.Children.Add(m); _ring = m; }
        else if (ring) { _ring = Ring(f.Share, f.Beat); _figs.Children.Add(_ring); }
        bool lead = _ring != null;
        if (waiting)
        {
            var w = Wk.Words(L.T(Waiting(f.Abyss)), 14, K, Wk.Heavy, Black);
            _pill = Wk.Tilt(Sticker(w, Wk.S(paper, PaperA), 10, new Thickness(lead ? 18 : 12, 5, 12, 6)), -1.5);
        }
        else
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal };
            var rate = Figure(f.Rate, f.Warm ? 20 : 29, K, Wk.Heavy, Black);
            if (f.Warm) rate.Opacity = .5;
            row.Children.Add(rate);
            if (f.Unit.Length > 0)
            {
                var u = Wk.Words(f.Unit, 15, K, Wk.Heavy, Black);
                u.VerticalAlignment = VerticalAlignment.Bottom; u.Margin = new Thickness(3, 0, 0, 5); u.Opacity = .78;
                row.Children.Add(u);
            }
            _pill = Wk.Tilt(Sticker(row, Wk.S(paper, PaperA), 10, new Thickness(lead ? 18 : 12, f.Warm ? 6 : 0, 13, f.Warm ? 7 : 2)), -1.5);
        }
        if (lead) { _pill.Margin = new Thickness(-11, 0, 0, 0); Panel.SetZIndex(_ring!, 3); }
        _figs.Children.Add(_pill);
        if (!waiting && f.Total.Length > 0)
        {
            var sp = new StackPanel { Orientation = Orientation.Horizontal };
            sp.Children.Add(Figure(f.Total, 16, YelB, Wk.Heavy, Black));
            var w = Wk.Words(f.TotalWord, 11, YelB, Wk.Heavy, FontWeights.Bold); w.Margin = new Thickness(4, 2, 0, 0); w.Opacity = .9;
            sp.Children.Add(w);
            var tag = InkTag(sp, 2, new Thickness(8, 1, 9, 2));
            tag.Margin = new Thickness(-9, 18, 0, 0);
            Panel.SetZIndex(tag, 2);
            _figs.Children.Add(tag);
        }
        // the Abyss's kills, deaths and Kinah: small stickers in their colours under the hour
        int n = 0;
        foreach (var (fig, word, tone) in f.Stats)
        {
            uint c = ReferenceEquals(tone, Rec) ? RedC : ReferenceEquals(tone, Gold) ? Yel : Lilac;
            var sp = new StackPanel { Orientation = Orientation.Horizontal };
            sp.Children.Add(Figure(fig, 12.5, K, Wk.Heavy, Black));
            var w = Wk.Words(word, 11, K, Wk.Segoe, FontWeights.Bold); w.Margin = new Thickness(3, 1, 0, 0);
            sp.Children.Add(w);
            var t = Wk.Tilt(Sticker(sp, Wk.S(c, PaperA), 4, new Thickness(6, 0, 7, 1), 2, 2.5), n++ % 2 == 0 ? -2 : 1.5);
            t.Margin = new Thickness(0, 6, 6, 0);
            _stats.Children.Add(t);
        }
        _stats.Visibility = _stats.Children.Count > 0 && !KinahHidden ? Visibility.Visible : Visibility.Collapsed;
        // the note: a small ink tag in its tone
        if (f.Note.Length > 0)
        {
            var nt = Wk.Words(f.Note, 11.5, f.NoteTone, Wk.Segoe, FontWeights.Bold);
            nt.TextTrimming = TextTrimming.CharacterEllipsis; nt.MaxWidth = 360;
            var tag = InkTag(nt, -1, new Thickness(7, 1, 8, 2));
            tag.Margin = new Thickness(4, 9, 0, 0);
            _noteHost.Child = tag;
        }
        else _noteHost.Child = null;
        _noteHost.Visibility = f.Note.Length > 0 && !KinahHidden ? Visibility.Visible : Visibility.Collapsed;
        _figs.Visibility = KinahHidden ? Visibility.Collapsed : Visibility.Visible;
        PlaceTag();
    }

    /// <summary>The ring of your best: a chunky pink ring on black round a paper face with the share in heavy black.</summary>
    FrameworkElement Ring(double share, bool beat)
    {
        const double D = 50;
        var g = new Grid { Width = D, Height = D, VerticalAlignment = VerticalAlignment.Center };
        g.Children.Add(new Ellipse { Fill = K, RenderTransform = new TranslateTransform(3, 3) });
        g.Children.Add(new Ellipse { Fill = Wk.S(KC) });
        // the track's faint pink so the ring reads as a whole
        g.Children.Add(Wk.Arc(D, 9, 1, Wk.S(Pink, .22), PenLineCap.Flat, 3));
        g.Children.Add(Wk.Arc(D, 9, beat ? 1 : share, Wk.S(beat ? Gold2 : Pink), PenLineCap.Flat, 3));
        g.Children.Add(new Ellipse { Width = 23, Height = 23, Fill = PaperB, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center });
        var t = Figure(beat ? "★" : $"{Math.Round(Math.Clamp(share, 0, 1) * 100):0}", beat ? 13 : 11.5, K, Wk.Heavy, Black);
        t.HorizontalAlignment = HorizontalAlignment.Center; t.Margin = new Thickness(0, 0, 0, 1);
        g.Children.Add(t);
        return g;
    }

    /// <summary>The Abyss Points art on a round paper sticker, where the ring is out of the Abyss.</summary>
    static FrameworkElement MarkSticker(string icon)
    {
        const double D = 50;
        var g = new Grid { Width = D, Height = D, VerticalAlignment = VerticalAlignment.Center };
        g.Children.Add(new Ellipse { Fill = K, RenderTransform = new TranslateTransform(3, 3) });
        g.Children.Add(new Ellipse { Fill = K });
        g.Children.Add(new Ellipse { Fill = Wk.S(0x2A1F4A), Stroke = PaperB, StrokeThickness = 3, Margin = new Thickness(2.5) });
        var m = AbyssMark(icon, 34); ((FrameworkElement)m).HorizontalAlignment = HorizontalAlignment.Center;
        g.Children.Add(m);
        return g;
    }

    // ---- the timer's tag

    protected override void DrawTag()
    {
        if (Due.FirstOrDefault() is not { } i) { _tag.Visibility = Visibility.Collapsed; PlaceTag(); return; }
        var (name, clock, tone) = TagWords(i, DueNow);
        _tagName.Text = name; _tagName.Visibility = name.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        _tagClock.Text = clock;
        _tagFace.Background = Wk.S(tone == TimerBadge.Tone.On ? GoC : tone == TimerBadge.Tone.Soon ? Cyan : Paper, PaperA);
        _tag.Visibility = Visibility.Visible;
        PlaceTag();
    }

    /// <summary>The tag at the line's end; in a dungeon, inside the fight card's head.</summary>
    void PlaceTag()
    {
        bool inCard = KinahHidden && _card.Visibility == Visibility.Visible;
        // in a dungeon the tag is slapped on the card's top edge, over the head's right side
        if (inCard && _tag.Parent != _card) { Detach(_tag); _card.Children.Add(_tag); Panel.SetZIndex(_tag, 5); }
        else if (!inCard && _tag.Parent != _line) { Detach(_tag); _line.Children.Add(_tag); }
        _tag.HorizontalAlignment = inCard ? HorizontalAlignment.Right : HorizontalAlignment.Left;
        _tag.VerticalAlignment = inCard ? VerticalAlignment.Top : VerticalAlignment.Center;
        _tag.Margin = inCard ? new Thickness(0, -15, 92, 0) : new Thickness(KinahHidden ? 2 : 12, KinahHidden ? 2 : 0, 0, KinahHidden ? 6 : 0);
        _line.Visibility = KinahHidden && (_tag.Parent != _line || _tag.Visibility != Visibility.Visible) ? Visibility.Collapsed : Visibility.Visible;
        _card.Margin = new Thickness(0, _line.Visibility == Visibility.Visible ? 14 : 0, 0, 0);
    }

    static void Detach(FrameworkElement el)
    {
        if (el.Parent is Panel p) p.Children.Remove(el);
        else if (el.Parent is Decorator d) d.Child = null;
    }

    protected override void KinahShown(bool shown)
    {
        _figs.Visibility = shown ? Visibility.Visible : Visibility.Collapsed;
        if (!shown) { _stats.Visibility = Visibility.Collapsed; _noteHost.Visibility = Visibility.Collapsed; }
        PlaceTag();
    }

    // ---- the fight

    protected override void DrawFight(Encounter? e, DateTime now)
    {
        _cardStack.Children.Clear();
        if (e == null) { _card.Visibility = Visibility.Collapsed; PlaceTag(); return; }
        _card.Width = FightWidth;
        _cardFace.Background = Wk.S(Yel, PaperA);
        var head = new Grid { Margin = new Thickness(11, KinahHidden && _tag.Visibility == Visibility.Visible ? 10 : 6, 8, 6) };
        for (int i = 0; i < 4; i++) head.ColumnDefinitions.Add(new ColumnDefinition { Width = i == 0 ? new GridLength(1, GridUnitType.Star) : GridLength.Auto });
        var title = Wk.Words(e.Title, 16, K, Wk.Heavy, Black); title.TextTrimming = TextTrimming.CharacterEllipsis;
        head.Children.Add(title);
        if (OutcomeText(e) is { Length: > 0 } outcome)
        {
            var ot = Wk.Tilt(Sticker(Wk.Words(outcome, 12.5, K, Wk.Heavy, Black), Wk.S(Won(e) ? GoC : e.Outcome == "wipe" ? RedC : Paper), 4, new Thickness(7, 0, 7, 1), 2.5, 3), -4);
            ot.Margin = new Thickness(6, 0, 8, 0);
            Grid.SetColumn(ot, 2); head.Children.Add(ot);
        }
        TextBlock clk = e.Waiting ? Wk.Words(L.T("Ready"), 13, PaperB, Wk.Heavy, Black) : Figure(Clock(e.Duration(now)), 15, e.Live ? YelB : PaperB, Wk.Heavy, Black);
        var ct = InkTag(clk, 2, new Thickness(8, 0, 8, 1));
        ct.Margin = new Thickness(4, 0, 0, 0);
        Grid.SetColumn(ct, 3); head.Children.Add(ct);
        _cardStack.Children.Add(head);
        if (!e.Waiting && HpOf(e) is double hp) _cardStack.Children.Add(HpRow(e, hp, now));
        var body = new Border { Background = Wk.S(CardC, InkA), CornerRadius = new CornerRadius(7), Margin = new Thickness(4, 0, 4, 4), Padding = new Thickness(4, 5, 4, 6) };
        var bs = new StackPanel();
        body.Child = bs;
        if (e.Waiting)
        {
            var w = Wk.Words(e.ReadyText, 13, PaperB, Wk.Segoe, FontWeights.Bold); w.Margin = new Thickness(6, 2, 6, 3);
            bs.Children.Add(w);
        }
        else
        {
            PutRows(bs, e, now);
            if (e.HasDamage) { var foot = ParserFoot.Line(e, now, 12); foot.Margin = new Thickness(6, 6, 6, 0); bs.Children.Add(foot); }
        }
        _cardStack.Children.Add(body);
        _card.Visibility = Visibility.Visible;
        PlaceTag();
    }

    /// <summary>The boss's HP: a thick bar with a black outline, notched at the thirds, and "47% · 1.82M left".</summary>
    FrameworkElement HpRow(Encounter e, double hp, DateTime now)
    {
        var row = new Grid { Margin = new Thickness(10, 0, 10, 8) };
        row.ColumnDefinitions.Add(new ColumnDefinition()); row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var outer = new Border { Background = K, CornerRadius = new CornerRadius(8), Padding = new Thickness(2.5), Height = 18, VerticalAlignment = VerticalAlignment.Center };
        var track = new Grid();
        track.Children.Add(new Border { Background = Wk.S(0x3A3340), CornerRadius = new CornerRadius(5.5) });
        bool low = hp < .34;
        var fill = new Border
        {
            Background = low ? Wk.Lin(0, 0, 1, 0, (0xE01530, 1, 0), (0xFF3D3D, 1, 1)) : Wk.Lin(0, 0, 1, 0, (0xFF3D6E, 1, 0), (Pink, 1, 1)),
            CornerRadius = new CornerRadius(5.5), HorizontalAlignment = HorizontalAlignment.Left, Width = 0,
            Child = new Border { Height = 3, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(4, 2, 4, 0), CornerRadius = new CornerRadius(1.5), Background = Wk.S(0xFFFFFF, .45) },
        };
        track.SizeChanged += (_, _) => fill.Width = hp <= 0 ? 0 : Math.Max(11, track.ActualWidth * hp);
        track.Children.Add(fill);
        HpGhost(track, e, hp, now, 5.5);
        var ticks = HpTicks(); foreach (var t in ticks.Children.OfType<Border>()) { t.Width = 3.5; t.Background = K; t.BorderBrush = Wk.S(0xFFFFFF, .5); t.BorderThickness = new Thickness(.8, 0, .8, 0); }
        track.Children.Add(ticks);
        outer.Child = track;
        row.Children.Add(outer);
        var words = new TextBlock { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(9, 0, 0, 0), FontFamily = Wk.Heavy };
        Typography.SetNumeralAlignment(words, FontNumeralAlignment.Tabular);
        words.Inlines.Add(new Run($"{Math.Round(hp * 100):0}%") { FontSize = 15, FontWeight = Black, Foreground = low ? Wk.S(0xC8102E) : K });
        words.Inlines.Add(new Run($" · {HpLeftText(e)}") { FontSize = 11.5, FontWeight = FontWeights.Bold, FontFamily = Wk.Segoe, Foreground = K });
        Grid.SetColumn(words, 1); row.Children.Add(words);
        return row;
    }

    // ---- the moments

    /// <summary>Comic action lines round a point, for a slap's impact.</summary>
    static Path Lines(double r0, double r1, int n, int seed)
    {
        var rnd = new Random(seed);
        var sg = new StreamGeometry();
        using (var c = sg.Open())
            for (int i = 0; i < n; i++)
            {
                double a = 2 * Math.PI * i / n + rnd.NextDouble() * .3;
                double k0 = r0 * (0.9 + rnd.NextDouble() * .2), k1 = r1 * (0.85 + rnd.NextDouble() * .3);
                c.BeginFigure(new Point(r1 * 1.2 + Math.Cos(a) * k0, r1 * 1.2 + Math.Sin(a) * k0 * .7), false, false);
                c.LineTo(new Point(r1 * 1.2 + Math.Cos(a) * k1, r1 * 1.2 + Math.Sin(a) * k1 * .7), true, false);
            }
        sg.Freeze();
        return new Path { Data = sg, Stroke = K, StrokeThickness = 3.5, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round, Width = r1 * 2.4, Height = r1 * 2.4, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
    }

    /// <summary>
    /// The slap every sticker moment shares: slammed in from big and turned (faster as it hits), a squash and rebound on
    /// impact with a little shake, held, then peeled off (its far edge lifting: turned, flattened, raised and gone).
    /// </summary>
    static void Slap(Sprite s, double p, double from, double rot0, double rot1, double inEnd, double peelAt, FrameworkElement? lines = null, double fall = 0)
    {
        double slap = Seg(p, 0, inEnd), after = Seg(p, inEnd, inEnd + .2), peel = Seg(p, peelAt, 1);
        double scale = from - (from - 1) * slap * slap;
        double k = slap >= 1 ? Math.Sin(after * Math.PI * 2) * (1 - after) : 0;
        double sx = scale * (1 + .18 * k), sy = scale * (1 - .22 * k);
        double rot = Lerp(rot0, rot1, slap) + 3 * Wob(after, 2, 3);
        double op = Seg(p, 0, .05) * (1 - Seg(p, peelAt + (1 - peelAt) * .3, .97));
        if (fall > 0) s.Set(sx, sy * (1 - .12 * peel), rot + fall * 20 * peel, 4 * peel, fall * 22 * peel * peel, op);
        else s.Set(sx * (1 - .08 * peel), sy * (1 - .55 * peel), rot + 22 * peel, 10 * peel, -3 * Seg(p, inEnd + .2, peelAt) - 16 * peel, op);
        if (lines != null)
        {
            lines.Opacity = Seg(p, inEnd * .9, inEnd) * (1 - Seg(p, inEnd + .04, inEnd + .2));
            double ls = .8 + .5 * Seg(p, inEnd * .9, inEnd + .2);
            lines.RenderTransform = new ScaleTransform(ls, ls) { CenterX = lines.Width / 2, CenterY = lines.Height / 2 };
        }
    }

    protected override void Pickup(Pick p, bool held)
    {
        if (KinahHidden || _pill == null) return;
        var words = new StackPanel();
        var pow = Wk.Outlined(L.T("POW!"), Wk.Impact, FontWeights.Normal, 12, YelB, K, 2.2); pow.HorizontalAlignment = HorizontalAlignment.Center; pow.Margin = new Thickness(0, 0, 0, -4);
        var fig = Wk.Outlined(PickWords(p), Wk.Impact, FontWeights.Normal, 22, WhiteB, K, 2.6); fig.HorizontalAlignment = HorizontalAlignment.Center;
        words.Children.Add(pow); words.Children.Add(fig);
        double rx = Math.Max(42, fig.Width / 2 + 20);
        var burst = Wk.Burst(rx, 30, 15, .74, 7, Wk.S(Pink), K, 3, 4, words);
        var lines = Lines(rx * .9, rx * 1.2, 9, 3);
        var g = new Grid { Children = { lines, burst } };
        // over the pill's top right corner, clear of the hour's digits
        var at = At(_pill, .96, 0);
        var s = Spawn(g, new Point(at.X + 8, at.Y - 18), "pickup");
        Play(s, "pickup", 1700, q => Slap(s, q, 1.9, -18, -7, .2, .56, lines));
    }

    protected override void RareDrop(Pick p, bool held)
    {
        if (KinahHidden || _pill == null) return;
        var c = ColorOf(p.Color, p.Grade);
        var words = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(7, 0, 0, 0) };
        var grade = (p.Grade.Length > 0 ? L.T(p.Grade) : L.T("Rare")).ToUpperInvariant() + "!";
        words.Children.Add(Wk.Outlined(grade, Wk.Impact, FontWeights.Normal, 13, YelB, K, 2.2));
        words.Children.Add(Wk.Outlined(p.Name, Wk.Heavy, FontWeights.Black, 13, WhiteB, K, 2.4));
        var row = new StackPanel { Orientation = Orientation.Horizontal, Children = { Tile(p, 30, glow: true), words } };
        row.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        double rx = Math.Max(66, row.DesiredSize.Width / 2 + 30);
        var burst = Wk.Burst(rx, 26, 22, .8, 11, Wk.S(Wk.Hex(c)), K, 3, 5, row);
        var lines = Lines(rx * .95, rx * 1.25, 12, 5);
        var g = new Grid { Children = { lines, burst } };
        // a banner burst slapped across the line's top edge: the hour and the session stay in view under it
        var at = At(_pill, .78, 0);
        var s = Spawn(g, new Point(at.X, at.Y - 24), "rare");
        Play(s, "rare", 2700, q => Slap(s, q, 2.3, -14, -3, .18, .62, lines));
    }

    protected override void BestPassed(bool held)
    {
        if (KinahHidden) return;
        var anchor = _ring ?? _pill;
        if (anchor == null) return;
        var txt = Wk.Outlined(L.T("NEW BEST!"), Wk.Impact, FontWeights.Normal, 18, K, null, 0);
        var burst = Wk.Burst(Math.Max(56, txt.Width / 2 + 22), 23, 18, .8, 21, Wk.S(Gold2), K, 3, 4, txt);
        // on the ring's top edge, up and off the hour
        var at = At(anchor, .5, 0);
        var s = Spawn(burst, new Point(at.X + 64, at.Y - 24), "best");
        Play(s, "best", 2100, q => Slap(s, q, 2, 14, -6, .2, .58));
    }

    protected override void EntryOpened(TimerBoard.Item i, bool held)
    {
        if (_tag.Visibility != Visibility.Visible) return;
        // the tag wiggles in place (its words stay as they are: the tag turns, not the letters)
        Motion.Play(_tag, 1000, q =>
        {
            _tagTurn.Angle = 3 + 11 * Wob(q, 3.5, 3.4);
            double pop = 1 + .16 * Math.Sin(Seg(q, 0, .3) * Math.PI);
            _tagScale.ScaleX = _tagScale.ScaleY = pop;
        }, () => { _tagTurn.Angle = 3; _tagScale.ScaleX = _tagScale.ScaleY = 1; });
        var go = Wk.Outlined(L.T("GO!"), Wk.Impact, FontWeights.Normal, 18, WhiteB, K, 2.4);
        var burst = Wk.Burst(30, 21, 11, .7, 31, Wk.S(Pink), K, 3, 3, go);
        // over the tag's top right corner, clear of its clock
        var at = At(_tag, 1, 0);
        var s = Spawn(burst, new Point(at.X + 8, at.Y - 18), "go");
        Play(s, "go", 1400, q => Slap(s, q, 1.6, 22, 8, .2, .56));
    }

    /// <summary>Where a fight's stamp lands: across the card's top edge, over the head's top and the room above it.</summary>
    Point StampAt(double fx) { var at = At(_card, fx, 0); return new Point(at.X, at.Y - 21); }

    protected override void FightStarted(Encounter e, bool held)
    {
        if (_card.Visibility != Visibility.Visible) return;
        var txt = Wk.Outlined(L.T("FIGHT!"), Wk.Impact, FontWeights.Normal, 26, WhiteB, K, 3);
        var stamp = Sticker(txt, Wk.S(0xFF2E3F), 8, new Thickness(12, 0, 12, 1), 3, 5);
        stamp.HorizontalAlignment = HorizontalAlignment.Center;
        var lines = Lines(70, 96, 12, 13);
        var g = new Grid { Children = { lines, stamp } };
        var s = Spawn(g, StampAt(.5), "fight");
        Play(s, "fight", 1500, q => Slap(s, q, 2.1, -18, -7, .2, .56, lines));
    }

    protected override void FightEnded(Encounter e, bool held)
    {
        if (_card.Visibility != Visibility.Visible) return;
        if (e.Outcome == "wipe")
        {
            var txt = Wk.Outlined(L.T("OOF"), Wk.Impact, FontWeights.Normal, 26, WhiteB, K, 3);
            var sad = Sticker(txt, Wk.S(0x8FA3D9), 10, new Thickness(14, 0, 14, 1), 3, 5);
            var s = Spawn(sad, StampAt(.42), "end");
            Play(s, "end", 2100, q => Slap(s, q, 1.8, -4, 6, .2, .56, null, 1));
            return;
        }
        if (!Won(e)) return;
        var ko = Wk.Outlined(L.T("K.O.!"), Wk.Impact, FontWeights.Normal, 28, Wk.S(0xFF2E3F), K, 3, 0, WhiteB, 2.5);
        var burst = Wk.Burst(62, 28, 18, .74, 41, YelB, K, 3, 5, ko);
        var lines = Lines(70, 100, 14, 17);
        var g = new Grid { Children = { lines, burst } };
        var s2 = Spawn(g, StampAt(.42), "end");
        Play(s2, "end", 2300, q => Slap(s2, q, 2.2, -20, -6, .18, .58, lines));
    }
}
