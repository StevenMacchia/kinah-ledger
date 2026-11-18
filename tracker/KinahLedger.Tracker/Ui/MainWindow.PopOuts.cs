using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Threading;
using KinahLedger.Parser;
using KinahLedger.Session;

namespace KinahLedger.Ui;

/// <summary>
/// The pop-out windows' reach (Steven, 2026-11-18: "Yes, build pop-outs"): the DPS meter (Ui/MeterPopOut.cs) and the
/// Applicants list (Ui/ApplicantsPopOut.cs), each in a desktop window for a second screen, for a game in exclusive fullscreen.
/// Each is apart from its overlay: either, both or neither can be on. They open from the header's Overlays menu (a switch
/// under each overlay's), the Parser page's head (Pop out), and Settings › DPS meter and › Applicants (Open in its own window).
/// Whether each was open is kept (popouts.json): open when the tracker quit, it opens again at start; closing one is just
/// closing it. They never hide for the game: no check of the game being in front, covered or minimized reaches them.
/// The hooks into the rest of the window: <see cref="MeterPopOutTick"/> from the Parser overlay's tick (the fight it would
/// show), <see cref="MeterPopOutClock"/> from the meter's clock, <see cref="AddPopOutEntries"/> from the Overlays menu.
/// </summary>
public partial class MainWindow
{
    PopOutPrefs _popPrefs = PopOutPrefs.Defaults();
    MeterPopOut? _meterPop;
    ApplicantsPopOut? _applicantsPop;
    DispatcherTimer? _applicantsPopTick;
    /// <summary>The tracker is quitting: the pop-outs close but stay "open" in popouts.json, to open again at start.</summary>
    bool _popOutsQuitting;
    /// <summary>The fight the Parser overlay's last tick would show (null: none), for a pop-out opened between ticks.</summary>
    Encounter? _meterPopFeed;

    /// <summary>Once, as the window opens: for a watching window the choices, and the pop-outs left open last time opened again.</summary>
    void InitPopOuts(bool watch)
    {
        if (!watch) return;
        _popPrefs = PopOutPrefs.Load();
        Closed += (_, _) => ClosePopOutsForQuit();
        // once the message loop runs (the window may start in the tray, never shown)
        Dispatcher.BeginInvoke(DispatcherPriority.Background, ReopenPopOuts);
    }

    void ReopenPopOuts()
    {
        try
        {
            if (_popPrefs.Meter.Open) OpenMeterPopOut();
            if (_popPrefs.Applicants.Open) OpenApplicantsPopOut();
        }
        catch (Exception ex) { CrashLog.Write("pop-outs at start", ex); }
    }

    /// <summary>The tracker quits: each pop-out closes, still marked open, so the next start brings it back.</summary>
    void ClosePopOutsForQuit()
    {
        _popOutsQuitting = true;
        try { _meterPop?.Close(); _applicantsPop?.Close(); }
        finally { _popOutsQuitting = false; }
    }

    // ---- the DPS meter ----

    internal bool MeterPopOutOpen => _meterPop != null;

    /// <summary>The meter in its own window: opened (where it sat last), or brought forward when it is open.</summary>
    internal void OpenMeterPopOut()
    {
        if (_meterPop == null)
        {
            var w = new MeterPopOut(this, _popPrefs.Meter, _popPrefs.Save);
            w.ShareAsked += ShareFromMeterPopOut;
            w.Closed += (_, _) =>
            {
                if (ReferenceEquals(_meterPop, w)) _meterPop = null;
                if (!_popOutsQuitting) { _popPrefs.Meter.Open = false; _popPrefs.Save(); }
                RedrawParserPage();
            };
            var (radius, pad) = HudWindow.FrameShape(HudFields.Parse(_prefs.HudFields));
            w.SetFrame(radius, pad);
            _meterPop = w;
            _popPrefs.Meter.Open = true; _popPrefs.Save();
            DrawMeterPopOut(_meterPopFeed, ParserNow);
            RedrawParserPage();
        }
        _meterPop.Open();
        if (!_demo) ParserOverlayTick();   // the fight shown now, at once
    }

    internal void CloseMeterPopOut() => _meterPop?.Close();
    void ToggleMeterPopOut() { if (_meterPop != null) CloseMeterPopOut(); else OpenMeterPopOut(); }

    /// <summary>
    /// The Parser overlay's tick (ParserOverlayTick), with the fight it would show (null: none): the pop-out draws it whether the
    /// overlay is on or not, and whether or not the game is in front.
    /// </summary>
    void MeterPopOutTick(Encounter? show, DateTime now)
    {
        _meterPopFeed = show;
        if (_meterPop != null) DrawMeterPopOut(show, now);
    }

