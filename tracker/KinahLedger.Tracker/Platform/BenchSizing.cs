using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using KinahLedger.Data;
using KinahLedger.Ocr;

namespace KinahLedger.Platform;

/// <summary>
/// "bench --sizing": the three OCR sizing switches of 2026-10-24 (ocr.chat_scale_to_target, ocr.target_sized,
/// ocr.plates_half) timed off and on, offline, on pictures the size Steven's 5120x2160 window gives, and what each reader
/// read both ways. No game, no capture. The built-in pictures are SYNTHETIC (drawn text at the measured heights: chat
/// 29 px in an 864x504 crop, the target band 1740x237, nameplates around the middle of a full frame); --chat and --frames
/// add real ones from a folder (chat crops; whole game frames for the target band and the nameplates).
/// </summary>
public static class BenchSizing
{
    public static int Run(Settings s, Dictionary<string, string> o)
    {
        int n = int.TryParse(o.GetValueOrDefault("n"), out var nn) ? Math.Max(2, nn) : 7;
        var items = ItemDictionary.LoadOrEmpty(s.ItemsFile);
        var parser = new ChatParser(items);
        Console.WriteLine($"bench --sizing: {n} timed runs each (median, the first run dropped), Windows OCR max image side {Windows.Media.Ocr.OcrEngine.MaxImageDimension} px");
        var rows = new List<string[]>();
        bool same = true, chatSame = true;

        // ---- chat ----
        var chats = new List<(string Name, Bitmap Bmp, string[] Truth)>();
        foreach (var (name, w, h, px, bright) in new[]
        {
            ("chat 5120x2160, dark backing (synthetic)", 864, 504, 29f, false), ("chat 5120x2160, bright scene behind (synthetic)", 864, 504, 29f, true),
            ("chat, 24 px text (synthetic)", 720, 420, 24f, false), ("chat, 40 px text (synthetic)", 1190, 695, 40f, true), ("chat 1920x1080, 12 px (synthetic)", 405, 236, 12f, false),
        })
            chats.Add((name, DrawChat(w, h, px, bright, out var truth), truth));
        if (o.TryGetValue("chat", out var chatDir) && Directory.Exists(chatDir))
            foreach (var f in Directory.EnumerateFiles(chatDir).Where(IsPicture).OrderBy(x => x))
                chats.Add(("chat " + Path.GetFileName(f), Load(f), Array.Empty<string>()));
        foreach (var (name, bmp, truth) in chats)
        {
            using var bmpOwner = bmp;
            // the text height as the live reader measures it: its word boxes at the old floor, in screen pixels
            var boxes = Run(() => ScreenReader.ReadBoxesAsync(bmp, Point.Empty, 2.0, boost: true));
            var hs = boxes.SelectMany(l => l.Words ?? Array.Empty<OcrWordBox>()).Where(w => w.Text.Count(char.IsLetter) >= 3).Select(w => (double)w.Box.Height).OrderBy(x => x).ToList();
            double textPx = hs.Count == 0 ? 0 : hs[hs.Count / 2];
            var oldScale = new TextScale(s.Ocr.Scale, s.Ocr.TargetTextPx) { ToTarget = () => false };
            var newScale = new TextScale(s.Ocr.Scale, s.Ocr.TargetTextPx) { ToTarget = () => true };
            double fOld = textPx > 0 ? oldScale.Want(textPx) : oldScale.Factor, fNew = textPx > 0 ? newScale.Want(textPx) : newScale.Factor;
            var (tOld, lOld) = Time(n, () => ScreenReader.ReadBitmapAsync(bmp, true, fOld));
            var (tNew, lNew) = Time(n, () => ScreenReader.ReadBitmapAsync(bmp, true, fNew));
            string Score(List<string> lines) => truth.Length > 0
                ? $"{truth.Count(t => lines.Contains(t))}/{truth.Length} exact, {lines.Count(l => parser.Meaning(l).Key.Length > 0)} pickups"
                : $"{lines.Count} lines, {lines.Count(l => parser.Meaning(l).Key.Length > 0)} pickups, {lines.Count(l => parser.Meaning(l).Exact)} exact";
            int exactOld = truth.Length > 0 ? truth.Count(t => lOld.Contains(t)) : lOld.Count(l => parser.Meaning(l).Exact);
            int exactNew = truth.Length > 0 ? truth.Count(t => lNew.Contains(t)) : lNew.Count(l => parser.Meaning(l).Exact);
            bool ok = exactNew >= exactOld;
            chatSame &= ok;
            // what each enlarging factor reads (ocr-test --scales on the same picture): exact lines at 1, 1.25, 1.5, 2, 3x
            var sweep = new[] { 1.0, 1.25, 1.5, 2.0, 3.0 }.Select(f =>
            {
                var ls = Run(() => ScreenReader.ReadBitmapAsync(bmp, true, f));
                return truth.Length > 0 ? truth.Count(t => ls.Contains(t)) : ls.Count(l => parser.Meaning(l).Exact);
            });
            Console.WriteLine($"  {name}: text {textPx:0} px; exact lines at 1x/1.25x/1.5x/2x/3x: {string.Join("/", sweep)}; off {fOld:0.##}x, on {fNew:0.##}x");
            rows.Add(new[] { name, $"{bmp.Width}x{bmp.Height}, text {textPx:0} px", $"{fOld:0.##}x = {(int)(bmp.Width * fOld)}x{(int)(bmp.Height * fOld)}", $"{tOld:0}", $"{fNew:0.##}x = {(int)(bmp.Width * fNew)}x{(int)(bmp.Height * fNew)}", $"{tNew:0}", $"{Score(lOld)} / {Score(lNew)}", ok ? "not worse" : "WORSE" });
            if (o.ContainsKey("verbose")) { Console.WriteLine($"[{name}] off:"); lOld.ForEach(l => Console.WriteLine("   " + l)); Console.WriteLine("  on:"); lNew.ForEach(l => Console.WriteLine("   " + l)); }
        }

        // --sweep: many drawn chats (seeds, fonts, dark and bright scenes, text 26 to 32 px), exact lines summed per factor
        if (o.GetValueOrDefault("sweep") is "true" or "chat")
        {
            var fs = new[] { 1.25, 1.5, 1.75, 2.0 };
            foreach (var px in new[] { 26f, 29f, 32f })
            {
                var sum = new int[fs.Length]; var worse = new int[fs.Length]; int total = 0, pics = 0;
                foreach (var family in new[] { "Segoe UI", "Arial", "Tahoma" })
                    foreach (var bright in new[] { false, true })
                        for (int seed = 1; seed <= 4; seed++)
                        {
                            using var bmp = DrawChat((int)(px * 29.8f), (int)(px * 17.4f), px, bright, out var truth, seed, family);
                            var got = fs.Select(f => { var ls = Run(() => ScreenReader.ReadBitmapAsync(bmp, true, f)); return truth.Count(t => ls.Contains(t)); }).ToArray();
                            for (int i = 0; i < fs.Length; i++) { sum[i] += got[i]; if (got[i] < got[^1]) worse[i]++; }
                            total += truth.Length; pics++;
                        }
                Console.WriteLine($"  sweep, {px:0} px text, {pics} drawn chats ({total} lines): exact lines " + string.Join(", ", fs.Select((f, i) => $"{f:0.##}x {sum[i]} ({worse[i]} pictures under 2x)")));
            }
        }

        // --sweep: many drawn target bands (enemies from the list, fonts, dark and bright skies), the enemy found per way of reading
        if (o.GetValueOrDefault("sweep") is "true" or "target")
        {
            var mobs = TargetReader.Known.Where(m => m.Length is >= 6 and <= 24).ToArray();
            foreach (var px in new[] { 22f, 29f })
            {
                var ways = new (string Name, bool Sized, double? F)[] { ("whole band 3x", false, null), ("own text 3x", true, 3.0), ("own text 2x", true, 2.0), ("own text 1.5x", true, 1.5), ("own text 1.25x", true, 1.25) };
                var hits = new int[ways.Length]; var ms = new double[ways.Length]; int pics = 0;
                var rnd = new Random(7);
                foreach (var family in new[] { "Segoe UI Semibold", "Arial", "Tahoma" })
                    foreach (var bright in new[] { false, true })
                        for (int k = 0; k < 4; k++)
                        {
                            var mob = mobs[rnd.Next(mobs.Length)];
                            using var frame = DrawBand(5120, 2160, bright, mob, px, family, k);
                            var strip = TargetReader.Strip(new Rectangle(0, 0, 5120, 2160));
                            using var band = frame.Clone(strip, PixelFormat.Format32bppArgb);
                            for (int i = 0; i < ways.Length; i++)
                            {
                                var t = new TargetReader(); var sw = Stopwatch.StartNew();
                                var ls = Run(() => t.ReadStripAsync(band, strip.Location, ways[i].Sized, ways[i].F));
                                ms[i] += sw.Elapsed.TotalMilliseconds;
                                if (TargetReader.FindName(ls.Select(l => (l.Box.Top, l.Text)), out _) == mob) hits[i]++;
                            }
                            pics++;
                        }
                Console.WriteLine($"  target sweep, {px:0} px names, {pics} drawn bands: enemy read right " + string.Join(", ", ways.Select((w, i) => $"{w.Name} {hits[i]} ({ms[i] / pics:0} ms)")));
            }
        }

        // ---- the target band and the nameplates: built-in frames, then --frames ----
        var frames = new List<(string Name, Bitmap Bmp, string? Enemy, string[] Plates)>
        {
            ("5120x2160, dark scene (synthetic)", DrawFrame(5120, 2160, false, out var e1, out var p1), e1, p1),
            ("5120x2160, bright sky (synthetic)", DrawFrame(5120, 2160, true, out var e2, out var p2), e2, p2),
        };
        if (o.TryGetValue("frames", out var frameDir) && Directory.Exists(frameDir))
            foreach (var f in Directory.EnumerateFiles(frameDir).Where(IsPicture).OrderBy(x => x))
                frames.Add((Path.GetFileName(f), Load(f), null, Array.Empty<string>()));
        foreach (var (name, frame, enemy, plateNames) in frames)
        {
            using var frameOwner = frame;
            var game = new Rectangle(0, 0, frame.Width, frame.Height);
            var strip = TargetReader.Strip(game);
            using var band = frame.Clone(strip, PixelFormat.Format32bppArgb);
            var oldT = new TargetReader();
            var newT = new TargetReader();
            // the sized reader settles its factor on its first reads (3x until five words are measured), as it does live
            for (int i = 0; i < 3; i++) Run(() => newT.ReadStripAsync(band, strip.Location, true));
            var (tOld, lOld) = Time(n, () => oldT.ReadStripAsync(band, strip.Location, false));
            long pxOld = oldT.LastPixels;
            var (tNew, lNew) = Time(n, () => newT.ReadStripAsync(band, strip.Location, true));
            long pxNew = newT.LastPixels;
            string? nOld = TargetReader.FindName(lOld.Select(l => (l.Box.Top, l.Text)), out _), nNew = TargetReader.FindName(lNew.Select(l => (l.Box.Top, l.Text)), out _);
            bool ok = nOld == nNew;
            same &= ok;
            string truthT = enemy == null ? "" : nNew == enemy ? ", the drawn enemy" : ", NOT the drawn enemy";
            rows.Add(new[] { "target band, " + name, $"{strip.Width}x{strip.Height}, text {(newT.TextPx is double tp ? $"{tp:0}" : "?")} px", $"3x, 2 reads, {pxOld / 1000}k px", $"{tOld:0}", $"{newT.Factor:0.##}x, {pxNew / 1000}k px", $"{tNew:0}", $"{nOld ?? "none"} / {nNew ?? "none"}{truthT}", ok ? "same" : "DIFFERENT" });
            if (o.ContainsKey("verbose")) { Console.WriteLine($"[{name}] band off: " + string.Join(" | ", lOld.Select(l => l.Text))); Console.WriteLine("  band on: " + string.Join(" | ", lNew.Select(l => l.Text))); }

            // nameplates: the bar search and the names, off (24-bit copy, full scan) and on (in place, half, then full size)
            var (bOld, barsOld) = TimeSync(n, () => Nameplates.FindBars(frame, false));
            var (bNew, barsNew) = TimeSync(n, () => Nameplates.FindBars(frame, true));
            var (rOld, platesOld) = Time(Math.Min(n, 3), () => Nameplates.ReadAsync(frame, false));
            var (rNew, platesNew) = Time(Math.Min(n, 3), () => Nameplates.ReadAsync(frame, true));
            var setOld = barsOld.OrderBy(b => b.Y).ThenBy(b => b.X).ToList();
            string Names(List<Nameplates.Plate> ps) => string.Join(", ", ps.Select(p => p.Enemy).OrderBy(x => x));
            bool sameBars = setOld.SequenceEqual(barsNew);
            bool okP = sameBars && Names(platesOld) == Names(platesNew);
            same &= okP;
            string truthP = plateNames.Length == 0 ? "" : $" ({platesNew.Count(p => plateNames.Contains(p.Enemy))} of the {plateNames.Length} drawn)";
            rows.Add(new[] { "nameplate bars, " + name, $"{frame.Width}x{frame.Height}", "24-bit copy, full scan", $"{bOld:0}", "in place, half, then full size", $"{bNew:0}", $"{barsOld.Count} / {barsNew.Count} bars", sameBars ? "same bars" : "DIFFERENT" });
            rows.Add(new[] { "nameplates with names, " + name, $"{frame.Width}x{frame.Height}", "", $"{rOld:0}", "", $"{rNew:0}", $"{Names(platesOld)} / {Names(platesNew)}{truthP}", okP ? "same" : "DIFFERENT" });
        }

        var head = new[] { "reader, picture", "crop", "off: size", "off ms", "on: size", "on ms", "read (off / on)", "result" };
        Console.WriteLine();
        Console.WriteLine("| " + string.Join(" | ", head) + " |");
        Console.WriteLine("|" + string.Concat(head.Select(_ => "---|")));
        foreach (var r in rows) Console.WriteLine("| " + string.Join(" | ", r) + " |");
        Console.WriteLine();
        Console.WriteLine(same ? "sizing: the target band and the nameplates read the same with ocr.target_sized and ocr.plates_half on" : "sizing: the target band or the nameplates read differently with their switch on (see the table)");
        Console.WriteLine(chatSame ? "chat: no picture read worse at the lower factor (ocr.chat_scale_to_target, off in code)" : "chat: a picture read worse at the lower factor, so ocr.chat_scale_to_target stays off in code");
        return same ? 0 : 1;
    }

