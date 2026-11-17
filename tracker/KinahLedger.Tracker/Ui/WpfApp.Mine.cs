using System.IO;
using System.Windows;
using KinahLedger.Parser;
using KinahLedger.Session;

namespace KinahLedger.Ui;

public static partial class WpfApp
{
    /// <summary>
    /// ui-shot DIR --parser mine: the overlays as a player's own settings draw them, from hud.json and parser.json in
    /// KL_MINE_DIR (by default the tracker's own folder, only read): the Kinah overlay in its look at its Size, and the
    /// Parser's window in its look, width, details and heal bars at that Size (the Size is the Kinah overlay's for both), in
    /// each state a dungeon shows; ui-mine-*.png, and spill.txt lists any frame that draws past its own edges. (2026-11-15:
    /// fixes tested at 100% did nothing at Steven's 110% and click-through on; check a player's settings, not the defaults.)
    /// </summary>
    static void ShotMine(MainWindow w, string dir)
    {
        var src = Environment.GetEnvironmentVariable("KL_MINE_DIR") is { Length: > 0 } d ? d
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "KinahLedger");
        string? Text(string name) { try { var f = Path.Combine(src, name); return File.Exists(f) ? File.ReadAllText(f) : null; } catch (IOException) { return null; } }
        var hud = Text("hud.json") is { } ht ? HudPrefs.FromText(ht, out _) ?? HudPrefs.Defaults() : HudPrefs.Defaults();
        var pp = Text("parser.json") is { } pt ? ParserPrefs.FromText(pt) : new ParserPrefs();
        try { File.Delete(Path.Combine(dir, "spill.txt")); } catch (IOException) { }
        Console.WriteLine($"settings from {src}: Kinah overlay {hud.Look} at {hud.Scale:0.##}x; Parser {pp.Look}, width {pp.Width}, details {pp.Details}, heal bars {pp.HealBars}, expanded {pp.Expanded}, click-through {pp.ClickThrough}");

        double fw0 = HudStackLook.FightWidth; bool exp0 = ParserRows.ExpandedPref, heal0 = ParserRows.HealBarsPref;
        HudStackLook.FightWidth = pp.Width + (pp.Expanded ? 70 : 0);
        ParserRows.ExpandedPref = pp.Expanded; ParserRows.HealBarsPref = pp.HealBars;
        try
        {
            var now = new DateTime(2026, 11, 13, 21, 30, 0, DateTimeKind.Utc);
            var (past, live) = ParserDemo.Sample(now, live: true, damage: true);
            var (_, liveNd) = ParserDemo.Sample(now, live: true, damage: false);
            var (_, liveRun) = ParserDemo.Sample(now, live: true, mode: ParserMode.Run);
            var kill = past.Last(e => e.Outcome == "kill");
            var wipe = past.Last(e => e.Outcome == "wipe");
            var held = HeldSample(live!, now);
            var ready = Encounter.Ready(KinahLedger.Packets.ContentKind.Expedition, "Ferocious Horn Den", "", now);
            var shots = new List<(string Tag, Encounter E, DateTime At)>
            {
                ("live", live!, now), ("nodamage", liveNd!, now), ("run", liveRun!, now), ("ready", ready, now),
                ("ready-party", ReadyWithParty(Encounter.Ready(KinahLedger.Packets.ContentKind.Expedition, "Ferocious Horn Den", "", now), live!), now),
                ("held", held, held.End!.Value), ("killed", kill, kill.End!.Value), ("wiped", wipe, wipe.End!.Value),
            };
            foreach (var (tag, e, at) in shots)
            {
                var o = new ParserOverlay(still: true) { Left = -12000, Top = -12000, Mode = e.ModeKind, Look = pp.Look, ShowDetails = pp.Details };
                o.SetScale(hud.Scale);
                o.ApplyFightWidth();
                o.Draw(e, at); o.Show(); DoEvents();
                var file = $"ui-mine-parser-{tag}.png";
                Save(Scaled(o), Path.Combine(dir, file), 0, ShotGround);
                SpillNote(dir, file, (FrameworkElement)o.Content);
                o.Close();
            }
            // the party before the first pull in every look the Parser's window takes (0.10.132 drew it on the classic window only)
            foreach (var lk in HudLooks.FightLooks.Concat(HudLooks.All.Select(l => l.Key).Where(HudThemes.Themed)).Distinct())
            {
                var e = ReadyWithParty(Encounter.Ready(KinahLedger.Packets.ContentKind.Expedition, "Ferocious Horn Den", "", now), live!);
                var o = new ParserOverlay(still: true) { Left = -12000, Top = -12000, Mode = e.ModeKind, Look = lk, ShowDetails = pp.Details };
                o.SetScale(hud.Scale);
                o.ApplyFightWidth();
                o.Draw(e, now); o.Show(); DoEvents();
                var file = $"ui-mine-ready-party-{lk}.png";
                Save(Scaled(o), Path.Combine(dir, file), 0, ShotGround);
                SpillNote(dir, file, (FrameworkElement)o.Content);
                o.Close();
            }
            // the Kinah overlay in its own look and Size: farming, the Abyss, and a boss fight in it (the meter in place of the Kinah line)
            var kinah = w.HudValues.ToDictionary(kv => kv.Key, kv => kv.Value);
            var abyss = w.HudAbyssSample(kinah);
            foreach (var (tag, v, e, at) in new[] { ("farming", kinah, (Encounter?)null, now), ("abyss", abyss, null, now), ("boss", kinah, live, now), ("killed", kinah, kill, kill.End!.Value) })
            {
                var h = new HudWindow(null, hud.Look, still: true) { Left = -12000, Top = -12000, ShowActivated = false };
                h.SetScale(hud.Scale);
                h.Update(v);
                if (e != null) { h.ShowParser(e, at, e.ModeKind, details: pp.Details); h.SetKinahHidden(true); }
                h.Show(); DoEvents();
                var file = $"ui-mine-kinah-{tag}.png";
                Save(Scaled(h), Path.Combine(dir, file), 0, ShotGround);
                SpillNote(dir, file, (FrameworkElement)h.Content);
                h.Close();
            }
        }
        finally { HudStackLook.FightWidth = fw0; ParserRows.ExpandedPref = exp0; ParserRows.HealBarsPref = heal0; }
        // the window's own root: its size is the scaled content's (the content's own is the size before the Size's scale)
        static FrameworkElement Scaled(Window win) => System.Windows.Media.VisualTreeHelper.GetChildrenCount(win) > 0 && System.Windows.Media.VisualTreeHelper.GetChild(win, 0) is FrameworkElement r ? r : (FrameworkElement)win.Content;
        var spill = Path.Combine(dir, "spill.txt");
        Console.WriteLine(File.Exists(spill) ? "drawn past an edge:" + Environment.NewLine + File.ReadAllText(spill).TrimEnd() : "nothing drawn past an edge");
    }
}
