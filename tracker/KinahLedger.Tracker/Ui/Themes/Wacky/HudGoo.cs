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
/// The Wacky theme's Goo look: candy jelly. A squishy glossy blob, purple to pink with uneven round corners, a white gloss
/// across its top, a shade inside its foot (a gradient, not an effect) and a darker lip under it. The ring in lime on deep
/// purple, the hour in butter yellow with a purple drop line, the session on a white pill, the next timer a lime jelly
/// bean. A fight is a deep grape jelly panel (dark for the rows) with a fat round HP tube, coral to orange. Moments: a
/// pickup pops a glossy orange bubble out of the blob that bounces up and bursts into droplets while the blob jiggles (the
/// jelly behind the figures moves, never the figures); a rare drop is a big bubble in the grade's colour carrying the
/// item; your best inflates the blob under a YUM! NEW BEST bubble; the bean bounces when an entry opens; a fight's panel
/// drips in; a kill throws sprinkles.
/// </summary>
sealed class HudGoo : WackyLook, IThemeCard
{
    const uint P1 = 0x7B2CFF, P2 = 0xC23CFF, P3 = 0xFF4FA3, Lime = 0xB6FF3D, Butter = 0xFFF27A, Grape = 0x3A0E6E, Grape2 = 0x24073F, Deep = 0x2A0A55, Night = 0x12021F;
    static readonly Brush ButterB = Wk.S(Butter), WhiteB = Wk.S(0xFFFFFF), GrapeB = Wk.S(Grape), LimeB = Wk.S(Lime), NightB = Wk.S(Night);

    /// <summary>A jelly: the lip under it, the body, the shade in its foot, a light rim and the gloss, all behind the content; its wobble moves the jelly only.</summary>
    sealed class Jelly
    {
        public readonly Grid Root = new() { HorizontalAlignment = HorizontalAlignment.Left };
        public readonly Grid Layer = new() { IsHitTestVisible = false };
        public readonly ScaleTransform Wobble = new();
        public readonly Border Body;
        public readonly Border Content = new();
        public Jelly(CornerRadius r, Thickness pad, uint lip, double gloss)
        {
            Layer.Children.Add(new Border { CornerRadius = r, Background = Wk.S(lip), RenderTransform = new TranslateTransform(0, 4.5) });
            Body = new Border { CornerRadius = r };
            Layer.Children.Add(Body);
            Layer.Children.Add(new Border { CornerRadius = r, Background = Wk.Lin(0, 0, 0, 1, (0x1A0040, 0, 0), (0x1A0040, 0, .55), (0x1A0040, .38, 1)) });
            Layer.Children.Add(new Border { CornerRadius = r, BorderThickness = new Thickness(1.3), BorderBrush = Wk.Lin(0, 0, 0, 1, (0xFFFFFF, .5, 0), (0xFFFFFF, .08, .5), (0xFFFFFF, 0, 1)) });
            Layer.Children.Add(new Border
            {
                CornerRadius = new CornerRadius(Math.Max(4, r.TopLeft - 4), Math.Max(4, r.TopRight - 4), gloss / 2, gloss / 2), Height = gloss, VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(Math.Max(7, r.TopLeft * .55), 3, Math.Max(9, r.TopRight * .7), 0), Background = Wk.Lin(0, 0, 0, 1, (0xFFFFFF, .62, 0), (0xFFFFFF, .06, 1)),
            });
            Layer.Children.Add(new Ellipse { Width = 12, Height = 4, Fill = Wk.Lin(0, 0, 1, 0, (0xFFFFFF, .95, 0), (0xFFFFFF, .4, 1)), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(Math.Max(6, r.TopLeft * .38), 4.5, 0, 0), RenderTransform = new RotateTransform(-14) });
            Layer.RenderTransformOrigin = new Point(.5, 1);
            Layer.RenderTransform = Wobble;
            Root.Children.Add(Layer);
            Content.Padding = pad;
            Root.Children.Add(Content);
        }
    }

    readonly Jelly _blob = new(new CornerRadius(24, 16, 22, 28), new Thickness(9, 7, 14, 9), 0x3E0B7A, 13);
    readonly Jelly _panel = new(new CornerRadius(20, 13, 22, 16), new Thickness(0), Night, 9);
    readonly StackPanel _lineStack = new();
    readonly StackPanel _line = new() { Orientation = Orientation.Horizontal };
    readonly WrapPanel _stats = new() { Orientation = Orientation.Horizontal, Visibility = Visibility.Collapsed, MaxWidth = 380 };
    readonly Border _noteHost = new() { HorizontalAlignment = HorizontalAlignment.Left, Visibility = Visibility.Collapsed };
    readonly StackPanel _fightStack = new();
    // the timer's jelly bean: kept, so its bounce lives through the second's redraws
    readonly Grid _bean = new() { VerticalAlignment = VerticalAlignment.Center, Visibility = Visibility.Collapsed };
    readonly Border _beanLip = new() { CornerRadius = new CornerRadius(13), RenderTransform = new TranslateTransform(0, 2.5) };
    readonly Border _beanFace = new() { CornerRadius = new CornerRadius(13), Padding = new Thickness(10, 3, 11, 4) };
    readonly TextBlock _beanName = Wk.Words("", 11, Wk.S(Grape2), Wk.Segoe, FontWeights.Bold), _beanClock;
    readonly TranslateTransform _beanUp = new();
    readonly ScaleTransform _beanSquash = new();
    FrameworkElement? _ring, _hour;

    public HudGoo() : base(new Thickness(12, 66, 36, 44), Wk.Segoe)
    {
        UseRows(34, _ => new GooRow(), 16, GooGap);
        _beanClock = Figure("", 13, Wk.S(Grape2), Wk.Heavy, FontWeights.Black);
        _beanClock.Margin = new Thickness(5, 0, 0, 0);
        _beanFace.Child = new StackPanel { Orientation = Orientation.Horizontal, Children = { _beanName, _beanClock } };
        _bean.Children.Add(_beanLip);
        _bean.Children.Add(_beanFace);
        _bean.Children.Add(new Border { Height = 5, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(8, 2.5, 10, 0), CornerRadius = new CornerRadius(2.5), Background = Wk.S(0xFFFFFF, .55), IsHitTestVisible = false });
        _bean.RenderTransformOrigin = new Point(.5, 1);
        _bean.RenderTransform = new TransformGroup { Children = { _beanSquash, _beanUp } };
        _stats.Margin = new Thickness(0, 5, 0, 0);
        _lineStack.Children.Add(_line); _lineStack.Children.Add(_stats);
        _blob.Content.Child = _lineStack;
        _panel.Content.Child = _fightStack;
        _panel.Root.Visibility = Visibility.Collapsed;
        _body.Children.Add(_blob.Root);
        _body.Children.Add(_noteHost);
        _body.Children.Add(_panel.Root);
        Ground(Op);
    }

    public override FrameworkElement? FightFrame => _panel.Root.Visibility == Visibility.Visible ? _panel.Root : null;

    protected override void Ground(double opacity)
    {
        double a = .55 + .45 * opacity;   // the jelly's colour carries the figures' ground: it thins, never goes
        _blob.Body.Background = Wk.Lin(0, 0, 1, 1, (P1, a, 0), (P2, a, .5), (P3, a, 1));
        _panel.Body.Background = Wk.Lin(0, 0, 0, 1, (Grape, Math.Max(.7, opacity), 0), (Grape2, Math.Max(.72, opacity), .45), (0x1C0533, Math.Max(.74, opacity), 1));
    }

    /// <summary>Figure text with the purple drop line under it (a copy a step down, not an effect).</summary>
    static Grid Dropped(string s, double size, Brush fg, uint drop, FontWeight w, bool figure = true, double at = 2.5)
    {
        var g = new Grid { VerticalAlignment = VerticalAlignment.Center };
        var d = figure ? Figure(s, size, Wk.S(drop), Wk.Heavy, w) : Wk.Words(s, size, Wk.S(drop), Wk.Heavy, w);
        d.RenderTransform = new TranslateTransform(0, at);
        g.Children.Add(d);
        g.Children.Add(figure ? Figure(s, size, fg, Wk.Heavy, w) : Wk.Words(s, size, fg, Wk.Heavy, w));
        return g;
    }

    // ---- the line

