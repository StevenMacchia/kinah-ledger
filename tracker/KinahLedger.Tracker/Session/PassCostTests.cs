using System.Drawing;
using System.Drawing.Imaging;
using KinahLedger.Data;
using KinahLedger.Ocr;

namespace KinahLedger.Session;

/// <summary>
/// parse-test's checks for the cost of a pass (2026-10-26, branch tracker/pass-cost). On 2026-10-25 a pass took 504 ms
/// (median of 905 audit lines, 86 passes a minute), and two readers spent most of it on things not on screen: the
/// crafting panel's locator, 116 ms a pass in the open world against 29 ms while crafting (the look for the raised
/// gathering panel reads about 780,000 pixels with GetPixel when no panel is there), and the loot list, recognized every
/// pass however unchanged. Checked here: the locator gives the same answers from the capture's locked pixels as from
/// GetPixel (ocr.craft_pixels), with its time both ways printed; the list's unchanged check (ocr.list_skip_same, SameBand)
/// stands on the last read only for the same pixels, place and size, under 2 s, with no change on screen since; and the
/// locator's pace (ocr.craft_probe_slow, off in code) looks every pass for 10 s of time after a panel, never by passes.
/// The replay case tests/replays/text-2026-10-25-list-skip-same is the list check on a whole gather.
/// </summary>
public static class PassCostTests
{
    public static bool Run(ItemDictionary items, Settings settings)
    {
        bool ok = true;
        void Check(string what, string got, string expect)
        {
            bool pass = got == expect; ok &= pass;
            Console.WriteLine($"  {what,-62} -> {(got.Length > 60 ? got[..60] + "…" : got),-40} {(pass ? "ok" : "MISMATCH, expected " + expect)}");
        }
        Console.WriteLine("\nThe cost of a pass: the crafting panel's pixels, the loot list's unchanged check, the locator's pace (2026-10-26):");
        var keepCfg = Update.RemoteConfig.Current;
        try
        {
            // ---- the switches ----
            var b = Update.RemoteConfig.BuiltIn;
            var empty = Update.RemoteConfig.Parse("{}");
            Check("switches: craft_pixels and list_skip_same on, craft_probe_slow off", $"{b.CraftPixels} {b.ListSkipSame} {b.CraftProbeSlow} / {empty.CraftPixels} {empty.ListSkipSame} {empty.CraftProbeSlow}", "True True False / True True False");
            var flipped = Update.RemoteConfig.Parse("""{"ocr": {"craft_pixels": false, "list_skip_same": false, "craft_probe_slow": true}}""");
            Check("...the site can turn each the other way", $"{flipped.Flag("ocr.craft_pixels")} {flipped.Flag("ocr.list_skip_same")} {flipped.Flag("ocr.craft_probe_slow")}", "False False True");

            // ---- 1. the crafting panel's locator: locked pixels give what GetPixel gives ----
            var pixelsOff = Update.RemoteConfig.Parse("""{"ocr": {"craft_pixels": false}}""");
            var pixelsOn = Update.RemoteConfig.BuiltIn;
            string Show(CraftPanel.Sample s) => $"{s.Visible} {s.Success:0.000}/{s.Fail:0.000}{(s.Combo ? " combo" : "")}{(s.Raised ? " raised" : "")}";
            foreach (var (w, h) in new[] { (1920, 1080), (5120, 2160) })
            {
                var game = new Rectangle(0, 0, w, h);
                var region = CraftPanel.Region(game);
                int lift = (int)Math.Round(213 / 2160.0 * h);
                using var craft = ReplayBank.DrawPanel(region, game, (0.5, 0.3));
                // the raised (gathering) panel: the same strip drawn as if the region sat lower, so the panel shows higher in it
                using var raised = ReplayBank.DrawPanel(new Rectangle(region.X, region.Y + lift, region.Width, region.Height), game, (0.2, 0.6));
                // no panel over a busy world: random pixels, so the raised panel's look tries every place and finds nothing
                using var none = new Bitmap(region.Width, region.Height, PixelFormat.Format32bppArgb);
                var rng = new Random(11);
                var nd = none.LockBits(new Rectangle(0, 0, none.Width, none.Height), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
                var noise = new byte[nd.Stride * none.Height]; rng.NextBytes(noise);
                for (int i = 3; i < noise.Length; i += 4) noise[i] = 255;
                System.Runtime.InteropServices.Marshal.Copy(noise, 0, nd.Scan0, noise.Length); none.UnlockBits(nd);
                // the same three in 24-bit pixels (a format GetPixel and the locked read both take)
                using var craft24 = craft.Clone(new Rectangle(0, 0, craft.Width, craft.Height), PixelFormat.Format24bppRgb);
                string All(Update.RemoteConfig cfg, out double noneMs)
                {
                    Update.RemoteConfig.Current = cfg;
                    var sw = System.Diagnostics.Stopwatch.StartNew();
                    var n = CraftPanel.Measure(none, region, game);
                    noneMs = sw.Elapsed.TotalMilliseconds;
                    return $"{Show(CraftPanel.Measure(craft, region, game))} | {Show(CraftPanel.Measure(raised, region, game))} | {Show(n)} | {Show(CraftPanel.Measure(craft24, region, game))}";
                }
                var slow = All(pixelsOff, out var slowMs);
                var fast = All(pixelsOn, out var fastMs);
                Check($"{w}x{h}: crafting, raised, none, 24-bit: same answers", fast, slow);
                Check($"...the crafting panel is up", $"{fast.StartsWith("True ")}", "True");
                Console.WriteLine($"      no panel on {w}x{h} ({region.Width}x{region.Height} strip): GetPixel {slowMs:0.0} ms, locked pixels {fastMs:0.0} ms");
                // a whole game window, as the scene's test frames are
                using var whole = ReplayBank.DrawPanel(game, game, (0.9, 0.1));
                Update.RemoteConfig.Current = pixelsOff; string wSlow = $"{CraftPanel.IsUp(whole)} {Show(CraftPanel.Measure(whole))}";
                Update.RemoteConfig.Current = pixelsOn; string wFast = $"{CraftPanel.IsUp(whole)} {Show(CraftPanel.Measure(whole))}";
                Check($"...a whole {w}x{h} window: the same both ways", wFast, wSlow);
            }
            Update.RemoteConfig.Current = Update.RemoteConfig.BuiltIn;

            // ---- 2. the loot list's unchanged check ----
            var t0 = new DateTime(2026, 10, 25, 12, 0, 0, DateTimeKind.Utc);
            var sb = new SameBand<string>();
            var at = new Point(100, 200); var size = new Size(300, 400);
            var lines = new List<string> { "Orichalcum Ore x1" };
            var never = DateTime.MinValue;
            string Ask(ulong hash, double s, Point? o = null, Size? z = null, DateTime? changed = null) =>
                sb.Same(hash, o ?? at, z ?? size, 3, t0.AddSeconds(s), changed ?? never) is { } k ? string.Join("|", k) : "read";
            Check("nothing read yet: recognized", Ask(1, 0), "read");
            sb.Recognized(1, at, size, 3, t0, lines);
            Check("the same pixels 0.5 s later: the last read's lines", Ask(1, 0.5), "Orichalcum Ore x1");
            sb.Same(1, at, size, 3, t0.AddSeconds(0.6), never)!.Clear();
            Check("...a returned copy can't change what is kept", Ask(1, 0.7), "Orichalcum Ore x1");
            Check("other pixels: recognized", Ask(2, 0.5), "read");
            Check("the same pixels at another place or size: recognized", $"{Ask(1, 0.5, new Point(101, 200))} {Ask(1, 0.5, z: new Size(300, 401))}", "read read");
            Check("the same pixels 2 s after the read: recognized (forced)", $"{Ask(1, 1.99)} / {Ask(1, 2.0)}", "Orichalcum Ore x1 / read");
            Check("the chat, target or counter changed after the read: recognized", $"{Ask(1, 1.0, changed: t0.AddSeconds(0.4))} / {Ask(1, 1.0, changed: t0)}", "read / Orichalcum Ore x1");
            Check("a clock that went back: recognized", Ask(1, -0.5), "read");
            sb.Forget();
            Check("forgotten (the switch off): recognized", Ask(1, 0.5), "read");
            Check("...asked and stood on, counted for the audit", $"{sb.Skips} of {sb.Reads}", "5 of 13");

            // the session's side: new chat lines, another target, a counter that moved are changes; the same again is not
            {
                var s = new TrackerSession(null, settings, items, "Replay");
                var c0 = s.ScreenChangedAt;
                s.ChatGated(new List<string> { "[Player] chat a1" }, t0, Gate.Hold);
                var c1 = s.ScreenChangedAt;
                s.ChatGated(new List<string> { "[Player] chat a1" }, t0.AddSeconds(1), Gate.Hold);
                var c2 = s.ScreenChangedAt;
                s.TraceTarget("Kukuru", t0.AddSeconds(2)); var c3 = s.ScreenChangedAt;
                s.TraceTarget("Kukuru", t0.AddSeconds(3)); var c4 = s.ScreenChangedAt;
                Check("session: new chat lines and another target are changes", $"{c0 == DateTime.MinValue} {c1 == t0} {c2 == t0} {c3 == t0.AddSeconds(2)} {c4 == t0.AddSeconds(2)}", "True True True True True");
                s.CheckBalance(1000, t0.AddSeconds(4)); var c5 = s.ScreenChangedAt;
                s.CheckBalance(1000, t0.AddSeconds(5)); var c6 = s.ScreenChangedAt;
                s.CheckBalance(1200, t0.AddSeconds(6)); var c7 = s.ScreenChangedAt;
                Check("...the Kinah counter: a change only when it moved", $"{c5 == t0.AddSeconds(4)} {c6 == t0.AddSeconds(4)} {c7 == t0.AddSeconds(6)}", "True True True");
                // a trace's list reads with fingerprints, as the replay bank feeds them
                var row = new List<LootLine> { new(400, "Orichalcum Ore x1", 0.85) };
                var t9 = t0.AddSeconds(10);
                var r1 = s.TraceListRead("A", new List<LootLine>(), t9);
                var r2 = s.TraceListRead("A", new List<LootLine>(), t9.AddSeconds(0.4));
                var r3 = s.TraceListRead("B", row, t9.AddSeconds(0.8));
                var r4 = s.TraceListRead("B", row, t9.AddSeconds(1.2));
                Check("...trace reads: unchanged stands, a new row is read at once", $"{r1.Count} {r2.Count} {r3.Count} {r4.Count} skips {s.ListSameSkips}", "0 0 1 1 skips 2");
            }

            if (ReplayBank.OcrAvailable())
            {
                // the live read: a loot list picture recognized once, the same picture again stands on it, another is read
                var game = new Rectangle(0, 0, 2560, 1440);
                var region = LootPopup.Region(game);
                using var one = ReplayBank.DrawLoot(region, game, new[] { "Orichalcum Ore x2" });
                using var two = ReplayBank.DrawLoot(region, game, new[] { "Orichalcum Ore x2", "Fine Orichalcum Ore x1" });
                var band = new SameBand<OcrLineBox>();
                string Read(Bitmap bmp, double s) => string.Join("|", Task.Run(() => ScreenReader.ReadBoxesAsync(bmp, region.Location, 3, band, t0.AddSeconds(s), DateTime.MinValue)).GetAwaiter().GetResult().Select(l => $"{l.Text}@{l.Box.Top}"));
                var plain = string.Join("|", Task.Run(() => ScreenReader.ReadBoxesAsync(one, region.Location, 3, boost: true)).GetAwaiter().GetResult().Select(l => $"{l.Text}@{l.Box.Top}"));
                var a = Read(one, 0);
                Check("live read: the same lines as the plain read", $"{a == plain} {band.Skips} of {band.Reads}", "True 0 of 1");
                var a2 = Read(one, 0.5);
                Check("...the same picture again: not recognized, the same lines", $"{a2 == a} {band.Skips} of {band.Reads}", "True 1 of 2");
                var c = Read(two, 1.0);
                Check("...a row more: recognized at once", $"{c != a} {c.Contains("Fine")} {band.Skips} of {band.Reads}", "True True 1 of 3");
            }
            else Console.WriteLine("  (no text recognition here: the live list read is checked where Windows OCR is installed)");

            // ---- 3. the locator's pace (ocr.craft_probe_slow) ----
            {
                var pc = new ProbeCadence();
                var notes = new List<string>();
                // the passes at these times (seconds), each asked once: the ones that look, and each switch logged
                string Run(double[] times, bool on, Func<double, string?>? why = null)
                {
                    var looks = new List<string>();
                    foreach (var s in times)
                    {
                        if (pc.Due(t0.AddSeconds(s), on, why?.Invoke(s), out var note)) looks.Add($"{s:0.0}");
                        if (note != null) notes.Add($"{s:0.0}: {(note.Contains("about once a second") ? "slow" : "full")}");
                    }
                    return string.Join(" ", looks);
                }
                Check("switch off: a look every pass, nothing logged", $"{Run(new[] { 0, 0.4, 0.8, 1.2, 1.6 }, false)} / {notes.Count}", "0.0 0.4 0.8 1.2 1.6 / 0");
                notes.Clear();
                Check("switch on, nothing to watch: about once a second", Run(new[] { 2.0, 2.4, 2.8, 3.2, 3.6, 4.0, 4.4, 4.8 }, true), "2.8 4.0");
                Check("...the switch logged once", string.Join(", ", notes), "2.0: slow");
                // a look sees a panel at 5.1: every pass for 10 s of time, however far apart the passes, then slow again
                notes.Clear();
                bool look51 = pc.Due(t0.AddSeconds(5.1), true, null, out var n51);
                Check("a look 1.1 s after the last one", $"{look51} {n51 == null}", "True True");
                pc.Seen(t0.AddSeconds(5.1));
                Check("a panel seen at 5.1: every pass to 15.1 (time, not passes)", Run(new[] { 5.5, 6.5, 9.0, 12.0, 15.0, 15.2, 15.4, 16.3, 16.9, 17.4 }, true), "5.5 6.5 9.0 12.0 15.0 16.3 17.4");
                Check("...each switch logged once", string.Join(", ", notes), "5.5: full, 15.2: slow");
                // a reason to watch closely (the scene is Crafting, a gathering sign): every pass from that pass
                notes.Clear();
                pc = new ProbeCadence();
                Check("a gathering sign from 1.6: every pass from that pass", Run(new[] { 0, 0.4, 0.8, 1.3, 1.7, 2.1, 2.5 }, true, s => s >= 1.6 ? "a gathering sign was seen" : null), "0.0 1.3 1.7 2.1 2.5");
                Check("...the switches logged once each", string.Join(", ", notes), "0.0: slow, 1.7: full");
                Check("...passes and looks counted", $"{pc.Looks} of {pc.Passes}", "5 of 7");
            }
        }
        finally { Update.RemoteConfig.Current = keepCfg; }
        return ok;
    }
}
