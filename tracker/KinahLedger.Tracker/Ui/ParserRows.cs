using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using KinahLedger.Parser;

namespace KinahLedger.Ui;

/// <summary>
/// The fight's rows over the game, in every overlay look (Parser v2, the Arena rows Steven picked, 2026-11-15): an angled rank
/// chip in the member's class colour, the class's emblem, the name, the DPS large and the share, on a thick bar in the class's
/// colour; your row in the gold frame. Kept between draws, so a change moves rather than redraws: the DPS rolls, the bar slides,
/// a member who passes another glides to the new place and their chip flips. Compact shows four rows and keeps you in sight
/// (pinned, with your true rank, when you're below them); expanded shows everyone and adds the damage, and crit and the DPS over
/// time once the record carries them. A death puts a coral tick on the row; a kill sweeps a mint light across; a kill where you
/// beat your best on the boss puts "PB +x%" on your row with a gold burst.
/// </summary>
public sealed class ParserRows : Grid
{
    /// <summary>Panel: on a look's panel. Bare: straight over the game (the Float look), the bars lighter.</summary>
    public enum Kind { Panel, Bare }

    /// <summary>Compact (four rows) or expanded (everyone, more columns): one choice for every look, kept in parser.json.</summary>
    public static bool ExpandedPref { get; set; }
    /// <summary>Your best kill of a boss before this pull (the window gives it from the records kept): the PB moment's measure.</summary>
    public static Func<Encounter, PullRef?>? BestOf { get; set; }

    public const int CompactRows = 4, ExpandedRows = 8;
    readonly Kind _kind;
    readonly double _rowH;
    readonly Grid _host = new() { VerticalAlignment = VerticalAlignment.Top };
    readonly Border _gap = new() { Height = 10, VerticalAlignment = VerticalAlignment.Top, Visibility = Visibility.Collapsed };
    readonly TextBlock _gapText = new() { FontSize = 9.5, FontWeight = FontWeights.SemiBold, Foreground = HudKit.Faint, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Padding = new Thickness(6, 0, 6, 0) };
    readonly Border _sweep = new() { IsHitTestVisible = false, Visibility = Visibility.Collapsed, CornerRadius = new CornerRadius(6) };
    readonly Dictionary<long, RowEl> _rows = new();
    string _fightId = "";
    bool _wasLive, _expanded;
    string _pbShown = "";

    public ParserRows(Kind kind = Kind.Panel)
    {
        _kind = kind;
        _rowH = 30;
        var line = new Grid();
        line.Children.Add(new Border { Height = 1, Background = HudKit.B(0xFFFFFF, 0.1), VerticalAlignment = VerticalAlignment.Center });
        line.Children.Add(new Border { Background = Brushes.Transparent, HorizontalAlignment = HorizontalAlignment.Center, Child = _gapText });
        _gap.Child = line;
        Children.Add(_host);
        Children.Add(_sweep);
        _host.Children.Add(_gap);
        TextOptions.SetTextFormattingMode(this, TextFormattingMode.Display);
        System.Windows.Documents.TextElement.SetFontFamily(this, HudKit.Text);
    }

    /// <summary>Takes the rows off the panel they were on (a look that builds its panel again each draw).</summary>
    public static void Detach(ParserRows r) { if (r.Parent is Panel p) p.Children.Remove(r); else if (r.Parent is Decorator d) d.Child = null; }

    /// <summary>The rows the look shows now (for its height and the tests).</summary>
    public int Shown => _rows.Values.Count(r => r.Root.Visibility == Visibility.Visible);
    public bool Pinned => _gap.Visibility == Visibility.Visible;

    sealed class RowEl
    {
        public required Grid Root, Cols; public required Border Chip, BarFill, Track, Edge, Tick, Pb; public required TextBlock ChipText, Name, Dps, Share, Damage, Crit; public required Grid Spark;
        public required Border Frame;
        public int Rank = -1, Deaths; public double Y = double.NaN; public bool You; public string Cls = "";
    }

