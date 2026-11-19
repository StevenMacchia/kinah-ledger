using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Shapes;
using KinahLedger.Parser;
using KinahLedger.Session;
using static KinahLedger.Ui.HudKit;

namespace KinahLedger.Ui;

/// <summary>
/// The Wacky theme's Holo card look (key "goo", so a saved Goo carries over; Steven, 2026-11-17, of the candy jelly: "abysmal
/// dont even do this theme find a new one", and of the three that replaced it: "do c"). A trading card in holographic foil:
/// a silver frame washed in rainbow foil with a light sweep across it, round a midnight face. The ring a foil-rimmed orb, the
/// hour crisp gold, the session a foil chip, the next timer a foil tag. A fight is a taller card: the boss and clock over a
/// foil rule, the HP bar, then each member an attack line (an energy orb with the rank, the name, the DPS big as the attack's
/// damage) over a foil-washed bar of the class's colour. Moments: a pickup flips out of the card as a foil chip and bursts
/// into glints; a rare drop is a big chip in the grade's colour carrying the item; your best a NEW BEST! chip; a fight's card
/// drops in with a light sweeping across its foil; a kill throws foil confetti round a K.O.! chip.
/// </summary>
sealed partial class HudHoloCard : WackyLook, IThemeCard
{
    const uint Navy = 0x0A0F22, Navy2 = 0x141C3C, Deep = 0x050814, Ice = 0xDCE6FF, Steel = 0x9AA8D8, Cyan = 0x8FEFFF, GoldC = 0xFFE29A, Mint = 0x7AFFC4, Cherry = 0xFF4D6A, Ink = 0x0A0F22;
    static readonly Brush WhiteB = Wk.S(0xFFFFFF), IceB = Wk.S(Ice), SteelB = Wk.S(Steel), GoldB = Wk.S(GoldC), InkB = Wk.S(Ink);
    static readonly FontFamily Face = HudKit.Text;

    /// <summary>A holographic foil: rainbow bands at a slant, repeating every <paramref name="period"/> DIPs.</summary>
    static Brush Foil(double a, double period = 46)
    {
        var b = new LinearGradientBrush { MappingMode = BrushMappingMode.Absolute, StartPoint = new Point(0, 0), EndPoint = new Point(period, period * .55), SpreadMethod = GradientSpreadMethod.Repeat };
        foreach (var (c, at) in new[] { (0xFF7AD9u, 0.0), (0x8FE8FFu, .2), (0xFFF39Au, .4), (0xB59BFFu, .6), (0x8AFFCBu, .8), (0xFF7AD9u, 1.0) })
            b.GradientStops.Add(new GradientStop(Rgb(c, a), at));
        b.Freeze();
        return b;
    }

    /// <summary>
    /// A card: its shadow, a silver frame washed in foil with a band of light across it and a white rim, then the midnight face
    /// (a whisper of foil on it, lit at its top, a fine inner rim), all behind the content; its wobble moves the card only.
    /// </summary>
    sealed class Card
    {
        public readonly Grid Root = new() { HorizontalAlignment = HorizontalAlignment.Left };
        public readonly Grid Layer = new() { IsHitTestVisible = false };
        public readonly ScaleTransform Wobble = new();
        public readonly Border Body;
        public readonly Border Content = new();
        public Card(double radius, Thickness pad, double frame = 4)
        {
            var r = new CornerRadius(radius);
            var inner = new CornerRadius(Math.Max(2, radius - frame));
            var fm = new Thickness(frame);
            Layer.Children.Add(new Border { CornerRadius = r, Background = Wk.S(0x000000, .5), RenderTransform = new TranslateTransform(0, 3) });
            Layer.Children.Add(new Border { CornerRadius = r, Background = Wk.Lin(0, 0, 1, 1, (0xF2F5FF, 1, 0), (0x8F98B8, 1, .45), (0xF6F8FF, 1, .7), (0xA4ACC8, 1, 1)) });
            Layer.Children.Add(new Border { CornerRadius = r, Background = Foil(.86, 74) });
            Layer.Children.Add(new Border { CornerRadius = r, Background = Wk.Lin(0, 0, 1, 1, (0xFFFFFF, 0, .3), (0xFFFFFF, .55, .42), (0xFFFFFF, 0, .54)) });
            Layer.Children.Add(new Border { CornerRadius = r, BorderBrush = Wk.S(0xFFFFFF, .85), BorderThickness = new Thickness(1) });
            Body = new Border { CornerRadius = inner, Margin = fm };
            Layer.Children.Add(Body);
            Layer.Children.Add(new Border { CornerRadius = inner, Margin = fm, Background = Foil(.045, 96) });
            Layer.Children.Add(new Border { CornerRadius = inner, Margin = fm, Background = Wk.Lin(0, 0, 0, 1, (0xFFFFFF, .07, 0), (0xFFFFFF, 0, .3)) });
            Layer.Children.Add(new Border { CornerRadius = inner, Margin = fm, BorderBrush = Wk.Lin(0, 0, 0, 1, (0x000000, .65, 0), (0xFFFFFF, .14, 1)), BorderThickness = new Thickness(1) });
            Layer.RenderTransformOrigin = new Point(.5, 1);
            Layer.RenderTransform = Wobble;
            Root.Children.Add(Layer);
            Content.Padding = new Thickness(pad.Left + frame, pad.Top + frame, pad.Right + frame, pad.Bottom + frame);
            Root.Children.Add(Content);
        }
    }

    readonly Card _blob = new(16, new Thickness(9, 6, 13, 7));
    readonly Card _panel = new(14, new Thickness(0));
    readonly StackPanel _lineStack = new();
    readonly StackPanel _line = new() { Orientation = Orientation.Horizontal };
    // the ring at the left of the hour's line and the pickups' row under it, centred across both
    readonly StackPanel _head = new() { Orientation = Orientation.Horizontal };
    readonly Border _ringHost = new() { VerticalAlignment = VerticalAlignment.Center };
    readonly StackPanel _col = new() { VerticalAlignment = VerticalAlignment.Center };
    readonly WrapPanel _stats = new() { Orientation = Orientation.Horizontal, Visibility = Visibility.Collapsed, MaxWidth = 380 };
    readonly Border _noteHost = new() { HorizontalAlignment = HorizontalAlignment.Left, Visibility = Visibility.Collapsed };
    readonly StackPanel _fightStack = new();
    // the pickups: a row of item tiles under the hour on the card's face, a foil rule after the best drop
    readonly Border _pickHost = new() { HorizontalAlignment = HorizontalAlignment.Left, Visibility = Visibility.Collapsed };
    static readonly PickStrip.Look Strip = new(24, 4, true, Foil(.95, 30), GoldB);
    protected override PickStrip.Look StripLook => Strip;
    // the timer's foil tag: kept, so its hop lives through the second's redraws
    readonly Grid _bean = new() { VerticalAlignment = VerticalAlignment.Center, Visibility = Visibility.Collapsed };
    readonly Border _beanLip = new() { CornerRadius = new CornerRadius(7), RenderTransform = new TranslateTransform(0, 2) };
    readonly Border _beanFace = new() { CornerRadius = new CornerRadius(7), Padding = new Thickness(10, 3, 11, 4), BorderThickness = new Thickness(1), BorderBrush = Wk.S(0xFFFFFF, .9) };
    readonly TextBlock _beanName = Wk.Words("", 11, InkB, Face, FontWeights.SemiBold), _beanClock;
    readonly TranslateTransform _beanUp = new();
    readonly ScaleTransform _beanSquash = new();
    FrameworkElement? _ring, _hour;

