using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using KinahLedger.Parser;
using KinahLedger.Session;
using static KinahLedger.Ui.HudKit;

namespace KinahLedger.Ui;

/// <summary>
/// Dragon (the Overdrive theme; Steven, 2026-11-20: "at least 1 theme should be WILD"): a Balaur dragon holds the meter. Its
/// horned head rests at the top-left beside the fight's title, its neck runs down the left side, its scaled obsidian body
/// round the foot and up the right side, and its tail back along the top to its spade by the horns: the coil is the panel.
/// Scales rimmed in a bronze-gold metallic sheen, an emerald glow along the belly, an obsidian ground inside. Every bar is
/// its fire: a class-coloured stream out of its side with a hot core and a flame front of three tongues; the leader's rank
/// sits on a molten gold scale crest and the dragon's slit eye glows toward them.
/// The Buddy is the dragon smaller, coiled round the hour like a hoard's guardian, and the ring of your best an ouroboros
/// whose molten scales fill to your share. The coils are vector art drawn once per size (HudDragon.Art.cs).
/// The moments: a fight starting snaps the eye open and races fire down the bars; a new leader makes the head flare and
/// breathe a burst of fire along their row (under the words) as their crest lands; a kill is a roar, shockwaves out of the
/// jaws and embers under a slammed "Killed" plate; your best passed runs gold fire round the ouroboros; a rare drop glints
/// on the hoard.
/// </summary>
sealed partial class HudDragon : ModernLook
{
    static readonly Color MintC2 = Color.FromRgb(0x5D, 0xF2, 0xA6), MarkMintC = Color.FromRgb(0x7D, 0xFF, 0xC0),
        CoralC = Color.FromRgb(0xFF, 0x86, 0x86), HpC = Color.FromRgb(0xFF, 0x4E, 0x3A), DeepC = Color.FromRgb(0x0B, 0x0A, 0x0E),
        WarnC2 = Color.FromRgb(0xFF, 0xB0, 0x86), GoldC2 = Color.FromRgb(0xFF, 0xCF, 0x6A);
    static readonly SolidColorBrush White = Frozen(Colors.White), Soft = Frozen(Mod.A(Colors.White, .84)), Quiet = Frozen(Mod.A(Colors.White, .62)),
        Mint2 = Frozen(Color.FromRgb(0x8C, 0xFF, 0xC6)), MarkMint = Frozen(MarkMintC), Coral = Frozen(CoralC), BronzeInk = Frozen(Color.FromRgb(0xE2, 0xC4, 0x8A));

    // the room round the look; 98 at the right for the pickup's chip beside the Buddy
    readonly Grid _root = new() { Margin = new Thickness(4, 6, 98, 8), HorizontalAlignment = HorizontalAlignment.Left };
    readonly StackPanel _body = new() { HorizontalAlignment = HorizontalAlignment.Left };
    double _opacity = 0.9;

    // the Buddy: the small dragon's coil, the ouroboros, the hour, the session and the timer chips
    readonly Grid _line = new() { HorizontalAlignment = HorizontalAlignment.Left };
    readonly TacSketch _lineArt;
    readonly StackPanel _lineBody = new() { Orientation = Orientation.Horizontal, Margin = new Thickness(8, 12, 18, 12) };
    readonly Canvas _lineFx = new() { IsHitTestVisible = false, ClipToBounds = true };
    readonly Canvas _lineAnchor = Tac.Anchor(HorizontalAlignment.Right, VerticalAlignment.Center);
    /// <summary>The timer chips; beside the meter's head they wrap to a second line before any is cut.</summary>
    readonly WrapPanel _timers = new() { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
    readonly Border _timersInLine = new() { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 0, 0) };
    /// <summary>The timer chips over the line, when the pickup strip takes their room on it.</summary>
    readonly Border _timersUp = new() { HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(6, 0, 0, 4), Visibility = Visibility.Collapsed };
    /// <summary>The pickup strip: 24 px tiles beside the hour, a bronze rule, a gold star.</summary>
    static readonly PickStrip.Look Strip = new(24, 4, true, Frozen(Mod.A(Color.FromRgb(0xE2, 0xC4, 0x8A), .4)), Frozen(Color.FromRgb(0xFF, 0xCF, 0x6A)));
    bool _dim, _lineHead;
    TacSketch? _ringArt;
    Grid? _ringHost;
    double _share = -1, _complete;
    bool _beat;
    Border? _firstChip;
    /// <summary>The hour's figure on the line (a best passed punches it).</summary>
    TextBlock? _rate;
    readonly TranslateTransform _fightShift = new();

