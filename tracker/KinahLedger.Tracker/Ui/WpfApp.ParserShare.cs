using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using KinahLedger.Parser;

namespace KinahLedger.Ui;

public static partial class WpfApp
{
    /// <summary>
    /// ui-shot DIR --parser views: the Parser page's share pictures (Ui/ParserShare.cs) for a kept kill with the store-v2
    /// sample (crits, the timeline, damage taken): the summary card, the group, the timeline, the damage taken, the compare,
    /// a member's abilities and everything (ui-parser-share-VIEW.png); the group with other names hidden; the pull under way; and the group drawn while
    /// the window is in Daylight (the picture stays dark). Example figures, never the player's.
    /// </summary>
    static void ShotParserShare(MainWindow w, string dir)
    {
        var skin0 = Skins.Current;
        var got = new List<(BitmapSource Bmp, string File)>();
        MainWindow.ShareSinkForTest = (b, f) => got.Add((b, f));
        bool hide0 = w.ShareHideNamesForTest;
        void Write(string file)
        {
            if (got.Count == 0) { Console.WriteLine($"{file}: no picture"); return; }
            var enc = new PngBitmapEncoder();
            enc.Frames.Add(BitmapFrame.Create(got[^1].Bmp));
            using var fs = File.Create(Path.Combine(dir, file));
            enc.Save(fs);
            got.Clear();
        }
        try
        {
            w.ShareHideNamesForTest = false;
            w.ShowParserDemo("detail"); DoEvents();
            MainWindow.ParserDepthSampleForRender(true);
            var kept = w.ParserShownForTest!;
            foreach (var view in new[] { ParserShare.Hero, ParserShare.Group, ParserShare.Timeline, ParserShare.Taken, ParserShare.All })
            {
                if (w.ShareViewForTest(kept, view)) Write($"ui-parser-share-{view}.png");
            }
            w.ShareHideNamesForTest = true;
            if (w.ShareViewForTest(kept, ParserShare.Group)) Write("ui-parser-share-group-hidden.png");
            w.ShareHideNamesForTest = false;
            Skins.Apply("daylight");
            if (w.ShareViewForTest(kept, ParserShare.Group)) Write("ui-parser-share-group-daylight.png");
            Skins.Apply(skin0);
            // the pull under way (the demo's live boss with its HP on the ring)
            w.ShowParserDemo("full"); DoEvents();
            var live = w.ParserShownForTest!;
            if (w.ShareViewForTest(live, ParserShare.Hero)) Write("ui-parser-share-live-hero.png");
            if (w.ShareViewForTest(live, ParserShare.All)) Write("ui-parser-share-live-all.png");
            if (w.ShareViewForTest(live, ParserShare.Group)) Write("ui-parser-share-live-group.png");
            // the compare (this pull against your best kill and your last pull of the boss)
            w.ShowParserDemo("compare"); DoEvents();
            var cmp = w.ParserShownForTest!;
            if (w.ShareViewForTest(cmp, ParserShare.Compare)) Write("ui-parser-share-compare.png");
            // a member's card and abilities (the inspect view's Copy picture)
            w.ShowParserDemo("inspect-full"); DoEvents();
            var ins = w.ParserShownForTest!;
            if (w.ShareViewForTest(ins, ParserShare.Abilities, w.ParserInspectForTest)) Write("ui-parser-share-abilities.png");
            // the page itself with the split Share and the back button
            w.ShowParserDemo("detail"); DoEvents();
            SavePart(w.ParserPage, Path.Combine(dir, "ui-parser-share-page.png"), w.TryFindResource("Bg") as Brush);
        }
        catch (Exception ex) { Console.WriteLine($"parser share pictures: {ex}"); }
        finally
        {
            MainWindow.ShareSinkForTest = null; w.ShareHideNamesForTest = hide0;
            MainWindow.ParserDepthSampleForRender(false);
            if (Skins.Current != skin0) Skins.Apply(skin0);
        }
    }