    public HudHoloCard() : base(new Thickness(12, 66, 36, 44), HudKit.Text)
    {
        UseRows(HoloRow.Pitch, _ => new HoloRow(), 18, HoloGap);
        _beanClock = Figure("", 13, InkB, Face, FontWeights.Bold);
        _beanClock.Margin = new Thickness(5, 0, 0, 0);
        _beanFace.Child = new Grid { Children = { new Border { CornerRadius = new CornerRadius(6), Margin = new Thickness(-10, -3, -11, -4), Background = Foil(.35, 30) }, new StackPanel { Orientation = Orientation.Horizontal, Children = { _beanName, _beanClock } } } };
        _bean.Children.Add(_beanLip);
        _bean.Children.Add(_beanFace);
        _bean.Children.Add(new Border { Height = 3, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(7, 2, 9, 0), CornerRadius = new CornerRadius(1.5), Background = Wk.S(0xFFFFFF, .7), IsHitTestVisible = false });
        _bean.RenderTransformOrigin = new Point(.5, 1);
        _bean.RenderTransform = new TransformGroup { Children = { _beanSquash, _beanUp } };
        _stats.Margin = new Thickness(0, 5, 0, 0);
        _col.Children.Add(_line); _col.Children.Add(_pickHost);
        _head.Children.Add(_ringHost); _head.Children.Add(_col);
        _lineStack.Children.Add(_head); _lineStack.Children.Add(_stats);
        _blob.Content.Child = _lineStack;
        TextElement.SetFontFamily(_fightStack, Face);
        _panel.Content.Child = _fightStack;
        _panel.Root.Visibility = Visibility.Collapsed;
        _body.Children.Add(_blob.Root);
        _body.Children.Add(_noteHost);
        _body.Children.Add(_panel.Root);
        BuildRun();   // the dungeon run's head and splits on the card's face (Ui/Themes/Wacky/HudHoloCard.Run.cs)
        Ground(Op);
    }

    public override FrameworkElement? FightFrame => _panel.Root.Visibility == Visibility.Visible ? _panel.Root : null;

    protected override void Ground(double opacity)
    {
        double a = Math.Max(.72, opacity);   // the card's face carries the figures' ground: it thins, never goes
        _blob.Body.Background = Wk.Lin(0, 0, 0, 1, (Navy2, a, 0), (Navy, a, .55), (0x070A18, a, 1));
        _panel.Body.Background = Wk.Lin(0, 0, 0, 1, (Navy2, a, 0), (Navy, a, .4), (0x070A18, a, 1));
    }

    /// <summary>Text with a dark line under it (a copy a step down, not an effect), in the app's own face.</summary>
    static Grid Dropped(string s, double size, Brush fg, uint drop, FontWeight w, bool figure = true, double at = 1.4)
    {
        var g = new Grid { VerticalAlignment = VerticalAlignment.Center };
        var d = figure ? Figure(s, size, Wk.S(drop, .9), Face, w) : Wk.Words(s, size, Wk.S(drop, .9), Face, w);
        d.RenderTransform = new TranslateTransform(0, at);
        g.Children.Add(d);
        g.Children.Add(figure ? Figure(s, size, fg, Face, w) : Wk.Words(s, size, fg, Face, w));
        return g;
    }

    /// <summary>A foil chip: white under rainbow foil, a white rim, the words in ink.</summary>
    static Grid FoilChip(UIElement child, double radius, Thickness pad, double foil = .5)
    {
        var g = new Grid { VerticalAlignment = VerticalAlignment.Center };
        g.Children.Add(new Border { CornerRadius = new CornerRadius(radius), Background = Wk.S(0x000000, .45), RenderTransform = new TranslateTransform(0, 1.5) });
        g.Children.Add(new Border { CornerRadius = new CornerRadius(radius), Background = Wk.Lin(0, 0, 0, 1, (0xFFFFFF, 1, 0), (0xD6DEF5, 1, 1)) });
        g.Children.Add(new Border { CornerRadius = new CornerRadius(radius), Background = Foil(foil, 34) });
        g.Children.Add(new Border { CornerRadius = new CornerRadius(radius), BorderBrush = Wk.S(0xFFFFFF, .95), BorderThickness = new Thickness(1), Padding = pad, Child = child });
        return g;
    }

    // ---- the line

    protected override void DrawLine(Figures f)
    {
        _line.Children.Clear(); _stats.Children.Clear();
        _ringHost.Child = null;
        _ring = _hour = null;
        // in a dungeon run: the run's clock, splits and verdict in place of the hour (Ui/Themes/Wacky/HudHoloCard.Run.cs)
        if (RunOf() is { } rs) { DrawRun(rs, f); return; }
        EndRun();
        bool waiting = !f.Counted || f.Rate.Length == 0;
        bool mark = f.Abyss && f.Counted && f.Icon.Length > 0;
        if (mark)
        {
            var m = new Grid { Width = 44, Height = 44, Margin = new Thickness(0, 0, 10, 0), VerticalAlignment = VerticalAlignment.Center };
            m.Children.Add(new Ellipse { Fill = Wk.Rad(.4, .35, .7, (0x26305E, 1, 0), (Deep, 1, 1)), Stroke = Wk.S(Ice, .55), StrokeThickness = 1.2 });
            var a = AbyssMark(f.Icon, 32); ((FrameworkElement)a).HorizontalAlignment = HorizontalAlignment.Center;
            m.Children.Add(a);
            _ringHost.Child = m; _ring = m;
        }
        else if (!waiting && f.Share >= 0 && !f.Warm) { _ring = Ring(f.Share, f.Beat); _ringHost.Child = _ring; }
        if (waiting)
        {
            var w = Dropped(Waiting(f.Abyss), 14.5, WhiteB, Deep, FontWeights.Bold, false);
            w.Margin = new Thickness(4, 4, 4, 4);
            _line.Children.Add(w); _hour = w;
        }
        else
        {
            var hour = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            // warming up: dim steel, not the gold, still on its drop line so it reads
            var rate = Dropped(f.Rate, f.Warm ? 21 : 29, f.Warm ? SteelB : f.Abyss ? WhiteB : GoldB, Deep, FontWeights.Bold, true, 1.8);
            hour.Children.Add(rate);
            if (f.Unit.Length > 0)
            {
                var u = Dropped(f.Unit, 13.5, f.Abyss ? WhiteB : GoldB, Deep, FontWeights.SemiBold, false);
                u.VerticalAlignment = VerticalAlignment.Bottom; u.Margin = new Thickness(3, 0, 0, 6); u.Opacity = .9;
                hour.Children.Add(u);
            }
            _line.Children.Add(hour); _hour = hour;
            if (f.Total.Length > 0)
            {
                var sp = new StackPanel { Orientation = Orientation.Horizontal };
                sp.Children.Add(Figure(f.Total, 14.5, InkB, Face, FontWeights.Bold));
                var w = Wk.Words(f.TotalWord, 11, Wk.S(0x3A4670), Face, FontWeights.SemiBold); w.Margin = new Thickness(4, 1, 0, 0);
                sp.Children.Add(w);
                var pill = FoilChip(sp, 9, new Thickness(10, 2, 11, 3), .42);
                pill.Margin = new Thickness(11, 2, 0, 0);
                _line.Children.Add(pill);
            }
        }
        // the Abyss's kills, deaths and Kinah: small midnight chips in their colours
        foreach (var (fig, word, tone) in f.Stats)
        {
            Brush c = ReferenceEquals(tone, Rec) ? WhiteB : ReferenceEquals(tone, Gold) ? GoldB : WhiteB;
            var sp = new StackPanel { Orientation = Orientation.Horizontal };
            sp.Children.Add(Figure(fig, 12.5, c, Face, FontWeights.Bold));
            bool red = ReferenceEquals(tone, Rec);
            var w = Wk.Words(word, 11, Wk.S(red ? 0xFFFFFFu : Ice), Face, FontWeights.Medium); w.Margin = new Thickness(4, 1, 0, 0);
            sp.Children.Add(w);
            // deaths on a red chip, so the red reads at a glance
            _stats.Children.Add(new Border { CornerRadius = new CornerRadius(6), Background = red ? Wk.Lin(0, 0, 0, 1, (0xFF6A80, 1, 0), (0xD42A48, 1, 1)) : Wk.S(Navy, .88), BorderBrush = Wk.S(0xFFFFFF, red ? .5 : .16), BorderThickness = new Thickness(1), Padding = new Thickness(8, 1, 9, 2), Margin = new Thickness(0, 0, 5, 0), Child = sp });
        }
        if (_stats.Children.Count > 0) _stats.Margin = new Thickness(_ring != null ? 54 : 0, 5, 0, 0);
        _stats.Visibility = _stats.Children.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        // the best drop and the last three: a second row on the card's face, under the hour
        _pickHost.Child = StripFor(f);
        _pickHost.Margin = new Thickness(3, 3, 0, 2);
        DrawNote(f);
        PlaceBean();
    }

