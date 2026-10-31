using System.Windows.Media;
using System.Windows.Media.Imaging;
using KinahLedger.Session;

namespace KinahLedger.Ui;

/// <summary>
/// What is left of "Copy for Discord" (0.7.6): its PNG writer, the grade colours every card uses, and the layout render of
/// the session's picture. Since 0.9.3 the button is Share, and the picture is the Share sheet's (Ui/MainWindow.Share.cs).
/// </summary>
public partial class MainWindow
{
    static readonly Color CardInk = Color.FromRgb(0xE9, 0xEF, 0xF7);

    /// <summary>This session's Share picture for a layout render (ui-shot), next to the window's own picture.</summary>
    public void SaveShareCard(string path)
    {
        if (_shareSession is { Made: > 0 } f) SavePng(RenderSharePicture(SharePicture.Compose(f, false, null, Fk)), path);
    }

    static void SavePng(BitmapSource bmp, string path)
    {
        var enc = new PngBitmapEncoder();
        enc.Frames.Add(BitmapFrame.Create(bmp));
        using var fs = File.Create(path);
        enc.Save(fs);
    }

    /// <summary>The game's grade colours, fixed (the cards are always drawn dark): Rare green, Legend blue, Unique yellow, Epic violet.</summary>
    static Color GradeColor(string grade) => grade switch
    {
        "Rare" => Color.FromRgb(0x62, 0xD9, 0x8B), "Legend" => Color.FromRgb(0x5A, 0xA6, 0xFF), "Unique" => Color.FromRgb(0xF3, 0xD3, 0x5E), "Epic" => Color.FromRgb(0xB4, 0x8B, 0xFF),
        _ => CardInk,
    };
}
