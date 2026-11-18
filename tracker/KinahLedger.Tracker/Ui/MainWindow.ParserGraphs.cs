using System.Windows;
using System.Windows.Controls;
using KinahLedger.Parser;

namespace KinahLedger.Ui;

/// <summary>
/// The Parser page's Graphs tab (Ui/ParserGraphs.cs draws it): a fight's default view, the one under way and every kept pull.
/// The window keeps the view while the same fight shows, so the page's redraws (a hit's, up to 16 a second) only lay it out
/// again and its charts are drawn when their data moves; its charts take the room the window leaves under the hero; each
/// card's Copy picture copies that graph in the share frame and saves it in Pictures\Kinah Ledger.
/// </summary>
public partial class MainWindow
{
    ParserGraphs.GraphsPanel? _graphs;
    bool _graphsFitHooked;

    /// <summary>The Graphs view of <paramref name="e"/>: the kept panel when it is the same fight, else a new one.</summary>
    FrameworkElement GraphsView(Encounter e, FightView v, bool live)
    {
        string key = $"{e.Id}|{Skins.Current}|{live}|{L.Code}";
        if (_graphs == null || _graphs.Key != key)
        {
            _graphs?.Stop();
            string id = e.Id;
            _graphs = new ParserGraphs.GraphsPanel(key, kind => CopyGraph(id, kind));
            _graphs.Loaded += (_, _) => FitGraphs();
        }
        if (!_graphsFitHooked) { _graphsFitHooked = true; LowerScroll.SizeChanged += (_, _) => FitGraphs(); }
        if (_graphs.Parent is Decorator d) d.Child = null;
        else if (_graphs.Parent is Panel p) p.Children.Remove(_graphs);
        _graphs.Show(v, live);
        // a fresh wrapper each redraw: what the page hangs on the view (its right-click) goes on it, never piling up on the kept panel
        return new Border { Child = _graphs };
    }

    /// <summary>The charts' heights for the room between the panel's top and the window's foot (the page fits, nothing scrolls).</summary>
    void FitGraphs()
    {
        if (_graphs is not { IsLoaded: true } g || !g.IsDescendantOf(LowerScroll)) return;
        double top = g.TranslatePoint(new Point(0, 0), LowerScroll).Y + LowerScroll.VerticalOffset;
        g.Fit(LowerScroll.ViewportHeight - top - 14);
    }

    /// <summary>One graph of the fight shown as a picture: copied, saved in Pictures\Kinah Ledger, said in the toast.</summary>
    bool CopyGraph(string id, string kind)
    {
        var e = (ParserLive is { } l && l.Id == id ? l : null) ?? ParserPast().FirstOrDefault(x => x.Id == id);
        if (!Shareable(e)) { Say(L.T("No fight to share yet. Pull a boss and the picture fills in."), "Warn"); return false; }
        try
        {
            var now = ShareNowOf(e!);
            var shown = _parserPrefs.ShareHideNames ? MeterShare.Hidden(e!) : e!;
            var v = ParserViewOf(shown, now, ParserPast());
            var bmp = ParserGraphs.Picture(shown, now, v, kind);
            var file = ParserGraphs.FileName(e!, kind);
            if (ShareSinkForTest is { } sink) sink(bmp, file);   // a test: never the clipboard or Pictures
            else
            {
                CopyPng(bmp);
                try { _meterShareSaved = SavePicture(bmp, file); }
                catch (Exception ex) { CrashLog.Write("parser graph save", ex); }
            }
            Say(L.T("Picture copied. Paste it in Discord or anywhere with Ctrl+V."), "Good");
            return true;
        }
        catch (Exception ex)
        {
            CrashLog.Write("parser graph", ex);
            Say(L.F("Couldn't copy the picture: {0}", ex.Message), "Bad");
            return false;
        }
    }

    // ---- renders and the options test (ui-shot --parser graphs, KL_OPTIONS_ONLY=graphs) ----

