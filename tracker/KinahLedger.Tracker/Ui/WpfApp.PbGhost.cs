using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using KinahLedger.Parser;

namespace KinahLedger.Ui;

/// <summary>
/// The PB ghost on the meter (Parser/PbGhost.cs, Ui/GhostMarks.cs): <c>ui-shot DIR --parser ghost</c> draws the worst-case fight
/// (WpfApp.CutOff.cs) in each meter look with a ghost ahead (+6.2%), behind (−3.1%), and at the kill a new best (+4.1%) and
/// off it (−2.3%): ui-ghost-LOOK-STATE.png, at Steven's settings (width 350, 110%, expanded, details). KL_GHOST_LOOK=line,glass
/// picks the looks; KL_GHOST_GROUP=1 adds the group's pace. cut.txt lists any text cut.
/// </summary>
public static partial class WpfApp
{
    /// <summary>A made-up best kill whose pace is flat (the same DPS all through) and whose boss falls evenly: <paramref name="dps"/> at every point of it.</summary>
    internal static GhostCurve FlatGhost(double dps, double groupDps, int seconds = 600, bool boss = true)
    {
        var s = seconds;
        string Per(double v) => string.Join(",", Enumerable.Repeat(((long)Math.Round(v)).ToString(CultureInfo.InvariantCulture), s));
        string Hp() => string.Join(",", Enumerable.Range(0, s).Select(i => ((long)Math.Round(10_000.0 * (s - 1 - i) / s)).ToString(CultureInfo.InvariantCulture)));
        return new GhostCurve { Fight = "ghost-best", Boss = boss, Seconds = s, Dps = Math.Round(dps) , GroupDps = Math.Round(groupDps), You = Per(dps), Group = Per(groupDps), Hp = Hp() };
    }

    /// <summary>The ghost states drawn: (tag, the fight, its moment, your pace against the ghost, the group's).</summary>
    internal static List<(string Tag, Encounter E, DateTime At, double Delta, double Group)> GhostStates(DateTime now)
    {
        var (live, killed, _, _) = WorstSample(now);
        return new() { ("ahead", live, now, 0.062, 0.031), ("behind", live, now, -0.031, 0.012), ("newbest", killed, now, 0.041, 0.025), ("offbest", killed, now, -0.023, -0.008) };
    }

    /// <summary>Sets the ghost for a state: a flat best whose DPS stands <paramref name="delta"/> from yours (live at this point, or at the kill).</summary>
    internal static void UseGhost(Encounter e, DateTime at, double delta, double group)
    {
        var you = e.Dps(e.You!, e.End ?? at);
        var best = you / (1 + delta);
        var curve = FlatGhost(best, e.GroupDps(e.End ?? at) / (1 + group), boss: e.BossView);
        PbGhost.Forget();
        PbGhost.Source = _ => (curve, best);
    }

