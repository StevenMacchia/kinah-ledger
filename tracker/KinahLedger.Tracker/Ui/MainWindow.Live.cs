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
/// "Reading the chat · N reads", the one place the read count shows. A line that looks wrong is tapped and filed as a
/// miscount report with that line marked (line.json), a labelled case for the replay bank. Under the live card the sections
/// fold: Session, Drops from kills (open), Also this session (chips that open a small table in place), Where to farm now.
/// </summary>
public partial class MainWindow
{
    /* ---------------- sections you open ---------------- */

    /// <summary>Sections the player opened or folded, by name; the rest follow the view (Session and Where to farm open in Full view only).</summary>
    readonly Dictionary<string, bool> _secUser = new();

    bool SecOpen(string key) => _secUser.TryGetValue(key, out var v) ? v : key is "Session" or "Where" ? _prefs.FullView : true;

    void ToggleSec(string key)
    {
        _secUser[key] = !SecOpen(key);
        if (key is "CraftSession" or "Best") { if (!_demo) FillCraft(_watcher.Chat?.CraftView); else ApplySections(); return; }
        Redraw();
    }

    /// <summary>A section's chevron and body: pointing right and folded, or down and gold with the body showing.</summary>
    void SetSec(Path chev, FrameworkElement body, bool open)
    {
        chev.RenderTransform = new RotateTransform(open ? 90 : 0);
        chev.SetResourceReference(Shape.StrokeProperty, open ? "Gold" : "Faint");
        body.Visibility = open ? Visibility.Visible : Visibility.Collapsed;
    }

    void ApplySections()
    {
        SetSec(SessionChev, SessionBody, SecOpen("Session"));
        SetSec(DropsChev, DropsOpenBody, SecOpen("Drops"));
        SetSec(ExtrasChev, ExtrasBody, SecOpen("Extras"));
        SetSec(WhereChev, WhereBody, SecOpen("Where") && WhereBody.Children.Count > 0);
        SetSec(CraftSessionChev, CraftBody, SecOpen("CraftSession"));
        SetSec(BestChev, BestCraftsHost, SecOpen("Best"));
    }

    /// <summary>What options-test knows as the recent pickups strip: since the feed (0.8.30), the Session section's figures, open in Full view.</summary>
    internal FrameworkElement PickupStrip => SessionBody;
    /// <summary>What options-test knows as the top drops: since the feed, Where to farm now's zones, open in Full view.</summary>
    internal FrameworkElement TopDropsBox => WhereBody;

    /// <summary>The section heads, the feed's hints and the in-place table's "show all", wired once from the constructor.</summary>
    void InitLive()
    {
        foreach (var (head, key) in new (FrameworkElement, string)[] { (SessionHead, "Session"), (DropsHead, "Drops"), (ExtrasHead, "Extras"), (WhereHead, "Where"), (CraftSessionHead, "CraftSession"), (BestHead, "Best") })
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
    }

    /* ---------------- the feed ---------------- */

    /// <summary>Sample lines for layout renders (never the player's), by tab.</summary>
    List<FeedLine> _demoFeed = new();

