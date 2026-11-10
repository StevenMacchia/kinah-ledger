using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using KinahLedger.Data;
using KinahLedger.Session;
using WF = System.Windows.Forms;

namespace KinahLedger.Ui;

/// <summary>
/// The run-end recap (Steven, 2026-11-04): when a farming run stops (a minute or more, with something counted) or an
/// expedition or event closes with loot or a reward, a card says how it went: the time, the Kinah, the loot after the
/// Market's fee, the best drop and, when there is one, the player's best in that zone; Share opens the share sheet on
/// Last run. Options › General › Show a recap when a run ends turns it off.
///
/// It never takes the game's place: a card is a Popup, and a Popup sits on top of everything, so it opens only while the
/// tracker's window is the active one. A run that ends while the player is in the game waits (for a quarter of an hour)
/// and shows when they next bring the window up; the HUD flash and the status line say it at once, as before. With the
/// window hidden in the tray a tray notice says "Run done: 1.2M Kinah". Nothing calls Activate().
/// </summary>
public partial class MainWindow
{
    /// <summary>A recap that ended while the window was not the active one, and when (UTC).</summary>
    Func<FrameworkElement>? _recapWaiting;
    DateTime _recapWaitingAt;
    const double RecapWaitMinutes = 15;
    /// <summary>The run log whose RunClosed the window listens to (the chat reader, and so its log, is made again when the game restarts).</summary>
    InstanceLog? _recapLog;

