using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Effects;
using KinahLedger.Parser;
using KinahLedger.Session;
using static KinahLedger.Ui.HudKit;

namespace KinahLedger.Ui;

/// <summary>
/// Line (the Modern theme; Steven, 2026-11-17, of the mock-up: "fantastic"): barely there. A faint dark wash inside a
/// hairline white frame with 12 px corners, white Inter with a soft dark shade so every word reads over any scene, and
/// under each meter row a slim class-coloured bar (4 px, rounded, a soft glow) on a faint track; your row's name is
/// underlined. Marks are outline chips; the Buddy is a hairline pill: the ring of your best, the hour with the best drop and
/// the last pickups as tiles beside it, the session, the next timer as an outline chip at its end.
/// The moments stay as quiet as the look: a pickup brightens the pill's hairline and its words rise in; a rare drop's
/// pill traces its outline in its grade's colour; your best passed rings out from the ring in mint; an entry opening
/// pulses the timer chip's outline; a fight's frame draws itself round as the rows come up; a kill stamps its chip and a
/// light runs along the top row's bar, a wipe flashes the frame red.
/// </summary>
sealed partial class HudModernLine : ModernLook
{
    static readonly Color WhiteC = Colors.White, InkC0 = Color.FromRgb(0x0B, 0x0C, 0x10), MintC0 = Color.FromRgb(0x5D, 0xF2, 0xA6),
        RedC0 = Color.FromRgb(0xFF, 0x6B, 0x6B), GoldC0 = Color.FromRgb(0xF5, 0xC5, 0x42);
    static readonly SolidColorBrush WhiteB = Frozen(WhiteC), SoftB = Mod.Br(WhiteC, .86), DimB = Mod.Br(WhiteC, .66), FaintB = Mod.Br(WhiteC, .45),
        MintB = Frozen(MintC0), RedB = Frozen(RedC0), InkB = Frozen(InkC0), EdgeB = Mod.Br(WhiteC, .45), ChipEdgeB = Mod.Br(WhiteC, .78), ChipBgB = Mod.Br(Colors.Black, .25),
        TrackB = Mod.Br(WhiteC, .18), RuleB = Mod.Br(WhiteC, .35);

    /// <summary>The soft dark shade under every word (the mock-up's text-shadow): reads over snow and sky without a panel.</summary>
    static readonly DropShadowEffect ShadeFx = MakeShade();
    static DropShadowEffect MakeShade()
    {
        var e = new DropShadowEffect { Color = Colors.Black, Direction = 270, ShadowDepth = 1, BlurRadius = 5, Opacity = .9, RenderingBias = RenderingBias.Quality };
        e.Freeze();
        return e;
    }

    // room round the look for its effects (the ring's ripple, a chip's pulse): never drawn past the window's edge
    readonly Grid _root = new() { Margin = new Thickness(6, 6, 8, 6), HorizontalAlignment = HorizontalAlignment.Left };
    readonly StackPanel _body = new() { HorizontalAlignment = HorizontalAlignment.Left };
    double _k = .9;

    public HudModernLine() : base(bare: true)
    {
        UseRows(LineRow.Pitch, m => new LineRow(this), 18, GapLine);
        BuildPill();
        BuildFight();
        _body.Children.Add(_fightFill);
        _body.Children.Add(_timerUpHost);
        _body.Children.Add(_runHead);
        _body.Children.Add(_pillHost);
        _body.Children.Add(_under);
        _root.Children.Add(_body);
        TextElement.SetFontFamily(_root, Mod.Face);
        TextOptions.SetTextFormattingMode(_root, TextFormattingMode.Display);
        Paint();
    }

    public override FrameworkElement Root => _root;
    public override Thickness Inset => _root.Margin;
    public override double Corner => 12;
    public override FrameworkElement? FightFrame => _fightFill.Visibility == Visibility.Visible ? _fightHost : null;

    /// <summary>The wash behind the frames follows the panel's opacity setting: 18% black at the usual 0.9.</summary>
    Brush Wash => Mod.Br(Colors.Black, .2 * _k);

    public override void SetOpacity(double opacity)
    {
        _k = Math.Clamp(opacity, 0.3, 1);
        Paint();
    }

    void Paint()
    {
        _pill.Background = Wash;
        _fight.Background = Wash;
        if (_rarePill != null) _rarePill.Background = Wash;
    }

    protected override string LineSig(Figures f)
    {
        var p = Picks(Values).FirstOrDefault();
        return base.LineSig(f) + "|" + (Rare(Values)?.Id ?? "") + "|" + (p == null ? "" : p.Id + p.Worth + p.Count) + "|" + Values.GetValueOrDefault("paused", "") + "|" + PickStrip.Sig(Values, Strip) + "|" + RunBuddy.Sig(Values);
    }

    static Run R(string s, double size, Brush fg, FontWeight? w = null) => new(s) { FontSize = size, Foreground = fg, FontWeight = w ?? FontWeights.SemiBold };

