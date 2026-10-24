using System.Drawing;
using System.Drawing.Imaging;
using System.Text.Json;

namespace KinahLedger.Ocr;

/// <summary>
/// A small grey picture of a piece of the game's interface that never changes (an icon, a frame corner), with where it
/// sits in a reference frame as fractions of the game window, for <see cref="TemplateMatch"/> to find it.
/// </summary>
public sealed class AnchorTemplate
{
    public string Name { get; init; } = "";
    /// <summary>The region whose crop follows this anchor (loot, craft, zone, target, balance, chat).</summary>
    public string Region { get; init; } = "";
    /// <summary>Where the template sat in its reference frame: x, y, w, h as fractions of the game window.</summary>
    public RectangleF Where { get; init; }
    /// <summary>The reference frame's height in pixels: the template is scaled by the game's height over this.</summary>
    public int RefHeight { get; init; }
    /// <summary>How far around <see cref="Where"/> it is looked for, as a fraction of the game's height.</summary>
    public double Search { get; init; } = 0.05;
    public Bitmap? Picture { get; init; }
}

/// <summary>
/// Normalised cross-correlation on grey pixels (brief item 4): the place in a picture that looks most like a template,
/// whatever the brightness and contrast, and how alike (1 the same, 0 nothing in common, below 0 the negative). The sums
/// over each window come from integral images, so a search costs the template's size times the positions tried.
/// </summary>
public static class TemplateMatch
{
    /// <summary>Grey levels (0 to 1) of <paramref name="r"/> in <paramref name="b"/>, shrunk by <paramref name="shrink"/>.</summary>
    public static float[] Gray(Bitmap b, Rectangle r, int shrink, out int w, out int h)
    {
        r.Intersect(new Rectangle(0, 0, b.Width, b.Height));
        shrink = Math.Max(1, shrink);
        w = Math.Max(1, r.Width / shrink); h = Math.Max(1, r.Height / shrink);
        var outp = new float[w * h];
        if (r.Width <= 0 || r.Height <= 0) return outp;
        var data = b.LockBits(r, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        try
        {
            int stride = data.Stride;
            var buf = new byte[stride * r.Height];
            System.Runtime.InteropServices.Marshal.Copy(data.Scan0, buf, 0, buf.Length);
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    float s = 0;
                    for (int dy = 0; dy < shrink; dy++)
                        for (int dx = 0; dx < shrink; dx++)
                        {
                            int i = (y * shrink + dy) * stride + (x * shrink + dx) * 4;
                            s += (buf[i + 2] * 0.299f + buf[i + 1] * 0.587f + buf[i] * 0.114f) / 255f;
                        }
                    outp[y * w + x] = s / (shrink * shrink);
                }
        }
        finally { b.UnlockBits(data); }
        return outp;
    }

    /// <summary>
    /// The best place of template <paramref name="t"/> (tw by th) in picture <paramref name="img"/> (iw by ih), and its
    /// score. A flat template or window (nothing to correlate) scores 0.
    /// </summary>
    public static (Point At, double Score) Find(float[] img, int iw, int ih, float[] t, int tw, int th)
    {
        if (tw > iw || th > ih || tw * th == 0) return (Point.Empty, 0);
        int n = tw * th;
        double tMean = 0; foreach (var v in t) tMean += v; tMean /= n;
        double tVar = 0; var tc = new double[n];
        for (int i = 0; i < n; i++) { tc[i] = t[i] - tMean; tVar += tc[i] * tc[i]; }
        if (tVar < 1e-9) return (Point.Empty, 0);
        // integral images of the picture and its square, one row and column of zeros in front
        var s1 = new double[(iw + 1) * (ih + 1)]; var s2 = new double[(iw + 1) * (ih + 1)];
        for (int y = 0; y < ih; y++)
        {
            double r1 = 0, r2 = 0;
            for (int x = 0; x < iw; x++)
            {
                double v = img[y * iw + x]; r1 += v; r2 += v * v;
                s1[(y + 1) * (iw + 1) + x + 1] = s1[y * (iw + 1) + x + 1] + r1;
                s2[(y + 1) * (iw + 1) + x + 1] = s2[y * (iw + 1) + x + 1] + r2;
            }
        }
        double Sum(double[] s, int x, int y) => s[(y + th) * (iw + 1) + x + tw] - s[y * (iw + 1) + x + tw] - s[(y + th) * (iw + 1) + x] + s[y * (iw + 1) + x];
        var best = (At: Point.Empty, Score: double.MinValue);
        for (int y = 0; y + th <= ih; y++)
            for (int x = 0; x + tw <= iw; x++)
            {
                double sum = Sum(s1, x, y), sq = Sum(s2, x, y);
                double wVar = sq - sum * sum / n;
                if (wVar < 1e-9) { if (best.Score < 0) best = (new Point(x, y), 0); continue; }
                double cross = 0;
                for (int ty = 0; ty < th; ty++)
                {
                    int row = (y + ty) * iw + x, trow = ty * tw;
                    for (int tx = 0; tx < tw; tx++) cross += img[row + tx] * tc[trow + tx];
                }
                double score = cross / Math.Sqrt(wVar * tVar);
                if (score > best.Score) best = (new Point(x, y), score);
            }
        return best.Score == double.MinValue ? (Point.Empty, 0) : best;
    }

