using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text.Json;

namespace KinahLedger.Ui;

/// <summary>
/// The app's words in the player's language (2026-11-16, Steven: a Korean and a Traditional Chinese UI, the meter's path
/// first). A word is looked up by its English text, so anything not translated yet stays English and nothing breaks:
/// <c>L.T("Your DPS")</c>, and a sentence with figures in it <c>L.F("Killed in {0}", time)</c> (the translation keeps the
/// same {0}, {1} places). The tables are Lang/&lt;code&gt;/*.json, built into the exe (one file per area, so they can be
/// filled side by side), each an object of English to translated text. Game names (skills, bosses, items, classes) stay
/// English: they come from the data files, never through here.
/// </summary>
public static class L
{
    /// <summary>The languages there are, and what the picker calls each (in its own language).</summary>
    public static readonly (string Code, string Name)[] Choices = { ("auto", "Windows' language"), ("en", "English"), ("ko", "한국어"), ("zh-TW", "繁體中文") };

    /// <summary>The language in use: "en", "ko" or "zh-TW".</summary>
    public static string Code { get; private set; } = "en";

    static Dictionary<string, string> _t = new(StringComparer.Ordinal);

    /// <summary>The English text in the language in use (itself when it has no translation).</summary>
    public static string T(string en) => en.Length > 0 && _t.TryGetValue(en, out var s) ? s : en;

    /// <summary>A sentence with figures: the English pattern's translation filled in ({0}, {1} … as in string.Format).</summary>
    public static string F(string en, params object?[] args)
    {
        try { return string.Format(CultureInfo.CurrentCulture, T(en), args); }
        catch (FormatException) { return string.Format(CultureInfo.CurrentCulture, en, args); }   // a broken translation never breaks the row
    }

    /// <summary>A day and time as the language writes it: "Fri Nov 13, 16:16:51", "11월 13일 (금) 16:16:51", "11月13日 (週五) 16:16:51".</summary>
    public static string DayTime(DateTime local) => Code switch
    {
        "ko" => local.ToString("M월 d일 (ddd) HH:mm:ss", new CultureInfo("ko-KR")),
        "zh-TW" => local.ToString("M月d日 (ddd) HH:mm:ss", new CultureInfo("zh-TW")),
        _ => local.ToString("ddd MMM d, HH:mm:ss", CultureInfo.InvariantCulture),
    };

    /// <summary>Where the pick is kept (beside the other per-PC files; the tests' own folder in a test run).</summary>
    static string PickPath => Path.Combine(KinahLedger.Data.DictionaryCache.Dir, "language.txt");

    /// <summary>The pick as saved: "auto" (the default), "en", "ko" or "zh-TW".</summary>
    public static string Picked
    {
        get
        {
            try { var s = File.Exists(PickPath) ? File.ReadAllText(PickPath).Trim() : ""; return Choices.Any(c => c.Code == s) ? s : "auto"; }
            catch (IOException) { return "auto"; }
            catch (UnauthorizedAccessException) { return "auto"; }
        }
    }

    /// <summary>Saves the pick; it takes effect at the next start (the windows are built in one language).</summary>
    public static void Pick(string code)
    {
        try { Directory.CreateDirectory(Path.GetDirectoryName(PickPath)!); File.WriteAllText(PickPath, code); }
        catch (Exception ex) { CrashLog.Write("saving the language", ex); }
    }

    /// <summary>"auto": Korean on a Korean Windows, Traditional Chinese on a Taiwanese or Hong Kong one (or any zh-Hant), else English.</summary>
    public static string Resolve(string pick, CultureInfo? ui = null)
    {
        if (pick != "auto") return Choices.Any(c => c.Code == pick) ? pick : "en";
        ui ??= CultureInfo.CurrentUICulture;
        var name = ui.Name;
        if (name.StartsWith("ko", StringComparison.OrdinalIgnoreCase)) return "ko";
        if (name.StartsWith("zh", StringComparison.OrdinalIgnoreCase)
            && (name.Contains("Hant", StringComparison.OrdinalIgnoreCase) || name.EndsWith("TW", StringComparison.OrdinalIgnoreCase) || name.EndsWith("HK", StringComparison.OrdinalIgnoreCase) || name.EndsWith("MO", StringComparison.OrdinalIgnoreCase))) return "zh-TW";
        return "en";
    }

