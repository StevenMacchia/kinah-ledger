using System.Text.Json;

namespace KinahLedger.Data;

public sealed record ItemInfo(long Id, string Name, string Grade, bool Tradable, string Icon, string Category);

/// <summary>
/// Item names by ID. Loaded from data/tw-items.json beside the exe (the crawler's output) when present, else from the
/// site's item list kept by <see cref="DictionaryCache"/>; empty when neither exists. Safe to use from the reader thread
/// while a fresh list is merged in.
/// </summary>
public sealed class ItemDictionary
{
    readonly Dictionary<long, ItemInfo> _byId = new();
    /// <summary>Weapons, armour and accessories (the list's "Equip_" groups).</summary>
    readonly HashSet<long> _gear = new();
    readonly Dictionary<string, long> _byName = new();
    /// <summary>Misreads remembered under the item they snapped to (IdOf): never a name of their own, so never a candidate.</summary>
    readonly HashSet<string> _snapped = new();
    /// <summary>Misreads the crowd settled (data/aliases.json): looked up like a snapped misread, never reported as one.</summary>
    readonly HashSet<string> _aliased = new();
    readonly object _lock = new();

    public int Count { get { lock (_lock) return _byId.Count; } }

    /// <summary>Where the game's item icons live; the crawler's records carry full URLs under it, the gathering list only file names.</summary>
    public const string IconBase = "https://assets.playnccdn.com/static-aion2-gamedata/resources/";

    /// <summary>The icon URL for an item, or "" when none is known (an unlisted item, or a synthetic ID for a misread name).</summary>
    public string IconUrl(long id)
    {
        lock (_lock)
        {
            if (!_byId.TryGetValue(id, out var i) || i.Icon.Length == 0) return "";
            return i.Icon.StartsWith("http", StringComparison.OrdinalIgnoreCase) ? i.Icon : IconBase + i.Icon;
        }
    }
    public ItemInfo? Get(long id) { lock (_lock) return _byId.GetValueOrDefault(id); }

    /// <summary>
    /// A synthetic id's name, as a kept run wrote it (InstanceRun.Names): known again after a restart even before the
    /// name is read off the screen again. A real id, or one the list already names, is left as it is.
    /// </summary>
    public void Remember(long id, string name)
    {
        if (id >= 0 || string.IsNullOrWhiteSpace(name)) return;
        lock (_lock)
        {
            if (!_byId.TryAdd(id, new ItemInfo(id, name.Trim(), "", !Norm(name).EndsWith("(bound)"), "", ""))) return;
            _byName.TryAdd(Norm(name), id);
        }
    }
    /// <summary>
    /// A weapon, armour or accessory. Gear from an event or expedition is bound to the character and can't be sold, so it
    /// carries no value in a run (Steven, 2026-10-22); only the materials can be sold. The list's own tradable flag can't
    /// say this: one name has a tradable and a bound copy (Ethereal Longsword).
    /// </summary>
    public bool IsGear(long id) { lock (_lock) return _gear.Contains(id); }

    /// <summary>
    /// The item a name is known by, as the list, the extras, the skins or a misread already snapped spell it, or null.
    /// Adds nothing (unlike <see cref="IdOf"/>): the gear repair of old records asks what a saved name was (2026-10-24).
    /// </summary>
    public long? Find(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return null;
        lock (_lock)
        {
            var key = Norm(name);
            if (_byName.TryGetValue(key, out var id)) return id;
            var plain = System.Text.RegularExpressions.Regex.Replace(key, @"\s*\(bound\)$", "");
            return plain != key && _byName.TryGetValue(plain, out var pid) ? pid : null;
        }
    }

    public string NameOf(long id) { lock (_lock) return _byId.TryGetValue(id, out var i) ? i.Name : $"item {id}"; }

