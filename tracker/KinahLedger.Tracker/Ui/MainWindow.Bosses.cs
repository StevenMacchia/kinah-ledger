using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using KinahLedger.Session;

namespace KinahLedger.Ui;

/// <summary>
/// Timers (0.10.83): the game's timed events (the event the player is in, then when each scheduled one starts next,
/// Session/EventTimers.cs) above the world boss timers (0.10.82), one card in a window of its own (Ui/BossWindow.cs), opened
/// from the Events page's ⋯ menu.
///
/// World boss timers: the field bosses the game's world map lists. Each row is a boss and its place, with the time to its spawn
/// counting down, "Up now" while the map is open and says it is, or "Due" once its time has passed with no newer reading.
/// The timers come from the game's traffic (Session/BossTimers.cs), so they need packet reading on; they are as fresh as
/// the last time the map was opened, and the card says how old that is.
/// </summary>
public partial class MainWindow
{
    BossWindow? _bossWin;
    DispatcherTimer? _bossTick;

    /// <summary>The timers the card shows: the packet reader's, or a render's own.</summary>
    BossTimers Bosses => _bossesForShot ?? _watcher.Packets.Bosses;
    BossTimers? _bossesForShot;
    /// <summary>The event timers the card shows: the packet reader's, or a render's own.</summary>
    EventTimers EventClock => _eventsForShot ?? _watcher.Packets.Timers;
    EventTimers? _eventsForShot;
    /// <summary>The region whose schedule the events follow (Settings.Region; a render may set its own).</summary>
    string EventRegion => _eventRegionForShot ?? _settings.Region;
    string? _eventRegionForShot;

    const double BossCardWidth = 400;
    static readonly TimeSpan BossSoon = TimeSpan.FromMinutes(10);

    internal MenuEntry BossMenuEntry()
    {
        var now = DateTime.UtcNow;
        string sub = EventClock.Current(now) is { LeftAt: null } live ? $"{live.Label}: {EventTimers.StageText(live).ToLowerInvariant()}"
            : EventClock.Rows(now, EventRegion).FirstOrDefault() is { } next ? $"{next.Event.Name} {(next.OnNow ? "on now" : "in " + BossTimers.Span(next.Left))}" : "";
        return new MenuEntry("Timers", OpenBossWindow,
            Sub: sub.Length > 0 ? $"{sub} · world bosses" : "Events and world bosses",
            Tip: "When the game's events start next in your region's time (Shugo Festival, Dimensional Invasion, Spacetime Rift, Artifact Siege) and the event you're in; then the field bosses' spawn timers from the world map. The event you're in and the bosses need Options › Game reading.");
    }

    /// <summary>Opens the timers' window, or brings it forward; it redraws every second while open.</summary>
    internal void OpenBossWindow()
    {
        if (_bossWin == null)
        {
            _bossWin = new BossWindow(this);
            _bossWin.Closed += (_, _) => { _bossWin = null; _bossTick?.Stop(); };
        }
        _bossWin.ShowCard(BuildBossCard(DateTime.UtcNow));
        _bossTick ??= new DispatcherTimer(TimeSpan.FromSeconds(1), DispatcherPriority.Background, (_, _) =>
        {
            if (_bossWin is { IsVisible: true } w) w.ShowCard(BuildBossCard(DateTime.UtcNow));
        }, Dispatcher);
        _bossTick.Start();
    }

