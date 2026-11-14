using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using KinahLedger.Session;

namespace KinahLedger.Ui;

/// <summary>
/// World boss timers (0.10.75): the field bosses the game's world map lists, as a card in a window of its own
/// (Ui/BossWindow.cs), opened from the Events page's ⋯ menu. Each row is a boss and its place, with the time to its spawn
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

    const double BossCardWidth = 400;
    static readonly TimeSpan BossSoon = TimeSpan.FromMinutes(10);

    internal MenuEntry BossMenuEntry()
    {
        int n = Bosses.Count;
        return new MenuEntry("World boss timers", OpenBossWindow,
            Sub: n == 0 ? "Open the world map in the game once to fill them in" : Bosses.Freshness(DateTime.UtcNow),
            Tip: "The field bosses' spawn timers, read from the game's world map while it is open, then counted down here. They need packet reading (Options › Game reading).");
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

    /// <summary>The card at <paramref name="now"/> (UTC): the heading and how fresh the reading is, then a row a boss.</summary>
    internal Border BuildBossCard(DateTime now)
    {
        var (card, body) = CkShell(BossCardWidth);
        var rows = Bosses.Rows(now);
        string region = rows.Select(r => r.Boss.Region).FirstOrDefault(r => r.Length > 0) ?? "";

        // the band: what this is, and how fresh
        var band = new StackPanel { Margin = new Thickness(20, 16, 56, 4) };
        band.Children.Add(CkText("World bosses" + (region.Length > 0 ? $" · {region}" : ""), CkInk, 18, FontWeights.Normal, display: true));
        bool live = Bosses.LastSeen is { } seen && (now - seen).TotalSeconds <= BossTimers.LiveSeconds;
        var fresh = CkText(Bosses.Freshness(now), live ? CkGood : CkMuted, 12.5);
        fresh.Margin = new Thickness(0, 3, 0, 0); fresh.TextWrapping = TextWrapping.Wrap; fresh.TextTrimming = TextTrimming.None;
        band.Children.Add(fresh);
        body.Children.Add(band);

        if (rows.Count == 0)
        {
            body.Children.Add(CkSection("NO TIMERS YET"));
            body.Children.Add(CkNote("Open the world map in the game. While it is open the game sends every field boss's timer, and they count down here after you close it."));
            body.Children.Add(CkNote(_watcher.Packets.Enabled ? "Packet reading: " + _watcher.Packets.State : "Packet reading is off (Options › Game reading), so the timers can't be read."));
            body.Children.Add(new Border { Height = 16 });
            return card;
        }

        var up = rows.Where(r => r.Boss.State == BossTimers.Kind.Up || r.Due).ToList();
        var waiting = rows.Where(r => r.Boss.State == BossTimers.Kind.Waiting && !r.Due).ToList();
        var list = new StackPanel();
        if (up.Count > 0)
        {
            list.Children.Add(CkSection(up.Any(r => r.UpNow) ? "UP NOW" : "UP OR DUE"));
            foreach (var r in up) list.Children.Add(BossRow(r));
        }
        if (waiting.Count > 0)
        {
            list.Children.Add(CkSection("NEXT SPAWNS"));
            foreach (var r in waiting) list.Children.Add(BossRow(r));
        }
        var other = rows.Where(r => r.Boss.State == BossTimers.Kind.NoTimer).ToList();
        if (other.Count > 0)
        {
            list.Children.Add(CkSection("NO TIMER"));
            foreach (var r in other) list.Children.Add(BossRow(r));
        }
        body.Children.Add(list);
        body.Children.Add(CkNote(live ? "The map is open: this follows it live." : "Counted down on this PC since the map was last open. A kill nobody saw on the map doesn't show until it is opened again."));
        body.Children.Add(new Border { Height = 14 });
        return card;
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
    /// ui-shot --bosses: the card in its window over a made-up reading (live, an hour old, and none), as the player would see it.
    /// Written as bosses-&lt;state&gt;[-&lt;skin&gt;].png. The PC's own timers are never read or saved.
    /// </summary>
    internal void ShotBosses(string dir, string? skin)
    {
        var now = DateTime.UtcNow;
        string sfx = skin != null ? "-" + skin : "";
        foreach (var (state, age) in new[] { ("live", TimeSpan.FromSeconds(4)), ("stale", TimeSpan.FromMinutes(64)), ("empty", TimeSpan.Zero) })
        {
            var t = new BossTimers();
            if (state != "empty") t.SetForTest(SampleBosses(now - age));
            _bossesForShot = t;
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
                using var f = File.Create(Path.Combine(dir, $"bosses-{state}{sfx}.png")); enc.Save(f);
            }
            win.Close();
        }
        _bossesForShot = null;
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
        B("A Verteron boss", "", 26.1);
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
