using KinahLedger.Data;
using KinahLedger.Packets;

namespace KinahLedger.Session;

/// <summary>
/// Crafts from the game's inventory updates while crafting is under way (Steven, 2026-11-13, the crafting captures): a
/// craft takes its materials when it starts, in one update, and its item lands when it ends. So:
/// <list type="bullet">
/// <item>an update in which the recipe's materials went down is one or more tries: how far the materials went down over
///   what the recipe takes (the game sometimes sends two crafts' materials at once), one when that can't be told;</item>
/// <item>an update in which one other item went up is a craft that worked, its rise what it made: twice the recipe's yield
///   for a dish, potion or material is its won combo, the recipe's upgrade going up is gear's;</item>
/// <item>a try with nothing made is a fail: told once a second try is open with the first unmade, or when the crafting ends.</item>
/// </list>
/// On the 17:50 capture this counted a queue of 7 Kukuru Fried Meat as 7 that worked, two of them combos (9 made), where
/// the panel's bars had counted 6, and the 16:42 Artisan's Kukuru Fried Meat queue as 2 worked and 1 failed, as the chat said.
/// Pure: the session feeds it updates it already knows came while crafting, and counts what it says.
/// </summary>
public sealed class PacketCrafts
{
    /// <summary>
    /// A craft's outcome: the recipe (the item it makes), whether it worked, whether this is the won combo of the craft
    /// before it, and what it made. Lost: a try with nothing made while the capture lost bytes, so its item may have gone
    /// with them: its outcome isn't known, and it counts as nothing (as a craft the panel never saw).
    /// </summary>
    public sealed record Outcome(DateTime At, string Recipe, bool Ok, bool Combo, int? Makes, string Why, bool Lost = false);

    /// <summary>Whether the traffic lost bytes between two times (a try whose item may have gone with them).</summary>
    public Func<DateTime, DateTime, bool>? GapBetween { get; set; }
    // when each try still open began, oldest first
    readonly List<DateTime> _tryAt = new();

    readonly Func<long, string> _name;
    readonly Func<long, bool> _listed;
    /// <param name="listed">Whether an item is on the item list: one that isn't (a soul crystal) is no craft's material or item.</param>
    public PacketCrafts(Func<long, string> nameOf, Func<long, bool>? listed = null) { _name = nameOf; _listed = listed ?? (_ => true); }

    string _recipe = "";
    int _tries, _ok, _failed;
    int? _lastMakes;
    DateTime _lastAt = DateTime.MinValue;

    /// <summary>True while a stretch of crafting has tries or crafts not closed by <see cref="End"/>.</summary>
    public bool Open => _tries > 0 || _ok > 0;
    /// <summary>When the last crafting update came.</summary>
    public DateTime LastAt => _lastAt;
    /// <summary>The recipe being crafted, as the traffic last named it.</summary>
    public string Recipe => _recipe;
    public (int Tries, int Ok, int Failed) Tally => (_tries, _ok, _failed);

    static readonly StringComparer Cmp = StringComparer.OrdinalIgnoreCase;
    static string Unbound(string n) => n.EndsWith(" (Bound)", StringComparison.OrdinalIgnoreCase) ? n[..^8] : n;