    /// <summary>
    /// options-test (<c>KL_OPTIONS_ONLY=ghost</c> alone): Settings › DPS meter › More › PB ghost (on, Group pace off and only
    /// with it), saved to parser.json and taken by the meter; then in a set of looks, the ghost's line on your row (behind your
    /// bar's end when you're ahead, past it when behind, at the length your best's pace gives) and the strip's words, at the
    /// kill and never on a wipe or with the switch off. The strip never cuts a figure in any look: the cut-off sweep draws it.
    /// </summary>
    static void GhostChecks(MainWindow w, Action<bool, string> check)
    {
        static void Click(System.Windows.Controls.Primitives.ButtonBase b) => b.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
        OptionsWindow Open() { var o = new OptionsWindow(w, OptionsWindow.Page.Meter) { WindowStartupLocation = WindowStartupLocation.Manual, Left = -12000, Top = -12000, ShowActivated = false }; o.Show(); DoEvents(); return o; }
        var opt = Open();
        var (gh, gr) = opt.ParserGhostForTest;
        check(gh.IsChecked == true && gr.IsChecked != true && gr.IsEnabled && opt.ParserSwitchTitlesForTest.Contains("PB ghost") && PbGhost.Shown && !PbGhost.GroupShown,
            "ghost: Settings › DPS meter › More › PB ghost, on by default, Group pace off beside it");
        gh.IsChecked = false; Click(gh); DoEvents();
        check(!gr.IsEnabled, "ghost: Group pace waits for PB ghost (off with it)");
        opt.SaveForTest(); DoEvents();
        check(!KinahLedger.Parser.ParserPrefs.Load().Ghost && !PbGhost.Shown, $"ghost: PB ghost off saves to parser.json and the meter takes it ({PbGhost.Shown})");
        if (opt.IsVisible) opt.Close();
        opt = Open(); (gh, gr) = opt.ParserGhostForTest;
        gh.IsChecked = true; Click(gh); gr.IsChecked = true; Click(gr); DoEvents();
        opt.SaveForTest(); DoEvents();
        check(KinahLedger.Parser.ParserPrefs.Load() is { Ghost: true, GhostGroup: true } && PbGhost.Shown && PbGhost.GroupShown, "ghost: on again with Group pace, saved and taken");
        if (opt.IsVisible) opt.Close();
        opt = Open(); (gh, gr) = opt.ParserGhostForTest;
        gr.IsChecked = false; Click(gr); DoEvents(); opt.SaveForTest(); DoEvents();
        if (opt.IsVisible) opt.Close();

        var now = new DateTime(2026, 11, 13, 21, 30, 0, DateTimeKind.Utc);
        var states = GhostStates(now);
        var (_, wipeBase, _, _) = WorstSample(now);
        var wipe = BossParser.Copy(wipeBase); wipe.Id = "worst-wipe"; wipe.Outcome = "wipe"; wipe.BossHp = 9_000_000;
        var src0 = PbGhost.Source; bool shown0 = PbGhost.Shown, group0 = PbGhost.GroupShown, still0 = Motion.Still;
        double fw0 = HudStackLook.FightWidth; bool exp0 = ParserRows.ExpandedPref;
        try
        {
            Motion.Still = true; PbGhost.Shown = true; PbGhost.GroupShown = false;
            foreach (var look in MeterLooks)
            {
                var o = new ParserOverlay(still: true) { Left = -12000, Top = -12000, Look = look, ShowDetails = true };
                bool shown = false;
                var bad = new List<string>();
                void Draw(Encounter e, DateTime at)
                {
                    o.Mode = e.ModeKind; o.Draw(e, at);
                    if (!shown) { o.Show(); shown = true; }
                    DoEvents(); o.Draw(e, at.AddMilliseconds(1)); DoEvents();
                }
                int chips = 0;
                foreach (var (tag, e, at, delta, group) in states)
                {
                    UseGhost(e, at, delta, group);
                    PbGhost.Shown = false; Draw(e, at);
                    double h0 = RootOf(o).ActualHeight;
                    PbGhost.Shown = true; Draw(e, at.AddMilliseconds(2));
                    if (Math.Abs(RootOf(o).ActualHeight - h0) > 0.5) bad.Add($"{tag}: the meter grew {RootOf(o).ActualHeight - h0:0.#} px");
                    var line = Find<GhostMarks.LineArt>(RootOf(o)).FirstOrDefault(x => x.IsVisible);
                    if (line == null) { bad.Add($"{tag}: no ghost line"); continue; }
                    string want = tag switch { "ahead" => "+6.2%", "behind" => "−3.1%", "newbest" => "PB +4.1%", _ => "−2.3%" };
                    if (line.ChipShown != null) { chips++; if (line.ChipShown != want) bad.Add($"{tag}: the chip says \"{line.ChipShown}\""); }
                    string tipWant = tag switch { "ahead" => "+6.2% vs best", "behind" => "−3.1% vs best", "newbest" => "New best by 4.1%!", _ => "2.3% off your best" };
                    if (line.TipText is not string tip || !tip.StartsWith(tipWant)) bad.Add($"{tag}: the tooltip says \"{line.TipText}\"");
                    foreach (var c in GhostChipClashes(RootOf(o))) bad.Add($"{tag}: {c}");
                    double bx = line.FullForTest.X, yx = line.YouX, gx = line.GhostX;
                    double ratio = yx - bx > 1 ? (gx - bx) / (yx - bx) : double.NaN, wantRatio = 1 / (1 + delta);
                    if (double.IsNaN(ratio) || Math.Abs(ratio - wantRatio) > 0.015 || (delta > 0) != (gx < yx)) bad.Add($"{tag}: line at {gx:0.#} for your bar's end {yx:0.#} from {bx:0.#} ({ratio:0.###}, want {wantRatio:0.###})");
                }
                if (chips == 0) bad.Add("the chip never found room at 350 px");
                // a wipe says nothing; the switch off shows nothing
                UseGhost(wipe, now, 0.05, 0);
                Draw(wipe, now);
                if (Find<GhostMarks.LineArt>(RootOf(o)).Any(x => x.IsVisible)) bad.Add("a wipe shows the ghost");
                PbGhost.Shown = false;
                UseGhost(states[0].E, now, 0.062, 0);
                Draw(states[0].E, now.AddSeconds(1));
                if (Find<GhostMarks.LineArt>(RootOf(o)).Any(x => x.IsVisible)) bad.Add("shown with the switch off");
                PbGhost.Shown = true;
                o.Close();
                check(bad.Count == 0, $"ghost in the {look} meter: the line at your best's pace on your bar, the chip ({chips} of 4 states had room: \"+6.2%\", \"−3.1%\", \"PB +4.1%\", \"−2.3%\") clear of every word, the full words in its tooltip, no height added; nothing on a wipe or switched off{(bad.Count > 0 ? ": " + string.Join("; ", bad) : "")}");
            }
            // tight rows (the 300 px pop-out at 110%, Korean words): the chip hides rather than touch a word, nothing is cut
            {
                string lang0 = L.Code;
                var names = WorstNames.Append(states[0].E.Title).ToList();
                var tight = new List<string>();
                int hidden = 0, drawn = 0;
                try
                {
                    ParserRows.ExpandedPref = false; HudStackLook.FightWidth = 300;
                    foreach (var lang in new[] { "en", "ko" })
                    {
                        L.Init(lang);
                        foreach (var look in MeterLooks)
                        {
                            var o = new ParserOverlay(still: true) { Left = -12000, Top = -12000, Look = look, ShowDetails = true };
                            o.SetScale(1.1); o.ApplyFightWidth();
                            bool shown = false;
                            foreach (var (tag, e, at, delta, group) in states)
                            {
                                UseGhost(e, at, delta, group);
                                // the same frame without the ghost first: only what the ghost cuts is its own (300 px trims words of its own)
                                PbGhost.Shown = false;
                                o.Mode = e.ModeKind; o.Draw(e, at);
                                if (!shown) { o.Show(); shown = true; }
                                DoEvents();
                                var before = CutOffs(RootOf(o), names).ToHashSet();
                                PbGhost.Shown = true;
                                o.Draw(e, at.AddMilliseconds(1)); DoEvents(); o.Draw(e, at.AddMilliseconds(2)); DoEvents();
                                foreach (var c in CutOffs(RootOf(o), names).Where(c => !before.Contains(c)).Concat(GhostChipClashes(RootOf(o)))) tight.Add($"{lang} {look} {tag}: {c}");
                                foreach (var gl in Find<GhostMarks.LineArt>(RootOf(o)).Where(x => x.IsVisible)) { drawn++; if (gl.ChipShown == null) hidden++; }
                            }
                            o.Close();
                        }
                    }
                }
                finally { L.Init(lang0); ParserRows.ExpandedPref = exp0; HudStackLook.FightWidth = fw0; }
                check(tight.Count == 0 && drawn > 0, $"ghost at 300 px and 110%, English and Korean, every look: nothing cut, no chip over a word ({hidden} of {drawn} chips hid for room){(tight.Count > 0 ? ": " + string.Join("; ", tight.Take(10)) : "")}");
            }
            // the group's pace beside yours, in the chip when there's room, always in its tooltip
            PbGhost.GroupShown = true;
            {
                var o = new ParserOverlay(still: true) { Left = -12000, Top = -12000, Look = "glass", ShowDetails = true };
                var (_, e, at, delta, group) = states[0];
                UseGhost(e, at, delta, group);
                o.Mode = e.ModeKind; o.Draw(e, at); o.Show(); DoEvents(); o.Draw(e, at.AddMilliseconds(1)); DoEvents();
                var line = Find<GhostMarks.LineArt>(RootOf(o)).FirstOrDefault(x => x.IsVisible);
                check(line?.TipText is string tip && tip.Contains("group +3.1%") && (line.ChipShown == null || line.ChipShown.StartsWith("+6.2%")),
                    $"ghost: Group pace adds the group's (chip \"{line?.ChipShown}\", tooltip \"{line?.TipText}\")");
                o.Close();
            }
        }
        finally
        {
            PbGhost.Source = src0; PbGhost.Shown = shown0; PbGhost.GroupShown = group0; PbGhost.Forget(); Motion.Still = still0;
            HudStackLook.FightWidth = fw0; ParserRows.ExpandedPref = exp0;
        }
    }

