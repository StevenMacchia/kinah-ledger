using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using KinahLedger.Parser;

namespace KinahLedger.Ui;

/// <summary>
/// The Parser page's Share and Copy (Steven, 2026-11-17: "make everything from here copyable", "make timeline sharable as
/// well as a copy/paste picture"), and its back button ("the little thing for 'All boss pulls' is so fucking hard to find -
/// make a back button WAY more obvious").
/// <list type="bullet">
/// <item>Share on the hero copies a picture of the view shown (the group's table, the timeline, the damage taken, the
/// compare); its ▾ also offers the summary card, the meter (Ui/MeterShare.cs), everything stacked, and the table as text.
/// Each picture is copied, saved in Pictures\Kinah Ledger and said in the toast (Ui/ParserShare.cs draws them).</item>
/// <item>A right-click on the hero or a table opens the same menu's lines for it: Copy picture, Copy as text.</item>
/// <item>A pull open: a real ← All boss pulls button over the hero; Backspace, Alt+← and the mouse's back button go back too
/// (a member open goes back to the group first).</item>
/// </list>
/// </summary>
public partial class MainWindow
{
    /// <summary>Set by the options test: a table's text goes here instead of the clipboard.</summary>
    internal static Action<string>? CopyTextSinkForTest;

    ParserShare.Parts ShareParts() => new(PDetails, PKills, PDeaths, _parserPrefs.ShareHideNames, e => CoverageLine(e));

    /// <summary>The fight a click on the page means: the kept one open, else the one under way now (its latest figures).</summary>
    Encounter ShareFightNow(Encounter shown) => _parserOpen != null ? shown : ParserLive ?? shown;

    DateTime ShareNowOf(Encounter e) => e.Live ? ParserNow : e.End ?? e.LastAt;

    /// <summary>The view the page shows now (its tab), as Share's "This view" takes it.</summary>
    string ShownView(FightView v) => ParserCommand.Tabs(v).Contains(_parserTab) ? ParserShare.ViewOfTab(_parserTab) : ParserShare.Group;

    /// <summary>
    /// A picture of <paramref name="view"/> (a tab's view, the summary card, everything) copied and saved; "meter" is the meter's
    /// own picture. Said in the window's toast. True when it went to the clipboard (or the test's sink).
    /// </summary>
    bool ShareView(Encounter? e, string view)
    {
        if (view == "meter") return ShareMeter(e);
        if (!Shareable(e))
        {
            Say(L.T("No fight to share yet. Pull a boss and the picture fills in."), "Warn");
            return false;
        }
        try
        {
            var bmp = ParserShare.Render(e!, ShareNowOf(e!), ParserPast(), view, ShareParts(), ParserViewSample);
            var file = ParserShare.FileName(e!, view);
            if (ShareSinkForTest is { } sink) sink(bmp, file);   // a test: never the clipboard or Pictures
            else
            {
                CopyPng(bmp);
                try { _meterShareSaved = SavePicture(bmp, file); }
                catch (Exception ex) { CrashLog.Write("parser share save", ex); }
            }
            Say(L.T("Picture copied. Paste it in Discord or anywhere with Ctrl+V."), "Good");
            return true;
        }
        catch (Exception ex)
        {
            CrashLog.Write("parser share", ex);
            Say(L.F("Couldn't copy the picture: {0}", ex.Message), "Bad");
            return false;
        }
    }

    /// <summary>A table (the group, the damage taken, the compare) as tab-separated text on the clipboard.</summary>
    bool CopyViewText(Encounter? e, string view)
    {
        if (!Shareable(e)) { Say(L.T("No fight to share yet. Pull a boss and the picture fills in."), "Warn"); return false; }
        try
        {
            var text = ParserShare.Text(e!, ShareNowOf(e!), ParserPast(), view, ShareParts(), ParserViewSample);
            if (CopyTextSinkForTest is { } sink) sink(text);
            else Clipboard.SetText(text);
            Say(L.T("Copied as text. Paste it in Discord or anywhere."), "Good");
            return true;
        }
        catch (Exception ex) { CrashLog.Write("parser text", ex); Say(L.F("Couldn't copy the text: {0}", ex.Message), "Bad"); return false; }
    }

