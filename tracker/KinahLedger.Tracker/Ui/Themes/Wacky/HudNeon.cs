using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using Path = System.Windows.Shapes.Path;
using KinahLedger.Parser;
using KinahLedger.Session;
using static KinahLedger.Ui.HudKit;

namespace KinahLedger.Ui;

/// <summary>
/// The Wacky theme's Neon look: an arcade cabinet in synthwave. A dark violet panel with fine scanlines and a glowing
/// rim from magenta to cyan (strokes at falling alpha, not blurs), skewed heavy type: the hour in neon yellow with its
/// glow and the unit in pink, the session in white under a lilac label, the ring in cyan, the next timer a green neon
/// tube. A fight is a cabinet with a cyan rim, its timer a tube mounted on its top like a cabinet's marquee. Moments: a
/// pickup streaks up in cyan with a light trail as the rim flares; a rare drop cycles the rim's colours and runs a RARE!
/// marquee across; HIGH SCORE!; the tube flickers on like a sign; READY? FIGHT!; K.O. STAGE CLEAR or GAME OVER.
/// </summary>
sealed partial class HudNeon : WackyLook, IThemeCard
{
    /// <summary>The note's pop-out moves this (HudStackLook.PopNote).</summary>
    protected override UIElement? NoteHost => _note;

    const uint GroundC = 0x140A24, Mag = 0xFF2BD6, Cy = 0x00E5FF, Yel = 0xFFE66B, LilacC = 0xB79CFF, TubeC = 0x39FF88, RedC = 0xFF3355, Ice = 0xE9E4FF;
    static readonly Brush YelB = Wk.S(Yel), MagB = Wk.S(Mag), CyB = Wk.S(Cy), LilacB = Wk.S(LilacC), IceB = Wk.S(Ice), WhiteB = Wk.S(0xFFFFFF), GroundB = Wk.S(GroundC);
    const double Skew = -11;

