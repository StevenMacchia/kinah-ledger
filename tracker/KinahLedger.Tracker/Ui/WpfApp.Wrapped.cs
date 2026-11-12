using System.IO;
using System.Windows;
using System.Windows.Media;
using KinahLedger.Session;

namespace KinahLedger.Ui;

public static partial class WpfApp
{
    /// <summary>
    /// ui-shot --wrapped week|day (Week wrapped and Day wrapped, Ui/MainWindow.Wrapped.cs): the recap sheet's window over the
    /// look's page colour, from the sample (or a review state with --state: its runs, its look), named
    /// wrapped-&lt;scope&gt;-&lt;state&gt;[-&lt;skin&gt;][-at&lt;t&gt;].png. With KL_MOTION_AT the sheet's sequence is held at that point; without
    /// it the sheet is drawn settled. Nothing is tracked, saved or sent.
    /// </summary>
    public static int ShotWrapped(Settings settings, string dir, string scope, UiState? state, string? skin)
    {
        if (scope is not (Wrapped.Week or Wrapped.Day)) { Console.WriteLine("--wrapped takes week or day"); return 2; }
        Ensure();
        Directory.CreateDirectory(dir);
        var w = new MainWindow(settings, watch: false) { WindowStartupLocation = WindowStartupLocation.Manual, Left = -12000, Top = -12000, ShowActivated = false };
        w.Show();
        if (skin != null) Skins.Apply(skin);
        else if (state != null) Skins.Apply(state.Skin ?? Skins.Default);
        w.ShowDemo("Events", state);
        w.ShowWrappedSample(state);
        var root = w.OpenWrappedForShot(scope);
        IconCache.Drain(TimeSpan.FromSeconds(8));   // the drop's icon, when this PC is online
        DoEvents();
        if (root == null) { w.Close(); Console.WriteLine("the sheet did not open"); return 1; }
        var at = Motion.FrozenAt is double t ? "-at" + t.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture) : "";
        var file = Path.Combine(dir, $"wrapped-{scope}-{state?.Name ?? "demo"}{(skin != null ? "-" + skin : "")}{at}.png");
        Save(root, file, 0, (w.TryFindResource("Bg") as SolidColorBrush)?.Color);
        w.CloseWrapped();
        w.Close();
        Console.WriteLine($"wrote {Path.GetFullPath(file)}");
        return 0;
    }
}
