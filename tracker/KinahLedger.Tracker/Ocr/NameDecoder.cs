using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Threading.Channels;
using KinahLedger.Data;
using KinahLedger.Session;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;

namespace KinahLedger.Ocr;

/// <summary>What the decoder made of one crop: an item (or Kinah, id 0), "other", or nothing to read.</summary>
public enum DecodeKind { None, Item, Other }

/// <summary>
/// One decode. <see cref="LogP"/> is the log-probability of the best reading inside the item list (names plus the loot
/// list's suffixes), summed over every way the model could have spelled it; <see cref="FreeLogP"/> is the model's own best
/// reading with no list at all (<see cref="Free"/>, as normalised text). <see cref="Name"/> is the item's own name for an
/// Item, the nearest in-list name (when there was one) for Other. <see cref="Count"/> is the " x12" suffix (1 without one).
/// </summary>
public sealed record NameDecode(DecodeKind Kind, long? ItemId, string Name, double LogP, double FreeLogP, string Free, int Count, bool Bound)
{
    public double P => double.IsNegativeInfinity(LogP) ? 0 : Math.Exp(LogP);
    public static readonly NameDecode Nothing = new(DecodeKind.None, null, "", double.NegativeInfinity, double.NegativeInfinity, "", 0, false);
}

/// <summary>
/// The characters the decoder works in: exactly what <see cref="ItemDictionary.Norm"/> keeps (lower case letters, digits,
/// space and ' ( ) : + . , &amp; -), plus the CTC blank and one "mark" class that stands for every other character the
/// model can print ("[", "]", "*", "|", "!" ...), which Norm turns into a space and drops.
/// </summary>
public static class DecodeAlphabet
{
    public const string Chars = "abcdefghijklmnopqrstuvwxyz0123456789 '():+.,&-";
    public const int Blank = 0;
    /// <summary>Chars.Length + 1 (46 characters; parse-test checks the sum).</summary>
    public const int Mark = 47;
    public const int K = Mark + 1;

    /// <summary>The class of a character already normalised (lower case), or -1 when Norm would not keep it.</summary>
    public static int Of(char c) { int i = Chars.IndexOf(c); return i < 0 ? -1 : i + 1; }

    /// <summary>The class a model character falls in: its lower case form, or the mark class.</summary>
    public static int OfModel(char c) { int i = Chars.IndexOf(char.ToLowerInvariant(c)); return i < 0 ? Mark : i + 1; }

    public static char CharOf(int k) => k == Blank ? '_' : k == Mark ? '~' : Chars[k - 1];

    /// <summary>
    /// The model's per-step probabilities over its own classes (index 0 the blank, then <paramref name="classChars"/>)
    /// folded into log-probabilities over <see cref="K"/> classes: "A" and "a" add up, every other mark adds into Mark.
    /// When a row doesn't sum to about 1 the model gave raw scores, and a softmax is taken first.
    /// </summary>
    public static double[][] FromModel(ReadOnlySpan<float> probs, int steps, int classes, string classChars)
    {
        var map = new int[classes];
        map[0] = Blank;
        for (int c = 1; c < classes; c++) map[c] = c - 1 < classChars.Length ? OfModel(classChars[c - 1]) : Mark;
        var lp = new double[steps][];
        var row = new double[classes];
        for (int t = 0; t < steps; t++)
        {
            double sum = 0, max = double.NegativeInfinity;
            for (int c = 0; c < classes; c++) { row[c] = probs[t * classes + c]; sum += row[c]; if (row[c] > max) max = row[c]; }
            if (Math.Abs(sum - 1) > 0.05 || row.Any(v => v < 0))
            {
                double z = 0;
                for (int c = 0; c < classes; c++) { row[c] = Math.Exp(row[c] - max); z += row[c]; }
                for (int c = 0; c < classes; c++) row[c] /= z;
            }
            var acc = new double[K];
            for (int c = 0; c < classes; c++) acc[map[c]] += row[c];
            var l = new double[K];
            for (int k = 0; k < K; k++) l[k] = Math.Log(Math.Max(acc[k], 1e-12));
            lp[t] = l;
        }
        return lp;
    }
}

