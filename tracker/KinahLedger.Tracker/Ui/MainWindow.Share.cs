using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using KinahLedger.Data;
using KinahLedger.Session;
using Path = System.Windows.Shapes.Path;

namespace KinahLedger.Ui;

/// <summary>
/// Share (0.9.3, ShareA of the canvas, Steven's pick 2026-10-31). The Farming card's Share button (it was "Copy for
/// Discord") opens a sheet inside the card, under the actions: a dark panel with a gold rule, SHARE, a This session |
/// Last run | This week switch and a close ✕; a live preview of the 1200×630 picture at half size (the mark, the day and
/// server, the zone or run, the time and kills, Kinah an hour and what it made after the Market's cut, the top five drops
/// with the best one named, kinahledger.com); "Show my character name", off until the player turns it on; then Copy
/// picture, Save picture and Copy as text, and "Paste it in Discord or anywhere. Nothing is uploaded." The Events page's
/// Share opens the same sheet on its last run. Every action is a visible button: no right-click, no Windows menu, no
/// window of its own (Steven, 2026-10-31). The PNG is the preview's own visual drawn again with RenderTargetBitmap at twice
/// its size, never a screenshot. No accuracy figures and no "to look at" counts. Nothing leaves the PC.
/// </summary>
public partial class MainWindow
{
    /// <summary>The sheet's side: This session, Last run or This week.</summary>
    string _shareScope = SharePicture.Session;
    /// <summary>The page the sheet is open on ("Farming" or "Events"), or null while it is closed.</summary>
    string? _shareOn;
    /// <summary>This session's picture, from the last FillNumbers: the same figures the card shows.</summary>
    ShareFacts? _shareSession;
    /// <summary>The last farming run stopped since the tracker started, and when (UTC), for Last run.</summary>
    (ShareFacts Facts, DateTime At)? _shareFarmRun;
    /// <summary>Where Save picture last put a picture, said under the buttons.</summary>
    string _shareSaved = "";
    /// <summary>What the preview shows now, so a tick redraws it only when that changed.</summary>
    string _shareSig = "";
    Border? _sharePreview;
    int _shareTicks;

    static readonly Color ShareSheetBg = Color.FromRgb(0x15, 0x1A, 0x33);
    const double PicW = SharePicture.Width / 2.0, PicH = SharePicture.Height / 2.0;

    void InitShare()
    {
        ShareCardBtn.Click += (_, _) => ToggleShare("Farming");
        EventsShareBtn.Click += (_, _) => ToggleShare("Events");
    }

    void ToggleShare(string page)
    {
        if (_shareOn == page) { CloseShare(); return; }
        _shareOn = page;
        // Events shares its last run; Farming opens on This session
        _shareScope = page == "Events" ? SharePicture.Run : SharePicture.Session;
        _shareSaved = "";
        DrawShare();
    }

    void CloseShare()
    {
        _shareOn = null; _shareSig = ""; _sharePreview = null;
        ShareHost.Child = null; ShareHost.Visibility = Visibility.Collapsed;
        EventsShareHost.Child = null; EventsShareHost.Visibility = Visibility.Collapsed;
    }

    /// <summary>The sheet drawn again in its page's place (a switch, the name switch, a save).</summary>
    void DrawShare()
    {
        if (_shareOn == null) return;
        var host = _shareOn == "Events" ? EventsShareHost : ShareHost;
        var other = _shareOn == "Events" ? ShareHost : EventsShareHost;
        other.Child = null; other.Visibility = Visibility.Collapsed;
        host.Child = BuildShareSheet();
        host.Visibility = Visibility.Visible;
    }

    /// <summary>From the tick, about once a second: the preview follows the count while the sheet is open.</summary>
    void ShareTick()
    {
        if (_shareOn == null || ++_shareTicks % 2 != 0) return;
        try { RefreshSharePreview(); } catch (Exception ex) { CrashLog.Write("share", ex); }
    }

