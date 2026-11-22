using System.Windows;
using System.Windows.Threading;
using KinahLedger.Session;

namespace KinahLedger.Ui;

/// <summary>
/// The Applicants window's reach (Steven, 2026-11-18: "a pop up window for party leaders ... to display the people who are
/// asking to be invited"): one tick a second reads the people waiting (PacketReader.Applicants) and shows them in their own
/// overlay (Ui/ApplicantsOverlay.cs) in the overlay theme in use, or folds it away when nobody is waiting. Its switch (on by
/// default), its spot and click-through are in Settings › Applicants (Ui/OptionsWindow.Applicants.cs) and the header's
/// Overlays menu; they save as they change (applicants.json).
/// </summary>
public partial class MainWindow
{
    ApplicantPrefs _applicantPrefs = ApplicantPrefs.Defaults();
    ApplicantsOverlay? _applicantsOverlay;
    DispatcherTimer? _applicantsTick;
    /// <summary>A render's or a test's own list in place of the reader's (null: the reader's).</summary>
    PartyApplicants? _applicantsForShot;

    PartyApplicants ApplicantsBoard => _applicantsForShot ?? _watcher.Packets.Applicants;

    /// <summary>Once, as the window opens: the Overlays entry; for a watching window the settings and the tick.</summary>
    void InitApplicants(bool watch)
    {
        RegisterOverlay("Applicants", () => ApplicantsOn, on => SetApplicantsOverlay(on), L.T("People asking to join your group, while someone waits. Set up in Settings › Applicants."));
        if (!watch) return;
        _applicantPrefs = ApplicantPrefs.Load();
        _applicantsTick = new DispatcherTimer(TimeSpan.FromSeconds(1), DispatcherPriority.Background, (_, _) => ApplicantsTick(DateTime.UtcNow), Dispatcher);
        _applicantsTick.Start();
        Closed += (_, _) => { _applicantsTick?.Stop(); _applicantsOverlay?.Close(); _applicantsOverlay = null; };
    }

    /// <summary>A second's work: the window shown with whoever is waiting, or folded away.</summary>
    void ApplicantsTick(DateTime now)
    {
        if (!_applicantPrefs.On) { CloseApplicants(); return; }
        IReadOnlyList<PartyApplicants.Applicant> items;
        try { items = ApplicantsBoard.Pending(now); } catch (Exception ex) { CrashLog.Write("applicants", ex); return; }
        if (items.Count == 0)
        {
            _applicantsOverlay?.Show(items, now);   // hides it
            return;
        }
        if (_applicantsOverlay == null) OpenApplicants();
        _applicantsOverlay?.Show(items, now);
    }

    void OpenApplicants()
    {
        if (_applicantsOverlay != null) return;
        _applicantsOverlay = new ApplicantsOverlay(_applicantPrefs)
        {
            KinahBox = () => _hud is { IsVisible: true } h && h.ActualWidth > 0 ? new Rect(h.Left, h.Top, h.ActualWidth, h.ActualHeight) : null,
            Theme = () => HudThemes.Of(HudLook),
            Look = () => HudLook,
        };
        _applicantsOverlay.Match(_hudPreview?.HudOpacity ?? _prefs.HudOpacity, _hudPreview?.HudScale ?? _hudPrefs.Scale);
        _applicantsOverlay.Closed += (_, _) => _applicantsOverlay = null;
    }

    void CloseApplicants() { _applicantsOverlay?.Close(); _applicantsOverlay = null; }

    /* ---------------- callable from Options and the Overlays menu ---------------- */

    internal ApplicantPrefs ApplicantChoices => _applicantPrefs;
    internal bool ApplicantsOn => _applicantPrefs.On;

    /// <summary>The Applicants window on or off (and saved).</summary>
    internal void SetApplicantsOverlay(bool on, bool save = true)
    {
        _applicantPrefs.On = on;
        if (save) _applicantPrefs.Save();
        if (!on) CloseApplicants();
        else ApplicantsTick(DateTime.UtcNow);
        DrawOverlayPills();
    }

    /// <summary>Reset position: the spot forgotten; it sits just under the Kinah overlay again.</summary>
    internal void ResetApplicantsPosition()
    {
        _applicantPrefs.X = null; _applicantPrefs.Y = null; _applicantPrefs.Save();
        _applicantsOverlay?.ResetSpot();
    }

    internal void SetApplicantsClickThrough(bool on)
    {
        _applicantPrefs.ClickThrough = on; _applicantPrefs.Save();
        _applicantsOverlay?.SetClickThrough(on);
    }

    /// <summary>The window drawn again in the theme and at the opacity and size in use (after Options' Save, a look picked).</summary>
    internal void RefreshApplicants()
    {
        _applicantsOverlay?.Match(_hudPreview?.HudOpacity ?? _prefs.HudOpacity, _hudPreview?.HudScale ?? _hudPrefs.Scale);
        if (_applicantsOverlay != null) ApplicantsTick(DateTime.UtcNow);
    }

    /// <summary>For the self-test and renders: a list of one's own, the settings, the tick and the window.</summary>
    internal PartyApplicants? ApplicantsBoardForTest { get => _applicantsForShot; set => _applicantsForShot = value; }
    internal ApplicantPrefs ApplicantPrefsForTest { get => _applicantPrefs; set => _applicantPrefs = value; }
    internal void ApplicantsTickForTest(DateTime now) => ApplicantsTick(now);
    internal ApplicantsOverlay? ApplicantsOverlayForTest => _applicantsOverlay;
}
