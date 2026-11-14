using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using KinahLedger.Packets;
using KinahLedger.Parser;
using KinahLedger.Platform;
using KinahLedger.Session;

namespace KinahLedger.Ui;

/// <summary>
/// The Parser page (Steven, 2026-11-13: "It should have its own tab for parser"): boss fights in a Transcendence, an
/// Expedition or a Sanctuary, one per boss (Parser/BossParser.cs). On top, the fight under way with its clock and the group
/// (name, class, Combat Power, item level; damage and DPS only when the game's messages give them, never made up) or, with
/// no fight, where you are and what starts one. Under it, the fights kept on this PC for the character playing, filtered by
/// kind and boss; a click opens one with its group, its timings and the kills near you. The overlay over the game
/// (Ui/ParserOverlay.cs) is switched here and with its shortcut.
/// </summary>
public partial class MainWindow
{
    ParserPrefs _parserPrefs = new();
    /// <summary>The kept fight open in the detail view, or null for the list.</summary>
    string? _parserOpen;
    bool _parserAllRows;
    /// <summary>A layout render's sample (ParserDemo): the page shows it instead of this PC's fights.</summary>
    List<Encounter>? _parserDemoPast;
    Encounter? _parserDemoLive;
    (ContentKind Kind, string Instance) _parserDemoWhere;
    DateTime _parserDemoNow;
    ParserOverlay? _parserOverlay;
    /// <summary>The last fight ended, and when: the overlay shows its result for a few seconds after the kill.</summary>
    Encounter? _parserJustEnded;
    DateTime _parserEndedAt = DateTime.MinValue;
    string _parserLastLiveId = "";
    const double OverlayResultSeconds = 15;
    const int ParserHotkeyId = 3;
    const int ParserRowsAtRest = 10;

    void InitParser(bool watch)
    {
        _parserPrefs = watch ? ParserPrefs.Load() : new ParserPrefs();
        ParserTab.Click += (_, _) => ShowPart("Parser");
        if (!watch) return;
        SourceInitialized += (_, _) =>
        {
            var h = new WindowInteropHelper(this).Handle;
            HwndSource.FromHwnd(h)?.AddHook(ParserWndProc);
            RegisterParserHotkey(h);
        };
        Closed += (_, _) =>
        {
            var h = new WindowInteropHelper(this).Handle;
            if (h != IntPtr.Zero) Native.UnregisterHotKey(h, ParserHotkeyId);
            _parserOverlay?.Close();
        };
    }

    /// <summary>The overlay's shortcut (parser.json, Ctrl+Shift+P by default); one that's taken or not allowed is left off and logged.</summary>
    void RegisterParserHotkey(IntPtr h)
    {
        if (Hotkey.Parse(_parserPrefs.Hotkey) is not { } key) return;
        if (!Native.RegisterHotKey(h, ParserHotkeyId, key.NativeModifiers, key.VirtualKey))
            CrashLog.Note($"parser: the overlay's shortcut {key} is taken by another program; switch it on the Parser page");
    }

