using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using KinahLedger.Packets;
using KinahLedger.Session;

namespace KinahLedger.Ui;

public static partial class WpfApp
{
    static readonly string[] ApplicantThemes = { HudThemes.Classic, HudThemes.Tactical, HudThemes.Wacky, HudThemes.Glitch, HudThemes.Modern, HudThemes.Atreia, HudThemes.Esports, HudThemes.Elements, HudThemes.Overdrive };
    /// <summary>The families whose looks each have a face of their own (Ui/ApplicantsFace.Themes.cs), and those looks.</summary>
    static readonly string[] ApplicantLookThemes = { HudThemes.Atreia, HudThemes.Esports, HudThemes.Elements, HudThemes.Overdrive };
    static IEnumerable<(string Theme, string Look)> ApplicantLooks() => ApplicantLookThemes.SelectMany(t => HudThemes.Looks(t).Select(l => (t, l.Key)));
    static readonly int[] ApplicantCounts = { 1, 4, 10 };
    static readonly string[] ApplicantLangs = { "en", "ko", "zh-TW" };

    /// <summary>Runs <paramref name="body"/> in each language (en, ko, zh-TW), the language and cultures put back after.</summary>
    static void InEachLanguage(Action<string> body)
    {
        var culture = CultureInfo.CurrentCulture; var ui = CultureInfo.CurrentUICulture;
        var defCulture = CultureInfo.DefaultThreadCurrentCulture; var defUi = CultureInfo.DefaultThreadCurrentUICulture;
        var lang0 = L.Code;
        try { foreach (var code in ApplicantLangs) { L.Init(code); body(code); } }
        finally
        {
            L.Init(lang0);
            CultureInfo.CurrentCulture = culture; CultureInfo.CurrentUICulture = ui;
            CultureInfo.DefaultThreadCurrentCulture = defCulture; CultureInfo.DefaultThreadCurrentUICulture = defUi;
        }
    }

    /// <summary>A face in a parked window of its own (laid out as on screen, never seen): the window, to close after.</summary>
    static Window Parked(FrameworkElement face)
    {
        var win = new Window
        {
            Left = -12000, Top = -12000, ShowActivated = false, ShowInTaskbar = false, WindowStyle = WindowStyle.None, AllowsTransparency = true, Background = Brushes.Transparent,
            SizeToContent = SizeToContent.WidthAndHeight, WindowStartupLocation = WindowStartupLocation.Manual, Content = face, Opacity = 0,
        };
        win.Show(); DoEvents();
        return win;
    }

