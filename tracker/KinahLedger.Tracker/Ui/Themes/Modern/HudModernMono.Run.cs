using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using static KinahLedger.Ui.HudKit;

namespace KinahLedger.Ui;

/// <summary>
/// Mono's Buddy in a dungeon run (the "run*" values, MainWindow.HudRun.cs; Steven, 2026-11-18: "Yes, all looks"): a
/// speedrun timer in the same near-black panel. Over a hairline, small, the dungeon and its mode with "Run 3 today · 38
/// clears"; under it the run clock big in off-white, green ahead of your fastest run's pace and red behind, the pace and your
/// best beside it, the last bosses' splits under it (a new best split in gold). After the final boss NEW BEST in green or
/// "Cleared in 4:17 · 2nd fastest", and the run's worth against your average; between runs today here. The words are
/// ModernRun's; the pickup strip and the timer's box sit at the right as on the farming line.
/// </summary>
sealed partial class HudModernMono
{
    static readonly SolidColorBrush RunGold = Frozen(Color.FromRgb(0xF5, 0xC5, 0x42));
    static readonly RunInk Ink = new(Paper, Soft, Grey, Grey, Green, Red, RunGold, Green, 24, 11.5);

    /// <summary>The run's panel: the head and a hairline over the whole width, then the figures, the strip and the timers.</summary>
    void DrawRun(RunBuddy.State s, bool dim)
    {
        var row = new Grid { Margin = new Thickness(4, 0, 0, 0) };
        for (int i = 0; i < 4; i++) row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });   // (no ring), words, strip, timers
        double cap = FightWidth - 8 - 2 - _lineBody.Margin.Left - _lineBody.Margin.Right - row.Margin.Left;
        var words = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        var big = Tt("", 24, Paper, FontWeights.Bold);
        big.Margin = new Thickness(0, -2, 0, -1);
        var small = Tt("", 11.5, Soft, FontWeights.Medium);
        small.Margin = new Thickness(0, 2, 0, 0);
        words.Children.Add(big); words.Children.Add(small);

        var (fullW, needW) = ModernRun.Widths(big, small, s, Ink);
        var strips = RunStrips(Strip);
        double timerW = LineTimerW();
        var (strip, up, room) = ModernRun.Place(cap, fullW, needW, timerW, strips, 14);
        _timerUp = up;
        _stripTimerW = strips.Count > 0 ? timerW : -1;
        ModernRun.Fill(big, small, s, room, Ink);
        small.Visibility = small.Inlines.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        if (strip != null)
        {
            strip.Margin = new Thickness(14, 0, 0, 0); strip.VerticalAlignment = VerticalAlignment.Center;
            if (dim) strip.Opacity = .5;
            Grid.SetColumn(strip, 2); row.Children.Add(strip);
        }
        Grid.SetColumn(words, 1); row.Children.Add(words);
        Grid.SetColumn(_timersBuddy, 3);
        if (_timersBuddy.Parent is Panel old) old.Children.Remove(_timersBuddy);
        row.Children.Add(_timersBuddy);

        // the head over the panel's width, a hairline under it as over the fight's rows
        var head = Tt("", 10.5, Grey, FontWeights.SemiBold);
        head.Margin = new Thickness(4, 0, 0, 0);
        ModernRun.Head(head, s, Math.Max(cap, Mod.W(row) - row.Margin.Left), Ink);
        if (dim) words.Opacity = head.Opacity = .55;
        _lineBody.Children.Add(head);
        _lineBody.Children.Add(Hairline(new Thickness(4, 6, 0, 7)));
        _lineBody.Children.Add(row);
    }
}
