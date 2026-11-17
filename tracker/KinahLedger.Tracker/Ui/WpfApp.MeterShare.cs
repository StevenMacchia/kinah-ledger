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
        ? new[] { ParserPrefs.StandardLook }.Concat(HudLooks.FightLooks).Concat(HudLooks.All.Where(l => HudThemes.Themed(l.Key)).Select(l => l.Key))
        : new[] { ParserPrefs.StandardLook }.Concat(new[] { HudThemes.Wacky, HudThemes.Tactical, HudThemes.Glitch }.Select(t => HudThemes.Looks(t)[0].Key));

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
                foreach (var want in new[] { kill.Title, L.F("Killed in {0}", BossParser.Clock(kill.Duration(end))), L.Day(kill.Start.ToLocalTime()), "Kinah Ledger", "kinahledger.com", L.F("Member {0}", 2) })
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
        check(bad.Count == 0, $"meter share picture: 1200 px wide in the standard look and one of each theme, with the boss, the outcome, the day, the mark and kinahledger.com, the meter drawn, other names hidden (figures kept), shown when the switch is off" + (bad.Count > 0 ? ": " + string.Join("; ", bad) : ""));
        check(new ParserPrefs().ShareHideNames && new ParserPrefs().ShareHotkey == ParserPrefs.DefaultHotkey && Platform.Hotkey.Parse(new ParserPrefs().ShareHotkey) == null,
            "meter share: other names hidden by default, its shortcut Off by default");
        check(MeterShare.FileName(kill) is var fn && fn.StartsWith("kinah-ledger-") && fn.EndsWith(".png") && fn.IndexOfAny(Path.GetInvalidFileNameChars()) < 0, $"meter share: a file name safe to save ({MeterShare.FileName(kill)})");

        // the overlay's Share: with the pointer over it while it takes clicks; never while clicks go through, nor on a ready card
        var po = new ParserOverlay { Left = -12000, Top = -12000, Mode = kill.ModeKind };
        try
        {
            po.Draw(kill, end); po.Show(); DoEvents();
            po.HoverForTest(false);
            bool away = !po.ShareShownForTest;
            po.HoverForTest(true);
            bool over = po.ShareShownForTest;
            po.SetClickThrough(true); po.HoverForTest(true);
            bool through = po.ShareShownForTest;
            po.SetClickThrough(false); po.ShareShown = false; po.HoverForTest(true);
            bool ready = po.ShareShownForTest;
            po.ShareShown = true;
            po.Flash(L.T("Picture copied"), good: true);
            bool flashed = po.HintTextForTest == L.T("Picture copied") && !po.ShareShownForTest;
            check(away && over && !through && !ready && flashed,
                $"meter share: the overlay's Share shows with the pointer over it ({over}, away {!away}), not while clicks go through ({through}), not on a ready card ({ready}); \"Picture copied\" in the hint's place after ({flashed})");
        }
        finally { po.Close(); }

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

        // the Parser page's Share, on a kept fight opened
        {
            w.ShowParserDemo("detail"); DoEvents();
            var btn = Find(w.ParserPage, "meter-share") as Button;
            check(btn is { IsEnabled: true }, "meter share: Share on the Parser page for the fight opened");
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

    /// <summary>options-test: Settings › In game › Parser has Hide other names (on) and the Share picture shortcut (Off, recorded as the others are).</summary>
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
                check(chk.IsChecked == true && key0 == ParserPrefs.DefaultHotkey && refuse != null && take == null && key1 == "Ctrl+Shift+F9" && o.ParserDirty,
                    $"meter share: Settings › In game › Parser has Hide other names (on: {chk.IsChecked}) and the Share picture shortcut (Off: {key0}; Ctrl+C refused: {refuse != null}; Ctrl+Shift+F9 taken: {key1}, Save lights up)");
            }
            finally { o.ReloadParserForTest(); }
        }

    }
}