    /// <summary>What the pop-out shows: the overlay's fight, else the last boss pull kept, in the meter's look.</summary>
    void DrawMeterPopOut(Encounter? show, DateTime now)
    {
        if (_meterPop is not { } w) return;
        try
        {
            var e = show ?? MeterPopOutLastPull();
            w.Look = MeterShareLook().Key;   // the look the meter is drawn in over the game
            w.Mode = ParserModeNow; w.ShowDetails = PDetails;
            w.Draw(e, e is { Live: true } ? now : e?.End ?? e?.LastAt ?? now, Shareable(e));
        }
        catch (Exception ex) { CrashLog.Write("meter pop-out", ex); }
    }

    /// <summary>With nothing on the meter: the pull that ended last (this session's, else the newest kept on this PC).</summary>
    Encounter? MeterPopOutLastPull()
    {
        if (Shareable(_parserJustEnded)) return _parserJustEnded;
        if (ParserFights?.LastEnded is { OpenWorld: false } le && Shareable(le)) return le;
        return ParserPast().LastOrDefault(Shareable);
    }

    /// <summary>The meter's clock (20 times a second): a live fight in the pop-out drawn again as the time passes between hits.</summary>
    void MeterPopOutClock(DateTime now)
    {
        if (_demo || _meterPop is not { IsVisible: true } w || w.Shown is not { Live: true, End: null } e) return;
        try { w.Draw(e, now, Shareable(e)); }
        catch (Exception ex) { CrashLog.Write("meter pop-out clock", ex); }
    }

    /// <summary>Share in the pop-out's head: the picture of the fight it shows, said in its head.</summary>
    void ShareFromMeterPopOut()
    {
        var e = _meterPop?.Shown;
        bool can = Shareable(e);
        bool ok = ShareMeter(e);
        _meterPop?.Flash(ok ? L.T("Picture copied") : can ? L.T("Couldn't copy the picture") : L.T("No fight to share yet. Pull a boss and the picture fills in."), ok);
    }

    /// <summary>The Parser page's head: Pop out, the meter in its own window.</summary>
    FrameworkElement MeterPopOutButton()
    {
        var b = new Button { Content = L.T("Pop out"), Height = 26, FontSize = 12.5, Padding = new Thickness(12, 0, 12, 0), Margin = new Thickness(0, 0, 14, 0), VerticalAlignment = VerticalAlignment.Center,
            ToolTip = Tip(L.T("The meter in its own window, for a second screen. Works with the game in fullscreen.")) };   // only with Explanations on hover, as the page's other tips
        AutomationProperties.SetName(b, "Pop out the DPS meter");
        b.Click += (_, _) => OpenMeterPopOut();
        _meterPopOutBtn = b;
        return b;
    }
    Button? _meterPopOutBtn;

    // ---- the Applicants list ----

    internal bool ApplicantsPopOutOpen => _applicantsPop != null;

    /// <summary>The Applicants list in its own window: opened (where it sat last), or brought forward; drawn each second while open.</summary>
    internal void OpenApplicantsPopOut()
    {
        if (_applicantsPop == null)
        {
            var w = new ApplicantsPopOut(this, _popPrefs.Applicants, _popPrefs.Save);
            w.Closed += (_, _) =>
            {
                if (ReferenceEquals(_applicantsPop, w)) { _applicantsPop = null; _applicantsPopTick?.Stop(); }
                if (!_popOutsQuitting) { _popPrefs.Applicants.Open = false; _popPrefs.Save(); }
            };
            _applicantsPop = w;
            _popPrefs.Applicants.Open = true; _popPrefs.Save();
            ApplicantsPopOutTick(DateTime.UtcNow);
            _applicantsPopTick ??= new DispatcherTimer(TimeSpan.FromSeconds(1), DispatcherPriority.Background, (_, _) => ApplicantsPopOutTick(DateTime.UtcNow), Dispatcher);
            _applicantsPopTick.Start();
        }
        _applicantsPop.Open();
    }

    internal void CloseApplicantsPopOut() => _applicantsPop?.Close();
    void ToggleApplicantsPopOut() { if (_applicantsPop != null) CloseApplicantsPopOut(); else OpenApplicantsPopOut(); }

