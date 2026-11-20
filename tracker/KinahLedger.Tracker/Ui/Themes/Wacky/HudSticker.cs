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
sealed partial class HudSticker : WackyLook, IThemeCard
{
    const uint Yel = 0xFFE14D, KC = 0x111111, Pink = 0xFF3DA6, Cyan = 0x3DF5C8, GoC = 0x6CFF8E, Paper = 0xFFF8E8, CardC = 0x141414, Gold2 = 0xFFC21A, RedC = 0xFF4D4D, Lilac = 0xD9C8FF;
    static readonly Brush K = Wk.S(KC), PaperB = Wk.S(Paper), YelB = Wk.S(Yel), WhiteB = Wk.S(0xFFFFFF);
    static readonly FontWeight Black = FontWeights.Black;

    readonly StackPanel _line = new() { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Left };
    readonly StackPanel _figs = new() { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
    readonly WrapPanel _stats = new() { Orientation = Orientation.Horizontal, Margin = new Thickness(46, 2, 0, 0), Visibility = Visibility.Collapsed, MaxWidth = 380 };
    readonly Border _noteHost = new() { HorizontalAlignment = HorizontalAlignment.Left, Visibility = Visibility.Collapsed };
    // the pickups: an ink tag of item tiles slapped under the hour sticker
    readonly Border _pickHost = new() { HorizontalAlignment = HorizontalAlignment.Left, Visibility = Visibility.Collapsed };
    static readonly PickStrip.Look Strip = new(26, 4, true, Wk.S(Paper, .5), Wk.S(Yel));
    protected override PickStrip.Look StripLook => Strip;
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

    public HudSticker() : base(new Thickness(12, 68, 16, 8), Wk.Segoe)
    {
        UseRows(StickerRow.H, _ => new StickerRow(), 15, StickerGap);   // 15: "N more" in Korean and Chinese   // 29 tall on a 27 pitch: each sticker laps the one under it
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
        _body.Children.Add(_pickHost);
        _body.Children.Add(_noteHost);
        _body.Children.Add(_card);
        BuildRun();   // the dungeon run's head over the line (Ui/Themes/Wacky/HudSticker.Run.cs)
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
        // in a dungeon run: the run's clock, splits and verdict in place of the hour (Ui/Themes/Wacky/HudSticker.Run.cs)
        if (RunOf() is { } rs) { DrawRun(rs, f); return; }
        EndRun();
        uint paper = f.Abyss ? 0xE6DAFF : Yel;   // the Abyss's points are not Kinah: a lilac sticker, not the gold
        bool waiting = !f.Counted || f.Rate.Length == 0;
        bool mark = f.Abyss && f.Counted && f.Icon.Length > 0;
        bool ring = !waiting && !mark && f.Share >= 0 && !f.Warm;
        if (mark) { var m = MarkSticker(f.Icon); _figs.Children.Add(m); _ring = m; }
        else if (ring) { _ring = Ring(f.Share, f.Beat); _figs.Children.Add(_ring); }
        bool lead = _ring != null;
        if (waiting)
        {
            var w = Wk.Words(Waiting(f.Abyss), 14, K, Wk.Heavy, Black);
            _pill = Wk.Tilt(Sticker(w, Wk.S(paper, PaperA), 10, new Thickness(lead ? 18 : 12, 5, 12, 6)), -1.5);
        }
        else
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal };
            var rate = Figure(f.Rate, f.Warm ? 20 : 29, K, Wk.Heavy, Black);
            if (f.Warm) rate.Opacity = .7;
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
        // the best drop and the last three: an ink tag slapped under the hour sticker, lapping its foot, turned against it
        if (StripFor(f) is { } strip)
        {
            var tag = InkTag(strip, 1.5, new Thickness(6, 5, 7, 5));
            tag.Margin = new Thickness(_ring != null ? 44 : 6, -3, 0, 0);
            _pickHost.Child = tag;
        }
        else _pickHost.Child = null;
        DrawNote(f);
        _figs.Visibility = KinahHidden ? Visibility.Collapsed : Visibility.Visible;
        PlaceTag();
    }

    /// <summary>The note: a small ink tag in its tone.</summary>
    void DrawNote(Figures f)
    {
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
        if (Due.FirstOrDefault() is not { } i)
        {
            _tag.Visibility = Visibility.Collapsed;
            if (_runOn && Figs is { } f0 && !KinahHidden) { DrawLine(f0); return; }
            PlaceTag(); return;
        }
        var (name, clock, tone) = TagWords(i, DueNow);
        _tagName.Text = name; _tagName.Visibility = name.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        _tagClock.Text = clock;
        _tagFace.Background = Wk.S(tone == TimerBadge.Tone.On ? GoC : tone == TimerBadge.Tone.Soon ? Cyan : Paper, PaperA);
        _tag.Visibility = Visibility.Visible;
        // in a run the line's forms leave room for the tag, or put it by the head: drawn again for the tag's new width
        if (_runOn && Figs is { } f && !KinahHidden) { DrawLine(f); return; }
        PlaceTag();
    }

