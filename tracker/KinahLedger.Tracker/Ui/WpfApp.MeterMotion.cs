using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using KinahLedger.Parser;

namespace KinahLedger.Ui;

public static partial class WpfApp
{
    /// <summary>The meter's looks the motion is drawn in: the Parser's own window in each look, and the Kinah overlay's two ways of holding it.</summary>
    static readonly string[] MeterPlaces = { "standard", HudLooks.Auto, HudLooks.Capsule, HudLooks.Meter, HudLooks.Float, "kinah-panel", "kinah-auto" };

    /// <summary>The meter's six motions, each a change from one draw to the next (the second held at t).</summary>
    sealed record MeterChange(string Name, Encounter From, Encounter To, DateTime At, DateTime ToAt, Func<Encounter, PullRef?>? Best = null);

    /// <summary>
    /// The DPS meter's motion (2026-11-15) as changes from the sample: the ghost of the bars and the boss HP's red chunk, the
    /// odometer, big-hit pop-ups, an overtake, the end of a fight (the sweep, the AVG. DPS stamp, the TOP DPS plate), a
    /// personal best's flare, and the heat of your pace (a state: three paces, settled).
    /// </summary>
    static List<MeterChange> MeterChanges(DateTime now)
    {
        var (_, live) = ParserDemo.Sample(now, live: true);
        var a = live!;
        var you = a.Roster.First(f => f.You);
        var others = a.Shown().Where(f => !f.You).ToList();
        // the ghost: one member's damage jumps (their share up, everyone else's down), the boss's HP drops 9%
        var ghost = BossParser.Copy(a);
        Add(ghost.Roster.First(f => f.Id == others[^1].Id), a.Credited / 6);
        ghost.BossHp = Math.Max(0, ghost.BossHp - (long)(ghost.BossMaxHp * 0.09));
        // the odometer: your damage up by a few percent, every figure moves
        var odo = BossParser.Copy(a);
        foreach (var f in odo.Roster) Add(f, f.Damage / (f.You ? 14 : 30));
        // pop-ups: three big hits of yours (one a crit), two of the lead's, one of a third's: two a row, four in all
        var pops = BossParser.Copy(a);
        foreach (var f in pops.Roster) Add(f, 1);
        pops.BigHits = new List<BigHit>
        {
            new(1, you.Id, 858_000, true, now), new(2, others[0].Id, 412_000, null, now), new(3, you.Id, 531_000, false, now),
            new(4, others[0].Id, 377_000, null, now), new(5, you.Id, 299_000, true, now), new(6, others[1].Id, 640_000, null, now),
        };
        // the overtake: the second passes the first
        var swap = BossParser.Copy(a);
        var top2 = swap.Shown().Take(2).ToList();
        Add(top2[1], swap.Figure(top2[0]) - swap.Figure(top2[1]) + swap.Figure(top2[0]) / 40);
        // the end: a kill (the sweep, the stamp, the plate)
        var kill = BossParser.Copy(a); kill.End = now; kill.Outcome = "kill"; kill.BossHp = 0;
        // a personal best on the kill: the figure flares gold with the PB chip
        var pb = BossParser.Copy(kill); var me = pb.Roster.First(f => f.You); Add(me, (long)(pb.Figure(me) * (4190.0 / Math.Max(1, kill.Dps(kill.You!, now)) - 1)));
        // the open world's fight held after 5 quiet seconds
        var heldLive = HeldSample(a, now); heldLive.End = null; heldLive.Outcome = "";
        var held = HeldSample(a, now);
        // the heal line: the Templar heals himself a big burst, so the Cleric's share of the group's healing falls and their line
        // slides shorter (the Cleric's figures stay; the Templar still gets no line: he healed no one else)
        var heal = BossParser.Copy(a);
        var tank = heal.Roster.First(f => f.Class == "Templar");
        long burst = heal.GroupHealing; tank.Healing += burst; tank.HealingSelf += burst; tank.HealingEffective += burst / 3;
        // a big hit of the Cleric's: its pop-up beside the heal figures, never over them
        var popsHealer = BossParser.Copy(a);
        foreach (var f in popsHealer.Roster) Add(f, 1);
        popsHealer.BigHits = new List<BigHit> { new(1, popsHealer.Roster.First(f => f.HealingOthers > 0).Id, 96_000, null, now) };
        double yourDps = a.DpsOf(you, now) ?? 1;
        // damage added to a member: to every target and to the boss alike (the meter counts the boss's when the record splits them)
        static void Add(Fighter f, long d) { f.Damage += d; f.BossDamage += d; }
        return new List<MeterChange>
        {
            new("ghost", a, ghost, now, now),
            new("pops", a, pops, now, now),
            new("overtake", a, swap, now, now),
            new("finish", a, kill, now, now),
            new("finish-pb", a, pb, now, now, _ => new PullRef { Dps = 4020 }),
            new("finish-held", heldLive, held, now.AddSeconds(-5), held.End!.Value),
            new("heal", a, heal, now, now),
            new("pops-healer", a, popsHealer, now, now),
            new("heat-near", a, a, now, now, _ => new PullRef { Dps = yourDps / 0.9 }),
            new("heat-beat", a, a, now, now, _ => new PullRef { Dps = yourDps / 1.06 }),
        };
    }

