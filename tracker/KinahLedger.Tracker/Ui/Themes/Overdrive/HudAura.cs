using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using KinahLedger.Parser;
using KinahLedger.Session;
using static KinahLedger.Ui.HudKit;

namespace KinahLedger.Ui;

/// <summary>
/// Aura (the Overdrive theme; Steven, 2026-11-20: "at least 1 theme should be WILD"): a battle-anime power-up screen. No
/// panel: each member's row is a dark core plate (an energy cell with slanted ends) with an aura burning round it, flame
/// tongues in the class's colour rising off its top edge along the member's part of the top, taller and brighter the more
/// damage they do; the bar inside is a gauge of pure energy (a soft body, a white-hot core line, a bright front). The
/// leader's aura goes gold-white with a bolt of static lightning crackling up through it.
/// The fight's title sits on a dark energy band, the boss's HP is an energy bar draining. The Buddy is the same core plate
/// with a low azure aura, the hour beside a burning ring (the ring's fill is your best's share, and only the filled arc burns).
///
/// The moments are events, each one course and over in about a second: a fight starting ignites every aura bottom-up with
/// a flash, then the leader powers up; a new leader's gold aura erupts up behind the meter with a shockwave ring and three
/// lightning strikes on their plate (behind the words, never over a figure); a kill flares every aura and settles; a wipe
/// gutters them; your best passed turns the Buddy gold with LIMIT BREAK slammed in beside it; a rare drop pops its tile out
/// of an energy orb; a pickup's worth rides in on a small energy chip. At rest nothing moves.
/// </summary>
sealed partial class HudAura : ModernLook
{
    static readonly Color MintC2 = Color.FromRgb(0x5D, 0xF2, 0xA6), MarkMintC = Color.FromRgb(0x7D, 0xFF, 0xC0),
        CoralC = Color.FromRgb(0xFF, 0x86, 0x86), DeepC = Color.FromRgb(0x0B, 0x0C, 0x10), GreyC = Color.FromRgb(0x8A, 0x90, 0xA0),
        WarnC2 = Color.FromRgb(0xFF, 0xB0, 0x86), GoldC2 = Color.FromRgb(0xFF, 0xCF, 0x6A);
    static readonly SolidColorBrush White = Frozen(Colors.White), Soft = Frozen(Mod.A(Colors.White, .84)), Quiet = Frozen(Mod.A(Colors.White, .62)),
        Mint2 = Frozen(Color.FromRgb(0x8C, 0xFF, 0xC6)), MarkMint = Frozen(MarkMintC), Coral = Frozen(CoralC);

    // the room round the look: the auras' glow stays inside the window; 98 at the right for the pickup's chip and LIMIT BREAK beside the Buddy
    readonly Grid _root = new() { Margin = new Thickness(8, 8, 98, 10), HorizontalAlignment = HorizontalAlignment.Left };
    readonly StackPanel _body = new() { HorizontalAlignment = HorizontalAlignment.Left };
    double _opacity = 0.9;

    /// <summary>The Buddy's flame zone over its plate.</summary>
    const double LZ = 12, LSl = 8;

    // the Buddy: its plate and aura, the gold aura of your best, the ring, the hour, the session and the timer chips
    readonly Grid _line = new() { HorizontalAlignment = HorizontalAlignment.Left };
    readonly TacSketch _linePlate, _lineGold, _lineFlash;
    readonly StackPanel _lineBody = new() { Orientation = Orientation.Horizontal, Margin = new Thickness(12, 5 + LZ, 18, 5) };
    readonly Grid _lineFx = new() { IsHitTestVisible = false, ClipToBounds = true };
    readonly Canvas _lineAnchor = Tac.Anchor(HorizontalAlignment.Right, VerticalAlignment.Center);
    readonly StackPanel _timers = new() { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
    readonly Border _timersInLine = new() { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 0, 0) };
    /// <summary>The timer chips over the line, when the pickup strip takes their room on it.</summary>
    readonly Border _timersUp = new() { HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(4, 0, 0, 3), Visibility = Visibility.Collapsed };
    /// <summary>The pickup strip in Aura: 24 px tiles beside the hour, an azure rule, a gold star.</summary>
    static readonly PickStrip.Look Strip = new(24, 4, true, Frozen(Mod.A(KiC, .55)), Frozen(Color.FromRgb(0xFF, 0xCF, 0x6A)));
    bool _dim;
    TacSketch? _ringArt;
    double _share = -1, _complete, _sheen = -1;
    bool _beat;
    Border? _firstChip;

