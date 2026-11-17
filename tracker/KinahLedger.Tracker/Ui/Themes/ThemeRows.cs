using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using KinahLedger.Parser;

namespace KinahLedger.Ui;

/// <summary>
/// One member of a fight as a theme's meter shows them: the same figures the Parser's rows (Ui/ParserRows.cs) show, worked
/// out the same way, for a row the theme draws in its own style (Steven, 2026-11-16, of the first pass that reused the
/// Parser's rows: "you put the top different and copy pasted the old parser on it").
/// </summary>
/// <param name="Figure">The DPS ("4.1K"; "–" before it is known), or the Combat Power before any damage (<see cref="CpOnly"/>).</param>
/// <param name="Share">The member's share of the group's damage, 0 to 1: the bar's length (33% fills a third).</param>
/// <param name="ShareText">"32.0%"; "CP" before any damage; empty without either.</param>
/// <param name="Cp">Combat Power beside the name once there is damage ("38.2K"), else empty.</param>
/// <param name="Heal">A healer's healing a second that landed ("1.1K/s"), else empty; <see cref="HealShare"/> their share of the group's.</param>
/// <param name="Damage">The member's damage ("1.27M"), shown when the meter is expanded (<see cref="MeterContext.Expanded"/>).</param>
/// <param name="Crit">The crit rate ("34%") and back attacks, when the record has them and the meter is expanded; else empty.</param>
public sealed record MeterMember(Fighter F, int Rank, bool You, bool Pinned, string Name, string Class, Color ClassColor, Color NameColor,
    string Figure, double FigureValue, double Share, string ShareText, string Cp, int Deaths, bool TopDps, bool TopHeals,
    string Heal, double HealShare, string Damage, string Crit, string Back, bool CpOnly)
{
    public long Id => F.Id;
}

/// <summary>What a row is drawn in: the fight, its moment, whether effects play, the meter's mode and the key for its motion.</summary>
/// <param name="Key">A key unique to this meter, this fight and this member's row: for Motion.Slide and friends.</param>
public sealed record MeterContext(Encounter E, DateTime Now, bool Moves, bool Expanded, int Count, string Key);

/// <summary>A theme's row: built once per member, then set again each draw (keep its parts, change them in place).</summary>
public interface IThemeRow
{
    FrameworkElement Root { get; }
    /// <param name="fresh">The row was just built (no motion from an old value).</param>
    void Set(MeterMember m, MeterContext c, bool fresh);
}

/// <summary>
/// A theme's meter rows: the members of the fight as the Parser's rows rank and show them (a party of five at most, ten when
/// expanded; you pinned last when ranked lower), each in a row the look builds (<see cref="IThemeRow"/>). Rows are kept per
/// member between draws, so a bar can slide and a row glides to its new rank. Each row opens the member's skills card
/// (ParserRows.HoverCard) on hover, and on a rest of the pointer with clicks going through (<see cref="Peek"/>).
/// </summary>
public sealed class ThemeRows : Grid
{
    readonly Func<MeterMember, IThemeRow> _make;
    readonly double _rowH, _gapH;
    readonly Func<int, FrameworkElement>? _makeGap;
    readonly string _key = "theme-rows|" + Guid.NewGuid().ToString("N")[..8] + "|";
    sealed class Slot { public required IThemeRow Row; public required TranslateTransform Shift; public double Y = double.NaN; public int Rank; }
    readonly Dictionary<long, Slot> _rows = new();
    FrameworkElement? _gap;
    string _fightId = "";
    bool _expanded;
    Encounter? _shownE;
    DateTime _shownAt;

    /// <param name="rowHeight">Each row's pitch (its height plus the space under it).</param>
    /// <param name="make">A new row for a member.</param>
    /// <param name="gapHeight">The room for the line between the party and you pinned below it ("2 more").</param>
    /// <param name="makeGap">The line itself, given how many are left out; a faint rule by default.</param>
    public ThemeRows(double rowHeight, Func<MeterMember, IThemeRow> make, double gapHeight = 12, Func<int, FrameworkElement>? makeGap = null)
    {
        _rowH = rowHeight; _make = make; _gapH = gapHeight; _makeGap = makeGap;
        VerticalAlignment = VerticalAlignment.Top;
        TextOptions.SetTextFormattingMode(this, TextFormattingMode.Display);
    }

