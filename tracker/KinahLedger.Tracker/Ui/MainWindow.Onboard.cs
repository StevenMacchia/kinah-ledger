using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using KinahLedger.Session;

namespace KinahLedger.Ui;

/// <summary>
/// The first-session checklist (Steven, 2026-11-04): until a new player is set up, the empty Farming feed shows "Getting
/// set up" and six steps, each ✓ or ○ with a link where something can be done about it: a server, the game open, the chat
/// found, the Market read once, the panel placed, a first run saved. "Skip this" puts it away; when every step is done it
/// says "All set." for a few seconds and is gone for good (UserPrefs.OnboardDone). A player who already has saved runs
/// never sees it: the first start of this version sets it done. The Crafting tab keeps its one line.
/// </summary>
public partial class MainWindow
{
    internal sealed record OnboardStep(string Title, bool Done, string Sub, string Link, Action? Go);

    /// <summary>When "All set." stops showing (UTC); the checklist is already done by then.</summary>
    DateTime _onboardAllSetUntil = DateTime.MinValue;
    /// <summary>The saved runs, looked up at most every few seconds (it reads sessions.jsonl).</summary>
    int _onboardRuns = -1;
    DateTime _onboardRunsAt = DateTime.MinValue;
    /// <summary>For the layout render: the checklist drawn in the sample window, nothing saved.</summary>
    bool _onboardShot;

    /// <summary>At start: a player with saved runs is set up already.</summary>
    void OnboardAtStart()
    {
        if (_prefs.OnboardDone || SessionStore.Count() == 0) return;
        _prefs.OnboardDone = true;
        _prefs.Save();
        CrashLog.Note("first-session checklist: not shown (runs are saved already)");
    }

    /// <summary>The panel was put somewhere on purpose (the guide's Enter, a drag, locking it in Options).</summary>
    void MarkPanelPlaced()
    {
        if (_hudPrefs.Placed || _demo) return;
        _hudPrefs.Placed = true;
        _hudPrefs.Save();
    }

    int SavedRuns()
    {
        if (_onboardRuns < 0 || (DateTime.UtcNow - _onboardRunsAt).TotalSeconds >= 5) { _onboardRuns = SessionStore.Count(); _onboardRunsAt = DateTime.UtcNow; }
        return _onboardRuns;
    }

    /// <summary>The steps while the checklist shows (or "All set." holds), else null.</summary>
    List<OnboardStep>? OnboardSteps()
    {
        bool allSet = DateTime.UtcNow < _onboardAllSetUntil;
        if (_onboardShot) return SampleOnboard();
        if (_demo || _welcome || (_prefs.OnboardDone && !allSet)) return null;
        var c = _watcher.Chat;
        var m = _watcher.Market;
        if (!_prefs.OnboardMarketRead && (m.Detected || m.AsksSeen > 0)) { _prefs.OnboardMarketRead = true; _prefs.Save(); }
        var steps = new List<OnboardStep>
        {
            new("Server picked", HasServer, "Everything shared is filed under your server.", "Pick", () => OpenOptions(OptionsWindow.Page.Server)),
            new("Aion 2 open", _watcher.GameRect != null, "Reading starts on its own once it is open.", "", null),
            new("Chat found", c != null && c.ChatFound, "Keep the chat window open at the bottom left of the game.", "Game reading", () => OpenOptions(OptionsWindow.Page.Reading)),
            new("Market read once", _prefs.OnboardMarketRead, "Open the Market in game once; prices come from there.", "", null),
            new("Panel placed", _hudPrefs.Placed || !_prefs.Hud, "Put the panel where it doesn't cover anything you need.", "Place the panel…", PlacePanelFromChecklist),
            new("First run saved", SavedRuns() > 0, "Start run, farm for a minute, Stop.", "", null),
        };
        if (!allSet && steps.All(s => s.Done))
        {
            // every step done: "All set." for a few seconds, and done for good from now
            _onboardAllSetUntil = DateTime.UtcNow.AddSeconds(5);
            _prefs.OnboardDone = true;
            _prefs.Save();
            CrashLog.Note("first-session checklist: all set");
        }
        return steps;
    }

    void PlacePanelFromChecklist()
    {
        if (OpenPlacementGuide() is { } why) Say(why, "Warn");
    }

