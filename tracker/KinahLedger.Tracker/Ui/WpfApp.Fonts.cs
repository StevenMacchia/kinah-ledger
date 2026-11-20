using System.Collections;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace KinahLedger.Ui;

/// <summary>
/// Drawing text never holds on to more of the font (2026-11-20: Steven's 1.0.30 held 526,259 streams on the four Inter files
/// after 4.7 hours, 52 MB and growing about 11 MB an hour). WPF keeps every stream it opens on a pack resource in that part's
/// list for the life of the app, and its font loader opened one on the exe's own Inter files each time DirectWrite read them.
/// <see cref="AppFonts"/> serves Inter from files on disk instead. The check draws changing text in every Inter weight, size
/// and family the app uses, many times, and counts the streams held on the exe's font resources before and after.
/// (<c>KL_OPTIONS_ONLY=fonts</c> alone)
/// </summary>
public static partial class WpfApp
{
    static void FontStreamChecks(Action<bool, string> check)
    {
        _ = AppFonts.Text; _ = AppFonts.Display;   // the copy to disk reads each file once, before the count
        int? before = HeldFontStreams();
        check(before != null, "the streams held on the exe's font resources can be counted" + (before == null ? " (WPF's package parts were not found)" : ""));
        var families = new[] { HudKit.Text, AppFonts.Text, AppFonts.Display };
        var weights = new[] { FontWeights.Normal, FontWeights.Medium, FontWeights.SemiBold, FontWeights.Bold };
        // KL_FONT_ROUNDS: a long run of the same drawing, to watch the process's memory from outside (200 as the check runs)
        int rounds = int.TryParse(Environment.GetEnvironmentVariable("KL_FONT_ROUNDS"), out var r) && r > 0 ? r : 200;
        for (int i = 0; i < rounds; i++)
        {
            var panel = new StackPanel { Background = Brushes.Black };
            foreach (var f in families)
                foreach (var w in weights)
                    panel.Children.Add(new TextBlock { Text = $"{i * 7919 % 100000:N0} Kinah · DPS {i * 13 % 997}.{i % 10}K", FontFamily = f, FontWeight = w, FontSize = 9 + i % 23, Foreground = Brushes.White });
            panel.Measure(new Size(600, 2000)); panel.Arrange(new Rect(panel.DesiredSize));
            var rtb = new RenderTargetBitmap(Math.Max(1, (int)panel.ActualWidth), Math.Max(1, (int)panel.ActualHeight), 96, 96, PixelFormats.Pbgra32);
            rtb.Render(panel);
            var dv = new DrawingVisual();
            using (var dc = dv.RenderOpen())
                foreach (var f in families)
                    dc.DrawText(new FormattedText((i * 31).ToString("N0"), System.Globalization.CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                        new Typeface(f, FontStyles.Normal, weights[i % 4], FontStretches.Normal), 11 + i % 17, Brushes.White, 1.0), new Point(0, 0));
            if (i % 50 == 49) { GC.Collect(); GC.WaitForPendingFinalizers(); }
            DoEvents();
        }
        int? after = HeldFontStreams();
        check(after != null && before != null && after - before <= 4,
            $"drawing text {rounds} times holds no more font streams (held {before} before, {after} after)");
        var face = new Typeface(AppFonts.Text, FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal);
        check(face.TryGetGlyphTypeface(out var g) && g.FamilyNames.Values.Any(n => n.Contains("Inter")),
            "the window's face is still Inter" + (g != null ? $" ({string.Join("/", g.FamilyNames.Values)})" : " (no glyph typeface)"));
        var hud = new Typeface(HudKit.Text, FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal);
        check(hud.TryGetGlyphTypeface(out var hg) && hg.FamilyNames.Values.Any(n => n.StartsWith("Segoe UI")),
            "the overlays keep the face they were always drawn in, Segoe UI Variable" + (hg != null ? $" ({string.Join("/", hg.FamilyNames.Values)})" : " (no glyph typeface)"));
        check(AppFonts.FromFiles, "Inter is read from files on disk, not from the exe's resources" + (AppFonts.FromFiles ? $" ({AppFonts.Dir})" : ""));
    }

    /// <summary>How many streams WPF holds open on the exe's font resources (its package parts' lists), or null when its
    /// internals are not where they were (then the check above says so instead of passing quietly).</summary>
    static int? HeldFontStreams()
    {
        try
        {
            var wb = typeof(Visual).Assembly;   // PresentationCore
            var pre = wb.GetType("MS.Internal.IO.Packaging.PreloadedPackages");
            var get = pre?.GetMethod("GetPackage", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic, null, new[] { typeof(Uri) }, null);
            var pkg = get?.Invoke(null, new object[] { new Uri("application:///") });
            if (pkg == null) { Console.WriteLine($"  probe: pre {pre != null} get {get != null} pkg null; asm of PreloadedPackages: " + string.Join(",", AppDomain.CurrentDomain.GetAssemblies().Where(a => a.GetType("MS.Internal.IO.Packaging.PreloadedPackages") != null).Select(a => a.GetName().Name))); return null; }
            FieldInfo? list = null;
            for (var t = pkg.GetType(); t != null && list == null; t = t.BaseType)
                list = t.GetField("_partList", BindingFlags.Instance | BindingFlags.NonPublic);
            if (list?.GetValue(pkg) is not IDictionary parts) { Console.WriteLine($"  probe: pkg {pkg.GetType().FullName}, _partList field {list != null}, value {list?.GetValue(pkg)?.GetType().FullName}"); return null; }
            int n = 0;
            foreach (DictionaryEntry e in parts)
            {
                if (e.Value is not System.IO.Packaging.PackagePart part || !part.Uri.OriginalString.Contains("fonts/", StringComparison.OrdinalIgnoreCase)) continue;
                FieldInfo? held = null;
                for (var t = part.GetType(); t != null && held == null; t = t.BaseType)
                    held = t.GetField("_requestedStreams", BindingFlags.Instance | BindingFlags.NonPublic);
                if (held?.GetValue(part) is ICollection c) n += c.Count;
            }
            return n;
        }
        catch (Exception ex) { Console.WriteLine("  probe: " + ex.GetType().Name + " " + ex.Message); return null; }
    }
}
