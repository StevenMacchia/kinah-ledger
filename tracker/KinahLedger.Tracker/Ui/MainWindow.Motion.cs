using System.Windows;

namespace KinahLedger.Ui;

public partial class MainWindow
{
    /// <summary>
    /// A render of the motion (KL_MOTION_AT=0..1 with ui-shot): the sample window with its effects held at that point of their
    /// course, since a render never sees a change. The hero hour rolls up from 85% of its figure, tinted; the bag strip's second
    /// slot lands as a Legend and its third gets one more; the LOOT tiles' second lands as a Unique.
    /// </summary>
    internal void MotionFrameForRender()
    {
        var key = "hero|" + _part + "|" + CountWhere();
        if (Motion.LastOf(key) is double v && v > 0) Motion.Replay(HeadRate, key, v * 0.85, v, x => Fk(x), 0.01);
        var bag = BagStripHost.Children.OfType<FrameworkElement>().ToList();
        if (bag.Count > 1) Motion.Land(bag[1], false, "Legend");
        if (bag.Count > 2) Motion.Land(bag[2], true, null);
        var tiles = LootHost.Children.OfType<System.Windows.Controls.Panel>().SelectMany(p => p.Children.OfType<System.Windows.Controls.WrapPanel>()).SelectMany(w => w.Children.OfType<FrameworkElement>()).ToList();
        if (tiles.Count > 1) Motion.Land(tiles[1], false, "Unique");
        // 0.10.44: the chase bar slides up from 60% of its share, the chart draws in, the first Next up row washes as if just reached
        if (ChaseBox.Visibility == Visibility.Visible)
        {
            double share = _chaseShare;
            Motion.ReplaySlide(ChaseFill, ChaseKey, share * 0.6, share, s => { _chaseShare = s; LayoutChase(); });
        }
        if (ChartBox.Visibility == Visibility.Visible) Motion.Reveal(RateCanvas, RateCanvas.Width, RateCanvas.Height);
        if (GoalsHost.Children.OfType<System.Windows.Controls.Border>().FirstOrDefault() is { } row) Motion.Wash(row, "Gold");
    }
}