    protected override void DrawLine(Figures f)
    {
        _line.Children.Clear(); _stats.Children.Clear();
        _ring = _hour = null;
        bool waiting = !f.Counted || f.Rate.Length == 0;
        bool mark = f.Abyss && f.Counted && f.Icon.Length > 0;
        if (mark)
        {
            var m = new Grid { Width = 44, Height = 44, Margin = new Thickness(0, 0, 10, 0), VerticalAlignment = VerticalAlignment.Center };
            m.Children.Add(new Ellipse { Fill = Wk.Rad(.4, .35, .7, (0x4A1890, 1, 0), (Deep, 1, 1)), Stroke = NightB, StrokeThickness = 1.5 });
            var a = AbyssMark(f.Icon, 32); ((FrameworkElement)a).HorizontalAlignment = HorizontalAlignment.Center;
            m.Children.Add(a);
            _line.Children.Add(m); _ring = m;
        }
        else if (!waiting && f.Share >= 0 && !f.Warm) { _ring = Ring(f.Share, f.Beat); _line.Children.Add(_ring); }
        if (waiting)
        {
            var w = Dropped(Waiting(f.Abyss), 14.5, WhiteB, Grape, FontWeights.Black, false, 2);
            w.Margin = new Thickness(4, 4, 4, 4);
            _line.Children.Add(w); _hour = w;
        }
        else
        {
            var hour = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            // warming up: dim (a pale lilac, not the butter), on its full drop line so it still reads
            var rate = Dropped(f.Rate, f.Warm ? 21 : 29, f.Warm ? Wk.S(0xE4D2FF) : f.Abyss ? WhiteB : ButterB, Grape, FontWeights.Black);
            hour.Children.Add(rate);
            if (f.Unit.Length > 0)
            {
                var u = Dropped(f.Unit, 14, f.Abyss ? WhiteB : ButterB, Grape, FontWeights.Black, false, 1.6);
                u.VerticalAlignment = VerticalAlignment.Bottom; u.Margin = new Thickness(3, 0, 0, 6); u.Opacity = .9;
                hour.Children.Add(u);
            }
            _line.Children.Add(hour); _hour = hour;
            if (f.Total.Length > 0)
            {
                var sp = new StackPanel { Orientation = Orientation.Horizontal };
                sp.Children.Add(Figure(f.Total, 15, GrapeB, Wk.Heavy, FontWeights.Black));
                var w = Wk.Words(f.TotalWord, 11, Wk.S(0x6B3FA0), Wk.Segoe, FontWeights.Bold); w.Margin = new Thickness(4, 1, 0, 0);
                sp.Children.Add(w);
                var pill = new Grid { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(11, 2, 0, 0) };
                pill.Children.Add(new Border { CornerRadius = new CornerRadius(12), Background = Wk.Lin(0, 0, 0, 1, (0xFFFFFF, .95, 0), (0xF1E2FF, .9, 1)), Padding = new Thickness(10, 2, 11, 3), Child = sp });
                pill.Children.Add(new Border { Height = 4, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(8, 2, 12, 0), CornerRadius = new CornerRadius(2), Background = Wk.S(0xFFFFFF, .9), IsHitTestVisible = false });
                _line.Children.Add(pill);
            }
        }
        // the Abyss's kills, deaths and Kinah: little grape pills in their colours
        foreach (var (fig, word, tone) in f.Stats)
        {
            Brush c = ReferenceEquals(tone, Rec) ? WhiteB : ReferenceEquals(tone, Gold) ? ButterB : WhiteB;
            var sp = new StackPanel { Orientation = Orientation.Horizontal };
            sp.Children.Add(Figure(fig, 12.5, c, Wk.Heavy, FontWeights.Black));
            bool red = ReferenceEquals(tone, Rec);
            var w = Wk.Words(word, 11, Wk.S(red ? 0xFFFFFFu : 0xE2CCFFu), Wk.Segoe, FontWeights.SemiBold); w.Margin = new Thickness(4, 1, 0, 0);
            sp.Children.Add(w);
            // deaths on a red sweet, so the red reads at a glance
            _stats.Children.Add(new Border { CornerRadius = new CornerRadius(10), Background = red ? Wk.Lin(0, 0, 0, 1, (0xFF6A80, 1, 0), (0xE8344E, 1, 1)) : Wk.S(Grape2, .78), Padding = new Thickness(8, 1, 9, 2), Margin = new Thickness(0, 0, 5, 0), Child = sp });
        }
        if (_stats.Children.Count > 0) _stats.Margin = new Thickness(_ring != null ? 54 : 0, 5, 0, 0);
        _stats.Visibility = _stats.Children.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        if (f.Note.Length > 0)
        {
            var nt = Wk.Words(f.Note, 11.5, f.NoteTone, Wk.Segoe, FontWeights.Bold);
            nt.TextTrimming = TextTrimming.CharacterEllipsis; nt.MaxWidth = 360;
            _noteHost.Child = new Border { CornerRadius = new CornerRadius(11), Background = Wk.S(Grape2, .9), BorderBrush = Wk.S(0x8E4BFF, .55), BorderThickness = new Thickness(1), Padding = new Thickness(10, 2, 11, 3), Margin = new Thickness(8, 9, 0, 0), Child = nt };
        }
        else _noteHost.Child = null;
        _noteHost.Visibility = f.Note.Length > 0 && !KinahHidden ? Visibility.Visible : Visibility.Collapsed;
        PlaceBean();
    }

    /// <summary>The ring of your best: a lime arc on a deep purple sweet, the share in white.</summary>
    FrameworkElement Ring(double share, bool beat)
    {
        const double D = 46;
        var g = new Grid { Width = D, Height = D, Margin = new Thickness(0, 0, 10, 0), VerticalAlignment = VerticalAlignment.Center };
        g.Children.Add(new Ellipse { Fill = Wk.S(Night), RenderTransform = new TranslateTransform(0, 2.5) });
        g.Children.Add(new Ellipse { Fill = Wk.Rad(.4, .35, .7, (0x4A1890, 1, 0), (Deep, 1, 1)), Stroke = NightB, StrokeThickness = 1.2 });
        g.Children.Add(Wk.Arc(D, 6.5, 1, Wk.S(Lime, .16), PenLineCap.Round, 3.5));
        g.Children.Add(Wk.Arc(D, 6.5, beat ? 1 : share, beat ? ButterB : LimeB, PenLineCap.Round, 3.5));
        g.Children.Add(Wk.Arc(D, 1.6, beat ? 1 : Math.Max(0, share - .02), Wk.S(0xFFFFFF, .6), PenLineCap.Round, 5.2));
        g.Children.Add(new Ellipse { Width = 10, Height = 5, Fill = Wk.S(0xFFFFFF, .5), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(13, 13, 0, 0) });
        var t = Figure(beat ? "★" : $"{Math.Round(Math.Clamp(share, 0, 1) * 100):0}", beat ? 15 : 13, WhiteB, Wk.Heavy, FontWeights.Black);
        t.HorizontalAlignment = HorizontalAlignment.Center;
        g.Children.Add(t);
        return g;
    }

    // ---- the timer's bean

    protected override void DrawTag()
    {
        if (Due.FirstOrDefault() is not { } i) { _bean.Visibility = Visibility.Collapsed; PlaceBean(); return; }
        var (name, clock, tone) = TagWords(i, DueNow);
        var (hi, lo, lip) = tone == TimerBadge.Tone.On ? (0xDDFF7Au, 0x92E21Cu, 0x4E8A06u) : tone == TimerBadge.Tone.Soon ? (0xFFF7A8u, 0xFFCF3Au, 0xB98500u) : (0xF6EDFFu, 0xCFB6FFu, 0x7E5BC4u);
        _beanFace.Background = Wk.Lin(0, 0, 0, 1, (hi, 1, 0), (lo, 1, 1));
        _beanLip.Background = Wk.S(lip);
        _beanName.Text = name; _beanName.Visibility = name.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        _beanClock.Text = clock;
        _bean.Visibility = Visibility.Visible;
        PlaceBean();
    }

    void PlaceBean()
    {
        bool onPanel = KinahHidden && _panel.Root.Visibility == Visibility.Visible;
        // in a dungeon the bean sits on the grape panel's top edge
        if (onPanel && _bean.Parent != _panel.Root) { Detach(_bean); _panel.Root.Children.Add(_bean); }
        else if (!onPanel && _bean.Parent != _line) { Detach(_bean); _line.Children.Add(_bean); }
        _bean.HorizontalAlignment = onPanel ? HorizontalAlignment.Right : HorizontalAlignment.Left;
        _bean.VerticalAlignment = onPanel ? VerticalAlignment.Top : VerticalAlignment.Center;
        _bean.Margin = onPanel ? new Thickness(0, -15, 72, 0) : new Thickness(12, 0, 0, 0);
        bool lineShown = !KinahHidden || (_bean.Parent == _line && _bean.Visibility == Visibility.Visible);
        _blob.Root.Visibility = lineShown ? Visibility.Visible : Visibility.Collapsed;
        foreach (UIElement ch in _line.Children) if (!ReferenceEquals(ch, _bean)) ch.Visibility = KinahHidden ? Visibility.Collapsed : Visibility.Visible;
        _panel.Root.Margin = new Thickness(0, lineShown ? 16 : 0, 0, 0);
    }

    static void Detach(FrameworkElement el)
    {
        if (el.Parent is Panel p) p.Children.Remove(el);
        else if (el.Parent is Decorator d) d.Child = null;
    }