    /// <summary>
    /// The ghost's chip on a meter drawn under <paramref name="root"/> touching a word or past its row: each as "what: why"
    /// (empty when none). The chip is drawn (no text of its own), so the cut-off check can't see it; this does.
    /// </summary>
    internal static List<string> GhostChipClashes(FrameworkElement root)
    {
        var found = new List<string>();
        root.UpdateLayout();
        foreach (var line in Find<GhostMarks.LineArt>(root).Where(l => l.IsVisible && l.ChipRect != null))
        {
            var chip = line.ChipRect!.Value;
            if (chip.Left < 0 || chip.Top < -0.5 || chip.Right > line.ActualWidth + 0.5 || chip.Bottom > line.ActualHeight + 0.5) found.Add($"ghost chip \"{line.ChipShown}\" past its row");
            foreach (var t in Find<TextBlock>(root).Where(t => t.IsVisible && t.ActualWidth > 0 && t.Opacity >= 0.15 && (t.Text.Trim().Length > 0 || t.Inlines.Count > 0)))
            {
                if (GhostMarks.InkOf(t, line) is not { } r) continue;
                if (r.IntersectsWith(chip) && Rect.Intersect(r, chip) is { Width: > 0.5, Height: > 0.5 }) found.Add($"ghost chip \"{line.ChipShown}\" over \"{t.Text}\" (it saw {line.WordsLast} words; row {line.Row?.GetType().Name}, text under it {line.Row?.IsAncestorOf(t)})");
            }
        }
        return found;
    }

