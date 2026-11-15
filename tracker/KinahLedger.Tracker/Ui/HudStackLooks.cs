using System.Globalization;
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
/// A look that draws the timers due and the fight itself (the Capsule, the Meter, the Float), in place of the window's
/// timer line and Parser section stacked above the Kinah line (Ui/HudWindow.Timers.cs, Ui/HudWindow.Parser.cs).
/// </summary>
public interface IHudStackLook
{
    /// <summary>The watched timers due within the minutes set, soonest first; none hides them.</summary>
    void ShowTimers(IReadOnlyList<TimerBoard.Item> items, DateTime now);
    /// <summary>The Parser's record under way or just ended, or null to put the fight away.</summary>
    void ShowFight(Encounter? e, DateTime now);
}

/// <summary>A look whose fight goes above it, in the window's own slot that grows upward (Auto): the panel to show, or null for none.</summary>
public interface IHudFightAbove
{
    FrameworkElement? FightAbove(Encounter? e, DateTime now);
}

/// <summary>
/// The three looks of the overlay's design pass (2026-11-14, Steven of the three redesigns: "I love all of them can we
/// just make them options for people in the settings"), picked in Options › In game › Panel look. Their shared parts:
/// the figures from the window's values, the ring of your best, a fight's member rows.
/// </summary>
abstract class HudStackLook : IHudLook, IHudStackLook
{
    public bool Still { get; set; }
    public abstract FrameworkElement Root { get; }
    public virtual Thickness Inset => new(0);
    public virtual double Corner => HudGlass.Radius;
    public abstract void SetOpacity(double opacity);
    public abstract void Update(IReadOnlyDictionary<string, string> values);
    public abstract void ShowTimers(IReadOnlyList<TimerBoard.Item> items, DateTime now);
    public abstract void ShowFight(Encounter? e, DateTime now);

    protected string _acc = "";
    protected bool _accCheck;

    /// <summary>
    /// In a dungeon the meter replaces the Kinah line (Steven, 2026-11-14: "the parser should REPLACE the kinah tracker and that
    /// should be hidden during expeditions"): the look hides its Kinah figures, and a timer due moves into the meter so no
    /// alert is lost. Off again when the player leaves the instance.
    /// </summary>
    public virtual void HideKinah(bool on) => KinahHidden = on;
    protected bool KinahHidden;

    /// <summary>
    /// The meter's width at 100% (Steven, 2026-11-14: "maybe an option to make it wider?"): one figure for every look and
    /// the separate overlay, set by dragging the meter's left or right edge or by Settings › In game › Parser › Width.
    /// The rows and names stretch with it; the figures stay right-aligned.
    /// </summary>
    public static double FightWidth { get => _fightWidth; set => _fightWidth = Math.Round(Math.Clamp(double.IsFinite(value) ? value : FightWidthDefault, FightWidthMin, FightWidthMax)); }
    static double _fightWidth = FightWidthDefault;
    public const double FightWidthDefault = 330, FightWidthMin = 300, FightWidthMax = 640;

    /// <summary>The fight's frame whose left and right edges widen it, or null while no fight shows.</summary>
    public virtual FrameworkElement? FightFrame => null;

    /// <summary>The fight drawn again at <see cref="FightWidth"/>.</summary>
    public virtual void ApplyWidth() { }
    protected Encounter? _lastFight;
    protected DateTime _lastNow;
    public void SetAccuracy(string? text, bool toCheck) { _acc = text ?? ""; _accCheck = toCheck; }

    /// <summary>A meter's figure (DPS, a share, a clock): tabular digits, so a figure that changes as hits land never shifts its column.</summary>
    protected static TextBlock Num(string s, double size, Brush fg, FontWeight? w = null)
    {
        var t = Tx(s, size, fg, w);
        System.Windows.Documents.Typography.SetNumeralAlignment(t, FontNumeralAlignment.Tabular);
        return t;
    }

    /// <summary>The line a dungeon's ready card shows where the members will be.</summary>
    protected const string ReadyLine = "Waiting for the first pull";

    protected static TextBlock Tx(string s, double size, Brush fg, FontWeight? w = null) => new()
    {
        Text = s, FontSize = size, Foreground = fg, FontWeight = w ?? FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center,
        TextTrimming = TextTrimming.CharacterEllipsis,
    };

    /// <summary>A figure and its word: "1.27M /h", the word a third smaller and muted.</summary>
    protected static StackPanel Fig(string big, string unit, Brush fg, double size)
    {
        var sp = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        var t = Tx(big, size, fg, FontWeights.Bold);
        System.Windows.Documents.Typography.SetNumeralAlignment(t, FontNumeralAlignment.Tabular);
        // your DPS rolls up to ten times a second (the Parser's rolling DPS): a fixed width, so the words after it never shift
        if (unit == "DPS") { t.MinWidth = Math.Round(size * 2.9); t.TextAlignment = TextAlignment.Right; }
        sp.Children.Add(t);
        if (unit.Length > 0) { var u = Tx(unit, Math.Round(size * 0.68, 1), Muted, FontWeights.Medium); u.Margin = new Thickness(4, Math.Round(size * 0.18), 0, 0); sp.Children.Add(u); }
        return sp;
    }

    protected static Border Rule(double h = 14, double m = 10) => new() { Width = 1, Height = h, Background = B(0xFFFFFF, .12), Margin = new Thickness(m, 0, m, 0), VerticalAlignment = VerticalAlignment.Center };