    /// <summary>A second's work while it is open: whoever is waiting, in the overlay theme in use (apart from the overlay's own switch).</summary>
    void ApplicantsPopOutTick(DateTime now)
    {
        if (_applicantsPop is not { } w) return;
        IReadOnlyList<PartyApplicants.Applicant> items;
        try { items = ApplicantsBoard.Pending(now); }
        catch (Exception ex) { CrashLog.Write("applicants pop-out", ex); items = Array.Empty<PartyApplicants.Applicant>(); }
        try { w.Draw(items, now, HudThemes.Of(HudLook)); }
        catch (Exception ex) { CrashLog.Write("applicants pop-out", ex); }
    }

    // ---- the Overlays menu ----

    /// <summary>The Overlays menu's pop-out switches: the meter's under the Parser overlay's, the list's under the Applicants overlay's.</summary>
    void AddPopOutEntries(List<MenuEntry> list)
    {
        var sub = L.T("Its own window, for a second screen");
        var meter = new MenuEntry(L.T("Pop out the DPS meter"), ToggleMeterPopOut, Sub: sub, On: MeterPopOutOpen);
        int i = list.FindIndex(e => e.Text == L.F("{0} overlay", L.T("Parser")));
        list.Insert(i >= 0 ? i + 1 : list.Count, meter);
        var apps = new MenuEntry(L.T("Pop out Applicants"), ToggleApplicantsPopOut, Sub: sub, On: ApplicantsPopOutOpen);
        int j = list.FindIndex(e => e.Text == L.F("{0} overlay", L.T("Applicants")));
        list.Insert(j >= 0 ? j + 1 : list.Count, apps);
    }

    /// <summary>A pop-out's switch in the Overlays menu (the self-test counts the overlays' own switches apart from them).</summary>
    internal static bool IsPopOutEntry(MenuEntry e) => e.Text == L.T("Pop out the DPS meter") || e.Text == L.T("Pop out Applicants");

    // ---- self-tests and renders ----

    internal MeterPopOut? MeterPopOutForTest => _meterPop;
    internal ApplicantsPopOut? ApplicantsPopOutForTest => _applicantsPop;
    internal PopOutPrefs PopOutPrefsForTest { get => _popPrefs; set => _popPrefs = value; }
    internal Button? MeterPopOutButtonForTest => _meterPopOutBtn;
    internal void ClosePopOutsForQuitForTest() => ClosePopOutsForQuit();
    internal void ReopenPopOutsForTest() => ReopenPopOuts();
    internal void ApplicantsPopOutTickForTest(DateTime now) => ApplicantsPopOutTick(now);
    /// <summary>For a render: the pop-out drawn on a given fight (null: the quiet line), whatever the Parser has.</summary>
    internal void DrawMeterPopOutForTest(Encounter? e, DateTime now)
    {
        if (_meterPop is not { } w) return;
        w.Look = MeterShareLook().Key; w.Mode = ParserModeNow; w.ShowDetails = PDetails;
        w.Draw(e, now, Shareable(e));
    }
    internal void SetParserLookForTest(string look) { _parserPrefs.Look = look; _parserPrefs.InKinah = false; }

    /// <summary>
    /// For the options test: one Parser overlay tick on <paramref name="parser"/> with the overlay on or off (its own window, the
    /// game not in front: nothing shown on any screen). Whether the overlay's own window was wanted, and the fight the pop-out drew.
    /// </summary>
    internal (bool OverlayWanted, Encounter? PopOut) MeterPopOutTickForTest(BossParser parser, bool overlayOn, Action? between = null)
    {
        var (hud0, demo0, src0, force0, prefs0) = (_hud, _demo, ParserSourceForTest, ParserOverlayForceForTest, (_parserPrefs.AlwaysOn, _parserPrefs.Overlay, _parserPrefs.InKinah));
        try
        {
            _hud = null; _demo = false; ParserSourceForTest = parser; ParserOverlayForceForTest = false;
            _parserPrefs.AlwaysOn = false; _parserPrefs.Overlay = overlayOn; _parserPrefs.InKinah = false;
            _parserOwnWanted = false;
            ParserOverlayTick();
            if (between != null) { between(); _parserOwnWanted = false; ParserOverlayTick(); }
            return (_parserOwnWanted, _meterPop?.Shown);
        }
        finally
        {
            (_hud, _demo, ParserSourceForTest, ParserOverlayForceForTest) = (hud0, demo0, src0, force0);
            (_parserPrefs.AlwaysOn, _parserPrefs.Overlay, _parserPrefs.InKinah) = prefs0;
            _parserOverlay?.ShowWanted(false);
        }
    }
    internal void ResetParserHeldForTest() { _parserLastLiveId = ""; _parserJustEnded = null; }
    /// <summary>For the self-test: past the first-run screen (a test window starts from a new install's prefs), so the pages draw.</summary>
    internal void SkipWelcomeForTest() => SetWelcome(false);
}
