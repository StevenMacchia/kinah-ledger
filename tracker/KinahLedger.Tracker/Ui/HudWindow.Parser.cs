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
    /// <summary>A click (no drag) on the fight's meter: the window switches it compact or expanded.</summary>
    public event Action? FightClicked;

    /// <summary>Whether a press was on the fight: the Parser's section, or the look's own fight.</summary>
    bool InFight(DependencyObject src)
    {
        for (var d = src; d != null; d = d is System.Windows.Media.Visual or System.Windows.Media.Media3D.Visual3D ? System.Windows.Media.VisualTreeHelper.GetParent(d) : LogicalTreeHelper.GetParent(d))
            if (d == _parserSlot || d is ParserRows || (_look is HudStackLook sl && d == sl.FightFrame)) return true;
        return false;
    }

    /// <summary>The section's place above the overlay's own content (its 6 px gap included, so the anchor counts it).</summary>
    readonly Border _parserSlot = new() { Visibility = Visibility.Collapsed, HorizontalAlignment = HorizontalAlignment.Left, Padding = new Thickness(0, 0, 0, 6) };
    ParserPanel? _parserPanel;
    Border? _parserRing;
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
        if (HudGlass.OldLooks && _look == null) { _parserSlot.Padding = new Thickness(0); _parserSlot.HorizontalAlignment = HorizontalAlignment.Stretch; if (own is FrameworkElement of) of.HorizontalAlignment = HorizontalAlignment.Stretch; }
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
        EndMorph();
        _parserSlot.Visibility = Visibility.Collapsed;
        ComposeStack();
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

    /// <summary>True while the meter replaces the Kinah line (a dungeon run, Ui/MainWindow.Parser.cs).</summary>
    public bool KinahHidden { get; private set; }

    /// <summary>
    /// In a Transcendence, an Expedition or a Sanctuary the meter replaces the Kinah line for the whole run (Steven,
    /// 2026-11-14: "the parser should REPLACE the kinah tracker and that should be hidden during expeditions"): the looks that
    /// draw the fight above it (Auto, and Badge, Buddy and Pop with the Parser's section) put their Kinah part away; the
    /// Capsule, the Meter and the Float hide their Kinah figures. A timer due stays in sight (the timer line, or inside the
    /// meter). The Kinah line's spot is the anchor the meter sits on, so it comes back where it was.
    /// </summary>
    public void SetKinahHidden(bool on)
    {
        if (on == KinahHidden) return;
        KinahHidden = on;
        bool swaps = (_look == null || _look is IHudFightAbove || _look is not HudStackLook) && OwnContent != null;
        // Parser v2: the pill's picture, for the meter to grow out of (Ui/HudWindow.Morph.cs)
        var pill = on && swaps && _parserSlot.Visibility == Visibility.Visible ? SnapOwn() : null;
        var pillSize = OwnContent is FrameworkElement of ? new Size(of.ActualWidth, of.ActualHeight) : Size.Empty;
        if (_look is HudStackLook sl) sl.HideKinah(on);
        if (swaps)
            OwnContent!.Visibility = on ? Visibility.Collapsed : Visibility.Visible;
        if (pill != null) MorphIn(pill, pillSize);
        else if (!on && swaps) MorphOut();
    }

    /// <summary>The record to show above the Kinah line, or null to fold the section away.</summary>
    public void ShowParser(Encounter? e, DateTime now, ParserMode mode, bool details)
    {
        // Auto (Ui/HudStackLooks.cs): its fight panel goes above the pill in the Parser's slot, so the pill never moves
        UIElement? above = null;
        if (_look is IHudFightAbove fa) { above = fa.FightAbove(e, now); if (above == null) e = null; }
        else if (_look is IHudStackLook own) { own.ShowFight(e, now); return; }   // the Capsule, the Meter and the Float draw the fight themselves
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
        if (above != null) { if (SlotContent != above) SetSlot(above); ShowSlot(); return; }
        if (_parserPanel == null)
        {
            // the Kinah overlay's own frame: its corner, its padding, its background at its opacity
            _parserPanel = new ParserPanel(glass: HudGlass.OldLooks);
            _parserPanel.SetFrame(_frame.CornerRadius.TopLeft, _frame.Padding);
            if (_parserOpacity >= 0) _parserPanel.SetOpacity(_parserOpacity);
        }
        UIElement slotChild = HudGlass.OldLooks ? (_parserRing ??= HudGlass.Ring(_parserPanel)) : _parserPanel;
        if (SlotContent != slotChild) SetSlot(slotChild);
        _parserPanel.Mode = mode; _parserPanel.ShowDetails = details;
        _parserPanel.Draw(e, now);
        ShowSlot();
    }

    /// <summary>The slot shown (if it isn't): the window up by its height, a 120 ms fade in.</summary>
    void ShowSlot()
    {
        if (_parserSlot.Visibility == Visibility.Visible && !_parserFolding) return;
        _parserFolding = false;
        _parserSlot.Visibility = Visibility.Visible;
        ComposeStack();
        if (IsLoaded && !_still) { UpdateLayout(); SettleParser(); }
        if (_still || Motion.Still) { _parserSlot.BeginAnimation(OpacityProperty, null); _parserSlot.Opacity = 1; return; }
        _parserSlot.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(120)));
    }
}
