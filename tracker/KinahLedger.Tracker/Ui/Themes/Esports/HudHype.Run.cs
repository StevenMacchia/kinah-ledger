using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using static KinahLedger.Ui.HudKit;

namespace KinahLedger.Ui;

/// <summary>
/// Hype's Buddy in a dungeon run (the "run*" values, MainWindow.HudRun.cs; Steven, 2026-11-18: "Yes, all looks"): a speedrun
/// timer on the Buddy's black plate. Over the plate, small and lit straight on the game, the dungeon and its mode with
/// "Run 3 today · 38 clears"; on it the run clock huge in chrome-lime italics, lime ahead of your fastest run's pace and
/// magenta behind, the pace and your best beside it, the last bosses' splits under it (a new best split in gold). After the
/// final boss NEW BEST in lime or "Cleared in 4:17 · 2nd fastest", and the run's worth in gold against your average; between
/// runs today here. The words are ModernRun's; the pickup strip (chrome tiles) and the timer chips sit at the plate's end as on
/// the farming line.
/// </summary>
sealed partial class HudHype
{
    static readonly RunInk Ink = new(ChromeLime, Soft, Quiet, Quiet, Lime, MagInk, Frozen(GoldC2), Frozen(GoldC2), 26, 11.5);

    /// <summary>The run's head over the plate, lined up with the words on it (collapsed out of a run).</summary>
    readonly TextBlock _runHead = RunHead();
    readonly Border _runHeadLit;

    static TextBlock RunHead()
    {
        var t = Ht("", 12, Soft, FontWeights.Bold);
        t.HorizontalAlignment = HorizontalAlignment.Left; t.Margin = new Thickness(13, 0, 0, 2);
        return t;
    }

    /// <summary>The run on the plate: the figures in the hour's place, the strip after them (the timer chips follow); the head over it.</summary>
    void DrawRun(RunBuddy.State s)
    {
        var lead = new Border { Width = 4 };
        _lineBody.Children.Add(lead);
        double cap = FightWidth - 10 - _lineBody.Margin.Left - _lineBody.Margin.Right - 4 - lead.Width;
        var words = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        var big = Ht("", 26, White, FontWeights.Bold);
        big.Margin = new Thickness(0, -3, 0, -2);
        var small = Ht("", 11.5, Frozen(Mod.A(Colors.White, .86)), FontWeights.SemiBold);
        small.Margin = new Thickness(1, 1, 0, 0);
        words.Children.Add(big); words.Children.Add(small);

        var (fullW, needW) = ModernRun.Widths(big, small, s, Ink);
        var strips = RunStrips(Strip);
        foreach (var st in strips) ChromeTiles(st, Strip.Size);
        double timerW = LineTimerW();
        var (strip, up, room) = ModernRun.Place(cap, fullW, needW, timerW, strips, 12);
        _timerUp = up;
        _stripTimerW = strips.Count > 0 ? timerW : -1;
        ModernRun.Fill(big, small, s, room, Ink);
        small.Visibility = small.Inlines.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        _lineBody.Children.Add(words);
        if (strip != null) { strip.Margin = new Thickness(12, 0, 0, 0); _lineBody.Children.Add(strip); }

        ModernRun.Head(_runHead, s, FightWidth - 10 - 4 - 19, Ink, 12);
        _runHeadLit.Opacity = _dim ? .62 : 1;
        _runHeadLit.Visibility = Visibility.Visible;
    }
}
