using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using KinahLedger.Parser;

namespace KinahLedger.Ui;

/// <summary>
/// The fight's rows over the game, in every overlay look (Parser v2, the Arena rows Steven picked, 2026-11-15): an angled rank
/// chip in the member's class colour, the class's emblem, the name, the DPS large and the share, on a thick bar in the class's
/// colour; your row in the gold frame. Kept between draws, so a change moves rather than redraws: the DPS rolls, the bar slides,
/// a member who passes another glides to the new place and their chip flips. Compact shows five rows and keeps you in sight
/// (pinned, with your true rank, when you're below them); expanded shows everyone and adds the damage, and crit and the DPS over
/// time once the record carries them. A death puts a coral tick on the row; a kill where you beat your best on the boss puts
/// "PB +x%" on your row with a gold burst.
/// The meter's motion (Steven, 2026-11-15: "the absolute next level"), every course short and off when the tracker sits still
/// (Motion.Still: renders and tests show the settled rows): the DPS's digits roll on their own (Ui/Motion.Meter.cs Odometer)
/// in a column that never moves; a bar that moves leaves its ghost; a big hit pops "+858K" off its row (gold for a crit);
/// the one who passes another lifts as it slides past and its chip glows; the fight's end sweeps a light across, its TOP DPS
/// plate slides in with a glint and a personal best flares your figure gold; your row's frame glows warmer as your pace
/// nears your best here, gold once you beat it.
/// The heal line (Steven, 2026-11-15: "a very tiny green line with a tiny number under classes that have heals"): a member who
/// healed another (a Cleric, a Chanter; everyone's own self-heals never count for it) gets a thin green line along the row's
/// foot, its length their share of the group's healing, and under the name what they healed, per second over the DPS's
/// seconds, and the part that landed (≈ when the target's HP didn't confirm it). The row keeps its height and its columns.
/// </summary>
public sealed partial class ParserRows : Grid
{
    /// <summary>Panel: on a look's panel. Bare: straight over the game (the Float look), the bars lighter.</summary>
    public enum Kind { Panel, Bare }

    /// <summary>Compact (five rows) or expanded (everyone, more columns): one choice for every look, kept in parser.json.</summary>
    public static bool ExpandedPref { get; set; }
    /// <summary>The heal bar along a healer's row (Settings › Parser › Heal bars; on unless turned off).</summary>
    public static bool HealBarsPref { get; set; } = true;
    /// <summary>Your best kill of a boss before this pull (the window gives it from the records kept): the PB moment's measure.</summary>
    public static Func<Encounter, PullRef?>? BestOf { get; set; }

    // compact shows a whole party of five (Steven, 2026-11-15: "how come only 4 party members appeared in my last ... dungeon
    // and not 5"), expanded a whole raid of ten
    public const int CompactRows = 5, ExpandedRows = 10;
    readonly Kind _kind;
    readonly double _rowH;
    readonly Grid _host = new() { VerticalAlignment = VerticalAlignment.Top };
    readonly Border _gap = new() { Height = 10, VerticalAlignment = VerticalAlignment.Top, Visibility = Visibility.Collapsed };
    readonly TextBlock _gapText = new() { FontSize = 9.5, FontWeight = FontWeights.SemiBold, Foreground = HudKit.Faint, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Padding = new Thickness(6, 0, 6, 0) };
    readonly Border _sweep = new() { IsHitTestVisible = false, Visibility = Visibility.Collapsed, CornerRadius = new CornerRadius(6) };
    readonly Dictionary<long, RowEl> _rows = new();
    /// <summary>The big hits' pop-ups over the rows (never taking a click); at most <see cref="PopsPerRow"/> a row, <see cref="PopsInAll"/> in all.</summary>
    readonly Canvas _pops = new() { IsHitTestVisible = false };
    public const int PopsPerRow = 2, PopsInAll = 4;
    public const double PopMs = 700;
    /// <summary>A pop-up: its band (the row's open bar, clipped to the row), its words, where they sit across the row.</summary>
    sealed class PopEl { public required Canvas Band; public required TextBlock Text; public required TranslateTransform Rise; public double Left, Right; public int Gen; }
    long _popSeq;
    int _popsOn, _popGen;
    string _fightId = "";
    bool _wasLive, _expanded, _sawLive;
    string _pbShown = "";
    /// <summary>The fight whose plates slid in already (rows built again for it, expanded or a row back in sight, show them still).</summary>
    string _platesSlidFor = "";
    /// <summary>Your best on this fight's boss (asked once a fight: the heat's measure).</summary>
    string _bestId = "\0";
    PullRef? _best;

    public ParserRows(Kind kind = Kind.Panel, Skin skin = Skin.Classic)
    {
        _kind = kind;
        _skin = skin;
        _rowH = PitchOf(skin);
        var line = new Grid();
        line.Children.Add(new Border { Height = 1, Background = HudKit.B(0xFFFFFF, 0.1), VerticalAlignment = VerticalAlignment.Center });
        line.Children.Add(new Border { Background = Brushes.Transparent, HorizontalAlignment = HorizontalAlignment.Center, Child = _gapText });
        _gap.Child = line;
        Children.Add(_host);
        Children.Add(_sweep);
        Children.Add(_pops);
        Children.Add(_fx);   // the finale's and the fire's particles, over everything, clipped to the rows (Ui/ParserRows.Cinema.cs)
        _host.Children.Add(_gap);
        TextOptions.SetTextFormattingMode(this, TextFormattingMode.Display);
        System.Windows.Documents.TextElement.SetFontFamily(this, HudKit.Text);
        // the expanded columns that fit are picked by the rows' width: laid out at another width, the rows are built again for it
        SizeChanged += (_, ev) => { if (ev.WidthChanged && _shownE is { } se && ColsKey(ExpandedPref, se, _shownAt) != _colsKey) Show(se, _shownAt); };
    }

    // ---- the columns (Steven, 2026-11-17: "a lot of numbers in all the meters you can't see, it's cut off!"; then "figure out
    // better looks that doesn't crowd everything and prioritizes the bar"): each figure's column is as wide as its widest real
    // form in its own font, and the expanded columns come in only while the name keeps its room; the rest is on the hover card

    /// <summary>The expanded columns built now (damage, crit, back, the DPS over time), as a key: rows built for another set are built again.</summary>
    string _colsKey = "";
    /// <summary>The least room the name's line keeps beside the expanded columns: a ten-letter name, its Combat Power and a death mark.</summary>
    public const double NameRoom = 150;
    const double ChipCol = 28, IconCol = 24, RateCol = 44, SparkCol = 66, ColsMargin = 11, NameLineMargin = 6;

    static readonly Dictionary<string, double> Widths = new();
    /// <summary>The widest of <paramref name="forms"/> in the meter's face at this size and weight, tabular digits (kept per language).</summary>
    static double Widest(double size, FontWeight w, params string[] forms)
    {
        string key = $"{L.Code}|{size}|{w}|{string.Join("|", forms)}";
        if (Widths.TryGetValue(key, out var had)) return had;
        double max = 0;
        foreach (var s in forms)
        {
            var t = new TextBlock { Text = s, FontSize = size, FontWeight = w, FontFamily = HudKit.Text };
            System.Windows.Documents.Typography.SetNumeralAlignment(t, FontNumeralAlignment.Tabular);
            TextOptions.SetTextFormattingMode(t, TextFormattingMode.Display);
            t.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            max = Math.Max(max, t.DesiredSize.Width);
        }
        return Widths[key] = Math.Ceiling(max);
    }
    /// <summary>The DPS's column: "888.8K" at 16 bold (the party's Combat Power before a hit, "888,888", may reach into the name's room).</summary>
    static double DpsCol => Math.Max(60, Widest(16, FontWeights.Bold, "888.8K", "8.88M") + 4);
    /// <summary>The share's column: "88.8%" (or "CP" before a hit) at 12, and its gap from the DPS.</summary>
    static double ShareCol => Math.Max(46, Widest(12, FontWeights.SemiBold, "88.8%", L.T("CP")) + 8);
    /// <summary>The damage's column, expanded: "888.88M" at 11.</summary>
    static double DamageCol => Math.Max(52, Widest(11, FontWeights.SemiBold, "888.88M") + 8);

    /// <summary>The rows' width now (before they are laid out, the meter's width as set).</summary>
    double RowsWidth => ActualWidth > 1 ? ActualWidth : HudStackLook.FightWidth - 4;

    /// <summary>The expanded columns that fit beside the name's room, in their order: the damage, crit, back attacks, the DPS over time.</summary>
    (bool Damage, bool Crit, bool Back, bool Spark) FitCols(bool expanded, bool crit, bool back, bool spark)
    {
        if (!expanded) return (false, false, false, false);
        double room = RowsWidth - ColsMargin - ChipCol - IconCol - DpsCol - ShareCol - NameLineMargin;
        bool Take(bool want, double w) { if (!want || room - w < NameRoom) return false; room -= w; return true; }
        bool d = Take(true, DamageCol);
        return (d, Take(crit, RateCol), Take(back, RateCol), Take(spark, SparkCol));
    }

    string ColsKey(bool expanded, Encounter e, DateTime now)
    {
        var view = expanded ? FightView.Of(e, now, null) : null;
        var c = FitCols(expanded, view?.HasCrit == true, view?.HasBack == true, view?.HasTimeline == true);
        return $"{c.Damage}{c.Crit}{c.Back}{c.Spark}";
    }

    /// <summary>Takes the rows off the panel they were on (a look that builds its panel again each draw).</summary>
    public static void Detach(ParserRows r) { if (r.Parent is Panel p) p.Children.Remove(r); else if (r.Parent is Decorator d) d.Child = null; }

    /// <summary>The rows the look shows now (for its height and the tests).</summary>
    public int Shown => _rows.Values.Count(r => r.Root.Visibility == Visibility.Visible);
    public bool Pinned => _gap.Visibility == Visibility.Visible;

    sealed class RowEl
    {
        public required Grid Root, Cols; public required Border Chip, BarFill, Track, Edge, Tick, Pb; public required TextBlock ChipText, Name, Dps, Share, Damage, Crit, Back; public required Grid Spark;
        /// <summary>The member's Combat Power after the name, short ("150.8K"; Steven, 2026-11-16), the first thing to give way in a tight row.</summary>
        public required TextBlock Cp;
        public required Border Frame;
        /// <summary>The name's line (tick, name, YOU, PB, the plates) and the plates' place at its end (TOP DPS, TOP HEALS).</summary>
        public required StackPanel NameLine, Plates; public required Border NameHost;
        public string PlatesShown = "";
        public int Rank = -1, Deaths; public double Y = double.NaN; public bool You; public string Cls = "";
        /// <summary>Podium's leader card (Ui/ParserRows.Skins.cs): built lit; a row that gains or loses it is built again.</summary>
        public bool Hero;
        /// <summary>A skin's hot edge at the bar's head (Forge): shown only where it ends clear of the DPS.</summary>
        public FrameworkElement? HotEdge;
        /// <summary>A skin's ink for its rank (Podium's gold, silver and bronze): run each time the rank's number is set.</summary>
        public Action? RankInk;
        /// <summary>The DPS as it rolls (Motion on), over the figure itself (the truth, laid out, read by the tests).</summary>
        public required Odometer Odo;
        public double LastDps = double.NaN;
        /// <summary>The row's moves: its glide and lift (one transform), the ghost of its bar, its heat edge.</summary>
        public required TranslateTransform Lift; public required ScaleTransform Rise;
        public required Border Ghost, Heat;
        public double GhostBase = double.NaN;
        /// <summary>
        /// The row's foot: the heal line, 2 px along its bottom edge, its length the member's share of the group's healing
        /// (collapsed for a member who healed no one else); and the heal figures under the name, the name's line lifted for them.
        /// </summary>
        public required Border Under, UnderTrack;
        public required TextBlock Heal;
        /// <summary>The heal chip after the name (a healer's row): a green cross and their healing a second.</summary>
        public required Border HealPill;
        public required FrameworkElement HealCross;
        public string HealFull = "", HealShort = "", HealDone = "";
        public double HealShare = double.NaN, HealShown = double.NaN;
        /// <summary>
        /// The effects a course changes, kept with the row so a course replaced part-way (a move again, a flip back) starts
        /// from the row's own look, never from where the last one left it: the lift's shadow, the chip's own glow (your gold one;
        /// none for the others) and the glow a gain puts on it, and your frame's glow as built.
        /// </summary>
        public DropShadowEffect? LiftShadow, ChipOwn, ChipGlow, FrameGlow;
        public double ChipBlur0, ChipOpacity0, FrameBlur0, FrameOpacity0;
        /// <summary>The row's pop-ups on screen, oldest first (at most <see cref="ParserRows.PopsPerRow"/>).</summary>
        public readonly List<PopEl> Live = new();
    }

