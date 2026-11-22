using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
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
        check(o.HudThemeForTest == HudThemes.Of(w.HudLook) && o.HudThemeHost.Children.Count == themes.Count && themes.SequenceEqual(new[] { HudThemes.Classic, HudThemes.Wacky, HudThemes.Tactical, HudThemes.Glitch, HudThemes.Modern, HudThemes.Atreia, HudThemes.Esports, HudThemes.Elements, HudThemes.Overdrive }),
            $"Options › In game: a Theme row (Classic, Wacky, Tactical, Glitch, Modern, Atreia, Esports, Elements, Overdrive) over the thumbnails, the look in use's theme shown ({o.HudThemeForTest}, {o.HudThemeHost.Children.Count} chips)");
        var said = new List<string>();
        foreach (var t in themes.Where(t => t != HudThemes.Classic))
        {
            var meterBefore = o.ParserThemeForTest;
            o.PickHudThemeForTest(t); DoEvents();
            var shown = o.HudLookHost.Children.OfType<FrameworkElement>().Select(c => c.Tag as string).ToList();
            var want = HudThemes.Looks(t).Select(l => l.Key).ToList();
            said.Add($"{t}: {string.Join(",", shown)}");
            // the DPS meter's look row has a theme of its own (Steven, 2026-11-17: "you shouldnt have to choose the corresponding
            // buddy ... in order to choose the DPS overlay"): the Buddy's theme leaves it alone, the meter's own chips change it
            bool meterKept = o.ParserThemeForTest == meterBefore;
            o.PickParserThemeForTest(t); DoEvents();
            check(want.Count >= 3 && shown.SequenceEqual(want) && o.HudLookPreview.Children.Count == 1 && meterKept && o.ParserThemeForTest == t && o.ParserLooksForTest.Keys.SequenceEqual(want),
                $"...{t} shows its {want.Count} looks as thumbnails; the DPS meter's row keeps its own theme, and its {t} chip shows the same ones ({string.Join(", ", shown)} | {string.Join(", ", o.ParserLooksForTest.Keys)})");
            o.PickParserThemeForTest(meterBefore ?? HudThemes.Classic); DoEvents();
        }
        o.PickHudThemeForTest(HudThemes.Of(w.HudLook)); DoEvents();
        var keys = HudLooks.All.Where(l => HudThemes.Themed(l.Key)).Select(l => l.Key).ToList();
        check(keys.Count >= 13 && keys.All(k => HudLooks.Parse(k) == k && KinahLedger.Parser.ParserPrefs.LookOf(k) == k && HudThemes.Make(k, true) != null) && !HudThemes.Themed(HudLooks.Capsule),
            $"the themes' {keys.Count} looks are kept in hud.json and parser.json and made by both windows ({string.Join(", ", keys)})");
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
        // the party before the first pull in every look (0.10.132 drew it on the classic Parser window only; Steven's is Neon):
        // the ready line and each member's row with their Combat Power, in the Parser's window and on the Kinah overlay; alone, no rows
        {
            var party = ReadyWithParty(Encounter.Ready(KinahLedger.Packets.ContentKind.Expedition, "Ferocious Horn Den", "", now), live!);
            var names = party.Roster.Where(f => !f.Other).Select(f => f.Name).ToList();
            var solo = Encounter.Ready(KinahLedger.Packets.ContentKind.Expedition, "Ferocious Horn Den", "", now);
            solo.Roster.Add(new Fighter { Id = 1, Name = names[0], Class = "Gladiator", CombatPower = 4_812, You = true });
            var missing = new List<string>();
            foreach (var k in keys.Concat(HudLooks.FightLooks).Distinct())
            {
                var po = new ParserOverlay(still: true) { Left = -12000, Top = -12000, Mode = ParserMode.Bosses, Look = k };
                po.Draw(party, now); po.Show(); DoEvents();
                var t = string.Join(" ", VisibleTexts(po));
                var gone = names.Where(n => !t.Contains(n, StringComparison.OrdinalIgnoreCase)).ToList();
                if (gone.Count > 0 || !t.Contains("Waiting for the first pull", StringComparison.OrdinalIgnoreCase)) missing.Add($"{k}: {(gone.Count > 0 ? "no row for " + string.Join(", ", gone) : "no ready line")}");
                po.Draw(solo, now); DoEvents();
                if (VisibleTexts(po).Any(x => x.Contains("4,812"))) missing.Add($"{k}: a row on your own");
                po.Close();
            }
            check(missing.Count == 0, $"the party before the first pull: every look's Parser window lists its {names.Count} members under the ready line, and none on your own" + (missing.Count > 0 ? ": " + string.Join("; ", missing) : ""));
        }
        // each look's own skills card on a row (Steven, 2026-11-16: "the extra DPS stats per player display classic standard
        // meter"), with the card's figures; and Combat Power beside every name on the meter ("dont forget the combat power")
        var cards = new List<string>();
        var ranked = live!.Ranked().Where(f => !f.Other).ToList();
        foreach (var k in keys)
        {
            var h = new HudWindow(null, k, still: true) { Left = -12000, Top = -12000, ShowActivated = false };
            h.Update(kinah); h.ShowParser(live, now, ParserMode.Bosses, details: false); h.SetKinahHidden(true);
            h.Show(); DoEvents();
            var texts = VisibleTexts(h);
            var rows = ThemeRowsOf(h).FirstOrDefault();
            var top = ranked[0];
            var card = rows?.CardForTest(top.Id);
            var mc = MemberCard.Of(live, top, now);
            var cardTexts = card == null ? new List<string>() : LogicalTexts(card).ToList();   // a card not yet shown: its words by the logical tree
            string cardText = string.Join(" ", cardTexts);
            var missing = new List<string>();
            if (HudThemes.Make(k, true) is not IThemeCard) missing.Add("its own card");
            if (card == null || mc == null) missing.Add("a card");
            else
            {
                foreach (var want in new[] { mc.Name, mc.Dps, mc.Share, mc.Damage, mc.Cp, mc.Abilities.FirstOrDefault()?.Name ?? "" }.Where(x => x.Length > 0))
                    if (!cardText.Contains(want, StringComparison.OrdinalIgnoreCase)) missing.Add($"card {want}");
            }
            string rowText = string.Join(" ", texts);
            foreach (var f in ranked.Take(KinahLedger.Ui.ParserRows.CompactRows).Where(f => f.CombatPower > 0))
                if (!rowText.Contains(Amounts.Meter(f.CombatPower))) missing.Add($"CP {Amounts.Meter(f.CombatPower)} by {f.Name}");
            if (missing.Count > 0) cards.Add($"{k}: {string.Join(", ", missing)}");
            h.Close();
        }
        // one fight through every look's fullest card: the same "N more" figure, deaths and specialties in each (a figure
        // that changes with the theme can't ship)
        {
            var full = FullCardSample(KinahLedger.Ui.WpfApp.WithKindsForTest(live));
            string? ck0 = Encounter.CritKey, bk0 = Encounter.BackKey;
            Encounter.CritKey = Encounter.CritKeyOf(KinahLedger.Parser.ParserPrefs.CritPick); Encounter.BackKey = Encounter.BackKeyOf(KinahLedger.Parser.ParserPrefs.BackPick);
            var youF = full.Roster.First(f => f.You);
            var mcF = MemberCard.Of(full, youF, now)!;
            foreach (var k in keys)
            {
                var h = new HudWindow(null, k, still: true) { Left = -12000, Top = -12000, ShowActivated = false };
                h.Update(kinah); h.ShowParser(full, now, ParserMode.Bosses, details: false); h.SetKinahHidden(true);
                h.Show(); DoEvents();
                var card = ThemeRowsOf(h).FirstOrDefault()?.CardForTest(youF.Id);
                string t = card == null ? "" : string.Join(" ", LogicalTexts(card));
                var miss = new[] { mcF.More, mcF.MoreDamage, mcF.DeathsText.Split(' ')[0], mcF.Abilities.First(a => a.Picks.Length > 0).Picks }.Where(x => !t.Replace(" ", "").Contains(x.Replace(" ", ""), StringComparison.OrdinalIgnoreCase)).ToList();   // "1 · 2" spaced is the same
                if (miss.Count > 0) cards.Add($"{k} full card: missing {string.Join(", ", miss)}");
                h.Close();
            }
            Encounter.CritKey = ck0; Encounter.BackKey = bk0;
        }
        check(cards.Count == 0, "every theme's look opens its own skills card on a row, with the member's DPS, share, damage, Combat Power and top skill, and shows each member's Combat Power on the meter" + (cards.Count > 0 ? ": " + string.Join("; ", cards) : ""));
    }

    /// <summary>
    /// --parser looks: every look of the Parser's own window as Settings › DPS meter's preview stage draws it (the stage's
    /// width, over the game scene, scaled down only past it): ui-settings-look-&lt;key&gt;.png, to catch a look cut off there.
    /// </summary>
    static void ShotSettingsLooks(string dir)
    {
        var now = new DateTime(2026, 11, 13, 21, 30, 0, DateTimeKind.Utc);
        var (_, fight) = KinahLedger.Parser.ParserDemo.Sample(now, live: true);
        foreach (var theme in HudThemes.All)
            foreach (var key in OptionsWindow.ParserLooksOf(theme.Key))
            {
                var face = ParserOverlay.PreviewFace(key, fight, now, fight.ModeKind, false);
                face.HorizontalAlignment = HorizontalAlignment.Center; face.VerticalAlignment = VerticalAlignment.Center;
                var stage = new System.Windows.Controls.Border { Width = 600, CornerRadius = new CornerRadius(8), Padding = new Thickness(16, 14, 16, 14), MinHeight = 200, Background = HudKit.GameScene(),
                    Child = new System.Windows.Controls.Viewbox { Child = face, Stretch = Stretch.Uniform, StretchDirection = System.Windows.Controls.StretchDirection.DownOnly } };
                stage.Measure(new Size(600, double.PositiveInfinity)); stage.Arrange(new Rect(stage.DesiredSize)); stage.UpdateLayout();
                SavePart(stage, Path.Combine(dir, "ui-settings-look-" + key + ".png"), null);
            }
    }

    /// <summary>The moments' held frames: 15/40/75%, or with KL_MOMENT_SEQ=N every 1/N from 0 to 1.</summary>
    static double[] MomentFrames() => int.TryParse(Environment.GetEnvironmentVariable("KL_MOMENT_SEQ"), out var n) && n > 1
        ? Enumerable.Range(0, n + 1).Select(i => i / (double)n).ToArray() : new[] { 0.15, 0.4, 0.75 };

    static void ShotThemes(MainWindow w, string dir)
    {
        var only = (Environment.GetEnvironmentVariable("KL_THEME_LOOK") ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        try { File.Delete(Path.Combine(dir, "spill.txt")); } catch (IOException) { }   // what each frame draws past its edges (SpillNote)
        // a classic look (auto, capsule, meter, float, card) only when named: KL_THEME_LOOK=auto,card renders those in the same states
        var looks = HudLooks.All.Where(l => (HudThemes.Themed(l.Key) || only.Length > 0) && (only.Length == 0 || only.Contains(l.Key))).Select(l => l.Key).ToList();
        // the pickup icons (Settings › Buddy › More › Pickup icons) on as shipped; KL_PICK_ICONS=0 renders them off. Render-only.
        bool picksShown0 = PickStrip.Shown;
        PickStrip.Shown = Environment.GetEnvironmentVariable("KL_PICK_ICONS") != "0";
        var kinah = w.HudValues.ToDictionary(kv => kv.Key, kv => kv.Value);
        var sample = w.HudSample();
        foreach (var (k, v) in sample) if (k.StartsWith("pick") || k.StartsWith("top")) kinah[k] = v;   // the four pickups, for the pickup's effect
        var abyss = w.HudAbyssSample(kinah);
        var abyssWaiting = new Dictionary<string, string>(abyss) { ["rate"] = "", ["total"] = "", ["feedRate"] = "", ["abPvp"] = "", ["abKills"] = "2", ["abRank"] = "", ["abDeaths"] = "", ["time"] = "RUN 0:48" };
        var notice = new Dictionary<string, string>(kinah) { ["paused"] = "Paused: Chrome covers the chat", ["show"] = "dim" };
        var warming = new Dictionary<string, string>(kinah) { ["warm"] = "1", ["rate"] = "warming up", ["time"] = "RUN 0:48", ["total"] = "21.4K", ["chase"] = "", ["toBest"] = "" };
        var best = new Dictionary<string, string>(kinah) { ["chase"] = "1.000", ["toBest"] = L.T("New best here"), ["rate"] = "1.52M" };
        var idle = new Dictionary<string, string>(kinah) { ["run"] = "", ["time"] = "0:00", ["rate"] = "", ["total"] = "0", ["chase"] = "", ["toBest"] = "", ["idle"] = MainWindow.IdleSample(), ["idleShort"] = MainWindow.IdleShortSample() };
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
            // before the first count with farming done today: the whole Buddy on today's figures (Steven, 2026-11-21)
            ("today", new Dictionary<string, string>(idle) { ["todayRate"] = "568K", ["todayTotal"] = "1.27M" }, soon, null, now, false),
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
        // the Buddy in a dungeon run (Ui/MainWindow.HudRun.cs): the sample runs through the live values' path, the pickups kept
        Dictionary<string, string> Run(string kind)
        {
            var v = new Dictionary<string, string>(kinah) { ["idle"] = "", ["chase"] = "", ["toBest"] = "" };
            foreach (var (k, x) in MainWindow.RunBuddyValues(MainWindow.RunBuddySample(kind))) v[k] = x;
            return v;
        }
        foreach (var kind in new[] { "run", "split", "ahead", "behind", "newbest", "clear", "between", "first" })
            states.Add(("dungeon-" + kind, Run(kind), null, null, now, false));
        states.Add(("dungeon-timer", Run("ahead"), soon, null, now, false));
        // KL_THEME_STATE=dungeon: only the states whose names start so, no moments, overlays or cards (fast previews)
        var stateOnly = Environment.GetEnvironmentVariable("KL_THEME_STATE") ?? "";
        if (stateOnly.Length > 0) states.RemoveAll(s => !s.Tag.StartsWith(stateOnly, StringComparison.Ordinal));
        // the moments, held at points of their course (Motion.FrozenAt), each from the state it plays in
        var moments = new List<(string Tag, Dictionary<string, string> V, List<TimerBoard.Item>? T, Encounter? E, DateTime At, bool Dungeon)>
        {
            ("pickup", kinah, null, null, now, false), ("rare-drop", rare, null, null, now, false), ("best-passed", best, null, null, now, false),
            ("run-clear", Run("clear"), null, null, now, false), ("run-newbest", Run("newbest"), null, null, now, false),
            ("entry-opened", kinah, due, null, now, false), ("fight-started", kinah, null, live, now, true), ("fight-ended", kinah, null, kill, kill.End!.Value, true), ("fight-wiped", kinah, null, wipe, wipe.End!.Value, true),
        };
        double? frozen0 = Motion.FrozenAt;
        foreach (var look in looks)
        {
            void Shot(string tag, Dictionary<string, string> v, List<TimerBoard.Item>? t, Encounter? e, DateTime at, bool dungeon, double? frame)
            {
                // the Terminal look types its lines out: a held frame mid-typing shows a cut word ("> reco", "Shade G") that no
                // table can match, so its moment frames draw in English only; the whole lines are checked in the state renders
                if (frame != null && look.Contains("terminal", StringComparison.OrdinalIgnoreCase) && L.Code != "en") return;
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
                string file = $"ui-theme-{look}-{tag}{(frame is double ff ? (MomentFrames().Length > 3 ? $"-s{Array.IndexOf(MomentFrames(), ff):00}" : "-" + Math.Round(ff * 100)) : "")}.png";
                Save((FrameworkElement)h.Content, Path.Combine(dir, file), 0, ShotGround);
                SpillNote(dir, file, (FrameworkElement)h.Content);
                h.Close();
            }
            try
            {
                foreach (var s in states) Shot(s.Tag, s.V, s.T, s.E, s.At, s.Dungeon, null);
                if (stateOnly.Length > 0) continue;
                foreach (var m in moments)
                    // KL_MOMENT_SEQ=N: every moment as N+1 frames, 0 to 1, to judge it moving (tests/ui/clips.py makes the clips);
                    // files ui-theme-LOOK-MOMENT-sNN.png. Without it, the three held frames.
                    foreach (var f in MomentFrames())
                        Shot(m.Tag, MomentOnly(m.Tag, m.V), m.T, m.E, m.At, m.Dungeon, f);
                // the widest meter, and the Parser's own window in this look (Settings › In game › Parser › Overlay look)
                double w0 = HudStackLook.FightWidth; HudStackLook.FightWidth = HudStackLook.FightWidthMax;
                Shot("boss-wide", kinah, due, live, now, true, null);
                HudStackLook.FightWidth = w0;
                // ...and expanded (the damage and crit columns: Steven's meter, 2026-11-21: the bars stopped half way)
                bool x0 = ParserRows.ExpandedPref;
                foreach (var (tag, e, at) in new[] { ("live", live!, now), ("killed", kill, kill.End!.Value), ("expanded", live!, now) })
                {
                    ParserRows.ExpandedPref = tag.StartsWith("expanded") || x0;
                    var o = new ParserOverlay(still: true) { Left = -12000, Top = -12000, Mode = e.ModeKind, Look = look };
                    o.Draw(e, at); o.Show(); DoEvents();
                    Save((FrameworkElement)o.Content, Path.Combine(dir, $"ui-theme-{look}-overlay-{tag}.png"), 0, ShotGround);
                    SpillNote(dir, $"ui-theme-{look}-overlay-{tag}.png", (FrameworkElement)o.Content);
                    o.Close();
                }
                ParserRows.ExpandedPref = x0;
                // the members' skills cards (a row hovered): the top member's, yours, a healer's, and yours with crit and back-attack rates
                {
                    var h = new HudWindow(null, look, still: true) { Left = -12000, Top = -12000, ShowActivated = false };
                    h.Update(kinah);
                    var withKinds = WithKinds(live!);
                    string? ck0 = Encounter.CritKey, bk0 = Encounter.BackKey;
                    var full = FullCardSample(withKinds);
                    foreach (var (tag, e) in new[] { ("", live!), ("-crit", withKinds), ("-full", full) })
                    {
                        if (tag.Length > 0) { Encounter.CritKey = Encounter.CritKeyOf(ParserPrefs.CritPick); Encounter.BackKey = Encounter.BackKeyOf(ParserPrefs.BackPick); }
                        h.ShowParser(e, now, e.ModeKind, details: false); h.SetKinahHidden(true);
                        if (!h.IsVisible) h.Show();
                        DoEvents();
                        var rows = ThemeRowsOf(h).FirstOrDefault();
                        var ranked = e.Ranked().Where(f => !f.Other).ToList();
                        var picks = tag.Length > 0 ? new[] { ("you", ranked.FirstOrDefault(f => f.You) ?? ranked[0]) }
                            : new[] { ("top", ranked[0]), ("you", ranked.FirstOrDefault(f => f.You) ?? ranked[0]), ("healer", ranked.FirstOrDefault(f => f.Healing > 0 && !f.You) ?? ranked[^1]) };
                        foreach (var (who, f) in picks)
                            if (rows?.CardForTest(f.Id) is { } card)
                            {
                                var file = $"ui-theme-{look}-card-{who}{tag}.png";
                                var host = new System.Windows.Controls.Border { Child = card, Padding = new Thickness(14) };
                                Save(host, Path.Combine(dir, file), 460, ShotGround);
                                SpillNote(dir, file, card);
                            }
                            else File.AppendAllText(Path.Combine(dir, "spill.txt"), $"ui-theme-{look}-card-{who}{tag}.png: no card (rows {(rows == null ? "missing" : "found")})" + Environment.NewLine);
                        Encounter.CritKey = ck0; Encounter.BackKey = bk0;
                    }
                    h.Close();
                }
            }
            catch (Exception ex) { Console.WriteLine($"{look}: {ex}"); }
        }
        Motion.FrozenAt = frozen0;
        PickStrip.Shown = picksShown0;

        // a moment's values with only what makes that moment: no rare drop or best on a plain pickup's frame, and so on
        static Dictionary<string, string> MomentOnly(string tag, Dictionary<string, string> v)
        {
            var c = new Dictionary<string, string>(v);
            if (tag != "rare-drop") c["rare"] = "";
            if (tag != "best-passed" && c.GetValueOrDefault("toBest", "").StartsWith("New")) { c["chase"] = "0.907"; c["toBest"] = "130K to your best here"; }
            if (tag is "rare-drop" or "best-passed" or "entry-opened" or "fight-started" or "fight-ended" or "fight-wiped") foreach (var k in c.Keys.Where(k => k.StartsWith("pick")).ToList()) c[k] = "";
            return c;
        }
    }

    /// <summary>Every TextBlock's words under an element by its logical tree (one not laid out yet has no visual tree).</summary>
    static IEnumerable<string> LogicalTexts(DependencyObject d)
    {
        if (d is System.Windows.Controls.TextBlock t) yield return MainWindow.InlineText(t);
        foreach (var ch in LogicalTreeHelper.GetChildren(d).OfType<DependencyObject>())
            foreach (var x in LogicalTexts(ch)) yield return x;
    }

    /// <summary>The fullest card's fight: yours with crit and back rates, specialties, a long name, "3 more" abilities and 2 deaths.</summary>
    static Encounter FullCardSample(Encounter withKinds)
    {
        var full = BossParser.Copy(withKinds);
        if (full.Roster.FirstOrDefault(f => f.You) is { } youF)
        {
            youF.Name = "Valkyriestrider"; youF.Deaths = 2;
            long low = youF.Abilities.Where(a => a.Damage > 0).Select(a => a.Damage).DefaultIfEmpty(1000).Min();
            foreach (var (a, i) in full.Roster.Where(f => !f.You && !f.Other).SelectMany(f => f.Abilities).Where(a => a.Damage > 0).Take(3).Select((a, i) => (a, i)))
                youF.Abilities.Add(new AbilityShare { Skill = a.Skill, Damage = low / (2 + i), BossDamage = low / (2 + i), Hits = 3 + i, BossHits = 3 + i });
        }
        return full;
    }

    /// <summary>A theme look's meter rows in a window (its own, Ui/Themes/ThemeRows.cs).</summary>
    static IEnumerable<ThemeRows> ThemeRowsOf(DependencyObject d)
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(d); i++)
        {
            var ch = VisualTreeHelper.GetChild(d, i);
            if (ch is ThemeRows tr) { yield return tr; continue; }
            foreach (var x in ThemeRowsOf(ch)) yield return x;
        }
    }

    /// <summary>
    /// What a frame draws past its own edges, in pixels per side (Steven, 2026-11-16: "some animations get cut off"): the
    /// window over the game is exactly its content's size, so an effect drawn outside it is cut there. Empty when nothing spills.
    /// Drawn through a brush of the whole visual (its descendants' bounds), so what Save's bitmap crops still shows here.
    /// </summary>
    internal static string Spill(FrameworkElement el)
    {
        double w = el.ActualWidth, h = el.ActualHeight;
        var b = VisualTreeHelper.GetDescendantBounds(el);
        if (b.IsEmpty || w < 1 || h < 1) return "";
        var all = Rect.Union(b, new Rect(0, 0, w, h));
        all.Intersect(new Rect(-600, -600, w + 1200, h + 1200));
        if (all.Left >= 0 && all.Top >= 0 && all.Right <= w + 0.5 && all.Bottom <= h + 0.5) return "";
        int pw = (int)Math.Ceiling(all.Width), ph = (int)Math.Ceiling(all.Height);
        var dv = new DrawingVisual();
        using (var dc = dv.RenderOpen())
            dc.DrawRectangle(new VisualBrush(el) { Stretch = Stretch.None, ViewboxUnits = BrushMappingMode.Absolute, Viewbox = all, AlignmentX = AlignmentX.Left, AlignmentY = AlignmentY.Top }, null, new Rect(0, 0, pw, ph));
        var rtb = new RenderTargetBitmap(pw, ph, 96, 96, PixelFormats.Pbgra32);
        rtb.Render(dv);
        var px = new byte[pw * ph * 4];
        rtb.CopyPixels(px, pw * 4, 0);
        double ox = -all.X, oy = -all.Y;
        int l = 0, t = 0, r = 0, bt = 0;
        for (int y = 0; y < ph; y++)
            for (int x = 0; x < pw; x++)
            {
                if (px[(y * pw + x) * 4 + 3] < 20) continue;   // faint glow edges don't count
                double cx = x + 0.5 - ox, cy = y + 0.5 - oy;
                if (cx < 0) l = Math.Max(l, (int)Math.Ceiling(-cx));
                if (cx > w) r = Math.Max(r, (int)Math.Ceiling(cx - w));
                if (cy < 0) t = Math.Max(t, (int)Math.Ceiling(-cy));
                if (cy > h) bt = Math.Max(bt, (int)Math.Ceiling(cy - h));
            }
        return l + t + r + bt == 0 ? "" : $"cut: left {l} top {t} right {r} bottom {bt} px";
    }

    static void SpillNote(string dir, string file, FrameworkElement el)
    {
        string s;
        try { s = Spill(el); } catch (Exception ex) { s = "spill check failed: " + ex.Message; }
        if (s.Length > 0) File.AppendAllText(Path.Combine(dir, "spill.txt"), file + ": " + s + Environment.NewLine);
        // and any figure drawn short of its words (Steven, 2026-11-17: "a lot of numbers in all the meters you can't see"; Ui/WpfApp.CutOff.cs)
        List<string> cut;
        try { cut = CutOffs(el); } catch (Exception ex) { cut = new() { "cut-off check failed: " + ex.Message }; }
        foreach (var c in cut) File.AppendAllText(Path.Combine(dir, "spill.txt"), file + ": text " + c + Environment.NewLine);
    }
}
