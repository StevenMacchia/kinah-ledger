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
    string EventRegion => _eventRegionForShot ?? (_watcher.RegionKey is { Length: > 0 } k ? k : _settings.Region);
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
            Tip: "When the events start next (Shugo Festival, Dimensional Invasion, Spacetime Rift, Artifact Siege) and the world bosses spawn.");
    }

    /// <summary>Opens the timers' window, or brings it forward; it redraws every second while open.</summary>
    internal void OpenBossWindow()
    {
        if (_bossWin == null)
        {
            _bossWin = new BossWindow(this, _timerPrefs);
            _bossWin.Closed += (_, _) => { _bossWin = null; _bossTick?.Stop(); };
            _bossWin.Resized += () => { if (_bossWin is { IsVisible: true } rw) rw.ShowCard(BuildBossCard(DateTime.UtcNow)); };
        }
        else if (_bossWin.IsVisible) _bossWin.Activate();
        _bossWin.ShowCard(BuildBossCard(DateTime.UtcNow));
        _bossTick ??= new DispatcherTimer(TimeSpan.FromSeconds(1), DispatcherPriority.Background, (_, _) =>
        {
            if (_bossWin is { IsVisible: true } w) w.ShowCard(BuildBossCard(DateTime.UtcNow));
        }, Dispatcher);
        _bossTick.Start();
    }

    /// <summary>
    /// The card at <paramref name="now"/> (UTC): the heading; the event the player is in; the scheduled events; then the world
    /// bosses map by map: the maps the game listed (Verteron), then the Abyss's on their schedule (0.10.85). Minimal by default
    /// (Steven, 2026-11-14): a name and its countdown a row; Options › Timers › Details adds each row's second line.
    /// It never names where a figure comes from (only Options does).
    /// </summary>
    internal Border BuildBossCard(DateTime now, double? width = null)
    {
        var (card, body) = CkShell(width ?? _bossWin?.CardWidth ?? BossCardWidth);
        var timers = EventClock;
        bool details = _timerPrefs.Details;

        var band = new StackPanel { Margin = new Thickness(20, 16, 56, 4) };
        band.Children.Add(CkText("Timers", CkInk, 18, FontWeights.Normal, display: true));
        // the region's time the events are in (Session/EventTimers.ClockOf); orange while that region isn't checked yet
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

        // the world bosses the game's map listed (0.10.82), a section a map
        var rows = Bosses.Rows(now);
        bool live2 = Bosses.LastSeen is { } seen && (now - seen).TotalSeconds <= BossTimers.LiveSeconds;
        if (rows.Count == 0)
        {
            body.Children.Add(CkSection("WORLD BOSSES"));
            body.Children.Add(CkNote("Open the world map in the game (Exploration › Field Boss) once: the bosses' timers fill in here and count down after you close it."));
        }
        foreach (var map in rows.GroupBy(r => r.Boss.Region.Length > 0 ? r.Boss.Region : "World"))
        {
            body.Children.Add(CkSection(map.Key.ToUpperInvariant() + " · WORLD BOSSES"));
            var f = CkText(Bosses.Freshness(now), live2 ? CkGood : CkMuted, 12.5);
            f.Margin = new Thickness(20, 0, 20, 4); f.TextWrapping = TextWrapping.Wrap; f.TextTrimming = TextTrimming.None;
            body.Children.Add(f);
            var order = map.OrderBy(r => r.UpNow ? 0 : r.Due ? 1 : r.Boss.State == BossTimers.Kind.Waiting ? 2 : r.Boss.State == BossTimers.Kind.Up ? 3 : 4)
                .ThenBy(r => r.Boss.State == BossTimers.Kind.Waiting ? r.Left : TimeSpan.Zero);
            foreach (var r in order) body.Children.Add(BossRow(r));
        }

        // the Abyss's bosses, on their schedule (the game's map draws them from it too)
        var (_, zoneChecked) = AbyssBosses.ZoneFor(EventRegion);
        foreach (var map in AbyssBosses.Rows(now, timers.Offset, EventRegion).GroupBy(r => r.Boss.Map))
        {
            body.Children.Add(CkSection(map.Key.ToUpperInvariant() + " · ABYSS"));
            bool sure = zoneChecked && map.All(r => r.Boss.Checked);
            if (details)
            {
                var f = CkText(sure ? "The Abyss's schedule" : "The Abyss's schedule (not checked on the map yet)", CkMuted, 12.5);
                f.Margin = new Thickness(20, 0, 20, 4); f.TextWrapping = TextWrapping.Wrap; f.TextTrimming = TextTrimming.None;
                body.Children.Add(f);
            }
            foreach (var r in map) body.Children.Add(AbyssRow(r));
        }
        if (details && rows.Count > 0) body.Children.Add(CkNote("A kill nobody saw on the map shows once the map is opened again."));
        body.Children.Add(new Border { Height = 14 });
        return card;
    }

    /// <summary>An Abyss boss: its name (×3 for Nahma's three spots) and place, the countdown or "Spawned".</summary>
    FrameworkElement AbyssRow(AbyssBosses.Row r)
    {
        bool soon = !r.Spawned && r.Left <= BossSoon;
        string name = r.Boss.Spots > 1 ? $"{r.Boss.Name} ×{r.Boss.Spots}" : r.Boss.Name;
        string small = r.Spawned ? $"spawned {r.When}" : r.Left.TotalHours >= 20 ? $"{r.SpawnPc.ToLocalTime():ddd HH:mm}" : $"at {r.SpawnPc.ToLocalTime():HH:mm}";
        return TimerRow(name, $"{r.Boss.Place} · {r.Boss.Every}", r.Spawned ? "Spawned" : r.When, small,
            r.Spawned ? CkWarn : soon ? CkGold : CkFaint, r.Spawned ? CkWarn : soon ? CkGold : CkInk, r.Spawned || soon);
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
        string sub = l.Name.Length > 0 && l.Family.Length > 0 ? l.Family : "";
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
        if (!_timerPrefs.Details) sub = small = "";   // minimal: the name and the figure only
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
        if (r.Boss.Place.Length > 0 && _timerPrefs.Details) left.Children.Add(CkText(r.Boss.Place, CkFaint, 12));
        Grid.SetColumn(left, 1); g.Children.Add(left);
        var right = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 0) };
        string big = r.UpNow ? "Up now" : r.Due ? "Due" : r.Boss.State == BossTimers.Kind.Up ? "Was up" : r.Boss.State == BossTimers.Kind.Waiting ? r.When : "No timer";
        var bt = CkText(big, r.UpNow ? CkGood : r.Due || r.Boss.State == BossTimers.Kind.Up ? CkWarn : soon ? CkGold : CkInk, 14, FontWeights.Medium, display: true);
        bt.HorizontalAlignment = HorizontalAlignment.Right;
        System.Windows.Documents.Typography.SetNumeralAlignment(bt, FontNumeralAlignment.Tabular);   // the seconds tick without the line shifting
        right.Children.Add(bt);
        string small = r.UpNow ? r.When : r.Due ? $"spawn {r.When}" : r.Boss.State == BossTimers.Kind.Up ? r.When : r.Boss.State == BossTimers.Kind.Waiting ? $"at {r.Boss.SpawnPc.ToLocalTime():HH:mm}" : "";
        if (small.Length > 0 && _timerPrefs.Details) { var st = CkText(small, CkFaint, 12); st.HorizontalAlignment = HorizontalAlignment.Right; right.Children.Add(st); }
        Grid.SetColumn(right, 2); g.Children.Add(right);
        return g;
    }

    /// <summary>
    /// ui-shot --timers (or --bosses): the timers window over made-up readings, as the player would see it, at its default
    /// size: live (in Defend Shugo Merchants, the map open), getting-ready (an event's lobby), stale (the map an hour ago) and
    /// empty (nothing read); then live with every option on (timers-live-details.png). Then the overlay: at rest (two timers
    /// on their way, one in its last 5 minutes, gold), close (the next ones inside 5 minutes) and with its menu open. Written
    /// as timers-&lt;state&gt;[-&lt;skin&gt;].png. The PC's own timers and settings are never read or saved.
    /// </summary>
    internal void ShotBosses(string dir, string? skin)
    {
        var now = DateTime.UtcNow;
        string sfx = skin != null ? "-" + skin : "";
        var lag = TimeSpan.FromSeconds(14.6);   // as on 2026-11-13
        var keepPrefs = _timerPrefs;
        void Save(FrameworkElement root, string file, Color? ground = null)
        {
            FrameworkElement what = root;
            if (ground is { } g)
            {
                var b0 = new System.Windows.Media.Imaging.RenderTargetBitmap((int)Math.Ceiling(root.ActualWidth), (int)Math.Ceiling(root.ActualHeight), 96, 96, PixelFormats.Pbgra32);
                b0.Render(root);
                var page = new Border { Background = new SolidColorBrush(g), Padding = new Thickness(18), Child = new System.Windows.Controls.Image { Source = b0, Width = root.ActualWidth, Height = root.ActualHeight } };
                page.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity)); page.Arrange(new Rect(page.DesiredSize)); page.UpdateLayout();
                what = page;
            }
            var bmp = new System.Windows.Media.Imaging.RenderTargetBitmap((int)Math.Ceiling(what.ActualWidth), (int)Math.Ceiling(what.ActualHeight), 96, 96, PixelFormats.Pbgra32);
            bmp.Render(what);
            var enc = new System.Windows.Media.Imaging.PngBitmapEncoder(); enc.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bmp));
            using var f = File.Create(Path.Combine(dir, file)); enc.Save(f);
        }
        foreach (var (state, age, details) in new[] { ("live", TimeSpan.FromSeconds(4), false), ("getting-ready", TimeSpan.FromSeconds(4), false), ("stale", TimeSpan.FromMinutes(64), false), ("empty", TimeSpan.Zero, false), ("live-details", TimeSpan.FromSeconds(4), true) })
        {
            _timerPrefs = TimerPrefs.Defaults();
            _timerPrefs.Details = details;
            var t = new BossTimers();
            if (state != "empty") t.SetForTest(SampleBosses(now - age));
            _bossesForShot = t;
            var ev = new EventTimers();
            ev.SetForTest(state == "empty" ? TimeSpan.Zero : lag, state != "empty", state switch
            {
                "live" or "live-details" => new EventTimers.Live("Shugo Festival", "Defend Shugo Merchants", Packets.AreaEvent.Stage.UnderWay, now.AddSeconds(-70), now.AddSeconds(-1), null, null),
                "getting-ready" => new EventTimers.Live("Shugo Festival", "", Packets.AreaEvent.Stage.Preparing, now.AddSeconds(-25), now.AddSeconds(-1), null, null),
                _ => null,
            });
            _eventsForShot = ev;
            _eventRegionForShot = state == "empty" ? "eu" : "na_e";   // the empty state: a region whose times aren't checked yet
            var win = new BossWindow(this, _timerPrefs);
            win.ShowCard(BuildBossCard(now, win.CardWidth), offscreen: true);
            win.UpdateLayout();
            Dispatcher.Invoke(() => { }, DispatcherPriority.Background);
            if (win.Content is FrameworkElement root) Save(root, $"timers-{state}{sfx}.png");
            win.Close();
        }
        // the overlay over the game (a game-like ground under it), watching the defaults
        {
            _timerPrefs = TimerPrefs.Defaults();
            var t = new BossTimers(); t.SetForTest(SampleBosses(now - TimeSpan.FromSeconds(4)));
            _bossesForShot = t;
            var ev = new EventTimers(); ev.SetForTest(lag, true, null);
            _eventsForShot = ev; _eventRegionForShot = "na_e";
            var ground = Color.FromRgb(0x5A, 0x6B, 0x4F);
            foreach (var (state, window, menu, watchAll) in new[] { ("overlay-rest", 15, false, true), ("overlay-close", 5, false, true), ("overlay-menu", 15, true, true) })
            {
                _timerPrefs.WindowMinutes = window;
                if (watchAll) foreach (var i in TimerItems(now)) _timerPrefs.SetWatched(i.Key, true);
                var at = now.AddMinutes(state == "overlay-close" ? 17 : 8);   // rest: Chaser Taulo 12 min away and others; close: Taulo 3 min
                var items = TimerBoard.ForOverlay(TimerItems(at), _timerPrefs, at);
                var o = new TimersOverlay(_timerPrefs) { Left = -12000, Top = -12000 };
                o.Show(items, at);
                if (menu) o.ToggleMenu(true);
                if (!o.IsVisible) o.Show();
                o.UpdateLayout();
                Dispatcher.Invoke(() => { }, DispatcherPriority.Background);
                if (o.Content is FrameworkElement root) Save(root, $"timers-{state}{sfx}.png", ground);
                o.Close();
            }
        }
        _bossesForShot = null; _eventsForShot = null; _eventRegionForShot = null; _timerPrefs = keepPrefs;
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