    /// <summary>The ring of your best here: a fine gold arc (mint once beaten) on a faint track, the share inside.</summary>
    protected static Grid BestRing(double size, double stroke, double share, bool beat, double fontSize)
    {
        var g = new Grid { Width = size, Height = size, VerticalAlignment = VerticalAlignment.Center };
        g.Children.Add(new Ellipse { Width = size - stroke, Height = size - stroke, StrokeThickness = stroke, Stroke = B(0xFFFFFF, .10), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center });
        var tone = beat ? Mint : Gold;
        double s = Math.Clamp(share, 0, 1);
        if (s >= 0.999) g.Children.Add(new Ellipse { Width = size - stroke, Height = size - stroke, StrokeThickness = stroke, Stroke = tone, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center });
        else if (s > 0.004)
        {
            double c = size / 2, r = (size - stroke) / 2, a = 2 * Math.PI * s;
            var fig = new PathFigure { StartPoint = new Point(c, c - r) };
            fig.Segments.Add(new ArcSegment(new Point(c + r * Math.Sin(a), c - r * Math.Cos(a)), new Size(r, r), 0, a > Math.PI, SweepDirection.Clockwise, true));
            var geo = new PathGeometry(); geo.Figures.Add(fig); geo.Freeze();
            g.Children.Add(new System.Windows.Shapes.Path { Data = geo, Stroke = tone, StrokeThickness = stroke, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round });
        }
        var t = Tx(beat ? "★" : $"{Math.Round(s * 100):0}", fontSize, Ink, FontWeights.Bold); t.HorizontalAlignment = HorizontalAlignment.Center; t.TextTrimming = TextTrimming.None;
        g.Children.Add(t);
        return g;
    }

    /// <summary>A thin bar on a faint track, <paramref name="share"/> of its width.</summary>
    protected static Grid Bar(double share, Brush fill, double h, Thickness m)
    {
        var g = new Grid { Height = h, Margin = m };
        g.Children.Add(new Border { Background = HudGlass.Track, CornerRadius = new CornerRadius(h / 2) });
        var f = new Border { Background = fill, CornerRadius = new CornerRadius(h / 2), HorizontalAlignment = HorizontalAlignment.Left };
        double frac = Math.Clamp(double.IsFinite(share) ? share : 0, 0, 1);
        g.SizeChanged += (_, _) => f.Width = frac <= 0 ? 0 : Math.Max(h, g.ActualWidth * frac);
        g.Children.Add(f);
        return g;
    }

    /// <summary>The glass round a part: the fill, the inner edge, the dark outer hairline (Ui/HudGlass.cs).</summary>
    protected static (Border Ring, Border Frame) Glass(UIElement child, Thickness pad, double radius)
    {
        var frame = new Border { Child = child, Padding = pad };
        HudGlass.Apply(frame, 0.94, new CornerRadius(radius));
        frame.BorderBrush = GlassEdge;
        return (HudGlass.Ring(frame, new CornerRadius(radius)), frame);
    }

    /// <summary>The glass's own edge, whatever variant the design pass's switch is on.</summary>
    protected static readonly Brush GlassEdge = MakeEdge();
    static Brush MakeEdge()
    {
        var b = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(0, 1) };
        b.GradientStops.Add(new GradientStop(Color.FromArgb(0x2E, 0xFF, 0xFF, 0xFF), 0));
        b.GradientStops.Add(new GradientStop(Color.FromArgb(0x10, 0xFF, 0xFF, 0xFF), 1));
        b.Freeze();
        return b;
    }

    // ---- the window's values

    protected sealed record Figures(bool Counted, bool Warm, string Rate, string Total, string Dps, double Share, string ToBest, bool Beat, string Zone, string Clock, string Note, Brush NoteTone);

    protected Figures Read(IReadOnlyDictionary<string, string> v)
    {
        string V(string k) => v.TryGetValue(k, out var s) ? s : "";
        var (run, clock) = Time(V("time"));
        bool counted = run || V("total") is not ("" or "0") || Picks(v).Count > 0;
        var (share, words, beat) = Chase(v);
        string note; Brush tone;
        if (V("paused").Length > 0) { note = V("paused"); tone = Warn; }
        else if (V("flash").Length > 0) { note = V("flash"); tone = Ink; }
        else if (V("notice").Length > 0) { note = V("notice"); tone = Gold; }
        else if (V("flag").Length > 0) { note = V("flag") + " · fix in tracker"; tone = Gold; }
        else if (_acc.Length > 0) { note = _acc; tone = _accCheck ? Warn : Mint; }
        else { note = ""; tone = Muted; }
        return new Figures(counted, V("warm") == "1", V("rate"), V("total"), V("farmDps"), counted ? share : -1, counted ? words : "", beat, V("zone"), counted ? clock : "", note, tone);
    }

    // ---- a fight

    protected static string Clock(TimeSpan t) => BossParser.Clock(t);

    /// <summary>A member: the class's emblem, the name (gold for you), the DPS and the share; a meter in the class's colour under it.</summary>
    protected static FrameworkElement MemberLine(Encounter e, Fighter f, DateTime now, double top, double size, double bar)
    {
        var sp = new StackPanel { Margin = new Thickness(0, 0, 0, 4) };
        var g = new Grid();
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(22) }); g.ColumnDefinitions.Add(new ColumnDefinition());
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(60) }); g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(50) });
        var mark = MainWindow.ClassMark(f.Class, 17, f.Name); mark.VerticalAlignment = VerticalAlignment.Center; g.Children.Add(mark);
        var nm = MemberName(f, size); Grid.SetColumn(nm, 1); g.Children.Add(nm);
        if (e.HasDamage)
        {
            var d = Num(Amounts.Short(e.Dps(f, now)), size + 1.5, Ink, FontWeights.Bold); d.HorizontalAlignment = HorizontalAlignment.Right; Grid.SetColumn(d, 2); g.Children.Add(d);
            var s = Num(Encounter.Pct(e.Share(f)), size - 0.5, Muted, FontWeights.SemiBold); s.HorizontalAlignment = HorizontalAlignment.Right; Grid.SetColumn(s, 3); g.Children.Add(s);
        }
        sp.Children.Add(g);
        var c = ClassArt.ColorOf(f.Class);
        double measure = e.HasDamage ? f.Damage : f.CombatPower;
        sp.Children.Add(Bar(measure / Math.Max(1, top), Frozen(c), bar, new Thickness(22, 2, 0, 0)));
        return sp;
    }

    protected static double Top(Encounter e) => e.TopMeasure();

    /// <summary>A member's name in their class's colour (the bar's, the emblem ring's), you marked by the gold YOU chip.</summary>
    protected static FrameworkElement MemberName(Fighter f, double size)
    {
        var sp = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) };
        var nm = Tx(f.Name.Length > 0 ? f.Name : "Unnamed", size, f.Name.Length > 0 ? ClassArt.NameBrush(f.Class) : Muted, FontWeights.SemiBold);
        sp.Children.Add(nm);
        if (f.You) sp.Children.Add(HudGlass.YouChip());
        return sp;
    }

    /// <summary>"47% · 1.82M left" after a boss's name, or null when its HP isn't known or the fight is over.</summary>
    protected static TextBlock? Hp(Encounter e, double size)
    {
        if (!e.Live || e.BossHpLeft is not { } hp) return null;
        return ParserPanel.HpText(e, size, hp < .34 ? B(RecC) : Ink, Muted);
    }

    /// <summary>The fight's sign: a fight's id, its second, its damage and its HP, so each hit draws once.</summary>
    protected static string FightSig(Encounter? e, DateTime now) => e == null ? "" : $"{e.Id}:{(int)e.Duration(now).TotalSeconds}:{e.Outcome}:{e.Roster.Sum(f => f.Damage)}:{e.BossHp}:{e.Roster.Count}:{e.Boss}";

    protected static (string Text, Brush Tone)? TimerChip(IReadOnlyList<TimerBoard.Item> items, DateTime now)
    {
        var i = items.FirstOrDefault();
        if (i?.AtPc is not { } at) return null;
        var left = at - now;
        return ($"{i.Short} {TimerBoard.Clock(left)}", left <= TimeSpan.FromMinutes(TimerPrefs.GoldMinutes) ? Gold : Ink);
    }
}