    void RefreshSharePreview()
    {
        if (_shareOn == null || _sharePreview == null) return;
        var pic = CurrentSharePic();
        if (ShareSig(pic) == _shareSig) return;
        // something to show now where there was nothing (or the other way): the buttons change too
        if ((pic == null) != (_shareSig.Length == 0 || _shareSig == "none")) { DrawShare(); return; }
        _sharePreview.Child = SharePreview(pic);
    }

    static string ShareSig(SharePic? p) => p == null ? "none" : SharePicture.Text(p) + "|" + string.Join(",", p.Icons.Select(i => i.Name));

    /* ---------------- the facts for each side ---------------- */

    ShareFacts? ShareFactsFor(string scope) => scope switch
    {
        SharePicture.Run => LastRunFacts(),
        SharePicture.Week => WeekShareFacts(),
        _ => _shareSession is { Made: > 0 } s ? s : null,
    };

    SharePic? CurrentSharePic() => ShareFactsFor(_shareScope) is { } f ? SharePicture.Compose(f, _prefs.ShareShowName, _prefs.ShareName, Fk) : null;

    /// <summary>"Siel · NA East", or nothing before a server is chosen.</summary>
    string ShareServer() => Server is { } sv ? $"{sv.Name} · {sv.Region.Name}" : "";

    /// <summary>The items a count picked up, each with its grade and its worth at the lowest Market price (0 with no price).</summary>
    List<ShareDrop> ShareDropsOf(SessionEngine e) => e.ItemRows()
        .Where(r => r.Count + r.Bound > 0)
        .Select(r => new ShareDrop(r.Name, GatheringCatalog.Find(r.Name)?.Grade ?? _items.Get(r.Id)?.Grade ?? "", r.Count + r.Bound,
            r.Count > 0 && PriceBook.Price(r.Name) is long p ? p * r.Count : 0))
        .ToList();

    /// <summary>The zone a count is named by: its own, else the one on screen.</summary>
    string ShareZone(SessionEngine e) =>
        e.Zone is { Length: > 0 } z && z != SessionEngine.UnknownZone ? z : _watcher.Chat?.ZoneView?.Zone ?? _demoZone ?? "Aion 2";

    /// <summary>A farming count as a picture: this session (from FillNumbers, with its total) or a run just stopped.</summary>
    ShareFacts FarmShareFacts(SessionEngine e, string scope, double seconds, long made, DateTime day)
    {
        var t = e.Totals();
        bool gather = t.Kills == 0 && t.Nodes > 0;
        string what = scope == SharePicture.Run ? (gather ? "Gathering run" : "Farming run") : gather ? "of gathering" : "of farming";
        return new ShareFacts(scope, day, ShareServer(), ShareZone(e), what, seconds, gather ? t.Nodes : t.Kills, gather ? "attempt" : "kill", made, ShareDropsOf(e));
    }

    /// <summary>The last expedition or event finished (the Events page's, a pending Clear's runs left out).</summary>
    InstanceRun? LastEventRun()
    {
        var log = _watcher.Chat?.Instances ?? _demoInst;
        if (log == null) return null;
        return log.Snapshot().Where(r => r.LastLoot != null && !_hiddenRuns.Contains(RunKey(r.Start, r.Zone))).OrderByDescending(r => r.LastLoot).FirstOrDefault();
    }

    ShareFacts EventShareFacts(InstanceRun r)
    {
        var drops = new Dictionary<long, (int N, long Worth)>();
        foreach (var (id, n) in r.Items)
        {
            long worth = !_items.IsGear(id) && PriceBook.Price(_items.NameOf(id)) is long p ? p * n : 0;
            drops[id] = (n, worth);
        }
        foreach (var (id, n) in r.BoundItems) { var had = drops.GetValueOrDefault(id); drops[id] = (had.N + n, had.Worth); }
        var list = drops.Select(kv => new ShareDrop(_items.NameOf(kv.Key), _items.Get(kv.Key)?.Grade ?? "", kv.Value.N, kv.Value.Worth)).ToList();
        double secs = ((r.LastLoot ?? r.Start) - r.Start).TotalSeconds;
        var kind = DayHistory.KindOf(r.Kind) is { Length: > 0 } k ? k : r.Kind.Length > 0 ? r.Kind : "Run";
        return new ShareFacts(SharePicture.Run, r.Start.ToLocalTime(), ShareServer(), r.Label, kind, secs, r.Kills, "kill", RunValue(r), list);
    }

