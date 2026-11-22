using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using KinahLedger.Parser;
using KinahLedger.Session;
using static KinahLedger.Ui.HudKit;

namespace KinahLedger.Ui;

/// <summary>
/// Fighter (the Tactical theme): a jet's head-up display. No panel: phosphor-green lines and mono type straight over the
/// game, every glyph and line with a tight dark outline so it reads over snow and sky. Corner brackets frame the Kinah line;
/// the ring of your best is a reticle with the share inside; the hour big, "SES 894K · BEST −130K" under it. The next timer
/// is a solid block (green, amber when soon). A fight is a target box: "[ Kashapa ]" and its T+ clock in amber, the HP as
/// three segments (one a third), the rows on a dark ground, a dashed rule and the foot.
/// The moments: a pickup locks the brackets on (in from wide) and a callout ticks out beside them; a rare drop flashes a
/// CONTACT box round its tile; your best passed stamps NEW RECORD across the reticle as it turns a quarter; an entry opening
/// blinks the timer's block; a fight's start slams brackets onto its box with ENGAGE; its end says SPLASH ONE or MISSION FAILED.
/// </summary>
sealed partial class HudFighter : TacticalLook
{
    /// <summary>The note's pop-out moves this (HudStackLook.PopNote).</summary>
    protected override UIElement? NoteHost => _note;

    static readonly Color PhosC = Color.FromRgb(0x7C, 0xFF, 0x9E), AmberC = Color.FromRgb(0xFF, 0xB8, 0x3D), RedC = Color.FromRgb(0xFF, 0x55, 0x55),
        InkC0 = Color.FromRgb(0x01, 0x0A, 0x04), PaleC = Color.FromRgb(0xDC, 0xFF, 0xE6);
    static readonly SolidColorBrush Phos = Frozen(PhosC), PhosDim = Frozen(Tac.A(PhosC, .66)), PhosFaint = Frozen(Tac.A(PhosC, .3)), Amber = Frozen(AmberC),
        Red = Frozen(RedC), Dark = Frozen(InkC0), Pale = Frozen(PaleC);
    static readonly FontFamily Face = HudThemes.Mono;
    static readonly Pen HaloPen2 = Tac.P(Frozen(Tac.A(InkC0, .85)), 4.6), HaloPen3 = Tac.P(Frozen(Tac.A(InkC0, .85)), 5.2, round: true);

    // the look's parts (built once; a draw fills them)
    readonly Grid _root = new() { Margin = new Thickness(16, 14, 100, 14), HorizontalAlignment = HorizontalAlignment.Left };
    readonly StackPanel _body = new() { HorizontalAlignment = HorizontalAlignment.Left };
    readonly Border _timersTop = new() { Margin = new Thickness(0, 0, 0, 5), HorizontalAlignment = HorizontalAlignment.Left };
    readonly StackPanel _timers = new() { Orientation = Orientation.Horizontal };
    Border? _timerBlock;
    TimerBadge.Tone _timerTone;

    // the Kinah line, framed by brackets
    readonly Grid _line = new() { HorizontalAlignment = HorizontalAlignment.Left };
    readonly TacSketch _lineBrackets;
    /// <summary>The combiner glass inside the brackets: a dark pane with fine scan lines, so the small figures read over snow.</summary>
    readonly TacSketch _lineScrim;
    readonly StackPanel _lineRow = new() { Orientation = Orientation.Horizontal, Margin = new Thickness(9, 7, 14, 7) };
    readonly Canvas _lineAnchor = Tac.Anchor(HorizontalAlignment.Right);
    double _lineSpread, _lineFlash;
    readonly Grid _reticle = new() { Width = 54, Height = 54, VerticalAlignment = VerticalAlignment.Center };
    readonly TacSketch _reticleArt;
    readonly TacHalo _reticleText;
    readonly Canvas _reticleAnchor = Tac.Anchor(HorizontalAlignment.Center, VerticalAlignment.Center);
    double _share = -1, _spin;
    bool _beat;
    readonly StackPanel _figs = new() { VerticalAlignment = VerticalAlignment.Center };
    readonly StackPanel _contact = new() { Orientation = Orientation.Horizontal, Margin = new Thickness(4, 6, 0, 0), Visibility = Visibility.Collapsed };
    TacSketch? _contactBox;
    double _contactOn = 1;
    readonly TextBlock _noteText = new() { FontSize = 11.5, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(7, 1, 8, 2) };
    readonly Border _note = new() { HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(4, 6, 0, 0), BorderThickness = new Thickness(3, 0, 0, 0), Visibility = Visibility.Collapsed };

    // the fight's target box
    readonly Grid _fight = new() { HorizontalAlignment = HorizontalAlignment.Left, Visibility = Visibility.Collapsed, Margin = new Thickness(0, 0, 0, 12) };
    readonly Border _fightGround = new();
    readonly TacSketch _fightBrackets;
    readonly StackPanel _fightBody = new() { Margin = new Thickness(10, 8, 10, 8) };
    readonly Border _timersIn = new() { Margin = new Thickness(0, 0, 0, 7), HorizontalAlignment = HorizontalAlignment.Left };
    readonly Grid _fightFx = new() { IsHitTestVisible = false };
    double _fightSpread, _fightFlash;
    /// <summary>The head's KILLED / WIPED tag and its T+ clock: a callout flies into one after its hit.</summary>
    FrameworkElement? _outcomeEl, _clockEl;
    double _opacity = 0.9;

    public HudFighter() : base(bare: true, Face)
    {
        // the target list: the look's own rows (Ui/Themes/ThemeRows.cs)
        UseRows(TargetRow.Pitch, m => new TargetRow(), 20, GapLine);
        _lineBrackets = new TacSketch(DrawLineBrackets);
        _lineScrim = new TacSketch((dc, s) => Glass(dc, s, 3));
        _line.Children.Add(_lineScrim);
        _line.Children.Add(_lineBrackets);
        _line.Children.Add(_lineRow);
        _line.Children.Add(_lineAnchor);
        _reticleArt = new TacSketch(DrawReticle, 54, 54);
        _reticleText = new TacHalo(Tt("", 12.5, Phos, Face, FontWeights.Bold), InkC0) { HorizontalAlignment = HorizontalAlignment.Center };
        _reticle.Children.Add(_reticleArt); _reticle.Children.Add(_reticleText); _reticle.Children.Add(_reticleAnchor);
        _figs.Margin = new Thickness(8, 0, 0, 0);
        _lineRow.Children.Add(_reticle); _lineRow.Children.Add(_figs);
        _noteText.FontFamily = Face;
        _note.Child = _noteText;
        _note.Background = Frozen(Tac.A(InkC0, .78));

        _fightBrackets = new TacSketch(DrawFightBrackets);
        _fight.Children.Add(_fightGround); _fight.Children.Add(_fightBrackets); _fight.Children.Add(_fightBody); _fight.Children.Add(_fightFx);
        _timersTop.Child = _timers;

        _body.Children.Add(_timersTop); _body.Children.Add(_fight); _body.Children.Add(_line); _body.Children.Add(_contact); _body.Children.Add(_note);
        _root.Children.Add(_body);
        System.Windows.Documents.TextElement.SetFontFamily(_root, Face);
        SetOpacity(_opacity);
    }

