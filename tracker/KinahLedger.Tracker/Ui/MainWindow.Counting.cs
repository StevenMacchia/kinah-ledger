using System.Windows;
using System.Windows.Controls;
using KinahLedger.Session;

namespace KinahLedger.Ui;

/// <summary>
/// "Help improve counting" in the window (Session/LabelWriter.cs): the player's choice is honoured everywhere (the writer,
/// the sender), said once on first run with a one-click off, and kept in Options › Sharing.
/// </summary>
public partial class MainWindow
{
    /// <summary>The one sentence, the same in Options and the first-run notice.</summary>
    public const string CountingSentence = "Sends what it read and small pictures of item names and icons, never your chat or names. Off here any time.";

    Border? _countingCard;

    /// <summary>The writer follows the choice: on only with the setting and sharing both on; off empties its folder.</summary>
    void ApplyCounting()
    {
        if (_demo) return;
        LabelWriter.Shared.SetEnabled(_prefs.HelpImproveCounting && _prefs.Share);
    }

    /// <summary>At the window's start (live only): the choice applied, the sender started, the notice shown once.</summary>
    void InitCounting(bool live)
    {
        if (!live) return;
        ApplyCounting();
        Upload.LabelClient.Start(() => _settings.Upload, LabelWriter.Shared);
        // after the window is up, so a layout render (demo) never shows it
        Dispatcher.InvokeAsync(() => { if (!_demo && !_prefs.CountingNoticeSeen) ShowCountingNotice(); }, System.Windows.Threading.DispatcherPriority.Loaded);
    }

    /// <summary>The one-time notice above the tagline: the sentence, Keep on, Turn off. Either answer hides it for good.</summary>
    void ShowCountingNotice()
    {
        if (_countingCard != null || HeadStack == null) return;
        var text = new TextBlock { Style = (Style)FindResource("BodyStyle"), FontSize = 12.5, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 12, 0) };
        // a resource reference, not B("Ink"): B() hands back the brush of the look at that moment, and the look is often picked
        // after this notice is up (first run: Welcome's look chips), which left Night's pale ink on Daylight's white card
        var head = new System.Windows.Documents.Run("Help improve counting is on. ") { FontWeight = FontWeights.SemiBold };
        head.SetResourceReference(System.Windows.Documents.TextElement.ForegroundProperty, "Ink");
        text.Inlines.Add(head);
        text.Inlines.Add(new System.Windows.Documents.Run(CountingSentence));
        var keep = new Button { Content = "Keep on", Style = (Style)FindResource("PrimaryButton"), Height = 34, Padding = new Thickness(16, 0, 16, 0), FontSize = 13, Margin = new Thickness(0, 0, 8, 0) };
        var off = new Button { Content = "Turn off", Height = 34, Padding = new Thickness(14, 0, 14, 0), FontSize = 13 };
        SetA11yName(keep, "Keep Help improve counting on");
        SetA11yName(off, "Turn Help improve counting off");
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        buttons.Children.Add(keep); buttons.Children.Add(off);
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.Children.Add(text);
        Grid.SetColumn(buttons, 1); grid.Children.Add(buttons);
        _countingCard = new Border { Style = (Style)FindResource("CardStyle"), Margin = new Thickness(0, 12, 0, 0), Padding = new Thickness(14, 10, 14, 10), Child = grid };
        keep.Click += (_, _) => AnswerCounting(true);
        off.Click += (_, _) => AnswerCounting(false);
        int at = HeadStack.Children.IndexOf(Tagline);
        HeadStack.Children.Insert(at >= 0 ? at : HeadStack.Children.Count, _countingCard);
    }

    static void SetA11yName(Button b, string name) => System.Windows.Automation.AutomationProperties.SetName(b, name);

    void AnswerCounting(bool keep)
    {
        _prefs.HelpImproveCounting = keep;
        _prefs.CountingNoticeSeen = true;
        ApplyCounting();
        SavePrefs();
        if (_countingCard != null) { HeadStack.Children.Remove(_countingCard); _countingCard = null; }
        Say(keep ? "Help improve counting stays on. Options › Sharing turns it off any time." : "Help improve counting is off. Nothing is written or sent for it.", keep ? "Good" : "Faint");
    }
}
