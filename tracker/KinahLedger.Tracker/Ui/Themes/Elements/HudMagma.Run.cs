using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using static KinahLedger.Ui.HudKit;

namespace KinahLedger.Ui;

/// <summary>
/// Magma's Buddy in a dungeon run (the "run*" values, MainWindow.HudRun.cs; Steven, 2026-11-18: "Yes, all looks"): a
/// speedrun timer on the same basalt slab. On a small basalt tab over it, the dungeon and its mode with "Run 3 today · 38
/// clears"; on the slab the run clock big in white-hot, mint ahead of your fastest run's pace and coral behind, the pace and
/// your best beside it, the last bosses' splits under it (a new best split in yellow-hot). After the final boss NEW BEST or
/// "Cleared in 4:17 · 2nd fastest", and the run's worth against your average; between runs today here. The words are
/// ModernRun's; the pickup strip (set in basalt) and the timer chips sit after them as on the farming line.
/// </summary>
sealed partial class HudMagma
{
    static readonly RunInk Ink = new(WhiteHot, Soft, Quiet, Quiet, Mint2, Coral, LavaYel, Frozen(GoldC2), 22, 10.5);

    /// <summary>The run's head on its tab over the slab (collapsed out of a run).</summary>
    readonly TextBlock _runHead = Tt("", 10.5, Soft, FontWeights.SemiBold);
    readonly Grid _runHeadHost;

    /// <summary>The run's head on a small basalt tab that sits on the slab's top edge.</summary>
    Grid RunHeadSlab(TextBlock head)
    {
        var g = new Grid { HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(10, 0, 0, -2) };
        g.Children.Add(new TacSketch((dc, s) => Slab(dc, s, SlabA, 17, cracks: false)));
        head.HorizontalAlignment = HorizontalAlignment.Left;
        head.Margin = new Thickness(8, 3, 9, 4);
        g.Children.Add(head);
        Panel.SetZIndex(g, 1);
        return g;
    }

    /// <summary>The run on the slab: the figures in the hour's place, the strip after them (the timer chips follow); the head on its tab.</summary>
    void DrawRun(RunBuddy.State s)
    {
        var lead = new Border { Width = 2 };
        _lineBody.Children.Add(lead);
        double cap = FightWidth - 10 - _lineBody.Margin.Left - _lineBody.Margin.Right - lead.Width;
        var words = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 0, 1) };
        var big = Tt("", 22, WhiteHot, FontWeights.Bold);
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
        if (strip != null) { strip.Margin = new Thickness(14, 0, 0, 0); Set(strip); _lineBody.Children.Add(strip); }

        ModernRun.Head(_runHead, s, FightWidth - 10 - _runHead.Margin.Left - 20, Ink);
        _runHeadHost.Opacity = _dim ? .62 : 1;
        _runHeadHost.Visibility = Visibility.Visible;
    }
}
