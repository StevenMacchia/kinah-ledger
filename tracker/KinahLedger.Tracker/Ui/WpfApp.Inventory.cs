using System.IO;
using System.Windows;

namespace KinahLedger.Ui;

public static partial class WpfApp
{
    /// <summary>
    /// ui-shot --inventory: the Inventory &amp; gear window (Ui/MainWindow.Inventory.cs) over a sample bag and gear, and before the
    /// game sent one (inventory-known.png, inventory-unknown.png). Nothing is read, saved or sent.
    /// </summary>
    public static void ShotInventory(Settings settings, string dir, string? skin)
    {
        Ensure();
        Directory.CreateDirectory(dir);
        var w = new MainWindow(settings, watch: false) { WindowStartupLocation = WindowStartupLocation.Manual, Left = -12000, Top = -12000, ShowActivated = false };
        w.Show();
        if (skin != null) Skins.Apply(skin);
        DoEvents();
        w.ShotInventory(dir, skin);
        w.Close();
    }
}
