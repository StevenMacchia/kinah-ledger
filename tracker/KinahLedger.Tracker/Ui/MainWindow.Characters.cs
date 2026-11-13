using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;
using KinahLedger.Session;
using Path = System.Windows.Shapes.Path;

namespace KinahLedger.Ui;

/// <summary>
/// The character in the header and its switcher (Session/Characters.cs): which character is counted for, read off the class
/// icon between the health and mana bars, switched by itself when another one is in the world. The header's chip shows its
/// class icon and name (UX pass 2026-11-13); the switcher lists every character with its week and best, renames one, counts
/// for another by hand (two characters of one class can't be told apart by the icon) or splits one off as a new character.
/// </summary>
public partial class MainWindow
{
    /// <summary>The demo's character for renders (ui-shot); null otherwise.</summary>
    string? _demoCharacter;

    /// <summary>The character counted for now: the demo's first sample in a render, else the book's; null before the first one was seen.</summary>
    Character? CurrentCharacter => _demo ? DemoCharacters()[0] : Characters.Current;

    /// <summary>The character counted for now, as the header says it; empty before the first one was seen.</summary>
    string CharacterLabel => CurrentCharacter is { } c ? Characters.Label(c) : "";

    /// <summary>
    /// The header's second line (UX pass 2026-11-13): the character chip (its class icon, its name, a chevron) once a
    /// character has been seen, then the server and region, muted. Both open the switcher.
    /// </summary>
    void FillServerChip(string serverText)
    {
        ServerChip.Text = serverText;
        var cur = CurrentCharacter;
        if (cur != null)   // the whole line is hidden on the first-run screen (SetWelcome)
        {
            var who = Characters.Label(cur);
            CharName.Text = who;
            CharIconHost.Child = CharacterIcon.Tile(cur.Badges.FirstOrDefault(), who, 16);
            CharChip.ToolTip = $"Counting for {who}: the tracker reads the class icon between the health and mana bars and follows the character in the game.\nClick to see every character's week, rename one, or count for another.";
            AutomationProperties.SetName(CharChip, $"Characters: counting for {who}");
            CharChip.Visibility = Visibility.Visible;
        }
        else CharChip.Visibility = Visibility.Collapsed;
        ServerChip.ToolTip = _welcome ? "Pick your server below."
            : cur != null ? "Your server and region: prices and drops are kept per server. Click for your characters, or change the server."
            : "Your server and region. Click to change it. Your character shows here once you're in the world: it is read from the class icon between the health and mana bars.";
    }

    /// <summary>For options-test: the header's chip as drawn (shown, the name, whether the class icon is a picture).</summary>
    internal (bool Shown, string Name, bool Picture) CharChipForTest() =>
        (CharChip.Visibility == Visibility.Visible, CharName.Text, CharIconHost.Child is Border { Child: Image });

