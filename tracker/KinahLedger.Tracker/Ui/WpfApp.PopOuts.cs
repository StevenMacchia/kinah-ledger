using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using KinahLedger.Packets;
using KinahLedger.Parser;
using KinahLedger.Session;

namespace KinahLedger.Ui;

public static partial class WpfApp
{
    /// <summary>The widths the pop-outs are checked and drawn at: the narrowest, the first one, a wide one.</summary>
    static readonly double[] PopOutWidths = { 300, 440, 620 };

    /// <summary>The words the pop-outs add to the window, outside the windows themselves (the menu, the Parser page, Settings).</summary>
    static readonly string[] PopOutWords =
    {
        "Pop out the DPS meter", "Pop out Applicants", "Its own window, for a second screen", "Open in its own window",
        "For a second screen. Works with the game in fullscreen.", "The meter in its own window, for a second screen. Works with the game in fullscreen.",
        "No boss pull yet. The meter shows here when one starts.", "Nobody is asking to join right now.", "Couldn't copy the picture", "Always on top", "Keep above other windows",
    };

    /// <summary>A boss pull under way in an Expedition (two members, the boss at 40%), and how to end it with a kill.</summary>
    static (BossParser Parser, Action Kill) PopOutFight()
    {
        var p = new BossParser(BossBook.Shared);
        p.Ended = _ => { };   // never kept on this PC
        var t = DateTime.UtcNow.AddSeconds(-20);
        var me = new GroupMember(1, "Swoly", "Gladiator", 4_812, 5_640, true, "Siel");
        var cleric = new GroupMember(2, "Aerinwyn", "Cleric", 4_655, 5_610, false, "Israphel");
        p.Take(new InstanceEntered(t, ContentKind.Expedition, "Ferocious Horn Den", 880));
        p.Take(new GroupChanged(t, new[] { me, cleric }));
        p.Take(new BossEngaged(t.AddSeconds(1), "Watchdog Kwapo", 881));
        p.Take(new BossHealth(t.AddSeconds(1), 881, 100_000, 100_000));
        p.Take(new DamageDealt(t.AddSeconds(2), 1, 881, 50_000, "Swoly", true, 11000000, 1));
        p.Take(new DamageDealt(t.AddSeconds(2), 2, 881, 10_000, "Aerinwyn", false, 17000000, 1));
        p.Take(new BossHealth(t.AddSeconds(2), 881, 40_000, 100_000));
        var end = DateTime.UtcNow.AddSeconds(-1);
        return (p, () => { p.Take(new DamageDealt(end, 1, 881, 40_000, "Swoly", true, 11000000, 1)); p.Take(new BossHealth(end, 881, 0, 100_000)); p.Take(new UnitDied(end, 881, "Watchdog Kwapo", true)); });
    }

