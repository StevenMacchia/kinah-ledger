using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using static KinahLedger.Ui.HudKit;

namespace KinahLedger.Ui;

/// <summary>
/// Arcade's Buddy in a dungeon run (the "run*" values, MainWindow.HudRun.cs): a speedrun timer on the 1UP screen. Over the
/// screen, small, the dungeon and its mode with "Run 3 today · 38 clears"; on it the run clock big in LED yellow, mint ahead of
/// your fastest run's pace and coral behind, the pace and your best beside it, the last bosses' splits under it (a new best
/// split in gold). After the final boss NEW BEST in mint or "Cleared in 4:17 · 2nd fastest", and the run's worth in gold
/// against your average. The words are ModernRun's; the pickup strip (in pixel frames) and the timer chips sit at the
/// screen's end as on the farming line.
/// </summary>
sealed partial class HudArcade
{
    static readonly RunInk RunTones = new(Led, Soft, Quiet, Quiet, Mint2, Coral, Frozen(GoldC2), Frozen(GoldC2), 22, 10.5);

    /// <summary>The run's head, set into the Buddy's top edge after 1UP (collapsed out of a run).</summary>
    readonly TextBlock _runHead = RunHead();

    static TextBlock RunHead()
    {
        var t = Tt("", 10, Soft, FontWeights.SemiBold);
        t.HorizontalAlignment = HorizontalAlignment.Left; t.Margin = new Thickness(0);
        return t;
    }

    /// <summary>The run on the wash: the figures in the hour's place, the strip after them (the timer chips follow); the head over it.</summary>
    void DrawRun(RunBuddy.State s)
    {
        var lead = new Border { Width = 0 };
        _lineBody.Children.Add(lead);
        double cap = FightWidth - 10 - _lineBody.Margin.Left - _lineBody.Margin.Right - lead.Width;
        var words = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 1, 0, 1) };
        var big = Tt("", 22, Led, FontWeights.Bold);
        var small = Tt("", 10.5, Frozen(Mod.A(Colors.White, .86)), FontWeights.Medium);
        small.Margin = new Thickness(1, 1, 0, 0);
        words.Children.Add(big); words.Children.Add(small);

        var (fullW, needW) = ModernRun.Widths(big, small, s, RunTones);
        var strips = RunStrips(Strip);
        double timerW = LineTimerW();
        var (strip, up, room) = ModernRun.Place(cap, fullW, needW, timerW, strips, 12);
        _timerUp = up;
        _stripTimerW = strips.Count > 0 ? timerW : -1;
        ModernRun.Fill(big, small, s, room, RunTones);
        small.Visibility = small.Inlines.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        _lineBody.Children.Add(words);
        if (strip != null) { strip.Margin = new Thickness(12, 0, 0, 0); Pixelate(strip); _lineBody.Children.Add(strip); }

        // the head on its plate across the top edge, after the 1UP legend
        double left = _lineLegend.Margin.Left + Mod.W(_lineLegend) + 6;
        _runPlate.Margin = new Thickness(left, -1, 0, 0);
        ModernRun.Head(_runHead, s, FightWidth - 10 - left - 20, RunTones);
        _runPlate.Visibility = Visibility.Visible;
    }
}