    /// <summary>
    /// options-test, KL_OPTIONS_ONLY=applicants (Steven, 2026-11-18): the Applicants window. On by default and in the Overlays
    /// menu; hidden with nobody waiting, shown with three, a row gone when one joins, folded away when every request goes; its
    /// look follows the overlay's theme; a window of its own that never takes focus, click-through only as a setting; Reset
    /// position; its Settings page. Every theme with 1, 4 and 10 applicants in English, Korean and Chinese: every row there,
    /// nothing cut or spilling, every word translated, and nothing saying where the figures come from.
    /// </summary>
    static void ApplicantsChecks(MainWindow w, Action<bool, string> check)
    {
        static void Click(System.Windows.Controls.Primitives.ButtonBase b) => b.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
        static void Flip(CheckBox c) { c.IsChecked = c.IsChecked != true; Click(c); }
        ApplicantsOverlay.InvisibleForTest = true;
        var prefs0 = w.ApplicantPrefsForTest;
        w.ApplicantPrefsForTest = ApplicantPrefs.Defaults();   // never saved
        var board = new PartyApplicants();
        w.ApplicantsBoardForTest = board;
        try
        {
            check(ApplicantPrefs.Defaults().On && !ApplicantPrefs.Defaults().ClickThrough && w.OverlaysForTest().Any(o => o.Name == "Applicants" && o.On),
                "applicants: on by default, click-through off, and in the header's Overlays menu");

            var now = DateTime.UtcNow;
            PartyApplication A(double ago, PartyApplication.Kind k, string key, string name = "", string cls = "", long cp = 0) =>
                new(now.AddSeconds(-ago), k, key, name, cls, 45, 2_651, cp, "Zikel", null);
            w.ApplicantsTickForTest(now); DoEvents();
            bool noneShown = w.ApplicantsOverlayForTest is not { IsVisible: true };
            board.Take(A(40, PartyApplication.Kind.Applied, "1", "Kyralis", "Gladiator", 133_774));
            board.Take(A(35, PartyApplication.Kind.Applied, "2", "Varrenthos", "Gladiator", 128_195));
            board.Take(A(33, PartyApplication.Kind.Applied, "3", "Embervale", "Sorcerer", 125_846));
            w.ApplicantsTickForTest(now); DoEvents();
            var ov = w.ApplicantsOverlayForTest;
            int RowsShown() => ov?.FaceForTest is { } f ? RowCount(f) : -1;
            bool three = ov is { IsVisible: true } && RowsShown() == 3;
            var names = ov?.FaceForTest is { } f3 ? RowNames(f3) : new List<string>();
            bool order = names.SequenceEqual(new[] { "Kyralis", "Varrenthos", "Embervale" });
            board.Take(A(10, PartyApplication.Kind.Accepted, "2", "Varrenthos"));
            w.ApplicantsTickForTest(now); DoEvents();
            bool two = RowsShown() == 2;
            check(noneShown && three && order && two, $"applicants: hidden with nobody waiting ({noneShown}), three shown oldest first ({three}, {order}), a row gone when one joins ({two})");

            // the theme in use: the window's face is the theme's
            var seen = new List<string>();
            foreach (var theme in ApplicantThemes)
            {
                if (ov != null) ov.Theme = () => theme;
                w.ApplicantsTickForTest(now); DoEvents();
                seen.Add(ov?.FaceForTest?.Tag as string ?? "");
            }
            check(seen.SequenceEqual(ApplicantThemes.Select(t => "applicants:" + t)), "applicants: the window takes the overlay's theme (" + string.Join(", ", seen) + ")");

            // ...and the Buddy's look in it, where the theme's looks each have their own face; the pop-out the same
            var lookSeen = new List<string>();
            foreach (var (theme, look) in ApplicantLooks())
            {
                if (ov != null) { ov.Theme = () => theme; ov.Look = () => look; }
                w.ApplicantsTickForTest(now); DoEvents();
                if (ov?.FaceForTest is not { } f || ApplicantsFace.LookOf(f) != look) lookSeen.Add($"{look}: {(ov?.FaceForTest is { } g ? ApplicantsFace.LookOf(g) ?? "none" : "no face")}");
            }
            var other = ApplicantsFace.Build(HudThemes.Elements, ApplicantsFace.Sample(2, now), now, .94, HudThemes.Versus);   // a look of another theme: the theme's first
            if (ApplicantsFace.LookOf(other) != HudThemes.Looks(HudThemes.Elements)[0].Key) lookSeen.Add("elements with versus: " + ApplicantsFace.LookOf(other));
            var pop = new ApplicantsPopOut(w, new PopOutPrefs.Spot(), () => { });
            pop.Draw(ApplicantsFace.Sample(2, now), now, HudThemes.Overdrive, HudThemes.Manga);
            if (pop.FaceForTest is not FrameworkElement pf || ApplicantsFace.LookOf(pf) != HudThemes.Manga) lookSeen.Add("pop-out: not manga");
            pop.Close();
            if (ov != null) { ov.Theme = () => HudThemes.Classic; ov.Look = () => null; }
            check(lookSeen.Count == 0, $"applicants: the window and its pop-out take the Buddy's look in Atreia, Esports, Elements and Overdrive ({ApplicantLooks().Count()} looks)" + (lookSeen.Count > 0 ? ": " + string.Join("; ", lookSeen) : ""));

            // someone new: their row's arrival beat, held mid-way and at its end (a render's frames): something on its stage
            // mid-way, and at the end nothing left (no stage, the row's cells at rest, the rows unclipped); nothing with motion off
            var beat = new List<string>();
            double? frozen0 = Motion.FrozenAt;
            bool still0 = Motion.Still;
            try
            {
                foreach (var (theme, look) in ApplicantLooks().Prepend((HudThemes.Classic, "")))
                {
                    double ms = 0; int mid = -1;
                    bool restful = true;
                    foreach (var at in new[] { .45, 1.0 })
                    {
                        var face = ApplicantsFace.Build(theme, ApplicantsFace.Sample(4, now), now, .94, look.Length > 0 ? look : null);
                        var win = Parked(face);
                        Motion.FrozenAt = at;
                        ms = ApplicantsFace.Arrive(face);
                        DoEvents();
                        int drawn = ApplicantsFace.BeatPartsForTest(face);
                        if (at < 1) mid = drawn;
                        else restful = drawn == 0 && ApplicantsFace.MovedForTest(face) == 0;
                        win.Close();
                    }
                    Motion.FrozenAt = null; Motion.Still = true;
                    var still = ApplicantsFace.Build(theme, ApplicantsFace.Sample(4, now), now, .94, look.Length > 0 ? look : null);
                    var sw = Parked(still);
                    double none = ApplicantsFace.Arrive(still);
                    sw.Close();
                    Motion.Still = still0;
                    if (ms <= 0 || ms > 1200 || (look.Length > 0 && mid <= 0) || !restful || none != 0) beat.Add($"{(look.Length > 0 ? look : theme)}: {ms} ms, {mid} parts mid-way, at rest {restful}, with motion off {none}");
                }
            }
            finally { Motion.FrozenAt = frozen0; Motion.Still = still0; }
            check(beat.Count == 0, "applicants: a new applicant's row comes in with its look's short beat (at most 1.2 s), leaves nothing behind, and none with motion off" + (beat.Count > 0 ? ": " + string.Join("; ", beat) : ""));

            // a window of its own over the game, never focused, click-through only as a setting, its grip grabbable then
            long ex0 = ov?.ExStyleForTest ?? 0;
            w.SetApplicantsClickThrough(true); DoEvents();
            long ex1 = ov?.ExStyleForTest ?? 0;
            ov?.GripForTest(true, true); DoEvents();
            long exGrip = ov?.ExStyleForTest ?? 0; bool grip = ov?.GripShownForTest == true;
            ov?.GripForTest(false, false);
            w.SetApplicantsClickThrough(false); DoEvents();
            bool style = (ex0 & Native.ExNoActivate) != 0 && (ex0 & Native.ExToolWindow) != 0 && (ex0 & Native.ExTransparent) == 0 && (ex1 & Native.ExTransparent) != 0 && (exGrip & Native.ExTransparent) == 0 && grip;
            check(ov != null && ov.Topmost && !ov.ShowActivated && ov.Owner == null && style, $"applicants: its own window, on top, never takes focus, out of Alt+Tab; click-through only as a setting, its grip grabbable then ({style})");
            w.ApplicantPrefsForTest.X = 300; w.ApplicantPrefsForTest.Y = 200;
            w.ResetApplicantsPosition();
            check(w.ApplicantPrefsForTest.X == null && w.ApplicantPrefsForTest.Y == null, "applicants: Reset position forgets the spot");

            board.Take(A(1, PartyApplication.Kind.Cleared, ""));
            w.ApplicantsTickForTest(now); DoEvents();
            check(ov != null && !ov.IsVisible, "applicants: folded away when every request goes");

            // the Settings page: the overlays' fourth, its switch turns the window off and on, Reset position is there
            board.Take(A(5, PartyApplication.Kind.Applied, "4", "Halvric", "Cleric", 110_552));
            var o = new OptionsWindow(w, OptionsWindow.Page.Applicants) { WindowStartupLocation = WindowStartupLocation.Manual, Left = -12000, Top = -12000, ShowActivated = false };
            o.Show(); DoEvents();
            bool page = o.PageShownForTest == OptionsWindow.Page.Applicants && OptionsWindow.OverlayPages.Any(p => p.Page == OptionsWindow.Page.Applicants) && o.ApplicantsChk?.IsChecked == true && o.ApplicantsResetBtn != null;
            bool fits = o.Scroller.ExtentHeight <= o.Scroller.ViewportHeight + 1;
            if (o.ApplicantsChk != null) Flip(o.ApplicantsChk);
            DoEvents();
            bool off = !w.ApplicantsOn && w.ApplicantsOverlayForTest == null;
            if (o.ApplicantsChk != null) Flip(o.ApplicantsChk);
            DoEvents();
            bool back = w.ApplicantsOn && w.ApplicantsOverlayForTest is { IsVisible: true };
            var pageWords = w.SourceWordsShown(o.PageApplicants);
            o.Close(); DoEvents();
            check(page && fits && off && back && pageWords.Count == 0, $"applicants: Settings › Applicants, with its switch (off closes the window, on brings it back), Reset position, fitting the window ({page}, {fits}, {off}, {back}{(pageWords.Count > 0 ? "; says " + string.Join(" | ", pageWords) : "")})");

            // every theme, 1, 4 and 10 waiting, three languages: every row, nothing cut or spilling, every word translated
            var problems = new List<string>(); var missing = new HashSet<string>(); var sources = new List<string>();
            InEachLanguage(code =>
            {
                L.Misses = code == "en" ? null : new HashSet<string>();
                foreach (var (theme, look) in ApplicantThemes.Select(t => (t, (string?)null)).Concat(ApplicantLooks().Select(x => (x.Theme, (string?)x.Look))))
                    foreach (int n in ApplicantCounts)
                    {
                        var items = ApplicantsFace.Sample(n, now);
                        var face = ApplicantsFace.Build(theme, items, now, 0.94, look);
                        var win = Parked(face);
                        var tag = look ?? theme;
                        if (RowCount(face) != n) problems.Add($"{tag} {n} {code}: {RowCount(face)} rows");
                        foreach (var c in CutOffs(face, items.Select(a => a.Name).ToList())) problems.Add($"{tag} {n} {code}: {c}");
                        if (Spill(face) is { Length: > 0 } sp) problems.Add($"{tag} {n} {code}: {sp}");
                        sources.AddRange(w.SourceWordsShown(face).Select(s => $"{tag} {code}: {s}"));
                        win.Close();
                    }
                if (L.Misses != null) lock (L.Misses) foreach (var m in L.Misses) missing.Add($"{code}: {m}");
                L.Misses = null;
            });
            check(problems.Count == 0, $"applicants: every theme with 1, 4 and 10 waiting, in English, Korean and Chinese: every row, nothing cut or spilling" + (problems.Count > 0 ? ": " + string.Join("; ", problems.Take(12)) : ""));
            check(missing.Count == 0, "applicants: every word in Korean and Chinese" + (missing.Count > 0 ? ": " + string.Join("; ", missing.Take(12)) : ""));
            check(sources.Count == 0, "applicants: nothing says where the figures come from" + (sources.Count > 0 ? ": " + string.Join("; ", sources.Take(6)) : ""));
        }
        finally
        {
            w.SetApplicantsOverlay(false, save: false);
            w.ApplicantsBoardForTest = null;
            w.ApplicantPrefsForTest = prefs0;
        }
    }

