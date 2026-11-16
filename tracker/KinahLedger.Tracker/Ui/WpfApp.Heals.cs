using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using KinahLedger.Packets;
using KinahLedger.Parser;

namespace KinahLedger.Ui;

public static partial class WpfApp
{
    /// <summary>The places the heal line is drawn in: the Parser's own window (Standard and each fight look) and inside the Kinah overlay.</summary>
    static readonly string[] HealPlaces = { "standard", HudLooks.Auto, HudLooks.Capsule, HudLooks.Meter, HudLooks.Float, "kinah-panel", "kinah-auto" };

    /// <summary>
    /// The heal looks from the sample (Parser/ParserDemo.cs: the Cleric heals the others, the Templar only himself): a pull
    /// under way, the open world's fight held, a boss killed (TOP DPS and TOP HEALS), you as the healer (your gold frame, its
    /// heat), the meter expanded, and a narrow meter (the part that landed gives way).
    /// </summary>
    static List<(string Name, Encounter E, DateTime At, Func<Encounter, PullRef?>? Best)> HealLooks(DateTime now)
    {
        var (past, live) = ParserDemo.Sample(now, live: true);
        var kill = past.Last(e => e.Outcome == "kill");
        var held = HeldSample(live!, now);
        // you are the Cleric: your row carries the heal line inside its gold frame, your pace near your best (the heat)
        var mine = BossParser.Copy(live!);
        foreach (var f in mine.Roster) f.You = f.Class == "Cleric";
        var me = mine.Roster.First(f => f.You);
        double myDps = mine.DpsOf(me, now) ?? 1;
        var mineKill = BossParser.Copy(kill);
        foreach (var f in mineKill.Roster) f.You = f.Class == "Cleric";
        // a raid's healer: figures in the millions (the longest the line gets), killed with both plates
        var big = BossParser.Copy(kill);
        foreach (var f in big.Roster) { f.Healing *= 40; f.HealingEffective *= 40; f.HealingEstimated *= 40; f.HealingSelf *= 40; f.HealingPotions *= 40; }
        return new()
        {
            ("live", live!, now, null),
            ("held", held, held.End!.Value, null),
            ("killed", kill, kill.End!.Value, null),
            ("you-healer", mine, now, _ => new PullRef { Dps = myDps / 0.92 }),
            ("you-healer-killed", mineKill, mineKill.End!.Value, null),
            ("big", big, big.End!.Value, null),
        };
    }

    /// <summary>
    /// ui-shot --parser heals: the heal line and the plates in every place (ui-heal-LOOK-PLACE.png), compact and expanded
    /// (…-expanded), and a narrow meter (…-narrow), with the tracker still. Every window is offscreen and never activated.
    /// </summary>
    static void ShotHeals(string dir, Dictionary<string, string> kinah)
    {
        var now = new DateTime(2026, 11, 13, 21, 30, 0, DateTimeKind.Utc);
        bool still0 = Motion.Still, exp0 = ParserRows.ExpandedPref;
        var best0 = ParserRows.BestOf;
        double width0 = HudStackLook.FightWidth;
        string sub = Path.Combine(dir, "heals");
        Directory.CreateDirectory(sub);
        try
        {
            Motion.Still = true;
            foreach (var (name, e, at, best) in HealLooks(now))
            {
                ParserRows.BestOf = best ?? (_ => null);
                foreach (var place in HealPlaces)
                {
                    ParserRows.ExpandedPref = false; HudStackLook.FightWidth = width0;
                    Save(HealFrame(e, at, place, kinah), Path.Combine(sub, $"ui-heal-{name}-{place}.png"), 0, ShotGround);
                }
                ParserRows.ExpandedPref = true; HudStackLook.FightWidth = width0 + 70;
                Save(HealFrame(e, at, "standard", kinah), Path.Combine(sub, $"ui-heal-{name}-standard-expanded.png"), 0, ShotGround);
                ParserRows.ExpandedPref = false; HudStackLook.FightWidth = HudStackLook.FightWidthMin;
                Save(HealFrame(e, at, "standard", kinah), Path.Combine(sub, $"ui-heal-{name}-standard-narrow.png"), 0, ShotGround);
            }
        }
        finally { Motion.Still = still0; ParserRows.ExpandedPref = exp0; ParserRows.BestOf = best0; HudStackLook.FightWidth = width0; }
    }

