using System.Windows;
using System.Windows.Media;
using KinahLedger.Data;
using KinahLedger.Session;

namespace KinahLedger.Ui;

/// <summary>
/// The window's side of the panel's looks (Options › In game › Panel look, 2026-10-25): the values the Card and the
/// Ticker draw from, worked out from what the window already has (the live feed, the zone, the reader's pause, the rare
/// drop alert), the look kept in hud.json, the Ticker's own place, and the sample figures Options and the renders draw.
/// </summary>
public partial class MainWindow
{
    /// <summary>The saved look (HudLooks), for Options and the self-test.</summary>
    public string HudLook => HudLooks.Parse(_hudPrefs.Look);

    /// <summary>The coin the game draws for Kinah (data/extra-items.json, "Kina"): the Kinah pickups' tile.</summary>
    const string KinahIcon = ItemDictionary.IconBase + "Icon_gold.png";
    static readonly string[] PickKeys = { "", "Id", "At", "Count", "Worth", "Grade", "Icon", "Note", "Kind" };
    static readonly string[] RareKeys = { "rare", "rareId", "rareCount", "rareGrade", "rareIcon", "rareColor" };

    /// <summary>
    /// The looks' values, from the window's tick (UpdateHud, through HudFeed): the last four pickups farming and in
    /// events ("pick0" newest to "pick3"), the zone, why the reader is paused, and a rare drop while it is held.
    /// </summary>
    void HudLookValues()
    {
        foreach (var (k, v) in HudPickValuesNow()) _hudValues[k] = v;
        _hudValues["zone"] = _watcher.Chat?.ZoneView?.Zone ?? _demoZone ?? "";
        var paused = _watcher.Chat?.Paused ?? "";
        _hudValues["paused"] = paused.Length > 0 ? "Paused: " + paused : "";
        bool held = DateTime.UtcNow < _hudRareUntil;
        foreach (var k in RareKeys) _hudValues[k] = held ? _hudRare.GetValueOrDefault(k, "") : "";
    }

    /// <summary>The last four things picked up, farming or in an event (Kinah and items, not crafts or run steps), as the looks' values.</summary>
    internal Dictionary<string, string> HudPickValuesNow() => HudPickValues(new[] { FeedTab.Farming, FeedTab.Events }.SelectMany(FeedFor)
        .Where(l => l.Kind is "item" or "kinah").OrderByDescending(l => l.At).Take(HudLooks.Picks).ToList());

    /// <summary>
    /// Feed lines as the looks' pickups: the name ("1,192 Kinah" for Kinah), the count badge ("×1", or the amount on the
    /// coin), the worth as the feed says it, the grade and the icon's URL (the window's icon cache), and when it was
    /// picked up, so the Ticker fades each one 6 s after. Keys past the last line are blank.
    /// </summary>
    internal Dictionary<string, string> HudPickValues(IReadOnlyList<FeedLine> lines)
    {
        var d = new Dictionary<string, string>();
        for (int i = 0; i < HudLooks.Picks; i++)
        {
            var p = "pick" + i;
            if (i >= lines.Count) { foreach (var k in PickKeys) d[p + k] = ""; continue; }
            var l = lines[i];
            bool kinah = l.Kind == "kinah";
            d[p] = kinah ? $"{l.Amount:N0} Kinah" : l.Name;
            d[p + "Id"] = $"{l.At.Ticks}:{l.Kind}:{l.Name}:{l.Amount}";
            d[p + "At"] = l.At.Ticks.ToString();
            d[p + "Count"] = kinah ? (l.Amount >= 10_000 ? Fk(l.Amount) : l.Amount.ToString()) : $"×{l.Amount:N0}";
            d[p + "Worth"] = PickupValue(l).Text;
            d[p + "Grade"] = kinah ? "" : GradeOf(l.Name);
            d[p + "Icon"] = kinah ? KinahIcon : _items.IconUrl(l.ItemId != 0 ? l.ItemId : _items.IdOf(l.Name));
            d[p + "Note"] = kinah ? "" : l.Bound ? "bound" : GatheringCatalog.Find(l.Name) != null ? "gathered" : "";
            d[p + "Kind"] = l.Kind;
        }
        return d;
    }

    /// <summary>The rare drop the looks show (AlertTick), held for <see cref="HudRareSeconds"/> like the strip's alert.</summary>
    Dictionary<string, string> _hudRare = new();
    DateTime _hudRareUntil = DateTime.MinValue;
    const int HudRareSeconds = 5;