// ================================================================ Capsule

/// <summary>
/// Capsule: one pill. The ring of your best, the hour in gold, the session, your DPS while farming; the next watched timer as
/// a gold chip at the pill's end. A fight drops a slim meter under the pill: the boss, its clock, its HP as a hairline, and
/// the members with a meter in their class's colour. A notice sits under the pill in its own small line.
/// </summary>
sealed class HudCapsule : HudStackLook
{
    readonly StackPanel _root = new() { HorizontalAlignment = HorizontalAlignment.Left };
    readonly Border _pill, _pillFrame;
    readonly StackPanel _row = new() { Orientation = Orientation.Horizontal };
    readonly TextBlock _note = new() { FontSize = 11.5, FontWeight = FontWeights.SemiBold, Margin = new Thickness(14, 4, 0, 0), Visibility = Visibility.Collapsed, TextTrimming = TextTrimming.CharacterEllipsis, MaxWidth = 380 };
    readonly Border _meter, _meterFrame;
    readonly StackPanel _meterBody = new();
    readonly Border _chip = new() { CornerRadius = new CornerRadius(10), Padding = new Thickness(8, 2, 9, 3), Margin = new Thickness(12, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, BorderThickness = new Thickness(1), Visibility = Visibility.Collapsed };
    readonly TextBlock _chipText = new() { FontSize = 11.5, FontWeight = FontWeights.Bold, VerticalAlignment = VerticalAlignment.Center };
    string _figSig = "", _fightSig = "";

    public HudCapsule()
    {
        _chip.Child = _chipText;
        var row = new StackPanel { Orientation = Orientation.Horizontal, Children = { _row, _chip } };
        (_pill, _pillFrame) = Glass(row, new Thickness(6, 5, 12, 5), 19);
        (_meter, _meterFrame) = Glass(new StackPanel { Children = { _meterBody, _meterTimer } }, new Thickness(12, 8, 12, 6), 12);
        _meter.Margin = new Thickness(10, 4, 0, 0); _meter.Width = FightWidth - 30; _meter.HorizontalAlignment = HorizontalAlignment.Left; _meter.Visibility = Visibility.Collapsed;
        _root.Children.Add(_pill); _root.Children.Add(_note); _root.Children.Add(_meter);
    }

    public override FrameworkElement Root => _root;
    public override double Corner => 19;
    public override void SetOpacity(double opacity) { _pillFrame.Background = HudGlass.Fill(opacity); _meterFrame.Background = HudGlass.Fill(opacity); }

    public override void Update(IReadOnlyDictionary<string, string> values) => Draw(Read(values));

    void Draw(Figures f)
    {
        var sig = $"{f.Counted}|{f.Warm}|{f.Rate}|{f.Total}|{f.Dps}|{Math.Round(f.Share, 2)}|{f.Beat}|{f.Note}";
        if (sig == _figSig) return;
        _figSig = sig;
        _row.Children.Clear();
        if (!f.Counted || f.Rate.Length == 0)
        {
            var w = Tx("Waiting for the first count", 12.5, Faint, FontWeights.SemiBold); w.Margin = new Thickness(8, 4, 0, 4);
            _row.Children.Add(w);
        }
        else
        {
            if (f.Share >= 0 && !f.Warm) _row.Children.Add(BestRing(26, 2.5, f.Share, f.Beat, 9.5));
            var figs = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(f.Share >= 0 && !f.Warm ? 10 : 8, 0, 0, 0) };
            var rate = Fig(f.Rate, f.Warm ? "" : "/h", Gold, 17); rate.Opacity = f.Warm ? .55 : 1;
            figs.Children.Add(rate);
            if (f.Total.Length > 0) { figs.Children.Add(Rule()); figs.Children.Add(Fig(f.Total, "session", Ink, 13)); }
            if (f.Dps.Length > 0) { figs.Children.Add(Rule()); figs.Children.Add(Fig(f.Dps, "DPS", Ink, 13)); }
            _row.Children.Add(figs);
        }
        _note.Text = f.Note; _note.Foreground = f.NoteTone;
        _note.Visibility = f.Note.Length > 0 && !KinahHidden ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>The next timer due inside the meter, while the pill is away in a dungeon.</summary>
    readonly TextBlock _meterTimer = new() { FontSize = 11.5, FontWeight = FontWeights.Bold, Margin = new Thickness(0, 4, 0, 0), Visibility = Visibility.Collapsed };
    (IReadOnlyList<TimerBoard.Item> Items, DateTime Now)? _timersNow;

    public override void HideKinah(bool on)
    {
        base.HideKinah(on);
        _pill.Visibility = on ? Visibility.Collapsed : Visibility.Visible;
        if (on) _note.Visibility = Visibility.Collapsed; else _figSig = "";
        _meter.Margin = new Thickness(on ? 0 : 10, on ? 0 : 4, 0, 0);
        if (_timersNow is { } t) ShowTimers(t.Items, t.Now);
    }

    public override void ShowTimers(IReadOnlyList<TimerBoard.Item> items, DateTime now)
    {
        _timersNow = (items, now);
        var tc = TimerChip(items, now);
        _meterTimer.Visibility = KinahHidden && tc != null ? Visibility.Visible : Visibility.Collapsed;
        if (tc is { } mt) { _meterTimer.Text = mt.Text; _meterTimer.Foreground = mt.Tone; }
        if (tc is not { } c) { _chip.Visibility = Visibility.Collapsed; return; }
        bool gold = ReferenceEquals(c.Tone, Gold);
        _chipText.Text = c.Text; _chipText.Foreground = c.Tone;
        _chip.Background = B(gold ? GoldC : 0xFFFFFF, gold ? .14 : .06); _chip.BorderBrush = B(gold ? GoldC : 0xFFFFFF, gold ? .45 : .14);
        _chip.Visibility = Visibility.Visible;
    }

    public override FrameworkElement? FightFrame => _meter.Visibility == Visibility.Visible ? _meter : null;

    public override void ApplyWidth()
    {
        _meter.Width = FightWidth - 30;
        _fightSig = ""; ShowFight(_lastFight, _lastNow);
    }

    public override void ShowFight(Encounter? e, DateTime now)
    {
        _lastFight = e; _lastNow = now;
        var sig = FightSig(e, now);
        if (sig == _fightSig) return;
        _fightSig = sig;
        _meterBody.Children.Clear();
        if (e == null) { _meter.Visibility = Visibility.Collapsed; return; }
        var head = new Grid();
        head.ColumnDefinitions.Add(new ColumnDefinition()); head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        head.Children.Add(Tx(e.Title, 13.5, Ink, FontWeights.Bold));
        var clk = e.Waiting ? Tx("Ready", 12.5, Muted, FontWeights.SemiBold) : Num(Clock(e.Duration(now)), 14.5, e.Live ? Gold : Ink, FontWeights.Bold); clk.Margin = new Thickness(10, 0, 0, 0); Grid.SetColumn(clk, 2); head.Children.Add(clk);
        _meterBody.Children.Add(head);
        if (e.Waiting) { var w = Tx(ReadyLine, 12.5, Faint, FontWeights.SemiBold); w.Margin = new Thickness(0, 6, 0, 2); _meterBody.Children.Add(w); _meter.Visibility = Visibility.Visible; return; }
        if (e.BossHpLeft is { } hp && e.Live)
        {
            // the HP as a hairline with "47% · 1.82M left" at its end
            var hpRow = new Grid { Margin = new Thickness(0, 4, 0, 6) };
            hpRow.ColumnDefinitions.Add(new ColumnDefinition()); hpRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var hb = Bar(hp, hp < .34 ? B(RecC) : Gold, 3, new Thickness(0)); hb.VerticalAlignment = VerticalAlignment.Center; hpRow.Children.Add(hb);
            if (Hp(e, 11) is { } hpText) { hpText.Margin = new Thickness(8, 0, 0, 0); Grid.SetColumn(hpText, 1); hpRow.Children.Add(hpText); }
            _meterBody.Children.Add(hpRow);
        }
        else _meterBody.Children.Add(new Border { Height = 6 });
        double top = Top(e);
        foreach (var f in e.Shown().Take(6)) _meterBody.Children.Add(MemberLine(e, f, now, top, 13, 2));
        if (!e.Live && e.Outcome.Length > 0)
            _meterBody.Children.Add(Tx(e.Outcome == "kill" ? "Killed" : e.Outcome == "wipe" ? "Wiped" : e.Outcome == "cleared" ? "Cleared" : "Over", 12.5, e.Outcome is "kill" or "cleared" ? Mint : e.Outcome == "wipe" ? B(RecC) : Muted, FontWeights.Bold));
        _meter.Visibility = Visibility.Visible;
    }
}

// ================================================================ Meter

/// <summary>
/// Meter: a raid meter. The header carries the context: the place and the run's clock while farming, or the boss, its
/// HP (the header's own fill, red) and its clock in a fight; the next watched timer is a gold tab on its right. A fight's
/// members are rows in their class's colour from the left, ranked. The Kinah line is the foot: the ring, the hour, the
/// session and your DPS.
/// </summary>
sealed class HudMeter : HudStackLook
{
    static double W => FightWidth;
    readonly Border _ring, _frame;
    readonly StackPanel _body = new();
    readonly Grid _head = new() { Height = 32 };
    readonly Border _hpFill = new() { HorizontalAlignment = HorizontalAlignment.Left, Width = 0 }, _hpLine = new() { Height = 2, VerticalAlignment = VerticalAlignment.Bottom, HorizontalAlignment = HorizontalAlignment.Left, Width = 0 };
    readonly TextBlock _title = new() { FontSize = 13.5, FontWeight = FontWeights.Bold, Foreground = Ink, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
    readonly TextBlock _hpPct = new() { FontSize = 12.5, FontWeight = FontWeights.Bold, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(7, 0, 0, 0) };
    /// <summary>
    /// The HP words' line under the header, used while a timer's tab takes the header's room (the boss's name would be cut
    /// to a word): "47% · 1.82M left" there instead of beside the name.
    /// </summary>
    readonly TextBlock _hpBelow = new() { FontSize = 11.5, FontWeight = FontWeights.Bold, Margin = new Thickness(10, 5, 10, 0), Visibility = Visibility.Collapsed };
    (string Pct, string Left, bool Words)? _hpNow;
    readonly TextBlock _clock = new() { FontSize = 15, FontWeight = FontWeights.Bold, VerticalAlignment = VerticalAlignment.Center };
    readonly Border _tab = new() { CornerRadius = new CornerRadius(4), Padding = new Thickness(6, 1, 6, 1), Margin = new Thickness(0, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center, Visibility = Visibility.Collapsed };
    readonly TextBlock _tabText = new() { FontSize = 10.5, FontWeight = FontWeights.Bold };
    readonly StackPanel _rows = new() { Visibility = Visibility.Collapsed };
    readonly Grid _foot = new();
    readonly TextBlock _note = new() { FontSize = 11, FontWeight = FontWeights.SemiBold, Margin = new Thickness(10, 0, 10, 7), Visibility = Visibility.Collapsed, TextTrimming = TextTrimming.CharacterEllipsis };
    Figures? _figs;
    bool _fight;
    string _figSig = "", _fightSig = "";

    /// <summary>False: the header and the members only, no Kinah foot (Auto puts it above the Capsule's pill).</summary>
    readonly bool _withFoot = true;

    public HudMeter(bool withFoot = true) : this((byte)0) { _withFoot = withFoot; if (!withFoot) { _foot.Visibility = Visibility.Collapsed; _note.Visibility = Visibility.Collapsed; } }

    HudMeter(byte _)
    {
        var body = _body; body.Width = W;
        _head.Children.Add(new Border { Background = B(0xFFFFFF, .05) });
        var hb = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 0) };
        hb.GradientStops.Add(new GradientStop(Color.FromArgb(0x10, 0xF2, 0x6D, 0x6D), 0)); hb.GradientStops.Add(new GradientStop(Color.FromArgb(0x55, 0xF2, 0x6D, 0x6D), 1)); hb.Freeze();
        _hpFill.Background = hb; _hpLine.Background = B(RecC);
        _head.Children.Add(_hpFill); _head.Children.Add(_hpLine);
        var hg = new Grid { Margin = new Thickness(10, 0, 10, 0) };
        hg.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); hg.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var title = new Grid();
        title.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); title.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        title.Children.Add(_title);
        Grid.SetColumn(_hpPct, 1); title.Children.Add(_hpPct);
        System.Windows.Documents.Typography.SetNumeralAlignment(_clock, FontNumeralAlignment.Tabular); System.Windows.Documents.Typography.SetNumeralAlignment(_hpPct, FontNumeralAlignment.Tabular);
        hg.Children.Add(title);
        _tab.Child = _tabText;
        var right = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(10, 0, 0, 0), Children = { _tab, _clock } };
        Grid.SetColumn(right, 1); hg.Children.Add(right);
        _head.Children.Add(hg);
        body.Children.Add(_head);
        body.Children.Add(new Border { Height = 1, Background = HudGlass.Rule });
        System.Windows.Documents.Typography.SetNumeralAlignment(_hpBelow, FontNumeralAlignment.Tabular);
        body.Children.Add(_hpBelow);
        body.Children.Add(_rows);
        body.Children.Add(_foot);
        body.Children.Add(_note);
        _frame = new Border { Child = body, CornerRadius = new CornerRadius(8), BorderThickness = new Thickness(1), BorderBrush = GlassEdge, Background = HudGlass.Fill(0.94), ClipToBounds = true };
        _ring = HudGlass.Ring(_frame, new CornerRadius(8));
    }

    public override FrameworkElement Root => _ring;
    public override double Corner => 8;
    public override void SetOpacity(double opacity) => _frame.Background = HudGlass.Fill(opacity);

    public override void Update(IReadOnlyDictionary<string, string> values)
    {
        var f = Read(values);
        _figs = f;
        var sig = $"{f.Counted}|{f.Warm}|{f.Rate}|{f.Total}|{f.Dps}|{Math.Round(f.Share, 2)}|{f.Beat}|{f.Note}|{f.Zone}|{f.Clock}|{f.ToBest}|{_fight}";
        if (sig == _figSig) return;
        _figSig = sig;
        if (!_fight) { _title.Text = f.Zone.Length > 0 ? f.Zone : "Kinah Ledger"; _clock.Text = f.Clock; _clock.Foreground = Muted; }
        DrawFoot(f);
        _note.Text = f.Note; _note.Foreground = f.NoteTone;
        _note.Visibility = f.Note.Length > 0 && _withFoot && !KinahHidden ? Visibility.Visible : Visibility.Collapsed;
    }

    public override void HideKinah(bool on)
    {
        base.HideKinah(on);
        if (!_withFoot) return;
        _foot.Visibility = on ? Visibility.Collapsed : Visibility.Visible;
        if (on) _note.Visibility = Visibility.Collapsed; else _figSig = "";
    }

    void DrawFoot(Figures f)
    {
        if (!_withFoot) return;
        _foot.Children.Clear(); _foot.ColumnDefinitions.Clear();
        _foot.Margin = new Thickness(10, _fight ? 6 : 9, 10, _fight ? 7 : 9);
        _foot.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); _foot.ColumnDefinitions.Add(new ColumnDefinition()); _foot.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        if (!f.Counted || f.Rate.Length == 0) { _foot.Children.Add(Tx("Waiting for the first count", 12.5, Faint, FontWeights.SemiBold)); return; }
        bool ring = f.Share >= 0 && !f.Warm;
        if (ring) _foot.Children.Add(BestRing(_fight ? 22 : 34, _fight ? 2.2 : 3, f.Share, f.Beat, _fight ? 8.5 : 10.5));
        var mid = new StackPanel { Margin = new Thickness(ring ? 10 : 0, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        var rate = Fig(f.Rate, f.Warm ? "" : "/h", Gold, _fight ? 14 : 19); rate.Opacity = f.Warm ? .55 : 1;
        mid.Children.Add(rate);
        if (!_fight && f.ToBest.Length > 0) { var tb = Tx((f.Beat ? "★ " : "") + f.ToBest, 11, Muted, FontWeights.Medium); tb.Margin = new Thickness(0, 1, 0, 0); mid.Children.Add(tb); }
        Grid.SetColumn(mid, 1); _foot.Children.Add(mid);
        var side = new StackPanel { VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Right };
        if (f.Total.Length > 0) { var s1 = Fig(f.Total, "session", Ink, 12); s1.HorizontalAlignment = HorizontalAlignment.Right; side.Children.Add(s1); }
        if (!_fight && f.Dps.Length > 0) { var s2 = Fig(f.Dps, "DPS", Ink, 12); s2.HorizontalAlignment = HorizontalAlignment.Right; s2.Margin = new Thickness(0, 1, 0, 0); side.Children.Add(s2); }
        Grid.SetColumn(side, 2); _foot.Children.Add(side);
    }

    public override void ShowTimers(IReadOnlyList<TimerBoard.Item> items, DateTime now)
    {
        if (TimerChip(items, now) is not { } c) { _tab.Visibility = Visibility.Collapsed; PlaceHp(); return; }
        bool gold = ReferenceEquals(c.Tone, Gold);
        _tabText.Text = c.Text; _tabText.Foreground = c.Tone;
        _tab.Background = B(gold ? GoldC : 0xFFFFFF, gold ? .16 : .07);
        _tab.Visibility = Visibility.Visible;
        PlaceHp();
    }

    public override FrameworkElement? FightFrame => _fight ? _ring : null;

    public override void ApplyWidth()
    {
        _body.Width = W;
        _fightSig = ""; ShowFight(_lastFight, _lastNow);
    }

    public override void ShowFight(Encounter? e, DateTime now)
    {
        _lastFight = e; _lastNow = now;
        var sig = FightSig(e, now);
        if (sig == _fightSig) return;
        _fightSig = sig;
        bool was = _fight;
        _fight = e != null;
        _rows.Children.Clear();
        if (e == null)
        {
            _rows.Visibility = Visibility.Collapsed;
            _hpFill.Width = _hpLine.Width = 0; _hpNow = null; PlaceHp();
            if (_figs is { } f0) { _title.Text = f0.Zone.Length > 0 ? f0.Zone : "Kinah Ledger"; _clock.Text = f0.Clock; _clock.Foreground = Muted; if (was) DrawFoot(f0); }
            _figSig = "";
            return;
        }
        _title.Text = e.Title;
        if (e.Waiting)
        {
            // a dungeon before its first pull: its name, Ready, and where the members will be
            _clock.Text = "Ready"; _clock.Foreground = Muted;
            _hpFill.Width = _hpLine.Width = 0; _hpNow = null; PlaceHp();
            var w = Tx(ReadyLine, 12.5, Faint, FontWeights.SemiBold); w.Margin = new Thickness(10, 7, 10, 8);
            _rows.Children.Add(w);
            _rows.Children.Add(new Border { Height = 1, Background = HudGlass.Rule });
            _rows.Visibility = Visibility.Visible;
            if (_figs is { } fw && !was) { DrawFoot(fw); _figSig = ""; }
            return;
        }
        _clock.Text = Clock(e.Duration(now)); _clock.Foreground = e.Live ? Gold : Ink;
        double hp = e.BossHpLeft is { } h && e.Live ? h : 0;
        _hpFill.Width = _hpLine.Width = W * hp;
        // "47% · 1.82M left" (Steven, 2026-11-14: the HP left beside the boss's %)
        _hpNow = hp > 0 ? ($"{Math.Round(hp * 100):0}%", ParserPanel.HpLeft(e), true) : (!e.Live && e.Outcome == "kill" ? "Killed" : !e.Live && e.Outcome == "wipe" ? "Wiped" : "", "", false);
        _hpPct.Tag = !e.Live && e.Outcome == "kill";
        PlaceHp();
        double top = Top(e);
        int rank = 1;
        foreach (var f in e.Shown().Take(6)) _rows.Children.Add(Row(e, f, now, top, rank++));
        if (e.HasDamage)
        {
            var gl = new Grid { Margin = new Thickness(10, 5, 10, 5) };
            gl.ColumnDefinitions.Add(new ColumnDefinition()); gl.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            gl.Children.Add(Tx("Group", 12, Muted, FontWeights.SemiBold));
            var gv = Num(Amounts.Short(e.GroupDps(now)) + " DPS", 13.5, Ink, FontWeights.Bold); Grid.SetColumn(gv, 1); gl.Children.Add(gv);
            _rows.Children.Add(gl);
        }
        _rows.Children.Add(new Border { Height = 1, Background = HudGlass.Rule });
        _rows.Visibility = Visibility.Visible;
        if (_figs is { } f1 && !was) { DrawFoot(f1); _figSig = ""; }
    }

    /// <summary>The HP words beside the boss's name, or on their own line under the header while a timer's tab is there.</summary>
    void PlaceHp()
    {
        _hpPct.Inlines.Clear(); _hpBelow.Inlines.Clear();
        _hpBelow.Visibility = Visibility.Collapsed;
        if (_hpNow is not { } h || h.Pct.Length == 0) return;
        if (!h.Words) { _hpPct.Inlines.Add(new System.Windows.Documents.Run(h.Pct) { Foreground = _hpPct.Tag is true ? Mint : B(RecC) }); return; }
        bool below = _tab.Visibility == Visibility.Visible;
        var host = below ? _hpBelow : _hpPct;
        host.Inlines.Add(new System.Windows.Documents.Run(h.Pct) { Foreground = B(RecC) });
        host.Inlines.Add(new System.Windows.Documents.Run($" · {h.Left}") { Foreground = Muted, FontWeight = FontWeights.SemiBold });
        if (below) _hpBelow.Visibility = Visibility.Visible;
    }

    static FrameworkElement Row(Encounter e, Fighter f, DateTime now, double top, int rank)
    {
        var host = new Grid { Height = 28, Margin = new Thickness(0, 0, 0, 1) };
        var c = ClassArt.ColorOf(f.Class);
        var fill = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 0) };
        fill.GradientStops.Add(new GradientStop(Color.FromArgb(0x60, c.R, c.G, c.B), 0)); fill.GradientStops.Add(new GradientStop(Color.FromArgb(0x22, c.R, c.G, c.B), 1)); fill.Freeze();
        double measure = e.HasDamage ? f.Damage : f.CombatPower;
        // a quiet track the row's width, so the rows read as one table; the fill fading from the class's solid edge
        host.Children.Add(new Border { Background = Frozen(Color.FromArgb(0x0E, c.R, c.G, c.B)) });
        host.Children.Add(new Border { Background = fill, Width = W * Math.Clamp(measure / top, 0, 1), HorizontalAlignment = HorizontalAlignment.Left });
        host.Children.Add(new Border { Width = 3, Background = Frozen(c), HorizontalAlignment = HorizontalAlignment.Left });
        var g = new Grid { Margin = new Thickness(8, 0, 10, 0) };
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(16) }); g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(24) });
        g.ColumnDefinitions.Add(new ColumnDefinition()); g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(64) }); g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(52) });
        g.Children.Add(Num(rank > 0 ? rank.ToString(CultureInfo.InvariantCulture) : "", 11.5, Faint, FontWeights.Bold));
        var mark = MainWindow.ClassMark(f.Class, 18, f.Name); mark.VerticalAlignment = VerticalAlignment.Center; Grid.SetColumn(mark, 1); g.Children.Add(mark);
        var nm = MemberName(f, 13.5); Grid.SetColumn(nm, 2); g.Children.Add(nm);
        if (e.HasDamage)
        {
            var d = Num(Amounts.Short(e.Dps(f, now)), 15, Ink, FontWeights.Bold); d.HorizontalAlignment = HorizontalAlignment.Right; Grid.SetColumn(d, 3); g.Children.Add(d);
            var s = Num(Encounter.Pct(e.Share(f)), 12, Muted, FontWeights.SemiBold); s.HorizontalAlignment = HorizontalAlignment.Right; Grid.SetColumn(s, 4); g.Children.Add(s);
        }
        host.Children.Add(g);
        return host;
    }
}