    /// <summary>One fight drawn in one place, still; the picture of the meter, its window closed.</summary>
    static FrameworkElement HealFrame(Encounter e, DateTime at, string place, Dictionary<string, string> kinah)
    {
        Motion.ForgetFigures();
        Window win;
        if (place.StartsWith("kinah", StringComparison.Ordinal))
        {
            var h = new HudWindow(null, place == "kinah-auto" ? HudLooks.Auto : null, still: true) { Left = -12000, Top = -12000, ShowActivated = false };
            h.Update(kinah); h.ShowParser(e, at, e.ModeKind, details: false); h.SetKinahHidden(true);
            win = h;
        }
        else
        {
            var o = new ParserOverlay(still: true) { Left = -12000, Top = -12000, Mode = e.ModeKind };
            if (place != "standard") o.Look = place;
            o.Draw(e, at);
            win = o;
        }
        win.Show(); DoEvents();
        var shot = Snapshot((FrameworkElement)win.Content);
        win.Close();
        return shot;
    }

    /// <summary>A heal line's row: the member's name (the name cell holds the name's line and the figures under it).</summary>
    static string HealRowName(FrameworkElement figures) =>
        VisualTreeHelper.GetParent(figures) is Grid cell && cell.Children.OfType<Border>().FirstOrDefault()?.Child is StackPanel line ? line.Children.OfType<TextBlock>().FirstOrDefault()?.Text ?? "" : "";