    /// <summary>The names on a face's rows, top to bottom (each row's name, its first words).</summary>
    static List<string> RowNames(DependencyObject face)
    {
        Grid? Find(DependencyObject d)
        {
            if (d is Grid { Tag: "applicant-rows" } g) return g;
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(d); i++) if (Find(VisualTreeHelper.GetChild(d, i)) is { } x) return x;
            return null;
        }
        if (Find(face) is not { } rows) return new();
        return rows.Children.OfType<TextBlock>().Where(t => Grid.GetColumn(t) == 1).OrderBy(Grid.GetRow)
            .Select(t => t.Inlines.OfType<System.Windows.Documents.Run>().FirstOrDefault()?.Text ?? t.Text).ToList();
    }

    /// <summary>The applicant rows a face draws (its row grid's rows).</summary>
    static int RowCount(DependencyObject face)
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(face); i++)
        {
            var ch = VisualTreeHelper.GetChild(face, i);
            if (ch is Grid g && g.Tag as string == "applicant-rows") return g.RowDefinitions.Count;
            int n = RowCount(ch);
            if (n >= 0) return n;
        }
        if (face is FrameworkElement fe && LogicalTreeHelper.GetChildren(fe).OfType<DependencyObject>().FirstOrDefault(c => c is Grid { Tag: "applicant-rows" }) is Grid lg) return lg.RowDefinitions.Count;
        return -1;
    }

    /// <summary>
    /// ui-shot --applicants: the Applicants window in every theme with 1, 4 and 10 waiting, over the game's scene, one sheet per
    /// language (applicants-en.png, applicants-ko.png, applicants-zh-TW.png) and each face on its own
    /// (applicants-&lt;theme&gt;-&lt;n&gt;-&lt;lang&gt;.png); spill.txt names anything cut. Nothing is read, saved or sent.
    /// </summary>
    public static int ShotApplicants(Settings settings, string dir)
    {
        Ensure();
        Directory.CreateDirectory(dir);
        var spill = Path.Combine(dir, "spill.txt");
        if (File.Exists(spill)) File.Delete(spill);
        var now = DateTime.UtcNow;
        // a parked window held open the whole time: closing the last window would end the application between two faces
        var keep = Parked(new Border { Width = 4, Height = 4 });
        // KL_APPLICANTS_PART=sheets|looks|arrive renders that part only (all three by default)
        var part = Environment.GetEnvironmentVariable("KL_APPLICANTS_PART") ?? "";
        try
        {
            if (part is "" or "sheets") Sheets();
            if (part is "" or "looks") Looks();
            if (part is "" or "arrive") Arrivals();
        }
        finally { keep.Close(); }
        Console.WriteLine($"wrote {Path.GetFullPath(dir)}" + (File.Exists(spill) ? " (spill.txt lists what was cut)" : " (nothing cut)"));
        return File.Exists(spill) ? 1 : 0;

        // every look of the themes whose looks have faces of their own, 1, 4 and 10 waiting (applicants-look-LOOK-N-LANG.png),
        // and one sheet of them all at 4 (applicants-looks-LANG.png), a family to a row
        void Looks() => InEachLanguage(code =>
        {
            var sheet = new Grid { Background = new SolidColorBrush(Color.FromRgb(0x0C, 0x0D, 0x11)) };
            var families = ApplicantLookThemes.ToList();
            foreach (var _ in families) sheet.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            for (int c = 0; c < 4; c++) sheet.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            foreach (var (theme, look) in ApplicantLooks())
            {
                foreach (int n in ApplicantCounts)
                {
                    var items = ApplicantsFace.Sample(n, now);
                    var alone = ApplicantsFace.Build(theme, items, now, 0.94, look);
                    var win = Parked(alone);
                    var file = $"applicants-look-{look}-{n}-{code}.png";
                    SavePart(alone, Path.Combine(dir, file), HudKit.GameScene());
                    SpillNote(dir, file, alone);
                    win.Close();
                }
                var cell = new Border { Background = HudKit.GameScene(), Margin = new Thickness(8), Padding = new Thickness(16), VerticalAlignment = VerticalAlignment.Top, Child = ApplicantsFace.Build(theme, ApplicantsFace.Sample(4, now), now, 0.94, look) };
                Grid.SetRow(cell, families.IndexOf(theme)); Grid.SetColumn(cell, HudThemes.Looks(theme).Select(l => l.Key).ToList().IndexOf(look));
                sheet.Children.Add(cell);
            }
            var sw = Parked(sheet);
            Save(sheet, Path.Combine(dir, $"applicants-looks-{code}.png"), 0);
            sw.Close();
        });

        // the arrival beat of every look (and Classic's slide) held at points of its course, 4 waiting, in English:
        // applicants-arrive-LOOK-15/40/75.png, or with KL_MOMENT_SEQ=N every 1/N as ui-theme-applicants-LOOK-arrive-sNN.png
        // (tests/ui/clips.py makes those clips)
        void Arrivals()
        {
            var frames = MomentFrames();
            double? frozen0 = Motion.FrozenAt;
            try
            {
                foreach (var (theme, look) in ApplicantLooks().Prepend((HudThemes.Classic, HudThemes.Classic)))
                    for (int i = 0; i < frames.Length; i++)
                    {
                        var face = ApplicantsFace.Build(theme, ApplicantsFace.Sample(4, now), now, 0.94, look);
                        var win = Parked(face);
                        Motion.FrozenAt = frames[i];
                        ApplicantsFace.Arrive(face);
                        DoEvents();
                        var file = frames.Length > 3 ? $"ui-theme-applicants-{look}-arrive-s{i:00}.png" : $"applicants-arrive-{look}-{Math.Round(frames[i] * 100)}.png";
                        SavePart(face, Path.Combine(dir, file), HudKit.GameScene());
                        SpillNote(dir, file, face);
                        Motion.FrozenAt = frozen0;
                        win.Close();
                    }
            }
            finally { Motion.FrozenAt = frozen0; }
        }

        void Sheets() => InEachLanguage(code =>
        {
            var sheet = new Grid { Background = new SolidColorBrush(Color.FromRgb(0x0C, 0x0D, 0x11)) };
            sheet.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            foreach (var _ in ApplicantCounts) sheet.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            sheet.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            for (int c = 0; c < ApplicantCounts.Length; c++)
            {
                var h = new TextBlock { Text = $"{ApplicantCounts[c]} waiting", Foreground = Brushes.White, FontSize = 14, FontWeight = FontWeights.Bold, Margin = new Thickness(16, 12, 16, 4) };
                Grid.SetColumn(h, c + 1); sheet.Children.Add(h);
            }
            for (int r = 0; r < ApplicantThemes.Length; r++)
            {
                var theme = ApplicantThemes[r];
                sheet.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                var label = new TextBlock { Text = theme, Foreground = Brushes.White, FontSize = 14, FontWeight = FontWeights.Bold, Margin = new Thickness(12, 16, 8, 0), VerticalAlignment = VerticalAlignment.Top };
                Grid.SetRow(label, r + 1); sheet.Children.Add(label);
                for (int c = 0; c < ApplicantCounts.Length; c++)
                {
                    int n = ApplicantCounts[c];
                    var items = ApplicantsFace.Sample(n, now);
                    // each face alone, as its window draws it, then again on the sheet over the game's scene
                    var alone = ApplicantsFace.Build(theme, items, now, 0.94);
                    var win = Parked(alone);
                    var file = $"applicants-{theme}-{n}-{code}.png";
                    SavePart(alone, Path.Combine(dir, file), HudKit.GameScene());
                    SpillNote(dir, file, alone);
                    win.Close();
                    var cell = new Border { Background = HudKit.GameScene(), Margin = new Thickness(8), Padding = new Thickness(16), VerticalAlignment = VerticalAlignment.Top, Child = ApplicantsFace.Build(theme, items, now, 0.94) };
                    Grid.SetRow(cell, r + 1); Grid.SetColumn(cell, c + 1); sheet.Children.Add(cell);
                }
            }
            var sw = Parked(sheet);
            Save(sheet, Path.Combine(dir, $"applicants-{code}.png"), 0);
            sw.Close();
        });
    }
}
