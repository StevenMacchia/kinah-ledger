using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Animation;
using KinahLedger.Parser;

namespace KinahLedger.Ui;

/// <summary>
/// The Parser inside the Kinah overlay (Steven, 2026-11-14: "expand by default but give option in settings to separate"):
/// when a record of the Parser's mode is under way, its section (Ui/ParserPanel: the boss, the clock, the members on their
/// class-coloured bars) sits above the Kinah line, and folds away after the fight once its result has shown a few seconds.
/// The overlay grows upward: as the section takes height, the window moves up by as much, so the Kinah line never moves.
/// The section fades in and out in 120 ms (not at all in a render); its figures follow every hit as the separate window's do.
/// </summary>
public sealed partial class HudWindow
{
    /// <summary>The section's place above the overlay's own content (its 6 px gap included, so the anchor counts it).</summary>
    readonly Border _parserSlot = new() { Visibility = Visibility.Collapsed, HorizontalAlignment = HorizontalAlignment.Left, Padding = new Thickness(0, 0, 0, 6) };
    ParserPanel? _parserPanel;
    bool _parserFolding;

    /// <summary>The overlay's own content, under the Parser's slot (what Content was before the section came).</summary>
    internal UIElement? OwnContent { get; private set; }

    /// <summary>Called at the end of the constructor: the overlay's content goes under the section's slot.</summary>
    void AddParserSection()
    {
        var own = (UIElement)Content;
        OwnContent = own;
        Content = null;
        var stack = new StackPanel();
        stack.Children.Add(_parserSlot);
        stack.Children.Add(own);
        Content = stack;
        // anchored at the bottom: the section's height comes off the top
        // (as it grows, and when it is shown: see SettleParser)
        _parserSlot.SizeChanged += (_, e) => { if (e.NewSize.Height > 0) SettleParser(); };
    }

    /// <summary>The height the section adds above the Kinah line now (0 when folded): a remembered spot is the Kinah line's.</summary>
    public double ParserHeight => _parserSlot.Visibility == Visibility.Visible ? _parserSlot.ActualHeight : 0;

    /// <summary>True while the section shows.</summary>
    public bool ParserShown => _parserSlot.Visibility == Visibility.Visible && !_parserFolding;

    /// <summary>The section away, the window down by its height: the Kinah line stays where it was.</summary>
    void Fold()
    {
        _parserSlot.Visibility = Visibility.Collapsed;
        double before = _timerTaken + _parserTaken;
        if (_parserTaken > 0) { _parserTaken = 0; Restack(before); }
    }

    /// <summary>The height the window has moved up by for the section (what folding gives back).</summary>
    double _parserTaken;

    /// <summary>
    /// The window up by what the section adds beyond what it has already taken. Counted against that, not the slot's last size:
    /// a section shown again at the size it had raises no size change, and the Kinah line would drop by its height.
    /// </summary>
    void SettleParser()
    {
        if (!IsLoaded || _still || _parserSlot.Visibility != Visibility.Visible) return;
        double before = _timerTaken + _parserTaken;
        if (Math.Abs(_parserSlot.ActualHeight - _parserTaken) > 0.5) { _parserTaken = _parserSlot.ActualHeight; Restack(before); }
    }

    /// <summary>The record to show above the Kinah line, or null to fold the section away.</summary>
    public void ShowParser(Encounter? e, DateTime now, ParserMode mode, bool details)
    {
        if (e == null)
        {
            if (_parserSlot.Visibility != Visibility.Visible || _parserFolding) return;
            if (_still || Motion.Still) { Fold(); return; }
            _parserFolding = true;
            var fade = new DoubleAnimation(0, TimeSpan.FromMilliseconds(120));
            fade.Completed += (_, _) => { if (_parserFolding) { Fold(); _parserFolding = false; } };
            _parserSlot.BeginAnimation(OpacityProperty, fade);
            return;
        }
        _parserPanel ??= new ParserPanel();
        if (_parserSlot.Child != _parserPanel) _parserSlot.Child = _parserPanel;
        _parserPanel.Mode = mode; _parserPanel.ShowDetails = details;
        _parserPanel.Draw(e, now);
        if (_parserSlot.Visibility == Visibility.Visible && !_parserFolding) return;
        _parserFolding = false;
        _parserSlot.Visibility = Visibility.Visible;
        if (IsLoaded && !_still) { UpdateLayout(); SettleParser(); }
        if (_still || Motion.Still) { _parserSlot.BeginAnimation(OpacityProperty, null); _parserSlot.Opacity = 1; return; }
        _parserSlot.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(120)));
    }
}
