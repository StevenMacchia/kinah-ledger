using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using KinahLedger.Parser;
using static KinahLedger.Ui.PK;
using Path = System.Windows.Shapes.Path;

namespace KinahLedger.Ui;

/// <summary>What the Parser page's views need from the window: its choices, and what a click does.</summary>
sealed class CommandCtx
{
    public string Tab = "Group";
    public string Sort = "Damage";
    public bool Details, Kills;
    /// <summary>The group's deaths shown (Settings › In game › Parser › Deaths, on by default).</summary>
    public bool Deaths = true;
    /// <summary>The group card's head (the Boss / All targets and Fight / Active switches) and foot (the coverage line), from the window.</summary>
    public Func<Encounter, FrameworkElement?> GroupHead = _ => null, GroupFoot = _ => null;
    public Action<string> SetTab = _ => { };
    public Action<string?> OpenFight = _ => { };
    public Action<long?> Inspect = _ => { };
    public Action<string> SetSort = _ => { };
    public Func<string, object?> Tip = s => s;
}

/// <summary>
/// The Parser page in Parser v2's Command look (Steven, 2026-11-15, the round-3 pick): a hero of glass with the group's DPS
/// as the big figure, your own card framed in gold beside it (your DPS, your place, against your best and last pull of this
/// boss), the boss's HP on the ring; tabs for what the record carries (Damage, Compare, Timeline, Taken); a dense table of the
/// group; a member's inspect view with every ability column the record has. A column or tab with no data behind it is not drawn.
/// </summary>
static class ParserCommand
{
    const double Wd = 630;
    static Brush Ink => K("Ink"); static Brush Muted => K("Muted"); static Brush Faint => K("Faint"); static Brush Text2 => K("Text2");
    static Brush Hair => K("CardHair"); static Brush Track => K("Bg");
    static string Rank(int r) => r switch { 1 => L.T("1st"), 2 => L.T("2nd"), 3 => L.T("3rd"), _ => L.F("{0}th", r) };
    static TextBlock Num(string s, double size, Brush b, FontWeight? w = null) { var t = T(s, size, b, w ?? FontWeights.SemiBold, tab: true); t.HorizontalAlignment = HorizontalAlignment.Right; return t; }
    static TextBlock Head(string text, bool sorted = false, bool right = true)
    {
        var t = T(L.T(text), 11, sorted ? GoldInk : Faint, FontWeights.SemiBold, trim: false);
        if (sorted) t.Inlines.Add(new System.Windows.Documents.Run(" ▾") { FontSize = 9 });
        if (right) t.HorizontalAlignment = HorizontalAlignment.Right;
        return t;
    }
    static Border YouTag() => new() { CornerRadius = new CornerRadius(5), Padding = new Thickness(5, 0, 5, 1), Margin = new Thickness(6, 0, 0, 0), Background = GoldInk, VerticalAlignment = VerticalAlignment.Center,
        Child = T(L.T("you"), 10, C(Color.FromRgb(0x1D, 0x13, 0)), FontWeights.Bold) };
    static Border Tag(string text, Color c) => new() { CornerRadius = new CornerRadius(5), Padding = new Thickness(5, 0, 6, 1), Margin = new Thickness(6, 0, 0, 0), Background = C(c, 0.16), BorderBrush = C(c, 0.6), BorderThickness = new Thickness(1), VerticalAlignment = VerticalAlignment.Center,
        Child = T(text, 10, C(c), FontWeights.Bold) };
    static Border Quiet(string text) => new() { Margin = new Thickness(6, 1, 0, 0), VerticalAlignment = VerticalAlignment.Center, Child = T(text, 11, Faint) };

    /// <summary>The tabs the record has something for: Damage always; Compare with a kept pull of this boss; Timeline and Taken once the record carries them.</summary>
    public static List<string> Tabs(FightView v)
    {
        var t = new List<string> { "Group" };
        if (v.HasCompare) t.Add("Compare");
        if (v.HasTimeline) t.Add("Timeline");
        if (v.HasTaken) t.Add("Taken");
        return t;
    }

    // ================================================================ the hero

    /// <param name="extras">What the mode adds under the figures (a run's pull under way and its splits, the open world's line).</param>
    public static Border Hero(FightView v, CommandCtx ctx, bool live, IEnumerable<FrameworkElement> extras)
    {
        var e = v.E;
        var body = new StackPanel();
        // the line: live dot or result, the boss (or the run, the fight), where, the clock and the outcome
        var top = Cols(-1, 0);
        var line = new StackPanel { Orientation = Orientation.Horizontal };
        if (live)
            line.Children.Add(new Border { Width = 7, Height = 7, CornerRadius = new CornerRadius(3.5), Margin = new Thickness(0, 1, 8, 0), Background = C(Mint), VerticalAlignment = VerticalAlignment.Center, Effect = new DropShadowEffect { Color = Mint, BlurRadius = 6, ShadowDepth = 0, Opacity = 0.8 } });
        var title = T(e.Title, 17, Ink, FontWeights.Bold); title.SetResourceReference(TextBlock.FontFamilyProperty, "DisplayFont");
        AutomationProperties.SetName(title, $"{(e.ModeKind == ParserMode.Run ? "Run" : e.ModeKind == ParserMode.Fights ? "Fight" : "Boss")} {e.Title}");
        line.Children.Add(title);
        var where = Where(e);
        if (where.Length > 0) line.Children.Add(M(T(where, 12.5, Muted), 10, 2, 0, 0));
        top.Children.Add(line);
        var right = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var clock = T(BossParser.Clock(e.Duration(v.Now)), 22, live ? GoldInk : Ink, FontWeights.Bold, tab: true, trim: false);
        AutomationProperties.SetName(clock, "Time " + BossParser.Clock(e.Duration(v.Now)));
        right.Children.Add(clock);
        if (!live) right.Children.Add(M(Outcome(e), 10, 0, 0, 0));
        top.Children.Add(Col(right, 1));
        body.Children.Add(top);

        // the figures: the group's DPS large; your own card; the boss's HP (or the result) on the ring
        var g = Cols(-1, 0, 0);
        g.Margin = new Thickness(0, 8, 0, 0);
        var grp = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        if (v.HasDamage)
        {
            grp.Children.Add(T(e.ModeKind == ParserMode.Run && (e.BossView || !e.TargetsSplit) ? L.T("Group DPS on boss pulls") : L.T("Group DPS"), 12, Muted, FontWeights.SemiBold));
            string gtext = v.GroupDpsOrNull is { } g0 ? Int(g0) : "–";   // under 3 s no figure yet
            var big = T(gtext, 50, Ink, FontWeights.Bold, tab: true, trim: false); big.SetResourceReference(TextBlock.FontFamilyProperty, "DisplayFont");
            if (live && v.GroupDpsOrNull is { } g1) Motion.Number(big, $"parser|gdps|{e.Id}", g1, x => Int(x), tintRise: -1);
            AutomationProperties.SetName(big, $"{gtext} group DPS");
            grp.Children.Add(big);
        }
        grp.Children.Add(Figures(v, ctx));
        g.Children.Add(grp);
        if (v.HasDamage && v.You is { Damage: > 0 } you)
        {
            var ys = new StackPanel();
            ys.Children.Add(T(L.F("You  ·  {0} of {1}", Rank(you.Rank), v.Members.Count(m => m.Damage > 0)), 12, GoldInk, FontWeights.Bold));
            var yd = T("", 30, GoldInk, FontWeights.Bold, tab: true, trim: false); yd.Inlines.Add(R(you.DpsOrNull is { } yv ? Int(yv) : "–", 30, GoldInk, FontWeights.Bold)); yd.Inlines.Add(R(" DPS", 12, GoldInk, FontWeights.SemiBold));
            ys.Children.Add(yd);
            // against your best kill of this boss and your last pull of it (kept on this PC)
            var cmp = T("", 11.5, Muted);
            if (v.Best != null) { double d = you.Dps / v.Best.Dps - 1; cmp.Inlines.Add(R(Signed(d), 11.5, C(d >= 0 ? Mint : Coral), FontWeights.Bold)); cmp.Inlines.Add(R(L.T(" best"), 11.5, Muted)); }
            if (v.Last != null) { double d = you.Dps / v.Last.Dps - 1; if (v.Best != null) cmp.Inlines.Add(R("  ·  ", 11.5, Faint)); cmp.Inlines.Add(R(Signed(d), 11.5, C(d >= 0 ? Mint : Coral), FontWeights.Bold)); cmp.Inlines.Add(R(L.T(" last"), 11.5, Muted)); }
            if (v.Best == null && v.Last == null) cmp.Inlines.Add(R(L.F("{0} of the group's damage", Pct(you.Share)), 11.5, Muted));
            ys.Children.Add(cmp);
            if (v.Best != null || v.Last != null) cmp.ToolTip = ctx.Tip(L.F("Against your {0}.", string.Concat(v.Best != null ? L.F("best kill of {0} ({1} DPS, {2:MMM d})", e.Boss, Int(v.Best.Dps), v.Best.When.ToLocalTime()) : "", v.Best != null && v.Last != null ? L.T(" and your ") : "", v.Last != null ? L.F("last pull of it ({0} DPS, {1:MMM d})", Int(v.Last.Dps), v.Last.When.ToLocalTime()) : "")));
            g.Children.Add(Col(M(YouFrame(ys, 12, new Thickness(12, 8, 14, 9)), 8, 0, 12, 0), 1));
        }
        if (Ring(v, live) is { } ring) g.Children.Add(Col(ring, 2));
        body.Children.Add(g);
        // the boss's HP, said once: the ring holds it (its share and the HP left); a fight with no ring (a run's, a fight's) keeps the bar
        if (live && e.BossHpLeft is { } hp && e.ModeKind != ParserMode.Run && Ring(v, live) == null)
        {
            var hw = T("", 12.5, Muted, trim: false); Typography(hw);
            hw.Inlines.Add(R($"{Math.Round(hp * 100):0}%", 13, hp < 0.34 ? C(Coral) : Ink, FontWeights.Bold));
            hw.Inlines.Add(R(L.F(" · {0} left", Amounts.Short(Math.Max(0, e.BossHp))), 12.5, Text2, FontWeights.SemiBold));
            hw.Inlines.Add(R(L.F(" of {0} HP", Amounts.Short(e.BossMaxHp)), 12.5, Muted));
            AutomationProperties.SetName(hw, $"Boss HP {Math.Round(hp * 100):0}%, {Amounts.Short(Math.Max(0, e.BossHp))} left of {Amounts.Short(e.BossMaxHp)}");
            body.Children.Add(M(hw, 0, 12, 0, 6));
            var bar = HpBar(hp, 9, Track, true);
            bar.Children.Add(HudKit.HpTicks());
            body.Children.Add(bar);
        }
        foreach (var x in extras) body.Children.Add(x);
        var tabs = Tabs(v);
        if (tabs.Count > 1) body.Children.Add(M(TabRow(tabs, ctx), 0, 14, 0, 0));
        var glow = live ? Gold : e.Tone == "good" ? Mint : e.Tone == "bad" ? Coral : Gold;
        var hero = Glass(body, new Thickness(16, 13, 16, 13), 14, glow: glow);
        hero.Tag = "parser-hero";
        return hero;
    }