    /// <summary>
    /// options-test, <c>KL_OPTIONS_ONLY=popouts</c> (Steven, 2026-11-18: "Yes, build pop-outs"): the DPS meter and the Applicants
    /// list in windows of their own, for a second screen beside a game in exclusive fullscreen. Each opens from the Overlays
    /// menu (its switch under its overlay's), the meter also from the Parser page's head, and each from its Settings page; a
    /// normal desktop window (its own, in the taskbar, takes focus, resizable, not on top unless asked); apart from its
    /// overlay (either, both or neither), and never gone because the game isn't in front; it follows the fight the overlay
    /// would show, the held result too; Share in it copies the picture; it remembers its spot and width, always opens on a
    /// screen, and opens again at start when it was open at quit; closing it is just closing it. Every word in Korean and
    /// Chinese, nothing cut at any width, nothing saying where the figures come from. Nothing is saved: the choices are the
    /// test's own; every window is parked off the screens.
    /// </summary>
    static void PopOutChecks(Settings settings, Action<bool, string> check)
    {
        static void Click(ButtonBase b) => b.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
        PopOutWindow.OffscreenForTest = true;
        ApplicantsOverlay.InvisibleForTest = true;
        bool still0 = Motion.Still; Motion.Still = true;
        var w = new MainWindow(settings, watch: false) { WindowStartupLocation = WindowStartupLocation.Manual, Left = -12000, Top = -12000, ShowActivated = false };
        w.Show(); DoEvents();
        var prefs = PopOutPrefs.Defaults();   // never saved (no path)
        w.PopOutPrefsForTest = prefs;
        var applicantPrefs0 = w.ApplicantPrefsForTest;
        w.ApplicantPrefsForTest = ApplicantPrefs.Defaults();
        var board = new PartyApplicants();
        w.ApplicantsBoardForTest = board;
        var sink = new List<string>();
        MainWindow.ShareSinkForTest = (_, name) => sink.Add(name);
        bool overlay0 = w.ParserOverlayOn;
        try
        {
            // the Overlays menu: a switch under the Parser overlay's and one under the Applicants overlay's, both off
            var names = w.OverlayEntries().Select(e => e.Text).ToList();
            int pi = names.IndexOf("Parser overlay"), mi = names.IndexOf("Pop out the DPS meter"), ai = names.IndexOf("Applicants overlay"), api = names.IndexOf("Pop out Applicants");
            bool offAtFirst = w.OverlayEntries().Where(e => e.Text.StartsWith("Pop out")).All(e => e.On == false);
            check(pi >= 0 && mi == pi + 1 && ai >= 0 && api == names.IndexOf("Applicants overlay") + 1 && offAtFirst,
                $"pop-outs: the Overlays menu has Pop out the DPS meter under the Parser overlay and Pop out Applicants under the Applicants overlay, both off ({string.Join(" | ", names)})");

            // the meter's: opened from the menu, a normal desktop window
            w.OverlayEntries().First(e => e.Text == "Pop out the DPS meter").Do!();
            DoEvents();
            var mp = w.MeterPopOutForTest;
            long ex = mp?.ExStyleForTest ?? 0;
            bool normal = mp is { IsVisible: true } && mp.Owner == null && mp.ShowInTaskbar && mp.ResizeMode == ResizeMode.CanResize && !mp.Topmost
                && (ex & Native.ExNoActivate) == 0 && (ex & Native.ExToolWindow) == 0 && (ex & Native.ExTransparent) == 0 && mp.Title.Contains(L.T("DPS meter"));
            check(normal && prefs.Meter.Open && w.OverlayEntries().First(e => e.Text == "Pop out the DPS meter").On == true,
                $"pop-outs: the menu's switch opens the meter in its own window: no owner, in the taskbar, resizable, takes focus, not on top unless asked ({normal}), kept as open ({prefs.Meter.Open})");

            // apart from the overlay, and never gone with the game out of front (the test's game is never in front)
            var (fight, kill) = PopOutFight();
            w.ResetParserHeldForTest();
            var off = w.MeterPopOutTickForTest(fight, overlayOn: false);
            var on = w.MeterPopOutTickForTest(fight, overlayOn: true);
            check(!off.OverlayWanted && off.PopOut is { Live: true, Boss: "Watchdog Kwapo" } && on.OverlayWanted && on.PopOut is { Live: true } && mp is { IsVisible: true } && mp.FaceForTest != null,
                $"pop-outs: the meter's window shows the pull under way with the overlay off ({off.PopOut?.Boss}) and on ({on.OverlayWanted}, {on.PopOut?.Boss}), the game not in front");
            // the held result: the pull ends, the window keeps it, as the overlay does
            var held = w.MeterPopOutTickForTest(fight, overlayOn: true, between: kill);
            check(held.PopOut is { Live: false, Outcome: "kill" } && held.PopOut.Id == on.PopOut?.Id, $"pop-outs: the pull killed, the window holds its result ({held.PopOut?.Outcome}, the same pull: {held.PopOut?.Id == on.PopOut?.Id})");

            // Share in its head: the picture of the fight it shows; nothing to share with a ready card
            sink.Clear();
            if (mp != null) mp.ShareForTest();
            DoEvents();
            bool shared = sink.Count == 1 && mp?.FlashForTest == L.T("Picture copied");
            var ready = Encounter.Ready(ContentKind.Expedition, "Ferocious Horn Den", "", DateTime.UtcNow);
            w.DrawMeterPopOutForTest(ready, DateTime.UtcNow); DoEvents();
            bool readyOff = mp?.ShareEnabledForTest == false;
            check(shared && readyOff, $"pop-outs: Share in the meter's window copies the picture of its fight ({sink.Count} picture, \"{mp?.FlashForTest}\"); off on a ready card ({readyOff})");

            // its spot and width kept; always on a screen; On top kept
            var area = SystemParameters.WorkArea;
            mp?.RememberForTest(area.Left + 50, area.Top + 60, 512);
            var spot = PopOutWindow.SpotFor(prefs.Meter, null, 512, 300);
            var gone = PopOutWindow.SpotFor(new PopOutPrefs.Spot { X = SystemParameters.VirtualScreenLeft - 40_000, Y = -30_000 }, null, 512, 300);
            bool onScreen = gone.X >= SystemParameters.VirtualScreenLeft && gone.Y >= SystemParameters.VirtualScreenTop && gone.X < SystemParameters.VirtualScreenLeft + SystemParameters.VirtualScreenWidth;
            if (mp != null) mp.OnTopForTest = true;
            bool topNow = mp?.Topmost == true && prefs.Meter.OnTop;
            if (mp != null) Click(mp.CloseButton);
            DoEvents();
            bool closed = w.MeterPopOutForTest == null && !prefs.Meter.Open && w.ParserOverlayOn == overlay0;
            w.OpenMeterPopOut(); DoEvents();
            var mp2 = w.MeterPopOutForTest;
            check(spot == new Point(area.Left + 50, area.Top + 60) && onScreen && topNow && closed && mp2 is { Width: 512, Topmost: true },
                $"pop-outs: the meter's window keeps its spot ({spot}) and width ({mp2?.Width}), a spot on a screen gone comes back on one ({gone}), Always on top kept ({topNow}); its cross just closes it ({closed})");

            // the Applicants list's: from the menu, with the overlay off; the overlay theme's face; empty says so
            w.SetApplicantsOverlay(false, save: false);
            var now = DateTime.UtcNow;
            board.Take(new PartyApplication(now.AddSeconds(-40), PartyApplication.Kind.Applied, "1", "Kyralis", "Gladiator", 45, 2_651, 133_774, "Zikel", null));
            board.Take(new PartyApplication(now.AddSeconds(-30), PartyApplication.Kind.Applied, "2", "Varrenthos", "Gladiator", 45, 2_651, 128_195, "Zikel", null));
            board.Take(new PartyApplication(now.AddSeconds(-20), PartyApplication.Kind.Applied, "3", "Embervale", "Sorcerer", 45, 2_651, 125_846, "Zikel", null));
            w.OverlayEntries().First(e => e.Text == "Pop out Applicants").Do!();
            DoEvents();
            var ap = w.ApplicantsPopOutForTest;
            bool three = ap?.FaceForTest is { } af && RowCount(af) == 3 && RowNames(af).SequenceEqual(new[] { "Kyralis", "Varrenthos", "Embervale" });
            bool themed = ap?.FaceForTest?.Tag as string == "applicants:" + HudThemes.Of(w.HudLook);
            bool apart = w.ApplicantsOverlayForTest == null && !w.ApplicantsOn;
            board.Take(new PartyApplication(now, PartyApplication.Kind.Cleared, "", "", "", 0, 0, 0, "", null));
            w.ApplicantsPopOutTickForTest(now.AddSeconds(1)); DoEvents();
            bool empty = ap is { IsVisible: true } && ap.FaceForTest is { } ef && RowCount(ef) == 0 && DrawnTexts(ef).Contains(L.T("Nobody is asking to join right now."));
            check(ap is { IsVisible: true, ShowInTaskbar: true, Owner: null } && three && themed && apart && empty && prefs.Applicants.Open,
                $"pop-outs: Applicants in its own window with the overlay off ({apart}): three oldest first ({three}) in the overlay's theme ({themed}); nobody waiting, it stays and says so ({empty})");

            // the overlay too: both at once
            board.Take(new PartyApplication(now, PartyApplication.Kind.Applied, "4", "Halvric", "Cleric", 45, 2_651, 110_552, "Triniel", null));
            w.SetApplicantsOverlay(true, save: false);
            w.ApplicantsTickForTest(now.AddSeconds(2)); w.ApplicantsPopOutTickForTest(now.AddSeconds(2)); DoEvents();
            bool both = w.ApplicantsOverlayForTest is { IsVisible: true } && w.ApplicantsPopOutForTest?.FaceForTest is { } bf && RowCount(bf) == 1;
            w.SetApplicantsOverlay(false, save: false);
            check(both, $"pop-outs: the Applicants overlay and its window both at once ({both})");

            // quit and start again: both open again where they were; closed by hand, they stay closed
            w.ClosePopOutsForQuitForTest(); DoEvents();
            bool goneAtQuit = w.MeterPopOutForTest == null && w.ApplicantsPopOutForTest == null && prefs.Meter.Open && prefs.Applicants.Open;
            w.ReopenPopOutsForTest(); DoEvents();
            bool back = w.MeterPopOutForTest is { IsVisible: true } && w.ApplicantsPopOutForTest is { IsVisible: true };
            if (w.ApplicantsPopOutForTest is { } ap2) Click(ap2.CloseButton);
            DoEvents();
            w.ClosePopOutsForQuitForTest(); w.ReopenPopOutsForTest(); DoEvents();
            bool stays = w.ApplicantsPopOutForTest == null && w.MeterPopOutForTest != null;
            check(goneAtQuit && back && stays, $"pop-outs: open at quit, both open again at start ({goneAtQuit}, {back}); one closed by hand stays closed ({stays})");
            w.CloseMeterPopOut(); DoEvents();

            // Settings: Open in its own window on the DPS meter page and the Applicants page, each page still fitting the window
            var fit = new List<string>(); var srcWords = new List<string>();
            foreach (var (page, meter) in new[] { (OptionsWindow.Page.Meter, true), (OptionsWindow.Page.Applicants, false) })
            {
                var o = new OptionsWindow(w, page) { WindowStartupLocation = WindowStartupLocation.Manual, Left = -12000, Top = -12000, ShowActivated = false };
                o.Show(); DoEvents();
                if (o.Scroller.ExtentHeight > o.Scroller.ViewportHeight + 1) fit.Add($"{page}: {o.Scroller.ExtentHeight:0} of {o.Scroller.ViewportHeight:0}");
                var b = meter ? o.MeterPopOutBtn : o.ApplicantsPopOutBtn;
                if (b == null) fit.Add($"{page}: no button");
                else { Click(b); DoEvents(); }
                bool opened = meter ? w.MeterPopOutForTest is { IsVisible: true } : w.ApplicantsPopOutForTest is { IsVisible: true };
                if (!opened) fit.Add($"{page}: the button opened nothing");
                srcWords.AddRange(w.SourceWordsShown(meter ? o.PageMeter : o.PageApplicants));
                o.Close(); DoEvents();
                if (meter) w.CloseMeterPopOut(); else w.CloseApplicantsPopOut();
                DoEvents();
            }
            check(fit.Count == 0, "pop-outs: Settings › DPS meter and › Applicants open their window (Open in its own window), each page fitting the window" + (fit.Count > 0 ? ": " + string.Join("; ", fit) : ""));

            // the Parser page's head: Pop out
            w.SkipWelcomeForTest();
            w.ShowParserDemo("live"); DoEvents();
            var pop = w.MeterPopOutButtonForTest;
            bool popShown = pop is { IsVisible: true };   // (the click draws the page again: asked before it)
            if (pop != null) Click(pop);
            DoEvents();
            bool fromPage = popShown && w.MeterPopOutForTest is { IsVisible: true } && w.MeterPopOutForTest.FaceForTest != null;
            check(fromPage, $"pop-outs: Pop out in the Parser page's head opens the meter's window, a fight in it ({fromPage}: button {(pop == null ? "none" : popShown ? "shown" : "hidden")}, window {(w.MeterPopOutForTest is { } mw ? (mw.IsVisible ? "open" : "hidden") + (mw.FaceForTest != null ? ", a fight" : ", \"" + mw.EmptyTextForTest + "\"") : "none")})");

            // every width, every language: nothing cut, every word translated, nothing saying where the figures come from
            var problems = new List<string>(); var missing = new HashSet<string>();
            var (_, live) = ParserDemo.Sample(DateTime.UtcNow, live: true, damage: true);
            InEachLanguage(code =>
            {
                L.Misses = code == "en" ? null : new HashSet<string>();
                if (code != "en") foreach (var s in PopOutWords) if (!L.CurrentTable().ContainsKey(s)) missing.Add($"{code}: {s}");
                _ = w.OverlayEntries();
                foreach (double width in PopOutWidths)
                {
                    var m = new MeterPopOut(w, new PopOutPrefs.Spot { W = width }, () => { });
                    foreach (var look in new[] { HudThemes.Looks(HudThemes.Tactical)[0].Key, HudThemes.Looks(HudThemes.Modern)[0].Key, ParserPrefs.StandardLook })
                    {
                        m.Look = look;
                        m.Draw(live, DateTime.UtcNow, true);
                        if (!m.IsVisible) m.Open();
                        DoEvents();
                        PopOutProblems(m, $"meter {look} {width} {code}", live!.Members.Select(f => f.Name).ToList(), problems, srcWords, w);
                    }
                    m.Draw(null, DateTime.UtcNow, false); DoEvents();
                    PopOutProblems(m, $"meter empty {width} {code}", null, problems, srcWords, w);
                    m.Close();
                    var a = new ApplicantsPopOut(w, new PopOutPrefs.Spot { W = width }, () => { });
                    var items = ApplicantsFace.Sample(10, DateTime.UtcNow);
                    a.Draw(items, DateTime.UtcNow, HudThemes.Classic); a.Open(); DoEvents();
                    PopOutProblems(a, $"applicants {width} {code}", items.Select(i => i.Name).ToList(), problems, srcWords, w);
                    a.Draw(Array.Empty<PartyApplicants.Applicant>(), DateTime.UtcNow, HudThemes.Tactical); DoEvents();
                    PopOutProblems(a, $"applicants empty {width} {code}", null, problems, srcWords, w);
                    a.Close();
                }
                DoEvents();
                if (L.Misses != null) lock (L.Misses) foreach (var s in L.Misses) missing.Add($"{code}: {s}");
                L.Misses = null;
            });
            check(problems.Count == 0, "pop-outs: both windows at 300, 440 and 620 wide in English, Korean and Chinese, nothing cut, the drawing filling the width" + (problems.Count > 0 ? ": " + string.Join("; ", problems.Take(10)) : ""));
            check(missing.Count == 0, "pop-outs: every word in Korean and Chinese" + (missing.Count > 0 ? ": " + string.Join("; ", missing.Take(12)) : ""));
            check(srcWords.Count == 0, "pop-outs: nothing says where the figures come from" + (srcWords.Count > 0 ? ": " + string.Join("; ", srcWords.Take(6)) : ""));
        }
        finally
        {
            MainWindow.ShareSinkForTest = null;
            w.CloseMeterPopOut(); w.CloseApplicantsPopOut();
            w.ApplicantsBoardForTest = null; w.ApplicantPrefsForTest = applicantPrefs0;
            Motion.Still = still0;
            w.Close();
            DoEvents();
        }
    }

    /// <summary>A pop-out's problems: the drawing not filling the stage's width, a word cut, anything saying where the figures come from.</summary>
    static void PopOutProblems(PopOutWindow win, string what, IReadOnlyCollection<string>? names, List<string> problems, List<string> src, MainWindow w)
    {
        win.UpdateLayout();
        var root = (FrameworkElement)win.Content;
        foreach (var c in CutOffs(root, names)) problems.Add($"{what}: {c}");
        if (win.FaceForTest is { } face)
        {
            double want = win.ActualWidth - 2 - 2 * PopOutWindow.StagePad, got = face.ActualWidth * win.ScaleForTest;
            if (Math.Abs(want - got) > 2) problems.Add($"{what}: drawn {got:0} wide on a stage {want:0} wide");
        }
        else if (win.EmptyTextForTest.Length == 0) problems.Add($"{what}: nothing drawn");
        src.AddRange(w.SourceWordsShown(root).Select(s => $"{what}: {s}"));
    }

    /// <summary>
    /// ui-shot DIR --popouts: the two pop-out windows as they open on a second screen, one sheet per window and language
    /// (popouts-meter-LANG.png, popouts-applicants-LANG.png): the meter under way and held, at 300, 440 and 620 wide, in the
    /// Night skin with the standard panel, Daylight with Podium and Tactical with its own fight look; Applicants with 0, 1, 4
    /// and 10 waiting in Night (Classic), Tactical and Daylight (Modern). Each window alone as well
    /// (popout-meter-SKIN-STATE-WIDTH-LANG.png ...); spill.txt names anything cut, missing.txt any word left in English.
    /// </summary>
    public static int ShotPopOuts(Settings settings, string dir)
    {
        Ensure();
        Directory.CreateDirectory(dir);
        foreach (var f in new[] { "spill.txt", "missing.txt" }) if (File.Exists(Path.Combine(dir, f))) File.Delete(Path.Combine(dir, f));
        PopOutWindow.OffscreenForTest = true;
        bool still0 = Motion.Still; Motion.Still = true;
        var keep = new Window { Left = -12000, Top = -12000, Width = 10, Height = 10, ShowActivated = false, ShowInTaskbar = false, WindowStyle = WindowStyle.None, WindowStartupLocation = WindowStartupLocation.Manual };
        keep.Show();
        var now = new DateTime(2026, 11, 18, 21, 30, 0, DateTimeKind.Utc);
        var (past, live) = ParserDemo.Sample(now, live: true, damage: true);
        var held = past.Last(e => e.Outcome == "kill");
        var meterRows = new (string Skin, string Look)[] { ("night", ParserPrefs.StandardLook), ("daylight", ParserPrefs.PodiumLook), (HudThemes.Tactical, HudThemes.Looks(HudThemes.Tactical)[0].Key) };
        var appRows = new (string Skin, string Theme)[] { ("night", HudThemes.Classic), (HudThemes.Tactical, HudThemes.Tactical), ("daylight", HudThemes.Modern) };
        int[] counts = { 0, 1, 4, 10 };
        var skin0 = Skins.Current;
        try
        {
            InEachLanguage(code =>
            {
                L.Misses = code == "en" ? null : new HashSet<string>();
                // the meter
                var sheet = SheetGrid(PopOutWidths.Length * 2);
                int col = 0;
                foreach (var width in PopOutWidths) foreach (var st in new[] { "live", "held" }) SheetHead(sheet, ++col, $"{st} · {width:0}");
                for (int r = 0; r < meterRows.Length; r++)
                {
                    var (skin, look) = meterRows[r];
                    Skins.Apply(skin);
                    SheetLabel(sheet, r + 1, $"{skin} · {look}");
                    col = 0;
                    foreach (var width in PopOutWidths)
                        foreach (var st in new[] { "live", "held" })
                        {
                            var m = new MeterPopOut(keep, new PopOutPrefs.Spot { W = width }, () => { });
                            m.Look = look;
                            var e = st == "live" ? live! : held;
                            m.Draw(e, st == "live" ? now : e.End ?? e.LastAt, true);
                            m.Open(); DoEvents();
                            var file = $"popout-meter-{skin}-{st}-{width:0}-{code}.png";
                            PopOutShot(m, dir, file, sheet, r + 1, ++col);
                            m.Close();
                        }
                }
                SaveSheet(sheet, Path.Combine(dir, $"popouts-meter-{code}.png"));
                // the Applicants list
                sheet = SheetGrid(counts.Length);
                for (int c = 0; c < counts.Length; c++) SheetHead(sheet, c + 1, $"{counts[c]} waiting · 440");
                for (int r = 0; r < appRows.Length; r++)
                {
                    var (skin, theme) = appRows[r];
                    Skins.Apply(skin);
                    SheetLabel(sheet, r + 1, $"{skin} · {theme}");
                    for (int c = 0; c < counts.Length; c++)
                    {
                        var a = new ApplicantsPopOut(keep, new PopOutPrefs.Spot { W = 440 }, () => { });
                        a.Draw(ApplicantsFace.Sample(counts[c], now), now, theme);
                        a.Open(); DoEvents();
                        PopOutShot(a, dir, $"popout-applicants-{theme}-{counts[c]}-{code}.png", sheet, r + 1, c + 1);
                        a.Close();
                    }
                }
                SaveSheet(sheet, Path.Combine(dir, $"popouts-applicants-{code}.png"));
                if (L.Misses is { Count: > 0 } miss) File.AppendAllLines(Path.Combine(dir, "missing.txt"), miss.Select(m => $"{code}: {m}"));
                L.Misses = null;
            });
        }
        finally { Skins.Apply(skin0); Motion.Still = still0; keep.Close(); }
        bool bad = File.Exists(Path.Combine(dir, "spill.txt")) || File.Exists(Path.Combine(dir, "missing.txt"));
        Console.WriteLine($"wrote {Path.GetFullPath(dir)}" + (bad ? " (see spill.txt / missing.txt)" : " (nothing cut, every word translated)"));
        return bad ? 1 : 0;
    }

    static Grid SheetGrid(int cols)
    {
        var g = new Grid { Background = new SolidColorBrush(Color.FromRgb(0x2A, 0x2E, 0x36)) };
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        for (int i = 0; i < cols; i++) g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        return g;
    }

    static void SheetHead(Grid g, int col, string text)
    {
        var h = new TextBlock { Text = text, Foreground = Brushes.White, FontSize = 14, FontWeight = FontWeights.Bold, Margin = new Thickness(12, 12, 12, 4) };
        Grid.SetColumn(h, col); g.Children.Add(h);
    }

    static void SheetLabel(Grid g, int row, string text)
    {
        while (g.RowDefinitions.Count <= row) g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var t = new TextBlock { Text = text, Foreground = Brushes.White, FontSize = 13, FontWeight = FontWeights.Bold, Margin = new Thickness(12, 14, 8, 0), VerticalAlignment = VerticalAlignment.Top, Width = 130, TextWrapping = TextWrapping.Wrap };
        Grid.SetRow(t, row); g.Children.Add(t);
    }

    /// <summary>The window drawn alone to its file and onto the sheet (a picture of it, as the window lives off the screens).</summary>
    static void PopOutShot(PopOutWindow win, string dir, string file, Grid sheet, int row, int col)
    {
        var root = (FrameworkElement)win.Content;
        root.UpdateLayout();
        SavePart(root, Path.Combine(dir, file), new SolidColorBrush(Color.FromRgb(0x2A, 0x2E, 0x36)));
        SpillNote(dir, file, root);
        int w = (int)Math.Ceiling(root.ActualWidth), h = (int)Math.Ceiling(root.ActualHeight);
        if (w < 1 || h < 1) return;
        var rtb = new System.Windows.Media.Imaging.RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
        rtb.Render(root);
        rtb.Freeze();
        var img = new System.Windows.Controls.Image { Source = rtb, Width = w, Height = h, Margin = new Thickness(12), VerticalAlignment = VerticalAlignment.Top, HorizontalAlignment = HorizontalAlignment.Left };
        Grid.SetRow(img, row); Grid.SetColumn(img, col); sheet.Children.Add(img);
    }

    static void SaveSheet(Grid sheet, string path)
    {
        var sw = Parked(sheet);
        Save(sheet, path, 0);
        sw.Close();
    }
}