    /// <summary>The tag at the line's end; in a dungeon, inside the fight card's head; in a run without room on its line, by the head.</summary>
    void PlaceTag()
    {
        bool inCard = KinahHidden && _card.Visibility == Visibility.Visible;
        bool inHead = !inCard && _runOn && _tagSpot == TagSpot.Head, inSmalls = !inCard && _runOn && _tagSpot == TagSpot.Smalls;
        // in a dungeon the tag is slapped on the card's top edge, over the head's right side
        if (inCard && _tag.Parent != _card) { Detach(_tag); _card.Children.Add(_tag); Panel.SetZIndex(_tag, 5); }
        else if (inHead && _tag.Parent != _runRow) { Detach(_tag); _runRow.Children.Add(_tag); }
        else if (inSmalls && _tag.Parent != _stats) { Detach(_tag); _stats.Children.Add(_tag); }
        else if (!inCard && !inHead && !inSmalls && _tag.Parent != _line) { Detach(_tag); _line.Children.Add(_tag); }
        _tag.HorizontalAlignment = inCard ? HorizontalAlignment.Right : HorizontalAlignment.Left;
        _tag.VerticalAlignment = inCard ? VerticalAlignment.Top : VerticalAlignment.Center;
        _tag.Margin = inCard ? new Thickness(0, -15, 92, 0) : inHead ? new Thickness(10, 0, 0, 0) : inSmalls ? new Thickness(4, 5, 0, 0) : new Thickness(KinahHidden ? 2 : 12, KinahHidden ? 2 : 0, 0, KinahHidden ? 6 : 0);
        _line.Visibility = KinahHidden && (_tag.Parent != _line || _tag.Visibility != Visibility.Visible) ? Visibility.Collapsed : Visibility.Visible;
        // the pickups stay with the Kinah line, a fight's card up or not (Steven, 2026-11-19: on ALL buddies)
        _pickHost.Visibility = _pickHost.Child != null && !KinahHidden ? Visibility.Visible : Visibility.Collapsed;
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
        if (!shown) { _stats.Visibility = Visibility.Collapsed; _noteHost.Visibility = Visibility.Collapsed; _pickHost.Visibility = Visibility.Collapsed; }
        PlaceTag();
    }

    // ---- the fight

    protected override void DrawFight(Encounter? e, DateTime now)
    {
        _cardStack.Children.Clear();
        if (e == null) { _card.Visibility = Visibility.Collapsed; PlaceTag(); return; }
        _card.Width = FightWidth;
        _cardFace.Background = Wk.S(Yel, PaperA);
        var head = new Grid { Margin = new Thickness(10, KinahHidden && _tag.Visibility == Visibility.Visible ? 10 : 4, 8, 3) };
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
        var body = new Border { Background = Wk.S(CardC, InkA), CornerRadius = new CornerRadius(7), Margin = new Thickness(4, 0, 4, 4), Padding = new Thickness(4, 5, 4, 4) };
        var bs = new StackPanel();
        body.Child = bs;
        if (e.Waiting)
        {
            var w = Wk.Words(e.ReadyText, 13, PaperB, Wk.Segoe, FontWeights.Bold); w.Margin = new Thickness(6, 2, 6, 3);
            bs.Children.Add(w);
            if (PartyBeforeHit(e)) PutRows(bs, e, now);
        }
        else
        {
            PutRows(bs, e, now);
            if (e.HasDamage) { var foot = ParserFoot.Line(e, now, 12); foot.Margin = new Thickness(6, 3, 6, 0); bs.Children.Add(foot); }
        }
        _cardStack.Children.Add(body);
        _card.Visibility = Visibility.Visible;
        PlaceTag();
    }