    /// <summary>Share's menu: this view (the default), the summary card, the meter, everything; the table as text when the view is one.</summary>
    List<MenuEntry> ShareEntries(Encounter shown, FightView v)
    {
        string view = ShownView(v);
        string table = ParserShare.HasText(v, view) ? view : ParserShare.Group;
        var list = new List<MenuEntry>
        {
            new(L.T("This view"), () => ShareView(ShareFightNow(shown), view), Sub: ParserShare.Name(view)),
            new(ParserShare.Name(ParserShare.Hero), () => ShareView(ShareFightNow(shown), ParserShare.Hero), Sub: L.T("Group DPS, your DPS and place, the clock")),
            new(L.T("Meter"), () => ShareView(ShareFightNow(shown), "meter"), Sub: L.T("The meter as it shows over the game")),
            new(ParserShare.Name(ParserShare.All), () => ShareView(ShareFightNow(shown), ParserShare.All), Sub: string.Join(" · ", ParserShare.Stack(v).Select(ParserShare.Name))),
        };
        if (ParserShare.HasText(v, table))
        {
            list.Add(MenuEntry.Line);
            list.Add(new(L.T("Copy as text"), () => CopyViewText(ShareFightNow(shown), table), Sub: L.F("{0}, tab-separated: pastes into Discord or a spreadsheet", ParserShare.Name(table))));
        }
        return list;
    }

