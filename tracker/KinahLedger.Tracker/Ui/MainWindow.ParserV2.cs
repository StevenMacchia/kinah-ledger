using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using KinahLedger.Parser;

namespace KinahLedger.Ui;

/// <summary>
/// The Parser page's fight in Parser v2's Command look (Ui/ParserCommand.cs draws it): the hero, the tabs the record has data
/// for, the group, a member's inspect view, compare with your best and last pull of the boss. This part chooses what shows
/// and wires the clicks; the drawing is ParserCommand's.
/// </summary>
public partial class MainWindow
{
    /// <summary>The fight's tab (Damage, Compare, Timeline, Taken), the member opened (their id) and how their abilities are sorted.</summary>
    string _parserTab = "Timeline";   // the Graphs tab first (Steven, 2026-11-18); a record without the seconds falls back to Group
    long? _parserInspect;
    string _parserSort = "Damage";
    /// <summary>The Compare tab's picks (Parser/PullCompare.cs): the pull against (null: your best kill of the fight), whose abilities (null: yours), all of them shown. Reset when another fight opens.</summary>
    string? _parserCompareWith, _parserCompareMember;
    bool _parserCompareAll;
    string? _parserCompareFor;

    /// <summary>For a render: fills the store-v2 figures of a view with a sample (ui-shot only; the app never sets it).</summary>
    internal static Action<FightView>? ParserViewSample;

    /// <summary>The width expanded adds to the meter (its damage column; crit and the DPS over time once kept).</summary>
    double MeterExtraWidth => _parserPrefs.Expanded ? 70 : 0;

    /// <summary>The overlay's meter compact or expanded (a click on it, or Settings › In game › Parser › Expanded meter).</summary>
    internal void ToggleMeterExpanded()
    {
        _parserPrefs.Expanded = !_parserPrefs.Expanded;
        ApplyMeterExpanded();
        if (!_demo) _parserPrefs.Save();
    }

    void ApplyMeterExpanded()
    {
        ParserRows.ExpandedPref = _parserPrefs.Expanded;
        ParserRows.HealBarsPref = _parserPrefs.HealBars;
        EnergyMark.Pref = _parserPrefs.OdyleEnergy;
        PbGhost.Shown = _parserPrefs.Ghost; PbGhost.GroupShown = _parserPrefs.GhostGroup;
        HudStackLook.FightWidth = _parserPrefs.Width + MeterExtraWidth;
        _hud?.ApplyFightWidth();
        ParserOverlayTick();
    }

    CommandCtx ParserCtx() => new()
    {
        Tab = _parserTab, Sort = _parserSort, Details = PDetails, Kills = PKills, Deaths = PDeaths,
        // no switches over the group (Steven, 2026-11-18: "No settings should be from the app outside settings"): what the figures
        // are, said, beside the copy buttons; DPS over active time and damage to adds are Settings › DPS meter's rows
        GroupHead = e => e.HasDamage ? GroupBasisLine(e) : null, GroupFoot = e => CoverageLine(e),
        SetTab = t => { _parserTab = t; RedrawParserPage(); },
        OpenFight = id => OpenFight(id),
        Inspect = id => { _parserInspect = id; RedrawParserPage(); LowerScroll.ScrollToTop(); },
        SetSort = k => { _parserSort = k; RedrawParserPage(); },
        Tip = s => Tip(s),
        CompareWith = _parserCompareWith, CompareMember = _parserCompareMember, CompareAll = _parserCompareAll,
        SetCompare = id => { _parserCompareWith = id; RedrawParserPage(); },
        SetCompareMember = name => { _parserCompareMember = name; _parserCompareAll = false; RedrawParserPage(); },
        ToggleCompareAll = () => { _parserCompareAll = !_parserCompareAll; RedrawParserPage(); },
    };