    /// <summary>
    /// Listed items whose name holds the text, for a name box's completion (Options › In game › Rare drops): names that
    /// start with it first, then shorter ones, a bound copy after its tradeable twin; one per name, at most <paramref name="max"/>.
    /// </summary>
    public List<ItemInfo> Like(string text, int max)
    {
        text = text.Trim();
        if (text.Length == 0 || max <= 0) return new();
        lock (_lock)
            return _byId.Values.Where(i => i.Id > 0 && i.Name.Contains(text, StringComparison.OrdinalIgnoreCase))
                .GroupBy(i => i.Name, StringComparer.OrdinalIgnoreCase).Select(g => g.First())
                .OrderBy(i => i.Name.StartsWith(text, StringComparison.OrdinalIgnoreCase) ? 0 : 1)
                .ThenBy(i => i.Name.EndsWith("(Bound)", StringComparison.OrdinalIgnoreCase) ? 1 : 0)
                .ThenBy(i => i.Name.Length).ThenBy(i => i.Name, StringComparer.OrdinalIgnoreCase)
                .Take(max).ToList();
    }

    /// <summary>
    /// How an item is named in what's shared: its dictionary ID when it has one, else its name. A synthetic ID only means
    /// something on this PC, so it never leaves it; the site matches names against the same dictionary.
    /// </summary>
    public string KeyOf(long id) => id > 0 ? id.ToString() : NameOf(id);

    /// <summary>
    /// ID for a name read off the screen. Known names map to their real item ID; unknown ones get a
    /// stable negative ID so they still count, and the name is kept so it displays properly.
    /// </summary>
    public long IdOf(string name)
    {
        lock (_lock)
        {
            var key = Norm(name);
            if (_byName.TryGetValue(key, out var id))
            {
                // the same misread again: reported each time it is read, so the site sees how common it is
                // (once per line on screen, not per look-up: the window looks names up on every refresh)
                if (_snapped.Contains(key) && !_aliased.Contains(key) && Real(id)) KinahLedger.Session.Accuracy.Snapped(name, id, NameMatch.Full);
                return id;
            }

            // a misread of a known name ("Fresh Frult") snaps to the closest real item, so one drop doesn't become two rows
            var close = Closest(key);
            if (close != null)
            {
                _byName[key] = close.Value; _snapped.Add(key);
                if (Real(close.Value)) KinahLedger.Session.Accuracy.Snapped(name, close.Value, NameMatch.Full);
                return close.Value;
            }

            long synthetic = -(long)(Fnv(key) & 0x7FFFFFFF) - 1;
            _byId.TryAdd(synthetic, new ItemInfo(synthetic, name.Trim(), "", !key.EndsWith("(bound)"), "", ""));
            _byName[key] = synthetic;
            return synthetic;
        }
    }

    /// <summary>True when the name is a real item, exactly or by a close misread: a dictionary entry or a gathering material. Adds nothing.</summary>
    /// <summary>Every item the list holds (real ids), for indexes built over the whole list.</summary>
    public List<ItemInfo> AllItems() { lock (_lock) return _byId.Values.Where(i => i.Id > 0).ToList(); }

    /// <summary>
    /// Every name an item is known by, as <see cref="Norm"/> writes it, with the item it maps to and that item's own name:
    /// the list's items, the gathering materials, skins and extras, and the plain forms they are also known by. Misreads
    /// (snapped or crowd-settled aliases) and the synthetic ids of unknown reads are left out. The name decoder's lexicon
    /// (Ocr/NameDecoder.cs). Adds nothing.
    /// </summary>
    public List<(string Key, long Id, string Name)> Lexicon()
    {
        lock (_lock)
        {
            var l = new List<(string, long, string)>(_byName.Count);
            foreach (var (key, id) in _byName)
                if (!_snapped.Contains(key) && !_aliased.Contains(key) && Real(id) && _byId.TryGetValue(id, out var info)) l.Add((key, id, info.Name));
            return l;
        }
    }

    /// <summary>True when the name is a real item's own name (case and spacing aside), not a misread snapped to one.</summary>
    public bool KnowsExactly(string name)
    {
        lock (_lock)
        {
            var key = Norm(name);
            return _byName.TryGetValue(key, out var id) && id > 0 && _byId.TryGetValue(id, out var info) && Norm(info.Name) == key;
        }
    }