    static bool IsPicture(string f) => f.EndsWith(".png", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".jpeg", StringComparison.OrdinalIgnoreCase);
    static Bitmap Load(string f) => Run(() => ScreenReader.LoadAsync(f));
    static T Run<T>(Func<Task<T>> f) => Task.Run(f).GetAwaiter().GetResult();

    static (double Ms, T Last) Time<T>(int n, Func<Task<T>> f)
    {
        var ms = new List<double>(); T last = default!;
        for (int i = 0; i < n; i++) { var sw = Stopwatch.StartNew(); last = Run(f); ms.Add(sw.Elapsed.TotalMilliseconds); }
        var o = ms.Skip(1).OrderBy(x => x).ToList();
        return (o[o.Count / 2], last);
    }

    static (double Ms, T Last) TimeSync<T>(int n, Func<T> f) => Time(n, () => Task.FromResult(f()));

    // ---- synthetic pictures ----
    static readonly Color Gold = Color.FromArgb(240, 196, 64), Green = Color.FromArgb(120, 230, 110), Blue = Color.FromArgb(110, 170, 255), White = Color.FromArgb(235, 235, 235);

    /// <summary>Text with a one-pixel dark outline, as the game draws its UI text.</summary>
    static void Outlined(Graphics g, string text, Font font, Color colour, float x, float y)
    {
        using var shadow = new SolidBrush(Color.FromArgb(220, 10, 10, 12));
        foreach (var (dx, dy) in new[] { (-1, 0), (1, 0), (0, -1), (0, 1), (1, 1) }) g.DrawString(text, font, shadow, x + dx, y + dy);
        using var b = new SolidBrush(colour);
        g.DrawString(text, font, b, x, y);
    }

