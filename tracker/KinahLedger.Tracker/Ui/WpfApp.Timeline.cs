using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using KinahLedger.Parser;

namespace KinahLedger.Ui;

public static partial class WpfApp
{
    /// <summary>
    /// The Parser page's Timeline tab (Ui/ParserTimeline.cs): beside Compare and one click from a kept pull; the card fits the
    /// window; the zoom, the fold and the hovers; a pull kept before (no lanes) has no such tab; its Copy picture. KL_OPTIONS_ONLY=timeline runs it alone.
    /// </summary>
    static void TimelineChecks(MainWindow w, Action<bool, string> check)
    {
        try
        {
            w.ShowParserDemo("track"); DoEvents();
            var e = w.ParserShownForTest;
            var tabs = FindTagged(w.ParserPage, "parser-tabs") as Panel;
            var names = tabs?.Children.OfType<FrameworkElement>().Select(AutomationProperties.GetName).ToList() ?? new();
            int ci = names.IndexOf("Parser view: Compare"), ti = names.IndexOf("Parser view: Timeline");
            check(e != null && PullTimeline.Has(e) && ti >= 0 && (ci < 0 || ti == ci + 1),
                $"timeline: a kept boss pull has a Timeline tab, beside Compare ({string.Join(", ", names)})");

            // one click from the pull's Group table
            w.ParserTabForTest = "Group"; DoEvents();
            tabs = FindTagged(w.ParserPage, "parser-tabs") as Panel;
            var tab = tabs?.Children.OfType<FrameworkElement>().FirstOrDefault(b => AutomationProperties.GetName(b) == "Parser view: Timeline");
            tab?.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left) { RoutedEvent = UIElement.MouseLeftButtonUpEvent });
            DoEvents(); DoEvents();
            var card = FindTagged(w.ParserPage, "parser-timeline");
            var ch = ParserTimeline.Last;
            check(w.ParserTabForTest == "Track" && card != null && ch is { IsVisible: true } && ch.ActualHeight > 200,
                $"timeline: one click on the tab shows it ({w.ParserTabForTest}, the chart {ch?.ActualHeight:0} px tall)");

            // fits: the card's foot inside the window's view of the page, nothing scrolled to
            double Foot(FrameworkElement el) => el.TranslatePoint(new Point(0, el.ActualHeight), w.ParserPage).Y;
            var glass = card is { Parent: FrameworkElement gp } ? gp : card;
            double foot = glass != null ? Foot(glass) : double.MaxValue, room = w.ParserViewportForTest;
            check(foot <= room + 1, $"timeline: the card fits the window, its lanes folded ({foot:0} of {room:0} px)");