    /// <summary>The group card's line on what its figures are ("DPS over the whole fight · boss damage only"); a click opens Settings › DPS meter, where both are set.</summary>
    FrameworkElement GroupBasisLine(Encounter e)
    {
        var parts = new List<string> { Encounter.ActiveBasis ? L.T("DPS over active time") : L.T("DPS over the whole fight") };
        if (e.HasAdds) parts.Add(Encounter.BossOnly ? L.T("boss damage only") : L.T("adds included"));
        var t = PT(string.Join("  ·  ", parts), 11.5, FontWeights.Medium, "Faint");
        t.VerticalAlignment = VerticalAlignment.Center;
        t.Cursor = System.Windows.Input.Cursors.Hand;
        t.ToolTip = Tip(L.T("Set in Settings › DPS meter: DPS over active time, Damage to adds."));
        t.Tag = "parser-group-basis";
        t.MouseLeftButtonUp += (_, _) => OpenOptions(OptionsWindow.Page.Meter);
        return t;
    }

    FightView ParserViewOf(Encounter e, DateTime now, List<Encounter> past)
    {
        // the Compare tab's picks belong to the fight they were made on
        if (_parserCompareFor != e.Id) { _parserCompareFor = e.Id; _parserCompareWith = null; _parserCompareMember = null; _parserCompareAll = false; }
        var v = FightView.Of(e, now, past, _parserCompareWith);
        ParserViewSample?.Invoke(v);
        return v;
    }

