using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using KinahLedger.Parser;

namespace KinahLedger.Ui;

public static partial class WpfApp
{
    /// <summary>
    /// ui-shot --parser motion: Parser v2's overlay states and transitions from the sample (Parser/ParserDemo.cs).
    /// <list type="bullet">
    /// <item>The meter expanded in each look, and an eight-member force with you 6th (pinned): ui-pv2-*.png.</item>
    /// <item>Each transition as held frames, the Motion courses stepped to t (KL_MOTION_AT=t renders that frame alone):
    /// ui-pv2-motion-{swap,death,kill,pb,enter,jump}-NNN.png.</item>
    /// </list>
    /// Every window is offscreen and never activated; no shortcut is taken.
    /// </summary>
    static void ShotParserV2(MainWindow w, string dir, Dictionary<string, string> kinah)
    {
        var now = new DateTime(2026, 11, 13, 21, 30, 0, DateTimeKind.Utc);
        var (past, live) = ParserDemo.Sample(now, live: true);
        bool still0 = Motion.Still;
        var frozen0 = Motion.FrozenAt;
        bool exp0 = ParserRows.ExpandedPref;
        var best0 = ParserRows.BestOf;
        double width0 = HudStackLook.FightWidth;
        try
        {
            // ---- expanded, and pinned
            ParserRows.ExpandedPref = true; HudStackLook.FightWidth = width0 + 70;
            foreach (var look in new[] { HudLooks.Auto, HudLooks.Capsule, HudLooks.Meter, HudLooks.Float })
                Hud(look, live!, now, $"ui-pv2-{look}-expanded.png");
            ParserRows.ExpandedPref = false; HudStackLook.FightWidth = width0;
            var eight = Eight(live!);
            Hud(HudLooks.Auto, eight, now, "ui-pv2-auto-pinned.png");
            ParserRows.ExpandedPref = true; HudStackLook.FightWidth = width0 + 70;
            Hud(HudLooks.Auto, eight, now, "ui-pv2-auto-pinned-expanded.png");
            ParserRows.ExpandedPref = false; HudStackLook.FightWidth = width0;

            // ---- the transitions, frame by frame
            double[] frames = frozen0 is double at ? new[] { at } : new[] { 0.0, 0.15, 0.3, 0.45, 0.6, 0.8, 1.0 };
            var swapped = BossParser.Copy(live!);
            var lead = swapped.Shown().First(f => !f.You);
            swapped.Roster.First(f => f.You).Damage = lead.Damage + lead.Damage / 40;
            var died = BossParser.Copy(live!); died.Roster.First(f => f.Class == "Templar").Deaths = 1;
            var killed = BossParser.Copy(live!); killed.End = now; killed.Outcome = "kill"; killed.BossHp = 0;
            var ready = Encounter.Ready(KinahLedger.Packets.ContentKind.Expedition, "Ferocious Horn Den", "", now);
            foreach (var t in frames)
            {
                string n = $"{(int)Math.Round(t * 100):000}";
                // each frame from a fresh window: the first draw lays the rows down, the second is the change, held at t
                Motion.FrozenAt = null; Motion.Still = true; Motion.ForgetFigures();
                Change(live!, swapped, now, $"ui-pv2-motion-swap-{n}.png", t);
                Change(live!, died, now, $"ui-pv2-motion-death-{n}.png", t);
                Change(live!, killed, now, $"ui-pv2-motion-kill-{n}.png", t);
                var pb = BossParser.Copy(killed); var me = pb.Roster.First(f => f.You); me.Damage = (long)(me.Damage * 4190.0 / Math.Max(1, killed.Dps(killed.You!, now)));
                ParserRows.BestOf = _ => new PullRef { Dps = 4020 };
                Change(live!, pb, now, $"ui-pv2-motion-pb-{n}.png", t);
                ParserRows.BestOf = best0;
                // entering a dungeon: the Kinah pill unfolds into the meter
                {
                    Motion.FrozenAt = null;
                    var h = new HudWindow(null, HudLooks.Auto, still: true) { Left = -12000, Top = -12000, ShowActivated = false };
                    h.Update(kinah); h.Show(); DoEvents();
                    h.ShowParser(ready, now, ParserMode.Bosses, details: false); DoEvents();
                    Motion.FrozenAt = t;
                    h.SetKinahHidden(true); DoEvents();
                    Save(VisualRoot(h), Path.Combine(dir, $"ui-pv2-motion-enter-{n}.png"), 0, ShotGround);
                    h.Close();
                }
                // the Runs page's boss pull flies into the Parser
                {
                    Motion.FrozenAt = null;
                    w.ShowParserDemo("live"); DoEvents();
                    w.ShowDemo("Events"); DoEvents(); DoEvents();
                    var row = FindRow(w);
                    if (row != null)
                    {
                        Motion.FrozenAt = t;
                        w.JumpToFight(row, null); DoEvents();
                        Save(VisualRoot(w), Path.Combine(dir, $"ui-pv2-motion-jump-{n}.png"), 800 - 16);
                        ClearAdorners(w);
                    }
                }
            }
            // the Runs page with a run's boss fights (the card the jump starts from)
            Motion.FrozenAt = null;
            w.ShowParserDemo("live"); DoEvents(); w.ShowDemo("Events"); DoEvents(); DoEvents();
            Save((FrameworkElement)w.Content, Path.Combine(dir, "ui-pv2-runs-boss-fights.png"), 800 - 16);
        }
        finally
        {
            Motion.FrozenAt = frozen0; Motion.Still = still0; ParserRows.ExpandedPref = exp0; ParserRows.BestOf = best0; HudStackLook.FightWidth = width0;
        }

        void Hud(string look, Encounter e, DateTime at, string file)
        {
            var h = new HudWindow(null, look, still: true) { Left = -12000, Top = -12000, ShowActivated = false };
            h.Update(kinah); h.ShowParser(e, at, ParserMode.Bosses, details: false); h.Show(); DoEvents();
            Save((FrameworkElement)h.Content, Path.Combine(dir, file), 0, ShotGround);
            h.Close();
        }

        // a separate overlay drawn with one fight, then with the next (the change held at t)
        void Change(Encounter a, Encounter b, DateTime at, string file, double t)
        {
            Motion.FrozenAt = null;
            var o = new ParserOverlay(still: true) { Left = -12000, Top = -12000, Mode = ParserMode.Bosses };
            o.Draw(a, at); o.Show(); DoEvents();
            Motion.FrozenAt = t;
            o.Draw(b, b.End ?? at); DoEvents();
            Save((FrameworkElement)o.Content, Path.Combine(dir, file), ParserOverlay.Width0, ShotGround);
            o.Close();
        }
    }

