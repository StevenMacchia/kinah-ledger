using System.Windows;
using KinahLedger.Session;

namespace KinahLedger.Ui;

/// <summary>
/// The Applicants list in a window of its own (Ui/PopOutWindow.cs), for a second screen beside a game in exclusive
/// fullscreen: the same face as the Applicants overlay (Ui/ApplicantsFace.cs) in the overlay theme in use, scaled to the
/// window's width. Unlike the overlay it stays open with nobody waiting and says so. Reading only: the game keeps accepting
/// and declining.
/// </summary>
sealed class ApplicantsPopOut : PopOutWindow
{
    public const double DefaultWidth = 460;
    /// <summary>The face's own width before it is scaled: wide enough for a long name and CP, so the scale holds as the list changes.</summary>
    public const double FaceWidth = 400;
    string _sig = "";

    public ApplicantsPopOut(Window main, PopOutPrefs.Spot spot, Action save)
        : base(main, spot, save, L.T("Applicants"), "Applicants window", DefaultWidth, 300) { }

    /// <summary>The people waiting at <paramref name="now"/> in <paramref name="theme"/>: drawn again only when a row, the theme or a row's "ago" changes.</summary>
    public void Draw(IReadOnlyList<PartyApplicants.Applicant> items, DateTime now, string theme)
    {
        var sig = theme + "|" + L.Code + "|" + string.Join(",", items.Select(a => a.Key + ":" + PartyApplicants.Ago(a.At, now)));
        if (sig == _sig && FaceForTest != null) return;
        _sig = sig;
        var face = ApplicantsFace.Build(theme, items, now, 1);
        face.HorizontalAlignment = HorizontalAlignment.Left;
        face.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        face.Width = Math.Max(FaceWidth, Math.Ceiling(face.DesiredSize.Width));
        SetFace(face);
    }
}
