using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using static KinahLedger.Ui.HudKit;

namespace KinahLedger.Ui;

/// <summary>
/// Rift's Buddy in a dungeon run (the "run*" values, MainWindow.HudRun.cs; Steven, 2026-11-18: "Yes, all looks"): a
/// speedrun timer on the same Abyss glass. Along its top, small, the dungeon and its mode with "Run 3 today · 38 clears";
/// under it the run clock big in starlight, mint ahead of your fastest run's pace and coral behind, the pace and your best
/// beside it, the last bosses' splits under it (a new best split in gold). After the final boss NEW BEST in mint or
/// "Cleared in 4:17 · 2nd fastest", and the run's worth in gold against your average; between runs today here. The words
/// are ModernRun's; the pickup strip and the timer chips sit at the line's end as on the farming line.
/// </summary>
sealed partial class HudRift
{
    static readonly RunInk RunK = new(White, Soft, Quiet, Quiet, Mint2, Coral, Gold2, Gold2, 23, 11);

    /// <summary>The run's head along the glass's top, lined up with the words under it (collapsed out of a run).</summary>
    readonly TextBlock _runHead = RunHead();

    static TextBlock RunHead()
    {
        var t = Tt("", 10.5, Lilac, FontWeights.SemiBold);
        t.HorizontalAlignment = HorizontalAlignment.Left; t.Margin = new Thickness(15, 17, 14, 0);
        t.Visibility = Visibility.Collapsed;
        return t;
    }

    /// <summary>The run on the glass: the figures in the hour's place, the strip after them (the timer chips follow); the head over them.</summary>
    void DrawRun(RunBuddy.State s)
    {
        _lineBody.Margin = new Thickness(8, 3, 14, 8);
        var lead = new Border { Width = 7 };
        _lineBody.Children.Add(lead);
        double cap = FightWidth - 8 - _lineBody.Margin.Left - _lineBody.Margin.Right - lead.Width;
        var words = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 0, 1) };
        var big = Tt("", 23, White, FontWeights.Bold);
        big.Effect = Glow(9, .7);
        var small = Tt("", 11, Soft, FontWeights.Medium);
        small.Margin = new Thickness(1, 1, 0, 0);
        words.Children.Add(big); words.Children.Add(small);

        var (fullW, needW) = ModernRun.Widths(big, small, s, RunK);
        var strips = RunStrips(Strip);
        double timerW = LineTimerW();
        var (strip, up, room) = ModernRun.Place(cap, fullW, needW, timerW, strips, 14);
        _timerUp = up;
        _stripTimerW = strips.Count > 0 ? timerW : -1;
        ModernRun.Fill(big, small, s, room, RunK);
        small.Visibility = small.Inlines.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        _lineBody.Children.Add(words);
        if (strip != null) { strip.Margin = new Thickness(14, 0, 0, 0); VioletRims(strip); _lineBody.Children.Add(strip); }

        ModernRun.Head(_runHead, s, FightWidth - 8 - _runHead.Margin.Left - _runHead.Margin.Right, RunK);
        _runHead.Visibility = Visibility.Visible;
    }
}
