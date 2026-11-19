using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using KinahLedger.Session;

namespace KinahLedger.Ui;

/// <summary>
/// The character's Odyle Energy on the DPS meter (Steven, 2026-11-19: "Show on the DPS meter very tiny an icon with the
/// energy odyle and then how much energy your character has"): the game's Odyle Energy art at the foot's text size and the
/// figure after it, after the fight's total in every look's foot (the overlay, the pop-out window, Settings' previews).
/// Nothing at all while the figure was never read (no 0), with Settings › DPS meter › More › Odyle Energy off, and on the
/// share picture. The figure is the character's last reading with the refills since (Session/OdyleEnergy.cs).
/// </summary>
static class EnergyMark
{
    /// <summary>Settings › DPS meter › More › Odyle Energy (parser.json's OdyleEnergy, on by default).</summary>
    public static bool Pref { get; set; } = true;
    /// <summary>A figure in place of the character's (below 0: never read): ui-shot's renders (KL_ODYLE=1975) and the self-tests.</summary>
    internal static int? Fixed { get; set; }
    /// <summary>The figure is left out (the share picture: a drawing for others).</summary>
    [ThreadStatic] static int _hidden;

    /// <summary>The figure to show, or null: off, hidden here, or never read.</summary>
    public static int? Value
    {
        get
        {
            if (!Pref || _hidden > 0) return null;
            if (Fixed is int f) return f >= 0 ? f : null;
            try { return OdyleEnergy.Shared.Now(Characters.Current?.Id ?? "", DateTime.UtcNow); }
            catch (Exception) { return null; }
        }
    }

    /// <summary>What a meter drawing compares, so a new figure draws the foot again.</summary>
    public static string Sig => Value is int v ? v.ToString(CultureInfo.InvariantCulture) : "";

    /// <summary>The figure as the foot says it: "1,975".</summary>
    public static string Text(int v) => v.ToString("N0", CultureInfo.InvariantCulture);

    /// <summary>Leaves the figure out of what is drawn until disposed (the share picture).</summary>
    public static IDisposable Hidden() { _hidden++; return new Undo(); }
    sealed class Undo : IDisposable { bool _done; public void Dispose() { if (_done) return; _done = true; _hidden--; } }

    /// <summary>
    /// The mark: the art, <paramref name="size"/> tall (the foot's text size), then the figure in <paramref name="ink"/>, a
    /// gap before them. Null when there is nothing to show.
    /// </summary>
    public static FrameworkElement? Chip(double size, Brush ink, FontFamily? face = null, double gap = -1)
    {
        if (Value is not int v) return null;
        var art = GameArt.Element("odyle", Math.Round(size * 1.1));
        art.Margin = new Thickness(0, 0, Math.Max(1.5, size * 0.18), 0);
        var fig = new TextBlock { Text = Text(v), FontSize = size, FontWeight = FontWeights.SemiBold, Foreground = ink, VerticalAlignment = VerticalAlignment.Center };
        if (face != null) fig.FontFamily = face;
        Typography.SetNumeralAlignment(fig, FontNumeralAlignment.Tabular);
        var chip = new StackPanel
        {
            Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Tag = "odyle-energy",
            Margin = new Thickness(gap >= 0 ? gap : Math.Round(size * 0.8), 0, 0, 0), Children = { art, fig },
            ToolTip = L.T("Odyle Energy: what's left for a run's final reward"),
        };
        System.Windows.Automation.AutomationProperties.SetName(chip, L.T("Odyle Energy") + " " + Text(v));
        return chip;
    }

    /// <summary>
    /// The mark at the end of a foot's text (its "Total 10.5M"): the art on the text's middle and the figure as the text's own
    /// run, so the line stays one text (it trims as one, and nothing is drawn over it); nothing when there is none.
    /// </summary>
    public static void After(TextBlock t, double size, Brush ink, FontFamily? face = null)
    {
        if (Value is not int v) return;
        var art = GameArt.Element("odyle", Math.Round(size * 1.1));
        art.Margin = new Thickness(0, -3, Math.Max(1.5, size * 0.18), -3);   // the line keeps its height (the art is a touch taller than the text)
        art.Tag = "odyle-energy";
        art.ToolTip = L.T("Odyle Energy: what's left for a run's final reward");
        System.Windows.Automation.AutomationProperties.SetName(art, L.T("Odyle Energy") + " " + Text(v));
        t.Inlines.Add(new Run("  ") { FontSize = size });
        t.Inlines.Add(new InlineUIContainer(art) { BaselineAlignment = BaselineAlignment.Center });
        var fig = new Run(Text(v)) { FontSize = size, FontWeight = FontWeights.SemiBold, Foreground = ink };
        if (face != null) fig.FontFamily = face;
        t.Inlines.Add(fig);
    }

    /// <summary>
    /// The mark taken off <paramref name="p"/> again when the whole foot (<paramref name="whole"/>) would be wider than
    /// <paramref name="room"/>: the foot's own figures come first.
    /// </summary>
    public static void DropIfWide(Panel p, FrameworkElement whole, double room)
    {
        var mark = p.Children.OfType<FrameworkElement>().FirstOrDefault(c => Equals(c.Tag, "odyle-energy"));
        if (mark == null) return;
        whole.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        if (whole.DesiredSize.Width > room) p.Children.Remove(mark);
    }

    /// <summary>The mark after a foot's last part (a row of "TOTAL" and its figure); nothing when there is none.</summary>
    public static void After(Panel p, double size, Brush ink, FontFamily? face = null)
    {
        if (Chip(size, ink, face) is { } c) p.Children.Add(c);
    }
}
