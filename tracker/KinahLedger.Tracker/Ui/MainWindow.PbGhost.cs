using KinahLedger.Parser;

namespace KinahLedger.Ui;

public partial class MainWindow
{
    /// <summary>
    /// The demo's PB ghost (the Parser page's demo and the renders): the curve of the demo's best kill of the fight, when it
    /// carries damage by the second, and its DPS; <see cref="DemoGhostFor"/> when a render sets one.
    /// </summary>
    (GhostCurve? Curve, double BestDps) DemoGhost(Encounter e)
    {
        if (DemoGhostFor?.Invoke(e) is { } set) return set;
        var best = FightView.Records(e, ParserPast()).Best;
        return (best?.E is { } b ? GhostCurve.Of(b) : null, best?.Dps ?? 0);
    }

    /// <summary>A render's or a test's ghost for a demo fight (null: the demo's own).</summary>
    internal static Func<Encounter, (GhostCurve? Curve, double BestDps)?>? DemoGhostFor { get; set; }
}
