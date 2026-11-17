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

    static readonly Regex Token = new(@"[A-Za-z][A-Za-z'’\-]*[A-Za-z]|[A-Za-z]", RegexOptions.Compiled);

    /// <summary>The English words left in a text: three letters or more, not a game name, not a word the translations keep.</summary>
    public static IEnumerable<string> English(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) yield break;
        var allowed = Allowed();
        var names = GameNames();
        // a text that is a game name whole, or one with a known name in it ("Fire Temple · 12:04"), keeps the name
        var parts = text.Split(new[] { ' ', ' ', '\t', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
        var covered = new bool[parts.Length];
        static string Bare(string p) => p.Trim('(', ')', '[', ']', ',', '.', ':', ';', '!', '?', '“', '”', '"', '‘', '’', '\'', '·', '•', '—', '–', '-', '+', '×', '/', '…', '«', '»', '「', '」', '『', '』', '（', '）', '，', '。', '：');
        for (int i = 0; i < parts.Length; i++)
            for (int len = Math.Min(10, parts.Length - i); len >= 1; len--)
            {
                var span = string.Join(' ', parts, i, len);
                if (names.Contains(span) || names.Contains(Bare(span)) || names.Contains(span.TrimEnd(',', '.', ':', ';', '·', '!')))
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

    static HashSet<string>? _allowed;
    static string _allowedFor = "";

    /// <summary>
    /// Words that stay English in every language: the app's and the game's names, key names, units, words the glossary
    /// keeps, and every Latin word the current language's own table writes (a translation that keeps "Windows" allows it).
    /// </summary>
    static HashSet<string> Allowed()
    {
        if (_allowed != null && _allowedFor == L.Code) return _allowed;
        var a = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Kinah", "Ledger", "Aion", "NCSOFT", "NCSoft", "Windows", "Discord", "GitHub", "Reddit", "MetaBot", "Steam", "Purple",
            "DPS", "HPS", "CP", "PB", "AP", "RP", "GP", "XP", "EXP", "HP", "MP", "PvP", "PvE", "OCR", "UTC", "CSV", "PNG", "JSON", "Npcap",
            "Ctrl", "Alt", "Shift", "Tab", "Esc", "Enter", "Space", "Home", "End", "PgUp", "PgDn", "Del", "Ins", "Win", "Num", "Pause",
            "Lv", "min", "sec", "ms", "fps",
        };
        foreach (var (_, v) in L.CurrentTable())
            foreach (Match m in Token.Matches(v)) a.Add(m.Value);
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
        _names = n;
        return n;
    }

    /// <summary>Names the data files don't hold that stay English (the demo's characters, a few of the game's own words).</summary>
    public static readonly List<string> Extra = new()
    {
        "Shugo",
    };
}
