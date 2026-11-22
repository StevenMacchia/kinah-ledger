using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using KinahLedger.Parser;
using KinahLedger.Session;
using static KinahLedger.Ui.HudKit;

namespace KinahLedger.Ui;

/// <summary>
/// Asmodian (the Atreia theme, the second looks pack, 2026-11-20): the shadow faction's war-gear. Every surface is a plate of
/// black iron (HudAsmodian.Iron.cs) with notched corners, brushed grain, a crimson edge glowing just past it, a smoulder
/// rising from its foot and a thorn off each deep cut. Titles in a heavy condensed face, figures crisp white.
/// The Buddy is one plate: the run's ring as a crimson iron ring with studs, the hour big in white over a red underglow, the
/// pickups as iron-framed tiles, the session under it, the timers as iron chips. The meter is one plate: the boss in the
/// heavy face, the clock, a forged HP bar with spikes at the thirds, then each member as an iron slab with their bar forged
/// in their class's colour, tempered dark, ending in a blade edge that burns crimson to ember; a blood-red horned crown on
/// the top row. The moments: a pickup's ember light crosses the plate, a rare drop's plate flares in its grade, your best
/// passed burns the ring round in ember gold with sparks; a fight rises in, embers lift off the bars and a heat glint runs
/// along them (on the start, a kill and a new leader, when the crown slams down on the new top row), and its end stamps an
/// iron banner over the head.
/// </summary>
sealed partial class HudAsmodian : ModernLook
{
    /// <summary>The note's pop-out moves this (HudStackLook.PopNote).</summary>
    protected override UIElement? NoteHost => _note;

    static readonly Color MintC2 = Color.FromRgb(0x5D, 0xF2, 0xA6), MarkMintC = Color.FromRgb(0x7D, 0xFF, 0xC0), CoralC = Color.FromRgb(0xFF, 0x8C, 0x8C),
        GoldC2 = Color.FromRgb(0xFF, 0xC4, 0x6A), WarnC2 = Color.FromRgb(0xFF, 0xB0, 0x86), HpLowC = Color.FromRgb(0xFF, 0x9A, 0x8A);
    static readonly SolidColorBrush White = Frozen(Colors.White), Soft = Frozen(Color.FromArgb(0xDC, 0xF6, 0xEE, 0xEE)), Quiet = Frozen(Color.FromArgb(0xC4, 0xF0, 0xDD, 0xDE)),
        Mint2 = Frozen(Color.FromRgb(0x8C, 0xFF, 0xC6)), MarkMint = Frozen(MarkMintC), Coral = Frozen(CoralC), Gold2 = Frozen(GoldC2);

    // the room round the look: the plates' glow lies inside their own room; 98 at the right for the pickup's chip beside the Buddy
    readonly Grid _root = new() { Margin = new Thickness(6, 6, 98, 8), HorizontalAlignment = HorizontalAlignment.Left };
    readonly StackPanel _body = new() { HorizontalAlignment = HorizontalAlignment.Left };
    double _opacity = 0.9;

    // the Buddy: its plate, the ring, the hour, the session and the timer chips
    readonly Grid _line = new() { HorizontalAlignment = HorizontalAlignment.Left };
    readonly TacSketch _linePlate, _lineHeat = HeatLayer(9, 3, room: LineRoom);
    /// <summary>The Buddy's plate wears its crest of thorns out of a dungeon run (the run's tab sits there in one).</summary>
    bool _lineCrest = true;
    /// <summary>The pickup's ember band, clipped to the plate's shape; and the sparks off the ring (each moved by transform alone).</summary>
    readonly Canvas _lineSheenHost = new() { IsHitTestVisible = false, ClipToBounds = true };
    readonly System.Windows.Shapes.Path _sheenBand = SheenBand();
    readonly Canvas _lineSparkLayer = new() { IsHitTestVisible = false, ClipToBounds = true };
    readonly Sparks _lineSparks;
    readonly StackPanel _lineBody = new() { Orientation = Orientation.Horizontal, Margin = new Thickness(LineRoom.Left + 8, LineRoom.Top + 5, LineRoom.Right + 13, LineRoom.Bottom + 5) };
    readonly Canvas _lineAnchor = Tac.Anchor(HorizontalAlignment.Right, VerticalAlignment.Center);
    readonly StackPanel _timers = new() { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
    readonly Border _timersInLine = new() { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 0) };
    /// <summary>The timer chips over the line, when the pickup strip takes their room on it.</summary>
    readonly Border _timersUp = new() { HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(G, 0, 0, 3), Visibility = Visibility.Collapsed };
    /// <summary>The pickup strip: 24 px tiles in iron frames beside the hour, a crimson rule, an ember-gold star.</summary>
    static readonly PickStrip.Look Strip = new(24, 5, true, Frozen(Color.FromArgb(0x90, 0xC2, 0x1F, 0x2E)), Frozen(Color.FromRgb(0xFF, 0xC4, 0x6A)));
    bool _dim;
    Grid? _ringHost;
    TacSketch? _ringArt;
    double _share = -1;
    bool _beat;
    Grid? _firstChip;

