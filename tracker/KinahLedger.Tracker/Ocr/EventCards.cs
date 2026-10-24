using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace KinahLedger.Ocr;

/// <summary>
/// The Shugo Festival event's reward screen ("Rewards have been sent to your cube.", "Can select 0 time(s)"): five tall
/// cards side by side, centred on the screen. A card the player received has a bright blue frame, a card icon and a cyan
/// "1" in its upper right; one not received has a dim gold frame and neither. Turned face up, each card is a list of
/// reward slots three to a row, each slot the expedition table's kind (icon on its grade's backing, count in the lower
/// right, none for one piece of gear), inside a window 4.8 rows tall that the player can scroll (2026-10-24, Hidden Lugi,
/// 17 pictures at 5120x2160 and the 10 fps footage in kinah-bench: a 19-slot card showed its last rows only after the
/// player scrolled it, and a 14-slot card's fifth row was cut by the window's lower edge the whole time).
/// The cards print no item names, so a slot is named by its icon (RewardGrid's fingerprints and sure distance) and its
/// backing's grade, and voted across reads like the expedition table (<see cref="Merger"/>). Geometry below is measured
/// on Hidden Lugi only; other Shugo events need one picture each before their layout is trusted, and a screen whose five
/// cards aren't found is left to its pictures (<see cref="Locate"/> returns null and says why).
/// </summary>
public static class EventCards
{
    // measured on 20261024-130700 and -100647 (5120x2160): x from the screen's centre, y from the top, at 2160 high
    const double Ref = 2160;
    const double Card0Left = -1355.25, CardPitch = 556.5, CardW = 484, CardTop = 616;
    const double SlotDx = 32, SlotW = 133, SlotH = 135, PitchX = 144, PitchY = 146;
    // the list's window: rows show between these lines; unscrolled, the first row's top is 5 px under the window's top
    const double ViewTop = 782, ViewBottom = 1482, FirstRowTop = 787;
    public const int CardCount = 5, Columns = 3;

    /// <summary>One slot as one read saw it: its row in this read's window (0 = the topmost row any part of which shows), column, the slot read, and whether the window cut it.</summary>
    public sealed record CardSlot(int Row, int Col, RewardGrid.Slot Slot, bool Clipped);

    /// <summary>
    /// One card as one read saw it. <c>FirstTop</c>: the top of the topmost row in the window (5 px under the window's top
    /// when not scrolled). <c>End</c>: the list's end shows (an empty place after the last slot, inside the window).
    /// <c>Covered</c>: something over the list (a tooltip): a place without a slot that isn't the card's own backing, or a
    /// slot after an empty place. <c>Scrolled</c>: the window isn't at its top.
    /// </summary>
    public sealed record Card(int Index, Rectangle Box, bool Received, int FirstTop, bool Scrolled, List<CardSlot> Slots, bool End, bool Covered, int Present);

    /// <summary>A read of the screen: the five cards left to right.</summary>
    public sealed record Screen(List<Card> Cards)
    {
        public IEnumerable<Card> Received => Cards.Where(c => c.Received);
        /// <summary>Every received card shows slots (not still face down or behind a flip).</summary>
        public bool FaceUp => Received.Any() && Received.All(c => c.Present > 0);
    }

    static int Diff(int a, int b) => Math.Abs(((a >> 16) & 255) - ((b >> 16) & 255)) + Math.Abs(((a >> 8) & 255) - ((b >> 8) & 255)) + Math.Abs((a & 255) - (b & 255));
    static int R(int c) => (c >> 16) & 255;
    static int G(int c) => (c >> 8) & 255;
    static int B(int c) => c & 255;

    /// <summary>The frame's pixels as 32-bit ARGB, row by row (GetPixel is far too slow for the searches here).</summary>
    sealed class Pixels
    {
        public readonly int W, H; readonly int[] _p;
        public Pixels(Bitmap f)
        {
            W = f.Width; H = f.Height; _p = new int[W * H];
            var d = f.LockBits(new Rectangle(0, 0, W, H), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            try { for (int y = 0; y < H; y++) Marshal.Copy(d.Scan0 + y * d.Stride, _p, y * W, W); }
            finally { f.UnlockBits(d); }
        }
        public int this[int x, int y] => _p[Math.Clamp(y, 0, H - 1) * W + Math.Clamp(x, 0, W - 1)];
    }

