using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using KinahLedger.Data;
using KinahLedger.Session;

namespace KinahLedger.Ocr;

/// <summary>
/// parse-test's checks of the name decoder (Prompt 8): the lexicon search on hand-built probability matrices (always),
/// and the model on 30 crops drawn here (only when the model files are on this machine; without them the same cases
/// pass as "decoder off" and the tracker's own path without a model is checked instead).
/// </summary>
static class NameDecoderTests
{
    // the model's classes after the blank, as en_dict.txt lists them plus the space PP-OCR adds
    const string ModelChars = "0123456789:;<=>?@ABCDEFGHIJKLMNOPQRSTUVWXYZ[\\]^_`abcdefghijklmnopqrstuvwxyz{|}~!\"#$%&'()*+,-./  ";

    public static bool Run(ItemDictionary items, Settings settings)
    {
        bool ok = true;
        void Check(string what, string got, string expect)
        {
            bool pass = got == expect; ok &= pass;
            Console.WriteLine($"  {what,-58} -> {got,-46} {(pass ? "ok" : "MISMATCH, expected " + expect)}");
        }
        Console.WriteLine("\nName decoder: lexicon-constrained CTC search (Prompt 8, shadow only):");
        Check("the alphabet: Norm's characters, the blank, the mark", $"{DecodeAlphabet.Chars.Length + 1} {DecodeAlphabet.Mark}", "47 47");

        var lex = NameLexicon.Build(new[]
        {
            ("Iron Ingot", 1L, "Iron Ingot"), ("Silver Ore", 2L, "Silver Ore"), ("Silver Orb", 3L, "Silver Orb"),
            ("Odyle Shard", 4L, "Odyle Shard"), ("Raw Leather", 5L, "Raw Leather"), ("Wolf Pelt", 6L, "Wolf Pelt"),
        });
        string D(NameDecode r) => r.Kind switch
        {
            DecodeKind.Item => $"{r.Name}" + (r.Count != 1 ? $" x{r.Count}" : "") + (r.Bound ? " bound" : ""),
            DecodeKind.Other => "other",
            _ => "none",
        };

        // a confusable pair: each "I" read as "l" more than as "I"; the free reading is "lron lngot", not an item
        var ll = Spell("Iron Ingot", new() { [0] = ('l', 0.5, 'I', 0.4), [5] = ('l', 0.5, 'I', 0.4) });
        var r1 = CtcLexiconSearch.Decode(ll, lex);
        Check("l/I: \"lron lngot\" (l 0.5, I 0.4) is the in-list name", D(r1), "Iron Ingot");
        Check("l/I: the free reading was the misread", CtcLexiconSearch.Greedy(ll).Text, "lron lngot");
        Check("l/I: the in-list reading is within the margin", (r1.FreeLogP - r1.LogP < 1.5).ToString(), "True");
        Check("1/l: \"Si1ver Ore\" is Silver Ore", D(CtcLexiconSearch.Decode(Spell("Silver Ore", new() { [2] = ('1', 0.55, 'l', 0.35) }), lex)), "Silver Ore");
        Check("e/b at the end: the likelier letter names the item", D(CtcLexiconSearch.Decode(Spell("Silver Ore", new() { [9] = ('b', 0.6, 'e', 0.3) }), lex)), "Silver Orb");
        // a string no item has, read with confidence: other, with a low probability
        var oo = CtcLexiconSearch.Decode(Spell("Zzyzx Shard"), lex);
        Check("out of the list: \"Zzyzx Shard\" is other", D(oo), "other");
        Check("out of the list: its in-list p is low", (oo.P < 0.001).ToString(), "True");
        Check("out of the list: the model's own reading is kept for the log", oo.Free, "zzyzx shard");
        // one confident wrong letter in a real name is past the margin too (cut off, garbled): unsure, not snapped
        Check("a letter the model was sure of (0.99) wrong: \"Raw Leathex\" is other", D(CtcLexiconSearch.Decode(Spell("Raw Leathex", p: 0.99), lex)), "other");
        // the suffixes the loot list and the chat print after a name
        Check("a quantity suffix: \"Raw Leather x12\"", D(CtcLexiconSearch.Decode(Spell("Raw Leather x12"), lex)), "Raw Leather x12");
        Check("no space before the count: \"Wolf Peltx3\"", D(CtcLexiconSearch.Decode(Spell("Wolf Peltx3"), lex)), "Wolf Pelt x3");
        Check("\"Odyle Shard (Bound) x3.\"", D(CtcLexiconSearch.Decode(Spell("Odyle Shard (Bound) x3."), lex)), "Odyle Shard x3 bound");
        Check("a count with a comma: \"Kina x1,240\" (Kinah, id 0)", D(CtcLexiconSearch.Decode(Spell("Kina x1,240"), lex)) + " id " + CtcLexiconSearch.Decode(Spell("Kina x1,240"), lex).ItemId, "Kina x1240 id 0");
        Check("marks Norm drops: \"*Raw Leather x2|\"", D(CtcLexiconSearch.Decode(Spell("*Raw Leather x2|"), lex)), "Raw Leather x2");
        Check("a case-only slip: \"RAW leather\"", D(CtcLexiconSearch.Decode(Spell("RAW leather"), lex)), "Raw Leather");
        Check("a letter held for two steps is one letter (\"Wolff\" is not read)", D(CtcLexiconSearch.Decode(Spell("Wolf Pelt", hold: 3), lex)), "Wolf Pelt");
        Check("an empty matrix returns none", D(CtcLexiconSearch.Decode(Array.Empty<double[]>(), lex)), "none");
        Check("blanks only (no text in the crop) returns none", D(CtcLexiconSearch.Decode(Enumerable.Range(0, 20).Select(_ => Frame(null)).ToArray(), lex)), "none");
        Check("a name cut short (\"Raw Leat\") is other", D(CtcLexiconSearch.Decode(Spell("Raw Leat"), lex)), "other");

        // the log line and the tally for each outcome
        Check("log: agree", NameDecoder.Outcome(r1, "lron lngot", 1, out var t1) + " / " + t1, "decode Iron Ingot p=" + r1.P.ToString("0.00") + " vs ocr lron lngot agree / decode_agree");
        Check("log: disagree", NameDecoder.Outcome(r1, "Iron Ingoat", null, out var t2).EndsWith("disagree") + " / " + t2, "True / decode_disagree");
        Check("log: other", NameDecoder.Outcome(oo, "Zzyzx Shard", null, out var t3).StartsWith("decode other p=0.00 vs ocr Zzyzx Shard other") + " / " + t3, "True / decode_other");
        Check("log: nothing to read, no tally", (NameDecoder.Outcome(NameDecode.Nothing, "", null, out var t4) == "" && t4 == null).ToString(), "True");
        Check("chat lines shadowed: pickups only", string.Join(",", new[] { "Acquired Raw Leather x2.", "Orichalcum Ore acquired.", "Acquired 2,790 Kina.", "Yarie acquired Soul: Ursus (Bound) through Force", "[Party] Bob: hi" }.Select(NameDecoder.PickupLine)), "True,True,False,False,False");

        // the lexicon from the tracker's own item list
        var sw = Stopwatch.StartNew();
        var real = NameLexicon.From(items);
        double buildMs = sw.Elapsed.TotalMilliseconds;
        Console.WriteLine($"  (lexicon from the item list: {real.Names:N0} names, {real.Nodes:N0} trie nodes, built in {buildMs:0} ms)");
        Check("the item list's lexicon holds the game's names", (real.Names > 3000).ToString(), "True");
        var rl = CtcLexiconSearch.Decode(Spell("Orichalcum Ore x2", new() { [9] = ('n', 0.5, 'm', 0.4) }), real);
        Check("full list: \"Orichalcun\" (m read as n, the model torn) settles", D(rl) + (rl.ItemId == items.IdOf("Orichalcum Ore") ? " (its id)" : " (wrong id)"), "Orichalcum Ore x2 (its id)");
        // the search's own cost on the full list, a long name
        sw.Restart();
        for (int i = 0; i < 20; i++) CtcLexiconSearch.Decode(Spell("Elite Guardian High Commander Greatsword"), real);
        Console.WriteLine($"  (search on the full list, a 40-letter name: {sw.Elapsed.TotalMilliseconds / 20:0.00} ms)");

        ok &= ModelChecks(items, settings, Check);
        return ok;
    }

