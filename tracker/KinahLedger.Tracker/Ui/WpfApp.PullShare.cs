using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using KinahLedger.Parser;

namespace KinahLedger.Ui;

public static partial class WpfApp
{
    /// <summary>
    /// Share this pull (1.0.36, Ui/MainWindow.PullShare.cs): the share mark on each finished boss pull in the Parser page's
    /// list copies that pull's picture without opening it; the picture carries the gold PB badge only when the pull beat your
    /// best before it on that fight, its figure said once (the badge's, or the PB chip on your row), nothing cut off in any look.
    /// KL_OPTIONS_ONLY=pullshare runs these alone.
    /// </summary>
    static void PullShareChecks(MainWindow w, Action<bool, string> check)
    {
        static void Click(System.Windows.Controls.Primitives.ButtonBase b) => b.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
        static List<FrameworkElement> AllTagged(DependencyObject d, string tag)
        {
            var list = new List<FrameworkElement>();
            void Walk(DependencyObject x)
            {
                if (x is FrameworkElement fe && fe.Tag as string == tag) list.Add(fe);
                if (x is Visual) for (int i = 0; i < VisualTreeHelper.GetChildrenCount(x); i++) Walk(VisualTreeHelper.GetChild(x, i));
            }
            Walk(d);
            return list;
        }

        // ---- the mark on the list's rows ----
        var got = new List<(int W, int H, string File)>();
        var sink0 = MainWindow.ShareSinkForTest;
        MainWindow.ShareSinkForTest = (b, f) => got.Add((b.PixelWidth, b.PixelHeight, f));
        try
        {
            w.ShowParserDemo("history"); DoEvents();
            var rows = AllTagged(w.ParserPage, "parser-row");
            var marks = rows.Select(r => AllTagged(r, "parser-row-share").OfType<Button>().FirstOrDefault()).ToList();
            var past = w.ParserPastForTest;
            int wantMarks = past.Count(MainWindow.PullShareableForTest);
            check(rows.Count > 0 && marks.All(m => m != null) && marks.Count <= wantMarks,
                $"Share this pull: a share mark on every finished boss pull in the list ({marks.Count(m => m != null)} of {rows.Count} rows)");
            check(marks.All(m => m == null || (m.Width <= 24 && m.Opacity < 1 && m.Content is System.Windows.Shapes.Path && m.ToolTip is string tip && tip.Length > 0)),
                "Share this pull: the mark is small and quiet (24 px, faint at rest, no words of its own, its hover says what it does)");
            if (marks.FirstOrDefault(m => m != null) is { } first)
            {
                string? open0 = w.ParserOpenForTest;
                got.Clear();
                Click(first); DoEvents();
                var names = past.Select(MeterShare.FileName).ToHashSet();
                check(got.Count == 1 && got[0].W == MeterShare.PixelWidth && got[0].H >= 630 && names.Contains(got[0].File) && w.ParserOpenForTest == open0,
                    $"Share this pull: a click copies that pull's {MeterShare.PixelWidth} px picture and leaves the list as it was (pull not opened)" + (got.Count == 1 ? $" ({got[0].W}×{got[0].H}, {got[0].File}, open {w.ParserOpenForTest ?? "none"})" : $" ({got.Count} pictures)"));
                var shown = AllTagged(w.ParserPage, "parser-row-share").OfType<Button>().FirstOrDefault();
                check(shown?.Content is System.Windows.Shapes.Path p && p.Data?.ToString() == Geometry.Parse("M2,6.5 L5,9.3 L10.2,3.2").ToString(),
                    "Share this pull: the mark shows a tick for a moment after the copy");
            }
        }
        finally { MainWindow.ShareSinkForTest = sink0; }

        // ---- the PB badge ----
        var now = new DateTime(2026, 11, 13, 21, 30, 0, DateTimeKind.Utc);
        var (sample, live) = ParserDemo.Sample(now, live: true, damage: true);
        var kill = sample.Last(e => e.Outcome == "kill");
        var wipe = sample.Last(e => e.Outcome == "wipe");
        var end = kill.End!.Value;
        double dps = kill.Dps(kill.You!, end);
        var lower = new PullRef { E = kill, Dps = dps / 1.08 };
        var higher = new PullRef { E = kill, Dps = dps * 1.2 };
        var pb = MeterShare.PbOf(kill, lower);
        check(pb?.Dps is { } gain && Math.Abs(gain - 0.08) < 0.001 && MeterShare.PbOf(kill, higher) == null && MeterShare.PbOf(kill, null) == null
            && MeterShare.PbOf(wipe, new PullRef { E = wipe, Dps = 1 }) == null && MeterShare.PbOf(live!, new PullRef { E = live!, Dps = 1 }) == null,
            "Share this pull: a PB only on a kill that beat your best before it on that fight (not a slower one, not a first kill, a wipe or a fight under way)");

        var best0 = ParserRows.BestOf;
        var bad = new List<string>();
        try
        {
            string figure = PK.Signed(0.08);
            foreach (var look in MeterShareLooks(all: true))
            {
                try
                {
                    var lk = ShareLookOf(look, kill.ModeKind);
                    ParserRows.BestOf = x => x.Id == kill.Id ? lower : null;   // the meter's own PB chip reads the same best
                    var v = MeterShare.Laid(kill, end, lk, hideNames: true, before: lower);
                    var texts = DrawnTexts(v);
                    var miss = new List<string>();
                    var badge = AllTagged(v, "share-pb");
                    if (badge.Count != 1) miss.Add($"{badge.Count} badges");
                    if (!texts.Any(t => t == L.T("NEW PERSONAL BEST"))) miss.Add("no NEW PERSONAL BEST");
                    int said = texts.Count(t => t.Contains(figure, StringComparison.Ordinal));
                    if (said != 1) miss.Add($"{figure} said {said} times");
                    if (MeterShare.Trimmed(v) is { Count: > 0 } cut) miss.Add($"cut off: \"{cut[0]}\"");
                    string started = kill.Start.ToLocalTime().ToString("HH:mm", System.Globalization.CultureInfo.InvariantCulture);
                    if (!string.Join(" ", texts).Contains(started, StringComparison.Ordinal)) miss.Add("no start time");
                    ParserRows.BestOf = _ => null;
                    var plain = MeterShare.Laid(kill, end, lk, hideNames: true);
                    if (AllTagged(plain, "share-pb").Count != 0) miss.Add("a badge with no record beaten");
                    if (miss.Count > 0) bad.Add($"{look}: {string.Join(", ", miss)}");
                }
                catch (Exception ex) { bad.Add($"{look}: {ex.GetType().Name} {ex.Message}"); }
            }
        }
        finally { ParserRows.BestOf = best0; }
        check(bad.Count == 0, "Share this pull: in every look a beaten record puts one gold NEW PERSONAL BEST badge in the head, its figure said once, the start time shown, nothing cut off; none without a record beaten"
            + (bad.Count > 0 ? ": " + string.Join("; ", bad.Take(10)) : ""));
    }
}