    /// <summary>
    /// options-test: the heal line (Steven, 2026-11-15) in every place, a pull under way, held and killed, compact and expanded:
    /// only a member who healed another has it (the sample's Cleric, never the Templar's own heals); the figures say the
    /// healing done exactly, per second over the DPS's seconds, and the part that landed with ≈ when it fits (a narrow meter
    /// drops that part, never trims a figure); the line's length is the member's share of the group's healing; the figures
    /// sit under the name, inside the row, clear of the DPS's column; the meter is no taller than without heals; TOP HEALS
    /// on the kill's top healer, beside TOP DPS; the held result takes a heal of the pull's last moment that came after its
    /// kill (the saved copy never does).
    /// </summary>
    static void HealChecks(Action<bool, string> check, MainWindow? w = null)
    {
        var now = new DateTime(2026, 11, 13, 21, 30, 0, DateTimeKind.Utc);
        bool still0 = Motion.Still, exp0 = ParserRows.ExpandedPref;
        var best0 = ParserRows.BestOf;
        double width0 = HudStackLook.FightWidth;
        try
        {
            Motion.Still = true;
            var wrong = new List<string>();
            int seen = 0;
            foreach (var (name, e, at, best) in HealLooks(now))
                foreach (var expanded in new[] { false, true })
                    foreach (var place in HealPlaces)
                    {
                        ParserRows.BestOf = best ?? (_ => null);
                        ParserRows.ExpandedPref = expanded; HudStackLook.FightWidth = expanded ? width0 + 70 : width0;
                        var (root, win) = HealWindow(e, at, place);
                        var figures = TaggedShown(root, "heal-figures").OfType<TextBlock>().ToList();
                        var lines = TaggedShown(root, "heal-line");
                        var healers = e.Members.Where(f => f.HealingOthers > 0).Select(f => f.Name).OrderBy(x => x).ToList();
                        var shown = figures.Select(HealRowName).OrderBy(x => x).ToList();
                        string tag = $"{name} {place}{(expanded ? " expanded" : "")}";
                        if (!shown.SequenceEqual(healers) || lines.Count != healers.Count) wrong.Add($"{tag}: heal figures on [{string.Join(",", shown)}], {lines.Count} lines (want [{string.Join(",", healers)}])");
                        var dpsLeft = TaggedShown(root, "row-dps").Select(t => t.TranslatePoint(new Point(0, 0), (UIElement)root).X).DefaultIfEmpty(9999).Min();
                        foreach (var fig in figures)
                        {
                            seen++;
                            var f = e.Members.First(m => m.Name == HealRowName(fig));
                            string want = HealWant(fig, e, f, at);
                            if (fig.Text != want) wrong.Add($"{tag} {f.Name}: \"{fig.Text}\" (want \"{want}\")");
                            fig.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                            if (fig.DesiredSize.Width > fig.ActualWidth + 0.5) wrong.Add($"{tag} {f.Name}: the figures trimmed ({fig.DesiredSize.Width:0.#} in {fig.ActualWidth:0.#})");
                            // under the name, inside the row, clear of the DPS's column
                            var cell = (FrameworkElement)VisualTreeHelper.GetParent(fig);
                            var nameT = ((StackPanel)((Border)((Grid)cell).Children[0]).Child).Children.OfType<TextBlock>().First();
                            double nameTop = nameT.TranslatePoint(new Point(0, 0), cell).Y, figTop = fig.TranslatePoint(new Point(0, 0), cell).Y, figBottom = figTop + fig.ActualHeight;
                            double figRight = fig.TranslatePoint(new Point(fig.ActualWidth, 0), (UIElement)root).X;
                            var host = (FrameworkElement)((Grid)cell).Children[0];
                            var nameLine = (FrameworkElement)((Border)host).Child;
                            if (nameLine.ActualHeight > host.ActualHeight + 0.5) wrong.Add($"{tag} {f.Name}: the name's line cut ({nameLine.ActualHeight:0.#} in {host.ActualHeight:0.#})");
                            if (!(figTop > nameTop + 8 && figBottom <= cell.ActualHeight + 0.5 && nameTop >= -0.5 && figRight <= dpsLeft)) wrong.Add($"{tag} {f.Name}: figures at {figTop:0.#}-{figBottom:0.#} in a {cell.ActualHeight:0.#} row (name at {nameTop:0.#}), right {figRight:0.#}, DPS from {dpsLeft:0.#}");
                        }
                        // the line's length: the member's share of the group's healing, of the row's width
                        foreach (var ln in lines)
                        {
                            var rowW = ((FrameworkElement)VisualTreeHelper.GetParent(ln)).ActualWidth;
                            double want = e.GroupHealing > 0 ? (double)figures.Select(HealRowName).Select(n => e.Members.First(m => m.Name == n)).Max(m => m.Healing) / e.GroupHealing : 0;
                            if (healers.Count == 1 && Math.Abs(ln.ActualWidth - Math.Max(4, rowW * want)) > 1) wrong.Add($"{tag}: the heal line {ln.ActualWidth:0.#} of {rowW:0.#} (want the share {want:P0})");
                        }
                        win.Close();
                    }
            check(wrong.Count == 0 && seen > 0, $"the heal line in every place, live, held and killed, compact and expanded: on the healer of others only, the healing done, per second and ≈ the part that landed, under the name and clear of the DPS, the line their share ({seen} drawn){(wrong.Count > 0 ? ": " + string.Join("; ", wrong.Take(8)) : "")}");

            // a raid healer's figures (millions) on the expanded meter, where the name's room is short: the part that landed gives way,
            // no figure is trimmed
            {
                // at Buddy's width (the default since 0.10.119) the whole line fits, the part that landed too, nothing trimmed
                ParserRows.ExpandedPref = true; HudStackLook.FightWidth = HudStackLook.FightWidthDefault + 70; ParserRows.BestOf = _ => null;
                var (name, big, at, _) = HealLooks(now).First(x => x.Name == "big");
                {
                    var (root0, win0) = HealWindow(big, at, "standard");
                    var fig0 = TaggedShown(root0, "heal-figures").OfType<TextBlock>().FirstOrDefault();
                    fig0?.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                    check(fig0 != null && fig0.DesiredSize.Width <= fig0.ActualWidth + 0.5, $"...a raid healer's figures on the expanded meter at the default width trim no figure (\"{fig0?.Text}\")");
                    win0.Close();
                }
                // the old, narrower default (330): the name's room is short there
                HudStackLook.FightWidth = KinahLedger.Parser.ParserPrefs.OldDefaultWidth + 70;
                var (root, win) = HealWindow(big, at, "standard");
                var fig = TaggedShown(root, "heal-figures").OfType<TextBlock>().FirstOrDefault();
                var f = big.Members.First(m => m.HealingOthers > 0);
                fig?.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                string short2 = $"{Amounts.Short(f.Healing)} · {Amounts.Short(big.HpsOf(f, at) ?? 0)}/s";
                check(fig != null && fig.Text == short2 && fig.DesiredSize.Width <= fig.ActualWidth + 0.5,
                    $"...a raid healer's figures on the expanded meter drop the part that landed and trim no figure (\"{fig?.Text}\", want \"{short2}\")");
                win.Close();
                HudStackLook.FightWidth = width0;
            }

            // no taller: the meter with heals is the height of the same fight without them, in every place
            {
                var (_, live) = ParserDemo.Sample(now, live: true);
                var bare = BossParser.Copy(live!);
                foreach (var f in bare.Roster) { f.Healing = f.HealingEffective = f.HealingEstimated = f.HealingSelf = f.HealingPotions = 0; f.Heals = null; }
                var taller = new List<string>();
                foreach (var place in HealPlaces)
                {
                    ParserRows.ExpandedPref = false;
                    var (r1, w1) = HealWindow(live!, now, place); double h1 = ((FrameworkElement)r1).ActualHeight; int heals1 = TaggedShown(r1, "heal-figures").Count; w1.Close();
                    var (r2, w2) = HealWindow(bare, now, place); double h2 = ((FrameworkElement)r2).ActualHeight; int heals2 = TaggedShown(r2, "heal-figures").Count; w2.Close();
                    if (Math.Abs(h1 - h2) > 0.5 || heals1 != 1 || heals2 != 0) taller.Add($"{place} {h1:0.#}/{h2:0.#} ({heals1}/{heals2} heal lines)");
                }
                check(taller.Count == 0, $"...the meter is no taller with the heal line, in every place{(taller.Count > 0 ? ": " + string.Join("; ", taller) : "")}");
            }

            // the held result takes a heal of the pull's last moment that came after its kill (the copy saved never does)
            if (w != null)
            {
                var p = new BossParser(BossBook.Shared);
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
                p.Take(new HealDone(t.AddSeconds(3), 2, 1, 3_000, 1_200, 17120000, false, false, false, false));
                var end = DateTime.UtcNow.AddSeconds(-1);
                var hud = new HudWindow(null, HudLooks.Auto, still: true) { Left = -12000, Top = -12000, ShowActivated = false };
                var saved = new List<Encounter>();
                p.Ended = saved.Add;
                var (atEnd, afterLate, same) = w.HeldHealForTest(hud, p, 2,
                    () => { p.Take(new DamageDealt(end, 1, 881, 40_000, "Swoly", true, 11000000, 1)); p.Take(new BossHealth(end, 881, 0, 100_000)); p.Take(new UnitDied(end, 881, "Watchdog Kwapo", true)); },
                    () => p.Take(new HealDone(end.AddSeconds(-0.2), 2, 1, 2_000, 0, 17120000, false, false, false, false)));
                hud.Close();
                long savedHeal = saved.LastOrDefault()?.Members.FirstOrDefault(f => f.Id == 2)?.Healing ?? -1;
                check(same && atEnd == 3_000 && afterLate == 5_000 && savedHeal == 3_000,
                    $"...the held result takes a heal of the pull's last moment that came after its kill ({atEnd:N0} at the kill, {afterLate:N0} after; the copy saved {savedHeal:N0}{(same ? "" : "; not the same pull")})");
            }
        }
        finally { Motion.Still = still0; ParserRows.ExpandedPref = exp0; ParserRows.BestOf = best0; HudStackLook.FightWidth = width0; }
    }

