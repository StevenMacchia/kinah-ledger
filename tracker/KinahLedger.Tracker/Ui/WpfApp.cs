using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using DRect = System.Drawing.Rectangle;

namespace KinahLedger.Ui;

/// <summary>Starts the WPF side without an App.xaml, so Program.Main stays the single entry point.</summary>
public static class WpfApp
{
    static Application Ensure()
    {
        if (Application.Current != null) return Application.Current;
        var app = new Application { ShutdownMode = ShutdownMode.OnMainWindowClose };
        app.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("/KinahLedger;component/Ui/Theme.xaml", UriKind.Relative) });
        return app;
    }

    public static int Run(Settings settings)
    {
        var app = Ensure();
        var w = new MainWindow(settings);
        return app.Run(w);
    }

    public static DRect? PickRegion()
    {
        Ensure();
        return RegionPickerWindow.Pick();
    }

    /// <summary>Renders the window (and the panel) off-screen to PNG files so the layout can be reviewed without a desktop session.</summary>
    public static void Shot(Settings settings, string dir, bool demo)
    {
        Ensure();
        Directory.CreateDirectory(dir);
        var w = new MainWindow(settings) { WindowStartupLocation = WindowStartupLocation.Manual, Left = -12000, Top = -12000, ShowActivated = false };
        w.Show();
        if (demo) w.ShowDemo();
        DoEvents();
        Save((FrameworkElement)w.Content, Path.Combine(dir, demo ? "ui-main-tracking.png" : "ui-main.png"), 800 - 16);
        w.Close();

        var h = new HudWindow { Left = -12000, Top = -12000, ShowActivated = false };
        h.Show();
        h.Update("938K Kinah/hr", "229 kills/hr · 156 drops/hr · 4.1K Kinah/kill\n00:42:17 · 661K Kinah · 110 drops · 161 kills", "Acquired Thick hide ×2.");
        DoEvents();
        Save((FrameworkElement)h.Content, Path.Combine(dir, "ui-hud.png"), 0);
        h.Close();
    }

    /// <summary>Lets pending layout and render work run, since no message loop is running in this mode.</summary>
    static void DoEvents()
    {
        var frame = new System.Windows.Threading.DispatcherFrame();
        System.Windows.Threading.Dispatcher.CurrentDispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.ContextIdle, new Action(() => frame.Continue = false));
        System.Windows.Threading.Dispatcher.PushFrame(frame);
    }

    static void Save(FrameworkElement root, string path, double fallbackWidth)
    {
        if (root.ActualWidth < 1 || root.ActualHeight < 1)
        {
            root.Measure(new Size(fallbackWidth > 0 ? fallbackWidth : double.PositiveInfinity, double.PositiveInfinity));
            root.Arrange(new Rect(root.DesiredSize));
            root.UpdateLayout();
        }
        int wpx = (int)Math.Ceiling(root.ActualWidth), hpx = (int)Math.Ceiling(root.ActualHeight);
        if (wpx < 1 || hpx < 1) throw new InvalidOperationException("the window did not lay out");
        var rtb = new RenderTargetBitmap(wpx, hpx, 96, 96, PixelFormats.Pbgra32);
        rtb.Render(root);
        var enc = new PngBitmapEncoder();
        enc.Frames.Add(BitmapFrame.Create(rtb));
        using var fs = File.Create(path);
        enc.Save(fs);
    }
}