    /// <summary>Scenery: a gradient with soft blotches, dark or bright, the same every time.</summary>
    static void Scene(Graphics g, int w, int h, bool bright, int seed)
    {
        var rnd = new Random(seed);
        using (var lg = new LinearGradientBrush(new Rectangle(0, 0, w, h), bright ? Color.FromArgb(150, 200, 245) : Color.FromArgb(40, 52, 46), bright ? Color.FromArgb(210, 225, 235) : Color.FromArgb(70, 64, 50), 90f))
            g.FillRectangle(lg, 0, 0, w, h);
        for (int i = 0; i < 60; i++)
        {
            int v = bright ? rnd.Next(170, 250) : rnd.Next(40, 110);
            using var b = new SolidBrush(Color.FromArgb(90, v, v - rnd.Next(0, 20), v - rnd.Next(0, 40)));
            g.FillEllipse(b, rnd.Next(-200, w), rnd.Next(-100, h), rnd.Next(80, Math.Max(100, w / 4)), rnd.Next(40, Math.Max(60, h / 4)));
        }
    }

    /// <summary>A chat crop: ten lines at about <paramref name="px"/> px, item names coloured by grade, on the game's translucent backing.</summary>
    static readonly string[] ChatLines =
    {
        "Acquired 436 Kina.", "Spiritstone acquired.", "Acquired Odyle x3.", "Raw Leather acquired.", "Acquired 1,240 Kina.",
        "Fine Odyle acquired.", "Riverside Helm acquired.", "Acquired Fine Odyle x2.", "Orichalcum Ore acquired.", "Acquired 300 Kina.",
        "Aria acquired.", "Fresh Aria acquired.", "Acquired 431 Kina.", "Forgotten Bow acquired.", "Acquired Spiritstone x4.", "Odyle acquired.",
    };