    /// <summary>The model on drawn crops, or the path without a model; both pass without a model on the machine.</summary>
    static bool ModelChecks(ItemDictionary items, Settings settings, Action<string, string, string> check)
    {
        bool ok = true;
        void Check(string what, string got, string expect) { ok &= got == expect; check(what, got, expect); }
        // the path every player without the model files takes: off, logged once, crops dropped, nothing tallied
        NameDecoder.ForceAbsent = true;
        try
        {
            NameDecoder.Start(items);
            Check("without the model: the decoder is off", NameDecoder.State, "off (no model)");
            var before = Accuracy.Shadows().Values.Sum();
            using var blank = new Bitmap(40, 12);
            NameDecoder.Shadow("list", (Bitmap)blank.Clone(), "Raw Leather x1", 1);
            Check("without the model: a shadow crop is dropped, nothing tallied", (Accuracy.Shadows().Values.Sum() - before).ToString(), "0");
            Check("without the model: a decode returns nothing", (NameDecoder.Decode(blank) == null).ToString(), "True");
        }
        finally { NameDecoder.ForceAbsent = false; NameDecoder.ResetForTests(); }

        NameDecoder.Start(items);
        if (!NameDecoder.Present)
        {
            Console.WriteLine("  decoder off (no model): the 30 drawn crops are skipped (node tools/data/models.js --out data/models fetches it)");
            return ok;
        }

        var sw = Stopwatch.StartNew();
        bool loaded = NameDecoder.EnsureLoaded();
        double cold = sw.Elapsed.TotalMilliseconds;
        Check("the model loads", loaded + " " + NameDecoder.State, "True on");
        if (!loaded) return ok;
        Console.WriteLine($"  (cold start {cold:0} ms: files checked, model loaded, lexicon built; budget 300 ms)");

        string[] names = { "Raw Leather", "Orichalcum Ore", "Fresh Fruit", "Spiritstone", "Ruby Gemstone", "Fine Odyle", "Kukuru", "Destruction Spellbook", "Spectral Guard", "Yggdrasil Log" };
        string[] suffix = { " x3", " x12", "", "", " x1", " x2", "", "", " x5", "" };
        Color[] ink = { Color.FromArgb(235, 235, 235), Color.FromArgb(120, 220, 110), Color.FromArgb(110, 170, 255), Color.FromArgb(240, 200, 90) };
        int[] heights = { 13, 19, 28 };
        int right = 0, wrong = 0, other = 0, n = 0;
        var times = new List<double>(); var modelMs = new List<double>(); var searchMs = new List<double>();
        foreach (var px in heights)
            for (int i = 0; i < names.Length; i++)
            {
                var text = names[i] + suffix[i];
                using var crop = Render(text, px, ink[(i + px) % ink.Length]);
                sw.Restart();
                var r = NameDecoder.Decode(crop)!;
                times.Add(sw.Elapsed.TotalMilliseconds); modelMs.Add(NameDecoder.LastModelMs); searchMs.Add(NameDecoder.LastSearchMs);
                n++;
                long want = items.IdOf(names[i]);
                string verdict = r.Kind == DecodeKind.Item && r.ItemId == want ? "right" : r.Kind == DecodeKind.Item ? "WRONG ITEM" : r.Kind == DecodeKind.Other ? "other" : "none";
                if (verdict == "right") right++; else if (verdict == "WRONG ITEM") wrong++; else other++;
                Console.WriteLine($"    {px,2} px  {text,-26} -> {(r.Kind == DecodeKind.Item ? r.Name + (r.Count != 1 ? " x" + r.Count : "") : r.Kind.ToString().ToLowerInvariant()),-26} p={r.P:0.00}  model read \"{r.Free}\"  {verdict}");
            }
        times.RemoveAt(0); modelMs.RemoveAt(0); searchMs.RemoveAt(0);   // the first run warms the model up
        Console.WriteLine($"  (per line: {times.Average():0.0} ms on average, {times.Max():0.0} ms longest (model {modelMs.Average():0.0}, search {searchMs.Average():0.0}), {right}/{n} right, {other} other, {wrong} wrong item; budget 5 ms)");
        // steady state: one typical crop decoded 40 times (the median says more than an average with the game running)
        using (var typical = Render("Orichalcum Ore x12", 19, ink[0]))
        {
            var ms = new List<double>(); var mm = new List<double>();
            for (int i = 0; i < 40; i++) { sw.Restart(); NameDecoder.Decode(typical); ms.Add(sw.Elapsed.TotalMilliseconds); mm.Add(NameDecoder.LastModelMs); }
            ms.Sort(); mm.Sort();
            Console.WriteLine($"  (one typical row 40 times: median {ms[20]:0.0} ms, model {mm[20]:0.0} ms, fastest {ms[0]:0.0} ms)");
        }
        Check("drawn crops: at least 27 of 30 name the right item", (right >= 27).ToString(), "True");
        Check("drawn crops: none names a wrong item", wrong.ToString(), "0");
        // strings no item has, drawn the same way: never an item
        int ool = 0;
        foreach (var s in new[] { "Zephyr Quartz Bracelet", "Blorpington Widget x4", "Lunar Pineapple Crate" })
        {
            using var crop = Render(s, 19, ink[0]);
            var r = NameDecoder.Decode(crop)!;
            Console.WriteLine($"    19 px  {s,-26} -> {(r.Kind == DecodeKind.Item ? r.Name : r.Kind.ToString().ToLowerInvariant()),-26} p={r.P:0.00}  model read \"{r.Free}\"");
            if (r.Kind != DecodeKind.Item) ool++;
        }
        Check("drawn names no item has: all three other", ool.ToString(), "3");
        // generous ceilings (CI machines are slower than a desktop): the real numbers are printed above and logged once
        Check("cold start well inside a second", (cold < 1000).ToString(), "True");
        Check("time per line under 200 ms (a shared CI runner is slow; the 5 ms target is logged above and gates promotion, not the build)", (times.Average() < 200).ToString(), "True");

        // the shadow where the tracker reads: a loot list picture through the session's own path, and a chat line's name span
        try
        {
            var shadows0 = Accuracy.Shadows();
            using var list = new Bitmap(420, 90, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(list))
            {
                g.Clear(Color.FromArgb(22, 24, 30));
                g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
                using var font = new Font("Segoe UI", 22, FontStyle.Regular, GraphicsUnit.Pixel);
                using var white = new SolidBrush(Color.FromArgb(235, 235, 235));
                g.DrawString("Raw Leather x3", font, white, 40, 10);
                g.DrawString("Kina x404", font, white, 40, 50);
            }
            var session = new TrackerSession(null, settings, items, "Test");
            Task.Run(() => session.ReplayListFrameAsync(list, new Point(0, 0), DateTime.UtcNow)).GetAwaiter().GetResult();
            long Got(string k) => Accuracy.Shadows().GetValueOrDefault(k) - shadows0.GetValueOrDefault(k);
            var until = DateTime.UtcNow.AddSeconds(10);
            while (Got("decode_agree") + Got("decode_disagree") + Got("decode_other") == 0 && DateTime.UtcNow < until) Thread.Sleep(50);
            Check("loot list: the item row decoded beside OCR, and agreed", $"agree {Got("decode_agree")} disagree {Got("decode_disagree")} other {Got("decode_other")}", "agree 1 disagree 0 other 0");
            Check("loot list: the shadow counted nothing", session.Engine.ItemRows().Count + " items, " + session.Engine.Totals().Kinah + " Kinah", "0 items, 0 Kinah");

            using var chat = new Bitmap(520, 40, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(chat))
            {
                g.Clear(Color.FromArgb(22, 24, 30));
                g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
                using var font = new Font("Segoe UI", 20, FontStyle.Regular, GraphicsUnit.Pixel);
                using var green = new SolidBrush(Color.FromArgb(120, 220, 110));
                g.DrawString("Acquired Orichalcum Ore x2.", font, green, 6, 6);
            }
            var spans = new List<(string Line, string Name, Rectangle Box)>();
            var reader = new ScreenReader(new Rectangle(0, 0, chat.Width, chat.Height), 2) { NameSpans = (_, sp) => spans.AddRange(sp) };
            Task.Run(() => reader.ReadCaptureAsync(chat)).GetAwaiter().GetResult();
            var span = spans.FirstOrDefault(x => NameDecoder.PickupLine(x.Line));
            string chatGot = "no pickup line read";
            if (span.Line != null)
            {
                using var crop = NameDecoder.Crop(chat, span.Box);
                var r = crop == null ? null : NameDecoder.Decode(crop);
                chatGot = r is { Kind: DecodeKind.Item } ? r.Name : r?.Kind.ToString() ?? "no crop";
            }
            Check("chat: the pickup line's name span decodes to its item", chatGot, "Orichalcum Ore");
        }
        catch (Exception ex) { Check("the shadow paths ran", ex.GetType().Name + ": " + ex.Message, "no exception"); }
        return ok;
    }