    internal ParserGraphs.GraphsPanel? GraphsForTest => _graphs;
    internal bool CopyGraphForTest(string kind) => _graphs != null && CopyGraph(_graphs.Key.Split('|')[0], kind);
    internal void ParserDrawNowForTest() => ParserDrawNow();
    internal double ParserViewportForTest => LowerScroll.ViewportHeight;
    /// <summary>The Graphs panel's foot in the window's scrolling part, scrolled to the top (above the viewport's height: it fits).</summary>
    internal double GraphsBottomForTest => _graphs is { IsLoaded: true } g && g.IsDescendantOf(LowerScroll) ? g.TranslatePoint(new Point(0, g.ActualHeight), LowerScroll).Y + LowerScroll.VerticalOffset : double.MaxValue;
    internal double ParserScrollableForTest => LowerScroll.ScrollableHeight;

    /// <summary>The render states of the Graphs tab: the fight each draws, live or kept.</summary>
    internal static readonly string[] GraphStates = { "live", "mid", "killed", "wipe", "solo", "six", "noheal", "real" };

    /// <summary>
    /// A render's Graphs tab: "live" a six-member pull 24 s in; "mid" the same 1:41 in with a death; "killed" a kept kill of the
    /// sample run through the real Parser (its heals as the Parser counted them); "wipe" a kept wipe at 31% with the group dying
    /// at the end; "solo" one player's kill (no group, no heals: the HP alone beside the DPS); "six" a kept six-member kill with
    /// a Cleric and a Chanter healing and the damage taken; "noheal" a kill whose record has no heals or damage taken (the row
    /// under the DPS has the HP alone); "real" the sample's own live pull (4 members, through the real Parser).
    /// </summary>
    internal void ShowGraphsDemo(string state)
    {
        ShowParserDemo("live");
        var like = _parserDemoLive!;
        var t0 = _parserDemoNow;
        Encounter? live = null, kept = null;
        switch (state)
        {
            case "real": live = like; break;
            case "live": live = GraphSample(like, state, t0.AddSeconds(-24), 24, 6, "", live: true); break;
            case "mid": live = GraphSample(like, state, t0.AddSeconds(-101), 101, 6, "", live: true, death: 62); break;
            case "killed": kept = _parserDemoPast!.Last(x => x.ModeKind == ParserMode.Bosses && x.Outcome == "kill" && x.HasDamage && x.Roster.Count(f => !f.Other) >= 4); break;
            case "wipe": kept = GraphSample(like, state, t0.AddMinutes(-9), 188, 6, "wipe", wipe: true); break;
            case "solo": kept = GraphSample(like, state, t0.AddMinutes(-6), 96, 1, "kill", heals: false, taken: false); break;
            case "six": kept = GraphSample(like, state, t0.AddMinutes(-4), 154, 6, "kill", death: 81); break;
            case "noheal": kept = GraphSample(like, state, t0.AddMinutes(-12), 131, 4, "kill", heals: false, taken: false); break;
            default: throw new ArgumentException("graphs state: " + state);
        }
        if (live != null) _parserDemoLive = live;
        if (kept != null)
        {
            if (!_parserDemoPast!.Contains(kept)) { _parserDemoPast.Add(kept); _parserDemoPast.Sort((a, b) => a.Start.CompareTo(b.Start)); }
            _parserOpen = kept.Id;
        }
        _parserTab = "Timeline"; _parserInspect = null; ParserViewSample = null;
        RedrawParserPage();
    }

    static readonly (long Id, string Name, string Class, int Cp, double Dps)[] GraphCast =
    {
        (1, "Swoly", "Gladiator", 4_812, 4_300), (4, "Kaelith", "Sorcerer", 4_730, 4_700), (6, "Lyrandel", "Ranger", 4_702, 4_050),
        (3, "Thornecrest", "Templar", 4_590, 2_700), (7, "Selunaire", "Chanter", 4_688, 2_100), (2, "Aerinwyn", "Cleric", 4_655, 1_350),
    };

    static double Hash(long a, long b, int c = 0) { unchecked { uint h = (uint)(a * 73856093) ^ (uint)(b * 19349663) ^ (uint)(c * 83492791); h ^= h >> 13; h *= 0x5bd1e995; h ^= h >> 15; return (h % 10000) / 10000.0; } }