    /// <summary>
    /// The anchor templates under data/anchors (built into the exe, and beside it for trying one out): each NAME.png with
    /// a NAME.json {"region", "where": [x, y, w, h] fractions of the game window, "ref_h": the reference frame's height,
    /// "search": fraction of the height looked around it}. None yet: the folder starts empty.
    /// </summary>
    public static List<AnchorTemplate> Load()
    {
        var found = new List<AnchorTemplate>();
        try
        {
            var asm = typeof(TemplateMatch).Assembly;
            foreach (var res in asm.GetManifestResourceNames().Where(n => n.StartsWith("anchors/", StringComparison.Ordinal) && n.EndsWith(".png", StringComparison.OrdinalIgnoreCase)))
            {
                using var png = asm.GetManifestResourceStream(res);
                using var meta = asm.GetManifestResourceStream(res[..^4] + ".json");
                if (png != null && meta != null && Parse(Path.GetFileNameWithoutExtension(res), png, meta) is { } t) found.Add(t);
            }
            var dir = Path.Combine(AppContext.BaseDirectory, "data", "anchors");
            if (Directory.Exists(dir))
                foreach (var p in Directory.GetFiles(dir, "*.png"))
                {
                    var j = Path.ChangeExtension(p, ".json");
                    if (!File.Exists(j) || found.Any(f => f.Name == Path.GetFileNameWithoutExtension(p))) continue;
                    using var png = File.OpenRead(p); using var meta = File.OpenRead(j);
                    if (Parse(Path.GetFileNameWithoutExtension(p), png, meta) is { } t) found.Add(t);
                }
        }
        catch (Exception ex) { CrashLog.Note("anchors: templates not loaded: " + ex.Message); }
        return found;
    }

    static AnchorTemplate? Parse(string name, Stream png, Stream meta)
    {
        using var doc = JsonDocument.Parse(meta);
        var r = doc.RootElement;
        if (!r.TryGetProperty("where", out var w) || w.GetArrayLength() != 4) return null;
        var f = w.EnumerateArray().Select(e => (float)e.GetDouble()).ToArray();
        using var loaded = new Bitmap(png);
        return new AnchorTemplate
        {
            Name = name,
            Region = r.TryGetProperty("region", out var rg) ? rg.GetString() ?? "" : "",
            Where = new RectangleF(f[0], f[1], f[2], f[3]),
            RefHeight = r.TryGetProperty("ref_h", out var rh) ? rh.GetInt32() : 2160,
            Search = r.TryGetProperty("search", out var se) ? Math.Clamp(se.GetDouble(), 0.005, 0.25) : 0.05,
            Picture = new Bitmap(loaded),
        };
    }
}

/// <summary>
/// Where the game's interface really is, found from anchors instead of assumed from fractions of the window (brief item
/// 4, phase one: shadow only). Two anchors come free from reads already made: the Kinah counter strip (the numbers'
/// boxes from BalanceReader's last read) and the chat (the box ChatLocator found above its input row, or from its
/// lines). Template anchors (data/anchors) are matched every <see cref="EverySeconds"/> s when there are any. Each
/// region's anchored crop is compared with its fixed-fraction crop and written to reads.log once a minute
/// ("anchor loot dx=.. dy=.. conf=.."). Nothing here changes which crops are read. With the game in view and no anchor
/// seen for <see cref="LostSeconds"/> s, the layout is "not found": logged, nothing paused (phase two decides that).
/// </summary>
public sealed class Anchors
{
    public const double EverySeconds = 2, LogSeconds = 60, LostSeconds = 60;
    /// <summary>A template hit below this score isn't the anchor.</summary>
    public const double MinScore = 0.7;

    /// <summary>One anchor's last sighting: the box it was found in, the box the fixed fractions expect, how sure.</summary>
    public sealed record Hit(string Name, string Region, Rectangle Found, Rectangle Expected, double Conf, DateTime At);