    /// <summary>A rare or watched drop for the Card and the Ticker: its name, count, grade, icon and the alert's colour.</summary>
    void HoldRareOnHud(string name, string count, string grade, Color color, DateTime at)
    {
        _hudRare = new()
        {
            ["rare"] = name, ["rareId"] = at.Ticks.ToString(), ["rareCount"] = count, ["rareGrade"] = grade,
            ["rareIcon"] = _items.IconUrl(_items.IdOf(name)), ["rareColor"] = $"#{color.R:X2}{color.G:X2}{color.B:X2}",
        };
        _hudRareUntil = DateTime.UtcNow.AddSeconds(HudRareSeconds);
    }

    /// <summary>
    /// The panel's figures as the mock draws them (Western Cantas Valley, 42:17, 1.27M an hour, 894K, and four pickups),
    /// for Options' preview of the three looks and the looks' layout renders: the same with the game closed, never the
    /// player's. The icons come from the window's icon cache.
    /// </summary>
    internal Dictionary<string, string> HudSample()
    {
        var d = new Dictionary<string, string>
        {
            ["show"] = "", ["unlocked"] = "", ["run"] = "1", ["time"] = "RUN 42:17", ["warm"] = "", ["rate"] = "1.27M", ["kinah"] = "512K", ["loot"] = "758K",
            ["total"] = "894K", ["best"] = "1.40M", ["last"] = "Spiritstone ×1", ["zone"] = "Western Cantas Valley", ["paused"] = "", ["notice"] = "", ["flash"] = "", ["flag"] = "",
            ["feedText"] = "Spiritstone", ["feedValue"] = "+7.5K", ["feedRate"] = "1.27M", ["feedUnit"] = "an hour",
        };
        var t = DateTime.UtcNow;
        var picks = new (string Name, string Count, string Worth, string Kind, string Note, string Icon)[]
        {
            ("Spiritstone", "×1", "+7.5K", "item", "", ""),
            ("1,192 Kinah", "1192", "+1.2K", "kinah", "", KinahIcon),
            ("Thick hide", "×1", "+1.1K", "item", "", ""),
            ("Orichalcum Ore", "×1", "+2.0K", "item", "gathered", ""),
        };
        for (int i = 0; i < picks.Length; i++)
        {
            var (name, count, worth, kind, note, icon) = picks[i];
            var p = "pick" + i;
            d[p] = name; d[p + "Id"] = "sample:" + i; d[p + "At"] = t.AddSeconds(-2 - 3 * i).Ticks.ToString(); d[p + "Count"] = count; d[p + "Worth"] = worth;
            d[p + "Grade"] = kind == "kinah" ? "" : GradeOf(name); d[p + "Kind"] = kind; d[p + "Note"] = note;
            d[p + "Icon"] = icon.Length > 0 ? icon : _items.IconUrl(_items.IdOf(name));
        }
        foreach (var k in RareKeys) d[k] = "";
        return d;
    }

    /// <summary>The Card and the Ticker in the mock's three states (normal, with the reader paused, with a rare drop), for layout renders.</summary>
    internal List<(string File, string Look, Dictionary<string, string> Values)> HudLookStates()
    {
        var normal = HudSample();
        var notice = new Dictionary<string, string>(normal) { ["paused"] = "Paused: Chrome covers the chat", ["show"] = "dim" };
        var rare = new Dictionary<string, string>(normal)
        {
            ["rare"] = "Shade Gloves", ["rareId"] = "sample", ["rareCount"] = "×1", ["rareGrade"] = "Legend",
            ["rareIcon"] = _items.IconUrl(_items.IdOf("Shade Gloves")), ["rareColor"] = "#5AA6FF",
        };
        var list = new List<(string, string, Dictionary<string, string>)>();
        foreach (var look in new[] { HudLooks.Card, HudLooks.Ticker })
            foreach (var (state, v) in new[] { ("normal", normal), ("notice", notice), ("rare", rare) })
                list.Add(($"ui-hud-{look}-{state}.png", look, v));
        return list;
    }

    /// <summary>Where the player last dropped the panel in its look: the Ticker's spot is its own (HudPrefs.TickerX/Y).</summary>
    (double? X, double? Y) HudSpotSaved() => HudLook == HudLooks.Ticker ? (_hudPrefs.TickerX, _hudPrefs.TickerY) : (_prefs.HudX, _prefs.HudY);

    /// <summary>A drag of the panel: remembered for its look (never in a layout render).</summary>
    void RememberHudSpot(double x, double y)
    {
        if (_demo) return;
        if (HudLook == HudLooks.Ticker) { _hudPrefs.TickerX = x; _hudPrefs.TickerY = y; }
        else { _prefs.HudX = x; _prefs.HudY = y; }
    }

    /// <summary>Options' Save with a new look: the panel is made again in it, where that look was last left.</summary>
    void ApplyHudLook(string look)
    {
        look = HudLooks.Parse(look);
        if (look == HudLook) return;
        _hudPrefs.Look = look;
        bool had = _hud != null;
        CloseHud();
        if (had) EnsureHud();
    }
}
