using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using KinahLedger.Session;

namespace KinahLedger.Ui;

/// <summary>
/// The Timers page (redesign 2026-11-14): the events and world bosses, a page of their own in the row of pages, so they are
/// never more than one click away (Steven: "I can not for the life of me find the tracker for the world boss spawns"). The
/// page shows the timers' card (Ui/MainWindow.Bosses.cs) at the ledger's width; Pop out opens the same card as a window of its
/// own, to keep on a second screen. The Timers tab carries the next timer's countdown once it is under 15 minutes away.
/// </summary>
public partial class MainWindow
{
    DispatcherTimer? _timersTick;
    /// <summary>How close the next timer must be before its countdown rides on the Timers tab.</summary>
    static readonly TimeSpan TabCountdownFrom = TimeSpan.FromMinutes(15);

    /// <summary>Starts the second's tick that keeps the tab's countdown, and the page while it shows, up to date.</summary>
    void StartTimersTick()
    {
        if (_timersTick != null || _demo) return;
        _timersTick = new DispatcherTimer(TimeSpan.FromSeconds(1), DispatcherPriority.Background, (_, _) => { FillTimersTab(DateTime.UtcNow); if (_part == "Timers") FillTimersPage(); }, Dispatcher);
        _timersTick.Start();
    }

    /// <summary>The page: its title and Pop out, then the timers' card at the ledger's width. Drawn only while it shows.</summary>
    void FillTimersPage()
    {
        if (_part != "Timers" || _welcome) { if (TimersPage.Children.Count > 0) TimersPage.Children.Clear(); return; }
        StartTimersTick();
        var now = DateTime.UtcNow;
        FillTimersTab(now);
        double scroll = LowerScroll.VerticalOffset;
        TimersPage.Children.Clear();
        TimersPage.Children.Add(TimersHead());
        TimersPage.Children.Add(TimersPageCard(now));
        if (scroll > 0) { LowerScroll.UpdateLayout(); LowerScroll.ScrollToVerticalOffset(scroll); }
    }

    Grid TimersHead()
    {
        var g = new Grid { Margin = new Thickness(2, 0, 0, 12) };
        g.ColumnDefinitions.Add(new ColumnDefinition());
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var title = new TextBlock { Text = "Timers", FontSize = 20, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center };
        title.SetResourceReference(TextBlock.FontFamilyProperty, "DisplayFont");
        title.SetResourceReference(TextBlock.ForegroundProperty, "Ink");
        g.Children.Add(title);
        var pop = new Button { Content = "Pop out", Height = 30, FontSize = 12.5, Padding = new Thickness(12, 0, 12, 0), VerticalAlignment = VerticalAlignment.Center,
            ToolTip = "Opens the timers in a window of their own, to keep beside the game or on a second screen." };
        System.Windows.Automation.AutomationProperties.SetName(pop, "Pop out the timers");
        pop.Click += (_, _) => OpenBossWindow();
        Grid.SetColumn(pop, 1); g.Children.Add(pop);
        return g;
    }

    /// <summary>The timers' card as the page shows it: the window's card at the ledger's width, flat, without the window's own title band.</summary>
    Border TimersPageCard(DateTime now)
    {
        var card = BuildBossCard(now);
        card.Effect = null; card.Margin = new Thickness(0);
        card.SetResourceReference(Border.BackgroundProperty, "Card");
        card.SetResourceReference(Border.BorderBrushProperty, "Line");
        if (card.Child is StackPanel body)
        {
            body.Width = 662 - 2;
            if (body.Children.Count > 0) body.Children.RemoveAt(0);   // the window's band: the page has its own title
            body.Children.Insert(0, new Border { Height = 6 });
        }
        return card;
    }

    /// <summary>The Timers tab's countdown: the soonest event or boss when it is under <see cref="TabCountdownFrom"/>, else nothing.</summary>
    void FillTimersTab(DateTime now)
    {
        TimeSpan? next = null;
        foreach (var r in EventClock.Rows(now)) if (!r.OnNow && r.Left > TimeSpan.Zero && (next == null || r.Left < next)) next = r.Left;
        foreach (var r in Bosses.Rows(now)) if (r.Boss.State == BossTimers.Kind.Waiting && !r.Due && r.Left > TimeSpan.Zero && (next == null || r.Left < next)) next = r.Left;
        if (next is TimeSpan left && left <= TabCountdownFrom)
        {
            TimersTabNext.Text = $"{(int)left.TotalMinutes}:{left.Seconds:00}";
            TimersTabNext.Visibility = Visibility.Visible;
        }
        else TimersTabNext.Visibility = Visibility.Collapsed;
    }
}

public partial class MainWindow
{
    /// <summary>ui-shot --timers-page: the page's sample readings, as the timers' window renders take them (live or empty; off puts the real ones back).</summary>
    internal void TimersSampleForShot(string state)
    {
        if (state == "off") { _bossesForShot = null; _eventsForShot = null; return; }
        var now = DateTime.UtcNow;
        var t = new BossTimers();
        if (state != "empty") t.SetForTest(SampleBosses(now.AddSeconds(-4)));
        _bossesForShot = t;
        var ev = new EventTimers();
        ev.SetForTest(state == "empty" ? TimeSpan.Zero : TimeSpan.FromSeconds(14.6), state != "empty", state == "live"
            ? new EventTimers.Live("Shugo Festival", "Defend Shugo Merchants", Packets.AreaEvent.Stage.UnderWay, now.AddSeconds(-70), now.AddSeconds(-1), null, null) : null);
        _eventsForShot = ev;
    }
}
