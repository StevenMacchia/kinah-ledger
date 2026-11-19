using System.Windows;
using KinahLedger.Parser;

namespace KinahLedger.Ui;

/// <summary>
/// The meter's Odyle Energy (Ui/EnergyMark.cs, 1.0.27): the options-test part (<c>KL_OPTIONS_ONLY=odyle</c> alone). Renders:
/// <c>KL_ODYLE=1975 ui-shot DIR --parser themes</c> (or <c>mine</c>); without KL_ODYLE the renders show none, as before.
/// </summary>
public static partial class WpfApp
{
    /// <summary>
    /// options-test: in every look the overlay's meter takes, the foot carries the art and the figure after the total (one
    /// mark, its figure said in full); none while it was never read; none with the switch off; none on the share picture.
    /// The switch is a row of its own under DPS meter › More.
    /// </summary>
    static void OdyleChecks(Action<bool, string> check)
    {
        int? fixed0 = EnergyMark.Fixed; bool pref0 = EnergyMark.Pref, still0 = Motion.Still;
        Motion.Still = true;
        try
        {
            var now = new DateTime(2026, 11, 13, 21, 30, 0, DateTimeKind.Utc);
            var (past, live) = ParserDemo.Sample(now, live: true, damage: true);
            var held = HeldSample(live!, now);
            var looks = HudLooks.FightLooks.Concat(HudLooks.All.Select(l => l.Key).Where(HudThemes.Themed))
                .Concat(new[] { ParserPrefs.StandardLook, ParserPrefs.PodiumLook, ParserPrefs.ReactorLook }).Distinct().ToList();
            (int Marks, string Said) Drawn(string look, Encounter e, DateTime at)
            {
                var po = new ParserOverlay(still: true) { Left = -12000, Top = -9000, Look = look, Mode = e.ModeKind };
                try
                {
                    po.Draw(e, at); po.Show(); DoEvents();
                    var marks = TaggedShown((DependencyObject)po.Content, "odyle-energy");
                    return (marks.Count, marks.Count == 1 ? System.Windows.Automation.AutomationProperties.GetName(marks[0]) : "");
                }
                finally { po.Close(); }
            }
            var wrong = new List<string>();
            EnergyMark.Pref = true; EnergyMark.Fixed = 1_975;
            foreach (var lk in looks)
                foreach (var (tag, e, at) in new[] { ("live", live!, now), ("held", held, held.End!.Value) })
                {
                    var (n, said) = Drawn(lk, e, at);
                    if (n != 1 || !said.EndsWith(" 1,975", StringComparison.Ordinal)) wrong.Add($"{lk} {tag}: {n} marks \"{said}\"");
                }
            check(wrong.Count == 0, $"Odyle Energy: the meter's foot shows the art and 1,975 in every look ({looks.Count} looks, live and held)" + (wrong.Count > 0 ? ": " + string.Join("; ", wrong) : ""));

            EnergyMark.Fixed = -1;
            var unknown = looks.Where(lk => Drawn(lk, held, held.End!.Value).Marks != 0).ToList();
            EnergyMark.Fixed = 1_975; EnergyMark.Pref = false;
            var off = looks.Where(lk => Drawn(lk, held, held.End!.Value).Marks != 0).ToList();
            EnergyMark.Pref = true;
            check(unknown.Count == 0 && off.Count == 0, $"Odyle Energy: none while never read (no 0), none with the switch off" + (unknown.Count + off.Count > 0 ? $": unknown in {string.Join(",", unknown)}; off in {string.Join(",", off)}" : ""));

            // a new figure draws the held meter again (its drawings compare it)
            var po2 = new ParserOverlay(still: true) { Left = -12000, Top = -9000, Look = ParserPrefs.StandardLook, Mode = held.ModeKind };
            string after;
            try
            {
                po2.Draw(held, held.End!.Value); po2.Show(); DoEvents();
                EnergyMark.Fixed = 1_895; po2.Draw(held, held.End!.Value); DoEvents();
                var m = TaggedShown((DependencyObject)po2.Content, "odyle-energy");
                after = m.Count == 1 ? System.Windows.Automation.AutomationProperties.GetName(m[0]) : "";
            }
            finally { po2.Close(); }
            check(after.EndsWith(" 1,895", StringComparison.Ordinal), $"Odyle Energy: a new figure reaches a meter already drawn (\"{after}\")");

            // the share picture is for others: none on it, in the standard look and a theme's
            var shareMarks = new[] { ParserPrefs.StandardLook, "line" }.Select(lk =>
            {
                var face = ParserOverlay.MeterFace(lk, held, held.End!.Value, held.ModeKind, false, 12, new Thickness(12));
                var host = new Window { Content = face, Left = -12000, Top = -9000, ShowActivated = false, ShowInTaskbar = false, WindowStyle = WindowStyle.None, SizeToContent = SizeToContent.WidthAndHeight, WindowStartupLocation = WindowStartupLocation.Manual };
                try { host.Show(); DoEvents(); return TaggedShown(face, "odyle-energy").Count; }
                finally { host.Close(); }
            }).Sum();
            check(shareMarks == 0 && EnergyMark.Value == 1_895, $"Odyle Energy: none on the share picture ({shareMarks}), and the meter keeps it after");
        }
        finally { EnergyMark.Fixed = fixed0; EnergyMark.Pref = pref0; Motion.Still = still0; }
    }
}