    /// <summary>The boss's HP: a thick bar with a black outline, notched at the thirds, and "47% · 1.82M left".</summary>
    FrameworkElement HpRow(Encounter e, double hp, DateTime now)
    {
        var row = new Grid { Margin = new Thickness(10, 0, 10, 4) };
        row.ColumnDefinitions.Add(new ColumnDefinition()); row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var outer = new Border { Background = K, CornerRadius = new CornerRadius(8), Padding = new Thickness(2.5), Height = 16, VerticalAlignment = VerticalAlignment.Center };
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

    /// <summary>
    /// Comic action lines round a point, for a slap's impact: <paramref name="sx"/> and <paramref name="sy"/> squash their
    /// reach across and up, so they end inside the window at their widest (their scale peaks at 1.3).
    /// </summary>
    static Path Lines(double r0, double r1, int n, int seed, double sx = 1, double sy = .7)
    {
        var rnd = new Random(seed);
        var sg = new StreamGeometry();
        using (var c = sg.Open())
            for (int i = 0; i < n; i++)
            {
                double a = 2 * Math.PI * i / n + rnd.NextDouble() * .3;
                double k0 = r0 * (0.9 + rnd.NextDouble() * .2), k1 = r1 * (0.85 + rnd.NextDouble() * .3);
                c.BeginFigure(new Point(r1 * 1.2 + Math.Cos(a) * k0 * sx, r1 * 1.2 + Math.Sin(a) * k0 * sy), false, false);
                c.LineTo(new Point(r1 * 1.2 + Math.Cos(a) * k1 * sx, r1 * 1.2 + Math.Sin(a) * k1 * sy), true, false);
            }
        sg.Freeze();
        return new Path { Data = sg, Stroke = K, StrokeThickness = 3.5, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round, Width = r1 * 2.4, Height = r1 * 2.4, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
    }

    /// <summary>
    /// The slap every sticker moment shares: slammed in from big and turned (faster as it hits), a squash and rebound on
    /// impact with a little shake, held, then peeled off (its far edge lifting: turned, flattened, raised and gone).
    /// </summary>
    static void Slap(Sprite s, double p, double from, double rot0, double rot1, double inEnd, double peelAt, double h, FrameworkElement? lines = null, double fall = 0)
    {
        double slap = Seg(p, 0, inEnd), after = Seg(p, inEnd, inEnd + .2), peel = Seg(p, peelAt, 1);
        double scale = from - (from - 1) * slap * slap;
        double k = slap >= 1 ? Math.Sin(after * Math.PI * 2) * (1 - after) : 0;
        double sx = scale * (1 + .18 * k), sy = scale * (1 - .22 * k);
        double rot = Lerp(rot0, rot1, slap) + 3 * Wob(after, 2, 3);
        // the peel: the sticker lifts from its foot up (flattening toward its top edge, leaning), then is flicked away up and
        // turning, off the panel; it keeps its full colour (no fade to grey) until the last moment
        double op = Seg(p, 0, .05) * (1 - Seg(p, .93, 1));
        double lift = 1 - .72 * InOut(peel);
        double keep = -h * (1 - lift) / 2 * sy;
        if (fall > 0) s.Set(sx, sy * (1 - .25 * peel), rot + fall * 22 * peel, 4 * peel, fall * 26 * peel * peel, op, -6 * peel);
        else s.Set(sx * (1 - .06 * peel), sy * lift, rot + 11 * peel, 20 * peel * peel, keep - 3 * Seg(p, inEnd + .2, peelAt), op, 16 * peel);
        if (lines != null)
        {
            lines.Opacity = Seg(p, inEnd * .9, inEnd) * (1 - Seg(p, inEnd + .04, inEnd + .2));
            double ls = .8 + .5 * Seg(p, inEnd * .9, inEnd + .2);
            lines.RenderTransform = new ScaleTransform(ls, ls) { CenterX = lines.Width / 2, CenterY = lines.Height / 2 };
        }
    }

    /// <summary>
    /// A sticker slapped on at <paramref name="at"/> (moved in so it lands whole inside the window), with action lines round
    /// it that reach no further than the window lets them, slammed from no bigger than the window holds, and kept inside
    /// the window through its whole course (a slam or a peel that would reach an edge slides along it).
    /// </summary>
    void SlapOn(FrameworkElement sticker, Point at, string slot, double ms, double from, double rot0, double rot1, double inEnd, double peelAt, (double R0, double R1, int N, int Seed)? lines = null, double fall = 0)
    {
        sticker.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        var size = sticker.DesiredSize;
        double turn = Math.Max(Math.Abs(rot0), Math.Abs(rot1)) + 3;
        // where it lands: whole, at its impact squash (a little wider, never taller), at its resting turn and shake (the slam's
        // wider turn and the peel's are kept inside as they play)
        var c = Inside(at, size, 1.06, Math.Abs(rot1) + 3);
        var g = new Grid();
        Path? ln = null;
        if (lines is { } l)
        {
            // the lines' widest (scale 1.3, 15% past their radius) inside the window round the landing point
            var (rl, rt, rr, rb) = Room(c);
            double reach = l.R1 * 1.15 * 1.3;
            double sx = Math.Clamp((Math.Min(rl, rr) - 6) / reach, .3, 1), sy = Math.Clamp((Math.Min(rt, rb) - 6) / reach, .2, .7);
            ln = Lines(l.R0, l.R1, l.N, l.Seed, sx, sy);
            g.Children.Add(ln);
        }
        g.Children.Add(sticker);
        // (a sticker's hard shadow sits past its own box, down and right)
        var part = Centred(g, sticker); part = new Rect(part.X - 2, part.Y - 2, part.Width + 8, part.Height + 8);
        double big = Fit(from, size, turn);
        var s = Spawn(g, c, slot);
        Play(s, slot, ms, q => { Slap(s, q, big, rot0, rot1, inEnd, peelAt, size.Height, ln, fall); Keep(s, part); });
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
        // over the pill's top right corner, clear of the hour's digits
        var at = At(_pill, .96, 0);
        SlapOn(burst, new Point(at.X + 8, at.Y - 18), "pickup", 1700, 1.9, -18, -7, .2, .56, (rx * .9, rx * 1.2, 9, 3));
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
        // a banner burst slapped across the line's top edge: the hour and the session stay in view under it
        var at = At(_pill, .78, 0);
        SlapOn(burst, new Point(at.X, at.Y - 30), "rare", 2700, 2.3, -14, -3, .18, .62, (rx * .95, rx * 1.25, 12, 5));
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
        SlapOn(burst, new Point(at.X + 64, at.Y - 24), "best", 2100, 2, 14, -6, .2, .58);
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
        SlapOn(burst, new Point(at.X + 8, at.Y - 18), "go", 1400, 1.6, 22, 8, .2, .56);
    }

    /// <summary>Where a fight's stamp lands: across the card's top edge, over the head's top and the room above it.</summary>
    Point StampAt(double fx, double up = 25) { var at = At(_card, fx, 0); return new Point(at.X, at.Y - up); }

    protected override void FightStarted(Encounter e, bool held)
    {
        if (_card.Visibility != Visibility.Visible) return;
        var txt = Wk.Outlined(L.T("FIGHT!"), Wk.Impact, FontWeights.Normal, 26, WhiteB, K, 3);
        var stamp = Sticker(txt, Wk.S(0xFF2E3F), 8, new Thickness(12, 0, 12, 1), 3, 5);
        stamp.HorizontalAlignment = HorizontalAlignment.Center;
        SlapOn(stamp, StampAt(.64), "fight", 1500, 1.7, -18, -7, .2, .56, (70, 96, 12, 13));
    }

    protected override void FightEnded(Encounter e, bool held)
    {
        if (_card.Visibility != Visibility.Visible) return;
        if (e.Outcome == "wipe")
        {
            var txt = Wk.Outlined(L.T("OOF"), Wk.Impact, FontWeights.Normal, 26, WhiteB, K, 3);
            var sad = Sticker(txt, Wk.S(0x8FA3D9), 10, new Thickness(14, 0, 14, 1), 3, 5);
            SlapOn(sad, StampAt(.6), "end", 2100, 1.8, -4, 6, .2, .56, null, 1);
            return;
        }
        if (!Won(e)) return;
        var ko = Wk.Outlined(L.T("K.O.!"), Wk.Impact, FontWeights.Normal, 28, Wk.S(0xFF2E3F), K, 3, 0, WhiteB, 2.5);
        // a burst a little flatter than the FIGHT! stamp, higher, and slammed from 1.35 (it was 1.7): it lands over the card's top
        // edge, off the boss's name and the Killed sticker (2026-11-20 review)
        var burst = Wk.Burst(62, 25, 18, .74, 41, YelB, K, 3, 5, ko);
        SlapOn(burst, StampAt(.6, 33), "end", 2300, 1.35, -20, -6, .18, .58, (70, 100, 14, 17));
    }

    // ---- the member's skills card: a sticker sheet

    /// <summary>
    /// A member's skills card as a sticker sheet: cream backing paper with a black outline and a hard shadow. Slapped on it:
    /// an ink sticker with the class's emblem, the name in its class's colour (gold for you), the Combat Power after it and
    /// the class; the DPS on a yellow sticker, the share on a pink one, the damage on a white one; a "cut here" line; then
    /// the abilities, each name swiped with a highlighter in the class's colour as long as its damage against the top one,
    /// share, damage (and crit and back) right-aligned in tabular figures; "N more"; the foot on an ink tag.
    /// </summary>
    public FrameworkElement DrawCard(MemberCard c)
    {
        double w = WkCard.Width(c, 276);
        var ink = Wk.S(KC);
        var muted = Wk.S(KC, .6);
        var hot = Wk.S(0xD0156E);
        var stack = new StackPanel();
        // the head: an ink sticker
        var head = new Grid();
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var icon = ThemeText.Icon(c.Class, 20, c.Name); icon.Margin = new Thickness(0, 0, 7, 0);
        head.Children.Add(icon);
        var name = Wk.Words(c.Name, 15, c.You ? Wk.S(0xFFC94A) : new SolidColorBrush(c.NameColor), Wk.Heavy, Black);
        name.TextTrimming = TextTrimming.CharacterEllipsis;
        var cp = new TextBlock { FontFamily = Wk.Heavy, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(7, 2, 0, 0), Visibility = c.Cp.Length > 0 ? Visibility.Visible : Visibility.Collapsed };
        cp.Inlines.Add(new Run(L.T("CP") + " ") { FontSize = 8.5, FontWeight = Black, Foreground = Wk.S(Paper, .55) });
        cp.Inlines.Add(new Run(c.Cp) { FontSize = 11.5, FontWeight = Black, Foreground = Wk.S(Paper, .78) });
        Typography.SetNumeralAlignment(cp, FontNumeralAlignment.Tabular);
        WkCard.Put(head, WkCard.NameLine(name, cp), 1);
        var cls = Wk.Words(c.ClassName, 10.5, Wk.S(Paper, .72), Wk.Segoe, FontWeights.Bold); cls.Margin = new Thickness(10, 1, 0, 0);
        WkCard.Put(head, cls, 2);
        var headTag = Wk.Tilt(Sticker(head, ink, 6, new Thickness(7, 4, 10, 5), 2, 3, PaperB), -1);
        headTag.Margin = new Thickness(0, 0, 3, 0);
        stack.Children.Add(headTag);
        // the figures: three stickers
        var figs = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(1, 9, 0, 0) };
        // each a stat sticker: its word in small capitals over the figure
        FrameworkElement FigSticker(string fig, string word, double size, Brush fill, Brush fg, double tilt)
        {
            var sp = new StackPanel();
            var wd = Wk.Words(word.ToUpperInvariant(), 7.5, fg, Wk.Heavy, Black); wd.Opacity = .7; wd.Margin = new Thickness(0, 0, 0, -3);
            sp.Children.Add(wd);
            sp.Children.Add(Figure(fig, size, fg, Wk.Heavy, Black));
            var st = Wk.Tilt(Sticker(sp, fill, 7, new Thickness(8, 2, 9, 1), 2.5, 3), tilt);
            st.VerticalAlignment = VerticalAlignment.Bottom;
            st.Margin = new Thickness(0, 0, 10, 0);
            return st;
        }
        if (c.Dps.Length > 0) figs.Children.Add(FigSticker(c.Dps, L.T("DPS"), 21, YelB, ink, -2.5));
        figs.Children.Add(FigSticker(c.Share, L.T("share"), 16, Wk.S(Pink), WhiteB, 2.5));
        figs.Children.Add(FigSticker(c.Damage, L.T("damage"), 16, WhiteB, ink, -1.5));
        stack.Children.Add(figs);
        if (c.RatesLine.Length > 0)
        {
            var rates = Wk.Words(c.RatesLine, 10.5, hot, Wk.Segoe, FontWeights.Bold);
            rates.TextWrapping = TextWrapping.Wrap; rates.Margin = new Thickness(2, 8, 0, 0);
            stack.Children.Add(rates);
        }
        // the die-cut line between the figures and the abilities
        stack.Children.Add(new Rectangle { Height = 1.5, Stroke = Wk.S(KC, .4), StrokeThickness = 1.5, StrokeDashArray = new DoubleCollection { 3, 2.5 }, Margin = new Thickness(0, 9, 0, 5) });
        var marks = new List<ScaleTransform>();
        if (c.Abilities.Count > 0)
        {
            if (c.CritCol || c.BackCol)
            {
                var names = WkCard.Cols(c, 46, 54, 13);
                TextBlock Head(string s) => WkCard.Num(s.ToUpperInvariant(), 8, Wk.S(KC, .5), Wk.Heavy, Black);
                WkCard.Put(names, Head(L.T("share")), 1); WkCard.Put(names, Head(L.T("damage")), 2);
                if (c.CritCol) WkCard.Put(names, Head(L.T("crit")), WkCard.CritAt);
                if (c.BackCol) WkCard.Put(names, Head(L.T("back")), WkCard.BackAt(c));
                stack.Children.Add(names);
            }
            uint hl = Wk.Vivid(Wk.Hex(c.ClassColor));
            foreach (var a in c.Abilities)
            {
                var row = WkCard.Cols(c, 46, 54, 21);
                // the highlighter: swiped across the name's lower half, its length the ability's damage against the top one
                var cell = new Grid();
                var mark = new Border { Height = 10, VerticalAlignment = VerticalAlignment.Bottom, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 0, 0, 3.5), CornerRadius = new CornerRadius(1.5, 5, 5, 1.5), Width = 0,
                    Background = Wk.Lin(0, 0, 1, 0, (hl, .62, 0), (hl, .5, 1)) };
                var grow = new ScaleTransform();
                mark.RenderTransform = new TransformGroup { Children = { grow, new SkewTransform(-8, 0) } };
                marks.Add(grow);
                new Wk.BarFill(cell, mark, 8).Apply(a.OfTop);
                cell.Children.Add(mark);
                var an = WkCard.Ability(a, 11.5, ink, Wk.Heavy, Black, hot, 9.5); an.Margin = new Thickness(3, 0, 6, 0);
                cell.Children.Add(an);
                row.Children.Add(cell);
                WkCard.Put(row, WkCard.Num(a.ShareText, 10.5, muted, Wk.Segoe, FontWeights.Bold), 1);
                WkCard.Put(row, WkCard.Num(a.Damage, 12, ink, Wk.Heavy, Black), 2);
                if (c.CritCol) WkCard.Put(row, WkCard.Num(a.Crit, 10.5, a.Crit == "–" ? Wk.S(KC, .4) : hot, Wk.Segoe, FontWeights.Bold), WkCard.CritAt);
                if (c.BackCol) WkCard.Put(row, WkCard.Num(a.Back, 10.5, muted, Wk.Segoe, FontWeights.Bold), WkCard.BackAt(c));
                if (a.Tip.Length > 0) row.ToolTip = a.Tip;
                row.Background = Brushes.Transparent;
                stack.Children.Add(row);
            }
            if (c.MoreLine.Length > 0) { var more = Wk.Words(c.MoreLine, 10, Wk.S(KC, .55), Wk.Heavy, Black); more.Margin = new Thickness(3, 2, 0, 0); stack.Children.Add(more); }
        }
        if (c.FootLine.Length > 0)
        {
            var foot = WkCard.Foot(c, 10.5, Wk.Segoe, FontWeights.Bold, PaperB, Wk.S(GoC), Wk.S(0xFF6B6B), Wk.S(Paper, .4));
            foot.MaxWidth = w - 50;
            var tag = Wk.Tilt(Sticker(foot, ink, 5, new Thickness(8, 2, 9, 3), 2, 2.5, PaperB), -.8);
            tag.HorizontalAlignment = HorizontalAlignment.Left; tag.Margin = new Thickness(0, 9, 0, 0);
            stack.Children.Add(tag);
        }
        // the sheet: cream backing paper, a black outline, a hard shadow; transparent room round it for the shadow and the slap
        var sheet = new Grid { Width = w, RenderTransformOrigin = new Point(.5, .5) };
        sheet.Children.Add(new Border { Background = ink, CornerRadius = new CornerRadius(12), RenderTransform = new TranslateTransform(5, 5) });
        sheet.Children.Add(new Border { Background = Wk.Lin(0, 0, 0, 1, (0xFFFBF0, 1, 0), (Paper, 1, .5), (0xF6EBD2, 1, 1)), BorderBrush = ink, BorderThickness = new Thickness(3), CornerRadius = new CornerRadius(12), Padding = new Thickness(10, 10, 11, 11), Child = stack });
        var card = new Border { Padding = new Thickness(4, 4, 10, 10), Background = Brushes.Transparent, Child = sheet };
        TextOptions.SetTextFormattingMode(stack, TextFormattingMode.Display);
        // slapped on (a small turn and a settle, never past its room), then the highlighters swipe across one by one
        var place = new TransformGroup { Children = { new ScaleTransform(), new RotateTransform() } };
        sheet.RenderTransform = place;
        WkCard.Enter(card, 280, p =>
        {
            double k = Out(Seg(p, 0, .55));
            var sc = (ScaleTransform)place.Children[0]; sc.ScaleX = sc.ScaleY = .95 + .05 * k;
            ((RotateTransform)place.Children[1]).Angle = -1.6 * (1 - k);
            for (int i = 0; i < marks.Count; i++) marks[i].ScaleX = Out(Seg(p, .18 + .06 * i, .6 + .06 * i));
        });
        return card;
    }

