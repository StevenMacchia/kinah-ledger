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

    /// <summary>The real app lifecycle (window shown, message loop running) with an automatic start → wait → stop, to catch anything that kills the process.</summary>
    public static int Live(Settings settings, DRect? region, int ms)
    {
        var app = Ensure();
        app.DispatcherUnhandledException += (_, e) => { Console.WriteLine("UI exception: " + e.Exception); CrashLog.Write("ui", e.Exception); e.Handled = true; };
        AppDomain.CurrentDomain.UnhandledException += (_, e) => Console.WriteLine("process exception: " + e.ExceptionObject);
        var w = new MainWindow(settings);
        string result = "window closed before the cycle finished";
        w.Loaded += async (_, _) =>
        {
            try
            {
                result = await w.DebugCycle(region, ms);
                await Task.Delay(1500);                // stay alive a moment after Stop: this is where the app vanished for the player
                result += " · app still alive after stop";
            }
            catch (Exception ex) { result = "cycle FAILED: " + ex; }
            w.ForceClose();
        };
        app.Exit += (_, e) => Console.WriteLine($"app exit code {e.ApplicationExitCode}");
        app.Run(w);
        Console.WriteLine(result);
        return result.Contains("still alive") ? 0 : 1;
    }

    public static int Run(Settings settings)
    {
        var app = Ensure();
        // an error in a button handler must not take the whole app down: log it, tell the player, carry on
        app.DispatcherUnhandledException += (_, e) =>
        {
            CrashLog.Write("ui", e.Exception);
            e.Handled = true;
            MessageBox.Show($"Something went wrong, but the tracker is still running.\n\n{e.Exception.Message}\n\nDetails were saved to:\n{CrashLog.CrashPath}", "Kinah Ledger", MessageBoxButton.OK, MessageBoxImage.Warning);
        };
        AppDomain.CurrentDomain.UnhandledException += (_, e) => CrashLog.Write("process", e.ExceptionObject as Exception ?? new Exception(e.ExceptionObject?.ToString()));
        TaskScheduler.UnobservedTaskException += (_, e) => { CrashLog.Write("task", e.Exception); e.SetObserved(); };
        var w = new MainWindow(settings);
        return app.Run(w);
    }

    /// <summary>Starts and stops a session in-process (no clicks needed) and reports any exception, for finding crashes.</summary>
    public static int Cycle(Settings settings, DRect? region, int ms)
    {
        Ensure();
        var w = new MainWindow(settings) { WindowStartupLocation = WindowStartupLocation.Manual, Left = -12000, Top = -12000, ShowActivated = false };
        w.Show();
        DoEvents();
        var task = w.DebugCycle(region, ms);
        var deadline = DateTime.UtcNow.AddMilliseconds(ms + 20000);
        while (!task.IsCompleted && DateTime.UtcNow < deadline) { DoEvents(); Thread.Sleep(20); }
        if (!task.IsCompleted) { Console.WriteLine("cycle did not finish in time"); return 2; }
        if (task.IsFaulted) { Console.WriteLine("cycle FAILED:\n" + task.Exception?.GetBaseException()); return 1; }
        Console.WriteLine("cycle ok: " + task.Result);
        w.Close();
        return 0;
    }

    public static DRect? PickRegion()
    {
        Ensure();
        return RegionPickerWindow.Pick();
    }

    /// <summary>Renders the window (and the panel) off-screen to PNG files so the layout can be reviewed without a desktop session.</summary>
    public static void Shot(Settings settings, string dir, bool demo, string kind = "Open world", bool compact = false)
    {
        Ensure();
        Directory.CreateDirectory(dir);
        var w = new MainWindow(settings, watch: false) { WindowStartupLocation = WindowStartupLocation.Manual, Left = -12000, Top = -12000, ShowActivated = false };
        w.Show();
        if (demo) w.ShowDemo(kind);
        if (compact) w.ShowCompact();
        IconCache.Drain(TimeSpan.FromSeconds(8));   // the demo rows' icons, when this PC is online
        DoEvents();
        var name = !demo ? "ui-main" : kind switch { "Gathering" => "ui-main-gathering", "Pets" => "ui-main-pets", "Market" => "ui-main-market", _ => "ui-main-tracking" };
        Save((FrameworkElement)w.Content, Path.Combine(dir, name + (compact ? "-compact" : "") + ".png"), (compact ? 640 : 800) - 16);
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
