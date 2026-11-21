using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using static KinahLedger.Ui.HudKit;

namespace KinahLedger.Ui;

/// <summary>
/// Asmodian's Buddy in a dungeon run (the "run*" values, MainWindow.HudRun.cs; Steven, 2026-11-18: "Yes, all looks"): a
/// speedrun timer on the same iron plate. On a small iron tab riveted over the plate, the dungeon and its mode with "Run 3
/// today · 38 clears"; on the plate the run clock big in the heavy face over its red underglow, mint ahead of your fastest
/// run's pace and coral behind, the pace and your best beside it, the last bosses' splits under it (a new best split in
/// ember gold). After the final boss NEW BEST in mint or "Cleared in 4:17 · 2nd fastest", and the run's worth in gold
/// against your average; between runs today here. The words are ModernRun's; the pickup strip and the timer chips sit at
/// the plate's end as on the farming line.
/// </summary>
sealed partial class HudAsmodian
{
    static readonly RunInk Ink = new(White, Soft, Quiet, Quiet, Mint2, Coral, Frozen(Color.FromRgb(0xFF, 0xC4, 0x6A)), Frozen(Color.FromRgb(0xFF, 0xC4, 0x6A)), 24, 10.5);

    /// <summary>The run's head on its tab over the plate (collapsed out of a run).</summary>
    readonly TextBlock _runHead = RunHead();
    readonly Grid _runHeadHost;

    static TextBlock RunHead()
    {
        var t = Tt("", 10.5, Soft, FontWeights.SemiBold);
        t.HorizontalAlignment = HorizontalAlignment.Left;
        return t;
    }

    /// <summary>The tab the run's head sits on: a small iron plate joined to the Buddy's top edge.</summary>
    Grid RunHeadPlate(TextBlock head)
    {
        var g = new Grid { HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(LineRoom.Left + 18, 0, 0, -LineRoom.Top - 1) };
        g.Children.Add(new TacSketch((dc, s) => Iron(dc, s, _opacity, big: 5, small: 0, thorns: false)));
        head.Margin = new Thickness(G + 8, G + 2, G + 9, G + 4);
        g.Children.Add(head);
        Panel.SetZIndex(g, 1);
        return g;
    }

    /// <summary>The run on the plate: the figures in the hour's place, the strip after them (the timer chips follow); the head on its tab.</summary>
    void DrawRun(RunBuddy.State s)
    {
        var lead = new Border { Width = 2 };
        _lineBody.Children.Add(lead);
        double cap = FightWidth - 10 - _lineBody.Margin.Left - _lineBody.Margin.Right - lead.Width;
        var words = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 1, 0, 1) };
        var big = Big("", 24, White, FontWeights.Bold);
        var small = Tt("", 10.5, Soft, FontWeights.Medium);
        small.Margin = new Thickness(1, 1, 0, 0);
        words.Children.Add(new Border { Child = big, Effect = Underglow(.7), HorizontalAlignment = HorizontalAlignment.Left }); words.Children.Add(small);

        var (fullW, needW) = ModernRun.Widths(big, small, s, Ink);
        var strips = RunStrips(Strip);
        double timerW = LineTimerW();
        var (strip, up, room) = ModernRun.Place(cap, fullW, needW, timerW, strips, 14);
        _timerUp = up;
        _stripTimerW = strips.Count > 0 ? timerW : -1;
        ModernRun.Fill(big, small, s, room, Ink);
        small.Visibility = small.Inlines.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        _lineBody.Children.Add(words);
        if (strip != null) { strip.Margin = new Thickness(14, 0, 0, 0); Forge(strip); _lineBody.Children.Add(strip); }

        ModernRun.Head(_runHead, s, FightWidth - 10 - 40, Ink);
        _runHeadHost.Opacity = _dim ? .62 : 1;
        _runHeadHost.Visibility = Visibility.Visible;
    }
}