    /// <summary>A fight under way (live) or a kept one: its hero, then what its tab shows.</summary>
    /// <param name="held">The last fight, held in the live view until the next boss fight starts: no way back drawn (nothing was opened).</param>
    FrameworkElement FightBlock(Encounter e, DateTime now, List<Encounter> past, bool live, bool held = false)
    {
        var end = live ? now : e.End ?? e.LastAt;
        var v = ParserViewOf(e, end, past);
        var ctx = ParserCtx();
        // each section's own copy buttons, top right of its head (Ui/MainWindow.ParserShare.cs)
        if (Shareable(e)) ctx.Copy = view => CopyButtons(e, v, view);
        var tabs = ParserCommand.Tabs(v);
        if (!tabs.Contains(ctx.Tab)) ctx.Tab = tabs[0];   // Graphs (with the group under them), or Group for a record without seconds
        var page = new StackPanel();
        // when it was: a kept fight's start and end, on the result's hover, and beside Back on a pull opened from the list
        string? when = null;
        if (!live)
        {
            var startWord = e.ModeKind switch { ParserMode.Run => L.T("Entered"), ParserMode.Fights => L.T("Started"), _ => L.T("Pulled") };
            var endWord = e.ModeKind == ParserMode.Run ? L.T("Left") : L.T("Ended");
            when = L.F("{0} {1}  ·  {2} {3}", startWord, L.DayTime(e.Start.ToLocalTime()), endWord, end.ToLocalTime().ToString("HH:mm:ss", CultureInfo.InvariantCulture));
        }
        // the way back, a real button top left over the fight (Steven, 2026-11-17: "make a back button WAY more obvious"), when
        // it was beside it; Backspace, Alt+← and the mouse's back button do the same (Ui/MainWindow.ParserShare.cs)
        FrameworkElement? back = null;
        if (_parserFromRuns) back = ParserBackButton(L.T("Back to the run"), BackToRuns);   // opened from a run on the Runs page: back goes there
        // a pull opened from the list: Back returns to the live view (the fight under way, or the last one held; the list under it)
        else if (!live && !held) back = ParserBackButton(L.T("Back", "button"), () => OpenFight(null));
        if (back != null)
        {
            back.Margin = new Thickness(0);
            var backRow = new DockPanel { Margin = new Thickness(0, 0, 0, 8), LastChildFill = true };
            DockPanel.SetDock(back, Dock.Left); backRow.Children.Add(back);
            if (when != null) backRow.Children.Add(Margin(PT(when, 12, FontWeights.Medium, "Muted", trim: true), 12, 0, 0, 0));
            foreach (var c in backRow.Children.OfType<FrameworkElement>()) c.VerticalAlignment = VerticalAlignment.Center;
            page.Children.Add(backRow);
        }
        var extras = new List<FrameworkElement>();
        if (e.ModeKind == ParserMode.Run)
        {
            // a run: the pull under way with its boss's HP, then the bosses so far as splits
            if (live && e.Boss.Length > 0) extras.Add(Margin(PullNow(e, now), 0, 12, 0, 0));
            if (e.Splits.Count > 0) extras.Add(Margin(SplitsTable(e, live), 0, 10, 0, 0));
        }
        if (live && e.OpenWorld && e.You is { Damage: > 0 })
        {
            // farming: your live DPS, rolling (refreshed ten times a second by FarmTick)
            var stat = PStat(L.T("Your DPS now"), FarmDpsText(now) is { Length: > 0 } t0 ? t0 : FarmFigure(e.FarmDps()), "Ink", L.F("last {0:0} s", BossParser.RollSeconds));
            _parserFarmNow = ((StackPanel)stat.Child).Children.OfType<TextBlock>().ElementAt(1);
            System.Windows.Documents.Typography.SetNumeralAlignment(_parserFarmNow, FontNumeralAlignment.Tabular);
            _parserFarmNow.MinWidth = 44;
            extras.Add(Margin(stat, 0, 10, 0, 0));
        }
        if (live && e.OpenWorld) extras.Add(Margin(PT(L.T("Shown while it lasts. Open-world and Abyss fights are never kept."), 11.5, FontWeights.Medium, "Faint"), 0, 8, 0, 0));
        if (!live && e.Partial)
        {
            // why it's partial
            var why = PT(L.F("Partial: {0}. Still one {1}; its damage and kills may be short.", ParserCommand.WhyText(e), Noun(e.ModeKind)), 12, FontWeights.Medium, "Warn");
            why.TextWrapping = TextWrapping.Wrap; why.Margin = new Thickness(0, 6, 0, 0);
            extras.Add(why);
        }
        // the fight as a compact strip over its graphs (Steven, 2026-11-18: the graph is the hero, 1.0.18's big hero card was
        // "awful"); the fight's Copy picture is the page head's big one (its ▾ has the summary card)
        var hero = ParserCommand.Strip(v, ctx, live, extras, when);
        if (Shareable(e)) ShareOnRightClick(hero, e, v, ParserShare.Hero);
        page.Children.Add(hero);
        FrameworkElement body = ctx.Tab switch
        {
            "Compare" => ParserCommand.Compare(v, ctx.Copy(ParserShare.Compare), ctx),
            "Track" => (FrameworkElement?)ParserTimeline.View(v, ctx, ctx.Copy(ParserShare.Track)) ?? new Border(),   // the Timeline tab (Ui/ParserTimeline.cs)
            "Timeline" => GraphsView(e, v, live, ctx),   // the Graphs tab, the group's table under it (Ui/MainWindow.ParserGraphs.cs)
            "Taken" => ParserCommand.Taken(v, ctx.Copy(ParserShare.Taken)),
            _ => (e.Roster.Count > 0 || e.ModeKind == ParserMode.Bosses) ? ParserCommand.Group(v, ctx, live, detail: !live) : new Border(),
        };
        // a right-click on the view: its picture, and a table's text (tab-separated)
        if (Shareable(e) && body is not Border { Child: null }) ShareOnRightClick(body, e, v, ParserShare.ViewOfTab(ctx.Tab));
        page.Children.Add(Margin(body, 0, 8, 0, 0));
        // a kept fight: your kills and the kills near you, under the group
        if (!live && ctx.Tab is "Group" or "Timeline")
        {
            void KillChips(string head, IEnumerable<KillNear> kills)
            {
                var groups = kills.GroupBy(k => k.Name, StringComparer.OrdinalIgnoreCase).OrderByDescending(g => g.Count()).ToList();
                if (groups.Count == 0) return;
                page.Children.Add(Margin(PT(L.T(head), 11, FontWeights.SemiBold, "Faint"), 4, 12, 0, 4));
                var wp = new WrapPanel { Margin = new Thickness(4, 0, 0, 0) };
                foreach (var g in groups) { var c = PChip(g.Count() > 1 ? $"{g.First().Name} ×{g.Count()}" : g.First().Name, "Text2", "ChipBg"); c.Margin = new Thickness(0, 0, 6, 4); wp.Children.Add(c); }
                page.Children.Add(wp);
            }
            if (PKills || e.ModeKind == ParserMode.Fights) KillChips("Your kills", e.Kills.Where(k => k.Yours));
            if (PKills) KillChips("Kills near you", e.Kills.Where(k => !k.Boss && !k.Yours));
            if (!e.HasDamage) page.Children.Add(Margin(PT(L.F("No damage was seen for this {0}, so none is shown.", Noun(e.ModeKind)), 11.5, FontWeights.Medium, "Faint"), 4, 10, 0, 0));
        }
        return page;
    }