    /// <summary>
    /// ui-shot --parser meter: every motion of the meter in every look, as held frames (ui-meter-NAME-PLACE-NNN.png, the
    /// course at t = NNN/100), and the settled frame with the tracker still (…-still.png). KL_MOTION_AT=t renders that frame
    /// alone. Every window is offscreen and never activated.
    /// </summary>
    static void ShotMeterMotion(string dir, Dictionary<string, string> kinah)
    {
        var now = new DateTime(2026, 11, 13, 21, 30, 0, DateTimeKind.Utc);
        bool still0 = Motion.Still;
        var frozen0 = Motion.FrozenAt;
        var best0 = ParserRows.BestOf;
        string sub = Path.Combine(dir, "meter");
        Directory.CreateDirectory(sub);
        double[] frames = frozen0 is double at0 ? new[] { at0 } : new[] { 0.1, 0.25, 0.4, 0.55, 0.7, 0.85, 1.0 };
        try
        {
            foreach (var c in MeterChanges(now))
                foreach (var place in MeterPlaces)
                {
                    bool state = c.Name.StartsWith("heat", StringComparison.Ordinal);
                    foreach (var t in state ? new[] { 1.0 } : frames)
                        Save(MeterFrame(c, place, kinah, t), Path.Combine(sub, $"ui-meter-{c.Name}-{place}-{(state ? "state" : $"{(int)Math.Round(t * 100):000}")}.png"), 0, ShotGround);
                    Save(MeterFrame(c, place, kinah, null), Path.Combine(sub, $"ui-meter-{c.Name}-{place}-still.png"), 0, ShotGround);
                }
        }
        finally { Motion.FrozenAt = frozen0; Motion.Still = still0; ParserRows.BestOf = best0; }
    }

    /// <summary>
    /// One change drawn in one place: the first draw lays the meter down (still), the second is the change, held at
    /// <paramref name="t"/> (null: the tracker still, the settled state). The picture of the meter, its window closed.
    /// </summary>
    static FrameworkElement MeterFrame(MeterChange c, string place, Dictionary<string, string> kinah, double? t)
    {
        Motion.FrozenAt = null; Motion.Still = true; Motion.ForgetFigures();
        ParserRows.BestOf = c.Best ?? (_ => null);
        Window win;
        Action<Encounter, DateTime> draw;
        if (place.StartsWith("kinah", StringComparison.Ordinal))
        {
            var h = new HudWindow(null, place == "kinah-auto" ? HudLooks.Auto : null, still: true) { Left = -12000, Top = -12000, ShowActivated = false };
            h.Update(kinah);
            draw = (e, at) => h.ShowParser(e, at, ParserMode.Bosses, details: false);
            win = h;
        }
        else
        {
            var o = new ParserOverlay(still: true) { Left = -12000, Top = -12000, Mode = c.To.ModeKind };
            if (place != "standard") o.Look = place;
            draw = (e, at) => o.Draw(e, at);
            win = o;
        }
        draw(c.From, c.At); win.Show(); DoEvents();
        Motion.FrozenAt = t; Motion.Still = t == null;
        draw(c.To, c.ToAt); DoEvents();
        // a picture of the window's content, kept after the window closes
        var root = (FrameworkElement)win.Content;
        var shot = Snapshot(root);
        win.Close();
        Motion.FrozenAt = null; Motion.Still = true;
        return shot;
    }

