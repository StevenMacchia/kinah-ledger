using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Threading;
using KinahLedger.Packets;

namespace KinahLedger.Ui;

/// <summary>
/// Why the DPS meter can't start, when the player can do something about it (Steven, 2026-11-18, "Yes, add the prompt":
/// his friend's meter never showed and the app never said why). The meter, party, boss fights and the Applicants window
/// all come from the reader (Packets/PacketReader.cs), which needs Npcap, or the tracker run as administrator.
/// Each case is told only when the facts tell it apart from the others; anything else shows nothing new.
/// </summary>
public enum MeterBlock
{
    None,
    /// <summary>Settings › Game reading › Read the game's network traffic is off.</summary>
    Off,
    /// <summary>No Npcap, and not running as administrator.</summary>
    Missing,
    /// <summary>Npcap installed with "Restrict Npcap driver's access to Administrators only", and not running as administrator.</summary>
    AdminsOnly,
    /// <summary>The game has run for minutes with no server connection the reader can follow: a ping booster or VPN, typically.</summary>
    NoServer,
    /// <summary>Npcap was installed while the tracker ran, and opening it fails since: a restart is what is left.</summary>
    Restart,
    /// <summary>
    /// Not a meter case: AION 2 in exclusive fullscreen, so no overlay shows over it (and screen capture can fail). Told
    /// apart from the meter's cases, beside them (MainWindow._fullscreen).
    /// </summary>
    Fullscreen,
}

/// <summary>The facts a <see cref="MeterBlock"/> is decided from, and the checks on this PC behind them.</summary>
public static class MeterBlocks
{
    public const string NpcapUrl = "https://npcap.com/#download";
    /// <summary>How long the game runs with no server connection before the ping booster line shows.</summary>
    public static readonly TimeSpan NoServerAfter = TimeSpan.FromMinutes(3);

    // the checks, swapped by the self-test (an install simulated)
    internal static Func<bool> NpcapInstalled = () => PacketSources.NpcapInstalled;
    internal static Func<bool> IsAdmin = () => Admin.Value;
    internal static Func<bool> AdminOnly = ReadAdminOnly;
    internal static Func<bool> CanElevate = () => Elevatable.Value;
    static readonly Lazy<bool> Admin = new(PacketSources.IsAdmin);
    static readonly Lazy<bool> Elevatable = new(LimitedAdminToken);

    /// <summary>
    /// Renders and tests: KL_NPCAP_MISSING=1 draws the Npcap card (0 draws none); KL_METER_BLOCK=off, missing, admins,
    /// noserver or restart draws that case. Null: decided from this PC.
    /// </summary>
    internal static MeterBlock? Forced = FromEnvironment();

    /// <summary>Renders and tests: KL_FULLSCREEN=1 draws the exclusive fullscreen card, 0 none. Null: seen on this PC.</summary>
    internal static bool? ForcedFullscreen = Environment.GetEnvironmentVariable("KL_FULLSCREEN") switch { "1" => true, "0" => false, _ => null };
    /// <summary>The look at the game (swapped by the self-test): true or false while it is in front, null when that can't be told.</summary>
    internal static Func<IReadOnlyCollection<uint>, bool?> FullscreenProbe = GameExclusiveFullscreen;

    /// <summary>
    /// The second look, which needs the game in front of nothing (2026-11-18, a friend's 1.0.18: the game's window was
    /// 5120x2160 on a 2560x1440 monitor, Windows never said "exclusive", and no overlay showed): a game window larger than
    /// the monitor it is on is exclusive fullscreen at a resolution the monitor doesn't have (a borderless or windowed game
    /// is never larger than its screen). A few pixels of a window's frame are allowed.
    /// </summary>
    internal static bool LargerThanItsScreen(System.Drawing.Rectangle? game)
    {
        if (game is not { Width: > 0, Height: > 0 } g) return false;
        try
        {
            var s = System.Windows.Forms.Screen.FromRectangle(g).Bounds;
            return g.Width > s.Width + 16 || g.Height > s.Height + 16;
        }
        catch (Exception) { return false; }
    }