    /// <summary>The fight now: each member's row updated in place, moved to their rank.</summary>
    public void Show(Encounter e, DateTime now)
    {
        _shownE = e; _shownAt = now;
        // an open hover card follows the fight, once a second (drawn again settled: its entrance played when it opened)
        if (_openCard is { IsOpen: true } oc && (now - _cardAt).TotalSeconds >= 1 && e.Roster.FirstOrDefault(x => x.Id == _openCardId && !x.Other) is { } om)
        {
            _cardAt = now;
            if (HoverCard(e, om, now) is { } fresh) oc.Content = fresh;
        }
        bool expanded = ExpandedPref;
        // the view of what the record carries: crit and the timeline columns only when it does, and only those that fit
        var view = expanded ? FightView.Of(e, now, null) : null;
        var (dmgCol, crit, back, spark) = FitCols(expanded, view?.HasCrit == true, view?.HasBack == true, view?.HasTimeline == true);
        string colsKey = $"{dmgCol}{crit}{back}{spark}";
        if (e.Id != _fightId || expanded != _expanded || colsKey != _colsKey)
        {
            _colsKey = colsKey;
            foreach (var r in _rows.Values) _host.Children.Remove(r.Root);
            _rows.Clear();
            _pops.Children.Clear(); _popsOn = 0; _popGen++;   // the rows go too, and their pop-ups with them
            _fx.Children.Clear(); _embers = 0; _flames.Clear();   // and the embers, sparks and flames
            // a fight drawn first here: its big hits so far are history, never popped
            if (e.Id != _fightId) { _pbShown = ""; _wasLive = e.Live; _sawLive = e.Live; _popSeq = e.BigHits?.LastOrDefault()?.Seq ?? 0; }
            _fightId = e.Id; _expanded = expanded;
        }
        if (e.Live) _sawLive = true;
        bool dmg = e.HasDamage;
        _medals = dmg;
        var all = e.Shown(now).Where(f => !f.Other).ToList();
        int max = expanded ? ExpandedRows : CompactRows;
        var visible = all.Take(max).ToList();
        var you = all.FirstOrDefault(f => f.You);
        bool pin = you != null && !visible.Contains(you);
        if (pin) visible = all.Take(max - 1).Append(you!).ToList();
        double top = Math.Max(1, all.Select(e.Measure).DefaultIfEmpty(1).Max());   // the figure (boss damage or all targets), else Combat Power
        double topShare = Math.Max(0.0001, all.Select(e.Share).DefaultIfEmpty(1).Max());   // the bars' measure: the top member's share
        long topDps = e.TopDps(now)?.Id ?? long.MinValue, topHeals = e.TopHealer(now)?.Id ?? long.MinValue;
        foreach (var gone in _rows.Keys.Where(id => !visible.Any(f => f.Id == id)).ToList()) { _host.Children.Remove(_rows[gone].Root); _rows.Remove(gone); }
        bool platesSlid = false;
        for (int i = 0; i < visible.Count; i++)
        {
            var f = visible[i];
            int rank = all.IndexOf(f) + 1;
            bool pinnedRow = pin && f.You;
            bool hero = HeroAt(e, i);
            double y = i * _rowH + (pinnedRow ? _gap.Height : 0);
            bool fresh = !_rows.TryGetValue(f.Id, out var r);
            RowEl? was = null;
            if (!fresh && r!.Hero != hero) { was = r; _host.Children.Remove(r.Root); _rows.Remove(f.Id); fresh = true; }   // the leader's card came or went
            if (fresh) { r = Build(f, expanded, dmgCol, crit, back, spark, hero); _rows[f.Id] = r; _host.Children.Add(r.Root); }
            // built again for the card (as tall as any row): it starts where the old one stood, with its rank, so it glides and flips there
            if (was != null) { r!.Y = was.Y; r.Root.Margin = was.Root.Margin; r.Rank = was.Rank; r.ChipText.Text = was.ChipText.Text; }
            var row = r!;
            // the figures: the DPS rolls to its new value, the share and the damage follow
            if (e.PvpOf(f) is { } pk)
            {
                // the PvP panel (Parser/PvpKills.cs): your damage on that player, Kill or Assist, who landed an assist's kill
                SetDps(row, Encounter.PvpFigure(pk), pk.Damage);
                row.Share.Text = Encounter.PvpWord(pk);
                row.Damage.Text = "";
            }
            else if (dmg)
            {
                // the DPS, plain, in its right-aligned column
                if (e.DpsOf(f, now) is { } dps) SetDps(row, Amounts.Meter(dps), dps);
                else SetDps(row, "–", double.NaN);
                row.Share.Text = Encounter.Pct(e.Share(f));
                row.Damage.Text = Amounts.Short(e.Figure(f));
                if (view?.Members.FirstOrDefault(m => m.F.Id == f.Id) is { } mv)
                {
                    row.Crit.Text = mv.Crit is { } c ? $"{c * 100:0}%" : "–";
                    row.Back.Text = mv.Back is { } bk ? $"{bk * 100:0}%" : "–";
                    if (spark && mv.PerSecond is { Count: > 1 } ps) { row.Spark.Children.Clear(); row.Spark.Children.Add(PK.Spark(PK.Smooth(ps.Select(x => (double)x).ToList()), 60, 18, f.You ? PK.Gold : ClassArt.ColorOf(f.Class), 0, f.You ? 1.6 : 1.2, glow: f.You)); }
                }
            }
            else { SetDps(row, f.CombatPower > 0 ? f.CombatPower.ToString("N0", CultureInfo.InvariantCulture) : "", f.CombatPower); row.Share.Text = f.CombatPower > 0 ? L.T("CP") : ""; }
            // Combat Power beside the name once there is damage (before it, the figure's column says it whole)
            string cpText = e.PvpOf(f) is { } pb ? Encounter.PvpBy(pb) : dmg && f.CombatPower > 0 ? Amounts.Meter(f.CombatPower) : "";
            if (row.Cp.Text != cpText) { row.Cp.Text = cpText; row.Cp.ToolTip = f.CombatPower > 0 ? L.F("Combat Power {0:N0}", f.CombatPower) : null; FitName(row); }
            // the bar slides to its new length: the member's share of the group's damage, in proportion to the top member's,
            // whose bar reaches BarTop of the row (Steven, 2026-11-15: "the bars shouldn't be full really and should be percentage
            // based"; 2026-11-17: "prioritizes the bar": a party of six at 15-20% each drew bars no longer than the rank chip)
            // (the panel's own looks: the top member's bar its row's whole length)
            // (2026-11-18 to 1.0.29 every bar its own share; 2026-11-20 against the top member again, the top bar full: Encounter.BarOf)
            double share = dmg ? e.BarOf(f) : 0;
            Motion.Slide(row.BarFill, $"parser|ovbar|{e.Id}|{f.Id}|{(int)_kind}", share, v => SetBar(row, v));
            // a move of the bar of a couple of points leaves its ghost: what it lost holds and shrinks away, what it gained flashes
            if (double.IsNaN(row.GhostBase) || fresh) row.GhostBase = share;
            else if (Math.Abs(share - row.GhostBase) >= GhostStep) { GhostTrail(row, row.GhostBase, share); row.GhostBase = share; }
            // the rank: a changed one flips the chip (and glows, for the one who passed); the row glides to its new place
            int rankWas = row.Rank;
            if (row.Rank != rank)
            {
                row.Rank = rank;
                if (rankWas > 0 && (!fresh || was != null)) Flip(row, rankWas, rank); else row.ChipText.Text = rank.ToString(CultureInfo.InvariantCulture);
            }
            row.RankInk?.Invoke();
            if (double.IsNaN(row.Y) || (fresh && was == null)) { row.Y = y; row.Root.Margin = new Thickness(0, y, 0, 0); }
            else if (Math.Abs(row.Y - y) > 0.5) Glide(row, y, passed: rankWas > 0 && rank < rankWas);
            foreach (var lp in row.Live) Canvas.SetTop(lp.Band, row.Y);   // its pop-ups go with a row that moved
            // your heat: your pace against your best on this boss, warmer as it nears it, gold once you beat it
            if (f.You)
            {
                bool wasBeat = Equals(row.Heat.Tag, "heat-beat");
                SetHeat(row, Heat(e, f, now));
                bool beat = Equals(row.Heat.Tag, "heat-beat");
                // on fire while you beat your best here: it ignites the first draw it's true (seen live), then burns (Cinema)
                Fire(row, beat, ignite: beat && !wasBeat && !fresh && e.Live);
            }
            // a death: the coral tick lands on the row
            if (f.Deaths > row.Deaths)
            {
                bool landed = !fresh;
                row.Deaths = f.Deaths;
                row.Tick.Visibility = Visibility.Visible;
                ((TextBlock)row.Tick.Child).Text = f.Deaths > 1 ? $"✕{f.Deaths}" : "✕";
                if (view?.Members.FirstOrDefault(m => m.F.Id == f.Id) is { DeathLog.Count: > 0 } dm) row.Tick.ToolTip = Recap(dm);
                if (landed) Land(row);
            }
            // the end of a boss fight: TOP DPS (gold) and TOP HEALS (green) on their rows, in the name's room
            string plates = (f.Id == topDps ? "d" : "") + (f.Id == topHeals ? "h" : "");
            if (plates != row.PlatesShown)
            {
                row.PlatesShown = plates;
                row.Plates.Children.Clear();
                // the fight seen ending here: the plate slides in with a glint (drawn first already over, it just sits there)
                bool slide = _sawLive && _platesSlidFor != e.Id;
                if (plates.Contains('d')) { var p = ParserFoot.Plate(heals: false); row.Plates.Children.Add(p); if (slide) SlideIn(p); }
                // the top healer's plate is their heal chip's tab (TOP HEALS beside the figure): one chip, the figure never lost to a plate;
                // the panel's own looks, whose heal chip sits under the name, put a TOP HEALS plate by the name as TOP DPS
                if (_skin == Skin.Classic)
                {
                    SetTopHeals(row, plates.Contains('h'));
                    if (plates.Contains('h') && slide) SlideIn(row.HealPill);
                }
                else if (plates.Contains('h')) { var p = ParserFoot.Plate(heals: true); row.Plates.Children.Add(p); if (slide) SlideIn(p); }
                if (plates.Length > 0) platesSlid = true;
                row.Plates.Visibility = plates.Contains('d') || (_skin != Skin.Classic && plates.Contains('h')) ? Visibility.Visible : Visibility.Collapsed;
                FitName(row);
            }
            // the heal line: for anyone who healed, their own heals from skills too; potions never (they are in no heal figure)
            // (Steven, 2026-11-15: "self heals should count, potions should not")
            SetHeal(row, e, f, now, dmg && f.Healing > 0);
        }
        if (platesSlid) _platesSlidFor = e.Id;
        // the big hits that came since the last draw pop off their members' rows
        if (e.BigHits is { Count: > 0 } hits && hits[^1].Seq > _popSeq)
        {
            foreach (var h in hits)
                if (h.Seq > _popSeq && e.Live && Math.Abs((now - h.At).TotalSeconds) <= PopFreshSeconds && _rows.TryGetValue(h.Member, out var pr) && pr.Root.Visibility == Visibility.Visible)
                    Pop(pr, h);
            _popSeq = hits[^1].Seq;
        }
        _gap.Visibility = pin ? Visibility.Visible : Visibility.Collapsed;
        if (pin) { _gap.Margin = new Thickness(10, (visible.Count - 1) * _rowH, 10, 0); _gapText.Text = L.F("{0} more", all.Count - visible.Count); _gapText.Background = _kind == Kind.Bare ? Brushes.Transparent : HudKit.B(0x111319); }
        _host.Height = visible.Count * _rowH + (pin ? _gap.Height : 0) - (_skin == Skin.Classic || visible.Count == 0 ? 0 : _rowH - RowHOf(_skin, false));
        // the moments: the fight's end (a kill or a wipe, the open world's fight held) sweeps a light across; a personal best
        if (_wasLive && !e.Live && dmg) Finale(e.Tone);   // the blade, the punch, the shine down the bars (Ui/ParserRows.Cinema.cs)
        // a record on this fight (this boss, this instance, this mode: FightRecords), DPS or healing, on a kill only
        if (!e.Live && e.Outcome == "kill" && you != null && _pbShown != e.Id && Best(e) is { } best && (best.Dps > 0 || best.Hps > 0))
        {
            var end = e.End ?? now;
            double? d = best.Dps > 0 && e.Dps(you, end) / best.Dps - 1 is > 0 and var dd ? dd : null;
            double? h = best.Hps > 0 && e.HpsOf(you, end) is { } hps && hps / best.Hps - 1 is > 0 and var hh ? hh : null;
            if ((d != null || h != null) && _rows.TryGetValue(you.Id, out var yr)) { _pbShown = e.Id; ShowPb(yr, d, h); }
        }
        _wasLive = e.Live;
    }

