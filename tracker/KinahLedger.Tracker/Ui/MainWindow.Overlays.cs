using System.Windows;

namespace KinahLedger.Ui;

/// <summary>
/// The header's Overlays button (redesign 2026-11-14, Steven's pick): one round button, a green dot on it while any overlay
/// over the game is on, and a menu with a switch for each one: Kinah (the panel), Timers, and Parser while it exists. Each
/// overlay is separate and optional; the row under each switch says where that overlay is set up. An overlay joins with
/// <see cref="RegisterOverlay"/>; the Kinah overlay is the first.
/// </summary>
public partial class MainWindow
{
    sealed record OverlayDef(string Name, Func<bool> IsOn, Action<bool> Set, string Tip);
    readonly List<OverlayDef> _overlays = new();

    /// <summary>
    /// Adds an overlay to the header's menu: <paramref name="isOn"/> says whether it shows, <paramref name="set"/> switches
    /// it (and saves the choice), <paramref name="tip"/> is the line under its switch. Overlays sit in the order they are
    /// registered; registering a name again replaces it.
    /// </summary>
    internal void RegisterOverlay(string name, Func<bool> isOn, Action<bool> set, string tip = "")
    {
        _overlays.RemoveAll(o => o.Name == name);
        _overlays.Add(new OverlayDef(name, isOn, set, tip));
        DrawOverlayPills();
    }

    void RegisterKinahOverlay() => RegisterOverlay("Kinah", () => _prefs.Hud, on =>
    {
        _prefs.Hud = on;
        if (!_demo) _prefs.Save();
        if (!on) CloseHud();
        else if (!_demo && _watcher.Chat != null) EnsureHud();
        UpdateHud();
    }, "Your hour, pickups and best. Its look: Options › In game.");

    /// <summary>
    /// The button follows the overlays' switches: the green dot while any is on, and its tooltip naming those on. Call it
    /// whenever an overlay is switched from anywhere else (its own page, its right-click menu, a shortcut, Options' Save).
    /// </summary>
    internal void DrawOverlayPills()
    {
        if (OverlaysBtn == null) return;
        var on = _overlays.Where(o => o.IsOn()).Select(o => o.Name).ToList();
        OverlaysDot.Visibility = on.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        OverlaysBtn.ToolTip = "Overlays over the game, each on or off: " + string.Join(", ", _overlays.Select(o => o.Name)) + "."
            + (on.Count > 0 ? $"\nOn now: {string.Join(", ", on)}." : "\nNone on now.");
    }

    /// <summary>The menu's switches: one per overlay, its line saying what it shows and where it is set up.</summary>
    internal List<MenuEntry> OverlayEntries() => _overlays.Select(o => new MenuEntry($"{o.Name} overlay", () => { o.Set(!o.IsOn()); DrawOverlayPills(); }, Sub: o.Tip, On: o.IsOn())).ToList();

    void OpenOverlaysMenu() => ShowMenu(OverlaysBtn, OverlayEntries());

    /// <summary>For the options self-test: the overlays as the menu lists them (name, on), and whether the button's dot shows.</summary>
    internal List<(string Name, bool On)> OverlaysForTest() => _overlays.Select(o => (o.Name, o.IsOn())).ToList();
    internal bool OverlaysDotForTest => OverlaysDot.Visibility == Visibility.Visible;
}
