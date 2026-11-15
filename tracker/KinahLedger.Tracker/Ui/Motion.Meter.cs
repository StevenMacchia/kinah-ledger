using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace KinahLedger.Ui;

/// <summary>
/// The DPS meter's motion (Steven, 2026-11-15: "the absolute next level"): moments that outlive the element they play on
/// (a meter part drawn again each tenth of a second), the boss HP's red chunk on a big drop, and the odometer the DPS rolls
/// on (<see cref="Odometer"/>). Every course is short and eased; nothing loops; off in a render or a test
/// (<see cref="Still"/>), held at KL_MOTION_AT.
/// </summary>
public static partial class Motion
{
    /* ---------------- a moment by key: a part drawn again picks its course up where it was ---------------- */

    static readonly Dictionary<string, DateTime> Moments = new();
    static readonly Queue<string> MomentOrder = new();

    /// <summary>
    /// A moment starts now under <paramref name="key"/> (the first mark only: a later one leaves it running). Nothing when the
    /// tracker sits still, so a part drawn for the key settles at once.
    /// </summary>
    internal static void Mark(string key)
    {
        if (!On || Moments.ContainsKey(key)) return;
        Moments[key] = DateTime.UtcNow;
        MomentOrder.Enqueue(key);
        while (MomentOrder.Count > 128) Moments.Remove(MomentOrder.Dequeue());
    }

    /// <summary>Whether a moment under this key was marked (it may be over).</summary>
    internal static bool Marked(string key) => Moments.ContainsKey(key);

    /// <summary>
    /// Plays the moment under <paramref name="key"/> on <paramref name="el"/> from where it is now (a part built again joins it
    /// part-way); a moment never marked, or over, is drawn settled (<paramref name="step"/> at 1, then <paramref name="done"/>).
    /// </summary>
    internal static void Resume(UIElement el, string key, double ms, Action<double> step, Action? done = null)
    {
        if (FrozenAt is double at) { if (Moments.ContainsKey(key)) { step(at); if (at >= 1) done?.Invoke(); } else { step(1); done?.Invoke(); } return; }
        if (!On || !Moments.TryGetValue(key, out var t0)) { step(1); done?.Invoke(); return; }
        double p0 = (DateTime.UtcNow - t0).TotalMilliseconds / ms;
        if (p0 >= 1) { step(1); done?.Invoke(); return; }
        PlayFrom(el, ms, Math.Max(0, p0), step, done);
    }

    /// <summary><see cref="Play"/> from <paramref name="p0"/> of the course (the rest of it over the time left).</summary>
    internal static void PlayFrom(UIElement el, double ms, double p0, Action<double> step, Action? done = null)
    {
        if (p0 <= 0) { Play(el, ms, step, done); return; }
        var token = ++_token;
        Runs.AddOrUpdate(el, new Course { Step = step, Token = token });
        step(p0);
        var a = new DoubleAnimation(p0, 1, TimeSpan.FromMilliseconds(Math.Max(1, ms * (1 - p0)))) { FillBehavior = FillBehavior.HoldEnd };
        a.Completed += (_, _) =>
        {
            if (!Runs.TryGetValue(el, out var r) || r.Token != token) return;
            Runs.Remove(el);
            el.BeginAnimation(TProperty, null);
            step(1);
            done?.Invoke();
        };
        el.BeginAnimation(TProperty, a);
    }

    /// <summary>A course's eased point that goes a little past the end and settles back (a stamp, a pop).</summary>
    internal static double Back(double p, double over = 1.6)
    {
        double q = Math.Clamp(p, 0, 1) - 1;
        return 1 + q * q * ((over + 1) * q + over);
    }

    /* ---------------- the boss's HP: a red chunk on a big drop ---------------- */

    sealed class HpChunk { public Action<double>? Apply; public double Last = 1; }
    sealed class HpWatch { public string Fight = ""; public double Base = -1; public DateTime BaseAt; public double From, To, Right; public string Key = ""; public int N; public DateTime MarkedAt; }
    static readonly Dictionary<string, HpWatch> HpWatches = new();
    static readonly Queue<string> HpOrder = new();

    /// <summary>A drop of the boss's HP this big (a share of its HP) within <see cref="HpChunkWithin"/> leaves a red chunk.</summary>
    public const double HpChunkDrop = 0.03, HpChunkWithin = 1.0;
    /// <summary>The chunk: held a moment at the old HP, then it shrinks to the new and fades.</summary>
    public const double HpChunkMs = 400;

