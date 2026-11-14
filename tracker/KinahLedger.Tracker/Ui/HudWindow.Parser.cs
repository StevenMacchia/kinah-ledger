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

    double _parserOpacity = -1;
    void ParserOpacity(double opacity) { _parserOpacity = opacity; _parserPanel?.SetOpacity(opacity); }

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
        _parserSlot.SizeChanged += (_, e) =>
        {
            if (e.NewSize.Height <= 0) return;   // folding is Fold's (a collapsed element says no size)
            double d = e.NewSize.Height - e.PreviousSize.Height;
            if (Math.Abs(d) > 0.5 && IsLoaded && !_still) Top -= d;
        };
    }

    /// <summary>The height the section adds above the Kinah line now (0 when folded): a remembered spot is the Kinah line's.</summary>
    public double ParserHeight => _parserSlot.Visibility == Visibility.Visible ? _parserSlot.ActualHeight : 0;

    /// <summary>True while the section shows.</summary>
    public bool ParserShown => _parserSlot.Visibility == Visibility.Visible && !_parserFolding;

    /// <summary>The section away, the window down by its height: the Kinah line stays where it was.</summary>
    void Fold()
    {
        double h = _parserSlot.ActualHeight;
        _parserSlot.Visibility = Visibility.Collapsed;
        if (h > 0 && IsLoaded && !_still) Top += h;
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
        if (_parserPanel == null)
        {
            // the Kinah overlay's own frame: its corner, its padding, its background at its opacity
            _parserPanel = new ParserPanel();
            _parserPanel.SetFrame(_frame.CornerRadius.TopLeft, _frame.Padding);
            if (_parserOpacity >= 0) _parserPanel.SetOpacity(_parserOpacity);
        }
        if (_parserSlot.Child != _parserPanel) _parserSlot.Child = _parserPanel;
        _parserPanel.Mode = mode; _parserPanel.ShowDetails = details;
        _parserPanel.Draw(e, now);
        if (_parserSlot.Visibility == Visibility.Visible && !_parserFolding) return;
        _parserFolding = false;
        _parserSlot.Visibility = Visibility.Visible;
        if (_still || Motion.Still) { _parserSlot.BeginAnimation(OpacityProperty, null); _parserSlot.Opacity = 1; return; }
        _parserSlot.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(120)));
    }
}
