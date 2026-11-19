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
/// Terminal: an old green CRT. A dark green-black screen with scanlines, a curved vignette and a faint green rim; Cascadia
/// Mono in phosphor green, the figures in pale green with a soft glow. A prompt "> kinah --rate", the hour "1.27M/h
/// [91% of best]", "session 894K", the timer "shugo: ENTER 2:41" in amber, and the last pickup as a log line with a block
/// cursor (the cursor blinks with the run clock's seconds). The fight: "> fight kashapa" and its clock, the HP as a block
/// bar [██████¦███░░░¦░░░░░░] with "¦" at the thirds, the rows on the dark ground, "process kashapa exited (0) KILLED", the
/// foot. What is new types out a character at a time (Ui/Themes/Glitch/HudTerminal.Type.cs); the figures that tick never do.
/// Moments: a pickup's log line blooms on its ground; a rare drop prints "!! RARE: Shade Gloves" in its grade's colour
/// and the screen flickers once; your best passed runs "> record --new  OK"; an entry opening sounds a "BEEP" mark beside
/// its timer; a fight starts with its boot lines; a kill or a wipe blooms its exit line ("segfault: party wiped").
/// </summary>
sealed partial class HudTerminal : HudThemeLook, IThemeCard
{
    static readonly FontFamily Face = CrtFace;
    const uint PhosC = 0x5CFF9A, PaleC = 0xD6FFE4, AmberC = 0xFFB641, GroundC = 0x031008, RedC2 = 0xFF6B5E;
    static readonly Brush Phos = Br(PhosC), PhosDim = Br(PhosC, .76), PhosFaint = Br(PhosC, .5), Pale = Br(PaleC), Amber = Br(AmberC), Ground = Br(GroundC);
    static readonly Color Glow = Rgb(PhosC);

    readonly StackPanel _root = new() { HorizontalAlignment = HorizontalAlignment.Left };
    // the fight's screen 7 px in at the sides (4 expanded: Side): the `top` table's nine columns take real fights' figures whole
    readonly GlitchPanel _crt = new(new Thickness(12, 7, 12, 8)), _cam = new(new Thickness(7, 7, 7, 8));
    readonly Border _crtGround = Screen(), _camGround = Screen();

