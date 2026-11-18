using System.Windows;
using System.Windows.Controls;

namespace KinahLedger.Ui;

/// <summary>
/// Options › Applicants (Steven, 2026-11-18), the fourth overlay page: the window listing the people asking to join the
/// group you lead. Its switch (on by default) in the header, a preview in the overlay theme in use, and where it sits (Reset
/// position, click-through). These save as they change (applicants.json), apart from Options' Save, as the Timers page's.
/// </summary>
public partial class OptionsWindow
{
    internal CheckBox? ApplicantsChk { get; private set; }
    internal CheckBox? ApplicantsThroughChk { get; private set; }
    internal Button? ApplicantsResetBtn { get; private set; }

    void FillApplicants()
    {
        PageApplicants.Children.Clear();
        var p = _host.ApplicantChoices;
        void Add(UIElement e) => PageApplicants.Children.Add(e);

        ApplicantsChk = Switch(p.On, on => _host.SetApplicantsOverlay(on));
        ApplicantsChk.ToolTip = L.T("While you lead a group, everyone asking to join, newest at the bottom; it folds away when nobody is waiting.");
        Add(OverlayHeader(Styled(new TextBlock { Text = L.T("Applicants") }, "PageTitle"),
            Styled(new TextBlock { Text = L.T("Everyone asking to join the group you lead, not one at a time. Reading only: accept or decline in the game."),
                TextWrapping = TextWrapping.Wrap }, "PageLead"),
            ApplicantsChk, "Show the applicants over the game"));
        Add(MeterBlockHost(Page.Applicants));   // "Needs Npcap · Get it" while something keeps the meter off (Ui/OptionsWindow.MeterBlock.cs)

        // the stage: four sample applicants in the theme the overlay is in
        var now = DateTime.UtcNow;
        string theme = HudThemes.Of(HudLooks.Parse(_edit.HudLook));
        try { Add(Margined(Stage(ApplicantsFace.Build(theme, ApplicantsFace.Sample(4, now), now, _edit.HudOpacity > 0 ? _edit.HudOpacity : 0.94), L.T("Applicants"), L.T("a sample: four asking to join"), 196), new Thickness(0, 12, 0, 0))); }
        catch (Exception ex) { CrashLog.Write("applicants preview", ex); }

        var g = new StackPanel();
        ApplicantsResetBtn = new Button { Content = L.T("Reset position"), ToolTip = L.T("Back to just under the Kinah overlay."), Margin = new Thickness(0, 0, 18, 0) };
        ApplicantsResetBtn.Click += (_, _) => _host.ResetApplicantsPosition();
        ApplicantsThroughChk = Switch(p.ClickThrough, on => _host.SetApplicantsClickThrough(on));
        System.Windows.Automation.AutomationProperties.SetName(ApplicantsThroughChk, "Applicants click-through");
        var said = new TextBlock { Text = L.T("Click-through"), FontSize = 13, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0),
            ToolTip = L.T("On: clicks pass through it to the game; its left edge stays grabbable, with a grip when you point at it.") };
        said.SetResourceReference(TextBlock.ForegroundProperty, "Ink");
        g.Children.Add(Row("Where it sits", "Drag it anywhere, as the Kinah overlay. Its look follows the overlay's theme.",
            new StackPanel { Orientation = Orientation.Horizontal, Children = { ApplicantsResetBtn, said, ApplicantsThroughChk } }));
        Add(Styled(new Border { Child = g, Margin = new Thickness(0, 10, 0, 0) }, "Group"));
    }
}
