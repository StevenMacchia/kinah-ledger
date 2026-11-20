using System.IO;
using System.Windows;
using System.Windows.Media;
using KinahLedger.Parser;

namespace KinahLedger.Ui;

public static partial class WpfApp
{
    static readonly string[] ParserStates = { "live", "history", "detail", "nodamage", "nogroup", "empty", "inspect", "compare", "timeline", "fight", "taken", "full", "inspect-full" };

    /// <summary>
    /// ui-shot --parser [live|history|detail|nodamage|empty|overlay] [--skin night|abyss|daylight|wacky|tactical|glitch]: the Parser page from
    /// its sample (Parser/ParserDemo.cs, made-up fights through the real rules) as the window shows it (ui-parser-STATE.png)
    /// and the whole page on its own (ui-parser-STATE-page.png); "overlay" draws the fight overlay over a game-like grey in
    /// four states (ui-parser-overlay-*.png). No state given: all of them. Nothing is tracked, saved or sent.
    /// </summary>
    public static int ShotParser(Settings settings, string dir, string state, string? skin)
    {
        Ensure();
        Directory.CreateDirectory(dir);
        // the default renders, then the PR's second set with every Parser option on ("-all")
        var states = state is "true" or "all" or "" ? ParserStates.Concat(new[] { "live-all", "detail-all", "overlay", "views", "graphs" }).ToArray() : new[] { state };
        if (states.Any(st => st is not ("overlay" or "mine" or "pvp" or "themes" or "motion" or "meter" or "heals" or "finale" or "share" or "views" or "looks" or "worst" or "concepts" or "graphs" or "layout" or "picks") &&!ParserStates.Contains(st.EndsWith("-all") || st.EndsWith("-min") ? st[..^4] : st))) { Console.WriteLine($"--parser takes {string.Join(", ", ParserStates)} or overlay"); return 2; }
        // one window for every state (a second MainWindow in one process trips the Farming page's own drawing)
        MainWindow? w = null;
        try
        {
            foreach (var st in states)
            {
                if (w == null)
                {
                    w = new MainWindow(settings, watch: false) { WindowStartupLocation = WindowStartupLocation.Manual, Left = -12000, Top = -12000, ShowActivated = false };
                    w.Show();
                    if (skin != null) Skins.Apply(skin);
                    w.ShowDemo("Open world", null);
                    DoEvents();
                    // crit is off as shipped; KL_CRIT=result3 or mark08 renders it on (after the window, which sets it from its prefs)
                    Encounter.CritKey = Encounter.CritKeyOf(Environment.GetEnvironmentVariable("KL_CRIT"));
                    Encounter.BackKey = Encounter.BackKeyOf(Environment.GetEnvironmentVariable("KL_BACK"));
                    // the meter's pick on the page: KL_PARSER_ALWAYS=1 renders it on Always on (a demo saves nothing)
                    if (Environment.GetEnvironmentVariable("KL_PARSER_ALWAYS") == "1") w.SetParserAlwaysOnForTest(true);
                }
                if (st == "themes") { ShotThemes(w, dir); continue; }
                if (st == "pvp") { ShotPvp(dir); continue; }   // the PvP panel in every look (Ui/WpfApp.Pvp.cs)
                if (st == "picks") { ShotPickStrips(w, dir); continue; }   // the pickup strip on every look (Ui/WpfApp.PickStrip.cs)
                if (st == "share") { ShotMeterShare(dir); continue; }   // the meter's share picture (Ui/WpfApp.MeterShare.cs)
                if (st == "views") { ShotParserShare(w, dir); continue; }   // the Parser page's share pictures (Ui/WpfApp.ParserShare.cs)
                if (st == "graphs") { ShotGraphs(w, dir); continue; }   // the Graphs tab (Ui/WpfApp.ParserGraphs.cs)
                if (st == "layout") { ShotParserLayout(w, dir); continue; }   // the page at Steven's window sizes (Ui/WpfApp.ParserLayout.cs)
                if (st == "looks") { ShotSettingsLooks(dir); continue; }
                if (st == "worst") { ShotWorst(dir); continue; }
                if (st == "concepts") { ShotConcepts(dir); continue; }   // the reworked looks' concept samples (Ui/MeterConcepts.cs)   // the worst-case fight in every meter look (Ui/WpfApp.CutOff.cs)
                if (st == "mine") { ShotMine(w, dir); continue; }   // Ui/WpfApp.Mine.cs: a player's own hud.json and parser.json   // Ui/WpfApp.Themes.cs
                if (st == "overlay") { ShotParserOverlays(dir, w.HudValues.ToDictionary(kv => kv.Key, kv => kv.Value)); continue; }
                if (st == "motion") { ShotParserV2(w, dir, w.HudValues.ToDictionary(kv => kv.Key, kv => kv.Value)); ShotMeterMotion(dir, w.HudValues.ToDictionary(kv => kv.Key, kv => kv.Value)); continue; }
                if (st == "meter") { ShotMeterMotion(dir, w.HudValues.ToDictionary(kv => kv.Key, kv => kv.Value)); continue; }
                if (st == "heals") { ShotHeals(dir, w.HudValues.ToDictionary(kv => kv.Key, kv => kv.Value)); continue; }
                if (st == "finale") { ShotFinale(dir, w.HudValues.ToDictionary(kv => kv.Key, kv => kv.Value)); continue; }
                w.ShowParserDemo(st);
                DoEvents();
                var name = "ui-parser-" + st + (skin != null ? "-" + skin : "");
                Save((FrameworkElement)w.Content, Path.Combine(dir, name + ".png"), 800 - 16);
                SavePart(w.ParserPage, Path.Combine(dir, name + "-page.png"), w.TryFindResource("Bg") as Brush);
            }
        }
        catch (Exception ex) { Console.WriteLine(ex); return 1; }
        finally { w?.Close(); }
        Console.WriteLine($"wrote {Path.GetFullPath(dir)}");
        return 0;
    }

    /// <summary>The game-like grey under the overlays' renders; KL_SHOT_CLEAR=1 leaves them clear, to lay over real game pictures.</summary>
    static Color? ShotGround => Environment.GetEnvironmentVariable("KL_SHOT_CLEAR") == "1" ? null : Color.FromRgb(0x4A, 0x52, 0x5C);

    /// <summary>
    /// An open-world fight held by Always on (5 s without fighting): the pull's members and damage as a fight in Verteron that
    /// ended at its last hit, so the meter shows the whole fight's average with the "Avg. DPS" badge.
    /// </summary>
    internal static Encounter HeldSample(Encounter live, DateTime now)
    {
        var h = BossParser.Copy(live);
        h.Id = "held-sample"; h.Mode = "fight"; h.Kind = "Open world"; h.Instance = "Verteron"; h.Boss = ""; h.BossId = 0; h.GameMode = "";
        h.BossHp = h.BossMaxHp = 0; h.TargetsSplit = false; h.Always = true;
        foreach (var f in h.Roster) f.BossDamage = f.Damage;
        h.End = now.AddSeconds(-5); h.Outcome = "ended";
        return h;
    }

    /// <summary>A ready card with a sample's members in it, as they'd be from the party list (no damage yet).</summary>
    static Encounter ReadyWithParty(Encounter ready, Encounter from)
    {
        foreach (var m in from.Members) ready.Roster.Add(new Fighter { Id = m.Id, Name = m.Name, Class = m.Class, CombatPower = m.CombatPower, You = m.You });
        return ready;
    }

    /// <summary>A fight's copy with each skill's crit and back-attack rates and specialties (Settings' crit switch on): the cards' sample.</summary>
    internal static Encounter WithKindsForTest(Encounter live) => WithKinds(live);
    static Encounter WithKinds(Encounter live)
    {
        var withKinds = BossParser.Copy(live);
        foreach (var f in withKinds.Roster)
            foreach (var a in f.Abilities)
            {
                int seen = Math.Max(12, a.Hits); uint h = (uint)(a.Skill * 2654435761u ^ (uint)f.Id);
                double crit = 0.22 + (h % 37) / 100.0, back = (h / 37 % 41) / 100.0;
                a.StreamHits = seen; a.StreamDamage = a.Damage;
                a.Kinds = new() { ["result:3"] = (int)(seen * crit), ["side:1"] = (int)(seen * back) };
                a.KindDamage = new() { ["result:3"] = (long)(a.Damage * Math.Min(0.9, crit * 1.6)) }; a.KindMax = new() { ["result:3"] = a.Damage / Math.Max(1, seen) * 2 };
                // and the specialties its hits named (a class skill's id: the picks x 10)
                if (a.Skill is >= 11_000_000 and < 20_000_000) a.Picks = KinahLedger.Parser.Abilities.PicksOf((uint)a.Skill + (uint)((h % 4) switch { 0 => 340, 1 => 120, 2 => 450, _ => 0 }));
            }
        return withKinds;
    }