    /// <summary>
    /// Share on the hero: the button shares the view shown; its ▾ opens the menu of the rest (one control, two parts, the
    /// split between them a hairline).
    /// </summary>
    FrameworkElement ParserShareButton(Encounter e, FightView v)
    {
        var icon = new System.Windows.Shapes.Path
        {
            Data = Geometry.Parse("M6,1 L6,8.5 M3,4 L6,1 L9,4 M2.5,6.5 L1.5,6.5 L1.5,11 L10.5,11 L10.5,6.5 L9.5,6.5"), StrokeThickness = 1.4,
            StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round, StrokeLineJoin = PenLineJoin.Round, Width = 12, Height = 12, VerticalAlignment = VerticalAlignment.Center,
        };
        icon.SetResourceReference(System.Windows.Shapes.Shape.StrokeProperty, "Ink");
        var text = new TextBlock { Text = L.T("Share", "button"), Margin = new Thickness(6, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        bool can = Shareable(e);
        var main = new Button
        {
            Content = new StackPanel { Orientation = Orientation.Horizontal, Children = { icon, text } }, Height = 26, FontSize = 12, Padding = new Thickness(8, 0, 9, 0),
            VerticalAlignment = VerticalAlignment.Center, Tag = "parser-share", IsEnabled = can,
            ToolTip = Tip(L.T("A picture of this view to paste in Discord or anywhere. Nothing is uploaded.")),
        };
        AutomationProperties.SetName(main, "Share a picture of this view");
        main.Click += (_, _) => ShareView(ShareFightNow(e), ShownView(v));
        var chev = new System.Windows.Shapes.Path { Data = Geometry.Parse("M0,0 L4,4 L8,0"), StrokeThickness = 1.5, Width = 8, Height = 5, Stretch = Stretch.None, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round, VerticalAlignment = VerticalAlignment.Center };
        chev.SetResourceReference(System.Windows.Shapes.Shape.StrokeProperty, "Ink");
        var more = new Button
        {
            Content = chev, Height = 26, Width = 22, Padding = new Thickness(0), Margin = new Thickness(2, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, Tag = "parser-share-more", IsEnabled = can,
            ToolTip = Tip(L.T("More to share: the summary card, the meter, everything in one picture, the table as text")),
        };
        AutomationProperties.SetName(more, "More ways to share this fight");
        more.Click += (_, _) => ShowMenu(more, ShareEntries(e, v));
        return new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Children = { main, more }, Tag = "parser-share-split" };
    }

    /// <summary>A right-click on the hero or a table: its picture, and its text when it is a table, in the window's own menu.</summary>
    void ShareOnRightClick(FrameworkElement el, Encounter e, FightView v, string view)
    {
        el.MouseRightButtonUp += (_, ev) =>
        {
            ev.Handled = true;
            ShowMenuAtPointer(el, ShareClickEntries(e, v, view));
        };
    }

    List<MenuEntry> ShareClickEntries(Encounter e, FightView v, string view)
    {
        var list = new List<MenuEntry> { new(L.T("Copy picture"), () => ShareView(ShareFightNow(e), view), Sub: ParserShare.Name(view)) };
        if (ParserShare.HasText(v, view)) list.Add(new(L.T("Copy as text"), () => CopyViewText(ShareFightNow(e), view), Sub: L.T("Tab-separated: pastes into Discord or a spreadsheet")));
        return list;
    }

    // ---- the back button (and Backspace, Alt+←, the mouse's back button) ----

    /// <summary>The way back from a pull open: a real button, top left over the hero, as tall as the page's other buttons.</summary>
    FrameworkElement ParserBackButton(string label, Action back)
    {
        var arrow = new System.Windows.Shapes.Path
        {
            Data = Geometry.Parse("M13,6 L1.5,6 M6,1 L1,6 L6,11"), StrokeThickness = 1.8, Width = 14, Height = 12, VerticalAlignment = VerticalAlignment.Center,
            StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round, StrokeLineJoin = PenLineJoin.Round,
        };
        arrow.SetResourceReference(System.Windows.Shapes.Shape.StrokeProperty, "Ink");
        var text = new TextBlock { Text = label, FontSize = 13, FontWeight = FontWeights.SemiBold, Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        text.SetResourceReference(TextBlock.ForegroundProperty, "Ink");
        var b = new Button
        {
            Content = new StackPanel { Orientation = Orientation.Horizontal, Children = { arrow, text } }, Height = 32, Padding = new Thickness(11, 0, 14, 0),
            HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 0, 0, 10), Tag = "parser-back", Cursor = Cursors.Hand,
            ToolTip = L.T("Back (Backspace, Alt+← or the mouse's back button)"),
        };
        AutomationProperties.SetName(b, label);
        // the hover lifts it a little more than the theme's: the arrow nudges left, the words go gold
        b.MouseEnter += (_, _) => { arrow.RenderTransform = new TranslateTransform(-2, 0); arrow.SetResourceReference(System.Windows.Shapes.Shape.StrokeProperty, "Gold"); text.SetResourceReference(TextBlock.ForegroundProperty, "Gold"); };
        b.MouseLeave += (_, _) => { arrow.RenderTransform = null; arrow.SetResourceReference(System.Windows.Shapes.Shape.StrokeProperty, "Ink"); text.SetResourceReference(TextBlock.ForegroundProperty, "Ink"); };
        b.Click += (_, _) => back();
        return b;
    }

    /// <summary>One step back on the Parser page: a member open goes back to the group, a pull open to the list (or the run it came from). False when there is nowhere to go.</summary>
    bool ParserGoBack()
    {
        if (_part != "Parser" || _welcome) return false;
        if (_parserInspect != null) { _parserInspect = null; RedrawParserPage(); return true; }
        if (_parserOpen == null && !_parserFromRuns) return false;
        if (_parserFromRuns) BackToRuns(); else OpenFight(null);
        return true;
    }

    /// <summary>Backspace (outside a text box), Alt+← and the keyboard's own back key go back.</summary>
    internal bool ParserBackKey(Key key, Key systemKey, ModifierKeys mods, bool inText)
    {
        bool back = (key == Key.Back && mods == ModifierKeys.None && !inText) || key == Key.BrowserBack
            || (key == Key.System && systemKey == Key.Left && mods == ModifierKeys.Alt) || (key == Key.Left && mods == ModifierKeys.Alt);
        return back && ParserGoBack();
    }

    /// <summary>The mouse's back button (the thumb button, XButton1) goes back.</summary>
    internal bool ParserBackMouse(MouseButton b) => b == MouseButton.XButton1 && ParserGoBack();

    void InitParserBack()
    {
        PreviewKeyDown += (_, e) =>
        {
            if (e.Handled) return;
            bool inText = Keyboard.FocusedElement is TextBox or PasswordBox or ComboBox { IsEditable: true } or RichTextBox;
            if (ParserBackKey(e.Key, e.SystemKey, Keyboard.Modifiers, inText)) e.Handled = true;
        };
        PreviewMouseDown += (_, e) => { if (!e.Handled && ParserBackMouse(e.ChangedButton)) e.Handled = true; };
    }

    // ---- for the options test and renders ----

    internal bool ShareViewForTest(Encounter? e, string view) => ShareView(e, view);
    internal bool CopyViewTextForTest(Encounter? e, string view) => CopyViewText(e, view);
    internal List<(string Text, string Sub)> ShareMenuForTest(Encounter e)
    {
        var v = ParserViewOf(e, ShareNowOf(e), ParserPast());
        return ShareEntries(e, v).Where(x => !x.Rule).Select(x => (x.Text, x.Sub)).ToList();
    }
    internal ParserShare.Parts SharePartsForTest => ShareParts();
    internal bool ParserGoBackForTest() => ParserGoBack();
    internal long? ParserInspectForTest { get => _parserInspect; set { _parserInspect = value; RedrawParserPage(); } }
    internal string ParserTabForTest { get => _parserTab; set { _parserTab = value; RedrawParserPage(); } }
    /// <summary>The window's menu card while it is open (a right-click's, Share's ▾), else null.</summary>
    internal FrameworkElement? MenuCardForTest => _card is { IsOpen: true } c ? c.Child as FrameworkElement : null;
    internal void CloseMenuForTest() { if (_card != null) _card.IsOpen = false; }
    /// <summary>For a render: the store-v2 sample figures (crits, the timeline, damage taken) on every view, or off again.</summary>
    internal static void ParserDepthSampleForRender(bool on) => ParserViewSample = on ? ParserDemoDepth : null;
    /// <summary>The fight the page shows: the kept one open, else the one under way.</summary>
    internal Encounter? ParserShownForTest => _parserOpen != null ? ParserPast().FirstOrDefault(x => x.Id == _parserOpen) : ParserLive;
    internal DateTime ShareNowForTest(Encounter e) => ShareNowOf(e);
}