    void InitRecap()
    {
        Activated += (_, _) =>
        {
            if (_recapWaiting is not { } build) return;
            _recapWaiting = null;
            if ((DateTime.UtcNow - _recapWaitingAt).TotalMinutes > RecapWaitMinutes || !_prefs.RunRecap) return;
            // after the click that brought the window up has been handled, so it doesn't close the card it opened
            Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Background, () => OpenRecap(build()));
        };
    }

    /// <summary>From the tick: listen to the current run log's closed runs.</summary>
    void HookRunClosed(TrackerSession? chat)
    {
        var log = chat?.Instances;
        if (log == null || ReferenceEquals(log, _recapLog)) return;
        if (_recapLog != null) _recapLog.RunClosed = null;
        _recapLog = log;
        // called under the log's lock on the reader's thread: only queued from there
        log.RunClosed = r => Dispatcher.BeginInvoke(() =>
        {
            try { RecapExpedition(r); } catch (Exception ex) { CrashLog.Write("recap", ex); }
        });
    }

    /// <summary>
    /// From StopTracking, after the run is kept for Share's Last run: a run of a minute or more with something counted.
    /// <paramref name="bestBefore"/> is the zone's best as it was before this run was offered to it.
    /// </summary>
    void RecapFarm(SessionEngine e, long worth, BestRuns.Best? bestBefore)
    {
        if (_demo || !_prefs.RunRecap || e.Active.TotalSeconds < 60 || e.Events == 0) return;
        if (_shareFarmRun is not { } fr) return;
        var f = fr.Facts;
        long kinah = e.Totals().Kinah;
        string best = "";
        if (bestBefore is { PerHr: > 0 } b)
        {
            double hours = f.Seconds / 3600;
            long rate = hours > 0 ? (long)Math.Round(worth / hours) : 0;
            best = f.Seconds >= BestRuns.MinMinutes * 60 && rate > b.PerHr
                ? $"A new best here: {Fk(rate)} Kinah/h (it was {Fk(b.PerHr)})"
                : $"Your best here: {Fk(b.PerHr)} Kinah/h";
        }
        OfferRecap(() => BuildRecapCard("Run done", f, kinah, best, expedition: false), $"Run done: {Fk(worth)} Kinah");
    }

    /// <summary>An expedition, event or world boss kept by the run log, with loot or a reward.</summary>
    void RecapExpedition(InstanceRun r)
    {
        if (_demo || !_prefs.RunRecap) return;
        if (r.Kinah <= 0 && r.Items.Count == 0 && r.BoundItems.Count == 0) return;
        var f = EventShareFacts(r);
        var what = r.Kind switch { "Event" => "Event", InstanceLog.WorldBoss => "World boss", _ => "Expedition" };
        string best = BestRuns.For(Server?.Label ?? "", r.Zone) is { PerHr: > 0 } b ? $"Your best here: {Fk(b.PerHr)} Kinah/h" : "";
        OfferRecap(() => BuildRecapCard($"{what} done · {r.Label}", f, r.Kinah, best, expedition: true), $"{what} done: {Fk(f.Made)} Kinah");
    }

    /// <summary>The card now when the window is the active one; else it waits for the window, with a tray notice when the window is in the tray.</summary>
    void OfferRecap(Func<FrameworkElement> build, string trayTitle)
    {
        if (IsVisible && WindowState != WindowState.Minimized && IsActive) { OpenRecap(build()); return; }
        _recapWaiting = build; _recapWaitingAt = DateTime.UtcNow;
        if (IsVisible) return;
        try { _tray.ShowBalloonTip(6000, trayTitle, "Open the tracker for the recap.", WF.ToolTipIcon.Info); }
        catch (Exception ex) { CrashLog.Write("recap notice", ex); }
    }

    void OpenRecap(FrameworkElement card)
    {
        if (_card != null) _card.IsOpen = false;
        _card = new InCard
        {
            Child = card, StaysOpen = false, AllowsTransparency = true, PopupAnimation = PopupAnimation.Fade,
            Placement = PlacementMode.Center, PlacementTarget = Content as UIElement ?? this,
        };
        _card.IsOpen = true;
    }

    /// <summary>The card: its title and place, then the time, Kinah, loot, best drop, the comparison, the total, and Share · Close.</summary>
    internal FrameworkElement BuildRecapCard(string title, ShareFacts f, long kinah, string best, bool expedition)
    {
        var (card, body) = CkShell(380);
        var head = new StackPanel { Margin = new Thickness(20, 16, 20, 4) };
        var t = CkText(title, CkGold, 19, FontWeights.Normal, display: true); t.TextWrapping = TextWrapping.Wrap; t.TextTrimming = TextTrimming.None;
        head.Children.Add(t);
        var sub = string.Join(" · ", new[] { expedition ? "" : f.Title, f.Server, f.Day.ToString("ddd HH:mm") }.Where(x => x.Length > 0));
        if (sub.Length > 0) { var s = CkText(sub, CkFaint, 12.5); s.Margin = new Thickness(0, 3, 0, 0); head.Children.Add(s); }
        body.Children.Add(head);

        body.Children.Add(CkSection("THIS RUN"));
        body.Children.Add(CkKv("Time", Clock(f.Seconds), CkInk));
        if (f.Count > 0) body.Children.Add(CkKv(f.CountWord == "attempt" ? "Attempts" : "Kills", f.Count.ToString("N0"), CkInk));
        body.Children.Add(CkKv("Kinah", kinah > 0 ? Fk(kinah) : "none", kinah > 0 ? CkGold : CkFaint));
        long loot = Math.Max(0, f.Made - Math.Max(0, kinah));
        int pieces = f.Drops.Sum(d => d.Count);
        body.Children.Add(CkKv("Loot", loot > 0 ? Fk(loot) : pieces > 0 ? "no price yet" : "none", loot > 0 ? CkGold : CkFaint,
            sub: pieces > 0 ? $"{pieces:N0} item{(pieces == 1 ? "" : "s")}, {PriceBook.Cut}" : ""));

        if (f.Drops.Where(d => d.Count > 0).OrderByDescending(d => d.Worth).ThenByDescending(d => SharePicture.GradeRank(d.Grade)).FirstOrDefault() is { } top)
        {
            body.Children.Add(CkSection("BEST DROP"));
            var g = new Grid { Margin = new Thickness(20, 2, 20, 2) };
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); g.ColumnDefinitions.Add(new ColumnDefinition()); g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var icon = CkIcon(top.Name, 36, GradeColor(top.Grade), glow: top.Grade is "Legend" or "Unique" or "Epic"); icon.Margin = new Thickness(0, 0, 12, 0);
            g.Children.Add(icon);
            var n = CkText($"{top.Name} ×{top.Count:N0}", top.Grade is "" or "Common" ? CkInk : GradeColor(top.Grade), 13.5, FontWeights.Normal); Grid.SetColumn(n, 1); g.Children.Add(n);
            var w = CkText(top.Worth > 0 ? Fk(top.Worth) : "no price", top.Worth > 0 ? CkGold : CkFaint, 13, FontWeights.Normal, mono: true); w.Margin = new Thickness(10, 0, 0, 0); Grid.SetColumn(w, 2); g.Children.Add(w);
            body.Children.Add(g);
        }
        if (best.Length > 0) { var b = CkNote(best); b.Margin = new Thickness(20, 12, 20, 0); body.Children.Add(b); }

        var made = CkFooter("Made", PriceBook.Cut, Fk(f.Made), f.Made > 0);
        made.CornerRadius = new CornerRadius(0);
        body.Children.Add(made);
        body.Children.Add(CkActions(("Share", () => ShareLastRun(expedition)), ("Close", () => { })));
        return card;
    }

    static string Clock(double seconds)
    {
        var s = TimeSpan.FromSeconds(Math.Max(0, seconds));
        return s.TotalHours >= 1 ? $"{(int)s.TotalHours}:{s:mm\\:ss}" : $"{s:m\\:ss}";
    }

    /// <summary>Share on the recap: the share sheet open on Last run, on the page the run belongs to.</summary>
    void ShareLastRun(bool expedition)
    {
        var page = expedition ? "Events" : "Farming";
        if (_part != page) ShowPart(page);
        if (_shareOn != page) ToggleShare(page);
        _shareScope = SharePicture.Run;
        _shareSaved = "";
        DrawShare();
    }

    /// <summary>ui-recap.png: the sample run's recap card.</summary>
    internal void ShotRecapCard(string path) => SaveCard((Border)SampleRecapCard(), path);

    /// <summary>For ui-shot: a recap of the sample run (the layout render; nothing is saved).</summary>
    internal FrameworkElement SampleRecapCard()
    {
        var drops = new List<ShareDrop> { new("Thick Balaur Horn", "Legend", 2, 186_000), new("Fine Orichalcum Ore", "Rare", 14, 61_000), new("Spiritstone", "Common", 37, 9_000) };
        var f = new ShareFacts(SharePicture.Run, DateTime.Now, ShareServer(), _demoZone ?? "Urugugu Canyon", "Farming run", 47 * 60 + 12, 312, "kill", 942_000, drops);
        return BuildRecapCard("Run done", f, 686_000, "Your best here: 1.4M Kinah/h", expedition: false);
    }
}