    public bool Knows(string name)
    {
        lock (_lock)
        {
            var key = Norm(name);
            if (_byName.TryGetValue(key, out var id)) return id > 0 || (_byId.TryGetValue(id, out var info) && info.Category.Length > 0);
            return Closest(key) is { } close && (close > 0 || (_byId.TryGetValue(close, out var ci) && ci.Category.Length > 0));
        }
    }

    /// <summary>
    /// The item a partly hidden or badly garbled name most likely is, when the plain match failed (Steven, 2026-10-21:
    /// "guessing based on closest match" for when something covers the chat). A name whose end or start is hidden
    /// ("Spiritst", "itstone") or whose middle is garbled is matched against the items that could be it; the ones in
    /// <paramref name="likely"/> (this run's items, the current enemy's loot table) are tried first. Only a single
    /// answer counts: two items that fit equally well give none. Needs five letters at least.
    /// </summary>
    public long? Guess(string read, IEnumerable<string>? likely = null) => GuessWhy(read, likely).Id;

    /// <summary>
    /// <see cref="Guess"/>, saying whether the answer came from the likely items (this run's, the enemies' loot tables) or
    /// only from being the one item near the name. <paramref name="allow"/> leaves out items it says no to (a grade the
    /// name's colour rules out), so two names that tie can still give one answer.
    /// </summary>
    public (long? Id, bool FromLikely) GuessWhy(string read, IEnumerable<string>? likely = null, Func<long, bool>? allow = null)
    {
        lock (_lock)
        {
            var key = Norm(read);
            if (key.Count(char.IsLetter) < 5 || key.Contains("(bound)")) return (null, false);
            int budget = Math.Max(2, key.Length / 5);
            var score = new Dictionary<long, int>();
            foreach (var kv in _byName)
            {
                var id = kv.Value;
                if (!(id > 0 || (_byId.TryGetValue(id, out var info) && info.Category.Length > 0))) continue;
                if (_snapped.Contains(kv.Key) || (allow != null && !allow(id))) continue;
                var n = kv.Key;
                int s = -1;
                if (n.Length > key.Length && (n.StartsWith(key, StringComparison.Ordinal) || n.EndsWith(key, StringComparison.Ordinal))) s = 1;
                else if (Math.Abs(n.Length - key.Length) <= budget) { int d = Edit(key, n, budget); if (d <= budget) s = 1 + d; }
                if (s > 0 && (!score.TryGetValue(id, out var old) || s < old)) score[id] = s;
            }
            if (score.Count == 0) return (null, false);
            long? Pick(IEnumerable<KeyValuePair<long, int>> c)
            {
                var l = c.OrderBy(x => x.Value).ToList();
                if (l.Count == 0) return null;
                return l.Count == 1 || l[1].Value > l[0].Value ? l[0].Key : null;
            }
            if (likely != null)
            {
                var ids = likely.Select(x => _byName.TryGetValue(Norm(x), out var i) ? i : 0).Where(i => i != 0).ToHashSet();
                if (Pick(score.Where(x => ids.Contains(x.Key))) is long hit) return (hit, true);
            }
            // without a hint, only an answer no other item comes near
            return (score.Count == 1 ? score.Keys.First() : null, false);
        }
    }

    /// <summary>One item a name read off the screen could be: how many letters apart it is, and its grade.</summary>
    public sealed record NameCandidate(long Id, string Name, string Grade, int Distance);

