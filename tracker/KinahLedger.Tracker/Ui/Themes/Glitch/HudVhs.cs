using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Shapes;
using KinahLedger.Parser;
using KinahLedger.Session;
using static KinahLedger.Ui.HudKit;
using static KinahLedger.Ui.GlitchKit;

namespace KinahLedger.Ui;

/// <summary>
/// VHS: a camcorder's viewfinder. A translucent black ground with fine scanlines, a soft vignette and the tape's
/// head-switching noise at its foot, white corner marks, the OSD's type (Lucida Console). The top row is the tape: a red
/// "● REC" (the dot blinks with the run clock's seconds) and the counter "SP 00:42:17". Then the hour, big and white with
/// a red/cyan split fixed behind it, and your best as a battery-style level; the session and the next timer ("▶ SHUGO
/// ENTER 2:41" in green). A fight is a second viewfinder: "CH 03 KASHAPA" and its clock in yellow, the HP as a segmented
/// level with notches at the thirds, the rows on the dark ground, the foot.
/// Moments: a pickup rolls a tracking band down the panel and "▶▶ +7.5K" lights in the top row; a rare drop flashes
/// white and holds a freeze-frame ("‖ PAUSE") with its tile; your best passed runs a "★ REC BEST" title card; an entry
/// opening flashes "▶ PLAY" on its timer; a fight starts with a channel switch (a static burst, the channel's OSD); a kill
/// stamps "■ STOP — KILLED", a wipe loses the tracking on a blue band.
/// </summary>
sealed class HudVhs : HudThemeLook, IThemeCard
{
    static readonly FontFamily Face = VhsFace;
    const uint RedC = 0xFF3B30, CyanC = 0x28E0FF, YellowC = 0xFFD23F, GreenC = 0x45FF86, BlueC = 0x1432C8, MintTextC = 0x5CFFB0;
    static readonly Brush White = Br(0xFFFFFF), Dim = Br(0xFFFFFF, .66), Faded = Br(0xFFFFFF, .42), Red = Br(RedC), Yellow = Br(YellowC), Green = Br(GreenC), Black = Br(0x000000);
    static readonly Color SplitL = Rgb(RedC, .85), SplitR = Rgb(CyanC, .85);

    readonly StackPanel _root = new() { HorizontalAlignment = HorizontalAlignment.Left };
    readonly GlitchPanel _deck = new(new Thickness(13, 8, 13, 9)), _cam = new(new Thickness(13, 8, 13, 9));
    readonly SolidColorBrush _deckGround = new(Rgb(0x000000, .75)), _camGround = new(Rgb(0x000000, .9));

    // the deck (the Kinah line): the tape row, the hour and your best, the session and the timer, the Abyss's stats, a rare drop, the note
    readonly System.Windows.Shapes.Ellipse _dot = new() { Width = 8, Height = 8, Fill = Red, Margin = new Thickness(0, 0, 5, 1), VerticalAlignment = VerticalAlignment.Center };
    readonly TextBlock _recWord = Txt("REC", 11, Red, Face);
    readonly StackPanel _rec = new() { Orientation = Orientation.Horizontal };
    readonly TextBlock _tc = Txt("", 11, White, Face), _ff = Txt("", 11, White, Face);
    readonly Grid _figRow = new() { Margin = new Thickness(0, 5, 0, 0) };
    readonly Grid _sessRow = new() { Margin = new Thickness(0, 5, 0, 0) };
    readonly TextBlock _sess = Txt("", 13, White, Face);
    readonly Grid _timerCell = new() { HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(14, 0, 0, 0) };
    readonly TextBlock _timer = Txt("", 11.5, White, Face);
    readonly Border _play = PlayTag();
    readonly TextBlock _stats = Txt("", 11, Dim, Face);
    readonly Grid _rare = new() { Margin = new Thickness(0, 6, 0, 0), Visibility = Visibility.Collapsed };
    readonly Border _pause = new() { Background = Yellow, Padding = new Thickness(4, 1, 4, 0), VerticalAlignment = VerticalAlignment.Center, Child = Txt("‖ " + L.T("FREEZE"), 10, Black, VhsFace) };
    FrameworkElement? _rareTile;
    readonly TextBlock _note = Txt("", 11, Dim, Face, FontWeights.Bold, trim: true);