    /// <summary>Last run: the newer of the last farming run stopped and the last expedition or event.</summary>
    ShareFacts? LastRunFacts()
    {
        var ev = LastEventRun();
        if (_shareFarmRun is { } fr && (ev == null || fr.At >= ev.LastLoot)) return fr.Facts;
        return ev != null ? EventShareFacts(ev) : null;
    }

    /// <summary>This week: the last seven days of farming (LOOT's Week side), with the best day and the zone that made most.</summary>
    ShareFacts? WeekShareFacts()
    {
        var days = FarmWeekDays().Where(d => !d.BeforeHistory).ToList();
        var t = DayHistory.Totals(days);
        if (t.Made <= 0 && t.Active < 60) return null;
        var best = days.Where(d => d.Farm.Made > 0).OrderByDescending(d => d.Farm.Made).FirstOrDefault();
        var zone = days.SelectMany(d => d.Farm.Zones).GroupBy(z => z.Zone).Select(g => (Zone: g.Key, Made: g.Sum(z => z.Made))).OrderByDescending(x => x.Made).FirstOrDefault();
        var note = SharePicture.WeekNote(best?.Date, best?.Farm.Made ?? 0, zone.Made > 0 ? zone.Zone : null, Fk);
        return new ShareFacts(SharePicture.Week, DateTime.Now, ShareServer(), "This week", "of farming", t.Active, t.Kills, "kill", t.Made, new List<ShareDrop>(), note);
    }

    /// <summary>From FillNumbers: this session's picture, the same zone, time and total the card shows.</summary>
    void SetShareSession(SessionEngine e, TimeSpan elapsed, long totalNow) =>
        _shareSession = FarmShareFacts(e, SharePicture.Session, elapsed.TotalSeconds, totalNow, DateTime.Now);

    /// <summary>From StopTracking: the run just stopped, for Last run.</summary>
    void KeepShareRun(SessionEngine e)
    {
        try { _shareFarmRun = (FarmShareFacts(e, SharePicture.Run, e.Active.TotalSeconds, BestRuns.Value(e), DateTime.Now), DateTime.UtcNow); }
        catch (Exception ex) { CrashLog.Write("share run", ex); }
    }

    /* ---------------- the sheet ---------------- */

