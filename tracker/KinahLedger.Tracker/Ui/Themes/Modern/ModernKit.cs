using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using KinahLedger.Parser;
using static KinahLedger.Ui.HudKit;

namespace KinahLedger.Ui;

/// <summary>
/// What the Modern theme's looks share (Glass, Mono, Line, Fade; Steven, 2026-11-17, picked from HTML mock-ups): Inter, the
/// app's own face, for every word and figure; calm surfaces, one figure big per row, no ornament that moves the figures.
/// The small sums the looks make the same way live here, as the Tactical looks' do in TacticalLook; the drawn parts reuse
/// <see cref="TacSketch"/> (a part that paints itself, redrawn by a moment).
/// </summary>
abstract class ModernLook : HudThemeLook
{
    protected ModernLook(bool bare = false) : base(bare, Mod.Face) { }

    /// <summary>A course's part from <paramref name="a"/> to <paramref name="b"/>, 0 before it and 1 after.</summary>
    protected static double Seg(double p, double a, double b) => Math.Clamp((p - a) / (b - a), 0, 1);

    /// <summary>Whether the line has an hour to show (else the waiting line).</summary>
    protected static bool HasHour(Figures f) => f.Counted && f.Rate.Length > 0;

    /// <summary>The ring of your best is shown: an hour counted, not warming, a best here, out of the Abyss.</summary>
    protected static bool HasRing(Figures f) => HasHour(f) && f.Share >= 0 && !f.Warm && !f.Abyss;

    /// <summary>The share of your best as a whole percent ("91").</summary>
    protected static string Pct(double share) => Math.Round(Math.Clamp(share, 0, 9.99) * 100).ToString("0", CultureInfo.InvariantCulture);

    /// <summary>The meter's foot as the Parser's says it: the fight's total, the group's word and figure, the held average (the AVG mark).</summary>
    protected static (string Total, string GroupWord, string Group, bool Held) FootOf(Encounter e, DateTime now)
    {
        var (word, value, held) = ParserFoot.Right(e, now);
        return (Amounts.Meter(e.Credited), word, value, held);
    }

    /// <summary>A look's own foot marked as the meter's foot (its words read aloud as the Parser's are).</summary>
    protected static T AsFoot<T>(T foot, Encounter e, DateTime now) where T : FrameworkElement
    {
        foot.Tag = "parser-foot";
        System.Windows.Automation.AutomationProperties.SetName(foot, e.FootText(now));
        return foot;
    }

    /// <summary>A bar's share shown through <paramref name="apply"/>: slid there while effects play, else set.</summary>
    protected static void SlideBar(UIElement el, MeterContext c, string part, double share, Action<double> apply)
    {
        if (c.Moves) Motion.Slide(el, c.Key + "|" + part, share, apply);
        else apply(share);
    }

    /// <summary>
    /// Whether the Buddy wears the pickup strip (Ui/HudPickStrip.cs; Steven, 2026-11-18: "best drop + last three") in place
    /// of the latest pickup's words: farming with an hour counted and a pickup made, out of the Abyss, no rare drop held, the
    /// switch on (Settings › Buddy › More › Pickup icons). Off, the line is drawn as before.
    /// </summary>
    protected bool StripTime(Figures f) => PickStrip.Shown && HasHour(f) && !f.Abyss && !KinahHidden && Rare(Values) == null && Picks(Values).Any();

    /// <summary>
    /// The strip in <paramref name="room"/> px, or null where it doesn't fit (the line keeps the pickup in words). Measured as
    /// drawn, so a best drop that is one of the three takes no room.
    /// </summary>
    protected FrameworkElement? StripIn(double room, PickStrip.Look look) =>
        PickStrip.Of(Values, look) is { } s && Mod.W(s) <= room + .5 ? s : null;

    /// <summary>The strips a look tries, widest first: the best drop and the last three, then the last three alone.</summary>
    protected static PickStrip.Look[] StripFall(PickStrip.Look look) => new[] { look, look with { Best = false } };

    /// <summary>
    /// The strip's room on a Buddy line: the panel's widest (<paramref name="cap"/>, the meter's panel, or the line as it was
    /// with the pickup in words if that was wider) less its other parts (<paramref name="fixedW"/>), the words without the
    /// pickup (<paramref name="wordsW"/>) and the air before the strip. Never wider than the line was, nor than the meter.
    /// </summary>
    protected static double StripRoom(double cap, double fixedW, double wordsW, double oldWordsW, double air) =>
        Math.Max(cap - fixedW, oldWordsW) - wordsW - air;

