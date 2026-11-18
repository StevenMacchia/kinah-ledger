using System.Windows;
using System.Windows.Controls;
using static KinahLedger.Ui.HudKit;

namespace KinahLedger.Ui;

/// <summary>
/// Line's Buddy in a dungeon run (the "run*" values, MainWindow.HudRun.cs; Steven, 2026-11-18: "Yes, all looks"): a
/// speedrun timer in the same hairline pill. Over the pill, small and shaded straight on the game, the dungeon and its mode
/// with "Run 3 today · 38 clears"; in it the run clock big in white, mint ahead of your fastest run's pace and red behind, the
/// pace and your best beside it, the last bosses' splits under it (a new best split in gold). After the final boss NEW BEST in
/// mint or "Cleared in 4:17 · 2nd fastest", and the run's worth in gold against your average; between runs today here. The
/// words are ModernRun's; the pickup strip and the timer chip sit at the pill's end as on the farming line.
/// </summary>
sealed partial class HudModernLine
{
    static readonly RunInk Ink = new(WhiteB, SoftB, DimB, FaintB, MintB, RedB, Frozen(GoldC0), Frozen(GoldC0), 22, 11.5);

    /// <summary>The run's head over the pill, lined up with the words in it (collapsed out of a run).</summary>
    readonly TextBlock _runHead = RunHead();

    static TextBlock RunHead()
    {
        var t = Tt("", 10.5, SoftB, FontWeights.SemiBold);
        t.Effect = ShadeFx; t.HorizontalAlignment = HorizontalAlignment.Left; t.Margin = new Thickness(14, 0, 0, 3); t.Visibility = Visibility.Collapsed;
        return t;
    }

    /// <summary>The run in the pill: no ring, the figures in the hour's place, the strip and the timer chip after them; the head over it.</summary>
    void DrawRun(RunBuddy.State s, Figures f)
    {
        _ringHost.Visibility = Visibility.Collapsed;
        _pill.Padding = new Thickness(13, 5, 7, 5);
        _pick.Inlines.Clear();
        _stripSlot.Child = null;
        double cap = FightWidth - 14 - 2 - _pill.Padding.Left - _pill.Padding.Right - _kv.Margin.Left - _kv.Margin.Right;
        var (fullW, needW) = ModernRun.Widths(_rate, _sub, s, Ink);
        var strips = RunStrips(Strip);
        double timerW = TimerChipW();
        var (strip, up, room) = ModernRun.Place(cap, fullW, needW, timerW, strips, 16);
        _timerUp = up;
        _stripTimerW = strips.Count > 0 ? timerW : -1;
        ModernRun.Fill(_rate, _sub, s, room, Ink);
        if (strip != null)
        {
            strip.Margin = new Thickness(10, 0, timerW > 0 && !up ? 0 : 6, 0);
            _stripSlot.Child = strip;
        }
        _stripSlot.Visibility = _stripSlot.Child != null ? Visibility.Visible : Visibility.Collapsed;
        _sub.Visibility = _subLine.Visibility = _sub.Inlines.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        _pick.Visibility = Visibility.Collapsed;

        ModernRun.Head(_runHead, s, FightWidth - 14 - _runHead.Margin.Left, Ink);
        _runHead.Visibility = Visibility.Visible;
        _kv.Opacity = _stripSlot.Opacity = _runHead.Opacity = _paused ? .5 : 1;
        DrawTimerChip();
        DrawUnder(f);
    }
}
