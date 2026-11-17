using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using KinahLedger.Parser;

namespace KinahLedger.Ui;

/// <summary>
/// Concept samples for the meter looks Steven asked to rework (2026-11-17: Auto, Goo, Scorebug, Standard, VHS: "clear,
/// legible and fucking incredible cinematic production quality"): three full designs a look (A, B, C), drawn for renders
/// only, never offered in Settings until he picks one. Each look's concepts live in their own file (Ui/Concepts/), adding
/// themselves through its partial method; <c>ui-shot DIR --parser concepts</c> draws them all on the worst-case fight.
/// </summary>
public static partial class MeterConcepts
{
    /// <summary>A concept: the look it is for, its letter, a few words on it, and its meter for a fight at a width (DIPs, the meter's FightWidth).</summary>
    public sealed record Concept(string Look, string Key, string Name, Func<Encounter, DateTime, double, FrameworkElement> Make);

    public static IReadOnlyList<Concept> All
    {
        get
        {
            var l = new List<Concept>();
            AddStandard(l); AddAuto(l); AddGoo(l); AddScorebug(l); AddVhs(l);
            return l;
        }
    }

    static partial void AddStandard(List<Concept> l);
    static partial void AddAuto(List<Concept> l);
    static partial void AddGoo(List<Concept> l);
    static partial void AddScorebug(List<Concept> l);
    static partial void AddVhs(List<Concept> l);
}

public static partial class WpfApp
{
    /// <summary>
    /// ui-shot DIR --parser concepts: every concept (Ui/MeterConcepts.cs) and each reworked look as it is now, on the worst-case
    /// fight (WorstSample), killed and live, at width 350 and 110% over the game scene: concept-LOOK-KEY-STATE.png and
    /// concept-LOOK-current-STATE.png; concepts-cut.txt lists any text cut (CutOffs). KL_CONCEPT_LOOK=goo,vhs draws those only.
    /// </summary>
    static void ShotConcepts(string dir)
    {
        var only = (Environment.GetEnvironmentVariable("KL_CONCEPT_LOOK") ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var now = new DateTime(2026, 11, 13, 21, 30, 0, DateTimeKind.Utc);
        var (live, killed, _, _) = WorstSample(now);
        string? ck0 = Encounter.CritKey, bk0 = Encounter.BackKey;
        double fw0 = HudStackLook.FightWidth; bool exp0 = ParserRows.ExpandedPref;
        var cuts = new List<string>();
        var names = WorstNames.Append(live.Title).ToList();
        try
        {
            Encounter.CritKey = Encounter.CritKeyOf("result3"); Encounter.BackKey = Encounter.BackKeyOf("side1");
            HudStackLook.FightWidth = 350; ParserRows.ExpandedPref = false;
            var looks = new[] { ParserPrefs.StandardLook, HudLooks.Auto, HudThemes.Goo, HudThemes.Scorebug, HudThemes.Vhs }.Where(k => only.Length == 0 || only.Contains(k));
            foreach (var look in looks)
            {
                var faces = new List<(string Key, Func<Encounter, FrameworkElement> Make)> { ("current", e => ParserOverlay.PreviewFace(look, e, e.End ?? now, e.ModeKind, true)) };
                foreach (var c in MeterConcepts.All.Where(c => c.Look == look)) faces.Add((c.Key, e => c.Make(e, e.End ?? now, HudStackLook.FightWidth)));
                foreach (var (key, make) in faces)
                    foreach (var (tag, e) in new[] { ("killed", killed), ("live", live) })
                    {
                        FrameworkElement face;
                        try { face = make(e); }
                        catch (Exception ex) { cuts.Add($"{look}-{key}-{tag}: failed: {ex.Message}"); continue; }
                        // over the game, at Steven's 110% (a window of its own: laid out and drawn as on screen)
                        var scaled = new Border { Child = face, LayoutTransform = new ScaleTransform(1.1, 1.1), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top };
                        var stage = new Border { Background = HudKit.GameScene(), Padding = new Thickness(28, 24, 28, 24), Child = scaled };
                        var win = new Window { WindowStyle = WindowStyle.None, AllowsTransparency = true, Background = Brushes.Transparent, ShowInTaskbar = false, ShowActivated = false,
                            SizeToContent = SizeToContent.WidthAndHeight, Left = -12000, Top = -12000, WindowStartupLocation = WindowStartupLocation.Manual, Content = stage };
                        TextOptions.SetTextFormattingMode(win, TextFormattingMode.Display);
                        win.Show(); DoEvents();
                        foreach (var c in CutOffs(face, names)) cuts.Add($"{look}-{key}-{tag}: {c}");
                        Save(stage, Path.Combine(dir, $"concept-{look}-{key}-{tag}.png"), 0);
                        win.Close();
                    }
            }
        }
        finally { Encounter.CritKey = ck0; Encounter.BackKey = bk0; HudStackLook.FightWidth = fw0; ParserRows.ExpandedPref = exp0; }
        File.WriteAllLines(Path.Combine(dir, "concepts-cut.txt"), cuts);
        Console.WriteLine(cuts.Count == 0 ? "concepts: nothing cut" : $"concepts: {cuts.Count} cut (concepts-cut.txt)" + Environment.NewLine + string.Join(Environment.NewLine, cuts.Take(40)));
    }
}
