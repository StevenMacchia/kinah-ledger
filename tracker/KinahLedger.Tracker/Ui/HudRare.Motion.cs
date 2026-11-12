using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using static KinahLedger.Ui.HudKit;

namespace KinahLedger.Ui;

/// <summary>
/// The rare drop card's entrance (2026-11-11, Steven: "cool and flashy"), on Buddy (the Card) and Pop (the Ticker): the
/// item's tile grows in with a ring bursting out from it in the grade's colour, the label comes up, a band of light runs
/// across the name in that colour, and the price rolls up to its figure, all in under a second. It plays alongside the
/// looks' own rare beats (Ui/HudCard.Motion.cs, Ui/HudTicker.Motion.cs), once for each new drop, never on a look's first
/// draw, and stays inside the card: the ring grows to half again the tile's size, a quarter of it each side, less than the card's margin round it, so
/// nothing moves outside the panel or over what the reader reads. Through Ui/Motion.cs, so a render (Still) and Windows's
/// animations off show the card as it is.
/// </summary>
static partial class HudRare
{
    /// <summary>The parts of a drawn drop that its entrance moves, and the colour and price it moves them to.</summary>
    sealed record Parts(FrameworkElement Tile, TextBlock Label, Run More, Run Name, Run? Figure, string Price, Color Color);

    static readonly ConditionalWeakTable<FrameworkElement, Parts> Drawn = new();

    static void Keep(FrameworkElement made, Parts parts) => Drawn.AddOrUpdate(made, parts);

    /// <summary>The entrance takes this long.</summary>
    public const double EnterMs = 900;
    /// <summary>The tile grows in from this share of its size.</summary>
    const double TileFrom = 0.6;
    /// <summary>The ring grows by this share of the tile, and is drawn this thick.</summary>
    const double RingGrow = 0.5, RingStroke = 2;

    /// <summary>
    /// A drawn drop (the Card's body or the Ticker's toast) makes its entrance: the tile grows in and fades up in the first
    /// third, the ring bursts from it, the label comes up, the name's light runs across from left to right and the price
    /// rolls up from nothing; at the end each part is as the card draws it still.
    /// </summary>
    internal static void Enter(FrameworkElement made)
    {
        if (!Drawn.TryGetValue(made, out var p) || p.Tile.Parent is not Grid host || Motion.Playing(p.Tile)) return;
        var tile = p.Tile;
        var scale = new ScaleTransform(TileFrom, TileFrom);
        tile.RenderTransformOrigin = new Point(0.5, 0.5);
        tile.RenderTransform = scale;
        var ring = Motion.BurstRing(p.Color, tile.Width, RingStroke);
        Grid.SetColumn(ring, Grid.GetColumn(tile));
        host.Children.Add(ring);
        var light = Motion.SweepBrush(p.Color);
        p.Name.Foreground = light;
        Motion.Play(tile, EnterMs, t =>
        {
            scale.ScaleX = scale.ScaleY = TileFrom + (1 - TileFrom) * Motion.Out(Motion.Span(t, 0, 0.35));
            tile.Opacity = Motion.Out(Motion.Span(t, 0, 0.2));
            Motion.Burst(ring, Motion.Span(t, 0.05, 0.6), RingGrow);
            p.Label.Opacity = Motion.Out(Motion.Span(t, 0.1, 0.35));
            Motion.Sweep(light, Motion.Span(t, 0.15, 0.8));
            if (p.Figure != null) p.Figure.Text = Motion.RollIn(p.Price, Motion.Span(t, 0, 0.6));
        }, () =>
        {
            tile.Opacity = 1;
            tile.ClearValue(UIElement.RenderTransformProperty);
            host.Children.Remove(ring);
            p.Label.Opacity = 1;
            p.Name.Foreground = Frozen(p.Color);
            if (p.Figure != null) p.Figure.Text = p.Price;
        });
    }

    /// <summary>More drops joined the one drawn: its "+2 more" says how many, where it is.</summary>
    internal static void SetMore(FrameworkElement made, int more)
    {
        if (Drawn.TryGetValue(made, out var p)) p.More.Text = MoreText(more);
    }

    /// <summary>For the options self-test: a drawn drop's tile, its label and price as shown, and whether its name's light is on it.</summary>
    internal static (FrameworkElement Tile, string Label, string Price, bool Lit)? PartsForTest(FrameworkElement? made) =>
        made != null && Drawn.TryGetValue(made, out var p)
            ? (p.Tile, string.Concat(p.Label.Inlines.OfType<Run>().Select(r => r.Text)), p.Figure?.Text ?? "", p.Name.Foreground is LinearGradientBrush)
            : null;
}
