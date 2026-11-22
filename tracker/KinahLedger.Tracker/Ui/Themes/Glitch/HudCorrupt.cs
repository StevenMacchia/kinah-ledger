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
/// Corrupted: a broken signal. A near-black panel cut into horizontal slices, one or two of them sitting a few px
/// sideways, with a magenta ghost frame on its left and a cyan one on its right (the frame breaks, never the digits).
/// Consolas; the hour white with fixed magenta/cyan copies behind it; your best in a white block; the session in lilac;
/// the next timer as a block ("SHUGO_ENTER 2:41" green). A fight sits in the same broken frame: the HP a magenta bar
/// with cyan notches at the thirds, the rows on the dark ground, the foot.
/// Moments: a pickup tears a slice of the frame sideways and back and "+7.5K" flickers up in a glitch block; a rare drop
/// tears in a "DATA_FOUND" banner with its tile; your best passed flashes "NEW_BEST.EXE"; an entry opening smears the
/// timer's block (a datamosh); a fight boots the panel out of corrupted blocks; a kill stamps "TARGET.DELETED", a wipe
/// "FATAL ERROR" with the frame torn apart.
/// </summary>
sealed partial class HudCorrupt : HudThemeLook, IThemeCard
{
    static readonly FontFamily Face = CodeFace;
    const uint MagC = 0xFF2B6A, CyanC = 0x2BE6FF, LilacC = 0xC9B8FF, GroundC = 0x0A0A12, GreenC = 0x3DFF8C, GoldC2 = 0xFFD24A, RedC2 = 0xFF3B3B;
    static readonly Brush White = Br(0xFFFFFF), Lilac = Br(LilacC), LilacDim = Br(LilacC, .62), Mag = Br(MagC), Cyan = Br(CyanC), Black = Br(0x000000), Gold2 = Br(GoldC2);
    static readonly Color SplitL = Rgb(MagC, .9), SplitR = Rgb(CyanC, .9);

    /// <summary>The frame's slices (top, bottom as shares of its height) and how far each sits sideways.</summary>
    static readonly (double Y0, double Y1, double Dx)[] DeckSlices = { (0, .2, 0), (.2, .29, 5), (.29, .63, 0), (.63, .7, -4), (.7, 1, 0) };
    static readonly (double Y0, double Y1, double Dx)[] CamSlices = { (0, .1, 0), (.1, .14, -4), (.14, .55, 0), (.55, .58, 6), (.58, 1, 0) };

    readonly Grid _root = new() { Margin = new Thickness(14, 3, 14, 3), HorizontalAlignment = HorizontalAlignment.Left };
    readonly StackPanel _stack = new();
    readonly GlitchPanel _deck = new(new Thickness(12, 7, 12, 8)), _cam = new(new Thickness(12, 8, 12, 8));
    readonly SlicedGround _deckGround = new(DeckSlices), _camGround = new(CamSlices) { Alpha = .93 };

    readonly TextBlock _meta = Txt("", 10, LilacDim, Face);
    readonly Grid _pickBlock = new() { HorizontalAlignment = HorizontalAlignment.Right, Visibility = Visibility.Hidden };
    readonly TextBlock _pickText = Txt("", 10.5, Black, Face);
    readonly Grid _figRow = new() { Margin = new Thickness(0, 3, 0, 0) };
    readonly Grid _sessRow = new() { Margin = new Thickness(0, 4, 0, 0) };
    readonly TextBlock _sess = Txt("", 14, Lilac, Face);
    readonly Grid _timerCell = new() { HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(14, 0, 0, 0) };
    readonly Border _timerBlock = new() { Padding = new Thickness(5, 1, 5, 1), Visibility = Visibility.Collapsed };
    readonly TextBlock _timer = Txt("", 11, Black, Face);
    readonly Canvas _smear = new() { IsHitTestVisible = false };
    readonly TextBlock _stats = Txt("", 11, LilacDim, Face);
    readonly Grid _rare = new() { Margin = new Thickness(0, 6, 0, 0), Visibility = Visibility.Collapsed };
    readonly Grid _found = GlitchBlock(L.T("DATA_FOUND"), Mag, White, 10.5);
    FrameworkElement? _rareTile;
    Grid? _bestBlock;
    readonly TextBlock _note = Txt("", 11, LilacDim, Face, trim: true);