    /// <summary>Sets the language for this run: <paramref name="code"/> when given (tests and renders), else the player's pick.</summary>
    public static void Init(string? code = null)
    {
        Code = Resolve(code ?? Picked);
        _t = Code == "en" ? new(StringComparer.Ordinal) : Table(Code);
        // dates and day names in the same language ("11월 13일 (금)", not "Fri Nov 13"); numbers write the same in all three
        // (ko-KR and zh-TW group with "," and mark decimals with "."), so figures and anything parsed are as before
        if (Code != "en" && !CultureInfo.CurrentCulture.Name.StartsWith(Code == "ko" ? "ko" : "zh", StringComparison.OrdinalIgnoreCase))
        {
            var c = new CultureInfo(Code == "ko" ? "ko-KR" : "zh-TW");
            CultureInfo.DefaultThreadCurrentCulture = CultureInfo.DefaultThreadCurrentUICulture = c;
            CultureInfo.CurrentCulture = CultureInfo.CurrentUICulture = c;
        }
    }

    /// <summary>Every built-in table of a language, merged (a key in two files: the later file's, by name).</summary>
    public static Dictionary<string, string> Table(string code)
    {
        var t = new Dictionary<string, string>(StringComparer.Ordinal);
        var asm = Assembly.GetExecutingAssembly();
        string prefix = $"lang/{code}/";
        foreach (var name in asm.GetManifestResourceNames().Where(n => n.Replace('\\', '/').StartsWith(prefix, StringComparison.Ordinal)).OrderBy(n => n, StringComparer.Ordinal))
        {
            try
            {
                using var s = asm.GetManifestResourceStream(name)!;
                var part = JsonSerializer.Deserialize<Dictionary<string, string>>(s) ?? new();
                foreach (var (k, v) in part) if (k.Length > 0 && !string.IsNullOrWhiteSpace(v)) t[k] = v;
            }
            catch (Exception ex) { CrashLog.Write($"reading {name}", ex); }
        }
        return t;
    }

    /// <summary>
    /// A window built in XAML, in the language in use: each TextBlock's text (or each of its runs), a button's or a check box's
    /// words, a tooltip that is text, and a header. Text typed by the player (a TextBox) and lists filled from data are left as
    /// they are. Run it once the window is built; text a window sets later goes through <see cref="T"/> where it is set.
    /// </summary>
    public static void Apply(System.Windows.DependencyObject root)
    {
        if (Code == "en") return;
        var seen = new HashSet<System.Windows.DependencyObject>();
        void Walk(System.Windows.DependencyObject d)
        {
            if (!seen.Add(d)) return;
            switch (d)
            {
                case System.Windows.Controls.TextBox: return;
                case System.Windows.Controls.TextBlock tb:
                    // runs one by one, inside links and spans too (setting Text would drop a link); a plain text only when it changes
                    if (tb.Inlines.Count > 1 || tb.Inlines.Any(i => i is not System.Windows.Documents.Run)) Runs(tb.Inlines);
                    else if (System.Windows.Data.BindingOperations.GetBindingExpression(tb, System.Windows.Controls.TextBlock.TextProperty) == null && T(tb.Text) is var t && t != tb.Text) tb.Text = t;
                    break;
                case System.Windows.Controls.HeaderedContentControl hc when hc.Header is string h: hc.Header = T(h); break;
                case System.Windows.Controls.ContentControl cc when cc.Content is string c && cc is not System.Windows.Controls.ComboBoxItem: cc.Content = T(c); break;
            }
            if (d is System.Windows.FrameworkElement fe && fe.ToolTip is string tip) fe.ToolTip = T(tip);
            foreach (var ch in System.Windows.LogicalTreeHelper.GetChildren(d).OfType<System.Windows.DependencyObject>()) Walk(ch);
        }
        Walk(root);
    }

    static void Runs(System.Windows.Documents.InlineCollection inlines)
    {
        foreach (var i in inlines.ToList())
        {
            if (i is System.Windows.Documents.Run run) { if (T(run.Text) is var t && t != run.Text) run.Text = t; }
            else if (i is System.Windows.Documents.Span span) Runs(span.Inlines);
        }
    }

    /// <summary>For the tests: every language's table by its code ("ko", "zh-TW").</summary>
    public static IEnumerable<(string Code, Dictionary<string, string> Table)> Tables() =>
        Choices.Where(c => c.Code is not ("auto" or "en")).Select(c => (c.Code, Table(c.Code)));
}