    /// <summary>
    /// The card at <paramref name="now"/> (UTC): the heading and the clock it counts on; the event the player is in; the
    /// scheduled events, soonest first; then the world bosses and how fresh their reading is.
    /// </summary>
    internal Border BuildBossCard(DateTime now)
    {
        var (card, body) = CkShell(BossCardWidth);
        var timers = EventClock;

        // the band: what this is, and the region's time the events are in (Session/EventTimers.ClockOf). Nothing outside
        // Options names how the times are read (Steven, 2026-11-14)
        var band = new StackPanel { Margin = new Thickness(20, 16, 56, 4) };
        band.Children.Add(CkText("Timers", CkInk, 18, FontWeights.Normal, display: true));
        var zone = EventTimers.ClockOf(EventRegion);
        var zt = CkText(zone.Checked ? $"{zone.Name} times: {zone.Note}" : $"Event times: {zone.Note}", zone.Checked ? CkMuted : CkWarn, 12.5);
        zt.Margin = new Thickness(0, 3, 0, 0); zt.TextWrapping = TextWrapping.Wrap; zt.TextTrimming = TextTrimming.None;
        band.Children.Add(zt);
        body.Children.Add(band);

        // the event the player is in, part by part, as its own server says
        if (timers.Current(now) is { } live)
        {
            body.Children.Add(CkSection("YOU'RE IN"));
            body.Children.Add(LiveEventRow(live, now));
        }
        body.Children.Add(CkSection("EVENTS"));
        foreach (var r in timers.Rows(now, EventRegion)) body.Children.Add(EventRow(r));

        // the world bosses (0.10.82)
        var rows = Bosses.Rows(now);
        string region = rows.Select(r => r.Boss.Region).FirstOrDefault(r => r.Length > 0) ?? "";
        bool live2 = Bosses.LastSeen is { } seen && (now - seen).TotalSeconds <= BossTimers.LiveSeconds;
        if (rows.Count == 0)
        {
            body.Children.Add(CkSection("WORLD BOSSES"));
            body.Children.Add(CkNote("Open the world map in the game once. Its field boss timers show here, and keep counting down after you close it."));
            if (!_watcher.Packets.Enabled) body.Children.Add(CkNote("They need Options › Game reading, which is off."));
            body.Children.Add(new Border { Height = 16 });
            return card;
        }
        var up = rows.Where(r => r.Boss.State == BossTimers.Kind.Up || r.Due).ToList();
        var waiting = rows.Where(r => r.Boss.State == BossTimers.Kind.Waiting && !r.Due).ToList();
        var other = rows.Where(r => r.Boss.State == BossTimers.Kind.NoTimer).ToList();
        var list = new StackPanel();
        bool first = true;
        void Head(string text)
        {
            list.Children.Add(CkSection(text));
            if (!first) return;
            first = false;
            // under the first boss heading: where, and how fresh the reading is
            var f = CkText((region.Length > 0 ? region + " · " : "") + Bosses.Freshness(now), live2 ? CkGood : CkMuted, 12.5);
            f.Margin = new Thickness(20, 0, 20, 4); f.TextWrapping = TextWrapping.Wrap; f.TextTrimming = TextTrimming.None;
            list.Children.Add(f);
        }
        if (up.Count > 0)
        {
            Head(up.Any(r => r.UpNow) ? "WORLD BOSSES UP NOW" : "WORLD BOSSES UP OR DUE");
            foreach (var r in up) list.Children.Add(BossRow(r));
        }
        if (waiting.Count > 0)
        {
            Head("NEXT WORLD BOSSES");
            foreach (var r in waiting) list.Children.Add(BossRow(r));
        }
        if (other.Count > 0)
        {
            Head("WORLD BOSSES WITH NO TIMER");
            foreach (var r in other) list.Children.Add(BossRow(r));
        }
        body.Children.Add(list);
        body.Children.Add(CkNote(live2 ? "The map is open: the bosses follow it live." : "Bosses: counted down since the map was last open. A kill nobody saw on the map doesn't show until it is opened again."));
        body.Children.Add(new Border { Height = 14 });
        return card;
    }

    /// <summary>A scheduled event: its name and how often on the left, the countdown to its start (or "On now") on the right.</summary>
    FrameworkElement EventRow(EventTimers.Row r)
    {
        bool soon = !r.OnNow && r.Left <= BossSoon;
        string sub = r.Event.Every + (r.Event.Where.Length > 0 ? " · " + r.Event.Where : "");
        string big = r.OnNow ? "On now" : r.When;
        string small = r.OnNow ? r.When : r.StartPc.ToLocalTime().Date != DateTime.Now.Date ? $"{r.StartPc.ToLocalTime():ddd HH:mm}" : $"at {r.StartPc.ToLocalTime():HH:mm}";
        return TimerRow(r.Event.Name, sub, big, small, r.OnNow ? CkGood : soon ? CkGold : CkFaint, r.OnNow ? CkGood : soon ? CkGold : CkInk, r.OnNow || soon);
    }

    /// <summary>The event the player is in: its game and family on the left, where it stands on the right.</summary>
    FrameworkElement LiveEventRow(EventTimers.Live l, DateTime now)
    {
        string stage = EventTimers.StageText(l);
        bool on = l.LeftAt == null && l.Stage is Packets.AreaEvent.Stage.Preparing or Packets.AreaEvent.Stage.UnderWay;
        var tone = l.LeftAt != null ? CkFaint : l.Stage == Packets.AreaEvent.Stage.UnderWay ? CkGood : l.Stage == Packets.AreaEvent.Stage.Preparing ? CkGold : CkMuted;
        string sub = l.Name.Length > 0 && l.Family.Length > 0 ? l.Family : "Event";
        string small = l.LeftAt is { } left ? $"{BossTimers.Ago(now - left)}" : l.OverAt is { } over ? $"{BossTimers.Ago(now - over)}" : l.StartedPc is { } st ? $"began {BossTimers.Ago(now - st)}" : "";
        return TimerRow(l.Label, sub, stage, small, tone, tone == CkFaint ? CkMuted : tone, on);
    }

