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
/// The Glitch theme's two tapes (Steven, 2026-11-17, of the VHS look: "cool as well but not legible enough"; of three concepts he
/// picked two: "Build viewfinder and b deck"). Both keep the tape (REC, PLAY, STOP, the counter, the channel, tracking noise)
/// and draw every name and figure crisp in Inter, the app's own face, the tape's effects on the frame and ground only.
/// <list type="bullet">
/// <item>Viewfinder (key "vhs", the VHS look's, so a saved choice carries over): a camcorder's black glass, lit from above, fine
/// scanlines, two faint tracking lines and the head-switching noise at its foot, white corner marks split red and cyan. The
/// Kinah line: "● REC" (the dot blinks with the run clock's seconds) and the counter "SP 00:42:17", the hour big and white,
/// your best as a battery level, the session and the next timer. The fight: "● REC" or "■ STOP  KILLED" with the channel,
/// the boss and its clock big, the HP with ticks at the thirds, the rows (Ui/Themes/Glitch/VhsRows.cs), the foot.</item>
/// <item>Deck (key "deck"): a VCR's front, dark brushed gunmetal, a black glass display window with a cyan fluorescent
/// display behind it (its rim split red and cyan). The Kinah line sits in the display, the hour in the display's cyan; the
/// fight's head is a display window of its own over rows that are each a bar, the DPS in a counter window.</item>
/// </list>
/// Moments: a pickup rolls a tracking band down the panel and "▶▶ +7.5K" lights in the top row; a rare drop flashes
/// white and holds a freeze-frame ("‖ PAUSE") with its tile; your best passed runs a "★ REC BEST" title card; an entry
/// opening flashes "▶ PLAY" on its timer; a fight starts with a channel switch (a static burst, the channel's OSD); a kill
/// stamps "■ STOP — KILLED", a wipe loses the tracking on a blue band.
/// </summary>
sealed partial class HudVhs : HudThemeLook, IThemeCard
{
    public enum Tape { Viewfinder, Deck }

    static readonly FontFamily Face = VhsFace, Sans = VhsRows.Sans;
    const uint RedC = 0xFF3B30, CyanC = 0x28E0FF, YellowC = 0xFFD23F, GreenC = 0x45FF86, BlueC = 0x1432C8, MintTextC = 0x5CFFB0, VfdC = VhsRows.VfdC;
    static readonly Brush White = Br(0xFFFFFF), Dim = Br(0xFFFFFF, .66), Faded = Br(0xFFFFFF, .42), Red = Br(RedC), Yellow = Br(YellowC), Green = Br(GreenC), Black = Br(0x000000);

    readonly Tape _tape;
    bool Deck => _tape == Tape.Deck;
    /// <summary>The tape's inks: the big figures, the words beside them, the quiet labels.</summary>
    readonly Brush _ink, _text, _label;

    readonly StackPanel _root = new() { HorizontalAlignment = HorizontalAlignment.Left };
    readonly GlitchPanel _deck, _cam;
    readonly Border _deckBack = new(), _camBack = new();

    // the Kinah line: the tape row, the hour and your best, the session and the timer, the Abyss's stats, a rare drop, the note
    readonly System.Windows.Shapes.Ellipse _dot = new() { Width = 8, Height = 8, Fill = Red, Margin = new Thickness(0, 0, 5, 1), VerticalAlignment = VerticalAlignment.Center };
    readonly TextBlock _recWord = Txt(L.T("REC"), 11, Red, Face);
    readonly StackPanel _rec = new() { Orientation = Orientation.Horizontal };
    readonly TextBlock _tc = Txt("", 11, White, Face), _ff = Txt("", 11, White, Face);
    readonly Grid _figRow = new() { Margin = new Thickness(0, 6, 0, 0) };
    readonly Grid _sessRow = new() { Margin = new Thickness(0, 6, 0, 0) };
    readonly TextBlock _sess = Txt("", 13, White, Sans);
    readonly Grid _timerCell = new() { HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(14, 0, 0, 0) };
    readonly TextBlock _timer = Txt("", 11.5, White, Sans, FontWeights.SemiBold);
    readonly Border _play = PlayTag();
    readonly TextBlock _stats = Txt("", 11, Dim, Sans, FontWeights.SemiBold);
    readonly Grid _rare = new() { Margin = new Thickness(0, 6, 0, 0), Visibility = Visibility.Collapsed };
    readonly Border _pause = new() { Background = Yellow, Padding = new Thickness(4, 1, 4, 0), VerticalAlignment = VerticalAlignment.Center, Child = Txt("‖ " + L.T("FREEZE"), 10, Black, VhsFace) };
    FrameworkElement? _rareTile;
    readonly TextBlock _note = Txt("", 11, Dim, Sans, FontWeights.SemiBold, trim: true);