/// <summary>
/// Every item name the tracker knows (<see cref="ItemDictionary.Lexicon"/>, normalised as Norm does) as a trie over
/// <see cref="DecodeAlphabet"/>, plus "kina"/"kinah" for the Kinah rows (id <see cref="KinaId"/>).
/// </summary>
public sealed class NameLexicon
{
    public const long KinaId = 0;
    readonly int[] _edgeStart;   // the edges of node n are [_edgeStart[n], _edgeStart[n + 1])
    readonly byte[] _edgeCls;
    readonly int[] _edgeTo;
    readonly long[] _item;
    readonly bool[] _terminal;
    readonly Dictionary<long, string> _names;

    public int Names { get; }
    public int Nodes => _terminal.Length;
    public string NameOf(long id) => _names.TryGetValue(id, out var n) ? n : $"item {id}";

    NameLexicon(int[] edgeStart, byte[] edgeCls, int[] edgeTo, long[] item, bool[] terminal, Dictionary<long, string> names, int count)
    { _edgeStart = edgeStart; _edgeCls = edgeCls; _edgeTo = edgeTo; _item = item; _terminal = terminal; _names = names; Names = count; }

    public static NameLexicon From(ItemDictionary items) => Build(items.Lexicon());

    /// <summary>The trie of these names (keys normalised already; the first id per key wins, as in the dictionary).</summary>
    public static NameLexicon Build(IEnumerable<(string Key, long Id, string Name)> names, bool withKina = true)
    {
        var kids = new List<Dictionary<byte, int>> { new() };
        var item = new List<long> { 0 };
        var term = new List<bool> { false };
        var display = new Dictionary<long, string>();
        int count = 0;
        void Add(string key, long id, string name)
        {
            if (key.Length == 0 || key.Any(c => DecodeAlphabet.Of(c) < 0)) return;
            int n = 0;
            foreach (var ch in key)
            {
                var cls = (byte)DecodeAlphabet.Of(ch);
                if (!kids[n].TryGetValue(cls, out var next))
                {
                    next = kids.Count; kids.Add(new()); item.Add(0); term.Add(false);
                    kids[n][cls] = next;
                }
                n = next;
            }
            if (term[n]) return;
            term[n] = true; item[n] = id; count++;
            display.TryAdd(id, name);
        }
        foreach (var (key, id, name) in names) Add(ItemDictionary.Norm(key), id, name);
        if (withKina) { Add("kina", KinaId, "Kina"); Add("kinah", KinaId, "Kina"); }
        var start = new int[kids.Count + 1];
        for (int i = 0; i < kids.Count; i++) start[i + 1] = start[i] + kids[i].Count;
        var cls2 = new byte[start[^1]];
        var to = new int[start[^1]];
        for (int i = 0; i < kids.Count; i++)
        {
            int j = start[i];
            foreach (var kv in kids[i].OrderBy(kv => kv.Key)) { cls2[j] = kv.Key; to[j] = kv.Value; j++; }
        }
        return new NameLexicon(start, cls2, to, item.ToArray(), term.ToArray(), display, count);
    }

    internal bool Terminal(int node) => _terminal[node];
    internal long ItemAt(int node) => _item[node];
    internal int EdgeFrom(int node) => _edgeStart[node];
    internal int EdgeTo(int node) => _edgeStart[node + 1];
    internal int EdgeClass(int e) => _edgeCls[e];
    internal int EdgeChild(int e) => _edgeTo[e];
}

/// <summary>
/// CTC prefix beam search held to the lexicon (the trie) followed by what the loot list and the chat print after a name:
/// an optional " (Bound)", an optional " x12" (or "x12"), an optional closing ".". A mark (any character Norm drops) may
/// come anywhere and changes nothing. The answer is the in-list reading with the highest probability, summed over every
/// alignment and every spelling that normalises to it.
/// </summary>
public static class CtcLexiconSearch
{
    /// <summary>
    /// How far below the model's own best reading the best in-list reading may fall and still name an item, in nats:
    /// ln(1000), a factor of a thousand. A clean crop of a real name scores the same both ways (0 apart); an l/I or O/0
    /// slip the model was torn on costs well under one nat; one character the model was sure of (99.9% against 0.1%)
    /// costs about 6.9 on its own. So "other" means the model read, with confidence, at least one character no item name
    /// has there, or a crop of something that isn't an item name at all: the garbled and cut-off cases, which stay
    /// "unsure" rather than being snapped to the nearest name. Tuned on labeled rows before any promotion.
    /// </summary>
    public const double OtherMargin = 6.907755;   // ln(1000)

    /// <summary>Hypotheses kept per step. 32 holds every confusable spelling of a name in tests; more costs time for nothing.</summary>
    public const int BeamWidth = 32;