    List<FeedLine> FeedFor(FeedTab tab) => _demo
        ? _demoFeed.Where(l => l.Tab == tab).OrderByDescending(l => l.At).Take(LiveFeed.Shown).ToList()
        : _watcher.Chat?.Live.Recent(tab) ?? new();

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
        var sig = string.Join("|", lines.Select(l => $"{l.At.Ticks}:{l.Text}:{value(l).Text}")) + (justNow ? "*" : "");
        if (_feedDrawn.TryGetValue(host, out var was) && was.Sig == sig) return;
        bool fresh = lines.Count > 0 && (!_feedDrawn.ContainsKey(host) || was.Newest != lines[0].At) && _feedDrawn.ContainsKey(host);
        _feedDrawn[host] = (sig, lines.Count > 0 ? lines[0].At : default);
        host.Children.Clear();
        empty.Visibility = lines.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        for (int i = 0; i < lines.Count; i++)
        {
            var l = lines[i];
            var row = FeedRow(l, i == 0, i == 0 && justNow, i == LiveFeed.Shown - 1, i > 0, value(l), icon?.Invoke(l) ?? FeedIcon(l));
            row.MouseLeftButtonUp += (_, _) => ShowLineReport(l, tab, row);
            host.Children.Add(row);
            if (i == 0 && fresh)
                row.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(260)));
        }
    }

    /// <summary>One feed line: time, a 22 px tile, what (the name in bold, a faint word after it), and its worth on the right.</summary>
    Border FeedRow(FeedLine l, bool newest, bool justNow, bool oldest, bool rule, (string Text, bool Muted) value, FrameworkElement icon)
    {
        var g = new Grid { Height = 36, Margin = new Thickness(12, 0, 12, 0) };
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(62) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(32) });
        g.ColumnDefinitions.Add(new ColumnDefinition());
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var t = new TextBlock { VerticalAlignment = VerticalAlignment.Center };
        if (justNow) { t.Text = "just now"; t.FontSize = 12; t.SetResourceReference(TextBlock.ForegroundProperty, "Gold"); }
        else
        {
            var local = l.At.ToLocalTime();
            t.Text = local.Date == DateTime.Today ? local.ToString("HH:mm:ss") : local.ToString("MMM d");
            t.FontSize = 11.5; t.SetResourceReference(TextBlock.FontFamilyProperty, "MonoFont"); t.Foreground = new SolidColorBrush(Color.FromRgb(0x5E, 0x65, 0x88));
        }
        g.Children.Add(t);
        icon.VerticalAlignment = VerticalAlignment.Center; icon.HorizontalAlignment = HorizontalAlignment.Left;
        Grid.SetColumn(icon, 1); g.Children.Add(icon);
        var x = new TextBlock { FontSize = 13, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
        x.SetResourceReference(TextBlock.ForegroundProperty, l.Kind == "flag" ? "Gold" : "Ink");
        var (bold, rest, small) = FeedWords(l);
        if (bold.Length > 0) x.Inlines.Add(new Run(bold) { FontWeight = FontWeights.SemiBold });
        if (rest.Length > 0) x.Inlines.Add(new Run(rest));
        if (small.Length > 0) { var s = new Run("  " + small) { FontSize = 12 }; s.SetResourceReference(TextElement.ForegroundProperty, "Faint"); x.Inlines.Add(s); }
        Grid.SetColumn(x, 2); g.Children.Add(x);
        var v = new TextBlock { Text = value.Text, FontSize = 12.5, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 0, 0) };
        v.SetResourceReference(TextBlock.FontFamilyProperty, l.Kind == "flag" ? "BodyFont" : "MonoFont");
        v.SetResourceReference(TextBlock.ForegroundProperty, value.Muted ? "Faint" : "Gold");
        Grid.SetColumn(v, 3); g.Children.Add(v);
        var b = new Border { Child = g, Cursor = Cursors.Hand, Background = Brushes.Transparent, BorderThickness = new Thickness(0, rule ? 1 : 0, 0, 0), BorderBrush = new SolidColorBrush(Color.FromArgb(0x73, 0x2A, 0x31, 0x54)) };
        b.ToolTip = ViaTip(l.Via) is { Length: > 0 } vt ? vt + " Tap if it's wrong." : "Tap if it's wrong.";
        ToolTipService.SetInitialShowDelay(b, 400);
        if (newest)
        {
            // the newest line: a gold tint fading to the right, and a gold rule on its left
            b.Background = new LinearGradientBrush(new GradientStopCollection { new(Color.FromArgb(0x1F, 0xF8, 0xD5, 0x98), 0), new(Color.FromArgb(0x05, 0xF8, 0xD5, 0x98), 0.7) }, 0);
            var edge = new Border { Width = 2, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(-12, 0, 0, 0) };
            edge.SetResourceReference(Border.BackgroundProperty, "Gold");
            g.Children.Add(edge);
        }
        if (oldest) b.Opacity = 0.62;
        var keep = b.Background;
        b.MouseEnter += (_, _) => b.Background = newest ? keep : new SolidColorBrush(Color.FromArgb(0x08, 0xFF, 0xFF, 0xFF));
        b.MouseLeave += (_, _) => b.Background = keep;
        return b;
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
            case "craft": return (l.Name.Length > 0 ? l.Name : "A craft", CraftMakes(l.Name) is int m && m > 1 && l.Ok ? $" ×{m}" : "", l.Ok ? "worked" : "failed");
            case "step": return (l.Name, "", l.Enemy);
            case "flag": return (l.Name, "", "");
            default:
                var small = l.Bound ? "bound" : GatheringCatalog.Find(l.Name) != null ? "gathered" : l.Tab == FeedTab.Events && l.Via.Contains("reward table") ? "reward screen" : "";
                return (l.Name, $" ×{l.Amount:N0}", small);
        }
    }

    static int? CraftMakes(string recipe) => recipe.Length >= 2 ? RecipeBook.ForOutput(recipe)?.OutputQty : null;

    /// <summary>A line's tile: the item's own icon (the tracker's icon cache, as the drops table), a coin for Kinah, a drawn glyph for a run step.</summary>
    FrameworkElement FeedIcon(FeedLine l)
    {
        if (l.Kind == "kinah") return CoinTile(22);
        if (l.Kind == "step") return GlyphTile(22, l.Name.StartsWith("Entered") ? "M5,20 V6 L12,3 L19,6 V20 Z M10,20 V14 H14 V20" : "M12,3 A9,9 0 1 0 12,21 A9,9 0 1 0 12,3 Z M8,12 L11,15 L16,9");
        if (l.Kind == "flag") return GlyphTile(22, "M5,21 V4 M5,4 H17 L14.5,8 L17,12 H5", gold: true);
        var name = l.Name; long id = l.ItemId != 0 ? l.ItemId : _items.IdOf(name);
        return ItemTile(name, id, 22, GradeOf(name));
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
        var letter = new TextBlock { Text = InitialOf(name), FontSize = size * 0.42, FontWeight = FontWeights.SemiBold, Foreground = Ck(CkMuted), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Visibility = slot.Image == null ? Visibility.Visible : Visibility.Collapsed };
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

    /// <summary>A feed figure: "+431", "+7.5K", "+17.6K", "+604K", "+1.27M".</summary>
    static string FeedK(double v)
    {
        double a = Math.Abs(v);
        var s = a < 1000 ? $"{a:N0}" : a < 100_000 ? $"{a / 1e3:0.0}K" : a < 1_000_000 ? $"{a / 1e3:0}K" : $"{a / 1e6:0.00}M";
        return (v < 0 ? "−" : "+") + s;
    }

    /// <summary>A pickup's worth for the feed: Kinah as it is, an item's tradeable copies at the lowest Market price; blank with no price.</summary>
    (string Text, bool Muted) PickupValue(FeedLine l)
    {
        if (l.Kind == "kinah") return (FeedK(l.Amount), false);
        if (l.Kind == "flag") return ("fix below", false);
        if (l.Kind == "step") return (l.Amount > 0 ? FeedK(l.Amount) : "", false);
        if (l.Bound) return ("", true);
        return PriceBook.Price(l.Name) is long p ? (FeedK(p * l.Amount), false) : ("", true);
    }

    /// <summary>A craft's worth for the feed: what it made at the lowest Market price after the Market's cut, less what the try cost; a failed one, minus the try (grey). Blank when a price is missing.</summary>
    (string Text, bool Muted) CraftValue(FeedLine l)
    {
        if (l.Name.Length < 2) return ("", true);
        var cost = RecipeBook.CostPerTry(l.Name, out int unpriced, out int vendor, out _, new HashSet<string>(StringComparer.OrdinalIgnoreCase));
        if (cost == null || unpriced + vendor > 0) return ("", true);
        if (!l.Ok) return (FeedK(-cost.Value), true);
        int makes = CraftMakes(l.Name) ?? 1;
        if (PriceBook.Price(l.Name) is not long p) return ("", true);
        return (FeedK(p * makes * PriceBook.SellerKeeps - cost.Value), false);
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
        HeadRate.Text = total > 0 ? Fk(total / h) : " ";
        HeadRateLbl.Text = total <= 0 ? "Kinah and loot an hour, once something is counted"
            : priced > 0 || loot > 0 ? (_mode == Mode.Gather && kinah == 0 ? "an hour, loot after tax" : "an hour, Kinah and loot after tax")
            : "an hour, Kinah; loot not priced yet";
        // one sample a minute, from the first count to now; the window's own count only (a render draws its sample line)
        if (!_demo)
        {
            if (!ReferenceEquals(_rateOf, e)) { _rateOf = e; _rateLine.Clear(); }
            if (total > 0) _rateLine.Add(DateTime.UtcNow, total / h);
        }
        DrawRateLine(_rateLine, _demo ? "42 min ago" : _rateLine.Samples.Count > 0 ? AgoMinutes(_rateLine.Samples[0].Minute) : "", "now");
        KvLine.Inlines.Clear();
        void Fig(string text) { var r = new Run(text); r.SetResourceReference(TextElement.FontFamilyProperty, "MonoFont"); r.SetResourceReference(TextElement.ForegroundProperty, "Ink"); KvLine.Inlines.Add(r); }
        if (_mode == Mode.Gather) { Fig($"{nodes:N0}"); KvLine.Inlines.Add(new Run(" attempts · ")); Fig(itemsPerTry > 0 ? itemsPerTry.ToString("0.0") : "0"); KvLine.Inlines.Add(new Run(" a try")); }
        else if (kills > 0) { Fig($"{kills:N0}"); KvLine.Inlines.Add(new Run($" kill{(kills == 1 ? "" : "s")} · ")); Fig(Fk(kinah / (double)kills)); KvLine.Inlines.Add(new Run(" a kill")); }
        var best = _demo ? new BestRuns.Best(1_400_000, 48, "2026-10-19") : BestRuns.For(Server?.Label ?? "", _watcher.Chat?.ZoneView?.Zone ?? e.Zone);
        if (best != null) { if (KvLine.Inlines.Count > 0) KvLine.Inlines.Add(new LineBreak()); KvLine.Inlines.Add(new Run("best here ")); Fig(Fk(best.PerHr)); }
        KvLine.Visibility = KvLine.Inlines.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        FillHealth(HealthText, HealthMark);
    }

    static string AgoMinutes(DateTime minuteUtc)
    {
        int m = (int)Math.Round((DateTime.UtcNow - minuteUtc).TotalMinutes);
        return m < 1 ? "a minute ago" : m < 60 ? $"{m} min ago" : $"{m / 60} h {m % 60} min ago";
    }

    void DrawRateLine(RateLine line, string from, string to)
    {
        var pts = line.Points(200, 40);
        bool show = pts.Count >= 2;
        RateCanvas.Visibility = RateAxis.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        if (!show) return;
        RateLinePath.Points = new PointCollection(pts.Select(p => new Point(p.X, p.Y)));
        var fig = new PathFigure { StartPoint = new Point(0, 40), IsClosed = true };
        foreach (var p in pts) fig.Segments.Add(new LineSegment(new Point(p.X, p.Y), false));
        fig.Segments.Add(new LineSegment(new Point(200, 40), false));
        RateFill.Data = new PathGeometry(new[] { fig });
        RateFrom.Text = from; RateTo.Text = to;
    }

    /// <summary>"Reading the chat · 3,620 reads", the one place the read count belongs; paused or a read problem says so instead.</summary>
    void FillHealth(TextBlock text, Path? mark)
    {
        var c = _watcher.Chat;
        string t; bool ok = true;
        if (_demo) t = "Reading the chat · 3,620 reads";
        else if (c == null) { t = _watcher.GameRect == null ? "Waiting for the game" : "Looking for the chat"; ok = false; }
        else if (c.LastError.Length > 0) { t = "Read problem: " + c.LastError; ok = false; }
        else if (c.Paused.Length > 0) { t = "Paused: the chat is out of view"; ok = false; }
        else if (!c.ChatFound) { t = "Reading the game · the chat not seen yet"; ok = false; }
        else t = $"Reading the chat · {c.Reads:N0} reads";
        text.Text = t;
        text.ToolTip = _demo || c == null ? null : $"{MarketShort()}.\n{BalanceShort()}." + (_watcher.Flushed > 0 ? $"\n{_watcher.Flushed} batch{(_watcher.Flushed == 1 ? "" : "es")} shared with the site." : "");
        if (mark != null)
        {
            mark.Data = Geometry.Parse(ok ? "M3.5,8.5 L6.5,11.5 L12.5,4.5" : "M8,3 V9.5 M8,12.5 V13");
            mark.SetResourceReference(Shape.StrokeProperty, ok ? "Gold" : "Warn");
        }
    }

    /// <summary>The Farming feed: pickups with their worth, Kinah with the enemy it was credited to, gathers.</summary>
    void FillLive()
    {
        DrawFeed(FeedHost, FeedEmpty, FeedFor(FeedTab.Farming), FeedTab.Farming, PickupValue);
        FeedHint.Visibility = Visibility.Visible;
    }

    /* ---------------- Session and Where to farm now ---------------- */

    /// <summary>Session, opened: Kinah, loot after the Market's cut, your best hour here, and what players at your gear make here.</summary>
    void FillSessionRows(long kinah, long loot, int priced, string? zone)
    {
        SessionRows.Children.Clear();
        void Row(string left, string right, bool strong = false)
        {
            var g = new Grid { Height = 36 };
            g.ColumnDefinitions.Add(new ColumnDefinition()); g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var l = new TextBlock { Text = left, FontSize = 12.5, VerticalAlignment = VerticalAlignment.Center }; l.SetResourceReference(TextBlock.ForegroundProperty, "Muted"); g.Children.Add(l);
            var r = new TextBlock { Text = right, FontSize = 13, VerticalAlignment = VerticalAlignment.Center };
            r.SetResourceReference(TextBlock.FontFamilyProperty, "MonoFont"); r.SetResourceReference(TextBlock.ForegroundProperty, strong ? "Ink" : "Muted");
            Grid.SetColumn(r, 1); g.Children.Add(r);
            var b = new Border { Child = g, BorderThickness = new Thickness(0, 1, 0, 0) }; b.SetResourceReference(Border.BorderBrushProperty, "Line");
            SessionRows.Children.Add(b);
        }
        if (kinah > 0 || _mode != Mode.Gather) Row("Kinah picked up", Fk(kinah), true);
        if (priced > 0) Row($"Loot, after the Market's {1 - PriceBook.SellerKeeps:P0}", Fk(loot), true);
        var best = _demo ? new BestRuns.Best(1_400_000, 48, "2026-10-19") : BestRuns.For(Server?.Label ?? "", zone);
        if (best != null) Row("Your best here, an hour", Fk(best.PerHr));
        if (PlayerBoards.Here(GearBracket, zone) is { } h && h.Row.ValueHr is long v) Row(h.AtGear ? "Players at your gear, an hour" : "Players here, an hour", Fk(v));
    }

    /// <summary>Where to farm now: folded, the best zone and its hour; opened, the best zones for the player's gear on the server.</summary>
    void FillWhere(string? zone)
    {
        WhereBody.Children.Clear();
        if (PlayerBoards.FarmAt == null) { WhereSection.Visibility = Visibility.Collapsed; return; }
        WhereSection.Visibility = _part == "Farming" && !_welcome ? Visibility.Visible : Visibility.Collapsed;
        var (zones, atGear) = PlayerBoards.Top(GearBracket, 5);
        bool open = SecOpen("Where");
        if (zones.Count == 0)
        {
            WhereNote.Text = "not enough runs on your server yet"; WhereSum.Text = "";
            var f = Faint($"A zone shows once {PlayerBoards.MinRuns} runs from {PlayerBoards.MinPlayers} players are in. Yours count."); f.Margin = new Thickness(0, 4, 0, 0);
            WhereBody.Children.Add(f);
            return;
        }
        var server = Server?.Name ?? "your server";
        WhereNote.Text = open ? (atGear ? $"gear {GearBracket!.Replace("-", "–")} · {server}" : $"all players · {server}") : $"best: {zones[0].Zone}";
        WhereSum.Text = open ? "" : Fk(zones[0].ValueHr ?? 0);
        foreach (var z in zones)
        {
            var g = new Grid { Height = 36 };
            g.ColumnDefinitions.Add(new ColumnDefinition()); g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(80) });
            var name = new TextBlock { FontSize = 13, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
            name.SetResourceReference(TextBlock.ForegroundProperty, "Ink");
            name.Inlines.Add(new Run(z.Zone));
            if (zone != null && string.Equals(zone, z.Zone, StringComparison.OrdinalIgnoreCase)) { var here = new Run("  you are here") { FontSize = 11.5, FontWeight = FontWeights.Normal }; here.SetResourceReference(TextElement.ForegroundProperty, "Gold"); name.Inlines.Add(here); }
            g.Children.Add(name);
            var who = new TextBlock { Text = $"{z.Runs} runs, {z.Players} players", FontSize = 12, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 0) };
            who.SetResourceReference(TextBlock.ForegroundProperty, "Faint");
            Grid.SetColumn(who, 1); g.Children.Add(who);
            var v = new TextBlock { Text = Fk(z.ValueHr ?? 0), FontSize = 13, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Right };
            v.SetResourceReference(TextBlock.FontFamilyProperty, "MonoFont"); v.SetResourceReference(TextBlock.ForegroundProperty, "Gold");
            Grid.SetColumn(v, 2); g.Children.Add(v);
            var b = new Border { Child = g, BorderThickness = new Thickness(0, 1, 0, 0), ToolTip = $"{z.Runs} runs from {z.Players} players in the last 7 days · Kinah plus loot after tax, an hour" };
            b.SetResourceReference(Border.BorderBrushProperty, "Line");
            WhereBody.Children.Add(b);
        }
    }

    /* ---------------- Also this session ---------------- */

    /// <summary>The chip open in place ("Gather", "skill:Mining", "Pet", "Mob"), or null.</summary>
    string? _extraOpen;

    /// <summary>
    /// The chips under the drops, one per thing counted besides them (Gathered, each gathering skill, Soul Crystals, Kinah by
    /// enemy), each opening its own small table in place under a gold rule. Only one is open at a time.
    /// </summary>
    void FillExtras(List<DropRow> gathered, List<SkillBox> skills, List<DropRow> pets, List<DropRow> mobs, long kinah)
    {
        ExtraChips.Children.Clear();
        var keys = new List<string>();
        void Chip(string key, FrameworkElement icon, string label, string figure)
        {
            keys.Add(key);
            bool on = _extraOpen == key;
            var sp = new StackPanel { Orientation = Orientation.Horizontal };
            icon.Margin = new Thickness(0, 0, 7, 0); icon.VerticalAlignment = VerticalAlignment.Center;
            sp.Children.Add(icon);
            var t = new TextBlock { FontSize = 12.5, VerticalAlignment = VerticalAlignment.Center }; t.SetResourceReference(TextBlock.ForegroundProperty, "Ink");
            t.Inlines.Add(new Run(label));
            if (figure.Length > 0) { var f = new Run("  " + figure); f.SetResourceReference(TextElement.ForegroundProperty, on ? "Gold" : "Muted"); t.Inlines.Add(f); }
            sp.Children.Add(t);
            var b = new Border
            {
                Child = sp, Height = 34, CornerRadius = new CornerRadius(17), Padding = new Thickness(5, 0, 12, 0), Margin = new Thickness(0, 0, 8, 8), BorderThickness = new Thickness(1), Cursor = Cursors.Hand,
                Background = on ? new SolidColorBrush(Color.FromArgb(0x14, 0xF8, 0xD5, 0x98)) : new SolidColorBrush(Color.FromArgb(0xB3, 0x0B, 0x0E, 0x1E)),
                BorderBrush = on ? new SolidColorBrush(Color.FromArgb(0x99, 0xF8, 0xD5, 0x98)) : new SolidColorBrush(Color.FromRgb(0x2A, 0x31, 0x54)),
            };
            b.MouseLeftButtonUp += (_, _) => { _extraOpen = _extraOpen == key ? null : key; Redraw(); };
            ExtraChips.Children.Add(b);
        }
        if (gathered.Count > 0) Chip("Gather", ItemTile(gathered[0].Name, _items.IdOf(gathered[0].Name), 24, gathered[0].Grade, round: true), "Gathered", $"{gathered.Count} kind{(gathered.Count == 1 ? "" : "s")}");
        foreach (var sk in skills)
        {
            var first = gathered.FirstOrDefault(g => SkillOf(g.Name) == sk.Name);
            Chip("skill:" + sk.Name, first != null ? ItemTile(first.Name, _items.IdOf(first.Name), 24, first.Grade, round: true) : GlyphTile(24, "M4,20 L14,10 M12,4 L20,12 L17,15 L9,7 Z", round: true), sk.Name.Split(" · ")[0], sk.Value > 0 ? Fk(sk.Value) : $"{sk.Attempts:N0} tries");
        }
        int crystals = pets.Sum(r => int.TryParse(r.Drops, System.Globalization.NumberStyles.AllowThousands, null, out var n) ? n : 0);
        if (pets.Count > 0) Chip("Pet", GlyphTile(24, "M12,2 L18,9 L12,22 L6,9 Z M6,9 H18", round: true, tint: Color.FromRgb(0x3E, 0x9E, 0x62)), "Soul Crystals", crystals.ToString("N0"));
        if (mobs.Count > 0) Chip("Mob", CoinTile(24, round: true), "Kinah by enemy", _lastMobKinah > 0 ? Fk(_lastMobKinah) : "");
        if (_extraOpen != null && !keys.Contains(_extraOpen)) _extraOpen = null;
        ExtrasSection.Visibility = keys.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

        // the open chip's table
        GatherRows.ItemsSource = null; MobRows.ItemsSource = null; PetRows.ItemsSource = null; SkillLines.Children.Clear();
        GatherRows.Visibility = MobRows.Visibility = PetRows.Visibility = Visibility.Collapsed;
        InPlaceLead.Visibility = InPlaceNote.Visibility = InPlaceMore.Visibility = MobCredit.Visibility = Visibility.Collapsed;
        InPlace.Visibility = _extraOpen != null ? Visibility.Visible : Visibility.Collapsed;
        if (_extraOpen == null) return;
        var more = "x:" + _extraOpen;
        List<DropRow> Top(List<DropRow> rows) => TopRows(more, rows, TableRows, InPlaceMore, InPlaceMoreRun);
        void Heads(string a, string b, string c) { InPlaceH1.Text = a; InPlaceH2.Text = b; InPlaceH3.Text = c; }
        switch (_extraOpen)
        {
            case "Gather":
                Heads("Qty", "Per try", "Value");
                GatherRows.ItemsSource = Top(gathered.Select(r => r with { Sub = r.Reported ? "reported" : "" }).ToList()); GatherRows.Visibility = Visibility.Visible;
                if (gathered.Any(r => r.Value.Length == 0)) { InPlaceNote.Text = $"{gathered.Count(r => r.Value.Length == 0)} have no price yet."; InPlaceNote.Visibility = Visibility.Visible; }
                break;
            case var s when s.StartsWith("skill:"):
            {
                var sk = skills.First(x => "skill:" + x.Name == s);
                int ok = sk.Attempts - sk.Failed;
                Heads("Qty", "Per try", "Value");
                InPlaceLead.Text = $"{sk.Attempts:N0} tr{(sk.Attempts == 1 ? "y" : "ies")}, {ok:N0} worked · {Whole(sk.PerHour)} an hour" + (ok > 0 && sk.Plentiful > 0 ? $" · {Math.Round(100.0 * sk.Plentiful / ok)}% plentiful" : "");
                InPlaceLead.Visibility = Visibility.Visible;
                GatherRows.ItemsSource = Top(gathered.Where(g => SkillOf(g.Name) == sk.Name).Select(r => r with { Sub = "" }).ToList()); GatherRows.Visibility = Visibility.Visible;
                break;
            }
            case "Pet":
                Heads("Qty", "An hour", "");
                PetRows.ItemsSource = Top(pets.Select(r => r with { DropPct = r.PerHour, Sub = "" }).ToList()); PetRows.Visibility = Visibility.Visible;
                break;
            case "Mob":
            {
                Heads("Kills", "A kill", "Kinah");
                MobRows.ItemsSource = Top(mobs.Select(r => r.Child ? r with { Sub = r.Sub } : r with { Sub = "" }).ToList()); MobRows.Visibility = Visibility.Visible;
                long none = kinah - _lastMobKinah;
                if (none > 0) { InPlaceNote.Text = $"{Fk(none)} of the session's Kinah came with no enemy named."; InPlaceNote.Visibility = Visibility.Visible; }
                MobCredit.Visibility = Visibility.Visible;
                break;
            }
        }
    }

    string SkillOf(string item) => GatheringCatalog.Find(item) is { } g ? GatherSkills.Label(g.Skill) : "";

    /// <summary>A rate as a whole number ("58", "10"; under one, "under 1").</summary>
    static string Whole(double v) => v <= 0 ? "0" : v < 0.5 ? "under 1" : Math.Round(v).ToString("N0");

    /* ---------------- tap a line: the miscount report ---------------- */

    /// <summary>
    /// A tapped feed line (or the hint, for a pickup that never showed): the line as it was, three choices (Wrong item, Wrong
    /// amount, Didn't happen) and "Missing a pickup" at the bottom, filed through the miscount report with the line marked.
    /// </summary>
    void ShowLineReport(FeedLine? line, FeedTab tab, FrameworkElement at)
    {
        if (_card != null) _card.IsOpen = false;
        _card = new Popup { Child = BuildLineReport(line, tab, line == null ? "missing" : "wrong_amount"), StaysOpen = false, AllowsTransparency = true, PopupAnimation = PopupAnimation.Fade, Placement = PlacementMode.Mouse, PlacementTarget = at };
        _card.IsOpen = true;
    }

    internal Border BuildLineReport(FeedLine? line, FeedTab tab, string label, string note = "")
    {
        var (card, body) = CkShell(420);
        var head = CkSection(line == null || label == "missing" ? "A PICKUP THAT NEVER SHOWED" : "REPORT THIS LINE"); body.Children.Add(head);
        if (line != null)
        {
            var g = new Grid();
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); g.ColumnDefinitions.Add(new ColumnDefinition()); g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            g.Children.Add(CkText(line.At.ToLocalTime().ToString("HH:mm:ss"), CkFaint, 12, mono: true));
            var what = CkText(line.Text, CkInk, 13, FontWeights.SemiBold); what.Margin = new Thickness(12, 0, 12, 0); Grid.SetColumn(what, 1); g.Children.Add(what);
            var val = tab == FeedTab.Crafting ? CraftValue(line) : PickupValue(line);
            var v = CkText(val.Text, val.Muted ? CkFaint : CkGold, 12.5, mono: true); Grid.SetColumn(v, 2); g.Children.Add(v);
            body.Children.Add(CkBox(g, new Thickness(20, 0, 20, 0)));
            if (ViaTip(line.Via) is { Length: > 0 } how) { var n = CkNote(how); n.Margin = new Thickness(20, 6, 20, 0); body.Children.Add(n); }
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
                    Child = CkText(text, on ? CkGold : CkMuted, 12.5), Padding = new Thickness(12, 5, 12, 6), Margin = new Thickness(0, 0, 8, 8), CornerRadius = new CornerRadius(14), BorderThickness = new Thickness(1), Cursor = Cursors.Hand,
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
        var save = new Button { Content = "Save report", Height = 30, Padding = new Thickness(16, 0, 16, 0), FontSize = 12.5 };
        save.SetResourceReference(StyleProperty, "PrimaryButton");
        save.Click += (_, _) =>
        {
            if (_card != null) _card.IsOpen = false;
            FileLineReport(new MiscountLine(chosen, chosen == "missing" ? null : line), missing?.Text.Trim() ?? "");
        };
        acts.Children.Add(save);
        var cancel = new Button { Content = "Cancel", Height = 30, Padding = new Thickness(14, 0, 14, 0), FontSize = 12.5, Margin = new Thickness(8, 0, 0, 0) };
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
        var c = _watcher.Chat;
        if (c == null) { Say("Nothing to report yet: the last minute is kept once the tracker is reading the game.", "Warn"); return; }
        try
        {
            var why = m.Why + (note.Length > 0 ? $" · {note}" : "");
            var dir = c.WriteReport(why);
            var frames = System.IO.Path.Combine(dir, "frames.jsonl");
            File.WriteAllText(System.IO.Path.Combine(dir, "line.json"), m.Json(File.Exists(frames) ? File.ReadAllLines(frames) : Array.Empty<string>()));
            CrashLog.Note($"feed line reported: {why}");
            Say("Report saved with the line marked, in your reports folder. Thanks: it becomes a test the tracker must pass.", "Good");
            Status.ToolTip = dir;
            _ = SendReportPictures(dir, why);
        }
        catch (Exception ex) { CrashLog.Write("line report", ex); Say("Couldn't save the report: " + ex.Message, "Bad"); }
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
    }

    Dictionary<string, string> HudFeedValues(FeedLine? l, string flag)
    {
        var d = new Dictionary<string, string> { ["flag"] = flag, ["feedText"] = "", ["feedValue"] = "", ["feedRate"] = "", ["feedUnit"] = "" };
        if (l == null) { d["feedRate"] = _hudValues.GetValueOrDefault("rate", ""); d["feedUnit"] = "an hour"; return d; }
        var (bold, plain, small) = FeedWords(l);
        d["feedText"] = l.Kind == "craft" ? $"{bold}{plain} {small}" : l.Kind == "kinah" ? bold : $"{bold}{(l.Amount > 1 ? plain : "")}";
        d["feedValue"] = (l.Tab == FeedTab.Crafting ? CraftValue(l) : PickupValue(l)).Text;
        (d["feedRate"], d["feedUnit"]) = l.Tab switch
        {
            FeedTab.Crafting => (_craftNet, "today"),
            FeedTab.Events => (_eventsRate, "an hour"),
            _ => (_hudValues.GetValueOrDefault("rate", ""), "an hour"),
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
        return new() { ("ui-hud-farming.png", farm), ("ui-hud-crafting.png", craft), ("ui-hud-flag.png", flag) };
    }

    /* ---------------- layout renders ---------------- */

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
            new(t.AddSeconds(-18), FeedTab.Farming, "item", "Thick hide", 1, Via: "chat", Where: "Farming · Western Cantas Valley", ItemId: Id("Thick hide")),
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