    /// <summary>
    /// The DPS shown: the figure itself, plain, drawn again as the meter's clock runs (Steven, 2026-11-15, of the rolling
    /// digits of 0.10.116: "the odometer is terrible get rid of it"). The odometer stays collapsed.
    /// </summary>
    static void SetDps(RowEl row, string text, double value)
    {
        row.Dps.Text = text;
        row.Odo.Visibility = Visibility.Collapsed;
        row.Dps.Opacity = 1;
        if (row.Dps.Effect == null) row.Dps.Effect = TextHalo();
        row.LastDps = value;
    }

    /// <summary>Your best on this fight's boss, asked once a fight (the records kept don't change while it runs).</summary>
    PullRef? Best(Encounter e)
    {
        if (_bestId == e.Id) return _best;
        _bestId = e.Id;
        try { _best = BestOf?.Invoke(e); } catch (Exception) { _best = null; }
        return _best;
    }

    /// <summary>Your pace against your best here: from 0.8 of it the edge warms, 1 and over is gold; null without a best or a figure.</summary>
    double? Heat(Encounter e, Fighter you, DateTime now)
    {
        if (!e.Live || e.Waiting || !e.HasDamage || e.Duration(now).TotalSeconds < HeatAfterSeconds) return null;
        if (Best(e) is not { Dps: > 0 } best || e.DpsOf(you, now) is not { } dps) return null;
        return dps / best.Dps;
    }
    public const double HeatFrom = 0.8, HeatAfterSeconds = 8;
    /// <summary>The top member's bar reaches this much of the row (never full: the others' bars in proportion to it).</summary>
    public const double BarTop = 0.92;
    /// <summary>A share move this big (of the group's damage) leaves the bar's ghost.</summary>
    public const double GhostStep = 0.02;
    /// <summary>A big hit pops only while it is this fresh (a meter shown again later shows none of the ones it missed).</summary>
    public const double PopFreshSeconds = 2.5;

    /// <summary>A soft dark halo for a row's text: it reads over the brightest class bar or your gold wash.</summary>
    // a tight dark edge since 0.10.131 (Steven, 2026-11-16: "idk if the numbers need small outline ... its hard to read"):
    // a small blur at full strength reads as an outline round each letter, not a soft glow
    static DropShadowEffect TextHalo() => new() { Color = Colors.Black, BlurRadius = 2.5, ShadowDepth = 0, Opacity = 1 };

    /// <summary>A second tight edge, on a figure's host: with the figure's own, a solid outline round the digits.</summary>
    static DropShadowEffect Outline() => new() { Color = Colors.Black, BlurRadius = 2, ShadowDepth = 0, Opacity = 1 };

    /// <summary>
    /// Your character's name as the tracker knows it (Characters, set by the window); null in renders and tests.
    /// </summary>
    public static Func<string>? YourName;

    /// <summary>
    /// A member's name as the meter says it: the game's name; for your row with none (alone in the open world no group list
    /// comes and the hits don't carry it), your character's name, else "You"; anyone else with none, "Unnamed" (Steven,
    /// 2026-11-15: "why is my character called unnamed now in the dps parser"). Read at draw time, never written into the record.
    /// </summary>
    public static string NameOf(Fighter f)
    {
        if (f.Name.Length > 0) return f.Name;
        if (!f.You) return L.T("Unnamed");
        string n = "";
        try { n = YourName?.Invoke()?.Trim() ?? ""; } catch (Exception) { }
        return n.Length > 0 ? n : L.T("You");
    }

    /// <summary>The fight last drawn, and when: what a row's hover card reads.</summary>
    Encounter? _shownE;
    DateTime _shownAt;
    /// <summary>The hover card open now, whose it is, and when it was last drawn (it follows the fight once a second).</summary>
    ToolTip? _openCard;
    long _openCardId;
    DateTime _cardAt;
    /// <summary>The abilities a hover card lists at most (the rest summed in one line).</summary>
    public const int CardAbilities = 6;
    const string CardBarTag = "card-bar";

    /// <summary>
    /// The card's entrance: it slides in from the meter's side and fades up (160 ms), and its ability bars grow from nothing one
    /// after another (40 ms apart, 260 ms each, easing out). Never in a render or a test (Motion.Still): the card is drawn settled.
    /// </summary>
    /// <summary>A member's card filled from the fight as it stands, and kept as the one open (it follows the fight); false with nothing to show.</summary>
    bool FillCard(ToolTip card, long fid)
    {
        var fight = _shownE;
        var fm = fight?.Roster.FirstOrDefault(x => x.Id == fid && !x.Other);
        if (fight == null || fm == null || HoverCard(fight, fm, _shownAt) is not { } content) return false;
        card.Content = content;
        _openCard = card; _openCardId = fid; _cardAt = _shownAt;
        CardIn(content);
        return true;
    }

    // ---- the peek: the card with clicks going through to the game (Steven, 2026-11-16: "I STILL DK WHERE THE OVERLAY FOR
    // INDIVIDUAL ABILITIES IS"; with click-through on, the hover never reached the meter, so the card never opened)

    /// <summary>The pointer rests on a row this long before its card opens (a pass over the meter opens nothing).</summary>
    public const int PeekMs = 450;
    long _peekId;
    DateTime _peekSince;
    bool _peeking;

    /// <summary>
    /// With clicks going through to the game, the overlay's pointer watch says where the pointer is over the rows (null: off
    /// them). Resting on a member's row opens their card beside the meter; moving off it closes it. Nothing takes the pointer:
    /// the card only shows, and every click still goes to the game.
    /// </summary>
    internal void Peek(Point? at)
    {
        long id = 0;
        if (at is { } p)
            foreach (var (fid, r) in _rows)
            {
                if (r.Root.Visibility != Visibility.Visible || r.Root.ActualWidth <= 0) continue;
                var tl = r.Root.TranslatePoint(new Point(0, 0), this);
                if (p.X >= tl.X && p.X < tl.X + r.Root.ActualWidth && p.Y >= tl.Y && p.Y < tl.Y + r.Root.ActualHeight) { id = fid; break; }
            }
        if (id != _peekId)
        {
            if (_peeking) { _peeking = false; if (_openCard is { } c) c.IsOpen = false; }
            _peekId = id; _peekSince = DateTime.UtcNow;
            return;
        }
        if (id == 0 || _peeking || (DateTime.UtcNow - _peekSince).TotalMilliseconds < PeekMs) return;
        if (_rows.TryGetValue(id, out var row) && row.Root.ToolTip is ToolTip card && FillCard(card, id))
        {
            card.PlacementTarget = row.Root;
            if (!PeekNoPopupForTest) card.IsOpen = true;   // a popup is kept on the screen by Windows: a test never opens one
            _peeking = true;
        }
    }

    /// <summary>For the tests: the member whose card the peek has open (0: none).</summary>
    internal long PeekingForTest => _peeking && (PeekNoPopupForTest || _openCard is { IsOpen: true }) ? _peekId : 0;
    /// <summary>Tests: the peek decides and fills the card, but never opens its popup (it would show on the player's screen).</summary>
    internal static bool PeekNoPopupForTest;
    /// <summary>For the tests: the card the peek filled (its content), null before.</summary>
    internal object? PeekCardForTest => _openCard?.Content;

    /// <summary>For the tests: where a member's row sits in the rows (its middle), to rest a pointer on.</summary>
    internal Point? RowMiddleForTest(long fid) => _rows.TryGetValue(fid, out var r) && r.Root.ActualWidth > 0
        ? r.Root.TranslatePoint(new Point(r.Root.ActualWidth / 2, r.Root.ActualHeight / 2), this) : null;