    /// <summary>
    /// A made-up pull for the Graphs renders and checks: <paramref name="size"/> members (1, 4 or 6), their damage a second with
    /// bursts and lulls, the boss's HP from it, the damage taken (the Templar most, a group hit every 20 s), the healers' effective
    /// healing from it, deaths with their gaps. Every total adds up from its seconds, as the Parser keeps them.
    /// </summary>
    static Encounter GraphSample(Encounter like, string name, DateTime start, int secs, int size, string outcome, bool live = false, bool heals = true, bool taken = true, double death = -1, bool wipe = false)
    {
        var e = BossParser.Copy(like);
        e.Id = "graphs-" + name; e.Start = start; e.End = live ? null : start.AddSeconds(secs); e.LastAt = start.AddSeconds(secs);
        e.Outcome = live ? "" : outcome; e.Partial = false; e.PartialWhy.Clear(); e.Splits.Clear();
        var cast = size == 1 ? GraphCast.Take(1).ToList() : size == 4 ? new[] { GraphCast[0], GraphCast[1], GraphCast[3], GraphCast[5] }.ToList() : GraphCast.ToList();
        int n = secs + 1;
        // who is down when: a death with a revive 14 s later; a wipe's group falls in its last 22 s
        var down = cast.ToDictionary(c => c.Id, _ => new List<(double From, double To)>());
        var deaths = new List<(long Id, double At)>();
        if (death >= 0 && cast.Any(c => c.Class == "Templar")) { var tank = cast.First(c => c.Class == "Templar").Id; down[tank].Add((death, death + 14)); deaths.Add((tank, death)); }
        if (wipe)
        {
            double at = secs - 22;
            foreach (var c in cast.OrderBy(c => c.Class == "Templar" ? 0 : c.Class == "Cleric" ? 1 : 2 + (int)c.Id)) { down[c.Id].Add((at, secs + 1)); deaths.Add((c.Id, at)); at += c.Class == "Templar" ? 10 : 3; if (at > secs - 1) at = secs - 1; }
        }
        bool Down(long id, int i) => down[id].Any(d => i >= d.From && i < d.To);
        var taken0 = new Dictionary<long, long[]>();
        foreach (var c in cast)
        {
            var t = new long[n];
            for (int i = 0; i < n; i++)
            {
                if (Down(c.Id, i)) continue;
                double v = c.Class == "Templar" ? 1_500 * (0.7 + Hash(i, c.Id, 4) * 0.6) : size == 1 ? 650 * Hash(i, c.Id, 4) : 180 * Hash(i, c.Id, 4);
                if (i % 20 == 12) v += c.Class == "Templar" ? 5_400 : 3_600;   // the boss's group hit
                t[i] = (long)v;
            }
            taken0[c.Id] = t;
        }
        var groupTaken = new double[n];
        foreach (var t in taken0.Values) for (int i = 0; i < n; i++) groupTaken[i] += t[i];
        e.Roster.Clear();
        long total = 0;
        var cum = new double[n];
        foreach (var c in cast)
        {
            var ps = new List<long>(n);
            for (int i = 0; i < n; i++)
            {
                double ramp = Math.Min(1, (i + 1) / 4.0), burst = ((i + c.Id * 11) % 30) < 8 ? 1.55 : 1, lull = (i % 47) > 40 ? 0.5 : 1;
                double v = Down(c.Id, i) ? 0 : c.Dps * ramp * burst * lull * (0.72 + Hash(i, c.Id) * 0.56);
                if (live && i == n - 1) v *= 0.4;   // the second under way, still filling
                ps.Add((long)v);
                cum[i] += v;
            }
            long dmg = ps.Sum(); total += dmg;
            var f = new Fighter
            {
                Id = c.Id, Name = c.Name, Class = c.Class, CombatPower = c.Cp, You = c.Id == 1, Damage = dmg, BossDamage = dmg, PerSecond = ps, BossPerSecond = ps.ToList(),
                FirstHit = start, LastHit = start.AddSeconds(secs), ActiveSeconds = secs * 0.94,
                Abilities = { new AbilityShare { Skill = c.Class switch { "Gladiator" => 11010000, "Sorcerer" => 15040000, "Ranger" => 14010000, "Templar" => 12090000, "Cleric" => 17040000, _ => 16010000 }, Damage = dmg, Hits = secs / 2, BossDamage = dmg, BossHits = secs / 2 } },
            };
            if (taken)
            {
                f.TakenPerSecond = taken0[c.Id].ToList();
                f.Taken = new() { new TakenShare { Source = "Ground Slam", Damage = (long)(f.TakenPerSecond.Sum() * 0.6), Hits = secs / 3 }, new TakenShare { Source = "Searing Breath", Damage = f.TakenPerSecond.Sum() - (long)(f.TakenPerSecond.Sum() * 0.6), Hits = secs / 20 } };
            }
            if (heals)
            {
                // the Cleric and the Chanter heal the group (effective: what the damage taken left room for); the Templar only himself
                double k = c.Class == "Cleric" ? 0.62 : c.Class == "Chanter" ? 0.24 : c.Class == "Templar" ? 0.05 : 0;
                if (k > 0)
                {
                    var hs = new List<long>(n);
                    for (int i = 0; i < n; i++) hs.Add(Down(c.Id, i) ? 0 : (long)(groupTaken[Math.Max(0, i - 1)] * k * (0.6 + Hash(i, c.Id, 9) * 0.8) + (c.Class == "Cleric" ? 300 : 0)));
                    f.HealPerSecond = hs;
                    f.HealingEffective = hs.Sum(); f.Healing = (long)(f.HealingEffective * 1.3);
                    f.HealingSelf = c.Class == "Templar" ? f.Healing : f.Healing / 20;
                }
            }
            foreach (var (id, at) in deaths.Where(d => d.Id == c.Id))
            {
                (f.DeathLog ??= new()).Add(new DeathRecord { At = at, Killer = "Searing Breath", BossHpLeft = null });
                f.Deaths++;
            }
            e.Roster.Add(f);
        }
        // the boss's HP from the group's damage: gone at a kill's last second, the share left at a wipe, about half way live
        double max = live ? total * 2.05 : wipe ? total / (1 - 0.31) : total;
        e.BossMaxHp = (long)max; e.BossHp = Math.Max(0, (long)(max - total));
        var hp = new List<double>(n);
        double run = 0;
        for (int i = 0; i < n; i++) { run += cum[i]; hp.Add(Math.Round(Math.Clamp(1 - run / max, 0, 1), 4)); }
        if (!live && outcome == "kill") hp[^1] = 0;
        e.BossHpPerSecond = hp;
        if (size == 1) e.Boss = "Watchdog Kwapo";
        return e;
    }

