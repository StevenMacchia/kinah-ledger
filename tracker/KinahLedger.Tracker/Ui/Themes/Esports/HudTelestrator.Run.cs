using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using static KinahLedger.Ui.HudKit;

namespace KinahLedger.Ui;

/// <summary>
/// Telestrator's Buddy in a dungeon run (the "run*" values, MainWindow.HudRun.cs): a speedrun timer on the same board. At
/// its top, small, the dungeon and its mode with "Run 3 today · 38 clears"; under it the run clock big in white, mint ahead
/// of your fastest run's pace and coral behind, the pace and your best beside it, the last bosses' splits under it (a new
/// best split in marker yellow). After the final boss NEW BEST in mint or "Cleared in 4:17 · 2nd fastest", and the run's
/// worth in yellow against your average; between runs today here. The words are ModernRun's; the pickup strip and the
/// timer tags sit at the board's end as on the farming line.
/// </summary>
sealed partial class HudTelestrator
{
    static readonly RunInk RunTone = new(White, Soft, Quiet, Quiet, Mint2, Coral, Frozen(PenC), Frozen(PenC), 22, 10.5);

    /// <summary>The run's head at the board's top, lined up with the words under it (collapsed out of a run).</summary>
    readonly TextBlock _runHead = RunHead();

    static TextBlock RunHead()
    {
        var t = Tt("", 10, Soft, FontWeights.SemiBold);
        t.HorizontalAlignment = HorizontalAlignment.Left; t.Margin = new Thickness(12, 6, 10, -3);
        t.Visibility = Visibility.Collapsed;
        return t;
    }

    /// <summary>The run on the board: the figures in the hour's place, the strip after them (the timer tags follow); the head over it.</summary>
    void DrawRun(RunBuddy.State s)
    {
        var lead = new Border { Width = 5 };
        _lineBody.Children.Add(lead);
        double cap = FightWidth - 10 - _lineBody.Margin.Left - _lineBody.Margin.Right - lead.Width;
        var words = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 1, 0, 1) };
        var big = Tt("", 22, White, FontWeights.Bold);
        var small = Tt("", 10.5, Frozen(Mod.A(Colors.White, .84)), FontWeights.Medium);
        small.Margin = new Thickness(1, 1, 0, 0);
        words.Children.Add(big); words.Children.Add(small);

        var (fullW, needW) = ModernRun.Widths(big, small, s, RunTone);
        var strips = RunStrips(Strip);
        double timerW = LineTimerW();
        var (strip, up, room) = ModernRun.Place(cap, fullW, needW, timerW, strips, 14);
        _timerUp = up;
        _stripTimerW = strips.Count > 0 ? timerW : -1;
        ModernRun.Fill(big, small, s, room, RunTone);
        small.Visibility = small.Inlines.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        _lineBody.Children.Add(words);
        if (strip != null) { strip.Margin = new Thickness(14, 0, 0, 0); _lineBody.Children.Add(strip); }

        ModernRun.Head(_runHead, s, FightWidth - 10 - _runHead.Margin.Left - _runHead.Margin.Right, RunTone);
        _runHead.Visibility = Visibility.Visible;
    }
}