    internal static Bitmap DrawChat(int w, int h, float px, bool bright, out string[] truth, int seed = 0, string family = "Segoe UI")
    {
        var rnd = new Random(seed);
        truth = seed == 0 ? ChatLines.Take(10).ToArray() : ChatLines.OrderBy(_ => rnd.Next()).Take(10).ToArray();
        var bmp = new Bitmap(w, h, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(bmp);
        Scene(g, w, h, bright, 11 + seed);
        using (var back = new SolidBrush(Color.FromArgb(bright ? 110 : 170, 8, 10, 14))) g.FillRectangle(back, 0, 0, w, h);
        g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
        using var font = new Font(family, px * 0.98f, FontStyle.Regular, GraphicsUnit.Pixel);
        float lineH = h / 10.5f, y = lineH * 0.25f;
        var colours = new[] { Green, Blue, Gold, White };
        for (int i = 0; i < truth.Length; i++, y += lineH)
        {
            float x = px * 0.4f;
            // the item name in its grade's colour, the rest white
            foreach (var (part, isName) in Split(truth[i]))
            {
                Outlined(g, part, font, isName ? colours[i % colours.Length] : White, x, y);
                x += g.MeasureString(part, font, PointF.Empty, StringFormat.GenericTypographic).Width + (part.EndsWith(' ') ? px * 0.28f : 0);
            }
        }
        return bmp;
    }

    static IEnumerable<(string, bool)> Split(string line)
    {
        if (line.StartsWith("Acquired ") && line.EndsWith(" Kina.")) { yield return (line, false); yield break; }
        if (line.StartsWith("Acquired "))
        {
            var rest = line[9..]; int x = rest.LastIndexOf(" x", StringComparison.Ordinal);
            yield return ("Acquired ", false); yield return (rest[..x], true); yield return (rest[x..], false); yield break;
        }
        int a = line.LastIndexOf(" acquired.", StringComparison.Ordinal);
        yield return (line[..a], true); yield return (line[a..], false);
    }

    /// <summary>A frame with only the target bar drawn: level, the enemy's gold name at <paramref name="px"/> px, distance, health bar.</summary>
    internal static Bitmap DrawBand(int w, int h, bool bright, string enemy, float px, string family, int seed)
    {
        var bmp = new Bitmap(w, h / 8, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(bmp);
        Scene(g, w, h / 8, bright, 40 + seed);
        g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
        using var redB = new SolidBrush(Color.FromArgb(241, 54, 82));
        using var tf = new Font(family, px * 1.0f, FontStyle.Regular, GraphicsUnit.Pixel);
        var strip = TargetReader.Strip(new Rectangle(0, 0, w, h));
        float cx = strip.Left + strip.Width / 2f + (seed - 2) * 40, ty = h * 0.03f;
        string lvl = $"{40 + seed * 3} ", dist = $" {seed + 1}m";
        float wl = g.MeasureString(lvl, tf).Width, wn = g.MeasureString(enemy, tf).Width, wd = g.MeasureString(dist, tf).Width;
        float x = cx - (wl + wn + wd) / 2;
        Outlined(g, lvl, tf, White, x, ty); Outlined(g, enemy, tf, Gold, x + wl, ty); Outlined(g, dist, tf, White, x + wl + wn, ty);
        using var dark = new SolidBrush(Color.FromArgb(200, 20, 16, 18));
        float bw = w * 0.12f, by = ty + px * 1.7f;
        g.FillRectangle(dark, cx - bw / 2 - 3, by - 3, bw + 6, h * 0.008f + 6);
        g.FillRectangle(redB, cx - bw / 2, by, bw * (0.3f + seed * 0.15f), h * 0.008f);
        return bmp;
    }

    /// <summary>
    /// A whole game frame: the target bar at the top centre (level, gold name, distance, red health bar), and around the
    /// middle three enemies in a fight (gold name, a red bar 0.7% of the height under it), one standing by (name, no bar),
    /// the player's own bar, and red that is no bar (a tall banner, specks).
    /// </summary>
    internal static Bitmap DrawFrame(int w, int h, bool bright, out string? enemy, out string[] plates)
    {
        enemy = "Highland Karnif";
        plates = new[] { "Hard Kalgolem", "Highland Karnif", "Drakan Probe" };
        var bmp = new Bitmap(w, h, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(bmp);
        Scene(g, w, h, bright, 23);
        g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
        g.SmoothingMode = SmoothingMode.None;
        using var redB = new SolidBrush(Color.FromArgb(241, 54, 82));
        // the target bar: about 29 px text, its health bar under it
        float tpx = h * 0.0134f;
        using (var tf = new Font("Segoe UI Semibold", tpx * 1.08f, FontStyle.Regular, GraphicsUnit.Pixel))
        {
            var strip = TargetReader.Strip(new Rectangle(0, 0, w, h));
            float cx = strip.Left + strip.Width / 2f, ty = h * 0.03f;
            string lvl = "45 ", dist = " 1m";
            float wl = g.MeasureString(lvl, tf).Width, wn = g.MeasureString(enemy, tf).Width, wd = g.MeasureString(dist, tf).Width;
            float x = cx - (wl + wn + wd) / 2;
            Outlined(g, lvl, tf, White, x, ty); Outlined(g, enemy, tf, Gold, x + wl, ty); Outlined(g, dist, tf, White, x + wl + wn, ty);
            using var dark = new SolidBrush(Color.FromArgb(200, 20, 16, 18));
            float bw = w * 0.12f, by = ty + tpx * 1.7f;
            g.FillRectangle(dark, cx - bw / 2 - 3, by - 3, bw + 6, h * 0.008f + 6);
            g.FillRectangle(redB, cx - bw / 2, by, bw * 0.8f, h * 0.008f);
        }
        // nameplates: the name about 2% of the height above a bar 0.7% of it tall
        float ppx = h * 0.011f;
        using var pf = new Font("Segoe UI Semibold", ppx * 1.08f, FontStyle.Regular, GraphicsUnit.Pixel);
        void Plate(string name, float cx, float barY, bool bar, Color nameColour)
        {
            float bw = h * 0.09f, bh = (float)Math.Round(h * 0.007);
            float nw = g.MeasureString(name, pf).Width;
            Outlined(g, name, pf, nameColour, cx - nw / 2, barY - h * 0.022f);
            if (!bar) return;
            using var dark = new SolidBrush(Color.FromArgb(220, 15, 12, 14));
            g.FillRectangle(dark, (int)(cx - bw / 2) - 2, (int)barY - 2, (int)bw + 4, (int)bh + 4);
            g.FillRectangle(redB, (int)(cx - bw / 2), (int)barY, (int)(bw * 0.7f), (int)bh);
        }
        Plate("Hard Kalgolem", w * 0.38f, h * 0.42f, true, Gold);
        Plate("Highland Karnif", w * 0.61f, h * 0.36f, true, Gold);
        Plate("Drakan Probe", w * 0.52f, h * 0.55f, true, Gold);
        Plate("Snowfield Drakan Probe", w * 0.70f, h * 0.50f, false, Gold);
        Plate("Swoly", w * 0.50f, h * 0.47f, true, White);
        // red that is no bar: a tall banner and specks
        g.FillRectangle(redB, (int)(w * 0.25f), (int)(h * 0.30f), (int)(h * 0.02f), (int)(h * 0.12f));
        var rnd = new Random(5);
        for (int i = 0; i < 300; i++) g.FillRectangle(redB, rnd.Next((int)(w * 0.12), (int)(w * 0.88)), rnd.Next((int)(h * 0.05), (int)(h * 0.8)), rnd.Next(1, 6), rnd.Next(1, 4));
        return bmp;
    }
}