    static int _hpPlace;
    /// <summary>A key of its own for each place an HP bar is drawn (a look, a panel).</summary>
    internal static string HpPlace() => "hp" + (++_hpPlace).ToString(CultureInfo.InvariantCulture);

    /// <summary>
    /// The boss's HP drawn in <paramref name="track"/> (its fill a share of the track's width, from the left) at
    /// <paramref name="left"/> of its HP: a drop of <see cref="HpChunkDrop"/> or more within <see cref="HpChunkWithin"/> s
    /// leaves a red chunk from the old HP to the new, which holds, then shrinks onto the fill and fades (like a fighting game's
    /// health bar). The chunk outlives a track built again each draw. <paramref name="radius"/>: the chunk's corners;
    /// <paramref name="alpha"/>: how strong (a hairline strong, a header's fill light).
    /// </summary>
    internal static void HpGhost(Grid track, string place, string fightId, double left, DateTime now, double radius = 1.5, double alpha = 0.9)
    {
        if (!HpWatches.TryGetValue(place, out var w))
        {
            HpWatches[place] = w = new HpWatch { Fight = fightId, Base = left, BaseAt = now };
            HpOrder.Enqueue(place);
            while (HpOrder.Count > 32) HpWatches.Remove(HpOrder.Dequeue());   // places come and go (Settings' previews): the last few kept
        }
        else if (w.Fight != fightId) { w.Fight = fightId; w.Base = left; w.BaseAt = now; w.Key = ""; }
        bool running = w.Key.Length > 0 && (FrozenAt != null || (DateTime.UtcNow - w.MarkedAt).TotalMilliseconds < HpChunkMs);
        if ((now - w.BaseAt).TotalSeconds > HpChunkWithin || left > w.Base) { w.Base = left; w.BaseAt = now; }
        else if (w.Base - left >= HpChunkDrop && On)
        {
            // a drop while a chunk still shows carries on from where its red is now, so none of it vanishes at once
            w.From = running ? Math.Max(w.Base, w.Right) : w.Base; w.To = left; w.Key = $"{place}|{fightId}|{++w.N}";
            Mark(w.Key); w.MarkedAt = DateTime.UtcNow; running = true;
            w.Base = left; w.BaseAt = now;
        }
        // over (or none): nothing built, a chunk left from before put away
        if (!running || !Marked(w.Key))
        {
            foreach (UIElement ch in track.Children) if (ch is Border { Tag: HpChunk } old) old.Visibility = Visibility.Collapsed;
            return;
        }
        Border? chunk = null;
        foreach (UIElement ch in track.Children) if (ch is Border { Tag: HpChunk } b) { chunk = b; break; }
        if (chunk == null)
        {
            var holder = new HpChunk();
            chunk = new Border { Tag = holder, HorizontalAlignment = HorizontalAlignment.Left, IsHitTestVisible = false, CornerRadius = new CornerRadius(radius) };
            track.Children.Insert(Math.Min(1, track.Children.Count), chunk);
            // one handler for the chunk's life: a track laid out (or resized) draws it again where its course is
            track.SizeChanged += (_, _) => holder.Apply?.Invoke(holder.Last);
        }
        var h = (HpChunk)chunk.Tag;
        var c = Color.FromRgb(0xFF, 0x5C, 0x5C);
        var fill = new SolidColorBrush(Color.FromArgb((byte)(255 * alpha), c.R, c.G, c.B));
        chunk.Background = fill;
        chunk.Visibility = Visibility.Visible;
        double from = w.From, to = w.To;
        var el = chunk;
        h.Apply = p =>
        {
            h.Last = p;
            double tw = track.ActualWidth;
            double q = p < 0.3 ? 0 : Out((p - 0.3) / 0.7);
            double right = from + (to - from) * q;
            w.Right = right;
            el.Margin = new Thickness(Math.Max(0, tw * to), 0, 0, 0);
            el.Width = Math.Max(0, tw * (right - to));
            fill.Color = Color.FromArgb((byte)(255 * alpha * (1 - 0.7 * q)), c.R, c.G, c.B);
        };
        Resume(chunk, w.Key, HpChunkMs, h.Apply, () => el.Visibility = Visibility.Collapsed);
    }
}