    /// <summary>An extension by a character the model gave less than e^-14 (about one in a million) at a step is not tried.</summary>
    const double Floor = -14;
    /// <summary>A hypothesis this far (in nats) below the step's best is dropped even when the beam has room.</summary>
    const double Gap = 18;

    // the suffix after a name: states, which accept, and their transitions (normalised characters)
    const int SA = 0, SB = 1, SP1 = 2, SP2 = 3, SP3 = 4, SP4 = 5, SP5 = 6, SP6 = 7, SC = 8, SD = 9, SX = 10, SX2 = 11, SN = 12, SE = 13;
    const int MaxDigits = 7;
    static bool Accepting(int s) => s is SA or SB or SC or SD or SN or SE;

    static int Suffix(int s, char c, int digits) => s switch
    {
        SA => c == ' ' ? SB : c == 'x' ? SX : c == '(' ? SP1 : c == '.' ? SE : -1,
        SB => c == 'x' ? SX : c == '(' ? SP1 : c == '.' ? SE : -1,
        SP1 => c == 'b' ? SP2 : -1,
        SP2 => c == 'o' ? SP3 : -1,
        SP3 => c == 'u' ? SP4 : -1,
        SP4 => c == 'n' ? SP5 : -1,
        SP5 => c == 'd' ? SP6 : -1,
        SP6 => c == ')' ? SC : -1,
        SC => c == ' ' ? SD : c == 'x' ? SX : c == '.' ? SE : -1,
        SD => c == 'x' ? SX : -1,
        SX => c == ' ' ? SX2 : char.IsAsciiDigit(c) ? SN : -1,
        SX2 => char.IsAsciiDigit(c) ? SN : -1,
        SN => (char.IsAsciiDigit(c) || c == ',') && digits < MaxDigits ? SN : c == '.' ? SE : -1,
        _ => -1,
    };

    // the characters any suffix state can take, tried in this order
    static readonly char[] SuffixChars = " x(bound).,0123456789".Distinct().ToArray();

    /// <summary>A hypothesis: a trie node (Suf &lt; 0) or a suffix state with the item it closed and the suffix so far; Last is the prefix's last class (CTC needs it to tell a repeat from a new letter).</summary>
    readonly record struct Key(int Node, int Suf, long Item, string Tail, int Last);

    static double Lse(double a, double b)
    {
        if (double.IsNegativeInfinity(a)) return b;
        if (double.IsNegativeInfinity(b)) return a;
        return a > b ? a + Math.Log(1 + Math.Exp(b - a)) : b + Math.Log(1 + Math.Exp(a - b));
    }

