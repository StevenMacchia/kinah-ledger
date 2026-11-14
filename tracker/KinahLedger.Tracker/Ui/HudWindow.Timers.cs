using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using KinahLedger.Session;

namespace KinahLedger.Ui;

/// <summary>
/// The timer line on top of the Kinah overlay (Steven 2026-11-14: "maybe we should just have the tracker kinah
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

    /// <summary>Called at the end of the constructor, after the Parser's section: the line goes above it, at the top of the same stack.</summary>
    void AddTimerSection()
    {
        var lineFrame = new Border
        {
            Child = _timerText, Padding = new Thickness(8, 2, 8, 3), CornerRadius = new CornerRadius(8),
            Background = Frozen(Color.FromArgb(0xE6, 0x0B, 0x0C, 0x10)), BorderBrush = FrameBrush, BorderThickness = new Thickness(1),
        };
        if (HudGlass.OldLooks)
        {
            // the glass, the panel's own padding, a step bigger type; on the strip it is the stack's top part
            var (_, pad) = FrameShape(_fields);
            HudGlass.Apply(lineFrame, 0.94, new CornerRadius(_look == null ? HudGlass.Radius : 9));
            lineFrame.Padding = _look == null ? new Thickness(pad.Left, 4, pad.Right, 4) : new Thickness(9, 3, 9, 3);
            _timerText.FontSize = 11.5;
        }
        _timerSlot.Child = HudGlass.OldLooks ? _timerRing = HudGlass.Ring(lineFrame, lineFrame.CornerRadius) : lineFrame;
        if (HudGlass.OldLooks && _look == null) { _timerSlot.Padding = new Thickness(0); _timerSlot.HorizontalAlignment = HorizontalAlignment.Stretch; }
        // flush with the Card's or the Ticker's edge, not its shadow: as close to the overlay as on the strip (Steven, 2026-11-14:
        // "i like the closest one to the tracker"). The line's own box reaches down over the look's empty inset, so the slot's
        // height (what the window moves up by) is only what it adds above the look's visible edge
        _timerSlot.Margin = new Thickness(_look?.Inset.Left ?? 0, 0, 0, 0);
        FitTimerLine();
        if (Content is StackPanel stack) stack.Children.Insert(0, _timerSlot);   // the Parser's stack: the timer line, its section, the Kinah line
        else
        {
            var own = (UIElement)Content;
            OwnContent = own;
            Content = null;
            Content = new StackPanel { Children = { _timerSlot, own } };
        }
        // anchored at the bottom: the line's height comes off the top (as it grows, and when it is shown: see SettleTimerLine)
        _timerSlot.SizeChanged += (_, e) => { if (e.NewSize.Height > 0) SettleTimerLine(); };
    }

    /// <summary>The height the timer line adds above the overlay now (0 when folded).</summary>
    public double TimerLineHeight => _timerSlot.Visibility == Visibility.Visible ? _timerSlot.ActualHeight : 0;

    /// <summary>Where the overlay's own content starts: Top without the timer line and the Parser's section. A remembered spot is this one.</summary>
    public double AnchorTop => StackAnchorValid ? _stackAnchor!.Value : Top + _timerTaken + _parserTaken;

    // The Kinah line's exact top while sections stack above it. Top is set from it, not nudged by each change: Windows puts the
    // window on whole pixels, and nudging would let the Kinah line creep a pixel with each show and fold
    double? _stackAnchor;
    double _stackTopSet;
    bool StackAnchorValid => _stackAnchor != null && Math.Abs(Top - _stackTopSet) <= 1.5;   // moved further: dragged

    /// <summary>What the timer line and the Parser's section take above the Kinah line changed: the window's top from the anchor.</summary>
    void Restack(double takenBefore)
    {
        if (!StackAnchorValid) _stackAnchor = Top + takenBefore;
        _stackTopSet = _stackAnchor!.Value - _timerTaken - _parserTaken;
        Top = _stackTopSet;
    }

    /// <summary>
    /// Flush on what is right under the line: over the look's empty inset when on the Kinah overlay, not into the Parser's
    /// section while it shows (its own gap under the line).
    /// </summary>
    void FitTimerLine()
    {
        if (HudGlass.OldLooks && _timerSlot.Child is Border r) { if (_look != null) r.Margin = new Thickness(0, 0, 0, _parserSlot.Visibility == Visibility.Visible ? 0 : 4); return; }
        if (_timerSlot.Child is not FrameworkElement line || _look == null) return;
        double under = _parserSlot.Visibility == Visibility.Visible ? 0 : -_look.Inset.Top;
        if (line.Margin.Bottom != under) line.Margin = new Thickness(0, 0, 0, under);
    }

    /// <summary>True while the timer line shows.</summary>
    public bool TimersShown => _timerSlot.Visibility == Visibility.Visible;

    /// <summary>The line's text, for the options self-test.</summary>
    internal string TimerLineForTest => new TextRange(_timerText.ContentStart, _timerText.ContentEnd).Text;

    /// <summary>The height the window has moved up by for the line (what folding gives back).</summary>
    double _timerTaken;

    /// <summary>
    /// The window up by what the line adds beyond what it has already taken. Counted against that, not the slot's last size:
    /// a line shown again at the size it had raises no size change, and the Kinah line would drop by its height.
    /// </summary>
    void SettleTimerLine()
    {
        if (!IsLoaded || _still || _timerSlot.Visibility != Visibility.Visible) return;
        double before = _timerTaken + _parserTaken;
        if (Math.Abs(_timerSlot.ActualHeight - _timerTaken) > 0.5) { _timerTaken = _timerSlot.ActualHeight; Restack(before); }
    }

    void FoldTimers()
    {
        _timerSlot.Visibility = Visibility.Collapsed;
        ComposeStack();
        double before = _timerTaken + _parserTaken;
        if (_timerTaken > 0) { _timerTaken = 0; Restack(before); }
    }

    /// <summary>The watched timers due within the minutes set, soonest first (the first <see cref="TimerLineMax"/>); none folds the line.</summary>
    public void ShowTimers(IReadOnlyList<TimerBoard.Item> items, DateTime now)
    {
        if (_look is IHudStackLook own) { own.ShowTimers(items, now); return; }   // the Capsule, the Meter and the Float draw their own
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
        FitTimerLine();
        if (TimersShown) return;
        _timerSlot.Visibility = Visibility.Visible;
        ComposeStack();
        if (IsLoaded && !_still) { UpdateLayout(); SettleTimerLine(); }
    }
}