    /// <summary>A timer's row in the bosses' look: a dot, the name and a line under it, the big figure and a line under it.</summary>
    FrameworkElement TimerRow(string name, string sub, string big, string small, Color dotTone, Color bigTone, bool bright)
    {
        var g = new Grid { Margin = new Thickness(20, 4, 20, 4) };
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        g.ColumnDefinitions.Add(new ColumnDefinition());
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        g.Children.Add(new Border { Width = 8, Height = 8, CornerRadius = new CornerRadius(4), Background = Ck(dotTone, bright ? (byte)255 : (byte)170), Margin = new Thickness(0, 0, 12, 0), VerticalAlignment = VerticalAlignment.Center });
        var left = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        left.Children.Add(CkText(name, CkInk, 13.5));
        if (sub.Length > 0) left.Children.Add(CkText(sub, CkFaint, 12));
        Grid.SetColumn(left, 1); g.Children.Add(left);
        var right = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 0) };
        var bt = CkText(big, bigTone, 14, FontWeights.Medium, display: true);
        bt.HorizontalAlignment = HorizontalAlignment.Right;
        System.Windows.Documents.Typography.SetNumeralAlignment(bt, FontNumeralAlignment.Tabular);
        right.Children.Add(bt);
        if (small.Length > 0) { var st = CkText(small, CkFaint, 12); st.HorizontalAlignment = HorizontalAlignment.Right; right.Children.Add(st); }
        Grid.SetColumn(right, 2); g.Children.Add(right);
        return g;
    }

    /// <summary>One boss: its name and place on the left, the countdown or its state on the right.</summary>
    FrameworkElement BossRow(BossTimers.Row r)
    {
        var g = new Grid { Margin = new Thickness(20, 4, 20, 4) };
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        g.ColumnDefinitions.Add(new ColumnDefinition());
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        bool soon = r.Boss.State == BossTimers.Kind.Waiting && !r.Due && r.Left <= BossSoon;
        var tone = r.UpNow ? CkGood : r.Due || r.Boss.State == BossTimers.Kind.Up ? CkWarn : soon ? CkGold : CkFaint;
        var dot = new Border { Width = 8, Height = 8, CornerRadius = new CornerRadius(4), Background = Ck(tone, r.UpNow || soon ? (byte)255 : (byte)170), Margin = new Thickness(0, 0, 12, 0), VerticalAlignment = VerticalAlignment.Center };
        g.Children.Add(dot);
        var left = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        left.Children.Add(CkText(r.Boss.Name, r.Stale && !r.Due && r.Boss.State != BossTimers.Kind.Waiting ? CkMuted : CkInk, 13.5));
        if (r.Boss.Place.Length > 0) left.Children.Add(CkText(r.Boss.Place, CkFaint, 12));
        Grid.SetColumn(left, 1); g.Children.Add(left);
        var right = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 0) };
        string big = r.UpNow ? "Up now" : r.Due ? "Due" : r.Boss.State == BossTimers.Kind.Up ? "Was up" : r.Boss.State == BossTimers.Kind.Waiting ? r.When : "No timer";
        var bt = CkText(big, r.UpNow ? CkGood : r.Due || r.Boss.State == BossTimers.Kind.Up ? CkWarn : soon ? CkGold : CkInk, 14, FontWeights.Medium, display: true);
        bt.HorizontalAlignment = HorizontalAlignment.Right;
        System.Windows.Documents.Typography.SetNumeralAlignment(bt, FontNumeralAlignment.Tabular);   // the seconds tick without the line shifting
        right.Children.Add(bt);
        string small = r.UpNow ? r.When : r.Due ? $"spawn {r.When}" : r.Boss.State == BossTimers.Kind.Up ? r.When : r.Boss.State == BossTimers.Kind.Waiting ? $"at {r.Boss.SpawnPc.ToLocalTime():HH:mm}" : "";
        if (small.Length > 0) { var st = CkText(small, CkFaint, 12); st.HorizontalAlignment = HorizontalAlignment.Right; right.Children.Add(st); }
        Grid.SetColumn(right, 2); g.Children.Add(right);
        return g;
    }

    /// <summary>
    /// ui-shot --timers (or --bosses): the card in its window over made-up readings, as the player would see it: live (in
    /// Defend Shugo Merchants, the map open), getting-ready (an event's lobby), stale (the map an hour ago, no event) and empty
    /// (nothing read, this PC's clock). Written as timers-&lt;state&gt;[-&lt;skin&gt;].png. The PC's own timers are never read or saved.
    /// </summary>
    internal void ShotBosses(string dir, string? skin)
    {
        var now = DateTime.UtcNow;
        string sfx = skin != null ? "-" + skin : "";
        var lag = TimeSpan.FromSeconds(14.6);   // the game's clock behind this PC's, as on 2026-11-13
        foreach (var (state, age) in new[] { ("live", TimeSpan.FromSeconds(4)), ("getting-ready", TimeSpan.FromSeconds(4)), ("stale", TimeSpan.FromMinutes(64)), ("empty", TimeSpan.Zero) })
        {
            var t = new BossTimers();
            if (state != "empty") t.SetForTest(SampleBosses(now - age));
            _bossesForShot = t;
            var ev = new EventTimers();
            ev.SetForTest(state == "empty" ? TimeSpan.Zero : lag, state != "empty", state switch
            {
                "live" => new EventTimers.Live("Shugo Festival", "Defend Shugo Merchants", Packets.AreaEvent.Stage.UnderWay, now.AddSeconds(-70), now.AddSeconds(-1), null, null),
                "getting-ready" => new EventTimers.Live("Shugo Festival", "", Packets.AreaEvent.Stage.Preparing, now.AddSeconds(-25), now.AddSeconds(-1), null, null),
                _ => null,
            });
            _eventsForShot = ev;
            _eventRegionForShot = state == "empty" ? "eu" : "na_e";   // the empty state: a region whose times aren't checked yet
            var card = BuildBossCard(now);
            var win = new BossWindow(this);
            win.ShowCard(card, offscreen: true);
            win.UpdateLayout();
            Dispatcher.Invoke(() => { }, DispatcherPriority.Background);
            if (win.Content is FrameworkElement root)
            {
                var bmp = new System.Windows.Media.Imaging.RenderTargetBitmap((int)Math.Ceiling(root.ActualWidth), (int)Math.Ceiling(root.ActualHeight), 96, 96, PixelFormats.Pbgra32);
                bmp.Render(root);
                var enc = new System.Windows.Media.Imaging.PngBitmapEncoder(); enc.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bmp));
                using var f = File.Create(Path.Combine(dir, $"timers-{state}{sfx}.png")); enc.Save(f);
            }
            win.Close();
        }
        _bossesForShot = null; _eventsForShot = null; _eventRegionForShot = null;
    }

    /// <summary>A reading like the one of 2026-11-13 18:58 (Verteron, two bosses up), taken at <paramref name="seen"/>.</summary>
    internal static List<BossTimers.Boss> SampleBosses(DateTime seen)
    {
        var list = new List<BossTimers.Boss>();
        void B(string name, string place, double minutes, bool up = false) =>
            list.Add(new BossTimers.Boss(name, name, place, "Verteron", up ? BossTimers.Kind.Up : BossTimers.Kind.Waiting, seen.AddMinutes(minutes), seen, false));
        B("Blooming Korin", "Ellun River Midstream", -11, up: true);
        B("Bloodfang Pnyn", "Northern Tolbas Forest", -14, up: true);
        B("Chaser Taulo", "Forest of Exiles", 20.05);
        B("Neikel of the East", "Eastern Cantas Valley", 23.3);
        B("Centurion Demiros", "Destroyed Temple Site", 23.47);
        B("Scholar Aulla", "Aullaeu Village", 23.5);
        B("Rotten Kutar", "Ellun River Marsh", 26.1);
        B("Kernon of the West", "Western Cantas Valley", 28.7);
        B("Black Tentacle Lawa", "Altamia Canyon", 32.2);
        B("Bodyguard Teegant", "Destroyed Abyss Gate", 33.6);
        B("Researcher Setram", "Nahid Brigade Fortress", 34.9);
        B("Forest Warrior Aullamu", "Aullaeu Village", 40.0);
        B("Sentinel K'nash", "Drana Processing Area", 41.2);
        B("Harvest Manager Moshav", "Drana Processing Area", 146.1);
        B("Heretic Layla", "Rock Tomb Hill", 160.0);
        B("Furious Saursus", "Night Dew Cave", 176.2);
        B("Eternal Gartua", "Eternal Isle", 306.5);
        B("Silent Dartan", "Southern Altamia Highland", 312.7);
        B("Phantasm Kasia", "Garden of the Illusion God", 349.3);
        return list;
    }
}
