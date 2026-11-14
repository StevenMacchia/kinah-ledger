using System.IO;
using System.Windows;
using System.Windows.Media;
using KinahLedger.Parser;

namespace KinahLedger.Ui;

public static partial class WpfApp
{
    static readonly string[] ParserStates = { "live", "history", "detail", "nodamage", "nogroup", "empty" };

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
        var states = state is "true" or "all" or "" ? ParserStates.Append("overlay").ToArray() : new[] { state };
        if (states.Any(st => st != "overlay" && !ParserStates.Contains(st))) { Console.WriteLine($"--parser takes {string.Join(", ", ParserStates)} or overlay"); return 2; }
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
        var kill = past.Last(e => e.Outcome == "kill");
        var wipe = BossParser.Copy(past.Last(e => e.Outcome == "wipe")); wipe.Partial = true; wipe.PartialWhy.Add("a gap in the game's traffic");
        var shots = new List<(string File, Encounter E, DateTime At)>
        {
            ("ui-parser-overlay-live.png", live!, now),
            ("ui-parser-overlay-nodamage.png", liveNd!, now),
            ("ui-parser-overlay-nogroup.png", liveNg!, now),
            ("ui-parser-overlay-killed.png", kill, kill.End!.Value),
            ("ui-parser-overlay-wiped.png", wipe, wipe.End!.Value),
        };
        foreach (var (file, e, at) in shots)
        {
            var o = new ParserOverlay(still: true) { Left = -12000, Top = -12000 };
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
        w.ShowPartForTest("Farming"); w.ShowDemo(); DoEvents();
    }
}
