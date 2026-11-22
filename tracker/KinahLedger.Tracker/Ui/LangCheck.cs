using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;

namespace KinahLedger.Ui;

/// <summary>
/// Finds English left on screen in a Korean or Chinese window (1.0, 2026-11-17: the whole tracker in both languages). A
/// render in a language walks what it draws (every text, a button's words, a tooltip) and lists each text that still has an
/// English word in it, and <see cref="L.T"/> lists each English text it was asked for and has no translation of. Allowed
/// to stay English: game names from the data files (items, skills, bosses, zones, mobs, servers), words the translations
/// themselves keep (Windows, Discord, DPS), key names and units. options-test fails on anything listed (WpfApp.Lang.cs);
/// <c>KL_LANG=ko KL_LANG_AUDIT=out.txt ui-shot …</c> writes the list of a render, so a page can be checked while it is
/// worked on.
/// </summary>
public static class LangCheck
{
    /// <summary>On while a test or an audit render runs in a language: each text found, with the first picture it was in.</summary>
    public static SortedDictionary<string, string>? Found { get; private set; }
    /// <summary>The file an audit render appends to (KL_LANG_AUDIT).</summary>
    static string? _file;

    /// <summary>Starts listing (the language must be set first with <see cref="L.Init"/>).</summary>
    public static void Begin(string? file = null)
    {
        Found = new(StringComparer.Ordinal);
        L.Misses = new(StringComparer.Ordinal);
        _file = file;
        _allowed = null;
    }

    /// <summary>Stops listing; returns what was found on screen and what <see cref="L.T"/> had no translation of.</summary>
    public static (List<string> OnScreen, List<string> NoEntry) End()
    {
        var found = Found?.Select(kv => $"{kv.Value} | {kv.Key}").ToList() ?? new();
        var misses = (L.Misses ?? new()).Where(m => English(m).Any()).OrderBy(m => m, StringComparer.Ordinal).ToList();
        Found = null; L.Misses = null; _file = null;
        return (found, misses);
    }

    /// <summary>KL_LANG_AUDIT=file in a render in a language: starts listing into that file.</summary>
    public static void FromEnvironment()
    {
        var f = Environment.GetEnvironmentVariable("KL_LANG_AUDIT");
        if (string.IsNullOrWhiteSpace(f) || L.Code == "en") return;
        Begin(f);
        AppDomain.CurrentDomain.ProcessExit += (_, _) => Flush();
    }

    /// <summary>Writes the audit file (what is on screen, then what had no entry).</summary>
    public static void Flush()
    {
        if (_file == null || Found == null) return;
        try
        {
            var lines = new List<string> { $"# {L.Code}: {Found.Count} texts with English on screen" };
            lines.AddRange(Found.Select(kv => $"{kv.Value} | {kv.Key}"));
            var misses = (L.Misses ?? new()).Where(m => English(m).Any()).OrderBy(m => m, StringComparer.Ordinal).ToList();
            lines.Add($"# {misses.Count} texts asked of L.T with no {L.Code} entry");
            lines.AddRange(misses.Select(m => "no entry | " + m));
            File.WriteAllLines(_file, lines);
        }
        catch (IOException) { }
    }

    /// <summary>Lists the English left in what <paramref name="root"/> draws (a render calls this as it saves a picture).</summary>
    public static void Scan(DependencyObject root, string where)
    {
        if (Found == null || L.Code == "en") return;
        var seen = new HashSet<DependencyObject>();
        void Text(string? s)
        {
            if (string.IsNullOrWhiteSpace(s)) return;
            var words = English(s).ToList();
            if (words.Count > 0) lock (Found) Found.TryAdd(s.Replace('\n', ' ').Replace('\r', ' ').Trim(), where);
        }
        void Walk(DependencyObject d)
        {
            if (!seen.Add(d)) return;
            if (d is UIElement u && u.Visibility != Visibility.Visible) return;
            switch (d)
            {
                case TextBox: return;   // typed by the player
                case PasswordBox: return;
                case TextBlock tb: Text(Plain(tb)); break;
                case HeaderedContentControl hc when hc.Header is string h: Text(h); break;
            }
            if (d is FrameworkElement fe)
                switch (fe.ToolTip)
                {
                    case string tip: Text(tip); break;
                    case ToolTip { Content: string t2 }: Text(t2); break;
                    case DependencyObject tipEl: Walk(tipEl); break;
                }
            if (d is Visual or System.Windows.Media.Media3D.Visual3D)
                for (int i = 0, n = VisualTreeHelper.GetChildrenCount(d); i < n; i++) Walk(VisualTreeHelper.GetChild(d, i));
            foreach (var ch in LogicalTreeHelper.GetChildren(d).OfType<DependencyObject>()) Walk(ch);
        }
        Walk(root);
        if (_file != null) Flush();
    }

