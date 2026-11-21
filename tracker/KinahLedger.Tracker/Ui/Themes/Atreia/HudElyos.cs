using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using KinahLedger.Parser;
using KinahLedger.Session;
using static KinahLedger.Ui.HudKit;

namespace KinahLedger.Ui;

/// <summary>
/// Fade (the Modern theme; Steven, 2026-11-17, from the mock-up "Modern theme, the fourth look, lighter"): no panel at all.
/// Each meter row is a soft wash in the member's class colour, fading out to the right over the length of their share,
/// rounded at its left; the words sit straight on the game in white with a soft dark shade (a tight drop under each glyph and
/// a wide faint halo), so they read over snow and sky as over a cave. You: a thin white bar at your row's left edge. Marks are
/// small dark-glass chips with a light hairline; the boss's HP a slim line with ticks at the thirds; the foot under a faint
/// hairline. The Buddy is the same idea: a soft blue wash fading to the right, rounded at its left, the run's ring, the hour
/// big with the best drop and the last pickups as tiles beside it, the session under it, the next timer as a dark-glass chip at its end.
/// The moments stay quiet: a pickup sends a light across the wash and its worth floats in beside it; a rare drop's wash
/// blooms in its grade's colour; your best passed runs the ring round in mint; an entry opening lights its chip; a fight
/// slides in; its end lays a wash with the outcome over the fight's head.
/// </summary>
sealed partial class HudElyos : ModernLook
{
    static readonly Color BlueC = Color.FromRgb(0x2B, 0x5C, 0xFF), MintC2 = Color.FromRgb(0x5D, 0xF2, 0xA6), MarkMintC = Color.FromRgb(0x7D, 0xFF, 0xC0),
        CoralC = Color.FromRgb(0xFF, 0x86, 0x86), HpC = Color.FromRgb(0xFF, 0x6B, 0x7D), DeepC = Color.FromRgb(0x0B, 0x0C, 0x10), GreyC = Color.FromRgb(0x8A, 0x90, 0xA0),
        WarnC2 = Color.FromRgb(0xFF, 0xB0, 0x86), GoldC2 = Color.FromRgb(0xFF, 0xCF, 0x6A);
    static readonly SolidColorBrush White = Frozen(Colors.White), Soft = Frozen(Mod.A(Colors.White, .84)), Quiet = Frozen(Mod.A(Colors.White, .62)),
        Mint2 = Frozen(Color.FromRgb(0x8C, 0xFF, 0xC6)), MarkMint = Frozen(MarkMintC), Coral = Frozen(CoralC);

    // the room round the look: the shade's halo stays inside the window; 98 at the right for the pickup's chip beside the Buddy
    readonly Grid _root = new() { Margin = new Thickness(8, 8, 98, 10), HorizontalAlignment = HorizontalAlignment.Left };
    readonly StackPanel _body = new() { HorizontalAlignment = HorizontalAlignment.Left };
    double _opacity = 0.9;

    // the Buddy: its wash, the ring, the hour, the session and the timer chips
    readonly Grid _line = new() { HorizontalAlignment = HorizontalAlignment.Left };
    readonly TacSketch _lineWash;
    readonly StackPanel _lineBody = new() { Orientation = Orientation.Horizontal, Margin = new Thickness(5, 5, 18, 5) };
    readonly Grid _lineFx = new() { IsHitTestVisible = false, ClipToBounds = true };
    readonly Canvas _lineAnchor = Tac.Anchor(HorizontalAlignment.Right, VerticalAlignment.Center);
    readonly StackPanel _timers = new() { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
    readonly Border _timersInLine = new() { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 0, 0) };
    /// <summary>The timer chips over the line, when the pickup strip takes their room on it.</summary>
    readonly Border _timersUp = new() { HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(4, 0, 0, 5), Visibility = Visibility.Collapsed };
    /// <summary>The pickup strip in Fade: 24 px tiles beside the hour, a soft white rule, a gold star.</summary>
    static readonly PickStrip.Look Strip = new(24, 4, true, Frozen(Mod.A(Colors.White, .32)), Frozen(Color.FromRgb(0xFF, 0xCF, 0x6A)));
    bool _dim;
    TacSketch? _ringArt;
    double _share = -1, _complete, _sheen = -1;
    bool _beat;
    Color _sheenC = Colors.White;
    Border? _firstChip;

