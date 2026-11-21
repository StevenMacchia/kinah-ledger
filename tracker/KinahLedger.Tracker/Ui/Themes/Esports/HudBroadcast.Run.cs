using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using static KinahLedger.Ui.HudKit;

namespace KinahLedger.Ui;

/// <summary>
/// Broadcast's Buddy in a dungeon run (the "run*" values, MainWindow.HudRun.cs; Steven, 2026-11-18: "Yes, all looks"): a
/// speedrun clock on the lower-third bar. Over the bar, on a slim navy tab with the cyan edge (a lower third's name tab), the
/// dungeon and its mode with "Run 3 today · 38 clears"; on the bar the run clock as its headline in condensed white, mint
/// ahead of your fastest run's pace and coral behind, the pace and your best beside it, the last bosses' splits under it (a
/// new best split in gold). After the final boss NEW BEST in mint or "Cleared in 4:17 · 2nd fastest", and the run's worth in
/// gold against your average. The words are ModernRun's; the pickup strip and the timer chips sit at the bar's end as on the
/// farming line.
/// </summary>
sealed partial class HudBroadcast
{
    static readonly RunInk Ink = new(White, Soft, Quiet, Quiet, Mint2, Coral, Frozen(GoldC2), Frozen(GoldC2), 26, 12);

    /// <summary>The run's head on its tab over the bar (collapsed out of a run).</summary>
    readonly TextBlock _runHead = RunHead();
    readonly Slab _runHeadSlab;

    static TextBlock RunHead()
    {
        var t = Bc("", 12, White, FontWeights.SemiBold);
        t.HorizontalAlignment = HorizontalAlignment.Left;
        return t;
    }

    /// <summary>The run on the bar: the figures in the hour's place, the strip after them (the timer chips follow); the head on its tab over it.</summary>
    void DrawRun(RunBuddy.State s)
    {
        var lead = new Border { Width = 6 };
        _lineBody.Children.Add(lead);
        double cap = FightWidth - 10 - _lineBody.Margin.Left - _lineBody.Margin.Right - lead.Width;
        var words = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        var big = Bc("", 26, White);
        big.Margin = new Thickness(0, -3, 0, -2);
        var small = Bc("", 12, Frozen(Mod.A(Colors.White, .84)), FontWeights.SemiBold);
        small.Margin = new Thickness(1, 0, 0, 0);
        words.Children.Add(big); words.Children.Add(small);

        var (fullW, needW) = ModernRun.Widths(big, small, s, Ink);
        var strips = RunStrips(Strip).Select(x => (FrameworkElement)StripPlate(x)).ToList();
        double timerW = LineTimerW();
        var (strip, up, room) = ModernRun.Place(cap, fullW, needW, timerW, strips, 12);
        _timerUp = up;
        _stripTimerW = strips.Count > 0 ? timerW : -1;
        ModernRun.Fill(big, small, s, room, Ink);
        small.Visibility = small.Inlines.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        _lineBody.Children.Add(words);
        if (strip != null) { strip.Margin = new Thickness(12, 0, 0, 0); _lineBody.Children.Add(strip); }

        ModernRun.Head(_runHead, s, FightWidth - 10 - _runHeadSlab.Margin.Left - 30, Ink, 12);
        _runHeadSlab.Opacity = _dim ? .66 : 1;
        _runHeadSlab.Visibility = Visibility.Visible;
    }
}