    /// <summary>The member opened in the fight shown (the one under way, or the kept one open), or null.</summary>
    FrameworkElement? InspectBlock(Encounter? e, DateTime now, List<Encounter> past)
    {
        if (_parserInspect is not long id || e == null) return null;
        var end = e.Live ? now : e.End ?? e.LastAt;
        var v = ParserViewOf(e, end, past);
        var m = v.Members.FirstOrDefault(x => x.F.Id == id);
        if (m == null || m.Abilities.Count == 0) { _parserInspect = null; return null; }
        var ctx = ParserCtx();
        // the member's copy buttons on the way back's line: their card and abilities as a picture, the abilities as text
        if (Shareable(e)) ctx.Copy = view => view == ParserShare.Abilities ? CopyButtons(e, v, view, id) : null;
        var page = ParserCommand.Inspect(v, m, ctx);
        if (Shareable(e)) ShareOnRightClick(page, e, v, ParserShare.Abilities, id);
        return page;
    }

    // ---- the demo's extras (layout renders and the options test only)

    /// <summary>Two earlier pulls of the demo's live boss, so its hero and Compare have your best kill and your last pull to measure against.</summary>
    static IEnumerable<Encounter> ParserDemoEarlier(Encounter live, DateTime now)
    {
        Encounter Pull(DateTime start, double secs, string outcome, double youK, double hpLeft)
        {
            var c = BossParser.Copy(live);
            c.Id = Guid.NewGuid().ToString("N")[..12];
            double liveSecs = Math.Max(1, live.Duration(now).TotalSeconds);
            c.Start = start; c.End = start.AddSeconds(secs); c.LastAt = c.End.Value; c.Outcome = outcome;
            c.BossHp = (long)(c.BossMaxHp * hpLeft);
            int i = 0;
            foreach (var f in c.Roster)
            {
                double k = (f.You ? youK : 0.97 + 0.02 * (i++ % 3)) * secs / liveSecs;
                f.Damage = (long)(f.Damage * k);
                foreach (var a in f.Abilities) { a.Damage = (long)(a.Damage * k * (f.You ? 1 + ((a.Skill / 10000) % 7 - 3) * 0.02 : 1)); a.Hits = Math.Max(1, (int)(a.Hits * secs / liveSecs)); }
                f.ActiveSeconds = secs * 0.93;
            }
            return c;
        }
        yield return Pull(now.AddDays(-2).AddMinutes(-20), 151, "kill", 1.0438, 0);
        yield return Pull(now.AddDays(-1).AddMinutes(-35), 171, "wipe", 0.9606, 0.22);
    }

    /// <summary>
    /// The compare render's casts and hit kinds (crit once KL_CRIT picks one), on the record itself so both pulls of a compare
    /// carry them: steady from one render to the next (by the pull's start, the member and the skill), a kill pull's casts a
    /// little different from the others. Render states only; the app never sets it.
    /// </summary>
    static void ParserDemoCasts(Encounter e)
    {
        static double N(long a, long b, long c) { unchecked { uint h = (uint)(a * 73856093) ^ (uint)(b * 19349663) ^ (uint)(c * 83492791); h ^= h >> 13; h *= 0x5bd1e995; h ^= h >> 15; return (h % 10000) / 10000.0; } }
        long salt = e.Start.Ticks / TimeSpan.TicksPerMinute;
        foreach (var f in e.Roster)
            foreach (var a in f.Abilities)
            {
                int hits = Math.Max(1, a.Hits);
                a.StreamHits = hits;
                a.Casts = Math.Max(1, (int)Math.Round(hits / (1 + (Math.Abs(a.Skill) / 10000 % 3) * 0.5) * (0.9 + N(a.Skill, f.Id, salt) * 0.2)));
                a.Kinds = new Dictionary<string, int> { ["result:3"] = (int)Math.Round(hits * (0.2 + N(a.Skill, f.Id, salt + 1) * 0.14)) };
                // your kept pulls' abilities apart from the one under way, each its own way (the demo's copies scale them all alike)
                if (f.You && !e.Live)
                {
                    double k = 0.84 + N(a.Skill, salt, 5) * 0.34;
                    long d0 = a.Damage, b0 = a.BossDamage;
                    a.Damage = (long)(a.Damage * k); a.BossDamage = (long)(a.BossDamage * k);
                    f.Damage += a.Damage - d0; f.BossDamage += a.BossDamage - b0;
                }
            }
    }

