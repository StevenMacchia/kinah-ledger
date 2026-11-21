using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using KinahLedger.Parser;

namespace KinahLedger.Ui;

/// <summary>
/// The PB ghost on the meter (Parser/PbGhost.cs), the same in every look and adding no height (Steven, 2026-11-20: "new wow
/// factors ... lightweight - not bulky"): on your row, a slim line where your bar would end at your best's pace (the ghost),
/// the gap between it and your bar's end lit along the bar's foot, and beside it a small chip, "+6.2%" green or "−3.1%" red,
/// the full words in its tooltip. At a kill the chip says "PB +4.1%" (gold, a glint across it once) or "−2.3%". Where the row
/// has no room for the chip beside the line without touching a word, the chip stays hidden and the line says it alone. One
/// element per row, drawn (never laid out) each time the pace moves; the line and the figure glide to a new pace.
/// </summary>
static class GhostMarks
{
    public static readonly Color AheadC = Color.FromRgb(0x46, 0xE0, 0x8F), BehindC = Color.FromRgb(0xFF, 0x5E, 0x5E), BestC = Color.FromRgb(0xFF, 0xC9, 0x4D);
    /// <summary>The glide to a new pace, and a new best's glint across the chip (ms).</summary>
    public const double GlideMs = 260, GlintMs = 750;
    /// <summary>How far from the line (px, beyond its own half width) the chip may step to clear a word; further, it stays hidden.</summary>
    public const double ChipReach = 48;

    /// <summary>
    /// Where a text's words are, in <paramref name="to"/>'s coordinates: its box, narrowed to the words' own width where the box
    /// is wider (a figure right-aligned in a stretched column fills the column, its words only the right end). Null when it
    /// can't be placed.
    /// </summary>
    public static Rect? InkOf(TextBlock t, Visual to)
    {
        try
        {
            double box = t.RenderSize.Width;
            var m = t.Margin;
            double words = Math.Max(0, t.DesiredSize.Width - m.Left - m.Right);
            // laid out wider than its words: where they sit in it by its alignment
            double w = words > 0 && words < box - 0.5 ? words : box;
            double x = w >= box ? 0 : t.TextAlignment switch { TextAlignment.Right => box - w, TextAlignment.Center => (box - w) / 2, _ => 0 };
            return t.TransformToVisual(to).TransformBounds(new Rect(x, 0, w, t.RenderSize.Height));
        }
        catch (InvalidOperationException) { return null; }
    }

    public static Color ToneOf(GhostPace p) => p.NewBest ? BestC : p.Ahead && !p.Final ? AheadC : BehindC;

    // brushes and pens made once (a paint 20 times a second makes none)
    static readonly Dictionary<uint, SolidColorBrush> Brushes = new();
    static SolidColorBrush Br(Color c, double a = 1)
    {
        byte al = (byte)Math.Round(255 * Math.Clamp(a, 0, 1));
        uint key = (uint)al << 24 | (uint)c.R << 16 | (uint)c.G << 8 | c.B;
        if (!Brushes.TryGetValue(key, out var b)) { b = new SolidColorBrush(Color.FromArgb(al, c.R, c.G, c.B)); b.Freeze(); Brushes[key] = b; }
        return b;
    }
    static readonly Pen NotchPen = MakePen();
    static Pen MakePen() { var p = new Pen(new SolidColorBrush(Color.FromArgb(0x8C, 0, 0, 0)), .8); p.Freeze(); return p; }
    static readonly LinearGradientBrush Shine = MakeShine();
    static LinearGradientBrush MakeShine() { var s = new LinearGradientBrush(Color.FromArgb(0, 255, 255, 255), Color.FromArgb(0xD0, 255, 255, 255), 0) { SpreadMethod = GradientSpreadMethod.Reflect, StartPoint = new Point(0, 0), EndPoint = new Point(0.5, 0) }; s.Freeze(); return s; }
    static readonly SolidColorBrush ChipInk = Br(Color.FromRgb(0x0B, 0x14, 0x10));

