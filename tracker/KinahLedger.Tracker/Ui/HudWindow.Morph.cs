using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace KinahLedger.Ui;

/// <summary>
/// Entering a Transcendence, an Expedition or a Sanctuary, the meter takes the Kinah line's place (Parser v2, Steven
/// 2026-11-15: one product, nothing pops): the Kinah pill's picture stays where the pill was and fades as the meter grows out
/// of that spot, from the pill's size and round corner to its own, anchored where the Kinah line sits. Leaving, the pill fades
/// back in as the meter folds. One Motion course each (Ui/Motion.cs: held at KL_MOTION_AT in a render, nothing when still).
/// </summary>
public sealed partial class HudWindow
{
    /// <summary>The Kinah line as it looks now, for the morph (null when it isn't drawn).</summary>
    BitmapSource? SnapOwn()
    {
        if (OwnContent is not FrameworkElement own || own.Visibility != Visibility.Visible || own.ActualWidth < 1 || own.ActualHeight < 1) return null;
        try
        {
            // the whole window drawn, then the Kinah line's box cut out of it (a brush of the line alone can come out blank)
            if (VisualTreeHelper.GetChildrenCount(this) == 0 || VisualTreeHelper.GetChild(this, 0) is not FrameworkElement root || root.ActualWidth < 1) return null;
            var rtb = new RenderTargetBitmap((int)Math.Ceiling(root.ActualWidth), (int)Math.Ceiling(root.ActualHeight), 96, 96, PixelFormats.Pbgra32);
            rtb.Render(root);
            var box = own.TransformToAncestor(root).TransformBounds(new Rect(0, 0, own.ActualWidth, own.ActualHeight));
            int x = Math.Clamp((int)Math.Floor(box.X), 0, rtb.PixelWidth - 1), y = Math.Clamp((int)Math.Floor(box.Y), 0, rtb.PixelHeight - 1);
            var cut = new CroppedBitmap(rtb, new Int32Rect(x, y, Math.Clamp((int)Math.Ceiling(box.Width), 1, rtb.PixelWidth - x), Math.Clamp((int)Math.Ceiling(box.Height), 1, rtb.PixelHeight - y)));
            cut.Freeze();
            return cut;
        }
        catch (Exception) { return null; }
    }

    /// <summary>While the meter grows: the slot's child is a grid of the meter and the pill's picture over it (the meter itself stays its own element).</summary>
    (System.Windows.Controls.Grid Wrap, FrameworkElement Content)? _morph;

    /// <summary>What the slot shows, the meter itself even while it grows out of the pill.</summary>
    UIElement? SlotContent => _morph?.Content ?? _parserSlot.Child;

    /// <summary>The slot set to <paramref name="el"/>, through the morph's grid when one is under way.</summary>
    void SetSlot(UIElement el)
    {
        if (_morph is { } m) { if (m.Content == el) return; EndMorph(); }
        _parserSlot.Child = el;
    }

    void EndMorph()
    {
        if (_morph is not { } m) return;
        _morph = null;
        Motion.Halt(m.Content);
        m.Content.Clip = null; m.Content.Opacity = 1;
        m.Wrap.Children.Clear();
        _parserSlot.Child = m.Content;
    }

    /// <summary>The meter grows out of the pill's spot: its clip from the pill's box (bottom left) to its own, the pill's picture fading over it.</summary>
    void MorphIn(BitmapSource pill, Size pillSize)
    {
        if (!Motion.On || _still && Motion.FrozenAt == null || _parserSlot.Child is not FrameworkElement content || _morph != null) return;
        UpdateLayout();
        double W = content.ActualWidth, H = content.ActualHeight;
        if (W < 1 || H < 1) return;
        double wp = Math.Min(W, pillSize.Width), hp = Math.Min(H, pillSize.Height), r1 = (_look as HudStackLook)?.Corner ?? HudGlass.Radius;
        _parserSlot.Child = null;
        var ghost = new System.Windows.Controls.Image { Source = pill, Width = wp, Height = hp, Stretch = Stretch.Fill, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Bottom, IsHitTestVisible = false, RenderTransform = new TranslateTransform() };
        var wrap = new System.Windows.Controls.Grid { Children = { content, ghost } };
        _parserSlot.Child = wrap;
        _morph = (wrap, content);
        Motion.Play(content, 520, p =>
        {
            double g = Motion.Out(Math.Clamp(p / 0.55, 0, 1));
            double w = wp + (W - wp) * g, h = hp + (H - hp) * g, r = hp / 2 + (r1 - hp / 2) * g;
            content.Clip = new RectangleGeometry(new Rect(0, H - h, w, h), r, r);
            content.Opacity = 0.3 + 0.7 * Math.Clamp((p - 0.1) / 0.4, 0, 1);
            double q = Math.Clamp(p / 0.3, 0, 1);
            ((TranslateTransform)ghost.RenderTransform).Y = 6 * q; ghost.Opacity = 1 - Motion.Out(q);
        }, EndMorph);
    }

    /// <summary>Leaving: the Kinah line fades back in where it was.</summary>
    void MorphOut()
    {
        if (!Motion.On || OwnContent is not UIElement own) return;
        own.Opacity = 0;
        Motion.Play(own, 320, p => own.Opacity = Motion.Out(p), () => own.Opacity = 1);
    }
}