    /// <summary>
    /// The best in-list reading of <paramref name="lp"/> (log-probabilities, steps x <see cref="DecodeAlphabet.K"/>), and
    /// "other" when it falls more than <paramref name="margin"/> below the model's own best reading. None for an empty
    /// matrix or a crop the model reads as empty.
    /// </summary>
    public static NameDecode Decode(double[][] lp, NameLexicon lex, double margin = OtherMargin, int beam = BeamWidth)
    {
        if (lp.Length == 0) return NameDecode.Nothing;
        var (freeLabels, freeText) = Greedy(lp);
        if (freeLabels.Length == 0) return NameDecode.Nothing;
        double free = Forward(lp, freeLabels);

        double NegInf = double.NegativeInfinity;
        var cur = new Dictionary<Key, (double B, double Nb)>(beam * 2) { [new Key(0, -1, 0, "", 0)] = (0, NegInf) };
        var next = new Dictionary<Key, (double B, double Nb)>(beam * 16);
        var rank = new List<(double Score, Key K)>(beam * 16);
        foreach (var row in lp)
        {
            next.Clear();
            void Add(Key k, double b, double nb)
            {
                if (next.TryGetValue(k, out var v)) next[k] = (Lse(v.B, b), Lse(v.Nb, nb));
                else next[k] = (b, nb);
            }
            foreach (var (k, (pb, pnb)) in cur)
            {
                double tot = Lse(pb, pnb);
                // a blank: the same prefix, now ending in a blank
                Add(k, tot + row[DecodeAlphabet.Blank], NegInf);
                // the last letter held for another step: the same prefix
                if (k.Last != 0 && !double.IsNegativeInfinity(pnb)) Add(k, NegInf, pnb + row[k.Last]);
                void Extend(Key to, int cls)
                {
                    double l = row[cls];
                    if (l < Floor) return;
                    // the same letter twice needs a blank between (CTC)
                    double src = cls == k.Last ? pb : tot;
                    if (!double.IsNegativeInfinity(src)) Add(to, NegInf, src + l);
                }
                // a mark changes nothing but the last class
                Extend(k with { Last = DecodeAlphabet.Mark }, DecodeAlphabet.Mark);
                if (k.Suf < 0)
                {
                    for (int e = lex.EdgeFrom(k.Node); e < lex.EdgeTo(k.Node); e++)
                    {
                        int cls = lex.EdgeClass(e);
                        Extend(new Key(lex.EdgeChild(e), -1, 0, "", cls), cls);
                    }
                    if (lex.Terminal(k.Node)) ExtendSuffix(SA, lex.ItemAt(k.Node), "");
                }
                else ExtendSuffix(k.Suf, k.Item, k.Tail);

                void ExtendSuffix(int s, long item, string tail)
                {
                    int digits = tail.Length - Math.Max(tail.LastIndexOf('x') + 1, 0);
                    foreach (var c in SuffixChars)
                    {
                        int to = Suffix(s, c, digits);
                        if (to < 0) continue;
                        int cls = DecodeAlphabet.Of(c);
                        Extend(new Key(-1, to, item, tail + c, cls), cls);
                    }
                }
            }
            // the beam: the best hypotheses, none far below the best
            rank.Clear();
            double top = NegInf;
            foreach (var (k, v) in next) { double sc = Lse(v.B, v.Nb); rank.Add((sc, k)); if (sc > top) top = sc; }
            if (rank.Count > beam) rank.Sort((a, b) => b.Score.CompareTo(a.Score));
            cur.Clear();
            for (int i = 0; i < rank.Count && cur.Count < beam; i++)
                if (rank[i].Score >= top - Gap) cur[rank[i].K] = next[rank[i].K];
        }

        // the accepting hypotheses, summed per (item, suffix): the spellings and marks that normalise to the same reading
        var groups = new Dictionary<(long Item, string Tail), double>();
        foreach (var (k, (pb, pnb)) in cur)
        {
            long item; string tail;
            if (k.Suf < 0) { if (!lex.Terminal(k.Node)) continue; item = lex.ItemAt(k.Node); tail = ""; }
            else { if (!Accepting(k.Suf)) continue; item = k.Item; tail = k.Tail; }
            var g = (item, tail);
            groups[g] = groups.TryGetValue(g, out var v) ? Lse(v, Lse(pb, pnb)) : Lse(pb, pnb);
        }
        if (groups.Count == 0) return new NameDecode(DecodeKind.Other, null, "", NegInf, free, freeText, 0, false);
        // the best reading; of two that score the same, the one without a suffix (a name that ends in "(Bound)" is its own item)
        var best = groups.OrderByDescending(g => g.Value).ThenBy(g => g.Key.Tail.Length).First();
        double lexP = best.Value;
        free = Math.Max(free, lexP);
        var (bid, btail) = best.Key;
        int count = 1;
        int xi = btail.LastIndexOf('x');
        if (xi >= 0 && int.TryParse(new string(btail[(xi + 1)..].Where(char.IsAsciiDigit).ToArray()), out var n) && n > 0) count = n;
        bool bound = btail.Contains("(bound)");
        var kind = lexP < free - margin ? DecodeKind.Other : DecodeKind.Item;
        return new NameDecode(kind, kind == DecodeKind.Item ? bid : null, lex.NameOf(bid), lexP, free, freeText, count, bound);
    }

    /// <summary>The model's own best path, collapsed (repeats merged, blanks out): its labels and its text.</summary>
    public static (int[] Labels, string Text) Greedy(double[][] lp)
    {
        var labels = new List<int>();
        int prev = -1;
        foreach (var row in lp)
        {
            int arg = 0;
            for (int k = 1; k < row.Length; k++) if (row[k] > row[arg]) arg = k;
            if (arg != prev && arg != DecodeAlphabet.Blank) labels.Add(arg);
            prev = arg;
        }
        return (labels.ToArray(), new string(labels.Select(DecodeAlphabet.CharOf).ToArray()));
    }

