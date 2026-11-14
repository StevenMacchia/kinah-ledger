using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;

namespace KinahLedger.Ui;

/// <summary>
/// The header's Overlays group (redesign 2026-11-14): one pill per overlay over the game (Kinah, Timers, Parser), each separate
/// and optional, lit while it is on; a click switches it from any page. An overlay joins the group with
/// <see cref="RegisterOverlay"/>; the Kinah overlay (the panel, Options › In game) is the first.
/// </summary>
public partial class MainWindow
{
    sealed record OverlayPillDef(string Name, Func<bool> IsOn, Action<bool> Set, string Tip);
    readonly List<OverlayPillDef> _overlays = new();

    /// <summary>
    /// Adds an overlay's pill to the header: <paramref name="isOn"/> says whether it shows, <paramref name="set"/> switches it
    /// (and saves the choice). Pills sit in the order they are registered; registering a name again replaces it.
    /// </summary>
    internal void RegisterOverlay(string name, Func<bool> isOn, Action<bool> set, string tip = "")
    {
        _overlays.RemoveAll(o => o.Name == name);
        _overlays.Add(new OverlayPillDef(name, isOn, set, tip));
        DrawOverlayPills();
    }

    void RegisterKinahOverlay() => RegisterOverlay("Kinah", () => _prefs.Hud, on =>
    {
        _prefs.Hud = on;
        if (!_demo) _prefs.Save();
        if (!on) CloseHud();
        else if (!_demo && _watcher.Chat != null) EnsureHud();
        UpdateHud();
    }, "Your run's Kinah an hour, pickups and best, over the game. Its look is in Options › In game.");

    /// <summary>Redraws the pills from their overlays' switches (after a click, Options' Save, or a registration).</summary>
    internal void DrawOverlayPills()
    {
        OverlaysGroup.Children.Clear();
        if (_overlays.Count == 0) return;
        var lbl = new TextBlock { Text = "Overlays", FontSize = 11.5, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 4, 0) };
        lbl.SetResourceReference(TextBlock.ForegroundProperty, "Faint");
        OverlaysGroup.Children.Add(lbl);
        foreach (var o in _overlays) OverlaysGroup.Children.Add(OverlayPill(o));
    }

    FrameworkElement OverlayPill(OverlayPillDef o)
    {
        bool on = o.IsOn();
        var sp = new StackPanel { Orientation = Orientation.Horizontal };
        var dot = new Ellipse { Width = 6, Height = 6, Margin = new Thickness(0, 0, 6, 0), VerticalAlignment = VerticalAlignment.Center, Opacity = on ? 1 : 0.6 };
        dot.SetResourceReference(Shape.FillProperty, on ? "Good" : "Faint");
        sp.Children.Add(dot);
        var t = new TextBlock { Text = o.Name, FontSize = 11.5, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center };
        t.SetResourceReference(TextBlock.ForegroundProperty, on ? "Ink" : "Faint");
        sp.Children.Add(t);
        var b = new Border { Child = sp, CornerRadius = new CornerRadius(10), Padding = new Thickness(8, 3, 9, 3), Margin = new Thickness(4, 0, 0, 0), BorderThickness = new Thickness(1), VerticalAlignment = VerticalAlignment.Center, Cursor = Cursors.Hand, Focusable = true };
        b.SetResourceReference(Border.BackgroundProperty, on ? "Card2" : "Bg");
        b.SetResourceReference(Border.BorderBrushProperty, on ? "LineStrong" : "Line");
        b.ToolTip = $"The {o.Name} overlay: {(on ? "on" : "off")}. Click to turn it {(on ? "off" : "on")}." + (o.Tip.Length > 0 ? "\n" + o.Tip : "");
        System.Windows.Automation.AutomationProperties.SetName(b, $"{o.Name} overlay, {(on ? "on" : "off")}");
        void Flip() { o.Set(!o.IsOn()); DrawOverlayPills(); }
        b.MouseLeftButtonUp += (_, e) => { Flip(); e.Handled = true; };
        b.KeyDown += (_, e) => { if (e.Key is Key.Enter or Key.Space) { Flip(); e.Handled = true; } };
        return b;
    }

    /// <summary>For the options self-test: the pills as they show (name, on).</summary>
    internal List<(string Name, bool On)> OverlayPillsForTest() => _overlays.Select(o => (o.Name, o.IsOn())).ToList();
    /// <summary>For the options self-test: a click on an overlay's pill.</summary>
    internal void ClickOverlayPillForTest(string name)
    {
        foreach (var c in OverlaysGroup.Children.OfType<Border>())
            if (c.Child is StackPanel sp && sp.Children.OfType<TextBlock>().Any(t => t.Text == name))
                c.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left) { RoutedEvent = UIElement.MouseLeftButtonUpEvent });
    }
}