    /// <summary>
    /// The Timeline tab's render sample (render states only): a kept pull's lanes as 1.0.38 keeps them on your row, each
    /// ability's damage spread over its casts through the pull (adding up to its row), a damage over time ticking every 2 s,
    /// your biggest hits from the casts; a member's death with a recap where the sample has none.
    /// </summary>
    static void ParserDemoTimeline(Encounter e)
    {
        static double N(long a, long b, long c) { unchecked { uint h = (uint)(a * 73856093) ^ (uint)(b * 19349663) ^ (uint)(c * 83492791); h ^= h >> 13; h *= 0x5bd1e995; h ^= h >> 15; return (h % 10000) / 10000.0; } }
        if (e.You is not { } you) return;
        double len = Math.Max(6, e.Duration(e.End ?? e.LastAt).TotalSeconds);
        var top = new List<TopHit>();
        foreach (var a in you.Abilities)
        {
            a.Seconds = new(); a.BossSeconds = new();
            bool dot = a.Skill <= 0;
            int c = dot ? Math.Max(1, (int)(len / 2)) : Math.Max(1, a.Casts ?? Math.Max(1, a.Hits / 2));
            double gap = len / c, off = N(a.Skill, you.Id, 1) * gap * 0.8;
            if (!dot) { a.Casts = c; a.CastLog = new(); }
            long left = a.Damage, leftB = a.BossDamage;
            for (int i = 0; i < c; i++)
            {
                double at = Math.Clamp(dot ? 1 + i * 2 : off + i * gap + (N(a.Skill, i, 2) - 0.5) * gap * 0.35, 0, len - 0.05);
                long d = i == c - 1 ? left : (long)(a.Damage / (double)c * (0.7 + N(a.Skill, i, 3) * 0.6));
                long db = i == c - 1 ? leftB : (long)(a.BossDamage / (double)c * (0.7 + N(a.Skill, i, 3) * 0.6));
                d = Math.Min(d, left); db = Math.Min(db, leftB); left -= d; leftB -= db;
                int s = (int)at;
                a.Seconds[s] = a.Seconds.GetValueOrDefault(s) + d;
                if (db > 0) a.BossSeconds[s] = a.BossSeconds.GetValueOrDefault(s) + db;
                if (dot) continue;
                int hits = Math.Max(1, a.Hits / c);
                a.CastLog!.Add(new CastMark { At = Math.Round(at, 2), Damage = d, Hits = hits });
                top.Add(new TopHit { At = Math.Round(at, 2), Skill = a.Skill, Amount = (long)(d / (double)hits * (1.2 + N(a.Skill, i, 4) * 0.5)), Kinds = N(a.Skill, i, 5) < 0.35 ? new() { "result:3" } : new() { "result:2" } });
            }
        }
        you.TopHits = top.OrderByDescending(h => h.Amount).Take(Fighter.TopHitsKept).ToList();
        // each death in the sample with a recap (the sample's deaths carry none): the boss's attacks get placeholder names
        foreach (var f in e.Members.Where(f => f.Deaths > 0 && (f.DeathLog?.Count ?? 0) < f.Deaths))
        {
            f.DeathLog ??= new();
            for (int k = f.DeathLog.Count; k < f.Deaths; k++)
            {
                double at = Math.Round(len * (0.45 + 0.3 * N(f.Id, k, 6)), 1);
                var d = new DeathRecord { At = at, Killer = e.Boss, BossHpLeft = 0.38, KillerSkill = 0 };
                foreach (var (dt, src, amt) in new[] { (-3.8, "Tail Sweep", 14_200L), (-2.5, "Horn Charge", 21_900L), (-1.1, "Ground Slam", 16_400L), (0.0, "Searing Breath", 31_700L) })
                    d.Last.Add(new TakenHit { At = Math.Round(at + dt, 2), Source = src, Amount = amt });
                f.DeathLog.Add(d);
            }
        }
    }

