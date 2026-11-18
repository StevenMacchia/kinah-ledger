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
    static readonly string[] ApplicantThemes = { HudThemes.Classic, HudThemes.Tactical, HudThemes.Wacky, HudThemes.Glitch, HudThemes.Modern };
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
            board.Take(A(40, PartyApplication.Kind.Applied, "1", "mz", "Gladiator", 133_774));
            board.Take(A(35, PartyApplication.Kind.Applied, "2", "Rezo", "Gladiator", 128_195));
            board.Take(A(33, PartyApplication.Kind.Applied, "3", "RubyDAD", "Sorcerer", 125_846));
            w.ApplicantsTickForTest(now); DoEvents();
            var ov = w.ApplicantsOverlayForTest;
            int RowsShown() => ov?.FaceForTest is { } f ? RowCount(f) : -1;
            bool three = ov is { IsVisible: true } && RowsShown() == 3;
            var names = ov?.FaceForTest is { } f3 ? RowNames(f3) : new List<string>();
            bool order = names.SequenceEqual(new[] { "mz", "Rezo", "RubyDAD" });
            board.Take(A(10, PartyApplication.Kind.Accepted, "2", "Rezo"));
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
            board.Take(A(5, PartyApplication.Kind.Applied, "4", "Bode", "Cleric", 110_552));
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
                foreach (var theme in ApplicantThemes)
                    foreach (int n in ApplicantCounts)
                    {
                        var items = ApplicantsFace.Sample(n, now);
                        var face = ApplicantsFace.Build(theme, items, now, 0.94);
                        var win = Parked(face);
                        if (RowCount(face) != n) problems.Add($"{theme} {n} {code}: {RowCount(face)} rows");
                        foreach (var c in CutOffs(face, items.Select(a => a.Name).ToList())) problems.Add($"{theme} {n} {code}: {c}");
                        if (Spill(face) is { Length: > 0 } sp) problems.Add($"{theme} {n} {code}: {sp}");
                        sources.AddRange(w.SourceWordsShown(face).Select(s => $"{theme} {code}: {s}"));
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
        try { Sheets(); } finally { keep.Close(); }
        Console.WriteLine($"wrote {Path.GetFullPath(dir)}" + (File.Exists(spill) ? " (spill.txt lists what was cut)" : " (nothing cut)"));
        return File.Exists(spill) ? 1 : 0;

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
