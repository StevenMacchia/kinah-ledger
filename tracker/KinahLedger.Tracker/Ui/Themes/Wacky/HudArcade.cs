using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using KinahLedger.Parser;
using KinahLedger.Session;
using static KinahLedger.Ui.HudKit;

namespace KinahLedger.Ui;

/// <summary>
/// Arcade (the Wacky theme, looks pack 2, 2026-11-20): a cabinet's high-score table. Each part is a black CRT screen with
/// static scanlines in a Pac-Man-blue double frame stepped at its corners, its legend set into the top edge in red LED
/// letters as an attract screen has them: "1UP" over the Buddy (your score), "HI-SCORE" over the meter. The meter is the
/// table: the rank words 1ST to 5TH in the classic name-entry colours (gold, silver-cyan, orange, pink, green), names in
/// white, the DPS in Bahnschrift tinted the rank's colour with a soft LED bloom, and under each name an LED bar of discrete
/// cells lit to the share in the class's colour, its head cell hot. Your row wears the player-one glow: a light band, a
/// dotted selection box and a pixel cursor pointing at you. A pixel crown sits over 1ST. The Buddy: the hour as the score in
/// LED yellow, your best as a segmented LED ring, the pickups as tiles in pixel frames, the timers as square LED chips.
/// The moments: the crown drops in with a bounce when the lead changes and a light chases along the leader's cells (and
/// along any row that climbs); a fight switches the screen on as a tube does (a bright line opening out) and a chase runs
/// down the table; a kill or wipe types KILLED / GAME OVER into a banner while the cells chase in its colour; a pickup pops
/// its score up in LED digits; a rare drop flashes its frame in its grade's colour; your best passed fills the ring cell by
/// cell and flashes HIGH SCORE!; an entry opening blinks its chip. Figures never move: the light goes round them.
/// </summary>
sealed partial class HudArcade : ModernLook
{
    static readonly Color LedC = Color.FromRgb(0xFF, 0xD2, 0x3F), MintC2 = Color.FromRgb(0x5D, 0xF2, 0xA6), MarkMintC = Color.FromRgb(0x7D, 0xFF, 0xC0),
        CoralC = Color.FromRgb(0xFF, 0x6E, 0x6E), HpC = Color.FromRgb(0xFF, 0x3D, 0x5A), DeepC = Color.FromRgb(0x05, 0x06, 0x0B), GreyC = Color.FromRgb(0x6A, 0x70, 0x84),
        WarnC2 = Color.FromRgb(0xFF, 0xB0, 0x86), GoldC2 = Color.FromRgb(0xFF, 0xCF, 0x6A), LegendC = Color.FromRgb(0xFF, 0x3E, 0x3E);
    static readonly SolidColorBrush White = Frozen(Colors.White), Soft = Frozen(Mod.A(Colors.White, .84)), Quiet = Frozen(Mod.A(Colors.White, .6)),
        Mint2 = Frozen(Color.FromRgb(0x8C, 0xFF, 0xC6)), MarkMint = Frozen(MarkMintC), Coral = Frozen(CoralC), Led = Frozen(LedC);

    /// <summary>The legends' LED dot, and where the screen's top edge sits under them (half the legend's height).</summary>
    const double LegendPx = 1.5, Top = 5;

    // the room round the look: 98 at the right for the pickup's score popping up beside the Buddy
    readonly Grid _root = new() { Margin = new Thickness(8, 8, 98, 10), HorizontalAlignment = HorizontalAlignment.Left };
    readonly StackPanel _body = new() { HorizontalAlignment = HorizontalAlignment.Left };
    double _opacity = 0.9;

    // the Buddy: its screen with the 1UP legend, the ring, the hour, the session and the timer chips
    readonly Grid _line = new() { HorizontalAlignment = HorizontalAlignment.Left };
    readonly TacSketch _lineScreen;
    readonly FrameworkElement _lineLegend;
    readonly StackPanel _lineBody = new() { Orientation = Orientation.Horizontal, Margin = new Thickness(12, 17, 15, 9) };
    readonly Grid _lineFx = new() { IsHitTestVisible = false, ClipToBounds = true };
    /// <summary>The bursts of pixel sparks over each screen (clipped to it), and the alert plates that flash over its legend.</summary>
    readonly Canvas _lineSparks = new() { IsHitTestVisible = false, ClipToBounds = true }, _fightSparks = new() { IsHitTestVisible = false, ClipToBounds = true };
    readonly Border _lineAlert = AlertPlate(), _fightAlert = AlertPlate();
    /// <summary>The run's head (the dungeon, its mode, today's runs) on a plate set into the Buddy's top edge, after 1UP.</summary>
    readonly Border _runPlate = new() { HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top, Padding = new Thickness(4, 0, 4, 0), Visibility = Visibility.Collapsed, IsHitTestVisible = false };
    readonly Canvas _lineAnchor = Tac.Anchor(HorizontalAlignment.Right, VerticalAlignment.Center);
    readonly StackPanel _timers = new() { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
    readonly Border _timersInLine = new() { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 0, 0) };
    /// <summary>The timer chips over the line, when the pickup strip takes their room on it.</summary>
    readonly Border _timersUp = new() { HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(4, 0, 0, 5), Visibility = Visibility.Collapsed };
    /// <summary>The pickup strip in Arcade: 24 px tiles in pixel frames beside the score, a faint rule, a gold star.</summary>
    static readonly PickStrip.Look Strip = new(24, 4, true, Frozen(Mod.A(Colors.White, .3)), Frozen(LedC));
    bool _dim;
    TacSketch? _ringArt;
    /// <summary>The score and the session under it (dimmed under HIGH SCORE!).</summary>
    StackPanel? _lineWords;
    double _share = -1, _complete;
    /// <summary>The Buddy screen's flare: its frame lit, drawn once; a moment moves its opacity.</summary>
    readonly TacSketch _lineFlare;
    bool _beat;
    Border? _firstChip;

