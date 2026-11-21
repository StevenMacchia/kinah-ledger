using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace KinahLedger.Ui;

/// <summary>
/// Options › Companion (Buddy 2.0, 2026-11-20): the little companion that reacts to the session. Its switch (off by
/// default) in the header, a stage where it plays each reaction in turn in the overlay theme in use, the bubble's lines and
/// where it sits, and folded, one switch per reaction with a Try button that plays it. These save as they change
/// (companion.json), apart from Options' Save, as the Timers and Applicants pages'.
/// </summary>
public partial class OptionsWindow
{
    internal CheckBox? CompanionChk { get; private set; }
    internal CheckBox? CompanionLinesChk { get; private set; }
    internal CheckBox? CompanionThroughChk { get; private set; }
    internal Button? CompanionResetBtn { get; private set; }
    internal readonly Dictionary<CompanionKind, CheckBox> CompanionReactChks = new();
    internal CompanionFace? CompanionStageForTest => _companionStage;
    CompanionFace? _companionStage;
    DispatcherTimer? _companionCycle;
    int _companionNext;

    /// <summary>The reactions in the order the page lists them, each with its title and hint.</summary>
    internal static readonly (CompanionKind Kind, string Title, string Hint)[] CompanionReactions =
    {
        (CompanionKind.Rare, "Rare drop", "A hop and a burst of sparks in the drop's grade colour."),
        (CompanionKind.Best, "New best", "A crown and rays when you beat your best on that fight."),
        (CompanionKind.Kill, "Boss down", "A happy bounce and confetti on a kill with no new best."),
        (CompanionKind.Streak, "Hot streak", "Flames after three big hits of yours within 6 seconds."),
        (CompanionKind.Timer, "Timer close", "An alert as a watched boss or event comes close."),
        (CompanionKind.Pvp, "Takedown", "Crossed blades for a kill or an assist on an enemy player."),
        (CompanionKind.Wipe, "Wipe", "A rain cloud when the group wipes."),
        (CompanionKind.Death, "You fall", "Dizzy stars when you fall."),
    };

    void FillCompanion()
    {
        PageCompanion.Children.Clear();
        CompanionReactChks.Clear();
        var p = _host.CompanionChoices;
        void Add(UIElement e) => PageCompanion.Children.Add(e);

        CompanionChk = Switch(p.On, on => _host.SetCompanionOverlay(on));
        CompanionChk.ToolTip = L.T("A little winged companion beside the game that reacts to what happens in your session.");
        Add(OverlayHeader(Styled(new TextBlock { Text = L.T("Companion") }, "PageTitle"),
            Styled(new TextBlock { Text = L.T("A little winged companion that floats beside the game and reacts to your drops, bests and fights. These save as you change them."), TextWrapping = TextWrapping.Wrap }, "PageLead"),
            CompanionChk, "Show the companion over the game"));

        // the stage: the companion in the theme in use, playing each reaction in turn
        string theme = HudThemes.Of(HudLooks.Parse(_edit.HudLook));
        try
        {
            _companionStage = new CompanionFace(theme, _edit.HudOpacity > 0 ? _edit.HudOpacity : 0.94, true, false);
            Add(Margined(Stage(_companionStage, L.T("Companion"), L.T("a sample: each reaction in turn"), 150), new Thickness(0, 12, 0, 0)));
            StageTry(CompanionKind.Best, hold: true);
            _companionCycle?.Stop();
            if (Motion.On && Motion.FrozenAt == null)
            {
                _companionStage.Sprite.Start();
                _companionCycle = new DispatcherTimer(TimeSpan.FromMilliseconds(CompanionFace.SayMs + 500), DispatcherPriority.Background, (_, _) =>
                {
                    if (!PageCompanion.IsVisible) return;
                    var k = CompanionReactions[_companionNext++ % CompanionReactions.Length].Kind;
                    StageTry(k);
                }, Dispatcher);
                _companionCycle.Start();
                Closed += (_, _) => _companionCycle?.Stop();
            }
        }
        catch (Exception ex) { CrashLog.Write("companion preview", ex); }

        var g = new StackPanel();
        CompanionLinesChk = Switch(p.Lines, on => _host.SetCompanionLines(on));
        g.Children.Add(Row("Lines", "A short line beside each reaction: \"NEW BEST · Gadioton +8.2%\".", CompanionLinesChk));
        g.Children.Add(Styled(new Border(), "Divider"));
        CompanionResetBtn = new Button { Content = L.T("Reset position"), ToolTip = L.T("Back to just right of the Kinah overlay."), Margin = new Thickness(0, 0, 18, 0) };
        CompanionResetBtn.Click += (_, _) => _host.ResetCompanionPosition();
        CompanionThroughChk = Switch(p.ClickThrough, on => _host.SetCompanionClickThrough(on));
        System.Windows.Automation.AutomationProperties.SetName(CompanionThroughChk, "Companion click-through");
        var said = new TextBlock { Text = L.T("Click-through"), FontSize = 13, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0),
            ToolTip = L.T("On: clicks pass through to the game; the companion itself stays grabbable, with a grip when you point at it.") };
        said.SetResourceReference(TextBlock.ForegroundProperty, "Ink");
        g.Children.Add(Row("Where it sits", "Drag it anywhere. Its look follows the overlay's theme.",
            new StackPanel { Orientation = Orientation.Horizontal, Children = { CompanionResetBtn, said, CompanionThroughChk } }));
        Add(Styled(new Border { Child = g, Margin = new Thickness(0, 10, 0, 0) }, "Group"));

        // reaction by reaction, folded: a switch each, and Try
        var which = new StackPanel { Tag = "Reactions" };
        var list = new StackPanel();
        bool first = true;
        foreach (var (kind, title, hint) in CompanionReactions)
        {
            if (!first) list.Children.Add(Styled(new Border(), "Divider"));
            first = false;
            var k = kind;
            var on = Switch(p.Reacts(k), v => { _host.SetCompanionReacts(k, v); RefreshFoldCounts(); });
            System.Windows.Automation.AutomationProperties.SetName(on, "React: " + title);
            CompanionReactChks[k] = on;
            var tryBtn = new Button { Content = L.T("Try"), Margin = new Thickness(0, 0, 14, 0), ToolTip = L.T("Plays it now, here and on the companion over the game.") };
            tryBtn.Click += (_, _) => { StageTry(k); _host.TryCompanion(k); };
            list.Children.Add(Row(title, hint, new StackPanel { Orientation = Orientation.Horizontal, Children = { tryBtn, on } }));
        }
        which.Children.Add(Styled(new Border { Child = list }, "Group"));
        Add(which);
        Fold(which, () => L.F("{0} of {1} on", CompanionReactChks.Values.Count(c => c.IsChecked == true), CompanionReactChks.Count));
    }

    /// <summary>The stage's companion plays <paramref name="k"/> (or, <paramref name="hold"/>, holds its key frame with the bubble up).</summary>
    void StageTry(CompanionKind k, bool hold = false)
    {
        if (_companionStage == null) return;
        var m = MainWindow.CompanionSample(k, DateTime.UtcNow);
        if (hold || !Motion.On || Motion.FrozenAt != null)
        {
            _companionStage.Sprite.HoldForRender(k, m.Tone, CompanionSprite.KeyMs(k), 0.25);
            _companionStage.HoldForRender(m, 1400);
            return;
        }
        _companionStage.Sprite.React(k, m.Tone);
        _companionStage.Say(m);
    }
}
