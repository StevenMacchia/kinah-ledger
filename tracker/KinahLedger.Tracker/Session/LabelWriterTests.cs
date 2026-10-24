using System.Drawing;
using System.Text.Json;
using KinahLedger.Data;
using KinahLedger.Ocr;
using KinahLedger.Upload;

namespace KinahLedger.Session;

/// <summary>
/// parse-test's checks for "Help improve counting" (branch tracker/label-writer, 2026-10-24): a Kina line the counter
/// confirmed and a loot row a chat line confirmed become pairs; a pickup no second witness confirmed doesn't; the setting
/// off writes nothing and keeps no file; the crop rule (chat panel refused, item span allowed, 4 px margin); text that
/// could name a player refused; the upload batch's shape. Writes only to a scratch folder; the live writer is restored.
/// </summary>
public static class LabelWriterTests
{
    public static bool Run(ItemDictionary items, Settings settings)
    {
        bool ok = true;
        void Check(string what, string got, string expect)
        {
            bool pass = got == expect; ok &= pass;
            Console.WriteLine($"  {what,-62} -> {got,-40} {(pass ? "ok" : "MISMATCH, expected " + expect)}");
        }
        Console.WriteLine("\nHelp improve counting: training pairs, crops, consent (2026-10-24):");
        var keepDir = CrashLog.DirOverride;
        var keepWriter = LabelWriter.Shared;
        var dir = TestTemp.Path("kl-labels-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(dir);
        CrashLog.DirOverride = dir;
        try
        {
            List<LabelPair> Pairs(LabelWriter w) => File.Exists(w.PendingPath)
                ? File.ReadAllLines(w.PendingPath).Where(l => l.Length > 0).Select(l => JsonSerializer.Deserialize<LabelPair>(l)!).ToList() : new();
            string Show(LabelPair p) => $"{p.Kind} \"{p.Text}\" = {p.Name} x{p.Qty} by {string.Join("+", p.Witnesses)}";
            var filler = new List<string> { "[P1] hello", "[P2] hi there" };
            var t0 = new DateTime(2026, 10, 24, 18, 0, 0, DateTimeKind.Utc);

            // a Kina line the Kinah counter's rise confirmed: one kina-line pair
            void KinaRun(TrackerSession s, DateTime t)
            {
                s.ProcessChat(filler, t);
                s.CheckBalance(29_000_000, t.AddSeconds(0.5));
                s.CheckBalance(29_000_408, t.AddSeconds(1));
                s.ProcessChat(filler.Append("Acquired 408 Kina.").ToList(), t.AddSeconds(2));
                s.CheckBalance(29_000_408, t.AddSeconds(7));
                s.CheckBalance(29_000_408, t.AddSeconds(10));
            }
            // a chat pickup with its loot list row up: a chat-item pair and a loot-row pair
            void RowRun(TrackerSession s, DateTime t)
            {
                s.ProcessChat(filler, t);
                s.ProcessList(new List<LootLine> { new(1197, "Odyle x7", 0.62, 0xF0F0F0F0F0F0F0F0UL, 0.14, 0.12, 1920) }, t.AddSeconds(0.5));
                s.ProcessChat(filler.Append("Acquired Odyle x7.").ToList(), t.AddSeconds(1));
                s.FlushVotes(t.AddSeconds(2));
            }

            {
                var w = new LabelWriter(Path.Combine(dir, "on"));
                w.SetEnabled(true);
                LabelWriter.Shared = w;
                KinaRun(new TrackerSession(null, settings, items, "Test") { Vote = false }, t0);
                var p = Pairs(w);
                Check("a Kina line the counter confirmed: one pair", string.Join(" | ", p.Select(Show)), "kina-line \"Acquired 408 Kina.\" = Kina x408 by chat+kinah counter");
                Check("its fields: no pixels, version, item 0, id", p.Count == 1 ? $"{p[0].Item} {p[0].Crops.Length} {p[0].Version == RunPayload.AppVersion} {p[0].Id.Length}" : "none", "0 0 True 16");

                var w2 = new LabelWriter(Path.Combine(dir, "row"));
                w2.SetEnabled(true);
                LabelWriter.Shared = w2;
                RowRun(new TrackerSession(null, settings, items, "Test") { Vote = false }, t0.AddMinutes(5));
                var p2 = Pairs(w2);
                Check("a chat pickup the loot list confirmed: two pairs", string.Join(" | ", p2.Select(Show)),
                    "chat-item \"Acquired Odyle x7.\" = Odyle x7 by chat+loot list | loot-row \"Odyle x7\" = Odyle x7 by loot list+chat");
                Check("the loot row carries its icon hash", p2.FirstOrDefault(x => x.Kind == "loot-row")?.Icon ?? "none", "f0f0f0f0f0f0f0f0");

                // no second witness: a chat pickup with no row, a Kina line with no counter read
                var w3 = new LabelWriter(Path.Combine(dir, "alone"));
                w3.SetEnabled(true);
                LabelWriter.Shared = w3;
                var s3 = new TrackerSession(null, settings, items, "Test") { Vote = false };
                var t3 = t0.AddMinutes(10);
                s3.ProcessChat(filler, t3);
                s3.ProcessChat(filler.Append("Acquired Odyle x3.").Append("Acquired 77 Kina.").ToList(), t3.AddSeconds(1));
                s3.FlushVotes(t3.AddSeconds(2));
                s3.ProcessList(new List<(int, string)>(), t3.AddSeconds(20));
                Check("no second witness: counted, but no pair", $"{s3.Engine.Kinah > 0} {Pairs(w3).Count}", "True 0");

                // the setting off: the same confirmed reads write nothing, and the folder is gone
                var w4 = new LabelWriter(Path.Combine(dir, "off"));
                w4.SetEnabled(true);
                File.WriteAllText(Path.Combine(Directory.CreateDirectory(w4.Dir).FullName, "pending.jsonl"), "{}\n");
                w4.SetEnabled(false);
                LabelWriter.Shared = w4;
                KinaRun(new TrackerSession(null, settings, items, "Test") { Vote = false }, t0.AddMinutes(15));
                RowRun(new TrackerSession(null, settings, items, "Test") { Vote = false }, t0.AddMinutes(20));
                Check("setting off: nothing written, the local file not kept", $"{Directory.Exists(w4.Dir)} {w4.Written} {w4.TakeBatch().Count}", "False 0 0");

                // what a pair's text may be: never a sender bracket, another player's loot, or a long line
                string Safe(string kind, string t) => LabelWriter.SafeText(kind, t) ?? "refused";
                Check("text: a timestamp dropped", Safe("kina-line", "[12:01] Acquired 408 Kina."), "Acquired 408 Kina.");
                Check("text: a sender bracket refused", Safe("chat-item", "[Yarie] Acquired Odyle x7."), "refused");
                Check("text: another player's loot refused", Safe("chat-item", "Yarie acquired Odyle x7 through Force distribution."), "refused");
                Check("text: a chat form refused", Safe("loot-row", "Yarie: Odyle x7"), "refused");
                Check("text: a loot row kept", Safe("loot-row", "Odyle x7"), "Odyle x7");
                Check("writer: one witness is no pair", (w2.Write("loot-row", "Odyle x2", null, 1, "Odyle", 2, ["loot list"], Size.Empty, 0, t0) == null).ToString(), "True");
            }

            // the crop rule
            {
                var span = new Rectangle(400, 900, 220, 30);
                bool A(Rectangle r, CropKind k, Rectangle s, Rectangle pic = default) => Crops.IsAllowed(r, new CropSource(k, s, pic));
                Check("crop: the chat panel is refused", A(new Rectangle(20, 600, 700, 400), CropKind.ChatPanel, new Rectangle(20, 600, 700, 400)).ToString(), "False");
                Check("crop: a whole frame is refused", A(new Rectangle(0, 0, 640, 360), CropKind.Frame, new Rectangle(0, 0, 640, 360)).ToString(), "False");
                Check("crop: an item span is allowed", A(span, CropKind.ChatItem, span).ToString(), "True");
                Check("crop: the span plus 4 px is allowed", A(Rectangle.Inflate(span, 4, 4), CropKind.LootName, span).ToString(), "True");
                Check("crop: the span plus 5 px is refused", A(Rectangle.Inflate(span, 5, 5), CropKind.LootName, span).ToString(), "False");
                Check("crop: 5 px past one side is refused", A(new Rectangle(span.X, span.Y, span.Width + 5, span.Height), CropKind.ChatItem, span).ToString(), "False");
                Check("crop: a chat 'span' as tall as the panel is refused", A(new Rectangle(20, 600, 300, 400), CropKind.ChatItem, new Rectangle(20, 600, 300, 400)).ToString(), "False");
                Check("crop: outside the picture is refused", A(Rectangle.Inflate(span, 4, 4), CropKind.LootName, span, new Rectangle(0, 0, 500, 1080)).ToString(), "False");
                Check("crop: an icon square is allowed, a 400 px one not", $"{A(new Rectangle(10, 10, 64, 64), CropKind.LootIcon, new Rectangle(10, 10, 64, 64))} {A(new Rectangle(10, 10, 400, 400), CropKind.CardSlot, new Rectangle(10, 10, 400, 400))}", "True False");
                Check("crop: a reader's slot crop fits the slot plus the margin", $"{Crops.IsAllowedCut(new Size(141, 143), CropKind.CardSlot, new Size(133, 135))} {Crops.IsAllowedCut(new Size(160, 135), CropKind.CardSlot, new Size(133, 135))}", "True False");
                using var bmp = new Bitmap(1000, 1080);
                using var cut = Crops.Cut(bmp, CropKind.LootName, span);
                using var none = Crops.Cut(bmp, CropKind.ChatPanel, span);
                Check("crop: Cut takes the span plus the margin, and refuses the panel", $"{cut?.Width}x{cut?.Height} {(none == null ? "refused" : "cut")}", "228x38 refused");

                // crops kept with a pair: under the rule only
                var w5 = new LabelWriter(Path.Combine(dir, "crops"));
                w5.SetEnabled(true);
                using var good = new Bitmap(228, 38);
                using var big = new Bitmap(700, 400);
                var pr = w5.Write("loot-row", "Odyle x7", "f0f0", 1, "Odyle", 7, ["loot list", "chat"], new Size(2560, 1440), 18, t0,
                    new List<(string, Bitmap, CropKind, Size)> { ("name", good, CropKind.LootName, span.Size), ("icon", big, CropKind.LootIcon, new Size(48, 48)) });
                Check("crops: the name kept, an oversized icon not", pr == null ? "none" : $"{string.Join(",", pr.Crops)} {w5.CropPath(pr.Id, "name") != null} {w5.CropPath(pr.Id, "icon") != null}", "name True False");
            }

            // the upload batch's shape: a head line (app, install, count), then one pair per line, gzipped
            {
                var pairs = new List<LabelPair>
                {
                    new("0123456789abcdef", "kina-line", "Acquired 408 Kina.", null, null, 0, "Kina", 408, ["chat", "kinah counter"], "0.8.29", 2560, 1440, 18, "2026-10-24T18:00:02Z", []),
                    new("fedcba9876543210", "loot-row", "Odyle x7", "f0f0f0f0f0f0f0f0", "Odyle", 1, "Odyle", 7, ["loot list", "chat"], "0.8.29", 2560, 1440, 18, "2026-10-24T18:05:01Z", ["name", "icon"]),
                };
                var gz = LabelClient.BuildBatch("0.8.29", "0123456789abcdef0123456789abcdef", pairs);
                var (head, back) = LabelClient.ReadBatch(gz);
                Check("batch: gzip, a head line, one line per pair", $"{gz[0]:x2}{gz[1]:x2} {head?.app} {head?.install.Length} {head?.pairs} {back.Count}", "1f8b 0.8.29 32 2 2");
                var keys = string.Join(",", JsonDocument.Parse(JsonSerializer.Serialize(back[1])).RootElement.EnumerateObject().Select(p => p.Name));
                Check("batch: a pair's fields", keys, "id,kind,text,icon,dec,item,name,qty,wit,ver,sw,sh,tpx,t,crops");
                Check("batch: the pair round-trips", $"{back[1].Kind} {back[1].Text} {back[1].Decoded} {string.Join("+", back[1].Witnesses)} {string.Join("+", back[1].Crops)}", "loot-row Odyle x7 Odyle loot list+chat name+icon");
            }
        }
        catch (Exception ex) { Console.WriteLine("  help improve counting tests threw: " + ex); ok = false; }
        finally
        {
            LabelWriter.Shared = keepWriter;
            CrashLog.DirOverride = keepDir;
            try { Directory.Delete(dir, true); } catch (Exception) { }
        }
        return ok;
    }
}