    /// <summary>The fight now: each member's row updated in place, moved to their rank.</summary>
    public void Show(Encounter e, DateTime now)
    {
        bool expanded = ExpandedPref;
        if (e.Id != _fightId || expanded != _expanded)
        {
            foreach (var r in _rows.Values) _host.Children.Remove(r.Root);
            _rows.Clear();
            if (e.Id != _fightId) { _pbShown = ""; _wasLive = e.Live; }
            _fightId = e.Id; _expanded = expanded;
        }
        bool dmg = e.HasDamage;
        var all = (dmg ? e.Shown() : e.Ranked()).Where(f => !f.Other).ToList();
        int max = expanded ? ExpandedRows : CompactRows;
        var visible = all.Take(max).ToList();
        var you = all.FirstOrDefault(f => f.You);
        bool pin = you != null && !visible.Contains(you);
        if (pin) visible = all.Take(max - 1).Append(you!).ToList();
        // the view of what the record carries: crit and the timeline columns only when it does
        var view = expanded ? FightView.Of(e, now, null) : null;
        bool crit = view?.HasCrit == true, spark = view?.HasTimeline == true;
        double top = e.TopMeasure();
        foreach (var gone in _rows.Keys.Where(id => !visible.Any(f => f.Id == id)).ToList()) { _host.Children.Remove(_rows[gone].Root); _rows.Remove(gone); }
        for (int i = 0; i < visible.Count; i++)
        {
            var f = visible[i];
            int rank = all.IndexOf(f) + 1;
            bool pinnedRow = pin && f.You;
            double y = i * _rowH + (pinnedRow ? _gap.Height : 0);
            bool fresh = !_rows.TryGetValue(f.Id, out var r);
            if (fresh) { r = Build(f, expanded, crit, spark); _rows[f.Id] = r; _host.Children.Add(r.Root); }
            var row = r!;
            // the figures: the DPS rolls to its new value, the share and the damage follow
            if (dmg)
            {
                if (e.DpsOf(f, now) is { } dps) Motion.Number(row.Dps, $"parser|ov|{e.Id}|{f.Id}|{(int)_kind}", dps, x => Amounts.Short(x), tintRise: -1);
                else row.Dps.Text = "–";
                row.Share.Text = Encounter.Pct(e.Share(f));
                row.Damage.Text = Amounts.Short(e.Figure(f));
                if (view?.Members.FirstOrDefault(m => m.F.Id == f.Id) is { } mv)
                {
                    row.Crit.Text = mv.Crit is { } c ? $"{c * 100:0}%" : "–";
                    if (spark && mv.PerSecond is { Count: > 1 } ps) { row.Spark.Children.Clear(); row.Spark.Children.Add(PK.Spark(PK.Smooth(ps.Select(x => (double)x).ToList()), 60, 18, f.You ? PK.Gold : ClassArt.ColorOf(f.Class), 0, f.You ? 1.6 : 1.2, glow: f.You)); }
                }
            }
            else { row.Dps.Text = f.CombatPower > 0 ? f.CombatPower.ToString("N0", CultureInfo.InvariantCulture) : ""; row.Share.Text = f.CombatPower > 0 ? "CP" : ""; }
            // the bar slides to its new length
            double share = (dmg ? e.Measure(f) : 0) / top;
            Motion.Slide(row.BarFill, $"parser|ovbar|{e.Id}|{f.Id}|{(int)_kind}", share, v => SetBar(row, v));
            // the rank: a changed one flips the chip; the row glides to its new place
            if (row.Rank != rank)
            {
                int was = row.Rank;
                row.Rank = rank;
                if (was > 0 && !fresh) Flip(row, was, rank); else row.ChipText.Text = rank.ToString(CultureInfo.InvariantCulture);
            }
            if (double.IsNaN(row.Y) || fresh) { row.Y = y; row.Root.Margin = new Thickness(0, y, 0, 0); }
            else if (Math.Abs(row.Y - y) > 0.5) Glide(row, y);
            // a death: the coral tick lands on the row
            if (f.Deaths > row.Deaths)
            {
                bool landed = !fresh;
                row.Deaths = f.Deaths;
                row.Tick.Visibility = Visibility.Visible;
                ((TextBlock)row.Tick.Child).Text = f.Deaths > 1 ? $"✕{f.Deaths}" : "✕";
                if (view?.Members.FirstOrDefault(m => m.F.Id == f.Id) is { DeathLog.Count: > 0 } dm) row.Tick.ToolTip = Recap(dm);
                if (landed) Land(row);
            }
        }
        _gap.Visibility = pin ? Visibility.Visible : Visibility.Collapsed;
        if (pin) { _gap.Margin = new Thickness(10, (visible.Count - 1) * _rowH, 10, 0); _gapText.Text = $"{all.Count - visible.Count} more"; _gapText.Background = _kind == Kind.Bare ? Brushes.Transparent : HudKit.B(0x111319); }
        _host.Height = visible.Count * _rowH + (pin ? _gap.Height : 0);
        // the moments: the kill's sweep; a personal best on this boss
        if (_wasLive && !e.Live && e.Outcome is "kill" or "cleared") Sweep();
        if (!e.Live && e.Outcome == "kill" && you != null && _pbShown != e.Id && BestOf?.Invoke(e) is { } best && best.Dps > 0)
        {
            double d = (e.DpsOf(you, e.End ?? now) ?? 0) / best.Dps - 1;
            if (d > 0 && _rows.TryGetValue(you.Id, out var yr)) { _pbShown = e.Id; ShowPb(yr, d); }
        }
        _wasLive = e.Live;
    }