    /// <summary>Why a record is partial, in the language in use: each reason the Parser wrote, translated ("3 moments the tracker missed" by its figure).</summary>
    public static string WhyText(Encounter e) => string.Join("; ", e.PartialWhy.Select(w =>
    {
        var gaps = System.Text.RegularExpressions.Regex.Match(w, @"^(\d+) moments the tracker missed$");
        if (gaps.Success) return L.F("{0} moments the tracker missed", int.Parse(gaps.Groups[1].Value, CultureInfo.InvariantCulture));
        var stale = System.Text.RegularExpressions.Regex.Match(w, @"^nothing heard of it for (\d+) minutes$");
        if (stale.Success) return L.F("nothing heard of it for {0:0} minutes", double.Parse(stale.Groups[1].Value, CultureInfo.InvariantCulture));
        return L.T(w);
    }));

    static void Typography(TextBlock t) => System.Windows.Documents.Typography.SetNumeralAlignment(t, FontNumeralAlignment.Tabular);

    /// <summary>"Ferocious Horn Den · Conquest · Expedition · final boss": the instance and the game's mode for it, the kind, the boss's place in it.</summary>
    public static string Where(Encounter e)
    {
        var place = BossBook.Strip(e.Instance);
        var parts = new List<string>();
        if (place.Length > 0 && place != e.Title && place != e.Kind) parts.Add(e.GameMode.Length > 0 ? $"{place} · {e.GameMode}" : place);
        else if (e.GameMode.Length > 0) parts.Add(e.GameMode);
        int i = e.Instance.IndexOf(" [", StringComparison.Ordinal);
        var stage = i > 0 ? e.Instance[(i + 2)..].TrimEnd(']') : "";
        if (e.Kind.Length > 0) parts.Add(stage.Length > 0 ? $"{e.Kind} {stage}" : e.Kind);
        if (e.ModeKind != ParserMode.Run && e.FinalBoss) parts.Add(L.T("final boss"));
        if (e.ModeKind == ParserMode.Run) parts.Add(L.F("{0} of {1} bosses down", e.Splits.Count(x => x.Outcome == "kill"), e.Splits.Select(x => x.Boss).Distinct().Count()));
        return string.Join(" · ", parts.Where(p => p.Length > 0));
    }

    public static Border Outcome(Encounter e)
    {
        var c = e.Tone switch { "good" => Mint, "bad" => Coral, "quiet" => Color.FromRgb(0x86, 0x8D, 0xA1), _ => Gold };
        return new Border { CornerRadius = new CornerRadius(7), Padding = new Thickness(8, 2, 8, 3), Background = C(c, 0.16), BorderBrush = C(c, 0.5), BorderThickness = new Thickness(1), VerticalAlignment = VerticalAlignment.Center,
            Child = T(e.OutcomeLabel, 11.5, C(c), FontWeights.SemiBold) };
    }