    /// <summary>The note: a midnight chip in its tone under the card.</summary>
    void DrawNote(Figures f)
    {
        if (f.Note.Length > 0)
        {
            var nt = Wk.Words(f.Note, 11.5, f.NoteTone, Face, FontWeights.SemiBold);
            nt.TextTrimming = TextTrimming.CharacterEllipsis; nt.MaxWidth = 360;
            _noteHost.Child = new Border { CornerRadius = new CornerRadius(7), Background = Wk.S(Navy, .92), BorderBrush = Wk.S(Ice, .35), BorderThickness = new Thickness(1), Padding = new Thickness(10, 2, 11, 3), Margin = new Thickness(8, 9, 0, 0), Child = nt };
        }
        else _noteHost.Child = null;
        _noteHost.Visibility = f.Note.Length > 0 && !KinahHidden ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>The ring of your best: a cyan arc round a midnight orb in a foil rim, the share in white (gold with a star once beaten).</summary>
    FrameworkElement Ring(double share, bool beat)
    {
        const double D = 46;
        var g = new Grid { Width = D, Height = D, Margin = new Thickness(0, 0, 10, 0), VerticalAlignment = VerticalAlignment.Center };
        g.Children.Add(new Ellipse { Fill = Wk.S(0x000000, .5), RenderTransform = new TranslateTransform(0, 2) });
        g.Children.Add(new Ellipse { Fill = Wk.Lin(0, 0, 1, 1, (0xF2F5FF, 1, 0), (0x8F98B8, 1, .5), (0xF6F8FF, 1, 1)) });
        g.Children.Add(new Ellipse { Fill = Foil(.75, 30) });
        g.Children.Add(new Ellipse { Margin = new Thickness(2.5), Fill = Wk.Rad(.42, .35, .7, (0x26305E, 1, 0), (Deep, 1, 1)) });
        g.Children.Add(Wk.Arc(D, 5.5, 1, Wk.S(Ice, .14), PenLineCap.Round, 5));
        g.Children.Add(Wk.Arc(D, 5.5, beat ? 1 : share, beat ? GoldB : Wk.S(Cyan), PenLineCap.Round, 5));
        var t = Figure(beat ? "★" : $"{Math.Round(Math.Clamp(share, 0, 1) * 100):0}", beat ? 15 : 13, beat ? GoldB : WhiteB, Face, FontWeights.Bold);
        t.HorizontalAlignment = HorizontalAlignment.Center;
        g.Children.Add(t);
        return g;
    }

    // ---- the timer's tag

    protected override void DrawTag()
    {
        if (Due.FirstOrDefault() is not { } i)
        {
            _bean.Visibility = Visibility.Collapsed;
            if (_runOn && Figs is { } f0 && !KinahHidden) { DrawLine(f0); return; }
            PlaceBean(); return;
        }
        var (name, clock, tone) = TagWords(i, DueNow);
        var (hi, lo, lip) = tone == TimerBadge.Tone.On ? (0xD4FFEAu, 0x5BE3A4u, 0x0E5A3Au) : tone == TimerBadge.Tone.Soon ? (0xFFF4CCu, 0xF2C75Au, 0x6A4A00u) : (0xF6F8FFu, 0xC2CCEEu, 0x2A3460u);
        _beanFace.Background = Wk.Lin(0, 0, 0, 1, (hi, 1, 0), (lo, 1, 1));
        _beanLip.Background = Wk.S(lip);
        _beanName.Text = name; _beanName.Visibility = name.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        _beanClock.Text = clock;
        _bean.Visibility = Visibility.Visible;
        // in a run the line's forms leave room for the tag, or put it on the card's top edge: drawn again for its new width
        if (_runOn && Figs is { } f && !KinahHidden) { DrawLine(f); return; }
        PlaceBean();
    }

    void PlaceBean()
    {
        bool onPanel = KinahHidden && _panel.Root.Visibility == Visibility.Visible;
        bool onTop = !onPanel && _runOn && _beanAbove;
        // in a dungeon the tag sits on the fight card's top edge; in a run without room on its line, on the line card's
        if (onPanel && _bean.Parent != _panel.Root) { Detach(_bean); _panel.Root.Children.Add(_bean); }
        else if (onTop && _bean.Parent != _blob.Root) { Detach(_bean); _blob.Root.Children.Add(_bean); }
        else if (!onPanel && !onTop && _bean.Parent != _line) { Detach(_bean); _line.Children.Add(_bean); }
        _bean.HorizontalAlignment = onPanel || onTop ? HorizontalAlignment.Right : HorizontalAlignment.Left;
        _bean.VerticalAlignment = onPanel || onTop ? VerticalAlignment.Top : VerticalAlignment.Center;
        _bean.Margin = onPanel ? new Thickness(0, -15, 72, 0) : onTop ? new Thickness(0, -14, 18, 0) : new Thickness(12, 0, 0, 0);
        bool lineShown = !KinahHidden || (_bean.Parent == _line && _bean.Visibility == Visibility.Visible);
        _blob.Root.Visibility = lineShown ? Visibility.Visible : Visibility.Collapsed;
        foreach (UIElement ch in _line.Children) if (!ReferenceEquals(ch, _bean)) ch.Visibility = KinahHidden ? Visibility.Collapsed : Visibility.Visible;
        _ringHost.Visibility = KinahHidden ? Visibility.Collapsed : Visibility.Visible;
        _panel.Root.Margin = new Thickness(0, lineShown ? 16 : 0, 0, 0);
        // the pickups stay with the Kinah line, a fight's card up or not (Steven, 2026-11-19: on ALL buddies)
        _pickHost.Visibility = _pickHost.Child != null && !KinahHidden ? Visibility.Visible : Visibility.Collapsed;
    }

    static void Detach(FrameworkElement el)
    {
        if (el.Parent is Panel p) p.Children.Remove(el);
        else if (el.Parent is Decorator d) d.Child = null;
    }

    protected override void KinahShown(bool shown)
    {
        if (!shown) { _stats.Visibility = Visibility.Collapsed; _noteHost.Visibility = Visibility.Collapsed; _pickHost.Visibility = Visibility.Collapsed; }
        PlaceBean();
    }

    // ---- the fight

    protected override void DrawFight(Encounter? e, DateTime now)
    {
        _fightStack.Children.Clear();
        if (e == null) { _panel.Root.Visibility = Visibility.Collapsed; PlaceBean(); return; }
        _panel.Root.Width = FightWidth;
        bool beanOn = KinahHidden && _bean.Visibility == Visibility.Visible;
        var head = new Grid { Margin = new Thickness(13, beanOn ? 13 : 9, 12, 7) };
        for (int i = 0; i < 3; i++) head.ColumnDefinitions.Add(new ColumnDefinition { Width = i == 0 ? new GridLength(1, GridUnitType.Star) : GridLength.Auto });
        var title = Dropped(e.Title, 15.5, WhiteB, Deep, FontWeights.Bold, false);
        foreach (var t in title.Children.OfType<TextBlock>()) t.TextTrimming = TextTrimming.CharacterEllipsis;
        head.Children.Add(title);
        if (OutcomeText(e) is { Length: > 0 } outcome)
        {
            var o = FoilChip(Wk.Words(outcome, 10.5, Wk.S(Won(e) ? Ink : e.Outcome == "wipe" ? 0x7A0A1Eu : Ink), Face, FontWeights.Bold), 9, new Thickness(8, 0, 8, .5), Won(e) ? .55 : .2);
            o.Margin = new Thickness(8, 0, 8, 0);
            Grid.SetColumn(o, 1); head.Children.Add(o);
        }
        var clk = e.Waiting ? Dropped(L.T("Ready"), 14, Wk.S(Mint), Deep, FontWeights.Bold, false) : Dropped(Clock(e.Duration(now)), 18, e.Live ? Wk.S(Cyan) : WhiteB, Deep, FontWeights.Bold, true);
        if (OutcomeText(e).Length == 0) clk.Margin = new Thickness(8, 0, 0, 0);
        Grid.SetColumn(clk, 2); head.Children.Add(clk);
        _fightStack.Children.Add(head);
        // the foil rule under the card's name
        _fightStack.Children.Add(new Border { Height = 2, Margin = new Thickness(12, 0, 12, 0), CornerRadius = new CornerRadius(1), Background = Foil(.95, 60) });
        if (!e.Waiting && HpOf(e) is double hp) _fightStack.Children.Add(HpRow(e, hp, now));
        if (e.Waiting)
        {
            var w = Wk.Words(e.ReadyText, 12, Wk.S(Ice, .85), Face, FontWeights.Medium); w.Margin = new Thickness(13, 9, 12, 10); w.TextWrapping = TextWrapping.Wrap;
            _fightStack.Children.Add(w);
            if (PartyBeforeHit(e)) { var party = new StackPanel { Margin = new Thickness(9, 2, 9, 9) }; PlaceRows(party, e, now); _fightStack.Children.Add(party); }
        }
        else
        {
            var rows = new StackPanel { Margin = new Thickness(9, 11, 9, 9) };
            PlaceRows(rows, e, now);
            if (e.HasDamage) { var foot = ParserFoot.Line(e, now, 12); foot.Margin = new Thickness(5, 7, 5, 0); rows.Children.Add(foot); }
            _fightStack.Children.Add(rows);
        }
        _panel.Root.Visibility = Visibility.Visible;
        PlaceBean();
    }

    /// <summary>The rows at their pitch for the meter's mode (expanded rows carry the damage and crit under the name).</summary>
    void PlaceRows(Panel host, Encounter e, DateTime now)
    {
        if (Meter is { } m) m.Pitch = HoloRow.Pitch + (ParserRows.ExpandedPref ? HoloRow.Extra : 0);
        PutRows(host, e, now);
    }

    /// <summary>The boss's HP: a rose-to-amber bar in a midnight groove, notched at the thirds, and "47% · 12.84M left".</summary>
    FrameworkElement HpRow(Encounter e, double hp, DateTime now)
    {
        var row = new Grid { Margin = new Thickness(13, 10, 12, 0) };
        row.ColumnDefinitions.Add(new ColumnDefinition()); row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var bar = new Grid { Height = 12, VerticalAlignment = VerticalAlignment.Center };
        bar.Children.Add(new Border { CornerRadius = new CornerRadius(3), Background = Wk.Lin(0, 0, 0, 1, (0x03050C, 1, 0), (0x101632, 1, 1)), BorderBrush = Wk.S(0xFFFFFF, .18), BorderThickness = new Thickness(1) });
        var track = new Grid { Margin = new Thickness(1.5) };
        bool low = hp < .34;
        var fill = new Grid { HorizontalAlignment = HorizontalAlignment.Left, Width = 0 };
        fill.Children.Add(new Border { CornerRadius = new CornerRadius(2), Background = low ? Wk.Lin(0, 0, 1, 0, (0xC81E44, 1, 0), (0xFF3A6A, 1, 1)) : Wk.Lin(0, 0, 1, 0, (0xFF3A6A, 1, 0), (0xFF8A3A, 1, 1)) });
        fill.Children.Add(new Border { CornerRadius = new CornerRadius(2), Background = Wk.Lin(0, 0, 0, 1, (0xFFFFFF, .4, 0), (0xFFFFFF, 0, .45), (0x000000, .25, 1)) });
        track.SizeChanged += (_, _) => fill.Width = hp <= 0 ? 0 : Math.Max(4, track.ActualWidth * hp);
        track.Children.Add(fill);
        HpGhost(track, e, hp, now, 2);
        bar.Children.Add(track);
        var ticks = HpTicks(); foreach (var t in ticks.Children.OfType<Border>()) { t.Width = 2.5; t.Margin = new Thickness(0, 0, -1.25, 0); t.Background = Wk.S(0x000000, .95); }
        bar.Children.Add(ticks);
        row.Children.Add(bar);
        var words = new TextBlock { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 0, 0), FontFamily = Face };
        Typography.SetNumeralAlignment(words, FontNumeralAlignment.Tabular);
        words.Inlines.Add(new Run($"{Math.Round(hp * 100):0}%") { FontSize = 13.5, FontWeight = FontWeights.Bold, Foreground = Wk.S(low ? 0xFF7A90u : 0xFF9AB0u) });
        words.Inlines.Add(new Run($" · {HpLeftText(e)}") { FontSize = 10.5, FontWeight = FontWeights.Medium, Foreground = Wk.S(Ice, .8) });
        Grid.SetColumn(words, 1); row.Children.Add(words);
        return row;
    }

