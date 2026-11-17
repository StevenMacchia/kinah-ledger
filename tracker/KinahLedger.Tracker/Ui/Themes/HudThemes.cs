using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using KinahLedger.Parser;
using KinahLedger.Session;
using static KinahLedger.Ui.HudKit;

namespace KinahLedger.Ui;

/// <summary>
/// The overlay's themes (Steven, 2026-11-16: "let us choose a theme and then let us choose from that theme"; "really go all
/// in and nail it and make it movie production level for all"). Settings › In game › Theme picks a family, and the look
/// thumbnails under it show that family's looks. Classic is every look from before themes, unchanged. A look's theme is
/// kept with it (HudLooks.Look.Theme), so hud.json still keeps only the look.
/// </summary>
public static class HudThemes
{
    public const string Classic = "classic", Wacky = "wacky", Tactical = "tactical", Glitch = "glitch";
    public const string Sticker = "sticker", Neon = "neon", Goo = "goo", Fighter = "fighter", Scorebug = "scorebug", Holo = "holo", Vhs = "vhs", Corrupt = "corrupt", Terminal = "terminal";

    public sealed record Theme(string Key, string Name, string Hint);

    public static readonly Theme[] All =
    {
        new(Classic, "Classic", "The looks the overlay has always had"),
        new(Wacky, "Wacky", "Loud, chunky and playful: stickers, neon, candy"),
        new(Tactical, "Tactical", "A pro's HUD: jet lines, a broadcast scorebug, holo glass"),
        new(Glitch, "Glitch", "A haunted tape: VHS, a broken signal, an old green screen"),
    };

    /// <summary>The theme a look belongs to; Classic for the strip, an unknown key or none.</summary>
    public static string Of(string? look) => HudLooks.All.FirstOrDefault(l => l.Key == look)?.Theme ?? Classic;

    /// <summary>A theme's looks in their picker order.</summary>
    public static IReadOnlyList<HudLooks.Look> Looks(string theme) => HudLooks.All.Where(l => l.Theme == theme).ToList();

    /// <summary>A look of one of the themes (not Classic's).</summary>
    public static bool Themed(string? look) => look != null && Of(look) != Classic;

    /// <summary>The looks drawn for fights in a theme (the Parser's look row): Classic's four, or every look of the theme.</summary>
    public static IReadOnlyList<string> FightLooks(string theme) => theme == Classic ? HudLooks.FightLooks : Looks(theme).Select(l => l.Key).ToList();

    /// <summary>A theme's look, or null for Classic's (the window and the Parser's own window make those themselves).</summary>
    internal static HudThemeLook? Make(string key, bool still) => key switch
    {
        Sticker => new HudSticker { Still = still }, Neon => new HudNeon { Still = still }, Goo => new HudGoo { Still = still },
        Fighter => new HudFighter { Still = still }, Scorebug => new HudScorebug { Still = still }, Holo => new HudHolo { Still = still },
        Vhs => new HudVhs { Still = still }, Corrupt => new HudCorrupt { Still = still }, Terminal => new HudTerminal { Still = still },
        _ => null,
    };

    // faces every Windows 10 and 11 has (no font files to ship): the looks name their own, the first found is used
    public static readonly FontFamily Heavy = new("Segoe UI Black, Segoe UI");
    public static readonly FontFamily Impact = new("Impact, Segoe UI Black, Segoe UI");
    public static readonly FontFamily Bahn = new("Bahnschrift, Segoe UI");
    public static readonly FontFamily Mono = new("Cascadia Mono, Consolas, Lucida Console");
    public static readonly FontFamily Console = new("Lucida Console, Consolas");
    public static readonly FontFamily Hand = new("Ink Free, Segoe Print, Segoe UI");
}

/// <summary>A look that holds its motion at a render's point (KL_MOTION_AT): HudWindow.MotionFrameForRender.</summary>
public interface IHudMotionFrame
{
    void MotionFrame(IReadOnlyDictionary<string, string> values);
}

