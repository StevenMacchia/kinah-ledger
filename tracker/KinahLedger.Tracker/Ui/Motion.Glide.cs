using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace KinahLedger.Ui;

/// <summary>
/// The lit pill glides (2026-11-11, Steven: "cool and flashy"): a page tab or an in-page switch (Today / Week, This zone /
/// Today / Week, This session / All time, Days / Zones) moved to another button slides its light from the old button to the
/// new one instead of the light jumping. The styles that light a button (NavOn and SegOn in Theme.xaml) set
/// <see cref="LitProperty"/>; when a button turns lit, its light is drawn for the slide on the switch's own row, behind the
/// buttons (the row's background, so nothing is measured again), and the button takes its own light back as it arrives:
/// at rest the window is drawn exactly as it was without the slide. Each label's ink changes as the light passes halfway, so
/// a label reads against what is behind it for nearly all of the slide. A switch drawn again with new buttons on its click
/// (Compare, Where, Share) slides too: the switch with the same words remembers which of them was lit.
/// </summary>
public static partial class Motion
{
    /// <summary>Set by the lit styles (NavOn, SegOn): this button holds its switch's light.</summary>
    public static readonly DependencyProperty LitProperty = DependencyProperty.RegisterAttached("Lit", typeof(bool), typeof(Motion),
        new PropertyMetadata(false, (d, e) => { if (d is Button b && (bool)e.NewValue) b.Dispatcher.BeginInvoke(DispatcherPriority.Normal, () => LightArrived(b)); }));
    public static bool GetLit(DependencyObject d) => (bool)d.GetValue(LitProperty);
    public static void SetLit(DependencyObject d, bool value) => d.SetValue(LitProperty, value);

    /// <summary>
    /// Set by the switch style (SegOff, so SegOn and NavOn too): a click on the button is noted, so the light slides only
    /// for the player's own pick (a switch drawn again by that click too), never for a restyle the player didn't make (the
    /// page following what they do, a new day, the prefs loading).
    /// </summary>
    public static readonly DependencyProperty SwitchProperty = DependencyProperty.RegisterAttached("Switch", typeof(bool), typeof(Motion),
        new PropertyMetadata(false, (d, e) =>
        {
            if (d is not Button b) return;
            b.Click -= SwitchClicked;
            if ((bool)e.NewValue) b.Click += SwitchClicked;
        }));
    public static bool GetSwitch(DependencyObject d) => (bool)d.GetValue(SwitchProperty);
    public static void SetSwitch(DependencyObject d, bool value) => d.SetValue(SwitchProperty, value);

    /// <summary>The light's slide from one button to the next takes this long.</summary>
    public const double GlideMs = 220;
    /// <summary>A switch lit or drawn again this soon after the player picked is that pick's answer, and slides.</summary>
    const long ClickAnswerMs = 600;
    /// <summary>A switch's new row gets this many layout passes to be drawn before its light is shown still.</summary>
    const int GlideLayoutPasses = 12;

    // the button lit last on each switch's row, and on each switch by its words ("Days|Zones") for one drawn again with new buttons
    static readonly ConditionalWeakTable<Panel, StrongBox<int>> RowLit = new();
    static readonly Dictionary<string, int> WordsLit = new();
    // the last click on a switch's button: its words and when (its row may be gone by the time the switch is drawn again)
    static (string Label, long At) _switchClick;
    // when the player last picked a switch's button or a page (a click, a shortcut): only then does a light slide
    static long _pickedAt = long.MinValue / 2;

    /// <summary>The player picked a page by hand (a tab, Ctrl+1 to 4, a link to a page): its tab's light slides there.</summary>
    public static void PickedByHand() => _pickedAt = Environment.TickCount64;

    /// <summary>One slide under way on a row: the light's shape (for a slide that starts from where this one is) and its end.</summary>
    sealed class Light { public required RectangleGeometry Shape; public required Action End; }
    static readonly ConditionalWeakTable<Panel, Light> Lights = new();

    static void SwitchClicked(object sender, RoutedEventArgs e)
    {
        if (sender is not Button b) return;
        _switchClick = (SwitchWords(b), Environment.TickCount64);
        PickedByHand();
    }

    /// <summary>A button's words as the switch shows them ("Farm", "This zone").</summary>
    static string SwitchWords(Button b) => b.Content switch { string s => s, TextBlock t => t.Text, _ => b.Name };

    /// <summary>
    /// On screen, with the size and place its last layout gave it. A restyle marks the layout stale, but the lit and the unlit
    /// styles measure the same, so the last layout's places are the ones the next will give.
    /// </summary>
    static bool ShownNow(FrameworkElement el) => el.IsVisible && el.ActualWidth > 0;