    // ---- the moments

    /// <summary>A foil chip in <paramref name="c"/>: its colour lit at the top, washed in foil, a white rim and a light across it, <paramref name="content"/> in it.</summary>
    static Grid Bubble(double w, double h, uint c, UIElement? content)
    {
        var g = new Grid { Width = w, Height = h };
        var r = new CornerRadius(h * .3);
        g.Children.Add(new Border { CornerRadius = r, Background = Wk.S(0x000000, .5), RenderTransform = new TranslateTransform(0, 3) });
        g.Children.Add(new Border { CornerRadius = r, Background = Wk.Lin(0, 0, 0, 1, (Wk.Shade(c, .3), 1, 0), (c, 1, .5), (Wk.Shade(c, -.3), 1, 1)) });
        g.Children.Add(new Border { CornerRadius = r, Background = Foil(.3, 36) });
        g.Children.Add(new Border { CornerRadius = r, Background = Wk.Lin(0, 0, 1, 1, (0xFFFFFF, 0, .25), (0xFFFFFF, .5, .38), (0xFFFFFF, 0, .5)) });
        g.Children.Add(new Border { CornerRadius = r, BorderBrush = Wk.S(0xFFFFFF, .95), BorderThickness = new Thickness(1.5) });
        if (content is FrameworkElement fe) { fe.HorizontalAlignment = HorizontalAlignment.Center; fe.VerticalAlignment = VerticalAlignment.Center; g.Children.Add(fe); }
        return g;
    }

    static readonly uint[] Glints = { 0xFF7AD9, 0x8FE8FF, 0xFFF39A, 0xB59BFF, 0x8AFFCB, 0xFFFFFF };

    /// <summary>Glints for a chip's burst: small foil diamonds in the foil's colours (a white core), on a canvas round its middle.</summary>
    static Wk.Bits Droplets(uint c, int n, double size, double room)
    {
        var bits = new Wk.Bits(room, room);
        for (int i = 0; i < n; i++)
        {
            double s = size * (i % 3 == 0 ? 1.15 : .85);
            uint k = i % 2 == 0 ? c : Glints[i % Glints.Length];
            bits.Add(new Border { Width = s, Height = s, CornerRadius = new CornerRadius(1), Background = Wk.Rad(.5, .5, .7, (0xFFFFFF, 1, 0), (k, 1, .6), (Wk.Shade(k, -.2), 1, 1)), RenderTransformOrigin = new Point(.5, .5), RenderTransform = new RotateTransform(45) });
        }
        return bits;
    }

    /// <param name="up">How far the glints may fly up from the chip's middle (the room the window has over it).</param>
    static void Pop(Wk.Bits bits, double t, double reach, int seed, double up = double.MaxValue)
    {
        var rnd = new Random(seed);
        // the ones flung up go only as high as the window lets them (the burst flattens, the window never cuts it)
        double uk = Math.Clamp(up / (reach * 1.2 * .8), 0, 1);
        for (int i = 0; i < bits.Count; i++)
        {
            double a = 2 * Math.PI * i / bits.Count + rnd.NextDouble() * .5, d = reach * (.7 + rnd.NextDouble() * .5);
            double k = Out(t), dy = Math.Sin(a) * d * k * .8;
            bits.Set(i, Math.Cos(a) * d * k, (dy < 0 ? dy * uk : dy * .45) + 6 * t * t, 45 + 180 * t, 1 - .5 * t, t <= 0 ? 0 : 1 - InOut(t));
        }
    }

    /// <summary>The card behind the figures gives a little (the figures stay still).</summary>
    static void Jiggle(Card j, double ms, double amount)
    {
        Motion.Play(j.Layer, ms, q =>
        {
            double w = Wob(q, 2, 4.5);
            j.Wobble.ScaleX = 1 + amount * w; j.Wobble.ScaleY = 1 - amount * .8 * w;
        }, () => { j.Wobble.ScaleX = j.Wobble.ScaleY = 1; });
    }