    /// <summary>The figures line under the big figure: damage, members, deaths, partial (each only when it's there).</summary>
    static FrameworkElement Figures(FightView v, CommandCtx ctx)
    {
        var e = v.E;
        var wp = new WrapPanel { Margin = new Thickness(0, 1, 0, 0) };
        int n0 = 0;
        void Add(string fig, string word, Brush? tone = null)
        {
            var t = T("", 12.5, Muted, trim: false); Typography(t);
            t.Inlines.Add(R(fig, 12.5, tone ?? Ink, FontWeights.Bold)); t.Inlines.Add(R(" " + L.T(word), 12.5, Muted));
            t.Margin = new Thickness(0, 0, 16, 0);
            n0++;
            wp.Children.Add(t);
        }
        if (v.HasDamage) Add(Amounts.Short(v.Members.Sum(m => m.Damage)), "damage");
        // a boss under way: its HP left of its whole (the ring holds the share)
        if (e.Live && e.ModeKind == ParserMode.Bosses && e.BossHpLeft != null && e.BossMaxHp > 0) Add(Amounts.Short(Math.Max(0, e.BossHp)), L.F("of {0} HP left", Amounts.Short(e.BossMaxHp)));
        if (v.Deaths > 0 && (ctx.Deaths || e.ModeKind == ParserMode.Fights)) Add(v.Deaths.ToString(CultureInfo.InvariantCulture), v.Deaths == 1 ? "death" : "deaths", C(Coral));
        int near = e.Kills.Count(k => !k.Boss && !k.Yours);
        if (ctx.Kills && near > 0 && e.ModeKind != ParserMode.Fights) Add(near.ToString(CultureInfo.InvariantCulture), "kills near you");
        if (e.ModeKind == ParserMode.Fights && e.YourKills > 0) Add(e.YourKills.ToString(CultureInfo.InvariantCulture), e.PlayerKills > 0 ? L.F(e.PlayerKills == 1 ? "kills, {0} player" : "kills, {0} players", e.PlayerKills) : e.YourKills == 1 ? "kill" : "kills", C(Mint));
        if (!v.HasDamage && ctx.Details && e.Members.Any(f => f.CombatPower > 0)) Add(((int)Math.Round(e.Members.Where(f => f.CombatPower > 0).Average(f => f.CombatPower))).ToString("N0", CultureInfo.InvariantCulture), "average CP");
        if (e.Partial)
        {
            var p = Tag(L.T("Partial"), Color.FromRgb(0xF7, 0xA0, 0x72));
            p.ToolTip = ctx.Tip(L.F("Some of it wasn't seen: {0}. Its damage and kills may be short.", WhyText(e)));
            wp.Children.Add(M(p, n0 > 0 ? 4 : 0, 0, 0, 0));
        }
        return wp;
    }

    /// <summary>The ring: the boss's HP left while a boss fight lasts; a kill's time in mint, a wipe's HP left in coral; none for a run or a fight.</summary>
    static FrameworkElement? Ring(FightView v, bool live)
    {
        var e = v.E;
        if (e.ModeKind != ParserMode.Bosses) return null;
        if (live && e.BossHpLeft is { } hp) { var r = HpRing(108, 10, hp, hp < 0.34 ? Coral : Gold, $"{Math.Round(hp * 100):0}%", L.T("HP left")); AutomationProperties.SetName(r, $"Boss HP {Math.Round(hp * 100):0}%, {Amounts.Short(Math.Max(0, e.BossHp))} left of {Amounts.Short(e.BossMaxHp)}"); return r; }
        if (!live && e.Outcome == "kill") return HpRing(108, 10, 1, Mint, BossParser.Clock(e.Duration(v.Now)), L.T("killed"));
        if (!live && e.Outcome == "wipe" && e.BossHpLeft is { } w) return HpRing(108, 10, w, Coral, $"{Math.Round(w * 100):0}%", L.T("HP left"));
        return null;
    }

    public static Grid HpRing(double size, double stroke, double share, Color tone, string big, string small)
    {
        var g = new Grid { Width = size, Height = size, VerticalAlignment = VerticalAlignment.Center };
        g.Children.Add(new Ellipse { Width = size - stroke, Height = size - stroke, StrokeThickness = stroke, Stroke = K("Line"), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center });
        double s = Math.Clamp(share, 0, 1);
        if (s >= 0.999)
            g.Children.Add(new Ellipse { Width = size - stroke, Height = size - stroke, StrokeThickness = stroke, Stroke = C(tone), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Effect = new DropShadowEffect { Color = tone, BlurRadius = 12, ShadowDepth = 0, Opacity = 0.55 } });
        else if (s > 0.004)
        {
            double c = size / 2, r = (size - stroke) / 2, a = 2 * Math.PI * s;
            var fig = new PathFigure { StartPoint = new Point(c, c - r) };
            fig.Segments.Add(new ArcSegment(new Point(c + r * Math.Sin(a), c - r * Math.Cos(a)), new Size(r, r), 0, a > Math.PI, SweepDirection.Clockwise, true));
            g.Children.Add(new Path { Data = new PathGeometry(new[] { fig }), Stroke = C(tone), StrokeThickness = stroke, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round, Effect = new DropShadowEffect { Color = tone, BlurRadius = 12, ShadowDepth = 0, Opacity = 0.55 } });
        }
        var sp = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        var b = T(big, big.Length > 4 ? 17 : 20, Ink, FontWeights.Bold, tab: true, trim: false); b.HorizontalAlignment = HorizontalAlignment.Center; sp.Children.Add(b);
        var sm = T(small.ToUpperInvariant(), 9.5, Muted, FontWeights.SemiBold, trim: false); sm.HorizontalAlignment = HorizontalAlignment.Center; sp.Children.Add(sm);
        g.Children.Add(sp);
        AutomationProperties.SetName(g, $"{big} {small}");
        return g;
    }

    static FrameworkElement TabRow(List<string> tabs, CommandCtx ctx)
    {
        var sp = new StackPanel { Orientation = Orientation.Horizontal };
        foreach (var t in tabs)
        {
            bool lit = t == ctx.Tab;
            var b = new Border { CornerRadius = new CornerRadius(8), Padding = new Thickness(12, 4, 12, 5), Margin = new Thickness(0, 0, 4, 0), BorderThickness = new Thickness(1), Cursor = Cursors.Hand, Focusable = true,
                Background = lit ? K("Card2") : Brushes.Transparent, BorderBrush = lit ? K("LineStrong") : Brushes.Transparent,
                Child = T(L.T(t), 12.5, lit ? Ink : Muted, lit ? FontWeights.Bold : FontWeights.SemiBold) };
            AutomationProperties.SetName(b, $"Parser view: {t}");
            var pick = t;
            b.MouseLeftButtonUp += (_, _) => ctx.SetTab(pick);
            b.KeyDown += (_, k) => { if (k.Key is Key.Enter or Key.Space) { k.Handled = true; ctx.SetTab(pick); } };
            if (!lit) { b.MouseEnter += (_, _) => b.Background = K("HoverTint"); b.MouseLeave += (_, _) => b.Background = Brushes.Transparent; }
            sp.Children.Add(b);
        }
        return sp;
    }

    // ================================================================ the group

