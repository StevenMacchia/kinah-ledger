using System.IO;
using System.Windows;
using KinahLedger.Parser;
using KinahLedger.Session;

namespace KinahLedger.Ui;

public static partial class WpfApp
{
    /// <summary>
    /// ui-shot DIR --parser themes: every theme's look (Ui/Themes/) in each state it draws, over the game-like grey
    /// (KL_SHOT_CLEAR=1: clear, to lay over game pictures): ui-theme-LOOK-STATE.png. KL_THEME_LOOK=sticker,neon renders those
    /// only. The moments' effects are drawn as held frames: ui-theme-LOOK-pickup-40.png is the pickup's effect 40% of the
    /// way through, and so on for the rare drop, your best passed, an entry opening, a fight starting and a kill.
    /// </summary>
    /// <summary>
    /// options-test: the Theme row over the look thumbnails (Classic shown for a Classic look, each theme's chip showing its
    /// three looks), the themes' looks known to every place that takes a look key, and none of their words naming where the
    /// figures come from, in any state.
    /// </summary>
    static void ThemeChecks(OptionsWindow o, MainWindow w, Action<bool, string> check)
    {
        var themes = HudThemes.All.Select(t => t.Key).ToList();
        check(o.HudThemeForTest == HudThemes.Of(w.HudLook) && o.HudThemeHost.Children.Count == themes.Count && themes.SequenceEqual(new[] { HudThemes.Classic, HudThemes.Wacky, HudThemes.Tactical, HudThemes.Glitch }),
            $"Options › In game: a Theme row (Classic, Wacky, Tactical, Glitch) over the thumbnails, the look in use's theme shown ({o.HudThemeForTest}, {o.HudThemeHost.Children.Count} chips)");
        var said = new List<string>();
        foreach (var t in themes.Where(t => t != HudThemes.Classic))
        {
            o.PickHudThemeForTest(t); DoEvents();
            var shown = o.HudLookHost.Children.OfType<FrameworkElement>().Select(c => c.Tag as string).ToList();
            var want = HudThemes.Looks(t).Select(l => l.Key).ToList();
            said.Add($"{t}: {string.Join(",", shown)}");
            check(want.Count == 3 && shown.SequenceEqual(want) && o.HudLookPreview.Children.Count == 1 && o.ParserLooksForTest.Keys.Where(k => k != KinahLedger.Parser.ParserPrefs.StandardLook).SequenceEqual(want),
                $"...{t} shows its three looks as thumbnails and in the Parser's look row, the look in use still drawn big ({string.Join(", ", shown)})");
        }
        o.PickHudThemeForTest(HudThemes.Of(w.HudLook)); DoEvents();
        var keys = HudLooks.All.Where(l => HudThemes.Themed(l.Key)).Select(l => l.Key).ToList();
        check(keys.Count == 9 && keys.All(k => HudLooks.Parse(k) == k && KinahLedger.Parser.ParserPrefs.LookOf(k) == k && HudThemes.Make(k, true) != null) && !HudThemes.Themed(HudLooks.Capsule),
            $"the themes' nine looks are kept in hud.json and parser.json and made by both windows ({string.Join(", ", keys)})");
        // no theme's look ever says where the figures come from (Steven, 2026-11-14), farming, in the Abyss, in a fight and after it
        var now = DateTime.UtcNow;
        var (past, live) = ParserDemo.Sample(now, live: true, damage: true);
        var kill = past.Last(e => e.Outcome == "kill");
        var kinah = w.HudValues.ToDictionary(kv => kv.Key, kv => kv.Value);
        var bad = new List<string>();
        foreach (var k in keys)
            foreach (var (v, e, at) in new[] { (kinah, (Encounter?)null, now), (w.HudAbyssSample(kinah), null, now), (kinah, live, now), (kinah, kill, kill.End!.Value) })
            {
                var h = new HudWindow(null, k, still: true) { Left = -12000, Top = -12000, ShowActivated = false };
                h.Update(v); h.ShowParser(e, at, ParserMode.Bosses, details: false); h.SetKinahHidden(e != null);
                h.Show(); DoEvents();
                foreach (var x in VisibleTexts(h).Where(x => System.Text.RegularExpressions.Regex.IsMatch(x, "packet|traffic|game's messages|capture", System.Text.RegularExpressions.RegexOptions.IgnoreCase))) bad.Add($"{k}: {x}");
                h.Close();
            }
        check(bad.Count == 0, "the themes' looks never name packets or traffic" + (bad.Count > 0 ? ": " + string.Join("; ", bad) : ""));
    }

