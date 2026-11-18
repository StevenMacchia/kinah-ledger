using System.IO;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using KinahLedger.Parser;

namespace KinahLedger.Ui;

public static partial class WpfApp
{
    /// <summary>The looks the share picture is checked and rendered in: Classic's standard panel and one look of each theme.</summary>
    static IEnumerable<string> MeterShareLooks(bool all) => all
        ? OptionsWindow.ParserLooksOf(HudThemes.Classic).Concat(HudLooks.All.Where(l => HudThemes.Themed(l.Key)).Select(l => l.Key))
        : OptionsWindow.PanelLooks.Concat(HudThemes.All.Where(t => t.Key != HudThemes.Classic).Select(t => HudThemes.Looks(t.Key)[0].Key));

    static MeterShare.Look ShareLookOf(string key, ParserMode mode)
    {
        var (radius, pad) = HudWindow.FrameShape(HudFields.Parse(null));
        return new MeterShare.Look(key, mode, false, radius, pad);
    }

    /// <summary>
    /// ui-shot DIR --parser share: the meter's share picture (Ui/MeterShare.cs) for a kill in every look, other names hidden
    /// (ui-meter-share-LOOK.png, 1200 px wide), then the standard look with names shown, a wipe, a run, a fight under way, and
    /// Steven's own width and look at the expanded meter. KL_THEME_LOOK=sticker,... renders those looks only.
    /// </summary>
    static void ShotMeterShare(string dir)
    {
        var only = (Environment.GetEnvironmentVariable("KL_THEME_LOOK") ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var now = new DateTime(2026, 11, 13, 21, 30, 0, DateTimeKind.Utc);
        var (past, live) = ParserDemo.Sample(now, live: true, damage: true);
        var (runPast, _) = ParserDemo.Sample(now, live: false, mode: ParserMode.Run);
        var kill = past.Last(e => e.Outcome == "kill");
        var wipe = past.Last(e => e.Outcome == "wipe");
        var run = runPast.LastOrDefault(e => e.ModeKind == ParserMode.Run);
        void Shot(string file, Encounter e, DateTime at, string look, bool hide)
        {
            try
            {
                var bmp = MeterShare.Render(e, at, ShareLookOf(look, e.ModeKind), hide);
                var enc = new PngBitmapEncoder();
                enc.Frames.Add(BitmapFrame.Create(bmp));
                using var fs = File.Create(Path.Combine(dir, file));
                enc.Save(fs);
            }
            catch (Exception ex) { Console.WriteLine($"{file}: {ex}"); }
        }
        foreach (var look in MeterShareLooks(all: true).Where(k => only.Length == 0 || only.Contains(k)))
            Shot($"ui-meter-share-{look}.png", kill, kill.End!.Value, look, true);
        if (only.Length > 0) return;
        Shot("ui-meter-share-standard-names.png", kill, kill.End!.Value, ParserPrefs.StandardLook, false);
        Shot("ui-meter-share-standard-wipe.png", wipe, wipe.End!.Value, ParserPrefs.StandardLook, true);
        Shot("ui-meter-share-neon-wipe.png", wipe, wipe.End!.Value, "neon", true);
        if (run != null) Shot("ui-meter-share-standard-run.png", run, run.End ?? run.LastAt, ParserPrefs.StandardLook, true);
        Shot("ui-meter-share-standard-live.png", live!, now, ParserPrefs.StandardLook, true);
        // Steven's own: 350 wide, the expanded meter, the widest meter
        double w0 = HudStackLook.FightWidth; bool exp0 = ParserRows.ExpandedPref;
        try
        {
            ParserRows.ExpandedPref = true; HudStackLook.FightWidth = ParserPrefs.DefaultWidth + 70;
            Shot("ui-meter-share-standard-expanded.png", kill, kill.End!.Value, ParserPrefs.StandardLook, true);
            Shot("ui-meter-share-neon-expanded.png", kill, kill.End!.Value, "neon", true);
            ParserRows.ExpandedPref = false; HudStackLook.FightWidth = HudStackLook.FightWidthMax;
            Shot("ui-meter-share-standard-widest.png", kill, kill.End!.Value, ParserPrefs.StandardLook, true);
        }
        finally { ParserRows.ExpandedPref = exp0; HudStackLook.FightWidth = w0; }
    }

    /// <summary>The words drawn under an element laid out off any window (Visibility, not IsVisible: no window shows it).</summary>
    static List<string> DrawnTexts(DependencyObject root)
    {
        var list = new List<string>();
        void Walk(DependencyObject d)
        {
            if (d is UIElement u && u.Visibility != Visibility.Visible) return;
            if (d is TextBlock t && BlockText(t) is { Length: > 0 } s) list.Add(s);
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(d); i++) Walk(VisualTreeHelper.GetChild(d, i));
        }
        Walk(root);
        return list;
    }

