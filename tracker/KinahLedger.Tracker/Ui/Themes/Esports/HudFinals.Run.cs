using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using static KinahLedger.Ui.HudKit;

namespace KinahLedger.Ui;

/// <summary>
/// Finals' Buddy in a dungeon run (the "run*" values, MainWindow.HudRun.cs; Steven, 2026-11-18: "Yes, all looks"): a
/// speedrun clock on the same black plate. At its top in small gold caps the dungeon and its mode with "Run 3 today · 38
/// clears"; under it the run clock as a gold headline (mint ahead of your fastest run's pace, coral behind), the pace and your
/// best beside it, the last bosses' splits under it (a new best split in gold). After the final boss NEW BEST in mint or
/// "Cleared in 4:17 · 2nd fastest", and the run's worth in gold against your average. The words are ModernRun's; the pickup
/// strip and the timer chips sit at the line's end as on the farming line.
/// </summary>
sealed partial class HudFinals
{
    // ahead of your pace and NEW BEST in gold (the look's colour of winning), behind in coral
    static readonly RunInk Ink = new(GoldInk, Soft, Quiet, Faint, AuLight, Coral, AuLight, AuLight, 26, 12);

    /// <summary>The run's head at the plate's top, over the clock (in the Buddy's body only during a run).</summary>
    readonly TextBlock _runHead = RunHead();

    static TextBlock RunHead()
    {
        var t = T("", 12, AuSoft, FontWeights.SemiBold, cond: true);
        t.HorizontalAlignment = HorizontalAlignment.Left; t.Margin = new Thickness(2, 0, 0, 3);
        return t;
    }

    /// <summary>The run on the plate: the figures in the hour's place, the strip after them (the timer chips follow); the head over them.</summary>
    void DrawRun(RunBuddy.State s)
    {
        _lineBody.Margin = new Thickness(12, 10, 14, 7);
        double cap = FightWidth - 8 - _lineBody.Margin.Left - _lineBody.Margin.Right;
        var words = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        var big = T("", 26, GoldInk, FontWeights.SemiBold, cond: true);
        big.Effect = new System.Windows.Media.Effects.DropShadowEffect { Color = Colors.Black, ShadowDepth = 1, Direction = 270, BlurRadius = 4, Opacity = .8 };
        var small = T("", 12, Soft, FontWeights.Normal);
        small.Margin = new Thickness(1, 0, 0, 0);
        words.Children.Add(big); words.Children.Add(small);

        var (fullW, needW) = ModernRun.Widths(big, small, s, Ink);
        var strips = RunStrips(Strip);
        double timerW = LineTimerW();
        var (strip, up, room) = ModernRun.Place(cap, fullW, needW, timerW, strips, 14);
        _timerUp = up;
        _stripTimerW = strips.Count > 0 ? timerW : -1;
        ModernRun.Fill(big, small, s, room, Ink);
        small.Visibility = small.Inlines.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        _lineRow.Children.Add(words);
        if (strip != null) { strip.Margin = new Thickness(14, 0, 0, 0); GoldTiles(strip); _lineRow.Children.Add(strip); }

        ModernRun.Head(_runHead, s, cap, Ink, 12);
    }
}