    static void ShotThemes(MainWindow w, string dir)
    {
        var only = (Environment.GetEnvironmentVariable("KL_THEME_LOOK") ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var looks = HudLooks.All.Where(l => HudThemes.Themed(l.Key) && (only.Length == 0 || only.Contains(l.Key))).Select(l => l.Key).ToList();
        var kinah = w.HudValues.ToDictionary(kv => kv.Key, kv => kv.Value);
        var sample = w.HudSample();
        foreach (var (k, v) in sample) if (k.StartsWith("pick") || k.StartsWith("top")) kinah[k] = v;   // the four pickups, for the pickup's effect
        var abyss = w.HudAbyssSample(kinah);
        var abyssWaiting = new Dictionary<string, string>(abyss) { ["rate"] = "", ["total"] = "", ["feedRate"] = "", ["abPvp"] = "", ["abKills"] = "2", ["abRank"] = "", ["abDeaths"] = "", ["time"] = "RUN 0:48" };
        var notice = new Dictionary<string, string>(kinah) { ["paused"] = "Paused: Chrome covers the chat", ["show"] = "dim" };
        var warming = new Dictionary<string, string>(kinah) { ["warm"] = "1", ["rate"] = "warming up", ["time"] = "RUN 0:48", ["total"] = "21.4K", ["chase"] = "", ["toBest"] = "" };
        var best = new Dictionary<string, string>(kinah) { ["chase"] = "1.000", ["toBest"] = "New best here", ["rate"] = "1.52M" };
        var idle = new Dictionary<string, string>(kinah) { ["run"] = "", ["time"] = "0:00", ["rate"] = "", ["total"] = "0", ["chase"] = "", ["toBest"] = "" };
        foreach (var k in idle.Keys.Where(k => k.StartsWith("pick") || k.StartsWith("top")).ToList()) idle[k] = "";
        var rare = new Dictionary<string, string>(kinah)
        {
            ["rare"] = "Shade Gloves", ["rareId"] = "sample", ["rareCount"] = "×1", ["rareGrade"] = "Legend", ["rareIcon"] = sample.GetValueOrDefault("topIcon") ?? "", ["rareColor"] = "#5AA6FF",
        };
        foreach (var url in kinah.Concat(abyss).Concat(rare).Where(kv => kv.Key.EndsWith("Icon")).Select(kv => kv.Value).Distinct()) IconCache.Get(url);
        IconCache.Drain(TimeSpan.FromSeconds(10)); DoEvents();

        var now = new DateTime(2026, 11, 13, 21, 30, 0, DateTimeKind.Utc);
        var (past, live) = ParserDemo.Sample(now, live: true, damage: true);
        var (_, liveRun) = ParserDemo.Sample(now, live: true, mode: ParserMode.Run);
        var kill = past.Last(e => e.Outcome == "kill");
        var wipe = past.Last(e => e.Outcome == "wipe");
        var held = HeldSample(live!, now);
        var ready = Encounter.Ready(KinahLedger.Packets.ContentKind.Expedition, "Ferocious Horn Den", "", now);
        var due = new List<TimerBoard.Item>
        {
            new("event:Shugo Festival", "Events", "Shugo Festival", "", TimerBoard.Kind.On, now.AddSeconds(161), "2m 41s", "left to enter") { Short = "Shugo" },
            new("event:Dimensional Invasion", "Events", "Dimensional Invasion", "", TimerBoard.Kind.Waiting, now.AddSeconds(279), "4m 39s", "") { Short = "Invasion" },
            new("boss:Soul Ruler Kashapa", "Verteron", "Soul Ruler Kashapa", "", TimerBoard.Kind.Waiting, now.AddSeconds(719), "11m 59s", "") { Short = "Kashapa" },
        };
        var soon = new List<TimerBoard.Item> { due[1], due[2] };

        // (state, values, timers, fight and its moment, in a dungeon)
        var states = new List<(string Tag, Dictionary<string, string> V, List<TimerBoard.Item>? T, Encounter? E, DateTime At, bool Dungeon)>
        {
            ("farming", kinah, null, null, now, false),
            ("timer", kinah, soon, null, now, false),
            ("enter", kinah, due, null, now, false),
            ("idle", idle, null, null, now, false),
            ("warming", warming, null, null, now, false),
            ("best", best, soon, null, now, false),
            ("notice", notice, null, null, now, false),
            ("rare", rare, null, null, now, false),
            ("abyss", abyss, soon, null, now, false),
            ("abyss-waiting", abyssWaiting, null, null, now, false),
            ("boss", kinah, due, live, now, true),
            ("boss-open", kinah, soon, live, now, false),   // a world boss in the open: the fight with the Kinah line
            ("run", kinah, null, liveRun, now, true),
            ("ready", kinah, due, ready, now, true),
            ("held", kinah, null, held, held.End!.Value, true),
            ("killed", kinah, null, kill, kill.End!.Value, true),
            ("wiped", kinah, null, wipe, wipe.End!.Value, true),
        };
        // the moments, held at points of their course (Motion.FrozenAt), each from the state it plays in
        var moments = new List<(string Tag, Dictionary<string, string> V, List<TimerBoard.Item>? T, Encounter? E, DateTime At, bool Dungeon)>
        {
            ("pickup", kinah, null, null, now, false), ("rare-drop", rare, null, null, now, false), ("best-passed", best, null, null, now, false),
            ("entry-opened", kinah, due, null, now, false), ("fight-started", kinah, null, live, now, true), ("fight-ended", kinah, null, kill, kill.End!.Value, true),
        };
        double? frozen0 = Motion.FrozenAt;
        foreach (var look in looks)
        {
            void Shot(string tag, Dictionary<string, string> v, List<TimerBoard.Item>? t, Encounter? e, DateTime at, bool dungeon, double? frame)
            {
                var h = new HudWindow(null, look, still: true) { Left = -12000, Top = -12000, ShowActivated = false };
                h.Update(v);
                if (t != null) h.ShowTimers(t, at);
                if (e != null) h.ShowParser(e, at, e.ModeKind, details: false);
                if (dungeon) h.SetKinahHidden(true);
                h.Show(); DoEvents();
                if (frame is double f)
                {
                    // only this moment's effect: the look's MotionFrame plays every moment its figures hold, so the values are cut to this one's
                    Motion.FrozenAt = f;
                    h.MotionFrameForRender(v);
                    DoEvents();
                    Motion.FrozenAt = frozen0;
                }
                Save((FrameworkElement)h.Content, Path.Combine(dir, $"ui-theme-{look}-{tag}{(frame is double ff ? "-" + Math.Round(ff * 100) : "")}.png"), 0, ShotGround);
                h.Close();
            }
            try
            {
                foreach (var s in states) Shot(s.Tag, s.V, s.T, s.E, s.At, s.Dungeon, null);
                foreach (var m in moments)
                    foreach (var f in new[] { 0.15, 0.4, 0.75 })
                        Shot(m.Tag, MomentOnly(m.Tag, m.V), m.T, m.E, m.At, m.Dungeon, f);
                // the widest meter, and the Parser's own window in this look (Settings › In game › Parser › Overlay look)
                double w0 = HudStackLook.FightWidth; HudStackLook.FightWidth = HudStackLook.FightWidthMax;
                Shot("boss-wide", kinah, due, live, now, true, null);
                HudStackLook.FightWidth = w0;
                foreach (var (tag, e, at) in new[] { ("live", live!, now), ("killed", kill, kill.End!.Value) })
                {
                    var o = new ParserOverlay(still: true) { Left = -12000, Top = -12000, Mode = e.ModeKind, Look = look };
                    o.Draw(e, at); o.Show(); DoEvents();
                    Save((FrameworkElement)o.Content, Path.Combine(dir, $"ui-theme-{look}-overlay-{tag}.png"), 0, ShotGround);
                    o.Close();
                }
            }
            catch (Exception ex) { Console.WriteLine($"{look}: {ex}"); }
        }
        Motion.FrozenAt = frozen0;

        // a moment's values with only what makes that moment: no rare drop or best on a plain pickup's frame, and so on
        static Dictionary<string, string> MomentOnly(string tag, Dictionary<string, string> v)
        {
            var c = new Dictionary<string, string>(v);
            if (tag != "rare-drop") c["rare"] = "";
            if (tag != "best-passed" && c.GetValueOrDefault("toBest", "").StartsWith("New")) { c["chase"] = "0.907"; c["toBest"] = "130K to your best here"; }
            if (tag is "rare-drop" or "best-passed" or "entry-opened" or "fight-started" or "fight-ended") foreach (var k in c.Keys.Where(k => k.StartsWith("pick")).ToList()) c[k] = "";
            return c;
        }
    }
}