    public override FrameworkElement Root => _root;
    public override Thickness Inset => _root.Margin;
    public override double Corner => 0;
    public override FrameworkElement? FightFrame => _fight.Visibility == Visibility.Visible ? _fight : null;

    public override void SetOpacity(double opacity)
    {
        _opacity = Math.Clamp(opacity, 0.3, 1);
        // the target box's ground: dark enough under the rows' white names on any scene, lighter as the setting goes down
        double a = 0.62 + 0.26 * _opacity;
        _fightGround.Background = Tac.Grad(false, (Tac.A(InkC0, a), 0), (Tac.A(InkC0, a), 0.7), (Tac.A(InkC0, a * 0.9), 1));
        _fightGround.BorderBrush = Tac.Grad(false, (Tac.A(PhosC, .32), 0), (Tac.A(PhosC, .08), 1));
        _fightGround.BorderThickness = new Thickness(0, 1, 0, 1);
        _lineScrim?.InvalidateVisual();
    }

    /// <summary>
    /// The HUD's glass: a dark pane <paramref name="inset"/> inside the brackets, a hair darker at its left, fine phosphor
    /// scan lines in it and a faint top edge, crisp, so the figures read on any scene and it reads as an instrument.
    /// </summary>
    void Glass(DrawingContext dc, Size s, double inset)
    {
        var r = new Rect(inset, inset, Math.Max(0, s.Width - 2 * inset), Math.Max(0, s.Height - 2 * inset));
        if (r.Width <= 0 || r.Height <= 0) return;
        double a = 0.5 + 0.3 * _opacity;
        dc.DrawRectangle(Tac.Grad(false, (Tac.A(InkC0, a), 0), (Tac.A(InkC0, a * .92), .75), (Tac.A(InkC0, a * .8), 1)), null, r);
        var line = Frozen(Tac.A(PhosC, .045));
        for (double y = r.Top + 2; y < r.Bottom; y += 3) dc.DrawRectangle(line, null, new Rect(r.Left, y, r.Width, 1));
        dc.DrawRectangle(Frozen(Tac.A(PhosC, .22)), null, new Rect(r.Left, r.Top, r.Width, 1));
        dc.DrawRectangle(Frozen(Tac.A(PhosC, .12)), null, new Rect(r.Left, r.Bottom - 1, r.Width, 1));
    }

    protected override string LineSig(Figures f) => base.LineSig(f) + "|" + (Rare(Values)?.Id ?? "") + "|" + StripSig(StripLook) + "|" + RunSig;

    /// <summary>The pickup strip as the HUD wears it: the tiles at the hour's height, the rule a faint phosphor line, the star amber.</summary>
    static readonly PickStrip.Look StripLook = new(Size: 24, Gap: 4, Rule: PhosFaint, Star: Amber);
    StackPanel? _topRow;

