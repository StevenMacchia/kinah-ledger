using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Effects;
using KinahLedger.Data;
using KinahLedger.Session;
using WF = System.Windows.Forms;

namespace KinahLedger.Ui;

/// <summary>
/// The run-end recap (Steven, 2026-11-04): when a farming run stops (a minute or more, with something counted) or an
/// expedition or event closes with loot or a reward, a card says how it went: the time, the Kinah, the loot after the
/// Market's fee, the drops and, when there is one, the player's best in that zone; Share opens the share sheet on
/// Last run. Options › General › Show a recap when a run ends turns it off.
///
/// It never takes the game's place: a card is a Popup, and a Popup sits on top of everything, so it opens only while the
/// tracker's window is the active one. A run that ends while the player is in the game waits (for a quarter of an hour)
/// and shows when they next bring the window up; the HUD flash and the status line say it at once, as before. With the
/// window hidden in the tray a tray notice says "Run done: 1.2M Kinah". Nothing calls Activate().
///
/// The card comes alive as it opens (2026-11-11, Steven: "cool and flashy"): it rises into place, its figures count up one
/// after another, its drops land one by one (the best glowing once), and a gold "New best here" tag sweeps in, or a line
/// says how the run stands against your average there. Once, on the run's end; with motion off it simply shows settled.
/// </summary>
public partial class MainWindow
{
    /// <summary>A recap that ended while the window was not the active one, and when (UTC).</summary>
    Func<RecapCard>? _recapWaiting;
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
        var standing = FarmStanding(e, bestBefore);
        OfferRecap(() => BuildRecap("Run done", f, kinah, standing, expedition: false), $"Run done: {Fk(worth)} Kinah");
    }

    /// <summary>
    /// A farming run against the zone it ended in, by its stay there (a run that moved zones is set against that zone's
    /// history with that zone's own Kinah, loot and time, as the best there is kept): a new best when its hour beats the best
    /// there (over ten minutes), else the best there and, with Compare zones and runs on and three earlier stays there, its
    /// hour against theirs. Worked out when the run ends, so a run saved while the card waits for the window is never counted
    /// among the earlier ones.
    /// </summary>
    RecapStanding FarmStanding(SessionEngine e, BestRuns.Best? bestBefore)
    {
        double seconds = 0; long worth = 0;
        if (e.Zone is { } zone && e.ZoneSnapshot().TryGetValue(zone, out var z))
        {
            seconds = z.Seconds;
            worth = BestRuns.Value(z.Kinah, z.Items.Select(kv => (_items.NameOf(kv.Key), kv.Value)));
        }
        double hours = seconds / 3600;
        long rate = hours > 0 ? (long)Math.Round(worth / hours) : 0;
        bool timed = seconds >= BestRuns.MinMinutes * 60;
        var b = bestBefore is { PerHr: > 0 } had ? had : null;
        if (b != null && timed && rate > b.PerHr) return new($"{Fk(rate)} Kinah/h · it was {Fk(b.PerHr)}", true, null);
        var best = b != null ? $"Your best here: {Fk(b.PerHr)} Kinah/h" : "";
        // the line against your average is one of Compare's figures: only with Options › Compare zones and runs on
        if (!_prefs.CompareZones || !timed || rate <= 0) return new(best, false, null);
        var earlier = BestRuns.Stays(Server?.Label ?? "", e.Zone, SessionStore.StartedKey(e.Started));
        return new(best, false, RunStanding.HourAverage(earlier) is double avg ? RunStanding.Share(rate, avg) : null);
    }

    /// <summary>An expedition, event or world boss kept by the run log, with loot or a reward.</summary>
    void RecapExpedition(InstanceRun r)
    {
        if (_demo || !_prefs.RunRecap) return;
        if (r.Kinah <= 0 && r.Items.Count == 0 && r.BoundItems.Count == 0) return;
        var f = EventShareFacts(r);
        var what = r.Kind switch { "Event" => "Event", InstanceLog.WorldBoss => "World boss", _ => "Expedition" };
        var standing = PlaceStanding(r);
        OfferRecap(() => BuildRecap($"{what} done · {r.Label}", f, r.Kinah, standing, expedition: true), $"{what} done: {Fk(f.Made)} Kinah");
    }

    /// <summary>
    /// An expedition or event against the earlier counted runs of its place (as the Events page's Compare counts them): a new
    /// best when it beats every one, else (with Compare zones and runs on) its worth against their average. With fewer than
    /// three, or a flagged run, only the zone's best hour, as before.
    /// </summary>
    RecapStanding PlaceStanding(InstanceRun r)
    {
        string best = BestRuns.For(Server?.Label ?? "", r.Zone) is { PerHr: > 0 } b ? $"Your best here: {Fk(b.PerHr)} Kinah/h" : "";
        if (r.OutOfAverages) return new(best, false, null);
        var place = ZoneCompare.PlaceOf(r);
        var earlier = WeekRunList().Where(x => x.Start < r.Start && !x.OutOfAverages && string.Equals(ZoneCompare.PlaceOf(x), place, StringComparison.OrdinalIgnoreCase)).Select(RunValue);
        if (ZoneCompare.VsPlace(RunValue(r), earlier) is not { Average: > 0 } v) return new(best, false, null);
        if (v.This > v.Best) return new($"Best of your {v.Of:N0} runs here · it was {Fk(v.Best)}", true, null);
        return new(best, false, _prefs.CompareZones ? RunStanding.Share(v.This, v.Average) : null);
    }

    /// <summary>The card now when the window is the active one; else it waits for the window, with a tray notice when the window is in the tray.</summary>
    void OfferRecap(Func<RecapCard> build, string trayTitle)
    {
        if (IsVisible && WindowState != WindowState.Minimized && IsActive) { OpenRecap(build()); return; }
        _recapWaiting = build; _recapWaitingAt = DateTime.UtcNow;
        if (IsVisible) return;
        try { _tray.ShowBalloonTip(6000, trayTitle, "Open the tracker for the recap.", WF.ToolTipIcon.Info); }
        catch (Exception ex) { CrashLog.Write("recap notice", ex); }
    }

    /// <summary>
    /// The card opens and plays its opening; closed (Share, Close, a click outside, Esc), it leaves the way it came, quicker.
    /// With motion off it shows settled and goes at once, with the plain fade every card has.
    /// </summary>
    void OpenRecap(RecapCard recap)
    {
        if (_card != null) _card.IsOpen = false;
        var card = recap.Card;
        bool moves = Motion.On;
        _card = new InCard
        {
            Child = card, StaysOpen = false, AllowsTransparency = true, PopupAnimation = moves ? PopupAnimation.None : PopupAnimation.Fade,
            Placement = PlacementMode.Center, PlacementTarget = Content as UIElement ?? this,
            Leave = moves ? remove => Motion.Leave(card, remove) : null,
        };
        _card.IsOpen = true;
        Motion.Sequence(card, recap.Intro);
    }

    /* ---------------- the card ---------------- */

    /// <summary>
    /// What the card says about the run against the player's others there: the best line under the figures, whether it is a
    /// new best (the gold tag), and how far above or below their average there it came (null when the history is thin, or on
    /// a new best).
    /// </summary>
    internal sealed record RecapStanding(string Best, bool NewBest, double? VsAverage);

    /// <summary>A recap card, built settled, and its opening: what plays, in order, as it arrives.</summary>
    internal sealed record RecapCard(Border Card, Motion.Cues Intro);

    // The opening, in ms from the card starting to arrive: the Kinah counts up first, the time, the kills and the loot follow
    // a beat apart and the total comes last; the best drop lands as the card settles and the other drops after it, one after
    // another; the standing (the gold tag, or the line against your average) comes in last. All settled by about 1.4 s, but
    // for the best drop's glow fading out (the window's own glow, 1.4 s from its landing).
    const double RecapFiguresAt = 160, RecapFigureGap = 80, RecapFigureMs = 560;
    internal const double RecapBestDropAt = 300;
    const double RecapDropsAt = 380, RecapDropGap = 60, RecapDropTextMs = 280;
    const double RecapStandingAt = 820, RecapStandingMs = 360, RecapGlintAt = RecapStandingAt + 80, RecapGlintMs = 480;
    /// <summary>How far a line of the card travels into place as it fades in (px): the drop's name from the right, the standing from below.</summary>
    const double RecapTextTravel = 8, RecapLineRise = 6, RecapTagTravel = 28;
    /// <summary>The strip of drops under the best one holds this many tiles; with more, the last place says how many more.</summary>
    const int RecapTiles = 8;
    const double RecapTileIcon = 30, RecapTileBox = 36, RecapTileGap = 6;
    // the new-best tag: dark ink on gold (the window's primary-button ink), and the light of the glint that crosses it
    static readonly Color RecapTagInk = Color.FromRgb(0x2B, 0x1B, 0x00), RecapGlint = Color.FromRgb(0xFF, 0xF3, 0xD1);

    /// <summary>
    /// The card: its title and place (and the gold tag on a new best), then the time, kills, Kinah and loot, the drops (the best
    /// one, then a strip of the rest), the standing, the total, and Share · Close. Built settled, with its opening beside it.
    /// </summary>
    internal RecapCard BuildRecap(string title, ShareFacts f, long kinah, RecapStanding standing, bool expedition)
    {
        var (card, body) = CkShell(380);
        var intro = new Motion.Cues().Add(0, Motion.EnterMs, Motion.Enter(card));
        var head = new StackPanel { Margin = new Thickness(20, 16, 20, 4) };
        // the title, with the tag beside it on a new best (the line under it keeps the card's whole width)
        var top = new Grid();
        top.ColumnDefinitions.Add(new ColumnDefinition()); top.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var t = CkText(title, CkInk, 19, FontWeights.Normal, display: true);   // a heading: ink (colour pass, 2026-11-13); the money is the gold t.TextWrapping = TextWrapping.Wrap; t.TextTrimming = TextTrimming.None;
        top.Children.Add(t);
        if (standing.NewBest) { var tag = NewBestTag(intro); Grid.SetColumn(tag, 1); top.Children.Add(tag); }
        head.Children.Add(top);
        var sub = string.Join(" · ", new[] { expedition ? "" : f.Title, f.Server, f.Day.ToString("ddd HH:mm") }.Where(x => x.Length > 0));
        if (sub.Length > 0) { var s = CkText(sub, CkFaint, 12.5); s.Margin = new Thickness(0, 3, 0, 0); head.Children.Add(s); }
        body.Children.Add(head);

        // the figures count up in their turns: 0 the Kinah, then the time, the kills, the loot, and the total last
        void Count(FrameworkElement row, int turn, double to, Func<double, string> fmt, Color? tint = null) =>
            intro.Add(RecapFiguresAt + turn * RecapFigureGap, RecapFigureMs, Motion.CountUp(FigureOf(row), to, fmt, tint));
        static string Whole(double v) => Math.Round(v).ToString("N0");
        static string Money(double v) => Fk(Math.Round(v));

        body.Children.Add(CkSection("THIS RUN"));
        var time = CkKv("Time", Clock(f.Seconds), CkInk); body.Children.Add(time);
        Count(time, 1, f.Seconds, Clock);
        if (f.Count > 0)
        {
            var count = CkKv(f.CountWord == "attempt" ? "Attempts" : "Kills", f.Count.ToString("N0"), CkInk); body.Children.Add(count);
            Count(count, 2, f.Count, Whole);
        }
        // Kinah and Loot each get a line only when the loot adds something: with no loot worth anything Made is the Kinah, and
        // the total says it once (its line under it says it was all Kinah); unpriced pieces still get their Loot line
        long loot = Math.Max(0, f.Made - Math.Max(0, kinah));
        int pieces = f.Drops.Sum(d => d.Count);
        if (loot > 0)
        {
            var kin = CkKv("Kinah", kinah > 0 ? Fk(kinah) : "none", kinah > 0 ? CkGold : CkFaint); body.Children.Add(kin);
            if (kinah > 0) Count(kin, 0, kinah, Money);
        }
        if (loot > 0 || pieces > 0)
        {
            var lootRow = CkKv("Loot", loot > 0 ? Fk(loot) : "no price yet", loot > 0 ? CkGold : CkFaint, sub: pieces > 0 ? $"{pieces:N0} item{(pieces == 1 ? "" : "s")}, {PriceBook.Cut}" : "");
            body.Children.Add(lootRow);
            if (loot > 0) Count(lootRow, 3, loot, Money);
        }

        var drops = f.Drops.Where(d => d.Count > 0).OrderByDescending(d => d.Worth).ThenByDescending(d => SharePicture.GradeRank(d.Grade)).ThenByDescending(d => d.Count).ToList();
        if (drops.Count > 0) AddRecapDrops(body, intro, drops);
        AddRecapStanding(body, intro, standing);

        var made = CkFooter("Made", loot > 0 || kinah <= 0 ? PriceBook.Cut : "all Kinah", Fk(f.Made), f.Made > 0);
        made.CornerRadius = new CornerRadius(0);
        body.Children.Add(made);
        if (f.Made > 0) Count(made, 4, f.Made, Money);
        body.Children.Add(CkActions(("Share", () => ShareLastRun(expedition)), ("Close", () => { })));
        return new RecapCard(card, intro);
    }

    /// <summary>A key-value line's figure (CkKv's, or the footer's): the text in its grid's right-hand column.</summary>
    static TextBlock FigureOf(FrameworkElement row) =>
        (row is Border { Child: Grid footer } ? footer : (Grid)row).Children.OfType<TextBlock>().Single(x => Grid.GetColumn(x) == 1);

    /// <summary>
    /// DROPS: the best drop's line (its icon in its grade's frame, its name and count, its worth), then the rest as a strip of
    /// tiles, best first, each named in its tip. The best one lands as the card settles and glows once in its grade's colour
    /// (a Legend, Unique or Epic one, as a drop in the window does); the tiles land after it, one after another, plain.
    /// </summary>
    void AddRecapDrops(StackPanel body, Motion.Cues intro, List<ShareDrop> drops)
    {
        var top = drops[0];
        body.Children.Add(CkSection("DROPS"));
        var g = new Grid { Margin = new Thickness(20, 2, 20, 2) };
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); g.ColumnDefinitions.Add(new ColumnDefinition()); g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        // the icon in a holder of its own: the landing's glow comes and goes on the holder, and the icon's frame glow stays
        bool rare = top.Grade is "Legend" or "Unique" or "Epic";
        var icon = new Border { Child = CkIcon(top.Name, 36, GradeColor(top.Grade), glow: rare), Margin = new Thickness(0, 0, 12, 0) };
        g.Children.Add(icon);
        // in the dark cards' grade colour where they have one (a Special drop keeps the window's own)
        intro.Add(RecapBestDropAt, Motion.LandCourse(top.Grade), Motion.Landing(icon, top.Grade, rare ? GradeColor(top.Grade) : null));
        var n = CkText($"{top.Name} ×{top.Count:N0}", top.Grade is "" or "Common" ? CkInk : GradeColor(top.Grade), 13.5, FontWeights.Normal); Grid.SetColumn(n, 1); g.Children.Add(n);
        var w = CkText(top.Worth > 0 ? Fk(top.Worth) : "no price", top.Worth > 0 ? CkGold : CkFaint, 13, FontWeights.Normal, mono: true); w.Margin = new Thickness(10, 0, 0, 0); Grid.SetColumn(w, 2); g.Children.Add(w);
        intro.Add(RecapBestDropAt, RecapDropTextMs, Motion.Arrive(n, RecapTextTravel, 0));
        intro.Add(RecapBestDropAt, RecapDropTextMs, Motion.Arrive(w, RecapTextTravel, 0));
        body.Children.Add(g);

        var rest = drops.Skip(1).ToList();
        if (rest.Count == 0) return;
        var strip = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(20, 10, 20, 0) };
        int shown = rest.Count > RecapTiles ? RecapTiles - 1 : rest.Count;
        for (int i = 0; i < shown; i++)
        {
            var d = rest[i];
            var tile = new Grid
            {
                Width = RecapTileBox, Height = RecapTileBox, Margin = new Thickness(0, 0, RecapTileGap, 0), Background = Brushes.Transparent,
                ToolTip = $"{d.Name} ×{d.Count:N0} · {(d.Worth > 0 ? Fk(d.Worth) : "no price yet")}",
            };
            var face = CkIcon(d.Name, RecapTileIcon, GradeColor(d.Grade), badge: d.Count > 1 ? d.Count.ToString("N0") : "");
            face.HorizontalAlignment = HorizontalAlignment.Left; face.VerticalAlignment = VerticalAlignment.Top;
            tile.Children.Add(face);
            strip.Children.Add(tile);
            intro.Add(RecapDropsAt + i * RecapDropGap, Motion.LandMs, Motion.Landing(tile, null));
        }
        if (shown < rest.Count)
        {
            var more = CkText($"+{rest.Count - shown:N0}", CkMuted, 12.5);
            more.ToolTip = string.Join("\n", rest.Skip(shown).Select(d => $"{d.Name} ×{d.Count:N0}"));
            strip.Children.Add(more);
            intro.Add(RecapDropsAt + shown * RecapDropGap, RecapDropTextMs, Motion.Arrive(more, RecapTextTravel, 0));
        }
        body.Children.Add(strip);
    }

    /// <summary>
    /// The standing under the drops: on a new best, its figures (the tag in the head says it is one); else the line against
    /// your average here, its percent counting up in the tone of the way it went, over your best here.
    /// </summary>
    void AddRecapStanding(StackPanel body, Motion.Cues intro, RecapStanding standing)
    {
        if (standing.VsAverage is double share)
        {
            var (figure, words, sign) = RunStanding.Line(share);
            var tone = sign > 0 ? CkGood : sign < 0 ? CkWarn : CkMuted;
            var line = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(20, 12, 20, 0) };
            if (figure.Length > 0)
            {
                var pct = CkText(figure, tone, 13, FontWeights.SemiBold, mono: true);
                // as wide as its final text from the start, so the words after it don't slide as it counts up
                pct.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                pct.MinWidth = pct.DesiredSize.Width;
                line.Children.Add(pct);
                intro.Add(RecapStandingAt, RecapStandingMs, Motion.CountUp(pct, Math.Round(Math.Abs(share) * 100), v => RunStanding.Percent(sign, v)));
            }
            line.Children.Add(CkText(words, figure.Length > 0 ? CkMuted : tone, 12.5));
            body.Children.Add(line);
            intro.Add(RecapStandingAt, RecapStandingMs, Motion.Arrive(line, 0, RecapLineRise));
        }
        if (standing.Best.Length == 0) return;
        var b = CkNote(standing.Best);
        b.Margin = new Thickness(20, standing.VsAverage != null ? 3 : 12, 20, 0);
        body.Children.Add(b);
        if (standing.NewBest) intro.Add(RecapStandingAt, RecapStandingMs, Motion.Arrive(b, 0, RecapLineRise));
    }

    /// <summary>The gold "New best here" tag: it sweeps in from the right in its turn, and a glint crosses it once.</summary>
    Border NewBestTag(Motion.Cues intro)
    {
        var tag = new Border
        {
            Child = CkText("★ NEW BEST HERE", RecapTagInk, 12, FontWeights.SemiBold), CornerRadius = new CornerRadius(10), Padding = new Thickness(9, 3, 10, 4),
            VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(12, 3, 0, 0),
            Effect = new DropShadowEffect { Color = CkGold, BlurRadius = 14, ShadowDepth = 0, Opacity = 0.45 },
        };
        intro.Add(RecapStandingAt, RecapStandingMs, Motion.Arrive(tag, RecapTagTravel, 0));
        intro.Add(RecapGlintAt, RecapGlintMs, Motion.Glint(tag, CkGold, RecapGlint));
        return tag;
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

    /* ---------------- self-tests and layout renders ---------------- */

    /// <summary>ui-recap.png: the sample run's recap card.</summary>
    internal void ShotRecapCard(string path) => SaveCard(SampleRecap("run").Card, path);

    /// <summary>For options-test: a recap of the sample run (the layout render; nothing is saved).</summary>
    internal FrameworkElement SampleRecapCard() => SampleRecap("run").Card;

    /// <summary>The recap's sample states, for a render of its motion (ui-shot --recap) and the self-test.</summary>
    internal static readonly string[] RecapStates = { "run", "new-best", "no-loot", "long-name" };

    /// <summary>For ui-shot --recap and options-test: each state's card, and its opening to play (held at KL_MOTION_AT's point in a render).</summary>
    internal List<(string State, Border Card, Action Open)> RecapSamples() =>
        RecapStates.Select(s => { var r = SampleRecap(s); return (s, r.Card, (Action)(() => Motion.Sequence(r.Card, r.Intro))); }).ToList();

    /// <summary>
    /// A sample recap (nothing is saved): "run", a farming run 18% over your average there; "new-best", the same run as a new
    /// best; "no-loot", a short run with Kinah and no drops, with too little history for a verdict; "long-name", an expedition
    /// with a 41-character name and a dozen kinds of drops, 7% under your average there.
    /// </summary>
    RecapCard SampleRecap(string state)
    {
        var drops = new List<ShareDrop>
        {
            new("Thick Balaur Horn", "Legend", 2, 186_000), new("Fine Orichalcum Ore", "Rare", 14, 61_000), new("Spiritstone", "Common", 37, 9_000),
            new("Sapphire Gemstone", "Rare", 4, 6_000), new("Fresh Fruit", "Common", 19, 4_000), new("Orichalcum Ore", "Common", 38, 3_000),
        };
        const long kinah = 686_000;
        const double secs = 47 * 60 + 12;
        long made = kinah + drops.Sum(d => d.Worth);
        ShareFacts Farm(double s, long m, List<ShareDrop> d) => new(SharePicture.Run, DateTime.Now, ShareServer(), _demoZone ?? "Urugugu Canyon", "Farming run", s, 312, "kill", m, d);
        switch (state)
        {
            case "new-best":
                return BuildRecap("Run done", Farm(secs, made, drops), kinah, new($"{Fk(made / (secs / 3600))} Kinah/h · it was {Fk(1_050_000)}", true, null), expedition: false);
            case "no-loot":
                return BuildRecap("Run done", Farm(12 * 60 + 40, 148_000, new()), 148_000, new("Your best here: 1.4M Kinah/h", false, null), expedition: false);
            case "long-name":
            {
                var many = drops.Concat(new ShareDrop[]
                {
                    new("Ruby Gemstone", "Rare", 6, 2_400), new("Diamond Gemstone", "Rare", 4, 1_900), new("Odyle", "Common", 12, 1_200),
                    new("Kukuru", "Common", 23, 900), new("Conide", "Common", 11, 700), new("Fine Odyle", "Common", 3, 0),
                }).ToList();
                const string place = "The Forgotten Sanctuary of the Silvermoon";
                const long paid = 412_000;
                var f = new ShareFacts(SharePicture.Run, DateTime.Now, ShareServer(), place, "Expedition", 23 * 60 + 5, 48, "kill", paid + many.Sum(d => d.Worth), many);
                return BuildRecap($"Expedition done · {place}", f, paid, new("", false, -0.07), expedition: true);
            }
            default:
                return BuildRecap("Run done", Farm(secs, made, drops), kinah, new("Your best here: 1.4M Kinah/h", false, 0.18), expedition: false);
        }
    }
}