    /// <summary>
    /// options-test: the meter's share picture. For a kill in the standard look and one look of each theme: 1200 px wide, 630
    /// tall at least, the boss, how it ended, the day and kinahledger.com on it, the meter drawn (not blank); other names
    /// hidden by default (class and figures kept, yours kept), shown with the switch off. The overlay's Share shows with the
    /// pointer over it only while it takes clicks; the shortcut copies the fight on the meter. Settings has both rows.
    /// </summary>
    static void MeterShareChecks(MainWindow w, Action<bool, string> check)
    {
        var now = new DateTime(2026, 11, 13, 21, 30, 0, DateTimeKind.Utc);
        var (past, live) = ParserDemo.Sample(now, live: true, damage: true);
        var kill = past.Last(e => e.Outcome == "kill");
        var end = kill.End!.Value;
        var you = kill.Roster.First(f => f.You && !f.Other);
        // Share on the Kinah overlay too (Steven, 2026-11-18: "it should be on the overlay"): with the Parser inside it there was none
        {
            var hw = new HudWindow(null, HudLooks.Auto) { Left = -12000, Top = -12000, ShowActivated = false };
            int asked = 0; hw.ShareAsked += () => asked++;
            hw.Show(); DoEvents();
            hw.PressShareForTest(); hw.FlashShare("Picture copied", good: true); DoEvents();
            bool offscreenShut = !hw.ShareRowShownForTest;   // parked off every screen: the tab never opens (it would be pulled onto it)
            hw.Close();
            check(hw.HasShareForTest && asked == 1 && offscreenShut, $"meter share: the Kinah overlay has Share on its meter too, a tab of its own window that never opens off-screen (pressed {asked}, shut off-screen {offscreenShut})");
        }
        var others = kill.Roster.Where(f => !f.Other && !f.You && f.Name.Length > 0).Select(f => f.Name).ToList();
        var figure = new Regex(@"^[\d.,]+[KMB]?$");
        var bad = new List<string>();
        foreach (var look in MeterShareLooks(all: false))
        {
            var lk = ShareLookOf(look, kill.ModeKind);
            try
            {
                var bmp = MeterShare.Render(kill, end, lk, hideNames: true);
                var v = MeterShare.Laid(kill, end, lk, hideNames: true);
                var texts = DrawnTexts(v);
                string all = string.Join(" | ", texts);
                var miss = new List<string>();
                if (bmp.PixelWidth != MeterShare.PixelWidth || bmp.PixelHeight < 630 || bmp.PixelHeight > 2400 || bmp.PixelHeight % 2 != 0) miss.Add($"size {bmp.PixelWidth}×{bmp.PixelHeight}");
                // the outcome said once, by the look ("KILLED", "Killed in 2:19") or by the frame's pill when the look has none
                if (!all.Contains(L.T("Killed"), StringComparison.OrdinalIgnoreCase)) miss.Add("no outcome");
                // no repeats: the boss's name drawn once as a text of its own (the look's title, or the frame's for a look without one)
                int titles = texts.Count(t => t.Trim().Trim('[', ']', ' ').EndsWith(kill.Title, StringComparison.OrdinalIgnoreCase));
                if (titles > 1) miss.Add($"the boss's name drawn {titles} times");
                foreach (var want in new[] { kill.Title, L.Day(kill.Start.ToLocalTime()), "Kinah Ledger", "kinahledger.com", L.F("Member {0}", 2) })
                    if (!all.Contains(want, StringComparison.OrdinalIgnoreCase)) miss.Add($"no \"{want}\"");   // a look may write names in capitals (VHS)
                var leaked = others.Where(n => all.Contains(n, StringComparison.OrdinalIgnoreCase)).ToList();
                if (leaked.Count > 0) miss.Add("names shown: " + string.Join(", ", leaked));
                if (!all.Contains(ParserRows.NameOf(you), StringComparison.OrdinalIgnoreCase)) miss.Add($"your name {ParserRows.NameOf(you)} gone");
                // the meter drawn: its box has size, and the picture's middle is far from one flat colour
                var meter = Find(v, "share-meter");
                if (meter is not { ActualHeight: > 60 }) miss.Add("no meter");
                if (Colours(bmp) < 400) miss.Add($"looks blank ({Colours(bmp)} colours)");
                // their figures stay: the same figures with names shown
                var named = DrawnTexts(MeterShare.Laid(kill, end, lk, hideNames: false));
                var figsHidden = texts.Where(t => figure.IsMatch(t.Trim())).OrderBy(x => x).ToList();
                var figsNamed = named.Where(t => figure.IsMatch(t.Trim())).OrderBy(x => x).ToList();
                if (!figsHidden.SequenceEqual(figsNamed) || figsHidden.Count < 3) miss.Add($"figures differ with names hidden ({figsHidden.Count} vs {figsNamed.Count})");
                if (others.Count > 0 && !others.All(n => string.Join(" ", named).Contains(n, StringComparison.OrdinalIgnoreCase))) miss.Add("names not shown with the switch off");
                if (miss.Count > 0) bad.Add($"{look}: {string.Join(", ", miss)}");
            }
            catch (Exception ex) { bad.Add($"{look}: {ex.GetType().Name} {ex.Message}"); }
        }
        check(bad.Count == 0, $"meter share picture: 1200 px wide in the standard look and one of each theme, with the boss once, the outcome, the day, the mark and kinahledger.com, the meter drawn, other names hidden (figures kept), shown when the switch is off" + (bad.Count > 0 ? ": " + string.Join("; ", bad) : ""));
        // no cut-off label in any look's picture (names hidden or shown, a kill, a wipe, a fight under way), at Steven's width
        {
            var wipe = past.Last(e => e.Outcome == "wipe");
            var cut = new List<string>();
            foreach (var look in MeterShareLooks(all: true))
                foreach (var (e, at) in new[] { (kill, end), (wipe, wipe.End!.Value), (live!, now) })
                    foreach (var hide in new[] { true, false })
                    {
                        try
                        {
                            var v = MeterShare.Laid(e, at, ShareLookOf(look, e.ModeKind), hide);
                            foreach (var t in MeterShare.Trimmed(v)) cut.Add($"{look}{(e == kill ? "" : e == wipe ? " wipe" : " live")}{(hide ? "" : " named")}: \"{t}\"");
                        }
                        catch (Exception ex) { cut.Add($"{look}: {ex.GetType().Name} {ex.Message}"); }
                    }
            check(cut.Count == 0, $"meter share picture: nothing cut off in any look (no text ends in \"…\" or is trimmed)" + (cut.Count > 0 ? ": " + string.Join("; ", cut.Distinct().Take(12)) : ""));
        }
        // names shown by default (Steven, 2026-11-17: "it should say the members names"), under a key the old default's "on" can't set
        var oldSaved = System.Text.Json.JsonSerializer.Deserialize<ParserPrefs>("{\"ShareHideNames\": true}")!;
        check(!new ParserPrefs().ShareHideNames && !oldSaved.ShareHideNames && new ParserPrefs().ShareHotkey == ParserPrefs.DefaultHotkey && Platform.Hotkey.Parse(new ParserPrefs().ShareHotkey) == null,
            "meter share: other names shown by default, an old saved \"hidden\" left behind, its shortcut Off by default");
        check(MeterShare.FileName(kill) is var fn && fn.StartsWith("kinah-ledger-") && fn.EndsWith(".png") && fn.IndexOfAny(Path.GetInvalidFileNameChars()) < 0, $"meter share: a file name safe to save ({MeterShare.FileName(kill)})");

        MeterShareOverlayChecks(check);   // the overlay's Share (KL_OPTIONS_ONLY=share runs it alone)

        // the shortcut and the buttons copy the fight on the meter (a test's sink, never the clipboard or Pictures)
        var got = new List<(int W, int H, string File)>();
        MainWindow.ShareSinkForTest = (b, f) => got.Add((b.PixelWidth, b.PixelHeight, f));
        bool hide0 = w.ShareHideNamesForTest;
        try
        {
            w.SetMeterOnOverlayForTest(kill);
            bool ok = w.ShareMeterShortcutForTest();
            var ready = Encounter.Ready(KinahLedger.Packets.ContentKind.Expedition, "Ferocious Horn Den", "", now);
            bool refused = !w.ShareMeterForTest(ready);
            check(ok && got.Count == 1 && got[0].W == MeterShare.PixelWidth && got[0].File == MeterShare.FileName(kill) && refused,
                $"meter share: the shortcut copies the fight on the meter ({got.Count} pictures{(got.Count > 0 ? $", {got[0].W}×{got[0].H} {got[0].File}" : "")}); a ready card has nothing to share ({refused})");
        }
        finally { MainWindow.ShareSinkForTest = null; w.SetMeterOnOverlayForTest(null); w.ShareHideNamesForTest = hide0; }

        // the Parser page's Copy picture, on a kept fight opened (the meter is in its ▾: Ui/WpfApp.ParserShare.cs checks the rest)
        {
            w.ShowParserDemo("detail"); DoEvents();
            var btn = Find(w.ParserPage, "parser-copy-picture-hero") as Button;
            check(btn is { IsEnabled: true } && Find(w.ParserPage, "parser-share-more") is Button, "meter share: Copy picture and its ▾ on the Parser page for the fight opened");
        }

        static FrameworkElement? Find(DependencyObject d, string tag)
        {
            if (d is FrameworkElement fe && fe.Tag as string == tag) return fe;
            if (d is Visual) for (int i = 0; i < VisualTreeHelper.GetChildrenCount(d); i++) if (Find(VisualTreeHelper.GetChild(d, i), tag) is { } x) return x;
            foreach (var ch in LogicalTreeHelper.GetChildren(d).OfType<DependencyObject>()) if (Find(ch, tag) is { } y) return y;
            return null;
        }
        static int Colours(BitmapSource b)
        {
            var px = new byte[b.PixelWidth * b.PixelHeight * 4];
            b.CopyPixels(px, b.PixelWidth * 4, 0);
            var set = new HashSet<int>();
            for (int i = 0; i < px.Length; i += 4 * 7) set.Add(px[i] | px[i + 1] << 8 | px[i + 2] << 16);
            return set.Count;
        }
    }

