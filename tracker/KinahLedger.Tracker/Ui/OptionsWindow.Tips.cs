using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;

namespace KinahLedger.Ui;

/// <summary>
/// Options › Shortcuts &amp; tips (Steven, 2026-11-04). The tray menu used to show Ctrl+N and Ctrl+Shift+C beside Clear
/// everything and Copy share picture, but those keys only work in the window, so from the tray they did nothing; they are
/// said here instead, apart from the two keys that work in the game. Then eight things a player doesn't find on their own,
/// each with a link where a page has it. The rows are built the first time the page shows. The two game keys are set on
/// this page since 2026-11-08 (the audit found them on In game and again here); In game keeps a pointer.
/// </summary>
public partial class OptionsWindow
{
    bool _tipsBuilt;
    readonly List<FrameworkElement> _tipRows = new();

    /// <summary>The window's own keys (MainWindow.Shortcuts.cs and the Undo bar).</summary>
    static readonly (string Key, string What)[] WindowKeys =
    {
        (MainWindow.OptionsKey, "Options"), (MainWindow.NewSessionKey, MainWindow.ClearAllText), (MainWindow.CopyPictureKey, "Copy the share picture"),
        ("Ctrl+Z", "Undo, while its bar shows (8 s)"),
        (MainWindow.WindowBiggerKey + " / " + MainWindow.WindowSmallerKey, "Window size, a step bigger or smaller (Ctrl+wheel too)"), (MainWindow.WindowResetKey, "Window size back to 100%"),
        (MainWindow.PageKeys, "Farm, Craft, Runs, Timers, Parser"),
    };

    void FillTips()
    {
        if (!_tipsBuilt) BuildTips();
        _tipsBuilt = true;
    }

    void BuildTips()
    {
        // in the game: the two global keys are the page's own rows in the XAML (HotkeyBox, MarkKeyBox), recorded here
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

    static TextBlock KeyText(string text)
    {
        var t = new TextBlock { Text = text, FontFamily = new FontFamily("Cascadia Mono, Consolas"), FontSize = 13, VerticalAlignment = VerticalAlignment.Center };
        t.SetResourceReference(TextBlock.ForegroundProperty, "Ink");   // a key's name, not money (colour pass, 2026-11-13)
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
    /// The options self-test's part (WpfApp.OptionsSelfTest): the page has its three groups, eight tips or more, the two
    /// game keys are set here and only here (In game points to them), and the start/stop row says the key as it is now.
    /// </summary>
    internal static void SelfTestTips(MainWindow w, Func<OptionsWindow> open, Action<bool, string> check)
    {
        var o = open();
        o.Go(Page.Tips);
        check(o.PageTips.Visibility == Visibility.Visible && o.TipsGame.Children.Count >= 3 && o.TipsWindow.Children.Count >= 7 && o.TipsKnow.Children.Count > 0,
            "Shortcuts & tips: In game, In this window and Did you know");
        check(o._tipRows.Count >= 8, $"Shortcuts & tips: {o._tipRows.Count} tips (8 or more)");
        var want = w.HotkeyText.Length > 0 ? w.HotkeyText : "Off";
        check(o.HotkeyBox.Text == want, $"Shortcuts & tips: Start and stop a run shows its key ({o.HotkeyBox.Text}, saved {want})");
        check(o.MarkKeyBox.Text == (w.MarkWrongText.Length > 0 ? w.MarkWrongText : "Off"), "Shortcuts & tips: That was wrong shows its key or \"Off\"");
        // one place for each key (2026-11-08 audit): the boxes sit on this page, In game has only the pointer to it
        bool OnPage(FrameworkElement e, FrameworkElement page) { for (DependencyObject? d = e; d != null; d = LogicalTreeHelper.GetParent(d)) if (d == page) return true; return false; }
        check(OnPage(o.HotkeyBox, o.PageTips) && OnPage(o.MarkKeyBox, o.PageTips) && !OnPage(o.HotkeyBox, o.PageInGame) && !OnPage(o.MarkKeyBox, o.PageInGame),
            "Shortcuts & tips: the two game keys are set here, not on In game");
        o.Go(Page.InGame);
        o.InGameKeysBtn.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
        check(o.PageTips.Visibility == Visibility.Visible, "In game's Shortcuts & tips button opens the page with the keys");
        o.Close();
    }
}