/// <summary>
/// What every theme's look shares, so a look only draws: the window's values read into <see cref="HudStackLook.Figures"/>,
/// redrawn when they change; the moments a look plays its effects for (a pickup, a rare drop, your best passed, an
/// event's entry opening, a fight starting and ending), each told once; the timers due; the fight with the Parser's rows
/// (Ui/ParserRows.cs, the same rows as every look, in the look's face); the Kinah line put away in a dungeon.
///
/// The rules every look keeps (Steven's, 2026-11): a figure is never animated itself (no rolling, flipping or jitter of
/// the digits: motion goes round them); nothing in the UI says packets, traffic or "the game's messages"; it reads over
/// any game scene (dark behind every light text); the fight's rows sit on a dark ground (their names are white); nothing
/// plays in a render or the Options thumbnails (<see cref="HudStackLook.Still"/>, Motion.On), except a held frame
/// (<see cref="MotionFrame"/>).
/// </summary>
abstract class HudThemeLook : HudStackLook, IHudMotionFrame
{
    /// <summary>The fight's rows, kept between draws so a change moves (ParserRows.Detach before adding them to a new parent).</summary>
    protected readonly ParserRows Rows;

    /// <param name="bare">The rows straight over the game (lighter bars), as the Float's.</param>
    /// <param name="rowsFace">The rows' face; the window's when null.</param>
    protected HudThemeLook(bool bare = false, FontFamily? rowsFace = null)
    {
        Rows = new ParserRows(bare ? ParserRows.Kind.Bare : ParserRows.Kind.Panel);
        if (rowsFace != null) System.Windows.Documents.TextElement.SetFontFamily(Rows, rowsFace);
    }

    /// <summary>The window's values last given, and the figures read from them (null before the first).</summary>
    protected IReadOnlyDictionary<string, string> Values { get; private set; } = new Dictionary<string, string>();
    protected Figures? Figs { get; private set; }
    /// <summary>Whether this look plays its effects now: never in a render or a thumbnail, nor with Windows's animations off.</summary>
    protected bool Moves => !Still && Motion.On;

    string _lineSig = "", _pickId = "", _rareId = "", _enterId = "";
    bool _updated, _wasBeat;

    // ---- what a look draws

    /// <summary>The Kinah line (in the Abyss the points' line) from the figures, and the note under it (<see cref="Figures.Note"/>), unless <see cref="HudStackLook.KinahHidden"/>.</summary>
    protected abstract void DrawLine(Figures f);
    /// <summary>The watched timers due, soonest first (none: hide them); in a dungeon (<see cref="HudStackLook.KinahHidden"/>) inside the fight's panel.</summary>
    protected abstract void DrawTimers(IReadOnlyList<TimerBoard.Item> due, DateTime now);
    /// <summary>The fight (null: put it away): its title, clock and HP, <see cref="Rows"/> and the foot (ParserFoot.Line); a ready card while <see cref="Encounter.Waiting"/>.</summary>
    protected abstract void DrawFight(Encounter? e, DateTime now);
    /// <summary>The Kinah line went away for a dungeon, or came back: hide or show it (the line is drawn again after).</summary>
    protected abstract void KinahShown(bool shown);

    /// <summary>The figures' sign: the line is drawn again when it changes. Add the clock or the zone if the look shows them.</summary>
    protected virtual string LineSig(Figures f) => $"{f.Sig}|{f.ToBest}|{KinahHidden}";

    // ---- the moments (each told once, only while <see cref="Moves"/>, or in a held frame with held true)

    /// <summary>A pickup just counted (the newest of the window's pickups).</summary>
    protected virtual void Pickup(Pick p, bool held) { }
    /// <summary>A rare drop the window just announced (a Legend, Unique or Epic grade, or a watched item).</summary>
    protected virtual void RareDrop(Pick p, bool held) { }
    /// <summary>This hour just passed your best here.</summary>
    protected virtual void BestPassed(bool held) { }
    /// <summary>An event's entry just opened (the next timer turned green, "ENTER").</summary>
    protected virtual void EntryOpened(TimerBoard.Item i, bool held) { }
    /// <summary>A fight started (its first hit), or ended: <see cref="Encounter.Outcome"/> says how.</summary>
    protected virtual void FightStarted(Encounter e, bool held) { }
    protected virtual void FightEnded(Encounter e, bool held) { }