    /// <summary>The CTC forward algorithm: the log-probability of one labelling over every alignment.</summary>
    public static double Forward(double[][] lp, int[] labels)
    {
        int s = 2 * labels.Length + 1;
        var a = new double[s];
        Array.Fill(a, double.NegativeInfinity);
        a[0] = lp[0][DecodeAlphabet.Blank];
        if (s > 1) a[1] = lp[0][labels[0]];
        var b = new double[s];
        for (int t = 1; t < lp.Length; t++)
        {
            for (int i = 0; i < s; i++)
            {
                int cls = i % 2 == 0 ? DecodeAlphabet.Blank : labels[i / 2];
                double v = a[i];
                if (i > 0) v = Lse(v, a[i - 1]);
                if (i > 1 && i % 2 == 1 && labels[i / 2] != labels[i / 2 - 1]) v = Lse(v, a[i - 2]);
                b[i] = v + lp[t][cls];
            }
            (a, b) = (b, a);
        }
        return s > 1 ? Lse(a[s - 1], a[s - 2]) : a[0];
    }
}

/// <summary>
/// The open text recogniser: PaddleOCR's en_PP-OCRv4 mobile recognition model (a CRNN-style network with CTC output,
/// about 7.7 MB) run through ONNX Runtime on the CPU. A crop is scaled to 48 px high, normalised as PaddleOCR does
/// (BGR, (x / 255 - 0.5) / 0.5) and padded to at least <see cref="MinWidth"/> px wide; the model returns a probability per character class
/// for each 8 px column.
/// </summary>
public sealed class RecModel : IDisposable
{
    /// <summary>PaddleOCR pads every line to 320 px; a short name padded only to 96 reads the same on parse-test's crops and costs a third less.</summary>
    public const int Height = 48, MinWidth = 96, MaxWidth = 1280;
    readonly InferenceSession _session;
    readonly string _input;
    readonly string _classChars;
    readonly object _lock = new();

    RecModel(InferenceSession s, string input, string classChars) { _session = s; _input = input; _classChars = classChars; }

    /// <summary>Loads the model and its character list; throws when the two don't fit together.</summary>
    public static RecModel Load(string onnx, string dict)
    {
        var lines = File.ReadAllText(dict).Replace("\r", "").Split('\n').ToList();
        if (lines.Count > 0 && lines[^1].Length == 0) lines.RemoveAt(lines.Count - 1);
        var chars = string.Concat(lines.Select(l => l.Length > 0 ? l[0] : ' '));
        var opts = new SessionOptions
        {
            // one thread: the decoder runs beside the game and the other readers. A line takes about 15 ms on one core of a
            // Ryzen 9 7950X3D with the game running (two threads: about 11); it is a shadow, so it stays out of the way
            IntraOpNumThreads = 1, InterOpNumThreads = 1, ExecutionMode = ExecutionMode.ORT_SEQUENTIAL,
            GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL,
        };
        // tiny values flushed to zero: a little faster on the CPU, no change in what is read (parse-test's crops)
        opts.AddSessionConfigEntry("session.set_denormal_as_zero", "1");
        var s = new InferenceSession(onnx, opts);
        var input = s.InputMetadata.Keys.First();
        var m = new RecModel(s, input, chars);
        // the class count: blank + the list, + one space when the model was trained with use_space_char (PP-OCR's default);
        // read from the output's shape when it is fixed there, else from one run on a blank picture
        var outDims = s.OutputMetadata.First().Value.Dimensions;
        int classes = outDims.Length > 0 && outDims[^1] > 0 ? outDims[^1] : m.RunRaw(new float[3 * Height * MinWidth], MinWidth).Classes;
        if (classes == chars.Length + 2) m = new RecModel(s, input, chars + " ");
        else if (classes != chars.Length + 1) { s.Dispose(); throw new InvalidDataException($"the model has {classes} classes, its character list {chars.Length}"); }
        return m;
    }

    (float[] Probs, int Steps, int Classes) RunRaw(float[] data, int width)
    {
        var tensor = new DenseTensor<float>(data, new[] { 1, 3, Height, width });
        lock (_lock)
        {
            using var res = _session.Run(new[] { NamedOnnxValue.CreateFromTensor(_input, tensor) });
            var o = res.First().AsTensor<float>();
            var dims = o.Dimensions.ToArray();
            return (o.ToArray(), dims[^2], dims[^1]);
        }
    }

    /// <summary>Per-step log-probabilities over <see cref="DecodeAlphabet"/> for one crop of one line of text.</summary>
    public double[][] Run(Bitmap crop)
    {
        var (data, width) = Prepare(crop);
        var (probs, steps, classes) = RunRaw(data, width);
        return DecodeAlphabet.FromModel(probs, steps, classes, _classChars);
    }

