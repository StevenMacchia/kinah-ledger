using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using KinahLedger.Data;
using KinahLedger.Ocr;
using KinahLedger.Session;
using Path = System.Windows.Shapes.Path;

namespace KinahLedger.Ui;

/// <summary>
/// Direction C, "Live" (Steven, 2026-10-24, mock-next-C.html, with mock-next-B.html's icon tiles and chips): every tab opens
/// on the last five things counted for it, newest first, beside the rate and a small line of the rate since the start, and
/// "Reading the chat" with a green tick (the read count on hover). A line that looks wrong is clicked and filed as a
/// miscount report with that line marked (line.json), a labelled case for the replay bank. Under the live card the sections
/// fold: Session, Drops from kills (open), Also this session (chips that open a small table in place), Where to farm now.
/// </summary>
public partial class MainWindow
{
    /* ---------------- sections you open ---------------- */

    /// <summary>Sections the player opened or folded, by name; the rest follow the view (Session and Where to farm open in Full view only).</summary>
    readonly Dictionary<string, bool> _secUser = new();

    bool SecOpen(string key) => _secUser.TryGetValue(key, out var v) ? v : key is "Session" or "Where" ? FullShown : key != "Best";

    void ToggleSec(string key)
    {
        _secUser[key] = !SecOpen(key);
        _secMoved = key;
        if (key is "CraftSession" or "Best") { if (!_demo) FillCraft(CraftShown); else ApplySections(); return; }
        Redraw();
    }

    /// <summary>A section's chevron and body: pointing right and folded, or down and gold with the body showing.</summary>
    /// The section the player just opened or folded: it moves (Motion.Open/Close, its chevron turning), the rest are set still.
    void SetSec(Path chev, FrameworkElement body, bool open, bool moved = false)
    {
        chev.SetResourceReference(Shape.StrokeProperty, open ? "Gold" : "Faint");
        bool shown = body.Visibility == Visibility.Visible;
        // a move under way is left to finish (a tick redraws the sections every half second)
        if (!moved && (Motion.Playing(body) || Motion.Playing(chev))) return;
        if (!moved || shown == open || !Motion.On) { chev.RenderTransform = new RotateTransform(open ? 90 : 0); body.Visibility = open ? Visibility.Visible : Visibility.Collapsed; return; }
        Motion.Turn(chev, open ? 0 : 90, open ? 90 : 0);
        if (open) { body.Visibility = Visibility.Visible; Motion.Open(body); }
        else Motion.Close(body, () => body.Visibility = SecShownFor(body) ? Visibility.Visible : Visibility.Collapsed);
    }
    // the section a click just opened or folded, for its one move (ApplySections); null once drawn
    string? _secMoved;
    // the hour's count under the hero figure at its last draw: the figure rolls only when it moved
    long _heroTotal = -1;
    // whether a section's body should show now (a fold that ends after the player opened it again shows it)
    bool SecShownFor(FrameworkElement body) =>
        body == DropsOpenBody ? SecOpen("Drops") : body == ExtrasBody ? SecOpen("Extras") : body == WhereBody ? SecOpen("Where") && WhereBody.Children.Count > 0
        : body == CraftBody ? SecOpen("CraftSession") : body == BestCraftsHost ? SecOpen("Best") : false;

    void ApplySections()
    {
        var moved = _secMoved; _secMoved = null;
        SetSec(DropsChev, DropsOpenBody, SecOpen("Drops"), moved == "Drops");
        SetSec(ExtrasChev, ExtrasBody, SecOpen("Extras"), moved == "Extras");
        SetSec(WhereChev, WhereBody, SecOpen("Where") && WhereBody.Children.Count > 0, moved == "Where");
        SetSec(CraftSessionChev, CraftBody, SecOpen("CraftSession"), moved == "CraftSession");
        SetSec(BestChev, BestCraftsHost, SecOpen("Best"), moved == "Best");
    }

    /// <summary>What options-test knows as the recent pickups strip: since 2026-10-28, LOOT's slots, under the live card in both views.</summary>
    internal FrameworkElement PickupStrip => SessionBody;
    /// <summary>What options-test knows as the top drops: since the feed, Where to farm now's zones, open in Full view.</summary>
    internal FrameworkElement TopDropsBox => WhereBody;

    /// <summary>The section heads, the feed's hints and the in-place table's "show all", wired once from the constructor.</summary>
    void InitLive()
    {
        foreach (var (head, key) in new (FrameworkElement, string)[] { (DropsHead, "Drops"), (ExtrasHead, "Extras"), (WhereHead, "Where"), (CraftSessionHead, "CraftSession"), (BestHead, "Best") })
            head.MouseLeftButtonUp += (_, ev) => { if (ev.OriginalSource is TextBlock { Name: "PctHeader" }) return; ToggleSec(key); };
        FeedHint.MouseLeftButtonUp += (_, _) => ShowLineReport(null, FeedTab.Farming, FeedHint);
        CraftHint.MouseLeftButtonUp += (_, _) => ShowLineReport(null, FeedTab.Crafting, CraftHint);
        foreach (var h in new[] { FeedHint, CraftHint })
        {
            var hint = h;
            hint.MouseEnter += (_, _) => hint.SetResourceReference(TextBlock.ForegroundProperty, "Gold");
            hint.MouseLeave += (_, _) => hint.SetResourceReference(TextBlock.ForegroundProperty, "Faint");
        }
        InPlaceMoreLink.Click += (_, _) => { var k = "x:" + (_extraOpen ?? ""); if (!_allRows.Remove(k)) _allRows.Add(k); Redraw(); };
        InitQuest();   // the hero's chart, the chips and Next up (Ui/MainWindow.QuestFarm.cs)
    }

    /* ---------------- the feed ---------------- */

    /// <summary>Sample lines for layout renders (never the player's), by tab.</summary>
    List<FeedLine> _demoFeed = new();

    List<FeedLine> FeedFor(FeedTab tab) => _demo
        ? _demoFeed.Where(l => l.Tab == tab).OrderByDescending(l => l.At).Take(LiveFeed.Shown).ToList()
        : _watcher.Chat?.Live.Recent(tab) ?? new();

    /// <summary>The newest Crafting lines of one recipe (its card's Last tries), from the whole feed kept, not just the five shown.</summary>
    List<FeedLine> CraftTries(string recipe, int n) =>
        (_demo ? _demoFeed.Where(l => l.Tab == FeedTab.Crafting).OrderByDescending(l => l.At).ToList() : _watcher.Chat?.Live.Recent(FeedTab.Crafting, LiveFeed.Keep) ?? new())
            .Where(l => l.Kind == "craft" && string.Equals(l.Name, recipe, StringComparison.OrdinalIgnoreCase)).Take(n).ToList();

    /// <summary>What each feed host drew last, so a tick that changes nothing redraws nothing (and the newest line fades in only when it is new).</summary>
    readonly Dictionary<Panel, (string Sig, DateTime Newest)> _feedDrawn = new();