    // ---- the plumbing

    public sealed override void Update(IReadOnlyDictionary<string, string> values)
    {
        Values = values;
        var f = Read(values);
        Figs = f;
        var sig = LineSig(f);
        if (sig != _lineSig) { _lineSig = sig; DrawLine(f); }
        var p0 = Picks(values).FirstOrDefault();
        var rare = Rare(values);
        bool first = !_updated;
        _updated = true;
        if (!first && p0 != null && p0.Id != _pickId && Moves) Pickup(p0, false);
        _pickId = p0?.Id ?? "";
        if (!first && rare != null && rare.Id != _rareId && Moves) RareDrop(rare, false);
        _rareId = rare?.Id ?? "";
        if (!first && f.Beat && !_wasBeat && Moves) BestPassed(false);
        _wasBeat = f.Beat;
    }

    /// <summary>A render's held frame (KL_MOTION_AT): the figures drawn, then every moment they hold played at that point.</summary>
    public virtual void MotionFrame(IReadOnlyDictionary<string, string> values)
    {
        Update(values);
        if (Picks(values).FirstOrDefault() is { } p) Pickup(p, true);
        if (Rare(values) is { } r) RareDrop(r, true);
        if (Figs is { Beat: true }) BestPassed(true);
        if (_timers is { } t && t.Items.FirstOrDefault() is { } i && TimerBadge.Of(i, t.Now).Tone == TimerBadge.Tone.On) EntryOpened(i, true);
        if (_lastFight is { } e) { if (e.Live) FightStarted(e, true); else FightEnded(e, true); }
    }

    (IReadOnlyList<TimerBoard.Item> Items, DateTime Now)? _timers;
    string _timerSig = "";
    bool _timersSeen;

    public sealed override void ShowTimers(IReadOnlyList<TimerBoard.Item> items, DateTime now)
    {
        var due = items.Where(i => i.AtPc != null).ToList();
        _timers = (due, now);
        var next = due.FirstOrDefault();
        bool on = next != null && TimerBadge.Of(next, now).Tone == TimerBadge.Tone.On;
        var id = on ? next!.Key : "";
        if (on && id != _enterId && _timersSeen && Moves) EntryOpened(next!, false);
        _enterId = id;
        _timersSeen = true;
        // a badge's figures move each second: drawn again only when the words change
        var sig = string.Join("|", due.Select(i => TimerBadge.Text(i, now) + TimerBadge.Of(i, now).Tone)) + "|" + KinahHidden;
        if (sig == _timerSig) return;
        _timerSig = sig;
        DrawTimers(due, now);
    }

    string _fightSig = "", _fightId = "", _fightEnd = "";

    public sealed override void ShowFight(Encounter? e, DateTime now)
    {
        _lastFight = e; _lastNow = now;
        var sig = FightSig(e, now);
        if (sig == _fightSig) return;
        _fightSig = sig;
        DrawFight(e, now);
        if (e == null) { _fightId = ""; _fightEnd = ""; return; }
        bool started = e.Live && !e.Waiting && e.HasDamage && e.Id != _fightId;
        if (e.Live && !e.Waiting && e.HasDamage) _fightId = e.Id;
        if (started && Moves) FightStarted(e, false);
        var end = e.Live ? "" : e.Id + e.Outcome;
        if (end.Length > 0 && end != _fightEnd && e.Id == _fightId && Moves) FightEnded(e, false);   // the fight seen live, ended once
        _fightEnd = end;
    }

    public sealed override void HideKinah(bool on)
    {
        base.HideKinah(on);
        KinahShown(!on);
        _lineSig = "";
        if (Figs is { } f) { _lineSig = LineSig(f); DrawLine(f); }
        _timerSig = "";
        if (_timers is { } t) DrawTimers(t.Items, t.Now);
    }