    // the camera (the fight)
    readonly Border _ch = new() { Background = White, Padding = new Thickness(4, 1, 4, 0), Margin = new Thickness(0, 0, 7, 0), VerticalAlignment = VerticalAlignment.Center };
    readonly TextBlock _chText = Txt("CH 03", 10, Black, VhsFace);
    readonly TextBlock _title = Txt("", 13, White, Face, trim: true), _clock = Txt("", 15, Yellow, Face);
    readonly Grid _camTimerCell = new() { HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 5, 0, 0), Visibility = Visibility.Collapsed };
    readonly TextBlock _camTimer = Txt("", 11.5, White, Face);
    readonly Border _camPlay = PlayTag();
    readonly Grid _hpRow = new() { Margin = new Thickness(0, 7, 0, 3), Visibility = Visibility.Collapsed };
    readonly Grid _hpTrack = new() { Height = 7, VerticalAlignment = VerticalAlignment.Center };
    readonly Border _hpFill = new() { HorizontalAlignment = HorizontalAlignment.Left };
    readonly TextBlock _hpText = Txt("", 11, White, Face);
    readonly TextBlock _ready = Txt("", 12, Dim, Face, trim: true);
    readonly StackPanel _rowsHost = new() { Margin = new Thickness(0, 7, 0, 0) };
    readonly ContentControl _foot = new() { Focusable = false };
    readonly TextBlock _outcome = Txt("", 12, White, Face);
    double _hpShare = -1;

    public HudVhs() : base(false, CodeFace)
    {
        // the meter's rows: the OSD's own list (Ui/Themes/Glitch/VhsRows.cs)
        UseRows(VhsRows.Pitch, _ => new VhsRows.Row(), VhsRows.GapHeight, VhsRows.Gap);
        Build(_deck, _deckGround);
        Build(_cam, _camGround);
        _cam.Root.Width = FightWidth;
        _cam.Root.Visibility = Visibility.Collapsed;
        _cam.Root.Margin = new Thickness(0, 4, 0, 0);
        TextElement.SetFontFamily(_cam.Body, Face);
        TextElement.SetFontFamily(_foot, CodeFace);

        // the tape row: REC, a pickup's fast-forward mark, the counter
        _rec.Children.Add(_dot); _rec.Children.Add(_recWord);
        var tape = new Grid();
        tape.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); tape.ColumnDefinitions.Add(new ColumnDefinition()); tape.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        tape.Children.Add(_rec);
        _ff.HorizontalAlignment = HorizontalAlignment.Right; _ff.Margin = new Thickness(14, 0, 12, 0); _ff.Visibility = Visibility.Hidden;
        Grid.SetColumn(_ff, 1); tape.Children.Add(_ff);
        Grid.SetColumn(_tc, 2); tape.Children.Add(_tc);
        _deck.Body.Children.Add(tape);
        _deck.Body.Children.Add(_figRow);
        _sessRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); _sessRow.ColumnDefinitions.Add(new ColumnDefinition());
        _sessRow.Children.Add(_sess);
        _timerCell.Children.Add(_timer); _timerCell.Children.Add(_play);
        Grid.SetColumn(_timerCell, 1); _sessRow.Children.Add(_timerCell);
        _deck.Body.Children.Add(_sessRow);
        _stats.Margin = new Thickness(0, 4, 0, 0); _stats.Visibility = Visibility.Collapsed;
        _deck.Body.Children.Add(_stats);
        _rare.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); _rare.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); _rare.ColumnDefinitions.Add(new ColumnDefinition());
        _rare.Children.Add(_pause);
        _deck.Body.Children.Add(_rare);
        _note.Margin = new Thickness(0, 5, 0, 0); _note.Visibility = Visibility.Collapsed; _note.MaxWidth = 380; _note.HorizontalAlignment = HorizontalAlignment.Left;
        _deck.Body.Children.Add(_note);

        // the camera: CH, the title, the clock; the timer in a dungeon; the HP; the rows; the foot
        _ch.Child = _chText;
        var head = new Grid();
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); head.ColumnDefinitions.Add(new ColumnDefinition()); head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        head.Children.Add(_ch);
        Grid.SetColumn(_title, 1); head.Children.Add(_title);
        _clock.Margin = new Thickness(10, 0, 0, 0);
        Grid.SetColumn(_clock, 2); head.Children.Add(_clock);
        _cam.Body.Children.Add(head);
        _camTimerCell.Children.Add(_camTimer); _camTimerCell.Children.Add(_camPlay);
        _cam.Body.Children.Add(_camTimerCell);
        _outcome.Margin = new Thickness(0, 5, 0, 0); _outcome.Visibility = Visibility.Collapsed;
        _cam.Body.Children.Add(_outcome);
        // the HP: a level of red segments on a faint track, notches at the thirds standing out of it, the words at its end
        _hpRow.ColumnDefinitions.Add(new ColumnDefinition()); _hpRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        _hpTrack.Children.Add(new Border { Background = Br(0xFFFFFF, .13) });
        _hpFill.Background = Red; _hpTrack.Children.Add(_hpFill);
        _hpTrack.SizeChanged += (_, _) => Share(_hpTrack, _hpFill, _hpShare);
        var hpCell = new Grid { Height = 13, VerticalAlignment = VerticalAlignment.Center };
        hpCell.Children.Add(_hpTrack);
        _hpTrack.Children.Add(new Rectangle { Fill = Segments(Rgb(0x000000, .7), 4, 1.2), IsHitTestVisible = false });
        hpCell.Children.Add(Notches(White, 1.6));
        _hpRow.Children.Add(hpCell);
        _hpText.Margin = new Thickness(10, 0, 0, 0); Grid.SetColumn(_hpText, 1); _hpRow.Children.Add(_hpText);
        _cam.Body.Children.Add(_hpRow);
        _ready.Margin = new Thickness(0, 7, 0, 1); _ready.Visibility = Visibility.Collapsed;
        _cam.Body.Children.Add(_ready);
        _cam.Body.Children.Add(_rowsHost);
        _cam.Body.Children.Add(_foot);

        _root.Children.Add(_deck.Root);
        _root.Children.Add(_cam.Root);
    }

    /// <summary>A viewfinder's layers: the ground, the vignette, the head-switching noise at the foot; over the body the scanlines and the corner marks.</summary>
    static void Build(GlitchPanel p, SolidColorBrush ground)
    {
        p.Root.ClipToBounds = true;
        p.Ground.Children.Add(new Border { Background = ground, CornerRadius = new CornerRadius(2) });
        p.Ground.Children.Add(new Rectangle { Fill = Vignette(Colors.Black, .45) });
        var head = NoiseBrush(.32);
        p.Ground.Children.Add(new Rectangle { Fill = head, Height = 2, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 0, 1) });
        p.Over.Children.Add(new Rectangle { Fill = Lines(Rgb(0xFFFFFF, .045), 3, 1) });
        p.Over.Children.Add(Corners(Br(0xFFFFFF, .72), 8, 1.4, 4));
    }

    static Border PlayTag() => new()
    {
        Background = Br(GreenC), Padding = new Thickness(5, 0, 5, 0), Visibility = Visibility.Hidden, HorizontalAlignment = HorizontalAlignment.Stretch,
        Child = new TextBlock { Text = "▶ PLAY", FontFamily = VhsFace, FontSize = 11.5, FontWeight = FontWeights.Bold, Foreground = Br(0x000000), VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center },
    };

    /// <summary>The notches at a third and two thirds, standing a little out of the bar.</summary>
    static Grid Notches(Brush b, double w)
    {
        var g = new Grid { IsHitTestVisible = false };
        for (int i = 0; i < 3; i++) g.ColumnDefinitions.Add(new ColumnDefinition());
        for (int i = 0; i < 2; i++)
        {
            var t = new Border { Width = w, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 0, -w / 2, 0), Background = b };
            Grid.SetColumn(t, i); g.Children.Add(t);
        }
        return g;
    }

    public override FrameworkElement Root => _root;

    /// <summary>The member's skills card in this look (Ui/Themes/Glitch/GlitchCards.cs).</summary>
    public FrameworkElement DrawCard(MemberCard c) => GlitchCards.Vhs(c, Moves);
    public override double Corner => 2;
    public override FrameworkElement? FightFrame => _cam.Root.Visibility == Visibility.Visible ? _cam.Root : null;

    public override void SetOpacity(double opacity)
    {
        double o = Math.Clamp(opacity, 0.3, 1);
        _deckGround.Color = Rgb(0x000000, o * .83);
        _camGround.Color = Rgb(0x000000, Math.Min(1, o * 1.0));
    }

    protected override string LineSig(Figures f) => $"{base.LineSig(f)}|{Values.GetValueOrDefault("time")}|{Values.GetValueOrDefault("rare")}{Values.GetValueOrDefault("rareId")}";

    string _figSig = "";

    protected override void DrawLine(Figures f)
    {
        // the tape row moves with the clock (the counter, the dot's blink): the rest only when the figures change
        var (run, clock, odd) = RunClock(Values);
        // the tape records while the session counts (a run or not); nothing counted yet, it stands by
        run = run || Values.GetValueOrDefault("run") == "1" || f.Counted;
        _recWord.Text = run ? "REC" : "STBY";
        _recWord.Foreground = run ? Red : Dim;
        _dot.Visibility = run ? Visibility.Visible : Visibility.Collapsed;
        _dot.Opacity = odd ? 1 : .3;
        _tc.Text = "SP " + Timecode(clock);
        var sig = $"{f.Sig}|{f.ToBest}|{KinahHidden}|{Values.GetValueOrDefault("rare")}{Values.GetValueOrDefault("rareId")}";
        if (sig == _figSig) return;
        _figSig = sig;

        // the hour and your best
        _figRow.Children.Clear(); _figRow.ColumnDefinitions.Clear();
        _figRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); _figRow.ColumnDefinitions.Add(new ColumnDefinition()); _figRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var left = new StackPanel { Orientation = Orientation.Horizontal };
        if (f.Abyss && f.Counted && f.Icon.Length > 0) { var m = AbyssMark(f.Icon, 30); m.Margin = new Thickness(0, 0, 9, 0); left.Children.Add(m); }
        if (!f.Counted || f.Rate.Length == 0)
            left.Children.Add(Txt(Waiting(f.Abyss).ToUpperInvariant(), 13, Dim, Face));
        else if (f.Warm)
            left.Children.Add(Txt(f.Rate.ToUpperInvariant(), 18, Faded, Face));
        else
        {
            left.Children.Add(Split(f.Rate, 27, Face, FontWeights.Bold, White, SplitL, SplitR, 1.6));
            var u = Txt(f.Unit, 14, Dim, Face); u.VerticalAlignment = VerticalAlignment.Bottom; u.Margin = new Thickness(3, 0, 0, 4);
            left.Children.Add(u);
        }
        _figRow.Children.Add(left);
        if (f.Share >= 0 && !f.Warm && f.Counted && f.Rate.Length > 0)
        {
            var best = BestLevel(f.Share, f.Beat);
            best.Margin = new Thickness(16, 0, 0, 0);
            Grid.SetColumn(best, 2); _figRow.Children.Add(best);
        }

        // the session (in the Abyss, the points)
        _sess.Inlines.Clear();
        _sessRow.Visibility = f.Counted && f.Total.Length > 0 || _timer.Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        if (f.Counted && f.Total.Length > 0)
        {
            _sess.Inlines.Add(Rn(f.Abyss ? f.TotalWord + " " : f.TotalWord.ToUpper(System.Globalization.CultureInfo.CurrentCulture) + " ", Dim, 10.5));
            _sess.Inlines.Add(Rn(f.Total, White, 14));
        }

        // the Abyss's kills, deaths and Kinah, each figure in its colour
        _stats.Inlines.Clear();
        foreach (var (fig, word, tone) in f.Stats)
        {
            if (_stats.Inlines.Count > 0) _stats.Inlines.Add(Rn("  ·  ", Faded));
            _stats.Inlines.Add(Rn(fig, ReferenceEquals(tone, Ink) ? White : tone, 12));
            _stats.Inlines.Add(Rn(" " + word, Dim));
        }
        _stats.Visibility = _stats.Inlines.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

        // a rare drop the window holds: a freeze-frame row, its tile and its name in its grade's colour
        if (_rareTile != null) { _rare.Children.Remove(_rareTile); _rareTile = null; }
        for (int i = _rare.Children.Count - 1; i >= 1; i--) _rare.Children.RemoveAt(i);
        if (Rare(Values) is { } r)
        {
            var c = ColorOf(r.Color, r.Grade);
            _rareTile = Tile(r, 24); _rareTile.Margin = new Thickness(9, 0, 8, 0);
            Grid.SetColumn(_rareTile, 1); _rare.Children.Add(_rareTile);
            var name = Txt("", 12.5, Frozen(c), Face, trim: true);
            name.Inlines.Add(Rn(Caps(r.Name), Frozen(c)));
            Grid.SetColumn(name, 2); _rare.Children.Add(name);
            _rare.Visibility = Visibility.Visible;
        }
        else _rare.Visibility = Visibility.Collapsed;

        _note.Text = f.Note.Length > 0 ? "! " + f.Note : "";
        _note.Foreground = f.NoteTone;
        _note.Visibility = f.Note.Length > 0 && !KinahHidden ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>Your best as a camcorder's battery level: "BEST 91%" over ten cells; beaten, "★ NEW BEST" with every cell lit yellow.</summary>
    static StackPanel BestLevel(double share, bool beat)
    {
        var sp = new StackPanel { VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Right };
        var t = Txt("", 11, White, Face); t.HorizontalAlignment = HorizontalAlignment.Right;
        if (beat) t.Inlines.Add(Rn(L.T("★ NEW BEST"), Yellow));
        else { t.Inlines.Add(Rn(L.T("BEST") + " ", Dim, 10)); t.Inlines.Add(Rn($"{Math.Round(Math.Clamp(share, 0, 1) * 100):0}%", White)); }
        sp.Children.Add(t);
        var cells = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 4, 0, 0) };
        int lit = beat ? 10 : (int)Math.Round(Math.Clamp(share, 0, 1) * 10);
        // the battery's body: ten cells, then its nub
        for (int i = 0; i < 10; i++)
            cells.Children.Add(new Border { Width = 5, Height = 8, Margin = new Thickness(0, 0, 1.5, 0), Background = i < lit ? (beat ? Yellow : White) : Br(0xFFFFFF, .16) });
        cells.Children.Add(new Border { Width = 2, Height = 4, Background = Br(0xFFFFFF, .45), VerticalAlignment = VerticalAlignment.Center });
        sp.Children.Add(cells);
        return sp;
    }

    protected override void DrawTimers(IReadOnlyList<TimerBoard.Item> due, DateTime now)
    {
        var next = due.FirstOrDefault();
        SetTimer(_timer, KinahHidden ? null : next, now);
        SetTimer(_camTimer, KinahHidden ? next : null, now);
        _camTimerCell.Visibility = KinahHidden && next != null ? Visibility.Visible : Visibility.Collapsed;
        if (Figs is { } f) _sessRow.Visibility = f.Counted && f.Total.Length > 0 || _timer.Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    static void SetTimer(TextBlock t, TimerBoard.Item? i, DateTime now)
    {
        t.Inlines.Clear();
        if (i == null) { t.Text = ""; return; }
        var (text, tone) = TimerWords(i, now);
        var (name, rest) = TimerParts(i, text);
        var b = tone == TimerBadge.Tone.On ? Green : tone == TimerBadge.Tone.Soon ? Yellow : White;
        t.Inlines.Add(Rn(tone == TimerBadge.Tone.On ? "▶ " : "◷ ", b));
        if (name.Length > 0) t.Inlines.Add(Rn(name + " ", b));
        t.Inlines.Add(Rn(rest, b));
    }

    protected override void KinahShown(bool shown)
    {
        _deck.Root.Visibility = shown ? Visibility.Visible : Visibility.Collapsed;
        _cam.Root.Margin = new Thickness(0, shown ? 4 : 0, 0, 0);
    }

    protected override void DrawFight(Encounter? e, DateTime now)
    {
        if (e == null) { _cam.Root.Visibility = Visibility.Collapsed; _rowsHost.Children.Clear(); _foot.Content = null; _title.Text = ""; _hpShare = -1; return; }
        _cam.Root.Width = FightWidth;
        _chText.Text = $"CH {1 + (int)(Hash(e.Title) * 12):00}";
        _title.Text = e.Title;
        _rowsHost.Children.Clear(); _foot.Content = null; _rowsHost.Visibility = Visibility.Collapsed; _foot.Visibility = Visibility.Collapsed;
        if (e.Waiting)
        {
            _outcome.Visibility = Visibility.Collapsed;
            _clock.Text = "‖ " + L.T("Ready").ToUpper(System.Globalization.CultureInfo.CurrentCulture); _clock.Foreground = Dim; _clock.FontSize = 12;
            _hpRow.Visibility = Visibility.Collapsed;
            _ready.Text = e.ReadyText; _ready.Visibility = Visibility.Visible;
            _cam.Root.Visibility = Visibility.Visible;
            return;
        }
        _ready.Visibility = Visibility.Collapsed;
        _clock.Text = Clock(e.Duration(now)); _clock.Foreground = e.Live ? Yellow : White; _clock.FontSize = 15;
        if (HpOf(e) is double hp)
        {
            _hpShare = hp; Share(_hpTrack, _hpFill, hp);
            _hpFill.Background = hp < .34 ? Br(0xFF2020) : Red;
            HpGhost(_hpTrack, e, hp, now, 0);
            _hpText.Inlines.Clear();
            _hpText.Inlines.Add(Rn($"{Math.Round(hp * 100):0}%", hp < .34 ? Br(0xFF6A5C) : White, 12));
            _hpText.Inlines.Add(Rn("  " + HpLeftText(e).ToUpper(System.Globalization.CultureInfo.CurrentCulture), Dim));
            _hpRow.Visibility = Visibility.Visible;
        }
        else _hpRow.Visibility = Visibility.Collapsed;
        PutRows(_rowsHost, e, now, FightWidth - 26);
        _rowsHost.Visibility = Visibility.Visible;
        // the outcome stamped in the head, before the clock: "■ KILLED 2:19" (mint), "■ WIPED" (red)
        var outcome = OutcomeText(e);
        _outcome.Inlines.Clear();
        if (outcome.Length > 0)
        {
            _outcome.Inlines.Add(Rn("■ " + L.T("STOP") + "  ·  ", White));
            _outcome.Inlines.Add(Rn(outcome.ToUpper(System.Globalization.CultureInfo.CurrentCulture), e.Outcome == "wipe" ? Br(0xFF5A4F) : e.Outcome is "kill" or "cleared" ? Br(MintTextC) : Dim));
        }
        _outcome.Foreground = e.Outcome == "wipe" ? Br(0xFF5A4F) : e.Outcome is "kill" or "cleared" ? Br(MintTextC) : Dim;
        _outcome.Visibility = outcome.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        if (e.HasDamage) _foot.Content = VhsRows.Foot(e, now);
        _foot.Visibility = _foot.Content != null ? Visibility.Visible : Visibility.Collapsed;
        _cam.Root.Visibility = Visibility.Visible;
    }

    // ---- the moments

    /// <summary>A tracking band rolling down a panel once: a soft band of tape noise with a bright core line.</summary>
    static void Band(GlitchPanel p, double ms, double height = 22, double alpha = .6)
    {
        double h = p.H;
        var band = new Grid { Height = height, VerticalAlignment = VerticalAlignment.Top, OpacityMask = SoftEdges };
        var nb = NoiseBrush(alpha * .7);
        band.Children.Add(new Rectangle { Fill = Br(0x000000, .45) });
        band.Children.Add(new Rectangle { Fill = nb });
        band.Children.Add(new Rectangle { Fill = Br(0xFFFFFF, .5), Height = 1.5, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 0, 3) });
        var tt = new TranslateTransform(); band.RenderTransform = tt;
        p.Run(band, ms, t => { tt.Y = -height + (h + height) * t; Shift(nb, t, 30); }, under: true);
    }

    protected override void Pickup(Pick p, bool held)
    {
        if (KinahHidden) return;
        Band(_deck, 760);
        _ff.Inlines.Clear();
        _ff.Inlines.Add(Rn("▶▶ ", White));
        _ff.Inlines.Add(Rn(p.Worth.Length > 0 ? p.Worth : Caps(p.Name), Green));
        _ff.Visibility = Visibility.Visible;
        Motion.Play(_ff, 1900, t => _ff.Opacity = !Blink(t, 2, .12) ? .15 : t > .82 ? 1 - (t - .82) / .18 : 1,
            () => { _ff.Visibility = Visibility.Hidden; _ff.Opacity = 1; });
    }

    protected override void RareDrop(Pick p, bool held)
    {
        if (KinahHidden) return;
        _deck.Flash(Colors.White, .6, 2300, .03, .36);
        // the freeze-frame: two noise bars held across the still picture, seething in place
        foreach (var (y, hh) in new[] { (.28, 3.0), (.74, 2.0) })
        {
            var nb = NoiseBrush(.5);
            var bar = new Rectangle { Fill = nb, Height = hh, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, Math.Round(_deck.H * y), 0, 0) };
            _deck.Run(bar, 2300, t => { bar.Opacity = Gate(t, .05, .9, .02, .12); Shift(nb, t, 40); }, under: true);
        }
        // PAUSE blinks, the tile lands
        Motion.Play(_pause, 2300, t => _pause.Opacity = Blink(t, 4, .5) ? 1 : .2, () => _pause.Opacity = 1);
        if (_rareTile is { } tile)
        {
            var s = new ScaleTransform(); tile.RenderTransformOrigin = new Point(.5, .5); tile.RenderTransform = s;
            Motion.Play(tile, 520, t => s.ScaleX = s.ScaleY = Math.Max(0, Back(Math.Clamp((t - .08) / .92, 0, 1))), () => tile.ClearValue(UIElement.RenderTransformProperty));
        }
    }

    protected override void BestPassed(bool held)
    {
        if (KinahHidden) return;
        _deck.Flash(Colors.White, .45, 1900, .05, .3);
        Band(_deck, 900, 22, .6);
        // the title card over the tape row: a yellow card wiped open, held, wiped away
        var card = new Grid { VerticalAlignment = VerticalAlignment.Top, Height = 19, Margin = new Thickness(6, 5, 6, 0) };
        card.Children.Add(new Border { Background = Yellow });
        var cw = Split(L.T("★ REC BEST"), 12, Face, FontWeights.Bold, Black, Rgb(RedC, .7), Rgb(CyanC, .9), 1.2);
        cw.HorizontalAlignment = HorizontalAlignment.Center;
        card.Children.Add(cw);
        var scale = new ScaleTransform(0, 1); card.RenderTransformOrigin = new Point(.5, .5); card.RenderTransform = scale;
        _deck.Run(card, 1900, t => scale.ScaleX = t < .14 ? Steps(t / .14, 5) + .2 : t < .86 ? 1 : 1 - Steps((t - .86) / .14, 4));
    }

    protected override void EntryOpened(TimerBoard.Item i, bool held)
    {
        var tag = KinahHidden ? _camPlay : _play;
        tag.Visibility = Visibility.Visible;
        // PLAY flashes three times over the timer, then the timer stands lit in green
        Motion.Play(tag, 1500, t => tag.Visibility = t < .8 && (t / .8 * 3) % 1 < .62 ? Visibility.Visible : Visibility.Hidden, () => tag.Visibility = Visibility.Hidden);
    }

    protected override void FightStarted(Encounter e, bool held)
    {
        // a channel switch: the picture drops to static, a roll line crosses, the channel's OSD stands in the corner
        var veil = new Rectangle { Fill = NoiseBrush(.6) };
        _cam.Run(veil, 1100, t => { veil.Opacity = t < .04 ? .8 : t < .25 ? .8 * (1 - (t - .04) / .21) : 0; Shift((ImageBrush)veil.Fill, t, 36); }, under: true);
        double h = _cam.H;
        // two heavy static bands roll down the picture, one after the other
        for (int k = 0; k < 2; k++)
        {
            var nb = NoiseBrush(.6);
            var bar = new Grid { Height = 26, VerticalAlignment = VerticalAlignment.Top, OpacityMask = SoftEdges };
            bar.Children.Add(new Rectangle { Fill = Br(0x000000, .45) });
            bar.Children.Add(new Rectangle { Fill = nb });
            bar.Children.Add(new Rectangle { Fill = Br(0xFFFFFF, .55), Height = 2, VerticalAlignment = VerticalAlignment.Center });
            var bt = new TranslateTransform(); bar.RenderTransform = bt;
            double a = k * .22;
            _cam.Run(bar, 1100, t => { double q = Math.Clamp((t - a) / .4, 0, 1); bt.Y = -26 + (h + 26) * q; bar.Opacity = t >= a && q < 1 ? 1 : 0; Shift(nb, t, 30); }, under: true);
        }
        var osd = Split(_chText.Text, 22, Face, FontWeights.Bold, Green, Rgb(RedC, .6), Rgb(CyanC, .6), 1.4);
        osd.HorizontalAlignment = HorizontalAlignment.Right; osd.VerticalAlignment = VerticalAlignment.Top; osd.Margin = new Thickness(0, 26, 16, 0);
        var halo = new Border { Background = Br(0x000000, .96), Padding = new Thickness(8, 0, 8, 0), Child = osd, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 3, 8, 0) };
        osd.Margin = new Thickness(0);
        _cam.Run(halo, 1100, t => halo.Opacity = t < .8 ? (Blink(t, 4, .18) ? 1 : 0) : 1 - (t - .8) / .2);
    }

    protected override void FightEnded(Encounter e, bool held)
    {
        bool wipe = e.Outcome == "wipe";
        var outcome = OutcomeText(e).ToUpper(System.Globalization.CultureInfo.CurrentCulture);
        var band = new Border { VerticalAlignment = VerticalAlignment.Top, Height = 44, Margin = new Thickness(0, 1, 0, 0) };
        var words = Txt("", 14, White, Face);
        words.HorizontalAlignment = HorizontalAlignment.Center;
        if (wipe)
        {
            // the tracking lost: a blue band, the picture torn by noise bars that jump about
            band.Background = Br(BlueC);
            words.Text = L.T("TRACKING LOST");
            for (int k = 0; k < 3; k++)
            {
                var nb = NoiseBrush(.5);
                var bar = new Rectangle { Fill = nb, Height = 4 + 3 * k, VerticalAlignment = VerticalAlignment.Top };
                var tt = new TranslateTransform(); bar.RenderTransform = tt;
                double h = _cam.H; int kk = k;
                _cam.Run(bar, 2200, t => { tt.Y = h * ((Hash(kk + ":" + Steps(t, 9)) * .8) + .1); bar.Opacity = Gate(t, .04, .8, 0, .15); Shift(nb, t, 30); }, under: true);
            }
        }
        else
        {
            // STOP: a white flash, the stop card over the head
            _cam.Flash(Colors.White, .6, 2200, .04, .22);
            // the top DPS's line glints as the picture stops
            if (Meter?.Members.FirstOrDefault(m => m.TopDps) is { } top && Meter.RowOf(top.Id) is VhsRows.Row row) row.Glint(2200);
            band.Background = Br(0x000000); band.BorderBrush = Br(0xFFFFFF, .85); band.BorderThickness = new Thickness(0, 1.5, 0, 1.5);
            words.Inlines.Add(Rn("■ " + L.T("STOP") + "  —  ", White));
            words.Inlines.Add(Rn(outcome, Br(MintTextC)));
        }
        band.Child = words;
        var s = new ScaleTransform(1, 0); band.RenderTransformOrigin = new Point(.5, .5); band.RenderTransform = s;
        _cam.Run(band, 2200, t =>
        {
            s.ScaleY = t < .06 ? Steps(t / .06, 3) + .34 : t < .5 ? 1 : Math.Max(.04, 1 - Motion.Out((t - .5) / .14));
            band.Opacity = t < .64 ? 1 : 0;
        });
    }
}
