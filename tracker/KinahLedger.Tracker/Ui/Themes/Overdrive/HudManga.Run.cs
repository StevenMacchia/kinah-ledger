using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using static KinahLedger.Ui.HudKit;

namespace KinahLedger.Ui;

/// <summary>
/// Manga's Buddy in a dungeon run (the "run*" values, MainWindow.HudRun.cs): the speech bubble holds the run clock huge in
/// Impact ink, red when behind your fastest run's pace, the pace and your best beside it, the last bosses' splits under it
/// (a new best split in red). The dungeon, its mode and "Run 3 today · 38 clears" sit over the bubble in a black narration
/// box. The words are ModernRun's; the pickup strip and the timer captions sit at the bubble's end as on the farming line.
/// </summary>
sealed partial class HudManga
{
    /// <summary>The run's inks in the bubble: ink on paper, the one red for behind and for a new best.</summary>
    static readonly RunInk PaperInk = new(Frozen(Color.FromRgb(0x0A, 0x0A, 0x0D)), Frozen(Mod.A(Color.FromRgb(0x0A, 0x0A, 0x0D), .76)), Frozen(Mod.A(Color.FromRgb(0x0A, 0x0A, 0x0D), .58)),
        Frozen(Mod.A(Color.FromRgb(0x0A, 0x0A, 0x0D), .4)), Frozen(Color.FromRgb(0x0A, 0x0A, 0x0D)), Frozen(Color.FromRgb(0xE8, 0x1C, 0x2B)), Frozen(Color.FromRgb(0xE8, 0x1C, 0x2B)),
        Frozen(Color.FromRgb(0xE8, 0x1C, 0x2B)), 26, 10.5);
    /// <summary>The run head's inks in its narration box: white on ink.</summary>
    static readonly RunInk BoxInk = new(Frozen(Colors.White), Frozen(Mod.A(Colors.White, .82)), Frozen(Mod.A(Colors.White, .64)), Frozen(Mod.A(Colors.White, .45)),
        Frozen(Colors.White), Frozen(Color.FromRgb(0xFF, 0x5A, 0x64)), Frozen(Color.FromRgb(0xFF, 0x5A, 0x64)), Frozen(Color.FromRgb(0xFF, 0x5A, 0x64)), 26, 10.5);

    /// <summary>The run's head in its narration box over the bubble (collapsed out of a run).</summary>
    readonly TextBlock _runHead = RunHead();
    readonly Border _runCaption;

    static TextBlock RunHead()
    {
        var t = Tt("", 10.5, Frozen(Mod.A(Colors.White, .82)), FontWeights.Bold);
        t.HorizontalAlignment = HorizontalAlignment.Left;
        return t;
    }

    /// <summary>The run in the bubble: the figures in the hour's place, the strip after them (the timer captions follow); the head over it.</summary>
    void DrawRun(RunBuddy.State s)
    {
        var lead = new Border { Width = 3 };
        _lineBody.Children.Add(lead);
        double cap = FightWidth - 10 - _lineBody.Margin.Left - _lineBody.Margin.Right - lead.Width;
        var words = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        var big = Mod.Text("", 24, PaperInk.Hero, FontWeights.Normal, FigFace);
        big.Margin = new Thickness(0, -3, 0, -3);
        var small = Tt("", 10.5, PaperInk.Soft, FontWeights.SemiBold);
        small.Margin = new Thickness(1, 1, 0, 0);
        words.Children.Add(big); words.Children.Add(small);

        var (fullW, needW) = ModernRun.Widths(big, small, s, PaperInk);
        var strips = RunStrips(Strip);
        double timerW = LineTimerW();
        var (strip, up, room) = ModernRun.Place(cap, fullW, needW, timerW, strips, 12);
        // the strip beside would cut the run's words short (the delta to your best): it goes under them instead
        FrameworkElement? under = null;
        if (strip != null && room < fullW - .5 && StripUnder(Strip, false) is { } su)
        {
            under = su; strip = null;
            (_, up, room) = ModernRun.Place(cap, fullW, needW, timerW, new List<FrameworkElement>(), 12);
        }
        _timerUp = up;
        _stripTimerW = strips.Count > 0 ? timerW : -1;
        ModernRun.Fill(big, small, s, room, PaperInk);
        small.Visibility = small.Inlines.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        if (under != null) words.Children.Add(under);
        _lineBody.Children.Add(words);
        if (strip != null) { strip.Margin = new Thickness(12, 0, 0, 0); _lineBody.Children.Add(strip); }

        ModernRun.Head(_runHead, s, FightWidth - 10 - 30, BoxInk);
        _runCaption.Opacity = _dim ? .66 : 1;
        _runCaption.Visibility = _runHead.Inlines.Count > 0 || _runHead.Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
    }
}