    readonly TextBlock _title = Txt("", 13.5, White, Face, trim: true), _clock = Txt("", 15, Gold2, Face), _outcome = Txt("", 12, White, Face);
    readonly Grid _camTimerCell = new() { HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 5, 0, 0), Visibility = Visibility.Collapsed };
    readonly Border _camTimerBlock = new() { Padding = new Thickness(5, 1, 5, 1) };
    readonly TextBlock _camTimer = Txt("", 11, Black, Face);
    readonly Canvas _camSmear = new() { IsHitTestVisible = false };
    readonly Grid _hpRow = new() { Margin = new Thickness(0, 7, 0, 2), Visibility = Visibility.Collapsed };
    readonly Grid _hpTrack = new() { Height = 7, VerticalAlignment = VerticalAlignment.Center };
    readonly Border _hpFill = new() { HorizontalAlignment = HorizontalAlignment.Left };
    readonly TextBlock _hpText = Txt("", 11, White, Face);
    readonly TextBlock _ready = Txt("", 12, LilacDim, Face, trim: true);
    readonly StackPanel _rowsHost = new() { Margin = new Thickness(0, 7, 0, 0) };
    readonly ContentControl _foot = new() { Focusable = false };
    double _hpShare = -1;

    public HudCorrupt() : base(false, CodeFace)
    {
        // the meter's rows: torn data rows (Ui/Themes/Glitch/CorruptRows.cs)
        UseRows(CorruptRows.Pitch, _ => new CorruptRows.Row(), CorruptRows.GapHeight, CorruptRows.Gap);
        _deck.Ground.Children.Add(_deckGround);
        _cam.Ground.Children.Add(_camGround);
        foreach (var p in new[] { _deck, _cam }) p.Over.Children.Add(new Rectangle { Fill = Lines(Rgb(0xFFFFFF, .03), 2, 1) });
        TextElement.SetFontFamily(_cam.Body, Face);
        _cam.Root.Width = FightWidth;
        _cam.Root.Visibility = Visibility.Collapsed;

        // the meta line: the run clock as a broken stream's header, and the pickup's glitch block on its right
        var meta = new Grid();
        meta.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); meta.ColumnDefinitions.Add(new ColumnDefinition());
        var tick = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) };
        foreach (var (w, b) in new[] { (7.0, Mag), (3.0, Cyan), (2.0, White), (5.0, Br(LilacC, .5)) })
            tick.Children.Add(new Border { Width = w, Height = 7, Background = b, Margin = new Thickness(0, 0, 1.5, 0) });
        meta.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Children = { tick, _meta } });
        _pickBlock.Margin = new Thickness(14, 0, 0, 0);
        _pickBlock.Children.Add(new Border { Background = Mag, RenderTransform = new TranslateTransform(-2, 1) });
        _pickBlock.Children.Add(new Border { Background = Cyan, RenderTransform = new TranslateTransform(2, -1) });
        _pickBlock.Children.Add(new Border { Background = White, Padding = new Thickness(4, 0, 4, 0), Child = _pickText });
        BuildRun(meta);   // a dungeon run's count, on the meta line's right (Ui/Themes/Glitch/HudCorrupt.Run.cs)
        Grid.SetColumn(_pickBlock, 1); meta.Children.Add(_pickBlock);
        _deck.Body.Children.Add(meta);
        _deck.Body.Children.Add(_figRow);
        _sessRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); _sessRow.ColumnDefinitions.Add(new ColumnDefinition());
        _sessRow.Children.Add(_sess);
        _timerBlock.Child = _timer;
        _timerCell.Children.Add(_smear); _timerCell.Children.Add(_timerBlock);
        Grid.SetColumn(_timerCell, 1); _sessRow.Children.Add(_timerCell);
        _deck.Body.Children.Add(_sessRow);
        _stats.Margin = new Thickness(0, 4, 0, 0); _stats.Visibility = Visibility.Collapsed;
        _deck.Body.Children.Add(_stats);
        _rare.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); _rare.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); _rare.ColumnDefinitions.Add(new ColumnDefinition());
        _found.VerticalAlignment = VerticalAlignment.Center;
        _rare.Children.Add(_found);
        _deck.Body.Children.Add(_rare);
        _note.Margin = new Thickness(0, 5, 0, 0); _note.Visibility = Visibility.Collapsed; _note.MaxWidth = 380; _note.HorizontalAlignment = HorizontalAlignment.Left;
        _deck.Body.Children.Add(_note);

        var head = new Grid();
        head.ColumnDefinitions.Add(new ColumnDefinition()); head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var tl = new StackPanel { Orientation = Orientation.Horizontal };
        var mark = Txt("▌", 13.5, Mag, Face); mark.Margin = new Thickness(-2, 0, 4, 0);
        head.Children.Add(new DockPanel { LastChildFill = true, Children = { mark, _title } });
        var right = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(10, 0, 0, 0), Children = { _outcome, _clock } };
        _outcome.Margin = new Thickness(0, 0, 9, 0);
        Grid.SetColumn(right, 1); head.Children.Add(right);
        _cam.Body.Children.Add(head);
        _camTimerBlock.Child = _camTimer;
        _camTimerCell.Children.Add(_camSmear); _camTimerCell.Children.Add(_camTimerBlock);
        _cam.Body.Children.Add(_camTimerCell);
        _hpRow.ColumnDefinitions.Add(new ColumnDefinition()); _hpRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        _hpTrack.Children.Add(new Border { Background = Br(0x5A1030, .75) });
        var fill = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(0, 1) };
        fill.GradientStops.Add(new GradientStop(Rgb(0xFF6A96), 0)); fill.GradientStops.Add(new GradientStop(Rgb(MagC), .5)); fill.GradientStops.Add(new GradientStop(Rgb(0xC0124A), 1)); fill.Freeze();
        _hpFill.Background = fill; _hpTrack.Children.Add(_hpFill);
        _hpTrack.SizeChanged += (_, _) => Share(_hpTrack, _hpFill, _hpShare);
        var hpCell = new Grid { Height = 13, VerticalAlignment = VerticalAlignment.Center };
        hpCell.Children.Add(_hpTrack);
        var notches = new Grid { IsHitTestVisible = false };
        for (int i = 0; i < 3; i++) notches.ColumnDefinitions.Add(new ColumnDefinition());
        for (int i = 0; i < 2; i++) { var t = new Border { Width = 2, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 0, -1, 0), Background = Cyan }; Grid.SetColumn(t, i); notches.Children.Add(t); }
        hpCell.Children.Add(notches);
        _hpRow.Children.Add(hpCell);
        _hpText.Margin = new Thickness(10, 0, 0, 0); Grid.SetColumn(_hpText, 1); _hpRow.Children.Add(_hpText);
        _cam.Body.Children.Add(_hpRow);
        _ready.Margin = new Thickness(0, 7, 0, 1); _ready.Visibility = Visibility.Collapsed;
        _cam.Body.Children.Add(_ready);
        _cam.Body.Children.Add(_rowsHost);
        _cam.Body.Children.Add(_foot);

        _cam.Root.Margin = new Thickness(0, 6, 0, 0);
        _stack.Children.Add(_deck.Root); _stack.Children.Add(_cam.Root);
        _root.Children.Add(_stack);
    }

    /// <summary>A block of colour with its words, a magenta and a cyan copy of the block fixed a hair off behind it.</summary>
    static Grid GlitchBlock(string text, Brush bg, Brush fg, double size)
    {
        var g = new Grid();
        g.Children.Add(new Border { Background = Br(MagC, .85), RenderTransform = new TranslateTransform(-2, 1) });
        g.Children.Add(new Border { Background = Br(CyanC, .85), RenderTransform = new TranslateTransform(2, -1) });
        g.Children.Add(new Border { Background = bg, Padding = new Thickness(5, 0, 5, 0), Child = Txt(text, size, fg, Face) });
        return g;
    }

    public override FrameworkElement Root => _root;

    /// <summary>The member's skills card in this look (Ui/Themes/Glitch/GlitchCards.cs).</summary>
    public FrameworkElement DrawCard(MemberCard c) => GlitchCards.Corrupt(c, Moves);
    public override Thickness Inset => _root.Margin;
    public override double Corner => 0;
    public override FrameworkElement? FightFrame => _cam.Root.Visibility == Visibility.Visible ? _cam.Root : null;

    public override void SetOpacity(double opacity)
    {
        double o = Math.Clamp(opacity, 0.3, 1);
        _deckGround.Alpha = Math.Min(1, o * 1.0); _deckGround.InvalidateVisual();
        _camGround.Alpha = Math.Min(1, o * 1.03); _camGround.InvalidateVisual();
    }

    protected override string LineSig(Figures f) => $"{base.LineSig(f)}|{Values.GetValueOrDefault("time")}|{Values.GetValueOrDefault("run")}|{Values.GetValueOrDefault("rare")}{Values.GetValueOrDefault("rareId")}|{PickStrip.Sig(Values, Strip)}|{FightWidth}|{RunBuddy.Sig(Values)}";

    /// <summary>The pickup strip in the broken frame: the rule the session's lilac, the star the block's gold.</summary>
    static readonly PickStrip.Look Strip = new(24, 5, true, Br(LilacC, .45), Br(GoldC2));

    string _figSig = "";

    protected override void DrawLine(Figures f)
    {
        var (run, clock, _) = RunClock(Values);
        run = run || Values.GetValueOrDefault("run") == "1";
        // in a dungeon run the stream carries the run: the dungeon and its count, its clock and splits (Ui/Themes/Glitch/HudCorrupt.Run.cs)
        var rs = RunOf(Values, KinahHidden);
        ShowRun(rs != null);
        if (rs != null) RunMeta(rs);
        // the stream's header: the run's clock, the session's as T+, nothing counted yet NO_SIGNAL
        else _meta.Text = !f.Counted ? L.T("NO_SIGNAL") : (run ? L.T("RUN_") : "T+") + (clock.Length > 0 ? clock : "0:00");
        var sig = $"{f.Sig}|{f.ToBest}|{KinahHidden}|{Values.GetValueOrDefault("rare")}{Values.GetValueOrDefault("rareId")}|{PickStrip.Sig(Values, Strip)}|{FightWidth}|{RunBuddy.Sig(Values)}";
        if (sig == _figSig) return;
        _figSig = sig;
        if (rs != null) { DrawRun(rs); Note(f); return; }

        _figRow.Children.Clear(); _figRow.ColumnDefinitions.Clear(); _figRow.RowDefinitions.Clear(); _bestBlock = null;
        _figRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); _figRow.ColumnDefinitions.Add(new ColumnDefinition()); _figRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var left = new StackPanel { Orientation = Orientation.Horizontal };
        if (f.Abyss && f.Counted && f.Icon.Length > 0) { var m = AbyssMark(f.Icon, 30); m.Margin = new Thickness(0, 0, 9, 0); left.Children.Add(m); }
        if (!f.Counted || f.Rate.Length == 0) left.Children.Add(Txt(Waiting(f.Abyss), 13, LilacDim, Face));
        else if (f.Warm) left.Children.Add(Txt(f.Rate, 19, LilacDim, Face));
        else
        {
            left.Children.Add(Split(f.Rate, 27, Face, FontWeights.Bold, White, SplitL, SplitR, 2.2));
            var u = Txt(f.Unit, 14, Lilac, Face); u.VerticalAlignment = VerticalAlignment.Bottom; u.Margin = new Thickness(3, 0, 0, 4);
            left.Children.Add(u);
        }
        _figRow.Children.Add(left);
        if (f.Share >= 0 && !f.Warm && f.Counted && f.Rate.Length > 0)
        {
            // your best: a white block; beaten, NEW_BEST.EXE
            _bestBlock = f.Beat ? GlitchBlock(L.T("★ NEW_BEST.EXE"), Br(0xFFFFFF), Black, 10.5) : GlitchBlock($"{L.T("BEST")} {Math.Round(Math.Clamp(f.Share, 0, 1) * 100):0}%", Br(0xE8E2F5), Black, 10.5);
            _bestBlock.Margin = new Thickness(16, 0, 2, 0); _bestBlock.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(_bestBlock, 2); _figRow.Children.Add(_bestBlock);
        }
        // the best drop and the last three in the room beside the hour, against the best block, else on their own line under it
        // (always whole, a fight or a rare drop's DATA_FOUND up or not); the Abyss and the idle line keep the row as it was
        if (!f.Abyss && f.Counted && f.Rate.Length > 0)
        {
            double room = FightWidth - _deck.Body.Margin.Left - _deck.Body.Margin.Right - Wide(left) - (_bestBlock != null ? Wide(_bestBlock) : 0) - 14;
            StripInRow(_figRow, Values, Strip, room);
        }

        _sess.Inlines.Clear();
        if (f.Counted && f.Total.Length > 0)
        {
            _sess.Inlines.Add(Rn(f.Total, Br(0xEDE6FF)));
            _sess.Inlines.Add(Rn(" " + f.TotalWord, Br(LilacC, .82), 12));
        }
        _sessRow.Visibility = _sess.Inlines.Count > 0 || _timerBlock.Visibility == Visibility.Visible ? Visibility.Visible : Visibility.Collapsed;

        _stats.Inlines.Clear();
        foreach (var (fig, word, tone) in f.Stats)
        {
            if (_stats.Inlines.Count > 0) _stats.Inlines.Add(Rn("  //  ", Br(MagC, .7)));
            _stats.Inlines.Add(Rn(fig, ReferenceEquals(tone, Ink) ? White : tone, 12));
            _stats.Inlines.Add(Rn(" " + word, LilacDim));
        }
        _stats.Visibility = _stats.Inlines.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

        if (_rareTile != null) { _rare.Children.Remove(_rareTile); _rareTile = null; }
        for (int i = _rare.Children.Count - 1; i >= 1; i--) _rare.Children.RemoveAt(i);
        if (Rare(Values) is { } r)
        {
            var c = ColorOf(r.Color, r.Grade);
            _rareTile = Tile(r, 24); _rareTile.Margin = new Thickness(10, 0, 8, 0);
            Grid.SetColumn(_rareTile, 1); _rare.Children.Add(_rareTile);
            var name = Txt(r.Name, 13, Frozen(c), Face, trim: true);
            Grid.SetColumn(name, 2); _rare.Children.Add(name);
            _rare.Visibility = Visibility.Visible;
        }
        else _rare.Visibility = Visibility.Collapsed;

        Note(f);
    }

    /// <summary>The note under the line ("▲ ..."), in its tone.</summary>
    void Note(Figures f)
    {
        _note.Text = f.Note.Length > 0 ? "▲ " + f.Note : "";
        _note.Foreground = f.NoteTone;
        _note.Visibility = f.Note.Length > 0 && !KinahHidden ? Visibility.Visible : Visibility.Collapsed;
    }

    protected override void DrawTimers(IReadOnlyList<TimerBoard.Item> due, DateTime now)
    {
        var next = due.FirstOrDefault();
        SetTimer(_timerBlock, _timer, KinahHidden ? null : next, now);
        SetTimer(_camTimerBlock, _camTimer, KinahHidden ? next : null, now);
        _camTimerCell.Visibility = KinahHidden && next != null ? Visibility.Visible : Visibility.Collapsed;
        if (_runOn && RunOf(Values, KinahHidden) is { } rs) RunSub(rs);   // in a run the splits take the room the timer leaves them
        _sessRow.Visibility = _sess.Inlines.Count > 0 || _timerBlock.Visibility == Visibility.Visible ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>The timer as a block: green with dark words while its entry is open ("SHUGO_ENTER 2:41"), gold when soon, a lilac outline else.</summary>
    static void SetTimer(Border block, TextBlock t, TimerBoard.Item? i, DateTime now)
    {
        if (i == null) { block.Visibility = Visibility.Collapsed; t.Text = ""; return; }
        var (text, tone) = TimerWords(i, now);
        var (name, rest) = TimerParts(i, text);
        t.Text = (name.Length > 0 ? name + (tone == TimerBadge.Tone.On ? "_" : " ") : "") + rest;
        bool lit = tone != TimerBadge.Tone.Quiet;
        block.Background = tone == TimerBadge.Tone.On ? Br(GreenC) : tone == TimerBadge.Tone.Soon ? Br(GoldC2) : Br(GroundC, .6);
        block.BorderBrush = lit ? null : Br(LilacC, .55); block.BorderThickness = new Thickness(lit ? 0 : 1);
        t.Foreground = lit ? Black : Lilac;
        block.Visibility = Visibility.Visible;
    }

    protected override void KinahShown(bool shown)
    {
        _deck.Root.Visibility = shown ? Visibility.Visible : Visibility.Collapsed;
        _cam.Root.Margin = new Thickness(0, shown ? 6 : 0, 0, 0);
    }

    protected override void DrawFight(Encounter? e, DateTime now)
    {
        if (e == null) { _cam.Root.Visibility = Visibility.Collapsed; _rowsHost.Children.Clear(); _foot.Content = null; _title.Text = ""; _hpShare = -1; return; }
        _cam.Root.Width = FightWidth;
        _title.Text = e.Title;
        _rowsHost.Children.Clear(); _foot.Content = null; _rowsHost.Visibility = Visibility.Collapsed; _foot.Visibility = Visibility.Collapsed;
        if (e.Waiting)
        {
            _outcome.Visibility = Visibility.Collapsed;
            _clock.Text = L.T("READY") + "_"; _clock.Foreground = LilacDim; _clock.FontSize = 12;
            _hpRow.Visibility = Visibility.Collapsed;
            _ready.Text = e.ReadyText; _ready.Visibility = Visibility.Visible;
            if (PartyBeforeHit(e)) { PutRows(_rowsHost, e, now, FightWidth - 24); _rowsHost.Visibility = Visibility.Visible; }
            _cam.Root.Visibility = Visibility.Visible;
            return;
        }
        _ready.Visibility = Visibility.Collapsed;
        _clock.Text = Clock(e, now); _clock.Foreground = e.Live ? Gold2 : White; _clock.FontSize = 15;
        if (HpOf(e) is double hp)
        {
            _hpShare = hp; Share(_hpTrack, _hpFill, hp);
            HpGhost(_hpTrack, e, hp, now, 0);
            _hpText.Inlines.Clear();
            _hpText.Inlines.Add(Rn($"{Math.Round(hp * 100):0}%", hp < .34 ? Br(0xFF6A8E) : White, 12));
            _hpText.Inlines.Add(Rn(" · " + HpLeftText(e), Lilac));
            _hpRow.Visibility = Visibility.Visible;
        }
        else _hpRow.Visibility = Visibility.Collapsed;
        PutRows(_rowsHost, e, now, FightWidth - 24);
        _rowsHost.Visibility = Visibility.Visible;
        var outcome = OutcomeText(e);
        _outcome.Text = outcome.Length > 0 ? outcome.ToUpper(System.Globalization.CultureInfo.CurrentCulture) : "";
        _outcome.Foreground = e.Outcome == "wipe" ? Br(0xFF5A6A) : e.Outcome is "kill" or "cleared" ? Br(0x5CFFB0) : LilacDim;
        _outcome.Visibility = outcome.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        if (e.HasDamage) _foot.Content = CorruptRows.Foot(e, now);
        _foot.Visibility = _foot.Content != null ? Visibility.Visible : Visibility.Collapsed;
        _cam.Root.Visibility = Visibility.Visible;
    }

    // ---- the moments

    /// <summary>A slice of a frame torn sideways and back in jumps over <paramref name="ms"/> (the frame, never the figures).</summary>
    static void Tear(GlitchPanel p, SlicedGround g, int slice, double ms, double reach, double until = 1)
    {
        var holder = new Border();
        double[] jumps = { 1, -.8, .9, -.6, .7, -.5, .35, 0 };
        p.Run(holder, ms, t =>
        {
            if (t >= until || t >= 1) { g.Tear(slice, 0); return; }
            int k = (int)(t / until * jumps.Length);
            g.Tear(slice, reach * jumps[Math.Clamp(k, 0, jumps.Length - 1)]);
        });
    }

    /// <summary>Where <paramref name="el"/>'s top (plus <paramref name="dy"/>) sits in <paramref name="p"/> as a share of its height (<paramref name="fallback"/> before it is laid out).</summary>
    static double At(GlitchPanel p, FrameworkElement el, double fallback, double dy = 0)
    {
        try { return el.ActualHeight > 0 && p.H > 0 && el.IsDescendantOf(p.Root) ? Math.Clamp(el.TranslatePoint(new Point(0, dy), p.Root).Y / p.H, 0, 1) : fallback; }
        catch (InvalidOperationException) { return fallback; }
    }

    static readonly Color GroundCol = Rgb(GroundC), TearL = Rgb(MagC, .95), TearR = Rgb(CyanC, .95);

    protected override void Pickup(Pick p, bool held)
    {
        if (KinahHidden) return;
        Tear(_deck, _deckGround, Hash(p.Id) < .5 ? 2 : 4, 700, 10);
        // the hour's line itself jolts: its lower half torn a hair sideways for a few frames
        _deck.Tear(700, GroundCol, TearL, TearR, new TearBand(At(_deck, _figRow, .4, 14), 9, 9, .04, .2));
        // the tears run in the gaps under the hour's line and under the session's, never through a figure
        double Under(FrameworkElement el, double fallback)
        {
            try { return el.ActualHeight > 0 ? el.TranslatePoint(new Point(0, el.ActualHeight), _deck.Root).Y + 1 : fallback; }
            catch (InvalidOperationException) { return fallback; }
        }
        foreach (var (y, c, dx) in new[] { (Under(_figRow, _deck.H * .62), Rgb(MagC, .6), 14.0), (Under(_sessRow, _deck.H * .9), Rgb(CyanC, .5), -10.0) })
        {
            var strip = new Rectangle { Height = 2, VerticalAlignment = VerticalAlignment.Top, Fill = Frozen(c), Margin = new Thickness(0, Math.Round(y), 0, 0) };
            var st = new TranslateTransform(); strip.RenderTransform = st;
            _deck.Run(strip, 900, t => { int k = (int)(t * 12); st.X = (k % 3 - 1) * dx; strip.Opacity = t < .55 && k % 2 == 0 ? 1 : 0; }, under: true);
        }
        _pickText.Text = (p.Worth.Length > 0 ? p.Worth + " " : "") + p.Name;
        _pickBlock.Visibility = Visibility.Visible;
        RunCountAside(1700);   // in a run the count steps aside for the block
        var ghostM = (TranslateTransform)((Border)_pickBlock.Children[0]).RenderTransform;
        var ghostC = (TranslateTransform)((Border)_pickBlock.Children[1]).RenderTransform;
        Motion.Play(_pickBlock, 1700, t =>
        {
            // flickers up, its ghost blocks jumping, then settles and goes
            _pickBlock.Opacity = t < .1 ? (Steps(t, 30) * 30 % 2 == 0 ? 1 : .2) : t > .84 ? 1 - (t - .84) / .16 : 1;
            double j = t < .3 ? 4 * (1 - t / .3) : 0;
            int s = (int)(t * 20);
            ghostM.X = -2 - j * ((s % 3) - 1); ghostC.X = 2 + j * ((s % 2) * 2 - 1);
        }, () => { _pickBlock.Visibility = Visibility.Hidden; _pickBlock.Opacity = 1; ghostM.X = -2; ghostC.X = 2; });
    }

    protected override void RareDrop(Pick p, bool held)
    {
        if (KinahHidden) return;
        // the big one: the picture tears in three bands at once, flashed in the drop's grade, the frame torn twice
        var gc = ColorOf(p.Color, p.Grade);
        _deck.Flash(gc, .45, 1800, .03, .3);
        Tear(_deck, _deckGround, 3, 1100, 12);
        Tear(_deck, _deckGround, 1, 900, -8);
        _deck.Tear(1800, GroundCol, TearL, TearR, new TearBand(.12, 10, -14, .03, .14), new TearBand(.46, 14, 18, .06, .2), new TearBand(At(_deck, _rare, .8), 22, -10, .1, .24));
        var ghostM = (TranslateTransform)((Border)_found.Children[0]).RenderTransform;
        var ghostC = (TranslateTransform)((Border)_found.Children[1]).RenderTransform;
        var face = (Border)_found.Children[2];
        Motion.Play(_found, 1800, t =>
        {
            int s = (int)(t * 24);
            double j = t < .5 ? 7 * (1 - t / .5) : 0;
            ghostM.X = -2 - j * (s % 2 == 0 ? 1 : -.5); ghostC.X = 2 + j * (s % 3 == 0 ? 1 : -.4);
            face.Background = t < .4 && s % 3 == 1 ? Cyan : Mag;
        }, () => { ghostM.X = -2; ghostC.X = 2; face.Background = Mag; });
        if (_rareTile is { } tile)
        {
            var sc = new ScaleTransform(); tile.RenderTransformOrigin = new Point(.5, .5); tile.RenderTransform = sc;
            Motion.Play(tile, 560, t => sc.ScaleX = sc.ScaleY = Math.Max(0, Back(Math.Clamp((t - .1) / .9, 0, 1))), () => tile.ClearValue(UIElement.RenderTransformProperty));
        }
    }

    protected override void BestPassed(bool held)
    {
        if (KinahHidden || _bestBlock == null) return;
        Tear(_deck, _deckGround, 2, 900, 7);
        _deck.Tear(1600, GroundCol, TearL, TearR, new TearBand(.3, 12, 12, .04, .2), new TearBand(.62, 8, -9, .1, .22));
        // the stream's header steps aside while the bar is over it (never two words over each other)
        if (_meta.Parent is FrameworkElement mp && mp.Parent is FrameworkElement meta) Motion.Play(meta, 1600, t => meta.Opacity = t < .55 ? 0 : 1, () => meta.Opacity = 1);
        var bar = GlitchBlock(L.T("NEW_BEST.EXE") + "  ★  " + L.T("RECORD WRITTEN"), White, Black, 11.5);
        bar.VerticalAlignment = VerticalAlignment.Top; bar.Margin = new Thickness(6, 5, 6, 0);
        ((Border)bar.Children[2]).Padding = new Thickness(6, 1, 6, 1);
        ((TextBlock)((Border)bar.Children[2]).Child).HorizontalAlignment = HorizontalAlignment.Center;
        var bm = (TranslateTransform)((Border)bar.Children[0]).RenderTransform; var bc = (TranslateTransform)((Border)bar.Children[1]).RenderTransform;
        _deck.Run(bar, 1600, t =>
        {
            int s = (int)(t * 20);
            bar.Opacity = t < .03 ? 0 : t < .45 ? 1 : t < .55 ? (s % 2 == 0 ? .7 : .1) : 0;
            bm.X = -3 - (t < .2 ? 5 * (s % 2) : 0); bc.X = 3 + (t < .2 ? 5 * ((s + 1) % 2) : 0);
        });
        var b = _bestBlock;
        var ghostM = (TranslateTransform)((Border)b.Children[0]).RenderTransform;
        var ghostC = (TranslateTransform)((Border)b.Children[1]).RenderTransform;
        var face = (Border)b.Children[2];
        var words = (TextBlock)face.Child;
        Brush[] bgs = { White, Mag, Cyan, White, Black, White };
        Motion.Play(b, 1600, t =>
        {
            int s = (int)(t * 18);
            bool flick = t < .45;
            face.Background = flick ? bgs[s % bgs.Length] : White;
            words.Foreground = flick && ReferenceEquals(face.Background, Black) ? White : Black;
            double j = flick ? 6 : 0;
            ghostM.X = -2 - j * (s % 2); ghostC.X = 2 + j * ((s + 1) % 2);
        }, () => { face.Background = White; words.Foreground = Black; ghostM.X = -2; ghostC.X = 2; });
    }

    protected override void EntryOpened(TimerBoard.Item i, bool held)
    {
        // a datamosh: the block's colour drags off to its left in three strips that pull back into it
        var (cell, block, smear) = KinahHidden ? (_camTimerCell, _camTimerBlock, _camSmear) : (_timerCell, _timerBlock, _smear);
        smear.Children.Clear();
        var c = (block.Background as SolidColorBrush)?.Color ?? Rgb(GreenC);
        double h = block.ActualHeight > 0 ? block.ActualHeight : 16, w = block.ActualWidth > 0 ? block.ActualWidth : 120;
        bool right = KinahHidden;   // inside the fight the block sits on the left: it drags right
        var strips = new List<(Rectangle R, double Len)>();
        double[] lens = { 70, 120, 45 };
        for (int k = 0; k < 3; k++)
        {
            var r = new Rectangle { Height = Math.Ceiling(h / 3) + .5, Fill = new LinearGradientBrush(right ? Color.FromArgb(0xE0, c.R, c.G, c.B) : Color.FromArgb(0, c.R, c.G, c.B), right ? Color.FromArgb(0, c.R, c.G, c.B) : Color.FromArgb(0xE0, c.R, c.G, c.B), 0) };
            Canvas.SetTop(r, k * h / 3);
            smear.Children.Add(r);
            strips.Add((r, lens[k]));
        }
        Motion.Play(smear, 1100, t =>
        {
            double q = 1 - Motion.Out(Math.Clamp(t / .85, 0, 1));
            int s = (int)(t * 16);
            foreach (var (r, len) in strips)
            {
                double l = len * q * (s % 3 == 0 ? 1.15 : 1);
                r.Width = Math.Max(0, l);
                Canvas.SetLeft(r, right ? w : -l);
            }
        }, () => smear.Children.Clear());
        var bw = new Border { BorderBrush = White, BorderThickness = new Thickness(1.5) };
        cell.Children.Add(bw);
        Motion.Play(bw, 900, t => bw.Opacity = t < .5 && (int)(t * 12) % 2 == 0 ? 1 : 0, () => cell.Children.Remove(bw));
        // the block lands with a punch (in wide and flat, past its size, back), the line it sits on torn for a beat
        // (it grows from its outer edge, the session's figure keeping its room)
        var sc = new ScaleTransform(); block.RenderTransformOrigin = new Point(right ? 0 : 1, .5); block.RenderTransform = sc;
        Motion.Play(block, 1100, t =>
        {
            double k = Math.Clamp(t / .3, 0, 1);
            sc.ScaleX = t < .3 ? .3 + .7 * Back(k, 1.2) : 1; sc.ScaleY = t < .3 ? 1.7 - .7 * Back(k, 2.2) : 1;
        }, () => block.ClearValue(UIElement.RenderTransformProperty));
        var (panel, line) = KinahHidden ? (_cam, (FrameworkElement)_camTimerCell) : (_deck, _sessRow);
        panel.Tear(1100, GroundCol, TearL, TearR, new TearBand(At(panel, line, .8), 12, right ? 10 : -12, .04, .2));
    }

    protected override void FightStarted(Encounter e, bool held)
    {
        // the panel boots from a corrupted picture: blocks of noise and colour resolve away, a few at a time
        double w = _cam.W, h = _cam.H;
        var canvas = new Canvas();
        var r = new Random(Hash(e.Id).GetHashCode());
        var cover = new Rectangle { Width = w, Height = h, Fill = Br(GroundC) };
        canvas.Children.Add(cover);
        var blocks = new List<(Rectangle R, double Gone, double X)>();
        Brush[] fills = { Br(MagC, .45), Br(CyanC, .4), Br(0xFFFFFF, .3), Br(0x2A1A3A, .8), Br(MagC, .3), Br(CyanC, .25), NoiseBrush(.4) };
        for (int k = 0; k < 30; k++)
        {
            double bw = 14 + r.NextDouble() * w * .45, bh = 1 + r.NextDouble() * 4;
            var rect = new Rectangle { Width = bw, Height = bh, Fill = fills[r.Next(fills.Length)] };
            double x = r.NextDouble() * (w - bw / 2) - bw / 4, y = r.NextDouble() * h;
            Canvas.SetLeft(rect, x); Canvas.SetTop(rect, y);
            canvas.Children.Add(rect);
            blocks.Add((rect, .08 + r.NextDouble() * .4, x));
        }
        // as the picture locks, it tears in four bands that snap back one after another
        _cam.Tear(1000, GroundCol, TearL, TearR, new TearBand(.08, 16, -16, .04, .2), new TearBand(.3, 22, 20, .08, .26), new TearBand(.55, 12, -12, .12, .3), new TearBand(.78, 18, 14, .16, .34));
        _cam.Run(canvas, 1000, under: true, step: t =>
        {
            cover.Opacity = t < .06 ? .9 : t < .4 ? .9 * (1 - (t - .06) / .34) : 0;
            int s = (int)(t * 20);
            foreach (var (rect, gone, x) in blocks)
            {
                rect.Visibility = t < gone ? Visibility.Visible : Visibility.Collapsed;
                Canvas.SetLeft(rect, x + ((s + (int)(gone * 10)) % 3 - 1) * 6);
            }
        });
    }

    protected override void FightEnded(Encounter e, bool held)
    {
        bool wipe = e.Outcome == "wipe";
        var text = wipe ? L.T("FATAL ERROR") : e.Outcome is "kill" ? L.T("TARGET.DELETED") : OutcomeText(e).ToUpper(System.Globalization.CultureInfo.CurrentCulture);
        var banner = GlitchBlock(text, wipe ? Br(RedC2) : Black, White, 14);
        banner.VerticalAlignment = VerticalAlignment.Top; banner.HorizontalAlignment = HorizontalAlignment.Stretch; banner.Margin = new Thickness(8, 5, 8, 0);
        var face = (Border)banner.Children[2];
        face.Padding = new Thickness(6, 3, 6, 3);
        var words = Split(text, 14.5, Face, FontWeights.Bold, White, Rgb(MagC, .5), Rgb(CyanC, .5), 1);
        words.HorizontalAlignment = HorizontalAlignment.Center;
        var lb = Txt("▌", 15, wipe ? White : Mag, Face); var rb = Txt("▐", 15, wipe ? White : Cyan, Face);
        DockPanel.SetDock(lb, Dock.Left); DockPanel.SetDock(rb, Dock.Right);
        face.Child = new DockPanel { Children = { lb, rb, words } };
        if (!wipe) { face.BorderBrush = White; face.BorderThickness = new Thickness(0, 1, 0, 1); }
        ((Border)banner.Children[0]).RenderTransform = new TranslateTransform(-4, 2); ((Border)banner.Children[1]).RenderTransform = new TranslateTransform(4, -2);
        var ghostM = (TranslateTransform)((Border)banner.Children[0]).RenderTransform;
        var ghostC = (TranslateTransform)((Border)banner.Children[1]).RenderTransform;
        var shift = new TranslateTransform(); banner.RenderTransform = shift;
        _cam.Run(banner, 2200, t =>
        {
            int s = (int)(t * 22);
            double j = t < .35 ? 8 * (1 - t / .35) : wipe ? 3 : 0;
            ghostM.X = -4 - j * (s % 2); ghostC.X = 4 + j * ((s + 1) % 2);
            shift.X = t < .2 ? ((s % 3) - 1) * 10 * (1 - t / .2) : 0;
            banner.Opacity = t < .03 ? 0 : t < .5 ? 1 : t < .62 ? (Steps(t, 40) * 40 % 2 == 0 ? .6 : .15) : 0;
        });
        // the picture tears: a kill one clean cut through the winner's row; a wipe falls apart, band after band, for half a second
        double RowAt(int i) => Meter is { Members.Count: > 0 } mm && i < mm.Members.Count && mm.RowOf(mm.Members[i].Id)?.Root is FrameworkElement r ? At(_cam, r, .3 + .15 * i) : .3 + .15 * i;
        if (wipe)
            _cam.Tear(2200, GroundCol, Rgb(RedC2, .95), TearR, new TearBand(RowAt(0), 16, 22, .02, .12), new TearBand(RowAt(1) + .02, 20, -26, .06, .18),
                new TearBand(RowAt(2), 12, 18, .1, .22), new TearBand(.05, 14, -14, .14, .24), new TearBand(RowAt(3), 24, 30, .16, .26));
        else _cam.Tear(2200, GroundCol, TearL, TearR, new TearBand(RowAt(0), 30, 26, .03, .2));
        // the frame tears: a kill once, a wipe every slice, again and again
        Tear(_cam, _camGround, 1, 900, wipe ? 14 : 9);
        if (!wipe && Meter?.Members.FirstOrDefault(m => m.TopDps) is { } top && Meter.RowOf(top.Id) is CorruptRows.Row row) row.Glint(2200);
        if (wipe) { Tear(_cam, _camGround, 3, 2000, -12, .9); _cam.Flash(Rgb(RedC2), .3, 2200, .04, .3); }
        else _cam.Flash(Colors.White, .4, 2200, .03, .2);
    }
}