    /// <summary>An outline chip (the look's marks): a hairline round small bold words on a faint dark ground.</summary>
    static Border Chip(string s, Brush edge, Brush fg, double size = 9, Brush? fill = null, double padX = 5)
    {
        var t = Tt(s, size, fg, FontWeights.Bold);
        t.Margin = new Thickness(0, 0, 0, .5);
        return new Border
        {
            BorderBrush = edge, BorderThickness = new Thickness(1), Background = fill ?? ChipBgB, CornerRadius = new CornerRadius(4),
            Padding = new Thickness(padX, 0, padX, 0), VerticalAlignment = VerticalAlignment.Center, Child = t,
        };
    }

    // ================================================================ the Buddy: a hairline pill

    readonly Grid _pillHost = new() { HorizontalAlignment = HorizontalAlignment.Left };
    readonly Border _pill = new() { BorderBrush = EdgeB, BorderThickness = new Thickness(1), Padding = new Thickness(5, 5, 7, 5), HorizontalAlignment = HorizontalAlignment.Left };
    readonly StackPanel _pillIn = new() { Orientation = Orientation.Horizontal, Effect = ShadeFx };
    readonly Grid _ringHost = new() { Width = 40, Height = 40, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0) };
    TacSketch _ring = null!, _pillFx = null!;
    readonly TextBlock _ringText = Tt("", 13, WhiteB, FontWeights.Bold);
    readonly TextBlock _rate = Tt("", 22, WhiteB, FontWeights.Bold), _sub = Tt("", 11.5, SoftB, FontWeights.SemiBold), _pick = Tt("", 11.5, SoftB, FontWeights.SemiBold);
    readonly StackPanel _kv = new() { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(2, 0, 4, 0) };
    readonly StackPanel _subLine = new() { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 1, 0, 0) };
    readonly TranslateTransform _pickShift = new();
    readonly Grid _timerSlot = new() { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 2, 0) };
    /// <summary>The pickup strip beside the hour (farming), and the timer chip's place over the pill when the strip takes its room.</summary>
    readonly Border _stripSlot = new() { VerticalAlignment = VerticalAlignment.Center, Visibility = Visibility.Collapsed };
    readonly Grid _timerUpHost = new() { HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 0, 0, 5), Visibility = Visibility.Collapsed };
    bool _timerUp;
    /// <summary>The timer's room the strip was fitted to (-1: no strip time); a chip that changes it draws the line again.</summary>
    double _stripTimerW = -1;
    /// <summary>The pickup strip in Line: 24 px tiles beside the hour, the hairline rule, a gold star.</summary>
    static readonly PickStrip.Look Strip = new(24, 4, true, RuleB, Frozen(GoldC0));
    readonly StackPanel _under = new() { HorizontalAlignment = HorizontalAlignment.Left };
    readonly TextBlock _note = Tt("", 11.5, Warn, FontWeights.SemiBold);
    Border? _rarePill;
    TacSketch? _rareFx;
    double _ringShare, _ringPulse = -1, _pillFlash = -1, _rareTrace = -1, _chipPulse = -1;
    bool _ringBeat, _paused;
    Color _rareC = WhiteC;
    (string Text, TimerBadge.Tone Tone)? _timer;

    void BuildPill()
    {
        _ring = new TacSketch(DrawRing, 40, 40);
        _ringText.HorizontalAlignment = HorizontalAlignment.Center;
        _rate.TextTrimming = TextTrimming.None;
        _pick.RenderTransform = _pickShift;
        _subLine.Children.Add(_sub); _subLine.Children.Add(_pick);
        _kv.Children.Add(_rate); _kv.Children.Add(_subLine);
        _pillIn.Children.Add(_ringHost); _pillIn.Children.Add(_kv); _pillIn.Children.Add(_stripSlot); _pillIn.Children.Add(_timerSlot);
        _pill.Child = _pillIn;
        _pill.SizeChanged += (_, _) => _pill.CornerRadius = new CornerRadius(_pill.ActualHeight / 2);
        _pillFx = new TacSketch(DrawPillFx);
        _pillHost.Children.Add(_pill); _pillHost.Children.Add(_pillFx);
        _note.Effect = ShadeFx; _note.TextTrimming = TextTrimming.CharacterEllipsis; _note.MaxWidth = 380;
    }

    /// <summary>The ring of your best: a faint track, the share in white (mint once beaten), round caps; its ripple when you pass it.</summary>
    void DrawRing(DrawingContext dc, Size s)
    {
        const double st = 3.5;
        var c = new Point(s.Width / 2, s.Height / 2);
        double r = (Math.Min(s.Width, s.Height) - st) / 2;
        dc.DrawEllipse(null, Mod.P(Mod.Br(WhiteC, .25), st), c, r, r);
        var tone = _paused ? Mod.Br(WhiteC, .5) : _ringBeat ? MintB : WhiteB;
        double share = Math.Clamp(_ringShare, 0, 1);
        if (share >= .999) dc.DrawEllipse(null, Mod.P(tone, st), c, r, r);
        else if (share > .004)
        {
            double a = 2 * Math.PI * share;
            var g = new StreamGeometry();
            using (var x = g.Open())
            {
                x.BeginFigure(new Point(c.X, c.Y - r), false, false);
                x.ArcTo(new Point(c.X + r * Math.Sin(a), c.Y - r * Math.Cos(a)), new Size(r, r), 0, a > Math.PI, SweepDirection.Clockwise, true, false);
            }
            g.Freeze();
            dc.DrawGeometry(null, Mod.P(tone, st, round: true), g);
        }
        if (_ringPulse is > 0 and < 1)
        {
            // your best passed: two mint rings go out from the ring and fade (6 px at most: inside the look's room)
            for (int k = 0; k < 2; k++)
            {
                double p = Math.Clamp(_ringPulse * 1.6 - k * .45, 0, 1);
                if (p <= 0 || p >= 1) continue;
                double rr = r + st / 2 + 6 * Out(p);
                dc.DrawEllipse(null, Mod.P(Mod.Br(MintC0, (1 - p) * .9), 2 * (1 - p) + .6), c, rr, rr);
            }
        }
    }

    /// <summary>The pill's own moment: its hairline brightening (a pickup) or pulsing out (an entry opened is the chip's).</summary>
    void DrawPillFx(DrawingContext dc, Size s)
    {
        if (_pillFlash is not (> 0 and < 1) || _pill.ActualWidth <= 0) return;
        double a = Math.Sin(Math.PI * _pillFlash);
        var r = new Rect(.5, .5, _pill.ActualWidth - 1, _pill.ActualHeight - 1);
        dc.DrawGeometry(null, Mod.P(Mod.Br(WhiteC, .55 * a), 1.2), Mod.Pill(r, r.Height / 2));
    }

    protected override void DrawLine(Figures f)
    {
        _paused = Values.TryGetValue("paused", out var ps) && ps.Length > 0;
        // the ring of your best (in the Abyss the points' mark)
        _ringHost.Children.Clear();
        // in a dungeon run: the run's clock, splits and verdict in place of the hour (HudModernLine.Run.cs)
        if (RunNow() is { } rs) { DrawRun(rs, f); return; }
        _runHead.Visibility = Visibility.Collapsed;
        if (f.Abyss && f.Counted && f.Icon.Length > 0) _ringHost.Children.Add(AbyssMark(f.Icon, 34));
        else if (HasRing(f))
        {
            _ringShare = f.Share; _ringBeat = f.Beat;
            _ringText.Text = f.Beat ? "★" : Pct(f.Share);
            _ringText.FontSize = f.Beat ? 17 : 13;
            _ringText.Foreground = f.Beat ? MintB : WhiteB;
            _ringHost.Children.Add(_ring); _ringHost.Children.Add(_ringText);
            _ring.InvalidateVisual();
        }
        _ringHost.Visibility = _ringHost.Children.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        _pill.Padding = new Thickness(_ringHost.Visibility == Visibility.Visible ? 5 : 13, 5, 7, 5);

        // the hour: the one big figure, its unit small after it; warming up smaller and quieter; nothing yet: the waiting line
        _rate.Inlines.Clear();
        if (!HasHour(f)) _rate.Inlines.Add(R(Waiting(f.Abyss), 13, SoftB));
        else if (f.Warm) _rate.Inlines.Add(R(f.Rate, 15, DimB));
        else
        {
            _rate.Inlines.Add(R(f.Rate, 22, WhiteB, FontWeights.Bold));
            if (f.Unit.Length > 0) _rate.Inlines.Add(R((f.Abyss ? " " : "") + f.Unit, 11.5, DimB));
        }

        // under it: the session, then the Abyss's stats, your best passed or the latest pickup
        _sub.Inlines.Clear(); _pick.Inlines.Clear();
        if (HasHour(f) && f.Total.Length > 0) { _sub.Inlines.Add(R(f.Total, 11.5, WhiteB, FontWeights.Bold)); _sub.Inlines.Add(R(" " + f.TotalWord, 11.5, SoftB)); }
        if (f.Stats.Count > 0)
            foreach (var (fig, word, tone) in f.Stats)
            {
                if (_sub.Inlines.Count > 0) _sub.Inlines.Add(R("  ·  ", 11, FaintB));
                _sub.Inlines.Add(R(fig, 11.5, ReferenceEquals(tone, Rec) ? RedB : ReferenceEquals(tone, Gold) ? Frozen(GoldC0) : WhiteB, FontWeights.Bold));
                _sub.Inlines.Add(R(" " + word, 11.5, SoftB));
            }
        else if (f.Beat && f.ToBest.Length > 0 && HasHour(f)) { _pick.Inlines.Add(R("  ·  ", 11, FaintB)); _pick.Inlines.Add(R("★ " + L.T("New best"), 11.5, MintB, FontWeights.Bold)); }
        else if (HasHour(f) && Picks(Values).FirstOrDefault() is { } p)
        {
            if (_sub.Inlines.Count > 0) _pick.Inlines.Add(R("  ·  ", 11, FaintB));
            var worth = p.Worth.Length > 0 ? (p.Worth.StartsWith('+') || p.Worth.StartsWith('−') ? p.Worth : "+" + p.Worth) : p.Count;
            _pick.Inlines.Add(R(p.Name, 11.5, SoftB));
            if (worth.Length > 0 && !p.Name.Contains(worth.TrimStart('+'), StringComparison.Ordinal)) _pick.Inlines.Add(R(" " + worth, 11.5, p.Kinah ? Frozen(GoldC0) : MintB, FontWeights.Bold));
        }
        _pick.MaxWidth = 200; _pick.TextTrimming = TextTrimming.CharacterEllipsis;

        // farming, the pickups as the strip beside the hour where it fits (the best drop and the last three), the latest
        // pickup's words then left out; with the timer chip at the pill's end and no room left beside it, the chip goes over the pill
        _stripSlot.Child = null; _timerUp = false; _stripTimerW = -1;
        if (StripTime(f))
        {
            bool pickInWords = !(f.Beat && f.ToBest.Length > 0);
            double timerW = TimerChipW();
            double baseW = 2 + _pill.Padding.Left + _pill.Padding.Right + (_ringHost.Visibility == Visibility.Visible ? Mod.W(_ringHost) : 0) + 6;
            double rateW = Mod.W(_rate), subW = _sub.Inlines.Count > 0 ? Mod.W(_sub) : 0, pickW = _pick.Inlines.Count > 0 ? Mod.W(_pick) : 0;
            double wordsW = Math.Max(rateW, subW + (pickInWords ? 0 : pickW)), oldW = Math.Max(rateW, subW + pickW);
            FrameworkElement? strip = null;
            foreach (var look in StripFall(Strip))
            {
                if ((strip = StripIn(StripRoom(FightWidth - 14, baseW + timerW, wordsW, oldW, 16), look)) != null) break;
                if (timerW > 0 && (strip = StripIn(StripRoom(FightWidth - 14, baseW, wordsW, oldW + timerW, 16), look)) != null) { _timerUp = true; break; }
            }
            if (strip != null)
            {
                if (pickInWords) _pick.Inlines.Clear();
                strip.Margin = new Thickness(10, 0, timerW > 0 && !_timerUp ? 0 : 6, 0);
                _stripSlot.Child = strip;
            }
            _stripTimerW = timerW;
        }
        _stripSlot.Visibility = _stripSlot.Child != null ? Visibility.Visible : Visibility.Collapsed;
        _sub.Visibility = _sub.Inlines.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        _pick.Visibility = _pick.Inlines.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        _subLine.Visibility = _sub.Visibility == Visibility.Visible || _pick.Visibility == Visibility.Visible ? Visibility.Visible : Visibility.Collapsed;

        // paused (the chat can't be read): the pill stays solid but quiet, the reason in orange under it
        _kv.Opacity = _ringHost.Opacity = _stripSlot.Opacity = _paused ? .5 : 1;
        DrawTimerChip();
        DrawUnder(f);
    }

    /// <summary>Under the pill: a rare drop's own hairline pill in its grade's colour, then the note (paused, a notice).</summary>
    void DrawUnder(Figures f)
    {
        _under.Children.Clear();
        _rarePill = null; _rareFx = null;
        if (KinahHidden) return;
        if (Rare(Values) is { } r)
        {
            _rareC = ColorOf(r.Color, r.Grade);
            var words = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 2, 0) };
            words.Children.Add(Tt(L.T("Rare drop").ToUpper(CultureInfo.CurrentCulture), 8.5, Frozen(Mod.Mix(_rareC, WhiteC, .25)), FontWeights.Bold));
            var nm = Tt("", 13, WhiteB, FontWeights.Bold);
            nm.Inlines.Add(R(r.Name, 13, WhiteB, FontWeights.Bold));
            nm.Margin = new Thickness(0, -1, 0, 0);
            words.Children.Add(nm);
            var row = new StackPanel { Orientation = Orientation.Horizontal, Effect = ShadeFx };
            row.Children.Add(Tile(r, 24)); row.Children.Add(words);
            _rarePill = new Border { BorderBrush = Mod.Br(_rareC, .85), BorderThickness = new Thickness(1), Background = Wash, Padding = new Thickness(5, 4, 12, 4), Child = row };
            var rp = _rarePill;
            rp.SizeChanged += (_, _) => rp.CornerRadius = new CornerRadius(rp.ActualHeight / 2);
            _rareFx = new TacSketch(DrawRareFx);
            var host = new Grid { HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 5, 0, 0), Children = { _rarePill, _rareFx } };
            _under.Children.Add(host);
        }
        if (f.Note.Length > 0)
        {
            // the note in its own hairline pill: paused in orange with its sign, a notice in its tone
            _note.Text = (_paused ? "‖  " : "") + f.Note;
            _note.Foreground = f.NoteTone;
            if (_note.Parent is Border old) old.Child = null;
            var np = new Border { BorderBrush = _paused ? Mod.Br(Color.FromRgb(0xF7, 0xA0, 0x72), .8) : EdgeB, BorderThickness = new Thickness(1), Background = Wash, Padding = new Thickness(10, 3, 12, 4), Margin = new Thickness(0, 5, 0, 0), HorizontalAlignment = HorizontalAlignment.Left, Child = _note };
            np.SizeChanged += (_, _) => np.CornerRadius = new CornerRadius(np.ActualHeight / 2);
            _under.Children.Add(np);
        }
    }

    /// <summary>A rare drop's moment: its outline traced round in the grade's colour, then a soft halo that fades.</summary>
    void DrawRareFx(DrawingContext dc, Size s)
    {
        if (_rareTrace is not (> 0 and < 1) || _rarePill is not { ActualWidth: > 0 } rp) return;
        var r = new Rect(.5, .5, rp.ActualWidth - 1, rp.ActualHeight - 1);
        double rad = r.Height / 2, len = 2 * (r.Width + r.Height) - 8 * rad + 2 * Math.PI * rad;
        double t = Out(Seg(_rareTrace, 0, .55));
        var pen = new Pen(Frozen(_rareC), 2) { DashStyle = new DashStyle(new[] { Math.Max(.01, len * t / 2), len }, 0), DashCap = PenLineCap.Round };
        pen.Freeze();
        dc.DrawGeometry(null, pen, Mod.Pill(r, rad));
        double halo = Seg(_rareTrace, .45, .7) * (1 - Seg(_rareTrace, .7, 1));
        if (halo > 0)
            for (int k = 1; k <= 3; k++)
            {
                var hr = new Rect(r.X - k * 1.2, r.Y - k * 1.2, r.Width + k * 2.4, r.Height + k * 2.4);
                dc.DrawGeometry(null, Mod.P(Mod.Br(_rareC, .3 * halo / k), 1.4), Mod.Pill(hr, hr.Height / 2));
            }
    }

    protected override void KinahShown(bool shown)
    {
        _pillHost.Visibility = shown ? Visibility.Visible : Visibility.Collapsed;
        _under.Visibility = shown ? Visibility.Visible : Visibility.Collapsed;
        _fightFill.Margin = new Thickness(0, 0, 0, shown ? 6 : 0);
    }

    // ================================================================ the timers

    protected override void DrawTimers(IReadOnlyList<TimerBoard.Item> due, DateTime now)
    {
        _timer = due.FirstOrDefault() is { } i ? TimerWords(i, now) : null;
        // the line's pickup strip was fitted beside the timer chip: a chip that comes, goes or changes its width fits it again
        if (_stripTimerW >= 0 && Figs is { } f && Math.Abs(TimerChipW() - _stripTimerW) > .5) DrawLine(f);
        else DrawTimerChip();
        if (_lastFight != null && KinahHidden) DrawFight(_lastFight, _lastNow);
    }

    TacSketch? _chipFx;
    Border? _chip;

    void DrawTimerChip()
    {
        _timerSlot.Children.Clear(); _timerUpHost.Children.Clear();
        _chip = null; _chipFx = null;
        if (KinahHidden || _timer is not { } t) { _timerSlot.Visibility = _timerUpHost.Visibility = Visibility.Collapsed; return; }
        var (chip, fx) = TimerChip(t);
        _chip = chip; _chipFx = fx;
        var host = _timerUp ? _timerUpHost : _timerSlot;
        host.Children.Add(chip); host.Children.Add(fx);
        _timerSlot.Visibility = _timerUp ? Visibility.Collapsed : Visibility.Visible;
        _timerUpHost.Visibility = _timerUp ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>The timer chip's room at the pill's end (its slot's margins counted), 0 with none.</summary>
    double TimerChipW() => !KinahHidden && _timer is { } t ? 10 + Mod.W(TimerChip(t).Chip) : 0;

    /// <summary>The next timer as an outline chip: a dot in its tone (mint while the entry is open), its name, its clock bold.</summary>
    (Border Chip, TacSketch Fx) TimerChip((string Text, TimerBadge.Tone Tone) t, double size = 11.5)
    {
        var dotC = t.Tone switch { TimerBadge.Tone.On => MintC0, TimerBadge.Tone.Soon => GoldC0, _ => Mod.A(WhiteC, .7) };
        var sp = new StackPanel { Orientation = Orientation.Horizontal };
        sp.Children.Add(new Border { Width = 7, Height = 7, CornerRadius = new CornerRadius(3.5), Background = Frozen(dotC), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) });
        var tx = Tt("", size, WhiteB, FontWeights.SemiBold);
        int i = t.Text.LastIndexOf(' ');
        if (i > 0 && t.Text[(i + 1)..].Contains(':')) { tx.Inlines.Add(R(t.Text[..i] + " ", size, WhiteB)); tx.Inlines.Add(R(t.Text[(i + 1)..], size, WhiteB, FontWeights.Bold)); }
        else tx.Text = t.Text;
        sp.Children.Add(tx);
        var chip = new Border
        {
            BorderBrush = t.Tone == TimerBadge.Tone.On ? Mod.Br(MintC0, .9) : ChipEdgeB, BorderThickness = new Thickness(1), Background = ChipBgB,
            Padding = new Thickness(8, 3, 10, 3), Child = sp, VerticalAlignment = VerticalAlignment.Center,
        };
        chip.SizeChanged += (_, _) => chip.CornerRadius = new CornerRadius(chip.ActualHeight / 2);
        var fx = new TacSketch((dc, s) =>
        {
            if (_chipPulse is not (> 0 and < 1) || chip.ActualWidth <= 0) return;
            // two mint outlines go out from the chip and fade (5 px at most)
            for (int k = 0; k < 2; k++)
            {
                double p = Math.Clamp(_chipPulse * 1.7 - k * .5, 0, 1);
                if (p <= 0 || p >= 1) continue;
                double g = 5 * Out(p);
                var r = new Rect(-g + .5, -g + .5, chip.ActualWidth + 2 * g - 1, chip.ActualHeight + 2 * g - 1);
                dc.DrawGeometry(null, Mod.P(Mod.Br(MintC0, (1 - p) * .95), 1.6 * (1 - p) + .6), Mod.Pill(r, r.Height / 2));
            }
        });
        return (chip, fx);
    }

    // ================================================================ the fight: a hairline frame

    readonly Grid _fightHost = new();
    readonly LineFill _fightFill = new() { Margin = new Thickness(0, 0, 0, 6), Visibility = Visibility.Collapsed };
    readonly Border _fight = new() { BorderBrush = EdgeB, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(12), Padding = new Thickness(11, 8, 11, 9) };
    readonly StackPanel _fightIn = new();
    TacSketch _frameFx = null!;
    double _enter = 1, _wipeFlash = -1;
    Border? _outcome;

    void BuildFight()
    {
        _fight.Child = _fightIn;
        _frameFx = new TacSketch(DrawFrameFx);
        _fightHost.Children.Add(_fight); _fightHost.Children.Add(_frameFx);
        _fightFill.Child = _fightHost;
    }

    /// <summary>The frame's moments: its hairline drawing itself round as a fight starts, a red flash round it on a wipe.</summary>
    void DrawFrameFx(DrawingContext dc, Size s)
    {
        var r = new Rect(.5, .5, s.Width - 1, s.Height - 1);
        if (r.Width <= 0 || r.Height <= 0) return;
        if (_enter < 1)
        {
            double len = 2 * (r.Width + r.Height) - 8 * 12 + 2 * Math.PI * 12, t = Out(Seg(_enter, 0, .6));
            var pen = new Pen(Mod.Br(WhiteC, .8), 1) { DashStyle = new DashStyle(new[] { Math.Max(.01, len * t), len * 2 }, 0) };
            pen.Freeze();
            dc.DrawGeometry(null, pen, Mod.Pill(r, 12));
        }
        if (_wipeFlash is > 0 and < 1)
        {
            double a = Math.Sin(Math.PI * Math.Min(1, _wipeFlash * 1.4)) * (1 - _wipeFlash * .3);
            dc.DrawGeometry(null, Mod.P(Mod.Br(RedC0, .95 * a), 1.6), Mod.Pill(r, 12));
            var inner = new Rect(r.X + 1.5, r.Y + 1.5, r.Width - 3, r.Height - 3);
            dc.DrawGeometry(null, Mod.P(Mod.Br(RedC0, .35 * a), 2), Mod.Pill(inner, 10.5));
        }
    }

    protected override void DrawFight(Encounter? e, DateTime now)
    {
        _fightIn.Children.Clear();
        _outcome = null;
        if (e == null) { _fightFill.Visibility = Visibility.Collapsed; return; }
        double w = FightWidth - 14;
        _fightFill.Max = w;
        _rowW = w - 2 - _fight.Padding.Left - _fight.Padding.Right;

        // the head: the boss, (in a dungeon the next timer), the outcome's chip, the clock
        var head = new Grid { Effect = ShadeFx };
        for (int i = 0; i < 4; i++) head.ColumnDefinitions.Add(new ColumnDefinition { Width = i == 0 ? new GridLength(1, GridUnitType.Star) : GridLength.Auto });
        var name = Tt(e.Title, 13.5, WhiteB, FontWeights.SemiBold);
        name.TextTrimming = TextTrimming.CharacterEllipsis;
        head.Children.Add(name);
        if (KinahHidden && _timer is { } t)
        {
            var (chip, fx) = TimerChip(t, 10.5);
            chip.Padding = new Thickness(6, 1, 8, 1);
            _chip = chip; _chipFx = fx;
            var slot = new Grid { Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, Children = { chip, fx } };
            Grid.SetColumn(slot, 1); head.Children.Add(slot);
        }
        if (e.Waiting)
        {
            var rd = Tt(L.T("Ready"), 12, SoftB, FontWeights.SemiBold); rd.Margin = new Thickness(8, 0, 0, 0);
            Grid.SetColumn(rd, 3); head.Children.Add(rd);
        }
        else
        {
            var oc = OutcomeText(e);
            if (oc.Length > 0)
            {
                bool wipe = e.Outcome == "wipe";
                _outcome = Chip(oc, wipe ? Mod.Br(RedC0, .9) : ChipEdgeB, wipe ? RedB : WhiteB, 10, padX: 7);
                _outcome.CornerRadius = new CornerRadius(9);
                _outcome.Margin = new Thickness(8, 0, 0, 0);
                Grid.SetColumn(_outcome, 2); head.Children.Add(_outcome);
            }
            var ck = Tt(Clock(e.Duration(now)), 17, WhiteB, FontWeights.Bold); ck.Margin = new Thickness(8, -1, 0, 0);
            Grid.SetColumn(ck, 3); head.Children.Add(ck);
        }
        _fightIn.Children.Add(head);

        if (e.Waiting)
        {
            var rl = Tt(e.ReadyText, 12, SoftB, FontWeights.SemiBold);
            rl.Margin = new Thickness(0, 6, 0, 0); rl.Effect = ShadeFx; rl.TextTrimming = TextTrimming.CharacterEllipsis;
            _fightIn.Children.Add(rl);
            if (PartyBeforeHit(e))
            {
                var rows = new Grid { Margin = new Thickness(0, 8, 0, 0) };
                PutRows(rows, e, now);
                _fightIn.Children.Add(rows);
                FitAll();
            }
        }
        else
        {
            if (HpOf(e) is { } hp) _fightIn.Children.Add(HpLine(e, hp, now));
            var rows = new Grid { Margin = new Thickness(0, HpOf(e) != null ? 6 : 9, 0, 0) };
            PutRows(rows, e, now);
            _fightIn.Children.Add(rows);
            FitAll();
            if (e.HasDamage) _fightIn.Children.Add(Foot(e, now));
        }
        _fightFill.Visibility = Visibility.Visible;
        ApplyEnter();
    }

    /// <summary>The boss's HP: a 3 px white hairline on a faint track, the thirds cut through it, "47% · 12.84M left" under its end.</summary>
    FrameworkElement HpLine(Encounter e, double hp, DateTime now)
    {
        var sp = new StackPanel { Margin = new Thickness(0, 9, 0, 0) };
        var track = new Grid { Height = 3 };
        track.Children.Add(new TacSketch((dc, s) =>
        {
            dc.DrawRoundedRectangle(Mod.Br(WhiteC, .25), null, new Rect(0, 0, s.Width, s.Height), 1.5, 1.5);
            if (hp > 0) dc.DrawRoundedRectangle(WhiteB, null, new Rect(0, 0, Math.Max(3, s.Width * hp), s.Height), 1.5, 1.5);
        }));
        HpGhost(track, e, hp, now, 1.5);
        track.Children.Add(new TacSketch((dc, s) =>
        {
            for (int k = 1; k <= 2; k++) { double x = Math.Round(s.Width * k / 3); dc.DrawRectangle(Mod.Br(Colors.Black, .6), null, new Rect(x - 1, -1, 2, s.Height + 2)); }
        }));
        sp.Children.Add(track);
        var words = Tt("", 11, SoftB, FontWeights.SemiBold);
        words.Inlines.Add(R($"{Math.Round(hp * 100):0}%", 11, hp < .34 ? RedB : WhiteB, FontWeights.Bold));
        var left = HpLeftText(e);
        if (left.Length > 0) words.Inlines.Add(R("  ·  " + left, 10.5, SoftB));
        words.HorizontalAlignment = HorizontalAlignment.Right; words.Margin = new Thickness(0, 3, 0, 0); words.Effect = ShadeFx;
        sp.Children.Add(words);
        return sp;
    }

    /// <summary>The foot under a hairline rule: "Total 622.4M" at the left, the group's figure at the right (the held average with its AVG chip).</summary>
    FrameworkElement Foot(Encounter e, DateTime now)
    {
        var (total, word, group, held) = FootOf(e, now);
        var g = new Grid { Margin = new Thickness(0, 5, 0, 0), Effect = ShadeFx };
        g.Children.Add(new Border { Height = 1, Background = RuleB, VerticalAlignment = VerticalAlignment.Top });
        var line = new Grid { Margin = new Thickness(0, 7, 0, 0) };
        line.ColumnDefinitions.Add(new ColumnDefinition()); line.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var lt = Tt("", 11, SoftB, FontWeights.Medium);
        lt.Inlines.Add(R(L.T("Total") + " ", 11, SoftB, FontWeights.Medium)); lt.Inlines.Add(R(total, 12, WhiteB, FontWeights.Bold));
        line.Children.Add(lt);
        var right = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(10, 0, 0, 0) };
        if (word.Length > 0)
        {
            var rt = Tt("", 11, SoftB, FontWeights.Medium);
            rt.Inlines.Add(R(word + " ", 11, SoftB, FontWeights.Medium)); rt.Inlines.Add(R(group, 12, WhiteB, FontWeights.Bold));
            right.Children.Add(rt);
        }
        if (held)
        {
            var avg = Chip(L.T(Encounter.AvgBadge).ToUpper(CultureInfo.CurrentCulture), ChipEdgeB, WhiteB, 8.5);
            avg.Tag = "avg-badge"; avg.Margin = new Thickness(6, 0, 0, 0); avg.CornerRadius = new CornerRadius(3);
            avg.ToolTip = L.T("The whole fight's average: the fight is over");
            right.Children.Add(avg);
        }
        Grid.SetColumn(right, 1); line.Children.Add(right);
        g.Children.Add(line);
        return AsFoot(g, e, now);
    }

    /// <summary>The fight's parts as far into their entrance as <see cref="_enter"/> says: the frame traced, then the words up.</summary>
    void ApplyEnter()
    {
        _fight.BorderBrush = _enter >= 1 ? EdgeB : Brushes.Transparent;
        double up = Seg(_enter, .25, .7);
        for (int i = 0; i < _fightIn.Children.Count; i++)
        {
            var c = _fightIn.Children[i];
            double p = _enter >= 1 ? 1 : Out(Seg(_enter, .2 + i * .08, .55 + i * .08));
            c.Opacity = p;
            c.RenderTransform = p >= 1 ? null : new TranslateTransform(0, 5 * (1 - p));
        }
        _fight.Background = _enter >= 1 ? Wash : Mod.Br(Colors.Black, .2 * _k * up);
        _frameFx.InvalidateVisual();
    }

    // ================================================================ the moments

    /// <summary>A pickup: the pill's hairline brightens and the pickup's words rise into their place.</summary>
    protected override void Pickup(Pick p, bool held)
    {
        if (KinahHidden || _pillHost.Visibility != Visibility.Visible) return;
        Motion.Play(_pillFx, 900, t =>
        {
            _pillFlash = t; _pillFx.InvalidateVisual();
            double k = Out(Seg(t, 0, .45));
            _pick.Opacity = k; _pickShift.Y = 6 * (1 - k);
        }, () => { _pillFlash = -1; _pillFx.InvalidateVisual(); _pick.Opacity = 1; _pickShift.Y = 0; });
    }

    /// <summary>A rare drop: its pill's outline traces round in the grade's colour, a soft halo after.</summary>
    protected override void RareDrop(Pick p, bool held)
    {
        if (_rareFx is not { } fx) return;
        Motion.Play(fx, 1500, t => { _rareTrace = t; fx.InvalidateVisual(); }, () => { _rareTrace = -1; fx.InvalidateVisual(); });
    }

    /// <summary>Your best passed: the ring goes mint and two rings ripple out from it.</summary>
    protected override void BestPassed(bool held)
    {
        if (KinahHidden || _ringHost.Visibility != Visibility.Visible) return;
        Motion.Play(_ring, 1300, t => { _ringPulse = t; _ring.InvalidateVisual(); }, () => { _ringPulse = -1; _ring.InvalidateVisual(); });
    }

    /// <summary>An entry opened: the timer chip's outline pulses out twice in mint.</summary>
    protected override void EntryOpened(TimerBoard.Item i, bool held)
    {
        if (_chipFx is not { } fx) return;
        Motion.Play(fx, 1400, t => { _chipPulse = t; fx.InvalidateVisual(); }, () => { _chipPulse = -1; fx.InvalidateVisual(); });
    }

    /// <summary>A fight started: the frame's hairline draws itself round and the head, the HP and the rows come up one after another.</summary>
    protected override void FightStarted(Encounter e, bool held)
    {
        Motion.Play(_fightHost, 900, t => { _enter = t; ApplyEnter(); }, () => { _enter = 1; ApplyEnter(); });
    }

    /// <summary>A fight ended: a kill stamps its chip and runs a light along the top row's bar; a wipe flashes the frame red.</summary>
    protected override void FightEnded(Encounter e, bool held)
    {
        if (e.Outcome is not ("kill" or "wipe" or "cleared")) return;
        bool wipe = e.Outcome == "wipe";
        var chip = _outcome;
        var st = new ScaleTransform(1, 1);
        if (chip != null) { chip.RenderTransformOrigin = new Point(.5, .5); chip.RenderTransform = st; }
        var top = !wipe && Meter is { Members.Count: > 0 } m ? m.RowOf(m.Members[0].Id) as LineRow : null;
        Motion.Play(_frameFx, 1600, t =>
        {
            if (chip != null)
            {
                double s = 1.35 - .35 * Back(Seg(t, 0, .25), 1.4);
                st.ScaleX = st.ScaleY = s;
                chip.Opacity = Seg(t, 0, .12);
            }
            if (wipe) { _wipeFlash = t; _frameFx.InvalidateVisual(); }
            if (top != null) top.SetGlint(Seg(t, .15, .75));
        }, () =>
        {
            if (chip != null) { st.ScaleX = st.ScaleY = 1; chip.Opacity = 1; }
            _wipeFlash = -1; _frameFx.InvalidateVisual();
            top?.SetGlint(-1);
        });
    }
}

/// <summary>
/// The fight's frame holder: as wide as the meter (<see cref="Max"/>) and never wider, but it takes a slot a pixel or two
/// narrower without cutting anything (the share picture lays the look out 2 px under what it measured): its content
/// stretches to the room it is given, so the hairline frame is always drawn whole at the room's edge.
/// </summary>
sealed class LineFill : Decorator
{
    public double Max { get => _max; set { if (_max == value) return; _max = value; InvalidateMeasure(); } }
    double _max = 300;

    protected override Size MeasureOverride(Size available)
    {
        if (Child is not { } c) return new Size();
        double w = Math.Min(_max, available.Width);
        c.Measure(new Size(w, available.Height));
        return new Size(w, c.DesiredSize.Height);   // never more than the room: a slot a hair narrower is taken whole, nothing clipped
    }

    protected override Size ArrangeOverride(Size final)
    {
        Child?.Arrange(new Rect(0, 0, Math.Min(_max, final.Width), final.Height));
        return final;
    }
}