/// <summary>
/// A figure whose digits roll on their own, like a car's odometer (the meter's DPS): each digit turns from the old to the
/// new one, up when the figure rose and down when it fell, the other marks ("." "K" "M") set at once. Every digit sits in a
/// cell of the same width (tabular figures), and the figure is right-aligned, so its column never moves; a new value while
/// a roll is under way turns on from where the digits are. Drawn without a roll when the tracker sits still.
/// </summary>
public sealed class Odometer : FrameworkElement
{
    /// <summary>A roll takes this long (the meter redraws every 50 ms, so a figure under fire chases its value).</summary>
    public const double RollMs = 260;

    readonly double _size;
    readonly Typeface _face;
    readonly SolidColorBrush _ink;
    readonly Color _inkColor;
    sealed class Cell { public char Ch; public double Pos, From, To; public bool Digit; public RectangleGeometry? Clip; }
    readonly List<Cell> _cells = new();   // the rightmost first; kept and changed in place, draw after draw
    readonly Dictionary<char, FormattedText> _glyphs = new();
    double _digitW = -1, _lineH;
    string _text = "";
    long _t0;
    bool _rolling;

    public Odometer(double size, FontWeight weight, Color ink)
    {
        _size = size;
        _face = new Typeface(HudKit.Text, FontStyles.Normal, weight, FontStretches.Normal);
        _inkColor = ink;
        _ink = new SolidColorBrush(ink);
        IsHitTestVisible = false;
        HorizontalAlignment = HorizontalAlignment.Right;
        VerticalAlignment = VerticalAlignment.Center;
        SnapsToDevicePixels = true;
    }

    /// <summary>The figure shown (where its roll ends).</summary>
    public string Text => _text;
    /// <summary>For the tests: whether a roll is under way.</summary>
    internal bool Rolling => _rolling;

    /// <summary>
    /// The figure: a change rolls to it (<paramref name="up"/>: the figure rose), unless the tracker sits still. A digit already
    /// near its new value (a roll under way) turns the short way to it; one further off turns the figure's way, as an odometer.
    /// </summary>
    public void Show(string text, bool up)
    {
        if (text == _text) return;
        bool had = _text.Length > 0;
        int len = text.Length;
        bool widthMoved = len != _cells.Count;
        bool roll = false;
        for (int i = 0; i < len; i++)
        {
            char ch = text[len - 1 - i];
            bool digit = ch is >= '0' and <= '9';
            Cell c;
            bool wasDigit;
            double wasPos;
            if (i < _cells.Count) { c = _cells[i]; wasDigit = c.Digit; wasPos = c.Pos; if (c.Digit != digit || c.Ch != ch) widthMoved |= !c.Digit || !digit; }
            else { c = new Cell(); _cells.Add(c); wasDigit = false; wasPos = 0; }
            c.Ch = ch; c.Digit = digit;
            if (!digit) continue;
            double d = ch - '0';
            if (wasDigit && had)
            {
                double from = Mod10(wasPos);
                double near = d - from;
                if (near > 5) near -= 10; else if (near <= -5) near += 10;
                double delta = Math.Abs(near) <= 1.5 ? near : up ? Mod10(d - from) : -Mod10(from - d);
                c.From = from; c.To = from + delta; c.Pos = from;
                if (Math.Abs(delta) > 0.001) roll = true;
            }
            else c.From = c.To = c.Pos = d;
        }
        if (_cells.Count > len) _cells.RemoveRange(len, _cells.Count - len);
        _text = text;
        if (widthMoved || _digitW < 0) InvalidateMeasure();
        roll &= Motion.On;
        if (!roll) { Stop(); Settle(); InvalidateVisual(); return; }
        if (Motion.FrozenAt is double at) { Step(at); if (at >= 1) Settle(); InvalidateVisual(); return; }
        _t0 = System.Diagnostics.Stopwatch.GetTimestamp();
        if (!_rolling) { _rolling = true; RollingNow.Add(this); if (RollingNow.Count == 1) CompositionTarget.Rendering += Frame; }
    }

    // one frame driver for every odometer rolling (the meter's rows): each frame moves them all, nothing is made per frame
    static readonly List<Odometer> RollingNow = new();

    static void Frame(object? sender, EventArgs e)
    {
        for (int i = RollingNow.Count - 1; i >= 0; i--)
        {
            var o = RollingNow[i];
            double p = System.Diagnostics.Stopwatch.GetElapsedTime(o._t0).TotalMilliseconds / RollMs;
            if (p >= 1 || !Motion.On) { o.Settle(); o._rolling = false; RollingNow.RemoveAt(i); }
            else o.Step(p);
            o.InvalidateVisual();
        }
        if (RollingNow.Count == 0) CompositionTarget.Rendering -= Frame;
    }