    IntPtr ParserWndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == Native.WM_HOTKEY && wParam.ToInt32() == ParserHotkeyId) { SetParserOverlay(!_parserPrefs.Overlay); handled = true; }
        return IntPtr.Zero;
    }

    void SetParserOverlay(bool on)
    {
        _parserPrefs.Overlay = on;
        if (!_demo) _parserPrefs.Save();
        Say(on ? "Fight overlay on: it shows over the game during boss fights." : "Fight overlay off.", "Faint");
        ParserOverlayTick();
        RedrawParserPage();
    }

    void SetParserClickThrough(bool on)
    {
        _parserPrefs.ClickThrough = on;
        if (!_demo) _parserPrefs.Save();
        _parserOverlay?.SetClickThrough(on);
        RedrawParserPage();
    }

    // ---- what the page shows ----

    BossParser? ParserFights => _watcher.Chat?.Fights;
    DateTime ParserNow => _demo ? _parserDemoNow : DateTime.UtcNow;
    Encounter? ParserLive => _demo ? _parserDemoLive : ParserFights?.Live;
    (ContentKind Kind, string Instance) ParserWhere => _demo ? _parserDemoWhere : ParserFights?.Where ?? (ContentKind.Other, "");
    List<Encounter> ParserPast() => _demo ? _parserDemoPast ?? new() : TrackerSession.FightStore.All();

    /// <summary>The window's tick: the page while it shows, and the overlay always.</summary>
    void ParserTick()
    {
        try
        {
            if (_part == "Parser") FillParserPage();
            ParserOverlayTick();
        }
        catch (Exception ex) { CrashLog.Write("parser page", ex); }
    }

    void RedrawParserPage() { _drawnSig.Remove("parserpage"); FillParserPage(); }

    /// <summary>
    /// The overlay follows the fight: on (its switch), a boss fight under way or one that ended in the last
    /// <see cref="OverlayResultSeconds"/>, and the game (or the tracker) in front. Made on first need, never in a render.
    /// </summary>
    void ParserOverlayTick()
    {
        if (_demo) return;
        var now = DateTime.UtcNow;
        var live = ParserLive;
        // the fight that just ended: its result shows a little while
        if (live == null && _parserLastLiveId.Length > 0)
        {
            _parserJustEnded = TrackerSession.FightStore.All().LastOrDefault(e => e.Id == _parserLastLiveId);
            _parserEndedAt = now;
            _parserLastLiveId = "";
        }
        if (live != null) { _parserLastLiveId = live.Id; _parserJustEnded = null; }
        var show = live ?? ((now - _parserEndedAt).TotalSeconds <= OverlayResultSeconds ? _parserJustEnded : null);
        bool inFront = _watcher.GameRect != null && (Native.ForegroundPid() == (uint)Environment.ProcessId || GameInFront());
        bool want = _parserPrefs.Overlay && show != null && inFront;
        if (!want) { _parserOverlay?.ShowWanted(false); return; }
        if (_parserOverlay == null)
        {
            _parserOverlay = new ParserOverlay { HotkeyText = Hotkey.Parse(_parserPrefs.Hotkey)?.ToString() ?? "" };
            _parserOverlay.Dropped += (x, y) => { _parserPrefs.Left = x; _parserPrefs.Top = y; _parserPrefs.Save(); };
            _parserOverlay.SetClickThrough(_parserPrefs.ClickThrough);
            _parserOverlay.Draw(show!, now);
            _parserOverlay.ShowWanted(true);
            _parserOverlay.SetClickThrough(_parserPrefs.ClickThrough);
            _parserOverlay.Place(_parserPrefs.Left, _parserPrefs.Top, _watcher.GameRect);
            return;
        }
        _parserOverlay.Draw(show!, now);
        _parserOverlay.ShowWanted(true);
    }

    void FillParserPage()
    {
        bool show = _part == "Parser" && !_welcome;
        ParserPage.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        if (!show) { _drawnSig.Remove("parserpage"); return; }
        var now = ParserNow;
        var live = ParserLive;
        var past = ParserPast();
        var where = ParserWhere;
        var sig = $"{live?.Id}:{(int)(live?.Duration(now).TotalSeconds ?? 0)}:{live?.Roster.Sum(f => f.Damage)}:{live?.Roster.Count}:{live?.Kills.Count}:{live?.Partial}|{past.Count}:{past.LastOrDefault()?.Id}|{where}|{_parserOpen}|{_parserAllRows}|{_parserPrefs.KindFilter}|{_parserPrefs.BossFilter}|{_parserPrefs.Overlay}:{_parserPrefs.ClickThrough}|{Skins.Current}|{ParserStatus(where).Line}";
        if (!Changed("parserpage", sig)) return;

        ParserPage.Children.Clear();
        ParserPage.Children.Add(ParserHead(past));
        var open = _parserOpen != null ? past.FirstOrDefault(e => e.Id == _parserOpen) : null;
        if (open != null) { ParserPage.Children.Add(ParserDetail(open)); return; }
        _parserOpen = null;
        ParserPage.Children.Add(live != null ? ParserLiveCard(live, now) : ParserWaitingCard(where, past));
        ParserPage.Children.Add(ParserHistory(past, now));
    }

    /// <summary>The page's head: the title and its one line, the overlay's switches on the right.</summary>
    FrameworkElement ParserHead(List<Encounter> past)
    {
        var g = new Grid { Margin = new Thickness(2, 0, 0, 10) };
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var left = new StackPanel { VerticalAlignment = VerticalAlignment.Bottom };
        left.Children.Add(PT("Parser", 20, FontWeights.SemiBold, "Ink", display: true));
        left.Children.Add(Margin(PT("Boss fights in Transcendence, Expeditions and Sanctuaries.", 12, FontWeights.Medium, "Muted", trim: true), 0, 3, 0, 0));
        g.Children.Add(left);
        var right = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(12, 0, 0, 0) };
        right.Children.Add(ParserSwitch("Overlay", _parserPrefs.Overlay, SetParserOverlay,
            $"The fight over the game: the boss, the clock and the group. It shows during boss fights only and never takes the game's focus.{(Hotkey.Parse(_parserPrefs.Hotkey) is { } k ? $" Shortcut: {k}." : "")}", "Fight overlay"));
        right.Children.Add(Margin(ParserSwitch("Click-through", _parserPrefs.ClickThrough, SetParserClickThrough,
            "On: clicks go through the overlay to the game. Off: drag the overlay where you want it; it stays there.", "Overlay click-through"), 14, 0, 0, 0));
        Grid.SetColumn(right, 1);
        g.Children.Add(right);
        return g;
    }

    FrameworkElement ParserSwitch(string label, bool on, Action<bool> set, string tip, string name)
    {
        var sp = new StackPanel { Orientation = Orientation.Horizontal, ToolTip = tip };
        var t = PT(label, 12, FontWeights.SemiBold, on ? "Ink" : "Muted");
        t.Margin = new Thickness(0, 0, 7, 0);
        var cb = new CheckBox { IsChecked = on, VerticalAlignment = VerticalAlignment.Center };
        cb.SetResourceReference(StyleProperty, "Switch");
        AutomationProperties.SetName(cb, name);
        cb.Click += (_, _) => set(cb.IsChecked == true);
        t.MouseLeftButtonUp += (_, _) => set(!on);
        t.Cursor = Cursors.Hand;
        sp.Children.Add(t); sp.Children.Add(cb);
        return sp;
    }

    // ---- the fight under way ----

    FrameworkElement ParserLiveCard(Encounter e, DateTime now)
    {
        var card = PCard(hero: true);
        var body = new StackPanel();
        body.Children.Add(FightHead(e, now, live: true));
        if (FightChips(e, now, live: true) is { } chips) body.Children.Add(Margin(chips, 0, 10, 0, 0));
        body.Children.Add(Margin(RosterTable(e, now, detail: false), 0, 10, 0, 0));
        card.Child = body;
        return card;
    }

    /// <summary>The fight's head: the boss's art, its name, the instance and kind under it; the clock and the outcome on the right.</summary>
    FrameworkElement FightHead(Encounter e, DateTime now, bool live)
    {
        var g = new Grid();
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var art = new Border { Width = 44, Height = 44, CornerRadius = new CornerRadius(10), Margin = new Thickness(0, 0, 12, 0), Child = GameArt.Element("boss", 34) };
        art.SetResourceReference(Border.BackgroundProperty, "SegGroup");
        art.SetResourceReference(Border.BorderBrushProperty, live ? "GoldLine" : "CardHair");
        art.BorderThickness = new Thickness(1);
        g.Children.Add(art);
        var mid = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        var name = PT(e.Boss, live ? 22 : 19, FontWeights.SemiBold, "Ink", trim: true, display: true);
        AutomationProperties.SetName(name, "Boss " + e.Boss);
        mid.Children.Add(name);
        var sub = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 3, 0, 0) };
        sub.Children.Add(PT(BossBook.Strip(e.Instance), 12, FontWeights.Medium, "Muted", trim: true));
        sub.Children.Add(PT("  ·  " + KindLabel(e), 12, FontWeights.Medium, "Faint"));
        if (e.FinalBoss) sub.Children.Add(PT("  ·  final boss", 12, FontWeights.Medium, "Faint"));
        mid.Children.Add(sub);
        Grid.SetColumn(mid, 1); g.Children.Add(mid);
        var right = new StackPanel { VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(12, 0, 0, 0) };
        var clock = PT(BossParser.Clock(e.Duration(now)), live ? 30 : 24, FontWeights.Bold, live ? "Gold" : "Ink", display: true);
        clock.HorizontalAlignment = HorizontalAlignment.Right;
        AutomationProperties.SetName(clock, "Fight time " + BossParser.Clock(e.Duration(now)));
        right.Children.Add(clock);
        var chipRow = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 2, 0, 0) };
        chipRow.Children.Add(OutcomeChip(e));
        right.Children.Add(chipRow);
        Grid.SetColumn(right, 2); g.Children.Add(right);
        return g;
    }

    static string KindLabel(Encounter e)
    {
        // "Shattered Arkanis [Stage 3]" says its stage beside the kind
        int i = e.Instance.IndexOf(" [", StringComparison.Ordinal);
        var stage = i > 0 ? e.Instance[(i + 2)..].TrimEnd(']') : "";
        return stage.Length > 0 ? $"{e.Kind} {stage}" : e.Kind;
    }

    /// <summary>"Killed" in mint, "Wiped" in coral, "Left" quiet, "In progress" in gold with a dot.</summary>
    FrameworkElement OutcomeChip(Encounter e)
    {
        var (ink, tint) = e.Outcome switch { "kill" => ("Mint", "MintTint"), "wipe" => ("Coral", "CoralTint"), "left" => ("Muted", "ChipBg"), _ => ("Gold", "GoldTint") };
        return PChip(e.OutcomeLabel, ink, tint, dot: e.Live);
    }

    /// <summary>The figures line: group DPS and boss damage when known, kills near you, deaths, and partial with why.</summary>
    FrameworkElement? FightChips(Encounter e, DateTime now, bool live)
    {
        var wp = new WrapPanel();
        void Add(FrameworkElement c) { c.Margin = new Thickness(0, 0, 6, 4); wp.Children.Add(c); }
        if (e.HasDamage)
        {
            Add(PStat("Group DPS", Amounts.Short(e.GroupDps(now)), "Ink"));
            if (e.BossDamage > 0) Add(PStat("Boss damage", Amounts.Short(e.BossDamage), "Ink"));
        }
        int adds = e.Kills.Count(k => !k.Boss);
        if (adds > 0) Add(PStat("Kills near you", adds.ToString("N0", CultureInfo.InvariantCulture), "Ink"));
        int deaths = e.Roster.Sum(f => f.Deaths);
        if (deaths > 0) Add(PStat("Deaths", deaths.ToString("N0", CultureInfo.InvariantCulture), "Coral"));
        if (!e.HasDamage && e.Roster.Count > 0) Add(PStat("Group CP", AvgCp(e), "Ink", "average"));
        if (e.Partial) { var c = PChip("Partial", "Warn", "GoldTint"); c.ToolTip = "Some of this fight wasn't seen: " + string.Join("; ", e.PartialWhy) + ". It is still one fight; its damage and kills may be short."; Add(c); }
        return wp.Children.Count > 0 ? wp : null;
    }

    static string AvgCp(Encounter e)
    {
        var known = e.Roster.Where(f => f.CombatPower > 0).ToList();
        return known.Count == 0 ? "–" : ((int)Math.Round(known.Average(f => f.CombatPower))).ToString("N0", CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// The group: class mark, name (you marked), class, Combat Power, item level; with damage from the game, damage, DPS
    /// and a share bar under the name; in the detail view, deaths too. Unknown figures say "–", never 0.
    /// </summary>
    FrameworkElement RosterTable(Encounter e, DateTime now, bool detail)
    {
        var host = new StackPanel();
        if (e.Roster.Count == 0)
        {
            host.Children.Add(PT("The group wasn't seen for this fight.", 12, FontWeights.Medium, "Faint"));
            return host;
        }
        bool dmg = e.HasDamage;
        bool deaths = detail && e.Roster.Any(f => f.Deaths > 0);
        var cols = new List<(double Width, string Head, bool Right)> { (28, "", false), (0, "Member", false), (92, "Class", false), (64, "CP", true), (62, "Item lvl", true) };
        if (dmg) { cols.Add((76, "Damage", true)); cols.Add((66, "DPS", true)); }
        if (deaths) cols.Add((50, "Deaths", true));
        Grid Row()
        {
            var g = new Grid();
            foreach (var c in cols) g.ColumnDefinitions.Add(new ColumnDefinition { Width = c.Width > 0 ? new GridLength(c.Width) : new GridLength(1, GridUnitType.Star) });
            return g;
        }
        void Cell(Grid g, int col, FrameworkElement el) { Grid.SetColumn(el, col); g.Children.Add(el); }

        var head = Row(); head.Margin = new Thickness(0, 0, 0, 4);
        for (int i = 0; i < cols.Count; i++)
        {
            if (cols[i].Head.Length == 0) continue;
            var t = PT(cols[i].Head, 11, FontWeights.SemiBold, "Faint");
            if (cols[i].Right) t.HorizontalAlignment = HorizontalAlignment.Right;
            Cell(head, i, t);
        }
        host.Children.Add(head);
        long top = Math.Max(1, e.Roster.Max(f => f.Damage));
        bool first = true;
        foreach (var f in e.Ranked())
        {
            var row = Row();
            var rule = new Border { BorderThickness = new Thickness(0, first ? 0 : 1, 0, 0), Padding = new Thickness(0, 6, 0, 6), Child = row };
            rule.SetResourceReference(Border.BorderBrushProperty, "CardHair");
            first = false;
            Cell(row, 0, ClassMark(f.Class, 20));
            var nameBox = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) };
            var nameLine = new StackPanel { Orientation = Orientation.Horizontal };
            nameLine.Children.Add(PT(f.Name.Length > 0 ? f.Name : "Unknown", 13, FontWeights.SemiBold, f.You ? "Gold" : "Ink", trim: true));
            if (f.You) nameLine.Children.Add(Margin(PT("you", 11, FontWeights.SemiBold, "Faint"), 6, 1, 0, 0));
            if (f.Late) nameLine.Children.Add(Margin(PT("joined late", 11, FontWeights.Medium, "Faint"), 6, 1, 0, 0));
            if (f.Gone) nameLine.Children.Add(Margin(PT("left", 11, FontWeights.Medium, "Faint"), 6, 1, 0, 0));
            nameBox.Children.Add(nameLine);
            if (dmg) nameBox.Children.Add(ShareBar(f.Damage / (double)top, f.You));
            Cell(row, 1, nameBox);
            Cell(row, 2, PT(f.Class.Length > 0 ? f.Class : "–", 12, FontWeights.Medium, "Muted", trim: true));
            Cell(row, 3, Right(PT(f.CombatPower > 0 ? f.CombatPower.ToString("N0", CultureInfo.InvariantCulture) : "–", 13, FontWeights.SemiBold, "Ink")));
            Cell(row, 4, Right(PT(f.ItemLevel > 0 ? f.ItemLevel.ToString("N0", CultureInfo.InvariantCulture) : "–", 13, FontWeights.Medium, "Text2")));
            int c = 5;
            if (dmg)
            {
                Cell(row, c++, Right(PT(Amounts.Short(f.Damage), 13, FontWeights.Medium, "Text2")));
                Cell(row, c++, Right(PT(Amounts.Short(e.Dps(f, now)), 13, FontWeights.Bold, f.You ? "Gold" : "Ink")));
            }
            if (deaths) Cell(row, c, Right(PT(f.Deaths > 0 ? f.Deaths.ToString(CultureInfo.InvariantCulture) : "–", 13, FontWeights.Medium, f.Deaths > 0 ? "Coral" : "Faint")));
            host.Children.Add(rule);
        }
        return host;
    }

    FrameworkElement ShareBar(double share, bool you)
    {
        var track = new Grid { Height = 3, Margin = new Thickness(0, 4, 0, 0), MaxWidth = 220, HorizontalAlignment = HorizontalAlignment.Stretch };
        var back = new Border { CornerRadius = new CornerRadius(1.5) }; back.SetResourceReference(Border.BackgroundProperty, "SegGroup");
        var fill = new Border { CornerRadius = new CornerRadius(1.5), HorizontalAlignment = HorizontalAlignment.Left };
        fill.SetResourceReference(Border.BackgroundProperty, you ? "Gold" : "Muted");
        track.Children.Add(back); track.Children.Add(fill);
        track.SizeChanged += (_, _) => fill.Width = Math.Max(2, track.ActualWidth * Math.Clamp(share, 0, 1));
        return track;
    }

    /// <summary>
    /// A class's mark: its two letters on a tile in the class's role colour (no class art in the tracker yet). Tanks blue,
    /// healers and support green, melee coral, casters and ranged violet; an unknown class a quiet tile.
    /// </summary>
    internal static FrameworkElement ClassMark(string cls, double size)
    {
        var c = ClassColor(cls);
        var b = new Border
        {
            Width = size, Height = size, CornerRadius = new CornerRadius(size * 0.28), VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Left,
            Background = Frozen(Color.FromArgb(0x2E, c.R, c.G, c.B)), BorderBrush = Frozen(Color.FromArgb(0x80, c.R, c.G, c.B)), BorderThickness = new Thickness(1),
            ToolTip = cls.Length > 0 ? cls : null,
        };
        var letters = cls.Trim().Length >= 2 ? cls.Trim()[..2] : cls.Trim().Length == 1 ? cls.Trim() : "?";
        b.Child = new TextBlock { Text = letters, FontSize = size * 0.45, FontWeight = FontWeights.Bold, Foreground = Frozen(c), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        return b;
    }

    internal static Color ClassColor(string cls) => cls.Trim().ToLowerInvariant() switch
    {
        "templar" or "gladiator" => Color.FromRgb(0x6F, 0xA8, 0xF0),
        "cleric" or "chanter" => Color.FromRgb(0x3D, 0xD6, 0x8C),
        "assassin" => Color.FromRgb(0xF2, 0x8A, 0x6D),
        "ranger" or "sorcerer" or "spiritmaster" => Color.FromRgb(0xB3, 0x8C, 0xF5),
        _ => Color.FromRgb(0x86, 0x8D, 0xA1),
    };

    static SolidColorBrush Frozen(Color c) { var b = new SolidColorBrush(c); b.Freeze(); return b; }

    /// <summary>What the Parser is doing with no fight under way, and where you are; null line when all is well.</summary>
    (string Head, string Line, string Tone) ParserStatus((ContentKind Kind, string Instance) where)
    {
        bool kept = where.Kind is ContentKind.Transcendence or ContentKind.Expedition or ContentKind.Sanctuary;
        if (!_demo && _watcher.Packets is { } pr && !pr.On)
            return ("Not reading the game's messages", pr.State.StartsWith("Off: ") ? pr.State[5..] + "." : "Fights come from the game's own messages; the screen alone can't tell a boss.", "Warn");
        if (kept)
            return ($"Ready in {BossBook.Strip(where.Instance)}", $"{Encounter.KindName(where.Kind)}. The next boss you pull starts a fight here; trash and adds never do.", "Good");
        return ("Waiting for a boss", "Fights are kept in Transcendence, Expeditions and Sanctuaries only. Enter one and pull a boss.", "Faint");
    }

    FrameworkElement ParserWaitingCard((ContentKind Kind, string Instance) where, List<Encounter> past)
    {
        var card = PCard(hero: false);
        card.Padding = new Thickness(14, 12, 14, 12);
        var g = new Grid();
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var art = GameArt.Element("dungeon", 30); art.Margin = new Thickness(0, 0, 12, 0); art.Opacity = 0.85;
        g.Children.Add(art);
        var (head, line, tone) = ParserStatus(where);
        var sp = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        var h = new StackPanel { Orientation = Orientation.Horizontal };
        var dot = new Border { Width = 7, Height = 7, CornerRadius = new CornerRadius(3.5), Margin = new Thickness(0, 1, 7, 0), VerticalAlignment = VerticalAlignment.Center };
        dot.SetResourceReference(Border.BackgroundProperty, tone);
        h.Children.Add(dot);
        h.Children.Add(PT(head, 14, FontWeights.SemiBold, "Ink", trim: true));
        sp.Children.Add(h);
        var l = PT(line, 12, FontWeights.Medium, "Text2"); l.TextWrapping = TextWrapping.Wrap; l.Margin = new Thickness(0, 3, 0, 0);
        sp.Children.Add(l);
        Grid.SetColumn(sp, 1); g.Children.Add(sp);
        // the last fight, one line, so the card says something useful between pulls
        if (past.LastOrDefault() is { } last)
        {
            var lastBox = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 0) };
            var lt = PT("Last fight", 11, FontWeights.SemiBold, "Faint"); lt.HorizontalAlignment = HorizontalAlignment.Right;
            lastBox.Children.Add(lt);
            var lr = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 3, 0, 0) };
            lr.Children.Add(PT(last.Boss, 12.5, FontWeights.SemiBold, "Ink", trim: true));
            lr.Children.Add(PT("  " + BossParser.Clock(last.Duration(last.End!.Value)) + "  ", 12.5, FontWeights.Medium, "Muted"));
            lr.Children.Add(OutcomeChip(last));
            lastBox.Children.Add(lr);
            lastBox.MaxWidth = 280;
            Grid.SetColumn(lastBox, 2); g.Children.Add(lastBox);
        }
        card.Child = g;
        return card;
    }

    // ---- the fights kept ----

    FrameworkElement ParserHistory(List<Encounter> past, DateTime now)
    {
        var card = new Border { Margin = new Thickness(0, 8, 0, 0) };
        card.SetResourceReference(StyleProperty, "QRefCard");
        var body = new StackPanel();
        // the head: the title and count, the kind switch, the boss pick
        var head = new Grid { Margin = new Thickness(0, 0, 0, 8) };
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var titleRow = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        var ic = GameArt.Element("scroll", 16); ic.Margin = new Thickness(0, 0, 7, 0); ic.Opacity = 0.75;
        titleRow.Children.Add(ic);
        var title = PT("Past fights", 12, FontWeights.Medium, "Muted");
        titleRow.ToolTip = "Every boss fight of this character, kept on this PC only: never uploaded."; title.SetResourceReference(StyleProperty, "RefHead");
        titleRow.Children.Add(title);
        head.Children.Add(titleRow);
        var kinds = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
        var seg = new Border { Padding = new Thickness(3), CornerRadius = new CornerRadius(10), BorderThickness = new Thickness(1), Child = kinds, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(10, 0, 0, 0), Visibility = past.Count == 0 ? Visibility.Collapsed : Visibility.Visible };
        seg.SetResourceReference(Border.BackgroundProperty, "SegGroup"); seg.SetResourceReference(Border.BorderBrushProperty, "CardLine");
        foreach (var (key, label) in new[] { ("", "All"), ("Transcendence", "Transcendence"), ("Expedition", "Expedition"), ("Sanctuary", "Sanctuary") })
        {
            int n = key.Length == 0 ? past.Count : past.Count(e => e.Kind == key);
            var b = new Button { Content = n > 0 ? $"{label} {n}" : label, Height = 24, FontSize = 12, Padding = new Thickness(9, 0, 9, 0), Margin = new Thickness(kinds.Children.Count > 0 ? 2 : 0, 0, 0, 0) };
            b.SetResourceReference(StyleProperty, _parserPrefs.KindFilter == key ? "SegOn" : "SegOff");
            b.Click += (_, _) => { _parserPrefs.KindFilter = key; _parserPrefs.BossFilter = ""; _parserAllRows = false; if (!_demo) _parserPrefs.Save(); RedrawParserPage(); };
            kinds.Children.Add(b);
        }
        Grid.SetColumn(seg, 1); head.Children.Add(seg);
        var inKind = past.Where(e => _parserPrefs.KindFilter.Length == 0 || e.Kind == _parserPrefs.KindFilter).ToList();
        var bossBtn = new Button { Height = 30, FontSize = 12, Padding = new Thickness(10, 0, 10, 0), Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        bossBtn.Content = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Children =
            {
                PT(_parserPrefs.BossFilter.Length > 0 ? _parserPrefs.BossFilter : "Any boss", 12, FontWeights.SemiBold, "Ink", trim: true),
                new System.Windows.Shapes.Path { Data = Geometry.Parse("M0,0 L3.5,3.5 L7,0"), StrokeThickness = 1.4, Margin = new Thickness(7, 1, 0, 0), VerticalAlignment = VerticalAlignment.Center, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round },
            },
        };
        ((System.Windows.Shapes.Path)((StackPanel)bossBtn.Content).Children[1]).SetResourceReference(System.Windows.Shapes.Shape.StrokeProperty, "Muted");
        ((TextBlock)((StackPanel)bossBtn.Content).Children[0]).MaxWidth = 150;
        AutomationProperties.SetName(bossBtn, "Filter by boss");
        bossBtn.ToolTip = "Show one boss's fights";
        bossBtn.Click += (_, _) =>
        {
            var entries = new List<MenuEntry> { new("Any boss", () => { _parserPrefs.BossFilter = ""; ParserFilterChanged(); }, Sub: $"{inKind.Count} fights", On: _parserPrefs.BossFilter.Length == 0) };
            foreach (var g in inKind.GroupBy(e => e.Boss, StringComparer.OrdinalIgnoreCase).OrderByDescending(g => g.Max(e => e.Start)))
            {
                var boss = g.First().Boss;
                int kills = g.Count(e => e.Outcome == "kill");
                entries.Add(new MenuEntry(boss, () => { _parserPrefs.BossFilter = boss; ParserFilterChanged(); }, Sub: $"{BossBook.Strip(g.First().Instance)} · {g.Count()} fight{(g.Count() == 1 ? "" : "s")}, {kills} kill{(kills == 1 ? "" : "s")}", On: _parserPrefs.BossFilter == boss));
            }
            ShowMenu(bossBtn, entries);
        };
        if (inKind.Count == 0) bossBtn.Visibility = Visibility.Collapsed;
        Grid.SetColumn(bossBtn, 2); head.Children.Add(bossBtn);
        body.Children.Add(head);

        var shown = inKind.Where(e => _parserPrefs.BossFilter.Length == 0 || e.Boss.Equals(_parserPrefs.BossFilter, StringComparison.OrdinalIgnoreCase)).OrderByDescending(e => e.Start).ToList();
        if (shown.Count == 0)
        {
            var empty = PT(past.Count == 0 ? "No fights kept yet. Each boss you fight in a Transcendence, an Expedition or a Sanctuary lands here, with its group and timings. They stay on this PC."
                : "No fights of this kind yet.", 12.5, FontWeights.Normal, "Faint");
            empty.TextWrapping = TextWrapping.Wrap; empty.Margin = new Thickness(0, 4, 0, 6);
            body.Children.Add(empty);
        }
        else
        {
            // a boss picked: its record on one line (fights, kills, best kill time)
            if (_parserPrefs.BossFilter.Length > 0)
            {
                var kills = shown.Where(e => e.Outcome == "kill").ToList();
                var best = kills.Count > 0 ? kills.Min(e => e.Duration(e.End!.Value)) : (TimeSpan?)null;
                var rec = PT($"{shown.Count} fight{(shown.Count == 1 ? "" : "s")} · {kills.Count} kill{(kills.Count == 1 ? "" : "s")}{(best is { } b ? " · best kill " + BossParser.Clock(b) : "")}", 12, FontWeights.SemiBold, "Text2");
                rec.Margin = new Thickness(0, 0, 0, 6);
                body.Children.Add(rec);
            }
            int take = _parserAllRows ? shown.Count : Math.Min(shown.Count, ParserRowsAtRest);
            for (int i = 0; i < take; i++) body.Children.Add(HistoryRow(shown[i], i > 0, now));
            if (shown.Count > ParserRowsAtRest)
                body.Children.Add(FoldLink(_parserAllRows ? "Fewer" : $"{shown.Count - ParserRowsAtRest} more  ›", () => { _parserAllRows = !_parserAllRows; RedrawParserPage(); }));
        }
        card.Child = body;
        return card;
    }

    void ParserFilterChanged() { _parserAllRows = false; if (!_demo) _parserPrefs.Save(); RedrawParserPage(); }

    /// <summary>One kept fight: when, the boss with its instance, the outcome, how long, the group's DPS (or its average CP); a click opens it.</summary>
    FrameworkElement HistoryRow(Encounter e, bool rule, DateTime now)
    {
        var g = new Grid();
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(86) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(92) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(54) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(78) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(16) });
        var local = e.Start.ToLocalTime();
        var today = (_demo ? _parserDemoNow : DateTime.UtcNow).ToLocalTime().Date;
        var when = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        when.Children.Add(PT(local.ToString("HH:mm", CultureInfo.InvariantCulture), 12.5, FontWeights.SemiBold, "Text2"));
        when.Children.Add(PT(local.Date == today ? "Today" : local.Date == today.AddDays(-1) ? "Yesterday" : local.ToString("MMM d", CultureInfo.InvariantCulture), 11, FontWeights.Medium, "Faint"));
        g.Children.Add(when);
        var mid = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) };
        var nameLine = new StackPanel { Orientation = Orientation.Horizontal };
        nameLine.Children.Add(PT(e.Boss, 13, FontWeights.SemiBold, "Ink", trim: true));
        if (e.Partial) nameLine.Children.Add(Margin(PT("partial", 11, FontWeights.SemiBold, "Warn"), 6, 1, 0, 0));
        mid.Children.Add(nameLine);
        mid.Children.Add(PT($"{BossBook.Strip(e.Instance)} · {KindLabel(e)}", 11.5, FontWeights.Medium, "Muted", trim: true));
        Grid.SetColumn(mid, 1); g.Children.Add(mid);
        var oc = OutcomeChip(e); oc.HorizontalAlignment = HorizontalAlignment.Left;
        Grid.SetColumn(oc, 2); g.Children.Add(oc);
        var dur = Right(PT(BossParser.Clock(e.Duration(e.End ?? now)), 13, FontWeights.SemiBold, "Ink"));
        Grid.SetColumn(dur, 3); g.Children.Add(dur);
        var fig = new StackPanel { VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Right };
        if (e.HasDamage) { fig.Children.Add(Right(PT(Amounts.Short(e.GroupDps(e.End ?? now)), 13, FontWeights.SemiBold, "Text2"))); fig.Children.Add(Right(PT("group DPS", 10.5, FontWeights.Medium, "Faint"))); }
        else { fig.Children.Add(Right(PT(AvgCp(e), 13, FontWeights.SemiBold, "Text2"))); fig.Children.Add(Right(PT($"avg CP · {e.Roster.Count} in group", 10.5, FontWeights.Medium, "Faint"))); }
        Grid.SetColumn(fig, 4); g.Children.Add(fig);
        var chev = new System.Windows.Shapes.Path { Data = Geometry.Parse("M0,0 L3.5,3.5 L0,7"), StrokeThickness = 1.4, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round };
        chev.SetResourceReference(System.Windows.Shapes.Shape.StrokeProperty, "Faint");
        Grid.SetColumn(chev, 5); g.Children.Add(chev);
        var b = new Border { Child = g, Padding = new Thickness(6, 6, 6, 6), Margin = new Thickness(-6, 0, -6, 0), CornerRadius = new CornerRadius(7), BorderThickness = new Thickness(0, rule ? 1 : 0, 0, 0), Background = Brushes.Transparent, Cursor = Cursors.Hand, Focusable = true };
        b.SetResourceReference(Border.BorderBrushProperty, "CardHair");
        AutomationProperties.SetName(b, $"{e.Boss}, {e.OutcomeLabel}, {BossParser.Clock(e.Duration(e.End ?? now))}");
        b.ToolTip = "Open this fight: the group, the timings, the kills near you";
        b.MouseEnter += (_, _) => b.SetResourceReference(Border.BackgroundProperty, "HoverTint");
        b.MouseLeave += (_, _) => b.Background = Brushes.Transparent;
        b.MouseLeftButtonUp += (_, _) => OpenFight(e.Id);
        b.KeyDown += (_, k) => { if (k.Key is Key.Enter or Key.Space) { k.Handled = true; OpenFight(e.Id); } };
        return b;
    }

    void OpenFight(string? id) { _parserOpen = id; RedrawParserPage(); LowerScroll.ScrollToTop(); }

    /// <summary>One kept fight: back to the list, its head, its figures, the timings, the group with deaths, the kills near you.</summary>
    FrameworkElement ParserDetail(Encounter e)
    {
        var end = e.End ?? e.LastAt;
        var box = new StackPanel();
        var back = FoldLink("‹  All fights", () => OpenFight(null));
        back.Margin = new Thickness(2, 0, 0, 8);
        box.Children.Add(back);
        var card = PCard(hero: false);
        var body = new StackPanel();
        body.Children.Add(FightHead(e, end, live: false));
        if (FightChips(e, end, live: false) is { } chips) body.Children.Add(Margin(chips, 0, 10, 0, 0));
        // the timings: pulled, ended, how long; the instance's copy said apart only when it matters
        var local = e.Start.ToLocalTime();
        var times = PT($"Pulled {local.ToString("ddd MMM d, HH:mm:ss", CultureInfo.InvariantCulture)}   ·   Ended {end.ToLocalTime().ToString("HH:mm:ss", CultureInfo.InvariantCulture)}   ·   {BossParser.Clock(e.Duration(end))}", 12, FontWeights.Medium, "Text2", trim: true);
        times.Margin = new Thickness(0, 8, 0, 0);
        body.Children.Add(times);
        if (e.Partial)
        {
            var why = PT("Partial: " + string.Join("; ", e.PartialWhy) + ". Still one fight; its damage and kills may be short.", 12, FontWeights.Medium, "Warn");
            why.TextWrapping = TextWrapping.Wrap; why.Margin = new Thickness(0, 4, 0, 0);
            body.Children.Add(why);
        }
        body.Children.Add(Margin(RosterTable(e, end, detail: true), 0, 12, 0, 0));
        var adds = e.Kills.Where(k => !k.Boss).GroupBy(k => k.Name, StringComparer.OrdinalIgnoreCase).OrderByDescending(g => g.Count()).ToList();
        if (adds.Count > 0)
        {
            body.Children.Add(Margin(PT("Kills near you", 11, FontWeights.SemiBold, "Faint"), 0, 12, 0, 4));
            var wp = new WrapPanel();
            foreach (var g in adds) { var c = PChip(g.Count() > 1 ? $"{g.First().Name} ×{g.Count()}" : g.First().Name, "Text2", "ChipBg"); c.Margin = new Thickness(0, 0, 6, 4); wp.Children.Add(c); }
            body.Children.Add(wp);
        }
        if (!e.HasDamage)
        {
            var nd = PT("The game's messages gave no damage for this fight, so none is shown.", 11.5, FontWeights.Medium, "Faint");
            nd.Margin = new Thickness(0, 10, 0, 0);
            body.Children.Add(nd);
        }
        card.Child = body;
        box.Children.Add(card);
        return box;
    }

    // ---- small parts ----

    static TextBlock PT(string text, double size, FontWeight weight, string brush, bool trim = false, bool display = false)
    {
        var t = QT(text, size, weight, brush, trim);
        if (display) t.SetResourceReference(TextBlock.FontFamilyProperty, "DisplayFont");
        return t;
    }

    static T Margin<T>(T el, double l, double t, double r, double b) where T : FrameworkElement { el.Margin = new Thickness(l, t, r, b); return el; }

    static TextBlock Right(TextBlock t) { t.HorizontalAlignment = HorizontalAlignment.Right; return t; }

    static Border PCard(bool hero)
    {
        var b = new Border();
        b.SetResourceReference(StyleProperty, "QCard");
        if (hero) b.SetResourceReference(Border.BackgroundProperty, "HeroBg");
        if (hero) b.Padding = new Thickness(16, 12, 16, 12);
        return b;
    }

    static Border PChip(string text, string ink, string tint, bool dot = false)
    {
        var sp = new StackPanel { Orientation = Orientation.Horizontal };
        if (dot)
        {
            var d = new Border { Width = 6, Height = 6, CornerRadius = new CornerRadius(3), Margin = new Thickness(0, 0, 5, 0), VerticalAlignment = VerticalAlignment.Center };
            d.SetResourceReference(Border.BackgroundProperty, ink);
            sp.Children.Add(d);
        }
        sp.Children.Add(QT(text, 11.5, FontWeights.SemiBold, ink));
        var b = new Border { Child = sp, CornerRadius = new CornerRadius(7), Padding = new Thickness(8, 2, 8, 3), VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Left };
        b.SetResourceReference(Border.BackgroundProperty, tint);
        return b;
    }

    /// <summary>A figure with its label in front: "Group DPS 41.2K".</summary>
    static Border PStat(string label, string value, string ink, string after = "")
    {
        var sp = new StackPanel { Orientation = Orientation.Horizontal };
        sp.Children.Add(QT(label, 11.5, FontWeights.Medium, "Muted"));
        sp.Children.Add(Margin(QT(value, 12.5, FontWeights.Bold, ink), 6, 0, 0, 0));
        if (after.Length > 0) sp.Children.Add(Margin(QT(after, 11.5, FontWeights.Medium, "Faint"), 5, 0, 0, 0));
        var b = new Border { Child = sp, CornerRadius = new CornerRadius(8), Padding = new Thickness(9, 3, 9, 4), BorderThickness = new Thickness(1) };
        b.SetResourceReference(Border.BackgroundProperty, "SegGroup");
        b.SetResourceReference(Border.BorderBrushProperty, "CardHair");
        return b;
    }

    // ---- renders and tests ----

    /// <summary>A layout render's Parser page: "live" (a fight under way), "history" (between fights), "detail" (one kept fight open), "nodamage", "empty".</summary>
    internal void ShowParserDemo(string state)
    {
        _demo = true;
        _parserDemoNow = new DateTime(2026, 11, 13, 21, 30, 0, DateTimeKind.Utc);
        var (past, live) = state == "empty" ? (new List<Encounter>(), null) : ParserDemo.Sample(_parserDemoNow, live: state is "live" or "nodamage", damage: state != "nodamage");
        _parserDemoPast = past; _parserDemoLive = live;
        _parserDemoWhere = state == "empty" ? (ContentKind.Other, "") : (ContentKind.Transcendence, "Shattered Arkanis [Stage 3]");
        _parserOpen = state == "detail" ? past.LastOrDefault(e => e.Kills.Any(k => !k.Boss) && e.Roster.Any(f => f.Deaths > 0))?.Id ?? past.LastOrDefault()?.Id : null;
        _parserPrefs.KindFilter = ""; _parserPrefs.BossFilter = "";
        ShowPart("Parser", byHand: false);
        RedrawParserPage();
    }

    /// <summary>For the options test: what the page shows (a live fight, the rows in the list, the detail open).</summary>
    internal (bool Live, int Rows, bool Detail, int Roster) ParserPageForTest()
    {
        int rows = 0; bool live = false, detail = _parserOpen != null;
        foreach (var c in ParserPage.Children.OfType<FrameworkElement>()) Walk(c);
        void Walk(DependencyObject d)
        {
            if (d is Border b && b.Cursor == Cursors.Hand && b.ToolTip is string s && s.StartsWith("Open this fight")) rows++;
            if (d is TextBlock t && t.Text == "In progress") live = true;
            foreach (var ch in LogicalTreeHelper.GetChildren(d).OfType<DependencyObject>()) Walk(ch);
        }
        int roster = (ParserLive ?? (_parserOpen != null ? ParserPast().FirstOrDefault(e => e.Id == _parserOpen) : null))?.Roster.Count ?? 0;
        return (live, rows, detail, roster);
    }

    internal void OpenFightForTest(string? id) => OpenFight(id);
    internal List<Encounter> ParserPastForTest => ParserPast();
    internal ParserOverlay? ParserOverlayForTest => _parserOverlay;
    internal void SetParserOverlayForTest(bool on) => SetParserOverlay(on);
}