    internal static MeterBlock? FromEnvironment()
    {
        var b = Environment.GetEnvironmentVariable("KL_METER_BLOCK")?.Trim().ToLowerInvariant();
        if (!string.IsNullOrEmpty(b))
            return b switch { "off" => MeterBlock.Off, "missing" => MeterBlock.Missing, "admins" => MeterBlock.AdminsOnly, "noserver" => MeterBlock.NoServer, "restart" => MeterBlock.Restart, _ => MeterBlock.None };
        return Environment.GetEnvironmentVariable("KL_NPCAP_MISSING") switch { "1" => MeterBlock.Missing, "0" => MeterBlock.None, _ => null };
    }

    /// <summary>
    /// The case, from the facts. A running reader blocks nothing. Off is the player's own switch. Without administrator, a
    /// missing Npcap and an admins-only one are read off the PC (its folder, its own registry setting). Restart only after
    /// an install seen in this run and an open that failed since. The booster line only after minutes of the game running
    /// with no server connection the reader can follow.
    /// </summary>
    public static MeterBlock Decide(bool enabled, bool on, bool installed, bool admin, bool adminOnly, string state, TimeSpan? noServerFor, bool installedThisRun)
    {
        if (on) return MeterBlock.None;
        if (!enabled) return MeterBlock.Off;
        if (!admin && !installed) return MeterBlock.Missing;
        if (!admin && adminOnly) return MeterBlock.AdminsOnly;
        if (installedThisRun && state.StartsWith("Off: Npcap: ", StringComparison.Ordinal)) return MeterBlock.Restart;
        if (noServerFor is { } f && f >= NoServerAfter) return MeterBlock.NoServer;
        return MeterBlock.None;
    }

    /// <summary>Npcap's own setting for "Restrict Npcap driver's access to Administrators only" (any user may read it).</summary>
    static bool ReadAdminOnly()
    {
        try
        {
            using var k = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\npcap\Parameters");
            return k?.GetValue("AdminOnly") is int v && v != 0;
        }
        catch (Exception) { return false; }
    }

    [DllImport("shell32.dll")] static extern int SHQueryUserNotificationState(out int state);
    [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint pid);

    /// <summary>
    /// Whether AION 2 runs in exclusive fullscreen: only told while the game's own window is in front, and only from Windows'
    /// own answer for "a Direct3D app holds the screen in exclusive mode" (QUNS_RUNNING_D3D_FULL_SCREEN). False when the game
    /// is closed; null when it is behind another window (exclusive fullscreen minimises on Alt+Tab, so nothing can be told then).
    /// </summary>
    static bool? GameExclusiveFullscreen(IReadOnlyCollection<uint> pids)
    {
        if (pids.Count == 0) return false;
        try
        {
            GetWindowThreadProcessId(GetForegroundWindow(), out var pid);
            if (!pids.Contains(pid)) return null;
            if (SHQueryUserNotificationState(out var state) != 0) return null;
            return state == 3;   // QUNS_RUNNING_D3D_FULL_SCREEN
        }
        catch (Exception) { return null; }
    }

    [DllImport("advapi32.dll", SetLastError = true)]
    static extern bool GetTokenInformation(IntPtr token, int cls, out int info, int length, out int returned);

    /// <summary>
    /// True when this user is an administrator running with UAC's limited token: "run as administrator" then starts the
    /// tracker as the same user (same settings and history). A standard user would start it as another account, so no button.
    /// </summary>
    static bool LimitedAdminToken()
    {
        try
        {
            using var id = WindowsIdentity.GetCurrent();
            return GetTokenInformation(id.Token, 18 /* TokenElevationType */, out var type, 4, out _) && type == 3 /* TokenElevationTypeLimited */;
        }
        catch (Exception) { return false; }
    }
}

