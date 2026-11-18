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
/// <item>Every section has its own buttons top right of its head (Steven, 2026-11-18: "IN THE APP IT SHOULD HAVE A MORE
/// PROMINENT COPY BUTTON NO ONE KNOWS TO RIGHT CLICK"): Copy picture on the hero (its summary card), the group, the timeline,
/// the damage taken, the compare and a member's abilities, and Copy text on the tables. A copy says "Copied" in the button
/// for a moment. Beside the hero's Copy picture a ▾ offers the rest: this view, the meter (Ui/MeterShare.cs), everything
/// stacked, and the table as text. Each picture is copied, saved in Pictures\Kinah Ledger and said in the toast
/// (Ui/ParserShare.cs draws them).</item>
/// <item>A right-click on the hero, a table or a member's view opens the same lines for it, an extra: Copy picture, Copy as text.</item>
/// <item>A pull open: a real ← All boss pulls button over the hero; Backspace, Alt+← and the mouse's back button go back too
/// (a member open goes back to the group first).</item>
/// </list>
/// </summary>
public partial class MainWindow
{
    /// <summary>Set by the options test: a table's text goes here instead of the clipboard.</summary>
    internal static Action<string>? CopyTextSinkForTest;

    ParserShare.Parts ShareParts(long? member = null) => new(PDetails, PKills, PDeaths, _parserPrefs.ShareHideNames, e => CoverageLine(e), member);

    /// <summary>The fight a click on the page means: the kept one open, else the one under way now (its latest figures).</summary>
    Encounter ShareFightNow(Encounter shown) => _parserOpen != null || !shown.Live ? shown : ParserLive is { } l && l.Id == shown.Id ? l : shown;

    DateTime ShareNowOf(Encounter e) => e.Live ? ParserNow : e.End ?? e.LastAt;

    /// <summary>The view the page shows now (its tab), as Share's "This view" takes it.</summary>
    string ShownView(FightView v) => ParserCommand.Tabs(v).Contains(_parserTab) ? ParserShare.ViewOfTab(_parserTab) : ParserShare.Group;

