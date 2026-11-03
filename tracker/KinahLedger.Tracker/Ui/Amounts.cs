namespace KinahLedger.Ui;

/// <summary>
/// The one way a figure is written on every surface (the HUD card, Today, Week, Compare, the tables and the status strings):
/// in full under 100,000, then "123.5K", then "1.27M". It replaces three that disagreed (MainWindow.Fk wrote 9.5K and 12K, and
/// 999,600 as "1000K"; MainWindow.Short wrote 12,345; the console view wrote 12K). Tooltips give <see cref="Full"/>.
/// </summary>
public static class Amounts
{
    /// <summary>Below this a figure is written in full; from it, in thousands.</summary>
    public const double FullBelow = 100_000;
    /// <summary>The first figure written in millions: 999,500 would round to "1000K", so it is "1M".</summary>
    public const double MillionsFrom = 999_500;

    /// <summary>
    /// "47", "9,500", "99,999", "123.5K", "680K", "1.27M", "2.5B"; nothing is "0". A fraction under 100 (a rate like 7.5 an
    /// hour) keeps its decimals ("7.5", "0.47"); a whole number under 1,000 has none ("47", "-500").
    /// </summary>
    public static string Short(double v)
    {
        double a = Math.Abs(v);
        if (v == 0) return "0";
        if (a < FullBelow) return a < 100 && v != Math.Round(v) ? (a >= 10 ? $"{v:0.0}" : $"{v:0.00}") : Math.Round(v).ToString("N0");
        if (a < MillionsFrom) return $"{v / 1e3:0.#}K";
        if (a < 999_500_000) return $"{v / 1e6:0.##}M";
        return $"{v / 1e9:0.##}B";
    }

    /// <summary>The whole figure with its thousands marks ("999,600"), for a tooltip that sits over a <see cref="Short"/> one.</summary>
    public static string Full(double v) => Math.Round(v).ToString("N0");

    /// <summary>The smallest step <see cref="Short"/> distinguishes at this size: 1, 100 (the tenth of a K) or 10,000 (the hundredth of an M).</summary>
    public static long Step(long v) { long a = Math.Abs(v); return a >= MillionsFrom ? 10_000 : a >= FullBelow ? 100 : 1; }
}