    /// <summary>Effects play (the look's Moves): a bar slides, a row glides. Off: everything set where it ends.</summary>
    public bool Moves { get; set; }
    /// <summary>The members shown in the last draw, top first.</summary>
    public IReadOnlyList<MeterMember> Members { get; private set; } = Array.Empty<MeterMember>();
    /// <summary>The rows shown (for a look's own layout and the tests).</summary>
    public int Shown => Members.Count;
    /// <summary>A member's row as built, for a look's moment (a kill's glint over the top row, say).</summary>
    public IThemeRow? RowOf(long id) => _rows.TryGetValue(id, out var s) ? s.Row : null;

    /// <summary>The members of a fight as the rows show them (the Parser's rules), without drawing anything.</summary>
    public static List<MeterMember> Of(Encounter e, DateTime now, bool expanded)
    {
        var all = e.Shown(now).Where(f => !f.Other).ToList();
        int max = expanded ? ParserRows.ExpandedRows : ParserRows.CompactRows;
        var visible = all.Take(max).ToList();
        var you = all.FirstOrDefault(f => f.You);
        bool pin = you != null && !visible.Contains(you);
        if (pin) visible = all.Take(max - 1).Append(you!).ToList();
        bool dmg = e.HasDamage;
        var view = expanded && dmg ? FightView.Of(e, now, null) : null;
        long topDps = e.TopDps(now)?.Id ?? long.MinValue, topHeals = e.TopHealer(now)?.Id ?? long.MinValue;
        long healAll = e.Members.Sum(m => m.HealingEffective);
        var list = new List<MeterMember>();
        foreach (var f in visible)
        {
            string figure; double value; string shareText;
            if (dmg)
            {
                var d = e.DpsOf(f, now);
                figure = d is { } x ? Amounts.Meter(x) : "–"; value = d ?? double.NaN;
                shareText = Encounter.Pct(e.Share(f));
            }
            else
            {
                figure = f.CombatPower > 0 ? f.CombatPower.ToString("N0", CultureInfo.InvariantCulture) : ""; value = f.CombatPower;
                shareText = f.CombatPower > 0 ? L.T("CP") : "";
            }
            string heal = "";
            if (dmg && f.Healing > 0) heal = e.HpsOf(f, now) is { } h ? L.F("{0}/s", Amounts.Meter(h)) : Amounts.Meter(f.HealingEffective);
            var mv = view?.Members.FirstOrDefault(m => m.F.Id == f.Id);
            list.Add(new MeterMember(f, all.IndexOf(f) + 1, f.You, pin && f.You, ParserRows.NameOf(f), f.Class, ClassArt.ColorOf(f.Class), ClassArt.NameColor(f.Class),
                figure, value, dmg ? e.Share(f) : 0, shareText, dmg && f.CombatPower > 0 ? Amounts.Meter(f.CombatPower) : "", f.Deaths,
                f.Id == topDps, f.Id == topHeals, heal, healAll > 0 && f.Healing > 0 ? Math.Clamp((double)f.HealingEffective / healAll, 0, 1) : 0,
                dmg ? Amounts.Short(e.Figure(f)) : "", mv?.Crit is { } c ? $"{c * 100:0}%" : "", mv?.Back is { } b ? $"{b * 100:0}%" : "", !dmg));
        }
        return list;
    }