    /// <summary>
    /// The item a name read off the screen is, settled by what could drop or be made right now (brief item 1, 2026-10-24).
    /// An exact name (or a crowd-settled alias) is that item, whatever the context. Otherwise each of the context's sets is
    /// tried in order (the enemies' loot tables, the gathering materials, the recipe's outputs, the run's items) with the
    /// OCR-aware distance and a wider budget (a fifth of the letters, at least two edits), and one item alone closest in a
    /// set is the answer, unless the whole list holds an item a full edit closer. Then the whole list, with the old budget.
    /// When the context is closed and that item is on none of its sets and more than <see cref="NameMatch.TightCost"/>
    /// away, the read is refused: "unsure", never counted. Nothing is remembered (unlike <see cref="IdOf"/>): the same read
    /// can be another item under another target.
    /// </summary>
    public NameMatchResult Match(string read, NameContext? context = null)
    {
        lock (_lock)
        {
            var key = Norm(read);
            if (key.Length == 0) return new(null, NameMatch.None, 0);
            if (_byName.TryGetValue(key, out var id) && Real(id))
            {
                if (!_snapped.Contains(key)) return new(id, NameMatch.Exact, 0, id);
                if (_aliased.Contains(key)) return new(id, NameMatch.Alias, 0, id);
            }
            long? full = Closest(key, out int fullCost);
            if (full is long f0 && !Real(f0)) full = null;
            var ctxIds = new HashSet<long>();
            if (context != null && key.Length >= 4)
            {
                int max = Math.Max(2, key.Length / 5) * NameMatch.Unit;
                foreach (var (source, names) in context.Sets)
                {
                    long? best = null; int bestCost = int.MaxValue; bool tie = false;
                    foreach (var name in names)
                    {
                        var n = Norm(name);
                        if (!_byName.TryGetValue(n, out var nid) || !Real(nid) || _snapped.Contains(n)) continue;
                        ctxIds.Add(nid);
                        if (nid == best) continue;
                        int d = n == key ? 0 : NameMatch.Distance(key, n, Math.Min(max, bestCost));
                        if (d == int.MaxValue) continue;
                        if (d < bestCost) { best = nid; bestCost = d; tie = false; }
                        else if (d == bestCost) tie = true;
                    }
                    if (best is not long cid || tie) continue;
                    // the whole list knows a name a full edit closer than anything here: this set isn't what was read
                    if (full is long f && f != cid && fullCost + NameMatch.Unit <= bestCost) continue;
                    return new(cid, source, bestCost, full);
                }
            }
            if (full is not long fid) return new(null, NameMatch.None, 0);
            // a gathering material comes from nodes, never a loot table: it is never refused for lacking one (as ChatParser.Unlisted)
            if (context is { Closed: true } && fullCost > NameMatch.TightCost && !ctxIds.Contains(fid) && GatheringCatalog.Find(_byId[fid].Name) == null)
                return new(fid, NameMatch.Refused, fullCost, fid);
            return new(fid, NameMatch.Full, fullCost, fid);
        }
    }

    bool Real(long id) => id > 0 || (_byId.TryGetValue(id, out var info) && info.Category.Length > 0);

    /// <summary>
    /// True when the name is an item's own name as the list spells it (case, accents and stray marks aside), or one of the
    /// names it is also known by (a skin's chat form, a currency without "(Bound)"); false for a misread that snapped to it.
    /// </summary>
    public bool IsExact(string name)
    {
        lock (_lock)
        {
            var key = Norm(name);
            return _byName.TryGetValue(key, out var id) && !_snapped.Contains(key) && Real(id);
        }
    }

    /// <summary>
    /// Every item within <paramref name="budget"/> letters of a name read off the screen, closest first, each once. What
    /// <see cref="IdOf"/> snaps to is the first of these; when two are as close, the caller can choose between them on
    /// other grounds (the enemy's loot table, the name's colour) instead of taking whichever the list holds first.
    /// </summary>
    public List<NameCandidate> Near(string read, int budget)
    {
        lock (_lock)
        {
            var key = Norm(read);
            var best = new Dictionary<long, int>();
            if (key.Length < 4) return new();
            foreach (var kv in _byName)
            {
                if (Math.Abs(kv.Key.Length - key.Length) > budget || _snapped.Contains(kv.Key) || !Real(kv.Value)) continue;
                int d = Edit(key, kv.Key, budget);
                if (d <= budget && (!best.TryGetValue(kv.Value, out var o) || d < o)) best[kv.Value] = d;
            }
            return best.Select(kv => new NameCandidate(kv.Key, _byId[kv.Key].Name, _byId[kv.Key].Grade, kv.Value))
                .OrderBy(c => c.Distance).ThenBy(c => c.Name, StringComparer.Ordinal).ToList();
        }
    }