    static unsafe (float[] Data, int Width) Prepare(Bitmap crop)
    {
        int w = Math.Clamp((int)Math.Ceiling(crop.Width * (double)Height / Math.Max(1, crop.Height)), 1, MaxWidth);
        int width = Math.Max(MinWidth, (w + 7) / 8 * 8);
        using var bmp = new Bitmap(w, Height, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(bmp))
        {
            g.InterpolationMode = InterpolationMode.HighQualityBilinear;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.DrawImage(crop, new Rectangle(0, 0, w, Height));
        }
        var data = new float[3 * Height * width];   // zeros: the padding is mid grey after normalising, as PaddleOCR pads
        var bd = bmp.LockBits(new Rectangle(0, 0, w, Height), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        try
        {
            int plane = Height * width;
            for (int y = 0; y < Height; y++)
            {
                byte* p = (byte*)bd.Scan0 + y * bd.Stride;
                for (int x = 0; x < w; x++, p += 4)
                {
                    int i = y * width + x;
                    // BGR, as PaddleOCR reads images with OpenCV
                    data[i] = p[0] / 127.5f - 1f;
                    data[plane + i] = p[1] / 127.5f - 1f;
                    data[2 * plane + i] = p[2] / 127.5f - 1f;
                }
            }
        }
        finally { bmp.UnlockBits(bd); }
        return (data, width);
    }

    public void Dispose() => _session.Dispose();
}

/// <summary>
/// The item-name decoder, shadow only (Prompt 8, 2026-10-24): beside Windows OCR, on the same loot list rows and chat
/// item names, the open recogniser reads the crop and the lexicon search turns its probabilities into an item id and a
/// log-probability, or "other". Nothing is counted from it. reads.log gets
/// <c>decode &lt;item&gt; p=&lt;x&gt; vs ocr &lt;text&gt; agree|disagree</c> (or <c>decode other ...</c>) and the local
/// tallies decode_agree / decode_disagree / decode_other (<see cref="Accuracy.Shadow"/>, never sent).
///
/// The model files come from <c>node tools/data/models.js</c> (pinned in data/models.json, checked by SHA-256). Without
/// them the tracker runs exactly as before and logs "decoder off (no model)" once. With them, the model is loaded on
/// first use on the decoder's own thread (never the capture thread), and the cold start and the time per line are
/// logged once each.
/// </summary>
public static class NameDecoder
{
    public const string ModelFile = "name-rec.onnx", DictFile = "name-rec-dict.txt";
    /// <summary>The same OCR text from the same place is decoded at most once in this long (a row stays up for seconds).</summary>
    const double RepeatSeconds = 3;
    /// <summary>Crops waiting at most; past that a crop is dropped (the decoder never holds the readers up).</summary>
    const int QueueCap = 16;

    /// <summary>Gate guards the queue and the repeat list (taken on the readers' thread, briefly); LoadGate the model, held while it loads, only on the decoder's thread.</summary>
    static readonly object Gate = new(), LoadGate = new();
    static ItemDictionary? _items;
    static NameLexicon? _lex;
    static int _lexCount = -1;
    static DateTime _lexAt;
    static RecModel? _model;
    static bool _present, _noted, _failed, _timed;
    static double _lineMs, _lineMax; static int _lines;

    /// <summary>"off (no model)", "ready" (files found, not loaded yet), "on", or "failed: ...".</summary>
    public static string State { get; private set; } = "off (no model)";
    /// <summary>The model files are there: the readers hand crops to <see cref="Shadow"/>.</summary>
    public static bool Present => _present && !_failed;
    /// <summary>The cold start, once loaded (model and lexicon), in ms.</summary>
    public static double ColdMs { get; private set; }

    /// <summary>At the session's start: a quick look for the model files (nothing is loaded yet).</summary>
    public static void Start(ItemDictionary items)
    {
        lock (Gate)
        {
            _items = items;
            _present = !ForceAbsent && DataFiles.ModelPresent(ModelFile) && DataFiles.ModelPresent(DictFile);
            if (!_present) { State = "off (no model)"; if (!_noted) { _noted = true; CrashLog.Note("decoder off (no model)"); } }
            else if (_model == null && !_failed) State = "ready";
        }
    }

