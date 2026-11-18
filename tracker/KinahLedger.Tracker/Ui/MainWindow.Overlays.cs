using System.Windows;

namespace KinahLedger.Ui;

/// <summary>
/// The header's Overlays button (redesign 2026-11-14, Steven's pick): one round button, a green dot on it while any overlay
/// over the game is on, and a menu with a switch for each one: Kinah (the panel), Timers, and Parser while it is an overlay
/// of its own (Settings › In game › Parser › Separate parser overlay; by default it is a section of the Kinah overlay). Each
/// overlay is separate and optional; the row under each switch says where that overlay is set up. An overlay joins with
/// <see cref="RegisterOverlay"/>; the Kinah overlay is the first.
/// </summary>
public partial class MainWindow
{
    sealed record OverlayDef(string Name, Func<bool> IsOn, Action<bool> Set, string Tip, Func<bool>? Visible)
    {
        public bool Shown => Visible?.Invoke() ?? true;
    }
    readonly List<OverlayDef> _overlays = new();

    /// <summary>
    /// Adds an overlay to the header's menu: <paramref name="isOn"/> says whether it shows, <paramref name="set"/> switches
    /// it (and saves the choice), <paramref name="tip"/> is the line under its switch, <paramref name="visible"/> (when given)
    /// says whether it is an overlay of its own just now (the Parser is a section of the Kinah overlay unless the player
    /// separates it). Overlays sit in the order they are registered; registering a name again replaces it.
    /// </summary>
    internal void RegisterOverlay(string name, Func<bool> isOn, Action<bool> set, string tip = "", Func<bool>? visible = null)
    {
        _overlays.RemoveAll(o => o.Name == name);
        _overlays.Add(new OverlayDef(name, isOn, set, tip, visible));
        DrawOverlayPills();
    }

    void RegisterKinahOverlay() => RegisterOverlay("Kinah", () => _prefs.Hud, on =>
    {
        _prefs.Hud = on;
        if (!_demo) _prefs.Save();
        if (!on) CloseHud();
        else if (!_demo && _watcher.Chat != null) EnsureHud();
        UpdateHud();
    }, L.T("Your hour, pickups and best. Its look: Options › In game."));

    /// <summary>
    /// The button follows the overlays' switches: the green dot while any is on, and its tooltip naming those on. Call it
    /// whenever an overlay is switched from anywhere else (its own page, its right-click menu, a shortcut, Options' Save).
    /// </summary>
    internal void DrawOverlayPills()
    {
        if (OverlaysBtn == null) return;
        var shown = _overlays.Where(o => o.Shown).ToList();
        var on = shown.Where(o => o.IsOn()).Select(o => o.Name).ToList();
        OverlaysDot.Visibility = on.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        OverlaysBtn.ToolTip = L.F("Overlays over the game, each on or off: {0}.", string.Join(", ", shown.Select(o => L.T(o.Name))))
            + (on.Count > 0 ? "\n" + L.F("On now: {0}.", string.Join(", ", on.Select(n => L.T(n)))) : "\n" + L.T("None on now."));
    }

    /// <summary>The menu's switches: one per overlay, its line saying what it shows and where it is set up.</summary>
    internal List<MenuEntry> OverlayEntries()
    {
        var shown = _overlays.Where(o => o.Shown).ToList();
        var list = shown.Select(o => new MenuEntry(L.F("{0} overlay", L.T(o.Name)), () => { o.Set(!o.IsOn()); DrawOverlayPills(); }, Sub: o.Tip, On: o.IsOn())).ToList();
        AddPopOutEntries(list);   // the pop-out windows' switches (Ui/MainWindow.PopOuts.cs)
        // what keeps the meter off ("Needs Npcap · Get it", Ui/MainWindow.MeterBlock.cs): one line after the Parser and Applicants switches
        if (MeterBlockMenuEntry() is { } block)
        {
            int at = list.FindLastIndex(e => e.Text.Contains(L.T("Parser")) || e.Text.Contains(L.T("Applicants")));
            list.Insert(at < 0 ? list.Count : at + 1, block);
        }
        // the game in exclusive fullscreen keeps every overlay from showing: first, above them all
        if (FullscreenMenuEntry() is { } fs) list.Insert(0, fs);
        return list;
    }

    void OpenOverlaysMenu() => ShowMenu(OverlaysBtn, OverlayEntries());

    /// <summary>For the options self-test: the overlays as the menu lists them (name, on), and whether the button's dot shows.</summary>
    internal List<(string Name, bool On)> OverlaysForTest() => _overlays.Where(o => o.Shown).Select(o => (o.Name, o.IsOn())).ToList();
    internal bool OverlaysDotForTest => OverlaysDot.Visibility == Visibility.Visible;
    internal void UnregisterOverlayForTest(string name) { _overlays.RemoveAll(o => o.Name == name); DrawOverlayPills(); }
}