    /// <summary>A chip that flips up out of the card (a turn on its edge as it rises), hovers, then bursts into glints.</summary>
    void Float(Grid bubble, uint c, Point from, double rise, string slot, double ms, double popAt, int drops, double dropSize)
    {
        // whole inside the window at its biggest (about 0.82 of its width and 0.75 of its height either side of its middle),
        // and it rises only as high as the room over it lets it
        var win = WinSize;
        const double pad = 4;
        double hw = bubble.Width * .82, hh = bubble.Height * .75;
        double x = win.Width > 0 ? Math.Clamp(from.X, pad + hw, Math.Max(pad + hw, win.Width - pad - hw)) : from.X;
        double over = from.Y - pad - hh - 9;
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
            double flip = Seg(q, 0, .24);
            double hover = Math.Sin(Seg(q, .22, popAt) * Math.PI * 2) * (1 - Seg(q, .22, popAt));
            double pop = Seg(q, popAt, popAt + .06);
            // a card's flip: its width turns from its edge (0) through a slight overshoot to face on
            double face = Math.Abs(Math.Cos((1 - flip) * Math.PI * 1.5)) * Back(flip, 1.4);
            bs.ScaleX = Math.Max(.02, face) * (1 + .25 * pop); bs.ScaleY = Back(Seg(q, 0, .16), 1.4) * (1 + .25 * pop);
            bt.Y = -rise * up - 5 * Seg(q, .22, popAt) + 2 * hover;
            bubble.Opacity = Seg(q, 0, .05) * (1 - pop);
            bits.Host.RenderTransform = new TranslateTransform(0, bt.Y);
            Pop(bits, Seg(q, popAt, 1), bubble.Width * .55, 3, from.Y + bt.Y - pad - dropSize * 1.3);
            s.Set(1, 1, 0, 0, 0, 1);
        });
    }

    static Grid Say(string s, double size, uint edge) => Wk.Outlined(s, Face, FontWeights.Bold, size, WhiteB, Wk.S(edge), 2.6);

    protected override void Pickup(Pick p, bool held)
    {
        if (KinahHidden || _hour == null) return;
        var words = Say(PickWords(p), 15.5, 0x5A2A00);
        var b = Bubble(Math.Max(66, words.Width + 24), 40, 0xFFA43A, words);
        var at = At(_hour, .92, .15);
        Jiggle(_blob, 700, .025);
        Float(b, 0xFFC25A, new Point(at.X + 18, at.Y - 8), 24, "pickup", 1500, .64, 10, 6);
    }

    protected override void RareDrop(Pick p, bool held)
    {
        if (KinahHidden) return;
        var c = Wk.Hex(ColorOf(p.Color, p.Grade));
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(Tile(p, 28, glow: true));
        var words = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(7, 0, 0, 0) };
        words.Children.Add(Wk.Outlined((p.Grade.Length > 0 ? L.T(p.Grade) : L.T("Rare")).ToUpperInvariant() + "!", Face, FontWeights.Bold, 11, GoldB, Wk.S(Deep), 2.2));
        words.Children.Add(Wk.Outlined(p.Name, Face, FontWeights.Bold, 13.5, WhiteB, Wk.S(Deep), 2.4));
        row.Children.Add(words);
        row.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        var b = Bubble(Math.Max(140, row.DesiredSize.Width + 44), 50, c, row);
        // it flips up out of the card's top edge and hovers above it, off the figures
        var at = At(_blob.Root, .72, 0);
        Jiggle(_blob, 900, .035);
        Float(b, c, new Point(at.X, at.Y - 2), 18, "rare", 2700, .66, 14, 7);
    }

    protected override void BestPassed(bool held)
    {
        if (KinahHidden) return;
        // the card lifts, settling back
        Motion.Play(_blob.Layer, 1200, q =>
        {
            double k = Math.Sin(Seg(q, 0, .3) * Math.PI / 2) * (1 - Seg(q, .3, 1)) + .4 * Wob(Seg(q, .3, 1), 2, 4) * (1 - Seg(q, .3, 1));
            _blob.Wobble.ScaleX = 1 + .03 * k; _blob.Wobble.ScaleY = 1 + .06 * k;
        }, () => { _blob.Wobble.ScaleX = _blob.Wobble.ScaleY = 1; });
        var words = Say(L.T("NEW BEST!"), 14, 0x5A3A00);
        var b = Bubble(Math.Max(120, words.Width + 28), 40, 0xFFC83A, words);
        var at = At(_ring ?? _hour, .6, 0);
        Float(b, 0xFFE29A, new Point(at.X + 44, at.Y - 6), 16, "best", 2000, .66, 12, 6);
    }

    protected override void EntryOpened(TimerBoard.Item i, bool held)
    {
        if (_bean.Visibility != Visibility.Visible) return;
        // the tag hops: up, down with a squash, two smaller hops (the tag moves, its words stay as they are)
        Motion.Play(_bean, 1100, q =>
        {
            double t = q * 3, hop = Math.Abs(Math.Sin(t * Math.PI)) * Math.Pow(.42, Math.Floor(t));
            double land = Math.Max(0, 1 - Math.Abs(t - Math.Round(t)) * 8) * Math.Pow(.5, Math.Round(t) - 1) * (t > .5 ? 1 : 0);
            _beanUp.Y = -11 * hop;
            _beanSquash.ScaleX = 1 + .08 * land - .03 * hop; _beanSquash.ScaleY = 1 - .12 * land + .05 * hop;
        }, () => { _beanUp.Y = 0; _beanSquash.ScaleX = _beanSquash.ScaleY = 1; });
    }

    protected override void FightStarted(Encounter e, bool held)
    {
        if (_panel.Root.Visibility != Visibility.Visible) return;
        // the card drops in, settling, and a band of light sweeps across its foil
        Motion.Play(_panel.Layer, 800, q =>
        {
            double k = Seg(q, 0, .3);
            _panel.Wobble.ScaleY = Lerp(.9, 1, Back(k, 1.8)) - .02 * Wob(Seg(q, .3, 1), 2, 4);
            _panel.Wobble.ScaleX = 1 + .015 * (1 - k);
        }, () => { _panel.Wobble.ScaleX = _panel.Wobble.ScaleY = 1; });
        double w = Math.Max(_panel.Root.ActualWidth, 260), h = Math.Max(_panel.Root.ActualHeight, 60);
        var band = new Border { Width = 70, Height = h * 1.6, Background = Wk.Lin(0, 0, 1, 0, (0xFFFFFF, 0, 0), (0xFFFFFF, .38, .5), (0xFFFFFF, 0, 1)), RenderTransform = new RotateTransform(22), RenderTransformOrigin = new Point(.5, .5) };
        var cv = new Canvas { Width = w, Height = h, Clip = new RectangleGeometry(new Rect(0, 0, w, h), 14, 14), Children = { band } };
        Canvas.SetTop(band, -h * .3);
        var at = At(_panel.Root, .5, .5);
        var s = Spawn(cv, at, "fight");
        Play(s, "fight", 1100, q =>
        {
            Canvas.SetLeft(band, Lerp(-90, w + 20, InOut(Seg(q, .1, .9))));
            s.Set(1, 1, 0, 0, 0, Seg(q, 0, .1) * (1 - Seg(q, .85, 1)));
        });
        // and a cyan GO! chip flips up off its top edge (the family's fight call)
        var go = Say(L.T("GO!"), 20, 0x063A4A);
        var top = At(_panel.Root, .5, 0);
        Float(Bubble(80, 40, 0x35C8E8, go), 0x8FEFFF, new Point(top.X, top.Y - 6), 14, "fight-go", 1500, .62, 10, 6);
    }

    protected override void FightEnded(Encounter e, bool held)
    {
        if (_panel.Root.Visibility != Visibility.Visible) return;
        var top = At(_panel.Root, .5, 0);
        var at = new Point(top.X, top.Y - 18);   // on the card's top edge: the rows stay in view
        if (e.Outcome == "wipe")
        {
            var words = Say(L.T("Wiped").ToUpperInvariant(), 18, 0x4A0012);
            var b = Bubble(Math.Max(110, words.Width + 30), 42, 0xE8344E, words);
            var bs = new ScaleTransform(); b.RenderTransform = bs; b.RenderTransformOrigin = new Point(.5, 1);
            var s = Spawn(new Grid { Width = 160, Height = 80, Children = { b } }, at, "end");
            Play(s, "end", 2000, q =>
            {
                double inn = Back(Seg(q, 0, .16), 1.6), droop = InOut(Seg(q, .35, .85));
                bs.ScaleX = inn; bs.ScaleY = inn * (1 - .08 * droop);
                s.Set(1, 1, 6 * droop, 0, 12 * droop, Seg(q, 0, .05) * (1 - Seg(q, .82, 1)));
            });
            return;
        }
        if (!Won(e)) return;
        // foil confetti bursts up from the card and rains down over its edge, round a gold K.O.! chip
        double w = Math.Max(_panel.Root.ActualWidth, 260);
        var bits = new Wk.Bits(w, 200);
        for (int i = 0; i < 34; i++)
            bits.Add(new Border { Width = i % 3 == 0 ? 6 : 4, Height = i % 3 == 0 ? 6 : 9, CornerRadius = new CornerRadius(1), Background = Wk.Lin(0, 0, 1, 1, (0xFFFFFF, 1, 0), (Glints[i % Glints.Length], 1, .5), (Wk.Shade(Glints[i % Glints.Length], -.25), 1, 1)) });
        var ko = Say(L.T("K.O.!"), 22, 0x5A3A00);
        var bubble = Bubble(Math.Max(96, ko.Width + 30), 44, 0xFFC83A, ko);
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
                // the confetti falls back to the card's edge and is gone there: it never rains on the rows
                bits.Set(i, x, y, f.Rot + f.Spin * t, 1, (t <= 0 ? 0 : 1) * (1 - Seg(q, .7, .95)) * (1 - Seg(y, 2, 16)));
            }
            double inn = Back(Seg(q, .02, .2), 2), pop = Seg(q, .66, .74);
            bsc.ScaleX = bsc.ScaleY = inn * (1 + .25 * pop);
            bubble.Opacity = Seg(q, .02, .06) * (1 - pop);
            bubble.RenderTransform = bsc;
            sp.Set(1, 1, 0, 0, 0, 1);
        });
    }

    // ---- the member's skills card: a trading card of its own

    /// <summary>A chip: a gradient from <paramref name="hi"/> to <paramref name="lo"/>, a fine rim in <paramref name="lip"/>, a light strip across its top.</summary>
    static Grid Candy(UIElement child, uint hi, uint lo, uint lip, double radius, Thickness pad)
    {
        var g = new Grid { VerticalAlignment = VerticalAlignment.Center };
        g.Children.Add(new Border { CornerRadius = new CornerRadius(radius), Background = Wk.Lin(0, 0, 0, 1, (hi, 1, 0), (lo, 1, 1)), BorderBrush = Wk.S(lip, .9), BorderThickness = new Thickness(1), Padding = pad, Child = child });
        g.Children.Add(new Border { Height = 2, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(radius * .7, 1.5, radius * .9, 0), CornerRadius = new CornerRadius(1), Background = Wk.S(0xFFFFFF, .5), IsHitTestVisible = false });
        return g;
    }

    /// <summary>
    /// A member's skills card as a trading card: a midnight face in a foil frame. The class's emblem and the name in its class's
    /// colour (gold for you), the Combat Power on a foil chip after it, the class in steel; the DPS on a white chip, the share on
    /// a cyan one, the damage on an ice one; the rates in gold; then the abilities, each name over a groove with a bar of the
    /// class's colour washed in foil as long as its damage against the top one (they slide in when the card opens), share,
    /// damage (crit, back) right-aligned and tabular; "N more"; the foot in steel with heals mint and deaths cherry.
    /// </summary>
    public FrameworkElement DrawCard(MemberCard c)
    {
        double w = WkCard.Width(c, 278);
        var card0 = new Card(14, new Thickness(12, 10, 12, 12));
        card0.Body.Background = Wk.Lin(0, 0, 0, 1, (Navy2, 1, 0), (Navy, 1, .4), (0x070A18, 1, 1));
        card0.Root.Width = w;
        var ice = IceB;
        var steel = SteelB;
        var stack = new StackPanel();
        // the head
        var head = new Grid();
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var icon = ThemeText.Icon(c.Class, 20, c.Name); icon.Margin = new Thickness(0, 0, 7, 0);
        head.Children.Add(icon);
        TextBlock N(Brush b) { var t = Wk.Words(c.Name, 15, b, Face, FontWeights.Bold); t.TextTrimming = TextTrimming.CharacterEllipsis; return t; }
        var dropName = N(Wk.S(Deep, .9)); dropName.RenderTransform = new TranslateTransform(0, 1.4);
        var name = new Grid { Children = { dropName, N(c.You ? GoldB : new SolidColorBrush(c.NameColor)) } };
        var cpText = new TextBlock { FontFamily = Face, VerticalAlignment = VerticalAlignment.Center };
        cpText.Inlines.Add(new Run(L.T("CP") + " ") { FontSize = 8, FontWeight = FontWeights.Bold, Foreground = Wk.S(0x3A4670) });
        cpText.Inlines.Add(new Run(c.Cp) { FontSize = 11, FontWeight = FontWeights.Bold, Foreground = InkB });
        Typography.SetNumeralAlignment(cpText, FontNumeralAlignment.Tabular);
        var cp = FoilChip(cpText, 4, new Thickness(6, 0, 7, .5), .35);
        cp.Margin = new Thickness(8, 0, 0, 0); cp.Visibility = c.Cp.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        WkCard.Put(head, WkCard.NameLine(name, cp), 1);
        var cls = Wk.Words(c.ClassName, 10.5, steel, Face, FontWeights.SemiBold); cls.Margin = new Thickness(10, 1, 0, 0);
        WkCard.Put(head, cls, 2);
        stack.Children.Add(head);
        // the figures: three chips
        var figs = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 9, 0, 0) };
        // each a chip: its word small over the figure
        void Piece(string fig, string word, double size, Brush fg, uint hi, uint lo, uint lip)
        {
            var sp = new StackPanel();
            var wd = Wk.Words(word.ToUpperInvariant(), 7.5, Wk.S(0x3A4670), Face, FontWeights.Bold); wd.Margin = new Thickness(0, 0, 0, -2);
            sp.Children.Add(wd);
            sp.Children.Add(Figure(fig, size, fg, Face, FontWeights.Bold));
            var cd = Candy(sp, hi, lo, lip, 5, new Thickness(9, 3, 10, 2));
            cd.VerticalAlignment = VerticalAlignment.Bottom;
            cd.Margin = new Thickness(0, 0, 7, 0);
            figs.Children.Add(cd);
        }
        if (c.Dps.Length > 0) Piece(c.Dps, L.T("DPS"), 19, InkB, 0xFFFFFF, 0xDCE4F8, 0xFFFFFF);
        Piece(c.Share, L.T("share"), 15, InkB, 0xC8F7FF, 0x6AD8F0, 0xE6FBFF);
        Piece(c.Damage, L.T("damage"), 15, InkB, 0xEEF2FF, 0xB8C4EC, 0xEEF2FF);
        stack.Children.Add(figs);
        if (c.RatesLine.Length > 0)
        {
            var rates = Wk.Words(c.RatesLine, 10.5, Wk.S(GoldC, .92), Face, FontWeights.SemiBold);
            rates.TextWrapping = TextWrapping.Wrap; rates.Margin = new Thickness(1, 8, 0, 0);
            stack.Children.Add(rates);
        }
        // a foil rule across the card
        stack.Children.Add(new Border { Height = 1.5, CornerRadius = new CornerRadius(.75), Margin = new Thickness(0, 9, 0, 5), Background = Foil(.8, 60) });
        var bars = new List<ScaleTransform>();
        if (c.Abilities.Count > 0)
        {
            if (c.CritCol || c.BackCol)
            {
                var names = WkCard.Cols(c, 46, 54, 13);
                TextBlock Head(string s) => WkCard.Num(s.ToUpperInvariant(), 8, steel, Face, FontWeights.Bold);
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
                // the groove and its foil-washed bar; the bars slide in when the card opens
                var groove = new Grid { Height = 5, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(1, 0, 8, 2), ClipToBounds = true };
                groove.Children.Add(new Border { CornerRadius = new CornerRadius(1.5), Background = Wk.S(0x03050C), BorderBrush = Wk.S(0xFFFFFF, .1), BorderThickness = new Thickness(0, 0, 0, .8) });
                var bar = new Grid { HorizontalAlignment = HorizontalAlignment.Left, Width = 0, RenderTransformOrigin = new Point(0, .5) };
                bar.Children.Add(new Border { CornerRadius = new CornerRadius(1.5), Background = Wk.Lin(0, 0, 1, 0, (Wk.Shade(vivid, -.35), 1, 0), (Wk.Shade(vivid, .05), 1, 1)) });
                bar.Children.Add(new Border { CornerRadius = new CornerRadius(1.5), Background = Foil(.18, 30) });
                var grow = new ScaleTransform(); bar.RenderTransform = grow; bars.Add(grow);
                new Wk.BarFill(groove, bar, 4).Apply(a.OfTop);
                groove.Children.Add(bar);
                cell.Children.Add(groove);
                var an = WkCard.Ability(a, 11.5, WhiteB, Face, FontWeights.SemiBold, GoldB, 9.5);
                an.VerticalAlignment = VerticalAlignment.Top; an.Margin = new Thickness(2, 1, 6, 0);
                cell.Children.Add(an);
                row.Children.Add(cell);
                TextBlock Fig(string s, double size, Brush b, FontWeight fw) { var t = WkCard.Num(s, size, b, Face, fw); t.VerticalAlignment = VerticalAlignment.Top; t.Margin = new Thickness(0, size > 11 ? 1 : 2, 0, 0); return t; }
                WkCard.Put(row, Fig(a.ShareText, 10.5, ice, FontWeights.SemiBold), 1);
                WkCard.Put(row, Fig(a.Damage, 12, WhiteB, FontWeights.Bold), 2);
                if (c.CritCol) WkCard.Put(row, Fig(a.Crit, 10.5, a.Crit == "–" ? steel : GoldB, FontWeights.SemiBold), WkCard.CritAt);
                if (c.BackCol) WkCard.Put(row, Fig(a.Back, 10.5, ice, FontWeights.SemiBold), WkCard.BackAt(c));
                if (a.Tip.Length > 0) row.ToolTip = a.Tip;
                row.Background = Brushes.Transparent;
                stack.Children.Add(row);
            }
            if (c.MoreLine.Length > 0) { var more = Wk.Words(c.MoreLine, 10, steel, Face, FontWeights.SemiBold); more.Margin = new Thickness(2, 3, 0, 0); stack.Children.Add(more); }
        }
        if (c.FootLine.Length > 0)
        {
            var foot = WkCard.Foot(c, 10.5, Face, FontWeights.SemiBold, steel, Wk.S(Mint), Wk.S(0xFF6A80), Wk.S(Ice, .35));
            foot.Margin = new Thickness(1, 9, 0, 0);
            stack.Children.Add(foot);
        }
        card0.Content.Child = stack;
        // transparent room round the card: its shadow under it and its settle as it lands
        var card = new Border { Padding = new Thickness(8, 8, 8, 10), Background = Brushes.Transparent, Child = card0.Root };
        // it flips in from its edge and the bars slide in one by one
        var turn = new ScaleTransform();
        card0.Root.RenderTransformOrigin = new Point(.5, .5);
        card0.Root.RenderTransform = turn;
        WkCard.Enter(card, 280, p =>
        {
            double t = Seg(p, 0, .55);
            turn.ScaleX = Lerp(.6, 1, Back(t, 1.4)); turn.ScaleY = Lerp(.96, 1, Out(t));
            for (int i = 0; i < bars.Count; i++) { double k = Seg(p, .22 + .06 * i, .62 + .06 * i); bars[i].ScaleX = Out(k); }
        });
        return card;
    }

    // ---- the meter's rows: attack lines over foil bars

    /// <summary>"2 more": a small midnight tag between the party and you below it, a faint rule either side.</summary>
    static FrameworkElement HoloGap(int n)
    {
        var g = new Grid { VerticalAlignment = VerticalAlignment.Center };
        g.Children.Add(new Border { Height = 1, Margin = new Thickness(18, 0, 18, 0), VerticalAlignment = VerticalAlignment.Center, Background = Wk.Lin(0, 0, 1, 0, (0xFFFFFF, 0, 0), (0xFFFFFF, .16, .5), (0xFFFFFF, 0, 1)) });
        if (n > 0)
            g.Children.Add(new Border { CornerRadius = new CornerRadius(8), Background = Wk.S(0x141B38), BorderBrush = Wk.S(0x4A5A90), BorderThickness = new Thickness(1), Padding = new Thickness(9, 0, 9, 1), HorizontalAlignment = HorizontalAlignment.Center,
                Child = Wk.Words(L.F("{0} more", n), 10, IceB, Face, FontWeights.SemiBold) });
        return g;
    }

    /// <summary>Text with a dark line under it (a copy a pixel down), for the rows' changing words.</summary>
    sealed class Inked
    {
        public readonly Grid Root = new() { VerticalAlignment = VerticalAlignment.Center };
        public readonly TextBlock Main, Under;
        public Inked(double size, FontWeight w, Brush fg, bool figure = false, bool trim = false, double dy = 1.2)
        {
            TextBlock Make(Brush b) => new()
            {
                FontSize = size, FontWeight = w, Foreground = b, FontFamily = Face, VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = trim ? TextTrimming.CharacterEllipsis : TextTrimming.None,
            };
            Under = Make(Wk.S(Deep, .9)); Under.RenderTransform = new TranslateTransform(0, dy); Under.IsHitTestVisible = false;
            Main = Make(fg);
            if (figure) { Typography.SetNumeralAlignment(Main, FontNumeralAlignment.Tabular); Typography.SetNumeralAlignment(Under, FontNumeralAlignment.Tabular); }
            Root.Children.Add(Under); Root.Children.Add(Main);
        }
        public string Text { set { Main.Text = value; Under.Text = value; } }
        public Brush Fg { set => Main.Foreground = value; }
        public HorizontalAlignment Align
        {
            set
            {
                Root.HorizontalAlignment = Main.HorizontalAlignment = Under.HorizontalAlignment = value;
                Main.TextAlignment = Under.TextAlignment = value == HorizontalAlignment.Right ? TextAlignment.Right : value == HorizontalAlignment.Center ? TextAlignment.Center : TextAlignment.Left;
            }
        }
    }

    /// <summary>
    /// A member as an attack line on the card: an energy orb of the class's colour with the rank, the icon, the name, quiet
    /// Combat Power and deaths; the DPS big at the right as the attack's damage, the share small after it; under them a
    /// full-width bar of the class's colour washed in holographic foil, white at its head. A healer's heal and TOP DPS / TOP
    /// HEALS are foil stamps at the bar's right end; you a gold name and a gold-ruled bar. Expanded, the damage and crit sit
    /// under the name (the row grows by <see cref="Extra"/>).
    /// </summary>
    sealed class HoloRow : IThemeRow
    {
        public const double Line = 21, BarH = 16, H = Line + 3 + BarH, Pitch = H + 9, Extra = 11;
        readonly Grid _root = new() { Height = H, Background = Brushes.Transparent };
        readonly Grid _line = new() { Height = Line, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(1, 0, 3, 0) };
        readonly Grid _orb = new() { Width = 17, Height = 17, VerticalAlignment = VerticalAlignment.Center };
        readonly Ellipse _orbFill = new() { StrokeThickness = 1 };
        readonly Inked _rank = new(10, FontWeights.Bold, WhiteB, figure: true, dy: .8);
        readonly Border _icon = new() { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(5, 0, 5, 0) };
        readonly Inked _name = new(13, FontWeights.SemiBold, WhiteB, trim: true);
        readonly Grid _you;
        readonly Border _cpBox, _dead;
        readonly Run _cp = new() { FontSize = 9.5, FontWeight = FontWeights.SemiBold, Foreground = Wk.S(Ice, .85) };
        readonly TextBlock _deadT = new() { FontFamily = Face, FontSize = 8.5, FontWeight = FontWeights.Bold, Foreground = WhiteB, LineStackingStrategy = LineStackingStrategy.BlockLineHeight, LineHeight = 11.5 };
        readonly Inked _detail = new(9.5, FontWeights.Medium, Wk.S(Ice, .88));
        readonly Inked _fig = new(17.5, FontWeights.Bold, WhiteB, figure: true), _share = new(10, FontWeights.SemiBold, Wk.S(0xA8B4E0), figure: true);
        readonly ColumnDefinition _figCol = new(), _shareCol = new();
        readonly Border _groove = new() { CornerRadius = new CornerRadius(3), BorderThickness = new Thickness(1) };
        readonly Grid _track = new() { Margin = new Thickness(1.5) };
        readonly Grid _fill = new() { HorizontalAlignment = HorizontalAlignment.Left, Width = 0, IsHitTestVisible = false };
        readonly Border _body = new() { CornerRadius = new CornerRadius(2) };
        readonly Wk.BarFill _bar;
        readonly Border _top, _heal;
        readonly TextBlock _topT, _healT;
        string _cls = "\0";
        bool? _cpOnly;

        public FrameworkElement Root => _root;

        static Border Stamp(out TextBlock t)
        {
            t = new TextBlock { FontFamily = Face, FontSize = 8.5, FontWeight = FontWeights.Bold, Foreground = InkB, LineStackingStrategy = LineStackingStrategy.BlockLineHeight, LineHeight = 11 };
            Typography.SetNumeralAlignment(t, FontNumeralAlignment.Tabular);
            var g = new Grid { IsHitTestVisible = false };
            g.Children.Add(new Border { CornerRadius = new CornerRadius(2), Background = Wk.S(0xFFFFFF) });
            g.Children.Add(new Border { CornerRadius = new CornerRadius(2), Background = Foil(.5, 22) });
            g.Children.Add(new Border { CornerRadius = new CornerRadius(2), BorderBrush = Wk.S(Ink, .9), BorderThickness = new Thickness(1), Padding = new Thickness(4, 0, 4, 0), Child = t });
            return new Border { Child = g, VerticalAlignment = VerticalAlignment.Center };
        }

        public HoloRow()
        {
            foreach (var cd in new[] { new ColumnDefinition { Width = GridLength.Auto }, new ColumnDefinition { Width = GridLength.Auto }, new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }, _figCol, _shareCol })
                _line.ColumnDefinitions.Add(cd);
            _orb.Children.Add(new Ellipse { Fill = Wk.S(0xFFFFFF, .3), Margin = new Thickness(-1.5) });
            _orb.Children.Add(_orbFill);
            _orb.Children.Add(new Ellipse { Width = 6, Height = 3, Fill = Wk.S(0xFFFFFF, .75), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(3.5, 2.2, 0, 0), IsHitTestVisible = false });
            _rank.Align = HorizontalAlignment.Center;
            _orb.Children.Add(_rank.Root);
            _line.Children.Add(_orb);
            Grid.SetColumn(_icon, 1); _line.Children.Add(_icon);
            _you = new Grid { Margin = new Thickness(5, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
            _you.Children.Add(new Border { CornerRadius = new CornerRadius(3), Background = Wk.Lin(0, 0, 0, 1, (0xFFF0B0, 1, 0), (0xE8B53A, 1, 1)) });
            _you.Children.Add(new Border { Padding = new Thickness(5, 0, 5, .5), Child = new TextBlock { Text = L.T("YOU"), FontFamily = Face, FontSize = 8.5, FontWeight = FontWeights.Bold, Foreground = Wk.S(0x2A1A00) } });
            var cpT = new TextBlock { FontFamily = Face, VerticalAlignment = VerticalAlignment.Center };
            Typography.SetNumeralAlignment(cpT, FontNumeralAlignment.Tabular);
            cpT.Inlines.Add(new Run(L.T("CP") + " ") { FontSize = 7.5, FontWeight = FontWeights.Bold, Foreground = Wk.S(Steel, .9) });
            cpT.Inlines.Add(_cp);
            _cpBox = new Border { Child = cpT, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 0, 0) };
            _dead = new Border { CornerRadius = new CornerRadius(3), Background = Wk.S(0xE8344E), BorderBrush = Wk.S(0x5A0A14), BorderThickness = new Thickness(1), Padding = new Thickness(5, 0, 5, .5), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 0, 0), Child = _deadT };
            var nameLine = WkCard.NameLine(_name.Root, _you, _cpBox, _dead);
            // expanded: the damage and crit under the name
            _detail.Root.HorizontalAlignment = HorizontalAlignment.Left; _detail.Root.Margin = new Thickness(1, 2.5, 0, 0);
            var nameCol = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Children = { nameLine, _detail.Root } };
            Grid.SetColumn(nameCol, 2); _line.Children.Add(nameCol);
            _fig.Align = HorizontalAlignment.Right; Grid.SetColumn(_fig.Root, 3); _line.Children.Add(_fig.Root);
            _share.Align = HorizontalAlignment.Right; _share.Root.Margin = new Thickness(0, 3, 0, 0); Grid.SetColumn(_share.Root, 4); _line.Children.Add(_share.Root);
            _root.Children.Add(_line);
            // the bar: a groove the full width, the class's colour washed in foil, lit on top, white at its head
            var bar = new Grid { Height = BarH, VerticalAlignment = VerticalAlignment.Bottom };
            bar.Children.Add(_groove);
            _fill.Children.Add(_body);
            _fill.Children.Add(new Border { CornerRadius = new CornerRadius(2), Background = Foil(.16, 40) });
            _fill.Children.Add(new Border { CornerRadius = new CornerRadius(2), Background = Wk.Lin(0, 0, 0, 1, (0xFFFFFF, .4, 0), (0xFFFFFF, 0, .45), (0x000000, .3, 1)) });
            _fill.Children.Add(new Border { Width = 2, HorizontalAlignment = HorizontalAlignment.Right, Background = Wk.S(0xFFFFFF, .9) });
            _track.Children.Add(_fill);
            bar.Children.Add(_track);
            _bar = new Wk.BarFill(_track, _fill, 8);
            _heal = Stamp(out _healT); _heal.Margin = new Thickness(0, 0, 3, 0);
            _top = Stamp(out _topT);
            bar.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 3, 0), Children = { _heal, _top } });
            _root.Children.Add(bar);
        }

        public void Set(MeterMember m, MeterContext c, bool fresh)
        {
            double extra = c.Expanded ? Extra : 0;
            _root.Height = H + extra; _line.Height = Line + extra;
            uint v = Wk.Vivid(Wk.Hex(m.ClassColor));
            _orbFill.Fill = Wk.Rad(.4, .32, .75, (Wk.Shade(v, .5), 1, 0), (v, 1, .5), (Wk.Shade(v, -.5), 1, 1));
            _orbFill.Stroke = Wk.S(Wk.Shade(v, -.6));
            _rank.Text = m.Rank.ToString(CultureInfo.InvariantCulture);
            if (_cls != m.Class) { _cls = m.Class; _icon.Child = ThemeText.Icon(m.Class, 15, m.Name); }
            _name.Text = m.Name; _name.Fg = m.You ? GoldB : WhiteB;
            _you.Visibility = m.You ? Visibility.Visible : Visibility.Collapsed;
            _cp.Text = m.Cp; _cpBox.Visibility = m.Cp.Length > 0 && !m.CpOnly ? Visibility.Visible : Visibility.Collapsed;
            _dead.Visibility = m.Deaths > 0 ? Visibility.Visible : Visibility.Collapsed;
            _deadT.Text = "✖ " + m.Deaths.ToString(CultureInfo.InvariantCulture);
            if (c.Expanded && m.Damage.Length > 0) { _detail.Text = m.Damage + (m.Crit.Length > 0 ? " · " + m.Crit + " " + L.T("crit") : ""); _detail.Root.Visibility = Visibility.Visible; }
            else _detail.Root.Visibility = Visibility.Collapsed;
            if (_cpOnly != m.CpOnly)
            {
                _cpOnly = m.CpOnly;
                _figCol.Width = new GridLength(Wk.Room(Face, 17.5, FontWeights.Bold, m.CpOnly ? new[] { "888,888" } : Wk.DpsForms, 8));
                _shareCol.Width = new GridLength(Wk.Room(Face, 10, FontWeights.SemiBold, m.CpOnly ? new[] { L.T("CP") } : Wk.ShareForms, 6));
            }
            _fig.Text = m.Figure; _share.Text = m.ShareText;
            _groove.Background = Wk.Lin(0, 0, 0, 1, (0x03050C, .95, 0), (0x101632, .95, 1));
            _groove.BorderBrush = m.You ? Wk.Lin(0, 0, 0, 1, (0xFFF0B0, 1, 0), (0xE8B53A, 1, 1)) : Wk.S(0xFFFFFF, .14);
            _body.Background = Wk.Lin(0, 0, 1, 0, (Wk.Shade(v, -.35), 1, 0), (Wk.Shade(v, -.05), 1, .7), (Wk.Shade(v, .1), 1, 1));
            _healT.Text = m.Heal.Length > 0 ? "✚ " + m.Heal : ""; _heal.Visibility = m.Heal.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
            _top.Visibility = m.TopDps || m.TopHeals ? Visibility.Visible : Visibility.Collapsed;
            _topT.Text = m.TopDps ? L.T("TOP DPS") : L.T("TOP HEALS");
            _fill.Visibility = m.CpOnly ? Visibility.Hidden : Visibility.Visible;
            _bar.Set(Wk.Bar(m, c), c, "bar", fresh);
        }
    }
}