    protected override void KinahShown(bool shown)
    {
        if (!shown) { _stats.Visibility = Visibility.Collapsed; _noteHost.Visibility = Visibility.Collapsed; }
        PlaceBean();
    }

    // ---- the fight

    protected override void DrawFight(Encounter? e, DateTime now)
    {
        _fightStack.Children.Clear();
        if (e == null) { _panel.Root.Visibility = Visibility.Collapsed; PlaceBean(); return; }
        _panel.Root.Width = FightWidth;
        bool beanOn = KinahHidden && _bean.Visibility == Visibility.Visible;
        var head = new Grid { Margin = new Thickness(14, beanOn ? 13 : 9, 13, 5) };
        for (int i = 0; i < 3; i++) head.ColumnDefinitions.Add(new ColumnDefinition { Width = i == 0 ? new GridLength(1, GridUnitType.Star) : GridLength.Auto });
        var title = Dropped(e.Title, 16, WhiteB, Night, FontWeights.Black, false, 2);
        foreach (var t in title.Children.OfType<TextBlock>()) t.TextTrimming = TextTrimming.CharacterEllipsis;
        head.Children.Add(title);
        if (OutcomeText(e) is { Length: > 0 } outcome)
        {
            uint c = Won(e) ? 0x7DFFB0u : e.Outcome == "wipe" ? 0xFF6B8Au : 0xE2CCFFu;
            var o = new Border { CornerRadius = new CornerRadius(10), Background = Wk.Lin(0, 0, 0, 1, (Wk.Shade(c, .3), 1, 0), (c, 1, 1)), Padding = new Thickness(8, 0, 9, 1), Margin = new Thickness(8, 0, 9, 0), VerticalAlignment = VerticalAlignment.Center,
                Child = Wk.Words(outcome, 12, Wk.S(Grape2), Wk.Heavy, FontWeights.Black) };
            Grid.SetColumn(o, 1); head.Children.Add(o);
        }
        var clk = e.Waiting ? Dropped(L.T("Ready"), 14, LimeB, Night, FontWeights.Black, false, 2) : Dropped(Clock(e.Duration(now)), 17, e.Live ? ButterB : WhiteB, Night, FontWeights.Black, true, 2);
        Grid.SetColumn(clk, 2); head.Children.Add(clk);
        _fightStack.Children.Add(head);
        if (!e.Waiting && HpOf(e) is double hp) _fightStack.Children.Add(HpRow(e, hp, now));
        else _fightStack.Children.Add(new Border { Height = 3 });
        if (e.Waiting)
        {
            var w = Wk.Words(e.ReadyText, 13, Wk.S(0xE2CCFF), Wk.Segoe, FontWeights.Bold); w.Margin = new Thickness(15, 0, 12, 12);
            _fightStack.Children.Add(w);
            if (PartyBeforeHit(e)) { var party = new StackPanel { Margin = new Thickness(6, 0, 6, 9) }; PutRows(party, e, now); _fightStack.Children.Add(party); }
        }
        else
        {
            var rows = new StackPanel { Margin = new Thickness(6, 0, 6, 9) };
            PutRows(rows, e, now);
            if (e.HasDamage) { var foot = ParserFoot.Line(e, now, 12); foot.Margin = new Thickness(7, 6, 7, 0); rows.Children.Add(foot); }
            _fightStack.Children.Add(rows);
        }
        _panel.Root.Visibility = Visibility.Visible;
        PlaceBean();
    }