/// <summary>
/// The Corrupted frame's ground: horizontal slices of the dark panel, each a few px sideways or not, a magenta ghost edge
/// on each slice's left and a cyan one on its right (and over the top and under the foot). Drawn in one pass of rectangles;
/// a moment tears one slice further (<see cref="Tear"/>). It draws past its own bounds (the root keeps room for it).
/// </summary>
sealed class SlicedGround : FrameworkElement
{
    readonly (double Y0, double Y1, double Dx)[] _slices;
    readonly double[] _torn;
    public double Alpha = .9;
    /// <summary>
    /// The farthest a slice ever sits sideways, torn or not: with its ghost edge (<see cref="Ghost"/>) it stays inside the room
    /// the panel keeps round it (the look's Inset, 14 px), so a tear is never cut at the window's edge.
    /// </summary>
    public double Reach = 10;
    static readonly Color Ground = Color.FromRgb(0x0A, 0x0A, 0x12);
    static readonly Brush GhostL = HudKit.Frozen(Color.FromArgb(0xE6, 0xFF, 0x2B, 0x6A)), GhostR = HudKit.Frozen(Color.FromArgb(0xE6, 0x2B, 0xE6, 0xFF));
    const double Ghost = 3;

    public SlicedGround((double, double, double)[] slices) { _slices = slices; _torn = new double[slices.Length]; IsHitTestVisible = false; }