    // ---- the meter's rows: a sticker strip per member

    /// <summary><paramref name="t"/> laid out on a line <paramref name="lh"/> tall, whatever its face's own line.</summary>
    static TextBlock Tight(TextBlock t, double lh) { t.LineStackingStrategy = LineStackingStrategy.BlockLineHeight; t.LineHeight = lh; return t; }

    /// <summary>"2 more": a dashed paper line with a small ink tag, between the party and you pinned below it.</summary>
    static FrameworkElement StickerGap(int n)
    {
        var g = new Grid { VerticalAlignment = VerticalAlignment.Center };
        g.Children.Add(new Rectangle { Height = 2, Stroke = Wk.S(Paper, .45), StrokeThickness = 2, StrokeDashArray = new DoubleCollection { 3, 3 }, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 8, 0) });
        if (n > 0)
            g.Children.Add(new Border
            {
                Background = Wk.S(KC), BorderBrush = Wk.S(Paper, .7), BorderThickness = new Thickness(1.5), CornerRadius = new CornerRadius(3), Padding = new Thickness(6, 0, 6, 1), HorizontalAlignment = HorizontalAlignment.Center,
                Child = Tight(Wk.Words(L.F("{0} more", n), 10, PaperB, Wk.Heavy, FontWeights.Black), 12),
            });
        return g;
    }

    /// <summary>
    /// A member as a die-cut sticker slapped on the card: a white die-cut margin round a black outline, a hard black shadow,
    /// turned a little (every other one the other way) and nudged off the line, each lapping the one under it. The rank is a
    /// rough-edged ink stamp, the name heavy ink on paper, and the damage share a separate piece of tape in the class's
    /// colour stuck across the sticker's foot (turned against it, torn at its end, its start running off the sticker's
    /// edge). You are edged in hot pink with a pink stamp and YOU tag; a healer's heal a green cross; deaths a red tag; TOP DPS
    /// and TOP HEALS stickers at a kill. DPS and % right-aligned and tabular, never moving.
    /// </summary>
    sealed class StickerRow : IThemeRow
    {
        // the row is its pitch tall (27) and the sticker in it 29, run 2 past its foot: a row taller than its room would be
        // clipped to its own box (the last row's foot and a TOP sticker lifted over its top edge). 2026-11-20 (Steven: "some
        // are just bulky"): 27 where it was 34, the strip's words 3.5 and 4.5 in from its edges where they were 5 and 6
        public const double H = 27;
        readonly Grid _root = new() { Height = H, Background = Brushes.Transparent };
        readonly Grid _all = new() { Margin = new Thickness(0, 0, 0, -2) };
        readonly Grid _strip = new() { RenderTransformOrigin = new Point(.5, .5), Margin = new Thickness(10, 1, 6, 2) };
        readonly TransformGroup _place = new();
        readonly RotateTransform _turn = new();
        readonly TranslateTransform _nudge = new();
        readonly Border _die = new() { CornerRadius = new CornerRadius(8), Background = WhiteB, Padding = new Thickness(2) };
        readonly Border _face = new() { BorderBrush = K, BorderThickness = new Thickness(2.5), CornerRadius = new CornerRadius(6), Background = PaperB };
        // the bar: a highlighter swipe in the class's colour across the sticker's face, under the words, its end torn (Steven,
        // 2026-11-17: "figure out better looks that doesn't crowd everything and prioritizes the bar")
        readonly Grid _tapeHost = new() { Margin = new Thickness(1) };
        readonly Grid _tape = new() { HorizontalAlignment = HorizontalAlignment.Left, Width = 0 };
        readonly Border _tapeBody = new() { Margin = new Thickness(0, 0, 5, 0), CornerRadius = new CornerRadius(4, 0, 0, 4) };
        readonly Path _tear = new() { Width = 6, HorizontalAlignment = HorizontalAlignment.Right, Stretch = Stretch.Fill, Data = Tear };
        readonly Wk.BarFill _fill;
        readonly Grid _stamp = new() { Width = 25, Height = 25, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 0, 2) };
        readonly Path _stampInk = new() { Data = Wk.Star(12.5, 12.5, 15, .88, 61), Stretch = Stretch.Fill, StrokeThickness = 1.4 };
        readonly RotateTransform _stampTurn = new(-6);
        readonly TextBlock _rank = Wk.Words("", 14.5, YelB, Wk.Impact, FontWeights.Normal);
        readonly Border _iconHost = new() { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 5, 0) };
        readonly TextBlock _name = Wk.Words("", 12.5, K, Wk.Heavy, FontWeights.Black);
        readonly Border _you;
        // the Combat Power after the name: small, in faded ink, its "CP" smaller still
        readonly TextBlock _cp = new() { FontFamily = Wk.Heavy, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 1, 0, 0) };
        readonly Run _cpValue = new() { FontSize = 10, FontWeight = FontWeights.Black, Foreground = Wk.S(KC, .58) };
        readonly TextBlock _heal = Wk.Words("", 10.5, Wk.S(0x0E7A3C), Wk.Heavy, FontWeights.Black);
        readonly Border _top = new() { BorderBrush = K, BorderThickness = new Thickness(1.5), CornerRadius = new CornerRadius(3), Padding = new Thickness(4, 0, 4, 0), Margin = new Thickness(5, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        readonly TextBlock _topText = Wk.Words("", 8.5, K, Wk.Heavy, FontWeights.Black);
        readonly Border _dead, _healTag;
        readonly TextBlock _deadText = Wk.Words("", 9.5, WhiteB, Wk.Heavy, FontWeights.Black);
        readonly TextBlock _detail = Wk.Words("", 9.5, Wk.S(KC, .65), Wk.Segoe, FontWeights.Bold);
        readonly TextBlock _figure, _share;
        string _cls = "\0";
        static readonly double FigureW = Wk.Room(Wk.Heavy, 14.5, FontWeights.Black, Wk.DpsForms, 6), ShareW = Wk.Room(Wk.Segoe, 10.5, FontWeights.Bold, Wk.ShareForms, 5);
        Grid _row = null!;
        bool? _cpOnly;
        readonly Run _critWord = new() { FontFamily = Wk.Heavy, FontSize = 7.5, FontWeight = FontWeights.Black, Foreground = Wk.S(KC, .45) };

        double _lean = 1;
        void Turn() => _turn.Angle = _lean * Math.Min(.6, 360 / Math.Max(200, _root.ActualWidth) * .6);

        /// <summary>The tape's torn end: a ragged edge.</summary>
        static readonly Geometry Tear = Geometry.Parse("M0,0 L5,0 L3,2 L6,4 L2.5,6 L5.5,8 L3,9 L0,9 Z");

        public FrameworkElement Root => _root;

        public StickerRow()
        {
            _place.Children.Add(_turn); _place.Children.Add(_nudge);
            // the turn keeps the strip's ends within about 2 px of level however wide the meter is dragged (on the 27 pitch a
            // steeper one ran the strip's edge into its own figures)
            _root.SizeChanged += (_, _) => Turn();
            _strip.RenderTransform = _place;
            TextOptions.SetTextFormattingMode(_root, TextFormattingMode.Ideal);
            _strip.Children.Add(new Border { Background = K, CornerRadius = new CornerRadius(8), RenderTransform = new TranslateTransform(3, 3) });
            _die.Child = _face;
            _face.Child = _tapeHost;
            _strip.Children.Add(_die);
            var inner = new Grid();
            var row = new Grid { Margin = new Thickness(16, 0, 4, 0) };
            // the DPS and the share in columns sized for their widest forms ("888.8K", "88.8%"), a gap before each: a figure never
            // runs into the one before it (Steven, 2026-11-17: "a lot of numbers in all the meters you can't see, it's cut off!")
            foreach (var w in new[] { GridLength.Auto, new GridLength(1, GridUnitType.Star), GridLength.Auto, GridLength.Auto, GridLength.Auto, GridLength.Auto, new GridLength(FigureW), new GridLength(ShareW) })
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = w });
            _row = row;
            row.Children.Add(_iconHost);
            _name.TextTrimming = TextTrimming.CharacterEllipsis;
            _you = new Border { Background = Wk.S(Pink), BorderBrush = K, BorderThickness = new Thickness(1.5), CornerRadius = new CornerRadius(3), Padding = new Thickness(4, 0, 4, 0), Margin = new Thickness(5, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center,
                Child = Wk.Words(L.T("YOU"), 8.5, WhiteB, Wk.Heavy, FontWeights.Black), RenderTransform = new RotateTransform(-4) };
            _cp.Inlines.Add(new Run(L.T("CP") + " ") { FontSize = 7.5, FontWeight = FontWeights.Black, Foreground = Wk.S(KC, .45) });
            _cp.Inlines.Add(_cpValue);
            Typography.SetNumeralAlignment(_cp, FontNumeralAlignment.Tabular);
            var nameLine = WkCard.NameLine(_name, _you, _cp);
            Grid.SetColumn(nameLine, 1); row.Children.Add(nameLine);
            // the heal: a paper tag of its own on the strip's line before the DPS, the TOP sticker after it (slapped over the strip's
            // top edge they read as the row above's, 2026-11-20 review); a crowded line drops the Combat Power, then the share,
            // before the name is cut (WkCard.Crowd)
            _healTag = new Border { Background = PaperB, BorderBrush = K, BorderThickness = new Thickness(1.5), CornerRadius = new CornerRadius(3), Padding = new Thickness(4, 0, 4, 0), Margin = new Thickness(5, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, Child = _heal };
            Wk.Tilt(_healTag, -3);
            Grid.SetColumn(_healTag, 2); row.Children.Add(_healTag);
            _top.Child = _topText;
            // deaths: a red tag slapped over the strip's top edge at its left, by the stamp
            _dead = new Border { Background = Wk.S(0xE5242F), BorderBrush = K, BorderThickness = new Thickness(1.5), CornerRadius = new CornerRadius(3), Padding = new Thickness(3, 0, 4, 0), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(30, 0, 0, 0), Child = _deadText };
            _detail.Margin = new Thickness(6, 1, 0, 0); Grid.SetColumn(_detail, 5); row.Children.Add(_detail);
            _figure = Figure("", 14.5, K, Wk.Heavy, FontWeights.Black); _figure.HorizontalAlignment = HorizontalAlignment.Right;
            Grid.SetColumn(_figure, 6); row.Children.Add(_figure);
            _share = Figure("", 10.5, Wk.S(KC, .72), Wk.Segoe, FontWeights.Bold); _share.HorizontalAlignment = HorizontalAlignment.Right; _share.Margin = new Thickness(0, 1, 0, 0);
            Grid.SetColumn(_share, 7); row.Children.Add(_share);
            inner.Children.Add(row);
            // the words stay level, in the same columns on every row: only the sticker under them turns and shifts
            var words = _words = new Grid { Margin = new Thickness(14.5, 3.5, 10.5, 4.5), Children = { inner } };
            // the highlighter: its body, its torn end, a faint lit streak along its top
            _tear.Width = 5;
            _tape.Children.Add(_tapeBody); _tape.Children.Add(_tear);
            _tape.Children.Add(new Border { Height = 2, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(3, 2, 9, 0), CornerRadius = new CornerRadius(1), Background = Wk.S(0xFFFFFF, .45) });
            _tapeHost.Children.Add(_tape);
            _fill = new Wk.BarFill(_tapeHost, _tape, 12);
            _stamp.Children.Add(_stampInk);
            _stamp.Children.Add(_rank); _rank.HorizontalAlignment = HorizontalAlignment.Center; _rank.Margin = new Thickness(0, 0, 0, 1);
            _stamp.RenderTransformOrigin = new Point(.5, .5); _stamp.RenderTransform = _stampTurn;
            _all.Children.Add(_strip);
            _all.Children.Add(words);
            _all.Children.Add(_stamp);
            // TOP DPS and TOP HEALS: a little sticker on the line after the heal's tag
            _top.Margin = new Thickness(4, 0, 0, 0); _top.VerticalAlignment = VerticalAlignment.Center;
            Wk.Tilt(_top, 4);
            Grid.SetColumn(_top, 3); row.Children.Add(_top);
            // every line on a box as tall as its letters (a Korean or Chinese face's own box is far taller), so the tags and the
            // deaths' tag lapping the strip's edge stay off the next row's words
            foreach (var (t, lh) in new[] { (_heal, 12.0), (_topText, 10.0), (_deadText, 11.0), (_name, 15.0), (_cp, 13.0), (_figure, 17.0), (_share, 13.0), (_detail, 12.0) }) Tight(t, lh);
            _root.SizeChanged += (_, _) => Crowd();
            Wk.Tilt(_dead, -5);
            _dead.RenderTransform = new TransformGroup { Children = { new RotateTransform(-5), new TranslateTransform(0, -6.5) } };
            _all.Children.Add(_dead);
            _root.Children.Add(_all);
        }

        Grid _words = null!;
        bool _cpWanted;
        GridLength _shareW;
        void Crowd() => WkCard.Crowd(_words, _root.ActualWidth, _cp, _cpWanted, on => { _share.Visibility = on ? Visibility.Visible : Visibility.Collapsed; _row.ColumnDefinitions[7].Width = on ? _shareW : new GridLength(0); }, _detail);

        public void Set(MeterMember m, MeterContext c, bool fresh)
        {
            bool odd = m.Rank % 2 == 1;
            _lean = odd ? -1 : 1; Turn();
            _nudge.X = odd ? -2 : 2;
            _stampTurn.Angle = odd ? -8 : 5;
            // you: edged in hot pink (the stamp and the YOU tag pink too), so it reads at a glance
            _face.BorderBrush = m.You ? Wk.S(Pink) : K;
            _face.BorderThickness = new Thickness(m.You ? 3 : 2.5);
            _stampInk.Fill = Wk.S(m.You ? Pink : KC); _stampInk.Stroke = m.You ? K : PaperB;
            _rank.Text = m.Rank.ToString(System.Globalization.CultureInfo.InvariantCulture);
            _rank.Foreground = m.You ? WhiteB : m.Rank == 1 ? YelB : PaperB;
            if (_cls != m.Class) { _cls = m.Class; _iconHost.Child = ThemeText.Icon(m.Class, 17, m.Name); }
            _name.Text = m.Name;
            _you.Visibility = m.You ? Visibility.Visible : Visibility.Collapsed;
            // Combat Power beside the name once there is damage (before it, the figure is the Combat Power: said once)
            _cpValue.Text = m.Cp; _cpWanted = m.Cp.Length > 0 && !m.CpOnly;
            _heal.Text = m.Heal.Length > 0 ? "✚ " + m.Heal : ""; _healTag.Visibility = m.Heal.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
            _top.Visibility = m.TopDps || m.TopHeals ? Visibility.Visible : Visibility.Collapsed;
            _topText.Text = m.TopDps ? L.T("TOP DPS") : L.T("TOP HEALS");
            _top.Background = Wk.S(m.TopDps ? Gold2 : GoC);
            Panel.SetZIndex(_root, m.Deaths > 0 ? 2 : 0);   // its deaths' tag laps the strip over it, never under
            _dead.Visibility = m.Deaths > 0 ? Visibility.Visible : Visibility.Collapsed;
            _deadText.Text = "✖" + m.Deaths.ToString(System.Globalization.CultureInfo.InvariantCulture);
            Wk.Detail(_detail, m, c, _critWord);
            if (_cpOnly != m.CpOnly) { _cpOnly = m.CpOnly; Wk.FigureCols(_row.ColumnDefinitions[6], _row.ColumnDefinitions[7], m.CpOnly, Wk.Heavy, 14.5, FontWeights.Black, Wk.Segoe, 10.5, FontWeights.Bold); _shareW = _row.ColumnDefinitions[7].Width; }
            _figure.Text = m.Figure; _share.Text = m.ShareText;
            // the highlighter: the class's colour as a light marker ink, so the black words stay crisp on it; against the top
            // member's damage, so the top row's runs the sticker's width
            uint mc = Wk.Vivid(Wk.Hex(m.ClassColor));
            var tape = Wk.Lin(0, 0, 0, 1, (Wk.Shade(mc, .62), 1, 0), (Wk.Shade(mc, .46), 1, .55), (Wk.Shade(mc, .36), 1, 1));
            _tapeBody.Background = tape; _tear.Fill = tape;
            _tapeHost.Visibility = m.CpOnly ? Visibility.Hidden : Visibility.Visible;
            _fill.Set(Wk.Bar(m, c), c, "bar", fresh);
            Crowd();
        }
    }
}