    /// <summary>
    /// One inventory update while crafting. <paramref name="panelRecipe"/>: what the crafting panel names ("" when unread),
    /// the recipe until the traffic names one.
    /// </summary>
    public List<Outcome> Take(ItemBatch b, string panelRecipe)
    {
        var o = new List<Outcome>();
        _lastAt = b.At;
        var net = b.Net().Where(n => _listed(n.ItemId)).ToList();
        if (net.Count == 0) return o;
        string cur = _recipe.Length >= 2 ? _recipe : panelRecipe;
        bool materials = net.Any(n => !n.Unknown && n.Net < 0) || net.Count > 1 || net.Any(n => IsMaterial(cur, _name(n.ItemId)));
        if (materials)
        {
            // another recipe's materials: the stretch before is over (its unmade tries were fails), and the panel's recipe starts
            // (every item of the update: the next queue's first one is often all stacks seen for the first time)
            var fell = net.Where(n => n.Unknown || n.Net < 0).Select(n => _name(n.ItemId)).ToList();
            if (cur.Length >= 2 && fell.Count > 0 && !fell.Any(f => IsMaterial(cur, f)))
            {
                o.AddRange(End(b.At));
                cur = panelRecipe.Length >= 2 && fell.Any(f => IsMaterial(panelRecipe, f)) ? panelRecipe : "";
                _recipe = cur;
            }
            else if (_recipe.Length < 2) _recipe = cur;
            int tries = TriesOf(net, cur);
            _tries += tries;
            for (int i = 0; i < tries; i++) _tryAt.Add(b.At);
            // one try may still be running; any more not made are fails
            for (int open = _tries - _ok - _failed; open >= 2; open--)
                o.Add(Fail(b.At, $"its materials went into another try with nothing made from this one: a craft that failed ({Said(b)})"));
            return o;
        }
        foreach (var (id, n, unknown) in net)
        {
            if (!unknown && n <= 0) continue;
            string name = _name(id);
            bool upgrade = cur.Length >= 2 && RecipeBook.ForOutput(cur)?.Upgrade is { } u0 && Cmp.Equals(Unbound(u0.Item), Unbound(name));
            // only what a recipe makes is a craft's item (a material seen for the first time, a gathered find, is not)
            if (!upgrade && RecipeBook.ForOutput(name) == null) continue;
            // the recipe's upgrade (gear's combo): the craft before worked, and its combo was won
            if (upgrade)
            {
                _ok++;
                if (_tryAt.Count > 0) _tryAt.RemoveAt(0);
                o.Add(new Outcome(b.At, cur, true, false, RecipeBook.ForOutput(cur)?.OutputQty, $"{name} went up: a craft of {cur} that worked..."));
                o.Add(new Outcome(b.At, cur, true, true, null, $"...and its combo, won: {name} in its place ({Said(b)})"));
                continue;
            }
            // another item than the recipe's: a new stretch (the queue before is over)
            if (cur.Length >= 2 && !Cmp.Equals(Unbound(cur), Unbound(name)) && Open) o.AddRange(End(b.At));
            _recipe = cur = name;
            _ok++;
            if (_tryAt.Count > 0) _tryAt.RemoveAt(0);
            var yields = RecipeBook.All.Where(r => Cmp.Equals(r.Output, name)).Select(r => r.OutputQty).Distinct().ToList();
            var rec = RecipeBook.ForOutput(name);
            if (unknown)
            {
                int? y = yields.Count == 1 ? yields[0] : _lastMakes;
                o.Add(new Outcome(b.At, name, true, false, y, $"{name} went up (its stack's earlier amount not known): a craft that worked{(y is int yy ? $", the recipe makes {yy}" : "")}"));
                continue;
            }
            if (n % 2 == 0 && !yields.Contains((int)n) && yields.Contains((int)(n / 2)) && rec?.DoublesOnCombo == true)
            {
                int y = (int)(n / 2);
                _lastMakes = y;
                o.Add(new Outcome(b.At, name, true, false, y, $"{name} +{n:N0}: a craft that worked..."));
                o.Add(new Outcome(b.At, name, true, true, null, $"...and its combo, won: twice the recipe's {y} ({Said(b)})"));
                continue;
            }
            _lastMakes = (int)Math.Min(n, 9999);
            o.Add(new Outcome(b.At, name, true, false, (int)Math.Min(n, 9999), $"{name} +{n:N0}: a craft that worked ({Said(b)})"));
        }
        return o;
    }

    /// <summary>The crafting is over: tries with nothing made are fails, and the next update starts afresh.</summary>
    public List<Outcome> End(DateTime at)
    {
        var o = new List<Outcome>();
        for (int i = _tries - _ok - _failed; i > 0; i--)
            o.Add(Fail(at, "a try whose materials went with nothing made by the end of the crafting: a craft that failed"));
        _tries = _ok = _failed = 0; _recipe = ""; _lastMakes = null; _tryAt.Clear();
        return o;
    }

    /// <summary>The oldest open try made nothing: a fail, or lost when the capture lost bytes while it ran.</summary>
    Outcome Fail(DateTime at, string why)
    {
        _failed++;
        var from = _tryAt.Count > 0 ? _tryAt[0] : at;
        if (_tryAt.Count > 0) _tryAt.RemoveAt(0);
        bool lost = GapBetween?.Invoke(from, at) == true;
        return lost
            ? new Outcome(at, _recipe, false, false, null, "a try with nothing made while the game's traffic lost bytes: its item may have gone with them, so its outcome isn't known", Lost: true)
            : new Outcome(at, _recipe, false, false, null, why);
    }

    /// <summary>Tries an update of materials stands for: the most any of the recipe's materials went down over what it takes, one when that can't be told.</summary>
    int TriesOf(IReadOnlyList<(long ItemId, long Net, bool Unknown)> net, string recipe)
    {
        var variants = recipe.Length >= 2 ? RecipeBook.All.Where(r => Cmp.Equals(r.Output, recipe)).ToList() : new();
        if (variants.Count == 0) return 1;
        // the variant this stretch makes (Strong Leather x1 or x10): the one whose yield the last craft made, else the smallest
        var v = variants.FirstOrDefault(r => r.OutputQty == _lastMakes) ?? variants.OrderBy(r => r.OutputQty).First();
        int best = 0;
        foreach (var (id, n, unknown) in net)
        {
            if (unknown || n >= 0) continue;
            var m = v.Materials.FirstOrDefault(x => Cmp.Equals(Unbound(x.Item), Unbound(_name(id))));
            if (m == null || m.Qty <= 0) continue;
            best = Math.Max(best, (int)Math.Round(-n / (double)m.Qty));
        }
        return Math.Clamp(best, 1, 50);
    }

    static bool IsMaterial(string recipe, string item) =>
        recipe.Length >= 2 && RecipeBook.All.Any(r => Cmp.Equals(r.Output, recipe) && r.Materials.Any(m => Cmp.Equals(Unbound(m.Item), Unbound(item))));

    string Said(ItemBatch b) => string.Join(", ", b.Net().Select(n => $"{_name(n.ItemId)} {(n.Unknown ? "?" : n.Net >= 0 ? $"+{n.Net:N0}" : $"−{-n.Net:N0}")}"));
}