    /// <summary>The boss's HP: a fat round jelly tube, coral to orange, notched at the thirds, and "47% · 1.82M left".</summary>
    FrameworkElement HpRow(Encounter e, double hp, DateTime now)
    {
        var row = new Grid { Margin = new Thickness(14, 1, 13, 9) };
        row.ColumnDefinitions.Add(new ColumnDefinition()); row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var tube = new Grid { Height = 15, VerticalAlignment = VerticalAlignment.Center };
        tube.Children.Add(new Border { Background = Wk.Lin(0, 0, 0, 1, (0x0E0119, 1, 0), (0x1C0533, 1, 1)), BorderBrush = Wk.S(0x6A35B0, .8), BorderThickness = new Thickness(1.2), CornerRadius = new CornerRadius(7.5) });
        bool low = hp < .34;
        var fill = new Border
        {
            Background = low ? Wk.Lin(0, 0, 0, 1, (0xFF7A8A, 1, 0), (0xF0284A, 1, 1)) : Wk.Lin(0, 0, 1, 0, (0xFF6B6B, 1, 0), (0xFFA53D, 1, 1)),
            CornerRadius = new CornerRadius(7.5), HorizontalAlignment = HorizontalAlignment.Left, Width = 0,
            Child = new Grid
            {
                Children =
                {
                    new Border { Background = Wk.Lin(0, 0, 0, 1, (0xFFFFFF, 0, 0), (0xFFFFFF, 0, .55), (0x7A1A00, .35, 1)), CornerRadius = new CornerRadius(7.5) },
                    new Border { Height = 4, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(5, 2.5, 6, 0), CornerRadius = new CornerRadius(2), Background = Wk.S(0xFFFFFF, .6) },
                },
            },
        };
        tube.SizeChanged += (_, _) => fill.Width = hp <= 0 ? 0 : Math.Max(15, tube.ActualWidth * hp);
        tube.Children.Add(fill);
        HpGhost(tube, e, hp, now, 7.5);
        var ticks = HpTicks(); foreach (var t in ticks.Children.OfType<Border>()) { t.Width = 3.5; t.Background = NightB; t.Margin = new Thickness(0, 1, -1.75, 1); t.BorderBrush = Wk.S(0xFFFFFF, .5); t.BorderThickness = new Thickness(.8, 0, .8, 0); }
        tube.Children.Add(ticks);
        row.Children.Add(tube);
        var words = new TextBlock { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 0, 0), FontFamily = Wk.Heavy };
        Typography.SetNumeralAlignment(words, FontNumeralAlignment.Tabular);
        words.Inlines.Add(new Run($"{Math.Round(hp * 100):0}%") { FontSize = 15, FontWeight = FontWeights.Black, Foreground = low ? Wk.S(0xFF6B8A) : Wk.S(0xFFB86B) });
        words.Inlines.Add(new Run($" · {HpLeftText(e)}") { FontSize = 11.5, FontWeight = FontWeights.SemiBold, FontFamily = Wk.Segoe, Foreground = Wk.S(0xE2CCFF) });
        Grid.SetColumn(words, 1); row.Children.Add(words);
        return row;
    }

    // ---- the moments

    /// <summary>A glossy candy bubble in <paramref name="c"/>, <paramref name="content"/> in it.</summary>
    static Grid Bubble(double w, double h, uint c, UIElement? content)
    {
        var g = new Grid { Width = w, Height = h };
        g.Children.Add(new Ellipse { Fill = Wk.S(Wk.Shade(c, -.55), .9), RenderTransform = new TranslateTransform(0, 3) });
        g.Children.Add(new Ellipse { Fill = Wk.Rad(.38, .3, .78, (Wk.Shade(c, .45), 1, 0), (c, 1, .55), (Wk.Shade(c, -.28), 1, 1)), Stroke = Wk.S(Wk.Shade(c, -.5)), StrokeThickness = 1.5 });
        g.Children.Add(new Ellipse { Width = w * .42, Height = h * .2, Fill = Wk.Lin(0, 0, 0, 1, (0xFFFFFF, .85, 0), (0xFFFFFF, .1, 1)), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(w * .14, h * .08, 0, 0), RenderTransform = new RotateTransform(-12) });
        g.Children.Add(new Ellipse { Width = 4.5, Height = 4.5, Fill = Wk.S(0xFFFFFF, .9), HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, w * .2, h * .18) });
        if (content is FrameworkElement fe) { fe.HorizontalAlignment = HorizontalAlignment.Center; fe.VerticalAlignment = VerticalAlignment.Center; g.Children.Add(fe); }
        return g;
    }

    /// <summary>Droplets for a bubble's pop, in its colour, on a canvas round the bubble's middle.</summary>
    static Wk.Bits Droplets(uint c, int n, double size, double room)
    {
        var bits = new Wk.Bits(room, room);
        for (int i = 0; i < n; i++)
            bits.Add(new Ellipse { Width = size * (i % 3 == 0 ? 1.3 : 1), Height = size * (i % 3 == 0 ? 1.3 : 1), Fill = Wk.Rad(.35, .3, .7, (Wk.Shade(c, .5), 1, 0), (c, 1, 1)) });
        return bits;
    }

    /// <param name="up">How far the droplets may fly up from the bubble's middle (the room the window has over it).</param>
    static void Pop(Wk.Bits bits, double t, double reach, int seed, double up = double.MaxValue)
    {
        var rnd = new Random(seed);
        // the ones flung up go only as high as the window lets them (the splash flattens, the window never cuts it)
        double uk = Math.Clamp(up / (reach * 1.2 * .8), 0, 1);
        for (int i = 0; i < bits.Count; i++)
        {
            double a = 2 * Math.PI * i / bits.Count + rnd.NextDouble() * .5, d = reach * (.7 + rnd.NextDouble() * .5);
            double k = Out(t), dy = Math.Sin(a) * d * k * .8;
            bits.Set(i, Math.Cos(a) * d * k, (dy < 0 ? dy * uk : dy * .45) + 10 * t * t, 0, 1 - .5 * t, t <= 0 ? 0 : 1 - InOut(t));
        }
    }

    /// <summary>The jelly behind the figures jiggles (the figures stay still).</summary>
    static void Jiggle(Jelly j, double ms, double amount)
    {
        Motion.Play(j.Layer, ms, q =>
        {
            double w = Wob(q, 2.6, 3.6);
            j.Wobble.ScaleX = 1 + amount * w; j.Wobble.ScaleY = 1 - amount * 1.2 * w;
        }, () => { j.Wobble.ScaleX = j.Wobble.ScaleY = 1; });
    }

    /// <summary>A bubble that rises out of the jelly with a squash and stretch, bobs, then pops into droplets.</summary>
    void Float(Grid bubble, uint c, Point from, double rise, string slot, double ms, double popAt, int drops, double dropSize)
    {
        // whole inside the window at its biggest (its swell before the pop: about 0.82 of its width and 0.75 of its height
        // either side of its middle), and it rises only as high as the room over it lets it
        var win = WinSize;
        const double pad = 4;
        double hw = bubble.Width * .82, hh = bubble.Height * .75;
        double x = win.Width > 0 ? Math.Clamp(from.X, pad + hw, Math.Max(pad + hw, win.Width - pad - hw)) : from.X;
        double over = from.Y - pad - hh - 9;   // the room over it (its bob and drift take 9)
        double y = over < 0 ? from.Y - over : from.Y;
        rise = Math.Clamp(Math.Min(rise, over), 0, rise);
        from = new Point(x, y);
        var bits = Droplets(c, drops, dropSize, bubble.Width * 2);
        var host = new Grid { Width = bubble.Width * 2, Height = Math.Max(bubble.Height * 2, bubble.Width * 2), Children = { bubble, bits.Host } };
        var bs = new ScaleTransform(); var bt = new TranslateTransform();
        bubble.RenderTransformOrigin = new Point(.5, .5);
        bubble.RenderTransform = new TransformGroup { Children = { bs, bt } };
        var s = Spawn(host, from, slot);
        Play(s, slot, ms, q =>
        {
            double up = Out(Seg(q, 0, .22));
            double bob = Wob(Seg(q, .18, popAt), 2.2, 3);
            double grow = Back(Seg(q, 0, .2), 1.6);
            double stretch = Seg(q, 0, .12) * (1 - Seg(q, .12, .26));
            double sx = grow * (1 - .18 * stretch + .1 * bob), sy = grow * (1 + .24 * stretch - .12 * bob);
            double pop = Seg(q, popAt, popAt + .06);
            bs.ScaleX = sx * (1 + .35 * pop); bs.ScaleY = sy * (1 + .35 * pop);
            bt.Y = -rise * up - 6 * Seg(q, .22, popAt) + 3 * bob;
            bubble.Opacity = Seg(q, 0, .05) * (1 - pop);
            bits.Host.RenderTransform = new TranslateTransform(0, bt.Y);
            Pop(bits, Seg(q, popAt, 1), bubble.Width * .55, 3, from.Y + bt.Y - pad - dropSize * 1.3);
            s.Set(1, 1, 0, 0, 0, 1);
        });
    }

    protected override void Pickup(Pick p, bool held)
    {
        if (KinahHidden || _hour == null) return;
        var words = Wk.Outlined(PickWords(p), Wk.Heavy, FontWeights.Black, 16, WhiteB, Wk.S(0x8A2E00), 2.4);
        var b = Bubble(Math.Max(66, words.Width + 22), 46, 0xFF8A1A, words);
        var at = At(_hour, .92, .15);
        Jiggle(_blob, 800, .045);
        Float(b, 0xFF9A2A, new Point(at.X + 18, at.Y - 8), 24, "pickup", 1500, .64, 8, 7);
    }

    protected override void RareDrop(Pick p, bool held)
    {
        if (KinahHidden) return;
        var c = Wk.Hex(ColorOf(p.Color, p.Grade));
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(Tile(p, 28, glow: true));
        var words = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(7, 0, 0, 0) };
        words.Children.Add(Wk.Outlined((p.Grade.Length > 0 ? L.T(p.Grade) : L.T("Rare")).ToUpperInvariant() + "!", Wk.Heavy, FontWeights.Black, 11, ButterB, NightB, 2));
        words.Children.Add(Wk.Outlined(p.Name, Wk.Heavy, FontWeights.Black, 13.5, WhiteB, NightB, 2.4));
        row.Children.Add(words);
        row.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        var b = Bubble(Math.Max(140, row.DesiredSize.Width + 44), 50, c, row);
        // it rises out of the blob's top edge and floats above it, off the figures
        var at = At(_blob.Root, .72, 0);
        Jiggle(_blob, 1000, .06);
        Float(b, c, new Point(at.X, at.Y - 2), 18, "rare", 2700, .66, 12, 9);
    }

    protected override void BestPassed(bool held)
    {
        if (KinahHidden) return;
        // the blob inflates, wobbling back
        Motion.Play(_blob.Layer, 1300, q =>
        {
            double k = Math.Sin(Seg(q, 0, .3) * Math.PI / 2) * (1 - Seg(q, .3, 1)) + .5 * Wob(Seg(q, .3, 1), 2, 3) * (1 - Seg(q, .3, 1));
            _blob.Wobble.ScaleX = 1 + .055 * k; _blob.Wobble.ScaleY = 1 + .12 * k;
        }, () => { _blob.Wobble.ScaleX = _blob.Wobble.ScaleY = 1; });
        var words = Wk.Outlined(L.T("YUM! NEW BEST"), Wk.Heavy, FontWeights.Black, 14, WhiteB, Wk.S(0x8A0F4A), 2.4);
        var b = Bubble(Math.Max(120, words.Width + 26), 44, 0xFF4FA3, words);
        var at = At(_ring ?? _hour, .6, 0);
        Float(b, 0xFF7AC0, new Point(at.X + 44, at.Y - 6), 16, "best", 2000, .66, 9, 7);
    }

    protected override void EntryOpened(TimerBoard.Item i, bool held)
    {
        if (_bean.Visibility != Visibility.Visible) return;
        // the bean hops: up, down with a squash, two smaller hops (the bean moves, its words stay as they are)
        Motion.Play(_bean, 1100, q =>
        {
            double t = q * 3, hop = Math.Abs(Math.Sin(t * Math.PI)) * Math.Pow(.42, Math.Floor(t));
            double land = Math.Max(0, 1 - Math.Abs(t - Math.Round(t)) * 8) * Math.Pow(.5, Math.Round(t) - 1) * (t > .5 ? 1 : 0);
            _beanUp.Y = -13 * hop;
            _beanSquash.ScaleX = 1 + .14 * land - .05 * hop; _beanSquash.ScaleY = 1 - .2 * land + .08 * hop;
        }, () => { _beanUp.Y = 0; _beanSquash.ScaleX = _beanSquash.ScaleY = 1; });
    }

    protected override void FightStarted(Encounter e, bool held)
    {
        if (_panel.Root.Visibility != Visibility.Visible) return;
        // the panel's jelly drops in with a squash, and goo drips off its foot
        Motion.Play(_panel.Layer, 900, q =>
        {
            double k = Seg(q, 0, .25);
            _panel.Wobble.ScaleY = Lerp(.82, 1, Back(k, 2)) - .05 * Wob(Seg(q, .25, 1), 2, 3.5);
            _panel.Wobble.ScaleX = 1 + .03 * Wob(Seg(q, .2, 1), 2, 3.5) + .03 * (1 - k);
        }, () => { _panel.Wobble.ScaleX = _panel.Wobble.ScaleY = 1; });
        double w = Math.Max(_panel.Root.ActualWidth, 260);
        var cv = new Canvas { Width = w, Height = 56 };
        var drips = new List<(Path Drip, Grid Drop, double Len)>();
        double[] xs = { .2, .52, .8 }, lens = { 22, 34, 16 };
        // the drips and their drops end inside the window: as long as the room under the panel lets them, and the drops
        // fall only that far before they're gone
        var (_, _, _, below) = Room(At(_panel.Root, .5, 1));
        double kl = Math.Clamp((below - 18) / 34, .4, 1);
        var falls = new double[xs.Length];
        for (int i = 0; i < lens.Length; i++) { lens[i] *= kl; falls[i] = Math.Clamp(below - 6 - lens[i], 0, 36); }
        var tear = Geometry.Parse("M5,0 C5,0 10,9 10,13 A5,5 0 1 1 0,13 C0,9 5,0 5,0 Z");
        for (int i = 0; i < xs.Length; i++)
        {
            // a tapered teardrop pulling out of the panel's lip, a specular dot near its round end
            var drip = new Path { Data = tear, Stretch = Stretch.Fill, Width = 13, Height = 0, Fill = Wk.Lin(0, 0, 1, 0, (0x4A128E, 1, 0), (0x9B4DFF, 1, .38), (0x3A0E6E, 1, 1)), RenderTransformOrigin = new Point(.5, 0) };
            var drop = new Grid { Width = 12, Height = 13, Opacity = 0 };
            drop.Children.Add(new Ellipse { Fill = Wk.Rad(.36, .3, .75, (0xC9A2FF, 1, 0), (0x8E3DFF, 1, .45), (0x4A128E, 1, 1)), Stroke = Wk.S(Night, .8), StrokeThickness = 1 });
            drop.Children.Add(new Ellipse { Width = 3.5, Height = 3.5, Fill = WhiteB, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(3, 2.5, 0, 0) });
            Canvas.SetLeft(drip, w * xs[i] - 6.5); Canvas.SetTop(drip, 0);
            Canvas.SetLeft(drop, w * xs[i] - 6);
            cv.Children.Add(drip); cv.Children.Add(drop);
            drips.Add((drip, drop, lens[i]));
        }
        var at = At(_panel.Root, .5, 1);
        var s = Spawn(cv, new Point(at.X, at.Y - 6 + 28), "fight");
        Play(s, "fight", 1700, q =>
        {
            for (int i = 0; i < drips.Count; i++)
            {
                var (drip, drop, len) = drips[i];
                double lag = i * .07, grow = Out(Seg(q, .06 + lag, .42 + lag)), fall = Seg(q, .42 + lag, .78 + lag), back = Seg(q, .44 + lag, .7 + lag);
                drip.Height = Math.Max(0, Math.Max(grow > 0 ? 12 * Math.Min(1, grow * 3) : 0, (len + 6) * grow * (1 - .8 * back)));
                drip.Opacity = 1 - Seg(q, .7 + lag, .82 + lag);
                Canvas.SetTop(drop, (len + 6) * grow - 12 + falls[i] * fall * fall);
                drop.Opacity = fall > 0 ? 1 - Seg(q, .7 + lag, .86 + lag) : 0;
            }
            s.Set(1, 1, 0, 0, 0, 1);
        });
        // and a lime GO! bubble pops up off its top edge (the family's fight call)
        var go = Wk.Outlined(L.T("GO!"), Wk.Heavy, FontWeights.Black, 22, Wk.S(Grape2), WhiteB, 2.4);
        var top = At(_panel.Root, .5, 0);
        Float(Bubble(84, 46, Lime, go), Lime, new Point(top.X, top.Y - 6), 14, "fight-go", 1500, .62, 9, 7);
    }

    protected override void FightEnded(Encounter e, bool held)
    {
        if (_panel.Root.Visibility != Visibility.Visible) return;
        var top = At(_panel.Root, .5, 0);
        var at = new Point(top.X, top.Y - 18);   // on the panel's top edge: the rows stay in view
        if (e.Outcome == "wipe")
        {
            var words = Wk.Outlined(L.T("SPLAT"), Wk.Heavy, FontWeights.Black, 20, WhiteB, Wk.S(0x4A0A26), 2.6);
            var b = Bubble(118, 50, 0xB0306A, words);
            var bs = new ScaleTransform(); b.RenderTransform = bs; b.RenderTransformOrigin = new Point(.5, 1);
            var s = Spawn(new Grid { Width = 140, Height = 80, Children = { b } }, at, "end");
            Play(s, "end", 2000, q =>
            {
                double inn = Back(Seg(q, 0, .16), 1.8), sag = InOut(Seg(q, .3, .8));
                bs.ScaleX = inn * (1 + .3 * sag); bs.ScaleY = inn * (1 - .4 * sag);
                s.Set(1, 1, 0, 0, 10 * sag, Seg(q, 0, .05) * (1 - Seg(q, .82, 1)));
            });
            return;
        }
        if (!Won(e)) return;
        // sprinkles burst up from the panel and rain down over it, round a lime K.O.! bubble
        double w = Math.Max(_panel.Root.ActualWidth, 260);
        var bits = new Wk.Bits(w, 200);
        uint[] candy = { 0xFF4FA3, Lime, 0xFFF27A, 0x5AD8FF, 0xFFFFFF, 0xFF8A1A, 0xC23CFF };
        for (int i = 0; i < 34; i++)
            bits.Add(new Border { Width = 3.5, Height = 10, CornerRadius = new CornerRadius(1.75), Background = Wk.S(candy[i % candy.Length]) });
        var ko = Wk.Outlined(L.T("K.O.!"), Wk.Heavy, FontWeights.Black, 24, Wk.S(Grape2), WhiteB, 2.4);
        var bubble = Bubble(96, 50, Lime, ko);
        var bsc = new ScaleTransform(); bubble.RenderTransform = bsc; bubble.RenderTransformOrigin = new Point(.5, .5);
        var host = new Grid { Width = w, Height = 200, Children = { bits.Host, bubble } };
        var sp = Spawn(host, at, "end");
        var rnd = new Random(17);
        var flight = Enumerable.Range(0, bits.Count).Select(_ => (Vx: (rnd.NextDouble() - .5) * w * .95, Vy: -60 - rnd.NextDouble() * 45, Spin: (rnd.NextDouble() - .5) * 900, Rot: rnd.NextDouble() * 180)).ToList();
        Play(sp, "end", 2400, q =>
        {
            double t = Seg(q, 0, .95);
            for (int i = 0; i < bits.Count; i++)
            {
                var f = flight[i];
                double x = f.Vx * Out(t), y = f.Vy * 1.6 * t + 210 * t * t;
                // the sprinkles fall back to the panel's edge and are gone there: they never rain on the rows
                bits.Set(i, x, y, f.Rot + f.Spin * t, 1, (t <= 0 ? 0 : 1) * (1 - Seg(q, .7, .95)) * (1 - Seg(y, 2, 16)));
            }
            double inn = Back(Seg(q, .02, .2), 2.2), pop = Seg(q, .66, .74);
            bsc.ScaleX = bsc.ScaleY = inn * (1 + .3 * pop) * (1 + .04 * Wob(Seg(q, .2, .78), 2, 2.5));
            bubble.Opacity = Seg(q, .02, .06) * (1 - pop);
            bubble.RenderTransform = bsc;
            sp.Set(1, 1, 0, 0, 0, 1);
        });
    }

    // ---- the member's skills card: a box of candy

    /// <summary>A candy pill: a glossy gradient from <paramref name="hi"/> to <paramref name="lo"/> on its lip, a gloss strip across its top.</summary>
    static Grid Candy(UIElement child, uint hi, uint lo, uint lip, double radius, Thickness pad)
    {
        var g = new Grid { VerticalAlignment = VerticalAlignment.Center };
        g.Children.Add(new Border { CornerRadius = new CornerRadius(radius), Background = Wk.S(lip), RenderTransform = new TranslateTransform(0, 2.2) });
        g.Children.Add(new Border { CornerRadius = new CornerRadius(radius), Background = Wk.Lin(0, 0, 0, 1, (hi, 1, 0), (lo, 1, 1)), Padding = pad, Child = child });
        g.Children.Add(new Border { Height = 3.5, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(radius * .7, 2, radius * .9, 0), CornerRadius = new CornerRadius(1.75), Background = Wk.S(0xFFFFFF, .55), IsHitTestVisible = false });
        return g;
    }

    /// <summary>
    /// A member's skills card as a box of candy: a grape jelly panel (gloss on top, a darker lip under it). The class's emblem
    /// and the name with its purple drop line in its class's colour (gold for you), the Combat Power on a white candy after
    /// it, the class in lilac; the DPS on a butter candy, the share on a pink one, the damage on a lilac one; the rates in
    /// butter; then the abilities, each name over a recessed groove with a candy bar of the class's colour as long as its
    /// damage against the top one (they squish in when the card opens), share, damage (crit, back) right-aligned and
    /// tabular; "N more"; the foot in lilac with heals lime and deaths cherry.
    /// </summary>
    public FrameworkElement DrawCard(MemberCard c)
    {
        double w = WkCard.Width(c, 278);
        var jelly = new Jelly(new CornerRadius(20, 14, 22, 16), new Thickness(12, 10, 12, 12), Night, 9);
        jelly.Body.Background = Wk.Lin(0, 0, 0, 1, (0x4A1590, 1, 0), (Grape, 1, .3), (Grape2, 1, 1));
        jelly.Root.Width = w;
        var lilac = Wk.S(0xE2CCFF);
        var lilacDim = Wk.S(0xE2CCFF, .8);
        var stack = new StackPanel();
        // the head
        var head = new Grid();
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var icon = ThemeText.Icon(c.Class, 20, c.Name); icon.Margin = new Thickness(0, 0, 7, 0);
        head.Children.Add(icon);
        TextBlock N(Brush b) { var t = Wk.Words(c.Name, 15, b, Wk.Heavy, FontWeights.Black); t.TextTrimming = TextTrimming.CharacterEllipsis; return t; }
        var dropName = N(NightB); dropName.RenderTransform = new TranslateTransform(0, 2);
        var name = new Grid { Children = { dropName, N(c.You ? Wk.S(0xFFD84A) : new SolidColorBrush(c.NameColor)) } };
        var cpText = new TextBlock { FontFamily = Wk.Heavy, VerticalAlignment = VerticalAlignment.Center };
        cpText.Inlines.Add(new Run(L.T("CP") + " ") { FontSize = 8, FontWeight = FontWeights.Black, Foreground = Wk.S(0x6B3FA0) });
        cpText.Inlines.Add(new Run(c.Cp) { FontSize = 11, FontWeight = FontWeights.Black, Foreground = GrapeB });
        Typography.SetNumeralAlignment(cpText, FontNumeralAlignment.Tabular);
        var cp = Candy(cpText, 0xFFFFFF, 0xEBDCFF, 0x8E6CC8, 9, new Thickness(7, 0, 8, 1));
        cp.Margin = new Thickness(8, 0, 0, 0); cp.Visibility = c.Cp.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        WkCard.Put(head, WkCard.NameLine(name, cp), 1);
        var cls = Wk.Words(c.ClassName, 10.5, lilac, Wk.Segoe, FontWeights.Bold); cls.Margin = new Thickness(10, 1, 0, 0); cls.Opacity = .85;
        WkCard.Put(head, cls, 2);
        stack.Children.Add(head);
        // the figures: three candies
        var figs = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 9, 0, 0) };
        // each a candy: its word small over the figure
        void Piece(string fig, string word, double size, Brush fg, uint hi, uint lo, uint lip)
        {
            var sp = new StackPanel();
            var wd = Wk.Words(word.ToUpperInvariant(), 7.5, Wk.S(Grape2), Wk.Heavy, FontWeights.Black); wd.Opacity = .72; wd.Margin = new Thickness(0, 0, 0, -3);
            sp.Children.Add(wd);
            sp.Children.Add(Figure(fig, size, fg, Wk.Heavy, FontWeights.Black));
            var cd = Candy(sp, hi, lo, lip, 12, new Thickness(10, 3, 11, 2));
            cd.VerticalAlignment = VerticalAlignment.Bottom;
            cd.Margin = new Thickness(0, 0, 8, 0);
            figs.Children.Add(cd);
        }
        if (c.Dps.Length > 0) Piece(c.Dps, L.T("DPS"), 20, Wk.S(Grape2), 0xFFF7A8, 0xFFCF3A, 0xB98500);
        Piece(c.Share, L.T("share"), 15, WhiteB, 0xFF8AC4, 0xFF4FA3, 0x9E1F5E);
        Piece(c.Damage, L.T("damage"), 15, Wk.S(Grape2), 0xF6EDFF, 0xCFB6FF, 0x7E5BC4);
        stack.Children.Add(figs);
        if (c.RatesLine.Length > 0)
        {
            var rates = Wk.Words(c.RatesLine, 10.5, Wk.S(Butter, .92), Wk.Segoe, FontWeights.Bold);
            rates.TextWrapping = TextWrapping.Wrap; rates.Margin = new Thickness(1, 8, 0, 0);
            stack.Children.Add(rates);
        }
        // a soft groove across the box
        stack.Children.Add(new Border { Height = 3, CornerRadius = new CornerRadius(1.5), Margin = new Thickness(0, 9, 0, 5), Background = Wk.S(Night, .7), BorderBrush = Wk.Lin(0, 0, 0, 1, (0x0A0214, 1, 0), (0x8E4BFF, .45, 1)), BorderThickness = new Thickness(0, .8, 0, .8) });
        var bars = new List<ScaleTransform>();
        if (c.Abilities.Count > 0)
        {
            if (c.CritCol || c.BackCol)
            {
                var names = WkCard.Cols(c, 46, 54, 13);
                TextBlock Head(string s) => WkCard.Num(s.ToUpperInvariant(), 8, lilacDim, Wk.Heavy, FontWeights.Black);
                WkCard.Put(names, Head(L.T("share")), 1); WkCard.Put(names, Head(L.T("damage")), 2);
                if (c.CritCol) WkCard.Put(names, Head(L.T("crit")), WkCard.CritAt);
                if (c.BackCol) WkCard.Put(names, Head(L.T("back")), WkCard.BackAt(c));
                stack.Children.Add(names);
            }
            uint vivid = Wk.Vivid(Wk.Hex(c.ClassColor));
            foreach (var a in c.Abilities)
            {
                var row = WkCard.Cols(c, 46, 54, 25);
                var cell = new Grid();
                // the groove and its candy bar, with a round cap; the bar squishes in when the card opens
                var groove = new Grid { Height = 6, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(1, 0, 8, 2), ClipToBounds = true };
                groove.Children.Add(new Border { CornerRadius = new CornerRadius(3), Background = Wk.S(0x1C0833), BorderBrush = Wk.Lin(0, 0, 0, 1, (0x0A0214, 1, 0), (0x7A40C0, .6, 1)), BorderThickness = new Thickness(0, 1, 0, .8) });
                var candy = new Grid { HorizontalAlignment = HorizontalAlignment.Left, Width = 0, RenderTransformOrigin = new Point(0, .5) };
                candy.Children.Add(new Border { CornerRadius = new CornerRadius(3), Background = Wk.Lin(0, 0, 0, 1, (Wk.Shade(vivid, .35), 1, 0), (vivid, 1, .5), (Wk.Shade(vivid, -.3), 1, 1)) });
                candy.Children.Add(new Border { Height = 1.2, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(3, 1, 4, 0), CornerRadius = new CornerRadius(.6), Background = Wk.S(0xFFFFFF, .8) });
                // the jelly bead at its end, as on the meter's bars
                candy.Children.Add(new Ellipse { Width = 6, Height = 6, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center, Fill = Wk.Rad(.35, .3, .7, (Wk.Shade(vivid, .6), 1, 0), (vivid, 1, .6), (Wk.Shade(vivid, -.3), 1, 1)), Stroke = Wk.S(Wk.Shade(vivid, -.5)), StrokeThickness = .7 });
                var grow = new ScaleTransform(); candy.RenderTransform = grow; bars.Add(grow);
                new Wk.BarFill(groove, candy, 7).Apply(a.OfTop);
                groove.Children.Add(candy);
                cell.Children.Add(groove);
                var an = WkCard.Ability(a, 11.5, WhiteB, Wk.Heavy, FontWeights.Black, ButterB, 9.5);
                an.VerticalAlignment = VerticalAlignment.Top; an.Margin = new Thickness(2, 1, 6, 0);
                cell.Children.Add(an);
                row.Children.Add(cell);
                TextBlock Fig(string s, double size, Brush b, FontFamily f, FontWeight fw) { var t = WkCard.Num(s, size, b, f, fw); t.VerticalAlignment = VerticalAlignment.Top; t.Margin = new Thickness(0, size > 11 ? 1 : 2, 0, 0); return t; }
                WkCard.Put(row, Fig(a.ShareText, 10.5, lilac, Wk.Segoe, FontWeights.Bold), 1);
                WkCard.Put(row, Fig(a.Damage, 12, WhiteB, Wk.Heavy, FontWeights.Black), 2);
                if (c.CritCol) WkCard.Put(row, Fig(a.Crit, 10.5, a.Crit == "–" ? lilacDim : ButterB, Wk.Segoe, FontWeights.Bold), WkCard.CritAt);
                if (c.BackCol) WkCard.Put(row, Fig(a.Back, 10.5, lilac, Wk.Segoe, FontWeights.Bold), WkCard.BackAt(c));
                if (a.Tip.Length > 0) row.ToolTip = a.Tip;
                row.Background = Brushes.Transparent;
                stack.Children.Add(row);
            }
            if (c.MoreLine.Length > 0) { var more = Wk.Words(c.MoreLine, 10, lilacDim, Wk.Segoe, FontWeights.Bold); more.Margin = new Thickness(2, 3, 0, 0); stack.Children.Add(more); }
        }
        if (c.FootLine.Length > 0)
        {
            var foot = WkCard.Foot(c, 10.5, Wk.Segoe, FontWeights.Bold, lilac, LimeB, Wk.S(0xFF6A80), Wk.S(0xE2CCFF, .4));
            foot.Margin = new Thickness(1, 9, 0, 0);
            stack.Children.Add(foot);
        }
        jelly.Content.Child = stack;
        // transparent room round the jelly: its lip under it and its wobble as it lands
        var card = new Border { Padding = new Thickness(8, 8, 8, 10), Background = Brushes.Transparent, Child = jelly.Root };
        // it drops in like jelly (a squash settling from its foot, a hair either way) and the candy bars squish in one by one
        var squash = new ScaleTransform();
        jelly.Root.RenderTransformOrigin = new Point(.5, 1);
        jelly.Root.RenderTransform = squash;
        WkCard.Enter(card, 280, p =>
        {
            double t = Seg(p, 0, .7), e = Math.Exp(-3.2 * t) * (t >= 1 ? 0 : 1), osc = Math.Cos(t * Math.PI * 2.5);
            squash.ScaleY = 1 - .1 * e * osc; squash.ScaleX = 1 + .03 * e * osc;
            for (int i = 0; i < bars.Count; i++) { double k = Seg(p, .22 + .06 * i, .62 + .06 * i); bars[i].ScaleX = Back(k, 2.2); bars[i].ScaleY = 1 + .35 * Math.Sin(k * Math.PI) * (1 - k); }
        });
        return card;
    }

    // ---- the meter's rows: jelly beans with candy bars

    /// <summary>"2 more": a small grape pill between the party and you below it.</summary>
    static FrameworkElement GooGap(int n)
    {
        var g = new Grid { VerticalAlignment = VerticalAlignment.Center };
        for (int i = 0; i < 3; i++)
            g.Children.Add(new Ellipse { Width = 4, Height = 4, Fill = Wk.S(0xE2CCFF, .5), HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness((i - 1) * 14 + (n > 0 ? 0 : 0), 0, 0, 0), Visibility = n > 0 ? Visibility.Collapsed : Visibility.Visible });
        if (n > 0)
            g.Children.Add(new Border { CornerRadius = new CornerRadius(8), Background = Wk.S(Grape2, .95), BorderBrush = Wk.S(0x8E4BFF, .6), BorderThickness = new Thickness(1), Padding = new Thickness(8, 0, 8, 1), HorizontalAlignment = HorizontalAlignment.Center,
                Child = Wk.Words(L.F("{0} more", n), 10, Wk.S(0xE2CCFF), Wk.Segoe, FontWeights.Bold) });
        return g;
    }

    /// <summary>
    /// A member as a jelly bean: a glossy grape pill (a lighter rim, a specular streak along its top) holding the name on its
    /// first line and, under it, a recessed groove with the damage share as a candy bar of the class's colour (full colour,
    /// a gloss line on top, a round bulged cap) that wobbles like jelly when it moves. The rank is a wrapped sweet in the
    /// class's colour (twists either side). You are a butter-rimmed bean with a soft butter glow, a butter sweet and a YOU
    /// pill; a healer's heal a lime pill; deaths a cherry sweet; TOP DPS and TOP HEALS candies at a kill. DPS and % tabular.
    /// </summary>
    sealed class GooRow : IThemeRow
    {
        const double H = 31;
        readonly Grid _root = new() { Height = H, Background = Brushes.Transparent, Margin = new Thickness(3, 0, 3, 0) };
        readonly Border _glow = new() { CornerRadius = new CornerRadius(17), Margin = new Thickness(-3), BorderThickness = new Thickness(3) };
        readonly Border _bean = new() { CornerRadius = new CornerRadius(15), BorderThickness = new Thickness(1.6) };
        // the bar: the bean itself fills with jelly of the class's colour, the words over it (Steven, 2026-11-17: "figure out better
        // looks that doesn't crowd everything and prioritizes the bar")
        readonly Grid _groove = new() { Margin = new Thickness(1.6) };
        readonly Grid _wrap = new() { HorizontalAlignment = HorizontalAlignment.Left, RenderTransformOrigin = new Point(0, .5) };
        readonly ScaleTransform _wob = new();
        readonly Border _candy = new() { CornerRadius = new CornerRadius(13.5) };
        readonly Wk.BarFill _fill;
        readonly Grid _sweet = new() { Width = 29, Height = 20, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 0, 0) };
        readonly Ellipse _ball = new() { Width = 19, Height = 19, StrokeThickness = 1.2 };
        readonly Path _twistL = new() { Data = Geometry.Parse("M0,0 L5.5,2.2 L5.5,4.8 L0,7 Z M0,3.5 L5.5,3.5"), Width = 5.5, Height = 8, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center, Stretch = Stretch.Fill, StrokeThickness = .8 };
        readonly Path _twistR = new() { Data = Geometry.Parse("M5.5,0 L0,2.2 L0,4.8 L5.5,7 Z M5.5,3.5 L0,3.5"), Width = 5.5, Height = 8, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center, Stretch = Stretch.Fill, StrokeThickness = .8 };
        readonly TextBlock _rankDrop = Wk.Words("", 11, Wk.S(Night, .85), Wk.Heavy, FontWeights.Black), _rank = Wk.Words("", 11, WhiteB, Wk.Heavy, FontWeights.Black);
        readonly Border _iconHost = new() { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(2, 0, 6, 0) };
        readonly TextBlock _name = Wk.Words("", 12.5, WhiteB, Wk.Heavy, FontWeights.Black);
        readonly Border _you;
        // the Combat Power after the name, in soft lilac, its "CP" smaller
        readonly TextBlock _cp = new() { FontFamily = Wk.Heavy, VerticalAlignment = VerticalAlignment.Center };
        readonly Run _cpValue = new() { FontSize = 10, FontWeight = FontWeights.Black, Foreground = Wk.S(0xE2CCFF, .78) };
        readonly Border _healPill;
        readonly TextBlock _heal = Wk.Words("", 10, Wk.S(Grape2), Wk.Heavy, FontWeights.Black);
        readonly Border _top = new() { CornerRadius = new CornerRadius(7), Padding = new Thickness(5, 0, 5, 0), BorderThickness = new Thickness(1), BorderBrush = Wk.S(Night, .8) };
        readonly TextBlock _topText = Wk.Words("", 8.5, Wk.S(Grape2), Wk.Heavy, FontWeights.Black);
        readonly Border _dead;
        readonly TextBlock _deadText = Wk.Words("", 9.5, WhiteB, Wk.Heavy, FontWeights.Black);
        readonly TextBlock _detail = Wk.Words("", 9.5, Wk.S(0xF0E4FF, .85), Wk.Segoe, FontWeights.Bold);
        readonly TextBlock _figure, _share;
        string _cls = "\0";
        double _was = -1;
        static readonly double FigureW = Wk.Room(Wk.Heavy, 14, FontWeights.Black, Wk.DpsForms, 6), ShareW = Wk.Room(Wk.Segoe, 10.5, FontWeights.Bold, Wk.ShareForms, 5);
        Grid _row = null!;
        bool? _cpOnly;
        readonly Run _critWord = new() { FontFamily = Wk.Heavy, FontSize = 7.5, FontWeight = FontWeights.Black, Foreground = Wk.S(0xE2CCFF, .55) };

        public FrameworkElement Root => _root;

        public GooRow()
        {
            _root.Children.Add(_glow);
            _root.Children.Add(new Border { CornerRadius = new CornerRadius(15), Background = Wk.S(Night, .95), RenderTransform = new TranslateTransform(0, 2.2) });
            _bean.Background = Wk.Lin(0, 0, 0, 1, (0x5B1F8F, 1, 0), (0x46157A, 1, .45), (0x3A1260, 1, 1));
            _root.Children.Add(_bean);
            // the bean's specular streak along its top
            _root.Children.Add(new Border { Height = 3.5, Width = 90, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(16, 2.5, 0, 0), CornerRadius = new CornerRadius(1.75), Background = Wk.Lin(0, 0, 1, 0, (0xFFFFFF, .55, 0), (0xFFFFFF, 0, 1)), IsHitTestVisible = false });
            // the jelly: the class's colour, lit on top with a gloss streak, deep at its foot, its end rounded like the bean
            _wrap.RenderTransform = _wob;
            _candy.Child = new Border { Height = 3, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(10, 2, 12, 0), CornerRadius = new CornerRadius(1.5), Background = Wk.Lin(0, 0, 1, 0, (0xFFFFFF, .5, 0), (0xFFFFFF, .12, 1)) };
            _wrap.Children.Add(_candy);
            _groove.Children.Add(_wrap);
            _fill = new Wk.BarFill(_groove, _wrap, 26);
            _root.Children.Insert(_root.Children.IndexOf(_bean) + 1, _groove);
            var row = new Grid { Margin = new Thickness(6, 0, 12, 8) };
            // the DPS and the share in columns sized for their widest forms ("888.8K", "88.8%"), a gap before each (Steven,
            // 2026-11-17: "a lot of numbers in all the meters you can't see, it's cut off!")
            foreach (var w in new[] { GridLength.Auto, GridLength.Auto, new GridLength(1, GridUnitType.Star), GridLength.Auto, GridLength.Auto, GridLength.Auto, GridLength.Auto, new GridLength(FigureW), new GridLength(ShareW) })
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = w });
            _row = row;
            _sweet.Children.Add(_twistL); _sweet.Children.Add(_twistR);
            _sweet.Children.Add(_ball);
            _sweet.Children.Add(new Ellipse { Width = 7, Height = 3.5, Fill = Wk.S(0xFFFFFF, .75), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(9, 3, 0, 0), RenderTransform = new RotateTransform(-20) });
            _rankDrop.HorizontalAlignment = _rank.HorizontalAlignment = HorizontalAlignment.Center;
            _rankDrop.RenderTransform = new TranslateTransform(0, 1.2);
            _sweet.Children.Add(_rankDrop); _sweet.Children.Add(_rank);
            row.Children.Add(_sweet);
            Grid.SetColumn(_iconHost, 1); row.Children.Add(_iconHost);
            _name.TextTrimming = TextTrimming.CharacterEllipsis;
            _you = new Border { CornerRadius = new CornerRadius(8), Background = Wk.Lin(0, 0, 0, 1, (0xFFF7A8, 1, 0), (0xFFCF3A, 1, 1)), Padding = new Thickness(5, 0, 5, 1), Margin = new Thickness(5, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center,
                Child = Wk.Words(L.T("YOU"), 8.5, Wk.S(Grape2), Wk.Heavy, FontWeights.Black) };
            _cp.Margin = new Thickness(6, 1, 0, 0);
            _cp.Inlines.Add(new Run(L.T("CP") + " ") { FontSize = 7.5, FontWeight = FontWeights.Black, Foreground = Wk.S(0xE2CCFF, .55) });
            _cp.Inlines.Add(_cpValue);
            Typography.SetNumeralAlignment(_cp, FontNumeralAlignment.Tabular);
            var nameLine = WkCard.NameLine(_name, _you, _cp);
            Grid.SetColumn(nameLine, 2); row.Children.Add(nameLine);
            // the heal: a lime candy at the groove's right end beside TOP's (below), off the name's line, so a name of ten letters
            // keeps its room beside its Combat Power
            _healPill = new Border { CornerRadius = new CornerRadius(8), Background = Wk.Lin(0, 0, 0, 1, (0xDDFF7A, 1, 0), (0x92E21C, 1, 1)), Padding = new Thickness(3, 0, 4, 1), Margin = new Thickness(0, 0, 4, 0), BorderThickness = new Thickness(1), BorderBrush = Wk.S(Night, .8), VerticalAlignment = VerticalAlignment.Bottom, Child = _heal };
            _top.Child = _topText;
            // deaths: a cherry candy stuck on the bean's top edge, over the class icon
            _dead = new Border { CornerRadius = new CornerRadius(8), Background = Wk.Lin(0, 0, 0, 1, (0xFF6A80, 1, 0), (0xE8344E, 1, 1)), Padding = new Thickness(4, 0, 5, 1), BorderThickness = new Thickness(1), BorderBrush = Wk.S(Night, .8),
                HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(16, 0, 0, 0), RenderTransform = new TranslateTransform(0, -9), IsHitTestVisible = false, Child = _deadText };
            // the candies at the groove's end held to their letters' height, clear of the DPS over them
            foreach (var t in new[] { _heal, _topText }) { t.LineStackingStrategy = LineStackingStrategy.BlockLineHeight; t.LineHeight = 11; }
            _detail.Margin = new Thickness(6, 1, 0, 0); Grid.SetColumn(_detail, 6); row.Children.Add(_detail);
            _figure = Figure("", 14, WhiteB, Wk.Heavy, FontWeights.Black); _figure.HorizontalAlignment = HorizontalAlignment.Right;
            Grid.SetColumn(_figure, 7); row.Children.Add(_figure);
            _share = Figure("", 10.5, Wk.S(0xE2CCFF), Wk.Segoe, FontWeights.Bold); _share.HorizontalAlignment = HorizontalAlignment.Right; _share.Margin = new Thickness(0, 1, 0, 0);
            Grid.SetColumn(_share, 8); row.Children.Add(_share);
            _root.Children.Add(row);
            // TOP DPS / TOP HEALS: a small candy at the groove's right end, off the name's line (the bar rarely reaches it), the heal's before it
            _top.VerticalAlignment = VerticalAlignment.Bottom;
            _root.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 14, -1.5), Children = { _healPill, _top } });
            _root.Children.Add(_dead);
        }

        public void Set(MeterMember m, MeterContext c, bool fresh)
        {
            _bean.BorderBrush = m.You ? Wk.S(0xFFE38A) : Wk.Lin(0, 0, 0, 1, (0xA070E0, .9, 0), (0x6A35B0, .7, 1));
            _bean.BorderThickness = new Thickness(m.You ? 2 : 1.4);
            _glow.BorderBrush = m.You ? Wk.S(0xFFE38A, .32) : null;
            uint cc = Wk.Hex(m.ClassColor), vivid = Wk.Vivid(cc);
            uint ball = m.You ? 0xFFCF3Au : vivid;
            _ball.Fill = Wk.Rad(.38, .32, .75, (Wk.Shade(ball, .45), 1, 0), (ball, 1, .55), (Wk.Shade(ball, -.35), 1, 1));
            _ball.Stroke = Wk.S(Wk.Shade(ball, -.55));
            _twistL.Fill = _twistR.Fill = Wk.S(Wk.Shade(ball, -.1)); _twistL.Stroke = _twistR.Stroke = Wk.S(Wk.Shade(ball, -.55));
            _rank.Text = _rankDrop.Text = m.Rank.ToString(System.Globalization.CultureInfo.InvariantCulture);
            _rank.Foreground = m.You ? Wk.S(Grape2) : WhiteB; _rankDrop.Visibility = m.You ? Visibility.Hidden : Visibility.Visible;
            if (_cls != m.Class) { _cls = m.Class; _iconHost.Child = ThemeText.Icon(m.Class, 17, m.Name); }
            _name.Text = m.Name;
            _you.Visibility = m.You ? Visibility.Visible : Visibility.Collapsed;
            // Combat Power beside the name once there is damage (before it, the figure is the Combat Power: said once)
            _cpValue.Text = m.Cp; _cp.Visibility = m.Cp.Length > 0 && !m.CpOnly ? Visibility.Visible : Visibility.Collapsed;
            _heal.Text = m.Heal.Length > 0 ? "✚" + m.Heal : ""; _healPill.Visibility = m.Heal.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
            _top.Visibility = m.TopDps || m.TopHeals ? Visibility.Visible : Visibility.Collapsed;
            _topText.Text = m.TopDps ? L.T("TOP DPS") : L.T("TOP HEALS");
            _top.Background = m.TopDps ? Wk.Lin(0, 0, 0, 1, (0xFFF7A8, 1, 0), (0xFFCF3A, 1, 1)) : Wk.Lin(0, 0, 0, 1, (0xC8FFE0, 1, 0), (0x3DD68C, 1, 1));
            _dead.Visibility = m.Deaths > 0 ? Visibility.Visible : Visibility.Collapsed;
            Panel.SetZIndex(_root, m.Deaths > 0 ? 2 : 0);   // its cherry over the bean above, never under
            _deadText.Text = "✖" + m.Deaths.ToString(System.Globalization.CultureInfo.InvariantCulture);
            Wk.Detail(_detail, m, c, _critWord);
            if (_cpOnly != m.CpOnly) { _cpOnly = m.CpOnly; Wk.FigureCols(_row.ColumnDefinitions[7], _row.ColumnDefinitions[8], m.CpOnly, Wk.Heavy, 14, FontWeights.Black, Wk.Segoe, 10.5, FontWeights.Bold); }
            _figure.Text = m.Figure; _share.Text = m.ShareText;
            // the jelly: the class's colour deepened, so the white words read on it, lit at its top and darker at its foot
            _candy.Background = Wk.Lin(0, 0, 0, 1, (Wk.Shade(vivid, -.18), .96, 0), (Wk.Shade(vivid, -.38), .96, .55), (Wk.Shade(vivid, -.58), .96, 1));
            double share = Wk.Bar(m, c);
            _groove.Visibility = m.CpOnly ? Visibility.Hidden : Visibility.Visible;
            bool moved = _fill.Set(share, c, "bar", fresh);
            // a bar that moves wobbles like jelly as it settles (the bar, not the figures)
            if (moved && c.Moves && _was >= 0 && Math.Abs(share - _was) > .01)
                Motion.Play(_candy, 700, q => { double w = Wob(q, 2.2, 3.4); _wob.ScaleX = 1 + .025 * w; _wob.ScaleY = 1 - .04 * Math.Abs(w); }, () => { _wob.ScaleX = _wob.ScaleY = 1; });
            _was = share;
        }
    }
}