    static void ShotParserOverlays(string dir, Dictionary<string, string> kinah)
    {
        // the card a specialties chip opens (0.10.133): Judgment with 4 and 5 picked
        {
            var card = ParserCommand.SpecCard("Judgment", Specialties.Of(12240000), new[] { 4, 5 });
            var host = new System.Windows.Controls.Border { Padding = new Thickness(12), Child = card };
            Save(host, Path.Combine(dir, "ui-parser-spec-card.png"), 0, ShotGround);
        }
        var now = new DateTime(2026, 11, 13, 21, 30, 0, DateTimeKind.Utc);
        var (past, live) = ParserDemo.Sample(now, live: true, damage: true);
        var (pastNd, liveNd) = ParserDemo.Sample(now, live: true, damage: false);
        var (_, liveNg) = ParserDemo.Sample(now, live: true, damage: true, group: false);
        var (pastRun, liveRun) = ParserDemo.Sample(now, live: true, mode: ParserMode.Run);
        var (pastFight, _) = ParserDemo.Sample(now, live: false, mode: ParserMode.Fights);
        var cleared = pastRun.Last(e => e.Outcome == "cleared");
        // Every fight keeps only fights inside instances now (open-world and Abyss ones are never kept)
        var pvp = pastFight.LastOrDefault(e => e.PlayerKills > 0) ?? pastFight.Last();
        var kill = past.Last(e => e.Outcome == "kill");
        var wipe = BossParser.Copy(past.Last(e => e.Outcome == "wipe")); wipe.Partial = true; wipe.PartialWhy.Add("a moment the tracker missed");
        var held = HeldSample(live!, now);
        // a row's hover card (the top member's, and yours), each as it opens beside the meter
        foreach (var (file, who) in new[] { ("ui-parser-card-top.png", live!.Ranked().First(f => !f.Other)), ("ui-parser-card-you.png", live!.Roster.FirstOrDefault(f => f.You) ?? live!.Ranked().First(f => !f.Other)) })
            if (ParserRows.HoverCard(live!, who, now) is { } card) { card.Margin = new Thickness(12); Save(new System.Windows.Controls.Border { Child = card }, Path.Combine(dir, file), 320, ShotGround); }
        // the same card with Settings' crit and back-attack switch on: each skill's rates from hits kept one by one
        {
            var withKinds = WithKinds(live!);
            string? ck0 = Encounter.CritKey, bk0 = Encounter.BackKey;
            Encounter.CritKey = Encounter.CritKeyOf(ParserPrefs.CritPick); Encounter.BackKey = Encounter.BackKeyOf(ParserPrefs.BackPick);
            var youK = withKinds.Roster.FirstOrDefault(f => f.You) ?? withKinds.Ranked().First(f => !f.Other);
            if (ParserRows.HoverCard(withKinds, youK, now) is { } cc) { cc.Margin = new Thickness(12); Save(new System.Windows.Controls.Border { Child = cc }, Path.Combine(dir, "ui-parser-card-crit.png"), 380, ShotGround); }
            Encounter.CritKey = ck0; Encounter.BackKey = bk0;
        }
        var shots = new List<(string File, Encounter E, DateTime At)>
        {
            ("ui-parser-overlay-live.png", live!, now),
            ("ui-parser-overlay-nodamage.png", liveNd!, now),
            ("ui-parser-overlay-nogroup.png", liveNg!, now),
            ("ui-parser-overlay-run.png", liveRun!, now),
            ("ui-parser-overlay-run-cleared.png", cleared, cleared.End!.Value),
            ("ui-parser-overlay-fight.png", pvp, pvp.End!.Value),
            ("ui-parser-overlay-killed.png", kill, kill.End!.Value),
            ("ui-parser-overlay-wiped.png", wipe, wipe.End!.Value),
            ("ui-parser-overlay-ready.png", Encounter.Ready(KinahLedger.Packets.ContentKind.Expedition, "Ferocious Horn Den", "", now), now),
            ("ui-parser-overlay-ready-anywhere.png", Encounter.ReadyAnywhere("Verteron", now), now),
            ("ui-parser-overlay-held.png", held, held.End!.Value),
            // a party formed before the first hit: its rows, each with Combat Power (0.10.132)
            ("ui-parser-overlay-ready-party.png", ReadyWithParty(Encounter.Ready(KinahLedger.Packets.ContentKind.Expedition, "Ferocious Horn Den", "", now), live!), now),
        };
        foreach (var (file, e, at) in shots)
        {
            var o = new ParserOverlay(still: true) { Left = -12000, Top = -12000, Mode = e.ModeKind };
            o.Draw(e, at);
            o.Show();
            DoEvents();
            // over a game-like mid grey, the way it sits over a bright scene
            Save((FrameworkElement)o.Content, Path.Combine(dir, file), ParserOverlay.Width0, ShotGround);
            o.Close();
        }
        // the Parser's own window in each fight look (Settings › In game › Parser › Overlay look), a pull under way and the
        // open world's Always on card: the fight alone
        foreach (var lk in HudLooks.FightLooks)
            foreach (var (tag, e, at) in new[] { ("live", live!, now), ("held", held, held.End!.Value), ("killed", kill, kill.End!.Value), ("anywhere", Encounter.ReadyAnywhere("Verteron", now), now) })
            {
                var o = new ParserOverlay(still: true) { Left = -12000, Top = -12000, Mode = e.ModeKind, Look = lk };
                o.Draw(e, at);
                o.Show();
                DoEvents();
                Save((FrameworkElement)o.Content, Path.Combine(dir, $"ui-parser-overlay-look-{lk}-{tag}.png"), 0, ShotGround);
                o.Close();
            }
        // expanded by default (Steven, 2026-11-14): the Kinah overlay alone, then with the Parser's section above the Kinah
        // line (a pull under way, a run, the result held after a kill), the Strip and the Card looks. Farming in the open world
        // shows no DPS on the Kinah line (Steven, 2026-11-15: the Parser has its own meter now)
        var kinahShots = new List<(string File, string Look, Encounter? E, DateTime At)>
        {
            ("ui-parser-kinah-overlay.png", HudLooks.Strip, null, now),
            ("ui-parser-kinah-overlay-live.png", HudLooks.Strip, live, now),
            ("ui-parser-kinah-overlay-run.png", HudLooks.Strip, liveRun, now),
            ("ui-parser-kinah-overlay-killed.png", HudLooks.Strip, kill, kill.End!.Value),
            ("ui-parser-kinah-overlay-card-live.png", HudLooks.Card, live, now),
        };
        foreach (var (file, look, e, at) in kinahShots)
        {
            var h = new HudWindow(null, look, still: true) { Left = -12000, Top = -12000, ShowActivated = false };
            h.Update(kinah);
            h.ShowParser(e, at, e?.ModeKind ?? ParserMode.Bosses, details: false);
            h.SetKinahHidden(e != null);   // a boss fight is a dungeon's: the meter replaces the Kinah line
            h.Show();
            DoEvents();
            Save((FrameworkElement)h.Content, Path.Combine(dir, file), 0, ShotGround);
            h.Close();
        }
        // a timer due on the Kinah line, and all three stacked (the timer line, the Parser's section, the Kinah line), on the
        // strip and on the Card (2026-11-14, the overlay's design pass)
        {
            var due = new List<Session.TimerBoard.Item>
            {
                // the Shugo Festival's entry open (a green badge counting the minutes to enter down), then two timers due
                new("event:Shugo Festival", "Events", "Shugo Festival", "", Session.TimerBoard.Kind.On, now.AddSeconds(161), "2m 41s", "left to enter") { Short = "Shugo" },
                new("event:Dimensional Invasion", "Events", "Dimensional Invasion", "", Session.TimerBoard.Kind.Waiting, now.AddSeconds(279), "4m 39s", "") { Short = "Invasion" },
                new("boss:Soul Ruler Kashapa", "Verteron", "Soul Ruler Kashapa", "", Session.TimerBoard.Kind.Waiting, now.AddSeconds(719), "11m 59s", "") { Short = "Kashapa" },
            };
            var stackShots = new List<(string File, string Look, bool WithParser, bool WithTimers)>
            {
                ("ui-parser-kinah-timer.png", HudLooks.Strip, false, true), ("ui-parser-kinah-stack.png", HudLooks.Strip, true, true), ("ui-parser-kinah-stack-card.png", HudLooks.Card, true, true),
            };
            // the design pass's looks (Ui/HudStackLooks.cs): farming, a timer due, a boss fight with a timer due
            foreach (var look in new[] { HudLooks.Auto, HudLooks.Capsule, HudLooks.Meter, HudLooks.Float })
                stackShots.AddRange(new[] { ($"ui-hud-{look}-farming.png", look, false, false), ($"ui-hud-{look}-timer.png", look, false, true), ($"ui-hud-{look}-boss.png", look, true, true) });
            foreach (var (file, look, withParser, withTimers) in stackShots)
            {
                var h = new HudWindow(null, look, still: true) { Left = -12000, Top = -12000, ShowActivated = false };
                h.Update(kinah);
                if (withTimers) h.ShowTimers(due, now);
                if (withParser) { h.ShowParser(live, now, live!.ModeKind, details: false); h.SetKinahHidden(true); }
                h.Show();
                DoEvents();
                Save((FrameworkElement)h.Content, Path.Combine(dir, file), 0, ShotGround);
                h.Close();
            }
            // wider (Steven, 2026-11-14: "maybe an option to make it wider?"): Auto's boss fight at the widest
            {
                double w0 = HudStackLook.FightWidth; HudStackLook.FightWidth = HudStackLook.FightWidthMax;
                var h = new HudWindow(null, HudLooks.Auto, still: true) { Left = -12000, Top = -12000, ShowActivated = false };
                h.Update(kinah); h.ShowTimers(due, now);
                h.ShowParser(live, now, live!.ModeKind, details: false); h.SetKinahHidden(true);
                h.Show(); DoEvents();
                Save((FrameworkElement)h.Content, Path.Combine(dir, "ui-hud-auto-boss-wide.png"), 0, ShotGround);
                h.Close();
                HudStackLook.FightWidth = w0;
            }
            // in a dungeon the overlay stays the Parser (Steven, 2026-11-14): its ready card before the first pull, and the last
            // fight's result held until the next one starts
            var ready = Encounter.Ready(KinahLedger.Packets.ContentKind.Expedition, "Ferocious Horn Den", "", now);
            foreach (var look in new[] { HudLooks.Auto, HudLooks.Capsule, HudLooks.Meter, HudLooks.Float })
                foreach (var (tag, e, at) in new[] { ("ready", ready, now), ("held", kill, kill.End!.Value.AddMinutes(2)) })
                {
                    var h = new HudWindow(null, look, still: true) { Left = -12000, Top = -12000, ShowActivated = false };
                    h.Update(kinah);
                    h.ShowTimers(due, at);   // a timer due during the run stays in sight inside the meter
                    h.ShowParser(e, at, ParserMode.Bosses, details: false);
                    h.SetKinahHidden(true);
                    h.Show();
                    DoEvents();
                    Save((FrameworkElement)h.Content, Path.Combine(dir, $"ui-hud-{look}-{tag}.png"), 0, ShotGround);
                    h.Close();
                }
        }
        // side by side (Steven, 2026-11-14: the separate overlay looks exactly like the Kinah overlay): the Kinah overlay and the
        // separate Parser overlay on one game-like grey, each drawn by its own window and lifted into one picture
        {
            var h = new HudWindow(null, HudLooks.Strip, still: true) { Left = -12000, Top = -12000, ShowActivated = false };
            h.Update(kinah); h.Show(); DoEvents();
            var o = new ParserOverlay(still: true) { Left = -12000, Top = -12000, Mode = live!.ModeKind };
            o.Draw(live, now); o.Show(); DoEvents();
            var left = (UIElement)h.Content; h.Content = null;
            var right = (UIElement)o.Content; o.Content = null;
            var row = new System.Windows.Controls.StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal };
            var pic = new System.Windows.Controls.Border { Padding = new Thickness(16), Child = row };
            var l = new System.Windows.Controls.Border { Child = left, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 24, 0) };
            System.Windows.Documents.TextElement.SetFontFamily(l, HudKit.Text); System.Windows.Documents.TextElement.SetFontWeight(l, FontWeights.Medium);
            row.Children.Add(l);
            row.Children.Add(new System.Windows.Controls.Border { Child = right, VerticalAlignment = VerticalAlignment.Bottom });
            var host = new Window { WindowStyle = WindowStyle.None, AllowsTransparency = true, Background = Brushes.Transparent, ShowInTaskbar = false, ShowActivated = false,
                SizeToContent = SizeToContent.WidthAndHeight, Left = -12000, Top = -12000, Content = pic, FontFamily = HudKit.Text, FontWeight = FontWeights.Medium };
            TextOptions.SetTextFormattingMode(host, TextFormattingMode.Display);
            host.Show(); DoEvents();
            Save(pic, Path.Combine(dir, "ui-parser-side-by-side.png"), 0, ShotGround);
            host.Close(); h.Close(); o.Close();
        }
    }

    /// <summary>options-test's Parser checks (Ui/MainWindow.Parser.cs): the tab, the live fight, the list, a fight opened, the page staying put, the overlay.</summary>
    static void ParserChecks(MainWindow w, Action<bool, string> check)
    {
        // a live boss pull with damage seen but none of it the group's yet (or no group at all): the table draws, it doesn't throw
        // (crash.log "parser page", 2026-11-18: an empty Max left the Parser page blank)
        foreach (var roster in new[] { new List<Fighter>(), new List<Fighter> { new() { Id = 1, Name = "Brightmoor", Class = "Gladiator", You = true } } })
        {
            var pull = new Encounter { Mode = "boss", Start = DateTime.UtcNow.AddSeconds(-20), HasDamage = true, BossDamage = 50_000, Roster = roster };
            System.Windows.Controls.Border? table = null; string err = "";
            try { table = ParserCommand.Group(FightView.Of(pull, DateTime.UtcNow, null), new CommandCtx(), live: true, detail: false); }
            catch (Exception ex) { err = ex.GetType().Name; }
            check(table != null, $"parser: a live pull with damage but none of the group's yet ({roster.Count} in the group) draws its table{(err.Length > 0 ? $" (threw {err})" : "")}");
        }
        w.ShowPartForTest("Farming"); DoEvents();
        double farmHeight = w.ActualHeight;
        w.ShowParserDemo("live"); DoEvents();
        var live = w.ParserPageForTest();
        check(w.PartForTest == "Parser" && w.ParserPage.Visibility == Visibility.Visible && w.QuestsPage.Visibility != Visibility.Visible && w.LeftCol.Visibility != Visibility.Visible,
            "Parser: its own page, nothing of Farming or Quests beside it");
        check(live.Live && live.Roster == 4 && live.Rows >= 5, $"Parser: the fight under way with its group of {live.Roster}, the kept fights under it ({live.Rows} rows)");
        check(farmHeight < 500 || w.ActualHeight <= farmHeight + 1, $"Parser: the page keeps the Farming page's height and scrolls inside it: {w.ActualHeight:0} (Farming {farmHeight:0})");
        w.FollowForTest(Session.ActivityKind.Crafting, DateTime.UtcNow); w.FollowForTest(Session.ActivityKind.Crafting, DateTime.UtcNow.AddMinutes(5)); DoEvents();
        check(w.PartForTest == "Parser", $"Parser: the page stays while it shows, whatever the player does: {w.PartForTest}");
        var first = w.ParserPastForTest.OrderByDescending(e => e.Start).First();
        w.OpenFightForTest(first.Id); DoEvents();
        var det = w.ParserPageForTest();
        check(det.Detail && det.Rows == 0, "Parser: a click on a fight opens it in place of the list");
        w.OpenFightForTest(null); DoEvents();
        w.ShowParserDemo("empty"); DoEvents();
        var empty = w.ParserPageForTest();
        check(!empty.Live && empty.Rows == 0, "Parser: no fights yet says what lands there");
        // the page's head: actions only (Steven, 2026-11-18: "No settings should be from the app outside settings"); the meter's
        // pick (Only boss fights, Always on) went to Settings › DPS meter › When it shows
        w.ShowParserDemo("live"); DoEvents();
        static IEnumerable<System.Windows.Controls.Button> Buttons(DependencyObject d)
        {
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(d); i++)
            {
                var c = VisualTreeHelper.GetChild(d, i);
                if (c is System.Windows.Controls.Button b) yield return b;
                foreach (var x in Buttons(c)) yield return x;
            }
        }
        System.Windows.Controls.Button? Meter(string label) => Buttons(w.ParserPage).FirstOrDefault(b => System.Windows.Automation.AutomationProperties.GetName(b) == "Parser meter: " + label);
        {
            var head = FindTagged(w.ParserPage, "parser-head");
            var named = head == null ? new List<string>() : Buttons(head).Select(b => System.Windows.Automation.AutomationProperties.GetName(b)).ToList();
            bool switches = head != null && OptionsWindow.TreeOf(head).Any(d => d is System.Windows.Controls.CheckBox or System.Windows.Controls.ComboBox);
            check(head != null && Meter("Only boss fights") == null && Meter("Always on") == null && !switches
                && named.Contains("Copy a picture of this fight") && named.Contains("Copy this fight as text") && named.Contains("Pop out the DPS meter") && FindTagged(head, "parser-settings") != null,
                $"Parser: the page's head has actions only (Copy picture, Copy text, Pop out, a Settings link), no switch or meter pick ({string.Join(", ", named)})");
        }
        var o = new ParserOverlay(still: true) { Left = -12000, Top = -12000 };
        var (_, sample) = ParserDemo.Sample(new DateTime(2026, 11, 13, 21, 30, 0, DateTimeKind.Utc));
        o.Draw(sample!, new DateTime(2026, 11, 13, 21, 30, 0, DateTimeKind.Utc)); o.Show(); DoEvents();
        check(o.ActualWidth >= ParserOverlay.Width0 - 1 && !o.ShowActivated && o.Topmost && !o.ShowInTaskbar, "Parser: the overlay is drawn, on top, out of the taskbar, never activated");
        // its width (Steven, 2026-11-15): Buddy's by default, and dragged from its own left or right edge
        {
            double w0 = HudStackLook.FightWidth;
            HudStackLook.FightWidth = HudStackLook.FightWidthDefault; o.ApplyFightWidth(); DoEvents();
            double f1 = o.FaceWidthForTest, h = o.ActualHeight / 2;
            check(Math.Abs(f1 - 360) <= 2 && HudStackLook.FightWidthDefault == KinahLedger.Parser.ParserPrefs.DefaultWidth, $"Parser: the overlay is Buddy's width by default ({f1:0}, Buddy 360)");
            check(o.EdgeAt(new Point(2, h)) == -1 && o.EdgeAt(new Point(f1 - 2, h)) == 1 && o.EdgeAt(new Point(f1 / 2, h)) == 0,
                "...its left and right edges take a drag to widen it, the middle moves it");
            HudStackLook.FightWidth = 500; o.ApplyFightWidth(); DoEvents();
            check(Math.Abs(o.FaceWidthForTest - f1 - 150) <= 2, $"...and wider, it draws wider ({o.FaceWidthForTest:0} at 500)");
            // the overlays' size (Options › In game › Size) on it too: at 110% it is Buddy's 110% width on screen, its edges still found
            HudStackLook.FightWidth = HudStackLook.FightWidthDefault; o.ApplyFightWidth(); o.SetScale(1.1); DoEvents();
            double onScreen = o.ActualWidth;
            check(Math.Abs(onScreen - 360 * 1.1) <= 3 && o.EdgeAt(new Point(onScreen - 2, o.ActualHeight / 2)) == 1 && o.EdgeAt(new Point(2, o.ActualHeight / 2)) == -1,
                $"...at the overlays' 110% size it is as wide as Buddy at 110% ({onScreen:0}, Buddy {360 * 1.1:0}), its edges where they show");
            o.SetScale(1); DoEvents();
            HudStackLook.FightWidth = w0;
        }
        // a row's hover card: the member's figures and top abilities by damage (at most six), only from what the record has
        {
            var at = new DateTime(2026, 11, 13, 21, 30, 0, DateTimeKind.Utc);
            var top = sample!.Ranked().First(f => !f.Other && sample.Figure(f) > 0);
            var card = ParserRows.HoverCard(sample, top, at);
            var texts = card == null ? new List<string>() : FindTexts(card).Select(BlockText).ToList();
            int abilityRows = FindTexts(card ?? new System.Windows.Controls.Border()).Count(t => t.Text.EndsWith("%") && t.FontSize < 11) ;
            var biggest = FightView.Of(sample, at, null).Members.First(m => m.F.Id == top.Id).Abilities.OrderByDescending(a => a.Damage).FirstOrDefault()?.Name;
            check(card != null && texts.Contains(top.Name) && texts.Any(t => t.Contains("DPS")) && abilityRows is >= 1 and <= ParserRows.CardAbilities && biggest != null && texts.Contains(biggest) && !texts.Any(t => t == "Other"),
                $"Parser: a row's hover card names {top.Name}, their DPS and {abilityRows} abilities, the biggest first ({string.Join(" | ", texts.Take(8))})");
            check(ParserRows.HoverCard(sample, new KinahLedger.Parser.Fighter { Id = 999 }, at) == null, "...and none for a member with no damage");
            // alone on the meter the foot no longer repeats your row (Steven, 2026-11-17: "isnt the avg thing redundant?"): your
            // best on this fight, else your uptime (Steven, 2026-11-18: in place of the peak 5 s), else nothing; with a group, the group's DPS
            {
                var solo = BossParser.Copy(sample);
                var me = solo.Roster.FirstOrDefault(f => f.You) ?? solo.Roster.First();
                me.You = true;
                foreach (var f in solo.Roster.Where(f => f.Id != me.Id)) { f.Damage = 0; f.BossDamage = 0; f.Abilities.Clear(); }
                string FootOf(Encounter x) => string.Join(" ", FindTexts(ParserFoot.Line(x, at)).Select(BlockText));
                string group = FootOf(sample), alone = FootOf(solo);
                var up = ParserFoot.Uptime(solo, at);
                string want = up is { } u ? "Uptime " + u.ToString("P0", System.Globalization.CultureInfo.InvariantCulture).Replace(" ", "") : "";
                // uptime: the seconds with damage over the fight's seconds so far
                var u1 = new Encounter { Start = at.AddSeconds(-9) };
                u1.Roster.Add(new Fighter { Id = 1, Name = "Swoly", You = true, Damage = 6, PerSecond = new List<long> { 1, 0, 1, 1, 0, 1, 0, 1, 1, 0 } });
                double? upHalf = ParserFoot.Uptime(u1, at), upNone = ParserFoot.Uptime(new Encounter { Start = at }, at);
                check(upHalf is { } uh && Math.Abs(uh - 0.6) < 1e-9 && upNone == null, $"...uptime: 6 of 10 seconds with damage is 60% ({upHalf}), none before any damage ({upNone})");
                check(want.Length > 0 && alone.Contains(want) && !alone.Contains("Peak") && !alone.Contains("Your DPS") && !alone.Contains("Group") && group.Contains("Group DPS") && ParserFoot.SoloYou(solo) && !ParserFoot.SoloYou(sample),
                    $"Parser: alone on the meter the foot shows your uptime or best, not your row again; with a group the group's DPS (\"{alone}\" | \"{group}\")");
                // a best on this fight comes first
                var keep = ParserRows.BestOf;
                ParserRows.BestOf = x => new PullRef { E = x, Dps = 28_100 };
                string withBest = FootOf(solo);
                ParserRows.BestOf = keep;
                check(withBest.Contains("Best " + Amounts.Meter(28_100)) && !withBest.Contains("Uptime"), $"...with a best on this fight it shows that (\"{withBest}\")");
            }
            var bars = card == null ? new List<double>() : ParserRows.CardBarsForTest(card);
            check(bars.Count == abilityRows && bars.Count > 0 && Math.Abs(bars[0] - 156) < 0.5 && bars.Zip(bars.Skip(1)).All(p => p.First >= p.Second),
                $"...its ability bars against the top one, the top one full and the rest shorter in order ({string.Join(", ", bars.Select(b => b.ToString("0")))})");
            // crit and back attacks (Settings' switch): none on the card until a kind is picked; then each skill's rates as their
            // own columns, the member's in a gold line, only from the hits the record kept one by one
            {
                var copy = BossParser.Copy(sample);
                var who = copy.Roster.First(f => f.Id == top.Id);
                foreach (var a in who.Abilities)
                {
                    a.StreamHits = 20 * (1 + a.Hits / 20); a.StreamDamage = a.Damage;   // a multiple of 20: 2 in 5 and 1 in 4 exact
                    a.Kinds = new() { ["result:3"] = a.StreamHits.Value * 2 / 5, ["side:1"] = a.StreamHits.Value / 4 };
                    a.KindDamage = new() { ["result:3"] = a.Damage / 2 }; a.KindMax = new() { ["result:3"] = Math.Max(1, a.Damage / 10) };
                }
                string? ck0 = Encounter.CritKey, bk0 = Encounter.BackKey;
                var off = ParserRows.HoverCard(copy, who, at);
                Encounter.CritKey = Encounter.CritKeyOf(ParserPrefs.CritPick); Encounter.BackKey = Encounter.BackKeyOf(ParserPrefs.BackPick);
                var onCard = ParserRows.HoverCard(copy, who, at);
                Encounter.CritKey = ck0; Encounter.BackKey = bk0;
                var (critOff, backOff) = off == null ? (new List<string>(), new List<string>()) : ParserRows.CardRatesForTest(off);
                var (critOn, backOn) = onCard == null ? (new List<string>(), new List<string>()) : ParserRows.CardRatesForTest(onCard);
                var onTexts = onCard == null ? new List<string>() : FindTexts(onCard).Select(BlockText).ToList();
                int rowsOn = onCard == null ? 0 : ParserRows.CardBarsForTest(onCard).Count;
                check(critOff.Count == 0 && backOff.Count == 0 && !FindTexts(off ?? new System.Windows.Controls.Border()).Any(t => t.Text.Contains("crit")),
                    "Parser: a row's hover card shows no crit or back attack until Settings' switch picks them");
                check(rowsOn > 0 && critOn.Count == rowsOn && backOn.Count == rowsOn && critOn.All(x => x == "40%") && backOn.All(x => x == "25%")
                    && onTexts.Any(t => t.Contains("40% crit") && t.Contains("50% of the damage from crits") && t.Contains("25% from behind")) && onTexts.Contains("crit") && onTexts.Contains("back"),
                    $"...switched on: each skill's crit and back-attack rate in its own column, the member's in one line ({string.Join(", ", critOn)} | {string.Join(", ", backOn)} | {onTexts.FirstOrDefault(t => t.Contains("crit"))})");
                if (onCard != null)
                {
                    onCard.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                    var probe = new ParserPrefs(); OptionsWindow.CritSwitch(probe, true); var json = System.Text.Json.JsonSerializer.Serialize(probe); OptionsWindow.CritSwitch(probe, false); var json0 = System.Text.Json.JsonSerializer.Serialize(probe);
                    check(onCard.DesiredSize.Width <= 360 && probe.CritNow == "none" && probe.BackNow == "none" && json.Contains("\"result3\"") && json.Contains("\"side1\"") && !json0.Contains("CritSource"),
                        $"...the card stays narrower than the meter ({onCard.DesiredSize.Width:0}); the switch writes result3 and side1, off leaves the release's default");
                }
            }
        }
        o.Close();
        // the modes are gone (Steven, 2026-11-17: "get rid of full run and every fight"): Bosses only, nothing to pick
        ParserModesGoneChecks(w, check);   // Ui/WpfApp.ParserShare.cs
        // the classes (Steven, 2026-11-14): the game's emblem for each of the eight, one colour each, far enough apart; a meter bar's share
        var missing = ClassArt.Keys.Where(k => ClassArt.Image(k) == null).ToList();
        check(missing.Count == 0 && ClassArt.Key("Spirit Master") == "spiritmaster" && ClassArt.Image("") == null, $"Parser: the game's class emblem for each of the eight classes{(missing.Count > 0 ? ", missing " + string.Join(", ", missing) : "")}");
        double Dist(Color a, Color b) => Math.Sqrt(Math.Pow(a.R - b.R, 2) + Math.Pow(a.G - b.G, 2) + Math.Pow(a.B - b.B, 2));
        var pairs = ClassArt.Keys.SelectMany((a, i) => ClassArt.Keys.Skip(i + 1).Select(b => (a, b, d: Dist(ClassArt.ColorOf(a), ClassArt.ColorOf(b))))).OrderBy(x => x.d).ToList();
        // the game's own colours (its party list's): the nearest two (Cleric's gold, Chanter's orange) are the game's choice; the emblem tells them apart
        check(pairs[0].d >= 35, $"Parser: one colour per class, the game's own, the nearest two {pairs[0].a} and {pairs[0].b} {pairs[0].d:0} apart");
        w.ShowParserDemo("live"); DoEvents();
        // Parser v2: each member a row of the group's table, ranked by DPS, yours in the gold frame
        var members = new List<FrameworkElement>();
        void Walk(DependencyObject d) { if (d is FrameworkElement fe && Equals(fe.Tag, "parser-member")) members.Add(fe); foreach (var ch in LogicalTreeHelper.GetChildren(d).OfType<DependencyObject>()) Walk(ch); }
        Walk(w.ParserPage);
        var dpsOrder = members.Select(m => ParserTexts(m).Texts.Select(t => double.TryParse(t.Replace(",", ""), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var v) && v > 500 ? v : -1).DefaultIfEmpty(-1).Max()).ToList();
        check(members.Count == 4 && dpsOrder.Zip(dpsOrder.Skip(1)).All(x => x.First >= x.Second) && members.Count(m => m is System.Windows.Controls.Border { Effect: System.Windows.Media.Effects.DropShadowEffect }) == 1,
            $"Parser: the group's rows ranked by DPS ({string.Join(", ", dpsOrder)}), yours in the gold frame");
        // minimal with the switches off (Steven, 2026-11-14): no CP, item level, damage or kills near you until Options turns them on
        w.ShowParserDemo("live-min"); DoEvents();
        var plain = ParserTexts(w.ParserPage);
        w.ShowParserDemo("live-all"); DoEvents();
        var every = ParserTexts(w.ParserPage);
        // a class emblem's hover names its class: that stays (the emblem is the class, colour-blind or not)
        // and a row's figure says the damage behind it ("1.2M damage in 2:42"): a figure, not an explanation
        // as are the coverage line's uncredited share and a late joiner's or leaver's time (the honest pass): figures too
        // and the page head's actions (Copy picture, Copy text, Pop out, Settings) say what they do and their keys whatever the switch
        w.ShowParserDemo("live-min"); DoEvents();
        var headTips = FindTagged(w.ParserPage, "parser-head") is { } ph ? ParserTexts(ph).Tips.ToHashSet() : new HashSet<string>();
        var plainTips = plain.Tips.Where(t => ClassArt.Key(t).Length == 0 && !t.Contains(" damage in ") && !t.Contains("HP the boss lost") && !t.Contains("HP the bosses lost")
            && !t.StartsWith("Joined ") && !t.StartsWith("Left ") && !headTips.Contains(t)
            && t != L.T("A picture of this graph to paste in Discord or anywhere. Nothing is uploaded.")).ToList();   // a graph's Copy picture says what it does, like the head's
        // with Group details each member's class, Combat Power and item level sit under their name ("Sorcerer · 4,730 CP · 5,620 item level")
        // (the item level steps aside when the row carries its DPS over time: store v2's per-second damage)
        static bool Gear(HashSet<string> texts) => texts.Any(t => t.Contains(" CP"));
        static bool Near(HashSet<string> texts) => texts.Any(t => t.Contains("kills near you"));
        static bool DpsHead(HashSet<string> texts) => texts.Any(t => t == "DPS" || t.StartsWith("DPS ", StringComparison.Ordinal));
        check(!Gear(plain.Texts) && !plain.Texts.Contains("Damage") && !Near(plain.Texts) && DpsHead(plain.Texts) && plainTips.Count == 0
            && Gear(every.Texts) && every.Texts.Contains("Damage") && Near(every.Texts) && every.Tips.Count > 3,
            $"Parser: the switches off, DPS only (no CP, damage, kills near you or hovers); every option on adds them ({every.Tips.Count} hovers){(plainTips.Count > 0 ? "; left on: " + plainTips[0] : "")}{(Gear(plain.Texts) ? "; CP shown" : "")}{(plain.Texts.Contains("Damage") ? "; Damage shown" : "")}{(Near(plain.Texts) ? "; kills near you shown" : "")}");
        // each row: "48.2K DPS · 25.1%" (Steven, 2026-11-14: "should give DPS and % not just a %")
        check(DpsHead(plain.Texts) && plain.Texts.Count(t => System.Text.RegularExpressions.Regex.IsMatch(t, @"^\d+(\.\d)?%$")) >= 3 && plain.Tips.Count(t => t.Contains(" damage in ")) >= 3,
            $"Parser: each row says its DPS and its share of the damage, its damage on hover: {string.Join(", ", plain.Texts.Where(t => t.EndsWith('%')).Take(4))}");
        // a member's inspect view (Parser v2): their server, item level and Combat Power over every ability's columns
        w.ShowParserDemo("inspect-all"); DoEvents();
        var opened = ParserTexts(w.ParserPage);
        check(opened.Texts.Any(t => t.Contains("item level", StringComparison.OrdinalIgnoreCase) && t.Contains("Combat Power") && (t.Contains("Siel") || t.Contains("Israphel") || t.Contains("Fregion") || t.Contains("Zikel")))
            && opened.Texts.Contains("Average") && opened.Texts.Contains("Hits") && opened.Texts.Contains("Share"),
            "Parser: a member's inspect view says their server, item level and Combat Power over their abilities' damage, share, DPS, hits and average");
        // the honest pass: the inspect view's figures (to the boss, to adds, active, idle), the group card's coverage line and its
        // two switches; a late joiner marked; no "Other" and no "and more" anywhere
        w.ShowParserDemo("detail"); DoEvents();
        var kwapo = ParserTexts(w.ParserPage);
        check(new[] { "To the boss", "To adds" }.All(opened.Texts.Contains) && opened.Texts.Contains("Active") && opened.Texts.Contains("Idle")
            && kwapo.Texts.Contains("joined late") && kwapo.Texts.Contains("left")
            && kwapo.Texts.Any(t => t.Contains("of the boss's HP credited") && t.Contains("DPS and shares count credited damage")),
            $"Parser, honest pass: the inspect view's boss / adds / active and idle figures; a late joiner and a leaver marked, the coverage line ({kwapo.Texts.FirstOrDefault(t => t.Contains("credited"))})");
        check(!opened.Texts.Concat(every.Texts).Concat(kwapo.Texts).Any(t => t == "Other" || t.Contains("and more")), "Parser, honest pass: no Other row and no \"and more\" bucket");
        {
            // the group card says what its figures are (Settings › DPS meter › DPS over active time and Damage to adds set them,
            // 2026-11-18: no switches on the page): boss damage over the fight by default; with both set, the line and the figures follow
            w.ShowParserDemo("detail"); DoEvents();
            string Basis() => FindTagged(w.ParserPage, "parser-group-basis") is System.Windows.Controls.TextBlock t ? BlockText(t) : "";
            var openedE = w.ParserPastForTest.Single(e => e.Id == w.ParserOpenForTest);
            var top = openedE.Ranked().First();
            double bossDps = openedE.Dps(top, openedE.End!.Value);
            string basis0 = Basis();
            Encounter.BossOnly = false; Encounter.ActiveBasis = true;
            w.ParserTabForTest = "Group"; DoEvents();
            string basis1 = Basis();
            bool moved = openedE.Dps(top, openedE.End.Value) > bossDps;
            w.ShowParserDemo("detail"); DoEvents();
            check(FindButton(w.ParserPage, "All targets") == null && FindButton(w.ParserPage, "Active") == null
                && basis0.Contains(L.T("DPS over the whole fight")) && basis0.Contains(L.T("boss damage only"))
                && basis1.Contains(L.T("DPS over active time")) && basis1.Contains(L.T("adds included")) && moved && Encounter.BossOnly && !Encounter.ActiveBasis,
                $"Parser: the group card says what its figures are, no switches (\"{basis0}\", then \"{basis1}\" with active time and adds set; the figures follow)");
        }
        // the header: the instance with the game's mode for it
        w.ShowParserDemo("live"); DoEvents();
        check(ParserTexts(w.ParserPage).Texts.Any(t => t.StartsWith("Ferocious Horn Den · Conquest · ")), "Parser: the header says the instance and its mode (Ferocious Horn Den · Conquest)");
        // the boss's HP left beside its share, on the page (Steven, 2026-11-14)
        var hpTexts = ParserTexts(w.ParserPage);
        check(hpTexts.Texts.Contains("47%") && hpTexts.Texts.Any(t => System.Text.RegularExpressions.Regex.IsMatch(t, @"[\d.,]+[KM]? of [\d.,]+[KM]? HP left")),
            $"Parser: the boss's HP: its share on the ring (47%), the HP left of its whole on the figures line (1.82M of 3.85M HP left) [{string.Join(" | ", hpTexts.Texts.Where(t => t.Contains("HP")))}]");
        // two windows (Steven, 2026-11-15: "when im in the open world killing things with the parser on - it turns in to the
        // parser and not the kinah ledger ... have them just be 2 separate windows"): Always on, fighting in the open world,
        // the Kinah overlay stays the Kinah line and the Parser wants its own window; inside the Kinah overlay (the switch) as before
        {
            bool still0 = Motion.Still; Motion.Still = true;
            var t0 = DateTime.UtcNow;
            var ow = new BossParser();
            ow.Take(new Packets.GroupChanged(t0, new[] { new Packets.GroupMember(1, "Swoly", "Cleric", 4_500, 5_600, true) }));
            ow.Take(new Packets.DamageDealt(t0, 1, 5000, 12_000, "Swoly", true));
            foreach (var look in new[] { HudLooks.Auto, HudLooks.Capsule, HudLooks.Meter, HudLooks.Strip })
            {
                var hk = new HudWindow(null, look, still: true) { WindowStartupLocation = WindowStartupLocation.Manual, Left = -12000, Top = 900, ShowActivated = false };
                hk.Update(new Dictionary<string, string> { ["feedText"] = "Spiritstone", ["feedValue"] = "+7.5K", ["rate"] = "1.27M", ["total"] = "894K", ["time"] = "12:04" });
                hk.Show(); DoEvents();
                bool Kinah() => look == HudLooks.Strip ? hk.OwnContent?.IsVisible == true : ParserVisibleText(hk, "1.27M");
                var two = w.TwoWindowsForTest(hk, ow, inKinah: false); DoEvents();
                bool kinahTwo = Kinah(), fightInKinah = ParserVisibleText(hk, "Swoly");
                var one = w.TwoWindowsForTest(hk, ow, inKinah: true); DoEvents();
                check(!two.KinahHidden && !two.ParserInKinah && two.OwnWanted && kinahTwo && !fightInKinah && !one.OwnWanted,
                    $"two windows ({look}), Always on in the open world: the Kinah overlay stays the Kinah line ({(kinahTwo ? "shown" : "gone")}{(fightInKinah ? ", the fight drawn in it" : "")}), the Parser in its own window ({two.OwnWanted}); inside the Kinah overlay, no own window ({!one.OwnWanted})");
                hk.Close();
            }
            Motion.Still = still0;
        }
        // the overlay's look, one click from the page (Steven, 2026-11-14: "idk how to change the style"): with the Parser inside the Kinah overlay
        w.SetParserSeparateForTest(false); DoEvents();
        check(!ParserTexts(w.ParserPage).Texts.Any(t => t.StartsWith("Overlay look: ")) && FindTagged(w.ParserPage, "parser-settings") != null, "Parser: the page has no Overlay look link (the look is Settings › DPS meter's), a Settings link in its place");
        // separated (Steven, 2026-11-14: overlays move by a plain drag, no Move button or placement mode; the Kinah overlay's look exactly)
        {
            w.SetParserSeparateForTest(true); DoEvents();
            check(w.OverlaysForTest().Any(o => o.Name == "Parser"), "Parser: separated, it is in the header's Overlays menu (registered with the Kinah and Timers overlays)");
            check(FindButton(w.ParserPage, "Move overlay") == null, "Parser: separated, no Move button or placement mode: the overlay moves by a plain drag");
            // the same frame as the Kinah overlay's: surface, line, radius, padding, face
            var hud = new HudWindow(null, HudLooks.Strip, still: true) { Left = -12000, Top = -12000, ShowActivated = false };
            hud.Show(); DoEvents();
            var kinahFrame = (hud.OwnContent as System.Windows.Controls.Grid)?.Children.OfType<System.Windows.Controls.Border>().FirstOrDefault();
            var po = new ParserOverlay(still: true) { Left = -12000, Top = -12000 };
            var (_, pull1) = ParserDemo.Sample(new DateTime(2026, 11, 13, 21, 30, 0, DateTimeKind.Utc));
            po.Draw(pull1!, new DateTime(2026, 11, 13, 21, 30, 0, DateTimeKind.Utc)); po.Show(); DoEvents();
            var panel = FindPanel(po);
            string Look(System.Windows.Controls.Border? b) => b == null ? "none" : $"{(b.Background as SolidColorBrush)?.Color} {(b.BorderBrush as SolidColorBrush)?.Color} {b.BorderThickness} {b.CornerRadius} {b.Padding} {System.Windows.Documents.TextElement.GetFontFamily(b)}";
            // the overlays' glass (Ui/HudGlass.cs), as the default look's pill: the gradient fill, the lit edge, the corner, the face
            bool glass = panel != null && panel.Background is LinearGradientBrush && ReferenceEquals(panel.BorderBrush, HudGlass.Edge) && panel.CornerRadius.TopLeft == HudGlass.Radius
                && System.Windows.Documents.TextElement.GetFontFamily(panel).Source == HudKit.Text.Source;
            check(kinahFrame != null && glass, $"Parser: the separate overlay has the overlays' glass, corner and face (Parser {Look(panel)})");
            var before = (panel?.Background as LinearGradientBrush)?.GradientStops.LastOrDefault()?.Color.A;
            po.SetOpacity(0.5);
            var after = (panel?.Background as LinearGradientBrush)?.GradientStops.LastOrDefault()?.Color.A;
            check(before > after && after > 0, $"...and its fill follows the opacity setting ({before} to {after})");
            // click-through (off by default): a grab handle on the top edge shows with the pointer over it, and moves it
            po.SetClickThrough(true); po.ShowGrabForTest(); DoEvents();
            check(po.GrabShownForTest && ParserTexts((DependencyObject)po.Content).Texts.Contains("rest on a name for its skills  ·  drag the handle to move  ·  an edge to resize"), "Parser: with click-through on, a grab handle on its top edge moves the overlay (and the hint says an edge resizes it)");
            // the skills card with clicks going through (Steven, 2026-11-16: he couldn't find it): the pointer resting on a name opens
            // that member's card, a pass over the meter opens nothing, moving off closes it; the window never takes the pointer
            {
                ParserRows.PeekNoPopupForTest = true;
                try
                {
                    ParserRows? Find(DependencyObject d) { for (int i = 0; i < VisualTreeHelper.GetChildrenCount(d); i++) { var ch = VisualTreeHelper.GetChild(d, i); if (ch is ParserRows pr && pr.IsVisible) return pr; if (Find(ch) is { } x) return x; } return null; }
                    var rows = Find(po);
                    var mem = pull1!.Members.First(m => !m.Other && !m.You);
                    var mid = rows?.RowMiddleForTest(mem.Id);
                    Point? at = mid is { } m0 ? rows!.TranslatePoint(m0, po) : null;
                    po.PeekForTest(at); DoEvents();
                    bool passOpens = rows?.PeekingForTest != 0;
                    System.Threading.Thread.Sleep(ParserRows.PeekMs + 80);
                    po.PeekForTest(at); DoEvents();
                    long peekOpen = rows?.PeekingForTest ?? 0;
                    bool filled = rows?.PeekCardForTest is FrameworkElement;
                    po.PeekForTest(null); DoEvents();
                    long peekAfter = rows?.PeekingForTest ?? -1;
                    check(at != null && !passOpens && peekOpen == mem.Id && filled && peekAfter == 0,
                        $"Parser: with click-through on, resting the pointer on {mem.Name}'s row opens their skills card ({peekOpen}, want {mem.Id}), a pass opens nothing ({!passOpens}), moving off closes it ({peekAfter})");
                }
                finally { ParserRows.PeekNoPopupForTest = false; }
            }
            // the shortcut "Off" (the default): the hint and the menu name none; a real one is named
            var off = new ParserOverlay(still: true) { HotkeyText = "Off" }.HintForTest;
            var on = new ParserOverlay(still: true) { HotkeyText = "Ctrl+Shift+P" }.HintForTest;
            check(!off.Hint.Contains("Off") && !off.Hide.Contains("(") && off.Hint.StartsWith("drag to move") && on.Hint.Contains("Ctrl+Shift+P") && on.Hide.Contains("Ctrl+Shift+P"),
                $"Parser: with no shortcut (Off, the default) the overlay's hint and menu name none (\"{off.Hint}\" / \"{off.Hide}\"); with one, they name it");
            po.Close(); hud.Close();
        }
        static System.Windows.Controls.Border? FindPanel(DependencyObject root)
        {
            if (root is ParserPanel pp) return pp;
            foreach (var ch in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>()) if (FindPanel(ch) is { } f) return f;
            return null;
        }
        static System.Windows.Controls.Button? FindButton(DependencyObject root, string text)
        {
            if (root is System.Windows.Controls.Button b && b.Content as string == text) return b;
            foreach (var ch in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>()) if (FindButton(ch, text) is { } f) return f;
            return null;
        }
        // no word for how the game is read, anywhere on the page or its hovers (only Options names it)
        var banned = new[] { "packet", "traffic", "npcap", "decoder", "recording", "game's message", "game's own message", "from the game itself", "packet reading", "game's clock", "pc's clock" };
        foreach (var st in new[] { "live-all", "detail-all", "history-all", "nogroup-all", "nodamage-all", "empty-all" })
        {
            w.ShowParserDemo(st); DoEvents();
            var (texts, tips) = ParserTexts(w.ParserPage);
            var hit = texts.Concat(tips).Where(t => banned.Any(b => t.Contains(b, StringComparison.OrdinalIgnoreCase))).ToList();
            if (hit.Count > 0) { check(false, $"Parser {st}: no word for how the game is read: \"{hit[0]}\""); return; }
        }
        check(true, "Parser: no word for how the game is read on the page or its hovers, in any state");
        // Options › In game › Parser: four switches, off by default, before Rare drops
        var opt = new OptionsWindow(w, OptionsWindow.Page.InGame) { WindowStartupLocation = WindowStartupLocation.Manual, Left = -12000, Top = -12000, ShowActivated = false };
        opt.Show(); DoEvents();
        var sws = opt.ParserSwitchesForTest;
        check(new[] { sws.Details, sws.Deaths, sws.Filters, sws.Hovers }.All(c => c?.IsChecked == true) && new[] { sws.Separate, sws.Active }.All(c => c.IsChecked == false)
            && sws.Kills.IsChecked == ParserPrefs.Load().KillsNear,
            "Options › DPS meter: Group details, Deaths, List filters and Explanations on hover on by default; DPS over active time and Parser inside the Kinah overlay off; Kills near you as parser.json has it (on for a new install since 1.0.18)");
        // two windows by default (Steven, 2026-11-15: "have them just be 2 separate windows pls i miss the old one"): a new
        // player, and a file from before with the old "Separate": false, both get the Parser's own window
        check(ParserPrefs.FromText("{}").Separate && ParserPrefs.FromText("{\"Separate\":false,\"DetailsSettled\":true,\"HonestSettled\":true}").Separate
            && !ParserPrefs.FromText("{\"InKinah\":true}").Separate,
            "Options › In game › Parser: the Parser in its own window by default, a file from before too (its old Separate: false is no choice), Inside the Kinah overlay kept when picked");
        // its own window's look (Steven, 2026-11-15: "what happened to the settings for the parser views in the options?"):
        // Standard and the four fight looks, apart from the Kinah overlay's; a pick outlines it and fills the big preview
        {
            var (keys0, on0) = opt.ParserLooksForTest;
            string kinahLook = w.HudLook;
            bool rowShown = opt.ParserLookRowShownForTest == true;
            bool save0 = opt.SaveBtn.IsEnabled;
            string pick1 = keys0.Count > 1 ? keys0[1] : "capsule";   // another of the row shown
            opt.PickParserLookForTest(pick1); DoEvents();
            var (_, on1) = opt.ParserLooksForTest;
            string stage1 = opt.ParserLookStageForTest ?? "";
            bool save1 = opt.SaveBtn.IsEnabled;
            opt.PickParserLookForTest(on0); DoEvents();
            // a look picked here alone is a change to save (Steven, 2026-11-16: "i cant save it unless i also change the buddy")
            check(!save0 && save1 && !opt.SaveBtn.IsEnabled, $"Options › In game › Parser: a look picked for its own window alone lights Save, and picking the saved one again puts it out (before {save0}, picked {save1}, back {opt.SaveBtn.IsEnabled})");
            check(rowShown && keys0.SequenceEqual(OptionsWindow.ParserLooksOf(HudThemes.Modern)) && on0 == ParserPrefs.DefaultLook && on0 == HudThemes.ModernGlass && on1 == pick1 && stage1 == pick1 && w.HudLook == kinahLook
                && ParserPrefs.LookOf("badge") == ParserPrefs.StandardLook && ParserPrefs.FromText("{}").Look == ParserPrefs.DefaultLook
                && ParserPrefs.FromText("{\"Look\":\"standard\"}").Look == ParserPrefs.DefaultLook
                && ParserPrefs.FromText("{\"Look\":\"standard\",\"GlassSettled\":true}").Look == ParserPrefs.StandardLook
                && ParserPrefs.FromText("{\"Look\":\"podium\"}").Look == ParserPrefs.PodiumLook,
                $"Options › In game › Parser: its own window's look row ({string.Join(", ", keys0)}), Modern Glass by default ({on0}), the old standard panel moved to it once, another pick kept; a pick outlines it and draws it big ({on1}, {stage1}), the Kinah overlay's look left alone");
            // each look in the window itself: the fight drawn, no Kinah figure, the window as wide as the meter or so
            bool st0 = Motion.Still; Motion.Still = true;
            var nowL = new DateTime(2026, 11, 13, 21, 30, 0, DateTimeKind.Utc);
            var (_, pullL) = ParserDemo.Sample(nowL);
            var bad = new List<string>();
            foreach (var lk in OptionsWindow.ParserLooksOf(HudThemes.Classic))
            {
                var po = new ParserOverlay(still: true) { Left = -12000, Top = -12000, Look = lk };
                po.Draw(pullL!, nowL); po.Show(); DoEvents();
                bool boss = ParserVisibleText(po, "Ferocious Horn Nuakum"), kinah = ParserVisibleText(po, "/h") || ParserVisibleText(po, "an hour");
                if (!boss || kinah || po.ActualWidth < 200) bad.Add($"{lk} (boss {boss}, Kinah {kinah}, {po.ActualWidth:0} wide)");
                po.Close();
            }
            check(bad.Count == 0, $"the Parser's own window in each look draws the fight alone{(bad.Count > 0 ? ": " + string.Join("; ", bad) : "")}");
            // the meter's bottom edge stays put (Steven, 2026-11-15: "the bottom of the DPS meter is always the source of truth -
            // when the window needs to get bigger, it grows UP not down"): the ready card, a pull, the card again, in each look
            var moved = new List<string>();
            foreach (var lk in OptionsWindow.ParserLooksOf(HudThemes.Classic))
            {
                var po = new ParserOverlay(still: true) { Left = -12000, Top = -9000, Look = lk };
                po.Draw(Encounter.ReadyAnywhere("Verteron", nowL), nowL); po.Show(); DoEvents();
                po.AnchorBottomForTest();
                var e0 = po.EdgesForTest;
                po.Draw(pullL!, nowL); DoEvents();
                var e1 = po.EdgesForTest;
                po.Draw(Encounter.ReadyAnywhere("Verteron", nowL), nowL); DoEvents();
                var e2 = po.EdgesForTest;
                if (!(e1.FaceBottom == e0.FaceBottom && e2.FaceBottom == e0.FaceBottom && e1.Top < e0.Top - 40 && e2.Top == e0.Top))
                    moved.Add($"{lk}: bottom {e0.FaceBottom}/{e1.FaceBottom}/{e2.FaceBottom}, top {e0.Top}/{e1.Top}/{e2.Top}");
                po.Close();
            }
            Motion.Still = st0;
            // the meter moves between hits (Steven, 2026-11-15: "refreshing the numbers every like 0.01 seconds and moving it
            // depending on if youre attacking or not"): the same live record drawn half a second later reads a lower DPS
            {
                var po = new ParserOverlay(still: true) { Left = -12000, Top = -9000 };
                po.Draw(pullL!, nowL); po.Show(); DoEvents();
                var t1 = ParserTexts((DependencyObject)po.Content).Texts.ToList();
                po.Draw(pullL!, nowL.AddSeconds(0.5)); DoEvents();
                var t2 = ParserTexts((DependencyObject)po.Content).Texts.ToList();
                // the figures are short since 0.10.128 ("4.3K"): 30 seconds on for the fall to show in them
                var d1 = TaggedShown((DependencyObject)po.Content, "row-dps").OfType<System.Windows.Controls.TextBlock>().Select(x => x.Text).ToList();
                po.Draw(pullL!, nowL.AddSeconds(30)); DoEvents();
                var t3 = TaggedShown((DependencyObject)po.Content, "row-dps").OfType<System.Windows.Controls.TextBlock>().Select(x => x.Text).ToList();
                po.Close();
                static double Num(string x)
                {
                    x = x.Replace(",", "").Trim(); double m = x.EndsWith("K") ? 1e3 : x.EndsWith("M") ? 1e6 : 1;
                    return double.TryParse(m > 1 ? x[..^1] : x, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var v) ? v * m : -1;
                }
                double Top1(List<string> ts) => ts.Select(Num).Where(v => v >= 1000).DefaultIfEmpty(-1).Max();
                check(!t1.SequenceEqual(t2) && Top1(t3) < Top1(d1),
                    $"the live meter redraws within a second, its DPS falling while no hit lands ({Top1(d1):N0} to {Top1(t3):N0} 30 seconds on)");
            }
            // the foot, the held badge and the top plates (Steven, 2026-11-15: "have the bottom be total AND group dps avg ... a
            // green badge next to it that says 'Avg. DPS' ... a gold plate [TOP DPS]"), in every look: a pull under way, an
            // open-world fight held by Always on, a boss killed in an Expedition
            {
                bool st1 = Motion.Still; Motion.Still = true;
                static bool shouldTopOf(string tag) => tag == "killed";
                var (pastF, _) = ParserDemo.Sample(nowL);
                var killF = pastF.Last(x => x.Outcome == "kill");
                var heldF = HeldSample(pullL!, nowL);
                var wrong = new List<string>();
                foreach (var lk in OptionsWindow.ParserLooksOf(HudThemes.Classic))
                    foreach (var (tag, e, at) in new[] { ("live", pullL!, nowL), ("held", heldF, heldF.End!.Value), ("killed", killF, killF.End!.Value) })
                    {
                        var po = new ParserOverlay(still: true) { Left = -12000, Top = -9000, Look = lk, Mode = e.ModeKind };
                        po.Draw(e, at); po.Show(); DoEvents();
                        var root = (DependencyObject)po.Content;
                        var feet = TaggedShown(root, "parser-foot");
                        var footName = feet.Count == 1 ? System.Windows.Automation.AutomationProperties.GetName(feet[0]) : "";
                        int badges = TaggedShown(root, "avg-badge").Count;
                        var tops = TaggedShown(root, "top-dps").Select(PlateName).ToList();
                        var heals = TaggedShown(root, "top-heals").Select(PlateName).ToList();
                        string healWant = shouldTopOf(tag) ? e.TopHealer(at)?.Name ?? "" : "";
                        bool shouldHold = tag != "live", shouldTop = tag == "killed";
                        string want = e.FootText(at), topWant = e.TopDps(at)?.Name ?? "";
                        // the plate sits in the name's room: every row's DPS still ends at the same place (your gold frame's 1 px line apart)
                        var dpsRights = DpsRights(root);
                        bool aligned = dpsRights.Count >= 2 && dpsRights.Max() - dpsRights.Min() <= 1.5;
                        if (feet.Count != 1 || footName != want || !want.StartsWith("Total ", StringComparison.Ordinal) || badges != (shouldHold ? 1 : 0)
                            || (shouldTop ? tops.Count != 1 || tops[0] != topWant || topWant.Length == 0 : tops.Count != 0)
                            || (shouldTop ? heals.Count != 1 || heals[0] != healWant || healWant.Length == 0 : heals.Count != 0) || !aligned)
                            wrong.Add($"{lk} {tag}: foot {feet.Count} \"{footName}\" (want \"{want}\"), badge {badges}, top DPS [{string.Join(",", tops)}] (want {(shouldTop ? topWant : "none")}), top heals [{string.Join(",", heals)}] (want {(shouldTop ? healWant : "none")}), DPS column {(aligned ? "aligned" : string.Join("/", dpsRights.Select(x => x.ToString("0.#"))))}");
                        po.Close();
                    }
                Motion.Still = st1;
                check(wrong.Count == 0 && killF.TopsShown && !heldF.TopsShown && heldF.HeldAverage && !pullL!.HeldAverage,
                    $"the meter's foot says the total and the group's DPS in every look, \"Avg. DPS\" only on a held fight, TOP DPS only on a boss kill's top row and TOP HEALS on its top healer's, the DPS column unmoved{(wrong.Count > 0 ? ": " + string.Join("; ", wrong) : "")}");
            }
            check(moved.Count == 0, $"the Parser's own window grows up: the meter's bottom edge stays where it is as it grows into a pull and back, in each look{(moved.Count > 0 ? ": " + string.Join("; ", moved) : "")}");
            check(ParserPrefs.FromText("{\"Left\":10,\"Top\":20}").Bottom == null, "...a file from before has no bottom edge kept (its top places it once)");
        }
        // the Parser's look row (Parser inside the Kinah overlay on): the four looks drawn for fights, the one in use outlined, a click picks it for the panel too
        sws.Separate.IsChecked = true; sws.Separate.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent)); DoEvents();
        check(opt.ParserLookRowShownForTest == true, "...with Parser inside the Kinah overlay on, the look row shows");
        {
            var (keys, onKey) = opt.ParserLooksForTest;
            check(keys.SequenceEqual(HudLooks.FightLooks) && (onKey == null || HudLooks.FightLooks.Contains(onKey)), $"Options › In game › Parser: the overlay's look row offers {string.Join(", ", keys)}, the one in use outlined ({onKey ?? "none: an older look"})");
            var pick = HudLooks.FightLooks.First(k => k != onKey);
            opt.PickHudLookForTest(pick); DoEvents();
            check(opt.ParserLooksForTest.On == pick, $"...a look picked shows in the row at once ({pick})");
            check(opt.ParserLookStageForTest == pick, $"...and the big preview draws it ({opt.ParserLookStageForTest ?? "nothing"})");
            var hoverKey = HudLooks.FightLooks.First(k => k != pick);
            opt.HoverParserLookForTest(hoverKey); DoEvents();
            check(opt.ParserLookStageForTest == hoverKey, $"...a look under the mouse fills the big preview ({opt.ParserLookStageForTest ?? "nothing"}, wanted {hoverKey})");
            opt.HoverParserLookForTest(null); DoEvents();
            check(opt.ParserLookStageForTest == pick, $"...and the mouse leaving puts the picked one back ({opt.ParserLookStageForTest ?? "nothing"})");
            var ws = opt.ParserWidthForTest;
            double wBefore = HudStackLook.FightWidth;
            if (ws != null) { ws.Value = 500; DoEvents(); }
            check(ws != null && ws.Minimum == 300 && ws.Maximum == 640 && HudStackLook.FightWidth == 500, $"Options › In game › Parser: a Width slider, 300 to 640, that widens the meter as it moves ({HudStackLook.FightWidth:0})");
            if (ws != null) { ws.Value = wBefore; DoEvents(); }
            check(HudStackLook.FightWidth == wBefore, $"...and moved back puts the meter back ({HudStackLook.FightWidth:0}, was {wBefore:0})");
            // every look stays on offer inside the Kinah overlay too (Steven, 2026-11-20: "you should not have to choose the buddy
            // theme to choose the dps meter theme ... the styles should always all be available"): another theme's chip shows its
            // looks, and picking one gives the Parser its own window in it with the Kinah overlay's look untouched
            {
                string kinahLook0 = opt.HudLookEditForTest;
                string other = HudThemes.All.Select(t => t.Key).First(t => t != HudThemes.Of(kinahLook0) && t != HudThemes.Classic);
                opt.PickParserThemeForTest(other); DoEvents();
                var (otherKeys, otherOn) = opt.ParserLooksForTest;
                string otherPick = otherKeys.LastOrDefault() ?? "";
                opt.PickParserLookForTest(otherPick); DoEvents();
                check(otherKeys.SequenceEqual(OptionsWindow.ParserLooksOf(other)) && otherOn == null && sws.Separate.IsChecked == false && opt.ParserLooksForTest.On == otherPick && opt.HudLookEditForTest == kinahLook0,
                    $"Options › DPS meter: inside the Kinah overlay every theme's looks are on offer ({other}: {string.Join(", ", otherKeys)}); picking {otherPick} gives the Parser its own window in it (inside the overlay now {sws.Separate.IsChecked}, outlined {opt.ParserLooksForTest.On ?? "none"}) and the Kinah overlay keeps {kinahLook0} ({opt.HudLookEditForTest})");
                sws.Separate.IsChecked = true; sws.Separate.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent)); DoEvents();
                check(opt.ParserLooksForTest.Keys.SequenceEqual(HudThemes.FightLooks(HudThemes.Of(kinahLook0))), $"...and Parser inside the Kinah overlay on again offers the overlay's own fight looks first ({string.Join(", ", opt.ParserLooksForTest.Keys)})");
            }
            sws.Separate.IsChecked = false; sws.Separate.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent)); DoEvents();
        }
        // the Kinah overlay's small DPS went (Steven, 2026-11-15: "get rid of the dps meter on the kinah tracker overlay only since
        // we have the dps meters now as a separate thing"): no switch for it, and no look draws a DPS on the Kinah line, even
        // handed an old "farmDps" value; the Parser's own meter is the one place
        var parserTitles = opt.ParserSwitchTitlesForTest;
        check(!parserTitles.Any(t => t.Contains("DPS on the Kinah", StringComparison.OrdinalIgnoreCase) || t.Contains("Open-world DPS", StringComparison.OrdinalIgnoreCase)) && parserTitles.Contains("Parser inside the Kinah overlay"),
            "Options › In game › Parser: no Open-world DPS on the Kinah line switch; Parser inside the Kinah overlay stays (" + string.Join(", ", parserTitles) + ")");
        check(parserTitles.Contains("PvP kills in PvP") && opt.ParserPvpForTest.IsChecked == true, "Settings › DPS meter › More: PvP kills in PvP, on by default");
        check(!typeof(KinahLedger.Parser.ParserPrefs).GetProperties().Any(p => p.Name == "FarmDps"), "the Parser's settings keep no Kinah-line DPS choice");
        foreach (var look in HudLooks.All.Select(l => l.Key))
        {
            bool still0 = Motion.Still; Motion.Still = true;
            var hf = new HudWindow(null, look, still: true) { WindowStartupLocation = WindowStartupLocation.Manual, Left = -12000, Top = 900, ShowActivated = false };
            hf.Update(new Dictionary<string, string> { ["feedText"] = "Spiritstone", ["feedValue"] = "+7.5K", ["feedRate"] = "1.27M", ["feedUnit"] = "an hour", ["rate"] = "1.27M", ["total"] = "894K", ["time"] = "12:04", ["run"] = "1", ["farmDps"] = "1.2K" });
            hf.Show(); DoEvents();
            var texts = VisibleTexts(hf);
            check(texts.Any(x => x.Contains("1.27M")) && !texts.Any(x => x.Contains("1.2K") || x.Contains("DPS")),
                $"farming ({look}): the Kinah line shows the hour and no DPS figure ({string.Join(" | ", texts.Take(8))})");
            hf.Close(); Motion.Still = still0;
        }
        // in a dungeon the meter replaces the Kinah line (Steven, 2026-11-14), a timer due stays in sight, and leaving brings it back
        {
            bool still0 = Motion.Still; Motion.Still = true;
            var now0 = new DateTime(2026, 11, 13, 21, 30, 0, DateTimeKind.Utc);
            var (_, pull0) = ParserDemo.Sample(now0);
            var due0 = new List<Session.TimerBoard.Item> { new("boss:Soul Ruler Kashapa", "Verteron", "Soul Ruler Kashapa", "", Session.TimerBoard.Kind.Waiting, now0.AddSeconds(279), "4m 39s", "") { Short = "Kashapa" } };
            var kin = new Dictionary<string, string> { ["feedText"] = "Spiritstone", ["feedValue"] = "+7.5K", ["rate"] = "1.27M", ["total"] = "894K", ["time"] = "12:04" };
            foreach (var look in new[] { HudLooks.Auto, HudLooks.Capsule, HudLooks.Meter, HudLooks.Float, HudLooks.Strip })
            {
                var hd = new HudWindow(null, look, still: true) { WindowStartupLocation = WindowStartupLocation.Manual, Left = -12000, Top = 900, ShowActivated = false };
                hd.Update(kin); hd.ShowTimers(due0, now0); hd.ShowParser(pull0, now0, ParserMode.Bosses, false);
                hd.Show(); DoEvents();
                // the Strip draws its own line from these figures (not the rate); its whole Kinah line is the part to watch
                bool KinahShown() => look == HudLooks.Strip ? hd.OwnContent?.IsVisible == true : ParserVisibleText(hd, "1.27M");
                bool kinahBefore = KinahShown();
                hd.SetKinahHidden(true); DoEvents();
                bool gone = !KinahShown();
                bool meter = ParserVisibleText(hd, "Ferocious Horn Nuakum");
                bool timer = ParserVisibleText(hd, "Kashapa");
                hd.SetKinahHidden(false); DoEvents();
                bool back = KinahShown();
                check(kinahBefore && gone && meter && timer && back, $"in a dungeon ({look}): the meter replaces the Kinah line ({(gone ? "hidden" : "still shown")}), the timer due stays in sight ({timer}), the Kinah line back after ({back}){(kinahBefore ? "" : ", the Kinah line not shown before")}{(meter ? "" : ", the meter not shown")}");
                hd.Close();
            }
            Motion.Still = still0;
        }
        // wider (Steven, 2026-11-14): the meter's width in every look, 300 to 640; the rows stretch, the figures stay at the right
        {
            bool still0 = Motion.Still; Motion.Still = true;
            double w0 = HudStackLook.FightWidth;
            var now0 = new DateTime(2026, 11, 13, 21, 30, 0, DateTimeKind.Utc);
            var (_, pull0) = ParserDemo.Sample(now0);
            foreach (var look in HudLooks.FightLooks)
            {
                HudStackLook.FightWidth = HudStackLook.FightWidthDefault;
                var hd = new HudWindow(null, look, still: true) { WindowStartupLocation = WindowStartupLocation.Manual, Left = -12000, Top = 900, ShowActivated = false };
                hd.ShowParser(pull0, now0, ParserMode.Bosses, false); hd.SetKinahHidden(true);
                hd.Show(); DoEvents();
                (double W, double Gap) Measure()
                {
                    var f = hd.FightFrameForTest;
                    if (f == null || f.ActualWidth < 1) return (0, -1);
                    // the top member's share, the right-most figure of its row
                    var share = FindTexts(f).FirstOrDefault(t => BlockText(t) == "31.5%");
                    if (share == null) return (f.ActualWidth, -1);
                    var r = share.TransformToAncestor(f).Transform(new Point(share.ActualWidth, 0)).X;
                    return (f.ActualWidth, f.ActualWidth - r);
                }
                var (w1, gap1) = Measure();
                HudStackLook.FightWidth = HudStackLook.FightWidthMax; hd.ApplyFightWidth(); DoEvents();
                var (w2, gap2) = Measure();
                bool wider = w2 - w1 >= 250, right = gap1 >= 0 && gap2 >= 0 && Math.Abs(gap2 - gap1) <= 2;
                check(wider && right, $"wider ({look}): the meter {w1:0} wide at 100%, {w2:0} at the widest; the figures at its right edge ({gap1:0} and {gap2:0} from it)");
                hd.Close();
            }
            check(Math.Abs(HudStackLook.FightWidth - 640) < 0.5 && (HudStackLook.FightWidth = 9999) == 9999 && HudStackLook.FightWidth == HudStackLook.FightWidthMax && (HudStackLook.FightWidth = 10) == 10 && HudStackLook.FightWidth == HudStackLook.FightWidthMin,
                "wider: the width stays within 300 to 640");
            HudStackLook.FightWidth = w0;
            Motion.Still = still0;
        }
        // in a Transcendence, an Expedition or a Sanctuary the meter replaces the Kinah line (Steven, 0.10.95): no look shows
        // the hour's figure there, compact or expanded (Parser v2), whatever the fight
        {
            bool still = Motion.Still; Motion.Still = true;
            bool exp = ParserRows.ExpandedPref;
            var (_, pull) = ParserDemo.Sample(new DateTime(2026, 11, 13, 21, 30, 0, DateTimeKind.Utc));
            var shown = new List<string>();
            foreach (var look in new[] { HudLooks.Auto, HudLooks.Capsule, HudLooks.Meter, HudLooks.Float })
                foreach (var expanded in new[] { false, true })
                {
                    ParserRows.ExpandedPref = expanded;
                    var hd = new HudWindow(null, look, still: true) { WindowStartupLocation = WindowStartupLocation.Manual, Left = -12000, Top = 900, ShowActivated = false };
                    hd.Update(new Dictionary<string, string> { ["rate"] = "1.27M", ["total"] = "894K", ["time"] = "12:04" });
                    hd.ShowParser(pull, new DateTime(2026, 11, 13, 21, 30, 0, DateTimeKind.Utc), ParserMode.Bosses, false);
                    hd.SetKinahHidden(true);
                    hd.Show(); DoEvents();
                    if (VisibleTexts((DependencyObject)hd.Content).Any(t => t.Contains("1.27M") || t.Contains("894K"))) shown.Add($"{look}{(expanded ? " expanded" : "")}");
                    hd.Close();
                }
            ParserRows.ExpandedPref = exp; Motion.Still = still;
            check(shown.Count == 0, $"in a dungeon no look shows the Kinah line, compact or expanded{(shown.Count > 0 ? ": shown in " + string.Join(", ", shown) : "")}");
        }
        // expanded by default: the Parser's section opens above the Kinah line and the Kinah line stays where it is
        {
            bool still = Motion.Still; Motion.Still = true;
            var hud = new HudWindow(null) { WindowStartupLocation = WindowStartupLocation.Manual, Left = -12000, Top = 900, ShowActivated = false };
            hud.Show(); DoEvents();
            double bottom = hud.Top + hud.ActualHeight;
            var (_, pull) = ParserDemo.Sample(new DateTime(2026, 11, 13, 21, 30, 0, DateTimeKind.Utc));
            hud.ShowParser(pull, new DateTime(2026, 11, 13, 21, 30, 0, DateTimeKind.Utc), ParserMode.Bosses, false); DoEvents();
            double grown = hud.ActualHeight, bottom2 = hud.Top + hud.ActualHeight, parserH = hud.ParserHeight;
            hud.ShowParser(null, DateTime.UtcNow, ParserMode.Bosses, false); DoEvents();
            double bottom3 = hud.Top + hud.ActualHeight;
            check(parserH > 100 && Math.Abs(bottom2 - bottom) < 1.5 && Math.Abs(bottom3 - bottom) < 1.5 && hud.ParserHeight == 0,
                $"the Kinah overlay grows upward for a fight and folds back: its bottom stays put ({bottom:0} / {bottom2:0} / {bottom3:0}), the section {parserH:0} high");
            hud.Close(); Motion.Still = still;
        }
        opt.Discard();   // the look picked above is an unsaved edit: dropped, never a save question
        w.ShowPartForTest("Farming"); w.ShowDemo(); DoEvents();
    }

    /// <summary>True when a text containing <paramref name="part"/> is laid out and visible in the overlay (not collapsed or under a collapsed parent).</summary>
    /// <summary>The shown elements under <paramref name="root"/> with this tag (the meter's foot, its badges and plates).</summary>
    static List<FrameworkElement> TaggedShown(DependencyObject root, string tag)
    {
        var found = new List<FrameworkElement>();
        var seen = new HashSet<DependencyObject>();
        void Walk(DependencyObject d)
        {
            if (!seen.Add(d)) return;
            if (d is UIElement u && !u.IsVisible) return;
            if (d is FrameworkElement fe && fe.Tag as string == tag && fe.ActualWidth > 0) found.Add(fe);
            foreach (var ch in LogicalTreeHelper.GetChildren(d).OfType<DependencyObject>()) Walk(ch);
            if (d is Visual) for (int i = 0; i < VisualTreeHelper.GetChildrenCount(d); i++) Walk(VisualTreeHelper.GetChild(d, i));
        }
        Walk(root);
        return found;
    }

    /// <summary>The member's name on the row a plate sits on (the plate's line holds the name).</summary>
    static string PlateName(FrameworkElement plate)
    {
        // up to the name's line: a plate sits in the plates beside the name, the TOP HEALS tab in the heal chip after it
        for (DependencyObject? d = VisualTreeHelper.GetParent(plate); d != null; d = VisualTreeHelper.GetParent(d))
            if (d is System.Windows.Controls.StackPanel line && line.Children.OfType<System.Windows.Controls.TextBlock>().FirstOrDefault(t => t.Tag as string != "heal-figures") is { } name) return name.Text;
        return "";
    }

    /// <summary>Where each shown row's DPS figure ends, across the meter (the column a plate must never push).</summary>
    static List<double> DpsRights(DependencyObject root) => root is Visual v
        ? TaggedShown(root, "row-dps").Select(t => t.TranslatePoint(new Point(t.ActualWidth, 0), (UIElement)v).X).ToList()
        : new List<double>();

    static bool ParserVisibleText(Window w, string part)
    {
        bool found = false;
        var seen = new HashSet<DependencyObject>();
        void Walk(DependencyObject d)
        {
            if (found || !seen.Add(d)) return;
            if (d is UIElement u && !u.IsVisible) return;
            if (d is System.Windows.Controls.TextBlock t && BlockText(t).Contains(part) && t.ActualWidth > 0) { found = true; return; }
            foreach (var ch in LogicalTreeHelper.GetChildren(d).OfType<DependencyObject>()) Walk(ch);
            // some looks draw parts outside the logical tree (the glass ring, the strip's cells)
            if (d is Visual or System.Windows.Media.Media3D.Visual3D)
                for (int i = 0; i < VisualTreeHelper.GetChildrenCount(d); i++) Walk(VisualTreeHelper.GetChild(d, i));
        }
        Walk(w);
        return found;
    }

    /// <summary>The words of every text block the window shows now (shown and laid out), the visual tree's too, in tree order.</summary>
    internal static List<string> VisibleTexts(Window w)
    {
        var list = new List<string>();
        var seen = new HashSet<DependencyObject>();
        void Walk(DependencyObject d)
        {
            if (!seen.Add(d)) return;
            if (d is UIElement u && !u.IsVisible) return;
            if (d is System.Windows.Controls.TextBlock t && t.ActualWidth > 0 && BlockText(t) is { Length: > 0 } s && s.Trim().Length > 0) list.Add(s);
            foreach (var ch in LogicalTreeHelper.GetChildren(d).OfType<DependencyObject>()) Walk(ch);
            if (d is Visual or System.Windows.Media.Media3D.Visual3D)
                for (int i = 0; i < VisualTreeHelper.GetChildrenCount(d); i++) Walk(VisualTreeHelper.GetChild(d, i));
        }
        Walk(w);
        return list;
    }

    /// <summary>Every text block under <paramref name="root"/>, the visual tree's too.</summary>
    static List<System.Windows.Controls.TextBlock> FindTexts(DependencyObject root)
    {
        var all = new List<System.Windows.Controls.TextBlock>();
        var seen = new HashSet<DependencyObject>();
        void Walk(DependencyObject d)
        {
            if (!seen.Add(d)) return;
            if (d is System.Windows.Controls.TextBlock t) all.Add(t);
            foreach (var ch in LogicalTreeHelper.GetChildren(d).OfType<DependencyObject>()) Walk(ch);
            if (d is Visual) for (int i = 0; i < VisualTreeHelper.GetChildrenCount(d); i++) Walk(VisualTreeHelper.GetChild(d, i));
        }
        Walk(root);
        return all;
    }

    /// <summary>A text block's words, its runs too (Text alone misses runs added in code).</summary>
    static string BlockText(System.Windows.Controls.TextBlock t) => t.Inlines.Count > 0 ? new System.Windows.Documents.TextRange(t.ContentStart, t.ContentEnd).Text : t.Text;

    /// <summary>The texts drawn on screen under <paramref name="root"/>: only those visible (a collapsed part's are left out).</summary>
    static List<string> VisibleTexts(DependencyObject root)
    {
        var list = new List<string>();
        void Walk(DependencyObject d)
        {
            if (d is UIElement u && !u.IsVisible) return;
            if (d is System.Windows.Controls.TextBlock t && t.Text.Length > 0) list.Add(t.Text);
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(d); i++) Walk(VisualTreeHelper.GetChild(d, i));
        }
        Walk(root);
        return list;
    }

    /// <summary>Every text and hover on the Parser page, for the checks above.</summary>
    static (HashSet<string> Texts, List<string> Tips) ParserTexts(DependencyObject root)
    {
        var texts = new HashSet<string>(); var tips = new List<string>();
        void Walk(DependencyObject d)
        {
            if (d is System.Windows.Controls.TextBlock t && BlockText(t) is { Length: > 0 } tt) texts.Add(tt);
            if (d is System.Windows.Controls.ContentControl cc && cc.Content is string cs) texts.Add(cs);
            if (d is FrameworkElement fe && fe.ToolTip is string tip) tips.Add(tip);
            foreach (var ch in LogicalTreeHelper.GetChildren(d).OfType<DependencyObject>()) Walk(ch);
        }
        Walk(root);
        return (texts, tips);
    }
}