    /// <summary>
    /// A button just took its switch's light (after the caller has restyled the whole switch, before the next frame): the
    /// light slides from the button lit before when the player has just picked (a click, a page by hand) and that one was on
    /// screen in the same row, or the switch was drawn again on a click on it. Anything else (the switch's first drawing, a
    /// page set up before it is shown, a restyle the player didn't make) just remembers it.
    /// </summary>
    static void LightArrived(Button to)
    {
        if (!GetLit(to) || VisualTreeHelper.GetParent(to) is not Panel row) return;
        var buttons = row.Children.OfType<Button>().ToList();
        int at = buttons.IndexOf(to);
        // the switch's words, its buttons' in order: what names it from one drawing to the next
        string words = string.Join("|", buttons.Select(SwitchWords));
        bool sameRow = RowLit.TryGetValue(row, out var onRow);
        bool drawnBefore = WordsLit.TryGetValue(words, out var onWords);
        int before = sameRow ? onRow!.Value : onWords;
        RowLit.AddOrUpdate(row, new StrongBox<int>(at));
        WordsLit[words] = at;
        if (!(sameRow || drawnBefore) || before == at || before >= buttons.Count || !On || Environment.TickCount64 - _pickedAt > ClickAnswerMs) return;
        var from = buttons[before];
        if (sameRow) { if (ShownNow(row) && ShownNow(from) && ShownNow(to)) GlideLight(row, from, to); return; }
        if (_switchClick.Label != SwitchWords(to) || Environment.TickCount64 - _switchClick.At > ClickAnswerMs) return;
        // a switch drawn again: its new row is laid out before the frame that shows it, and the light starts there
        EventHandler? laid = null;
        int passes = 0;
        laid = (_, _) =>
        {
            if (!ShownNow(row) || !ShownNow(from) || !ShownNow(to)) { if (++passes > GlideLayoutPasses) row.LayoutUpdated -= laid; return; }
            row.LayoutUpdated -= laid;
            if (GetLit(to)) GlideLight(row, from, to);
        };
        row.LayoutUpdated += laid;
    }

    /// <summary>
    /// Where a button's light is drawn in its row: its face (the template's border "b") inside its edge, with the face's corner,
    /// as WPF's Border draws a background (the edge rounded to the screen's pixels when the layout is). From the layout, not
    /// the screen, so a button pressed (scaled) at the moment gives the place it springs back to.
    /// </summary>
    internal static (Rect Box, double Radius)? LightBox(Button b)
    {
        if (b.Template?.FindName("b", b) is not Border face) return null;
        double edge = face.BorderThickness.Left;
        if (face.UseLayoutRounding) { double dpi = VisualTreeHelper.GetDpi(face).DpiScaleX; edge = Math.Round(edge * dpi) / dpi; }
        var at = VisualTreeHelper.GetOffset(b);
        var box = new Rect(at.X + edge, at.Y + edge, Math.Max(0, b.ActualWidth - 2 * edge), Math.Max(0, b.ActualHeight - 2 * edge));
        return (box, Math.Max(0, face.CornerRadius.TopLeft - edge / 2));
    }

    /// <summary>
    /// The slide: the new button's own light is hidden while a copy of it moves along the row's background from the old button
    /// (or from where a slide under way has it) to the new one, eased; the inks swap at the middle; at the end the button's own
    /// light shows again and the row is as it was. Only the light's rectangle changes on each frame: nothing is measured.
    /// </summary>
    static void GlideLight(Panel row, Button from, Button to)
    {
        if (Lights.TryGetValue(row, out var under)) under.End();
        // a row that draws a background of its own, or a button lit some other way, is left to show the light as it is
        if (row.Background != null || to.ReadLocalValue(Control.BackgroundProperty) != DependencyProperty.UnsetValue) return;
        if (LightBox(from) is not { } a || LightBox(to) is not { } z || to.Background is not { } light) return;
        var start = under?.Shape.Rect ?? a.Box;
        var shape = new RectangleGeometry(start, z.Radius, z.Radius);
        var layer = new DrawingBrush(new GeometryDrawing(light, null, shape))
        {
            Stretch = Stretch.None, ViewboxUnits = BrushMappingMode.Absolute, ViewportUnits = BrushMappingMode.Absolute,
            Viewbox = new Rect(row.RenderSize), Viewport = new Rect(row.RenderSize), AlignmentX = AlignmentX.Left, AlignmentY = AlignmentY.Top,
        };
        // the inks: the new label keeps the unlit ink and the old one the lit ink until the light is halfway (only when the
        // styles give them; an ink set on a button by its page is left alone)
        bool inks = to.ReadLocalValue(Control.ForegroundProperty) == DependencyProperty.UnsetValue && from.ReadLocalValue(Control.ForegroundProperty) == DependencyProperty.UnsetValue;
        var litInk = to.Foreground; var offInk = from.Foreground;
        bool swapped = !inks;
        void SwapInks() { if (swapped) return; swapped = true; to.ClearValue(Control.ForegroundProperty); from.ClearValue(Control.ForegroundProperty); }
        if (inks) { to.Foreground = offInk; from.Foreground = litInk; }
        to.Background = Brushes.Transparent;
        row.Background = layer;
        Light? self = null;
        void Finish()
        {
            if (self == null) return;
            self = null;
            Lights.Remove(row);
            SwapInks();
            to.ClearValue(Control.BackgroundProperty);
            if (row.Background == layer) row.ClearValue(Panel.BackgroundProperty);
        }
        self = new Light { Shape = shape, End = Finish };
        Lights.AddOrUpdate(row, self);
        Play(row, GlideMs, p =>
        {
            double q = Out(p);
            shape.Rect = new Rect(start.X + (z.Box.X - start.X) * q, start.Y + (z.Box.Y - start.Y) * q,
                start.Width + (z.Box.Width - start.Width) * q, start.Height + (z.Box.Height - start.Height) * q);
            if (q >= 0.5) SwapInks();
        }, Finish);
    }

    /// <summary>For options-test: the light's place in its row while it slides, or null when no slide is under way there.</summary>
    internal static Rect? LightNow(Panel row) => Lights.TryGetValue(row, out var l) ? l.Shape.Rect : null;
}