    // the rare drop under the Buddy, and the notice line
    readonly Grid _rare = new() { HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 2, 0, 0), Visibility = Visibility.Collapsed };
    readonly Grid _note = new() { HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(6, 1, 0, 0), Visibility = Visibility.Collapsed };
    readonly StackPanel _noteBody = new() { Orientation = Orientation.Horizontal, Margin = new Thickness(G + 8, G + 3, G + 10, G + 4) };

    // the fight: one iron plate, its head, HP, rows and foot
    readonly Grid _fight = new() { HorizontalAlignment = HorizontalAlignment.Left, Visibility = Visibility.Collapsed, Margin = new Thickness(0, 0, 0, 4) };
    readonly TacSketch _fightPlate;
    const double FightPad = 6;
    readonly StackPanel _fightBody = new() { Margin = new Thickness(FightRoom.Left + FightPad, FightRoom.Top + 6, FightRoom.Right + FightPad, FightRoom.Bottom + 7) };
    /// <summary>The fight's moments' layer over the plate (a kill's shockwave, the fight's opening cut), clipped to the fight's own bounds.</summary>
    readonly System.Windows.Controls.Canvas _fightFx = new() { IsHitTestVisible = false, ClipToBounds = true };
    readonly Border _timersInFight = new() { Margin = new Thickness(0, 0, 0, 6), HorizontalAlignment = HorizontalAlignment.Left };
    readonly TacSketch _fightHeat = HeatLayer(11, room: FightRoom);
    /// <summary>The embers' layer, over the rows inside the meter's own grid (rows' coordinates), and the burst on it.</summary>
    readonly Canvas _emberLayer = new() { IsHitTestVisible = false };
    readonly Sparks _embers;
    readonly TranslateTransform _fightShift = new();
    /// <summary>The fight plate's punch (a kill's hit), under its shift.</summary>
    readonly ScaleTransform _fightPunch = new();
    /// <summary>The moments' layer over the whole look (AtreiaFx): each moment a stage of its own on it, clipped to the window.</summary>
    readonly Canvas _fx = new() { IsHitTestVisible = false };
    DateTime _lastLead;
    Grid? _head;
    Encounter? _shownE;
    double _rowsW;

    public HudAsmodian() : base(bare: true)
    {
        // the look's own rows (Ui/Themes/ThemeRows.cs): each an iron slab with its forged bar
        UseRows(IronRow.Pitch, m => new IronRow(this), 14, GapLine);
        _linePlate = new TacSketch((dc, s) => WarPlate(dc, s, _opacity, LineRoom, 9, 30, edge: _dim ? AshC : null, crest: _lineCrest));
        _lineSheenHost.Children.Add(_sheenBand);
        _lineSparks = new Sparks(_lineSparkLayer);
        _line.Children.Add(_linePlate); _line.Children.Add(_lineHeat); _line.Children.Add(_lineSheenHost); _line.Children.Add(_lineBody); _line.Children.Add(_lineSparkLayer); _line.Children.Add(_lineAnchor);
        // the band's clip follows the plate's shape (set when the plate's size changes, never per frame)
        _line.SizeChanged += (_, e) => _lineSheenHost.Clip = Plate(new Rect(LineRoom.Left, LineRoom.Top, Math.Max(0, e.NewSize.Width - LineRoom.Left - LineRoom.Right), Math.Max(0, e.NewSize.Height - LineRoom.Top - LineRoom.Bottom)), 9, 3);
        // the war plate: horns, crest, fangs and the blood moon behind the title, drawn once per size
        _fightPlate = new TacSketch((dc, s) => WarPlate(dc, s, _opacity, FightRoom, 11, 40, new Point(2, 16), 30));
        _fight.Children.Add(_fightPlate); _fight.Children.Add(_fightHeat); _fight.Children.Add(_fightBody); _fight.Children.Add(_fightFx);
        _embers = new Sparks(_emberLayer);
        Panel.SetZIndex(_emberLayer, 50);
        Meter!.Children.Add(_emberLayer);
        _fight.RenderTransform = new TransformGroup { Children = { _fightPunch, _fightShift } };
        _note.Children.Add(new TacSketch((dc, s) => Iron(dc, s, _opacity, big: 6, small: 2, thorns: false)));
        _note.Children.Add(_noteBody);
        _runHeadHost = RunHeadPlate(_runHead); _runHeadHost.Visibility = Visibility.Collapsed;
        _body.Children.Add(_fight); _body.Children.Add(_timersUp); _body.Children.Add(_runHeadHost); _body.Children.Add(_line); _body.Children.Add(_rare); _body.Children.Add(_note);
        _root.Children.Add(_body); _root.Children.Add(_fx);
        System.Windows.Documents.TextElement.SetFontFamily(_root, Mod.Face);
        TextOptions.SetTextFormattingMode(_root, TextFormattingMode.Display);
    }

    public override FrameworkElement Root => _root;
    public override Thickness Inset => _root.Margin;
    public override double Corner => 0;
    public override FrameworkElement? FightFrame => _fight.Visibility == Visibility.Visible ? _fight : null;

    public override void SetOpacity(double opacity)
    {
        _opacity = Math.Clamp(opacity, 0.3, 1);
        _linePlate.InvalidateVisual(); _fightPlate.InvalidateVisual();
        foreach (var s in _note.Children.OfType<TacSketch>()) s.InvalidateVisual();
        foreach (var s in _rare.Children.OfType<TacSketch>()) s.InvalidateVisual();
    }

    protected override string LineSig(Figures f) => base.LineSig(f) + "|" + (Rare(Values)?.Id ?? "") + "|" + (Picks(Values).FirstOrDefault()?.Id ?? "") + "|" + Dimmed(Values) + "|" + PickStrip.Sig(Values, Strip) + "|" + RunBuddy.Sig(Values);

    static bool Dimmed(IReadOnlyDictionary<string, string> v) => (v.TryGetValue("show", out var s) && s == "dim") || (v.TryGetValue("paused", out var p) && p.Length > 0);

    /// <summary>The tight shade under words straight on the game (the run's head over its tab is the only such text).</summary>
    static DropShadowEffect Shade() => new() { Color = Colors.Black, Direction = 270, ShadowDepth = 1, BlurRadius = 3, Opacity = .95 };

    /// <summary>The red underglow under the hour (one effect, on one text).</summary>
    static DropShadowEffect Underglow(double opacity = 1) => new() { Color = Color.FromRgb(0xFF, 0x1E, 0x1E), Direction = 270, ShadowDepth = 2.5, BlurRadius = 14, Opacity = opacity };

    /// <summary>The pickup strip's tiles each set in an iron frame: a notched dark rim and crimson corner brackets.</summary>
    static FrameworkElement? Forge(FrameworkElement? strip)
    {
        if (strip is not Panel row) return strip;
        foreach (var cell in row.Children.OfType<Grid>())
        {
            if (cell.Tag is not ("pick-tile" or "pick-best")) continue;
            cell.Children.Insert(1, new TacSketch((dc, s) =>
            {
                var r = new Rect(-1.5, -1.5, s.Width + 3, s.Height + 3);
                dc.DrawGeometry(null, Mod.P(Mod.Br(IronC, .9), 1.4), Plate(r, 4, 0));
                var pen = Mod.P(Mod.Br(HotC, .95), 1.3);
                double k = 5;
                dc.DrawLine(pen, new Point(r.Right - k, r.Top), new Point(r.Right, r.Top)); dc.DrawLine(pen, new Point(r.Right, r.Top), new Point(r.Right, r.Top + k));
                dc.DrawLine(pen, new Point(r.Left, r.Bottom - k), new Point(r.Left, r.Bottom)); dc.DrawLine(pen, new Point(r.Left, r.Bottom), new Point(r.Left + k, r.Bottom));
            }));
        }
        return strip;
    }

    // ================================================================ the Buddy

    protected override void DrawLine(Figures f)
    {
        _lineBody.Children.Clear();
        _ringArt = null; _ringHost = null; _ringGold = null; _ringHead = null; _lineSparks.Clear(); _timerUp = false; _stripTimerW = -1;
        if (_timersInLine.Child != null) _timersInLine.Child = null;
        bool dim = Dimmed(Values);
        if (dim != _dim) { _dim = dim; _linePlate.InvalidateVisual(); }   // the plate is drawn again only when it turns ash or back (its size redraws it itself)
        _lineBody.Opacity = _dim ? .62 : 1;
        bool mark = f.Abyss && f.Counted && f.Icon.Length > 0;
        if (mark) { var m = AbyssMark(f.Icon, 36); m.Margin = new Thickness(0, 0, 10, 0); _lineBody.Children.Add(m); }
        // in a dungeon run: the run's clock, splits and verdict in place of the hour (HudAsmodian.Run.cs)
        var rs = RunNow();
        if (_lineCrest != (rs == null)) { _lineCrest = rs == null; _linePlate.InvalidateVisual(); }
        _runHeadHost.Visibility = Visibility.Collapsed;
        if (rs != null) DrawRun(rs);
        else if (!HasHour(f))
        {
            // and the figures there are under the words (in the Abyss: kills and Kinah): information, never an empty plate
            var w = Head(Waiting(f.Abyss), 14, Soft);
            var sub = Sub(f, false);
            bool more = sub.Inlines.Count > 0;
            var words = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(2, more ? 2 : 5, 0, more ? 2 : 5) };
            words.Children.Add(w);
            if (more) words.Children.Add(sub);
            _lineBody.Children.Add(words);
        }
        else
        {
            if (HasRing(f))
            {
                _share = f.Share; _beat = f.Beat;
                var ring = new Grid { Width = 42, Height = 42, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0) };
                _ringArt = new TacSketch(DrawRing, 42, 42);
                ring.Children.Add(_ringArt);
                // your best passed: the ring in ember gold over it and a hot point running round (shown only in that moment)
                _ringGold = new TacSketch((dc, s) => DrawRingOf(dc, s, 1, true), 42, 42) { Opacity = 0, Visibility = Visibility.Collapsed };
                ring.Children.Add(_ringGold);
                var dot = new System.Windows.Shapes.Ellipse { Width = 7, Height = 7, Fill = SparkPale, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 1.5, 0, 0) };
                _ringHead = new Grid { Width = 42, Height = 42, Opacity = 0, Visibility = Visibility.Collapsed, RenderTransformOrigin = new Point(.5, .5), RenderTransform = new RotateTransform(0), IsHitTestVisible = false, Children = { dot } };
                ring.Children.Add(_ringHead);
                var pc = Big(f.Beat ? "★" : Pct(f.Share), f.Beat ? 15 : 14, f.Beat ? Frozen(PaleC) : White, FontWeights.Bold); pc.HorizontalAlignment = HorizontalAlignment.Center;
                ring.Children.Add(pc);
                _ringHost = ring;
                _lineBody.Children.Add(ring);
            }
            // the hour big over its red underglow, its unit small; the session and the latest pickup under it
            var words = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 0, 1) };
            var top = new StackPanel { Orientation = Orientation.Horizontal };
            var rate = Big(f.Rate, f.Warm ? 16 : 25, f.Warm ? Soft : White, f.Warm ? FontWeights.SemiBold : FontWeights.Bold);
            rate.Tag = "buddy-rate";
            if (f.Warm) rate.FontStretch = FontStretches.Condensed;
            top.Children.Add(new Border { Child = rate, Effect = f.Warm ? null : Underglow() });
            if (f.Unit.Length > 0) { var u = Head(f.Unit, 12, Quiet); u.VerticalAlignment = VerticalAlignment.Bottom; u.Margin = new Thickness(2, 0, 0, f.Warm ? 1 : 4); top.Children.Add(u); }
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
                    if ((strip = StripIn(StripRoom(FightWidth - 10, baseW + timerW, wordsW, oldW, 14), look)) != null) break;
                    if (timerW > 0 && (strip = StripIn(StripRoom(FightWidth - 10, baseW, wordsW, oldW + timerW, 14), look)) != null) { _timerUp = true; break; }
                }
                if (strip != null) { sub = bare; strip.Margin = new Thickness(14, 0, 0, 0); Forge(strip); }
                // no room beside the hour: under the session, never dropped (Steven, 2026-11-19)
                else if ((under = StripUnder(Strip, false)) != null) { sub = bare; under.Margin = new Thickness(2, 7, 0, 2); Forge(under); }
                _stripTimerW = timerW;
            }
            if (sub.Inlines.Count > 0) words.Children.Add(sub);
            if (under != null) words.Children.Add(under);
            _lineBody.Children.Add(words);
            if (strip != null) _lineBody.Children.Add(strip);
        }
        _lineBody.Children.Add(_timersInLine);
        PlaceTimers();


        // the rare drop the window holds: its own plate edged in its grade's colour, its tile, RARE DROP and its name
        _rare.Children.Clear();
        _rareSparks ??= new Sparks(_rareSparkLayer);
        _rareSparks.Clear();
        if (!KinahHidden && Rare(Values) is { } r)
        {
            var c = ColorOf(r.Color, r.Grade);
            _rareC = c;
            // the plate drawn once; its moment moves it by transform (forged out to the right) and lights its heat layer
            var plate = new TacSketch((dc, s) =>
            {
                Iron(dc, s, _opacity, .35, big: 8);
                // the grade's colour as a wash from the tile and a thin line under the plate's top
                dc.DrawRectangle(Mod.Grad(false, (Mod.A(c, .28), 0), (Mod.A(c, 0), .5)), null, new Rect(G + 1, G + 1, Math.Max(0, s.Width - 2 * G - 2), Math.Max(0, s.Height - 2 * G - 2)));
                dc.DrawRectangle(Mod.Grad(false, (Mod.A(c, .9), 0), (Mod.A(c, 0), 1)), null, new Rect(G + 9, G + 2, Math.Max(0, s.Width * .6), 1));
            });
            _rareArt = plate;
            _rare.Children.Add(plate);
            _rareHeat = HeatLayer(8, 3);
            _rare.Children.Add(_rareHeat);
            var bandHost = new Canvas { IsHitTestVisible = false, ClipToBounds = true };
            _rareBand = SheenBand(); _rareBand.Fill = RareSheen(c); _rareBand.Height = 60;
            bandHost.Children.Add(_rareBand);
            _rare.Children.Add(bandHost);
            var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(G + 7, G + 5, G + 14, G + 5) };
            var tile = Tile(r, 26); tile.Margin = new Thickness(0, 0, 9, 0);
            _rareTile = tile;
            row.Children.Add(tile);
            var mk = Chip(L.T("RARE DROP"), Frozen(Color.FromRgb(0xFF, 0x5A, 0x48)), CrimC, size: 8.5); mk.Margin = new Thickness(0, 0, 8, 0);
            row.Children.Add(mk);
            var nm = Head(r.Name, 15, Frozen(Mod.Mix(c, Colors.White, .4)), FontWeights.Bold);
            row.Children.Add(nm);
            if (r.Count.Length > 0) { var cn = Tt(" " + r.Count, 11, Soft, FontWeights.SemiBold); cn.Margin = new Thickness(3, 1, 0, 0); row.Children.Add(cn); }
            _rareRow = row;
            _rare.Children.Add(row);
            _rare.Children.Add(_rareSparkLayer);
            _rare.Visibility = Visibility.Visible;
        }
        else { _rare.Visibility = Visibility.Collapsed; _rareArt = null; _rareTile = null; _rareHeat = null; _rareRow = null; }

        // the notice: its words in their tone on a slim iron plate under the Buddy, the warning sign before a pause
        _noteBody.Children.Clear();
        if (f.Note.Length > 0 && !KinahHidden)
        {
            bool warn = ReferenceEquals(f.NoteTone, Warn);
            if (warn) { var wi = WarnIcon(); wi.Width = wi.Height = 13; wi.Margin = new Thickness(0, 0, 6, 0); _noteBody.Children.Add(wi); }
            var tone = warn ? WarnC2 : ReferenceEquals(f.NoteTone, Gold) ? GoldC2 : ReferenceEquals(f.NoteTone, Mint) ? MintC2 : BoneC;
            var t = Tt(f.Note, 11.5, Frozen(tone), FontWeights.SemiBold); t.TextTrimming = TextTrimming.CharacterEllipsis; t.MaxWidth = 380;
            _noteBody.Children.Add(t);
            _note.Visibility = Visibility.Visible;
        }
        else _note.Visibility = Visibility.Collapsed;
    }

    /// <summary>The line under the hour: the session, the latest pickup and its worth (<paramref name="pick"/>); in the Abyss its stats.</summary>
    TextBlock Sub(Figures f, bool pick)
    {
        var sub = new TextBlock { FontFamily = Mod.Face, FontSize = 10.5, FontWeight = FontWeights.Medium, Foreground = Soft, Margin = new Thickness(1, 0, 0, 0) };
        System.Windows.Documents.Typography.SetNumeralAlignment(sub, FontNumeralAlignment.Tabular);
        void Dot() { if (sub.Inlines.Count > 0) sub.Inlines.Add(new System.Windows.Documents.Run("  ◆  ") { Foreground = Frozen(Mod.A(CrimC, .9)), FontSize = 6.5, BaselineAlignment = BaselineAlignment.Center }); }
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
    double LineTimerW() => _timers.Children.Count > 0 && !KinahHidden ? 12 + Mod.W(_timers) : 0;

    /// <summary>The timers' room the line's strip was fitted to (-1: no strip time); chips that change it draw the line again.</summary>
    double _stripTimerW = -1;

    /// <summary>The timer chips sit over the line, to leave its room to the pickup strip.</summary>
    bool _timerUp;

    Color _rareC;
    TacSketch? _rareArt, _rareHeat;
    FrameworkElement? _rareTile, _rareRow;
    readonly Canvas _rareSparkLayer = new() { IsHitTestVisible = false };
    Sparks? _rareSparks;
    System.Windows.Shapes.Path? _rareBand;

    /// <summary>A rare drop's band of light in its grade's colour (made once per drop shown).</summary>
    static Brush RareSheen(Color c) => Mod.Grad(false, (Mod.A(c, 0), 0), (Mod.A(Mod.Mix(c, Colors.White, .5), .55), .55), (Mod.A(c, 0), 1));

    /// <summary>
    /// The run's ring as a ring of crimson iron: a dark well, a heavy iron track with four studs, your best's share as a
    /// crimson arc burning hotter at its head (ember gold once beaten), flat-cut ends.
    /// </summary>
    void DrawRing(DrawingContext dc, Size s) => DrawRingOf(dc, s, _beat ? 1 : _share, _beat);

    void DrawRingOf(DrawingContext dc, Size s, double share, bool won)
    {
        double c = s.Width / 2, r = c - 4;
        var center = new Point(c, c);
        dc.DrawEllipse(Mod.Br(IronC, .9), Mod.P(Mod.Br(CrimC, .5), 1), center, c - .5, c - .5);
        dc.DrawEllipse(null, Mod.P(Mod.Br(RimC), 5), center, r, r);
        double shown = Math.Clamp(share, 0, 1);
        Brush tone = won ? Mod.Grad(true, (PaleC, 0), (EmberC, 1)) : Mod.Grad(false, (CrimC, 0), (HotC, 1));
        if (shown >= .999) dc.DrawEllipse(null, Mod.P(tone, 4.2), center, r, r);
        else if (shown > .004)
        {
            double a = 2 * Math.PI * shown;
            var fig = new PathFigure { StartPoint = new Point(c, c - r) };
            fig.Segments.Add(new ArcSegment(new Point(c + r * Math.Sin(a), c - r * Math.Cos(a)), new Size(r, r), 0, a > Math.PI, SweepDirection.Clockwise, true));
            var pg = new PathGeometry(); pg.Figures.Add(fig); pg.Freeze();
            dc.DrawGeometry(null, Mod.P(tone, 4.2), pg);
            // the arc's head: a hot point
            var head = new Point(c + r * Math.Sin(a), c - r * Math.Cos(a));
            dc.DrawEllipse(Mod.Br(PaleC, .9), null, head, 1.6, 1.6);
        }
        // the studs: four dark notches across the ring
        var stud = Mod.P(Mod.Br(IronC), 1.4);
        for (int k = 0; k < 4; k++)
        {
            double ang = Math.PI / 2 * k + Math.PI / 4;
            dc.DrawLine(stud, new Point(c + (r - 3) * Math.Sin(ang), c - (r - 3) * Math.Cos(ang)), new Point(c + (r + 3) * Math.Sin(ang), c - (r + 3) * Math.Cos(ang)));
        }
    }

    TacSketch? _ringGold;
    Grid? _ringHead;

    protected override void KinahShown(bool shown)
    {
        _line.Visibility = shown ? Visibility.Visible : Visibility.Collapsed;
        if (!shown) { _note.Visibility = Visibility.Collapsed; _rare.Visibility = Visibility.Collapsed; }
        _fight.Margin = new Thickness(0, 0, 0, shown ? 4 : 0);
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
            var c = tone == TimerBadge.Tone.On ? MintC2 : tone == TimerBadge.Tone.Soon ? GoldC2 : Mod.Mix(CrimC, BoneC, .25);
            var sp = new StackPanel { Orientation = Orientation.Horizontal, Margin = first ? new Thickness(8, 3, 10, 4) : new Thickness(7, 2, 9, 3) };
            // a small diamond in the tone before the words
            double d = first ? 6.5 : 5.5;
            sp.Children.Add(new System.Windows.Shapes.Rectangle { Width = d, Height = d, Fill = Frozen(c), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 7, 0), RenderTransformOrigin = new Point(.5, .5), RenderTransform = new RotateTransform(45) });
            var t = Tt(text, first ? 11.5 : 10, first ? White : Soft, first ? FontWeights.Bold : FontWeights.SemiBold);
            if (tone == TimerBadge.Tone.On) t.Foreground = Frozen(Mod.Mix(MintC2, Colors.White, .45));
            else if (tone == TimerBadge.Tone.Soon) t.Foreground = Frozen(Mod.Mix(GoldC2, Colors.White, .4));
            sp.Children.Add(t);
            var edge = tone == TimerBadge.Tone.Quiet ? CrimC : c;
            var chip = new Grid { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(first ? 0 : 5, 0, 0, 0), Tag = first ? "timer-chip" : null };
            chip.Children.Add(new TacSketch((dc, s) =>
            {
                if (s.Width < 4 || s.Height < 4) return;
                var shape = Plate(new Rect(.5, .5, s.Width - 1, s.Height - 1), 4, 1);
                dc.DrawGeometry(Mod.Grad(true, (Mod.A(Iron2C, .93), 0), (Mod.A(IronC, .95), 1)), Mod.P(Mod.Br(edge, tone == TimerBadge.Tone.Quiet ? .75 : .85), 1), shape);
                dc.DrawRectangle(Mod.Br(edge, .12), null, new Rect(1, 1, s.Width - 2, s.Height - 2));
            }));
            chip.Children.Add(sp);
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
        if (e == null) { _fight.Visibility = Visibility.Collapsed; _embers.Clear(); PlaceTimers(); return; }
        double w = FightWidth - 10;
        _fight.Width = w;
        double inner = w - FightRoom.Left - FightRoom.Right - 2 * FightPad;
        _rowsW = inner; _shownE = e;
        _fightBody.Children.Add(_timersInFight);
        // the head: the boss in the heavy face, the outcome once it's over, the clock
        var head = new Grid();
        head.ColumnDefinitions.Add(new ColumnDefinition()); head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var title = Head(e.Title, 17, White); title.TextTrimming = TextTrimming.CharacterEllipsis;
        head.Children.Add(title);
        var right = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(8, 0, 0, 0) };
        if (e.Waiting) right.Children.Add(Chip(L.T("READY"), MarkMint, MintC2, size: 9.5, pad: new Thickness(7, 1, 7, 2)));
        else
        {
            var outcome = OutcomeText(e);
            if (outcome.Length > 0)
            {
                bool wipe = e.Outcome == "wipe";
                var oc = Chip(outcome, wipe ? Frozen(Mod.Mix(AshC, Colors.White, .35)) : Gold2, wipe ? AshC : GoldC2, size: 10, face: Bahn, pad: new Thickness(7, 1, 7, 2));
                oc.Margin = new Thickness(0, 1, 9, 0); oc.Tag = "outcome";
                right.Children.Add(oc);
            }
            var clock = Big(Clock(e, now), 19, White); clock.Tag = "fight-clock";
            right.Children.Add(clock);
        }
        Grid.SetColumn(right, 1); head.Children.Add(right);
        _head = head;
        _fightBody.Children.Add(head);
        if (e.Waiting)
        {
            var wt = Tt(e.ReadyText, 12.5, Soft, FontWeights.SemiBold); wt.Margin = new Thickness(0, 5, 0, 1); wt.TextTrimming = TextTrimming.CharacterEllipsis;
            _fightBody.Children.Add(wt);
            if (PartyBeforeHit(e)) { _fightBody.Children.Add(Rule(new Thickness(0, 4, 0, 4), false)); PutRows(_fightBody, e, now, inner); }
        }
        else
        {
            if (HpOf(e) is { } hp)
            {
                // the boss's HP: a forged bar with spikes at the thirds, "47%  ·  1.84M left" at its right
                var hpLine = new Grid { Margin = new Thickness(0, 5, 0, 0) };
                hpLine.ColumnDefinitions.Add(new ColumnDefinition()); hpLine.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                var track = new Grid { Height = 7, VerticalAlignment = VerticalAlignment.Center };
                track.Children.Add(new TacSketch((dc, s) =>
                {
                    var r = new Rect(0, 0, s.Width, s.Height);
                    var shape = Plate(r, 3, 0);
                    dc.DrawGeometry(Mod.Br(Color.FromRgb(0x22, 0x0E, 0x10)), Mod.P(Mod.Br(Colors.Black, .8), 1), shape);
                    double x1 = s.Width * Math.Clamp(hp, 0, 1);
                    dc.PushClip(shape);
                    if (x1 > .5)
                    {
                        dc.DrawRectangle(Mod.Grad(false, (Color.FromRgb(0x6E, 0x0B, 0x16), 0), (Color.FromRgb(0xE0, 0x28, 0x3A), 1)), null, new Rect(0, 0, x1, s.Height));
                        dc.DrawRectangle(Mod.Br(Colors.White, .22), null, new Rect(0, 0, x1, 1));
                        dc.DrawRectangle(Mod.Br(Colors.Black, .3), null, new Rect(0, s.Height - 2, x1, 2));
                        dc.DrawRectangle(Mod.Br(Color.FromRgb(0xFF, 0x9A, 0x70)), null, new Rect(Math.Max(0, x1 - 1.5), 0, 1.5, s.Height));
                    }
                    dc.Pop();
                    // the spikes at the thirds: dark wedges biting down from the top
                    for (int k = 1; k <= 2; k++)
                    {
                        double x = Math.Round(s.Width * k / 3);
                        var sp = new StreamGeometry();
                        using (var q = sp.Open()) { q.BeginFigure(new Point(x - 2.5, -1), true, true); q.LineTo(new Point(x + 2.5, -1), true, false); q.LineTo(new Point(x, s.Height - 1), true, false); }
                        sp.Freeze();
                        dc.DrawGeometry(Mod.Br(IronC), null, sp);
                    }
                }));
                HpGhost(track, e, hp, now, 0);
                hpLine.Children.Add(track);
                var hl = new TextBlock { FontFamily = Mod.Face, FontSize = 10.5, Foreground = Soft, Margin = new Thickness(10, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
                System.Windows.Documents.Typography.SetNumeralAlignment(hl, FontNumeralAlignment.Tabular);
                hl.Inlines.Add(new System.Windows.Documents.Run($"{Math.Round(hp * 100):0}%") { FontWeight = FontWeights.Bold, FontSize = 11.5, Foreground = hp < .34 ? Frozen(HpLowC) : White });
                var left = HpLeftText(e);
                if (left.Length > 0) hl.Inlines.Add(new System.Windows.Documents.Run("  ·  " + left) { FontWeight = FontWeights.Medium });
                hl.Tag = "hp-text";
                Grid.SetColumn(hl, 1); hpLine.Children.Add(hl);
                _fightBody.Children.Add(hpLine);
                _fightBody.Children.Add(new Border { Height = 7 });
            }
            else _fightBody.Children.Add(Rule(new Thickness(0, 3, 0, 4), false));
            PutRows(_fightBody, e, now, inner);
            if (e.HasDamage)
            {
                _fightBody.Children.Add(Rule(new Thickness(0, 3, 0, 3)));
                _fightBody.Children.Add(Foot(e, now));
            }
        }
        _fight.Visibility = Visibility.Visible;
        PlaceTimers();
    }

    // ================================================================ the embers

    /// <summary>A new leader took the top row: the embers and the heat glint (at most once in a while, however often the lead swaps).</summary>
    internal void Lead()
    {
        if (!Moves || (DateTime.UtcNow - _lastLead).TotalMilliseconds < 1500) return;
        _lastLead = DateTime.UtcNow;
        Embers();
    }

    /// <summary>
    /// The meter's burst: a few embers (nine at most) lift off the bars' burning ends and a heat glint runs along each bar,
    /// one row after another, the plate's heat flaring and cooling with them. One course, then gone. Everything in it moves
    /// by transform and opacity: the sparks are laid once on the rows' own layer, at the bars' ends as the fight says them.
    /// </summary>
    void Embers(bool storm = false)
    {
        if (Meter is not { } m || _shownE is not { } e) return;
        var rows = m.Members.Select(x => m.RowOf(x.Id)).OfType<IronRow>().ToList();
        var pts = new List<(Point, int, double)>();
        int cap = storm ? 16 : 9;
        for (int i = 0, n = 0; i < m.Members.Count && n < cap; i++)
        {
            var mm = m.Members[i];
            double y = i * IronRow.Pitch + (mm.Pinned ? 14 : 0) + IronRow.H / 2 + 3;
            double x = Math.Max(30, (mm.CpOnly ? 0 : e.BarOf(mm.F)) * _rowsW);
            int count = storm ? (i == 0 ? 5 : i < 3 ? 4 : 2) : i == 0 ? 3 : i < 3 ? 2 : 1;
            for (int k = 0; k < count && n < cap; k++, n++) pts.Add((new Point(Math.Clamp(x - 6 - (storm ? 9 : 11) * k, 8, Math.Max(8, _rowsW - 105)), y), n * 7 + i, (storm ? 30 : 22) + 4 * (count - k)));
        }
        _embers.Start(pts, cap);
        Motion.Play(_emberLayer, storm ? 1300 : 1150, t =>
        {
            _embers.Step(t);
            for (int i = 0; i < rows.Count; i++) rows[i].SetGlint(Seg(t, .05 * i, .5 + .05 * i));
            Heat(_fightHeat, .8 * (t < .2 ? Seg(t, 0, .2) : 1 - Seg(t, .2, 1)));
        }, () =>
        {
            _embers.Clear(); Heat(_fightHeat, 0);
            foreach (var r in rows) r.SetGlint(-1);
        });
    }

    // ================================================================ the moments

    /// <summary>A pickup: a band of ember light crosses the Buddy's plate, its edge warms, and the worth comes in on an iron chip beside it, then goes.</summary>
    protected override void Pickup(Pick p, bool held)
    {
        if (KinahHidden || _line.Visibility != Visibility.Visible) return;
        var worth = p.Worth.Length > 0 ? "+" + p.Worth.TrimStart('+') : p.Count;
        var words = new StackPanel { Margin = new Thickness(9, 3, 11, 4) };
        words.Children.Add(Big(worth, 16, p.Worth.Length > 0 ? Frozen(Mod.Mix(MintC2, Colors.White, .25)) : White, FontWeights.Bold));
        var nm = Tt(p.Name.Length > 13 ? p.Name[..12].TrimEnd() + "…" : p.Name, 9.5, Soft, FontWeights.SemiBold); nm.Margin = new Thickness(0, -1, 0, 0);
        words.Children.Add(nm);
        var chip = new Grid { MaxWidth = 88 };
        chip.Children.Add(new TacSketch((dc, s) => Iron(dc, s, 1, .4, big: 6, small: 2, thorns: false, g: 2)));
        chip.Children.Add(words);
        var shift = new TranslateTransform();
        chip.RenderTransform = shift;
        chip.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        Canvas.SetLeft(chip, 4); Canvas.SetTop(chip, -chip.DesiredSize.Height / 2);
        _lineAnchor.Children.Clear(); _lineAnchor.Children.Add(chip);
        var band = (TranslateTransform)_sheenBand.RenderTransform;
        double w = Math.Max(120, _line.ActualWidth > 0 ? _line.ActualWidth : FightWidth * .7);
        // the newest tile pops (1.15 and back in 0.22 s) and three embers flick up off it (on the look's stage)
        var tile = AtreiaFx.NewestTile(_line);
        var st = AtreiaFx.Stage(_fx, _root);
        var parts = new List<Action<double>> { AtreiaFx.Pop(tile, 0, .116, .15, middle: true) };
        if (tile != null && AtreiaFx.At(tile, _fx, new Point(tile.ActualWidth / 2, 2)) is { } tc)
        {
            var embers = new AtreiaFx.Bits(st);
            for (int k = 0; k < 3; k++)
                embers.Add(AtreiaFx.Flame, AtreiaFx.Br(k == 1 ? PaleC : EmberC, .95), tc + new Vector((k - 1) * 6, 0), new Vector((k - 1) * 5, -16 - 4 * (k % 2)), rot0: -90 + (k - 1) * 15,
                    s0: 6, s1: 2, delay: .02 + .02 * k, life: .26);
            parts.Add(embers.At);
        }
        var pop = new ScaleTransform(1, 1); chip.RenderTransform = new TransformGroup { Children = { pop, shift } };
        AtreiaFx.Play(_fx, st, 1900, t => AtreiaFx.All(parts, t));
        Motion.Play(chip, 1900, t =>
        {
            double k0 = Seg(t, 0, .5);
            band.X = -70 + (w + 70) * k0; _sheenBand.Opacity = t < .5 ? Math.Sin(Math.PI * k0) : 0;
            Heat(_lineHeat, t < .5 ? .55 * Math.Sin(Math.PI * k0) : 0);
            double k = Back(Seg(t, .06, .24), 3);
            shift.X = -14 * (1 - k);
            pop.ScaleX = pop.ScaleY = AtreiaFx.Punch(t, .06, .4, 0, .05);
            chip.Opacity = t < .82 ? Math.Min(1, Seg(t, .06, .16)) : 1 - Seg(t, .82, 1);
        }, () => { _sheenBand.Opacity = 0; Heat(_lineHeat, 0); _lineAnchor.Children.Remove(chip); });
    }

    /// <summary>
    /// A rare drop: its plate's edge flares in the grade's colour, a band of that light forges across the plate, the tile
    /// stamps in from half again its size and sparks fly off it. The plate and its words are whole from the first frame.
    /// </summary>
    protected override void RareDrop(Pick p, bool held)
    {
        if (_rareArt is not { } art || _rareSparks is not { } sparks) return;
        var tile = _rareTile; var heat = _rareHeat; var band = _rareBand;
        var sc = new ScaleTransform(1, 1);
        if (tile != null) { tile.RenderTransformOrigin = new Point(.5, .5); tile.RenderTransform = sc; }
        // the sparks off the tile (its place on the plate is fixed: the row's margin, the tile's 26 px)
        double tx = G + 7 + 13, ty = G + 6;
        sparks.Start(Enumerable.Range(0, 7).Select(i => (new Point(tx - 10 + 3.4 * i, ty), 40 + i, 20.0)));
        double w = Math.Max(160, _rare.ActualWidth > 0 ? _rare.ActualWidth : 260);
        var slam = new ScaleTransform(1, 1); _rare.RenderTransformOrigin = new Point(.04, .5); _rare.RenderTransform = slam;
        // the slam's flash on the plate in the grade's colour (at 40%), not white over its words
        var gc = ColorOf(p.Color, p.Grade);
        var white = new TacSketch((dc, s) => { if (s.Width > 2 * G && s.Height > 2 * G) dc.DrawGeometry(AtreiaFx.Br(gc), null, Plate(new Rect(G, G, s.Width - 2 * G, s.Height - 2 * G), 8, 3)); }) { Opacity = 0, IsHitTestVisible = false };
        _rare.Children.Add(white);
        // on the look's stage, the same 1.5 s: a column of dark fire in the grade's colour slams down onto the tile with the
        // plate, a ring of that fire and a crown of rays burst out of the tile
        var c = ColorOf(p.Color, p.Grade);
        var st = AtreiaFx.Stage(_fx, _root);
        var extra = new List<Action<double>>();
        if (AtreiaFx.At(tile, _fx, new Point(13, 13)) is { } at)
        {
            extra.Add(AtreiaFx.Pillar(st, at + new Vector(0, 6), 38, 86, CrimC, 0, .07, .5));
            // dark fire: six black-red tongues licking up off the icon for 0.4 s
            var fire = new AtreiaFx.Bits(st);
            for (int k = 0; k < 6; k++)
            {
                var col = k % 3 == 0 ? Mod.Mix(BloodC, Colors.Black, .35) : k % 3 == 1 ? CrimC : HotC;
                fire.Add(AtreiaFx.Flame, AtreiaFx.Br(col, .95), at + new Vector((k - 2.5) * 4.5, 8), new Vector((k - 2.5) * 2.5, -22 - 6 * (k % 2)), rot0: -90 + (k - 2.5) * 8,
                    s0: 10, s1: 3, delay: .06 + .025 * k, life: .27);
            }
            extra.Add(fire.At);
            extra.Add(AtreiaFx.Bloom(st, at, 100, 60, AtreiaFx.Glow(HotC, .9, PaleC), .05, .1, .5));
            extra.Add(AtreiaFx.Rays(st, at, 12, 11, 44, 3.6, i => i % 2 == 0 ? Mod.Mix(c, Colors.White, .35) : HotC, .06, .14, .62, turn: 24, max: .9));
        }
        AtreiaFx.Play(_fx, st, 1500, t => AtreiaFx.All(extra, t));
        Motion.Play(art, 1500, t =>
        {
            // the slam: from 1.12 to its size in 100 ms (inside the window), a white flash on the plate
            slam.ScaleX = slam.ScaleY = 1.12 - .12 * Out(Seg(t, 0, .07));
            white.Opacity = t < .04 ? .4 * Seg(t, 0, .04) : .4 * (1 - Seg(t, .04, .2));
            if (heat != null) Heat(heat, t < .15 ? Seg(t, 0, .15) : 1 - Seg(t, .15, .85));
            if (band != null) band.Opacity = 0;   // (its band ran past the plate's cut end: the fire carries the moment)
            sparks.Step(t is > .12 and < .95 ? Seg(t, .12, .95) : 1);
            sc.ScaleX = sc.ScaleY = AtreiaFx.Punch(t, 0, .5, .2, .3);
        }, () => { if (heat != null) Heat(heat, 0); if (band != null) band.Opacity = 0; sparks.Clear(); sc.ScaleX = sc.ScaleY = 1; slam.ScaleX = slam.ScaleY = 1; _rare.Children.Remove(white); });
    }

    /// <summary>
    /// Your best passed: a hot point runs on round the ring from your old share to full, the ring turns ember gold behind it,
    /// sparks rise off the ring and the plate's edge flares. The ring is drawn twice, once each (its old share, then gold);
    /// the moment only turns the point and fades the gold in.
    /// </summary>
    protected override void BestPassed(bool held)
    {
        if (_ringArt is not { } art || _ringGold is not { } gold || _ringHead is not { } head) return;
        double was = _share is > 0 and < 1 ? _share : .86;
        bool beat = _beat;
        _beat = false; _share = was; art.InvalidateVisual();   // once: the ring as it was
        var rot = (RotateTransform)head.RenderTransform;
        gold.Visibility = Visibility.Visible; head.Visibility = Visibility.Visible;
        // the sparks off the ring's top (its place in the line: the body's margin)
        double cx = _lineBody.Margin.Left + 21, cy = _lineBody.Margin.Top + 14;
        _lineSparks.Start(Enumerable.Range(0, 8).Select(i => { double ang = Math.PI * (.15 + .7 * i / 7.0); return (new Point(cx - 15 * Math.Cos(ang), cy - 12 * Math.Sin(ang)), 70 + i, 18.0); }));
        // the NEW BEST tag drops into the strip between the horns (over the crest) and stays for the moment
        var tag = Chip(L.T("NEW BEST"), Frozen(PaleC), EmberC, size: 11.5, face: Bahn, pad: new Thickness(9, 1, 9, 2));
        tag.HorizontalAlignment = HorizontalAlignment.Center; tag.VerticalAlignment = VerticalAlignment.Top; tag.Margin = new Thickness(0, 2, 0, 0);
        var tagDrop = new TranslateTransform(); tag.RenderTransform = tagDrop; tag.Opacity = 0; tag.IsHitTestVisible = false;
        _line.Children.Add(tag);
        // the ring punching as it turns gold, a crown of ember light flaring out of it (on the look's stage, the same 1.8 s)
        var st = AtreiaFx.Stage(_fx, _root);
        var extra = new List<Action<double>>();
        if (AtreiaFx.At(_ringHost, _fx, new Point(21, 21)) is { } rc && _ringHost != null)
        {
            var ring = AtreiaFx.ScaleOn(_ringHost, new Point(21, 21));
            extra.Add(t => ring.ScaleX = ring.ScaleY = AtreiaFx.Punch(t, 0, .92, .08, .22));
            extra.Add(AtreiaFx.Rays(st, rc, 12, 22, 42, 4.4, i => i % 2 == 0 ? EmberC : HotC, .32, .42, .9, turn: 24, max: .9));
            extra.Add(AtreiaFx.Bloom(st, rc, 76, 76, AtreiaFx.Glow(EmberC, .95, PaleC), .32, .38, .7));
        }
        AtreiaFx.Play(_fx, st, 1800, t => AtreiaFx.All(extra, t));
        Motion.Play(art, 1800, t =>
        {
            double d = Seg(t, .3, .4);
            tagDrop.Y = -12 * (1 - Back(d, 2));
            tag.Opacity = t < .3 ? 0 : t < .88 ? Math.Min(1, d * 2) : 1 - Seg(t, .88, 1);
            double k = Out(Seg(t, 0, .45));
            rot.Angle = 360 * (was + (1 - was) * k);
            head.Opacity = t < .45 ? Math.Min(1, Seg(t, 0, .08) * 1.5) : 1 - Seg(t, .45, .6);
            gold.Opacity = Seg(t, .3, .48);
            _lineSparks.Step(t is > .35 and < 1 ? Seg(t, .35, 1) : 1);
            Heat(_lineHeat, t < .45 ? Seg(t, 0, .45) : 1 - Seg(t, .45, 1));
        }, () =>
        {
            _beat = beat; _share = was; art.InvalidateVisual();
            gold.Opacity = 0; gold.Visibility = Visibility.Collapsed; head.Visibility = Visibility.Collapsed; _lineSparks.Clear(); Heat(_lineHeat, 0); _line.Children.Remove(tag);
        });
    }

    /// <summary>
    /// An entry opened: shadow flame catches on the chip: the chips duck and punch out, a hot flash runs through it, a
    /// crimson ring ripples off its edge and tongues of flame and embers lick up off it; then it rests.
    /// </summary>
    protected override void EntryOpened(TimerBoard.Item i, bool held)
    {
        if (_firstChip is not { } chip || AtreiaFx.Box(chip, _fx) is not { } r) return;
        var st = AtreiaFx.Stage(_fx, _root);
        var pop = AtreiaFx.ScaleOn(_timers, chip.TranslatePoint(new Point(chip.ActualWidth / 2, chip.ActualHeight / 2), _timers));
        var c = new Point(r.X + r.Width / 2, r.Y + r.Height / 2);
        var parts = new List<Action<double>>
        {
            AtreiaFx.Bloom(st, c, r.Width + 40, r.Height + 30, AtreiaFx.Glow(HotC, .9, PaleC), .1, .17, .55, s0: .5),
            AtreiaFx.Streak(st, c, r.Width + 30, 6, HotC, .08, .16, .48),
            AtreiaFx.Ripple(st, r, 2, AtreiaFx.Br(HotC), 2, 6, .14, .45),
        };
        var bits = new AtreiaFx.Bits(st);
        // the flames lick up off the chip's top edge, the embers drift higher
        for (int k = 0; k < 6; k++)
        {
            var o = new Point(r.X + r.Width * (k + .5) / 6, r.Y + 2);
            bits.Add(AtreiaFx.Flame, AtreiaFx.Br(k % 2 == 0 ? HotC : EmberC, .95), o, new Vector((k - 2.5) * 2, -12 - 4 * (k % 3)), rot0: -90 + (k - 2.5) * 6, s0: 7, s1: 2,
                delay: .12 + .03 * (k % 3), life: .5);
        }
        bits.Burst(5, AtreiaFx.Dot, k => AtreiaFx.Br(k % 2 == 0 ? PaleC : EmberC), c, 18, 1.6, .16, .7, fall: -14, dir: -90, spread: 150, r0: r.Height / 2, seed: 71);
        parts.Add(bits.At);
        AtreiaFx.Play(_fx, st, 1150, t =>
        {
            pop.ScaleX = pop.ScaleY = AtreiaFx.Punch(t, 0, .72, .06, .08);
            AtreiaFx.All(parts, t);
        }, () => pop.ScaleX = pop.ScaleY = 1);
    }

    /// <summary>A fight started: the plate rises in as it fades up, the crown slams onto the top row, embers lift off the bars.</summary>
    protected override void FightStarted(Encounter e, bool held)
    {
        Cut(Color.FromRgb(0xE0, 0x1C, 0x2A), 300);
        Motion.Play(_fight, 360, t => _fightShift.Y = 5 * (1 - Out(t)), () => _fightShift.Y = 0);
        SigilFlare();
        if (Meter is { Members.Count: > 0 } m && m.RowOf(m.Members[0].Id) is IronRow top) top.Slam();
        Embers();
    }

    /// <summary>
    /// A fight ended: an iron banner stamps down over the fight's head with how it went and the clock (ember gold for a kill,
    /// ash for a wipe), its edge flaring; a kill sends the embers up the bars. It lifts away after a moment. The banner sits
    /// in the head's own grid (laid out with it once); the moment moves it by scale and opacity alone.
    /// </summary>
    protected override void FightEnded(Encounter e, bool held)
    {
        if (e.Outcome is not ("kill" or "wipe" or "cleared") || _head is not { } head) return;
        bool wipe = e.Outcome == "wipe";
        var c = wipe ? AshC : GoldC2;
        var band = new Grid { Margin = new Thickness(-G - 6, -G - 5, -G - 6, -G - 5), IsHitTestVisible = false, Tag = "end-banner" };
        Grid.SetColumnSpan(band, 2); Panel.SetZIndex(band, 10);
        band.Children.Add(new TacSketch((dc, s) =>
        {
            // a solid ground first, so the head under it never shows through the iron
            if (s.Width > 2 * G && s.Height > 2 * G) dc.DrawGeometry(Mod.Br(IronC), null, Plate(new Rect(G, G, s.Width - 2 * G, s.Height - 2 * G), 9, 3));
            Iron(dc, s, 1, 0, wipe ? AshC : null, big: 9);
        }));
        var flare = HeatLayer(9, 3, wipe ? AshC : null);
        band.Children.Add(flare);
        var words = new Grid { Margin = new Thickness(G + 10, G + 2, G + 10, G + 2), VerticalAlignment = VerticalAlignment.Center };
        words.ColumnDefinitions.Add(new ColumnDefinition()); words.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var ot = Head(OutcomeText(e).ToUpper(System.Globalization.CultureInfo.CurrentCulture), 25, Frozen(Mod.Mix(c, Colors.White, .3)), FontWeights.Bold);
        if (!wipe) ot.Effect = Underglow(.6);
        words.Children.Add(ot);
        var ck = Big(Clock(e.Duration(e.End ?? DateTime.UtcNow)), 21, White);
        Grid.SetColumn(ck, 1); words.Children.Add(ck);
        band.Children.Add(words);
        var sc = new ScaleTransform(1, 1); var drop = new TranslateTransform();
        band.RenderTransformOrigin = new Point(.5, .5); band.RenderTransform = new TransformGroup { Children = { sc, drop } };
        foreach (var old in head.Children.OfType<Grid>().Where(g => Equals(g.Tag, "end-banner")).ToList()) head.Children.Remove(old);
        head.Children.Add(band);
        Motion.Play(band, 2400, t =>
        {
            // it drops from the crest in 120 ms with an overshoot, holds, lifts away at the end
            double i = Seg(t, 0, .05), o = Seg(t, .88, 1);
            band.Opacity = Math.Min(1, i * 3) * (1 - o);
            drop.Y = -30 * (1 - Back(i, 2.2)) - 10 * Out(o);
            sc.ScaleX = sc.ScaleY = 1 + .05 * (1 - Out(Seg(t, .05, .12)));
            if (!wipe) Heat(flare, t < .14 ? 0 : 1 - Seg(t, .14, .6));
        }, () => head.Children.Remove(band));
        if (!wipe) { Shockwave(); Embers(storm: true); }
        // on the look's stage: a kill's hit (the plate punches as the banner lands, a hot flare line across the head, embers
        // flung out of the banner); a wipe is the shadow swallowing the plate: it shakes, darkness closes in from its edges,
        // a dying red glow at the head, ash falling
        var st = AtreiaFx.Stage(_fx, _root);
        var parts = new List<Action<double>>();
        double hy = head.ActualHeight > 0 && head.IsDescendantOf(_fight) ? head.TranslatePoint(new Point(0, head.ActualHeight / 2), _fight).Y : FightRoom.Top + 16;
        if (AtreiaFx.Box(_fight, _fx) is { } b)
        {
            var hit = new Point(b.X + b.Width / 2, b.Y + hy);
            var bits = new AtreiaFx.Bits(st);
            if (!wipe)
            {
                parts.Add(AtreiaFx.Streak(st, hit, b.Width * 1.05, 8, HotC, .03, .08, .4));
                // seven iron spikes flung out of the banner, outward and down (never up past the horns)
                bits.Burst(7, AtreiaFx.Shard, i => AtreiaFx.Br(i % 2 == 0 ? CrimC : Mod.Mix(HotC, PaleC, .3)), hit, b.Width * .3, 9, .04, .45, fall: 18, dir: 90, spread: 170, r0: 16, spin: 120, seed: 83, squash: .5);
                bits.Burst(10, AtreiaFx.Flame, i => AtreiaFx.Br(i % 3 == 0 ? PaleC : i % 3 == 1 ? EmberC : HotC), hit, b.Width * .3, 7, .05, .6, fall: -18, dir: -90, spread: 300, r0: 24, spin: 40, seed: 73, squash: .45);
            }
            else
            {
                parts.Add(AtreiaFx.Veil(st, new Rect(b.X + FightRoom.Left - 1, b.Y + FightRoom.Top - 8, Math.Max(10, b.Width - FightRoom.Left - FightRoom.Right + 2), Math.Max(10, b.Height - FightRoom.Top - 2)), Color.FromRgb(0x05, 0x01, 0x06), .9, .02, .16, .7));
                parts.Add(AtreiaFx.Bloom(st, hit, b.Width * .8, 70, AtreiaFx.Glow(BloodC, .9, CrimC), .02, .08, .5, s0: 1));
                var rnd = new Random(79);
                for (int k = 0; k < 10; k++)
                {
                    var o = new Point(b.X + 14 + (b.Width - 28) * (k + .5) / 10, b.Y + hy + 8 + 16 * rnd.NextDouble());
                    bits.Add(k % 3 == 0 ? AtreiaFx.Shard : AtreiaFx.Dot, AtreiaFx.Br(k % 2 == 0 ? AshC : Mod.Mix(AshC, Colors.Black, .3), .9), o, new Vector((rnd.NextDouble() - .5) * 12, 6),
                        fall: 46 + 34 * rnd.NextDouble(), rot0: 360 * rnd.NextDouble(), spin: (rnd.NextDouble() - .5) * 200, s0: k % 3 == 0 ? 7 : 3, s1: k % 3 == 0 ? 4 : 1.8,
                        delay: .05 + .15 * rnd.NextDouble(), life: .7, sway: 3);
                }
            }
            parts.Add(bits.At);
        }
        _fightPunch.CenterX = _fight.ActualWidth / 2; _fightPunch.CenterY = hy;
        // the course in the banner's 2.4 s; the light's parts play in its first 1.5 s
        AtreiaFx.Play(_fx, st, 2400, t =>
        {
            double k = Math.Min(1, t * 2400 / 1500);
            AtreiaFx.All(parts, k);
            // a wipe lands like dropped iron: a thunk down 3 px and back in 0.12 s, then the shake
            if (wipe) { _fightShift.X = AtreiaFx.Shake(k, .06, .46, 5, 3.5); _fightShift.Y = 3 * AtreiaFx.Flash(k, 0, .03, .08); }
            else _fightPunch.ScaleX = _fightPunch.ScaleY = AtreiaFx.Punch(k, 0, .5, .012, .026);
        }, () => { _fightShift.X = 0; _fightShift.Y = 0; _fightPunch.ScaleX = _fightPunch.ScaleY = 1; });
    }

    /// <summary>
    /// A band of light (a cut) sweeping across the fight's plate, left to right: one frozen path, moved by its transform.
    /// </summary>
    void Cut(Color c, double ms)
    {
        double h = Math.Max(60, _fight.ActualHeight > 0 ? _fight.ActualHeight : 200), w = Math.Max(200, _fight.ActualWidth > 0 ? _fight.ActualWidth : FightWidth);
        var line = new LineGeometry(new Point(h * .5, 0), new Point(0, h)); line.Freeze();
        var move = new TranslateTransform(-h * .5 - 10, 0);
        var body = new System.Windows.Shapes.Path { Data = line, Stroke = SlashBody, StrokeThickness = 10, IsHitTestVisible = false, Opacity = 0, RenderTransform = move };
        var core = new System.Windows.Shapes.Path { Data = line, Stroke = Brushes.White, StrokeThickness = 2, IsHitTestVisible = false, Opacity = 0, RenderTransform = move };
        _fightFx.Children.Add(body); _fightFx.Children.Add(core);
        double end = w - 70;   // it fades out before the figure column
        Motion.Play(body, ms, t =>
        {
            move.X = -h * .5 - 10 + (end + h * .5) * Out(t);
            double x = move.X + h * .25;
            body.Opacity = core.Opacity = Math.Min(1, Seg(t, 0, .12) * 1.2) * (1 - Seg(x, end - 120, end));
        }, () => { _fightFx.Children.Remove(body); _fightFx.Children.Remove(core); });
    }

    static readonly Brush SlashBody = Frozen(Color.FromRgb(0xE0, 0x10, 0x1A));
    static readonly Geometry CutShape = MakeCut();
    static Geometry MakeCut() { var g = Geometry.Parse("M 76,0 L 90,0 L 14,100 L 0,100 Z"); g.Freeze(); return g; }
    static readonly Brush SlashFill = Mod.Grad(false, (Color.FromArgb(0x00, 0xE0, 0x1C, 0x2A), 0), (Color.FromArgb(0xD0, 0xE0, 0x1C, 0x2A), .3), (Color.FromArgb(0xFF, 0xFF, 0xF2, 0xE0), .5), (Color.FromArgb(0xD0, 0xE0, 0x1C, 0x2A), .7), (Color.FromArgb(0x00, 0xE0, 0x1C, 0x2A), 1));

    /// <summary>The flare's fade from its left (a mask): clear over its part past the plate's box, whole by its middle.</summary>
    static readonly Brush FlareFade = Mod.Grad(false, (Colors.Transparent, 0), (Colors.Transparent, .4), (Colors.Black, .6), (Colors.Black, 1));

    /// <summary>The blood moon behind the title flares: a bright copy of it (drawn once) scales from 1.2 down to its place and fades.</summary>
    void SigilFlare()
    {
        double r = 30, cx = FightRoom.Left + 2, cy = FightRoom.Top + 16;
        var flare = new TacSketch((dc, s) => { BloodMoon(dc, new Point(r * 1.4, r * 1.4), r); dc.DrawEllipse(SigilHot, null, new Point(r * 1.4, r * 1.4), r * .9, r * .9); }, r * 2.8, r * 2.8)
            { Opacity = 0, RenderTransformOrigin = new Point(.5, .5), RenderTransform = new ScaleTransform(1.2, 1.2), OpacityMask = FlareFade };
        Canvas.SetLeft(flare, cx - r * 1.4); Canvas.SetTop(flare, cy - r * 1.4);
        _fightFx.Children.Add(flare);
        Motion.Play(flare, 700, t =>
        {
            // from 1.2 (its top stays inside the plate's box), its left side faded out before the box's edge: never cut hard
            var sc = (ScaleTransform)flare.RenderTransform; sc.ScaleX = sc.ScaleY = 1.2 - .2 * Out(Seg(t, 0, .4));
            flare.Opacity = t < .15 ? Seg(t, 0, .15) : 1 - Seg(t, .3, 1);
        }, () => _fightFx.Children.Remove(flare));
    }

    static readonly Brush SigilHot = MakeRadial(Color.FromArgb(0x90, 0xFF, 0xC0, 0x90), Color.FromArgb(0, 0xFF, 0x3A, 0x2E), Color.FromArgb(0x60, 0xFF, 0x4A, 0x3D));

    /// <summary>
    /// A kill's shockwave: a blood-red flash fills the plate and two rings (a hot crimson one and a wide blood one behind it)
    /// burst out from the banner across the whole meter, clipped to the fight's bounds. Three elements, scale and opacity only.
    /// </summary>
    void Shockwave()
    {
        double w = Math.Max(200, _fight.ActualWidth > 0 ? _fight.ActualWidth : FightWidth), h = Math.Max(80, _fight.ActualHeight > 0 ? _fight.ActualHeight : 200);
        var at = new Point(w / 2, FightRoom.Top + 16);
        double D = .8 * Math.Max(w, h);
        var flash = new System.Windows.Shapes.Rectangle { Width = w, Height = h, Fill = AtreiaFx.Glow(Color.FromRgb(0xFF, 0x3A, 0x30), .6), IsHitTestVisible = false, Opacity = 0 };
        System.Windows.Shapes.Ellipse Ring(Brush b, double th, double d) => new() { Width = d, Height = d * .62, Stroke = b, StrokeThickness = th, IsHitTestVisible = false, Opacity = 0, RenderTransformOrigin = new Point(.5, .5), RenderTransform = new ScaleTransform(.2, .2) };
        var blood = Ring(ShockBlood, 8, D); var hot = Ring(ShockHot, 4, D); var core = Ring(Brushes.White, 1, D - 4);
        foreach (var (r, d) in new[] { (blood, D), (hot, D), (core, D - 4) }) { Canvas.SetLeft(r, at.X - d / 2); Canvas.SetTop(r, at.Y - d * .31); }
        double figX = FightRoom.Left + FightPad + Math.Max(60, _rowsW - 110);
        // the rings kept to the head's band, short of the figures' column: they never cross the rows' names
        var layer = new Canvas { IsHitTestVisible = false, Clip = new RectangleGeometry(new Rect(FightRoom.Left, 0, Math.Max(10, figX - FightRoom.Left), FightRoom.Top + 34)) };
        // the flash over the whole plate, soft to its edges (never cut at the figures' column); only the rings clipped short of it
        // (the rings are not shown any more: they ran past the plate at its horn; spikes fly from the banner in their place)
        _fightFx.Children.Add(flash);
        _fightFx.Children.Add(layer);
        void Scale(System.Windows.Shapes.Ellipse r, double k) { var sc = (ScaleTransform)r.RenderTransform; sc.ScaleX = sc.ScaleY = .2 + 1.2 * k; }
        Motion.Play(hot, 900, t =>
        {
            flash.Opacity = t < .06 ? Seg(t, 0, .06) * .45 : .45 * (1 - Seg(t, .06, .45));
            double a = Seg(t, 0, .4), b = Seg(t, .05, .5);
            Scale(hot, Out(a)); Scale(core, Out(a)); Scale(blood, Out(b));
            hot.Opacity = core.Opacity = a is > 0 and < 1 ? 1 - a : 0;
            blood.Opacity = b is > 0 and < 1 ? .9 * (1 - b) : 0;
        }, () => { _fightFx.Children.Remove(layer); _fightFx.Children.Remove(flash); });
    }

    static readonly Brush KillFlash = MakeRadial(Color.FromArgb(0x70, 0xFF, 0x3A, 0x30), Color.FromArgb(0, 0x8E, 0x0F, 0x1C), Color.FromArgb(0x40, 0xC2, 0x1F, 0x2E));
    static readonly Brush ShockHot = Frozen(Color.FromRgb(0xFF, 0x2A, 0x2A)), ShockBlood = Frozen(Color.FromArgb(0xC0, 0xA0, 0x0E, 0x1C));
}