public partial class MainWindow
{
    MeterBlock _meterBlock;
    bool _npcapWasMissing, _meterWatch;
    /// <summary>AION 2 seen in exclusive fullscreen, kept while it is behind other windows; cleared when seen otherwise, or closed.</summary>
    bool _fullscreen, _fullscreenTold;
    DispatcherTimer? _meterBlockTick;
    /// <summary>For the self-test: a demo window decides from the (swapped) checks instead of showing nothing.</summary>
    internal bool MeterBlockLiveForTest;
    internal int MeterRetriesForTest;
    internal MeterBlock MeterBlockShown => _meterBlock;

    /// <summary>Once, from the constructor: the case now, and a look every 3 seconds while the window watches the game.</summary>
    void InitMeterBlock(bool watch)
    {
        _meterWatch = watch;
        _meterBlock = MeterBlockNow();
        _fullscreen = FullscreenNow();
        if (!watch) return;
        _meterBlockTick = new DispatcherTimer(TimeSpan.FromSeconds(3), DispatcherPriority.Background, (_, _) => CheckMeterBlock(), Dispatcher);
        _meterBlockTick.Start();
        Closed += (_, _) => _meterBlockTick?.Stop();
    }

    internal MeterBlock MeterBlockNow()
    {
        if (MeterBlocks.Forced is { } forced) return forced;
        if ((_demo || !_meterWatch) && !MeterBlockLiveForTest) return MeterBlock.None;   // a render or a test window reads nothing
        try
        {
            var pr = _watcher.Packets;
            bool installed = MeterBlocks.NpcapInstalled();
            if (!installed) _npcapWasMissing = true;
            return MeterBlocks.Decide(_prefs.PacketReading, pr.On, installed, MeterBlocks.IsAdmin(), installed && MeterBlocks.AdminOnly(), pr.State,
                pr.NoServerSince is { } s ? DateTime.UtcNow - s : null, _npcapWasMissing && installed);
        }
        catch (Exception ex) { CrashLog.Write("meter block", ex); return MeterBlock.None; }
    }

    bool FullscreenNow()
    {
        if (MeterBlocks.ForcedFullscreen is { } forced) return forced;
        if ((_demo || !_meterWatch) && !MeterBlockLiveForTest) return false;
        try
        {
            if (MeterBlocks.LargerThanItsScreen(_watcher.GameRect)) return true;
            return MeterBlocks.FullscreenProbe(Ocr.GameWindow.Pids) ?? _fullscreen;
        }
        catch (Exception ex) { CrashLog.Write("fullscreen check", ex); return _fullscreen; }
    }

    /// <summary>
    /// The look every few seconds: when the case changes, every place that tells it is redrawn. Npcap just installed (or set
    /// open to everyone): the reader tries again at once instead of after its minute's wait, so the meter starts without a restart.
    /// </summary>
    internal void CheckMeterBlock()
    {
        var fs = FullscreenNow();
        if (fs != _fullscreen)
        {
            _fullscreen = fs;
            CrashLog.Note(fs ? "the game is in exclusive fullscreen: the overlays can't show over it" : "the game is out of exclusive fullscreen");
            // the overlays can't say it while hidden: the tray once a session (Windows holds it back until the game is left)
            if (fs && !_fullscreenTold && _meterWatch && !_demo)
            {
                _fullscreenTold = true;
                var (t, l) = MeterBlockWords(MeterBlock.Fullscreen);
                try { _tray?.ShowBalloonTip(10000, t, l, System.Windows.Forms.ToolTipIcon.Warning); } catch (Exception ex) { CrashLog.Write("fullscreen notice", ex); }
            }
            if (MeterBlockNow() == _meterBlock) { MeterBlockChanged(); return; }
        }
        var b = MeterBlockNow();
        if (b == _meterBlock) return;
        var was = _meterBlock;
        _meterBlock = b;
        if (was is MeterBlock.Missing or MeterBlock.AdminsOnly && b is not (MeterBlock.Missing or MeterBlock.AdminsOnly))
        {
            MeterRetriesForTest++;
            _watcher.Packets.RetryNow();
            CrashLog.Note($"meter: {was} cleared, the reader tries again now");
        }
        MeterBlockChanged();
    }

    void MeterBlockChanged()
    {
        RedrawParserPage();
        DrawWelcomeMeterBlock();
        DrawFullscreenCard();
        DrawOverlayPills();
        _options?.MeterBlockChanged();
    }