    /// <summary>
    /// options-test: Full run and Every fight are gone (Steven, 2026-11-17: "get rid of full run and every fight"). No mode
    /// picker on the Parser page, in the overlay's menu or in Settings › DPS meter; parser.json's "run" and "fight" read as
    /// "boss"; the kept runs and fights an older version left are out of the list and still open without a fault.
    /// </summary>
    static void ParserModesGoneChecks(MainWindow w, Action<bool, string> check)
    {
        w.ShowParserDemo("history"); DoEvents();
        var (texts, tips) = ParserTexts(w.ParserPage);
        var named = new List<string>();
        void Names(DependencyObject d)
        {
            if (d is FrameworkElement fe && System.Windows.Automation.AutomationProperties.GetName(fe) is { Length: > 0 } n) named.Add(n);
            foreach (var ch in LogicalTreeHelper.GetChildren(d).OfType<DependencyObject>()) Names(ch);
        }
        Names(w.ParserPage);
        var past = w.ParserPastForTest;
        int bosses = past.Count(e => e.ModeKind == ParserMode.Bosses);
        bool oldKept = past.Any(e => e.ModeKind == ParserMode.Run) && past.Any(e => e.ModeKind == ParserMode.Fights);
        var words = texts.Concat(tips).Where(t => t.Contains("Full run") || t.Contains("Every fight") || t == "Runs" || t.StartsWith("Fights")).ToList();
        int rows = w.ParserPageForTest().Rows;
        check(w.ParserModeForTest == ParserMode.Bosses && !named.Any(n => n.StartsWith("Parser mode") || n == "What the Parser keeps" || n.StartsWith("Show the kept")) && words.Count == 0
            && texts.Any(t => t.StartsWith(L.T("Boss pulls"))) && oldKept && rows == Math.Min(bosses, 5),
            $"Parser: no mode picker on the page (Bosses only; no Full run or Every fight: {string.Join(", ", words.Take(3))}), the list is the boss pulls ({rows} rows of {bosses}), the old runs and fights left out ({oldKept})");
        // the overlay's menu: hide and clicks through, no mode
        var mo = new ParserOverlay(still: true);
        try
        {
            var items = mo.BuildMenu().Items.OfType<System.Windows.Controls.MenuItem>().ToList();
            check(items.Count == 2 && !items.Any(i => i.IsCheckable) && !items.Any(i => i.Header is string h && (h.Contains("Full run") || h.Contains("Every fight") || h.Contains("What the Parser keeps"))),
                $"Parser: the overlay's menu has no mode to pick ({string.Join(" | ", items.Select(i => i.Header))})");
        }
        finally { mo.Close(); }
        // Settings › DPS meter: no Full run or Every fight anywhere on the page or its hovers
        {
            var o = new OptionsWindow(w, OptionsWindow.Page.Meter) { WindowStartupLocation = WindowStartupLocation.Manual, Left = -12000, Top = -12000, ShowActivated = false };
            try
            {
                o.Show(); DoEvents();
                var (ot, otips) = ParserTexts(o);
                var hit = ot.Concat(otips).Where(t => t.Contains("Full run") || t.Contains("Every fight") || t.Contains("What the Parser keeps")).ToList();
                check(hit.Count == 0, $"Settings › DPS meter: no mode picker and no word of Full run or Every fight{(hit.Count > 0 ? ": \"" + hit[0] + "\"" : "")}");
            }
            finally { o.Close(); }
        }
        // parser.json: "run" and "fight" move to "boss", the list's own pick with them
        var r = ParserPrefs.FromText("{\"Mode\":\"run\",\"ListMode\":\"run\",\"BossFilter\":\"Ferocious Horn Den\",\"DetailsSettled\":true,\"HonestSettled\":true}");
        var f = ParserPrefs.FromText("{\"Mode\":\"fight\",\"ListMode\":\"fight\",\"DetailsSettled\":true,\"HonestSettled\":true}");
        var b = ParserPrefs.FromText("{\"Mode\":\"boss\",\"ListMode\":\"boss\",\"BossFilter\":\"Kromede\",\"DetailsSettled\":true,\"HonestSettled\":true}");
        check(r.Mode == "boss" && r.ListMode == "" && r.BossFilter == "" && f.Mode == "boss" && f.ListMode == "" && b.Mode == "boss" && b.ListMode == "boss" && b.BossFilter == "Kromede",
            $"parser.json: Mode \"run\" and \"fight\" read as \"boss\" (now {r.Mode}, {f.Mode}), their list pick cleared; a boss pick is kept ({b.BossFilter})");
        // the runs and fights an older version kept still open, cleanly, with the way back
        var bad = new List<string>();
        foreach (var old in past.Where(e => e.ModeKind != ParserMode.Bosses).GroupBy(e => e.ModeKind).Select(g => g.Last()))
        {
            try
            {
                w.OpenFightForTest(old.Id); DoEvents();
                if (!w.ParserPageForTest().Detail || FindTagged(w.ParserPage, "parser-hero") == null || FindTagged(w.ParserPage, "parser-back") == null) bad.Add($"{old.ModeKind}: not drawn");
            }
            catch (Exception ex) { bad.Add($"{old.ModeKind}: {ex.GetType().Name} {ex.Message}"); }
        }
        w.OpenFightForTest(null); DoEvents();
        check(bad.Count == 0, $"Parser: a run and a fight kept by an older version still open, with their hero and the back button{(bad.Count > 0 ? ": " + string.Join("; ", bad) : "")}");
    }

    static FrameworkElement? FindTagged(DependencyObject d, string tag)
    {
        if (d is FrameworkElement fe && fe.Tag as string == tag) return fe;
        foreach (var ch in LogicalTreeHelper.GetChildren(d).OfType<DependencyObject>()) if (FindTagged(ch, tag) is { } x) return x;
        if (d is Visual) for (int i = 0; i < VisualTreeHelper.GetChildrenCount(d); i++) if (FindTagged(VisualTreeHelper.GetChild(d, i), tag) is { } y) return y;
        return null;
    }

    static List<string> TagsUnder(DependencyObject d, string prefix)
    {
        var list = new List<string>();
        void Walk(DependencyObject x)
        {
            if (x is FrameworkElement fe && fe.Tag is string t && t.StartsWith(prefix, StringComparison.Ordinal)) list.Add(t);
            if (x is Visual) for (int i = 0; i < VisualTreeHelper.GetChildrenCount(x); i++) Walk(VisualTreeHelper.GetChild(x, i));
        }
        Walk(d);
        return list;
    }

    /// <summary>
    /// options-test: the Parser page's Share, Copy as text and back button (Ui/MainWindow.ParserShare.cs). Share on each tab
    /// copies a picture of that view (its file named for it, the frame round it: the boss, the day, kinahledger.com); Everything
    /// stacks the hero, the group, the timeline and the damage taken; the meter is in the ▾; a table copies as tab-separated rows,
    /// names hidden when the setting says so; a right-click on a table opens Copy picture and Copy as text; a pull open has the
    /// ← All boss pulls button, and it, Backspace, Alt+← and the mouse's back button go back. Never the clipboard or Pictures.
    /// </summary>
    static void ParserShareChecks(MainWindow w, Action<bool, string> check)
    {
        static void Click(System.Windows.Controls.Primitives.ButtonBase b) => b.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
        var got = new List<(int W, int H, string File, BitmapSource Bmp)>();
        string? text = null;
        MainWindow.ShareSinkForTest = (b, f) => got.Add((b.PixelWidth, b.PixelHeight, f, b));
        MainWindow.CopyTextSinkForTest = s => text = s;
        bool hide0 = w.ShareHideNamesForTest;
        int wantW = ParserShare.PixelWidth;
        try
        {
            w.ShareHideNamesForTest = false;
            // each section's own buttons, top right of its head (Steven, 2026-11-18: "A MORE PROMINENT COPY BUTTON NO ONE KNOWS
            // TO RIGHT CLICK"): Copy picture on every one, Copy text on a table, each copying its own section
            CopyButtonChecks(w, check, got, () => text, s => text = s);
            // the picture each tab's Copy picture copies: that view, in the share frame
            var bad = new List<string>();
            foreach (var (state, view) in new[] { ("detail", ParserShare.Group), ("taken", ParserShare.Taken), ("compare", ParserShare.Compare) })   // the timeline tab is Graphs now: each graph its own Copy picture (WpfApp.ParserGraphs.cs)
            {
                w.ShowParserDemo(state); DoEvents();
                var e = w.ParserShownForTest;
                var btn = FindTagged(w.ParserPage, "parser-copy-picture-" + view) as Button;
                if (e == null || btn is not { IsEnabled: true }) { bad.Add($"{state}: no Copy picture"); continue; }
                got.Clear();
                Click(btn); DoEvents();
                if (got.Count != 1) { bad.Add($"{state}: {got.Count} pictures"); continue; }
                var (pw, ph, file, bmp) = got[0];
                var laid = ParserShare.Laid(e, w.ShareNowForTest(e), w.ParserPastForTest, view, w.SharePartsForTest, MainWindow.ParserViewSample);
                var views = TagsUnder(laid, "share-view-");
                var all = string.Join(" | ", DrawnTexts(laid));
                var miss = new List<string>();
                if (!file.EndsWith($"-{view}.png", StringComparison.Ordinal)) miss.Add($"file {file}");
                if (pw != wantW || ph < 630 || ph % 2 != 0) miss.Add($"size {pw}×{ph}");
                if (views.Count != 1 || views[0] != "share-view-" + view) miss.Add($"views {string.Join(",", views)}");
                foreach (var want in new[] { e.Title, L.Day(e.Start.ToLocalTime()), "kinahledger.com" }) if (!all.Contains(want, StringComparison.OrdinalIgnoreCase)) miss.Add($"no \"{want}\"");
                if (MeterShare.Trimmed(laid) is { Count: > 0 } cut) miss.Add($"cut off: \"{cut[0]}\"");
                if (miss.Count > 0) bad.Add($"{state}: {string.Join(", ", miss)}");
            }
            check(bad.Count == 0, $"Parser share: Copy picture on the Group, Timeline, Taken and Compare sections copies a {wantW} px picture of that view in the share frame (the boss, the day, kinahledger.com), nothing cut off" + (bad.Count > 0 ? ": " + string.Join("; ", bad) : ""));

            // Everything: the hero, the group, the timeline and the damage taken, stacked in that order; the summary card alone; the meter
            {
                w.ShowParserDemo("full"); DoEvents();
                var e = w.ParserShownForTest!;
                var laidAll = ParserShare.Laid(e, w.ShareNowForTest(e), w.ParserPastForTest, ParserShare.All, w.SharePartsForTest, MainWindow.ParserViewSample);
                var order = TagsUnder(laidAll, "share-view-");
                var laidGroup = ParserShare.Laid(e, w.ShareNowForTest(e), w.ParserPastForTest, ParserShare.Group, w.SharePartsForTest, MainWindow.ParserViewSample);
                got.Clear();
                bool okAll = w.ShareViewForTest(e, ParserShare.All);
                bool okHero = w.ShareViewForTest(e, ParserShare.Hero);
                bool okMeter = w.ShareViewForTest(e, "meter");
                var files = got.Select(g => g.File).ToList();
                check(okAll && order.SequenceEqual(new[] { "share-view-hero", "share-view-group", "share-view-timeline", "share-view-taken" }) && laidAll.ActualHeight > laidGroup.ActualHeight * 1.8
                    && files.Count == 3 && files[0].EndsWith("-all.png") && got[0].W == wantW,
                    $"Parser share: Everything stacks the hero, the group, the timeline and the damage taken in one tall picture ({string.Join(", ", order)}; {laidAll.ActualHeight:0} tall against the group's {laidGroup.ActualHeight:0})");
                var heroLaid = ParserShare.Laid(e, w.ShareNowForTest(e), w.ParserPastForTest, ParserShare.Hero, w.SharePartsForTest, MainWindow.ParserViewSample);
                var heroTexts = DrawnTexts(heroLaid);
                check(okHero && files.Count > 1 && files[1].EndsWith("-hero.png") && TagsUnder(heroLaid, "share-view-").SequenceEqual(new[] { "share-view-hero" }) && heroTexts.Any(t => t == L.T("Group DPS"))
                    && !heroTexts.Any(t => t == L.T("Timeline") || t == L.T("Taken")),
                    "Parser share: the summary card alone (the group's DPS, yours and the clock; no tabs drawn on it)");
                check(okMeter && files.Count > 2 && files[2] == MeterShare.FileName(e) && got[2].W == MeterShare.PixelWidth, $"Parser share: Meter in the ▾ copies the meter's own picture ({(files.Count > 2 ? files[2] : "none")})");
                var menu = w.ShareMenuForTest(e).Select(x => x.Text).ToList();
                check(menu.SequenceEqual(new[] { L.T("This view"), L.T("Meter"), L.T("Everything"), L.T("Copy as text") }),
                    $"Parser share: the ▾ beside the hero's Copy picture offers This view, Meter, Everything and Copy as text ({string.Join(", ", menu)})");
            }

            // Copy as text: the group's table as tab-separated rows; names hidden with the setting
            {
                w.ShowParserDemo("detail"); DoEvents();
                var e = w.ParserShownForTest!;
                text = null;
                bool ok = w.CopyViewTextForTest(e, ParserShare.Group);
                var lines = (text ?? "").Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
                int members = e.Members.Count();
                int cols = lines.Length > 1 ? lines[1].Split('\t').Length : 0;
                var you = e.Roster.FirstOrDefault(f => f.You && !f.Other);
                var others = e.Members.Where(f => !f.You && f.Name.Length > 0).Select(f => f.Name).ToList();
                bool rowsOk = lines.Length == members + 2 && lines.Skip(1).All(l => l.Split('\t').Length == cols) && cols >= 6;
                bool named = others.All(n => text!.Contains(n)) && (you == null || text!.Contains(ParserRows.NameOf(you)));
                check(ok && rowsOk && lines[0].Contains(e.Title) && lines[1].Split('\t').Contains(L.T("DPS")) && lines[1].Split('\t').Contains(L.T("Member")) && named,
                    $"Parser share: Copy as text puts the group's table on the clipboard, a head row and a row a member split by tabs ({lines.Length - 2} rows of {members}, {cols} columns)");
                w.ShareHideNamesForTest = true;
                text = null;
                w.CopyViewTextForTest(e, ParserShare.Group);
                var hidden = text ?? "";
                w.ShareHideNamesForTest = false;
                check(others.Count > 0 && !others.Any(n => hidden.Contains(n)) && Enumerable.Range(1, 6).Count(i => hidden.Contains(L.F("Member {0}", i))) == others.Count && (you == null || hidden.Contains(ParserRows.NameOf(you))),
                    "Parser share: with Hide other names on, the text names each of the others by their place (\"Member 1\", \"Member 3\"), yours kept");
                // the damage taken copies too
                w.ShowParserDemo("taken"); DoEvents();
                var et = w.ParserShownForTest!;
                text = null;
                w.CopyViewTextForTest(et, ParserShare.Taken);
                var tl = (text ?? "").Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
                check(tl.Length >= 3 && tl[1].Split('\t').Contains(L.T("Taken")) && tl.Skip(2).All(l => l.Split('\t').Length == tl[1].Split('\t').Length),
                    $"Parser share: the damage taken copies as text too ({tl.Length - 2} rows)");
                // a right-click on the table: Copy picture and Copy as text, in the window's own menu (no Windows ContextMenu)
                w.ParserTabForTest = "Group"; DoEvents();
                var row = FindTagged(w.ParserPage, "parser-member");
                if (row is UIElement ru)
                {
                    ru.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Right) { RoutedEvent = Mouse.MouseUpEvent });
                    DoEvents();
                }
                var card = w.MenuCardForTest;
                var lines2 = card == null ? new List<string>() : FindTexts(card).Select(BlockText).Where(s => s.Length > 0).ToList();
                w.CloseMenuForTest();
                check(card != null && lines2.Contains(L.T("Copy picture")) && lines2.Contains(L.T("Copy as text")) && w.RightClickMenusForTest().Count == 0,
                    $"Parser share: a right-click on the group's table opens Copy picture and Copy as text ({string.Join(", ", lines2)})");
            }

            // the back button, top left above the hero, and Backspace, Alt+← and the mouse's back button
            {
                w.ShowParserDemo("detail"); DoEvents();
                var id = w.ParserOpenForTest;
                var back = FindTagged(w.ParserPage, "parser-back") as Button;
                var hero = FindTagged(w.ParserPage, "parser-hero");
                var words = back == null ? new List<string>() : FindTexts(back).Select(BlockText).ToList();
                bool above = false;
                if (back != null && hero != null)
                {
                    w.UpdateLayout();
                    var pb = back.TransformToAncestor(w.ParserPage).Transform(new Point(0, 0));
                    var ph = hero.TransformToAncestor(w.ParserPage).Transform(new Point(0, 0));
                    above = pb.Y + back.ActualHeight <= ph.Y + 1 && pb.X <= ph.X + 4;
                }
                bool clicked = false;
                if (back != null) { Click(back); DoEvents(); clicked = w.ParserOpenForTest == null; }
                check(back != null && id != null && words.Contains(L.T("All boss pulls")) && back.ActualHeight >= 30 && above && clicked,
                    $"Parser: a pull open has a real ← All boss pulls button top left over the hero ({back?.ActualHeight:0} tall, above {above}), and it goes back ({clicked})");
                w.OpenFightForTest(id); DoEvents();
                bool key = w.ParserBackKey(Key.Back, Key.None, ModifierKeys.None, inText: false) && w.ParserOpenForTest == null;
                w.OpenFightForTest(id); DoEvents();
                bool typing = !w.ParserBackKey(Key.Back, Key.None, ModifierKeys.None, inText: true) && w.ParserOpenForTest == id;
                bool alt = w.ParserBackKey(Key.System, Key.Left, ModifierKeys.Alt, inText: false) && w.ParserOpenForTest == null;
                w.OpenFightForTest(id); DoEvents();
                bool mouse = w.ParserBackMouse(MouseButton.XButton1) && w.ParserOpenForTest == null;
                bool none = !w.ParserGoBackForTest();
                // the window's own key: Backspace through its PreviewKeyDown
                w.OpenFightForTest(id); DoEvents();
                bool wired = false;
                if (PresentationSource.FromVisual(w) is { } src)
                {
                    var k = new KeyEventArgs(Keyboard.PrimaryDevice, src, 0, Key.Back) { RoutedEvent = Keyboard.PreviewKeyDownEvent };
                    w.RaiseEvent(k); DoEvents();
                    wired = k.Handled && w.ParserOpenForTest == null;
                }
                // a member open: back goes to the group first, then the list
                w.OpenFightForTest(id); DoEvents();
                var opened = w.ParserShownForTest?.Ranked().FirstOrDefault(f => f.Abilities.Count > 0)?.Id;
                w.ParserInspectForTest = opened; DoEvents();
                bool step = opened != null && w.ParserGoBackForTest() && w.ParserInspectForTest == null && w.ParserOpenForTest == id && w.ParserGoBackForTest() && w.ParserOpenForTest == null;
                check(key && typing && alt && mouse && none && wired && step,
                    $"Parser: Backspace (not while typing), Alt+← and the mouse's back button go back (Backspace {key}, typing {typing}, Alt+← {alt}, mouse {mouse}, through the window {wired}); a member open steps back to the group first ({step}); on the list nothing ({none})");
            }
        }
        finally
        {
            MainWindow.ShareSinkForTest = null; MainWindow.CopyTextSinkForTest = null; w.ShareHideNamesForTest = hide0;
            w.CloseMenuForTest();
        }
    }

    /// <summary>
    /// options-test: every section of the Parser page has its own copy buttons top right of its head (Steven, 2026-11-18): the
    /// hero (Copy picture, the summary card, and a ▾ beside it), the group, the timeline, the damage taken, the compare and a
    /// member's abilities. Each says its words whole, Copy picture copies that section's picture and Copy text (on a table) its
    /// rows; the button says "Copied" in place, through a redraw too, and its words come back after.
    /// </summary>
    static void CopyButtonChecks(MainWindow w, Action<bool, string> check, List<(int W, int H, string File, BitmapSource Bmp)> got, Func<string?> text, Action<string?> setText)
    {
        static void Click(System.Windows.Controls.Primitives.ButtonBase b) => b.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
        static List<string> Words(DependencyObject d) => FindTexts(d).Select(BlockText).Where(s => s.Length > 0).ToList();
        // the section a button sits in: the fight's (or the member's) block's own child holding it
        FrameworkElement? Section(DependencyObject el)
        {
            for (DependencyObject cur = el; ;)
            {
                var p = VisualTreeHelper.GetParent(cur);
                if (p == null) return null;
                // a section: a part of the page, or of the Graphs view (the graphs, the group's table under them)
                if ((p is Panel && VisualTreeHelper.GetParent(p) == w.ParserPage) || p is FrameworkElement { Tag: "parser-graphs-view" }) return cur as FrameworkElement;
                cur = p;
            }
        }
        var bad = new List<string>();
        // the state, the section's view, the head word its text's first row holds (null: no Copy text)
        var cases = new (string State, string View, string? Head)[]
        {
            ("detail", ParserShare.Hero, null), ("detail", ParserShare.Group, L.T("DPS")),
            ("taken", ParserShare.Taken, L.T("Taken")), ("compare", ParserShare.Compare, L.T("This pull")), ("inspect-full", ParserShare.Abilities, L.T("Ability")),
            ("live", ParserShare.Hero, null), ("live", ParserShare.Group, L.T("DPS")),
        };
        var seen = new List<string>();
        foreach (var (state, view, head) in cases)
        {
            w.ShowParserDemo(state); DoEvents(); w.UpdateLayout();
            string at = $"{state}/{view}";
            var e = w.ParserShownForTest;
            var box = FindTagged(w.ParserPage, "parser-copy-" + view);
            var pic = FindTagged(w.ParserPage, "parser-copy-picture-" + view) as Button;
            var txt = FindTagged(w.ParserPage, "parser-copy-text-" + view) as Button;
            if (e == null || box == null || pic == null) { bad.Add($"{at}: no Copy picture"); continue; }
            if ((txt != null) != (head != null)) { bad.Add($"{at}: Copy text {(txt != null ? "shown" : "missing")}"); continue; }
            if (view == ParserShare.Hero && FindTagged(box, "parser-share-more") is not Button) bad.Add($"{at}: no ▾ beside Copy picture");
            // the words whole, and the buttons top right of their section
            var words = Words(box);
            if (!words.Contains(L.T("Copy picture")) || (head != null && !words.Contains(L.T("Copy text")))) bad.Add($"{at}: words {string.Join("|", words)}");
            if (MeterShare.Trimmed(box) is { Count: > 0 } cut) bad.Add($"{at}: cut \"{cut[0]}\"");
            if (Section(box) is { } sec)
            {
                var p = box.TransformToAncestor(sec).Transform(new Point(0, 0));
                double top = p.Y, right = sec.ActualWidth - p.X - box.ActualWidth;
                // the hero's sit on its top line before the clock and the outcome (as Share did): its right half
                double room = view == ParserShare.Hero ? sec.ActualWidth / 2 - box.ActualWidth : 30;
                if (top > 44 || right > room || right < -1) bad.Add($"{at}: not top right of its section ({top:0} from the top, {right:0} from the right)");
            }
            else bad.Add($"{at}: no section");
            // Copy picture: that section's picture, and "Copied" in place
            long? member = view == ParserShare.Abilities ? w.ParserInspectForTest : null;
            got.Clear();
            Click(pic);
            if (got.Count != 1 || !got[0].File.EndsWith($"-{view}.png", StringComparison.Ordinal)) bad.Add($"{at}: picture {string.Join(",", got.Select(g => g.File))}");
            if (!Words(pic).Contains(L.T("Copied")) || pic.MinWidth <= 0) bad.Add($"{at}: no \"Copied\" in the button ({string.Join("|", Words(pic))})");
            // the abilities' picture: the member's card and their abilities, no way back drawn, nothing cut
            if (view == ParserShare.Abilities && member is long id)
            {
                var laid = ParserShare.Laid(e, w.ShareNowForTest(e), w.ParserPastForTest, ParserShare.Abilities, w.SharePartsOfForTest(id), MainWindow.ParserViewSample);
                var drawn = DrawnTexts(laid);
                var name = e.Roster.FirstOrDefault(f => f.Id == id)?.Name ?? "?";
                if (!TagsUnder(laid, "share-view-").SequenceEqual(new[] { "share-view-abilities" }) || !drawn.Contains(name) || drawn.Any(t => t.StartsWith("‹")) || !drawn.Contains(L.T("Ability")))
                    bad.Add($"{at}: the picture isn't {name}'s card and abilities ({string.Join(",", TagsUnder(laid, "share-view-"))})");
                if (MeterShare.Trimmed(laid) is { Count: > 0 } lc) bad.Add($"{at}: picture cut \"{lc[0]}\"");
            }
            // Copy text: the table's rows, tab-separated, its head row first
            if (txt != null)
            {
                setText(null);
                Click(txt);
                var lines = (text() ?? "").Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
                var heads = lines.Length > 1 ? lines[1].Split('\t') : Array.Empty<string>();
                int rows = view == ParserShare.Abilities ? e.Roster.First(f => f.Id == member).Abilities.Count : -1;
                if (lines.Length < 3 || !heads.Contains(head) || lines.Skip(2).Any(l => l.Split('\t').Length != heads.Length) || (rows >= 0 && lines.Length != rows + 2))
                    bad.Add($"{at}: text {lines.Length} lines, head {string.Join("|", heads)}");
                if (!Words(txt).Contains(L.T("Copied"))) bad.Add($"{at}: no \"Copied\" in Copy text");
            }
            seen.Add(at);
        }
        check(bad.Count == 0 && seen.Count == cases.Length,
            $"Parser copy buttons: the hero, group, timeline, damage taken, compare and abilities each have Copy picture (and Copy text on a table) top right of their head, words whole; each copies its own section and says \"Copied\" in place ({seen.Count} of {cases.Length})" + (bad.Count > 0 ? ": " + string.Join("; ", bad) : ""));

        // "Copied" stays through a redraw (a live fight redraws), then the words come back; the hero's ▾ opens its menu
        {
            var keep = MainWindow.CopiedFor;
            try
            {
                w.ShowParserDemo("detail"); DoEvents();
                var pic = FindTagged(w.ParserPage, "parser-copy-picture-group") as Button;
                MainWindow.CopiedFor = TimeSpan.FromSeconds(30);
                if (pic != null) Click(pic);
                w.ParserTabForTest = "Group"; DoEvents();
                var again = FindTagged(w.ParserPage, "parser-copy-picture-group") as Button;
                bool kept = again != null && again != pic && Words(again).Contains(L.T("Copied"));
                MainWindow.CopiedFor = TimeSpan.FromMilliseconds(40);
                var hero = FindTagged(w.ParserPage, "parser-copy-picture-hero") as Button;
                bool said = false;
                if (hero != null) { Click(hero); said = Words(hero).Contains(L.T("Copied")); }
                System.Threading.Thread.Sleep(150); DoEvents(); DoEvents();
                bool back = hero != null && Words(hero).Contains(L.T("Copy picture")) && !Words(hero).Contains(L.T("Copied")) && hero.MinWidth == 0;
                var more = FindTagged(w.ParserPage, "parser-share-more") as Button;
                if (more != null) { Click(more); DoEvents(); }
                var menu = w.MenuCardForTest is { } card ? Words(card) : new List<string>();
                w.CloseMenuForTest();
                check(kept && said && back && menu.Contains(L.T("This view")) && menu.Contains(L.T("Everything")),
                    $"Parser copy buttons: \"Copied\" holds through a redraw ({kept}) and the words come back after ({said}, {back}); the ▾ beside the hero's Copy picture opens the rest ({string.Join(", ", menu)})");
            }
            finally { MainWindow.CopiedFor = keep; }
        }
    }
}
