using System.Diagnostics;
using System.Drawing;
using KinahLedger.Ocr;

namespace KinahLedger.Platform;

/// <summary>
/// "bench": where a read's time goes on this PC, for the capture work of 0.8. Copies the chat area and the loot list
/// area N times and times the screen copy and the text recognition apart, then both reads side by side as the tracker does.
/// </summary>
public static class Bench
{
    public static int Run(Settings s, int n)
    {
        var game = GameWindow.Find(s.ProcessHints);
        if (game == null) { Console.WriteLine("Game window not found."); return 1; }
        var chat = Task.Run(() => ChatLocator.DetectAsync(game.Value)).GetAwaiter().GetResult().region;
        if (chat == null) { Console.WriteLine("Chat not found."); return 1; }
        var loot = LootPopup.Region(game.Value);
        Console.WriteLine($"Game {game.Value.Width}x{game.Value.Height}, chat {chat.Value.Width}x{chat.Value.Height}, loot list {loot.Width}x{loot.Height}, scale {s.Ocr.Scale}");

        var cap = new List<double>(); var ocr = new List<double>(); var lootMs = new List<double>(); var both = new List<double>(); var same = 0;
        string? lastHash = null;
        for (int i = 0; i < n; i++)
        {
            var sw = Stopwatch.StartNew();
            using var raw = ScreenReader.Capture(chat.Value);
            cap.Add(sw.Elapsed.TotalMilliseconds);
            var h = Hash(raw); if (h == lastHash) same++; lastHash = h;
            sw.Restart();
            Task.Run(() => ScreenReader.ReadBitmapAsync(raw, true, s.Ocr.Scale)).GetAwaiter().GetResult();
            ocr.Add(sw.Elapsed.TotalMilliseconds);
            sw.Restart();
            Task.Run(() => ScreenReader.ReadBoxesAsync(loot, 3, boost: true)).GetAwaiter().GetResult();
            lootMs.Add(sw.Elapsed.TotalMilliseconds);
            sw.Restart();
            using var raw2 = ScreenReader.Capture(chat.Value);
            Task.WhenAll(Task.Run(() => ScreenReader.ReadBitmapAsync(raw2, true, s.Ocr.Scale)), Task.Run(() => ScreenReader.ReadBoxesAsync(loot, 3, boost: true))).GetAwaiter().GetResult();
            both.Add(sw.Elapsed.TotalMilliseconds);
        }
        string S(List<double> v) { var o = v.Skip(1).OrderBy(x => x).ToList(); return o.Count == 0 ? "-" : $"median {o[o.Count / 2]:0} ms, worst {o[^1]:0} ms"; }
        Console.WriteLine($"screen copy of the chat:   {S(cap)}");
        Console.WriteLine($"recognize the chat:        {S(ocr)}");
        Console.WriteLine($"copy + read the loot list: {S(lootMs)}");
        Console.WriteLine($"chat and loot list at once:{S(both)}");
        Console.WriteLine($"chat unchanged between copies: {same} of {n - 1}");
        // the loot list at scale 2, and what each scale reads, for the same frames
        var l2 = new List<double>(); int rows3 = 0, rows2 = 0;
        for (int i = 0; i < Math.Min(n, 8); i++)
        {
            using var lr = ScreenReader.Capture(loot);
            var sw2 = Stopwatch.StartNew();
            var a2 = Task.Run(() => ScreenReader.ReadBoxesAsync(lr, loot.Location, 2, boost: true)).GetAwaiter().GetResult();
            l2.Add(sw2.Elapsed.TotalMilliseconds);
            var a3 = Task.Run(() => ScreenReader.ReadBoxesAsync(lr, loot.Location, 3, boost: true)).GetAwaiter().GetResult();
            rows2 += LootPopup.Rows(a2.Select(l => (l.Box.Top, l.Text))).Count; rows3 += LootPopup.Rows(a3.Select(l => (l.Box.Top, l.Text))).Count;
        }
        Console.WriteLine($"read the loot list at scale 2: {S(l2)}; loot rows found at scale 3: {rows3}, at scale 2: {rows2}");
        var proc = Process.GetCurrentProcess();
        Console.WriteLine($"CPU time used by the whole bench: {proc.TotalProcessorTime.TotalSeconds:0.0} s");
        return 0;
    }

    /// <summary>A quick fingerprint of a capture: every 7th pixel's colour, so an unchanged chat can skip recognition.</summary>
    public static unsafe string Hash(Bitmap b)
    {
        var d = b.LockBits(new Rectangle(0, 0, b.Width, b.Height), System.Drawing.Imaging.ImageLockMode.ReadOnly, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        try
        {
            ulong h = 1469598103934665603;
            for (int y = 0; y < b.Height; y += 3)
            {
                uint* row = (uint*)((byte*)d.Scan0 + y * d.Stride);
                for (int x = 0; x < b.Width; x += 7) { h ^= row[x]; h *= 1099511628211; }
            }
            return h.ToString("x16");
        }
        finally { b.UnlockBits(d); }
    }
}