    /// <summary>
    /// The heal figures a row should say: the whole line (done · per second · the part that landed, ≈ unless confirmed) when it
    /// fits the name's room, else without the part that landed, else the healing done alone.
    /// </summary>
    static string HealWant(TextBlock fig, Encounter e, Fighter f, DateTime at)
    {
        string done = Amounts.Short(f.Healing), ps = $"{done} · {Amounts.Short(e.HpsOf(f, at) ?? 0)}/s";
        string full = $"{ps} · {(f.HealingIsEstimate ? "≈" : "")}{Amounts.Short(f.HealingEffective)} eff";
        double room = ((FrameworkElement)VisualTreeHelper.GetParent(fig)).ActualWidth;
        foreach (var text in new[] { full, ps })
        {
            var probe = new TextBlock { Text = text, FontSize = fig.FontSize, FontWeight = fig.FontWeight, FontFamily = fig.FontFamily };
            System.Windows.Documents.Typography.SetNumeralAlignment(probe, FontNumeralAlignment.Tabular);
            TextOptions.SetTextFormattingMode(probe, TextOptions.GetTextFormattingMode(fig));
            probe.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            if (probe.DesiredSize.Width <= room) return text;
        }
        return done;
    }

    /// <summary>A fight drawn in one place, off-screen and still, for the checks: the meter's root and its window (closed by the caller).</summary>
    static (DependencyObject Root, Window Win) HealWindow(Encounter e, DateTime at, string place)
    {
        Motion.ForgetFigures();
        Window win;
        if (place.StartsWith("kinah", StringComparison.Ordinal))
        {
            var h = new HudWindow(null, place == "kinah-auto" ? HudLooks.Auto : null, still: true) { Left = -12000, Top = -12000, ShowActivated = false };
            h.Update(new Dictionary<string, string>()); h.ShowParser(e, at, e.ModeKind, details: false); h.SetKinahHidden(true);
            win = h;
        }
        else
        {
            var o = new ParserOverlay(still: true) { Left = -12000, Top = -12000, Mode = e.ModeKind };
            if (place != "standard") o.Look = place;
            o.Draw(e, at);
            win = o;
        }
        win.Show(); DoEvents();
        return ((DependencyObject)win.Content, win);
    }
}