    readonly Dictionary<string, Hit> _hits = new();
    readonly List<AnchorTemplate> _templates;
    DateTime _tickAt = DateTime.MinValue, _logAt = DateTime.MinValue, _startedAt = DateTime.MinValue;
    double _costMs; int _ticks;
    readonly object _lock = new();

    /// <summary>Phase one's verdict: no anchor seen lately while the game is in view (logged only).</summary>
    public bool LayoutNotFound { get; private set; }

    public Anchors(List<AnchorTemplate>? templates = null) { _templates = templates ?? TemplateMatch.Load(); }

    /// <summary>The fixed-fraction crop of each region, as the readers take them today.</summary>
    public static Dictionary<string, Rectangle> FixedRegions(Rectangle game) => new()
    {
        ["balance"] = BalanceReader.Strip(game),
        ["chat"] = ChatLocator.Corner(game),
        ["loot"] = LootPopup.Region(game),
        ["craft"] = CraftPanel.Region(game),
        ["zone"] = ZoneLocator.Strip(game),
        ["target"] = TargetReader.Strip(game),
    };

    /// <summary>
    /// The Kinah counter strip's numbers as BalanceReader just read them (screen boxes): the anchor for the strip. Sure in
    /// proportion to how many of its four counters were read.
    /// </summary>
    public void ObserveBalance(Rectangle game, IReadOnlyList<Rectangle> numbers, DateTime at)
    {
        if (numbers.Count == 0) return;
        var u = numbers.Aggregate(Rectangle.Union);
        int pad = Math.Max(2, numbers.Max(n => n.Height) / 4);
        u.Inflate(pad, pad);
        u.Intersect(game);
        lock (_lock) _hits["kinah-strip"] = new Hit("kinah-strip", "balance", u, BalanceReader.Strip(game), Math.Min(1, numbers.Count / 4.0), at);
    }

    /// <summary>
    /// The chat box the tracker reads (found above the input row with its channel tab, or from its lines, or chosen or
    /// kept): the anchor for the chat, sure by how it was found. Its bottom-left is the point that matters (the chat grows up).
    /// </summary>
    public void ObserveChat(Rectangle game, Rectangle chat, string how, bool manual, DateTime at)
    {
        double conf = manual ? 0.5 : how.Contains("input box", StringComparison.OrdinalIgnoreCase) ? 1.0
            : how.Contains("from its lines", StringComparison.OrdinalIgnoreCase) ? 0.7 : 0.4;
        lock (_lock) _hits["chat-input"] = new Hit("chat-input", "chat", chat, ChatLocator.Corner(game), conf, at);
    }

    /// <summary>
    /// Whether the anchor <paramref name="name"/> ("kinah-strip", "chat-input", "tpl:…") was seen within
    /// <paramref name="seconds"/> of <paramref name="now"/>: the scene classifier's sign that the HUD is on screen.
    /// </summary>
    public bool SeenWithin(string name, DateTime now, double seconds)
    {
        lock (_lock)
        {
            if (!_hits.TryGetValue(name, out var h) || h.Conf <= 0) return false;
            double age = (now - h.At).TotalSeconds;
            return age >= -5 && age <= seconds;
        }
    }

    /// <summary>
    /// The anchored crop of each region (null: no anchor for it yet) and how sure, beside its fixed-fraction crop. A region
    /// follows its anchor by the anchor's own displacement from where the fixed fractions expect it; the chat and the strip
    /// are their anchors' boxes themselves (the chat aligned at its bottom-left).
    /// </summary>
    public Dictionary<string, (Rectangle Fixed, Rectangle? Anchored, double Conf, string From)> Layout(Rectangle game, DateTime now)
    {
        var fixedR = FixedRegions(game);
        var res = new Dictionary<string, (Rectangle, Rectangle?, double, string)>();
        lock (_lock)
            foreach (var (region, f) in fixedR)
            {
                var hit = _hits.Values.Where(h => h.Region == region && (now - h.At).TotalSeconds <= LostSeconds).OrderByDescending(h => h.Conf).FirstOrDefault();
                if (hit == null) { res[region] = (f, null, 0, ""); continue; }
                Rectangle a = hit.Name switch
                {
                    "kinah-strip" or "chat-input" => hit.Found,
                    _ => new Rectangle(f.X + hit.Found.X - hit.Expected.X, f.Y + hit.Found.Y - hit.Expected.Y, f.Width, f.Height),
                };
                res[region] = (f, a, hit.Conf, hit.Name);
            }
        return res;
    }