    // the camera (the fight): the OSD line (the tape's state, the channel), the boss and its clock, the HP, the rows, the foot
    readonly StackPanel _state = new() { Orientation = Orientation.Horizontal };
    readonly TextBlock _chText = Txt("", 10, Dim, Face);
    string _channel = "CH 03";
    readonly TextBlock _title, _clock;
    readonly Grid _camTimerCell = new() { HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 7, 0, 0), Visibility = Visibility.Collapsed };
    readonly TextBlock _camTimer = Txt("", 11.5, White, Sans, FontWeights.SemiBold);
    readonly Border _camPlay = PlayTag();
    readonly Grid _hpRow = new() { Margin = new Thickness(0, 9, 0, 0), Visibility = Visibility.Collapsed };
    readonly Grid _hpTrack = new() { VerticalAlignment = VerticalAlignment.Center };
    readonly Border _hpFill = new() { HorizontalAlignment = HorizontalAlignment.Left };
    readonly TextBlock _hpText = Txt("", 12, White, Sans);
    readonly TextBlock _ready = Txt("", 11.5, Dim, Sans, FontWeights.SemiBold, trim: true);
    readonly StackPanel _rowsHost = new();
    readonly ContentControl _foot = new() { Focusable = false };
    double _hpShare = -1;

    public HudVhs() : this(Tape.Viewfinder) { }

    public HudVhs(Tape kind) : base(false, CodeFace)
    {
        _tape = kind;
        _ink = Deck ? Br(VfdC) : White;
        _text = Deck ? Br(0xE6FDFF) : White;
        _label = Deck ? Br(VfdC, .66) : Dim;
        _deck = new GlitchPanel(Deck ? new Thickness(16, 12, 16, 13) : new Thickness(14, 10, 14, 11));
        _cam = new GlitchPanel(Deck ? new Thickness(12, 10, 12, 10) : new Thickness(14, 9, 14, 9));
        _title = Txt("", Deck ? 15 : 15.5, _text, Sans, trim: true);
        _clock = Txt("", 19, Yellow, Sans);
        // the meter's rows: the tape's own (Ui/Themes/Glitch/VhsRows.cs)
        if (Deck) UseRows(VhsRows.DeckPitch, _ => new VhsRows.DeckRow(), VhsRows.DeckGap, VhsRows.Gap);
        else UseRows(VhsRows.ViewPitch, _ => new VhsRows.ViewRow(), VhsRows.ViewGap, VhsRows.Gap);
        if (Deck) { DeckGround(_deck, _deckBack, display: true); DeckGround(_cam, _camBack, display: false); }
        else { ViewGround(_deck, _deckBack, 0, 0); ViewGround(_cam, _camBack, 64, 92); }
        _cam.Root.Width = FightWidth;
        _cam.Root.Visibility = Visibility.Collapsed;
        _cam.Root.Margin = new Thickness(0, 4, 0, 0);
        TextElement.SetFontFamily(_cam.Body, Sans);
        TextElement.SetFontFamily(_foot, Sans);
        _tc.Foreground = Deck ? Br(VfdC) : White;
        _sess.Foreground = _text; _timer.Foreground = _text;

        // the tape row: REC, a pickup's fast-forward mark, the counter
        _rec.Children.Add(_dot); _rec.Children.Add(_recWord);
        var tape = new Grid();
        tape.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); tape.ColumnDefinitions.Add(new ColumnDefinition()); tape.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        tape.Children.Add(_rec);
        _ff.HorizontalAlignment = HorizontalAlignment.Right; _ff.Margin = new Thickness(14, 0, 12, 0); _ff.Visibility = Visibility.Hidden;
        Grid.SetColumn(_ff, 1); tape.Children.Add(_ff);
        Grid.SetColumn(_tc, 2); tape.Children.Add(_tc);
        BuildRun(tape);   // a dungeon run's head and count, in the tape row (Ui/Themes/Glitch/HudVhs.Run.cs)
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

        // the camera: the head (Deck's in a display window of its own), the timer in a dungeon, the HP, the ready line, the rows, the foot
        _cam.Body.Children.Add(Head());
        _camTimerCell.Children.Add(_camTimer); _camTimerCell.Children.Add(_camPlay);
        _cam.Body.Children.Add(_camTimerCell);
        BuildHp();
        _cam.Body.Children.Add(_hpRow);
        _ready.Margin = new Thickness(0, 8, 0, 1); _ready.Visibility = Visibility.Collapsed;
        _cam.Body.Children.Add(_ready);
        _rowsHost.Margin = new Thickness(0, Deck ? 10 : 8, 0, 0);
        if (!Deck) _hpRow.Margin = new Thickness(0, 6, 0, 0);
        _cam.Body.Children.Add(_rowsHost);
        _cam.Body.Children.Add(_foot);

        _root.Children.Add(_deck.Root);
        _root.Children.Add(_cam.Root);
    }

    // ---- the grounds

    /// <summary>
    /// Viewfinder's glass: black lit from above, a vignette, fine scanlines, two faint tracking lines rolling through the picture
    /// (under every word) and the head-switching noise at its foot; over it the corner marks, split red and cyan behind the white.
    /// </summary>
    static void ViewGround(GlitchPanel p, Border back, double trackTop, double trackBottom)
    {
        p.Root.ClipToBounds = true;
        back.CornerRadius = new CornerRadius(3);
        back.Background = VhsRows.Vertical((Rgb(0x0B0D10, .92), 0), (Rgb(0x000000, .88), 1));
        p.Ground.Children.Add(back);
        p.Ground.Children.Add(new Rectangle { Fill = Vignette(Colors.Black, .5), RadiusX = 3, RadiusY = 3 });
        p.Ground.Children.Add(new Rectangle { Fill = VhsRows.Vertical((Rgb(0xFFFFFF, .07), 0), (Rgb(0xFFFFFF, 0), .3)), RadiusX = 3, RadiusY = 3 });
        p.Ground.Children.Add(new Rectangle { Fill = Lines(Rgb(0xFFFFFF, .035), 3, 1) });
        if (trackTop > 0) p.Ground.Children.Add(new Rectangle { Fill = NoiseBrush(.16), Height = 2, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, trackTop, 0, 0) });
        if (trackBottom > 0) p.Ground.Children.Add(new Rectangle { Fill = NoiseBrush(.12), Height = 1, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 0, trackBottom) });
        p.Ground.Children.Add(new Rectangle { Fill = NoiseBrush(.4), Height = 3, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 0, 1) });
        var red = Corners(Br(RedC, .75), 10, 1.6, 5); red.RenderTransform = new TranslateTransform(-1.2, 0);
        var cyan = Corners(Br(CyanC, .75), 10, 1.6, 5); cyan.RenderTransform = new TranslateTransform(1.2, 0);
        p.Over.Children.Add(red); p.Over.Children.Add(cyan); p.Over.Children.Add(Corners(Br(0xFFFFFF, .92), 10, 1.6, 5));
    }

    /// <summary>Deck's front: dark brushed gunmetal with a lit top edge; with <paramref name="display"/>, the display window set into it behind the body.</summary>
    static void DeckGround(GlitchPanel p, Border back, bool display)
    {
        p.Root.ClipToBounds = true;
        back.CornerRadius = new CornerRadius(4);
        back.Background = VhsRows.Vertical((Rgb(0x23262D, .96), 0), (Rgb(0x121419, .96), .3), (Rgb(0x08090B, .96), 1));
        back.BorderBrush = VhsRows.Vertical((Rgb(0xFFFFFF, .22), 0), (Rgb(0xFFFFFF, .05), .2), (Rgb(0x000000, .6), 1));
        back.BorderThickness = new Thickness(1);
        p.Ground.Children.Add(back);
        p.Ground.Children.Add(new Rectangle { Fill = Lines(Rgb(0xFFFFFF, .022), 2, 1), RadiusX = 4, RadiusY = 4 });
        if (display) p.Ground.Children.Add(Display(new Thickness(6), new Point(.2, .5)));
        else p.Ground.Children.Add(new Rectangle { Fill = NoiseBrush(.3), Height = 2, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(3, 0, 3, 2) });
    }

    /// <summary>The deck's display window: black glass, the display's faint cyan mesh, a haze of its light round <paramref name="glow"/>, its rim split red and cyan.</summary>
    static Grid Display(Thickness margin, Point glow)
    {
        var w = new Grid { Margin = margin, IsHitTestVisible = false };
        w.Children.Add(new Border { BorderBrush = Br(RedC, .38), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(3), RenderTransform = new TranslateTransform(-1, 0) });
        w.Children.Add(new Border { BorderBrush = Br(CyanC, .5), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(3), RenderTransform = new TranslateTransform(1, 0) });
        w.Children.Add(new Border { CornerRadius = new CornerRadius(3), BorderBrush = Br(0x000000, .9), BorderThickness = new Thickness(1), Background = VhsRows.Vertical((Rgb(0x02090B, .97), 0), (Rgb(0x041216, .97), 1)) });
        w.Children.Add(new Rectangle { Fill = Lines(Rgb(VfdC, .045), 2, 1), RadiusX = 3, RadiusY = 3, Margin = new Thickness(1) });
        w.Children.Add(new Rectangle { Margin = new Thickness(1), RadiusX = 3, RadiusY = 3,
            Fill = new RadialGradientBrush { Center = glow, GradientOrigin = glow, RadiusX = .4, RadiusY = 1, GradientStops = { new GradientStop(Rgb(VfdC, .15), 0), new GradientStop(Rgb(VfdC, 0), 1) } } });
        w.Children.Add(new Border { Height = 1, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(4, 1, 4, 0), Background = Br(0xFFFFFF, .14) });
        return w;
    }

    /// <summary>The fight's head: the OSD line (the tape's state, the channel) over the boss and its clock; Deck's in a display window.</summary>
    FrameworkElement Head()
    {
        var sp = new StackPanel();
        var osd = new Grid();
        osd.ColumnDefinitions.Add(new ColumnDefinition()); osd.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        osd.Children.Add(_state);
        _chText.Foreground = Deck ? Br(VfdC, .6) : Dim;
        Grid.SetColumn(_chText, 1); osd.Children.Add(_chText);
        sp.Children.Add(osd);
        var title = new Grid { Margin = new Thickness(0, Deck ? 3 : 1, 0, 0) };
        title.ColumnDefinitions.Add(new ColumnDefinition()); title.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        title.Children.Add(_title);
        _clock.Margin = new Thickness(12, 0, 0, 0);
        Grid.SetColumn(_clock, 1); title.Children.Add(_clock);
        sp.Children.Add(title);
        if (!Deck) return sp;
        var window = Display(new Thickness(0), new Point(.86, .4));
        var host = new Grid();
        host.Children.Add(window);
        sp.Margin = new Thickness(10, 6, 10, 7);
        host.Children.Add(sp);
        return host;
    }

    /// <summary>The HP: a thick level (Deck's in the display's segments) with ticks at the thirds standing out of it, "47%  12.84M LEFT" at its end.</summary>
    void BuildHp()
    {
        double h = Deck ? 8 : 7;
        _hpRow.ColumnDefinitions.Add(new ColumnDefinition()); _hpRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        _hpTrack.Height = h;
        _hpTrack.Children.Add(new Border { Background = Br(0xFFFFFF, Deck ? .1 : .14) });
        _hpTrack.Children.Add(_hpFill);
        _hpTrack.SizeChanged += (_, _) => Share(_hpTrack, _hpFill, _hpShare);
        _hpTrack.Children.Add(new Border { Height = 1, VerticalAlignment = VerticalAlignment.Top, Background = Br(0xFFFFFF, .3), IsHitTestVisible = false });
        if (Deck) _hpTrack.Children.Add(new Rectangle { Fill = Segments(Rgb(0x000000, .85), 4, 1.2), IsHitTestVisible = false });
        var cell = new Grid { Height = h + 6, VerticalAlignment = VerticalAlignment.Center };
        cell.Children.Add(_hpTrack);
        cell.Children.Add(Notches(Deck ? Br(VfdC) : White, 1.6));
        _hpRow.Children.Add(cell);
        _hpText.Margin = new Thickness(10, 0, 0, 0); Grid.SetColumn(_hpText, 1); _hpRow.Children.Add(_hpText);
    }

    static Border PlayTag() => new()
    {
        Background = Br(GreenC), Padding = new Thickness(5, 0, 5, 0), Visibility = Visibility.Hidden, HorizontalAlignment = HorizontalAlignment.Stretch,
        Child = new TextBlock { Text = L.T("▶ PLAY"), FontFamily = VhsFace, FontSize = 11.5, FontWeight = FontWeights.Bold, Foreground = Br(0x000000), VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center },
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
    public override double Corner => Deck ? 4 : 3;
    public override FrameworkElement? FightFrame => _cam.Root.Visibility == Visibility.Visible ? _cam.Root : null;

    public override void SetOpacity(double opacity)
    {
        double o = Math.Clamp(opacity, 0.3, 1);
        _deckBack.Opacity = Math.Min(1, o * 1.05);
        _camBack.Opacity = Math.Min(1, o * 1.15);
    }

    protected override string LineSig(Figures f) => $"{base.LineSig(f)}|{Values.GetValueOrDefault("time")}|{Values.GetValueOrDefault("rare")}{Values.GetValueOrDefault("rareId")}|{PickStrip.Sig(Values, Strip)}|{FightWidth}|{RunBuddy.Sig(Values)}";

    /// <summary>The pickup strip in this tape: the tiles a size under the hour, the rule the glass's hairline (Deck's the display's cyan), the star the tape's yellow.</summary>
    PickStrip.Look Strip => new(24, 5, true, Deck ? Br(VfdC, .4) : Br(0xFFFFFF, .3), Yellow);

    string _figSig = "";

    protected override void DrawLine(Figures f)
    {
        // the tape row moves with the clock (the counter, the dot's blink): the rest only when the figures change
        var (run, clock, odd) = RunClock(Values);
        // in a dungeon run the tape records the run: its state, the dungeon and its count, its clock and splits (Ui/Themes/Glitch/HudVhs.Run.cs)
        var rs = RunOf(Values, KinahHidden);
        ShowRun(rs != null);
        if (rs != null) RunTape(rs);
        else
        {
            // the tape records while the session counts (a run or not); nothing counted yet, it stands by
            run = run || Values.GetValueOrDefault("run") == "1" || f.Counted;
            _recWord.Text = run ? L.T("REC") : L.T("STBY");
            _recWord.Foreground = run ? Red : _label;
            _dot.Visibility = run ? Visibility.Visible : Visibility.Collapsed;
            _dot.Opacity = odd ? 1 : .3;
            _tc.Text = "SP " + Timecode(clock);
        }
        var sig = $"{f.Sig}|{f.ToBest}|{KinahHidden}|{Values.GetValueOrDefault("rare")}{Values.GetValueOrDefault("rareId")}|{PickStrip.Sig(Values, Strip)}|{FightWidth}|{RunBuddy.Sig(Values)}";
        if (sig == _figSig) return;
        _figSig = sig;
        if (rs != null) { DrawRun(rs); Note(f); return; }

        // the hour, big and crisp, and your best
        _figRow.Children.Clear(); _figRow.ColumnDefinitions.Clear(); _figRow.RowDefinitions.Clear();
        _figRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); _figRow.ColumnDefinitions.Add(new ColumnDefinition()); _figRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var left = new StackPanel { Orientation = Orientation.Horizontal };
        if (f.Abyss && f.Counted && f.Icon.Length > 0) { var m = AbyssMark(f.Icon, 30); m.Margin = new Thickness(0, 0, 9, 0); left.Children.Add(m); }
        if (!f.Counted || f.Rate.Length == 0)
            left.Children.Add(Txt(Waiting(f.Abyss).ToUpperInvariant(), 13, _label, Sans, FontWeights.SemiBold));
        else if (f.Warm)
            left.Children.Add(Txt(f.Rate.ToUpperInvariant(), 18, Deck ? Br(VfdC, .45) : Faded, Sans));
        else
        {
            left.Children.Add(Txt(f.Rate, 28, _ink, Sans));
            var u = Txt(f.Unit, 13, _label, Sans, FontWeights.SemiBold); u.VerticalAlignment = VerticalAlignment.Bottom; u.Margin = new Thickness(4, 0, 0, 5);
            left.Children.Add(u);
        }
        _figRow.Children.Add(left);
        StackPanel? best = null;
        if (f.Share >= 0 && !f.Warm && f.Counted && f.Rate.Length > 0)
        {
            best = BestLevel(f.Share, f.Beat);
            best.Margin = new Thickness(16, 0, 0, 0);
            Grid.SetColumn(best, 2); _figRow.Children.Add(best);
        }
        // the best drop and the last three in the room beside the hour, against the battery (or the panel's edge, under the
        // counter), else on their own line under it (always whole, a fight or a rare drop's freeze-frame up or not); the Abyss
        // and the idle line keep the row as it was
        if (!f.Abyss && f.Counted && f.Rate.Length > 0)
        {
            double room = FightWidth - _deck.Body.Margin.Left - _deck.Body.Margin.Right - Wide(left) - (best != null ? Wide(best) : 0) - 14;
            StripInRow(_figRow, Values, Strip, room);
        }

        // the session (in the Abyss, the points)
        _sess.Inlines.Clear();
        _sessRow.Visibility = f.Counted && f.Total.Length > 0 || _timer.Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        if (f.Counted && f.Total.Length > 0)
        {
            _sess.Inlines.Add(Rn(f.Abyss ? f.TotalWord + "  " : f.TotalWord.ToUpper(System.Globalization.CultureInfo.CurrentCulture) + "  ", _label, 10, FontWeights.SemiBold));
            _sess.Inlines.Add(Rn(f.Total, _text, 14, FontWeights.Bold));
        }

        // the Abyss's kills, deaths and Kinah, each figure in its colour
        _stats.Inlines.Clear();
        foreach (var (fig, word, tone) in f.Stats)
        {
            if (_stats.Inlines.Count > 0) _stats.Inlines.Add(Rn("  ·  ", Faded));
            _stats.Inlines.Add(Rn(fig, ReferenceEquals(tone, Ink) ? _text : tone, 12, FontWeights.Bold));
            _stats.Inlines.Add(Rn(" " + word, _label));
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
            var name = Txt("", 12.5, Frozen(c), Sans, trim: true);
            name.Inlines.Add(Rn(Caps(r.Name), Frozen(c)));
            Grid.SetColumn(name, 2); _rare.Children.Add(name);
            _rare.Visibility = Visibility.Visible;
        }
        else _rare.Visibility = Visibility.Collapsed;

        Note(f);
    }

    /// <summary>The note under the line ("! ..."), in its tone.</summary>
    void Note(Figures f)
    {
        _note.Text = f.Note.Length > 0 ? "! " + f.Note : "";
        _note.Foreground = f.NoteTone;
        _note.Visibility = f.Note.Length > 0 && !KinahHidden ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>Your best as a camcorder's battery level: "BEST 91%" over ten cells; beaten, "★ NEW BEST" with every cell lit yellow.</summary>
    StackPanel BestLevel(double share, bool beat)
    {
        var sp = new StackPanel { VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Right };
        var t = Txt("", 11.5, _text, Sans); t.HorizontalAlignment = HorizontalAlignment.Right;
        if (beat) t.Inlines.Add(Rn(L.T("★ NEW BEST"), Yellow));
        else { t.Inlines.Add(Rn(L.T("BEST") + "  ", _label, 9.5, FontWeights.SemiBold)); t.Inlines.Add(Rn($"{Math.Round(Math.Clamp(share, 0, 1) * 100):0}%", _text)); }
        sp.Children.Add(t);
        var cells = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 4, 0, 0) };
        int lit = beat ? 10 : (int)Math.Round(Math.Clamp(share, 0, 1) * 10);
        var on = beat ? Yellow : _ink;
        // the battery's body: ten cells, then its nub
        for (int i = 0; i < 10; i++)
            cells.Children.Add(new Border { Width = 5, Height = 8, Margin = new Thickness(0, 0, 1.5, 0), Background = i < lit ? on : Br(0xFFFFFF, .16) });
        cells.Children.Add(new Border { Width = 2, Height = 4, Background = Br(0xFFFFFF, .45), VerticalAlignment = VerticalAlignment.Center });
        sp.Children.Add(cells);
        return sp;
    }

    protected override void DrawTimers(IReadOnlyList<TimerBoard.Item> due, DateTime now)
    {
        var next = due.FirstOrDefault();
        SetTimer(_timer, KinahHidden ? null : next, now, _text);
        SetTimer(_camTimer, KinahHidden ? next : null, now, _text);
        _camTimerCell.Visibility = KinahHidden && next != null ? Visibility.Visible : Visibility.Collapsed;
        // in a run the line under the clock takes the room the timer leaves it
        if (_runOn && RunOf(Values, KinahHidden) is { } rs) RunSub(rs);
        else if (Figs is { } f) _sessRow.Visibility = f.Counted && f.Total.Length > 0 || _timer.Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    static void SetTimer(TextBlock t, TimerBoard.Item? i, DateTime now, Brush plain)
    {
        t.Inlines.Clear();
        if (i == null) { t.Text = ""; return; }
        var (text, tone) = TimerWords(i, now);
        var (name, rest) = TimerParts(i, text);
        var b = tone == TimerBadge.Tone.On ? Green : tone == TimerBadge.Tone.Soon ? Yellow : plain;
        t.Inlines.Add(Rn(tone == TimerBadge.Tone.On ? "▶ " : "◷ ", b));
        if (name.Length > 0) t.Inlines.Add(Rn(name + " ", b));
        t.Inlines.Add(Rn(rest, b));
    }

    protected override void KinahShown(bool shown)
    {
        _deck.Root.Visibility = shown ? Visibility.Visible : Visibility.Collapsed;
        _cam.Root.Margin = new Thickness(0, shown ? 4 : 0, 0, 0);
    }

    double Side => Deck ? 24 : 30;

    /// <summary>The tape's state in the OSD line: "● REC" (Deck: "▶ PLAY") while the fight runs, "■ STOP  KILLED" after it, "‖ STBY" before it.</summary>
    void SetState(Encounter e)
    {
        _state.Children.Clear();
        if (e.Waiting) { _state.Children.Add(Txt("‖ " + L.T("STBY"), 10.5, _label, Face)); return; }
        if (e.Live)
        {
            if (Deck) { _state.Children.Add(Txt(L.T("▶ PLAY"), 10.5, Br(VfdC), Face)); return; }
            _state.Children.Add(new System.Windows.Shapes.Ellipse { Width = 8, Height = 8, Fill = Red, Margin = new Thickness(0, 0, 6, 0), VerticalAlignment = VerticalAlignment.Center });
            _state.Children.Add(Txt(L.T("REC"), 10.5, Red, Face));
            return;
        }
        _state.Children.Add(Txt("■ " + L.T("STOP"), 10.5, Deck ? Br(VfdC) : White, Face));
        var outcome = OutcomeText(e);
        if (outcome.Length > 0)
            _state.Children.Add(Txt("   " + outcome.ToUpper(System.Globalization.CultureInfo.CurrentCulture), 10.5,
                e.Outcome == "wipe" ? Br(0xFF6A5C) : e.Outcome is "kill" or "cleared" ? Br(MintTextC) : Dim, Face));
    }

    protected override void DrawFight(Encounter? e, DateTime now)
    {
        if (e == null) { _cam.Root.Visibility = Visibility.Collapsed; _rowsHost.Children.Clear(); _foot.Content = null; _title.Text = ""; _hpShare = -1; return; }
        _cam.Root.Width = FightWidth;
        _channel = $"CH {1 + (int)(Hash(e.Title) * 12):00}";
        _chText.Text = "SP  " + _channel;
        _title.Text = e.Title;
        SetState(e);
        _rowsHost.Children.Clear(); _foot.Content = null; _rowsHost.Visibility = Visibility.Collapsed; _foot.Visibility = Visibility.Collapsed;
        // Viewfinder before the first hit: each row one line (the CP after its figure, no bar under it), so a shorter pitch
        if (!Deck && Meter is { } rows) rows.Pitch = e.Waiting ? VhsRows.ViewReadyPitch : VhsRows.ViewPitch;
        if (e.Waiting)
        {
            _clock.Text = L.T("READY").ToUpper(System.Globalization.CultureInfo.CurrentCulture); _clock.Foreground = _label; _clock.FontSize = 13;
            _hpRow.Visibility = Visibility.Collapsed;
            _ready.Text = e.ReadyText; _ready.Visibility = Visibility.Visible;
            if (PartyBeforeHit(e)) { PutRows(_rowsHost, e, now, FightWidth - Side); _rowsHost.Visibility = Visibility.Visible; }
            _cam.Root.Visibility = Visibility.Visible;
            return;
        }
        _ready.Visibility = Visibility.Collapsed;
        _clock.Text = Clock(e.Duration(now)); _clock.FontSize = Deck ? 20 : 19;
        _clock.Foreground = Deck ? Br(VfdC) : e.Live ? Yellow : White;
        if (HpOf(e) is double hp)
        {
            _hpShare = hp; Share(_hpTrack, _hpFill, hp);
            _hpFill.Background = hp < .34 ? Br(0xFF2020) : Deck ? Br(0xFF5A3C) : Red;
            HpGhost(_hpTrack, e, hp, now, 0);
            _hpText.Inlines.Clear();
            _hpText.Inlines.Add(Rn($"{Math.Round(hp * 100):0}%", hp < .34 ? Br(0xFF8A7C) : _text, 12.5, FontWeights.Bold));
            _hpText.Inlines.Add(Rn("  " + HpLeftText(e).ToUpper(System.Globalization.CultureInfo.CurrentCulture), _label, 10, FontWeights.SemiBold));
            _hpRow.Visibility = Visibility.Visible;
        }
        else _hpRow.Visibility = Visibility.Collapsed;
        PutRows(_rowsHost, e, now, FightWidth - Side);
        _rowsHost.Visibility = Visibility.Visible;
        if (e.HasDamage) _foot.Content = VhsRows.Foot(e, now, Deck);
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
        RunHeadAside(1900);   // in a run the dungeon's name steps aside for the mark
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
        var osd = Split(_channel, 22, Face, FontWeights.Bold, Green, Rgb(RedC, .6), Rgb(CyanC, .6), 1.4);
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
            if (Meter?.Members.FirstOrDefault(m => m.TopDps) is { } top && Meter.RowOf(top.Id) is VhsRows.TapeRow row) row.Glint(2200);
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