    void SetBar(RowEl r, double v)
    {
        double w = r.Track.ActualWidth;
        if (w <= 0) { r.Track.Tag = v; return; }
        r.BarFill.Width = v <= 0.001 ? 0 : Math.Max(6, w * Math.Clamp(v, 0, 1));
    }

    RowEl Build(Fighter f, bool expanded, bool crit, bool spark)
    {
        var c = ClassArt.ColorOf(f.Class);
        bool bare = _kind == Kind.Bare;
        var widths = new List<double> { 28, 24, -1, 60, 46 };
        if (expanded) { widths.Add(56); if (crit) widths.Add(44); if (spark) widths.Add(66); }
        var cols = PK.Cols(widths.ToArray());
        cols.Height = _rowH - 2;
        cols.Margin = new Thickness(5, 0, 6, 0);
        // the angled rank chip in the class's colour (gold for you)
        var chipText = new TextBlock { FontSize = 11, FontWeight = FontWeights.Bold, Foreground = f.You ? HudKit.B(0x1D1300) : PK.C(ClassArt.NameColor(f.Class)), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        var chip = new Border
        {
            Width = 22, Height = 18, CornerRadius = new CornerRadius(3), VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Left,
            Background = PK.C(f.You ? PK.Gold : c, f.You ? 0.95 : 0.22), BorderBrush = PK.C(f.You ? PK.Gold : c, f.You ? 1 : 0.75), BorderThickness = new Thickness(1), Child = chipText,
            RenderTransformOrigin = new Point(0.5, 0.5), RenderTransform = new TransformGroup { Children = { new SkewTransform(-14, 0), new ScaleTransform(1, 1) } },
            Effect = f.You ? new DropShadowEffect { Color = PK.Gold, BlurRadius = 10, ShadowDepth = 0, Opacity = 0.6 } : null,
        };
        cols.Children.Add(chip);
        cols.Children.Add(PK.Col(ClassArt.Icon(f.Class, 19, f.Name), 1));
        var nm = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) };
        var tick = new Border { Width = 15, Height = 15, CornerRadius = new CornerRadius(2), Background = PK.C(PK.Coral), Margin = new Thickness(0, 0, 5, 0), VerticalAlignment = VerticalAlignment.Center, Visibility = Visibility.Collapsed,
            RenderTransformOrigin = new Point(0.5, 0.5), Effect = new DropShadowEffect { Color = PK.Coral, BlurRadius = 9, ShadowDepth = 0, Opacity = 0.85 },
            Child = new TextBlock { Text = "✕", FontSize = 8.5, FontWeight = FontWeights.Bold, Foreground = Brushes.White, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center } };
        nm.Children.Add(tick);
        var name = new TextBlock { Text = f.Name.Length > 0 ? f.Name : "Unnamed", FontSize = 13, FontWeight = f.You ? FontWeights.Bold : FontWeights.SemiBold, Foreground = f.You ? HudKit.Ink : f.Name.Length > 0 ? ClassArt.NameBrush(f.Class) : HudKit.Muted, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
        nm.Children.Add(name);
        if (f.You) nm.Children.Add(HudGlass.YouChip());
        var pb = new Border { CornerRadius = new CornerRadius(3), Padding = new Thickness(5, 0, 5, 1), Margin = new Thickness(6, 0, 0, 0), Background = HudKit.Gold, VerticalAlignment = VerticalAlignment.Center, Visibility = Visibility.Collapsed,
            RenderTransformOrigin = new Point(0, 0.5), Effect = new DropShadowEffect { Color = PK.Gold, BlurRadius = 12, ShadowDepth = 0, Opacity = 0.9 },
            Child = new TextBlock { FontSize = 9.5, FontWeight = FontWeights.Bold, Foreground = HudKit.B(0x1D1300) } };
        nm.Children.Add(pb);
        cols.Children.Add(PK.Col(nm, 2));
        TextBlock Fig(double size, Brush b, FontWeight w) { var t = new TextBlock { FontSize = size, Foreground = b, FontWeight = w, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center }; System.Windows.Documents.Typography.SetNumeralAlignment(t, FontNumeralAlignment.Tabular); return t; }
        var dps = Fig(15, HudKit.Ink, FontWeights.Bold);
        var share = Fig(11, HudKit.B(0xC9CDDA), FontWeights.SemiBold); share.Margin = new Thickness(0, 0, expanded ? 0 : 2, 0);
        var damage = Fig(11, HudKit.B(0xC9CDDA), FontWeights.SemiBold);
        var critT = Fig(11, HudKit.B(0xC9CDDA), FontWeights.SemiBold);
        var sparkG = new Grid { Width = 60, Height = 18, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
        cols.Children.Add(PK.Col(dps, 3)); cols.Children.Add(PK.Col(share, 4));
        int k = 5;
        if (expanded) { cols.Children.Add(PK.Col(damage, k++)); if (crit) cols.Children.Add(PK.Col(critT, k++)); if (spark) cols.Children.Add(PK.Col(sparkG, k++)); }
        // the bar: a quiet track the row's width, the fill in the class's colour, angled at its end, fading to the right
        var track = new Border { CornerRadius = new CornerRadius(3), Background = PK.C(c, bare ? 0 : 0.07) };
        var fade = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 0) };
        fade.GradientStops.Add(new GradientStop(Color.FromArgb(bare ? (byte)0x60 : (byte)0x8C, c.R, c.G, c.B), 0));
        fade.GradientStops.Add(new GradientStop(Color.FromArgb(bare ? (byte)0x18 : (byte)0x30, c.R, c.G, c.B), 1));
        fade.Freeze();
        var fill = new Border { HorizontalAlignment = HorizontalAlignment.Left, CornerRadius = new CornerRadius(3), Background = fade, Width = 0, RenderTransformOrigin = new Point(0, 1), RenderTransform = new SkewTransform(-16, 0) };
        var edge = new Border { Width = 2, HorizontalAlignment = HorizontalAlignment.Left, Background = PK.C(c), CornerRadius = new CornerRadius(3, 0, 0, 3), Visibility = f.You ? Visibility.Collapsed : Visibility.Visible };
        var inner = new Grid { Height = _rowH - 2, ClipToBounds = true };
        inner.Children.Add(track); inner.Children.Add(fill); inner.Children.Add(edge); inner.Children.Add(cols);
        Border frame = f.You ? PK.YouFrame(inner, 4, new Thickness(0)) : new Border { Child = inner };
        var root = new Grid { VerticalAlignment = VerticalAlignment.Top, Height = _rowH - 2, RenderTransform = new TranslateTransform() };
        root.Children.Add(frame);
        if (f.You) Panel.SetZIndex(root, 2);
        var r = new RowEl { Root = root, Cols = cols, Chip = chip, ChipText = chipText, Name = name, Dps = dps, Share = share, Damage = damage, Crit = critT, Spark = sparkG, BarFill = fill, Track = track, Edge = edge, Tick = tick, Pb = pb, Frame = frame, You = f.You, Cls = f.Class };
        track.SizeChanged += (_, _) => { if (track.Tag is double v) SetBar(r, v); else SetBar(r, fill.Width > 0 ? fill.Width / Math.Max(1, track.ActualWidth) : 0); };
        return r;
    }

    // ---- the motion (Ui/Motion.cs courses: held at KL_MOTION_AT in a render, nothing while the tracker sits still)

    /// <summary>The row glides from where it is to its new place (a member passed another).</summary>
    void Glide(RowEl r, double to)
    {
        double from = r.Y + ((TranslateTransform)r.Root.RenderTransform).Y;
        r.Y = to;
        r.Root.Margin = new Thickness(0, to, 0, 0);
        var tt = (TranslateTransform)r.Root.RenderTransform;
        if (!Motion.On) { tt.Y = 0; return; }
        double offset = from - to;
        tt.Y = offset;
        bool down = to > from;
        if (down) Panel.SetZIndex(r.Root, 0); else Panel.SetZIndex(r.Root, r.You ? 3 : 2);
        Motion.Play(r.Root, 380, p => { tt.Y = offset * (1 - Motion.Out(p)); if (down) r.Root.Opacity = 1 - 0.65 * Math.Sin(Math.PI * Math.Clamp(p, 0, 1)); },
            () => { tt.Y = 0; r.Root.Opacity = 1; Panel.SetZIndex(r.Root, r.You ? 2 : 0); });
    }

    /// <summary>The rank chip turns over like a card: the old number folds away, the new one unfolds.</summary>
    void Flip(RowEl r, int was, int now)
    {
        var sc = (ScaleTransform)((TransformGroup)r.Chip.RenderTransform).Children[1];
        if (!Motion.On) { r.ChipText.Text = now.ToString(CultureInfo.InvariantCulture); sc.ScaleY = 1; return; }
        Motion.Play(r.Chip, 320, p => { sc.ScaleY = Math.Max(0.04, Math.Abs(Math.Cos(Math.PI * p))); r.ChipText.Text = (p < 0.5 ? was : now).ToString(CultureInfo.InvariantCulture); },
            () => { sc.ScaleY = 1; r.ChipText.Text = now.ToString(CultureInfo.InvariantCulture); });
    }

    /// <summary>A death: the coral tick lands (large, settling in) and the row washes coral once.</summary>
    void Land(RowEl r)
    {
        var st = new ScaleTransform(1, 1); r.Tick.RenderTransform = st;
        var wash = new Border { CornerRadius = new CornerRadius(3), IsHitTestVisible = false };
        if (r.Frame.Child is Grid g) g.Children.Add(wash); else return;
        if (!Motion.On) { g.Children.Remove(wash); return; }
        Motion.Play(r.Tick, 700, p =>
        {
            double s = 1 + 0.8 * (1 - Motion.Out(Math.Min(1, p * 2))); st.ScaleX = st.ScaleY = s;
            wash.Background = PK.C(PK.Coral, 0.35 * (1 - p));
        }, () => { st.ScaleX = st.ScaleY = 1; g.Children.Remove(wash); });
    }

    /// <summary>The kill: a mint light sweeps across the rows.</summary>
    void Sweep()
    {
        if (!Motion.On) return;
        _sweep.Visibility = Visibility.Visible;
        var band = new LinearGradientBrush { StartPoint = new Point(0, 0.3), EndPoint = new Point(1, 0.7) };
        var a = new GradientStop(Color.FromArgb(0, PK.Mint.R, PK.Mint.G, PK.Mint.B), 0); var m = new GradientStop(PK.Mint, 0); var b = new GradientStop(Color.FromArgb(0, PK.Mint.R, PK.Mint.G, PK.Mint.B), 0);
        band.GradientStops.Add(a); band.GradientStops.Add(m); band.GradientStops.Add(b);
        _sweep.Background = band;
        Motion.Play(_sweep, 900, p =>
        {
            double at = -0.2 + 1.4 * Motion.Out(p), k = Math.Sin(Math.PI * p);
            a.Offset = at - 0.22; m.Offset = at; b.Offset = at + 0.22;
            m.Color = Color.FromArgb((byte)(0x78 * k), PK.Mint.R, PK.Mint.G, PK.Mint.B);
        }, () => _sweep.Visibility = Visibility.Collapsed);
    }

    /// <summary>A new personal best on this boss: "PB +4.2%" pops onto your row with a burst of gold.</summary>
    void ShowPb(RowEl r, double d)
    {
        ((TextBlock)r.Pb.Child).Text = $"PB {PK.Signed(d)}";
        r.Pb.Visibility = Visibility.Visible;
        var st = new ScaleTransform(1, 1); r.Pb.RenderTransform = st;
        if (!Motion.On) return;
        var glow = r.Frame.Effect as DropShadowEffect;
        Motion.Play(r.Pb, 900, p =>
        {
            double q = Math.Min(1, p / 0.55);
            double s = 0.4 + 1.1 * Motion.Out(q) - 0.15 * Math.Sin(Math.PI * q); st.ScaleX = st.ScaleY = s; r.Pb.Opacity = Math.Clamp(p / 0.25, 0, 1);
            if (glow != null) { double k = Math.Sin(Math.PI * p); glow.BlurRadius = 16 + 20 * k; glow.Opacity = 0.42 + 0.5 * k; }
        }, () => { st.ScaleX = st.ScaleY = 1; r.Pb.Opacity = 1; if (glow != null) { glow.BlurRadius = 16; glow.Opacity = 0.42; } });
    }

    /// <summary>The death recap a hover on the tick shows (when the record carries the last hits).</summary>
    static FrameworkElement Recap(MemberView m)
    {
        var sp = new StackPanel();
        foreach (var d in m.DeathLog.Take(1))
        {
            sp.Children.Add(new TextBlock { Text = $"{m.F.Name} died at {BossParser.Clock(TimeSpan.FromSeconds(d.At))}", FontWeight = FontWeights.Bold, Foreground = PK.C(PK.Coral) });
            foreach (var h in d.Last) sp.Children.Add(new TextBlock { Text = $"−{d.At - h.At:0.0} s  {h.Source}  {Amounts.Short(h.Amount)}{(h.HpAfter is { } hp ? $"  {Math.Round(hp * 100):0}%" : "")}" });
        }
        return sp;
    }
}