    /// <summary>
    /// The ghost on your row: drawn over the row. <see cref="Bar"/> says where a full bar would be (in this element's
    /// coordinates; the row's whole width when null): the ghost's line at the length its bar would have, the gap from your
    /// bar's end to it along the bar's foot, and the chip beside the line. It takes the pointer only over the chip (its tooltip).
    /// Cheap by design: drawn into a drawing of its own (a new pace records it again: no layout pass), the row's words read once
    /// after the row lays out, and a frame clock only for a glide to a pace far from the last (a pull's first pace, a kill).
    /// </summary>
    public sealed class LineArt : FrameworkElement
    {
        public LineArt()
        {
            Tag = "pb-ghost-line";
            ToolTipService.SetInitialShowDelay(this, 250);
            // the tooltip's words made when it opens, not with each pace
            ToolTip = new ToolTip();
            ToolTipOpening += (_, ev) => { if (TipText is { } t && ToolTip is ToolTip tt) tt.Content = t; else ev.Handled = true; };
        }
        /// <summary>Your bar's length, 0 to 1 of a full bar, and the pace (the ghost's length is yours times its DPS over yours).</summary>
        public double You;
        public GhostPace? Pace;
        /// <summary>The full bar's rect, worked out when drawn (the row laid out by then); null: the whole row.</summary>
        public Func<Rect?>? Bar;
        /// <summary>Your bar's drawn end when the row knows it (an animated fill's width now), else from <see cref="You"/>.</summary>
        public Func<double?>? YouEnd;
        /// <summary>The row whose words the line and the chip never cross: the line is a short mark at the bar's foot there, the chip hidden.</summary>
        public FrameworkElement? Row { get => _row; set { if (!ReferenceEquals(_row, value)) { _row = value; _face = null; _words = new(); } } }
        FrameworkElement? _row;
        /// <summary>The chip may show (the Parser's own rows hide it while their PB chip says the same at a kill).</summary>
        public bool ChipAllowed = true;
        /// <summary>For the tests: the line was drawn short last time (it met a word), the chip's words and place (null: hidden).</summary>
        public bool ShortLast { get; private set; }
        public string? ChipShown { get; private set; }
        public int WordsLast { get; private set; }
        public Rect? ChipRect { get; private set; }
        /// <summary>What the chip's tooltip says: the full words (and the group's), then how the two figures stand.</summary>
        public string? TipText => Pace is { } p ? PbGhost.Words(p) + (PbGhost.GroupShown && p.GroupDelta is { } gd ? " · " + PbGhost.GroupWords(gd) : "") + "\n" + PbGhost.Tip(p) : null;

        /// <summary>A move this far (px of the line) or this much (of the delta) glides; a smaller one, a pace's usual step, is drawn where it lands.</summary>
        const double GlideFromPx = 4, GlideFromDelta = 0.01;

        // the line's ratio to your bar and the delta as drawn now (mid-glide they differ from the pace's)
        double _ratio = double.NaN, _delta = double.NaN, _ratioFrom, _deltaFrom, _ratioTo = double.NaN, _deltaTo = double.NaN;
        DateTime _glideAt = DateTime.MinValue, _glintAt = DateTime.MinValue;
        GhostPace? _glintFor;
        bool _ticking;

        public void Set(GhostPace? p, double yourBar)
        {
            bool was = Pace != null;
            Pace = p; You = yourBar;
            Visibility = p == null ? Visibility.Collapsed : Visibility.Visible;
            if (p == null) { _ratio = _delta = _ratioTo = _deltaTo = double.NaN; return; }
            double ratio = p.You > 0 ? p.Ghost / p.You : 1;
            bool far = !double.IsNaN(_ratio) && (Math.Abs(ratio - _ratio) * Math.Max(1, _youLen) >= GlideFromPx || Math.Abs(p.Delta - _delta) >= GlideFromDelta);
            if (Motion.Still || !was || double.IsNaN(_ratio) || !far) { if (!_ticking) { _ratio = ratio; _delta = p.Delta; } }
            else { _ratioFrom = _ratio; _deltaFrom = _delta; _glideAt = DateTime.UtcNow; Tick(true); }
            _ratioTo = ratio; _deltaTo = p.Delta;
            // a new best: a glint across the chip once, as the kill is seen
            if (p.NewBest && !Equals(_glintFor, p) && !Motion.Still && was) { _glintFor = p; _glintAt = DateTime.UtcNow; Tick(true); }
            if (!_ticking) Redraw();
            Recheck();
        }