    public override void ApplyWidth() { _fightSig = ""; ShowFight(_lastFight, _lastNow); }

    // ---- helpers a look draws with

    /// <summary>The rows shown for the fight on <paramref name="host"/>, at <paramref name="width"/>.</summary>
    protected void PutRows(Panel host, Encounter e, DateTime now, double width = double.NaN)
    {
        if (Meter is { } m)
        {
            ParserRows.Detach(Rows);
            if (m.Parent is Panel p0) p0.Children.Remove(m); else if (m.Parent is Decorator d0) d0.Child = null;
            m.Width = width;
            m.Moves = Moves;
            host.Children.Add(m);
            m.Show(e, now);
            return;
        }
        ParserRows.Detach(Rows);
        Rows.Width = width;
        host.Children.Add(Rows);
        Rows.Show(e, now);
    }

    /// <summary>
    /// The look's own meter rows (Ui/Themes/ThemeRows.cs): every member drawn in the theme's style, the same figures as the
    /// Parser's rows. Set in the look's constructor with <see cref="UseRows"/>; <see cref="PutRows"/> then shows these.
    /// </summary>
    protected ThemeRows? Meter { get; private set; }

    /// <param name="rowHeight">Each row's pitch (height and the space under it).</param>
    protected void UseRows(double rowHeight, Func<MeterMember, IThemeRow> make, double gapHeight = 12, Func<int, FrameworkElement>? makeGap = null)
        => Meter = new ThemeRows(rowHeight, make, gapHeight, makeGap) { Card = this is IThemeCard tc ? tc.DrawCard : null };   // the look's own skills card

    /// <summary>The rows shown now (the look's own, or the Parser's).</summary>
    protected int RowsShown => Meter?.Shown ?? Rows.Shown;

    /// <summary>"Killed", "Wiped", "Cleared", "Over" for a fight that ended; empty while it runs.</summary>
    protected static string OutcomeText(Encounter e) => e.Live || e.Outcome.Length == 0 ? ""
        : L.T(e.Outcome == "kill" ? "Killed" : e.Outcome == "wipe" ? "Wiped" : e.Outcome == "cleared" ? "Cleared" : "Over");

    /// <summary>The boss's HP left (0 to 1) while it is known and the fight runs; else null.</summary>
    protected static double? HpOf(Encounter e) => e.Live && e.BossHpLeft is { } hp ? hp : null;

    /// <summary>"1.82M left" (ParserOverlay.HpLeft).</summary>
    protected static string HpLeftText(Encounter e) => ParserPanel.HpLeft(e);

    /// <summary>The boss's red chunk falling onto the fill after a big drop (Motion.HpGhost) on a bar's track (a Grid).</summary>
    protected void HpGhost(Grid track, Encounter e, double hp, DateTime now, double radius = 1.5) => Motion.HpGhost(track, _hpPlace, e.Id, hp, now, radius);

    /// <summary>The next timer's words and tone (TimerBadge): "Shugo ENTER 2:41" green while the entry is open.</summary>
    protected static (string Text, TimerBadge.Tone Tone) TimerWords(TimerBoard.Item i, DateTime now) => (TimerBadge.Text(i, now), TimerBadge.Of(i, now).Tone);

    /// <summary>A course's eased point with an overshoot at its end (a pop): 0 to about 1.1 and back to 1.</summary>
    protected static double Back(double p, double s = 1.7) { p = Math.Clamp(p, 0, 1) - 1; return p * p * ((s + 1) * p + s) + 1; }
    protected static double Out(double p) => Motion.Out(p);

    /// <summary>A figure's text: tabular digits, so a figure that changes never shifts its column.</summary>
    protected static TextBlock Figure(string s, double size, Brush fg, FontFamily face, FontWeight weight)
    {
        var t = new TextBlock { Text = s, FontSize = size, Foreground = fg, FontFamily = face, FontWeight = weight, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.None };
        System.Windows.Documents.Typography.SetNumeralAlignment(t, FontNumeralAlignment.Tabular);
        return t;
    }
}