            if (ch?.Data is { } t)
            {
                check(t.Abilities.All(l => l.LaneDamage == l.Damage) && ch.Rows.Count(r => r.Kind == "ability") == Math.Min(t.Abilities.Count, PullTimeline.TopLanes + 1),
                    $"timeline: {ch.Rows.Count(r => r.Kind == "ability")} ability lanes shown of {t.Abilities.Count}, each adding up to its row");
                // the zoom: in round the middle, the span kept when the page redraws; Fit shows the whole pull again
                double len = ch.Length;
                ch.ZoomBy(0.5, null);
                double span = ch.To - ch.From;
                w.ParserTabForTest = "Track"; DoEvents();
                var ch2 = ParserTimeline.Last!;
                check(Math.Abs(span - len / 2) < 0.01 && Math.Abs(ch2.To - ch2.From - span) < 0.01,
                    $"timeline: the zoom halves the span ({span:0.0} of {len:0} s) and keeps it when the page redraws ({ch2.To - ch2.From:0.0} s)");
                (FindTagged(w.ParserPage, "parser-timeline-fit") as Border)?.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left) { RoutedEvent = UIElement.MouseLeftButtonUpEvent });
                DoEvents();
                check(ch2.From == 0 && Math.Abs(ch2.To - len) < 0.01, $"timeline: Fit shows the whole pull again ({ch2.From:0}-{ch2.To:0} s)");
                ch2.ZoomBy(1e-6, null);
                check(ch2.To - ch2.From >= Math.Min(ParserTimeline.Chart.MinSpan, len) - 1e-9, $"timeline: the zoom stops at {ParserTimeline.Chart.MinSpan} s ({ch2.To - ch2.From:0.0} s)");
                ch2.SetSpan(0, len, notify: true);
                // the hovers: a cast, a death
                var lane = ch2.Rows.First(r => r.Kind == "ability");
                var cast = (lane.Data as PullTimeline.Lane)!.Casts?.FirstOrDefault();
                var atCast = cast != null ? ch2.At(new Point(ch2.XOf(cast.At), lane.Top + lane.Height / 2)) : null;
                check(atCast is { } ac && ac.Title == lane.Label && ac.Lines.Count == 2 && ac.Lines[1].Figure == PK.Int(cast!.Damage),
                    $"timeline: the hover on a cast names it: {atCast?.Title}, {string.Join(" | ", atCast?.Lines.Select(l => l.Words + " " + l.Figure) ?? Array.Empty<string>())}");
                if (t.Deaths.FirstOrDefault() is { } d)
                {
                    var row = ch2.Rows.First(r => r.Data is PullTimeline.MemberLane m && m.F == d.F);
                    var atDeath = ch2.At(new Point(ch2.XOf(d.At), row.Top + row.Height / 2));
                    check(atDeath is { } ad && ad.Title.StartsWith(L.F("{0} died at {1}", ParserRows.NameOf(d.F), ParserTimeline.ClockTenths(d.At))) && ad.Lines.Count == Math.Max(1, d.Last.Count),
                        $"timeline: the hover on a death gives its recap: {atDeath?.Title} ({atDeath?.Lines.Count} lines)");
                }
                else check(false, "timeline: the render sample has a death");
                // the fold: Show all draws every ability, and folds back
                if (t.Folded != null)
                {
                    (FindTagged(w.ParserPage, "parser-timeline-fold") as Border)?.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left) { RoutedEvent = UIElement.MouseLeftButtonUpEvent });
                    DoEvents();
                    int open = ParserTimeline.Last!.Rows.Count(r => r.Kind == "ability");
                    (FindTagged(w.ParserPage, "parser-timeline-fold") as Border)?.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left) { RoutedEvent = UIElement.MouseLeftButtonUpEvent });
                    DoEvents();
                    int shut = ParserTimeline.Last!.Rows.Count(r => r.Kind == "ability");
                    check(open == t.Abilities.Count && shut == PullTimeline.TopLanes + 1, $"timeline: Show all draws every ability ({open}), and folds back ({shut})");
                }
                // a death's chip opens its recap under the row
                (FindTagged(w.ParserPage, "parser-timeline-death") as Border)?.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left) { RoutedEvent = UIElement.MouseLeftButtonUpEvent });
                DoEvents();
                check(FindTagged(w.ParserPage, "parser-timeline-recap") != null, "timeline: a death's chip opens its recap");
                ParserTimeline.DeathOpenForTest = -1;
            }

            // the Copy picture of the tab: the timeline in the share frame
            var parts = w.SharePartsForTest;
            var laid = ParserShare.Laid(e!, w.ShareNowForTest(e!), w.ParserPastForTest, ParserShare.Track, parts);
            check(FindTagged(laid, "share-view-track") != null && FindTagged(laid, "parser-timeline") != null && ParserShare.Name(ParserShare.Track) == L.T("Timeline") && ParserShare.Name(ParserShare.Timeline) == L.T("Graphs"),
                "timeline: its Copy picture is the timeline in the share frame; the Graphs tab's picture is called Graphs");

            // a pull kept before 1.0.38 (no lanes): no Timeline tab
            foreach (var f in e!.Roster) { f.TopHits = null; foreach (var a in f.Abilities) { a.Seconds = null; a.BossSeconds = null; a.CastLog = null; } }
            w.ParserTabForTest = "Group"; DoEvents();
            tabs = FindTagged(w.ParserPage, "parser-tabs") as Panel;
            names = tabs?.Children.OfType<FrameworkElement>().Select(AutomationProperties.GetName).ToList() ?? new();
            check(!names.Contains("Parser view: Timeline"), $"timeline: a pull kept before has no Timeline tab ({string.Join(", ", names)})");
        }
        catch (Exception ex) { check(false, "timeline: " + ex); }
        finally { w.ShowDemo(); DoEvents(); }
    }
}