    /// <summary>The card's head and its one line, in the player's language.</summary>
    internal static (string Title, string Line) MeterBlockWords(MeterBlock b) => b switch
    {
        MeterBlock.Missing => (L.T("The DPS meter needs Npcap"), L.T("A free Windows helper (npcap.com) that lets Kinah Ledger read your fights, party and boss. Install it with the default options, then come back.")),
        MeterBlock.AdminsOnly => (L.T("Npcap is set to admins only"), L.T("Reinstall it with 'Restrict Npcap driver's access to Administrators only' unticked, or run Kinah Ledger as administrator.")),
        MeterBlock.NoServer => (L.T("Not seeing your game's connection"), L.T("If you use a ping booster or VPN, try one session without it.")),
        MeterBlock.Off => (L.T("The DPS meter is off"), L.T("Turned off in Settings › Game reading.")),
        MeterBlock.Restart => (L.T("Restart Kinah Ledger to start the meter"), L.T("Npcap is installed. One restart and the meter starts.")),
        MeterBlock.Fullscreen => (L.T("AION 2 is in exclusive fullscreen"), L.T("The overlays can't show over it. In the game's graphics settings, set Display mode to Borderless.")),
        _ => ("", ""),
    };

    /// <summary>The Overlays menu's first line while the game is in exclusive fullscreen: the whole sentence, nothing to click.</summary>
    internal MenuEntry? FullscreenMenuEntry() => _fullscreen
        ? new MenuEntry(L.T("AION 2 is in exclusive fullscreen, so the overlays can't show over it. In the game's graphics settings, set Display mode to Borderless."), Warn: true)
        : null;

    /// <summary>The main window's card while the game is in exclusive fullscreen, on every page (first run has its own place).</summary>
    void DrawFullscreenCard()
    {
        if (FullscreenHost == null) return;
        bool show = _fullscreen && !_welcome;
        FullscreenHost.Child = show ? MeterBlockCard(MeterBlock.Fullscreen, welcome: true) : null;
        FullscreenHost.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
    }
    internal bool FullscreenShownForTest => FullscreenHost.Visibility == Visibility.Visible && FullscreenHost.Child != null;

    /// <summary>The Overlays menu's line and Settings' line: what is wrong in a few words, and the link that acts on it.</summary>
    internal static (string Text, string Link) MeterBlockShort(MeterBlock b) => b switch
    {
        MeterBlock.Missing => (L.T("Needs Npcap"), L.T("Get it")),
        MeterBlock.AdminsOnly => (L.T("Npcap is set to admins only"), L.T("Fix it")),
        MeterBlock.NoServer => (L.T("Not seeing your game's connection"), L.T("Why")),
        MeterBlock.Off => (L.T("Turned off in Settings › Game reading"), L.T("Open")),
        MeterBlock.Restart => (L.T("Restart to start the meter"), L.T("Restart")),
        _ => ("", ""),
    };

    /// <summary>What the menu's link does: the download, the Parser page's card (it says how), Settings, or the restart.</summary>
    void MeterBlockAct(MeterBlock b)
    {
        switch (b)
        {
            case MeterBlock.Missing: Open(MeterBlocks.NpcapUrl); break;
            case MeterBlock.AdminsOnly or MeterBlock.NoServer: ShowPart("Parser"); break;
            case MeterBlock.Off: OpenOptions(OptionsWindow.Page.Reading); break;
            case MeterBlock.Restart: _ = RestartTracker(admin: false); break;
        }
    }

    /// <summary>The Overlays menu's line under the Parser and Applicants switches, or null when nothing blocks them.</summary>
    internal MenuEntry? MeterBlockMenuEntry()
    {
        var b = _meterBlock;
        if (b == MeterBlock.None) return null;
        var (text, link) = MeterBlockShort(b);
        return new MenuEntry(text, () => MeterBlockAct(b), Tip: MeterBlockWords(b).Line, Link: link);
    }