    /// <summary>
    /// A picture of <paramref name="view"/> (a tab's view, the summary card, everything) copied and saved; "meter" is the meter's
    /// own picture. Said in the window's toast. True when it went to the clipboard (or the test's sink).
    /// </summary>
    /// <param name="member">For <see cref="ParserShare.Abilities"/>: whose (their id).</param>
    bool ShareView(Encounter? e, string view, long? member = null)
    {
        if (view == "meter") return ShareMeter(e);
        if (!Shareable(e))
        {
            Say(L.T("No fight to share yet. Pull a boss and the picture fills in."), "Warn");
            return false;
        }
        try
        {
            var bmp = ParserShare.Render(e!, ShareNowOf(e!), ParserPast(), view, ShareParts(member), ParserViewSample);
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

    /// <summary>A table (the group, the damage taken, the compare, a member's abilities) as tab-separated text on the clipboard.</summary>
    bool CopyViewText(Encounter? e, string view, long? member = null)
    {
        if (!Shareable(e)) { Say(L.T("No fight to share yet. Pull a boss and the picture fills in."), "Warn"); return false; }
        try
        {
            var text = ParserShare.Text(e!, ShareNowOf(e!), ParserPast(), view, ShareParts(member), ParserViewSample);
            if (CopyTextSinkForTest is { } sink) sink(text);
            else Clipboard.SetText(text);
            Say(L.T("Copied as text. Paste it in Discord or anywhere."), "Good");
            return true;
        }
        catch (Exception ex) { CrashLog.Write("parser text", ex); Say(L.F("Couldn't copy the text: {0}", ex.Message), "Bad"); return false; }
    }

    /// <summary>
    /// The ▾ beside the head's Copy picture and Copy text (Copy picture itself copies the view shown): the summary card, the
    /// meter, everything; the table as text when the view is one.
    /// </summary>
    List<MenuEntry> ShareEntries(Encounter shown, FightView v)
    {
        string view = ShownView(v);
        string table = ParserShare.HasText(v, view) ? view : ParserShare.Group;
        var list = new List<MenuEntry>
        {
            new(ParserShare.Name(ParserShare.Hero), () => ShareView(ShareFightNow(shown), ParserShare.Hero), Sub: L.T("The boss, the clock, the group's DPS and yours")),
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

    // ---- the copy buttons (Steven, 2026-11-18: "IN THE APP IT SHOULD HAVE A MORE PROMINENT COPY BUTTON NO ONE KNOWS TO RIGHT CLICK")

    /// <summary>The buttons' marks, drawn 12 × 12: a picture (a frame, a hill, the sun), lines of text, and the tick of "Copied".</summary>
    const string PictureMark = "M1.5,2.5 L10.5,2.5 L10.5,10 L1.5,10 Z M2.2,9.3 L4.8,6.5 L6.6,8.3 L8,6.9 L9.8,8.7 M7.3,4.7 A0.9,0.9 0 1 0 9.1,4.7 A0.9,0.9 0 1 0 7.3,4.7",
        TextMark = "M2,3 L10,3 M2,6 L10,6 M2,9 L7,9", TickMark = "M2,6.5 L5,9.3 L10.2,3.2";

    /// <summary>The copy shown as done ("Copied", in the button itself) and until when: the page redraws a live fight, and the word stays put across it.</summary>
    static string? _copiedKey;
    static DateTime _copiedUntil;
    /// <summary>How long "Copied" stays (the options test shortens it to see the words come back).</summary>
    internal static TimeSpan CopiedFor = TimeSpan.FromSeconds(1.6);

    /// <summary>
    /// A copy button in the page's own button look (the theme's, as the back button and Share were), with its mark and its
    /// words. A copy that worked says "Copied" with a tick in its place for a moment, its width held, and the toast says
    /// where to paste it. <paramref name="key"/> names the button (the fight, the view, the member, picture or text).
    /// </summary>
    Button CopyButton(string key, string label, string mark, string tag, string tip, string name, Func<bool> act, bool large = false, bool primary = false)
    {
        var icon = new System.Windows.Shapes.Path
        {
            Data = Geometry.Parse(mark), StrokeThickness = 1.35, Width = 12, Height = 12, Stretch = Stretch.None, VerticalAlignment = VerticalAlignment.Center,
            StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round, StrokeLineJoin = PenLineJoin.Round,
        };
        var text = new TextBlock { Text = label, Margin = new Thickness(6, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        var b = new Button
        {
            Content = new StackPanel { Orientation = Orientation.Horizontal, Children = { icon, text } }, Height = primary ? 32 : large ? 26 : 24, FontSize = primary ? 13 : large ? 12 : 11.5,
            Padding = new Thickness(primary ? 12 : large ? 8 : 7, 0, primary ? 14 : large ? 10 : 9, 0), VerticalAlignment = VerticalAlignment.Center, Tag = tag,
            // the head's buttons say their key whatever Explanations on hover says: the shortcut is the point of them
            ToolTip = primary || large ? tip : Tip(tip),
        };
        if (primary) { b.SetResourceReference(StyleProperty, "PrimaryButton"); icon.StrokeThickness = 1.6; }
        AutomationProperties.SetName(b, name);
        // on the gold primary button the ink stays the button's own (green on gold doesn't read): the tick and "Copied" say it
        string ink = primary ? "BtnPrimaryInk" : "Ink", done = primary ? "BtnPrimaryInk" : "Good";
        void Look(bool copied)
        {
            icon.Data = Geometry.Parse(copied ? TickMark : mark);
            text.Text = copied ? L.T("Copied") : label;
            icon.SetResourceReference(System.Windows.Shapes.Shape.StrokeProperty, copied ? done : ink);
            text.SetResourceReference(TextBlock.ForegroundProperty, copied ? done : ink);
        }
        void Settle()
        {
            var left = _copiedUntil - DateTime.UtcNow;
            var t = new System.Windows.Threading.DispatcherTimer { Interval = left > TimeSpan.Zero ? left : TimeSpan.FromMilliseconds(1) };
            t.Tick += (_, _) => { t.Stop(); if (_copiedKey != key || DateTime.UtcNow >= _copiedUntil) { Look(false); b.MinWidth = 0; } };
            t.Start();
        }
        bool showing = _copiedKey == key && DateTime.UtcNow < _copiedUntil;
        Look(showing);
        if (showing) Settle();
        b.Click += (_, _) =>
        {
            if (!act()) return;
            b.MinWidth = b.ActualWidth;   // "Copied" in the same room: nothing beside it moves
            _copiedKey = key; _copiedUntil = DateTime.UtcNow + CopiedFor;
            Look(true);
            Settle();
        };
        return b;
    }

    /// <summary>
    /// A section's copy buttons, top right of its head: Copy picture, and Copy text when the section is a table. Tagged
    /// "parser-copy-VIEW", the buttons "parser-copy-picture-VIEW" and "parser-copy-text-VIEW".
    /// </summary>
    FrameworkElement CopyButtons(Encounter e, FightView v, string view, long? member = null)
    {
        var sp = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Tag = "parser-copy-" + view };
        string key = $"{e.Id}|{view}|{member}";
        sp.Children.Add(CopyButton(key + "|picture", L.T("Copy picture"), PictureMark, "parser-copy-picture-" + view,
            L.T("Copies a picture of this to paste in Discord or anywhere with Ctrl+V. Nothing is uploaded."), $"Copy a picture of the {view}",
            () => ShareView(ShareFightNow(e), view, member)));
        if (ParserShare.HasText(v, view))
        {
            var t = CopyButton(key + "|text", L.T("Copy text"), TextMark, "parser-copy-text-" + view,
                L.T("Copies this table as text, tab-separated: pastes into Discord or a spreadsheet."), $"Copy the {view} as text",
                () => CopyViewText(ShareFightNow(e), view, member));
            t.Margin = new Thickness(4, 0, 0, 0);
            sp.Children.Add(t);
        }
        return sp;
    }

    // ---- the page's head: Copy picture and Copy text, always in sight (Steven, 2026-11-18: "Make copy and pasting EASY and promoted") ----

    /// <summary>The key that copies the Parser page's view as a picture, and the one that copies it as text.</summary>
    public const string ParserCopyKey = "Ctrl+C", ParserCopyTextKey = "Ctrl+Shift+C";

    /// <summary>The fight the head's buttons and Ctrl+C copy: the one open, else the one under way, else the newest kept; null when none can be shared.</summary>
    Encounter? CopyFightNow(List<Encounter> past)
    {
        var shown = ParserShownFight(past);   // opened, under way, or held until the next boss fight
        return Shareable(shown) ? shown : null;
    }

    /// <summary>
    /// Copy picture or Copy text from the head, Ctrl+C or Ctrl+Shift+C: the view the page shows of <see cref="CopyFightNow"/> (a
    /// member's abilities while one is open, else the tab's view; on the list, the newest pull in the tab last used). Text is
    /// that view's table, else the group's. True when it went to the clipboard.
    /// </summary>
    bool CopyParserNow(bool text)
    {
        var past = ParserPast();
        if (CopyFightNow(past) is not { } e) { Say(L.T("Nothing to copy yet. Pull a boss and Copy picture fills in."), "Warn"); return false; }
        bool shown = true;   // CopyFightNow is the fight the page shows: a member open there is this fight's
        long? member = shown ? _parserInspect : null;
        var v = ParserViewOf(e, ShareNowOf(e), past);
        string view = member != null ? ParserShare.Abilities : ShownView(v);
        if (!text) return ShareView(e, view, member);
        string table = ParserShare.HasText(v, view) ? view : ParserShare.Group;
        return CopyViewText(e, table, table == ParserShare.Abilities ? member : null);
    }

    /// <summary>Ctrl+C and Ctrl+Shift+C on the Parser page (a text box takes Ctrl+C before the window does); false off the page.</summary>
    bool ParserCopyKeyPressed(bool text)
    {
        if (_part != "Parser" || _welcome) return false;
        CopyParserNow(text);
        return true;
    }

    /// <summary>
    /// The head's left side: a big gold Copy picture, Copy text beside it, the ▾ for the rest, and the keys said beside them
    /// (or, for a little while after a boss kill, the kill with its own Copy picture). Always shown; greyed while there is
    /// nothing to copy yet.
    /// </summary>
    FrameworkElement ParserCopyBar(List<Encounter> past)
    {
        var target = CopyFightNow(past);
        bool any = target != null;
        string viewName = ParserShare.Name(_parserInspect != null ? ParserShare.Abilities : ParserShare.ViewOfTab(_parserTab));
        string key = target != null ? $"{target.Id}|head" : "none|head";
        var pic = CopyButton(key + "|picture", L.T("Copy picture"), PictureMark, "parser-copy-now",
            L.F("Copies a picture of this fight as the page shows it ({0}) to paste in Discord or anywhere with Ctrl+V. Nothing is uploaded.", viewName) + "\n" + L.F("{0} does the same here.", ParserCopyKey),
            "Copy a picture of this fight", () => CopyParserNow(text: false), primary: true);
        var txt = CopyButton(key + "|text", L.T("Copy text"), TextMark, "parser-copy-now-text",
            L.T("Copies this fight's table as text: tab-separated, so it pastes into Discord, a message or a spreadsheet.") + "\n" + L.F("{0} does the same here.", ParserCopyTextKey),
            "Copy this fight as text", () => CopyParserNow(text: true), large: true);
        txt.Height = 32; txt.Margin = new Thickness(6, 0, 0, 0);
        var chev = new System.Windows.Shapes.Path { Data = Geometry.Parse("M0,0 L5,5 L10,0"), StrokeThickness = 1.7, Width = 10, Height = 6, Stretch = Stretch.None, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round, StrokeLineJoin = PenLineJoin.Round, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 1, 0, 0) };
        chev.SetResourceReference(System.Windows.Shapes.Shape.StrokeProperty, "Ink");
        var more = new Button
        {
            Content = chev, Height = 32, Width = 30, Padding = new Thickness(0), Margin = new Thickness(4, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, Tag = "parser-share-more",
            ToolTip = L.T("More to copy: the summary card, the meter, everything in one picture, the table as text"),
        };
        AutomationProperties.SetName(more, "More ways to copy this fight");
        more.Click += (_, _) =>
        {
            if (CopyFightNow(ParserPast()) is not { } e) return;
            ShowMenu(more, ShareEntries(e, ParserViewOf(e, ShareNowOf(e), ParserPast())));
        };
        foreach (var b in new[] { pic, txt, more }) { b.IsEnabled = any; ToolTipService.SetShowOnDisabled(b, true); }
        if (!any) pic.ToolTip = txt.ToolTip = more.ToolTip = L.T("Nothing to copy yet. Pull a boss and Copy picture fills in.");
        var row = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Tag = "parser-copy-bar", Children = { pic, txt, more } };
        // beside them: the keys, or a kill just made
        FrameworkElement said;
        if (_killPrompt is { } k && DateTime.UtcNow < _killPromptUntil)
        {
            var l1 = PT(L.F("{0} killed in {1}", k.Boss.Length > 0 ? k.Boss : k.Title, BossParser.Clock(k.Duration(k.End ?? k.LastAt))), 12, FontWeights.SemiBold, "Good", trim: true);
            var l2 = QLinkify(PT(L.T("Copy picture  ›"), 12, FontWeights.Bold, "Gold"));
            l2.HorizontalAlignment = HorizontalAlignment.Left;
            l2.ToolTip = L.T("Copies a picture of the kill to paste in Discord or anywhere with Ctrl+V.");
            AutomationProperties.SetName(l2, "Copy a picture of the kill");
            l2.Tag = "parser-kill-copy";
            l2.MouseLeftButtonUp += (_, _) => CopyKillPicture();
            said = new StackPanel { Children = { l1, l2 }, Tag = "parser-kill-prompt" };
        }
        else
        {
            var l1 = PT(any ? L.F("{0} copies this fight as a picture", ParserCopyKey) : L.T("Pull a boss and Copy picture fills in."), 11.5, FontWeights.Medium, "Muted", trim: true);
            var l2 = PT(any ? L.F("{0} copies it as text", ParserCopyTextKey) : L.T("Then paste it in Discord with Ctrl+V."), 11.5, FontWeights.Medium, "Faint", trim: true);
            said = new StackPanel { Children = { l1, l2 }, Tag = "parser-copy-hint" };
        }
        said.Margin = new Thickness(12, 0, 0, 0);
        said.VerticalAlignment = VerticalAlignment.Center;
        var g = new Grid();
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        g.Children.Add(row);
        Grid.SetColumn(said, 1); g.Children.Add(said);
        return g;
    }

    // ---- after a boss kill: "Copy picture" on the page and the status line for a little while, never a popup ----

    /// <summary>How long the kill's Copy picture stays on the page's head and the status line.</summary>
    internal static TimeSpan KillPromptFor = TimeSpan.FromSeconds(30);
    Encounter? _killPrompt;
    DateTime _killPromptUntil;
    string _killSeen = "";

    /// <summary>The window's tick: a boss pull of this session that just ended in a kill puts its Copy picture up; its time up takes it away.</summary>
    void KillPromptTick()
    {
        var now = DateTime.UtcNow;
        if (!_demo && ParserFights?.LastEnded is { Outcome: "kill" } k && k.Id != _killSeen)
        {
            _killSeen = k.Id;
            if (Shareable(k) && !k.OpenWorld) ShowKillPrompt(k, now);
        }
        if (_killPrompt != null && now >= _killPromptUntil)
        {
            _killPrompt = null;
            KillCopyText.Visibility = Visibility.Collapsed;
            if (_part == "Parser") RedrawParserPage();
        }
    }

    void ShowKillPrompt(Encounter k, DateTime now)
    {
        _killPrompt = k; _killPromptUntil = now + KillPromptFor;
        KillCopyRun.Text = L.T("Copy picture");
        KillCopyText.Visibility = Visibility.Visible;
        SayOnLine(L.F("{0} killed in {1}.", k.Boss.Length > 0 ? k.Boss : k.Title, BossParser.Clock(k.Duration(k.End ?? k.LastAt))), "Good");
        if (now + KillPromptFor > _statusHoldUntil) _statusHoldUntil = now + KillPromptFor;
        if (_part == "Parser") RedrawParserPage();
    }

    /// <summary>The kill's Copy picture (the page's head, the status line): the view the Parser page uses, of the kill.</summary>
    bool CopyKillPicture()
    {
        if (_killPrompt is not { } k) return false;
        var v = ParserViewOf(k, ShareNowOf(k), ParserPast());
        return ShareView(k, ShownView(v));
    }

    void InitKillPrompt()
    {
        KillCopyLink.Click += (_, _) => CopyKillPicture();
        KillCopyRun.Text = L.T("Copy picture");
        KillCopyText.ToolTip = L.T("Copies a picture of the kill to paste in Discord or anywhere with Ctrl+V.");
    }

    /// <summary>A right-click on the hero or a table, an extra beside the buttons: its picture, and its text when it is a table, in the window's own menu.</summary>
    void ShareOnRightClick(FrameworkElement el, Encounter e, FightView v, string view, long? member = null)
    {
        el.MouseRightButtonUp += (_, ev) =>
        {
            ev.Handled = true;
            ShowMenuAtPointer(el, ShareClickEntries(e, v, view, member));
        };
    }

    List<MenuEntry> ShareClickEntries(Encounter e, FightView v, string view, long? member = null)
    {
        var list = new List<MenuEntry> { new(L.T("Copy picture"), () => ShareView(ShareFightNow(e), view, member), Sub: ParserShare.Name(view)) };
        if (ParserShare.HasText(v, view)) list.Add(new(L.T("Copy as text"), () => CopyViewText(ShareFightNow(e), view, member), Sub: L.T("Tab-separated: pastes into Discord or a spreadsheet")));
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

    internal bool ShareViewForTest(Encounter? e, string view, long? member = null) => ShareView(e, view, member);
    internal bool CopyViewTextForTest(Encounter? e, string view, long? member = null) => CopyViewText(e, view, member);
    internal ParserShare.Parts SharePartsOfForTest(long? member) => ShareParts(member);
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
    /// <summary>For a render and the options test: the kill prompt on the newest kept kill, as just after it.</summary>
    internal void KillPromptForTest()
    {
        if (ParserPast().LastOrDefault(x => x.Outcome == "kill") is { } k) ShowKillPrompt(k, DateTime.UtcNow);
    }
    internal bool KillPromptShownForTest => KillCopyText.Visibility == Visibility.Visible;
    /// <summary>For a render: the meter's settings as a new install has them (the test window keeps the old ones, see InitParser).</summary>
    internal void ParserPrefsFreshForTest() { _parserPrefs = new ParserPrefs(); ApplyMeterExpanded(); }
    internal bool CopyParserNowForTest(bool text) => CopyParserNow(text);
    internal bool ParserCopyKeyForTest(bool text) => ParserCopyKeyPressed(text);
    internal bool CopyKillPictureForTest() => CopyKillPicture();
}