// ================================================================ Float

/// <summary>
/// Float: no panel. The figures straight over the game with a dark halo round every letter: the timers due on one line, a
/// fight's boss with its clock, a red hairline of its HP and the members on hairline meters, then the ring, the hour in gold,
/// the session and your DPS, and the chase under them. The panel's opacity is the halo's.
/// </summary>
sealed class HudFloat : HudStackLook
{
    readonly Border _host;
    readonly StackPanel _body = new() { HorizontalAlignment = HorizontalAlignment.Left };
    readonly StackPanel _timers = new() { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 6), Visibility = Visibility.Collapsed };
    readonly StackPanel _fight = new() { Visibility = Visibility.Collapsed };
    readonly StackPanel _line = new() { Orientation = Orientation.Horizontal };
    readonly TextBlock _note = new() { FontSize = 11.5, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 4, 0, 0), Visibility = Visibility.Collapsed, TextTrimming = TextTrimming.CharacterEllipsis, MaxWidth = 380 };
    readonly DropShadowEffect _halo = new() { Color = Colors.Black, BlurRadius = 7, ShadowDepth = 0, Opacity = 0.95, RenderingBias = RenderingBias.Quality };
    string _figSig = "", _fightSig = "";

    public HudFloat()
    {
        _body.Children.Add(_timers); _body.Children.Add(_fight); _body.Children.Add(_line); _body.Children.Add(_note);
        _host = new Border { Child = _body, Padding = new Thickness(10), Effect = _halo, Background = Brushes.Transparent };
    }

    public override FrameworkElement Root => _host;
    public override Thickness Inset => new(10);
    public override double Corner => 6;
    public override void SetOpacity(double opacity) => _halo.Opacity = Math.Clamp(0.55 + opacity * 0.45, 0.6, 1);

    public override void Update(IReadOnlyDictionary<string, string> values)
    {
        var f = Read(values);
        var sig = $"{f.Counted}|{f.Warm}|{f.Rate}|{f.Total}|{f.Dps}|{Math.Round(f.Share, 2)}|{f.Beat}|{f.Note}|{f.ToBest}";
        if (sig == _figSig) return;
        _figSig = sig;
        _line.Children.Clear();
        if (!f.Counted || f.Rate.Length == 0) _line.Children.Add(Tx("Waiting for the first count", 13, Muted, FontWeights.SemiBold));
        else
        {
            bool ring = f.Share >= 0 && !f.Warm;
            if (ring) _line.Children.Add(BestRing(30, 2.5, f.Share, f.Beat, 10));
            var figs = new StackPanel { Margin = new Thickness(ring ? 10 : 0, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
            var top = new StackPanel { Orientation = Orientation.Horizontal };
            var rate = Fig(f.Rate, f.Warm ? "" : "/h", Gold, 20); rate.Opacity = f.Warm ? .6 : 1;
            top.Children.Add(rate);
            var rest = (f.Total.Length > 0 ? $"   {f.Total} session" : "") + (f.Dps.Length > 0 ? $"   {f.Dps} DPS" : "");
            if (rest.Length > 0) { var r = Tx(rest, 12, Ink, FontWeights.SemiBold); r.Margin = new Thickness(0, 4, 0, 0); top.Children.Add(r); }
            figs.Children.Add(top);
            if (f.ToBest.Length > 0) figs.Children.Add(Tx((f.Beat ? "★ " : "") + f.ToBest, 11, Muted, FontWeights.Medium));
            _line.Children.Add(figs);
        }
        _note.Text = f.Note; _note.Foreground = f.NoteTone;
        _note.Visibility = f.Note.Length > 0 && !KinahHidden ? Visibility.Visible : Visibility.Collapsed;
    }

    public override void HideKinah(bool on)
    {
        base.HideKinah(on);
        _line.Visibility = on ? Visibility.Collapsed : Visibility.Visible;
        if (on) _note.Visibility = Visibility.Collapsed; else _figSig = "";
        // the meter's rows end the overlay: no gap left under them for the line
        _fight.Margin = new Thickness(0, 0, 0, on ? -8 : 0);
    }

    public override void ShowTimers(IReadOnlyList<TimerBoard.Item> items, DateTime now)
    {
        _timers.Children.Clear();
        foreach (var i in items.Take(HudWindow.TimerLineMax))
        {
            if (i.AtPc is not { } at) continue;
            var left = at - now;
            bool soon = left <= TimeSpan.FromMinutes(TimerPrefs.GoldMinutes);
            if (_timers.Children.Count > 0) _timers.Children.Add(Tx("   ", 11, Faint));
            _timers.Children.Add(Tx(i.Short + " ", 11.5, soon ? Gold : Muted, FontWeights.SemiBold));
            _timers.Children.Add(Tx(TimerBoard.Clock(left), 11.5, soon ? Gold : Ink, FontWeights.Bold));
        }
        _timers.Visibility = _timers.Children.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    StackPanel? _rowsNow;
    public override FrameworkElement? FightFrame => _fight.Visibility == Visibility.Visible ? _rowsNow : null;

    public override void ApplyWidth() { _fightSig = ""; ShowFight(_lastFight, _lastNow); }

    public override void ShowFight(Encounter? e, DateTime now)
    {
        _lastFight = e; _lastNow = now;
        var sig = FightSig(e, now);
        if (sig == _fightSig) return;
        _fightSig = sig;
        _fight.Children.Clear();
        if (e == null) { _fight.Visibility = Visibility.Collapsed; return; }
        var head = new StackPanel { Orientation = Orientation.Horizontal };
        head.Children.Add(Tx(e.Title, 14, Ink, FontWeights.Bold));
        if (Hp(e, 12) is { } hpText) { hpText.Margin = new Thickness(8, 1, 0, 0); head.Children.Add(hpText); }
        head.Children.Add(e.Waiting ? Tx("  Ready", 12.5, Muted, FontWeights.SemiBold) : Num("  " + Clock(e.Duration(now)), 14.5, e.Live ? Gold : Ink, FontWeights.Bold));
        if (!e.Live && e.Outcome is "kill" or "wipe" or "cleared")
            head.Children.Add(Tx(e.Outcome == "wipe" ? "  Wiped" : e.Outcome == "cleared" ? "  Cleared" : "  Killed", 12, e.Outcome == "wipe" ? B(RecC) : Mint, FontWeights.Bold));
        _fight.Children.Add(head);
        if (e.BossHpLeft is { } hp && e.Live) { var bar = Bar(hp, B(RecC), 2, new Thickness(0, 4, 0, 6)); bar.Width = FightWidth - 80; bar.HorizontalAlignment = HorizontalAlignment.Left; _fight.Children.Add(bar); }
        else _fight.Children.Add(new Border { Height = 6 });
        var rows = _rowsNow = new StackPanel { Width = FightWidth - 50, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 0, 0, 8) };
        double top = Top(e);
        if (e.Waiting) rows.Children.Add(Tx(ReadyLine, 12.5, Muted, FontWeights.SemiBold));
        foreach (var f in e.Shown().Take(5)) rows.Children.Add(MemberLine(e, f, now, top, 13, 1.5));
        _fight.Children.Add(rows);
        _fight.Visibility = Visibility.Visible;
    }
}

// ================================================================ Auto

/// <summary>
/// Auto, the default since the design pass (2026-11-14, the command center's mix of Steven's two favourites): the Capsule's
/// pill while there is no fight; in a boss fight or a run, the Meter's header (the boss, its HP as the header's red fill,
/// the timer tab, the clock) and its class-coloured rows open above the pill, which stays where it is as their foot. The
/// window grows upward for them (the Parser's slot, Ui/HudWindow.Parser.cs) and they fade in and out in 120 ms.
/// </summary>
sealed class HudAuto : HudStackLook, IHudFightAbove
{
    readonly HudCapsule _pill = new();
    readonly HudMeter _meter = new(withFoot: false);

    public HudAuto() { _meter.Root.Margin = new Thickness(0, 0, 0, 4); }

    public override FrameworkElement Root => _pill.Root;
    public override double Corner => _pill.Corner;
    public override void SetOpacity(double opacity) { _pill.SetOpacity(opacity); _meter.SetOpacity(opacity); }
    public override void Update(IReadOnlyDictionary<string, string> values) { _pill.Update(values); _meter.Update(values); }
    (IReadOnlyList<TimerBoard.Item> Items, DateTime Now)? _timersNow;
    public override void ShowTimers(IReadOnlyList<TimerBoard.Item> items, DateTime now)
    {
        _timersNow = (items, now);
        // the pill's chip says it once; in a dungeon the pill is away, and the meter's header tab says it
        _pill.ShowTimers(KinahHidden ? System.Array.Empty<TimerBoard.Item>() : items, now);
        _meter.ShowTimers(KinahHidden ? items : System.Array.Empty<TimerBoard.Item>(), now);
    }

    public override void HideKinah(bool on)
    {
        base.HideKinah(on);
        _meter.Root.Margin = new Thickness(0, 0, 0, on ? 0 : 4);
        if (_timersNow is { } t) ShowTimers(t.Items, t.Now);
    }
    public override FrameworkElement? FightFrame => _meter.FightFrame;
    public override void ApplyWidth() => _meter.ApplyWidth();

    /// <summary>A fight comes through FightAbove; the pill never draws one of its own.</summary>
    public override void ShowFight(Encounter? e, DateTime now) => FightAbove(e, now);

    public FrameworkElement? FightAbove(Encounter? e, DateTime now)
    {
        _meter.ShowFight(e, now);
        return e == null ? null : _meter.Root;
    }
}