    /// <summary>
    /// The card at the top of the Parser page (and, for an Npcap case, in first run): high-contrast, compact, the head and one
    /// line on the left, what to do on the right. Null when nothing blocks the meter.
    /// </summary>
    internal FrameworkElement? MeterBlockCard(MeterBlock b, bool welcome = false)
    {
        if (b == MeterBlock.None) return null;
        var (title, line) = MeterBlockWords(b);
        var card = new Border
        {
            CornerRadius = new CornerRadius(10), BorderThickness = new Thickness(1), Padding = new Thickness(14, 11, 14, 11),
            Margin = welcome ? new Thickness(0, 14, 0, 0) : new Thickness(0, 0, 0, 10), Tag = MeterBlockTag,
        };
        card.SetResourceReference(Border.BackgroundProperty, "Card2");
        card.SetResourceReference(Border.BorderBrushProperty, "Warn");
        AutomationProperties.SetName(card, title);
        var g = new Grid();
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var dot = new Border { Width = 8, Height = 8, CornerRadius = new CornerRadius(4), Margin = new Thickness(0, 6, 10, 0), VerticalAlignment = VerticalAlignment.Top };
        dot.SetResourceReference(Border.BackgroundProperty, "Warn");
        g.Children.Add(dot);
        var words = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        words.Children.Add(PT(title, 14, FontWeights.SemiBold, "Ink"));
        var l = PT(line, 12.5, FontWeights.Medium, "Text2"); l.TextWrapping = TextWrapping.Wrap; l.Margin = new Thickness(0, 3, 0, 0);
        words.Children.Add(l);
        Grid.SetColumn(words, 1); g.Children.Add(words);

        var acts = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(14, 0, 0, 0) };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        Button Btn(string text, bool primary, Action act, string? tag = null)
        {
            var bt = new Button { Content = text, Height = 32, FontSize = 13, Padding = new Thickness(16, 0, 16, 0), Margin = new Thickness(buttons.Children.Count > 0 ? 8 : 0, 0, 0, 0), Tag = tag };
            if (primary) bt.SetResourceReference(StyleProperty, "PrimaryButton");
            AutomationProperties.SetName(bt, text);
            bt.Click += (_, _) => act();
            buttons.Children.Add(bt);
            return bt;
        }
        switch (b)
        {
            case MeterBlock.Missing:
                Btn(L.T("Get Npcap"), true, () => Open(MeterBlocks.NpcapUrl), MeterBlocks.NpcapUrl);
                break;
            case MeterBlock.AdminsOnly:
                Btn(L.T("Get Npcap"), true, () => Open(MeterBlocks.NpcapUrl), MeterBlocks.NpcapUrl);
                if (MeterBlocks.CanElevate() && !welcome) Btn(L.T("Restart as administrator"), false, () => _ = RestartTracker(admin: true), "runas");
                break;
            case MeterBlock.Off:
                Btn(L.T("Open Settings"), true, () => OpenOptions(OptionsWindow.Page.Reading));
                break;
            case MeterBlock.Restart:
                Btn(L.T("Restart"), true, () => _ = RestartTracker(admin: false), "restart");
                break;
        }
        if (buttons.Children.Count > 0) acts.Children.Add(buttons);
        if (b is MeterBlock.Missing or MeterBlock.AdminsOnly)
        {
            var again = QLinkify(PT(L.T("Check again"), 12, FontWeights.SemiBold, "Link"));
            again.HorizontalAlignment = HorizontalAlignment.Right; again.Margin = new Thickness(0, 6, 2, 0);
            AutomationProperties.SetName(again, "Check again");
            again.MouseLeftButtonUp += (_, _) => CheckAgain();
            acts.Children.Add(again);
        }
        if (acts.Children.Count > 0) { Grid.SetColumn(acts, 2); g.Children.Add(acts); }
        card.Child = g;
        return card;
    }

    internal const string MeterBlockTag = "meterblock";
    /// <summary>The card on the Parser page (Ui/MainWindow.Parser.cs sets it), for the self-test.</summary>
    internal FrameworkElement? _meterCardForTest;

    /// <summary>Check again: the same look the timer makes, at once; the reader tries again too.</summary>
    void CheckAgain()
    {
        var was = _meterBlock;
        CheckMeterBlock();
        if (_meterBlock == was) { _watcher.Packets.RetryNow(); Say(L.T("No change yet."), "Warn"); }
    }

    /// <summary>
    /// Restart (the update's restart path: the new copy waits for this one to let go), or as administrator through Windows'
    /// own prompt. A run in progress is stopped and saved first, after asking. A declined prompt leaves everything as it was.
    /// </summary>
    async Task RestartTracker(bool admin)
    {
        if (_exe == null) return;
        if (Farming)
        {
            var answer = MessageBox.Show(this, L.T("Restart Kinah Ledger now?\n\nYour run stops first and is saved (and shared, if sharing is on) as usual."),
                "Kinah Ledger", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (answer != MessageBoxResult.Yes) return;
            await StopTracking();
        }
        try
        {
            if (admin) Process.Start(new ProcessStartInfo(_exe, AfterUpdateArg) { UseShellExecute = true, Verb = "runas", WorkingDirectory = Path.GetDirectoryName(_exe) ?? "" });
            else Update.Updater.Relaunch(_exe, AfterUpdateArg);
        }
        catch (Win32Exception) { return; }   // Windows' prompt declined
        catch (Exception ex) { CrashLog.Write("restart", ex); return; }
        _quitting = true;
        Close();
    }

    /// <summary>Settings' Restart link: Settings closes with the window.</summary>
    internal void RestartFromOptions() => _ = RestartTracker(admin: false);

    /// <summary>First run's look step: the Npcap card, when Npcap is what blocks the meter. Done carries on either way.</summary>
    void DrawWelcomeMeterBlock()
    {
        if (WelcomeMeterHost == null) return;
        var cards = new StackPanel();
        if (_fullscreen) cards.Children.Add(MeterBlockCard(MeterBlock.Fullscreen, welcome: true));
        if (_meterBlock is MeterBlock.Missing or MeterBlock.AdminsOnly) cards.Children.Add(MeterBlockCard(_meterBlock, welcome: true));
        bool show = cards.Children.Count > 0;
        WelcomeMeterHost.Child = show ? cards : null;
        WelcomeMeterHost.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
    }

    /* ---------------- self-tests ---------------- */

    /// <summary>The Parser page's card: its title, its buttons (text, tag) and whether Check again is there; null when none shows.</summary>
    internal (string Title, List<(string Text, string? Tag)> Buttons, bool CheckAgain)? MeterCardForTest()
    {
        if (_meterCardForTest is not { } card || !ParserPage.Children.Contains(card)) return null;
        var texts = new List<TextBlock>(); var buttons = new List<Button>();
        void Walk(DependencyObject d)
        {
            if (d is TextBlock t) texts.Add(t);
            if (d is Button bt) { buttons.Add(bt); return; }
            foreach (var c in LogicalTreeHelper.GetChildren(d).OfType<DependencyObject>()) Walk(c);
        }
        Walk(card);
        return (texts.FirstOrDefault()?.Text ?? "", buttons.Select(x => (x.Content as string ?? "", x.Tag as string)).ToList(), texts.Any(t => AutomationProperties.GetName(t) == "Check again"));
    }
    internal void ClickMeterButtonForTest(string? tag)
    {
        if (_meterCardForTest == null) return;
        Button? found = null;
        void Walk(DependencyObject d) { if (d is Button bt && bt.Tag as string == tag) found = bt; foreach (var c in LogicalTreeHelper.GetChildren(d).OfType<DependencyObject>()) Walk(c); }
        Walk(_meterCardForTest);
        found?.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
    }
    internal ScrollViewer LowerScrollForTest => LowerScroll;
    internal int WelcomeMeterCardsForTest => WelcomeMeterHost.Visibility == Visibility.Visible && WelcomeMeterHost.Child is StackPanel sp ? sp.Children.Count : 0;
    internal bool WelcomeMeterShownForTest => WelcomeMeterCardsForTest > 0;
}