    void Step(double p)
    {
        double q = Motion.Out(p);
        foreach (var c in _cells) if (c.Digit) c.Pos = c.From + (c.To - c.From) * q;
    }

    void Stop()
    {
        if (!_rolling) return;
        _rolling = false;
        RollingNow.Remove(this);
        if (RollingNow.Count == 0) CompositionTarget.Rendering -= Frame;
    }

    void Settle() { foreach (var c in _cells) if (c.Digit) c.Pos = c.From = c.To = c.Ch - '0'; }

    static double Mod10(double v) => ((v % 10) + 10) % 10;

    /// <summary>A personal best: the figure flares gold, then settles back to its own colour.</summary>
    public void Flare(Color gold, double ms = 700)
    {
        if (!Motion.On) return;
        // its own element, so a roll under way goes on
        Motion.Play(_flareHost ??= new UIElement(), ms, p =>
        {
            double k = p < 0.2 ? p / 0.2 : 1 - Motion.Out((p - 0.2) / 0.8);
            _ink.Color = Mix(_inkColor, gold, k);
            InvalidateVisual();
        }, () => { _ink.Color = _inkColor; InvalidateVisual(); });
    }
    UIElement? _flareHost;
    /// <summary>For the tests and renders: the colour the figure is drawn in now.</summary>
    internal Color InkNow => _ink.Color;

    static Color Mix(Color a, Color b, double t) => Color.FromArgb(
        (byte)(a.A + (b.A - a.A) * t), (byte)(a.R + (b.R - a.R) * t), (byte)(a.G + (b.G - a.G) * t), (byte)(a.B + (b.B - a.B) * t));

    FormattedText Glyph(char ch)
    {
        if (_glyphs.TryGetValue(ch, out var ft)) return ft;
        double dip = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        ft = new FormattedText(ch.ToString(), CultureInfo.InvariantCulture, FlowDirection.LeftToRight, _face, _size, _ink, null, TextFormattingMode.Display, dip);
        _glyphs[ch] = ft;
        return ft;
    }

    void Metrics()
    {
        if (_digitW >= 0) return;
        _digitW = 0;
        for (char ch = '0'; ch <= '9'; ch++) _digitW = Math.Max(_digitW, Glyph(ch).WidthIncludingTrailingWhitespace);
        _lineH = Glyph('0').Height;
    }

    double CellW(Cell c) => c.Digit ? _digitW : Glyph(c.Ch).WidthIncludingTrailingWhitespace;

    protected override Size MeasureOverride(Size available)
    {
        Metrics();
        double w = 0;
        foreach (var c in _cells) w += CellW(c);
        return new Size(Math.Ceiling(w), Math.Ceiling(_lineH));
    }

    protected override void OnRender(DrawingContext dc)
    {
        Metrics();
        double x = ActualWidth, h = _lineH, top = Math.Round((ActualHeight - h) / 2);
        foreach (var c in _cells)
        {
            double w = CellW(c);
            x -= w;
            if (!c.Digit) { dc.DrawText(Glyph(c.Ch), new Point(x, top)); continue; }
            double pos = Mod10(c.Pos);
            int d0 = (int)Math.Floor(pos + 1e-6) % 10;
            double frac = pos - Math.Floor(pos + 1e-6);
            if (frac < 0.002 || frac > 0.998)
            {
                int d = frac > 0.998 ? (d0 + 1) % 10 : d0;
                var g = Glyph((char)('0' + d));
                dc.DrawText(g, new Point(x + (w - g.WidthIncludingTrailingWhitespace) / 2, top));
                continue;
            }
            // the turning digit: the one leaving goes up and fades, the next comes up from under it
            var clip = c.Clip ??= new RectangleGeometry();
            clip.Rect = new Rect(x - 1, top, w + 2, h);
            dc.PushClip(clip);
            var a = Glyph((char)('0' + d0)); var b = Glyph((char)('0' + (d0 + 1) % 10));
            dc.PushOpacity(1 - frac);
            dc.DrawText(a, new Point(x + (w - a.WidthIncludingTrailingWhitespace) / 2, top - frac * h * 0.8));
            dc.Pop();
            dc.PushOpacity(frac);
            dc.DrawText(b, new Point(x + (w - b.WidthIncludingTrailingWhitespace) / 2, top + (1 - frac) * h * 0.8));
            dc.Pop();
            dc.Pop();
        }
    }
}