    /// <summary>
    /// For the options test's CPU measure: the demo's live pull <paramref name="dt"/> seconds on, each member's damage (and the
    /// heals and damage taken) for that time added at the second it falls in, as hits arrive.
    /// </summary>
    internal void GraphsLiveStepForTest(double dt)
    {
        if (_parserDemoLive is not { } e) return;
        _parserDemoNow = _parserDemoNow.AddSeconds(dt);
        e.LastAt = _parserDemoNow;
        int sec = e.SecondOf(_parserDemoNow);
        long all = 0;
        foreach (var f in e.Members)
        {
            var c = GraphCast.FirstOrDefault(x => x.Id == f.Id);
            long amount = (long)((c.Dps > 0 ? c.Dps : 3_000) * dt * (0.6 + Hash(sec, f.Id) * 0.8));
            f.Damage += amount; f.BossDamage += amount; all += amount;
            f.PerSecond = Fighter.AddAt(f.PerSecond, sec, amount);
            f.BossPerSecond = Fighter.AddAt(f.BossPerSecond, sec, amount);
            if (f.TakenPerSecond != null) f.TakenPerSecond = Fighter.AddAt(f.TakenPerSecond, sec, (long)(400 * dt));
            if (f.HealPerSecond != null) { long h = (long)(900 * dt); f.HealPerSecond = Fighter.AddAt(f.HealPerSecond, sec, h); f.HealingEffective += h; f.Healing += h; }
        }
        e.BossHp = Math.Max(1, e.BossHp - all);
        if (e.BossHpPerSecond is { } hps) { while (hps.Count <= sec) hps.Add(hps.Count > 0 ? hps[^1] : 1); hps[sec] = e.BossHpLeft ?? hps[sec]; }
    }
}
