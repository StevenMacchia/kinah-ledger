using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace KinahLedger.Ui;

/// <summary>
/// The pop-out windows in Settings (Steven, 2026-11-18: "Yes, build pop-outs"): on the DPS meter page and the Applicants page,
/// "Open in its own window ›" under the page's "Show over the game" switch, for a second screen beside a game in exclusive
/// fullscreen (Ui/MainWindow.PopOuts.cs). In the head, so the page is no taller (every page fits without scrolling). It acts
/// at once, apart from Save.
/// </summary>
public partial class OptionsWindow
{
    internal Button? MeterPopOutBtn { get; private set; }
    internal Button? ApplicantsPopOutBtn { get; private set; }

    /// <summary>
    /// The link under the switch of the page's head (OverlayHeader's grid: its title on the left, the switch in its right column),
    /// found among what <paramref name="page"/> holds so far, whatever was added after it (the meter's blocker card, 1.0.17).
    /// </summary>
    void AddPopOutLink(Panel page, bool meter)
    {
        var head = page.Children.OfType<Grid>().FirstOrDefault(g => g.ColumnDefinitions.Count == 2 && g.Children.OfType<FrameworkElement>().Any(c => Grid.GetColumn(c) == 1 && HasSwitch(c)));
        var right = head?.Children.OfType<FrameworkElement>().FirstOrDefault(c => Grid.GetColumn(c) == 1);
        if (head == null || right == null) { CrashLog.Note("settings: the pop-out link found no page head"); return; }
        var words = new TextBlock { Text = L.T("Open in its own window") + "  ›", FontSize = 12, FontWeight = FontWeights.SemiBold };
        words.SetResourceReference(TextBlock.ForegroundProperty, "Link");
        var b = new Button
        {
            Content = words, Cursor = Cursors.Hand, Background = Brushes.Transparent, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 5, 0, 0),
            ToolTip = L.T("For a second screen. Works with the game in fullscreen."),
            Template = new ControlTemplate(typeof(Button)) { VisualTree = new FrameworkElementFactory(typeof(ContentPresenter)) },
        };
        b.MouseEnter += (_, _) => words.TextDecorations = TextDecorations.Underline;
        b.MouseLeave += (_, _) => words.TextDecorations = null;
        AutomationProperties.SetName(b, meter ? "Open the DPS meter in its own window" : "Open Applicants in its own window");
        b.Click += (_, _) => { if (meter) _host.OpenMeterPopOut(); else _host.OpenApplicantsPopOut(); };
        if (meter) MeterPopOutBtn = b; else ApplicantsPopOutBtn = b;
        // the switch's row, then the link under it, right-aligned, in the head's right column
        head.Children.Remove(right);
        right.HorizontalAlignment = HorizontalAlignment.Right;
        var col = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Children = { right, b } };
        Grid.SetColumn(col, 1);
        head.Children.Add(col);
    }

    /// <summary>The head's right column holds the page's "Show over the game" switch.</summary>
    static bool HasSwitch(DependencyObject d)
    {
        if (d is CheckBox) return true;
        foreach (var ch in LogicalTreeHelper.GetChildren(d).OfType<DependencyObject>()) if (HasSwitch(ch)) return true;
        return false;
    }
}
