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
        var settings = Settings.Load();
        if (args.Length == 0 || args[0] is Platform.AutoStart.BackgroundArg or Ui.MainWindow.AfterUpdateArg)
        {
            // double-clicked, started with Windows (in the tray), or restarted by an update: open the window
            return WpfApp.Run(settings, background: args.Contains(Platform.AutoStart.BackgroundArg), afterUpdate: args.Contains(Ui.MainWindow.AfterUpdateArg));
        }
        // command line: write to the terminal that started us; if there's none and no pipe either, open one
        if (!AttachConsole(-1) && GetStdHandle(-11) == IntPtr.Zero) AllocConsole();
        var cmd = args[0].ToLowerInvariant();
        var (opts, pos) = ParseArgs(args.Skip(1));
        if (opts.TryGetValue("process", out var hints))
            settings.ProcessHints = hints.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        try
        {
            switch (cmd)
            {
                case "pick": return Pick();
                case "locate": return Locate(settings);
                case "in-view": return InView(settings);
                case "ocr-file": return OcrFile(pos, opts);
                case "ui-shot": return UiShot(settings, opts, pos);
                case "ui-cycle": return WpfApp.Cycle(settings, opts.ContainsKey("auto") ? null : RegionFor(opts), int.TryParse(opts.GetValueOrDefault("ms"), out var ms) ? ms : 4000);
                case "ui-live": return WpfApp.Live(settings, opts.ContainsKey("auto") ? null : RegionFor(opts), int.TryParse(opts.GetValueOrDefault("ms"), out var lms) ? lms : 4000);
                case "parse-test": return ParseTest(settings);
                case "balance-test": return BalanceTest(settings, opts);
                case "options-test": return WpfApp.OptionsSelfTest(settings);
                case "market-test": return MarketTest(settings, opts);
                case "market-shot": opts["demo"] = "true"; opts["market"] = "true"; return UiShot(settings, opts, pos);
                case "market-watch": return MarketWatch(settings, opts);
                case "ocr-test": return OcrTest(settings, opts);
                case "target-test": return TargetTest(opts);
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
  ui-shot [dir] [--demo] [--gathering|--market|--crafting] [--compact] [--welcome] [--skin night|abyss|daylight]
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
        var reader = new ScreenReader(RegionFor(o), s.Ocr.Scale);
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
        return 0;
    }

    /// <summary>Reads a saved image of the target bar (--image file.png) the way the tracker reads the band, and prints what it finds.</summary>
    static int TargetTest(Dictionary<string, string> o)
    {
        if (!o.TryGetValue("image", out var image)) { Console.WriteLine("target-test --image <png of the target bar>"); return 1; }
        using var bmp = Task.Run(() => ScreenReader.LoadAsync(image)).GetAwaiter().GetResult();
        var lines = new List<OcrLineBox>();
        foreach (var (label, img, scale) in new (string, System.Drawing.Bitmap, int)[] { ("plain", bmp, 3), ("boost", ScreenReader.Boost(bmp), 3), ("name colour", ScreenReader.NameMask(bmp), 3) })
        {
            var got = Task.Run(() => ScreenReader.ReadBoxesAsync(img, new System.Drawing.Point(0, 0), scale)).GetAwaiter().GetResult();
            foreach (var l in got.OrderBy(l => l.Box.Top)) Console.WriteLine($"  {label}: {l.Text}");
            lines.AddRange(got);
        }
        var name = TargetReader.FindName(lines.Select(l => (l.Box.Top, l.Text)), out var unlisted);
        Console.WriteLine(name != null ? $"Enemy: {name}" : $"No enemy on the list" + (unlisted.Length > 0 ? $" (name-shaped text: {unlisted})" : ""));
        return name != null ? 0 : 1;
    }

    /// <summary>Renders the window (and the panel) off-screen to PNG files, so the layout can be reviewed without a desktop session.</summary>
    static int UiShot(Settings s, Dictionary<string, string> o, List<string> pos)
    {
        var dir = pos.Count > 0 ? pos[0] : ".";
        if (o.ContainsKey("options")) { WpfApp.ShotOptions(s, dir, o.GetValueOrDefault("skin")); Console.WriteLine($"wrote {Path.GetFullPath(dir)}"); return 0; }
        WpfApp.Shot(s, dir, o.ContainsKey("demo"), o.ContainsKey("market") ? "Market" : o.ContainsKey("crafting") ? "Crafting" : o.ContainsKey("pets") ? "Pets" : o.ContainsKey("gathering") ? "Gathering" : "Open world", o.ContainsKey("compact"), o.ContainsKey("welcome"), o.GetValueOrDefault("skin"));
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
            Check("instances: an open-world zone has none", ZoneLocator.KindOf("Eastern Cantas Valley") ?? "none", "none");
            Check("instances: they are on the zone list", ZoneLocator.Known.Contains("Urugugu Canyon") ? "yes" : "no", "yes");
            {
                var r = new InstanceRun { Zone = "Urugugu Canyon", Kind = "Expedition", Start = b0 };
                r.LastLoot = b0.AddMinutes(7);
                Check("an expedition run lasts from entry to the last loot", TimeSpan.FromSeconds(r.Seconds).ToString(@"m\:ss"), "7:00");
                var none = new InstanceRun { Zone = "Shugo Festival", Kind = "Event", Start = b0 };
                Check("a run with no loot has no length", none.Seconds.ToString("0"), "0");
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
        CurrencyGained c => $"{c.Name} +{c.Amount:N0} (counts as a kill, not a drop)",
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