    static string OnboardSig(List<OnboardStep>? steps) =>
        steps == null ? "" : string.Concat(steps.Select(s => s.Done ? '1' : '0')) + (steps.All(s => s.Done) ? "|all" : "");

    /// <summary>The card under the empty feed's line: heading, six rows, Skip this; or nothing.</summary>
    void DrawOnboard(List<OnboardStep>? steps)
    {
        FeedOnboard.Children.Clear();
        FeedOnboard.Visibility = steps == null ? Visibility.Collapsed : Visibility.Visible;
        if (steps == null) return;
        bool all = steps.All(s => s.Done);

        var head = new Grid { Margin = new Thickness(0, 0, 0, 6) };
        head.ColumnDefinitions.Add(new ColumnDefinition()); head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        head.Children.Add(PT(all ? "All set." : "Getting set up", all ? "Good" : "Ink", 13.5, FontWeights.Normal));
        var count = PT($"{steps.Count(s => s.Done)} of {steps.Count}", "Faint", 12);
        Grid.SetColumn(count, 1); head.Children.Add(count);
        FeedOnboard.Children.Add(head);
        if (all)
        {
            FeedOnboard.Children.Add(PT("Pick something up and it shows here, newest first.", "Faint", 12.5));
            return;
        }

        foreach (var s in steps)
        {
            var g = new Grid { Margin = new Thickness(0, 3, 0, 3) };
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(20) }); g.ColumnDefinitions.Add(new ColumnDefinition()); g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var glyph = PT(s.Done ? "✓" : "○", s.Done ? "Good" : "Faint", 13, FontWeights.Normal);
            glyph.VerticalAlignment = VerticalAlignment.Top;
            g.Children.Add(glyph);
            var words = new StackPanel();
            words.Children.Add(PT(s.Title, s.Done ? "Muted" : "Ink", 12.5));
            if (!s.Done && s.Sub.Length > 0) { var sub = PT(s.Sub, "Faint", 11.5); sub.TextWrapping = TextWrapping.Wrap; sub.TextTrimming = TextTrimming.None; words.Children.Add(sub); }
            Grid.SetColumn(words, 1); g.Children.Add(words);
            if (!s.Done && s.Link.Length > 0 && s.Go is { } go)
            {
                var t = new TextBlock { FontSize = 12, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(10, 0, 0, 0) };
                var h = new Hyperlink(new Run(s.Link)) { Cursor = Cursors.Hand };
                h.SetResourceReference(FrameworkContentElement.StyleProperty, "LinkStyle");
                h.Click += (_, _) => go();
                t.Inlines.Add(h);
                Grid.SetColumn(t, 2); g.Children.Add(t);
            }
            FeedOnboard.Children.Add(g);
        }

        var skipLine = new TextBlock { FontSize = 11.5, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 6, 0, 0) };
        var skip = new Hyperlink(new Run("Skip this")) { Cursor = Cursors.Hand, TextDecorations = null };
        skip.SetResourceReference(TextElement.ForegroundProperty, "Faint");
        skip.Click += (_, _) => SkipOnboard();
        skipLine.Inlines.Add(skip);
        FeedOnboard.Children.Add(skipLine);
    }

    void SkipOnboard()
    {
        _onboardShot = false;
        if (!_demo) { _prefs.OnboardDone = true; _prefs.Save(); CrashLog.Note("first-session checklist: skipped"); }
        _feedEmptySig = "";
        FillFeedEmpty();
    }

    /// <summary>For the layout render: a player halfway through (a server, the game, the chat; not yet the rest).</summary>
    List<OnboardStep> SampleOnboard() => new()
    {
        new("Server picked", true, "", "Pick", null),
        new("Aion 2 open", true, "Reading starts on its own once it is open.", "", null),
        new("Chat found", true, "Keep the chat window open at the bottom left of the game.", "Game reading", null),
        new("Market read once", false, "Open the Market in game once; prices come from there.", "", null),
        new("Panel placed", false, "Put the panel where it doesn't cover anything you need.", "Place the panel…", () => { }),
        new("First run saved", false, "Start run, farm for a minute, Stop.", "", null),
    };

    /// <summary>For ui-shot: the empty Farming feed with the sample checklist.</summary>
    internal void ShowOnboardSample(bool on)
    {
        _onboardShot = on;
        _feedEmptySig = "";
        FillFeedEmpty();
    }
}