        readonly DrawingGroup _drawing = new();
        List<Rect> _words = new();
        Typeface? _face;
        double _youLen;
        bool _rechecking;
        (string Text, double Dip, FormattedText Ft)? _ftLast, _ftLast2;

        void Redraw() { using var dc = _drawing.Open(); Paint(dc); }

        protected override void OnRender(DrawingContext dc) { _words = Words(); Redraw(); dc.DrawDrawing(_drawing); }

        /// <summary>Once the row has laid out (its words moved with the new figures): its words read again, and drawn again if they moved.</summary>
        void Recheck()
        {
            if (_rechecking) return;
            _rechecking = true;
            Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded, () =>
            {
                _rechecking = false;
                if (Pace == null) return;
                var now = Words();
                bool moved = now.Count != _words.Count;
                for (int i = 0; !moved && i < now.Count; i++)
                    moved = Math.Abs(now[i].X - _words[i].X) > .5 || Math.Abs(now[i].Right - _words[i].Right) > .5 || Math.Abs(now[i].Y - _words[i].Y) > .5;
                _words = now;
                if (moved && !_ticking) Redraw();
            });
        }

        /// <summary>The frame clock while a glide or a glint runs (off the rest of the time).</summary>
        void Tick(bool on)
        {
            if (on == _ticking) return;
            _ticking = on;
            if (on) CompositionTarget.Rendering += OnFrame; else CompositionTarget.Rendering -= OnFrame;
        }

        void OnFrame(object? sender, EventArgs e)
        {
            var now = DateTime.UtcNow;
            double g = Math.Clamp((now - _glideAt).TotalMilliseconds / GlideMs, 0, 1), k = Motion.Out(g);
            if (!double.IsNaN(_ratioTo)) { _ratio = _ratioFrom + (_ratioTo - _ratioFrom) * k; _delta = _deltaFrom + (_deltaTo - _deltaFrom) * k; }
            bool glint = (now - _glintAt).TotalMilliseconds < GlintMs;
            if (g >= 1 && !glint || !IsVisible) { if (!double.IsNaN(_ratioTo)) { _ratio = _ratioTo; _delta = _deltaTo; } Tick(false); }
            Redraw();
        }

        internal Rect FullForTest => Full();
        Rect Full() => Bar?.Invoke() is { Width: > 0 } r ? r : new Rect(0, 2, Math.Max(0, ActualWidth), Math.Max(0, ActualHeight - 4));
        /// <summary>Your bar's end and the ghost's line, in this element's x (for the tests too; the line as the pace puts it, not mid-glide).</summary>
        public double YouX => YouOf(Full());
        double YouOf(Rect f) => YouEnd?.Invoke() is { } x ? x : f.X + f.Width * Math.Clamp(You, 0, 1);
        public double GhostX { get { var f = Full(); return XAt(f, YouOf(f), Pace is { You: > 0 } p ? p.Ghost / p.You : 1); } }
        double XAt(Rect f, double yx, double ratio) => Math.Clamp(f.X + (yx - f.X) * ratio, f.X, Math.Max(f.X, ActualWidth - 3));

        /// <summary>The words on the row (visible texts with something in them), in this element's coordinates.</summary>
        List<Rect> Words()
        {
            var list = new List<Rect>();
            if (Row == null) return list;
            var stack = new Stack<DependencyObject>();
            stack.Push(Row);
            while (stack.Count > 0)
            {
                var d = stack.Pop();
                if (ReferenceEquals(d, this)) continue;
                // (Visibility, not IsVisible: a row just built says IsVisible false until a later pass; any opacity: a look's rows
                // fade in, and a word fading in is a word the chip must keep clear of)
                if (d is UIElement u && u.Visibility != Visibility.Visible) continue;
                if (d is TextBlock t)
                {
                    if (t.ActualWidth > 0 && (t.Text.Trim().Length > 0 || t.Inlines.Count > 0) && InkOf(t, this) is { } r) list.Add(r);
                    if (_face == null && t.Tag is "row-dps") _face = new Typeface(t.FontFamily, FontStyles.Normal, FontWeights.Bold, FontStretches.Normal);
                    continue;
                }
                for (int i = 0; i < VisualTreeHelper.GetChildrenCount(d); i++) stack.Push(VisualTreeHelper.GetChild(d, i));
            }
            return list;
        }

