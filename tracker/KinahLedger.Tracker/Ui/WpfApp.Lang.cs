using System.Globalization;
using System.IO;

namespace KinahLedger.Ui;

public static partial class WpfApp
{
    /// <summary>
    /// The whole tracker in Korean and in Traditional Chinese (1.0, 2026-11-17): the main window on every page, the Options
    /// window, the overlays and their looks and themes, the Parser, the timers, the inventory, the recap, the wrapped sheets and
    /// the share pictures are drawn in each language, and the test fails on any text drawn with an English word left in it
    /// (a text that never went through L.T) and on any text L.T was asked for with no entry in that language's tables. Game
    /// names from the data files stay English (LangCheck.GameNames). <c>KL_OPTIONS_ONLY=lang</c> runs this part alone.
    /// </summary>
    static void LangChecks(Settings settings, Action<bool, string> check)
    {
        var dir = Path.Combine(Path.GetTempPath(), "KinahLedger-lang-test");
        var culture = CultureInfo.CurrentCulture; var ui = CultureInfo.CurrentUICulture;
        var defCulture = CultureInfo.DefaultThreadCurrentCulture; var defUi = CultureInfo.DefaultThreadCurrentUICulture;
        var skin = Skins.Current;
        var app = Ensure(); var shutdown = app.ShutdownMode;
        app.ShutdownMode = System.Windows.ShutdownMode.OnExplicitShutdown;   // one window per view: the first one's close must not end the application
        try
        {
            foreach (var code in new[] { "ko", "zh-TW" })
            {
                L.Init(code);
                LangCheck.Begin();
                var crashed = new List<string>();
                void Draw(string what, Action draw)
                {
                    try { draw(); }
                    catch (Exception ex) { crashed.Add($"{what}: {ex.GetType().Name} {ex.Message}"); }
                }
                var d = Path.Combine(dir, code);
                Directory.CreateDirectory(d);
                foreach (var kind in new[] { "Open world", "Crafting", "Events", "Quests", "Market", "Abyss", "Gathering" })
                    Draw(kind, () => Shot(settings, d, demo: true, kind: kind, tall: true));
                Draw("welcome", () => Shot(settings, d, demo: false, welcome: true));
                Draw("compact", () => Shot(settings, d, demo: true, compact: true));
                Draw("options", () => ShotOptions(settings, d));
                Draw("parser", () => ShotParser(settings, d, "all", null));
                Draw("themes", () => ShotParser(settings, d, "themes", null));
                Draw("meter share", () => ShotParser(settings, d, "share", null));
                Draw("heals", () => ShotParser(settings, d, "heals", null));
                Draw("pvp", () => ShotParser(settings, d, "pvp", null));
                Draw("inventory", () => ShotInventory(settings, d, null));
                Draw("timers page", () => ShotTimersPage(settings, d, null));
                Draw("timers", () => ShotBosses(settings, d, null));
                Draw("recap", () => ShotRecap(settings, d, null));
                Draw("week wrapped", () => ShotWrapped(settings, d, "week", null, null));
                Draw("day wrapped", () => ShotWrapped(settings, d, "day", null, null));
                var (onScreen, noEntry) = LangCheck.End();
                check(crashed.Count == 0, $"languages, {code}: every view draws ({string.Join("; ", crashed.Take(5))})");
                check(onScreen.Count == 0, $"languages, {code}: no English left on screen ({onScreen.Count} texts{Few(onScreen)})");
                check(noEntry.Count == 0, $"languages, {code}: every text asked of L.T has a {code} entry ({noEntry.Count} missing{Few(noEntry)})");
            }
        }
        finally
        {
            L.Init("en");
            CultureInfo.CurrentCulture = culture; CultureInfo.CurrentUICulture = ui;
            CultureInfo.DefaultThreadCurrentCulture = defCulture; CultureInfo.DefaultThreadCurrentUICulture = defUi;
            Skins.Apply(skin);
            app.ShutdownMode = shutdown;
            try { Directory.Delete(dir, true); } catch (Exception) { }
        }
        static string Few(List<string> l) => l.Count == 0 ? "" : ":\n        " + string.Join("\n        ", l.Take(40)) + (l.Count > 40 ? $"\n        … and {l.Count - 40} more" : "");
    }
}