    /// <summary>The fight now: each member's row set again in place and moved to their rank.</summary>
    public void Show(Encounter e, DateTime now)
    {
        _shownE = e; _shownAt = now;
        bool expanded = ParserRows.ExpandedPref;
        if (e.Id != _fightId || expanded != _expanded)
        {
            foreach (var s in _rows.Values) Children.Remove(s.Row.Root);
            _rows.Clear();
            _fightId = e.Id; _expanded = expanded;
        }
        var members = Of(e, now, expanded);
        Members = members;
        foreach (var gone in _rows.Keys.Where(id => members.All(m => m.Id != id)).ToList()) { Children.Remove(_rows[gone].Row.Root); _rows.Remove(gone); }
        bool pin = members.Any(m => m.Pinned);
        for (int i = 0; i < members.Count; i++)
        {
            var m = members[i];
            double y = i * _rowH + (m.Pinned ? _gapH : 0);
            bool fresh = !_rows.TryGetValue(m.Id, out var s);
            if (fresh)
            {
                var row = _make(m);
                s = new Slot { Row = row, Shift = new TranslateTransform() };
                row.Root.VerticalAlignment = VerticalAlignment.Top;
                row.Root.RenderTransform = s.Shift;
                Hover(row.Root, m.Id);
                _rows[m.Id] = s;
                Children.Add(row.Root);
            }
            var slot = s!;
            slot.Row.Set(m, new MeterContext(e, now, Moves, expanded, members.Count, $"{_key}{e.Id}|{m.Id}"), fresh);
            // the row's place: set at once when new, else it glides there (a rank won or lost)
            if (double.IsNaN(slot.Y) || fresh || !Moves) { slot.Y = y; slot.Row.Root.Margin = new Thickness(0, y, 0, 0); slot.Shift.Y = 0; }
            else if (Math.Abs(slot.Y - y) > 0.5)
            {
                double from = slot.Y + slot.Shift.Y - y;
                slot.Y = y; slot.Row.Root.Margin = new Thickness(0, y, 0, 0);
                var sh = slot.Shift; sh.Y = from;
                Motion.Play(slot.Row.Root, 320, p => sh.Y = from * (1 - Motion.Out(p)), () => sh.Y = 0);
            }
            slot.Rank = m.Rank;
        }
        // the line between the party and you below it
        if (_gap != null) { Children.Remove(_gap); _gap = null; }
        if (pin)
        {
            int left = Math.Max(0, e.Shown(now).Count(f => !f.Other) - members.Count);
            _gap = _makeGap?.Invoke(left) ?? new Border { Height = 1, Background = HudKit.B(0xFFFFFF, .14), VerticalAlignment = VerticalAlignment.Center };
            var host = new Grid { Height = _gapH, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, (members.Count - 1) * _rowH, 0, 0), IsHitTestVisible = false, Children = { _gap } };
            _gap = host;
            Children.Add(host);
        }
        Height = members.Count == 0 ? 0 : (members.Count - 1) * _rowH + (pin ? _gapH : 0) + _rowH;
    }

    // ---- the member's card (as the Parser's rows open it)

    void Hover(FrameworkElement root, long fid)
    {
        var card = new ToolTip { Background = Brushes.Transparent, BorderThickness = new Thickness(0), Padding = new Thickness(0), HasDropShadow = false, Placement = System.Windows.Controls.Primitives.PlacementMode.Right, HorizontalOffset = 6 };
        root.ToolTip = card;
        ToolTipService.SetInitialShowDelay(root, 350);
        ToolTipService.SetShowDuration(root, 60_000);
        root.ToolTipOpening += (_, ev) =>
        {
            if (Window.GetWindow(this) is ParserOverlay { ClickThroughNow: true }) { ev.Handled = true; return; }
            if (!Fill(card, fid)) ev.Handled = true;
        };
    }

    bool Fill(ToolTip card, long fid)
    {
        var e = _shownE;
        var f = e?.Roster.FirstOrDefault(x => x.Id == fid && !x.Other);
        if (e == null || f == null || ParserRows.HoverCard(e, f, _shownAt) is not { } content) return false;
        card.Content = content;
        return true;
    }

    long _peekId;
    DateTime _peekSince;
    ToolTip? _peekCard;

    /// <summary>With clicks going through to the game: the pointer resting on a row opens its card, moving off closes it (ParserRows.Peek's rule).</summary>
    internal void Peek(Point? at)
    {
        long id = 0;
        if (at is { } p)
            foreach (var (fid, s) in _rows)
            {
                var r = s.Row.Root;
                if (r.Visibility != Visibility.Visible || r.ActualWidth <= 0) continue;
                var tl = r.TranslatePoint(new Point(0, 0), this);
                if (p.X >= tl.X && p.X < tl.X + r.ActualWidth && p.Y >= tl.Y && p.Y < tl.Y + r.ActualHeight) { id = fid; break; }
            }
        if (id != _peekId)
        {
            if (_peekCard is { } c) { c.IsOpen = false; _peekCard = null; }
            _peekId = id; _peekSince = DateTime.UtcNow;
            return;
        }
        if (id == 0 || _peekCard != null || (DateTime.UtcNow - _peekSince).TotalMilliseconds < ParserRows.PeekMs) return;
        if (_rows.TryGetValue(id, out var row) && row.Row.Root.ToolTip is ToolTip card && Fill(card, id))
        {
            card.PlacementTarget = row.Row.Root;
            if (!ParserRows.PeekNoPopupForTest) card.IsOpen = true;
            _peekCard = card;
        }
    }
}