    // the rare drop under the Buddy, and the notice line
    readonly Grid _rare = new() { HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 3, 0, 0), Visibility = Visibility.Collapsed };
    readonly StackPanel _note = new() { Orientation = Orientation.Horizontal, Margin = new Thickness(12, 5, 0, 0), Visibility = Visibility.Collapsed };

    // the fight: no panel; the burst's layer behind it, its head, HP, rows and foot, the end's band over it
    readonly Grid _fight = new() { HorizontalAlignment = HorizontalAlignment.Left, Visibility = Visibility.Collapsed, Margin = new Thickness(0, 0, 0, 10) };
    readonly Grid _under = new() { IsHitTestVisible = false, ClipToBounds = true };
    readonly StackPanel _fightBody = new();
    readonly Border _timersInFight = new() { Margin = new Thickness(0, 0, 0, 7), HorizontalAlignment = HorizontalAlignment.Left };
    readonly Grid _fightFx = new() { IsHitTestVisible = false, VerticalAlignment = VerticalAlignment.Top };
    readonly TranslateTransform _fightShift = new();
    FrameworkElement? _head, _hpLine;

    public HudAura() : base(bare: true)
    {
        // the look's own rows (Ui/Themes/ThemeRows.cs): each a core plate with its aura burning round it
        UseRows(AuraRow.Pitch, m => new AuraRow(this), 16, GapLine);
        _linePlate = new TacSketch(DrawLinePlate);
        _lineGold = new TacSketch(DrawLineGold) { Opacity = 0 };
        _lineFlash = new TacSketch((dc, s) => { if (s.Width > 20 && s.Height > LZ + 8) dc.DrawGeometry(VGrad(LZ, s.Height, (Mod.A(Colors.White, .95), 0), (Mod.A(AuGoldHotC, .6), 1)), null, Plate(new Rect(1, LZ, s.Width - 2, s.Height - LZ - 1), LSl)); }) { Opacity = 0 };
        _lineBody.Effect = Shade();
        _line.Children.Add(_linePlate); _line.Children.Add(_lineGold); _line.Children.Add(_lineFx); _line.Children.Add(_lineBody); _line.Children.Add(_lineFlash); _line.Children.Add(_lineAnchor);
        _fight.Children.Add(_under); _fight.Children.Add(_fightBody); _fight.Children.Add(_fightFx);
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
        // no panel to fade: the Buddy's plate takes the setting (never so faint the words are lost)
        _opacity = Math.Clamp(opacity, 0.3, 1);
        _linePlate.InvalidateVisual();
    }

    /// <summary>The plate's strength from the opacity setting: 1 at the usual 90%, a little lighter or richer either side.</summary>
    double WashK => .7 + .35 * _opacity;

    protected override string LineSig(Figures f) => base.LineSig(f) + "|" + (Rare(Values)?.Id ?? "") + "|" + (Picks(Values).FirstOrDefault()?.Id ?? "") + "|" + Dimmed(Values) + "|" + PickStrip.Sig(Values, Strip) + "|" + RunBuddy.Sig(Values);

    static bool Dimmed(IReadOnlyDictionary<string, string> v) => (v.TryGetValue("show", out var s) && s == "dim") || (v.TryGetValue("paused", out var p) && p.Length > 0);

    // ================================================================ the shade

    /// <summary>The tight shade under every glyph: 1 px down, soft, near black.</summary>
    static DropShadowEffect Shade() => new() { Color = Colors.Black, Direction = 270, ShadowDepth = 1, BlurRadius = 3, Opacity = .95 };

    /// <summary>The wide soft halo round a group of words straight on the game (8 px), so a bright scene behind them goes a shade darker.</summary>
    static DropShadowEffect Halo() => new() { Color = Colors.Black, ShadowDepth = 0, BlurRadius = 10, Opacity = .62 };

    /// <summary>Words lit for the game: the tight shade on them, the halo round them.</summary>
    static Border Lit(FrameworkElement el)
    {
        el.Effect = Shade();
        return new Border { Child = el, Effect = Halo(), HorizontalAlignment = el.HorizontalAlignment, VerticalAlignment = el.VerticalAlignment };
    }

    /// <summary>
    /// The face a shouted word takes (LIMIT BREAK, the fight's outcome): Impact in English; a heavy face that has the
    /// script in Korean and Chinese (Impact has no Hangul or Hanzi, and the fallback would draw them thin).
    /// </summary>
    static (FontFamily Face, FontWeight Weight) ShoutFace() => L.Code switch
    {
        "ko" => (new FontFamily("Malgun Gothic, Segoe UI"), FontWeights.Bold),
        "zh-TW" => (new FontFamily("Microsoft JhengHei, Segoe UI"), FontWeights.Bold),
        _ => (HudThemes.Impact, FontWeights.Normal),
    };

    /// <summary>A small dark chip with a hairline in its tone (TOP DPS, the heal mark, AVG): <paramref name="filled"/> white with dark words (YOU).</summary>
    static Border Mark(string s, Brush? fg = null, Color? edge = null, bool filled = false, double size = 8.5)
    {
        var t = Tt(s, size, filled ? Frozen(Color.FromRgb(0x0A, 0x0A, 0x0E)) : fg ?? White, FontWeights.Bold);
        return new Border
        {
            Child = t, CornerRadius = new CornerRadius(2), Padding = new Thickness(4, 0, 4, 1), VerticalAlignment = VerticalAlignment.Center,
            Background = filled ? White : Frozen(Mod.A(CoreBotC, .78)), BorderThickness = new Thickness(1),
            BorderBrush = filled ? White : Frozen(Mod.A(edge ?? Colors.White, edge == null ? .4 : .8)),
        };
    }

    // ================================================================ the Buddy

    protected override void DrawLine(Figures f)
    {
        _lineBody.Children.Clear();
        _ringArt = null; _timerUp = false; _stripTimerW = -1;
        if (_timersInLine.Child != null) _timersInLine.Child = null;
        _dim = Dimmed(Values);
        _line.Opacity = _dim ? .62 : 1;
        bool mark = f.Abyss && f.Counted && f.Icon.Length > 0;
        if (mark) { var m = AbyssMark(f.Icon, 36); m.Margin = new Thickness(0, 0, 9, 0); _lineBody.Children.Add(m); }
        _beat = false;
        // in a dungeon run: the run's clock, splits and verdict in place of the hour (HudAura.Run.cs)
        var rs = RunNow();
        _runHeadLit.Visibility = Visibility.Collapsed;
        if (rs != null) DrawRun(rs);
        else if (!HasHour(f))
        {
            var w = Tt(Waiting(f.Abyss), 12.5, Soft, FontWeights.SemiBold); w.Margin = new Thickness(mark ? 0 : 4, 7, 0, 7);
            // the kills and the Kinah so far under it (Steven, 2026-11-21: information, not emptiness)
            var stats = Sub(f, false);
            if (stats.Inlines.Count > 0)
            {
                w.Margin = new Thickness(mark ? 0 : 4, 3, 0, 0);
                stats.Margin = new Thickness(mark ? 1 : 5, 1, 0, 3);
                _lineBody.Children.Add(new StackPanel { VerticalAlignment = VerticalAlignment.Center, Children = { w, stats } });
            }
            else _lineBody.Children.Add(w);
        }
        else
        {
            if (HasRing(f))
            {
                _share = f.Share; _beat = f.Beat; _complete = 0;
                var ring = new Grid { Width = 56, Height = 56, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(-9, -10, 3, -10) };
                _ringArt = new TacSketch(DrawRing, 56, 56);
                ring.Children.Add(_ringArt);
                var pc = Tt(f.Beat ? "★" : Pct(f.Share), 12.5, f.Beat ? Frozen(AuGoldHotC) : White, FontWeights.Bold); pc.HorizontalAlignment = HorizontalAlignment.Center;
                ring.Children.Add(pc);
                _lineBody.Children.Add(ring);
            }
            else if (!mark) _lineBody.Children.Add(new Border { Width = 2 });
            // the hour big, its unit small; the session and the latest pickup under it
            var words = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 1, 0, 1) };
            var top = new StackPanel { Orientation = Orientation.Horizontal };
            var rate = Tt(f.Rate, f.Warm ? 15 : 22, f.Warm ? Soft : f.Beat ? Frozen(AuGoldHotC) : White, f.Warm ? FontWeights.SemiBold : FontWeights.Bold);
            rate.Tag = "buddy-rate";
            top.Children.Add(rate);
            if (f.Unit.Length > 0) { var u = Tt(f.Unit, 11, Soft, FontWeights.SemiBold); u.VerticalAlignment = VerticalAlignment.Bottom; u.Margin = new Thickness(2, 0, 0, 4); top.Children.Add(u); }
            words.Children.Add(top);
            var sub = Sub(f, true);
            FrameworkElement? strip = null, under = null;
            if (StripTime(f))
            {
                var bare = Sub(f, false);
                double baseW = 12 + 18 + _lineBody.Children.OfType<FrameworkElement>().Sum(Mod.W), timerW = LineTimerW();
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
        _lineGold.Opacity = _beat && !_dim ? 1 : 0;
        _linePlate.InvalidateVisual();

        // the rare drop the window holds: its tile on a core plate burning in its grade's colour, RARE DROP and its name
        _rare.Children.Clear();
        if (!KinahHidden && Rare(Values) is { } r)
        {
            var c = ColorOf(r.Color, r.Grade);
            _rareC = c;
            var plate = new TacSketch((dc, s) =>
            {
                if (s.Width < 30) return;
                Flames(dc, 1 + LSl, s.Width - 3, 6 + 3, 9, 23, c, Mod.Mix(c, Colors.White, .6), 1, 12);
                CorePlate(dc, new Rect(1, 6, s.Width - 2, s.Height - 7), LSl, c, .8 * WashK, Mod.Mix(c, Colors.White, .3), .7, .86);
            });
            _rare.Children.Add(plate);
            var orb = new TacSketch((dc, s) => Orb(dc, new Point(s.Width / 2, s.Height / 2), s.Width / 2, s.Height / 2, c)) { Width = 56, Height = 56, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(-3, 3, 0, -3), Opacity = 0 };
            _rareOrb = orb;
            _rare.Children.Add(orb);
            var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(12, 10, 24, 4) };
            var tile = Tile(r, 26); tile.Margin = new Thickness(0, 0, 8, 0);
            _rareTile = tile;
            row.Children.Add(tile);
            var mk = Mark(L.T("RARE DROP"), Frozen(Mod.Mix(c, Colors.White, .6)), c, size: 8); mk.Margin = new Thickness(0, 0, 7, 0);
            row.Children.Add(mk);
            var nm = Tt(r.Name, 13, Frozen(Mod.Mix(c, Colors.White, .45)), FontWeights.Bold);
            row.Children.Add(nm);
            if (r.Count.Length > 0) { var cn = Tt(" " + r.Count, 11, Soft, FontWeights.SemiBold); cn.Margin = new Thickness(2, 1, 0, 0); row.Children.Add(cn); }
            row.Effect = Shade();
            _rare.Children.Add(row);
            _rareFlash = new TacSketch((dc, s) => { if (s.Width > 30) dc.DrawGeometry(VGrad(6, s.Height, (Mod.A(Mod.Mix(c, Colors.White, .6), .7), 0), (Mod.A(c, .2), 1)), null, Plate(new Rect(1, 6, s.Width - 2, s.Height - 7), LSl)); }) { Opacity = 0 };
            _rare.Children.Insert(1, _rareFlash);
            _rare.Visibility = Visibility.Visible;
        }
        else { _rare.Visibility = Visibility.Collapsed; _rareOrb = null; _rareTile = null; _rareFlash = null; }

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
    TacSketch? _rareOrb, _rareFlash;
    FrameworkElement? _rareTile;

    /// <summary>
    /// The Buddy's plate: the low azure aura off its top edge (grey and out while paused), the dark core, the energy's core
    /// line along its foot; a pickup's light crossing it.
    /// </summary>
    void DrawLinePlate(DrawingContext dc, Size s)
    {
        if (s.Width < 20 || s.Height < LZ + 8) return;
        var r = new Rect(1, LZ, s.Width - 2, s.Height - LZ - 1);
        var tone = _dim ? GreyC : KiC;
        if (!_dim) Flames(dc, 1 + LSl + 2, s.Width - 4, LZ + 3, LZ + 3, 7, KiC, KiHotC, Math.Min(1, WashK), 20);
        CorePlate(dc, r, LSl, tone, (_dim ? .25 : .75) * WashK, Mod.Mix(tone, Colors.White, .3), .65, Math.Clamp(.8 * WashK, .55, .95));
        var g = Plate(r, LSl);
        dc.PushClip(g);
        // the energy's core along the foot, brightest at the left, fading toward the right end
        dc.DrawRectangle(Mod.Grad(false, (Mod.A(tone, .75), 0), (Mod.A(tone, .35), .7), (Mod.A(tone, .1), 1)), null, new Rect(r.X, r.Bottom - 7, r.Width, 7));
        dc.DrawRectangle(VGrad(r.Bottom - 7, r.Bottom, (Mod.A(CoreBotC, .6), 0), (Mod.A(CoreBotC, 0), 1)), null, new Rect(r.X, r.Bottom - 7, r.Width, 7));
        dc.DrawRectangle(Mod.Grad(false, (Mod.A(Mod.Mix(tone, Colors.White, .7), 1), 0), (Mod.A(Mod.Mix(tone, Colors.White, .5), .6), .75), (Mod.A(tone, 0), 1)), null, new Rect(r.X, r.Bottom - 2.2, r.Width, 1.4));
        if (_sheen is >= 0 and <= 1)
        {
            double x = -50 + (r.Width + 50) * _sheen, a = .4 * Math.Sin(Math.PI * _sheen);
            dc.DrawRectangle(Mod.Grad(false, (Mod.A(KiHotC, 0), 0), (Mod.A(KiHotC, a), .6), (Mod.A(KiHotC, 0), 1)), null, new Rect(x, r.Y, 50, r.Height));
        }
        dc.Pop();
    }

    /// <summary>The Buddy's gold aura (your best passed): taller gold tongues and a gold halo round the plate.</summary>
    void DrawLineGold(DrawingContext dc, Size s)
    {
        if (s.Width < 20 || s.Height < LZ + 8) return;
        var r = new Rect(1, LZ, s.Width - 2, s.Height - LZ - 1);
        Flames(dc, 1 + LSl + 2, s.Width - 4, LZ + 3, LZ + 3, 9, AuGoldC, AuGoldHotC, 1, 22);
        var g = Plate(r, LSl);
        dc.DrawGeometry(null, Pn(AuGoldC, .2, 6), g);
        dc.DrawGeometry(null, Pn(AuGoldC, .85, 1.3), g);
    }

    /// <summary>The ring of your best: burning azure along the share reached (gold once beaten), the rest a faint track.</summary>
    void DrawRing(DrawingContext dc, Size s)
    {
        double share = Math.Clamp(_beat ? 1 : _share, 0, 1);
        double shown = share + (1 - share) * Math.Clamp(_complete, 0, 1);
        bool gold = _beat || _complete >= 1;
        AuraRing(dc, s, shown, gold ? AuGoldC : KiC, gold ? AuGoldHotC : KiHotC, 5);
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
            var c = tone == TimerBadge.Tone.On ? MintC2 : tone == TimerBadge.Tone.Soon ? GoldC2 : KiC;
            var sp = new StackPanel { Orientation = Orientation.Horizontal };
            var dot = new RadialGradientBrush(Mod.Mix(c, Colors.White, .6), Mod.A(c, .9)); dot.Freeze();
            sp.Children.Add(new System.Windows.Shapes.Ellipse { Width = first ? 7 : 5.5, Height = first ? 7 : 5.5, Fill = dot, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0), Effect = first ? new DropShadowEffect { Color = c, ShadowDepth = 0, BlurRadius = 6, Opacity = .9 } : null });
            var t = Tt(text, first ? 11.5 : 10, first ? White : Soft, first ? FontWeights.Bold : FontWeights.SemiBold);
            if (tone == TimerBadge.Tone.On) t.Foreground = Frozen(Mod.Mix(MintC2, Colors.White, .45));
            sp.Children.Add(t);
            var chip = new Border
            {
                Child = sp, CornerRadius = new CornerRadius(3), Padding = first ? new Thickness(8, 3, 10, 4) : new Thickness(7, 2, 9, 3), VerticalAlignment = VerticalAlignment.Center,
                Background = Frozen(Mod.A(CoreBotC, .82)), BorderThickness = new Thickness(1), BorderBrush = Frozen(Mod.A(c, tone == TimerBadge.Tone.On ? .85 : .5)),
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

    /// <summary>The fight's head band: a dark core plate, a faint azure aura round it, a line of energy along its foot.</summary>
    static void DrawHeadBand(DrawingContext dc, Size s, Color tone)
    {
        if (s.Width < 30) return;
        var r = new Rect(1, 0, s.Width - 2, s.Height - 1);
        CorePlate(dc, r, 8, tone, .55, Mod.Mix(tone, Colors.White, .35), .55, .8);
        dc.PushClip(Plate(r, 8));
        dc.DrawRectangle(Mod.Grad(false, (Mod.A(tone, .0), 0), (Mod.A(tone, .32), .5), (Mod.A(tone, 0), 1)), null, new Rect(r.X, r.Bottom - 6, r.Width, 6));
        dc.DrawRectangle(Mod.Grad(false, (Mod.A(Colors.White, 0), 0), (Mod.A(Mod.Mix(tone, Colors.White, .7), 1), .5), (Mod.A(Colors.White, 0), 1)), null, new Rect(r.X, r.Bottom - 1.6, r.Width, 1.2));
        dc.Pop();
    }

    protected override void DrawFight(Encounter? e, DateTime now)
    {
        _fightBody.Children.Clear();
        _hpLine = null;
        if (e == null) { _fight.Visibility = Visibility.Collapsed; _fightFx.Children.Clear(); _under.Children.Clear(); PlaceTimers(); return; }
        double w = FightWidth - 10;
        _fight.Width = w;
        _fightBody.Children.Add(_timersInFight);
        // the head: the boss on a dark energy band, the outcome once it's over, the clock
        var head = new Grid { Height = 27 };
        head.Children.Add(new TacSketch((dc, s) => DrawHeadBand(dc, s, KiC)));
        var hg = new Grid { Margin = new Thickness(12, 0, 14, 1) };
        hg.ColumnDefinitions.Add(new ColumnDefinition()); hg.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var title = Tt(e.Title, 14, White, FontWeights.Bold); title.TextTrimming = TextTrimming.CharacterEllipsis;
        hg.Children.Add(title);
        var right = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(8, 0, 0, 0) };
        if (e.Waiting) right.Children.Add(Mark(L.T("READY"), MarkMint, MarkMintC, size: 9.5));
        else
        {
            var outcome = OutcomeText(e);
            if (outcome.Length > 0)
            {
                bool wipe = e.Outcome == "wipe";
                var st = Tt(outcome, 10, wipe ? Frozen(Color.FromRgb(0xFF, 0x9A, 0x9A)) : Frozen(AuGoldHotC), FontWeights.Bold);
                right.Children.Add(new Border { Child = st, CornerRadius = new CornerRadius(2), Padding = new Thickness(6, 0, 6, 1), Background = Frozen(Mod.A(CoreBotC, .7)), BorderThickness = new Thickness(1), BorderBrush = Frozen(Mod.A(wipe ? WipeC : AuGoldC, .85)), Margin = new Thickness(0, 1, 8, 0), VerticalAlignment = VerticalAlignment.Center, Tag = "outcome" });
            }
            var clock = Tt(Clock(e, now), 17, White, FontWeights.Bold); clock.Tag = "fight-clock";
            right.Children.Add(clock);
        }
        Grid.SetColumn(right, 1); hg.Children.Add(right);
        hg.Effect = Shade();
        head.Children.Add(hg);
        _head = head;
        _fightBody.Children.Add(head);
        if (e.Waiting)
        {
            var wt = Tt(e.ReadyText, 12.5, Soft, FontWeights.SemiBold); wt.Margin = new Thickness(8, 6, 0, 2); wt.TextTrimming = TextTrimming.CharacterEllipsis;
            _fightBody.Children.Add(Lit(wt));
            if (PartyBeforeHit(e)) { _fightBody.Children.Add(new Border { Height = 6 }); PutRows(_fightBody, e, now, w); }
        }
        else
        {
            if (HpOf(e) is { } hp)
            {
                // the boss's HP: an energy bar draining, red with a white-hot core, ticks at the thirds, a bright front; its
                // figures at its right on the same line (the room over the leader is the aura's)
                var hpLine = new Grid { Margin = new Thickness(4, 6, 6, 0) };
                hpLine.ColumnDefinitions.Add(new ColumnDefinition()); hpLine.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                var track = new Grid { Height = 8, VerticalAlignment = VerticalAlignment.Center };
                track.Children.Add(new TacSketch((dc, s) =>
                {
                    var r = new Rect(0, 0, s.Width, s.Height);
                    var g = Plate(r, 4);
                    dc.DrawGeometry(null, Pn(HpEnC, .14, 4), g);
                    dc.DrawGeometry(Frozen(Mod.A(CoreBotC, .85)), Pn(Colors.White, .18, 1), g);
                    double x1 = s.Width * Math.Clamp(hp, 0, 1);
                    if (x1 > .5)
                    {
                        dc.PushClip(g);
                        var fr = new Rect(0, 0, x1, s.Height);
                        dc.DrawRectangle(VGrad(0, s.Height, (Mod.A(HpEnC, .55), 0), (Mod.A(HpHotC, 1), .5), (Mod.A(HpEnC, .55), 1)), null, fr);
                        dc.DrawRectangle(Mod.Br(Colors.White, .85), null, new Rect(0, s.Height / 2 - .5, x1, 1));
                        dc.Pop();
                        Orb(dc, new Point(x1, s.Height / 2), 6, 6, HpEnC, .9, Colors.White);
                    }
                    for (int k = 1; k <= 2; k++) { double x = Math.Round(s.Width * k / 3); dc.DrawRectangle(Frozen(Mod.A(Colors.Black, .7)), null, new Rect(x - 1, 0, 2, s.Height)); }
                }));
                HpGhost(track, e, hp, now, 1.5);
                hpLine.Children.Add(track);
                var hl = new TextBlock { FontFamily = Mod.Face, FontSize = 11.5, Foreground = Frozen(Mod.A(Colors.White, .92)), VerticalAlignment = VerticalAlignment.Center };
                System.Windows.Documents.Typography.SetNumeralAlignment(hl, FontNumeralAlignment.Tabular);
                hl.Inlines.Add(new System.Windows.Documents.Run($"{Math.Round(hp * 100):0}%") { FontWeight = FontWeights.Bold, Foreground = hp < .34 ? Frozen(Color.FromRgb(0xFF, 0x9A, 0xA4)) : White });
                var left = HpLeftText(e);
                if (left.Length > 0) hl.Inlines.Add(new System.Windows.Documents.Run("  ·  " + left) { FontWeight = FontWeights.Medium });
                hl.Tag = "hp-text";
                var hlLit = Lit(hl); hlLit.Margin = new Thickness(8, 0, 0, 0);
                Grid.SetColumn(hlLit, 1); hpLine.Children.Add(hlLit);
                _fightBody.Children.Add(hpLine);
                _hpLine = hpLine;
            }
            else _fightBody.Children.Add(new Border { Height = 6 });
            // the leader's stage: the room their gold aura and its lightning burn up into
            if (e.HasDamage) _fightBody.Children.Add(new Border { Height = AuraRow.Stage });
            PutRows(_fightBody, e, now, w);
            if (e.HasDamage)
            {
                _fightBody.Children.Add(EnergyLine(new Thickness(4, 5, 4, 5)));
                _fightBody.Children.Add(Foot(e, now));
            }
        }
        _fight.Visibility = Visibility.Visible;
        PlaceTimers();
    }

    /// <summary>The look's rows, top first (the members as the meter shows them).</summary>
    List<AuraRow> RowsNow() => Meter?.Members.Select(m => Meter.RowOf(m.Id)).OfType<AuraRow>().ToList() ?? new();

    /// <summary>
    /// The power-up's burst behind the meter for <paramref name="row"/>: the gold aura erupting up from its plate (tall tongues,
    /// behind the head and every word), a shockwave ring round the plate and a gold flash. Built when it plays; the step takes
    /// the course's point (below 0: not yet), the end takes it away. Null when the row isn't laid out yet.
    /// </summary>
    Action<double>? Burst(AuraRow row)
    {
        var root = row.Root;
        if (root.ActualWidth <= 0 || !root.IsDescendantOf(_fight)) return null;
        Point at;
        try { at = root.TranslatePoint(new Point(0, 0), _fight); } catch (InvalidOperationException) { return null; }
        var plate = row.PlateIn();
        double px = at.X + plate.X, py = at.Y + plate.Y, pw = plate.Width, ph = plate.Height;
        // it rises from the plate's top edge up to the HP readout's foot (never behind the boss's figures), else the head's
        double ceil = 0;
        var over = _hpLine ?? _head;
        if (over is { ActualHeight: > 0 } && over.IsDescendantOf(_fight)) { try { ceil = over.TranslatePoint(new Point(0, over.ActualHeight), _fight).Y + 1; } catch (InvalidOperationException) { } }
        double eh = Math.Clamp(py + 3 - ceil, 0, 90);
        if (eh < 12) return null;
        var canvas = new Canvas { IsHitTestVisible = false };
        // the gold flash round the plate
        var glow = new TacSketch((dc, s) => Orb(dc, new Point(s.Width / 2, s.Height / 2), s.Width / 2, s.Height / 2, AuGoldC, .8, AuGoldHotC)) { Width = pw + 40, Height = ph + 50, Opacity = 0 };
        Canvas.SetLeft(glow, px - 20); Canvas.SetTop(glow, py - 25);
        // the eruption: tall gold tongues standing on the plate's top edge
        var erupt = new TacSketch((dc, s) =>
        {
            Flames(dc, 0, s.Width, s.Height, s.Height - 1, 41, AuGoldC, AuGoldHotC, 1, 24);
        }) { Width = pw, Height = eh };
        var es = new ScaleTransform(1, 0, 0, eh);
        erupt.RenderTransform = es;
        Canvas.SetLeft(erupt, px); Canvas.SetTop(erupt, py + 3 - eh);
        canvas.Children.Add(glow); canvas.Children.Add(erupt);
        _under.Children.Clear();
        _under.Children.Add(canvas);
        return p =>
        {
            if (p >= 1 || p == -1) { _under.Children.Remove(canvas); return; }
            if (p < 0) { glow.Opacity = 0; es.ScaleY = 0; return; }
            glow.Opacity = Math.Sin(Math.PI * Seg(p, 0, .7));
            es.ScaleY = p < .3 ? Back(Seg(p, 0, .3), 2.2) : 1;
            erupt.Opacity = 1 - Seg(p, .45, 1);
        };
    }

    // ================================================================ the moments

    /// <summary>A pickup: a light crosses the Buddy's plate and the worth rides in on a small energy chip beside it, then fades.</summary>
    protected override void Pickup(Pick p, bool held)
    {
        if (KinahHidden || _line.Visibility != Visibility.Visible) return;
        var worth = p.Worth.Length > 0 ? "+" + p.Worth.TrimStart('+') : p.Count;
        var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(6, 2, 10, 3) };
        var orbBrush = new RadialGradientBrush(Mod.Mix(KiC, Colors.White, .75), Mod.A(KiC, .0)); orbBrush.Freeze();
        row.Children.Add(new System.Windows.Shapes.Ellipse { Width = 12, Height = 12, Fill = orbBrush, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 4, 0) });
        var words = new StackPanel();
        words.Children.Add(Tt(worth, 14, p.Worth.Length > 0 ? Frozen(Mod.Mix(MintC2, Colors.White, .25)) : White, FontWeights.Bold));
        var nm = Tt(p.Name.Length > 11 ? p.Name[..10].TrimEnd() + "…" : p.Name, 9.5, Soft, FontWeights.SemiBold); nm.Margin = new Thickness(0, -2, 0, 0);
        words.Children.Add(nm);
        row.Children.Add(words);
        var chip = new Border
        {
            Child = row, MaxWidth = 88, CornerRadius = new CornerRadius(12), Background = Frozen(Mod.A(CoreBotC, .85)), BorderThickness = new Thickness(1),
            BorderBrush = Frozen(Mod.A(KiC, .8)), Effect = new DropShadowEffect { Color = KiC, ShadowDepth = 0, BlurRadius = 8, Opacity = .55 },
        };
        var shift = new TranslateTransform();
        chip.RenderTransform = shift;
        chip.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        Canvas.SetLeft(chip, 6); Canvas.SetTop(chip, -chip.DesiredSize.Height / 2 + LZ / 2);
        _lineAnchor.Children.Clear(); _lineAnchor.Children.Add(chip);
        Motion.Play(chip, 1900, t =>
        {
            _sheen = t < .5 ? Seg(t, 0, .5) : -1; _linePlate.InvalidateVisual();
            double k = Out(Seg(t, .1, .3));
            shift.X = -10 * (1 - k);
            chip.Opacity = t < .82 ? k : 1 - Seg(t, .82, 1);
        }, () => { _sheen = -1; _linePlate.InvalidateVisual(); _lineAnchor.Children.Remove(chip); });
    }

    /// <summary>A rare drop: an energy orb in the grade's colour swells and bursts behind the tile, which pops in; the plate flashes.</summary>
    protected override void RareDrop(Pick p, bool held)
    {
        if (_rareOrb is not { } orb) return;
        var tile = _rareTile;
        var flash = _rareFlash;
        var sc = new ScaleTransform(1, 1);
        if (tile != null) { tile.RenderTransformOrigin = new Point(.5, .5); tile.RenderTransform = sc; }
        var os = new ScaleTransform(.2, .2);
        orb.RenderTransformOrigin = new Point(.5, .5); orb.RenderTransform = os;
        // the big one, a power-up in its grade's colour (2026-11-21: the small orb read like a pickup): the orb gathers,
        // swells past the plate and bursts, the grade's energy surges up the whole plate, and the plate jolts with the burst
        double pw = _rare.ActualWidth > 0 ? _rare.ActualWidth : 240, ph = _rare.ActualHeight > 0 ? _rare.ActualHeight : 46;
        var surge = Surge(Mod.Mix(_rareC, Colors.White, .15), pw - 20, ph, 26, 29); surge.HorizontalAlignment = HorizontalAlignment.Left; surge.VerticalAlignment = VerticalAlignment.Bottom; surge.Margin = new Thickness(10, 0, 0, 0);
        var rise = new TranslateTransform(); surge.RenderTransform = rise;
        _rare.Children.Insert(Math.Min(_rare.Children.Count, 2), surge);
        var jolt = new TranslateTransform(); _rare.RenderTransform = jolt;
        Motion.Play(orb, 1500, t =>
        {
            // gather (a short pull in), swell, burst
            double g = Seg(t, 0, .12), k = Out(Seg(t, .12, .4));
            os.ScaleX = os.ScaleY = t < .12 ? .5 - .3 * g : .2 + 1.05 * k;
            orb.Opacity = t < .12 ? Seg(t, 0, .06) : 1 - Seg(t, .3, .7);
            if (flash != null) flash.Opacity = t < .14 ? Seg(t, .06, .14) : 1 - Seg(t, .14, .6);
            sc.ScaleX = sc.ScaleY = .5 + .5 * Back(Seg(t, .12, .45), 2.6);
            double u = Seg(t, .14, .75);
            rise.Y = -ph * .35 * Out(u);
            surge.Opacity = t < .14 ? 0 : Math.Min(1, 6 * u) * (1 - u);
            jolt.X = Ez.Jolt(Seg(t, .14, .36), 3, 5);
        }, () => { orb.Opacity = 0; if (flash != null) flash.Opacity = 0; sc.ScaleX = sc.ScaleY = 1; _rare.Children.Remove(surge); jolt.X = 0; _rare.RenderTransform = null; });
    }

    /// <summary>
    /// Your best passed, LIMIT BREAK: a white flash frame over the Buddy, a slanted gold slab slashes in across the plate's
    /// right end with the words in black italic, speed streaks tearing past behind it; the ring burns round to full gold and
    /// the Buddy's aura turns gold. The slab is gone in under a second; the gold stays while the hour is past your best.
    /// </summary>
    protected override void BestPassed(bool held)
    {
        if (_ringArt is not { } art) return;
        double was = _share is > 0 and < 1 ? _share : .86;
        bool beat = _beat;
        double lh = Math.Max(40, _line.ActualHeight > 0 ? _line.ActualHeight : 61);
        double sh = lh - LZ - 2, sw = 100;
        // the slab: gold, slanted, a dark ink edge, the words in black italic (one or two lines)
        var word = L.T("LIMIT BREAK");
        int sp = word.IndexOf(' ');
        var lines = sp > 0 ? new[] { word[..sp], word[(sp + 1)..] } : new[] { word };
        var words = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        foreach (var ln in lines)
        {
            var (sf, swt) = ShoutFace();
            var t = Tt(ln, lines.Length > 1 ? 21 : 17, Frozen(Color.FromRgb(0x12, 0x08, 0x00)), swt, sf);
            t.FontStyle = FontStyles.Italic; t.HorizontalAlignment = HorizontalAlignment.Center;
            t.Margin = new Thickness(0, -3, 0, -3);
            words.Children.Add(t);
        }
        var slab = new Grid { Width = sw, Height = sh };
        slab.Children.Add(new TacSketch((dc, s) =>
        {
            var r = new Rect(0, 0, s.Width, s.Height);
            dc.DrawRectangle(Mod.Grad(true, (AuGoldHotC, 0), (AuGoldC, .5), (Color.FromRgb(0xFF, 0x8A, 0x1E), 1)), Pn(InkC, .9, 2, false), r);
            dc.DrawRectangle(Mod.Br(Colors.White, .85), null, new Rect(1, 1, s.Width - 2, 2));
        }));
        slab.Children.Add(words);
        var slabX = new TranslateTransform();
        var tg = new TransformGroup(); tg.Children.Add(new SkewTransform(-12, 0, sw / 2, sh / 2)); tg.Children.Add(slabX);
        slab.RenderTransform = tg;
        Canvas.SetLeft(slab, -16); Canvas.SetTop(slab, -sh / 2 + LZ / 2 + 1);
        // the speed streaks behind it: thin tapered gold-white lines tearing left, drawn once, inside the line's height
        var streaks = new TacSketch((dc, s) =>
        {
            var rnd = new Random(5);
            for (int i = 0; i < 9; i++)
            {
                double y = 3 + (s.Height - 6) * (i + .5) / 9 + (rnd.NextDouble() - .5) * 3, len = 30 + 50 * rnd.NextDouble(), x = s.Width - len - 10 * rnd.NextDouble();
                dc.DrawRectangle(Mod.Grad(false, (Mod.A(AuGoldHotC, 0), 0), (Mod.A(i % 3 == 0 ? Colors.White : AuGoldC, .9), 1)), null, new Rect(x, y, len, i % 3 == 0 ? 2 : 1.2));
            }
        }) { Width = 104, Height = sh, Opacity = 0 };
        var stX = new TranslateTransform();
        streaks.RenderTransform = stX;
        Canvas.SetLeft(streaks, -20); Canvas.SetTop(streaks, -sh / 2 + LZ / 2 + 1);
        _lineAnchor.Children.Clear(); _lineAnchor.Children.Add(streaks); _lineAnchor.Children.Add(slab);
        Motion.Play(art, 1400, t =>
        {
            _beat = false; _share = was;
            _complete = Out(Seg(t, 0, .3));
            if (t >= .3) { _beat = beat; _complete = 1; }
            art.InvalidateVisual();
            _lineGold.Opacity = Out(Seg(t, .12, .35));
            _lineFlash.Opacity = t < .04 ? .32 * Seg(t, 0, .04) : .32 * (1 - Seg(t, .04, .2));
            double i = Out(Seg(t, 0, .09)), o = Seg(t, .6, .7);
            slabX.X = 46 * (1 - i) - 30 * o;
            slab.Opacity = Math.Min(1, Seg(t, 0, .04) * 1.5) * (1 - o);
            stX.X = -40 * Seg(t, 0, .5);
            streaks.Opacity = t < .05 ? Seg(t, 0, .05) : 1 - Seg(t, .25, .55);
        }, () => { _complete = 0; _beat = beat; art.InvalidateVisual(); _lineGold.Opacity = beat ? 1 : 0; _lineFlash.Opacity = 0; _lineAnchor.Children.Remove(slab); _lineAnchor.Children.Remove(streaks); });
    }

    /// <summary>
    /// Energy rising (drawn once; a moment lifts and fades it): tapered streaks of <paramref name="c"/>, white-hot at their
    /// heads, spread across <paramref name="w"/> and climbing <paramref name="h"/>.
    /// </summary>
    static TacSketch Surge(Color c, double w, double h, int n, int seed) => new((dc, s) =>
    {
        var rnd = new Random(seed);
        var hot = Mod.Mix(c, Colors.White, .7);
        for (int k = 0; k < n; k++)
        {
            double x = s.Width * (k + .5) / n + (rnd.NextDouble() - .5) * s.Width / n, len = s.Height * (.45 + .5 * rnd.NextDouble()), y = s.Height - len - rnd.NextDouble() * s.Height * .2;
            double th = k % 3 == 0 ? 2 : 1.3;
            dc.DrawRectangle(Mod.Grad(true, (Mod.A(hot, .95), 0), (Mod.A(c, .7), .35), (Mod.A(c, 0), 1)), null, new Rect(x - th / 2, Math.Max(0, y), th, len));
        }
    }, w, h) { IsHitTestVisible = false, Opacity = 0 };

    static readonly Brush AuraEntryFlood = Mod.Grad(true, (Mod.A(Mod.Mix(MintC2, Colors.White, .5), .75), 0), (Mod.A(MintC2, .5), .6), (Mod.A(MintC2, .3), 1));

    /// <summary>
    /// An entry opened, a power-up: the first timer's chip draws in, then flares out past its size as mint energy surges up
    /// off it and a ring of mint light bursts round it; the chip settles with its edge lit mint.
    /// </summary>
    protected override void EntryOpened(TimerBoard.Item i, bool held)
    {
        if (_firstChip is not { } chip || chip.Child is not FrameworkElement words) return;
        chip.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        double cw = Math.Max(40, chip.DesiredSize.Width), ch = Math.Max(14, chip.DesiredSize.Height);
        var edge = chip.BorderBrush;
        chip.BorderBrush = Frozen(MintC2);
        // the surge and the ring, laid round the chip (its words over them)
        var surge = Surge(MintC2, cw - 6, ch, 14, 17); surge.HorizontalAlignment = HorizontalAlignment.Center; surge.VerticalAlignment = VerticalAlignment.Bottom;
        var rise = new TranslateTransform(); surge.RenderTransform = rise;
        var ring = new System.Windows.Shapes.Rectangle { RadiusX = 6, RadiusY = 6, Stroke = Frozen(Mod.Mix(MintC2, Colors.White, .4)), StrokeThickness = 2, Opacity = 0, RenderTransformOrigin = new Point(.5, .5) };
        var ringSc = new ScaleTransform(1, 1); ring.RenderTransform = ringSc;
        chip.Child = null;
        var inner = new Border { Padding = chip.Padding, Child = words };
        var g = new Grid { Margin = new Thickness(-chip.Padding.Left, -chip.Padding.Top, -chip.Padding.Right, -chip.Padding.Bottom) };
        surge.Margin = new Thickness(0, 0, 0, 2);
        g.Children.Add(ring); g.Children.Add(surge); g.Children.Add(inner);
        chip.Child = g;
        var sc = new ScaleTransform(1, 1);
        chip.RenderTransformOrigin = new Point(.5, .5); chip.RenderTransform = sc;
        // the flare: the chip floods mint as it bursts (a brush swapped twice, never each frame)
        var bg = chip.Background; bool? lit = null;
        Motion.Play(chip, 1200, t =>
        {
            bool on = t >= .14 && t < .32;
            if (lit != on) { lit = on; chip.Background = on ? AuraEntryFlood : bg; }
            double wind = Seg(t, 0, .14), pop = Ez.Wob(Seg(t, .14, .6), 1.5, 4), r = Out(Seg(t, .14, .2));
            sc.ScaleX = sc.ScaleY = t < .14 ? 1 - .12 * Ez.InOut(wind) : .88 + .12 * r + .22 * pop;
            double u = Seg(t, .14, .7);
            rise.Y = -ch * .6 * Out(u);   // (the line often sits at the window's top)
            surge.Opacity = t < .14 ? 0 : Math.Min(1, 5 * u) * (1 - u);
            double q = Seg(t, .16, .62);
            ringSc.ScaleX = 1 + .25 * Out(q); ringSc.ScaleY = 1 + .9 * Out(q);
            ring.Opacity = t < .16 ? 0 : 1 - q;
        }, () =>
        {
            chip.RenderTransform = null; chip.BorderBrush = edge; chip.Background = bg;
            inner.Child = null; chip.Child = words;
        });
    }

    /// <summary>
    /// A fight started: every aura ignites bottom-up, the gauges filling from nothing with a flash and a surge along them;
    /// then the leader powers up, the gold aura erupting behind the meter with its ring and strikes.
    /// </summary>
    protected override void FightStarted(Encounter e, bool held)
    {
        var rows = RowsNow();
        var lead = Meter is { Members.Count: > 0 } m && m.Members[0] is { Rank: 1, CpOnly: false } && rows.Count > 0 ? rows[0] : null;
        var burst = lead != null ? Burst(lead) : null;
        // the charge (2026-11-21 review: it popped in with nothing before it): the panel gathers in small and dim for a beat,
        // then bursts out past its size as the auras ignite, settling square
        var pull = new ScaleTransform(1, 1);
        var was = _fight.RenderTransform;
        _fight.RenderTransformOrigin = new Point(.5, .4);
        _fight.RenderTransform = new TransformGroup { Children = { pull, _fightShift } };
        Motion.Play(_fight, 1500, t =>
        {
            double c = Seg(t, 0, .07), k = Out(Seg(t, .07, .16));
            pull.ScaleX = pull.ScaleY = t < .07 ? 1 - .05 * Ez.InOut(c) : .95 + .05 * k + .015 * Ez.Wob(Seg(t, .07, .4), 1.5, 4);
            _fightShift.Y = t < .07 ? 3 * c : 3 * (1 - k);
            _fightBody.Opacity = t < .07 ? .3 * c : .3 + .7 * k;
            for (int i = 0; i < rows.Count; i++)
            {
                int j = rows.Count - 1 - i;
                rows[i].Ignite(Seg(t, .07 + .035 * j, .33 + .035 * j));
            }
            if (lead != null && t >= .3) lead.PowerUp(Seg(t, .3, 1));
            burst?.Invoke(t < .3 ? -2 : Seg(t, .3, 1) >= 1 ? .999 : Seg(t, .3, 1));
        }, () =>
        {
            _fightShift.Y = 0; _fightBody.Opacity = 1; pull.ScaleX = pull.ScaleY = 1; _fight.RenderTransform = was;
            foreach (var r in rows) r.Ignite(-1);
            lead?.PowerUp(-1);
            burst?.Invoke(-1);
        });
    }

    /// <summary>
    /// A fight ended: a dark energy band lands over the fight's head with how it went, the clock beside it, gold flames off
    /// its top (a wipe's band in red, its flames out); a kill flares every aura and settles, a wipe gutters them. It lifts after a moment.
    /// </summary>
    protected override void FightEnded(Encounter e, bool held)
    {
        if (e.Outcome is not ("kill" or "wipe" or "cleared")) return;
        bool wipe = e.Outcome == "wipe";
        var c = wipe ? WipeC : AuGoldC;
        var band = new Grid { Height = 42, Margin = new Thickness(0, -7, 0, 0), VerticalAlignment = VerticalAlignment.Top };
        band.Children.Add(new TacSketch((dc, s) =>
        {
            if (s.Width < 30) return;
            if (!wipe) Flames(dc, 10, s.Width - 3, 12, 12, 61, AuGoldC, AuGoldHotC, 1, 16);
            CorePlate(dc, new Rect(1, 9, s.Width - 2, s.Height - 10), 8, c, 1, c, .9, .94);
            dc.PushClip(Plate(new Rect(1, 9, s.Width - 2, s.Height - 10), 8));
            dc.DrawRectangle(Mod.Grad(false, (Mod.A(c, .0), 0), (Mod.A(c, .45), .5), (Mod.A(c, 0), 1)), null, new Rect(0, s.Height - 8, s.Width, 7));
            dc.DrawRectangle(Mod.Grad(false, (Mod.A(Colors.White, 0), 0), (Mod.A(Mod.Mix(c, Colors.White, .6), 1), .5), (Mod.A(Colors.White, 0), 1)), null, new Rect(0, s.Height - 2.6, s.Width, 1.4));
            dc.Pop();
        }));
        var words = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(14, 9, 0, -1), VerticalAlignment = VerticalAlignment.Center };
        var (of, ow) = ShoutFace();
        var ot = Tt(OutcomeText(e), 26, wipe ? Frozen(Color.FromRgb(0xFF, 0xD0, 0xD4)) : Frozen(AuGoldHotC), ow, of);
        ot.FontStyle = FontStyles.Italic;
        words.Children.Add(new TacHalo(ot, InkC, 3));
        var ck = Tt("  " + Clock(e.Duration(e.End ?? DateTime.UtcNow)), 15, Frozen(Mod.Mix(c, Colors.White, .55)), FontWeights.Bold); ck.Margin = new Thickness(4, 3, 0, 0);
        words.Children.Add(ck);
        words.Effect = Shade();
        band.Children.Add(words);
        var shift = new TranslateTransform();
        band.RenderTransform = shift;
        _fightFx.Children.Clear(); _fightFx.Children.Add(band);
        var head = _head;
        var rows = RowsNow();
        Motion.Play(_fightFx, 2400, t =>
        {
            // the head gone before the band comes, back once it has left: their words never cross
            double i = Seg(t, .04, .12), o = Seg(t, .84, .92);
            band.Opacity = t < .04 ? 0 : Math.Min(1, i * 2) * (1 - o);
            shift.X = -14 * (1 - Out(i));
            if (head != null) head.Opacity = t < .5 ? 1 - Seg(t, 0, .04) : Seg(t, .92, 1);
            for (int r = 0; r < rows.Count; r++)
            {
                double p = Seg(t, .04 + .05 * r, .6 + .05 * r);
                if (wipe) rows[r].Gutter(p); else rows[r].Flare(p);
            }
        }, () =>
        {
            _fightFx.Children.Clear(); if (head != null) head.Opacity = 1;
            foreach (var r in rows) { if (wipe) r.Gutter(-1); else r.Flare(-1); }
        });
    }
}