    /// <summary>The displacement logged for a region: top-left for most, bottom-left for the chat.</summary>
    public static (int Dx, int Dy) Delta(string region, Rectangle fixedR, Rectangle anchored) =>
        region == "chat" ? (anchored.Left - fixedR.Left, anchored.Bottom - fixedR.Bottom) : (anchored.Left - fixedR.Left, anchored.Top - fixedR.Top);

    /// <summary>
    /// Every <see cref="EverySeconds"/> s while the game is in view: template anchors are looked for (only when there are
    /// any; <paramref name="capture"/> copies a piece of the game's picture), the layout's state is judged, and once a
    /// minute one line per region goes to reads.log. Returns whether it ran.
    /// </summary>
    public bool Tick(Rectangle game, DateTime now, Func<Rectangle, Bitmap?> capture)
    {
        if ((now - _tickAt).TotalSeconds < EverySeconds && _tickAt != DateTime.MinValue) return false;
        _tickAt = now;
        if (_startedAt == DateTime.MinValue) _startedAt = now;
        var sw = System.Diagnostics.Stopwatch.StartNew();
        foreach (var t in _templates)
        {
            try { MatchTemplate(game, now, t, capture); }
            catch (Exception ex) { CrashLog.Write("anchor " + t.Name, ex); }
        }
        // the layout's state: an anchor seen lately, or not (only judged once the tracker has looked for a minute)
        bool any; lock (_lock) any = _hits.Values.Any(h => (now - h.At).TotalSeconds <= LostSeconds && h.Conf > 0);
        bool lost = !any && (now - _startedAt).TotalSeconds >= LostSeconds;
        if (lost != LayoutNotFound)
        {
            LayoutNotFound = lost;
            CrashLog.Note(lost
                ? $"anchor: layout not found (no Kinah counter numbers, no chat box, no template hit in {LostSeconds:0} s); reading goes on from the fixed places"
                : "anchor: layout found again");
        }
        sw.Stop(); _costMs += sw.Elapsed.TotalMilliseconds; _ticks++;
        if ((now - _logAt).TotalSeconds >= LogSeconds)
        {
            _logAt = now;
            foreach (var (region, (f, a, conf, from)) in Layout(game, now))
            {
                if (a is not Rectangle ar) { CrashLog.Note($"anchor {region} none (no anchor seen for it)"); continue; }
                var (dx, dy) = Delta(region, f, ar);
                CrashLog.Note($"anchor {region} dx={dx} dy={dy} conf={conf:0.00} (from {from}; size {ar.Width}x{ar.Height}, fixed {f.Width}x{f.Height})");
            }
            CrashLog.Note($"anchor: {_ticks} checks, {(_ticks > 0 ? _costMs / _ticks : 0):0.0} ms each, {_templates.Count} template(s){(LayoutNotFound ? ", layout not found" : "")}");
            _costMs = 0; _ticks = 0;
        }
        return true;
    }

    /// <summary>A template looked for around where its reference frame had it, at the game's scale.</summary>
    void MatchTemplate(Rectangle game, DateTime now, AnchorTemplate t, Func<Rectangle, Bitmap?> capture)
    {
        if (t.Picture == null) return;
        double k = game.Height / (double)Math.Max(1, t.RefHeight);
        var expected = new Rectangle(game.Left + (int)(t.Where.X * game.Width), game.Top + (int)(t.Where.Y * game.Height), Math.Max(4, (int)(t.Picture.Width * k)), Math.Max(4, (int)(t.Picture.Height * k)));
        int m = (int)(t.Search * game.Height);
        var search = Rectangle.Inflate(expected, m, m); search.Intersect(game);
        if (search.Width < expected.Width || search.Height < expected.Height) return;
        using var shot = capture(search);
        if (shot == null) return;
        // shrink so the template is at most about 24 px tall: the search stays a few milliseconds
        int shrink = Math.Max(1, expected.Height / 24);
        using var scaled = new Bitmap(t.Picture, expected.Width, expected.Height);
        var tg = TemplateMatch.Gray(scaled, new Rectangle(0, 0, scaled.Width, scaled.Height), shrink, out int tw, out int th);
        var ig = TemplateMatch.Gray(shot, new Rectangle(0, 0, shot.Width, shot.Height), shrink, out int iw, out int ih);
        var (at, score) = TemplateMatch.Find(ig, iw, ih, tg, tw, th);
        if (score < MinScore) return;
        var found = new Rectangle(search.Left + at.X * shrink, search.Top + at.Y * shrink, expected.Width, expected.Height);
        lock (_lock) _hits["tpl:" + t.Name] = new Hit("tpl:" + t.Name, t.Region, found, expected, score, now);
    }
}