    /// <summary>
    /// A render's sample of what the record doesn't carry yet (crits, max and min hits, casts, damage per second, damage taken, a
    /// death with its last hits), so the views that light up with store v2 can be seen and reviewed. Only render states set it.
    /// The boss's attacks get placeholder names.
    /// </summary>
    static void ParserDemoDepth(FightView v)
    {
        static double N(long a, long b, int c = 0) { unchecked { uint h = (uint)(a * 73856093) ^ (uint)(b * 19349663) ^ (uint)(c * 83492791); h ^= h >> 13; h *= 0x5bd1e995; h ^= h >> 15; return (h % 10000) / 10000.0; } }
        var hits = new[] { "Horn Charge", "Ground Slam", "Searing Breath", "Tail Sweep", "Crushing Stomp" };
        int secs = Math.Max(10, (int)v.E.Duration(v.Now).TotalSeconds);
        foreach (var m in v.Members)
        {
            foreach (var a in m.Abilities)
            {
                a.Crit = 0.18 + N(a.Skill, m.F.Id, 1) * 0.26;
                a.Max = (long)(a.Average * (1.9 + N(a.Skill, m.F.Id, 2) * 0.8)); a.Min = (long)(a.Average * (0.42 + N(a.Skill, m.F.Id, 6) * 0.1));
                a.Casts = Math.Max(1, (int)Math.Round(a.Hits / (1 + (int)(N(a.Skill, m.F.Id, 3) * 3) * 0.5)));
            }
            if (m.Abilities.Count > 0) { m.Crit = m.Abilities.Sum(a => (a.Crit ?? 0) * a.Hits) / Math.Max(1, m.Abilities.Sum(a => a.Hits)); m.MaxHit = m.Abilities.Max(a => a.Max); }
            if (m.Damage > 0)
            {
                var s = new double[secs];
                for (int i = 0; i < secs; i++) s[i] = Math.Max(0, 1 + (((i + m.F.Id * 7) % 30) < 10 ? 0.55 : -0.12) + ((i % 45) > 36 ? -0.4 : 0) + (N(i, m.F.Id) - 0.5) * 0.8);
                double sum = s.Sum();
                m.PerSecond = s.Select(x => (long)(x / sum * m.Damage)).ToList();
            }
            double w = ClassArt.Key(m.F.Class) == "templar" ? 2.6 : ClassArt.Key(m.F.Class) == "cleric" ? 0.7 : 1;
            long taken = (long)(secs * 410 * w * (0.85 + N(9, m.F.Id) * 0.3));
            m.Taken = taken;
            var parts = new[] { 0.36, 0.27, 0.19, 0.12, 0.06 };
            m.TakenBy = parts.Select((p, j) => (hits[j], 0, (long)(taken * p), 3 + (int)(N(j, m.F.Id, 5) * 9))).ToList();
            // buffs: placeholder names (a render's sample only)
            m.Buffs = new[] { "Group buff A", "Group buff B", "Self buff", "Short burst buff" }.Select((b, j) => { double sh = Math.Clamp(0.95 - j * 0.22 + (N(j, m.F.Id, 7) - 0.5) * 0.1, 0.05, 1); return (b, sh * secs, sh); }).ToList();
        }
        if (v.Members.FirstOrDefault(m => ClassArt.Key(m.F.Class) == "templar") is { } tank)
        {
            double at = Math.Round(secs * 0.62);
            var d = new DeathView { At = at, Killer = "Searing Breath", BossHpLeft = 0.6 };
            double hp = 0.71;
            foreach (var (dt, src, frac, crit) in new[] { (-4.6, "Tail Sweep", 0.12, false), (-3.4, "Horn Charge", 0.17, true), (-2.1, "Ground Slam", 0.14, false), (-1.0, "Tail Sweep", 0.11, false), (0.0, "Searing Breath", 0.17, true) })
            { hp = dt == 0 ? 0 : Math.Max(0, hp - frac); d.Last.Add((at + dt, src, (long)(frac * 108_000), hp, crit)); }
            tank.DeathLog.Add(d);
            tank.Deaths = Math.Max(tank.Deaths, 1);
            if (tank.PerSecond != null) for (int i = (int)at; i < Math.Min(secs, at + 14); i++) tank.PerSecond[i] = 0;
        }
    }
}