    /// <summary>
    /// The group, dense: a rank chip in the class's colour, the member, then DPS and share; damage with Group details or in a
    /// kept fight; crit, max hit and the DPS over time once the record carries them. Your row in the gold frame; a death tags the
    /// row. A click opens the member's inspect view.
    /// </summary>
    public static Border Group(FightView v, CommandCtx ctx, bool live, bool detail)
    {
        var e = v.E;
        var body = new StackPanel();
        if (v.Members.Count == 0) { body.Children.Add(T(L.T("The group wasn't seen."), 12, Faint)); return Glass(body, new Thickness(14, 12, 14, 12), 14); }
        bool dmg = v.HasDamage, dmgCol = dmg && (detail || ctx.Details), crit = v.HasCrit, max = v.HasMax, spark = v.HasTimeline;
        var widths = new List<double> { 30, 38, -1 };
        if (dmgCol) widths.Add(70);
        if (dmg) { widths.Add(70); widths.Add(56); }
        if (crit) widths.Add(52);
        if (max) widths.Add(62);
        if (spark) widths.Add(80);
        widths.Add(18);
        Grid Row() => Cols(widths.ToArray());
        int col = 3;
        var h = Row(); h.Margin = new Thickness(10, 0, 10, 6);
        h.Children.Add(Col(Head(dmg ? "Member" : "Member", right: false), 2));
        int cDmg = -1, cDps = -1, cShare = -1, cCrit = -1, cMax = -1, cSpark = -1;
        if (dmgCol) h.Children.Add(Col(Head("Damage"), cDmg = col++));
        if (dmg) { h.Children.Add(Col(Head("DPS", sorted: true), cDps = col++)); h.Children.Add(Col(Head("Share"), cShare = col++)); }
        if (crit) h.Children.Add(Col(Head("Crit"), cCrit = col++));
        if (max) h.Children.Add(Col(Head("Max hit"), cMax = col++));
        if (spark) h.Children.Add(Col(Head("Over time"), cSpark = col++));
        int cChev = col;
        // what the figures are, one click away (the honest pass): Boss / All targets and Fight / Active
        if (ctx.GroupHead(e) is { } gh) { gh.HorizontalAlignment = HorizontalAlignment.Right; body.Children.Add(M(gh, 10, 0, 10, 8)); }
        body.Children.Add(h);
        var rows = (live && dmg ? v.Members.Where(m => m.Damage > 0) : v.Members).ToList();
        double top = Math.Max(1, rows.Max(m => dmg ? (double)m.Damage : m.F.CombatPower));
        double smax = spark ? rows.Max(m => m.PerSecond is { Count: > 0 } p ? Smooth(p.Select(x => (double)x).ToList()).Max() : 0) : 0;
        for (int i = 0; i < rows.Count; i++)
        {
            var m = rows[i];
            var g = Row(); g.MinHeight = ctx.Details ? 46 : 40;
            if (dmg) g.Children.Add(RankChip(m.Rank, m.F.Class, 22, you: m.You));
            g.Children.Add(Col(ClassArt.Icon(m.F.Class, 30, m.F.Name), 1));
            var nameBox = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) };
            var nl = new StackPanel { Orientation = Orientation.Horizontal };
            var nm = T(ParserRows.NameOf(m.F), 13.5, m.F.Name.Length > 0 || m.F.You ? ClassArt.NameBrush(m.F.Class, Dark) : Muted, FontWeights.SemiBold);
            nl.Children.Add(nm);
            if (m.You) nl.Children.Add(YouTag());
            if (m.Deaths > 0 && ctx.Deaths) { var dt = Tag(m.Deaths == 1 ? L.T("✕ died") : L.F("✕ {0} deaths", m.Deaths), Coral); if (m.DeathLog.Count > 0) dt.ToolTip = DeathTip(m); nl.Children.Add(dt); }
            // joined late or left, whether or not damage was dealt by then; when, on hover
            if (m.F.Late) { var q = Quiet(L.T("joined late")); if (m.F.JoinedAt is { } ja) q.ToolTip = L.F("Joined {0} in", BossParser.Clock(ja - e.Start)); nl.Children.Add(q); }
            if (m.F.Gone) { var q = Quiet(L.T("left")); if (m.F.LeftAt is { } la) q.ToolTip = L.F("Left {0} in", BossParser.Clock(la - e.Start)); nl.Children.Add(q); }
            if (dmg && m.Damage <= 0) nl.Children.Add(Quiet(L.T("no damage")));
            nameBox.Children.Add(nl);
            if (ctx.Details)
            {
                var bits = new List<string>();
                if (m.F.Class.Length > 0) bits.Add(ClassArt.Title(m.F.Class));
                if (m.F.CombatPower > 0) bits.Add(L.F("{0} CP", m.F.CombatPower.ToString("N0", CultureInfo.InvariantCulture)));
                if (m.F.ItemLevel > 0 && !(crit || max || spark)) bits.Add(L.F("{0} item level", m.F.ItemLevel.ToString("N0", CultureInfo.InvariantCulture)));
                if (bits.Count > 0) nameBox.Children.Add(T(string.Join(" · ", bits), 11, Muted));
            }
            if (dmg)
            {
                var bar = Bar(m.Damage / top, m.You ? Gold : ClassArt.ColorOf(m.F.Class), 3, Track, glow: m.You);
                bar.Margin = new Thickness(0, 4, 0, 0);
                nameBox.Children.Add(bar);
            }
            g.Children.Add(Col(nameBox, 2));
            if (cDmg >= 0) g.Children.Add(Col(Num(Amounts.Short(m.Damage), 12.5, Text2), cDmg));
            if (cDps >= 0)
            {
                string dtext = m.DpsOrNull is { } dv ? Int(dv) : "–";   // under 3 s no figure yet
                var d = Num(dtext, 16.5, m.You ? GoldInk : Ink, FontWeights.Bold);
                if (live && m.DpsOrNull is { } dv2) Motion.Number(d, $"parser|dps|{e.Id}|{m.F.Id}", dv2, x => Int(x), tintRise: -1);
                d.ToolTip = L.F("{0} damage in {1}{2}{3}", Amounts.Short(m.Damage), BossParser.Clock(TimeSpan.FromSeconds(e.DpsSeconds(m.F, v.Now))), Encounter.ActiveBasis ? L.T(" active") : "", e.BossView ? L.T(", to the boss") : "");
                AutomationProperties.SetName(d, $"{dtext} DPS, {Pct(m.Share)} of the damage");
                g.Children.Add(Col(d, cDps));
                g.Children.Add(Col(Num(Pct(m.Share), 12.5, Text2), cShare));
            }
            if (cCrit >= 0) g.Children.Add(Col(Num(m.Crit is { } cr ? Pct(cr) : "–", 12.5, Text2), cCrit));
            if (cMax >= 0) g.Children.Add(Col(Num(m.MaxHit is { } mx ? Amounts.Short(mx) : "–", 12.5, Text2), cMax));
            if (cSpark >= 0 && m.PerSecond is { Count: > 1 } ps)
            {
                var sp = Spark(Smooth(ps.Select(x => (double)x).ToList()), 72, 26, m.You ? Gold : ClassArt.ColorOf(m.F.Class), smax, m.You ? 1.8 : 1.3, glow: m.You);
                sp.HorizontalAlignment = HorizontalAlignment.Right; g.Children.Add(Col(sp, cSpark));
            }
            bool canOpen = dmg && m.Abilities.Count > 0;
            if (canOpen)
            {
                var chev = new Path { Data = Geometry.Parse("M0,0 L3.5,3.5 L0,7"), StrokeThickness = 1.5, Stroke = Faint, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round };
                g.Children.Add(Col(chev, cChev));
            }
            FrameworkElement row;
            if (m.You) row = M(YouFrame(g, 10, new Thickness(9, 2, 9, 2)), 0, 2, 0, 2);
            else row = new Border { Padding = new Thickness(10, 2, 10, 2), CornerRadius = new CornerRadius(10), Background = Brushes.Transparent, Child = g, Opacity = dmg && m.Damage <= 0 ? 0.6 : 1 };
            row.Tag = "parser-member";
            if (canOpen)
            {
                row.Cursor = Cursors.Hand;
                AutomationProperties.SetName(row, $"{m.F.Name}: open their abilities");
                long id = m.F.Id;
                row.MouseLeftButtonUp += (_, ev) => { ev.Handled = true; ctx.Inspect(id); };
                if (row is Border rb && !m.You) { rb.MouseEnter += (_, _) => rb.Background = K("HoverTint"); rb.MouseLeave += (_, _) => rb.Background = Brushes.Transparent; }
            }
            body.Children.Add(row);
            if (i < rows.Count - 1 && !m.You && !rows[i + 1].You) body.Children.Add(M(new Border { Height = 1, Background = Hair }, 10, 0, 10, 0));
        }
        // how much of the boss's HP the rows hold (no "Other" row), the rest and why on hover
        if (ctx.GroupFoot(e) is { } gf) body.Children.Add(M(gf, 12, 8, 12, 0));
        return Glass(body, new Thickness(6, 12, 6, 10), 14);
    }

    /// <summary>The death recap a hover on a death tag shows: when, the killing blow, the last five hits and the HP after each.</summary>
    static FrameworkElement DeathTip(MemberView m)
    {
        var sp = new StackPanel { MaxWidth = 300 };
        foreach (var d in m.DeathLog.Take(2))
        {
            sp.Children.Add(T(L.F("Died at {0}{1}", BossParser.Clock(TimeSpan.FromSeconds(d.At)), d.Killer.Length > 0 ? $" · {d.Killer}" : ""), 12, C(Coral), FontWeights.Bold));
            foreach (var h in d.Last)
                sp.Children.Add(T(L.F("−{0:0.0} s  {1}  {2}{3}", d.At - h.At, h.Source, Int(h.Amount), h.HpAfter is { } hp ? L.F("  ({0:0}% left)", Math.Round(hp * 100)) : ""), 11.5, Ink));
        }
        return sp;
    }

    // ================================================================ a member's inspect view

    public static FrameworkElement Inspect(FightView v, MemberView m, CommandCtx ctx)
    {
        var e = v.E;
        var page = new StackPanel();
        var back = T($"‹  {e.Title}", 12.5, K("Link"), FontWeights.SemiBold); back.Margin = new Thickness(2, 0, 0, 8); back.Cursor = Cursors.Hand;
        back.MouseLeftButtonUp += (_, _) => ctx.Inspect(null);
        AutomationProperties.SetName(back, "Back to the group");
        page.Children.Add(back);
        var c = ClassArt.ColorOf(m.F.Class);
        // the head: the class's emblem as art, large and faint, behind the member
        var head = new Grid { ClipToBounds = true };
        if (ClassArt.Image(m.F.Class) is { } art)
            head.Children.Add(new Image { Source = art, Width = 250, Height = 250, Opacity = Dark ? 0.12 : 0.08, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, -50, 140, -80), IsHitTestVisible = false });
        var hs = new StackPanel();
        var g = Cols(70, -1, 0);
        g.Children.Add(ClassArt.Icon(m.F.Class, 56, m.F.Name));
        var mid = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        var nm = new StackPanel { Orientation = Orientation.Horizontal };
        var name = T(m.F.Name, 24, ClassArt.NameBrush(m.F.Class, Dark), FontWeights.Bold); name.SetResourceReference(TextBlock.FontFamilyProperty, "DisplayFont");
        nm.Children.Add(name);
        if (m.You) nm.Children.Add(YouTag());
        mid.Children.Add(nm);
        var who = new List<string>();
        if (m.F.Class.Length > 0) who.Add(ClassArt.Title(m.F.Class));
        if (m.F.Server.Length > 0) who.Add(m.F.Server);
        if (m.F.ItemLevel > 0) who.Add(L.F("{0} item level", m.F.ItemLevel.ToString("N0", CultureInfo.InvariantCulture)));
        if (m.F.CombatPower > 0) who.Add(L.F("{0} Combat Power", m.F.CombatPower.ToString("N0", CultureInfo.InvariantCulture)));
        if (who.Count > 0) mid.Children.Add(T(string.Join(" · ", who), 12.5, Muted));
        g.Children.Add(Col(mid, 1));
        var big = new StackPanel { HorizontalAlignment = HorizontalAlignment.Right };
        var d = T("", 40, Ink, FontWeights.Bold, tab: true, trim: false); d.Inlines.Add(R(m.DpsOrNull is { } mv ? Int(mv) : "–", 40, m.You ? GoldInk : Ink, FontWeights.Bold)); d.Inlines.Add(R(" DPS", 14, Muted, FontWeights.SemiBold));
        d.HorizontalAlignment = HorizontalAlignment.Right; big.Children.Add(d);
        int shown = v.Members.Count(x => x.Damage > 0);
        big.Children.Add(Num(L.F("{0} of {1} · {2} of the group", Rank(m.Rank), shown, Pct(m.Share)), 12.5, Muted, FontWeights.Medium));
        g.Children.Add(Col(big, 2));
        hs.Children.Add(g);
        // the figures a min-maxer reads first: what the record has
        // the damage as the figures count it, with the adds beside it when the boss's is apart (the honest pass)
        var tiles = new List<(string Label, string Value, Brush Tone)> { (e.BossView ? "To the boss" : "Damage", Amounts.Short(m.Damage), Ink) };
        if (e.HasAdds) tiles.Add(("To adds", Amounts.Short(Math.Max(0, m.F.Damage - m.F.BossDamage)), Ink));
        // hits as counted (none made up), the average over the counted ones only
        int hits = m.Abilities.Sum(a => a.Hits);
        if (hits > 0) tiles.Add(("Hits", hits.ToString("N0", CultureInfo.InvariantCulture), Ink));
        if (hits > 0) tiles.Add(("Average hit", Amounts.Short(m.Abilities.Sum(a => a.Average * a.Hits) / hits), Ink));
        // crit: its rate, with the crits' share of the damage in the label; the biggest crit in place of the biggest hit when known
        if (m.Crit is { } cr) tiles.Add(("Crit", Pct(cr), Ink));
        if (m.CritShare is { } cs) tiles.Add(("Crit damage", Pct(cs, 0), Ink));
        if (m.Back is { } ba) tiles.Add(("Back attacks", Pct(ba), Ink));
        if (m.MaxCrit is { } mc && mc > 0) tiles.Add(("Max crit", Amounts.Short(mc), Ink));
        else if (m.MaxHit is { } mx) tiles.Add(("Max hit", Amounts.Short(mx), Ink));
        if (m.Abilities.Any(a => a.Casts != null)) tiles.Add(("Casts", m.Abilities.Sum(a => a.Casts ?? 0).ToString("N0", CultureInfo.InvariantCulture), Ink));
        // active time and the idle share of their time in the group (a late joiner's from joining, a leaver's to leaving)
        if (m.F.FirstHit != null) tiles.Add(("Active", BossParser.Clock(TimeSpan.FromSeconds(m.ActiveSeconds)), Ink));
        if (m.F.FirstHit != null && e.IdleShare(m.F, v.Now) is { } idle) tiles.Add(("Idle", $"{Math.Round(idle * 100):0}%", Ink));
        if (ctx.Deaths) tiles.Add(("Deaths", m.Deaths.ToString(CultureInfo.InvariantCulture), m.Deaths > 0 ? C(Coral) : Ink));
        // every tile the record has, in one row up to six, else two even rows (none dropped: active time and deaths stay)
        var tg = new System.Windows.Controls.Primitives.UniformGrid { Columns = tiles.Count <= 6 ? tiles.Count : (tiles.Count + 1) / 2, Margin = new Thickness(-4, 14, -4, 0) };
        foreach (var (l, val, tone) in tiles)
            tg.Children.Add(new Border { CornerRadius = new CornerRadius(9), Padding = new Thickness(10, 6, 10, 7), Margin = new Thickness(4, 0, 4, 0), Background = K("CardQuiet"), BorderBrush = Hair, BorderThickness = new Thickness(1),
                Child = Stat(L.T(l), T(val, 17, tone, FontWeights.Bold, tab: true), "", Muted, Faint) });
        hs.Children.Add(tg);
        if (m.PerSecond is { Count: > 1 } ps)
        {
            var sl = Cols(-1, 0); sl.Margin = new Thickness(0, 12, 0, 4);
            var series = Smooth(ps.Select(x => (double)x).ToList());
            int peakAt = Array.IndexOf(series, series.Max());
            sl.Children.Add(T(L.T("DPS over the fight"), 11.5, Muted, FontWeights.SemiBold)); sl.Children.Add(Col(T(L.F("peak {0} at {1}", Int(series.Max()), BossParser.Clock(TimeSpan.FromSeconds(peakAt))), 11.5, Faint), 1));
            hs.Children.Add(sl);
            hs.Children.Add(Spark(series, Wd - 2, 46, m.You ? Gold : c, 0, 1.8, glow: true));
        }
        head.Children.Add(hs);
        page.Children.Add(Glass(head, new Thickness(16, 14, 16, 14), 14, glow: c));
        page.Children.Add(M(Abilities(v, m, ctx), 0, 12, 0, 0));
        if (m.Buffs is { Count: > 0 } buffs) page.Children.Add(M(Buffs(m, buffs), 0, 12, 0, 0));
        return page;
    }

    /// <summary>Each buff's uptime over the fight (once the record carries it): its name, a bar of its share of the fight, the time.</summary>
    static Border Buffs(MemberView m, List<(string Name, double Seconds, double Share)> buffs)
    {
        var body = new StackPanel();
        var head = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(2, 0, 0, 8) };
        head.Children.Add(T(L.T("Buff uptime"), 13, Ink, FontWeights.SemiBold)); head.Children.Add(M(T(L.T("time up over the fight"), 12, Muted), 8, 0, 0, 0));
        body.Children.Add(head);
        foreach (var (name, secs, share) in buffs.OrderByDescending(b => b.Share).Take(10))
        {
            var r = Cols(190, -1, 56, 52); r.MinHeight = 28;
            r.Children.Add(T(name, 12.5, Ink, FontWeights.SemiBold));
            r.Children.Add(Col(M(Bar(share, share >= 0.8 ? Mint : share >= 0.5 ? Gold : Coral, 6, Track, glow: false), 0, 0, 12, 0), 1));
            r.Children.Add(Col(Num(Pct(share, 0), 12.5, Ink, FontWeights.Bold), 2));
            r.Children.Add(Col(Num(BossParser.Clock(TimeSpan.FromSeconds(secs)), 12, Text2), 3));
            body.Children.Add(r);
        }
        return Glass(body, new Thickness(14, 12, 14, 10), 14);
    }

    /// <summary>Every ability: its icon and name, damage, share, DPS, hits, average; casts, crit, max and min once the record carries them. A click on a column's name sorts by it.</summary>
    static Border Abilities(FightView v, MemberView m, CommandCtx ctx)
    {
        var body = new StackPanel();
        bool casts = m.Abilities.Any(a => a.Casts != null), crit = m.Abilities.Any(a => a.Crit != null), max = m.Abilities.Any(a => a.Max != null), min = m.Abilities.Any(a => a.Min != null);
        var cols = new List<(string Key, double W, Func<AbilityView, double> By, Func<AbilityView, string> Text)>
        {
            ("Damage", 62, a => a.Damage, a => Amounts.Short(a.Damage)),
            ("Share", 52, a => a.Share, a => Pct(a.Share)),
            ("DPS", 54, a => a.Dps, a => a.DpsOrNull is { } x ? Int(x) : "–"),
        };
        if (casts) cols.Add(("Casts", 46, a => a.Casts ?? 0, a => a.Casts?.ToString(CultureInfo.InvariantCulture) ?? "–"));
        cols.Add(("Hits", 44, a => a.Hits, a => a.Hits > 0 ? a.Hits.ToString("N0", CultureInfo.InvariantCulture) : "–"));   // a count that didn't come: "–", never a made-up 1
        if (crit) cols.Add(("Crit", 48, a => a.Crit ?? 0, a => a.Crit is { } x ? Pct(x) : "–"));
        if (m.Abilities.Any(a => a.Back != null)) cols.Add(("Back", 48, a => a.Back ?? 0, a => a.Back is { } x ? Pct(x) : "–"));
        if (max) cols.Add(("Max", 56, a => a.Max ?? 0, a => a.Max is { } x ? Amounts.Short(x) : "–"));
        if (min) cols.Add(("Min", 52, a => a.Min ?? 0, a => a.Min is { } x ? Amounts.Short(x) : "–"));
        cols.Add(("Average", 60, a => a.Average, a => a.Avg is { } x ? Amounts.Short(x) : "–"));
        Grid Row() => Cols(new[] { 38.0, -1 }.Concat(cols.Select(x => x.W)).ToArray());
        var sortCol = cols.FirstOrDefault(x => x.Key == ctx.Sort);
        if (sortCol.Key == null) sortCol = cols[0];
        var h = Row(); h.Margin = new Thickness(8, 0, 8, 6);
        h.Children.Add(Col(Head("Ability", right: false), 1));
        for (int i = 0; i < cols.Count; i++)
        {
            var hd = Head(cols[i].Key, sorted: cols[i].Key == sortCol.Key);
            hd.Cursor = Cursors.Hand; hd.Background = Brushes.Transparent;
            var key = cols[i].Key;
            hd.MouseLeftButtonUp += (_, _) => ctx.SetSort(key);
            AutomationProperties.SetName(hd, $"Sort the abilities by {key}");
            h.Children.Add(Col(hd, i + 2));
        }
        body.Children.Add(h);
        var list = m.Abilities.OrderByDescending(sortCol.By).ToList();
        double top = Math.Max(1, m.Abilities.Max(a => (double)a.Damage));
        var c = ClassArt.ColorOf(m.F.Class);
        for (int i = 0; i < list.Count; i++)
        {
            var a = list[i];
            var r = Row(); r.MinHeight = 40;
            r.Children.Add(SkillArt.Icon(a.Skill, m.F.Class, 28));
            var nb = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0) };
            var an = T(a.Name, 13, a.Skill == 0 ? Muted : Ink, FontWeights.SemiBold);
            an.TextTrimming = TextTrimming.CharacterEllipsis;
            if (a.Kinds is { Count: > 0 } kinds) an.ToolTip = string.Join(" · ", kinds.Select(k => $"{k.Value} {k.Key}"));
            // the specialties its hits named, after the name, as a chip that opens what each does (Steven, 2026-11-16: "make
            // these clickable in the app so u can see which specialties are grabbed"): "Pummel  [SPEC 3·4 ▾]"
            if (a.Picks != 0)
            {
                var line = new Grid();
                line.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                line.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                line.Children.Add(an);
                var chip = SpecChip(a.Skill, a.Name, a.Picks);
                Grid.SetColumn(chip, 1); line.Children.Add(chip);
                // the name takes what the chip leaves (Auto then *: the chip is never the one cut)
                line.ColumnDefinitions[0].Width = GridLength.Auto; an.MaxWidth = 150;
                nb.Children.Add(line);
            }
            else nb.Children.Add(an);
            var bar = Bar(a.Damage / top, c, 3, Track, glow: false); bar.Margin = new Thickness(0, 4, 0, 0);
            nb.Children.Add(bar);
            r.Children.Add(Col(nb, 1));
            for (int k = 0; k < cols.Count; k++)
                r.Children.Add(Col(Num(cols[k].Text(a), k == 0 ? 13 : 12, k == 0 ? Ink : Text2, k == 0 ? FontWeights.Bold : FontWeights.SemiBold), k + 2));
            body.Children.Add(new Border { Padding = new Thickness(8, 0, 8, 0), Child = r });
            if (i < list.Count - 1) body.Children.Add(M(new Border { Height = 1, Background = Hair }, 8, 0, 8, 0));
        }
        return Glass(body, new Thickness(6, 12, 6, 10), 14);
    }

    /// <summary>
    /// The specialties chip after an ability's name: "SPEC 4·5 ▾" in gold. A click opens a card of the skill's five
    /// specializations, the ones its hits named lit (their effect and the skill level each needs) and the others dim; its
    /// tooltip says the picked ones' effects too. Without data for the skill, the chip still says the numbers.
    /// </summary>
    static Border SpecChip(int skill, string name, int picks)
    {
        var all = Specialties.Of(skill);
        var picked = Enumerable.Range(1, 5).Where(p => (picks & (1 << p)) != 0).ToList();
        var label = new TextBlock { FontSize = 10.5, FontWeight = FontWeights.Bold, Foreground = GoldInk, VerticalAlignment = VerticalAlignment.Center,
            Text = L.T("SPEC") + " " + KinahLedger.Parser.Abilities.PicksText(picks) + (all.Count > 0 ? "  ▾" : "") };
        var chip = new Border
        {
            Tag = "spec-chip", CornerRadius = new CornerRadius(5), Padding = new Thickness(6, 1, 6, 2), Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center,
            Background = C(Color.FromRgb(0xF5, 0xB9, 0x42), 0.12), BorderBrush = C(Color.FromRgb(0xF5, 0xB9, 0x42), 0.5), BorderThickness = new Thickness(1), Child = label,
            Cursor = all.Count > 0 ? Cursors.Hand : null,
        };
        AutomationProperties.SetName(chip, L.F("Specialties {0}", KinahLedger.Parser.Abilities.PicksText(picks)));
        if (all.Count == 0) { chip.ToolTip = L.T("No details for this skill's specializations yet"); return chip; }
        chip.ToolTip = string.Join("\n", all.Where(s => picked.Contains(s.N)).Select(s => $"{L.F("Specialization {0}", s.N)}: {s.Text}"));
        chip.MouseLeftButtonUp += (_, e) =>
        {
            e.Handled = true;
            var pop = new System.Windows.Controls.Primitives.Popup
            {
                PlacementTarget = chip, Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom, VerticalOffset = 4, StaysOpen = false,
                AllowsTransparency = true, PopupAnimation = System.Windows.Controls.Primitives.PopupAnimation.Fade, Child = SpecCard(name, all, picked),
            };
            pop.IsOpen = true;
        };
        return chip;
    }

    /// <summary>The card a specialties chip opens: the skill's name, then its five specializations, the picked ones lit.</summary>
    internal static Border SpecCard(string name, IReadOnlyList<Specialty> all, IReadOnlyList<int> picked)
    {
        var sp = new StackPanel { Width = 300 };
        sp.Children.Add(T(name, 13.5, Ink, FontWeights.Bold));
        sp.Children.Add(M(T(L.T("Specialties picked are lit"), 11, Faint, FontWeights.Medium), 0, 1, 0, 6));
        foreach (var s in all)
        {
            bool on = picked.Contains(s.N);
            var row = new Grid { Margin = new Thickness(0, 4, 0, 0), Opacity = on ? 1 : 0.45 };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(26) });
            row.ColumnDefinitions.Add(new ColumnDefinition());
            var num = new Border
            {
                Width = 20, Height = 20, CornerRadius = new CornerRadius(10), VerticalAlignment = VerticalAlignment.Top, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 1, 0, 0),
                Background = on ? GoldInk : C(Color.FromRgb(0xFF, 0xFF, 0xFF), 0.08),
                Child = new TextBlock { Text = s.N.ToString(CultureInfo.InvariantCulture), FontSize = 11, FontWeight = FontWeights.Bold, Foreground = on ? new SolidColorBrush(Color.FromRgb(0x1D, 0x13, 0x00)) : Muted, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center },
            };
            row.Children.Add(num);
            var words = new StackPanel();
            var head = T(L.F("Specialization {0}", s.N) + (s.Level > 0 ? "  ·  " + L.F("skill level {0}", s.Level) : ""), 10.5, on ? GoldInk : Faint, FontWeights.SemiBold);
            words.Children.Add(head);
            var text = T(s.Text, 12.5, on ? Ink : Muted, on ? FontWeights.SemiBold : FontWeights.Normal);
            text.TextWrapping = TextWrapping.Wrap;
            words.Children.Add(text);
            Grid.SetColumn(words, 1); row.Children.Add(words);
            sp.Children.Add(row);
        }
        var card = new Border
        {
            Tag = "spec-card", CornerRadius = new CornerRadius(10), Padding = new Thickness(14, 12, 14, 12), BorderThickness = new Thickness(1), Child = sp,
            Background = new SolidColorBrush(Color.FromRgb(0x17, 0x1A, 0x22)), BorderBrush = C(Color.FromRgb(0xF5, 0xB9, 0x42), 0.35),
            Effect = new DropShadowEffect { Color = Colors.Black, BlurRadius = 18, ShadowDepth = 4, Opacity = 0.55 },
        };
        return card;
    }

    // ================================================================ compare

    /// <summary>
    /// This pull against your best kill of this boss and your last pull of it (kept on this PC): the three figures, then your DPS
    /// by ability against the best (or the last) pull's. With the per-second record, your pace against your best as a line.
    /// </summary>
    public static Border Compare(FightView v)
    {
        var you = v.You!;
        var body = new StackPanel();
        var head = new StackPanel { Orientation = Orientation.Horizontal };
        head.Children.Add(T(L.T("Compare"), 13, Ink, FontWeights.SemiBold)); head.Children.Add(M(T(L.F("your DPS on {0}", v.E.Boss), 12, Muted), 8, 0, 0, 0));
        body.Children.Add(M(head, 0, 0, 0, 10));
        var tiles = new System.Windows.Controls.Primitives.UniformGrid { Columns = 1 + (v.Best != null ? 1 : 0) + (v.Last != null ? 1 : 0), Margin = new Thickness(-4, 0, -4, 10) };
        Border Tile(string label, string value, string sub, Brush tone, bool lit)
        {
            var sp = Stat(L.T(label), T(value, 24, tone, FontWeights.Bold, tab: true), sub, Muted, Faint);
            return lit ? M(YouFrame(sp, 10, new Thickness(12, 8, 12, 9), 0.6), 4, 0, 4, 0)
                : M(new Border { CornerRadius = new CornerRadius(10), Padding = new Thickness(12, 8, 12, 9), Background = K("CardQuiet"), BorderBrush = Hair, BorderThickness = new Thickness(1), Child = sp }, 4, 0, 4, 0);
        }
        tiles.Children.Add(Tile("This pull", Int(you.Dps), L.F("{0} · {1} in the group", BossParser.Clock(v.E.Duration(v.Now)), Rank(you.Rank)), GoldInk, true));
        if (v.Best != null) { double d = you.Dps / v.Best.Dps - 1; tiles.Children.Add(Tile("Your best kill", Int(v.Best.Dps), L.F("{0:MMM d} · you're {1}", v.Best.When.ToLocalTime(), Signed(d)), Ink, false)); }
        if (v.Last != null) { double d = you.Dps / v.Last.Dps - 1; tiles.Children.Add(Tile("Last pull", Int(v.Last.Dps), L.F("{0:MMM d} · you're {1}", v.Last.When.ToLocalTime(), Signed(d)), Text2, false)); }
        body.Children.Add(tiles);
        var basis = v.Best ?? v.Last!;
        string basisName = v.Best != null ? "Best" : "Last";
        var h = Cols(38, -1, 74, 74, 74); h.Margin = new Thickness(0, 4, 0, 4);
        h.Children.Add(Col(Head("Ability", right: false), 1)); h.Children.Add(Col(Head("This pull"), 2)); h.Children.Add(Col(Head(basisName), 3)); h.Children.Add(Col(Head("Change", sorted: true), 4));
        body.Children.Add(h);
        var skills = you.Abilities.Select(a => a.Skill).Union(basis.Abilities.Keys).ToList();
        var rows = skills.Select(s => (Skill: s, Now: you.Abilities.FirstOrDefault(a => a.Skill == s)?.Dps ?? 0, Then: basis.Abilities.TryGetValue(s, out var t) ? t : 0))
            .OrderByDescending(x => Math.Max(x.Now, x.Then)).Take(12).ToList();
        foreach (var x in rows)
        {
            var r = Cols(38, -1, 74, 74, 74); r.MinHeight = 34;
            r.Children.Add(SkillArt.Icon(x.Skill, you.F.Class, 26));
            r.Children.Add(Col(T(Parser.Abilities.Name(x.Skill, you.F.Class), 13, Ink, FontWeights.SemiBold), 1));
            r.Children.Add(Col(Num(x.Now > 0 ? Int(x.Now) : "–", 12.5, Ink, FontWeights.Bold), 2));
            r.Children.Add(Col(Num(x.Then > 0 ? Int(x.Then) : "–", 12.5, Text2), 3));
            if (x.Now > 0 && x.Then > 0) { double d = x.Now / x.Then - 1; r.Children.Add(Col(Num(Signed(d), 12.5, C(d >= 0 ? Mint : Coral), FontWeights.Bold), 4)); }
            else r.Children.Add(Col(Num(x.Now > 0 ? L.T("new") : L.T("not used"), 11.5, Faint), 4));
            body.Children.Add(r);
        }
        return Glass(body, new Thickness(14, 12, 14, 10), 14);
    }

    // ================================================================ timeline and damage taken (once the record carries them)

    public static Border Timeline(FightView v)
    {
        var body = new StackPanel();
        var head = Cols(-1, 0);
        var hl = new StackPanel { Orientation = Orientation.Horizontal };
        hl.Children.Add(T(L.T("Timeline"), 13, Ink, FontWeights.SemiBold)); hl.Children.Add(M(T(L.T("DPS over the fight"), 12, Muted), 8, 0, 0, 0));
        head.Children.Add(hl);
        var legend = new StackPanel { Orientation = Orientation.Horizontal };
        foreach (var m in v.Members.Where(m => m.PerSecond != null))
        {
            legend.Children.Add(new Border { Width = 10, Height = 3, CornerRadius = new CornerRadius(1.5), Background = C(m.You ? Gold : ClassArt.ColorOf(m.F.Class)), Margin = new Thickness(10, 0, 5, 0), VerticalAlignment = VerticalAlignment.Center });
            legend.Children.Add(T(m.F.Name, 11, m.You ? GoldInk : Muted, FontWeights.SemiBold));
        }
        head.Children.Add(Col(legend, 1));
        body.Children.Add(M(head, 0, 0, 0, 8));
        var lines = v.Members.Where(m => m.PerSecond is { Count: > 1 }).Select(m => (M: m, S: Smooth(m.PerSecond!.Select(x => (double)x).ToList(), 3))).ToList();
        double secs = lines.Max(l => l.S.Length);
        var (cv, ch) = Frame(secs, Wd - 4, 230, lines.Max(l => l.S.Max()) * 1.1, K("Line"), Faint);
        foreach (var (m, s) in lines.Where(l => !l.M.You)) Line(cv, ch, s, ClassArt.ColorOf(m.F.Class), 1.6, false, opacity: 0.85);
        foreach (var (m, s) in lines.Where(l => l.M.You)) Line(cv, ch, s, Gold, 2.6, true);
        foreach (var (m, s) in lines) foreach (var d in m.DeathLog) DeathMark(cv, ch, d.At, s[Math.Clamp((int)d.At, 0, s.Length - 1)], m.F.Name);
        body.Children.Add(cv);
        return Glass(body, new Thickness(14, 12, 14, 10), 14);
    }

    public static FrameworkElement Taken(FightView v)
    {
        var page = new StackPanel();
        var body = new StackPanel();
        Grid Row() => Cols(30, 38, -1, 74, 64, 60);
        var h = Row(); h.Margin = new Thickness(10, 0, 10, 6);
        h.Children.Add(Col(Head("Member", right: false), 2)); h.Children.Add(Col(Head("Taken", sorted: true), 3)); h.Children.Add(Col(Head("Per second"), 4)); h.Children.Add(Col(Head("Deaths"), 5));
        body.Children.Add(h);
        var list = v.Members.Where(m => m.Taken != null).OrderByDescending(m => m.Taken).ToList();
        int i = 0;
        foreach (var m in list)
        {
            var g = Row(); g.MinHeight = 40;
            g.Children.Add(RankChip(++i, m.F.Class, 22, you: m.You));
            g.Children.Add(Col(ClassArt.Icon(m.F.Class, 28, m.F.Name), 1));
            g.Children.Add(Col(T(m.F.Name, 13.5, ClassArt.NameBrush(m.F.Class, Dark), FontWeights.SemiBold), 2));
            g.Children.Add(Col(Num(Amounts.Short(m.Taken ?? 0), 14, Ink, FontWeights.Bold), 3));
            g.Children.Add(Col(Num(Int((m.Taken ?? 0) / Math.Max(1, v.Seconds)), 12.5, Text2), 4));
            g.Children.Add(Col(Num(m.Deaths > 0 ? $"✕ {m.Deaths}" : "–", 12.5, m.Deaths > 0 ? C(Coral) : Faint, FontWeights.Bold), 5));
            body.Children.Add(m.You ? M(YouFrame(g, 10, new Thickness(9, 0, 9, 0), 0.7), 0, 2, 0, 2) : new Border { Padding = new Thickness(10, 0, 10, 0), Child = g });
        }
        page.Children.Add(Glass(body, new Thickness(6, 12, 6, 10), 14));
        foreach (var m in v.Members.Where(x => x.DeathLog.Count > 0))
        {
            var d = m.DeathLog[0];
            var rb = new StackPanel();
            var ht = T("", 13.5, Ink); ht.Inlines.Add(R("✕  ", 12, C(Coral), FontWeights.Bold)); ht.Inlines.Add(R(m.F.Name, 13.5, ClassArt.NameBrush(m.F.Class, Dark), FontWeights.Bold)); ht.Inlines.Add(R(L.F(" died at {0}", BossParser.Clock(TimeSpan.FromSeconds(d.At))), 13.5, Ink, FontWeights.SemiBold));
            if (d.Killer.Length > 0) ht.Inlines.Add(R(L.F("   killing blow: {0}", d.Killer), 12, Muted));
            rb.Children.Add(M(ht, 0, 0, 0, 10));
            foreach (var hit in d.Last)
            {
                bool kb = hit == d.Last[^1];
                var r = Cols(56, -1, 74, 170); r.MinHeight = 32;
                r.Children.Add(T(kb ? BossParser.Clock(TimeSpan.FromSeconds(hit.At)) : L.F("−{0:0.0} s", d.At - hit.At), 12, kb ? C(Coral) : Faint, FontWeights.SemiBold, tab: true));
                var src = T("", 13, Ink); src.Inlines.Add(R(hit.Source, 13, Ink, FontWeights.SemiBold)); if (hit.Crit) src.Inlines.Add(R(L.T("  crit"), 11, GoldInk, FontWeights.Bold));
                r.Children.Add(Col(src, 1));
                r.Children.Add(Col(Num(Int(hit.Amount), 13, kb ? C(Coral) : Ink, FontWeights.Bold), 2));
                if (hit.HpAfter is { } hp)
                {
                    var hg = Cols(-1, 48); hg.Margin = new Thickness(14, 0, 0, 0);
                    hg.Children.Add(Bar(hp, hp < 0.25 ? Coral : Mint, 6, Track, glow: false));
                    hg.Children.Add(Col(Num(hp <= 0 ? L.T("dead") : Pct(hp, 0), 12, hp <= 0 ? C(Coral) : Text2, FontWeights.Bold), 1));
                    r.Children.Add(Col(hg, 3));
                }
                rb.Children.Add(new Border { Child = r, CornerRadius = new CornerRadius(8), Padding = new Thickness(10, 0, 10, 0), Margin = new Thickness(0, 0, 0, 3), Background = kb ? C(Coral, 0.14) : Brushes.Transparent });
            }
            page.Children.Add(M(Glass(rb, new Thickness(16, 13, 16, 12), 14, glow: Coral), 0, 12, 0, 0));
        }
        return page;
    }
}
