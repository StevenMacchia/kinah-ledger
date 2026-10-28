namespace KinahLedger.Session;

/// <summary>
/// parse-test's checks for the page following the activity (0.8.55, Session/PageFollow.cs): the window opens on Farming,
/// follows a change that holds for 5 s, ignores a blip, keeps a page picked by hand until the activity moves on, and does
/// nothing with the setting off. A stand-in window ticks every half second, as the real one does.
/// </summary>
public static class PageFollowTests
{
    /// <summary>A stand-in for the window: the page it shows, and each switch with the second it happened.</summary>
    sealed class Sim
    {
        static readonly DateTime T0 = new(2026, 10, 28, 12, 0, 0, DateTimeKind.Utc);
        readonly PageFollow _f = new();
        readonly List<string> _log = new();
        public string Shown = PageFollow.Farming;   // the window opens on Farming, whatever it closed on
        public bool On = true;
        double _t;

        /// <summary>The activity for <paramref name="secs"/> seconds, a window tick every half second.</summary>
        public Sim For(double secs, ActivityKind kind)
        {
            for (double end = _t + secs; _t < end - 1e-9; _t += 0.5)
                if (_f.Step(kind, T0.AddSeconds(_t), On) is { } p && p != Shown) { Shown = p; _log.Add($"{_t:0.#}s {p}"); }
            return this;
        }

        /// <summary>The player clicks a page.</summary>
        public Sim Pick(string page) { Shown = page; _f.Picked(); _log.Add($"{_t:0.#}s picked {page}"); return this; }

        public string Log => _log.Count == 0 ? "none" : string.Join(", ", _log);
        public void Clear() => _log.Clear();
    }

