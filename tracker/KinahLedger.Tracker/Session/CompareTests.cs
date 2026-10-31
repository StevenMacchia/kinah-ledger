using System.Globalization;
using System.Text.Json;
using KinahLedger.Data;

namespace KinahLedger.Session;

/// <summary>
/// Compare (0.9.3), part of parse-test: the switch is off by default (a new install, an old ui.json, Options); Zones
/// ranks the week's zones by made an hour and leaves out a zone under ten minutes, the unread zone and the days before
/// history began; Places ranks by worth a run over the counted runs only; two picks at most, the zone here picked to
/// start with; your usual here waits for ten minutes today and thirty on earlier days, and your best here for a day of
/// ten minutes there; a run is set against its place only with three earlier runs, its rank counted among all of them;
/// the verdict lines. No file is written.
/// </summary>
public static class CompareTests
{
    public static bool Run(ItemDictionary items, Settings settings)
    {
        bool ok = true;
        void Check(string what, string got, string expect)
        {
            bool pass = got == expect; ok &= pass;
            Console.WriteLine($"  {what,-62} -> {got,-40} {(pass ? "ok" : "MISMATCH, expected " + expect)}");
        }
        static string Fk(double v) => v >= 1e6 ? $"{v / 1e6:0.00}M" : v >= 1e4 ? $"{v / 1e3:0}K" : v.ToString("0", CultureInfo.InvariantCulture);
        Console.WriteLine("\nCompare: zones and places this week, your usual here, a run against your runs there (0.9.3):");

        // a. the switch: off for a new install, off when an older ui.json doesn't have it, off in Options' defaults
        Check("Compare zones and runs: off for a new install", new UserPrefs().CompareZones.ToString(), "False");
        var old = JsonSerializer.Deserialize<UserPrefs>("{\"FollowActivity\":true,\"Skin\":\"night\"}")!;
        Check("...off when ui.json is from before 0.9.3", old.CompareZones.ToString(), "False");
        var back = JsonSerializer.Deserialize<UserPrefs>(JsonSerializer.Serialize(new UserPrefs { CompareZones = true }))!;
        Check("...and kept once turned on", back.CompareZones.ToString(), "True");
        var choice = new Ui.OptionsChoice("na_e", "", true, true, "", "night", true, false, "open", false, true, true, "");
        Check("...off in Options' choices unless set", choice.Compare.ToString(), "False");

        // b. Zones: the week's zones best an hour first; under ten minutes, the unread zone and days before history left out
        var today = DateTime.Today;
        FarmDay Day(int ago, params (string Zone, int Mins, int Kills, long Made, string Best, int N, long W)[] zones)
        {
            var d = new FarmDay { Day = DayStore.DayOf(today.AddDays(-ago)) };
            foreach (var z in zones) d.Zones.Add(new FarmDayZone { Zone = z.Zone, Active = z.Mins * 60, Kills = z.Kills, Made = z.Made, Best = z.Best, BestCount = z.N, BestWorth = z.W });
            d.Made = d.Zones.Sum(z => z.Made); d.Active = d.Zones.Sum(z => z.Active); d.Kills = d.Zones.Sum(z => z.Kills);
            return d;
        }
        HistoryDay H(FarmDay f, int ago, bool before = false) => new(today.AddDays(-ago), f.Day, ago == 0, before, f, RunsDay.None);
        var week = new List<HistoryDay>
        {
            H(Day(6, ("Kraka Footpath", 60, 600, 9_000_000, "", 0, 0)), 6, before: true),   // before history: adds nothing
            H(Day(4, ("Kraka Footpath", 20, 300, 100_000, "Spiritstone", 4, 28_000), ("Pilgrim's Ridge", 9, 50, 60_000, "", 0, 0)), 4),
            H(Day(2, ("Kraka Footpath", 12, 150, 60_000, "Spiritstone", 2, 14_000), ("Altamia Canyon", 30, 200, 120_000, "Orichalcum Ore", 8, 16_000)), 2),
            H(Day(1, (SessionEngine.UnknownZone, 40, 100, 900_000, "", 0, 0), ("Pilgrim's Ridge", 0, 0, 0, "", 0, 0), ("Ellun River Marsh", 10, 40, 20_000, "Fresh Fruit", 6, 6_000)), 1),
            H(Day(0, ("Altamia Canyon", 15, 100, 50_000, "Thick hide", 9, 30_000)), 0),
        };
        var zones = ZoneCompare.Zones(week);
        Check("Zones: best an hour first, each with its time", string.Join(", ", zones.Select(z => $"{z.Zone} {Fk(z.PerHour)} {z.Active / 60:0}m")), "Kraka Footpath 300K 32m, Altamia Canyon 227K 45m, Ellun River Marsh 120K 10m");
        Check("...under 10 minutes this week left out (Pilgrim's Ridge 9 min)", zones.Any(z => z.Zone == "Pilgrim's Ridge").ToString(), "False");
        Check("...exactly 10 minutes is kept", zones.Any(z => z.Zone == "Ellun River Marsh").ToString(), "True");
        Check("...the unread zone and days before history left out", zones.Any(z => z.Zone == SessionEngine.UnknownZone || z.Made > 1_000_000).ToString(), "False");
        Check("...kills an hour, a kill, days and the best drop", $"{zones[0].KillsHour:0} {zones[0].PerKill:0} {zones[0].Days} {zones[0].BestDrop}", "844 356 2 Spiritstone ×6");
        Check("...the best drop is the one worth most over the days", zones[1].BestDrop, "Thick hide ×9");

        // c. picks: the zone here to start with, two at most, a pick off again, picks no longer listed dropped
        var names = zones.Select(z => z.Zone).ToList();
        Check("picks: the zone here is picked to start with", string.Join(",", ZoneCompare.Picks(new(), false, names, "altamia canyon")), "Altamia Canyon");
        Check("...none when here isn't listed (under 10 minutes)", ZoneCompare.Picks(new(), false, names, "Pilgrim's Ridge").Count.ToString(), "0");
        var p = ZoneCompare.Toggle(new() { "Altamia Canyon" }, "Kraka Footpath");
        Check("...a tick adds a second", string.Join(",", p), "Altamia Canyon,Kraka Footpath");
        p = ZoneCompare.Toggle(p, "Ellun River Marsh");
        Check("...a third makes room by dropping the oldest", string.Join(",", p), "Kraka Footpath,Ellun River Marsh");
        p = ZoneCompare.Toggle(p, "kraka footpath");
        Check("...a picked one's tick takes it off", string.Join(",", p), "Ellun River Marsh");
        Check("...a pick no longer listed is dropped", string.Join(",", ZoneCompare.Picks(new() { "Pilgrim's Ridge", "Kraka Footpath" }, true, names, null)), "Kraka Footpath");

        // d. the verdicts
        var (l1, b1, r1) = ZoneCompare.ZoneVerdict(zones[1], zones[0], Fk);
        Check("zone verdict: the better one first, by how much an hour", l1 + b1 + r1, "Kraka Footpath made 73K more an hour, mostly from more kills.");
        var near = zones[0] with { Zone = "Twin Zone", Made = zones[0].Made * 96 / 100 };
        var (l2, b2, r2) = ZoneCompare.ZoneVerdict(zones[0], near, Fk);
        Check("...within a tenth: about the same", l2 + b2 + r2, "Close: Kraka Footpath and Twin Zone made about the same an hour this week.");

        // e. Places: worth a run over the counted runs, flagged runs left out of the figures, a place with none left out
        var now = DateTime.UtcNow;
        InstanceRun R(string zone, double hoursAgo, long kinah, int mins, bool flagged = false, string ev = "")
        {
            var r = new InstanceRun { Zone = zone, Event = ev, Kind = ev.Length > 0 ? "Event" : "Expedition", Start = now.AddHours(-hoursAgo), LastLoot = now.AddHours(-hoursAgo).AddMinutes(mins), Kinah = kinah };
            if (flagged) r.PartlyRead = true;
            return r;
        }
        var runs = new List<InstanceRun>
        {
            R("Urugugu Canyon", 30, 600_000, 14), R("Urugugu Canyon", 20, 700_000, 14), R("Urugugu Canyon", 10, 9_000_000, 14, flagged: true),
            R("Vakron Sky Island", 25, 500_000, 8), R("Vakron Sky Island", 15, 520_000, 8), R("Vakron Sky Island", 5, 480_000, 8),
            R("Deus Research Base", 3, 400_000, 20, flagged: true),
        };
        var places = ZoneCompare.Places(runs, r => r.Kinah);
        Check("Places: best a run first, counted runs only", string.Join(", ", places.Select(x => $"{x.Place} {Fk(x.PerRun)} {x.Counted}/{x.Runs}")), "Urugugu Canyon 650K 2/3, Vakron Sky Island 500K 3/3");
        Check("...the best run and the hour", $"{Fk(places[0].BestRun)} {Fk(places[0].PerHour)} {Fk(places[1].PerHour)}", "700K 2.79M 3.75M");
        var (l3, b3, r3) = ZoneCompare.PlaceVerdict(places[0], places[1], Fk);
        Check("place verdict: more a run, but the quicker one an hour", l3 + b3 + r3, "Urugugu Canyon pays 150K more a run, but Vakron Sky Island is quicker, so it makes more an hour.");

        // f. your usual here: ten minutes today and thirty on earlier days, else nothing; the best day needs ten minutes there
        var earlier = new List<FarmDay>
        {
            Day(3, ("Western Cantas Valley", 20, 100, 205_000, "", 0, 0)),
            Day(2, ("Western Cantas Valley", 9, 40, 300_000, "", 0, 0)),   // 2.0M an hour but under ten minutes: not a best day
            Day(1, ("Western Cantas Valley", 15, 80, 375_000, "", 0, 0)),
        };
        var u = ZoneCompare.Usual("Western Cantas Valley", earlier, 400_000, 1_200);
        Check("usual here: over the earlier days, today's, the best day", u == null ? "none" : $"{Fk(u.Usual)} {Fk(u.Today)} {Fk(u.Best ?? 0)}", "1.20M 1.20M 1.50M");
        // the difference is the card's own hour (this stay's, the big figure) less the usual: never a different figure
        Check("versus: the headline 1.27M against a usual 805K", ZoneCompare.Versus(805_000, 1_270_000, 2_520, Fk)?.Text ?? "none", "465K more than usual");
        Check("...less than usual", ZoneCompare.Versus(1_200_000, 1_000_000, 1_800, Fk)?.Text ?? "none", "200K less than usual");
        Check("...within 3%: about your usual", ZoneCompare.Versus(1_200_000, 1_220_000, 1_800, Fk)?.Text ?? "none", "about your usual");
        Check("...no difference line with a stay under 10 minutes", (ZoneCompare.Versus(805_000, 1_270_000, 599, Fk) == null).ToString(), "True");
        Check("...one at exactly 10 minutes", (ZoneCompare.Versus(805_000, 1_270_000, 600, Fk) != null).ToString(), "True");
        Check("...none before there is an hour", (ZoneCompare.Versus(805_000, 0, 2_520, Fk) == null).ToString(), "True");
        bool matches = true;
        var rnd = new Random(93);
        for (int i = 0; i < 500; i++)
        {
            long usual = rnd.Next(50_000, 3_000_000), head = rnd.Next(50_000, 3_000_000);
            var vs = ZoneCompare.Versus(usual, head, 600 + rnd.Next(0, 7_200), Fk);
            if (vs is not { } x) { matches = false; break; }
            bool same = x.Diff == head - usual
                && (x.Sign == 0 ? x.Text == "about your usual" : x.Text == $"{Fk(Math.Abs(head - usual))} {(head > usual ? "more" : "less")} than usual")
                && (x.Sign == 0 || x.Sign == Math.Sign(head - usual));
            if (!same) { matches = false; Console.WriteLine($"    headline {head}, usual {usual}: \"{x.Text}\" ({x.Diff})"); break; }
        }
        Check("...500 random cases: the difference always the shown headline less the usual", matches.ToString(), "True");
        Check("...nothing with 9 minutes here today", (ZoneCompare.Usual("Western Cantas Valley", earlier, 400_000, 599) == null).ToString(), "True");
        Check("...shows at exactly 10 minutes today", (ZoneCompare.Usual("Western Cantas Valley", earlier, 400_000, 600) != null).ToString(), "True");
        Check("...nothing with 29 minutes on earlier days", (ZoneCompare.Usual("Western Cantas Valley", earlier.Take(2).ToList(), 400_000, 1_200) == null).ToString(), "True");
        Check("...shows with exactly 30 minutes on earlier days",
            (ZoneCompare.Usual("Western Cantas Valley", new List<FarmDay> { Day(2, ("Western Cantas Valley", 30, 0, 300_000, "", 0, 0)) }, 400_000, 1_200) != null).ToString(), "True");
        var u3 = ZoneCompare.Usual("Western Cantas Valley", new List<FarmDay> { Day(3, ("Western Cantas Valley", 8, 0, 100_000, "", 0, 0)), Day(2, ("Western Cantas Valley", 8, 0, 100_000, "", 0, 0)), Day(1, ("Western Cantas Valley", 8, 0, 100_000, "", 0, 0)), Day(4, ("Western Cantas Valley", 8, 0, 100_000, "", 0, 0)) }, 400_000, 1_200);
        Check("...no best day when no earlier day had ten minutes there", u3 == null ? "none" : (u3.Best == null).ToString(), "True");
        Check("...the zone not read never has a usual", (ZoneCompare.Usual(SessionEngine.UnknownZone, earlier, 400_000, 1_200) == null).ToString(), "True");

        // g. a run against its place: three earlier runs at least; its rank among all of them
        Check("run vs place: nothing with two earlier runs", (ZoneCompare.VsPlace(700_000, new long[] { 600_000, 650_000 }) == null).ToString(), "True");
        var v = ZoneCompare.VsPlace(712_000, new long[] { 600_000, 650_000, 1_120_000, 800_000, 500_000 });
        Check("...average, this, best, rank of all", v == null ? "none" : $"{Fk(v.Average)} {Fk(v.Diff)} {Fk(v.Best)} {ZoneCompare.Nth(v.Rank)} of {v.Of}", "734K -22000 1.12M 3rd of 6");
        var top = ZoneCompare.VsPlace(2_000_000, new long[] { 600_000, 650_000, 700_000 });
        Check("...the best yet ranks first", top == null ? "none" : $"{top.Rank} of {top.Of}", "1 of 4");
        Check("ordinals", string.Join(" ", new[] { 1, 2, 3, 4, 11, 12, 13, 21, 22, 103 }.Select(ZoneCompare.Nth)), "1st 2nd 3rd 4th 11th 12th 13th 21st 22nd 103rd");
        return ok;
    }
}