    // the rare drop under the Buddy (on the hoard), and the notice line
    readonly Grid _rare = new() { HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 3, 0, 0), Visibility = Visibility.Collapsed };
    readonly StackPanel _note = new() { Orientation = Orientation.Horizontal, Margin = new Thickness(12, 5, 0, 0), Visibility = Visibility.Collapsed };

    // the fight: the coil and its ground, the moments under the words, the eye, the words, the plate over the head
    readonly Grid _fight = new() { HorizontalAlignment = HorizontalAlignment.Left, Visibility = Visibility.Collapsed, Margin = new Thickness(0, 0, 0, 6) };
    readonly TacSketch _fightArt;
    readonly Canvas _fightUnder = new() { IsHitTestVisible = false, ClipToBounds = true };
    /// <summary>The dragon ablaze (FightBlaze), flashed by a moment: drawn once per colour, only its opacity moves.</summary>
    readonly TacSketch _blaze;
    Color _blazeC = DragonGoldC;
    /// <summary>The shockwaves and embers over the meter (a kill's roar), clipped to it.</summary>
    readonly Canvas _fightOver = new() { IsHitTestVisible = false, ClipToBounds = true };
    readonly Canvas _eye = new() { IsHitTestVisible = false };
    readonly StackPanel _fightBody = new() { Margin = FightPad };
    /// <summary>The words over the rows (timers, title, HP), never shorter than the head's jaws reach: the rows start under them.</summary>
    readonly StackPanel _fightTop = new() { MinHeight = HeadFloor - FightPad.Top };
    readonly Border _timersInFight = new() { Margin = new Thickness(TitleX - 26, 0, 0, 6), HorizontalAlignment = HorizontalAlignment.Left };
    readonly Grid _fightFx = new() { IsHitTestVisible = false, VerticalAlignment = VerticalAlignment.Top };
    readonly Ellipse _eyeFlare, _mouthGlow;
    readonly System.Windows.Shapes.Path _lid;
    readonly ScaleTransform _lidScale = new(1, 0);
    FrameworkElement? _head;

    public HudDragon() : base(bare: true)
    {
        // the look's own rows: each bar the dragon's fire
        UseRows(DragonRow.Pitch, m => new DragonRow(this), 15, GapLine);
        _lineArt = new TacSketch((dc, s) => { if (s.Width > 20 && s.Height > 12) dc.DrawDrawing(LineCoil(Math.Floor(s.Width), Math.Floor(s.Height), GroundK, _lineHead, _dim)); });
        _line.Children.Add(_lineArt); _line.Children.Add(_lineFx); _line.Children.Add(Lit(_lineBody)); _line.Children.Add(_lineAnchor);

        _fightArt = new TacSketch((dc, s) => { if (s.Width > 60 && s.Height > 40) dc.DrawDrawing(FightCoil(Math.Floor(s.Width), Math.Floor(s.Height), GroundK)); });
        // the coils are drawn once per size and kept as pictures, so a moment's frames never draw their scales again
        _fightArt.CacheMode = new BitmapCache(1.25) { SnapsToDevicePixels = true };
        _lineArt.CacheMode = new BitmapCache(1.25) { SnapsToDevicePixels = true };
        // the eye's flare, its lid, the glow in the jaws (at the head's own points on the meter)
        var eye = new Point(FightHeadX + EyeAt.X * FightHeadK, FightHeadY + EyeAt.Y * FightHeadK);
        var mouth = new Point(FightHeadX + MouthAt.X * FightHeadK, FightHeadY + MouthAt.Y * FightHeadK);
        _eyeFlare = new Ellipse { Width = 40, Height = 26, Fill = Glow(DragonGoldC, .95), Opacity = 0 };
        Canvas.SetLeft(_eyeFlare, eye.X - 20); Canvas.SetTop(_eyeFlare, eye.Y - 13);
        _mouthGlow = new Ellipse { Width = 56, Height = 44, Fill = Glow(HotC, .95), Opacity = 0 };
        Canvas.SetLeft(_mouthGlow, mouth.X - 30); Canvas.SetTop(_mouthGlow, mouth.Y - 22);
        var lidGeo = G(LidPath).Clone();
        lidGeo.Transform = new MatrixTransform(FightHeadK, 0, 0, FightHeadK, FightHeadX, FightHeadY); lidGeo.Freeze();
        _lid = new System.Windows.Shapes.Path { Data = lidGeo, Fill = Mod.Grad(true, (Color.FromRgb(0x46, 0x2C, 0x2C), 0), (Color.FromRgb(0x16, 0x0A, 0x0C), 1)), Stroke = Frozen(Mod.A(BronzeC, .7)), StrokeThickness = .6 };
        _lidScale.CenterY = FightHeadY + LidTop * FightHeadK;
        _lid.RenderTransform = _lidScale;
        _eye.Children.Add(_eyeFlare); _eye.Children.Add(_mouthGlow); _eye.Children.Add(_lid);
        _blaze = new TacSketch((dc, s) => { if (s.Width > 60 && s.Height > 40) dc.DrawDrawing(FightBlaze(Math.Floor(s.Width), Math.Floor(s.Height), _blazeC)); }) { Opacity = 0, ClipToBounds = true };
        _fight.Children.Add(_fightArt); _fight.Children.Add(_blaze); _fight.Children.Add(_fightUnder); _fight.Children.Add(_eye); _fight.Children.Add(_fightBody); _fight.Children.Add(_fightOver); _fight.Children.Add(_fightFx);
        _fight.RenderTransform = _fightShift;   // a kill's jolt, a wipe's thud

        _runHeadLit = Lit(_runHead); _runHeadLit.Visibility = Visibility.Collapsed;
        _body.Children.Add(_fight); _body.Children.Add(Lit(_timersUp)); _body.Children.Add(_runHeadLit); _body.Children.Add(_line); _body.Children.Add(_rare); _body.Children.Add(Lit(_note));
        _root.Children.Add(_body);
        System.Windows.Documents.TextElement.SetFontFamily(_root, Mod.Face);
        TextOptions.SetTextFormattingMode(_root, TextFormattingMode.Display);
    }

    public override FrameworkElement Root => _root;
    public override Thickness Inset => _root.Margin;
    public override double Corner => 12;
    public override FrameworkElement? FightFrame => _fight.Visibility == Visibility.Visible ? _fight : null;

    public override void SetOpacity(double opacity)
    {
        // the ground under the coils takes the setting (never so faint the words lose their dark)
        _opacity = Math.Clamp(opacity, 0.3, 1);
        _lineArt.InvalidateVisual(); _fightArt.InvalidateVisual();
    }

    /// <summary>The obsidian ground's strength from the opacity setting: .9 at the usual 90%, never under .62.</summary>
    double GroundK => Math.Round(Math.Clamp(.55 + .4 * _opacity, .62, .96) * 20) / 20;

    protected override string LineSig(Figures f) => base.LineSig(f) + "|" + (Rare(Values)?.Id ?? "") + "|" + (Picks(Values).FirstOrDefault()?.Id ?? "") + "|" + Dimmed(Values) + "|" + PickStrip.Sig(Values, Strip) + "|" + RunBuddy.Sig(Values);

    static bool Dimmed(IReadOnlyDictionary<string, string> v) => (v.TryGetValue("show", out var s) && s == "dim") || (v.TryGetValue("paused", out var p) && p.Length > 0);

    // ================================================================ the shade

    /// <summary>The tight shade under the words: 1 px down, 3 px soft, black (the ground behind them is dark already).</summary>
    static DropShadowEffect Shade() => new() { Color = Colors.Black, Direction = 270, ShadowDepth = 1, BlurRadius = 3, Opacity = .95 };

    /// <summary>Words on the obsidian: the tight shade on them.</summary>
    static Border Lit(FrameworkElement el)
    {
        el.Effect = Shade();
        return new Border { Child = el, HorizontalAlignment = el.HorizontalAlignment, VerticalAlignment = el.VerticalAlignment };
    }

    /// <summary>A small obsidian chip rimmed in bronze (TOP DPS, the heal mark, AVG); <paramref name="filled"/> ivory with dark words (YOU).</summary>
    static Border Mark(string s, Brush? fg = null, Color? edge = null, bool filled = false, double size = 8.5)
    {
        var t = Tt(s, size, filled ? Frozen(Color.FromRgb(0x1A, 0x12, 0x08)) : fg ?? White, FontWeights.Bold);
        return new Border
        {
            Child = t, CornerRadius = new CornerRadius(5, 1, 5, 1), Padding = new Thickness(4, 0, 4, 1), VerticalAlignment = VerticalAlignment.Center,
            Background = filled ? IvoryInk : Frozen(Mod.A(DeepC, .82)), BorderThickness = new Thickness(1),
            BorderBrush = filled ? Frozen(Color.FromRgb(0xFF, 0xF4, 0xD8)) : Frozen(Mod.A(edge ?? BronzeC, edge == null ? .8 : .7)),
        };
    }

    /// <summary>A bronze rule across, fading at its ends (the foot's, the card's).</summary>
    static Border Hairline(Thickness m, double a = .5) => new()
    {
        Height = 1, Margin = m, IsHitTestVisible = false,
        Background = Mod.Grad(false, (Mod.A(BronzeC, 0), 0), (Mod.A(BronzeC, a), .2), (Mod.A(Color.FromRgb(0xF2, 0xD4, 0x92), a), .5), (Mod.A(BronzeC, a), .8), (Mod.A(BronzeC, 0), 1)),
    };

    // ================================================================ the Buddy

    protected override void DrawLine(Figures f)
    {
        _lineBody.Children.Clear();
        _ringArt = null; _ringHost = null; _rate = null; _timerUp = false; _stripTimerW = -1;
        if (_timersInLine.Child != null) _timersInLine.Child = null;
        _dim = Dimmed(Values);
        _line.Opacity = _dim ? .7 : 1;
        bool mark = f.Abyss && f.Counted && f.Icon.Length > 0;
        bool ring = false;
        if (mark) { var m = AbyssMark(f.Icon, 36); m.Margin = new Thickness(4, 0, 9, 0); _lineBody.Children.Add(m); }
        // in a dungeon run: the run's clock, splits and verdict in place of the hour (HudDragon.Run.cs)
        var rs = RunNow();
        _runHeadLit.Visibility = Visibility.Collapsed;
        if (rs != null) { _lineBody.Children.Add(new Border { Width = 30 }); DrawRun(rs); }
        else if (!HasHour(f))
        {
            var w = Tt(Waiting(f.Abyss), 12.5, Soft, FontWeights.SemiBold); w.Margin = new Thickness(mark ? 0 : 34, 10, 4, 6);
            // the kills and the Kinah so far under it (Steven, 2026-11-21: information, not emptiness)
            var stats = Sub(f, false);
            if (stats.Inlines.Count > 0)
            {
                w.Margin = new Thickness(mark ? 0 : 34, 3, 4, 0);
                stats.Margin = new Thickness(mark ? 1 : 35, 1, 4, 2);
                _lineBody.Children.Add(new StackPanel { VerticalAlignment = VerticalAlignment.Center, Children = { w, stats } });
            }
            else _lineBody.Children.Add(w);
        }
        else
        {
            if (HasRing(f))
            {
                ring = true;
                _share = f.Share; _beat = f.Beat; _complete = 0;
                var host = new Grid { Width = 52, Height = 52, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(-3, -4, 9, -6) };
                _ringArt = new TacSketch(DrawRing, 52, 52);
                host.Children.Add(_ringArt);
                var pc = Tt(f.Beat ? "★" : Pct(f.Share), 12, f.Beat ? GoldInk : White, FontWeights.Bold); pc.HorizontalAlignment = HorizontalAlignment.Center; pc.Margin = new Thickness(0, 1, 0, 0);
                host.Children.Add(pc);
                _ringHost = host;
                _lineBody.Children.Add(host);
            }
            else if (!mark) _lineBody.Children.Add(new Border { Width = 32 });
            // the hour big, its unit small; the session and the latest pickup under it
            var words = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 1, 0, 1) };
            var top = new StackPanel { Orientation = Orientation.Horizontal };
            var rate = Tt(f.Rate, f.Warm ? 15 : 22, f.Warm ? Soft : White, f.Warm ? FontWeights.SemiBold : FontWeights.Bold);
            rate.Tag = "buddy-rate";
            if (!f.Warm) _rate = rate;
            top.Children.Add(rate);
            if (f.Unit.Length > 0) { var u = Tt(f.Unit, 11, BronzeInk, FontWeights.SemiBold); u.VerticalAlignment = VerticalAlignment.Bottom; u.Margin = new Thickness(2, 0, 0, 4); top.Children.Add(u); }
            words.Children.Add(top);
            // farming, the pickups as the strip beside the hour where it fits (the best drop and the last three), the session
            // alone under it; with the timer chips at the line's end and no room left beside them, the chips go over the line
            var sub = Sub(f, true);
            FrameworkElement? strip = null, under = null;
            if (StripTime(f))
            {
                var bare = Sub(f, false);
                double baseW = _lineBody.Margin.Left + _lineBody.Margin.Right + _lineBody.Children.OfType<FrameworkElement>().Sum(e => Mod.W(e) + e.Margin.Left + e.Margin.Right), timerW = LineTimerW();
                double wordsW = Math.Max(Mod.W(top), Mod.W(bare)), oldW = Math.Max(Mod.W(top), Mod.W(sub));
                foreach (var look in StripFall(Strip))
                {
                    if ((strip = StripIn(StripRoom(FightWidth - 6, baseW + timerW, wordsW, oldW, 12), look)) != null) break;
                    if (timerW > 0 && (strip = StripIn(StripRoom(FightWidth - 6, baseW, wordsW, oldW + timerW, 12), look)) != null) { _timerUp = true; break; }
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
        _lineHead = !ring && !mark;
        _lineBody.Children.Add(_timersInLine);
        PlaceTimers();
        _lineArt.InvalidateVisual();

        // the rare drop the window holds: its tile on the dragon's hoard, RARE DROP and its name in its grade's colour
        _rare.Children.Clear();
        _rareFx = null; _rareTop = null; _rareTile = null;
        if (!KinahHidden && Rare(Values) is { } r)
        {
            var c = ColorOf(r.Color, r.Grade);
            _rareC = c;
            _rare.Children.Add(new TacSketch((dc, s) => Hoard(dc, s, c, GroundK)));
            var fx = new Canvas { IsHitTestVisible = false, ClipToBounds = true };
            _rareFx = fx;
            _rare.Children.Add(fx);
            _rareTop = new Canvas { IsHitTestVisible = false };
            var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(8, 6, 16, 6) };
            var tile = Tile(r, 26); tile.Margin = new Thickness(0, 0, 9, 0);
            _rareTile = tile;
            row.Children.Add(tile);
            var mk = Mark(L.T("RARE DROP"), GoldInk, DragonGoldC, size: 8); mk.Margin = new Thickness(0, 0, 7, 0);
            row.Children.Add(mk);
            var nm = Tt(r.Name, 13, Frozen(Mod.Mix(c, Colors.White, .45)), FontWeights.Bold);
            row.Children.Add(nm);
            if (r.Count.Length > 0) { var cn = Tt(" " + r.Count, 11, Soft, FontWeights.SemiBold); cn.Margin = new Thickness(2, 1, 0, 0); row.Children.Add(cn); }
            _rare.Children.Add(Lit(row));
            _rare.Children.Add(_rareTop);
            _rare.Visibility = Visibility.Visible;
        }
        else _rare.Visibility = Visibility.Collapsed;

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
        void Dot() { if (sub.Inlines.Count > 0) sub.Inlines.Add(new System.Windows.Documents.Run("  ·  ") { Foreground = BronzeInk }); }
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
    Canvas? _rareFx, _rareTop;
    FrameworkElement? _rareTile;

    /// <summary>The hoard under a rare drop: an obsidian plate washed in the grade's colour from the left, a heap of gold coins behind the tile, a gold rim.</summary>
    static void Hoard(DrawingContext dc, Size s, Color c, double ground)
    {
        if (s.Width < 20 || s.Height < 10) return;
        var r = new Rect(.5, .5, s.Width - 1, s.Height - 1);
        var shape = Mod.Pill(r, 9);
        dc.DrawGeometry(Frozen(Mod.A(DeepC, ground)), null, shape);
        dc.DrawGeometry(Mod.Grad(false, (Mod.A(c, .42), 0), (Mod.A(c, .16), .45), (Mod.A(c, 0), 1)), null, shape);
        // the coins: a heap of gold rims under and behind the tile
        dc.PushClip(shape);
        var coin = Mod.Grad(true, (Color.FromRgb(0xFF, 0xE6, 0x9A), 0), (DragonGoldC, .5), (Color.FromRgb(0x9A, 0x5E, 0x10), 1));
        var edge = Mod.P(Frozen(Color.FromArgb(0xCC, 0x4A, 0x2A, 0x04)), .6);
        foreach (var (x, y, rr) in new[] { (3.0, s.Height - 4, 7.0), (13, s.Height - 1, 7.5), (25, s.Height - 2, 7.0), (37, s.Height - .5, 7.0), (48, s.Height - 2.5, 6.0), (58, s.Height - .5, 5.0), (5, s.Height - 12, 6.5), (17, s.Height - 10, 7.0), (33, s.Height - 11, 6.5), (45, s.Height - 9, 5.5), (9, s.Height - 20, 5.5), (25, s.Height - 19, 6.0), (39, s.Height - 18, 5.0), (17, s.Height - 27, 4.5), (32, s.Height - 26, 4.5), (24, s.Height - 33, 4.0) })
            dc.DrawEllipse(coin, edge, new Point(x, y), rr, rr * .55);
        dc.Pop();
        dc.DrawGeometry(null, Mod.P(Mod.Grad(false, (Mod.A(DragonGoldC, .9), 0), (Mod.A(BronzeC, .5), .5), (Mod.A(BronzeC, .15), 1)), 1), shape);
    }

    /// <summary>The ouroboros: your best's share in molten gold scales (all of it once beaten, gold fire running round on the moment).</summary>
    void DrawRing(DrawingContext dc, Size s)
    {
        double share = Math.Clamp(_beat ? 1 : _share, 0, 1);
        double shown = share + (1 - share) * Math.Clamp(_complete, 0, 1);
        Ouroboros(dc, s.Width, shown, _beat || _complete >= 1 ? Color.FromRgb(0xFF, 0xD2, 0x5A) : DragonGoldC);
    }

    protected override void KinahShown(bool shown)
    {
        _line.Visibility = shown ? Visibility.Visible : Visibility.Collapsed;
        if (!shown) { _note.Visibility = Visibility.Collapsed; _rare.Visibility = Visibility.Collapsed; }
        _fight.Margin = new Thickness(0, 0, 0, shown ? 6 : 0);
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
            var c = tone == TimerBadge.Tone.On ? EmeraldC : tone == TimerBadge.Tone.Soon ? DragonGoldC : Color.FromRgb(0xC8, 0xB4, 0x90);
            var sp = new StackPanel { Orientation = Orientation.Horizontal };
            // a small scale for the dot
            sp.Children.Add(new System.Windows.Shapes.Path
            {
                Data = G("M 0,1 Q 3,-0.6 6,1 Q 6,5 3,7.5 Q 0,5 0,1 Z"), Fill = Frozen(c), Stretch = Stretch.Fill, Width = first ? 6.5 : 5.5, Height = first ? 8 : 7,
                VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0),
            });
            var t = Tt(text, first ? 11.5 : 10, first ? White : Soft, first ? FontWeights.Bold : FontWeights.SemiBold);
            if (tone == TimerBadge.Tone.On) t.Foreground = EmeraldInk;
            sp.Children.Add(t);
            var chip = new Border
            {
                Child = sp, CornerRadius = new CornerRadius(9, 2, 9, 2), Padding = first ? new Thickness(8, 3, 10, 4) : new Thickness(7, 2, 9, 3), VerticalAlignment = VerticalAlignment.Center,
                Background = Frozen(Mod.A(DeepC, .86)), BorderThickness = new Thickness(1), BorderBrush = Frozen(Mod.A(tone == TimerBadge.Tone.On ? EmeraldC : BronzeC, tone == TimerBadge.Tone.On ? .75 : .6)),
                Margin = new Thickness(first ? 0 : 5, 1.5, 0, 1.5), Tag = first ? "timer-chip" : null,
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

    /// <summary>The rows' width inside the coil.</summary>
    double RowsW => FightWidth - 6 - FightPad.Left - FightPad.Right;

    protected override void DrawFight(Encounter? e, DateTime now)
    {
        _fightBody.Children.Clear(); _fightTop.Children.Clear();
        if (e == null) { _fight.Visibility = Visibility.Collapsed; _fightFx.Children.Clear(); _fightUnder.Children.Clear(); _fightOver.Children.Clear(); PlaceTimers(); return; }
        double w = FightWidth - 6;
        _fight.Width = w;
        _fightBody.Children.Add(_fightTop);
        _fightTop.Children.Add(_timersInFight);
        // the head row: the title beside the dragon's head, the outcome once it's over, the clock
        var head = new Grid { Margin = new Thickness(TitleX - FightPad.Left, 0, 0, 0), MinHeight = 22 };
        head.ColumnDefinitions.Add(new ColumnDefinition()); head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var title = Tt(e.Title, 14, Frozen(Color.FromRgb(0xFF, 0xF4, 0xDE)), FontWeights.Bold, Serif); title.TextTrimming = TextTrimming.CharacterEllipsis;
        head.Children.Add(title);
        var right = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(8, 0, 0, 0) };
        if (e.Waiting) right.Children.Add(Mark(L.T("READY"), EmeraldInk, EmeraldC, size: 9.5));
        else
        {
            var outcome = OutcomeText(e);
            if (outcome.Length > 0)
            {
                bool wipe = e.Outcome == "wipe";
                var st = Tt(outcome, 10, wipe ? Frozen(Color.FromRgb(0xFF, 0x9A, 0x9A)) : GoldInk, FontWeights.Bold);
                right.Children.Add(new Border
                {
                    Child = st, CornerRadius = new CornerRadius(7, 2, 7, 2), Padding = new Thickness(7, 1, 7, 2), Background = Frozen(Mod.A(DeepC, .9)), BorderThickness = new Thickness(1),
                    BorderBrush = Frozen(Mod.A(wipe ? CoralC : DragonGoldC, .6)), Margin = new Thickness(0, 1, 8, 0), VerticalAlignment = VerticalAlignment.Center, Tag = "outcome",
                });
            }
            var clock = Tt(Clock(e, now), 17, White, FontWeights.Bold); clock.Tag = "fight-clock";
            right.Children.Add(clock);
        }
        Grid.SetColumn(right, 1); head.Children.Add(right);
        // beside the head the title's room is short: the outcome drops to its own line under the title (beside the jaws, where
        // there is room) before the name is squeezed, then a long name steps its size down (to 11.5) before it is ever cut
        double titleW = Mod.W(title), titleRoom = RowsW - head.Margin.Left - Mod.W(right) - right.Margin.Left - 2;
        Border? under = null;
        if (titleW > titleRoom && right.Children.Count > 1 && right.Children[0] is Border oc && (string?)oc.Tag == "outcome")
        {
            right.Children.Remove(oc);
            oc.Margin = new Thickness(TitleX - FightPad.Left, 2, 0, 0); oc.HorizontalAlignment = HorizontalAlignment.Left;
            under = oc;
            titleRoom = RowsW - head.Margin.Left - Mod.W(right) - right.Margin.Left - 2;
        }
        if (titleW > titleRoom && titleRoom > 40) title.FontSize = Math.Max(11.5, Math.Floor(title.FontSize * titleRoom / titleW * 10) / 10);
        // the title row and the outcome under it are one block: a kill's plate slams over both
        if (under != null) { var both = new StackPanel(); both.Children.Add(head); both.Children.Add(under); _head = Lit(both); }
        else _head = Lit(head);
        _fightTop.Children.Add(_head);
        if (e.Waiting)
        {
            var wt = Tt(e.ReadyText, 12.5, Soft, FontWeights.SemiBold); wt.Margin = new Thickness(TitleX - FightPad.Left, 2, 0, 8); wt.TextTrimming = TextTrimming.CharacterEllipsis;
            _fightTop.Children.Add(Lit(wt));
            if (PartyBeforeHit(e)) { _fightBody.Children.Add(new Border { Height = 6 }); PutRows(_fightBody, e, now, RowsW); }
        }
        else
        {
            if (HpOf(e) is { } hp)
            {
                // the boss's HP: a slim ember line with gold ticks at the thirds, the figure and what's left at its right
                var hpRow = new Grid { Margin = new Thickness(0, 7, 0, 0) };
                hpRow.ColumnDefinitions.Add(new ColumnDefinition()); hpRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                var track = new Grid { Height = 6, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(TitleX - FightPad.Left, 0, 8, 0) };
                track.Children.Add(new TacSketch((dc, s) =>
                {
                    double y = 1, h = 4, x1 = s.Width * Math.Clamp(hp, 0, 1);
                    dc.DrawRoundedRectangle(Frozen(Mod.A(Colors.Black, .6)), Mod.P(Frozen(Mod.A(BronzeC, .45)), .6), new Rect(-.5, y - .5, s.Width + 1, h + 1), 2, 2);
                    if (x1 > .5) dc.DrawRoundedRectangle(Mod.Grad(false, (Color.FromRgb(0x8E, 0x12, 0x10), 0), (HpC, .8), (Color.FromRgb(0xFF, 0xB0, 0x60), 1)), null, new Rect(0, y, x1, h), 1.5, 1.5);
                    for (int k = 1; k <= 2; k++) { double x = Math.Round(s.Width * k / 3); dc.DrawRectangle(Frozen(Mod.A(DragonGoldC, .75)), null, new Rect(x - .5, y - 1, 1, h + 2)); }
                }));
                HpGhost(track, e, hp, now, 1.5);
                hpRow.Children.Add(track);
                var hl = new TextBlock { FontFamily = Mod.Face, FontSize = 10.5, Foreground = Soft, VerticalAlignment = VerticalAlignment.Center };
                System.Windows.Documents.Typography.SetNumeralAlignment(hl, FontNumeralAlignment.Tabular);
                hl.Inlines.Add(new System.Windows.Documents.Run($"{Math.Round(hp * 100):0}%") { FontWeight = FontWeights.Bold, Foreground = hp < .34 ? Frozen(Color.FromRgb(0xFF, 0x9A, 0x8A)) : White });
                var left = HpLeftText(e);
                if (left.Length > 0) hl.Inlines.Add(new System.Windows.Documents.Run("  ·  " + left) { FontWeight = FontWeights.Medium });
                hl.Tag = "hp-text";
                var hlLit = Lit(hl); Grid.SetColumn(hlLit, 1);
                hpRow.Children.Add(hlLit);
                _fightTop.Children.Add(hpRow);
                _fightBody.Children.Add(new Border { Height = 6 });
            }
            else _fightBody.Children.Add(new Border { Height = 10 });
            PutRows(_fightBody, e, now, RowsW);
            if (e.HasDamage)
            {
                _fightBody.Children.Add(Hairline(new Thickness(0, 2, 0, 3)));
                _fightBody.Children.Add(Foot(e, now));
            }
        }
        _fight.Visibility = Visibility.Visible;
        PlaceTimers();
    }

    // ================================================================ the leader and the head

    long _leadId;
    string _leadFight = "";

    /// <summary>The leader's row as drawn: a new leader in the fight seen (effects playing) makes the head flare and breathe fire along their row.</summary>
    internal void Lead(DragonRow row, MeterMember m, MeterContext c)
    {
        bool changed = _leadFight == c.E.Id && _leadId != 0 && _leadId != m.Id;
        _leadFight = c.E.Id; _leadId = m.Id;
        if (changed && c.Moves) Breathe(row);
    }

    DragonRow? LeaderRow() => Meter is { } m && m.Members.FirstOrDefault(x => x.Rank == 1 && !x.CpOnly) is { } top ? m.RowOf(top.Id) as DragonRow : null;

    IEnumerable<DragonRow> RowsInOrder() => Meter is { } m ? m.Members.Select(x => m.RowOf(x.Id)).OfType<DragonRow>() : Enumerable.Empty<DragonRow>();

    /// <summary>The dragon ablaze in <paramref name="c"/>: the drawing for that colour made (once) before the moment moves its opacity.</summary>
    void BlazeIn(Color c)
    {
        if (c != _blazeC) { _blazeC = c; _blaze.InvalidateVisual(); }
    }

    /// <summary>The plume out of the jaws toward the rows (drawn once: the burst's flame, turned down along the head's line).</summary>
    Rectangle Plume()
    {
        var mouth = new Point(FightHeadX + MouthAt.X * FightHeadK, FightHeadY + MouthAt.Y * FightHeadK);
        var p = new Rectangle { Width = 84, Height = 20, Fill = Burst, Opacity = 0, RenderTransformOrigin = new Point(0, .5) };
        var tg = new TransformGroup();
        tg.Children.Add(new ScaleTransform(0, 1));
        tg.Children.Add(new RotateTransform(34));
        p.RenderTransform = tg;
        Canvas.SetLeft(p, mouth.X - 4); Canvas.SetTop(p, mouth.Y - 10);
        return p;
    }

    /// <summary>A new leader: the eye flares, the jaws glow and a plume pours toward the rows, the burst runs along their row as their crest lands.</summary>
    void Breathe(DragonRow row)
    {
        var plume = Plume();
        _fightUnder.Children.Add(plume);
        var sc = (ScaleTransform)((TransformGroup)plume.RenderTransform).Children[0];
        row.Arm();
        Motion.Play(_eye, 900, t =>
        {
            _eyeFlare.Opacity = t < .15 ? Seg(t, 0, .15) : 1 - Seg(t, .3, .9);
            _mouthGlow.Opacity = t < .2 ? Seg(t, .05, .2) : 1 - Seg(t, .35, .85);
            sc.ScaleX = Out(Seg(t, .08, .4));
            plume.Opacity = t < .45 ? Seg(t, .08, .2) : 1 - Seg(t, .45, .75);
            row.Breath(Seg(t, .2, 1));
        }, () => { _eyeFlare.Opacity = 0; _mouthGlow.Opacity = 0; _fightUnder.Children.Remove(plume); row.Breath(-1); });
    }

    // ================================================================ the moments

    /// <summary>A pickup: an ember light runs along the Buddy's coil plate and the worth floats in beside it on an obsidian chip, then fades.</summary>
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
            Child = words, MaxWidth = 88, CornerRadius = new CornerRadius(10, 3, 10, 3), Background = Frozen(Mod.A(DeepC, .9)), BorderThickness = new Thickness(1),
            BorderBrush = Frozen(Mod.A(BronzeC, .7)),
        };
        var shift = new TranslateTransform();
        chip.RenderTransform = shift;
        chip.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        Canvas.SetLeft(chip, 6); Canvas.SetTop(chip, -chip.DesiredSize.Height / 2);
        _lineAnchor.Children.Clear(); _lineAnchor.Children.Add(chip);
        var race = Race(_lineFx, _line.ActualHeight > 0 ? _line.ActualHeight : 56, EmberC);
        Motion.Play(chip, 1900, t =>
        {
            RaceAt(race, _line.ActualWidth, Seg(t, 0, .5));
            double k = Out(Seg(t, .1, .3));
            shift.X = -10 * (1 - k);
            chip.Opacity = t < .82 ? k : 1 - Seg(t, .82, 1);
        }, () => { _lineFx.Children.Remove(race); _lineAnchor.Children.Remove(chip); });
    }

    /// <summary>A light that runs along a plate: a band of <paramref name="c"/> on <paramref name="host"/>, moved by <see cref="RaceAt"/>.</summary>
    static Rectangle Race(Canvas host, double h, Color c)
    {
        var r = new Rectangle { Width = 70, Height = h, Opacity = 0, RenderTransform = new TranslateTransform(), IsHitTestVisible = false,
            Fill = Mod.Grad(false, (Mod.A(c, 0), 0), (Mod.A(c, .22), .5), (Mod.A(Mod.Mix(c, Colors.White, .6), .45), .8), (Mod.A(c, 0), 1)) };
        host.Children.Add(r);
        return r;
    }

    static void RaceAt(Rectangle r, double w, double t)
    {
        if (t <= 0 || t >= 1) { r.Opacity = 0; return; }
        ((TranslateTransform)r.RenderTransform).X = -r.Width + (w + r.Width) * t;
        r.Opacity = t < .8 ? 1 : 1 - (t - .8) / .2;
    }

    /// <summary>A rare drop: a glint flashes on the hoard by the tile (turning as it grows and shrinks), a gold light sweeps the plate, the tile pops in.</summary>
    protected override void RareDrop(Pick p, bool held)
    {
        if (_rareFx is not { } fx || _rareTop is not { } top) return;
        var tile = _rareTile;
        var sc = new ScaleTransform(1, 1);
        if (tile != null) { tile.RenderTransformOrigin = new Point(.5, .5); tile.RenderTransform = sc; }
        var glint = new Rectangle { Width = 46, Height = 46, Fill = Glint, Opacity = 0, RenderTransformOrigin = new Point(.5, .5) };
        var gs = new ScaleTransform(0, 0); var gr = new RotateTransform(0);
        var tg = new TransformGroup(); tg.Children.Add(gs); tg.Children.Add(gr);
        glint.RenderTransform = tg;
        Canvas.SetLeft(glint, 8 + 26 - 23 + 3); Canvas.SetTop(glint, 6 - 23 + 3);
        var glint2 = new Rectangle { Width = 24, Height = 24, Fill = Glint, Opacity = 0, RenderTransformOrigin = new Point(.5, .5), RenderTransform = new ScaleTransform(0, 0) };
        Canvas.SetLeft(glint2, 8 + 26 - 8); Canvas.SetTop(glint2, 6 + 26 - 14);
        top.Children.Add(glint); top.Children.Add(glint2);
        // the big one in its grade's colour (2026-11-21: it read like a pickup): the light that runs the hoard, a shockwave
        // rolling off the tile and embers flung out of it are all the grade's
        var gc = _rareC;
        // the hoard flares in the grade's colour as the tile lands (a frozen fill, its opacity only)
        var flood = new Rectangle { Width = _rare.ActualWidth > 0 ? _rare.ActualWidth : 260, Height = _rare.ActualHeight > 0 ? _rare.ActualHeight : 46, Opacity = 0, IsHitTestVisible = false,
            Fill = Mod.Grad(false, (Mod.A(Mod.Mix(gc, Colors.White, .4), .85), 0), (Mod.A(gc, .55), .45), (Mod.A(gc, 0), 1)) };
        fx.Children.Add(flood);
        var race = Race(fx, 60, Mod.Mix(gc, Colors.White, .25));
        var waves = new List<(Ellipse E, ScaleTransform S, double At)>();
        var motes = new List<(Ellipse E, TranslateTransform T, Vector V, double At)>();
        if (MidOn(tile, fx) is { } mid)
        {
            var shock = Shock(Mod.Mix(gc, Colors.White, .2));
            for (int k = 0; k < 2; k++) { var (e, s) = Wave(fx, mid, 64, shock); waves.Add((e, s, .06 + .12 * k)); }
            var hot = Glow(Mod.Mix(gc, Colors.White, .5), 1); var deep = Glow(gc, 1);
            var rnd = new Random(13);
            for (int k = 0; k < 8; k++)
            {
                double ang = -Math.PI * .6 + Math.PI * 1.2 * k / 7 + .15 * rnd.NextDouble(), dist = 40 + 60 * rnd.NextDouble(), sz = 6 + 5 * rnd.NextDouble();
                var el = new Ellipse { Width = sz, Height = sz, Fill = k % 2 == 0 ? hot : deep, Opacity = 0, RenderTransform = new TranslateTransform(), IsHitTestVisible = false };
                Canvas.SetLeft(el, mid.X - sz / 2); Canvas.SetTop(el, mid.Y - sz / 2);
                fx.Children.Add(el);
                motes.Add((el, (TranslateTransform)el.RenderTransform, new Vector(Math.Cos(ang), Math.Sin(ang) * .5) * dist, .08 + .06 * rnd.NextDouble()));
            }
        }
        Motion.Play(_rare, 1500, t =>
        {
            foreach (var (e, s, at) in waves) { double u = Seg(t, at, at + .45); s.ScaleX = s.ScaleY = .1 + 5.5 * Out(u); e.Opacity = u <= 0 || u >= 1 ? 0 : (1 - u); }
            flood.Opacity = t < .06 ? Seg(t, 0, .06) : 1 - Seg(t, .06, .45);
            foreach (var (e, tr, v, at) in motes) { double u = Seg(t, at, at + .4); var q = Out(u); tr.X = v.X * q; tr.Y = v.Y * q; e.Opacity = u <= 0 || u >= 1 ? 0 : Math.Min(1, 4 * u) * (1 - u); }
            double a = Seg(t, .05, .55), k = Math.Sin(Math.PI * a);
            gs.ScaleX = gs.ScaleY = 1.25 * k; gr.Angle = 90 * a; glint.Opacity = Math.Min(1, 1.5 * k);
            double b = Seg(t, .25, .7), k2 = Math.Sin(Math.PI * b);
            ((ScaleTransform)glint2.RenderTransform).ScaleX = ((ScaleTransform)glint2.RenderTransform).ScaleY = k2; glint2.Opacity = k2;
            RaceAt(race, _rare.ActualWidth > 0 ? _rare.ActualWidth : 260, Seg(t, .15, .7));
            sc.ScaleX = sc.ScaleY = .62 + .38 * Back(Seg(t, .02, .35), 2.4);
        }, () => { fx.Children.Clear(); top.Children.Clear(); sc.ScaleX = sc.ScaleY = 1; });
    }

    /// <summary>Your best passed: gold fire runs round the ouroboros (a flame at the molten scales' head, the scales lighting behind it), a ring bursts off it, a gold light crosses the coil.</summary>
    protected override void BestPassed(bool held)
    {
        if (_ringArt is not { } art || _ringHost is not { } host) return;
        double was = _share is > 0 and < 1 ? _share : .86;
        bool beat = _beat;
        const double c = 26, r = 19;
        var turn = new RotateTransform(0, c, c);
        var flame = new Rectangle { Width = 16, Height = 16, Fill = Glint, Margin = new Thickness(c - 8, c - r - 8, 0, 0), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top, Opacity = 0, IsHitTestVisible = false };
        var orbit = new Grid { Width = 52, Height = 52, RenderTransform = turn, IsHitTestVisible = false };
        orbit.Children.Add(flame);
        var pop = new ScaleTransform(1, 1, c, c);
        var burst = new Ellipse { Width = 2 * r + 12, Height = 2 * r + 12, Margin = new Thickness(c - r - 6), Stroke = Frozen(Mod.A(DragonGoldC, .45)), StrokeThickness = 10, Opacity = 0, RenderTransform = pop, IsHitTestVisible = false };
        var halo = new Ellipse { Width = 2 * r + 12, Height = 2 * r + 12, Margin = new Thickness(c - r - 6), Stroke = Frozen(Mod.A(Color.FromRgb(0xFF, 0xD8, 0x6A), .95)), StrokeThickness = 4.5, Opacity = 0, RenderTransform = pop, IsHitTestVisible = false };
        host.Children.Add(burst); host.Children.Add(halo); host.Children.Add(orbit);
        var race = Race(_lineFx, _line.ActualHeight > 0 ? _line.ActualHeight : 56, DragonGoldC);
        // the hoard ignites (2026-11-21, "best-passed weak in Dragon"): as the flame closes the circle two gold shockwaves roll
        // off the ring across the coil plate, embers spit out of it, and the hour takes the hit
        var waves = new List<(Ellipse E, ScaleTransform S, double At)>();
        var motes = new List<(Ellipse E, TranslateTransform T, Vector V, double At)>();
        if (MidOn(host, _lineFx) is { } mid)
        {
            for (int k = 0; k < 2; k++) { var (e, s) = Wave(_lineFx, mid, 56, k == 0 ? ShockHot : ShockGold); waves.Add((e, s, .28 + .1 * k)); }
            var rnd = new Random(11);
            for (int k = 0; k < 8; k++)
            {
                double ang = -Math.PI * .95 + Math.PI * 1.3 * k / 7 + .2 * rnd.NextDouble(), dist = 34 + 40 * rnd.NextDouble(), sz = 4 + 4 * rnd.NextDouble();
                var el = new Ellipse { Width = sz, Height = sz, Fill = k % 2 == 0 ? MoteHot : MoteEmber, Opacity = 0, RenderTransform = new TranslateTransform(), IsHitTestVisible = false };
                Canvas.SetLeft(el, mid.X - sz / 2); Canvas.SetTop(el, mid.Y - sz / 2);
                _lineFx.Children.Add(el);
                motes.Add((el, (TranslateTransform)el.RenderTransform, new Vector(Math.Cos(ang), Math.Sin(ang) * .7) * dist, .3 + .05 * rnd.NextDouble()));
            }
        }
        // (the hour with its unit: the hour alone would grow into its "/h")
        var rate = _rate?.Parent as FrameworkElement;
        var rsc = new ScaleTransform(1, 1);
        if (rate != null) { rate.RenderTransformOrigin = new Point(0, .7); rate.RenderTransform = rsc; }
        Motion.Play(art, 1800, t =>
        {
            foreach (var (e, s, at) in waves) { double u = Seg(t, at, at + .4); s.ScaleX = s.ScaleY = .1 + 4.2 * Out(u); e.Opacity = u <= 0 || u >= 1 ? 0 : (1 - u); }
            foreach (var (e, tr, v, at) in motes) { double u = Seg(t, at, at + .38); var q = Out(u); tr.X = v.X * q; tr.Y = v.Y * q + 10 * u * u; e.Opacity = u <= 0 || u >= 1 ? 0 : Math.Min(1, 4 * u) * (1 - u); }
            rsc.ScaleX = rsc.ScaleY = 1 + .1 * Ez.Wob(Seg(t, .28, .7), 1.5, 4);
            _beat = false; _share = was;
            double run = Out(Seg(t, 0, .3));
            _complete = run;
            if (t >= .3) { _beat = beat; _complete = 1; }
            art.InvalidateVisual();
            turn.Angle = 360 * (was + (1 - was) * run);
            flame.Opacity = t < .3 ? Math.Min(1, t * 10) : 1 - Seg(t, .3, .4);
            double b = Seg(t, .1, .85);
            pop.ScaleX = pop.ScaleY = .9 + .32 * Out(b);
            burst.Opacity = t < .1 ? Seg(t, 0, .1) : 1 - b * b;
            halo.Opacity = t < .1 ? Seg(t, 0, .1) : 1 - b * b * b;
            RaceAt(race, _line.ActualWidth, Seg(t, .15, .7));
        }, () =>
        {
            host.Children.Remove(burst); host.Children.Remove(halo); host.Children.Remove(orbit); _lineFx.Children.Remove(race); _complete = 0; _beat = beat; art.InvalidateVisual();
            foreach (var (e, _, _) in waves) _lineFx.Children.Remove(e);
            foreach (var (e, _, _, _) in motes) _lineFx.Children.Remove(e);
            if (rate != null) rate.RenderTransform = null;
        });
    }

    /// <summary>The middle of <paramref name="el"/> on <paramref name="host"/>, or null when it is not laid out inside it.</summary>
    static Point? MidOn(FrameworkElement? el, FrameworkElement host)
    {
        try
        {
            if (el == null || el.ActualWidth <= 0 || !el.IsVisible || !host.IsVisible) return null;
            return el.TranslatePoint(new Point(el.ActualWidth / 2, el.ActualHeight / 2), host);
        }
        catch (InvalidOperationException) { return null; }
    }

    /// <summary>A soft shockwave of <paramref name="fill"/> centred on <paramref name="at"/> on <paramref name="host"/>: its scale and opacity are the moment's.</summary>
    static (Ellipse E, ScaleTransform S) Wave(Canvas host, Point at, double d, Brush fill)
    {
        var s = new ScaleTransform(.1, .1, d / 2, d / 2);
        var e = new Ellipse { Width = d, Height = d, Fill = fill, Opacity = 0, RenderTransform = s, IsHitTestVisible = false };
        Canvas.SetLeft(e, at.X - d / 2); Canvas.SetTop(e, at.Y - d / 2);
        host.Children.Add(e);
        return (e, s);
    }

    // (made on first use: the colours are other parts' statics)
    static RadialGradientBrush? _shockEmerald;
    static RadialGradientBrush ShockEmerald => _shockEmerald ??= Shock(EmeraldC);

    /// <summary>
    /// An entry opened, the gate's seal breaking: the first timer's chip draws in (a breath held), bursts out past its size
    /// with an emerald shockwave rolling off it across the coil plate, an emerald light runs the plate, and the chip
    /// settles with its edge lit.
    /// </summary>
    protected override void EntryOpened(TimerBoard.Item i, bool held)
    {
        if (_firstChip is not { } chip) return;
        var edge = chip.BorderBrush;
        chip.BorderBrush = Frozen(Mod.Mix(EmeraldC, Colors.White, .3));
        var sc = new ScaleTransform(1, 1);
        chip.RenderTransformOrigin = new Point(.5, .5); chip.RenderTransform = sc;
        // the waves and the light on the coil plate (clipped to it); none when the chip sits in the fight
        var waves = new List<(Ellipse E, ScaleTransform S, double At)>();
        Rectangle? race = null;
        if (MidOn(chip, _lineFx) is { } mid)
        {
            for (int k = 0; k < 2; k++) { var (e, s) = Wave(_lineFx, mid, 60, k == 0 ? ShockEmerald : ShockGold); waves.Add((e, s, .2 + .12 * k)); }
            race = Race(_lineFx, _line.ActualHeight > 0 ? _line.ActualHeight : 56, EmeraldC);
        }
        Motion.Play(chip, 1200, t =>
        {
            double wind = Seg(t, 0, .14), pop = Ez.Wob(Seg(t, .14, .6), 1.5, 4), rise = Out(Seg(t, .14, .2));
            sc.ScaleX = sc.ScaleY = t < .14 ? 1 - .12 * Ez.InOut(wind) : .88 + .12 * rise + .22 * pop;
            chip.Opacity = t < .14 ? 1 - .4 * wind : .6 + .4 * Seg(t, .14, .2);
            foreach (var (e, s, at) in waves) { double u = Seg(t, at, at + .45); s.ScaleX = s.ScaleY = .1 + 3.4 * Out(u); e.Opacity = u <= 0 || u >= 1 ? 0 : .95 * (1 - u); }
            if (race != null) RaceAt(race, _line.ActualWidth, Seg(t, .16, .7));
        }, () =>
        {
            chip.RenderTransform = null; chip.Opacity = 1; chip.BorderBrush = edge;
            foreach (var (e, _, _) in waves) _lineFx.Children.Remove(e);
            if (race != null) _lineFx.Children.Remove(race);
        });
    }

    /// <summary>
    /// A fight started: the dragon wakes. Its eye snaps open in a flare, an emerald light runs through the whole coil, the
    /// jaws light, and fire bursts down the bars in rank order; the leader's crest lands.
    /// </summary>
    protected override void FightStarted(Encounter e, bool held)
    {
        var rows = RowsInOrder().ToList();
        var lead = LeaderRow();
        BlazeIn(EmeraldC);
        foreach (var r in rows) r.Arm();
        Motion.Play(_fight, 1000, t =>
        {
            _lidScale.ScaleY = Math.Max(0, 1 - Math.Clamp(Back(Seg(t, .06, .26), 2.6), 0, 1.15));
            _eyeFlare.Opacity = t < .26 ? Seg(t, .12, .26) : 1 - Seg(t, .3, .8);
            _mouthGlow.Opacity = t < .3 ? Seg(t, .18, .3) : 1 - Seg(t, .4, .9);
            _blaze.Opacity = t < .3 ? .85 * Seg(t, .1, .3) : .85 * (1 - Seg(t, .35, .85));
            _fightBody.Opacity = .35 + .65 * Out(Seg(t, 0, .2));
            for (int i = 0; i < rows.Count; i++) rows[i].Blaze(Seg(t, .2 + .07 * i, .7 + .07 * i));
            lead?.Crown(Seg(t, .3, .7));
        }, () =>
        {
            _lidScale.ScaleY = 0; _eyeFlare.Opacity = 0; _mouthGlow.Opacity = 0; _blaze.Opacity = 0; _fightBody.Opacity = 1;
            foreach (var r in rows) r.Blaze(-1);
            lead?.Crown(-1);
        });
    }

    /// <summary>
    /// A fight ended. A kill is a roar: shockwaves ring out of the jaws, embers fly, the eye blazes, gold fire races down
    /// every bar, and a plate slams over the head's row with the outcome and the clock (the rows' words never covered).
    /// A wipe: the eye half closes, ash drifts down and the plate is ash and coral.
    /// </summary>
    protected override void FightEnded(Encounter e, bool held)
    {
        if (e.Outcome is not ("kill" or "wipe" or "cleared")) return;
        bool wipe = e.Outcome == "wipe";
        var c = wipe ? CoralC : DragonGoldC;
        var mouth = new Point(FightHeadX + MouthAt.X * FightHeadK, FightHeadY + MouthAt.Y * FightHeadK);
        _fightUnder.Children.Clear(); _fightOver.Children.Clear();
        BlazeIn(wipe ? CoralC : DragonGoldC);
        // the shockwaves (kill) out of the jaws
        var rings = new List<(Ellipse E, ScaleTransform S, double At)>();
        if (!wipe)
            for (int k = 0; k < 3; k++)
            {
                var s = new ScaleTransform(.2, .2, 20, 20);
                var el = new Ellipse { Width = 40, Height = 40, Fill = k == 0 ? ShockHot : ShockGold, Opacity = 0, RenderTransform = s };
                Canvas.SetLeft(el, mouth.X - 20); Canvas.SetTop(el, mouth.Y - 20);
                _fightUnder.Children.Add(el);
                rings.Add((el, s, .04 + .09 * k));
            }
        // the embers (ash on a wipe): small motes thrown out of the jaws
        var motes = new List<(Ellipse E, TranslateTransform T, Vector V, double At)>();
        var rnd = new Random(7);
        for (int k = 0; k < 12; k++)   // (12: a few short-lived pieces, 2026-11-21)
        {
            double ang = wipe ? Math.PI * (.35 + .3 * rnd.NextDouble()) : -Math.PI * .45 + Math.PI * .95 * rnd.NextDouble();
            double dist = (wipe ? 50 : 90) + (wipe ? 60 : 150) * rnd.NextDouble();
            double sz = 5 + 5 * rnd.NextDouble();
            var el = new Ellipse { Width = sz, Height = sz, Fill = wipe ? MoteAsh : rnd.NextDouble() < .5 ? MoteHot : MoteEmber, Opacity = 0, RenderTransform = new TranslateTransform() };
            Canvas.SetLeft(el, mouth.X - sz / 2 + (wipe ? 20 * rnd.NextDouble() : 0)); Canvas.SetTop(el, mouth.Y - sz / 2);
            _fightOver.Children.Add(el);
            motes.Add((el, (TranslateTransform)el.RenderTransform, new Vector(Math.Cos(ang), Math.Sin(ang)) * dist, .05 * rnd.NextDouble()));
        }
        // the plate over the head's row: the outcome big in serif and the clock
        var plate = new Grid { VerticalAlignment = VerticalAlignment.Top, HorizontalAlignment = HorizontalAlignment.Stretch };
        plate.Children.Add(new TacSketch((dc, s) =>
        {
            if (s.Width < 10 || s.Height < 6) return;
            var r = new Rect(.5, .5, s.Width - 1, s.Height - 1);
            var g = Mod.Pill(r, 6);
            dc.DrawGeometry(Frozen(Mod.A(DeepC, .97)), null, g);
            dc.DrawGeometry(Mod.Grad(false, (Mod.A(c, .38), 0), (Mod.A(c, .12), .55), (Mod.A(c, 0), 1)), null, g);
            dc.DrawGeometry(null, Mod.P(Mod.Grad(false, (Mod.A(c, .95), 0), (Mod.A(BronzeC, .6), .6), (Mod.A(BronzeC, .25), 1)), 1.1), g);
        }));
        var words = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(10, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        words.Children.Add(Tt(OutcomeText(e), 25, wipe ? Frozen(Color.FromRgb(0xFF, 0xB4, 0xAE)) : GoldInk, FontWeights.Bold, Serif));
        var ck = Tt("  " + Clock(e.Duration(e.End ?? DateTime.UtcNow)), 16, White, FontWeights.SemiBold); ck.Margin = new Thickness(4, 4, 0, 0);
        words.Children.Add(ck);
        plate.Children.Add(Lit(words));
        var slam = new ScaleTransform(1, 1); var fall = new TranslateTransform(); var tip = new RotateTransform(0);
        plate.RenderTransformOrigin = new Point(.3, .5); plate.RenderTransform = new TransformGroup { Children = { slam, tip, fall } };
        _fightFx.Children.Clear(); _fightFx.Children.Add(plate);
        bool Seat()
        {
            double y = FightPad.Top, h = 22;
            bool laid = _head is { } hd && hd.ActualHeight > 0 && hd.IsDescendantOf(_fight);
            if (laid) { y = _head!.TranslatePoint(new Point(0, 0), _fight).Y; h = _head.ActualHeight; }
            plate.Margin = new Thickness(TitleX - 8, Math.Max(1, y - 22), FightPad.Right - 8, 0);
            plate.Height = h + 24;
            return laid;
        }
        bool seated = Seat();
        var rows = RowsInOrder().ToList();
        foreach (var r in rows) r.Arm(wipe);
        var head = _head;
        Motion.Play(_fightFx, 2200, t =>
        {
            if (!seated) seated = Seat();
            foreach (var (el, s, at) in rings) { double u = Seg(t, at, at + .3); s.ScaleX = s.ScaleY = .2 + 11 * Out(u); el.Opacity = u <= 0 || u >= 1 ? 0 : .85 * (1 - u); }
            _blaze.Opacity = (wipe ? .8 : 1) * (t < .06 ? Seg(t, 0, .06) : 1 - Seg(t, .2, .7));
            foreach (var (el, tr, v, at) in motes) { double u = Seg(t, at, at + .42); var q = Out(u); tr.X = v.X * q; tr.Y = v.Y * q + (wipe ? 0 : 14 * u * u); el.Opacity = u <= 0 || u >= 1 ? 0 : Math.Min(1, 4 * u) * (1 - u); }
            _eyeFlare.Opacity = wipe ? 0 : t < .1 ? Seg(t, 0, .1) : 1 - Seg(t, .3, .7);
            _mouthGlow.Opacity = wipe ? 0 : t < .08 ? Seg(t, 0, .08) : 1 - Seg(t, .25, .55);
            _lidScale.ScaleY = wipe ? .78 * (t < .2 ? Out(Seg(t, 0, .2)) : 1 - Seg(t, .85, 1)) : 0;
            // a wipe: the rows sag to half light under the falling ash and come back as the plate leaves (a kill stays bright)
            if (wipe) _fightBody.Opacity = t < .1 ? 1 : t < .6 ? 1 - .45 * Out(Seg(t, .1, .2)) : .55 + .45 * Seg(t, .6, .8);
            for (int i = 0; i < rows.Count; i++) rows[i].Blaze(Seg(t, .05 + .05 * i, .45 + .05 * i));
            // the plate takes the head's place: the head gone before the plate comes, back only once the plate has left
            // (their words never cross); a kill slams it from big, a wipe drops it from above with a dead thud, tipped
            double i0 = Seg(t, .07, .16);
            plate.Opacity = t < .07 ? 0 : t < .82 ? Math.Min(1, i0 * 1.6) : 1 - Seg(t, .82, .91);
            if (wipe)
            {
                fall.Y = -8 * (1 - i0 * i0);   // (8: the plate sits by the window's top) tip.Angle = -2.5 * Out(Seg(t, .16, .4));
                _fightShift.Y = Math.Abs(Ez.Jolt(Seg(t, .16, .36), 2.5, 4));   // (2.5: the panel sits on the window's foot)
            }
            else
            {
                slam.ScaleX = slam.ScaleY = 1.22 - .22 * Back(i0, 2.4);
                _fightShift.X = Ez.Jolt(Seg(t, .14, .34), 4, 5);
            }
            if (head != null) head.Opacity = t < .5 ? 1 - Seg(t, .03, .07) : Seg(t, .91, 1);
        }, () =>
        {
            _fightFx.Children.Clear(); _fightUnder.Children.Clear(); _fightOver.Children.Clear();
            _eyeFlare.Opacity = 0; _mouthGlow.Opacity = 0; _lidScale.ScaleY = 0; _blaze.Opacity = 0;
            foreach (var r in rows) r.Blaze(-1);
            if (head != null) head.Opacity = 1;
            _fightShift.X = _fightShift.Y = 0; _fightBody.Opacity = 1;
        });
    }
}