    static void ShotGhost(string dir)
    {
        try { File.Delete(Path.Combine(dir, "spill.txt")); } catch (IOException) { }
        var only = (Environment.GetEnvironmentVariable("KL_GHOST_LOOK") ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var looks = only.Length > 0 ? only.ToList() : MeterLooks.ToList();
        var now = new DateTime(2026, 11, 13, 21, 30, 0, DateTimeKind.Utc);
        var states = GhostStates(now);
        var names = WorstNames.Append(states[0].E.Title).ToList();
        var src0 = PbGhost.Source; bool shown0 = PbGhost.Shown, group0 = PbGhost.GroupShown, still0 = Motion.Still;
        double fw0 = HudStackLook.FightWidth; bool exp0 = ParserRows.ExpandedPref;
        var cuts = new List<string>();
        try
        {
            Motion.Still = true;   // the bars settled where the figures put them (a render caught mid-slide puts the line off the bar's end)
            PbGhost.Shown = true; PbGhost.GroupShown = Environment.GetEnvironmentVariable("KL_GHOST_GROUP") == "1";
            double scale = 1.1; ParserRows.ExpandedPref = Environment.GetEnvironmentVariable("KL_GHOST_COMPACT") != "1";
            HudStackLook.FightWidth = 350 + (ParserRows.ExpandedPref ? 70 : 0);
            foreach (var look in looks)
            {
                var o = new ParserOverlay(still: true) { Left = -12000, Top = -12000, Look = look, ShowDetails = true };
                o.SetScale(scale);
                o.ApplyFightWidth();
                bool shown = false;
                foreach (var (tag, e, at, delta, group) in states)
                {
                    UseGhost(e, at, delta, group);
                    o.Mode = e.ModeKind;
                    o.Draw(e, at);
                    if (!shown) { o.Show(); shown = true; }
                    DoEvents();
                    o.Draw(e, at.AddMilliseconds(1));   // a second draw: the rows laid out (the ghost's line finds the bar's span)
                    DoEvents();
                    var root = RootOf(o);
                    foreach (var c in CutOffs(root, names).Concat(GhostChipClashes(root))) cuts.Add($"{look}-{tag}: {c}");
                    var f = Path.Combine(dir, $"ui-ghost-{look}-{tag}.png");
                    Save(root, f, 0, ShotGround);
                    SpillNote(dir, Path.GetFileName(f), root);
                }
                o.Close();
            }
        }
        finally
        {
            PbGhost.Source = src0; PbGhost.Shown = shown0; PbGhost.GroupShown = group0; PbGhost.Forget(); Motion.Still = still0;
            HudStackLook.FightWidth = fw0; ParserRows.ExpandedPref = exp0;
        }
        File.WriteAllLines(Path.Combine(dir, "cut.txt"), cuts);
        Console.WriteLine(cuts.Count == 0 ? "ghost: nothing cut" : $"ghost: {cuts.Count} cut (cut.txt)" + Environment.NewLine + string.Join(Environment.NewLine, cuts.Take(40)));
    }
}