    /// <summary>Loads the model and builds the lexicon if not done yet. Call off the capture thread. False when it can't run.</summary>
    public static bool EnsureLoaded()
    {
        lock (LoadGate)
        {
            if (_failed || _items == null || !_present) return false;
            if (_model != null) { RefreshLexicon(); return true; }
            var sw = Stopwatch.StartNew();
            try
            {
                var pins = DataFiles.ModelPins();
                var onnx = DataFiles.ModelPath(ModelFile, pins);
                var dict = DataFiles.ModelPath(DictFile, pins);
                if (onnx == null || dict == null)
                {
                    _failed = true; State = "off (no model)";
                    CrashLog.Note("decoder off (no model): the model files didn't match their SHA-256 in data/models.json");
                    return false;
                }
                double hashMs = sw.Elapsed.TotalMilliseconds;
                _model = RecModel.Load(onnx, dict);
                double modelMs = sw.Elapsed.TotalMilliseconds - hashMs;
                RefreshLexicon(force: true);
                ColdMs = sw.Elapsed.TotalMilliseconds;
                State = "on";
                CrashLog.Note($"decoder on: cold start {ColdMs:0} ms (files checked {hashMs:0} ms, model {modelMs:0} ms, lexicon of {_lex!.Names:N0} names {ColdMs - hashMs - modelMs:0} ms; budget 300 ms)");
                return true;
            }
            catch (Exception ex)
            {
                _failed = true; State = "failed: " + ex.Message;
                CrashLog.Note("decoder off: " + ex.Message);
                CrashLog.Write("name decoder", ex);
                return false;
            }
        }
    }

    /// <summary>For parse-test: behave as if the model files weren't there (the path every player without them takes).</summary>
    internal static bool ForceAbsent;
    /// <summary>For parse-test: forget the last look for the files (a loaded model stays loaded).</summary>
    internal static void ResetForTests() { lock (Gate) { _present = false; _failed = false; State = _model != null ? "on" : "off (no model)"; } }

    /// <summary>A fresh item list merged in while running: the lexicon follows it, at most every ten minutes.</summary>
    static void RefreshLexicon(bool force = false)
    {
        if (_items == null) return;
        int n = _items.Count;
        if (!force && (n == _lexCount || (DateTime.UtcNow - _lexAt).TotalMinutes < 10)) return;
        _lex = NameLexicon.From(_items); _lexCount = n; _lexAt = DateTime.UtcNow;
    }

    /// <summary>Decodes one crop (loads the model first if needed); null when the decoder can't run.</summary>
    public static NameDecode? Decode(Bitmap crop)
    {
        if (!EnsureLoaded()) return null;
        var sw = Stopwatch.StartNew();
        var lp = _model!.Run(crop);
        LastModelMs = sw.Elapsed.TotalMilliseconds;
        var r = CtcLexiconSearch.Decode(lp, _lex!);
        double ms = sw.Elapsed.TotalMilliseconds;
        LastSearchMs = ms - LastModelMs;
        lock (LoadGate)
        {
            if (!_timed)
            {
                // the first decode warms the model up; the next twenty are the time per line
                if (_lines++ > 0) { _lineMs += ms; _lineMax = Math.Max(_lineMax, ms); }
                if (_lines > 20) { _timed = true; CrashLog.Note($"decoder: {_lineMs / (_lines - 1):0.0} ms per line on average over {_lines - 1} lines, longest {_lineMax:0.0} ms (budget 5 ms)"); }
            }
        }
        return r;
    }

    /// <summary>The last decode's time in the model and in the search, ms (parse-test prints them).</summary>
    public static double LastModelMs { get; private set; }
    public static double LastSearchMs { get; private set; }

    /// <summary>The average time per line once measured (first twenty lines after the first), else -1.</summary>
    public static double LineMs { get { lock (LoadGate) return _lines > 1 ? _lineMs / (_lines - 1) : -1; } }

    /// <summary>A crop of one line of a picture, a little wider than the text's box, clamped to the picture. Null when empty.</summary>
    public static Bitmap? Crop(Bitmap raw, Rectangle box)
    {
        int h = box.Height;
        var r = Rectangle.FromLTRB(box.Left - h / 6, box.Top - h / 5, box.Right + h / 3, box.Bottom + h / 5);
        r.Intersect(new Rectangle(0, 0, raw.Width, raw.Height));
        if (r.Width < 4 || r.Height < 4) return null;
        try { return raw.Clone(r, PixelFormat.Format32bppArgb); } catch (Exception) { return null; }
    }

    // ---- shadow: a queue the readers drop crops into, one thread that decodes and logs ----

