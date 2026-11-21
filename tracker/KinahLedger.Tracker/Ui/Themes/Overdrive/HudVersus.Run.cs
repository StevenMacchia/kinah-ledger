using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using static KinahLedger.Ui.HudKit;

namespace KinahLedger.Ui;

/// <summary>
/// Versus's Buddy in a dungeon run (the "run*" values, MainWindow.HudRun.cs; Steven, 2026-11-18: "Yes, all looks"): a
/// speedrun's timer on the score plate. Over the plate, small and shaded, the dungeon and its mode with "Run 3 today · 38
/// clears"; on it the run clock big and leaning, mint ahead of your fastest run's pace and coral behind, the pace and your
/// best beside it, the last bosses' splits under it (a new best split in gold). After the final boss NEW BEST in mint or
/// "Cleared in 4:17 · 2nd fastest", and the run's worth in gold against your average; between runs today here. The words
/// are ModernRun's; the pickup strip and the timer chips sit at the plate's end as on the farming line.
/// </summary>
sealed partial class HudVersus
{
    static readonly RunInk Ink = new(White, Soft, Quiet, Quiet, Mint, Coral, Yellow, Yellow, 24, 10.5);

    /// <summary>The run's head over the plate, lined up with the words on it (collapsed out of a run).</summary>
    readonly TextBlock _runHead = RunHead();
    readonly TextBlock _runHeadLit;

    static TextBlock RunHead()
    {
        var t = T("", 10.5, Soft, FontWeights.SemiBold, italic: true);
        t.HorizontalAlignment = HorizontalAlignment.Left; t.Margin = new Thickness(14, 0, 0, 3);
        return t;
    }

    /// <summary>The run on the plate: the figures in the hour's place, the strip after them (the timer chips follow); the head over it.</summary>
    void DrawRun(RunBuddy.State s)
    {
        double cap = FightWidth - 10 - _lineBody.Margin.Left - _lineBody.Margin.Right;
        var words = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        var big = T("", 24, White, FontWeights.Bold, italic: true);
        var small = T("", 10.5, Frozen(Mod.A(Colors.White, .86)), FontWeights.Medium);
        small.Margin = new Thickness(1, 1, 0, 0);
        words.Children.Add(big); words.Children.Add(small);

        var (fullW, needW) = ModernRun.Widths(big, small, s, Ink);
        var strips = RunStrips(Strip);
        double timerW = LineTimerW();
        var (strip, up, room) = ModernRun.Place(cap, fullW, needW, timerW, strips, 12);
        _timerUp = up;
        _stripTimerW = strips.Count > 0 ? timerW : -1;
        ModernRun.Fill(big, small, s, room, Ink);
        small.Visibility = small.Inlines.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        _lineBody.Children.Add(words);
        if (strip != null) { strip.Margin = new Thickness(12, 0, 0, 0); _lineBody.Children.Add(strip); }

        ModernRun.Head(_runHead, s, FightWidth - 10 - _runHead.Margin.Left, Ink);
        _runHeadLit.Opacity = _dim ? .62 : 1;
        _runHeadLit.Visibility = Visibility.Visible;
    }
}