    /// <summary>
    /// The items a cut-off or garbled name could be, for the loot list beside the character, where a long name scrolls so
    /// that a read shows its middle or its end ("itstone x1"): every item whose name holds the read's letters, and every
    /// item a few letters from it. At most <paramref name="max"/> + 1 (more means the read says too little to choose).
    /// </summary>
    public List<NameCandidate> Fragment(string read, int max)
    {
        lock (_lock)
        {
            var key = Norm(read);
            if (key.Count(char.IsLetter) < 4) return new();
            var found = new Dictionary<long, int>();
            int budget = Math.Max(2, key.Length / 4);
            foreach (var kv in _byName)
            {
                if (_snapped.Contains(kv.Key) || !Real(kv.Value)) continue;
                int d = kv.Key.Contains(key, StringComparison.Ordinal) ? kv.Key.Length - key.Length
                      : Math.Abs(kv.Key.Length - key.Length) <= budget ? Edit(key, kv.Key, budget) : int.MaxValue;
                if (d == int.MaxValue || (!kv.Key.Contains(key, StringComparison.Ordinal) && d > budget)) continue;
                if (!found.TryGetValue(kv.Value, out var o) || d < o) found[kv.Value] = d;
                if (found.Count > max) break;
            }
            return found.Select(kv => new NameCandidate(kv.Key, _byId[kv.Key].Name, _byId[kv.Key].Grade, kv.Value))
                .OrderBy(c => c.Distance).ThenBy(c => c.Name, StringComparer.Ordinal).ToList();
        }
    }

    long? Closest(string key) => Closest(key, out _);

    /// <summary>
    /// The closest item to a misread on the whole list, by the OCR-aware distance (<see cref="NameMatch.Distance"/>) within
    /// the old budget (a sixth of the letters, at least one edit), and its weighted cost. A name starts with the same
    /// letter, or with one of the usual slips for it ("l" for "I").
    /// </summary>
    long? Closest(string key, out int cost)
    {
        cost = 0;
        if (key.Length < 4) return null;
        int budget = Math.Max(1, key.Length / 6);
        int max = budget * NameMatch.Unit;
        long? best = null; int bestD = max + 1;
        foreach (var kv in _byName)
        {
            if (Math.Abs(kv.Key.Length - key.Length) > budget || !NameMatch.CloseFirst(kv.Key[0], key[0])) continue;
            int d = NameMatch.Distance(key, kv.Key, bestD - 1);
            if (d < bestD) { bestD = d; best = kv.Value; if (d == 0) break; }
        }
        if (bestD <= max) { cost = bestD; return best; }
        cost = NameMatch.Unit;
        // the first letter of a line fading in is the one most often misread ("mria acquired." for "Aria acquired."):
        // a name that differs from exactly one known name in its first letter only is that name
        long? only = null;
        foreach (var kv in _byName)
        {
            if (kv.Key.Length != key.Length || kv.Key[0] == key[0] || string.CompareOrdinal(kv.Key, 1, key, 1, key.Length - 1) != 0) continue;
            if (only != null && only != kv.Value) return null;
            only = kv.Value;
        }
        return only;
    }

    /// <summary>Levenshtein distance with a cutoff, so the scan over thousands of names stays cheap.</summary>
    static int Edit(string a, string b, int max)
    {
        if (max < 0) return int.MaxValue;
        var prev = new int[b.Length + 1]; var cur = new int[b.Length + 1];
        for (int j = 0; j <= b.Length; j++) prev[j] = j;
        for (int i = 1; i <= a.Length; i++)
        {
            cur[0] = i; int rowMin = cur[0];
            for (int j = 1; j <= b.Length; j++)
            {
                int cost = a[i - 1] == b[j - 1] ? 0 : 1;
                cur[j] = Math.Min(Math.Min(cur[j - 1] + 1, prev[j] + 1), prev[j - 1] + cost);
                rowMin = Math.Min(rowMin, cur[j]);
            }
            if (rowMin > max) return int.MaxValue;
            (prev, cur) = (cur, prev);
        }
        return prev[b.Length];
    }