    // ---- hand-built matrices ----

    /// <summary>One step over the model's classes: <paramref name="c"/> at 0.9 (null: the blank at 0.9), the rest spread evenly; folded to the decoder's classes.</summary>
    static double[] Frame(char? c, char? alt = null, double pc = 0.9, double palt = 0)
    {
        int classes = ModelChars.Length + 1;
        var p = new float[classes];
        double rest = (1 - pc - palt) / (classes - (alt == null ? 1 : 2));
        for (int i = 0; i < classes; i++) p[i] = (float)rest;
        int Idx(char ch) => ModelChars.IndexOf(ch) + 1;
        p[c == null ? 0 : Idx(c.Value)] = (float)pc;
        if (alt != null) p[Idx(alt.Value)] = (float)palt;
        return DecodeAlphabet.FromModel(p, 1, classes, ModelChars)[0];
    }

    /// <summary>
    /// A text spelled one letter per step with a blank after each; <paramref name="slips"/> puts two letters at one place
    /// (index into the text: the first at its probability, the second at its). <paramref name="hold"/> holds that letter for two steps.
    /// </summary>
    static double[][] Spell(string text, Dictionary<int, (char A, double Pa, char B, double Pb)>? slips = null, int hold = -1, double p = 0.9)
    {
        var steps = new List<double[]> { Frame(null) };
        for (int i = 0; i < text.Length; i++)
        {
            var f = slips != null && slips.TryGetValue(i, out var s) ? Frame(s.A, s.B, s.Pa, s.Pb) : Frame(text[i], null, p);
            steps.Add(f);
            if (i == hold) steps.Add(f);
            steps.Add(Frame(null));
        }
        return steps.ToArray();
    }