    readonly Border _promptLine = new();
    readonly TextBlock _prompt = Txt("", 12, PhosDim, Face, FontWeights.SemiBold), _runClock = Txt("", 11, PhosFaint, Face, FontWeights.SemiBold);
    readonly Border _ok = new() { Background = Phos, Padding = new Thickness(4, 0, 4, 0), Margin = new Thickness(8, 0, 0, 0), Visibility = Visibility.Collapsed, VerticalAlignment = VerticalAlignment.Center };
    readonly StackPanel _figLine = new() { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 3, 0, 0) };
    readonly TextBlock _sess = Txt("", 12.5, PhosDim, Face, FontWeights.SemiBold);
    /// <summary>The session line: the session, then the best drop and the last three in brackets while farming.</summary>
    readonly StackPanel _sessLine = new() { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 2, 0, 0) };
    readonly TextBlock _stats = Txt("", 11.5, PhosDim, Face, FontWeights.SemiBold);
    readonly Border _timerLine = new() { Visibility = Visibility.Collapsed };
    readonly TextBlock _timer = Txt("", 12, Amber, Face);
    readonly Border _beep = Beep();
    readonly Border _pickLine = new() { Margin = new Thickness(0, 2, 0, 0) };
    readonly TextBlock _pick = Txt("", 12, PhosDim, Face, FontWeights.SemiBold, trim: true);
    readonly Border _cursor = new() { Width = 7.5, Height = 14, Background = Phos, Margin = new Thickness(3, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
    readonly StackPanel _pickRow = new() { Orientation = Orientation.Horizontal };
    readonly Border _rareLine = new() { Margin = new Thickness(0, 3, 0, 0), Visibility = Visibility.Collapsed };
    FrameworkElement? _rareTile;
    readonly TextBlock _note = Txt("", 11.5, PhosDim, Face, FontWeights.SemiBold, trim: true);

    readonly TextBlock _fightPrompt = Txt("", 12.5, PhosDim, Face, trim: true), _clock = Txt("", 15, Amber, Face);
    readonly Border _camTimerLine = new() { Visibility = Visibility.Collapsed, Margin = new Thickness(0, 3, 0, 0) };
    readonly TextBlock _camTimer = Txt("", 12, Amber, Face);
    readonly Border _camBeep = Beep();
    readonly Grid _hpRow = new() { Margin = new Thickness(0, 6, 0, 2), Visibility = Visibility.Collapsed };
    readonly Grid _hpTrack = new() { Height = 10, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(2, 0, 2, 0) };
    readonly Border _hpFill = new() { HorizontalAlignment = HorizontalAlignment.Left };
    readonly TextBlock _hpText = Txt("", 11.5, PhosDim, Face, FontWeights.SemiBold);
    readonly TextBlock _ready = Txt("", 12, PhosDim, Face, FontWeights.SemiBold, trim: true);
    readonly StackPanel _rowsHost = new() { Margin = new Thickness(0, 0, 0, 0) };
    readonly Border _endLine = new() { Visibility = Visibility.Collapsed, Margin = new Thickness(0, 4, 0, 0) };
    readonly TextBlock _end = Txt("", 12, Phos, Face);
    readonly ContentControl _foot = new() { Focusable = false };
    double _hpShare = -1;

    public HudTerminal() : base(false, CrtFace)
    {
        // the meter's rows: a `top` table (Ui/Themes/Glitch/TerminalRows.cs)
        UseRows(TerminalRows.Pitch, _ => new TerminalRows.Row(), TerminalRows.GapHeight, TerminalRows.Gap);
        Build(_crt, _crtGround);
        Build(_cam, _camGround);
        TextElement.SetFontFamily(_cam.Body, Face);
        _cam.Root.Width = FightWidth;
        _cam.Root.Visibility = Visibility.Collapsed;
        _cam.Root.Margin = new Thickness(0, 4, 0, 0);

        var pl = new Grid();
        pl.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); pl.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); pl.ColumnDefinitions.Add(new ColumnDefinition());
        pl.Children.Add(_prompt);
        _ok.Child = Txt("OK", 11, Ground, Face);
        Grid.SetColumn(_ok, 1); pl.Children.Add(_ok);
        _runClock.HorizontalAlignment = HorizontalAlignment.Right; _runClock.Margin = new Thickness(16, 0, 0, 0);
        Grid.SetColumn(_runClock, 2); pl.Children.Add(_runClock);
        BuildRun(pl);   // a dungeon run's cursor after the prompt (Ui/Themes/Glitch/HudTerminal.Run.cs)
        _promptLine.Child = pl;
        _crt.Body.Children.Add(_promptLine);
        _crt.Body.Children.Add(_figLine);
        _sessLine.Children.Add(_sess);
        _crt.Body.Children.Add(_sessLine);
        _stats.Margin = new Thickness(0, 2, 0, 0); _stats.Visibility = Visibility.Collapsed;
        _crt.Body.Children.Add(_stats);
        _timerLine.Child = new StackPanel { Orientation = Orientation.Horizontal, Children = { _timer, _beep } };
        _timerLine.Margin = new Thickness(0, 2, 0, 0);
        _crt.Body.Children.Add(_timerLine);
        _pickRow.Children.Add(_pick); _pickRow.Children.Add(_cursor);
        _pickLine.Child = _pickRow;
        _crt.Body.Children.Add(_pickLine);
        _crt.Body.Children.Add(_rareLine);
        _note.Margin = new Thickness(0, 3, 0, 0); _note.Visibility = Visibility.Collapsed; _note.MaxWidth = 380; _note.HorizontalAlignment = HorizontalAlignment.Left;
        _crt.Body.Children.Add(_note);

        var head = new Grid();
        head.ColumnDefinitions.Add(new ColumnDefinition()); head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        head.Children.Add(_fightPrompt);
        _clock.Margin = new Thickness(10, 0, 0, 0); Grid.SetColumn(_clock, 1); head.Children.Add(_clock);
        _cam.Body.Children.Add(head);
        _camTimerLine.Child = new StackPanel { Orientation = Orientation.Horizontal, Children = { _camTimer, _camBeep } };
        _cam.Body.Children.Add(_camTimerLine);
        // the HP: [cells¦cells¦cells] 47% 1.82M left
        _hpRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); _hpRow.ColumnDefinitions.Add(new ColumnDefinition());
        _hpRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); _hpRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        _hpRow.Children.Add(Txt("[", 13, Phos, Face));
        var cells = Segments(Rgb(GroundC), 6, 1.5);
        _hpTrack.Children.Add(new Border { Background = Br(PhosC, .16) });
        _hpFill.Background = Phos; _hpTrack.Children.Add(_hpFill);
        _hpTrack.Children.Add(new Rectangle { Fill = cells, IsHitTestVisible = false });
        _hpTrack.SizeChanged += (_, _) => Share(_hpTrack, _hpFill, _hpShare);
        var hpCell = new Grid { VerticalAlignment = VerticalAlignment.Center };
        hpCell.Children.Add(_hpTrack);
        hpCell.Children.Add(Thirds());
        Grid.SetColumn(hpCell, 1); _hpRow.Children.Add(hpCell);
        var close = Txt("]", 13, Phos, Face); Grid.SetColumn(close, 2); _hpRow.Children.Add(close);
        _hpText.Margin = new Thickness(8, 0, 0, 0); Grid.SetColumn(_hpText, 3); _hpRow.Children.Add(_hpText);
        _cam.Body.Children.Add(_hpRow);
        _ready.Margin = new Thickness(0, 5, 0, 1); _ready.Visibility = Visibility.Collapsed;
        _cam.Body.Children.Add(_ready);
        _cam.Body.Children.Add(_rowsHost);
        // a long boss's exit line wraps as a terminal's does (it ran past the meter's edge: "...exited (0)  KIL")
        _end.TextWrapping = TextWrapping.Wrap;
        _endLine.Child = _end;
        _cam.Body.Children.Add(_endLine);
        _cam.Body.Children.Add(_foot);

        _root.Children.Add(_crt.Root);
        _root.Children.Add(_cam.Root);
    }

    /// <summary>The screen's glass: the dark green ground in a faint green rim, rounded as a tube's.</summary>
    static Border Screen() => new() { Background = Br(GroundC, .96), CornerRadius = new CornerRadius(7), BorderBrush = Br(PhosC, .3), BorderThickness = new Thickness(1) };

    /// <summary>The screen's layers: the glass, a centre glow and a darker curve to the rim; the scanlines over the text.</summary>
    static void Build(GlitchPanel p, Border ground)
    {
        p.Ground.Children.Add(ground);
        p.Ground.Children.Add(new Border { CornerRadius = new CornerRadius(7), Background = Vignette(Colors.Black, .5, 0), Margin = new Thickness(1) });
        p.Ground.Children.Add(new Border { CornerRadius = new CornerRadius(7), Background = CentreGlow, Margin = new Thickness(1) });
        p.Over.Children.Add(new Border { CornerRadius = new CornerRadius(7), Background = Lines(Rgb(0x000000, .22), 3, 1), Margin = new Thickness(1) });
        p.Fx.Clip = null;
    }

    static readonly Brush CentreGlow = MakeCentre();
    static Brush MakeCentre()
    {
        var b = new RadialGradientBrush { Center = new Point(.5, .45), GradientOrigin = new Point(.5, .45), RadiusX = .7, RadiusY = .8 };
        b.GradientStops.Add(new GradientStop(Rgb(PhosC, .07), 0));
        b.GradientStops.Add(new GradientStop(Rgb(PhosC, 0), 1));
        b.Freeze();
        return b;
    }

    static Border Beep() => new()
    {
        Background = Br(AmberC), Padding = new Thickness(4, 0, 4, 0), Margin = new Thickness(10, 0, 0, 0), Visibility = Visibility.Hidden, VerticalAlignment = VerticalAlignment.Center,
        Child = Txt(L.T("BEEP"), 11, Br(GroundC), CrtFace),
    };

    /// <summary>The bar's "¦" at a third and two thirds: a broken pale line standing a little out of the bar.</summary>
    static Grid Thirds()
    {
        var g = new Grid { IsHitTestVisible = false, Margin = new Thickness(2, -2, 2, -2) };
        for (int i = 0; i < 3; i++) g.ColumnDefinitions.Add(new ColumnDefinition());
        for (int i = 0; i < 2; i++)
        {
            var line = new Line { Y1 = 0, Y2 = 14, Stroke = Pale, StrokeThickness = 1.6, StrokeDashArray = new DoubleCollection { 2.6, 1.4 }, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 0, -1, 0), Stretch = Stretch.None };
            Grid.SetColumn(line, i); g.Children.Add(line);
        }
        return g;
    }

    public override FrameworkElement Root => _root;

    /// <summary>The member's skills card in this look (Ui/Themes/Glitch/GlitchCards.cs).</summary>
    public FrameworkElement DrawCard(MemberCard c) => GlitchCards.Terminal(c, Moves);
    public override double Corner => 7;
    public override FrameworkElement? FightFrame => _cam.Root.Visibility == Visibility.Visible ? _cam.Root : null;

    public override void SetOpacity(double opacity)
    {
        double o = Math.Clamp(opacity, 0.3, 1);
        _crtGround.Background = Br(GroundC, Math.Min(1, o * 1.1));
        _camGround.Background = Br(GroundC, Math.Min(1, o * 1.12));
    }

    protected override string LineSig(Figures f) => $"{base.LineSig(f)}|{Values.GetValueOrDefault("time")}|{Values.GetValueOrDefault("run")}|{Values.GetValueOrDefault("rare")}{Values.GetValueOrDefault("rareId")}|{Picks(Values).FirstOrDefault()?.Id}|{PickStrip.Sig(Values, Strip)}|{FightWidth}|{RunBuddy.Sig(Values)}";

    /// <summary>The pickup strip on the screen: the rule a phosphor hairline, the star amber.</summary>
    static readonly PickStrip.Look Strip = new(22, 5, true, Br(PhosC, .4), Amber);

    string _figSig = "";

    protected override void DrawLine(Figures f)
    {
        var (run, clock, odd) = RunClock(Values);
        run = run || Values.GetValueOrDefault("run") == "1";
        // in a dungeon run the screen runs the run: the dungeon at the prompt, its count, clock and splits (Ui/Themes/Glitch/HudTerminal.Run.cs)
        var rs = RunOf(Values, KinahHidden);
        ShowRun(rs != null);
        FreshDraw(rs);   // a pickup or a clear just made types on (Ui/Themes/Glitch/HudTerminal.Type.cs)
        if (rs != null) RunPrompt(rs);
        else
        {
            _runClock.Text = $"[{(run ? L.T("run") + " " : "")}{(clock.Length > 0 ? clock : "0:00")}]";
            _cursor.Opacity = odd ? 1 : .28;
        }
        var p0 = Picks(Values).FirstOrDefault();
        var sig = $"{f.Sig}|{f.ToBest}|{KinahHidden}|{Values.GetValueOrDefault("rare")}{Values.GetValueOrDefault("rareId")}|{p0?.Id}|{PickStrip.Sig(Values, Strip)}|{FightWidth}|{RunBuddy.Sig(Values)}";
        if (sig == _figSig) return;
        _figSig = sig;
        if (rs != null) { DrawRun(rs); Note(f); return; }

        // the prompt: the rate asked for; a new best, the record written
        _prompt.Text = f.Beat ? L.T("> record --new") : f.Abyss ? L.T("> abyss --rate") : L.T("> kinah --rate");
        _ok.Visibility = f.Beat ? Visibility.Visible : Visibility.Collapsed;
        PromptDrawn(f.Beat);

        _figLine.Children.Clear();
        if (f.Abyss && f.Counted && f.Icon.Length > 0) { var m = AbyssMark(f.Icon, 28); m.Margin = new Thickness(0, 0, 9, 0); _figLine.Children.Add(m); }
        if (!f.Counted || f.Rate.Length == 0) _figLine.Children.Add(Txt("# " + Waiting(f.Abyss), 12.5, PhosDim, Face, FontWeights.SemiBold));
        else if (f.Warm) _figLine.Children.Add(Phosphor(f.Rate, 18, Face, FontWeights.SemiBold, PhosDim, Glow, .8, .18));
        else
        {
            _figLine.Children.Add(Phosphor(f.Rate, 25, Face, FontWeights.Bold, Pale, Glow, .9, .32));
            var u = Txt(f.Unit, 14, Phos, Face); u.VerticalAlignment = VerticalAlignment.Bottom; u.Margin = new Thickness(2, 0, 0, 4);
            _figLine.Children.Add(u);
            if (f.Share >= 0)
            {
                var best = Txt(f.Beat ? "  [" + L.T("★ new best") + "]" : "  " + L.F("[{0}% of best]", Math.Round(Math.Clamp(f.Share, 0, 1) * 100)), 12, f.Beat ? Pale : Phos, Face, FontWeights.SemiBold);
                best.VerticalAlignment = VerticalAlignment.Bottom; best.Margin = new Thickness(4, 0, 0, 5);
                _figLine.Children.Add(best);
            }
        }

        _sess.Inlines.Clear();
        if (f.Counted && f.Total.Length > 0)
        {
            _sess.Inlines.Add(Rn("  " + f.TotalWord + " ", PhosDim));
            _sess.Inlines.Add(Rn(f.Total, Pale, 14, FontWeights.Bold));
        }
        _sess.Visibility = _sess.Inlines.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        // the best drop and the last three after the session, in the console's brackets ("session 894K  [* # | # # #]"), while
        // farming: the Abyss, the idle line and a rare drop's "!! RARE" line keep the screen as it was
        while (_sessLine.Children.Count > 1) _sessLine.Children.RemoveAt(1);
        if (_sess.Inlines.Count > 0 && !f.Abyss && f.Rate.Length > 0 && Rare(Values) == null)
        {
            var open = Txt("[", 14, Phos, Face, FontWeights.SemiBold); open.Margin = new Thickness(12, 0, 5, 0);
            var close = Txt("]", 14, Phos, Face, FontWeights.SemiBold); close.Margin = new Thickness(5, 0, 0, 0);
            double room = FightWidth - _crt.Body.Margin.Left - _crt.Body.Margin.Right - Wide(_sess) - Wide(open) - Wide(close);
            if (FitStrip(Values, Strip, room) is { } strip)
            {
                _sessLine.Children.Add(open); _sessLine.Children.Add(strip); _sessLine.Children.Add(close);
            }
        }
        _sessLine.Visibility = _sess.Visibility;

        _stats.Inlines.Clear();
        foreach (var (fig, word, tone) in f.Stats)
        {
            _stats.Inlines.Add(Rn(_stats.Inlines.Count > 0 ? "  · " : "  ", PhosFaint));
            _stats.Inlines.Add(Rn(fig, ReferenceEquals(tone, Ink) ? Pale : tone, 12.5, FontWeights.Bold));
            _stats.Inlines.Add(Rn(" " + word, PhosDim));
        }
        _stats.Visibility = _stats.Inlines.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

        // the last pickup as a log line, the cursor after it; none yet: the prompt waiting
        _pick.Inlines.Clear();
        _pick.Inlines.Add(Rn("> ", PhosDim));
        if (p0 != null)
        {
            if (p0.Worth.Length > 0) _pick.Inlines.Add(Rn(p0.Worth + " ", Pale, null, FontWeights.Bold));
            _pick.Inlines.Add(Rn(p0.Name, Phos));
        }
        _pick.MaxWidth = 330;
        if (_type.Active(KeyPick)) _type.Rebind(KeyPick, PickSegs());   // drawn again while it types: it goes on in the new runs

        // a rare drop: "!! RARE: Shade Gloves" in its grade's colour, its tile
        if (Rare(Values) is { } r)
        {
            var c = Frozen(ColorOf(r.Color, r.Grade));
            var sp = new StackPanel { Orientation = Orientation.Horizontal };
            sp.Children.Add(Txt("!! " + L.T("RARE") + ": ", 12.5, c, Face));
            _rareTile = Tile(r, 22); _rareTile.Margin = new Thickness(0, 0, 7, 0);
            sp.Children.Add(_rareTile);
            sp.Children.Add(Txt(r.Name, 12.5, c, Face, trim: true));
            _rareLine.Child = sp;
            _rareLine.Visibility = Visibility.Visible;
        }
        else { _type.Cancel(KeyRare); _rareLine.Child = null; _rareTile = null; _rareLine.Visibility = Visibility.Collapsed; }
        if (_type.Active(KeyRare) && _rareLine.Child is StackPanel rsp) _type.Rebind(KeyRare, RareSegs(rsp));

        Note(f);
    }

    /// <summary>The note under the screen's lines ("  ! ..."), in its tone.</summary>
    void Note(Figures f)
    {
        _note.Text = f.Note.Length > 0 ? "  ! " + f.Note : "";
        _note.Foreground = f.NoteTone;
        _note.Visibility = f.Note.Length > 0 && !KinahHidden ? Visibility.Visible : Visibility.Collapsed;
    }

    protected override void DrawTimers(IReadOnlyList<TimerBoard.Item> due, DateTime now)
    {
        var next = due.FirstOrDefault();
        SetTimer(_timerLine, _timer, KinahHidden ? null : next, now);
        SetTimer(_camTimerLine, _camTimer, KinahHidden ? next : null, now);
    }

    /// <summary>"  shugo: ENTER 2:41": amber while its entry is open, a paler amber when soon, green else.</summary>
    static void SetTimer(Border line, TextBlock t, TimerBoard.Item? i, DateTime now)
    {
        t.Inlines.Clear();
        if (i == null) { line.Visibility = Visibility.Collapsed; return; }
        var (text, tone) = TimerWords(i, now);
        var (name, rest) = TimerParts(i, text);
        var b = tone == TimerBadge.Tone.On ? Amber : tone == TimerBadge.Tone.Soon ? Br(AmberC, .9) : PhosDim;
        t.Inlines.Add(Rn("  " + (name.Length > 0 ? name + ": " : ""), b));
        t.Inlines.Add(Rn(rest, b, null, tone == TimerBadge.Tone.On ? FontWeights.Bold : FontWeights.SemiBold));
        line.Visibility = Visibility.Visible;
    }

    protected override void KinahShown(bool shown)
    {
        _crt.Root.Visibility = shown ? Visibility.Visible : Visibility.Collapsed;
        _cam.Root.Margin = new Thickness(0, shown ? 4 : 0, 0, 0);
    }

    /// <summary>
    /// The fight's screen's inset at the sides, set on it: 7 px, 4 expanded (its damage and crit column takes the 70 px the meter
    /// widens by and more, in Korean and Chinese most), so every column keeps its widest figure.
    /// </summary>
    double Side()
    {
        double side = ParserRows.ExpandedPref ? 4 : 7;
        _cam.Body.Margin = new Thickness(side, 7, side, 8);
        return side;
    }

    protected override void DrawFight(Encounter? e, DateTime now)
    {
        if (e == null) { _cam.Root.Visibility = Visibility.Collapsed; _rowsHost.Children.Clear(); _foot.Content = null; _fightPrompt.Inlines.Clear(); _end.Inlines.Clear(); _hpShare = -1; return; }
        _cam.Root.Width = FightWidth;
        Side();
        var name = e.Title.ToLower(System.Globalization.CultureInfo.CurrentCulture);
        _fightPrompt.Inlines.Clear();
        _fightPrompt.Inlines.Add(Rn(L.T("> fight "), PhosDim));
        _fightPrompt.Inlines.Add(Rn(e.Title, Pale, null, FontWeights.Bold));
        _rowsHost.Children.Clear(); _foot.Content = null; _rowsHost.Visibility = Visibility.Collapsed; _foot.Visibility = Visibility.Collapsed;
        if (e.Waiting)
        {
            _clock.Text = L.T("ready"); _clock.Foreground = PhosDim; _clock.FontSize = 12.5;
            _hpRow.Visibility = Visibility.Collapsed; _endLine.Visibility = Visibility.Collapsed;
            _ready.Inlines.Clear();
            _ready.Inlines.Add(Rn("  " + e.ReadyText + " ", PhosDim));
            _ready.Inlines.Add(Rn("█", Phos));
            _ready.Visibility = Visibility.Visible;
            if (PartyBeforeHit(e)) { PutRows(_rowsHost, e, now, FightWidth - 2 * Side()); _rowsHost.Visibility = Visibility.Visible; }
            _cam.Root.Visibility = Visibility.Visible;
            return;
        }
        _ready.Visibility = Visibility.Collapsed;
        _clock.Text = Clock(e.Duration(now)); _clock.Foreground = e.Live ? Amber : Pale; _clock.FontSize = 15;
        if (HpOf(e) is double hp)
        {
            _hpShare = hp; Share(_hpTrack, _hpFill, hp);
            _hpFill.Background = hp < .34 ? Amber : Phos;
            HpGhost(_hpTrack, e, hp, now, 0);
            _hpText.Inlines.Clear();
            _hpText.Inlines.Add(Rn($"{Math.Round(hp * 100):0}%", hp < .34 ? Amber : Pale, 12.5, FontWeights.Bold));
            _hpText.Inlines.Add(Rn("  " + HpLeftText(e), PhosDim));
            _hpRow.Visibility = Visibility.Visible;
        }
        else _hpRow.Visibility = Visibility.Collapsed;
        _rowsHost.Children.Add(TerminalRows.Header(ParserRows.ExpandedPref));
        PutRows(_rowsHost, e, now, FightWidth - 2 * Side());
        _rowsHost.Visibility = Visibility.Visible;
        // over: the process's exit line, a kill in mint, a wipe a segfault in red
        var outcome = OutcomeText(e);
        if (outcome.Length > 0)
        {
            _end.Inlines.Clear();
            if (e.Outcome == "wipe") _end.Inlines.Add(Rn(L.T("segfault: party wiped"), Br(RedC2), null, FontWeights.Bold));
            else
            {
                _end.Inlines.Add(Rn(L.F("process {0} exited (0)", name) + "  ", PhosDim, null, FontWeights.SemiBold));
                _end.Inlines.Add(Rn(outcome.ToUpper(System.Globalization.CultureInfo.CurrentCulture), e.Outcome is "kill" or "cleared" ? Br(0x7DFFC4) : Pale, null, FontWeights.Bold));
            }
            _endLine.Visibility = Visibility.Visible;
        }
        else _endLine.Visibility = Visibility.Collapsed;
        if (e.HasDamage) _foot.Content = TerminalRows.Foot(e, now);
        _foot.Visibility = _foot.Content != null ? Visibility.Visible : Visibility.Collapsed;
        _cam.Root.Visibility = Visibility.Visible;
    }

    // ---- the moments

    /// <summary>A phosphor bloom on a line's ground: the line lights in <paramref name="c"/> and the light decays as phosphor does.</summary>
    static void Bloom(Border line, Color c, double ms, double peak = .42)
    {
        var bg = new SolidColorBrush(Color.FromArgb(0, c.R, c.G, c.B));
        line.Background = bg;
        Motion.Play(line, ms, t =>
        {
            double a = t < .06 ? t / .06 : Math.Pow(1 - (t - .06) / .94, 2.2);
            bg.Color = Color.FromArgb((byte)(255 * peak * a), c.R, c.G, c.B);
        }, () => line.Background = null);
    }

    /// <summary>The screen flickers once: its ground jumps bright, dark, a little bright, back (the text stays put).</summary>
    static void Flicker(GlitchPanel p, Color c, double ms)
    {
        var r = new Border { CornerRadius = new CornerRadius(7) };
        p.Run(r, ms, t =>
        {
            int s = (int)(t * 14);
            r.Background = s switch { 0 => Frozen(Color.FromArgb(77, c.R, c.G, c.B)), 1 => Br(0x000000, .45), 2 => Frozen(Color.FromArgb(41, c.R, c.G, c.B)), 3 => Br(0x000000, .2), _ => null };
        });
    }

    protected override void Pickup(Pick p, bool held)
    {
        if (KinahHidden) return;
        Bloom(_pickLine, Glow, 1100);
        Motion.Play(_cursor, 1100, t => _cursor.Opacity = 1);
        // the log line types out; only a pickup just made (a price refresh or a loaded session writes whole), never in a run
        if (!_runOn && (held || _freshPick?.Id == p.Id)) TypePick(p, held);
    }

    protected override void RareDrop(Pick p, bool held)
    {
        if (KinahHidden) return;
        var c = ColorOf(p.Color, p.Grade);
        Flicker(_crt, Glow, 800);
        Bloom(_rareLine, c, 1800, .38);
        if (!_runOn) TypeRare(held);
        if (_rareTile is { } tile)
        {
            var s = new ScaleTransform(); tile.RenderTransformOrigin = new Point(.5, .5); tile.RenderTransform = s;
            Motion.Play(tile, 520, t => s.ScaleX = s.ScaleY = Math.Max(0, Back(Math.Clamp((t - .12) / .88, 0, 1))), () => tile.ClearValue(UIElement.RenderTransformProperty));
        }
    }

    protected override void BestPassed(bool held)
    {
        if (KinahHidden) return;
        Bloom(_promptLine, Glow, 1600, .45);
        if (!_runOn) TypePrompt(held);
        var ok = (TextBlock)_ok.Child;
        Motion.Play(_ok, 1600, t =>
        {
            bool on = t >= .5 || (int)(t * 10) % 2 == 0;
            _ok.Background = on ? Phos : null; ok.Foreground = on ? Ground : Phos;
        }, () => { _ok.Background = Phos; ok.Foreground = Ground; });
    }

    protected override void EntryOpened(TimerBoard.Item i, bool held)
    {
        var (line, beep) = KinahHidden ? (_camTimerLine, _camBeep) : (_timerLine, _beep);
        Bloom(line, Rgb(AmberC), 1500, .3);
        beep.Visibility = Visibility.Visible;
        Motion.Play(beep, 1800, t => beep.Visibility = t < .85 && (t > .45 || t * 8 % 1 < .6) ? Visibility.Visible : Visibility.Hidden, () => beep.Visibility = Visibility.Hidden);
    }

    protected override void FightStarted(Encounter e, bool held)
    {
        // the boot lines: the screen clears, three lines come up one after another (each whole), then the meter shows through
        var name = e.Title.ToLower(System.Globalization.CultureInfo.CurrentCulture);
        // the boot lines over the head (the meter stays drawn under them)
        var cover = new Border { CornerRadius = new CornerRadius(7, 7, 0, 0), Background = Br(GroundC), Padding = new Thickness(12, 7, 12, 6), VerticalAlignment = VerticalAlignment.Top, BorderBrush = Br(PhosC, .5), BorderThickness = new Thickness(0, 0, 0, 1) };
        var lines = new StackPanel();
        var l1 = Txt(L.T("> fight start"), 12.5, Pale, Face);
        var l2 = Txt("  " + L.F("attach {0} ... ok", e.Title), 12, Phos, Face, FontWeights.SemiBold, trim: true);
        var l3 = Txt("  " + L.T("[ ok ] meter online"), 12, Phos, Face, FontWeights.SemiBold);
        var cur = new Border { Width = 7.5, Height = 14, Background = Phos, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 2, 0, 0) };
        foreach (var l in new UIElement[] { l1, l2, l3, cur }) lines.Children.Add(l);
        cover.Child = lines;
        _cam.Run(cover, 1300, t =>
        {
            l1.Visibility = t >= 0 ? Visibility.Visible : Visibility.Hidden;
            l2.Visibility = t >= .1 ? Visibility.Visible : Visibility.Hidden;
            l3.Visibility = t >= .3 ? Visibility.Visible : Visibility.Hidden;
            cur.Opacity = (int)(t * 8) % 2 == 0 ? 1 : .2;
            // the boot lines clear first, then the screen comes up under them
            lines.Opacity = t < .56 ? 1 : 0;
            cover.Opacity = t < .6 ? 1 : 1 - Motion.Out((t - .6) / .4);
        });
    }

    protected override void FightEnded(Encounter e, bool held)
    {
        bool wipe = e.Outcome == "wipe";
        Flicker(_cam, wipe ? Rgb(RedC2) : Glow, 800);
        Bloom(_endLine, wipe ? Rgb(RedC2) : Rgb(0x7DFFC4), 2000, .34);
        if (!wipe && Meter?.Members.FirstOrDefault(m => m.TopDps) is { } top && Meter.RowOf(top.Id) is TerminalRows.Row row) row.Bloom(Rgb(AmberC), 2000);
    }
}
