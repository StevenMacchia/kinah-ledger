namespace KinahLedger.Session;

/// <summary>
/// How a run that just ended stands against the player's earlier ones there, for the run-end recap (Ui/MainWindow.Recap.cs,
/// 2026-11-11): "+18% vs your average here", "−7% vs your average here", or "About your average here". Said only once there
/// is enough history to mean something (<see cref="ZoneCompare.RunEarlierMin"/> earlier runs there, as Compare asks), so a
/// second visit to a zone never reads as a verdict. Apart from the window so the self-test can check the arithmetic.
/// </summary>
public static class RunStanding
{
    /// <summary>Within this share of the average (either way) the line says "About your average here".</summary>
    public const double About = 0.03;

    /// <summary>The share a figure is above (+) or below (−) an average: 0.18 for 18% more.</summary>
    public static double Share(double figure, double average) => average > 0 ? figure / average - 1 : 0;

    /// <summary>
    /// A farming zone's average hour over the earlier stays there: their worth over their time, so a long stay counts for
    /// more than a short one. A stay under <see cref="BestRuns.MinMinutes"/> is left out (a few lucky minutes swing it, as
    /// they would a best). Null with fewer than <see cref="ZoneCompare.RunEarlierMin"/> stays left, or no worth at all.
    /// </summary>
    public static double? HourAverage(IEnumerable<(long Value, double Seconds)> earlier)
    {
        int n = 0; double value = 0, seconds = 0;
        foreach (var (v, s) in earlier)
        {
            if (s < BestRuns.MinMinutes * 60) continue;
            n++; value += v; seconds += s;
        }
        if (n < ZoneCompare.RunEarlierMin || value <= 0) return null;
        return value / (seconds / 3600);
    }

    /// <summary>
    /// The line's two parts: the figure ("+18%", "−7%", written with a true minus) and the words after it; within
    /// <see cref="About"/> no figure and "About your average here". <paramref name="sign"/> is +1, −1 or 0 (about).
    /// </summary>
    public static (string Figure, string Words, int Sign) Line(double share)
    {
        int pct = (int)Math.Round(Math.Abs(share) * 100);
        if (Math.Abs(share) < About) return ("", "About your average here", 0);
        return (Percent(Math.Sign(share), pct), " vs your average here", Math.Sign(share));
    }

    /// <summary>A percent with its sign: "+18%", "−7%", "+1,240%".</summary>
    public static string Percent(int sign, double pct) => (sign < 0 ? "−" : "+") + Math.Round(pct).ToString("N0") + "%";
}