    // the rare drop under the Buddy, and the notice line
    readonly Grid _rare = new() { HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 5, 0, 0), Visibility = Visibility.Collapsed };
    readonly StackPanel _note = new() { Orientation = Orientation.Horizontal, Margin = new Thickness(12, 5, 0, 0), Visibility = Visibility.Collapsed };

    // the fight: its screen with the HI-SCORE legend, the head, HP, the table and the foot
    readonly Grid _fight = new() { HorizontalAlignment = HorizontalAlignment.Left, Visibility = Visibility.Collapsed, Margin = new Thickness(0, 0, 0, 7) };
    readonly TacSketch _fightScreen;
    readonly FrameworkElement _fightLegend;
    readonly StackPanel _fightBody = new() { Margin = new Thickness(BodyX + 1, 2 + 2 * Bezel + MarqueeH, BodyX + 1, 10) };
    /// <summary>The marquee's room on the meter's screen: HI-SCORE, the alerts, the STAGE CLEAR banner and its fireworks (clipped to it).</summary>
    readonly Grid _marquee = new() { Height = MarqueeH, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(Bezel, Bezel, Bezel, 0), ClipToBounds = true, IsHitTestVisible = false };
    readonly Canvas _marqueeSparks = new() { IsHitTestVisible = false };
    /// <summary>The marquee's bulbs lit, two layers (odd and even): a moment alternates their opacity, a chase of lights.</summary>
    readonly TacSketch _bulbsA = Bulbs(0), _bulbsB = Bulbs(1);
    readonly Border _timersInFight = new() { Margin = new Thickness(0, 0, 0, 7), HorizontalAlignment = HorizontalAlignment.Left };
    readonly Grid _fightFx = new() { IsHitTestVisible = false, VerticalAlignment = VerticalAlignment.Stretch };
    readonly ScaleTransform _fightOpen = new(1, 1);
    readonly TacSketch _fightFlare;
    readonly ScaleTransform _lineOn = new(0, 1);
    FrameworkElement? _head;

    /// <summary>The table's inset from the screen's sides.</summary>
    const double BodyX = 9;

    public HudArcade() : base(bare: true)
    {
        // the look's own rows (Ui/Themes/ThemeRows.cs): the high-score table's lines
        UseRows(ArcadeRow.Pitch, m => new ArcadeRow(this), 14, GapLine);
        _lineLegend = Legend(L.T("1UP"));
        _lineScreen = new TacSketch((dc, s) => Screen(dc, s, _dim ? GreyC : CabinetC));
        _lineFlare = Flare(MintC2);
        _runPlate.Background = Ink(DeepC); _runPlate.Child = _runHead;
        _line.Children.Add(_lineScreen); _line.Children.Add(_lineFlare); _line.Children.Add(_lineBody); _line.Children.Add(_lineLegend); _line.Children.Add(_runPlate); _line.Children.Add(_lineFx); _line.Children.Add(_lineSparks); _line.Children.Add(_lineAnchor); _line.Children.Add(_lineAlert);
        _fightLegend = MarqueeWord(L.T("HI-SCORE"));
        _fightScreen = new TacSketch((dc, s) => DrawScreen(dc, new Rect(0, 0, s.Width, s.Height), CabinetC, Ground, marquee: true));
        _fightFlare = new TacSketch((dc, s) => DrawFlare(dc, new Rect(0, 0, s.Width, s.Height), Color.FromRgb(0xB8, 0xC8, 0xFF), marquee: true)) { Opacity = 0, Visibility = Visibility.Hidden };
        _fightBody.RenderTransformOrigin = new Point(.5, .5);
        _fightBody.RenderTransform = _fightOpen;
        _marquee.Children.Add(_bulbsA); _marquee.Children.Add(_bulbsB); _marquee.Children.Add(_fightLegend); _marquee.Children.Add(_fightAlert); _marquee.Children.Add(_marqueeSparks);
        _fightAlert.Margin = new Thickness(0); _fightAlert.VerticalAlignment = VerticalAlignment.Stretch; _fightAlert.HorizontalAlignment = HorizontalAlignment.Stretch; _fightAlert.Padding = new Thickness(4, 0, 4, 0);
        _fightAlert.RenderTransformOrigin = new Point(.5, .5);
        _fight.Children.Add(_fightScreen); _fight.Children.Add(_fightFlare); _fight.Children.Add(_fightBody); _fight.Children.Add(_marquee); _fight.Children.Add(_fightFx); _fight.Children.Add(_fightSparks);
        _body.Children.Add(_fight); _body.Children.Add(_timersUp); _body.Children.Add(_line); _body.Children.Add(_rare); _body.Children.Add(_note);
        _root.Children.Add(_body);
        _rare.RenderTransformOrigin = new Point(0, .5); _rare.RenderTransform = _rareOpen;
        System.Windows.Documents.TextElement.SetFontFamily(_root, Face);
        TextOptions.SetTextFormattingMode(_root, TextFormattingMode.Display);
    }

    public override FrameworkElement Root => _root;
    public override Thickness Inset => _root.Margin;
    public override double Corner => 0;
    public override FrameworkElement? FightFrame => _fight.Visibility == Visibility.Visible ? _fight : null;

    public override void SetOpacity(double opacity)
    {
        // the screens' black takes the setting (never so faint the words lose their ground)
        _opacity = Math.Clamp(opacity, 0.3, 1);
        _lineScreen.InvalidateVisual(); _fightScreen.InvalidateVisual();
        _rareScreen?.InvalidateVisual();
    }

    /// <summary>The screens' black at the opacity setting: .9 at the usual 90%, never under .55.</summary>
    double Ground => Math.Clamp(.45 + .5 * _opacity, .55, .97);

    protected override string LineSig(Figures f) => base.LineSig(f) + "|" + (Rare(Values)?.Id ?? "") + "|" + (Picks(Values).FirstOrDefault()?.Id ?? "") + "|" + Dimmed(Values) + "|" + PickStrip.Sig(Values, Strip) + "|" + RunBuddy.Sig(Values);

    static bool Dimmed(IReadOnlyDictionary<string, string> v) => (v.TryGetValue("show", out var s) && s == "dim") || (v.TryGetValue("paused", out var p) && p.Length > 0);

    /// <summary>A text in Bahnschrift (the look's face), tabular digits, never cut.</summary>
    static new TextBlock Tt(string s, double size, Brush fg, FontWeight weight, FontFamily? face = null) => Mod.Text(s, size, fg, weight, face ?? Face);

    // ================================================================ the screens

    /// <summary>The tight shade under the words: 1 px down, 2 px soft.</summary>
    static DropShadowEffect Shade() => new() { Color = Colors.Black, Direction = 270, ShadowDepth = 1, BlurRadius = 2, Opacity = .9 };

    /// <summary>An LED glow round a figure (a few figures only: the score, the clock).</summary>
    static DropShadowEffect Glow(Color c, double blur = 9, double a = .6) => new() { Color = c, ShadowDepth = 0, BlurRadius = blur, Opacity = a };

    static readonly Color MarqueeInk = Color.FromRgb(0xFF, 0xF6, 0xDC), MarqueeGlow = Color.FromRgb(0xFF, 0xE0, 0x80);

    /// <summary>The Buddy's legend: dark LED letters on a small lit marquee plate set into its top edge, at the left.</summary>
    static FrameworkElement Legend(string s)
    {
        var w = Word(s, LegendPx, MarqueeInk, .35, textSize: 11);
        if (w is TextBlock tb) tb.Effect = new DropShadowEffect { Color = Color.FromRgb(0x50, 0x08, 0x08), ShadowDepth = 0, BlurRadius = 2, Opacity = 1 };
        var glass = new Grid();
        glass.Children.Add(new TacSketch((dc, sz) => { var r = new Rect(0, 0, sz.Width, sz.Height); dc.DrawRectangle(MarqueeFill, null, r); dc.DrawRectangle(MarqueeGloss, null, r); }));
        glass.Children.Add(new Border { Child = w, Padding = new Thickness(5, 2, 5, 2) });
        return new Border
        {
            Child = glass, BorderBrush = Ink(Colors.Black, .85), BorderThickness = new Thickness(1), IsHitTestVisible = false,
            HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(10, Spells(s) ? -1 : -3, 0, 0),
        };
    }

    /// <summary>A word on the marquee: dark LED letters at its left, lit by the glass behind them.</summary>
    static FrameworkElement MarqueeWord(string s)
    {
        var w = Word(s, 1.9, MarqueeInk, .4, textSize: 14);
        if (w is TextBlock tb) tb.Effect = new DropShadowEffect { Color = Color.FromRgb(0x50, 0x08, 0x08), ShadowDepth = 0, BlurRadius = 2, Opacity = 1 };
        w.HorizontalAlignment = HorizontalAlignment.Center; w.VerticalAlignment = VerticalAlignment.Center;
        return w;
    }

    /// <summary>The marquee's bulbs lit (every other one, from <paramref name="phase"/>), drawn once, hidden until a moment.</summary>
    static TacSketch Bulbs(int phase) => new((dc, s) =>
    {
        var m = new Rect(0, 0, s.Width, s.Height);
        DrawBulbs(dc, m, Ink(Colors.White), phase, 3);
        dc.DrawRectangle(Ink(Colors.White, .12), null, m);
    }) { Opacity = 0, Visibility = Visibility.Hidden };

    /// <summary>A chase of the marquee's lights: the two layers of bulbs alternate in steps for <paramref name="ms"/>, after <paramref name="delay"/>.</summary>
    void MarqueeChase(double delay, double ms)
    {
        double total = delay + ms;
        Motion.Play(_bulbsA, total, p =>
        {
            double l = (p * total - delay) / ms;
            if (l < 0 || l >= 1) { Fade(_bulbsA, 0); Fade(_bulbsB, 0); return; }
            bool a = ((int)(l * 10)) % 2 == 0;
            Fade(_bulbsA, a ? 1 : 0); Fade(_bulbsB, a ? 0 : 1);
        }, () => { Fade(_bulbsA, 0); Fade(_bulbsB, 0); });
    }

    /// <summary>A moment's alert plate over the legend ("NEW LEADER!", "1UP!", "HIGH SCORE!"): hidden until it flashes.</summary>
    static Border AlertPlate() => new()
    {
        Background = Ink(DeepC), BorderThickness = new Thickness(1.5), Padding = new Thickness(4, 1, 4, 1), IsHitTestVisible = false, Visibility = Visibility.Hidden, Opacity = 0,
        HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(8, -2, 0, 0), RenderTransformOrigin = new Point(0, .5),
    };

    /// <summary>The screen: its top edge at <see cref="Top"/> (the legend's plate sits across it).</summary>
    void Screen(DrawingContext dc, Size s, Color frame) => DrawScreen(dc, new Rect(0, Top, s.Width, s.Height - Top), frame, Ground);

    /// <summary>A screen's flare layer: its frame lit in <paramref name="c"/>, drawn once, hidden until a moment moves its opacity.</summary>
    static TacSketch Flare(Color c) => new((dc, s) => DrawFlare(dc, new Rect(0, Top, s.Width, s.Height - Top), c)) { Opacity = 0, Visibility = Visibility.Hidden };

    /// <summary>An effect layer at <paramref name="a"/>: hidden at none, so an idle layer costs nothing to draw.</summary>
    static void Fade(UIElement el, double a)
    {
        el.Opacity = Math.Clamp(a, 0, 1);
        var v = a > .004 ? Visibility.Visible : Visibility.Hidden;
        if (el.Visibility != v) el.Visibility = v;
    }

    /// <summary>A square chip (TOP DPS, the heal mark, AVG, READY): dark with a 1 px edge; <paramref name="filled"/> white with black words (YOU).</summary>
    static Border Mark(string s, Brush? fg = null, Color? edge = null, bool filled = false, double size = 8.5)
    {
        var t = Tt(s, size, filled ? Frozen(Color.FromRgb(0x08, 0x08, 0x0C)) : fg ?? White, FontWeights.Bold);
        return new Border
        {
            Child = t, CornerRadius = new CornerRadius(0), Padding = new Thickness(3, 0, 3, 1), VerticalAlignment = VerticalAlignment.Center,
            Background = filled ? White : Frozen(Mod.A(DeepC, .8)), BorderThickness = new Thickness(1),
            BorderBrush = filled ? White : Frozen(Mod.A(edge ?? Colors.White, edge == null ? .45 : .85)),
        };
    }

    /// <summary>A dotted pixel rule across (the foot's, the card's): 2 px dots every 4 px.</summary>
    static FrameworkElement Dots(Thickness m, Color c, double a = .32) => new TacSketch((dc, s) =>
    {
        var b = Frozen(Mod.A(c, a));
        for (double x = 0; x + 2 <= s.Width; x += 4) dc.DrawRectangle(b, null, new Rect(x, 0, 2, 2));
    }) { Height = 2, Margin = m };

    // ================================================================ the Buddy

    protected override void DrawLine(Figures f)
    {
        _lineBody.Children.Clear();
        _ringArt = null; _lineWords = null; _timerUp = false; _stripTimerW = -1;
        if (_timersInLine.Child != null) _timersInLine.Child = null;
        bool dim = Dimmed(Values);
        if (dim != _dim) { _dim = dim; _lineScreen.InvalidateVisual(); }
        _line.Opacity = _dim ? .66 : 1;
        bool mark = f.Abyss && f.Counted && f.Icon.Length > 0;
        if (mark) { var m = AbyssMark(f.Icon, 36); m.Margin = new Thickness(0, 0, 9, 0); _lineBody.Children.Add(m); }
        // in a dungeon run: the run's clock, splits and verdict in place of the hour (HudArcade.Run.cs)
        var rs = RunNow();
        _runPlate.Visibility = Visibility.Collapsed;
        if (rs != null) DrawRun(rs);
        else if (!HasHour(f))
        {
            var w = Tt(Waiting(f.Abyss), 12.5, Soft, FontWeights.SemiBold); w.Margin = new Thickness(0, 5, 0, 5);
            _lineBody.Children.Add(w);
        }
        else
        {
            if (HasRing(f))
            {
                _share = f.Share; _beat = f.Beat; _complete = 0;
                var ring = new Grid { Width = 42, Height = 42, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0) };
                _ringArt = new TacSketch(DrawRing, 42, 42);
                ring.Children.Add(_ringArt);
                var pc = Tt(f.Beat ? "★" : Pct(f.Share), 13, f.Beat ? Frozen(Mod.Mix(MintC2, Colors.White, .4)) : White, FontWeights.Bold); pc.HorizontalAlignment = HorizontalAlignment.Center;
                ring.Children.Add(pc);
                _lineBody.Children.Add(ring);
            }
            // the hour as the score: LED yellow with its glow, the unit small; the session and the latest pickup under it
            var words = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 0, 1) };
            _lineWords = words;
            var top = new StackPanel { Orientation = Orientation.Horizontal };
            FrameworkElement rate = f.Warm ? Tt(f.Rate, 15, Soft, FontWeights.SemiBold) : Fig(f.Rate, 3, LedC, .45, textSize: 23);
            rate.Tag = "buddy-rate";
            top.Children.Add(rate);
            if (f.Unit.Length > 0) { var u = Tt(f.Unit, 11, Soft, FontWeights.SemiBold); u.VerticalAlignment = VerticalAlignment.Bottom; u.Margin = new Thickness(4, 0, 0, 0); top.Children.Add(u); }
            words.Children.Add(top);
            // farming, the pickups as the strip beside the score where it fits (the best drop and the last three), the session
            // alone under it; with the timer chips at the line's end and no room left beside them, the chips go over the line
            var sub = Sub(f, true);
            FrameworkElement? strip = null, under = null;
            if (StripTime(f))
            {
                var bare = Sub(f, false);
                double baseW = _lineBody.Margin.Left + _lineBody.Margin.Right + _lineBody.Children.OfType<FrameworkElement>().Sum(Mod.W), timerW = LineTimerW();
                double wordsW = Math.Max(Mod.W(top), Mod.W(bare)), oldW = Math.Max(Mod.W(top), Mod.W(sub));
                foreach (var look in StripFall(Strip))
                {
                    if ((strip = StripIn(StripRoom(FightWidth - 10, baseW + timerW, wordsW, oldW, 12), look)) != null) break;
                    if (timerW > 0 && (strip = StripIn(StripRoom(FightWidth - 10, baseW, wordsW, oldW + timerW, 12), look)) != null) { _timerUp = true; break; }
                }
                if (strip != null) { sub = bare; strip.Margin = new Thickness(12, 0, 0, 0); Pixelate(strip); }
                // no room beside the score: under the session, never dropped (Steven, 2026-11-19)
                else if ((under = StripUnder(Strip, false)) != null) { sub = bare; Pixelate(under); }
                _stripTimerW = timerW;
            }
            if (sub.Inlines.Count > 0) words.Children.Add(sub);
            if (under != null) words.Children.Add(under);
            _lineBody.Children.Add(words);
            if (strip != null) _lineBody.Children.Add(strip);
        }
        _lineBody.Children.Add(_timersInLine);
        PlaceTimers();

        // the rare drop the window holds: a screen framed in its grade's colour, the tile, RARE DROP in LED letters and its name
        _rare.Children.Clear();
        if (!KinahHidden && Rare(Values) is { } r)
        {
            var c = ColorOf(r.Color, r.Grade);
            var scr = new TacSketch((dc, s) => DrawScreen(dc, new Rect(0, 0, s.Width, s.Height), Mod.Mix(c, Colors.White, .1), Ground));
            var flare = new TacSketch((dc, s) => DrawFlare(dc, new Rect(0, 0, s.Width, s.Height), c)) { Opacity = 0, Visibility = Visibility.Hidden };
            _rareFlare = flare;
            _rareScreen = scr;
            _rare.Children.Add(scr); _rare.Children.Add(flare);
            var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(9, 8, 14, 8) };
            var tile = Tile(r, 26); tile.Margin = new Thickness(0, 0, 9, 0);
            Pixelate(tile);
            _rareTile = tile;
            row.Children.Add(tile);
            var rd = Word(L.T("RARE DROP") + "!", 1.5, Color.FromRgb(0x20, 0x10, 0x00), 0, Ink(Color.FromRgb(0x1A, 0x0C, 0x00)), 11);
            var mk = new Border { Child = rd, Background = MarqueeFill, BorderBrush = Ink(Color.FromRgb(0xFF, 0xF0, 0xB0)), BorderThickness = new Thickness(1), Padding = new Thickness(5, 3, 5, 3), Margin = new Thickness(0, 0, 9, 0), VerticalAlignment = VerticalAlignment.Center };
            _rareWord = mk;
            row.Children.Add(mk);
            var nm = Tt(r.Name, 13, White, FontWeights.SemiBold, HudKit.Text);
            row.Children.Add(nm);
            if (r.Count.Length > 0) { var cn = Tt(" " + r.Count, 11, Soft, FontWeights.SemiBold); cn.Margin = new Thickness(2, 1, 0, 0); row.Children.Add(cn); }
            _rare.Children.Add(row);
            _rare.Visibility = Visibility.Visible;
        }
        else { _rare.Visibility = Visibility.Collapsed; _rareScreen = null; _rareFlare = null; _rareTile = null; _rareWord = null; }

        // the notice: its words in their tone under the Buddy, the warning sign before a pause
        _note.Children.Clear();
        if (f.Note.Length > 0 && !KinahHidden)
        {
            bool warn = ReferenceEquals(f.NoteTone, Warn);
            if (warn) { var wi = WarnIcon(); wi.Width = wi.Height = 13; wi.Margin = new Thickness(0, 0, 5, 0); _note.Children.Add(wi); }
            var tone = warn ? WarnC2 : ReferenceEquals(f.NoteTone, Gold) ? GoldC2 : ReferenceEquals(f.NoteTone, Mint) ? MintC2 : Color.FromRgb(0xF2, 0xF4, 0xF8);
            var t = Tt(f.Note, 11.5, Frozen(tone), FontWeights.SemiBold); t.TextTrimming = TextTrimming.CharacterEllipsis; t.MaxWidth = 380;
            t.Effect = new DropShadowEffect { Color = Colors.Black, ShadowDepth = 0, BlurRadius = 6, Opacity = .95 };
            _note.Children.Add(t);
            _note.Visibility = Visibility.Visible;
        }
        else _note.Visibility = Visibility.Collapsed;
    }

    /// <summary>The line under the score: the session, the latest pickup and its worth (<paramref name="pick"/>); in the Abyss its stats.</summary>
    TextBlock Sub(Figures f, bool pick)
    {
        var sub = new TextBlock { FontFamily = Face, FontSize = 11, FontWeight = FontWeights.Medium, Foreground = Frozen(Mod.A(Colors.White, .9)), Margin = new Thickness(1, 3, 0, 0) };
        System.Windows.Documents.Typography.SetNumeralAlignment(sub, FontNumeralAlignment.Tabular);
        void Dot() { if (sub.Inlines.Count > 0) sub.Inlines.Add(new System.Windows.Documents.Run("  ▪  ") { Foreground = Frozen(Mod.A(MazeHiC, .8)), FontSize = 8 }); }
        if (f.Total.Length > 0) { sub.Inlines.Add(new System.Windows.Documents.Run(f.Total) { FontWeight = FontWeights.Bold, Foreground = White }); sub.Inlines.Add(new System.Windows.Documents.Run(" " + f.TotalWord)); }
        foreach (var (fig, word, tone) in f.Stats)
        {
            Dot();
            var c = ReferenceEquals(tone, Rec) ? CoralC : ReferenceEquals(tone, Gold) ? GoldC2 : Colors.White;
            sub.Inlines.Add(new System.Windows.Documents.Run(fig) { FontWeight = FontWeights.Bold, Foreground = Frozen(c) });
            sub.Inlines.Add(new System.Windows.Documents.Run(" " + word));
        }
        if (pick && !f.Abyss && Picks(Values).FirstOrDefault() is { } p)
        {
            Dot();
            sub.Inlines.Add(new System.Windows.Documents.Run(p.Name.Length > 22 ? p.Name[..21].TrimEnd() + "…" : p.Name));
            var worth = p.Worth.Length > 0 ? "+" + p.Worth.TrimStart('+') : p.Count;
            if (worth.Length > 0) sub.Inlines.Add(new System.Windows.Documents.Run(" " + worth) { FontWeight = FontWeights.Bold, Foreground = p.Worth.Length > 0 ? Mint2 : Soft });
        }
        return sub;
    }

    /// <summary>The timer chips' room at the line's end, 0 with none.</summary>
    double LineTimerW() => _timers.Children.Count > 0 && !KinahHidden ? 10 + Mod.W(_timers) : 0;

    /// <summary>The timers' room the line's strip was fitted to (-1: no strip time); chips that change it draw the line again.</summary>
    double _stripTimerW = -1;

    /// <summary>The timer chips sit over the line, to leave its room to the pickup strip.</summary>
    bool _timerUp;

    TacSketch? _rareScreen, _rareFlare;
    readonly ScaleTransform _rareOpen = new(1, 1);
    FrameworkElement? _rareTile, _rareWord;

    /// <summary>
    /// The run's ring as an LED ring: twenty cells round a dark track, lit clockwise from the top to your best's share in LED
    /// yellow (mint once beaten), each lit cell with a faint bloom.
    /// </summary>
    void DrawRing(DrawingContext dc, Size s)
    {
        const int n = 20;
        double c = s.Width / 2, r = c - 4;
        var center = new Point(c, c);
        dc.DrawEllipse(Frozen(Mod.A(Colors.Black, .45)), null, center, r + 3, r + 3);
        double share = Math.Clamp(_beat ? 1 : _share, 0, 1);
        double shown = share + (1 - share) * Math.Clamp(_complete, 0, 1);
        var tone = _beat || _complete >= 1 ? MintC2 : LedC;
        double lit = shown * n;
        var dimPen = Mod.P(Frozen(Mod.A(Colors.White, .13)), 4);
        var glowPen = Mod.P(Frozen(Mod.A(tone, .28)), 7.5);
        var litPen = Mod.P(Frozen(tone), 4);
        var hotPen = Mod.P(Frozen(Mod.Mix(tone, Colors.White, .55)), 4);
        for (int i = 0; i < n; i++)
        {
            double a0 = 2 * Math.PI * (i + .14) / n, a1 = 2 * Math.PI * (i + .86) / n;
            var fig = new PathFigure { StartPoint = new Point(c + r * Math.Sin(a0), c - r * Math.Cos(a0)) };
            fig.Segments.Add(new ArcSegment(new Point(c + r * Math.Sin(a1), c - r * Math.Cos(a1)), new Size(r, r), 0, false, SweepDirection.Clockwise, true));
            var pg = new PathGeometry(); pg.Figures.Add(fig); pg.Freeze();
            if (i + 1 <= lit)
            {
                dc.DrawGeometry(null, glowPen, pg);
                dc.DrawGeometry(null, i + 1 > lit - 1 && shown < .999 ? hotPen : litPen, pg);
            }
            else if (i < lit)
            {
                dc.DrawGeometry(null, dimPen, pg);
                dc.DrawGeometry(null, Mod.P(Frozen(Mod.A(tone, lit - i)), 4), pg);
            }
            else dc.DrawGeometry(null, dimPen, pg);
        }
    }

    protected override void KinahShown(bool shown)
    {
        _line.Visibility = shown ? Visibility.Visible : Visibility.Collapsed;
        if (!shown) { _note.Visibility = Visibility.Collapsed; _rare.Visibility = Visibility.Collapsed; }
        _fight.Margin = new Thickness(0, 0, 0, shown ? 7 : 0);
        PlaceTimers();
    }

    // ================================================================ the timers

    protected override void DrawTimers(IReadOnlyList<TimerBoard.Item> due, DateTime now)
    {
        _timers.Children.Clear();
        _firstChip = null;
        foreach (var i in due.Take(2))
        {
            var (text, tone) = TimerWords(i, now);
            bool first = _timers.Children.Count == 0;
            var c = tone == TimerBadge.Tone.On ? MintC2 : tone == TimerBadge.Tone.Soon ? GoldC2 : Color.FromRgb(0xC8, 0xD0, 0xFF);
            var sp = new StackPanel { Orientation = Orientation.Horizontal };
            // the chip's LED: a square lit in its tone
            double d = first ? 6 : 5;
            sp.Children.Add(new Border { Width = d, Height = d, Background = Frozen(c), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0), BorderBrush = Frozen(Mod.A(Colors.Black, .8)), BorderThickness = new Thickness(.5) });
            var t = Tt(text, first ? 11.5 : 10, first ? White : Soft, first ? FontWeights.Bold : FontWeights.SemiBold);
            if (tone == TimerBadge.Tone.On) t.Foreground = Frozen(Mod.Mix(MintC2, Colors.White, .45));
            sp.Children.Add(t);
            var chip = new Border
            {
                Child = sp, Padding = first ? new Thickness(7, 3, 9, 3) : new Thickness(6, 2, 8, 2), VerticalAlignment = VerticalAlignment.Center,
                Background = Frozen(Mod.A(DeepC, .9)), BorderThickness = new Thickness(1.5), BorderBrush = Frozen(Mod.A(tone == TimerBadge.Tone.On ? MintC2 : MazeC, tone == TimerBadge.Tone.On ? .85 : .9)),
                Margin = new Thickness(first ? 0 : 5, 0, 0, 0), Tag = first ? "timer-chip" : null,
            };
            if (first) _firstChip = chip;
            _timers.Children.Add(chip);
        }
        // the line's pickup strip was fitted beside the timer chips: chips that come, go or change their width fit it again
        if (_stripTimerW >= 0 && Figs is { } f && Math.Abs(LineTimerW() - _stripTimerW) > .5) DrawLine(f);
        PlaceTimers();
    }

    void PlaceTimers()
    {
        bool inside = KinahHidden && _fight.Visibility == Visibility.Visible, up = !inside && _timerUp;
        if (inside)
        {
            if (_timersInLine.Child == _timers) _timersInLine.Child = null;
            if (_timersUp.Child == _timers) _timersUp.Child = null;
            if (_timersInFight.Child != _timers) _timersInFight.Child = _timers;
        }
        else if (up)
        {
            if (_timersInLine.Child == _timers) _timersInLine.Child = null;
            if (_timersInFight.Child == _timers) _timersInFight.Child = null;
            if (_timersUp.Child != _timers) _timersUp.Child = _timers;
        }
        else
        {
            if (_timersInFight.Child == _timers) _timersInFight.Child = null;
            if (_timersUp.Child == _timers) _timersUp.Child = null;
            if (_timersInLine.Child != _timers && _timersInLine.Parent != null) _timersInLine.Child = _timers;
        }
        bool any = _timers.Children.Count > 0;
        _timersInFight.Visibility = inside && any ? Visibility.Visible : Visibility.Collapsed;
        _timersUp.Visibility = up && any ? Visibility.Visible : Visibility.Collapsed;
        _timersInLine.Visibility = !inside && !up && any ? Visibility.Visible : Visibility.Collapsed;
    }

    // ================================================================ the fight

    protected override void DrawFight(Encounter? e, DateTime now)
    {
        _fightBody.Children.Clear();
        if (e == null) { _fight.Visibility = Visibility.Collapsed; _fightFx.Children.Clear(); PlaceTimers(); return; }
        double w = FightWidth - 10, inner = w - 2 * BodyX;
        _fight.Width = w;
        _fightBody.Children.Add(_timersInFight);
        // the head: the boss, the outcome once it's over, the clock
        var head = new Grid();
        head.ColumnDefinitions.Add(new ColumnDefinition()); head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var title = Tt(e.Title, 14, White, FontWeights.Bold); title.TextTrimming = TextTrimming.CharacterEllipsis;
        head.Children.Add(title);
        var right = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(8, 0, 0, 0) };
        if (e.Waiting) right.Children.Add(Mark(L.T("READY"), MarkMint, MarkMintC, size: 9.5));
        else
        {
            var outcome = OutcomeText(e);
            if (outcome.Length > 0)
            {
                bool wipe = e.Outcome == "wipe";
                var oc = wipe ? CoralC : MarkMintC;
                var st = new Border { Child = Word(outcome.ToUpperInvariant(), 1.3, oc, .45, textSize: 10), BorderBrush = Ink(oc, .85), BorderThickness = new Thickness(1), Background = Ink(DeepC, .8),
                    Padding = new Thickness(4, 2, 4, 2), Margin = new Thickness(0, 0, 9, 0), VerticalAlignment = VerticalAlignment.Center, Tag = "outcome" };
                right.Children.Add(st);
            }
            // the clock in LED digits (a scoreboard's): white with a blue phosphor glow, no effect needed
            var clock = Fig(Clock(e, now), 2.2, Color.FromRgb(0x9F, 0xB4, 0xFF), .45, White, 18);
            clock.Tag = "fight-clock";
            right.Children.Add(clock);
        }
        Grid.SetColumn(right, 1); head.Children.Add(right);
        _head = head;
        _fightBody.Children.Add(head);
        if (e.Waiting)
        {
            var wt = Tt(e.ReadyText, 12.5, Soft, FontWeights.SemiBold); wt.Margin = new Thickness(0, 5, 0, 1); wt.TextTrimming = TextTrimming.CharacterEllipsis;
            _fightBody.Children.Add(wt);
            if (PartyBeforeHit(e)) { _fightBody.Children.Add(new Border { Height = 5 }); PutRows(_fightBody, e, now, inner); }
        }
        else
        {
            if (HpOf(e) is { } hp)
            {
                // the boss's HP: an LED bar of small red cells, a white pip over and under it at each third, the figures at its right
                var hpLine = new Grid { Margin = new Thickness(0, 6, 0, 0) };
                hpLine.ColumnDefinitions.Add(new ColumnDefinition()); hpLine.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                var track = new Grid { Height = 8, VerticalAlignment = VerticalAlignment.Center };
                track.Children.Add(new TacSketch((dc, s) =>
                {
                    DrawLed(dc, new Rect(0, 1.5, s.Width, 5), hp, HpC, 3, 1, .12);
                    for (int k = 1; k <= 2; k++)
                    {
                        double x = Math.Round(s.Width * k / 3) - 1;
                        dc.DrawRectangle(White, null, new Rect(x, 0, 2, 1));
                        dc.DrawRectangle(White, null, new Rect(x, 7, 2, 1));
                    }
                }));
                HpGhost(track, e, hp, now, 0);
                hpLine.Children.Add(track);
                var hl = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0) };
                var pct = Fig($"{Math.Round(hp * 100):0}%", 1.4, hp < .34 ? Color.FromRgb(0xFF, 0x8A, 0x9A) : Color.FromRgb(0xFF, 0x5A, 0x72), .4, textSize: 10.5);
                hl.Children.Add(pct);
                var left = HpLeftText(e);
                if (left.Length > 0)
                {
                    var lt = new TextBlock { FontFamily = Face, FontSize = 10.5, Foreground = Soft, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(7, 0, 0, 0), FontWeight = FontWeights.Medium, Text = left };
                    System.Windows.Documents.Typography.SetNumeralAlignment(lt, FontNumeralAlignment.Tabular);
                    lt.Tag = "hp-text";
                    hl.Children.Add(lt);
                }
                Grid.SetColumn(hl, 1); hpLine.Children.Add(hl);
                _fightBody.Children.Add(hpLine);
                _fightBody.Children.Add(new Border { Height = 5 });
            }
            else _fightBody.Children.Add(new Border { Height = 6 });
            PutRows(_fightBody, e, now, inner);
            if (e.HasDamage)
            {
                _fightBody.Children.Add(Dots(new Thickness(0, 6, 0, 5), MazeHiC, .55));
                _fightBody.Children.Add(Foot(e, now));
            }
        }
        _fight.Visibility = Visibility.Visible;
        PlaceTimers();
    }

    // ================================================================ the moments

    /// <summary>A pickup: its worth pops up beside the Buddy in LED digits as a score does over a hit, the screen's frame flares.</summary>
    protected override void Pickup(Pick p, bool held)
    {
        if (KinahHidden || _line.Visibility != Visibility.Visible) return;
        var worth = p.Worth.Length > 0 ? "+" + p.Worth.TrimStart('+') : p.Count;
        var tone = p.Worth.Length > 0 ? MintC2 : Colors.White;
        var words = new StackPanel { Margin = new Thickness(7, 4, 8, 4) };
        var fig = Word(worth, 2, Mod.Mix(tone, Colors.White, .2), .45, textSize: 15);
        fig.HorizontalAlignment = HorizontalAlignment.Left;
        words.Children.Add(fig);
        var nm = Tt(p.Name.Length > 13 ? p.Name[..12].TrimEnd() + "…" : p.Name, 9.5, Soft, FontWeights.SemiBold); nm.Margin = new Thickness(0, 3, 0, 0);
        words.Children.Add(nm);
        var chip = new Border { Child = words, MaxWidth = 88, Background = Ink(DeepC, .9), BorderThickness = new Thickness(1.5), BorderBrush = Ink(MazeC, .95) };
        var shift = new TranslateTransform();
        chip.RenderTransform = shift;
        chip.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        Canvas.SetLeft(chip, 6); Canvas.SetTop(chip, -chip.DesiredSize.Height / 2);
        _lineAnchor.Children.Clear(); _lineAnchor.Children.Add(chip);
        Motion.Play(chip, 1900, t =>
        {
            // the frame flares as the score lands; the popup pops up and rises a few pixels in whole steps, then goes
            Fade(_lineFlare, t < .12 ? Seg(t, 0, .12) : 1 - Seg(t, .12, .45));
            double k = Seg(t, 0, .12);
            shift.Y = 6 * (1 - Back(k, 2.4)) - Math.Floor(Seg(t, .45, 1) * 4) * 2;
            chip.Opacity = t < .82 ? Math.Min(1, k * 2) : 1 - Seg(t, .82, 1);
        }, () => { Fade(_lineFlare, 0); _lineAnchor.Children.Remove(chip); });
    }

    /// <summary>A rare drop: its screen opens out from the left, its frame flashes three times in the grade's colour, the tile pops in.</summary>
    protected override void RareDrop(Pick p, bool held)
    {
        if (_rareScreen is not { } scr || _rareFlare is not { } flare) return;
        var tile = _rareTile;
        var sc = new ScaleTransform(1, 1);
        if (tile != null) { tile.RenderTransformOrigin = new Point(.5, .5); tile.RenderTransform = sc; }
        var word = _rareWord;
        Motion.Play(scr, 1600, t =>
        {
            _rareOpen.ScaleX = .04 + .96 * Out(Seg(t, 0, .25));
            // three flashes, stepped (an LED blinking), fading over the course
            double b = Seg(t, .2, .9);
            Fade(flare, b > 0 && b < 1 && ((int)(b * 6)) % 2 == 0 ? 1 - b * .6 : 0);
            sc.ScaleX = sc.ScaleY = .6 + .4 * Back(Seg(t, .08, .38), 2.4);
            if (word != null) word.Opacity = t < .2 ? Seg(t, .1, .2) : 1;
        }, () => { _rareOpen.ScaleX = 1; Fade(flare, 0); sc.ScaleX = sc.ScaleY = 1; if (word != null) word.Opacity = 1; });
    }

    /// <summary>
    /// Your best passed: the ring's cells fill on one by one to full in mint, a burst of pixel sparks flies off it, and HIGH
    /// SCORE! flashes three times on a plate over the 1UP legend (never over the score). The ring redraws only when a cell lights.
    /// </summary>
    protected override void BestPassed(bool held)
    {
        if (_ringArt is not { } art) return;
        double was = _share is > 0 and < 1 ? _share : .86;
        bool beat = _beat;
        double cx = _lineBody.Margin.Left + 21, cy = _lineBody.Margin.Top + (Math.Max(42, _lineBody.ActualHeight) / 2);
        Burst(_lineSparks, new Point(cx, cy), new[] { MintC2, LedC, Colors.White }, 30, 380, 900);
        Alert(_lineAlert, _lineLegend, L.T("HIGH SCORE!"), LedC, 300);
        double shown = -1;
        Motion.Play(art, 1600, t =>
        {
            double step = t >= .4 ? 1 : Math.Floor(Out(Seg(t, 0, .4)) * 20) / 20;
            if (step != shown)
            {
                shown = step;
                _beat = t >= .4 && beat; _share = was; _complete = step;
                art.InvalidateVisual();
            }
            Fade(_lineFlare, t > .35 && t < .8 && ((int)(Seg(t, .35, .8) * 6)) % 2 == 0 ? .9 : 0);
        }, () => { _complete = 0; _beat = beat; Fade(_lineFlare, 0); art.InvalidateVisual(); });
    }

    /// <summary>
    /// An alert over a screen's legend: <paramref name="word"/> in big LED letters on a plate edged in <paramref name="tone"/>,
    /// slammed in (a scale) and flashed three times (its opacity, stepped), after <paramref name="delay"/> ms.
    /// </summary>
    void Alert(Border plate, FrameworkElement legend, string word, Color tone, double delay)
    {
        var fill = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(0, 1) };
        fill.GradientStops.Add(new GradientStop(Mod.Mix(tone, Colors.White, .8), 0)); fill.GradientStops.Add(new GradientStop(tone, 1)); fill.Freeze();
        bool big = plate == _fightAlert;
        var child = Word(word, big ? 2 : 1.3, tone, .55, fill, big ? 15 : 11);
        child.HorizontalAlignment = HorizontalAlignment.Center; child.VerticalAlignment = VerticalAlignment.Center;
        plate.Child = child;
        plate.MinWidth = Mod.W(legend) + 8;
        if (big) MarqueeChase(delay, 1000);
        plate.BorderBrush = Ink(tone);
        var slam = new ScaleTransform(1, 1);
        plate.RenderTransform = slam;
        const double Run = 1300;
        double total = delay + Run;
        Motion.Play(plate, total, p =>
        {
            double l = (p * total - delay) / Run;
            if (l < 0 || l >= 1) { Fade(plate, 0); if (big) legend.Opacity = 1; return; }
            if (big) legend.Opacity = 0;   // the marquee's own word steps aside while an alert holds it
            slam.ScaleX = slam.ScaleY = 1.35 - .35 * Out(Seg(l, 0, .12));
            // three flashes, on and off in steps, then held a beat and gone
            bool on = l < .3 || (l >= .36 && l < .56) || (l >= .62 && l < .94);
            Fade(plate, on ? 1 : .12);
        }, () => { Fade(plate, 0); if (big) legend.Opacity = 1; });
    }

    /// <summary>
    /// A burst of pixel sparks at <paramref name="at"/> on <paramref name="host"/>: two rings of square sparks in
    /// <paramref name="tones"/> drawn once; the burst flies out (its scale), falls a little (its shift) and fades (its opacity).
    /// </summary>
    static void Burst(Canvas host, Point at, Color[] tones, double radius, double delay, double ms)
    {
        double size = 2 * radius + 8;
        var art = new TacSketch((dc, s) =>
        {
            double c = s.Width / 2;
            for (int i = 0; i < 12; i++)
            {
                double a = 2 * Math.PI * i / 12, r = radius;
                dc.DrawRectangle(Ink(tones[i % tones.Length]), null, new Rect(Math.Round(c + r * Math.Cos(a)) - 1.5, Math.Round(c + r * Math.Sin(a)) - 1.5, 3, 3));
                double a2 = a + Math.PI / 12, r2 = radius * .6;
                dc.DrawRectangle(Ink(tones[(i + 1) % tones.Length], .9), null, new Rect(Math.Round(c + r2 * Math.Cos(a2)) - 1, Math.Round(c + r2 * Math.Sin(a2)) - 1, 2, 2));
            }
            var w = Ink(Colors.White);
            dc.DrawRectangle(w, null, new Rect(c - 4, c - 1, 8, 2)); dc.DrawRectangle(w, null, new Rect(c - 1, c - 4, 2, 8));
        }, size, size) { RenderTransformOrigin = new Point(.5, .5), Visibility = Visibility.Hidden, Opacity = 0 };
        var scale = new ScaleTransform(.2, .2); var fall = new TranslateTransform();
        art.RenderTransform = new TransformGroup { Children = { scale, fall } };
        Canvas.SetLeft(art, at.X - size / 2); Canvas.SetTop(art, at.Y - size / 2);
        host.Children.Add(art);
        double total = delay + ms;
        Motion.Play(art, total, p =>
        {
            double l = (p * total - delay) / ms;
            if (l < 0) { Fade(art, 0); return; }
            scale.ScaleX = scale.ScaleY = .2 + .9 * Out(Math.Min(1, l / .7));
            fall.Y = 4 * l * l;
            Fade(art, l < .55 ? 1 : 1 - Seg(l, .55, 1));
        }, () => host.Children.Remove(art));
    }

    /// <summary>
    /// A new leader (a row climbed to 1ST): a burst of pixel sparks off the crown and, on a plate over HI-SCORE, NEW LEADER!
    /// flashing three times (1UP! when the new leader is you).
    /// </summary>
    void NewLeader(ArcadeRow row, bool you, double delay = 0, string? word = null)
    {
        double w = _fight.Width > 0 ? _fight.Width - 2 * Bezel : FightWidth - 10 - 2 * Bezel;
        double x = row.CrownAt(_fight) is { } at ? at.X - Bezel : 30;
        Color pink = Color.FromRgb(0xFF, 0x66, 0xCC);
        Burst(_marqueeSparks, new Point(Math.Clamp(x, 12, w - 12), MarqueeH / 2), new[] { LedC, pink, Colors.White }, 10, delay + 250, 700);
        Burst(_marqueeSparks, new Point(w - 18, MarqueeH / 2), new[] { pink, LedC, Colors.White }, 9, delay + 420, 650);
        Alert(_fightAlert, _fightLegend, word ?? L.T("NEW LEADER!"), LedC, delay + 150);
    }

    static readonly SolidColorBrush EntryOn = Frozen(Mod.A(MintC2, .32)), EntryEdge = Frozen(MintC2);

    /// <summary>An entry opened: the first timer's chip blinks mint three times, as a cabinet's start button does, and settles.</summary>
    protected override void EntryOpened(TimerBoard.Item i, bool held)
    {
        if (_firstChip is not { } chip) return;
        var glow = new DropShadowEffect { Color = MintC2, ShadowDepth = 0, BlurRadius = 10, Opacity = 0 };
        chip.Effect = glow;
        var edge = chip.BorderBrush; var bg = chip.Background;
        bool? was = null;
        Motion.Play(chip, 1300, t =>
        {
            bool lit = t < .75 && ((int)(t / .125)) % 2 == 0;
            glow.Opacity = lit ? .9 : .15 * (1 - t);
            // the brushes change only on a blink, never each frame
            if (was == lit) return;
            was = lit;
            chip.BorderBrush = lit ? EntryEdge : edge;
            chip.Background = lit ? EntryOn : bg;
        }, () => { chip.Effect = null; chip.BorderBrush = edge; chip.Background = bg; });
    }

    /// <summary>
    /// A fight started: the screen switches on as a tube does (a bright line across its middle opens out to the table), then a
    /// chase runs along every row's cells down the table, and the crown drops onto 1ST.
    /// </summary>
    protected override void FightStarted(Encounter e, bool held)
    {
        Motion.Play(_fightFx, 700, t => Fade(_fightFlare, t < .2 ? Seg(t, 0, .1) : 1 - Seg(t, .2, 1)), () => Fade(_fightFlare, 0));
        if (Meter is { } m && m.Members.FirstOrDefault(x => x.Rank == 1) is { } top && m.RowOf(top.Id) is ArcadeRow lead)
        {
            lead.CrownDrop(120);
            NewLeader(lead, top.You, 0, L.T("READY?") + " " + L.T("FIGHT!"));
        }
    }

    /// <summary>A chase down the table: each row's cells after the row above (<paramref name="delay"/> ms before the first); the crown drops on 1ST.</summary>
    void ChaseRowsUnused(double delay, Color c, bool crown)
    {
        if (Meter is not { } m) return;
        int i = 0;
        foreach (var mm in m.Members)
        {
            if (m.RowOf(mm.Id) is not ArcadeRow r) continue;
            r.Chase(delay + 90 * i++, c);
            if (crown && mm.Rank == 1) r.CrownDrop(delay);
        }
    }

    /// <summary>
    /// A fight ended: a banner drops over the fight's head and types the outcome in big LED letters (KILLED, CLEARED, GAME OVER
    /// on a wipe) with the clock beside it, while a chase in its colour runs down the table; then it lifts.
    /// </summary>
    protected override void FightEnded(Encounter e, bool held)
    {
        if (e.Outcome is not ("kill" or "wipe" or "cleared")) return;
        bool wipe = e.Outcome == "wipe";
        var c = wipe ? CoralC : MintC2;
        var text = wipe ? L.T("GAME OVER") : L.T("STAGE CLEAR");
        var fill = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(0, 1) };
        fill.GradientStops.Add(new GradientStop(Mod.Mix(c, Colors.White, .7), 0)); fill.GradientStops.Add(new GradientStop(c, 1)); fill.Freeze();
        var word = Word(text, 2, c, .55, fill, 15);
        word.HorizontalAlignment = HorizontalAlignment.Center; word.VerticalAlignment = VerticalAlignment.Center;
        // the inverted frame: the marquee a field of the tone with the words in black, flashed against the lit one (opacity only)
        var inv = Word(text, 2, DeepC, 0, Ink(DeepC), 15);
        inv.HorizontalAlignment = HorizontalAlignment.Center; inv.VerticalAlignment = VerticalAlignment.Center;
        // the typing: a hard-edged mask over the words steps right a letter at a time (the mask's transform only)
        var maskAt = new TranslateTransform();
        if (word is LedWord lw0)
        {
            double mw = lw0.DotW + 20;
            var mask = new LinearGradientBrush { MappingMode = BrushMappingMode.Absolute, StartPoint = new Point(-mw, 0), EndPoint = new Point(mw, 0), Transform = maskAt };
            mask.GradientStops.Add(new GradientStop(Colors.Black, 0)); mask.GradientStops.Add(new GradientStop(Colors.Black, .5));
            mask.GradientStops.Add(new GradientStop(Colors.Transparent, .5)); mask.GradientStops.Add(new GradientStop(Colors.Transparent, 1));
            word.OpacityMask = mask; inv.OpacityMask = mask;
        }
        var lit = new Border { Background = Ink(DeepC), BorderBrush = Ink(c), BorderThickness = new Thickness(1.5), Child = word };
        var flip = new Border { Background = Ink(c), Child = inv, Opacity = 0, Visibility = Visibility.Hidden };
        var banner = new Grid { Opacity = 0, Children = { lit, flip } };
        var open = new ScaleTransform(.02, 1);
        banner.RenderTransformOrigin = new Point(.5, .5); banner.RenderTransform = open;
        _marquee.Children.Insert(_marquee.Children.IndexOf(_marqueeSparks), banner);
        double w = _fight.Width > 0 ? _fight.Width - 2 * Bezel : FightWidth - 10 - 2 * Bezel, y = MarqueeH / 2;
        if (!wipe)
        {
            // fireworks along the marquee either side of the words, one after another
            Color pink = Color.FromRgb(0xFF, 0x66, 0xCC), cyan = Color.FromRgb(0x8C, 0xEB, 0xFF);
            Burst(_marqueeSparks, new Point(w * .07, y), new[] { LedC, pink, Colors.White }, 11, 150, 650);
            Burst(_marqueeSparks, new Point(w * .93, y), new[] { MintC2, cyan, Colors.White }, 11, 280, 650);
            Burst(_marqueeSparks, new Point(w * .16, y), new[] { pink, MintC2, Colors.White }, 9, 410, 600);
            Burst(_marqueeSparks, new Point(w * .84, y), new[] { cyan, LedC, Colors.White }, 9, 540, 600);
            Burst(_marqueeSparks, new Point(w * .05, y), new[] { MintC2, LedC, Colors.White }, 8, 700, 600);
            Burst(_marqueeSparks, new Point(w * .95, y), new[] { LedC, pink, Colors.White }, 8, 820, 600);
        }
        MarqueeChase(0, 1600);
        Motion.Play(banner, 2400, t =>
        {
            open.ScaleX = Math.Max(.02, Out(Seg(t, 0, .1)) * (t < .9 ? 1 : 1 - Seg(t, .9, 1)));
            banner.Opacity = t < .9 ? 1 : 1 - Seg(t, .9, 1);
            // the flip: the inverted field for the first beats, flashing against the lit one, then the lit one held
            Fade(flip, t > .36 && t < .5 && ((int)((t - .36) / .035)) % 2 == 0 ? 1 : 0);
            _fightLegend.Opacity = t < .95 ? 0 : 1;
            if (word is LedWord lw) { int k = (int)Math.Floor(Seg(t, .08, .35) * lw.Letters); maskAt.X = k >= lw.Letters ? lw.DotW + 20 : lw.LetterEnd(k - 1); }
            else { word.Opacity = Seg(t, .08, .2); inv.Opacity = word.Opacity; }
        }, () => { _marquee.Children.Remove(banner); _fightLegend.Opacity = 1; });
    }
}
