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

    /// <summary>A heal chip's row: the member's name (the chip sits on the name's line, after the name).</summary>
    static bool RowHasPlate(DependencyObject root, string name) =>
        FindTexts(root).FirstOrDefault(t => t.Text == name) is { } nt && VisualTreeHelper.GetParent(nt) is StackPanel line
        && line.Children.OfType<StackPanel>().Any(pl => pl.Visibility == Visibility.Visible && pl.Children.Count > 0);

    static string HealRowName(FrameworkElement figures) =>
        VisualTreeHelper.GetParent(figures) is StackPanel inPill && VisualTreeHelper.GetParent(inPill) is Border pill && VisualTreeHelper.GetParent(pill) is StackPanel line
            ? line.Children.OfType<TextBlock>().FirstOrDefault()?.Text ?? "" : "";

    /// <summary>
    /// options-test: the heal chip (Steven, 2026-11-16: the figures under the name and the foot line "look awful") in every
    /// place, a pull under way, held and killed, compact and expanded: on every member with healing (self-heals count, potions
    /// don't), their healing a second (the healing done until there are seconds enough), whole on the name's line (the name
    /// trims for it), clear of the DPS's column, no line along the row's foot; the meter is no taller than without heals;
    /// TOP HEALS on the kill's top healer, beside TOP DPS; the held result takes a heal of the pull's last moment that came
    /// after its kill (the saved copy never does).
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
                        var healers = e.Members.Where(f => f.Healing > 0).Select(f => f.Name).OrderBy(x => x).ToList();   // self-heals count (0.10.120)
                        var shown = figures.Select(HealRowName).OrderBy(x => x).ToList();
                        string tag = $"{name} {place}{(expanded ? " expanded" : "")}";
                        // every healer's chip; one with no room beside the name (a narrow look's expanded meter, a row with a TOP plate)
                        // gives way, never cut
                        bool narrow = expanded && place is "capsule" or "float";
                        var missing = healers.Except(shown).Where(n => !narrow && !RowHasPlate(root, n)).ToList();
                        if (!shown.All(healers.Contains) || missing.Count > 0 || lines.Count != 0) wrong.Add($"{tag}: heal chips on [{string.Join(",", shown)}], {lines.Count} foot lines (want [{string.Join(",", healers)}], none)");
                        var dpsLeft = TaggedShown(root, "row-dps").Select(t => t.TranslatePoint(new Point(0, 0), (UIElement)root).X).DefaultIfEmpty(9999).Min();
                        foreach (var fig in figures)
                        {
                            seen++;
                            var f = e.Members.First(m => m.Name == HealRowName(fig));
                            string want = HealWant(fig, e, f, at);
                            if (fig.Text != want) wrong.Add($"{tag} {f.Name}: \"{fig.Text}\" (want \"{want}\")");
                            // the chip on the name's line, whole (the name trims for it), clear of the DPS's column, inside its row
                            var pill = (Border)VisualTreeHelper.GetParent(VisualTreeHelper.GetParent(fig));
                            var host = (FrameworkElement)VisualTreeHelper.GetParent(VisualTreeHelper.GetParent(pill));
                            double pillLeft = pill.TranslatePoint(new Point(0, 0), host).X, pillRight = pill.TranslatePoint(new Point(pill.ActualWidth, 0), (UIElement)root).X;
                            double pillTop = pill.TranslatePoint(new Point(0, 0), host).Y;
                            if (pill.ActualWidth <= 0 || pillLeft + pill.ActualWidth > host.ActualWidth + 0.5) wrong.Add($"{tag} {f.Name}: the chip cut ({pillLeft:0.#}+{pill.ActualWidth:0.#} in {host.ActualWidth:0.#})");
                            if (pillRight > dpsLeft) wrong.Add($"{tag} {f.Name}: the chip under the DPS (right {pillRight:0.#}, DPS from {dpsLeft:0.#})");
                            if (pillTop < -0.5 || pillTop + pill.ActualHeight > host.ActualHeight + 0.5) wrong.Add($"{tag} {f.Name}: the chip outside its row ({pillTop:0.#}-{pillTop + pill.ActualHeight:0.#} of {host.ActualHeight:0.#})");
                        }
                        win.Close();
                    }
            check(wrong.Count == 0 && seen > 0, $"the heal chip in every place, live, held and killed, compact and expanded: on every healer (self-heals count), their healing a second, whole on the name's line, clear of the DPS, no line along the foot ({seen} drawn){(wrong.Count > 0 ? ": " + string.Join("; ", wrong.Take(8)) : "")}");

            // a raid healer's chip (thousands a second) on the expanded meter at the default width (Buddy's), where the name's room
            // is shorter: the chip shows whole and the name trims for it
            {
                ParserRows.ExpandedPref = true; HudStackLook.FightWidth = HudStackLook.FightWidthDefault + 70; ParserRows.BestOf = _ => null;
                var (name, big, at, _) = HealLooks(now).First(x => x.Name == "big");
                var (root, win) = HealWindow(big, at, "standard");
                // (the TOP HEALS row's chip gives way to its plate; the other healer's shows)
                var f = big.Members.Where(m => m.Healing > 0).FirstOrDefault(m => !RowHasPlate(root, m.Name)) ?? big.Members.First(m => m.Healing > 0);
                var fig = TaggedShown(root, "heal-figures").OfType<TextBlock>().FirstOrDefault(x => HealRowName(x) == f.Name);
                var pill = fig == null ? null : (Border)VisualTreeHelper.GetParent(VisualTreeHelper.GetParent(fig));
                var hostW = pill == null ? 0 : ((FrameworkElement)VisualTreeHelper.GetParent(VisualTreeHelper.GetParent(pill))).ActualWidth;
                double pillEnd = pill == null ? 9999 : pill.TranslatePoint(new Point(pill.ActualWidth, 0), (UIElement)VisualTreeHelper.GetParent(VisualTreeHelper.GetParent(pill))).X;
                string want = fig == null ? "" : HealWant(fig, big, f, at);
                check(fig != null && fig.Text == want && pillEnd <= hostW + 0.5,
                    $"...a raid healer's chip on the expanded meter at the default width shows whole, the name trims for it (\"{fig?.Text}\", want \"{want}\", ends {pillEnd:0.#} of {hostW:0.#})");
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
                    // heals1: the Cleric's chip, and a self-healer's since 0.10.120
                    if (Math.Abs(h1 - h2) > 0.5 || heals1 < 1 || heals2 != 0) taller.Add($"{place} {h1:0.#}/{h2:0.#} ({heals1}/{heals2} heal chips)");
                }
                check(taller.Count == 0, $"...the meter is no taller with the heal chips, in every place{(taller.Count > 0 ? ": " + string.Join("; ", taller) : "")}");
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
    static string HealWant(TextBlock fig, Encounter e, Fighter f, DateTime at) =>
        e.HpsOf(f, at) is { } hps ? $"{ParserRows.Compact(hps)}/s" : ParserRows.Compact(f.Healing);

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
