using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using KinahLedger.Parser;
using KinahLedger.Session;

namespace KinahLedger.Ui;

public static partial class WpfApp
{
    /// <summary>Every theme family: the companion has fur and gear for each (the bubble's own finish in the first five).</summary>
    static readonly string[] CompanionThemes = HudThemes.All.Select(t => t.Key).ToArray();
    static readonly CompanionKind[] CompanionKinds = Enum.GetValues<CompanionKind>();

    /// <summary>
    /// options-test, KL_OPTIONS_ONLY=companion (Buddy 2.0, 2026-11-20): the companion. Off by default, in the Overlays menu;
    /// each moment comes once and only from what the tracker knows (a new best measured as the PB chip, a boss down, a wipe,
    /// a streak of your own big hits, crits named only when known, a death, a timer come close), never on a first look; one
    /// reaction at a time, a better one first; a window of its own that never takes focus, click-through only as a setting;
    /// Steven's size (1.1); its Settings page; every theme and moment in three languages, nothing cut, spilling or untranslated.
    /// </summary>
    static void CompanionChecks(MainWindow w, Action<bool, string> check)
    {
        static void Click(System.Windows.Controls.Primitives.ButtonBase b) => b.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
        static void Flip(CheckBox c) { c.IsChecked = c.IsChecked != true; Click(c); }
        bool invisible0 = CompanionOverlay.InvisibleForTest;
        CompanionOverlay.InvisibleForTest = true;
        var prefs0 = w.CompanionPrefsForTest;
        w.CompanionPrefsForTest = CompanionPrefs.Defaults();   // never saved
        bool still0 = Motion.Still;
        Motion.Still = true;
        try
        {
            var d = CompanionPrefs.Defaults();
            check(!d.On && d.Lines && !d.ClickThrough && CompanionKinds.All(d.Reacts) && w.OverlaysForTest().Any(o => o.Name == "Companion" && !o.On),
                "companion: off by default (lines on, click-through off, every reaction on), and in the header's Overlays menu");

            // the moments, from what the tracker knows
            var now = DateTime.UtcNow;
            var watch = new CompanionWatch();
            Encounter Pull(string outcome, double dps, DateTime end, string boss = "Gadioton")
            {
                var e = new Encounter { Boss = boss, Instance = "Fire Temple", Start = end.AddSeconds(-192), End = end, Outcome = outcome, HasDamage = true };
                e.Roster.Add(new Fighter { Id = 7, Name = "Swoly", Class = "Gladiator", You = true, Damage = (long)(dps * 192) });
                return e;
            }
            var first = Pull("kill", 50_000, now.AddSeconds(-3));
            bool firstQuiet = watch.Fights(null, first, _ => null, now).Count == 0;
            var pb = Pull("kill", 54_000, now.AddSeconds(-1));
            var pbSaid = watch.Fights(null, pb, e => new PullRef { E = e, Dps = 50_000 }, now);
            var again = watch.Fights(null, pb, e => new PullRef { E = e, Dps = 50_000 }, now);
            var slow = Pull("kill", 40_000, now.AddSeconds(-1));
            var killSaid = watch.Fights(null, slow, e => new PullRef { E = e, Dps = 50_000 }, now);
            var wipeSaid = watch.Fights(null, Pull("wipe", 30_000, now.AddSeconds(-2)), _ => null, now);
            var oldSaid = watch.Fights(null, Pull("kill", 90_000, now.AddMinutes(-3)), e => new PullRef { E = e, Dps = 50_000 }, now);
            bool pbOk = pbSaid.Count == 1 && pbSaid[0].Kind == CompanionKind.Best && pbSaid[0].Accent == PK.Signed(0.08) && pbSaid[0].Text.StartsWith("Gadioton");
            check(firstQuiet && pbOk && again.Count == 0 && killSaid is [{ Kind: CompanionKind.Kill }] && wipeSaid is [{ Kind: CompanionKind.Wipe }] && oldSaid.Count == 0,
                $"companion: a fight's end, once: nothing on a first look ({firstQuiet}), a new best as the PB chip measures it ({string.Join(",", pbSaid.Select(m => m.Text + m.Accent))}), a boss down, a wipe, nothing for a fight that ended minutes ago");

            // a streak of your own big hits, crits only when every one is known to be
            var live = new Encounter { Boss = "Gadioton", Start = now.AddSeconds(-20), HasDamage = true };
            live.Roster.Add(new Fighter { Id = 7, Name = "Swoly", You = true, Damage = 1 });
            live.Roster.Add(new Fighter { Id = 8, Name = "Kyralis", Damage = 1 });
            var sw = new CompanionWatch();
            live.BigHits = new() { new BigHit(1, 7, 900, null, now.AddSeconds(-30)) };
            bool baseQuiet = sw.Fights(live, null, null, now).Count == 0;
            live.BigHits.Add(new BigHit(2, 7, 900, null, now.AddSeconds(-2)));
            live.BigHits.Add(new BigHit(3, 8, 900, null, now.AddSeconds(-1.5)));
            bool twoQuiet = sw.Fights(live, null, null, now).Count == 0;
            live.BigHits.Add(new BigHit(4, 7, 900, null, now.AddSeconds(-1)));
            live.BigHits.Add(new BigHit(5, 7, 900, null, now));
            var streak = sw.Fights(live, null, null, now);
            live.BigHits.Add(new BigHit(6, 7, 900, true, now)); live.BigHits.Add(new BigHit(7, 7, 900, true, now)); live.BigHits.Add(new BigHit(8, 7, 900, true, now));
            bool rests = sw.Fights(live, null, null, now.AddSeconds(1)).Count == 0;
            var cw = new CompanionWatch();
            var cl = new Encounter { Start = now.AddSeconds(-5), HasDamage = true };
            cl.Roster.Add(new Fighter { Id = 7, You = true, Damage = 1 });
            cl.BigHits = new();
            cw.Fights(cl, null, null, now);
            for (int i = 0; i < 3; i++) cl.BigHits.Add(new BigHit(10 + i, 7, 900, true, now.AddSeconds(-2 + i)));
            var crit = cw.Fights(cl, null, null, now);
            check(baseQuiet && twoQuiet && streak is [{ Kind: CompanionKind.Streak, Label: var lb }] && lb == L.T("HOT STREAK") && rests && crit is [{ Label: var cl2 }] && cl2 == L.T("CRIT STREAK"),
                $"companion: a streak of three of your own big hits within {CompanionWatch.StreakSeconds} s (another's not counted, old ones not), then a rest; called crits only when each is known to be one ({string.Join(",", streak.Select(m => m.Label + " " + m.Text))})");

            // your deaths, the timers
            var dw = new CompanionWatch();
            var eng = new object();
            bool dBase = dw.Deaths(eng, 2, now) == null, dSame = dw.Deaths(eng, 2, now) == null;
            var died = dw.Deaths(eng, 3, now);
            bool dNew = dw.Deaths(new object(), 9, now) == null;
            var tw = new CompanionWatch();
            var item = new TimerBoard.Item("boss:Eternal Gartua", "World bosses", "Eternal Gartua", "", TimerBoard.Kind.Waiting, now.AddMinutes(4.5), "4:30", "") { Short = "Gartua" };
            var t1 = tw.Timers(new[] { item }, now);
            var t2 = tw.Timers(new[] { item }, now.AddSeconds(30));
            check(dBase && dSame && died is { Kind: CompanionKind.Death } && dNew && t1 is [{ Kind: CompanionKind.Timer }] && t1[0].Text.Contains("Gartua") && t1[0].Accent == L.F("in {0} min", 5) && t2.Count == 0,
                $"companion: a death of yours once (none on a first look or another session), a watched timer come close once ({string.Join(",", t1.Select(m => m.Text + m.Accent))})");

            // an extra beside Buddy, never in place of it: the Kinah overlay and its look stay as they are, on and off
            string look0 = w.HudLook; bool kinah0 = w.OverlaysForTest().First(o => o.Name == "Kinah").On;
            w.CompanionForceForTest = true;
            w.SetCompanionOverlay(true, save: false); DoEvents();
            bool beside = w.HudLook == look0 && w.OverlaysForTest().First(o => o.Name == "Kinah").On == kinah0 && w.CompanionOverlayForTest != null;
            w.SetCompanionOverlay(false, save: false); DoEvents();
            bool gone = w.CompanionOverlayForTest == null && !w.CompanionOn && w.HudLook == look0 && w.OverlaysForTest().First(o => o.Name == "Kinah").On == kinah0;
            check(beside && gone, $"companion: an extra beside Buddy: switched on or off, the Kinah overlay and its look (Buddy) stay as they were, and off closes it ({beside}, {gone})");

            // the window: shown with the game, one reaction at a time, a better one first, the bubble with Lines on
            w.SetCompanionOverlay(true, save: false); DoEvents();
            var ov = w.CompanionOverlayForTest;
            bool shown = ov is { IsVisible: true } && ov.FaceForTest != null;
            ov?.Play(MainWindow.CompanionSample(CompanionKind.Kill, now)); DoEvents();
            bool killPlays = ov?.PlayingForTest?.Kind == CompanionKind.Kill && ov.FaceForTest?.BubbleForTest != null;
            ov?.Play(MainWindow.CompanionSample(CompanionKind.Best, now)); DoEvents();
            bool bestTakes = ov?.PlayingForTest?.Kind == CompanionKind.Best && ov.QueuedForTest == 0;
            ov?.Play(MainWindow.CompanionSample(CompanionKind.Streak, now));
            ov?.Play(MainWindow.CompanionSample(CompanionKind.Timer, now)); DoEvents();
            bool waits = ov?.PlayingForTest?.Kind == CompanionKind.Best && ov.QueuedForTest == 2;
            ov?.EndReactionForTest(); DoEvents();
            bool nextBest = ov?.PlayingForTest?.Kind == CompanionKind.Timer && ov.QueuedForTest == 1;
            ov?.EndReactionForTest(); ov?.EndReactionForTest(); DoEvents();
            check(shown && killPlays && bestTakes && waits && nextBest, $"companion: shown while the game is open; one reaction at a time with its line, a better one takes over, lesser ones wait and the best of them plays next ({shown}, {killPlays}, {bestTakes}, {waits}, {nextBest})");

            w.SetCompanionLines(false); DoEvents();
            ov = w.CompanionOverlayForTest;
            ov?.Play(MainWindow.CompanionSample(CompanionKind.Rare, now)); DoEvents();
            bool silent = ov?.PlayingForTest?.Kind == CompanionKind.Rare && ov.FaceForTest is { Lines: false } && ov.FaceForTest.BubbleForTest == null;
            ov?.EndReactionForTest();
            w.SetCompanionLines(true); DoEvents();
            w.SetCompanionReacts(CompanionKind.Rare, false);
            w.CompanionRareForTest("Ancient Kaisinel Greatsword", "Legend"); DoEvents();
            bool rareOff = ov?.PlayingForTest == null;
            w.SetCompanionReacts(CompanionKind.Rare, true);
            w.CompanionRareForTest("Ancient Kaisinel Greatsword", "Legend"); DoEvents();
            bool rareOn = ov?.PlayingForTest?.Kind == CompanionKind.Rare && ov.FaceForTest?.BubbleForTest != null;
            ov?.EndReactionForTest();
            check(silent && rareOff && rareOn, $"companion: Lines off moves without a line; a reaction switched off never plays, on again it does ({silent}, {rareOff}, {rareOn})");

            // a window of its own over the game, never focused, click-through only as a setting, grabbable over the companion
            long ex0 = ov?.ExStyleForTest ?? 0;
            w.SetCompanionClickThrough(true); DoEvents();
            long ex1 = ov?.ExStyleForTest ?? 0;
            ov?.GripForTest(true, true); DoEvents();
            long exGrip = ov?.ExStyleForTest ?? 0; bool grip = ov?.GripShownForTest == true;
            ov?.GripForTest(false, false);
            w.SetCompanionClickThrough(false); DoEvents();
            bool style = (ex0 & Native.ExNoActivate) != 0 && (ex0 & Native.ExToolWindow) != 0 && (ex0 & Native.ExTransparent) == 0 && (ex1 & Native.ExTransparent) != 0 && (exGrip & Native.ExTransparent) == 0 && grip;
            check(ov != null && ov.Topmost && !ov.ShowActivated && ov.Owner == null && style, $"companion: its own window, on top, never takes focus, out of Alt+Tab; click-through only as a setting, grabbable over the companion then ({style})");
            w.CompanionPrefsForTest.X = 300; w.CompanionPrefsForTest.Y = 200;
            w.ResetCompanionPosition();
            check(w.CompanionPrefsForTest.X == null && w.CompanionPrefsForTest.Y == null, "companion: Reset position forgets the spot");

            // at Steven's size (1.1) and look (Terminal: the Glitch theme) with click-through on, as he plays: drawn larger, nothing cut
            ov?.Match(HudThemes.Glitch, 0.94, 1.1); w.SetCompanionClickThrough(true); DoEvents();
            ov?.Play(MainWindow.CompanionSample(CompanionKind.Best, now)); DoEvents();
            double wide = ov?.ActualWidth ?? 0;
            bool glitch = ov?.FaceForTest?.Theme == HudThemes.Glitch;
            bool big = Math.Abs(wide - (CompanionSprite.Side + CompanionFace.BubbleRoom) * 1.1) < 2 && (ov?.ExStyleForTest & Native.ExTransparent) != 0;
            ov?.EndReactionForTest();
            w.SetCompanionClickThrough(false);
            ov?.Match(HudThemes.Classic, 0.94, 1);
            check(big && glitch, $"companion: at 110% in the Glitch theme with click-through on (Steven's hud.json) it is drawn 1.1× ({wide:0} px) and lets clicks through");

            // its mood: leaning in for a fight, dozing after a long quiet
            ov?.SetMood(CompanionSprite.Mood.Focused);
            bool focused = ov?.FaceForTest?.Sprite.FaceForTest == "Focused";
            ov?.SetMood(CompanionSprite.Mood.Sleepy);
            bool sleepy = ov?.FaceForTest?.Sprite.FaceForTest == "Sleepy";
            var faces = new CompanionFace(HudThemes.Classic, 0.94, false, false).Sprite;
            faces.HoldForRender(null, Colors.White, 0, .5, 1); string smile = faces.FaceForTest;
            faces.HoldForRender(null, Colors.White, 0, .5, 2); string plain = faces.FaceForTest;
            ov?.SetMood(CompanionSprite.Mood.Idle);
            check(focused && sleepy && smile == "Happy" && plain == "Idle", $"companion: leans in for a fight, dozes after a long quiet, and smiles now and then as it floats ({smile}, {plain})");

            // fur and gear for every theme family, so it matches the Buddy's look: none falls back to Classic's, and the
            // families whose looks differ (Elements: frost against magma) tint it by the look the Buddy uses
            var fams = HudThemes.All.Select(t => (t.Key, Sprite: new CompanionSprite(t.Key))).ToList();
            bool ownTint = fams.All(f => f.Sprite.Theme == f.Key) && fams.Select(f => f.Sprite.TintForTest).Distinct().Count() == fams.Count;
            bool byLook = new CompanionSprite(HudThemes.Elements, HudThemes.Magma).TintForTest != new CompanionSprite(HudThemes.Elements, HudThemes.Frost).TintForTest;
            ov?.Match(HudThemes.Elements, 0.94, 1, HudThemes.Magma); DoEvents();
            bool matched = ov?.FaceForTest?.Sprite is { Theme: HudThemes.Elements, Look: HudThemes.Magma } && ov.FaceForTest.Theme == HudThemes.Classic;
            ov?.Match(HudThemes.Classic, 0.94, 1);
            check(ownTint && byLook && matched, $"companion: its own fur and gear in each of the {fams.Count} theme families, by look where a family's looks differ, and the window follows the Buddy's look ({ownTint}, {byLook}, {matched})");

            // the Settings page: an overlay page, its switch turns the companion off and on, a switch and Try per reaction
            var o = new OptionsWindow(w, OptionsWindow.Page.Companion) { WindowStartupLocation = WindowStartupLocation.Manual, Left = -12000, Top = -12000, ShowActivated = false };
            o.Show(); DoEvents();
            bool page = o.PageShownForTest == OptionsWindow.Page.Companion && OptionsWindow.OverlayPages.Any(p => p.Page == OptionsWindow.Page.Companion) && o.CompanionChk?.IsChecked == true
                && o.CompanionReactChks.Count == CompanionKinds.Length && o.CompanionStageForTest?.Sprite.Playing == CompanionKind.Best;
            bool fits = o.Scroller.ExtentHeight <= o.Scroller.ViewportHeight + 1;
            if (o.CompanionChk != null) Flip(o.CompanionChk);
            DoEvents();
            bool off = !w.CompanionOn && w.CompanionOverlayForTest == null;
            if (o.CompanionChk != null) Flip(o.CompanionChk);
            DoEvents();
            bool back = w.CompanionOn && w.CompanionOverlayForTest is { IsVisible: true };
            bool hasWipe = o.CompanionReactChks.TryGetValue(CompanionKind.Wipe, out var wipeChk);
            if (wipeChk != null) Flip(wipeChk);
            bool wipeOff = !w.CompanionPrefsForTest.Reacts(CompanionKind.Wipe);
            if (!wipeOff) Console.WriteLine($"  companion page: wipe switch {(hasWipe ? "found" : "missing")}, checked {wipeChk?.IsChecked}, prefs same {ReferenceEquals(o.CompanionPrefsShownForTest, w.CompanionPrefsForTest)}");
            if (wipeChk != null) Flip(wipeChk);
            var pageWords = w.SourceWordsShown(o.PageCompanion);
            o.Close(); DoEvents();
            check(page && fits && off && back && wipeOff && w.CompanionPrefsForTest.Reacts(CompanionKind.Wipe) && pageWords.Count == 0,
                $"companion: Settings › Companion, with its switch, a stage, a switch per reaction, fitting the window ({page}, {fits}, {off}, {back}, {wipeOff}{(pageWords.Count > 0 ? "; says " + string.Join(" | ", pageWords) : "")})");

            // every theme and every moment, three languages: nothing cut or spilling, every word translated
            var problems = new List<string>(); var missing = new HashSet<string>(); var sources = new List<string>();
            InEachLanguage(code =>
            {
                L.Misses = code == "en" ? null : new HashSet<string>();
                foreach (var theme in CompanionThemes)
                    foreach (var k in CompanionKinds)
                    {
                        var m = MainWindow.CompanionSample(k, now);
                        var face = new CompanionFace(theme, 0.94, true, k == CompanionKind.Pvp);
                        face.Sprite.HoldForRender(k, m.Tone, CompanionSprite.KeyMs(k), 0.25);
                        face.HoldForRender(m, 1400);
                        var win = Parked(face);
                        foreach (var c in CutOffs(face)) problems.Add($"{theme} {k} {code}: {c}");
                        if (Spill(face) is { Length: > 0 } sp) problems.Add($"{theme} {k} {code}: {sp}");
                        sources.AddRange(w.SourceWordsShown(face).Select(s => $"{theme} {code}: {s}"));
                        win.Close();
                    }
                if (L.Misses != null) lock (L.Misses) foreach (var x in L.Misses) missing.Add($"{code}: {x}");
                L.Misses = null;
            });
            check(problems.Count == 0, "companion: every theme and moment in English, Korean and Chinese, nothing cut or spilling" + (problems.Count > 0 ? ": " + string.Join("; ", problems.Take(12)) : ""));
            check(missing.Count == 0, "companion: every word in Korean and Chinese" + (missing.Count > 0 ? ": " + string.Join("; ", missing.Take(12)) : ""));
            check(sources.Count == 0, "companion: nothing says where the figures come from" + (sources.Count > 0 ? ": " + string.Join("; ", sources.Take(6)) : ""));
        }
        finally
        {
            w.SetCompanionOverlay(false, save: false);
            w.CompanionForceForTest = false;
            w.CompanionPrefsForTest = prefs0;
            Motion.Still = still0;
            CompanionOverlay.InvisibleForTest = invisible0;
        }
    }

