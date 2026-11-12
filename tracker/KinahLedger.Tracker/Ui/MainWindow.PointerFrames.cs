using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;

namespace KinahLedger.Ui;

/// <summary>
/// Renders of the motion a player's hand starts (KL_MOTION_AT with ui-shot --demo), which a render never sees on its own:
/// a click on a named button (KL_CLICK) and the pointer resting on a card or a tile (KL_HOVER).
/// </summary>
public partial class MainWindow
{
    /// <summary>
    /// A render of a click's motion (KL_MOTION_AT with KL_CLICK and ui-shot --demo): the button of that x:Name clicked on the
    /// sample window, its motion held at that point: EventsTab turns the page to Runs as its tab's light slides there,
    /// TodayWeekBtn (with --events) slides the Today strip's light to Week. True when it took the frame (the other effects are left out of it, so the
    /// click's motion reads alone).
    /// </summary>
    bool ClickFrameForRender()
    {
        if (Environment.GetEnvironmentVariable("KL_CLICK") is not { Length: > 0 } name || FindName(name) is not ButtonBase button) return false;
        button.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
        return true;
    }

    /// <summary>
    /// A render of the hover (KL_MOTION_AT with KL_HOVER=lift, tile or lift,tile and ui-shot --demo): the pointer comes on the
    /// first card that lifts and the first item tile on the page, their lift and the tile's passing light held at that point
    /// (Ui/Motion.Feel.cs). The elements' own hover colours show as they do under the mouse. True when it took the frame.
    /// </summary>
    bool HoverFrameForRender()
    {
        if (Environment.GetEnvironmentVariable("KL_HOVER") is not { Length: > 0 } asked) return false;
        foreach (var name in asked.Split(','))
        {
            var feel = name.Trim() == "tile" ? Feel.Tile : Feel.Lift;
            if (Under(LedgerCol).FirstOrDefault(e => Motion.GetFeel(e) == feel && e.Cursor == Cursors.Hand && e.IsVisible && e.ActualWidth > 0) is { } el)
                el.RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice, 0) { RoutedEvent = Mouse.MouseEnterEvent });
        }
        return true;
    }

    /// <summary>Every element under <paramref name="root"/> in the order drawn, top to bottom of the page.</summary>
    internal static IEnumerable<FrameworkElement> Under(DependencyObject root)
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var c = VisualTreeHelper.GetChild(root, i);
            if (c is FrameworkElement fe) yield return fe;
            foreach (var d in Under(c)) yield return d;
        }
    }
}
