using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using KinahLedger.Session;

namespace KinahLedger.Ui;

/// <summary>
/// The timer line on top of the Kinah overlay (0.10.86, Steven 2026-11-14: "maybe we should just have the tracker kinah
/// [overlay] pop out at the top tiny"): while a watched timer is within the minutes set, one tiny line pops out at the
/// overlay's top edge, "Invasion 4:39 · Kashapa 11:59", soonest first, gold in its last 5 minutes; it folds away when
/// nothing is due. The overlay grows upward: as the line takes height the window moves up by as much, and down again when
/// it folds, so the Kinah line never moves. No animation: it is there or it isn't.
///
/// The stacking, bottom to top: the Kinah line, the Parser's section (Ui/HudWindow.Parser.cs, when it is in this build),
/// then this line, at the very top. AddTimerSection runs after AddParserSection so it wraps outermost. With Settings ›
/// Timers › Separate timer overlay on, or the Kinah overlay off, the line lives in its own strip (Ui/TimersOverlay.cs).
/// </summary>
public sealed partial class HudWindow
{
    /// <summary>The line's place above everything else in the overlay (its 4 px gap included, so the anchor counts it).</summary>
    readonly Border _timerSlot = new() { Visibility = Visibility.Collapsed, HorizontalAlignment = HorizontalAlignment.Left, Padding = new Thickness(0, 0, 0, 4) };
    readonly TextBlock _timerText = new() { FontSize = 11, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis, MaxWidth = 420 };

    static readonly Brush TimerInk = Frozen(Color.FromRgb(0xE4, 0xE7, 0xEE)), TimerGold = Frozen(Color.FromRgb(0xF5, 0xB9, 0x42)), TimerDot = Frozen(Color.FromRgb(0x86, 0x8D, 0xA1));

    /// <summary>At most this many timers on the line.</summary>
    public const int TimerLineMax = 3;

    /// <summary>Called at the end of the constructor (after the Parser's section, when there is one): the line goes above all.</summary>
    void AddTimerSection()
    {
        var own = (UIElement)Content;
        Face = own;
        Content = null;
        _timerSlot.Child = new Border
        {
            Child = _timerText, Padding = new Thickness(8, 2, 8, 3), CornerRadius = new CornerRadius(8),
            Background = Frozen(Color.FromArgb(0xE6, 0x0B, 0x0C, 0x10)), BorderBrush = FrameBrush, BorderThickness = new Thickness(1),
        };
        // flush with the Card's or the Ticker's edge, not its shadow: as close to the overlay as on the strip (Steven, 2026-11-14:
        // "i like the closest one to the tracker"). The line's own box reaches down over the look's empty inset, so the slot's
        // height (what the window moves up by) is only what it adds above the look's visible edge
        _timerSlot.Margin = new Thickness(_look?.Inset.Left ?? 0, 0, 0, 0);
        if (_timerSlot.Child is FrameworkElement line && _look != null) line.Margin = new Thickness(0, 0, 0, -_look.Inset.Top);
        var stack = new StackPanel();
        stack.Children.Add(_timerSlot);
        stack.Children.Add(own);
        Content = stack;
        // anchored at the bottom: the line's height comes off the top
        _timerSlot.SizeChanged += (_, e) =>
        {
            if (e.NewSize.Height <= 0) return;   // folding is FoldTimers' (a collapsed element says no size)
            double d = e.NewSize.Height - e.PreviousSize.Height;
            if (Math.Abs(d) > 0.5 && IsLoaded && !_still) Top -= d;
        };
    }

    /// <summary>The overlay's own content, under the timer line (what Content was before the line came).</summary>
    internal UIElement? Face { get; private set; }

    /// <summary>The height the timer line adds above the overlay now (0 when folded).</summary>
    public double TimerLineHeight => _timerSlot.Visibility == Visibility.Visible ? _timerSlot.ActualHeight : 0;

    /// <summary>Where the overlay's own content starts: Top without what is stacked above it. A remembered spot is this one.</summary>
    public double AnchorTop => Top + TimerLineHeight;

    /// <summary>True while the timer line shows.</summary>
    public bool TimersShown => _timerSlot.Visibility == Visibility.Visible;

    /// <summary>The line's text, for the options self-test.</summary>
    internal string TimerLineForTest => new TextRange(_timerText.ContentStart, _timerText.ContentEnd).Text;

    void FoldTimers()
    {
        double h = _timerSlot.ActualHeight;
        _timerSlot.Visibility = Visibility.Collapsed;
        if (h > 0 && IsLoaded && !_still) Top += h;
    }

    /// <summary>The watched timers due within the minutes set, soonest first (the first <see cref="TimerLineMax"/>); none folds the line.</summary>
    public void ShowTimers(IReadOnlyList<TimerBoard.Item> items, DateTime now)
    {
        if (items.Count == 0) { if (TimersShown) FoldTimers(); return; }
        _timerText.Inlines.Clear();
        foreach (var (i, n) in items.Take(TimerLineMax).Select((i, n) => (i, n)))
        {
            if (n > 0) _timerText.Inlines.Add(new Run(" · ") { Foreground = TimerDot });
            var left = i.AtPc is { } at ? at - now : TimeSpan.Zero;
            var tone = left <= TimeSpan.FromMinutes(TimerPrefs.GoldMinutes) ? TimerGold : TimerInk;
            _timerText.Inlines.Add(new Run($"{i.Short} {TimerBoard.Clock(left)}") { Foreground = tone });
        }
        System.Windows.Documents.Typography.SetNumeralAlignment(_timerText, FontNumeralAlignment.Tabular);
        // the line grows and shrinks with the overlay's size (Options › In game)
        _timerSlot.LayoutTransform = _content.LayoutTransform;
        _timerSlot.Visibility = Visibility.Visible;
    }
}
