using System.Windows;

namespace KinahLedger.Ui;

/// <summary>
/// The screen's work area, for the layout that fits windows to it (Full view's scale, a window's height cap, a pop-out's room).
/// A normal launch always reads the real one. options-test and ui-shot may be given a screen by KL_TEST_WORKAREA ("1920x1040", in
/// DIPs): GitHub's Windows runners are 1024x768 and their video driver offers no other size, so every check written for a
/// 1080p desktop (Full view's two columns, the Parser at 150%) failed there, and a check that can't run on the machine that guards
/// main guards nothing. Not a player setting: the variable is read only when a test or render command turns it on (Program.cs).
/// </summary>
internal static class ScreenArea
{
    static Rect? _test;

    /// <summary>The screen a test or render names, or null (the real one).</summary>
    internal static Rect? Test => _test;

    /// <summary>Called by the test and render commands: reads KL_TEST_WORKAREA ("WxH", DIPs). Anything else leaves the real screen.</summary>
    internal static void EnableFromEnvironment()
    {
        var v = Environment.GetEnvironmentVariable("KL_TEST_WORKAREA");
        if (string.IsNullOrWhiteSpace(v)) return;
        var p = v.Split('x', 'X');
        if (p.Length == 2 && double.TryParse(p[0], out var w) && double.TryParse(p[1], out var h) && w >= 640 && h >= 480) _test = new Rect(0, 0, w, h);
    }

    /// <summary>The work area of the main screen, in DIPs.</summary>
    internal static Rect Work => _test ?? SystemParameters.WorkArea;
}