    Border BuildShareSheet()
    {
        var body = new StackPanel();
        var sheet = new Border
        {
            Child = body, CornerRadius = new CornerRadius(8), BorderThickness = new Thickness(1), BorderBrush = Ck(CkGold, 0x59), Background = Ck(ShareSheetBg),
            Padding = new Thickness(14, 12, 14, 14),
        };
        // drawn dark in every look, like the Set a goal sheet: its buttons and switch take Night's brushes
        sheet.Resources.MergedDictionaries.Add(Skins.Night());
        AutomationProperties.SetName(sheet, "Share");
        sheet.PreviewKeyDown += (_, e) => { if (e.Key == Key.Escape) { e.Handled = true; CloseShare(); } };

        // SHARE, a gold rule fading out, the switch, ✕
        var head = new Grid { Margin = new Thickness(0, 0, 0, 12) };
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        head.ColumnDefinitions.Add(new ColumnDefinition());
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var label = CkText("SHARE", CkGoldRule, 11, FontWeights.Bold); label.Margin = new Thickness(2, 0, 10, 0);
        head.Children.Add(label);
        var rule = new Border { Height = 1, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 12, 0), Background = new LinearGradientBrush(Color.FromArgb(0x66, CkGoldRule.R, CkGoldRule.G, CkGoldRule.B), Colors.Transparent, 0) };
        Grid.SetColumn(rule, 1); head.Children.Add(rule);
        var seg = new StackPanel { Orientation = Orientation.Horizontal };
        foreach (var (scope, text, tip) in new[]
        {
            (SharePicture.Session, "This session", "This zone since you came, as the card shows it."),
            (SharePicture.Run, "Last run", "The last run you stopped, or the last expedition or event."),
            (SharePicture.Week, "This week", "The last 7 days of farming on this PC."),
        })
        {
            var b = new Button { Content = text, Height = 26, FontSize = 12.5, Padding = new Thickness(10, 0, 10, 0), Margin = new Thickness(seg.Children.Count > 0 ? 2 : 0, 0, 0, 0), ToolTip = tip, Tag = "share-" + scope };
            b.SetResourceReference(StyleProperty, _shareScope == scope ? "SegOn" : "SegOff");
            var s = scope;
            b.Click += (_, _) => { if (_shareScope == s) return; _shareScope = s; _shareSaved = ""; DrawShare(); };
            seg.Children.Add(b);
        }
        var segBox = new Border { Child = seg, Padding = new Thickness(3), CornerRadius = new CornerRadius(12), BorderThickness = new Thickness(1), BorderBrush = Ck(CkSlotLine), Background = Ck(CkSlot, 0xB3), VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(segBox, 2); head.Children.Add(segBox);
        var close = new Button
        {
            Content = "✕", Width = 28, Height = 26, Padding = new Thickness(0), Margin = new Thickness(8, 0, 0, 0), FontSize = 14, FontWeight = FontWeights.Normal,
            Background = Brushes.Transparent, BorderBrush = Brushes.Transparent, Foreground = Ck(CkFaint), ToolTip = "Close", VerticalAlignment = VerticalAlignment.Center,
        };
        AutomationProperties.SetName(close, "Close Share");
        close.Click += (_, _) => CloseShare();
        Grid.SetColumn(close, 3); head.Children.Add(close);
        body.Children.Add(head);

        // the preview: the picture itself at half size, live
        var pic = CurrentSharePic();
        _shareSig = ShareSig(pic);
        _sharePreview = new Border { HorizontalAlignment = HorizontalAlignment.Center, Child = SharePreview(pic) };
        body.Children.Add(_sharePreview);

        // Show my character name: off by default and remembered; on, the name as typed here
        var nameRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 14, 0, 0) };
        var sw = new CheckBox { IsChecked = _prefs.ShareShowName, VerticalAlignment = VerticalAlignment.Center };
        sw.SetResourceReference(StyleProperty, "Switch");
        AutomationProperties.SetName(sw, "Show my character name");
        nameRow.Children.Add(sw);
        var swText = CkText("Show my character name", CkMuted, 12.5); swText.Margin = new Thickness(10, 0, 0, 0); swText.Cursor = Cursors.Hand;
        swText.MouseLeftButtonUp += (_, _) => sw.IsChecked = sw.IsChecked != true;
        nameRow.Children.Add(swText);
        void SetShow(bool on) { if (_prefs.ShareShowName == on) return; _prefs.ShareShowName = on; if (!_demo) _prefs.Save(); DrawShare(); }
        sw.Checked += (_, _) => SetShow(true);
        sw.Unchecked += (_, _) => SetShow(false);
        if (_prefs.ShareShowName)
        {
            var box = new TextBox
            {
                Width = 190, Height = 28, FontSize = 12.5, Padding = new Thickness(8, 0, 8, 0), Margin = new Thickness(14, 0, 0, 0), MaxLength = 24, Text = _prefs.ShareName,
                VerticalContentAlignment = VerticalAlignment.Center, Background = Ck(CkSlot), BorderBrush = Ck(CkFrame), Foreground = Ck(CkInk), CaretBrush = Ck(CkGold),
            };
            AutomationProperties.SetName(box, "Character name");
            var hint = CkText("Your character's name", CkFaint, 12.5); hint.Margin = new Thickness(24, 0, 0, 0); hint.IsHitTestVisible = false;
            hint.Visibility = box.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
            box.TextChanged += (_, _) =>
            {
                hint.Visibility = box.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
                _prefs.ShareName = box.Text.Trim(); if (!_demo) _prefs.Save();
                var p = CurrentSharePic(); _shareSig = ShareSig(p);
                if (_sharePreview != null) _sharePreview.Child = SharePreview(p);
            };
            var g = new Grid(); g.Children.Add(box); g.Children.Add(hint);
            nameRow.Children.Add(g);
        }
        body.Children.Add(nameRow);

        // Copy picture, Save picture, Copy as text
        var acts = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 14, 0, 0) };
        Button Act(string text, string tip, Action go, bool primary = false)
        {
            var b = new Button { Content = text, Height = 32, FontSize = 12.5, Padding = new Thickness(16, 0, 16, 0), Margin = new Thickness(acts.Children.Count > 0 ? 10 : 0, 0, 0, 0), ToolTip = tip, IsEnabled = pic != null };
            if (primary) b.SetResourceReference(StyleProperty, "PrimaryButton");
            AutomationProperties.SetName(b, text);
            b.Click += (_, _) => go();
            acts.Children.Add(b);
            return b;
        }
        Act("Copy picture", "Puts the picture on the clipboard. Paste it with Ctrl+V.", CopySharePicture, primary: true);
        Act("Save picture", @"Saves the picture as a PNG in Pictures\Kinah Ledger.", SaveSharePicture);
        Act("Copy as text", "The same figures as plain text, for a channel that takes no pictures.", CopyShareText);
        body.Children.Add(acts);

        // where Save picture put it, with a way to it
        if (_shareSaved.Length > 0)
        {
            var saved = new TextBlock { FontSize = 12, Margin = new Thickness(0, 10, 0, 0), Foreground = Ck(CkMuted), TextTrimming = TextTrimming.CharacterEllipsis };
            saved.Inlines.Add(new Run(@"Saved in Pictures\Kinah Ledger as " + System.IO.Path.GetFileName(_shareSaved) + "   "));
            var show = new Hyperlink(new Run("Show in folder")) { Foreground = Ck(CkGold), TextDecorations = null };
            var path = _shareSaved;
            show.Click += (_, _) => { try { System.Diagnostics.Process.Start("explorer.exe", $"/select,\"{path}\""); } catch (Exception) { } };
            saved.Inlines.Add(show);
            saved.ToolTip = _shareSaved;
            body.Children.Add(saved);
        }
        var note = CkText(SharePicture.Note, CkFaint, 12); note.Margin = new Thickness(0, 10, 0, 0);
        body.Children.Add(note);
        return sheet;
    }

    /// <summary>The preview: the picture at half size with its corners rounded, or a line saying what this side is waiting for.</summary>
    FrameworkElement SharePreview(SharePic? p)
    {
        if (p == null)
        {
            var why = _shareScope switch
            {
                SharePicture.Run => "No run yet. Stop a run, or finish an expedition or event, and it shows here.",
                SharePicture.Week => "Nothing farmed in the last 7 days yet.",
                _ => "Nothing counted yet. Kill or gather something and it shows here.",
            };
            var t = CkText(why, CkFaint, 12.5); t.HorizontalAlignment = HorizontalAlignment.Center; t.TextWrapping = TextWrapping.Wrap; t.TextTrimming = TextTrimming.None; t.TextAlignment = TextAlignment.Center;
            return new Border { Width = PicW, Height = 120, CornerRadius = new CornerRadius(10), BorderThickness = new Thickness(1), BorderBrush = Ck(CkSlotLine), Background = Ck(CkSlot, 0x99), Child = t, Padding = new Thickness(24, 0, 24, 0) };
        }
        var v = SharePictureVisual(p);
        v.Clip = new RectangleGeometry(new Rect(0, 0, PicW, PicH), 10, 10);
        return v;
    }

    /// <summary>
    /// The picture, at half its pixel size (600×315; the PNG draws it at twice that): the ShareA card. Fixed colours, the
    /// app's own fonts and item icons, so it looks the same whatever the window's look.
    /// </summary>
    FrameworkElement SharePictureVisual(SharePic p)
    {
        var display = TryFindResource("DisplayFont") as FontFamily ?? new FontFamily("Segoe UI Variable Display, Segoe UI");
        var bodyFont = TryFindResource("BodyFont") as FontFamily ?? new FontFamily("Segoe UI Variable Text, Segoe UI");
        TextBlock T(string text, double size, Color c, FontWeight? w = null, FontFamily? f = null) =>
            new() { Text = text, FontSize = size, Foreground = Ck(c), FontWeight = w ?? FontWeights.Normal, FontFamily = f ?? bodyFont, TextTrimming = TextTrimming.CharacterEllipsis };

        var bg = new LinearGradientBrush { StartPoint = new Point(0.33, 0), EndPoint = new Point(0.67, 1) };
        bg.GradientStops.Add(new GradientStop(Color.FromRgb(0x1A, 0x20, 0x46), 0));
        bg.GradientStops.Add(new GradientStop(Color.FromRgb(0x0B, 0x0E, 0x1E), 0.6));
        bg.GradientStops.Add(new GradientStop(Color.FromRgb(0x12, 0x0F, 0x1C), 1));
        var page = new Grid();
        for (int i = 0; i < 6; i++) page.RowDefinitions.Add(new RowDefinition { Height = i == 4 ? new GridLength(1, GridUnitType.Star) : GridLength.Auto });
        var card = new Border
        {
            Width = PicW, Height = PicH, Background = bg, BorderThickness = new Thickness(1), BorderBrush = Ck(Color.FromRgb(0x3C, 0x44, 0x66)),
            CornerRadius = new CornerRadius(10), Padding = new Thickness(26, 22, 26, 20), Child = page, ClipToBounds = true,
        };
        TextOptions.SetTextFormattingMode(card, TextFormattingMode.Ideal);

        // the mark, Kinah Ledger, and the day and server on the right
        var top = new Grid();
        top.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        top.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        top.ColumnDefinitions.Add(new ColumnDefinition());
        var tileBg = new LinearGradientBrush(Color.FromRgb(0x1E, 0x25, 0x50), Color.FromRgb(0x0E, 0x12, 0x30), new Point(0.33, 0), new Point(0.67, 1));
        var tile = new Border { Width = 28, Height = 28, CornerRadius = new CornerRadius(7), BorderThickness = new Thickness(1), BorderBrush = Ck(Color.FromRgb(0x2E, 0x36, 0x66)), Background = tileBg };
        tile.Child = TryFindResource("AppMark") is ImageSource mark
            ? new Image { Source = mark, Width = 20, Height = 20, Stretch = Stretch.Uniform }
            : new Path { Width = 16, Height = 16, Stretch = Stretch.Uniform, Fill = Ck(CkGoldRule), Data = Geometry.Parse("M6,3 H9.4 V10.2 L15.6,3 H19.7 L13.1,10.6 L20,21 H15.8 L10.8,13.2 L9.4,14.8 V21 H6 Z") };
        top.Children.Add(tile);
        var brand = T("Kinah Ledger", 13, CkInk, FontWeights.Bold, display); brand.VerticalAlignment = VerticalAlignment.Center; brand.Margin = new Thickness(10, 0, 0, 0);
        Grid.SetColumn(brand, 1); top.Children.Add(brand);
        var head = T(p.Head, 12, CkFaint); head.HorizontalAlignment = HorizontalAlignment.Right; head.VerticalAlignment = VerticalAlignment.Center; head.Margin = new Thickness(16, 0, 0, 0);
        head.Tag = "share-head";
        Grid.SetColumn(head, 2); top.Children.Add(head);
        page.Children.Add(top);

        var title = T(p.Title, 22, CkInk, FontWeights.SemiBold, display); title.Margin = new Thickness(0, 16, 0, 2);
        Grid.SetRow(title, 1); page.Children.Add(title);
        var sub = T(p.Sub, 12.5, CkMuted);
        Grid.SetRow(sub, 2); page.Children.Add(sub);

        // Kinah an hour, and what it made after the Market's cut
        var figs = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 16, 0, 0) };
        foreach (var (v, l) in new[] { (p.Rate, p.RateLabel), (p.Made, p.MadeLabel) })
        {
            var cell = new StackPanel { Margin = new Thickness(0, 0, 30, 0) };
            var big = T(v, 34, CkGold, FontWeights.Light, display); big.LineHeight = 34; big.LineStackingStrategy = LineStackingStrategy.BlockLineHeight;
            cell.Children.Add(big);
            var small = T(l, 12, CkMuted); small.Margin = new Thickness(0, 5, 0, 0);
            cell.Children.Add(small);
            figs.Children.Add(cell);
        }
        Grid.SetRow(figs, 3); page.Children.Add(figs);

        // the top five drops and the best one by name (the week: its best day and zone), the site on the right
        var foot = new Grid();
        foot.ColumnDefinitions.Add(new ColumnDefinition());
        foot.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var drops = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Bottom };
        foreach (var d in p.Icons)
        {
            var id = _items.IdOf(d.Name);
            var ic = CkIcon(d.Name, 34, FrameOf(id));
            ic.Margin = new Thickness(0, 0, 8, 0);
            drops.Children.Add(ic);
        }
        if (p.Best.Length > 0)
        {
            var best = T(p.Best, 12, CkFaint); best.VerticalAlignment = VerticalAlignment.Center; best.Margin = new Thickness(p.Icons.Count > 0 ? 6 : 0, 0, 12, 0);
            best.Tag = "share-best";
            drops.Children.Add(best);
        }
        foot.Children.Add(drops);
        var site = T(p.Site, 12, CkGoldRule, FontWeights.SemiBold); site.VerticalAlignment = VerticalAlignment.Bottom;
        Grid.SetColumn(site, 1); foot.Children.Add(site);
        Grid.SetRow(foot, 5); page.Children.Add(foot);
        return card;
    }

    /// <summary>The picture as a 1200×630 PNG-ready bitmap: the same visual as the preview, drawn at twice its size.</summary>
    BitmapSource RenderSharePicture(SharePic p)
    {
        var v = SharePictureVisual(p);
        // square corners on the file: Discord and the rest round a picture themselves
        if (v is Border b) b.CornerRadius = new CornerRadius(0);
        v.Measure(new Size(PicW, PicH));
        v.Arrange(new Rect(0, 0, PicW, PicH));
        v.UpdateLayout();
        var rtb = new RenderTargetBitmap(SharePicture.Width, SharePicture.Height, 192, 192, PixelFormats.Pbgra32);
        rtb.Render(v);
        rtb.Freeze();
        return rtb;
    }

    /* ---------------- the three buttons ---------------- */

    void CopySharePicture()
    {
        if (CurrentSharePic() is not { } p) return;
        try
        {
            var bmp = RenderSharePicture(p);
            // the picture as a PNG (Discord keeps it sharp) and as a plain bitmap for everything else
            var data = new DataObject();
            using (var ms = new MemoryStream()) { var enc = new PngBitmapEncoder(); enc.Frames.Add(BitmapFrame.Create(bmp)); enc.Save(ms); data.SetData("PNG", new MemoryStream(ms.ToArray())); }
            data.SetImage(bmp);
            Clipboard.SetDataObject(data, true);
            Say("Picture copied. Paste it in Discord or anywhere with Ctrl+V.", "Good");
        }
        catch (Exception ex) { CrashLog.Write("share picture", ex); Say("Couldn't copy the picture: " + ex.Message, "Bad"); }
    }

    void SaveSharePicture()
    {
        if (CurrentSharePic() is not { } p) return;
        try
        {
            var dir = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "Kinah Ledger");
            Directory.CreateDirectory(dir);
            var path = System.IO.Path.Combine(dir, SharePicture.FileName(_shareScope, DateTime.Now));
            SavePng(RenderSharePicture(p), path);
            _shareSaved = path;
            DrawShare();
            Say(@"Picture saved in Pictures\Kinah Ledger.", "Good");
        }
        catch (Exception ex) { CrashLog.Write("share save", ex); Say("Couldn't save the picture: " + ex.Message, "Bad"); }
    }

    void CopyShareText()
    {
        if (CurrentSharePic() is not { } p) return;
        try { Clipboard.SetText(SharePicture.Text(p)); Say("Copied as text. Paste it in Discord or anywhere.", "Good"); }
        catch (Exception ex) { CrashLog.Write("share text", ex); Say("Couldn't copy the text: " + ex.Message, "Bad"); }
    }

    /* ---------------- layout renders and options-test ---------------- */

    /// <summary>
    /// A layout render (KL_SHOT_STATE=share-session|share-run|share-week|share-picture): the sheet open on that side, with a
    /// sample farming run stopped for Last run; on the Events page, its last sample run. Example figures, never the player's.
    /// </summary>
    internal void OpenShareSample(string state, bool events = false)
    {
        if (!events && _demoEngine != null)
            _shareFarmRun = (FarmShareFacts(_demoEngine, SharePicture.Run, _demoElapsed.TotalSeconds, BestRuns.Value(_demoEngine), DateTime.Now), DateTime.UtcNow);
        _shareOn = null;
        ToggleShare(events ? "Events" : "Farming");
        _shareScope = state switch { "share-run" => SharePicture.Run, "share-week" => SharePicture.Week, _ => events ? SharePicture.Run : SharePicture.Session };
        DrawShare();
    }

    /// <summary>Layout renders: the 1200×630 pictures themselves (each side, and the session with a sample name shown).</summary>
    internal void ShotSharePictures(string dir)
    {
        foreach (var scope in new[] { SharePicture.Session, SharePicture.Run, SharePicture.Week })
            if (ShareFactsFor(scope) is { } f) SavePng(RenderSharePicture(SharePicture.Compose(f, false, null, Fk)), System.IO.Path.Combine(dir, $"ui-share-picture-{scope}.png"));
        if (ShareFactsFor(SharePicture.Session) is { } s) SavePng(RenderSharePicture(SharePicture.Compose(s, true, "Aerin", Fk)), System.IO.Path.Combine(dir, "ui-share-picture-named.png"));
    }

    /// <summary>For options-test: the sheet as drawn: open, where (inside which card), its side, the head line and the buttons' names.</summary>
    internal (bool Open, string Host, string Scope, string Head, List<string> Buttons, bool Switch) ShareForTest()
    {
        var host = ShareHost.Visibility == Visibility.Visible ? ShareHost : EventsShareHost.Visibility == Visibility.Visible ? EventsShareHost : null;
        if (host?.Child is not Border sheet) return (false, "", _shareScope, "", new(), false);
        var all = WeekWalk(sheet).ToList();
        var head = all.OfType<TextBlock>().FirstOrDefault(t => t.Tag as string == "share-head")?.Text ?? "";
        var buttons = all.OfType<Button>().Select(b => AutomationProperties.GetName(b) is { Length: > 0 } n ? n : b.Content as string ?? "").Where(n => n.Length > 0).ToList();
        bool sw = all.OfType<CheckBox>().FirstOrDefault()?.IsChecked == true;
        return (true, host.Name, _shareScope, head, buttons, sw);
    }

    internal void ToggleShareForTest(string page) => ToggleShare(page);
    internal void PickShareScopeForTest(string scope) { _shareScope = scope; DrawShare(); }
    internal (bool Show, string Name) SharePrefsForTest { get => (_prefs.ShareShowName, _prefs.ShareName); set { _prefs.ShareShowName = value.Show; _prefs.ShareName = value.Name; DrawShare(); } }
    internal SharePic? SharePicForTest(string scope) => ShareFactsFor(scope) is { } f ? SharePicture.Compose(f, _prefs.ShareShowName, _prefs.ShareName, Fk) : null;
}
