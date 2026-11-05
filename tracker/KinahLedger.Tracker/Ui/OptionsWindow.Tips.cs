using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;

namespace KinahLedger.Ui;

/// <summary>
/// Options › Shortcuts &amp; tips (Steven, 2026-11-04). The tray menu used to show Ctrl+N and Ctrl+Shift+C beside Clear
/// everything and Copy share picture, but those keys only work in the window, so from the tray they did nothing; they are
/// said here instead, apart from the two keys that work in the game. Then eight things a player doesn't find on their own,
/// each with a link where a page has it. The rows are built the first time the page shows; the keys are read from the
/// edited choices each time, so a key just recorded on In game shows here before Save.
/// </summary>
public partial class OptionsWindow
{
    TextBlock? _tipsHotkey, _tipsMarkKey;
    readonly List<FrameworkElement> _tipRows = new();

    /// <summary>The window's own keys (MainWindow.Shortcuts.cs and the Undo bar).</summary>
    static readonly (string Key, string What)[] WindowKeys =
    {
        (MainWindow.OptionsKey, "Options"), (MainWindow.NewSessionKey, MainWindow.ClearAllText), (MainWindow.CopyPictureKey, "Copy the share picture"),
        ("Ctrl+Z", "Undo, while its bar shows (8 s)"),
    };

    void FillTips()
    {
        if (_tipsHotkey == null) BuildTips();
        _tipsHotkey!.Text = _edit.Hotkey.Length > 0 ? _edit.Hotkey : "off";
        _tipsMarkKey!.Text = _edit.MarkWrongKey.Length > 0 ? _edit.MarkWrongKey : "not set";
    }

    void BuildTips()
    {
        // in the game: the two global keys, each with Change (the keys are recorded on In game)
        _tipsHotkey = KeyText("");
        _tipsMarkKey = KeyText("");
        TipsGame.Children.Add(GameKeyRow("Start or stop a run", "Works while the game is in front.", _tipsHotkey));
        TipsGame.Children.Add(Styled(new Border(), "Divider"));
        TipsGame.Children.Add(GameKeyRow("That was wrong", "Marks the newest counted line as wrong and keeps the last minute for a report.", _tipsMarkKey));

        // in this window: what Shortcuts.cs binds, and Undo
        for (int i = 0; i < WindowKeys.Length; i++)
        {
            if (i > 0) TipsWindow.Children.Add(Styled(new Border(), "Divider"));
            var g = new Grid { Margin = new Thickness(18, 10, 18, 10) };
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(130) }); g.ColumnDefinitions.Add(new ColumnDefinition());
            g.Children.Add(KeyText(WindowKeys[i].Key));
            var what = Styled(new TextBlock { Text = WindowKeys[i].What, VerticalAlignment = VerticalAlignment.Center }, "RowTitle");
            Grid.SetColumn(what, 1); g.Children.Add(what);
            TipsWindow.Children.Add(g);
        }

        // did you know: one line each, a link where a page has it
        Tip("Click any counted line to report it. With Explain each count on (Game reading), its card says why it was counted.", "Turn it on in Game reading", () => Go(Page.Reading));
        Tip("Click a recipe row for its last tries.");
        Tip("Set a goal from an item's card: the ring on the Farming page follows it.");
        Tip("Compare zones and runs side by side; it's off by default.", "Turn it on in General", () => Go(Page.General));
        Tip("Move the panel for 20 s from the tray, or put it exactly where you want it.", "Place the panel…", () => { if (_host.OpenPlacementGuide() is { } why) ShowNote(why, "Warn"); });
        Tip("Right-click a Where to farm row for that zone's page on kinahledger.com.");
        Tip("An event run that was really two runs: Split it from its card.");
        Tip("Link this PC to your profile once and your week shows on the site.", "Sharing", () => Go(Page.Sharing));
    }

    FrameworkElement GameKeyRow(string title, string hint, TextBlock key)
    {
        var g = new Grid { Margin = new Thickness(18, 12, 18, 12) };
        g.ColumnDefinitions.Add(new ColumnDefinition()); g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var words = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        words.Children.Add(Styled(new TextBlock { Text = title }, "RowTitle"));
        words.Children.Add(Styled(new TextBlock { Text = hint, TextWrapping = TextWrapping.Wrap }, "RowHint"));
        g.Children.Add(words);
        key.Margin = new Thickness(12, 0, 12, 0);
        Grid.SetColumn(key, 1); g.Children.Add(key);
        var change = new Button { Content = "Change", VerticalAlignment = VerticalAlignment.Center, ToolTip = "Pick the key on the In game page" };
        AutomationProperties.SetName(change, "Change the " + title + " key");
        change.Click += (_, _) => Go(Page.InGame);
        Grid.SetColumn(change, 2); g.Children.Add(change);
        return g;
    }

    static TextBlock KeyText(string text)
    {
        var t = new TextBlock { Text = text, FontFamily = new FontFamily("Cascadia Mono, Consolas"), FontSize = 13, VerticalAlignment = VerticalAlignment.Center };
        t.SetResourceReference(TextBlock.ForegroundProperty, "Gold");
        return t;
    }

    void Tip(string text, string link = "", Action? go = null)
    {
        if (_tipRows.Count > 0) TipsKnow.Children.Add(Styled(new Border(), "Divider"));
        var t = Styled(new TextBlock { Margin = new Thickness(18, 10, 18, 10), TextWrapping = TextWrapping.Wrap }, "RowTitle");
        t.Inlines.Add(new Run(text));
        if (link.Length > 0 && go != null)
        {
            t.Inlines.Add(new Run("   "));
            var h = new Hyperlink(new Run(link));
            h.SetResourceReference(StyleProperty, "LinkStyle");
            h.Click += (_, _) => go();
            t.Inlines.Add(h);
        }
        _tipRows.Add(t);
        TipsKnow.Children.Add(t);
    }

    /// <summary>
    /// The options self-test's part (WpfApp.OptionsSelfTest): the page has its three groups, eight tips or more, and the
    /// start/stop row says the key as it is now ("off" with none).
    /// </summary>
    internal static void SelfTestTips(MainWindow w, Func<OptionsWindow> open, Action<bool, string> check)
    {
        var o = open();
        o.Go(Page.Tips);
        check(o.PageTips.Visibility == Visibility.Visible && o.TipsGame.Children.Count >= 3 && o.TipsWindow.Children.Count >= 7 && o.TipsKnow.Children.Count > 0,
            "Shortcuts & tips: In game, In this window and Did you know");
        check(o._tipRows.Count >= 8, $"Shortcuts & tips: {o._tipRows.Count} tips (8 or more)");
        var want = w.HotkeyText.Length > 0 ? w.HotkeyText : "off";
        check(o._tipsHotkey?.Text == want, $"Shortcuts & tips: Start or stop a run shows its key ({o._tipsHotkey?.Text}, saved {want})");
        check(o._tipsMarkKey?.Text == (w.MarkWrongText.Length > 0 ? w.MarkWrongText : "not set"), "Shortcuts & tips: That was wrong shows its key or \"not set\"");
        o.Close();
    }
}