    /// <param name="LogDir">Where the log went when the crop was handed over: a test that has moved on (replay-bank, parse-test) never gets a late line in another folder.</param>
    sealed record Job(string Source, Bitmap Crop, string Ocr, long? OcrId, string? LogDir);
    static Channel<Job>? _queue;
    static readonly Dictionary<string, DateTime> Recent = new();

    /// <summary>
    /// Hands one crop to the decoder's thread (it owns and disposes the crop), with what OCR plus NameMatch made of the
    /// same name (<paramref name="ocrId"/>; null when it named no item). Returns at once; nothing waits on the decode.
    /// </summary>
    public static void Shadow(string source, Bitmap? crop, string ocr, long? ocrId)
    {
        if (crop == null) return;
        if (!Present) { crop.Dispose(); return; }
        var now = DateTime.UtcNow;
        var key = source + "|" + ItemDictionary.Norm(ocr);
        lock (Gate)
        {
            if (Recent.TryGetValue(key, out var at) && (now - at).TotalSeconds < RepeatSeconds) { crop.Dispose(); return; }
            Recent[key] = now;
            if (Recent.Count > 256) foreach (var k in Recent.Where(kv => (now - kv.Value).TotalSeconds >= RepeatSeconds).Select(kv => kv.Key).ToList()) Recent.Remove(k);
            if (_queue == null)
            {
                _queue = Channel.CreateBounded<Job>(new BoundedChannelOptions(QueueCap) { SingleReader = true, FullMode = BoundedChannelFullMode.Wait });
                var q = _queue;
                var th = new Thread(() => Work(q)) { IsBackground = true, Name = "name decoder", Priority = ThreadPriority.BelowNormal };
                th.Start();
            }
        }
        if (!_queue.Writer.TryWrite(new Job(source, crop, ocr, ocrId, CrashLog.DirOverride))) crop.Dispose();
    }

    static void Work(Channel<Job> q)
    {
        while (true)
        {
            Job job;
            try
            {
                if (!q.Reader.WaitToReadAsync().AsTask().GetAwaiter().GetResult()) return;
                if (!q.Reader.TryRead(out job!)) continue;
            }
            catch (Exception) { return; }
            using (job.Crop)
            {
                try
                {
                    if (CrashLog.DirOverride != job.LogDir) continue;
                    var r = Decode(job.Crop);
                    if (r == null || CrashLog.DirOverride != job.LogDir) continue;
                    var line = Outcome(r, job.Ocr, job.OcrId, out var tally);
                    if (tally == null) continue;
                    Accuracy.Shadow(tally);
                    CrashLog.Note(line + $" ({job.Source})");
                }
                catch (Exception ex) { CrashLog.Write("name decoder", ex); }
            }
        }
    }

    /// <summary>
    /// The reads.log line and the tally for one decode against OCR plus NameMatch: agree when both name the same item,
    /// disagree when the decoder names an item and OCR another or none, other when the decoder's best in-list reading was
    /// too far below its own free reading (<see cref="CtcLexiconSearch.OtherMargin"/>). Nothing to read: no tally.
    /// </summary>
    public static string Outcome(NameDecode r, string ocr, long? ocrId, out string? tally)
    {
        switch (r.Kind)
        {
            case DecodeKind.Item:
                bool same = ocrId is long o && o == r.ItemId;
                tally = same ? "decode_agree" : "decode_disagree";
                return $"decode {r.Name} p={r.P:0.00} vs ocr {ocr} {(same ? "agree" : "disagree")}";
            case DecodeKind.Other:
                tally = "decode_other";
                return $"decode other p={r.P:0.00} vs ocr {ocr} other (model read \"{r.Free}\"{(r.Name.Length > 0 ? $", nearest in the list {r.Name}" : "")})";
            default:
                tally = null;
                return "";
        }
    }

    /// <summary>A chat line that is the player's own pickup ("Acquired X x2.", "X acquired."), not Kinah, not another's loot.</summary>
    public static bool PickupLine(string line)
    {
        var t = line.Trim();
        if (t.Contains("kina", StringComparison.OrdinalIgnoreCase) || t.Contains(" through ", StringComparison.OrdinalIgnoreCase)) return false;
        return System.Text.RegularExpressions.Regex.IsMatch(t, @"^(?:\[[\d:]+\]\s*)?(?:acqu\w+\s+\S.*|\S.*\s+acqu\w+\W*)$", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
    }
}
