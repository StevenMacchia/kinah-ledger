using System.Drawing;
using System.Drawing.Imaging;

namespace KinahLedger.Session;

/// <summary>Where a picture would be cut from. Only the item-sized ones may ever be cut for "Help improve counting".</summary>
public enum CropKind
{
    /// <summary>A loot list row's name words (the OCR line's box).</summary>
    LootName,
    /// <summary>The icon square left of a loot list row.</summary>
    LootIcon,
    /// <summary>The item name words of one of the player's own pickup lines in the chat (ScreenReader.NameSpans).</summary>
    ChatItem,
    /// <summary>One reward slot on an event card or a reward table.</summary>
    CardSlot,
    /// <summary>The chat box as a whole, or any line of it: never cut.</summary>
    ChatPanel,
    /// <summary>A whole frame or window: never cut (whole frames stay under "Send report pictures").</summary>
    Frame,
}

/// <summary>
/// A crop request: what kind of thing is cut, the item's own bounds in the picture (<paramref name="Span"/>), and the
/// picture's bounds (empty when the crop was cut already, then only its size is checked).
/// </summary>
public readonly record struct CropSource(CropKind Kind, Rectangle Span, Rectangle Picture = default);

/// <summary>
/// The hard rule for the training crops (Steven, 2026-10-24: "never secretly", and never anything that could hold a
/// player's name): a crop is an item's name words, its icon or its reward slot, and never more than those bounds plus
/// <see cref="Margin"/> pixels on each side. The chat panel and whole frames are refused outright. Every crop the
/// label writer keeps goes through <see cref="IsAllowed"/>, and parse-test checks it.
/// </summary>
public static class Crops
{
    /// <summary>Pixels a crop may reach past the item's own bounds on each side.</summary>
    public const int Margin = 4;
    /// <summary>A name span is one line of text: at most this tall and this wide (a 2160p loot row's name is about 40 by 420).</summary>
    public const int MaxNameHeight = 72, MaxNameWidth = 760;
    /// <summary>An icon or a reward slot: at most this on a side (a 2160p card slot is 133 by 135).</summary>
    public const int MaxSquare = 256;
    /// <summary>Anything smaller isn't worth keeping.</summary>
    public const int MinSide = 4;

    /// <summary>
    /// True when <paramref name="rect"/> may be cut and kept: an item kind (never <see cref="CropKind.ChatPanel"/> or
    /// <see cref="CropKind.Frame"/>), inside the item's span grown by <see cref="Margin"/>, inside the picture, and the
    /// size of one line of text or one icon.
    /// </summary>
    public static bool IsAllowed(Rectangle rect, CropSource source)
    {
        if (source.Kind is CropKind.ChatPanel or CropKind.Frame) return false;
        var span = source.Span;
        if (span.Width < MinSide || span.Height < MinSide || rect.Width < MinSide || rect.Height < MinSide) return false;
        var limit = Rectangle.Inflate(span, Margin, Margin);
        if (!limit.Contains(rect)) return false;
        if (!source.Picture.IsEmpty && !source.Picture.Contains(rect)) return false;
        return source.Kind switch
        {
            CropKind.LootName or CropKind.ChatItem => span.Height <= MaxNameHeight && rect.Height <= MaxNameHeight + 2 * Margin && rect.Width <= MaxNameWidth + 2 * Margin,
            CropKind.LootIcon or CropKind.CardSlot => rect.Width <= MaxSquare + 2 * Margin && rect.Height <= MaxSquare + 2 * Margin,
            _ => false,
        };
    }

    /// <summary>The largest crop allowed for a source: its span plus the margin, held to the picture.</summary>
    public static Rectangle Bounds(CropSource s)
    {
        var r = Rectangle.Inflate(s.Span, Margin, Margin);
        if (!s.Picture.IsEmpty) r.Intersect(s.Picture);
        return r;
    }

    /// <summary>Cuts the allowed crop out of <paramref name="raw"/> (whose bounds are the picture); null when the rule refuses it.</summary>
    public static Bitmap? Cut(Bitmap raw, CropKind kind, Rectangle span)
    {
        var src = new CropSource(kind, span, new Rectangle(0, 0, raw.Width, raw.Height));
        var r = Bounds(src);
        if (!IsAllowed(r, src)) return null;
        try { return raw.Clone(r, PixelFormat.Format32bppArgb); } catch (Exception) { return null; }
    }

    /// <summary>A picture already cut by a reader (an event card's slot): kept only when its size fits the slot's bounds plus the margin.</summary>
    public static bool IsAllowedCut(Size crop, CropKind kind, Size span) =>
        IsAllowed(new Rectangle(-Margin, -Margin, crop.Width, crop.Height), new CropSource(kind, new Rectangle(0, 0, span.Width, span.Height)));
}