    /// <summary>options-test: the Parser overlay's Share under the meter (shown, reachable, its fill). <c>KL_OPTIONS_ONLY=share</c> runs it alone.</summary>
    static void MeterShareOverlayChecks(Action<bool, string> check)
    {
        var now = new DateTime(2026, 11, 13, 21, 30, 0, DateTimeKind.Utc);
        var kill = ParserDemo.Sample(now, live: true, damage: true).Past.Last(e => e.Outcome == "kill");
        var end = kill.End!.Value;
        // the overlay's Share: with the pointer over it, clicks taken or going through (with them going through it shows with the
        // grab handle, the pointer over the overlay; Steven, 2026-11-17: "it needs to be shareable"); never on a ready card
        var po = new ParserOverlay { Left = -12000, Top = -12000, Mode = kill.ModeKind };
        try
        {
            po.Draw(kill, end); po.Show(); DoEvents();
            po.HoverForTest(false);
            bool away = !po.ShareShownForTest;
            po.HoverForTest(true);
            bool over = po.ShareShownForTest;
            po.SetClickThrough(true); po.HoverForTest(false);
            bool throughAway = !po.ShareShownForTest;
            po.ShowGrabForTest();
            bool through = po.ShareShownForTest && throughAway;
            po.SetClickThrough(false); po.ShareShown = false; po.HoverForTest(true);
            bool ready = po.ShareShownForTest;
            po.ShareShown = true;
            po.Flash(L.T("Picture copied"), good: true);
            bool flashed = po.HintTextForTest == L.T("Picture copied") && !po.ShareShownForTest;
            check(away && over && through && !ready && flashed,
                $"meter share: the overlay's Share shows with the pointer over it ({over}, away {!away}), with clicks going through too once the pointer is over it ({through}), not on a ready card ({ready}); \"Picture copied\" in the hint's place after ({flashed})");
            // Share can be reached: from the meter down to it and over it, every pixel holds the pointer, so the hover never ends on
            // the way (Steven, 2026-11-17: the layered window's clear gap let the pointer through to the game and Share went away).
            // At 100% and at Steven's 110% (hud.json Scale 1.1); the fill is gone away from the pointer and while clicks go through.
            po.FlashDoneForTest();
            var holes = new List<string>();
            foreach (var scale in new[] { 1.0, 1.1 })
            {
                po.SetScale(scale); po.HoverForTest(true); DoEvents();
                if (po.ShareReachHoleForTest() is { } hole) holes.Add($"{scale:0.0}: {hole}");
            }
            po.HoverForTest(false); bool fillAway = po.HoverFillForTest;
            po.SetClickThrough(true); po.HoverForTest(true); bool fillThrough = po.HoverFillForTest;
            po.SetClickThrough(false); po.SetScale(1);
            check(holes.Count == 0 && !fillAway && !fillThrough,
                $"meter share: the overlay's Share sits inside the area that keeps it shown, no clear gap from the meter to it (at 100% and 110%{(holes.Count > 0 ? ": " + string.Join("; ", holes) : "")}); the fill is off away from the pointer ({!fillAway}) and while clicks go through ({!fillThrough})");
        }
        finally { po.Close(); }
    }

    /// <summary>options-test: Settings › In game › Parser has Hide other names (off by default since 4c3c730) and the Share picture shortcut (Off, recorded as the others are).</summary>
    static void MeterShareOptionChecks(OptionsWindow o, Action<bool, string> check)
    {
        {
            try
            {
                var chk = o.ParserHideNamesForTest;
                string key0 = o.ShareKeyForTest;
                var refuse = o.RecordShareKeyForTest(ModifierKeys.Control, Key.C);
                var take = o.RecordShareKeyForTest(ModifierKeys.Control | ModifierKeys.Shift, Key.F9);
                string key1 = o.ShareKeyForTest;
                // off by default since PR 342 ("Meter share: the members' names shown by default")
                check(chk.IsChecked == false && key0 == ParserPrefs.DefaultHotkey && refuse != null && take == null && key1 == "Ctrl+Shift+F9" && o.ParserDirty,
                    $"meter share: Settings › In game › Parser has Hide other names (off by default: {chk.IsChecked}) and the Share picture shortcut (Off: {key0}; Ctrl+C refused: {refuse != null}; Ctrl+Shift+F9 taken: {key1}, Save lights up)");
            }
            finally { o.ReloadParserForTest(); }
        }

    }
}
