using System.IO;
using System.Windows.Media;

namespace KinahLedger.Ui;

/// <summary>
/// Inter (the window's face since the v2 look; the overlays draw Segoe UI Variable, HudKit.Text), read from files on disk: the exe's four TTFs are copied to
/// %LOCALAPPDATA%\KinahLedger\fonts the first time they are needed. Drawing from the exe's own resources held memory for good:
/// WPF keeps every stream it opens on a pack resource in that part's list for the life of the app, and its font loader opened
/// one on the Inter files each time DirectWrite read them (Steven's 1.0.30: 526,259 streams, 52 MB, after 4.7 hours, growing
/// about 11 MB an hour). A file on disk goes to DirectWrite's own file loader, which holds nothing. When the copy can't be
/// written the resources stand in, as before. options-test checks it (Ui/WpfApp.Fonts.cs).
/// </summary>
public static class AppFonts
{
    static readonly string[] Files = { "Inter-Regular.ttf", "Inter-Medium.ttf", "Inter-SemiBold.ttf", "Inter-Bold.ttf" };

    /// <summary>Set by the test and render commands: the copy goes to a temp folder, never the player's.</summary>
    public static string? DirOverride { get; set; }

    /// <summary>The folder Inter is read from, or null when the exe's resources are used.</summary>
    public static string? Dir { get; private set; }
    public static bool FromFiles => Dir != null;

    /// <summary>The text face: Inter, then Segoe UI for any glyph it lacks.</summary>
    public static FontFamily Text => _text ??= Make("Segoe UI Variable Text, Segoe UI");
    /// <summary>The headings' face: Inter, then Segoe UI's display cut.</summary>
    public static FontFamily Display => _display ??= Make("Segoe UI Variable Display, Segoe UI");
    static FontFamily? _text, _display;
    static bool _tried;
    static readonly object Lock = new();
    static readonly System.Resources.ResourceManager Resources = new(typeof(AppFonts).Assembly.GetName().Name + ".g", typeof(AppFonts).Assembly);

    static FontFamily Make(string fallback)
    {
        lock (Lock)
        {
            if (!_tried) { _tried = true; Dir = Unpack(); }
            return Dir != null
                ? new FontFamily(new Uri(Path.TrimEndingDirectorySeparator(Dir) + Path.DirectorySeparatorChar), "./#Inter, " + fallback)
                : new FontFamily(new Uri("pack://application:,,,/"), "./Fonts/#Inter, " + fallback);
        }
    }

    /// <summary>Copies the four TTFs out of the exe when a copy is missing or differs in size; null when it can't (a
    /// read-only folder) or when KL_PACK_FONTS=1 asks for the old way.</summary>
    static string? Unpack()
    {
        if (Environment.GetEnvironmentVariable("KL_PACK_FONTS") == "1") return null;
        try
        {
            var dir = DirOverride ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "KinahLedger", "fonts");
            Directory.CreateDirectory(dir);
            foreach (var name in Files)
            {
                // straight from the exe's .g resources (what pack:// reads), so no Application is needed and no pack stream is held
                using var src = Resources.GetStream("fonts/" + name.ToLowerInvariant());
                if (src == null) return null;
                var path = Path.Combine(dir, name);
                if (File.Exists(path) && new FileInfo(path).Length == src.Length) continue;
                var tmp = path + "." + Environment.ProcessId + ".tmp";
                using (var dst = File.Create(tmp)) src.CopyTo(dst);
                File.Move(tmp, path, overwrite: true);
            }
            return dir;
        }
        catch (Exception ex)
        {
            CrashLog.Note("fonts: reading Inter from the exe, the copy on disk failed: " + ex.Message);
            return null;
        }
    }
}
