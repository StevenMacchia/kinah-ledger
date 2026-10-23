using KinahLedger.Data;
using KinahLedger.Ocr;
using KinahLedger.Session;
using KinahLedger.Ui;
using KinahLedger.Upload;

namespace KinahLedger;

internal static class Program
{
    [System.Runtime.InteropServices.DllImport("kernel32.dll")] static extern bool AttachConsole(int pid);
    [System.Runtime.InteropServices.DllImport("kernel32.dll")] static extern bool AllocConsole();
    [System.Runtime.InteropServices.DllImport("kernel32.dll")] static extern IntPtr GetStdHandle(int which);

    [STAThread]
    static int Main(string[] args)
    {
        // the installer's own runs (install, update, uninstall) end inside; a normal start returns at once
        Platform.Installer.RunHooks();
        var settings = Settings.Load();
        Ocr.ScreenReader.UseWindowCapture = settings.Ocr.WindowCapture;
        // a kinahledger:// link from the site opens (or brings up) the window
        if (args.Length > 0 && Platform.Installer.IsLink(args[0])) args = Array.Empty<string>();
        if (args.Length == 0 || args[0] is Platform.AutoStart.BackgroundArg or Ui.MainWindow.AfterUpdateArg)
        {
            // double-clicked, started with Windows (in the tray), or restarted by an update: open the window.
            // The site's reading settings as last fetched (Update/RemoteConfig.cs) apply first; settings.json's own values win.
            Update.RemoteConfig.Current = Update.RemoteConfig.LoadKept();
            try { var sp = Path.Combine(AppContext.BaseDirectory, "settings.json"); Update.RemoteConfig.Current.ApplyTo(settings, File.Exists(sp) ? File.ReadAllText(sp) : null); }
            catch (Exception ex) { CrashLog.Write("remote config", ex); }
            return WpfApp.Run(settings, background: args.Contains(Platform.AutoStart.BackgroundArg), afterUpdate: args.Contains(Ui.MainWindow.AfterUpdateArg));
        }
        // command line: write to the terminal that started us; if there's none and no pipe either, open one
        if (!AttachConsole(-1) && GetStdHandle(-11) == IntPtr.Zero) AllocConsole();
        var cmd = args[0].ToLowerInvariant();
        var (opts, pos) = ParseArgs(args.Skip(1));
        if (opts.TryGetValue("process", out var hints))
            settings.ProcessHints = hints.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        // test and measuring commands log to a temp folder, never into the player's own reads.log
        if (cmd is "data-update" or "data-check" or "capture-test" or "calib-shot" or "bench" or "replay-bank")
            CrashLog.DirOverride = Path.Combine(Path.GetTempPath(), "KinahLedger-test-logs");
        try
        {
            switch (cmd)
            {
                case "pick": return Pick();
                case "locate": return Locate(settings);
                case "data-check":
                {
                    // each built-in data file's hash beside data/manifest.json's (path as the first argument): equal means a
                    // tracker with this build downloads nothing until the data changes
                    var man = KinahLedger.Data.DataFiles.Parse(File.ReadAllText(pos.FirstOrDefault() ?? "data/manifest.json"));
                    int bad = 0;
                    foreach (var n in KinahLedger.Data.DataFiles.Names)
                    {
                        var b = KinahLedger.Data.DataFiles.BuiltInHash(n); var m = man.Files.GetValueOrDefault(n);
                        Console.WriteLine($"{n,-22} {(b == m ? "same" : "DIFFERENT")}  built-in {b?[..12]}  manifest {m?[..12]}");
                        if (b != m) bad++;
                    }
                    return bad == 0 ? 0 : 1;
                }
                case "data-update":
                {
                    // one data update against another address and folder (a local test server): what came, and what is read after
                    if (opts.TryGetValue("from", out var from)) KinahLedger.Data.DataFiles.FileUrl = from.TrimEnd('/') + "/";
                    if (opts.TryGetValue("dir", out var ddir)) KinahLedger.Data.DataFiles.DirOverride = ddir;
                    using var dhttp = new HttpClient();
                    var got = Task.Run(() => KinahLedger.Data.DataFiles.UpdateAsync(dhttp)).GetAwaiter().GetResult();
                    Console.WriteLine("updated: " + (got.Count == 0 ? "nothing" : string.Join(", ", got)));
                    using var zs = KinahLedger.Data.DataFiles.Open("zones.txt");
                    using var zr = new StreamReader(zs!);
                    var zl = zr.ReadToEnd().Split((char)10).Select(x => x.Trim()).Where(x => x.Length > 0 && !x.StartsWith('#')).ToList();
                    Console.WriteLine($"zones.txt now read: {zl.Count} names, last: {zl.LastOrDefault()}");
                    return 0;
                }
                case "calib-shot": { var cdir = pos.FirstOrDefault() ?? "."; WpfApp.ShotCalibration(settings, cdir); Console.WriteLine($"wrote {Path.GetFullPath(cdir)}"); return 0; }
                case "capture-test":
                {
                    // the chat area two ways: a screen copy (what is on top) and the game window's own pixels
                    var cg = GameWindow.Find(settings.ProcessHints) ?? throw new InvalidOperationException("game window not found");
                    var cr = Task.Run(() => ChatLocator.DetectAsync(cg)).GetAwaiter().GetResult().region ?? new System.Drawing.Rectangle(cg.Left + 100, cg.Bottom - 700, 860, 600);
                    var cdir2 = pos.FirstOrDefault() ?? ".";
                    using (var sc = ScreenReader.Capture(cr)) sc.Save(Path.Combine(cdir2, "capture-screen.png"));
                    WindowCapture.Track(GameWindow.Handle);
                    System.Drawing.Bitmap? wc = null;
                    for (int i = 0; i < 40 && wc == null; i++) { Thread.Sleep(100); wc = WindowCapture.TryCapture(cr); }
                    if (wc == null) { Console.WriteLine("window capture: no frame"); return 1; }
                    var sw3 = System.Diagnostics.Stopwatch.StartNew(); int n3 = 0;
                    while (sw3.ElapsedMilliseconds < 2000) { using var x = WindowCapture.TryCapture(cr); n3++; }
                    wc.Save(Path.Combine(cdir2, "capture-window.png"));
                    Console.WriteLine($"window capture works: {cr.Width}x{cr.Height} crops at {n3 / 2.0:0} a second; images in {Path.GetFullPath(cdir2)}");
                    WindowCapture.Stop();
                    return 0;
                }
                case "bench": return Platform.Bench.Run(settings, int.TryParse(pos.FirstOrDefault(), out var bn) ? bn : 15);
                case "in-view": return InView(settings);
                case "ocr-file": return OcrFile(pos, opts);
                case "ui-shot": return UiShot(settings, opts, pos);
                case "ui-cycle": return WpfApp.Cycle(settings, opts.ContainsKey("auto") ? null : RegionFor(opts), int.TryParse(opts.GetValueOrDefault("ms"), out var ms) ? ms : 4000);
                case "ui-live": return WpfApp.Live(settings, opts.ContainsKey("auto") ? null : RegionFor(opts), int.TryParse(opts.GetValueOrDefault("ms"), out var lms) ? lms : 4000);
                case "parse-test": return ParseTest(settings);
                case "accuracy-test": return Session.AccuracyTests.Run(settings);
                case "accuracy-sample": return Session.AccuracyTests.Sample(settings, pos, opts);
                case "replay-bank": return ReplayBankCmd(settings, pos, opts);
                case "balance-test": return BalanceTest(settings, opts);
                case "options-test": return WpfApp.OptionsSelfTest(settings);
                case "market-test": return MarketTest(settings, opts);
                case "market-shot": opts["demo"] = "true"; opts["market"] = "true"; return UiShot(settings, opts, pos);
                case "market-watch": return MarketWatch(settings, opts);
                case "ocr-test": return OcrTest(settings, opts);
                case "target-test": return TargetTest(opts);
                case "popup-test":
                {
                    // a screenshot of the whole game (--image): the loot list's region and the rows read from it
                    using var bmp = Task.Run(() => ScreenReader.LoadAsync(opts["image"])).GetAwaiter().GetResult();
                    var game = new System.Drawing.Rectangle(0, 0, bmp.Width, bmp.Height);
                    // --crop: the image is the list's region already
                    var reg = opts.ContainsKey("crop") ? game : LootPopup.Region(game);
                    using var crop = bmp.Clone(reg, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
                    if (opts.ContainsKey("save")) crop.Save("popup-crop.png");
                    int sc = int.TryParse(opts.GetValueOrDefault("scale"), out var scv) ? scv : 3; var sw = System.Diagnostics.Stopwatch.StartNew(); var lines = Task.Run(() => ScreenReader.ReadBoxesAsync(crop, reg.Location, sc, boost: true)).GetAwaiter().GetResult(); Console.WriteLine($"read in {sw.ElapsedMilliseconds} ms at scale {sc}");
                    Console.WriteLine($"region {reg}");
                    foreach (var l in lines.OrderBy(l => l.Box.Top)) Console.WriteLine($"  read: {l.Text}");
                    foreach (var r in LootPopup.Rows(lines.Select(l => (l.Box.Top, l.Text)))) Console.WriteLine($"  row: {(r.IsKinah ? "Kinah " + r.Amount : r.Name + " x" + r.Count)}");
                    PopupIconsReport(ItemDictionary.LoadOrEmpty(settings.ItemsFile), crop, reg.Location, lines);
                    return 0;
                }
                case "replay": return Replay(settings, pos, opts);
                case "report-replay": return ReportReplay(settings, pos);
                case "icon-index":
                {
                    // every distinct item icon, fingerprinted for the reward grid: icon-index [tw-items.json] [out.bin] [cache dir]
                    var src = pos.Count > 0 ? pos[0] : Path.Combine("..", "..", "data", "tw-items.json");
                    var outBin = pos.Count > 1 ? pos[1] : Path.Combine("..", "..", "data", "icon-index.bin");
                    var cache = pos.Count > 2 ? pos[2] : Path.Combine(Path.GetTempPath(), "kinah-icons");
                    using var doc = System.Text.Json.JsonDocument.Parse(File.ReadAllText(src));
                    var arr = doc.RootElement.ValueKind == System.Text.Json.JsonValueKind.Array ? doc.RootElement : doc.RootElement.GetProperty("items");
                    var urls = arr.EnumerateArray().Select(e => e.TryGetProperty("icon", out var ic) ? ic.GetString() ?? "" : "").Where(u => u.Length > 0).ToList();
                    // the currencies and reward items the list lacks (data/extra-items.json), when it is there
                    var extra = opts.GetValueOrDefault("extra", Path.Combine(Path.GetDirectoryName(src) ?? ".", "extra-items.json"));
                    if (File.Exists(extra))
                    {
                        using var xdoc = System.Text.Json.JsonDocument.Parse(File.ReadAllText(extra));
                        urls.AddRange(xdoc.RootElement.GetProperty("items").EnumerateArray().Select(e => e.TryGetProperty("icon", out var ic) ? ic.GetString() ?? "" : "").Where(u => u.Length > 0));
                    }
                    // reward slots as the game draws them, from saved pictures: data/icon-slots.txt, "key|picture|x,y,size" per line
                    var slotRefs = new List<IconIndex.Entry>();
                    var slotsFile = Path.Combine(Path.GetDirectoryName(src) ?? ".", "icon-slots.txt");
                    if (File.Exists(slotsFile))
                        foreach (var line in File.ReadAllLines(slotsFile).Where(l => l.Trim().Length > 0 && !l.StartsWith('#')))
                        {
                            var parts = line.Split('|'); var xy = parts[2].Split(',').Select(int.Parse).ToArray();
                            var pic = Path.Combine(Path.GetDirectoryName(slotsFile) ?? ".", parts[1].Trim());   // pictures kept beside the list, in data/icon-slots/
                            if (!File.Exists(pic)) { Console.Error.WriteLine($"no picture for {parts[0]}: {pic}"); continue; }
                            using var bmpPic = new System.Drawing.Bitmap(pic);
                            slotRefs.Add(IconIndex.FromSlot(parts[0].Trim(), bmpPic, new System.Drawing.Rectangle(xy[0], xy[1], xy[2], xy[2])));
                        }
                    int n = IconIndex.BuildAsync(urls, cache, outBin, slotRefs).GetAwaiter().GetResult();
                    Console.WriteLine($"wrote {n} icons to {Path.GetFullPath(outBin)}");
                    return 0;
                }
                case "plate-test":
                {
                    // plate-test <frame> [frame ...]: the enemies with a health bar in each saved game frame
                    foreach (var fp in pos)
                    {
                        using var pf = new System.Drawing.Bitmap(fp);
                        var sw = System.Diagnostics.Stopwatch.StartNew();
                        var bars = Nameplates.FindBars(pf); var found = sw.ElapsedMilliseconds;
                        var plates = Nameplates.ReadAsync(pf).GetAwaiter().GetResult();
                        Console.WriteLine($"{Path.GetFileName(fp)}: {bars.Count} bars ({found} ms), {plates.Count} enemies ({sw.ElapsedMilliseconds} ms): " + string.Join(" | ", plates.Select(x => $"{x.Enemy} @{x.Bar.X},{x.Bar.Y} [{x.Read}]")) + (bars.Count > plates.Count ? "   unnamed bars: " + string.Join(" ", bars.Where(b => !plates.Any(x => x.Bar == b)).Select(b => $"{b.X},{b.Y} {b.Width}x{b.Height}")) : ""));
                    }
                    return 0;
                }
                case "grid-run":
                {
                    // grid-run --image <file> --zone <expedition> [chat lines...]: the reward table into a fresh run, after the chat lines given
                    var gi = ItemDictionary.LoadOrEmpty(settings.ItemsFile);
                    CrashLog.DirOverride = Path.Combine(Path.GetTempPath(), "kinah-grid-run"); Directory.CreateDirectory(CrashLog.DirOverride);
                    File.WriteAllText(Path.Combine(CrashLog.DirOverride, "reads.log"), ""); File.Delete(Path.Combine(CrashLog.DirOverride, "instances.jsonl"));
                    var gs = new TrackerSession(null, settings, gi, "Test"); var zone = opts.GetValueOrDefault("zone", "Urugugu Canyon"); var g0 = DateTime.UtcNow;
                    gs.Engine.SetZone(zone); gs.ZoneView.SetZone(zone); gs.Instances.Zone(zone, g0);
                    if (opts.TryGetValue("balance", out var bal)) { var bv = bal.Split(',').Select(long.Parse).ToArray(); gs.SeedBalance(g0, bv[0]); gs.SeedBalance(g0.AddSeconds(2), bv[1]); }
                    gs.ProcessChat(new List<string> { "[x] start" }, g0);
                    if (pos.Count > 0) gs.ProcessChat(new List<string> { "[x] start" }.Concat(pos).ToList(), g0.AddSeconds(1));
                    using var gf = new System.Drawing.Bitmap(opts["image"]);
                    var gband = new System.Drawing.Rectangle(gf.Width / 4, 0, gf.Width / 2, gf.Height / 3);
                    using var gbb = gf.Clone(gband, gf.PixelFormat);
                    var glines = ScreenReader.ReadBoxesAsync(gbb, gband.Location, 1).GetAwaiter().GetResult();
                    var grc = glines.First(l => l.Text.Contains("collected", StringComparison.OrdinalIgnoreCase));
                    gs.ApplyGrid(RewardGrid.ReadAsync(gf, grc.Box.Bottom + (int)(gf.Height * 0.016)).GetAwaiter().GetResult(), g0.AddSeconds(3));
                    var grun = gs.Instances.Snapshot().LastOrDefault();
                    Console.WriteLine($"run {grun?.Zone}: {grun?.Kinah:N0} Kinah; " + string.Join(", ", (grun?.Items ?? new()).Select(kv => $"{gi.NameOf(kv.Key)} x{kv.Value}")));
                    foreach (var l in File.ReadAllLines(Path.Combine(CrashLog.DirOverride, "reads.log")).Where(l => l.Contains("reward table"))) Console.WriteLine("  " + l);
                    return 0;
                }
                case "grid-test":
                {
                    // the expedition reward grid in a saved picture: grid-test --image <file> [--top <y>]
                    if (!opts.TryGetValue("image", out var gimg)) { Console.Error.WriteLine("grid-test --image <file>"); return 1; }
                    using var frame = new System.Drawing.Bitmap(gimg);
                    int top;
                    if (opts.TryGetValue("top", out var ts)) top = int.Parse(ts);
                    else
                    {
                        var band = new System.Drawing.Rectangle(frame.Width / 4, 0, frame.Width / 2, frame.Height / 3);
                        using var bandBmp = frame.Clone(band, frame.PixelFormat);
                        var lines = ScreenReader.ReadBoxesAsync(bandBmp, band.Location, 1).GetAwaiter().GetResult();
                        var rc = lines.FirstOrDefault(l => l.Text.Contains("collected", StringComparison.OrdinalIgnoreCase));
                        if (rc == null) { Console.WriteLine("no \"Reward collected\" in the top third: " + string.Join(" | ", lines.Select(l => l.Text))); return 1; }
                        top = rc.Box.Bottom + (int)(frame.Height * 0.016);
                    }
                    var gitems = ItemDictionary.LoadOrEmpty(settings.ItemsFile);
                    var byIcon = gitems.AllWithIcons().GroupBy(i => IconIndex.KeyOf(i.Icon)).ToDictionary(g => g.Key, g => g.ToList());
                    var slots = RewardGrid.ReadAsync(frame, top).GetAwaiter().GetResult();
                    Console.WriteLine($"{slots.Count} slots from y {top} ({IconIndex.All.Count} icons in the index)");
                    foreach (var sl in slots)
                    {
                        var best = sl.Icons.FirstOrDefault();
                        var names = best.Key != null && byIcon.TryGetValue(best.Key, out var its) ? its.Where(i => i.Grade == sl.Grade).Select(i => i.Name).Distinct().Take(3).ToList() : new List<string>();
                        if (names.Count == 0 && best.Key != null && byIcon.TryGetValue(best.Key, out var any)) names = any.Select(i => i.Name + " [" + i.Grade + "]").Distinct().Take(2).ToList();
                        Console.WriteLine($"  {sl.Grade,-8} x{sl.Count,-8} [{sl.CountText,-7}] {best.Key} ({best.Distance:0.0}; next {(sl.Icons.Count > 1 ? sl.Icons[1].Distance : 0):0.0})  {string.Join(" / ", names)}");
                    }
                    return 0;
                }
                case "craft-test":
                {
                    // the crafting panel over saved pictures, in name order: craft-test --dir <folder> [--glob *crafting.jpg]
                    if (!opts.TryGetValue("dir", out var cdir)) { Console.Error.WriteLine("craft-test --dir <folder> [--glob <pattern>]"); return 1; }
                    var ctr = new CraftPanelTracker(); bool cread = false;
                    foreach (var cf in Directory.GetFiles(cdir, opts.TryGetValue("glob", out var cg) ? cg : "*.jpg").OrderBy(p => p))
                    {
                        using var cfull = new System.Drawing.Bitmap(cf);
                        // as the session reads it: only the panel's strip, cut from a game window placed off the screen's origin
                        var cgame = new System.Drawing.Rectangle(100, 50, cfull.Width, cfull.Height);
                        var carea = CraftPanel.Region(cgame);
                        using var cbmp = cfull.Clone(new System.Drawing.Rectangle(carea.X - cgame.X, carea.Y - cgame.Y, carea.Width, carea.Height), cfull.PixelFormat);
                        var cs = CraftPanel.Measure(cbmp, carea, cgame);
                        var co = ctr.Feed(File.GetLastWriteTime(cf), cs);
                        string cnote = co == null ? "" : $"  => {(co.Ok ? "SUCCESS" : "FAIL")}{(co.Combo ? " (combo)" : "")}";
                        if (cs.Visible && (!cread || co != null))
                        {
                            var ct = CraftPanel.ReadTextAsync(cbmp, carea, cgame).GetAwaiter().GetResult();
                            Console.WriteLine("  read: " + string.Join(" | ", CraftPanel.LastLines));
                            if (ct != null) { cnote += $"  [{ct.Value.Done} / {ct.Value.Of}, {CraftPanel.ItemOf(ct.Value.Recipe)}]"; cread = true; }
                        }
                        Console.WriteLine($"{Path.GetFileName(cf)}  up={cs.Visible,-5} success={cs.Success:0.00} fail={cs.Fail:0.00} combo={cs.Combo}{cnote}");
                    }
                    return 0;
                }
                case "parse-lines":
                {
                    // each argument is one chat line: what the parser makes of it, for checking a line from reads.log
                    var pitems = ItemDictionary.LoadOrEmpty(settings.ItemsFile);
                    var pp = new ChatParser(pitems);
                    foreach (var line in pos)
                    {
                        var evs = pp.Parse(line, DateTime.UtcNow).Select(e => Describe(e, pitems)).ToList();
                        Console.WriteLine($"{line,-45} -> {(evs.Count == 0 ? "(nothing)" : string.Join("; ", evs))}   [cleaned: {ChatParser.CleanSystemLine(line)}]");
                    }
                    return 0;
                }
                case "watch": return Watch(settings, opts);
                case "update": return UpdateCmd(settings, opts);
                case "reset-id":
                    InstallId.Reset();
                    Console.WriteLine("Install ID removed. A new one is created on the next upload.");
                    return 0;
                case "help": Help(); return 0;
                default:
                    Console.Error.WriteLine($"unknown command '{cmd}'");
                    Help();
                    return 1;
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("error: " + ex.Message);
            return 2;
        }
    }

    static void Help() => Console.WriteLine(
$@"Kinah Ledger tracker {RunPayload.AppVersion}

Screen reading (works today; the chat, the zone and the Market are found on their own):
  locate                        find the game window, the chat, the zone and the Market; saves an annotated frame
  in-view                       which parts of the game can be read now, and which window covers the others (reads nothing)
  pick                         drag a box around the chat's loot lines; remembered for next time
  ocr-test [--save]             read the box once and show what was recognized (--save keeps a PNG)
  ocr-test --scales [--image p] read the chat (or a saved picture of it) at each enlarging factor, to see which reads best
  popup-test --image shot.png   read the loot list beside the character in a game picture, with each row's icon match (--crop, --save, --scale n)
  replay [trace.log]            count a recorded fight's reads again (a miscount report's trace.jsonl too; --from/--to HH:mm, --novote)
  report-replay <folder>        read a miscount report's pictures again with this version's reader, beside what was counted then
  ocr-file <image> [--scale 2]  run recognition on a screenshot, plain and boosted
  watch [--activity name]       a farming run in the console, with the Market reader beside it; Ctrl+C to stop
        [--region x,y,w,h] [--interval 700] [--server ""Siel (Elyos)""] [--no-market]
  market-watch [--server ..]    only the Market reader, as the window runs it in the background; Ctrl+C to stop
  parse-test                    self-test of the chat-line parser, the 0.7 reads and the balance counter logic
  balance-test [--save]         read the currency counters at the top-left once and list the numbers (--save keeps the crop)
  options-test                  self-test of the Options window: changes, Save, Cancel (saves nothing)
  market-test                   self-test of the Market row parser
  market-test --image shot.png  find the Market in a screenshot and read its rows
        [--game x,y,w,h] [--region x,y,w,h]
  ui-shot [dir] [--demo] [--gathering|--market|--crafting] [--compact|--full] [--welcome] [--skin night|abyss|daylight]
                                render the window to PNG files for layout review (--options: every Options page)
  market-shot [dir]             the window with the Market card in its tracking state
  reset-id                      replace the random install ID used for uploads
  update [--check]              install the newest version from kinahledger.com now (--check only says whether there is one)
        [--manifest url]        read another version file (a local test server)

The tracker only ever reads the screen: it never captures network traffic, reads game memory or sends input to the game.

options: --process aion[,other]");

    static (Dictionary<string, string>, List<string>) ParseArgs(IEnumerable<string> args)
    {
        var o = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var p = new List<string>();
        string? key = null;
        foreach (var s in args)
        {
            if (s.StartsWith("--")) { if (key != null) o[key] = "true"; key = s[2..]; }
            else if (key != null) { o[key] = s; key = null; }
            else p.Add(s);
        }
        if (key != null) o[key] = "true";
        return (o, p);
    }

    /// <summary>The window's update, from the command line: check, download, verify, swap. The new version runs from the next start.</summary>
    static int UpdateCmd(Settings s, Dictionary<string, string> o)
    {
        var manifest = o.GetValueOrDefault("manifest") is { Length: > 0 } m && m != "true" ? m : s.Update.Manifest;
        var r = Task.Run(() => Update.Updater.CheckAsync(manifest)).GetAwaiter().GetResult();
        Console.WriteLine(r.Ok ? r.Note : "Couldn't check: " + r.Note);
        if (!r.Ok) return 1;
        if (r.Newer == null || o.ContainsKey("check")) return 0;
        var exe = Update.Updater.ExePath ?? throw new InvalidOperationException("not running as KinahLedger.exe, so there is nothing to replace");
        Console.WriteLine($"Downloading {r.Newer.Url}");
        int shown = -1;
        var progress = new Progress<double>(f => { int p = (int)(f * 10); if (p > shown) { shown = p; Console.Write($" {p * 10}%"); } });
        try
        {
            var staged = Task.Run(() => Update.Updater.DownloadAsync(r.Newer, exe, progress)).GetAwaiter().GetResult();
            Console.WriteLine();
            Update.Updater.Apply(staged, exe);
        }
        catch (Update.UpdateException ex) { Console.WriteLine(); Console.Error.WriteLine("Update failed: " + ex.Message); return 1; }
        Console.WriteLine($"Updated to {Update.Updater.Show(r.Newer.Version)}, checksum verified. Start Kinah Ledger again to use it.");
        return 0;
    }

    /* ---------------- screen reading ---------------- */

    static int Pick()
    {
        Console.WriteLine("Drag a box around the chat lines that show loot and Kinah. Esc cancels.");
        var r = WpfApp.PickRegion();
        if (r == null) { Console.WriteLine("Cancelled."); return 1; }
        RegionStore.Save(r.Value);
        Console.WriteLine($"Saved chat region {r.Value.X},{r.Value.Y},{r.Value.Width},{r.Value.Height}. Next: `ocr-test` to check it reads, then `watch`.");
        return 0;
    }

    /// <summary>Runs recognition on an image file, plain and boosted, to compare what each sees. For tuning against real screenshots.</summary>
    static int OcrFile(List<string> pos, Dictionary<string, string> o)
    {
        if (pos.Count == 0) throw new ArgumentException("usage: ocr-file <image> [--scale 2]");
        int scale = int.TryParse(o.GetValueOrDefault("scale"), out var sc) ? sc : 2;
        using var bmp = Task.Run(() => ScreenReader.LoadAsync(pos[0])).GetAwaiter().GetResult();
        Console.WriteLine($"{pos[0]}: {bmp.Width} × {bmp.Height}");
        foreach (var boost in new[] { false, true })
        {
            var lines = Task.Run(() => ScreenReader.ReadBitmapAsync(bmp, boost, scale)).GetAwaiter().GetResult();
            Console.WriteLine($"== {(boost ? "boosted" : "plain")}: {lines.Count} line(s)");
            foreach (var l in lines) Console.WriteLine("  " + l);
        }
        if (o.ContainsKey("save")) { using var b = ScreenReader.Boost(bmp); b.Save("boosted.png", System.Drawing.Imaging.ImageFormat.Png); Console.WriteLine("boosted image saved to boosted.png"); }
        return 0;
    }

    /// <summary>Finds the game window, the chat area and the zone label, and prints what it saw. The live test for auto-detection.</summary>
    static int Locate(Settings s)
    {
        var game = GameWindow.Find(s.ProcessHints);
        if (game == null) { Console.WriteLine("Game window not found (is it running and not minimized?)."); return 1; }
        Console.WriteLine($"Game window: {game.Value.Width} × {game.Value.Height} at {game.Value.X}, {game.Value.Y}");
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var chat = Task.Run(() => ChatLocator.DetectAsync(game.Value)).GetAwaiter().GetResult();
        Console.WriteLine($"Chat: {(chat.region is { } r ? $"{r.Width} × {r.Height} at {r.X}, {r.Y}" : "not found")}  ({chat.note}, {sw.ElapsedMilliseconds} ms)");
        sw.Restart();
        var zone = Task.Run(() => ZoneLocator.DetectAsync(game.Value)).GetAwaiter().GetResult();
        Console.WriteLine($"Zone: {zone.zone ?? "not found"}  ({zone.note}, {sw.ElapsedMilliseconds} ms)");
        sw.Restart();
        var market = Task.Run(() => MarketLocator.DetectAsync(game.Value)).GetAwaiter().GetResult();
        Console.WriteLine($"Market: {(market.layout != null ? market.layout.ToString() : "not open")}  ({market.note}, {sw.ElapsedMilliseconds} ms)");
        if (chat.region is { } rr)
        {
            var reader = new ScreenReader(rr, s.Ocr.Scale);
            var lines = Task.Run(() => reader.ReadAsync()).GetAwaiter().GetResult();
            Console.WriteLine($"Chat area reads {lines.Count} line(s):");
            foreach (var l in lines.TakeLast(8)) Console.WriteLine("  " + l);
        }
        if (market.layout is { } ml)
        {
            var items = ItemDictionary.LoadOrEmpty(s.ItemsFile);
            var parser = new MarketParser(items);
            var rows = Task.Run(() => MarketReader.ReadRowsAsync(ml, s.Ocr.Scale)).GetAwaiter().GetResult();
            Console.WriteLine($"Market table reads {rows.Count} row(s):");
            foreach (var row in rows.Take(12)) Console.WriteLine($"  {row.Flat,-60} -> {parser.Parse(row)?.ToString() ?? "(ignored)"}");
        }
        var marks = new List<(System.Drawing.Rectangle, System.Drawing.Color)> { (ZoneLocator.Strip(game.Value), TrackerSession.ZoneColor) };
        if (chat.region is { } cr) marks.Add((cr, TrackerSession.ChatColor));
        if (market.layout is { } mk) marks.Add((mk.Outline, MarketCapture.MarkColor));
        using (var frame = ScreenReader.Frame(game.Value, 1280, marks.ToArray())) frame.Save("locate-frame.png", System.Drawing.Imaging.ImageFormat.Png);
        Console.WriteLine("Annotated frame saved to locate-frame.png");
        return 0;
    }

    /// <summary>Which parts of the game the tracker would read right now; for each one that's covered, the window over it. Captures nothing.</summary>
    static int InView(Settings s)
    {
        var game = GameWindow.Find(s.ProcessHints);
        if (game == null) { Console.WriteLine("Game window not found (is it running and not minimized?)."); return 1; }
        var g = game.Value;
        Console.WriteLine($"Game window: {g.Width} × {g.Height} at {g.X}, {g.Y} · {(GameWindow.InFront(s.ProcessHints) ? "in front" : "not the active window")}");
        void Show(string name, System.Drawing.Rectangle r) =>
            Console.WriteLine($"  {name,-14} {(GameWindow.Covered(r, s.ProcessHints) is { } c ? "covered by " + c : "in view")}");
        Show("chat corner", ChatLocator.Corner(g));
        if (RegionStore.Load() is { } chosen) Show("chosen chat", chosen);
        Show("minimap", ZoneLocator.Strip(g));
        Show("Market area", MarketLocator.Area(g));
        return 0;
    }

    static System.Drawing.Rectangle RegionFor(Dictionary<string, string> o) =>
        RegionStore.Parse(o.GetValueOrDefault("region")) ?? RegionStore.Load()
        ?? throw new InvalidOperationException("no chat region yet: run `pick` first, or pass --region x,y,w,h");

    static int OcrTest(Settings s, Dictionary<string, string> o)
    {
        var items = ItemDictionary.LoadOrEmpty(s.ItemsFile);
        if (o.ContainsKey("scales")) return ScalesTest(s, items, o);
        var auto = s.Ocr.AutoScale ? new TextScale(s.Ocr.Scale, s.Ocr.TargetTextPx) : null;
        var reader = new ScreenReader(RegionFor(o), s.Ocr.Scale, auto);
        var parser = new ChatParser(items);
        string? png = o.ContainsKey("save") ? $"ocr-test-{DateTime.Now:HHmmss}.png" : null;
        var lines = Task.Run(() => reader.ReadAsync(png)).GetAwaiter().GetResult();
        Console.WriteLine($"Region {reader.Region}: {lines.Count} line(s) recognized" + (png != null ? $", capture saved to {png}" : ""));
        foreach (var l in lines)
        {
            var evs = parser.Parse(l, DateTime.UtcNow).ToList();
            Console.WriteLine($"  {l}");
            foreach (var ev in evs) Console.WriteLine($"      -> {Describe(ev, items)}");
        }
        if (lines.Count == 0) Console.WriteLine("Nothing recognized. Make sure the game is in borderless or windowed mode and the box covers the chat text.");
        if (auto?.TextPx is { } tpx) Console.WriteLine($"Text is {tpx:0} px tall: read at {s.Ocr.Scale}x this time, {auto.Factor:0.##}x from the next read on (target {s.Ocr.TargetTextPx} px).");
        foreach (var (t, c) in reader.LastAccents) Console.WriteLine($"  name colour {c.R,3},{c.G,3},{c.B,3}  {t}");
        return 0;
    }

    /// <summary>
    /// The chat (or --image, a saved picture of it) read at each enlarging factor: how many lines came out and how many of
    /// them are pickups the parser knows. The factor with the most known pickups is the one to aim ocr.targetTextPx at.
    /// </summary>
    static int ScalesTest(Settings s, ItemDictionary items, Dictionary<string, string> o)
    {
        var parser = new ChatParser(items);
        using var raw = o.TryGetValue("image", out var img) ? Task.Run(() => ScreenReader.LoadAsync(img)).GetAwaiter().GetResult() : ScreenReader.Capture(RegionFor(o));
        Console.WriteLine($"{raw.Width}x{raw.Height} px of chat:");
        foreach (var f in new[] { 1.0, 1.5, 2.0, 2.5, 3.0, 3.5, 4.0 })
        {
            var lines = Task.Run(() => ScreenReader.ReadBitmapAsync(raw, true, f)).GetAwaiter().GetResult();
            int known = lines.Count(l => parser.Meaning(l).Key.Length > 0), exact = lines.Count(l => parser.Meaning(l).Exact);
            Console.WriteLine($"  {f:0.0}x  {lines.Count,3} lines  {known,3} pickups  {exact,3} read exactly");
        }
        return 0;
    }

    /// <summary>
    /// What each loot list row's icon says (PopupIcons), for popup-test: the text it would be counted by, why, and the
    /// closest icons with their distances. For checking the icon's place beside the text and the sure distance on real pictures.
    /// </summary>
    static void PopupIconsReport(ItemDictionary items, System.Drawing.Bitmap crop, System.Drawing.Point origin, List<OcrLineBox> lines)
    {
        Console.WriteLine("icons:");
        foreach (var l in lines.OrderBy(l => l.Box.Top))
        {
            var look = PopupIcons.Name(items, crop, origin, l);
            Console.WriteLine($"  {l.Text,-36} -> {look.Text}" + (look.Note.Length > 0 ? $"   ({look.Note})" : ""));
            foreach (var (n, d) in look.Scores.Take(4)) Console.WriteLine($"      {d,5:0}  {n}");
        }
    }

    /// <summary>Reads a saved image of the target bar (--image file.png) the way the tracker reads the band, and prints what it finds.</summary>
    static int TargetTest(Dictionary<string, string> o)
    {
        if (!o.TryGetValue("image", out var image)) { Console.WriteLine("target-test --image <png of the target bar>"); return 1; }
        using var whole = Task.Run(() => ScreenReader.LoadAsync(image)).GetAwaiter().GetResult();
        // --strip: a whole game picture, cut to the band the tracker reads
        using var bmp = o.ContainsKey("strip") ? whole.Clone(TargetReader.Strip(new System.Drawing.Rectangle(0, 0, whole.Width, whole.Height)), whole.PixelFormat) : (System.Drawing.Bitmap)whole.Clone();
        var lines = new List<OcrLineBox>();
        foreach (var (label, img, scale) in new (string, System.Drawing.Bitmap, int)[] { ("plain", bmp, 3), ("boost", ScreenReader.Boost(bmp), 3), ("name colour", ScreenReader.NameMask(bmp), 3) })
        {
            var got = Task.Run(() => ScreenReader.ReadBoxesAsync(img, new System.Drawing.Point(0, 0), scale)).GetAwaiter().GetResult();
            foreach (var l in got.OrderBy(l => l.Box.Top)) Console.WriteLine($"  {label}: {l.Text}");
            lines.AddRange(got);
        }
        Console.WriteLine($"crafting window: {lines.Any(l => TargetReader.IsCraftTab(l.Text))}");
        var name = TargetReader.FindName(lines.Select(l => (l.Box.Top, l.Text)), out var unlisted);
        Console.WriteLine(name != null ? $"Enemy: {name}" : $"No enemy on the list" + (unlisted.Length > 0 ? $" (name-shaped text: {unlisted})" : ""));
        return name != null ? 0 : 1;
    }

    /// <summary>
    /// A miscount report's pictures (frames.jsonl, in order) read again with this version's reader and counted as a live run
    /// counts them, printed beside what was counted when the report was saved (counted.jsonl). A reader fix is measured
    /// here on what the screen really showed (2026-10-23).
    /// </summary>
    static int ReportReplay(Settings settings, List<string> pos)
    {
        var dir = pos.FirstOrDefault();
        var index = dir == null ? "" : Path.Combine(dir, "frames.jsonl");
        if (dir == null || !File.Exists(index)) { Console.Error.WriteLine("report-replay <a miscount report folder with pictures (frames.jsonl)>"); return 1; }
        CrashLog.DirOverride = Path.Combine(Path.GetTempPath(), "kinah-report-replay");   // the replay's own notes, never the player's logs
        Directory.CreateDirectory(CrashLog.DirOverride);
        File.WriteAllText(Path.Combine(CrashLog.DirOverride, "reads.log"), "");
        var items = ItemDictionary.LoadOrEmpty(settings.ItemsFile);
        var s = new TrackerSession(null, settings, items, "Replay");
        var auto = settings.Ocr.AutoScale ? new TextScale(settings.Ocr.Scale, settings.Ocr.TargetTextPx) : null;
        ScreenReader? reader = null;
        int chats = 0, lists = 0; DateTime? last = null;
        foreach (var raw in File.ReadLines(index))
        {
            try
            {
                using var doc = System.Text.Json.JsonDocument.Parse(raw);
                var r = doc.RootElement;
                var t = DateTime.Parse(r.GetProperty("t").GetString()!, null, System.Globalization.DateTimeStyles.RoundtripKind).ToUniversalTime();
                var origin = new System.Drawing.Point(r.GetProperty("x").GetInt32(), r.GetProperty("y").GetInt32());
                using var bmp = Task.Run(() => ScreenReader.LoadAsync(Path.Combine(dir, r.GetProperty("f").GetString()!))).GetAwaiter().GetResult();
                if (r.GetProperty("k").GetString() == "chat")
                {
                    reader ??= new ScreenReader(new System.Drawing.Rectangle(origin, bmp.Size), settings.Ocr.Scale, auto);
                    var lines = Task.Run(() => reader.ReadCaptureAsync(bmp)).GetAwaiter().GetResult();
                    s.Parser.NoteAccents(reader.LastAccents);
                    s.QuestCounter(lines); s.ProcessChat(lines, t); chats++;
                }
                else
                {
                    var boxes = Task.Run(() => ScreenReader.ReadBoxesAsync(bmp, origin, 3, boost: true)).GetAwaiter().GetResult();
                    var rows = boxes.Select(l => (l.Box.Top, l.Text)).ToList();
                    if (settings.Ocr.LootIcons) for (int i = 0; i < boxes.Count; i++) rows[i] = (rows[i].Top, PopupIcons.Name(items, bmp, origin, boxes[i]).Text);
                    s.ProcessList(rows, t); lists++;
                }
                last = t;
            }
            catch (Exception ex) { Console.Error.WriteLine("skipped a picture: " + ex.Message); }
        }
        if (last != null) s.FlushVotes(last.Value);
        var e = s.Engine; var tot = e.Totals();
        Console.WriteLine($"Read {chats} chat and {lists} loot list pictures again with {Upload.RunPayload.AppVersion}" + (auto?.TextPx is double px ? $" (chat text {px:0} px, read at {auto.Factor:0.##}x)" : "") + ":");
        Console.WriteLine($"  Kinah {tot.Kinah:N0} · kills {tot.Kills} · drops {e.Items.Values.Sum()}");
        foreach (var (id, n) in e.Items.OrderByDescending(kv => kv.Value).Take(20)) Console.WriteLine($"    {items.NameOf(id)} ×{n}");
        var counted = Path.Combine(dir, "counted.jsonl");
        if (File.Exists(counted))
        {
            var then = File.ReadLines(counted).Select(l => { using var d = System.Text.Json.JsonDocument.Parse(l); return (What: d.RootElement.GetProperty("what").GetString() ?? "", Via: d.RootElement.GetProperty("via").GetString() ?? ""); }).ToList();
            Console.WriteLine($"When the report was saved, {then.Count} pickups were counted in that minute:");
            foreach (var (what, via) in then) Console.WriteLine($"    {what}" + (via.Length > 0 ? $"   ({via})" : ""));
        }
        var notes = File.ReadAllLines(Path.Combine(CrashLog.DirOverride, "reads.log"));
        Console.WriteLine($"  reads that didn't line up: {notes.Count(n => n.Contains("couldn't line"))} · names settled by colour {s.Parser.ColourPicks}, by loot table {s.Parser.TablePicks} · guesses held {s.Parser.HeldUnlisted}");
        CrashLog.DirOverride = null;
        return 0;
    }

    /// <summary>
    /// Feeds a recorded fight (trace.log: every chat and loot list read, as the tracker saw it) through the same counting
    /// as a live run, and prints what it counted beside the game's own quest counter. A fix is measured here, on what the
    /// screen really showed, before it ships. --from/--to take local times (HH:mm) to cut the trace to one fight.
    /// </summary>
    static int Replay(Settings settings, List<string> pos, Dictionary<string, string> opts)
    {
        var path = pos.Count > 0 ? pos[0] : Path.Combine(CrashLog.Dir, "trace.log");
        if (!File.Exists(path)) { Console.Error.WriteLine($"no trace at {path}"); return 1; }
        TimeSpan? from = opts.TryGetValue("from", out var f) && TimeSpan.TryParse(f, out var ft) ? ft : null;
        TimeSpan? to = opts.TryGetValue("to", out var tt0) && TimeSpan.TryParse(tt0, out var tt) ? tt : null;
        var real = CrashLog.Dir;
        CrashLog.DirOverride = Path.Combine(Path.GetTempPath(), "kinah-replay");   // the replay's own notes, never the player's logs
        Directory.CreateDirectory(CrashLog.DirOverride);
        File.WriteAllText(Path.Combine(CrashLog.DirOverride, "reads.log"), "");
        var items = ItemDictionary.LoadOrEmpty(settings.ItemsFile);
        var s = new TrackerSession(null, settings, items, "Replay");
        s.Vote = !opts.ContainsKey("novote");
        int chats = 0, lists = 0; DateTime? first = null, last = null;
        foreach (var raw in File.ReadLines(path))
        {
            try
            {
                using var doc = System.Text.Json.JsonDocument.Parse(raw);
                var r = doc.RootElement;
                var t = DateTime.Parse(r.GetProperty("t").GetString()!, null, System.Globalization.DateTimeStyles.RoundtripKind).ToUniversalTime();
                var local = t.ToLocalTime().TimeOfDay;
                if ((from != null && local < from) || (to != null && local > to)) continue;
                var lines = r.GetProperty("l").EnumerateArray().Select(x => x.GetString() ?? "").ToList();
                first ??= t; last = t;
                if (r.GetProperty("k").GetString() == "chat") { s.QuestCounter(lines); s.ProcessChat(lines, t); chats++; }
                else
                {
                    // the trace keeps list rows as "463 Kina" and "Name x2"; the list's own form is "Kina x463"
                    var rows = lines.Select((x, i) => (i * 20, System.Text.RegularExpressions.Regex.Replace(x, @"^(\d+) Kina$", "Kina x$1"))).ToList();
                    s.ProcessList(rows, t); lists++;
                }
            }
            catch (Exception ex) { Console.Error.WriteLine("skipped a trace line: " + ex.Message); }
        }
        if (last != null) s.FlushVotes(last.Value);
        var e = s.Engine; var tot = e.Totals();
        Console.WriteLine($"Replayed {chats} chat reads and {lists} loot list reads, {first?.ToLocalTime():HH:mm:ss} to {last?.ToLocalTime():HH:mm:ss}.");
        Console.WriteLine($"  kills counted        {tot.Kills}   (evidence: {e.KillsSeen} · Kinah pickups {e.KinahDrops} · Soul Points lines {e.CurrencyLines})");
        Console.WriteLine($"  quest counter steps  {s.QuestStepsTotal}   (kills of quest enemies, counted by the game)");
        Console.WriteLine($"  Kinah                {tot.Kinah:N0}");
        Console.WriteLine($"  drops                {e.Items.Values.Sum()} items · crafts kept out of drops {e.CraftedSkipped}");
        Console.WriteLine($"  loot list            {s.CrossCheck.Matched} matched the chat · {s.CrossCheck.Added} added (chat missed) · {s.CrossCheck.Duplicates} chat repeats skipped");
        Console.WriteLine("  by enemy:");
        foreach (var m in e.MobSnapshot().Take(12)) Console.WriteLine($"    {m.Name,-36} {m.Kills,4} kills  {m.Kinah,9:N0} Kinah");
        var notes = File.ReadAllLines(Path.Combine(CrashLog.DirOverride, "reads.log"));
        Console.WriteLine($"  reads that didn't line up: {notes.Count(n => n.Contains("couldn't line"))}");
        CrashLog.DirOverride = null; _ = real;
        return 0;
    }

    /// <summary>
    /// The replay test bank (Session/ReplayBank.cs): `replay-bank <dir>` runs every case and fails on a miscount (the
    /// release workflow runs it before publishing); `--record <case>` writes what this version counts into that case;
    /// `--seed` writes the generated cases into <dir>.
    /// </summary>
    static int ReplayBankCmd(Settings settings, List<string> pos, Dictionary<string, string> opts)
    {
        var dir = pos.FirstOrDefault() ?? Path.Combine("..", "tests", "replays");
        if (opts.ContainsKey("seed-full")) { Console.WriteLine($"wrote {Session.ReplayBank.SeedFullFrames(dir)} whole-window crafting pictures to {Path.GetFullPath(dir)}"); return 0; }
        if (opts.ContainsKey("seed")) { Directory.CreateDirectory(dir); foreach (var c in Session.ReplayBank.Seed(dir)) Console.WriteLine($"wrote {c}"); return 0; }
        if (!Directory.Exists(dir)) { Console.Error.WriteLine($"no replay bank at {Path.GetFullPath(dir)}"); return 1; }
        var results = Session.ReplayBank.Run(dir, settings, opts.GetValueOrDefault("record"));
        foreach (var r in results) Console.WriteLine($"{r.Status,-9} {r.Case}  {r.Detail}");
        int failed = results.Count(r => r.Status == "FAIL");
        Console.WriteLine($"replay bank: {results.Count(r => r.Status == "pass")} passed, {failed} failed, {results.Count(r => r.Status == "pending")} pending, {results.Count(r => r.Status == "skipped")} skipped" + (Session.ReplayBank.OcrAvailable() ? "" : " (no text recognition here)"));
        return failed > 0 || results.Count == 0 ? 1 : 0;
    }

    /// <summary>Renders the window (and the panel) off-screen to PNG files, so the layout can be reviewed without a desktop session.</summary>
    static int UiShot(Settings s, Dictionary<string, string> o, List<string> pos)
    {
        var dir = pos.Count > 0 ? pos[0] : ".";
        if (o.ContainsKey("options")) { WpfApp.ShotOptions(s, dir, o.GetValueOrDefault("skin")); Console.WriteLine($"wrote {Path.GetFullPath(dir)}"); return 0; }
        WpfApp.Shot(s, dir, o.ContainsKey("demo"), o.ContainsKey("market") ? "Market" : o.ContainsKey("crafting") ? "Crafting" : o.ContainsKey("events") ? "Events" : o.ContainsKey("pets") ? "Pets" : o.ContainsKey("gathering") ? "Gathering" : o.ContainsKey("mixed") ? "Mixed" : "Open world", o.ContainsKey("compact"), o.ContainsKey("welcome"), o.GetValueOrDefault("skin"), o.ContainsKey("full"));
        Console.WriteLine($"wrote {Path.GetFullPath(dir)}");
        return 0;
    }

    /// <summary>Feeds typical chat lines (including recognition slips) through the parser and the new-line tracker.</summary>
    static int ParseTest(Settings s)
    {
        // the test's made-up lines and notes go to a scratch log, never the player's reads.log
        CrashLog.DirOverride = Path.Combine(Path.GetTempPath(), "KinahLedger-parse-test");
        var items = ItemDictionary.LoadOrEmpty(s.ItemsFile);
        var parser = new ChatParser(items);
        string[] lines =
        {
            "[21:14] Acquired 9,000 Kina (Bound).",
            "Acquired 2,790 Kina.",
            "Acquired 1,24O Kina.",
            "Acquired Drakan hide ×2.",
            "Acquired Drakan hide x2",
            "Acquired Aether shard *3.",
            "Acquired Greater manastone.",
            "Acquired Radiant Odyle (Bound) ×4.",
            "Spiritstone acquired.",
            "Fresh Fruit acquirea.",
            "Orichalcum Ore acquired.",
            "Orichalcum Ore x2 acquired.",
            "Acquired Fine Odyle x2.",
            "Odyle acquired.",
            "Kukuru acquired.",
            "Essence Extraction failed",
            "Fine Orichalcum Ore x2 acquired.",
            "Ruby Gemstone acquired.",
            "Orichalcurn Ore acquired.",
            "Fresh Fruit x3 acquired.",
            "Obtained 1 Oculazen Soul Points.",
            "Orichalcum Ore acquiréd.i",
            "Fresh Fruit acquirea.",
            "Acquiredn521 Kina?",
            "Obtained 1 Natura Soul Crystals.",
            "Acquired .107 Kina.",
            "-obtained papis soul poin!s.",
            "Obtained n Papis Soul Points.",
            "Obtained 2-Varian Soul Crystals.",
            "Obtained 1 Papis Soul",
            "Obtained-I Papis SouUPoints.",
            "Obtained fPapis Soul-Points.",
            "Acquired LesserSoulstone (Bound)lx10.",
            "Obtained IfVarian Sput Crystals.",
            "Obtained 15 SouUCrystals.",
            "Obtained Varian Soul Crystals.",
            "Acquired Flarble Wossname.",
            "Obtained 1 Zorbo Soul Crystals.",
            "Obtained 1 Papls Soul Points.",
            "Acquired Lesser Soulstone x3.",
            "Obtained 2 Fera Soul Crystals.",
            "Obtained 2 Fera Soul Crystals;",
            "Obtained 1 Ivera Soul Crystal:",
            "Increased Pet Insight Level Oculazen.",
            "Acquired 520 Kina.",
            "Acquired 13,000 Kina (Bound).",
            "Acquired Artisan's Refining Stone (Bound) x3.",
            "Title Acquired: Extracting in My Sleep",
            "[Sakukun] the eastern altamia hig8hland",
            "You defeated Flame Sprite.",
            "Flame Sprite dealt 1,204 damage to you.",
            "[Vina] where did the filthy asmos go",
            "[CharlieNyerk] [Force Recruitment] COME",
            "Guild chat: anyone selling Odyle?"
        };
        Console.WriteLine("Parser:");
        foreach (var l in lines)
        {
            var evs = parser.Parse(l, DateTime.UtcNow).Select(e => Describe(e, items)).ToList();
            Console.WriteLine($"  {l,-44} -> {(evs.Count == 0 ? "(ignored)" : string.Join("; ", evs))}");
        }

        Console.WriteLine("\nNew-line tracker (what each read counts as new):");
        var t = new LineTracker();
        var frames = new[]
        {
            new List<string> { "a", "b", "c" },
            new List<string> { "b", "c", "d" },
            new List<string> { "b", "c", "d" },
            new List<string> { "c", "d", "d" },
            new List<string> { "x", "y", "z" },
            new List<string> { "y", "z", "w" },
            new List<string> { "a", "b", "c" },
            new List<string>(),                      // the chat faded out: nothing new, and the baseline stays
            new List<string> { "a", "b", "c", "d" }, // it came back with one more line, and only that one counts
            new List<string> { "b", "c", "d", "Targena" },               // world text behind the frameless chat shows up at the bottom
            new List<string> { "b", "c", "d", "Odyle acquired." },        // it's gone and a loot line arrived: the loot line counts
            new List<string> { "c", "d", "Odyle acquired.", "Kukur" },
            new List<string> { "c", "d", "Odyle acquirea.", "Ore" }       // a misread copy of a line already seen isn't new; Ore is
        };
        string[] expect = { "", "d", "", "d", "", "w", "", "", "d", "Targena", "Odyle acquired.", "Kukur", "Ore" };
        bool ok = true;
        for (int i = 0; i < frames.Length; i++)
        {
            var got = string.Join(",", t.NewLines(frames[i]));
            ok &= got == expect[i];
            Console.WriteLine($"  read {i + 1} [{string.Join(",", frames[i])}] -> new: [{got}]  expected [{expect[i]}] {(got == expect[i] ? "ok" : "MISMATCH")}");
        }
        // a read that garbles the bottom lines, then a clean one: the garbled lines were seen, so nothing in the clean read is new
        // (taken from a live reads.log, 2026-10-20 15:08, where they were counted twice)
        var g = new LineTracker();
        var garbled = new[]
        {
            new List<string> { "Odyle acquired.", "[Theo] next", "Fresh Aria acquired.", "[Carahx3] radiant mat proc chance", "Fresh Aria acquired." },
            new List<string> { "Odyle acquired.", "[Theo] next", "Fresh Aria acquircd.", "[Carahx3] radlant mat proc chanee", "Fresh Arla acquired.", "mria acquired." },
            new List<string> { "Odyle acquired.", "[Theo] next", "Fresh Aria acquired.", "[Carahx3] radiant mat proc chance", "Fresh Aria acquired.", "Aria acquired." },
        };
        string[] gExpect = { "", "mria acquired.", "" };
        // a read of nothing but a stray number from the world is skipped, not made the baseline (15:11:10 in the same log)
        garbled = garbled.Append(new List<string> { "99" })
                         .Append(new List<string> { "Odyle acquired.", "[Theo] next", "Fresh Aria acquired.", "[Carahx3] radiant mat proc chance", "Fresh Aria acquired.", "Aria acquired.", "Acquired 420 Kina." }).ToArray();
        gExpect = gExpect.Append("").Append("Acquired 420 Kina.").ToArray();
        for (int i = 0; i < garbled.Length; i++)
        {
            var got = string.Join(",", g.NewLines(garbled[i]));
            ok &= got == gExpect[i];
            Console.WriteLine($"  garbled read {i + 1} -> new: [{got}]  expected [{gExpect[i]}] {(got == gExpect[i] ? "ok" : "MISMATCH")}");
        }
        // two identical drops in a row while the chat isn't full: the second is new (live reads.log, 2026-10-21 16:25)
        var dup = new LineTracker();
        var dupFrames = new[]
        {
            new List<string> { "[Ririchiyo] wait you cant make new chars", "Acquired 128 Kina." },
            new List<string>(),
            new List<string> { "Raw Leather acquired." },
            new List<string> { "Raw Leather acquired.", "Raw Leather acquired." },
            new List<string> { "Raw Leather acquired.", "Raw Leather acquired.", "Acquired 444 Kina." },
            new List<string> { "Raw Leather acquired.", "Raw Leather acquired.", "Acquired 444 Kina." },
        };
        string[] dupExpect = { "", "", "Raw Leather acquired.", "Raw Leather acquired.", "Acquired 444 Kina.", "" };
        for (int i = 0; i < dupFrames.Length; i++)
        {
            var got = string.Join(",", dup.NewLines(dupFrames[i]));
            ok &= got == dupExpect[i];
            Console.WriteLine($"  same drop twice, read {i + 1} -> new: [{got}]  expected [{dupExpect[i]}] {(got == dupExpect[i] ? "ok" : "MISMATCH")}");
        }
        // lines the recognizer garbled around the words that matter (live reads.log, 2026-10-21)
        {
            var gp = new ChatParser(items);
            foreach (var (gline, gexpect) in new[] { ("Raw Leather *acquired.«", "Raw Leather ×1"), ("Raw Leatheracquired.", "Raw Leather ×1"), ("Spiritstone acquired:-", "Spiritstone ×1"),
                                                   ("SpiritStone—acquired.", "Spiritstone ×1"), ("Orichalcum Ore acquiréd.i", "Orichalcum Ore ×1"), ("cquired 347 Kina.", "Kinah +347"), ("Acquiréd 169 Kina.", "Kinah +169"), ("Acquired 31>2. Kina.", "Kinah +312"), ("Obtained 2 Stigma Shard points.", "Stigma Shard points +2"), ("AcquireÖ 212 Kina.", "Kinah +212"), ("Raw Leather acquíred.", "Raw Leather ×1"), ("Restored Recon Team (Helm) Skin acquired.", "(nothing)"), ("Dawn Frost (Cloak) Skin acquired.", "Dawn Frost (Skin: Cloak) ×1"), ("Forbidden Ritual (Orb) Skin acquired.", "Forbidden Ritual (Skin: Orb) ×1"), ("[Theo] Raw Leatheracquired lol", "(nothing)") })
            {
                var gevs = gp.Parse(gline, DateTime.UtcNow).Select(e => Describe(e, items)).ToList();
                var ggot = gevs.Count == 0 ? "(nothing)" : string.Join("; ", gevs);
                ok &= ggot == gexpect;
                Console.WriteLine($"  garbled loot line {gline,-30} -> {ggot}  {(ggot == gexpect ? "ok" : "MISMATCH, expected " + gexpect)}");
            }
        }
        // a name partly hidden (something over the chat, the line cut at the edge): its closest item, when only one fits
        {
            var gp2 = new ChatParser(items) { Likely = () => new[] { "Spiritstone", "Raw Leather" } };
            string G(string line) { var evs = gp2.Parse(line, DateTime.UtcNow).ToList(); return evs.Count == 0 ? "(nothing)" : string.Join("; ", evs.Select(e2 => Describe(e2, items) + (e2.Via.Length > 0 ? " [" + e2.Via + "]" : ""))); }
            foreach (var (gname, gline, gwant) in new[] {
                ("end hidden, a likely item", "Spiritst acquired.", "Spiritstone ×1 [guessed]"),
                ("start hidden, a likely item", "itstone acquired.", "Spiritstone ×1 [guessed]"),
                ("middle garbled, a likely item", "Raw Lxathxr acquired.", "Raw Leather ×1 [guessed]"),
                ("too little left to tell", "Spi acquired.", "(nothing)"),
                ("a plain read is not a guess", "Spiritstone acquired.", "Spiritstone ×1") })
            {
                var ggot2 = G(gline).Replace("x1", "×1");
                ok &= ggot2 == gwant;
                Console.WriteLine($"  guess: {gname,-36} -> {ggot2}  {(ggot2 == gwant ? "ok" : "MISMATCH, expected " + gwant)}");
            }
        }
        // a read that shares lines with the last one but doesn't line up (the quest tracker garbled differently): the
        // reads of 2026-10-21 17:51, where a Spiritstone, 1,192 Kina and 3 Soul Crystals were lost
        {
            var ovPrev = new[] { "asmos doing daily/feather runs. Got big bro", "holding the skies for ya", "Acquired 412 Kina.", "Defeat Kerubar and Kerubian", "Acquired 415 Kina.", "Acquired 223 Kina.", "Defeat Kerubar and Kerubian", "Defeat Kerubar and Kerubian", "Acquired 431 Kina.", "Defeat Kerubar and Kerubian", "(8/15)", "(9/15)", "(10/15)", "(11/15)" };
            var now2 = new[] { "Acquired 415 Kina.", "Acquired 223 Kina.", "Defeat Kerubar and Kerubian (9/15)", "Defeat Kerubar and Kerubian (10/15)", "Acquired 431 Kina.", "Defeat Kerubar and Kerubian (11/15)", "Spiritstone acquired.", "Obtained 3 Cogni Soul Crystals.", "Obtained 1 Kerubian Soul Points.", "Acquired 1,192 Kina." };
            var ovGot = Reread.AfterOverlap(ovPrev, now2);
            void Ov(string what, string got2, string want) { ok &= got2 == want; Console.WriteLine($"  {what,-58} -> {got2}  {(got2 == want ? "ok" : "MISMATCH, expected " + want)}"); }
            Ov("unaligned but overlapping: the lines after the last shared one", string.Join(" | ", ovGot), "Spiritstone acquired. | Obtained 3 Cogni Soul Crystals. | Obtained 1 Kerubian Soul Points. | Acquired 1,192 Kina.");
            Ov("unaligned, nothing new below the shared line: nothing", Reread.AfterOverlap(now2, now2.Take(8).ToArray()).Count.ToString(), "0");
            Ov("a new Kinah amount is never taken for an old one", string.Join(" | ", Reread.AfterOverlap(new[] { "Acquired 431 Kina.", "Raw Leather acquired." }, new[] { "Raw Leather acquired.", "Acquired 413 Kina." })), "Acquired 413 Kina.");
            // the chat came back from a fade with a whole event reward at once (Accursed Sword, 2026-10-22 14:35): the loot lines
            // not seen in the last ten minutes are new, from the bottom up; history shown by a hover ends with a seen line
            {
                var rec = new Dictionary<string, DateTime>(); var r0 = DateTime.UtcNow;
                for (int i2 = 0; i2 < 25; i2++) Reread.Remember(rec, new[] { $"[P{i2}] chat line {i2}", "Acquired 431 Kina." }, r0.AddMinutes(-3));
                var reward = new[] { "[P3] chat line 3", "Forgotten Bow acquired.", "Riverside Helm acquired.", "Acquired Odyle x6." };
                var history = new[] { "Forgotten Bow acquired.", "Riverside Helm acquired.", "Acquired 431 Kina." };
                Ov("a burst after a fade counts; history from a hover doesn't", $"{string.Join(" | ", Reread.Burst(Array.Empty<string>(), reward, rec, r0))} · [{string.Join(" | ", Reread.Burst(Array.Empty<string>(), history, rec, r0))}]", "Forgotten Bow acquired. | Riverside Helm acquired. | Acquired Odyle x6. · []");
            }
        }
        // the chat wraps a line at about 40 characters: a long item name comes as two lines (2026-10-21, a green armor drop lost)
        {
            string Batches(params string[][] reads)
            {
                var wp = new ChatParser(items); var t = DateTime.UtcNow; var got = new List<string>();
                foreach (var r in reads) { foreach (var (_, evs) in wp.ParseBatch(r, t)) got.AddRange(evs.Select(e => Describe(e, items))); t = t.AddSeconds(1); }
                return got.Count == 0 ? "(nothing)" : string.Join("; ", got);
            }
            foreach (var (wname, wgot, wexpect) in new[] {
                ("a long item name split over two lines", Batches(new[] { "Artisan's Orichalcum", "Longsword acquired." }), "Artisan's Orichalcum Longsword ×1"),
                ("the verb alone, the amount on the next line", Batches(new[] { "Acquired", "169 Kina." }), "Kinah +169"),
                ("the split falls between two reads", Batches(new[] { "Raw Leather acquired.", "Artisan's Orichalcum" }, new[] { "Longsword acquired." }), "Raw Leather ×1; Artisan's Orichalcum Longsword ×1"),
                ("a stray line above a drop never changes it", Batches(new[] { "lol nice", "Raw Leather acquired." }), "Raw Leather ×1"),
                ("a wrapped quest line is nothing", Batches(new[] { "Defeat Gravi, specters, spirits, and Papis", "(4/20)", "Raw Leather acquired." }), "Raw Leather ×1"),
                ("player chat is never joined", Batches(new[] { "[Theo] look at my Artisan's Orichalcum", "Longsword acquired." }), "(nothing)") })
            {
                ok &= wgot == wexpect;
                Console.WriteLine($"  wrapped: {wname,-44} -> {wgot}  {(wgot == wexpect ? "ok" : "MISMATCH, expected " + wexpect)}");
            }
        }
        // the crafting window's "Show acquired designs" over the chat became the baseline after a fade, and the next read,
        // holding the craft and its item line, was dropped as unaligned (live reads.log, 2026-10-21 09:38)
        var h = new List<string> { "[Masu] only way is to swipe", "[Drogo] i only swipe for cosmetics", "Acquired 144 Kina.", "Obtained 1 Karnif Soul Points.", "[Drogo] we're wumbo" };
        var cw = new LineTracker();
        var craftFrames = new[]
        {
            h,
            new List<string>(),
            new List<string> { "Odyle acquired." },
            new List<string>(),
            new List<string> { "Show acquired designs" },
            h.Concat(new[] { "Odyle acquired.", "Successfully crafted Orichalcum Ingot.", "Orichalcum Ingot acquired." }).ToList(),
            h.Concat(new[] { "Odyle acquired.", "Successfully crafted Orichalcum Ingot.", "Orichalcum Ingot acquired.", "Acquired 150 Kina." }).ToList(),
        };
        string[] craftExpect = { "", "", "Odyle acquired.", "", "Show acquired designs", "Successfully crafted Orichalcum Ingot.,Orichalcum Ingot acquired.", "Acquired 150 Kina." };
        for (int i = 0; i < craftFrames.Length; i++)
        {
            var got = string.Join(",", cw.NewLines(craftFrames[i]));
            ok &= got == craftExpect[i];
            Console.WriteLine($"  crafting window read {i + 1} -> new: [{got}]  expected [{craftExpect[i]}] {(got == craftExpect[i] ? "ok" : "MISMATCH")}");
        }
        Console.WriteLine("\nZones in what is shared (unverified names fold into Unknown zone):");
        var ze = new KinahLedger.Session.SessionEngine(items);
        ze.CreditZone("Ask Gemini", 600); ze.CreditZone("Altamia Canyon", 300); ze.CreditZone("Sunlit Grove", 120); ze.CreditZone("Some Caption", 60);
        var shared = RunPayload.Shareable(ze.ZoneSnapshot());
        var sharedKeys = string.Join(", ", shared.Keys.OrderBy(k => k));
        bool zok = !shared.ContainsKey("Ask Gemini") && !shared.ContainsKey("Some Caption") && shared.ContainsKey("Altamia Canyon") && shared.ContainsKey("Sunlit Grove")
            && Math.Abs(shared[KinahLedger.Session.SessionEngine.UnknownZone].Seconds - 660) < 5;
        var act = KinahLedger.Session.TrackerSession.ActivityOf(ze, "Open world");
        zok &= act == "Open world · Altamia Canyon";
        Console.WriteLine($"  shared zones: [{sharedKeys}]  activity: {act}  {(zok ? "ok" : "MISMATCH")}");
        ok &= zok;
        Console.WriteLine("\nItem list shapes (crawler and site) and the keys drops are shared under:");
        var tmpList = Path.Combine(Path.GetTempPath(), "kl-items-test.json");
        File.WriteAllText(tmpList, "{\"items\":[{\"id\":610531002,\"n\":\"Fresh Fruit\",\"g\":\"Common\",\"t\":1,\"c2\":\"CraftResource\",\"i\":\"Icon_A.png\"},{\"id\":110120001,\"name\":\"Noble Dragon Lord Greatsword\",\"grade\":\"Epic\",\"tradable\":true,\"categoryName\":\"Greatsword\",\"icon\":\"https://assets.playnccdn.com/x/Icon_B.png\"}]}");
        var dict = new ItemDictionary();
        long before = dict.IdOf("Fresh Fruit");                      // read before the list arrived: a synthetic id
        int merged = dict.Merge(tmpList);
        long after = dict.IdOf("Fresh Fruit"), sword = dict.IdOf("Noble Dragon Lord Greatsword"), odd = dict.IdOf("Zzyzx Shard");
        var de = new KinahLedger.Session.SessionEngine(dict);
        bool dok = merged == 2 && before < 0 && after == 610531002 && sword == 110120001 && odd < 0
            && dict.IconUrl(after) == ItemDictionary.IconBase + "Icon_A.png" && dict.IconUrl(sword).EndsWith("Icon_B.png")
            && RunPayload.ItemKey(de, after) == "610531002" && RunPayload.ItemKey(de, odd) == "Zzyzx Shard";
        Console.WriteLine($"  merged {merged}, Fresh Fruit {before} -> {after}, sword {sword}, unknown {odd} -> key \"{RunPayload.ItemKey(de, odd)}\"  {(dok ? "ok" : "MISMATCH")}");
        try { File.Delete(tmpList); } catch (Exception) { }
        ok &= dok;
        ok &= TradeTest(items);
        ok &= BestTest(items);
        ok &= AccuracyTest(items);
        ok &= RelogTest();
        ok &= MixedTest(items, s);
        ok &= EventRewardsTest(items, s);
        Console.WriteLine(ok ? "\nAll parser, tracker and 0.7 cases passed." : "\nMismatch, see above.");
        return ok ? 0 : 1;
    }

    /// <summary>Reads the currency strip once, the way the window does every ten seconds, and lists the numbers with where each starts.</summary>
    static int BalanceTest(Settings s, Dictionary<string, string> o)
    {
        var game = GameWindow.Find(s.ProcessHints);
        if (game == null) { Console.WriteLine("Game window not found (is it running and not minimized?)."); return 1; }
        var strip = BalanceReader.Strip(game.Value);
        if (GameWindow.Covered(strip, s.ProcessHints) is { } cover) { Console.WriteLine($"The top-left corner isn't readable now: {cover}."); return 1; }
        using var raw = ScreenReader.Capture(strip);
        if (o.ContainsKey("save")) { raw.Save("balance-strip.png", System.Drawing.Imaging.ImageFormat.Png); Console.WriteLine("Strip saved to balance-strip.png"); }
        var lines = Task.Run(() => ScreenReader.ReadBoxesAsync(raw, strip.Location, 3, boost: true)).GetAwaiter().GetResult();
        Console.WriteLine($"Strip {strip.Width} × {strip.Height} at {strip.X}, {strip.Y}: {lines.Count} line(s) read");
        foreach (var l in lines) Console.WriteLine("  " + l.Text);
        var counters = BalanceReader.Counters(lines);
        Console.WriteLine(counters.Count == 0 ? "No numbers found." : "Numbers, left to right: " + string.Join("   ", counters.Select(c => $"{c.Value:N0} (x {c.Left})")));
        Console.WriteLine("Which one is Kinah is worked out while you play: the counter that grows by exactly the Kina the chat shows.");
        return counters.Count > 0 ? 0 : 1;
    }

    /// <summary>
    /// 0.7.6: the region's middle price stands in where the server's board has none (and says so), the best hour per zone
    /// is kept only past ten minutes and a better rate, and the panel's field list always has one figure in panel order.
    /// Everything runs on temporary files, never the player's own.
    /// </summary>
    /// <summary>Text sizing, name colours, the name chooser, loot tables against guesses, the loot list's icon verdict (2026-10-23).</summary>
    static bool AccuracyTest(ItemDictionary items)
    {
        bool ok = true;
        void Check(string what, string got, string expect)
        {
            bool pass = got == expect; ok &= pass;
            Console.WriteLine($"  {what,-58} -> {got,-46} {(pass ? "ok" : "MISMATCH, expected " + expect)}");
        }
        Console.WriteLine("\nText size, name colours, loot tables and icons (2026-10-23):");

        // ---- the chat's enlarging factor from its text height ----
        var ts = new TextScale(2, 36);
        Check("fewer than five words: the configured scale", $"{ts.Observe(new[] { 12.0, 12, 12 })} {ts.Factor}", "False 2");
        Check("12 px text on a 1080p screen: read at 3x", $"{ts.Observe(new[] { 12.0, 12, 12, 13, 11 })} {ts.Factor}", "True 3");
        Check("a small drift in the measure: no change", $"{ts.Observe(new[] { 12.5, 12.5, 12.5, 12.5 })} {ts.Factor}", "False 3");
        var big = new TextScale(2, 36);
        Check("25 px text: never below the configured 2x", $"{big.Observe(Enumerable.Repeat(25.0, 8))} {big.Factor}", "False 2");
        var tiny = new TextScale(2, 36);
        Check("7 px text: no more than 4x", $"{tiny.Observe(Enumerable.Repeat(7.0, 8))} {tiny.Factor}", "True 4");

        // ---- a word's colour from its brightest pixels ----
        int w = 20, h = 10, stride = w * 4;
        var px = new byte[stride * h];
        for (int i = 0; i < px.Length; i += 4) { px[i] = 30; px[i + 1] = 20; px[i + 2] = 20; px[i + 3] = 255; }   // the chat's dark backing (BGRA)
        for (int y = 3; y < 7; y++) for (int x = 4; x < 16; x++) { int i = y * stride + x * 4; px[i] = 60; px[i + 1] = 180; px[i + 2] = 230; }   // gold letters
        var tc = TextColour.Of(px, stride, w, h, new System.Drawing.Rectangle(0, 0, w, h));
        Check("gold letters on the dark backing", tc is { } t1 ? $"{t1.Colour.R},{t1.Colour.G},{t1.Colour.B}" : "none", "230,180,60");
        Check("a box with no letters in it", TextColour.Of(px, stride, w, h, new System.Drawing.Rectangle(0, 0, 3, 3)) is null ? "none" : "some", "none");
        Check("name words: the verb, counts and Kina are not", string.Join(",", new[] { "Spiritstone", "acquired.", "x2", "1,240", "Kina.", "(Bound)", "Raw" }.Where(TextColour.NameWord)), "Spiritstone,Raw");

        // ---- each grade's colour, learned from names read exactly ----
        var gold = System.Drawing.Color.FromArgb(230, 180, 60); var blue = System.Drawing.Color.FromArgb(80, 140, 240); var white = System.Drawing.Color.FromArgb(225, 225, 225);
        var book = new GradeColours();
        Check("nothing learned: the colour says nothing", $"{book.Agrees("Unique", gold)?.ToString() ?? "null"}", "null");
        var rnd = new Random(7);
        System.Drawing.Color Jitter(System.Drawing.Color c, int d) => System.Drawing.Color.FromArgb(Math.Clamp(c.R + rnd.Next(-d, d + 1), 0, 255), Math.Clamp(c.G + rnd.Next(-d, d + 1), 0, 255), Math.Clamp(c.B + rnd.Next(-d, d + 1), 0, 255));
        for (int i = 0; i < 8; i++) { book.Observe("Unique", Jitter(gold, 12)); book.Observe("Legend", Jitter(blue, 12)); book.Observe("Common", Jitter(white, 8)); }
        Check("gold is Unique, not Legend; white is Common", $"{book.Agrees("Unique", gold)} {book.Agrees("Legend", gold)} {book.Agrees("Common", white)} {book.Agrees("Legend", white)}", "True False True False");
        Check("a dim gold is still gold", $"{book.Agrees("Unique", System.Drawing.Color.FromArgb(150, 117, 39))}", "True");
        Check("a grade not learned yet says nothing", $"{book.Agrees("Rare", gold)?.ToString() ?? "null"}", "null");
        var again = GradeColours.FromJson(book.ToJson());
        Check("kept and read back", $"{again.Learned().Count} {again.Agrees("Unique", gold)}", "3 True");

        // ---- the name chooser: "Kasra Breastplate" is one letter from Kaira (Unique) and Kasia (Legend) ----
        var cp = new ChatParser(items);
        string Item(IEnumerable<KinahLedger.Session.GameEvent> evs) => evs.OfType<KinahLedger.Session.ItemAcquired>().Select(a => items.NameOf(a.ItemId) + (a.Via.Length > 0 ? " (" + a.Via + ")" : "")).FirstOrDefault() ?? "nothing";
        const string misread = "Kasra Breastplate acquired.";
        string snap = Item(cp.Parse(misread, DateTime.UtcNow));
        Check("the two names are as close as each other", string.Join(" | ", items.Near("Kasra Breastplate", 2).Select(c => $"{c.Name} {c.Grade} {c.Distance}")), "Kaira Breastplate Unique 1 | Kasia Breastplate Legend 1");
        cp.Colours = book;
        cp.NoteAccents(new[] { (misread, gold) });
        Check("printed in gold: the Unique one", Item(cp.Parse(misread, DateTime.UtcNow)), "Kaira Breastplate");
        cp.NoteAccents(new[] { (misread, blue) });
        Check("printed in blue: the Legend one", Item(cp.Parse(misread, DateTime.UtcNow)), "Kasia Breastplate");
        var cp2 = new ChatParser(items);
        Check("no colour, nothing else known: the list's closest, as before", Item(cp2.Parse(misread, DateTime.UtcNow)), snap);
        cp2.Likely = () => new[] { "Kasia Breastplate" };
        Check("the enemy's loot table holds one of them: that one", Item(cp2.Parse(misread, DateTime.UtcNow)), "Kasia Breastplate");
        cp2.Likely = () => new[] { "Kaira Breastplate" };
        Check("...and the other way round", Item(cp2.Parse(misread, DateTime.UtcNow)), "Kaira Breastplate");
        var cp3 = new ChatParser(items) { Colours = book };
        cp3.NoteAccents(new[] { ("Kaira Breastplate acquired.", gold) });
        int seenBefore = book.Observed;
        Check("an exact name is never second-guessed", Item(cp3.Parse("Kaira Breastplate acquired.", DateTime.UtcNow)), "Kaira Breastplate");
        Check("...and teaches its grade's colour", $"{book.Observed - seenBefore}", "1");

        // ---- a guess no loot table backs, while fighting enemies whose tables are known ----
        const string cut = "ctral Greatsword acquired.";
        var gp = new ChatParser(items);
        Check("a name cut at the start is guessed", Item(gp.Parse(cut, DateTime.UtcNow)), "Spectral Greatsword (guessed)");
        gp.Fighting = () => new[] { "Abandoned Daeva" };
        Check("fighting an enemy whose table lacks it: held back", $"{Item(gp.Parse(cut, DateTime.UtcNow))} {gp.HeldUnlisted}", "nothing 1");
        gp.Fighting = () => new[] { "Advisor Resana" };
        Check("fighting one whose table has it: counted", Item(gp.Parse(cut, DateTime.UtcNow)), "Spectral Greatsword (guessed)");
        gp.Fighting = () => new[] { "Nobody We Know" };
        Check("an enemy with no table known: counted, as before", Item(gp.Parse(cut, DateTime.UtcNow)), "Spectral Greatsword (guessed)");

        // ---- the loot list's icon verdict ----
        string V(PopupIcons.Verdict? v) => v == null ? "none" : $"{v.Name} {v.Distance:0}";
        Check("one icon clearly closest: named", V(PopupIcons.Decide(new[] { (1L, "Spiritstone", "IconA", 40.0), (2L, "Raw Leather", "IconB", 80.0) })), "Spiritstone 40");
        Check("two icons nearly as close: no answer", V(PopupIcons.Decide(new[] { (1L, "Spiritstone", "IconA", 40.0), (2L, "Raw Leather", "IconB", 45.0) })), "none");
        Check("the closest isn't close enough: no answer", V(PopupIcons.Decide(new[] { (1L, "Spiritstone", "IconA", 90.0) })), "none");
        Check("two items share the icon: no answer", V(PopupIcons.Decide(new[] { (1L, "Fine Odyle", "IconA", 30.0), (2L, "Pure Odyle", "IconA", 30.0) })), "none");
        Check("a tradable and a bound copy share the art: the row's own kind", $"{V(PopupIcons.Decide(new[] { (1L, "Spiritstone", "IconA", 30.0), (2L, "Spiritstone (Bound)", "IconA", 30.0) }))} | {V(PopupIcons.Decide(new[] { (1L, "Spiritstone", "IconA", 30.0), (2L, "Spiritstone (Bound)", "IconA", 30.0) }, bound: true))}", "Spiritstone 30 | Spiritstone (Bound) 30");
        Check("a scrolled name's letters find their item first", items.Fragment("itstone", PopupIcons.MaxCandidates).FirstOrDefault()?.Name ?? "none", "Spiritstone");

        // ---- the miscount report: the last minute only, in trace.log's own form ----
        var rb = new ReportBuffer();
        var r0 = new DateTime(2026, 10, 23, 12, 0, 0, DateTimeKind.Utc);
        rb.Lines("chat", new[] { "Spiritstone acquired." }, r0);
        rb.Counted(r0, "Spiritstone ×1", "chat");
        rb.Lines("chat", new[] { "Acquired 463 Kina." }, r0.AddSeconds(70));
        rb.Lines("list", new[] { "463 Kina" }, r0.AddSeconds(71));
        rb.Counted(r0.AddSeconds(70), "463 Kinah", "chat");
        Check("only the last minute is held", $"{rb.Held.Reads} reads, {rb.Held.Counted} counted", "2 reads, 1 counted");
        var rtmp = Path.Combine(Path.GetTempPath(), "kl-report-" + Guid.NewGuid().ToString("N"));
        try
        {
            var rdir = rb.Write(rtmp, "state", new[] { "a log line" }, r0.AddSeconds(72));
            var traced = File.ReadAllLines(Path.Combine(rdir, "trace.jsonl"));
            using var first = System.Text.Json.JsonDocument.Parse(traced[0]);
            Check("trace.jsonl reads like trace.log", $"{traced.Length} {first.RootElement.GetProperty("k").GetString()} {first.RootElement.GetProperty("l")[0].GetString()}", "2 chat Acquired 463 Kina.");
            Check("no pictures unless the player said yes", $"{Directory.Exists(Path.Combine(rdir, "frames"))} {File.Exists(Path.Combine(rdir, "counted.jsonl"))} {File.Exists(Path.Combine(rdir, "README.txt"))}", "False True True");
        }
        finally { try { Directory.Delete(rtmp, true); } catch { } }
        return ok;
    }

    /// <summary>
    /// Steven, 2026-10-23, Hidden Lugi: the event's rewards printed 11 s after the game moved him out, while the chat faded
    /// in garbled, and none of them counted, toward the event or anywhere. Modelled on that trace (other players' lines
    /// replaced): the rewards count once the chat settles, dated when they landed, and go to the event run just left.
    /// </summary>
    static bool EventRewardsTest(ItemDictionary items, Settings settings)
    {
        bool ok = true;
        void Check(string what, string got, string expect)
        {
            bool pass = got == expect; ok &= pass;
            Console.WriteLine($"  {what,-58} -> {got,-46} {(pass ? "ok" : "MISMATCH, expected " + expect)}");
        }
        Console.WriteLine("\nAn event's rewards after leaving it, read while the chat fades in (2026-10-23):");
        var t0 = new DateTime(2026, 10, 23, 15, 1, 0, DateTimeKind.Utc);
        int n(SessionEngine e, string name) => e.Items.Where(kv => items.NameOf(kv.Key) == name).Sum(kv => kv.Value);
        // the same reads twice: as they came (an achievement line printed with the items), and without that line
        foreach (bool achievement in new[] { false, true })
        {
            var tb = t0.AddHours(achievement ? 1 : 0);   // its own run, apart from the first pass's, kept on disk
            string ach = achievement ? "One-of-a-Kind Gear III achieved." : "[Player5] gg";
            var s = new TrackerSession(null, settings, items, "Test");
            InstanceRun? run() => s.Instances.Snapshot().LastOrDefault(x => x.Zone == "Shugo Festival" && x.Start == tb);
            int r(string name) => (run()?.Items ?? new()).Where(kv => items.NameOf(kv.Key) == name).Sum(kv => kv.Value);
            s.Engine.SetZone("Hidden Lugi"); s.Instances.Zone("Hidden Lugi", tb);
            if (!achievement) Check("Hidden Lugi opens a Shugo Festival run", s.Instances.Open ?? "none", "Shugo Festival");
            var before = new List<string> { "Player1 (Shugo) found Player2 (Lugi).", "Player3 (Shugo) found Player1 (Lugi)." };
            s.ProcessChat(before, tb.AddSeconds(300));
            s.ProcessChat(before, tb.AddSeconds(301));
            // the game moves the player out; the minimap names the campsite
            var left = tb.AddSeconds(383);
            s.Engine.SetZone("Ruin Exploration Campsite"); s.Instances.Zone("Ruin Exploration Campsite", left);
            if (!achievement) Check("left the event: its run stays open for its rewards", $"{s.Instances.Open} {s.Instances.Ending(left.AddSeconds(11))} {s.Instances.Ending(left.AddSeconds(40))}", "Shugo Festival True False");
            // the rewards fade in garbled three ways, then a clean read, then one that lines up with it
            s.ProcessChat(new List<string> { "Forgotten Hatquired.", "Forgotten", "Illusion", "AcquirGdylO", "Codex (Bomd)", ach }, left.AddSeconds(11));
            s.ProcessChat(new List<string> { "rorgotten Cloak", "Acquire d Fine OJy1e", "*quire d", "Odyle", "Acquire d Refining stone", "Acquire d soul codex (BoulJ) x]", "One-" }, left.AddSeconds(12.2));
            s.ProcessChat(new List<string> { "• vrgotten Cloak", "Acquire d F•", "acquired", "Acquire d", "Ine Odyle", "Acquire d Refining stone", "Acquire d soul codex (Bounjl", "One-" }, left.AddSeconds(13.5));
            s.ProcessChat(new List<string> { "SEA", "Forgotten Cloak acquired.", "Acquired Fine Odyle x2.", "Acquired Odyle x9.", "Acquired Refining Stone", "Acquired Soul Codex (Bound) x3", ach, "[Player4] a chat line" }, left.AddSeconds(18.5));
            var settled = new List<string> { "Acquired Fine Odyle x2.", "Acquired Odyle x9.", "Acquired Refining Stone x6.", "Acquired Soul Codex (Bound) x3.", ach, "[Player4] a chat line" };
            for (int i = 0; i < 4; i++) s.ProcessChat(settled, left.AddSeconds(29 + i));
            s.FlushVotes(left.AddSeconds(33));
            if (!achievement)
            {
                Check("the rewards count once the chat settles, in the run", $"{r("Forgotten Cloak")} {r("Fine Odyle")} {r("Odyle")} {r("Refining Stone")}", "1 2 9 6");
                Check("...and not as farming in the campsite", $"{n(s.Engine, "Odyle")} {n(s.Engine, "Refining Stone")}", "0 0");
                s.ProcessChat(settled, left.AddSeconds(40));
                Check("read again later: counted once", $"{r("Odyle")}", "9");
                s.Instances.Zone("Ruin Exploration Campsite", left.AddSeconds(45));
                Check("its time for rewards over: the run closes, kept", $"{s.Instances.Open ?? "none"} {run()?.Items.Values.Sum()}", "none 18");
            }
            else
            {
                // Steven, 2026-10-23: achievement rewards never count; items printed with an achievement line are its reward
                Check("printed with an achievement line: its reward, not drops", $"{r("Forgotten Cloak")} {r("Odyle")} {n(s.Engine, "Odyle")} {n(s.Engine, "Refining Stone")}", "0 0 0 0");
                s.Instances.Zone("Ruin Exploration Campsite", left.AddSeconds(45));
                Check("...so the run has no loot and isn't kept", $"{s.Instances.Open ?? "none"} {run() == null}", "none True");
            }
        }
        // ---- achievement rewards and box contents: never drops (Steven, 2026-10-23) ----
        int List(TrackerSession ss, DateTime at, params string[] rows)
        {
            // a row up for two seconds, read four times, then gone; the chat never shows it
            for (int i = 0; i < 4; i++) ss.ProcessList(rows.Select((r0, k) => (k * 20, r0)).ToList(), at.AddSeconds(i * 0.5));
            foreach (var dt in new[] { 5.0, 9.0 }) ss.ProcessList(new List<(int, string)>(), at.AddSeconds(dt));
            return 0;
        }
        var b0 = new DateTime(2026, 10, 23, 16, 0, 0, DateTimeKind.Utc);
        // with list-only adds off (the default since 2026-10-23), a row the chat never names counts nothing
        var s0 = new TrackerSession(null, settings, items, "Test");
        s0.Engine.SetZone("Ruin Exploration Campsite");
        List(s0, b0, "Spiritstone x1");
        Check("loot list row the chat never names: not counted (chat-confirmed only)", $"{n(s0.Engine, "Spiritstone")}", "0");
        // the reward rules below hold for when list-only adds are switched back on (ocr.lootListAdds)
        bool addsWere = settings.Ocr.LootListAdds; settings.Ocr.LootListAdds = true;
        var sb = new TrackerSession(null, settings, items, "Test");
        sb.Engine.SetZone("Ruin Exploration Campsite");
        List(sb, b0, "Spiritstone x1");
        Check("loot list, no kill or gather around it: a box's contents, set aside", $"{n(sb.Engine, "Spiritstone")}", "0");
        List(sb, b0.AddSeconds(60), "Orichalcum Ore x1");
        Check("...but a gathering material is a gather, counted", $"{n(sb.Engine, "Orichalcum Ore")}", "1");
        sb.ProcessChat(new List<string> { "[Player1] hi" }, b0.AddSeconds(120));
        for (int i = 1; i <= 4; i++) sb.ProcessChat(new List<string> { "[Player1] hi", "Acquired 300 Kina." }, b0.AddSeconds(120 + i * 0.4));
        sb.FlushVotes(b0.AddSeconds(122));
        List(sb, b0.AddSeconds(123), "Spiritstone x1");
        Check("a row right after a kill: a drop the chat missed, counted", $"{n(sb.Engine, "Spiritstone")}", "1");
        sb.ProcessChat(new List<string> { "[Player1] hi", "Acquired 300 Kina.", "Acquired Growth Pet Chest (Bound) x2." }, b0.AddSeconds(130));
        for (int i = 1; i <= 3; i++) sb.ProcessChat(new List<string> { "[Player1] hi", "Acquired 300 Kina.", "Acquired Growth Pet Chest (Bound) x2." }, b0.AddSeconds(130 + i * 0.4));
        sb.FlushVotes(b0.AddSeconds(132));
        sb.ProcessChat(new List<string> { "[Player1] hi", "Acquired 300 Kina.", "Acquired Growth Pet Chest (Bound) x2.", "Acquired 520 Kina." }, b0.AddSeconds(133));
        List(sb, b0.AddSeconds(133.5), "Spiritstone x1");
        Check("a row just after a box or reward line, even near a kill: set aside", $"{n(sb.Engine, "Spiritstone")}", "1");
        settings.Ocr.LootListAdds = addsWere;
        var sa = new TrackerSession(null, settings, items, "Test");
        sa.Engine.SetZone("Ruin Exploration Campsite");
        sa.ProcessChat(new List<string> { "[Player1] hi" }, b0);
        var achLines = new List<string> { "[Player1] hi", "Acquired 300 Kina.", "Spiritstone acquired.", "Fortress Guardian Notun III achieved.", "Lesser Manastone (Bound) acquired." };
        for (int i = 1; i <= 5; i++) sa.ProcessChat(achLines, b0.AddSeconds(i * 0.4));
        sa.FlushVotes(b0.AddSeconds(3));
        Check("items printed with an achievement line: its reward, not drops", $"{n(sa.Engine, "Spiritstone")} {n(sa.Engine, "Lesser Manastone")} {sa.Engine.Totals().Kinah}", "0 0 300");
        // the achievement line stays on screen, read a little differently now and then; a drop two minutes later is a drop
        var later = new List<string> { "Acquired 300 Kina.", "Spiritstone acquired.", "Fortress Guardian Notun Ill achieved.", "Lesser Manastone (Bound) acquired.", "Acquired 410 Kina.", "Lesser Soulstone acquired." };
        for (int i = 0; i < 5; i++) sa.ProcessChat(later, b0.AddSeconds(120 + i * 0.4));
        sa.FlushVotes(b0.AddSeconds(123));
        Check("an old achievement line still on screen: a new drop counts", $"{n(sa.Engine, "Lesser Soulstone")}", "1");

        // ---- gathering (Steven, 2026-10-23 11:28-11:29, Orichalcum: "like a 25% accuracy") ----
        // the loot list band read the gathering panel (node, proficiency, item, Success) as rows; the game upgraded the ore
        // to Fine in place; one chat line came, late; then a gather he stopped showed the panel again and "failed"
        var so = new TrackerSession(null, settings, items, "Test");
        so.Engine.SetZone("Ruin Exploration Campsite");
        var g0 = new DateTime(2026, 10, 23, 15, 28, 40, DateTimeKind.Utc);
        List<(int, string)> L(params string[] r) => r.Select((x, k) => (100 + k * 30, x)).ToList();
        var chatBase = new List<string> { "[Player1] a chat line", "Cannot end flight in current state." };
        so.ProcessChat(chatBase, g0);
        so.ProcessList(L("Orich x1", "Profes x1", "Orichalcum Ore x1", "Success x1"), g0.AddSeconds(11.7));
        so.ProcessList(L("Orich x1", "AProfes x1", "Fine Orichalcum Ore x1", "Success x1"), g0.AddSeconds(12.4));
        so.ProcessList(L("Orich x1", "APrdfes x1", "Fine Orichalcum Ore x1", "Success x1"), g0.AddSeconds(13.3));
        so.ProcessList(L("Orich x1", "Fihe Orichalcum Ore x1", "Success x1"), g0.AddSeconds(15.0));
        so.ProcessList(L("Orich x1", "EPFOfes x1", "Success x1"), g0.AddSeconds(16.1));
        var chatOre = new List<string> { "[Player1] a chat line", "Cannot end flight in current state.", "Fine Orichalcum Ore acquired." };
        for (int i = 0; i < 4; i++) so.ProcessChat(chatOre, g0.AddSeconds(16.8 + i));
        so.ProcessList(L("Fine Orichalcum x1"), g0.AddSeconds(17.2));
        so.ProcessList(L("Orichal x1", "Pero(eS x1", "Orichalcum Ore x1", "Success x1"), g0.AddSeconds(18.3));
        for (int i = 0; i < 6; i++) so.ProcessList(L("Vangt x1", "Orichalcum x1", "Orichalcum Ore x1", "Success x1"), g0.AddSeconds(19.4 + i));
        var chatFail = new List<string> { "Cannot end flight in current state.", "Fine Orichalcum Ore acquired.", "Essence Extraction failed" };
        for (int i = 0; i < 4; i++) so.ProcessChat(chatFail, g0.AddSeconds(24.1 + i));
        foreach (var dt in new[] { 30.0, 34.0, 40.0 }) so.ProcessList(new List<(int, string)>(), g0.AddSeconds(dt));
        so.FlushVotes(g0.AddSeconds(41));
        Check("one gather, upgraded in place, then a stopped one: one Fine ore", $"{n(so.Engine, "Fine Orichalcum Ore")} {n(so.Engine, "Orichalcum Ore")}", "1 0");
        Check("...the failed gather is an attempt, not an item", $"{so.Engine.Totals().Nodes}", "2");
        // the gathering bar as the second witness for a gather the chat missed (step 1)
        var sg = new TrackerSession(null, settings, items, "Test");
        sg.Engine.SetZone("Ellun River Midstream");
        var w0 = new DateTime(2026, 10, 23, 16, 30, 0, DateTimeKind.Utc);
        List(sg, w0, "Orichalcum Ore x1");
        Check("loot list gather, no gathering bar seen: not counted", $"{n(sg.Engine, "Orichalcum Ore")}", "0");
        sg.GatherOutcome(w0.AddSeconds(20), true, false);
        List(sg, w0.AddSeconds(21), "Orichalcum Ore x1");
        Check("...the bar finished on Success: counted", $"{n(sg.Engine, "Orichalcum Ore")}", "1");
        sg.GatherOutcome(w0.AddSeconds(40), false, false);
        List(sg, w0.AddSeconds(41), "Orichalcum Ore x1");
        Check("...the bar ended on Fail: not counted", $"{n(sg.Engine, "Orichalcum Ore")}", "1");
        sg.GatherOutcome(w0.AddSeconds(60), true, false);
        sg.ProcessChat(new List<string> { "[Player1] hi" }, w0.AddSeconds(59));
        for (int i = 0; i < 4; i++) sg.ProcessChat(new List<string> { "[Player1] hi", "Orichalcum Ore acquired." }, w0.AddSeconds(60.5 + i * 0.4));
        sg.FlushVotes(w0.AddSeconds(62.5));
        List(sg, w0.AddSeconds(61), "Orichalcum Ore x1");
        Check("...a gather the chat named: once, not twice", $"{n(sg.Engine, "Orichalcum Ore")}", "2");

        // garbled reads over lines that were already on screen: nothing more
        var s2 = new TrackerSession(null, settings, items, "Test");
        var old = new List<string> { "Acquired Odyle x3.", "[Player1] hello" };
        s2.ProcessChat(new List<string> { "[Player2] hi" }, t0);
        s2.ProcessChat(new List<string> { "[Player2] hi", "Acquired Odyle x3.", "[Player1] hello" }, t0.AddSeconds(1));
        for (int i = 2; i < 5; i++) s2.ProcessChat(new List<string> { "[Player2] hi", "Acquired Odyle x3.", "[Player1] hello" }, t0.AddSeconds(i));
        int counted = n(s2.Engine, "Odyle");
        s2.ProcessChat(new List<string> { "xqzv", "Acqu ired 0dyle x3", "pl ayerl hel" }, t0.AddSeconds(6));
        s2.ProcessChat(new List<string> { "Acquired Odyle x3.", "[Player1] hello", "[Player5] new" }, t0.AddSeconds(7));
        s2.ProcessChat(new List<string> { "Acquired Odyle x3.", "[Player1] hello", "[Player5] new" }, t0.AddSeconds(8));
        s2.FlushVotes(t0.AddSeconds(9));
        Check("a line already on screen before the garble: not again", $"{counted} {n(s2.Engine, "Odyle")}", "3 3");
        return ok;
    }

    /// <summary>
    /// Farming and gathering at once (Steven, 2026-10-23): the tiles show one mixed set while both happened in the last
    /// five minutes, and the run says how focused it was (focus), which the site's per-hour figures go by.
    /// </summary>
    static bool MixedTest(ItemDictionary items, Settings settings)
    {
        bool ok = true;
        void Check(string what, string got, string expect)
        {
            bool pass = got == expect; ok &= pass;
            Console.WriteLine($"  {what,-58} -> {got,-46} {(pass ? "ok" : "MISMATCH, expected " + expect)}");
        }
        Console.WriteLine("\nFarming and gathering at once (2026-10-23):");
        var t0 = new DateTime(2026, 10, 23, 18, 0, 0, DateTimeKind.Utc);
        var e = new SessionEngine(items);
        Check("nothing yet: no focus, not mixed", $"{e.Focus?.ToString() ?? "none"} {e.MixedAt(t0)}", "none False");
        // twelve minutes of mobs, one a minute
        for (int i = 0; i < 12; i++) { e.Apply(new KinahChanged(t0.AddMinutes(i), 300, false)); e.Apply(new CurrencyGained(t0.AddMinutes(i), "Papis Soul Points", 1, "Papis")); }
        Check("mobs only: focus 1, not mixed", $"{e.Focus} {e.MixedAt(t0.AddMinutes(11.5))}", "1 False");
        // then a node between the mobs
        e.Apply(new ItemAcquired(t0.AddMinutes(12), items.IdOf("Orichalcum Ore"), 1, false));
        Check("a node among the mobs: mixed", $"{e.MixedAt(t0.AddMinutes(12.2))}", "True");
        e.Apply(new KinahChanged(t0.AddMinutes(13), 300, false));
        Check("mobs again, four minutes on: still mixed (no flip back)", $"{e.MixedAt(t0.AddMinutes(16))}", "True");
        Check("five minutes and more of mobs alone: back to farming", $"{e.MixedAt(t0.AddMinutes(17.5))}", "False");
        // as many minutes gathering as farming
        for (int i = 0; i < 12; i++) e.Apply(new ItemAcquired(t0.AddMinutes(20 + i), items.IdOf("Orichalcum Ore"), 1, false));
        Check("13 kill minutes, 13 gather minutes: focus 0.5", $"{e.Focus}", "0.5");
        var json = System.Text.Json.JsonSerializer.Serialize(RunPayload.Build(e, settings, "run", "Open world", "na_e", "", items));
        Check("the run carries its focus", System.Text.RegularExpressions.Regex.Match(json, "\"focus\":[0-9.]+").Value, "\"focus\":0.5");
        Check("...and a run with neither carries none", System.Text.Json.JsonSerializer.Serialize(RunPayload.Build(new SessionEngine(items), settings, "run", "Open world", "na_e", "", items)).Contains("focus") ? "focus" : "absent", "absent");
        return ok;
    }

    /// <summary>Another character in the world (Steven, 2026-10-23: log off or switch character, close the run and start fresh).</summary>
    static bool RelogTest()
    {
        bool ok = true;
        void Check(string what, string got, string expect)
        {
            bool pass = got == expect; ok &= pass;
            Console.WriteLine($"  {what,-58} -> {got,-46} {(pass ? "ok" : "MISMATCH, expected " + expect)}");
        }
        Console.WriteLine("\nLogging off and another character (2026-10-23):");
        var t0 = new DateTime(2026, 10, 23, 18, 0, 0, DateTimeKind.Utc);
        // one tick: whether the counter shows, the balance it was last trusted at (and when), when the zone was last read
        string Tick(RelogDetector d, double s, bool counter, long? bal, double? trustedAt, double zoneAt) =>
            d.Observe(t0.AddSeconds(s), counter, bal, trustedAt is double ta ? (DateTime?)t0.AddSeconds(ta) : null, t0.AddSeconds(zoneAt)) ?? "none";
        var d1 = new RelogDetector();
        Tick(d1, 0, true, 500_000, 0, 0); Tick(d1, 10, true, 500_000, 10, 10);
        // a loading screen: both gone a while, the same balance after
        Tick(d1, 20, false, 500_000, 10, 10); Tick(d1, 30, false, 500_000, 10, 10); Tick(d1, 40, false, 500_000, 10, 10);
        Check("a loading screen (same balance after): nothing", $"{Tick(d1, 50, true, 500_000, 50, 50)}", "none");
        // a window over the counter while the zone still reads, and a sale meanwhile: nothing
        var d2 = new RelogDetector();
        Tick(d2, 0, true, 500_000, 0, 0);
        for (int s = 10; s <= 60; s += 10) Tick(d2, s, false, 500_000, 0, s);
        Check("the counter hidden, the zone still read, a sale: nothing", $"{Tick(d2, 70, true, 650_000, 70, 70)}", "none");
        // the character screen, then another character: both gone 40 s, then a new balance read twice
        var d3 = new RelogDetector();
        Tick(d3, 0, true, 500_000, 0, 0);
        for (int s = 10; s <= 50; s += 10) Tick(d3, s, false, 500_000, 0, 0);
        Check("...back with a balance not yet read twice: wait", $"{Tick(d3, 60, true, 500_000, 0, 60)}", "none");
        Check("another character (a new balance, read twice): reset", Tick(d3, 70, true, 1_250_000, 70, 70).Contains("another character") ? "reset" : "none", "reset");
        Check("...and not again on the next read", $"{Tick(d3, 80, true, 1_250_000, 80, 80)}", "none");
        // the same character logging back in: the same balance, nothing
        var d4 = new RelogDetector();
        Tick(d4, 0, true, 777, 0, 0);
        for (int s = 10; s <= 60; s += 10) Tick(d4, s, false, 777, 0, 0);
        Check("the same character back (the same balance): nothing", $"{Tick(d4, 70, true, 777, 70, 70)}", "none");
        // a short blink of both (under 20 s) with a changed balance is not a log-off
        var d5 = new RelogDetector();
        Tick(d5, 0, true, 1000, 0, 0); Tick(d5, 5, false, 1000, 0, -30);
        Check("both gone under 20 s: nothing", $"{Tick(d5, 15, true, 2000, 15, 15)}", "none");
        return ok;
    }

    static bool BestTest(ItemDictionary items)
    {
        bool ok = true;
        void Check(string what, string got, string expect)
        {
            bool pass = got == expect; ok &= pass;
            Console.WriteLine($"  {what,-58} -> {got,-46} {(pass ? "ok" : "MISMATCH, expected " + expect)}");
        }
        Console.WriteLine("\nRegion prices, best hour per zone, the panel's figures (0.7.6):");
        var tmp = Path.Combine(Path.GetTempPath(), "kl-best-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tmp);
        var oldPrices = PriceBook.PathOverride; var oldDir = BestRuns.DirOverride;
        try
        {
            PriceBook.PathOverride = Path.Combine(tmp, "prices.json"); BestRuns.DirOverride = tmp; BestRuns.Reset();
            PriceBook.SetBoard(new() { ["Spiritstone"] = 7_000 });
            PriceBook.SetRegionBoards(new[] { new Dictionary<string, long> { ["Thick hide"] = 1_000, ["Spiritstone"] = 9_000 }, new Dictionary<string, long> { ["Thick hide"] = 1_400 }, new Dictionary<string, long> { ["Thick hide"] = 1_100 } });
            Check("server board first, the region's middle price second", $"{PriceBook.Price("Spiritstone")} {PriceBook.SourceOf("Spiritstone")} {PriceBook.Price("thick hide")} {PriceBook.SourceOf("Thick hide")} {PriceBook.SourceOf("Manastone")}", "7000 Server 1100 Region None");

            var t0 = DateTime.UtcNow;
            long hide = items.IdOf("Thick hide");
            var stay = new SessionEngine(items);
            stay.SetZone("Western Cantas Valley");
            for (int i = 0; i < 10; i++) stay.Apply(new KinahChanged(t0, 5_000, false));   // ten mobs (one 50,000 line would be a quest reward)
            stay.Apply(new ItemAcquired(t0, hide, 10, false));
            Check("a stay's value: Kinah plus loot after the cut", BestRuns.Value(stay).ToString(), (50_000 + (long)Math.Round(11_000 * PriceBook.SellerKeeps)).ToString());
            Check("under ten minutes doesn't count", BestRuns.Offer("Siel (Elyos)", "Western Cantas Valley", 100_000, 9 * 60).ToString(), "False");
            Check("twenty minutes sets the best", $"{BestRuns.Offer("Siel (Elyos)", "Western Cantas Valley", 100_000, 20 * 60)} {BestRuns.For("Siel (Elyos)", "Western Cantas Valley")?.PerHr}", "True 300000");
            Check("a slower stay leaves it", $"{BestRuns.Offer("Siel (Elyos)", "Western Cantas Valley", 100_000, 40 * 60)} {BestRuns.For("Siel (Elyos)", "Western Cantas Valley")?.PerHr}", "False 300000");
            Check("kept per server", (BestRuns.For("Nezekan (Elyos)", "Western Cantas Valley") == null).ToString(), "True");
            BestRuns.Reset();
            Check("read back from best.json", BestRuns.For("Siel (Elyos)", "Western Cantas Valley")?.PerHr.ToString() ?? "none", "300000");

            Check("panel fields in panel order", string.Join(",", Ui.HudFields.Parse("total, rate,time,nope")), "time,rate,total");
            Check("an empty choice keeps the clock", string.Join(",", Ui.HudFields.Parse("")), "time");

            // the 0.7.7 test work, ported: bases, short loot-list names, vendor costs
            Check("a base is a base, a field isn't", $"{ZoneLocator.IsBase("Dawn Legion Base")} {ZoneLocator.IsBase("Andsha Forward Base")} {ZoneLocator.IsBase("Western Cantas Valley")}", "True True False");
            Check("a short name counts only read exactly", $"{items.KnowsExactly("Aria")} {items.KnowsExactly("Aryia")}", "True False");
            var pepper = RecipeBook.ForOutput("Artisan's Fruit Salad")?.Materials.FirstOrDefault(m => m.Item.Contains("(Bound)"))?.Item;
            if (pepper != null)
            {
                PriceBook.SetBoard(new() { ["Fresh Fruit"] = 900 });
                var before = RecipeBook.CostPerTry("Artisan's Fruit Salad", out _, out int vendorBefore);
                PriceBook.SetVendorPrice(pepper, 150);
                var after = RecipeBook.CostPerTry("Artisan's Fruit Salad", out _, out int vendorAfter);
                PriceBook.SetVendorPrice(pepper, 0);
                Check("a typed NPC cost joins the recipe's cost", $"{before} vendor {vendorBefore} -> {after} vendor {vendorAfter}", "4500 vendor 1 -> 4800 vendor 0");
            }
        }
        finally
        {
            PriceBook.PathOverride = oldPrices; BestRuns.DirOverride = oldDir; BestRuns.Reset();
            PriceBook.SetBoard(new()); PriceBook.SetRegionBoards(Array.Empty<Dictionary<string, long>>());
            try { Directory.Delete(tmp, true); } catch (Exception) { }
        }
        return ok;
    }

    /// <summary>
    /// The 0.7 reads: Market sale, listing and expiry lines, craft and enchant results, how the engine counts them,
    /// what the run payload carries, and how the balance counter is found and trusted. Only "Successfully crafted …" is a
    /// confirmed wording; the rest are the likely ones and get replaced by the client's own lines once they're collected.
    /// </summary>
    static bool TradeTest(ItemDictionary items)
    {
        bool ok = true;
        void Check(string what, string got, string expect)
        {
            bool pass = got == expect; ok &= pass;
            Console.WriteLine($"  {what,-58} -> {got,-46} {(pass ? "ok" : "MISMATCH, expected " + expect)}");
        }
        var parser = new ChatParser(items);
        string One(string line) { var evs = parser.Parse(line, DateTime.UtcNow).Select(e => Describe(e, items)).ToList(); return evs.Count == 0 ? "(ignored)" : string.Join("; ", evs); }

        Console.WriteLine("\nMarket, craft and enchant lines (0.7):");
        (string line, string expect)[] lines =
        {
            ("Your Spiritstone x10 sold for 75,200 Kina.", "sold: Spiritstone ×10 for 75,200"),
            ("Spiritstone x10 has been sold for 75,200 Kina.", "sold: Spiritstone ×10 for 75,200"),
            ("Sold Raw Leather x20 for 298,980 Kina.", "sold: Raw Leather ×20 for 298,980"),
            ("The Market sold your Fine Odyle x2 for 37,8OO Kina.", "sold: Fine Odyle ×2 for 37,800"),
            ("Registered Spiritstone x10 on the Market for 75,200 Kina.", "listed: Spiritstone ×10 for 75,200"),
            ("Spiritstone x5 has been registered on the Market.", "listed: Spiritstone ×5"),
            ("Join request registered.", "(ignored)"),
            ("Listing for Spiritstone x10 has expired.", "expired: Spiritstone ×10"),
            ("Your Raw Leather listing has expired.", "expired: Raw Leather ×1"),
            ("Blessing of Rock has expired.", "(ignored)"),
            ("Successfully crafted Orichalcum Ingot.", "craft ok: Orichalcum Ingot"),
            ("Successfully crafted Orichalcl", "craft ok: Orichalcl"),
            ("Great success! Successfully crafted Orichalcum Ingot x2.", "craft ok: Orichalcum Ingot"),
            ("Crafting failed.", "craft failed: ?"),
            ("Failed to craft Orichalcum Ingot.", "craft failed: Orichalcum Ingot"),
            ("Orichalcum Ingot crafting failed.", "craft failed: Orichalcum Ingot"),
            ("Enchantment succeeded: Tahabata's Sword +11", "enchant ok: Tahabata's Sword +10 → +11"),
            ("Successfully enchanted Tahabata's Sword to +11.", "enchant ok: Tahabata's Sword +10 → +11"),
            ("Enchantment failed. Tahabata's Sword +10", "enchant failed: Tahabata's Sword +10 → +11"),
            ("Failed to enchant Tahabata's Sword +10.", "enchant failed: Tahabata's Sword +10 → +11"),
            ("Enchanting failed. The level dropped to +9.", "enchant failed: ? +? → +?"),
            ("Enchantment Stone acquired.", "(ignored)"),
            ("Acquired 67,676 Kina from the Market.", "reward: Market proceeds ×1 (skipped)"),
            ("Acquired 520 Kina.", "Kinah +520"),
            ("[ZenBytez] Crafted a Chalice cause my first", "(ignored)"),
        };
        foreach (var (line, expect) in lines) Check(line, One(line), expect);

        Console.WriteLine("\nEngine (0.7):");
        var e = new SessionEngine(items); var t0 = DateTime.UtcNow;
        long ingot = items.IdOf("Orichalcum Ingot");
        e.Apply(new CraftResult(t0, "Orichalcum Ingot", true));
        e.Apply(new ItemAcquired(t0.AddSeconds(1), ingot, 1, false));                // the craft's own item line
        e.Apply(new CraftResult(t0.AddSeconds(20), "", false));                       // "Crafting failed." right after: same recipe
        e.Apply(new ItemAcquired(t0.AddSeconds(30), ingot, 1, false));               // later, unrelated to a craft: a drop
        e.Apply(new EnchantResult(t0, "Tahabata's Sword", 10, 11, false));
        e.Apply(new EnchantResult(t0, "Tahabata's Sword", 10, 11, true));
        e.Apply(new EnchantResult(t0, "", -1, -1, false));
        var (crafts, enchants) = e.OddsSnapshot();
        var ingotKey = items.KeyOf(ingot);
        Check("craft output not a drop, later pickup is", $"skipped {e.CraftedSkipped}, drops {e.Items.GetValueOrDefault(ingot)}", "skipped 1, drops 1");
        Check("unnamed craft failure goes to the last recipe", crafts.TryGetValue(ingotKey, out var co) ? $"{co.Ok} ok, {co.Fail} failed" : "missing", "1 ok, 1 failed");
        Check("enchant step key", string.Join(" ", enchants.OrderBy(k => k.Key).Select(k => $"{k.Key}={k.Value.Ok}/{k.Value.Attempts}")), "?|?|?=0/1 Tahabata's Sword|10|11=1/2");
        var odds = OddsPayload.From(e);
        Check("shared odds leave out unknown items", $"crafts {odds?.crafts?.Count ?? 0}, enchants {string.Join(",", odds?.enchants?.Keys ?? Enumerable.Empty<string>())}", "crafts 1, enchants Tahabata's Sword|10|11");
        Check("synthetic item ids go out as names", items.KeyOf(items.IdOf("Some Unlisted Thing")), "Some Unlisted Thing");
        Check("net Kinah rounded to 100", $"{RunPayload.RoundNet(123_449)} {RunPayload.RoundNet(-1_250)} {RunPayload.RoundNet(49)}", "123400 -1300 0");
        var board = PriceBook.ParseBoard("""{"items":[{"item":"Spiritstone","lowest_ask":7499,"stale":false,"median_24h":7600},{"item":"Raw Leather","lowest_ask":15000,"stale":true,"median_24h":null,"median_7d":14900},{"item":"Fine Odyle","lowest_ask":null,"ref_price":18900,"ref_source":"a2"}]}""", items);
        Check("price board: fresh ask, else medians, else the reference", string.Join(" ", board.OrderBy(k => k.Key).Select(k => $"{k.Key}={k.Value}")), "Fine Odyle=18900 Raw Leather=14900 Spiritstone=7499");
        var idle = new SessionEngine(items); idle.Apply(new CraftResult(t0, "Orichalcum Ingot", true));
        Check("a craft-only count is shared as Crafting", GameWatcher.DominantKind(idle), "Crafting");
        var town = new SessionEngine(items); town.Apply(new ItemAcquired(t0, ingot, 1, false));
        Check("a stray pickup in town is not worth sharing", GameWatcher.Worth(town).ToString(), "False");

        Console.WriteLine("\nQuest rewards, zones and Kinah by enemy (0.7.1):");
        Check("payment line, then its Kinah", One("A total payment of 34,916 Kina has been") + " | " + One("Acquired 34,916 Kina."), "(ignored) | Kinah +34,916 (bound)");
        Check("a later pickup of another amount is a drop", One("Acquired 512 Kina."), "Kinah +512");
        Check("a Kinah line naming a quest", One("Acquired 20,000 Kina (Quest reward)."), "Kinah +20,000 (bound)");
        var qe = new SessionEngine(items);
        foreach (var amt in new long[] { 430, 452, 466, 501, 519, 523 }) qe.Apply(new KinahChanged(t0, amt, false));
        qe.Apply(new KinahChanged(t0, 34_916, false));   // an unmarked quest reward
        qe.Apply(new KinahChanged(t0, 2_400, false));    // an elite: well above the median, still a mob
        Check("one line far above the mobs' Kinah is a reward", $"farmed {qe.Kinah:N0}, skipped {qe.KinahSkipped:N0}, kills {qe.Kills}", "farmed 5,291, skipped 34,916, kills 7");
        var qe2 = new SessionEngine(items);
        qe2.Apply(new KinahChanged(t0, 30_000, false)); qe2.Apply(new KinahChanged(t0, 3_000, false));
        Check("before five pickups, over 25,000 is a reward", $"farmed {qe2.Kinah:N0}, skipped {qe2.KinahSkipped:N0}", "farmed 3,000, skipped 30,000");

        var ze = new SessionEngine(items);
        long hide = items.IdOf("Thick hide"), stone = items.IdOf("Spiritstone");
        ze.SetZone("Western Cantas Valley");
        ze.Apply(new KinahChanged(t0, 450, false)); ze.Apply(new ItemAcquired(t0, hide, 2, false));
        ze.SetZone("Altamia Canyon");
        ze.Apply(new KinahChanged(t0, 470, false)); ze.Apply(new ItemAcquired(t0, stone, 1, false)); ze.Apply(new ItemAcquired(t0, stone, 1, true));
        var zp = RunPayload.Build(ze, new Settings(), "Open world", "Open world", "na_e", "", items);
        string ZoneItems(string z) => zp.zones.TryGetValue(z, out var zr) ? string.Join(",", zr.items.Select(kv => $"{(long.TryParse(kv.Key, out var iid) ? items.NameOf(iid) : kv.Key)}={kv.Value}")) + "/" + string.Join(",", zr.items_bound.Select(kv => $"{(long.TryParse(kv.Key, out var iid) ? items.NameOf(iid) : kv.Key)}={kv.Value}")) : "missing";
        Check("each zone sends its own items", $"{ZoneItems("Western Cantas Valley")} | {ZoneItems("Altamia Canyon")}", $"{items.NameOf(hide)}=2/ | {items.NameOf(stone)}=1/{items.NameOf(stone)}=1");
        // "Looks wrong?" on a row keeps that item out of everything sent to the site (Steven, 2026-10-21)
        ReportedDrops.Toggle(items.NameOf(stone));
        var rp2 = RunPayload.Build(ze, new Settings(), "Open world", "Open world", "na_e", "", items);
        string Has(Dictionary<string, int> d) => d.Keys.Any(k => long.TryParse(k, out var iid) && iid == stone) ? "sent" : "not sent";
        Check("a reported drop is not sent: run, zone, bound", $"{Has(rp2.items)} {Has(rp2.zones["Altamia Canyon"].items)} {Has(rp2.zones["Altamia Canyon"].items_bound)} · other drops {(rp2.zones["Western Cantas Valley"].items.Count > 0 ? "sent" : "missing")}", "not sent not sent not sent · other drops sent");
        ReportedDrops.Toggle(items.NameOf(stone));
        Check("undo: it is sent again", Has(RunPayload.Build(ze, new Settings(), "Open world", "Open world", "na_e", "", items).items), "sent");
        // the card a click on a drop opens reads two site files: the zones snapshot and the listed drops
        {
            var zj = "{\"publish_rule\":{\"min_runs\":5},\"windows\":{\"7d\":[{\"zone\":\"Altamia Canyon\",\"activity\":\"pets\",\"n_runs\":9,\"rates\":{}},{\"zone\":\"Altamia Canyon\",\"activity\":\"open-world\",\"n_runs\":7,\"rates\":{\"kina_hr\":1},\"drops\":{\"" + stone + "\":{\"per_kill\":0.04,\"per_hour\":12.5,\"n_runs\":6}}},{\"zone\":\"Kraka Footpath\",\"activity\":\"open-world\",\"n_runs\":2,\"rates\":null}]}}";
            var lj = "{\"items\":{\"" + stone + "\":{\"name\":\"Spiritstone\",\"url\":\"https://metabot.gg/en/aion-2/items/spiritstone\",\"sources\":[{\"monster\":\"Stonebeak\",\"chance\":0.02,\"zone\":\"Altamia Canyon\",\"map\":\"Altamia\",\"level\":12},{\"monster\":\"Far Away\",\"chance\":0.5,\"zone\":\"Elsewhere\"}]}}}";
            SiteData.Load(zj, lj);
            var sz = SiteData.Zone("Altamia Canyon"); var kz = SiteData.Zone("Kraka Footpath");
            Check("site card: the open-world bucket, its rate for the item", sz != null && sz.Drops.TryGetValue(stone.ToString(), out var sr) ? $"{sz.Activity} {sz.Runs} runs · {sr.PerKill} per kill · {sr.PerHour}/hr" : "missing", "open-world 7 runs · 0.04 per kill · 12.5/hr");
            Check("site card: a zone under the publish rule says so", kz == null ? "missing" : $"published {kz.Published} · {kz.Runs} of {SiteData.MinRuns}", "published False · 2 of 5");
            var sl = SiteData.ListedFor(stone, "Spiritstone");
            Check("site card: listed chances with their zone", sl == null ? "missing" : string.Join(", ", sl.Sources.Select(x => $"{x.Monster}@{x.Zone}={x.Chance}")), "Stonebeak@Altamia Canyon=0.02, Far Away@Elsewhere=0.5");
            SiteData.Load("{}", "{}");
        }
        // a drop that looks like a normal drop is that drop: accents and stray marks don't make a new item
        Check("accents and marks snap to the real item", $"{items.IdOf("Raw Léather") == items.IdOf("Raw Leather")} {items.IdOf("Raw Leather~") == items.IdOf("Raw Leather")} {items.Knows("Spiritstöne")}", "True True True");

        var me = new SessionEngine(items);
        me.SetZone("Western Cantas Valley");
        me.SetTarget("Test Lizard", t0);
        me.Apply(new KinahChanged(t0.AddSeconds(2), 500, false));
        me.Apply(new ItemAcquired(t0.AddSeconds(3), hide, 1, false));
        me.Apply(new PetCrystal(t0.AddSeconds(4), "Natura Soul Crystal", 1));
        me.Apply(new KinahChanged(t0.AddSeconds(12), 480, false));   // past the 8-second grace: nobody's
        me.SetTarget("Test Lizard", t0.AddSeconds(20));
        me.Apply(new KinahChanged(t0.AddSeconds(21), 520, false));
        me.SetZone("Altamia Canyon");
        me.Apply(new KinahChanged(t0.AddSeconds(22), 510, false));   // same name, new zone: its own row
        var mobs = me.MobSnapshot().OrderBy(m => m.Zone).Select(m => $"{m.Name}@{m.Zone}: {m.Kills} kills {m.Kinah} Kinah {m.Drops.Values.Sum()} drops {m.Crystals} crystals");
        Check("Kinah by enemy, 8-second grace, by zone", string.Join(" | ", mobs), "Test Lizard@Altamia Canyon: 1 kills 510 Kinah 0 drops 0 crystals | Test Lizard@Western Cantas Valley: 2 kills 1020 Kinah 1 drops 1 crystals");
        var mp = RunPayload.Build(me, new Settings(), "Open world", "Open world", "na_e", "", items);
        Check("mobs go out with their zone", string.Join(" | ", (mp.mobs ?? new()).OrderBy(m => m.zone).Select(m => $"{m.name}/{m.zone}/{m.kills}/{m.kinah}")), "Test Lizard/Altamia Canyon/1/510 | Test Lizard/Western Cantas Valley/2/1020");
        Check("a run with no target bar sends no mobs", RunPayload.Build(ze, new Settings(), "Open world", "Open world", "na_e", "", items).mobs == null ? "none" : "some", "none");
        Check("enemy list built in", TargetReader.Known.Length > 0 ? "yes" : "no (data/mobs.txt missing)", "yes");
        {
            // kills from the evidence that agrees on them: a kill's Kinah and its Soul Points line are one kill; a kill
            // whose Kinah line was lost still counts from its Soul Points line; two kills at once are two
            var ke = new SessionEngine(items); var k0 = DateTime.UtcNow;
            void Pick(double s2, long amt) => ke.Apply(new KinahChanged(k0.AddSeconds(s2), amt, false));
            void Soul(double s2, string kind) => ke.Apply(new CurrencyGained(k0.AddSeconds(s2), kind + " Soul Points", 1, kind));
            Pick(0, 400); Soul(0.6, "Drakan Probe");                       // one kill
            Pick(5, 300); Pick(5, 310); Soul(5.8, "Drakan Probe"); Soul(5.8, "Kerubian");   // two at once
            Soul(12, "Drakan Probe");                                      // its Kinah line was lost
            Pick(20, 250);                                                 // an enemy with no Soul Points
            // the loot list's "Soul: <enemy>" row: a kill of that enemy, joined to the same kill's Kinah and Soul Points line
            Check("soul rows in the loot list", $"{LootCrossCheck.SoulOf("Soul: Papis (Bound)")} | {LootCrossCheck.SoulOf("Soul: Drakan Probe")} | {LootCrossCheck.SoulOf("Cogni Soul Crystals") ?? "none"} | {LootCrossCheck.SoulOf("Raw Leather") ?? "none"} | {LootCrossCheck.SoulOf("Soul: Papis (Boundj'")} | [{LootCrossCheck.SoulOf("Soul")}]", "Papis | Drakan Probe | none | none | Papis | []");
            var se2 = new SessionEngine(items); var q0 = DateTime.UtcNow;
            se2.Apply(new KinahChanged(q0, 400, false)); se2.Apply(new EnemySoul(q0.AddSeconds(0.5), "Drakan Probe")); se2.Apply(new CurrencyGained(q0.AddSeconds(0.8), "Drakan Probe Soul Points", 1, "Drakan Probe"));
            se2.Apply(new EnemySoul(q0.AddSeconds(6), "Kerubian"));          // the chat lost this kill's lines; the list's soul row names it
            Check("a kill seen three ways is one; a soul row alone is a kill", $"{se2.Totals().Kills} kills · " + string.Join(" | ", se2.MobSnapshot().OrderBy(m => m.Name).Select(m => $"{m.Name}: {m.Kills} kills {m.Kinah}")), "2 kills · Drakan Probe: 1 kills 400 | Kerubian: 1 kills 0");
            Check("kills from Kinah and Soul Points together", $"{ke.Totals().Kills} kills ({ke.KillsSeen} seen, {ke.KinahDrops} Kinah, {ke.CurrencyLines} Soul Points)", "5 kills (5 seen, 4 Kinah, 4 Soul Points)");
        }
        {
            // each kill prints "Obtained 1 <kind> Soul Points.": that names the enemy, whatever the target bar read
            // (2026-10-22 11:01: three kinds killed, the bar misread as "Drana Processing Mainte", "Dtana Proc", "Medil")
            var sp = new ChatParser(items); var se = new SessionEngine(items); var s0 = DateTime.UtcNow;
            se.SetZone("Western Cantas Valley");
            void L(int sec, string line) { foreach (var ev in sp.Parse(line, s0.AddSeconds(sec))) se.Apply(ev); }
            se.SetTarget("Medil", s0);                                                   // a misread that is no enemy: nothing should go to it
            L(1, "Acquired 400 Kina."); L(1, "Obtained 1 Dracuni Herbalist Soul Points.");
            L(8, "Acquired 486 Kina."); L(9, "Obtained 1 Drakan Probe Soul Points.");
            se.SetTarget("Eternal Kerubian", s0.AddSeconds(14));
            L(15, "Acquired 352 Kina."); L(16, "Obtained 1 Kerubian Soul Points.");
            L(30, "Acquired 300 Kina."); L(30, "Obtained 1 Dracunt Herbalist Soul Points.");   // the same kind, misread
            var rows = se.MobSnapshot().OrderBy(m => m.Name).Select(m => $"{m.Name}: {m.Kills} kills {m.Kinah}");
            Check("Soul Points name the enemy killed", string.Join(" | ", rows), "Dracuni Herbalist: 2 kills 700 | Drakan Probe: 1 kills 486 | Eternal Kerubian: 1 kills 352");
            // the soul names a kind, not always the enemy (2026-10-22 15:00: "Fossa Soul Points" fighting Astin Mercenaries)
            Check("a soul kind and who has it", $"{TargetReader.EnemyForSoul("Fossa", new[] { "Astin Mercenaries Fossa" })} | {TargetReader.EnemyForSoul("Fossa", Array.Empty<string>())} | {TargetReader.EnemyForSoul("Fossa", Array.Empty<string>(), new[] { "Patrolling Fossa" })}", "Astin Mercenaries Fossa | Fossa | Patrolling Fossa");
            {
                // the nameplates: the enemy whose red bar goes as its loot lands is the one that died, whatever soul it gives
                // (2026-10-22 16:39, Southern Tolbas Forest: Astin enemies counted as Tolbas Slink from their "Slink" souls)
                var ne2 = new SessionEngine(items); var n0 = DateTime.UtcNow; ne2.SetZone("Southern Tolbas Forest");
                ne2.SetEngaged(new[] { "Astin Mercenaries Watcher", "Astin Mercenaries Fighter" }, n0);
                ne2.SetEngaged(new[] { "Astin Mercenaries Fighter" }, n0.AddSeconds(1));        // the Watcher's bar went
                ne2.Apply(new KinahChanged(n0.AddSeconds(1.5), 300, false));
                ne2.Apply(new CurrencyGained(n0.AddSeconds(2), "Slink Soul Points", 1, "Slink"));
                ne2.SetEngaged(Array.Empty<string>(), n0.AddSeconds(6));                       // the Fighter's too
                ne2.Apply(new KinahChanged(n0.AddSeconds(6.4), 410, false));
                Check("the enemy whose bar went is the kill", string.Join(" | ", ne2.MobSnapshot().OrderBy(m => m.Name).Select(m => $"{m.Name}: {m.Kills} kills {m.Kinah}")), "Astin Mercenaries Fighter: 1 kills 410 | Astin Mercenaries Watcher: 1 kills 300");
            }
            Check("a target bar cut short is its one enemy", TargetReader.Match("Drana Processing Mainte") ?? "none", "Drana Processing Maintenance Machine");
        }
        string Bar(params string[] ls) => TargetReader.FindName(ls.Select((l, i) => (i, l)), out var un) ?? (un.Length > 0 ? "none, unlisted: " + un : "none");
        Check("target bar: level first, distance after", Bar("45 Highland Karnif 1m"), "Highland Karnif");
        Check("target bar: the enemy's own target on the line", Bar("45 Highland Karnif 1m 45 Swoly"), "Highland Karnif");
        Check("target bar: a misread letter", Bar("45 Highland Kamif"), "Highland Karnif");
        Check("target bar: a gathering node is no enemy", Bar("Orichalcum", "Ore"), "none, unlisted: Orichalcum");
        Check("target bar: the tag under the bar alone", Bar("Fera"), "none, unlisted: Fera");
        Check("target bar: rn read for m", Bar("Hard Kalgolern"), "Hard Kalgolem");
        Check("target bar: rn for m, level and distance", Bar("45 Hard Kalgolern 2m"), "Hard Kalgolem");
        Check("target bar: a bare Kalgolem fits 26 names, so none", Bar("Kalgolern"), "none, unlisted: Kalgolern");
        Check("target bar: a name with a real rn still matches", Bar("45 Abyss Karnif"), "Abyss Karnif");
        Check("target bar: a game notice in the band is no name", Bar("Title acquired"), "none");
        Check("target bar: a notice above the enemy doesn't hide it", Bar("Quest completed", "45 Highland Karnif 1m"), "Highland Karnif");
        Check("target bar: an enemy named Failed is still an enemy", Bar("45 Failed Drana Mutant 3m"), "Failed Drana Mutant");
        {
            // a loot line read while it faded in is looked at again at its place in the next read
            var rr = new Reread(); var r0 = DateTime.UtcNow;
            rr.Remember("ria acquired", 0, r0);
            Check("fading line: read clean at the same place next time", string.Join("|", rr.Take(new[] { "Odyle acquired.", "Aria acquired." }, 0, r0.AddSeconds(1))), "Aria acquired.");
            rr.Remember("»eqwacquired.", 0, r0);
            Check("fast second gather: garbled, then clean, one line further up", string.Join("|", rr.Take(new[] { "Aria acquired.", "Aria acquired.", "Orichalcum Ore acquired." }, 1, r0.AddSeconds(1))), "Aria acquired.");
            rr.Remember("ria acquired", 0, r0);
            Check("a different line in that place is not taken", string.Join("|", rr.Take(new[] { "Acquired 436 Kina." }, 0, r0.AddSeconds(1))), "");
            rr.Remember("ria acquired", 0, r0);
            Check("still garbled: one more look", string.Join("|", rr.Take(new[] { "ria acquired" }, 0, r0.AddSeconds(1))) + "/" + string.Join("|", rr.Take(new[] { "Aria acquired." }, 0, r0.AddSeconds(2))), "/Aria acquired.");
            var rec = new Dictionary<string, DateTime>(); var b0 = DateTime.UtcNow;
            Reread.Remember(rec, new[] { "Orichalcum Ore acquired.", "Acquired 436 Kina.", "[Bob] hi" }, b0.AddMinutes(-1));
            Check("burst: a read sharing nothing with the last counts its unseen loot", string.Join("|", Reread.Burst(new[] { "Acquired 436 Kina.", "[Bob] hi" },
                new[] { "Lesser Refining Stone acquired.", "Acquired Fine Odyle x2.", "Spiritstone acquired.", "[Amy] gg" }, rec, b0)), "Lesser Refining Stone acquired.|Acquired Fine Odyle x2.|Spiritstone acquired.");
            Check("burst: a read that overlaps the last is no burst", string.Join("|", Reread.Burst(new[] { "Acquired 436 Kina.", "[Bob] hi" },
                new[] { "Acquired 436 Kina.", "Spiritstone acquired.", "Odyle acquired." }, rec, b0)), "");
            Check("burst: loot seen in the last ten minutes is history, not new", string.Join("|", Reread.Burst(new[] { "Acquired 436 Kina.", "[Bob] hi" },
                new[] { "Orichalcum Ore acquired.", "Orichalcum Ore acquirad.", "Spiritstone acquired." }, rec, b0)), "Spiritstone acquired.");
            Check("instances: an expedition area has a kind", ZoneLocator.KindOf("Auldor's Sanctum") ?? "none", "Expedition");
            Check("instances: an event area too", ZoneLocator.KindOf("Defend Shugo Merchants") ?? "none", "Event");
            {
                // inside an expedition, loot is the run's alone: the Urugugu Canyon reward cube's items were counted as drops
                // from kills (2026-10-22 15:22). Never on the Farming page, never the site's farming numbers.
                var ts = new TrackerSession(null, new Settings(), items, "Test"); var u0 = DateTime.UtcNow;
                ts.Engine.SetZone("Urugugu Canyon"); ts.ZoneView.SetZone("Urugugu Canyon"); ts.Instances.Zone("Urugugu Canyon", u0);
                ts.ProcessChat(new List<string> { "[Theo] gg" }, u0);
                ts.ProcessChat(new List<string> { "[Theo] gg", "Refining Stone acquired.", "Acquired 600 Kina." }, u0.AddSeconds(1));
                ts.ProcessChat(new List<string> { "[Theo] gg", "Refining Stone acquired.", "Acquired 600 Kina." }, u0.AddSeconds(2.2));   // the vote's next read
                var run = ts.Instances.Snapshot().LastOrDefault(r => r.Zone == "Urugugu Canyon" && r.Start >= u0.AddSeconds(-1));
                Check("inside an expedition, loot is the run's alone", $"farming: {ts.Engine.Items.Count} items {ts.Engine.Kinah} Kinah · run: {run?.Items.Values.Sum() ?? 0} items {run?.Kinah ?? 0} Kinah", "farming: 0 items 0 Kinah · run: 1 items 600 Kinah");
            }
            {
                // the reward table at the end of an expedition: a slot the chat already counted is skipped, a sure one is added
                // to the run, an unsure one (an event item the list lacks, measured 82 and up) is never guessed
                var rs = items.AllItems().First(i => i.Name == "Refining Stone");
                var key = IconIndex.KeyOf(rs.Icon);
                var gt = new TrackerSession(null, new Settings(), items, "Test"); var gt0 = DateTime.UtcNow;
                gt.Engine.SetZone("Urugugu Canyon"); gt.ZoneView.SetZone("Urugugu Canyon"); gt.Instances.Zone("Urugugu Canyon", gt0);
                gt.ProcessChat(new List<string> { "[x] a" }, gt0);
                gt.ProcessChat(new List<string> { "[x] a", "Acquired Refining Stone x5." }, gt0.AddSeconds(1));
                var box = new System.Drawing.Rectangle(0, 0, 10, 10);
                gt.ApplyGrid(new List<RewardGrid.Slot> {
                    new(box, rs.Grade, "5", 5, new() { (key, 39.6), ("Other", 98.7) }),
                    new(box, rs.Grade, "3", 3, new() { (key, 41.0), ("Other", 99.0) }),
                    new(box, "Common", "6", 6, new() { ("Icon_GM_0030_T04_Shoulder", 82.2), ("Other", 85.1) }) }, gt0.AddSeconds(3));
                var grun2 = gt.Instances.Snapshot().LastOrDefault();
                // the gold coin counts as Kinah only when the Kinah counter rose by exactly its amount (Urugugu: 17,889 to 617,889)
                var coin = IconIndex.KeyOf(items.AllWithIcons().First(i => i.Name == "Kina (Bound)").Icon);
                Slot600(out var withCounter, true); Slot600(out var without, false);
                void Slot600(out string got, bool counter)
                {
                    var cs = new TrackerSession(null, new Settings(), items, "Test"); var c0 = DateTime.UtcNow;
                    cs.Engine.SetZone("Urugugu Canyon"); cs.Instances.Zone("Urugugu Canyon", c0);
                    if (counter) { cs.SeedBalance(c0, 17_889); cs.SeedBalance(c0.AddSeconds(2), 617_889); }
                    cs.ApplyGrid(new List<RewardGrid.Slot> { new(new System.Drawing.Rectangle(0, 0, 10, 10), "Common", "600.0K", 600_000, new() { (coin, 84.4), ("Other", 103.7) }) }, c0.AddSeconds(3));
                    got = $"{cs.Instances.Snapshot().LastOrDefault(r => r.Start >= c0.AddSeconds(-1))?.Kinah ?? 0:N0}";
                }
                Check("a coin slot is Kinah when the counter agrees", $"{withCounter} · {without}", "600,000 · 0");
                // a clear match to the coin as the game draws it (Icon_gold, 4.4 against 84.4) needs no counter
                var cs2 = new TrackerSession(null, new Settings(), items, "Test"); var cz = DateTime.UtcNow;
                cs2.Engine.SetZone("Urugugu Canyon"); cs2.Instances.Zone("Urugugu Canyon", cz);
                cs2.ApplyGrid(new List<RewardGrid.Slot> { new(new System.Drawing.Rectangle(0, 0, 10, 10), "Common", "600.0K", 600_000, new() { (IconIndex.KeyOf(items.AllWithIcons().First(i => i.Name == "Kina").Icon), 4.4), (coin, 84.4) }) }, cz.AddSeconds(1));
                Check("a clear coin match is Kinah without the counter", $"{cs2.Instances.Snapshot().LastOrDefault(r => r.Start >= cz.AddSeconds(-1))?.Kinah ?? 0:N0}", "600,000");
                Check("the reward table into the run", string.Join(", ", (grun2?.Items ?? new()).Select(kv => $"{items.NameOf(kv.Key)} x{kv.Value}")), "Refining Stone x8");
            }
            {
                // Nightmare is known but left alone: its rewards never change, so nothing there is counted
                var ne = new SessionEngine(items); ne.SetZone("Nightmare");
                ne.Apply(new KinahChanged(DateTime.UtcNow, 5000, false)); ne.Apply(new ItemAcquired(DateTime.UtcNow, items.IdOf("Raw Leather"), 1, false));
                Check("Nightmare: known, never counted", $"{ZoneLocator.KindOf("Nightmare")} · {ne.Kinah} Kinah · {ne.Items.Count} items · {ne.Events} events", "Ignored · 0 Kinah · 0 items · 0 events");
            }
            Check("instances: an open-world zone has none", ZoneLocator.KindOf("Eastern Cantas Valley") ?? "none", "none");
            Check("instances: they are on the zone list", ZoneLocator.Known.Contains("Urugugu Canyon") ? "yes" : "no", "yes");
            Check("timed events: Shugo Festival games and invasions", string.Join(",", new[] { "Odyle Flight Frenzy", "Jump Jump", "Up! Up! Up!", "Not This Tile?", "Incursion of the Spirits", "Beritra Air Raid" }.Select(z => ZoneLocator.KindOf(z) ?? "none")), "Event,Event,Event,Event,Event,Event");
            Check("an expedition's areas file under it", $"{ZoneLocator.RunName("Auldor's Sanctum")} | {ZoneLocator.RunName("Urugugu Village")} | {ZoneLocator.RunName("Urugugu Canyon")} | {ZoneLocator.KindOf("Auldor's Sanctum")}", "Urugugu Canyon | Urugugu Canyon | Urugugu Canyon | Expedition");
            Check("the closest zone, not the first close enough", $"{ZoneLocator.Match("Southern Tolbas Forest")} | {ZoneLocator.Match("Nortnern Tolbas Forest")}", "Southern Tolbas Forest | Northern Tolbas Forest");
            Check("event titles pass the label filter, UI labels don't", $"{ZoneLocator.Candidate("Jump Jump")} {ZoneLocator.Candidate("Up! Up! Up!")} {ZoneLocator.Candidate("Not This Tile?")} {ZoneLocator.Candidate("Jump Higher")}", "True True True False");
            {
                var r = new InstanceRun { Zone = "Urugugu Canyon", Kind = "Expedition", Start = b0 };
                r.LastLoot = b0.AddMinutes(7);
                Check("an expedition run lasts from entry to the last loot", TimeSpan.FromSeconds(r.Seconds).ToString(@"m\:ss"), "7:00");
                var none = new InstanceRun { Zone = "Shugo Festival", Kind = "Event", Start = b0 };
                Check("a run with no loot has no length", none.Seconds.ToString("0"), "0");
            }
            {
                // instance runs ride along with a run: listed areas only, items keyed like the rest of the payload
                var a = new InstanceRun { Zone = "Auldor's Sanctum", Kind = "Expedition", Start = b0, LastLoot = b0.AddSeconds(545), Kinah = 120_000, Kills = 31 };
                a.Items[hide] = 2;
                var off = new InstanceRun { Zone = "Eastern Cantas Valley", Kind = "Expedition", Start = b0, LastLoot = b0.AddMinutes(5), Kinah = 9 };
                var open = new InstanceRun { Zone = "Shugo Festival", Kind = "Event", Start = b0 };
                var ip = RunPayload.Build(ze, new Settings(), "Open world", "Open world", "na_e", "", items, new[] { a, off, open });
                Check("instance runs: listed areas with loot only, keyed like the rest", string.Join(" | ", (ip.instance_runs ?? new()).Select(r => $"{r.zone}/{r.kind}/{r.seconds}/{r.kinah}/{r.kills}/{string.Join(",", r.items.Select(kv => $"{(long.TryParse(kv.Key, out var iid) ? items.NameOf(iid) : kv.Key)}={kv.Value}"))}")), $"Auldor's Sanctum/Expedition/545/120000/31/{items.NameOf(hide)}=2");
                Check("instance runs: none, no field sent", System.Text.Json.JsonSerializer.Serialize(zp).Contains("instance_runs") ? "sent" : "absent", "absent");
                var json = System.Text.Json.JsonSerializer.Serialize(ip);
                Check("instance runs: only the run's own figures leave the PC", json.Contains("\"kind\":\"Expedition\",\"seconds\":545,\"kinah\":120000,\"kills\":31,\"items\":{") ? "yes" : json[json.IndexOf("instance_runs", StringComparison.Ordinal)..], "yes");
                // each finished run is handed over once (the farming run and the chunk both share), back after a failed send, let go when old
                var log = new InstanceLog();
                log.Zone("Auldor's Sanctum", b0);
                log.Loot(new KinahChanged(b0.AddMinutes(3), 50_000, false));
                log.Zone("Eastern Cantas Valley", b0.AddMinutes(4));
                // out in the open world, the run takes its rewards for InstanceLog.RewardGraceSeconds, then closes
                log.Zone("Eastern Cantas Valley", b0.AddMinutes(4).AddSeconds(InstanceLog.RewardGraceSeconds + 1));
                var first = log.TakeUnshared(b0); var second = log.TakeUnshared(b0);
                log.Restore(first); var third = log.TakeUnshared(b0);
                log.Restore(third); var stale = log.TakeUnshared(b0.AddHours(1));
                Check("instance runs: handed over once, back after a failed send, let go when old", $"{first.Count}/{second.Count}/{third.Count}/{stale.Count}", "1/0/1/0");
                log.Zone("Shugo Festival", b0.AddMinutes(10));
                log.Loot(new KinahChanged(b0.AddMinutes(12), 40_000, false));
                Check("instance runs: a run still open isn't handed over", log.TakeUnshared(b0).Count.ToString(), "0");
            }
            Check("player chat is not loot-shaped", Reread.LooksLikeLoot("[Drogo] bro i live between 1984 and 2008") ? "yes" : "no", "no");
        }
        Check("target bar: a garbled read keeps the enemy on the bar", TargetReader.Close("Hgrcl Kalgolem", "Hard Kalgolem") ? "yes" : "no", "yes");
        Check("target bar: a bare species keeps it too", TargetReader.Close("Kalgolern", "Hard Kalgolem") ? "yes" : "no", "yes");
        Check("target bar: another enemy doesn't", TargetReader.Close("Abyss Drakan Scout", "Hard Kalgolem") ? "yes" : "no", "no");
        Check("target bar: a UI word doesn't", TargetReader.Close("Trade Shop", "Hard Kalgolem") ? "yes" : "no", "no");
        Check("target bar: and its rn misread as m too", Bar("Abyss Kamif"), "Abyss Karnif");

        Console.WriteLine("\nCrafting and Market settlements (2026-10-21 log):");
        {
            var cp = new ChatParser(items); var ce = new SessionEngine(items); ce.SetZone("Eastern Altamia Highland");
            void Feed(params string[] ls) { foreach (var (_, evs) in cp.ParseBatch(ls, DateTime.UtcNow)) foreach (var ev in evs) ce.Apply(ev); }
            Feed("Successfully crafted Artisan's Orichalcum", "Longsword.", "Artisan's Orichalcum Longsword acquired.");
            var made = ce.CraftSnapshot();
            Check("a wrapped craft line: its item is the craft's output", string.Join(" | ", made.Select(r => $"{r.Name}: {r.Ok} ok, made {string.Join(",", r.Made.Select(m => m.Key + " x" + m.Value))}")), "Artisan's Orichalcum Longsword: 1 ok, made Artisan's Orichalcum Longsword x1");
            var zs = ce.ZoneSnapshot();
            Check("and never a drop or a zone item", $"drops {ce.Items.Count + ce.ItemsBound.Count}, zone items {zs.Values.Sum(z => z.Items.Count + z.ItemsBound.Count)}, farming {ce.Active.TotalSeconds:0}s", "drops 0, zone items 0, farming 0s");
            // the crafting window hides the chat: only the item line is read (2026-10-21 16:37). With the window seen
            // open, an item a recipe makes is a craft; with it closed, the same line is a pickup as before
            var cw2 = new SessionEngine(items); var cwt = DateTime.UtcNow;
            cw2.SetCraftWindow(cwt);
            cw2.Apply(new ItemAcquired(cwt.AddSeconds(2), items.IdOf("Artisan's Orichalcum Longsword"), 1, false));
            Check("crafting window open, no crafted line: still a craft", $"{string.Join(",", cw2.CraftSnapshot().Select(r => r.Name + " " + r.Ok + " ok"))} · drops {cw2.Items.Count}", "Artisan's Orichalcum Longsword 1 ok · drops 0");
            var cw3 = new SessionEngine(items);
            cw3.Apply(new ItemAcquired(cwt, items.IdOf("Artisan's Orichalcum Longsword"), 1, false));
            Check("crafting window not seen: a pickup as before", $"crafts {cw3.CraftSnapshot().Count} · drops {cw3.Items.Count}", "crafts 0 · drops 1");
            Check("the crafting window's skill tabs", $"{TargetReader.IsCraftTab("Armorsmithing")} {TargetReader.IsCraftTab("Cookinq")} {TargetReader.IsCraftTab("Highland Karnif")}", "True True False");
            Feed("Crafting failed.");
            Check("an unnamed failure goes to the last recipe", string.Join(" | ", ce.CraftSnapshot().Select(r => $"{r.Name} {r.Ok}/{r.Attempts}")), "Artisan's Orichalcum Longsword 1/2");
            Feed("Acquired 44,998 Kina.", "A total payment of 61,765 Kina has been", "completed.", "Acquired 16,767 Kina.");
            Check("a settlement paid out before and after its total line", $"farmed {ce.Kinah:N0}, skipped {ce.KinahSkipped:N0}", "farmed 0, skipped 61,765");
            Feed("Acquired 512 Kina.");
            Check("the next pickup is loot again", $"farmed {ce.Kinah:N0}", "farmed 512");
            Check("recipe book: the Longsword's materials", RecipeBook.ForOutput("Artisan's Orichalcum Longsword") is { } rb ? string.Join(", ", rb.Materials.Select(m => $"{m.Qty} {m.Item}")) : "missing", "1 Expert's Splendent Orichalcum Longsword, 5 Artisan's Refining Stone, 8 Pure Orichalcum Ore, 4 Pure Odyle");
            PriceBook.SetBoard(new() { ["Expert's Splendent Orichalcum Longsword"] = 400_000, ["Artisan's Refining Stone"] = 20_000, ["Pure Orichalcum Ore"] = 5_000, ["Pure Odyle"] = 6_000 });
            Check("cost of one try at Market prices", $"{RecipeBook.CostPerTry("Artisan's Orichalcum Longsword", out var unp):N0} ({unp} unpriced)", "564,000 (0 unpriced)");
            PriceBook.SetBoard(new());
        }

        Console.WriteLine("\nDaeva Pass, shops and purchases (2026-10-21 log):");
        {
            Check("pass, shop and reward titles", string.Join(" ", new[] { "Daeva Pass", "Pass Shop", "Daeva Pass Level Reward", "Trade Shop", "Quna Shop", "Highland Karnif", "Orichalcum", "Status" }.Select(x => TargetReader.IsRewardTitle(x) ? "R" : "-")), "R R R R R - - -");
            var tr = new TargetReader(); var rt = DateTime.UtcNow;
            tr.MarkReward(rt);
            Check("a reward screen stays open 3 s after its title", $"{tr.RewardOpen(rt.AddSeconds(2.9))} {tr.RewardOpen(rt.AddSeconds(5))}", "True False");
            var rp = new ChatParser(items); var re = new SessionEngine(items);
            void Batch(bool open, params string[] ls) { foreach (var (_, evs) in rp.ParseBatch(ls, DateTime.UtcNow)) foreach (var ev in evs) re.Apply(open ? rp.AsReward(ev) : ev); }
            Batch(true, "Acquired 5,000 Kina.", "Fine Odyle acquired.", "Odyle (Bound) acquired.");
            Check("in a reward screen: Kinah and items are rewards", $"farmed {re.Kinah:N0}, drops {re.Items.Count + re.ItemsBound.Count}, skipped {re.RewardsSkipped} lines ({re.KinahSkipped:N0} Kinah), farming {re.Active.TotalSeconds:0}s", "farmed 0, drops 0, skipped 3 lines (5,000 Kinah), farming 0s");
            Batch(false, "Acquired 200 Kina.", "Obtained 1 Karnif Soul Points.");
            Check("the kill 5 s later still counts", $"farmed {re.Kinah:N0}, kills {re.Kills}", "farmed 200, kills 1");
            var pp = new ChatParser(items); var pe = new SessionEngine(items);
            void Buy(params string[] ls) { foreach (var (_, evs) in pp.ParseBatch(ls, DateTime.UtcNow)) foreach (var ev in evs) pe.Apply(ev); }
            Buy("Acquired 300 Kina.", "Purchase complete.", "Fine Odyle acquired.");
            Buy("Odyle acquired.");
            Check("bought items, this read and the next, are not drops", $"drops {pe.Items.Values.Sum()}, skipped {pe.RewardsSkipped}, farmed {pe.Kinah}", "drops 0, skipped 2, farmed 300");
            Buy("Odyle acquired.");
            Check("the read after that counts again", $"drops {pe.Items.Values.Sum()}", "drops 1");
            Check("parse-test writes to a scratch log", CrashLog.Dir.Contains("parse-test") ? "scratch" : CrashLog.Dir, "scratch");
            Check("not an enemy: a quest step", TargetReader.Match("Pass through the narrow gap") ?? "none", "none");
        }

        Console.WriteLine("\nLoot tables (which enemy can drop what):");
        {
            var keep = LootBook.Current;
            LootBook.Current = LootBook.FromJson("""
                {"tables": {
                  "Stonebeak": {"drops": [{"item": "Raw Leather", "chance": 0.2}, {"item": "Spiritstone", "chance": 0.0667}]},
                  "Hard Kalgolem": {"drops": [{"item": "Lesser Abyssal Manastone", "chance": 0.1}]}
                }}
                """);
            var lb = LootBook.Current;
            Check("leather from a Stonebeak, never from a rock", $"{lb.CanDrop("Stonebeak", "Raw Leather")} {lb.CanDrop("Hard Kalgolem", "Raw Leather")} {lb.CanDrop("Highland Karnif", "Raw Leather")?.ToString() ?? "unknown"}", "True False unknown");
            Check("who drops Raw Leather", string.Join(",", lb.SourcesOf("raw leather")), "Stonebeak");
            {
                // odds pool every enemy that can drop the item: two tables listing meat both count, a rock doesn't, an enemy with
                // no table counts once it dropped it; the listed chance is weighted by kills (100 × 5% + 50 × 20%) / 150 = 10%
                var mb = LootBook.FromJson("""
                    {"tables": {
                      "Coradon": {"drops": [{"item": "Fresh Raw Meat", "chance": 0.05}]},
                      "Karnif": {"drops": [{"item": "Fresh Raw Meat", "chance": 0.2}]},
                      "Hard Kalgolem": {"drops": [{"item": "Lesser Abyssal Manastone", "chance": 0.1}]}
                    }}
                    """);
                var (pk, pf, pl) = mb.Pool(new[] { ("Coradon", 100, false), ("Karnif", 50, true), ("Hard Kalgolem", 80, false), ("Tog", 30, true), ("Ursus", 40, false) }, "Fresh Raw Meat");
                Check("odds: every enemy that can drop it, pooled", $"{pk} kills from {string.Join(",", pf)}, listed {pl:0.###}", "180 kills from Coradon,Karnif,Tog, listed 0.1");
            }
            var le = new SessionEngine(items); var l0 = DateTime.UtcNow;
            le.SetZone("Eastern Altamia Highland");
            le.SetTarget("Stonebeak", l0); le.SetTarget("Hard Kalgolem", l0.AddSeconds(1));   // two on the player; the bar shows the rock last
            le.Apply(new KinahChanged(l0.AddSeconds(2), 400, false));
            le.Apply(new ItemAcquired(l0.AddSeconds(2), items.IdOf("Raw Leather"), 1, false));
            le.Apply(new ItemAcquired(l0.AddSeconds(2), items.IdOf("Lesser Abyssal Manastone"), 1, false));
            le.Apply(new ItemAcquired(l0.AddSeconds(2), items.IdOf("Spiritstone"), 1, false));
            string Got(string mob) => string.Join(",", le.MobSnapshot().Where(m => m.Name == mob).SelectMany(m => m.Drops.Select(d => items.NameOf(d.Key))).OrderBy(x => x));
            Check("the leather goes to the Stonebeak, the manastone stays with the rock", $"Stonebeak [{Got("Stonebeak")}] · Kalgolem [{Got("Hard Kalgolem")}]", "Stonebeak [Raw Leather,Spiritstone] · Kalgolem [Lesser Abyssal Manastone]");
            Check("moved and unplaced drops are counted", $"{le.ReassignedDrops} moved, {le.UnplacedDrops} unplaced, {le.Items.Values.Sum()} drops", "2 moved, 0 unplaced, 3 drops");
            le.SetTarget("Hard Kalgolem", l0.AddSeconds(30));
            le.Apply(new ItemAcquired(l0.AddSeconds(30), items.IdOf("Raw Leather"), 1, false));   // only the rock is near: nobody can have dropped it
            Check("a drop no enemy near can drop is a drop, credited to nobody", $"{le.UnplacedDrops} unplaced, Kalgolem [{Got("Hard Kalgolem")}], {le.Items.Values.Sum()} drops", "1 unplaced, Kalgolem [Lesser Abyssal Manastone], 4 drops");
            LootBook.Current = keep;
            // the tables built into the exe (data/loot-tables.json, every enemy on mobs.txt tried, 2026-10-21)
            Check("loot tables built in", keep.Knows("Highland Karnif") ? $"Highland Karnif: Raw Leather {keep.CanDrop("Highland Karnif", "Raw Leather")}, listed {keep.Find("Highland Karnif", "Raw Leather")?.Chance:0.####}" : "no (data/loot-tables.json missing)", "Highland Karnif: Raw Leather True, listed 0.0389");
        }

        Console.WriteLine("\nThe loot list beside the character, checked against the chat:");
        {
            LootPopup.Row K(long a) => new($"Kina x{a}", "Kina", a, 1, true);
            LootPopup.Row I(string n, int c = 1) => new($"{n} x{c}", n, 0, c, false);
            var x0 = DateTime.UtcNow;
            string Desc(List<GameEvent> evs) => evs.Count == 0 ? "nothing" : string.Join(", ", evs.Select(e => Describe(e, items)));

            var c1 = new LootCrossCheck(items);
            c1.Chat(new KinahChanged(x0, 404, false), x0);
            var a1 = c1.Popup(new() { K(404) }, x0.AddSeconds(0.5)).Concat(c1.Popup(new() { K(404) }, x0.AddSeconds(1))).Concat(c1.Popup(new(), x0.AddSeconds(6))).ToList();
            Check("a row the chat read is matched, nothing added", $"{Desc(a1)} · {c1.Matched} matched", "nothing · 1 matched");

            var c2 = new LootCrossCheck(items);
            var a2 = c2.Popup(new() { I("Raw Leather") }, x0).Concat(c2.Popup(new() { I("Raw Leather") }, x0.AddSeconds(0.7))).Concat(c2.Popup(new(), x0.AddSeconds(3.5))).Concat(c2.Popup(new(), x0.AddSeconds(5))).ToList();
            Check("a row the chat missed is added once, after the wait", Desc(a2), "Raw Leather ×1");

            var c3 = new LootCrossCheck(items);
            c3.Chat(new ItemAcquired(x0, items.IdOf("Raw Leather"), 1, false), x0);
            var a3 = new List<GameEvent>();
            a3.AddRange(c3.Popup(new() { I("Raw Leather") }, x0.AddSeconds(0.3)));
            a3.AddRange(c3.Popup(new() { I("Raw Leather") }, x0.AddSeconds(1.0)));
            a3.AddRange(c3.Popup(new() { I("Raw Leather"), I("Raw Leather") }, x0.AddSeconds(1.7)));
            a3.AddRange(c3.Popup(new() { I("Raw Leather"), I("Raw Leather") }, x0.AddSeconds(2.4)));
            a3.AddRange(c3.Popup(new(), x0.AddSeconds(8)));
            Check("two identical drops, the chat read one: the other is added", Desc(a3), "Raw Leather ×1");

            var c4 = new LootCrossCheck(items);
            c4.Popup(new() { K(483) }, x0); c4.Popup(new() { K(483) }, x0.AddSeconds(0.7));
            var a4 = c4.Popup(new(), x0.AddSeconds(5));
            bool skip = c4.Chat(new KinahChanged(x0.AddSeconds(6), 483, false), x0.AddSeconds(6));
            Check("the chat line for an added row comes late: skipped, counted once", $"{Desc(a4)} · late line skipped {skip}", "Kinah +483 · late line skipped True");

            var c5 = new LootCrossCheck(items);
            var a5 = new List<GameEvent>();
            a5.AddRange(c5.Popup(new() { K(250), K(1), I("Oracle") }, x0));                              // a Kinah amount seen once, a 1-Kina misread, a name plate
            a5.AddRange(c5.Popup(new() { K(1), I("Oracle") }, x0.AddSeconds(0.7)));
            a5.AddRange(c5.Popup(new() { K(1), I("Oracle") }, x0.AddSeconds(6)));
            Check("Kinah seen once, Kina under 10 and names off the item list never count", Desc(a5), "nothing");

            // a shop window where the list shows (the Festival Shop's "Radiant Odyle (Bound) x1" beside "Sold Out x1",
            // 2026-10-22 14:48), and any row that stays up longer than a loot row does: never loot
            var c14 = new LootCrossCheck(items);
            var a14 = new List<GameEvent>();
            for (int i = 0; i < 6; i++) a14.AddRange(c14.Popup(new() { I("Raw Leather"), I("Sold Out") }, x0.AddSeconds(i)));
            a14.AddRange(c14.Popup(new(), x0.AddSeconds(9)));
            var c15 = new LootCrossCheck(items);
            var a15 = new List<GameEvent>();
            for (int i = 0; i <= 8; i++) a15.AddRange(c15.Popup(new() { I("Raw Leather") }, x0.AddSeconds(i)));
            a15.AddRange(c15.Popup(new(), x0.AddSeconds(12)));
            Check("a shop window, and a row that stays up, are never loot", $"{Desc(a14)} · {Desc(a15)}", "nothing · nothing");

            // the chat misread the list's 463 as 460 (2026-10-22 11:02): the same pickup, not a second one
            var c10 = new LootCrossCheck(items);
            c10.Chat(new KinahChanged(x0, 460, false), x0);
            var a10 = c10.Popup(new() { K(463) }, x0.AddSeconds(0.5)).Concat(c10.Popup(new() { K(463) }, x0.AddSeconds(1.2))).Concat(c10.Popup(new(), x0.AddSeconds(7))).ToList();
            Check("a Kinah amount one digit off is the same pickup", Desc(a10), "nothing");

            // the chat counted one Kinah line twice; the list showed it once (2026-10-22 11:01, 352 Kina)
            var c11 = new LootCrossCheck(items);
            bool first = c11.Chat(new KinahChanged(x0, 352, false), x0);
            c11.Popup(new() { K(352) }, x0.AddSeconds(0.9));
            bool second = c11.Chat(new KinahChanged(x0.AddSeconds(1.1), 352, false), x0.AddSeconds(1.1));
            var c12 = new LootCrossCheck(items);
            c12.Chat(new KinahChanged(x0, 352, false), x0);
            c12.Popup(new() { K(352), K(352) }, x0.AddSeconds(0.9));
            bool twoKills = c12.Chat(new KinahChanged(x0.AddSeconds(1.1), 352, false), x0.AddSeconds(1.1));
            Check("the same amount read twice: skipped when the list showed it once, kept when twice", $"{first} {second} {twoKills}", "False True False");

            // the Kinah counter against the count: a gap that holds is judged once; the chat catching up closes it
            var bc = new BalanceCheck(); var b0 = x0;
            long g1 = bc.Observe(10_000, 0, b0);                          // the start
            long g2 = bc.Observe(10_400, 0, b0.AddSeconds(1));            // the counter moved first...
            long g3 = bc.Observe(10_400, 400, b0.AddSeconds(2));          // ...and the chat caught up: agreed
            long g4 = bc.Observe(10_863, 400, b0.AddSeconds(3));          // 463 the count lacks
            long g5 = bc.Observe(10_863, 400, b0.AddSeconds(6));
            long g6 = bc.Observe(10_863, 400, b0.AddSeconds(10));         // held over six seconds: judged
            Check("the Kinah counter against the count", $"{g1} {g2} {g3} {g4} {g5} {g6} · agreed {bc.Agreed}", "0 0 0 0 0 463 · agreed 1");
            var c13 = new LootCrossCheck(items);
            c13.Popup(new() { K(463) }, x0); c13.Popup(new(), x0.AddSeconds(1)); c13.Popup(new(), x0.AddSeconds(5));
            Check("a Kinah row read once waits for the counter", $"{c13.TakeSingleKinah(463, x0.AddSeconds(10))} {c13.TakeSingleKinah(463, x0.AddSeconds(10))} {c13.TakeSingleKinah(500, x0.AddSeconds(10))}", "True False False");

            // the list misread a count (x14 for x1, 2026-10-22 14:26): with the chat's line, the same pickup; alone, read once, nothing
            var c16 = new LootCrossCheck(items);
            c16.Chat(new ItemAcquired(x0, items.IdOf("Orichalcum Ore"), 1, false), x0);
            var a16 = c16.Popup(new() { I("Orichalcum Ore", 14) }, x0.AddSeconds(1)).Concat(c16.Popup(new(), x0.AddSeconds(2))).Concat(c16.Popup(new(), x0.AddSeconds(7))).ToList();
            var c17 = new LootCrossCheck(items);
            var a17 = c17.Popup(new() { I("Orichalcum Ore", 14) }, x0).Concat(c17.Popup(new(), x0.AddSeconds(1))).Concat(c17.Popup(new(), x0.AddSeconds(7))).ToList();
            // a row that flickers out of a read or two and comes back is the same row (Ethereal Longsword, 2026-10-22 15:01)
            var c18 = new LootCrossCheck(items);
            c18.Popup(new() { I("Raw Leather") }, x0); c18.Popup(new(), x0.AddSeconds(0.7)); c18.Popup(new(), x0.AddSeconds(2.4));
            c18.Chat(new ItemAcquired(x0.AddSeconds(2.6), items.IdOf("Raw Leather"), 1, false), x0.AddSeconds(2.6));
            var a18 = c18.Popup(new() { I("Raw Leather") }, x0.AddSeconds(3)).Concat(c18.Popup(new(), x0.AddSeconds(4))).Concat(c18.Popup(new(), x0.AddSeconds(9))).ToList();
            Check("a misread count, and a row that flickers back", $"{Desc(a16)} · {Desc(a17)} · {Desc(a18)}", "nothing · nothing · nothing");

            // the loot list's Kinah row read without its amount is no item called "Kina" (0.8.2, 16:39)
            var c19 = new LootCrossCheck(items);
            var a19 = c19.Popup(new() { I("Kina") }, x0).Concat(c19.Popup(new(), x0.AddSeconds(1))).Concat(c19.Popup(new(), x0.AddSeconds(7))).ToList();
            Check("a Kina row without its amount is nothing", Desc(a19), "nothing");

            // multi-frame voting: three reads of one new line, one garbled; the reading most agree on is the one counted
            var vp = new ChatParser(items);
            var v1 = new ChatVoter(); var vt = x0; var vo = new List<string>();
            vo.AddRange(v1.Push(new[] { "Acquired 46&Kina." }, new[] { "Acquired 46&Kina." }, vt, vp.Meaning));
            vo.AddRange(v1.Push(Array.Empty<string>(), new[] { "Acquired 468 Kina." }, vt.AddSeconds(0.33), vp.Meaning));
            vo.AddRange(v1.Push(new[] { "You have acquired [Raw Leather]." }, new[] { "Acquired 468 Kina.", "You have acquired [Raw Leather]." }, vt.AddSeconds(0.66), vp.Meaning));
            vo.AddRange(v1.Flush(vp.Meaning));
            // a line that scrolled away after one read goes as read
            var v2 = new ChatVoter(); var vo2 = new List<string>();
            vo2.AddRange(v2.Push(new[] { "Acquired 352 Kina." }, new[] { "Acquired 352 Kina." }, vt, vp.Meaning));
            vo2.AddRange(v2.Push(new[] { "Acquired 18 Kina." }, new[] { "Acquired 18 Kina." }, vt.AddSeconds(0.33), vp.Meaning));
            // two Kinah lines that differ only in their digits are two pickups, never one line misread (16:53, 531 and 320)
            var v3 = new ChatVoter(); var vo3 = new List<string>();
            vo3.AddRange(v3.Push(new[] { "Acquired 320 Kina.", "Acquired 531 Kina." }, new[] { "Acquired 320 Kina.", "Acquired 531 Kina." }, vt, vp.Meaning));
            vo3.AddRange(v3.Push(Array.Empty<string>(), new[] { "Acquired 320 Kina.", "x" }, vt.AddSeconds(0.33), vp.Meaning));
            vo3.AddRange(v3.Flush(vp.Meaning));
            Check("votes: different amounts are different lines", string.Join(" | ", vo3), "Acquired 320 Kina. | Acquired 531 Kina.");
            // one digit apart is another pickup too (19:00: 486, 153, 386 all real; a read lost the last amount)
            var v4 = new ChatVoter(); var vo4 = new List<string>();
            vo4.AddRange(v4.Push(new[] { "Acquired 386 Kina." }, new[] { "Acquired 486 Kina.", "Acquired 153 Kina.", "Acquired 386 Kina." }, vt, vp.Meaning));
            vo4.AddRange(v4.Push(Array.Empty<string>(), new[] { "Acquired 486 Kina.", "Acquired 153 Kina.", "Kina." }, vt.AddSeconds(0.33), vp.Meaning));
            vo4.AddRange(v4.Push(Array.Empty<string>(), new[] { "Acquired 486 Kina." }, vt.AddSeconds(0.66), vp.Meaning));
            vo4.AddRange(v4.Flush(vp.Meaning));
            Check("votes: a digit apart is another pickup", string.Join(" | ", vo4), "Acquired 386 Kina.");
            Check("votes across reads", $"{vp.Meaning("Acquired 463 Kina.").Key} · {string.Join(" | ", vo)} · {string.Join(" | ", vo2)} · changed {v1.Changed}",
                "k:463 · Acquired 468 Kina. | You have acquired [Raw Leather]. · Acquired 352 Kina. · changed 1");

            // reward-shots cleans itself: in-run pictures after a day, reward screens after a week, never other files
            var rs = Path.Combine(Path.GetTempPath(), "kl-rs-" + Guid.NewGuid().ToString("N")[..8]); Directory.CreateDirectory(rs);
            void Pic(string n, double daysOld) { var f = Path.Combine(rs, n); File.WriteAllBytes(f, new byte[10]); File.SetLastWriteTime(f, DateTime.Now.AddDays(-daysOld)); }
            Pic("20261020-101010-Zone-in-run.jpg", 2); Pic("20261022-101010-Zone-in-run.jpg", 0.1);
            Pic("20261020-101010-Zone-Jump-Jump.jpg", 2); Pic("20261012-101010-Zone-Jump-Jump.jpg", 10); Pic("mine.jpg", 30);
            int rsGone = TrackerSession.PruneRewardShots(rs, DateTime.Now);
            var rsLeft = string.Join(" ", Directory.GetFiles(rs).Select(Path.GetFileName).OrderBy(n => n));
            Directory.Delete(rs, true);
            // a craft queue: the loot list shows each result long after the window's last sign (16:52, 16 s and 40 s);
            // within the 60 s hold a recipe's item is a craft, after it a drop, and a drop before the window opened stays one
            var ce = new SessionEngine(items); var cw = x0;
            ce.SetCraftWindow(cw);
            ce.Apply(new ItemAcquired(cw.AddSeconds(-30), items.IdOf("Orichalcum Longsword"), 1, false));
            ce.Apply(new ItemAcquired(cw.AddSeconds(16), items.IdOf("Orichalcum Longsword"), 1, false));
            ce.Apply(new ItemAcquired(cw.AddSeconds(40), items.IdOf("Orichalcum Longsword"), 1, false));
            ce.Apply(new ItemAcquired(cw.AddSeconds(75), items.IdOf("Orichalcum Longsword"), 1, false));
            // event and expedition gear is bound: only materials carry a run's value (Steven, 2026-10-22)
            // a world boss's loot goes to the site as its own kind; a run in no known area and of no boss kind doesn't
            var wbRuns = new[] {
                new InstanceRun { Zone = "Saursus", Kind = InstanceLog.WorldBoss, Start = x0, LastLoot = x0.AddSeconds(20), Kills = 1, Items = { [items.IdOf("Refining Stone")] = 2 } },
                new InstanceRun { Zone = "Nowhere Known", Kind = "Event", Start = x0, LastLoot = x0.AddSeconds(20) } };
            var wbSent = InstanceRunPayload.From(wbRuns, id => id.ToString()) ?? new();
            Check("a world boss's loot is sent as its own kind", string.Join(" | ", wbSent.Select(r => $"{r.zone} {r.kind} {r.kills} kill {r.items.Values.Sum()} items")), "Saursus World Boss 1 kill 2 items");
            Check("gear from the item list's groups", $"{items.IsGear(items.IdOf("Orichalcum Longsword"))} {items.IsGear(items.IdOf("Holy Spirit Helm"))} {items.IsGear(items.IdOf("Refining Stone"))} {items.IsGear(items.IdOf("Fine Odyle"))}", "True True False False");
            Check("crafting window: its tabs, alone or joined on one line", $"{TargetReader.IsCraftTab("Blacksmithing")} {TargetReader.IsCraftTab("Blacksmithing Armorsmithing Handicrafting")} {TargetReader.IsCraftTab("Cooking Pot")}", "True True False");
            {
                // the crafting panel's bar races, as read from Steven's two Life Serums on 2026-10-22: success, success, combo success
                var cpt = new CraftPanelTracker(); var cp0 = new DateTime(2026, 10, 22, 17, 0, 13);
                var races = new (double S, double F, bool C, bool Up)[] { (0, 0, false, true), (.32, .42, false, true), (.32, .70, false, true), (.99, .70, false, true), (1, .70, false, true),
                    (0, 0, false, false), (0, 0, false, true), (.71, .28, false, true), (1, .28, false, true), (1, .28, false, true),
                    (0, 0, true, true), (.32, .42, true, true), (1, .42, true, true), (1, .42, true, true), (0, 0, false, true), (.1, .98, false, true) };
                var outs = races.Select((r, i) => cpt.Feed(cp0.AddSeconds(i), new CraftPanel.Sample(r.Up, r.S, r.F, r.C))).Where(o => o != null).Select(o => (o!.Ok ? "ok" : "fail") + (o.Combo ? "+combo" : ""));
                Check("crafting panel: one outcome per race, the full bar decides", string.Join(" ", outs), "ok ok ok+combo fail");
                Check("crafting panel: the recipe's item", CraftPanel.ItemOf("Life Serum (Bound) (Novice Lv. 1)"), "Life Serum (Bound)");
                // the panel counts the tries; what the loot list shows meanwhile is their output, never a drop; chat craft lines are left out
                var pe = new SessionEngine(items); var pt = DateTime.UtcNow;
                var serum = items.IdOf("Life Serum (Bound)"); var other = items.IdOf("Orichalcum Ore");
                pe.CraftPanelSeen("Life Serum (Bound)", pt);
                pe.Apply(new CraftResult(pt, "Life Serum (Bound)", true) { Via = SessionEngine.CraftPanelVia });
                pe.Apply(new ItemAcquired(pt.AddSeconds(1), serum, 1, true) { Via = "loot list" });
                pe.Apply(new CraftResult(pt.AddSeconds(7), "Life Serum (Bound)", true) { Via = SessionEngine.CraftPanelVia });
                pe.Apply(new CraftResult(pt.AddSeconds(14), "Life Serum (Bound)", true, Combo: true) { Via = SessionEngine.CraftPanelVia });
                pe.Apply(new ItemAcquired(pt.AddSeconds(15), other, 2, false) { Via = "loot list" });
                pe.Apply(new CraftResult(pt.AddSeconds(16), "Crafting failed.", false) { Via = "chat" });
                var pr = pe.CraftSnapshot();
                Check("crafting panel: tries, combo and output", string.Join(" | ", pr.Select(r => $"{r.Name} {r.Ok}/{r.Attempts} up {r.Upgraded}/{r.Upgraded + r.NotUpgraded} made {string.Join(",", r.Made.Select(m => m.Key + " x" + m.Value))}")) + $" · drops {pe.Items.Count + pe.ItemsBound.Count}",
                    "Life Serum (Bound) 2/2 up 1/1 made Life Serum (Bound) x1,Orichalcum Ore x2 · drops 0");
                Check("crafting panel: gone a while, a pickup is a drop again", $"{pe.CraftPanelActive(pt.AddSeconds(31))}", "False");
            }
            {
                // a world boss with a Force (Steven, 2026-10-22): other players' shares are never the player's; the player's own share is
                var fp = new ChatParser(items); var ft = DateTime.UtcNow;
                string Got(string l) => string.Join(",", fp.Parse(l, ft).Select(e => e switch { ItemAcquired a => items.NameOf(a.ItemId), KinahChanged k => k.Delta + " Kina", _ => e.GetType().Name }));
                Check("someone else's share of a Force's loot", $"[{Got("Yarie acquired Soul; Ursus (Bound) through")}] [{Got("zMochi acquired Artwork: Furious Saursus")}] [{Got("Kubie acquired Soul: Ursus (Bound) through)")}] [{Got("HePeDawneroacquired Soul: Ursus4(BoÃ¼nd)")}]", "[] [] [] []");
                Check("the player's own lines still count", $"[{Got("Acquired 23 Kina.")}] [{Got("Spiritstone acquired.")}] [{Got("Acquired Spiritstone through Force distribution.")}]", "[23 Kina] [Spiritstone] [Spiritstone]");
                Check("world bosses by name, titled or learned from the chat", $"{TrackerSession.WorldBossOf("Furious Saursus")} {TrackerSession.WorldBossOf("Young Ursus") ?? "-"} {TrackerSession.WorldBossOf("Raging Grimtusk", new[] { "Grimtusk" })}", "Saursus - Grimtusk");
            }
            Check("crafting window: held 60 s after its last sign", $"crafts {ce.CraftedSkipped} · drops {ce.Items.Values.Sum()}", "crafts 2 · drops 2");
            Check("reward-shots cleans itself", $"{rsGone} · {rsLeft}", "2 · 20261020-101010-Zone-Jump-Jump.jpg 20261022-101010-Zone-in-run.jpg mine.jpg");

            // a joke ask on the site's board (one listing, 5,000x its day before) is no price
            var pb = PriceBook.ParseBoard("{\"items\":[{\"item\":\"Ethereal Longsword\",\"lowest_ask\":10000011,\"n_obs\":1,\"change_24h\":4999.0},{\"item\":\"Raw Leather\",\"lowest_ask\":900,\"n_obs\":1,\"change_24h\":0.1}]}", null);
            Check("a one-off price spike from the board is left out", string.Join(",", pb.Select(kv => kv.Key + "=" + kv.Value)), "Raw Leather=900");

            // fast kills: the list is read about once a second and a row is up about three, so a row can be read once only
            var c7 = new LootCrossCheck(items);
            var a7 = c7.Popup(new() { K(431), I("Spiritstone") }, x0).Concat(c7.Popup(new() { K(431) }, x0.AddSeconds(1.2))).Concat(c7.Popup(new(), x0.AddSeconds(6))).ToList();
            Check("a listed item read once counts; Kinah read twice does", Desc(a7), "Spiritstone ×1, Kinah +431");

            // the rows move as new ones come in: the same pickup at another place in the list is not new
            var c8 = new LootCrossCheck(items);
            var a8 = new List<GameEvent>();
            a8.AddRange(c8.Popup(new() { I("Raw Leather"), I("Spiritstone") }, x0));
            a8.AddRange(c8.Popup(new() { I("Spiritstone"), I("Raw Leather"), I("Fresh Fruit") }, x0.AddSeconds(1)));
            a8.AddRange(c8.Popup(new() { I("Fresh Fruit") }, x0.AddSeconds(2)));
            a8.AddRange(c8.Popup(new(), x0.AddSeconds(8)));
            Check("rows that change places are counted once each", Desc(a8), "Raw Leather ×1, Spiritstone ×1, Fresh Fruit ×1");

            // a row the reader missed for one read, then read again: still the same pickup
            var c9 = new LootCrossCheck(items);
            var a9 = new List<GameEvent>();
            a9.AddRange(c9.Popup(new() { I("Raw Leather") }, x0));
            a9.AddRange(c9.Popup(new(), x0.AddSeconds(1)));
            a9.AddRange(c9.Popup(new() { I("Raw Leather") }, x0.AddSeconds(2)));
            a9.AddRange(c9.Popup(new(), x0.AddSeconds(9)));
            Check("a row missed by one read is not counted twice", Desc(a9), "Raw Leather ×1");

            var c6 = new LootCrossCheck(items);
            c6.Chat(new RewardReceived(x0, "Raw Leather", 1), x0);                                     // a purchase or a reward the chat set aside
            c6.Popup(new() { I("Raw Leather") }, x0.AddSeconds(0.5));
            var a6 = c6.Popup(new() { I("Raw Leather") }, x0.AddSeconds(1)).Concat(c6.Popup(new(), x0.AddSeconds(7))).ToList();
            Check("a reward the chat set aside is not added back as a drop", Desc(a6), "nothing");

            var rows = LootPopup.Rows(new[] { (0, "*Kina x212"), (1, "kina x483?"), (2, "Raw Leather x1"), (3, "Soul: Papis (Bound) x1"), (4, "Fresh Raw Meat") });
            Check("rows read from the list", string.Join(" | ", rows.Select(r => r.IsKinah ? "Kinah " + r.Amount : r.Name + " x" + r.Count)), "Kinah 212 | Kinah 483 | Raw Leather x1 | Soul: Papis (Bound) x1 | Fresh Raw Meat x1");
        }

        Console.WriteLine("\nUpdates from the GitHub releases:");
        {
            string Rel(string tag, string asset = "KinahLedger.exe", long size = 72_500_000, string body = "KinahLedger.exe sha256: 3F9A0000000000000000000000000000000000000000000000000000000000AB", bool pre = false) =>
                "{\"tag_name\":\"" + tag + "\",\"draft\":false,\"prerelease\":" + (pre ? "true" : "false") + ",\"html_url\":\"https://github.com/StevenMacchia/kinah-ledger/releases/tag/" + tag + "\",\"body\":" + System.Text.Json.JsonSerializer.Serialize(body) +
                ",\"assets\":[{\"name\":\"" + asset + "\",\"size\":" + size + ",\"browser_download_url\":\"https://github.com/StevenMacchia/kinah-ledger/releases/download/" + tag + "/" + asset + "\"}]}";
            var api = new Uri(KinahLedger.Update.Updater.DefaultManifest);
            string U(string json) => KinahLedger.Update.Updater.Parse(json, api, out var why) is { } r ? $"{KinahLedger.Update.Updater.Show(r.Version)} {r.Size} {r.Sha256[..6]}" : "none: " + why;
            Check("a release with the exe and a sha256 line", U(Rel("v0.7.3")), "0.7.3 72500000 3f9a00");
            Check("a prerelease is never offered", U(Rel("v0.8.0", pre: true)), "none: a prerelease");
            Check("a release without KinahLedger.exe", U(Rel("v0.7.3", asset: "KinahLedger.zip")), "none: no KinahLedger.exe in the release");
            Check("a release without a sha256 line", U(Rel("v0.7.3", body: "Fixes.")), "none: no sha256 line in the release notes");
            Check("the site's manifest still reads", U("{\"version\":\"0.7.3\",\"url\":\"https://kinahledger.com/download/KinahLedger.exe\",\"sha256\":\"" + new string('a', 64) + "\",\"size\":72500000}"), "0.7.3 72500000 aaaaaa");
        }

        Console.WriteLine("\nBalance counter (0.7): which counter is Kinah, and when a read is trusted:");
        var bt = new BalanceTracker();
        List<CounterRead> Strip(long kinah, long other = 2751) => new() { new(7811, 40), new(kinah, 300), new(other, 560), new(15265, 820) };
        (List<CounterRead> read, long picked, bool same, string expect, string why)[] steps =
        {
            (Strip(23501), 0, false, "none", "first read: nothing to compare yet"),
            (Strip(24021), 520, false, "none", "grew by the 520 picked up: one match"),
            (Strip(24457, 2752), 956, false, "24457", "grew by 436 again: found, and this read counts"),
            (Strip(24857), 956, false, "24457", "a misread digit: not trusted"),
            (Strip(24457), 956, false, "24457", "back to the right value, once: not yet"),
            (Strip(24457), 956, false, "24457", "the same twice: trusted"),
            (Strip(20000), 956, false, "24457", "spent on something: one read isn't enough"),
            (Strip(20000), 956, true, "20000", "the same pixels again: trusted"),
        };
        foreach (var (read, picked, same, expect, why) in steps)
        {
            bt.Observe(read, picked, same);
            Check(why, bt.Balance?.ToString() ?? "none", expect);
        }
        Check("the counter found", bt.KinahLeft?.ToString() ?? "none", "300");
        var split = BalanceReader.Counters(new[] { new OcrLineBox("x 23, 501 @ 7,811", new System.Drawing.Rectangle(0, 0, 200, 20), new[]
        {
            new OcrWordBox("x", new System.Drawing.Rectangle(0, 0, 10, 20)), new OcrWordBox("23,", new System.Drawing.Rectangle(20, 0, 26, 20)),
            new OcrWordBox("501", new System.Drawing.Rectangle(50, 0, 30, 20)), new OcrWordBox("@", new System.Drawing.Rectangle(110, 0, 12, 20)),
            new OcrWordBox("7,811", new System.Drawing.Rectangle(130, 0, 50, 20))
        }) });
        Check("a number split by the recognizer is joined back", string.Join(" ", split.Select(c => $"{c.Value}@{c.Left}")), "23501@20 7811@130");
        return ok;
    }

    /// <summary>
    /// Feeds typical Market rows (as a chosen area reads them: one line per row, with recognition slips) and column cells
    /// (as the found table gives them) through the Market parser. With --image, finds the Market in a screenshot and reads it instead.
    /// </summary>
    static int MarketTest(Settings s, Dictionary<string, string> o)
    {
        var items = ItemDictionary.LoadOrEmpty(s.ItemsFile);
        var parser = new MarketParser(items);
        if (o.TryGetValue("image", out var image)) return MarketImage(s, parser, image, o);

        (string line, string expect)[] lines =
        {
            ("Spiritstone x1 7,499 7,499", "Spiritstone ×1 @ 7,499"),
            ("Spiritstone x10 7,520 75,200", "Spiritstone ×10 @ 7,520"),
            ("Spiritstone ×5 7,600 38,000", "Spiritstone ×5 @ 7,600"),
            ("Raw Leather x20 14,949 298,980", "Raw Leather ×20 @ 14,949"),
            ("Orichalcum Ore 1 1,950 1,950", "Orichalcum Ore ×1 @ 1,950"),
            ("Fine Odyle x2 18,9OO 37,800", "Fine Odyle ×2 @ 18,900"),
            ("Fresh Fruit x3 4,2OO 12,600", "Fresh Fruit ×3 @ 4,200"),
            ("Orichalcurn Ore x3 1,950 5,850", "Orichalcum Ore ×3 @ 1,950"),
            ("Ruby Gemstone x1 12,000 12,000 Kina", "Ruby Gemstone ×1 @ 12,000"),
            ("Expert's Refining Stone x1 71,777 71,777 Zandr 23h", "Expert's Refining Stone ×1 @ 71,777"),
            ("Thick hide x20 1,100 22,000 Sakukun", "Thick hide ×20 @ 1,100"),
            ("Radiant Odyle Lv. 10 x1 45,000 45,000", "Radiant Odyle ×1 @ 45,000"),
            ("Enchantment Stone 15 x1 9,900 9,900", "Enchantment Stone 15 ×1 @ 9,900"),
            ("Spiritstone 7,499 7,499", "Spiritstone ×1 @ 7,499"),
            ("Raw Leather 14,949 298,980", "Raw Leather ×20 @ 14,949"),
            ("Spiritstone 1 500", "Spiritstone ×1 @ 500"),
            ("Spiritstone x10 7,520 75,000", "(ignored)"),
            ("Spiritstone 7,499", "(ignored)"),
            ("Item Quantity Unit Price Total Price", "(ignored)"),
            ("Acquired 1,240 Kina.", "(ignored)"),
            ("[Vina] where did the filthy asmos go", "(ignored)"),
            ("Search x1 7,499 7,499", "(ignored)"),
            ("Market", "(ignored)")
        };
        bool ok = true;
        Console.WriteLine("Market parser, one line per row (a chosen area):");
        foreach (var (line, expect) in lines)
        {
            var got = parser.Parse(line)?.ToString() ?? "(ignored)";
            bool pass = got == expect; ok &= pass;
            Console.WriteLine($"  {line,-50} -> {got,-38} {(pass ? "ok" : "MISMATCH, expected " + expect)}");
        }

        Console.WriteLine("\nMarket parser, column cells (the found table):");
        (Dictionary<string, string> cells, string expect)[] cells =
        {
            (new() { ["name"] = "Spiritstone", ["qty"] = "x10", ["unit"] = "7,520", ["total"] = "75,200" }, "Spiritstone ×10 @ 7,520"),
            (new() { ["name"] = "Spiritstone", ["qty"] = "x10", ["unit"] = "7,520", ["total"] = "75,000" }, "(ignored)"),
            (new() { ["name"] = "Raw Leather", ["unit"] = "14,949", ["total"] = "298,980" }, "Raw Leather ×20 @ 14,949"),
            (new() { ["name"] = "Orichalcurn Ore", ["qty"] = "3", ["unit"] = "1,95O" }, "Orichalcum Ore ×3 @ 1,950"),
            (new() { ["name"] = "Spiritstone", ["unit"] = "7,499" }, "(ignored)"),
            (new() { ["name"] = "Unit Price", ["qty"] = "Quantity", ["unit"] = "Total" }, "(ignored)")
        };
        foreach (var (c, expect) in cells)
        {
            var got = parser.ParseCells(c)?.ToString() ?? "(ignored)";
            bool pass = got == expect; ok &= pass;
            Console.WriteLine($"  {string.Join(" | ", c.Select(kv => kv.Key + "=" + kv.Value)),-50} -> {got,-38} {(pass ? "ok" : "MISMATCH, expected " + expect)}");
        }

        Console.WriteLine("\nServer keys:");
        foreach (var (server, expect) in new[] { ("Siel (Elyos)", "na_e-siel-elyos"), ("  Israphel / Asmodian ", "na_e-israphel-asmodian"), ("", "na_e") })
        {
            var got = ServerKeys.Build("na_e", server);
            bool pass = got == expect; ok &= pass;
            Console.WriteLine($"  \"{server}\" -> {got,-28} {(pass ? "ok" : "MISMATCH, expected " + expect)}");
        }
        Console.WriteLine("\nServer roster:");
        var roster = Servers.All.ToList();
        var rosterKeys = roster.Select(x => x.Key).ToList();
        bool rosterOk = roster.Count == 86 && rosterKeys.Distinct().Count() == rosterKeys.Count
                     && rosterKeys.All(k => System.Text.RegularExpressions.Regex.IsMatch(k, @"^[a-z_]+-[a-z]+-(elyos|asmodian)$"))
                     && Servers.Find("na_e", "Siel (Elyos)")?.Key == "na_e-siel-elyos";
        ok &= rosterOk;
        Console.WriteLine($"  {roster.Count} servers in {Servers.Regions.Length} regions, keys {rosterKeys[0]} .. {rosterKeys[^1]}  {(rosterOk ? "ok" : "MISMATCH: expected 86 distinct well-formed keys")}");
        Console.WriteLine(ok ? "\nAll market cases passed." : "\nMarket mismatch, see above.");
        return ok ? 0 : 1;
    }

    /// <summary>Runs the Market locator and reader over a screenshot: what it finds, where, and what each row parses to.</summary>
    static int MarketImage(Settings s, MarketParser parser, string image, Dictionary<string, string> o)
    {
        using var bmp = Task.Run(() => ScreenReader.LoadAsync(image)).GetAwaiter().GetResult();
        var whole = new System.Drawing.Rectangle(0, 0, bmp.Width, bmp.Height);
        var game = RegionStore.Parse(o.GetValueOrDefault("game")) ?? whole;
        MarketLocator.Reader read = async (region, scale, boost) =>
        {
            var r = System.Drawing.Rectangle.Intersect(region, whole);
            if (r.Width < 2 || r.Height < 2) return new List<OcrLineBox>();
            using var crop = bmp.Clone(r, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            return await ScreenReader.ReadBoxesAsync(crop, r.Location, scale, boost);
        };
        Console.WriteLine($"{image}: {bmp.Width} × {bmp.Height}; game frame {game.Width} × {game.Height} at {game.X}, {game.Y}");
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var (layout, note) = Task.Run(() => MarketLocator.DetectAsync(game, read)).GetAwaiter().GetResult();
        Console.WriteLine($"Locator: {note} ({sw.ElapsedMilliseconds} ms)");
        if (layout == null && RegionStore.Parse(o.GetValueOrDefault("region")) is { } manual) { layout = MarketLayout.FromRegion(manual); Console.WriteLine($"Reading the given area as a chosen one: {layout}"); }
        if (layout == null) { Console.WriteLine("No table to read. Pass --region x,y,w,h to read an area the way a chosen one is read."); return 1; }
        var rows = Task.Run(() => MarketReader.ReadRowsAsync(layout, s.Ocr.Scale, read)).GetAwaiter().GetResult();
        int asks = 0;
        Console.WriteLine($"{rows.Count} row(s):");
        foreach (var row in rows)
        {
            var ask = parser.Parse(row);
            if (ask != null) asks++;
            Console.WriteLine($"  {row.Flat,-60} -> {(ask == null ? "(ignored)" : ask + (ask.Known ? "" : "  (not a known item name)"))}");
        }
        Console.WriteLine($"{asks} ask(s) parsed.");
        return asks > 0 ? 0 : 1;
    }

    static string Describe(GameEvent ev, ItemDictionary items) => ev switch
    {
        KinahChanged k => $"Kinah {(k.Delta >= 0 ? "+" : "")}{k.Delta:N0}{(k.Bound ? " (bound)" : "")}",
        ItemAcquired a => $"{items.NameOf(a.ItemId)} ×{a.Count}{(a.Bound ? " (bound)" : "")}",
        MobKilled => "kill",
        CurrencyGained c => $"{c.Name} +{c.Amount:N0}" + (c.Name.EndsWith("Soul Points", StringComparison.OrdinalIgnoreCase) ? " (counts as a kill, not a drop)" : ""),
        PetCrystal p => $"pet: {p.Name} ×{p.Count} (Pets table, by zone; not a drop)",
        RewardReceived r => $"reward: {r.Name} ×{r.Count} (skipped)",
        GatherFailed f => $"{f.Skill} failed (node attempt, no yield)",
        MarketSold s => $"sold: {s.Item} ×{s.Qty} for {s.Total:N0}",
        MarketListed l => $"listed: {l.Item} ×{l.Qty}" + (l.Price > 0 ? $" for {l.Price:N0}" : ""),
        MarketExpired x => $"expired: {x.Item} ×{x.Qty}",
        CraftResult c => $"craft {(c.Ok ? "ok" : "failed")}: {(c.Item.Length > 0 ? c.Item : "?")}",
        EnchantResult en => $"enchant {(en.Ok ? "ok" : "failed")}: {(en.Item.Length > 0 ? en.Item : "?")} +{(en.From >= 0 ? en.From.ToString() : "?")} → +{(en.To >= 0 ? en.To.ToString() : "?")}",
        _ => ev.GetType().Name
    };

    static int Watch(Settings s, Dictionary<string, string> o)
    {
        var items = ItemDictionary.LoadOrEmpty(s.ItemsFile);
        var activity = o.GetValueOrDefault("activity") ?? s.Ocr.Activity;
        int? interval = int.TryParse(o.GetValueOrDefault("interval"), out var iv) ? iv : null;
        var manual = RegionStore.Parse(o.GetValueOrDefault("region")) ?? (o.ContainsKey("auto") ? null : RegionStore.Load());
        var session = new TrackerSession(manual, s, items, activity, interval);
        var prefs = UserPrefs.Load();
        session.RegionKey = prefs.Region; session.Server = o.GetValueOrDefault("server") ?? prefs.Server;
        var watcher = MarketWatcher(s, items, o);
        var engine = session.Engine;
        session.StartAsync().GetAwaiter().GetResult();
        watcher.Start();
        Console.WriteLine($"Watching {session.Region} every {interval ?? s.Ocr.IntervalMs} ms. Activity: {activity}. Zone: {session.Zone ?? "not read"}. Items known: {items.Count}. (The on-screen panel is only in the window app.)");
        if (watcher.Market.Enabled) Console.WriteLine(MarketIntro(watcher));
        if (s.Upload.Enabled && !string.IsNullOrWhiteSpace(s.Upload.Endpoint))
            Console.WriteLine($"When you stop, this run's totals go anonymously to {s.Upload.Endpoint}: server {ServerKeys.Build(session.RegionKey.Length > 0 ? session.RegionKey : s.Region, session.Server)}, activity, gear bracket, minutes, kills, kinah, item counts and a random install ID.");
        Console.WriteLine("Ctrl+C to stop.");

        var stop = false;
        Console.CancelKeyPress += (_, e) => { e.Cancel = true; stop = true; };
        var view = new ConsoleView(engine, () => session.Activity, () => $"reads {session.Reads}  skipped {session.Unaligned}  empty {session.Empty}" + (session.LastError.Length > 0 ? "  " + session.LastError : "") + (watcher.Market.Enabled ? $"  market: {watcher.Market.AsksSeen} asks, {watcher.Market.Shared} shared" : ""));
        while (!stop) { view.Render(); Thread.Sleep(1000); }
        session.StopAsync().GetAwaiter().GetResult();
        watcher.StopAsync().GetAwaiter().GetResult();
        view.Render();
        if (watcher.Market.Enabled) Console.WriteLine("Market: " + watcher.Market.Status);

        if (!session.Eligible) { Console.WriteLine("Run too short or empty; nothing uploaded."); return 0; }
        var sent = session.UploadAsync(s).GetAwaiter().GetResult();
        Console.WriteLine((sent ? "Run shared anonymously." : "Run kept on this PC.") + $" Activity: {session.Activity}.");
        return 0;
    }

    /// <summary>The always-on Market reader as the window runs it: the saved switch and area, the server from --server or the saved one.</summary>
    static GameWatcher MarketWatcher(Settings s, ItemDictionary items, Dictionary<string, string> o)
    {
        var prefs = UserPrefs.Load();
        var w = new GameWatcher(s, items, prefs.ManualMarket ? RegionStore.Load(RegionStore.MarketFile) : null, null, o.GetValueOrDefault("server") ?? prefs.Server);
        if (prefs.Region.Length > 0) w.Market.Region = prefs.Region;
        w.Market.Enabled = s.Market.Enabled && prefs.Share && !o.ContainsKey("no-market");
        return w;
    }

    static string MarketIntro(GameWatcher w) =>
        $"Market prices: read whenever the Market is open, server \"{w.Market.Server}\" ({(w.Market.Server.Length == 0 ? "none: asks are read but not sent; pass --server" : w.Market.ServerKey)}). --no-market turns it off.";

    /// <summary>Only the Market reader, no farming run: what the window does in the background, for testing it on its own.</summary>
    static int MarketWatch(Settings s, Dictionary<string, string> o)
    {
        var items = ItemDictionary.LoadOrEmpty(s.ItemsFile);
        var watcher = MarketWatcher(s, items, o);
        if (!watcher.Market.Enabled) { Console.WriteLine("Market reading is off (settings, the Share switch, or --no-market)."); return 1; }
        watcher.Start();
        Console.WriteLine(MarketIntro(watcher));
        Console.WriteLine("Open the Market in the game. Ctrl+C to stop.");
        var stop = false;
        Console.CancelKeyPress += (_, e) => { e.Cancel = true; stop = true; };
        string last = "";
        while (!stop)
        {
            var line = (watcher.GameRect == null ? "game not found · " : "") + watcher.Market.Status + (watcher.Market.LastAsk.Length > 0 ? " · last: " + watcher.Market.LastAsk : "") + (watcher.Market.Note.Length > 0 && !watcher.Market.Detected ? " · " + watcher.Market.Note : "");
            if (line != last) { Console.WriteLine($"{DateTime.Now:HH:mm:ss}  {line}"); last = line; }
            Thread.Sleep(1000);
        }
        watcher.StopAsync().GetAwaiter().GetResult();
        Console.WriteLine("Market: " + watcher.Market.Status);
        return 0;
    }

    static string Fk(double v) => Math.Abs(v) >= 1e6 ? $"{v / 1e6:0.00}M" : Math.Abs(v) >= 1e3 ? $"{v / 1e3:0}K" : $"{v:0}";
}