    /// <summary>Hooks the watcher's character events (from the window's start, when it watches the game).</summary>
    void InitCharacters()
    {
        // a name read off the nameplate: the header follows
        _watcher.CharacterNamed += () => Dispatcher.BeginInvoke(() => ServerRowState());
        // a 0.10.51 or 0.10.52 install, whose first character seen took all the history kept before: put it right, once
        if (!_demo && Characters.NeedsPlacing) Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.ApplicationIdle, () => _ = PlaceHistoryAsync());
        _watcher.CharacterSeen += v => Dispatcher.BeginInvoke(async () =>
        {
            try { await OnCharacterSeen(v); }
            catch (Exception ex) { CrashLog.Write("character switch", ex); }
            finally { _watcher.CharacterHandled(); }
        });
    }

    /// <summary>A steady class icon that isn't the character counted for now.</summary>
    async Task OnCharacterSeen(CharacterSpotter.Verdict v)
    {
        if (_demo) return;
        var server = _prefs.Server;
        if (Characters.Current == null)
        {
            // the first character seen after the update takes over everything kept so far, then the old runs are sorted
            var first = v.Character ?? Characters.Add(v.Print, server);
            SaveCharacterState();
            if (_watcher.Chat is { } ch) ch.Instances.Reload(() => Characters.Adopt(first)); else Characters.Adopt(first);
            BestRuns.Reset();
            ServerRowState(); Redraw();
            Say($"Counting for {Characters.Label(first)} from now on. Each character keeps its own numbers; click the name at the top to name it.", "Good");
            _statusHoldUntil = DateTime.UtcNow.AddSeconds(15);
            if (Characters.NeedsPlacing) _ = PlaceHistoryAsync();
            return;
        }
        var c = v.Character ?? Characters.Add(v.Print, server);
        await SwitchCharacter(c, v.Kind == CharacterSpotter.Kind.New ? "A new character" : Characters.Label(c));
    }

    bool _placing;

    /// <summary>
    /// The history kept before characters put with the characters it belongs to (Session/CharacterSort.cs Place), once:
    /// the pictures are read off the window's thread (every kept picture is opened), then the files change between a save
    /// and a load of the character counted for, so nothing it holds in memory is written back over them.
    /// </summary>
    async Task PlaceHistoryAsync()
    {
        if (_demo || _placing || Characters.Adopter is not { } adopter) return;
        _placing = true;
        try
        {
            var server = _prefs.Server;
            var shots = await Task.Run(() => CharacterSort.ReadShots(EventShotRecovery.ShotsDir));
            SaveCharacterState();
            var outcome = CharacterSort.Place(adopter, server, shots, act => { if (_watcher.Chat is { } ch) ch.Instances.Reload(act); else act(); });
            ReloadCharacterState();
            var said = new List<string>();
            if (outcome.Named.Count > 0) said.Add("named " + string.Join(", ", outcome.Named.Select(x => x.Name)) + " from their pictures");
            if (outcome.Moved > 0) said.Add("runs sorted by class icon: " + string.Join(", ", outcome.To.Select(x => $"{x.Runs} to {Characters.Label(x.Who)}")));
            if (outcome.HistoryMoved && outcome.Main != null) said.Add($"the days, bests, goals and quest points from before go to {Characters.Label(outcome.Main)}, your most played");
            if (said.Count > 0) { Say("Characters: " + string.Join("; ", said) + ".", "Faint"); _statusHoldUntil = DateTime.UtcNow.AddSeconds(20); }
        }
        catch (Exception ex) { CrashLog.Write("characters place", ex); Characters.MarkSorted(); }
        finally { _placing = false; }
    }

    /// <summary>The character counted for: its today, runs, goals, quest points and bests read again from its folder.</summary>
    void ReloadCharacterState()
    {
        _watcher.LoadDay();
        _watcher.Chat?.Instances.Reload();
        CloseWrapped();   // an open Week or Day wrapped is the other character's: it goes (Ui/MainWindow.Wrapped.cs)
        _goals = Goals.Load(); _questLog = Quests.Load(); _questLoaded = true;
        BestRuns.Reset();
        ServerRowState(); RedrawGoals(); Redraw();
    }

    /// <summary>The current character's numbers written to its folder: today, goals, quests.</summary>
    void SaveCharacterState()
    {
        _watcher.SaveDay();
        SaveGoals();
        SaveQuests();
    }

    bool _switchingCharacter;

    /// <summary>
    /// Counts for another character from here on: the run and the always-on count close as for any character switch
    /// (StartFresh), the old character's numbers are saved, and the new one's today, days, runs, goals and quest points load.
    /// </summary>
    async Task SwitchCharacter(Character c, string why)
    {
        if (_demo || _switchingCharacter || Characters.Current?.Id == c.Id) return;
        _switchingCharacter = true;
        ForgetRare();   // the other character's drop never leads this one's moment (Ui/MainWindow.RareMoment.cs)
        try
        {
            CrashLog.Note($"character: counting for {Characters.Label(c)} now (was {(Characters.Current is { } was ? Characters.Label(was) : "nobody")})");
            await StartFresh(why == "A new character" ? "Another character" : $"{why} is in the world");
            SaveCharacterState();
            Characters.SetCurrent(c);
            ReloadCharacterState();
            Say(why == "A new character"
                ? $"A character not seen before: counting for {Characters.Label(c)}, with numbers of its own. Click its name at the top to name it."
                : $"Counting for {Characters.Label(c)} now: its own today, days, runs, goals and quest points.", "Good");
            _statusHoldUntil = DateTime.UtcNow.AddSeconds(15);
        }
        finally { _switchingCharacter = false; }
    }

    /* ---------------- the switcher (UX pass 2026-11-13) ---------------- */

    /// <summary>
    /// The switcher under the chip (under the server line before any character was seen): every character of this server
    /// with its class icon, its name, when it was last played, its last seven days and its best hour, the one counted for
    /// marked. Seeing a character's week here never changes what is counted: the figures are read from its own folder
    /// (Session/CharacterLooks.cs), while the window's state stays the counted character's. Rename is on every row; counting
    /// for another one by hand (two characters of one class, which the icon can't tell apart) is on the others' rows.
    /// </summary>
    void ShowCharacterMenu()
    {
        if (_welcome) return;
        if (_card != null) _card.IsOpen = false;
        var card = BuildSwitcher();
        FrameworkElement at = CharChip.Visibility == Visibility.Visible ? CharChip : ServerChip;
        // the card's own margin (10, for its shadow) hangs outside: its left edge lines up with the chip's, 6 px under it
        _card = new InCard
        {
            Child = card, StaysOpen = false, AllowsTransparency = true, PopupAnimation = PopupAnimation.Fade, Placement = PlacementMode.Bottom, PlacementTarget = at,
            HorizontalOffset = -SwitcherInset, VerticalOffset = -4,
        };
        _card.Opened += (_, _) => card.Focus();
        _card.IsOpen = true;
    }

    const double SwitcherWidth = 392, SwitcherInset = 10;

    /// <summary>One row of the switcher: the character, whether it is the one counted for, when it was played, its week.</summary>
    internal sealed record SwitcherRow(Character Who, bool Counting, string Name, string Status, CharacterLook Look);

    /// <summary>The switcher's rows: this server's characters, the one counted for first, then the most recently played.</summary>
    internal List<SwitcherRow> SwitcherRows()
    {
        var cur = CurrentCharacter;
        var server = _prefs.Server ?? "";
        var all = _demo ? DemoCharacters() : Characters.All;
        return all.Where(c => c.Server.Length == 0 || server.Length == 0 || string.Equals(c.Server.Trim(), server.Trim(), StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(c => c.Id == cur?.Id).ThenByDescending(c => c.LastSeen)
            .Select(c =>
            {
                bool counting = c.Id == cur?.Id;
                return new SwitcherRow(c, counting, Characters.Label(c), counting ? "Counting now" : Played(c.LastSeen), LookOf(c, counting));
            }).ToList();
    }

    /// <summary>"Played 3 h ago", "Played yesterday", "Played Nov 9": when the character was last in the world.</summary>
    static string Played(DateTime lastSeenUtc)
    {
        if (lastSeenUtc == default) return "Not played yet";
        var ago = DateTime.UtcNow - lastSeenUtc;
        var local = lastSeenUtc.ToLocalTime();
        if (ago.TotalMinutes < 1) return "Played just now";
        if (ago.TotalMinutes < 60) return $"Played {(int)ago.TotalMinutes} min ago";
        if (local.Date == DateTime.Today) return $"Played {(int)ago.TotalHours} h ago";
        if (local.Date == DateTime.Today.AddDays(-1)) return "Played yesterday";
        if (local.Date > DateTime.Today.AddDays(-7)) return "Played " + local.ToString("dddd", System.Globalization.CultureInfo.InvariantCulture);
        return "Played " + local.ToString("MMM d", System.Globalization.CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// A character's seven days and best hour. The one counted for: the Week side's own days (today live from the day's
    /// count), so its figure is the one LOOT's Week side shows. Another one: read from its folder, nothing written.
    /// </summary>
    CharacterLook LookOf(Character c, bool counting)
    {
        try
        {
            if (_demo && !counting) return DemoLook(c);
            if (counting)
            {
                var best = _demo ? (new BestRuns.Best(1_400_000, 47, DayStore.DayOf(DateTime.Today.AddDays(-2))), "Western Cantas Valley") : CharacterLooks.BestIn(Characters.DirOf(c), _prefs.Server ?? "");
                return new CharacterLook(DayHistory.Days(7, TodayEngine, _items, WeekRunList(), WeekFile()), best?.Item1, best?.Item2 ?? "");
            }
            return CharacterLooks.Read(Characters.DirOf(c), _prefs.Server ?? "", _items);
        }
        catch (Exception ex) { CrashLog.Write("character look", ex); return new CharacterLook(new(), null, ""); }
    }

    /// <summary>The week's line under a name: "Last 7 days: 4.21M farmed, 23 runs".</summary>
    internal static string WeekLine(CharacterLook l)
    {
        var parts = new List<string>();
        if (l.Farmed > 0) parts.Add($"{Fk(l.Farmed)} farmed");
        if (l.Runs > 0) parts.Add($"{l.Runs:N0} run{(l.Runs == 1 ? "" : "s")}");
        return parts.Count == 0 ? "Nothing counted in the last 7 days" : "Last 7 days: " + string.Join(", ", parts);
    }

    /// <summary>The switcher's card: the head, a row a character, how characters are told apart, then the actions.</summary>
    Border BuildSwitcher()
    {
        var rows = SwitcherRows();
        var (card, body) = CkShell(SwitcherWidth);
        card.Focusable = true; card.FocusVisualStyle = null;
        AutomationProperties.SetName(card, "Characters");
        body.Children.Add(new Border
        {
            Height = 2, CornerRadius = new CornerRadius(5, 5, 0, 0), Margin = new Thickness(0, 0, 0, 6),
            Background = new LinearGradientBrush(Color.FromArgb(0xCC, CkGoldRule.R, CkGoldRule.G, CkGoldRule.B), Color.FromArgb(0x10, CkGoldRule.R, CkGoldRule.G, CkGoldRule.B), 0),
        });
        var head = new Grid { Margin = new Thickness(18, 4, 18, 8) };
        head.ColumnDefinitions.Add(new ColumnDefinition()); head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        head.Children.Add(CkText("Your characters", CkInk, 14, FontWeights.SemiBold));
        var where = CkText(Server is { } sv ? $"{sv.Label} · {sv.Region.Name}" : _prefs.Server ?? "", CkFaint, 12);
        where.Margin = new Thickness(12, 0, 0, 0); Grid.SetColumn(where, 1); head.Children.Add(where);
        body.Children.Add(head);
        if (rows.Count == 0)
        {
            var none = CkText("Your character shows here once you're in the world: the tracker reads the class icon between the health and mana bars. Each character you play keeps its own days, runs, goals and bests.", CkMuted, 12);
            none.TextWrapping = TextWrapping.Wrap; none.TextTrimming = TextTrimming.None; none.Margin = new Thickness(18, 0, 18, 8);
            body.Children.Add(none);
        }
        long top = rows.SelectMany(r => r.Look.Days).Select(CharacterLook.DayMade).DefaultIfEmpty(0).Max();
        foreach (var r in rows) body.Children.Add(SwitcherRowView(r, top));
        int elsewhere = _demo ? 0 : Characters.All.Count - rows.Count;
        body.Children.Add(MenuRow(MenuEntry.Line));
        if (rows.Count > 0)
        {
            var how = CkText("The tracker counts for the character in the game: it reads the class icon between the health and mana bars and switches by itself."
                + (elsewhere > 0 ? $" {elsewhere} more on other servers." : ""), CkFaint, 12);
            how.TextWrapping = TextWrapping.Wrap; how.TextTrimming = TextTrimming.None; how.Margin = new Thickness(18, 0, 18, 4);
            body.Children.Add(how);
            body.Children.Add(MenuRow(new MenuEntry("Count as a new character", () => { if (!_demo) _ = SplitNewCharacter(); },
                Sub: "A second character of the same class: the icon can't tell them apart.")));
        }
        body.Children.Add(MenuRow(new MenuEntry("Server and region…", () => OpenOptions(OptionsWindow.Page.Server), Sub: Server is { } s ? $"{s.Label} · {s.Region.Name}" : "None chosen")));
        body.Children.Add(new Border { Height = 6 });
        card.PreviewKeyDown += (_, k) => { if (k.Key == Key.Escape && _card != null) { k.Handled = true; _card.IsOpen = false; } };
        return card;
    }

    /// <summary>
    /// A character's row: the class icon (gold-edged for the one counted for), the name and when it was played (or
    /// "Counting now"), the week's line and the best hour; the seven days as small bars (today gold, on the right); Rename,
    /// and for another character Count for it.
    /// </summary>
    FrameworkElement SwitcherRowView(SwitcherRow r, long top)
    {
        var g = new Grid();
        foreach (var w in new[] { GridLength.Auto, new GridLength(1, GridUnitType.Star), GridLength.Auto, GridLength.Auto }) g.ColumnDefinitions.Add(new ColumnDefinition { Width = w });
        var tile = CharacterIcon.Tile(r.Who.Badges.FirstOrDefault(), r.Name, 34, lit: r.Counting);
        tile.VerticalAlignment = VerticalAlignment.Top; tile.Margin = new Thickness(0, 1, 11, 0);
        g.Children.Add(tile);

        var words = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        var line1 = new NameLine();
        var name = CkText(r.Name, CkInk, 13.5, FontWeights.SemiBold);
        if (r.Name.Length > 14) name.ToolTip = r.Name;
        line1.Children.Add(name);
        if (r.Counting)
        {
            var pill = new Border
            {
                CornerRadius = new CornerRadius(4), Padding = new Thickness(6, 1, 6, 1), Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center,
                Background = Ck(CkGood, 0x22), BorderBrush = Ck(CkGood, 0x55), BorderThickness = new Thickness(1),
                Child = CkText(r.Status, CkGood, 12, FontWeights.SemiBold),
            };
            line1.Children.Add(pill);
        }
        else { var st = CkText(r.Status, CkFaint, 12); st.Margin = new Thickness(8, 1, 0, 0); line1.Children.Add(st); }
        words.Children.Add(line1);
        var week = CkText(WeekLine(r.Look), r.Look.Any ? CkMuted : CkFaint, 12); week.Margin = new Thickness(0, 3, 0, 0);
        words.Children.Add(week);
        if (r.Look.Best is { } best)
        {
            var b = CkText($"Best hour {Fk(best.PerHr)}" + (r.Look.BestZone.Length > 0 ? " · " + r.Look.BestZone : ""), CkFaint, 12);
            b.Margin = new Thickness(0, 1, 0, 0);
            b.ToolTip = $"The best hour {r.Name} has farmed on this server: {Amounts.Full(best.PerHr)} Kinah and loot after the Market's cut, over {best.Minutes} minutes" + (r.Look.BestZone.Length > 0 ? $" in {r.Look.BestZone}" : "") + (best.At.Length > 0 ? $", on {best.At}." : ".");
            words.Children.Add(b);
        }
        Grid.SetColumn(words, 1); g.Children.Add(words);

        var bars = DayBars(r.Look, top);
        bars.Margin = new Thickness(10, 0, 6, 0); Grid.SetColumn(bars, 2); g.Children.Add(bars);

        var acts = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        var who = r.Who;
        acts.Children.Add(SwitcherButton(IcPencil, $"Rename {r.Name}", $"Rename {r.Name}. Names are read off the nameplate and can come out wrong; a name you type is kept.",
            () => { if (!_demo) OpenNameSheet(who); }));
        if (!r.Counting)
            acts.Children.Add(SwitcherButton(IcSwap, $"Count for {r.Name}", $"Count for {r.Name} from now on. For two characters of one class, which the icon can't tell apart; another class in the game switches back by itself. A run in progress ends first.",
                () => { if (!_demo) _ = SwitchByHand(who); }));
        Grid.SetColumn(acts, 3); g.Children.Add(acts);

        var row = new Border
        {
            Child = g, Padding = new Thickness(12, 9, 6, 9), Margin = new Thickness(6, 0, 6, 3), CornerRadius = new CornerRadius(7), BorderThickness = new Thickness(1),
            Background = r.Counting ? Ck(CkGold, 0x12) : Brushes.Transparent, BorderBrush = r.Counting ? Ck(CkGold, 0x48) : Ck(CkSlotLine, 0x99), Tag = "character",
        };
        AutomationProperties.SetName(row, r.Name + (r.Counting ? ", counting now" : ", " + r.Status));
        return row;
    }

    /// <summary>
    /// The seven days as bars, oldest on the left, today gold on the right; each day's farming and its runs' Kinah together,
    /// on one scale for every character (<paramref name="top"/>), so the rows compare. A day with nothing is a stub; each bar
    /// says its day's figures on hover.
    /// </summary>
    FrameworkElement DayBars(CharacterLook l, long top)
    {
        const double barW = 5, gap = 2, high = 26;
        var panel = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Height = high, Background = Brushes.Transparent };
        var days = l.Days.Count == 7 ? l.Days : Enumerable.Range(0, 7).Select(i =>
        {
            var d = DateTime.Today.AddDays(i - 6);
            return new HistoryDay(d, DayStore.DayOf(d), i == 6, true, new FarmDay { Day = DayStore.DayOf(d) }, RunsDay.None);
        }).ToList();
        foreach (var d in days)
        {
            long made = CharacterLook.DayMade(d);
            double h = made > 0 && top > 0 ? Math.Max(3, high * made / (double)top) : 2;
            var bar = new Border
            {
                Width = barW, Height = h, VerticalAlignment = VerticalAlignment.Bottom, CornerRadius = new CornerRadius(1.5), Margin = new Thickness(0, 0, d.IsToday ? 0 : gap, 0),
                Background = made <= 0 ? Ck(CkSlotLine) : d.IsToday ? Ck(CkGold) : Ck(CkMuted, 0x99),
            };
            var label = d.IsToday ? "Today" : d.Date.ToString("ddd d", System.Globalization.CultureInfo.InvariantCulture);
            var said = new List<string>();
            if (d.Farm.Made > 0) said.Add($"{Fk(d.Farm.Made)} farmed");
            if (d.Runs.Runs > 0) said.Add($"{d.Runs.Runs:N0} run{(d.Runs.Runs == 1 ? "" : "s")}, {Fk(d.Runs.Kinah)}");
            // a hit area the bar's full height, so a short bar is as easy to point at as a tall one
            var hit = new Border { Background = Brushes.Transparent, Child = bar, Height = high, ToolTip = $"{label}: " + (d.BeforeHistory ? "before this character's history began" : said.Count > 0 ? string.Join(" · ", said) : "nothing counted") };
            ToolTipService.SetInitialShowDelay(hit, 200);
            panel.Children.Add(hit);
        }
        AutomationProperties.SetName(panel, "Last 7 days");
        return panel;
    }

    const string IcPencil = "M4,20 L4.6,15.8 L15.4,5 A2,2 0 0 1 18.2,5 L19,5.8 A2,2 0 0 1 19,8.6 L8.2,19.4 Z M13.5,7 L17,10.5";
    const string IcSwap = "M5,8 H18 M14.5,4.5 L18,8 L14.5,11.5 M19,16 H6 M9.5,12.5 L6,16 L9.5,19.5";

    /// <summary>A small icon button on a switcher row: the line icon muted, gold under the mouse or the keyboard; a click closes the card, then acts.</summary>
    Border SwitcherButton(string geometry, string name, string tip, Action act)
    {
        var path = new Path
        {
            Data = Geometry.Parse(geometry), Width = 13, Height = 13, Stretch = Stretch.Uniform, Stroke = Ck(CkMuted), StrokeThickness = 1.6,
            StrokeLineJoin = PenLineJoin.Round, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round,
        };
        var b = new Border { Width = 26, Height = 26, CornerRadius = new CornerRadius(6), Background = Brushes.Transparent, Child = path, Cursor = Cursors.Hand, Focusable = true, FocusVisualStyle = null, ToolTip = tip };
        ToolTipService.SetInitialShowDelay(b, 300);
        AutomationProperties.SetName(b, name);
        void Lit(bool on) { b.Background = on ? Ck(CkGold, 0x1C) : Brushes.Transparent; path.Stroke = Ck(on ? CkGold : CkMuted); }
        b.MouseEnter += (_, _) => Lit(true); b.MouseLeave += (_, _) => Lit(b.IsKeyboardFocused);
        b.GotKeyboardFocus += (_, _) => Lit(true); b.LostKeyboardFocus += (_, _) => Lit(b.IsMouseOver);
        void Go() { if (_card != null) _card.IsOpen = false; Dispatcher.BeginInvoke(DispatcherPriority.Background, act); }
        b.MouseLeftButtonUp += (_, e) => { e.Handled = true; Go(); };
        b.KeyDown += (_, k) => { if (k.Key is Key.Enter or Key.Space) { k.Handled = true; Go(); } };
        return b;
    }

    /* ---------------- the demo's characters, renders and tests ---------------- */

    // the class icons as the tracker fingerprints them, read off tracker/tests/characters (shield-1, harp-1, knot-1 at 1080 high)
    const string DemoShield = "0000000000797aa3ab8276000000000000000000667c986d3093755e00000000000000005e788c100e4b814600000000000000657c99220f3e0f625d4b0000000000496b86401e71984817694a510000005550829c9cae9fb09ca99b84464500524b63c6926ca881ad739679b7873f545e4d8abc61769482b89f6d70738b53504a6660a76a7875a79f92786c669e454a5734529e7a707a99af7471736ea81247000ca8a66f7a717b9e72737166c75600000063c06d657593847168689c7b00000000009d5b6d6696b47f6a619700000000000079a764648c9387518bb2000000000000006d585f9b7058782800000000000000000017948eab9b560000000000";
    const string DemoHarp = "0000000000061e9e990f0600000000000000000000088a45516703000000000000000000035d6a0d0e6d3d050000000000000028678a1119151574471600000000003b3984230e433b0f28612d1d0000003a2270643c2a6a6b2b3b60501c1b00003057ac48856140425d8248a43c3100316d9a985268bb8584b567509598692b226d85964542ab5756a7424493825c20621f6b853455986460af5531836c2139000db4532e6d494e554b6c2c51b30e00000099222b5753625f3e4b2c239a00000000001f5257395a5a3a35521f00000000000024ac223649483642a924000000000000009e4a31494933519d0000000000000000005e2d4a97aa680000000000";
    const string DemoKnot = "00000000003537948f3a3e000000000000000000003079707a5d340000000000000000003f588d111681405b000000000000004c668a1e21202371553a00000000005a5389220e8e8c0e276944310000005842753f0e2e89992e0e3e584c5700004c526f0e18656c7d6c190f665856005749596c7d408499a1874082745147523d60106abd4eb36f81b24ec66f144d3c5e1f23b9b23b7fa387823bb9c125203f000d86cc8b4c49a3ab494c8fd48a0e000000ccc7a7308faea69230aacdd500000000e1c1295d629291625e27c2e4000000000078314a2a9ea1294b317a000000000000006b6859adac59666a0000000000000000008b29cece298e0000000000";

    /// <summary>The demo's name for the character counted for (a state's "character" sets another).</summary>
    public const string DemoCharacterName = "Lysandra";

    /// <summary>The demo's characters, for the renders: the one counted for (a shield) and two alts (a harp, a knot).</summary>
    List<Character> DemoCharacters() => new()
    {
        new() { Id = "c1", Name = _demoCharacter ?? DemoCharacterName, Server = "Siel (Elyos)", Badges = new() { DemoShield }, LastSeen = DateTime.UtcNow },
        new() { Id = "c2", Name = "Thornwick", NameRead = true, Server = "Siel (Elyos)", Badges = new() { DemoHarp }, LastSeen = DateTime.UtcNow.AddHours(-3) },
        new() { Id = "c3", Name = "Kaelith", NameRead = true, Server = "Siel (Elyos)", Badges = new() { DemoKnot }, LastSeen = DateTime.UtcNow.AddDays(-1).AddHours(-2) },
    };

    /// <summary>The demo alts' weeks: sample days, never a player's.</summary>
    static CharacterLook DemoLook(Character c)
    {
        var (farm, runs, best, zone) = c.Id == "c2"
            ? (new long[] { 0, 820_000, 1_140_000, 0, 640_000, 910_000, 380_000 }, new[] { 2, 4, 0, 3, 5, 1, 2 }, 1_120_000L, "Eastern Cantas Valley")
            : (new long[] { 0, 0, 0, 260_000, 0, 410_000, 0 }, new[] { 0, 0, 1, 0, 0, 2, 0 }, 760_000L, "Southern Tolbas Forest");
        var days = new List<HistoryDay>();
        for (int i = 0; i < 7; i++)
        {
            var d = DateTime.Today.AddDays(i - 6); var key = DayStore.DayOf(d);
            days.Add(new HistoryDay(d, key, i == 6, false, new FarmDay { Day = key, Made = farm[i], Active = farm[i] / 950_000.0 * 3600 },
                runs[i] == 0 ? RunsDay.None : new RunsDay(runs[i], runs[i], runs[i] * 58_000L, new() { new RunKind("Expedition", runs[i]) }, new())));
        }
        return new CharacterLook(days, new BestRuns.Best(best, 38, DayStore.DayOf(DateTime.Today.AddDays(-4))), zone);
    }

    /// <summary>Layout render: the switcher as the demo's chip opens it (ui-character-switcher.png).</summary>
    internal void ShotCharacterSwitcher(string dir) => SaveCard(BuildSwitcher(), System.IO.Path.Combine(dir, "ui-character-switcher.png"));

    /// <summary>
    /// The window with the switcher open under the chip, as a click shows it (KL_SWITCHER=1 with ui-shot): the card is a popup,
    /// which a render of the window alone never has, so it is drawn over the window where the popup would sit.
    /// </summary>
    internal void SaveWithSwitcher(string file)
    {
        var root = (FrameworkElement)Content;
        root.UpdateLayout();
        var card = BuildSwitcher();
        card.Resources = Resources;
        card.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        FrameworkElement at = CharChip.Visibility == Visibility.Visible ? CharChip : ServerChip;
        var p = at.TranslatePoint(new Point(-SwitcherInset, at.ActualHeight - 4), root);
        card.Arrange(new Rect(p, card.DesiredSize)); card.UpdateLayout();
        int w = (int)Math.Ceiling(Math.Max(root.ActualWidth, p.X + card.DesiredSize.Width)), h = (int)Math.Ceiling(Math.Max(root.ActualHeight, p.Y + card.DesiredSize.Height));
        var rtb = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
        var under = new DrawingVisual();
        using (var dc = under.RenderOpen()) dc.DrawRectangle(TryFindResource("Bg") as Brush ?? Brushes.Black, null, new Rect(0, 0, w, h));
        rtb.Render(under);
        rtb.Render(root);
        rtb.Render(card);
        var enc = new PngBitmapEncoder();
        enc.Frames.Add(BitmapFrame.Create(rtb));
        using var fs = System.IO.File.Create(file);
        enc.Save(fs);
    }

    /// <summary>For options-test: the switcher's rows as drawn (name, counted for, the status line, the week's line).</summary>
    internal List<(string Name, bool Counting, string Status, string Week, bool Picture)> SwitcherForTest() =>
        SwitcherRows().Select(r => (r.Name, r.Counting, r.Status, WeekLine(r.Look), CharacterIcon.Picture(r.Who.Badges.FirstOrDefault()) != null)).ToList();

    /// <summary>For options-test: the item list the window prices with.</summary>
    internal KinahLedger.Data.ItemDictionary ItemsForTest => _items;

    /// <summary>For options-test: the switcher's card, built as a click builds it.</summary>
    internal Border SwitcherCardForTest() => BuildSwitcher();

    /// <summary>
    /// The player picks a character. Nothing is taught: when another class's icon is on screen the reader switches back
    /// once it is steady; two characters of one class stay put, as the one counted for keeps an icon it matches.
    /// </summary>
    async Task SwitchByHand(Character c)
    {
        _watcher.CharacterHandled();
        await SwitchCharacter(c, Characters.Label(c));
    }

    /// <summary>A second character of a class already counted: a new character with the icon on screen, counted from now on.</summary>
    async Task SplitNewCharacter()
    {
        var c = Characters.Add(_watcher.Spotter.Print ?? "", _prefs.Server);
        _watcher.CharacterHandled();
        await SwitchCharacter(c, "A new character");
        OpenNameSheet(c);
    }

    /// <summary>A small card to name a character: a box, Save and Cancel.</summary>
    void OpenNameSheet(Character c)
    {
        if (_card != null) _card.IsOpen = false;
        var (card, body) = CkShell(320);
        var title = CkText("Name this character", CkInk, 14); title.FontWeight = FontWeights.SemiBold; title.Margin = new Thickness(20, 14, 20, 4);
        body.Children.Add(title);
        var hint = CkText("As it is in the game, or anything that tells it apart. Only on this PC.", CkFaint, 12);
        hint.TextWrapping = TextWrapping.Wrap; hint.TextTrimming = TextTrimming.None; hint.Margin = new Thickness(20, 0, 20, 10);
        body.Children.Add(hint);
        var box = new TextBox
        {
            Height = 34, FontSize = 13, Padding = new Thickness(10, 0, 10, 0), VerticalContentAlignment = VerticalAlignment.Center, Text = c.Name, MaxLength = 24,
            Background = Ck(CkSlot), BorderBrush = Ck(CkFrame), Foreground = Ck(CkInk), CaretBrush = Ck(CkGold), Margin = new Thickness(20, 0, 20, 12),
        };
        AutomationProperties.SetName(box, "Character name");
        body.Children.Add(box);
        var acts = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(20, 0, 20, 16) };
        var save = new Button { Content = "Save", Height = 30, Padding = new Thickness(16, 0, 16, 0), FontSize = 12.5 };
        save.SetResourceReference(StyleProperty, "PrimaryButton");
        var cancel = new Button { Content = "Cancel", Height = 30, Padding = new Thickness(14, 0, 14, 0), FontSize = 12.5, Margin = new Thickness(8, 0, 0, 0) };
        void Save()
        {
            Characters.Rename(c, box.Text);
            if (_card != null) _card.IsOpen = false;
            ServerRowState(); Redraw();
        }
        save.Click += (_, _) => Save();
        cancel.Click += (_, _) => { if (_card != null) _card.IsOpen = false; };
        box.KeyDown += (_, k) => { if (k.Key == Key.Enter) { k.Handled = true; Save(); } };
        acts.Children.Add(save); acts.Children.Add(cancel);
        body.Children.Add(acts);
        _card = new InCard
        {
            Child = card, StaysOpen = false, AllowsTransparency = true, PopupAnimation = PopupAnimation.Fade, Placement = PlacementMode.Bottom,
            PlacementTarget = CharChip.Visibility == Visibility.Visible ? CharChip : ServerChip, HorizontalOffset = -SwitcherInset, VerticalOffset = -4,
        };
        _card.Opened += (_, _) => { box.Focus(); box.SelectAll(); };
        _card.IsOpen = true;
    }
}