    /// <summary>The window's whole visual (its adorners too: the morph's and the jump's pictures live there).</summary>
    static FrameworkElement VisualRoot(Window win) => VisualTreeHelper.GetChildrenCount(win) > 0 && VisualTreeHelper.GetChild(win, 0) is FrameworkElement fe ? fe : (FrameworkElement)win.Content;

    static FrameworkElement? FindRow(MainWindow w)
    {
        FrameworkElement? last = null;
        void Walk(DependencyObject d) { if (d is FrameworkElement fe && Equals(fe.Tag, "run-boss")) last = fe; foreach (var ch in LogicalTreeHelper.GetChildren(d).OfType<DependencyObject>()) Walk(ch); }
        Walk((DependencyObject)w.Content);
        return last;
    }

    static void ClearAdorners(Window w)
    {
        if (w.Content is not UIElement root || AdornerLayer.GetAdornerLayer(root) is not { } layer) return;
        foreach (var a in layer.GetAdorners(root) ?? Array.Empty<Adorner>()) layer.Remove(a);
    }

    /// <summary>The sample as an eight-member force where you are 6th (a render of the pinned row).</summary>
    static Encounter Eight(Encounter e)
    {
        var c = BossParser.Copy(e);
        long you = c.Roster.First(f => f.You).Damage;
        long id = 50;
        foreach (var (name, cls, k) in new[] { ("Vexmora", "Assassin", 1.146), ("Ithrandel", "Ranger", 1.099), ("Selunaire", "Spiritmaster", 1.051), ("Brannoc", "Chanter", 1.019) })
            c.Roster.Add(new Fighter { Id = id++, Name = name, Class = cls, Damage = (long)(you * k), CombatPower = 4600, ItemLevel = 5600, Server = "Siel" });
        return c;
    }
}
