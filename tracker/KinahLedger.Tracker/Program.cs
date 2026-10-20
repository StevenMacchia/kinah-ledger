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
        if (args.Length == 0)
        {
            // double-clicked: open the window
            return WpfApp.Run(settings);
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
                case "ocr-file": return OcrFile(pos, opts);
                case "ui-shot": return UiShot(settings, opts, pos);
                case "ui-cycle": return WpfApp.Cycle(settings, opts.ContainsKey("auto") ? null : RegionFor(opts), int.TryParse(opts.GetValueOrDefault("ms"), out var ms) ? ms : 4000);
                case "ui-live": return WpfApp.Live(settings, opts.ContainsKey("auto") ? null : RegionFor(opts), int.TryParse(opts.GetValueOrDefault("ms"), out var lms) ? lms : 4000);
                case "parse-test": return ParseTest(settings);
                case "market-test": return MarketTest(settings, opts);
                case "market-shot": opts["demo"] = "true"; opts["market"] = "true"; return UiShot(settings, opts, pos);
                case "market-watch": return MarketWatch(settings, opts);
                case "ocr-test": return OcrTest(settings, opts);
                case "watch": return Watch(settings, opts);
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
  pick                          drag a box around the chat's loot lines; remembered for next time
  ocr-test [--save]             read the box once and show what was recognized (--save keeps a PNG)
  ocr-file <image> [--scale 2]  run recognition on a screenshot, plain and boosted
  watch [--activity name]       a farming run in the console, with the Market reader beside it; Ctrl+C to stop
        [--region x,y,w,h] [--interval 700] [--server ""Siel (Elyos)""] [--no-market]
  market-watch [--server ..]    only the Market reader, as the window runs it in the background; Ctrl+C to stop
  parse-test                    self-test of the chat-line parser
  market-test                   self-test of the Market row parser
  market-test --image shot.png  find the Market in a screenshot and read its rows
        [--game x,y,w,h] [--region x,y,w,h]
  ui-shot [dir] [--demo] [--pets|--gathering|--market] [--compact]
                                render the window to PNG files for layout review
  market-shot [dir]             the window with the Market card in its tracking state
  reset-id                      replace the random install ID used for uploads

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

    /// <summary>Renders the window (and the panel) off-screen to PNG files, so the layout can be reviewed without a desktop session.</summary>
    static int UiShot(Settings s, Dictionary<string, string> o, List<string> pos)
    {
        var dir = pos.Count > 0 ? pos[0] : ".";
        WpfApp.Shot(s, dir, o.ContainsKey("demo"), o.ContainsKey("market") ? "Market" : o.ContainsKey("pets") ? "Pets" : o.ContainsKey("gathering") ? "Gathering" : "Open world", o.ContainsKey("compact"));
        Console.WriteLine($"wrote {Path.GetFullPath(dir)}");
        return 0;
    }

    /// <summary>Feeds typical chat lines (including recognition slips) through the parser and the new-line tracker.</summary>
    static int ParseTest(Settings s)
    {
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
            "Obtained 1 Natura Soul Crystals.",
            "Obtained 2 Fera Soul Crystals.",
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
            new List<string> { "a", "b", "c", "d" }  // it came back with one more line, and only that one counts
        };
        string[] expect = { "", "d", "", "d", "", "w", "", "", "d" };
        bool ok = true;
        for (int i = 0; i < frames.Length; i++)
        {
            var got = string.Join(",", t.NewLines(frames[i]));
            ok &= got == expect[i];
            Console.WriteLine($"  read {i + 1} [{string.Join(",", frames[i])}] -> new: [{got}]  expected [{expect[i]}] {(got == expect[i] ? "ok" : "MISMATCH")}");
        }
        Console.WriteLine(ok ? "\nAll tracker cases passed." : "\nTracker mismatch, see above.");
        return ok ? 0 : 1;
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
        var w = new GameWatcher(s, items, prefs.ManualMarket ? RegionStore.Load(RegionStore.MarketFile) : null, o.GetValueOrDefault("server") ?? prefs.Server);
        if (prefs.Region.Length > 0) w.Market.Region = prefs.Region;
        w.Market.Enabled = s.Market.Enabled && prefs.ShareMarket && !o.ContainsKey("no-market");
        return w;
    }

    static string MarketIntro(GameWatcher w) =>
        $"Market prices: read whenever the Market is open, server \"{w.Market.Server}\" ({(w.Market.Server.Length == 0 ? "none: asks are read but not sent; pass --server" : w.Market.ServerKey)}). --no-market turns it off.";

    /// <summary>Only the Market reader, no farming run: what the window does in the background, for testing it on its own.</summary>
    static int MarketWatch(Settings s, Dictionary<string, string> o)
    {
        var items = ItemDictionary.LoadOrEmpty(s.ItemsFile);
        var watcher = MarketWatcher(s, items, o);
        if (!watcher.Market.Enabled) { Console.WriteLine("Market reading is off (settings, the window's switch, or --no-market)."); return 1; }
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