    /// <summary>
    /// A name as it is looked up: lower case, accents off and stray marks gone, so "Raw Léather", "Raw Leather~" and
    /// "Raw Leather" are the same item (Steven, 2026-10-21: a drop that looks like a normal drop is that drop).
    /// </summary>
    public static string Norm(string s)
    {
        var t = KinahLedger.Ocr.ChatParser.StripAccents(s).ToLowerInvariant().Replace('’', '\'');
        t = System.Text.RegularExpressions.Regex.Replace(t, @"[^a-z0-9'():+.,&\- ]", " ");
        return string.Join(' ', t.Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }
    static uint Fnv(string s) { uint h = 2166136261; foreach (var c in s) { h ^= c; h *= 16777619; } return h; }

    public static ItemDictionary LoadOrEmpty(string path)
    {
        var d = new ItemDictionary();
        var p = Settings.Resolve(path);
        if (File.Exists(p)) d.LoadFile(p);
        else if (File.Exists(DictionaryCache.FilePath)) { try { d.LoadFile(DictionaryCache.FilePath); } catch (Exception) { /* a torn download: the next refresh replaces it */ } }
        // the item list built into the exe (data/tw-items.json at build time) is the whitelist whenever no other list is there
        if (d._byId.Count(kv => kv.Key > 0) == 0) d.LoadEmbedded();
        // the gathering materials are always known, so misreads of them snap to the right name even without the full list
        foreach (var g in GatheringCatalog.All)
            if (!d._byName.ContainsKey(Norm(g.Name)))
            {
                long id = -(long)(Fnv("gather:" + Norm(g.Name)) & 0x7FFFFFFF) - 1;
                d._byId[id] = new ItemInfo(id, g.Name, g.Grade, true, "", "Gathering Material");
                d._byName[Norm(g.Name)] = id;
            }
        d.LoadGatheringIcons(p);
        d.LoadSkins();
        d.LoadExtras();
        d.LoadAliases();
        return d;
    }

    /// <summary>How many crowd-learned misread fixes are loaded (data/aliases.json).</summary>
    public int AliasCount { get { lock (_lock) return _aliased.Count; } }

    /// <summary>
    /// Crowd-learned misread fixes (2026-10-23): reads that several trackers snapped or guessed to the same item, from
    /// data/aliases.json (the daily game-data update carries it; the site builds it, see /v1/aliases.json). Each is looked
    /// up like a misread that snapped: it counts as its item, is never a candidate of its own, and a real item's name
    /// always wins over it. The site never promotes a Legend, Unique or Epic item this way.
    /// </summary>
    internal void LoadAliases(Stream? from = null)
    {
        try
        {
            using var s = from ?? DataFiles.Open("aliases.json");
            if (s == null) return;
            using var doc = JsonDocument.Parse(s);
            if (!doc.RootElement.TryGetProperty("aliases", out var a) || a.ValueKind != JsonValueKind.Object) return;
            lock (_lock)
                foreach (var p in a.EnumerateObject())
                {
                    if (p.Value.ValueKind != JsonValueKind.Number || !p.Value.TryGetInt64(out var id) || id <= 0 || !_byId.ContainsKey(id)) continue;
                    var key = Norm(p.Name);
                    if (key.Length < 3 || _byName.ContainsKey(key)) continue;
                    _byName[key] = id; _snapped.Add(key); _aliased.Add(key);
                    // a one-letter misread teaches the confusion table that pair (NameMatch.Learn)
                    if (_byId.TryGetValue(id, out var ai)) NameMatch.Learn(key, Norm(ai.Name));
                }
        }
        catch (Exception ex) { CrashLog.Note("aliases: " + ex.Message); }
    }

    /// <summary>
    /// Cosmetic skins (data/skins.json, from MetaBot.GG, built into the exe): most are missing from the item list, so a
    /// skin drop never counted. Each is known as "Set (Skin: Slot)", the list's own form; the chat's "Set (Slot) Skin" is
    /// turned into it by the parser. A skin with no item id gets a stable one of its own and goes to the site by name.
    /// </summary>
    /// <summary>
    /// Currencies and reward items the item list lacks (data/extra-items.json, from MetaBot.GG): Kina, Abyss Points, Soul
    /// Codex, Silentium, Odyle Energy, shop coins and the like. They show in expedition and event reward grids, so the
    /// grid's icons can name them. Each is known under its own name and, for a "(Bound)" one, without that suffix too,
    /// since the chat parser takes "(Bound)" off before it looks a name up. No ids: they go to the site by name.
    /// </summary>
    void LoadExtras()
    {
        try
        {
            using var stream = typeof(ItemDictionary).Assembly.GetManifestResourceStream("extra-items.json");
            if (stream == null) return;
            using var doc = JsonDocument.Parse(stream);
            if (!doc.RootElement.TryGetProperty("items", out var arr) || arr.ValueKind != JsonValueKind.Array) return;
            foreach (var e in arr.EnumerateArray())
            {
                var name = Str(e, "name");
                if (name.Length == 0 || _byName.ContainsKey(Norm(name))) continue;
                long id = -(long)(Fnv("extra:" + Norm(name)) & 0x7FFFFFFF) - 1;
                bool tradable = !(e.TryGetProperty("tradable", out var t) && t.ValueKind == JsonValueKind.False);
                var cat = Str(e, "category"); if (cat.Length == 0) cat = "Currency";
                _byId[id] = new ItemInfo(id, name, Str(e, "grade"), tradable, Str(e, "icon"), cat);
                _byName[Norm(name)] = id;
                var plain = System.Text.RegularExpressions.Regex.Replace(name, @"\s*\(bound\)\s*$", "", System.Text.RegularExpressions.RegexOptions.IgnoreCase).Trim();
                if (plain != name && !_byName.ContainsKey(Norm(plain))) _byName[Norm(plain)] = id;
            }
        }
        catch (Exception) { /* without the extras, those slots stay unnamed */ }
    }

    /// <summary>Every item the list holds, the extras included (they have no real ids), for the reward grid's icon names.</summary>
    public List<ItemInfo> AllWithIcons() { lock (_lock) return _byId.Values.Where(i => i.Icon.Length > 0).ToList(); }

    void LoadSkins()
    {
        try
        {
            using var stream = KinahLedger.Data.DataFiles.Open("skins.json");
            if (stream == null) return;
            using var doc = JsonDocument.Parse(stream);
            if (!doc.RootElement.TryGetProperty("items", out var arr) || arr.ValueKind != JsonValueKind.Array) return;
            foreach (var e in arr.EnumerateArray())
            {
                // MetaBot's own name carries "(Bound)" ("Dawnfrost (Skin: Cloak) (Bound)"); bound and tradable copies are one item
                string set = Str(e, "set"), slot = Str(e, "slot"), raw = System.Text.RegularExpressions.Regex.Replace(Str(e, "name"), @"\s*\(bound\)\s*$", "", System.Text.RegularExpressions.RegexOptions.IgnoreCase).Trim();
                var name = set.Length > 0 && slot.Length > 0 ? $"{set} (Skin: {slot})" : raw;
                if (name.Length == 0 || _byName.ContainsKey(Norm(name))) continue;
                long id = e.TryGetProperty("id", out var idEl) && idEl.ValueKind == JsonValueKind.Number && idEl.TryGetInt64(out var v) && v > 0 && !_byId.ContainsKey(v)
                    ? v : -(long)(Fnv("skin:" + Norm(name)) & 0x7FFFFFFF) - 1;
                bool tradable = !(e.TryGetProperty("tradable", out var t) && t.ValueKind == JsonValueKind.False);
                _byId[id] = new ItemInfo(id, name, Str(e, "grade"), tradable, "", "Skin");
                _byName[Norm(name)] = id;
                if (raw.Length > 0 && !_byName.ContainsKey(Norm(raw))) _byName[Norm(raw)] = id;
            }
        }
        catch (Exception) { /* without the skins file, skins count only when the item list has them */ }
    }

    /// <summary>Icon file names for the gathering materials: data/gathering-items.json beside the item list when present, else the copy built into the exe.</summary>
    void LoadGatheringIcons(string itemsPath)
    {
        try
        {
            var beside = Path.Combine(Path.GetDirectoryName(itemsPath) ?? "", "gathering-items.json");
            using var stream = File.Exists(beside) ? File.OpenRead(beside) : KinahLedger.Data.DataFiles.Open("gathering-items.json");
            if (stream == null) return;
            using var doc = JsonDocument.Parse(stream);
            if (!doc.RootElement.TryGetProperty("items", out var arr)) return;
            foreach (var e in arr.EnumerateArray())
            {
                var name = Str(e, "name"); var icon = Str(e, "icon");
                if (name.Length == 0 || icon.Length == 0 || !_byName.TryGetValue(Norm(name), out var id)) continue;
                if (_byId.TryGetValue(id, out var info) && info.Icon.Length == 0) _byId[id] = info with { Icon = icon };
            }
        }
        catch (Exception) { /* icons are decoration: a bad file costs nothing */ }
    }

    /// <summary>Adds (or refreshes) the items in a list file while the tracker runs. Names read before the list arrived now resolve to their real item.</summary>
    public int Merge(string path) { lock (_lock) return LoadFile(path); }

    int LoadFile(string p) { using var doc = JsonDocument.Parse(File.ReadAllText(p)); return LoadJson(doc); }

    /// <summary>The item list built into the exe (data/tw-items.json at build time): the whitelist when no list sits beside the exe.</summary>
    void LoadEmbedded()
    {
        try
        {
            using var stream = typeof(ItemDictionary).Assembly.GetManifestResourceStream("tw-items.json");
            if (stream == null) return;
            using var doc = JsonDocument.Parse(stream);
            LoadJson(doc);
        }
        catch (Exception) { /* without a list nothing counts as a drop; the window's status says how many items are known */ }
    }

    /// <summary>Reads either shape: the crawler's (name, grade, tradable, icon, categoryName) or the site's compact list (n, g, t, i, c2).</summary>
    int LoadJson(JsonDocument doc)
    {
        int n = 0;
        if (!doc.RootElement.TryGetProperty("items", out var arr) || arr.ValueKind != JsonValueKind.Array) return 0;
        foreach (var e in arr.EnumerateArray())
        {
            if (!e.TryGetProperty("id", out var idEl) || !idEl.TryGetInt64(out var id) || id <= 0) continue;
            var name = Str(e, "name"); if (name.Length == 0) name = Str(e, "n");
            if (name.Length == 0) continue;
            var grade = Str(e, "grade"); if (grade.Length == 0) grade = Str(e, "g");
            bool tradable = e.TryGetProperty("tradable", out var t) ? t.ValueKind == JsonValueKind.True
                          : e.TryGetProperty("t", out var t2) && t2.ValueKind == JsonValueKind.Number && t2.GetInt32() != 0;
            var icon = Str(e, "icon"); if (icon.Length == 0) icon = Str(e, "i");
            var cat = Str(e, "categoryName"); if (cat.Length == 0) cat = System.Text.RegularExpressions.Regex.Replace(Str(e, "c2"), "(?<=[a-z])(?=[A-Z])", " ");   // the site's list keeps the code ("CraftResource"): spaced for the rows
            _byId[id] = new ItemInfo(id, name.Trim(), grade, tradable, icon, cat);
            // the site's list calls the group c1, the crawler's (the copy built into the exe) cat1: either marks gear
            if ((Str(e, "c1") + Str(e, "cat1")).StartsWith("Equip_", StringComparison.OrdinalIgnoreCase)) _gear.Add(id);
            n++;
            // first real ID wins per name, so a later duplicate doesn't flip the mapping; a synthetic one gives way
            var key = Norm(name);
            if (!_byName.TryGetValue(key, out var cur) || cur < 0) _byName[key] = id;
        }
        return n;
    }

    static string Str(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString()! : "";
}