    /// <summary>
    /// Draws up to five lines into <paramref name="host"/>, newest first: "just now" on the newest while it is under a minute
    /// old, then the time; the newest tinted gold with a rule on its left, the fifth dimmed. A tap on a line opens the report.
    /// </summary>
    void DrawFeed(Panel host, FrameworkElement empty, List<FeedLine> lines, FeedTab tab, Func<FeedLine, (string Text, bool Muted)> value, Func<FeedLine, FrameworkElement>? icon = null)
    {
        var now = DateTime.UtcNow;
        bool justNow = lines.Count > 0 && (now - lines[0].At).TotalSeconds < 60;
        // Explain each count in the signature: flipping it in Options redraws the rows with their other hover
        var sig = string.Join("|", lines.Select(l => $"{l.At.Ticks}:{l.Text}:{value(l).Text}{(IsMarked(l) ? ":marked" : "")}")) + (justNow ? "*" : "") + (_prefs.ExplainCounts ? "?" : "");
        if (_feedDrawn.TryGetValue(host, out var was) && was.Sig == sig) return;
        bool fresh = lines.Count > 0 && (!_feedDrawn.ContainsKey(host) || was.Newest != lines[0].At) && _feedDrawn.ContainsKey(host);
        _feedDrawn[host] = (sig, lines.Count > 0 ? lines[0].At : default);
        host.Children.Clear();
        empty.Visibility = lines.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        for (int i = 0; i < lines.Count; i++)
        {
            var l = lines[i];
            var row = FeedRow(l, i == 0, i == 0 && justNow, i == LiveFeed.Shown - 1, i > 0, value(l), icon?.Invoke(l) ?? FeedIcon(l));
            // a Just crafted line opens its recipe in the craft window, with the line's report a link in it (2026-11-10, Steven:
            // anything on the Crafting page opens how to craft it); a pickup's line opens its report, as before
            if (tab == FeedTab.Crafting && l.Name.Length > 0) { row.MouseLeftButtonUp += (_, _) => OpenCraftWindow(l.Name, report: () => ShowLineReport(l, tab, row)); row.ToolTip = "Click for its recipe, prices and market. A line counted wrong is reported from there."; }
            else row.MouseLeftButtonUp += (_, _) => ShowLineReport(l, tab, row);
            if (IsMarked(l)) MarkRow(row, l);   // marked with the That was wrong key: a gold edge and a flag (Ui/MainWindow.MarkWrong.cs)
            host.Children.Add(row);
            if (i == 0 && fresh)
                row.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(260)));
        }
    }

    /// <summary>One feed line (Quest): the item's own icon at 30 px, what in bold over when and from whom, and its worth in mint on the right.</summary>
    Border FeedRow(FeedLine l, bool newest, bool justNow, bool oldest, bool rule, (string Text, bool Muted) value, FrameworkElement icon)
    {
        var (bold, rest, small) = FeedWords(l);
        var local = l.At.ToLocalTime();
        var when = justNow ? "just now" : local.Date == DateTime.Today ? local.ToString("HH:mm:ss") : local.ToString("MMM d HH:mm");
        var row = QRow(icon, bold + rest, small.Length > 0 ? when + " · " + small : when, value.Text, value.Text.StartsWith("−") || value.Text.StartsWith("≤ −") ? "Coral" : value.Muted ? "Faint" : "Mint", rule: rule, inline: true);
        // the why is behind Options › Game reading › Explain each count (Steven, 2026-11-04), so the hover offers it only then
        var click = _prefs.ExplainCounts ? "Click to see why, or to report it." : "Click if it's wrong.";
        row.ToolTip = ViaTip(l.Via) is { Length: > 0 } vt ? vt + " " + click : click;
        ToolTipService.SetInitialShowDelay(row, 400);
        // the big card on hover (Steven, 2026-11-07: the item cards stay), the report on a click, as LOOT's slots do
        if (_lootHere is { } lh && _part == "Farming" && tabOf(l) == FeedTab.Farming)
        {
            if (l.Kind == "kinah" && lh.Kinah > 0)
                CardTip(row, () => KinahTodayCard(lh.Kinah, lh.ByEnemy, lh.Kills > 0 ? $"{lh.Kills:N0} kills · {Fk(lh.Kinah / (double)lh.Kills)} a kill" : "", "Picked up " + CountWhere(), "BY ENEMY"));
            else if (l.Kind == "item")
            {
                var r = lh.S.Drops.FirstOrDefault(x => !x.Header && string.Equals(x.Name, l.Name, StringComparison.OrdinalIgnoreCase));
                bool gathered = r == null;
                r ??= lh.S.Gathered.FirstOrDefault(x => !x.Header && string.Equals(x.Name, l.Name, StringComparison.OrdinalIgnoreCase));
                if (r != null) CardTip(row, () => ItemHereCard(r, gathered, CountWhere()));
            }
        }
        if (oldest) row.Opacity = 0.7;
        return row;

        static FeedTab tabOf(FeedLine x) => x.Tab;
    }

    /// <summary>A line's words: the bold part, the plain rest, and a faint word after ("Cantas Raider", "gathered", "worked").</summary>
    (string Bold, string Plain, string Small) FeedWords(FeedLine l)
    {
        switch (l.Kind)
        {
            case "kinah":
            {
                var enemy = l.Enemy.Length > 0 ? l.Enemy : _watcher.Chat?.Engine.CreditedTo(l.At, l.Amount) ?? "";
                return ($"{l.Amount:N0} Kinah", "", l.Tab == FeedTab.Events ? "in the run" : enemy);
            }
            case "craft" when l.Combo: return (l.Name.Length > 0 ? l.Name : "A craft", "", l.Ok ? ComboWon(l.Name) : "combo lost");
            case "craft": return (l.Name.Length > 0 ? l.Name : "A craft", CraftMakes(l) is int m && m > 1 && l.Ok ? $" ×{m}" : "", l.Ok ? "worked" : "failed");
            default:
                var small = l.Bound ? "bound" : GatheringCatalog.Find(l.Name) != null ? "gathered" : l.Tab == FeedTab.Events && l.Via.Contains("reward table") ? "reward screen" : "";
                return (l.Name, $" ×{l.Amount:N0}", small);
        }
    }

    /// <summary>How many a craft line's success made: the yield the panel's level told, else the recipe's when the book has one count for the item.</summary>
    static int? CraftMakes(FeedLine l) => l.Makes > 0 ? l.Makes : l.Name.Length >= 2 ? RecipeBook.YieldFor(l.Name, 0) : null;

    /// <summary>A line's tile: the item's own icon (the tracker's icon cache) in a round box, a coin for Kinah.</summary>
    FrameworkElement FeedIcon(FeedLine l)
    {
        if (l.Kind == "kinah") return QCoin(26);
        var name = l.Name; long id = l.ItemId != 0 ? l.ItemId : _items.IdOf(name);
        return QItemIcon(name, id, 26, round: true, grade: GradeOf(name));
    }

    /// <summary>An item's icon in a slot framed (and lit) in its grade colour; its initial while the icon loads. <paramref name="badge"/> draws the stack count on it.</summary>
    FrameworkElement ItemTile(string name, long id, double size, string grade, string badge = "", bool round = false)
    {
        var c = grade.Length > 0 ? GradeColor(grade) : Color.FromRgb(0xCD, 0xD3, 0xE2);
        bool common = grade is "" or "Common";
        var slot = Slot(id);
        var box = new Border
        {
            Width = size, Height = size, CornerRadius = new CornerRadius(round ? size / 2 : Math.Max(4, size * 0.18)), BorderThickness = new Thickness(1),
            BorderBrush = new SolidColorBrush(Color.FromArgb(common ? (byte)0x47 : (byte)0x99, c.R, c.G, c.B)),
            Background = new RadialGradientBrush(Color.FromArgb(common ? (byte)0x1A : (byte)0x42, c.R, c.G, c.B), CkSlot) { Center = new Point(0.5, 0.38), GradientOrigin = new Point(0.5, 0.38), RadiusX = 0.75, RadiusY = 0.75 },
        };
        var pic = new Image { Source = slot.Image, Stretch = Stretch.Uniform, Margin = new Thickness(Math.Max(1, size * 0.06)) };
        RenderOptions.SetBitmapScalingMode(pic, BitmapScalingMode.HighQuality);
        var letter = KindGlyph(name, id, size);   // the item's kind, not its initial (2026-11-08, Ui/Quest.cs)
        letter.Visibility = slot.Image == null ? Visibility.Visible : Visibility.Collapsed;
        if (slot.Image == null && !ReferenceEquals(slot, IconCache.None))
        {
            System.ComponentModel.PropertyChangedEventHandler? on = null;
            on = (_, _) => { if (slot.Image == null) return; pic.Source = slot.Image; letter.Visibility = Visibility.Collapsed; slot.PropertyChanged -= on; };
            slot.PropertyChanged += on;
        }
        var face = new Grid(); face.Children.Add(letter); face.Children.Add(pic); box.Child = face;
        if (badge.Length == 0) return box;
        var g = new Grid { Width = size + 6, Height = size + 6 };
        box.HorizontalAlignment = HorizontalAlignment.Left; box.VerticalAlignment = VerticalAlignment.Top;
        g.Children.Add(box);
        g.Children.Add(new Border
        {
            HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom, MinWidth = 18, Height = 16, CornerRadius = new CornerRadius(8), Padding = new Thickness(4, 0, 4, 0),
            Background = Ck(CkSlot), BorderBrush = Ck(CkFrame), BorderThickness = new Thickness(1),
            Child = new TextBlock { Text = badge, FontSize = 10.5, FontFamily = new FontFamily("Cascadia Mono, Consolas"), Foreground = Brushes.White, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center },
        });
        return g;
    }

    /// <summary>A gold coin in a slot, for Kinah (the game has no item icon for its money).</summary>
    static FrameworkElement CoinTile(double size, bool round = false)
    {
        var coin = new Ellipse
        {
            Width = size * 0.6, Height = size * 0.6, Stroke = new SolidColorBrush(Color.FromRgb(0xB9, 0x78, 0x1F)), StrokeThickness = 1,
            Fill = new RadialGradientBrush(Color.FromRgb(0xFB, 0xE3, 0xB0), Color.FromRgb(0xD9, 0x95, 0x2F)) { Center = new Point(0.4, 0.35), GradientOrigin = new Point(0.4, 0.35) },
        };
        return new Border
        {
            Width = size, Height = size, CornerRadius = new CornerRadius(round ? size / 2 : Math.Max(4, size * 0.18)), BorderThickness = new Thickness(1), BorderBrush = Ck(CkGold, 0x8C),
            Background = new RadialGradientBrush(Color.FromArgb(0x38, CkGold.R, CkGold.G, CkGold.B), CkSlot) { Center = new Point(0.5, 0.38), GradientOrigin = new Point(0.5, 0.38) }, Child = coin,
        };
    }

    /// <summary>A drawn glyph (24-unit path) in a slot: a run's door, a check, a flag; an enemy's crossed swords.</summary>
    static FrameworkElement GlyphTile(double size, string path, bool gold = false, bool round = false, Color? tint = null)
    {
        var ink = gold ? CkGold : CkMuted;
        var p = new Path { Data = Geometry.Parse(path), Stroke = Ck(ink), StrokeThickness = 1.6, StrokeLineJoin = PenLineJoin.Round, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round, Stretch = Stretch.Uniform, Margin = new Thickness(size * 0.2) };
        if (tint is { } t) { p.Fill = new SolidColorBrush(t); p.Stroke = new SolidColorBrush(Color.FromArgb(0xFF, (byte)Math.Min(255, t.R + 70), (byte)Math.Min(255, t.G + 70), (byte)Math.Min(255, t.B + 70))); p.StrokeThickness = 0.8; }
        return new Border
        {
            Width = size, Height = size, CornerRadius = new CornerRadius(round ? size / 2 : Math.Max(4, size * 0.18)), BorderThickness = new Thickness(1),
            BorderBrush = gold ? Ck(CkGold, 0x8C) : tint is { } t2 ? new SolidColorBrush(Color.FromArgb(0x99, t2.R, t2.G, t2.B)) : new SolidColorBrush(Color.FromArgb(0x47, 0xCD, 0xD3, 0xE2)),
            Background = Ck(CkSlot), Child = p,
        };
    }

    /// <summary>A feed figure in the window's one format (Fk): "+431", "+7.5K", "+18K", "+604K", "+1.27M". It said "+17.6K" where the tables said "18K".</summary>
    static string FeedK(double v)
    {
        double a = Math.Abs(v);
        var s = a < 1000 ? $"{a:N0}" : Fk(a);
        return (v < 0 ? "−" : "+") + s;
    }

    /// <summary>A pickup's worth for the feed: Kinah as it is, an item's tradeable copies at the lowest Market price after the Market's cut (before it with Show amounts before the fee on); blank with no price.</summary>
    (string Text, bool Muted) PickupValue(FeedLine l)
    {
        if (l.Kind == "kinah") return (FeedK(l.Amount), false);
        if (l.Bound) return ("", true);
        return PriceBook.Price(l.Name) is long p ? (FeedK((long)Math.Round(p * l.Amount * PriceBook.SellerKeeps)), false) : ("", true);
    }

    /// <summary>
    /// A craft's worth for the feed: what it made at the lowest Market price after the Market's cut, less what the try cost;
    /// a failed one, minus the try (grey). With a material that has no price yet the cost is only a floor, so the worth is a
    /// ceiling, "≤ +38K" in grey, as the This session table says it (Steven, 2026-10-25: the newest Fine Hard Balaur Horn
    /// line was blank while the table showed "≤ +269K"). Blank only when what came out, or every material, has no price.
    /// </summary>
    /// <summary>The faint word after a won combo roll: what it made of the craft before it ("combo ×2", "combo: Splendent Orichalcum Longsword").</summary>
    static string ComboWon(string recipe) => CraftLedger.ComboOf(recipe) switch
    {
        RecipeBook.ComboMakes.Double => "combo ×2",
        RecipeBook.ComboMakes.Upgrade => "combo: " + RecipeBook.ForOutput(recipe)!.Upgrade!.Item,
        _ => "combo won",
    };

    (string Text, bool Muted) CraftValue(FeedLine l)
    {
        if (l.Name.Length < 2) return ("", true);
        if (l.Combo)
        {
            // a roll costs nothing; a won one is worth what it added to the craft before it (a second output, or the upgrade over the base item)
            if (!l.Ok || RecipeBook.ForOutput(l.Name) is not { } rb) return ("", true);
            int per = Math.Max(1, rb.OutputQty);
            if (rb.Combo == RecipeBook.ComboMakes.Double) return PriceBook.Price(l.Name) is long dp ? (FeedK(dp * per * PriceBook.SellerKeeps), false) : ("", true);
            if (rb.Combo == RecipeBook.ComboMakes.Upgrade && rb.Upgrade is { } ug && PriceBook.Price(ug.Item) is long gp)
                return (FeedK((gp * ug.Qty - (PriceBook.Price(l.Name) ?? 0) * per) * PriceBook.SellerKeeps), false);
            return ("", true);
        }
        var cost = RecipeBook.CostPerTry(l.Name, l.Makes, out int unpriced, out int vendor, out _, new HashSet<string>(StringComparer.OrdinalIgnoreCase));
        if (cost == null) return ("", true);
        bool floor = unpriced + vendor > 0;
        if (!l.Ok) return ((floor ? "≤ " : "") + FeedK(-cost.Value), true);
        // made from bound stock: bound, not for sale; what the try cost (the NPC's ink) is all there is to say
        if (RecipeBook.BoundCraft(l.Name)) return (cost.Value > 0 ? FeedK(-cost.Value) + " · bound" : "bound", true);
        int makes = CraftMakes(l) ?? 1;
        if (PriceBook.Price(l.Name) is not long p) return ("", true);
        var worth = FeedK(p * makes * PriceBook.SellerKeeps - cost.Value);
        return floor ? ("≤ " + worth, true) : (worth, false);
    }

    /* ---------------- the Farming live card ---------------- */

    /// <summary>The rate line beside the feed and the count it was sampled from (a new count starts it over).</summary>
    readonly RateLine _rateLine = new();
    object? _rateOf;

    /// <summary>
    /// The live card's right side for the count on screen: the hour's worth, the rate line since the start, kills and Kinah
    /// a kill (or attempts in a gathering stretch), your best here, and the health line. Called from FillNumbers.
    /// </summary>
    void FillRate(SessionEngine e, double h, long kinah, long loot, int priced, int kills, int nodes, double itemsPerTry)
    {
        long total = kinah + loot;
        // it rolls when the count under it moved (a pickup), not as the hour ticks on; green when it rose 1% (not warming up)
        bool counted = total != _heroTotal; _heroTotal = total;
        if (total > 0) Motion.Number(HeadRate, "hero|" + _part + "|" + CountWhere(), total / h, v => Fk(v), _rateWarming ? -1 : 0.01, roll: counted);
        else Motion.Blank(HeadRate, "hero|" + _part + "|" + CountWhere(), " ");
        // which count it is, on the first line: overall (every zone since a break, the default) or this zone; a click on the
        // figure switches. What it adds up, on the second
        string which = RunShown ? "an hour" : _prefs.AllZones ? "an hour · all zones" : "an hour · this zone";
        HeadRateLbl.Text = total <= 0 ? "Kinah and loot an hour, once something is counted"
            : priced > 0 || loot > 0 ? which + "\n" + (_mode == Mode.Gather && kinah == 0 ? "loot after tax" : "Kinah and loot after tax")
            : which + "\nKinah; loot not priced yet";
        // warming up (FillNumbers): the figure dimmed, and said so, until the count is old enough for an hour to mean something
        bool warm = _rateWarming && total > 0;
        HeadRate.Opacity = warm ? 0.4 : 1;
        if (warm) HeadRateLbl.Text += " · warming up";
        string flip = RunShown ? "" : _prefs.AllZones
            ? $"\nEvery zone since your last break ({TrackerSession.BreakMinutes:0} minutes with nothing farmed). Click for this zone only."
            : "\nThis zone only: it starts over when you change zone. Click for every zone.";
        HeadRate.ToolTip = (warm ? $"Warming up: a rate from the first moments swings wildly. It settles after {RateWarmup.TotalMinutes:0} minutes and {RateWarmupCounts} things counted." : total > 0 ? Amounts.Full(total / h) + " an hour" : "Nothing counted yet.") + flip;
        // one sample a minute, from the first count to now; the window's own count only (a render draws its sample line);
        // none while warming up, so the line doesn't open on a spike
        // a clear that can still be undone draws no line and keeps the real one's samples for Undo
        if (!_demo && !ReferenceEquals(e, _clearedView))
        {
            if (!ReferenceEquals(_rateOf, e)) { _rateOf = e; _rateLine.Clear(); }
            if (total > 0 && !_rateWarming) _rateLine.Add(DateTime.UtcNow, total / h);
        }
        KvLine.Inlines.Clear();
        void Fig(string text) { var r = new Run(text); r.SetResourceReference(TextElement.FontFamilyProperty, "MonoFont"); r.SetResourceReference(TextElement.ForegroundProperty, "Ink"); KvLine.Inlines.Add(r); }
        if (_mode == Mode.Gather) { Fig($"{nodes:N0}"); KvLine.Inlines.Add(new Run(" attempts · ")); Fig(itemsPerTry > 0 ? itemsPerTry.ToString("0.0") : "0"); KvLine.Inlines.Add(new Run(" a try")); }
        else if (kills > 0) { Fig($"{kills:N0}"); KvLine.Inlines.Add(new Run($" kill{(kills == 1 ? "" : "s")} · ")); Fig(Fk(kinah / (double)kills)); KvLine.Inlines.Add(new Run(" a kill")); }
        var best = _demo ? new BestRuns.Best(1_400_000, 48, "2026-10-19") : BestRuns.For(Server?.Label ?? "", _watcher.Chat?.ZoneView?.Zone ?? e.Zone);
        double bestHr = best?.PerHr ?? 0, nowHr = total > 0 ? total / h : 0;
        _lastRateHr = nowHr;
        // Compare (0.9.3, Ui/MainWindow.Compare.cs): your usual and best here under the hour; it says "best here" itself, so this line doesn't
        // and it shows the same best as this line, so switching Compare on never changes what "best here" means
        if (FillUsual(_watcher.Chat?.ZoneView?.Zone ?? _demoZone ?? e.Zone, total > 0 ? (long)Math.Round(total / h) : 0, h * 3600, best?.PerHr)) best = null;
        if (best != null) { if (KvLine.Inlines.Count > 0) KvLine.Inlines.Add(new LineBreak()); KvLine.Inlines.Add(new Run("best here ")); Fig(Fk(best.PerHr)); }
        // the chase (Quest): this hour against your best here, as the bar toward the flag and the ring; the panel's Badge and
        // Buddy draw the same ring from "chase" (the share) and "toBest" (the words)
        FillChase(nowHr, bestHr, warm);
        FillBests(e, h, kinah, nowHr, bestHr, warm);   // a new best here and today's milestones get a moment (Ui/MainWindow.Bests.cs)
        bool chase = nowHr > 0 && bestHr > 0 && !warm;
        _hudValues["chase"] = chase ? QShare(nowHr, bestHr).ToString("0.000", System.Globalization.CultureInfo.InvariantCulture) : "";
        _hudValues["toBest"] = !chase ? "" : nowHr >= bestHr ? "New best here" : $"{Fk(bestHr - nowHr)} to your best here";
        DrawRateLine(ReferenceEquals(e, _clearedView) ? new RateLine() : _rateLine, _demo ? "42 min ago" : _rateLine.Samples.Count > 0 ? AgoMinutes(_rateLine.Samples[0].Minute) : "", "now", bestHr);
        KvLine.Visibility = Visibility.Collapsed;   // the chips and the chase say it (Quest)
        FillHealth(HealthText, HealthMark, hideOk: true);
    }

    /// <summary>A click on the hour: overall (every zone since a break) or this zone, the same switch as ⋯ › All zones.</summary>
    void HeadRate_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (_welcome || RunShown) return;
        _prefs.AllZones = !_prefs.AllZones;
        if (!_demo) _prefs.Save();
        Redraw(); Tick();
        e.Handled = true;
    }

    static string AgoMinutes(DateTime minuteUtc) => TodayLoot.Dur((DateTime.UtcNow - minuteUtc).TotalSeconds) + " ago";

    /// <summary>The hour this count is at, for the Where next line's "43% more than here".</summary>
    double _lastRateHr;

    /// <summary>The rate chart shown or hidden; once it opens (a click on "chart", or its line ready with it open) it draws itself in from the left.</summary>
    void ShowChart(bool on)
    {
        bool was = ChartBox.Visibility == Visibility.Visible;
        ChartBox.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
        if (on && !was) { _rateChart?.Open(); Motion.Reveal(RateCanvas, RateCanvas.Width, RateCanvas.Height); }
    }

    /// <summary>
    /// The rate line (Quest: behind "chart" on the hero): the samples under a gold line in a 628 by 96 box, your best here
    /// as a dashed lilac line at its height on the same scale, clamped to the box when it is above every sample. Live while
    /// open (Ui/RateChart.cs): a new minute runs the line out to it and pops the head dot; the mouse reads a minute.
    /// </summary>
    void DrawRateLine(RateLine line, string from, string to, double best = 0)
    {
        bool show = line.Samples.Count >= 2;
        _sparkOf = line; DrawHeroSpark();   // the hero's faint trend beside the figure (Ui/MainWindow.QuestFarm.cs)
        ChartToggle.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        ShowChart(show && QOpen("chart"));
        QSetChev(ChartChev, QOpen("chart"));
        var chart = LiveChart();
        if (!show) { chart.Forget(); return; }
        RateLinePath.Effect ??= QGlow("Gold", 12, 0.7);
        RateDot.Effect ??= QGlow("Gold", 10, 0.9);
        chart.Draw(line, best, live: ChartBox.Visibility == Visibility.Visible);
        RateFrom.Text = from; RateTo.Text = to;
    }

    /// <summary>
    /// "Reading the chat" with a steady green tick while all is well; paused or a read problem says so instead, with a
    /// warning mark. How many reads, the Market and the Kinah counter are on hover (2026-10-25 audit: "Reading the chat ·
    /// 3,620 reads" read like a parse log, and the count ticking up twice a second drew the eye for nothing).
    /// </summary>
    /// <param name="hideOk">The Farming card's line (2026-10-28): said only when something is wrong; "Reading the chat" is in ⋯.</param>
    /// <summary>How long without a line from the chat before the reading line says so, and asks whether the chat is in view.</summary>
    static readonly TimeSpan NothingReadAfter = TimeSpan.FromMinutes(2);

    /// <summary>
    /// "Reading · last pickup 4s ago" under the zone while all is well; "Nothing read for 2 min. Is chat visible?" when the
    /// chat has gone quiet with the game in front; once the game is read, also while the chat is looked for, paused or has
    /// a read problem (0.9.10). Its dot says which at a glance: green reading, gold looking or paused, red a read problem.
    /// Before the game is read the zone and the line under it already say so, and it stays hidden.
    /// </summary>
    void FillReading(TrackerSession? c, SessionEngine? shown)
    {
        string text = "", tone = "Faint", dot = "Good";
        if (c == null) { }
        else if (c.LastError.Length > 0) { text = "Read problem · Game reading options"; dot = "Bad"; }
        else if (c.Paused.Length > 0) { text = "Paused · the chat is out of view"; dot = "Gold"; }
        else if (!c.ChatFound) { text = "Looking for the chat · reading the game"; dot = "Gold"; }
        else
        {
            var now = DateTime.UtcNow;
            if (now - c.LastLinesAt >= NothingReadAfter) { text = $"Nothing read for {(int)(now - c.LastLinesAt).TotalMinutes} min. Is chat visible?"; tone = "Warn"; dot = "Gold"; }
            else
            {
                text = c.RecentPickups(1).FirstOrDefault() is { } p && p.At != DateTime.MinValue ? "Reading · last pickup " + Ago(now - p.At) + " ago" : "Reading · nothing picked up yet";
                // when the count on screen began: a run's start, or this zone's / this session's (it starts over with Start run and Clear everything)
                if (shown != null) text += " · counting since " + shown.Started.ToLocalTime().ToString("HH:mm");
            }
        }
        if (ReadingLbl.Text != text) ReadingLbl.Text = text;
        ReadingLbl.SetResourceReference(TextBlock.ForegroundProperty, tone);
        ReadingDot.SetResourceReference(Shape.FillProperty, dot);
        var vis = text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        if (ReadingLine.Visibility != vis) ReadingLine.Visibility = vis;
        // the rate column's health line said the same paused or read problem a second time: this line says it now
        if (vis == Visibility.Visible && HealthLine.Visibility == Visibility.Visible) HealthLine.Visibility = Visibility.Collapsed;
        if (ReadingLine.ToolTip == null) ReadingLine.ToolTip = "How reading goes. Click for Options › Game reading.";
    }

    /// <summary>
    /// What the empty Farming feed waits for (0.9.10), from the reader: the game ("Waiting for Aion 2 to start"), the chat
    /// (with the Game reading link, as the footer has), or the first pickup. Redrawn only when that changes.
    /// </summary>
    void FillFeedEmpty()
    {
        var c = _watcher.Chat;
        string text; bool link = false;
        if (_demo) text = _demoPaused ? "Paused: the chat is out of view. Counting picks up when it shows again." : "Ready. Pick something up and it shows here, newest first.";
        else if (c == null && _watcher.GameRect == null) text = "Waiting for Aion 2 to start. Reading starts on its own once it is open.";
        else if (c == null || !c.ChatFound) { text = "Game found, looking for the chat. Keep the chat window open at the bottom left of the game. "; link = true; }
        else if (c.LastError.Length > 0) { text = "The game can't be read right now: " + c.LastError + " "; link = true; }
        else if (c.Paused.Length > 0) text = "Paused: the chat is out of view. Counting picks up when it shows again.";
        else text = "Ready. Pick something up and it shows here, newest first.";
        // a new player's checklist (Ui/MainWindow.Onboard.cs) says the game and the chat itself, so only a read problem or a
        // pause stays above it: reading problems must stay in view
        var steps = OnboardSteps();
        if (steps != null && !(c != null && c.ChatFound && (c.LastError.Length > 0 || c.Paused.Length > 0))) { text = ""; link = false; }
        var sig = text + (link ? "|link" : "") + "|" + OnboardSig(steps);
        if (sig == _feedEmptySig) return;
        _feedEmptySig = sig;
        DrawOnboard(steps);
        FeedEmpty.Visibility = text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        FeedEmpty.Inlines.Clear();
        FeedEmpty.Inlines.Add(new Run(text));
        if (link)
        {
            var go = new Hyperlink(new Run("Game reading options")) { Cursor = Cursors.Hand };
            go.SetResourceReference(FrameworkContentElement.StyleProperty, "LinkStyle");
            go.Click += (_, _) => OpenOptions(OptionsWindow.Page.Reading);
            FeedEmpty.Inlines.Add(go);
        }
    }
    string _feedEmptySig = "";

    static string Ago(TimeSpan t) => t.TotalSeconds < 60 ? $"{Math.Max(0, (int)t.TotalSeconds)}s" : t.TotalMinutes < 60 ? $"{(int)t.TotalMinutes} min" : $"{(int)t.TotalHours} h";

    void FillHealth(TextBlock text, Path? mark, bool hideOk = false)
    {
        var c = _watcher.Chat;
        string t; bool ok = true;
        if (_demo) { t = _demoPaused ? "Paused: the chat is out of view" : "Reading the chat"; ok = !_demoPaused; }
        else if (c == null) { t = _watcher.GameRect == null ? "Waiting for the game" : "Looking for the chat"; ok = false; }
        else if (c.LastError.Length > 0) { t = "Read problem: " + c.LastError; ok = false; }
        else if (c.Paused.Length > 0) { t = "Paused: the chat is out of view"; ok = false; }
        else if (!c.ChatFound) { t = "Reading the game · the chat not seen yet"; ok = false; }
        else t = "Reading the chat";
        if (text.Text != t) text.Text = t;
        var reads = _demo ? "3,620 reads so far." : c != null && c.ChatFound ? $"{c.Reads:N0} reads so far." : "";
        text.ToolTip = _demo ? reads : c == null ? null : string.Join("\n", new[] { reads, $"{MarketShort()}.", $"{BalanceShort()}.",
            _watcher.Flushed > 0 ? $"{_watcher.Flushed} batch{(_watcher.Flushed == 1 ? "" : "es")} shared with the site." : "" }.Where(x => x.Length > 0));
        // the hover covers the whole line, the tick too
        if (text.Parent is Panel line)
        {
            line.ToolTip = text.ToolTip; line.Background ??= Brushes.Transparent;
            if (hideOk) line.Visibility = ok ? Visibility.Collapsed : Visibility.Visible;
        }
        if (mark != null)
        {
            mark.Data = Geometry.Parse(ok ? "M3.5,8.5 L6.5,11.5 L12.5,4.5" : "M8,3 V9.5 M8,12.5 V13");
            mark.SetResourceReference(Shape.StrokeProperty, ok ? "Good" : "Warn");
        }
    }

    /// <summary>The Farming feed: pickups with their worth, Kinah with the enemy it was credited to, gathers.</summary>
    void FillLive()
    {
        DrawFeed(FeedHost, FeedEmptyBox, FeedFor(FeedTab.Farming), FeedTab.Farming, PickupValue);
        FillFeedEmpty();
        // the hint under the feed stays hidden (2026-10-28): its words are the feed's hover, its action ⋯ › A pickup is missing;
        // the line a marked row's Undo takes still goes where it stood (ShowMarkLine)
    }

    /* ---------------- Session and Where to farm now ---------------- */

    /// <summary>
    /// What players at your gear make here, an hour, for LOOT's total hover (it was a row of the Session section, which LOOT
    /// replaced on 2026-10-28). Your best hour here is said once, under the hour on the live card.
    /// </summary>
    string PlayersHereLine(string? zone)
        => PlayerBoards.Here(GearBracket, zone) is { } h && h.Row.ValueHr is long v ? $"{(h.AtGear ? "Players at your gear" : "Players")} make {Fk(v)} an hour here{(h.Everywhere ? ", everywhere" : "")}." : "";

    /// <summary>
    /// Where next (Quest): at rest one line, the zone that pays more than here (or that you are in the one that pays most)
    /// with its hour in mint and who it is from; opened, the best zones for the player's gear as bars, over every region
    /// by default (0.9.9) or on the player's own server, one click apart. A scope with nothing published yet still shows,
    /// saying so, as long as the other has something, so the switch is always there to go back.
    /// </summary>
    void FillWhere(string? zone)
    {
        if (PlayerBoards.FarmAt == null) { WhereSection.Visibility = Visibility.Collapsed; return; }
        bool all = !_prefs.WhereYourServer;
        var (zones, atGear) = PlayerBoards.Top(GearBracket, 5, all);
        bool otherHas = PlayerBoards.Top(GearBracket, 1, !all).Zones.Count > 0;
        // a section with nothing to suggest isn't shown (2026-10-28: "not enough runs on your server yet" took a line for nothing)
        WhereSection.Visibility = _part == "Farming" && !_welcome && (zones.Count > 0 || otherHas) ? Visibility.Visible : Visibility.Collapsed;
        bool open = SecOpen("Where");
        var server = Server?.Name ?? "your server";
        var scope = all ? "every region" : server;
        int hereAt = zone == null ? -1 : zones.FindIndex(z => string.Equals(zone, z.Zone, StringComparison.OrdinalIgnoreCase));
        var top = zones.Count > 0 ? zones[0] : null;
        long topHr = top?.ValueHr ?? 0;
        string more = top != null && _lastRateHr > 0 && topHr > _lastRateHr * 1.05 ? $", {Math.Round(100 * (topHr / _lastRateHr - 1)):0}% more than here" : "";
        WhereTitle.Text = zones.Count == 0 || open ? "Where to farm" : hereAt == 0 ? "You are in the zone that pays most" : $"{top!.Zone} pays more";
        WhereNote.Inlines.Clear();
        if (zones.Count == 0) WhereNote.Inlines.Add(PR($"nothing published for {scope} yet", "Muted"));
        else if (open) WhereNote.Inlines.Add(PR("Kinah and loot an hour · " + (atGear ? $"gear {GearBracket!.Replace("-", "–")}" : "all players") + $" · {(all ? "all regions" : server)} · last 7 days", "Muted"));
        else
        {
            WhereNote.Inlines.Add(PR(Fk(topHr) + " an hour", "Mint", FontWeights.SemiBold));
            WhereNote.Inlines.Add(PR($"{more} · {top!.Runs} runs from {top.Players} players at {(atGear ? "your gear" : "any gear")}", "Muted"));
        }
        WhereSum.Text = open || zones.Count <= 1 ? "" : $"{zones.Count - 1} more";
        // your own best hour in each zone on your server (Session/BestRuns.cs, the card's "best here"), beside the players' figure (0.9.11)
        var bests = zones.Select(z => YourBestHour(z.Zone)).ToList();
        bool cmp = _prefs.CompareZones;
        if (!Changed("where", $"{zone}|{all}|{server}|{cmp}|{Skins.Current}|" + string.Join(";", zones.Select((z, i) => $"{z.Zone}|{z.Runs}|{z.Players}|{z.ValueHr}|{bests[i]}")))) return;
        WhereBody.Children.Clear();
        WhereBody.Children.Add(WhereScopeSwitch(all));
        if (zones.Count == 0) WhereBody.Children.Add(Faint(all ? "No zone has enough runs across the regions yet." : $"No zone has enough runs on {server} yet. Every region has some."));
        for (int i = 0; i < zones.Count; i++)
        {
            var z = zones[i];
            var key = z.Zone;
            bool isHere = i == hereAt;
            var sub = $"{z.Runs} runs · {z.Players} players" + (isHere ? " · you are here" : "");
            var row = QRow(isHere ? QIconBox("Flag", "Gold", 30, round: true) : QIconBox("Pin", "Sky", 30, round: true), z.Zone, sub, Fk(z.ValueHr ?? 0), "Ink",
                topHr > 0 ? (z.ValueHr ?? 0) / (double)topHr : null, isHere ? "Gold" : "Sky", rule: i > 0);
            var g = (Grid)row.Child;
            g.Background = Brushes.Transparent; g.Cursor = Cursors.Hand;
            if (bests[i] is long yb)
            {
                // "your best 1.4M" under the figure (options-test reads it by its tag)
                var y = new TextBlock { FontSize = 10.5, FontWeight = FontWeights.Medium, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom, Tag = "where-best", Margin = new Thickness(0, 0, 0, -1) };
                y.Inlines.Add(PR("your best ", "Faint")); y.Inlines.Add(PR(Fk(yb), "Muted"));
                foreach (var v in g.Children.OfType<TextBlock>().Where(t => Grid.GetColumn(t) == 2 && Grid.GetRow(t) == 0)) { v.VerticalAlignment = VerticalAlignment.Top; v.Margin = new Thickness(0, -2, 0, 0); }
                Grid.SetColumn(y, 2); g.Children.Add(y);
            }
            g.MouseLeftButtonUp += (_, _) => CompareZone(key);
            g.MouseRightButtonUp += (_, e) => { e.Handled = true; Open(ZonePageUrl(key)); };
            // the zone you are in: the look's gold tint stays under the mouse
            if (isHere) { row.SetResourceReference(Border.BackgroundProperty, "GoldTint"); row.MouseLeave += (_, _) => row.SetResourceReference(Border.BackgroundProperty, "GoldTint"); }
            var tip = $"{(isHere ? "You are here. " : "")}{z.Runs} runs from {z.Players} players {(all ? "across every region" : "on " + server)} in the last 7 days · Kinah plus loot {PriceBook.Cut}, an hour."
                + (bests[i] is long tb ? $"\nYour best hour here on {server}: {Fk(tb)}." : "")
                + (cmp ? "\nClick to compare it with your zones this week (LOOT › Week › Zones). Right-click for its page on kinahledger.com."
                       : "\nClick for its page on kinahledger.com. Turn on Compare zones and runs in Options › General to compare zones here.");
            var b = new Border { Child = row, ToolTip = tip, Tag = "where-row|" + key + (isHere ? "|here" : "") };
            ToolTipService.SetInitialShowDelay(b, 500);
            WhereBody.Children.Add(b);
        }
    }

    /// <summary>Your best hour in a zone on your server (Session/BestRuns.cs), or null; a render's sample zone has the card's sample best.</summary>
    long? YourBestHour(string zone)
        => _demo ? (string.Equals(zone, _demoZone, StringComparison.OrdinalIgnoreCase) ? 1_400_000 : null) : BestRuns.For(Server?.Label ?? "", zone)?.PerHr;

    /// <summary>"All regions | Your server" over the zones, as LOOT's side switch: the choice is kept in the prefs.</summary>
    FrameworkElement WhereScopeSwitch(bool all)
    {
        Button Seg(string text, bool on, bool yourServer, string tip)
        {
            var b = new Button { Content = text, Height = 26, FontSize = 12.5, Padding = new Thickness(10, 0, 10, 0), ToolTip = tip, Margin = new Thickness(yourServer ? 2 : 0, 0, 0, 0) };
            b.SetResourceReference(StyleProperty, on ? "SegOn" : "SegOff");
            b.Click += (_, _) => { _prefs.WhereYourServer = yourServer; if (!_demo) _prefs.Save(); _drawnSig.Remove("where"); FillWhere(_watcher.Chat?.ZoneView?.Zone ?? _demoZone); };
            return b;
        }
        var group = new Border { Padding = new Thickness(3), CornerRadius = new CornerRadius(12), BorderThickness = new Thickness(1), HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 2, 0, 8),
            Child = new StackPanel { Orientation = Orientation.Horizontal, Children = {
                Seg("All regions", all, false, "Every region's players pooled, the last 7 days."),
                Seg("Your server", !all, true, $"Only players on {Server?.Name ?? "your server"}, the last 7 days.") } } };
        group.SetResourceReference(Border.BorderBrushProperty, "LineStrong"); group.SetResourceReference(Border.BackgroundProperty, "SegGroup");
        return group;
    }

    /* ---------------- Also this session ---------------- */

    /// <summary>The chip open in place ("Gather", "skill:Mining", "Pet", "Mob"), or null.</summary>
    string? _extraOpen;

    /// <summary>
    /// The chips under the drops, one per thing counted besides them (Gathered, each gathering skill, Soul Crystals, Kinah by
    /// enemy), each opening its own small table in place under a gold rule. Only one is open at a time.
    /// </summary>
    /// <summary>The game's daily gathering limit (1,000 a server, reset at midnight Pacific): a muted line under the chips, and each warning said once a game day.</summary>
    void ShowGatherLimit()
    {
        try
        {
            var server = Server?.Label ?? "";
            var now = DateTime.UtcNow;
            var line = Session.GatherDay.Line(server, now);
            GatherLimit.Text = line; GatherLimit.Visibility = line.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
            if (Session.GatherDay.Warning(server, now) is { } w) SayOnLine(w, "Warn");
        }
        catch (Exception ex) { CrashLog.Write("gather limit", ex); }
    }

    void FillExtras(List<DropRow> gathered, List<SkillBox> skills, List<DropRow> pets, List<DropRow> mobs, long kinah)
    {
        var keys = new List<string>();
        var words = new System.Text.StringBuilder(_extraOpen).Append('|').Append(_lastMobKinah).Append('|');
        bool build = false;   // set once the chips' words are known to differ from the last draw
        void Chip(string key, Func<FrameworkElement> icon, string label, string figure, string iconOf)
        {
            keys.Add(key);
            words.Append(key).Append('|').Append(label).Append('|').Append(figure).Append('|').Append(iconOf).Append(';');
            if (!build) return;
            bool on = _extraOpen == key;
            var sp = new StackPanel { Orientation = Orientation.Horizontal };
            var ic = icon(); ic.Margin = new Thickness(0, 0, 7, 0); ic.VerticalAlignment = VerticalAlignment.Center;
            sp.Children.Add(ic);
            var t = new TextBlock { FontSize = 12.5, VerticalAlignment = VerticalAlignment.Center }; t.SetResourceReference(TextBlock.ForegroundProperty, "Ink");
            t.Inlines.Add(new Run(label));
            if (figure.Length > 0) { var f = new Run("  " + figure); f.SetResourceReference(TextElement.ForegroundProperty, on ? "Gold" : "Muted"); t.Inlines.Add(f); }
            sp.Children.Add(t);
            var b = new Border
            {
                Child = sp, Height = 34, CornerRadius = new CornerRadius(8), Padding = new Thickness(5, 0, 12, 0), Margin = new Thickness(0, 0, 8, 8), BorderThickness = new Thickness(1), Cursor = Cursors.Hand,
            };
            // each look's own chip colours (Daylight had dark chips with dark text)
            b.SetResourceReference(Border.BackgroundProperty, on ? "GoldTint" : "ChipOff");
            b.SetResourceReference(Border.BorderBrushProperty, on ? "GoldLine" : "ChipOffLine");
            b.MouseLeftButtonUp += (_, _) => { _extraOpen = _extraOpen == key ? null : key; Redraw(); };
            ExtraChips.Children.Add(b);
        }
        // two passes over the same chips: the first collects their words, and the second builds them only if the words
        // changed since the last draw (the icons, tiles with an image each, are the cost)
        void Chips()
        {
            if (gathered.Count > 0) Chip("Gather", () => ItemTile(gathered[0].Name, _items.IdOf(gathered[0].Name), 24, gathered[0].Grade, round: true), "Gathered", $"{gathered.Count} kind{(gathered.Count == 1 ? "" : "s")}", gathered[0].Name + gathered[0].Grade);
            foreach (var sk in skills)
            {
                var first = gathered.FirstOrDefault(g => SkillOf(g.Name) == sk.Name);
                Chip("skill:" + sk.Name, () => first != null ? ItemTile(first.Name, _items.IdOf(first.Name), 24, first.Grade, round: true) : GlyphTile(24, "M4,20 L14,10 M12,4 L20,12 L17,15 L9,7 Z", round: true), sk.Name.Split(" · ")[0], sk.Value > 0 ? Fk(sk.Value) : $"{sk.Attempts:N0} tries", first?.Name + first?.Grade);
            }
            int crystals = pets.Sum(r => int.TryParse(r.Drops, System.Globalization.NumberStyles.AllowThousands, null, out var n) ? n : 0);
            if (pets.Count > 0) Chip("Pet", () => GlyphTile(24, "M12,2 L18,9 L12,22 L6,9 Z M6,9 H18", round: true, tint: Color.FromRgb(0x3E, 0x9E, 0x62)), "Soul Crystals", crystals.ToString("N0"), "");
            if (mobs.Count > 0) Chip("Mob", () => CoinTile(24, round: true), "Kinah by enemy", _lastMobKinah > 0 ? Fk(_lastMobKinah) : "", "");
        }
        Chips();
        if (Changed("chips", words.ToString()))
        {
            build = true; keys.Clear(); ExtraChips.Children.Clear();
            Chips();
        }
        if (_extraOpen != null && !keys.Contains(_extraOpen)) _extraOpen = null;
        ExtrasSection.Visibility = keys.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

        // the open chip's table; the tables keep their row controls, so the shut ones are emptied rather than replaced
        SkillLines.Children.Clear();
        IReadOnlyList<DropRow> gatherRows = NoRows, mobRows = NoRows, petRows = NoRows;
        InPlaceLead.Visibility = InPlaceNote.Visibility = InPlaceMore.Visibility = MobCredit.Visibility = Visibility.Collapsed;
        InPlace.Visibility = _extraOpen != null ? Visibility.Visible : Visibility.Collapsed;
        var more = "x:" + _extraOpen;
        List<DropRow> Top(List<DropRow> rows) => TopRows(more, rows, TableRows, InPlaceMore, InPlaceMoreRun);
        void Heads(string a, string b, string c) { InPlaceH1.Text = a; InPlaceH2.Text = b; InPlaceH3.Text = c; }
        switch (_extraOpen)
        {
            case "Gather":
                Heads("Qty", "Per try", "Value");
                gatherRows = Top(gathered.Select(r => r with { Sub = r.Reported ? "reported" : "" }).ToList());
                if (gathered.Any(r => r.Value.Length == 0)) { InPlaceNote.Text = $"{gathered.Count(r => r.Value.Length == 0)} have no price yet."; InPlaceNote.Visibility = Visibility.Visible; }
                break;
            case { } s when s.StartsWith("skill:"):
            {
                var sk = skills.First(x => "skill:" + x.Name == s);
                int ok = sk.Attempts - sk.Failed;
                Heads("Qty", "Per try", "Value");
                InPlaceLead.Text = $"{sk.Attempts:N0} tr{(sk.Attempts == 1 ? "y" : "ies")}, {ok:N0} worked · {Whole(sk.PerHour)} an hour" + (ok > 0 && sk.Plentiful > 0 ? $" · {Math.Round(100.0 * sk.Plentiful / ok)}% plentiful" : "");
                InPlaceLead.Visibility = Visibility.Visible;
                gatherRows = Top(gathered.Where(g => SkillOf(g.Name) == sk.Name).Select(r => r with { Sub = "" }).ToList());
                break;
            }
            case "Pet":
                Heads("Qty", "An hour", "");
                petRows = Top(pets.Select(r => r with { DropPct = r.PerHour, Sub = "" }).ToList());
                break;
            case "Mob":
            {
                Heads("Kills", "A kill", "Kinah");
                mobRows = Top(mobs.Select(r => r.Child ? r with { Sub = r.Sub } : r with { Sub = "" }).ToList());
                long none = kinah - _lastMobKinah;
                if (none > 0) { InPlaceNote.Text = $"{Fk(none)} of the session's Kinah came with no enemy named."; InPlaceNote.Visibility = Visibility.Visible; }
                MobCredit.Visibility = Visibility.Visible;
                break;
            }
        }
        SyncRows(GatherRows, gatherRows); SyncRows(MobRows, mobRows); SyncRows(PetRows, petRows);
        GatherRows.Visibility = gatherRows.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        MobRows.Visibility = mobRows.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        PetRows.Visibility = petRows.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    string SkillOf(string item) => GatheringCatalog.Find(item) is { } g ? GatherSkills.Label(g.Skill) : "";

    /// <summary>A rate as a whole number ("58", "10"; under one, "under 1").</summary>
    static string Whole(double v) => v <= 0 ? "0" : v < 0.5 ? "under 1" : Math.Round(v).ToString("N0");

    /* ---------------- click a line: the miscount report ---------------- */

    /// <summary>
    /// A clicked feed line (or the hint, for a pickup that never showed): the line as it was, three choices (Wrong item, Wrong
    /// amount, Didn't happen) and "Missing a pickup" at the bottom, filed through the miscount report with the line marked.
    /// </summary>
    void ShowLineReport(FeedLine? line, FeedTab tab, FrameworkElement at)
    {
        if (_card != null) _card.IsOpen = false;
        _card = new InCard { Child = BuildLineReport(line, tab, line == null ? "missing" : "wrong_amount"), StaysOpen = false, AllowsTransparency = true, PopupAnimation = PopupAnimation.Fade, Placement = PlacementMode.Mouse, PlacementTarget = at };
        _card.IsOpen = true;
    }

    /// <summary>A line's reason as the card shows it: its own, else up to four notes from within 3 s of it (time stripped), else where the full trail is.</summary>
    internal static List<string> WhyLines(FeedLine line)
    {
        if (line.Why.Length > 0) return new() { Capital(line.Why) };
        var near = CrashLog.NotesNear(line.At, 3).TakeLast(4).Select(n => Capital(n.Text)).ToList();
        return near.Count > 0 ? near : new() { "No note for this one. The reads log has the full trail: Options › About › Open logs." };
    }

    static string Capital(string s) => s.Length > 0 && char.IsLower(s[0]) ? char.ToUpperInvariant(s[0]) + s[1..] : s;

    internal Border BuildLineReport(FeedLine? line, FeedTab tab, string label, string note = "")
    {
        var (card, body) = CkShell(420);
        var head = CkSection(line == null || label == "missing" ? "A PICKUP THAT NEVER SHOWED" : "REPORT THIS LINE"); body.Children.Add(head);
        if (line != null)
        {
            var g = new Grid();
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); g.ColumnDefinitions.Add(new ColumnDefinition()); g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            g.Children.Add(CkText(line.At.ToLocalTime().ToString("HH:mm:ss"), CkFaint, 12, mono: true));
            var what = CkText(line.Text, CkInk, 13, FontWeights.Normal); what.Margin = new Thickness(12, 0, 12, 0); Grid.SetColumn(what, 1); g.Children.Add(what);
            var val = tab == FeedTab.Crafting ? CraftValue(line) : PickupValue(line);
            var v = CkText(val.Text, val.Muted ? CkFaint : CkGold, 12.5, mono: true); Grid.SetColumn(v, 2); g.Children.Add(v);
            body.Children.Add(CkBox(g, new Thickness(20, 0, 20, 0)));
            if (ViaTip(line.Via) is { Length: > 0 } how) { var n = CkNote(how); n.Margin = new Thickness(20, 6, 20, 0); body.Children.Add(n); }
            // why it was counted, in the reads log's own words (Steven, 2026-11-04: he found the day's miscounts by reading
            // reads.log for these; the line says it now). A line with no reason of its own offers the notes written around it.
            // Only with Options › Game reading › Explain each count on (Steven, 2026-11-04: "Explain this should be an option
            // you can turn on, in dev mode or something"): it is for checking the reading, not for every player.
            if (_prefs.ExplainCounts)
            {
                body.Children.Add(CkSection("WHY IT WAS COUNTED"));
                foreach (var w in WhyLines(line)) body.Children.Add(CkNote(w));
            }
        }
        // the choices: one is picked; Missing a pickup sits apart at the bottom
        var picks = new WrapPanel { Margin = new Thickness(20, 12, 20, 0) };
        string chosen = label;
        TextBox? missing = null;
        void Redo(string l) { if (_card is { IsOpen: true }) _card.Child = BuildLineReport(line, tab, l, missing?.Text ?? note); }
        if (line != null && label != "missing")
            foreach (var (key, text) in MiscountLine.Choices.Where(c => c.Label != "missing"))
            {
                bool on = key == chosen;
                var pill = new Border
                {
                    Child = CkText(text, on ? CkGold : CkMuted, 12.5), Padding = new Thickness(12, 5, 12, 6), Margin = new Thickness(0, 0, 8, 8), CornerRadius = new CornerRadius(8), BorderThickness = new Thickness(1), Cursor = Cursors.Hand,
                    BorderBrush = on ? Ck(CkGold, 0x99) : Ck(CkSlotLine), Background = on ? Ck(CkGold, 0x14) : Brushes.Transparent,
                };
                pill.MouseLeftButtonUp += (_, _) => Redo(key);
                picks.Children.Add(pill);
            }
        else
        {
            var lbl = CkText("What should have been counted? A name and amount helps.", CkMuted, 12.5); lbl.Margin = new Thickness(0, 0, 0, 6);
            missing = new TextBox { Width = 380, FontSize = 13, Padding = new Thickness(6, 4, 6, 4), Background = Ck(CkSlot), BorderBrush = Ck(CkFrame), Foreground = Ck(CkInk), CaretBrush = Ck(CkGold), Text = note };
            var sp = new StackPanel(); sp.Children.Add(lbl); sp.Children.Add(missing);
            picks.Children.Add(sp);
        }
        body.Children.Add(picks);
        bool sends = _prefs.SendReportFrames && _prefs.Share;
        var how2 = CkNote("Saves the last minute of what was read, with this " + (line != null && label != "missing" ? "line" : "note") + " marked, to your reports folder. "
            + (sends ? "Pictures of the loot list and crafting panel go to the site too (Options › Sharing)." : ReportBuffer.KeepFrames ? "Nothing is sent." : "Pictures aren't kept (Options › Sharing), so it holds the text read."));
        how2.Margin = new Thickness(20, 4, 20, 0); body.Children.Add(how2);
        var acts = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(20, 12, 20, 0) };
        // one name for it everywhere: Report on the action, Looks wrong? as the hint, Fine to leave the line as it is
        var save = new Button { Content = "Report", Height = 30, Padding = new Thickness(16, 0, 16, 0), FontSize = 12.5 };
        save.SetResourceReference(StyleProperty, "PrimaryButton");
        save.Click += (_, _) =>
        {
            if (_card != null) _card.IsOpen = false;
            FileLineReport(new MiscountLine(chosen, chosen == "missing" ? null : line), missing?.Text.Trim() ?? "");
        };
        acts.Children.Add(save);
        var cancel = new Button { Content = line != null && label != "missing" ? "Fine" : "Cancel", Height = 30, Padding = new Thickness(14, 0, 14, 0), FontSize = 12.5, Margin = new Thickness(8, 0, 0, 0) };
        cancel.Click += (_, _) => { if (_card != null) _card.IsOpen = false; };
        acts.Children.Add(cancel);
        body.Children.Add(acts);
        // Missing a pickup, at the bottom: the line was fine, something else never showed
        if (line != null)
        {
            var other = new TextBlock { Margin = new Thickness(20, 12, 20, 14), FontSize = 12 };
            var link = new Hyperlink(new Run(label == "missing" ? "Report this line instead" : "Missing a pickup")) { Foreground = Ck(CkGold), TextDecorations = null };
            link.Click += (_, _) => Redo(label == "missing" ? "wrong_amount" : "missing");
            other.Inlines.Add(link);
            body.Children.Add(other);
        }
        else body.Children.Add(new Border { Height = 14 });
        return card;
    }

    /// <summary>
    /// Files the report the way "Report a miscount" does (TrackerSession.WriteReport: the last minute of reads, pickups and,
    /// when the player said yes, pictures), with what was flagged in its summary and line.json beside it; the loot list and
    /// crafting panel pictures go to the site only when Options › Sharing allows (SendReportPictures).
    /// </summary>
    void FileLineReport(MiscountLine m, string note)
    {
        if (SaveLineReport(m, note) is not { } saved) return;
        SayOnLine("Report saved with the line marked, in your reports folder. Thanks: it becomes a test the tracker must pass.", "Good");
        Status.ToolTip = saved.Dir;
        _ = SendReportPictures(saved.Dir, saved.Why);
    }

    /// <summary>
    /// The report's saving part, shared with the That was wrong key (Ui/MainWindow.MarkWrong.cs): the last minute and
    /// line.json written to a new folder under reports/. Its folder and summary, or null (said in the status line) when it
    /// couldn't be saved. Nothing is sent from here.
    /// </summary>
    (string Dir, string Why)? SaveLineReport(MiscountLine m, string note)
    {
        var c = _watcher.Chat;
        if (c == null) { Say("Nothing to report yet: the last minute is kept once the tracker is reading the game.", "Warn"); return null; }
        try
        {
            var why = m.Why + (note.Length > 0 ? $" · {note}" : "");
            var dir = c.WriteReport(why);
            var frames = System.IO.Path.Combine(dir, "frames.jsonl");
            File.WriteAllText(System.IO.Path.Combine(dir, "line.json"), m.Json(File.Exists(frames) ? File.ReadAllLines(frames) : Array.Empty<string>()));
            CrashLog.Note($"feed line reported: {why}");
            return (dir, why);
        }
        catch (Exception ex) { CrashLog.Write("line report", ex); Say("Couldn't save the report: " + ex.Message, "Bad"); return null; }
    }

    /* ---------------- the panel over the game ---------------- */

    /// <summary>The craft part's profit this session ("+686K") and the events' rate across runs, from their last fill, for the panel.</summary>
    string _craftNet = "", _eventsRate = "";
    /// <summary>A run of this session that just closed flagged ("Urugugu record looks like 5 runs"), for the panel; "" when none.</summary>
    string _hudFlag = "";

    /// <summary>
    /// The panel's feed cell (direction C): the newest thing counted on any tab with its worth, and that tab's rate beside it
    /// (Farming the hour's worth, Crafting the profit today, Events the hour across runs); a run just closed flagged instead.
    /// </summary>
    void HudFeed()
    {
        var newest = new[] { FeedTab.Farming, FeedTab.Crafting, FeedTab.Events }.Select(t => FeedFor(t).FirstOrDefault()).Where(l => l != null).OrderByDescending(l => l!.At).FirstOrDefault();
        foreach (var (k, v) in HudFeedValues(newest, _hudFlag)) _hudValues[k] = v;
        HudLookValues();   // the Card's and the Ticker's pickups, zone, pause and rare drop (MainWindow.HudLooks.cs)
        HudCraftValues();  // the Card's and the Ticker's crafting line while the crafting window is open (MainWindow.HudCraft.cs)
    }

    /// <summary>A queue's count against its size, "3/10", while it runs or for CraftLedger.PanelSeconds after; "" otherwise or with its size unread.</summary>
    internal static string QueueShort(TrackerSession.CraftQueueView? q) =>
        q is { Of: > 0 } && (q.Open || q.EndedAt is DateTime end && (DateTime.UtcNow - end).TotalSeconds <= CraftLedger.PanelSeconds) ? $"{q.Done}/{q.Of}" : "";

    Dictionary<string, string> HudFeedValues(FeedLine? l, string flag)
    {
        var d = new Dictionary<string, string> { ["flag"] = flag, ["feedText"] = "", ["feedValue"] = "", ["feedRate"] = "", ["feedUnit"] = "" };
        // the farming hour beside the line: "warming up" alone while the count is young (FillNumbers), else "1.27M an hour"
        var hour = _hudValues.GetValueOrDefault("rate", "");
        var hourUnit = hour == WarmingUp ? "" : "an hour";
        if (l == null) { d["feedRate"] = hour; d["feedUnit"] = hourUnit; return d; }
        var (bold, plain, small) = FeedWords(l);
        d["feedText"] = l.Kind == "craft" ? $"{bold}{plain} {small}" : l.Kind == "kinah" ? bold : $"{bold}{(l.Amount > 1 ? plain : "")}";
        // the queue under way beside a craft: "· 3/10" (Steven, 2026-11-04: a miss should show while it happens)
        if (l.Tab == FeedTab.Crafting && !_demo && QueueShort(_watcher.Chat?.CraftQueue) is { Length: > 0 } qs) d["feedText"] += " · " + qs;
        d["feedValue"] = (l.Tab == FeedTab.Crafting ? CraftValue(l) : PickupValue(l)).Text;
        (d["feedRate"], d["feedUnit"]) = l.Tab switch
        {
            FeedTab.Crafting => (_craftNet, "today"),
            FeedTab.Events => (_eventsRate, "an hour"),
            _ => (hour, hourUnit),
        };
        return d;
    }

    /// <summary>For layout renders: the panel in its three states, farming, crafting and a flagged run, with the sample feed.</summary>
    internal List<(string File, Dictionary<string, string> Values)> HudStates()
    {
        var farm = HudFeedValues(FeedFor(FeedTab.Farming).FirstOrDefault(), "");
        farm["feedRate"] = _hudValues.GetValueOrDefault("rate", "");
        var craftLine = FeedFor(FeedTab.Crafting).FirstOrDefault();
        var craft = HudFeedValues(craftLine, "");
        if (craft["feedRate"].Length == 0) craft["feedRate"] = "+686K";
        var flag = HudFeedValues(null, "Urugugu Canyon record looks like 5 runs");
        // the hot streak bar from the sample feed's pickups (Ui/HudStreak.cs), part filled
        foreach (var d in new[] { farm, craft, flag }) d["streak"] = HudStreakNow();
        return new() { ("ui-hud-farming.png", farm), ("ui-hud-crafting.png", craft), ("ui-hud-flag.png", flag) };
    }

    /* ---------------- layout renders ---------------- */

    /// <summary>For layout review: the report sheet a tap on the second feed line opens, drawn to a PNG.</summary>
    internal void ShotLineReport(string path)
    {
        var lines = FeedFor(FeedTab.Farming);
        if (lines.Count < 2) return;
        SaveCard(BuildLineReport(lines[1], FeedTab.Farming, "wrong_amount"), path);
    }

    /// <summary>For the options self-test: whether the second sample line's card has its WHY IT WAS COUNTED section (Explain each count).</summary>
    internal bool SampleLineReportExplains()
    {
        var lines = FeedFor(FeedTab.Farming);
        if (lines.Count < 2) return false;
        static IEnumerable<TextBlock> Texts(DependencyObject o) =>
            (o is TextBlock tb ? new[] { tb } : Enumerable.Empty<TextBlock>()).Concat(LogicalTreeHelper.GetChildren(o).OfType<DependencyObject>().SelectMany(Texts));
        return Texts(BuildLineReport(lines[1], FeedTab.Farming, "wrong_amount")).Any(t => t.Text == "WHY IT WAS COUNTED");
    }

    /// <summary>Sample feed lines for a layout render: the shipped screens' three newest lines and the mock's others. Never the player's.</summary>
    void LoadDemoFeed(string kind)
    {
        var t = DateTime.UtcNow;
        long Id(string n) => _items.IdOf(n);
        _demoFeed = new List<FeedLine>
        {
            new(t.AddSeconds(-4), FeedTab.Farming, "item", "Spiritstone", 1, Via: "chat · recovered", Where: "Farming · Western Cantas Valley", ItemId: Id("Spiritstone")),
            new(t.AddSeconds(-5), FeedTab.Farming, "kinah", "", 1_192, "Abyss Drakan Striker", Via: "chat · recovered", Where: "Farming · Western Cantas Valley"),
            new(t.AddSeconds(-7), FeedTab.Farming, "kinah", "", 431, "Abyss Drakan Scout", Via: "chat", Where: "Farming · Western Cantas Valley"),
            new(t.AddSeconds(-18), FeedTab.Farming, "item", "Raw Leather", 1, Via: "chat", Where: "Farming · Western Cantas Valley", ItemId: Id("Raw Leather")),
            new(t.AddSeconds(-35), FeedTab.Farming, "item", kind == "Gathering" ? "Sapphire Gemstone" : "Orichalcum Ore", 1, Via: "chat", Where: "Farming · Western Cantas Valley"),
        };
        if (kind == "Gathering")
            _demoFeed = new List<FeedLine>
            {
                new(t.AddSeconds(-3), FeedTab.Farming, "item", "Sapphire Gemstone", 1, Via: "chat", Where: "Farming · Western Cantas Valley"),
                new(t.AddSeconds(-9), FeedTab.Farming, "item", "Orichalcum Ore", 2, Via: "chat", Where: "Farming · Western Cantas Valley"),
                new(t.AddSeconds(-20), FeedTab.Farming, "item", "Odyle", 1, Via: "chat", Where: "Farming · Western Cantas Valley"),
                new(t.AddSeconds(-31), FeedTab.Farming, "item", "Orichalcum Ore", 1, Via: "chat", Where: "Farming · Western Cantas Valley"),
                new(t.AddSeconds(-44), FeedTab.Farming, "item", "Kukuru", 1, Via: "chat", Where: "Farming · Western Cantas Valley"),
            };
        // crafts: the morning in the mock, newest first
        _demoFeed.AddRange(new FeedLine[]
        {
            new(t.AddSeconds(-6), FeedTab.Crafting, "craft", "Diamond Powder", 1, Ok: true, Via: SessionEngine.CraftPanelVia, Where: "Crafting"),
            new(t.AddMinutes(-2), FeedTab.Crafting, "craft", "Orichalcum Greaves", 1, Ok: false, Via: SessionEngine.CraftPanelVia, Where: "Crafting"),
            new(t.AddMinutes(-3), FeedTab.Crafting, "craft", "Orichalcum Greaves", 1, Ok: true, Via: SessionEngine.CraftPanelVia, Where: "Crafting"),
            new(t.AddMinutes(-6), FeedTab.Crafting, "craft", "Sapphire Powder", 1, Ok: true, Via: SessionEngine.CraftPanelVia, Where: "Crafting"),
            new(t.AddMinutes(-9), FeedTab.Crafting, "craft", "Sapphire Powder", 1, Ok: true, Via: SessionEngine.CraftPanelVia, Where: "Crafting"),
        });
        // the open expedition's reward screen lines (the Events render's runs are DemoRuns, in Ui/MainWindow.RunsCard.cs)
        _demoFeed.AddRange(new FeedLine[]
        {
            new(t.AddSeconds(-3), FeedTab.Events, "item", "Spiritstone Powder", 6, Via: "reward table", Where: "Expedition · Urugugu Canyon", ItemId: Id("Spiritstone Powder")),
            new(t.AddSeconds(-50), FeedTab.Events, "kinah", "", 2_410, Via: "chat", Where: "Expedition · Urugugu Canyon"),
        });
        // the rate line: 42 minutes of a rate settling up to the hour's worth
        _rateLine.Clear();
        var rnd = new Random(7);
        for (int i = 0; i <= 42; i++) _rateLine.Add(t.AddMinutes(i - 42), 760_000 + 510_000 * Math.Sqrt(i / 42.0) + rnd.Next(-60_000, 60_000));
    }
}