    // ---- drawn crops ----

    /// <summary>A loot-list-like crop: the text in a system font on a dark field, text <paramref name="px"/> high, softened by a mild blur.</summary>
    static Bitmap Render(string text, int px, Color ink)
    {
        using var font = new Font("Segoe UI", px, FontStyle.Regular, GraphicsUnit.Pixel);
        Size size;
        using (var probe = new Bitmap(1, 1)) using (var g0 = Graphics.FromImage(probe)) size = Size.Ceiling(g0.MeasureString(text, font));
        int pad = Math.Max(3, px / 3);
        var bmp = new Bitmap(size.Width + 2 * pad, size.Height + pad, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(bmp))
        {
            g.Clear(Color.FromArgb(22, 24, 30));
            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            g.SmoothingMode = SmoothingMode.HighQuality;
            using var brush = new SolidBrush(ink);
            g.DrawString(text, font, brush, pad, pad / 2f);
        }
        using (bmp) return BoxBlur(bmp);
    }

    /// <summary>A 3x3 box blur: the softness of a scaled game frame.</summary>
    static unsafe Bitmap BoxBlur(Bitmap src)
    {
        var dst = new Bitmap(src.Width, src.Height, PixelFormat.Format32bppArgb);
        var r = new Rectangle(0, 0, src.Width, src.Height);
        var s = src.LockBits(r, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        var d = dst.LockBits(r, ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
        try
        {
            for (int y = 0; y < src.Height; y++)
                for (int x = 0; x < src.Width; x++)
                {
                    int b = 0, g = 0, rr = 0, n = 0;
                    for (int dy = -1; dy <= 1; dy++)
                        for (int dx = -1; dx <= 1; dx++)
                        {
                            int yy = y + dy, xx = x + dx;
                            if (yy < 0 || xx < 0 || yy >= src.Height || xx >= src.Width) continue;
                            byte* p = (byte*)s.Scan0 + yy * s.Stride + xx * 4;
                            b += p[0]; g += p[1]; rr += p[2]; n++;
                        }
                    byte* q = (byte*)d.Scan0 + y * d.Stride + x * 4;
                    q[0] = (byte)(b / n); q[1] = (byte)(g / n); q[2] = (byte)(rr / n); q[3] = 255;
                }
        }
        finally { src.UnlockBits(s); dst.UnlockBits(d); }
        return dst;
    }
}