    /// <summary>The element drawn into a picture (so its window can close before the picture is saved).</summary>
    static FrameworkElement Snapshot(FrameworkElement root)
    {
        double dpi = 96 * VisualTreeHelper.GetDpi(root).DpiScaleX;
        int w = Math.Max(1, (int)Math.Ceiling(root.ActualWidth)), h = Math.Max(1, (int)Math.Ceiling(root.ActualHeight));
        var bmp = new System.Windows.Media.Imaging.RenderTargetBitmap((int)Math.Ceiling(w * dpi / 96), (int)Math.Ceiling(h * dpi / 96), dpi, dpi, PixelFormats.Pbgra32);
        bmp.Render(root);
        bmp.Freeze();
        var img = new Image { Source = bmp, Width = w, Height = h, Stretch = Stretch.Fill };
        img.Measure(new Size(w, h)); img.Arrange(new Rect(0, 0, w, h));
        var host = new Border { Child = img, Width = w, Height = h };
        host.Measure(new Size(w, h)); host.Arrange(new Rect(0, 0, w, h));
        return host;
    }

    /// <summary>
    /// options-test: each motion of the meter settles where it should (held at its end: no pop-up left, no ghost, the
    /// odometer saying the figure, the rows in place, the badge and plate at rest), the tracker still shows no transient at
    /// all, a course part-way shows it (pop-ups capped, crits gold only where the record says so), the DPS's column never
    /// moves, the heat follows your pace, and the Parser keeps big hits on the live copy only.
    /// </summary>
    static void MeterMotionChecks(Action<bool, string> check)
    {
        var now = new DateTime(2026, 11, 13, 21, 30, 0, DateTimeKind.Utc);
        bool still0 = Motion.Still;
        var frozen0 = Motion.FrozenAt;
        var best0 = ParserRows.BestOf;
        try
        {
            var changes = MeterChanges(now);
            foreach (var c in changes)
            {
                var still = Probe(c, null);
                var settled = Probe(c, 1);
                var mid = Probe(c, 0.5);
                bool heat = c.Name.StartsWith("heat", StringComparison.Ordinal);
                check(!still.Moving && still.Pops.Count == 0 && still.Badge is null or (1, 1) && still.PlateAtRest && still.Chunk == false,
                    $"meter motion ({c.Name}): the tracker still shows no transient (pops {still.Pops.Count}, moving {still.Moving}, badge {still.Badge}, plate at rest {still.PlateAtRest}, HP chunk {still.Chunk})");
                check(settled.Pops.Count == 0 && settled.Badge is null or (1, 1) && settled.PlateAtRest && settled.Chunk == false && !settled.Lifted && !settled.Ghost,
                    $"meter motion ({c.Name}): held at its end it settles (pops {settled.Pops.Count}, badge {settled.Badge}, plate at rest {settled.PlateAtRest}, HP chunk {settled.Chunk}, odometer {(settled.Dps.All(d => d.Value.Figure == d.Value.Odometer) ? "says the figure" : "differs: " + string.Join(", ", settled.Dps.Select(d => $"{d.Value.Figure}/{d.Value.Odometer}")))}, lifted {settled.Lifted}, ghost {settled.Ghost})");
                check(still.Dps.Keys.All(k => settled.Dps.TryGetValue(k, out var s) && s.Figure == still.Dps[k].Figure),
                    $"meter motion ({c.Name}): the settled figures are the still ones");
                // the DPS's column: where each row's figure ends, part-way and settled, as when still
                static bool Same(List<double> x, List<double> y) => x.Count == y.Count && x.Order().Zip(y.Order()).All(p => Math.Abs(p.First - p.Second) < 0.6);
                bool column = Same(mid.DpsRights, still.DpsRights) && Same(settled.DpsRights, still.DpsRights) && (mid.OdoRights.Count == 0 || Same(mid.OdoRights, mid.DpsRights)) && (settled.OdoRights.Count == 0 || Same(settled.OdoRights, still.DpsRights));
                check(column, $"meter motion ({c.Name}): the DPS's column never moves, rolling or not ({string.Join(",", still.DpsRights.Select(x => x.ToString("0.0")))} / {string.Join(",", mid.DpsRights.Select(x => x.ToString("0.0")))} / odometer {string.Join(",", mid.OdoRights.Select(x => x.ToString("0.0")))})");
                switch (c.Name)
                {
                    case "ghost": check(mid.Ghost && mid.Chunk == true, $"meter motion (ghost): part-way the bars' ghost and the boss HP's red chunk show (ghost {mid.Ghost}, chunk {mid.Chunk})"); break;
                    case "pops":
                        var rowsOf = mid.Pops.GroupBy(p => p.Row).ToList();
                        int perRow = rowsOf.Select(g => g.Count()).DefaultIfEmpty(0).Max();
                        bool apart = rowsOf.All(g => g.OrderBy(p => p.Left).Zip(g.OrderBy(p => p.Left).Skip(1)).All(z => z.Second.Left - z.First.Right >= ParserRows.PopGap - 0.5));
                        bool clear = mid.Pops.All(p => p.Left >= p.NameEnd - 0.5 && p.Right <= p.DpsLeft + 0.5);
                        bool inRow = mid.Pops.All(p => Math.Abs(p.BandTop - p.RowTop) < 0.5 && Math.Abs(p.BandBottom - p.RowBottom) < 0.5 && p.Top >= p.BandTop - 0.5 && p.Bottom <= p.BandBottom + 0.5);
                        bool gold = mid.Pops.Any(p => p.Word == "pop-crit:+299K") && !mid.Pops.Any(p => p.Word.Contains("+858K")) && mid.Pops.All(p => p.Word.StartsWith("pop-crit:") == (p.Word.EndsWith("+858K") || p.Word.EndsWith("+299K")));
                        check(mid.Pops.Count > 0 && mid.Pops.Count <= ParserRows.PopsInAll && perRow <= ParserRows.PopsPerRow && apart && clear && inRow && gold,
                            $"meter motion (pops): four at most, two a row (a third takes the oldest's place), apart, in their own row, clear of the name and the DPS, gold only for a crit (count {mid.Pops.Count}, per row {perRow}, apart {apart}, clear {clear}, in row {inRow}, gold {gold}: {string.Join(", ", mid.Pops.Select(p => $"{p.Row}:{p.Word}@{p.Left:0}-{p.Right:0}/{p.NameEnd:0}-{p.DpsLeft:0}"))})");
                        break;
                    case "pops-healer":
                        bool clearH = mid.Pops.All(p => p.Left >= p.NameEnd - 0.5 && p.Right <= p.DpsLeft + 0.5);
                        bool inRowH = mid.Pops.All(p => p.Top >= p.BandTop - 0.5 && p.Bottom <= p.BandBottom + 0.5);
                        check(mid.Pops.Count == 1 && clearH && inRowH,
                            $"meter motion (pops-healer): a healer's big hit pops in its row clear of the name and the heal figures under it, and of the DPS ({string.Join(", ", mid.Pops.Select(p => $"{p.Row}:{p.Word}@{p.Left:0}-{p.Right:0}/{p.NameEnd:0}-{p.DpsLeft:0}"))})");
                        break;
                    case "overtake": check(mid.Lifted, "meter motion (overtake): part-way the one who passed is lifted, with its shadow"); break;
                    case "finish": check(mid.Sweep == "sweep-good" && mid.Badge is { } b && b.Opacity > 0 && Math.Abs(b.Scale - 1) > 0.01 && !mid.PlateAtRest, $"meter motion (finish): part-way the light sweeps, the AVG. DPS badge stamps, the TOP DPS plate slides in (sweep {mid.Sweep}, badge {mid.Badge}, plate at rest {mid.PlateAtRest})"); break;
                    case "finish-pb": check(mid.Flare, "meter motion (finish-pb): part-way your figure flares gold"); break;
                    case "finish-held": check(mid.Sweep == "sweep-quiet" && mid.Badge is { } hb && Math.Abs(hb.Scale - 1) > 0.01, $"meter motion (finish-held): the open world's held fight sweeps and stamps its badge (sweep {mid.Sweep}, badge {mid.Badge})"); break;
                    case "heat-near": check(still.Heat == "heat-warm" && settled.Heat == "heat-warm", $"meter motion (heat): at 90% of your best your row's edge is warm ({still.Heat})"); break;
                    case "heat-beat": check(still.Heat == "heat-beat" && settled.Heat == "heat-beat", $"meter motion (heat): beating your best it is gold ({still.Heat})"); break;
                }
            }
            // a course replaced part-way leaves nothing behind: a pass, passed back mid-glide, then settled: no shadow on a row,
            // no glow left on a chip (yours back to its own), every row at rest
            {
                var c = changes.First(x => x.Name == "overtake");
                Motion.FrozenAt = null; Motion.Still = true; Motion.ForgetFigures();
                ParserRows.BestOf = _ => null;
                var o = new ParserOverlay(still: true) { Left = -12000, Top = -12000, Mode = c.To.ModeKind };
                o.Draw(c.From, c.At); o.Show(); DoEvents();
                var rows = Find<ParserRows>((DependencyObject)o.Content).First();
                var chips0 = rows.ChipLooksForTest();
                Motion.FrozenAt = 0.5; Motion.Still = false;
                o.Draw(c.To, c.At); DoEvents();
                bool lifted = rows.LiftedForTest();
                var back = BossParser.Copy(c.From); foreach (var f in back.Roster) { f.Damage += 2; f.BossDamage += 2; }
                o.Draw(back, c.At); DoEvents();
                Motion.FrozenAt = 1;
                var again = c.To;   // passed again, held at its end: what the course replaced left must be gone
                o.Draw(again, c.At); DoEvents();
                var chips1 = rows.ChipLooksForTest();
                bool rest = !rows.LiftedForTest() && rows.RowEffectsForTest() == 0 && chips0.SequenceEqual(chips1);
                o.Close(); Motion.FrozenAt = null; Motion.Still = true;
                check(lifted && rest, $"meter motion (overtake): passed back part-way, nothing is left behind (lifted first {lifted}; row effects after {rows.RowEffectsForTest()}; chips {string.Join(",", chips0)} / {string.Join(",", chips1)})");
            }
            // no heat without a best, or below 80% of it
            {
                var c = changes.First(x => x.Name == "heat-near");
                var none = Probe(c with { Best = _ => null }, null);
                var low = Probe(c with { Best = _ => new PullRef { Dps = (c.From.DpsOf(c.From.You!, now) ?? 1) / 0.5 } }, null);
                check(none.Heat == null && low.Heat == null, $"meter motion (heat): none without a best or at half of it ({none.Heat}, {low.Heat})");
            }
        }
        finally { Motion.FrozenAt = frozen0; Motion.Still = still0; ParserRows.BestOf = best0; }

        // the Parser: a hit of three times the member's average (after eight) is a big hit, on the live copy only
        {
            var p = new BossParser();
            var t = DateTime.UtcNow;
            p.SetMode(ParserMode.Bosses, t);
            p.Take(new KinahLedger.Packets.InstanceEntered(t, KinahLedger.Packets.ContentKind.Transcendence, "Shattered Arkanis [Stage 3]", 1));
            p.Take(new KinahLedger.Packets.GroupChanged(t, new[] { new KinahLedger.Packets.GroupMember(1, "Swoles", "Gladiator", 4_500, 5_600, true), new KinahLedger.Packets.GroupMember(2, "Vexmora", "Cleric", 4_400, 5_600, false) }));
            p.Take(new KinahLedger.Packets.BossEngaged(t, "Ruthilis of Pain", 99));
            string? key0 = Encounter.CritKey;
            Encounter.CritKey = null;
            for (int i = 0; i < 12; i++)
            {
                var at = t.AddMilliseconds(200 * i);
                long amt = i == 4 ? 9_000 : i == 9 ? 6_000 : i == 11 ? 2_000 : 1_000;
                p.Take(new KinahLedger.Packets.DamageDealt(at, 1, 99, amt));
                p.Take(new KinahLedger.Packets.HitDealt(at, 1, 1, 99, amt, 1100, i, 0, false, false, 3, 0, 0, 0));
                if (i == 10) p.Take(new KinahLedger.Packets.HitDealt(at, 1, 1, 99, 50_000, 1100, i, 0, false, true, 3, 0, 0, 0));   // a tick: never a hit
            }
            var live = p.Live;
            var big = live?.BigHits ?? new List<BigHit>();
            check(big.Count == 1 && big[0].Amount == 6_000 && big[0].Member == 1 && big[0].Crit == null,
                $"meter motion (parser): a big hit is three times the member's average after eight hits, ticks aside, never a crit without crit data ({string.Join(", ", big.Select(b => $"{b.Amount}/{b.Crit}"))})");
            Encounter.CritKey = "result:3";
            var at2 = t.AddSeconds(3);
            p.Take(new KinahLedger.Packets.DamageDealt(at2, 1, 99, 40_000));
            p.Take(new KinahLedger.Packets.HitDealt(at2, 1, 1, 99, 40_000, 1100, 20, 0, false, false, 3, 0, 0, 0));
            var big2 = p.Live?.BigHits ?? new List<BigHit>();
            Encounter.CritKey = key0;
            string json = live != null ? System.Text.Json.JsonSerializer.Serialize(live) : "";
            check(big2.Count == 2 && big2[^1].Crit == true && big2[^1].Seq > big2[0].Seq && !json.Contains("BigHits") && !json.Contains("StreamHitCount"),
                $"meter motion (parser): with crit data a crit says so, each hit its own order, and nothing of it is written ({string.Join(", ", big2.Select(b => $"{b.Seq}:{b.Amount}/{b.Crit}"))})");
        }
    }