        protected override HitTestResult? HitTestCore(PointHitTestParameters p) => ChipRect is { } r && r.Contains(p.HitPoint) ? new PointHitTestResult(this, p.HitPoint) : null;

        void Paint(DrawingContext dc)
        {
            ChipShown = null; ChipRect = null;
            if (Pace is not { } p || ActualWidth < 20 || ActualHeight < 6) return;
            var bar = Full();
            if (_words.Count == 0 && Row != null) _words = Words();   // a row just given: its words before the chip goes anywhere
            var words = _words;
            WordsLast = words.Count;
            // a thin bar (a line look's hairline) gets a marker a little taller than itself, a thick one its own height
            double ext = bar.Height < 10 ? 2.5 : 0;
            double top = Math.Max(0, bar.Top - ext), bottom = Math.Min(ActualHeight, bar.Bottom + ext);
            double ratio = double.IsNaN(_ratio) ? (p.You > 0 ? p.Ghost / p.You : 1) : _ratio;
            double yx = YouOf(bar);
            _youLen = yx - bar.X;
            double gx = Math.Round(XAt(bar, yx, ratio)) + .5;
            var tone = ToneOf(p);
            // the gap between you and the ghost along the bar's foot
            double a = Math.Min(gx, yx), b = Math.Max(gx, yx);
            if (b - a >= 1)
            {
                double bh = Math.Min(3, Math.Max(2, bar.Height));
                var band = new Rect(a, bar.Bottom - bh, b - a, bh);
                dc.DrawRectangle(Br(Colors.Black, .45), null, new Rect(band.X, band.Y - .5, band.Width, band.Height + 1));
                dc.DrawRectangle(Br(tone, .95), null, band);
            }
            // a word of the row in its way (the DPS, a name): a short mark at the bar's foot, the words left whole
            bool cross = false;
            foreach (var r in words) if (gx >= r.Left - 3 && gx <= r.Right + 3 && r.Bottom > top + 1 && r.Top < bottom - 1) { cross = true; break; }
            ShortLast = cross;
            if (cross) top = Math.Max(top, bottom - 7);
            dc.DrawRectangle(Br(Colors.Black, .6), null, new Rect(gx - 2, top, 4, bottom - top));
            dc.DrawRectangle(Br(Colors.White, .92), null, new Rect(gx - 1, top, 2, bottom - top));
            var notch = new StreamGeometry();
            using (var c = notch.Open())
            {
                if (!cross) { c.BeginFigure(new Point(gx - 3.5, top), true, true); c.LineTo(new Point(gx + 3.5, top), true, false); c.LineTo(new Point(gx, top + 3.5), true, false); }
                c.BeginFigure(new Point(gx - 3.5, bottom), true, true); c.LineTo(new Point(gx + 3.5, bottom), true, false); c.LineTo(new Point(gx, bottom - 3.5), true, false);
            }
            notch.Freeze();
            dc.DrawGeometry(Br(Colors.White, .95), NotchPen, notch);
            if (ChipAllowed) Chip(dc, p, double.IsNaN(_delta) ? p.Delta : _delta, gx, yx, bar, words, tone);
        }

        /// <summary>A chip's words laid out (the last two kept: a pace's figure changes a tenth at a time).</summary>
        FormattedText Text(string s, double dip)
        {
            if (_ftLast is { } l && l.Text == s && l.Dip == dip) return l.Ft;
            if (_ftLast2 is { } l2 && l2.Text == s && l2.Dip == dip) { (_ftLast, _ftLast2) = (_ftLast2, _ftLast); return l2.Ft; }
            var ft = new FormattedText(s, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, _face ?? DefaultFace, 10, ChipInk, dip);
            _ftLast2 = _ftLast; _ftLast = (s, dip, ft);
            return ft;
        }
        static readonly Typeface DefaultFace = new(HudKit.Text, FontStyles.Normal, FontWeights.Bold, FontStretches.Normal);