    public static bool Run()
    {
        bool ok = true;
        void Check(string what, string got, string expect)
        {
            bool pass = got == expect; ok &= pass;
            Console.WriteLine($"  {what,-62} -> {(got.Length > 60 ? got[..60] + "…" : got),-40} {(pass ? "ok" : "MISMATCH, expected " + expect)}");
        }
        Console.WriteLine("\nThe page follows the activity: opens on Farming, holds 5 s, a pick by hand stays, off stops it (0.8.55):");

        // which activity belongs to which page; a base, the Market, a reward screen, idle and not read belong to none
        var kinds = Enum.GetValues<ActivityKind>();
        Check("the pages of the activities", string.Join(" ", kinds.Select(k => $"{k}:{PageFollow.PageOf(k) ?? "-"}")),
            "NotRead:- Idle:- Town:- Farming:Farming Event:Events Expedition:Events Arena:Events Legion:Events WorldBoss:Events Crafting:Crafting Market:- RewardScreen:-");

        // a. the window opens on Farming, and farming keeps it there
        var s = new Sim();
        Check("the window opens on Farming", s.Shown, "Farming");
        s.For(2, ActivityKind.NotRead).For(60, ActivityKind.Farming);
        Check("...and a minute of farming leaves it there", $"{s.Shown} | {s.Log}", "Farming | none");
        // opened in the middle of crafting: Crafting once the crafting window has held 5 s
        s = new Sim().For(2, ActivityKind.NotRead).For(10, ActivityKind.Crafting);
        Check("opened while crafting: Crafting after 5 s", $"{s.Shown} | {s.Log}", "Crafting | 7s Crafting");

        // b. it follows a change: farming, crafting, an expedition, its reward screen (no page), farming again
        s = new Sim().For(60, ActivityKind.Farming).For(10, ActivityKind.Crafting).For(20, ActivityKind.Expedition)
            .For(8, ActivityKind.RewardScreen).For(12, ActivityKind.Farming);
        Check("follows farming → crafting → expedition → farming", s.Log, "65s Crafting, 75s Events, 103s Farming");
        // an event, a world boss and the Market: the event and the boss are Events, the Market switches nothing
        s = new Sim().For(10, ActivityKind.Farming).For(10, ActivityKind.Event).For(10, ActivityKind.Farming)
            .For(10, ActivityKind.WorldBoss).For(10, ActivityKind.Town).For(20, ActivityKind.Market);
        Check("an event and a world boss show Events; a base and the Market nothing", $"{s.Shown} | {s.Log}", "Events | 15s Events, 25s Farming, 35s Events");

        // c. a blip never moves it: 3 s of crafting, two short stretches with something between, a dropped read
        s = new Sim().For(30, ActivityKind.Farming).For(3, ActivityKind.Crafting).For(10, ActivityKind.Farming)
            .For(3, ActivityKind.Crafting).For(0.5, ActivityKind.Town).For(3, ActivityKind.Crafting)
            .For(3, ActivityKind.Expedition).For(1, ActivityKind.NotRead).For(3, ActivityKind.Expedition).For(10, ActivityKind.Farming);
        Check("a blip of crafting or an expedition never switches", $"{s.Shown} | {s.Log}", "Farming | none");
        // crafting that ends 4 s after its first read isn't long enough; at 5 s it switches
        s = new Sim().For(10, ActivityKind.Farming).For(4.5, ActivityKind.Crafting).For(10, ActivityKind.Farming).For(5.5, ActivityKind.Crafting);
        Check("crafting gone after 4 s stays; at 5 s it switches", s.Log, "29.5s Crafting");

        // d. a page picked by hand stays until the activity moves on
        s = new Sim().For(10, ActivityKind.Farming).Pick(PageFollow.Events).For(60, ActivityKind.Farming);
        Check("Events picked while farming: a minute of farming keeps it", $"{s.Shown} | {s.Log}", "Events | 10s picked Events");
        s.For(10, ActivityKind.Crafting);
        Check("...until crafting starts", $"{s.Shown} | {s.Log}", "Crafting | 10s picked Events, 75s Crafting");
        // picked just after crafting began (before it held 5 s): the crafting under way is what was picked over
        s = new Sim().For(10, ActivityKind.Farming).For(3, ActivityKind.Crafting).Pick(PageFollow.Farming).For(30, ActivityKind.Crafting);
        Check("Farming picked 3 s into crafting: the crafting doesn't take it", $"{s.Shown} | {s.Log}", "Farming | 13s picked Farming");
        s.For(10, ActivityKind.Farming).For(10, ActivityKind.Crafting);
        Check("...farming, then crafting again, does", $"{s.Shown} | {s.Log}", "Crafting | 13s picked Farming, 58s Crafting");
        // picked in a base: starting to farm is the next change
        s = new Sim().For(10, ActivityKind.Town).Pick(PageFollow.Crafting).For(10, ActivityKind.Town).For(10, ActivityKind.Farming);
        Check("Crafting picked in a base: farming takes it back", s.Log, "10s picked Crafting, 25s Farming");
        // picked before anything was read: the first activity read was already under way, so it isn't a change
        s = new Sim().For(3, ActivityKind.NotRead).Pick(PageFollow.Events).For(30, ActivityKind.Farming);
        Check("Events picked before the first read: farming keeps it", $"{s.Shown} | {s.Log}", "Events | 3s picked Events");
        s.For(10, ActivityKind.Crafting);
        Check("...until crafting starts", s.Log, "3s picked Events, 38s Crafting");

        // e. the setting off: nothing switches, whatever happens
        s = new Sim { On = false };
        s.For(10, ActivityKind.Farming).For(10, ActivityKind.Crafting).For(10, ActivityKind.Expedition).For(10, ActivityKind.WorldBoss).For(10, ActivityKind.Farming);
        Check("off: farming, crafting, an expedition, a boss switch nothing", $"{s.Shown} | {s.Log}", "Farming | none");
        s.Pick(PageFollow.Crafting).For(10, ActivityKind.Expedition).For(10, ActivityKind.Farming);
        Check("off: a page picked by hand stays through all of it", $"{s.Shown} | {s.Log}", "Crafting | 50s picked Crafting");
        // turned back on: it kept up while off, so nothing old jumps in; the next change switches
        s = new Sim { On = false }.For(10, ActivityKind.Farming).For(20, ActivityKind.Crafting);
        s.On = true;
        s.For(20, ActivityKind.Crafting);
        Check("turned on mid-craft: no jump to Crafting", $"{s.Shown} | {s.Log}", "Farming | none");
        s.For(10, ActivityKind.Expedition);
        Check("...the next change switches", s.Log, "55s Events");
        return ok;
    }
}