    // the rare drop under the Buddy, and the notice line
    readonly Grid _rare = new() { HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 4, 0, 0), Visibility = Visibility.Collapsed };
    readonly StackPanel _note = new() { Orientation = Orientation.Horizontal, Margin = new Thickness(12, 5, 0, 0), Visibility = Visibility.Collapsed };

    // the fight: no panel, its head, HP, rows and foot straight over the game
    readonly Grid _fight = new() { HorizontalAlignment = HorizontalAlignment.Left, Visibility = Visibility.Collapsed, Margin = new Thickness(0, 0, 0, 10) };
    readonly StackPanel _fightBody = new();
    readonly Border _timersInFight = new() { Margin = new Thickness(0, 0, 0, 7), HorizontalAlignment = HorizontalAlignment.Left };
    readonly Grid _fightFx = new() { IsHitTestVisible = false, VerticalAlignment = VerticalAlignment.Top };
    readonly TranslateTransform _fightShift = new();
    FrameworkElement? _head;

    public HudElyos() : base(bare: true)
    {
        // the look's own rows (Ui/Themes/ThemeRows.cs): each a class wash fading out to the right
        UseRows(FadeRow.Pitch, m => new FadeRow(), 16, GapLine);
        _lineWash = new TacSketch(DrawLineWash);
        _line.Children.Add(_lineWash); _line.Children.Add(_lineFx); _line.Children.Add(Lit(_lineBody)); _line.Children.Add(_lineAnchor);
        _fight.Children.Add(_fightBody); _fight.Children.Add(_fightFx);
        _fight.RenderTransform = _fightShift;
        _runHeadLit = Lit(_runHead); _runHeadLit.Visibility = Visibility.Collapsed;
        _body.Children.Add(_fight); _body.Children.Add(Lit(_timersUp)); _body.Children.Add(_runHeadLit); _body.Children.Add(_line); _body.Children.Add(_rare); _body.Children.Add(Lit(_note));
        _root.Children.Add(_body);
        System.Windows.Documents.TextElement.SetFontFamily(_root, Mod.Face);
        TextOptions.SetTextFormattingMode(_root, TextFormattingMode.Display);
    }

    public override FrameworkElement Root => _root;
    public override Thickness Inset => _root.Margin;
    public override double Corner => 0;
    public override FrameworkElement? FightFrame => _fight.Visibility == Visibility.Visible ? _fight : null;

    public override void SetOpacity(double opacity)
    {
        // no panel to fade: the Buddy's wash and the rows' washes take the setting (never so faint the bar is lost)
        _opacity = Math.Clamp(opacity, 0.3, 1);
        _lineWash.InvalidateVisual();
    }

    /// <summary>The wash's strength from the opacity setting: 1 at the usual 90%, a little lighter or richer either side.</summary>
    double WashK => .7 + .35 * _opacity;

    protected override string LineSig(Figures f) => base.LineSig(f) + "|" + (Rare(Values)?.Id ?? "") + "|" + (Picks(Values).FirstOrDefault()?.Id ?? "") + "|" + Dimmed(Values) + "|" + PickStrip.Sig(Values, Strip) + "|" + RunBuddy.Sig(Values);

    static bool Dimmed(IReadOnlyDictionary<string, string> v) => (v.TryGetValue("show", out var s) && s == "dim") || (v.TryGetValue("paused", out var p) && p.Length > 0);

    // ================================================================ the shade

    /// <summary>The tight shade under every glyph straight on the game: 1 px down, 2 px soft, 85% black.</summary>
    static DropShadowEffect Shade() => new() { Color = Colors.Black, Direction = 270, ShadowDepth = 1, BlurRadius = 3, Opacity = .95 };

    /// <summary>The wide soft halo round a group of words (8 px), so a bright scene behind them goes a shade darker.</summary>
    static DropShadowEffect Halo() => new() { Color = Colors.Black, ShadowDepth = 0, BlurRadius = 10, Opacity = .62 };

    /// <summary>Words lit for the game: the tight shade on them, the halo round them.</summary>
    static Border Lit(FrameworkElement el)
    {
        el.Effect = Shade();
        return new Border { Child = el, Effect = Halo(), HorizontalAlignment = el.HorizontalAlignment, VerticalAlignment = el.VerticalAlignment };
    }

    /// <summary>A soft wash in <paramref name="c"/> from its left (rounded, <paramref name="radius"/>) fading out by its right end.</summary>
    static void Wash(DrawingContext dc, Rect r, Color c, double a0, double a1, double radius)
    {
        if (r.Width < 1 || r.Height < 1) return;
        var b = new LinearGradientBrush { MappingMode = BrushMappingMode.Absolute, StartPoint = new Point(r.Left, 0), EndPoint = new Point(r.Right, 0) };
        b.GradientStops.Add(new GradientStop(Mod.A(c, a0), 0));
        b.GradientStops.Add(new GradientStop(Mod.A(c, a1), .7));
        b.GradientStops.Add(new GradientStop(Mod.A(c, 0), 1));
        b.Freeze();
        dc.PushClip(new RectangleGeometry(r));
        dc.DrawRoundedRectangle(b, null, new Rect(r.Left, r.Top, r.Width + radius + 2, r.Height), radius, radius);
        dc.Pop();
    }

    /// <summary>A small dark-glass chip with a light hairline (TOP DPS, the heal mark, AVG): <paramref name="filled"/> white with dark words (YOU).</summary>
    static Border Mark(string s, Brush? fg = null, Color? edge = null, bool filled = false, double size = 8.5)
    {
        var t = Tt(s, size, filled ? Frozen(Color.FromRgb(0x11, 0x11, 0x11)) : fg ?? White, FontWeights.Bold);
        return new Border
        {
            Child = t, CornerRadius = new CornerRadius(4), Padding = new Thickness(4, 0, 4, 1), VerticalAlignment = VerticalAlignment.Center,
            Background = filled ? White : Frozen(Mod.A(DeepC, .48)), BorderThickness = new Thickness(1),
            BorderBrush = filled ? White : Frozen(Mod.A(edge ?? Colors.White, edge == null ? .38 : .62)),
        };
    }

    /// <summary>A faint hairline across (the foot's, the card's).</summary>
    static Border Hairline(Thickness m, double a = .25) => new() { Height = 1, Background = Frozen(Mod.A(Colors.White, a)), Margin = m, IsHitTestVisible = false };

    // ================================================================ the Buddy

    protected override void DrawLine(Figures f)
    {
        _lineBody.Children.Clear();
        _ringArt = null; _timerUp = false; _stripTimerW = -1;
        if (_timersInLine.Child != null) _timersInLine.Child = null;
        _dim = Dimmed(Values);
        _line.Opacity = _dim ? .62 : 1;
        bool mark = f.Abyss && f.Counted && f.Icon.Length > 0;
        if (mark) { var m = AbyssMark(f.Icon, 36); m.Margin = new Thickness(2, 0, 9, 0); _lineBody.Children.Add(m); }
        // in a dungeon run: the run's clock, splits and verdict in place of the hour (HudElyos.Run.cs)
        var rs = RunNow();
        _runHeadLit.Visibility = Visibility.Collapsed;
        if (rs != null) DrawRun(rs);
        else if (!HasHour(f))
        {
            var w = Tt(Waiting(f.Abyss), 12.5, Soft, FontWeights.SemiBold); w.Margin = new Thickness(mark ? 0 : 9, 7, 0, 7);
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
                var pc = Tt(f.Beat ? "★" : Pct(f.Share), 13, White, FontWeights.Bold); pc.HorizontalAlignment = HorizontalAlignment.Center;
                ring.Children.Add(pc);
                _lineBody.Children.Add(ring);
            }
            else if (!mark) _lineBody.Children.Add(new Border { Width = 8 });
            // the hour big, its unit small; the session and the latest pickup under it
            var words = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 1, 0, 1) };
            var top = new StackPanel { Orientation = Orientation.Horizontal };
            var rate = Tt(f.Rate, f.Warm ? 15 : 22, f.Warm ? Soft : White, f.Warm ? FontWeights.SemiBold : FontWeights.Bold);
            rate.Tag = "buddy-rate";
            top.Children.Add(rate);
            if (f.Unit.Length > 0) { var u = Tt(f.Unit, 11, Soft, FontWeights.SemiBold); u.VerticalAlignment = VerticalAlignment.Bottom; u.Margin = new Thickness(2, 0, 0, 4); top.Children.Add(u); }
            words.Children.Add(top);
            // farming, the pickups as the strip beside the hour where it fits (the best drop and the last three), the session
            // alone under it; with the timer chips at the line's end and no room left beside them, the chips go over the line
            var sub = Sub(f, true);
            FrameworkElement? strip = null, under = null;
            if (StripTime(f))
            {
                var bare = Sub(f, false);
                double baseW = 5 + 18 + _lineBody.Children.OfType<FrameworkElement>().Sum(Mod.W), timerW = LineTimerW();
                double wordsW = Math.Max(Mod.W(top), Mod.W(bare)), oldW = Math.Max(Mod.W(top), Mod.W(sub));
                foreach (var look in StripFall(Strip))
                {
                    if ((strip = StripIn(StripRoom(FightWidth - 10, baseW + timerW, wordsW, oldW, 12), look)) != null) break;
                    if (timerW > 0 && (strip = StripIn(StripRoom(FightWidth - 10, baseW, wordsW, oldW + timerW, 12), look)) != null) { _timerUp = true; break; }
                }
                if (strip != null) { sub = bare; strip.Margin = new Thickness(12, 0, 0, 0); }
                // no room beside the hour: under the session, never dropped (Steven, 2026-11-19)
                else if ((under = StripUnder(Strip, false)) != null) sub = bare;
                _stripTimerW = timerW;
            }
            if (sub.Inlines.Count > 0) words.Children.Add(sub);
            if (under != null) words.Children.Add(under);
            _lineBody.Children.Add(words);
            if (strip != null) _lineBody.Children.Add(strip);
        }
        _lineBody.Children.Add(_timersInLine);
        PlaceTimers();
        _lineWash.InvalidateVisual();

        // the rare drop the window holds: its tile on a wash in its grade's colour, RARE DROP and its name
        _rare.Children.Clear();
        if (!KinahHidden && Rare(Values) is { } r)
        {
            var c = ColorOf(r.Color, r.Grade);
            _rareC = c; _rareBloom = 1;
            var wash = new TacSketch((dc, s) => Wash(dc, new Rect(0, 0, s.Width * Math.Clamp(_rareBloom, 0, 1), s.Height), c, .58 * WashK + .4 * _rareFlash, .34 * WashK, s.Height / 2));
            _rareWash = wash;
            _rare.Children.Add(wash);
            var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(4, 4, 24, 4) };
            var tile = Tile(r, 26); tile.Margin = new Thickness(0, 0, 8, 0);
            _rareTile = tile;
            row.Children.Add(tile);
            var mk = Mark(L.T("RARE DROP"), size: 8); mk.Margin = new Thickness(0, 0, 7, 0);
            row.Children.Add(mk);
            var nm = Tt(r.Name, 13, Frozen(Mod.Mix(c, Colors.White, .45)), FontWeights.Bold);
            row.Children.Add(nm);
            if (r.Count.Length > 0) { var cn = Tt(" " + r.Count, 11, Soft, FontWeights.SemiBold); cn.Margin = new Thickness(2, 1, 0, 0); row.Children.Add(cn); }
            var lit = Lit(row);
            _rare.Children.Add(lit);
            _rare.Visibility = Visibility.Visible;
        }
        else { _rare.Visibility = Visibility.Collapsed; _rareWash = null; _rareTile = null; }

        // the notice: its words in their tone under the Buddy, the warning sign before a pause
        _note.Children.Clear();
        if (f.Note.Length > 0 && !KinahHidden)
        {
            bool warn = ReferenceEquals(f.NoteTone, Warn);
            if (warn) { var wi = WarnIcon(); wi.Width = wi.Height = 13; wi.Margin = new Thickness(0, 0, 5, 0); _note.Children.Add(wi); }
            var tone = warn ? WarnC2 : ReferenceEquals(f.NoteTone, Gold) ? GoldC2 : ReferenceEquals(f.NoteTone, Mint) ? MintC2 : Color.FromRgb(0xF2, 0xF4, 0xF8);
            var t = Tt(f.Note, 11.5, Frozen(tone), FontWeights.SemiBold); t.TextTrimming = TextTrimming.CharacterEllipsis; t.MaxWidth = 380;
            _note.Children.Add(t);
            _note.Visibility = Visibility.Visible;
        }
        else _note.Visibility = Visibility.Collapsed;
    }

    /// <summary>The line under the hour: the session, the latest pickup and its worth (<paramref name="pick"/>); in the Abyss its stats.</summary>
    TextBlock Sub(Figures f, bool pick)
    {
        var sub = new TextBlock { FontFamily = Mod.Face, FontSize = 10.5, FontWeight = FontWeights.Medium, Foreground = Frozen(Mod.A(Colors.White, .86)), Margin = new Thickness(1, 1, 0, 0) };
        System.Windows.Documents.Typography.SetNumeralAlignment(sub, FontNumeralAlignment.Tabular);
        void Dot() { if (sub.Inlines.Count > 0) sub.Inlines.Add(new System.Windows.Documents.Run("  ·  ") { Foreground = Quiet }); }
        if (f.Total.Length > 0) { sub.Inlines.Add(new System.Windows.Documents.Run(f.Total) { FontWeight = FontWeights.SemiBold, Foreground = White }); sub.Inlines.Add(new System.Windows.Documents.Run(" " + f.TotalWord)); }
        foreach (var (fig, word, tone) in f.Stats)
        {
            Dot();
            var c = ReferenceEquals(tone, Rec) ? CoralC : ReferenceEquals(tone, Gold) ? GoldC2 : Colors.White;
            sub.Inlines.Add(new System.Windows.Documents.Run(fig) { FontWeight = FontWeights.SemiBold, Foreground = Frozen(c) });
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

    Color _rareC;
    double _rareBloom = 1, _rareFlash;
    TacSketch? _rareWash;
    FrameworkElement? _rareTile;

    /// <summary>The Buddy's wash: blue fading out to the right, rounded at its left (grey while paused); a pickup's light crossing it.</summary>
    void DrawLineWash(DrawingContext dc, Size s)
    {
        if (s.Width < 4 || s.Height < 4) return;
        var c = _dim ? GreyC : BlueC;
        Wash(dc, new Rect(0, 0, s.Width, s.Height), c, (_dim ? .42 : .6) * WashK, (_dim ? .2 : .3) * WashK, s.Height / 2);
        if (_sheen is >= 0 and <= 1)
        {
            // the light: a soft band crossing the wash left to right, inside its rounded shape, fading as it nears the faded end
            double x = -40 + (s.Width * .8 + 40) * _sheen, a = .26 * (1 - _sheen * .7);
            dc.PushClip(new RectangleGeometry(new Rect(0, 0, s.Width, s.Height), s.Height / 2, s.Height / 2));
            dc.DrawRectangle(Mod.Grad(false, (Mod.A(_sheenC, 0), 0), (Mod.A(_sheenC, a), .5), (Mod.A(_sheenC, 0), 1)), null, new Rect(x, 0, 46, s.Height));
            dc.Pop();
        }
    }

    /// <summary>The run's ring: a faint track and your best's share as a white arc (mint once beaten), round caps.</summary>
    void DrawRing(DrawingContext dc, Size s)
    {
        double c = s.Width / 2, r = c - 3;
        var center = new Point(c, c);
        dc.DrawEllipse(Frozen(Mod.A(DeepC, .22)), null, center, r - 1, r - 1);
        dc.DrawEllipse(null, Mod.P(Frozen(Mod.A(Colors.White, .28)), 3.2), center, r, r);
        double share = Math.Clamp(_beat ? 1 : _share, 0, 1);
        double shown = share + (1 - share) * Math.Clamp(_complete, 0, 1);
        var tone = _beat || _complete >= 1 ? MintC2 : Colors.White;
        if (shown >= .999) { dc.DrawEllipse(null, Mod.P(Frozen(tone), 3.4), center, r, r); return; }
        if (shown < .004) return;
        double a = 2 * Math.PI * shown;
        var fig = new PathFigure { StartPoint = new Point(c, c - r) };
        fig.Segments.Add(new ArcSegment(new Point(c + r * Math.Sin(a), c - r * Math.Cos(a)), new Size(r, r), 0, a > Math.PI, SweepDirection.Clockwise, true));
        var pg = new PathGeometry(); pg.Figures.Add(fig); pg.Freeze();
        dc.DrawGeometry(null, Mod.P(Frozen(Mod.A(DeepC, .35)), 5.4, round: true), pg);
        dc.DrawGeometry(null, Mod.P(Frozen(tone), 3.4, round: true), pg);
    }

    protected override void KinahShown(bool shown)
    {
        _line.Visibility = shown ? Visibility.Visible : Visibility.Collapsed;
        if (!shown) { _note.Visibility = Visibility.Collapsed; _rare.Visibility = Visibility.Collapsed; }
        _fight.Margin = new Thickness(0, 0, 0, shown ? 10 : 0);
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
            var c = tone == TimerBadge.Tone.On ? MintC2 : tone == TimerBadge.Tone.Soon ? GoldC2 : Color.FromRgb(0xE6, 0xE9, 0xF0);
            var sp = new StackPanel { Orientation = Orientation.Horizontal };
            sp.Children.Add(new System.Windows.Shapes.Ellipse { Width = first ? 6.5 : 5, Height = first ? 6.5 : 5, Fill = Frozen(c), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) });
            var t = Tt(text, first ? 11.5 : 10, first ? White : Soft, first ? FontWeights.Bold : FontWeights.SemiBold);
            if (tone == TimerBadge.Tone.On) t.Foreground = Frozen(Mod.Mix(MintC2, Colors.White, .45));
            sp.Children.Add(t);
            var chip = new Border
            {
                Child = sp, CornerRadius = new CornerRadius(99), Padding = first ? new Thickness(8, 3, 10, 4) : new Thickness(7, 2, 9, 3), VerticalAlignment = VerticalAlignment.Center,
                Background = Frozen(Mod.A(DeepC, .46)), BorderThickness = new Thickness(1), BorderBrush = Frozen(Mod.A(tone == TimerBadge.Tone.On ? MintC2 : Colors.White, tone == TimerBadge.Tone.On ? .5 : .22)),
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
        double w = FightWidth - 10;
        _fight.Width = w;
        _fightBody.Children.Add(_timersInFight);
        // the head: the boss, the outcome once it's over, the clock
        var head = new Grid();
        head.ColumnDefinitions.Add(new ColumnDefinition()); head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var title = Tt(e.Title, 14, White, FontWeights.SemiBold); title.TextTrimming = TextTrimming.CharacterEllipsis;
        head.Children.Add(title);
        var right = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(8, 0, 0, 0) };
        if (e.Waiting) right.Children.Add(Mark(L.T("READY"), MarkMint, MarkMintC, size: 9.5));
        else
        {
            var outcome = OutcomeText(e);
            if (outcome.Length > 0)
            {
                bool wipe = e.Outcome == "wipe";
                var st = Tt(outcome, 10, wipe ? Frozen(Color.FromRgb(0xFF, 0x9A, 0x9A)) : MarkMint, FontWeights.Bold);
                right.Children.Add(new Border { Child = st, CornerRadius = new CornerRadius(99), Padding = new Thickness(7, 1, 7, 2), Background = Frozen(Mod.A(DeepC, .42)), Margin = new Thickness(0, 1, 8, 0), VerticalAlignment = VerticalAlignment.Center, Tag = "outcome" });
            }
            var clock = Tt(Clock(e, now), 18, White, FontWeights.Bold); clock.Tag = "fight-clock";
            right.Children.Add(clock);
        }
        Grid.SetColumn(right, 1); head.Children.Add(right);
        _head = Lit(head);
        _fightBody.Children.Add(_head);
        if (e.Waiting)
        {
            var wt = Tt(e.ReadyText, 12.5, Soft, FontWeights.SemiBold); wt.Margin = new Thickness(0, 6, 0, 2); wt.TextTrimming = TextTrimming.CharacterEllipsis;
            _fightBody.Children.Add(Lit(wt));
            if (PartyBeforeHit(e)) { _fightBody.Children.Add(new Border { Height = 6 }); PutRows(_fightBody, e, now, w); }
        }
        else
        {
            if (HpOf(e) is { } hp)
            {
                // the boss's HP: a slim 3 px line with ticks at the thirds, the figure and what's left under its right end
                var track = new Grid { Height = 5, Margin = new Thickness(0, 8, 0, 0) };
                track.Children.Add(new TacSketch((dc, s) =>
                {
                    double y = 1, h = 3, x1 = s.Width * Math.Clamp(hp, 0, 1);
                    dc.DrawRoundedRectangle(Frozen(Mod.A(DeepC, .3)), null, new Rect(-.5, y - .5, s.Width + 1, h + 1), 2, 2);
                    dc.DrawRoundedRectangle(Frozen(Mod.A(Colors.White, .24)), null, new Rect(0, y, s.Width, h), 1.5, 1.5);
                    if (x1 > .5) dc.DrawRoundedRectangle(Frozen(HpC), null, new Rect(0, y, x1, h), 1.5, 1.5);
                    for (int k = 1; k <= 2; k++) { double x = Math.Round(s.Width * k / 3); dc.DrawRectangle(Frozen(Mod.A(Colors.Black, .62)), null, new Rect(x - 1, y - .5, 2, h + 1)); }
                }));
                HpGhost(track, e, hp, now, 1.5);
                _fightBody.Children.Add(track);
                var hl = new TextBlock { FontFamily = Mod.Face, FontSize = 10.5, Foreground = Soft, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 2, 0, 0) };
                System.Windows.Documents.Typography.SetNumeralAlignment(hl, FontNumeralAlignment.Tabular);
                hl.Inlines.Add(new System.Windows.Documents.Run($"{Math.Round(hp * 100):0}%") { FontWeight = FontWeights.Bold, Foreground = hp < .34 ? Frozen(Color.FromRgb(0xFF, 0x9A, 0xA4)) : White });
                var left = HpLeftText(e);
                if (left.Length > 0) hl.Inlines.Add(new System.Windows.Documents.Run("  ·  " + left) { FontWeight = FontWeights.Medium });
                hl.Tag = "hp-text";
                _fightBody.Children.Add(Lit(hl));
                _fightBody.Children.Add(new Border { Height = 4 });
            }
            else _fightBody.Children.Add(new Border { Height = 8 });
            PutRows(_fightBody, e, now, w);
            if (e.HasDamage)
            {
                _fightBody.Children.Add(Hairline(new Thickness(0, 8, 0, 6)));
                _fightBody.Children.Add(Foot(e, now));
            }
        }
        _fight.Visibility = Visibility.Visible;
        PlaceTimers();
    }

    // ================================================================ the moments

    /// <summary>A pickup: a soft light crosses the Buddy's wash and the worth floats in beside it, then fades.</summary>
    protected override void Pickup(Pick p, bool held)
    {
        if (KinahHidden || _line.Visibility != Visibility.Visible) return;
        var worth = p.Worth.Length > 0 ? "+" + p.Worth.TrimStart('+') : p.Count;
        var words = new StackPanel { Margin = new Thickness(9, 2, 11, 3) };
        words.Children.Add(Tt(worth, 14, p.Worth.Length > 0 ? Frozen(Mod.Mix(MintC2, Colors.White, .25)) : White, FontWeights.Bold));
        var nm = Tt(p.Name.Length > 13 ? p.Name[..12].TrimEnd() + "…" : p.Name, 9.5, Soft, FontWeights.SemiBold); nm.Margin = new Thickness(0, -2, 0, 0);
        words.Children.Add(nm);
        var chip = new Border
        {
            Child = words, MaxWidth = 88, CornerRadius = new CornerRadius(10), Background = Frozen(Mod.A(DeepC, .5)), BorderThickness = new Thickness(1),
            BorderBrush = Frozen(Mod.A(Colors.White, .28)), Effect = Halo(),
        };
        var shift = new TranslateTransform();
        chip.RenderTransform = shift;
        chip.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        Canvas.SetLeft(chip, 6); Canvas.SetTop(chip, -chip.DesiredSize.Height / 2);
        _lineAnchor.Children.Clear(); _lineAnchor.Children.Add(chip);
        _sheenC = Colors.White;
        Motion.Play(chip, 1900, t =>
        {
            _sheen = t < .5 ? Seg(t, 0, .5) : -1; _lineWash.InvalidateVisual();
            double k = Out(Seg(t, .1, .3));
            shift.X = -10 * (1 - k);
            chip.Opacity = t < .82 ? k : 1 - Seg(t, .82, 1);
        }, () => { _sheen = -1; _lineWash.InvalidateVisual(); _lineAnchor.Children.Remove(chip); });
    }

    /// <summary>A rare drop: its wash blooms out to the right in the grade's colour, a flash in it, the tile popping in.</summary>
    protected override void RareDrop(Pick p, bool held)
    {
        if (_rareWash is not { } wash) return;
        var tile = _rareTile;
        var sc = new ScaleTransform(1, 1);
        if (tile != null) { tile.RenderTransformOrigin = new Point(.5, .5); tile.RenderTransform = sc; }
        Motion.Play(wash, 1500, t =>
        {
            _rareBloom = Out(Seg(t, 0, .45));
            _rareFlash = t < .2 ? Seg(t, 0, .2) : 1 - Seg(t, .2, .8);
            wash.InvalidateVisual();
            sc.ScaleX = sc.ScaleY = .65 + .35 * Back(Seg(t, .05, .4), 2.2);
        }, () => { _rareBloom = 1; _rareFlash = 0; wash.InvalidateVisual(); sc.ScaleX = sc.ScaleY = 1; });
    }

    /// <summary>Your best passed: the ring runs on round to full in mint and a mint light crosses the wash.</summary>
    protected override void BestPassed(bool held)
    {
        if (_ringArt is not { } art) return;
        double was = _share is > 0 and < 1 ? _share : .86;
        bool beat = _beat;
        _sheenC = MintC2;
        Motion.Play(art, 1800, t =>
        {
            _beat = false; _share = was;
            _complete = Out(Seg(t, 0, .5));
            if (t >= .5) { _beat = beat; _complete = 1; }
            art.InvalidateVisual();
            _sheen = t is > .3 and < .85 ? Seg(t, .3, .85) : -1; _lineWash.InvalidateVisual();
        }, () => { _complete = 0; _beat = beat; _sheen = -1; _sheenC = Colors.White; art.InvalidateVisual(); _lineWash.InvalidateVisual(); });
    }

    /// <summary>An entry opened: the first timer's chip lights mint round its edge and settles.</summary>
    protected override void EntryOpened(TimerBoard.Item i, bool held)
    {
        if (_firstChip is not { } chip) return;
        var glow = new DropShadowEffect { Color = MintC2, ShadowDepth = 0, BlurRadius = 10, Opacity = 0 };
        chip.Effect = glow;
        var edge = chip.BorderBrush;
        Motion.Play(chip, 1200, t =>
        {
            double k = t < .25 ? Seg(t, 0, .25) : 1 - Seg(t, .25, 1);
            glow.Opacity = .85 * k;
            chip.BorderBrush = Frozen(Mod.A(MintC2, .5 + .5 * k));
        }, () => { chip.Effect = null; chip.BorderBrush = edge; });
    }

    /// <summary>A fight started: the meter slides in from the left as it fades up.</summary>
    protected override void FightStarted(Encounter e, bool held)
    {
        Motion.Play(_fight, 520, t =>
        {
            double k = Out(t);
            _fightShift.Y = 10 * (1 - k);
            _fightBody.Opacity = k;
        }, () => { _fightShift.Y = 0; _fightBody.Opacity = 1; });
    }

    /// <summary>A fight ended: a wash in mint (coral for a wipe) lays over the fight's head with the outcome and the clock, then lifts.</summary>
    protected override void FightEnded(Encounter e, bool held)
    {
        if (e.Outcome is not ("kill" or "wipe" or "cleared")) return;
        bool wipe = e.Outcome == "wipe";
        var c = wipe ? CoralC : MintC2;
        double bloom = 0;
        var band = new Grid { Height = 34, Margin = new Thickness(-4, -6, 0, 0) };
        band.Children.Add(new TacSketch((dc, s) =>
        {
            var r = new Rect(0, 0, s.Width * bloom, s.Height);
            Wash(dc, r, DeepC, .72, .5, 8);
            Wash(dc, r, c, .5, .26, 8);
        }));
        var words = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(12, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        words.Children.Add(Tt(OutcomeText(e), 18, White, FontWeights.Bold));
        var ck = Tt("  " + Clock(e.Duration(e.End ?? DateTime.UtcNow)), 12, Frozen(Mod.Mix(c, Colors.White, .5)), FontWeights.SemiBold); ck.Margin = new Thickness(4, 3, 0, 0);
        words.Children.Add(ck);
        var lit = Lit(words);
        band.Children.Add(lit);
        _fightFx.Children.Clear(); _fightFx.Children.Add(band);
        var head = _head;
        Motion.Play(_fightFx, 2400, t =>
        {
            bloom = Out(Seg(t, 0, .22));
            ((TacSketch)band.Children[0]).InvalidateVisual();
            lit.Opacity = t < .84 ? Seg(t, .08, .22) : 1 - Seg(t, .84, 1);
            band.Opacity = t < .84 ? 1 : 1 - Seg(t, .84, 1);
            if (head != null) head.Opacity = t < .84 ? 1 - Seg(t, 0, .12) : Seg(t, .84, 1);
        }, () => { _fightFx.Children.Clear(); if (head != null) head.Opacity = 1; });
    }
}