    public void Tear(int slice, double dx)
    {
        if (slice < 0 || slice >= _torn.Length || _torn[slice] == dx) return;
        _torn[slice] = dx;
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext dc)
    {
        double w = ActualWidth, h = ActualHeight;
        if (w <= 0 || h <= 0) return;
        var ground = new SolidColorBrush(Color.FromArgb((byte)(255 * Math.Clamp(Alpha, 0, 1)), Ground.R, Ground.G, Ground.B));
        ground.Freeze();
        // the slices' edges in px: a slice set sideways is never thinner than 7 px (a thinner one reads as a stray stub), and a
        // short panel (a line or two) keeps only its first sideways slice
        int n = _slices.Length;
        var ys = new double[n + 1];
        for (int i = 0; i < n; i++) ys[i] = Math.Round(h * _slices[i].Y0);
        ys[n] = h;
        for (int i = 0; i < n; i++)
            if (_slices[i].Dx != 0 && ys[i + 1] - ys[i] < 7 && i + 1 < n) ys[i + 1] = Math.Min(h, ys[i] + 7);
        bool shortOne = h < 64;
        int sideways = 0;
        for (int i = 0; i < n; i++)
        {
            var dx0 = _slices[i].Dx;
            if (dx0 != 0 && shortOne && sideways++ > 0) dx0 = 0;
            double y0 = ys[i], y1 = Math.Max(ys[i], ys[i + 1]), dx = Math.Clamp(Math.Round(dx0 + _torn[i]), -Reach, Reach);
            if (y1 <= y0) continue;
            // the ghost edges: magenta out of the left, cyan out of the right; over the top and under the foot too
            dc.DrawRectangle(GhostL, null, new Rect(dx - Ghost, y0, Ghost, y1 - y0));
            dc.DrawRectangle(GhostR, null, new Rect(dx + w, y0, Ghost, y1 - y0));
            if (i == 0) dc.DrawRectangle(GhostL, null, new Rect(dx - Ghost, y0 - 2, w, 2));
            if (i == _slices.Length - 1) dc.DrawRectangle(GhostR, null, new Rect(dx + Ghost, y1, w, 2));
            dc.DrawRectangle(ground, null, new Rect(dx, y0, w, y1 - y0));
            // a slice knocked out past the frame keeps its own edges: a hairline over and under the part that hangs out
            if (dx > 0) { dc.DrawRectangle(GhostR, null, new Rect(w, y0, dx, 1)); dc.DrawRectangle(GhostR, null, new Rect(w, y1 - 1, dx, 1)); }
            else if (dx < 0) { dc.DrawRectangle(GhostL, null, new Rect(dx, y0, -dx, 1)); dc.DrawRectangle(GhostL, null, new Rect(dx, y1 - 1, -dx, 1)); }
        }
    }
}