    /// <summary>A cabinet: the glow (three strokes outward at falling alpha), the violet ground with its scanlines, the rim, and a flare for the moments.</summary>
    sealed class Cabinet
    {
        public readonly Grid Root = new() { HorizontalAlignment = HorizontalAlignment.Left };
        public readonly Border Ground = new() { CornerRadius = new CornerRadius(5) };
        public readonly Border Flare = new() { CornerRadius = new CornerRadius(5), BorderThickness = new Thickness(2.5), Opacity = 0, IsHitTestVisible = false };
        public readonly Border Content = new();
        public readonly LinearGradientBrush Rim;
        public readonly Color[] RimColors;
        public Cabinet(Color a, Color b, Thickness pad)
        {
            Rim = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 1) };
            Rim.GradientStops.Add(new GradientStop(a, 0)); Rim.GradientStops.Add(new GradientStop(b, 1));
            RimColors = new[] { a, b };
            // a crisp dark keyline outside the glow, so the glow stays clean over pale skies
            Root.Children.Add(new Border { Margin = new Thickness(-4.4), CornerRadius = new CornerRadius(9), BorderThickness = new Thickness(1), BorderBrush = Wk.S(0x0A0418, .85), IsHitTestVisible = false });
            for (int i = 2; i >= 1; i--)
            {
                double o = i * 1.6;
                Root.Children.Add(new Border { Margin = new Thickness(-o), CornerRadius = new CornerRadius(5 + o), BorderThickness = new Thickness(1.6), BorderBrush = Rim, Opacity = i == 2 ? .28 : .5, IsHitTestVisible = false });
            }
            Root.Children.Add(Ground);
            Root.Children.Add(new Border { CornerRadius = new CornerRadius(5), Background = Scan, IsHitTestVisible = false });
            Root.Children.Add(new Border { CornerRadius = new CornerRadius(5), BorderThickness = new Thickness(1.6), BorderBrush = Rim, IsHitTestVisible = false });
            Root.Children.Add(new Border { CornerRadius = new CornerRadius(4), Margin = new Thickness(1.6), BorderThickness = new Thickness(1), BorderBrush = Wk.S(0xFFFFFF, .1), IsHitTestVisible = false });
            Flare.BorderBrush = Wk.Lin(0, 0, 1, 1, (0xFFFFFF, 1, 0), (Wk.Hex(b), 1, 1));
            Root.Children.Add(Flare);
            Content.Padding = pad;
            Root.Children.Add(Content);
        }
        public void SetGround(double op) => Ground.Background = Wk.Lin(0, 0, 0, 1, (0x221040, op, 0), (GroundC, op, .55), (0x0C0616, op, 1));
        /// <summary>The rim's colours turned round the hue wheel by <paramref name="turn"/> (0 to 1).</summary>
        public void Hue(double turn)
        {
            for (int i = 0; i < RimColors.Length; i++) Rim.GradientStops[i].Color = Turn(RimColors[i], turn);
        }
    }

    /// <summary>The scanlines: a faint line every third pixel.</summary>
    static readonly Brush Scan = MakeScan();
    static Brush MakeScan()
    {
        var d = new GeometryDrawing(Wk.S(0x000000, .28), null, new RectangleGeometry(new Rect(0, 0, 4, 1)));
        var b = new DrawingBrush(new DrawingGroup { Children = { new GeometryDrawing(Brushes.Transparent, null, new RectangleGeometry(new Rect(0, 0, 4, 3))), d } })
        { TileMode = TileMode.Tile, Viewport = new Rect(0, 0, 4, 3), ViewportUnits = BrushMappingMode.Absolute, Stretch = Stretch.None };
        b.Freeze();
        return b;
    }

    static Color Turn(Color c, double turn)
    {
        double r = c.R / 255.0, g = c.G / 255.0, b = c.B / 255.0;
        double max = Math.Max(r, Math.Max(g, b)), min = Math.Min(r, Math.Min(g, b)), d = max - min;
        double h = d == 0 ? 0 : max == r ? ((g - b) / d % 6) : max == g ? (b - r) / d + 2 : (r - g) / d + 4;
        h = (h * 60 + turn * 360) % 360; if (h < 0) h += 360;
        double s = max == 0 ? 0 : d / max, v = max;
        double cc = v * s, x = cc * (1 - Math.Abs(h / 60 % 2 - 1)), m = v - cc;
        (double R, double G, double B) = h < 60 ? (cc, x, 0.0) : h < 120 ? (x, cc, 0.0) : h < 180 ? (0.0, cc, x) : h < 240 ? (0.0, x, cc) : h < 300 ? (x, 0.0, cc) : (cc, 0.0, x);
        return Color.FromRgb((byte)Math.Round((R + m) * 255), (byte)Math.Round((G + m) * 255), (byte)Math.Round((B + m) * 255));
    }

    readonly Cabinet _cab = new(Rgb(Mag), Rgb(Cy), new Thickness(10, 7, 12, 7));
    readonly Cabinet _fight = new(Rgb(Cy), Rgb(0x7A5CFF), new Thickness(0, 0, 0, 0));
    readonly StackPanel _lineStack = new();
    readonly StackPanel _line = new() { Orientation = Orientation.Horizontal };
    // the ring at the left of the hour's line and the pickups' row under it, centred across both
    readonly StackPanel _head = new() { Orientation = Orientation.Horizontal };
    readonly Border _ringHost = new() { VerticalAlignment = VerticalAlignment.Center };
    readonly StackPanel _col = new() { VerticalAlignment = VerticalAlignment.Center };
    readonly StackPanel _stats = new() { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 5, 0, 0), Visibility = Visibility.Collapsed };
    readonly TextBlock _note = new() { FontFamily = Wk.Bahn, FontSize = 12, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 6, 0, 0), Visibility = Visibility.Collapsed, TextTrimming = TextTrimming.CharacterEllipsis, MaxWidth = 380 };
    readonly StackPanel _fightStack = new();
    // the pickups: a row of item tiles under the hour, inside the cabinet
    readonly Border _pickHost = new() { HorizontalAlignment = HorizontalAlignment.Left, Visibility = Visibility.Collapsed };
    static readonly PickStrip.Look Strip = new(24, 4, true, Wk.Lin(0, 0, 0, 1, (Mag, 1, 0), (Cy, 1, 1)), Wk.S(Yel));
    protected override PickStrip.Look StripLook => Strip;
    // the timer's tube: kept, so its flicker lives through the second's redraws
    readonly Grid _tube = new() { VerticalAlignment = VerticalAlignment.Center, Visibility = Visibility.Collapsed };
    readonly Border _tubeGlow = new() { Margin = new Thickness(-3), CornerRadius = new CornerRadius(14), BorderThickness = new Thickness(3) };
    readonly Border _tubeFace = new() { CornerRadius = new CornerRadius(11), BorderThickness = new Thickness(1.7), Padding = new Thickness(10, 2, 11, 3) };
    readonly TextBlock _tubeName = Wk.Words("", 11, IceB, Wk.Bahn, FontWeights.SemiBold), _tubeClock;
    FrameworkElement? _ring, _hour;

    public HudNeon() : base(new Thickness(12, 56, 12, 8), Wk.Bahn)
    {
        UseRows(NeonRow.Pitch, _ => new NeonRow(), 10, NeonGap);
        _tubeClock = Figure("", 13, Wk.S(TubeC), Wk.Bahn, FontWeights.Bold);
        _tubeClock.Margin = new Thickness(6, 0, 0, 0);
        _tubeFace.Child = new StackPanel { Orientation = Orientation.Horizontal, Children = { _tubeName, _tubeClock } };
        _tube.Children.Add(_tubeGlow); _tube.Children.Add(_tubeFace);
        Slant(_tube, Skew * .7);
        _col.Children.Add(_line); _col.Children.Add(_pickHost);
        _head.Children.Add(_ringHost); _head.Children.Add(_col);
        _lineStack.Children.Add(_head); _lineStack.Children.Add(_stats); _lineStack.Children.Add(_note);
        _cab.Content.Child = _lineStack;
        _fight.Content.Child = _fightStack;
        _fight.Root.Visibility = Visibility.Collapsed;
        _body.Children.Add(_cab.Root);
        _body.Children.Add(_fight.Root);
        BuildRun();   // the dungeon run's head and splits in the cabinet (Ui/Themes/Wacky/HudNeon.Run.cs)
        Ground(Op);
    }

    public override FrameworkElement? FightFrame => _fight.Root.Visibility == Visibility.Visible ? _fight.Root : null;

    protected override void Ground(double opacity) { _cab.SetGround(opacity); _fight.SetGround(Math.Max(.62, opacity)); }

    /// <summary>The italic lean of the cabinet's lettering.</summary>
    static T Slant<T>(T el, double skew = Skew) where T : FrameworkElement
    {
        el.RenderTransformOrigin = new Point(.5, .5);
        el.RenderTransform = new SkewTransform(skew, 0);
        TextOptions.SetTextFormattingMode(el, TextFormattingMode.Ideal);
        return el;
    }

    /// <summary>A neon figure: the glow (one small blur), a magenta drop a step down and right, the figure.</summary>
    static Grid Lit(string s, double size, Brush fg, Color glow)
    {
        var g = new Grid { VerticalAlignment = VerticalAlignment.Center };
        var halo = Figure(s, size, new SolidColorBrush(glow), Wk.Bahn, FontWeights.Bold);
        halo.Effect = new BlurEffect { Radius = 9, RenderingBias = RenderingBias.Performance };
        halo.Opacity = .85;
        var drop = Figure(s, size, Wk.S(Mag, .7), Wk.Bahn, FontWeights.Bold);
        drop.RenderTransform = new TranslateTransform(2, 2);
        g.Children.Add(halo); g.Children.Add(drop);
        g.Children.Add(Figure(s, size, fg, Wk.Bahn, FontWeights.Bold));
        return Slant(g);
    }

    // ---- the line

    protected override void DrawLine(Figures f)
    {
        _line.Children.Clear(); _stats.Children.Clear();
        _ringHost.Child = null;
        _ring = _hour = null;
        // in a dungeon run: the run's clock, splits and verdict in place of the hour (Ui/Themes/Wacky/HudNeon.Run.cs)
        if (RunOf() is { } rs) { DrawRun(rs, f); return; }
        EndRun();
        bool waiting = !f.Counted || f.Rate.Length == 0;
        bool mark = f.Abyss && f.Counted && f.Icon.Length > 0;
        if (mark)
        {
            var m = new Grid { Width = 40, Height = 40, Margin = new Thickness(0, 0, 10, 0), VerticalAlignment = VerticalAlignment.Center };
            m.Children.Add(new Ellipse { Stroke = Wk.S(Cy, .25), StrokeThickness = 7 });
            m.Children.Add(new Ellipse { Stroke = CyB, StrokeThickness = 2, Margin = new Thickness(2.5) });
            var a = AbyssMark(f.Icon, 30); ((FrameworkElement)a).HorizontalAlignment = HorizontalAlignment.Center;
            m.Children.Add(a);
            _ringHost.Child = m; _ring = m;
        }
        else if (!waiting && f.Share >= 0 && !f.Warm) { _ring = Ring(f.Share, f.Beat); _ringHost.Child = _ring; }
        if (waiting)
        {
            var w = Slant(Wk.Words(Waiting(f.Abyss), 15, LilacB, Wk.Bahn, FontWeights.SemiBold));
            w.Margin = new Thickness(2, 4, 4, 4);
            _line.Children.Add(w); _hour = w;
        }
        else
        {
            var hour = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            if (f.Warm)
            {
                var w = Slant(Figure(f.Rate, 22, Wk.S(Yel, .72), Wk.Bahn, FontWeights.Bold));
                hour.Children.Add(w);
            }
            else
            {
                hour.Children.Add(Lit(f.Rate, 30, f.Abyss ? WhiteB : YelB, f.Abyss ? Rgb(Cy) : Rgb(0xFFB800)));
                if (f.Unit.Length > 0)
                {
                    var u = Slant(Wk.Words(f.Unit, 15, MagB, Wk.Bahn, FontWeights.Bold));
                    u.VerticalAlignment = VerticalAlignment.Bottom; u.Margin = new Thickness(7, 0, 0, 7);   // 7: the figure's slant leans into it
                    hour.Children.Add(u);
                }
            }
            _line.Children.Add(hour); _hour = hour;
            if (f.Total.Length > 0)
            {
                _line.Children.Add(new Border { Width = 2, Height = 32, Margin = new Thickness(13, 0, 13, 0), Background = Wk.Lin(0, 0, 0, 1, (Mag, 1, 0), (Cy, 1, 1)), RenderTransform = new SkewTransform(Skew, 0), VerticalAlignment = VerticalAlignment.Center });
                var sess = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
                sess.Children.Add(Slant(Wk.Words(f.TotalWord.ToUpperInvariant(), 9.5, LilacB, Wk.Bahn, FontWeights.Bold)));
                sess.Children.Add(Slant(Figure(f.Total, 18, WhiteB, Wk.Bahn, FontWeights.Bold)));
                ((FrameworkElement)sess.Children[1]).Margin = new Thickness(0, -3, 0, 0);
                _line.Children.Add(sess);
            }
        }
        // the Abyss's kills, deaths and Kinah: a second line, figures lit in their colours
        foreach (var (fig, word, tone) in f.Stats)
        {
            if (_stats.Children.Count > 0) _stats.Children.Add(Slant(Wk.Words("  ·  ", 11, MagB, Wk.Bahn, FontWeights.Bold)));
            Brush c = ReferenceEquals(tone, Rec) ? Wk.S(0xFF4545) : ReferenceEquals(tone, Gold) ? YelB : WhiteB;
            _stats.Children.Add(Slant(Figure(fig, 13.5, c, Wk.Bahn, FontWeights.Bold)));
            var w = Slant(Wk.Words(word, 11.5, ReferenceEquals(tone, Rec) ? Wk.S(0xFF8A8A) : LilacB, Wk.Bahn, FontWeights.SemiBold)); w.Margin = new Thickness(4, 1, 0, 0);
            _stats.Children.Add(w);
        }
        if (_stats.Children.Count > 0) _stats.Margin = new Thickness(_ring != null ? 50 : 0, 4, 0, 0);
        _stats.Visibility = _stats.Children.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        // the best drop and the last three: a second row in the cabinet, under the hour
        _pickHost.Child = StripFor(f);
        _pickHost.Margin = new Thickness(2, 3, 0, 2);
        _note.Text = f.Note; _note.Foreground = f.NoteTone;
        _note.Visibility = f.Note.Length > 0 && !KinahHidden ? Visibility.Visible : Visibility.Collapsed;
        PlaceTube();
    }

    /// <summary>The ring of your best: a cyan tube round the share, its glow a wide faint stroke under it.</summary>
    FrameworkElement Ring(double share, bool beat)
    {
        const double D = 42;
        var g = new Grid { Width = D, Height = D, Margin = new Thickness(0, 0, 11, 0), VerticalAlignment = VerticalAlignment.Center };
        var tone = beat ? Rgb(Yel) : Rgb(Cy);
        g.Children.Add(new Ellipse { Stroke = Wk.S(0x3A2466), StrokeThickness = 4, Margin = new Thickness(2.5) });
        g.Children.Add(Wk.Arc(D, 10, beat ? 1 : share, new SolidColorBrush(Color.FromArgb(0x40, tone.R, tone.G, tone.B)), PenLineCap.Round));
        g.Children.Add(Wk.Arc(D, 4, beat ? 1 : share, new SolidColorBrush(tone), PenLineCap.Round, 3));
        g.Children.Add(Wk.Arc(D, 1.4, beat ? 1 : share, Wk.S(0xFFFFFF, .75), PenLineCap.Round, 4.3));
        var t = Slant(Figure(beat ? "★" : $"{Math.Round(Math.Clamp(share, 0, 1) * 100):0}", beat ? 15 : 13, beat ? YelB : WhiteB, Wk.Bahn, FontWeights.Bold));
        t.HorizontalAlignment = HorizontalAlignment.Center;
        g.Children.Add(t);
        return g;
    }

    // ---- the timer's tube

    protected override void DrawTag()
    {
        if (Due.FirstOrDefault() is not { } i)
        {
            _tube.Visibility = Visibility.Collapsed;
            if (_runOn && Figs is { } f0 && !KinahHidden) { DrawLine(f0); return; }
            PlaceTube(); return;
        }
        var (name, clock, tone) = TagWords(i, DueNow);
        uint c = tone == TimerBadge.Tone.On ? TubeC : tone == TimerBadge.Tone.Soon ? Yel : LilacC;
        _tubeName.Text = name; _tubeName.Visibility = name.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        _tubeClock.Text = clock; _tubeClock.Foreground = Wk.S(c);
        _tubeFace.BorderBrush = Wk.S(c); _tubeFace.Background = Wk.S(Wk.Shade(c, -.86), .92);
        _tubeGlow.BorderBrush = Wk.S(c, .28);
        _tube.Visibility = Visibility.Visible;
        // in a run the line's forms leave room for the tube, or mount it as the marquee: drawn again for its new width
        if (_runOn && Figs is { } f && !KinahHidden) { DrawLine(f); return; }
        PlaceTube();
    }

    void PlaceTube()
    {
        bool onCab = KinahHidden && _fight.Root.Visibility == Visibility.Visible;
        bool onTop = !onCab && _runOn && _tubeUp;
        // in a dungeon the tube is the fight cabinet's marquee, mounted on its top edge; in a run without room on its line, the line cabinet's
        if (onCab && _tube.Parent != _fight.Root) { Detach(_tube); _fight.Root.Children.Add(_tube); }
        else if (onTop && _tube.Parent != _cab.Root) { Detach(_tube); _cab.Root.Children.Add(_tube); }
        else if (!onCab && !onTop && _tube.Parent != _line) { Detach(_tube); _line.Children.Add(_tube); }
        _tube.HorizontalAlignment = onCab || onTop ? HorizontalAlignment.Right : HorizontalAlignment.Left;
        _tube.VerticalAlignment = onCab || onTop ? VerticalAlignment.Top : VerticalAlignment.Center;
        _tube.Margin = onCab ? new Thickness(0, -15, 72, 0) : onTop ? new Thickness(0, -14, 16, 0) : new Thickness(14, 0, 0, 0);
        bool lineShown = !KinahHidden || (_tube.Parent == _line && _tube.Visibility == Visibility.Visible);
        _cab.Root.Visibility = lineShown ? Visibility.Visible : Visibility.Collapsed;
        foreach (UIElement ch in _line.Children) if (!ReferenceEquals(ch, _tube)) ch.Visibility = KinahHidden ? Visibility.Collapsed : Visibility.Visible;
        _ringHost.Visibility = KinahHidden ? Visibility.Collapsed : Visibility.Visible;
        _fight.Root.Margin = new Thickness(0, lineShown ? 14 : 0, 0, 0);
        // the pickups stay with the Kinah line, a fight's cabinet up or not (Steven, 2026-11-19: on ALL buddies)
        _pickHost.Visibility = _pickHost.Child != null && !KinahHidden ? Visibility.Visible : Visibility.Collapsed;
    }

    static void Detach(FrameworkElement el)
    {
        if (el.Parent is Panel p) p.Children.Remove(el);
        else if (el.Parent is Decorator d) d.Child = null;
    }

    protected override void KinahShown(bool shown)
    {
        if (!shown) { _stats.Visibility = Visibility.Collapsed; _note.Visibility = Visibility.Collapsed; _pickHost.Visibility = Visibility.Collapsed; }
        PlaceTube();
    }

    // ---- the fight

    protected override void DrawFight(Encounter? e, DateTime now)
    {
        _fightStack.Children.Clear();
        if (e == null) { _fight.Root.Visibility = Visibility.Collapsed; PlaceTube(); return; }
        _fight.Root.Width = FightWidth;
        bool tubeOn = KinahHidden && _tube.Visibility == Visibility.Visible;
        var head = new Grid { Margin = new Thickness(12, tubeOn ? 12 : 6, 12, 3) };
        for (int i = 0; i < 3; i++) head.ColumnDefinitions.Add(new ColumnDefinition { Width = i == 0 ? new GridLength(1, GridUnitType.Star) : GridLength.Auto });
        var tg = new Grid();
        var t0 = Wk.Words(e.Title, 17, Wk.S(Mag, .8), Wk.Bahn, FontWeights.Bold); t0.RenderTransform = new TranslateTransform(1.5, 1.5); t0.TextTrimming = TextTrimming.CharacterEllipsis;
        var t1 = Wk.Words(e.Title, 17, WhiteB, Wk.Bahn, FontWeights.Bold); t1.TextTrimming = TextTrimming.CharacterEllipsis;
        tg.Children.Add(t0); tg.Children.Add(t1);
        head.Children.Add(Slant(tg));
        if (OutcomeText(e) is { Length: > 0 } outcome)
        {
            var o = Slant(Wk.Words(outcome.ToUpperInvariant(), 13, Won(e) ? Wk.S(TubeC) : e.Outcome == "wipe" ? Wk.S(RedC) : LilacB, Wk.Bahn, FontWeights.Bold));
            o.Margin = new Thickness(8, 1, 10, 0);
            Grid.SetColumn(o, 1); head.Children.Add(o);
        }
        FrameworkElement clk = e.Waiting ? Slant(Wk.Words(L.T("Ready"), 14, Wk.S(TubeC), Wk.Bahn, FontWeights.Bold)) : Slant(Figure(Clock(e, now), 18, e.Live ? YelB : WhiteB, Wk.Bahn, FontWeights.Bold));
        Grid.SetColumn(clk, 2); head.Children.Add(clk);
        _fightStack.Children.Add(head);
        if (!e.Waiting && HpOf(e) is double hp) _fightStack.Children.Add(HpRow(e, hp, now));
        else _fightStack.Children.Add(new Border { Height = 4 });
        if (e.Waiting)
        {
            var w = Slant(Wk.Words(e.ReadyText, 13, LilacB, Wk.Bahn, FontWeights.SemiBold)); w.Margin = new Thickness(13, 0, 12, 10);
            _fightStack.Children.Add(w);
            if (PartyBeforeHit(e)) { var party = new StackPanel { Margin = new Thickness(5, 0, 5, 7) }; PutRows(party, e, now); _fightStack.Children.Add(party); }
        }
        else
        {
            var rows = new StackPanel { Margin = new Thickness(5, 0, 5, 5) };
            PutRows(rows, e, now);
            if (e.HasDamage) { var foot = ParserFoot.Line(e, now, 12); foot.Margin = new Thickness(7, 3, 7, 0); rows.Children.Add(foot); }
            _fightStack.Children.Add(rows);
        }
        _fight.Root.Visibility = Visibility.Visible;
        PlaceTube();
    }

    /// <summary>The boss's HP: a lit tube, magenta to red, notched at the thirds, and "47% · 1.82M left".</summary>
    FrameworkElement HpRow(Encounter e, double hp, DateTime now)
    {
        var row = new Grid { Margin = new Thickness(12, 1, 12, 6) };
        row.ColumnDefinitions.Add(new ColumnDefinition()); row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var tube = new Grid { Height = 12, VerticalAlignment = VerticalAlignment.Center };
        tube.Children.Add(new Border { Background = Wk.S(0x2A1745), BorderBrush = Wk.S(0x5A3590), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(6) });
        bool low = hp < .34;
        var fill = new Border
        {
            Background = low ? Wk.Lin(0, 0, 1, 0, (0xFF1E3C, 1, 0), (RedC, 1, 1)) : Wk.Lin(0, 0, 1, 0, (Mag, 1, 0), (RedC, 1, 1)),
            CornerRadius = new CornerRadius(6), HorizontalAlignment = HorizontalAlignment.Left, Width = 0,
            Child = new Border { Height = 2, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(4, 2, 4, 0), CornerRadius = new CornerRadius(1), Background = Wk.S(0xFFFFFF, .55) },
        };
        tube.SizeChanged += (_, _) => fill.Width = hp <= 0 ? 0 : Math.Max(12, tube.ActualWidth * hp);
        // the tube's glow: the fill's colour, wide and faint, under it
        var glow = new Border { Margin = new Thickness(-3, -3, 0, -3), CornerRadius = new CornerRadius(9), HorizontalAlignment = HorizontalAlignment.Left, Background = Wk.S(Mag, .2), Width = 0 };
        tube.SizeChanged += (_, _) => glow.Width = hp <= 0 ? 0 : Math.Max(12, tube.ActualWidth * hp) + 6;
        tube.Children.Insert(0, glow);
        tube.Children.Add(fill);
        HpGhost(tube, e, hp, now, 6);
        var ticks = HpTicks(); foreach (var t in ticks.Children.OfType<Border>()) { t.Width = 3.5; t.Background = GroundB; t.BorderBrush = Wk.S(0xFFFFFF, .55); t.BorderThickness = new Thickness(.8, 0, .8, 0); }
        tube.Children.Add(ticks);
        row.Children.Add(tube);
        var words = new TextBlock { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 0, 0), FontFamily = Wk.Bahn };
        Typography.SetNumeralAlignment(words, FontNumeralAlignment.Tabular);
        words.Inlines.Add(new Run($"{Math.Round(hp * 100):0}%") { FontSize = 15, FontWeight = FontWeights.Bold, Foreground = low ? Wk.S(RedC) : MagB });
        words.Inlines.Add(new Run($" · {HpLeftText(e)}") { FontSize = 12, FontWeight = FontWeights.SemiBold, Foreground = LilacB });
        Grid.SetColumn(Slant(words), 1); row.Children.Add(words);
        return row;
    }

    // ---- the moments

    /// <summary>A band (a fight call, a marquee): dark, lit at its top and bottom edges, its words clipped to it.</summary>
    static Grid Band(double w, double h, UIElement content)
    {
        var g = new Grid { Width = w, Height = h, ClipToBounds = true };
        // what scrolls along it fades in and out at its ends (never sliced by the edge)
        // (the mask in the band's own pixels: one sized to the lane's content would slide along with it)
        var fade = new LinearGradientBrush { MappingMode = BrushMappingMode.Absolute, StartPoint = new Point(0, 0), EndPoint = new Point(Math.Max(1, w - 12), 0) };
        foreach (var (a, at) in new[] { (0.0, 0.0), (1.0, Math.Min(.4, 40 / w)), (1.0, 1 - Math.Min(.4, 40 / w)), (0.0, 1.0) }) fade.GradientStops.Add(new GradientStop(Color.FromArgb((byte)(255 * a), 255, 255, 255), at));
        fade.Freeze();
        var lane = new Grid { Margin = new Thickness(6, 0, 6, 0), OpacityMask = fade };
        g.Children.Add(new Border { Background = Wk.S(GroundC, .95) });
        g.Children.Add(new Border { Background = Scan });
        g.Children.Add(new Border { Height = 2, VerticalAlignment = VerticalAlignment.Top, Background = Wk.Lin(0, 0, 1, 0, (Mag, 1, 0), (Cy, 1, 1)) });
        g.Children.Add(new Border { Height = 2, VerticalAlignment = VerticalAlignment.Bottom, Background = Wk.Lin(0, 0, 1, 0, (Cy, 1, 0), (Mag, 1, 1)) });
        if (content is FrameworkElement fe) { fe.HorizontalAlignment = HorizontalAlignment.Center; fe.VerticalAlignment = VerticalAlignment.Center; }
        lane.Children.Add(content);
        g.Children.Add(lane);
        return g;
    }

    /// <summary>Neon lettering as a shape: a dark outline, a glow stroke in its colour, the fill.</summary>
    static Grid Sign(string s, double size, Brush fill, uint glow) =>
        Wk.Outlined(s, Wk.Bahn, FontWeights.Bold, size, fill, GroundB, 2.4, Skew, null, 0, Wk.S(glow, .3), 5.5);

    /// <summary>The rim's quick flare (a pickup, a rare drop).</summary>
    static void Flare(Cabinet c, double ms)
    {
        Motion.Play(c.Flare, ms, q => c.Flare.Opacity = Math.Sin(Seg(q, 0, .15) * Math.PI / 2) * (1 - Seg(q, .15, 1)), () => c.Flare.Opacity = 0);
    }

    /// <summary>
    /// Where a band runs: the marquee strip on top of a cabinet (above its rim, off the figures); a fight's calls with the
    /// Kinah line above it run across the gap between the two.
    /// </summary>
    Point StripAt(FrameworkElement cab, double h)
    {
        var top = At(cab, .5, 0);
        bool gap = ReferenceEquals(cab, _fight.Root) && _cab.Root.Visibility == Visibility.Visible;
        return new Point(top.X, gap ? top.Y - 7 : top.Y - h / 2 - 9);
    }

    /// <summary>A band's life: opens from a line, runs, folds back to a line (held frames read the fold at 75%).</summary>
    static void BandLife(Sprite s, double q, double openEnd, double foldAt, double foldEnd) =>
        s.Set(1, Math.Max(.02, Out(Seg(q, 0, openEnd))), 0, 0, 0, PowerDown(Seg(q, foldAt, foldEnd)));

    /// <summary>A tube switching off: a stutter at full colour (bright, dim, bright, dim), then out.</summary>
    static double PowerDown(double t) => t <= 0 ? 1 : t < .3 ? 1 : t < .42 ? .22 : t < .7 ? .9 : t < .8 ? .12 : t < .9 ? .55 : 0;

    protected override void Pickup(Pick p, bool held)
    {
        if (KinahHidden || _hour == null) return;
        string words = PickWords(p) + "!";
        var main = Sign(words, 21, CyB, Cy);
        // the light trail: a streak of cyan under the figure, long while it flies, gone once it stops
        var streak = new Border { Width = Math.Max(10, main.Width * .3), Height = 1, VerticalAlignment = VerticalAlignment.Top, HorizontalAlignment = HorizontalAlignment.Center, CornerRadius = new CornerRadius(3),
            Background = Wk.Lin(0, 0, 0, 1, (0xE8FDFF, .6, 0), (Cy, .3, .45), (Cy, 0, 1)), RenderTransform = new SkewTransform(Skew, 0) };
        var g = new Grid { Height = main.Height + 60, Children = { streak, main } };
        main.VerticalAlignment = VerticalAlignment.Top;
        streak.Margin = new Thickness(0, main.Height - 8, 0, 0);
        var at = At(_hour, .78, 0);
        var c = new Point(at.X, at.Y + 6 + 30);
        // it flies up as far as the window lets it (the sign's top stays inside, its stretch at the end too)
        double top = c.Y - (main.Height + 60) / 2;
        double fly = Math.Clamp((top - 4 - main.Height * .1) / 62, .35, 1);
        var sign = new Rect(0, 0, main.Width, main.Height);
        var s = Spawn(g, c, "pickup");
        Flare(_cab, 700);
        Play(s, "pickup", 1400, q =>
        {
            double k = Seg(q, 0, .38), rise = 1 - (1 - k) * (1 - k);
            streak.Height = Math.Max(1, 26 * (1 - k));
            streak.Opacity = 1 - Seg(q, .22, .4);   // a short streak while it flies, gone well before the figure settles
            double sc = 1 + .2 * (1 - k);
            s.Set(sc * (1 - .1 * Seg(q, .6, 1)), sc * (1 + .15 * Seg(q, .6, 1)), 0, 8 * rise, (-40 * rise - 22 * Seg(q, .6, 1)) * fly, Seg(q, 0, .05) * (1 - Seg(q, .62, .95)));
            Keep(s, sign);
        });
    }

    protected override void RareDrop(Pick p, bool held)
    {
        if (KinahHidden) return;
        var c = ColorOf(p.Color, p.Grade);
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(Sign("★ " + L.T("RARE!"), 17, YelB, Yel));
        var tile = Tile(p, 26, glow: true); tile.Margin = new Thickness(10, 0, 8, 0);
        row.Children.Add(tile);
        var name = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        if (p.Grade.Length > 0) name.Children.Add(Wk.Outlined(L.T(p.Grade).ToUpperInvariant(), Wk.Bahn, FontWeights.Bold, 9.5, new SolidColorBrush(c), GroundB, 1.6, Skew));
        name.Children.Add(Sign(p.Name, 14, WhiteB, Wk.Hex(c)));
        ((FrameworkElement)name.Children[^1]).Margin = new Thickness(0, -3, 0, 0);
        row.Children.Add(name);
        var zoom = new ScaleTransform(1, 1);
        row.RenderTransformOrigin = new Point(.5, .5);
        row.RenderTransform = zoom;
        double w = _cab.Root.ActualWidth > 0 ? _cab.Root.ActualWidth : 260, h = 38;
        var band = Band(w, h, row);
        var s = Spawn(band, StripAt(_cab.Root, h), "rare");
        Flare(_cab, 900);
        // the rim cycles once round the colours
        Motion.Play(_cab.Ground, 1500, q => _cab.Hue(Seg(q, 0, 1)), () => _cab.Hue(0));
        Play(s, "rare", 2800, q =>
        {
            // the sign strikes on where it hangs (a tube catching: flicker, a surge past full, settled), then buzzes off;
            // never slid through the band's edge, where its words were cut (2026-11-21: "×1" clipped at 15%)
            double on = Seg(q, .04, .26);
            row.Opacity = q < .6 ? (on <= 0 ? 0 : on < .3 ? (on < .12 ? .7 : .15) : on < .45 ? .85 : on < .55 ? .3 : 1) : PowerDown(Seg(q, .6, .9));
            zoom.ScaleX = zoom.ScaleY = q < .6 ? 1 + .06 * (1 - Out(Seg(q, .1, .36))) : 1;
            BandLife(s, q, .07, .88, 1);
        });
    }

    protected override void BestPassed(bool held)
    {
        if (KinahHidden) return;
        var anchor = _ring ?? _hour;
        if (anchor == null) return;
        var hot = Sign(L.T("HIGH SCORE!"), 22, Wk.Lin(0, 0, 0, 1, (Yel, 1, 0), (Mag, 1, 1)), Mag);
        var cold = Sign(L.T("HIGH SCORE!"), 22, Wk.Lin(0, 0, 0, 1, (0xFFFFFF, 1, 0), (Cy, 1, 1)), Cy);
        var g = new Grid { Children = { hot, cold } };
        var at = At(_cab.Root, 0, 0);
        var sign = Centred(g, hot);
        double big = Fit(1.7, sign.Size);
        var s = Spawn(g, Inside(new Point(at.X + 26 + g.Children.OfType<FrameworkElement>().Max(x => x.Width) / 2, at.Y - 15), sign.Size), "best");
        // the ring sends out a ring of light
        var c0 = At(_ring ?? anchor, .5, .5);
        var ps = Spawn(new Grid { Width = 44, Height = 44, Children = { new Ellipse { Stroke = CyB, StrokeThickness = 2.5 } } }, c0, "best-ring");
        Play(ps, "best-ring", 900, q => { double k = Out(q); ps.Set(1 + .22 * k, 1 + .22 * k, 0, 0, 0, PowerDown(Seg(q, .5, 1))); });
        Play(s, "best", 2100, q =>
        {
            double drop = Seg(q, 0, .2);
            bool blink = q > .2 && q < .55 && (int)(q * 22) % 2 == 0;
            cold.Visibility = blink ? Visibility.Visible : Visibility.Hidden;
            hot.Visibility = blink ? Visibility.Hidden : Visibility.Visible;
            double sc = Lerp(big, 1, drop * drop);
            s.Set(sc, sc, 0, 0, -26 * (1 - drop * drop), Seg(q, 0, .06) * PowerDown(Seg(q, .62, .95)));
            Keep(s, sign);
        });
    }

    protected override void EntryOpened(TimerBoard.Item i, bool held)
    {
        if (_tube.Visibility != Visibility.Visible) return;
        // a sign switching on: flickers, a dark beat, then lit for good (round the words, which stay as they are)
        double[] keys = { 0, .15, .06, 1, .1, .25, .16, 1, .24, .35, .3, 1, .42, .45, .5, 1 };
        Motion.Play(_tube, 1100, q =>
        {
            double o = 1;
            for (int k = 0; k + 1 < keys.Length; k += 2) if (q >= keys[k]) o = keys[k + 1];
            _tube.Opacity = o;
            _tubeGlow.BorderThickness = new Thickness(3 + 4 * (1 - Seg(q, .5, 1)) * (o > .9 ? 1 : 0));
            _tubeGlow.Opacity = q < .5 ? (o > .9 ? 1 : .25) : .6 + .4 * (1 - Seg(q, .5, 1));   // (a glow that strikes with each "on")
        }, () => { _tube.Opacity = 1; _tubeGlow.BorderThickness = new Thickness(3); _tubeGlow.Opacity = 1; });
    }

    protected override void FightStarted(Encounter e, bool held)
    {
        if (_fight.Root.Visibility != Visibility.Visible) return;
        var ready = Sign(L.T("READY?"), 22, CyB, Cy);
        var fight = Sign(L.T("FIGHT!"), 27, Wk.Lin(0, 0, 0, 1, (Yel, 1, 0), (0xFF3355, 1, 1)), Mag);
        var g = new Grid { Children = { ready, fight } };
        double w = _fight.Root.ActualWidth > 0 ? _fight.Root.ActualWidth : 300, h = 38;
        var band = Band(w, h, g);
        var s = Spawn(band, StripAt(_fight.Root, h), "fight");
        var rt = new TranslateTransform(); ready.RenderTransform = rt;
        var fs = new ScaleTransform(); fight.RenderTransform = fs; fight.RenderTransformOrigin = new Point(.5, .5);
        Play(s, "fight", 1800, q =>
        {
            rt.X = -w * .6 * (1 - InOut(Seg(q, .04, .24))) + w * .6 * In(Seg(q, .3, .36));
            ready.Opacity = q < .36 ? 1 : 0;
            fight.Opacity = q >= .36 ? 1 : 0;
            fs.ScaleX = fs.ScaleY = Lerp(2, 1, Back(Seg(q, .36, .5), 2.4));
            BandLife(s, q, .06, .62, .82);
        });
    }

    protected override void FightEnded(Encounter e, bool held)
    {
        if (_fight.Root.Visibility != Visibility.Visible) return;
        bool wipe = e.Outcome == "wipe";
        if (!wipe && !Won(e)) return;
        var words = new StackPanel { Orientation = Orientation.Horizontal };
        if (wipe) words.Children.Add(Sign(L.T("GAME OVER"), 25, Wk.Lin(0, 0, 0, 1, (0xFF6A88, 1, 0), (0xFF1E3C, 1, 1)), RedC));
        else
        {
            words.Children.Add(Sign(L.T("K.O."), 29, Wk.Lin(0, 0, 0, 1, (Yel, 1, 0), (Mag, 1, 1)), Mag));
            var sc = new Border { BorderBrush = CyB, BorderThickness = new Thickness(1.6), CornerRadius = new CornerRadius(4), Background = Wk.S(Cy, .12), Padding = new Thickness(6, 0, 7, 1), Margin = new Thickness(12, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center,
                Child = Sign(L.T("STAGE CLEAR"), 15, WhiteB, Cy) };
            words.Children.Add(sc);
        }
        double w = _fight.Root.ActualWidth > 0 ? _fight.Root.ActualWidth : 300, h = 42;
        var band = Band(w, h, words);
        var zoom = new ScaleTransform(); words.RenderTransform = zoom; words.RenderTransformOrigin = new Point(.5, .5);
        var s = Spawn(band, StripAt(_fight.Root, h), "end");
        if (wipe)
        {
            // a wipe is a power cut, not a slam (2026-11-21 review: it read like the K.O.): the cabinet's tubes stutter and
            // sag to half light, GAME OVER sputters on at its own size, the light crawls back up as it holds
            var cab = _fight.Root;
            Motion.Play(cab, 2300, q =>
            {
                double d = Seg(q, .06, .3), back = Seg(q, .5, .7);
                double sag = q < .06 ? 1 : q < .3 ? .5 + .5 * PowerDown(d * .9) : .5 + .5 * Out(back);
                cab.Opacity = Math.Max(.5, sag);
            }, () => cab.Opacity = 1);
        }
        Play(s, "end", 2300, q =>
        {
            if (wipe)
            {
                zoom.ScaleX = 1; zoom.ScaleY = Lerp(.2, 1, Out(Seg(q, .12, .2)));
                double on = Seg(q, .12, .34);
                words.Opacity = on <= 0 ? 0 : on < .2 ? .8 : on < .35 ? .1 : on < .5 ? .9 : on < .6 ? .25 : 1;
            }
            else
            {
                zoom.ScaleX = zoom.ScaleY = Lerp(2.2, 1, Back(Seg(q, .05, .22), 2.2));
                words.Opacity = Seg(q, .05, .1);
            }
            BandLife(s, q, .06, .62, .84);
        });
    }

    // ---- the member's skills card: an arcade stats screen

    /// <summary>
    /// A member's skills card as an arcade cabinet's stats screen: the violet cabinet with its scanlines and glowing rim. The
    /// name lit in its class's neon (yellow for you) with the Combat Power on a cyan tube after it and the class in lilac
    /// capitals; a score bar of DPS (lit yellow), SHARE and DAMAGE; the rates; then the abilities as a numbered high-score
    /// table, each with a neon tube under its name as long as its damage against the top one, share, damage (crit, back)
    /// right-aligned and tabular; "N MORE"; the foot in lilac with heals green and deaths red.
    /// </summary>
    public FrameworkElement DrawCard(MemberCard c)
    {
        double w = WkCard.Width(c, 282);
        var cab = new Cabinet(Rgb(Mag), Rgb(Cy), new Thickness(12, 9, 12, 11));
        cab.SetGround(.97);
        cab.Root.Width = w;
        var stack = new StackPanel();
        uint neon = Wk.Vivid(Wk.Hex(c.ClassColor), .85, 1);
        var lilacDim = Wk.S(0xC9B8FF, .85);
        // the head: the lit name, the Combat Power's tube, the class
        var head = new Grid();
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var nameFg = c.You ? YelB : new SolidColorBrush(c.NameColor);
        TextBlock N(Brush b) { var t = Wk.Words(c.Name, 17, b, Wk.Bahn, FontWeights.Bold); t.TextTrimming = TextTrimming.CharacterEllipsis; return t; }
        var halo = N(Wk.S(c.You ? 0xFFB800u : neon)); halo.Effect = new System.Windows.Media.Effects.BlurEffect { Radius = 8, RenderingBias = System.Windows.Media.Effects.RenderingBias.Performance }; halo.Opacity = .8;
        var drop = N(Wk.S(Mag, .7)); drop.RenderTransform = new TranslateTransform(1.5, 1.5);
        var name = new Grid { Children = { halo, drop, N(nameFg) } };
        var cp = new Border { BorderBrush = CyB, BorderThickness = new Thickness(1.3), CornerRadius = new CornerRadius(7), Background = Wk.S(Cy, .14), Padding = new Thickness(6, 0, 7, 1), Margin = new Thickness(9, 1, 0, 0), VerticalAlignment = VerticalAlignment.Center,
            Visibility = c.Cp.Length > 0 ? Visibility.Visible : Visibility.Collapsed };
        var cpText = new TextBlock { FontFamily = Wk.Bahn, VerticalAlignment = VerticalAlignment.Center };
        cpText.Inlines.Add(new Run(L.T("CP") + " ") { FontSize = 8.5, FontWeight = FontWeights.Bold, Foreground = Wk.S(Cy, .85) });
        cpText.Inlines.Add(new Run(c.Cp) { FontSize = 11.5, FontWeight = FontWeights.Bold, Foreground = WhiteB });
        Typography.SetNumeralAlignment(cpText, FontNumeralAlignment.Tabular);
        cp.Child = cpText;
        head.Children.Add(Slant(WkCard.NameLine(name, cp)));
        var cls = Slant(Wk.Words(c.ClassName.ToUpperInvariant(), 9.5, LilacB, Wk.Bahn, FontWeights.Bold)); cls.Margin = new Thickness(10, 2, 2, 0);
        WkCard.Put(head, cls, 1);
        stack.Children.Add(head);
        // the score bar: DPS, SHARE, DAMAGE, each under its label, lit
        var score = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(1, 8, 0, 0) };
        void Cell(string label, FrameworkElement fig)
        {
            if (score.Children.Count > 0)
                score.Children.Add(new Border { Width = 2, Height = 30, Margin = new Thickness(12, 2, 12, 0), Background = Wk.Lin(0, 0, 0, 1, (Mag, 1, 0), (Cy, 1, 1)), RenderTransform = new SkewTransform(Skew, 0), VerticalAlignment = VerticalAlignment.Center });
            var sp = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            sp.Children.Add(Slant(Wk.Words(label, 8.5, lilacDim, Wk.Bahn, FontWeights.Bold)));
            fig.Margin = new Thickness(0, -2, 0, 0); fig.HorizontalAlignment = HorizontalAlignment.Left;
            sp.Children.Add(fig);
            score.Children.Add(sp);
        }
        if (c.Dps.Length > 0) Cell(L.T("DPS"), Lit(c.Dps, 22, YelB, Rgb(0xFFB800)));
        Cell(L.T("SHARE"), Lit(c.Share, 17, WhiteB, Rgb(Cy)));
        Cell(L.T("DAMAGE"), Lit(c.Damage, 17, WhiteB, Rgb(Mag)));
        stack.Children.Add(score);
        if (c.RatesLine.Length > 0)
        {
            var rates = Slant(Wk.Words(c.RatesLine, 10.5, Wk.S(Yel, .92), Wk.Bahn, FontWeights.SemiBold));
            rates.TextWrapping = TextWrapping.Wrap; rates.Margin = new Thickness(1, 7, 0, 0);
            stack.Children.Add(rates);
        }
        // a lit rule under the score
        stack.Children.Add(new Grid
        {
            Height = 6, Margin = new Thickness(0, 7, 0, 3),
            Children =
            {
                new Border { Height = 4, CornerRadius = new CornerRadius(2), Background = Wk.Lin(0, 0, 1, 0, (Mag, .28, 0), (Cy, .28, 1)) },
                new Border { Height = 1.4, Background = Wk.Lin(0, 0, 1, 0, (Mag, 1, 0), (Cy, 1, 1)) },
            },
        });
        var tubes = new List<ScaleTransform>();
        if (c.Abilities.Count > 0)
        {
            var names = WkCard.Cols(c, 46, 54, 14);
            TextBlock Head(string s, bool right = true) { var t = Wk.Words(s.ToUpperInvariant(), 8.5, lilacDim, Wk.Bahn, FontWeights.Bold); t.HorizontalAlignment = right ? HorizontalAlignment.Right : HorizontalAlignment.Left; return Slant(t); }
            var skill = Head(L.T("skill"), false); skill.Margin = new Thickness(20, 0, 0, 0); names.Children.Add(skill);
            WkCard.Put(names, Head(L.T("share")), 1); WkCard.Put(names, Head(L.T("damage")), 2);
            if (c.CritCol) WkCard.Put(names, Head(L.T("crit")), WkCard.CritAt);
            if (c.BackCol) WkCard.Put(names, Head(L.T("back")), WkCard.BackAt(c));
            stack.Children.Add(names);
            int n = 0;
            foreach (var a in c.Abilities)
            {
                var row = WkCard.Cols(c, 46, 54, 23);
                var cell = new Grid();
                var no = Slant(Wk.Words((++n).ToString("00", System.Globalization.CultureInfo.InvariantCulture), 9.5, Wk.S(n == 1 ? Yel : LilacC, n == 1 ? .95 : .55), Wk.Bahn, FontWeights.Bold));
                no.Margin = new Thickness(0, 0, 0, 3);
                cell.Children.Add(no);
                // the tube: a white-hot core in the class's neon, its glow wide and faint, its length the ability's against the top one
                var host = new Grid { Height = 3, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(20, 0, 8, 2.5) };
                var tube = new Grid { HorizontalAlignment = HorizontalAlignment.Left, Width = 0, RenderTransformOrigin = new Point(0, .5) };
                tube.Children.Add(new Border { Margin = new Thickness(-2), CornerRadius = new CornerRadius(3.5), Background = Wk.S(neon, .32) });
                tube.Children.Add(new Border { CornerRadius = new CornerRadius(1.5), Background = Wk.S(neon) });
                tube.Children.Add(new Border { Height = 1, Margin = new Thickness(1.5, 0, 1.5, 0), VerticalAlignment = VerticalAlignment.Center, Background = Wk.S(0xFFFFFF, .85) });
                var grow = new ScaleTransform(); tube.RenderTransform = grow; tubes.Add(grow);
                new Wk.BarFill(host, tube, 6).Apply(a.OfTop);
                host.Children.Add(tube);
                cell.Children.Add(host);
                var an = Slant(WkCard.Ability(a, 11.5, n == 1 ? WhiteB : IceB, Wk.Bahn, FontWeights.SemiBold, MagB, 9.5)); an.Margin = new Thickness(20, 0, 6, 5);
                cell.Children.Add(an);
                row.Children.Add(cell);
                TextBlock Fig(string s, double size, Brush b, FontWeight fw) { var t = WkCard.Num(s, size, b, Wk.Bahn, fw); t.Margin = new Thickness(0, 0, 1, 4); return Slant(t); }
                WkCard.Put(row, Fig(a.ShareText, 10.5, Wk.S(0xB9A6F0), FontWeights.SemiBold), 1);
                WkCard.Put(row, Fig(a.Damage, 12, WhiteB, FontWeights.Bold), 2);
                if (c.CritCol) WkCard.Put(row, Fig(a.Crit, 10.5, a.Crit == "–" ? lilacDim : YelB, FontWeights.Bold), WkCard.CritAt);
                if (c.BackCol) WkCard.Put(row, Fig(a.Back, 10.5, Wk.S(0xB9A6F0), FontWeights.SemiBold), WkCard.BackAt(c));
                if (a.Tip.Length > 0) row.ToolTip = a.Tip;
                row.Background = Brushes.Transparent;
                stack.Children.Add(row);
            }
            if (c.MoreLine.Length > 0) { var more = Slant(Wk.Words(c.MoreLine.ToUpperInvariant(), 9.5, lilacDim, Wk.Bahn, FontWeights.Bold)); more.Margin = new Thickness(20, 1, 0, 0); stack.Children.Add(more); }
        }
        if (c.FootLine.Length > 0)
        {
            var foot = Slant(WkCard.Foot(c, 10.5, Wk.Bahn, FontWeights.SemiBold, LilacB, Wk.S(TubeC), Wk.S(0xFF4D6D), Wk.S(Mag, .7)));
            foot.Margin = new Thickness(1, 8, 0, 0);
            stack.Children.Add(foot);
        }
        cab.Content.Child = stack;
        // transparent room round the cabinet for its glow and keyline
        var card = new Border { Padding = new Thickness(7), Background = Brushes.Transparent, Child = cab.Root };
        // the screen powers on: a lit line opening to the full screen, a flash of the rim, then the tubes light in turn
        var crt = new ScaleTransform();
        cab.Root.RenderTransformOrigin = new Point(.5, .5);
        cab.Root.RenderTransform = crt;
        WkCard.Enter(card, 280, p =>
        {
            crt.ScaleY = .04 + .96 * Out(Seg(p, 0, .4));
            cab.Flare.Opacity = Seg(p, .3, .4) * (1 - Seg(p, .4, .85));
            for (int i = 0; i < tubes.Count; i++) tubes[i].ScaleX = Out(Seg(p, .28 + .06 * i, .66 + .06 * i));
        });
        return card;
    }

    // ---- the meter's rows: an arcade high-score table

    /// <summary>"· · · 2 MORE · · ·" in lilac between the party and you below it.</summary>
    static FrameworkElement NeonGap(int n)
    {
        var g = new Grid { VerticalAlignment = VerticalAlignment.Center };
        g.Children.Add(new Rectangle { Height = 1.5, Stroke = Wk.S(LilacC, .45), StrokeThickness = 1.5, StrokeDashArray = new DoubleCollection { 1, 3 }, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 10, 0) });
        if (n > 0)
            g.Children.Add(new Border { Background = GroundB, Padding = new Thickness(8, 0, 8, 0), HorizontalAlignment = HorizontalAlignment.Center,
                Child = Slant(Wk.Words(L.F("{0} more", n).ToUpperInvariant(), 10, LilacB, Wk.Bahn, FontWeights.Bold)) });
        return g;
    }

    /// <summary>The rank's colour on the table: gold, cyan, magenta, then lilac.</summary>
    static uint RankColor(int rank) => rank switch { 1 => Yel, 2 => Cy, 3 => Mag, _ => LilacC };

    /// <summary>"1ST", "2ND", "3RD", "4TH".</summary>
    static string Suffix(int rank) => (rank % 100) is 11 or 12 or 13 ? L.T("TH") : (rank % 10) switch { 1 => L.T("ST"), 2 => L.T("ND"), 3 => L.T("RD"), _ => L.T("TH") };

    /// <summary>
    /// A member as a line of an arcade high-score table: the rank "1ST" lit in its colour, the name glowing in the class's
    /// colour, and the damage share as a neon tube (a lit outline round a dark core, its glow a wider faint stroke) running
    /// under the words. You are a magenta band with a ▶ and a YOU tube; a healer's heal is a green figure over a thin green
    /// tube; deaths a red ✖; TOP DPS and TOP HEALS lit signs at a kill. DPS and % right-aligned and tabular.
    /// </summary>
    sealed class NeonRow : IThemeRow, IThemeBarSpan
    {
        // 2026-11-20 (Steven: "some are just bulky"): 21 on a 24 pitch, the words and the tube the row's height, where the row
        // was 27 on a 30 pitch with 6 of it empty under the words
        public const double H = 21, Pitch = H + 3;
        readonly Grid _root = new() { Height = H, Background = Brushes.Transparent };
        readonly Border _band = new() { CornerRadius = new CornerRadius(3) };
        readonly Border _edge = new() { Width = 3, HorizontalAlignment = HorizontalAlignment.Left, CornerRadius = new CornerRadius(1.5), Margin = new Thickness(0, 2, 0, 2) };
        // the bar: a lit neon panel the row's height from the rank on, the name and figures over it (Steven, 2026-11-17: "figure
        // out better looks that doesn't crowd everything and prioritizes the bar"): a tube of the class's colour round a tinted
        // glass, its glow a wider faint ring, open at its end (no line crosses a figure there)
        readonly Grid _tubeHost = new() { Margin = new Thickness(36, 1, 3, 1) };
        readonly Grid _tube = new() { HorizontalAlignment = HorizontalAlignment.Left, Width = 0 };
        readonly Border _tubeGlow = new() { Margin = new Thickness(-2, -2, 0, -2), CornerRadius = new CornerRadius(5, 0, 0, 5), BorderThickness = new Thickness(2.2, 2.2, 0, 2.2) };
        readonly Border _tubeLine = new() { CornerRadius = new CornerRadius(3.5, 0, 0, 3.5), BorderThickness = new Thickness(1.4, 1.4, 0, 1.4) };
        readonly Grid _healHost = new() { Height = 2, Margin = new Thickness(0, 1, 0, 0) };
        readonly Border _healTube = new() { HorizontalAlignment = HorizontalAlignment.Right, CornerRadius = new CornerRadius(1), Width = 0, Background = Wk.S(TubeC) };
        readonly Wk.BarFill _fill, _healFill;
        readonly TextBlock _rank = new() { FontFamily = Wk.Bahn, VerticalAlignment = VerticalAlignment.Center };
        readonly TextBlock[] _glows = Enumerable.Range(0, 4).Select(_ => Wk.Words("", 13, Brushes.Transparent, Wk.Bahn, FontWeights.Bold)).ToArray();
        readonly TextBlock _name = Wk.Words("", 13, WhiteB, Wk.Bahn, FontWeights.Bold);
        readonly TextBlock _you = Wk.Words("", 13, MagB, Wk.Bahn, FontWeights.Bold);
        readonly Border _youTag;
        // the Combat Power after the name, in dim lilac, its "CP" smaller
        readonly TextBlock _cp = new() { FontFamily = Wk.Bahn, VerticalAlignment = VerticalAlignment.Center };
        readonly Run _cpValue = new() { FontSize = 10.5, FontWeight = FontWeights.SemiBold, Foreground = Wk.S(0xC9B8FF, .85) };
        readonly TextBlock _heal = Wk.Words("", 11, Wk.S(TubeC), Wk.Bahn, FontWeights.Bold);
        readonly Border _top = new() { BorderThickness = new Thickness(1.3), CornerRadius = new CornerRadius(7), Padding = new Thickness(5, 0, 5, 0), Margin = new Thickness(6, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        readonly TextBlock _topText = Wk.Words("", 9, WhiteB, Wk.Bahn, FontWeights.Bold);
        readonly TextBlock _detail = Wk.Words("", 10, Wk.S(LilacC, .9), Wk.Bahn, FontWeights.SemiBold);
        readonly TextBlock _figure, _share;
        readonly Border _topSign;
        static readonly double FigureW = Wk.Room(Wk.Bahn, 15, FontWeights.Bold, Wk.DpsForms, 6), ShareW = Wk.Room(Wk.Bahn, 11, FontWeights.SemiBold, Wk.ShareForms, 5);
        Grid _row = null!;
        bool? _cpOnly;
        readonly Run _critWord = new() { FontSize = 8, FontWeight = FontWeights.Bold, Foreground = Wk.S(LilacC, .6) };

        public FrameworkElement Root => _root;

        /// <summary>The neon tube runs its host (from x 36 to 3 px short of the row's end, 1 px in top and foot): Wk.BarFill makes the tube width * share (10 px at the least); its glow ring is 2 px wider round.</summary>
        public (FrameworkElement On, Rect Track)? BarTrack => _tubeHost.ActualWidth <= 0 ? null : (_tubeHost, new Rect(0, 0, _tubeHost.ActualWidth, _tubeHost.ActualHeight));

        public NeonRow()
        {
            TextOptions.SetTextFormattingMode(_root, TextFormattingMode.Ideal);
            _root.Children.Add(_band);
            _root.Children.Add(_edge);
            _tube.Children.Add(_tubeGlow); _tube.Children.Add(_tubeLine);
            _tubeHost.Children.Add(_tube);
            _fill = new Wk.BarFill(_tubeHost, _tube, 10);
            _root.Children.Add(_tubeHost);
            _healHost.Children.Add(_healTube);
            _healFill = new Wk.BarFill(_healHost, _healTube, 8);
            var row = new Grid { Margin = new Thickness(6, 0, 6, 1) };
            // the DPS and the share in columns sized for their widest forms ("888.8K", "88.8%"), a gap before each (Steven,
            // 2026-11-17: "a lot of numbers in all the meters you can't see, it's cut off!")
            foreach (var w in new[] { new GridLength(34), new GridLength(1, GridUnitType.Star), GridLength.Auto, GridLength.Auto, GridLength.Auto, GridLength.Auto, new GridLength(FigureW), new GridLength(ShareW) })
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = w });
            _row = row;
            _rank.RenderTransform = new SkewTransform(Skew, 0); _rank.RenderTransformOrigin = new Point(.5, .5);
            row.Children.Add(_rank);
            // the name glows in the class's colour: four copies a pixel out, round it
            _name.TextTrimming = TextTrimming.CharacterEllipsis;
            var nameGrid = new Grid();
            (double X, double Y)[] at = { (-1, 0), (1, 0), (0, -1), (0, 1) };
            for (int i = 0; i < 4; i++) { _glows[i].TextTrimming = TextTrimming.CharacterEllipsis; _glows[i].RenderTransform = new TranslateTransform(at[i].X, at[i].Y); nameGrid.Children.Add(_glows[i]); }
            nameGrid.Children.Add(_name);
            _youTag = new Border { BorderBrush = MagB, BorderThickness = new Thickness(1.3), CornerRadius = new CornerRadius(7), Padding = new Thickness(5, 0, 5, 0), Margin = new Thickness(6, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center,
                Background = Wk.S(Mag, .18), Child = Wk.Words(L.T("YOU"), 9, Wk.S(0xFFB8F2), Wk.Bahn, FontWeights.Bold) };
            _cp.Margin = new Thickness(5, 2, 0, 0);
            _cp.Inlines.Add(new Run(L.T("CP") + " ") { FontSize = 8, FontWeight = FontWeights.Bold, Foreground = Wk.S(LilacC, .6) });
            _cp.Inlines.Add(_cpValue);
            Typography.SetNumeralAlignment(_cp, FontNumeralAlignment.Tabular);
            // the name is cut before its marks: YOU and the Combat Power keep their room
            var nameLine = Slant(new Grid { HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center });
            foreach (var cw in new[] { GridLength.Auto, new GridLength(1, GridUnitType.Star), GridLength.Auto, GridLength.Auto }) nameLine.ColumnDefinitions.Add(new ColumnDefinition { Width = cw });
            nameLine.Children.Add(_you);
            WkCard.Put(nameLine, nameGrid, 1); WkCard.Put(nameLine, _youTag, 2); WkCard.Put(nameLine, _cp, 3);
            Grid.SetColumn(nameLine, 1); row.Children.Add(nameLine);
            // a healer's heal: the figure in green over its own thin green tube, its share of the group's healing
            var healCell = Slant(new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 2, 0, 0), Children = { _heal, _healHost } }); healCell.Margin = new Thickness(4, 2, 0, 0);
            Grid.SetColumn(healCell, 2); row.Children.Add(healCell);
            _detail.Margin = new Thickness(5, 1, 0, 0); Grid.SetColumn(Slant(_detail), 5); row.Children.Add(_detail);
            _figure = Figure("", 15, WhiteB, Wk.Bahn, FontWeights.Bold); _figure.HorizontalAlignment = HorizontalAlignment.Right;
            Grid.SetColumn(Slant(_figure), 6); row.Children.Add(_figure);
            _share = Figure("", 11, Wk.S(0xB9A6F0), Wk.Bahn, FontWeights.SemiBold); _share.HorizontalAlignment = HorizontalAlignment.Right; _share.Margin = new Thickness(0, 1, 0, 0);
            Grid.SetColumn(Slant(_share), 7); row.Children.Add(_share);
            _root.Children.Add(row);
            // TOP DPS and TOP HEALS: a lit sign on the row's own line before the DPS (2026-11-20: hung over the row's top edge it
            // needed 6 empty pixels under every row; on a tight pitch it sat on the figures). When the line is crowded the Combat
            // Power goes first, so the name keeps its letters (Crowd)
            _top.Child = _topText; _top.Margin = new Thickness(0);
            _topSign = new Border { Child = _top, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 0, 0), IsHitTestVisible = false };
            Slant(_topSign);
            Grid.SetColumn(_topSign, 3); row.Children.Add(_topSign);
            _root.SizeChanged += (_, _) => Crowd();
        }

        bool _cpWanted;


        /// <summary>A crowded line drops the Combat Power, then the share, before the name is cut (WkCard.Crowd).</summary>
        void Crowd() => WkCard.Crowd(_row, _root.ActualWidth, _cp, _cpWanted, on => _share.Visibility = on ? Visibility.Visible : Visibility.Hidden, _detail);

        public void Set(MeterMember m, MeterContext c, bool fresh)
        {
            uint rc = RankColor(m.Rank);
            _rank.Inlines.Clear();
            _rank.Inlines.Add(new Run(m.RankText()) { FontSize = 15, FontWeight = FontWeights.Bold, Foreground = Wk.S(rc) });
            // deaths in the suffix's place, a red ✖ and the count ("4✖2"): on the row's own line, off the name's
            if (m.Deaths > 0) _rank.Inlines.Add(new Run("✖" + m.Deaths.ToString(System.Globalization.CultureInfo.InvariantCulture)) { FontSize = 9, FontWeight = FontWeights.Bold, Foreground = Wk.S(0xFF4545) });
            else if (!m.Died) _rank.Inlines.Add(new Run(Suffix(m.Rank)) { FontSize = 9, FontWeight = FontWeights.Bold, Foreground = Wk.S(rc, .85) });
            _band.Background = m.You ? Wk.Lin(0, 0, 1, 0, (Mag, .26, 0), (Mag, .06, 1)) : m.Rank % 2 == 0 ? Wk.S(0xFFFFFF, .035) : null;
            _edge.Background = m.You ? MagB : null;
            _you.Text = m.You ? "▶ " : ""; _you.Visibility = m.You ? Visibility.Visible : Visibility.Collapsed;
            var cc = m.ClassColor;
            uint neon = Wk.Vivid(Wk.Hex(cc), .85, 1);
            _name.Text = m.Name;
            _name.Foreground = new SolidColorBrush(m.NameColor);
            foreach (var g in _glows) { g.Text = m.Name; g.Foreground = Wk.S(neon, .38); }
            _youTag.Visibility = m.You ? Visibility.Visible : Visibility.Collapsed;
            // Combat Power beside the name once there is damage (before it, the figure is the Combat Power: said once)
            _cpValue.Text = m.Cp; _cpWanted = m.Cp.Length > 0 && !m.CpOnly;
            if (_cp.Inlines.FirstInline is Run cpw && !ReferenceEquals(cpw, _cpValue)) cpw.Text = m.Pvp ? "" : L.T("CP") + " ";   // the PvP panel's line is who killed
            _heal.Text = m.Heal.Length > 0 ? "+" + m.Heal : ""; _heal.Visibility = m.Heal.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
            _healHost.Visibility = m.Heal.Length > 0 && m.HealShare > 0 ? Visibility.Visible : Visibility.Collapsed;
            _topSign.Visibility = m.TopDps || m.TopHeals ? Visibility.Visible : Visibility.Collapsed;
            uint tc = m.TopDps ? Yel : TubeC;
            _topText.Text = m.TopDps ? L.T("TOP DPS") : L.T("TOP HEALS"); _topText.Foreground = Wk.S(tc);
            _top.BorderBrush = Wk.S(tc); _top.Background = Wk.S(tc, .14);
            Wk.Detail(_detail, m, c, _critWord);
            if (_cpOnly != m.CpOnly) { _cpOnly = m.CpOnly; Wk.FigureCols(_row.ColumnDefinitions[6], _row.ColumnDefinitions[7], m.CpOnly, Wk.Bahn, 15, FontWeights.Bold, Wk.Bahn, 11, FontWeights.SemiBold); }
            _figure.Text = m.Figure; _share.Text = m.ShareText;
            _figure.Foreground = m.Rank == 1 && !m.CpOnly ? YelB : WhiteB;
            // the bar: the class's neon round a glass tinted in it (deeper at its start), its glow a wider faint ring; against the
            // top member's damage, so the top row's runs the table's width
            _tubeLine.BorderBrush = Wk.S(neon, .95);
            _tubeLine.Background = Wk.Lin(0, 0, 1, 0, (neon, .36, 0), (neon, .27, 1));   // (its end lit too: a glass fading to .16 read as a bar ending at 70%)
            _tubeGlow.BorderBrush = Wk.S(neon, .3);
            _tubeHost.Visibility = m.CpOnly ? Visibility.Hidden : Visibility.Visible;
            _fill.Set(Wk.Bar(m, c), c, "bar", fresh);
            _healFill.Set(m.HealShare, c, "heal", fresh);
            Crowd();
        }
    }
}