    /// <summary>
    /// The pickup strip beside the hour, inside the brackets, out to the line's right edge (the room the session line leaves
    /// there), so the line grows only by what the tiles need past it and never past the panel's width. When the best drop and
    /// the three don't fit there (a long hour, a narrow meter) they take their own row under the session line, inside the
    /// brackets still: always whole, a fight up or not (Steven, 2026-11-19).
    /// </summary>
    void PutStrip(Figures f)
    {
        if (_topRow is not { } top || !_figs.Children.Contains(top)) return;
        if (FarmStrip(f, StripLook) is not { } strip) return;
        _figs.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        _line.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        top.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        const double air = 12;
        // the room: the panel's width less the line as it is, plus what the session line already leaves beside the hour
        double room = FightWidth - 20 - _line.DesiredSize.Width + (_figs.DesiredSize.Width - _figs.Margin.Left - top.DesiredSize.Width) - air;
        if (Wants(strip) > room + .5)
        {
            strip.HorizontalAlignment = HorizontalAlignment.Left;
            strip.Margin = new Thickness(1, 5, 0, 1);
            _figs.Children.Add(strip);
            return;
        }
        int at = _figs.Children.IndexOf(top);
        _figs.Children.RemoveAt(at);
        var g = new Grid();
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MinWidth = air });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        g.Children.Add(top);
        strip.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(strip, 2); g.Children.Add(strip);
        _figs.Children.Insert(at, g);
    }

    // ================================================================ the Kinah line

    protected override void DrawLine(Figures f)
    {
        _figs.Children.Clear();
        _topRow = null;
        _reticle.Visibility = Visibility.Collapsed;
        bool mark = f.Abyss && f.Counted && f.Icon.Length > 0;
        if (_lineRow.Children.Count > 2) _lineRow.Children.RemoveAt(0);   // the last Abyss mark
        if (mark) { var m = AbyssMark(f.Icon, 34); m.Margin = new Thickness(2, 0, 2, 0); _lineRow.Children.Insert(0, m); }
        // in a dungeon run: the run's clock, splits and verdict in place of the hour (Ui/Themes/Tactical/HudFighter.Run.cs)
        if (RunNow is { } rs) DrawRun(rs);
        else if (!HasHour(f))
        {
            // nothing counted yet: a standby tag and the line's words
            var row = new StackPanel { Orientation = Orientation.Horizontal };
            row.Children.Add(Tag(L.T("STBY"), PhosDim, Dark, 10.5));
            var w = Halo(Waiting(f.Abyss), 12.5, PhosDim, FontWeights.SemiBold); w.Margin = new Thickness(7, 0, 0, 0);
            row.Children.Add(w);
            _figs.Children.Add(row);
            _figs.Margin = new Thickness(mark ? 8 : 0, 0, 0, 0);
        }
        else
        {
            bool ring = HasRing(f);
            _share = f.Share; _beat = f.Beat;
            if (ring)
            {
                _reticle.Visibility = Visibility.Visible;
                _reticleText.Text = f.Beat ? "★" : Pct(f.Share);
                _reticleText.Fill = f.Beat ? Amber : Phos;
                _reticleArt.InvalidateVisual();
            }
            _figs.Margin = new Thickness(ring || mark ? 8 : 0, 0, 0, 0);
            var top = new StackPanel { Orientation = Orientation.Horizontal };
            var rateTone = f.Warm ? PhosDim : f.Abyss ? Pale : Phos;
            var rate = Halo(f.Warm ? f.Rate.ToUpperInvariant() : f.Rate, f.Warm ? 17 : 27, rateTone, FontWeights.Bold);
            top.Children.Add(rate);
            if (f.Unit.Length > 0) { var u = Halo(f.Unit, 13, PhosDim, FontWeights.Bold); u.VerticalAlignment = VerticalAlignment.Bottom; u.Margin = new Thickness(3, 0, 0, 5); top.Children.Add(u); }
            _figs.Children.Add(top);
            _topRow = top;
            // SES 894K · BEST −130K (in the Abyss: AP 24.7K)
            var sub = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(1, -1, 0, 0) };
            if (f.Total.Length > 0) { sub.Children.Add(Label(f.Abyss ? f.TotalWord : f.Today ? L.T("today").ToUpperInvariant() : L.T("SES"))); sub.Children.Add(Fig(f.Total, Phos, 16)); }
            var gap = Gap(f);
            if (gap.Length > 0 && !f.Abyss)
            {
                if (sub.Children.Count > 0) sub.Children.Add(Dot());
                sub.Children.Add(Label(f.Beat ? L.T("BEST") : L.T("TO BEST")));
                sub.Children.Add(Fig(gap.TrimStart('−'), f.Beat ? Amber : PhosDim, 13));
            }
            if (sub.Children.Count > 0) _figs.Children.Add(sub);
        }
        // the Abyss's kills, Rank Points, deaths and Kinah: each figure in its colour, its word dim
        if (f.Stats.Count > 0)
        {
            var st = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(1, 2, 0, 0) };
            foreach (var (fig, word, tone) in f.Stats)
            {
                if (st.Children.Count > 0) st.Children.Add(Dot());
                st.Children.Add(Fig(fig, ReferenceEquals(tone, Rec) ? Red : ReferenceEquals(tone, Gold) ? Amber : Pale, 12));
                var wd = Label(word, 10.5); wd.Margin = new Thickness(4, 0, 0, 0); st.Children.Add(wd);
            }
            _figs.Children.Add(st);
        }
        PutStrip(f);
        _lineBrackets.InvalidateVisual();
        DrawContact();
        // the note on its own dark tag, a bar of its tone at its edge (a sentence reads best on a ground)
        _noteText.Text = f.Note;
        var nt = ReferenceEquals(f.NoteTone, Warn) || ReferenceEquals(f.NoteTone, Gold) ? Amber : Phos;
        _noteText.Foreground = nt; _note.BorderBrush = nt;
        _note.Visibility = f.Note.Length > 0 && !KinahHidden ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>The rare drop the window holds: its tile in a CONTACT box under the line, its name in its grade's colour.</summary>
    void DrawContact()
    {
        _contact.Children.Clear();
        if (KinahHidden || Rare(Values) is not { } r) { _contact.Visibility = Visibility.Collapsed; _contactBox = null; return; }
        var tileHost = new Grid { Width = 40, Height = 40 };
        _contactBox = new TacSketch((dc, s) =>
        {
            if (_contactOn <= 0) return;
            var b = Frozen(Tac.A(AmberC, _contactOn));
            var g = Tac.Cut(new Rect(1.5, 1.5, s.Width - 3, s.Height - 3), 6, all: true);
            dc.DrawGeometry(null, HaloPen2, g);
            dc.DrawGeometry(Frozen(Tac.A(AmberC, _contactOn > 1 ? .5 : .16 * _contactOn)), Tac.P(b, 2), g);
        }, 40, 40);
        tileHost.Children.Add(_contactBox);
        var tile = Tile(r, 28); tile.HorizontalAlignment = HorizontalAlignment.Center; tile.VerticalAlignment = VerticalAlignment.Center;
        tileHost.Children.Add(tile);
        _contact.Children.Add(tileHost);
        var words = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0) };
        words.Children.Add(Tag("▲ " + L.T("CONTACT"), Amber, Dark, 10));
        var name = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 3, 0, 0) };
        // the name on a dark tag (a light grade colour over snow needs a ground)
        name.Children.Add(Tt(r.Name, 13.5, Frozen(Tac.Mix(ColorOf(r.Color, r.Grade), Colors.White, .3)), Face, FontWeights.Bold));
        if (r.Count.Length > 0) { var c = Tt(r.Count, 11.5, PhosDim, Face, FontWeights.SemiBold); c.Margin = new Thickness(5, 0, 0, 0); name.Children.Add(c); }
        name.Margin = new Thickness(0);
        var nameTag = new Border { Background = Frozen(Tac.A(InkC0, .8)), Padding = new Thickness(6, 1, 7, 2), Margin = new Thickness(0, 3, 0, 0), HorizontalAlignment = HorizontalAlignment.Left, Child = name };
        words.Children.Add(nameTag);
        _contact.Children.Add(words);
        _contact.Visibility = Visibility.Visible;
    }

    /// <summary>The line's corner brackets, <see cref="_lineSpread"/> out from their place (the lock-on), whiter while <see cref="_lineFlash"/>.</summary>
    void DrawLineBrackets(DrawingContext dc, Size s) => Brackets(dc, s, _lineSpread, _lineFlash, 10);

    void DrawFightBrackets(DrawingContext dc, Size s) => Brackets(dc, s, _fightSpread, _fightFlash, 12);

    static void Brackets(DrawingContext dc, Size s, double spread, double flash, double arm, double alpha = 1)
    {
        if (s.Width <= 0 || s.Height <= 0 || alpha <= 0) return;
        var tone = Frozen(Tac.A(Tac.Mix(PhosC, Colors.White, flash), alpha));
        double l = 1 - spread, t = 1 - spread, r = s.Width - 1 + spread, b = s.Height - 1 + spread;
        var g = new StreamGeometry();
        using (var x = g.Open())
        {
            void L3(Point a, Point c, Point d) { x.BeginFigure(a, false, false); x.LineTo(c, true, false); x.LineTo(d, true, false); }
            L3(new Point(l, t + arm), new Point(l, t), new Point(l + arm, t));
            L3(new Point(r - arm, t), new Point(r, t), new Point(r, t + arm));
            L3(new Point(r, b - arm), new Point(r, b), new Point(r - arm, b));
            L3(new Point(l + arm, b), new Point(l, b), new Point(l, b - arm));
        }
        g.Freeze();
        dc.DrawGeometry(null, HaloPen2, g);
        dc.DrawGeometry(null, Tac.P(tone, 2), g);
    }

    /// <summary>The reticle: a fine ring, the share as a heavier arc with a pip at its end, the four ticks (turned by <see cref="_spin"/>).</summary>
    void DrawReticle(DrawingContext dc, Size s)
    {
        double c = s.Width / 2, r = 17;
        var center = new Point(c, c);
        var tone = _beat ? Amber : Phos;
        dc.DrawEllipse(null, Tac.P(Frozen(Tac.A(InkC0, .8)), 4.4), center, r, r);
        dc.DrawEllipse(null, Tac.P(PhosFaint, 1.4), center, r, r);
        double share = Math.Clamp(_beat ? 1 : _share, 0, 1);
        if (share > 0.004)
        {
            Geometry arc;
            if (share >= 0.999) arc = new EllipseGeometry(center, r, r);
            else
            {
                double a = 2 * Math.PI * share;
                var fig = new PathFigure { StartPoint = new Point(c, c - r) };
                fig.Segments.Add(new ArcSegment(new Point(c + r * Math.Sin(a), c - r * Math.Cos(a)), new Size(r, r), 0, a > Math.PI, SweepDirection.Clockwise, true));
                var pg = new PathGeometry(); pg.Figures.Add(fig); arc = pg;
            }
            dc.DrawGeometry(null, Tac.P(Frozen(Tac.A(InkC0, .9)), 5.6), arc);
            dc.DrawGeometry(null, Tac.P(tone, 2.6), arc);
            if (share < 0.999)
            {
                double a = 2 * Math.PI * share;
                var tip = new Point(c + r * Math.Sin(a), c - r * Math.Cos(a));
                var pip = new RectangleGeometry(new Rect(tip.X - 2.4, tip.Y - 2.4, 4.8, 4.8), 0, 0, new RotateTransform(45, tip.X, tip.Y));
                dc.DrawGeometry(tone, Tac.P(Dark, 1.2), pip);
            }
        }
        // the ticks: outside the ring at the four points, and short dim ones between
        var ticks = new StreamGeometry();
        using (var x = ticks.Open())
        {
            for (int i = 0; i < 4; i++)
            {
                double a = (i * 90 + _spin) * Math.PI / 180;
                x.BeginFigure(new Point(c + (r + 3) * Math.Sin(a), c - (r + 3) * Math.Cos(a)), false, false);
                x.LineTo(new Point(c + (r + 9) * Math.Sin(a), c - (r + 9) * Math.Cos(a)), true, false);
            }
        }
        ticks.Freeze();
        dc.DrawGeometry(null, Tac.P(Frozen(Tac.A(InkC0, .9)), 4.6), ticks);
        dc.DrawGeometry(null, Tac.P(tone, 2), ticks);
        var minor = new StreamGeometry();
        using (var x = minor.Open())
            for (int i = 0; i < 4; i++)
            {
                double a = (45 + i * 90 + _spin) * Math.PI / 180;
                x.BeginFigure(new Point(c + (r + 3) * Math.Sin(a), c - (r + 3) * Math.Cos(a)), false, false);
                x.LineTo(new Point(c + (r + 5.5) * Math.Sin(a), c - (r + 5.5) * Math.Cos(a)), true, false);
            }
        minor.Freeze();
        dc.DrawGeometry(null, Tac.P(PhosDim, 1.2), minor);
    }

    protected override void KinahShown(bool shown)
    {
        _line.Visibility = shown ? Visibility.Visible : Visibility.Collapsed;
        if (!shown) { _note.Visibility = Visibility.Collapsed; _contact.Visibility = Visibility.Collapsed; }
        _fight.Margin = new Thickness(0, 0, 0, shown ? 12 : 0);
        PlaceTimers();
    }

    // ================================================================ the timers

    protected override void DrawTimers(IReadOnlyList<TimerBoard.Item> due, DateTime now)
    {
        _timers.Children.Clear();
        _timerBlock = null;
        // inside the fight's box (a dungeon) two fit beside each other; above the line, three
        foreach (var i in due.Take(KinahHidden ? 2 : HudWindow.TimerLineMax))
        {
            var (text, tone) = TimerWords(i, now);
            if (_timers.Children.Count == 0)
            {
                // the next one: a solid block, dark text (amber when soon, an outline while it's quiet)
                _timerTone = tone;
                _timerBlock = new Border { Padding = new Thickness(7, 2, 7, 2), BorderThickness = new Thickness(1.5), VerticalAlignment = VerticalAlignment.Center, Child = Tt(text, 12, Dark, Face, FontWeights.Bold) };
                PaintBlock(_timerBlock, tone, on: true);
                _timers.Children.Add(Grounded(_timerBlock));
            }
            else
            {
                var c = tone == TimerBadge.Tone.Soon ? AmberC : PhosC;
                var t = new Border
                {
                    Background = Frozen(Tac.A(InkC0, .72)), BorderBrush = Frozen(Tac.A(c, .45)), BorderThickness = new Thickness(1), Padding = new Thickness(6, 1, 6, 2),
                    Margin = new Thickness(6, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, Child = Tt(text, 11, Frozen(Tac.A(c, .9)), Face, FontWeights.SemiBold),
                };
                _timers.Children.Add(t);
            }
        }
        PlaceTimers();
    }

    /// <summary>The block in its tone: solid with dark text (<paramref name="on"/>), or its outline with the text in the tone (an entry's blink).</summary>
    static void PaintBlock(Border b, TimerBadge.Tone tone, bool on)
    {
        var c = tone == TimerBadge.Tone.Soon ? AmberC : PhosC;
        bool solid = on && tone != TimerBadge.Tone.Quiet;
        b.Background = solid ? Frozen(c) : Frozen(Tac.A(InkC0, .78));
        b.BorderBrush = Frozen(solid ? Tac.A(InkC0, .9) : c);
        if (b.Child is TextBlock t) t.Foreground = solid ? Dark : Frozen(c);
    }

    /// <summary>A part with a dark hairline round it (a solid block on snow keeps its edge).</summary>
    FrameworkElement Grounded(Border b)
    {
        var g = new Grid { VerticalAlignment = VerticalAlignment.Center };
        g.Children.Add(new Border { BorderBrush = Frozen(Tac.A(InkC0, .7)), BorderThickness = new Thickness(1), Child = b });
        // the entry's lock: brackets round the block, snapping in as it blinks (drawn only while an entry opens)
        _blockBrackets = new TacSketch((dc, s) => { if (_blockLock >= 0) Brackets(dc, s, _blockLock, 0, 6, _blockLockA); });
        g.Children.Add(_blockBrackets);
        return g;
    }
    TacSketch? _blockBrackets;
    double _blockLock = -1, _blockLockA = 1;

    /// <summary>The timers above everything, or inside the fight's box in a dungeon (the Kinah line away).</summary>
    void PlaceTimers()
    {
        bool inside = KinahHidden && _fight.Visibility == Visibility.Visible;
        var host = inside ? _timersIn : _timersTop;
        var other = inside ? _timersTop : _timersIn;
        if (other.Child == _timers) other.Child = null;
        if (host.Child != _timers) host.Child = _timers;
        _timersTop.Visibility = !inside && _timers.Children.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        _timersIn.Visibility = inside && _timers.Children.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    // ================================================================ the fight

    protected override void DrawFight(Encounter? e, DateTime now)
    {
        StripFollowsFight(e);
        _fightBody.Children.Clear();
        _outcomeEl = _clockEl = null;
        if (e == null) { _fight.Visibility = Visibility.Collapsed; _fightFx.Children.Clear(); PlaceTimers(); return; }
        double w = FightWidth - 20;
        _fight.Width = w;
        _fightBody.Children.Add(_timersIn);
        // [ Kashapa ]                T+01:42
        var head = new Grid();
        head.ColumnDefinitions.Add(new ColumnDefinition()); head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var title = new StackPanel { Orientation = Orientation.Horizontal };
        title.Children.Add(Halo("[", 14, PhosDim, FontWeights.Bold));
        var tt = Halo(e.Title, 14, Amber, FontWeights.Bold); tt.Margin = new Thickness(5, 0, 5, 0); title.Children.Add(tt);
        title.Children.Add(Halo("]", 14, PhosDim, FontWeights.Bold));
        head.Children.Add(title);
        var right = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(8, 0, 0, 0) };
        if (e.Waiting) right.Children.Add(Tag(L.T("READY"), PhosDim, Dark, 10.5));
        else
        {
            var outcome = OutcomeText(e);
            if (outcome.Length > 0) { var ot = Tag(outcome.ToUpperInvariant(), e.Outcome == "wipe" ? Red : Phos, Dark, 10.5); ot.Margin = new Thickness(0, 0, 8, 0); right.Children.Add(ot); _outcomeEl = ot; }
            var clockFig = Halo(Clock(e, now) is { Length: > 0 } tc ? "T+" + Pad(tc) : "", 15, e.Live ? Amber : Phos, FontWeights.Bold);
            right.Children.Add(clockFig); _clockEl = clockFig;
        }
        Grid.SetColumn(right, 1); head.Children.Add(right);
        // a long title gives way to the outcome and the clock: cut with "…", its brackets kept round it (it ran under them)
        head.SizeChanged += (_, _) => FitTitle(tt, e.Title, head.ColumnDefinitions[0].ActualWidth - ((FrameworkElement)title.Children[0]).ActualWidth - ((FrameworkElement)title.Children[2]).ActualWidth - tt.Margin.Left - tt.Margin.Right - 2);
        _fightBody.Children.Add(head);
        if (e.Waiting)
        {
            var wr = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 2) };
            wr.Children.Add(Tag(L.T("STBY"), PhosDim, Dark, 10));
            var wt = Halo(e.ReadyText, 12.5, PhosDim, FontWeights.SemiBold); wt.Margin = new Thickness(7, 0, 0, 0); wr.Children.Add(wt);
            _fightBody.Children.Add(wr);
            if (PartyBeforeHit(e)) { _fightBody.Children.Add(new Border { Height = 6 }); PutRows(_fightBody, e, now, w - 20); }
        }
        else
        {
            if (HpOf(e) is { } hp)
            {
                // HP ▰▰▱  47% · 1.82M LEFT
                var hpRow = new Grid { Margin = new Thickness(0, 6, 0, 6) };
                hpRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); hpRow.ColumnDefinitions.Add(new ColumnDefinition()); hpRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                hpRow.Children.Add(Label(L.T("HP"), 11));
                var track = new Grid { Height = 10, Margin = new Thickness(6, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center };
                track.Children.Add(new TacSketch((dc, s) => Thirds(dc, s, hp)));
                HpGhost(track, e, hp, now, 0);
                Grid.SetColumn(track, 1); hpRow.Children.Add(track);
                var words = new StackPanel { Orientation = Orientation.Horizontal };
                words.Children.Add(Fig($"{Math.Round(hp * 100):0}%", hp < .34 ? Red : Phos, 12.5));
                var left = HpLeftText(e);
                if (left.Length > 0) { words.Children.Add(Dot()); words.Children.Add(Label(left.ToUpperInvariant(), 11)); }
                Grid.SetColumn(words, 2); hpRow.Children.Add(words);
                _fightBody.Children.Add(hpRow);
            }
            else _fightBody.Children.Add(new Border { Height = 6 });
            PutRows(_fightBody, e, now, w - 20);
            if (e.HasDamage)
            {
                _fightBody.Children.Add(new TacSketch((dc, s) =>
                {
                    dc.DrawLine(Tac.P(PhosDim, 1, dash: new DashStyle(new double[] { 3, 3 }, 0)), new Point(0, 0.5), new Point(s.Width, 0.5));
                }) { Height = 1, Margin = new Thickness(0, 6, 0, 5) });
                _fightBody.Children.Add(Foot(e, now));
            }
        }
        _fight.Visibility = Visibility.Visible;
        _fightBrackets.InvalidateVisual();
        PlaceTimers();
    }

    /// <summary>The boss's HP in three segments: spent thirds as outlines, the current one part filled, the rest full.</summary>
    static void Thirds(DrawingContext dc, Size s, double hp)
    {
        const double gap = 4;
        double seg = (s.Width - 2 * gap) / 3, h = s.Height;
        var fill = hp < .34 ? Red : Phos;
        var line = hp < .34 ? Frozen(Tac.A(RedC, .55)) : PhosDim;
        for (int i = 0; i < 3; i++)
        {
            var r = new Rect(i * (seg + gap), 0, seg, h);
            double part = Math.Clamp(hp * 3 - i, 0, 1);
            dc.DrawRectangle(Frozen(Tac.A(InkC0, .55)), Tac.P(line, 1), new Rect(r.X + .5, r.Y + .5, r.Width - 1, r.Height - 1));
            if (part > 0) dc.DrawRectangle(fill, null, new Rect(r.X + 2, r.Y + 2, Math.Max(0, (r.Width - 4) * part), r.Height - 4));
        }
    }

    // ================================================================ the moments

    /// <summary>A pickup: the brackets lock on from wide, white-hot, and a callout ticks out beside them with its worth.</summary>
    protected override void Pickup(Pick p, bool held)
    {
        if (KinahHidden || _line.Visibility != Visibility.Visible) return;
        var call = new Canvas { IsHitTestVisible = false };
        var worth = p.Worth.Length > 0 ? p.Worth : p.Count;
        var box = new Border { Background = Phos, BorderBrush = Frozen(Tac.A(InkC0, .9)), BorderThickness = new Thickness(1), Padding = new Thickness(5, 0, 5, 1), Child = Tt(worth, 13, Dark, Face, FontWeights.Bold) };
        var name = Halo(p.Name.Length > 12 ? p.Name[..11].TrimEnd() + "…" : p.Name, 10.5, PhosDim, FontWeights.SemiBold);
        double lead = 0;
        var leader = new TacSketch((dc, s) =>
        {
            if (lead <= 0) return;
            // from the top-right bracket out: down-right 9, then along 12
            var a = new Point(2, 2); var k = new Point(10, -7); var z = new Point(22, -7);
            var g = new StreamGeometry();
            using (var x = g.Open())
            {
                x.BeginFigure(a, false, false);
                double d1 = Math.Min(1, lead * 2), d2 = Math.Clamp(lead * 2 - 1, 0, 1);
                x.LineTo(new Point(a.X + (k.X - a.X) * d1, a.Y + (k.Y - a.Y) * d1), true, false);
                if (d2 > 0) x.LineTo(new Point(k.X + (z.X - k.X) * d2, k.Y), true, false);
            }
            g.Freeze();
            dc.DrawGeometry(null, HaloPen3, g);
            dc.DrawGeometry(null, Tac.P(Phos, 1.6, round: true), g);
        }, 1, 1);
        call.Children.Add(leader);
        Canvas.SetLeft(box, 22); Canvas.SetTop(box, -16);
        Canvas.SetLeft(name, 23); Canvas.SetTop(name, 4);
        call.Children.Add(box); call.Children.Add(name);
        _lineAnchor.Children.Clear();
        _lineAnchor.Children.Add(call);
        Motion.Play(call, 1800, t =>
        {
            // the lock: in from 11 px out over the first third (inside the window's 14 px room: never cut), a white flash as it seats
            double lk = Seg(t, 0, .32); lk = lk * lk * (3 - 2 * lk);
            _lineSpread = 11 * (1 - lk);
            _lineFlash = t < .3 ? .6 * lk : 1 - Seg(t, .3, .5);
            _lineFlash = Math.Clamp(_lineFlash, 0, 1);
            _lineBrackets.InvalidateVisual();
            // the callout: the leader runs out, the box lands on its end, holds, then all of it goes
            lead = Seg(t, .26, .42); leader.InvalidateVisual();
            double show = t < .4 ? 0 : t < .86 ? 1 : 1 - Seg(t, .86, 1);
            box.Opacity = name.Opacity = show;
            call.Opacity = t < .86 ? 1 : 1 - Seg(t, .86, 1);
        }, () => { _lineAnchor.Children.Remove(call); _lineSpread = 0; _lineFlash = 0; _lineBrackets.InvalidateVisual(); });
    }

    /// <summary>A rare drop: the CONTACT box round its tile flashes twice, then holds; a square pings out from it.</summary>
    protected override void RareDrop(Pick p, bool held)
    {
        if (_contactBox == null || _contact.Parent is not Panel) return;
        var host = (Grid)_contactBox.Parent;
        double ping = 0;
        // the ping in the drop's grade (the big one: a pickup never pings)
        var gc = ColorOf(p.Color, p.Grade);
        var ring = new TacSketch((dc, s) =>
        {
            if (ping <= 0 || ping >= 1) return;
            double grow = 10 * Out(ping);   // at its widest still inside the window's 14 px room (its outline and halo in)
            var r = new Rect(-grow, -grow, s.Width + 2 * grow, s.Height + 2 * grow);
            dc.DrawGeometry(null, HaloPen2, Tac.Cut(r, 8, all: true));
            dc.DrawGeometry(null, Tac.P(Frozen(Tac.A(gc, Math.Max(.35, 1 - ping))), 2.4), Tac.Cut(r, 8, all: true));
        }) { Width = 40, Height = 40 };
        host.Children.Add(ring);
        var box = _contactBox;
        // the tile's box locks on: it punches past its size as the first flash lands, settles as the ping runs out
        // (it grows up from near its foot: the window's edge is a pixel under it)
        var pop = PopOn(host, new Point(.5, .9));
        Motion.Play(ring, 1500, t =>
        {
            // on, off, on, off, on (two flashes), then held on
            int phase = (int)(t / .1);
            _contactOn = t >= .5 ? 1 : phase % 2 == 0 ? 1.01 : 0.12;   // over 1: lit (a filled flash)
            box.InvalidateVisual();
            ping = Seg(t, 0, .55); ring.InvalidateVisual();
            Pop(pop, t, .02, .36, .28);
        }, () => { _contactOn = 1; box.InvalidateVisual(); host.Children.Remove(ring); host.ClearValue(UIElement.RenderTransformProperty); });
    }

    /// <summary>Your best passed: the reticle turns a quarter and NEW RECORD is stamped across it.</summary>
    protected override void BestPassed(bool held)
    {
        if (_reticle.Visibility != Visibility.Visible) return;
        var band = new Border { Background = Amber, BorderBrush = Frozen(Tac.A(InkC0, .9)), BorderThickness = new Thickness(1), Padding = new Thickness(5, 0, 5, 0), Child = Tt(L.T("NEW RECORD"), 9.5, Dark, Face, FontWeights.Bold) };
        band.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        // the plate opens by its scale from the middle (a clip cut its words while it opened), and is stamped: past its size, back
        var bs = new ScaleTransform(0, 1); band.RenderTransformOrigin = new Point(.5, .5); band.RenderTransform = bs;
        // centred across the reticle, an amber rule through it behind the plate
        Canvas.SetLeft(band, -band.DesiredSize.Width / 2); Canvas.SetTop(band, -band.DesiredSize.Height / 2);
        var rule = new Border { Height = 1.5, Width = 0, Background = Amber };
        Canvas.SetTop(rule, -.75);
        _reticleAnchor.Children.Clear();
        _reticleAnchor.Children.Add(rule);
        _reticleAnchor.Children.Add(band);
        // the hour itself takes the hit as the plate is stamped: a punch past its size, settled
        var figs = _figs; var pop = PopOn(figs, new Point(0, .5));
        Motion.Play(band, 2000, t =>
        {
            Pop(pop, t, .12, .5, .15);
            _spin = 90 * Back(Seg(t, 0, .35)); _reticleArt.InvalidateVisual();
            double k = Seg(t, .12, .26);
            bs.ScaleX = Out(k); bs.ScaleY = t < .12 ? 1 : 1 + .35 * (1 - Back(Seg(t, .2, .34), 2.4)) * (k >= 1 ? 1 : 0);
            double rw = 64 * Out(Seg(t, .05, .2)); rule.Width = rw; Canvas.SetLeft(rule, -rw / 2); rule.Opacity = band.Opacity;
            band.Opacity = t < .85 ? (t > .3 && t < .4 && (int)((t - .3) / .025) % 2 == 1 ? .25 : 1) : 1 - Seg(t, .85, 1);
        }, () => { _reticleAnchor.Children.Clear(); _spin = 0; _reticleArt.InvalidateVisual(); figs.ClearValue(UIElement.RenderTransformProperty); });
    }

    /// <summary>An entry opened: the timer's block blinks twice (the block: its words stay put).</summary>
    protected override void EntryOpened(TimerBoard.Item i, bool held)
    {
        var blk = _timerBlock;
        var ps = new ScaleTransform(1, 1);
        if (blk != null) { blk.RenderTransformOrigin = new Point(0, .5); blk.RenderTransform = ps; }
        Motion.Play(_timers, 1300, t =>
        {
            bool on = t >= .6 || (int)(t / .15) % 2 == 1;
            // the block jolts as its brackets lock: wider and flatter for a frame, back past its size, settled
            // (its width never past its own: the next chip sits 5 px off)
            ps.ScaleX = t < .08 ? .82 : t < .3 ? .82 + .18 * Back(Seg(t, .08, .3), 1.2) : 1;
            ps.ScaleY = t < .08 ? 1.35 : t < .3 ? 1 + .35 * (1 - Back(Seg(t, .08, .3), 2.4)) : 1;
            if (_timerBlock != null) PaintBlock(_timerBlock, TimerBadge.Tone.On, on);
            // the brackets hold wide a beat, then lock on with an overshoot
            _blockLock = t < .14 ? 12 : 10 * (1 - Back(Seg(t, .14, .32), 2)) + 2;
            _blockLockA = 1 - Seg(t, .7, 1);
            _blockBrackets?.InvalidateVisual();
        }, () => { if (_timerBlock != null) PaintBlock(_timerBlock, _timerTone, true); blk?.ClearValue(UIElement.RenderTransformProperty); _blockLock = -1; _blockBrackets?.InvalidateVisual(); });
    }

    /// <summary>A fight started: brackets slam onto its box from wide, ENGAGE hits across it, then flies into the T+ clock, which pops.</summary>
    protected override void FightStarted(Encounter e, bool held)
    {
        var banner = Banner(L.T("ENGAGE"), null, Amber, AmberC, hit: .12, leave: .22, _clockEl, slump: false);
        _fightFx.Children.Clear(); _fightFx.Children.Add(banner.Root);
        var body = _fightBody;
        var clock = _clockEl; var pop = PopOn(clock, new Point(1, .5));
        Motion.Play(_fightFx, 1800, t =>
        {
            double k = Seg(t, 0, .12); k = k * k * (3 - 2 * k);
            // from 11 px out (the window's room round the box is 14: the slam is never cut at its edge), a recoil as they seat
            _fightSpread = 11 * (1 - k) - (t > .12 && t < .19 ? 3 * Math.Sin((t - .12) / .07 * Math.PI) : 0);
            _fightFlash = t < .12 ? 0 : 1 - Seg(t, .12, .3);
            _fightBrackets.InvalidateVisual();
            banner.Step(t);
            Pop(pop, t, .27, .5, .35);
        }, () => { _fightFx.Children.Clear(); _fightSpread = 0; _fightFlash = 0; _fightBrackets.InvalidateVisual(); body.Opacity = 1; if (clock != null) { clock.Opacity = 1; clock.ClearValue(UIElement.RenderTransformProperty); } });
    }

    /// <summary>
    /// A fight ended. A kill: the brackets kick out and lock back on white-hot, SPLASH ONE hits across the box (the rows dimmed
    /// for that moment) and flies into the KILLED tag, which pops; a green light sweeps the rows. A wipe: the box shudders,
    /// MISSION FAILED hits and slumps away (no overshoot), the WIPED tag flickers red, the rows sag and stutter.
    /// </summary>
    protected override void FightEnded(Encounter e, bool held)
    {
        bool wipe = e.Outcome == "wipe";
        if (e.Outcome is not ("kill" or "wipe" or "cleared")) return;
        var target = _outcomeEl ?? _clockEl;
        var banner = wipe ? Banner(L.T("MISSION FAILED"), null, Red, RedC, hit: .08, leave: .24, target, slump: true)
            : e.Outcome == "cleared" ? Banner(L.T("MISSION COMPLETE"), L.T("ALL TARGETS DOWN"), Phos, PhosC, hit: .08, leave: .22, target, slump: false)
            : Banner(L.T("SPLASH ONE"), L.T("TARGET DOWN"), Phos, PhosC, hit: .08, leave: .22, target, slump: false);
        _fightFx.Children.Clear(); _fightFx.Children.Add(banner.Root);
        var body = _fightBody;
        var shake = new TranslateTransform(); _fight.RenderTransform = shake;
        var sag = new TranslateTransform(); body.RenderTransform = sag;
        var pop = wipe ? null : PopOn(target, new Point(.5, .5));
        if (!wipe) LightSweep(_fightFx, PhosC, 2000, .3, .62, new Thickness(0, 30, 0, 0));
        Motion.Play(_fightFx, 2000, t =>
        {
            banner.Step(t);
            if (wipe)
            {
                int s = (int)(t * 60);
                shake.X = t < .3 ? Math.Round(5 * (1 - t / .3) * (s % 2 == 0 ? 1 : -1)) : 0;
                _fightFlash = t < .3 && s % 3 != 0 ? .7 : 0;
                _fightSpread = 0;
                // the rows sag 2 px and stutter as the box gives up, then stand again
                sag.Y = t > .24 && t < .6 ? 2 : 0;
                if (t > .26 && t < .5) body.Opacity = s % 4 == 0 ? .5 : 1;
                if (target != null) target.Opacity = t > .26 && t < .56 && s % 3 == 0 ? .25 : 1;
            }
            else
            {
                _fightFlash = t < .1 ? Seg(t, 0, .05) : t < .28 ? 1 - Seg(t, .1, .2) : .8 * (1 - Seg(t, .28, .45));
                _fightSpread = t < .1 ? 8 * Out(Seg(t, 0, .06)) : 8 * (1 - Back(Seg(t, .1, .2), 2));
                Pop(pop, t, .28, .48, .35);
            }
            _fightBrackets.InvalidateVisual();
        }, () =>
        {
            _fightFx.Children.Clear(); _fightFlash = 0; _fightSpread = 0; _fightBrackets.InvalidateVisual();
            _fight.ClearValue(UIElement.RenderTransformProperty); body.ClearValue(UIElement.RenderTransformProperty); body.Opacity = 1;
            if (target != null) { target.Opacity = 1; if (!wipe) target.ClearValue(UIElement.RenderTransformProperty); }
        });
    }

    /// <summary>
    /// A callout across the fight's box: a dark band that opens from its middle, double rules, the words (and a smaller
    /// line). Its step at 0..1: the rules run out and the words hit at <paramref name="hit"/> (the box under it dimmed for that
    /// moment only); at <paramref name="leave"/> it flies into <paramref name="target"/> (the head's tag or clock), shrinking to
    /// nothing, or with <paramref name="slump"/> it drops and fades, beaten (2026-11-21: a callout held two seconds over the rows,
    /// then over the head, hid the figures and the boss).
    /// </summary>
    (FrameworkElement Root, Action<double> Step) Banner(string big, string? small, Brush tone, Color toneC, double hit, double leave, FrameworkElement? target, bool slump)
    {
        var (root, step0) = BannerArt(big, small, tone, toneC);
        var lift = new TranslateTransform(); var shrink = new ScaleTransform(1, 1);
        var grid = (FrameworkElement)((Grid)root).Children[1];
        var words = ((Grid)grid).Children[^1];
        grid.RenderTransformOrigin = new Point(.5, .5); grid.RenderTransform = new TransformGroup { Children = { shrink, lift } };
        Vector? to = null; bool sought = false;
        var body = _fightBody;
        return (root, t =>
        {
            if (!sought)
            {
                sought = true;
                if (Centre(target, _fight) is Point c && _fight.ActualWidth > 0) to = new Vector(c.X - _fight.ActualWidth / 2, c.Y - _fight.ActualHeight / 2);
            }
            step0(t, hit);
            double k = Seg(t, leave, leave + .08);
            if (slump) { lift.Y = 22 * k * k; shrink.ScaleY = 1 - .35 * k * k; grid.Opacity = 1 - k * k; }
            else if (to is Vector v) { double q = Out(k); lift.X = v.X * q; lift.Y = v.Y * q; shrink.ScaleX = shrink.ScaleY = 1 - .86 * q; grid.Opacity = k < .7 ? 1 : 1 - (k - .7) / .3; }
            else grid.Opacity = 1 - k;
            if (k >= 1) grid.Opacity = 0;
            if (k > 0) words.Opacity = 0;
            // the part it flies into steps aside while it lands in it, then pops (never two words over each other)
            if (target != null && !slump && to != null) target.Opacity = k > .05 && k < 1 ? 0 : 1;
            // the box dims under the hit only
            double down = Seg(t, hit - .08, hit - .03), up = Seg(t, leave, leave + .06);
            body.Opacity = 1 - .88 * down * (1 - up);
        });
    }
    /// <summary>The callout's art and its course (the rules, the band, the words; the dim under it only while it hits).</summary>
    (FrameworkElement Root, Action<double, double> Step) BannerArt(string big, string? small, Brush tone, Color toneC)
    {
        var outer = new Grid();
        // the box under the callout dims, so the words stand alone on it
        var dim = new Border { Background = Frozen(Tac.A(InkC0, .82)), Opacity = 0 };
        outer.Children.Add(dim);
        var grid = new Grid { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 0, 0) };
        outer.Children.Add(grid);
        // the two rules run out from the middle first, then the band fills between them
        var scale = new ScaleTransform(0, 1);
        var band = new Border { Background = Frozen(Tac.A(InkC0, .92)), Opacity = 0 };
        grid.Children.Add(band);
        var rules = new Grid { RenderTransformOrigin = new Point(.5, .5), RenderTransform = scale };
        rules.Children.Add(new Border { Height = 2, VerticalAlignment = VerticalAlignment.Top, Background = Frozen(toneC) });
        rules.Children.Add(new Border { Height = 2, VerticalAlignment = VerticalAlignment.Bottom, Background = Frozen(toneC) });
        grid.Children.Add(rules);
        var words = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 7, 0, 8) };
        var bt = Tt(big, 22, tone, Face, FontWeights.Bold); bt.HorizontalAlignment = HorizontalAlignment.Center;
        words.Children.Add(bt);
        if (small != null) { var st = Tt(small, 11, Frozen(Tac.A(toneC, .75)), Face, FontWeights.Bold); st.HorizontalAlignment = HorizontalAlignment.Center; words.Children.Add(st); }
        // the side chevrons that frame the words: > SPLASH ONE <
        var chev = new TacSketch((dc, s) =>
        {
            double y = s.Height / 2;
            var g = new StreamGeometry();
            using (var x = g.Open())
            {
                x.BeginFigure(new Point(14, y - 6), false, false); x.LineTo(new Point(20, y), true, false); x.LineTo(new Point(14, y + 6), true, false);
                x.BeginFigure(new Point(s.Width - 14, y - 6), false, false); x.LineTo(new Point(s.Width - 20, y), true, false); x.LineTo(new Point(s.Width - 14, y + 6), true, false);
            }
            g.Freeze();
            dc.DrawGeometry(null, Tac.P(tone, 2), g);
        });
        grid.Children.Add(chev);
        grid.Children.Add(words);
        var punch = new ScaleTransform(1, 1); words.RenderTransformOrigin = new Point(.5, .5); words.RenderTransform = punch;
        return (outer, (t, hit) =>
        {
            dim.Opacity = 0;
            // the rules run out from the middle just before the hit, the band fills between them as the words land
            double k = Seg(t, hit - .1, hit - .02);
            scale.ScaleX = k * k * (3 - 2 * k);
            double fill = Seg(t, hit - .05, hit);
            double w = t < hit ? 0 : t < .82 ? 1 : 1 - Seg(t, .82, 1);
            // the words come on hard, past their size and back, with one flicker as a HUD's would
            words.Opacity = t > hit + .03 && t < hit + .05 ? .3 : w;
            punch.ScaleX = punch.ScaleY = t < hit ? 1.3 : 1 + .3 * (1 - Back(Seg(t, hit, hit + .08), 2.2));
            chev.Opacity = w;
            band.Opacity = t < .82 ? fill : 1 - Seg(t, .82, 1);
            rules.Opacity = t <= 0 || t < hit - .1 ? 0 : t < .82 ? 1 : 1 - Seg(t, .82, 1);
        });
    }

    // ================================================================ parts

    /// <summary>A title cut to its room with "…" (whole when it fits).</summary>
    static void FitTitle(TacHalo t, string full, double room)
    {
        if (room <= 0) return;
        string s = full;
        if (Tac.TextW(t.Block, full) > room)
        {
            int n = full.Length;
            while (n > 1 && Tac.TextW(t.Block, full[..n].TrimEnd() + "…") > room) n--;
            s = full[..n].TrimEnd() + "…";
        }
        t.Text = s;
    }

    static TacHalo Halo(string s, double size, Brush fg, FontWeight w) => new(Tt(s, size, fg, Face, w), InkC0, Math.Clamp(size * 0.15, 2.6, 4));

    /// <summary>"2:40" as a HUD's clock, "02:40" (an hour's "1:02:40" as it is).</summary>
    static string Pad(string clock) => clock.Length == 4 && clock[1] == ':' ? "0" + clock : clock;
    static TacHalo Label(string s, double size = 11) { var h = Halo(s, size, PhosDim, FontWeights.Bold); h.VerticalAlignment = VerticalAlignment.Center; return h; }
    static TacHalo Fig(string s, Brush fg, double size = 14) { var h = Halo(s, size, fg, FontWeights.Bold); h.Margin = new Thickness(4, 0, 0, 0); return h; }
    static TacHalo Dot() { var h = Halo("·", 12, PhosDim, FontWeights.Bold); h.Margin = new Thickness(6, 0, 6, 0); return h; }

    /// <summary>A small solid tag (STBY, CONTACT, KILLED): dark words on the tone, a dark hairline round it.</summary>
    static Border Tag(string s, Brush bg, Brush fg, double size) => new()
    {
        Background = bg, BorderBrush = Frozen(Tac.A(InkC0, .85)), BorderThickness = new Thickness(1), Padding = new Thickness(4, 0, 4, 0), VerticalAlignment = VerticalAlignment.Center,
        HorizontalAlignment = HorizontalAlignment.Left, Child = Tt(s, size, fg, Face, FontWeights.Bold),
    };
}