    /// <summary>A text block's words as drawn (its runs joined).</summary>
    static string Plain(TextBlock tb)
    {
        if (tb.Inlines.Count == 0) return tb.Text;
        var sb = new System.Text.StringBuilder();
        void Add(InlineCollection ins)
        {
            foreach (var i in ins)
                switch (i)
                {
                    case Run r: sb.Append(r.Text); break;
                    case Span s: Add(s.Inlines); break;
                    case LineBreak: sb.Append(' '); break;
                }
        }
        Add(tb.Inlines);
        return sb.ToString();
    }

    static readonly Regex Cjk = new(@"(?<=[A-Za-z0-9'’)\]])(?=\([가-힯])|(?<=[A-Za-z0-9'’)\]])(?=[ᄀ-ᇿ　-鿿가-힯豈-﫿＀-￯])|(?<=[ᄀ-ᇿ　-鿿가-힯豈-﫿＀-￯])(?=[A-Za-z(])", RegexOptions.Compiled);
    static readonly Regex Token =new(@"[A-Za-z][A-Za-z'’\-]*[A-Za-z]|[A-Za-z]", RegexOptions.Compiled);

    /// <summary>The English words left in a text: three letters or more, not a game name, not a word the translations keep.</summary>
    public static IEnumerable<string> English(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) yield break;
        var allowed = Allowed();
        var names = GameNames();
        // a text that is a game name whole, or one with a known name in it ("Fire Temple · 12:04"), keeps the name
        var parts = text.Split(new[] { ' ', ' ', '\t', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
        // a Korean particle or a Chinese word stuck to a name ("Western Cantas Valley에서", "在Siel") is not part of it: split it off
        parts = parts.SelectMany(p => Cjk.Split(p)).Where(p => p.Length > 0).ToArray();
        var covered = new bool[parts.Length];
        static string Bare(string p) => p.Trim('(', ')', '[', ']', ',', '.', ':', ';', '!', '?', '“', '”', '"', '‘', '’', '\'', '·', '•', '—', '–', '-', '+', '×', '/', '…', '«', '»', '「', '」', '『', '』', '（', '）', '，', '。', '：');
        for (int i = 0; i < parts.Length; i++)
            for (int len = Math.Min(24, parts.Length - i); len >= 1; len--)   // a specialty's text runs to 15 words
            {
                var span = string.Join(' ', parts, i, len);
                if (names.Contains(span) || names.Contains(Bare(span)) || names.Contains(span.TrimEnd(',', '.', ':', ';', '·', '!')) || _kept.Contains(span))
                {
                    for (int k = i; k < i + len; k++) covered[k] = true;
                    break;
                }
            }
        for (int i = 0; i < parts.Length; i++)
        {
            if (covered[i]) continue;
            var p = parts[i];
            if (p.Any(char.IsDigit)) continue;   // 12.5M, 5m, x2, Lv45, 1:04
            if (p.Contains("://") || p.Contains(".com") || p.Contains(".gg") || p.Contains('@') || p.Contains('\\') || p.EndsWith(".json") || p.EndsWith(".txt") || p.EndsWith(".log") || p.EndsWith(".exe")) continue;   // links and files
            foreach (Match m in Token.Matches(p))
            {
                var w = m.Value;
                if (w.Length < 3) continue;
                if (allowed.Contains(w)) continue;
                yield return w;
            }
        }
    }

    /// <summary>
    /// Table entries that stay in English on purpose: the Terminal look's commands (shell syntax) and the site's name. Every
    /// other translation is written in its language's own script (parser-test), unless it only keeps the key's own fixed
    /// words or punctuation (<see cref="KeptOnPurpose"/>).
    /// </summary>
    public static readonly HashSet<string> KeptEnglish = new(StringComparer.Ordinal)
    {
        "> record --new", "> abyss --rate", "> kinah --rate", "> fight ", "> fight start", "perf report --sort sym",
        "KINAHLEDGER.COM · {0}", "NPC {0:N0}",
    };

    /// <summary>
    /// A translation with none of its language's letters that is still right: a kept text above, or one that adds no
    /// English of its own (" this week." → "." where the sentence's verb moved; "Ctrl+1 to 5" → "Ctrl+1~5").
    /// </summary>
    public static bool KeptOnPurpose(string key, string value)
    {
        if (KeptEnglish.Contains(key) && value == key) return true;
        var keyWords = Token.Matches(key).Select(m => m.Value).ToHashSet(StringComparer.Ordinal);
        return Token.Matches(value).All(m => keyWords.Contains(m.Value) && Fixed.Contains(m.Value));
    }

    /// <summary>Words that stay English in every language whatever the tables say: names, key names, units.</summary>
    static readonly HashSet<string> Fixed = new(StringComparer.OrdinalIgnoreCase)
    {
        "Kinah", "Ledger", "Aion", "NCSOFT", "NCSoft", "Windows", "Discord", "GitHub", "Reddit", "MetaBot", "Steam", "Purple",
        "DPS", "HPS", "CP", "PB", "AP", "RP", "GP", "XP", "EXP", "HP", "MP", "PvP", "PvE", "OCR", "UTC", "CSV", "PNG", "JSON", "Npcap",
        "Ctrl", "Alt", "Shift", "Tab", "Esc", "Enter", "Space", "Home", "End", "PgUp", "PgDn", "Del", "Ins", "Win", "Num", "Pause",
        "Lv", "min", "sec", "ms", "fps",
        "Chrome", "Edge", "Firefox", "Ko-fi",
        // a chat line quoted as the game wrote it ("Last: Diamond Powder acquired")
        "acquired", "Acquired",
    };

    static HashSet<string>? _allowed;
    /// <summary>Texts the current language's table keeps in English on purpose (a terminal look's commands): allowed whole.</summary>
    static HashSet<string> _kept = new(StringComparer.Ordinal);
    static string _allowedFor = "";

    /// <summary>
    /// Words that stay English in every language: the app's and the game's names, key names, units, words the glossary
    /// keeps, and every Latin word the current language's own table writes (a translation that keeps "Windows" allows it).
    /// </summary>
    static HashSet<string> Allowed()
    {
        if (_allowed != null && _allowedFor == L.Code) return _allowed;
        var a = new HashSet<string>(Fixed, StringComparer.OrdinalIgnoreCase);
        // (a text kept as it is, "> kinah --rate", allows itself whole, in Kept, never its words one by one)
        foreach (var (k, v) in L.CurrentTable())
            if (v != k && !KeptOnPurpose(k, v)) foreach (Match m in Token.Matches(v)) a.Add(m.Value);
        _kept = new HashSet<string>(L.CurrentTable().Where(kv => kv.Value == kv.Key).Select(kv => kv.Key.Trim()), StringComparer.Ordinal);
        _allowed = a; _allowedFor = L.Code;
        return a;
    }

    static HashSet<string>? _names;

    /// <summary>The game's names as the data files write them (they stay English: the game client on the global servers writes them so).</summary>
    public static HashSet<string> GameNames()
    {
        if (_names != null) return _names;
        var n = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var asm = Assembly.GetExecutingAssembly();
        string? Read(string name) { try { using var s = asm.GetManifestResourceStream(name); if (s == null) return null; using var r = new StreamReader(s); return r.ReadToEnd(); } catch (Exception) { return null; } }
        void Add(string? s) { if (!string.IsNullOrWhiteSpace(s) && s.Length <= 80) { n.Add(s.Trim()); var plain = Regex.Replace(s, @"\s*\(.*?\)\s*$", "").Trim(); if (plain.Length > 0) n.Add(plain); } }
        foreach (var (file, fields) in new[] { ("zones.txt", new[] { 0 }), ("instances.txt", new[] { 0, 2 }), ("bosses.txt", new[] { 0, 2 }), ("mobs.txt", new[] { 0 }), ("abyss.txt", new[] { 0 }) })
        {
            var text = Read(file);
            if (text == null) continue;
            foreach (var line in text.Split('\n'))
            {
                var l = line.Trim();
                if (l.Length == 0 || l.StartsWith('#')) continue;
                var f = l.Split('|');
                foreach (var i in fields)
                    if (i < f.Length)
                    {
                        Add(f[i]);
                        // an Abyss area as the timers write it: its part after " - " ("Siel's Heart"), Reshanta without "Chaotic"
                        foreach (var piece in f[i].Split(" - ")) { Add(piece); if (piece.StartsWith("Chaotic ")) Add(piece["Chaotic ".Length..]); }
                        // a boss as the Buddy's run splits shorten it ("Watchdog Kwapo" → "Kwapo")
                        if (file == "bosses.txt" && i == 2) Add(KinahLedger.Session.RunSplits.Short(f[i]));
                    }
            }
        }
        var keys = new HashSet<string>(StringComparer.Ordinal) { "name", "item", "skill", "output", "set", "cat1Name", "cat2Name", "categoryName", "text" };
        foreach (var file in new[] { "tw-items.json", "skills.json", "specialties.json", "gathering-items.json", "recipes.json", "extra-items.json", "skins.json", "loot-tables.json" })
        {
            var text = Read(file);
            if (text == null) continue;
            try
            {
                using var doc = JsonDocument.Parse(text);
                void Walk(JsonElement e, string? key)
                {
                    switch (e.ValueKind)
                    {
                        case JsonValueKind.Object: foreach (var p in e.EnumerateObject()) Walk(p.Value, p.Name); break;
                        case JsonValueKind.Array: foreach (var x in e.EnumerateArray()) Walk(x, key); break;
                        case JsonValueKind.String: if (key != null && keys.Contains(key)) Add(e.GetString()); break;
                    }
                }
                Walk(doc.RootElement, null);
            }
            catch (JsonException) { }
        }
        var mobImages = Read("mob-images.json");
        if (mobImages != null) try { using var doc = JsonDocument.Parse(mobImages); foreach (var p in doc.RootElement.EnumerateObject()) Add(p.Name); } catch (JsonException) { }
        foreach (var sv in KinahLedger.Data.Servers.All) { Add(sv.Label); Add(sv.Name); } foreach (var r in KinahLedger.Data.Servers.Regions) Add(r.Name);
        foreach (var extra in Extra) Add(extra);
        // the timers' events and Abyss bosses, and the places they name
        foreach (var e in KinahLedger.Session.EventTimers.Schedule) { Add(e.Name); Add(e.Where); }
        // (the timers shorten a boss to its own name, "Executor Argo" to "Argo": that is a name too)
        foreach (var b in KinahLedger.Session.AbyssBosses.Schedule) { Add(b.Name); Add(b.Name.Split(' ')[^1]); Add(b.Place); Add(b.Map); }
        _names = n;
        return n;
    }

    /// <summary>Names the data files don't hold that stay English (the demo's characters, a few of the game's own words).</summary>
    public static readonly List<string> Extra = new()
    {
        "Shugo",
        // the game's modes and instance kinds, written as the game writes them
        "Conquest", "Expedition", "Transcendence", "Sanctuary", "Conquest · Expedition", "Easy",
        // the demo's characters, fights and buffs (sample data in renders and tests)
        "Aerinwyn", "Kaelith", "Thornecrest", "Thornwick", "Lyrandel", "Lysandra", "Brannoc", "Ithrandel", "Selunaire", "Vexmora",
        "Ground Slam", "Tail Sweep", "Horn Charge", "Searing Breath", "Void Fragment", "Invasion",
        "Group buff A", "Group buff B", "Self buff", "Short burst buff",
        "Valkyriestrider", "The Forgotten Sanctuary of the Silvermoon",   // the long-names state
        // the demo's world bosses as the timers shorten them ("Chaser Taulo" → "Taulo")
        "Taulo", "Aulla", "Demiros", "Neikel", "Kashapa",
        // the Applicants sample (Ui/ApplicantsFace.cs Sample): real names from a listing, and their servers
        "Varrenthos", "Kyralis", "Embervale", "Thornecrestwarde", "Aerinwynne", "Seraphimdawnlite", "Quillon", "Brightmoor", "Halvric", "Selwyra", "Dornak",
        "Israphel", "Zikel", "Lumiel", "Meslamtaeda", "Siel", "Fregion", "Ereshkigal", "Nezekan", "Triniel",
    };
}