    static void CardIn(FrameworkElement card)
    {
        if (Motion.Still) return;
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        var slide = new TranslateTransform(-8, 0);
        card.RenderTransform = slide;
        card.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(160)) { EasingFunction = ease });
        slide.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(-8, 0, TimeSpan.FromMilliseconds(160)) { EasingFunction = ease });
        int i = 0;
        foreach (var bar in Descendants(card).OfType<Border>().Where(b => CardBarTag.Equals(b.Tag)))
        {
            double to = bar.Width;
            bar.BeginAnimation(WidthProperty, new DoubleAnimation(3, to, TimeSpan.FromMilliseconds(260)) { BeginTime = TimeSpan.FromMilliseconds(60 + 40 * i++), EasingFunction = ease, FillBehavior = FillBehavior.HoldEnd });
        }
    }

    /// <summary>The card's elements by its logical tree (it opens before it is laid out, so the visual tree is empty then).</summary>
    static IEnumerable<DependencyObject> Descendants(DependencyObject d)
    {
        foreach (var ch in LogicalTreeHelper.GetChildren(d).OfType<DependencyObject>()) { yield return ch; foreach (var x in Descendants(ch)) yield return x; }
    }

    /// <summary>A rate column's width on the hover card (crit, back), and the tags its cells carry for the tests.</summary>
    const double CardRateCol = 38;
    static string Pct0(double x) => (Math.Clamp(x, 0, 1) * 100).ToString("0", System.Globalization.CultureInfo.InvariantCulture) + "%";
    const string CardCritTag = "card-crit", CardBackTag = "card-back", CardPicksTag = "card-picks";

    /// <summary>For the options test: the specialties shown after the card's skill names, top ability first.</summary>
    internal static List<string> CardPicksForTest(FrameworkElement card) =>
        Descendants(card).OfType<TextBlock>().SelectMany(t => t.Inlines.OfType<System.Windows.Documents.Run>()).Where(r => CardPicksTag.Equals(r.Tag)).Select(r => r.Text.Trim()).ToList();

    /// <summary>The header row's columns, the same as an ability row's: name, share, damage, then the rates shown.</summary>
    static void RateColumns(Grid g, bool crit, bool back)
    {
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(44) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(50) });
        if (crit) g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(CardRateCol) });
        if (back) g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(CardRateCol) });
    }

    /// <summary>For the options test: the card's crit and back cells, top ability first.</summary>
    internal static (List<string> Crit, List<string> Back) CardRatesForTest(FrameworkElement card) =>
        (Descendants(card).OfType<TextBlock>().Where(t => CardCritTag.Equals(t.Tag)).Select(t => t.Text).ToList(),
         Descendants(card).OfType<TextBlock>().Where(t => CardBackTag.Equals(t.Tag)).Select(t => t.Text).ToList());

    /// <summary>For the options test: the card's ability bars and their widths (what the entrance grows them to).</summary>
    internal static List<double> CardBarsForTest(FrameworkElement card) => Descendants(card).OfType<Border>().Where(b => CardBarTag.Equals(b.Tag)).Select(b => b.Width).ToList();

    /// <summary>
    /// A member's hover card: their DPS, share and damage, then their top abilities by damage, each with its share as a bar in the
    /// class's colour, its damage and hits; their heals when they healed; the biggest hit when the record carries it. Only what
    /// the record has (nothing made up); null when there's nothing to show (no damage yet).
    /// </summary>
    internal static FrameworkElement? HoverCard(Encounter e, Fighter f, DateTime now)
    {
        (e, f) = e.CardOf(f);   // a PvP panel row: your fight with that player
        if (!e.HasDamage || e.Figure(f) <= 0) return null;
        var v = FightView.Of(e, now, null);
        if (v.Members.FirstOrDefault(m => m.F.Id == f.Id) is not { } m) return null;
        var c = ClassArt.ColorOf(f.Class);
        TextBlock T(string s, double size, Brush b, FontWeight w, HorizontalAlignment h = HorizontalAlignment.Left)
        { var t = new TextBlock { Text = s, FontSize = size, Foreground = b, FontWeight = w, HorizontalAlignment = h, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis }; System.Windows.Documents.Typography.SetNumeralAlignment(t, FontNumeralAlignment.Tabular); return t; }
        // crit and back attacks per skill, once Settings' switch picks the kinds and the record carries them
        bool critCol = m.Abilities.Any(x => x.Damage > 0 && x.Crit != null), backCol = m.Abilities.Any(x => x.Damage > 0 && x.Back != null);
        var stack = new StackPanel { Width = 250 + (critCol ? CardRateCol : 0) + (backCol ? CardRateCol : 0) };
        var head = new DockPanel { Margin = new Thickness(0, 0, 0, 2) };
        var cls = T(L.T(ClassArt.Title(f.Class)), 10.5, HudKit.Muted, FontWeights.Medium, HorizontalAlignment.Right); DockPanel.SetDock(cls, Dock.Right); head.Children.Add(cls);
        head.Children.Add(T(NameOf(f), 13, f.You ? PK.C(PK.Gold) : ClassArt.NameBrush(f.Class), FontWeights.Bold));
        stack.Children.Add(head);
        var sums = new List<string>();
        if (m.DpsOrNull is { } dps) sums.Add(L.F("{0} DPS", Amounts.Short(dps)));
        sums.Add(Encounter.Pct(m.Share));
        sums.Add(L.F("{0} damage", Amounts.Short(m.Damage)));
        stack.Children.Add(T(string.Join("  ·  ", sums), 11, HudKit.Ink, FontWeights.SemiBold));
        var rates = new List<string>();
        if (m.Crit is { } cr) rates.Add(L.F("{0} crit", Pct0(cr)));
        if (m.CritShare is { } cs) rates.Add(L.F("{0} of the damage from crits", Pct0(cs)));
        if (m.Back is { } ba) rates.Add(L.F("{0} from behind", Pct0(ba)));
        if (rates.Count > 0) stack.Children.Add(T(string.Join("  ·  ", rates), 10.5, PK.C(PK.Gold, 0.92), FontWeights.SemiBold));
        // the abilities with damage on the figures' basis (boss only or all targets): one with none there is not listed
        var dealt = m.Abilities.Where(x => x.Damage > 0).OrderByDescending(x => x.Damage).ToList();
        if (dealt.Count > 0)
        {
            stack.Children.Add(new Border { Height = 1, Background = HudKit.FrameLine, Margin = new Thickness(0, 6, 0, 5) });
            var shownAbilities = dealt.Take(CardAbilities).ToList();
            double topShare = Math.Max(1e-9, shownAbilities[0].Share);
            if (critCol || backCol)
            {
                // the columns' names over them, only when the rates make the row more than share and damage
                var names = new Grid { Height = 13, Margin = new Thickness(0, 0, 0, 1) };
                void Name(string s, int col) { var t = T(s, 9, HudKit.Muted, FontWeights.SemiBold, HorizontalAlignment.Right); Grid.SetColumn(t, col); names.Children.Add(t); }
                RateColumns(names, critCol, backCol);
                Name(L.T("share"), 1); Name(L.T("damage"), 2);
                if (critCol) Name(L.T("crit"), 3);
                if (backCol) Name(L.T("back"), critCol ? 4 : 3);
                stack.Children.Add(names);
            }
            foreach (var a in shownAbilities)
            {
                var row = new Grid { Height = 19, Margin = new Thickness(0, 0, 0, 2) };
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(44) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(50) });
                if (critCol) row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(CardRateCol) });
                if (backCol) row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(CardRateCol) });
                // the ability against the top one as a bar behind the name, in the class's colour, fading right like the meter's
                var fill = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 0) };
                fill.GradientStops.Add(new GradientStop(Color.FromArgb(0x7A, c.R, c.G, c.B), 0)); fill.GradientStops.Add(new GradientStop(Color.FromArgb(0x26, c.R, c.G, c.B), 1)); fill.Freeze();
                var bar = new Border { HorizontalAlignment = HorizontalAlignment.Left, CornerRadius = new CornerRadius(2), Background = fill, BorderBrush = PK.C(c, 0.9), BorderThickness = new Thickness(2, 0, 0, 0),
                    Width = Math.Max(3, 156 * Math.Clamp(a.Share / topShare, 0, 1)), Tag = CardBarTag };
                // the specialties its hits named, after the name in the class's light: "Pummel  3·4"
                var nameText = T(a.Name, 11, HudKit.Ink, FontWeights.Medium);
                if (a.Picks != 0)
                {
                    nameText.Text = "";
                    nameText.Inlines.Add(new System.Windows.Documents.Run(a.Name));
                    nameText.Inlines.Add(new System.Windows.Documents.Run("  " + Abilities.PicksText(a.Picks)) { FontSize = 9.5, FontWeight = FontWeights.Bold, Foreground = PK.C(PK.Gold, 0.85), Tag = CardPicksTag });
                }
                var nameCell = new Grid { ClipToBounds = true, Children = { bar, new Border { Padding = new Thickness(5, 0, 4, 0), Child = nameText } } };
                row.Children.Add(nameCell);
                var pct = T(Encounter.Pct(a.Share), 10.5, HudKit.Muted, FontWeights.Medium, HorizontalAlignment.Right); Grid.SetColumn(pct, 1); row.Children.Add(pct);
                var dmg = T(Amounts.Short(a.Damage), 11, HudKit.Ink, FontWeights.SemiBold, HorizontalAlignment.Right); Grid.SetColumn(dmg, 2); row.Children.Add(dmg);
                if (critCol) { var ct = T(a.Crit is { } ac ? Pct0(ac) : "–", 10.5, a.Crit != null ? PK.C(PK.Gold, 0.92) : HudKit.Muted, FontWeights.SemiBold, HorizontalAlignment.Right); Grid.SetColumn(ct, 3); ct.Tag = CardCritTag; row.Children.Add(ct); }
                if (backCol) { var bt = T(a.Back is { } ab ? Pct0(ab) : "–", 10.5, HudKit.Muted, FontWeights.Medium, HorizontalAlignment.Right); Grid.SetColumn(bt, critCol ? 4 : 3); bt.Tag = CardBackTag; row.Children.Add(bt); }
                if (a.Hits > 0) row.ToolTip = L.F("{0:N0} hits", a.Hits) + (a.Avg is { } avg ? L.F(", {0} a hit", Amounts.Short(avg)) : "") + (!e.BossView && a.Max is { } mx ? L.F(", biggest {0}", Amounts.Short(mx)) : "");
                stack.Children.Add(row);
            }
            int more = dealt.Count - CardAbilities;
            if (more > 0)
            {
                long rest = dealt.Skip(CardAbilities).Sum(x => x.Damage);
                stack.Children.Add(T(L.F("{0} more  ·  {1}", more, Amounts.Short(rest)), 10.5, HudKit.Muted, FontWeights.Medium));
            }
        }
        var foot = new List<string>();
        var dealtHits = m.Abilities.Where(x => x.Damage > 0).ToList();
        int hits = dealtHits.Sum(a => a.Hits);
        if (hits > 0) foot.Add(L.F("{0:N0} hits", hits));
        // the biggest hit is over every target: said only when the figures are too (boss only, it could be an add's)
        if (!e.BossView && m.MaxHit is { } big && big > 0) foot.Add(L.F("biggest {0}", Amounts.Short(big)));
        if (f.Healing > 0) foot.Add(L.F("healed {0}", Amounts.Meter(f.HealingEffective)) + (e.HpsOf(f, now) is { } hps ? L.F(" ({0}/s)", Amounts.Meter(hps)) : ""));
        if (f.Deaths > 0) foot.Add(L.F(f.Deaths == 1 ? "{0} death" : "{0} deaths", f.Deaths));
        if (foot.Count > 0) stack.Children.Add(T(string.Join("  ·  ", foot), 10.5, HudKit.Muted, FontWeights.Medium));
        if (stack.Children[^1] is FrameworkElement last) last.Margin = new Thickness(0, 4, 0, 0);
        var card = new Border
        {
            Child = stack, Padding = new Thickness(11, 8, 11, 9), CornerRadius = new CornerRadius(8), Background = HudKit.B(HudKit.Panel2C, 0.97), BorderBrush = PK.C(c, 0.55), BorderThickness = new Thickness(1),
            Effect = new DropShadowEffect { Color = Colors.Black, BlurRadius = 14, ShadowDepth = 2, Opacity = 0.55 },
        };
        System.Windows.Documents.TextElement.SetFontFamily(card, HudKit.Text);
        TextOptions.SetTextFormattingMode(card, TextFormattingMode.Display);
        return card;
    }

    void SetBar(RowEl r, double v)
    {
        double w = r.Track.ActualWidth;
        if (w <= 0) { r.Track.Tag = v; return; }
        r.BarFill.Width = v <= 0.001 ? 0 : Math.Max(6, w * Math.Clamp(v, 0, 1));
        if (r.HotEdge != null) r.HotEdge.Visibility = v >= 0.999 || r.BarFill.Width < DpsLeft(r, w) - 14 ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>The name's line rises this much on a healer's row, so the heal figures fit under it (the row keeps its height).</summary>
    public const double HealLift = 10;

    /// <summary>
    /// The heal chip on a healer's row (anyone with healing: self-heals count, potions don't): "✚ 2.9K/s", their healing a
    /// second over the DPS's seconds, or the healing done until there are seconds enough. The tooltip has the rest: done, on
    /// others, and the part that fit under the targets' HP (≈ unless the HP confirmed all of it).
    /// </summary>
    void SetHeal(RowEl r, Encounter e, Fighter f, DateTime now, bool healer)
    {
        if (!healer)
        {
            if (r.HealPill.Visibility == Visibility.Collapsed) return;
            r.HealPill.Visibility = Visibility.Collapsed; r.Heal.Text = ""; r.HealShare = r.HealShown = double.NaN;   // Text "" keeps FitName from bringing it back
            r.Under.Visibility = r.UnderTrack.Visibility = Visibility.Collapsed;
            FitName(r);
            return;
        }
        string wasFull = r.HealFull; bool wasShort = r.Heal.Text.Length > 0 && r.Heal.Text == r.HealShort && r.HealShort != r.HealFull;
        double? hps = e.HpsOf(f, now);
        // the part that landed (overheal out), as the DPS is the damage that landed; the whole in the tooltip
        // the total is the hover's (the member's card: "healed 132K (2.4K/s)"); beside the figure it was "too crowded" (Steven, 2026-11-16)
        string text = hps is { } x ? L.F("{0}/s", Compact(x)) : Compact(f.HealingEffective);
        r.HealDone = Amounts.Short(f.Healing);
        r.HealFull = text;
        r.HealShort = hps is { } y ? L.F("{0}/s", Compact(y)) : text;
        r.Heal.Text = wasShort ? r.HealShort : text;
        r.HealPill.ToolTip = (f.HealingIsEstimate ? L.T("about ") : "") + L.F("{0:N0} a second landed", hps ?? 0) + "\n"
            + L.F("Healed {0:N0}", f.Healing) + (f.HealingOthers > 0 && f.HealingSelf > 0 ? L.F(" ({0:N0} on others)", f.HealingOthers) : f.HealingOthers <= 0 ? L.T(" (on themselves)") : "") + "; "
            + (f.HealingIsEstimate ? L.T("about ") : "") + L.F("{0:N0} landed, the rest overheal", f.HealingEffective)
            + (f.Healing > 0 ? L.F(" ({0}% overheal)", Math.Round(100.0 * (f.Healing - f.HealingEffective) / f.Healing)) : "")
            + (f.HealingIsEstimate ? L.T(" (an estimate: the HP didn't confirm all of it)") : "");
        if (wasFull.Length != text.Length || wasShort) FitName(r);   // a chip new or of another length: the name's line fits again
        // the heal bar along the row's foot (Steven, 2026-11-16: "the healing bar underneath ... should be toggleable and slightly
        // bigger"): 4 px, sliding to the member's share of the group's healing (self-heals in), a Settings switch
        if (!HealBarsPref) { r.Under.Visibility = r.UnderTrack.Visibility = Visibility.Collapsed; r.HealShare = r.HealShown = double.NaN; return; }
        r.Under.Visibility = r.UnderTrack.Visibility = Visibility.Visible;
        // its length the member's share of the fight's healing that landed (Steven, 2026-11-16: "the bars should be % of total
        // heals in fight"), over a track the row's width so a short one still reads as a bar
        long all = e.Members.Sum(m => m.HealingEffective);
        double share = all > 0 ? Math.Clamp((double)f.HealingEffective / all, 0, 1) : 0;
        r.HealShare = share;
        Motion.Slide(r.Under, $"parser|ovheal|{e.Id}|{f.Id}|{(int)_kind}", share, v => { r.HealShown = v; SetHealBar(r, v); });
    }

    /// <summary>The heal bar's length: its share of the row's track (never a sliver under 4 px once there's any).</summary>
    static void SetHealBar(RowEl r, double v)
    {
        double w = r.Track.ActualWidth;
        if (w <= 0) return;   // laid out later: the track's resize draws it (from the share shown)
        r.Under.Width = v <= 0.001 ? 0 : Math.Max(4, w * Math.Clamp(v, 0, 1));
    }

    /// <summary>The heal chip's cross: plain green, or on a green square for a boss kill's top healer (tagged as the TOP HEALS plate it stands for).</summary>
    static void SetTopHeals(RowEl r, bool top)
    {
        if (r.HealCross is not Border { Child: TextBlock t } tab) return;
        string tag = top ? "top-heals" : "heal-label";
        if (tab.Tag as string == tag) return;
        tab.Tag = tag;
        tab.Background = top ? ParserFoot.Plate(heals: true).Background : null;
        t.Foreground = top ? HudKit.B(0x062A17) : HudKit.B(0x3DD68C);
        tab.ToolTip = top ? L.T("TOP HEALS") : null;
        FitName(r);
    }

    /// <summary>The heal chip sits on the name's line: the name gives way to it (FitName), nothing else to fit.</summary>
    static void FitHeal(RowEl r) { }

    RowEl Build(Fighter f, bool expanded, bool dmgCol, bool crit, bool back, bool spark, bool hero = false)
    {
        if (_skin != Skin.Classic) return BuildSkin(f, expanded, dmgCol, crit, back, spark, hero);
        var c = ClassArt.ColorOf(f.Class);
        bool bare = _kind == Kind.Bare;
        var widths = new List<double> { ChipCol, IconCol, -1, DpsCol, ShareCol };
        if (expanded) { if (dmgCol) widths.Add(DamageCol); if (crit) widths.Add(RateCol); if (back) widths.Add(RateCol); if (spark) widths.Add(SparkCol); }
        var cols = PK.Cols(widths.ToArray());
        // your row sits in the gold frame (a 1 px edge all round): its inside is that much shorter, so nothing at its foot (the heal
        // line, the heat's line) is cut off and its text is centred as on the other rows
        double innerH = _rowH - 2 - (f.You ? 2 : 0);
        cols.Height = innerH;
        cols.Margin = new Thickness(5, 0, 6, 0);
        // the angled rank chip in the class's colour (gold for you)
        var chipText = new TextBlock { FontSize = 11, FontWeight = FontWeights.Bold, Foreground = f.You ? HudKit.B(0x1D1300) : PK.C(ClassArt.NameColor(f.Class)), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        var chip = new Border
        {
            Width = 22, Height = 18, CornerRadius = new CornerRadius(3), VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Left,
            Background = PK.C(f.You ? PK.Gold : c, f.You ? 0.95 : 0.22), BorderBrush = PK.C(f.You ? PK.Gold : c, f.You ? 1 : 0.75), BorderThickness = new Thickness(1), Child = chipText,
            RenderTransformOrigin = new Point(0.5, 0.5), RenderTransform = new TransformGroup { Children = { new SkewTransform(-14, 0), new ScaleTransform(1, 1) } },
            Effect = f.You ? new DropShadowEffect { Color = PK.Gold, BlurRadius = 10, ShadowDepth = 0, Opacity = 0.6 } : null,
        };
        cols.Children.Add(chip);
        cols.Children.Add(PK.Col(ClassArt.Icon(f.Class, 19, f.Name), 1));
        var nm = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) };
        var tick = new Border { Width = 15, Height = 15, CornerRadius = new CornerRadius(2), Background = PK.C(PK.Coral), Margin = new Thickness(0, 0, 5, 0), VerticalAlignment = VerticalAlignment.Center, Visibility = Visibility.Collapsed,
            RenderTransformOrigin = new Point(0.5, 0.5), Effect = new DropShadowEffect { Color = PK.Coral, BlurRadius = 9, ShadowDepth = 0, Opacity = 0.85 },
            Child = new TextBlock { Text = "✕", FontSize = 8.5, FontWeight = FontWeights.Bold, Foreground = Brushes.White, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center } };
        nm.Children.Add(tick);
        // the name in white (0.10.131, "a little hard to read"): the class tint melted into the class's own bar; the icon and the bar say the class
        var name = new TextBlock { Text = NameOf(f), FontSize = 13.5, FontWeight = f.You ? FontWeights.Bold : FontWeights.SemiBold, Foreground = f.Name.Length > 0 ? HudKit.Ink : HudKit.Muted, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
        // a soft dark halo under the name and the DPS: they read over any bar (Steven, 2026-11-15: "the highlight over the name
        // of yourself makes it so hard to read")
        name.Effect = TextHalo();
        nm.Children.Add(name);
        // Combat Power after the name ("150.8K", muted): from the party list, there from the start
        var cp = new TextBlock { FontSize = 11, FontWeight = FontWeights.SemiBold, Foreground = HudKit.B(0xB1B7C8), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(5, 1, 0, 0),
            Tag = "cp", Effect = TextHalo(), Visibility = Visibility.Collapsed };
        System.Windows.Documents.Typography.SetNumeralAlignment(cp, FontNumeralAlignment.Tabular);
        nm.Children.Add(cp);
        if (f.You) { var yc = HudGlass.YouChip(); if (yc.Child is TextBlock yt) yt.Text = L.T(yt.Text); yc.Tag = "you"; nm.Children.Add(yc); }
        // the heal chip after the name (a healer's row only; Steven, 2026-11-16: the tiny green figures under the name and the
        // line along the row's foot "look awful"): a green cross and the healing a second, on the name's line, so the row keeps
        // its look and its height; the rest (done, landed, on others) in the chip's tooltip and the member's pop-up
        // 2026-11-16 later: "TOO small and you cant tell wtf is going on": the chip says what it is (a solid HEAL tab) and its figure
        // reads at the name's size, short (7.8K/s), as tall as the rank chip
        // 2026-11-16 later still: the HEAL tab and its box took "WAY too much space": a green cross and the figure, nothing round
        // them (about half the width); the top healer's cross on a solid green square (the TOP HEALS plate it stands for)
        var heal = new TextBlock { FontSize = 11.5, FontWeight = FontWeights.Bold, Foreground = HudKit.B(0x9CFACB), Tag = "heal-figures", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(2, 0, 0, 0), Effect = TextHalo() };
        System.Windows.Documents.Typography.SetNumeralAlignment(heal, FontNumeralAlignment.Tabular);
        var cross = new Border { Tag = "heal-label", Width = 13, Height = 13, CornerRadius = new CornerRadius(3), VerticalAlignment = VerticalAlignment.Center,
            Child = new TextBlock { Text = "\u271A", FontSize = 10.5, FontWeight = FontWeights.Bold, Foreground = HudKit.B(0x3DD68C), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Effect = TextHalo() } };
        var healPill = new Border { Tag = "heal-pill", Margin = new Thickness(5, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, Visibility = Visibility.Collapsed,
            Child = new StackPanel { Orientation = Orientation.Horizontal, Children = { cross, heal } } };
        nm.Children.Add(healPill);
        var pb = new Border { CornerRadius = new CornerRadius(3), Padding = new Thickness(5, 0, 5, 1), Margin = new Thickness(6, 0, 0, 0), Background = HudKit.Gold, VerticalAlignment = VerticalAlignment.Center, Visibility = Visibility.Collapsed,
            RenderTransformOrigin = new Point(0, 0.5), Effect = new DropShadowEffect { Color = PK.Gold, BlurRadius = 12, ShadowDepth = 0, Opacity = 0.9 },
            Child = new TextBlock { FontSize = 9.5, FontWeight = FontWeights.Bold, Foreground = HudKit.B(0x1D1300) } };
        nm.Children.Add(pb);
        var plates = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Visibility = Visibility.Collapsed };
        nm.Children.Add(plates);
        // the name gives way to the chips and plates after it (it trims), so the row never widens and the figures never move
        var nameHost = new Border { Child = nm, ClipToBounds = true, VerticalAlignment = VerticalAlignment.Center };
        cols.Children.Add(PK.Col(nameHost, 2));
        TextBlock Fig(double size, Brush b, FontWeight w) { var t = new TextBlock { FontSize = size, Foreground = b, FontWeight = w, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center }; System.Windows.Documents.Typography.SetNumeralAlignment(t, FontNumeralAlignment.Tabular); return t; }
        var dps = Fig(16, HudKit.Ink, FontWeights.Bold); dps.Tag = "row-dps"; dps.Effect = TextHalo();
        var share = Fig(12, HudKit.B(0xDDE0E8), FontWeights.SemiBold); share.Effect = TextHalo(); share.Margin = new Thickness(0, 0, expanded ? 0 : 2, 0);
        var damage = Fig(11, HudKit.B(0xC9CDDA), FontWeights.SemiBold);
        var critT = Fig(11, HudKit.B(0xC9CDDA), FontWeights.SemiBold); critT.ToolTip = L.T("Crit rate");
        // back attacks beside crit, in a quieter tone so the two rates read apart
        var backT = Fig(11, HudKit.B(0x9FA6B8), FontWeights.Medium); backT.ToolTip = L.T("Hits from behind");
        var sparkG = new Grid { Width = 60, Height = 18, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
        // the odometer over the figure, right-aligned in the same column: the column never moves, rolling or not
        var odo = new Odometer(16, FontWeights.Bold, (HudKit.Ink as SolidColorBrush)?.Color ?? Colors.White) { Effect = TextHalo(), Visibility = Visibility.Collapsed };
        var dpsHost = new Grid { HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center, Children = { dps, odo }, Effect = Outline() };
        var shareHost = new Grid { HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center, Children = { share }, Effect = Outline() };
        // the DPS ends at its column's right edge; a figure wider than the column (the party's Combat Power before a hit, in full)
        // reaches into the name's room, which gives it that much (FitName)
        cols.Children.Add(PK.Col(dpsHost, 2, 2)); cols.Children.Add(PK.Col(shareHost, 4));
        int k = 5;
        if (expanded) { if (dmgCol) cols.Children.Add(PK.Col(damage, k++)); if (crit) cols.Children.Add(PK.Col(critT, k++)); if (back) cols.Children.Add(PK.Col(backT, k++)); if (spark) cols.Children.Add(PK.Col(sparkG, k++)); }
        // the bar: a quiet track the row's width, the fill in the class's colour, angled at its end, fading to the right
        var track = new Border { CornerRadius = new CornerRadius(3), Background = PK.C(c, bare ? 0 : 0.15) };   // the row's whole length reads as the bar's gauge
        var fade = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 0) };
        // your row's bar quieter: under the gold frame's wash a full-strength bar made the name hard to read
        // 2026-11-16: "really hard to see the damage bars cause transparent": more solid, the fade gentler (names keep their halo)
        // 0.10.131: a shade deeper than the class colour (15% toward the panel), so the white name and figures stand on it and the bar still shows
        var deep = Color.FromRgb((byte)(c.R * 0.85 + 0x10 * 0.15), (byte)(c.G * 0.85 + 0x12 * 0.15), (byte)(c.B * 0.85 + 0x18 * 0.15));
        fade.GradientStops.Add(new GradientStop(Color.FromArgb(bare ? (byte)0xA0 : f.You ? (byte)0x98 : (byte)0xD8, deep.R, deep.G, deep.B), 0));
        fade.GradientStops.Add(new GradientStop(Color.FromArgb(bare ? (byte)0x50 : f.You ? (byte)0x58 : (byte)0x80, deep.R, deep.G, deep.B), 1));
        fade.Freeze();
        var fill = new Border { HorizontalAlignment = HorizontalAlignment.Left, CornerRadius = new CornerRadius(3), Background = fade, Width = 0, RenderTransformOrigin = new Point(0, 1), RenderTransform = new SkewTransform(-16, 0) };
        var edge = new Border { Width = 2, HorizontalAlignment = HorizontalAlignment.Left, Background = PK.C(c), CornerRadius = new CornerRadius(3, 0, 0, 3), Visibility = f.You ? Visibility.Collapsed : Visibility.Visible };
        // the bar's ghost (the trail of a move), angled as the fill, over it; never in the layout
        var ghost = new Border { HorizontalAlignment = HorizontalAlignment.Left, CornerRadius = new CornerRadius(3), IsHitTestVisible = false, Visibility = Visibility.Collapsed,
            RenderTransformOrigin = new Point(0, 1), RenderTransform = new SkewTransform(-16, 0) };
        // your heat: a warm wash across the row, from the left, and a warm line along its foot (only your row has one)
        var heat = new Border { IsHitTestVisible = false, Visibility = Visibility.Collapsed, BorderThickness = new Thickness(0, 0, 0, 1.5), CornerRadius = new CornerRadius(3),
            Background = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 0), GradientStops = { new GradientStop(Colors.Transparent, 0), new GradientStop(Colors.Transparent, 1) } }, BorderBrush = new SolidColorBrush(Colors.Transparent) };
        // the heal line along the row's foot, over the bar: green, brighter at its head, with a soft glow (a healer's row only)
        var healFade = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 0) };
        healFade.GradientStops.Add(new GradientStop(Color.FromArgb(0xB0, 0x2F, 0xC4, 0x7A), 0)); healFade.GradientStops.Add(new GradientStop(Color.FromRgb(0x7D, 0xF5, 0xB5), 1));
        healFade.Freeze();
        var under = new Border { Height = HealBarPx, VerticalAlignment = VerticalAlignment.Bottom, HorizontalAlignment = HorizontalAlignment.Left, IsHitTestVisible = false, Visibility = Visibility.Collapsed,
            Tag = "heal-line", Width = 0, CornerRadius = new CornerRadius(2), Background = healFade, Effect = new DropShadowEffect { Color = PK.Mint, BlurRadius = 5, ShadowDepth = 0, Opacity = 0.7 } };
        // its track: the row's width in a dark green, so the bar reads as a bar even when short
        var underTrack = new Border { Height = HealBarPx, VerticalAlignment = VerticalAlignment.Bottom, IsHitTestVisible = false, Visibility = Visibility.Collapsed, Tag = "heal-track",
            CornerRadius = new CornerRadius(2), Background = new SolidColorBrush(Color.FromArgb(0x38, 0x2F, 0xC4, 0x7A)) };
        var inner = new Grid { Height = innerH, ClipToBounds = true };
        inner.Children.Add(track); inner.Children.Add(fill); inner.Children.Add(ghost); inner.Children.Add(edge); inner.Children.Add(heat); inner.Children.Add(underTrack); inner.Children.Add(under); inner.Children.Add(cols);
        Border frame = f.You ? PK.YouFrame(inner, 4, new Thickness(0)) : new Border { Child = inner };
        // its moves: the glide and the lift (a rise from the right edge, so the figures' column stays where it is)
        var lift = new TranslateTransform(); var rise = new ScaleTransform(1, 1);
        var root = new Grid { VerticalAlignment = VerticalAlignment.Top, Height = _rowH - 2, RenderTransformOrigin = new Point(1, 0.5), RenderTransform = new TransformGroup { Children = { rise, lift } } };
        root.Children.Add(frame);
        if (f.You) Panel.SetZIndex(root, 2);
        var r = new RowEl { Root = root, Cols = cols, Chip = chip, ChipText = chipText, Name = name, Dps = dps, Share = share, Damage = damage, Crit = critT, Back = backT, Spark = sparkG, BarFill = fill, Track = track, Edge = edge, Tick = tick, Pb = pb, Frame = frame, You = f.You, Cls = f.Class, NameLine = nm, Plates = plates, NameHost = nameHost,
            Odo = odo, Lift = lift, Rise = rise, Ghost = ghost, Heat = heat, Under = under, UnderTrack = underTrack, Cp = cp, Heal = heal, HealPill = healPill, HealCross = cross };
        r.ChipOwn = chip.Effect as DropShadowEffect; r.ChipBlur0 = r.ChipOwn?.BlurRadius ?? 0; r.ChipOpacity0 = r.ChipOwn?.Opacity ?? 0;
        r.FrameGlow = frame.Effect as DropShadowEffect; r.FrameBlur0 = r.FrameGlow?.BlurRadius ?? 0; r.FrameOpacity0 = r.FrameGlow?.Opacity ?? 0;
        // the member's abilities on hover (2026-11-15 night, the meters players pick: a breakdown on the meter itself, not only
        // on the Parser page): built when it opens, from the fight as it stands, so it costs nothing while the meter runs
        var card = new ToolTip { Background = Brushes.Transparent, BorderThickness = new Thickness(0), Padding = new Thickness(0), HasDropShadow = false, Placement = System.Windows.Controls.Primitives.PlacementMode.Right, HorizontalOffset = 6 };
        root.ToolTip = card;
        ToolTipService.SetInitialShowDelay(root, 350);
        ToolTipService.SetShowDuration(root, 60_000);
        long fid = f.Id;
        root.ToolTipOpening += (_, ev) =>
        {
            // never while clicks go through to the game (the pointer is the game's; an edge taken for a resize is no hover)
            if (Window.GetWindow(this) is ParserOverlay { ClickThroughNow: true }) { ev.Handled = true; return; }
            if (!FillCard(card, fid)) ev.Handled = true;
        };
        card.Closed += (_, _) => { if (_openCard == card) _openCard = null; };
        nameHost.SizeChanged += (_, _) => { FitName(r); FitHeal(r); };
        track.SizeChanged += (_, _) =>
        {
            if (track.Tag is double v) SetBar(r, v); else SetBar(r, fill.Width > 0 ? fill.Width / Math.Max(1, track.ActualWidth) : 0);
            if (!double.IsNaN(r.HealShare)) SetHealBar(r, double.IsNaN(r.HealShown) ? r.HealShare : r.HealShown);
        };
        return r;
    }

    /// <summary>The name's room: the column less what follows it on the line (the tick, YOU, PB, the plates); the name trims to it.</summary>
    static void FitName(RowEl r)
    {
        double w = r.NameHost.ActualWidth;
        if (w <= 0) return;
        var you = r.NameLine.Children.OfType<Border>().FirstOrDefault(b => b.Tag as string == "you");
        if (you != null) you.Visibility = Visibility.Visible;
        r.Cp.Visibility = r.Cp.Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        PlatesWide(r, true);
        if (r.Heal.Text.Length > 0)
        {
            r.HealPill.Visibility = Visibility.Visible;
            if (r.HealFull.Length > 0 && r.Heal.Text != r.HealFull) r.Heal.Text = r.HealFull;
            // the figure's length may have changed: the chip measured afresh (a parent's Measure keeps its last size until the layout pass reaches it)
            r.Heal.InvalidateMeasure(); ((UIElement)r.HealPill.Child).InvalidateMeasure(); r.HealPill.InvalidateMeasure();
        }
        // a figure wider than the DPS's column (the Combat Power before a hit) takes its overflow from the name's room
        r.Dps.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        double dpsCol = r.Cols.ColumnDefinitions.Count > 3 && r.Cols.ColumnDefinitions[3].ActualWidth > 0 ? r.Cols.ColumnDefinitions[3].ActualWidth : DpsCol;
        double over = Math.Max(0, r.Dps.DesiredSize.Width - dpsCol + 4);
        double Others()
        {
            double o = r.NameLine.Margin.Left + r.NameLine.Margin.Right + over;
            foreach (UIElement c in r.NameLine.Children)
            {
                if (ReferenceEquals(c, r.Name) || c.Visibility == Visibility.Collapsed) continue;
                c.InvalidateMeasure();
                c.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                o += c.DesiredSize.Width;
            }
            return o;
        }
        // the name keeps its first ten letters whole (a longer one trims after them); what follows it on the line gives way in
        // turn while it doesn't fit: YOU (the gold frame and chip say it), the TOP DPS plate to its mark, Combat Power (its
        // tooltip and the card say it), the heal chip's total, then the chip (the card says the healing)
        double keep = NameKeep(r);
        double others = Others();
        bool Short() => w - others < keep;
        // Combat Power in its "CP" pair on the name's line (Podium) goes first, before YOU and the plates (Steven: "CP drops first")
        if (Short() && r.Cp.Visibility == Visibility.Visible && (r.Cp.Parent as FrameworkElement)?.Parent == r.NameLine) { r.Cp.Visibility = Visibility.Collapsed; others = Others(); }
        if (Short() && you is { Visibility: Visibility.Visible }) { you.Visibility = Visibility.Collapsed; others = Others(); }
        if (Short() && r.Plates.Visibility == Visibility.Visible) { PlatesWide(r, false); others = Others(); }
        if (Short() && r.Cp.Visibility == Visibility.Visible && r.Cp.Parent == r.NameLine) { r.Cp.Visibility = Visibility.Collapsed; others = Others(); }
        if (r.Heal.Text.Length > 0)
        {
            if (Short() && r.HealShort.Length > 0 && r.HealShort != r.Heal.Text && r.HealPill.Parent == r.NameLine)
            {
                r.Heal.Text = r.HealShort;
                r.Heal.InvalidateMeasure(); ((UIElement)r.HealPill.Child).InvalidateMeasure(); r.HealPill.InvalidateMeasure();
                others = Others();
            }
            if (Short() && r.HealPill.Parent == r.NameLine) { r.HealPill.Visibility = Visibility.Collapsed; others = Others(); }
        }
        r.Name.MaxWidth = Math.Max(24, w - others);
    }

    /// <summary>The room a name keeps on its line: whole up to ten letters, else its first ten and the "…".</summary>
    static double NameKeep(RowEl r)
    {
        var n = r.Name;
        if (n.Text.Length <= 10)
        {
            n.MaxWidth = double.PositiveInfinity;
            n.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            return Math.Ceiling(n.DesiredSize.Width) + 1;
        }
        var t = new TextBlock { Text = n.Text[..10] + "…", FontSize = n.FontSize, FontWeight = n.FontWeight, FontFamily = n.FontFamily };
        TextOptions.SetTextFormattingMode(t, TextFormattingMode.Display);
        t.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        return Math.Ceiling(t.DesiredSize.Width) + 2;
    }

    /// <summary>The TOP DPS plate in words, or short of room its mark (a gold ★ on the same plate; its tooltip says it).</summary>
    static void PlatesWide(RowEl r, bool wide)
    {
        foreach (var p in r.Plates.Children.OfType<Border>())
        {
            var word = p.Child as TextBlock ?? (p.Child as Panel)?.Children.OfType<TextBlock>().FirstOrDefault();
            if (word == null) continue;
            bool heals = p.Tag as string == "top-heals";
            string text = heals ? (wide ? L.T("TOP HEALS") : "✚") : wide ? L.T("TOP DPS") : "\u2605";
            if (word.Text == text) continue;
            word.Text = text;
            word.FontSize = wide ? 8 : 9.5;
            p.Padding = wide ? new Thickness(4, 0, 4, 0) : new Thickness(3, 0, 3, 0);
        }
    }

    /// <summary>The heal chip's figure, short: 940, 7.8K, 91.8K, 1.24M (the tooltip has it whole).</summary>
    internal static string Compact(double v) => Amounts.Meter(v);

    /// <summary>The least room a name keeps beside its heal chip (a few letters and the ellipsis).</summary>
    const double NameMin = 40;

    /// <summary>The record chip's words: "PB +4.2%" (DPS), "HEAL PB +9%", or both ("PB +4.2% · HEAL +9%").</summary>
    internal static string PbText(double? dps, double? heal) => (dps, heal) switch
    {
        ({ } d, { } h) => L.F("PB {0} · HEAL {1}", PK.Signed(d), PK.Signed(h)),
        ({ } d, null) => L.F("PB {0}", PK.Signed(d)),
        (null, { } h) => L.F("HEAL PB {0}", PK.Signed(h)),
        _ => "",
    };

    /// <summary>The heal bar's height (2 px to 0.10.121: "TOO small"; 4 px to 0.10.126: "cant see the bars at all").</summary>
    public const double HealBarPx = 5;

    /// <summary>The least a name keeps beside the + heal chip in a tight meter (two letters and the ellipsis).</summary>
    const double NameTight = 26;

    /// <summary>For the tests: the plates on each shown member's row ("top-dps", "top-heals"), by name.</summary>
    internal Dictionary<string, string> PlatesForTest() => _rows.Values.Where(r => r.Root.Visibility == Visibility.Visible)
        .ToDictionary(r => r.Name.Text, r => string.Join(",", r.Plates.Children.OfType<FrameworkElement>().Select(p => p.Tag as string ?? "").Concat(r.HealCross.Tag as string == "top-heals" && r.HealPill.Visibility == Visibility.Visible ? new[] { "top-heals" } : Array.Empty<string>())));

    // ---- the motion (Ui/Motion.cs courses: held at KL_MOTION_AT in a render, nothing while the tracker sits still)

    /// <summary>A row's glide to its new place takes this long; the one who passed lifts this much (px, and its rise from the right edge).</summary>
    public const double GlideMs = 380, LiftPx = 3, LiftRise = 0.018;

    /// <summary>
    /// The row glides from where it is to its new place. The one who passed another (<paramref name="passed"/>) slides past
    /// over it, lifted a little with a shadow under it; the one passed sinks under, dimmed for a moment.
    /// </summary>
    void Glide(RowEl r, double to, bool passed = false)
    {
        double from = r.Y + r.Lift.Y;
        r.Y = to;
        r.Root.Margin = new Thickness(0, to, 0, 0);
        var tt = r.Lift;
        // a glide under way is replaced: whatever it left (its lift, its shadow, its dimming) goes first
        r.Rise.ScaleX = r.Rise.ScaleY = 1; r.Root.Opacity = 1;
        if (r.Root.Effect != null) r.Root.ClearValue(EffectProperty);
        if (!Motion.On) { tt.Y = 0; return; }
        double offset = from - to;
        tt.Y = offset;
        bool down = to > from;
        if (down) Panel.SetZIndex(r.Root, 0); else Panel.SetZIndex(r.Root, r.You ? 3 : 2);
        bool lift = passed && !down;
        // the rise grows from the DPS's right edge, so the figure stays exactly where it is while the row lifts
        if (lift && r.Root.ActualWidth > 0) r.Root.RenderTransformOrigin = new Point(Math.Clamp(DpsRightIn(r) / r.Root.ActualWidth, 0, 1), 0.5);
        // Podium's one-line rows cross in one lane: the one going down is dimmed to a third at once and stays so until they have
        // passed (the figures never read over each other), then comes back
        bool podium = _skin == Skin.Podium;
        static double Dim(double p) => p < 0.06 ? p / 0.06 : p < 0.6 ? 1 : Math.Max(0, (1 - p) / 0.4);
        var shadow = lift ? r.LiftShadow ??= new DropShadowEffect { Color = Colors.Black, ShadowDepth = 0, BlurRadius = 0, Opacity = 0, Direction = 270 } : null;
        if (shadow != null) { shadow.BlurRadius = shadow.ShadowDepth = shadow.Opacity = 0; r.Root.Effect = shadow; }
        Motion.Play(r.Root, GlideMs, p =>
        {
            tt.Y = offset * (1 - Motion.Out(p));
            if (down) r.Root.Opacity = 1 - 0.65 * (podium ? Dim(p) : Math.Sin(Math.PI * Math.Clamp(p, 0, 1)));
            if (shadow != null)
            {
                double k = Math.Sin(Math.PI * Math.Clamp(p, 0, 1));
                tt.Y -= LiftPx * k;
                r.Rise.ScaleX = r.Rise.ScaleY = 1 + LiftRise * k;
                shadow.BlurRadius = 12 * k; shadow.ShadowDepth = 3 * k; shadow.Opacity = 0.6 * k;
            }
        },
            () => { tt.Y = 0; r.Root.Opacity = 1; r.Rise.ScaleX = r.Rise.ScaleY = 1; if (shadow != null && r.Root.Effect == shadow) r.Root.ClearValue(EffectProperty); Panel.SetZIndex(r.Root, r.You ? 2 : 0); });
    }

    /// <summary>Where the DPS's column ends across the row (its right edge, the figure's).</summary>
    static double DpsRightIn(RowEl r)
    {
        var defs = r.Cols.ColumnDefinitions;
        double left = DpsLeft(r, r.Track.ActualWidth);
        return left + (defs.Count > 3 && defs[3].ActualWidth > 0 ? defs[3].ActualWidth : 60) + (r.You ? 1 : 0);
    }

    /// <summary>The rank chip's turn, and the glow of the one who passed (after the turn, fading).</summary>
    public const double FlipMs = 420;

    /// <summary>The rank chip turns over like a card: the old number folds away, the new one unfolds; a rank gained glows briefly in the class's colour.</summary>
    void Flip(RowEl r, int was, int now)
    {
        var sc = (ScaleTransform)((TransformGroup)r.Chip.RenderTransform).Children[1];
        if (!Motion.On) { r.ChipText.Text = now.ToString(CultureInfo.InvariantCulture); r.RankInk?.Invoke(); sc.ScaleY = 1; return; }
        bool gained = now < was;
        // the chip's own look back first (a glow a flip under way left), then the gain's glow from there
        var own = r.ChipOwn;
        double ownBlur = r.ChipBlur0, ownOpacity = r.ChipOpacity0;
        void Rest() { if (own != null) { own.BlurRadius = ownBlur; own.Opacity = ownOpacity; } else if (r.Chip.Effect != null) r.Chip.ClearValue(EffectProperty); }
        Rest();
        var glow = gained ? own ?? (r.ChipGlow ??= new DropShadowEffect { Color = r.You ? PK.Gold : ClassArt.ColorOf(r.Cls), ShadowDepth = 0, BlurRadius = 0, Opacity = 0 }) : null;
        if (glow != null && own == null) { glow.BlurRadius = glow.Opacity = 0; r.Chip.Effect = glow; }
        // Podium: the new number at once, the chip unfolding with the row's glide (it lands with the row, not after it)
        bool land = _skin == Skin.Podium;
        if (land) { r.ChipText.Text = now.ToString(CultureInfo.InvariantCulture); r.RankInk?.Invoke(); }
        Motion.Play(r.Chip, FlipMs, p =>
        {
            if (land)
            {
                sc.ScaleY = 0.25 + 0.75 * Motion.Out(Math.Min(1, p / 0.35));
                if (glow != null) { double g = p < 0.2 ? 0 : Math.Sin(Math.PI * Math.Min(1, (p - 0.2) / 0.8)); glow.BlurRadius = ownBlur + 14 * g; glow.Opacity = ownOpacity + (0.95 - ownOpacity) * g; }
                return;
            }
            double q = Math.Min(1, p / 0.76);
            sc.ScaleY = Math.Max(0.04, Math.Abs(Math.Cos(Math.PI * q))); r.ChipText.Text = (q < 0.5 ? was : now).ToString(CultureInfo.InvariantCulture); r.RankInk?.Invoke();
            if (glow != null) { double k = p < 0.45 ? 0 : Math.Sin(Math.PI * (p - 0.45) / 0.55); glow.BlurRadius = ownBlur + 14 * k; glow.Opacity = ownOpacity + (0.95 - ownOpacity) * k; }
        },
            () =>
            {
                sc.ScaleY = 1; r.ChipText.Text = now.ToString(CultureInfo.InvariantCulture); r.RankInk?.Invoke();
                Rest();
            });
    }

    /// <summary>The bar's ghost: the length it lost holds, then shrinks onto the fill and fades; a length gained flashes light and fades.</summary>
    public const double GhostMs = 400;

    void GhostTrail(RowEl r, double from, double to)
    {
        double w = r.Track.ActualWidth;
        if (!Motion.On || w <= 0) return;
        var g = r.Ghost;
        bool lost = to < from;
        var c = ClassArt.ColorOf(r.Cls);
        var brush = new SolidColorBrush(lost ? Color.FromArgb(0x80, (byte)Math.Min(255, c.R + 60), (byte)Math.Min(255, c.G + 60), (byte)Math.Min(255, c.B + 60)) : Color.FromArgb(0x48, 0xFF, 0xFF, 0xFF));
        g.Background = brush;
        g.Visibility = Visibility.Visible;
        double lo = Math.Min(from, to), hi = Math.Max(from, to);
        byte a0 = brush.Color.A;
        Motion.Play(g, GhostMs, p =>
        {
            double q = lost ? (p < 0.25 ? 0 : Motion.Out((p - 0.25) / 0.75)) : Motion.Out(p);
            double right = lost ? hi - (hi - lo) * q : hi;
            g.Margin = new Thickness(w * lo, 0, 0, 0);
            g.Width = Math.Max(0, w * (right - lo));
            brush.Color = Color.FromArgb((byte)(a0 * (1 - (lost ? 0.6 * q : q))), brush.Color.R, brush.Color.G, brush.Color.B);
        }, () => g.Visibility = Visibility.Collapsed);
    }

    /// <summary>
    /// Your row's heat: none below <see cref="HeatFrom"/> of your best here; from there your row's gold frame glows warmer
    /// (amber, stronger as you near your best) with a faint warm wash across the row, and bright gold once you beat it. The
    /// state of your pace, drawn as it is (it moves with your DPS, so it needs no course of its own).
    /// </summary>
    static void SetHeat(RowEl r, double? pace)
    {
        var glow = r.FrameGlow;
        if (pace is not { } x || x < HeatFrom)
        {
            if (r.Heat.Tag == null) return;
            r.Heat.Visibility = Visibility.Collapsed; r.Heat.Tag = null;
            // the frame's own glow back (as built; the PB moment's own course puts it back itself)
            if (glow != null && !Motion.Playing(r.Pb)) { glow.Color = PK.Gold; glow.BlurRadius = r.FrameBlur0; glow.Opacity = r.FrameOpacity0; }
            return;
        }
        bool beat = x >= 1;
        double k = beat ? 1 : (x - HeatFrom) / (1 - HeatFrom);
        var warm = Color.FromRgb(0xFF, 0x7A, 0x3C);
        var c = beat ? Color.FromRgb(0xFF, 0xD0, 0x5A) : Mix(PK.Gold, warm, 0.35 + 0.65 * k);
        if (glow != null) { glow.Color = c; glow.BlurRadius = beat ? 26 : 16 + 8 * k; glow.Opacity = beat ? 0.95 : 0.5 + 0.3 * k; }
        var bg = (LinearGradientBrush)r.Heat.Background;
        bg.GradientStops[0].Color = Color.FromArgb((byte)(beat ? 0x40 : 0x14 + 0x20 * k), c.R, c.G, c.B);
        bg.GradientStops[1].Color = Color.FromArgb(0, c.R, c.G, c.B);
        ((SolidColorBrush)r.Heat.BorderBrush).Color = Color.FromArgb((byte)(beat ? 0xFF : 0x80 + 0x60 * k), c.R, c.G, c.B);
        r.Heat.Tag = beat ? "heat-beat" : "heat-warm";
        r.Heat.Visibility = Visibility.Visible;
    }

    static Color Mix(Color a, Color b, double t) => Color.FromArgb(255, (byte)(a.R + (b.R - a.R) * t), (byte)(a.G + (b.G - a.G) * t), (byte)(a.B + (b.B - a.B) * t));

    /// <summary>The end of a boss fight: its plate slides in from the right and a glint runs over it.</summary>
    public const double PlateMs = 380;

    static void SlideIn(Border plate)
    {
        if (!Motion.On || plate.Child is not UIElement word) return;
        var glint = new Border { IsHitTestVisible = false, HorizontalAlignment = HorizontalAlignment.Stretch };
        var band = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 0.4) };
        GradientStop a = new(Color.FromArgb(0, 0xFF, 0xFF, 0xFF), 0), m = new(Color.FromArgb(0, 0xFF, 0xFF, 0xFF), 0), b = new(Color.FromArgb(0, 0xFF, 0xFF, 0xFF), 0);
        band.GradientStops.Add(a); band.GradientStops.Add(m); band.GradientStops.Add(b);
        glint.Background = band;
        plate.Child = null;
        plate.Child = new Grid { Children = { word, glint } };
        plate.ClipToBounds = true;
        // on the finale's beat: after the blade, dropped in with a stamp, then the glint (Ui/ParserRows.Cinema.cs)
        PlateIn(plate, glint, a, m, b);
    }

    /// <summary>
    /// A big hit: "+858K" floats up off the member's row and fades, gold when the record says it was a crit. Never over the
    /// figures' columns, and at most <see cref="PopsPerRow"/> on a row and <see cref="PopsInAll"/> in all.
    /// </summary>
    void Pop(RowEl r, BigHit h)
    {
        if (!Motion.On) return;
        double tw = r.Track.ActualWidth;
        if (tw <= 0) return;
        // a full meter: a row that shows one already takes the new one in its place; any other waits for none
        if (_popsOn >= PopsInAll && r.Live.Count == 0) return;
        bool crit = h.Crit == true;
        var t = new TextBlock { Text = "+" + Amounts.Short(h.Amount), FontSize = 11.5, FontWeight = FontWeights.Bold, Tag = crit ? "pop-crit" : "pop",
            Foreground = crit ? HudKit.B(0xFFD36B) : HudKit.Ink, IsHitTestVisible = false, RenderTransformOrigin = new Point(0.5, 0.5),
            Effect = crit ? new DropShadowEffect { Color = Color.FromRgb(0x6B, 0x3A, 0x00), BlurRadius = 5, ShadowDepth = 0, Opacity = 1 } : TextHalo() };
        System.Windows.Documents.Typography.SetNumeralAlignment(t, FontNumeralAlignment.Tabular);
        // measured the way the meter draws it (its window sets Display; a block not yet in it would read Ideal, a pixel narrower on a 100% screen,
        // and two pop-ups on a row then sat 9 px apart, not the 10 they keep)
        TextOptions.SetTextFormattingMode(t, TextFormattingMode.Display);
        t.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        double pw = t.DesiredSize.Width, ph = t.DesiredSize.Height;
        // the open bar: from past the name's line (the name, YOU, the plates) to just short of the DPS's column; no room, no pop-up
        double lo = Math.Max(r.Cols.Margin.Left + 52, NameEnd(r) + 8), hi = DpsLeft(r, tw) - 6;
        if (hi - lo < pw) return;
        // next to the DPS first; else left of the one there with a clear gap; else the oldest gives way (never more than two a row)
        while (true)
        {
            double? x = null;
            if (r.Live.Count < PopsPerRow)
            {
                double right = hi;
                foreach (var o in r.Live) right = Math.Min(right, o.Left - PopGap);
                if (r.Live.Count == 0 || right - pw >= lo) x = right - pw;
            }
            if (x is double at) { Place(r, t, at, pw, ph, lo, hi); return; }
            if (r.Live.Count == 0) return;
            Drop(r, r.Live[0]);
        }
    }

    /// <summary>The space kept between two pop-ups on a row.</summary>
    public const double PopGap = 10;

    void Place(RowEl r, TextBlock t, double x, double pw, double ph, double lo, double hi)
    {
        double bandH = _rowH - 2;
        var band = new Canvas { Width = hi - lo, Height = bandH, ClipToBounds = true, IsHitTestVisible = false };
        Canvas.SetLeft(band, lo); Canvas.SetTop(band, r.Y);
        double top = Math.Round((bandH - ph) / 2) + 3;
        Canvas.SetLeft(t, x - lo); Canvas.SetTop(t, top);
        var st = new ScaleTransform(1, 1); var tt = new TranslateTransform();
        t.RenderTransform = new TransformGroup { Children = { st, tt } };
        band.Children.Add(t);
        _pops.Children.Add(band);
        var pop = new PopEl { Band = band, Text = t, Rise = tt, Left = x, Right = x + pw, Gen = _popGen };
        r.Live.Add(pop); _popsOn++;
        // up 6 px inside its own row (never into the row above or the header), in quickly, out over the second half
        Motion.Play(t, PopMs, p =>
        {
            tt.Y = -6 * Motion.Out(p);
            t.Opacity = p < 0.12 ? p / 0.12 : p > 0.5 ? 1 - (p - 0.5) / 0.5 : 1;
            st.ScaleX = st.ScaleY = p < 0.2 ? 0.85 + 0.15 * Motion.Out(p / 0.2) : 1;
        }, () => Drop(r, pop));
    }

    /// <summary>A pop-up away (its course over, or a newer one on its row in its place).</summary>
    void Drop(RowEl r, PopEl pop)
    {
        if (!r.Live.Remove(pop)) return;
        Motion.Halt(pop.Text);
        _pops.Children.Remove(pop.Band);
        if (pop.Gen == _popGen) _popsOn = Math.Max(0, _popsOn - 1);   // a pop-up of a fight drawn before counts for nothing now
    }

    /// <summary>Where the name's line ends across the row (the name, YOU, PB, the plates: what a pop-up must never cover).</summary>
    static double NameEnd(RowEl r)
    {
        var defs = r.Cols.ColumnDefinitions;
        double start = r.Cols.Margin.Left + (defs.Count > 1 ? defs[0].ActualWidth + defs[1].ActualWidth : 52);
        // the heal chip is on the name's line: a pop-up never covers it either
        return start + r.NameLine.DesiredSize.Width;
    }

    /// <summary>Where the DPS's column starts across the row (the figures after it too, expanded or not).</summary>
    static double DpsLeft(RowEl r, double trackWidth)
    {
        var defs = r.Cols.ColumnDefinitions;
        if (defs.Count < 4 || defs[2].ActualWidth <= 0) return trackWidth - r.Cols.Margin.Right - 46 - 60;
        return r.Cols.Margin.Left + defs[0].ActualWidth + defs[1].ActualWidth + defs[2].ActualWidth;
    }

    /// <summary>For the tests: the pop-ups on the rows now (their words), and whether any part of a row is moving.</summary>
    /// <summary>For the tests: each pop-up on screen, where its words are across and down the meter (rise included), its band, and its row's limits.</summary>
    internal List<(string Row, string Word, double Left, double Right, double Top, double Bottom, double BandTop, double BandBottom, double RowTop, double RowBottom, double NameEnd, double DpsLeft)> PopsForTest() =>
        _rows.Values.SelectMany(r => r.Live.Select(p =>
        {
            double bl = Canvas.GetLeft(p.Band), bt = Canvas.GetTop(p.Band), top = bt + Canvas.GetTop(p.Text) + p.Rise.Y;
            return (r.Name.Text, $"{p.Text.Tag}:{p.Text.Text}", bl + Canvas.GetLeft(p.Text), bl + Canvas.GetLeft(p.Text) + p.Text.ActualWidth, top, top + p.Text.ActualHeight, bt, bt + p.Band.Height, r.Y, r.Y + _rowH - 2, NameEnd(r), DpsLeft(r, r.Track.ActualWidth));
        })).ToList();
    internal string? HeatForTest() => _rows.Values.FirstOrDefault(r => r.You)?.Heat is { Visibility: Visibility.Visible } h ? h.Tag as string : null;
    internal bool AnyMotionForTest() => _pops.Children.Count > 0 || _sweep.Visibility == Visibility.Visible
        || _rows.Values.Any(r => r.Ghost.Visibility == Visibility.Visible || r.Odo.Visibility == Visibility.Visible || r.Odo.Rolling || Math.Abs(r.Lift.Y) > 0.01 || r.Rise.ScaleX != 1 || r.Root.Effect != null
            || r.Plates.Children.OfType<UIElement>().Any(p => p.Opacity < 1 || p.RenderTransform is TranslateTransform { X: not 0 }) || r.Root.Opacity < 1);
    /// <summary>For the tests: each row's chip glow (blur/opacity, "none"), by name order; and how many rows carry an effect of their own.</summary>
    internal List<string> ChipLooksForTest() => _rows.Values.OrderBy(r => r.Name.Text).Select(r => r.Chip.Effect is DropShadowEffect d ? $"{r.Name.Text}:{d.BlurRadius:0.#}/{d.Opacity:0.##}" : $"{r.Name.Text}:none").ToList();
    internal int RowEffectsForTest() => _rows.Values.Count(r => r.Root.Effect != null);
    internal bool LiftedForTest() => _rows.Values.Any(r => r.Rise.ScaleX > 1.001 && r.Root.Effect is DropShadowEffect);
    internal bool GhostForTest() => _rows.Values.Any(r => r.Ghost.Visibility == Visibility.Visible && r.Ghost.Width > 0.5);
    internal string? SweepForTest() => _sweep.Visibility == Visibility.Visible ? _sweep.Tag as string : null;
    internal bool FlareForTest() => _rows.Values.FirstOrDefault(r => r.You) is { } y && y.Dps.Foreground is SolidColorBrush fb && fb.Color != ((HudKit.Ink as SolidColorBrush)?.Color ?? Colors.White);
    /// <summary>For the tests: the name on the row whose top is at <paramref name="y"/>, and where its DPS's column starts.</summary>
    internal string RowNameAt(double y) => _rows.Values.FirstOrDefault(r => Math.Abs(r.Y - y) < 1)?.Name.Text ?? "";
    internal double DpsLeftAt(double y) => _rows.Values.FirstOrDefault(r => Math.Abs(r.Y - y) < 1) is { } r ? DpsLeft(r, r.Track.ActualWidth) : 0;
    /// <summary>For the tests: the DPS figure each shown row says, by name, and the odometer's (it must say the same once settled).</summary>
    internal Dictionary<string, (string Figure, string Odometer)> DpsForTest() => _rows.Values.Where(r => r.Root.Visibility == Visibility.Visible).ToDictionary(r => r.Name.Text, r => (r.Dps.Text, r.Odo.Text));

    /// <summary>A death: the coral tick lands (large, settling in) and the row washes coral once.</summary>
    void Land(RowEl r)
    {
        var st = new ScaleTransform(1, 1); r.Tick.RenderTransform = st;
        var wash = new Border { CornerRadius = new CornerRadius(3), IsHitTestVisible = false };
        if (r.Frame.Child is Grid g) g.Children.Add(wash); else return;
        if (!Motion.On) { g.Children.Remove(wash); return; }
        Motion.Play(r.Tick, 700, p =>
        {
            double s = 1 + 0.8 * (1 - Motion.Out(Math.Min(1, p * 2))); st.ScaleX = st.ScaleY = s;
            wash.Background = PK.C(PK.Coral, 0.35 * (1 - p));
        }, () => { st.ScaleX = st.ScaleY = 1; g.Children.Remove(wash); });
    }

    /// <summary>The fight's end: a light sweeps across the rows.</summary>
    public const double SweepMs = 400;

    /// <summary>
    /// The fight's end (a kill, a wipe, the open world's fight held after 5 quiet seconds): a light sweeps across the rows, mint
    /// for a kill or a clear, a soft coral for a wipe or a death, white for a fight that just ended.
    /// </summary>
    void Sweep(string tone)
    {
        if (!Motion.On) return;
        _sweep.Visibility = Visibility.Visible;
        var c = tone == "good" ? PK.Mint : tone == "bad" ? PK.Coral : Colors.White;
        byte peak = tone == "good" ? (byte)0x70 : tone == "bad" ? (byte)0x50 : (byte)0x40;
        // a blade of light: a long soft trail behind, a bright near-white core, a short soft lead (it reads as moving)
        var core = Color.FromRgb((byte)((c.R + 0xFF * 2) / 3), (byte)((c.G + 0xFF * 2) / 3), (byte)((c.B + 0xFF * 2) / 3));
        var band = new LinearGradientBrush { StartPoint = new Point(0, 0.35), EndPoint = new Point(1, 0.65) };
        var a = new GradientStop(Color.FromArgb(0, c.R, c.G, c.B), 0); var trail = new GradientStop(Color.FromArgb(0, c.R, c.G, c.B), 0);
        var m = new GradientStop(Color.FromArgb(0, core.R, core.G, core.B), 0); var b = new GradientStop(Color.FromArgb(0, c.R, c.G, c.B), 0);
        band.GradientStops.Add(a); band.GradientStops.Add(trail); band.GradientStops.Add(m); band.GradientStops.Add(b);
        _sweep.Background = band;
        _sweep.Tag = "sweep-" + tone;
        Motion.Play(_sweep, BladeMs, p =>
        {
            double at = -0.15 + 1.35 * Motion.Out(p), k = Math.Sin(Math.PI * Math.Min(1, p * 1.15));
            a.Offset = at - 0.42; trail.Offset = at - 0.1; m.Offset = at; b.Offset = at + 0.05;
            trail.Color = Color.FromArgb((byte)(peak * 0.45 * k), c.R, c.G, c.B);
            m.Color = Color.FromArgb((byte)(Math.Min(255, peak * 1.8) * k), core.R, core.G, core.B);
        }, () => _sweep.Visibility = Visibility.Collapsed);
    }

    /// <summary>A new personal best on this boss: "PB +4.2%" pops onto your row with a burst of gold.</summary>
    void ShowPb(RowEl r, double? d, double? heal = null)
    {
        var word = r.Pb.Child as TextBlock ?? (r.Pb.Child as Grid)?.Children.OfType<TextBlock>().FirstOrDefault();
        if (word != null) word.Text = PbText(d, heal);
        r.Pb.Visibility = Visibility.Visible;
        FitName(r);   // the chip takes room on the name's line: the name trims and YOU gives way, so a TOP DPS plate beside it shows whole
        if (!Motion.On) return;
        // last on the finale's beat, never on the plates: the chip drops in with a flash and a burst of gold sparks, your DPS
        // flares gold, your frame pulses (Ui/ParserRows.Cinema.cs; the odometer's flare went with it in 0.10.118)
        PbIn(r);
    }

    /// <summary>The death recap a hover on the tick shows (when the record carries the last hits).</summary>
    static FrameworkElement Recap(MemberView m)
    {
        var sp = new StackPanel();
        foreach (var d in m.DeathLog.Take(1))
        {
            sp.Children.Add(new TextBlock { Text = L.F("{0} died at {1}", m.F.Name, BossParser.Clock(TimeSpan.FromSeconds(d.At))), FontWeight = FontWeights.Bold, Foreground = PK.C(PK.Coral) });
            foreach (var h in d.Last) sp.Children.Add(new TextBlock { Text = L.F("−{0:0.0} s  {1}  {2}", d.At - h.At, h.Source, Amounts.Short(h.Amount)) + (h.HpAfter is { } hp ? $"  {Math.Round(hp * 100):0}%" : "") });
        }
        return sp;
    }
}
