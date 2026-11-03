using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using KinahLedger.Platform;

namespace KinahLedger.Ui;

/// <summary>
/// What's new (0.9.12): after an update, a card at the top of the Farming page with this version's release notes, each
/// bullet as its bold lead and the rest (Platform/WhatsNew.cs reads them from the exe). Shown once per version: Got it
/// stores the version in ui.json (WhatsNewSeen) and puts it away; All notes on GitHub opens the release. A fresh install
/// never sees it (the Welcome card is there); the same card in Compact and Full view, as GOALS.
/// </summary>
public partial class MainWindow
{
    /// <summary>Bullets drawn before "and N more": the window grows with the card, so it stays short.</summary>
    internal const int WhatsNewMax = 6;
    bool _whatsNewOn;

    static string ThisVersion => Upload.RunPayload.AppVersion;
    static string ReleaseUrlFor(string version) => $"{Installer.RepoUrl}/releases/tag/v{version}";

    /// <param name="watch">False for layout renders and self-tests: the player's ui.json is never read for it or written.</param>
    void InitWhatsNew(bool watch)
    {
        WhatsNewGotIt.Click += (_, _) => PutWhatsNewAway();
        WhatsNewAllLink.Click += (_, _) => Open(ReleaseUrlFor(ThisVersion));
        if (!watch) return;
        var notes = WhatsNew.Built();
        // LastVersion is still the version that ran before this start here: AnnounceUpdated writes it later, in StartUpdates
        var (show, seen) = WhatsNew.Due(_prefs.WhatsNewSeen, _prefs.LastVersion, ThisVersion, notes.Count > 0);
        if (seen != _prefs.WhatsNewSeen) { _prefs.WhatsNewSeen = seen; _prefs.Save(); }
        if (show) ShowWhatsNew(notes);
    }

    /// <summary>Fills the card with the notes and shows it (on Farming, outside the first-run screen).</summary>
    void ShowWhatsNew(IReadOnlyList<WhatsNew.Note> notes)
    {
        WhatsNewTitle.Text = $"WHAT'S NEW IN {ThisVersion}";
        WhatsNewList.Children.Clear();
        foreach (var n in notes.Take(WhatsNewMax)) WhatsNewList.Children.Add(WhatsNewLine(n));
        if (notes.Count > WhatsNewMax)
        {
            var more = new TextBlock { Text = $"and {notes.Count - WhatsNewMax} more in the notes", FontSize = 12.5, Margin = new Thickness(14, 6, 0, 0) };
            more.SetResourceReference(TextBlock.ForegroundProperty, "Faint");
            WhatsNewList.Children.Add(more);
        }
        _whatsNewOn = notes.Count > 0;
        PlaceWhatsNew();
    }

    /// <summary>One bullet: a small gold dot, the bold lead in the window's ink and the rest in its muted ink, wrapped.</summary>
    static FrameworkElement WhatsNewLine(WhatsNew.Note n)
    {
        var g = new Grid { Margin = new Thickness(0, 5, 0, 0) };
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(14) });
        g.ColumnDefinitions.Add(new ColumnDefinition());
        var dot = new Border { Width = 5, Height = 5, CornerRadius = new CornerRadius(2.5), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(1, 7, 0, 0) };
        dot.SetResourceReference(Border.BackgroundProperty, "SecGold");
        g.Children.Add(dot);
        var t = new TextBlock { FontSize = 12.5, LineHeight = 18, TextWrapping = TextWrapping.Wrap };
        if (n.Lead.Length > 0)
        {
            var lead = new Run(n.Lead) { FontWeight = FontWeights.SemiBold };
            lead.SetResourceReference(TextElement.ForegroundProperty, "Ink");
            t.Inlines.Add(lead);
        }
        if (n.Rest.Length > 0)
        {
            var rest = new Run((n.Lead.Length > 0 ? " " : "") + n.Rest);
            rest.SetResourceReference(TextElement.ForegroundProperty, "Muted");
            t.Inlines.Add(rest);
        }
        Grid.SetColumn(t, 1); g.Children.Add(t);
        return g;
    }

    /// <summary>The card shows on the Farming page only, never on the first-run screen (called from ApplyView too).</summary>
    void PlaceWhatsNew() =>
        WhatsNewCard.Visibility = _whatsNewOn && _part == "Farming" && !_welcome ? Visibility.Visible : Visibility.Collapsed;

    /// <summary>Got it: this version's notes are seen; the card comes back with the next version's.</summary>
    void PutWhatsNewAway()
    {
        _whatsNewOn = false;
        PlaceWhatsNew();
        _prefs.WhatsNewSeen = ThisVersion;
        if (!_demo) _prefs.Save();
    }

    /// <summary>
    /// For the options self-test: a start with these stored values and notes, as InitWhatsNew decides it (the prefs in
    /// memory only; a self-test window never saves). Returns whether the card shows, its title, bullets and "more" line.
    /// </summary>
    internal (bool Shown, string Title, int Lines, string More) WhatsNewStartForTest(string seen, string lastRun, string notesText)
    {
        _whatsNewOn = false; PlaceWhatsNew();
        _prefs.WhatsNewSeen = seen;
        var notes = WhatsNew.Parse(notesText);
        var (show, stored) = WhatsNew.Due(seen, lastRun, ThisVersion, notes.Count > 0);
        _prefs.WhatsNewSeen = stored;
        if (show) ShowWhatsNew(notes);
        return WhatsNewForTest();
    }

    internal (bool Shown, string Title, int Lines, string More) WhatsNewForTest()
    {
        UpdateLayout();
        var lines = WhatsNewList.Children.OfType<Grid>().Count();
        var more = WhatsNewList.Children.OfType<TextBlock>().FirstOrDefault()?.Text ?? "";
        return (WhatsNewCard.IsVisible, WhatsNewTitle.Text, lines, more);
    }

    /// <summary>A layout render with the card (KL_SHOT_STATE=whats-new): this build's own notes, or a sample in a build without them.</summary>
    internal void ShowWhatsNewSample()
    {
        var notes = WhatsNew.Built();
        if (notes.Count == 0) notes = WhatsNew.Parse("Sample.\n\n- **What's new, in the window.** After an update, a card at the top shows what changed.\n\nInstalled copies update by themselves in the background.");
        ShowWhatsNew(notes);
    }
}