        /// <summary>
        /// The chip beside the line: its words (with the group's pace when Group pace is on and there's room, else yours alone),
        /// on the first side with room: past the further of the line and your bar's end, else before the nearer; a word in the
        /// way, just past it while the chip stays within <see cref="ChipReach"/> of the line; inside the row and clear of every
        /// word. No room: no chip.
        /// </summary>
        void Chip(DrawingContext dc, GhostPace p, double delta, double gx, double yx, Rect bar, List<Rect> words, Color tone)
        {
            var shown = p with { Delta = delta };
            string mine = PbGhost.Short(shown);
            string? both = PbGhost.GroupShown && p.GroupDelta is { } gd && !p.Final ? mine + " · " + PbGhost.GroupWords(gd) : null;
            double dip = VisualTreeHelper.GetDpi(this).PixelsPerDip;
            double cy = Math.Clamp(bar.Top + bar.Height / 2, 0, ActualHeight);
            var tries = new double[8];
            foreach (var form in both != null ? new[] { both, mine } : new[] { mine })
            {
                var ft = Text(form, dip);
                double w = Math.Ceiling(ft.WidthIncludingTrailingWhitespace) + 8, h = Math.Ceiling(ft.Height) + 1;
                double y = Math.Clamp(cy - h / 2, 0, Math.Max(0, ActualHeight - h));
                double far = Math.Max(gx, yx), near = Math.Min(gx, yx);
                int n = 0;
                tries[n++] = far + 5; tries[n++] = near - 5 - w;
                for (int i = 0; i < n; i++)
                {
                    var r = new Rect(Math.Round(tries[i]), Math.Round(y), w, h);
                    if (r.Left < 1 || r.Right > ActualWidth - 1) continue;
                    Rect? hit = null;
                    foreach (var o in words) { var g = o; g.Inflate(2, 0); if (g.IntersectsWith(r)) { hit = o; break; } }
                    if (hit is { } hr)
                    {
                        foreach (var nx in new[] { hr.Right + 4, hr.Left - 4 - w })
                        {
                            bool had = false;
                            for (int j = 0; j < n; j++) if (Math.Abs(tries[j] - nx) < 0.5) had = true;
                            if (!had && n < tries.Length && Math.Abs(nx + w / 2 - gx) <= ChipReach + w / 2) tries[n++] = nx;
                        }
                        continue;
                    }
                    ChipShown = form; ChipRect = r;
                    dc.DrawRectangle(Br(Colors.Black, .5), null, new Rect(r.X - .5, r.Y - .5, r.Width + 1, r.Height + 1));
                    dc.DrawRoundedRectangle(Br(tone, .96), null, r, 3, 3);
                    dc.DrawText(ft, new Point(r.X + 4, r.Y + .5));
                    // a new best's glint: a band of light across the chip, once
                    double gt = (DateTime.UtcNow - _glintAt).TotalMilliseconds / GlintMs;
                    if (p.NewBest && gt is >= 0 and < 1)
                    {
                        double gx0 = r.X - 14 + (r.Width + 28) * Motion.Out(gt);
                        dc.PushClip(new RectangleGeometry(r, 3, 3));
                        dc.DrawRectangle(Shine, null, new Rect(gx0 - 8, r.Y, 16, r.Height));
                        dc.Pop();
                    }
                    return;
                }
            }
        }
    }
}

/// <summary>
/// A theme's row says where its DPS bar is drawn, so the PB ghost's line sits on it (GhostMarks.LineArt): the element the bar
/// is drawn in and, in that element's own coordinates, the rect a FULL bar (value 1) fills; a bar of value v fills from
/// Track.Left to Track.Left + Track.Width * v, between Track.Top and Track.Bottom. Null while not laid out (ActualWidth 0).
/// A row without it is taken to fill its whole width and height.
/// </summary>
public interface IThemeBarSpan
{
    (FrameworkElement On, Rect Track)? BarTrack { get; }
}
