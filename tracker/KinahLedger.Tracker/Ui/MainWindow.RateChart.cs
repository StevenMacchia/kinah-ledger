using System.Windows;

namespace KinahLedger.Ui;

public partial class MainWindow
{
    /// <summary>The live rate chart over the hero's chart box (Ui/RateChart.cs), made with its first draw.</summary>
    RateChart? _rateChart;

    RateChart LiveChart() => _rateChart ??= new RateChart(RateCanvas, RateFill, RateLinePath, RateDot, BestLinePath, BestLineLbl);

    /// <summary>A render of the chart (KL_SHOT_STATE=chart or chart-hover): set by the state sample, read by the motion frame.</summary>
    bool _chartShot;

    /// <summary>
    /// KL_SHOT_STATE=chart: the hero's chart open over the sample's 42 minutes, as a click on "chart" opens it; with
    /// KL_MOTION_AT the motion frame lands its newest minute instead of drawing it in. chart-hover: the mouse on the minute
    /// two thirds along, its crosshair and readout up.
    /// </summary>
    internal void ShowChartSample(string state)
    {
        _chartShot = true;
        _qOpen["chart"] = true;
        // shown as it is once drawn in: KL_MOTION_AT would otherwise hold the draw-in's clip over the frame
        ChartBox.Visibility = Visibility.Visible;
        Redraw();
        if (state == "chart-hover" && _rateLine.Samples.Count >= 2) LiveChart().HoverAt(_rateLine.Samples.Count * 2 / 3);
    }

    /// <summary>The chart's motion in a render: its newest minute landing (the chart state), else the chart drawing itself in.</summary>
    void ChartFrameForRender()
    {
        if (_chartShot) LiveChart().ReplayLanding(_rateLine);
        else Motion.Reveal(RateCanvas, RateCanvas.Width, RateCanvas.Height);
    }
}