    /// <summary>
    /// Finds the five cards and tells received from not: null when the layout isn't this one (fewer than five frames where
    /// they belong, a frame neither blue nor gold, or a frame and its card icon that disagree), with the reason in
    /// <paramref name="why"/>. Slots are not read.
    /// </summary>
    public static Screen? Locate(Bitmap frame, out string why) => Locate(new Pixels(frame), out why);

    static Screen? Locate(Pixels px, out string why)
    {
        why = "";
        double k = px.H / Ref; int cx = px.W / 2;
        int S(double v) => (int)Math.Round(v * k);
        var cards = new List<Card>();
        for (int i = 0; i < CardCount; i++)
        {
            int est = cx + S(Card0Left + i * CardPitch);
            // the frame's sides: the brightest column near where each belongs, over the card's middle
            (int X, double V) Side(int around)
            {
                int bx = around; double bv = -1;
                for (int x = around - S(12); x <= around + S(12); x++)
                {
                    double v = 0; int n = 0;
                    for (int y = S(900); y < S(1300); y += Math.Max(1, S(5))) { int c = px[x, y]; v += Math.Max(R(c), Math.Max(G(c), B(c))); n++; }
                    v /= Math.Max(1, n);
                    if (v > bv) { bv = v; bx = x; }
                }
                return (bx, bv);
            }
            var l = Side(est); var r = Side(est + S(CardW));
            double Outside(int x)
            {
                double v = 0; int n = 0;
                for (int y = S(900); y < S(1300); y += Math.Max(1, S(5))) { int c = px[x, y]; v += Math.Max(R(c), Math.Max(G(c), B(c))); n++; }
                return v / Math.Max(1, n);
            }
            if (l.V < 80 || r.V < 80 || l.V - Outside(l.X - S(15)) < 45 || r.V - Outside(r.X + S(15)) < 45 || Math.Abs(r.X - l.X - S(CardW)) > S(10))
            { why = $"card {i + 1}'s frame isn't where the five cards' frames are (sides {l.V:0} and {r.V:0} bright, {r.X - l.X} px apart)"; return null; }
            // its top edge: the brightest line near where it belongs, over the middle of the card's width
            int top = S(CardTop); double tv = -1; (int R, int G, int B) tc = (0, 0, 0);
            for (int y = S(CardTop - 14); y <= S(CardTop + 14); y++)
            {
                long rr = 0, gg = 0, bb = 0; int n = 0;
                for (int x = l.X + S(60); x < r.X - S(60); x += Math.Max(1, S(4))) { int c = px[x, y]; rr += R(c); gg += G(c); bb += B(c); n++; }
                double v = (rr + gg + bb) / (double)Math.Max(1, n);
                if (v > tv) { tv = v; top = y; tc = ((int)(rr / n), (int)(gg / n), (int)(bb / n)); }
            }
            bool blue = Math.Min(tc.R, Math.Min(tc.G, tc.B)) >= 180 && tc.B >= tc.R - 20;
            bool gold = !blue && tc.R >= 110 && tc.R - tc.B >= 30;
            // the card icon's cyan "1" in the upper right
            int cyan = 0;
            for (int y = S(CardTop + 93); y < S(CardTop + 163); y++)
                for (int x = l.X + S(390); x < l.X + S(460); x++) { int c = px[x, y]; if (G(c) > 170 && B(c) > 190 && R(c) < 130) cyan++; }
            bool one = cyan >= 80 * k * k, none = cyan <= 20 * k * k;
            if (!(blue && one) && !(gold && none))
            { why = $"card {i + 1}: its frame is {(blue ? "blue" : gold ? "gold" : $"neither blue nor gold ({tc.R},{tc.G},{tc.B})")} but its card icon {(one ? "shows" : none ? "doesn't show" : "half shows")} the \"1\" ({cyan} px)"; return null; }
            cards.Add(new Card(i, Rectangle.FromLTRB(l.X, top, r.X, top + S(1000)), blue, 0, false, new List<CardSlot>(), false, false, 0));
        }
        return new Screen(cards);
    }

    /// <summary>
    /// A quick look, about two hundred pixels: a bright upright where each of the five cards' left frames belongs. For
    /// keying on the cards themselves when the screen's title can't be read (2026-10-24: a Daeva Pass banner over the
    /// title held the screen's recognition back 20 s); <see cref="Locate"/> then decides.
    /// </summary>
    public static bool LooksLikeCards(Bitmap f)
    {
        double k = f.Height / Ref; int cx = f.Width / 2;
        for (int i = 0; i < CardCount; i++)
        {
            int est = cx + (int)Math.Round((Card0Left + i * CardPitch) * k);
            double best = 0;
            for (int x = est - (int)(8 * k); x <= est + (int)(8 * k); x += 2)
            {
                if (x < 0 || x >= f.Width) continue;
                double v = 0; int n = 0;
                for (int y = (int)(950 * k); y < (int)(1300 * k); y += (int)Math.Max(1, 60 * k)) { var c = f.GetPixel(x, y); v += Math.Max(c.R, Math.Max(c.G, c.B)); n++; }
                best = Math.Max(best, v / Math.Max(1, n));
            }
            if (best < 80) return false;
        }
        return true;
    }

    /// <summary>
    /// Reads the screen: the cards (<see cref="Locate"/>), then each received card's slots in its window, each read as the
    /// expedition table reads its slots (count, grade, closest icons). A slot the window cuts is kept with no count and no
    /// icons (never read from half a picture). <paramref name="cache"/> keeps slots read before by their exact pixels, so a
    /// screen that hasn't moved costs no text recognition the second time.
    /// </summary>
    public static async Task<(Screen? Screen, string Why)> ReadAsync(Bitmap frame, Dictionary<long, RewardGrid.Slot>? cache = null)
    {
        var px = new Pixels(frame);
        var located = Locate(px, out var why);
        if (located == null) return (null, why);
        double k = px.H / Ref;
        int S(double v) => (int)Math.Round(v * k);
        int vt = S(ViewTop), vb = S(ViewBottom), sw = S(SlotW), sh = S(SlotH), pitch = S(PitchY);
        var cards = new List<Card>();
        foreach (var card in located.Cards)
        {
            if (!card.Received) { cards.Add(card); continue; }
            int[] xs = Enumerable.Range(0, Columns).Select(c => card.Box.Left + S(SlotDx + c * PitchX)).ToArray();
            // the rows' place: the first row's top that puts the most slot edges (a slot's top and bottom lines, the whole
            // width) on the rows' lines inside the window. Unscrolled it is 5 px under the window's top
            double Line(int x0, int y, bool topEdge)
            {
                if (y - 4 < vt || y + 4 >= vb) return 0;
                // the lower quartile along the line: a slot's edge runs its whole width, an edge in its art doesn't (the mean
                // put the rows' lines inside the icons, 2026-10-24)
                var d = new List<int>();
                for (int x = x0 + sw / 8; x < x0 + sw - sw / 8; x += 2)
                    d.Add(topEdge ? Diff(px[x, y + 1], px[x, y - 3]) : Diff(px[x, y - 1], px[x, y + 3]));
                d.Sort();
                return d.Count == 0 ? 0 : d[d.Count / 4];
            }
            int unscrolled = S(FirstRowTop), firstTop = unscrolled; double bestScore = -1;
            // a window a few pixels either side of unscrolled is unscrolled (the pitch is 146 px give or take one, so a search
            // reaching a whole pitch up found the same rows one row early)
            for (int t0 = unscrolled - pitch + 4; t0 <= unscrolled + 3; t0++)
            {
                double score = 0;
                for (int t = t0; t < vb; t += pitch)
                    foreach (var x in xs) score += Line(x, t, true) + Line(x, t + sh - 1, false);
                if (score > bestScore + 1e-9) { bestScore = score; firstTop = t0; }
            }
            bool scrolled = Math.Abs(firstTop - unscrolled) > Math.Max(2, S(2));
            var slots = new List<CardSlot>();
            var pattern = new List<(int Row, int Col, bool Present, bool Backing, bool HalfSeen)>();
            int row = 0;
            for (int t = firstTop; t < vb; t += pitch, row++)
            {
                int visTop = Math.Max(t, vt), visBottom = Math.Min(t + sh, vb);
                if (visBottom - visTop < sh / 4) continue;   // a sliver of a row says nothing
                bool clipped = t < vt - 1 || t + sh > vb + 1;
                for (int c = 0; c < Columns; c++)
                {
                    var vis = Rectangle.FromLTRB(xs[c], visTop, xs[c] + sw, visBottom);
                    bool present = SidesStandOut(px, vis) >= SideMin;
                    pattern.Add((row, c, present, !present && CardBacking(px, vis), visBottom - visTop >= sh / 2));
                    if (!present) continue;
                    var box = new Rectangle(xs[c], t, sw, sh);
                    RewardGrid.Slot sl;
                    if (clipped) sl = new RewardGrid.Slot(box, RewardGrid.GradeOf(TopBacking(px, vis)), "", 0, new List<(string, double)>());
                    else
                    {
                        long key = Key(px, box);
                        if (cache == null || !cache.TryGetValue(key, out sl!))
                        {
                            sl = await RewardGrid.ReadSlotAsync(frame, box);
                            // more figures read than the corner holds is a misread (2026-10-24: 360 read as 3360), so the count is
                            // not read rather than taken
                            var corner = new Rectangle(box.Left + box.Width / 8, box.Top + box.Height * 3 / 5, box.Width * 7 / 8 - 2, box.Height * 2 / 5 - 2);
                            if (System.Text.RegularExpressions.Regex.IsMatch(sl.CountText.Trim(), @"^\d{2,6}$") && sl.CountText.Trim().Length > RewardGrid.FigureRuns(frame, corner))
                                sl = sl with { Count = 0 };
                            // four plain figures or more is a misread: the cards write 1,000 and up as "1.1K". Seen 2026-10-24: "440"
                            // read as "3440" and "360" as "3360", the Abyss Points coin's ring glinting white just left of the
                            // figures. The figures' own width on the count's lower half, where the ring doesn't reach, says how many
                            // there are (three at most), and the reader's last ones are taken; otherwise the count is not read
                            var said = sl.CountText.Trim();
                            if (System.Text.RegularExpressions.Regex.IsMatch(said, @"^\d{4,}$"))
                            {
                                int wide = FiguresWide(px, box) ?? 0;
                                sl = wide is >= 1 and <= 3 ? sl with { CountText = said[^wide..], Count = long.Parse(said[^wide..]) } : sl with { Count = 0 };
                                if (Environment.GetEnvironmentVariable("KL_CARDS_DEBUG") is { Length: > 0 }) Console.WriteLine($"    count {said}: {wide} figure(s) wide, now {sl.Count}");
                            }
                            if (cache != null) cache[key] = sl;
                        }
                    }
                    slots.Add(new CardSlot(row, c, sl, clipped));
                }
            }
            // the list's end shows when, inside the window, a place after the last slot is empty and is the card's own backing;
            // something over the list (a tooltip) leaves a place that is neither, or a slot after an empty place
            int last = pattern.FindLastIndex(p => p.Present);
            bool covered = pattern.Any(p => !p.Present && !p.Backing && p.HalfSeen) || pattern.Take(Math.Max(0, last)).Any(p => !p.Present);
            bool end = !covered && last >= 0 && pattern.Skip(last + 1).Any(p => !p.Present && p.Backing && p.HalfSeen);
            cards.Add(card with { FirstTop = firstTop, Scrolled = scrolled, Slots = slots, End = end, Covered = covered, Present = pattern.Count(p => p.Present) });
        }
        return (new Screen(cards), "");
    }

    /// <summary>
    /// How many figures wide a slot's count is: on the lower half of the count's line (its figures are about 0.14 of the
    /// slot tall and 0.11 apart, measured on "700" and "440"), the white run from the right edge leftwards, gaps under half
    /// a figure's height bridged. A "1" is narrow, so this is only used to drop a figure the reader added before the count.
    /// Null when no white is there.
    /// </summary>
    static int? FiguresWide(Pixels px, Rectangle box)
    {
        bool White(int x, int y) { int c = px[x, y]; int mn = Math.Min(R(c), Math.Min(G(c), B(c))), mx = Math.Max(R(c), Math.Max(G(c), B(c))); return mn > 185 && mx - mn < 40; }
        int x0 = box.Left + box.Width / 4, x1 = box.Right - 2, yTop = box.Top + box.Height * 3 / 5, yb = -1;
        for (int y = box.Bottom - 2; y >= yTop && yb < 0; y--) for (int x = x0; x < x1; x++) if (White(x, y)) { yb = y; break; }
        if (yb < 0) return null;
        int fh = Math.Max(6, (int)Math.Round(box.Height * 0.135));
        var col = new bool[x1 - x0];
        for (int x = x0; x < x1; x++) for (int y = yb - fh / 2; y <= yb; y++) if (White(x, y)) { col[x - x0] = true; break; }
        int r = Array.LastIndexOf(col, true); if (r < 0) return null;
        int l = r, gap = 0;
        for (int i = r - 1; i >= 0; i--) { if (col[i]) { l = i; gap = 0; } else if (++gap > fh / 2) break; }
        double advance = box.Height * 0.11;
        return Math.Max(1, (int)Math.Round((r - l + 1) / advance));
    }

    /// <summary>A slot's outline stands out at least this much (see <see cref="SidesStandOut"/>).</summary>
    public const double SideMin = 12;

    /// <summary>
    /// How much a box's left and right sides stand out from the card behind them: the colour difference between a pixel 2 px
    /// inside and one 4 px outside, along both sides, the lower quartile. A slot has a lighter rim on every side whatever
    /// its art; an empty place is the card's own smooth backing (2026-10-24: slots 20 to 108, empty places 0 to 3).
    /// </summary>
    static double SidesStandOut(Pixels px, Rectangle b)
    {
        var d = new List<int>();
        for (int y = b.Top + 6; y < b.Bottom - 6; y += 2)
        {
            d.Add(Diff(px[b.Left + 2, y], px[b.Left - 4, y]));
            d.Add(Diff(px[b.Right - 3, y], px[b.Right + 3, y]));
        }
        if (d.Count == 0) return 0;
        d.Sort();
        return d[d.Count / 4];
    }

    /// <summary>The place is the received card's own backing: blue (from dark navy at the top to lighter blue at the bottom), never a grey panel over it.</summary>
    static bool CardBacking(Pixels px, Rectangle b)
    {
        long rr = 0, bb = 0; int n = 0;
        for (int y = b.Top + 4; y < b.Bottom - 4; y += 4)
            for (int x = b.Left + 4; x < b.Right - 4; x += 4) { int c = px[x, y]; rr += R(c); bb += B(c); n++; }
        if (n == 0) return false;
        double r = rr / (double)n, bl = bb / (double)n;
        return bl - r >= 6 && bl >= r * 1.3;
    }

    /// <summary>The backing's colour from the two top corners (the lower ones may be under the window's edge).</summary>
    static Color TopBacking(Pixels px, Rectangle b)
    {
        int d = Math.Max(3, b.Width / 20);
        int a = px[b.Left + d, b.Top + d], c = px[b.Right - d, b.Top + d];
        return Color.FromArgb((R(a) + R(c)) / 2, (G(a) + G(c)) / 2, (B(a) + B(c)) / 2);
    }

    /// <summary>A slot's pixels and place as one number (every second pixel of every second row), for the read cache.</summary>
    static long Key(Pixels px, Rectangle b)
    {
        unchecked
        {
            long h = 1469598103934665603L ^ (b.X * 31L + b.Y) * 1099511628211L;
            for (int y = b.Top; y < b.Bottom; y += 2)
                for (int x = b.Left; x < b.Right; x += 2) h = (h ^ (px[x, y] & 0xFFFFFF)) * 1099511628211L;
            return h;
        }
    }

    /// <summary>
    /// Every read of one reward screen put together, card by card. A card's list can be longer than its window, and the
    /// player can scroll it between reads, so each read is placed in the list by its content: unscrolled it starts at the
    /// list's first row; scrolled, at the whole-row offset where its sure slots agree with what the list holds (no slot that
    /// disagrees, at least two that agree, and only one such offset), else it is left out for that card. Each place in the
    /// list keeps every full read of its slot, and the place's slot is voted like the expedition table's (sure reads only
    /// vote when there are any). A place only ever seen cut by the window stays not read.
    /// </summary>
    public sealed class Merger
    {
        public readonly double Sure;
        public Merger(double sure) { Sure = sure; }
        public int Reads { get; private set; }
        public int Unaligned { get; private set; }
        public readonly Dictionary<long, RewardGrid.Slot> Cache = new();
        /// <summary>Each received card: per place in its list, every full read of the slot there; places seen only cut; where its end showed.</summary>
        public sealed class CardList
        {
            public readonly Dictionary<int, List<RewardGrid.Slot>> Full = new();
            public readonly Dictionary<int, RewardGrid.Slot> Cut = new();
            public int? Length;
            public bool EverScrolled;
            public readonly Dictionary<int, Bitmap> Crops = new();
            public int Count => Math.Max(Length ?? 0, Full.Keys.Concat(Cut.Keys).DefaultIfEmpty(-1).Max() + 1);
        }
        public readonly Dictionary<int, CardList> Cards = new();
        public bool?[] Received { get; } = new bool?[CardCount];
        readonly List<Dictionary<int, (int Offset, int FirstTop, List<string> Sig)>> _sigs = new();

        /// <summary>Adds one read; false when it doesn't fit the screen read so far (a different set of received cards).</summary>
        public bool Add(Screen s, Bitmap frame)
        {
            for (int i = 0; i < CardCount; i++)
                if (Received[i] is bool was && was != s.Cards[i].Received) return false;
            for (int i = 0; i < CardCount; i++) Received[i] = s.Cards[i].Received;
            Reads++;
            var sig = new Dictionary<int, (int, int, List<string>)>();
            foreach (var c in s.Received)
            {
                if (!Cards.TryGetValue(c.Index, out var list)) Cards[c.Index] = list = new CardList();
                int? offset = Place(list, c);
                if (offset is not int o) { Unaligned++; continue; }
                if (c.Scrolled) list.EverScrolled = true;
                foreach (var cs in c.Slots)
                {
                    int at = (o + cs.Row) * Columns + cs.Col;
                    if (cs.Clipped) { if (!list.Full.ContainsKey(at)) list.Cut[at] = cs.Slot; continue; }
                    list.Cut.Remove(at);
                    if (!list.Full.TryGetValue(at, out var votes)) list.Full[at] = votes = new List<RewardGrid.Slot>();
                    votes.Add(cs.Slot);
                    // the crop of its latest full read, kept for a slot that ends not read
                    try { var r = Rectangle.Intersect(cs.Slot.Box, new Rectangle(0, 0, frame.Width, frame.Height)); if (r.Width > 0 && r.Height > 0) { if (list.Crops.TryGetValue(at, out var old)) old.Dispose(); list.Crops[at] = frame.Clone(r, PixelFormat.Format24bppRgb); } }
                    catch (Exception) { /* no crop: the slot is still filed */ }
                }
                if (c.End && !c.Covered)
                {
                    int lastAt = c.Slots.Count == 0 ? -1 : c.Slots.Max(cs => (o + cs.Row) * Columns + cs.Col);
                    list.Length = Math.Max(list.Length ?? 0, lastAt + 1);
                }
                sig[c.Index] = (o, c.FirstTop, c.Slots.Where(x => !x.Clipped).Select(x => $"{x.Row}.{x.Col}:{Top(x.Slot)}:{x.Slot.Count}").ToList());
            }
            _sigs.Add(sig);
            return true;
        }

        string Top(RewardGrid.Slot s) => s.Icons.Count > 0 && s.Icons[0].Distance <= Sure ? s.Icons[0].Key : "?";

        /// <summary>The whole-row offset of a card's read in its list, or null when it can't be placed surely.</summary>
        int? Place(CardList list, Card c)
        {
            var full = c.Slots.Where(x => !x.Clipped && x.Slot.Icons.Count > 0 && x.Slot.Icons[0].Distance <= Sure).ToList();
            var fits = new List<(int O, int Agree)>();
            for (int o = 0; o <= 40; o++)
            {
                int agree = 0, clash = 0;
                foreach (var x in full)
                {
                    int at = (o + x.Row) * Columns + x.Col;
                    if (!list.Full.TryGetValue(at, out var votes)) continue;
                    var sure = votes.Where(v => v.Icons.Count > 0 && v.Icons[0].Distance <= Sure).ToList();
                    if (sure.Count == 0) continue;
                    var key = sure.GroupBy(v => v.Icons[0].Key).OrderByDescending(g => g.Count()).First().Key;
                    // the same icon agrees; with a count read differently it is neither (a misread count mustn't stop a
                    // placement, nor make one)
                    if (key != x.Slot.Icons[0].Key) clash++;
                    else if (x.Slot.Count <= 0 || sure.Any(v => v.Count == x.Slot.Count)) agree++;
                }
                // past the list's known end nothing can be
                if (list.Length is int len && c.Slots.Any(x => (o + x.Row) * Columns + x.Col >= len)) continue;
                if (clash == 0 && agree >= 2) fits.Add((o, agree));
            }
            // unscrolled by its rows' place, it is at the top unless that clashes (a scroll of exactly whole rows looks the same)
            if (!c.Scrolled && (list.Full.Count == 0 || fits.Any(f => f.O == 0) || !Clashes(list, c, 0))) return 0;
            if (fits.Count == 0) return null;
            int most = fits.Max(f => f.Agree);
            var best = fits.Where(f => f.Agree == most).ToList();
            return best.Count == 1 ? best[0].O : null;
        }

        bool Clashes(CardList list, Card c, int o)
        {
            foreach (var x in c.Slots.Where(x => !x.Clipped && x.Slot.Icons.Count > 0 && x.Slot.Icons[0].Distance <= Sure))
                if (list.Full.TryGetValue((o + x.Row) * Columns + x.Col, out var votes)
                    && votes.Where(v => v.Icons.Count > 0 && v.Icons[0].Distance <= Sure).GroupBy(v => v.Icons[0].Key).OrderByDescending(g => g.Count()).FirstOrDefault() is { } top
                    && top.Key != x.Slot.Icons[0].Key) return true;
            return false;
        }

        /// <summary>
        /// The last two reads agree card by card (same place in the list, same slots, same icons and counts) and every full
        /// slot of both is a sure match: the screen has stopped moving.
        /// </summary>
        public bool Settled()
        {
            if (_sigs.Count < 2 || Cards.Count == 0) return false;
            var a = _sigs[^2]; var b = _sigs[^1];
            foreach (var i in Cards.Keys)
            {
                if (!a.TryGetValue(i, out var x) || !b.TryGetValue(i, out var y)) return false;
                if (x.Offset != y.Offset || Math.Abs(x.FirstTop - y.FirstTop) > 2 || !x.Sig.SequenceEqual(y.Sig) || x.Sig.Count == 0) return false;
                if (x.Sig.Any(s => s.Contains(":?:"))) return false;
            }
            return true;
        }

        /// <summary>Every received card's whole list has been seen: its end, and each place before it read in full.</summary>
        public bool Complete => Cards.Count > 0 && Cards.Values.All(l => l.Length is int n && Enumerable.Range(0, n).All(l.Full.ContainsKey));

        /// <summary>A card's list as voted, place by place: a full read's vote, or a cut slot (no count, no icons) where only that was seen.</summary>
        public List<RewardGrid.Slot> Voted(int card)
        {
            var res = new List<RewardGrid.Slot>();
            if (!Cards.TryGetValue(card, out var l)) return res;
            for (int at = 0; at < l.Count; at++)
            {
                if (l.Full.TryGetValue(at, out var votes))
                {
                    var sureOnes = votes.Where(v => v.Icons.Count > 0 && v.Icons[0].Distance <= Sure).ToList();
                    res.Add(RewardGrid.Vote((sureOnes.Count > 0 ? sureOnes : votes).Select(v => new List<RewardGrid.Slot> { v }).ToList())[0]);
                }
                else if (l.Cut.TryGetValue(at, out var cut)) res.Add(cut);
                else res.Add(new RewardGrid.Slot(Rectangle.Empty, "Common", "", 0, new List<(string, double)>()));   // a place between seen ones, never shown
            }
            return res;
        }

        public void Dispose() { foreach (var l in Cards.Values) foreach (var b in l.Crops.Values) b.Dispose(); }
    }
}