    /// <summary>
    /// companion-bench [seconds] [--on-screen]: what the companion costs (Steven, 2026-11-20: "lightweight - not bulky"). The
    /// process's CPU time over a quiet baseline (a parked window, nothing drawn), then the companion idling (its float, wings,
    /// glow and blinks at 30 frames a second), then reacting every 6 s with its line; each as ms of CPU a second and % of one
    /// core. Parked off every screen unless --on-screen (only under tests\hidden-run.ps1, on its own desktop).
    /// </summary>
    public static int BenchCompanion(int seconds, bool onScreen)
    {
        Ensure();
        bool still0 = Motion.Still; Motion.Still = false;
        CompanionOverlay.InvisibleForTest = !onScreen;
        var proc = System.Diagnostics.Process.GetCurrentProcess();
        double Cpu() { proc.Refresh(); return proc.TotalProcessorTime.TotalMilliseconds; }
        static void Run(double sec)
        {
            var frame = new System.Windows.Threading.DispatcherFrame();
            var t = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(sec) };
            t.Tick += (_, _) => { t.Stop(); frame.Continue = false; };
            t.Start();
            System.Windows.Threading.Dispatcher.PushFrame(frame);
        }
        var keep = Parked(new Border { Width = 4, Height = 4 });
        try
        {
            Run(3);   // settle after start-up
            double c0 = Cpu(); var w0 = DateTime.UtcNow; Run(Math.Max(10, seconds / 3.0)); double baseMs = Cpu() - c0, baseWall = (DateTime.UtcNow - w0).TotalMilliseconds;
            var ov = new CompanionOverlay(CompanionPrefs.Defaults());
            if (!onScreen) ov.Opacity = 1;   // parked off screen, still drawn every frame
            ov.Match(HudThemes.Glitch, 0.94, 1.1);   // Steven's look and size
            ov.ShowWanted(true);
            Run(2);
            c0 = Cpu(); w0 = DateTime.UtcNow; Run(seconds); double idleMs = Cpu() - c0, idleWall = (DateTime.UtcNow - w0).TotalMilliseconds;
            int n = 0;
            var react = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(6) };
            react.Tick += (_, _) => ov.Play(MainWindow.CompanionSample(CompanionKinds[n++ % CompanionKinds.Length], DateTime.UtcNow));
            ov.Play(MainWindow.CompanionSample(CompanionKinds[n++], DateTime.UtcNow)); react.Start();
            c0 = Cpu(); w0 = DateTime.UtcNow; Run(seconds); double reactMs = Cpu() - c0, reactWall = (DateTime.UtcNow - w0).TotalMilliseconds;
            react.Stop(); ov.Close();
            string Line(string what, double ms, double wall) => $"{what,-34} {ms / wall * 1000,7:0.0} ms CPU a second  ({ms / wall * 100,5:0.00}% of one core)";
            Console.WriteLine(Line("baseline (tracker idle, no companion)", baseMs, baseWall));
            Console.WriteLine(Line($"companion idling, {seconds} s", idleMs, idleWall));
            Console.WriteLine(Line($"companion reacting every 6 s, {seconds} s", reactMs, reactWall));
            Console.WriteLine($"companion's own share: idle {(idleMs / idleWall - baseMs / baseWall) * 100:0.00}% of one core, reacting {(reactMs / reactWall - baseMs / baseWall) * 100:0.00}%; window {ov.ActualWidth:0}x{ov.ActualHeight:0} px, no bitmaps (vector only)");
        }
        finally { keep.Close(); Motion.Still = still0; CompanionOverlay.InvisibleForTest = false; }
        return 0;
    }

    /// <summary>
    /// ui-shot --companion: the companion in every theme, at 1x and 2x (and 1.1, Steven's size): its moods, each reaction's
    /// key frame with its line, and each reaction's sequence frame by frame (Classic), over the game's scene; the reactions
    /// again in Korean and Chinese. spill.txt lists anything drawn past its window's edge (cut in game).
    /// </summary>
    public static int ShotCompanion(Settings settings, string dir)
    {
        Ensure();
        Directory.CreateDirectory(dir);
        var spill = Path.Combine(dir, "spill.txt");
        if (File.Exists(spill)) File.Delete(spill);
        bool still0 = Motion.Still;
        Motion.Still = true;
        var keep = Parked(new Border { Width = 4, Height = 4 });
        try
        {
            var now = DateTime.UtcNow;
            foreach (double scale in new[] { 1.0, 2.0 })
            {
                // the moods: idle (two points of its float), leaning in, dozing
                var moods = Sheet();
                var cols = new (string Name, CompanionSprite.Mood Mood, double At, int Variant)[] { ("idle", CompanionSprite.Mood.Idle, .25, 2), ("idle glance", CompanionSprite.Mood.Idle, .25, 0), ("idle smile", CompanionSprite.Mood.Idle, .5, 1), ("focused", CompanionSprite.Mood.Focused, .3, 2), ("sleepy", CompanionSprite.Mood.Sleepy, .35, 2) };
                for (int r = 0; r < CompanionThemes.Length; r++)
                    for (int c = 0; c < cols.Length; c++)
                    {
                        var face = new CompanionFace(CompanionThemes[r], 0.94, false, false);
                        face.Sprite.SetMood(cols[c].Mood);
                        face.Sprite.HoldForRender(null, Colors.White, 0, cols[c].At, cols[c].Variant);
                        Cell(moods, face, r, c, CompanionThemes[r] + " · " + cols[c].Name);
                    }
                SaveSheet(moods, Path.Combine(dir, $"companion-moods-{scale:0}x.png"), scale);

                // the palettes: every theme family, and each look of the families whose looks tint it apart
                var tints = Sheet(); int ti = 0;
                foreach (var l in HudLooks.All.Where(l => HudThemes.Themed(l.Key)))
                {
                    var face = new CompanionFace(l.Theme, 0.94, false, false, l.Key);
                    face.Sprite.HoldForRender(null, Colors.White, 0, .5, 1);
                    Cell(tints, face, ti / 8, ti % 8, l.Theme + " · " + l.Key); ti++;
                }
                foreach (var key in new[] { HudThemes.Classic })
                {
                    var face = new CompanionFace(key, 0.94, false, false);
                    face.Sprite.HoldForRender(null, Colors.White, 0, .5, 1);
                    Cell(tints, face, ti / 8, ti % 8, key); ti++;
                }
                SaveSheet(tints, Path.Combine(dir, $"companion-palettes-{scale:0}x.png"), scale);

                // every reaction's key frame with its line, theme by theme
                foreach (var theme in CompanionThemes)
                {
                    var sheet = Sheet();
                    int i = 0;
                    foreach (var k in CompanionKinds)
                    {
                        var face = KeyFace(theme, k, now, k is CompanionKind.Pvp or CompanionKind.Death);
                        var win = Parked(face);
                        SpillNote(dir, $"companion-{theme}-{k}", face);
                        win.Close();
                        Cell(sheet, KeyFace(theme, k, now, k is CompanionKind.Pvp or CompanionKind.Death), i / 2, i % 2, k.ToString());
                        i++;
                    }
                    SaveSheet(sheet, Path.Combine(dir, $"companion-{theme}-{scale:0}x.png"), scale);
                }
            }

            // Steven's size (1.1) and look (Terminal, the Glitch theme), and Classic at it
            foreach (var theme in new[] { HudThemes.Glitch, HudThemes.Classic })
            {
                var stv = Sheet(); int j = 0;
                foreach (var k in CompanionKinds)
                {
                    var face = KeyFace(theme, k, now, false);
                    face.LayoutTransform = new ScaleTransform(1.1, 1.1);
                    Cell(stv, face, j / 2, j % 2, k + " · 110%"); j++;
                }
                SaveSheet(stv, Path.Combine(dir, $"companion-{theme}-110.png"), 1);
            }

            // every frame of every reaction in every theme family inside the window (a cut there shows in game for a moment)
            foreach (var theme in CompanionThemes)
            {
                var face = new CompanionFace(theme, 0.94, false, false);
                var win = Parked(face);
                foreach (var k in CompanionKinds)
                {
                    var m = MainWindow.CompanionSample(k, now);
                    string first = ""; double firstAt = 0; int frames = 0;
                    for (double t = 0; t <= CompanionSprite.ReactMs; t += 40)
                    {
                        face.Sprite.HoldForRender(k, m.Tone, t, 0);
                        face.UpdateLayout();
                        var cut = Spill(face);
                        if (cut.Length == 0) continue;
                        if (frames++ == 0) { first = cut; firstAt = t; }
                    }
                    if (frames > 0) File.AppendAllText(spill, $"companion-frames-{theme}-{k}: {frames} frames, the first at {firstAt:0} ms {first}{Environment.NewLine}");
                }
                win.Close();
            }

            // beside the Buddy: each themed look on the farming sample with the companion in its palette beside it, at rest and tossing a coin
            ShotCompanionBeside(settings, dir);

            // each reaction frame by frame (Classic, 2x), the bubble's course alongside
            double[] at = { 0, 90, 180, 300, 420, 560, 720, 900, 1100, 1350, 1600, 1800 };
            foreach (var k in CompanionKinds)
            {
                var seq = Sheet();
                for (int f = 0; f < at.Length; f++)
                {
                    var m = MainWindow.CompanionSample(k, now);
                    var face = new CompanionFace(HudThemes.Classic, 0.94, false, false);
                    face.Sprite.HoldForRender(k, m.Tone, at[f], 0);
                    Cell(seq, face, f / 6, f % 6, $"{at[f]:0} ms");
                }
                SaveSheet(seq, Path.Combine(dir, $"companion-seq-{k}.png"), 2);
            }

            // the reactions' lines in Korean and Chinese
            InEachLanguage(code =>
            {
                if (code == "en") return;
                foreach (var theme in new[] { HudThemes.Classic, HudThemes.Wacky })
                {
                    var sheet = Sheet(); int n = 0;
                    foreach (var k in CompanionKinds) { Cell(sheet, KeyFace(theme, k, now, false), n / 2, n % 2, k.ToString()); n++; }
                    SaveSheet(sheet, Path.Combine(dir, $"companion-{theme}-{code}.png"), 1);
                }
            });
        }
        finally { keep.Close(); Motion.Still = still0; }
        Console.WriteLine($"wrote {Path.GetFullPath(dir)}" + (File.Exists(spill) ? " (spill.txt lists what was cut)" : " (nothing cut)"));
        return File.Exists(spill) ? 1 : 0;

        static void ShotCompanionBeside(Settings settings, string dir)
        {
            var w = new MainWindow(settings, watch: false) { WindowStartupLocation = WindowStartupLocation.Manual, Left = -12000, Top = -12000, ShowActivated = false };
            w.Show(); DoEvents();
            try
            {
                var v = w.HudValues.ToDictionary(kv => kv.Key, kv => kv.Value);
                foreach (var (k, x) in w.HudSample()) if (!v.ContainsKey(k) || k.StartsWith("pick") || k.StartsWith("top")) v[k] = x;
                v["show"] = "1";
                foreach (var url in v.Where(kv => kv.Key.EndsWith("Icon")).Select(kv => kv.Value).Distinct()) IconCache.Get(url);
                IconCache.Drain(TimeSpan.FromSeconds(10)); DoEvents();
                var now = DateTime.UtcNow;
                var looks = HudLooks.All.Where(l => HudThemes.Themed(l.Key)).ToList();
                for (int part = 0; part * 8 < looks.Count; part++)
                {
                    var sheet = Sheet(); int i = 0;
                    foreach (var l in looks.Skip(part * 8).Take(8))
                    {
                        var look = HudThemes.Make(l.Key, still: true);
                        if (look == null) continue;
                        look.SetOpacity(1);
                        look.Update(v);
                        var ins = look.Inset;
                        var buddy = new Border { Child = look.Root, Margin = new Thickness(-ins.Left, -ins.Top, -ins.Right, -ins.Bottom), VerticalAlignment = VerticalAlignment.Center };
                        var rest = new CompanionFace(l.Theme, 0.94, false, false, l.Key);
                        rest.Sprite.HoldForRender(null, Colors.White, 0, .5, 1);
                        var m = MainWindow.CompanionSample(CompanionKind.Rare, now);
                        var coin = new CompanionFace(l.Theme, 0.94, false, false, l.Key);
                        coin.Sprite.HoldForRender(CompanionKind.Rare, m.Tone, CompanionSprite.KeyMs(CompanionKind.Rare), .25);
                        var row = new StackPanel { Orientation = Orientation.Horizontal, Children = { buddy, rest, coin } };
                        Cell(sheet, row, i / 2, i % 2, l.Theme + " · " + l.Key); i++;
                    }
                    SaveSheet(sheet, Path.Combine(dir, $"companion-beside-{part + 1}.png"), 1);
                }
            }
            finally { w.Close(); }
        }

        static CompanionFace KeyFace(string theme, CompanionKind k, DateTime now, bool left)
        {
            var m = MainWindow.CompanionSample(k, now);
            var face = new CompanionFace(theme, 0.94, true, left);
            face.Sprite.HoldForRender(k, m.Tone, CompanionSprite.KeyMs(k), 0.25);
            face.HoldForRender(m, 1400);
            return face;
        }

        static Grid Sheet() => new() { Background = new SolidColorBrush(Color.FromRgb(0x0C, 0x0D, 0x11)) };

        static void Cell(Grid sheet, FrameworkElement face, int r, int c, string name)
        {
            while (sheet.RowDefinitions.Count <= r) sheet.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            while (sheet.ColumnDefinitions.Count <= c) sheet.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var label = new TextBlock { Text = name, Foreground = new SolidColorBrush(Color.FromRgb(0xAF, 0xB5, 0xC6)), FontSize = 11, Margin = new Thickness(8, 6, 8, 0) };
            var cell = new Border { Background = HudKit.GameScene(), Margin = new Thickness(6), Padding = new Thickness(14, 10, 14, 10), Child = new StackPanel { Children = { label, face } } };
            Grid.SetRow(cell, r); Grid.SetColumn(cell, c);
            sheet.Children.Add(cell);
        }

        static void SaveSheet(Grid sheet, string path, double scale)
        {
            var win = Parked(sheet);
            try
            {
                double w = sheet.ActualWidth, h = sheet.ActualHeight;
                var dv = new DrawingVisual();
                using (var dc = dv.RenderOpen())
                {
                    dc.PushTransform(new ScaleTransform(scale, scale));
                    dc.DrawRectangle(new VisualBrush(sheet) { ViewboxUnits = BrushMappingMode.Absolute, Viewbox = new Rect(0, 0, w, h), Stretch = Stretch.Fill }, null, new Rect(0, 0, w, h));
                }
                var rtb = new RenderTargetBitmap((int)Math.Ceiling(w * scale), (int)Math.Ceiling(h * scale), 96, 96, PixelFormats.Pbgra32);
                LangCheck.Scan(sheet, Path.GetFileName(path));
                rtb.Render(dv);
                var enc = new PngBitmapEncoder();
                enc.Frames.Add(BitmapFrame.Create(rtb));
                using var fs = File.Create(path);
                enc.Save(fs);
            }
            finally { win.Close(); }
        }
    }
}
