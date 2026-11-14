using System.IO;
using System.Windows;
using System.Windows.Media;
using KinahLedger.Parser;

namespace KinahLedger.Ui;

public static partial class WpfApp
{
    static readonly string[] ParserStates = { "live", "history", "detail", "nodamage", "nogroup", "empty", "run", "run-detail", "fight", "fight-history" };

    /// <summary>
    /// ui-shot --parser [live|history|detail|nodamage|empty|overlay] [--skin night|abyss|daylight]: the Parser page from
    /// its sample (Parser/ParserDemo.cs, made-up fights through the real rules) as the window shows it (ui-parser-STATE.png)
    /// and the whole page on its own (ui-parser-STATE-page.png); "overlay" draws the fight overlay over a game-like grey in
    /// four states (ui-parser-overlay-*.png). No state given: all of them. Nothing is tracked, saved or sent.
    /// </summary>
    public static int ShotParser(Settings settings, string dir, string state, string? skin)
    {
        Ensure();
        Directory.CreateDirectory(dir);
        // the default renders, then the PR's second set with every Parser option on ("-all")
        var states = state is "true" or "all" or "" ? ParserStates.Concat(new[] { "live-all", "detail-all", "run-all", "fight-history-all", "overlay" }).ToArray() : new[] { state };
        if (states.Any(st => st != "overlay" && !ParserStates.Contains(st.EndsWith("-all") ? st[..^4] : st))) { Console.WriteLine($"--parser takes {string.Join(", ", ParserStates)} or overlay"); return 2; }
        // one window for every state (a second MainWindow in one process trips the Farming page's own drawing)
        MainWindow? w = null;
        try
        {
            foreach (var st in states)
            {
                if (st == "overlay") { ShotParserOverlays(dir); continue; }
                if (w == null)
                {
                    w = new MainWindow(settings, watch: false) { WindowStartupLocation = WindowStartupLocation.Manual, Left = -12000, Top = -12000, ShowActivated = false };
                    w.Show();
                    if (skin != null) Skins.Apply(skin);
                    w.ShowDemo("Open world", null);
                    DoEvents();
                }
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

    static void ShotParserOverlays(string dir)
    {
        var now = new DateTime(2026, 11, 13, 21, 30, 0, DateTimeKind.Utc);
        var (past, live) = ParserDemo.Sample(now, live: true, damage: true);
        var (pastNd, liveNd) = ParserDemo.Sample(now, live: true, damage: false);
        var (_, liveNg) = ParserDemo.Sample(now, live: true, damage: true, group: false);
        var (pastRun, liveRun) = ParserDemo.Sample(now, live: true, mode: ParserMode.Run);
        var (pastFight, _) = ParserDemo.Sample(now, live: false, mode: ParserMode.Fights);
        var cleared = pastRun.Last(e => e.Outcome == "cleared");
        var pvp = pastFight.Last(e => e.PlayerKills > 0);
        var kill = past.Last(e => e.Outcome == "kill");
        var wipe = BossParser.Copy(past.Last(e => e.Outcome == "wipe")); wipe.Partial = true; wipe.PartialWhy.Add("a moment the tracker missed");
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
        };
        foreach (var (file, e, at) in shots)
        {
            var o = new ParserOverlay(still: true) { Left = -12000, Top = -12000, Mode = e.ModeKind };
            o.Draw(e, at);
            o.Show();
            DoEvents();
            // over a game-like mid grey, the way it sits over a bright scene
            Save((FrameworkElement)o.Content, Path.Combine(dir, file), ParserOverlay.Width0, Color.FromRgb(0x4A, 0x52, 0x5C));
            o.Close();
        }
    }

    /// <summary>options-test's Parser checks (Ui/MainWindow.Parser.cs): the tab, the live fight, the list, a fight opened, the page staying put, the overlay.</summary>
    static void ParserChecks(MainWindow w, Action<bool, string> check)
    {
        w.ShowPartForTest("Farming"); DoEvents();
        double farmHeight = w.ActualHeight;
        w.ShowParserDemo("live"); DoEvents();
        var live = w.ParserPageForTest();
        check(w.PartForTest == "Parser" && w.ParserPage.Visibility == Visibility.Visible && w.QuestsPage.Visibility != Visibility.Visible && w.LeftCol.Visibility != Visibility.Visible,
            "Parser: its own page, nothing of Farming or Quests beside it");
        check(live.Live && live.Roster == 4 && live.Rows == 9, $"Parser: the fight under way with its group of {live.Roster}, the 9 kept fights under it ({live.Rows} rows)");
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
        var o = new ParserOverlay(still: true) { Left = -12000, Top = -12000 };
        var (_, sample) = ParserDemo.Sample(new DateTime(2026, 11, 13, 21, 30, 0, DateTimeKind.Utc));
        o.Draw(sample!, new DateTime(2026, 11, 13, 21, 30, 0, DateTimeKind.Utc)); o.Show(); DoEvents();
        check(o.ActualWidth >= ParserOverlay.Width0 - 1 && !o.ShowActivated && o.Topmost && !o.ShowInTaskbar, "Parser: the overlay is drawn, on top, out of the taskbar, never activated");
        o.Close();
        // the modes: Full run's run under way with its splits, Every fight's fights, the list by mode, the overlay's menu
        w.ShowParserDemo("run"); DoEvents();
        var run = w.ParserLiveForTest;
        check(w.ParserModeForTest == ParserMode.Run && run is { ModeKind: ParserMode.Run } && run.Splits.Count >= 3 && run.Splits[^1].Outcome == "" && w.ParserPageForTest().Rows >= 2,
            $"Parser, Full run: the run under way with {run?.Splits.Count} splits (the last under way), the kept runs listed ({w.ParserPageForTest().Rows})");
        w.ShowParserDemo("fight-history"); DoEvents();
        check(w.ParserModeForTest == ParserMode.Fights && w.ParserPageForTest().Rows >= 4 && w.ParserPastForTest.Any(e => e.ModeKind == ParserMode.Fights && e.Kind == "Abyss" && e.PlayerKills > 0),
            $"Parser, Every fight: the fights kept, the Abyss's PvP among them ({w.ParserPageForTest().Rows} rows)");
        w.ShowParserDemo("live"); DoEvents();
        w.SetParserModeForTest(ParserMode.Run); DoEvents();
        check(w.ParserModeForTest == ParserMode.Run && w.ParserLiveForTest?.ModeKind == ParserMode.Run, "Parser: picking Full run switches the page to the run under way");
        var mo = new ParserOverlay(still: true) { Mode = ParserMode.Run };
        var menu = mo.BuildMenu();
        var items = menu.Items.OfType<System.Windows.Controls.MenuItem>().Where(i => i.IsCheckable).ToList();
        check(items.Count == 3 && items.Count(i => i.IsChecked) == 1 && (string)items.Single(i => i.IsChecked).Header == "Full run" && items.All(i => i.ToolTip is string t && t.Length > 20),
            "Parser: the overlay's menu has the three modes, the one on ticked, each saying what it counts");
        mo.Close();
        w.SetParserModeForTest(ParserMode.Bosses); DoEvents();
        // the classes (Steven, 2026-11-14): the game's emblem for each of the eight, one colour each, far enough apart; a meter bar's share
        var missing = ClassArt.Keys.Where(k => ClassArt.Image(k) == null).ToList();
        check(missing.Count == 0 && ClassArt.Key("Spirit Master") == "spiritmaster" && ClassArt.Image("") == null, $"Parser: the game's class emblem for each of the eight classes{(missing.Count > 0 ? ", missing " + string.Join(", ", missing) : "")}");
        double Dist(Color a, Color b) => Math.Sqrt(Math.Pow(a.R - b.R, 2) + Math.Pow(a.G - b.G, 2) + Math.Pow(a.B - b.B, 2));
        var pairs = ClassArt.Keys.SelectMany((a, i) => ClassArt.Keys.Skip(i + 1).Select(b => (a, b, d: Dist(ClassArt.ColorOf(a), ClassArt.ColorOf(b))))).OrderBy(x => x.d).ToList();
        check(pairs[0].d >= 60, $"Parser: one colour per class, the nearest two {pairs[0].a} and {pairs[0].b} {pairs[0].d:0} apart");
        w.ShowParserDemo("live"); DoEvents();
        var meters = new List<double>();
        void Walk(DependencyObject d) { if (d is System.Windows.Controls.Grid g && g.Tag is double share) meters.Add(share); foreach (var ch in LogicalTreeHelper.GetChildren(d).OfType<DependencyObject>()) Walk(ch); }
        Walk(w.ParserPage);
        check(meters.Count >= 4 && Math.Abs(meters.Max() - 1) < 1e-9 && meters.Zip(meters.Skip(1)).All(x => x.First >= x.Second - 1e-9),
            $"Parser: each group row on a meter bar, the top one full and the rest shorter in order: {string.Join(", ", meters.Select(x => x.ToString("0.00")))}");
        // minimal by default (Steven, 2026-11-14): no CP, item level, damage or kills near you until Options turns them on
        w.ShowParserDemo("live"); DoEvents();
        var plain = ParserTexts(w.ParserPage);
        w.ShowParserDemo("live-all"); DoEvents();
        var every = ParserTexts(w.ParserPage);
        // a class emblem's hover names its class: that stays (the emblem is the class, colour-blind or not)
        var plainTips = plain.Tips.Where(t => ClassArt.Key(t).Length == 0).ToList();
        check(!plain.Texts.Contains("CP") && !plain.Texts.Contains("Damage") && !plain.Texts.Contains("Kills near you") && plain.Texts.Contains("DPS") && plainTips.Count == 0
            && every.Texts.Contains("CP") && every.Texts.Contains("Item lvl") && every.Texts.Contains("Damage") && every.Texts.Contains("Kills near you") && every.Tips.Count > 3,
            $"Parser: minimal by default (DPS, no CP, damage, kills near you or hovers); every option on adds them ({every.Tips.Count} hovers){(plainTips.Count > 0 ? "; left on: " + plainTips[0] : "")}{(plain.Texts.Contains("CP") ? "; CP shown" : "")}{(plain.Texts.Contains("Damage") ? "; Damage shown" : "")}{(plain.Texts.Contains("Kills near you") ? "; kills near you shown" : "")}");
        // no word for how the game is read, anywhere on the page or its hovers (only Options names it)
        var banned = new[] { "packet", "traffic", "npcap", "decoder", "recording", "game's message", "game's own message", "from the game itself", "packet reading", "game's clock", "pc's clock" };
        foreach (var st in new[] { "live-all", "detail-all", "run-all", "fight-history-all", "nogroup-all", "nodamage-all", "empty-all" })
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
        check(sws.Details != null && new[] { sws.Details, sws.Kills, sws.Filters, sws.Hovers }.All(c => c.IsChecked == false), "Options › In game › Parser: Group details, Kills and deaths, List filters, Explanations on hover, all off by default");
        opt.Close();
        w.ShowPartForTest("Farming"); w.ShowDemo(); DoEvents();
    }

    /// <summary>Every text and hover on the Parser page, for the checks above.</summary>
    static (HashSet<string> Texts, List<string> Tips) ParserTexts(DependencyObject root)
    {
        var texts = new HashSet<string>(); var tips = new List<string>();
        void Walk(DependencyObject d)
        {
            if (d is System.Windows.Controls.TextBlock t && t.Text.Length > 0) texts.Add(t.Text);
            if (d is System.Windows.Controls.ContentControl cc && cc.Content is string cs) texts.Add(cs);
            if (d is FrameworkElement fe && fe.ToolTip is string tip) tips.Add(tip);
            foreach (var ch in LogicalTreeHelper.GetChildren(d).OfType<DependencyObject>()) Walk(ch);
        }
        Walk(root);
        return (texts, tips);
    }
}