    /// <summary>The dungeon run the Buddy shows in place of the hour (Ui/Themes/Modern/ModernRun.cs), or null: out of a run, Kinah hidden or a rare drop held.</summary>
    protected RunBuddy.State? RunNow() => !KinahHidden && Rare(Values) == null ? RunBuddy.Of(Values) : null;

    /// <summary>The run line's pickup strips, widest first (the best drop and the last three, then the last three); none with the icons off or no pickup.</summary>
    protected List<FrameworkElement> RunStrips(PickStrip.Look look) =>
        PickStrip.Shown && Picks(Values).Any() ? StripFall(look).Select(l => PickStrip.Of(Values, l)).Where(x => x != null).Select(x => x!).ToList() : new List<FrameworkElement>();

    /// <summary>A text in Inter (or <paramref name="face"/>), tabular digits, never cut.</summary>
    protected static TextBlock Tt(string s, double size, Brush fg, FontWeight weight, FontFamily? face = null) => Mod.Text(s, size, fg, weight, face);
}

/// <summary>The Modern theme's shared parts: the face, colour sums, pens and gradients, a rounded pill and a row's fit.</summary>
static class Mod
{
    /// <summary>Inter, shipped in the exe (the app's face since v2): the Modern looks' one face.</summary>
    public static readonly FontFamily Face = HudKit.Text;

    public static TextBlock Text(string s, double size, Brush fg, FontWeight weight, FontFamily? face = null)
    {
        var t = new TextBlock { Text = s, FontSize = size, Foreground = fg, FontFamily = face ?? Face, FontWeight = weight, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.None };
        System.Windows.Documents.Typography.SetNumeralAlignment(t, FontNumeralAlignment.Tabular);
        return t;
    }

    public static Color A(Color c, double a) => Color.FromArgb((byte)Math.Round(255 * Math.Clamp(a, 0, 1)), c.R, c.G, c.B);
    public static Color Mix(Color a, Color b, double t) => Tac.Mix(a, b, t);
    public static SolidColorBrush Br(Color c, double a = 1) => Frozen(A(c, a * c.A / 255.0));
    public static Pen P(Brush b, double w, bool round = false) => Tac.P(b, w, round);
    public static LinearGradientBrush Grad(bool down, params (Color C, double At)[] stops) => Tac.Grad(down, stops);

    /// <summary>A rounded rectangle (a pill when <paramref name="radius"/> is half its height), frozen.</summary>
    public static Geometry Pill(Rect r, double radius)
    {
        double rr = Math.Max(0, Math.Min(radius, Math.Min(r.Width, r.Height) / 2));
        var g = new RectangleGeometry(r, rr, rr);
        g.Freeze();
        return g;
    }

    /// <summary>A text's width as laid out alone (collapsed parts measured as if shown).</summary>
    public static double W(FrameworkElement e)
    {
        var v = e.Visibility; e.Visibility = Visibility.Visible;
        e.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        double w = e.DesiredSize.Width; e.Visibility = v;
        e.InvalidateMeasure();   // laid out again in its real room on the next pass (the measure above was unbounded)
        return w;
    }

    /// <summary>
    /// The Modern meter row's crowding rule (Steven, 2026-11-17): the Combat Power goes first, then the share; a name is cut
    /// only after both, and never below about ten characters (<paramref name="nameMin"/> px); figures are never cut. The
    /// marks (TOP DPS, YOU, deaths, heals) are kept: what's left of the room after them is the name's. Returns whether the CP
    /// and the share are shown.
    /// </summary>
    /// <param name="room">The width from the name's start to the DPS figure's left edge, with the share's column counted in.</param>
    /// <param name="shareW">The share's column (its width and the air before it), given back to the name when the share goes.</param>
    public static (bool Cp, bool Share) Fit(TextBlock name, double room, FrameworkElement? cp, IReadOnlyList<FrameworkElement> marks, double shareW, double nameMin)
    {
        name.ClearValue(FrameworkElement.MaxWidthProperty);
        double nw = W(name), cpw = cp is { } c && (c is not TextBlock tb || tb.Text.Length > 0) ? W(c) : 0, mw = marks.Sum(W);
        bool showCp = cpw > 0, showShare = true;
        double Need() => nw + (showCp ? cpw : 0) + mw + (showShare ? shareW : 0);
        if (Need() > room && showCp) showCp = false;
        if (Need() > room) showShare = false;
        double free = room - mw - (showShare ? shareW : 0);
        name.MaxWidth = Math.Max(Math.Min(nameMin, nw), free);
        return (showCp, showShare);
    }
}