    sealed record MeterProbe(bool Moving, List<(string Row, string Word, double Left, double Right, double Top, double Bottom, double BandTop, double BandBottom, double RowTop, double RowBottom, double NameEnd, double DpsLeft)> Pops, (double Opacity, double Scale)? Badge, bool PlateAtRest, bool? Chunk,
        Dictionary<string, (string Figure, string Odometer)> Dps, List<double> DpsRights, List<double> OdoRights, bool Lifted, bool Ghost, string? Sweep, bool Flare, string? Heat);

    /// <summary>One change drawn in the Parser's own window (the standard look), held at <paramref name="t"/> (null: still), and what shows.</summary>
    static MeterProbe Probe(MeterChange c, double? t)
    {
        Motion.FrozenAt = null; Motion.Still = true; Motion.ForgetFigures();
        ParserRows.BestOf = c.Best ?? (_ => null);
        var o = new ParserOverlay(still: true) { Left = -12000, Top = -12000, Mode = c.To.ModeKind };
        o.Draw(c.From, c.At); o.Show(); DoEvents();
        Motion.FrozenAt = t; Motion.Still = t == null;
        o.Draw(c.To, c.ToAt); DoEvents();
        var root = (FrameworkElement)o.Content;
        var rows = Find<ParserRows>(root).First();
        var pops = rows.PopsForTest();
        var badges = TaggedShown(root, "avg-badge");
        (double, double)? badge = badges.Count == 0 ? null : (Math.Round(badges[0].Opacity, 3), badges[0].RenderTransform is ScaleTransform s ? Math.Round(s.ScaleX, 3) : 1);
        var plates = TaggedShown(root, "top-dps");
        bool plateAtRest = plates.All(p => p.Opacity >= 0.999 && p.RenderTransform is not TranslateTransform { X: > 0.01 });
        var chunks = Find<Border>(root).Where(b => b.Tag?.GetType().Name == "HpChunk").ToList();
        bool? chunk = chunks.Count == 0 ? false : chunks.Any(b => b.Visibility == Visibility.Visible && b.Width > 0.5);
        var dpsRights = DpsRights(root);
        var odoRights = Find<Odometer>(root).Where(x => x.Visibility == Visibility.Visible).Select(x => x.TranslatePoint(new Point(x.ActualWidth, 0), root).X).ToList();
        var probe = new MeterProbe(rows.AnyMotionForTest(), pops, badge, plateAtRest, chunk, rows.DpsForTest(), dpsRights, odoRights, rows.LiftedForTest(), rows.GhostForTest(), rows.SweepForTest(), rows.FlareForTest(), rows.HeatForTest());
        o.Close();
        Motion.FrozenAt = null; Motion.Still = true;
        return probe;
    }

    /// <summary>Every element of a type under <paramref name="root"/> (the visual tree).</summary>
    static List<T> Find<T>(DependencyObject root) where T : DependencyObject
    {
        var found = new List<T>();
        void Walk(DependencyObject d)
        {
            if (d is T x) found.Add(x);
            if (d is Visual) for (int i = 0; i < VisualTreeHelper.GetChildrenCount(d); i++) Walk(VisualTreeHelper.GetChild(d, i));
        }
        Walk(root);
        return found;
    }
}
